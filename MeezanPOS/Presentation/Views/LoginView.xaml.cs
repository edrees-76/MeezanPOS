using System.Windows;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

public partial class LoginView : Window
{
    public LoginView()
    {
        InitializeComponent();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (txtPassword.Visibility == Visibility.Visible && DataContext is LoginViewModel vm)
        {
            vm.Password = txtPassword.Password;
        }
    }

    private void TxtVisiblePassword_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (txtVisiblePassword.Visibility == Visibility.Visible && DataContext is LoginViewModel vm)
        {
            vm.Password = txtVisiblePassword.Text;
        }
    }

    private bool _isPasswordVisible = false;

    private void BtnTogglePassword_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;

        if (_isPasswordVisible)
        {
            // Show Password
            txtVisiblePassword.Text = txtPassword.Password;
            txtPassword.Visibility = Visibility.Collapsed;
            txtVisiblePassword.Visibility = Visibility.Visible;
            iconTogglePassword.Kind = MaterialDesignThemes.Wpf.PackIconKind.EyeOffOutline;
            txtVisiblePassword.Focus();
            txtVisiblePassword.CaretIndex = txtVisiblePassword.Text.Length;
        }
        else
        {
            // Hide Password
            txtPassword.Password = txtVisiblePassword.Text;
            txtVisiblePassword.Visibility = Visibility.Collapsed;
            txtPassword.Visibility = Visibility.Visible;
            iconTogglePassword.Kind = MaterialDesignThemes.Wpf.PackIconKind.EyeOutline;
            txtPassword.Focus();
        }
    }
}
