using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.DemoData;

/// <summary>
/// يولّد 30 يوم عمل تجريبية (أيام بورديتين وأيام بوردية واحدة) في قاعدة بيانات منفصلة:
/// %LOCALAPPDATA%\MeezanPOS_Demo\Meezan.db
/// كل الحركات تمر عبر خدمات المنظومة نفسها (الترحيل، الخزينة، المصارف، الموردين، الأجور)
/// حتى تكون الأرصدة متسقة تماماً كما لو أُدخلت من الشاشات.
/// </summary>
public static class Program
{
    public const string DemoPassword = "Demo@2026";

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        bool force = args.Contains("--force");
        int days = 30;
        var daysArg = args.FirstOrDefault(a => a.StartsWith("--days="));
        if (daysArg != null) days = int.Parse(daysArg["--days=".Length..]);

        var realDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeezanPOS");
        var demoDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeezanPOS_Demo");

        // يجب ضبط مجلد البيانات قبل أي استخدام لـ AppDbContext
        Environment.SetEnvironmentVariable("MEEZANPOS_DATA_DIR", demoDir);
        var dbPath = AppDbContext.GetDatabasePath();
        if (Path.GetFullPath(Path.GetDirectoryName(dbPath)!).TrimEnd('\\')
            .Equals(Path.GetFullPath(realDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("رفض: مسار قاعدة البيانات يشير إلى البيانات الحقيقية.");
            return 2;
        }

        if (File.Exists(dbPath))
        {
            if (!force)
            {
                Console.Error.WriteLine($"قاعدة البيانات التجريبية موجودة مسبقاً: {dbPath}\nاستخدم --force لإعادة توليدها.");
                return 1;
            }
            SqliteConnection.ClearAllPools();
            foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                if (File.Exists(f)) File.Delete(f);
        }

        Console.WriteLine($"توليد {days} يوم عمل في: {dbPath}");
        var generator = new DemoGenerator(days);
        await generator.RunAsync();
        Console.WriteLine(generator.Summary);
        return 0;
    }
}

internal sealed class DemoGenerator
{
    private readonly int _days;
    private readonly Random _rnd = new(2026);
    private readonly StringBuilder _log = new();

    private AppDbContext _db = null!;
    private SessionService _session = null!;
    private CashLedgerService _cash = null!;
    private BankService _bank = null!;
    private LedgerService _ledger = null!;
    private PostingService _posting = null!;
    private WagesService _wages = null!;

    private readonly List<BankAccount> _banks = new();
    private readonly List<Supplier> _suppliers = new();
    private readonly List<Worker> _workers = new();
    private string _adminId = "1";

    private int _journals, _twoShiftDays, _invoices, _generalExpenses;

    public DemoGenerator(int days) => _days = days;

    public string Summary => _log.ToString();

