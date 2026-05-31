using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Presentation.Views;

public partial class PartnerStatementWindow : Window
{
    private readonly PartnerStatementViewModel _vm;

    public PartnerStatementWindow(string partnerName)
    {
        InitializeComponent();

        // إنشاء الخدمات والـ ViewModel
        var ownerDebtService = AppServiceProvider.Resolve<IOwnerDebtService>();
        _vm = new PartnerStatementViewModel(ownerDebtService);

        DataContext = _vm;

        Loaded += async (s, e) => await _vm.LoadStatementAsync(partnerName);
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }



    /// <summary>
    /// النقر المزدوج لفتح المستند المصدر (فاتورة مورد أو مصروف عام)
    /// </summary>
    private void StatementGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid) return;
        if (dataGrid.SelectedItem is not PartnerStatementEntryDto entry) return;

        // فتح نافذة التفاصيل إذا وجد مُعرف للمصدر
        if (entry.SourceId.HasValue)
        {
            var detailsWindow = new TransactionDetailsViewWindow(entry.SourceType ?? "OwnerDebt", entry.SourceId.Value)
            {
                Owner = this
            };
            detailsWindow.ShowDialog();
        }
    }
}
