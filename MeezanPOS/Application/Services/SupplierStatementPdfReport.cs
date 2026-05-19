using System;
using System.Collections.Generic;
using System.Linq;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Application.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MeezanPOS.Application.Services;

public class SupplierStatementPdfReport
{
    public static void GeneratePdf(
        string filePath,
        string supplierName,
        decimal totalOpeningBalance,
        decimal totalInvoicesSum,
        decimal totalPaymentsSum,
        decimal remainingBalance,
        List<UnifiedLedgerRow> transactions,
        DateTime? filterStartDate,
        DateTime? filterEndDate)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));
                
                // تفعيل اتجاه اليمين إلى اليسار للصفحة بالكامل
                page.ContentFromRightToLeft();

                page.Header().Element(c => ComposeHeader(c, supplierName, remainingBalance, filterStartDate, filterEndDate, transactions));
                page.Content().Element(c => ComposeContent(c, totalOpeningBalance, totalInvoicesSum, totalPaymentsSum, remainingBalance, transactions));
                page.Footer().Element(ComposeFooter);
            });
        })
        .GeneratePdf(filePath);
    }

    private static void ComposeHeader(IContainer container, string supplierName, decimal remainingBalance, DateTime? filterStartDate, DateTime? filterEndDate, List<UnifiedLedgerRow> transactions)
    {
        container.PaddingBottom(0.5f, Unit.Centimetre).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("كشف حساب").FontSize(22).Bold().FontColor(Colors.Blue.Darken3);
                column.Item().Text($"المورد: {supplierName}").FontSize(14).SemiBold();
                
                DateTime? startDate = filterStartDate;
                DateTime? endDate = filterEndDate;

                if (transactions != null && transactions.Count > 0)
                {
                    if (!startDate.HasValue)
                        startDate = transactions.Min(r => r.InvoiceDate ?? r.PaymentDate);
                    
                    if (!endDate.HasValue)
                        endDate = transactions.Max(r => r.InvoiceDate ?? r.PaymentDate);
                }

                string dateText = "";
                if (startDate.HasValue && endDate.HasValue)
                    dateText = $"من {startDate.Value:yyyy/MM/dd} إلى {endDate.Value:yyyy/MM/dd}";
                else if (startDate.HasValue)
                    dateText = $"من {startDate.Value:yyyy/MM/dd}";
                else if (endDate.HasValue)
                    dateText = $"حتى {endDate.Value:yyyy/MM/dd}";

                column.Item().Text(dateText).FontSize(10).FontColor(Colors.Grey.Darken1);
            });

            row.ConstantItem(200).Background(remainingBalance > 0 ? Colors.Red.Lighten5 : Colors.Green.Lighten5)
                .Border(1)
                .BorderColor(remainingBalance > 0 ? Colors.Red.Lighten2 : Colors.Green.Lighten2)
                .Padding(10)
                .Column(column =>
                {
                    column.Item().Text("الرصيد المتبقي الحالي").FontSize(11).SemiBold().AlignCenter().FontColor(Colors.Grey.Darken3);
                    column.Item().Text($"{Math.Abs(remainingBalance):N2} د.ل").FontSize(18).Bold().AlignCenter()
                        .FontColor(remainingBalance > 0 ? Colors.Red.Darken2 : (remainingBalance < 0 ? Colors.Green.Darken2 : Colors.Black));
                    
                    string status = remainingBalance > 0 ? "مدين (مستحق عليه)" : (remainingBalance < 0 ? "دائن (له رصيد)" : "خالص");
                    column.Item().Text(status).FontSize(10).SemiBold().AlignCenter().FontColor(Colors.Grey.Darken2);
                });
        });
    }

    private static void ComposeContent(
        IContainer container,
        decimal totalOpeningBalance,
        decimal totalInvoicesSum,
        decimal totalPaymentsSum,
        decimal remainingBalance,
        List<UnifiedLedgerRow> transactions)
    {
        container.Column(column =>
        {
            // جدول الحركات الموحد
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(0.6f); // التسلسل
                    columns.RelativeColumn(1.2f); // رصيد أول
                    columns.RelativeColumn(1.2f); // قيمة الفاتورة
                    columns.RelativeColumn(1.2f); // تاريخ الفاتورة
                    columns.RelativeColumn(1.2f); // رقم الفاتورة
                    columns.RelativeColumn(1.2f); // المبلغ المستلم
                    columns.RelativeColumn(1.2f); // تاريخ الدفع
                    columns.RelativeColumn(1.2f); // طريقة الدفع
                    columns.RelativeColumn(1.2f); // رقم الايصال
                    columns.RelativeColumn(1.2f); // المتبقي
                    columns.RelativeColumn(2.0f); // ملاحظات
                });

                table.Header(header =>
                {
                    header.Cell().Element(CellStyle).Text("م").SemiBold();
                    header.Cell().Element(CellStyle).Text("رصيد أول").SemiBold();
                    header.Cell().Element(CellStyle).Text("الفاتورة").SemiBold();
                    header.Cell().Element(CellStyle).Text("تاريخها").SemiBold();
                    header.Cell().Element(CellStyle).Text("رقمها").SemiBold();
                    header.Cell().Element(CellStyle).Text("مستلم").SemiBold();
                    header.Cell().Element(CellStyle).Text("تاريخ الدفع").SemiBold();
                    header.Cell().Element(CellStyle).Text("الطريقة").SemiBold();
                    header.Cell().Element(CellStyle).Text("الإيصال").SemiBold();
                    header.Cell().Element(CellStyle).Text("المتبقي").SemiBold();
                    header.Cell().Element(CellStyle).Text("ملاحظات").SemiBold();

                    static IContainer CellStyle(IContainer container)
                    {
                        return container.DefaultTextStyle(x => x.SemiBold().FontSize(9))
                                        .Background(Colors.Grey.Lighten3)
                                        .Padding(6)
                                        .Border(0.5f)
                                        .BorderColor(Colors.Grey.Lighten1)
                                        .AlignCenter();
                    }
                });

                foreach (var tx in transactions)
                {
                    table.Cell().Element(CellStyle).Text(tx.Sequence.ToString());
                    table.Cell().Element(CellStyle).Text($"{tx.OpeningBalance:N2}");
                    
                    table.Cell().Element(CellStyle).Text(tx.InvoiceAmount.HasValue ? $"{tx.InvoiceAmount:N2}" : "").FontColor(Colors.Blue.Darken3).SemiBold();
                    table.Cell().Element(CellStyle).Text(tx.InvoiceDate?.ToString("yyyy/MM/dd") ?? "");
                    table.Cell().Element(CellStyle).Text(tx.InvoiceNumber ?? "");
                    
                    table.Cell().Element(CellStyle).Text(tx.PaymentAmount.HasValue ? $"{tx.PaymentAmount:N2}" : "").FontColor(Colors.Green.Darken3).SemiBold();
                    table.Cell().Element(CellStyle).Text(tx.PaymentDate?.ToString("yyyy/MM/dd") ?? "");
                    string printMethod = tx.PaymentMethod ?? "";
                    if (printMethod == "من اليومية")
                        printMethod = "نقدي";
                        
                    table.Cell().Element(CellStyle).Text(printMethod);
                    table.Cell().Element(CellStyle).Text(tx.ReceiptNumber ?? "");
                    
                    table.Cell().Element(CellStyle).Text($"{tx.RemainingBalance:N2}")
                        .FontColor(tx.RemainingBalance > 0 ? Colors.Red.Darken2 : (tx.RemainingBalance < 0 ? Colors.Green.Darken2 : Colors.Black))
                        .Bold();
                        
                    string printNotes = tx.Notes ?? "";
                    if (tx.IsTransferPayment)
                    {
                        var bankMatch = System.Text.RegularExpressions.Regex.Match(printNotes, @"تحويل بنكي:\s*(.+?)\s*\((\d{1,4})\)");
                        if (bankMatch.Success)
                        {
                            // If there is an original description, we can keep it or just show the bank.
                            // User asked to write bank name and last 4 digits in notes.
                            string bankName = bankMatch.Groups[1].Value.Trim();
                            string last4 = bankMatch.Groups[2].Value;
                            
                            // Extract just the description if there is any (before the ' | ' pipe)
                            string description = printNotes.Split('|')[0].Trim();
                            if (!string.IsNullOrEmpty(description))
                                printNotes = $"{description} | {bankName} ({last4})";
                            else
                                printNotes = $"{bankName} ({last4})";
                        }
                    }
                    table.Cell().Element(CellStyle).AlignLeft().Text(printNotes).FontSize(8);

                    static IContainer CellStyle(IContainer container)
                    {
                        return container.Padding(5)
                                        .BorderBottom(0.5f)
                                        .BorderColor(Colors.Grey.Lighten2)
                                        .AlignCenter();
                    }
                }

                // صف إجماليات في أسفل الجدول
                table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Text(""); // 1. التسلسل
                table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).PaddingTop(5).PaddingRight(2).PaddingLeft(2).Element(c => DrawSummaryCard(c, "رصيد أول", totalOpeningBalance, Colors.Grey.Darken2, Colors.Black)); // 2. رصيد أول
                table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).PaddingTop(5).PaddingRight(2).PaddingLeft(2).Element(c => DrawSummaryCard(c, "اجمالى الفواتير", totalInvoicesSum, Colors.Amber.Darken3, Colors.Amber.Darken4)); // 3. الفاتورة
                table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Text(""); // 4. تاريخ الفاتورة
                table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Text(""); // 5. رقم الفاتورة
                table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).PaddingTop(5).PaddingRight(2).PaddingLeft(2).Element(c => DrawSummaryCard(c, "اجمالى المبالغ المستلمة", totalPaymentsSum, Colors.Green.Darken2, Colors.Green.Darken3)); // 6. المبلغ المستلم
                table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Text(""); // 7. تاريخ الدفع
                table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Text(""); // 8. طريقة الدفع
                table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Text(""); // 9. رقم الايصال
                table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).PaddingTop(5).PaddingRight(2).PaddingLeft(2).Element(c => DrawSummaryCard(c, "المتبقي", remainingBalance, remainingBalance > 0 ? Colors.Red.Darken2 : Colors.Green.Darken2, remainingBalance > 0 ? Colors.Red.Darken3 : Colors.Green.Darken3)); // 10. المتبقي
                table.Cell().BorderTop(2).BorderColor(Colors.Grey.Darken2).Text(""); // 11. الملاحظات
            });
        });
    }

    private static void DrawSummaryCard(IContainer container, string title, decimal value, string textColor, string valueColor)
    {
        container.Background(Colors.Grey.Lighten5)
            .Border(1)
            .BorderColor(Colors.Grey.Lighten3)
            .Padding(4)
            .Column(col =>
            {
                col.Item().Text(title).FontSize(8).SemiBold().AlignCenter().FontColor(textColor);
                col.Item().PaddingTop(2).Text($"{value:N2}").FontSize(10).Bold().AlignCenter().FontColor(valueColor);
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
