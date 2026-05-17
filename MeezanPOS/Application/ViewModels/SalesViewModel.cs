using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Infrastructure.Data;
using System.Threading.Tasks;
using System.Windows.Input;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MeezanPOS.Application.ViewModels;

public partial class SalesViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<DailyJournal> journals = new();

    [ObservableProperty]
    private decimal totalSalesPeriod;

    [ObservableProperty]
    private decimal totalCashSalesPeriod;

    [ObservableProperty]
    private decimal totalBankingSalesPeriod;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private System.DateTime? filterDate;

    [ObservableProperty]
    private string filterShiftType = "الكل";

    public ObservableCollection<string> ShiftTypes { get; } = new() { "الكل", "صباحية", "مسائية", "يوم كامل" };

    [ObservableProperty]
    private DailyJournal? selectedJournal;

    private List<DailyJournal> _allJournals = new();

    public SalesViewModel()
    {
        _ = LoadDataAsync();
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            using var context = new AppDbContext();
            
            // Get all journals ordered by date descending
            _allJournals = await context.DailyJournals
                .Include(j => j.ExpenseItems)
                .Include(j => j.BankingItems)
                .Include(j => j.Adjustments)
                .OrderByDescending(j => j.JournalDate)
                .ThenByDescending(j => j.Id)
                .ToListAsync();

            ApplyFilters();
        }
        catch
        {
            // Handle exceptions
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ApplyFilters()
    {
        var filtered = _allJournals.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(j => j.EmployeeName.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase));
        }

        if (FilterDate.HasValue)
        {
            filtered = filtered.Where(j => j.JournalDate.Date == FilterDate.Value.Date);
        }

        if (FilterShiftType != "الكل")
        {
            MeezanPOS.Domain.Enums.ShiftType selectedShiftType = MeezanPOS.Domain.Enums.ShiftType.FirstShift;
            if (FilterShiftType == "مسائية") selectedShiftType = MeezanPOS.Domain.Enums.ShiftType.SecondShift;
            else if (FilterShiftType == "يوم كامل") selectedShiftType = MeezanPOS.Domain.Enums.ShiftType.FullDay;

            filtered = filtered.Where(j => j.ShiftType == selectedShiftType);
        }

        var resultList = filtered.ToList();
        Journals = new ObservableCollection<DailyJournal>(resultList);

        TotalSalesPeriod = resultList.Sum(j => j.TotalSales);
        TotalCashSalesPeriod = resultList.Sum(j => j.CashSales);
        TotalBankingSalesPeriod = resultList.Sum(j => j.BankingTotal);
    }

    [RelayCommand]
    public void ViewDetails(DailyJournal journal)
    {
        if (journal == null) return;
        
        var mainWindow = System.Windows.Application.Current.MainWindow;
        if (mainWindow?.DataContext is MainViewModel mainVM)
        {
            var journalVM = new DailyJournalViewModel();
            journalVM.LoadJournalForViewing(journal);
            journalVM.OnClose = () =>
            {
                mainVM.CurrentViewModel = new SalesViewModel();
                mainVM.Title = "ميزان للمالية - المبيعات والإيرادات";
            };
            mainVM.CurrentViewModel = journalVM;
            mainVM.Title = "ميزان للمالية - عرض تفاصيل الحركة اليومية";
        }
    }

    [RelayCommand]
    public void EditJournal(DailyJournal journal)
    {
        if (journal == null) return;
        
        var mainWindow = System.Windows.Application.Current.MainWindow;
        if (mainWindow?.DataContext is MainViewModel mainVM)
        {
            var journalVM = new DailyJournalViewModel();
            journalVM.LoadJournalForEditing(journal);
            journalVM.OnClose = () =>
            {
                mainVM.CurrentViewModel = new SalesViewModel();
                mainVM.Title = "ميزان للمالية - المبيعات والإيرادات";
            };
            mainVM.CurrentViewModel = journalVM;
            mainVM.Title = "ميزان للمالية - تعديل الحركة اليومية";
        }
    }

    [RelayCommand]
    public void ExportPdf()
    {
        try
        {
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            var filePath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), 
                $"تقرير_المبيعات_{System.DateTime.Now:yyyyMMdd_HHmmss}.pdf");

            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(QuestPDF.Helpers.PageSizes.A4.Landscape());
                    page.Margin(1, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.PageColor(QuestPDF.Helpers.Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial").DirectionFromRightToLeft());
                    page.ContentFromRightToLeft();

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeContent);
                    page.Footer().Element(ComposeFooter);
                });
            })
            .GeneratePdf(filePath);

            // فتح الملف مباشرة
            new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(filePath)
                {
                    UseShellExecute = true
                }
            }.Start();
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"حدث خطأ أثناء تصدير ملف PDF: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ComposeHeader(QuestPDF.Infrastructure.IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("تقرير المبيعات والإيرادات - منظومة ميزان").FontSize(20).SemiBold().FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);
                column.Item().Text($"تاريخ الإصدار: {System.DateTime.Now:dd/MM/yyyy HH:mm}");
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
                row.RelativeItem().PaddingRight(10).Background(QuestPDF.Helpers.Colors.Grey.Lighten4).Padding(10).Column(c =>
                {
                    c.Item().Text("إجمالي المبيعات").FontSize(14).SemiBold().FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);
                    c.Item().Text($"{TotalSalesPeriod:N2} د.ل").FontSize(16).Bold();
                });

                row.RelativeItem().PaddingRight(10).Background(QuestPDF.Helpers.Colors.Grey.Lighten4).Padding(10).Column(c =>
                {
                    c.Item().Text("إجمالي المبيعات النقدية").FontSize(14).SemiBold().FontColor(QuestPDF.Helpers.Colors.Green.Darken2);
                    c.Item().Text($"{TotalCashSalesPeriod:N2} د.ل").FontSize(16).Bold();
                });

                row.RelativeItem().Background(QuestPDF.Helpers.Colors.Grey.Lighten4).Padding(10).Column(c =>
                {
                    c.Item().Text("إجمالي الخدمات المصرفية").FontSize(14).SemiBold().FontColor(QuestPDF.Helpers.Colors.Purple.Darken2);
                    c.Item().Text($"{TotalBankingSalesPeriod:N2} د.ل").FontSize(16).Bold();
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
                    return container.DefaultTextStyle(x => x.SemiBold()).PaddingVertical(5).BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Black);
                }
            });

            foreach (var item in Journals.OrderBy(j => j.Id))
            {
                table.Cell().Element(CellStyle).Text(item.Id.ToString());
                table.Cell().Element(CellStyle).Text(item.JournalDate.ToString("dd-MM-yyyy"));
                table.Cell().Element(CellStyle).Text(item.ShiftName);
                table.Cell().Element(CellStyle).Text(item.EmployeeName);
                table.Cell().Element(CellStyle).Text(item.TotalSales.ToString("N2"));
                table.Cell().Element(CellStyle).Text(item.CashSales.ToString("N2"));
                table.Cell().Element(CellStyle).Text(item.BankingTotal.ToString("N2"));
                table.Cell().Element(CellStyle).Text(item.ReturnsTotal.ToString("N2"));
                table.Cell().Element(CellStyle).Text(item.FreeOrdersTotal.ToString("N2"));
                table.Cell().Element(CellStyle).Text(item.TotalExpenses.ToString("N2"));
                
                var diffColor = item.Difference < 0 ? QuestPDF.Helpers.Colors.Red.Medium : item.Difference > 0 ? QuestPDF.Helpers.Colors.Black : QuestPDF.Helpers.Colors.Green.Medium;
                table.Cell().Element(CellStyle).Text(item.DifferenceText).FontColor(diffColor).Bold();

                QuestPDF.Infrastructure.IContainer CellStyle(QuestPDF.Infrastructure.IContainer container)
                {
                    return container.BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).PaddingVertical(5);
                }
            }
        });
    }

    private void ComposeFooter(QuestPDF.Infrastructure.IContainer container)
    {
        container.AlignCenter().Text(x =>
        {
            x.Span("صفحة ");
            x.CurrentPageNumber();
            x.Span(" من ");
            x.TotalPages();
        });
    }
}
