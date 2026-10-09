using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.Services.Queries;

/// <summary>ما تعرضه اللوحة: كاملة (مالية) أو مبيعات فقط للكاشير.</summary>
public enum DashboardAudience { Full, SalesOnly }

/// <summary>
/// الفترة المعروضة والفترة السابقة للمقارنة. الأسبوع والشهر الجاريان يُقارنان بنفس عدد الأيام
/// المنقضية من السابق (أول 9 أيام بأول 9 أيام)، لا بالفترة السابقة كاملة فتبدو كل الأرقام هابطة.
/// </summary>
public sealed record DashboardRange(DateTime Start, DateTime End, DateTime PrevStart, DateTime PrevEnd)
{
    public static DashboardRange For(DashboardPeriod period, DateTime today, DateTime? customStart = null, DateTime? customEnd = null)
    {
        today = today.Date;
        switch (period)
        {
            case DashboardPeriod.ThisWeek:
            {
                var start = today.AddDays(-(int)today.DayOfWeek); // الأسبوع يبدأ الأحد
                var prevStart = start.AddDays(-7);
                return new(start, start.AddDays(7).AddTicks(-1), prevStart, prevStart.AddDays((today - start).Days + 1).AddTicks(-1));
            }
            case DashboardPeriod.ThisMonth:
            {
                var start = new DateTime(today.Year, today.Month, 1);
                var prevStart = start.AddMonths(-1);
                // الشهر السابق قد يكون أقصر (31 مارس يقابله نهاية فبراير)
                var prevEnd = prevStart.AddDays(today.Day) < start ? prevStart.AddDays(today.Day) : start;
                return new(start, start.AddMonths(1).AddTicks(-1), prevStart, prevEnd.AddTicks(-1));
            }
            case DashboardPeriod.LastMonth:
            {
                var start = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                return new(start, start.AddMonths(1).AddTicks(-1), start.AddMonths(-1), start.AddTicks(-1));
            }
            case DashboardPeriod.Custom:
            {
                var from = (customStart ?? today).Date;
                var to = (customEnd ?? today).Date;
                if (to < from) (from, to) = (to, from);
                var days = (to - from).Days + 1;
                return new(from, to.AddDays(1).AddTicks(-1), from.AddDays(-days), from.AddTicks(-1));
            }
            default:
                return new(today, today.AddDays(1).AddTicks(-1), today.AddDays(-1), today.AddTicks(-1));
        }
    }
}

/// <summary>نسبة التغير عن الفترة السابقة.</summary>
public sealed record DashboardTrend(decimal Percent, TrendDirection Direction)
{
    public static DashboardTrend Of(decimal current, decimal previous)
    {
        if (previous == 0)
            return current == 0 ? new(0, TrendDirection.Flat) : new(100, TrendDirection.Up);
        var percent = Math.Round((current - previous) / Math.Abs(previous) * 100, 1);
        return new(percent, percent > 0 ? TrendDirection.Up : percent < 0 ? TrendDirection.Down : TrendDirection.Flat);
    }
}

/// <summary>عجز وزيادة الدرج لكاشير واحد خلال الفترة.</summary>
public sealed record CashierVariance(string Name, decimal Net, int Journals, int ShortCount);

/// <summary>عجز وزيادة الدرج: الفرق بين النقد الفعلي والمتوقع في اليوميات المرحّلة.</summary>
public sealed record DrawerVarianceSummary(decimal Net, decimal TotalShort, decimal TotalOver, int Journals, int ShortCount, int OverCount,
    IReadOnlyList<CashierVariance> ByCashier);

/// <summary>السيولة مقابل الالتزامات: كم يبقى فعلاً بعد سداد ما على المطعم.</summary>
public sealed record LiquiditySummary(decimal Cash, decimal Banks, decimal PendingCards,
    decimal SupplierPayables, decimal WorkerWagesDue, decimal PartnersNet)
{
    public decimal Assets => Cash + Banks + PendingCards;
    /// <summary>حساب الشركاء التزام فقط إن كان لهم (موجب)؛ إن كان عليهم فلا يُخصم.</summary>
    public decimal PartnersDue => Math.Max(0, PartnersNet);
    public decimal Obligations => SupplierPayables + WorkerWagesDue + PartnersDue;
    public decimal Net => Assets - Obligations;
}

/// <summary>بند في رسم توزيع المصروفات (أعمدة أفقية مرتبة).</summary>
public sealed record ExpenseBar(string Name, decimal Amount, decimal PreviousAmount, double Percentage, double BarRatio, string Color)
{
    public bool IsNew => PreviousAmount == 0 && Amount > 0;
    public decimal? ChangePercent => PreviousAmount == 0 ? null : Math.Round((Amount - PreviousAmount) / PreviousAmount * 100, 0);
    public TrendDirection Direction => ChangePercent is null or 0 ? TrendDirection.Flat : ChangePercent > 0 ? TrendDirection.Up : TrendDirection.Down;
    public string ChangeText => IsNew ? "جديد" : ChangePercent is { } c && c != 0 ? $"{Math.Abs(c):N0}%" : "—";
}

