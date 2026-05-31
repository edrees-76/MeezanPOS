namespace MeezanPOS.Domain.Enums;

public enum RoleType { Admin, Manager, Cashier }
public enum PaymentMethod { Cash, Bank }
public enum TransactionType { Deposit, Withdrawal, Settlement }
public enum ShiftStatus { Open, Closed }
public enum WorkerTransactionType { WageAccrual, Payment, Advance, Deduction, Adjustment }
public enum ShiftType { FirstShift, SecondShift, FullDay }
public enum AttendanceStatus { Present, Absent, HalfDay, Leave }
