using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeezanPOS.Application.Services;

/// <summary>بيانات حفظ يومية جاهزة (بنودها مبنية من شاشة الإدخال).</summary>
public sealed class JournalSaveRequest
{
    public int? EditingJournalId { get; init; }
    /// <summary>رقم نسخة اليومية عند فتحها للتعديل: يُرفض الحفظ إن عُدّلت بعده من مكان آخر.</summary>
    public long? ExpectedRowVersion { get; init; }
    public DateTime JournalDate { get; init; }
    public ShiftType Shift { get; init; }
    public string ShiftDisplayName { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public decimal CashFloat { get; init; }
    public decimal TotalSales { get; init; }
    public decimal BankingTotal { get; init; }
    public decimal TotalExpenses { get; init; }
    public decimal ReturnsTotal { get; init; }
    public decimal FreeOrdersTotal { get; init; }
    public decimal ActualCash { get; init; }

    public List<DailyExpenseItem> ExpenseItems { get; init; } = new();
    public List<BankingItem> BankingItems { get; init; } = new();
    public List<DailyJournalBankSale> BankSales { get; init; } = new();
    public List<OrderAdjustmentItem> Adjustments { get; init; } = new();

    /// <summary>حركات العمال التفصيلية لكل بند أجور، بمفتاح رقم تسلسل البند (يُربط بمعرف البند بعد حفظه).</summary>
    public Dictionary<int, List<WorkerTransaction>> WorkerTransactionsBySequence { get; init; } = new();
}

public sealed record JournalSaveResult(bool Success, string? Error, int? JournalId)
{
    public static JournalSaveResult Fail(string error) => new(false, error, null);
}

public interface IDailyJournalService
{
    Task<List<string>> GetUsedExpenseCategoriesAsync();
    Task<List<ShiftType>> GetRegisteredShiftsAsync(DateTime date, CancellationToken cancellationToken = default);
    Task<DailyJournal?> GetJournalWithDetailsAsync(int journalId);
    /// <summary>متزامن عمداً: يُستدعى من تحميل الشاشة على خيط الواجهة (الانتظار المتزامن لنسخة async قد يجمّدها).</summary>
    Dictionary<int, List<WorkerTransaction>> GetWorkerTransactionsByExpenseItem(IReadOnlyCollection<int> expenseItemIds);
    Task<JournalSaveResult> SaveAsync(JournalSaveRequest request);
}

/// <summary>
/// قراءة وحفظ اليوميات. كان هذا الكود داخل DailyJournalViewModel ويفتح قاعدة البيانات مباشرة.
/// الحفظ عملية واحدة: السياق والخدمات المالية في نطاق واحد ومعاملة واحدة.
/// </summary>
public sealed class DailyJournalService : IDailyJournalService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IBankService? _fallbackBank;
    private readonly ILedgerService? _fallbackLedger;

    /// <param name="scopeFactory">ينشئ نطاقاً لكل حفظ. بدونه (الاختبارات) يُستخدم سياق جديد مع الخدمات الممررة.</param>
    public DailyJournalService(
        IDbContextFactory<AppDbContext>? factory = null,
        IServiceScopeFactory? scopeFactory = null,
        IBankService? fallbackBank = null,
        ILedgerService? fallbackLedger = null)
    {
        _factory = factory ?? DefaultDbContextFactory.Instance;
        _scopeFactory = scopeFactory;
        _fallbackBank = fallbackBank;
        _fallbackLedger = fallbackLedger;
    }

    public async Task<List<string>> GetUsedExpenseCategoriesAsync()
    {
        await using var db = _factory.CreateDbContext();
        return (await db.DailyExpenseItems
                .Where(e => !string.IsNullOrEmpty(e.Category))
                .Select(e => e.Category)
                .Distinct()
                .ToListAsync())
            .Where(c => c != null)
            .Select(c => c!)
            .ToList();
    }

