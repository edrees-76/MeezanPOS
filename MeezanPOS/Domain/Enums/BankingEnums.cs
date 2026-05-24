namespace MeezanPOS.Domain.Enums;

/// <summary>
/// تصنيف ملكية الحساب البنكي
/// </summary>
public enum BankAccountType
{
    Commercial = 1,   // حساب تجاري للمطعم
    PersonalMixed = 2 // حساب شخصي للمالك / شريك (مختلط)
}

/// <summary>
/// أنواع العمليات البنكية
/// </summary>
public enum BankTransactionType
{
    Deposit = 1,            // إيداع بنكي من النقدية
    Withdrawal = 2,         // سحب بنكي لتغذية الصندوق
    InternalTransfer = 3,   // تحويل داخلي بين الحسابات البنكية للمطعم
    CardSalesDeposit = 4,   // إيداع تلقائي لمبيعات البطاقات (تأكيد عملية معلقة)
    SupplierPayment = 5,    // سداد مورد عبر البنك
    ExpensePayment = 6,     // دفع مصروف عام أو يومي عبر البنك
    OwnerDebtSettlement = 7,// سداد مستحقات المالك الشخصية من البنك
    ExchangeDifference = 8  // فروقات وعجز العمليات المصرفية
}

/// <summary>
/// حالة مبيعات البطاقات الإلكترونية
/// </summary>
public enum CardPaymentStatus
{
    Pending = 1, // تحت التحصيل
    Cleared = 2  // مؤكدة ووصلت الحساب البنكي
}

/// <summary>
/// حالة ديون المالك/الشركاء
/// </summary>
public enum OwnerDebtStatus
{
    Unpaid = 1, // غير مسدد (دين قائم)
    Paid = 2    // تم التسوية (مسوّى)
}

/// <summary>
/// مصدر تمويل تسوية ديون المالك
/// </summary>
public enum OwnerDebtSettlementSource
{
    CashRegister = 1, // كاشير الوردية (الدرج)
    Bank = 2,         // الحساب البنكي
    PettyCash = 3     // النقدية المجمعة (الخزينة)
}

/// <summary>
/// ����� ������ ������ ������
/// </summary>
public enum PartnerBalanceDirection
{
    RestaurantOwesPartner = 1, // ������ ���� ������
    PartnerOwesRestaurant = 2, // ������ ���� ������
    Settled = 3                // ��� ������� / ���
}
