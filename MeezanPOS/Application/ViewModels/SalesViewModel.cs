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

public partial class SalesViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;
    private readonly IPostingService _postingService;
    private readonly ICashLedgerService _cashLedgerService;
    private readonly MeezanPOS.Application.Services.Queries.ISalesQueryService _queries;

    public SalesViewModel() : this(
        AppServiceProvider.Resolve<ISessionService>(),
        AppServiceProvider.Resolve<IPostingService>(),
        AppServiceProvider.Resolve<ICashLedgerService>())
    {
    }

    public SalesViewModel(
        ISessionService sessionService,
        IPostingService postingService,
        ICashLedgerService cashLedgerService,
        MeezanPOS.Application.Services.Queries.ISalesQueryService? queries = null)
    {
        _queries = queries ?? new MeezanPOS.Application.Services.Queries.SalesQueryService();
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _postingService = postingService ?? throw new ArgumentNullException(nameof(postingService));
        _cashLedgerService = cashLedgerService ?? throw new ArgumentNullException(nameof(cashLedgerService));

        _ = LoadDataAsync();
    }

    private string CurrentUserId => _sessionService.CurrentUserId;

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
            _allJournals = await _queries.GetAllJournalsWithItemsAsync();

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

                    var postedJournals = g.Where(j => j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived).ToList();

                    return new MonthSummaryCard
                    {
                        Year = year,
                        Month = month,
                        MonthName = $"{GetArabicMonthName(month)} {year}",
                        TotalSales = postedJournals.Sum(j => j.TotalSales),
                        TotalCashSales = postedJournals.Sum(j => j.CashSales),
                        TotalBankingSales = postedJournals.Sum(j => j.BankingTotal),
                        TotalExpenses = postedJournals.Sum(j => j.TotalExpenses),
                        DaysCount = postedJournals.Select(j => j.JournalDate.Date).Distinct().Count(),
                        IsPosted = isPosted,
                        IsPartiallyPosted = isPartiallyPosted
                    };
                })
                .Where(m => m.IsPosted || m.IsPartiallyPosted) // Only show months with at least one posted/archived journal
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
            // كان الخطأ يُكتب في نافذة التصحيح فقط فتظهر شاشة المبيعات فارغة بلا تفسير
            Serilog.Log.Error(ex, "خطأ في تحميل بيانات المبيعات");
            Dialogs.Show($"تعذر تحميل بيانات المبيعات:\n{ex.Message}", "خطأ في التحميل", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
                                                           j.JournalDate.Month == SelectedArchivedMonth.Month &&
                                                           (j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived)).ToList();

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

                        var postedJournals = g.Where(j => j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived).ToList();

                        return new MonthSummaryCard
                        {
                            Year = year,
                            Month = month,
                            MonthName = $"{GetArabicMonthName(month)} {year}",
                            TotalSales = postedJournals.Sum(j => j.TotalSales),
                            TotalCashSales = postedJournals.Sum(j => j.CashSales),
                            TotalBankingSales = postedJournals.Sum(j => j.BankingTotal),
                            TotalExpenses = postedJournals.Sum(j => j.TotalExpenses),
                            DaysCount = postedJournals.Select(j => j.JournalDate.Date).Distinct().Count(),
                            IsPosted = isPosted,
                            IsPartiallyPosted = isPartiallyPosted
                        };
                    })
                    .Where(m => m.IsPosted || m.IsPartiallyPosted) // Only show months with at least one posted/archived journal
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

        var result = Dialogs.Show(
            $"هل أنت متأكد من ترحيل الوردية تاريخ {journal.JournalDate:dd-MM-yyyy}؟\nبعد الترحيل لن تتمكن من تعديل أو حذف هذه الوردية والمصاريف الملحقة بها.",
            "تأكيد الترحيل المالي",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            IsLoading = true;
            var postingService = _postingService;
            await postingService.PostEntityAsync<DailyJournal>(journal.Id, CurrentUserId);

            Dialogs.Show("تم ترحيل الوردية وإقفالها مالياً بنجاح!", "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            await LoadDataAsync();
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء الترحيل: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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

        var result = Dialogs.Show(
            $"هل أنت متأكد من ترحيل شهر {month.MonthName} بالكامل؟\nسيتم قفل جميع ورديات هذا الشهر ومصاريفها اليومية ولن تتمكن من تعديلها.",
            "تأكيد ترحيل الشهر بالكامل",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            IsLoading = true;
            var draftJournals = await _queries.GetJournalIdsInMonthAsync(month.Year, month.Month, FinancialStatus.Draft);

            if (!draftJournals.Any())
            {
                Dialogs.Show("لا توجد ورديات مفتوحة لترحيلها في هذا الشهر.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var postingService = _postingService;
            foreach (var journalId in draftJournals)
            {
                await postingService.PostEntityAsync<DailyJournal>(journalId, CurrentUserId);
            }

            Dialogs.Show($"تم ترحيل شهر {month.MonthName} بالكامل وإقفاله بنجاح!", "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            await LoadDataAsync();

            if (SelectedArchivedMonth != null && SelectedArchivedMonth.Year == month.Year && SelectedArchivedMonth.Month == month.Month)
            {
                SelectedArchivedMonth = ArchivedMonths.FirstOrDefault(m => m.Year == month.Year && m.Month == month.Month);
            }
            ApplyFilters();
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"خطأ أثناء ترحيل الشهر: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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

        if (!_sessionService.HasPermission("UnpostFinancial"))
        {
            Dialogs.Show("عذراً، هذا الإجراء متاح فقط للمدير العام.", "صلاحية غير كافية", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        // 1. تحقق مما إذا كان الشهر يحتوي على ورديات تابعة لفترة تمت تسويتها وإغلاقها مسبقاً
        {
            if (await _queries.MonthHasSettledJournalsAsync(month.Year, month.Month))
            {
                Dialogs.Show(
                    "عذراً، هذا الشهر يحتوي على ورديات تابعة لفترة تم تسويتها وإقفالها مسبقاً.\nيجب إلغاء قفل فترة التسوية المعنية أولاً من شاشة (الكاش الحالي -> أرشيف التسويات).",
                    "فترة مغلقة ومسواة",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
        }

        // 2. إظهار نافذة إدخال سبب فك الترحيل
        var unlock = AppWindows.Current.AskPeriodUnlockReason();
        if (unlock == null) return;
        string selectedReason = unlock.Reason;
        string detailReason = unlock.Detail;

        var reason = $"{selectedReason} - {detailReason}";

        var result = Dialogs.Show(
            $"هل أنت متأكد من فك ترحيل شهر {month.MonthName} بالكامل؟\nسيتم فتح جميع ورديات هذا الشهر للتعديل مجدداً، وسيتم تسجيل هذا الإجراء في سجل التدقيق.",
            "تأكيد فك الترحيل",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            IsLoading = true;
            var postedJournals = await _queries.GetJournalIdsInMonthAsync(month.Year, month.Month, FinancialStatus.Posted);

            if (!postedJournals.Any())
            {
                Dialogs.Show("لا توجد ورديات مرحلة لفكها في هذا الشهر.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var postingService = _postingService;
            foreach (var journalId in postedJournals)
            {
                await postingService.UnpostEntityAsync<DailyJournal>(journalId, reason, CurrentUserId);
            }

            Dialogs.Show($"تم فك ترحيل شهر {month.MonthName} بنجاح، وأصبحت الوردية قابلة للتعديل مجدداً.", "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            await LoadDataAsync();

            if (SelectedArchivedMonth != null && SelectedArchivedMonth.Year == month.Year && SelectedArchivedMonth.Month == month.Month)
            {
                SelectedArchivedMonth = ArchivedMonths.FirstOrDefault(m => m.Year == month.Year && m.Month == month.Month);
            }
            ApplyFilters();
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"خطأ أثناء فك الترحيل: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
            Dialogs.Show("لا يمكن تعديل حركة مرحّلة مالياً. يرجى فك الترحيل أولاً.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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
            Dialogs.Show("يرجى تحديد وردية واحدة على الأقل للترحيل.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var confirmResult = Dialogs.Show(
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
            var postingService = _postingService;

            var batchResult = await postingService.PostDailyJournalsBatchAsync(selectedIds, "Admin", "ترحيل جماعي لليوميات المحددة من الواجهة");

            if (batchResult.Success)
            {
                Dialogs.Show(
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
                Dialogs.Show($"فشلت عملية الترحيل الجماعي:\n{errors}", "خطأ في الترحيل", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"حدث خطأ غير متوقع أثناء الترحيل الجماعي: {ex.Message}", "خطأ قاتل", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task PostPeriodAsync()
    {
        var range = AppWindows.Current.AskDateRange(FilterStartDate, FilterEndDate);
        if (range == null) return;
        DateTime startDate = range.Value.Start;
        DateTime endDate = range.Value.End;

        try
        {
            IsLoading = true;
            // اليوميات المفتوحة فقط في هذه الفترة
            var journalsInPeriod = await _queries.GetDraftJournalIdsInRangeAsync(startDate, endDate);

            if (!journalsInPeriod.Any())
            {
                Dialogs.Show(
                    $"لا توجد أي يوميات عمل مفتوحة (غير مرحلة) في الفترة المحددة:\nمن: {startDate:dd-MM-yyyy} إلى: {endDate:dd-MM-yyyy}",
                    "لا توجد بيانات للترحيل",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            var confirmResult = Dialogs.Show(
                $"هل أنت متأكد من ترحيل وإقفال جميع يوميات العمل المفتوحة في الفترة المحددة؟\n\n" +
                $"الفترة: من {startDate:dd-MM-yyyy} إلى {endDate:dd-MM-yyyy}\n" +
                $"📦 عدد اليوميات المفتوحة المكتشفة: {journalsInPeriod.Count} يومية عمل\n\n" +
                $"بعد الترحيل، سيتم قفل هذه العمليات محاسبياً نهائياً ولن تتمكن من تعديلها.",
                "تأكيد ترحيل وإقفال فترة زمنية",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (confirmResult != System.Windows.MessageBoxResult.Yes) return;

            var postingService = _postingService;
            var batchResult = await postingService.PostDailyJournalsBatchAsync(journalsInPeriod, CurrentUserId, $"ترحيل جماعي للفترة من {startDate:dd-MM-yyyy} إلى {endDate:dd-MM-yyyy}");

            if (batchResult.Success)
            {
                Dialogs.Show(
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
                Dialogs.Show($"فشلت عملية الترحيل الجماعي:\n{errors}", "خطأ في الترحيل", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"حدث خطأ غير متوقع أثناء الترحيل الجماعي للفترة: {ex.Message}", "خطأ قاتل", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

}

