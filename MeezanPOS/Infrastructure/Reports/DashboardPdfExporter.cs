using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Infrastructure.Reports;

public class DashboardPdfExporter
{
    // Arabic Shaping Table and Maps
    private struct ArabicGlyph
    {
        public char Isolated;
        public char Final;
        public char Initial;
        public char Medial;
        public bool ConnectsToLeft;

        public ArabicGlyph(char isolated, char final, char initial, char medial, bool connectsToLeft)
        {
            Isolated = isolated;
            Final = final;
            Initial = initial;
            Medial = medial;
            ConnectsToLeft = connectsToLeft;
        }
    }

    private static readonly Dictionary<char, ArabicGlyph> Glyphs = new()
    {
        { 'ء', new ArabicGlyph((char)0xFE80, (char)0xFE80, (char)0xFE80, (char)0xFE80, false) },
        { 'آ', new ArabicGlyph((char)0xFE81, (char)0xFE82, (char)0xFE81, (char)0xFE82, false) },
        { 'أ', new ArabicGlyph((char)0xFE83, (char)0xFE84, (char)0xFE83, (char)0xFE84, false) },
        { 'ؤ', new ArabicGlyph((char)0xFE85, (char)0xFE86, (char)0xFE85, (char)0xFE86, false) },
        { 'إ', new ArabicGlyph((char)0xFE87, (char)0xFE88, (char)0xFE87, (char)0xFE88, false) },
        { 'ئ', new ArabicGlyph((char)0xFE89, (char)0xFE8A, (char)0xFE8B, (char)0xFE8C, true) },
        { 'ا', new ArabicGlyph((char)0xFE8D, (char)0xFE8E, (char)0xFE8D, (char)0xFE8E, false) },
        { 'ب', new ArabicGlyph((char)0xFE8F, (char)0xFE90, (char)0xFE91, (char)0xFE92, true) },
        { 'ة', new ArabicGlyph((char)0xFE93, (char)0xFE94, (char)0xFE93, (char)0xFE94, false) },
        { 'ت', new ArabicGlyph((char)0xFE95, (char)0xFE96, (char)0xFE97, (char)0xFE98, true) },
        { 'ث', new ArabicGlyph((char)0xFE99, (char)0xFE9A, (char)0xFE9B, (char)0xFE9C, true) },
        { 'ج', new ArabicGlyph((char)0xFE9D, (char)0xFE9E, (char)0xFE9F, (char)0xFEA0, true) },
        { 'ح', new ArabicGlyph((char)0xFEA1, (char)0xFEA2, (char)0xFEA3, (char)0xFEA4, true) },
        { 'خ', new ArabicGlyph((char)0xFEA5, (char)0xFEA6, (char)0xFEA7, (char)0xFEA8, true) },
        { 'د', new ArabicGlyph((char)0xFEA9, (char)0xFEAA, (char)0xFEA9, (char)0xFEAA, false) },
        { 'ذ', new ArabicGlyph((char)0xFEAB, (char)0xFEAC, (char)0xFEAB, (char)0xFEAC, false) },
        { 'ر', new ArabicGlyph((char)0xFEAD, (char)0xFEAE, (char)0xFEAD, (char)0xFEAE, false) },
        { 'ز', new ArabicGlyph((char)0xFEAF, (char)0xFEB0, (char)0xFEAF, (char)0xFEB0, false) },
        { 'س', new ArabicGlyph((char)0xFEB1, (char)0xFEB2, (char)0xFEB3, (char)0xFEB4, true) },
        { 'ش', new ArabicGlyph((char)0xFEB5, (char)0xFEB6, (char)0xFEB7, (char)0xFEB8, true) },
        { 'ص', new ArabicGlyph((char)0xFEB9, (char)0xFEBA, (char)0xFEBB, (char)0xFEBC, true) },
        { 'ض', new ArabicGlyph((char)0xFEBD, (char)0xFEBE, (char)0xFEBF, (char)0xFEC0, true) },
        { 'ط', new ArabicGlyph((char)0xFEC1, (char)0xFEC2, (char)0xFEC3, (char)0xFEC4, true) },
        { 'ظ', new ArabicGlyph((char)0xFEC5, (char)0xFEC6, (char)0xFEC7, (char)0xFEC8, true) },
        { 'ع', new ArabicGlyph((char)0xFEC9, (char)0xFECA, (char)0xFECB, (char)0xFECC, true) },
        { 'غ', new ArabicGlyph((char)0xFECD, (char)0xFECE, (char)0xFECF, (char)0xFED0, true) },
        { 'ف', new ArabicGlyph((char)0xFED1, (char)0xFED2, (char)0xFED3, (char)0xFED4, true) },
        { 'ق', new ArabicGlyph((char)0xFED5, (char)0xFED6, (char)0xFED7, (char)0xFED8, true) },
        { 'ك', new ArabicGlyph((char)0xFED9, (char)0xFEDA, (char)0xFEDB, (char)0xFEDC, true) },
        { 'ل', new ArabicGlyph((char)0xFEDD, (char)0xFEDE, (char)0xFEDF, (char)0xFEE0, true) },
        { 'م', new ArabicGlyph((char)0xFEE1, (char)0xFEE2, (char)0xFEE3, (char)0xFEE4, true) },
        { 'ن', new ArabicGlyph((char)0xFEE5, (char)0xFEE6, (char)0xFEE7, (char)0xFEE8, true) },
        { 'ه', new ArabicGlyph((char)0xFEE9, (char)0xFEEA, (char)0xFEEB, (char)0xFEEC, true) },
        { 'و', new ArabicGlyph((char)0xFEED, (char)0xFEEE, (char)0xFEED, (char)0xFEEE, false) },
        { 'ى', new ArabicGlyph((char)0xFEEF, (char)0xFEF0, (char)0xFEEF, (char)0xFEF0, false) },
        { 'ي', new ArabicGlyph((char)0xFEF1, (char)0xFEF2, (char)0xFEF3, (char)0xFEF4, true) }
    };

