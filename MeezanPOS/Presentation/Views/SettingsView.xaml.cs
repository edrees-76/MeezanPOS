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
        // DataContext يأتي من DataTemplate في App.xaml (الـ ViewModel الذي أنشأه MainViewModel).
        // إنشاء ViewModel ثانٍ هنا كان يكرر التحميل من قاعدة البيانات ويتجاهل نسخة MainViewModel.
    }
}
