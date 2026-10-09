using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Application.Services;

public class OwnerDebtService : IOwnerDebtService
{
    private readonly AppDbContext _context;
    private readonly IBankService _bankService;
    private readonly ISessionService _session;
    private readonly AuditService _auditService;
    private readonly ICashLedgerService _cashLedgerService;

    public OwnerDebtService(AppDbContext context, ISessionService session, IBankService bankService, AuditService auditService, ICashLedgerService? cashLedgerService = null)
    {
        _context = context;
        _bankService = bankService;
        _session = session;
        _auditService = auditService;
        _cashLedgerService = cashLedgerService ?? new CashLedgerService(context, session);
    }

    /// <summary>
    /// حالة الدين من مجموع تسوياته: "مسدد" فقط عند تغطية المبلغ كاملاً.
    /// </summary>
    private async Task RefreshDebtStatusAsync(OwnerDebt debt)
    {
        var settled = (await _context.OwnerDebtSettlements
            .Where(s => s.OwnerDebtId == debt.Id && !s.IsDeleted)
            .Select(s => s.Amount)
            .ToListAsync()).Sum();
        debt.Status = settled >= debt.Amount ? OwnerDebtStatus.Paid : OwnerDebtStatus.Unpaid;
        debt.UpdatedAt = DateTime.UtcNow;
    }

