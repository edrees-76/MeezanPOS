using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using System.Threading.Tasks;
using System.Windows.Input;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using MeezanPOS.Application.Services;
using MeezanPOS.Application.Interfaces;

namespace MeezanPOS.Application.ViewModels;

/// <summary>تصدير قائمة اليوميات إلى PDF.</summary>
public partial class SalesViewModel
{
    [RelayCommand]
    public void ExportPdf()
    {
        try
        {

            var filePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"تقرير_المبيعات_{System.DateTime.Now:yyyyMMdd_HHmmss}.pdf");

            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(QuestPDF.Helpers.PageSizes.A4.Portrait());
                    page.Margin(0.8f, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.PageColor(QuestPDF.Helpers.Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial").DirectionFromRightToLeft());
                    page.ContentFromRightToLeft();

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeContent);
                    page.Footer().Element(ComposeFooter);
                });
            })
            .GeneratePdf(filePath);

            // فتح الملف مباشرة
            using (var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(filePath)
                {
                    UseShellExecute = true
                }
            })
            {
                process.Start();
            }
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء تصدير ملف PDF: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ComposeHeader(QuestPDF.Infrastructure.IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("تقرير المبيعات والإيرادات").FontSize(20).SemiBold().FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);

                // بناء سطر معلومات التصفية
                var filterParts = new System.Collections.Generic.List<string>();

                // الكاشير
                if (!string.IsNullOrWhiteSpace(SelectedCashier) && SelectedCashier != "الكل")
                    filterParts.Add($"الكاشير: {SelectedCashier}");

                // نوع الوردية
                if (!string.IsNullOrWhiteSpace(FilterShiftType) && FilterShiftType != "الكل")
                    filterParts.Add($"الوردية: {FilterShiftType}");

                // فلتر الفروقات
                if (!string.IsNullOrWhiteSpace(SelectedDifferenceFilter) && SelectedDifferenceFilter != "الكل")
                    filterParts.Add($"الفروقات: {SelectedDifferenceFilter}");

                // التاريخ من - إلى
                System.DateTime? startDate = FilterStartDate;
                System.DateTime? endDate = FilterEndDate;

                var currentList = SelectedTab == 0 ? Journals : ArchivedJournals;

                if (currentList != null && currentList.Count > 0)
                {
                    if (!startDate.HasValue)
                        startDate = currentList.Min(j => j.Journal.JournalDate);
                    if (!endDate.HasValue)
                        endDate = currentList.Max(j => j.Journal.JournalDate);
                }

                if (startDate.HasValue && endDate.HasValue)
                    filterParts.Add($"من {startDate.Value:yyyy/MM/dd} إلى {endDate.Value:yyyy/MM/dd}");
                else if (startDate.HasValue)
                    filterParts.Add($"من {startDate.Value:yyyy/MM/dd}");
                else if (endDate.HasValue)
                    filterParts.Add($"حتى {endDate.Value:yyyy/MM/dd}");

                if (filterParts.Count > 0)
                    column.Item().Text(string.Join("  |  ", filterParts)).FontSize(10).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1);
            });
        });
    }

    private void ComposeContent(QuestPDF.Infrastructure.IContainer container)
    {
        container.PaddingVertical(1, QuestPDF.Infrastructure.Unit.Centimetre).Column(column =>
        {
            // Cards Summary
            column.Item().Row(row =>
            {
                row.RelativeItem().PaddingRight(6).Background(QuestPDF.Helpers.Colors.Grey.Lighten4).Padding(6).Column(c =>
                {
                    c.Item().Text("إجمالي المبيعات").FontSize(11).SemiBold().FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);
                    c.Item().Text($"{TotalSalesPeriod:N2} د.ل").FontSize(13).Bold();
                });

                row.RelativeItem().PaddingRight(6).Background(QuestPDF.Helpers.Colors.Grey.Lighten4).Padding(6).Column(c =>
                {
                    c.Item().Text("إجمالي المبيعات النقدية").FontSize(11).SemiBold().FontColor(QuestPDF.Helpers.Colors.Green.Darken2);
                    c.Item().Text($"{TotalCashSalesPeriod:N2} د.ل").FontSize(13).Bold();
                });

                row.RelativeItem().PaddingRight(6).Background(QuestPDF.Helpers.Colors.Grey.Lighten4).Padding(6).Column(c =>
                {
                    c.Item().Text("إجمالي الخدمات المصرفية").FontSize(11).SemiBold().FontColor(QuestPDF.Helpers.Colors.Purple.Darken2);
                    c.Item().Text($"{TotalBankingSalesPeriod:N2} د.ل").FontSize(13).Bold();
                });

                row.RelativeItem().Background(QuestPDF.Helpers.Colors.Grey.Lighten4).Padding(6).Column(c =>
                {
                    c.Item().Text("إجمالي الفروقات").FontSize(11).SemiBold().FontColor(QuestPDF.Helpers.Colors.Grey.Darken3);
                    var diffColor = TotalDifferencePeriod < 0 ? QuestPDF.Helpers.Colors.Red.Medium : TotalDifferencePeriod > 0 ? QuestPDF.Helpers.Colors.Blue.Medium : QuestPDF.Helpers.Colors.Green.Medium;
                    c.Item().Text(TotalDifferenceText).FontSize(13).Bold().FontColor(diffColor);
                });
            });

            column.Item().PaddingTop(20).Element(ComposeTable);
        });
    }

    private void ComposeTable(QuestPDF.Infrastructure.IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3); // التسلسل
                columns.RelativeColumn(5); // التاريخ
                columns.RelativeColumn(5); // الوردية
                columns.RelativeColumn(8); // الكاشير
                columns.RelativeColumn(5); // المبيعات
                columns.RelativeColumn(5); // النقدي
                columns.RelativeColumn(5); // المصرفي
                columns.RelativeColumn(5); // المرتجعات
                columns.RelativeColumn(5); // المجاني
                columns.RelativeColumn(5); // المصاريف
                columns.RelativeColumn(6); // الفروقات
            });

            table.Header(header =>
            {
                header.Cell().Element(CellStyle).Text("التسلسل");
                header.Cell().Element(CellStyle).Text("التاريخ");
                header.Cell().Element(CellStyle).Text("الوردية");
                header.Cell().Element(CellStyle).Text("اسم الكاشير");
                header.Cell().Element(CellStyle).Text("المبيعات");
                header.Cell().Element(CellStyle).Text("النقدي");
                header.Cell().Element(CellStyle).Text("المصرفي");
                header.Cell().Element(CellStyle).Text("المرتجعات");
                header.Cell().Element(CellStyle).Text("المجاني");
                header.Cell().Element(CellStyle).Text("المصاريف");
                header.Cell().Element(CellStyle).Text("الفروقات");

                QuestPDF.Infrastructure.IContainer CellStyle(QuestPDF.Infrastructure.IContainer container)
                {
                    return container.DefaultTextStyle(x => x.SemiBold().FontSize(7.5f)).PaddingVertical(4).BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Black);
                }
            });

            var currentList = SelectedTab == 0 ? Journals : ArchivedJournals;

            int pdfSeq = 1;
            foreach (var item in currentList.OrderBy(j => j.Journal.Id))
            {
                table.Cell().Element(CellStyle).Text(pdfSeq.ToString());
                pdfSeq++;
                table.Cell().Element(CellStyle).Text(item.Journal.JournalDate.ToString("dd-MM-yyyy"));
                table.Cell().Element(CellStyle).Text(item.Journal.ShiftName);
                table.Cell().Element(CellStyle).Text(item.Journal.EmployeeName);
                table.Cell().Element(CellStyle).Text(item.Journal.TotalSales.ToString("N2"));
                table.Cell().Element(CellStyle).Text(item.Journal.CashSales.ToString("N2"));
                table.Cell().Element(CellStyle).Text(item.Journal.BankingTotal.ToString("N2"));
                table.Cell().Element(CellStyle).Text(item.Journal.ReturnsTotal.ToString("N2"));
                table.Cell().Element(CellStyle).Text(item.Journal.FreeOrdersTotal.ToString("N2"));
                table.Cell().Element(CellStyle).Text(item.Journal.TotalExpenses.ToString("N2"));

                var diffColor = item.Journal.Difference < 0 ? QuestPDF.Helpers.Colors.Red.Medium : item.Journal.Difference > 0 ? QuestPDF.Helpers.Colors.Black : QuestPDF.Helpers.Colors.Green.Medium;
                table.Cell().Element(CellStyle).Text(item.Journal.DifferenceText).FontColor(diffColor).Bold();

                QuestPDF.Infrastructure.IContainer CellStyle(QuestPDF.Infrastructure.IContainer container)
                {
                    return container.DefaultTextStyle(x => x.FontSize(7.5f)).BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).PaddingVertical(4);
                }
            }
        });
    }

    private void ComposeFooter(QuestPDF.Infrastructure.IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().AlignRight().Text(x =>
            {
                x.Span("صفحة ");
                x.CurrentPageNumber();
                x.Span(" من ");
                x.TotalPages();
            });
            row.RelativeItem().AlignLeft().Text(MeezanPOS.Infrastructure.Data.RestaurantContext.ReportFooter).FontSize(12).SemiBold().FontColor(QuestPDF.Helpers.Colors.Grey.Medium);
        });
    }

    partial void OnIsAllSelectedChanged(bool value)
    {
        if (_isUpdatingSelection) return;

        _isUpdatingSelection = true;
        try
        {
            foreach (var item in Journals)
            {
                item.IsSelected = value;
            }
            RecalculateTotals();
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }
}
