using System;
using System.IO;
using Serilog;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Application.Services;

/// <summary>
/// خدمة النسخ الاحتياطي اليدوي قبل العمليات الخطرة
/// </summary>
public class BackupService
{
    /// <summary>
    /// نسخة احتياطية يدوية قبل العمليات الخطرة (ترحيل، تعديل بنية القاعدة)
    /// </summary>
    public static string CreateManualBackup(string reason)
    {
        var dbPath = AppDbContext.GetDatabasePath();
        var backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "Backups");
        Directory.CreateDirectory(backupDir);

        var sanitizedReason = new string(reason
            .Replace(" ", "_")
            .Take(30)
            .ToArray());
        var backupPath = Path.Combine(backupDir,
            $"Meezan_MANUAL_{sanitizedReason}_{DateTime.Now:yyyyMMdd_HHmmss}.db");

        File.Copy(dbPath, backupPath, true);
        Log.Information("نسخة احتياطية يدوية: {Reason} → {Path}", reason, backupPath);
        return backupPath;
    }
}
