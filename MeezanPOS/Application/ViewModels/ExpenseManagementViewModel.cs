using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.IO;

namespace MeezanPOS.Application.ViewModels;

/// <summary>
/// عنصر مصروف واحد لعرضه في قائمة المصروفات
/// </summary>
public partial class ExpenseDisplayItem : ObservableObject
{
    public int Sequence { get; set; }
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string DateDisplay => Date.ToString("yyyy/MM/dd");
    public string ShiftDisplay { get; set; } = string.Empty;
    public string ExpenseType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
}

public partial class DailyExpenseSummaryItem : ObservableObject
{
    public int Sequence { get; set; }
    public DateTime Date { get; set; }
    public string DateDisplay => Date.ToString("yyyy/MM/dd");
    public decimal TotalAmount { get; set; }
}

public partial class ShiftExpenseSummaryItem : ObservableObject
{
    public int JournalId { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public string CashierName { get; set; } = string.Empty;
    public decimal ShiftTotal { get; set; }
    public ObservableCollection<ExpenseDisplayItem> Expenses { get; } = new();
}

public partial class ExpenseManagementViewModel : ObservableObject
{
    public Action? OnBack { get; set; }
    public Action<int>? OnEditJournal { get; set; }

    // --- حالة الواجهة ---
    [ObservableProperty]
    private bool isViewingDetails = false;

    [ObservableProperty]
    private bool showPostedArchive = false;

    [ObservableProperty]
    private string selectedDateDetailsTitle = "تفاصيل مصاريف يوم: ";

    // --- مستويات الأرشيف الهرمي الجديد ---
    [ObservableProperty]
    private int archiveLevel = 0; // 0 = الأشهر, 1 = الأيام داخل الشهر المحدد

    [ObservableProperty]
    private MonthSummaryCard? selectedArchivedMonth;

    [ObservableProperty]
    private ObservableCollection<MonthSummaryCard> archivedMonths = new();

    partial void OnShowPostedArchiveChanged(bool value)
    {
        ArchiveLevel = 0;
        SelectedArchivedMonth = null;
        if (value)
        {
            _ = LoadArchivedMonthsAsync();
        }
        else
        {
            LoadExpenses();
        }
    }

    [RelayCommand]
    private void SetShowPostedArchive(string value)
    {
        if (bool.TryParse(value, out bool result))
        {
            ShowPostedArchive = result;
        }
    }

    // --- فلاتر البحث ---
    [ObservableProperty]
    private DateTime dateFrom = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime dateTo = DateTime.Today;

    [ObservableProperty]
    private ShiftType? selectedShiftType;

    [ObservableProperty]
    private string selectedCashierName = "الكل";

    // --- البيانات ---
    public ObservableCollection<DailyExpenseSummaryItem> DailySummaries { get; } = new();

    // --- تفاصيل اليوم المعروض ---
    [ObservableProperty]
    private DateTime selectedDateDetails;
    public string SelectedDateDetailsDisplay => SelectedDateDetails.ToString("yyyy/MM/dd");

    partial void OnSelectedDateDetailsChanged(DateTime value)
    {
        OnPropertyChanged(nameof(SelectedDateDetailsDisplay));
    }

    [ObservableProperty]
    private decimal dayGrandTotal;

    public ObservableCollection<ShiftExpenseSummaryItem> DayShifts { get; } = new();

    // --- قوائم الفلترة ---
    public ObservableCollection<ShiftFilterItem> Shifts { get; } = new();
    public ObservableCollection<string> Cashiers { get; } = new();

    [ObservableProperty]
    private decimal totalFilteredExpensesAmount;

    private readonly MeezanPOS.Application.Services.Queries.IJournalExpenseQueryService _queries;

    /// <summary>التبويب المعروض: 0 المصاريف اليومية، 1 المصاريف العامة.</summary>
    [ObservableProperty]
    private int selectedTabIndex;

    public ExpenseManagementViewModel(MeezanPOS.Application.Services.Queries.IJournalExpenseQueryService? queries = null)
    {
        _queries = queries ?? new MeezanPOS.Application.Services.Queries.JournalExpenseQueryService();
        InitializeFilters();
        LoadCashiers();
        LoadExpenses();
    }

    private void InitializeFilters()
    {
        Shifts.Add(new ShiftFilterItem { Name = "الكل", Value = null });
        Shifts.Add(new ShiftFilterItem { Name = "الوردية الأولى", Value = ShiftType.FirstShift });
        Shifts.Add(new ShiftFilterItem { Name = "الوردية الثانية", Value = ShiftType.SecondShift });
        Shifts.Add(new ShiftFilterItem { Name = "يوم كامل", Value = ShiftType.FullDay });
        SelectedShiftType = null;
    }

