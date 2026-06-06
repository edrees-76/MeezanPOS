using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Application.Services;

public class BankService : IBankService
{
    private readonly AppDbContext _context;
    private readonly ISessionService _session;
    private readonly AuditService _auditService;

    public BankService(AppDbContext context, ISessionService session, AuditService auditService)
    {
        _context = context;
        _session = session;
        _auditService = auditService;
    }

    public async Task<List<BankAccount>> GetAllAccountsAsync()
    {
        return await _context.BankAccounts
            .Where(a => !a.IsDeleted)
            .OrderBy(a => a.FriendlyName)
            .ToListAsync();
    }

    public async Task<BankAccount?> GetAccountByIdAsync(int id)
    {
        return await _context.BankAccounts
            .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
    }

    public async Task<BankAccount> CreateAccountAsync(BankAccount account)
    {
        account.CurrentBalance = account.OpeningBalance;
        _context.BankAccounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    public async Task UpdateAccountAsync(BankAccount account)
    {
        var dbAccount = await _context.BankAccounts.FindAsync(account.Id);
        if (dbAccount == null || dbAccount.IsDeleted)
            throw new Exception("الحساب البنكي غير موجود.");

        dbAccount.FriendlyName = account.FriendlyName;
        dbAccount.LegalOwnerName = account.LegalOwnerName;
        dbAccount.AccountType = account.AccountType;
        dbAccount.BankName = account.BankName;
        dbAccount.AccountNumber = account.AccountNumber;
        dbAccount.IsActive = account.IsActive;
        dbAccount.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await RebuildAccountBalanceAsync(dbAccount.Id);
    }

    public async Task DeleteAccountAsync(int id)
    {
        var account = await _context.BankAccounts.FindAsync(id);
        if (account == null || account.IsDeleted)
            throw new Exception("الحساب البنكي غير موجود.");

        account.IsDeleted = true;
        account.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<List<BankTransaction>> GetTransactionsAsync(int bankAccountId, DateTime fromDate, DateTime toDate)
    {
        return await _context.BankTransactions
            .Where(t => t.BankAccountId == bankAccountId && !t.IsDeleted && 
                        t.TransactionDate.Date >= fromDate.Date && t.TransactionDate.Date <= toDate.Date)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<BankTransaction> RecordTransactionAsync(int bankAccountId, BankTransactionType type, decimal amount, string? referenceNumber, string? notes, string? sourceType = null, int? sourceId = null, DateTime? transactionDate = null)
    {
        var account = await _context.BankAccounts.FindAsync(bankAccountId);
        if (account == null || account.IsDeleted)
            throw new Exception("الحساب البنكي غير موجود.");

        var date = transactionDate ?? DateTime.UtcNow;
        decimal change = CalculateBalanceChange(type, amount);

        account.CurrentBalance += change;
        account.UpdatedAt = DateTime.UtcNow;

        var tx = new BankTransaction
        {
            BankAccountId = bankAccountId,
            Type = type,
            Amount = amount,
            ReferenceNumber = referenceNumber,
            BalanceAfter = account.CurrentBalance,
            TransactionDate = date,
            Notes = notes,
            SourceType = sourceType,
            SourceId = sourceId
        };

        _context.BankTransactions.Add(tx);
        await _context.SaveChangesAsync();
        return tx;
    }

    public async Task RecordInternalTransferAsync(int fromAccountId, int toAccountId, decimal amount, string? notes, DateTime date)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var fromAccount = await _context.BankAccounts.FindAsync(fromAccountId);
            var toAccount = await _context.BankAccounts.FindAsync(toAccountId);

            if (fromAccount == null || fromAccount.IsDeleted || toAccount == null || toAccount.IsDeleted)
                throw new Exception("أحد الحسابات البنكية غير موجودة.");

            // 1. خصم من الحساب المصدر
            fromAccount.CurrentBalance -= amount;
            fromAccount.UpdatedAt = DateTime.UtcNow;
            var fromTx = new BankTransaction
            {
                BankAccountId = fromAccountId,
                Type = BankTransactionType.InternalTransferOut,
                Amount = amount,
                BalanceAfter = fromAccount.CurrentBalance,
                TransactionDate = date,
                Notes = notes ?? $"تحويل إلى {toAccount.FriendlyName}",
                SourceType = SourceTypes.InternalTransfer
            };
            _context.BankTransactions.Add(fromTx);

            // 2. إضافة للحساب المستلم
            toAccount.CurrentBalance += amount;
            toAccount.UpdatedAt = DateTime.UtcNow;
            var toTx = new BankTransaction
            {
                BankAccountId = toAccountId,
                Type = BankTransactionType.InternalTransferIn,
                Amount = amount,
                BalanceAfter = toAccount.CurrentBalance,
                TransactionDate = date,
                Notes = notes ?? $"تحويل من {fromAccount.FriendlyName}",
                SourceType = SourceTypes.InternalTransfer
            };
            _context.BankTransactions.Add(toTx);

            await _context.SaveChangesAsync();

            // ربط المراجع التبادلية قبل الـ Commit لضمان الذرية
            fromTx.SourceId = toTx.Id;
            toTx.SourceId = fromTx.Id;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(_session.CurrentUsername, "InternalTransfer", "BankTransaction", fromTx.Id, null, $"From Account {fromAccountId} to {toAccountId} Amount {amount}");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task RecordDepositAsync(int bankAccountId, decimal amount, string? referenceNumber, string? notes, DateTime date)
    {
        await RecordTransactionAsync(bankAccountId, BankTransactionType.Deposit, amount, referenceNumber, notes, "Manual", null, date);
    }

    public async Task RecordWithdrawalAsync(int bankAccountId, decimal amount, string? referenceNumber, string? notes, DateTime date)
    {
        await RecordTransactionAsync(bankAccountId, BankTransactionType.Withdrawal, amount, referenceNumber, notes, "Manual", null, date);
    }

    public async Task<List<CardPaymentReconciliation>> GetPendingCardPaymentsAsync()
    {
        return await _context.CardPaymentReconciliations
            .Include(r => r.DailyJournal)
            .Where(r => r.Status == CardPaymentStatus.Pending && !r.IsDeleted)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task ClearCardPaymentAsync(int reconciliationId, int bankAccountId, DateTime clearDate)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var recon = await _context.CardPaymentReconciliations
                .Include(r => r.DailyJournal)
                .FirstOrDefaultAsync(r => r.Id == reconciliationId && !r.IsDeleted);

            if (recon == null)
                throw new Exception("عملية التحصيل غير موجودة.");

            if (recon.Status == CardPaymentStatus.Cleared)
                throw new Exception("هذه العملية مؤكدة مسبقاً.");

            // 1. تسجيل الحركة البنكية كإيداع كروت
            var notes = $"تأكيد مبيعات إلكترونية - وردية {recon.DailyJournal?.JournalDate:dd-MM-yyyy} / {recon.DailyJournal?.ShiftName}";
            var bankTx = await RecordTransactionAsync(
                bankAccountId,
                BankTransactionType.CardSalesDeposit,
                recon.Amount,
                recon.ReferenceNumber,
                notes,
                "CardReconciliation",
                recon.Id,
                clearDate
            );

            // 2. تحديث حالة عملية المطابقة
            recon.Status = CardPaymentStatus.Cleared;
            recon.ClearedDate = clearDate;
            recon.BankTransactionId = bankTx.Id;
            recon.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task RecordPendingCardPaymentAsync(int dailyJournalId, decimal amount, string? bankName, string? referenceNumber, int? bankAccountId = null)
    {
        var recon = new CardPaymentReconciliation
        {
            DailyJournalId = dailyJournalId,
            Amount = amount,
            BankName = bankName,
            ReferenceNumber = referenceNumber,
            BankAccountId = bankAccountId,
            Status = CardPaymentStatus.Pending
        };
        _context.CardPaymentReconciliations.Add(recon);
        await _context.SaveChangesAsync();
    }

    public async Task<decimal> CalculateRestaurantShareAsync(int bankAccountId)
    {
        var account = await _context.BankAccounts.FindAsync(bankAccountId);
        if (account == null || account.IsDeleted)
            return 0;

        // بالنسبة للحساب المختلط، نقوم بحساب المجموع الفعلي للحركات البنكية للمطعم
        var transactions = await _context.BankTransactions
            .Where(t => t.BankAccountId == bankAccountId && !t.IsDeleted)
            .ToListAsync();

        decimal balance = account.OpeningBalance;
        foreach (var tx in transactions)
        {
            balance += CalculateBalanceChange(tx.Type, tx.Amount, tx.Notes);
        }

        return balance;
    }

    public async Task RebuildAccountBalanceAsync(int bankAccountId)
    {
        var account = await _context.BankAccounts.FindAsync(bankAccountId);

        if (account == null || account.IsDeleted) return;

        var transactions = await _context.BankTransactions
            .Where(t => t.BankAccountId == bankAccountId && !t.IsDeleted)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();

        decimal runningBalance = account.OpeningBalance;

        foreach (var tx in transactions)
        {
            runningBalance += CalculateBalanceChange(tx.Type, tx.Amount, tx.Notes);
            tx.BalanceAfter = runningBalance;
        }

        account.CurrentBalance = runningBalance;
        await _context.SaveChangesAsync();
    }

    public async Task DeleteTransactionBySourceAsync(string sourceType, int sourceId)
    {
        var transactions = await _context.BankTransactions
            .Where(t => t.SourceType == sourceType && t.SourceId == sourceId && !t.IsDeleted)
            .ToListAsync();

        if (!transactions.Any()) return;

        foreach (var tx in transactions)
        {
            tx.IsDeleted = true;
            tx.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        var affectedAccountIds = transactions.Select(t => t.BankAccountId).Distinct();
        foreach (var accId in affectedAccountIds)
        {
            await RebuildAccountBalanceAsync(accId);
        }
    }

    public async Task<decimal> GetOpeningBalanceBeforeDateAsync(int bankAccountId, DateTime beforeDate)
    {
        var account = await _context.BankAccounts.FindAsync(bankAccountId);
        if (account == null || account.IsDeleted) return 0;

        // الرصيد الافتتاحي الأصلي + مجموع كل الحركات قبل تاريخ البداية
        var priorTransactions = await _context.BankTransactions
            .Where(t => t.BankAccountId == bankAccountId && !t.IsDeleted
                         && t.TransactionDate.Date < beforeDate.Date)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();

        decimal balance = account.OpeningBalance;
        foreach (var tx in priorTransactions)
        {
            balance += CalculateBalanceChange(tx.Type, tx.Amount, tx.Notes);
        }
        return balance;
    }

    private decimal CalculateBalanceChange(BankTransactionType type, decimal amount, string? notes = null)
    {
        return type switch
        {
            BankTransactionType.Deposit => amount,
            BankTransactionType.CardSalesDeposit => amount,
            BankTransactionType.Withdrawal => -amount,
            BankTransactionType.SupplierPayment => -amount,
            BankTransactionType.ExpensePayment => -amount,
            BankTransactionType.OwnerDebtSettlement => -amount,
            BankTransactionType.InternalTransferOut => -amount,
            BankTransactionType.InternalTransferIn => amount,
            // التوافق مع البيانات القديمة فقط:
            BankTransactionType.InternalTransfer => (notes != null && (notes.StartsWith("تحويل من") || notes.Contains("من "))) ? amount : -amount,
            BankTransactionType.ExchangeDifference => amount,
            _ => 0
        };
    }
}

