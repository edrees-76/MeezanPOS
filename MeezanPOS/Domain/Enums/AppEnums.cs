namespace MeezanPOS.Domain.Enums;

public enum RoleType { Admin, Manager, Cashier }
public enum PaymentMethod { Cash, Bank }
public enum TransactionType { Deposit, Withdrawal, Settlement }
public enum ShiftStatus { Open, Closed }
public enum WorkerTransactionType { WageAccrual, Payment, Advance, Deduction, Adjustment }
public enum ShiftType { FirstShift, SecondShift, FullDay }
public enum AttendanceStatus { Present, Absent, HalfDay, Leave }

public enum BackupFrequency
{
    Disabled,
    Daily,
    Weekly,
    Monthly
}

public enum WorkerLedgerFilter
{
    All,         // الكل
    WageAccrual, // استحقاق أجور
    Payment,     // رواتب ومدفوعات
    Advance,     // سلف عمال
    Deduction,   // خصومات وغرامات
    Adjustment   // تسويات يدوية
}

