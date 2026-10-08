using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using QuestPDF.Fluent;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Services;
using MeezanPOS.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace MeezanPOS.Application.ViewModels;
public partial class DailyJournalViewModel : ObservableObject
{
    private readonly LoadFailureReporter _loadErrors = new();
    private readonly IWagesService _wagesService;
    private readonly IBankService _bankService;
    private readonly ILedgerService _ledgerService;
    private readonly IDailyJournalService _journalService;
    private readonly MeezanPOS.Application.Services.Queries.ILookupService _lookups;

    public DailyJournalViewModel() : this(
        AppServiceProvider.Resolve<IWagesService>(),
        AppServiceProvider.Resolve<IBankService>(),
        AppServiceProvider.Resolve<ILedgerService>(),
        AppServiceProvider.Provider.GetRequiredService<IServiceScopeFactory>())
    {
    }

    /// <param name="scopeFactory">
    /// يُنشئ نطاقاً لكل حفظ تتشارك فيه الخدمات سياقاً ومعاملة واحدة. بدونه (في الاختبارات) تُستخدم الخدمات الممررة.
    /// </param>
    public DailyJournalViewModel(
        IWagesService wagesService,
        IBankService bankService,
        ILedgerService ledgerService,
        IServiceScopeFactory? scopeFactory = null)
    {
        _wagesService = wagesService ?? throw new ArgumentNullException(nameof(wagesService));
        _bankService = bankService ?? throw new ArgumentNullException(nameof(bankService));
        _ledgerService = ledgerService ?? throw new ArgumentNullException(nameof(ledgerService));
        _journalService = new DailyJournalService(DefaultDbContextFactory.Instance, scopeFactory, bankService, ledgerService);
        _lookups = MeezanPOS.Application.Services.Queries.LookupService.Default;

        _ = LoadSuppliersAsync();
        _ = LoadCustomExpenseTypesAsync();
        _ = LoadWorkerNamesAsync();
        _ = LoadBankAccountsAsync();
        AddExpenseItem();
        AddBankingItem();
        _ = UpdateAvailableShiftsAsync(JournalDate);
        MarkClean();
    }

    // --- معلومات الوردية ---
    [ObservableProperty]
    private DateTime journalDate = DateTime.Today;

    [ObservableProperty]
    private ShiftType selectedShiftType = ShiftType.FirstShift;

    private CancellationTokenSource? _shiftCts;

    partial void OnJournalDateChanged(DateTime value)
    {
        _shiftCts?.Cancel();
        _shiftCts = new CancellationTokenSource();
        _ = UpdateAvailableShiftsAsync(value, _shiftCts.Token);
    }

    /// <summary>
    /// تحويل نوع الوردية إلى اسم عرض موحّد
    /// </summary>
    public static string GetShiftDisplayName(ShiftType type) => type switch
    {
        ShiftType.FirstShift  => "الوردية الأولى",
        ShiftType.SecondShift => "الوردية الثانية",
        ShiftType.FullDay     => "يوم كامل",
        _ => ""
    };

    [ObservableProperty]
    private string employeeName = string.Empty;

    [ObservableProperty]
    private string notes = string.Empty;

    // --- المبالغ الأساسية (nullable لتظهر فارغة) ---
    [ObservableProperty]
    private decimal? cashFloat;  // مبلغ الصرف (الفكة)

    [ObservableProperty]
    private decimal? cashSalesInput;  // مبيعات نقدية (من الكاشير)

    [ObservableProperty]
    private decimal? bankingSalesInput;  // خدمات مصرفية (من الكاشير) - اختياري

    // --- الموردين (للربط بالمصروفات) ---
    public ObservableCollection<MeezanPOS.Domain.Entities.Supplier> Suppliers { get; } = new();

    // --- المرتجعات والطلبات المجانية ---
    public ObservableCollection<OrderAdjustmentItemViewModel> Returns { get; } = new();
    public ObservableCollection<OrderAdjustmentItemViewModel> FreeOrders { get; } = new();

    // --- تقسيم مبيعات المصارف ---
    public ObservableCollection<BankAccount> ActiveBankAccounts { get; } = new();
    public ObservableCollection<BankSaleInputViewModel> BankSalesInputs { get; } = new();

    public decimal ReturnsAmount => Returns.Sum(r => r.Amount ?? 0);
    public decimal FreeOrdersAmount => FreeOrders.Sum(f => f.Amount ?? 0);

    // --- المطابقة ---
    [ObservableProperty]
    private decimal? actualCash;  // النقد الفعلي المستلم

    // --- وضع المطابقة ---
    public bool HasBankingSalesInput => (BankingSalesInput ?? 0) > 0;

