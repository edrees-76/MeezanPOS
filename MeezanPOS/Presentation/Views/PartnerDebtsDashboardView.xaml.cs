using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

public partial class PartnerDebtsDashboardView : UserControl
{
    public PartnerDebtsDashboardView()
    {
        InitializeComponent();
    }

    private void PartnerCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is PartnerSummaryDto partner)
        {
            var statementWindow = new PartnerStatementWindow(partner.PartnerName)
            {
                Owner = Window.GetWindow(this)
            };
            statementWindow.ShowDialog();

            // تحديث الداشبورد بعد إغلاق نافذة الكشف
            if (DataContext is PartnerDebtsDashboardViewModel vm)
            {
                _ = vm.LoadDashboardAsync();
            }
        }
    }

    private void PartnerGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid dataGrid && dataGrid.SelectedItem is PartnerSummaryDto partner)
        {
            var statementWindow = new PartnerStatementWindow(partner.PartnerName)
            {
                Owner = Window.GetWindow(this)
            };
            statementWindow.ShowDialog();

            // تحديث الداشبورد بعد إغلاق نافذة الكشف
            if (DataContext is PartnerDebtsDashboardViewModel vm)
            {
                _ = vm.LoadDashboardAsync();
            }
        }
    }
}
