using System.Windows;
using System.Windows.Controls;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

public partial class DailyJournalView : UserControl
{
    public DailyJournalView()
    {
        InitializeComponent();
        this.KeyDown += DailyJournalView_KeyDown;
    }

    private void DailyJournalView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            if (DataContext is DailyJournalViewModel vm)
            {
                if (vm.ClearFormCommand.CanExecute(null))
                {
                    vm.ClearFormCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}
