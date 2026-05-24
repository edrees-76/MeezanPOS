using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Application.Interfaces;

public interface IBankService
{
    // إدارة الحسابات
    Task<List<BankAccount>> GetAllAccountsAsync();
    Task<BankAccount?> GetAccountByIdAsync(int id);
    Task<BankAccount> CreateAccountAsync(BankAccount account);
    Task UpdateAccountAsync(BankAccount account);
    Task DeleteAccountAsync(int id);

    // الحركات المالية
    Task<List<BankTransaction>> GetTransactionsAsync(int bankAccountId, DateTime fromDate, DateTime toDate);
    Task<BankTransaction> RecordTransactionAsync(int bankAccountId, BankTransactionType type, decimal amount, string? referenceNumber, string? notes, string? sourceType = null, int? sourceId = null, DateTime? transactionDate = null);

    // التحويل الداخلي والسحب والإيداع
    Task RecordInternalTransferAsync(int fromAccountId, int toAccountId, decimal amount, string? notes, DateTime date);
    Task RecordDepositAsync(int bankAccountId, decimal amount, string? referenceNumber, string? notes, DateTime date);
    Task RecordWithdrawalAsync(int bankAccountId, decimal amount, string? referenceNumber, string? notes, DateTime date);

    // مطابقة البطاقات
    Task<List<CardPaymentReconciliation>> GetPendingCardPaymentsAsync();
    Task ClearCardPaymentAsync(int reconciliationId, int bankAccountId, DateTime clearDate);
    Task RecordPendingCardPaymentAsync(int dailyJournalId, decimal amount, string? bankName, string? referenceNumber, int? bankAccountId = null);

    // احتساب حصة المطعم
    Task<decimal> CalculateRestaurantShareAsync(int bankAccountId);

    // حذف الحركات بناءً على المصدر وإعادة الحساب
    Task DeleteTransactionBySourceAsync(string sourceType, int sourceId);

    // إعادة احتساب رصيد الحساب البنكي بناءً على كافة حركاته
    Task RebuildAccountBalanceAsync(int bankAccountId);

    // حساب الرصيد الافتتاحي قبل فترة محددة (لكشف الحساب)
    Task<decimal> GetOpeningBalanceBeforeDateAsync(int bankAccountId, DateTime beforeDate);
}


