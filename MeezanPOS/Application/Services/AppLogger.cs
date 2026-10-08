using System;
using System.IO;
using Serilog;

namespace MeezanPOS.Application.Services;

/// <summary>
/// تهيئة نظام التسجيل المركزي باستخدام Serilog
/// </summary>
public static class AppLogger
{
    public static void Initialize()
    {
        // بجوار قاعدة البيانات: نسخة التجربة (MEEZANPOS_DATA_DIR) لا تكتب في سجلات البيانات الحقيقية
        var logDir = Path.Combine(
            Path.GetDirectoryName(MeezanPOS.Infrastructure.Data.AppDbContext.GetDatabasePath())!,
            "Logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDir, "meezan-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Debug()
            .CreateLogger();
    }
}
