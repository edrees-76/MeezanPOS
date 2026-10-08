using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    [ObservableProperty]
    private WorkerLedgerFilter activeTimelineFilter = WorkerLedgerFilter.All;

    [ObservableProperty]
    private System.ComponentModel.ICollectionView? workerLedgerView;

    [ObservableProperty]
    private DateTime? workerFirstTransactionDate;

    [ObservableProperty]
    private DateTime? workerLastTransactionDate;

    [ObservableProperty]
    private int workerTotalTransactionsCount;

    [ObservableProperty]
    private bool autoOpenPdfAfterExport = true;

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
    private string timelineSearchQuery = string.Empty;

    partial void OnTimelineSearchQueryChanged(string value)
    {
        WorkerLedgerView?.Refresh();
    }

    [ObservableProperty]
    private decimal totalOutstandingBalance;

    [ObservableProperty]
    private int totalWorkersCount;

    [ObservableProperty]
    private int activeWorkersCount;

    [ObservableProperty]
    private decimal totalDailyWagesCost;

    [ObservableProperty]
    private string workerSearchQuery = string.Empty;

    partial void OnWorkerSearchQueryChanged(string value)
    {
        ApplyWorkerFilter();
    }

    private List<Worker> _allWorkers = new();
    private List<WorkerWageSummary> _allSummaries = new();

    public WagesManagementViewModel()
    {
        _wagesService = AppServiceProvider.Resolve<IWagesService>();

        InitializeShiftTypes();
        SelectedAttendanceShift = ShiftTypes.First(s => s.Type == ShiftType.FullDay);
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
            _allWorkers = workers;
            
            // 2. تحميل ملخصات أرصدة العمال (التبويب الثالث)
            var summaries = await _wagesService.GetWorkerSummariesAsync();
            _allSummaries = summaries;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                TotalWorkersCount = workers.Count;
                ActiveWorkersCount = workers.Count(w => w.IsActive);
                TotalDailyWagesCost = workers.Where(w => w.IsActive).Sum(w => w.DailyWage);

                ApplyWorkerFilter();
                ApplyFilter();
                TotalOutstandingBalance = _allSummaries.Sum(s => s.Balance);
            });

            // 3. تحميل كشف الحضور لليوم المحدد (التبويب الأول)
            await LoadAttendanceForSelectedDateAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ أثناء تحميل البيانات في إدارة الأجور");
            Dialogs.Show("حدث خطأ أثناء تحميل البيانات. يرجى المحاولة مرة أخرى أو الاتصال بالدعم الفني.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task LoadedAsync()
    {
        await LoadAllDataAsync();
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
            Serilog.Log.Error(ex, "خطأ أثناء تحميل كشف الحضور لليوم المحدد");
            Dialogs.Show("تعذر تحميل كشف الحضور لليوم المحدد.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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

            Dialogs.Show("تم حفظ واعتماد حضور العمال وترحيل المستحقات المالية للأستاذ المساعد بنجاح.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ أثناء حفظ كشف حضور العمال وتثبيت الأجور");
            Dialogs.Show("حدث خطأ أثناء حفظ كشف الحضور.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            Dialogs.Show("يرجى إدخال اسم العامل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (NewWorkerDailyWage <= 0)
        {
            Dialogs.Show("يرجى تحديد أجر يومي صحيح أكبر من الصفر.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            Dialogs.Show("تم حفظ ملف العامل بنجاح.", "تم بنجاح", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ أثناء حفظ ملف العامل");
            Dialogs.Show("حدث خطأ أثناء حفظ ملف العامل.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            Serilog.Log.Error(ex, "خطأ أثناء تعديل حالة نشاط العامل");
            Dialogs.Show("حدث خطأ أثناء تعديل حالة النشاط.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task DeleteWorkerAsync(Worker worker)
    {
        if (worker == null) return;

        var result = Dialogs.Show($"هل أنت متأكد من حذف الموظف '{worker.WorkerName}' نهائياً؟ ستظل سجلاته وحركاته المالية القديمة محفوظة.", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _wagesService.DeleteWorkerAsync(worker.Id);
            await LoadAllDataAsync();
            ClearWorkerForm();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ أثناء حذف ملف العامل");
            Dialogs.Show("حدث خطأ أثناء حذف ملف العامل.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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

    [RelayCommand]
    public async Task ClearSelectedWorkerAsync()
    {
        SelectedWorkerSummary = null;
        await ReloadWorkerBalancesAsync();
    }

    [RelayCommand]
    public void ChangeTimelineFilter(WorkerLedgerFilter filter)
    {
        ActiveTimelineFilter = filter;
        WorkerLedgerView?.Refresh();
    }

    private bool FilterLedgerEntries(object item)
    {
        if (item is not WorkerLedgerEntry entry) return false;
        
        bool matchesFilter = ActiveTimelineFilter switch
        {
            WorkerLedgerFilter.All => true,
            WorkerLedgerFilter.WageAccrual => entry.Source == "استحقاق حضور",
            WorkerLedgerFilter.Payment => entry.Source == "سداد نقدي",
            WorkerLedgerFilter.Advance => entry.Source == "سلفة عمال",
            WorkerLedgerFilter.Deduction => entry.Source == "خصم وغرامة",
            WorkerLedgerFilter.Adjustment => entry.Source == "تسوية يدوية",
            _ => true
        };

        if (!matchesFilter) return false;

        if (!string.IsNullOrWhiteSpace(TimelineSearchQuery))
        {
            string query = TimelineSearchQuery.Trim();
            bool descMatch = entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
            bool notesMatch = entry.Notes != null && entry.Notes.Contains(query, StringComparison.OrdinalIgnoreCase);
            return descMatch || notesMatch;
        }

        return true;
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
                WorkerFirstTransactionDate = null;
                WorkerLastTransactionDate = null;
                WorkerTotalTransactionsCount = 0;
            });
            return;
        }

        try
        {
            var ledger = await _wagesService.GetWorkerLedgerAsync(SelectedWorkerSummary.WorkerId);
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                WorkerLedger.Clear();
                // نعرض الحركات من الأحدث إلى الأقدم في الـ Timeline
                foreach (var entry in ledger.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id))
                {
                    WorkerLedger.Add(entry);
                }

                if (WorkerLedgerView == null)
                {
                    WorkerLedgerView = System.Windows.Data.CollectionViewSource.GetDefaultView(WorkerLedger);
                    WorkerLedgerView.Filter = FilterLedgerEntries;
                }
                else
                {
                    WorkerLedgerView.Refresh();
                }

                // Single Source of Truth
                SelectedWorkerTotalAccrued = ledger.Sum(x => x.AccruedAmount);
                SelectedWorkerTotalPaid = ledger.Sum(x => x.PaidAmount);
                SelectedWorkerBalance = SelectedWorkerTotalAccrued - SelectedWorkerTotalPaid;

                WorkerFirstTransactionDate = ledger.OrderBy(x => x.Date).FirstOrDefault()?.Date;
                WorkerLastTransactionDate = ledger.OrderBy(x => x.Date).LastOrDefault()?.Date;
                WorkerTotalTransactionsCount = ledger.Count;
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ أثناء تحميل كشف حساب العامل");
            Dialogs.Show("حدث خطأ أثناء تحميل كشف حساب الموظف.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task ProcessWorkerPaymentAsync()
    {
        if (SelectedWorkerSummary == null)
        {
            Dialogs.Show("يرجى اختيار موظف أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (InputPaymentAmount <= 0)
        {
            Dialogs.Show("يرجى إدخال مبلغ صحيح أكبر من الصفر للصرف.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var scope = AppServiceProvider.Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var payTx = await db.Database.BeginOrJoinTransactionAsync();

            var paymentDay = InputPaymentDate.Date;
            if (await PeriodLock.IsDateLockedAsync(db, paymentDay))
            {
                Dialogs.Show(PeriodLock.LockedMessage, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (SelectedPaymentSource == "Cashier")
            {
                // صرف من صندوق الكاشير: على يومية مسودة بتاريخ الصرف نفسه (آخر وردية في ذلك اليوم).
                // سابقاً كانت تُضاف لآخر مسودة أياً كان تاريخها، فتظهر في يوم قديم.
                var nextDay = paymentDay.AddDays(1);
                var openJournal = await db.DailyJournals
                    .Where(j => j.FinancialStatus == FinancialStatus.Draft && j.JournalDate >= paymentDay && j.JournalDate < nextDay)
                    .OrderByDescending(j => j.ShiftType)
                    .ThenByDescending(j => j.Id)
                    .FirstOrDefaultAsync();

                if (openJournal == null)
                {
                    Dialogs.Show($"لا توجد يومية مسودة بتاريخ {paymentDay:yyyy/MM/dd} لتسجيل الصرف عليها. أنشئ يومية هذا اليوم أولاً أو اصرف كـ (مصروف عام).", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // المصروف يُنقص النقد المتوقع لليومية، فيجب أن يدخل في إجمالي مصروفاتها
                openJournal.TotalExpenses += InputPaymentAmount;
                openJournal.UpdatedAt = DateTime.UtcNow;

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
                    WorkerId = SelectedWorkerSummary.WorkerId,
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
                    WorkerId = SelectedWorkerSummary.WorkerId,
                    CreatedAt = DateTime.UtcNow
                };

                db.GeneralExpenses.Add(generalExpense);
                await db.SaveChangesAsync();

                // الصرف من نقدية المطعم يُخرج نقداً فعلياً (مثل بقية المصروفات العامة النقدية)
                var cashLedger = scope.ServiceProvider.GetRequiredService<ICashLedgerService>();
                await cashLedger.RecordMovementAsync(
                    CashMovementType.CashOut,
                    InputPaymentAmount,
                    SourceTypes.GeneralExpense,
                    generalExpense.Id,
                    $"مصروف عام: رواتب | {generalExpense.Description}",
                    paymentDay);
            }

            await db.SaveChangesAsync();
            await payTx.CommitAsync();

            // حفظ الاسم لإعادة التحديد بعد التحديث
            int currentWorkerId = SelectedWorkerSummary.WorkerId;

            // تصفير حقول الدفع
            InputPaymentAmount = 0m;
            InputPaymentNotes = string.Empty;

            await LoadAllDataAsync();

            SelectedWorkerSummary = _allSummaries.FirstOrDefault(s => s.WorkerId == currentWorkerId);

            Dialogs.Show("تم تسجيل عملية صرف المبالغ بنجاح وتحديث درج النقدية المخصص تلقائياً.", "تم بنجاح", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ أثناء تسجيل صرف مستحقات العامل");
            Dialogs.Show("حدث خطأ أثناء تسجيل عملية الصرف.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task ProcessWorkerAdvanceAsync()
    {
        // سلفة غير مرتبطة بصندوق فوري (سلفة ذمة مسجلة في كشف حساب الموظف مباشرة)
        if (SelectedWorkerSummary == null)
        {
            Dialogs.Show("يرجى اختيار موظف أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (InputPaymentAmount <= 0)
        {
            Dialogs.Show("يرجى إدخال قيمة السلفة أكبر من الصفر.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            Dialogs.Show("تم تسجيل السلفة في حساب العامل بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ أثناء تسجيل سلفة العامل");
            Dialogs.Show("حدث خطأ أثناء تسجيل السلفة.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task ProcessWorkerAdjustmentAsync()
    {
        // تسوية رصيد يدوية (إما تسوية موجبة Credit أو سالبة Debit)
        if (SelectedWorkerSummary == null)
        {
            Dialogs.Show("يرجى اختيار موظف أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (InputPaymentAmount == 0)
        {
            Dialogs.Show("قيمة التسوية لا يمكن أن تكون صفراً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            Dialogs.Show("تم حفظ تسوية الحساب يدوياً بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ أثناء حفظ التسوية اليدوية لحساب العامل");
            Dialogs.Show("حدث خطأ أثناء حفظ التسوية اليدوية.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task ReloadWorkerBalancesAsync()
    {
        try
        {
            var summaries = await _wagesService.GetWorkerSummariesAsync();
            _allSummaries = summaries;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ApplyFilter();
                TotalOutstandingBalance = _allSummaries.Sum(s => s.Balance);
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ أثناء تحديث أرصدة وملخصات العمال");
            Dialogs.Show("حدث خطأ أثناء تحديث الأرصدة.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task DeleteLedgerItemAsync(WorkerLedgerEntry entry)
    {
        if (entry == null) return;

        if (!entry.CanDelete)
        {
            Dialogs.Show("الحركات المالية الناتجة عن الحضور والغياب أو الكاشير أو المصاريف العامة لا يمكن حذفها يدوياً من هنا لضمان تطابق الصناديق واليوميات الحسابية. يرجى حذفها أو تسويتها من شاشاتها المخصصة.", "تنبيه (منع الحذف)", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var deleteReason = AppWindows.Current.AskDeleteReason();
        if (deleteReason == null) return;

        try
        {
            string reason = deleteReason.ToString();
            var session = AppServiceProvider.Resolve<ISessionService>();
            string deletedBy = session.CurrentUsername;
            int? deletedByUserId = session.CurrentUser?.Id;

            await _wagesService.DeleteTransactionAsync(entry.Id, reason, deletedBy, deletedByUserId);

            int currentWorkerId = SelectedWorkerSummary?.WorkerId ?? 0;

            await ReloadWorkerBalancesAsync();

            if (currentWorkerId > 0)
            {
                SelectedWorkerSummary = _allSummaries.FirstOrDefault(s => s.WorkerId == currentWorkerId);
            }

            Dialogs.Show("تم حذف المعاملة المالية وتوثيق العملية في سجل التدقيق بنجاح.", "تم بنجاح", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ أثناء حذف المعاملة المالية للعامل");
            Dialogs.Show("حدث خطأ أثناء حذف المعاملة.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void ExportWorkerLedgerPdf()
    {
        if (SelectedWorkerSummary == null) return;

        try
        {
            string tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MeezanPOS");
            System.IO.Directory.CreateDirectory(tempFolder);
            string fileName = $"كشف_حساب_{SelectedWorkerSummary.WorkerName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string filePath = System.IO.Path.Combine(tempFolder, fileName);

            // نمرر الحركات مرتبة تصاعدياً ليظهر الرصيد التراكمي التاريخي بشكل منطقي
            var pdfTransactions = WorkerLedger.OrderBy(x => x.Date).ThenBy(x => x.Id).ToList();

            WorkerStatementPdfReport.GeneratePdf(
                filePath,
                SelectedWorkerSummary.WorkerName,
                SelectedWorkerTotalAccrued,
                SelectedWorkerTotalPaid,
                SelectedWorkerBalance,
                pdfTransactions,
                true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ أثناء تصدير أو فتح كشف الحساب كـ PDF");
            Dialogs.Show("حدث خطأ أثناء توليد أو فتح تقرير الـ PDF.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==========================================
    // فلاتر البحث والفرز
    // ==========================================
    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    public async Task OpenWorkerStatementAsync(WorkerWageSummary summary)
    {
        if (summary == null) return;
        AppWindows.Current.ShowWorkerStatement(summary);
        await LoadAllDataAsync();
    }

    [RelayCommand]
    public async Task OpenAddWorkerDialogAsync()
    {
        if (AppWindows.Current.EditWorker(null))
        {
            await LoadAllDataAsync();
        }
    }

    [RelayCommand]
    public async Task OpenEditWorkerDialogAsync(Worker worker)
    {
        if (worker == null) return;
        if (AppWindows.Current.EditWorker(worker))
        {
            await LoadAllDataAsync();
        }
    }

    private void ApplyWorkerFilter()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            WorkersList.Clear();
            var filtered = _allWorkers;
            if (!string.IsNullOrWhiteSpace(WorkerSearchQuery))
            {
                string query = WorkerSearchQuery.Trim();
                filtered = filtered.Where(w => w.WorkerName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            foreach (var w in filtered)
            {
                WorkersList.Add(w);
            }
        });
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