    public async Task RunAsync()
    {
        using (var init = new AppDbContext())
        {
            init.Database.Migrate();
        }
        AppDbContext.SeedData();

        _db = new AppDbContext();
        await SetupUsersAsync();

        var audit = new AuditService(_db);
        _cash = new CashLedgerService(_db, _session);
        _bank = new BankService(_db, _session, audit);
        var ownerDebt = new OwnerDebtService(_db, _session, _bank, audit);
        _ledger = new LedgerService(_db, _session, _bank, ownerDebt, _cash, audit);
        _posting = new PostingService(_db, _cash, audit, _session);
        _wages = new WagesService(_db, _session);

        var start = DateTime.Today.AddDays(-_days);
        await SetupMasterDataAsync(start.AddDays(-1));

        int settleAfterDay = Math.Max(1, _days / 2 - 1);     // تسوية وإقفال منتصف الفترة
        int draftFromDay = _days - 4;                         // آخر 4 أيام تبقى مسودات (قيد المراجعة)

        for (int i = 0; i < _days; i++)
        {
            var day = start.AddDays(i);
            bool twoShifts = day.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Friday || _rnd.NextDouble() < 0.2;
            if (twoShifts) _twoShiftDays++;

            await RecordAttendanceAsync(day, twoShifts);

            var shifts = twoShifts ? new[] { ShiftType.FirstShift, ShiftType.SecondShift } : new[] { ShiftType.FullDay };
            foreach (var shift in shifts)
            {
                var journal = await CreateJournalAsync(day, shift);
                // الأيام قبل التسوية تبقى مسودة حتى تُرحَّل بالتسوية؛ ما بعدها يُرحَّل فردياً عدا آخر الأيام
                if (i > settleAfterDay && i < draftFromDay)
                    await _posting.PostEntityAsync<DailyJournal>(journal.Id, _adminId);
            }

            if (i % 2 == 0) await CreateSupplierInvoiceAsync(day);
            if (day.DayOfWeek == DayOfWeek.Saturday) await PayWeeklyWagesAsync(day);
            if (i == 2) await CreateMonthlyExpensesAsync(day);
            if (i == 9) await CreateGeneralExpenseAsync(day, GeneralExpenseType.Maintenance, 450m, PaymentMethodType.Cash, "صيانة ثلاجة العرض");
            if (i == 20) await CreateGeneralExpenseAsync(day, GeneralExpenseType.Internet, 120m, PaymentMethodType.Cash, "اشتراك الإنترنت الشهري");

            if (i == settleAfterDay) await SettleAsync(day);
        }

        var cashBalance = await _cash.GetCurrentBalanceAsync();
        var drafts = await _db.DailyJournals.CountAsync(j => j.FinancialStatus == FinancialStatus.Draft);
        var posted = await _db.DailyJournals.CountAsync(j => j.FinancialStatus == FinancialStatus.Posted);
        var sales = (await _db.DailyJournals.Select(j => j.TotalSales).ToListAsync()).Sum();

        _log.AppendLine($"اليوميات: {_journals} (أيام بورديتين: {_twoShiftDays}، أيام بوردية واحدة: {_days - _twoShiftDays})");
        _log.AppendLine($"مرحّلة: {posted} | مسودة: {drafts} | إجمالي المبيعات: {sales:N2}");
        _log.AppendLine($"فواتير موردين: {_invoices} | مصروفات عامة: {_generalExpenses}");
        _log.AppendLine($"رصيد الخزينة الحالي: {cashBalance:N2}");
        foreach (var b in await _db.BankAccounts.AsNoTracking().ToListAsync())
            _log.AppendLine($"رصيد {b.FriendlyName}: {b.CurrentBalance:N2}");
        foreach (var s in await _db.Suppliers.AsNoTracking().ToListAsync())
            _log.AppendLine($"مستحق للمورد {s.Name}: {s.CurrentBalance:N2}");

        _session.ClearSession();
        await _db.DisposeAsync();
    }

    // ------------------------------------------------------------------ المستخدمون

    private async Task SetupUsersAsync()
    {
        var auth = new AuthenticationService(_db);
        async Task<Role> RoleAsync(RoleType type)
        {
            var r = await _db.Roles.FirstOrDefaultAsync(x => x.Type == type);
            if (r != null) return r;
            r = new Role { Type = type, Name = UserManagementService.RoleDisplayName(type) };
            _db.Roles.Add(r);
            await _db.SaveChangesAsync();
            return r;
        }

        var adminRole = await RoleAsync(RoleType.Admin);
        var admin = await _db.Users.Include(u => u.Role).FirstAsync(u => u.Username == "admin");
        admin.PasswordHash = auth.HashPassword(Program.DemoPassword);
        admin.MustChangePassword = false;
        admin.FullName = "مدير المطعم (تجريبي)";

        _db.Users.Add(new User { Username = "manager", FullName = "سالم المدير", RoleId = (await RoleAsync(RoleType.Manager)).Id, PasswordHash = auth.HashPassword(Program.DemoPassword) });
        _db.Users.Add(new User { Username = "cashier", FullName = "أحمد الكاشير", RoleId = (await RoleAsync(RoleType.Cashier)).Id, PasswordHash = auth.HashPassword(Program.DemoPassword) });
        await _db.SaveChangesAsync();

        admin.Role ??= adminRole;
        _session = new SessionService();
        _session.SetUser(admin); // يضبط AuditContext أيضاً فتظهر الحركات في سجل التدقيق باسم المدير
        _adminId = admin.Id.ToString();
    }

    // ------------------------------------------------------------------ البيانات الأساسية

    private async Task SetupMasterDataAsync(DateTime openingDate)
    {
        _banks.Add(await _bank.CreateAccountAsync(new BankAccount { FriendlyName = "مصرف الجمهورية", BankName = "مصرف الجمهورية", AccountNumber = "001-245-778", OpeningBalance = 12000m, CurrentBalance = 12000m }));
        _banks.Add(await _bank.CreateAccountAsync(new BankAccount { FriendlyName = "مصرف التجارة والتنمية", BankName = "مصرف التجارة والتنمية", AccountNumber = "07-55120-3", OpeningBalance = 6500m, CurrentBalance = 6500m }));

        foreach (var name in new[] { "ملحمة الوادي للحوم", "خضار وفواكه الحقول", "دواجن الشروق", "مشروبات النخبة", "مخبز الأمل" })
        {
            var s = new Supplier { Name = name, Phone = "09" + _rnd.Next(10000000, 99999999), IsActive = true };
            _db.Suppliers.Add(s);
            _suppliers.Add(s);
        }

        foreach (var (name, wage) in new[] { ("محمود الطباخ", 90m), ("علي مساعد الطباخ", 70m), ("خالد النادل", 60m), ("يوسف النظافة", 50m) })
        {
            var w = new Worker { WorkerName = name, DailyWage = wage, IsActive = true };
            _db.Workers.Add(w);
            _workers.Add(w);
        }
        await _db.SaveChangesAsync();

        // رصيد افتتاحي للخزينة
        await _cash.RecordMovementAsync(CashMovementType.CashIn, 3000m, SourceTypes.Manual, null, "رصيد افتتاحي للخزينة", openingDate);
    }

