using System.Windows;
using System.Windows.Controls;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

public partial class FreeOrdersReturnsView : UserControl
{
    public FreeOrdersReturnsView()
    {
        InitializeComponent();
        // DataContext يأتي من DataTemplate في App.xaml (الـ ViewModel الذي أنشأه MainViewModel).
        // إنشاء ViewModel ثانٍ هنا كان يكرر التحميل من قاعدة البيانات ويتجاهل نسخة MainViewModel.
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is FreeOrdersReturnsViewModel vm
            && vm.LoadedCommand.CanExecute(null))
        {
            vm.LoadedCommand.Execute(null);
        }
    }
}