    public async Task<List<ShiftType>> GetRegisteredShiftsAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        await using var db = _factory.CreateDbContext();
        var targetDate = date.Date;
        return await db.DailyJournals
            .Where(j => j.JournalDate.Year == targetDate.Year && j.JournalDate.Month == targetDate.Month && j.JournalDate.Day == targetDate.Day)
            .Select(j => j.ShiftType)
            .ToListAsync(cancellationToken);
    }

    public async Task<DailyJournal?> GetJournalWithDetailsAsync(int journalId)
    {
        await using var db = _factory.CreateDbContext();
        return await db.DailyJournals
            .Include(j => j.ExpenseItems)
            .Include(j => j.BankingItems)
            .Include(j => j.Adjustments)
            .Include(j => j.BankSales)
            .FirstOrDefaultAsync(j => j.Id == journalId);
    }

    public Dictionary<int, List<WorkerTransaction>> GetWorkerTransactionsByExpenseItem(IReadOnlyCollection<int> expenseItemIds)
    {
        if (expenseItemIds.Count == 0) return new Dictionary<int, List<WorkerTransaction>>();
        using var db = _factory.CreateDbContext();
        var ids = expenseItemIds.ToList();
        return db.WorkerTransactions
                .Where(t => t.DailyExpenseItemId != null && ids.Contains(t.DailyExpenseItemId.Value) && !t.IsDeleted)
                .ToList()
            .GroupBy(t => t.DailyExpenseItemId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task<JournalSaveResult> SaveAsync(JournalSaveRequest r)
    {
        // نطاق واحد: السياق والخدمات تتشارك AppDbContext واحداً، فتدخل كل الكتابات في معاملة واحدة.
        // أي فشل في المنتصف (حركة بنكية، دفتر مورد...) يتراجع عن الحفظ كله بدلاً من ترك يومية نصف محفوظة.
        using var scope = _scopeFactory?.CreateScope();
        using var ownedContext = scope == null ? _factory.CreateDbContext() : null;
        var context = scope?.ServiceProvider.GetRequiredService<AppDbContext>() ?? ownedContext!;
        var bankService = scope?.ServiceProvider.GetRequiredService<IBankService>() ?? _fallbackBank
            ?? throw new InvalidOperationException("خدمة البنوك غير متوفرة.");
        var ledgerService = scope?.ServiceProvider.GetRequiredService<ILedgerService>() ?? _fallbackLedger
            ?? throw new InvalidOperationException("خدمة دفتر الموردين غير متوفرة.");
        await using var saveTx = await context.Database.BeginOrJoinTransactionAsync();
        var affectedSuppliers = new HashSet<int>();

        // لا تتكرر الوردية نفسها في اليوم نفسه
        var targetDate = r.JournalDate.Date;
        bool isDuplicate = await context.DailyJournals.AnyAsync(j =>
            j.JournalDate.Year == targetDate.Year &&
            j.JournalDate.Month == targetDate.Month &&
            j.JournalDate.Day == targetDate.Day &&
            j.ShiftType == r.Shift &&
            (!r.EditingJournalId.HasValue || j.Id != r.EditingJournalId.Value));
        if (isDuplicate)
            return JournalSaveResult.Fail($"عذراً، تم تسجيل الوردية {r.ShiftDisplayName} مسبقاً في هذا اليوم ولا يمكن تكرارها.");

        if (await PeriodLock.IsDateLockedAsync(context, targetDate))
            return JournalSaveResult.Fail(PeriodLock.LockedMessage);

        // عند إدخال تفاصيل الدفتر تُودَع هي وحدها في المصارف؛ أي فرق عن إجمالي المبيعات المصرفية
        // كان يختفي: اليومية تخصمه من النقد المتوقع ولا يدخل أي حساب مصرفي
        if (r.BankingItems.Count > 0)
        {
            var itemsTotal = r.BankingItems.Sum(b => b.Amount);
            if (itemsTotal != r.BankingTotal)
                return JournalSaveResult.Fail(
                    $"تفاصيل الخدمات المصرفية ({itemsTotal:N2}) لا تساوي إجمالي المبيعات المصرفية ({r.BankingTotal:N2}). " +
                    $"الفرق {Math.Abs(r.BankingTotal - itemsTotal):N2} لن يُسجَّل في أي حساب مصرفي. أكمل تفاصيل الدفتر أو صحّح الإجمالي.");
            if (r.BankingItems.Any(b => !b.BankAccountId.HasValue))
                return JournalSaveResult.Fail("حدد الحساب المصرفي لكل بند في تفاصيل الخدمات المصرفية.");
        }

        DailyJournal? journal;
        if (r.EditingJournalId.HasValue)
        {
            journal = await context.DailyJournals
                .Include(j => j.ExpenseItems)
                .Include(j => j.BankingItems)
                .Include(j => j.Adjustments)
                .FirstOrDefaultAsync(j => j.Id == r.EditingJournalId.Value);

            if (journal == null)
                return JournalSaveResult.Fail("خطأ: لم يتم العثور على السجل لتعديله.");

            // حماية على مستوى الحفظ وليس الواجهة فقط: اليومية المرحلة لا تُعدَّل إلا بعد فك ترحيلها
            if (journal.FinancialStatus != FinancialStatus.Draft)
                return JournalSaveResult.Fail("لا يمكن تعديل يومية مرحّلة. يجب فك ترحيلها أولاً.");

            if (await PeriodLock.IsDateLockedAsync(context, journal.JournalDate))
                return JournalSaveResult.Fail(PeriodLock.LockedMessage);

            // صرف أجر أو تسوية شريك من الدرج يعدّل اليومية من شاشة أخرى؛ الحفظ فوقه كان يمحو أثره
            if (r.ExpectedRowVersion.HasValue && journal.RowVersion != r.ExpectedRowVersion.Value)
                return JournalSaveResult.Fail(
                    "عُدّلت هذه اليومية بعد فتحها (مثل صرف أجر أو تسوية شريك من الدرج). " +
                    "أغلق الشاشة وأعد فتح اليومية ثم أدخل تعديلاتك، حتى لا تُمحى تلك الحركة.");

            journal.RowVersion++;
            ApplyHeader(journal, r);
            journal.UpdatedAt = DateTime.UtcNow;

            // حذف العناصر القديمة صراحة لأن قاعدة البيانات تمنع Cascade Delete
            context.DailyExpenseItems.RemoveRange(journal.ExpenseItems);
            context.BankingItems.RemoveRange(journal.BankingItems);

            var oldBankSales = await context.DailyJournalBankSales.Where(s => s.DailyJournalId == journal.Id).ToListAsync();
            context.DailyJournalBankSales.RemoveRange(oldBankSales);

            // إزالة المطابقات البنكية التابعة لهذه الوردية
            var oldCardRecons = await context.CardPaymentReconciliations.Where(rc => rc.DailyJournalId == journal.Id).ToListAsync();
            context.CardPaymentReconciliations.RemoveRange(oldCardRecons);

            // إزالة الحركات البنكية المباشرة القديمة المرتبطة بهذه الوردية وإعادة بناء أرصدتها
            await bankService.DeleteTransactionBySourceAsync("DailyJournal", journal.Id);

            // الموردون السابقون يُعاد بناء أرصدتهم أيضاً (وإلا يبقى رصيد المورد القديم محسوباً بالدفعة المحذوفة عند تغيير المورد)
            var oldExpenseIds = journal.ExpenseItems.Select(e => e.Id).ToList();
            foreach (var oldSupplierId in journal.ExpenseItems.Where(e => e.SupplierId.HasValue).Select(e => e.SupplierId!.Value))
                affectedSuppliers.Add(oldSupplierId);
            if (oldExpenseIds.Any())
            {
                // إزالة حركات الدفتر المرتبطة بالمصروفات المحذوفة
                var linkedTxs = await context.SupplierTransactions
                    .Where(t => oldExpenseIds.Contains(t.SourceId) && t.SourceType == TransactionSourceType.DailyJournalPayment)
                    .ToListAsync();
                foreach (var linked in linkedTxs)
                    affectedSuppliers.Add(linked.SupplierId);
                context.SupplierTransactions.RemoveRange(linkedTxs);

                // إزالة حركات العمال المرتبطة بالمصروفات المحذوفة
                var oldWorkerTxs = await context.WorkerTransactions
                    .Where(t => t.DailyExpenseItemId != null && oldExpenseIds.Contains(t.DailyExpenseItemId.Value) && !t.IsDeleted)
                    .ToListAsync();
                foreach (var tx in oldWorkerTxs)
                {
                    tx.IsDeleted = true;
                    tx.UpdatedAt = DateTime.UtcNow;
                }
            }

            context.OrderAdjustmentItems.RemoveRange(journal.Adjustments);
            journal.ExpenseItems.Clear();
            journal.BankingItems.Clear();
            journal.Adjustments.Clear();
        }
        else
        {
            journal = new DailyJournal { CreatedAt = DateTime.UtcNow };
            ApplyHeader(journal, r);
            context.DailyJournals.Add(journal);
        }

        foreach (var item in r.ExpenseItems) journal.ExpenseItems.Add(item);
        foreach (var item in r.BankingItems) journal.BankingItems.Add(item);
        foreach (var item in r.BankSales) journal.BankSales.Add(item);
        foreach (var item in r.Adjustments) journal.Adjustments.Add(item);

        await context.SaveChangesAsync();

        // حفظ حركات العمال التفصيلية للوردية (بعد معرفة معرفات البنود)
        bool hasWorkerDetails = false;
        foreach (var (sequence, transactions) in r.WorkerTransactionsBySequence)
        {
            var dei = journal.ExpenseItems.FirstOrDefault(e => e.SequenceNumber == sequence);
            if (dei == null) continue;
            foreach (var tx in transactions)
            {
                tx.DailyExpenseItemId = dei.Id;
                context.WorkerTransactions.Add(tx);
                hasWorkerDetails = true;
            }
        }
        if (hasWorkerDetails)
            await context.SaveChangesAsync();

        // تسجيل الدفعات المصرفية مباشرة في الحسابات البنكية المحددة
        if (journal.BankingItems.Any())
        {
            foreach (var bItem in journal.BankingItems.Where(b => b.BankAccountId.HasValue))
            {
                string notes = $"مبيعات إلكترونية - {journal.ShiftName}";
                if (!string.IsNullOrWhiteSpace(bItem.Description))
                    notes += $" - {bItem.Description}";

                await bankService.RecordTransactionAsync(
                    bankAccountId: bItem.BankAccountId!.Value,
                    type: BankTransactionType.CardSalesDeposit,
                    amount: bItem.Amount,
                    referenceNumber: bItem.ReferenceNumber,
                    notes: notes,
                    sourceType: "DailyJournal",
                    sourceId: journal.Id,
                    transactionDate: journal.JournalDate);
            }
        }
        else
        {
            // إذا لم يتم إدخال تفاصيل الدفتر، نسجل حركة إيداع مباشرة لكل مصرف تم إدخال مبيعاته
            foreach (var bSale in journal.BankSales.Where(s => s.BankAccountId.HasValue))
            {
                await bankService.RecordTransactionAsync(
                    bankAccountId: bSale.BankAccountId!.Value,
                    type: BankTransactionType.CardSalesDeposit,
                    amount: bSale.Amount,
                    referenceNumber: null,
                    notes: $"إجمالي مبيعات إلكترونية - {bSale.BankName} - {journal.ShiftName}",
                    sourceType: "DailyJournal",
                    sourceId: journal.Id,
                    transactionDate: journal.JournalDate);
            }
        }

        // ترحيل المصروفات المرتبطة بالموردين للدفتر المالي
        foreach (var newExp in journal.ExpenseItems.Where(e => e.SupplierId.HasValue))
        {
            affectedSuppliers.Add(newExp.SupplierId!.Value);
            if (newExp.Type == ExpenseType.SupplierPayment || newExp.Type == ExpenseType.Purchase || newExp.Type == ExpenseType.InvoicePayment)
            {
                // تاريخ اليومية لا لحظة الحفظ، ليظهر في يومه الصحيح بكشف المورد
                await ledgerService.PostPaymentAsync(
                    newExp.SupplierId.Value,
                    newExp.Amount,
                    TransactionSourceType.DailyJournalPayment,
                    newExp.Id,
                    journal.JournalDate.Date);
            }
        }

        // إعادة بناء أرصدة الموردين المتأثرين: الحاليون والسابقون قبل التعديل
        foreach (var supId in affectedSuppliers)
            await ledgerService.RebuildSupplierLedgerAsync(supId);

        await saveTx.CommitAsync();
        return new JournalSaveResult(true, null, journal.Id);
    }

    private static void ApplyHeader(DailyJournal journal, JournalSaveRequest r)
    {
        journal.JournalDate = r.JournalDate;
        journal.ShiftType = r.Shift;
        journal.EmployeeName = r.EmployeeName;
        journal.Notes = r.Notes;
        journal.CashFloat = r.CashFloat;
        journal.TotalSales = r.TotalSales;
        journal.BankingTotal = r.BankingTotal;
        journal.TotalExpenses = r.TotalExpenses;
        journal.ReturnsTotal = r.ReturnsTotal;
        journal.FreeOrdersTotal = r.FreeOrdersTotal;
        journal.ActualCash = r.ActualCash;
    }
}