/// <summary>تقدير مستوى نسبة التكلفة مقابل نطاق مرجعي تقريبي.</summary>
public enum RatioLevel { NoSales, Good, Watch, High }

/// <summary>نسبة تكلفة (مشتريات أو أجور) من صافي المبيعات.</summary>
public sealed record CostRatio(string Name, decimal Amount, decimal NetSales, decimal GoodUpTo, decimal WatchUpTo, string Basis)
{
    public decimal? Percent => NetSales > 0 ? Math.Round(Amount / NetSales * 100, 1) : null;
    public RatioLevel Level => Percent is not { } p ? RatioLevel.NoSales : p <= GoodUpTo ? RatioLevel.Good : p <= WatchUpTo ? RatioLevel.Watch : RatioLevel.High;
    /// <summary>عرض الشريط: النسبة حتى 100%.</summary>
    public double BarRatio => Percent is { } p ? (double)Math.Min(p, 100) / 100 : 0;
    public string PercentText => Percent is { } p ? $"{p:0.#}%" : "—";
    public string ReferenceText => $"المرجع التقريبي حتى {GoodUpTo:0}%";
}

/// <summary>متوسط مبيعات يوم من أيام الأسبوع خلال آخر <see cref="DashboardQueryService.WeekdayWindowWeeks"/> أسابيع.</summary>
public sealed record WeekdaySales(DayOfWeek Day, string Name, decimal Average, int Days, double BarRatio, bool IsBest, bool IsWorst);

/// <summary>التقدم نحو هدف مبيعات الشهر الجاري.</summary>
public sealed record MonthlyTargetProgress(DateTime Month, decimal? Target, decimal Sales, int ElapsedDays, int DaysInMonth)
{
    public bool HasTarget => Target is > 0;
    public decimal? Percent => HasTarget ? Math.Round(Sales / Target!.Value * 100, 1) : null;
    public double BarRatio => Percent is { } p ? (double)Math.Min(p, 100) / 100 : 0;
    /// <summary>المتوقع بنهاية الشهر بنفس الوتيرة.</summary>
    public decimal Projection => ElapsedDays > 0 ? Math.Round(Sales / ElapsedDays * DaysInMonth, 2) : 0;
    public int RemainingDays => DaysInMonth - ElapsedDays;
    /// <summary>المطلوب يومياً في الأيام الباقية لبلوغ الهدف (0 إن بُلغ).</summary>
    public decimal? RequiredPerDay => !HasTarget ? null
        : Sales >= Target ? 0
        : RemainingDays > 0 ? Math.Round((Target!.Value - Sales) / RemainingDays, 2) : null;
    public bool OnTrack => HasTarget && Projection >= Target;
    public bool Reached => HasTarget && Sales >= Target;
}

public sealed class DashboardSnapshot
{
    public required DashboardAudience Audience { get; init; }
    public required DashboardRange Range { get; init; }

    public decimal TotalSales { get; init; }
    public decimal CashSales { get; init; }
    public decimal CardSales { get; init; }
    public decimal TotalExpenses { get; init; }
    public decimal PreviousTotalExpenses { get; init; }
    public decimal NetProfit { get; init; }
    public decimal CashBalance { get; init; }

    public DashboardTrend SalesTrend { get; init; } = new(0, TrendDirection.Flat);
    public DashboardTrend CashSalesTrend { get; init; } = new(0, TrendDirection.Flat);
    public DashboardTrend CardSalesTrend { get; init; } = new(0, TrendDirection.Flat);
    public DashboardTrend ExpensesTrend { get; init; } = new(0, TrendDirection.Flat);
    public DashboardTrend ProfitTrend { get; init; } = new(0, TrendDirection.Flat);
    public DashboardTrend CashBalanceTrend { get; init; } = new(0, TrendDirection.Flat);

    public List<DailySalesPoint> SalesPoints { get; init; } = new();
    /// <summary>التفصيل الكامل (يومي/عام) لتقرير PDF.</summary>
    public List<ExpenseCategory> ExpenseDetails { get; init; } = new();
    /// <summary>أهم البنود مدمجة بالاسم، والباقي في "أخرى"، مع المقارنة بالفترة السابقة.</summary>
    public List<ExpenseBar> ExpenseBars { get; init; } = new();
    public List<RecentActivity> Activities { get; init; } = new();
    public List<RecentSaleItem> RecentSales { get; init; } = new();
    public List<DashboardAlert> Alerts { get; init; } = new();

    /// <summary>null للكاشير.</summary>
    public DrawerVarianceSummary? Drawer { get; init; }
    /// <summary>null للكاشير.</summary>
    public LiquiditySummary? Liquidity { get; init; }

    /// <summary>نسبة المشتريات والأجور من صافي المبيعات (null للكاشير).</summary>
    public CostRatio? PurchasesRatio { get; init; }
    public CostRatio? WagesRatio { get; init; }

    /// <summary>أداء أيام الأسبوع (آخر 8 أسابيع، مستقل عن الفترة المختارة).</summary>
    public List<WeekdaySales> Weekdays { get; init; } = new();

    /// <summary>هدف الشهر الجاري (null للكاشير).</summary>
    public MonthlyTargetProgress? MonthlyTarget { get; init; }
}

