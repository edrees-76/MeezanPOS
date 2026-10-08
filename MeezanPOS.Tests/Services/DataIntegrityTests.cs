using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Tests.Helpers;

namespace MeezanPOS.Tests.Services;

/// <summary>
/// اختبارات سلامة البيانات: قفل الفترات، التراجع عن المعاملات، النسخ الاحتياطي والاستعادة.
/// </summary>
public class DataIntegrityTests
{
    private const string Reason = "سبب اختباري معتمد لفك الترحيل المالي للوردية";

    private static (AppDbContext Context, PostingService Posting) CreatePosting()
    {
        var context = SqliteTestDbContextFactory.Create();
        var session = new Mock<ISessionService>();
        session.Setup(s => s.CurrentUser).Returns(new User { Id = 1, Username = "admin" });
        var audit = new AuditService(context);
        var cash = new CashLedgerService(context, session.Object);
        return (context, new PostingService(context, cash, audit));
    }

    private static async Task<DailyJournal> AddJournalAsync(AppDbContext context, DateTime date)
    {
        var journal = TestDataBuilder.BuildDailyJournal(date, FinancialStatus.Draft, 1000m, 0m);
        journal.ActualCash = 800m;
        journal.CashFloat = 300m;
        context.DailyJournals.Add(journal);
        await context.SaveChangesAsync();
        return journal;
    }

    private static async Task<PostingSession> AddSettlementAsync(AppDbContext context, DateTime start, DateTime end, PostingSessionStatus status)
    {
        var settlement = new PostingSession
        {
            CreatedBy = "1",
            PeriodStartDate = start,
            PeriodEndDate = end,
            SessionType = PostingSessionType.Settlement,
            Status = status
        };
        context.PostingSessions.Add(settlement);
        await context.SaveChangesAsync();
        return settlement;
    }

    // ---------- قفل الفترات ----------

    [Fact]
    public async Task Unpost_IndividuallyPostedJournalInsideSettledPeriod_IsBlocked()
    {
        var (context, posting) = CreatePosting();
        var day = DateTime.Today.AddDays(-3);
        var journal = await AddJournalAsync(context, day);
        await posting.PostEntityAsync<DailyJournal>(journal.Id, "1"); // ترحيل فردي: لا PostingSessionId
        await AddSettlementAsync(context, day.AddDays(-1), day.AddDays(1), PostingSessionStatus.Settled);

        Func<Task> act = () => posting.UnpostEntityAsync<DailyJournal>(journal.Id, Reason, "1");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*فترة مقفلة*");
    }

