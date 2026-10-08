using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MeezanPOS.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static MeezanPOS.Infrastructure.Reports.ReportStyle;

namespace MeezanPOS.Infrastructure.Reports;

/// <summary>
/// تقرير الأداء المالي واللوحة التحليلية: مؤشرات، رسم المبيعات والمصروفات، توزيع المصروفات، التنبيهات وآخر العمليات.
/// </summary>
/// <remarks>
/// أعيدت كتابته بـ QuestPDF (كان بـ PdfSharp). الرسوم البيانية تُولَّد SVG بأرقام وتواريخ فقط،
/// والنصوص العربية (العناوين ومفتاح الألوان) تُكتب بنصوص QuestPDF لضمان تشكيل الحروف.
/// </remarks>
public static class DashboardPdfExporter
{
    private const string SalesColor = "#3B82F6";
    private const string ExpenseColor = "#EF4444";
    private static readonly string[] SliceColors =
        { "#3B82F6", "#EF4444", "#10B981", "#F59E0B", "#8B5CF6", "#EC4899", "#06B6D4", "#14B8A6", "#F97316", "#6366F1" };

    public static void GenerateReport(string filePath, DashboardExportData data, List<DailySalesPoint> salesPoints, List<ExpenseCategory> expenseCategories)
        => Build(data, salesPoints, expenseCategories).GeneratePdf(filePath);

