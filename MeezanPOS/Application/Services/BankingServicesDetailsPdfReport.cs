using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Application.Services;

/// <summary>
/// تقرير PDF لتفاصيل عمليات الخدمات المصرفية اليومية المصفاة حسب خيارات المستخدم.
/// </summary>
public class BankingServicesDetailsPdfReport : IDocument
{
    private readonly DateTime _date;
    private readonly BankAccount? _account;
    private readonly List<BankingItemDetailDto> _items;
    private readonly List<BankingServicesViewModel.ShiftTotalDto> _shiftTotals;

    public BankingServicesDetailsPdfReport(
        DateTime date, 
        BankAccount? account, 
        List<BankingItemDetailDto> items, 
        List<BankingServicesViewModel.ShiftTotalDto> shiftTotals)
    {
        _date = date;
        _account = account;
        _items = items;
        _shiftTotals = shiftTotals;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(30);
            page.Size(PageSizes.A4);
            page.PageColor("#f4f6f8");
            page.DefaultTextStyle(x => x.FontSize(11).FontFamily(Fonts.Arial).FontColor(Colors.Black));
            page.ContentFromRightToLeft();

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Row(row =>
            {
                row.RelativeItem().AlignRight().Text(x =>
                {
                    x.Span("صفحة ");
                    x.CurrentPageNumber();
                    x.Span(" من ");
                    x.TotalPages();
                });
                row.RelativeItem().AlignLeft().Text("منظومة ميزان").FontSize(11).SemiBold().FontColor(Colors.Grey.Medium);
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(15).Row(row =>
        {
            row.RelativeItem().AlignCenter().Text("تقرير تفاصيل مبيعات الخدمات المصرفية").FontSize(18).SemiBold().FontColor(Colors.Black);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(15);

            // 1. معلومات الحساب والتقرير
            column.Item().Element(c => DrawCard(c, "معلومات التقرير", content =>
            {
                content.Row(row =>
                {
                    row.RelativeItem().Column(col => { col.Item().Text("التاريخ").SemiBold(); col.Item().Text($"{_date:yyyy/MM/dd}"); });
                    row.RelativeItem().Column(col => { col.Item().Text("الحساب البنكي").SemiBold(); col.Item().Text(_account?.DisplayName ?? "غير محدد"); });
                    row.RelativeItem().Column(col => { col.Item().Text("المصرف").SemiBold(); col.Item().Text(_account?.BankName ?? "غير محدد"); });
                    row.RelativeItem().Column(col => { col.Item().Text("رقم الحساب").SemiBold(); col.Item().Text(_account?.AccountNumber ?? "غير محدد"); });
                });
            }));

            // 2. جدول العمليات التفصيلية المصفى
            column.Item().Element(c => DrawCard(c, "العمليات التفصيلية", content =>
            {
                content.Column(innerCol =>
                {
                    if (_items.Any())
                    {
                        innerCol.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(30); // #
                                cols.RelativeColumn(2); // الموظف
                                cols.RelativeColumn(2); // الوردية
                                cols.RelativeColumn(2); // رقم الفاتورة
                                cols.RelativeColumn(2); // رقم التحويل
                                cols.RelativeColumn(2); // المبلغ
                                cols.RelativeColumn(2); // حالة المطابقة
                            });

                            table.Header(h =>
                            {
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#f8fafc").Padding(5).Text("#").SemiBold().AlignCenter();
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#f8fafc").Padding(5).Text("الموظف/الكاشير").SemiBold();
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#f8fafc").Padding(5).Text("الوردية").SemiBold();
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#f8fafc").Padding(5).Text("رقم الفاتورة").SemiBold();
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#f8fafc").Padding(5).Text("رقم التحويل").SemiBold();
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#f8fafc").Padding(5).Text("المبلغ").SemiBold().AlignRight();
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#f8fafc").Padding(5).Text("المطابقة").SemiBold().AlignCenter();
                            });

                            int index = 1;
                            foreach (var item in _items)
                            {
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(5).Text($"{index++}").AlignCenter();
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(5).Text(item.CashierName);
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(5).Text(item.ShiftName);
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(5).Text(item.InvoiceNumber);
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(5).Text(item.TransferReference);
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(5).Text($"{item.Amount:N2} د.ل").AlignRight();
                                
                                var reconText = item.IsReconciled ? "تمت المطابقة" : "معلق";
                                var reconColor = item.IsReconciled ? Colors.Green.Darken2 : Colors.Red.Darken2;
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(5).Text(reconText).SemiBold().FontColor(reconColor).AlignCenter();
                            }
                        });
                    }
                    else
                    {
                        innerCol.Item().Text("لا توجد حركات لعرضها.").Italic().FontColor(Colors.Grey.Medium);
                    }
                });
            }));

            // 3. إجماليات الورديات والإجمالي الكلي
            column.Item().Element(c => DrawCard(c, "ملخص إجماليات الورديات واليوم", content =>
            {
                content.Column(innerCol =>
                {
                    innerCol.Spacing(8);
                    
                    // طباعة إجمالي كل وردية
                    if (_shiftTotals.Any())
                    {
                        foreach (var st in _shiftTotals)
                        {
                            innerCol.Item().Row(r =>
                            {
                                r.RelativeItem().Text($"- إجمالي {st.ShiftName}:").SemiBold();
                                r.RelativeItem().AlignRight().Text($"{st.TotalAmount:N2} د.ل");
                            });
                        }
                    }
                    
                    // إجمالي الحركات المصفاة الكلي
                    decimal totalAmount = _items.Sum(x => x.Amount);
                    innerCol.Item().PaddingTop(8).BorderTop(1).BorderColor(Colors.Black).Background("#eff6ff").Padding(8).Row(r =>
                    {
                        r.RelativeItem().Text("إجمالي العمليات المطبوعة:").SemiBold().FontSize(12).FontColor(Colors.Blue.Darken3);
                        r.RelativeItem().AlignRight().Text($"{totalAmount:N2} د.ل").SemiBold().FontSize(12).FontColor(Colors.Blue.Darken3);
                    });
                });
            }));
        });
    }

    private void DrawCard(IContainer container, string title, Action<IContainer> content)
    {
        container
            .Background(Colors.White)
            .Border(1)
            .BorderColor("#e2e8f0")
            .Padding(15)
            .Column(column =>
            {
                column.Item().PaddingBottom(10).Text(title).FontSize(13).SemiBold().FontColor(Colors.Blue.Darken3);
                column.Item().Element(content);
            });
    }
}
