using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Linq;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Application.Services;

public class GeneralExpensePdfReport
{
    private readonly GeneralExpensesViewModel _vm;

    public GeneralExpensePdfReport(GeneralExpensesViewModel vm)
    {
        _vm = vm;
    }

    public void GeneratePdf(string filePath)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily(Fonts.Arial));
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
                    row.RelativeItem().AlignLeft().Text(MeezanPOS.Infrastructure.Data.RestaurantContext.ReportFooter).FontSize(12).SemiBold().FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf(filePath);
    }

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(15).Column(col =>
        {
            col.Item().AlignCenter().Text("تقرير المصاريف العامة").FontSize(20).SemiBold();
            col.Item().AlignCenter().Text($"الفترة: من {_vm.DateFrom:yyyy/MM/dd} إلى {_vm.DateTo:yyyy/MM/dd}")
                .FontSize(12).FontColor(Colors.Grey.Darken1);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(10);

            // --- ملخص حسب النوع ---
            if (_vm.TypeSummaries.Any())
            {
                col.Item().PaddingBottom(5).Text("ملخص حسب النوع").FontSize(14).SemiBold().FontColor("#1a56db");
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background("#1a56db").Padding(6).Text("النوع").FontColor(Colors.White).FontSize(11).SemiBold();
                        header.Cell().Background("#1a56db").Padding(6).Text("الإجمالي").FontColor(Colors.White).FontSize(11).SemiBold();
                        header.Cell().Background("#1a56db").Padding(6).Text("العدد").FontColor(Colors.White).FontSize(11).SemiBold();
                    });

                    foreach (var item in _vm.TypeSummaries)
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(item.TypeName).FontSize(10);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(item.Total.ToString("N2")).FontSize(10);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(item.Count.ToString()).FontSize(10);
                    }

                    // صف الإجمالي
                    table.Cell().Background("#f0f4ff").Padding(5).Text("الإجمالي الكلي").FontSize(11).SemiBold();
                    table.Cell().Background("#f0f4ff").Padding(5).Text(_vm.TotalAmount.ToString("N2")).FontSize(11).SemiBold();
                    table.Cell().Background("#f0f4ff").Padding(5).Text(_vm.TotalCount.ToString()).FontSize(11).SemiBold();
                });
            }

            col.Item().PaddingTop(10);

            // --- التفاصيل ---
            col.Item().PaddingBottom(5).Text("التفاصيل").FontSize(14).SemiBold().FontColor("#1a56db");
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(30);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(3);
                });

                table.Header(header =>
                {
                    header.Cell().Background("#1a56db").Padding(6).Text("#").FontColor(Colors.White).FontSize(10).SemiBold();
                    header.Cell().Background("#1a56db").Padding(6).Text("النوع").FontColor(Colors.White).FontSize(10).SemiBold();
                    header.Cell().Background("#1a56db").Padding(6).Text("المبلغ").FontColor(Colors.White).FontSize(10).SemiBold();
                    header.Cell().Background("#1a56db").Padding(6).Text("تاريخ الدفع").FontColor(Colors.White).FontSize(10).SemiBold();
                    header.Cell().Background("#1a56db").Padding(6).Text("طريقة الدفع").FontColor(Colors.White).FontSize(10).SemiBold();
                    header.Cell().Background("#1a56db").Padding(6).Text("الوصف").FontColor(Colors.White).FontSize(10).SemiBold();
                });

                foreach (var item in _vm.Expenses)
                {
                    var bg = item.Sequence % 2 == 0 ? "#f9fafb" : "#ffffff";
                    table.Cell().Background(bg).Padding(4).Text(item.Sequence.ToString()).FontSize(9);
                    table.Cell().Background(bg).Padding(4).Text(item.ExpenseTypeName).FontSize(9);
                    table.Cell().Background(bg).Padding(4).Text(item.Amount.ToString("N2")).FontSize(9);
                    table.Cell().Background(bg).Padding(4).Text(item.PaymentDateDisplay).FontSize(9);
                    table.Cell().Background(bg).Padding(4).Text(item.PaymentMethodName).FontSize(9);
                    table.Cell().Background(bg).Padding(4).Text(item.Description).FontSize(9);
                }

                // صف الإجمالي
                table.Cell().ColumnSpan(2).Background("#e8f0fe").Padding(5).Text("الإجمالي").FontSize(11).SemiBold();
                table.Cell().Background("#e8f0fe").Padding(5).Text(_vm.TotalAmount.ToString("N2")).FontSize(11).SemiBold();
                table.Cell().ColumnSpan(3).Background("#e8f0fe").Padding(5).Text("");
            });
        });
    }
}
