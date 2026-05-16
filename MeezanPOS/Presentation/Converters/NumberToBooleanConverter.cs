using System;
using System.Globalization;
using System.Windows.Data;

namespace MeezanPOS.Presentation.Converters;

public class NumberToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
        {
            return d < 0; // True if shortage (negative)
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
