namespace MeezanPOS.Application.Services;

/// <summary>
/// مساعد التوقيت الموحد — UTC للتخزين، Libya +2 للعرض
/// </summary>
public static class DateTimeHelper
{
    /// <summary>التوقيت الحالي للتخزين (UTC دائماً)</summary>
    public static DateTime UtcNow => DateTime.UtcNow;

    /// <summary>تحويل للعرض في الواجهة (توقيت ليبيا +2)</summary>
    public static DateTime ToLocal(DateTime utc)
    {
        try
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utc,
                TimeZoneInfo.FindSystemTimeZoneById("Libya Standard Time"));
        }
        catch
        {
            // Fallback: إزاحة ثابتة +2
            return utc.AddHours(2);
        }
    }

    /// <summary>التاريخ المحلي الحالي (للعرض وأسماء الملفات فقط)</summary>
    public static DateTime LocalNow => ToLocal(UtcNow);
}
