using System;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Application.Services;

/// <summary>
/// أسماء الصلاحيات ومصفوفة الأدوار في مكان واحد.
/// - المدير العام (Admin): كل شيء.
/// - المدير (Manager): العمل اليومي والتقارير والإعدادات العادية، دون إدارة المستخدمين
///   أو فك الترحيل/إلغاء قفل الفترات أو الاستعادة وإعادة الضبط.
/// - الكاشير (Cashier): إدخال اليومية والاطلاع على المبيعات ولوحة التحكم فقط.
/// </summary>
public static class Permissions
{
    public const string ViewDashboard = "ViewDashboard";
    public const string CreateJournal = "CreateJournal";
    public const string ViewSales = "ViewSales";
    public const string ManageSuppliers = "ManageSuppliers";
    public const string ManageExpenses = "ManageExpenses";
    public const string ManageWages = "ManageWages";
    public const string ManageBanking = "ManageBanking";
    public const string ManageReturns = "ManageReturns";
    public const string ClosingAccount = "ClosingAccount";
    public const string ViewSettings = "ViewSettings";

    public const string UnpostFinancial = "UnpostFinancial";
    public const string UnlockPeriod = "UnlockPeriod";
    public const string ManageUsers = "ManageUsers";
    public const string RestoreBackup = "RestoreBackup";
    public const string SystemReset = "DeleteSystem";

    public const string DeniedMessage = "ليس لديك صلاحية لتنفيذ هذه العملية. يرجى التواصل مع المدير العام.";

    public static bool RoleAllows(RoleType role, string operation) => role switch
    {
        RoleType.Admin => true,
        RoleType.Manager => operation is not (UnpostFinancial or UnlockPeriod or ManageUsers or RestoreBackup or SystemReset),
        RoleType.Cashier => operation is CreateJournal or ViewSales or ViewDashboard,
        _ => false
    };
}

/// <summary>رفض تنفيذ عملية لعدم وجود صلاحية.</summary>
public class PermissionDeniedException : UnauthorizedAccessException
{
    public PermissionDeniedException() : base(Permissions.DeniedMessage) { }
}
