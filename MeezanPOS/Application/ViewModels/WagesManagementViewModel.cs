using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace MeezanPOS.Application.ViewModels;

public partial class WagesManagementViewModel : ObservableObject
{
    private readonly IWagesService _wagesService;

    // --- أرصدة وملخصات العمال ---
    [ObservableProperty]
    private ObservableCollection<WorkerWageSummary> workerSummaries = new();

    [ObservableProperty]
    private WorkerWageSummary? selectedWorkerSummary;

    // --- كشف الحساب التفصيلي للجملة ---
    [ObservableProperty]
    private ObservableCollection<WorkerLedgerEntry> workerLedger = new();

    // --- قائمة الأسماء الفريدة للاقتراح التلقائي ---
    [ObservableProperty]
    private ObservableCollection<string> availableWorkerNames = new();

    // --- حقول نموذج تسجيل الحضور والاستحقاق ---
    [ObservableProperty]
    private string inputWorkerName = string.Empty;

    [ObservableProperty]
    private DateTime inputWorkDate = DateTime.Today;

    [ObservableProperty]
    private ShiftOption selectedShiftType;

    [ObservableProperty]
    private decimal inputAccruedWage;

    [ObservableProperty]
    private string inputNotes = string.Empty;

    // --- البحث والفلترة ---
    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private decimal totalOutstandingBalance;

    public ObservableCollection<ShiftOption> ShiftTypes { get; } = new();

    private List<WorkerWageSummary> _allSummaries = new();

    public WagesManagementViewModel()
    {
        _wagesService = new WagesService(new AppDbContext());
        
        InitializeShiftTypes();
        SelectedShiftType = ShiftTypes.First(s => s.Type == ShiftType.FullDay);
        
        _ = LoadDataAsync();
    }

    private void InitializeShiftTypes()
    {
        ShiftTypes.Add(new ShiftOption(ShiftType.FirstShift, "الوردية الأولى (صباحية)"));
        ShiftTypes.Add(new ShiftOption(ShiftType.SecondShift, "الوردية الثانية (مسائية)"));
        ShiftTypes.Add(new ShiftOption(ShiftType.FullDay, "يوم كامل"));
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        try
        {
            var summaries = await _wagesService.GetWorkerSummariesAsync();
            var names = await _wagesService.GetUniqueWorkerNamesAsync();

            _allSummaries = summaries;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ApplyFilter();
                
                AvailableWorkerNames.Clear();
                foreach (var name in names)
                {
                    AvailableWorkerNames.Add(name);
                }

                TotalOutstandingBalance = _allSummaries.Sum(s => s.Balance);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء تحميل البيانات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            WorkerSummaries = new ObservableCollection<WorkerWageSummary>(_allSummaries);
        }
        else
        {
            var filtered = _allSummaries
                .Where(s => s.WorkerName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
            WorkerSummaries = new ObservableCollection<WorkerWageSummary>(filtered);
        }
    }

    partial void OnSelectedWorkerSummaryChanged(WorkerWageSummary? value)
    {
        _ = LoadWorkerLedgerAsync();
        
        if (value != null)
        {
            InputWorkerName = value.WorkerName;
        }
    }

    private async Task LoadWorkerLedgerAsync()
    {
        if (SelectedWorkerSummary == null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => WorkerLedger.Clear());
            return;
        }

        try
        {
            var ledger = await _wagesService.GetWorkerLedgerAsync(SelectedWorkerSummary.WorkerName);
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                WorkerLedger.Clear();
                foreach (var entry in ledger)
                {
                    WorkerLedger.Add(entry);
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء تحميل كشف الحساب: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task AddAttendanceAsync()
    {
        if (string.IsNullOrWhiteSpace(InputWorkerName))
        {
            MessageBox.Show("يرجى إدخال اسم العامل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (InputAccruedWage <= 0)
        {
            MessageBox.Show("يرجى إدخال قيمة أجر صالحة أكبر من الصفر.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var attendance = new WorkerAttendance
            {
                WorkerName = InputWorkerName.Trim(),
                WorkDate = InputWorkDate,
                ShiftType = SelectedShiftType.Type,
                AccruedWage = InputAccruedWage,
                Notes = InputNotes?.Trim()
            };

            await _wagesService.RecordAttendanceAsync(attendance);

            // تذكر اسم الموظف الحالي لإعادة اختياره بعد التحديث لتحديث كشف حسابه
            string currentWorkerName = InputWorkerName.Trim();

            // مسح وتصفير حقول النموذج
            ClearForm();

            // تحديث البيانات
            await LoadDataAsync();

            // إعادة اختيار الموظف لرؤية التغييرات الفورية في كشف حسابه
            var updatedSummary = _allSummaries.FirstOrDefault(s => string.Equals(s.WorkerName, currentWorkerName, StringComparison.OrdinalIgnoreCase));
            if (updatedSummary != null)
            {
                SelectedWorkerSummary = updatedSummary;
            }

            MessageBox.Show("تم تسجيل الحضور والاستحقاق بنجاح.", "تمت العملية", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء حفظ الحضور: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task DeleteLedgerItemAsync(WorkerLedgerEntry entry)
    {
        if (entry == null) return;

        if (entry.Source != "حضور")
        {
            MessageBox.Show("يمكنك فقط حذف حركات الحضور من هنا. المدفوعات والمصاريف يجب إدارتها وحذفها من شاشات الصندوق والمصاريف العامة.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"هل أنت متأكد من حذف حركة الحضور هذه بتاريخ {entry.Date:yyyy/MM/dd} بمبلغ {entry.AccruedAmount:N2}؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _wagesService.DeleteAttendanceAsync(entry.Id);

            string currentWorkerName = SelectedWorkerSummary?.WorkerName ?? string.Empty;

            await LoadDataAsync();

            if (!string.IsNullOrEmpty(currentWorkerName))
            {
                var updatedSummary = _allSummaries.FirstOrDefault(s => string.Equals(s.WorkerName, currentWorkerName, StringComparison.OrdinalIgnoreCase));
                SelectedWorkerSummary = updatedSummary;
            }

            MessageBox.Show("تم حذف حركة الحضور بنجاح.", "تمت العملية", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء حذف حركة الحضور: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void ClearForm()
    {
        InputWorkerName = string.Empty;
        InputWorkDate = DateTime.Today;
        SelectedShiftType = ShiftTypes.First(s => s.Type == ShiftType.FullDay);
        InputAccruedWage = 0;
        InputNotes = string.Empty;
    }
}
