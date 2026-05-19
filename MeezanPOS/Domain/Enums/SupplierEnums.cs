namespace MeezanPOS.Domain.Enums;

public enum ExpenseType
{
    General = 1,
    SupplierPayment = 2,
    Purchase = 3,
    InvoicePayment = 4,
    WorkerWage = 5,
    Regular = 6,
    Other = 7,
    Gas = 8,
    Cleaning = 9,
    Maintenance = 10,
    Transport = 11,
    PettyCash = 12,
    Coal = 13,
    Bread = 14
}

public enum SupplierTransactionType
{
    IncreaseDebt = 1, // يزيد ديون المطعم (فاتورة مشتريات)
    DecreaseDebt = 2  // يقلل ديون المطعم (سداد دفعة)
}

public enum TransactionSourceType
{
    OpeningBalance = 1,
    Invoice = 2,
    DailyJournalPayment = 3,
    ExternalPayment = 4,
    Adjustment = 5
}

public enum InvoiceStatus
{
    Unpaid = 1,
    PartiallyPaid = 2,
    Paid = 3
}
