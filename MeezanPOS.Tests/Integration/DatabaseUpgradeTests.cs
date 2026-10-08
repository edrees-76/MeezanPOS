using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace MeezanPOS.Tests.Integration;

/// <summary>
/// ترقية قاعدة بيانات من إصدار قديم إلى الإصدار الحالي بنفس مسار بدء التشغيل:
/// Database.Migrate ثم SeedData. يتأكد أن البيانات القديمة باقية
/// وأن كل جداول النموذج قابلة للقراءة بعد الترقية (أي عمود ناقص يُفشل الاستعلام).
/// </summary>
public class DatabaseUpgradeTests
{
    // أول ترحيل بعد إعادة هيكلة المرحلة التجريبية (مايو 2026) التي استبدلت جداول الموردين والعمال الأولى
    private const string FirstMigration = "20260517075809_AddSupplierLedgerModule";

    [Fact]
    public void OldestSchema_UpgradesToCurrent_KeepingData()
    {
        MessageBoxMock.Initialize(); // يوجّه GetDatabasePath إلى قاعدة الاختبار
        var dbPath = AppDbContext.GetDatabasePath();
        dbPath.Should().NotContain(Path.Combine("AppData", "Local", "MeezanPOS" + Path.DirectorySeparatorChar),
            "the test must never touch the real database");

        DeleteDatabase(dbPath);
        try
        {
            // 1) قاعدة بالمخطط الأقدم مع بيانات
            using (var old = new AppDbContext())
            {
                old.GetService<IMigrator>().Migrate(FirstMigration);
                old.Database.ExecuteSqlRaw(
                    "INSERT INTO Suppliers (Name, Phone, OpeningBalance, CreditLimit, CurrentBalance, IsActive, CreatedAt, IsDeleted) " +
                    "VALUES ('مورد قديم', '091', '150.25', '0', '150.25', 1, '2026-05-17 10:00:00', 0)");
            }

            // 2) نفس خطوات بدء التشغيل
            using (var context = new AppDbContext())
            {
                context.Database.Migrate();
            }
            AppDbContext.SeedData();

            // 3) التحقق
            using var check = new AppDbContext();
            check.Database.GetPendingMigrations().Should().BeEmpty();

            // مشغّلات حماية السجلات المرحّلة يُنشئها ترحيل EF وحده
            var conn = check.Database.GetDbConnection();
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger'";
                var triggers = new System.Collections.Generic.List<string>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) triggers.Add(reader.GetString(0));
                triggers.Should().BeEquivalentTo(PostedRecordTriggers.Names);
            }
            check.Users.Should().ContainSingle(u => u.Username == "admin", "SeedData creates the default admin");

            var supplier = check.Suppliers.Single(s => s.Name == "مورد قديم");
            supplier.OpeningBalance.Should().Be(150.25m);

            // كل DbSet يُقرأ بلا خطأ "no such column" بعد الترقية
            var setProperties = typeof(AppDbContext).GetProperties()
                .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));
            foreach (var prop in setProperties)
            {
                var set = (IQueryable<object>)prop.GetValue(check)!;
                var act = () => set.Take(1).ToList();
                act.Should().NotThrow($"table for {prop.Name} must match the model after upgrade");
            }
        }
        finally
        {
            // إعادة قاعدة الاختبار لحالة نظيفة لبقية الاختبارات
            DeleteDatabase(dbPath);
            using var fresh = new AppDbContext();
            fresh.Database.EnsureCreated();
        }
    }

    [Fact]
    public void JournalSupplierPayments_GetTheirJournalDate_EvenWhenPosted()
    {
        MessageBoxMock.Initialize();
        var dbPath = AppDbContext.GetDatabasePath();
        dbPath.Should().NotContain(Path.Combine("AppData", "Local", "MeezanPOS" + Path.DirectorySeparatorChar),
            "the test must never touch the real database");

        var journalDate = new DateTime(2026, 6, 10, 0, 0, 0);
        DeleteDatabase(dbPath);
        try
        {
            int wrongId, rightId;
            // 1) المخطط قبل الإصلاح، مع دفعة مسجلة بتاريخ الحفظ لا تاريخ اليومية (اليومية مرحّلة)
            using (var old = new AppDbContext())
            {
                old.GetService<IMigrator>().Migrate("20261008163212_MoveManualSqlToMigrations");
                // SQL خام: نموذج EF الحالي فيه أعمدة أحدث من مخطط هذا الترحيل
                old.Database.ExecuteSqlRaw(
                    "INSERT INTO Suppliers (Id, Name, OpeningBalance, CreditLimit, CurrentBalance, IsActive, CreatedAt, IsDeleted) " +
                    "VALUES (1, 'مورد', '0', '0', '0', 1, '2026-06-10 09:00:00', 0)");
                old.Database.ExecuteSqlRaw(
                    "INSERT INTO DailyJournals (Id, JournalDate, ShiftType, EmployeeName, CashFloat, TotalSales, BankingTotal, ReturnsTotal, FreeOrdersTotal, " +
                    "TotalExpenses, ActualCash, FinancialStatus, RowVersion, CreatedAt, IsDeleted) " +
                    "VALUES (1, '2026-06-10 00:00:00', 3, 'كاشير', '0', '100', '0', '0', '0', '40', '60', 2, 0, '2026-06-10 09:00:00', 0)");
                old.Database.ExecuteSqlRaw(
                    "INSERT INTO DailyExpenseItems (Id, DailyJournalId, SequenceNumber, Description, Amount, Type, CreatedAt, IsDeleted) " +
                    "VALUES (7, 1, 1, 'دفعة مورد', '40', 2, '2026-06-10 09:00:00', 0)");
                old.Database.ExecuteSqlRaw(
                    "INSERT INTO SupplierTransactions (Id, SupplierId, Type, Amount, BalanceAfter, SourceType, SourceId, TransactionDate, CreatedAt, IsDeleted) VALUES " +
                    "(1, 1, 2, '40', '0', 3, 7, '2026-06-13 14:00:00', '2026-06-13 14:00:00', 0), " +
                    "(2, 1, 2, '10', '0', 4, 999, '2026-06-15 00:00:00', '2026-06-15 00:00:00', 0)");
                wrongId = 1;
                rightId = 2;
            }

            // 2) الترقية
            using (var context = new AppDbContext())
                context.Database.Migrate();

            // 3) التاريخ صُحح رغم أن اليومية مرحّلة، والدفعات الأخرى لم تُمس، والمشغّلات عادت
            using var check = new AppDbContext();
            check.SupplierTransactions.Single(t => t.Id == wrongId).TransactionDate.Date.Should().Be(journalDate.Date);
            check.SupplierTransactions.Single(t => t.Id == rightId).TransactionDate.Should().Be(journalDate.AddDays(5));
            var act = () => check.Database.ExecuteSqlRaw($"UPDATE SupplierTransactions SET Amount = 1 WHERE Id = {wrongId}");
            act.Should().Throw<Exception>("the posted-journal trigger is recreated after the fix");
        }
        finally
        {
            DeleteDatabase(dbPath);
            using var fresh = new AppDbContext();
            fresh.Database.EnsureCreated();
        }
    }

    private static void DeleteDatabase(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            if (File.Exists(dbPath + suffix))
                File.Delete(dbPath + suffix);
        }
    }
}
