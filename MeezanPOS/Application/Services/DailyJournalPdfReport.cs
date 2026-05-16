using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.IO;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Application.Services;

public class DailyJournalPdfReport : IDocument
{
    private readonly DailyJournalViewModel _vm;

    public DailyJournalPdfReport(DailyJournalViewModel vm)
    {
        _vm = vm;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public void Compose(IDocumentContainer container)
    {
        container
            .Page(page =>
            {
                page.Margin(50);
                page.Size(PageSizes.A4);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(14).FontFamily(Fonts.Arial));
                page.ContentFromRightToLeft(); // For Arabic

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().AlignCenter().Text(x =>
                {
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("تقرير الحركة اليومية").FontSize(24).SemiBold().FontColor(Colors.Blue.Darken2);
                column.Item().Text($"التاريخ: {_vm.JournalDate:dd-MM-yyyy}");
                column.Item().Text($"نوع الوردية: {_vm.ShiftTypes[_vm.SelectedShiftIndex]}");
                column.Item().Text($"اسم الكاشير: {_vm.EmployeeName}");
            });
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
        {
            column.Spacing(20);

            // المبيعات
            column.Item().Text("1. المبيعات والإيرادات").FontSize(16).SemiBold().Underline();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                table.Cell().Text("إجمالي المبيعات:");
                table.Cell().Text($"{_vm.TotalSales:N2} د.ل");
                
                table.Cell().Text("المرتجعات:");
                table.Cell().Text($"{_vm.ReturnsAmount:N2} د.ل");

                table.Cell().Text("الطلبات المجانية:");
                table.Cell().Text($"{_vm.FreeOrdersAmount:N2} د.ل");
            });

            // الخدمات المصرفية
            column.Item().Text("2. الخدمات المصرفية").FontSize(16).SemiBold().Underline();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                table.Cell().Text("المصرفية من الدفتر:");
                table.Cell().Text($"{_vm.BankingItemsTotal:N2} د.ل");
                table.Cell().Text("المصرفية من الكاشير:");
                table.Cell().Text($"{_vm.BankingSalesInput:N2} د.ل");
                table.Cell().Text("نتيجة مطابقة المصرفية:");
                table.Cell().Text($"{_vm.BankingDifferenceStatus}");
            });

            // المصروفات
            column.Item().Text("3. المصروفات النثرية").FontSize(16).SemiBold().Underline();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                table.Cell().Text("إجمالي المصروفات:");
                table.Cell().Text($"{_vm.TotalExpenses:N2} د.ل");
            });

            // المطابقة والتسليم
            column.Item().Text("4. المطابقة والتسليم النقدي").FontSize(16).SemiBold().Underline();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                table.Cell().Text("النقد المتوقع في الدرج:");
                table.Cell().Text($"{_vm.ExpectedCash:N2} د.ل");
                table.Cell().Text("النقد الفعلي المستلم:");
                table.Cell().Text($"{_vm.ActualCash:N2} د.ل");
                table.Cell().Text("نتيجة المطابقة (الفروقات):");
                table.Cell().Text($"{_vm.DifferenceStatus}").FontColor(Colors.Green.Darken2).SemiBold();
            });

            // Notes
            if (!string.IsNullOrWhiteSpace(_vm.Notes))
            {
                column.Item().Text("5. ملاحظات").FontSize(16).SemiBold().Underline();
                column.Item().Text(_vm.Notes);
            }
        });
    }
}
