using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Presentation.Views;

public partial class SalesView : UserControl
{
    public SalesView()
    {
        InitializeComponent();
    }

    private void CashMovementsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid) return;
        if (dataGrid.SelectedItem is not CashMovement movement) return;

        if (!string.IsNullOrEmpty(movement.SourceType) && movement.SourceId.HasValue)
        {
            var window = Window.GetWindow(this);
            var detailsDialog = new TransactionDetailsViewWindow(movement.SourceType, movement.SourceId.Value)
            {
                Owner = window
            };
            detailsDialog.ShowDialog();
        }
    }
}
