using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Application.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ICashLedgerService _cashLedgerService;
    private readonly System.Windows.Threading.DispatcherTimer? _autoRefreshTimer;
    private bool _isTimerStarted;

    // KPI Values
    [ObservableProperty]
    private decimal totalSales;

    [ObservableProperty]
    private decimal cashSales;

    [ObservableProperty]
    private decimal cardSales;

    [ObservableProperty]
    private decimal totalExpenses;

    [ObservableProperty]
    private decimal netProfit;

    [ObservableProperty]
    private decimal cashBalance;

    // Trends
    [ObservableProperty]
    private decimal totalSalesTrend;

    [ObservableProperty]
    private TrendDirection totalSalesTrendDirection;

    [ObservableProperty]
    private decimal cashSalesTrend;

    [ObservableProperty]
    private TrendDirection cashSalesTrendDirection;

    [ObservableProperty]
    private decimal cardSalesTrend;

    [ObservableProperty]
    private TrendDirection cardSalesTrendDirection;

    [ObservableProperty]
    private decimal totalExpensesTrend;

    [ObservableProperty]
    private TrendDirection totalExpensesTrendDirection;

    [ObservableProperty]
    private decimal netProfitTrend;

    [ObservableProperty]
    private TrendDirection netProfitTrendDirection;

    [ObservableProperty]
    private decimal cashBalanceTrend;

    [ObservableProperty]
    private TrendDirection cashBalanceTrendDirection;

    // Period selection
    [ObservableProperty]
    private DashboardPeriod selectedPeriod = DashboardPeriod.Today;

    [ObservableProperty]
    private DateTime? customStartDate;

    [ObservableProperty]
    private DateTime? customEndDate;

    [ObservableProperty]
    private DateTime? lastUpdated;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private bool isExporting;

    // Data collections
    public ObservableCollection<DailySalesPoint> SalesTrendData { get; }
    public ObservableCollection<ExpenseCategory> ExpenseDistribution { get; }
    public ObservableCollection<DashboardAlert> Alerts { get; }
    public ObservableCollection<RecentActivity> RecentActivities { get; }
    public ObservableCollection<RecentSaleItem> RecentSales { get; }

    // Chart Series (Stubs for WPF binding)
    [ObservableProperty]
    private ISeries[] salesTrendSeries = Array.Empty<ISeries>();

    [ObservableProperty]
    private ISeries[] expenseDistributionSeries = Array.Empty<ISeries>();

    [ObservableProperty]
    private Axis[] xAxes = Array.Empty<Axis>();

    // Parameterless constructor for views/MainViewModel resolution
    public DashboardViewModel() : this(
        AppServiceProvider.Resolve<IDbContextFactory<AppDbContext>>(),
        AppServiceProvider.Resolve<ICashLedgerService>())
    {
    }

    // Main Constructor with Injection
    public DashboardViewModel(IDbContextFactory<AppDbContext> dbContextFactory, ICashLedgerService cashLedgerService)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _cashLedgerService = cashLedgerService ?? throw new ArgumentNullException(nameof(cashLedgerService));

        SalesTrendData = new();
        ExpenseDistribution = new();
        Alerts = new();
        RecentActivities = new();
        RecentSales = new();

        // Initial Data Load
        _ = RefreshAsync();

        // Setup safe auto-refresh timer (5 minutes), but do not start it yet
        _autoRefreshTimer = new System.Windows.Threading.DispatcherTimer();
        _autoRefreshTimer.Interval = TimeSpan.FromMinutes(5);
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
    }

    private void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (IsRefreshing) return;
        _ = RefreshAsync();
    }

    public void StopAutoRefresh()
    {
        if (_autoRefreshTimer != null)
        {
            _autoRefreshTimer.Tick -= AutoRefreshTimer_Tick;
            _autoRefreshTimer.Stop();
        }
    }

    partial void OnSelectedPeriodChanged(DashboardPeriod value)
    {
        if (value != DashboardPeriod.Custom)
        {
            _ = RefreshAsync();
        }
    }

    [RelayCommand]
    public async Task ApplyCustomPeriodAsync()
    {
        if (SelectedPeriod == DashboardPeriod.Custom)
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    public async Task ExportPdfAsync()
    {
        if (IsExporting) return;
        IsExporting = true;

        try
        {
            string periodText = SelectedPeriod switch
            {
                DashboardPeriod.Today => "اليوم",
                DashboardPeriod.ThisWeek => "هذا الأسبوع",
                DashboardPeriod.ThisMonth => "هذا الشهر",
                DashboardPeriod.LastMonth => "الشهر السابق",
                DashboardPeriod.Custom => $"فترة مخصصة (من {CustomStartDate:yyyy-MM-dd} إلى {CustomEndDate:yyyy-MM-dd})",
                _ => "غير محدد"
            };

            var exportData = new DashboardExportData
            {
                PeriodText = periodText,
                ExportTime = DateTime.Now,
                
                TotalSales = TotalSales,
                CashSales = CashSales,
                CardSales = CardSales,
                TotalExpenses = TotalExpenses,
                NetProfit = NetProfit,
                CashBalance = CashBalance,
                
                TotalSalesTrend = TotalSalesTrend,
                TotalSalesTrendDirection = TotalSalesTrendDirection,
                CashSalesTrend = CashSalesTrend,
                CashSalesTrendDirection = CashSalesTrendDirection,
                CardSalesTrend = CardSalesTrend,
                CardSalesTrendDirection = CardSalesTrendDirection,
                TotalExpensesTrend = TotalExpensesTrend,
                TotalExpensesTrendDirection = TotalExpensesTrendDirection,
                NetProfitTrend = NetProfitTrend,
                NetProfitTrendDirection = NetProfitTrendDirection,
                CashBalanceTrend = CashBalanceTrend,
                CashBalanceTrendDirection = CashBalanceTrendDirection,
                
                Alerts = Alerts.ToList(),
                RecentActivities = RecentActivities.ToList()
            };

            string fileName = $"تقرير الأداء المالي - {DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);

            var salesPoints = SalesTrendData.ToList();
            var expenseCategories = ExpenseDistribution.ToList();

            await Task.Run(() =>
            {
                MeezanPOS.Infrastructure.Reports.DashboardPdfExporter.GenerateReport(
                    filePath, 
                    exportData, 
                    salesPoints, 
                    expenseCategories);
            });

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error exporting dashboard to PDF");
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Dialogs.Show(
                    "حدث خطأ أثناء تصدير تقرير الـ PDF: " + ex.Message,
                    "خطأ التصدير",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            });
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;

        // Capture date filter settings from UI Thread
        var period = SelectedPeriod;
        var customStart = CustomStartDate;
        var customEnd = CustomEndDate;

        try
        {
            await Task.Run(async () =>
            {
                var today = DateTime.Today;
                DateTime start = today;
                DateTime end = today.AddDays(1).AddTicks(-1);

                DateTime prevStart = today;
                DateTime prevEnd = today;

                // Calculate period date ranges
                if (period == DashboardPeriod.Today)
                {
                    start = today;
                    end = today.AddDays(1).AddTicks(-1);
                    prevStart = today.AddDays(-1);
                    prevEnd = today.AddTicks(-1);
                }
                else if (period == DashboardPeriod.ThisWeek)
                {
                    int diff = (7 + (today.DayOfWeek - DayOfWeek.Sunday)) % 7;
                    start = today.AddDays(-diff);
                    end = start.AddDays(7).AddTicks(-1);
                    prevStart = start.AddDays(-7);
                    prevEnd = start.AddTicks(-1);
                }
                else if (period == DashboardPeriod.ThisMonth)
                {
                    start = new DateTime(today.Year, today.Month, 1);
                    end = start.AddMonths(1).AddTicks(-1);
                    prevStart = start.AddMonths(-1);
                    prevEnd = start.AddTicks(-1);
                }
                else if (period == DashboardPeriod.LastMonth)
                {
                    start = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                    end = new DateTime(today.Year, today.Month, 1).AddTicks(-1);
                    prevStart = start.AddMonths(-1);
                    prevEnd = start.AddTicks(-1);
                }
                else if (period == DashboardPeriod.Custom)
                {
                    start = customStart ?? today;
                    end = customEnd ?? today;
                    if (end < start)
                    {
                        var temp = start;
                        start = end;
                        end = temp;
                    }
                    end = end.Date.AddDays(1).AddTicks(-1);
                    var duration = (end - start).Days + 1;
                    prevStart = start.AddDays(-duration);
                    prevEnd = start.AddTicks(-1);
                }

                // 1. Fetch live balance from service
                var currentCash = await _cashLedgerService.GetCurrentBalanceAsync();

                // 2. Fetch records using a new context resolved from the factory for this background thread
                await using var context = await _dbContextFactory.CreateDbContextAsync();

                // Load journals for current and previous periods (excluding Draft)
                var currentJournals = await context.DailyJournals
                    .Where(j => j.JournalDate >= start && j.JournalDate <= end && 
                                (j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived))
                    .ToListAsync();

                var prevJournals = await context.DailyJournals
                    .Where(j => j.JournalDate >= prevStart && j.JournalDate <= prevEnd && 
                                (j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived))
                    .ToListAsync();

                // Load general expenses for current and previous periods (excluding Draft)
                var currentGenExpenses = await context.GeneralExpenses
                    .Where(e => e.PaymentDate >= start && e.PaymentDate <= end && 
                                (e.FinancialStatus == FinancialStatus.Posted || e.FinancialStatus == FinancialStatus.Archived))
                    .ToListAsync();

                var prevGenExpenses = await context.GeneralExpenses
                    .Where(e => e.PaymentDate >= prevStart && e.PaymentDate <= prevEnd && 
                                (e.FinancialStatus == FinancialStatus.Posted || e.FinancialStatus == FinancialStatus.Archived))
                    .ToListAsync();

                // 3. Compute KPI Values
                var salesVal = currentJournals.Sum(j => j.TotalSales);
                var prevSalesVal = prevJournals.Sum(j => j.TotalSales);

                var cashSalesVal = currentJournals.Sum(j => j.TotalSales - j.BankingTotal);
                var prevCashSalesVal = prevJournals.Sum(j => j.TotalSales - j.BankingTotal);

                var cardSalesVal = currentJournals.Sum(j => j.BankingTotal);
                var prevCardSalesVal = prevJournals.Sum(j => j.BankingTotal);

                var expDailyVal = currentJournals.Sum(j => j.TotalExpenses);
                var prevExpDailyVal = prevJournals.Sum(j => j.TotalExpenses);

                var expGenVal = currentGenExpenses.Sum(e => e.Amount);
                var prevExpGenVal = prevGenExpenses.Sum(e => e.Amount);

                var expVal = expDailyVal + expGenVal;
                var prevExpVal = prevExpDailyVal + prevExpGenVal;

                var profitVal = salesVal - expVal;
                var prevProfitVal = prevSalesVal - prevExpVal;

                // Cash balance trend comparison
                // من مجموع الحركات السارية حتى نهاية الفترة السابقة: BalanceAfter لآخر حركة بالتاريخ
                // يتأثر بترتيب الإدخال (حركة بتاريخ قديم أُدخلت لاحقاً تحمل رصيداً لاحقاً)
                var prevMovements = await context.CashMovements
                    .Where(m => !m.IsDeleted && m.TransactionDate <= prevEnd)
                    .WhereLive()
                    .Select(m => new { m.Type, m.Amount })
                    .ToListAsync();
                var prevCashBalance = prevMovements.Sum(m => m.Type == CashMovementType.CashIn ? m.Amount : -m.Amount);

                // Trends
                var (salesTrendVal, salesTrendDir) = CalculateTrend(salesVal, prevSalesVal);
                var (cashSalesTrendVal, cashSalesTrendDir) = CalculateTrend(cashSalesVal, prevCashSalesVal);
                var (cardSalesTrendVal, cardSalesTrendDir) = CalculateTrend(cardSalesVal, prevCardSalesVal);
                var (expTrendVal, expTrendDir) = CalculateTrend(expVal, prevExpVal);
                var (profitTrendVal, profitTrendDir) = CalculateTrend(profitVal, prevProfitVal);
                var (cashBalanceTrendVal, cashBalanceTrendDir) = CalculateTrend(currentCash, prevCashBalance);

                // 4. Sales Trend Points
                var dateRange = Enumerable.Range(0, (end.Date - start.Date).Days + 1)
                    .Select(d => start.Date.AddDays(d))
                    .ToList();

                var journalGroups = currentJournals
                    .GroupBy(j => j.JournalDate.Date)
                    .ToDictionary(g => g.Key, g => new {
                        TotalSales = g.Sum(j => j.TotalSales),
                        TotalExpenses = g.Sum(j => j.TotalExpenses)
                    });

                var generalExpGroups = currentGenExpenses
                    .GroupBy(e => e.PaymentDate.Date)
                    .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

                var trendPoints = dateRange.Select(date =>
                {
                    decimal s = 0;
                    decimal e = 0;
                    if (journalGroups.TryGetValue(date, out var jg))
                    {
                        s = jg.TotalSales;
                        e = jg.TotalExpenses;
                    }
                    if (generalExpGroups.TryGetValue(date, out var geAmount))
                    {
                        e += geAmount;
                    }
                    return new DailySalesPoint
                    {
                        Date = date,
                        TotalSales = s,
                        TotalExpenses = e
                    };
                }).ToList();

                // Trim leading and trailing zero-value days to make chart range dynamic
                int firstActive = trendPoints.FindIndex(p => p.TotalSales > 0 || p.TotalExpenses > 0);
                int lastActive = trendPoints.FindLastIndex(p => p.TotalSales > 0 || p.TotalExpenses > 0);
                if (firstActive != -1 && lastActive != -1)
                {
                    trendPoints = trendPoints.GetRange(firstActive, lastActive - firstActive + 1);
                }
                else
                {
                    // If no active data at all, keep only today or a single day to avoid rendering a huge empty chart
                    trendPoints = trendPoints.Where(p => p.Date.Date == today).ToList();
                }


                // 5. Expense distribution categories
                var dailyExpenseItems = await context.DailyExpenseItems
                    .Include(e => e.DailyJournal)
                    .Where(e => e.DailyJournal!.JournalDate >= start && e.DailyJournal.JournalDate <= end && 
                                (e.DailyJournal.FinancialStatus == FinancialStatus.Posted || e.DailyJournal.FinancialStatus == FinancialStatus.Archived))
                    .Select(e => new { Category = e.CategoryName ?? e.Category ?? "مصاريف نثرية أخرى", e.Amount })
                    .ToListAsync();

                var mappedGeneralExpenses = currentGenExpenses.Select(e => new
                {
                    Category = GetGeneralExpenseTypeName(e.ExpenseType, e.CustomExpenseName),
                    e.Amount
                }).ToList();

                var dailyGrouped = dailyExpenseItems
                    .GroupBy(e => e.Category)
                    .Select(g => new ExpenseCategory
                    {
                        CategoryName = g.Key,
                        Amount = g.Sum(e => e.Amount),
                        SourceType = "يومي"
                    })
                    .ToList();

                var generalGrouped = mappedGeneralExpenses
                    .GroupBy(e => e.Category)
                    .Select(g => new ExpenseCategory
                    {
                        CategoryName = g.Key,
                        Amount = g.Sum(e => e.Amount),
                        SourceType = "عام"
                    })
                    .ToList();

                var allGrouped = dailyGrouped.Concat(generalGrouped).ToList();
                var totalExpAmount = allGrouped.Sum(e => e.Amount);
                
                foreach (var item in allGrouped)
                {
                    item.Percentage = totalExpAmount > 0 ? (double)(item.Amount / totalExpAmount * 100) : 0;
                }

                var distribution = allGrouped
                    .OrderByDescending(c => c.Amount)
                    .ToList();

                // 6. Recent activities
                var dbJournals = await context.DailyJournals
                    .Where(j => j.JournalDate >= start && j.JournalDate <= end && j.FinancialStatus != FinancialStatus.Draft)
                    .OrderByDescending(j => j.JournalDate)
                    .Take(10)
                    .Select(j => new RecentActivity
                    {
                        ActivityType = "وردية",
                        Description = $"إقفال وردية - {j.EmployeeName}",
                        Amount = j.TotalSales,
                        IsIncoming = true,
                        Timestamp = j.JournalDate,
                        UserName = j.EmployeeName,
                        Icon = "CalendarRange"
                    })
                    .ToListAsync();

                var dbExpenses = await context.GeneralExpenses
                    .Where(e => e.PaymentDate >= start && e.PaymentDate <= end && e.FinancialStatus != FinancialStatus.Draft)
                    .OrderByDescending(e => e.PaymentDate)
                    .Take(10)
                    .Select(e => new RecentActivity
                    {
                        ActivityType = "مصروف عام",
                        Description = e.Description,
                        Amount = e.Amount,
                        IsIncoming = false,
                        Timestamp = e.PaymentDate,
                        UserName = e.WorkerName ?? "النظام",
                        Icon = "ReceiptText"
                    })
                    .ToListAsync();

                var dbSupplierTx = await context.SupplierTransactions
                    .Include(t => t.Supplier)
                    .Where(t => t.TransactionDate >= start && t.TransactionDate <= end && !t.IsDeleted)
                    .OrderByDescending(t => t.TransactionDate)
                    .Take(10)
                    .Select(t => new RecentActivity
                    {
                        ActivityType = "دفعة مورد",
                        Description = t.Notes ?? $"حركة مورد: {t.Supplier!.Name}",
                        Amount = t.Amount,
                        IsIncoming = t.Type == SupplierTransactionType.DecreaseDebt,
                        Timestamp = t.TransactionDate,
                        UserName = t.Supplier!.Name,
                        Icon = "Handshake"
                    })
                    .ToListAsync();

                var dbOwnerSettlements = await context.OwnerDebtSettlements
                    .Where(s => s.SettlementDate >= start && s.SettlementDate <= end && !s.IsDeleted)
                    .OrderByDescending(s => s.SettlementDate)
                    .Take(10)
                    .Select(s => new RecentActivity
                    {
                        ActivityType = "سحب مالك",
                        Description = s.Notes ?? $"تسوية سحب شريك: {s.PartnerName}",
                        Amount = s.Amount,
                        IsIncoming = false,
                        Timestamp = s.SettlementDate,
                        UserName = s.PartnerName,
                        Icon = "Wallet"
                    })
                    .ToListAsync();

                var dbCashMovements = await context.CashMovements
                    .Where(m => m.TransactionDate >= start && m.TransactionDate <= end && !m.IsDeleted)
                    .WhereLive()
                    .OrderByDescending(m => m.TransactionDate)
                    .Take(10)
                    .Select(m => new RecentActivity
                    {
                        ActivityType = "حركة خزينة",
                        Description = m.Notes ?? $"حركة نقدية - {m.SourceType}",
                        Amount = m.Amount,
                        IsIncoming = m.Type == CashMovementType.CashIn,
                        Timestamp = m.TransactionDate,
                        UserName = "الخزينة",
                        Icon = "CashMultiple"
                    })
                    .ToListAsync();

                var mergedActivities = dbJournals
                    .Concat(dbExpenses)
                    .Concat(dbSupplierTx)
                    .Concat(dbOwnerSettlements)
                    .Concat(dbCashMovements)
                    .OrderByDescending(a => a.Timestamp)
                    .Take(10)
                    .ToList();

                // 7. Recent Sales for legacy UI list
                var recentSalesList = await context.SaleHeaders
                    .Where(s => s.FinancialStatus != FinancialStatus.Draft)
                    .OrderByDescending(s => s.CreatedAt)
                    .Take(10)
                    .Select(s => new RecentSaleItem
                    {
                        InvoiceNo = s.InvoiceNumber,
                        Date = s.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        Amount = s.TotalAmount,
                        Method = s.PaymentMethod == PaymentMethod.Cash ? "نقدي" : "شبكة"
                    })
                    .ToListAsync();

                // 8. Alerts
                var alertsList = new List<DashboardAlert>();

                var twoDaysAgo = DateTime.Today.AddDays(-2);
                var unpostedShiftsCount = await context.DailyJournals
                    .CountAsync(j => j.FinancialStatus == FinancialStatus.Draft && j.JournalDate < twoDaysAgo);
                if (unpostedShiftsCount > 0)
                {
                    alertsList.Add(new DashboardAlert
                    {
                        Type = "Warning",
                        Message = $"يوجد {GetShiftCountText(unpostedShiftsCount)} غير مرحّلة أقدم من يومين. يرجى مراجعتها وترحيلها.",
                        Icon = "AlertCircle",
                        Color = "#f59e0b",
                        IsDismissible = true
                    });
                }

                if (currentCash < 0)
                {
                    alertsList.Add(new DashboardAlert
                    {
                        Type = "Danger",
                        Message = $"تنبيه: رصيد الخزينة الحالي سالب ({currentCash:N2} د.ل). يرجى مراجعة الحركات المالية.",
                        Icon = "CashRemove",
                        Color = "#ef4444",
                        IsDismissible = true
                    });
                }

                if (salesVal > 0)
                {
                    var expenseRatio = expVal / salesVal;
                    if (expenseRatio > 0.70m)
                    {
                        alertsList.Add(new DashboardAlert
                        {
                            Type = "Warning",
                            Message = $"نسبة المصروفات مرتفعة جداً وتمثل {expenseRatio:P1} من إجمالي المبيعات للفترة الحالية.",
                            Icon = "TrendingUp",
                            Color = "#f97316",
                            IsDismissible = true
                        });
                    }
                }

                var overdueSuppliers = await context.Suppliers
                    .CountAsync(s => s.IsActive && s.CurrentBalance > 0);
                if (overdueSuppliers > 0)
                {
                    alertsList.Add(new DashboardAlert
                    {
                        Type = "Info",
                        Message = $"يوجد {overdueSuppliers} موردين نشطين لديهم أرصدة مستحقة غير مسواة.",
                        Icon = "CreditCardClock",
                        Color = "#3b82f6",
                        IsDismissible = true
                    });
                }

                // 9. Update UI properties safely using Dispatcher
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    TotalSales = salesVal;
                    TotalSalesTrend = salesTrendVal;
                    TotalSalesTrendDirection = salesTrendDir;

                    CashSales = cashSalesVal;
                    CashSalesTrend = cashSalesTrendVal;
                    CashSalesTrendDirection = cashSalesTrendDir;

                    CardSales = cardSalesVal;
                    CardSalesTrend = cardSalesTrendVal;
                    CardSalesTrendDirection = cardSalesTrendDir;

                    TotalExpenses = expVal;
                    TotalExpensesTrend = expTrendVal;
                    TotalExpensesTrendDirection = expTrendDir;

                    NetProfit = profitVal;
                    NetProfitTrend = profitTrendVal;
                    NetProfitTrendDirection = profitTrendDir;

                    CashBalance = currentCash;
                    CashBalanceTrend = cashBalanceTrendVal;
                    CashBalanceTrendDirection = cashBalanceTrendDir;

                    SalesTrendData.Clear();
                    foreach (var pt in trendPoints) SalesTrendData.Add(pt);

                    ExpenseDistribution.Clear();
                    foreach (var ed in distribution) ExpenseDistribution.Add(ed);

                    // Build SalesTrendSeries
                    var blueColor = SKColor.Parse("#3b82f6");
                    var redColor = SKColor.Parse("#ef4444");

                    var salesLine = new LineSeries<double>
                    {
                        Name = "المبيعات",
                        Values = trendPoints.Select(p => (double)p.TotalSales).ToArray(),
                        Stroke = new SolidColorPaint(blueColor) { StrokeThickness = 3 },
                        Fill = new LinearGradientPaint(
                            new[] { blueColor.WithAlpha(40), blueColor.WithAlpha(0) },
                            new SKPoint(0.5f, 0),
                            new SKPoint(0.5f, 1)),
                        GeometrySize = 0
                    };

                    var expensesLine = new LineSeries<double>
                    {
                        Name = "المصروفات",
                        Values = trendPoints.Select(p => (double)p.TotalExpenses).ToArray(),
                        Stroke = new SolidColorPaint(redColor)
                        {
                            StrokeThickness = 2,
                            PathEffect = new DashEffect(new float[] { 6, 4 })
                        },
                        Fill = null,
                        GeometrySize = 0
                    };

                    SalesTrendSeries = new ISeries[] { salesLine, expensesLine };

                    // Build X-Axis Labels
                    XAxes = new Axis[]
                    {
                        new Axis
                        {
                            Labels = trendPoints.Select(p => p.Date.ToString("dd/MM")).ToArray(),
                            LabelsRotation = 15
                        }
                    };

                    // Build ExpenseDistributionSeries
                    var pieSeriesList = new List<ISeries>();
                    for (int i = 0; i < distribution.Count; i++)
                    {
                        var ed = distribution[i];
                        pieSeriesList.Add(new PieSeries<double>
                        {
                            Name = ed.CategoryName,
                            Values = new double[] { (double)ed.Amount },
                            InnerRadius = 50,
                            Fill = new SolidColorPaint(GetColorForCategory(ed.CategoryName, i))
                        });
                    }
                    ExpenseDistributionSeries = pieSeriesList.ToArray();

                    RecentActivities.Clear();
                    foreach (var act in mergedActivities) RecentActivities.Add(act);

                    RecentSales.Clear();
                    foreach (var sale in recentSalesList) RecentSales.Add(sale);

                    Alerts.Clear();
                    foreach (var alert in alertsList) Alerts.Add(alert);

                    LastUpdated = DateTime.Now;
                });
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error refreshing dashboard data in background thread");
        }
        finally
        {
            IsRefreshing = false;
            // Start the auto-refresh timer only after the first load succeeds
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (!_isTimerStarted && _autoRefreshTimer != null)
                {
                    _isTimerStarted = true;
                    _autoRefreshTimer.Start();
                }
            });
        }
    }

    private (decimal Trend, TrendDirection Direction) CalculateTrend(decimal current, decimal previous)
    {
        if (previous == 0)
        {
            if (current == 0) return (0, TrendDirection.Flat);
            return (100, TrendDirection.Up);
        }
        var trend = ((current - previous) / previous) * 100;
        var direction = trend > 0 ? TrendDirection.Up : (trend < 0 ? TrendDirection.Down : TrendDirection.Flat);
        return (Math.Round(trend, 2), direction);
    }

    private string GetGeneralExpenseTypeName(GeneralExpenseType type, string? customName)
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

    private SKColor GetColorForCategory(string categoryName, int index)
    {
        var colors = new[]
        {
            SKColor.Parse("#3b82f6"), // Blue
            SKColor.Parse("#ef4444"), // Red
            SKColor.Parse("#10b981"), // Emerald
            SKColor.Parse("#f59e0b"), // Amber
            SKColor.Parse("#8b5cf6"), // Purple
            SKColor.Parse("#ec4899"), // Pink
            SKColor.Parse("#06b6d4"), // Cyan
            SKColor.Parse("#14b8a6"), // Teal
            SKColor.Parse("#f97316"), // Orange
            SKColor.Parse("#6366f1")  // Indigo
        };

        if (index >= 0 && index < colors.Length)
        {
            return colors[index];
        }

        var hash = Math.Abs(categoryName.GetHashCode());
        return colors[hash % colors.Length];
    }

    private static string GetShiftCountText(int count)
    {
        if (count == 1) return "وردية واحدة";
        if (count == 2) return "ورديتان";
        if (count >= 3 && count <= 10) return $"{count} ورديات";
        return $"{count} وردية";
    }
}
