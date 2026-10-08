using System;
using System.Collections.Generic;
using System.Linq;
using MeezanPOS.Application.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MeezanPOS.Application.Services
{
    public class WorkerStatementPdfReport
    {
        public static void GeneratePdf(
            string filePath,
            string workerName,
            decimal totalAccrued,
            decimal totalPaid,
            decimal remainingBalance,
            List<WorkerLedgerEntry> transactions,
            bool autoOpen)
        {

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Portrait());
                    page.Margin(1.0f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));
                    
                    // تفعيل اتجاه اليمين إلى اليسار للصفحة بالكامل
                    page.ContentFromRightToLeft();

                    page.Header().Element(c => ComposeHeader(c, workerName, remainingBalance, transactions));
                    page.Content().Element(c => ComposeContent(c, totalAccrued, totalPaid, remainingBalance, transactions));
                    page.Footer().Element(ComposeFooter);
                });
            })
            .GeneratePdf(filePath);

            if (autoOpen)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception)
                {
                    // تجاهل الأخطاء إذا فشل الفتح التلقائي
                }
            }
        }

        private static void ComposeHeader(IContainer container, string workerName, decimal remainingBalance, List<WorkerLedgerEntry> transactions)
        {
            container.PaddingBottom(0.5f, Unit.Centimetre).Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text("كشف حساب عامل").FontSize(22).Bold().FontColor(Colors.Indigo.Darken3);
                    column.Item().Text($"العامل: {workerName}").FontSize(14).SemiBold();
                    
                    string dateText = $"تاريخ التوليد: {DateTime.Now:yyyy/MM/dd HH:mm}";
                    column.Item().Text(dateText).FontSize(10).FontColor(Colors.Grey.Darken1);
                });

                row.ConstantItem(160).Background(remainingBalance > 0 ? Colors.Green.Lighten5 : (remainingBalance < 0 ? Colors.Red.Lighten5 : Colors.Grey.Lighten5))
                    .Border(1)
                    .BorderColor(remainingBalance > 0 ? Colors.Green.Lighten2 : (remainingBalance < 0 ? Colors.Red.Lighten2 : Colors.Grey.Lighten2))
                    .Padding(8)
                    .Column(column =>
                    {
                        column.Item().Text("الرصيد الحالي").FontSize(10).SemiBold().AlignCenter().FontColor(Colors.Grey.Darken3);
                        column.Item().Text($"{Math.Abs(remainingBalance):N2} د.ل").FontSize(14).Bold().AlignCenter()
                            .FontColor(remainingBalance > 0 ? Colors.Green.Darken2 : (remainingBalance < 0 ? Colors.Red.Darken2 : Colors.Black));
                        
                        string status = remainingBalance > 0 ? "دائن (له مستحقات)" : (remainingBalance < 0 ? "مدين (عليه سلفة/سحوبات)" : "خالص");
                        column.Item().Text(status).FontSize(9).SemiBold().AlignCenter().FontColor(Colors.Grey.Darken2);
                    });
            });
        }

        private static void ComposeContent(
            IContainer container,
            decimal totalAccrued,
            decimal totalPaid,
            decimal remainingBalance,
            List<WorkerLedgerEntry> transactions)
        {
            // تعريف دوال التنسيق الموضعية على مستوى الميثود لتكون مرئية لكافة عناصر الجدول
            IContainer HeaderCellStyle(IContainer c)
            {
                return c.DefaultTextStyle(x => x.SemiBold().FontSize(9))
                        .Background(Colors.Grey.Lighten3)
                        .Padding(6)
                        .Border(0.5f)
                        .BorderColor(Colors.Grey.Lighten1)
                        .AlignCenter();
            }

            IContainer ContentCellStyle(IContainer c)
            {
                return c.DefaultTextStyle(x => x.FontSize(8.5f))
                        .Padding(5)
                        .BorderBottom(0.5f)
                        .BorderColor(Colors.Grey.Lighten2)
                        .AlignCenter();
            }

            // حساب رصيد أول المدة قبل أول معاملة معروضة في القائمة
            decimal openingBalance = transactions.Count > 0
                ? (transactions[0].BalanceAfter - transactions[0].AccruedAmount + transactions[0].PaidAmount)
                : 0;

            container.Column(column =>
            {
                // قسم الكروت التلخيصية العلوية (تمت إضافة كارت رصيد أول المدة لملخص الـ PDF)
                column.Item().PaddingBottom(0.5f, Unit.Centimetre).Row(row =>
                {
                    row.RelativeItem().Element(c => DrawSummaryCard(c, "رصيد أول المدة", openingBalance, Colors.Grey.Darken3, Colors.Black));
                    row.Spacing(8);
                    row.RelativeItem().Element(c => DrawSummaryCard(c, "إجمالي المستحقات", totalAccrued, Colors.Indigo.Darken3, Colors.Indigo.Darken4));
                    row.Spacing(8);
                    row.RelativeItem().Element(c => DrawSummaryCard(c, "إجمالي المدفوعات", totalPaid, Colors.Amber.Darken3, Colors.Amber.Darken4));
                    row.Spacing(8);
                    row.RelativeItem().Element(c => DrawSummaryCard(c, "الرصيد الحالي", remainingBalance, remainingBalance >= 0 ? Colors.Green.Darken2 : Colors.Red.Darken2, remainingBalance >= 0 ? Colors.Green.Darken3 : Colors.Red.Darken3));
                    row.Spacing(8);
                    row.RelativeItem().Element(c => DrawSummaryCard(c, "عدد الحركات", transactions.Count, Colors.Grey.Darken2, Colors.Black));
                });

                // جدول كشف الحساب التراكمي
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(0.5f);  // ت
                        columns.RelativeColumn(1.2f);  // التاريخ
                        columns.RelativeColumn(1.2f);  // نوع الحركة
                        columns.RelativeColumn(3.0f);  // البيان
                        columns.RelativeColumn(1.2f);  // دائن (+) له
                        columns.RelativeColumn(1.2f);  // مدين (-) عليه
                        columns.RelativeColumn(1.5f);  // الرصيد التراكمي
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCellStyle).Text("م").SemiBold();
                        header.Cell().Element(HeaderCellStyle).Text("التاريخ").SemiBold();
                        header.Cell().Element(HeaderCellStyle).Text("نوع الحركة").SemiBold();
                        header.Cell().Element(HeaderCellStyle).Text("البيان والوصف").SemiBold();
                        header.Cell().Element(HeaderCellStyle).Text("مستحق (له)").SemiBold();
                        header.Cell().Element(HeaderCellStyle).Text("مدفوع (عليه)").SemiBold();
                        header.Cell().Element(HeaderCellStyle).Text("الرصيد التراكمي").SemiBold();
                    });

                    // 1. صف رصيد أول المدة الافتتاحي (دائماً في بداية الكشف)
                    table.Cell().Element(ContentCellStyle).Text("-");
                    table.Cell().Element(ContentCellStyle).Text("-");
                    table.Cell().Element(ContentCellStyle).Text("رصيد افتتاحي");
                    table.Cell().Element(ContentCellStyle).AlignRight().Text("رصيد أول المدة للعمليات المعروضة");
                    table.Cell().Element(ContentCellStyle).Text("-");
                    table.Cell().Element(ContentCellStyle).Text("-");
                    table.Cell().Element(ContentCellStyle).Text($"{openingBalance:N2}").Bold();

                    // 2. تعبئة حركات الجدول
                    int seq = 1;
                    foreach (var tx in transactions)
                    {
                        table.Cell().Element(ContentCellStyle).Text(seq++.ToString());
                        table.Cell().Element(ContentCellStyle).Text(tx.Date.ToString("yyyy/MM/dd"));
                        table.Cell().Element(ContentCellStyle).Text(tx.Source);
                        table.Cell().Element(ContentCellStyle).AlignRight().Text(tx.Description).FontSize(8.5f);
                        table.Cell().Element(ContentCellStyle).Text(tx.AccruedAmount > 0 ? $"{tx.AccruedAmount:N2}" : "-").FontColor(Colors.Green.Darken3).SemiBold();
                        table.Cell().Element(ContentCellStyle).Text(tx.PaidAmount > 0 ? $"{tx.PaidAmount:N2}" : "-").FontColor(Colors.Red.Darken3).SemiBold();
                        table.Cell().Element(ContentCellStyle).Text($"{tx.BalanceAfter:N2}")
                            .FontColor(tx.BalanceAfter >= 0 ? Colors.Green.Darken2 : Colors.Red.Darken2)
                            .Bold();
                    }

                    // صف إجماليات نهائية في أسفل الجدول
                    table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Text(""); 
                    table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Text("");
                    table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Text("");
                    table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Padding(6).AlignRight().Text("الإجماليات والـصافي").Bold().FontSize(9);
                    table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Padding(6).Text($"{totalAccrued:N2}").Bold().FontSize(9).FontColor(Colors.Green.Darken3);
                    table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Padding(6).Text($"{totalPaid:N2}").Bold().FontSize(9).FontColor(Colors.Red.Darken3);
                    table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Padding(6).Text($"{remainingBalance:N2}")
                        .Bold().FontSize(9).FontColor(remainingBalance >= 0 ? Colors.Green.Darken3 : Colors.Red.Darken3);
                });
            });
        }

        private static void DrawSummaryCard(IContainer container, string title, decimal value, string textColor, string valueColor)
        {
            container.Background(Colors.Grey.Lighten5)
                .Border(1)
                .BorderColor(Colors.Grey.Lighten3)
                .Padding(6)
                .Column(col =>
                {
                    col.Item().Text(title).FontSize(8).SemiBold().AlignCenter().FontColor(textColor);
                    col.Item().PaddingTop(2).Text($"{value:N2}").FontSize(10).Bold().AlignCenter().FontColor(valueColor);
                });
        }

        private static void DrawSummaryCard(IContainer container, string title, int value, string textColor, string valueColor)
        {
            container.Background(Colors.Grey.Lighten5)
                .Border(1)
                .BorderColor(Colors.Grey.Lighten3)
                .Padding(6)
                .Column(col =>
                {
                    col.Item().Text(title).FontSize(8).SemiBold().AlignCenter().FontColor(textColor);
                    col.Item().PaddingTop(2).Text($"{value}").FontSize(10).Bold().AlignCenter().FontColor(valueColor);
                });
        }

        private static void ComposeFooter(IContainer container)
        {
            container.BorderTop(0.5f)
                .BorderColor(Colors.Grey.Lighten1)
                .PaddingTop(10)
                .Row(row =>
                {
                    row.RelativeItem().AlignRight().Text(x =>
                    {
                        x.Span("صفحة ");
                        x.CurrentPageNumber();
                        x.Span(" من ");
                        x.TotalPages();
                    });
                    row.RelativeItem().AlignLeft().Text("منظومة ميزان").FontSize(12).SemiBold().FontColor(Colors.Grey.Medium);
                });
        }
    }
}
