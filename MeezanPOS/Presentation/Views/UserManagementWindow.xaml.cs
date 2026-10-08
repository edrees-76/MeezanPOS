using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using Serilog;

namespace MeezanPOS.Presentation.Views;

public partial class UserManagementWindow : Window
{
    private sealed record UserRow(int Id, string Username, string FullName, RoleType? Role, bool IsActive, DateTime? LastLoginAt)
    {
        public string RoleName => Role.HasValue ? UserManagementService.RoleDisplayName(Role.Value) : "-";
        public string StatusText => IsActive ? "فعّال" : "موقوف";
        public string LastLoginText => LastLoginAt.HasValue ? LastLoginAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm") : "لم يدخل بعد";
    }

    private sealed record RoleOption(RoleType Type, string Name)
    {
        public override string ToString() => Name;
    }

    private static readonly RoleOption[] RoleOptions =
        Enum.GetValues<RoleType>().Select(r => new RoleOption(r, UserManagementService.RoleDisplayName(r))).ToArray();

    public UserManagementWindow()
    {
        InitializeComponent();
        CbRole.ItemsSource = RoleOptions;
        CbRole.SelectedItem = RoleOptions.First(r => r.Type == RoleType.Cashier);
        CbChangeRole.ItemsSource = RoleOptions;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private static IUserManagementService Service => AppServiceProvider.Resolve<IUserManagementService>();

    private async Task ReloadAsync()
    {
        try
        {
            var users = await Service.GetUsersAsync();
            UsersGrid.ItemsSource = users
                .Select(u => new UserRow(u.Id, u.Username, u.FullName, u.Role?.Type, u.IsActive, u.LastLoginAt))
                .ToList();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, isError: true);
        }
    }

    private UserRow? Selected => UsersGrid.SelectedItem as UserRow;

    private async Task RunAsync(Func<Task> action, string successMessage)
    {
        try
        {
            await action();
            ShowMessage(successMessage, isError: false);
            await ReloadAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            ShowMessage(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "User management operation failed");
            ShowMessage("حدث خطأ غير متوقع. التفاصيل في سجل الأخطاء.", isError: true);
        }
    }

    private async void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var role = (CbRole.SelectedItem as RoleOption)?.Type ?? RoleType.Cashier;
        var username = TxtUsername.Text;
        await RunAsync(() => Service.CreateUserAsync(username, TxtFullName.Text, TxtTempPassword.Text, role),
            $"تمت إضافة المستخدم \"{username.Trim()}\". سيُطلب منه تغيير كلمة المرور عند أول دخول.");
        if (MessageBorder.Tag is false)
        {
            TxtUsername.Clear();
            TxtFullName.Clear();
            TxtTempPassword.Clear();
        }
    }

    private async void ResetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } user) { ShowMessage("اختر مستخدماً من القائمة أولاً.", true); return; }
        if (string.IsNullOrWhiteSpace(TxtTempPassword.Text)) { ShowMessage("اكتب كلمة المرور المؤقتة في الخانة المخصصة.", true); return; }

        await RunAsync(() => Service.ResetPasswordAsync(user.Id, TxtTempPassword.Text),
            $"تمت إعادة تعيين كلمة مرور \"{user.Username}\". سيُطلب منه تغييرها عند الدخول.");
        if (MessageBorder.Tag is false) TxtTempPassword.Clear();
    }

    private async void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } user) { ShowMessage("اختر مستخدماً من القائمة أولاً.", true); return; }
        await RunAsync(() => Service.SetActiveAsync(user.Id, !user.IsActive),
            user.IsActive ? $"تم إيقاف حساب \"{user.Username}\"." : $"تم تفعيل حساب \"{user.Username}\".");
    }

    private async void ChangeRole_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } user) { ShowMessage("اختر مستخدماً من القائمة أولاً.", true); return; }
        if (CbChangeRole.SelectedItem is not RoleOption role) { ShowMessage("اختر الدور الجديد.", true); return; }
        await RunAsync(() => Service.ChangeRoleAsync(user.Id, role.Type),
            $"تم تغيير دور \"{user.Username}\" إلى {role.Name}.");
    }

    private void ShowMessage(string text, bool isError)
    {
        TxtMessage.Text = text;
        MessageBorder.Tag = isError;
        MessageBorder.Background = (Brush)FindResource(isError ? "Brush.ExpenseSoft" : "Brush.IncomeSoft");
        TxtMessage.Foreground = (Brush)FindResource(isError ? "Brush.ExpenseText" : "Brush.IncomeText");
        MessageBorder.Visibility = Visibility.Visible;
    }
}
