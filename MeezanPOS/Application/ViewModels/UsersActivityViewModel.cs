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
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Application.ViewModels;

/// <summary>حالة حساب المستخدم كما تُعرض (لون الشارة).</summary>
public enum UserStatusKind { Active, Disabled, Locked }

/// <summary>مستخدم في القائمة.</summary>
public sealed record UserRowItem(int Id, string Username, string FullName, RoleType Role, bool IsActive,
    DateTime? LastLoginAt, int FailedLoginAttempts, DateTime? LockoutEnd)
{
    public RoleProfile RoleProfile => RoleProfiles.For(Role);
    public string RoleName => RoleProfile.Name;
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? Username : FullName;
    public string Initial => DisplayName.Trim().Length > 0 ? DisplayName.Trim()[0].ToString() : "؟";
    public bool IsLockedOut => LockoutEnd.HasValue && LockoutEnd.Value > DateTime.UtcNow;
    public UserStatusKind Status => !IsActive ? UserStatusKind.Disabled : IsLockedOut ? UserStatusKind.Locked : UserStatusKind.Active;
    public string StatusText => Status switch
    {
        UserStatusKind.Disabled => "موقوف",
        UserStatusKind.Locked => $"مقفل حتى {LockoutEnd!.Value.ToLocalTime():HH:mm}",
        _ => "فعّال"
    };
    public string LastLoginText => LastLoginAt.HasValue ? LastLoginAt.Value.ToLocalTime().ToString("yyyy/MM/dd  HH:mm") : "لم يدخل بعد";
    public bool HasFailedAttempts => FailedLoginAttempts > 0;
}

