using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeezanPOS.Application.Services;

/// <summary>معايير عرض المصروفات العامة في الشاشة.</summary>
public sealed record GeneralExpenseQuery(
    bool PostedArchive,
    DateTime From,
    DateTime To,
    GeneralExpenseType? Type,
    PaymentMethodType? Method);

/// <summary>بيانات حفظ مصروف عام (إضافة أو تعديل).</summary>
public sealed class GeneralExpenseSaveRequest
{
    public int? EditingId { get; init; }
    public GeneralExpenseType ExpenseType { get; init; }
    /// <summary>اسم النوع للعرض في بيانات الحركات (اليدوي أو المترجم).</summary>
    public string TypeDisplayName { get; init; } = string.Empty;
    public string? CustomExpenseName { get; init; }
    public decimal Amount { get; init; }
    public DateTime PaymentDate { get; init; }
    public PaymentMethodType PaymentMethod { get; init; }
    public string? Description { get; init; }
    public string? WorkerName { get; init; }
    public int? WorkerId { get; init; }
    public int? BankAccountId { get; init; }
    public string? BankReferenceNumber { get; init; }
    public string? PartnerName { get; init; }
    /// <summary>حركات العمال التفصيلية (بدون معرف المصروف؛ يُربط بعد الحفظ).</summary>
    public List<WorkerTransaction> WorkerTransactions { get; init; } = new();
}

public sealed record OperationResult(bool Success, string? Error, string? ErrorTitle = null)
{
    public static OperationResult Ok { get; } = new(true, null);
    public static OperationResult Fail(string error, string? title = null) => new(false, error, title);
}

/// <summary>معلومات وسيلة الدفع لعرض تفاصيل مصروف.</summary>
public sealed record ExpensePaymentInfo(string? BankDisplayName, string? ReferenceNumber, string? PartnerName);

public interface IGeneralExpenseService
{
    /// <summary>متزامن عمداً: تحميل الشاشة يُستدعى من أوامر متزامنة على خيط الواجهة.</summary>
    List<GeneralExpense> Query(GeneralExpenseQuery query);
    List<GeneralExpense> GetPostedInRange(DateTime from, DateTime to);
    Task<List<WorkerTransaction>> GetWorkerTransactionsAsync(int expenseId);
    Task<ExpensePaymentInfo> GetPaymentInfoAsync(int expenseId, bool isSupplierPayment);
    Task<bool> IsInSettledSessionAsync(int expenseId);
    Task<List<int>> GetDraftIdsInRangeAsync(DateTime from, DateTime to);
    Task<OperationResult> SaveAsync(GeneralExpenseSaveRequest request);
    Task<OperationResult> DeleteAsync(int expenseId);
}

/// <summary>
/// قراءة وحفظ وحذف المصروفات العامة. كان هذا الكود داخل GeneralExpensesViewModel.
/// الحفظ والحذف في نطاق خدمات ومعاملة واحدة: المصروف وحركته البنكية/النقدية/دين الشريك وحركات العمال معاً.
/// </summary>
public sealed class GeneralExpenseService : IGeneralExpenseService
{
    private const string Source = SourceTypes.GeneralExpense;

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IBankService? _bank;
    private readonly IOwnerDebtService? _ownerDebt;
    private readonly ICashLedgerService? _cash;

    /// <param name="scopeFactory">ينشئ نطاقاً لكل عملية كتابة. بدونه (الاختبارات) تُستخدم الخدمات الممررة.</param>
    public GeneralExpenseService(
        IDbContextFactory<AppDbContext>? factory = null,
        IServiceScopeFactory? scopeFactory = null,
        IBankService? bank = null,
        IOwnerDebtService? ownerDebt = null,
        ICashLedgerService? cash = null)
    {
        _factory = factory ?? DefaultDbContextFactory.Instance;
        _scopeFactory = scopeFactory;
        _bank = bank;
        _ownerDebt = ownerDebt;
        _cash = cash;
    }

