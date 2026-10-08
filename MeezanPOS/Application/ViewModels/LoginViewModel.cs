using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using Serilog;
using System.Windows;

namespace MeezanPOS.Application.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private bool rememberMe;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    public LoginViewModel()
    {
        LoadSettings();
    }

    /// <summary>
    /// ملف "تذكرني" في مجلد بيانات المستخدم وليس بجوار البرنامج
    /// (مجلد Program Files للقراءة فقط بعد التثبيت فيفشل الحفظ بصمت).
    /// </summary>
    private static string SettingsFilePath => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(MeezanPOS.Infrastructure.Data.AppDbContext.GetDatabasePath())!,
        "user.settings");

    private void LoadSettings()
    {
        var settingsPath = SettingsFilePath;
        if (System.IO.File.Exists(settingsPath))
        {
            try
            {
                var content = System.IO.File.ReadAllText(settingsPath);
                var parts = content.Split('|');
                if (parts.Length >= 2 && parts[0] == "1")
                {
                    RememberMe = true;
                    Username = parts[1];
                }
            }
            catch { }
        }
    }

    private void SaveSettings()
    {
        var settingsPath = SettingsFilePath;
        try
        {
            if (RememberMe)
            {
                System.IO.File.WriteAllText(settingsPath, $"1|{Username}");
            }
            else
            {
                if (System.IO.File.Exists(settingsPath))
                {
                    System.IO.File.Delete(settingsPath);
                }
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "الرجاء إدخال اسم المستخدم وكلمة المرور.";
            return;
        }

        try
        {
            var authService = AppServiceProvider.Resolve<IAuthenticationService>();
            var user = await authService.AuthenticateAsync(Username, Password);

            if (user == null)
            {
                ErrorMessage = "بيانات الدخول غير صحيحة أو الحساب مقفل.";
                return;
            }

            // تسجيل الجلسة
            var session = AppServiceProvider.Resolve<ISessionService>();
            session.SetUser(user);

            SaveSettings();

            // كلمة مرور افتراضية أو مؤقتة: لا دخول قبل تغييرها
            // تُفرض أيضاً إذا كانت كلمة المرور الحالية ضعيفة (مثل admin/admin الافتراضية)
            if (user.MustChangePassword || UserManagementService.ValidatePassword(Password, user.Username) != null)
            {
                if (!AppWindows.Current.ChangePassword(forced: true))
                {
                    session.ClearSession();
                    ErrorMessage = "يجب تغيير كلمة المرور للمتابعة.";
                    return;
                }
            }

            AppWindows.Current.ShowMainShell();

            // تحقق صامت من وجود إصدار أحدث (مرة يومياً، لا يعطل الدخول)
            _ = UpdateChecker.NotifyIfNewerAsync(manual: false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "خطأ أثناء تسجيل الدخول");
            ErrorMessage = "حدث خطأ أثناء تسجيل الدخول. الرجاء المحاولة مرة أخرى.";
        }
    }
}