/// <summary>
/// كل استعلامات لوحة التحكم (كانت داخل DashboardViewModel). الكاشير لا تُحسب له المصروفات
/// والربح والخزينة والالتزامات أصلاً، لا مجرد إخفائها في الواجهة.
/// </summary>
public sealed class DashboardQueryService
{
    public const string LastExternalBackupKey = "LastExternalBackupDate";
    public const string LastAutoBackupKey = "LastAutoBackupDate";
    public const int BackupWarningDays = 7;
    public const int PendingCardWarningDays = 3;
    public const int TopExpenseBars = 5;
    public const string OtherExpensesName = "أخرى";
    public const string MonthlyTargetKey = "MonthlySalesTarget";
    public const int WeekdayWindowWeeks = 8;

    // نطاقات مرجعية تقريبية شائعة للمطاعم (للتلوين فقط)
    public const decimal PurchasesGoodUpTo = 35, PurchasesWatchUpTo = 42;
    public const decimal WagesGoodUpTo = 30, WagesWatchUpTo = 38;

    private static readonly string[] BarColors = { "#185FA5", "#1D9E75", "#534AB7", "#BA7517", "#D85A30" };
    private const string OtherBarColor = "#888780";

    private readonly IDbContextFactory<AppDbContext> _factory;

    public DashboardQueryService(IDbContextFactory<AppDbContext>? factory = null)
        => _factory = factory ?? DefaultDbContextFactory.Instance;

    public async Task<DashboardSnapshot> GetAsync(DashboardRange range, DashboardAudience audience, DateTime today)
    {
        await using var context = await _factory.CreateDbContextAsync();
        bool full = audience == DashboardAudience.Full;

        var journals = await PostedJournals(context, range.Start, range.End).ToListAsync();
        var prevJournals = await PostedJournals(context, range.PrevStart, range.PrevEnd).ToListAsync();
        var general = full ? await PostedGeneralExpenses(context, range.Start, range.End).ToListAsync() : new();
        var prevGeneral = full ? await PostedGeneralExpenses(context, range.PrevStart, range.PrevEnd).ToListAsync() : new();

        decimal sales = journals.Sum(j => j.TotalSales), prevSales = prevJournals.Sum(j => j.TotalSales);
        decimal card = journals.Sum(j => j.BankingTotal), prevCard = prevJournals.Sum(j => j.BankingTotal);
        decimal expenses = full ? journals.Sum(j => j.TotalExpenses) + general.Sum(e => e.Amount) : 0;
        decimal prevExpenses = full ? prevJournals.Sum(j => j.TotalExpenses) + prevGeneral.Sum(e => e.Amount) : 0;
        // الربح من صافي المبيعات (بعد المرتجعات والمجاني) كما في اليومية وقائمة الدخل
        decimal netSales = sales - journals.Sum(j => j.TotalAdjustments);
        decimal profit = netSales - expenses;
        decimal prevProfit = prevSales - prevJournals.Sum(j => j.TotalAdjustments) - prevExpenses;

        decimal cash = 0, prevCash = 0;
        if (full)
        {
            // الرصيد الجاري لآخر حركة (كما في CashLedgerService)، والسابق من مجموع الحركات السارية حتى نهاية الفترة السابقة
            cash = await context.CashMovements.OrderByDescending(m => m.Id).Select(m => (decimal?)m.BalanceAfter).FirstOrDefaultAsync() ?? 0m;
            prevCash = (await context.CashMovements.Where(m => m.TransactionDate <= range.PrevEnd).WhereLive()
                    .Select(m => new { m.Type, m.Amount }).ToListAsync())
                .Sum(m => m.Type == CashMovementType.CashIn ? m.Amount : -m.Amount);
        }

        var (details, bars) = full
            ? await BuildExpensesAsync(context, range, general, prevGeneral)
            : (new List<ExpenseCategory>(), new List<ExpenseBar>());

        return new DashboardSnapshot
        {
            Audience = audience,
            Range = range,
            TotalSales = sales,
            CashSales = sales - card,
            CardSales = card,
            TotalExpenses = expenses,
            PreviousTotalExpenses = prevExpenses,
            NetProfit = full ? profit : 0,
            CashBalance = cash,
            SalesTrend = DashboardTrend.Of(sales, prevSales),
            CashSalesTrend = DashboardTrend.Of(sales - card, prevSales - prevCard),
            CardSalesTrend = DashboardTrend.Of(card, prevCard),
            ExpensesTrend = DashboardTrend.Of(expenses, prevExpenses),
            ProfitTrend = full ? DashboardTrend.Of(profit, prevProfit) : new(0, TrendDirection.Flat),
            CashBalanceTrend = full ? DashboardTrend.Of(cash, prevCash) : new(0, TrendDirection.Flat),
            SalesPoints = BuildSalesPoints(range, today, journals, general, full),
            ExpenseDetails = details,
            ExpenseBars = bars,
            Activities = full ? await RecentActivitiesAsync(context, range) : new(),
            RecentSales = await RecentSalesAsync(context),
            Alerts = await AlertsAsync(context, today, full, sales, expenses, cash),
            Drawer = full ? DrawerVariance(journals) : null,
            Liquidity = full ? await LiquidityAsync(context, cash) : null,
            PurchasesRatio = full ? await PurchasesRatioAsync(context, range, netSales) : null,
            WagesRatio = full ? await WagesRatioAsync(context, range, netSales) : null,
            Weekdays = await WeekdaysAsync(context, today),
            MonthlyTarget = full ? await MonthlyTargetAsync(context, today) : null,
        };
    }

