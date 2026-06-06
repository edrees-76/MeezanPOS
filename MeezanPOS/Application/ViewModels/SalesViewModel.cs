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
    [NotifyPropertyChangedFor(nameof(NetProfit))]
    private decimal totalSales;
    
    [ObservableProperty]
    private decimal totalCashSales;
    
    [ObservableProperty]
    private decimal totalBankingSales;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetProfit))]
    private decimal totalExpenses;
    
    public decimal NetProfit => TotalSales - TotalExpenses;
    
    [ObservableProperty]
    private int daysCount;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(CardBackground))]
    [NotifyPropertyChangedFor(nameof(CardBorderBrush))]
    private bool isPosted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(CardBackground))]
    [NotifyPropertyChangedFor(nameof(CardBorderBrush))]
    private bool isPartiallyPosted;
    
    public string StatusText => IsPosted ? "مرحّل بالكامل" : (IsPartiallyPosted ? "مرحّل جزئياً" : "مفتوح");
    public string StatusColor => IsPosted ? "#10b981" : (IsPartiallyPosted ? "#f59e0b" : "#3b82f6");
    public string CardBackground => IsPosted ? "#f0fdf4" : (IsPartiallyPosted ? "#fffbeb" : "#f8faff");
    public string CardBorderBrush => IsPosted ? "#dcfce7" : (IsPartiallyPosted ? "#fef3c7" : "#e5eeff");
}

public partial class SalesViewModel : ObservableObject
{
    private string CurrentUserId => AppServiceProvider.Resolve<ISessionService>().CurrentUserId;

