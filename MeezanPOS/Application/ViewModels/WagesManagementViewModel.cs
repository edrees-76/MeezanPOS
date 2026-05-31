using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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

    // ==========================================
    // التبويب الأول: كشف الحضور اليومي واليوميات
    // ==========================================
    [ObservableProperty]
    private DateTime selectedAttendanceDate = DateTime.Today;

    [ObservableProperty]
    private ShiftOption selectedAttendanceShift;

    [ObservableProperty]
    private ObservableCollection<WorkerAttendance> attendanceList = new();

    public ObservableCollection<ShiftOption> ShiftTypes { get; } = new();

    public System.Collections.Generic.List<AttendanceStatus> AttendanceStatuses { get; } = 
        System.Enum.GetValues(typeof(AttendanceStatus)).Cast<AttendanceStatus>().ToList();

    // ==========================================
    // التبويب الثاني: ملفات العمال وإدارة البيانات
    // ==========================================
    [ObservableProperty]
    private ObservableCollection<Worker> workersList = new();

    [ObservableProperty]
    private Worker? selectedWorkerProfile;

    [ObservableProperty]
    private string newWorkerName = string.Empty;

    [ObservableProperty]
    private decimal newWorkerDailyWage;

    [ObservableProperty]
    private string newWorkerNotes = string.Empty;

    [ObservableProperty]
    private bool isWorkerActive = true;

    // ==========================================
    // التبويب الثالث: كشف الحساب التراكمي وصرف الدفعات
    // ==========================================
    [ObservableProperty]
    private ObservableCollection<WorkerWageSummary> workerSummaries = new();

    [ObservableProperty]
    private WorkerWageSummary? selectedWorkerSummary;

    [ObservableProperty]
    private ObservableCollection<WorkerLedgerEntry> workerLedger = new();

    [ObservableProperty]
    private decimal selectedWorkerBalance;

    [ObservableProperty]
    private decimal selectedWorkerTotalAccrued;

    [ObservableProperty]
    private decimal selectedWorkerTotalPaid;

    // حقول الدفع والسلف والتسويات
    [ObservableProperty]
    private decimal inputPaymentAmount;

    [ObservableProperty]
    private DateTime inputPaymentDate = DateTime.Today;

    [ObservableProperty]
    private string inputPaymentNotes = string.Empty;

    [ObservableProperty]
    private string selectedPaymentSource = "Cashier"; // "Cashier" أو "GeneralCash"

    // حقول البحث
    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private decimal totalOutstandingBalance;

    private List<WorkerWageSummary> _allSummaries = new();

    public WagesManagementViewModel()
    {
        _wagesService = new WagesService(new AppDbContext());

        InitializeShiftTypes();
        SelectedAttendanceShift = ShiftTypes.First(s => s.Type == ShiftType.FullDay);

        _ = LoadAllDataAsync();
    }

    private void InitializeShiftTypes()
    {
        ShiftTypes.Add(new ShiftOption(ShiftType.FirstShift, "الوردية الأولى (صباحية)"));
        ShiftTypes.Add(new ShiftOption(ShiftType.SecondShift, "الوردية الثانية (مسائية)"));
        ShiftTypes.Add(new ShiftOption(ShiftType.FullDay, "يوم كامل"));
    }

    // ==========================================
    // تحميل البيانات المشترك
    // ==========================================
    [RelayCommand]
    public async Task LoadAllDataAsync()
    {
        try
        {
            // 1. تحميل قائمة ملفات العمال (التبويب الثاني)
            var workers = await _wagesService.GetAllWorkersAsync(includeInactive: true);
            
            // 2. تحميل ملخصات أرصدة العمال (التبويب الثالث)
            var summaries = await _wagesService.GetWorkerSummariesAsync();
            _allSummaries = summaries;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                WorkersList.Clear();
                foreach (var worker in workers)
                {
                    WorkersList.Add(worker);
                }

                ApplyFilter();
                TotalOutstandingBalance = _allSummaries.Sum(s => s.Balance);
            });

            // 3. تحميل كشف الحضور لليوم المحدد (التبويب الأول)
            await LoadAttendanceForSelectedDateAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء تحميل البيانات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==========================================
    // منطق التبويب الأول: الحضور واليومية
    // ==========================================
    partial void OnSelectedAttendanceDateChanged(DateTime value)
    {
        _ = LoadAttendanceForSelectedDateAsync();
    }

    partial void OnSelectedAttendanceShiftChanged(ShiftOption value)
    {
        _ = LoadAttendanceForSelectedDateAsync();
    }

    [RelayCommand]
    public async Task LoadAttendanceForSelectedDateAsync()
    {
        try
        {
            var activeWorkers = await _wagesService.GetAllWorkersAsync(includeInactive: false);
            var existingAttendances = await _wagesService.GetAttendanceForDateAsync(SelectedAttendanceDate);

            // تصفية الحضور حسب الوردية المحددة في الواجهة
            existingAttendances = existingAttendances.Where(a => a.ShiftType == SelectedAttendanceShift.Type).ToList();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AttendanceList.Clear();

                foreach (var worker in activeWorkers)
                {
                    var existing = existingAttendances.FirstOrDefault(a => a.WorkerId == worker.Id);
                    if (existing != null)
                    {
                        AttendanceList.Add(existing);
                    }
                    else
                    {
                        // إنشاء سجل حضور افتراضي غير محفوظ في الداتابيز حتى يعتمده المستخدم
                        AttendanceList.Add(new WorkerAttendance
                        {
                            WorkerId = worker.Id,
                            WorkerName = worker.WorkerName,
                            WorkDate = SelectedAttendanceDate,
                            Status = AttendanceStatus.Present,
                            ShiftType = SelectedAttendanceShift.Type,
                            SnapshotDailyWage = worker.DailyWage,
                            AccruedWage = worker.DailyWage,
                            DeductionAmount = 0m,
                            AdvanceDeducted = 0m
                        });
                    }
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ في تحميل كشف الحضور: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task SaveAttendanceBatchAsync()
    {
        if (!AttendanceList.Any()) return;

        try
        {
            await _wagesService.SaveAttendanceBatchAsync(AttendanceList.ToList());
            
            // تحديث الأرصدة والملخصات
            await LoadAllDataAsync();

            MessageBox.Show("تم حفظ واعتماد حضور العمال وترحيل المستحقات المالية للأستاذ المساعد بنجاح.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء حفظ كشف الحضور: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==========================================
    // منطق التبويب الثاني: ملفات العمال
    // ==========================================
    [RelayCommand]
    public async Task SaveWorkerProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWorkerName))
        {
            MessageBox.Show("يرجى إدخال اسم العامل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (NewWorkerDailyWage <= 0)
        {
            MessageBox.Show("يرجى تحديد أجر يومي صحيح أكبر من الصفر.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Worker worker = SelectedWorkerProfile ?? new Worker();
            worker.WorkerName = NewWorkerName.Trim();
            worker.DailyWage = NewWorkerDailyWage;
            worker.IsActive = IsWorkerActive;
            worker.Notes = NewWorkerNotes.Trim();

            await _wagesService.SaveWorkerAsync(worker);

            ClearWorkerForm();
            await LoadAllDataAsync();

            MessageBox.Show("تم حفظ ملف العامل بنجاح.", "تم بنجاح", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء حفظ ملف العامل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void SelectWorkerForEdit(Worker worker)
    {
        if (worker == null) return;
        SelectedWorkerProfile = worker;
        NewWorkerName = worker.WorkerName;
        NewWorkerDailyWage = worker.DailyWage;
        IsWorkerActive = worker.IsActive;
        NewWorkerNotes = worker.Notes ?? string.Empty;
    }

    [RelayCommand]
    public async Task ToggleWorkerActiveAsync(Worker worker)
    {
        if (worker == null) return;
        try
        {
            await _wagesService.ToggleWorkerActiveStatusAsync(worker.Id);
            await LoadAllDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ في تعديل حالة النشاط: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task DeleteWorkerAsync(Worker worker)
    {
        if (worker == null) return;

        var result = MessageBox.Show($"هل أنت متأكد من حذف الموظف '{worker.WorkerName}' نهائياً؟ ستظل سجلاته وحركاته المالية القديمة محفوظة.", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _wagesService.DeleteWorkerAsync(worker.Id);
            await LoadAllDataAsync();
            ClearWorkerForm();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء حذف ملف الموظف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void ClearWorkerForm()
    {
        SelectedWorkerProfile = null;
        NewWorkerName = string.Empty;
        NewWorkerDailyWage = 0m;
        IsWorkerActive = true;
        NewWorkerNotes = string.Empty;
    }

    // ==========================================
    // منطق التبويب الثالث: كشف الحساب والعمليات المالية
    // ==========================================
    partial void OnSelectedWorkerSummaryChanged(WorkerWageSummary? value)
    {
        _ = LoadWorkerLedgerAsync();
    }

    private async Task LoadWorkerLedgerAsync()
    {
        if (SelectedWorkerSummary == null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                WorkerLedger.Clear();
                SelectedWorkerBalance = 0;
                SelectedWorkerTotalAccrued = 0;
                SelectedWorkerTotalPaid = 0;
            });
            return;
        }

        try
        {
            var ledger = await _wagesService.GetWorkerLedgerAsync(SelectedWorkerSummary.WorkerId);
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                WorkerLedger.Clear();
                foreach (var entry in ledger)
                {
                    WorkerLedger.Add(entry);
                }

                SelectedWorkerBalance = SelectedWorkerSummary.Balance;
                SelectedWorkerTotalAccrued = SelectedWorkerSummary.TotalAccrued;
                SelectedWorkerTotalPaid = SelectedWorkerSummary.TotalPaid;
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء تحميل كشف حساب الموظف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task ProcessWorkerPaymentAsync()
    {
        if (SelectedWorkerSummary == null)
        {
            MessageBox.Show("يرجى اختيار موظف أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (InputPaymentAmount <= 0)
        {
            MessageBox.Show("يرجى إدخال مبلغ صحيح أكبر من الصفر للصرف.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var db = new AppDbContext();

            if (SelectedPaymentSource == "Cashier")
            {
                // صرف من صندوق الكاشير للوردية المفتوحة حالياً
                var openJournal = await db.DailyJournals
                    .Where(j => j.FinancialStatus == FinancialStatus.Draft)
                    .OrderByDescending(j => j.Id)
                    .FirstOrDefaultAsync();

                if (openJournal == null)
                {
                    MessageBox.Show("لا توجد وردية مفتوحة حالياً في الكاشير لتسجيل المصروف عليها. يرجى فتح وردية أولاً أو الصرف كـ (مصروف عام).", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var expenseItem = new DailyExpenseItem
                {
                    DailyJournalId = openJournal.Id,
                    SequenceNumber = db.DailyExpenseItems.Count(e => e.DailyJournalId == openJournal.Id) + 1,
                    Amount = InputPaymentAmount,
                    Category = "يومية عامل",
                    CategoryName = "يومية عامل",
                    Description = !string.IsNullOrWhiteSpace(InputPaymentNotes) ? InputPaymentNotes.Trim() : $"صرف مستحقات للعامل {SelectedWorkerSummary.WorkerName}",
                    Type = ExpenseType.WorkerWage,
                    WorkerName = SelectedWorkerSummary.WorkerName,
                    CreatedAt = DateTime.UtcNow
                };

                db.DailyExpenseItems.Add(expenseItem);
            }
            else
            {
                // صرف كـ مصروف عام من نقدية المطعم
                var generalExpense = new GeneralExpense
                {
                    ExpenseType = GeneralExpenseType.Salaries,
                    Amount = InputPaymentAmount,
                    PaymentDate = InputPaymentDate,
                    PaymentMethod = PaymentMethodType.Cash,
                    Description = !string.IsNullOrWhiteSpace(InputPaymentNotes) ? InputPaymentNotes.Trim() : $"صرف مستحقات للعامل {SelectedWorkerSummary.WorkerName}",
                    WorkerName = SelectedWorkerSummary.WorkerName,
                    CreatedAt = DateTime.UtcNow
                };

                db.GeneralExpenses.Add(generalExpense);
            }

            await db.SaveChangesAsync();

            // حفظ الاسم لإعادة التحديد بعد التحديث
            int currentWorkerId = SelectedWorkerSummary.WorkerId;

            // تصفير حقول الدفع
            InputPaymentAmount = 0m;
            InputPaymentNotes = string.Empty;

            await LoadAllDataAsync();

            SelectedWorkerSummary = _allSummaries.FirstOrDefault(s => s.WorkerId == currentWorkerId);

            MessageBox.Show("تم تسجيل عملية صرف المبالغ بنجاح وتحديث درج النقدية المخصص تلقائياً.", "تم بنجاح", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء تسجيل عملية الصرف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task ProcessWorkerAdvanceAsync()
    {
        // سلفة غير مرتبطة بصندوق فوري (سلفة ذمة مسجلة في كشف حساب الموظف مباشرة)
        if (SelectedWorkerSummary == null)
        {
            MessageBox.Show("يرجى اختيار موظف أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (InputPaymentAmount <= 0)
        {
            MessageBox.Show("يرجى إدخال قيمة السلفة أكبر من الصفر.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var tx = new WorkerTransaction
            {
                WorkerId = SelectedWorkerSummary.WorkerId,
                WorkerName = SelectedWorkerSummary.WorkerName,
                TransactionDate = InputPaymentDate,
                Type = WorkerTransactionType.Advance,
                DebitAmount = InputPaymentAmount,
                CreditAmount = 0m,
                Notes = !string.IsNullOrWhiteSpace(InputPaymentNotes) ? InputPaymentNotes.Trim() : "صرف سلفة مالية عامل"
            };

            await _wagesService.RecordTransactionAsync(tx);

            int currentWorkerId = SelectedWorkerSummary.WorkerId;
            InputPaymentAmount = 0m;
            InputPaymentNotes = string.Empty;

            await LoadAllDataAsync();
            SelectedWorkerSummary = _allSummaries.FirstOrDefault(s => s.WorkerId == currentWorkerId);

            MessageBox.Show("تم تسجيل السلفة في حساب العامل بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء تسجيل السلفة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task ProcessWorkerAdjustmentAsync()
    {
        // تسوية رصيد يدوية (إما تسوية موجبة Credit أو سالبة Debit)
        if (SelectedWorkerSummary == null)
        {
            MessageBox.Show("يرجى اختيار موظف أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (InputPaymentAmount == 0)
        {
            MessageBox.Show("قيمة التسوية لا يمكن أن تكون صفراً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            decimal credit = InputPaymentAmount > 0 ? InputPaymentAmount : 0m;
            decimal debit = InputPaymentAmount < 0 ? Math.Abs(InputPaymentAmount) : 0m;

            var tx = new WorkerTransaction
            {
                WorkerId = SelectedWorkerSummary.WorkerId,
                WorkerName = SelectedWorkerSummary.WorkerName,
                TransactionDate = InputPaymentDate,
                Type = WorkerTransactionType.Adjustment,
                CreditAmount = credit,
                DebitAmount = debit,
                Notes = !string.IsNullOrWhiteSpace(InputPaymentNotes) ? InputPaymentNotes.Trim() : "تسوية رصيد حساب يدوية"
            };

            await _wagesService.RecordTransactionAsync(tx);

            int currentWorkerId = SelectedWorkerSummary.WorkerId;
            InputPaymentAmount = 0m;
            InputPaymentNotes = string.Empty;

            await LoadAllDataAsync();
            SelectedWorkerSummary = _allSummaries.FirstOrDefault(s => s.WorkerId == currentWorkerId);

            MessageBox.Show("تم حفظ تسوية الحساب يدوياً بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء حفظ التسوية اليدوية: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task DeleteLedgerItemAsync(WorkerLedgerEntry entry)
    {
        if (entry == null) return;

        if (entry.Source != "استحقاق حضور" && entry.Source != "سلفة عمال" && entry.Source != "تسوية يدوية")
        {
            MessageBox.Show("الحركات المالية الناتجة من الكاشير أو المصاريف العامة يجب حذفها وتعديلها من شاشاتها المخصصة لضمان تطابق الصندوق.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show($"هل أنت متأكد من حذف المعاملة المالية ({entry.Source}) بتاريخ {entry.Date:yyyy/MM/dd} بقيمة {(entry.AccruedAmount > 0 ? entry.AccruedAmount : entry.PaidAmount)}؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _wagesService.DeleteTransactionAsync(entry.Id);

            int currentWorkerId = SelectedWorkerSummary?.WorkerId ?? 0;

            await LoadAllDataAsync();

            if (currentWorkerId > 0)
            {
                SelectedWorkerSummary = _allSummaries.FirstOrDefault(s => s.WorkerId == currentWorkerId);
            }

            MessageBox.Show("تم حذف المعاملة المالية وتحديث كشف الحساب التراكمي بنجاح.", "تم بنجاح", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء حذف المعاملة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==========================================
    // فلاتر البحث والفرز
    // ==========================================
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
}