    // ------------------------------------------------------------------ اليوميات

    private static readonly string[] Cashiers = { "أحمد الكاشير", "مروان", "عبدالله" };
    private static readonly string[] People = { "عبدالرحمن", "فاطمة", "سالم", "منى", "إبراهيم", "هدى", "طارق", "ريم" };

    private decimal Money(int min, int max) => Math.Round((decimal)_rnd.Next(min * 2, max * 2) / 2m, 2);

    private async Task<DailyJournal> CreateJournalAsync(DateTime day, ShiftType shift)
    {
        bool weekend = day.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Friday;
        decimal sales = shift switch
        {
            ShiftType.FirstShift => Money(1600, 2800),
            ShiftType.SecondShift => Money(2600, 4400),
            _ => Money(3800, 6200)
        };
        if (weekend) sales = Math.Round(sales * 1.2m, 0);

        // المبيعات الإلكترونية (بطاقات) 20%-35% موزعة على المصرفين
        decimal cardTotal = Math.Round(sales * (decimal)(0.20 + _rnd.NextDouble() * 0.15), 0);
        decimal bank1 = Math.Round(cardTotal * (decimal)(0.55 + _rnd.NextDouble() * 0.2), 0);
        decimal bank2 = cardTotal - bank1;

        var journal = new DailyJournal
        {
            JournalDate = day,
            ShiftType = shift,
            EmployeeName = Cashiers[_rnd.Next(Cashiers.Length)],
            CashFloat = 300m,
            TotalSales = sales,
            BankingTotal = cardTotal,
            FinancialStatus = FinancialStatus.Draft,
            RowVersion = 1,
            Notes = weekend ? "ضغط عطلة نهاية الأسبوع" : null
        };

        // المصروفات النثرية من الصندوق
        int seq = 1;
        void AddExpense(string desc, decimal amount, ExpenseType type, string category, Supplier? supplier = null) =>
            journal.ExpenseItems.Add(new DailyExpenseItem
            {
                SequenceNumber = seq++,
                Description = desc,
                Amount = amount,
                Type = type,
                CategoryName = category,
                SupplierId = supplier?.Id,
                SupplierName = supplier?.Name
            });

        AddExpense("خبز وصامولي", Money(40, 90), ExpenseType.General, "مشتريات");
        if (_rnd.NextDouble() < 0.5) AddExpense("أسطوانة غاز", 35m, ExpenseType.Gas, "غاز");
        if (_rnd.NextDouble() < 0.35) AddExpense("مواد تنظيف", Money(25, 70), ExpenseType.Cleaning, "تنظيف");
        if (_rnd.NextDouble() < 0.25) AddExpense("توصيل طلبات", Money(20, 50), ExpenseType.Transport, "نقل");
        // دفعة نقدية لمورد عليه مستحقات فقط، وبما لا يتجاوز المستحق
        var owed = _suppliers.Where(x => x.CurrentBalance >= 200m).ToList();
        if (owed.Count > 0 && _rnd.NextDouble() < 0.45)
        {
            var supplier = owed[_rnd.Next(owed.Count)];
            var amount = Math.Min(Money(300, 900), Math.Floor(supplier.CurrentBalance));
            AddExpense($"دفعة نقدية للمورد {supplier.Name}", amount, ExpenseType.SupplierPayment, "سداد مورد", supplier);
        }
        journal.TotalExpenses = journal.ExpenseItems.Sum(e => e.Amount);

        // المرتجعات والطلبات المجانية
        for (int r = 0, n = _rnd.Next(0, 3); r < n; r++)
            journal.Adjustments.Add(new OrderAdjustmentItem { IsFreeOrder = false, Amount = Money(15, 80), PersonName = People[_rnd.Next(People.Length)], InvoiceNumber = _rnd.Next(1000, 9999).ToString(), Notes = "طلب مرتجع من الزبون" });
        if (_rnd.NextDouble() < 0.45)
            journal.Adjustments.Add(new OrderAdjustmentItem { IsFreeOrder = true, Amount = Money(30, 120), PersonName = People[_rnd.Next(People.Length)], Notes = "وجبة ضيافة" });
        journal.ReturnsTotal = journal.Adjustments.Where(a => !a.IsFreeOrder).Sum(a => a.Amount);
        journal.FreeOrdersTotal = journal.Adjustments.Where(a => a.IsFreeOrder).Sum(a => a.Amount);

        // تقسيم المبيعات الإلكترونية حسب المصرف
        journal.BankSales.Add(new DailyJournalBankSale { BankAccountId = _banks[0].Id, BankName = _banks[0].FriendlyName, Amount = bank1 });
        if (bank2 > 0)
            journal.BankSales.Add(new DailyJournalBankSale { BankAccountId = _banks[1].Id, BankName = _banks[1].FriendlyName, Amount = bank2 });

        // النقد الفعلي: مطابق غالباً، مع عجز أو زيادة بسيطة أحياناً
        double roll = _rnd.NextDouble();
        decimal diff = roll < 0.7 ? 0m : roll < 0.9 ? -Money(5, 25) : Money(5, 15);
        journal.ActualCash = journal.ExpectedCash + diff;

        _db.DailyJournals.Add(journal);
        await _db.SaveChangesAsync();
        _journals++;

        // نفس ما تفعله شاشة اليومية بعد الحفظ: إيداع المبيعات الإلكترونية في المصارف، وتسجيل دفعات الموردين
        foreach (var bSale in journal.BankSales)
        {
            await _bank.RecordTransactionAsync(bSale.BankAccountId!.Value, BankTransactionType.CardSalesDeposit, bSale.Amount, null,
                $"إجمالي مبيعات إلكترونية - {bSale.BankName} - {journal.ShiftName}", "DailyJournal", journal.Id, journal.JournalDate);
        }
        foreach (var exp in journal.ExpenseItems.Where(e => e.SupplierId.HasValue))
        {
            await _ledger.PostPaymentAsync(exp.SupplierId!.Value, exp.Amount, TransactionSourceType.DailyJournalPayment, exp.Id, journal.JournalDate);
        }

        return journal;
    }

