using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Services;
using MeezanPOS.Application.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace MeezanPOS.Application.ViewModels;
public partial class GeneralExpensesViewModel : ObservableObject
{
    private readonly LoadFailureReporter _loadErrors = new();
    private readonly IBankService _bankService;
    private readonly IOwnerDebtService _ownerDebtService;
    private readonly IWagesService _wagesService;
    private readonly ICashLedgerService _cashLedgerService;
    private readonly IPostingService _postingService;
    private readonly ISessionService _sessionService;
    private readonly IGeneralExpenseService _expenses;

    public GeneralExpensesViewModel() : this(
        AppServiceProvider.Resolve<IBankService>(),
        AppServiceProvider.Resolve<IOwnerDebtService>(),
        AppServiceProvider.Resolve<IWagesService>(),
        AppServiceProvider.Resolve<ICashLedgerService>(),
        AppServiceProvider.Resolve<IPostingService>(),
        AppServiceProvider.Resolve<ISessionService>(),
        new GeneralExpenseService(DefaultDbContextFactory.Instance,
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(AppServiceProvider.Provider)))
    {
    }

    public GeneralExpensesViewModel(
        IBankService bankService,
        IOwnerDebtService ownerDebtService,
        IWagesService wagesService,
        ICashLedgerService cashLedgerService,
        IPostingService postingService,
        ISessionService sessionService,
        IGeneralExpenseService? expenseService = null)
    {
        _bankService = bankService ?? throw new ArgumentNullException(nameof(bankService));
        _ownerDebtService = ownerDebtService ?? throw new ArgumentNullException(nameof(ownerDebtService));
        _wagesService = wagesService ?? throw new ArgumentNullException(nameof(wagesService));
        _cashLedgerService = cashLedgerService ?? throw new ArgumentNullException(nameof(cashLedgerService));
        _postingService = postingService ?? throw new ArgumentNullException(nameof(postingService));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        // بدون خدمة ممررة (الاختبارات): الكتابة عبر الخدمات الممررة نفسها
        _expenses = expenseService ?? new GeneralExpenseService(DefaultDbContextFactory.Instance, null, bankService, ownerDebtService, cashLedgerService);

        InitializeLists();
        LoadExpenses();
        _ = LoadBankAccountsAsync();
        _ = LoadPartnerNamesAsync();
    }

    // --- خصائص التحديد والترحيل الجماعي ---
    [ObservableProperty]
    private bool isAllSelected;

    [ObservableProperty]
    private bool hasSelectedExpenses;

    [ObservableProperty]
    private decimal runningSelectedExpensesTotal;

    [ObservableProperty]
    private int runningSelectedCount;

    private bool _isUpdatingSelection;

    // --- فلاتر البحث ---
    [ObservableProperty]
    private DateTime dateFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    private DateTime dateTo = DateTime.Today;

    [ObservableProperty]
    private GeneralExpenseType? selectedFilterType;

    [ObservableProperty]
    private PaymentMethodType? selectedFilterPaymentMethod;

    [ObservableProperty]
    private bool showPostedArchive = false;

    [ObservableProperty]
    private int archiveLevel = 0; // 0 = الأشهر, 1 = التفاصيل داخل الشهر المحدد

    [ObservableProperty]
    private GeneralExpenseMonthCard? selectedArchivedMonth;

    public ObservableCollection<GeneralExpenseMonthCard> ArchivedMonths { get; } = new();

