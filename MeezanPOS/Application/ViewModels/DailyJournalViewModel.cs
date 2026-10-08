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

    /// <summary>تسويات شركاء صُرفت من درج هذه اليومية (تُسجل من شاشة الخدمات المصرفية، وتُعرض هنا فقط).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpectedCash), nameof(Difference), nameof(DifferenceStatus), nameof(DifferenceColor), nameof(HasDrawerPayouts))]
    private decimal drawerPayouts;

    public bool HasDrawerPayouts => DrawerPayouts > 0;

    public decimal ExpectedCash => (CashFloat ?? 0) + (CashSalesInput ?? 0) - TotalExpenses - DrawerPayouts - ReturnsAmount - FreeOrdersAmount;
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

}