    private void LoadCashiers()
    {
        try
        {
            var names = _queries.GetCashierNames();

            Cashiers.Clear();
            Cashiers.Add("الكل");
            foreach (var n in names)
            {
                if (n != null) Cashiers.Add(n);
            }
        }
        catch (Exception ex)
        {
            // فلتر الكاشير اختياري: الشاشة تعمل بدونه، لكن يجب أن يُسجل السبب
            Serilog.Log.Warning(ex, "تعذر تحميل أسماء الكاشيرية لفلتر المصروفات");
        }
    }

    [RelayCommand]
    private void LoadExpenses()
    {
        try
        {
            // 1) جلب كل الأيام التي فيها مصاريف لبناء تسلسل ثابت عالمي للتبويب النشط
            var journalsForSeq = _queries.GetJournalsWithExpenses(ShowPostedArchive);

            var allDays = journalsForSeq
                .GroupBy(j => j.JournalDate.Date)
                .Where(g => g.Sum(j => j.ExpenseItems.Sum(e => e.Amount)) > 0)
                .OrderBy(g => g.Key)
                .Select((g, index) => new { Date = g.Key, Sequence = index + 1 })
                .ToDictionary(x => x.Date, x => x.Sequence);

            // 2) تطبيق الفلاتر على البيانات
            DateTime startFilter = DateFrom.Date;
            DateTime endFilter = DateTo.Date;

            if (ShowPostedArchive && ArchiveLevel == 1 && SelectedArchivedMonth != null)
            {
                startFilter = new DateTime(SelectedArchivedMonth.Year, SelectedArchivedMonth.Month, 1);
                endFilter = startFilter.AddMonths(1).AddDays(-1);
            }

            var journals = _queries.GetJournalsWithExpenses(new MeezanPOS.Application.Services.Queries.JournalExpenseFilter(
                ShowPostedArchive, startFilter, endFilter, SelectedShiftType, SelectedCashierName));

            var dailyGroup = journals
                .GroupBy(j => j.JournalDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Total = g.Sum(j => j.ExpenseItems.Sum(e => e.Amount))
                })
                .Where(x => x.Total > 0)
                .OrderBy(x => x.Date)
                .ToList();

            DailySummaries.Clear();
            decimal total = 0;
            foreach (var item in dailyGroup)
            {
                DailySummaries.Add(new DailyExpenseSummaryItem
                {
                    Sequence = allDays.ContainsKey(item.Date) ? allDays[item.Date] : 0,
                    Date = item.Date,
                    TotalAmount = item.Total
                });
                total += item.Total;
            }

            TotalFilteredExpensesAmount = total;
            IsViewingDetails = false;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ في تحميل المصروفات");
            Dialogs.Show($"حدث خطأ أثناء تحميل المصروفات:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ResetFilters()
    {
        DateFrom = DateTime.Today.AddMonths(-1);
        DateTo = DateTime.Today;
        SelectedShiftType = null;
        SelectedCashierName = "الكل";
        LoadExpenses();
    }

    [RelayCommand]
    private void ViewDayDetails(DateTime date)
    {
        try
        {
            // تحديث العنوان بناءً على حالة الأرشيف
            if (ShowPostedArchive)
            {
                SelectedDateDetailsTitle = "تفاصيل مصاريف يوم (مرحّل): ";
            }
            else
            {
                SelectedDateDetailsTitle = "تفاصيل مصاريف يوم: ";
            }

            var journals = _queries.GetJournalsWithExpenses(new MeezanPOS.Application.Services.Queries.JournalExpenseFilter(
                ShowPostedArchive, date.Date, date.Date.AddDays(1).AddTicks(-1), SelectedShiftType, SelectedCashierName,
                IncludeSuppliers: true));

            DayShifts.Clear();
            decimal grandTotal = 0;

            foreach (var j in journals.Where(x => x.ExpenseItems.Any()))
            {
                var shiftTotal = j.ExpenseItems.Sum(e => e.Amount);
                var shiftItem = new ShiftExpenseSummaryItem
                {
                    JournalId = j.Id,
                    ShiftName = DailyJournalViewModel.GetShiftDisplayName(j.ShiftType),
                    CashierName = j.EmployeeName ?? "—",
                    ShiftTotal = shiftTotal
                };

                var sortedExpenses = j.ExpenseItems.OrderBy(e => e.SequenceNumber).ToList();
                foreach (var e in sortedExpenses)
                {
                    shiftItem.Expenses.Add(new ExpenseDisplayItem
                    {
                        Sequence = e.SequenceNumber,
                        Id = e.Id,
                        Date = j.JournalDate,
                        ShiftDisplay = shiftItem.ShiftName,
                        ExpenseType = e.CategoryName ?? "—",
                        Amount = e.Amount,
                        SupplierName = e.Supplier?.Name ?? e.SupplierName ?? "—",
                        Description = e.Description ?? "",
                        Notes = e.Notes ?? "",
                        InvoiceNumber = e.InvoiceNumber ?? ""
                    });
                }

                DayShifts.Add(shiftItem);
                grandTotal += shiftTotal;
            }

            SelectedDateDetails = date;
            DayGrandTotal = grandTotal;
            IsViewingDetails = true;
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء عرض التفاصيل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void EditShiftExpense(int journalId)
    {
        OnEditJournal?.Invoke(journalId);
    }

    [RelayCommand]
    private void CloseDetails()
    {
        IsViewingDetails = false;
        LoadExpenses(); // Reload in case edits were made
    }

    [RelayCommand]
    private void GoBack()
    {
        if (IsViewingDetails)
        {
            CloseDetails();
        }
        else if (ShowPostedArchive && ArchiveLevel == 1)
        {
            GoBackToMonths();
        }
        else
        {
            OnBack?.Invoke();
        }
    }

    [RelayCommand]
    private void SelectMonth(MonthSummaryCard month)
    {
        SelectedArchivedMonth = month;
        ArchiveLevel = 1;
        LoadExpenses();
    }

    [RelayCommand]
    private void GoBackToMonths()
    {
        ArchiveLevel = 0;
        SelectedArchivedMonth = null;
        _ = LoadArchivedMonthsAsync();
    }

    public static string GetArabicMonthName(int month) => month switch
    {
        1 => "يناير",
        2 => "فبراير",
        3 => "مارس",
        4 => "أبريل",
        5 => "مايو",
        6 => "يونيو",
        7 => "يوليو",
        8 => "أغسطس",
        9 => "سبتمبر",
        10 => "أكتوبر",
        11 => "نوفمبر",
        12 => "ديسمبر",
        _ => ""
    };

    [RelayCommand]
    public async Task LoadArchivedMonthsAsync()
    {
        try
        {
            var journals = await _queries.GetArchivedJournalTotalsAsync();

            var grouped = journals
                .GroupBy(j => new { j.JournalDate.Year, j.JournalDate.Month })
                .Select(g => {
                    var year = g.Key.Year;
                    var month = g.Key.Month;
                    var isPosted = g.All(j => j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived);
                    return new MonthSummaryCard
                    {
                        Year = year,
                        Month = month,
                        MonthName = $"{GetArabicMonthName(month)} {year}",
                        TotalSales = g.Sum(j => j.TotalSales),
                        TotalDeductions = g.Sum(j => j.ReturnsTotal + j.FreeOrdersTotal),
                        TotalCashSales = g.Sum(j => j.TotalSales - j.BankingTotal),
                        TotalBankingSales = g.Sum(j => j.BankingTotal),
                        TotalExpenses = g.Sum(j => j.TotalExpenses),
                        DaysCount = g.Select(j => j.JournalDate.Date).Distinct().Count(),
                        IsPosted = isPosted
                    };
                })
                .OrderByDescending(m => m.Year)
                .ThenByDescending(m => m.Month)
                .Select((m, idx) => {
                    m.Sequence = idx + 1;
                    return m;
                })
                .ToList();

            ArchivedMonths.Clear();
            foreach (var m in grouped)
            {
                ArchivedMonths.Add(m);
            }
        }
        catch (Exception ex)
        {
            // كان الخطأ يُكتب في نافذة التصحيح فقط فتظهر شاشة الأرشيف فارغة بلا تفسير
            Serilog.Log.Error(ex, "خطأ في تحميل كروت أشهر الأرشيف");
            Dialogs.Show($"تعذر تحميل أشهر الأرشيف:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void PrintDailySummaries()
    {
        try
        {
            if (DailySummaries == null || !DailySummaries.Any())
            {
                Dialogs.Show("لا يوجد بيانات لطباعتها.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string pdfPath = Path.Combine(Path.GetTempPath(), $"ExpenseSummaries_{DateTime.Now.Ticks}.pdf");

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20, Unit.Millimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(11));
                    page.ContentFromRightToLeft();

                    page.Header().Element(ComposeSummaryHeader);
                    page.Content().Element(ComposeSummaryContent);
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
            });

            document.GeneratePdf(pdfPath);
            var p = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(pdfPath) { UseShellExecute = true }
            };
            p.Start();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"خطأ في طباعة التقرير: {ex.Message}");
        }
    }

    private void ComposeSummaryHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("تقرير المصروفات الإجمالي").FontSize(20).SemiBold().FontColor(Colors.Blue.Darken2);
                column.Item().Text($"من تاريخ: {DateFrom:yyyy/MM/dd} إلى تاريخ: {DateTo:yyyy/MM/dd}").FontSize(14);

                string shiftName = SelectedShiftType.HasValue ? DailyJournalViewModel.GetShiftDisplayName(SelectedShiftType.Value) : "الكل";
                string cashierName = string.IsNullOrEmpty(SelectedCashierName) ? "الكل" : SelectedCashierName;

                column.Item().Text($"الوردية: {shiftName}  |  الكاشير: {cashierName}").FontSize(12).FontColor(Colors.Grey.Darken2);
            });
        });
    }