    /// <summary>يحفظ هدف مبيعات الشهر (0 أو null يلغيه).</summary>
    public async Task SetMonthlyTargetAsync(decimal? target)
    {
        if (target < 0)
            throw new ArgumentException("الهدف لا يكون سالباً.");
        await using var context = await _factory.CreateDbContextAsync();
        var setting = await context.Settings.FirstOrDefaultAsync(s => s.Key == MonthlyTargetKey);
        var value = target is > 0 ? target.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        if (setting == null)
            context.Settings.Add(new Setting { Key = MonthlyTargetKey, Value = value });
        else
            setting.Value = value;
        await context.SaveChangesAsync();
    }

    // أنواع مصروفات الدرج التي تُعد مشتريات مواد (الغاز والفحم تشغيل لا مواد)
    private static readonly ExpenseType[] DrawerPurchaseTypes = { ExpenseType.Purchase, ExpenseType.Bread };

    /// <summary>
    /// المشتريات = فواتير الموردين بتاريخها + مشتريات الدرج من غير مورد مسجل.
    /// دفعات الدرج لمورد مسجل تسدد فاتورته المسجلة، فعدّها يضاعف المبلغ.
    /// </summary>
    private static async Task<CostRatio> PurchasesRatioAsync(AppDbContext context, DashboardRange range, decimal netSales)
    {
        var invoices = await context.SupplierInvoices.AsNoTracking()
            .Where(i => i.InvoiceDate >= range.Start && i.InvoiceDate <= range.End)
            .Select(i => i.TotalAmount).ToListAsync();
        var drawer = await context.DailyExpenseItems.AsNoTracking()
            .Where(e => e.SupplierId == null && DrawerPurchaseTypes.Contains(e.Type) &&
                        e.DailyJournal!.JournalDate >= range.Start && e.DailyJournal.JournalDate <= range.End &&
                        (e.DailyJournal.FinancialStatus == FinancialStatus.Posted || e.DailyJournal.FinancialStatus == FinancialStatus.Archived))
            .Select(e => e.Amount).ToListAsync();
        return new CostRatio("المشتريات", invoices.Sum() + drawer.Sum(), netSales, PurchasesGoodUpTo, PurchasesWatchUpTo,
            "فواتير الموردين + مشتريات الدرج من بائعين غير مسجلين");
    }

    /// <summary>
    /// الأجور = الأجور المستحقة للعمال في الفترة (من قسم الأجور) + رواتب أو يوميات مسجلة كمصروف لغير عامل مسجل.
    /// صرف أجر لعامل مسجل يسدد استحقاقه المحسوب أصلاً، فلا يُعد مرة ثانية.
    /// </summary>
    private static async Task<CostRatio> WagesRatioAsync(AppDbContext context, DashboardRange range, decimal netSales)
    {
        var accrued = await context.WorkerTransactions.AsNoTracking()
            .Where(t => t.Type == WorkerTransactionType.WageAccrual && t.TransactionDate >= range.Start && t.TransactionDate <= range.End)
            .Select(t => t.CreditAmount).ToListAsync();
        var salaries = await context.GeneralExpenses.AsNoTracking()
            .Where(e => e.ExpenseType == GeneralExpenseType.Salaries && e.WorkerId == null &&
                        e.PaymentDate >= range.Start && e.PaymentDate <= range.End &&
                        (e.FinancialStatus == FinancialStatus.Posted || e.FinancialStatus == FinancialStatus.Archived))
            .Select(e => e.Amount).ToListAsync();
        var drawerWages = await context.DailyExpenseItems.AsNoTracking()
            .Where(e => e.Type == ExpenseType.WorkerWage && e.WorkerId == null &&
                        e.DailyJournal!.JournalDate >= range.Start && e.DailyJournal.JournalDate <= range.End &&
                        (e.DailyJournal.FinancialStatus == FinancialStatus.Posted || e.DailyJournal.FinancialStatus == FinancialStatus.Archived))
            .Select(e => e.Amount).ToListAsync();
        return new CostRatio("الأجور", accrued.Sum() + salaries.Sum() + drawerWages.Sum(), netSales, WagesGoodUpTo, WagesWatchUpTo,
            "أجور العمال المستحقة + رواتب ويوميات مسجلة كمصروف لغير عامل مسجل");
    }

    private static readonly DayOfWeek[] WeekOrder =
        { DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };

