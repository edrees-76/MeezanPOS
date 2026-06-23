using System.Windows.Controls;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

public partial class GeneralExpensesView : UserControl
{
    public GeneralExpensesView()
    {
        InitializeComponent();
        DataContext = new GeneralExpensesViewModel();
        this.KeyDown += GeneralExpensesView_KeyDown;
    }

    private void GeneralExpensesView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            if (DataContext is GeneralExpensesViewModel vm)
            {
                if (vm.ArchiveLevel == 1)
                {
                    if (vm.GoBackToMonthsCommand.CanExecute(null))
                    {
                        vm.GoBackToMonthsCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
        }
    }
}