    private void ComposeSummaryContent(IContainer container)
    {
        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
        {
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(80); // التسلسل
                    columns.RelativeColumn(3); // التاريخ
                    columns.RelativeColumn(2); // الإجمالي
                });

                table.Header(header =>
                {
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingBottom(5).Text("التسلسل").SemiBold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingBottom(5).Text("تاريخ المصروفات").SemiBold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingBottom(5).AlignRight().Text("إجمالي اليوم (د.ل)").SemiBold();
                });

                foreach (var summary in DailySummaries)
                {
                    table.Cell().PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Text(summary.Sequence.ToString());
                    table.Cell().PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Text(summary.Date.ToString("yyyy/MM/dd"));
                    table.Cell().PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).AlignRight().Text($"{summary.TotalAmount:N2}").Bold();
                }
            });

            column.Item().PaddingTop(20).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
            column.Item().PaddingTop(10).AlignRight().Text($"الإجمالي الكلي: {TotalFilteredExpensesAmount:N2} د.ل").FontSize(16).Bold().FontColor(Colors.Green.Darken2);
        });
    }

    [RelayCommand]
    private void PrintDayExpenses()
    {
        try
        {
            string pdfPath = Path.Combine(Path.GetTempPath(), $"Expenses_{SelectedDateDetails:yyyyMMdd}_{DateTime.Now.Ticks}.pdf");

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20, Unit.Millimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(11));
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
            });

            document.GeneratePdf(pdfPath);
            var p = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(pdfPath) { UseShellExecute = true }
            };
            p.Start();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"خطأ في طباعة التقرير: {ex.Message}");
        }
    }

    private void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("تقرير المصروفات التفصيلي").FontSize(20).SemiBold().FontColor(Colors.Blue.Darken2);
                column.Item().Text($"التاريخ: {SelectedDateDetailsDisplay}").FontSize(14);
            });
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
        {
            foreach (var shift in DayShifts)
            {
                column.Item().PaddingBottom(10).Background(Colors.Grey.Lighten3).Padding(5).Row(row =>
                {
                    row.RelativeItem().Text($"الوردية: {shift.ShiftName} | الكاشير: {shift.CashierName}").Bold();
                    row.RelativeItem().AlignRight().Text($"الإجمالي: {shift.ShiftTotal:N2} د.ل").Bold().FontColor(Colors.Red.Darken2);
                });

                column.Item().PaddingBottom(15).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(50); // التسلسل
                        columns.RelativeColumn(2); // النوع
                        columns.RelativeColumn(3); // المورد
                        columns.RelativeColumn(3); // الوصف/الملاحظات
                        columns.RelativeColumn(2); // المبلغ
                    });

                    table.Header(header =>
                    {
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingBottom(5).Text("التسلسل").SemiBold();
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingBottom(5).Text("نوع المصروف").SemiBold();
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingBottom(5).Text("المورد/الجهة").SemiBold();
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingBottom(5).Text("البيان").SemiBold();
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingBottom(5).AlignRight().Text("المبلغ (د.ل)").SemiBold();
                    });

                    int seq = 1;
                    foreach (var exp in shift.Expenses)
                    {
                        table.Cell().PaddingVertical(3).Text((seq++).ToString());
                        table.Cell().PaddingVertical(3).Text(exp.ExpenseType);
                        table.Cell().PaddingVertical(3).Text(exp.SupplierName);
                        table.Cell().PaddingVertical(3).Text(exp.Description + " " + exp.Notes);
                        table.Cell().PaddingVertical(3).AlignRight().Text($"{exp.Amount:N2}").Bold();
                    }
                });
            }

            column.Item().PaddingTop(20).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
            column.Item().PaddingTop(10).AlignRight().Text($"الإجمالي العام لليوم: {DayGrandTotal:N2} د.ل").FontSize(16).Bold().FontColor(Colors.Green.Darken2);
        });
    }
}

public class ShiftFilterItem
{
    public string Name { get; set; } = string.Empty;
    public ShiftType? Value { get; set; }
}
