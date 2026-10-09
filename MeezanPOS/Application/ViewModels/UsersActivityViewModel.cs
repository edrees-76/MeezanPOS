using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Application.Services;
using MeezanPOS.Application.Services.Queries;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Application.ViewModels;

/// <summary>سطر مستخدم في قائمة المستخدمين.</summary>
public sealed record UserRowItem(int Id, string Username, string FullName, RoleType? Role, bool IsActive,
    DateTime? LastLoginAt, int FailedLoginAttempts, DateTime? LockoutEnd)
{
    public string RoleName => Role.HasValue ? UserManagementService.RoleDisplayName(Role.Value) : "-";
    public bool IsLockedOut => LockoutEnd.HasValue && LockoutEnd.Value > DateTime.UtcNow;
    public string StatusText => !IsActive ? "موقوف" : IsLockedOut ? $"مقفل حتى {LockoutEnd!.Value.ToLocalTime():HH:mm}" : "فعّال";
    public string LastLoginText => LastLoginAt.HasValue ? LastLoginAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm") : "لم يدخل بعد";
}

public sealed record RoleItem(RoleType Type, string Name)
{
    public override string ToString() => Name;
}

public sealed record CategoryItem(ActivityCategory Category, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// قسم "المستخدمون والنشاط": إدارة حسابات المستخدمين، وسجل نشاطهم (للمدير العام فقط).
/// كانت إدارة المستخدمين نافذة تُفتح من الإعدادات، ولم تكن هناك شاشة لعرض سجل النشاط.
/// </summary>
public partial class UsersActivityViewModel : ObservableObject
{
    private readonly Func<IUserManagementService> _users;
    private readonly IActivityQueryService _activity;

    public UsersActivityViewModel()
        : this(() => AppServiceProvider.Resolve<IUserManagementService>(), new ActivityQueryService())
    {
    }

    public UsersActivityViewModel(Func<IUserManagementService> users, IActivityQueryService activity)
    {
        _users = users;
        _activity = activity;
        NewRole = Roles.First(r => r.Type == RoleType.Cashier);
        SelectedCategory = Categories[0];
        _ = LoadAsync();
    }

    public IReadOnlyList<RoleItem> Roles { get; } =
        Enum.GetValues<RoleType>().Select(r => new RoleItem(r, UserManagementService.RoleDisplayName(r))).ToList();

    public IReadOnlyList<CategoryItem> Categories { get; } = new List<CategoryItem>
    {
        new(ActivityCategory.All, "كل العمليات"),
        new(ActivityCategory.Sessions, "الدخول والخروج"),
        new(ActivityCategory.Financial, "العمليات المالية"),
        new(ActivityCategory.Changes, "الإضافة والتعديل والحذف"),
        new(ActivityCategory.Users, "إدارة المستخدمين"),
    };

    // ── المستخدمون ──────────────────────────────────────────────

    public ObservableCollection<UserRowItem> Users { get; } = new();

    [ObservableProperty] private UserRowItem? selectedUser;
    [ObservableProperty] private string newUsername = string.Empty;
    [ObservableProperty] private string newFullName = string.Empty;
    [ObservableProperty] private RoleItem? newRole;
    [ObservableProperty] private string temporaryPassword = string.Empty;
    [ObservableProperty] private RoleItem? changeRoleTo;
    [ObservableProperty] private string userMessage = string.Empty;
    [ObservableProperty] private bool userMessageIsError;

    public async Task LoadAsync()
    {
        await LoadUsersAsync();
        await LoadActivityUsersAsync();
        await SearchActivityAsync();
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var list = await _users().GetUsersAsync();
            UiThread.Run(() =>
            {
                Users.Clear();
                foreach (var u in list)
                    Users.Add(new UserRowItem(u.Id, u.Username, u.FullName, u.Role?.Type, u.IsActive,
                        u.LastLoginAt, u.FailedLoginAttempts, u.LockoutEnd));
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "تعذر تحميل المستخدمين");
            ShowUserMessage($"تعذر تحميل المستخدمين: {ex.Message}", true);
        }
    }

    private void ShowUserMessage(string text, bool isError)
    {
        UserMessage = text;
        UserMessageIsError = isError;
    }

    private async Task<bool> RunUserActionAsync(Func<IUserManagementService, Task> action, string success)
    {
        try
        {
            await action(_users());
            ShowUserMessage(success, false);
            await LoadUsersAsync();
            await SearchActivityAsync();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            ShowUserMessage(ex.Message, true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "فشلت عملية إدارة مستخدم");
            ShowUserMessage("حدث خطأ غير متوقع. التفاصيل في سجل الأخطاء.", true);
        }
        return false;
    }

    [RelayCommand]
    private async Task AddUserAsync()
    {
        var username = NewUsername.Trim();
        var role = NewRole?.Type ?? RoleType.Cashier;
        if (await RunUserActionAsync(s => s.CreateUserAsync(username, NewFullName, TemporaryPassword, role),
                $"تمت إضافة المستخدم \"{username}\". سيُطلب منه تغيير كلمة المرور عند أول دخول."))
        {
            NewUsername = string.Empty;
            NewFullName = string.Empty;
            TemporaryPassword = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (SelectedUser is not { } user) { ShowUserMessage("اختر مستخدماً من القائمة أولاً.", true); return; }
        if (string.IsNullOrWhiteSpace(TemporaryPassword)) { ShowUserMessage("اكتب كلمة المرور المؤقتة في الخانة المخصصة.", true); return; }
        if (await RunUserActionAsync(s => s.ResetPasswordAsync(user.Id, TemporaryPassword),
                $"تمت إعادة تعيين كلمة مرور \"{user.Username}\". سيُطلب منه تغييرها عند الدخول."))
            TemporaryPassword = string.Empty;
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (SelectedUser is not { } user) { ShowUserMessage("اختر مستخدماً من القائمة أولاً.", true); return; }
        await RunUserActionAsync(s => s.SetActiveAsync(user.Id, !user.IsActive),
            user.IsActive ? $"تم إيقاف حساب \"{user.Username}\"." : $"تم تفعيل حساب \"{user.Username}\".");
    }

    [RelayCommand]
    private async Task ChangeRoleAsync()
    {
        if (SelectedUser is not { } user) { ShowUserMessage("اختر مستخدماً من القائمة أولاً.", true); return; }
        if (ChangeRoleTo is not { } role) { ShowUserMessage("اختر الدور الجديد.", true); return; }
        await RunUserActionAsync(s => s.ChangeRoleAsync(user.Id, role.Type), $"تم تغيير دور \"{user.Username}\" إلى {role.Name}.");
    }

    [RelayCommand]
    private async Task UnlockUserAsync()
    {
        if (SelectedUser is not { } user) { ShowUserMessage("اختر مستخدماً من القائمة أولاً.", true); return; }
        if (!user.IsLockedOut && user.FailedLoginAttempts == 0) { ShowUserMessage("هذا الحساب غير مقفل.", true); return; }
        await RunUserActionAsync(s => s.UnlockAsync(user.Id), $"تم فك قفل حساب \"{user.Username}\".");
    }

    // ── سجل النشاط ──────────────────────────────────────────────

    public ObservableCollection<ActivityRow> Activity { get; } = new();
    public ObservableCollection<ActivityUser> ActivityUsers { get; } = new();

    private static readonly ActivityUser AllUsers = new(0, "كل المستخدمين");

    [ObservableProperty] private DateTime activityFrom = DateTime.Today.AddDays(-30);
    [ObservableProperty] private DateTime activityTo = DateTime.Today;
    [ObservableProperty] private ActivityUser? selectedActivityUser;
    [ObservableProperty] private CategoryItem? selectedCategory;
    [ObservableProperty] private bool isActivityLoading;
    [ObservableProperty] private string activitySummary = string.Empty;

    private const int MaxRows = 5000;

    private async Task LoadActivityUsersAsync()
    {
        try
        {
            var list = await _activity.GetUsersAsync();
            UiThread.Run(() =>
            {
                ActivityUsers.Clear();
                ActivityUsers.Add(AllUsers);
                foreach (var u in list) ActivityUsers.Add(u);
                SelectedActivityUser = AllUsers;
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "تعذر تحميل قائمة المستخدمين لفلتر النشاط");
        }
    }

    private ActivityFilter CurrentFilter() => new(
        ActivityFrom, ActivityTo,
        SelectedActivityUser is { Id: > 0 } u ? u.Id : null,
        SelectedCategory?.Category ?? ActivityCategory.All);

    [RelayCommand]
    private async Task SearchActivityAsync()
    {
        if (ActivityTo < ActivityFrom)
        {
            Dialogs.Show("تاريخ النهاية قبل تاريخ البداية.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsActivityLoading = true;
        try
        {
            var rows = await Task.Run(() => _activity.GetActivityAsync(CurrentFilter(), MaxRows));
            UiThread.Run(() =>
            {
                Activity.Clear();
                foreach (var r in rows) Activity.Add(r);
            });
            ActivitySummary = rows.Count >= MaxRows
                ? $"يُعرض أحدث {MaxRows:N0} عملية فقط. ضيّق الفترة أو الفلاتر لرؤية الباقي."
                : $"عدد العمليات: {rows.Count:N0}";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "تعذر تحميل سجل النشاط");
            Dialogs.Show($"تعذر تحميل سجل النشاط:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsActivityLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportActivityPdfAsync()
    {
        if (Activity.Count == 0)
        {
            Dialogs.Show("لا توجد عمليات لتصديرها.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var filterText = $"من {ActivityFrom:yyyy/MM/dd} إلى {ActivityTo:yyyy/MM/dd}"
                             + $" | {SelectedActivityUser?.Name ?? AllUsers.Name} | {SelectedCategory?.Name}";
            var folder = Path.Combine(Path.GetTempPath(), "MeezanPOS");
            Directory.CreateDirectory(folder);
            var filePath = Path.Combine(folder, $"سجل النشاط - {DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            var rows = Activity.ToList();

            await Task.Run(() => MeezanPOS.Infrastructure.Reports.ActivityPdfExporter.GenerateReport(filePath, rows, filterText));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "تعذر تصدير سجل النشاط");
            Dialogs.Show($"تعذر تصدير سجل النشاط:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
