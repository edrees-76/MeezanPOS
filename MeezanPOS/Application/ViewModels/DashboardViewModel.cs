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
using MeezanPOS.Domain.Entities;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Application.Services.Queries;

namespace MeezanPOS.Application.ViewModels;

/// <summary>حالة سهم التغير في بطاقة: جيد (أخضر) أو سيئ (أحمر) أو محايد، حسب طبيعة الرقم.</summary>
public enum TrendState { Neutral, Good, Bad }

/// <summary>نص التغير عن الفترة السابقة ولونه. للمصروفات: الزيادة سيئة.</summary>
public sealed record KpiTrend(string Text, TrendState State, TrendDirection Direction)
{
    public static readonly KpiTrend None = new(string.Empty, TrendState.Neutral, TrendDirection.Flat);

    public static KpiTrend From(DashboardTrend trend, bool higherIsBetter)
    {
        if (trend.Direction == TrendDirection.Flat)
            return new("بدون تغيير عن الفترة السابقة", TrendState.Neutral, TrendDirection.Flat);
        bool up = trend.Direction == TrendDirection.Up;
        var state = up == higherIsBetter ? TrendState.Good : TrendState.Bad;
        return new($"{Math.Abs(trend.Percent):0.#}% عن الفترة السابقة", state, trend.Direction);
    }
}

public partial class DashboardViewModel : ObservableObject
{
    private readonly DashboardQueryService _queries;
    private readonly DashboardAudience _audience;
    private readonly System.Windows.Threading.DispatcherTimer? _autoRefreshTimer;
    private bool _isTimerStarted;
    private readonly LoadFailureReporter _loadErrors = new();

    // KPI Values
    [ObservableProperty] private decimal totalSales;
    [ObservableProperty] private decimal cashSales;
    [ObservableProperty] private decimal cardSales;
    [ObservableProperty] private decimal totalExpenses;
    [ObservableProperty] private decimal netProfit;
    [ObservableProperty] private decimal cashBalance;

    // Trends (للتقرير)
    [ObservableProperty] private decimal totalSalesTrend;
    [ObservableProperty] private TrendDirection totalSalesTrendDirection;
    [ObservableProperty] private decimal cashSalesTrend;
    [ObservableProperty] private TrendDirection cashSalesTrendDirection;
    [ObservableProperty] private decimal cardSalesTrend;
    [ObservableProperty] private TrendDirection cardSalesTrendDirection;
    [ObservableProperty] private decimal totalExpensesTrend;
    [ObservableProperty] private TrendDirection totalExpensesTrendDirection;
    [ObservableProperty] private decimal netProfitTrend;
    [ObservableProperty] private TrendDirection netProfitTrendDirection;
    [ObservableProperty] private decimal cashBalanceTrend;
    [ObservableProperty] private TrendDirection cashBalanceTrendDirection;

    // Trends (للبطاقات)
    [ObservableProperty] private KpiTrend salesTrendInfo = KpiTrend.None;
    [ObservableProperty] private KpiTrend cashSalesTrendInfo = KpiTrend.None;
    [ObservableProperty] private KpiTrend cardSalesTrendInfo = KpiTrend.None;
    [ObservableProperty] private KpiTrend expensesTrendInfo = KpiTrend.None;
    [ObservableProperty] private KpiTrend profitTrendInfo = KpiTrend.None;
    [ObservableProperty] private KpiTrend cashBalanceTrendInfo = KpiTrend.None;

    // Period selection
    [ObservableProperty] private DashboardPeriod selectedPeriod = DashboardPeriod.Today;
    [ObservableProperty] private DateTime? customStartDate;
    [ObservableProperty] private DateTime? customEndDate;
    [ObservableProperty] private DateTime? lastUpdated;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool isExporting;