    [Fact]
    public async Task Post_DraftDatedInsideSettledPeriod_IsBlocked()
    {
        var (context, posting) = CreatePosting();
        var day = DateTime.Today.AddDays(-3);
        await AddSettlementAsync(context, day.AddDays(-1), day.AddDays(1), PostingSessionStatus.ReSettled);
        var journal = await AddJournalAsync(context, day);

        Func<Task> act = () => posting.PostEntityAsync<DailyJournal>(journal.Id, "1");

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await context.CashMovements.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task UnlockedPeriod_AllowsUnpost_AndOutsideDatesAreOpen()
    {
        var (context, posting) = CreatePosting();
        var day = DateTime.Today.AddDays(-3);
        var journal = await AddJournalAsync(context, day);
        await posting.PostEntityAsync<DailyJournal>(journal.Id, "1");
        await AddSettlementAsync(context, day.AddDays(-1), day.AddDays(1), PostingSessionStatus.Unlocked);

        (await posting.UnpostEntityAsync<DailyJournal>(journal.Id, Reason, "1")).Should().BeTrue();
        (await PeriodLock.IsDateLockedAsync(context, day.AddDays(5))).Should().BeFalse();
    }

    // ---------- التراجع عن المعاملة ----------

    [Fact]
    public async Task Rollback_ClearsTrackedChanges_SoLaterSavesDoNotPersistThem()
    {
        using var context = SqliteTestDbContextFactory.Create();
        var supplier = TestDataBuilder.BuildSupplier("مورد");
        supplier.CurrentBalance = 100m;
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        await using (var tx = await context.Database.BeginOrJoinTransactionAsync())
        {
            supplier.CurrentBalance += 500m; // تعديل ضمن عملية ستفشل
            await context.SaveChangesAsync();
            await tx.RollbackAsync();
        }

        // حفظ لاحق غير مرتبط يجب ألا يعيد كتابة الرصيد المتضخم
        context.Settings.Add(new Setting { Key = "x", Value = "y" });
        await context.SaveChangesAsync();

        var balance = await context.Suppliers.AsNoTracking().Where(s => s.Id == supplier.Id).Select(s => s.CurrentBalance).FirstAsync();
        balance.Should().Be(100m);
    }

    [Fact]
    public async Task JoinedTransaction_DoesNotCommitOrRollBackTheOuterOne()
    {
        using var context = SqliteTestDbContextFactory.Create();
        await using var outer = await context.Database.BeginOrJoinTransactionAsync();
        await using var inner = await context.Database.BeginOrJoinTransactionAsync();

        outer.IsOwner.Should().BeTrue();
        inner.IsOwner.Should().BeFalse();
        await inner.CommitAsync();
        context.Database.CurrentTransaction.Should().NotBeNull();
    }

    // ---------- النسخ الاحتياطي والاستعادة ----------

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MeezanPOS.Tests", "backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long Count(string file, string table)
    {
        using var c = new SqliteConnection($"Data Source={file};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)cmd.ExecuteScalar()!;
    }

    private static string CreateMeezanLikeDb(string path, int journals)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        Exec(c, "CREATE TABLE DailyJournals(Id INTEGER PRIMARY KEY); CREATE TABLE CashMovements(Id INTEGER PRIMARY KEY); CREATE TABLE Suppliers(Id INTEGER PRIMARY KEY); CREATE TABLE __EFMigrationsHistory(MigrationId TEXT);");
        for (int i = 0; i < journals; i++) Exec(c, "INSERT INTO DailyJournals DEFAULT VALUES;");
        return path;
    }

    [Fact]
    public void CreateBackup_IncludesRowsStillInWalFile()
    {
        var dir = NewTempDir();
        var db = Path.Combine(dir, "live.db");
        CreateMeezanLikeDb(db, 1);

        // اتصال مفتوح بنمط WAL مع إيقاف الدمج التلقائي: الصفوف الجديدة تبقى في ملف -wal فقط
        using var live = new SqliteConnection($"Data Source={db};Pooling=False");
        live.Open();
        Exec(live, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;");
        Exec(live, "INSERT INTO DailyJournals DEFAULT VALUES; INSERT INTO DailyJournals DEFAULT VALUES;");

        var backup = Path.Combine(dir, "backup.db");
        DatabaseBackupHelper.CreateBackup(db, backup);

        Count(backup, "DailyJournals").Should().Be(3);
    }

    [Fact]
    public void ValidateMeezanDatabase_RejectsForeignAndNonDatabaseFiles()
    {
        var dir = NewTempDir();
        var text = Path.Combine(dir, "notes.db");
        File.WriteAllText(text, "this is not a database file at all, just some text content");
        var foreign = Path.Combine(dir, "foreign.db");
        using (var c = new SqliteConnection($"Data Source={foreign};Pooling=False"))
        {
            c.Open();
            Exec(c, "CREATE TABLE Something(Id INTEGER);");
        }
        var valid = CreateMeezanLikeDb(Path.Combine(dir, "valid.db"), 1);

        FluentActions.Invoking(() => DatabaseBackupHelper.ValidateMeezanDatabase(text)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => DatabaseBackupHelper.ValidateMeezanDatabase(foreign)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => DatabaseBackupHelper.ValidateMeezanDatabase(valid)).Should().NotThrow();
    }

    [Fact]
    public void ReplaceDatabase_DiscardsStaleWalFiles()
    {
        var dir = NewTempDir();
        var db = Path.Combine(dir, "live.db");
        CreateMeezanLikeDb(db, 5);
        var backup = CreateMeezanLikeDb(Path.Combine(dir, "old_backup.db"), 2);

        // ملفات WAL متبقية من القاعدة الحالية (تحاكي تعطل التطبيق قبل الدمج)
        File.WriteAllBytes(db + "-wal", new byte[] { 1, 2, 3, 4 });
        File.WriteAllBytes(db + "-shm", new byte[] { 1, 2, 3, 4 });

        DatabaseBackupHelper.ReplaceDatabase(backup, db);

        File.Exists(db + "-wal").Should().BeFalse();
        Count(db, "DailyJournals").Should().Be(2);
    }

    [Fact]
    public void PruneBackups_KeepsLatestAndOnePerDay()
    {
        var dir = NewTempDir();
        // 12 نسخة اليوم + نسخة لكل يوم من الأيام الثلاثة السابقة
        for (int i = 0; i < 12; i++)
        {
            var f = Path.Combine(dir, $"Meezan_backup_today_{i:00}.db");
            File.WriteAllText(f, "x");
            File.SetLastWriteTime(f, DateTime.Now.AddMinutes(-i));
        }
        for (int d = 1; d <= 3; d++)
        {
            var f = Path.Combine(dir, $"Meezan_backup_day{d}.db");
            File.WriteAllText(f, "x");
            File.SetLastWriteTime(f, DateTime.Now.AddDays(-d));
        }

        DatabaseBackupHelper.PruneBackups(dir, "Meezan_backup_*.db", keepLatest: 10, keepDays: 14);

        var left = Directory.GetFiles(dir).Select(Path.GetFileName).ToList();
        left.Should().HaveCount(13); // أحدث 10 + نسخة لكل يوم من الأيام الثلاثة
        left.Should().Contain(new[] { "Meezan_backup_day1.db", "Meezan_backup_day2.db", "Meezan_backup_day3.db" });
    }
}
