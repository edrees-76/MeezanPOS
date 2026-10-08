using System;
using System.Windows;
using System.Windows.Input;
using MeezanPOS.Application.Services;
using Serilog;

namespace MeezanPOS.Presentation.Views;

/// <summary>
/// نافذة تغيير كلمة المرور.
/// في الوضع الإجباري (أول دخول أو بعد إعادة تعيين المدير) لا يمكن إغلاقها دون تغيير كلمة المرور؛
/// الإلغاء يعني عدم الدخول للمنظومة.
/// </summary>
public partial class ChangePasswordDialog : Window
{
    private readonly bool _isForced;

    public ChangePasswordDialog(bool isForced)
    {
        InitializeComponent();
        _isForced = isForced;

        TxtRules.Text = $"يجب ألا تقل عن {UserManagementService.MinPasswordLength} أحرف، وألا تطابق اسم المستخدم.";
        if (isForced)
        {
            TxtIntro.Text = "لحماية حسابك يجب تغيير كلمة المرور قبل المتابعة. هذه كلمة مرور مؤقتة أو افتراضية.";
            BtnCancel.Content = "خروج";
        }

        Loaded += (_, _) => TxtCurrent.Focus();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorBox.Visibility = Visibility.Collapsed;

        if (TxtNew.Password != TxtConfirm.Password)
        {
            ShowError("كلمة المرور الجديدة وتأكيدها غير متطابقين.");
            return;
        }

        BtnSave.IsEnabled = false;
        try
        {
            var service = AppServiceProvider.Resolve<IUserManagementService>();
            await service.ChangeOwnPasswordAsync(TxtCurrent.Password, TxtNew.Password);

            Dialogs.Show("تم تغيير كلمة المرور بنجاح.", "تم", MessageBoxButton.OK, MessageBoxImage.Information,
                MessageBoxResult.OK, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
            DialogResult = true;
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error changing password");
            ShowError("حدث خطأ غير متوقع أثناء تغيير كلمة المرور.");
        }
        finally
        {
            BtnSave.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        ErrorBox.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { }
        }
    }
}
