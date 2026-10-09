namespace MeezanPOS.Domain.Enums;

/// <summary>
/// أنواع الحسابات المصرفية المتاحة
/// </summary>
public enum BankAccountType
{
    Commercial = 1,
    PersonalMixed = 2
}

/// <summary>
/// أنواع المعاملات المصرفية
/// </summary>
public enum BankTransactionType
{
    Deposit = 1,
    Withdrawal = 2,
    InternalTransfer = 3,
    CardSalesDeposit = 4,
    SupplierPayment = 5,
    ExpensePayment = 6,
    OwnerDebtSettlement = 7,
    ExchangeDifference = 8,
    InternalTransferOut = 9,
    InternalTransferIn = 10,
}

public enum CardPaymentStatus
{
    Pending = 1,
    Cleared = 2
}

public enum OwnerDebtStatus
{
    Unpaid = 1,
    Paid = 2
}

/// <summary>أين يدخل مبلغ تمويل الشريك.</summary>
public enum OwnerFundingDestination
{
    CashierDrawer = 0,
    Bank = 1,
    Treasury = 2
}

public enum OwnerDebtSettlementSource
{
    CashRegister = 1,
    Bank = 2,
    PettyCash = 3
}

public enum PartnerBalanceDirection
{
    RestaurantOwesPartner = 1,
    PartnerOwesRestaurant = 2,
    Settled = 3
}

