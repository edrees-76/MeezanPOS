using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Infrastructure.Data;
using System.Threading.Tasks;

namespace MeezanPOS.Application.ViewModels;

public partial class SalesViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<DailyJournal> journals = new();

    [ObservableProperty]
    private decimal totalSalesPeriod;

    [ObservableProperty]
    private decimal totalCashPeriod;

    [ObservableProperty]
    private decimal totalExpensesPeriod;

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
        TotalCashPeriod = resultList.Sum(j => j.ActualCash);
        TotalExpensesPeriod = resultList.Sum(j => j.TotalExpenses);
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
}
