using System;
using System.Windows;
using System.Windows.Controls;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

public partial class ClosingAccountView : UserControl
{
    public ClosingAccountView()
    {
        InitializeComponent();
    }

    private void btnExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }
}

public class IndexToSequenceConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is int index)
        {
            return (index + 1).ToString();
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

