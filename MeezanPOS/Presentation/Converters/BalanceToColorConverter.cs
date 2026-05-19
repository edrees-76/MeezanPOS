using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MeezanPOS.Presentation.Converters;

public class BalanceToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal balance)
        {
            // إذا كان الرصيد أكبر من 0 (دين على المطعم)، نلونه بالأحمر للتنبيه
            if (balance > 0)
                return new SolidColorBrush(Colors.Red);
            
            // إذا كان 0 أو أقل (لا يوجد دين أو رصيد مدفوع مقدماً)، نلونه بالأسود (أو أخضر)
            if (balance < 0)
                return new SolidColorBrush(Colors.Green);
        }

        return new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
