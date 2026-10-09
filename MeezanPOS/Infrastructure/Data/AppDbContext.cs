using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        // MEEZANPOS_DATA_DIR يسمح للاختبارات باستخدام مجلد مؤقت بدل قاعدة بيانات المستخدم الحقيقية
        // وإلا فمجلد المطعم المفتوح، ثم المجلد الرئيسي (المطعم الأول)
        var appDataDir = System.Environment.GetEnvironmentVariable("MEEZANPOS_DATA_DIR");
        if (string.IsNullOrWhiteSpace(appDataDir))
            appDataDir = RestaurantContext.Current?.DataDirectory ?? RestaurantRegistry.DefaultRoot;
        System.IO.Directory.CreateDirectory(appDataDir);
        return System.IO.Path.Combine(appDataDir, "Meezan.db");
    }

    private static T ParseEnumWithFallback<T>(string value, T fallbackValue) where T : struct, System.Enum
    {
        if (System.Enum.TryParse<T>(value, out var result))
        {
            return result;
        }
        Log.Warning("Failed to parse enum {EnumType} from value '{Value}'. Falling back to default: {Fallback}", typeof(T).Name, value, fallbackValue);
        return fallbackValue;
    }

    private static void SetGlobalQueryFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : BaseEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => !e.IsDeleted);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={GetDatabasePath()};Default Timeout=30");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply Global Query Filter for all entities inheriting from BaseEntity via Reflection
        var filterMethod = typeof(AppDbContext).GetMethod(nameof(SetGlobalQueryFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType != null && typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                var genericMethod = filterMethod!.MakeGenericMethod(entityType.ClrType);
                genericMethod.Invoke(null, new object[] { modelBuilder });
            }
        }

        modelBuilder.Entity<WorkerTransaction>()
            .HasIndex(t => new { t.WorkerId, t.AttendanceId, t.Type })
            .IsUnique()
            .HasFilter("AttendanceId IS NOT NULL AND IsDeleted = 0");

        // Value converters for Enums stored as strings
        modelBuilder.Entity<PostingSession>()
            .Property(s => s.SessionType)
            .HasConversion(
                v => v.ToString(),
                v => ParseEnumWithFallback<PostingSessionType>(v, PostingSessionType.Posting));

        modelBuilder.Entity<PostingSession>()
            .Property(s => s.Status)
            .HasConversion(
                v => v.ToString(),
                v => v == "Settle" ? PostingSessionStatus.Settled :
                     v == "Unpost" ? PostingSessionStatus.Unlocked :
                     ParseEnumWithFallback<PostingSessionStatus>(v, PostingSessionStatus.Draft));

        modelBuilder.Entity<PostingSessionDetail>()
            .Property(d => d.EntityType)
            .HasConversion(
                v => v.ToString(),
                v => ParseEnumWithFallback<PostingEntityType>(v, PostingEntityType.DailyJournal));

        modelBuilder.Entity<PostingSessionDetail>()
            .Property(d => d.ActionType)
            .HasConversion(
                v => v.ToString(),
                v => v == "Unposted" ? PostingActionType.Unposted :
                     ParseEnumWithFallback<PostingActionType>(v, PostingActionType.Posted));

        modelBuilder.Entity<PeriodUnlockHistory>()
            .Property(h => h.PreviousStatus)
            .HasConversion(
                v => v.ToString(),
                v => ParseEnumWithFallback<PostingSessionStatus>(v, PostingSessionStatus.Draft));

        // تكوين علاقات أجور العمال بالمصروفات
        modelBuilder.Entity<GeneralExpense>()
            .HasOne(e => e.Worker)
            .WithMany()
            .HasForeignKey(e => e.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DailyExpenseItem>()
            .HasOne(e => e.Worker)
            .WithMany()
            .HasForeignKey(e => e.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Disable cascade delete
        foreach (var relationship in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            relationship.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    /// <summary>
    /// بيانات بدء التشغيل: المستخدم الافتراضي وترقية كلمات المرور القديمة إلى BCrypt.
    /// </summary>
    /// <remarks>
    /// لا يغيّر المخطط: كل تغييرات الجداول والأعمدة والمشغّلات صارت في ترحيلات EF وحدها
    /// (آخرها MoveManualSqlToMigrations). يُستدعى بعد Database.Migrate().
    /// </remarks>
    public static void SeedData()
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
                if (!isDeleted && ge.ExpenseType == GeneralExpenseType.Salaries && ge.WorkerId.HasValue)
                {
                    var worker = Workers.FirstOrDefault(w => w.Id == ge.WorkerId.Value && !w.IsDeleted);
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
                if (!isDeleted && dei.Type == ExpenseType.WorkerWage && dei.WorkerId.HasValue)
                {
                    var worker = Workers.FirstOrDefault(w => w.Id == dei.WorkerId.Value && !w.IsDeleted);
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
        AddAutomaticAuditEntries();
        var added = CaptureCreatedForAudit();
        var result = base.SaveChanges();
        if (AddCreatedAuditEntries(added))
            base.SaveChanges();
        return result;
    }

    public override async Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        SyncWorkerTransactions();
        AddAutomaticAuditEntries();
        var added = CaptureCreatedForAudit();
        var result = await base.SaveChangesAsync(cancellationToken);
        if (AddCreatedAuditEntries(added))
            await base.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// السجلات التي يُسجَّل إنشاؤها في سجل النشاط (من أضاف ماذا ومتى). الحركات المشتقة منها
    /// (حركات النقدية والمصارف الناتجة عن يومية...) لا تُسجَّل حتى لا يغرق السجل.
    /// </summary>
    private static bool IsAuditedCreation(BaseEntity entity) => entity switch
    {
        DailyJournal or GeneralExpense or SupplierInvoice or Supplier or Worker or BankAccount
            or OwnerDebt or OwnerDebtSettlement => true,
        // دفعة مورد من شاشة المورد (دفعات اليومية تتبع اليومية)
        SupplierTransaction t => t.Type == SupplierTransactionType.DecreaseDebt && t.SourceType != TransactionSourceType.DailyJournalPayment,
        // إيداع أو سحب يدوي
        BankTransaction b => b.SourceType == "Manual",
        // سلفة أو دفعة يدوية للعامل (لا استحقاقات الحضور ولا بنود اليومية)
        WorkerTransaction w => w.AttendanceId == null && w.DailyExpenseItemId == null && w.GeneralExpenseId == null,
        _ => false
    };

    private List<EntityEntry<BaseEntity>> CaptureCreatedForAudit()
    {
        if (AuditContext.CurrentUserId is not int) return new();
        return ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.State == EntityState.Added && IsAuditedCreation(e.Entity))
            .ToList();
    }

    private bool AddCreatedAuditEntries(List<EntityEntry<BaseEntity>> added)
    {
        if (added.Count == 0 || AuditContext.CurrentUserId is not int userId) return false;
        foreach (var entry in added)
        {
            var amounts = entry.Properties
                .Where(p => p.Metadata.ClrType == typeof(decimal) && p.CurrentValue is decimal d && d != 0
                            && !AuditIgnoredProperties.Contains(p.Metadata.Name))
                .Select(p => $"{p.Metadata.Name}={p.CurrentValue}");
            AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "AutoCreate",
                EntityName = entry.Entity.GetType().Name,
                EntityId = entry.Entity.Id,
                Changes = string.Join(" | ", amounts),
                CreatedAt = DateTime.UtcNow
            });
        }
        return true;
    }

    /// <summary>
    /// كيانات لا تُسجَّل تلقائياً: سجلات التدقيق نفسها، والإعدادات، وبنود اليومية الفرعية
    /// (تُحذف وتُعاد كتابتها في كل حفظ لمسودة اليومية، وتغيّر إجماليات اليومية يُسجَّل على اليومية نفسها).
    /// </summary>
    private static readonly HashSet<Type> AuditExcludedTypes = new()
    {
        typeof(AuditLog), typeof(UserActionLog), typeof(Setting),
        typeof(DailyExpenseItem), typeof(BankingItem), typeof(OrderAdjustmentItem), typeof(DailyJournalBankSale),
        typeof(PostingSessionDetail), typeof(SupplierInvoiceItem), typeof(WorkerAttendance)
    };

    /// <summary>أرصدة تراكمية يُعاد حسابها آلياً؛ تسجيلها يغرق السجل بلا فائدة.</summary>
    private static readonly HashSet<string> AuditIgnoredProperties = new() { "BalanceAfter", "CurrentBalance" };

    /// <summary>
    /// سجل تدقيق تلقائي لكل حذف (فعلي أو منطقي) ولكل تعديل على المبالغ، مع المستخدم والقيم قبل/بعد.
    /// يعمل فقط عند وجود مستخدم مسجل (لا يُسجَّل أثناء ترقية قاعدة البيانات أو في الاختبارات).
    /// </summary>
    private void AddAutomaticAuditEntries()
    {
        if (AuditContext.CurrentUserId is not int userId) return;

        var logs = new List<AuditLog>();
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (AuditExcludedTypes.Contains(entry.Entity.GetType())) continue;

            string? action = null;
            var changes = new List<string>();

            if (entry.State == EntityState.Deleted)
            {
                action = "AutoDelete";
                foreach (var prop in entry.Properties.Where(p => p.Metadata.ClrType == typeof(decimal)))
                    changes.Add($"{prop.Metadata.Name}={prop.OriginalValue}");
            }
            else if (entry.State == EntityState.Modified)
            {
                var deletedProp = entry.Property(nameof(BaseEntity.IsDeleted));
                if (deletedProp.IsModified && deletedProp.OriginalValue is false && deletedProp.CurrentValue is true)
                {
                    action = "AutoSoftDelete";
                    foreach (var prop in entry.Properties.Where(p => p.Metadata.ClrType == typeof(decimal)))
                        changes.Add($"{prop.Metadata.Name}={prop.OriginalValue}");
                }
                else
                {
                    foreach (var prop in entry.Properties.Where(p => p.IsModified
                                 && (p.Metadata.ClrType == typeof(decimal) || p.Metadata.ClrType == typeof(decimal?))
                                 && !AuditIgnoredProperties.Contains(p.Metadata.Name)
                                 && !Equals(p.OriginalValue, p.CurrentValue)))
                    {
                        changes.Add($"{prop.Metadata.Name}: {prop.OriginalValue} → {prop.CurrentValue}");
                    }
                    if (changes.Count > 0) action = "AutoUpdate";
                }
            }

            if (action == null) continue;
            logs.Add(new AuditLog
            {
                UserId = userId,
                Action = action,
                EntityName = entry.Entity.GetType().Name,
                EntityId = entry.Entity.Id,
                Changes = string.Join(" | ", changes),
                CreatedAt = DateTime.UtcNow
            });
        }

        if (logs.Count > 0) AuditLogs.AddRange(logs);
    }
}