    public static void GenerateReport(string filePath, DashboardExportData data, List<DailySalesPoint> salesPoints, List<ExpenseCategory> expenseCategories)
    {
        var document = new PdfDocument();
        document.Info.Title = "تقرير الأداء المالي";
        document.Info.Author = "منظومة ميزان";

        int pageCount = 0;
        double y = 0;
        var gfx = CreateNewPage(document, ref pageCount, data, out y);

        double gap = 12;

        // 1. KPI Cards
        double cardWidth = 165;
        double cardHeight = 60;
        double cardGap = 10;

        // Row 1
        DrawKpiCard(gfx, 40, y, cardWidth, cardHeight, "إجمالي المبيعات", data.TotalSales, data.TotalSalesTrend, data.TotalSalesTrendDirection);
        DrawKpiCard(gfx, 40 + cardWidth + cardGap, y, cardWidth, cardHeight, "مبيعات نقدي", data.CashSales, data.CashSalesTrend, data.CashSalesTrendDirection);
        DrawKpiCard(gfx, 40 + 2 * (cardWidth + cardGap), y, cardWidth, cardHeight, "مبيعات خدمات مصرفية", data.CardSales, data.CardSalesTrend, data.CardSalesTrendDirection);

        y += cardHeight + cardGap;

        // Row 2
        DrawKpiCard(gfx, 40, y, cardWidth, cardHeight, "إجمالي المصروفات", data.TotalExpenses, data.TotalExpensesTrend, data.TotalExpensesTrendDirection);
        DrawKpiCard(gfx, 40 + cardWidth + cardGap, y, cardWidth, cardHeight, "صافي الربح", data.NetProfit, data.NetProfitTrend, data.NetProfitTrendDirection);
        DrawKpiCard(gfx, 40 + 2 * (cardWidth + cardGap), y, cardWidth, cardHeight, "رصيد الخزينة", data.CashBalance, data.CashBalanceTrend, data.CashBalanceTrendDirection);

        y += cardHeight + gap;

        // 2. Charts
        if (salesPoints != null || expenseCategories != null)
        {
            double chartHeight = 170;
            var activeExpenses = expenseCategories?.Where(e => e.Amount > 0).ToList() ?? new();
            bool hasExpenses = activeExpenses.Count > 0;

            if (hasExpenses)
            {
                DrawSalesLineChart(gfx, 40, y, 240, chartHeight, "تحليل المبيعات", salesPoints ?? new());
                DrawExpenseDonutWithLegend(gfx, 295, y, 260, chartHeight, "توزيع المصروفات", expenseCategories ?? new());
            }
            else
            {
                // Full width for Sales Chart if no expenses exist (width = 515)
                DrawSalesLineChart(gfx, 40, y, 515, chartHeight, "تحليل المبيعات", salesPoints ?? new());
            }

            y += chartHeight + gap;
        }

        // 3. Alerts
        if (data.Alerts != null && data.Alerts.Count > 0)
        {
            double alertsHeight = 12 + 25 + (data.Alerts.Count * 16) + 12;
            if (y + alertsHeight > 750)
            {
                gfx.Dispose();
                gfx = CreateNewPage(document, ref pageCount, data, out y);
            }
            DrawAlertsCard(gfx, 40, y, 515, data.Alerts, out double drawnHeight);
            y += drawnHeight + gap;
        }

        // 4. Recent Activities
        if (data.RecentActivities != null && data.RecentActivities.Count > 0)
        {
            double tableHeight = 12 + 25 + 22 + (data.RecentActivities.Count * 20) + 12;
            if (y + tableHeight > 750)
            {
                gfx.Dispose();
                gfx = CreateNewPage(document, ref pageCount, data, out y);
            }
            DrawActivitiesTable(gfx, 40, y, 515, data.RecentActivities, out double drawnHeight);
            y += drawnHeight + gap;
        }

        // Dispose the main drawing gfx so that footers can be drawn
        gfx.Dispose();

        // Add footers on all pages
        int totalPages = document.Pages.Count;
        for (int i = 0; i < totalPages; i++)
        {
            var page = document.Pages[i];
            using (var fGfx = XGraphics.FromPdfPage(page))
            {
                DrawFooter(fGfx, i + 1, totalPages);
            }
        }

        document.Save(filePath);
    }

    private static XGraphics CreateNewPage(PdfDocument document, ref int pageCount, DashboardExportData data, out double y)
    {
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);
        pageCount++;

        // Background color
        var bgBrush = new XSolidBrush(XColor.FromArgb(248, 249, 255));
        gfx.DrawRectangle(bgBrush, 0, 0, page.Width.Point, page.Height.Point);