    partial void OnShowPostedArchiveChanged(bool value)
    {
        if (value)
        {
            ArchiveLevel = 0;
            SelectedArchivedMonth = null;
            LoadArchivedMonths();
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

    // --- بيانات العرض ---
    public ObservableCollection<GeneralExpenseDisplayItem> Expenses { get; } = new();

    // --- ملخصات ---
    [ObservableProperty]
    private decimal totalAmount;

    [ObservableProperty]
    private int totalCount;

    // --- نموذج الإضافة/التعديل ---
    [ObservableProperty]
    private bool isEditing = false;

    [ObservableProperty]
    private bool isViewingMode = false;

    [ObservableProperty]
    private int? editingId = null;

    [ObservableProperty]
    private GeneralExpenseType selectedExpenseType = GeneralExpenseType.Rent;

    [ObservableProperty]
    private decimal? inputAmount;

    [ObservableProperty]
    private DateTime inputPaymentDate = DateTime.Today;

    [ObservableProperty]
    private PaymentMethodType selectedPaymentMethod = PaymentMethodType.Cash;

    [ObservableProperty]
    private string inputDescription = string.Empty;

    [ObservableProperty]
    private string inputCustomExpenseType = string.Empty;

    [ObservableProperty]
    private bool isDetailedWage = false;

    [ObservableProperty]
    private string inputWorkerName = string.Empty;

    [ObservableProperty]
    private int? inputWorkerId;

    [ObservableProperty]
    private List<WorkerTransactionDetailDto>? _selectedWorkerWagesDetails;

    public ObservableCollection<string> AvailableWorkerNames { get; } = new();

    public ObservableCollection<BankAccount> BankAccounts { get; } = new();

    [ObservableProperty]
    private BankAccount? selectedBankAccountForExpense;

    [ObservableProperty]
    private string inputReferenceNumber = string.Empty;

    [ObservableProperty]
    private string inputPartnerName = string.Empty;

    public ObservableCollection<string> PartnerNames { get; } = new();

    private async System.Threading.Tasks.Task LoadBankAccountsAsync()
    {
        try
        {
            var bankService = _bankService;
            var accountsList = await bankService.GetAllAccountsAsync();
            UiThread.Run(() =>
            {
                BankAccounts.Clear();
                foreach (var account in accountsList.Where(a => a.IsActive))
                {
                    BankAccounts.Add(account);
                }
            });
        }
        catch (Exception ex)
        {
            _loadErrors.Report(ex, "الحسابات المصرفية");
        }
    }

    private async System.Threading.Tasks.Task LoadPartnerNamesAsync()
    {
        try
        {
            var ownerDebtService = _ownerDebtService;
            var names = await ownerDebtService.GetPartnerNamesAsync();
            UiThread.Run(() =>
            {
                PartnerNames.Clear();
                foreach (var name in names)
                {
                    PartnerNames.Add(name);
                }
            });
        }
        catch (Exception ex)
        {
            _loadErrors.Report(ex, "أسماء الشركاء");
        }
    }

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

    // --- نافذة الإضافة ---
    [ObservableProperty]
    private string dialogTitle = "إضافة مصروف عام";


    // --- القوائم ---
    public ObservableCollection<ExpenseTypeItem> ExpenseTypes { get; } = new();
    public ObservableCollection<ExpenseTypeItem> FilterExpenseTypes { get; } = new();
    public ObservableCollection<PaymentMethodItem> PaymentMethods { get; } = new();
    public ObservableCollection<PaymentMethodItem> FilterPaymentMethods { get; } = new();

    // --- ملخصات حسب النوع ---
    public ObservableCollection<TypeSummaryItem> TypeSummaries { get; } = new();



    private void InitializeLists()
    {
        // أنواع المصاريف
        ExpenseTypes.Add(new ExpenseTypeItem { Name = "إيجار", Value = GeneralExpenseType.Rent });
        ExpenseTypes.Add(new ExpenseTypeItem { Name = "كهرباء", Value = GeneralExpenseType.Electricity });
        ExpenseTypes.Add(new ExpenseTypeItem { Name = "ماء", Value = GeneralExpenseType.Water });
        ExpenseTypes.Add(new ExpenseTypeItem { Name = "رواتب", Value = GeneralExpenseType.Salaries });
        ExpenseTypes.Add(new ExpenseTypeItem { Name = "إنترنت", Value = GeneralExpenseType.Internet });
        ExpenseTypes.Add(new ExpenseTypeItem { Name = "صيانة", Value = GeneralExpenseType.Maintenance });
        ExpenseTypes.Add(new ExpenseTypeItem { Name = "تأمين", Value = GeneralExpenseType.Insurance });
        ExpenseTypes.Add(new ExpenseTypeItem { Name = "ضرائب/رسوم", Value = GeneralExpenseType.Taxes });
        ExpenseTypes.Add(new ExpenseTypeItem { Name = "تسديد قيمة", Value = GeneralExpenseType.SupplierPayment });
        ExpenseTypes.Add(new ExpenseTypeItem { Name = "أخرى", Value = GeneralExpenseType.Other });

        // فلتر الأنواع (مع خيار الكل)
        FilterExpenseTypes.Add(new ExpenseTypeItem { Name = "الكل", Value = null });
        foreach (var item in ExpenseTypes)
            FilterExpenseTypes.Add(item);

        // طرق الدفع (للإضافة - بدون شيك)
        PaymentMethods.Add(new PaymentMethodItem { Name = "نقدي", Value = PaymentMethodType.Cash });
        PaymentMethods.Add(new PaymentMethodItem { Name = "تحويل", Value = PaymentMethodType.BankTransfer });
        PaymentMethods.Add(new PaymentMethodItem { Name = "شخصي (شريك)", Value = PaymentMethodType.PersonalPartner });

        // فلتر طرق الدفع (للبحث - مع خيار الكل والشيك للبيانات القديمة)
        FilterPaymentMethods.Add(new PaymentMethodItem { Name = "الكل", Value = null });
        FilterPaymentMethods.Add(new PaymentMethodItem { Name = "نقدي", Value = PaymentMethodType.Cash });
        FilterPaymentMethods.Add(new PaymentMethodItem { Name = "تحويل", Value = PaymentMethodType.BankTransfer });
        FilterPaymentMethods.Add(new PaymentMethodItem { Name = "شخصي (شريك)", Value = PaymentMethodType.PersonalPartner });
    }

    public static string GetExpenseTypeName(GeneralExpenseType type) => type switch
    {
        GeneralExpenseType.Rent => "إيجار",
        GeneralExpenseType.Electricity => "كهرباء",
        GeneralExpenseType.Water => "ماء",
        GeneralExpenseType.Salaries => "رواتب",
        GeneralExpenseType.Internet => "إنترنت",
        GeneralExpenseType.Maintenance => "صيانة",
        GeneralExpenseType.Insurance => "تأمين",
        GeneralExpenseType.Taxes => "ضرائب/رسوم",
        GeneralExpenseType.SupplierPayment => "تسديد قيمة",
        GeneralExpenseType.Other => "أخرى",
        _ => "غير معروف"
    };

    public static string GetPaymentMethodName(PaymentMethodType method) => method switch
    {
        PaymentMethodType.Cash => "نقدي",
        PaymentMethodType.BankTransfer => "تحويل",
        PaymentMethodType.Cheque => "شيك",
        PaymentMethodType.PersonalPartner => "شخصي (شريك)",
        _ => "غير معروف"
    };

    [RelayCommand]
    private void LoadExpenses()
    {
        try
        {
            var from = DateFrom;
            var to = DateTo;
            if (ShowPostedArchive && ArchiveLevel == 1 && SelectedArchivedMonth != null)
            {
                from = new DateTime(SelectedArchivedMonth.Year, SelectedArchivedMonth.Month, 1);
                to = from.AddMonths(1).AddDays(-1);
            }
            var data = _expenses.Query(new GeneralExpenseQuery(ShowPostedArchive, from, to, SelectedFilterType, SelectedFilterPaymentMethod));

            foreach (var item in Expenses)
            {
                if (item != null)
                {
                    item.PropertyChanged -= Item_PropertyChanged;
                }
            }

            Expenses.Clear();
            int seq = 1;

            foreach (var item in data)
            {
                var displayItem = new GeneralExpenseDisplayItem
                {
                    Sequence = seq++,
                    Id = item.Id,
                    ExpenseType = item.ExpenseType,
                    CustomExpenseName = item.CustomExpenseName,
                    Amount = item.Amount,
                    PaymentDate = item.PaymentDate,
                    PaymentMethod = item.PaymentMethod,
                    PaymentMethodName = GetPaymentMethodName(item.PaymentMethod),
                    Description = item.Description,
                    WorkerName = item.WorkerName,
                    WorkerId = item.WorkerId,
                    BankAccountId = item.BankAccountId,
                    FinancialStatus = item.FinancialStatus
                };
                displayItem.PropertyChanged += Item_PropertyChanged;
                Expenses.Add(displayItem);
            }

            TotalAmount = Expenses.Sum(e => e.Amount);
            TotalCount = Expenses.Count;
            UpdateTypeSummaries();
            RecalculateTotals();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"خطأ في تحميل المصاريف العامة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateTypeSummaries()
    {
        TypeSummaries.Clear();
        var groups = Expenses.GroupBy(e => e.ExpenseType)
            .Select(g => new TypeSummaryItem
            {
                TypeName = GetExpenseTypeName(g.Key),
                Total = g.Sum(e => e.Amount),
                Count = g.Count()
            })
            .OrderByDescending(s => s.Total);

        foreach (var item in groups)
            TypeSummaries.Add(item);
    }

    [RelayCommand]
    private void ResetFilters()
    {
        DateFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        DateTo = DateTime.Today;
        SelectedFilterType = null;
        SelectedFilterPaymentMethod = null;
        LoadExpenses();
    }

    [RelayCommand]
    private void OpenAddExpenseDialog()
    {
        ClearForm();
        DialogTitle = "إضافة مصروف عام";
        _ = LoadWorkerNamesAsync();
        _ = LoadPartnerNamesAsync();
        AppWindows.Current.ShowGeneralExpenseForm(this);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SaveExpenseAsync(System.Windows.Window window)
    {
        // --- Validation ---
        if (!InputAmount.HasValue || InputAmount.Value <= 0)
        {
            Dialogs.Show("يرجى إدخال مبلغ صحيح أكبر من صفر.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (InputPaymentDate > DateTime.Today)
        {
            Dialogs.Show("تاريخ الدفع لا يمكن أن يكون في المستقبل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedExpenseType == GeneralExpenseType.Salaries && IsDetailedWage && (SelectedWorkerWagesDetails == null || !SelectedWorkerWagesDetails.Any()))
        {
            Dialogs.Show("يرجى تحديد تفاصيل أجور حضور العمال.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedExpenseType == GeneralExpenseType.Other && string.IsNullOrWhiteSpace(InputCustomExpenseType))
        {
            Dialogs.Show("يرجى كتابة نوع المصروف اليدوي.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedPaymentMethod == PaymentMethodType.BankTransfer && SelectedBankAccountForExpense == null)
        {
            Dialogs.Show("يرجى اختيار الحساب البنكي للدفع عن طريق التحويل البنكي.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedPaymentMethod == PaymentMethodType.PersonalPartner && string.IsNullOrWhiteSpace(InputPartnerName))
        {
            Dialogs.Show("يرجى إدخال أو تحديد اسم الشريك الممول للمصروف الشخصي.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var isSalaries = SelectedExpenseType == GeneralExpenseType.Salaries;
            var result = await _expenses.SaveAsync(new GeneralExpenseSaveRequest
            {
                EditingId = EditingId,
                ExpenseType = SelectedExpenseType,
                TypeDisplayName = SelectedExpenseType == GeneralExpenseType.Other ? InputCustomExpenseType : GetExpenseTypeName(SelectedExpenseType),
                CustomExpenseName = SelectedExpenseType == GeneralExpenseType.Other ? InputCustomExpenseType.Trim() : null,
                Amount = InputAmount.Value,
                PaymentDate = InputPaymentDate,
                PaymentMethod = SelectedPaymentMethod,
                Description = InputDescription,
                WorkerName = isSalaries ? (IsDetailedWage ? "[متعدد]" : (!string.IsNullOrEmpty(InputWorkerName) ? InputWorkerName : null)) : null,
                WorkerId = (isSalaries && !IsDetailedWage) ? InputWorkerId : null,
                BankAccountId = SelectedBankAccountForExpense?.Id,
                BankReferenceNumber = InputReferenceNumber,
                PartnerName = InputPartnerName,
                WorkerTransactions = (isSalaries && IsDetailedWage && SelectedWorkerWagesDetails != null)
                    ? BuildWorkerTransactions(SelectedWorkerWagesDetails, InputPaymentDate)
                    : new List<WorkerTransaction>(),
            });

            if (!result.Success)
            {
                Dialogs.Show(result.Error!, result.ErrorTitle ?? "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Dialogs.Show(EditingId.HasValue ? "تم تعديل المصروف بنجاح." : "تم إضافة المصروف بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);

            ClearForm();
            LoadExpenses();

            // إغلاق النافذة إذا تم التمرير بنجاح
            if (window != null)
            {
                window.Close();
            }
        }
        catch (Exception ex)
        {
            Dialogs.Show($"خطأ في حفظ المصروف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task EditExpense(GeneralExpenseDisplayItem item)
    {
        if (item == null) return;
        if (item.ExpenseType == GeneralExpenseType.SupplierPayment)
        {
            Dialogs.Show("لا يمكن تعديل مصروف تسديد الموردين من هنا. يرجى إدارته من شاشة كشف حساب المورد المحدد.", "منع التعديل", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        IsEditing = true;
        IsViewingMode = false;
        EditingId = item.Id;
        DialogTitle = "تعديل مصروف عام";
        SelectedExpenseType = item.ExpenseType;
        InputAmount = item.Amount;
        InputPaymentDate = item.PaymentDate;
        SelectedPaymentMethod = item.PaymentMethod;
        SelectedBankAccountForExpense = BankAccounts.FirstOrDefault(b => b.Id == item.BankAccountId);
        InputDescription = item.Description;
        InputCustomExpenseType = item.CustomExpenseName ?? string.Empty;
        IsDetailedWage = item.WorkerName == "[متعدد]";
        InputWorkerName = item.WorkerName ?? string.Empty;
        InputWorkerId = item.WorkerId;

        if (IsDetailedWage)
        {
            SelectedWorkerWagesDetails = ToWageDetails(await _expenses.GetWorkerTransactionsAsync(item.Id));
        }
        else
        {
            SelectedWorkerWagesDetails = new List<WorkerTransactionDetailDto>();
        }

        if (IsDetailedWage)
        {
            SelectedWorkerWagesDetails = ToWageDetails(await _expenses.GetWorkerTransactionsAsync(item.Id));
        }
        else
        {
            SelectedWorkerWagesDetails = new List<WorkerTransactionDetailDto>();
        }

        InputReferenceNumber = string.Empty;
        InputPartnerName = string.Empty;

        if (SelectedPaymentMethod == PaymentMethodType.BankTransfer || SelectedPaymentMethod == PaymentMethodType.PersonalPartner)
        {
            var payment = await _expenses.GetPaymentInfoAsync(item.Id, isSupplierPayment: false);
            if (SelectedPaymentMethod == PaymentMethodType.BankTransfer)
                InputReferenceNumber = payment.ReferenceNumber ?? string.Empty;
            else
                InputPartnerName = payment.PartnerName ?? string.Empty;
        }

        _ = LoadWorkerNamesAsync();
        _ = LoadPartnerNamesAsync();

        AppWindows.Current.ShowGeneralExpenseForm(this);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ViewExpense(GeneralExpenseDisplayItem item)
    {
        if (item == null) return;
        IsEditing = false;
        IsViewingMode = true;
        EditingId = null;
        DialogTitle = "عرض تفاصيل المصروف";
        SelectedExpenseType = item.ExpenseType;
        InputAmount = item.Amount;
        InputPaymentDate = item.PaymentDate;
        SelectedPaymentMethod = item.PaymentMethod;
        SelectedBankAccountForExpense = BankAccounts.FirstOrDefault(b => b.Id == item.BankAccountId);
        InputDescription = item.Description;
        InputCustomExpenseType = item.CustomExpenseName ?? string.Empty;
        IsDetailedWage = item.WorkerName == "[متعدد]";
        InputWorkerName = item.WorkerName ?? string.Empty;
        InputWorkerId = item.WorkerId;

        InputReferenceNumber = string.Empty;
        InputPartnerName = string.Empty;

        if (SelectedPaymentMethod == PaymentMethodType.BankTransfer || SelectedPaymentMethod == PaymentMethodType.PersonalPartner)
        {
            var payment = await _expenses.GetPaymentInfoAsync(item.Id, isSupplierPayment: false);
            if (SelectedPaymentMethod == PaymentMethodType.BankTransfer)
                InputReferenceNumber = payment.ReferenceNumber ?? string.Empty;
            else
                InputPartnerName = payment.PartnerName ?? string.Empty;
        }

        _ = LoadWorkerNamesAsync();
        _ = LoadPartnerNamesAsync();

        AppWindows.Current.ShowGeneralExpenseForm(this);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ShowExpenseDetailsAsync(GeneralExpenseDisplayItem item)
    {
        if (item == null) return;

        try
        {
            string bankDetails = "";

            if (item.PaymentMethod == PaymentMethodType.BankTransfer)
            {
                var payment = await _expenses.GetPaymentInfoAsync(item.Id, item.ExpenseType == GeneralExpenseType.SupplierPayment);
                if (payment.BankDisplayName != null)
                {
                    bankDetails = $"\n🏦 الحساب البنكي: {payment.BankDisplayName}\n🔢 رقم التحويل اخر 4 ارقام: {payment.ReferenceNumber ?? "----"}\n";
                }
                else if (item.BankAccountId.HasValue)
                {
                    var bankAcc = BankAccounts.FirstOrDefault(b => b.Id == item.BankAccountId.Value);
                    if (bankAcc != null)
                    {
                        bankDetails = $"\n🏦 الحساب البنكي: {bankAcc.BankName} - {bankAcc.FriendlyName}\n🔢 رقم التحويل اخر 4 ارقام: ----\n";
                    }
                }
            }
            else if (item.PaymentMethod == PaymentMethodType.PersonalPartner)
            {
                var payment = await _expenses.GetPaymentInfoAsync(item.Id, false);
                if (payment.PartnerName != null)
                {
                    bankDetails = $"\n👤 الشريك الممول: {payment.PartnerName}\n";
                }
            }

            string details = $"🧾 تفاصيل المصروف:\n" +
                             $"------------------------------------------------------\n" +
                             $"📌 النوع: {item.ExpenseTypeName}\n" +
                             $"💰 المبلغ: {item.Amount:N2} د.ل\n" +
                             $"📅 التاريخ: {item.PaymentDate:yyyy/MM/dd}\n" +
                             $"💳 طريقة الدفع: {item.PaymentMethodName}\n";

            if (!string.IsNullOrEmpty(bankDetails))
            {
                details += bankDetails;
            }

            if (!string.IsNullOrEmpty(item.WorkerName))
            {
                details += $"\n👤 اسم العامل: {item.WorkerName}\n";
            }

            details += $"\n📝 الوصف: {(string.IsNullOrEmpty(item.Description) ? "لا يوجد" : item.Description)}";

            Dialogs.Show(details, "تفاصيل المصروف العام", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء جلب التفاصيل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>تفاصيل أجور العمال من حركاتهم المحفوظة (لكل عامل: استحقاق، سداد، سلفة، خصم).</summary>
    private static List<WorkerTransactionDetailDto> ToWageDetails(List<WorkerTransaction> txs)
        => txs.GroupBy(t => t.WorkerId).Select(g =>
        {
            var accrual = g.FirstOrDefault(t => t.Type == WorkerTransactionType.WageAccrual);
            var payment = g.FirstOrDefault(t => t.Type == WorkerTransactionType.Payment);
            var advance = g.FirstOrDefault(t => t.Type == WorkerTransactionType.Advance);
            var deduction = g.FirstOrDefault(t => t.Type == WorkerTransactionType.Deduction);
            return new WorkerTransactionDetailDto
            {
                WorkerId = g.Key,
                WorkerName = g.First().WorkerName,
                IsAttended = accrual != null,
                ActualWage = accrual?.CreditAmount ?? 0m,
                Advance = advance?.DebitAmount ?? 0m,
                Deduction = deduction?.DebitAmount ?? 0m,
                AmountPaid = payment?.DebitAmount ?? 0m,
                Notes = accrual?.Notes ?? payment?.Notes ?? advance?.Notes ?? deduction?.Notes
            };
        }).ToList();

    /// <summary>حركات العمال لمصروف أجور تفصيلي (يُربط معرف المصروف عند الحفظ).</summary>
    private static List<WorkerTransaction> BuildWorkerTransactions(IEnumerable<WorkerTransactionDetailDto> details, DateTime date)
    {
        var list = new List<WorkerTransaction>();
        WorkerTransaction Tx(WorkerTransactionDetailDto d, WorkerTransactionType type, decimal debit, decimal credit, string notes) => new()
        {
            WorkerId = d.WorkerId,
            WorkerName = d.WorkerName,
            TransactionDate = date,
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
                    string.IsNullOrWhiteSpace(d.Notes) ? "استحقاق حضور - مصروف عام" : $"استحقاق: {d.Notes}"));
                if (d.AmountPaid > 0)
                    list.Add(Tx(d, WorkerTransactionType.Payment, d.AmountPaid, 0m,
                        string.IsNullOrWhiteSpace(d.Notes) ? "سداد أجر - مصروف عام" : $"سداد: {d.Notes}"));
            }
            if (d.Advance > 0)
                list.Add(Tx(d, WorkerTransactionType.Advance, d.Advance, 0m,
                    string.IsNullOrWhiteSpace(d.Notes) ? "سلفة - مصروف عام" : $"سلفة: {d.Notes}"));
            if (d.Deduction > 0)
                list.Add(Tx(d, WorkerTransactionType.Deduction, d.Deduction, 0m,
                    string.IsNullOrWhiteSpace(d.Notes) ? "خصم وغرامة - مصروف عام" : $"خصم: {d.Notes}"));
        }
        return list;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        ClearForm();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task OpenWorkerWagesDialogAsync()
    {
        var result = await AppWindows.Current.EditWorkerWagesAsync(SelectedWorkerWagesDetails);
        if (result != null)
        {
            SelectedWorkerWagesDetails = result.Details;
            InputAmount = result.TotalPaid;
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeleteExpenseAsync(GeneralExpenseDisplayItem item)
    {
        if (item == null) return;

        if (item.ExpenseType == GeneralExpenseType.SupplierPayment)
        {
            Dialogs.Show("لا يمكن حذف مصروف تسديد الموردين من هنا. يرجى إدارته من شاشة كشف حساب المورد المحدد.", "منع الحذف", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = Dialogs.Show(
            $"هل تريد حذف مصروف ({item.ExpenseTypeName}) بمبلغ {item.Amount:N2}؟",
            "تأكيد الحذف",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var deleted = await _expenses.DeleteAsync(item.Id);
            if (!deleted.Success)
            {
                Dialogs.Show(deleted.Error!, deleted.ErrorTitle ?? "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            LoadExpenses();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"خطأ في حذف المصروف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task PostExpense(GeneralExpenseDisplayItem item)
    {
        if (item == null || item.IsPosted) return;

        var result = Dialogs.Show(
            $"هل أنت متأكد من ترحيل المصروف بقيمة {item.Amount:N2}؟\nلن تتمكن من تعديله أو حذفه بعد الترحيل.",
            "تأكيد الترحيل", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var postingService = _postingService;
            var currentUserId = _sessionService.CurrentUserId;
            await postingService.PostEntityAsync<Domain.Entities.GeneralExpense>(item.Id, currentUserId);

            Dialogs.Show("تم ترحيل المصروف بنجاح. أصبحت الحركة مغلقة مالياً.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadExpenses();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"خطأ في الترحيل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task UnpostExpense(GeneralExpenseDisplayItem item)
    {
        if (item == null || !item.IsPosted) return;

        var sessionService = _sessionService;
        if (!sessionService.HasPermission("UnpostFinancial"))
        {
            Dialogs.Show("عذراً، هذا الإجراء متاح فقط للمدير العام.", "صلاحية غير كافية", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // تحقق إذا كان المصروف مرتبط بجلسة مقفلة/مسواة مالياً
        if (await _expenses.IsInSettledSessionAsync(item.Id))
        {
            Dialogs.Show(
                "هذا المصروف يقع ضمن فترة مقفلة ومسواة مالياً مسبقاً.\nيجب إلغاء قفل الفترة أولاً من شاشة المبيعات (الكاش الحالي -> أرشيف التسويات) قبل التمكن من فك الترحيل.",
                "فترة مغلقة ومسواة",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // إظهار نافذة إدخال سبب فك الترحيل
        var unlock = AppWindows.Current.AskPeriodUnlockReason();
        if (unlock == null) return;
        string selectedReason = unlock.Reason;
        string detailReason = unlock.Detail;

        var reason = $"{selectedReason} - {detailReason}";

        var result = Dialogs.Show(
            $"هل أنت متأكد من فك ترحيل المصروف؟\nهذا الإجراء سيتم تسجيله في سجل التدقيق (Audit Log) باسمك.",
            "تأكيد فك الترحيل", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var postingService = _postingService;
            var currentUserId = sessionService.CurrentUserId;
            await postingService.UnpostEntityAsync<Domain.Entities.GeneralExpense>(item.Id, reason, currentUserId);

            Dialogs.Show("تم فك الترحيل بنجاح وتم تسجيل العملية في سجل التدقيق.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadExpenses();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"خطأ في فك الترحيل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void PrintPdf()
    {
        try
        {
            if (!Expenses.Any())
            {
                Dialogs.Show("لا توجد بيانات للطباعة.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var report = new Services.GeneralExpensePdfReport(this);
            var filePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"تقرير_المصاريف_العامة_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

            report.GeneratePdf(filePath);

            // فتح الملف مباشرة من المجلد المؤقت
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Dialogs.Show($"خطأ في إنشاء التقرير: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearForm()
    {
        IsEditing = false;
        IsViewingMode = false;
        EditingId = null;
        SelectedExpenseType = GeneralExpenseType.Rent;
        InputAmount = null;
        InputPaymentDate = DateTime.Today;
        SelectedPaymentMethod = PaymentMethodType.Cash;
        InputDescription = string.Empty;
        InputCustomExpenseType = string.Empty;
        IsDetailedWage = false;
        InputWorkerName = string.Empty;
        InputWorkerId = null;
        SelectedWorkerWagesDetails = new List<WorkerTransactionDetailDto>();
        SelectedBankAccountForExpense = BankAccounts.FirstOrDefault();
        InputReferenceNumber = string.Empty;
        InputPartnerName = string.Empty;
    }

    partial void OnIsAllSelectedChanged(bool value)
    {
        if (_isUpdatingSelection) return;

        _isUpdatingSelection = true;
        try
        {
            foreach (var item in Expenses)
            {
                if (item.IsDraft)
                {
                    item.IsSelected = value;
                }
                else
                {
                    item.IsSelected = false;
                }
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
        if (e.PropertyName == nameof(GeneralExpenseDisplayItem.IsSelected))
        {
            if (_isUpdatingSelection) return;

            if (sender is GeneralExpenseDisplayItem item)
            {
                if (item.IsSelected)
                {
                    RunningSelectedExpensesTotal += item.Amount;
                    RunningSelectedCount++;
                }
                else
                {
                    RunningSelectedExpensesTotal -= item.Amount;
                    RunningSelectedCount--;
                }

                if (RunningSelectedExpensesTotal < 0) RunningSelectedExpensesTotal = 0;
                if (RunningSelectedCount < 0) RunningSelectedCount = 0;

                HasSelectedExpenses = RunningSelectedCount > 0;

                _isUpdatingSelection = true;
                try
                {
                    var draftCount = Expenses.Count(x => x.IsDraft);
                    IsAllSelected = draftCount > 0 && RunningSelectedCount == draftCount;
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
        decimal expensesTotal = 0;
        int count = 0;

        foreach (var item in Expenses)
        {
            if (item.IsSelected && item.IsDraft)
            {
                expensesTotal += item.Amount;
                count++;
            }
        }

        RunningSelectedExpensesTotal = expensesTotal;
        RunningSelectedCount = count;
        HasSelectedExpenses = count > 0;

        _isUpdatingSelection = true;
        try
        {
            var draftCount = Expenses.Count(x => x.IsDraft);
            IsAllSelected = draftCount > 0 && count == draftCount;
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task PostSelectedExpensesAsync()
    {
        var selectedIds = Expenses.Where(e => e.IsSelected && e.IsDraft).Select(e => e.Id).ToList();
        if (!selectedIds.Any())
        {
            Dialogs.Show("يرجى تحديد مصروف واحد على الأقل للترحيل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirmResult = Dialogs.Show(
            $"هل أنت متأكد من ترحيل وإقفال عدد ({selectedIds.Count}) مصاريف محددة مالياً؟\n" +
            $"إجمالي المصاريف المحددة: {RunningSelectedExpensesTotal:N2} د.ل\n" +
            $"بعد الترحيل، سيتم قفل هذه العمليات نهائياً ولن تتمكن من تعديلها أو حذفها.",
            "تأكيد الترحيل الجماعي للمصاريف",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmResult != MessageBoxResult.Yes) return;

        try
        {
            var postingService = _postingService;
            var currentUserId = _sessionService.CurrentUserId;
            var batchResult = await postingService.PostGeneralExpensesBatchAsync(selectedIds, currentUserId, "ترحيل جماعي للمصاريف المحددة من الواجهة");

            if (batchResult.Success)
            {
                Dialogs.Show(
                    $"تمت عملية الترحيل الجماعي للمصاريف بنجاح!\n\n" +
                    $"🔹 عدد المصاريف المرحلة: {batchResult.PostedCount}\n" +
                    $"🔹 إجمالي المصاريف المرحلة: {batchResult.TotalExpenses:N2} د.ل\n" +
                    $"🔹 زمن التنفيذ الفعلي: {batchResult.Duration.TotalMilliseconds:N0} مللي ثانية\n" +
                    $"🔹 معرف جلسة الترحيل (Session Guid):\n{batchResult.SessionGuid}\n" +
                    $"🔹 معرف التتبع (Correlation Id):\n{batchResult.CorrelationId}",
                    "نجاح الترحيل الجماعي للمصاريف",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadExpenses();
            }
            else
            {
                var errors = string.Join("\n", batchResult.Errors);
                Dialogs.Show($"فشلت عملية الترحيل الجماعي:\n{errors}", "خطأ في الترحيل", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ غير متوقع أثناء الترحيل الجماعي: {ex.Message}", "خطأ قاتل", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task PostPeriodAsync()
    {
        var range = AppWindows.Current.AskDateRange(DateFrom, DateTo);
        if (range == null) return;
        DateTime startDate = range.Value.Start;
        DateTime endDate = range.Value.End;

        try
        {
            var expensesInPeriod = await _expenses.GetDraftIdsInRangeAsync(startDate, endDate);

            if (!expensesInPeriod.Any())
            {
                Dialogs.Show(
                    $"لا توجد أي مصاريف مفتوحة (غير مرحلة) في الفترة المحددة:\nمن: {startDate:dd-MM-yyyy} إلى: {endDate:dd-MM-yyyy}",
                    "لا توجد بيانات للترحيل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirmResult = Dialogs.Show(
                $"هل أنت متأكد من ترحيل وإقفال جميع المصاريف المفتوحة في الفترة المحددة؟\n\n" +
                $"الفترة: من {startDate:dd-MM-yyyy} إلى {endDate:dd-MM-yyyy}\n" +
                $"📦 عدد المصاريف المفتوحة المكتشفة: {expensesInPeriod.Count} مصروف\n\n" +
                $"بعد الترحيل، سيتم قفل هذه العمليات محاسبياً نهائياً ولن تتمكن من تعديلها أو حذفها.",
                "تأكيد ترحيل وإقفال فترة زمنية",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes) return;

            var postingService = _postingService;
            var currentUserId = _sessionService.CurrentUserId;
            var batchResult = await postingService.PostGeneralExpensesBatchAsync(expensesInPeriod, currentUserId, $"ترحيل جماعي للمصاريف للفترة من {startDate:dd-MM-yyyy} إلى {endDate:dd-MM-yyyy}");

            if (batchResult.Success)
            {
                Dialogs.Show(
                    $"تمت عملية الترحيل الجماعي للمصاريف بنجاح!\n\n" +
                    $"الفترة: من {startDate:dd-MM-yyyy} إلى {endDate:dd-MM-yyyy}\n" +
                    $"🔹 عدد المصاريف المرحلة: {batchResult.PostedCount}\n" +
                    $"🔹 إجمالي المصاريف المرحلة: {batchResult.TotalExpenses:N2} د.ل\n" +
                    $"🔹 زمن التنفيذ الفعلي: {batchResult.Duration.TotalMilliseconds:N0} مللي ثانية\n" +
                    $"🔹 معرف الجلسة (Session Guid):\n{batchResult.SessionGuid}\n" +
                    $"🔹 معرف التتبع (Correlation Id):\n{batchResult.CorrelationId}",
                    "نجاح الترحيل الجماعي للفترة الزمنية",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadExpenses();
            }
            else
            {
                var errors = string.Join("\n", batchResult.Errors);
                Dialogs.Show($"فشلت عملية الترحيل الجماعي:\n{errors}", "خطأ في الترحيل", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ غير متوقع أثناء الترحيل الجماعي للفترة: {ex.Message}", "خطأ قاتل", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void LoadArchivedMonths()
    {
        try
        {
            var expenses = _expenses.GetPostedInRange(DateFrom, DateTo);

            var grouped = expenses
                .GroupBy(e => new { e.PaymentDate.Year, e.PaymentDate.Month })
                .Select(g => {
                    var year = g.Key.Year;
                    var month = g.Key.Month;
                    var postedCount = g.Count(e => e.FinancialStatus == FinancialStatus.Posted || e.FinancialStatus == FinancialStatus.Archived);
                    var totalCount = g.Count();
                    var isPosted = postedCount == totalCount;
                    var isPartiallyPosted = postedCount > 0 && postedCount < totalCount;
                    return new GeneralExpenseMonthCard
                    {
                        Year = year,
                        Month = month,
                        MonthName = $"{ExpenseManagementViewModel.GetArabicMonthName(month)} {year}",
                        TotalExpenses = g.Sum(e => e.Amount),
                        OperationsCount = totalCount,
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
        }
        catch (Exception ex)
        {
            Dialogs.Show($"خطأ في تحميل كروت أشهر الأرشيف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void SelectMonth(GeneralExpenseMonthCard month)
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
        LoadArchivedMonths();
    }
}
