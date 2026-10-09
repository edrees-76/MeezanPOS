using System;
using System.Collections.Generic;
using System.Linq;
using MeezanPOS.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static MeezanPOS.Infrastructure.Reports.ReportStyle;

namespace MeezanPOS.Infrastructure.Reports;

/// <summary>
/// تقرير الحساب الختامي والمركز المالي (مختصر أو شامل بملاحق التفاصيل).
/// </summary>
/// <remarks>
/// كان مكتوباً بـ PdfSharp مع محرك تشكيل عربي يدوي ورسم بالإحداثيات؛ أعيدت كتابته بـ QuestPDF
/// مثل بقية التقارير، فصار التشكيل العربي وتقسيم الصفحات وتكرار رؤوس الجداول تلقائياً.
/// </remarks>
public static class ClosingAccountPdfExporter
{
    public static void GenerateReport(string filePath, ClosingAccountSummary summary, bool isComprehensive)
        => Build(summary, isComprehensive).GeneratePdf(filePath);

    /// <summary>المستند دون حفظ (للمعاينة كصور في الاختبارات).</summary>
    public static IDocument Build(ClosingAccountSummary summary, bool isComprehensive)
    {
        var kind = isComprehensive ? "الشامل" : "المختصر";
        var exportTime = DateTime.Now;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                SetupPage(page);
                page.Header().Element(c => Header(c,
                    $"تقرير الحساب الختامي والمركز المالي ({kind})",
                    summary.PeriodText,
                    exportTime,
                    $"تابع: تقرير الحساب الختامي والمركز المالي - {kind}"));

                page.Content().Column(col =>
                {
                    col.Spacing(12);
                    col.Item().Element(c => KpiRow(c, summary.IncomeStatement));
                    col.Item().Element(c => FourValueBlock(c, "ملخص حركة الخزينة النقدية (Cash Ledger)",
                        new[] { "رصيد أول المدة", "الوارد النقدي (+)", "الصادر النقدي (-)", "الرصيد الختامي" },
                        new[] { summary.CashLedger.OpeningBalance, summary.CashLedger.TotalCashIn, summary.CashLedger.TotalCashOut, summary.CashLedger.ClosingBalance }));

                    var banks = summary.BankAccounts ?? new List<BankAccountReportItem>();
                    if (banks.Any())
                    {
                        col.Item().Element(c => FourValueBlock(c, "ملخص أرصدة وحسابات المصارف (Bank Accounts)",
                            new[] { "رصيد أول المدة بنك", "إجمالي الإيداعات (+)", "إجمالي السحوبات (-)", "الرصيد الختامي بنك" },
                            new[] { banks.Sum(b => b.OpeningBalance), banks.Sum(b => b.TotalDeposits), banks.Sum(b => b.TotalWithdrawals), banks.Sum(b => b.ClosingBalance) }));
                    }

                    col.Item().Element(c => TopExpenses(c, summary.ExpensesByCategory.Take(5).ToList()));
                    col.Item().Element(c => Liabilities(c, summary.Liabilities));

                    if (isComprehensive)
                        Annexes(col, summary);
                });
            });
        });
    }

    private static void KpiRow(IContainer container, IncomeStatementReport income)
    {
        container.Row(row =>
        {
            row.Spacing(10);
            row.RelativeItem().Element(c => KpiCard(c, "إجمالي المبيعات", income.TotalSales, null,
                $"نقدي: {income.CashSales:N2} | بطاقة: {income.CardSales:N2} | مرتجعات ومجاني: {income.ReturnsTotal + income.FreeOrdersTotal:N2}"));
            row.RelativeItem().Element(c => KpiCard(c, "إجمالي المصروفات", income.TotalExpenses));
            row.RelativeItem().Element(c => KpiCard(c, "صافي الأرباح", income.NetProfit,
                income.NetProfit >= 0 ? "#DCFCE7" : "#FEE2E2"));
        });
    }

    private static void KpiCard(IContainer container, string title, decimal value, string? background = null, string? sub = null)
    {
        Card(container, background).Column(c =>
        {
            c.Item().Text(title).FontSize(8.5f).Bold().FontColor(Muted);
            c.Item().PaddingTop(3).Text(Money(value)).FontSize(11.5f).Bold();
            if (!string.IsNullOrEmpty(sub))
                c.Item().PaddingTop(2).Text(sub).FontSize(7.5f).FontColor(Muted);
        });
    }

    private static void FourValueBlock(IContainer container, string title, string[] labels, decimal[] values)
    {
        string[] colors = { Slate, Green, Red, Blue };
        Card(container).Column(c =>
        {
            c.Item().Text(title).FontSize(9.5f).Bold().FontColor(Slate);
            c.Item().PaddingTop(6).Row(row =>
            {
                for (int i = 0; i < 4; i++)
                {
                    var index = i;
                    row.RelativeItem().Column(cell =>
                    {
                        cell.Item().AlignCenter().Text(labels[index]).FontSize(8.5f).FontColor(Muted);
                        cell.Item().AlignCenter().Text(Money(values[index])).FontSize(9.5f).Bold().FontColor(colors[index]);
                    });
                }
            });
        });
    }

    private static void TopExpenses(IContainer container, List<ExpenseCategoryReportItem> categories)
    {
        container.Column(col =>
        {
            col.Item().Element(c => SectionTitle(c, "أبرز المصروفات حسب فئة الصرف:"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(2);
                });
                table.Header(h =>
                {
                    foreach (var title in new[] { "فئة المصروف", "قيمة المصروف", "النسبة", "المصدر" })
                        h.Cell().Element(HeaderCell).Text(title).Bold().FontSize(8.5f).FontColor(HeaderText);
                });
                foreach (var cat in categories)
                {
                    table.Cell().Element(BodyCell).Text(cat.CategoryName).Bold();
                    table.Cell().Element(BodyCell).Text(Money(cat.Amount));
                    table.Cell().Element(BodyCell).Text($"{cat.Percentage:F1}%").FontColor(Orange);
                    table.Cell().Element(BodyCell).Text(cat.SourceType).FontColor(SlateMuted);
                }
            });
        });
    }

    private static void Liabilities(IContainer container, LiabilitiesSummary l)
    {
        container.Column(col =>
        {
            col.Item().Element(c => SectionTitle(c, "ملخص الالتزامات والمستحقات المالي المباشر:"));
            col.Item().Row(row =>
            {
                row.Spacing(8);
                row.RelativeItem().Element(c => DetailBox(c, "ديون الموردين للغير", l.TotalSupplierDebt, Slate));
                row.RelativeItem().Element(c => DetailBox(c, "سلف العمال (عليهم)", l.TotalWorkerAdvancesOutstanding, Amber, "صافي ما يدين به العمال للمطعم"));
                row.RelativeItem().Element(c => DetailBox(c, "أجور عمال مستحقة (علينا)", l.TotalWorkerWagesUnpaid, Blue, "أجور مستحقة للعمال لم تدفع بعد"));
            });
            col.Item().PaddingTop(8).Row(row =>
            {
                row.Spacing(8);
                row.RelativeItem().Element(c => DetailBox(c, "مستحقات الشركاء (ما لهم)", l.TotalOwnerReceivables, Green, "ما لهم طرف المطعم"));
                row.RelativeItem().Element(c => DetailBox(c, "التزامات الشركاء (ما عليهم)", l.TotalOwnerObligations, Red, "ما عليهم لصالح المطعم"));
                row.RelativeItem().Element(c => DetailBox(c, "إجمالي ديون الموردين للغير", l.TotalSupplierDebt, Slate));
            });
        });
    }

    private static void DetailBox(IContainer container, string title, decimal value, string color, string? sub = null)
    {
        Card(container).Column(c =>
        {
            c.Item().Text(title).FontSize(7.5f).Bold().FontColor(HeaderText);
            c.Item().PaddingTop(2).Text(Money(value)).FontSize(10).Bold().FontColor(color);
            if (!string.IsNullOrEmpty(sub))
                c.Item().PaddingTop(2).Text(sub).FontSize(6.5f).FontColor(SlateMuted);
        });
    }

    // ── الملاحق (التقرير الشامل) ───────────────────────────────────

    private static void Annexes(ColumnDescriptor col, ClosingAccountSummary s)
    {
        int annex = 1;

        if (s.Suppliers.Any())
            AnnexTable(col, annex++, "تفاصيل أرصدة وحركات الموردين", null,
                new[] { 3f, 2, 2, 2, 2 },
                new[] { "اسم المورد", "رصيد أول المدة", "مشتريات (+)", "مسدد (-)", "الرصيد الختامي" },
                s.Suppliers.Select(i => new[]
                {
                    (i.SupplierName, Ink, true), ($"{i.OpeningBalance:N2}", Ink, false), ($"{i.TotalPurchases:N2}", Amber, false),
                    ($"{i.TotalPayments:N2}", Green, false), ($"{i.ClosingBalance:N2}", Ink, true)
                }));

        if (s.Liabilities.WorkerAdvanceDetails.Any())
            AnnexTable(col, annex++, "تفاصيل سلف العمال غير المسددة", "صافي ما يدين به العمال للمطعم (المبالغ المسحوبة كذمة مالية)",
                new[] { 3f, 2, 2 },
                new[] { "اسم العامل", "صافي مبلغ السلفة غير المسددة", "تاريخ آخر معاملة مالية" },
                s.Liabilities.WorkerAdvanceDetails.Select(i => new[]
                {
                    (i.WorkerName, Ink, true), (Money(i.AdvanceAmount), Amber, true), ($"{i.LastTransactionDate:yyyy-MM-dd}", Ink, false)
                }));

        if (s.Liabilities.WorkerUnpaidWageDetails.Any())
            AnnexTable(col, annex++, "تفاصيل الأجور المستحقة غير المصروفة للعمال", "أجور مستحقة للعمال لقاء حضورهم ولم تُصرف لهم نقداً بعد",
                new[] { 3f, 2, 2 },
                new[] { "اسم العامل", "أجور مستحقة غير مصروفة", "تاريخ آخر استحقاق عمل" },
                s.Liabilities.WorkerUnpaidWageDetails.Select(i => new[]
                {
                    (i.WorkerName, Ink, true), (Money(i.UnpaidAmount), Blue, true), ($"{i.LastAccrualDate:yyyy-MM-dd}", Ink, false)
                }));

        if (s.Liabilities.OwnerDebtDetails.Any())
            AnnexTable(col, annex++, "تفاصيل حسابات ذمم الملاك والشركاء", null,
                new[] { 3f, 2, 2, 2 },
                new[] { "الشريك / المالك", "النوع", "المبلغ", "تاريخ آخر عملية" },
                s.Liabilities.OwnerDebtDetails.Select(i => new[]
                {
                    (i.PartnerName, Ink, true), (i.Type, i.Type == "مستحق له" ? Green : Red, true),
                    (Money(i.Amount), Ink, true), ($"{i.TransactionDate:yyyy-MM-dd}", Ink, false)
                }));

        if (s.BankAccounts != null && s.BankAccounts.Any())
            AnnexTable(col, annex++, "تفاصيل أرصدة وحسابات المصارف", null,
                new[] { 2.5f, 2.5f, 2, 2, 2, 2 },
                new[] { "حساب المصرف", "البنك / رقم الحساب", "رصيد أول المدة", "إيداعات (+)", "سحوبات (-)", "الرصيد الختامي" },
                s.BankAccounts.Select(i => new[]
                {
                    (i.AccountName, Ink, true), ($"{i.BankName} / {i.AccountNumber}", Ink, false), ($"{i.OpeningBalance:N2}", Ink, false),
                    ($"{i.TotalDeposits:N2}", Green, false), ($"{i.TotalWithdrawals:N2}", Red, false), ($"{i.ClosingBalance:N2}", Ink, true)
                }));
    }

    /// <summary>ملحق في صفحة جديدة: عنوان، وصف اختياري، وجدول يتكرر رأسه عند الانتقال لصفحة أخرى.</summary>
    private static void AnnexTable(ColumnDescriptor col, int number, string title, string? note, float[] widths, string[] headers,
        IEnumerable<(string Text, string Color, bool Bold)[]> rows)
    {
        col.Item().PageBreak();
        col.Item().Text($"ملحق رقم ({number}): {title}").FontSize(12).Bold();
        if (note != null)
            col.Item().Text(note).FontSize(8.5f).Italic().FontColor(Muted);

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                foreach (var w in widths) cols.RelativeColumn(w);
            });
            table.Header(h =>
            {
                foreach (var header in headers)
                    h.Cell().Element(HeaderCell).Text(header).Bold().FontColor(HeaderText);
            });
            foreach (var row in rows)
            {
                foreach (var (text, color, bold) in row)
                {
                    var t = table.Cell().Element(BodyCell).Text(text ?? string.Empty).FontSize(8.5f).FontColor(color);
                    if (bold) t.Bold();
                }
            }
        });
    }
}
