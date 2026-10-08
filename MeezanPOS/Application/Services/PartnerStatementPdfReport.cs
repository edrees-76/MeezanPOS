using System;
using System.Collections.Generic;
using System.Linq;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MeezanPOS.Application.Services;

public class PartnerStatementPdfReport
{
    public static void GeneratePdf(
        string filePath,
        string partnerName,
        decimal totalOpeningBalance,
        decimal totalIncreases,
        decimal totalDecreases,
        decimal finalBalance,
        PartnerBalanceDirection balanceDirection,
        List<PartnerStatementEntryDto> transactions,
        DateTime startDate,
        DateTime endDate)
    {

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Portrait());
                page.Margin(1.0f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));
                
                // RTL Support for Arabic
                page.ContentFromRightToLeft();

                page.Header().Element(c => ComposeHeader(c, partnerName, startDate, endDate, finalBalance, balanceDirection));
                page.Content().Element(c => ComposeContent(c, totalOpeningBalance, totalIncreases, totalDecreases, finalBalance, balanceDirection, transactions));
                page.Footer().Element(ComposeFooter);
            });
        })
        .GeneratePdf(filePath);
    }

    private static void ComposeHeader(IContainer container, string partnerName, DateTime startDate, DateTime endDate, decimal finalBalance, PartnerBalanceDirection direction)
    {
        container.PaddingBottom(0.5f, Unit.Centimetre).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("كشف حساب تمويل تشغيلي (شريك)").FontSize(22).Bold().FontColor(Colors.Indigo.Darken3);
                column.Item().Text($"اسم الشريك: {partnerName}").FontSize(14).SemiBold().FontColor(Colors.Grey.Darken3);
                
                string dateText = $"الفترة الزمنية: من {startDate:yyyy/MM/dd} إلى {endDate:yyyy/MM/dd}";
                column.Item().PaddingTop(2).Text(dateText).FontSize(10).FontColor(Colors.Grey.Darken1);
            });

            row.ConstantItem(160).Background(Colors.Indigo.Lighten5)
                .Border(1.2f)
                .BorderColor(Colors.Indigo.Lighten2)
                .Padding(10)
                .Column(column =>
                {
                    column.Item().Text("صافي الرصيد الحالي").FontSize(10).SemiBold().AlignCenter().FontColor(Colors.Grey.Darken3);
                    column.Item().Text($"{finalBalance:N2} د.ل").FontSize(16).Bold().AlignCenter().FontColor(Colors.Indigo.Darken4);
                    
                    string directionStr = direction switch
                    {
                        PartnerBalanceDirection.PartnerOwesRestaurant => "مطلوب منه (سحب زائد)",
                        PartnerBalanceDirection.RestaurantOwesPartner => "مستحق له (تمويل)",
                        _ => "حساب مسوّى"
                    };
                    
                    var directionColor = direction switch
                    {
                        PartnerBalanceDirection.PartnerOwesRestaurant => Colors.Red.Medium,
                        PartnerBalanceDirection.RestaurantOwesPartner => Colors.Green.Medium,
                        _ => Colors.Grey.Medium
                    };

                    column.Item().PaddingTop(4).Background(directionColor).PaddingVertical(2).AlignCenter().Text(directionStr).FontSize(9).SemiBold().FontColor(Colors.White);
                });
        });
    }

    private static void ComposeContent(
        IContainer container,
        decimal openingBalance,
        decimal totalIncreases,
        decimal totalDecreases,
        decimal finalBalance,
        PartnerBalanceDirection direction,
        List<PartnerStatementEntryDto> transactions)
    {
        container.Column(column =>
        {
            // Summary Cards
            column.Item().PaddingBottom(0.6f, Unit.Centimetre).Row(row =>
            {
                row.RelativeItem().Element(c => DrawSummaryCard(c, "رصيد مرحل سابق", openingBalance, Colors.Grey.Darken1, Colors.Black));
                row.RelativeItem().PaddingRight(8).Element(c => DrawSummaryCard(c, "تمويل وتسويات (+)", totalIncreases, Colors.Green.Darken2, Colors.Green.Darken3));
                row.RelativeItem().PaddingRight(8).Element(c => DrawSummaryCard(c, "مسحوبات شخصية (-)", totalDecreases, Colors.Red.Darken2, Colors.Red.Darken3));
                row.RelativeItem().PaddingRight(8).Element(c => DrawSummaryCard(c, "الصافي الختامي", finalBalance, Colors.Indigo.Darken2, Colors.Indigo.Darken4));
            });

            // Transactions Table
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(30);    // Sequential
                    columns.ConstantColumn(80);    // Date
                    columns.RelativeColumn(3.5f);  // Description
                    columns.RelativeColumn(1f);    // Increase (تمويل)
                    columns.RelativeColumn(1f);    // Decrease (سحب)
                    columns.RelativeColumn(1.2f);  // Running Balance
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderStyle).Text("ت").SemiBold();
                    header.Cell().Element(HeaderStyle).Text("التاريخ").SemiBold();
                    header.Cell().Element(HeaderStyle).Text("البيان والتفاصيل").SemiBold();
                    header.Cell().Element(HeaderStyle).Text("تمويل (+)").SemiBold();
                    header.Cell().Element(HeaderStyle).Text("سحب (-)").SemiBold();
                    header.Cell().Element(HeaderStyle).Text("الرصيد").SemiBold();

                    static IContainer HeaderStyle(IContainer hContainer)
                    {
                        return hContainer.Background(Colors.Indigo.Darken3)
                                         .DefaultTextStyle(x => x.SemiBold().FontSize(9).FontColor(Colors.White))
                                         .Padding(6)
                                         .Border(0.5f)
                                         .BorderColor(Colors.Grey.Lighten1)
                                         .AlignCenter();
                    }
                });

                foreach (var tx in transactions)
                {
                    table.Cell().Element(CellStyle).Text(tx.SequenceNumber.ToString());
                    table.Cell().Element(CellStyle).Text(tx.TransactionDate.ToString("yyyy/MM/dd"));
                    table.Cell().Element(CellStyle).AlignLeft().Text(tx.Description);
                    
                    // !IsSettlement = Partner paid (Increase/Funding for the restaurant's debt to partner)
                    // IsSettlement = Restaurant paid back (Decrease/Withdrawal)
                    table.Cell().Element(CellStyle).Text(!tx.IsSettlement ? $"{tx.Amount:N2}" : "").FontColor(Colors.Green.Darken3).SemiBold();
                    table.Cell().Element(CellStyle).Text(tx.IsSettlement ? $"{tx.Amount:N2}" : "").FontColor(Colors.Red.Darken3).SemiBold();
                    
                    table.Cell().Element(CellStyle).Text($"{tx.RunningBalance:N2}").Bold().FontColor(Colors.Indigo.Darken4);

                    static IContainer CellStyle(IContainer cContainer)
                    {
                        return cContainer.Padding(5)
                                         .BorderBottom(0.5f)
                                         .BorderColor(Colors.Grey.Lighten2)
                                         .AlignCenter();
                    }
                }
            });
            
        });
    }

    private static void DrawSummaryCard(IContainer container, string title, decimal value, string textColor, string valueColor)
    {
        container.Background(Colors.Grey.Lighten5)
            .Border(1)
            .BorderColor(Colors.Grey.Lighten3)
            .Padding(8)
            .Column(col =>
            {
                col.Item().Text(title).FontSize(8).SemiBold().AlignCenter().FontColor(textColor);
                col.Item().PaddingTop(2).Text($"{value:N2}").FontSize(12).Bold().AlignCenter().FontColor(valueColor);
            });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.BorderTop(0.5f)
            .BorderColor(Colors.Grey.Lighten1)
            .PaddingTop(8)
            .Row(row =>
            {
                row.RelativeItem().AlignRight().Text(x =>
                {
                    x.Span("صفحة ");
                    x.CurrentPageNumber();
                    x.Span(" من ");
                    x.TotalPages();
                });
                row.RelativeItem().AlignLeft().PaddingRight(5).Text($"صدر في: {DateTime.Now:yyyy/MM/dd HH:mm}").FontSize(8).FontColor(Colors.Grey.Medium);
                row.RelativeItem().AlignCenter().Text("منظومة ميزان").FontSize(12).SemiBold().FontColor(Colors.Grey.Medium);
            });
    }
}