    public List<GeneralExpense> Query(GeneralExpenseQuery q)
    {
        using var db = _factory.CreateDbContext();
        var query = db.GeneralExpenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.PaymentDate.Date >= q.From.Date && e.PaymentDate.Date <= q.To.Date);
        query = q.PostedArchive
            ? query.Where(e => e.FinancialStatus == FinancialStatus.Posted || e.FinancialStatus == FinancialStatus.Archived)
            : query.Where(e => e.FinancialStatus != FinancialStatus.Posted && e.FinancialStatus != FinancialStatus.Archived);
        if (q.Type.HasValue) query = query.Where(e => e.ExpenseType == q.Type.Value);
        if (q.Method.HasValue) query = query.Where(e => e.PaymentMethod == q.Method.Value);
        return query.OrderByDescending(e => e.PaymentDate).ToList();
    }

    public List<GeneralExpense> GetPostedInRange(DateTime from, DateTime to)
        => Query(new GeneralExpenseQuery(true, from, to, null, null)).OrderBy(e => e.PaymentDate).ToList();

    public async Task<List<WorkerTransaction>> GetWorkerTransactionsAsync(int expenseId)
    {
        await using var db = _factory.CreateDbContext();
        return await db.WorkerTransactions.AsNoTracking().Where(t => t.GeneralExpenseId == expenseId && !t.IsDeleted).ToListAsync();
    }

    public async Task<ExpensePaymentInfo> GetPaymentInfoAsync(int expenseId, bool isSupplierPayment)
    {
        await using var db = _factory.CreateDbContext();
        var bankTx = await db.BankTransactions.AsNoTracking().Include(t => t.BankAccount)
            .FirstOrDefaultAsync(t => t.SourceType == Source && t.SourceId == expenseId && !t.IsDeleted);

        // سداد مورد بتحويل: الحركة البنكية مرتبطة بحركة دفتر المورد لا بالمصروف
        if (bankTx == null && isSupplierPayment)
        {
            var supplierTx = await db.SupplierTransactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.SourceType == TransactionSourceType.ExternalPayment && t.SourceId == expenseId && !t.IsDeleted);
            if (supplierTx != null)
            {
                bankTx = await db.BankTransactions.AsNoTracking().Include(t => t.BankAccount)
                    .FirstOrDefaultAsync(t => t.SourceType == SourceTypes.SupplierTransaction && t.SourceId == supplierTx.Id && !t.IsDeleted);
            }
        }

        var debt = await db.OwnerDebts.AsNoTracking()
            .FirstOrDefaultAsync(d => d.SourceType == Source && d.SourceId == expenseId && !d.IsDeleted);

        string? bankName = bankTx?.BankAccount != null ? $"{bankTx.BankAccount.BankName} - {bankTx.BankAccount.FriendlyName}" : null;
        return new ExpensePaymentInfo(bankName, bankTx?.ReferenceNumber, debt?.PartnerName);
    }

    public async Task<bool> IsInSettledSessionAsync(int expenseId)
    {
        await using var db = _factory.CreateDbContext();
        var sessionId = await db.GeneralExpenses.Where(e => e.Id == expenseId).Select(e => e.PostingSessionId).FirstOrDefaultAsync();
        if (!sessionId.HasValue) return false;
        var status = await db.PostingSessions.Where(s => s.Id == sessionId.Value).Select(s => (PostingSessionStatus?)s.Status).FirstOrDefaultAsync();
        return status == PostingSessionStatus.Settled || status == PostingSessionStatus.ReSettled;
    }

    public async Task<List<int>> GetDraftIdsInRangeAsync(DateTime from, DateTime to)
    {
        await using var db = _factory.CreateDbContext();
        return await db.GeneralExpenses
            .Where(e => e.PaymentDate.Date >= from && e.PaymentDate.Date <= to && e.FinancialStatus == FinancialStatus.Draft)
            .Select(e => e.Id)
            .ToListAsync();
    }

    private sealed record WriteScope(IServiceScope? Scope, AppDbContext Context, IBankService Bank, IOwnerDebtService OwnerDebt, ICashLedgerService Cash) : IDisposable
    {
        public void Dispose()
        {
            if (Scope != null) Scope.Dispose();
            else Context.Dispose();
        }
    }

    private WriteScope OpenWriteScope()
    {
        if (_scopeFactory != null)
        {
            var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            return new WriteScope(scope, sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<IBankService>(),
                sp.GetRequiredService<IOwnerDebtService>(), sp.GetRequiredService<ICashLedgerService>());
        }
        return new WriteScope(null, _factory.CreateDbContext(),
            _bank ?? throw new InvalidOperationException("خدمة البنوك غير متوفرة."),
            _ownerDebt ?? throw new InvalidOperationException("خدمة ديون الشركاء غير متوفرة."),
            _cash ?? throw new InvalidOperationException("خدمة الخزينة غير متوفرة."));
    }

    /// <summary>إزالة كل ما سجّله المصروف خارج جدوله: الحركة البنكية، دين الشريك، الحركة النقدية.</summary>
    private static async Task RemoveLinkedMovementsAsync(WriteScope w, int expenseId, string reason)
    {
        await w.Bank.DeleteTransactionBySourceAsync(Source, expenseId);

        var debts = await w.Context.OwnerDebts.Where(d => d.SourceType == Source && d.SourceId == expenseId && !d.IsDeleted).ToListAsync();
        foreach (var debt in debts)
            await w.OwnerDebt.DeleteDebtAsync(debt.Id);

        var cashMovement = await w.Context.CashMovements.FindLiveForSourceAsync(Source, expenseId);
        if (cashMovement != null)
            await w.Cash.ReverseMovementAsync(cashMovement.Id, reason);
    }

    public async Task<OperationResult> SaveAsync(GeneralExpenseSaveRequest r)
    {
        using var w = OpenWriteScope();
        var db = w.Context;
        await using var tx = await db.Database.BeginOrJoinTransactionAsync();

        if (await PeriodLock.IsDateLockedAsync(db, r.PaymentDate))
            return OperationResult.Fail(PeriodLock.LockedMessage, "فترة مقفلة");

        GeneralExpense expense;
        if (r.EditingId.HasValue)
        {
            var existing = await db.GeneralExpenses.FindAsync(r.EditingId.Value);
            if (existing == null)
                return OperationResult.Fail("المصروف غير موجود.");
            if (existing.ExpenseType == GeneralExpenseType.SupplierPayment)
                return OperationResult.Fail(SupplierPaymentManagedElsewhere, "منع التعديل");
            if (await PeriodLock.IsDateLockedAsync(db, existing.PaymentDate))
                return OperationResult.Fail(PeriodLock.LockedMessage, "فترة مقفلة");
            if (existing.FinancialStatus == FinancialStatus.Posted || existing.FinancialStatus == FinancialStatus.Archived)
                return OperationResult.Fail("لا يمكن تعديل مصروف مرحّل مالياً.", "منع التعديل");

            await RemoveLinkedMovementsAsync(w, existing.Id, "تعديل المصروف العام");

            // حذف حركات العمال القديمة المرتبطة بهذا المصروف
            var oldWorkerTxs = await db.WorkerTransactions.Where(t => t.GeneralExpenseId == existing.Id && !t.IsDeleted).ToListAsync();
            foreach (var old in oldWorkerTxs)
            {
                old.IsDeleted = true;
                old.UpdatedAt = DateTime.UtcNow;
            }

            expense = existing;
            expense.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            expense = new GeneralExpense();
            db.GeneralExpenses.Add(expense);
        }

        expense.ExpenseType = r.ExpenseType;
        expense.CustomExpenseName = r.CustomExpenseName;
        expense.Amount = r.Amount;
        expense.PaymentDate = r.PaymentDate;
        expense.PaymentMethod = r.PaymentMethod;
        expense.Description = r.Description ?? string.Empty;
        expense.WorkerName = r.WorkerName;
        expense.WorkerId = r.WorkerId;
        expense.BankAccountId = r.PaymentMethod == PaymentMethodType.BankTransfer ? r.BankAccountId : null;
        await db.SaveChangesAsync();

        if (r.WorkerTransactions.Count > 0)
        {
            foreach (var wt in r.WorkerTransactions)
            {
                wt.GeneralExpenseId = expense.Id;
                db.WorkerTransactions.Add(wt);
            }
            await db.SaveChangesAsync();
        }

        var descriptionSuffix = string.IsNullOrEmpty(r.Description) ? "" : $" | {r.Description}";
        if (r.PaymentMethod == PaymentMethodType.BankTransfer && r.BankAccountId.HasValue)
        {
            await w.Bank.RecordTransactionAsync(r.BankAccountId.Value, BankTransactionType.ExpensePayment, r.Amount,
                r.BankReferenceNumber, $"مصروف عام: {r.TypeDisplayName}{descriptionSuffix}", Source, expense.Id, r.PaymentDate);
        }
        else if (r.PaymentMethod == PaymentMethodType.PersonalPartner && !string.IsNullOrWhiteSpace(r.PartnerName))
        {
            await w.OwnerDebt.RecordDebtAsync(r.PartnerName.Trim(), r.Amount, r.TypeDisplayName,
                $"مصروف عام شخصي: {r.TypeDisplayName}{descriptionSuffix}", r.PaymentDate, Source, expense.Id);
        }
        else if (r.PaymentMethod == PaymentMethodType.Cash)
        {
            await w.Cash.RecordMovementAsync(CashMovementType.CashOut, r.Amount, Source, expense.Id,
                $"مصروف عام: {r.TypeDisplayName}{descriptionSuffix}", r.PaymentDate);
        }

        await tx.CommitAsync();
        return OperationResult.Ok;
    }

    private const string SupplierPaymentManagedElsewhere =
        "مصروف تسديد المورد مرتبط بدفعة في كشف حساب المورد، ويُعدَّل أو يُحذف من شاشة كشف المورد فقط.";

    public async Task<OperationResult> DeleteAsync(int expenseId)
    {
        using var w = OpenWriteScope();
        var db = w.Context;
        await using var tx = await db.Database.BeginOrJoinTransactionAsync();

        var existing = await db.GeneralExpenses.FindAsync(expenseId);
        if (existing == null) return OperationResult.Ok;
        // حذفه من هنا كان يزيل المصروف وحركته النقدية ويترك دفعة المورد قائمة في كشفه
        if (existing.ExpenseType == GeneralExpenseType.SupplierPayment)
            return OperationResult.Fail(SupplierPaymentManagedElsewhere, "منع الحذف");
        if (existing.FinancialStatus == FinancialStatus.Posted || existing.FinancialStatus == FinancialStatus.Archived)
            return OperationResult.Fail("لا يمكن حذف مصروف مرحّل مالياً.", "منع الحذف");
        if (await PeriodLock.IsDateLockedAsync(db, existing.PaymentDate))
            return OperationResult.Fail(PeriodLock.LockedMessage, "فترة مقفلة");

        await RemoveLinkedMovementsAsync(w, existing.Id, "حذف المصروف العام");

        existing.IsDeleted = true;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return OperationResult.Ok;
    }
}
