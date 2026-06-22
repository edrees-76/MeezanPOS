using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Infrastructure.Reports;

public class ClosingAccountPdfExporter
{
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

    public static void GenerateReport(string filePath, ClosingAccountSummary summary, bool isComprehensive)
    {
        var document = new PdfDocument();
        document.Info.Title = isComprehensive ? "تقرير الحساب الختامي الشامل" : "تقرير الحساب الختامي المختصر";
        document.Info.Author = "منظومة ميزان";

        int pageCount = 0;
        double y = 0;
        var gfx = CreateNewPage(document, ref pageCount, summary, isComprehensive, out y);

        double gap = 12;

        // 1. KPI Cards Row 1 (Sales / Expenses / Net Profit)
        double cardWidth = 165;
        double cardHeight = 55;
        double cardGap = 10;

        DrawSummaryCard(gfx, 40, y, cardWidth, cardHeight, "إجمالي المبيعات", $"{summary.IncomeStatement.TotalSales:N2} د.ل");
        DrawSummaryCard(gfx, 40 + cardWidth + cardGap, y, cardWidth, cardHeight, "إجمالي المصروفات", $"{summary.IncomeStatement.TotalExpenses:N2} د.ل");
        
        // Draw Net Profit with visual color distinction
        var profitColor = summary.IncomeStatement.NetProfit >= 0 ? XColor.FromArgb(220, 252, 231) : XColor.FromArgb(254, 226, 226); // green or red background
        DrawSummaryCard(gfx, 40 + 2 * (cardWidth + cardGap), y, cardWidth, cardHeight, "صافي الأرباح", $"{summary.IncomeStatement.NetProfit:N2} د.ل", profitColor);

        y += cardHeight + gap;

        // 2. Cash Ledger Summary Box
        double blockWidth = 515;
        double blockHeight = 65;
        DrawCashLedgerSummaryBlock(gfx, 40, y, blockWidth, blockHeight, summary.CashLedger);

        y += blockHeight + gap;

        // 3. Section: Grouped Expenses (Top 5 categories for summary page)
        var topExpenses = summary.ExpensesByCategory.Take(5).ToList();
        DrawExpensesTable(gfx, 40, y, 515, topExpenses, out double expensesHeight);

        y += expensesHeight + gap;

        // 4. Section: Liabilities Summary (The 5 aggregated liability metrics)
        DrawLiabilitiesOverview(gfx, 40, y, 515, summary.Liabilities, out double liabilitiesHeight);
        y += liabilitiesHeight + gap;

        // End of Summary page. Dispose current graphics.
        gfx.Dispose();

        // 5. Detailed Annex Pages (Only generated in Comprehensive mode)
        if (isComprehensive)
        {
            // Supplier Details Page
            if (summary.Suppliers.Any())
            {
                gfx = CreateNewPage(document, ref pageCount, summary, isComprehensive, out y);
                DrawText(gfx, "ملحق رقم (1): تفاصيل أرصدة وحركات الموردين", new XFont("Segoe UI", 12, XFontStyleEx.Bold), new XSolidBrush(XColor.FromArgb(11, 28, 48)), new XRect(40, y, 515, 20), XStringAlignment.Far);
                y += 25;

                // Headers
                DrawSupplierDetailsTable(gfx, ref y, document, ref pageCount, summary, summary.Suppliers);
                gfx.Dispose();
            }

            // Worker Advances Outstanding Page
            if (summary.Liabilities.WorkerAdvanceDetails.Any())
            {
                gfx = CreateNewPage(document, ref pageCount, summary, isComprehensive, out y);
                DrawText(gfx, "ملحق رقم (2): تفاصيل سلف العمال غير المسددة", new XFont("Segoe UI", 12, XFontStyleEx.Bold), new XSolidBrush(XColor.FromArgb(11, 28, 48)), new XRect(40, y, 515, 20), XStringAlignment.Far);
                y += 10;
                DrawText(gfx, "صافي ما يدين به العمال للمطعم (المبالغ المسحوبة كذمة مالية)", new XFont("Segoe UI", 8.5, XFontStyleEx.Italic), new XSolidBrush(XColor.FromArgb(118, 119, 125)), new XRect(40, y, 515, 12), XStringAlignment.Far);
                y += 18;

                DrawWorkerAdvancesTable(gfx, ref y, document, ref pageCount, summary, summary.Liabilities.WorkerAdvanceDetails);
                gfx.Dispose();
            }

            // Worker Unpaid Wages Page
            if (summary.Liabilities.WorkerUnpaidWageDetails.Any())
            {
                gfx = CreateNewPage(document, ref pageCount, summary, isComprehensive, out y);
                DrawText(gfx, "ملحق رقم (3): تفاصيل الأجور المستحقة غير المصروفة للعمال", new XFont("Segoe UI", 12, XFontStyleEx.Bold), new XSolidBrush(XColor.FromArgb(11, 28, 48)), new XRect(40, y, 515, 20), XStringAlignment.Far);
                y += 10;
                DrawText(gfx, "أجور مستحقة للعمال لقاء حضورهم ولم تُصرف لهم نقداً بعد", new XFont("Segoe UI", 8.5, XFontStyleEx.Italic), new XSolidBrush(XColor.FromArgb(118, 119, 125)), new XRect(40, y, 515, 12), XStringAlignment.Far);
                y += 18;

                DrawWorkerUnpaidWagesTable(gfx, ref y, document, ref pageCount, summary, summary.Liabilities.WorkerUnpaidWageDetails);
                gfx.Dispose();
            }

            // Partner Debt Details Page
            if (summary.Liabilities.OwnerDebtDetails.Any())
            {
                gfx = CreateNewPage(document, ref pageCount, summary, isComprehensive, out y);
                DrawText(gfx, "ملحق رقم (4): تفاصيل حسابات ذمم الملاك والشركاء", new XFont("Segoe UI", 12, XFontStyleEx.Bold), new XSolidBrush(XColor.FromArgb(11, 28, 48)), new XRect(40, y, 515, 20), XStringAlignment.Far);
                y += 25;

                DrawOwnerDebtsTable(gfx, ref y, document, ref pageCount, summary, summary.Liabilities.OwnerDebtDetails);
                gfx.Dispose();
            }
        }

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

    private static XGraphics CreateNewPage(PdfDocument document, ref int pageCount, ClosingAccountSummary summary, bool isComprehensive, out double y)
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
            DrawHeader(gfx, ref y, summary, isComprehensive);
        }
        else
        {
            var titleFont = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
            var titleBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));
            var titleRect = new XRect(40, y, 515, 15);
            string followText = isComprehensive ? "تابع: تقرير الحساب الختامي والمركز المالي - الشامل" : "تابع: تقرير الحساب الختامي والمركز المالي - المختصر";
            DrawText(gfx, followText, titleFont, titleBrush, titleRect, XStringAlignment.Far);

            y += 20;
            var linePen = new XPen(XColor.FromArgb(229, 238, 255), 1);
            gfx.DrawLine(linePen, 40, y, 555, y);
            y += 15;
        }

        return gfx;
    }

    private static void DrawHeader(XGraphics gfx, ref double y, ClosingAccountSummary summary, bool isComprehensive)
    {
        var logo = TryLoadLogo();
        double logoSize = 45;
        double logoX = 555 - logoSize;

        if (logo != null)
        {
            try { gfx.DrawImage(logo, logoX, y, logoSize, logoSize); } catch { logo = null; }
        }

        if (logo == null)
        {
            var logoBg = new XSolidBrush(XColor.FromArgb(15, 23, 42)); // dark slate logo
            var logoRect = new XRect(logoX, y, logoSize, logoSize);
            gfx.DrawRoundedRectangle(new XPen(XColor.FromArgb(15, 23, 42)), logoBg, logoRect, new XSize(6, 6));

            var logoFont = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
            gfx.DrawString(ToRtl("ميزان"), logoFont, XBrushes.White, logoRect, new XStringFormat { Alignment = XStringAlignment.Center, LineAlignment = XLineAlignment.Center });
        }

        double titleEndX = logoX - 15;
        double titleWidth = titleEndX - 40;

        var titleRect = new XRect(40, y, titleWidth, 20);
        var titleFont = new XFont("Segoe UI", 15, XFontStyleEx.Bold);
        var titleBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));
        string reportTitle = isComprehensive ? "تقرير الحساب الختامي والمركز المالي (الشامل)" : "تقرير الحساب الختامي والمركز المالي (المختصر)";
        DrawText(gfx, reportTitle, titleFont, titleBrush, titleRect, XStringAlignment.Far);

        var subtitleRect = new XRect(40, y + 20, titleWidth, 15);
        var subtitleFont = new XFont("Segoe UI", 9.5, XFontStyleEx.Regular);
        var subtitleBrush = new XSolidBrush(XColor.FromArgb(118, 119, 125));
        DrawText(gfx, summary.PeriodText, subtitleFont, subtitleBrush, subtitleRect, XStringAlignment.Far);

        var metaRect1 = new XRect(40, y, 150, 15);
        var metaFont = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
        var metaBrush = new XSolidBrush(XColor.FromArgb(118, 119, 125));
        DrawText(gfx, $"تاريخ التصدير: {DateTime.Now:yyyy-MM-dd}", metaFont, metaBrush, metaRect1, XStringAlignment.Near);

        var metaRect2 = new XRect(40, y + 15, 150, 15);
        DrawText(gfx, $"وقت التصدير: {DateTime.Now:HH:mm:ss}", metaFont, metaBrush, metaRect2, XStringAlignment.Near);

        y += Math.Max(logoSize, 35) + 15;

        var linePen = new XPen(XColor.FromArgb(229, 238, 255), 1.5);
        gfx.DrawLine(linePen, 40, y, 555, y);

        y += 15;
    }

    private static void DrawSummaryCard(XGraphics gfx, double x, double y, double width, double height, string title, string val, XColor? bgColor = null)
    {
        var rect = new XRect(x, y, width, height);
        var bgBrush = bgColor.HasValue ? new XSolidBrush(bgColor.Value) : XBrushes.White;
        var borderPen = new XPen(XColor.FromArgb(229, 238, 255), 1);
        gfx.DrawRoundedRectangle(borderPen, bgBrush, rect, new XSize(8, 8));

        var titleRect = new XRect(x + 10, y + 8, width - 20, 15);
        var titleFont = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
        var titleBrush = new XSolidBrush(XColor.FromArgb(118, 119, 125));
        DrawText(gfx, title, titleFont, titleBrush, titleRect, XStringAlignment.Far);

        var valRect = new XRect(x + 10, y + 25, width - 20, 20);
        var valFont = new XFont("Segoe UI", 12, XFontStyleEx.Bold);
        var valBrush = new XSolidBrush(XColor.FromArgb(11, 28, 48));
        DrawText(gfx, val, valFont, valBrush, valRect, XStringAlignment.Far);
    }

    private static void DrawCashLedgerSummaryBlock(XGraphics gfx, double x, double y, double width, double height, CashLedgerReport cashLedger)
    {
        var rect = new XRect(x, y, width, height);
        var bgBrush = XBrushes.White;
        var borderPen = new XPen(XColor.FromArgb(229, 238, 255), 1);
        gfx.DrawRoundedRectangle(borderPen, bgBrush, rect, new XSize(8, 8));

        var titleFont = new XFont("Segoe UI", 9.5, XFontStyleEx.Bold);
        var labelFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Regular);
        var valFont = new XFont("Segoe UI", 9.5, XFontStyleEx.Bold);

        // Section header
        var headerRect = new XRect(x + 10, y + 6, width - 20, 15);
        DrawText(gfx, "ملخص حركة الخزينة النقدية (Cash Ledger)", titleFont, new XSolidBrush(XColor.FromArgb(15, 23, 42)), headerRect, XStringAlignment.Far);

        double rowY = y + 25;
        double colWidth = (width - 20) / 4;

        // Draw 4 columns (Opening, Cash In, Cash Out, Closing)
        string[] labels = new[] { "رصيد أول المدة", "الوارد النقدي (+)", "الصادر النقدي (-)", "الرصيد الختامي" };
        decimal[] values = new[] { cashLedger.OpeningBalance, cashLedger.TotalCashIn, cashLedger.TotalCashOut, cashLedger.ClosingBalance };
        XColor[] colors = new[] { XColor.FromArgb(15, 23, 42), XColor.FromArgb(21, 128, 61), XColor.FromArgb(185, 28, 28), XColor.FromArgb(29, 78, 216) };

        for (int i = 0; i < 4; i++)
        {
            double colX = x + 10 + (3 - i) * colWidth; // Right-to-Left column arrangement
            var lblRect = new XRect(colX, rowY, colWidth, 12);
            DrawText(gfx, labels[i], labelFont, new XSolidBrush(XColor.FromArgb(118, 119, 125)), lblRect, XStringAlignment.Center);

            var valRect = new XRect(colX, rowY + 14, colWidth, 15);
            DrawText(gfx, $"{values[i]:N2} د.ل", valFont, new XSolidBrush(colors[i]), valRect, XStringAlignment.Center);
        }
    }

    private static void DrawExpensesTable(XGraphics gfx, double x, double y, double width, List<ExpenseCategoryReportItem> categories, out double height)
    {
        double titleHeight = 20;
        double headerHeight = 20;
        double rowHeight = 18;
        double calculatedHeight = titleHeight + headerHeight + (categories.Count * rowHeight) + 15;

        // Title
        DrawText(gfx, "أبرز المصروفات حسب فئة الصرف:", new XFont("Segoe UI", 10, XFontStyleEx.Bold), new XSolidBrush(XColor.FromArgb(11, 28, 48)), new XRect(x, y, width, titleHeight), XStringAlignment.Far);

        double tableY = y + titleHeight;

        // Headers
        var thBg = new XSolidBrush(XColor.FromArgb(241, 245, 249));
        gfx.DrawRectangle(thBg, x, tableY, width, headerHeight);
        var borderPen = new XPen(XColor.FromArgb(226, 232, 240), 1);
        gfx.DrawRectangle(borderPen, x, tableY, width, headerHeight);

        var thFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Bold);
        var thBrush = new XSolidBrush(XColor.FromArgb(71, 85, 105));

        DrawText(gfx, "فئة المصروف", thFont, thBrush, new XRect(x + width - 180, tableY, 170, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "قيمة المصروف", thFont, thBrush, new XRect(x + width - 300, tableY, 110, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "النسبة", thFont, thBrush, new XRect(x + width - 420, tableY, 110, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "المصدر", thFont, thBrush, new XRect(x, tableY, 90, headerHeight), XStringAlignment.Far);

        double currentY = tableY + headerHeight;
        var tdFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Regular);
        var tdBoldFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Bold);
        var tdBrush = new XSolidBrush(XColor.FromArgb(15, 23, 42));

        foreach (var cat in categories)
        {
            // Row boundary
            gfx.DrawRectangle(borderPen, x, currentY, width, rowHeight);

            DrawText(gfx, cat.CategoryName, tdBoldFont, tdBrush, new XRect(x + width - 180, currentY, 170, rowHeight), XStringAlignment.Far);
            DrawText(gfx, $"{cat.Amount:N2} د.ل", tdFont, tdBrush, new XRect(x + width - 300, currentY, 110, rowHeight), XStringAlignment.Far);
            DrawText(gfx, $"{cat.Percentage:F1}%", tdFont, new XSolidBrush(XColor.FromArgb(217, 119, 6)), new XRect(x + width - 420, currentY, 110, rowHeight), XStringAlignment.Far);
            DrawText(gfx, cat.SourceType, tdFont, new XSolidBrush(XColor.FromArgb(100, 116, 139)), new XRect(x, currentY, 90, rowHeight), XStringAlignment.Far);

            currentY += rowHeight;
        }

        height = calculatedHeight;
    }

    private static void DrawLiabilitiesOverview(XGraphics gfx, double x, double y, double width, LiabilitiesSummary liabilities, out double height)
    {
        double titleHeight = 20;
        double blockHeight = 110;
        double calculatedHeight = titleHeight + blockHeight;

        // Title
        DrawText(gfx, "ملخص الالتزامات والمستحقات المالي المباشر:", new XFont("Segoe UI", 10, XFontStyleEx.Bold), new XSolidBrush(XColor.FromArgb(11, 28, 48)), new XRect(x, y, width, titleHeight), XStringAlignment.Far);

        double boxesY = y + titleHeight;
        double colWidth = (width - 16) / 3;
        double boxHeight = 50;

        // Row 1
        DrawDetailBox(gfx, x + 2 * (colWidth + 8), boxesY, colWidth, boxHeight, "ديون الموردين للغير", liabilities.TotalSupplierDebt, XColor.FromArgb(15, 23, 42));
        DrawDetailBox(gfx, x + colWidth + 8, boxesY, colWidth, boxHeight, "سلف العمال (عليهم)", liabilities.TotalWorkerAdvancesOutstanding, XColor.FromArgb(180, 83, 9), "صافي ما يدين به العمال للمطعم");
        DrawDetailBox(gfx, x, boxesY, colWidth, boxHeight, "أجور عمال مستحقة (علينا)", liabilities.TotalWorkerWagesUnpaid, XColor.FromArgb(29, 78, 216), "أجور مستحقة للعمال لم تدفع بعد");

        // Row 2
        DrawDetailBox(gfx, x + 2 * (colWidth + 8), boxesY + boxHeight + 8, colWidth, boxHeight, "مستحقات الشركاء (ما لهم)", liabilities.TotalOwnerReceivables, XColor.FromArgb(21, 128, 61), "ما لهم طرف المطعم");
        DrawDetailBox(gfx, x + colWidth + 8, boxesY + boxHeight + 8, colWidth, boxHeight, "التزامات الشركاء (ما عليهم)", liabilities.TotalOwnerObligations, XColor.FromArgb(185, 28, 28), "ما عليهم لصالح المطعم");

        // Total Consolidated Liability Box
        var totalLiabilities = liabilities.TotalSupplierDebt; // Binds to total supplier debt or aggregate
        DrawDetailBox(gfx, x, boxesY + boxHeight + 8, colWidth, boxHeight, "إجمالي ديون الموردين للغير", totalLiabilities, XColor.FromArgb(15, 23, 42));

        height = calculatedHeight;
    }

    private static void DrawDetailBox(XGraphics gfx, double x, double y, double width, double height, string title, decimal val, XColor color, string? subText = null)
    {
        var rect = new XRect(x, y, width, height);
        var borderPen = new XPen(XColor.FromArgb(226, 232, 240), 1);
        gfx.DrawRoundedRectangle(borderPen, XBrushes.White, rect, new XSize(6, 6));

        var titleFont = new XFont("Segoe UI", 7.5, XFontStyleEx.Bold);
        var valFont = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
        var subFont = new XFont("Segoe UI", 6, XFontStyleEx.Regular);

        var titleRect = new XRect(x + 5, y + 4, width - 10, 11);
        DrawText(gfx, title, titleFont, new XSolidBrush(XColor.FromArgb(71, 85, 105)), titleRect, XStringAlignment.Far);

        var valRect = new XRect(x + 5, y + 16, width - 10, 15);
        DrawText(gfx, $"{val:N2} د.ل", valFont, new XSolidBrush(color), valRect, XStringAlignment.Far);

        if (!string.IsNullOrEmpty(subText))
        {
            var subRect = new XRect(x + 5, y + 33, width - 10, 10);
            DrawText(gfx, subText, subFont, new XSolidBrush(XColor.FromArgb(100, 116, 139)), subRect, XStringAlignment.Far);
        }
    }

    // Annex Drawing Methods
    private static void DrawSupplierDetailsTable(XGraphics gfx, ref double y, PdfDocument document, ref int pageCount, ClosingAccountSummary summary, List<SupplierReportItem> items)
    {
        double headerHeight = 22;
        double rowHeight = 20;

        // Table Header
        var thBg = new XSolidBrush(XColor.FromArgb(241, 245, 249));
        gfx.DrawRectangle(thBg, 40, y, 515, headerHeight);
        var borderPen = new XPen(XColor.FromArgb(226, 232, 240), 1);
        gfx.DrawRectangle(borderPen, 40, y, 515, headerHeight);

        var thFont = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
        var thBrush = new XSolidBrush(XColor.FromArgb(71, 85, 105));

        DrawText(gfx, "اسم المورد", thFont, thBrush, new XRect(40 + 515 - 180, y, 170, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "رصيد أول المدة", thFont, thBrush, new XRect(40 + 515 - 280, y, 90, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "مشتريات (+)", thFont, thBrush, new XRect(40 + 515 - 370, y, 80, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "مسدد (-)", thFont, thBrush, new XRect(40 + 515 - 450, y, 70, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "الرصيد الختامي", thFont, thBrush, new XRect(40, y, 65, headerHeight), XStringAlignment.Far);

        y += headerHeight;

        var tdFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Regular);
        var tdBoldFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Bold);
        var tdBrush = new XSolidBrush(XColor.FromArgb(15, 23, 42));

        foreach (var item in items)
        {
            if (y > 740)
            {
                gfx.Dispose();
                gfx = CreateNewPage(document, ref pageCount, summary, true, out y);
                
                // Redraw table headers on new page
                gfx.DrawRectangle(thBg, 40, y, 515, headerHeight);
                gfx.DrawRectangle(borderPen, 40, y, 515, headerHeight);
                DrawText(gfx, "اسم المورد", thFont, thBrush, new XRect(40 + 515 - 180, y, 170, headerHeight), XStringAlignment.Far);
                DrawText(gfx, "رصيد أول المدة", thFont, thBrush, new XRect(40 + 515 - 280, y, 90, headerHeight), XStringAlignment.Far);
                DrawText(gfx, "مشتريات (+)", thFont, thBrush, new XRect(40 + 515 - 370, y, 80, headerHeight), XStringAlignment.Far);
                DrawText(gfx, "مسدد (-)", thFont, thBrush, new XRect(40 + 515 - 450, y, 70, headerHeight), XStringAlignment.Far);
                DrawText(gfx, "الرصيد الختامي", thFont, thBrush, new XRect(40, y, 65, headerHeight), XStringAlignment.Far);
                y += headerHeight;
            }

            gfx.DrawRectangle(borderPen, 40, y, 515, rowHeight);

            DrawText(gfx, item.SupplierName, tdBoldFont, tdBrush, new XRect(40 + 515 - 180, y, 170, rowHeight), XStringAlignment.Far);
            DrawText(gfx, $"{item.OpeningBalance:N2}", tdFont, tdBrush, new XRect(40 + 515 - 280, y, 90, rowHeight), XStringAlignment.Far);
            DrawText(gfx, $"{item.TotalPurchases:N2}", tdFont, new XSolidBrush(XColor.FromArgb(180, 83, 9)), new XRect(40 + 515 - 370, y, 80, rowHeight), XStringAlignment.Far);
            DrawText(gfx, $"{item.TotalPayments:N2}", tdFont, new XSolidBrush(XColor.FromArgb(21, 128, 61)), new XRect(40 + 515 - 450, y, 70, rowHeight), XStringAlignment.Far);
            DrawText(gfx, $"{item.ClosingBalance:N2}", tdBoldFont, tdBrush, new XRect(40, y, 65, rowHeight), XStringAlignment.Far);

            y += rowHeight;
        }
    }

    private static void DrawWorkerAdvancesTable(XGraphics gfx, ref double y, PdfDocument document, ref int pageCount, ClosingAccountSummary summary, List<WorkerAdvanceDetailItem> items)
    {
        double headerHeight = 22;
        double rowHeight = 20;

        var thBg = new XSolidBrush(XColor.FromArgb(241, 245, 249));
        gfx.DrawRectangle(thBg, 40, y, 515, headerHeight);
        var borderPen = new XPen(XColor.FromArgb(226, 232, 240), 1);
        gfx.DrawRectangle(borderPen, 40, y, 515, headerHeight);

        var thFont = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
        var thBrush = new XSolidBrush(XColor.FromArgb(71, 85, 105));

        DrawText(gfx, "اسم العامل", thFont, thBrush, new XRect(40 + 515 - 220, y, 210, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "صافي مبلغ السلفة غير المسددة", thFont, thBrush, new XRect(40 + 515 - 380, y, 150, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "تاريخ آخر معاملة مالية", thFont, thBrush, new XRect(40, y, 120, headerHeight), XStringAlignment.Far);

        y += headerHeight;

        var tdFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Regular);
        var tdBoldFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Bold);
        var tdBrush = new XSolidBrush(XColor.FromArgb(15, 23, 42));

        foreach (var item in items)
        {
            if (y > 740)
            {
                gfx.Dispose();
                gfx = CreateNewPage(document, ref pageCount, summary, true, out y);
                
                gfx.DrawRectangle(thBg, 40, y, 515, headerHeight);
                gfx.DrawRectangle(borderPen, 40, y, 515, headerHeight);
                DrawText(gfx, "اسم العامل", thFont, thBrush, new XRect(40 + 515 - 220, y, 210, headerHeight), XStringAlignment.Far);
                DrawText(gfx, "صافي مبلغ السلفة غير المسددة", thFont, thBrush, new XRect(40 + 515 - 380, y, 150, headerHeight), XStringAlignment.Far);
                DrawText(gfx, "تاريخ آخر معاملة مالية", thFont, thBrush, new XRect(40, y, 120, headerHeight), XStringAlignment.Far);
                y += headerHeight;
            }

            gfx.DrawRectangle(borderPen, 40, y, 515, rowHeight);

            DrawText(gfx, item.WorkerName, tdBoldFont, tdBrush, new XRect(40 + 515 - 220, y, 210, rowHeight), XStringAlignment.Far);
            DrawText(gfx, $"{item.AdvanceAmount:N2} د.ل", tdBoldFont, new XSolidBrush(XColor.FromArgb(180, 83, 9)), new XRect(40 + 515 - 380, y, 150, rowHeight), XStringAlignment.Far);
            DrawText(gfx, $"{item.LastTransactionDate:yyyy-MM-dd}", tdFont, tdBrush, new XRect(40, y, 120, rowHeight), XStringAlignment.Far);

            y += rowHeight;
        }
    }

    private static void DrawWorkerUnpaidWagesTable(XGraphics gfx, ref double y, PdfDocument document, ref int pageCount, ClosingAccountSummary summary, List<WorkerUnpaidWageDetailItem> items)
    {
        double headerHeight = 22;
        double rowHeight = 20;

        var thBg = new XSolidBrush(XColor.FromArgb(241, 245, 249));
        gfx.DrawRectangle(thBg, 40, y, 515, headerHeight);
        var borderPen = new XPen(XColor.FromArgb(226, 232, 240), 1);
        gfx.DrawRectangle(borderPen, 40, y, 515, headerHeight);

        var thFont = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
        var thBrush = new XSolidBrush(XColor.FromArgb(71, 85, 105));

        DrawText(gfx, "اسم العامل", thFont, thBrush, new XRect(40 + 515 - 220, y, 210, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "أجور مستحقة غير مصروفة", thFont, thBrush, new XRect(40 + 515 - 380, y, 150, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "تاريخ آخر استحقاق عمل", thFont, thBrush, new XRect(40, y, 120, headerHeight), XStringAlignment.Far);

        y += headerHeight;

        var tdFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Regular);
        var tdBoldFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Bold);
        var tdBrush = new XSolidBrush(XColor.FromArgb(15, 23, 42));

        foreach (var item in items)
        {
            if (y > 740)
            {
                gfx.Dispose();
                gfx = CreateNewPage(document, ref pageCount, summary, true, out y);
                
                gfx.DrawRectangle(thBg, 40, y, 515, headerHeight);
                gfx.DrawRectangle(borderPen, 40, y, 515, headerHeight);
                DrawText(gfx, "اسم العامل", thFont, thBrush, new XRect(40 + 515 - 220, y, 210, headerHeight), XStringAlignment.Far);
                DrawText(gfx, "أجور مستحقة غير مصروفة", thFont, thBrush, new XRect(40 + 515 - 380, y, 150, headerHeight), XStringAlignment.Far);
                DrawText(gfx, "تاريخ آخر استحقاق عمل", thFont, thBrush, new XRect(40, y, 120, headerHeight), XStringAlignment.Far);
                y += headerHeight;
            }

            gfx.DrawRectangle(borderPen, 40, y, 515, rowHeight);

            DrawText(gfx, item.WorkerName, tdBoldFont, tdBrush, new XRect(40 + 515 - 220, y, 210, rowHeight), XStringAlignment.Far);
            DrawText(gfx, $"{item.UnpaidAmount:N2} د.ل", tdBoldFont, new XSolidBrush(XColor.FromArgb(29, 78, 216)), new XRect(40 + 515 - 380, y, 150, rowHeight), XStringAlignment.Far);
            DrawText(gfx, $"{item.LastAccrualDate:yyyy-MM-dd}", tdFont, tdBrush, new XRect(40, y, 120, rowHeight), XStringAlignment.Far);

            y += rowHeight;
        }
    }

    private static void DrawOwnerDebtsTable(XGraphics gfx, ref double y, PdfDocument document, ref int pageCount, ClosingAccountSummary summary, List<OwnerDebtDetailItem> items)
    {
        double headerHeight = 22;
        double rowHeight = 20;

        var thBg = new XSolidBrush(XColor.FromArgb(241, 245, 249));
        gfx.DrawRectangle(thBg, 40, y, 515, headerHeight);
        var borderPen = new XPen(XColor.FromArgb(226, 232, 240), 1);
        gfx.DrawRectangle(borderPen, 40, y, 515, headerHeight);

        var thFont = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
        var thBrush = new XSolidBrush(XColor.FromArgb(71, 85, 105));

        DrawText(gfx, "الشريك / المالك", thFont, thBrush, new XRect(40 + 515 - 180, y, 170, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "النوع", thFont, thBrush, new XRect(40 + 515 - 280, y, 90, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "المبلغ", thFont, thBrush, new XRect(40 + 515 - 380, y, 90, headerHeight), XStringAlignment.Far);
        DrawText(gfx, "تاريخ آخر عملية", thFont, thBrush, new XRect(40, y, 120, headerHeight), XStringAlignment.Far);

        y += headerHeight;

        var tdFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Regular);
        var tdBoldFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Bold);
        var tdBrush = new XSolidBrush(XColor.FromArgb(15, 23, 42));

        foreach (var item in items)
        {
            if (y > 740)
            {
                gfx.Dispose();
                gfx = CreateNewPage(document, ref pageCount, summary, true, out y);
                
                gfx.DrawRectangle(thBg, 40, y, 515, headerHeight);
                gfx.DrawRectangle(borderPen, 40, y, 515, headerHeight);
                DrawText(gfx, "الشريك / المالك", thFont, thBrush, new XRect(40 + 515 - 180, y, 170, headerHeight), XStringAlignment.Far);
                DrawText(gfx, "النوع", thFont, thBrush, new XRect(40 + 515 - 280, y, 90, headerHeight), XStringAlignment.Far);
                DrawText(gfx, "المبلغ", thFont, thBrush, new XRect(40 + 515 - 380, y, 90, headerHeight), XStringAlignment.Far);
                DrawText(gfx, "تاريخ آخر عملية", thFont, thBrush, new XRect(40, y, 120, headerHeight), XStringAlignment.Far);
                y += headerHeight;
            }

            gfx.DrawRectangle(borderPen, 40, y, 515, rowHeight);

            DrawText(gfx, item.PartnerName, tdBoldFont, tdBrush, new XRect(40 + 515 - 180, y, 170, rowHeight), XStringAlignment.Far);
            
            var typeColor = item.Type == "مستحق له" ? XColor.FromArgb(21, 128, 61) : XColor.FromArgb(185, 28, 28);
            DrawText(gfx, item.Type, tdBoldFont, new XSolidBrush(typeColor), new XRect(40 + 515 - 280, y, 90, rowHeight), XStringAlignment.Far);
            
            DrawText(gfx, $"{item.Amount:N2} د.ل", tdBoldFont, tdBrush, new XRect(40 + 515 - 380, y, 90, rowHeight), XStringAlignment.Far);
            DrawText(gfx, $"{item.TransactionDate:yyyy-MM-dd}", tdFont, tdBrush, new XRect(40, y, 120, rowHeight), XStringAlignment.Far);

            y += rowHeight;
        }
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
        catch { }
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