    private static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Saturday => "السبت",
        DayOfWeek.Sunday => "الأحد",
        DayOfWeek.Monday => "الاثنين",
        DayOfWeek.Tuesday => "الثلاثاء",
        DayOfWeek.Wednesday => "الأربعاء",
        DayOfWeek.Thursday => "الخميس",
        _ => "الجمعة",
    };

    /// <summary>متوسط مبيعات كل يوم من أيام الأسبوع على الأيام التي عمل فيها المطعم فقط (أيام الإغلاق لا تُنزل المتوسط).</summary>
    private static async Task<List<WeekdaySales>> WeekdaysAsync(AppDbContext context, DateTime today)
    {
        var from = today.Date.AddDays(-7 * WeekdayWindowWeeks + 1);
        var to = today.Date.AddDays(1).AddTicks(-1);
        var rows = await PostedJournals(context, from, to).Select(j => new { j.JournalDate, j.TotalSales }).ToListAsync();
        var perDate = rows.GroupBy(r => r.JournalDate.Date).Select(g => (Date: g.Key, Sales: g.Sum(r => r.TotalSales))).ToList();
        if (perDate.Count == 0)
            return new();

        var stats = WeekOrder.Select(day =>
        {
            var days = perDate.Where(d => d.Date.DayOfWeek == day).ToList();
            return (Day: day, Avg: days.Count > 0 ? Math.Round(days.Average(d => d.Sales), 2) : 0m, Count: days.Count);
        }).ToList();

        var active = stats.Where(x => x.Count > 0).ToList();
        decimal max = active.Max(x => x.Avg), min = active.Min(x => x.Avg);
        bool distinct = active.Count > 1 && max != min;
        return stats.Select(x => new WeekdaySales(x.Day, DayName(x.Day), x.Avg, x.Count,
                max > 0 ? (double)(x.Avg / max) : 0,
                IsBest: distinct && x.Count > 0 && x.Avg == max,
                IsWorst: distinct && x.Count > 0 && x.Avg == min))
            .ToList();
    }

    private static async Task<MonthlyTargetProgress> MonthlyTargetAsync(AppDbContext context, DateTime today)
    {
        var month = new DateTime(today.Year, today.Month, 1);
        var raw = await context.Settings.AsNoTracking().Where(x => x.Key == MonthlyTargetKey).Select(x => x.Value).FirstOrDefaultAsync();
        decimal? target = decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var t) && t > 0 ? t : null;
        var sales = (await PostedJournals(context, month, today.Date.AddDays(1).AddTicks(-1)).Select(j => j.TotalSales).ToListAsync()).Sum();
        return new MonthlyTargetProgress(month, target, sales, today.Day, DateTime.DaysInMonth(today.Year, today.Month));
    }

    private static IQueryable<DailyJournal> PostedJournals(AppDbContext context, DateTime from, DateTime to) =>
        context.DailyJournals.AsNoTracking().Where(j => j.JournalDate >= from && j.JournalDate <= to &&
            (j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived));

    private static IQueryable<GeneralExpense> PostedGeneralExpenses(AppDbContext context, DateTime from, DateTime to) =>
        context.GeneralExpenses.AsNoTracking().Where(e => e.PaymentDate >= from && e.PaymentDate <= to &&
            (e.FinancialStatus == FinancialStatus.Posted || e.FinancialStatus == FinancialStatus.Archived));

    private static List<DailySalesPoint> BuildSalesPoints(DashboardRange range, DateTime today, List<DailyJournal> journals,
        List<GeneralExpense> general, bool includeExpenses)
    {
        var byDay = journals.GroupBy(j => j.JournalDate.Date)
            .ToDictionary(g => g.Key, g => (Sales: g.Sum(j => j.TotalSales), Expenses: g.Sum(j => j.TotalExpenses)));
        var generalByDay = general.GroupBy(e => e.PaymentDate.Date).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        var points = Enumerable.Range(0, (range.End.Date - range.Start.Date).Days + 1)
            .Select(d => range.Start.Date.AddDays(d))
            .Select(date =>
            {
                byDay.TryGetValue(date, out var day);
                generalByDay.TryGetValue(date, out var gen);
                return new DailySalesPoint
                {
                    Date = date,
                    TotalSales = day.Sales,
                    TotalExpenses = includeExpenses ? day.Expenses + gen : 0,
                };
            })
            .ToList();

        // قصّ الأيام الفارغة في البداية والنهاية حتى يتسع الرسم للأيام الفعلية
        int first = points.FindIndex(p => p.TotalSales > 0 || p.TotalExpenses > 0);
        int last = points.FindLastIndex(p => p.TotalSales > 0 || p.TotalExpenses > 0);
        return first >= 0 ? points.GetRange(first, last - first + 1) : points.Where(p => p.Date == today.Date).ToList();
    }

    private static async Task<(List<ExpenseCategory> Details, List<ExpenseBar> Bars)> BuildExpensesAsync(
        AppDbContext context, DashboardRange range, List<GeneralExpense> general, List<GeneralExpense> prevGeneral)
    {
        async Task<List<(string Name, decimal Amount, string Source)>> LoadAsync(DateTime from, DateTime to, List<GeneralExpense> gen)
        {
            var daily = await context.DailyExpenseItems.AsNoTracking()
                .Where(e => e.DailyJournal!.JournalDate >= from && e.DailyJournal.JournalDate <= to &&
                            (e.DailyJournal.FinancialStatus == FinancialStatus.Posted || e.DailyJournal.FinancialStatus == FinancialStatus.Archived))
                .Select(e => new { Name = e.CategoryName ?? e.Category ?? "مصاريف نثرية أخرى", e.Amount })
                .ToListAsync();
            return daily.Select(d => (d.Name, d.Amount, "يومي"))
                .Concat(gen.Select(e => (GeneralExpenseName(e.ExpenseType, e.CustomExpenseName), e.Amount, "عام")))
                .ToList();
        }

        var current = await LoadAsync(range.Start, range.End, general);
        var previous = await LoadAsync(range.PrevStart, range.PrevEnd, prevGeneral);
        decimal total = current.Sum(c => c.Amount);

        var details = current.GroupBy(c => (c.Name, c.Source))
            .Select(g => new ExpenseCategory
            {
                CategoryName = g.Key.Name,
                SourceType = g.Key.Source,
                Amount = g.Sum(x => x.Amount),
                Percentage = total > 0 ? (double)(g.Sum(x => x.Amount) / total * 100) : 0,
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        // الرسم: البند الواحد يُدمج بالاسم سواء سُجّل في اليومية أو كمصروف عام
        var prevByName = previous.GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var merged = current.GroupBy(c => c.Name)
            .Select(g => (Name: g.Key, Amount: g.Sum(x => x.Amount), Prev: prevByName.GetValueOrDefault(g.Key)))
            .Where(x => x.Amount > 0)
            .OrderByDescending(x => x.Amount)
            .ToList();

        // أهم البنود، والباقي في "أخرى" (إن كان بنداً واحداً يُعرض باسمه)
        var shown = merged.Count <= TopExpenseBars + 1 ? merged : merged.Take(TopExpenseBars).ToList();
        var rest = merged.Skip(shown.Count).ToList();
        var rows = shown.Select((x, i) => (x.Name, x.Amount, x.Prev, Color: BarColors[Math.Min(i, BarColors.Length - 1)])).ToList();
        if (rest.Count > 0)
        {
            var restNames = rest.Select(r => r.Name).ToHashSet();
            rows.Add((OtherExpensesName, rest.Sum(r => r.Amount),
                prevByName.Where(p => restNames.Contains(p.Key)).Sum(p => p.Value), OtherBarColor));
        }

        decimal max = rows.Count > 0 ? rows.Max(r => r.Amount) : 0;
        var bars = rows.Select(r => new ExpenseBar(r.Name, r.Amount, r.Prev,
                total > 0 ? Math.Round((double)(r.Amount / total * 100), 1) : 0,
                max > 0 ? (double)(r.Amount / max) : 0,
                r.Color))
            .ToList();
        return (details, bars);
    }

    private static DrawerVarianceSummary DrawerVariance(List<DailyJournal> journals)
    {
        var byCashier = journals
            .GroupBy(j => string.IsNullOrWhiteSpace(j.EmployeeName) ? "غير محدد" : j.EmployeeName.Trim())
            .Select(g => new CashierVariance(g.Key, g.Sum(j => j.Difference), g.Count(), g.Count(j => j.Difference < 0)))
            .Where(c => c.Net != 0 || c.ShortCount > 0)
            .OrderBy(c => c.Net) // الأكبر عجزاً أولاً
            .ToList();
        return new DrawerVarianceSummary(
            journals.Sum(j => j.Difference),
            journals.Where(j => j.Difference < 0).Sum(j => -j.Difference),
            journals.Where(j => j.Difference > 0).Sum(j => j.Difference),
            journals.Count,
            journals.Count(j => j.Difference < 0),
            journals.Count(j => j.Difference > 0),
            byCashier);
    }

    private static async Task<LiquiditySummary> LiquidityAsync(AppDbContext context, decimal cash)
    {
        // الجمع في الذاكرة: SQLite لا يجمع decimal بدقة
        var banks = (await context.BankAccounts.AsNoTracking().Where(a => a.IsActive).Select(a => a.CurrentBalance).ToListAsync()).Sum();
        var pendingCards = (await context.CardPaymentReconciliations.AsNoTracking()
            .Where(c => c.Status == CardPaymentStatus.Pending).Select(c => c.Amount).ToListAsync()).Sum();
        var payables = (await context.Suppliers.AsNoTracking().Where(s => s.CurrentBalance > 0).Select(s => s.CurrentBalance).ToListAsync()).Sum();

        var workerIds = await context.Workers.AsNoTracking().Select(w => w.Id).ToListAsync();
        var wagesDue = (await context.WorkerTransactions.AsNoTracking()
                .Where(t => workerIds.Contains(t.WorkerId))
                .Select(t => new { t.WorkerId, t.CreditAmount, t.DebitAmount }).ToListAsync())
            .GroupBy(t => t.WorkerId)
            .Select(g => g.Sum(t => t.CreditAmount) - g.Sum(t => t.DebitAmount))
            .Where(balance => balance > 0) // السلف الزائدة على عامل لا تُنقص مستحقات غيره
            .Sum();

        var partnerDebts = (await context.OwnerDebts.AsNoTracking().Select(d => d.Amount).ToListAsync()).Sum();
        var partnerSettlements = (await context.OwnerDebtSettlements.AsNoTracking().Select(s => s.Amount).ToListAsync()).Sum();

        return new LiquiditySummary(cash, banks, pendingCards, payables, wagesDue, partnerDebts - partnerSettlements);
    }

    private static async Task<List<RecentActivity>> RecentActivitiesAsync(AppDbContext context, DashboardRange range)
    {
        DateTime start = range.Start, end = range.End;
        var journals = await context.DailyJournals.AsNoTracking()
            .Where(j => j.JournalDate >= start && j.JournalDate <= end && j.FinancialStatus != FinancialStatus.Draft)
            .OrderByDescending(j => j.JournalDate).Take(10)
            .Select(j => new RecentActivity
            {
                ActivityType = "وردية", Description = $"إقفال وردية - {j.EmployeeName}", Amount = j.TotalSales,
                IsIncoming = true, Timestamp = j.JournalDate, UserName = j.EmployeeName, Icon = "CalendarRange"
            }).ToListAsync();
        var expenses = await context.GeneralExpenses.AsNoTracking()
            .Where(e => e.PaymentDate >= start && e.PaymentDate <= end && e.FinancialStatus != FinancialStatus.Draft)
            .OrderByDescending(e => e.PaymentDate).Take(10)
            .Select(e => new RecentActivity
            {
                ActivityType = "مصروف عام", Description = e.Description, Amount = e.Amount, IsIncoming = false,
                Timestamp = e.PaymentDate, UserName = e.WorkerName ?? "النظام", Icon = "ReceiptText"
            }).ToListAsync();
        var supplierTx = await context.SupplierTransactions.AsNoTracking()
            .Where(t => t.TransactionDate >= start && t.TransactionDate <= end)
            .OrderByDescending(t => t.TransactionDate).Take(10)
            .Select(t => new RecentActivity
            {
                ActivityType = "دفعة مورد", Description = t.Notes ?? $"حركة مورد: {t.Supplier!.Name}", Amount = t.Amount,
                IsIncoming = t.Type == SupplierTransactionType.DecreaseDebt, Timestamp = t.TransactionDate,
                UserName = t.Supplier!.Name, Icon = "Handshake"
            }).ToListAsync();
        var settlements = await context.OwnerDebtSettlements.AsNoTracking()
            .Where(s => s.SettlementDate >= start && s.SettlementDate <= end)
            .OrderByDescending(s => s.SettlementDate).Take(10)
            .Select(s => new RecentActivity
            {
                ActivityType = "سحب مالك", Description = s.Notes ?? $"تسوية سحب شريك: {s.PartnerName}", Amount = s.Amount,
                IsIncoming = false, Timestamp = s.SettlementDate, UserName = s.PartnerName, Icon = "Wallet"
            }).ToListAsync();
        var cashMoves = await context.CashMovements.AsNoTracking()
            .Where(m => m.TransactionDate >= start && m.TransactionDate <= end).WhereLive()
            .OrderByDescending(m => m.TransactionDate).Take(10)
            .Select(m => new RecentActivity
            {
                ActivityType = "حركة خزينة", Description = m.Notes ?? $"حركة نقدية - {m.SourceType}", Amount = m.Amount,
                IsIncoming = m.Type == CashMovementType.CashIn, Timestamp = m.TransactionDate, UserName = "الخزينة", Icon = "CashMultiple"
            }).ToListAsync();

        return journals.Concat(expenses).Concat(supplierTx).Concat(settlements).Concat(cashMoves)
            .OrderByDescending(a => a.Timestamp).Take(10).ToList();
    }

    private static Task<List<RecentSaleItem>> RecentSalesAsync(AppDbContext context) =>
        context.SaleHeaders.AsNoTracking()
            .Where(s => s.FinancialStatus != FinancialStatus.Draft)
            .OrderByDescending(s => s.CreatedAt).Take(10)
            .Select(s => new RecentSaleItem
            {
                InvoiceNo = s.InvoiceNumber,
                Date = s.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                Amount = s.TotalAmount,
                Method = s.PaymentMethod == PaymentMethod.Cash ? "نقدي" : "شبكة"
            }).ToListAsync();

    private static async Task<List<DashboardAlert>> AlertsAsync(AppDbContext context, DateTime today, bool full,
        decimal sales, decimal expenses, decimal cash)
    {
        today = today.Date;
        var alerts = new List<DashboardAlert>();

        var twoDaysAgo = today.AddDays(-2);
        var unposted = await context.DailyJournals.CountAsync(j => j.FinancialStatus == FinancialStatus.Draft && j.JournalDate < twoDaysAgo);
        if (unposted > 0)
            alerts.Add(Alert("Warning", "AlertCircle", "#f59e0b", "Sales",
                $"يوجد {ShiftCountText(unposted)} غير مرحّلة أقدم من يومين. يرجى مراجعتها وترحيلها."));

        if (!full)
            return alerts;

        if (cash < 0)
            alerts.Add(Alert("Danger", "CashRemove", "#ef4444", "Banking",
                $"رصيد الخزينة الحالي سالب ({cash:N2} د.ل). يرجى مراجعة الحركات المالية."));

        if (sales > 0 && expenses / sales > 0.70m)
            alerts.Add(Alert("Warning", "TrendingUp", "#f97316", "Expenses",
                $"نسبة المصروفات مرتفعة جداً وتمثل {expenses / sales:P1} من إجمالي المبيعات للفترة الحالية."));

        // النسخة الاحتياطية الخارجية (اليدوية أو التلقائية عند الإغلاق) — النسخ عند كل تشغيل على نفس الجهاز لا تكفي
        var backupDates = await context.Settings.AsNoTracking()
            .Where(s => s.Key == LastExternalBackupKey || s.Key == LastAutoBackupKey).Select(s => s.Value).ToListAsync();
        DateTime? lastBackup = backupDates
            .Select(v => DateTime.TryParse(v, out var d) ? d : (DateTime?)null)
            .Where(d => d != null).DefaultIfEmpty(null).Max();
        if (lastBackup == null)
            alerts.Add(Alert("Danger", "DatabaseAlert", "#ef4444", "Settings",
                "لم تُؤخذ أي نسخة احتياطية خارجية بعد. خذ نسخة الآن من الإعدادات واحفظها على وحدة تخزين أخرى."));
        else if ((today - lastBackup.Value.Date).TotalDays > BackupWarningDays)
            alerts.Add(Alert("Warning", "DatabaseClock", "#f59e0b", "Settings",
                $"آخر نسخة احتياطية خارجية قبل {(today - lastBackup.Value.Date).TotalDays:N0} يوماً ({lastBackup:yyyy/MM/dd}). خذ نسخة جديدة من الإعدادات."));

        var cardCutoff = today.AddDays(-PendingCardWarningDays);
        var pendingCards = await context.CardPaymentReconciliations.AsNoTracking()
            .Where(c => c.Status == CardPaymentStatus.Pending && c.DailyJournal!.JournalDate < cardCutoff)
            .Select(c => c.Amount).ToListAsync();
        if (pendingCards.Count > 0)
            alerts.Add(Alert("Warning", "CreditCardClock", "#f59e0b", "Banking",
                $"يوجد {pendingCards.Count} من مدفوعات البطاقات بقيمة {pendingCards.Sum():N2} د.ل لم تُطابق مع المصرف منذ أكثر من {PendingCardWarningDays} أيام."));

        // الشهر السابق: يوميات مرحّلة لا تغطيها أي تسوية (بعد مرور أسبوع من الشهر الجديد)
        if (today.Day > 7)
        {
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var prevMonthStart = monthStart.AddMonths(-1);
            var dates = await context.DailyJournals.AsNoTracking()
                .Where(j => j.JournalDate >= prevMonthStart && j.JournalDate < monthStart &&
                            (j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived))
                .Select(j => j.JournalDate).ToListAsync();
            var settled = await context.PostingSessions.AsNoTracking()
                .Where(s => s.SessionType == PostingSessionType.Settlement &&
                            (s.Status == PostingSessionStatus.Settled || s.Status == PostingSessionStatus.ReSettled) &&
                            s.PeriodEndDate >= prevMonthStart && s.PeriodStartDate < monthStart)
                .Select(s => new { s.PeriodStartDate, s.PeriodEndDate }).ToListAsync();
            var unsettled = dates.Count(d => !settled.Any(s => s.PeriodStartDate.Date <= d.Date && s.PeriodEndDate.Date >= d.Date));
            if (unsettled > 0)
                alerts.Add(Alert("Info", "CalendarAlert", "#3b82f6", "Sales",
                    $"شهر {prevMonthStart:MM/yyyy} فيه {ShiftCountText(unsettled)} مرحّلة لم تدخل في أي تسوية بعد."));
        }

        var suppliersDue = await context.Suppliers.CountAsync(s => s.IsActive && s.CurrentBalance > 0);
        if (suppliersDue > 0)
            alerts.Add(Alert("Info", "CreditCardClock", "#3b82f6", "Suppliers",
                $"يوجد {suppliersDue} موردين نشطين لديهم أرصدة مستحقة غير مسواة."));

        return alerts;
    }

    private static DashboardAlert Alert(string type, string icon, string color, string target, string message) =>
        new() { Type = type, Icon = icon, Color = color, Target = target, Message = message, IsDismissible = true };

    public static string GeneralExpenseName(GeneralExpenseType type, string? customName)
    {
        if (type == GeneralExpenseType.Other && !string.IsNullOrWhiteSpace(customName))
            return customName;
        return type switch
        {
            GeneralExpenseType.Rent => "إيجار",
            GeneralExpenseType.Electricity => "كهرباء",
            GeneralExpenseType.Water => "ماء",
            GeneralExpenseType.Salaries => "رواتب/أجور عمال",
            GeneralExpenseType.Internet => "إنترنت",
            GeneralExpenseType.Maintenance => "صيانة",
            GeneralExpenseType.Insurance => "تأمين",
            GeneralExpenseType.Taxes => "ضرائب/رسوم",
            GeneralExpenseType.SupplierPayment => "تسديد موردين",
            _ => "أخرى"
        };
    }

    private static string ShiftCountText(int count) => count switch
    {
        1 => "وردية واحدة",
        2 => "ورديتان",
        >= 3 and <= 10 => $"{count} ورديات",
        _ => $"{count} وردية"
    };
}
