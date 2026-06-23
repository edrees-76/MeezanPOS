using System.Windows.Controls;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

public partial class SupplierDetailsView : UserControl
{
    public SupplierDetailsView()
    {
        InitializeComponent();
        this.KeyDown += SupplierDetailsView_KeyDown;
    }

    private void SupplierDetailsView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            if (DataContext is SupplierDetailsViewModel vm)
            {
                if (vm.IsInvoiceDetailsOpen)
                {
                    if (vm.CloseInvoiceDetailsCommand.CanExecute(null))
                    {
                        vm.CloseInvoiceDetailsCommand.Execute(null);
                        e.Handled = true;
                    }
                }
                else if (vm.IsInvoiceFormOpen)
                {
                    if (vm.CloseInvoiceFormCommand.CanExecute(null))
                    {
                        vm.CloseInvoiceFormCommand.Execute(null);
                        e.Handled = true;
                    }
                }
                else if (vm.IsPaymentFormOpen)
                {
                    if (vm.ClosePaymentFormCommand.CanExecute(null))
                    {
                        vm.ClosePaymentFormCommand.Execute(null);
                        e.Handled = true;
                    }
                }
                else if (vm.IsPrintSelectionOpen)
                {
                    if (vm.ClosePrintSelectionCommand.CanExecute(null))
                    {
                        vm.ClosePrintSelectionCommand.Execute(null);
                        e.Handled = true;
                    }
                }
                else
                {
                    if (vm.GoBackCommand.CanExecute(null))
                    {
                        vm.GoBackCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
        }
    }
}
