using System.Windows.Controls;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

public partial class SupplierListView : UserControl
{
    public SupplierListView()
    {
        InitializeComponent();
        this.KeyDown += SupplierListView_KeyDown;
    }

    private void SupplierListView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            if (DataContext is SupplierListViewModel vm)
            {
                if (vm.IsFormOpen)
                {
                    if (vm.CloseFormCommand.CanExecute(null))
                    {
                        vm.CloseFormCommand.Execute(null);
                        e.Handled = true;
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(vm.SearchText))
                    {
                        vm.SearchText = string.Empty;
                        if (vm.LoadSuppliersCommand.CanExecute(null))
                        {
                            vm.LoadSuppliersCommand.Execute(null);
                        }
                        e.Handled = true;
                    }
                }
            }
        }
    }
}
