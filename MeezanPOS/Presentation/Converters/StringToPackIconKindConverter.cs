using System;
using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace MeezanPOS.Presentation.Converters;

public class StringToPackIconKindConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string iconName && !string.IsNullOrWhiteSpace(iconName))
        {
            if (Enum.TryParse<PackIconKind>(iconName, true, out var kind))
            {
                return kind;
            }
        }
        
        // Default fallback icon
        return PackIconKind.HelpCircleOutline;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
