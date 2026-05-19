using System.Windows.Controls;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

public partial class GeneralExpensesView : UserControl
{
    public GeneralExpensesView()
    {
        InitializeComponent();
        DataContext = new GeneralExpensesViewModel();
    }
}
