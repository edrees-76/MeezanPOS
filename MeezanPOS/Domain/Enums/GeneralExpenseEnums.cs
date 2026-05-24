namespace MeezanPOS.Domain.Enums;

/// <summary>
/// أنواع المصاريف العامة (الثابتة/الشهرية)
/// </summary>
public enum GeneralExpenseType
{
    Rent = 1,           // إيجار
    Electricity = 2,    // كهرباء
    Water = 3,          // ماء
    Salaries = 4,       // رواتب
    Internet = 5,       // إنترنت
    Maintenance = 6,    // صيانة
    Insurance = 7,      // تأمين
    Taxes = 8,          // ضرائب/رسوم
    SupplierPayment = 10,     // تسديد قيمة
    Other = 99          // أخرى
}

/// <summary>
/// طرق الدفع
/// </summary>
public enum PaymentMethodType
{
    Cash = 1,           // نقدي
    BankTransfer = 2,   // تحويل بنكي
    Cheque = 3,         // شيك
    PersonalPartner = 4 // شخصي (شريك)
}
