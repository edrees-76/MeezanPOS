using System;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Tests.Helpers;

public static class TestDataBuilder
{
    public static DailyJournal BuildDailyJournal(DateTime date, FinancialStatus status, decimal totalSales, decimal totalExpenses)
    {
        return new DailyJournal
        {
            JournalDate = date,
            FinancialStatus = status,
            TotalSales = totalSales,
            TotalExpenses = totalExpenses,
            ShiftType = ShiftType.FullDay,
            EmployeeName = "Test Employee",
            RowVersion = 1
        };
    }

    public static CashMovement BuildCashMovement(decimal amount, CashMovementType type, decimal balanceAfter, DateTime date)
    {
        return new CashMovement
        {
            Amount = amount,
            Type = type,
            BalanceAfter = balanceAfter,
            TransactionDate = date,
            IsReversed = false
        };
    }

    public static BankTransaction BuildBankTransaction(decimal amount, BankTransactionType type, int bankAccountId, DateTime date)
    {
        return new BankTransaction
        {
            Amount = amount,
            Type = type,
            BankAccountId = bankAccountId,
            TransactionDate = date,
            BalanceAfter = 0m
        };
    }

    public static SupplierInvoice BuildSupplierInvoice(decimal amount, int supplierId, DateTime date)
    {
        return new SupplierInvoice
        {
            TotalAmount = amount,
            SupplierId = supplierId,
            InvoiceDate = date,
            Status = InvoiceStatus.Unpaid
        };
    }

    // Since SupplierPayment is not a separate entity in the database (payments are tracked via SupplierTransaction),
    // we return a SupplierTransaction of type DecreaseDebt representing a payment.
    public static SupplierTransaction BuildSupplierPayment(decimal amount, int supplierId, DateTime date)
    {
        return new SupplierTransaction
        {
            Amount = amount,
            SupplierId = supplierId,
            TransactionDate = date,
            Type = SupplierTransactionType.DecreaseDebt,
            SourceType = TransactionSourceType.ExternalPayment
        };
    }

    public static SupplierTransaction BuildSupplierTransaction(decimal amount, SupplierTransactionType type, int supplierId, DateTime date, decimal balanceAfter)
    {
        return new SupplierTransaction
        {
            Amount = amount,
            Type = type,
            SupplierId = supplierId,
            TransactionDate = date,
            BalanceAfter = balanceAfter,
            SourceType = TransactionSourceType.Invoice
        };
    }

    public static Worker BuildWorker(string name, decimal dailyWage)
    {
        return new Worker
        {
            WorkerName = name,
            DailyWage = dailyWage,
            IsActive = true
        };
    }

    public static BankAccount BuildBankAccount(string name, decimal initialBalance)
    {
        return new BankAccount
        {
            FriendlyName = name,
            OpeningBalance = initialBalance,
            CurrentBalance = initialBalance,
            IsActive = true
        };
    }

    public static Supplier BuildSupplier(string name)
    {
        return new Supplier
        {
            Name = name,
            OpeningBalance = 0m,
            CurrentBalance = 0m,
            IsActive = true
        };
    }
}