    [ObservableProperty]
    private ObservableCollection<CashMovement> cashMovements = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettleKeepCalculation))]
    private decimal currentCashBalance;

    [ObservableProperty]
    private bool isCashLoading;

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
    private string selectedDifferenceFilter = "الكل";

    public ObservableCollection<string> DifferenceFilters { get; } = new() { "الكل", "يوجد فروقات", "مطابق", "عجز", "زيادة" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalDifferenceColor))]
    [NotifyPropertyChangedFor(nameof(TotalDifferenceText))]
    private decimal totalDifferencePeriod;

    public string TotalDifferenceColor => TotalDifferencePeriod < 0 ? "#ef4444" : TotalDifferencePeriod > 0 ? "#3b82f6" : "#10b981";

    public string TotalDifferenceText => TotalDifferencePeriod < 0 ? $"عجز {System.Math.Abs(TotalDifferencePeriod):N2} د.ل" : TotalDifferencePeriod > 0 ? $"زيادة {TotalDifferencePeriod:N2} د.ل" : "مطابق ✓";

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
    private bool isRebuildRequired;

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

    [ObservableProperty]
    private bool isSettleOwnerCashDialogOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettleKeepCalculation))]
    private decimal settlePayoutInput;

    [ObservableProperty]
    private string settleNotesInput = string.Empty;

    [ObservableProperty]
    private bool isSettlementArchiveDialogOpen;

    [ObservableProperty]
    private ObservableCollection<SettlementHistoryItem> settlementHistory = new();

    public decimal SettleKeepCalculation => CurrentCashBalance - SettlePayoutInput;

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
                    var postedCount = g.Count(j => j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived);
                    var totalCount = g.Count();
                    var isPosted = postedCount == totalCount;
                    var isPartiallyPosted = postedCount > 0 && postedCount < totalCount;
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
                        IsPosted = isPosted,
                        IsPartiallyPosted = isPartiallyPosted
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
            await LoadCashMovementsAsync();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading data: {ex.Message}");
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
        SelectedDifferenceFilter = "الكل";
        FilterStartDate = null;
        FilterEndDate = null;
        ApplyFilters();
    }

    [RelayCommand]
    public void ApplyFilters()
    {
        var commonFiltered = _allJournals.AsEnumerable();

        // تطبيق الفلاتر التقليدية
        if (!string.IsNullOrWhiteSpace(SelectedCashier) && SelectedCashier != "الكل")
        {
            commonFiltered = commonFiltered.Where(j => j.EmployeeName.Equals(SelectedCashier, System.StringComparison.OrdinalIgnoreCase));
        }

        if (FilterStartDate.HasValue)
        {
            commonFiltered = commonFiltered.Where(j => j.JournalDate.Date >= FilterStartDate.Value.Date);
        }

        if (FilterEndDate.HasValue)
        {
            commonFiltered = commonFiltered.Where(j => j.JournalDate.Date <= FilterEndDate.Value.Date);
        }

        if (FilterShiftType != "الكل")
        {
            MeezanPOS.Domain.Enums.ShiftType selectedShiftType = MeezanPOS.Domain.Enums.ShiftType.FirstShift;
            if (FilterShiftType == "مسائية") selectedShiftType = MeezanPOS.Domain.Enums.ShiftType.SecondShift;
            else if (FilterShiftType == "يوم كامل") selectedShiftType = MeezanPOS.Domain.Enums.ShiftType.FullDay;

            commonFiltered = commonFiltered.Where(j => j.ShiftType == selectedShiftType);
        }

        if (SelectedDifferenceFilter != "الكل")
        {
            if (SelectedDifferenceFilter == "يوجد فروقات")
            {
                commonFiltered = commonFiltered.Where(j => j.Difference != 0);
            }
            else if (SelectedDifferenceFilter == "مطابق")
            {
                commonFiltered = commonFiltered.Where(j => j.Difference == 0);
            }
            else if (SelectedDifferenceFilter == "عجز")
            {
                commonFiltered = commonFiltered.Where(j => j.Difference < 0);
            }
            else if (SelectedDifferenceFilter == "زيادة")
            {
                commonFiltered = commonFiltered.Where(j => j.Difference > 0);
            }
        }

        if (SelectedTab == 0)
        {
            var resultList = commonFiltered.Where(j => j.FinancialStatus == FinancialStatus.Draft).ToList();

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

            TotalSalesPeriod = resultList.Sum(j => j.TotalSales);
            TotalCashSalesPeriod = resultList.Sum(j => j.CashSales);
            TotalBankingSalesPeriod = resultList.Sum(j => j.BankingTotal);
            TotalDifferencePeriod = resultList.Sum(j => j.Difference);
        }
        else // SelectedTab == 1 (Archive)
        {
            if (SelectedArchivedMonth != null)
            {
                var resultList = commonFiltered.Where(j => j.JournalDate.Year == SelectedArchivedMonth.Year && 
                                                           j.JournalDate.Month == SelectedArchivedMonth.Month).ToList();

                ArchivedJournals = new ObservableCollection<SelectableDailyJournal>(
                    resultList.Select((j, idx) => new SelectableDailyJournal(j) { Sequence = idx + 1 })
                );

                TotalSalesPeriod = resultList.Sum(j => j.TotalSales);
                TotalCashSalesPeriod = resultList.Sum(j => j.CashSales);
                TotalBankingSalesPeriod = resultList.Sum(j => j.BankingTotal);
                TotalDifferencePeriod = resultList.Sum(j => j.Difference);
            }
            else
            {
                // مستوى الأشهر: نقوم بتصفية قائمة الكروت
                var gList = commonFiltered.ToList();
                var grouped = gList
                    .GroupBy(j => new { j.JournalDate.Year, j.JournalDate.Month })
                    .Select(g => {
                        var year = g.Key.Year;
                        var month = g.Key.Month;
                        var postedCount = g.Count(j => j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived);
                        var totalCount = g.Count();
                        var isPosted = postedCount == totalCount;
                        var isPartiallyPosted = postedCount > 0 && postedCount < totalCount;
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
                            IsPosted = isPosted,
                            IsPartiallyPosted = isPartiallyPosted
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

                TotalSalesPeriod = 0;
                TotalCashSalesPeriod = 0;
                TotalBankingSalesPeriod = 0;
                TotalDifferencePeriod = 0;
            }
        }
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
            var postingService = AppServiceProvider.Resolve<IPostingService>();
            await postingService.PostEntityAsync<DailyJournal>(journal.Id, CurrentUserId);

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
            List<DailyJournal> draftJournals;
            using (var context = new AppDbContext())
            {
                draftJournals = await context.DailyJournals
                    .Where(j => j.JournalDate.Year == month.Year && 
                                j.JournalDate.Month == month.Month && 
                                j.FinancialStatus == FinancialStatus.Draft)
                    .ToListAsync();
            }

            if (!draftJournals.Any())
            {
                System.Windows.MessageBox.Show("لا توجد ورديات مفتوحة لترحيلها في هذا الشهر.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var postingService = AppServiceProvider.Resolve<IPostingService>();
            foreach (var j in draftJournals)
            {
                await postingService.PostEntityAsync<DailyJournal>(j.Id, CurrentUserId);
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

        if (!AppServiceProvider.Resolve<ISessionService>().HasPermission("UnpostFinancial"))
        {
            System.Windows.MessageBox.Show("عذراً، هذا الإجراء متاح فقط للمدير العام.", "صلاحية غير كافية", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        // 1. تحقق مما إذا كان الشهر يحتوي على ورديات تابعة لفترة تمت تسويتها وإغلاقها مسبقاً
        using (var context = new AppDbContext())
        {
            var settledSessionIds = await context.PostingSessions
                .Where(s => s.Status == PostingSessionStatus.Settled || s.Status == PostingSessionStatus.ReSettled)
                .Select(s => s.Id)
                .ToListAsync();
            
            var hasSettled = await context.DailyJournals
                .AnyAsync(j => j.JournalDate.Year == month.Year && 
                               j.JournalDate.Month == month.Month && 
                               j.PostingSessionId.HasValue && 
                               settledSessionIds.Contains(j.PostingSessionId.Value));
            
            if (hasSettled)
            {
                System.Windows.MessageBox.Show(
                    "عذراً، هذا الشهر يحتوي على ورديات تابعة لفترة تم تسويتها وإقفالها مسبقاً.\nيجب إلغاء قفل فترة التسوية المعنية أولاً من شاشة (الكاش الحالي -> أرشيف التسويات).",
                    "فترة مغلقة ومسواة",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
        }

        // 2. إظهار نافذة إدخال سبب فك الترحيل
        bool dialogResult = false;
        string selectedReason = string.Empty;
        string detailReason = string.Empty;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new MeezanPOS.Presentation.Views.PeriodUnlockDialog();
            if (System.Windows.Application.Current.MainWindow != null)
                dialog.Owner = System.Windows.Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                selectedReason = dialog.SelectedReason;
                detailReason = dialog.SelectedDetailReason;
                dialogResult = true;
            }
        });

        if (!dialogResult) return;

        var reason = $"{selectedReason} - {detailReason}";

        var result = System.Windows.MessageBox.Show(
            $"هل أنت متأكد من فك ترحيل شهر {month.MonthName} بالكامل؟\nسيتم فتح جميع ورديات هذا الشهر للتعديل مجدداً، وسيتم تسجيل هذا الإجراء في سجل التدقيق.",
            "تأكيد فك الترحيل",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            IsLoading = true;
            List<DailyJournal> postedJournals;
            using (var context = new AppDbContext())
            {
                postedJournals = await context.DailyJournals
                    .Where(j => j.JournalDate.Year == month.Year && 
                                j.JournalDate.Month == month.Month && 
                                j.FinancialStatus == FinancialStatus.Posted)
                    .ToListAsync();
            }

            if (!postedJournals.Any())
            {
                System.Windows.MessageBox.Show("لا توجد ورديات مرحلة لفكها في هذا الشهر.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var postingService = AppServiceProvider.Resolve<IPostingService>();
            foreach (var j in postedJournals)
            {
                await postingService.UnpostEntityAsync<DailyJournal>(j.Id, reason, CurrentUserId);
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
            var postingService = AppServiceProvider.Resolve<IPostingService>();

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
            List<int> journalsInPeriod;
            using (var context = new AppDbContext())
            {
                // جلب اليوميات المفتوحة فقط في هذه الفترة
                journalsInPeriod = await context.DailyJournals
                    .Where(j => j.JournalDate.Date >= startDate && j.JournalDate.Date <= endDate && j.FinancialStatus == FinancialStatus.Draft)
                    .Select(j => j.Id)
                    .ToListAsync();
            }

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

            var postingService = AppServiceProvider.Resolve<IPostingService>();
            var batchResult = await postingService.PostDailyJournalsBatchAsync(journalsInPeriod, CurrentUserId, $"ترحيل جماعي للفترة من {startDate:dd-MM-yyyy} إلى {endDate:dd-MM-yyyy}");

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

    [RelayCommand]
    public async Task LoadCashMovementsAsync()
    {
        IsCashLoading = true;
        try
        {
            using var context = new AppDbContext();
            var movements = await context.CashMovements
                .OrderByDescending(m => m.TransactionDate)
                .ThenByDescending(m => m.Id)
                .Take(150)
                .ToListAsync();

            movements.Reverse();

            // Add sequence numbers
            for (int i = 0; i < movements.Count; i++)
            {
                movements[i].Sequence = i + 1;
            }

            CashMovements = new ObservableCollection<CashMovement>(movements);
            
            var cashLedgerService = AppServiceProvider.Resolve<ICashLedgerService>();
            CurrentCashBalance = await cashLedgerService.GetCurrentBalanceAsync();
            IsRebuildRequired = await cashLedgerService.IsRebuildRequiredAsync();
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"خطأ في تحميل حركات النقدية: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsCashLoading = false;
        }
    }

    [RelayCommand]
    public async Task RebuildCashLedgerAsync()
    {
        var result = System.Windows.MessageBox.Show(
            "هل أنت متأكد من إعادة بناء دفتر النقدية؟\nسيقوم هذا الإجراء بإعادة حساب الأرصدة التراكمية بناءً على الترتيب التاريخي للحركات.",
            "تأكيد إعادة البناء",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        IsCashLoading = true;
        try
        {
            using var context = new AppDbContext();
            var cashLedgerService = AppServiceProvider.Resolve<ICashLedgerService>();
            await cashLedgerService.RebuildLedgerAsync();
            await LoadCashMovementsAsync();
            System.Windows.MessageBox.Show("تم إعادة بناء دفتر النقدية بنجاح!", "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"خطأ أثناء إعادة بناء الدفتر: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsCashLoading = false;
        }
    }

    [RelayCommand]
    public void ViewCashMovementDetails(CashMovement movement)
    {
        if (movement == null) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var window = System.Windows.Application.Current.MainWindow;
            var detailsDialog = new MeezanPOS.Presentation.Views.TransactionDetailsViewWindow(movement)
            {
                Owner = window
            };
            detailsDialog.ShowDialog();
        });
    }

    [RelayCommand]
    public async Task OpenSettleOwnerCashDialogAsync()
    {
        IsCashLoading = true;
        try
        {
            using var context = new AppDbContext();
            var cashLedgerService = AppServiceProvider.Resolve<ICashLedgerService>();
            CurrentCashBalance = await cashLedgerService.GetCurrentBalanceAsync();
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"خطأ في تحديث رصيد الخزينة: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsCashLoading = false;
        }

        SettlePayoutInput = 0;
        SettleNotesInput = $"تسوية نقدية للمالك - فترة {System.DateTime.Now:yyyy/MM/dd}";
        IsSettleOwnerCashDialogOpen = true;
    }

    [RelayCommand]
    public void CloseSettleOwnerCashDialog()
    {
        IsSettleOwnerCashDialogOpen = false;
    }

    [RelayCommand]
    public async Task ConfirmSettleOwnerCashAsync()
    {
        if (SettlePayoutInput < 0)
        {
            System.Windows.MessageBox.Show("يجب إدخال مبلغ صحيح وموجب للتسوية.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (SettlePayoutInput > CurrentCashBalance)
        {
            System.Windows.MessageBox.Show("المبلغ المطلوب أكبر من الرصيد المتاح في الخزينة.", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"هل أنت متأكد من تأكيد تسوية الخزينة وسحب مبلغ للمالك؟\n\n" +
            $"💰 السيولة النقدية المتوفرة: {CurrentCashBalance:N2} د.ل\n" +
            $"💸 المبلغ المسحوب للمالك: {SettlePayoutInput:N2} د.ل\n" +
            $"⚙️ السيولة المتبقية بالصندوق: {SettleKeepCalculation:N2} د.ل\n\n" +
            $"سيتم ترحيل وتأكيد كافة اليوميات والمصاريف اليومية غير المرحلة وتجميدها نهائياً.",
            "تأكيد تسوية الخزينة وإقفال الفترة",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            var postingService = AppServiceProvider.Resolve<IPostingService>();

            var batchResult = await postingService.SettleAndLockPeriodAsync(
                payoutAmount: SettlePayoutInput,
                keepAmount: SettleKeepCalculation,
                notes: SettleNotesInput,
                postedByUserId: CurrentUserId
            );

            if (batchResult.Success)
            {
                IsSettleOwnerCashDialogOpen = false;

                // تحديث البيانات
                await LoadCashMovementsAsync();
                await LoadDataAsync();

                var successMsg = $"تمت عملية التسوية بنجاح وتجميد الحركات السابقة!\n\n" +
                                 $"🔹 الرصيد السابق: {batchResult.BalanceBefore:N2} د.ل\n" +
                                 $"🔹 المسلم للمالك: {batchResult.PayoutAmount:N2} د.ل\n" +
                                 $"🔹 المتبقي بالخزينة: {batchResult.KeepAmount:N2} د.ل\n" +
                                 $"🔹 عدد اليوميات والمصاريف المقفلة: {batchResult.PostedCount}\n" +
                                 $"🔹 معرف الجلسة: {batchResult.SessionGuid.ToString().Substring(0,8).ToUpper()}";

                System.Windows.MessageBox.Show(successMsg, "نجاح تسوية الخزينة", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                // فتح ملف الـ PDF تلقائياً للمستند المولد
                if (!string.IsNullOrEmpty(batchResult.PdfPath) && System.IO.File.Exists(batchResult.PdfPath))
                {
                    using var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo(batchResult.PdfPath) { UseShellExecute = true }
                    };
                    process.Start();
                }
            }
            else
            {
                var errors = string.Join("\n", batchResult.Errors);
                System.Windows.MessageBox.Show($"فشلت عملية تسوية الخزينة:\n{errors}", "خطأ في التسوية", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"حدث خطأ غير متوقع أثناء تسوية الخزينة: {ex.Message}", "خطأ قاتل", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task OpenSettlementArchiveAsync()
    {
        IsLoading = true;
        try
        {
            var postingService = AppServiceProvider.Resolve<IPostingService>();
            var history = await postingService.GetSettlementHistoryAsync();
            
            SettlementHistory.Clear();
            foreach (var item in history)
            {
                SettlementHistory.Add(item);
            }
            IsSettlementArchiveDialogOpen = true;
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"فشل تحميل أرشيف التسويات: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void CloseSettlementArchive()
    {
        IsSettlementArchiveDialogOpen = false;
    }

    [RelayCommand]
    public async Task UnlockPeriodAsync(SettlementHistoryItem item)
    {
        if (item == null) return;
        if (!AppServiceProvider.Resolve<ISessionService>().HasPermission("UnpostFinancial"))
        {
            System.Windows.MessageBox.Show("عذراً، هذا الإجراء متاح فقط للمدير العام.", "صلاحية غير كافية", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        bool dialogResult = false;
        string reason = string.Empty;
        string detailReason = string.Empty;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new MeezanPOS.Presentation.Views.PeriodUnlockDialog();
            if (System.Windows.Application.Current.MainWindow != null)
                dialog.Owner = System.Windows.Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                reason = dialog.SelectedReason;
                detailReason = dialog.SelectedDetailReason;
                dialogResult = true;
            }
        });

        if (!dialogResult) return;

        IsLoading = true;
        try
        {
            var postingService = AppServiceProvider.Resolve<IPostingService>();
            var success = await postingService.UnlockPeriodAsync(item.SessionId, reason, detailReason, CurrentUserId);

            if (success)
            {
                System.Windows.MessageBox.Show("تم إلغاء قفل الفترة بنجاح للمراجعة والتدقيق.", "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                
                // تحديث قائمة أرشيف التسويات
                var history = await postingService.GetSettlementHistoryAsync();
                SettlementHistory.Clear();
                foreach (var h in history)
                {
                    SettlementHistory.Add(h);
                }
                await LoadDataAsync();
            }
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"فشل إلغاء قفل الفترة: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task UnpostPeriodAsync(SettlementHistoryItem item)
    {
        if (item == null) return;
        if (!AppServiceProvider.Resolve<ISessionService>().HasPermission("UnpostFinancial"))
        {
            System.Windows.MessageBox.Show("عذراً، هذا الإجراء متاح فقط للمدير العام.", "صلاحية غير كافية", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        if (item.Status != PostingSessionStatus.Unlocked)
        {
            System.Windows.MessageBox.Show("يجب إلغاء قفل الفترة أولاً قبل البدء بفك الترحيل المجمع.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        int journalsCount = 0;
        int expensesCount = 0;
        decimal totalReversedAmount = 0;

        try
        {
            using (var context = new AppDbContext())
            {
                journalsCount = await context.DailyJournals.CountAsync(j => j.PostingSessionId == item.SessionId);
                expensesCount = await context.GeneralExpenses.CountAsync(e => e.PostingSessionId == item.SessionId);
                
                var journalCash = await context.DailyJournals
                    .Where(j => j.PostingSessionId == item.SessionId)
                    .Select(j => j.ActualCash - j.CashFloat)
                    .ToListAsync();
                var expenseAmounts = await context.GeneralExpenses
                    .Where(e => e.PostingSessionId == item.SessionId)
                    .Select(e => e.Amount)
                    .ToListAsync();

                totalReversedAmount = journalCash.Sum() + expenseAmounts.Sum();
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"خطأ في جلب بيانات الفترة: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        decimal currentBalance = CurrentCashBalance;
        decimal expectedBalance = currentBalance - totalReversedAmount;

        var confirmMsg = $"تنبيه: أنت على وشك فك ترحيل الفترة بالكامل كحزمة واحدة.\n\n" +
                         $"📦 تفاصيل العملية العكسية:\n" +
                         $"🔹 عدد اليوميات المتأثرة: {journalsCount} يومية\n" +
                         $"🔹 عدد المصاريف المتأثرة: {expensesCount} مصروف\n" +
                         $"🔹 إجمالي القيمة المسترجعة (عكس حركة الخزينة): {totalReversedAmount:N2} د.ل\n\n" +
                         $"💰 الرصيد الحالي للخزينة: {currentBalance:N2} د.ل\n" +
                         $"📉 الرصيد المتوقع بعد العكس: {expectedBalance:N2} د.ل\n\n" +
                         $"هل تريد فك ترحيل الفترة وعكس حركات النقدية آلياً؟";

        var confirmResult = System.Windows.MessageBox.Show(confirmMsg, "تأكيد العمليات العكسية وفك ترحيل الفترة", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirmResult != System.Windows.MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            var postingService = AppServiceProvider.Resolve<IPostingService>();
            var reason = $"إجراء فك ترحيل الفترة للجلسة {item.ShortSessionGuid} بواسطة المدير العام";
            var success = await postingService.UnpostPeriodAsync(item.SessionId, reason, CurrentUserId);

            if (success)
            {
                System.Windows.MessageBox.Show("تم فك ترحيل الفترة بالكامل وإلغاء وعكس حركات النقدية بنجاح.", "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                
                // تحديث قائمة أرشيف التسويات
                var history = await postingService.GetSettlementHistoryAsync();
                SettlementHistory.Clear();
                foreach (var h in history)
                {
                    SettlementHistory.Add(h);
                }
                await LoadDataAsync();
                await LoadCashMovementsAsync();
            }
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"فشل فك ترحيل الفترة: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RegenerateSettlementPdfAsync(SettlementHistoryItem item)
    {
        if (item == null) return;
        
        IsLoading = true;
        try
        {
            var postingService = AppServiceProvider.Resolve<IPostingService>();
            string pdfPath = await postingService.RegenerateSettlementPdfAsync(item.SessionId);

            if (!string.IsNullOrEmpty(pdfPath) && System.IO.File.Exists(pdfPath))
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo(pdfPath) { UseShellExecute = true }
                };
                process.Start();
            }
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"فشل إعادة توليد ملف PDF: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }
}