    /// <summary>المستند دون حفظ (للمعاينة كصور في الاختبارات).</summary>
    public static IDocument Build(DashboardExportData data, List<DailySalesPoint> salesPoints, List<ExpenseCategory> expenseCategories)
    {
        var points = salesPoints ?? new List<DailySalesPoint>();
        var expenses = (expenseCategories ?? new List<ExpenseCategory>()).Where(e => e.Amount > 0).ToList();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                SetupPage(page);
                page.Header().Element(c => Header(c,
                    "تقرير الأداء المالي واللوحة التحليلية",
                    $"الفترة: {data.PeriodText}",
                    data.ExportTime,
                    "تابع: تقرير الأداء المالي واللوحة التحليلية"));

                page.Content().Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Row(row =>
                    {
                        row.Spacing(10);
                        row.RelativeItem().Element(c => Kpi(c, "إجمالي المبيعات", data.TotalSales, data.TotalSalesTrend, data.TotalSalesTrendDirection));
                        row.RelativeItem().Element(c => Kpi(c, "مبيعات نقدي", data.CashSales, data.CashSalesTrend, data.CashSalesTrendDirection));
                        row.RelativeItem().Element(c => Kpi(c, "مبيعات خدمات مصرفية", data.CardSales, data.CardSalesTrend, data.CardSalesTrendDirection));
                    });
                    col.Item().Row(row =>
                    {
                        row.Spacing(10);
                        row.RelativeItem().Element(c => Kpi(c, "إجمالي المصروفات", data.TotalExpenses, data.TotalExpensesTrend, data.TotalExpensesTrendDirection));
                        row.RelativeItem().Element(c => Kpi(c, "صافي الربح", data.NetProfit, data.NetProfitTrend, data.NetProfitTrendDirection));
                        row.RelativeItem().Element(c => Kpi(c, "رصيد الخزينة", data.CashBalance, data.CashBalanceTrend, data.CashBalanceTrendDirection));
                    });

                    col.Item().Height(175).Row(row =>
                    {
                        row.Spacing(10);
                        row.RelativeItem(expenses.Count > 0 ? 1 : 2).Element(c => SalesChart(c, points));
                        if (expenses.Count > 0)
                            row.RelativeItem().Element(c => ExpenseDonut(c, expenses));
                    });

                    if (data.Alerts != null && data.Alerts.Count > 0)
                        col.Item().Element(c => Alerts(c, data.Alerts));

                    if (data.RecentActivities != null && data.RecentActivities.Count > 0)
                        col.Item().Element(c => Activities(c, data.RecentActivities));
                });
            });
        });
    }

    private static void Kpi(IContainer container, string title, decimal value, decimal trend, TrendDirection direction)
    {
        var (label, color) = direction switch
        {
            TrendDirection.Up => ($"▲ {Math.Abs(trend):N1}%", "#10B981"),
            TrendDirection.Down => ($"▼ {Math.Abs(trend):N1}%", "#EF4444"),
            _ => ("مستقر", Muted)
        };
        Card(container).Column(c =>
        {
            c.Item().Text(title).FontSize(9).Bold().FontColor(Muted);
            c.Item().PaddingTop(3).Text(Money(value)).FontSize(12).Bold();
            c.Item().PaddingTop(2).Text(label).FontSize(8).Bold().FontColor(color);
        });
    }

    private static void SalesChart(IContainer container, List<DailySalesPoint> data)
    {
        Card(container).Column(c =>
        {
            c.Item().Row(r =>
            {
                r.RelativeItem().Text("تحليل المبيعات").FontSize(10).Bold();
                r.AutoItem().AlignMiddle().Width(14).Height(3).Background(SalesColor);
                r.AutoItem().PaddingHorizontal(4).Text("المبيعات").FontSize(8).FontColor(SalesColor);
                r.AutoItem().AlignMiddle().Width(14).Height(2).Background(ExpenseColor);
                r.AutoItem().PaddingHorizontal(4).Text("المصروفات").FontSize(8).FontColor(ExpenseColor);
            });

            if (data.Count == 0)
            {
                c.Item().ExtendVertical().AlignCenter().AlignMiddle().Text("لا توجد بيانات مبيعات لهذه الفترة").FontColor("#94A3B8");
                return;
            }

            // الرسم يُبنى من اليسار لليمين بالتسلسل الزمني مثل الأصل
            c.Item().ExtendVertical().PaddingTop(4).ContentFromLeftToRight().Svg(size => LineChartSvg(data, size.Width, size.Height));
        });
    }

    internal static string LineChartSvg(List<DailySalesPoint> data, float width, float height)
    {
        static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        double left = 40, right = 8, top = 8, bottom = 18;
        double w = Math.Max(10, width - left - right), h = Math.Max(10, height - top - bottom);
        double max = (double)data.Max(p => Math.Max(p.TotalSales, p.TotalExpenses));
        double niceMax = Math.Max(100, Math.Ceiling(max / 100.0) * 100.0);
        int n = data.Count;
        double X(int i) => n == 1 ? left + w / 2 : left + i * w / (n - 1);
        double Y(decimal v) => top + h - (double)v / niceMax * h;

        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(width)}\" height=\"{F(height)}\" viewBox=\"0 0 {F(width)} {F(height)}\">");
        for (int g = 0; g < 4; g++)
        {
            double ratio = g / 3.0, gy = top + h - ratio * h;
            if (g > 0) svg.Append($"<line x1=\"{F(left)}\" y1=\"{F(gy)}\" x2=\"{F(left + w)}\" y2=\"{F(gy)}\" stroke=\"#F1F5F9\" stroke-width=\"1\"/>");
            svg.Append($"<text x=\"{F(left - 4)}\" y=\"{F(gy + 3)}\" font-size=\"7\" fill=\"#64748B\" text-anchor=\"end\" font-family=\"Arial\">{(ratio * niceMax):N0}</text>");
        }
        svg.Append($"<line x1=\"{F(left)}\" y1=\"{F(top + h)}\" x2=\"{F(left + w)}\" y2=\"{F(top + h)}\" stroke=\"#CBD5E1\" stroke-width=\"1\"/>");
        svg.Append($"<line x1=\"{F(left)}\" y1=\"{F(top)}\" x2=\"{F(left)}\" y2=\"{F(top + h)}\" stroke=\"#CBD5E1\" stroke-width=\"1\"/>");

        string Poly(Func<DailySalesPoint, decimal> value) =>
            string.Join(" ", data.Select((p, i) => $"{F(X(i))},{F(Y(value(p)))}"));
        svg.Append($"<polyline points=\"{Poly(p => p.TotalSales)}\" fill=\"none\" stroke=\"{SalesColor}\" stroke-width=\"2\"/>");
        svg.Append($"<polyline points=\"{Poly(p => p.TotalExpenses)}\" fill=\"none\" stroke=\"{ExpenseColor}\" stroke-width=\"1.5\" stroke-dasharray=\"4 3\"/>");

        int step = Math.Max(1, n / 5);
        for (int i = 0; i < n; i++)
        {
            var p = data[i];
            if (p.TotalSales > 0)
                svg.Append($"<circle cx=\"{F(X(i))}\" cy=\"{F(Y(p.TotalSales))}\" r=\"2\" fill=\"{SalesColor}\"/>");
            if (p.TotalExpenses > 0)
                svg.Append($"<circle cx=\"{F(X(i))}\" cy=\"{F(Y(p.TotalExpenses))}\" r=\"1.5\" fill=\"{ExpenseColor}\"/>");
            if (i == 0 || i == n - 1 || i % step == 0)
                svg.Append($"<text x=\"{F(X(i))}\" y=\"{F(top + h + 12)}\" font-size=\"7\" fill=\"#64748B\" text-anchor=\"middle\" font-family=\"Arial\">{p.Date:dd/MM}</text>");
        }
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void ExpenseDonut(IContainer container, List<ExpenseCategory> data)
    {
        Card(container).Column(c =>
        {
            c.Item().Text("توزيع المصروفات").FontSize(10).Bold();
            c.Item().ExtendVertical().PaddingTop(4).Row(row =>
            {
                row.RelativeItem(3).Column(legend =>
                {
                    void Group(string title, List<ExpenseCategory> items)
                    {
                        if (items.Count == 0) return;
                        legend.Item().PaddingTop(3).Text(title).FontSize(7.5f).Bold().FontColor(SlateMuted);
                        foreach (var item in items.Take(4))
                        {
                            legend.Item().Row(r =>
                            {
                                r.ConstantItem(6).AlignMiddle().Height(5).Background(SliceColors[data.IndexOf(item) % SliceColors.Length]);
                                r.RelativeItem().PaddingRight(4).Text($"{item.CategoryName}: {Money(item.Amount)} ({item.Percentage:F1}%)").FontSize(7);
                            });
                        }
                    }
                    Group("مصاريف يومية", data.Where(d => d.SourceType == "يومي").ToList());
                    Group("مصاريف عامة", data.Where(d => d.SourceType == "عام").ToList());
                });
                row.RelativeItem(2).AlignMiddle().AlignCenter().Width(90).Height(90).Svg(size => DonutSvg(data, size.Width));
            });
        });
    }

    internal static string DonutSvg(List<ExpenseCategory> data, float size)
    {
        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        double r = size / 2.0, cx = r, cy = r, inner = r * 0.55;
        double total = data.Sum(d => Math.Max(0, d.Percentage));
        var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(size)}\" height=\"{F(size)}\" viewBox=\"0 0 {F(size)} {F(size)}\">");

        if (data.Count == 1 || total <= 0)
        {
            svg.Append($"<circle cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"{F(r)}\" fill=\"{SliceColors[0]}\"/>");
        }
        else
        {
            double angle = -Math.PI / 2;
            for (int i = 0; i < data.Count; i++)
            {
                double sweep = data[i].Percentage / total * 2 * Math.PI;
                if (sweep <= 0) continue;
                double x1 = cx + r * Math.Cos(angle), y1 = cy + r * Math.Sin(angle);
                double x2 = cx + r * Math.Cos(angle + sweep), y2 = cy + r * Math.Sin(angle + sweep);
                int large = sweep > Math.PI ? 1 : 0;
                svg.Append($"<path d=\"M{F(cx)},{F(cy)} L{F(x1)},{F(y1)} A{F(r)},{F(r)} 0 {large} 1 {F(x2)},{F(y2)} Z\" fill=\"{SliceColors[i % SliceColors.Length]}\"/>");
                angle += sweep;
            }
        }
        svg.Append($"<circle cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"{F(inner)}\" fill=\"#FFFFFF\"/>");
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void Alerts(IContainer container, List<DashboardAlert> alerts)
    {
        Card(container).Column(c =>
        {
            c.Item().PaddingBottom(4).Text("التنبيهات الإدارية النشطة").FontSize(10).Bold();
            foreach (var alert in alerts)
            {
                c.Item().PaddingVertical(2).Row(r =>
                {
                    r.ConstantItem(6).AlignMiddle().Height(6).Background(AlertColor(alert));
                    r.RelativeItem().PaddingRight(6).Text(alert.Message).FontSize(9);
                });
            }
        });
    }

    private static string AlertColor(DashboardAlert alert)
    {
        var color = alert.Color ?? string.Empty;
        if (color.StartsWith("#") && (color.Length == 7 || color.Length == 9)) return color;
        if (alert.Type == "Danger" || color.Contains("red")) return "#EF4444";
        if (alert.Type == "Info" || color.Contains("blue")) return "#3B82F6";
        return "#F59E0B";
    }

    private static void Activities(IContainer container, List<RecentActivity> activities)
    {
        Card(container).Column(c =>
        {
            c.Item().PaddingBottom(6).Text("آخر العمليات والتحركات المالية").FontSize(10).Bold();
            c.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(75);
                    cols.RelativeColumn(185);
                    cols.RelativeColumn(85);
                    cols.RelativeColumn(100);
                    cols.RelativeColumn(70);
                });
                table.Header(h =>
                {
                    foreach (var title in new[] { "النوع", "الوصف", "المبلغ", "التاريخ والوقت", "بواسطة" })
                        h.Cell().Element(HeaderCell).Text(title).Bold();
                });
                foreach (var act in activities)
                {
                    var description = act.Description.Length > 45 ? act.Description[..42] + "..." : act.Description;
                    table.Cell().Element(BodyCell).Text(act.ActivityType).FontSize(8);
                    table.Cell().Element(BodyCell).Text(description).FontSize(8);
                    table.Cell().Element(BodyCell).Text($"{act.Amount:N2}").FontSize(8).FontColor(act.IsIncoming ? "#10B981" : "#EF4444");
                    table.Cell().Element(BodyCell).Text($"{act.Timestamp:dd/MM HH:mm}").FontSize(8);
                    table.Cell().Element(BodyCell).Text(act.UserName).FontSize(8);
                }
            });
        });
    }
}
