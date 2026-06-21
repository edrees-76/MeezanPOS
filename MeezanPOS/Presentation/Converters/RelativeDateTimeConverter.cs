using System;
using System.Globalization;
using System.Windows.Data;

namespace MeezanPOS.Presentation.Converters;

public class RelativeDateTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "الآن";
            if (timeSpan.TotalMinutes < 60)
                return $"منذ {Math.Max(1, (int)timeSpan.TotalMinutes)} دقيقة";
            if (timeSpan.TotalHours < 24)
            {
                int hours = (int)timeSpan.TotalHours;
                if (hours == 1) return "منذ ساعة";
                if (hours == 2) return "منذ ساعتين";
                return $"منذ {hours} ساعات";
            }
            if (timeSpan.TotalDays < 2)
                return "أمس";
            if (timeSpan.TotalDays < 7)
            {
                int days = (int)timeSpan.TotalDays;
                if (days == 2) return "منذ يومين";
                return $"منذ {days} أيام";
            }
            return dateTime.ToString("yyyy-MM-dd HH:mm");
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
