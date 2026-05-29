using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using MeezanPOS.Domain.Entities;
using System.Linq;

namespace MeezanPOS.Infrastructure.Data;

public class AppDbContext : DbContext
{
    // --- الأمن والمستخدمين ---
    public DbSet<Role> Roles { get; set; }
    public DbSet<User> Users { get; set; }

    // --- المبيعات والإيرادات ---
    public DbSet<SaleHeader> SaleHeaders { get; set; }
    public DbSet<SaleItem> SaleItems { get; set; }
    public DbSet<CashSession> CashSessions { get; set; }
    public DbSet<CashTransaction> CashTransactions { get; set; }
    public DbSet<Expense> Expenses { get; set; }

    // --- النظام ---
    public DbSet<Setting> Settings { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<UserActionLog> UserActionLogs { get; set; }

    // --- اليومية ---
    public DbSet<DailyJournal> DailyJournals { get; set; }
    public DbSet<DailyExpenseItem> DailyExpenseItems { get; set; }
    public DbSet<BankingItem> BankingItems { get; set; }
    public DbSet<OrderAdjustmentItem> OrderAdjustmentItems { get; set; }
    public DbSet<DailyJournalBankSale> DailyJournalBankSales { get; set; }

    // --- الموردين ---
    public DbSet<Supplier> Suppliers { get; set; }
    public DbSet<SupplierInvoice> SupplierInvoices { get; set; }
    public DbSet<SupplierInvoiceItem> SupplierInvoiceItems { get; set; }
    public DbSet<SupplierTransaction> SupplierTransactions { get; set; }

    // --- المصاريف العامة ---
    public DbSet<GeneralExpense> GeneralExpenses { get; set; }

    // --- حضور العمال وأجورهم ---
    public DbSet<WorkerAttendance> WorkerAttendances { get; set; }

    // --- التدقيق المالي والترحيل (Financial Core) ---
    public DbSet<FinancialPeriod> FinancialPeriods { get; set; }
    public DbSet<PostingSession> PostingSessions { get; set; }
    public DbSet<PostingSessionDetail> PostingSessionDetails { get; set; }

    // --- الخدمات المصرفية والبنكية ---
    public DbSet<BankAccount> BankAccounts { get; set; }
    public DbSet<BankTransaction> BankTransactions { get; set; }
    public DbSet<CardPaymentReconciliation> CardPaymentReconciliations { get; set; }
    public DbSet<OwnerDebt> OwnerDebts { get; set; }
    public DbSet<OwnerDebtSettlement> OwnerDebtSettlements { get; set; }
    public DbSet<CashMovement> CashMovements { get; set; }

    // المسار الثابت والموحد لقاعدة البيانات - يمنع إنشاء قواعد بيانات متعددة
    public static string GetDatabasePath()
    {
        var appDataDir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "MeezanPOS");
        System.IO.Directory.CreateDirectory(appDataDir);
        return System.IO.Path.Combine(appDataDir, "Meezan.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={GetDatabasePath()}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Global Query Filter for Soft Delete
        modelBuilder.Entity<Role>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<User>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<SaleHeader>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<SaleItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<CashSession>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<CashTransaction>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Expense>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Supplier>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<SupplierInvoice>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<SupplierInvoiceItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<SupplierTransaction>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<GeneralExpense>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<WorkerAttendance>().HasQueryFilter(e => !e.IsDeleted);
        
        // اليوميات وعناصرها الفرعية — كانت مفقودة سابقاً
        modelBuilder.Entity<DailyJournal>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<DailyExpenseItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<BankingItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<OrderAdjustmentItem>().HasQueryFilter(e => !e.IsDeleted);

        // Financial Core
        modelBuilder.Entity<FinancialPeriod>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<PostingSession>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<PostingSessionDetail>().HasQueryFilter(e => !e.IsDeleted);

        // الخدمات المصرفية والبنكية
        modelBuilder.Entity<BankAccount>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<BankTransaction>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<CardPaymentReconciliation>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<OwnerDebt>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<OwnerDebtSettlement>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<DailyJournalBankSale>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<CashMovement>().HasQueryFilter(e => !e.IsDeleted);
        
        // Disable cascade delete
        foreach (var relationship in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            relationship.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    /// <summary>
    /// ترحيل تلقائي: إضافة الأعمدة الجديدة لقاعدة البيانات إن لم تكن موجودة
    /// </summary>
    public static void MigrateDatabase()
    {
        using var context = new AppDbContext();
        var conn = context.Database.GetDbConnection();
        conn.Open();
        
        // إنشاء جدول حركات النقدية الجديد إذا لم يكن موجوداً
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS CashMovements (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CreatedAt TEXT,
                    UpdatedAt TEXT,
                    IsDeleted INTEGER NOT NULL DEFAULT 0,
                    TransactionDate TEXT NOT NULL,
                    Type INTEGER NOT NULL,
                    Amount REAL NOT NULL,
                    BalanceAfter REAL NOT NULL,
                    SourceType TEXT,
                    SourceId INTEGER,
                    Notes TEXT,
                    IsReversed INTEGER NOT NULL DEFAULT 0
                );";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { /* الجدول/العمود موجود مسبقاً */ }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"⚠️ خطأ غير متوقع أثناء إنشاء جدول: {ex.Message}"); }

        var alterCommands = new[]
        {
            "ALTER TABLE OwnerDebts ADD COLUMN PaymentMethod TEXT;",
            "ALTER TABLE OwnerDebts ADD COLUMN TransferReference TEXT;",
            "ALTER TABLE GeneralExpenses ADD COLUMN CustomExpenseName TEXT;"
        };
        foreach (var sql in alterCommands)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { /* العمود موجود بالفعل */ }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"⚠️ خطأ غير متوقع أثناء ALTER TABLE: {ex.Message}"); }
        }

        // Drop the old triggers to recreate them with the correct transition exemption
        var dropCommands = new[]
        {
            "DROP TRIGGER IF EXISTS trg_PreventUpdatePostedDailyJournal;",
            "DROP TRIGGER IF EXISTS trg_PreventUpdatePostedGeneralExpense;"
        };
        foreach (var sql in dropCommands)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"⚠️ خطأ أثناء حذف trigger: {ex.Message}"); }
        }

        var triggerCommands = new[]
        {
            @"CREATE TRIGGER IF NOT EXISTS trg_PreventUpdatePostedDailyJournal
              BEFORE UPDATE ON DailyJournals
              FOR EACH ROW
              WHEN OLD.FinancialStatus = 2 AND NEW.FinancialStatus = 2
              BEGIN
                  SELECT RAISE(FAIL, 'Cannot update a posted daily journal.');
              END;",

            @"CREATE TRIGGER IF NOT EXISTS trg_PreventDeletePostedDailyJournal
              BEFORE DELETE ON DailyJournals
              FOR EACH ROW
              WHEN OLD.FinancialStatus = 2
              BEGIN
                  SELECT RAISE(FAIL, 'Cannot delete a posted daily journal.');
              END;",

            @"CREATE TRIGGER IF NOT EXISTS trg_PreventUpdatePostedGeneralExpense
              BEFORE UPDATE ON GeneralExpenses
              FOR EACH ROW
              WHEN OLD.FinancialStatus = 2 AND NEW.FinancialStatus = 2
              BEGIN
                  SELECT RAISE(FAIL, 'Cannot update a posted general expense.');
              END;",

            @"CREATE TRIGGER IF NOT EXISTS trg_PreventDeletePostedGeneralExpense
              BEFORE DELETE ON GeneralExpenses
              FOR EACH ROW
              WHEN OLD.FinancialStatus = 2
              BEGIN
                  SELECT RAISE(FAIL, 'Cannot delete a posted general expense.');
              END;",

            @"CREATE TRIGGER IF NOT EXISTS trg_PreventUpdatePostedSupplierTransaction_Journal
              BEFORE UPDATE ON SupplierTransactions
              FOR EACH ROW
              WHEN OLD.SourceType = 3 AND EXISTS (
                  SELECT 1 FROM DailyJournals j
                  JOIN DailyExpenseItems e ON e.DailyJournalId = j.Id
                  WHERE e.Id = OLD.SourceId AND j.FinancialStatus = 2
              )
              BEGIN
                  SELECT RAISE(FAIL, 'Cannot update a supplier transaction linked to a posted daily journal.');
              END;",

            @"CREATE TRIGGER IF NOT EXISTS trg_PreventDeletePostedSupplierTransaction_Journal
              BEFORE DELETE ON SupplierTransactions
              FOR EACH ROW
              WHEN OLD.SourceType = 3 AND EXISTS (
                  SELECT 1 FROM DailyJournals j
                  JOIN DailyExpenseItems e ON e.DailyJournalId = j.Id
                  WHERE e.Id = OLD.SourceId AND j.FinancialStatus = 2
              )
              BEGIN
                  SELECT RAISE(FAIL, 'Cannot delete a supplier transaction linked to a posted daily journal.');
              END;",

            @"CREATE TRIGGER IF NOT EXISTS trg_PreventUpdatePostedSupplierTransaction_Expense
              BEFORE UPDATE ON SupplierTransactions
              FOR EACH ROW
              WHEN OLD.SourceType = 4 AND EXISTS (
                  SELECT 1 FROM GeneralExpenses e
                  WHERE e.Id = OLD.SourceId AND e.FinancialStatus = 2
              )
              BEGIN
                  SELECT RAISE(FAIL, 'Cannot update a supplier transaction linked to a posted general expense.');
              END;",

            @"CREATE TRIGGER IF NOT EXISTS trg_PreventDeletePostedSupplierTransaction_Expense
              BEFORE DELETE ON SupplierTransactions
              FOR EACH ROW
              WHEN OLD.SourceType = 4 AND EXISTS (
                  SELECT 1 FROM GeneralExpenses e
                  WHERE e.Id = OLD.SourceId AND e.FinancialStatus = 2
              )
              BEGIN
                  SELECT RAISE(FAIL, 'Cannot delete a supplier transaction linked to a posted general expense.');
              END;"
        };
        foreach (var sql in triggerCommands)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // ⚠️ فشل إنشاء trigger = فقدان حماية السجلات المرحّلة — يجب عدم التجاهل
                System.Diagnostics.Debug.WriteLine($"🔴 خطأ حرج أثناء إنشاء trigger حماية السجلات المرحّلة: {ex.Message}");
            }
        }

        conn.Close();
    }
}