    public async Task<List<OwnerDebt>> GetDebtsAsync(string? partnerName = null, OwnerDebtStatus? status = null)
    {
        var query = _context.OwnerDebts
            .Where(d => !d.IsDeleted);

        if (!string.IsNullOrEmpty(partnerName))
        {
            query = query.Where(d => d.PartnerName == partnerName);
        }

        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        return await query.OrderByDescending(d => d.TransactionDate)
            .ThenByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<OwnerDebt> RecordDebtAsync(string partnerName, decimal amount, string? expenseCategory, string? notes, DateTime date, string? sourceType = null, int? sourceId = null, string? paymentMethod = null, string? transferReference = null, int? bankAccountId = null)
    {
        if (amount <= 0)
            throw new ArgumentException("مبلغ الدين يجب أن يكون أكبر من صفر.");
        if (string.IsNullOrWhiteSpace(partnerName))
            throw new ArgumentException("يجب تحديد اسم الشريك.");

        using var transaction = await _context.Database.BeginOrJoinTransactionAsync();
        try
        {
            var debt = new OwnerDebt
            {
                PartnerName = partnerName,
                Amount = amount,
                ExpenseCategory = expenseCategory,
                Notes = notes,
                TransactionDate = date,
                Status = OwnerDebtStatus.Unpaid,
                SourceType = sourceType,
                SourceId = sourceId,
                PaymentMethod = paymentMethod,
                TransferReference = transferReference
            };

            _context.OwnerDebts.Add(debt);
            await _context.SaveChangesAsync();

            // إذا كان التمويل عبر تحويل مصرفي، نقوم بزيادة رصيد البنك فورا
            if (paymentMethod == "Transfer" && bankAccountId.HasValue)
            {
                var bankTxNotes = $"تمويل من الشريك: {partnerName}" + (string.IsNullOrEmpty(notes) ? "" : $" - {notes}");
                await _bankService.RecordTransactionAsync(
                    bankAccountId.Value,
                    BankTransactionType.Deposit, // إيداع
                    amount,
                    transferReference,
                    bankTxNotes,
                    "OwnerDebt_Transfer",
                    debt.Id,
                    date
                );
            }
            // إذا كان نقدا وتم تحديده كإيداع في البنك (مثلا شريك أودع نقدا في حساب البنك)
            else if (paymentMethod == "Cash" && bankAccountId.HasValue)
            {
                var bankTxNotes = $"إيداع نقدي من الشريك: {partnerName}" + (string.IsNullOrEmpty(notes) ? "" : $" - {notes}");
                await _bankService.RecordTransactionAsync(
                    bankAccountId.Value,
                    BankTransactionType.Deposit,
                    amount,
                    null,
                    bankTxNotes,
                    "OwnerDebt_Cash",
                    debt.Id,
                    date
                );
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return debt;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteDebtAsync(int debtId)
    {
        var debt = await _context.OwnerDebts.FindAsync(debtId);
        if (debt == null || debt.IsDeleted)
            throw new Exception("الدين غير موجود.");

        var hasSettlements = await _context.OwnerDebtSettlements
            .AnyAsync(s => s.OwnerDebtId == debtId && !s.IsDeleted);

        if (hasSettlements)
            throw new Exception("لا يمكن حذف دين تم تسويته بالفعل. الرجاء حذف التسويات المرتبطة به أولاً.");

        debt.IsDeleted = true;
        debt.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<List<OwnerDebtSettlement>> GetSettlementsAsync(string? partnerName = null)
    {
        var query = _context.OwnerDebtSettlements
            .Include(s => s.BankAccount)
            .Include(s => s.OwnerDebt)
            .Where(s => !s.IsDeleted);

        if (!string.IsNullOrEmpty(partnerName))
        {
            query = query.Where(s => s.PartnerName == partnerName);
        }

        return await query.OrderByDescending(s => s.SettlementDate)
            .ThenByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<OwnerDebtSettlement> RecordSettlementAsync(int? debtId, string partnerName, decimal amount, OwnerDebtSettlementSource source, int? bankAccountId, string? notes, DateTime date)
    {
        if (amount <= 0)
            throw new ArgumentException("مبلغ التسوية يجب أن يكون أكبر من صفر.");

        using var transaction = await _context.Database.BeginOrJoinTransactionAsync();
        try
        {
            OwnerDebt? debt = null;
            if (debtId.HasValue)
            {
                debt = await _context.OwnerDebts.FindAsync(debtId.Value);
                if (debt == null || debt.IsDeleted)
                    throw new Exception("الدين المحدد غير موجود.");

                // الشريك هو صاحب الدين دائماً: كان الاسم يؤخذ من النموذج القابل للتعديل،
                // فيُسدَّد دين شريك ويُسجَّل الدفع على كشف شريك آخر
                partnerName = debt.PartnerName;

                var alreadySettled = (await _context.OwnerDebtSettlements
                    .Where(s => s.OwnerDebtId == debt.Id && !s.IsDeleted)
                    .Select(s => s.Amount)
                    .ToListAsync()).Sum();
                var remaining = debt.Amount - alreadySettled;
                if (amount > remaining)
                    throw new InvalidOperationException($"مبلغ التسوية ({amount:N2}) أكبر من المتبقي على الدين ({remaining:N2}).");
            }

            // الصرف من درج الكاشير يُسجل على يومية ذلك اليوم المسودة (آخر وردية فيه)، فيُنقص نقدها
            // المتوقع ويظهر في اليومية بدل أن يظهر عجزاً غير مفسر في نقدها الفعلي
            DailyJournal? drawerJournal = null;
            if (source == OwnerDebtSettlementSource.CashRegister)
            {
                var day = date.Date;
                var nextDay = day.AddDays(1);
                drawerJournal = await _context.DailyJournals
                    .Where(j => j.FinancialStatus == FinancialStatus.Draft && j.JournalDate >= day && j.JournalDate < nextDay)
                    .OrderByDescending(j => j.ShiftType)
                    .ThenByDescending(j => j.Id)
                    .FirstOrDefaultAsync();

                if (drawerJournal == null)
                    throw new InvalidOperationException(
                        $"لا توجد يومية مسودة بتاريخ {day:yyyy/MM/dd} لتسجيل الصرف من درج الكاشير عليها. " +
                        "أنشئ يومية هذا اليوم أولاً، أو سجّل التسوية من الخزينة أو المصرف.");

                drawerJournal.DrawerPayouts += amount;
                drawerJournal.UpdatedAt = DateTime.UtcNow;
            }

            var settlement = new OwnerDebtSettlement
            {
                OwnerDebtId = debtId,
                PartnerName = partnerName,
                Amount = amount,
                SettlementSource = source,
                BankAccountId = bankAccountId,
                DailyJournalId = drawerJournal?.Id,
                Notes = notes,
                SettlementDate = date
            };

            _context.OwnerDebtSettlements.Add(settlement);
            await _context.SaveChangesAsync();

            // الدين يصبح "مسدداً" فقط إذا غطت التسويات كامل مبلغه (السداد الجزئي يبقيه مفتوحاً)
            if (debt != null)
                await RefreshDebtStatusAsync(debt);

            // التسوية من الخزينة تُخرج نقداً فعلياً من رصيد النقدية.
            // (التسوية من صندوق الكاشير لا تُسجل هنا: سُجلت أعلاه على اليومية، ونقدها يدخل الخزينة عند ترحيلها ناقصاً بقيمتها)
            if (source == OwnerDebtSettlementSource.PettyCash)
            {
                await _cashLedgerService.RecordMovementAsync(
                    CashMovementType.CashOut,
                    amount,
                    SourceTypes.OwnerDebtSettlement,
                    settlement.Id,
                    $"تسوية مستحقات الشريك {partnerName}" + (string.IsNullOrEmpty(notes) ? "" : $" - {notes}"),
                    date);
            }

            // إذا كان التمويل من الحساب البنكي، نقوم بتسجيل حركة بنكية
            if (source == OwnerDebtSettlementSource.Bank)
            {
                if (!bankAccountId.HasValue)
                    throw new Exception("يجب تحديد الحساب البنكي للتسوية المصرفية.");

                var bankTxNotes = $"تسوية مستحقات المالك {partnerName}" + (string.IsNullOrEmpty(notes) ? "" : $" - {notes}");
                await _bankService.RecordTransactionAsync(
                    bankAccountId.Value,
                    BankTransactionType.OwnerDebtSettlement,
                    amount,
                    null,
                    bankTxNotes,
                    "OwnerDebtSettlement",
                    settlement.Id,
                    date
                );
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(_session.CurrentUsername, "RecordSettlement", "OwnerDebtSettlement", settlement.Id, null, $"Settlement for Partner {partnerName} Amount {amount} via {source}");

            await transaction.CommitAsync();

            return settlement;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteSettlementAsync(int settlementId)
    {
        using var transaction = await _context.Database.BeginOrJoinTransactionAsync();
        try
        {
            var settlement = await _context.OwnerDebtSettlements.FindAsync(settlementId);
            if (settlement == null || settlement.IsDeleted)
                throw new Exception("التسوية غير موجودة.");

            // التسوية من درج الكاشير تُرفع من يوميتها، ما دامت اليومية مفتوحة
            if (settlement.DailyJournalId.HasValue)
            {
                var journal = await _context.DailyJournals.FindAsync(settlement.DailyJournalId.Value);
                if (journal != null)
                {
                    if (journal.FinancialStatus != FinancialStatus.Draft)
                        throw new InvalidOperationException(
                            $"لا يمكن حذف هذه التسوية: صُرفت من درج يومية {journal.JournalDate:yyyy/MM/dd} وقد رُحّلت. ألغِ ترحيل اليومية أولاً.");
                    journal.DrawerPayouts = Math.Max(0m, journal.DrawerPayouts - settlement.Amount);
                    journal.UpdatedAt = DateTime.UtcNow;
                }
            }

            // 1. حذف التسوية أولاً ثم إعادة حساب حالة الدين من التسويات المتبقية
            settlement.IsDeleted = true;
            settlement.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            if (settlement.OwnerDebtId.HasValue)
            {
                var debt = await _context.OwnerDebts.FindAsync(settlement.OwnerDebtId.Value);
                if (debt != null && !debt.IsDeleted)
                    await RefreshDebtStatusAsync(debt);
            }

            // عكس الحركة النقدية لتسوية الخزينة
            var liveCash = await _context.CashMovements.FindLiveForSourceAsync(SourceTypes.OwnerDebtSettlement, settlement.Id);
            if (liveCash != null)
                await _cashLedgerService.ReverseMovementAsync(liveCash.Id, "حذف تسوية مستحقات شريك");

            // 2. إذا كانت التسوية من البنك، نقوم بحذف الحركة البنكية المرتبطة بها
            if (settlement.SettlementSource == OwnerDebtSettlementSource.Bank)
            {
                await _bankService.DeleteTransactionBySourceAsync("OwnerDebtSettlement", settlement.Id);
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(_session.CurrentUsername, "DeleteSettlement", "OwnerDebtSettlement", settlement.Id, $"Partner: {settlement.PartnerName}, Amount: {settlement.Amount}", "Deleted");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<decimal> GetTotalOwnerDebtsAsync(string? partnerName = null)
    {
        var query = _context.OwnerDebts.Where(d => !d.IsDeleted);
        if (!string.IsNullOrEmpty(partnerName))
        {
            query = query.Where(d => d.PartnerName == partnerName);
        }
        var amounts = await query.Select(d => d.Amount).ToListAsync();
        return amounts.Sum();
    }

    public async Task<decimal> GetTotalOwnerSettlementsAsync(string? partnerName = null)
    {
        var query = _context.OwnerDebtSettlements.Where(s => !s.IsDeleted);
        if (!string.IsNullOrEmpty(partnerName))
        {
            query = query.Where(s => s.PartnerName == partnerName);
        }
        var amounts = await query.Select(s => s.Amount).ToListAsync();
        return amounts.Sum();
    }

    public async Task<decimal> GetNetOwnerBalanceAsync(string? partnerName = null)
    {
        decimal debts = await GetTotalOwnerDebtsAsync(partnerName);
        decimal settlements = await GetTotalOwnerSettlementsAsync(partnerName);
        return debts - settlements;
    }

    public async Task<List<string>> GetPartnerNamesAsync()
    {
        var debtPartners = await _context.OwnerDebts
            .Where(d => !d.IsDeleted)
            .Select(d => d.PartnerName)
            .Distinct()
            .ToListAsync();

        var settlementPartners = await _context.OwnerDebtSettlements
            .Where(s => !s.IsDeleted)
            .Select(s => s.PartnerName)
            .Distinct()
            .ToListAsync();

        return debtPartners.Union(settlementPartners)
            .Where(p => !string.IsNullOrEmpty(p))
            .OrderBy(p => p)
            .ToList();
    }

    // ────────────────────────────────────────────────────────────────
    // لوحة المتابعة المالية - كروت الشركاء
    // ────────────────────────────────────────────────────────────────
    public async Task<List<PartnerSummaryDto>> GetPartnersSummaryAsync()
    {
        // استعلامان فقط بدلاً من 2N استعلام (حيث N = عدد الشركاء)
        var debtSums = (await _context.OwnerDebts
            .Where(d => !d.IsDeleted)
            .Select(d => new { d.PartnerName, d.Amount })
            .ToListAsync())
            .GroupBy(d => d.PartnerName)
            .Select(g => new { Name = g.Key, Total = g.Sum(d => d.Amount) })
            .ToList();

        var settlementSums = (await _context.OwnerDebtSettlements
            .Where(s => !s.IsDeleted)
            .Select(s => new { s.PartnerName, s.Amount })
            .ToListAsync())
            .GroupBy(s => s.PartnerName)
            .Select(g => new { Name = g.Key, Total = g.Sum(s => s.Amount) })
            .ToList();

        var debtDict = debtSums.ToDictionary(d => d.Name, d => d.Total);
        var settlementDict = settlementSums.ToDictionary(s => s.Name, s => s.Total);

        var allNames = debtDict.Keys
            .Union(settlementDict.Keys)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n);

        var summaries = new List<PartnerSummaryDto>();
        foreach (var name in allNames)
        {
            var debtsTotal = debtDict.GetValueOrDefault(name, 0m);
            var settlementsTotal = settlementDict.GetValueOrDefault(name, 0m);
            var net = debtsTotal - settlementsTotal;

            var direction = net > 0
                ? PartnerBalanceDirection.RestaurantOwesPartner
                : net < 0
                    ? PartnerBalanceDirection.PartnerOwesRestaurant
                    : PartnerBalanceDirection.Settled;

            var directionText = direction switch
            {
                PartnerBalanceDirection.PartnerOwesRestaurant => "الشريك مدين للمطعم",
                PartnerBalanceDirection.RestaurantOwesPartner => "المطعم مدين للشريك",
                _ => "تمت التسوية"
            };

            summaries.Add(new PartnerSummaryDto
            {
                PartnerName = name,
                DebtsTotal = debtsTotal,
                SettlementsTotal = settlementsTotal,
                NetBalance = Math.Abs(net),
                BalanceDirection = direction,
                BalanceDirectionText = directionText
            });
        }

        return summaries;
    }

    // ────────────────────────────────────────────────────────────────
    // كشف حساب الشريك مع الرصيد التراكمي الحي
    // ────────────────────────────────────────────────────────────────
    public async Task<List<PartnerStatementEntryDto>> GetPartnerStatementAsync(string partnerName)
    {
        // 1. جلب كل الديون
        var debts = await _context.OwnerDebts
            .Where(d => !d.IsDeleted && d.PartnerName == partnerName)
            .Select(d => new PartnerStatementEntryDto
            {
                TransactionDate = d.TransactionDate,
                TransactionType = "دين على المطعم",
                Description = GenerateDebtDescription(d.SourceType, d.ExpenseCategory, d.Notes),
                Amount = d.Amount,
                IsSettlement = false,
                SourceType = d.SourceType ?? "OwnerDebt",
                SourceId = (d.SourceType == "GeneralExpense" || d.SourceType == "SupplierTransaction") ? (d.SourceId ?? d.Id) : d.Id,
                ReferenceNumber = !string.IsNullOrEmpty(d.TransferReference) ? d.TransferReference : d.Id.ToString("D5"),
                PaymentMethod = d.PaymentMethod == "Transfer" ? "تحويل مصرفي" :
                                d.PaymentMethod == "Cash" ? "نقدي" :
                                d.PaymentMethod == "PersonalPartner" ? "شخصي (شريك)" :
                                d.PaymentMethod == "GeneralExpense" ? "مصروف عام" : "-",
                TransferReference = d.TransferReference,
                CanDelete = d.SourceType == null // فقط الديون اليدوية يمكن حذفها
            })
            .ToListAsync();

        // 2. جلب كل التسويات
        var settlements = await _context.OwnerDebtSettlements
            .Include(s => s.BankAccount)
            .Where(s => !s.IsDeleted && s.PartnerName == partnerName)
            .Select(s => new PartnerStatementEntryDto
            {
                TransactionDate = s.SettlementDate,
                TransactionType = "تسوية ذمة",
                Description = GenerateSettlementDescription(s.SettlementSource, s.BankAccount != null ? s.BankAccount.FriendlyName : null, s.Notes),
                Amount = s.Amount,
                IsSettlement = true,
                SourceType = SourceTypes.OwnerDebtSettlement,
                SourceId = s.Id,
                ReferenceNumber = s.Id.ToString(),
                CanDelete = true
            })
            .ToListAsync();
        var allEntriesList = debts.Concat(settlements)
            .OrderBy(e => e.TransactionDate)
            .ThenBy(e => e.IsSettlement)
            .ToList();

        // 4. حساب الرصيد التراكمي (Running Balance Engine)
        decimal runningBalance = 0;
        for (int i = 0; i < allEntriesList.Count; i++)
        {
            allEntriesList[i].SequenceNumber = i + 1;

            if (allEntriesList[i].IsSettlement)
                runningBalance -= allEntriesList[i].Amount;
            else
                runningBalance += allEntriesList[i].Amount;

            allEntriesList[i].RunningBalance = Math.Abs(runningBalance);
            allEntriesList[i].BalanceDirection = runningBalance > 0
                ? PartnerBalanceDirection.RestaurantOwesPartner
                : runningBalance < 0
                    ? PartnerBalanceDirection.PartnerOwesRestaurant
                    : PartnerBalanceDirection.Settled;

            allEntriesList[i].BalanceDirectionText = allEntriesList[i].BalanceDirection switch
            {
                PartnerBalanceDirection.RestaurantOwesPartner => "على المطعم",
                PartnerBalanceDirection.PartnerOwesRestaurant => "على الشريك",
                _ => "مسوّى"
            };
        }

        return allEntriesList;
    }

    // ────────────────────────────────────────────────────────────────
    // محرك توليد الأوصاف الذكية (Smart Description Engine)
    // ────────────────────────────────────────────────────────────────
    private static string GenerateDebtDescription(string? sourceType, string? expenseCategory, string? notes)
    {
        if (sourceType == "SupplierPayment")
            return $"سداد فاتورة مورد مدفوعة شخصياً{(string.IsNullOrEmpty(notes) ? "" : $" - {notes}")}";

        if (sourceType == "GeneralExpense")
            return $"مصروف تشغيلي ({expenseCategory ?? "عام"}) مدفوع بواسطة الشريك";

        return string.IsNullOrEmpty(notes) ? "تمويل مباشر من الشريك" : notes;
    }

    private static string GenerateSettlementDescription(OwnerDebtSettlementSource source, string? bankName, string? notes)
    {
        var sourceText = source switch
        {
            OwnerDebtSettlementSource.CashRegister => "صندوق الكاشير",
            OwnerDebtSettlementSource.Bank => bankName ?? "الحساب البنكي",
            OwnerDebtSettlementSource.PettyCash => "الخزينة",
            _ => "غير محدد"
        };

        return $"تسوية ذمة مالية عبر {sourceText}{(string.IsNullOrEmpty(notes) ? "" : $" - {notes}")}";
    }
}
