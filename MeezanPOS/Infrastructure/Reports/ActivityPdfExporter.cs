using System;
using System.Collections.Generic;
using MeezanPOS.Application.Services.Queries;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using static MeezanPOS.Infrastructure.Reports.ReportStyle;

namespace MeezanPOS.Infrastructure.Reports;

/// <summary>تقرير سجل نشاط المستخدمين للفترة والفلاتر المختارة.</summary>
public static class ActivityPdfExporter
{
    public static void GenerateReport(string filePath, IReadOnlyList<ActivityRow> rows, string filterText)
        => Build(rows, filterText).GeneratePdf(filePath);

    public static IDocument Build(IReadOnlyList<ActivityRow> rows, string filterText)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                SetupPage(page);
                page.Header().Element(c => Header(c, "سجل نشاط المستخدمين", filterText, DateTime.Now, "تابع: سجل نشاط المستخدمين"));

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text($"عدد العمليات: {rows.Count}").FontSize(9).FontColor(SlateMuted);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(2.2f);
                            cols.RelativeColumn(2.6f);
                            cols.RelativeColumn(2.2f);
                            cols.RelativeColumn(2f);
                            cols.RelativeColumn(4f);
                        });
                        table.Header(h =>
                        {
                            foreach (var title in new[] { "الوقت", "المستخدم", "العملية", "السجل", "التفاصيل" })
                                h.Cell().Element(HeaderCell).Text(title).Bold().FontSize(8.5f).FontColor(HeaderText);
                        });
                        foreach (var row in rows)
                        {
                            table.Cell().Element(BodyCell).Text(row.TimeText).FontSize(8);
                            table.Cell().Element(BodyCell).Text(row.UserName).FontSize(8);
                            table.Cell().Element(BodyCell).Text(row.ActionName).FontSize(8).Bold();
                            table.Cell().Element(BodyCell).Text(row.RecordText).FontSize(8);
                            table.Cell().Element(BodyCell).Text(row.Details).FontSize(7.5f).FontColor(SlateMuted);
                        }
                    });
                });
            });
        });
    }
}