    // عجز وزيادة الدرج
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDrawerData), nameof(DrawerHeadline), nameof(DrawerState), nameof(DrawerDetails))]
    private DrawerVarianceSummary? drawer;

    // السيولة والالتزامات
    [ObservableProperty] private LiquiditySummary? liquidity;

    // رسم المصروفات
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpenseChangeText), nameof(ExpenseChangeState))]
    private decimal previousTotalExpenses;

    public ObservableCollection<DailySalesPoint> SalesTrendData { get; } = new();
    /// <summary>التفصيل الكامل لتقرير PDF.</summary>
    public ObservableCollection<ExpenseCategory> ExpenseDistribution { get; } = new();
    public ObservableCollection<ExpenseBar> ExpenseBars { get; } = new();
    public ObservableCollection<DashboardAlert> Alerts { get; } = new();
    public ObservableCollection<RecentActivity> RecentActivities { get; } = new();
    public ObservableCollection<RecentSaleItem> RecentSales { get; } = new();

    [ObservableProperty] private ISeries[] salesTrendSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] xAxes = Array.Empty<Axis>();

    /// <summary>يفتح شاشة أخرى (يضبطه MainViewModel). المفتاح من NavOrder.</summary>
    public Action<string>? NavigateTo { get; set; }

    /// <summary>اللوحة الكاملة (مالية)، أو مبيعات فقط للكاشير.</summary>
    public bool IsFullView => _audience == DashboardAudience.Full;
    public bool IsSalesOnly => !IsFullView;
    public string PageSubtitle => IsFullView ? "لوحة التحكم المالية والتشغيلية" : "لوحة المبيعات والورديات";

    public bool HasDrawerData => Drawer is { Journals: > 0 };
    public TrendState DrawerState => Drawer is null || Drawer.Net == 0 ? TrendState.Neutral : Drawer.Net > 0 ? TrendState.Good : TrendState.Bad;
    public string DrawerHeadline => Drawer is null || Drawer.Net == 0
        ? "متوازن"
        : Drawer.Net < 0 ? $"عجز {Math.Abs(Drawer.Net):N2}" : $"زيادة {Drawer.Net:N2}";
    public string DrawerDetails => Drawer is null
        ? string.Empty
        : $"{Drawer.Journals} يومية · عجز في {Drawer.ShortCount} ({Drawer.TotalShort:N2}) · زيادة في {Drawer.OverCount} ({Drawer.TotalOver:N2})";

    public string ExpenseChangeText => KpiTrend.From(DashboardTrend.Of(TotalExpenses, PreviousTotalExpenses), higherIsBetter: false).Text;
    public TrendState ExpenseChangeState => KpiTrend.From(DashboardTrend.Of(TotalExpenses, PreviousTotalExpenses), higherIsBetter: false).State;

    public DashboardViewModel() : this(new DashboardQueryService(), AppServiceProvider.Resolve<ISessionService>())
    {
    }

    public DashboardViewModel(DashboardQueryService queries, ISessionService session, bool autoLoad = true)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        // الكاشير لا يرى الأرقام المالية (الربح والخزينة والمصروفات والالتزامات)، ولا تُحسب له أصلاً
        _audience = session.HasPermission(Permissions.ClosingAccount) ? DashboardAudience.Full : DashboardAudience.SalesOnly;

        _autoRefreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;

        if (autoLoad)
            _ = RefreshAsync();
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
            _ = RefreshAsync();
    }

    partial void OnTotalExpensesChanged(decimal value)
    {
        OnPropertyChanged(nameof(ExpenseChangeText));
        OnPropertyChanged(nameof(ExpenseChangeState));
    }

    [RelayCommand]
    public async Task ApplyCustomPeriodAsync()
    {
        if (SelectedPeriod == DashboardPeriod.Custom)
            await RefreshAsync();
    }

    /// <summary>فتح الشاشة المرتبطة ببطاقة أو تنبيه أو بند مصروف.</summary>
    [RelayCommand]
    private void Open(string? target)
    {
        if (!string.IsNullOrEmpty(target))
            NavigateTo?.Invoke(target);
    }

    [RelayCommand]
    private void OpenAlert(DashboardAlert? alert)
    {
        if (alert?.Target is { Length: > 0 } target)
            NavigateTo?.Invoke(target);
    }

    [RelayCommand]
    public async Task ExportPdfAsync()
    {
        if (IsExporting || !IsFullView) return;
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

            await Task.Run(() => MeezanPOS.Infrastructure.Reports.DashboardPdfExporter.GenerateReport(
                filePath, exportData, salesPoints, expenseCategories));

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error exporting dashboard to PDF");
            UiThread.Run(() => Dialogs.Show(
                "حدث خطأ أثناء تصدير تقرير الـ PDF: " + ex.Message,
                "خطأ التصدير",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error));
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

        var range = DashboardRange.For(SelectedPeriod, DateTime.Today, CustomStartDate, CustomEndDate);

        try
        {
            var snapshot = await Task.Run(() => _queries.GetAsync(range, _audience, DateTime.Today));
            UiThread.Run(() => Apply(snapshot));
        }
        catch (Exception ex)
        {
            // رسالة واحدة لكل فتح للشاشة، لا مع كل تحديث تلقائي
            _loadErrors.Report(ex, "بيانات لوحة التحكم");
        }
        finally
        {
            IsRefreshing = false;
            UiThread.Run(() =>
            {
                // المؤقت يبدأ بعد أول تحميل
                if (!_isTimerStarted && _autoRefreshTimer != null)
                {
                    _isTimerStarted = true;
                    _autoRefreshTimer.Start();
                }
            });
        }
    }

    /// <summary>نقل نتيجة الاستعلام إلى الخصائص المعروضة (على خيط الواجهة).</summary>
    internal void Apply(DashboardSnapshot s)
    {
        TotalSales = s.TotalSales;
        CashSales = s.CashSales;
        CardSales = s.CardSales;
        TotalExpenses = s.TotalExpenses;
        PreviousTotalExpenses = s.PreviousTotalExpenses;
        NetProfit = s.NetProfit;
        CashBalance = s.CashBalance;

        (TotalSalesTrend, TotalSalesTrendDirection) = (s.SalesTrend.Percent, s.SalesTrend.Direction);
        (CashSalesTrend, CashSalesTrendDirection) = (s.CashSalesTrend.Percent, s.CashSalesTrend.Direction);
        (CardSalesTrend, CardSalesTrendDirection) = (s.CardSalesTrend.Percent, s.CardSalesTrend.Direction);
        (TotalExpensesTrend, TotalExpensesTrendDirection) = (s.ExpensesTrend.Percent, s.ExpensesTrend.Direction);
        (NetProfitTrend, NetProfitTrendDirection) = (s.ProfitTrend.Percent, s.ProfitTrend.Direction);
        (CashBalanceTrend, CashBalanceTrendDirection) = (s.CashBalanceTrend.Percent, s.CashBalanceTrend.Direction);

        SalesTrendInfo = KpiTrend.From(s.SalesTrend, higherIsBetter: true);
        CashSalesTrendInfo = KpiTrend.From(s.CashSalesTrend, higherIsBetter: true);
        CardSalesTrendInfo = KpiTrend.From(s.CardSalesTrend, higherIsBetter: true);
        ExpensesTrendInfo = KpiTrend.From(s.ExpensesTrend, higherIsBetter: false);
        ProfitTrendInfo = KpiTrend.From(s.ProfitTrend, higherIsBetter: true);
        CashBalanceTrendInfo = KpiTrend.From(s.CashBalanceTrend, higherIsBetter: true);

        Drawer = s.Drawer;
        Liquidity = s.Liquidity;

        Replace(SalesTrendData, s.SalesPoints);
        Replace(ExpenseDistribution, s.ExpenseDetails);
        Replace(ExpenseBars, s.ExpenseBars);
        Replace(RecentActivities, s.Activities);
        Replace(RecentSales, s.RecentSales);
        Replace(Alerts, s.Alerts);

        BuildSalesChart(s.SalesPoints);
        LastUpdated = DateTime.Now;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    private void BuildSalesChart(List<DailySalesPoint> points)
    {
        var blue = SKColor.Parse("#3b82f6");
        var red = SKColor.Parse("#ef4444");

        var series = new List<ISeries>
        {
            new LineSeries<double>
            {
                Name = "المبيعات",
                Values = points.Select(p => (double)p.TotalSales).ToArray(),
                Stroke = new SolidColorPaint(blue) { StrokeThickness = 3 },
                Fill = new LinearGradientPaint(new[] { blue.WithAlpha(40), blue.WithAlpha(0) }, new SKPoint(0.5f, 0), new SKPoint(0.5f, 1)),
                GeometrySize = 0
            }
        };
        if (IsFullView)
        {
            series.Add(new LineSeries<double>
            {
                Name = "المصروفات",
                Values = points.Select(p => (double)p.TotalExpenses).ToArray(),
                Stroke = new SolidColorPaint(red) { StrokeThickness = 2, PathEffect = new DashEffect(new float[] { 6, 4 }) },
                Fill = null,
                GeometrySize = 0
            });
        }
        SalesTrendSeries = series.ToArray();
        XAxes = new[] { new Axis { Labels = points.Select(p => p.Date.ToString("dd/MM")).ToArray(), LabelsRotation = 15 } };
    }
}