    // --- الحسابات التلقائية ---
    public decimal BankingItemsTotal => BankingItems.Sum(b => b.Amount ?? 0);
    public decimal EffectiveBankingTotal => HasBankingSalesInput ? (BankingSalesInput ?? 0) : BankingItemsTotal;
    public decimal TotalSales => (CashSalesInput ?? 0) + EffectiveBankingTotal;
    public decimal NetSales => TotalSales - ReturnsAmount - FreeOrdersAmount;
    public decimal BankingDifference => (BankingSalesInput ?? 0) - BankingItemsTotal;
    public decimal TotalExpenses => ExpenseItems.Sum(e => e.Amount ?? 0);
    public decimal PurchasesTotal => ExpenseItems.Where(e => e.ExpenseType == "مشتريات").Sum(e => e.Amount ?? 0);
    public decimal WorkerWagesTotal => ExpenseItems.Where(e => e.ExpenseType == "أجرة عامل" || e.ExpenseType == "يومية عامل").Sum(e => e.Amount ?? 0);
    public decimal SupplierPaymentsTotal => ExpenseItems.Where(e => e.ExpenseType == "دفعة مورد").Sum(e => e.Amount ?? 0);
    public decimal InvoicePaymentsTotal => ExpenseItems.Where(e => e.ExpenseType == "دفعة فاتورة").Sum(e => e.Amount ?? 0);
    public decimal GasTotal => ExpenseItems.Where(e => e.ExpenseType == "غاز").Sum(e => e.Amount ?? 0);
    public decimal CoalTotal => ExpenseItems.Where(e => e.ExpenseType == "فحم").Sum(e => e.Amount ?? 0);
    public decimal BreadTotal => ExpenseItems.Where(e => e.ExpenseType == "الخبزة").Sum(e => e.Amount ?? 0);
    public decimal CleaningTotal => ExpenseItems.Where(e => e.ExpenseType == "نظافة").Sum(e => e.Amount ?? 0);
    public decimal MaintenanceTotal => ExpenseItems.Where(e => e.ExpenseType == "صيانة").Sum(e => e.Amount ?? 0);
    public decimal TransportTotal => ExpenseItems.Where(e => e.ExpenseType == "مواصلات").Sum(e => e.Amount ?? 0);
    public decimal PettyCashTotal => ExpenseItems.Where(e => e.ExpenseType == "مصروف نثري").Sum(e => e.Amount ?? 0);

    public bool HasPurchases => PurchasesTotal > 0;
    public bool HasWorkerWages => WorkerWagesTotal > 0;
    public bool HasSupplierPayments => SupplierPaymentsTotal > 0;
    public bool HasInvoicePayments => InvoicePaymentsTotal > 0;
    public bool HasGas => GasTotal > 0;
    public bool HasCoal => CoalTotal > 0;
    public bool HasBread => BreadTotal > 0;
    public bool HasCleaning => CleaningTotal > 0;
    public bool HasMaintenance => MaintenanceTotal > 0;
    public bool HasTransport => TransportTotal > 0;
    public bool HasPettyCash => PettyCashTotal > 0;

    public decimal ExpectedCash => (CashFloat ?? 0) + (CashSalesInput ?? 0) - TotalExpenses - ReturnsAmount - FreeOrdersAmount;
    public decimal Difference => (ActualCash ?? 0) - ExpectedCash;

    public string BankingDifferenceStatus
    {
        get
        {
            if (BankingDifference == 0) return "مطابق ✓";
            if (BankingDifference > 0) return $"فرق: {BankingDifference:N2} (الكاشير أكثر)";
            return $"فرق: {Math.Abs(BankingDifference):N2} (الدفتر أكثر)";
        }
    }

    public string BankingDifferenceColor
    {
        get
        {
            if (BankingDifference == 0) return "#10b981";
            if (BankingDifference > 0) return "#000000";
            return "#ef4444";
        }
    }

    public string BankingSectionTitle => HasBankingSalesInput
        ? "تفاصيل الخدمات المصرفية (مطابقة مع الكاشير)"
        : "تفاصيل الخدمات المصرفية (من الدفتر)";

    public string DifferenceStatus
    {
        get
        {
            if (Difference == 0) return "مطابق ✓";
            if (Difference > 0) return $"زيادة: {Difference:N2}";
            return $"عجز: {Math.Abs(Difference):N2}";
        }
    }

    public string DifferenceColor
    {
        get
        {
            if (Difference == 0) return "#10b981";
            if (Difference > 0) return "#000000";
            return "#ef4444";
        }
    }

    // --- القوائم ---
    public ObservableCollection<ExpenseItemViewModel> ExpenseItems { get; } = new();
    public ObservableCollection<string> AvailableWorkerNames { get; } = new();

    private async System.Threading.Tasks.Task LoadWorkerNamesAsync()
    {
        try
        {
            var service = _wagesService;
            var names = await service.GetUniqueWorkerNamesAsync();
            UiThread.Run(() =>
            {
                AvailableWorkerNames.Clear();
                foreach (var name in names)
                {
                    AvailableWorkerNames.Add(name);
                }
            });
        }
        catch (Exception ex)
        {
            _loadErrors.Report(ex, "أسماء العمال");
        }
    }
    public ObservableCollection<BankingItemViewModel> BankingItems { get; } = new();

    // --- أنواع الورديات ---
    public ObservableCollection<ShiftOption> ShiftTypes { get; } = new();

    // --- قائمة المصارف الليبية ---
    public string[] BankNames { get; } = {
        "مصرف الجمهورية",
        "المصرف التجاري الوطني",
        "مصرف الوحدة",
        "مصرف الصحارى",
        "مصرف التجارة والتنمية",
        "مصرف الأمان",
        "مصرف الأندلس",
        "مصرف اليقين",
        "المصرف الإسلامي الليبي",
        "مصرف الاتحاد الوطني",
        "مصرف الواحة",
        "مصرف النوران",
        "مصرف السراي للتجارة والاستثمار"
    };

    // --- أنواع المصروفات ---
    public System.Collections.ObjectModel.ObservableCollection<string> ExpenseTypes { get; } = new(new[] {
        "مشتريات",
        "الخبزة",
        "يومية عامل",
        "دفعة مورد",
        "دفعة فاتورة",
        "غاز",
        "فحم",
        "نظافة",
        "صيانة",
        "مواصلات",
        "مصروف نثري"
    });

