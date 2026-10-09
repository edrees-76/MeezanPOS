using System.Collections.Generic;

namespace MeezanPOS.Application.Services;

/// <summary>فئات سجل النشاط لفلترة الشاشة.</summary>
public enum ActivityCategory
{
    All,
    Sessions,
    Financial,
    Changes,
    Users
}

/// <summary>
/// أسماء عربية لعمليات سجل التدقيق والكيانات المتأثرة، وتصنيف كل عملية.
/// العمليات تُخزَّن بأسمائها البرمجية (Post, AutoDelete...) لتبقى ثابتة إن تغيّرت الصياغة العربية.
/// </summary>
public static class ActivityLabels
{
    public const string Login = "Login";
    public const string Logout = "Logout";
    public const string LoginFailed = "LoginFailed";
    public const string LockedOut = "LockedOut";
    public const string UnlockUser = "UnlockUser";
    public const string AutoCreate = "AutoCreate";
    public const string SystemReset = "SystemReset";

    private static readonly Dictionary<string, (string Name, ActivityCategory Category)> Actions = new()
    {
        [Login] = ("تسجيل دخول", ActivityCategory.Sessions),
        [Logout] = ("تسجيل خروج", ActivityCategory.Sessions),
        [LoginFailed] = ("كلمة مرور خاطئة", ActivityCategory.Sessions),
        [LockedOut] = ("قفل الحساب مؤقتاً بعد محاولات فاشلة", ActivityCategory.Sessions),

        ["CreateUser"] = ("إضافة مستخدم", ActivityCategory.Users),
        ["ResetPassword"] = ("إعادة تعيين كلمة مرور", ActivityCategory.Users),
        ["ChangePassword"] = ("تغيير كلمة المرور", ActivityCategory.Users),
        ["ChangeRole"] = ("تغيير الدور", ActivityCategory.Users),
        ["EnableUser"] = ("تفعيل حساب", ActivityCategory.Users),
        ["DisableUser"] = ("إيقاف حساب", ActivityCategory.Users),
        [UnlockUser] = ("فك قفل حساب", ActivityCategory.Users),

        [AutoCreate] = ("إضافة", ActivityCategory.Changes),
        ["AutoUpdate"] = ("تعديل مبلغ", ActivityCategory.Changes),
        ["AutoDelete"] = ("حذف", ActivityCategory.Changes),
        ["AutoSoftDelete"] = ("حذف", ActivityCategory.Changes),

        ["Post"] = ("ترحيل", ActivityCategory.Financial),
        ["PostBatch"] = ("ترحيل مجموعة", ActivityCategory.Financial),
        ["Unpost"] = ("فك ترحيل", ActivityCategory.Financial),
        ["UnpostPeriod"] = ("فك ترحيل فترة", ActivityCategory.Financial),
        ["SettleAndLockPeriod"] = ("تسوية وإقفال فترة", ActivityCategory.Financial),
        ["UnlockPeriod"] = ("فك قفل فترة", ActivityCategory.Financial),
        ["RecordSettlement"] = ("تسوية مستحقات شريك", ActivityCategory.Financial),
        ["DeleteSettlement"] = ("حذف تسوية شريك", ActivityCategory.Financial),
        ["RecordFunding"] = ("تمويل من شريك", ActivityCategory.Financial),
        ["DeleteFunding"] = ("حذف تمويل شريك", ActivityCategory.Financial),
        ["InternalTransfer"] = ("تحويل بين المصارف", ActivityCategory.Financial),
        ["RebuildSupplierLedger"] = ("إعادة بناء كشف مورد", ActivityCategory.Financial),
        ["RebuildLedger"] = ("إعادة بناء دفتر النقدية", ActivityCategory.Financial),
        [SystemReset] = ("إعادة ضبط المنظومة", ActivityCategory.Financial),
    };

    private static readonly Dictionary<string, string> Entities = new()
    {
        ["User"] = "مستخدم",
        ["DailyJournal"] = "يومية",
        ["GeneralExpense"] = "مصروف عام",
        ["Supplier"] = "مورد",
        ["SupplierInvoice"] = "فاتورة مورد",
        ["SupplierTransaction"] = "حركة مورد",
        ["Worker"] = "عامل",
        ["WorkerTransaction"] = "حركة عامل",
        ["BankAccount"] = "حساب مصرفي",
        ["BankTransaction"] = "حركة مصرفية",
        ["CashMovement"] = "حركة نقدية",
        ["OwnerDebt"] = "دين شريك",
        ["OwnerDebtSettlement"] = "تسوية شريك",
        ["PostingSession"] = "فترة مالية",
        ["FinancialPeriod"] = "فترة مالية",
        ["CardPaymentReconciliation"] = "تحصيل بطاقات",
        ["Role"] = "دور",
    };

    public static string ActionName(string action)
        => Actions.TryGetValue(action, out var a) ? a.Name : action;

    public static ActivityCategory CategoryOf(string action)
        => Actions.TryGetValue(action, out var a) ? a.Category : ActivityCategory.Financial;

    public static string EntityName(string entity)
        => Entities.TryGetValue(entity, out var name) ? name : entity;

    /// <summary>العمليات البرمجية لكل فئة (للفلترة في الاستعلام).</summary>
    public static IEnumerable<string> ActionsIn(ActivityCategory category)
    {
        foreach (var (action, info) in Actions)
            if (info.Category == category)
                yield return action;
    }
}
