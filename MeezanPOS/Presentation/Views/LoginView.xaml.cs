// =========================================================
// File: LoginView.xaml.cs
// Project: Mizan Restaurants System
// Technology: WPF + .NET 8 + XAML
// =========================================================

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views
{
    public partial class LoginView : Window
    {
        private bool _isPasswordVisible = false;

        public LoginView()
        {
            InitializeComponent();
            
            // Set initial watermark visibility based on potential saved settings loaded in VM
            Loaded += (s, e) =>
            {
                if (DataContext is LoginViewModel vm)
                {
                    txtUsernameWatermark.Visibility = string.IsNullOrEmpty(vm.Username) ? Visibility.Visible : Visibility.Collapsed;
                }
            };

            // Register StateChanged event to update layout background
            this.StateChanged += (s, e) => UpdateWindowLayout();
        }

        // =========================================================
        // Login Button Event
        // =========================================================

        private async void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text;
            string password = _isPasswordVisible ? txtVisiblePassword.Text : txtPassword.Password;

            // Validate Username

            if (string.IsNullOrWhiteSpace(username))
            {
                Dialogs.Show(
                    "يرجى إدخال اسم المستخدم",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                txtUsername.Focus();
                return;
            }

            // Validate Password

            if (string.IsNullOrWhiteSpace(password))
            {
                Dialogs.Show(
                    "يرجى إدخال كلمة المرور",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                if (_isPasswordVisible)
                    txtVisiblePassword.Focus();
                else
                    txtPassword.Focus();
                return;
            }

            // Execute Login logic in ViewModel
            if (DataContext is LoginViewModel vm)
            {
                vm.Username = username;
                vm.Password = password;

                if (vm.LoginCommand.CanExecute(null))
                {
                    // انتظار اكتمال الدخول قبل فحص النتيجة (كان يُفحص الخطأ قبل انتهاء المصادقة)
                    try
                    {
                        btnLogin.IsEnabled = false;
                        await vm.LoginCommand.ExecuteAsync(null);
                    }
                    finally
                    {
                        btnLogin.IsEnabled = true;
                    }

                    // Show error if authentication fails
                    if (vm.HasError)
                    {
                        Dialogs.Show(
                            vm.ErrorMessage,
                            "خطأ في الدخول",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    else
                    {
                        vm.Password = string.Empty;
                        txtPassword.Clear();
                        txtVisiblePassword.Clear();
                    }
                }
            }
        }

        // =========================================================
        // Window Control Actions
        // =========================================================

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        private void UpdateWindowLayout()
        {
            if (this.WindowState == WindowState.Maximized)
            {
                if (FullScreenBackground != null)
                {
                    FullScreenBackground.Visibility = Visibility.Visible;
                }
                this.Background = System.Windows.Media.Brushes.Transparent;
            }
            else
            {
                if (FullScreenBackground != null)
                {
                    FullScreenBackground.Visibility = Visibility.Collapsed;
                }
                this.Background = System.Windows.Media.Brushes.Transparent;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && this.WindowState == WindowState.Normal)
            {
                this.DragMove();
            }
        }

        // =========================================================
        // Watermark Toggles
        // =========================================================

        private void TxtUsername_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtUsernameWatermark != null)
            {
                txtUsernameWatermark.Visibility = string.IsNullOrEmpty(txtUsername.Text) 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdatePasswordWatermark();

            // Sync with ViewModel
            if (txtPassword.Visibility == Visibility.Visible && DataContext is LoginViewModel vm)
            {
                vm.Password = txtPassword.Password;
            }
        }

        private void TxtVisiblePassword_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePasswordWatermark();

            // Sync with ViewModel
            if (txtVisiblePassword.Visibility == Visibility.Visible && DataContext is LoginViewModel vm)
            {
                vm.Password = txtVisiblePassword.Text;
            }
        }

        private void UpdatePasswordWatermark()
        {
            if (txtPasswordWatermark != null)
            {
                string currentPassword = _isPasswordVisible ? txtVisiblePassword.Text : txtPassword.Password;
                txtPasswordWatermark.Visibility = string.IsNullOrEmpty(currentPassword) 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        }

        // =========================================================
        // Password Visibility Toggle
        // =========================================================

        private void BtnTogglePassword_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;

            if (_isPasswordVisible)
            {
                // Show password
                txtVisiblePassword.Text = txtPassword.Password;
                txtPassword.Visibility = Visibility.Collapsed;
                txtVisiblePassword.Visibility = Visibility.Visible;
                txtVisiblePassword.Focus();
                txtVisiblePassword.CaretIndex = txtVisiblePassword.Text.Length;

                // Find eye off icon dynamically from template
                var packIcon = btnTogglePassword.Template.FindName("iconTogglePassword", btnTogglePassword) as MaterialDesignThemes.Wpf.PackIcon;
                if (packIcon != null)
                {
                    packIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EyeOffOutline;
                }
            }
            else
            {
                // Hide password
                txtPassword.Password = txtVisiblePassword.Text;
                txtVisiblePassword.Visibility = Visibility.Collapsed;
                txtPassword.Visibility = Visibility.Visible;
                txtPassword.Focus();

                // Find eye icon dynamically from template
                var packIcon = btnTogglePassword.Template.FindName("iconTogglePassword", btnTogglePassword) as MaterialDesignThemes.Wpf.PackIcon;
                if (packIcon != null)
                {
                    packIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EyeOutline;
                }
            }
        }
    }
}
