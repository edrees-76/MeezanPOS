using System.Windows;
using System.Windows.Input;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

/// <summary>شاشة اختيار المطعم قبل تسجيل الدخول.</summary>
public partial class RestaurantPickerWindow : Window
{
    private readonly RestaurantPickerViewModel _vm;

    public RestaurantPickerWindow(RestaurantPickerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.CloseRequested += opened => DialogResult = opened;
        vm.PasswordsCleared += () =>
        {
            TxtOwner.Clear();
            TxtNew.Clear();
            TxtConfirm.Clear();
        };
    }

    private void Owner_PasswordChanged(object sender, RoutedEventArgs e) => _vm.OwnerPassword = TxtOwner.Password;
    private void New_PasswordChanged(object sender, RoutedEventArgs e) => _vm.NewOwnerPassword = TxtNew.Password;
    private void Confirm_PasswordChanged(object sender, RoutedEventArgs e) => _vm.ConfirmOwnerPassword = TxtConfirm.Password;

    private void Restaurants_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_vm.IsEditing && _vm.OpenCommand.CanExecute(null))
            _vm.OpenCommand.Execute(null);
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