        y = 40;
        if (pageCount == 1)
        {
            DrawHeader(gfx, ref y, data);
        }
        else
        {
            var titleFont = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
            var titleBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));
            var titleRect = new XRect(40, y, 515, 15);
            DrawText(gfx, "تابع: تقرير الأداء المالي واللوحة التحليلية", titleFont, titleBrush, titleRect, XStringAlignment.Far);

            y += 20;
            var linePen = new XPen(XColor.FromArgb(229, 238, 255), 1);
            gfx.DrawLine(linePen, 40, y, 555, y);
            y += 15;
        }

        return gfx;
    }

    private static void DrawHeader(XGraphics gfx, ref double y, DashboardExportData data)
    {
        var logo = TryLoadLogo();
        double logoSize = 45;
        double logoX = 555 - logoSize;

        if (logo != null)
        {
            try
            {
                gfx.DrawImage(logo, logoX, y, logoSize, logoSize);
            }
            catch
            {
                logo = null;
            }
        }

        if (logo == null)
        {
            var logoBg = new XSolidBrush(XColor.FromArgb(59, 130, 246));
            var logoRect = new XRect(logoX, y, logoSize, logoSize);
            gfx.DrawRoundedRectangle(new XPen(XColor.FromArgb(59, 130, 246)), logoBg, logoRect, new XSize(6, 6));

            var logoFont = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
            var logoTextRect = new XRect(logoX, y, logoSize, logoSize);
            var format = new XStringFormat
            {
                Alignment = XStringAlignment.Center,
                LineAlignment = XLineAlignment.Center
            };
            gfx.DrawString(ToRtl("ميزان"), logoFont, XBrushes.White, logoTextRect, format);
        }

        double titleEndX = logoX - 15;
        double titleWidth = titleEndX - 40;

        var titleRect = new XRect(40, y, titleWidth, 20);
        var titleFont = new XFont("Segoe UI", 16, XFontStyleEx.Bold);
        var titleBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));
        DrawText(gfx, "تقرير الأداء المالي واللوحة التحليلية", titleFont, titleBrush, titleRect, XStringAlignment.Far);

        var subtitleRect = new XRect(40, y + 20, titleWidth, 15);
        var subtitleFont = new XFont("Segoe UI", 10, XFontStyleEx.Regular);
        var subtitleBrush = new XSolidBrush(XColor.FromArgb(118, 119, 125));
        string periodStr = $"الفترة: {data.PeriodText}";
        DrawText(gfx, periodStr, subtitleFont, subtitleBrush, subtitleRect, XStringAlignment.Far);

        var metaRect1 = new XRect(40, y, 150, 15);
        var metaFont = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
        var metaBrush = new XSolidBrush(XColor.FromArgb(118, 119, 125));
        DrawText(gfx, $"تاريخ التصدير: {data.ExportTime:yyyy-MM-dd}", metaFont, metaBrush, metaRect1, XStringAlignment.Near);

        var metaRect2 = new XRect(40, y + 15, 150, 15);
        DrawText(gfx, $"وقت التصدير: {data.ExportTime:HH:mm:ss}", metaFont, metaBrush, metaRect2, XStringAlignment.Near);

        y += Math.Max(logoSize, 35) + 15;

        var linePen = new XPen(XColor.FromArgb(229, 238, 255), 1.5);
        gfx.DrawLine(linePen, 40, y, 555, y);

        y += 15;
    }

    private static void DrawKpiCard(XGraphics gfx, double x, double y, double width, double height, string title, decimal val, decimal trend, TrendDirection trendDir)
    {
        var rect = new XRect(x, y, width, height);
        var bgBrush = XBrushes.White;
        var borderPen = new XPen(XColor.FromArgb(229, 238, 255), 1);
        gfx.DrawRoundedRectangle(borderPen, bgBrush, rect, new XSize(8, 8));

        var titleRect = new XRect(x + 10, y + 8, width - 20, 15);
        var titleFont = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
        var titleBrush = new XSolidBrush(XColor.FromArgb(118, 119, 125));
        DrawText(gfx, title, titleFont, titleBrush, titleRect, XStringAlignment.Far);

        var valRect = new XRect(x + 10, y + 23, width - 20, 20);
        var valFont = new XFont("Segoe UI", 12, XFontStyleEx.Bold);
        var valBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));
        string valText = $"{val:N2} د.ل";
        DrawText(gfx, valText, valFont, valBrush, valRect, XStringAlignment.Far);

        var trendRect = new XRect(x + 10, y + 43, width - 20, 12);
        var trendFont = new XFont("Segoe UI", 8, XFontStyleEx.Bold);

        var trendSign = trendDir == TrendDirection.Up ? "▲ " : (trendDir == TrendDirection.Down ? "▼ " : "");
        var trendColor = trendDir == TrendDirection.Up ? XColor.FromArgb(16, 185, 129) : (trendDir == TrendDirection.Down ? XColor.FromArgb(239, 68, 68) : XColor.FromArgb(118, 119, 125));
        var trendBrush = new XSolidBrush(trendColor);
        var trendLabel = trendDir == TrendDirection.Flat ? "مستقر" : $"{trendSign}{Math.Abs(trend):N1}%";
        DrawText(gfx, trendLabel, trendFont, trendBrush, trendRect, XStringAlignment.Far);
    }

    private static void DrawSalesLineChart(XGraphics gfx, double x, double y, double width, double height, string title, List<DailySalesPoint> data)
    {
        // 1. Draw container card
        var rect = new XRect(x, y, width, height);
        var bgBrush = XBrushes.White;
        var borderPen = new XPen(XColor.FromArgb(229, 238, 255), 1);
        gfx.DrawRoundedRectangle(borderPen, bgBrush, rect, new XSize(8, 8));

        // 2. Draw Title (RTL Arabic)
        var titleRect = new XRect(x + 10, y + 8, width - 20, 15);
        var titleFont = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
        var titleBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));
        DrawText(gfx, title, titleFont, titleBrush, titleRect, XStringAlignment.Far);

        // 3. Draw Legend on the left (horizontally aligned with title)
        var legendFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Regular);
        var salesBrush = new XSolidBrush(XColor.FromArgb(59, 130, 246));
        var expBrush = new XSolidBrush(XColor.FromArgb(239, 68, 68));
        
        // Sales Legend Item (Solid Blue Line + Text)
        var salesLegendPen = new XPen(XColor.FromArgb(59, 130, 246), 2.5);
        gfx.DrawLine(salesLegendPen, x + 10, y + 16, x + 24, y + 16);
        var salesTextRect = new XRect(x + 28, y + 10, 40, 12);
        DrawText(gfx, "المبيعات", legendFont, salesBrush, salesTextRect, XStringAlignment.Near);
        
        // Expenses Legend Item (Dashed Red Line + Text)
        var expLegendPen = new XPen(XColor.FromArgb(239, 68, 68), 2);
        expLegendPen.DashStyle = XDashStyle.Dash;
        gfx.DrawLine(expLegendPen, x + 75, y + 16, x + 89, y + 16);
        var expTextRect = new XRect(x + 93, y + 10, 50, 12);
        DrawText(gfx, "المصروفات", legendFont, expBrush, expTextRect, XStringAlignment.Near);

        double marginTop = 42;
        double marginBottom = 25;
        double marginLeft = 42;
        double marginRight = 15;

        double chartWidth = width - marginLeft - marginRight;
        double chartHeight = height - marginTop - marginBottom;

        double chartX = x + marginLeft;
        double chartY = y + marginTop;
        double chartMaxX = x + width - marginRight;
        double chartMaxY = y + height - marginBottom;

        // Draw basic axis lines
        var axisPen = new XPen(XColor.FromArgb(203, 213, 225), 1);
        gfx.DrawLine(axisPen, chartX, chartMaxY, chartMaxX, chartMaxY); // X axis
        gfx.DrawLine(axisPen, chartX, chartY, chartX, chartMaxY); // Y axis

        // Check if there is data
        if (data == null || data.Count == 0)
        {
            var noDataFont = new XFont("Segoe UI", 9, XFontStyleEx.Regular);
            var noDataBrush = new XSolidBrush(XColor.FromArgb(148, 163, 184));
            var noDataRect = new XRect(chartX, chartY, chartWidth, chartHeight);
            DrawText(gfx, "لا توجد بيانات مبيعات لهذه الفترة", noDataFont, noDataBrush, noDataRect, XStringAlignment.Center);
            return;
        }

        // Calculate scaling
        decimal maxVal = data.Max(p => Math.Max(p.TotalSales, p.TotalExpenses));
        if (maxVal < 0) maxVal = 0;
        double roundedMax = Math.Ceiling((double)maxVal / 100.0) * 100.0;
        if (roundedMax <= 0) roundedMax = 100.0;

        // Draw horizontal grid lines and Y-axis labels
        var gridPen = new XPen(XColor.FromArgb(241, 245, 249), 1);
        var labelFont = new XFont("Segoe UI", 7.5, XFontStyleEx.Regular);
        var labelBrush = new XSolidBrush(XColor.FromArgb(100, 116, 139));

        int gridLinesCount = 4;
        for (int i = 0; i < gridLinesCount; i++)
        {
            double ratio = (double)i / (gridLinesCount - 1);
            double valY = chartMaxY - (ratio * chartHeight);
            double gridVal = ratio * roundedMax;

            if (i > 0)
            {
                gfx.DrawLine(gridPen, chartX, valY, chartMaxX, valY);
            }

            var labelRect = new XRect(x + 5, valY - 6, marginLeft - 10, 12);
            DrawText(gfx, gridVal.ToString("N0"), labelFont, labelBrush, labelRect, XStringAlignment.Far);
        }

        // Draw Sales and Expenses curves
        int N = data.Count;
        var salesPoints = new List<XPoint>();
        var expensesPoints = new List<XPoint>();

        for (int i = 0; i < N; i++)
        {
            double ptX = (N == 1) 
                ? (chartX + chartWidth / 2) 
                : (chartX + (i * chartWidth / (N - 1)));
                
            double salesY = chartMaxY - ((double)data[i].TotalSales / roundedMax * chartHeight);
            double expensesY = chartMaxY - ((double)data[i].TotalExpenses / roundedMax * chartHeight);

            salesPoints.Add(new XPoint(ptX, salesY));
            expensesPoints.Add(new XPoint(ptX, expensesY));
        }

        // Draw Sales Line (Blue)
        var salesPen = new XPen(XColor.FromArgb(59, 130, 246), 2);
        for (int i = 0; i < N - 1; i++)
        {
            gfx.DrawLine(salesPen, salesPoints[i], salesPoints[i + 1]);
        }
        // Draw Sales points dots (only if TotalSales > 0)
        var salesDotBrush = new XSolidBrush(XColor.FromArgb(59, 130, 246));
        for (int i = 0; i < N; i++)
        {
            if (data[i].TotalSales > 0)
            {
                var pt = salesPoints[i];
                gfx.DrawEllipse(salesDotBrush, pt.X - 2, pt.Y - 2, 4, 4);
            }
        }

        // Draw Expenses Line (Red dashed)
        var expensesPen = new XPen(XColor.FromArgb(239, 68, 68), 1.5);
        expensesPen.DashStyle = XDashStyle.Dash;
        for (int i = 0; i < N - 1; i++)
        {
            gfx.DrawLine(expensesPen, expensesPoints[i], expensesPoints[i + 1]);
        }
        // Draw Expenses points dots (only if TotalExpenses > 0)
        var expensesDotBrush = new XSolidBrush(XColor.FromArgb(239, 68, 68));
        for (int i = 0; i < N; i++)
        {
            if (data[i].TotalExpenses > 0)
            {
                var pt = expensesPoints[i];
                gfx.DrawEllipse(expensesDotBrush, pt.X - 1.5, pt.Y - 1.5, 3, 3);
            }
        }

        // Draw value labels above/below non-zero points
        var valLabelFont = new XFont("Segoe UI", 7, XFontStyleEx.Regular);
        var salesLabelBrush = new XSolidBrush(XColor.FromArgb(29, 78, 216)); // Darker blue for contrast
        var expensesLabelBrush = new XSolidBrush(XColor.FromArgb(239, 68, 68)); // Same red

        for (int i = 0; i < N; i++)
        {
            decimal sVal = data[i].TotalSales;
            if (sVal > 0)
            {
                var pt = salesPoints[i];
                var labelRect = new XRect(pt.X - 30, pt.Y - 14, 60, 10);
                DrawText(gfx, sVal.ToString("N0"), valLabelFont, salesLabelBrush, labelRect, XStringAlignment.Center);
            }

            decimal eVal = data[i].TotalExpenses;
            if (eVal > 0)
            {
                var pt = expensesPoints[i];
                var labelRect = new XRect(pt.X - 30, pt.Y + 4, 60, 10);
                DrawText(gfx, eVal.ToString("N0"), valLabelFont, expensesLabelBrush, labelRect, XStringAlignment.Center);
            }
        }

        // Draw X-axis date labels
        int step = Math.Max(1, N / 5);
        for (int i = 0; i < N; i++)
        {
            if (i == 0 || i == N - 1 || i % step == 0)
            {
                double ptX = salesPoints[i].X;
                var dateRect = new XRect(ptX - 25, chartMaxY + 5, 50, 12);
                DrawText(gfx, data[i].Date.ToString("dd/MM"), labelFont, labelBrush, dateRect, XStringAlignment.Center);
            }
        }
    }

    private static void DrawExpenseDonutWithLegend(XGraphics gfx, double x, double y, double width, double height, string title, List<ExpenseCategory> data)
    {
        // 1. Draw container card
        var rect = new XRect(x, y, width, height);
        var bgBrush = XBrushes.White;
        var borderPen = new XPen(XColor.FromArgb(229, 238, 255), 1);
        gfx.DrawRoundedRectangle(borderPen, bgBrush, rect, new XSize(8, 8));

        // 2. Draw Title (RTL Arabic)
        var titleRect = new XRect(x + 10, y + 8, width - 20, 15);
        var titleFont = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
        var titleBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));
        DrawText(gfx, title, titleFont, titleBrush, titleRect, XStringAlignment.Far);

        double marginTop = 30;

        // Check if there is data
        var validData = data != null ? data.Where(d => d.Amount > 0).ToList() : new();
        if (validData.Count == 0)
        {
            var noDataFont = new XFont("Segoe UI", 9, XFontStyleEx.Regular);
            var noDataBrush = new XSolidBrush(XColor.FromArgb(148, 163, 184));
            var noDataRect = new XRect(x + 10, y + marginTop, width - 20, height - marginTop - 15);
            DrawText(gfx, "لا توجد مصروفات مسجّلة لهذه الفترة", noDataFont, noDataBrush, noDataRect, XStringAlignment.Center);
            return;
        }

        // Define colors
        var colors = new[]
        {
            XColor.FromArgb(59, 130, 246),  // Blue
            XColor.FromArgb(239, 68, 68),  // Red
            XColor.FromArgb(16, 185, 129),  // Emerald/Green
            XColor.FromArgb(245, 158, 11),  // Amber
            XColor.FromArgb(139, 92, 246),  // Purple
            XColor.FromArgb(236, 72, 153),  // Pink
            XColor.FromArgb(6, 182, 212),   // Cyan
            XColor.FromArgb(20, 184, 166),  // Teal
            XColor.FromArgb(249, 115, 22),  // Orange
            XColor.FromArgb(99, 102, 241)   // Indigo
        };

        // Donut dimensions
        double donutWidth = width * 0.40;
        double radius = 42;
        double centerX = x + donutWidth / 2 + 10;
        double centerY = y + marginTop + (height - marginTop) / 2 - 5;

        // Map categories to colors
        var categoryColors = new Dictionary<string, XColor>();
        for (int i = 0; i < validData.Count; i++)
        {
            categoryColors[validData[i].CategoryName] = colors[i % colors.Length];
        }

        // Draw donut pie slices
        double startAngle = 0;
        foreach (var item in validData)
        {
            double sweepAngle = (item.Percentage / 100.0) * 360.0;
            if (sweepAngle <= 0) continue;
            
            var sliceBrush = new XSolidBrush(categoryColors[item.CategoryName]);
            gfx.DrawPie(sliceBrush, centerX - radius, centerY - radius, radius * 2, radius * 2, startAngle, sweepAngle);
            startAngle += sweepAngle;
        }

        // Draw white inner mask to create donut hole
        double innerRadius = radius * 0.55;
        gfx.DrawEllipse(XBrushes.White, centerX - innerRadius, centerY - innerRadius, innerRadius * 2, innerRadius * 2);

        // Draw Legend (Right Column, width ~60%)
        double legendX = x + donutWidth + 10;
        double legendWidth = width * 0.60 - 20;
        double legendY = y + marginTop + 4;

        var dailyExp = validData.Where(c => c.SourceType == "يومي").ToList();
        var generalExp = validData.Where(c => c.SourceType == "عام").ToList();

        var subHeaderFont = new XFont("Segoe UI", 7.5, XFontStyleEx.Bold);
        var itemFont = new XFont("Segoe UI", 7, XFontStyleEx.Regular);
        var subHeaderBrush = new XSolidBrush(XColor.FromArgb(100, 116, 139));
        var textBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));

        double rowHeight = 11;

        if (dailyExp.Count > 0)
        {
            var shRect = new XRect(legendX, legendY, legendWidth, 12);
            DrawText(gfx, "مصاريف يومية", subHeaderFont, subHeaderBrush, shRect, XStringAlignment.Far);
            legendY += 12;

            foreach (var item in dailyExp.Take(4))
            {
                DrawLegendItem(gfx, legendX, legendY, legendWidth, item, categoryColors[item.CategoryName], itemFont, textBrush, rowHeight);
                legendY += rowHeight;
            }
        }

        if (generalExp.Count > 0)
        {
            legendY += 3;
            var shRect = new XRect(legendX, legendY, legendWidth, 12);
            DrawText(gfx, "مصاريف عامة", subHeaderFont, subHeaderBrush, shRect, XStringAlignment.Far);
            legendY += 12;

            foreach (var item in generalExp.Take(4))
            {
                DrawLegendItem(gfx, legendX, legendY, legendWidth, item, categoryColors[item.CategoryName], itemFont, textBrush, rowHeight);
                legendY += rowHeight;
            }
        }
    }

    private static void DrawLegendItem(XGraphics gfx, double x, double y, double width, ExpenseCategory item, XColor color, XFont font, XBrush brush, double rowHeight)
    {
        double squareSize = 5;
        double squareX = x + width - squareSize;
        double squareY = y + (rowHeight - squareSize) / 2;

        gfx.DrawRectangle(new XSolidBrush(color), squareX, squareY, squareSize, squareSize);

        double textWidth = width - squareSize - 4;
        var textRect = new XRect(x, y, textWidth, rowHeight);

        string displayText = $"{item.CategoryName}: {item.Amount:N2} د.ل ({item.Percentage:F1}%)";
        DrawText(gfx, displayText, font, brush, textRect, XStringAlignment.Far);
    }

    private static void DrawAlertsCard(XGraphics gfx, double x, double y, double width, List<DashboardAlert> alerts, out double height)
    {
        double currentY = y;
        double headerHeight = 25;
        double rowHeight = 16;
        double cardPadding = 12;
        double calculatedHeight = cardPadding + headerHeight + (alerts.Count * rowHeight) + cardPadding;

        var rect = new XRect(x, y, width, calculatedHeight);
        var bgBrush = XBrushes.White;
        var borderPen = new XPen(XColor.FromArgb(229, 238, 255), 1);
        gfx.DrawRoundedRectangle(borderPen, bgBrush, rect, new XSize(8, 8));

        var titleRect = new XRect(x + 12, y + cardPadding, width - 24, headerHeight);
        var titleFont = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
        var titleBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));
        DrawText(gfx, "التنبيهات الإدارية النشطة", titleFont, titleBrush, titleRect, XStringAlignment.Far);

        currentY += cardPadding + headerHeight;
        var alertFont = new XFont("Segoe UI", 9, XFontStyleEx.Regular);

        foreach (var alert in alerts)
        {
            XColor alertColor = XColor.FromArgb(245, 158, 11);
            if (alert.Color.StartsWith("#"))
            {
                try
                {
                    alertColor = XColor.FromArgb((int)(Convert.ToUInt32(alert.Color.Substring(1), 16) | 0xFF000000));
                }
                catch {}
            }
            else if (alert.Type == "Danger" || alert.Color.Contains("red") || alert.Color.Contains("ef4444"))
            {
                alertColor = XColor.FromArgb(239, 68, 68);
            }
            else if (alert.Type == "Warning" || alert.Color.Contains("orange") || alert.Color.Contains("f59e0b"))
            {
                alertColor = XColor.FromArgb(245, 158, 11);
            }
            else if (alert.Type == "Info" || alert.Color.Contains("blue") || alert.Color.Contains("3b82f6"))
            {
                alertColor = XColor.FromArgb(59, 130, 246);
            }

            var alertBrush = new XSolidBrush(alertColor);

            double bulletSize = 6;
            double bulletX = x + width - 12 - bulletSize;
            double bulletY = currentY + (rowHeight - bulletSize) / 2;
            gfx.DrawEllipse(alertBrush, bulletX, bulletY, bulletSize, bulletSize);

            var textRect = new XRect(x + 12, currentY, width - 24 - bulletSize - 6, rowHeight);
            var textBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));
            DrawText(gfx, alert.Message, alertFont, textBrush, textRect, XStringAlignment.Far);

            currentY += rowHeight;
        }

        height = calculatedHeight;
    }

    private static void DrawActivitiesTable(XGraphics gfx, double x, double y, double width, List<RecentActivity> activities, out double height)
    {
        double currentY = y;
        double cardPadding = 12;
        double headerHeight = 25;
        double tableHeaderHeight = 22;
        double rowHeight = 20;

        double calculatedHeight = cardPadding + headerHeight + tableHeaderHeight + (activities.Count * rowHeight) + cardPadding;
        var rect = new XRect(x, y, width, calculatedHeight);

        var bgBrush = XBrushes.White;
        var borderPen = new XPen(XColor.FromArgb(229, 238, 255), 1);
        gfx.DrawRoundedRectangle(borderPen, bgBrush, rect, new XSize(8, 8));

        var titleRect = new XRect(x + 12, y + cardPadding, width - 24, headerHeight);
        var titleFont = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
        var titleBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));
        DrawText(gfx, "آخر العمليات والتحركات المالية", titleFont, titleBrush, titleRect, XStringAlignment.Far);

        currentY += cardPadding + headerHeight;

        double[] colWidths = new double[] { 75, 185, 85, 100, 70 };
        string[] headers = new string[] { "النوع", "الوصف", "المبلغ", "التاريخ والوقت", "بواسطة" };

        var headerBgBrush = new XSolidBrush(XColor.FromArgb(241, 245, 249));
        gfx.DrawRectangle(headerBgBrush, x + 12, currentY, width - 24, tableHeaderHeight);

        var thFont = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
        var thBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));

        double colX = x + width - 12;
        for (int col = 0; col < 5; col++)
        {
            colX -= colWidths[col];
            var cellRect = new XRect(colX, currentY, colWidths[col], tableHeaderHeight);
            var paddedRect = new XRect(cellRect.X + 4, cellRect.Y, cellRect.Width - 8, cellRect.Height);
            
            var state = gfx.Save();
            gfx.IntersectClip(cellRect);
            DrawText(gfx, headers[col], thFont, thBrush, paddedRect, XStringAlignment.Far);
            gfx.Restore(state);
        }

        currentY += tableHeaderHeight;

        var tdFont = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
        var borderRowPen = new XPen(XColor.FromArgb(226, 232, 240), 1);

        foreach (var act in activities)
        {
            gfx.DrawLine(borderRowPen, x + 12, currentY + rowHeight, x + width - 12, currentY + rowHeight);

            colX = x + width - 12;
            for (int col = 0; col < 5; col++)
            {
                colX -= colWidths[col];
                var cellRect = new XRect(colX, currentY, colWidths[col], rowHeight);
                var paddedRect = new XRect(cellRect.X + 4, cellRect.Y, cellRect.Width - 8, cellRect.Height);

                string cellText = "";
                XBrush cellBrush = thBrush;

                switch (col)
                {
                    case 0:
                        cellText = act.ActivityType;
                        break;
                    case 1:
                        cellText = act.Description;
                        if (cellText.Length > 45)
                        {
                            cellText = cellText.Substring(0, 42) + "...";
                        }
                        break;
                    case 2:
                        cellText = $"{act.Amount:N2}";
                        cellBrush = act.IsIncoming ? new XSolidBrush(XColor.FromArgb(16, 185, 129)) : new XSolidBrush(XColor.FromArgb(239, 68, 68));
                        break;
                    case 3:
                        cellText = act.Timestamp.ToString("dd/MM HH:mm");
                        break;
                    case 4:
                        cellText = act.UserName;
                        break;
                }

                var state = gfx.Save();
                gfx.IntersectClip(cellRect);
                DrawText(gfx, cellText, tdFont, cellBrush, paddedRect, XStringAlignment.Far);
                gfx.Restore(state);
            }

            currentY += rowHeight;
        }

        height = calculatedHeight;
    }

    private static void DrawFooter(XGraphics gfx, int pageNumber, int totalPages)
    {
        var linePen = new XPen(XColor.FromArgb(229, 238, 255), 1);
        gfx.DrawLine(linePen, 40, 800, 555, 800);

        var footerFont = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
        var footerBrush = new XSolidBrush(XColor.FromArgb(118, 119, 125));

        var leftRect = new XRect(40, 805, 200, 15);
        DrawText(gfx, "منظومة ميزان للمطاعم", footerFont, footerBrush, leftRect, XStringAlignment.Near);

        var rightRect = new XRect(355, 805, 200, 15);
        string pageStr = $"صفحة {pageNumber} من {totalPages}";
        DrawText(gfx, pageStr, footerFont, footerBrush, rightRect, XStringAlignment.Far);
    }

    private static XImage? TryLoadLogo()
    {
        try
        {
            string[] paths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/Images/mizan_3d_icon_final_transparent.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mizan_3d_icon_final_transparent.png"),
                Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.Parent?.Parent?.FullName ?? "", "mizan_3d_icon_final_transparent.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../mizan_3d_icon_final_transparent.png"),
                @"D:\Meezan sys\mizan_3d_icon_final_transparent.png"
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    return XImage.FromFile(path);
                }
            }
        }
        catch
        {
            // Suppress exception and let it return null
        }
        return null;
    }

    private static void DrawText(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect, XStringAlignment alignment = XStringAlignment.Far, XLineAlignment lineAlignment = XLineAlignment.Center)
    {
        var format = new XStringFormat
        {
            Alignment = alignment,
            LineAlignment = lineAlignment
        };
        gfx.DrawString(ToRtl(text), font, brush, rect, format);
    }

    // --- Arabic Shaping Engine ---

    public static string ToRtl(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        string shaped = Shape(text);

        char[] chars = shaped.ToCharArray();
        for (int k = 0; k < chars.Length; k++)
        {
            if (chars[k] == '(') chars[k] = ')';
            else if (chars[k] == ')') chars[k] = '(';
            else if (chars[k] == '[') chars[k] = ']';
            else if (chars[k] == ']') chars[k] = '[';
            else if (chars[k] == '{') chars[k] = '}';
            else if (chars[k] == '}') chars[k] = '{';
            else if (chars[k] == '<') chars[k] = '>';
            else if (chars[k] == '>') chars[k] = '<';
        }
        Array.Reverse(chars);
        string reversed = new string(chars);

        var finalBuilder = new System.Text.StringBuilder();
        int i = 0;
        while (i < reversed.Length)
        {
            if (IsLtrChar(reversed[i]))
            {
                int start = i;
                while (i < reversed.Length && (IsLtrChar(reversed[i]) || IsLtrNeutral(reversed[i], i, reversed)))
                {
                    i++;
                }
                int end = i;
                while (end > start && IsLtrNeutralOnly(reversed[end - 1]))
                {
                    end--;
                }

                char[] sub = reversed.Substring(start, end - start).ToCharArray();
                Array.Reverse(sub);
                finalBuilder.Append(sub);

                for (int k = end; k < i; k++)
                {
                    finalBuilder.Append(reversed[k]);
                }
            }
            else
            {
                finalBuilder.Append(reversed[i]);
                i++;
            }
        }

        return finalBuilder.ToString();
    }

    private static bool IsLtrChar(char c)
    {
        return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
    }

    private static bool IsLtrNeutral(char c, int index, string str)
    {
        if (c == '.' || c == ',' || c == ':' || c == '/' || c == '-' || c == '+' || c == '%' || c == ' ')
        {
            for (int k = index + 1; k < str.Length; k++)
            {
                if (IsLtrChar(str[k])) return true;
                if (str[k] != '.' && str[k] != ',' && str[k] != ':' && str[k] != '/' && str[k] != '-' && str[k] != '+' && str[k] != '%' && str[k] != ' ') break;
            }
        }
        return false;
    }

    private static bool IsLtrNeutralOnly(char c)
    {
        return c == '.' || c == ',' || c == ':' || c == '/' || c == '-' || c == '+' || c == '%' || c == ' ';
    }

    public static string Shape(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == 'ل' && i < input.Length - 1)
            {
                char nextC = input[i + 1];
                char? lamAlefChar = null;
                if (nextC == 'آ') lamAlefChar = 'آ';
                else if (nextC == 'أ') lamAlefChar = 'أ';
                else if (nextC == 'إ') lamAlefChar = 'إ';
                else if (nextC == 'ا') lamAlefChar = 'ا';

                if (lamAlefChar != null)
                {
                    bool linkRight = i > 0 && ConnectsToLeft(input[i - 1]);
                    if (lamAlefChar == 'آ') result.Append(linkRight ? (char)0xFEF6 : (char)0xFEF5);
                    else if (lamAlefChar == 'أ') result.Append(linkRight ? (char)0xFEF8 : (char)0xFEF7);
                    else if (lamAlefChar == 'إ') result.Append(linkRight ? (char)0xFEFA : (char)0xFEF9);
                    else if (lamAlefChar == 'ا') result.Append(linkRight ? (char)0xFEFC : (char)0xFEFB);

                    i++;
                    continue;
                }
            }

            if (Glyphs.TryGetValue(c, out var glyph))
            {
                bool linkRight = i > 0 && ConnectsToLeft(input[i - 1]);
                bool linkLeft = i < input.Length - 1 && ConnectsToRight(input[i + 1]);

                if (linkRight && linkLeft)
                    result.Append(glyph.Medial);
                else if (linkRight)
                    result.Append(glyph.Final);
                else if (linkLeft)
                    result.Append(glyph.Initial);
                else
                    result.Append(glyph.Isolated);
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }

    private static bool ConnectsToLeft(char c)
    {
        return Glyphs.TryGetValue(c, out var glyph) && glyph.ConnectsToLeft;
    }

    private static bool ConnectsToRight(char c)
    {
        return Glyphs.ContainsKey(c) && c != 'ء';
    }
}
