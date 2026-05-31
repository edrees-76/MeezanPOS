using System.Windows;
using System.Linq;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

/// <summary>
/// Interaction logic for WorkerStatementWindow.xaml
/// </summary>
public partial class WorkerStatementWindow : Window
{
    private readonly WagesManagementViewModel _vm;

    public WorkerStatementWindow(WorkerWageSummary summary)
    {
        InitializeComponent();

        _vm = new WagesManagementViewModel();
        DataContext = _vm;

        Loaded += (s, e) =>
        {
            var foundSummary = _vm.WorkerSummaries.FirstOrDefault(w => w.WorkerId == summary.WorkerId);
            if (foundSummary != null)
            {
                _vm.SelectedWorkerSummary = foundSummary;
            }
            else
            {
                _vm.SelectedWorkerSummary = summary;
            }
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public class IncrementConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is int val)
        {
            return val + 1;
        }
        return value;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new System.NotImplementedException();
    }
}
