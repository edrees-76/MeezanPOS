using System;
using System.Collections.Generic;
using System.Linq;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Application.Services;

/// <summary>شرح دور مستخدم: وصف مختصر وما يستطيعه وما لا يستطيعه.</summary>
public sealed record RoleProfile(RoleType Type, string Name, string Summary, string Icon,
    IReadOnlyList<string> Allowed, IReadOnlyList<string> Denied)
{
    public override string ToString() => Name;
}

/// <summary>
/// شرح الأدوار للمستخدم. القوائم تُبنى من <see cref="Permissions.RoleAllows"/> نفسها،
/// فلا يمكن أن يختلف الشرح المعروض عن الصلاحيات المطبقة فعلاً.
/// </summary>
public static class RoleProfiles
{
    /// <summary>كل صلاحية باسمها المفهوم، بترتيب العرض.</summary>
    private static readonly (string Permission, string Name)[] PermissionNames =
    {
        (Permissions.ViewDashboard, "لوحة التحكم"),
        (Permissions.CreateJournal, "تسجيل اليوميات"),
        (Permissions.ViewSales, "المبيعات والإيرادات"),
        (Permissions.ManageSuppliers, "الموردون وفواتيرهم ودفعاتهم"),
        (Permissions.ManageExpenses, "المصروفات"),
        (Permissions.ManageWages, "أجور العمال وحضورهم"),
        (Permissions.ManageBanking, "المصارف والخزينة والشركاء"),
        (Permissions.ManageReturns, "الطلبات المجانية والمرتجعات"),
        (Permissions.ClosingAccount, "الحساب الختامي والتقارير"),
        (Permissions.ViewSettings, "الإعدادات والنسخ الاحتياطي"),
        (Permissions.UnpostFinancial, "فك ترحيل اليوميات والمصروفات"),
        (Permissions.UnlockPeriod, "فك قفل فترة مسوّاة"),
        (Permissions.ManageUsers, "إدارة المستخدمين وسجل النشاط"),
        (Permissions.RestoreBackup, "استعادة نسخة احتياطية"),
        (Permissions.SystemReset, "إعادة ضبط المنظومة"),
    };

    private static readonly Dictionary<RoleType, (string Summary, string Icon)> Descriptions = new()
    {
        [RoleType.Admin] = ("صاحب المنظومة: كل الصلاحيات، ومنها العمليات الحساسة التي لا يمكن التراجع عنها بسهولة.", "ShieldCrown"),
        [RoleType.Manager] = ("يدير العمل اليومي والحسابات والتقارير، دون العمليات الحساسة وإدارة المستخدمين.", "AccountTie"),
        [RoleType.Cashier] = ("يسجّل يومية ورديته ويطّلع على المبيعات ولوحة التحكم فقط.", "CashRegister"),
    };

    public static IReadOnlyList<RoleProfile> All { get; } = Enum.GetValues<RoleType>().Select(Build).ToList();

    public static RoleProfile For(RoleType role) => All.First(r => r.Type == role);

    private static RoleProfile Build(RoleType role)
    {
        var (summary, icon) = Descriptions.TryGetValue(role, out var d) ? d : (string.Empty, "Account");
        return new RoleProfile(
            role,
            UserManagementService.RoleDisplayName(role),
            summary,
            icon,
            PermissionNames.Where(p => Permissions.RoleAllows(role, p.Permission)).Select(p => p.Name).ToList(),
            PermissionNames.Where(p => !Permissions.RoleAllows(role, p.Permission)).Select(p => p.Name).ToList());
    }
}