    // ------------------------------------------------------------------ الأجور

    private async Task RecordAttendanceAsync(DateTime day, bool twoShifts)
    {
        var list = _workers.Select(w =>
        {
            double r = _rnd.NextDouble();
            var status = r < 0.85 ? AttendanceStatus.Present : r < 0.93 ? AttendanceStatus.HalfDay : AttendanceStatus.Absent;
            return new WorkerAttendance
            {
                WorkerId = w.Id,
                WorkerName = w.WorkerName,
                WorkDate = day,
                Status = status,
                ShiftType = twoShifts ? ShiftType.FullDay : ShiftType.FullDay,
                SnapshotDailyWage = w.DailyWage
            };
        }).ToList();
        await _wages.SaveAttendanceBatchAsync(list);
    }

    private async Task PayWeeklyWagesAsync(DateTime day)
    {
        foreach (var w in _workers)
        {
            var earned = (await _db.WorkerAttendances.Where(a => a.WorkerId == w.Id && a.WorkDate > day.AddDays(-7) && a.WorkDate <= day)
                .Select(a => a.AccruedWage).ToListAsync()).Sum();
            if (earned <= 0) continue;
            await CreateGeneralExpenseAsync(day, GeneralExpenseType.Salaries, earned, PaymentMethodType.Cash,
                $"أجور الأسبوع - {w.WorkerName}", w);
        }
    }

    // ------------------------------------------------------------------ الموردون والمصروفات العامة

