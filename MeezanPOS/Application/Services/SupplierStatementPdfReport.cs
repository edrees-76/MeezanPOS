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
        DateTime? filterEndDate,
        List<SupplierInvoice>? detailedInvoices = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            // القسم الأول: كشف الحساب الموحد العام (عمودي Portrait)
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Portrait());
                page.Margin(1.0f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));
                
                // تفعيل اتجاه اليمين إلى اليسار للصفحة بالكامل
                page.ContentFromRightToLeft();

                page.Header().Element(c => ComposeHeader(c, supplierName, remainingBalance, filterStartDate, filterEndDate, transactions));
                page.Content().Element(c => ComposeContent(c, totalOpeningBalance, totalInvoicesSum, totalPaymentsSum, remainingBalance, transactions));
                page.Footer().Element(ComposeFooter);
            });

            // القسم الثاني: تفاصيل الفواتير المرفقة (عمودي Portrait)
            if (detailedInvoices != null && detailedInvoices.Any())
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Portrait());
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));
                    
                    // تفعيل اتجاه اليمين إلى اليسار للصفحة بالكامل
                    page.ContentFromRightToLeft();

                    page.Header().Element(c => ComposeDetailedHeader(c, supplierName));
                    page.Content().Element(c => ComposeDetailedContent(c, detailedInvoices));
                    page.Footer().Element(ComposeFooter);
                });
            }
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

            row.ConstantItem(150).Background(remainingBalance > 0 ? Colors.Red.Lighten5 : Colors.Green.Lighten5)
                .Border(1)
                .BorderColor(remainingBalance > 0 ? Colors.Red.Lighten2 : Colors.Green.Lighten2)
                .Padding(8)
                .Column(column =>
                {
                    column.Item().Text("الرصيد المتبقي الحالي").FontSize(10).SemiBold().AlignCenter().FontColor(Colors.Grey.Darken3);
                    column.Item().Text($"{Math.Abs(remainingBalance):N2} د.ل").FontSize(14).Bold().AlignCenter()
                        .FontColor(remainingBalance > 0 ? Colors.Red.Darken2 : (remainingBalance < 0 ? Colors.Green.Darken2 : Colors.Black));
                    
                    string status = remainingBalance > 0 ? "مدين (مستحق عليه)" : (remainingBalance < 0 ? "دائن (له رصيد)" : "خالص");
                    column.Item().Text(status).FontSize(9).SemiBold().AlignCenter().FontColor(Colors.Grey.Darken2);
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
                    columns.RelativeColumn(0.4f); // التسلسل
                    columns.RelativeColumn(1.0f); // رصيد أول
                    columns.RelativeColumn(1.0f); // قيمة الفاتورة
                    columns.RelativeColumn(1.0f); // تاريخ الفاتورة
                    columns.RelativeColumn(0.9f); // رقم الفاتورة
                    columns.RelativeColumn(1.0f); // المبلغ المستلم
                    columns.RelativeColumn(1.0f); // تاريخ الدفع
                    columns.RelativeColumn(0.8f); // طريقة الدفع
                    columns.RelativeColumn(0.9f); // رقم الايصال
                    columns.RelativeColumn(1.0f); // المتبقي
                    columns.RelativeColumn(1.6f); // ملاحظات
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
                        return container.DefaultTextStyle(x => x.SemiBold().FontSize(7.5f))
                                        .Background(Colors.Grey.Lighten3)
                                        .Padding(4)
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
                        var bankMatch = System.Text.RegularExpressions.Regex.Match(printNotes, @"تحويل بنكي:\s*(.+?)\s*\(([^)]+)\)");
                        if (bankMatch.Success)
                        {
                            string bankName = bankMatch.Groups[1].Value.Trim();
                            string last4 = bankMatch.Groups[2].Value;
                            
                            string description = printNotes.Split('|')[0].Trim();
                            if (!string.IsNullOrEmpty(description))
                                printNotes = $"{description} | {bankName} ({last4})";
                            else
                                printNotes = $"{bankName} ({last4})";
                        }
                    }
                    table.Cell().Element(CellStyle).AlignLeft().Text(printNotes).FontSize(7.5f);

                    static IContainer CellStyle(IContainer container)
                    {
                        return container.DefaultTextStyle(x => x.FontSize(7.5f))
                                        .Padding(4)
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

    private static void ComposeDetailedHeader(IContainer container, string supplierName)
    {
        container.PaddingBottom(0.5f, Unit.Centimetre).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("تفاصيل الفواتير المرفقة").FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
                column.Item().Text($"المورد: {supplierName}").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken3);
            });
        });
    }

    private static void ComposeDetailedContent(IContainer container, List<SupplierInvoice> detailedInvoices)
    {
        container.Column(column =>
        {
            foreach (var invoice in detailedInvoices)
            {
                column.Item().PaddingBottom(16).Background(Colors.Grey.Lighten5)
                    .Border(1)
                    .BorderColor(Colors.Grey.Lighten2)
                    .Padding(12)
                    .Column(invoiceCol =>
                    {
                        // رأس بطاقة الفاتورة بتصميم أنيق ومحترف للغاية
                        invoiceCol.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"📄 فاتورة مشتريات رقم: {invoice.InvoiceNumber ?? invoice.Id.ToString()}").FontSize(12).Bold().FontColor(Colors.Blue.Darken2);
                            row.RelativeItem().AlignRight().Text($"📅 تاريخ الفاتورة: {invoice.InvoiceDate:yyyy/MM/dd}").FontSize(10).SemiBold().FontColor(Colors.Grey.Darken2);
                        });

                        invoiceCol.Item().PaddingTop(4).PaddingBottom(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        // جدول أصناف الفاتورة بالتنسيق العريض الأنيق
                        if (invoice.Items != null && invoice.Items.Any())
                        {
                            invoiceCol.Item().Table(itemTable =>
                            {
                                itemTable.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(0.6f); // م
                                    cols.RelativeColumn(5.0f); // البيان
                                    cols.RelativeColumn(1.2f); // الكمية
                                    cols.RelativeColumn(1.2f); // سعر الوحدة
                                    cols.RelativeColumn(1.5f); // الإجمالي
                                });

                                itemTable.Header(header =>
                                {
                                    header.Cell().Element(HeaderStyle).Text("م").SemiBold();
                                    header.Cell().Element(HeaderStyle).Text("البيان / صنف التوريد").SemiBold();
                                    header.Cell().Element(HeaderStyle).Text("الكمية").SemiBold();
                                    header.Cell().Element(HeaderStyle).Text("سعر الوحدة").SemiBold();
                                    header.Cell().Element(HeaderStyle).Text("القيمة الإجمالية").SemiBold();

                                    static IContainer HeaderStyle(IContainer headerContainer)
                                    {
                                        return headerContainer.Background(Colors.Blue.Lighten4)
                                                              .Padding(6)
                                                              .Border(0.5f)
                                                              .BorderColor(Colors.Grey.Lighten2)
                                                              .AlignCenter();
                                    }
                                });

                                int itemSeq = 1;
                                foreach (var item in invoice.Items)
                                {
                                    itemTable.Cell().Element(ItemStyle).Text(itemSeq++.ToString());
                                    itemTable.Cell().Element(ItemStyle).AlignLeft().Text(item.Description).FontSize(9);
                                    itemTable.Cell().Element(ItemStyle).Text($"{item.Quantity:N2}");
                                    itemTable.Cell().Element(ItemStyle).Text($"{item.UnitPrice:N2}");
                                    itemTable.Cell().Element(ItemStyle).Text($"{item.TotalValue:N2} د.ل").Bold();

                                    static IContainer ItemStyle(IContainer itemContainer)
                                    {
                                        return itemContainer.Padding(5)
                                                            .BorderBottom(0.5f)
                                                            .BorderColor(Colors.Grey.Lighten2)
                                                            .AlignCenter();
                                    }
                                }

                                // صف إجمالي الفاتورة أسفل الجدول
                                itemTable.Cell().BorderTop(1).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten5).Text("");
                                itemTable.Cell().BorderTop(1).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten5).Padding(6).AlignRight().Text("إجمالي الفاتورة").Bold().FontSize(10).FontColor(Colors.Blue.Darken3);
                                itemTable.Cell().BorderTop(1).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten5).Text("");
                                itemTable.Cell().BorderTop(1).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten5).Text("");
                                itemTable.Cell().BorderTop(1).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten5).Padding(6).AlignCenter().Text($"{invoice.TotalAmount:N2} د.ل").Bold().FontSize(10).FontColor(Colors.Blue.Darken3);
                            });
                        }
                        else
                        {
                            invoiceCol.Item().Text("⚠️ لا توجد تفاصيل أصناف مسجلة لهذه الفاتورة.").FontSize(9).Italic().FontColor(Colors.Grey.Darken1);
                        }

                        if (!string.IsNullOrEmpty(invoice.Notes))
                        {
                            invoiceCol.Item().PaddingTop(8).Text($"📝 ملاحظات الفاتورة: {invoice.Notes}").FontSize(9).FontColor(Colors.Grey.Darken2);
                        }
                    });
            }
        });
    }

    private static void DrawSummaryCard(IContainer container, string title, decimal value, string textColor, string valueColor)
    {
        container.Background(Colors.Grey.Lighten5)
            .Border(1)
            .BorderColor(Colors.Grey.Lighten3)
            .Padding(3)
            .Column(col =>
            {
                col.Item().Text(title).FontSize(6.5f).SemiBold().AlignCenter().FontColor(textColor);
                col.Item().PaddingTop(1).Text($"{value:N2}").FontSize(7.5f).Bold().AlignCenter().FontColor(valueColor);
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