    // --- رسائل ---
    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isSaved;



    private async System.Threading.Tasks.Task LoadBankAccountsAsync()
    {
        try
        {
            var list = await _lookups.GetActiveBankAccountsAsync();
            UiThread.Run(() =>
            {
                ActiveBankAccounts.Clear();
                BankSalesInputs.Clear();
                foreach (var bank in list)
                {
                    ActiveBankAccounts.Add(bank);
                    var input = new BankSaleInputViewModel
                    {
                        BankAccountId = bank.Id,
                        BankFriendlyName = bank.DisplayName,
                        BankName = bank.BankName ?? bank.FriendlyName,
                        Amount = null
                    };
                    input.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(BankSaleInputViewModel.Amount))
                        {
                            UpdateBankingSalesInput();
                        }
                    };
                    BankSalesInputs.Add(input);
                }
            });
        }
        catch (Exception ex)
        {
            _loadErrors.Report(ex, "الحسابات المصرفية");
        }
    }

    private void UpdateBankingSalesInput()
    {
        var hasAnyPerBankInput = BankSalesInputs.Any(b => b.Amount.HasValue && b.Amount.Value > 0);
        if (hasAnyPerBankInput)
        {
            BankingSalesInput = BankSalesInputs.Sum(b => b.Amount ?? 0m);
        }
    }

    public DailyJournalViewModel(int journalId) : this(
        journalId,
        AppServiceProvider.Resolve<IWagesService>(),
        AppServiceProvider.Resolve<IBankService>(),
        AppServiceProvider.Resolve<ILedgerService>(),
        AppServiceProvider.Provider.GetRequiredService<IServiceScopeFactory>())
    {
    }

    // تعديل يومية قائمة: يجب أن يصل مصنع النطاقات أيضاً، وإلا يُحفظ التعديل خارج المعاملة الواحدة
    public DailyJournalViewModel(
        int journalId,
        IWagesService wagesService,
        IBankService bankService,
        ILedgerService ledgerService,
        IServiceScopeFactory? scopeFactory = null) : this(wagesService, bankService, ledgerService, scopeFactory)
    {
        // استخدام ديسباتشر لتنفيذ التحميل في الخلفية أو بعد التهيئة
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                // تحميل جميع الحسابات البنكية النشطة أولاً لبناء الحقول
                var activeBanks = await _lookups.GetActiveBankAccountsAsync();
                var journal = await _journalService.GetJournalWithDetailsAsync(journalId);

                if (journal != null)
                {
                    UiThread.Run(() =>
                    {
                        ActiveBankAccounts.Clear();
                        BankSalesInputs.Clear();
                        foreach (var bank in activeBanks)
                        {
                            ActiveBankAccounts.Add(bank);
                            var savedSale = journal.BankSales?.FirstOrDefault(s => s.BankAccountId == bank.Id);
                            var input = new BankSaleInputViewModel
                            {
                                BankAccountId = bank.Id,
                                BankFriendlyName = bank.DisplayName,
                                BankName = bank.BankName ?? bank.FriendlyName,
                                Amount = savedSale?.Amount > 0 ? savedSale.Amount : null
                            };
                            input.PropertyChanged += (s, e) =>
                            {
                                if (e.PropertyName == nameof(BankSaleInputViewModel.Amount))
                                {
                                    UpdateBankingSalesInput();
                                }
                            };
                            BankSalesInputs.Add(input);
                        }
                    });

                    if (journal.FinancialStatus == MeezanPOS.Domain.Enums.FinancialStatus.Posted || journal.FinancialStatus == MeezanPOS.Domain.Enums.FinancialStatus.Archived)
                    {
                        LoadJournalForViewing(journal);
                    }
                    else
                    {
                        LoadJournalForEditing(journal);
                    }

                    MarkClean();
                }
            }
            catch (Exception ex)
            {
                // كان الخطأ يُكتب في نافذة التصحيح فقط فتظهر اليومية فارغة، وقد يحفظها المستخدم كأنها جديدة
                Serilog.Log.Error(ex, "خطأ في تحميل الوردية {JournalId} للتعديل أو العرض", journalId);
                Dialogs.Show($"تعذر تحميل بيانات اليومية:\n{ex.Message}\n\nلا تحفظ هذه الشاشة؛ أغلقها وأعد فتح اليومية.",
                    "خطأ في التحميل", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        });
    }

    private async System.Threading.Tasks.Task LoadSuppliersAsync()
    {
        try
        {
            var list = await _lookups.GetActiveSuppliersAsync();
            UiThread.Run(() =>
            {
                foreach (var supplier in list)
                    Suppliers.Add(supplier);
            });
        }
        catch (Exception ex) { _loadErrors.Report(ex, "قائمة الموردين"); }
    }

    private async System.Threading.Tasks.Task LoadCustomExpenseTypesAsync()
    {
        try
        {
            var customTypes = await _journalService.GetUsedExpenseCategoriesAsync();

            UiThread.Run(() =>
            {
                foreach (var t in customTypes)
                {
                    if (t != null && !ExpenseTypes.Contains(t))
                    {
                        ExpenseTypes.Add(t);
                    }
                }
            });
        }
        catch (Exception ex) { _loadErrors.Report(ex, "أنواع المصروفات المضافة"); }
    }

    // --- إعادة حساب تلقائية عند تغيير أي قيمة ---
    partial void OnCashSalesInputChanged(decimal? value) => RefreshCalculations();
    partial void OnBankingSalesInputChanged(decimal? value) => RefreshCalculations();
    partial void OnCashFloatChanged(decimal? value) => RefreshCalculations();
    partial void OnActualCashChanged(decimal? value) => RefreshCalculations();
    private void RefreshCalculations()
    {
        OnPropertyChanged(nameof(HasBankingSalesInput));
        OnPropertyChanged(nameof(BankingItemsTotal));
        OnPropertyChanged(nameof(EffectiveBankingTotal));
        OnPropertyChanged(nameof(TotalSales));
        OnPropertyChanged(nameof(NetSales));
        OnPropertyChanged(nameof(ReturnsAmount));
        OnPropertyChanged(nameof(FreeOrdersAmount));
        OnPropertyChanged(nameof(BankingDifference));
        OnPropertyChanged(nameof(BankingDifferenceStatus));
        OnPropertyChanged(nameof(BankingSectionTitle));
        OnPropertyChanged(nameof(TotalExpenses));
        OnPropertyChanged(nameof(PurchasesTotal));
        OnPropertyChanged(nameof(HasPurchases));
        OnPropertyChanged(nameof(WorkerWagesTotal));
        OnPropertyChanged(nameof(HasWorkerWages));
        OnPropertyChanged(nameof(SupplierPaymentsTotal));
        OnPropertyChanged(nameof(HasSupplierPayments));
        OnPropertyChanged(nameof(InvoicePaymentsTotal));
        OnPropertyChanged(nameof(HasInvoicePayments));
        OnPropertyChanged(nameof(GasTotal));
        OnPropertyChanged(nameof(HasGas));
        OnPropertyChanged(nameof(CleaningTotal));
        OnPropertyChanged(nameof(HasCleaning));
        OnPropertyChanged(nameof(MaintenanceTotal));
        OnPropertyChanged(nameof(HasMaintenance));
        OnPropertyChanged(nameof(TransportTotal));
        OnPropertyChanged(nameof(HasTransport));
        OnPropertyChanged(nameof(PettyCashTotal));
        OnPropertyChanged(nameof(HasPettyCash));
        OnPropertyChanged(nameof(ExpectedCash));
        OnPropertyChanged(nameof(Difference));
        OnPropertyChanged(nameof(DifferenceStatus));
        OnPropertyChanged(nameof(DifferenceColor));
        OnPropertyChanged(nameof(BankingDifferenceColor));
    }

    // --- أوامر المصروفات ---
    [RelayCommand]
    private void AddExpenseItem()
    {
        var item = new ExpenseItemViewModel
        {
            SequenceNumber = ExpenseItems.Count + 1,
            ExpenseType = "مشتريات" // تعيين قيمة افتراضية
        };
        item.PropertyChanged += (s, e) => RefreshCalculations();
        ExpenseItems.Add(item);
    }

    [RelayCommand]
    private void RemoveExpenseItem(ExpenseItemViewModel item)
    {
        ExpenseItems.Remove(item);
        RenumberExpenseItems();
        RefreshCalculations();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task OpenWorkerWagesDialogAsync(ExpenseItemViewModel item)
    {
        if (item == null) return;
        var result = await AppWindows.Current.EditWorkerWagesAsync(item.SelectedWorkerWagesDetails);
        if (result != null)
        {
            item.SelectedWorkerWagesDetails = result.Details;
            item.Amount = result.TotalPaid;
            RefreshCalculations();
        }
    }

    private void RenumberExpenseItems()
    {
        for (int i = 0; i < ExpenseItems.Count; i++)
        {
            ExpenseItems[i].SequenceNumber = i + 1;
        }
    }
    // --- أوامر المرتجعات والطلبات المجانية ---
    [RelayCommand]
    private void AddReturnItem()
    {
        var item = new OrderAdjustmentItemViewModel { SequenceNumber = Returns.Count + 1 };
        item.PropertyChanged += (s, e) => RefreshCalculations();
        Returns.Add(item);
    }

    [RelayCommand]
    private void RemoveReturnItem(OrderAdjustmentItemViewModel item)
    {
        Returns.Remove(item);
        for (int i = 0; i < Returns.Count; i++) Returns[i].SequenceNumber = i + 1;
        RefreshCalculations();
    }

    [RelayCommand]
    private void AddFreeOrderItem()
    {
        var item = new OrderAdjustmentItemViewModel { SequenceNumber = FreeOrders.Count + 1 };
        item.PropertyChanged += (s, e) => RefreshCalculations();
        FreeOrders.Add(item);
    }

    [RelayCommand]
    private void RemoveFreeOrderItem(OrderAdjustmentItemViewModel item)
    {
        FreeOrders.Remove(item);
        for (int i = 0; i < FreeOrders.Count; i++) FreeOrders[i].SequenceNumber = i + 1;
        RefreshCalculations();
    }

    // --- أوامر الخدمات المصرفية ---
    [RelayCommand]
    private void AddBankingItem()
    {
        var item = new BankingItemViewModel
        {
            SequenceNumber = BankingItems.Count + 1
        };
        item.PropertyChanged += (s, e) =>
        {
            // تحديث اسم المصرف تلقائياً عند اختيار مصرف من القائمة المنسدلة
            if (e.PropertyName == nameof(BankingItemViewModel.BankAccountId) && s is BankingItemViewModel bankItem && bankItem.BankAccountId.HasValue)
            {
                var bank = ActiveBankAccounts.FirstOrDefault(b => b.Id == bankItem.BankAccountId.Value);
                if (bank != null)
                {
                    bankItem.BankName = bank.DisplayName;
                }
            }
            RefreshCalculations();
        };
        BankingItems.Add(item);
    }

    [RelayCommand]
    private void RemoveBankingItem(BankingItemViewModel item)
    {
        BankingItems.Remove(item);
        RenumberBankingItems();
        RefreshCalculations();
    }

    private void RenumberBankingItems()
    {
        for (int i = 0; i < BankingItems.Count; i++)
        {
            BankingItems[i].SequenceNumber = i + 1;
        }
    }

    private int? editingJournalId;
    private ShiftType? editingJournalShift;

    private async System.Threading.Tasks.Task UpdateAvailableShiftsAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        try
        {
            var registeredShifts = await _journalService.GetRegisteredShiftsAsync(date, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (editingJournalId.HasValue && editingJournalShift.HasValue)
            {
                registeredShifts.Remove(editingJournalShift.Value);
            }

            var allShifts = new[] { ShiftType.FirstShift, ShiftType.SecondShift, ShiftType.FullDay };
            var availableShifts = allShifts
                .Where(s => !registeredShifts.Contains(s))
                .Select(s => new ShiftOption(s, GetShiftDisplayName(s)))
                .ToList();

            UiThread.Run(() =>
            {
                ShiftTypes.Clear();
                foreach (var shift in availableShifts)
                {
                    ShiftTypes.Add(shift);
                }

                if (!availableShifts.Any(s => s.Type == SelectedShiftType) && availableShifts.Any())
                {
                    SelectedShiftType = availableShifts.First().Type;
                }
            });
        }
        catch (OperationCanceledException)
        {
            // تم إلغاء العملية بسبب تغيير التاريخ — سلوك طبيعي
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Warning(ex, "خطأ في تحديث الورديات المتاحة؛ تُعرض كل الورديات");
            // احتياطي: عرض جميع الورديات عند فشل الاستعلام
            UiThread.Run(() =>
            {
                if (ShiftTypes.Count == 0)
                {
                    ShiftTypes.Add(new ShiftOption(ShiftType.FirstShift, GetShiftDisplayName(ShiftType.FirstShift)));
                    ShiftTypes.Add(new ShiftOption(ShiftType.SecondShift, GetShiftDisplayName(ShiftType.SecondShift)));
                    ShiftTypes.Add(new ShiftOption(ShiftType.FullDay, GetShiftDisplayName(ShiftType.FullDay)));
                }
            });
        }
    }

    // --- حفظ الحركة اليومية ---
    [RelayCommand]
    private async System.Threading.Tasks.Task SaveJournalAsync()
    {
        if (string.IsNullOrWhiteSpace(EmployeeName))
        {
            StatusMessage = "الرجاء إدخال اسم الموظف.";
            return;
        }
        if (TotalSales <= 0)
        {
            StatusMessage = "الرجاء إدخال المبيعات.";
            return;
        }

        foreach (var exp in ExpenseItems)
        {
            if (exp.IsWorkerWage && exp.IsDetailedWage && (exp.SelectedWorkerWagesDetails == null || !exp.SelectedWorkerWagesDetails.Any()))
            {
                StatusMessage = $"يرجى تحديد تفاصيل أجور حضور العمال للمصروف رقم {exp.SequenceNumber}.";
                Dialogs.Show($"يرجى تحديد تفاصيل أجور حضور العمال للمصروف رقم {exp.SequenceNumber}.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
        }

        try
        {
            var result = await _journalService.SaveAsync(BuildSaveRequest());
            if (!result.Success)
            {
                StatusMessage = result.Error ?? "تعذر الحفظ.";
                return;
            }

            StatusMessage = "تم حفظ الحركة اليومية بنجاح ✓";
            IsSaved = true;

            // إغلاق النافذة والعودة بعد الحفظ
            CloseForm();
        }
        catch (System.Exception ex)
        {
            StatusMessage = "حدث خطأ أثناء الحفظ: " + (ex.InnerException?.Message ?? ex.Message);
        }
    }

    /// <summary>
    /// تحويل حقول الشاشة إلى بيانات حفظ: البنود ذات المبلغ الموجب فقط، وحركات العمال التفصيلية لكل بند أجور.
    /// </summary>
    private JournalSaveRequest BuildSaveRequest()
    {
        var request = new JournalSaveRequest
        {
            EditingJournalId = editingJournalId,
            JournalDate = JournalDate,
            Shift = SelectedShiftType,
            ShiftDisplayName = GetShiftDisplayName(SelectedShiftType),
            EmployeeName = EmployeeName,
            Notes = Notes,
            CashFloat = CashFloat ?? 0m,
            TotalSales = TotalSales,
            BankingTotal = EffectiveBankingTotal,
            TotalExpenses = TotalExpenses,
            ReturnsTotal = ReturnsAmount,
            FreeOrdersTotal = FreeOrdersAmount,
            ActualCash = ActualCash ?? 0m,
        };

        // المصروفات
        foreach (var exp in ExpenseItems.Where(e => (e.Amount ?? 0m) > 0))
        {
            string desc = exp.ExpenseType;
            ExpenseType dbExpenseType;
            int? dbSupplierId = null;

            if (exp.IsPurchase || exp.IsInvoicePayment || exp.IsSupplierPayment || exp.IsGas || exp.IsCoal || exp.IsBread)
            {
                if (exp.IsPurchase) dbExpenseType = ExpenseType.Purchase;
                else if (exp.IsInvoicePayment) dbExpenseType = ExpenseType.InvoicePayment;
                else if (exp.IsSupplierPayment) dbExpenseType = ExpenseType.SupplierPayment;
                else if (exp.IsGas) dbExpenseType = ExpenseType.Gas;
                else if (exp.IsCoal) dbExpenseType = ExpenseType.Coal;
                else dbExpenseType = ExpenseType.Bread;

                dbSupplierId = exp.SelectedSupplierId;

                var parts = new[] { exp.SupplierName, exp.InvoiceNumber, exp.Notes }
                    .Where(part => !string.IsNullOrWhiteSpace(part));
                if (parts.Any()) desc = string.Join(" - ", parts);
            }
            else if (exp.IsWorkerWage)
            {
                dbExpenseType = ExpenseType.WorkerWage;
                if (!string.IsNullOrWhiteSpace(exp.Description)) desc = exp.Description;
            }
            else
            {
                // الأنواع البسيطة: نظافة، صيانة، مواصلات، مصروف نثري
                dbExpenseType = exp.ExpenseType switch
                {
                    "نظافة" => ExpenseType.Cleaning,
                    "صيانة" => ExpenseType.Maintenance,
                    "مواصلات" => ExpenseType.Transport,
                    "مصروف نثري" => ExpenseType.PettyCash,
                    _ => ExpenseType.Regular
                };
                if (!string.IsNullOrWhiteSpace(exp.Description)) desc = exp.Description;
            }

            request.ExpenseItems.Add(new MeezanPOS.Domain.Entities.DailyExpenseItem
            {
                SequenceNumber = exp.SequenceNumber,
                Amount = exp.Amount ?? 0m,
                Category = exp.ExpenseType,
                CategoryName = exp.ExpenseType,
                Description = desc,
                Type = dbExpenseType,
                SupplierId = dbSupplierId,
                SupplierName = exp.SupplierName,
                Notes = exp.Notes,
                InvoiceNumber = exp.InvoiceNumber,
                WorkerName = exp.IsWorkerWage ? (exp.IsDetailedWage ? "[متعدد]" : (!string.IsNullOrEmpty(exp.WorkerName) ? exp.WorkerName : null)) : null,
                WorkerId = (exp.IsWorkerWage && !exp.IsDetailedWage) ? exp.WorkerId : null,
                CreatedAt = System.DateTime.UtcNow
            });

            if (exp.IsWorkerWage && exp.IsDetailedWage && exp.SelectedWorkerWagesDetails != null && exp.SelectedWorkerWagesDetails.Any())
                request.WorkerTransactionsBySequence[exp.SequenceNumber] = BuildWorkerTransactions(exp.SelectedWorkerWagesDetails);
        }

        // الخدمات المصرفية
        foreach (var bank in BankingItems.Where(b => (b.Amount ?? 0m) > 0))
        {
            request.BankingItems.Add(new MeezanPOS.Domain.Entities.BankingItem
            {
                Amount = bank.Amount ?? 0m,
                Description = bank.InvoiceNumber?.Trim(),
                BankAccountId = bank.BankAccountId,
                ReferenceNumber = bank.Last4Digits?.Trim(),
                CreatedAt = System.DateTime.UtcNow
            });
        }

        // تقسيم مبيعات الخدمات المصرفية
        foreach (var bSale in BankSalesInputs.Where(b => (b.Amount ?? 0m) > 0))
        {
            request.BankSales.Add(new DailyJournalBankSale
            {
                BankAccountId = bSale.BankAccountId,
                BankName = bSale.BankFriendlyName,
                Amount = bSale.Amount ?? 0m,
                CreatedAt = System.DateTime.UtcNow
            });
        }

        // المرتجعات ثم المجاني
        foreach (var ret in Returns.Where(x => (x.Amount ?? 0m) > 0))
            request.Adjustments.Add(NewAdjustment(false, ret.Amount ?? 0m, ret.InvoiceNumber, ret.Notes, ret.PersonName));
        foreach (var free in FreeOrders.Where(x => (x.Amount ?? 0m) > 0))
            request.Adjustments.Add(NewAdjustment(true, free.Amount ?? 0m, free.InvoiceNumber, free.Notes, free.PersonName));

        return request;
    }

    private static MeezanPOS.Domain.Entities.OrderAdjustmentItem NewAdjustment(bool isFree, decimal amount, string? invoice, string? notes, string? person) => new()
    {
        IsFreeOrder = isFree,
        Amount = amount,
        InvoiceNumber = invoice,
        Notes = notes,
        PersonName = person?.Trim(),
        CreatedAt = System.DateTime.UtcNow
    };

    /// <summary>استحقاق وسداد وسلفة وخصم لكل عامل في بند الأجور التفصيلي.</summary>
    private System.Collections.Generic.List<WorkerTransaction> BuildWorkerTransactions(System.Collections.Generic.IEnumerable<WorkerTransactionDetailDto> details)
    {
        var list = new System.Collections.Generic.List<WorkerTransaction>();
        WorkerTransaction Tx(WorkerTransactionDetailDto d, WorkerTransactionType type, decimal debit, decimal credit, string notes) => new()
        {
            WorkerId = d.WorkerId,
            WorkerName = d.WorkerName,
            TransactionDate = JournalDate,
            Type = type,
            DebitAmount = debit,
            CreditAmount = credit,
            Notes = notes
        };

        foreach (var d in details)
        {
            if (d.IsAttended)
            {
                list.Add(Tx(d, WorkerTransactionType.WageAccrual, 0m, d.ActualWage,
                    string.IsNullOrWhiteSpace(d.Notes) ? "استحقاق حضور - وردية يومية" : $"استحقاق: {d.Notes}"));
                if (d.AmountPaid > 0)
                    list.Add(Tx(d, WorkerTransactionType.Payment, d.AmountPaid, 0m,
                        string.IsNullOrWhiteSpace(d.Notes) ? "سداد أجر - وردية يومية" : $"سداد: {d.Notes}"));
            }
            if (d.Advance > 0)
                list.Add(Tx(d, WorkerTransactionType.Advance, d.Advance, 0m,
                    string.IsNullOrWhiteSpace(d.Notes) ? "سلفة - وردية يومية" : $"سلفة: {d.Notes}"));
            if (d.Deduction > 0)
                list.Add(Tx(d, WorkerTransactionType.Deduction, d.Deduction, 0m,
                    string.IsNullOrWhiteSpace(d.Notes) ? "خصم وغرامة - وردية يومية" : $"خصم: {d.Notes}"));
        }
        return list;
    }

    [ObservableProperty]
    private bool isViewingMode = false;

    [ObservableProperty]
    private string saveButtonText = "حفظ وترحيل";

    partial void OnIsViewingModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotViewingMode));
    }

    public bool IsNotViewingMode => !IsViewingMode;

    // --- تنظيف النموذج ---
    [RelayCommand]
    private void ClearForm()
    {
        IsViewingMode = false;
        SaveButtonText = "حفظ وترحيل";
        editingJournalId = null;
        editingJournalShift = null;
        EmployeeName = string.Empty;
        Notes = string.Empty;
        CashFloat = null;
        CashSalesInput = null;
        BankingSalesInput = null;
        ActualCash = null;

        foreach (var input in BankSalesInputs)
        {
            input.Amount = null;
        }

        ExpenseItems.Clear();
        BankingItems.Clear();
        Returns.Clear();
        FreeOrders.Clear();

        AddExpenseItem();
        AddBankingItem();
        AddReturnItem();
        AddFreeOrderItem();

        StatusMessage = string.Empty;
        IsSaved = false;
        RefreshCalculations();
        _ = UpdateAvailableShiftsAsync(JournalDate);
        MarkClean();
    }

    // --- كشف البيانات غير المحفوظة ---
    private string _cleanSignature = string.Empty;

    /// <summary>بصمة الحقول التي يدخلها المستخدم؛ تغيّرها يعني وجود بيانات غير محفوظة.</summary>
    private string CurrentSignature() => string.Join("|",
        JournalDate.ToString("yyyyMMdd"), EmployeeName, Notes, CashFloat, CashSalesInput, BankingSalesInput, ActualCash,
        TotalSales, TotalExpenses, EffectiveBankingTotal, ReturnsAmount, FreeOrdersAmount,
        ExpenseItems.Count, BankingItems.Count, Returns.Count, FreeOrders.Count);

    private void MarkClean() => _cleanSignature = CurrentSignature();

    /// <summary>هل توجد تعديلات لم تُحفظ (تُستخدم للتنبيه قبل مغادرة الشاشة)؟</summary>
    public bool HasUnsavedChanges => !IsSaved && !IsViewingMode && CurrentSignature() != _cleanSignature;

    public System.Action? OnClose { get; set; }

    [RelayCommand]
    private void CloseForm()
    {
        OnClose?.Invoke();
    }

    [RelayCommand]
    private void PrintPdf()
    {
        try
        {
            var report = new MeezanPOS.Application.Services.DailyJournalPdfReport(this);
            string shiftName = GetShiftDisplayName(SelectedShiftType);
            string fileName = $"حركة يومية - {JournalDate:yyyy-MM-dd} - الوردية {shiftName}.pdf";
            var filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);

            // Generate PDF
            report.GeneratePdf(filePath);

            // Open PDF
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (System.Exception ex)
        {
            StatusMessage = "خطأ في الطباعة: " + ex.Message;
        }
    }

    public void LoadJournalForViewing(MeezanPOS.Domain.Entities.DailyJournal journal)
    {
        IsViewingMode = true;
        editingJournalId = null;
        editingJournalShift = null;
        LoadJournalData(journal);
        StatusMessage = "وضع العرض - لا يمكن تعديل حركة سابقة";
    }

    public void LoadJournalForEditing(MeezanPOS.Domain.Entities.DailyJournal journal)
    {
        IsViewingMode = false;
        editingJournalId = journal.Id;
        editingJournalShift = journal.ShiftType;
        LoadJournalData(journal);
        SaveButtonText = "حفظ";
        StatusMessage = "وضع التعديل - يمكنك تعديل البيانات ثم الضغط على حفظ";
    }

    private void LoadJournalData(MeezanPOS.Domain.Entities.DailyJournal journal)
    {
        JournalDate = journal.JournalDate;
        SelectedShiftType = journal.ShiftType;
        EmployeeName = journal.EmployeeName;
        Notes = journal.Notes ?? "";
        CashFloat = journal.CashFloat;

        CashSalesInput = journal.TotalSales - journal.BankingTotal;
        BankingSalesInput = journal.BankingTotal;
        ActualCash = journal.ActualCash;

        ExpenseItems.Clear();
        if (journal.ExpenseItems != null)
        {
            var multiWorkerItemIds = journal.ExpenseItems.Where(x => x.WorkerName == "[متعدد]").Select(x => x.Id).ToList();
            var workerTxsByItem = _journalService.GetWorkerTransactionsByExpenseItem(multiWorkerItemIds);
            foreach (var e in journal.ExpenseItems)
            {
                var item = new ExpenseItemViewModel
                {
                    SequenceNumber = ExpenseItems.Count + 1,
                    ExpenseType = e.Category ?? "",
                    Amount = e.Amount,
                    Description = e.Description ?? "",
                    SelectedSupplierId = e.SupplierId,
                    InvoiceNumber = e.InvoiceNumber ?? "",
                    Notes = e.Notes ?? "",
                    WorkerName = e.WorkerName ?? "",
                    WorkerId = e.WorkerId,
                    IsDetailedWage = e.WorkerName == "[متعدد]"
                };

                if (e.WorkerName == "[متعدد]")
                {
                    var txs = workerTxsByItem.TryGetValue(e.Id, out var itemTxs) ? itemTxs : new System.Collections.Generic.List<WorkerTransaction>();

                    var grouped = txs.GroupBy(t => t.WorkerId);
                    item.SelectedWorkerWagesDetails = grouped.Select(g => {
                        var workerId = g.Key;
                        var workerName = g.First().WorkerName;
                        var accrual = g.FirstOrDefault(t => t.Type == WorkerTransactionType.WageAccrual);
                        var payment = g.FirstOrDefault(t => t.Type == WorkerTransactionType.Payment);
                        var advance = g.FirstOrDefault(t => t.Type == WorkerTransactionType.Advance);
                        var deduction = g.FirstOrDefault(t => t.Type == WorkerTransactionType.Deduction);

                        return new WorkerTransactionDetailDto
                        {
                            WorkerId = workerId,
                            WorkerName = workerName,
                            IsAttended = accrual != null,
                            ActualWage = accrual?.CreditAmount ?? 0m,
                            Advance = advance?.DebitAmount ?? 0m,
                            Deduction = deduction?.DebitAmount ?? 0m,
                            AmountPaid = payment?.DebitAmount ?? 0m,
                            Notes = accrual?.Notes ?? payment?.Notes ?? advance?.Notes ?? deduction?.Notes
                        };
                    }).ToList();
                }
                else
                {
                    item.SelectedWorkerWagesDetails = new System.Collections.Generic.List<WorkerTransactionDetailDto>();
                }

                item.PropertyChanged += (s, e) => RefreshCalculations();
                ExpenseItems.Add(item);
            }
        }

        Returns.Clear();
        FreeOrders.Clear();
        if (journal.Adjustments != null)
        {
            foreach (var a in journal.Adjustments)
            {
                var item = new OrderAdjustmentItemViewModel
                {
                    Amount = a.Amount,
                    InvoiceNumber = a.InvoiceNumber ?? "",
                    Notes = a.Notes ?? "",
                    PersonName = a.PersonName ?? ""
                };
                item.PropertyChanged += (s, e) => RefreshCalculations();
                if (a.IsFreeOrder)
                {
                    item.SequenceNumber = FreeOrders.Count + 1;
                    FreeOrders.Add(item);
                }
                else
                {
                    item.SequenceNumber = Returns.Count + 1;
                    Returns.Add(item);
                }
            }
        }

        BankingItems.Clear();
        if (journal.BankingItems != null)
        {
            foreach (var b in journal.BankingItems)
            {
                string invoiceNo = "";
                string last4 = b.ReferenceNumber ?? "";
                string bankName = "";

                var activeBank = ActiveBankAccounts.FirstOrDefault(x => x.Id == b.BankAccountId);
                if (activeBank != null)
                {
                    bankName = activeBank.DisplayName;
                }

                string desc = b.Description ?? "";
                bool isLegacy = false;
                if (!string.IsNullOrEmpty(desc))
                {
                    foreach (var acc in ActiveBankAccounts)
                    {
                        if (desc.Contains(acc.DisplayName))
                        {
                            isLegacy = true;
                            break;
                        }
                    }
                    if (!isLegacy && desc.Contains(" - "))
                    {
                        isLegacy = true;
                    }
                }

                if (isLegacy)
                {
                    invoiceNo = "";
                    if (string.IsNullOrEmpty(last4) && desc.Contains(" - "))
                    {
                        var parts = desc.Split(new[] { " - " }, StringSplitOptions.None);
                        last4 = parts[parts.Length - 1];
                    }
                }
                else
                {
                    invoiceNo = desc;
                }

                var vmItem = new BankingItemViewModel
                {
                    SequenceNumber = BankingItems.Count + 1,
                    Amount = b.Amount,
                    BankName = bankName,
                    InvoiceNumber = invoiceNo,
                    Last4Digits = last4,
                    BankAccountId = b.BankAccountId
                };
                vmItem.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(BankingItemViewModel.BankAccountId) && s is BankingItemViewModel bankItem && bankItem.BankAccountId.HasValue)
                    {
                        var bank = ActiveBankAccounts.FirstOrDefault(x => x.Id == bankItem.BankAccountId.Value);
                        if (bank != null)
                        {
                            bankItem.BankName = bank.DisplayName;
                        }
                    }
                    RefreshCalculations();
                };
                BankingItems.Add(vmItem);
            }
        }

        RefreshCalculations();
    }
}
