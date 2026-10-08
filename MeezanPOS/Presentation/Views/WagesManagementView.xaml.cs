using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Application.Interfaces;

namespace MeezanPOS.Presentation.Views;

/// <summary>
/// Interaction logic for WagesManagementView.xaml
/// </summary>
public partial class WagesManagementView : UserControl
{
    public WagesManagementView()
    {
        InitializeComponent();
        // DataContext يأتي من DataTemplate في App.xaml (الـ ViewModel الذي أنشأه MainViewModel).
        // إنشاء ViewModel ثانٍ هنا كان يكرر التحميل من قاعدة البيانات ويتجاهل نسخة MainViewModel.
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is WagesManagementViewModel vm && vm.LoadedCommand.CanExecute(null))
        {
            vm.LoadedCommand.Execute(null);
        }
    }

    /// <summary>
    /// يفتح نافذة كشف حساب العامل عند النقر المزدوج على صف العامل
    /// </summary>
    private void WorkerRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            WorkerWageSummary? summary = element.DataContext as WorkerWageSummary ?? element.Tag as WorkerWageSummary;
            if (summary != null)
            {
                if (DataContext is WagesManagementViewModel vm && vm.OpenWorkerStatementCommand.CanExecute(summary))
                {
                    vm.OpenWorkerStatementCommand.Execute(summary);
                }
            }
        }
    }
}
