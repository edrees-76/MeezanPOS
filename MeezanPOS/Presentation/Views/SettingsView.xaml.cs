using System.Windows.Controls;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

/// <summary>
/// Interaction logic for SettingsView.xaml
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
    }
}