    private async Task CreateSupplierInvoiceAsync(DateTime day)
    {
        var supplier = _suppliers[_invoices % _suppliers.Count];
        var catalog = supplier.Name switch
        {
            var n when n.Contains("لحوم") => new[] { ("لحم عجل", 45m), ("لحم مفروم", 40m), ("كبدة", 30m) },
            var n when n.Contains("خضار") => new[] { ("طماطم", 4m), ("بصل", 3m), ("بطاطا", 3.5m), ("خس", 2.5m) },
            var n when n.Contains("دواجن") => new[] { ("دجاج كامل", 18m), ("صدور دجاج", 26m) },
            var n when n.Contains("مشروبات") => new[] { ("كرتونة مياه", 9m), ("مشروبات غازية", 22m), ("عصائر", 28m) },
            _ => new[] { ("خبز تنور", 0.5m), ("صامولي", 0.25m) }
        };

        var invoice = new SupplierInvoice
        {
            SupplierId = supplier.Id,
            InvoiceDate = day,
            InvoiceNumber = $"INV-{day:MMdd}-{_rnd.Next(100, 999)}",
            Notes = "فاتورة توريد"
        };
        foreach (var (desc, price) in catalog.OrderBy(_ => _rnd.Next()).Take(_rnd.Next(2, catalog.Length + 1)))
        {
            decimal qty = price < 1m ? _rnd.Next(200, 600) : _rnd.Next(5, 40);
            invoice.Items.Add(new SupplierInvoiceItem { Description = desc, Quantity = qty, UnitPrice = price, TotalValue = qty * price });
        }
        invoice.TotalAmount = invoice.Items.Sum(x => x.TotalValue);

        await _ledger.PostInvoiceAsync(invoice);
        _invoices++;

        // بعض الفواتير تُسدد بتحويل مصرفي
        if (_rnd.NextDouble() < 0.35)
        {
            await _ledger.PostPaymentAsync(supplier.Id, Math.Round(invoice.TotalAmount * 0.6m, 0), TransactionSourceType.ExternalPayment, 0,
                day, targetInvoiceId: invoice.Id, receiptNumber: $"TR-{_rnd.Next(10000, 99999)}", notes: "سداد جزئي بتحويل",
                bankAccountId: _banks[0].Id, bankReferenceNumber: $"REF{_rnd.Next(100000, 999999)}");
        }
    }

    private async Task CreateMonthlyExpensesAsync(DateTime day)
    {
        await CreateGeneralExpenseAsync(day, GeneralExpenseType.Rent, 3500m, PaymentMethodType.BankTransfer, "إيجار المحل للشهر الحالي");
        await CreateGeneralExpenseAsync(day, GeneralExpenseType.Electricity, 380m, PaymentMethodType.Cash, "فاتورة الكهرباء");
        await CreateGeneralExpenseAsync(day, GeneralExpenseType.Water, 90m, PaymentMethodType.Cash, "فاتورة المياه");
    }

    private async Task CreateGeneralExpenseAsync(DateTime day, GeneralExpenseType type, decimal amount, PaymentMethodType method, string description, Worker? worker = null)
    {
        var expense = new GeneralExpense
        {
            ExpenseType = type,
            Amount = amount,
            PaymentDate = day,
            PaymentMethod = method,
            Description = description,
            WorkerId = worker?.Id,
            WorkerName = worker?.WorkerName,
            BankAccountId = method == PaymentMethodType.BankTransfer ? _banks[0].Id : null,
            FinancialStatus = FinancialStatus.Draft
        };
        _db.GeneralExpenses.Add(expense);
        await _db.SaveChangesAsync();
        _generalExpenses++;

        // نفس مسار شاشة المصروفات العامة: نقدي => صادر من الخزينة، تحويل => حركة مصرفية
        if (method == PaymentMethodType.Cash)
            await _cash.RecordMovementAsync(CashMovementType.CashOut, amount, SourceTypes.GeneralExpense, expense.Id, $"مصروف عام: {description}", day);
        else if (method == PaymentMethodType.BankTransfer)
            await _bank.RecordTransactionAsync(_banks[0].Id, BankTransactionType.ExpensePayment, amount, null, $"مصروف عام: {description}", "GeneralExpense", expense.Id, day);
    }

    // ------------------------------------------------------------------ التسوية

    private async Task SettleAsync(DateTime day)
    {
        // التسوية ترحّل كل المسودات حتى الآن وتسجل حركة الوارد النقدي لكل وردية، ثم يُسحب جزء للمالك
        var balanceBefore = await _cash.GetCurrentBalanceAsync();
        var draftCash = (await _db.DailyJournals.Where(j => j.FinancialStatus == FinancialStatus.Draft)
            .Select(j => new { j.ActualCash, j.CashFloat }).ToListAsync()).Sum(j => j.ActualCash - j.CashFloat);
        var available = balanceBefore + draftCash;
        var payout = Math.Floor(available * 0.7m / 100m) * 100m;
        var keep = available - payout;

        var result = await _posting.SettleAndLockPeriodAsync(payout, keep, $"تسوية النصف الأول حتى {day:yyyy/MM/dd}", _adminId);
        if (!result.Success)
            throw new InvalidOperationException("فشلت التسوية: " + string.Join(" | ", result.Errors));
        _log.AppendLine($"تسوية وإقفال حتى {day:yyyy/MM/dd}: سحب المالك {payout:N2} والاحتفاظ بـ {keep:N2}");
    }
}
