using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MeezanPOS.Presentation.Converters;

public class AlertColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hexColor && !string.IsNullOrWhiteSpace(hexColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                string param = parameter as string ?? string.Empty;

                if (param.Equals("background", StringComparison.OrdinalIgnoreCase))
                {
                    // Low opacity background (~10%)
                    return new SolidColorBrush(Color.FromArgb(25, color.R, color.G, color.B));
                }
                else
                {
                    // Full opacity border/foreground
                    return new SolidColorBrush(color);
                }
            }
            catch
            {
                // Fallback
            }
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