public sealed record CategoryItem(ActivityCategory Category, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// قسم "المستخدمون والنشاط": حسابات المستخدمين وأدوارهم، وسجل نشاطهم (للمدير العام فقط).
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
        SelectedCategory = Categories[0];
        _ = LoadAsync();
    }

    public IReadOnlyList<RoleProfile> Roles => RoleProfiles.All;

    public IReadOnlyList<CategoryItem> Categories { get; } = new List<CategoryItem>
    {
        new(ActivityCategory.All, "كل العمليات"),
        new(ActivityCategory.Sessions, "الدخول والخروج"),
        new(ActivityCategory.Financial, "العمليات المالية"),
        new(ActivityCategory.Changes, "الإضافة والتعديل والحذف"),
        new(ActivityCategory.Users, "إدارة المستخدمين"),
    };

    public async Task LoadAsync()
    {
        await LoadUsersAsync();
        await LoadActivityUsersAsync();
        await SearchActivityAsync();
    }

    // ── قائمة المستخدمين ──────────────────────────────────────────

    public ObservableCollection<UserRowItem> Users { get; } = new();

    [ObservableProperty] private int totalUsers;
    [ObservableProperty] private int activeUsers;
    [ObservableProperty] private int disabledUsers;
    [ObservableProperty] private int lockedUsers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedUser))]
    private UserRowItem? selectedUser;

    public bool HasSelectedUser => SelectedUser != null;

    /// <summary>الدور المختار لتغيير دور المستخدم المحدد (تُعرض صلاحياته قبل التأكيد).</summary>
    [ObservableProperty] private RoleProfile? changeRoleTo;
    [ObservableProperty] private string resetPasswordValue = string.Empty;

    [ObservableProperty] private string userMessage = string.Empty;
    [ObservableProperty] private bool userMessageIsError;

    partial void OnSelectedUserChanged(UserRowItem? value)
    {
        ChangeRoleTo = value?.RoleProfile;
        ResetPasswordValue = string.Empty;
    }

    private async Task LoadUsersAsync(int? reselectId = null)
    {
        try
        {
            var list = await _users().GetUsersAsync();
            var rows = list.Select(u => new UserRowItem(u.Id, u.Username, u.FullName, u.Role?.Type ?? RoleType.Cashier,
                u.IsActive, u.LastLoginAt, u.FailedLoginAttempts, u.LockoutEnd)).ToList();
            UiThread.Run(() =>
            {
                Users.Clear();
                foreach (var r in rows) Users.Add(r);
                TotalUsers = rows.Count;
                ActiveUsers = rows.Count(r => r.Status == UserStatusKind.Active);
                DisabledUsers = rows.Count(r => r.Status == UserStatusKind.Disabled);
                LockedUsers = rows.Count(r => r.Status == UserStatusKind.Locked);
                var keep = reselectId ?? SelectedUser?.Id;
                SelectedUser = Users.FirstOrDefault(u => u.Id == keep);
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

    private async Task<bool> RunUserActionAsync(Func<IUserManagementService, Task> action, string success, int? reselectId = null)
    {
        try
        {
            await action(_users());
            ShowUserMessage(success, false);
            await LoadUsersAsync(reselectId);
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
    private async Task ToggleActiveAsync()
    {
        if (SelectedUser is not { } user) return;
        if (user.IsActive)
        {
            var answer = Dialogs.Show(
                $"إيقاف حساب \"{user.DisplayName}\" يمنعه من الدخول إلى المنظومة حتى يُعاد تفعيله.\n\nهل تريد المتابعة؟",
                "إيقاف حساب", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }
        await RunUserActionAsync(s => s.SetActiveAsync(user.Id, !user.IsActive),
            user.IsActive ? $"تم إيقاف حساب \"{user.DisplayName}\"." : $"تم تفعيل حساب \"{user.DisplayName}\".", user.Id);
    }

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (SelectedUser is not { } user) return;
        if (string.IsNullOrWhiteSpace(ResetPasswordValue)) { ShowUserMessage("اكتب كلمة المرور المؤقتة الجديدة.", true); return; }
        if (await RunUserActionAsync(s => s.ResetPasswordAsync(user.Id, ResetPasswordValue),
                $"تمت إعادة تعيين كلمة مرور \"{user.DisplayName}\". سيُطلب منه تغييرها عند الدخول.", user.Id))
            ResetPasswordValue = string.Empty;
    }

    [RelayCommand]
    private async Task ChangeRoleAsync()
    {
        if (SelectedUser is not { } user) return;
        if (ChangeRoleTo is not { } role || role.Type == user.Role) { ShowUserMessage("اختر دوراً مختلفاً عن الدور الحالي.", true); return; }
        await RunUserActionAsync(s => s.ChangeRoleAsync(user.Id, role.Type), $"تم تغيير دور \"{user.DisplayName}\" إلى {role.Name}.", user.Id);
    }

    [RelayCommand]
    private async Task UnlockUserAsync()
    {
        if (SelectedUser is not { } user) return;
        await RunUserActionAsync(s => s.UnlockAsync(user.Id), $"تم فك قفل حساب \"{user.DisplayName}\" وتصفير المحاولات الفاشلة.", user.Id);
    }

    // ── نافذة إضافة مستخدم ──────────────────────────────────────────

    [ObservableProperty] private bool isAddUserOpen;
    [ObservableProperty] private string newUsername = string.Empty;
    [ObservableProperty] private string newFullName = string.Empty;
    [ObservableProperty] private string newPassword = string.Empty;
    [ObservableProperty] private RoleProfile? newRole;
    [ObservableProperty] private string addUserError = string.Empty;

    [RelayCommand]
    private void OpenAddUser()
    {
        NewUsername = string.Empty;
        NewFullName = string.Empty;
        NewPassword = string.Empty;
        NewRole = RoleProfiles.For(RoleType.Cashier);
        AddUserError = string.Empty;
        IsAddUserOpen = true;
    }

    [RelayCommand]
    private void CloseAddUser() => IsAddUserOpen = false;

    [RelayCommand]
    private void SelectNewRole(RoleProfile? role)
    {
        if (role != null) NewRole = role;
    }

    [RelayCommand]
    private async Task AddUserAsync()
    {
        var username = NewUsername.Trim();
        var role = NewRole?.Type ?? RoleType.Cashier;
        try
        {
            var created = await _users().CreateUserAsync(username, NewFullName, NewPassword, role);
            IsAddUserOpen = false;
            ShowUserMessage($"تمت إضافة \"{username}\" بدور {RoleProfiles.For(role).Name}. سيُطلب منه تغيير كلمة المرور عند أول دخول.", false);
            await LoadUsersAsync(created.Id);
            await SearchActivityAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            AddUserError = ex.Message;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "فشلت إضافة مستخدم");
            AddUserError = "حدث خطأ غير متوقع. التفاصيل في سجل الأخطاء.";
        }
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
