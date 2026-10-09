using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MeezanPOS.Presentation.Converters;

/// <summary>
/// نسبة بين 0 و1 إلى عرض عمود نسبي: الجزء الممتلئ من العمود، أو الباقي مع ConverterParameter="rest".
/// يُستخدم لرسم الأعمدة الأفقية دون مكتبة رسوم.
/// </summary>
public class RatioToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double ratio = value is double d && !double.IsNaN(d) ? Math.Clamp(d, 0, 1) : 0;
        if (parameter as string == "rest")
            ratio = 1 - ratio;
        // صفر نجمي يخفي العمود تماماً؛ قيمة صغيرة جداً تبقيه بلا عرض ظاهر
        return new GridLength(Math.Max(ratio, 0.0001), GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
