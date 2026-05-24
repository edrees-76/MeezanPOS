using System.Windows;
using System.Windows.Input;

namespace MeezanPOS.Presentation.Views;

public partial class AddGeneralExpenseDialog : Window
{
    public AddGeneralExpenseDialog()
    {
        InitializeComponent();
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
