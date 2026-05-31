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

    private void LoadSettings()
    {
        var settingsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "user.settings");
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
        var settingsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "user.settings");
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

            // فحص MustChangePassword
            if (user.MustChangePassword)
            {
                MessageBox.Show("يجب تغيير كلمة المرور عند أول دخول.", "تنبيه أمني",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            var mainView = new Presentation.Views.MainView();
            System.Windows.Application.Current.MainWindow = mainView;
            mainView.Show();

            // Close all other windows (like LoginView)
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window != mainView)
                {
                    window.Close();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "خطأ أثناء تسجيل الدخول");
            ErrorMessage = "حدث خطأ أثناء تسجيل الدخول. الرجاء المحاولة مرة أخرى.";
        }
    }
}
