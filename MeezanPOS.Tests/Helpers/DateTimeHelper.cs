using System;

namespace MeezanPOS.Tests.Helpers;

public static class DateTimeHelper
{
    public static DateTime Today => DateTime.Today;
    public static DateTime Yesterday => DateTime.Today.AddDays(-1);
    public static DateTime LastMonth => DateTime.Today.AddMonths(-1);
    public static DateTime StartOfMonth => 
        new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    public static DateTime EndOfMonth => StartOfMonth.AddMonths(1).AddDays(-1);
}
