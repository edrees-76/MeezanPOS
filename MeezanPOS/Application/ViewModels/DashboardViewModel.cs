using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace MeezanPOS.Application.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty]
    private decimal cashSales = 14250m;

    [ObservableProperty]
    private decimal cardSales = 38900m;

    [ObservableProperty]
    private decimal totalExpenses = 18400m;

    [ObservableProperty]
    private decimal netProfit = 34750m;

    public ISeries[] SalesTrendSeries { get; set; } = new ISeries[]
    {
        new LineSeries<double>
        {
            Values = new double[] { 2, 1, 3, 5, 3, 4, 6 },
            Fill = new SolidColorPaint(SKColors.Blue.WithAlpha(90)),
            Stroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 3 },
            GeometrySize = 10,
            Name = "المبيعات"
        }
    };

    public ISeries[] ExpenseDistributionSeries { get; set; } = new ISeries[]
    {
        new PieSeries<double> { Values = new double[] { 40 }, Name = "رواتب" },
        new PieSeries<double> { Values = new double[] { 30 }, Name = "مواد خام" },
        new PieSeries<double> { Values = new double[] { 15 }, Name = "إيجار" },
        new PieSeries<double> { Values = new double[] { 15 }, Name = "نثريات" }
    };

    public ObservableCollection<RecentSaleItem> RecentSales { get; set; } = new();

    public DashboardViewModel()
    {
        RecentSales.Add(new RecentSaleItem { InvoiceNo = "#INV-001", Date = "24/10/2023 - 14:30", Amount = 450.00m, Method = "نقدي" });
        RecentSales.Add(new RecentSaleItem { InvoiceNo = "#INV-002", Date = "24/10/2023 - 15:15", Amount = 1200.50m, Method = "شبكة" });
        RecentSales.Add(new RecentSaleItem { InvoiceNo = "#INV-003", Date = "24/10/2023 - 16:00", Amount = 85.00m, Method = "نقدي" });
        RecentSales.Add(new RecentSaleItem { InvoiceNo = "#INV-004", Date = "24/10/2023 - 16:45", Amount = 3450.00m, Method = "شبكة" });
    }
}

public class RecentSaleItem
{
    public string InvoiceNo { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
}
