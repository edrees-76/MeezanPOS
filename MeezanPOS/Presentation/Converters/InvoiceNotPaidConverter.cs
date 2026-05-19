using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Presentation.Converters;

public class InvoiceNotPaidConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is InvoiceStatus status)
        {
            return status != InvoiceStatus.Paid;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
