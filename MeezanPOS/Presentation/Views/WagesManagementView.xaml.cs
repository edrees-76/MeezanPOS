using System.Windows.Controls;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

/// <summary>
/// Interaction logic for WagesManagementView.xaml
/// </summary>
public partial class WagesManagementView : UserControl
{
    public WagesManagementView()
    {
        InitializeComponent();
        DataContext = new WagesManagementViewModel();
    }
}
