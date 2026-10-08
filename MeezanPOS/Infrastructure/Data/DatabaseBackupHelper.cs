using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace MeezanPOS.Infrastructure.Data;

/// <summary>
/// نسخ احتياطي واستعادة آمنة لقاعدة SQLite تعمل بنمط WAL.
/// - النسخ يتم عبر Backup API (يتضمن ما في ملف ‎-wal‎ ويعطي لقطة متسقة) وليس File.Copy.
/// - الاستعادة تفرغ مجمع الاتصالات وتحذف ملفي ‎-wal / -shm‎ القديمين حتى لا تُطبَّق تعديلات أحدث
///   على النسخة المسترجعة فتختلط البيانات أو يتلف الملف.
/// </summary>
public static class DatabaseBackupHelper
{
    private static readonly string[] RequiredTables = { "DailyJournals", "CashMovements", "Suppliers", "__EFMigrationsHistory" };

    /// <summary>نسخة احتياطية متسقة إلى ملف مؤقت ثم إعادة تسمية ذرية.</summary>
    public static void CreateBackup(string dbPath, string targetPath)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tempPath = targetPath + ".tmp";
        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);

            using (var source = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False"))
            using (var destination = new SqliteConnection($"Data Source={tempPath};Pooling=False"))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            if (File.Exists(targetPath)) File.Delete(targetPath);
            File.Move(tempPath, targetPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// التحقق من أن الملف قاعدة بيانات ميزان سليمة قبل استعادتها.
    /// </summary>
    /// <exception cref="InvalidDataException">برسالة عربية توضح السبب.</exception>
    public static void ValidateMeezanDatabase(string file)
    {
        if (!File.Exists(file))
            throw new InvalidDataException("الملف المحدد غير موجود.");

        try
        {
            using var connection = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
            connection.Open();

            using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA integrity_check;";
                var result = check.ExecuteScalar() as string;
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("ملف قاعدة البيانات تالف ولا يمكن استعادته.");
            }

            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) tables.Add(reader.GetString(0));
            }

            var missing = RequiredTables.Where(t => !tables.Contains(t)).ToList();
            if (missing.Count > 0)
                throw new InvalidDataException("الملف المحدد ليس نسخة احتياطية من منظومة ميزان.");
        }
        catch (SqliteException)
        {
            throw new InvalidDataException("الملف المحدد ليس قاعدة بيانات صالحة.");
        }
    }

    /// <summary>
    /// استبدال قاعدة البيانات الحالية بمحتوى ملف آخر بأمان.
    /// يجب ألا يكون هناك أي استخدام نشط لقاعدة البيانات أثناء الاستدعاء (يُغلق التطبيق بعده).
    /// </summary>
    public static void ReplaceDatabase(string sourceFile, string dbPath)
    {
        SqliteConnection.ClearAllPools();

        var tempPath = dbPath + ".restore.tmp";
        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            using (var source = new SqliteConnection($"Data Source={sourceFile};Mode=ReadOnly;Pooling=False"))
            using (var destination = new SqliteConnection($"Data Source={tempPath};Pooling=False"))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
            // ملفات WAL القديمة تخص القاعدة المحذوفة؛ تركها يجعل SQLite يطبقها على القاعدة المسترجعة
            TryDelete(dbPath + "-wal");
            TryDelete(dbPath + "-shm");
            File.Move(tempPath, dbPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// تدوير النسخ: الإبقاء على أحدث <paramref name="keepLatest"/> نسخ،
    /// بالإضافة إلى أحدث نسخة لكل يوم خلال آخر <paramref name="keepDays"/> يوماً
    /// (حتى لا تطرد إعادات التشغيل المتكررة آخر نسخة سليمة من الأيام السابقة).
    /// </summary>
    public static void PruneBackups(string backupDir, string searchPattern, int keepLatest = 10, int keepDays = 14)
    {
        if (!Directory.Exists(backupDir)) return;

        var files = Directory.GetFiles(backupDir, searchPattern)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();

        var keep = new HashSet<string>(files.Take(keepLatest).Select(f => f.FullName));
        var cutoff = DateTime.Now.Date.AddDays(-keepDays);
        foreach (var daily in files.Where(f => f.LastWriteTime >= cutoff).GroupBy(f => f.LastWriteTime.Date))
            keep.Add(daily.First().FullName);

        foreach (var file in files.Where(f => !keep.Contains(f.FullName)))
            TryDelete(file.FullName);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
