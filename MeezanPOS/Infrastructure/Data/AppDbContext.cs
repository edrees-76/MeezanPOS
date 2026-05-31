using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using Serilog;
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
    public DbSet<Worker> Workers { get; set; }
    public DbSet<WorkerTransaction> WorkerTransactions { get; set; }

    // --- التدقيق المالي والترحيل (Financial Core) ---
    public DbSet<FinancialPeriod> FinancialPeriods { get; set; }
    public DbSet<PostingSession> PostingSessions { get; set; }
    public DbSet<PostingSessionDetail> PostingSessionDetails { get; set; }
    public DbSet<PeriodUnlockHistory> PeriodUnlockHistories { get; set; }

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
        optionsBuilder.UseSqlite($"Data Source={GetDatabasePath()};Default Timeout=30");
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
        modelBuilder.Entity<Worker>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<WorkerTransaction>().HasQueryFilter(e => !e.IsDeleted);

        modelBuilder.Entity<WorkerTransaction>()
            .HasIndex(t => new { t.WorkerId, t.AttendanceId, t.Type })
            .IsUnique()
            .HasFilter("AttendanceId IS NOT NULL AND IsDeleted = 0");
        
        // اليوميات وعناصرها الفرعية — كانت مفقودة سابقاً
        modelBuilder.Entity<DailyJournal>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<DailyExpenseItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<BankingItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<OrderAdjustmentItem>().HasQueryFilter(e => !e.IsDeleted);

        // Financial Core
        modelBuilder.Entity<FinancialPeriod>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<PostingSession>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<PostingSessionDetail>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<PeriodUnlockHistory>().HasQueryFilter(e => !e.IsDeleted);

        // Value converters for Enums stored as strings
        modelBuilder.Entity<PostingSession>()
            .Property(s => s.SessionType)
            .HasConversion(
                v => v.ToString(),
                v => (PostingSessionType)System.Enum.Parse(typeof(PostingSessionType), v));

        modelBuilder.Entity<PostingSession>()
            .Property(s => s.Status)
            .HasConversion(
                v => v.ToString(),
                v => v == "Settle" ? PostingSessionStatus.Settled :
                     v == "Unpost" ? PostingSessionStatus.Unlocked :
                     (PostingSessionStatus)System.Enum.Parse(typeof(PostingSessionStatus), v));

        modelBuilder.Entity<PostingSessionDetail>()
            .Property(d => d.EntityType)
            .HasConversion(
                v => v.ToString(),
                v => (PostingEntityType)System.Enum.Parse(typeof(PostingEntityType), v));

        modelBuilder.Entity<PostingSessionDetail>()
            .Property(d => d.ActionType)
            .HasConversion(
                v => v.ToString(),
                v => v == "Unposted" ? PostingActionType.Unposted :
                     (PostingActionType)System.Enum.Parse(typeof(PostingActionType), v));

        modelBuilder.Entity<PeriodUnlockHistory>()
            .Property(h => h.PreviousStatus)
            .HasConversion(
                v => v.ToString(),
                v => (PostingSessionStatus)System.Enum.Parse(typeof(PostingSessionStatus), v));

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

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000;";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to enable WAL journal mode"); }
        
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
        catch (Exception ex) { Log.Warning(ex, "خطأ غير متوقع أثناء إنشاء جدول CashMovements"); }

        // إنشاء جدول العمال وجدول الحركات الجديد
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Workers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT,
                    IsDeleted INTEGER NOT NULL DEFAULT 0,
                    WorkerName TEXT NOT NULL,
                    DailyWage REAL NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    Notes TEXT
                );
                
                CREATE TABLE IF NOT EXISTS WorkerTransactions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT,
                    IsDeleted INTEGER NOT NULL DEFAULT 0,
                    WorkerId INTEGER NOT NULL,
                    WorkerName TEXT NOT NULL,
                    TransactionDate TEXT NOT NULL,
                    Type INTEGER NOT NULL,
                    DebitAmount REAL NOT NULL,
                    CreditAmount REAL NOT NULL,
                    AttendanceId INTEGER,
                    DailyExpenseItemId INTEGER,
                    GeneralExpenseId INTEGER,
                    Notes TEXT,
                    FOREIGN KEY (WorkerId) REFERENCES Workers(Id) ON DELETE RESTRICT,
                    FOREIGN KEY (AttendanceId) REFERENCES WorkerAttendances(Id) ON DELETE RESTRICT,
                    FOREIGN KEY (DailyExpenseItemId) REFERENCES DailyExpenseItems(Id) ON DELETE RESTRICT,
                    FOREIGN KEY (GeneralExpenseId) REFERENCES GeneralExpenses(Id) ON DELETE RESTRICT
                );

                CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkerTransactions_WorkerId_AttendanceId_Type 
                ON WorkerTransactions (WorkerId, AttendanceId, Type) 
                WHERE AttendanceId IS NOT NULL AND IsDeleted = 0;
            ";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) { Log.Warning(ex, "خطأ أثناء إنشاء جداول العمال والأستاذ المساعد"); }

        // إنشاء جدول تاريخ إلغاء قفل الفترات
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS PeriodUnlockHistories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT,
                    IsDeleted INTEGER NOT NULL DEFAULT 0,
                    PostingSessionId INTEGER NOT NULL,
                    UnlockSequence INTEGER NOT NULL DEFAULT 1,
                    UnlockedBy TEXT NOT NULL,
                    UnlockDate TEXT NOT NULL,
                    PreviousStatus TEXT NOT NULL,
                    Reason TEXT NOT NULL,
                    DetailReason TEXT NOT NULL,
                    IsReLocked INTEGER NOT NULL DEFAULT 0,
                    ReLockedDate TEXT,
                    FOREIGN KEY (PostingSessionId) REFERENCES PostingSessions(Id) ON DELETE RESTRICT
                );";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) { Log.Warning(ex, "خطأ أثناء إنشاء جدول PeriodUnlockHistories"); }

        var alterCommands = new[]
        {
            "ALTER TABLE OwnerDebts ADD COLUMN PaymentMethod TEXT;",
            "ALTER TABLE OwnerDebts ADD COLUMN TransferReference TEXT;",
            "ALTER TABLE GeneralExpenses ADD COLUMN CustomExpenseName TEXT;",
            // حقول أمنية جديدة للمستخدمين
            "ALTER TABLE Users ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1;",
            "ALTER TABLE Users ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Users ADD COLUMN FailedLoginAttempts INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Users ADD COLUMN LockoutEnd TEXT;",
            "ALTER TABLE Users ADD COLUMN LastLoginAt TEXT;",
            // حقول حضور العمال الجديدة
            "ALTER TABLE WorkerAttendances ADD COLUMN WorkerId INTEGER;",
            "ALTER TABLE WorkerAttendances ADD COLUMN Status INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE WorkerAttendances ADD COLUMN SnapshotDailyWage REAL NOT NULL DEFAULT 0.0;",
            // حقول جلسات الترحيل والتفاصيل
            "ALTER TABLE PostingSessions ADD COLUMN PeriodStartDate TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';",
            "ALTER TABLE PostingSessions ADD COLUMN PeriodEndDate TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';",
            "ALTER TABLE PostingSessions ADD COLUMN SessionType TEXT NOT NULL DEFAULT 'Posting';",
            "ALTER TABLE PostingSessions ADD COLUMN RowVersion INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE PostingSessionDetails ADD COLUMN TransactionAmount REAL NOT NULL DEFAULT 0.0;",
            "ALTER TABLE PostingSessionDetails ADD COLUMN CashMovementId INTEGER;"
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
            catch (Exception ex) { Log.Warning(ex, "خطأ غير متوقع أثناء ALTER TABLE"); }
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
            catch (Exception ex) { Log.Warning(ex, "خطأ أثناء حذف trigger"); }
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
                Log.Error(ex, "خطأ حرج أثناء إنشاء trigger حماية السجلات المرحّلة");
            }
        }

        // بذر مستخدم Admin افتراضي إن لم يكن موجوداً
        if (!context.Users.Any())
        {
            var adminRole = context.Roles.FirstOrDefault(r => r.Type == RoleType.Admin);
            if (adminRole == null)
            {
                adminRole = new Role { Name = "مدير", Type = RoleType.Admin };
                context.Roles.Add(adminRole);
                context.SaveChanges();
            }

            context.Users.Add(new User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin", 12),
                FullName = "المدير العام",
                RoleId = adminRole.Id,
                MustChangePassword = true
            });
            context.SaveChanges();
            Log.Information("تم إنشاء المستخدم الافتراضي (admin) — يجب تغيير كلمة المرور");
        }
        else
        {
            // ترحيل كلمات المرور القديمة (النص الواضح) إلى BCrypt إن لزم
            var usersWithPlainPasswords = context.Users
                .Where(u => !u.IsDeleted && !u.PasswordHash.StartsWith("$2"))
                .ToList();
            foreach (var user in usersWithPlainPasswords)
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash, 12);
                user.MustChangePassword = true;
            }
            if (usersWithPlainPasswords.Any())
            {
                context.SaveChanges();
                Log.Information("تم ترحيل {Count} كلمة مرور إلى BCrypt", usersWithPlainPasswords.Count);
            }
        }

        // ترحيل InternalTransfer → InternalTransferIn/Out (مرة واحدة)
        var migrationDone = context.Settings.FirstOrDefault(s => s.Key == "InternalTransferMigrated");
        if (migrationDone == null)
        {
            try
            {
                // التحويلات الواردة
                using var cmdIn = conn.CreateCommand();
                cmdIn.CommandText = "UPDATE BankTransactions SET Type = 10 WHERE Type = 3 AND IsDeleted = 0 AND (Notes LIKE 'تحويل من%' OR Notes LIKE '%من %');";
                var inCount = cmdIn.ExecuteNonQuery();

                // التحويلات الصادرة: كل ما تبقى من Type = 3
                using var cmdOut = conn.CreateCommand();
                cmdOut.CommandText = "UPDATE BankTransactions SET Type = 9 WHERE Type = 3 AND IsDeleted = 0;";
                var outCount = cmdOut.ExecuteNonQuery();

                context.Settings.Add(new Domain.Entities.Setting { Key = "InternalTransferMigrated", Value = "true" });
                context.SaveChanges();

                Log.Information("ترحيل InternalTransfer: {InCount} وارد + {OutCount} صادر", inCount, outCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "فشل ترحيل InternalTransfer");
            }
        }

        conn.Close();
    }

    private void SyncWorkerTransactions()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => (e.Entity is GeneralExpense || e.Entity is DailyExpenseItem) 
                        && (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted))
            .ToList();

        foreach (var entry in entries)
        {
            if (entry.Entity is GeneralExpense ge)
            {
                bool isDeleted = entry.State == EntityState.Deleted || ge.IsDeleted;
                if (!isDeleted && ge.ExpenseType == GeneralExpenseType.Salaries && !string.IsNullOrWhiteSpace(ge.WorkerName))
                {
                    var worker = Workers.FirstOrDefault(w => w.WorkerName.Trim().ToLower() == ge.WorkerName.Trim().ToLower() && !w.IsDeleted);
                    if (worker != null)
                    {
                        WorkerTransaction? tx = null;
                        if (ge.Id > 0)
                        {
                            tx = WorkerTransactions.FirstOrDefault(t => t.GeneralExpenseId == ge.Id && !t.IsDeleted);
                        }

                        if (tx == null)
                        {
                            tx = new WorkerTransaction
                            {
                                WorkerId = worker.Id,
                                WorkerName = worker.WorkerName,
                                TransactionDate = ge.PaymentDate,
                                Type = WorkerTransactionType.Payment,
                                DebitAmount = ge.Amount,
                                CreditAmount = 0m,
                                GeneralExpense = ge,
                                Notes = ge.Description ?? "سداد من المصاريف العامة"
                            };
                            WorkerTransactions.Add(tx);
                        }
                        else
                        {
                            tx.WorkerId = worker.Id;
                            tx.WorkerName = worker.WorkerName;
                            tx.TransactionDate = ge.PaymentDate;
                            tx.DebitAmount = ge.Amount;
                            tx.Notes = ge.Description ?? "سداد من المصاريف العامة";
                            tx.UpdatedAt = DateTime.UtcNow;
                        }
                    }
                }
                else
                {
                    if (ge.Id > 0)
                    {
                        var tx = WorkerTransactions.FirstOrDefault(t => t.GeneralExpenseId == ge.Id && !t.IsDeleted);
                        if (tx != null)
                        {
                            tx.IsDeleted = true;
                            tx.GeneralExpenseId = null;
                            tx.GeneralExpense = null;
                            tx.UpdatedAt = DateTime.UtcNow;
                        }
                    }
                }
            }
            else if (entry.Entity is DailyExpenseItem dei)
            {
                bool isDeleted = entry.State == EntityState.Deleted || dei.IsDeleted;
                if (!isDeleted && dei.Type == ExpenseType.WorkerWage && !string.IsNullOrWhiteSpace(dei.WorkerName))
                {
                    var worker = Workers.FirstOrDefault(w => w.WorkerName.Trim().ToLower() == dei.WorkerName.Trim().ToLower() && !w.IsDeleted);
                    if (worker != null)
                    {
                        var journal = DailyJournals.FirstOrDefault(j => j.Id == dei.DailyJournalId);
                        var txDate = journal?.JournalDate ?? dei.CreatedAt;

                        WorkerTransaction? tx = null;
                        if (dei.Id > 0)
                        {
                            tx = WorkerTransactions.FirstOrDefault(t => t.DailyExpenseItemId == dei.Id && !t.IsDeleted);
                        }

                        if (tx == null)
                        {
                            tx = new WorkerTransaction
                            {
                                WorkerId = worker.Id,
                                WorkerName = worker.WorkerName,
                                TransactionDate = txDate,
                                Type = WorkerTransactionType.Payment,
                                DebitAmount = dei.Amount,
                                CreditAmount = 0m,
                                DailyExpenseItem = dei,
                                Notes = dei.Notes ?? dei.Description ?? "سداد من حركة الوردية"
                            };
                            WorkerTransactions.Add(tx);
                        }
                        else
                        {
                            tx.WorkerId = worker.Id;
                            tx.WorkerName = worker.WorkerName;
                            tx.TransactionDate = txDate;
                            tx.DebitAmount = dei.Amount;
                            tx.Notes = dei.Notes ?? dei.Description ?? "سداد من حركة الوردية";
                            tx.UpdatedAt = DateTime.UtcNow;
                        }
                    }
                }
                else
                {
                    if (dei.Id > 0)
                    {
                        var tx = WorkerTransactions.FirstOrDefault(t => t.DailyExpenseItemId == dei.Id && !t.IsDeleted);
                        if (tx != null)
                        {
                            tx.IsDeleted = true;
                            tx.DailyExpenseItemId = null;
                            tx.DailyExpenseItem = null;
                            tx.UpdatedAt = DateTime.UtcNow;
                        }
                    }
                }
            }
        }
    }

    public override int SaveChanges()
    {
        SyncWorkerTransactions();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        SyncWorkerTransactions();
        return base.SaveChangesAsync(cancellationToken);
    }
}
