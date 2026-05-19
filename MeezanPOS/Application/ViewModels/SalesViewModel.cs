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

namespace MeezanPOS.Application.ViewModels;

public partial class SelectableDailyJournal : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private int sequence;

    public DailyJournal Journal { get; }

    public SelectableDailyJournal(DailyJournal journal)
    {
        Journal = journal;
    }
}

public partial class MonthSummaryCard : ObservableObject
{
    [ObservableProperty]
    private int sequence;

    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    
    [ObservableProperty]
    private decimal totalSales;
    
    [ObservableProperty]
    private decimal totalCashSales;
    
    [ObservableProperty]
    private decimal totalBankingSales;
    
    [ObservableProperty]
    private decimal totalExpenses;
    
    public decimal NetProfit => TotalSales - TotalExpenses;
    
    [ObservableProperty]
    private int daysCount;
    
    [ObservableProperty]
    private bool isPosted;
    
    public string StatusText => IsPosted ? "مرحّل بالكامل" : "مفتوح";
    public string StatusColor => IsPosted ? "#10b981" : "#3b82f6";
    public string CardBackground => IsPosted ? "#f0fdf4" : "#f8faff";
    public string CardBorderBrush => IsPosted ? "#dcfce7" : "#e5eeff";
}

public partial class SalesViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<SelectableDailyJournal> journals = new();

    [ObservableProperty]
    private decimal totalSalesPeriod;

    [ObservableProperty]
    private decimal totalCashSalesPeriod;

    [ObservableProperty]
    private decimal totalBankingSalesPeriod;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private ObservableCollection<string> cashierNames = new() { "الكل" };

    [ObservableProperty]
    private string selectedCashier = "الكل";

    [ObservableProperty]
    private System.DateTime? filterStartDate;

    [ObservableProperty]
    private System.DateTime? filterEndDate;

    [ObservableProperty]
    private string filterShiftType = "الكل";

    public ObservableCollection<string> ShiftTypes { get; } = new() { "الكل", "صباحية", "مسائية", "يوم كامل" };

    [ObservableProperty]
    private SelectableDailyJournal? selectedJournal;

    // --- أوامر التصفح الهرمي الجديدة ---
    [ObservableProperty]
    private int selectedTab = 0; // 0 = العمل الحالي, 1 = الأرشيف

    [ObservableProperty]
    private int archiveLevel = 0; // 0 = الأشهر, 1 = الأيام داخل الشهر المحدد

    [ObservableProperty]
    private MonthSummaryCard? selectedArchivedMonth;

    [ObservableProperty]
    private ObservableCollection<MonthSummaryCard> archivedMonths = new();

    [ObservableProperty]
    private ObservableCollection<SelectableDailyJournal> archivedJournals = new();

    [ObservableProperty]
    private decimal runningSelectedSalesTotal;

    [ObservableProperty]
    private decimal runningSelectedExpensesTotal;

    [ObservableProperty]
    private int runningSelectedCount;

    [ObservableProperty]
    private bool isAllSelected;

    [ObservableProperty]
    private bool hasSelectedJournals;

    private bool _isUpdatingSelection;

    private List<DailyJournal> _allJournals = new();

    public SalesViewModel()
    {
        _ = LoadDataAsync();
    }

    partial void OnSelectedTabChanged(int value)
    {
        ApplyFilters();
    }

    [RelayCommand]
    public void SetTab(string tabIndex)
    {
        if (int.TryParse(tabIndex, out int index))
        {
            SelectedTab = index;
        }
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
                .OrderBy(j => j.JournalDate)
                .ThenBy(j => j.Id)
                .ToListAsync();

            // Populate CashierNames
            var uniqueCashiers = _allJournals
                .Where(j => !string.IsNullOrWhiteSpace(j.EmployeeName))
                .Select(j => j.EmployeeName.Trim())
                .Distinct()
                .OrderBy(name => name)
                .ToList();
            
            CashierNames.Clear();
            CashierNames.Add("الكل");
            foreach (var name in uniqueCashiers)
            {
                CashierNames.Add(name);
            }
            if (!CashierNames.Contains(SelectedCashier))
                SelectedCashier = "الكل";

            // Populate Archived Months
            var grouped = _allJournals
                .GroupBy(j => new { j.JournalDate.Year, j.JournalDate.Month })
                .Select(g => {
                    var year = g.Key.Year;
                    var month = g.Key.Month;
                    var isPosted = g.All(j => j.FinancialStatus == FinancialStatus.Posted);
                    return new MonthSummaryCard
                    {
                        Year = year,
                        Month = month,
                        MonthName = $"{GetArabicMonthName(month)} {year}",
                        TotalSales = g.Sum(j => j.TotalSales),
                        TotalCashSales = g.Sum(j => j.CashSales),
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
    public void ClearFilters()
    {
        SelectedCashier = "الكل";
        FilterShiftType = "الكل";
        FilterStartDate = null;
        FilterEndDate = null;
        ApplyFilters();
    }

    [RelayCommand]
    public void ApplyFilters()
    {
        var filtered = _allJournals.AsEnumerable();

        if (SelectedTab == 0)
        {
            // 🟢 العمل الحالي: نعرض فقط اليوميات المفتوحة (Draft)
            filtered = filtered.Where(j => j.FinancialStatus == FinancialStatus.Draft);
        }
        else if (SelectedTab == 1 && SelectedArchivedMonth != null)
        {
            // 🗄️ الأرشيف: نعرض فقط اليوميات التابعة للشهر المحدد
            filtered = filtered.Where(j => j.JournalDate.Year == SelectedArchivedMonth.Year && 
                                           j.JournalDate.Month == SelectedArchivedMonth.Month);
        }
        else if (SelectedTab == 1 && SelectedArchivedMonth == null)
        {
            // مستوى الأشهر: لا نعرض حركات منفردة
            filtered = Enumerable.Empty<DailyJournal>();
        }

        // تطبيق الفلاتر التقليدية
        if (!string.IsNullOrWhiteSpace(SelectedCashier) && SelectedCashier != "الكل")
        {
            filtered = filtered.Where(j => j.EmployeeName.Equals(SelectedCashier, System.StringComparison.OrdinalIgnoreCase));
        }

        if (FilterStartDate.HasValue)
        {
            filtered = filtered.Where(j => j.JournalDate.Date >= FilterStartDate.Value.Date);
        }

        if (FilterEndDate.HasValue)
        {
            filtered = filtered.Where(j => j.JournalDate.Date <= FilterEndDate.Value.Date);
        }

        if (FilterShiftType != "الكل")
        {
            MeezanPOS.Domain.Enums.ShiftType selectedShiftType = MeezanPOS.Domain.Enums.ShiftType.FirstShift;
            if (FilterShiftType == "مسائية") selectedShiftType = MeezanPOS.Domain.Enums.ShiftType.SecondShift;
            else if (FilterShiftType == "يوم كامل") selectedShiftType = MeezanPOS.Domain.Enums.ShiftType.FullDay;

            filtered = filtered.Where(j => j.ShiftType == selectedShiftType);
        }

        var resultList = filtered.ToList();

        if (SelectedTab == 0)
        {
            // إلغاء الاشتراك من العناصر القديمة
            if (Journals != null)
            {
                foreach (var item in Journals)
                {
                    item.PropertyChanged -= Item_PropertyChanged;
                }
            }

            Journals = new ObservableCollection<SelectableDailyJournal>(
                resultList.Select((j, idx) =>
                {
                    var wrapper = new SelectableDailyJournal(j) { Sequence = idx + 1 };
                    wrapper.PropertyChanged += Item_PropertyChanged;
                    return wrapper;
                })
            );
            RecalculateTotals();
        }
        else
        {
            ArchivedJournals = new ObservableCollection<SelectableDailyJournal>(
                resultList.Select((j, idx) => new SelectableDailyJournal(j) { Sequence = idx + 1 })
            );
        }

        TotalSalesPeriod = resultList.Sum(j => j.TotalSales);
        TotalCashSalesPeriod = resultList.Sum(j => j.CashSales);
        TotalBankingSalesPeriod = resultList.Sum(j => j.BankingTotal);
    }

    [RelayCommand]
    public void SelectMonth(MonthSummaryCard month)
    {
        if (month == null) return;
        SelectedArchivedMonth = month;
        ArchiveLevel = 1; // الدخول لمستوى الأيام
        ApplyFilters();
    }

    [RelayCommand]
    public void GoBackToMonths()
    {
        SelectedArchivedMonth = null;
        ArchiveLevel = 0; // العودة لمستوى الأشهر
        ApplyFilters();
    }

    // --- أوامر الترحيل وفك الترحيل الجديدة ---

    [RelayCommand]
    public async Task PostJournalAsync(SelectableDailyJournal selectableJournal)
    {
        if (selectableJournal == null) return;
        var journal = selectableJournal.Journal;

        var result = System.Windows.MessageBox.Show(
            $"هل أنت متأكد من ترحيل الوردية تاريخ {journal.JournalDate:dd-MM-yyyy}؟\nبعد الترحيل لن تتمكن من تعديل أو حذف هذه الوردية والمصاريف الملحقة بها.",
            "تأكيد الترحيل المالي",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            IsLoading = true;
            using var context = new AppDbContext();
            var postingService = new MeezanPOS.Application.Services.PostingService(context);
            await postingService.PostEntityAsync<DailyJournal>(journal.Id, "Admin");

            System.Windows.MessageBox.Show("تم ترحيل الوردية وإقفالها مالياً بنجاح!", "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            await LoadDataAsync();
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"حدث خطأ أثناء الترحيل: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task PostMonthAsync(MonthSummaryCard month)
    {
        if (month == null) return;

        var result = System.Windows.MessageBox.Show(
            $"هل أنت متأكد من ترحيل شهر {month.MonthName} بالكامل؟\nسيتم قفل جميع ورديات هذا الشهر ومصاريفها اليومية ولن تتمكن من تعديلها.",
            "تأكيد ترحيل الشهر بالكامل",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            IsLoading = true;
            using var context = new AppDbContext();
            var postingService = new MeezanPOS.Application.Services.PostingService(context);

            var draftJournals = await context.DailyJournals
                .Where(j => j.JournalDate.Year == month.Year && 
                            j.JournalDate.Month == month.Month && 
                            j.FinancialStatus == FinancialStatus.Draft)
                .ToListAsync();

            if (!draftJournals.Any())
            {
                System.Windows.MessageBox.Show("لا توجد ورديات مفتوحة لترحيلها في هذا الشهر.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            foreach (var j in draftJournals)
            {
                await postingService.PostEntityAsync<DailyJournal>(j.Id, "Admin");
            }

            System.Windows.MessageBox.Show($"تم ترحيل شهر {month.MonthName} بالكامل وإقفاله بنجاح!", "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            await LoadDataAsync();

            if (SelectedArchivedMonth != null && SelectedArchivedMonth.Year == month.Year && SelectedArchivedMonth.Month == month.Month)
            {
                SelectedArchivedMonth = ArchivedMonths.FirstOrDefault(m => m.Year == month.Year && m.Month == month.Month);
            }
            ApplyFilters();
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"خطأ أثناء ترحيل الشهر: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task UnpostMonthAsync(MonthSummaryCard month)
    {
        if (month == null) return;

        var reason = "طلب فك ترحيل الشهر للمراجعة وإعادة التدقيق";

        var result = System.Windows.MessageBox.Show(
            $"هل أنت متأكد من فك ترحيل شهر {month.MonthName} بالكامل؟\nسيتم فتح جميع ورديات هذا الشهر للتعديل مجدداً، وسيتم تسجيل هذا الإجراء في سجل التدقيق.",
            "تأكيد فك الترحيل",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            IsLoading = true;
            using var context = new AppDbContext();
            var postingService = new MeezanPOS.Application.Services.PostingService(context);

            var postedJournals = await context.DailyJournals
                .Where(j => j.JournalDate.Year == month.Year && 
                            j.JournalDate.Month == month.Month && 
                            j.FinancialStatus == FinancialStatus.Posted)
                .ToListAsync();

            if (!postedJournals.Any())
            {
                System.Windows.MessageBox.Show("لا توجد ورديات مرحلة لفكها في هذا الشهر.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            foreach (var j in postedJournals)
            {
                await postingService.UnpostEntityAsync<DailyJournal>(j.Id, reason, "Admin");
            }

            System.Windows.MessageBox.Show($"تم فك ترحيل شهر {month.MonthName} بنجاح، وأصبحت الوردية قابلة للتعديل مجدداً.", "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            await LoadDataAsync();

            if (SelectedArchivedMonth != null && SelectedArchivedMonth.Year == month.Year && SelectedArchivedMonth.Month == month.Month)
            {
                SelectedArchivedMonth = ArchivedMonths.FirstOrDefault(m => m.Year == month.Year && m.Month == month.Month);
            }
            ApplyFilters();
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"خطأ أثناء فك الترحيل: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ViewDetails(SelectableDailyJournal selectableJournal)
    {
        if (selectableJournal == null) return;
        var journal = selectableJournal.Journal;
        
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
    public void EditJournal(SelectableDailyJournal selectableJournal)
    {
        if (selectableJournal == null) return;
        var journal = selectableJournal.Journal;

        // منع التعديل إذا كانت الوردية مرحلة
        if (journal.FinancialStatus == FinancialStatus.Posted || journal.FinancialStatus == FinancialStatus.Archived)
        {
            System.Windows.MessageBox.Show("لا يمكن تعديل حركة مرحّلة مالياً. يرجى فك الترحيل أولاً.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        
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
                column.Item().Text("تقرير المبيعات والإيرادات").FontSize(20).SemiBold().FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);
                
                // بناء سطر معلومات التصفية
                var filterParts = new System.Collections.Generic.List<string>();

                // الكاشير
                if (!string.IsNullOrWhiteSpace(SelectedCashier) && SelectedCashier != "الكل")
                    filterParts.Add($"الكاشير: {SelectedCashier}");

                // نوع الوردية
                if (!string.IsNullOrWhiteSpace(FilterShiftType) && FilterShiftType != "الكل")
                    filterParts.Add($"الوردية: {FilterShiftType}");

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
                    return container.BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).PaddingVertical(5);
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
            row.RelativeItem().AlignLeft().Text("منظومة ميزان").FontSize(12).SemiBold().FontColor(QuestPDF.Helpers.Colors.Grey.Medium);
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

    private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableDailyJournal.IsSelected))
        {
            if (_isUpdatingSelection) return;

            if (sender is SelectableDailyJournal item)
            {
                if (item.IsSelected)
                {
                    RunningSelectedSalesTotal += item.Journal.TotalSales;
                    RunningSelectedExpensesTotal += item.Journal.TotalExpenses;
                    RunningSelectedCount++;
                }
                else
                {
                    RunningSelectedSalesTotal -= item.Journal.TotalSales;
                    RunningSelectedExpensesTotal -= item.Journal.TotalExpenses;
                    RunningSelectedCount--;
                }

                // منع الأرقام السالبة الناتجة عن التقريب العشري
                if (RunningSelectedSalesTotal < 0) RunningSelectedSalesTotal = 0;
                if (RunningSelectedExpensesTotal < 0) RunningSelectedExpensesTotal = 0;
                if (RunningSelectedCount < 0) RunningSelectedCount = 0;

                HasSelectedJournals = RunningSelectedCount > 0;

                _isUpdatingSelection = true;
                try
                {
                    IsAllSelected = Journals.Count > 0 && RunningSelectedCount == Journals.Count;
                }
                finally
                {
                    _isUpdatingSelection = false;
                }
            }
        }
    }

    private void RecalculateTotals()
    {
        decimal salesTotal = 0;
        decimal expensesTotal = 0;
        int count = 0;

        foreach (var item in Journals)
        {
            if (item.IsSelected)
            {
                salesTotal += item.Journal.TotalSales;
                expensesTotal += item.Journal.TotalExpenses;
                count++;
            }
        }

        RunningSelectedSalesTotal = salesTotal;
        RunningSelectedExpensesTotal = expensesTotal;
        RunningSelectedCount = count;
        HasSelectedJournals = count > 0;

        _isUpdatingSelection = true;
        try
        {
            IsAllSelected = Journals.Count > 0 && count == Journals.Count;
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    [RelayCommand]
    public async Task PostSelectedJournalsAsync()
    {
        var selectedIds = Journals.Where(j => j.IsSelected).Select(j => j.Journal.Id).ToList();
        if (!selectedIds.Any())
        {
            System.Windows.MessageBox.Show("يرجى تحديد وردية واحدة على الأقل للترحيل.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var confirmResult = System.Windows.MessageBox.Show(
            $"هل أنت متأكد من ترحيل وإقفال عدد ({selectedIds.Count}) يوميات عمل محددة مالياً؟\n" +
            $"إجمالي المبيعات المحددة: {RunningSelectedSalesTotal:N2} د.ل\n" +
            $"إجمالي المصاريف المحددة: {RunningSelectedExpensesTotal:N2} د.ل\n" +
            $"بعد الترحيل، سيتم قفل هذه العمليات نهائياً ولن تتمكن من تعديلها.",
            "تأكيد الترحيل الجماعي للمؤسسات",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirmResult != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            IsLoading = true;
            using var context = new AppDbContext();
            var postingService = new MeezanPOS.Application.Services.PostingService(context);

            var batchResult = await postingService.PostDailyJournalsBatchAsync(selectedIds, "Admin", "ترحيل جماعي لليوميات المحددة من الواجهة");

            if (batchResult.Success)
            {
                System.Windows.MessageBox.Show(
                    $"تمت عملية الترحيل الجماعي للمؤسسات بنجاح!\n\n" +
                    $"🔹 عدد اليوميات المرحلة: {batchResult.PostedCount}\n" +
                    $"🔹 إجمالي المبيعات المرحلة: {batchResult.TotalSales:N2} د.ل\n" +
                    $"🔹 إجمالي المصاريف المرحلة: {batchResult.TotalExpenses:N2} د.ل\n" +
                    $"🔹 زمن التنفيذ الفعلي: {batchResult.Duration.TotalMilliseconds:N0} مللي ثانية\n" +
                    $"🔹 معرف جلسة الترحيل (Session Guid):\n{batchResult.SessionGuid}\n" +
                    $"🔹 معرف التتبع (Correlation Id):\n{batchResult.CorrelationId}",
                    "نجاح الترحيل الجماعي للمؤسسات",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);

                await LoadDataAsync();
            }
            else
            {
                var errors = string.Join("\n", batchResult.Errors);
                System.Windows.MessageBox.Show($"فشلت عملية الترحيل الجماعي:\n{errors}", "خطأ في الترحيل", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"حدث خطأ غير متوقع أثناء الترحيل الجماعي: {ex.Message}", "خطأ قاتل", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task PostPeriodAsync()
    {
        System.DateTime startDate = System.DateTime.MinValue;
        System.DateTime endDate = System.DateTime.MinValue;
        bool dateSelected = false;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new MeezanPOS.Presentation.Views.DatePeriodSelectionDialog(FilterStartDate, FilterEndDate);
            if (System.Windows.Application.Current.MainWindow != null)
                dialog.Owner = System.Windows.Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                startDate = dialog.SelectedStartDate;
                endDate = dialog.SelectedEndDate;
                dateSelected = true;
            }
        });

        if (!dateSelected) return;

        try
        {
            IsLoading = true;
            using var context = new AppDbContext();
            
            // جلب اليوميات المفتوحة فقط في هذه الفترة
            var journalsInPeriod = await context.DailyJournals
                .Where(j => j.JournalDate.Date >= startDate && j.JournalDate.Date <= endDate && j.FinancialStatus == FinancialStatus.Draft)
                .Select(j => j.Id)
                .ToListAsync();

            if (!journalsInPeriod.Any())
            {
                System.Windows.MessageBox.Show(
                    $"لا توجد أي يوميات عمل مفتوحة (غير مرحلة) في الفترة المحددة:\nمن: {startDate:dd-MM-yyyy} إلى: {endDate:dd-MM-yyyy}",
                    "لا توجد بيانات للترحيل",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            var confirmResult = System.Windows.MessageBox.Show(
                $"هل أنت متأكد من ترحيل وإقفال جميع يوميات العمل المفتوحة في الفترة المحددة؟\n\n" +
                $"الفترة: من {startDate:dd-MM-yyyy} إلى {endDate:dd-MM-yyyy}\n" +
                $"📦 عدد اليوميات المفتوحة المكتشفة: {journalsInPeriod.Count} يومية عمل\n\n" +
                $"بعد الترحيل، سيتم قفل هذه العمليات محاسبياً نهائياً ولن تتمكن من تعديلها.",
                "تأكيد ترحيل وإقفال فترة زمنية",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (confirmResult != System.Windows.MessageBoxResult.Yes) return;

            var postingService = new MeezanPOS.Application.Services.PostingService(context);
            var batchResult = await postingService.PostDailyJournalsBatchAsync(journalsInPeriod, "Admin", $"ترحيل جماعي للفترة من {startDate:dd-MM-yyyy} إلى {endDate:dd-MM-yyyy}");

            if (batchResult.Success)
            {
                System.Windows.MessageBox.Show(
                    $"تمت عملية الترحيل الجماعي للفترة بنجاح!\n\n" +
                    $"الفترة: من {startDate:dd-MM-yyyy} إلى {endDate:dd-MM-yyyy}\n" +
                    $"🔹 عدد اليوميات المرحلة: {batchResult.PostedCount}\n" +
                    $"🔹 إجمالي المبيعات المرحلة: {batchResult.TotalSales:N2} د.ل\n" +
                    $"🔹 إجمالي المصاريف المرحلة: {batchResult.TotalExpenses:N2} د.ل\n" +
                    $"🔹 زمن التنفيذ الفعلي: {batchResult.Duration.TotalMilliseconds:N0} مللي ثانية\n" +
                    $"🔹 معرف الجلسة (Session Guid):\n{batchResult.SessionGuid}\n" +
                    $"🔹 معرف التتبع (Correlation Id):\n{batchResult.CorrelationId}",
                    "نجاح الترحيل الجماعي للفترة الزمنية",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);

                await LoadDataAsync();
            }
            else
            {
                var errors = string.Join("\n", batchResult.Errors);
                System.Windows.MessageBox.Show($"فشلت عملية الترحيل الجماعي:\n{errors}", "خطأ في الترحيل", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"حدث خطأ غير متوقع أثناء الترحيل الجماعي للفترة: {ex.Message}", "خطأ قاتل", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }
}

