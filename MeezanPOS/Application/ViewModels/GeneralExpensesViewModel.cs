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

/// <summary>
/// عنصر عرض مصروف عام
/// </summary>
public partial class GeneralExpenseDisplayItem : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    public int Sequence { get; set; }
    public int Id { get; set; }
    public GeneralExpenseType ExpenseType { get; set; }
    public string? CustomExpenseName { get; set; }
    public string ExpenseTypeName => (ExpenseType == GeneralExpenseType.Other && !string.IsNullOrEmpty(CustomExpenseName)) 
        ? CustomExpenseName 
        : GeneralExpensesViewModel.GetExpenseTypeName(ExpenseType);
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string PaymentDateDisplay => PaymentDate.ToString("yyyy/MM/dd");
    public PaymentMethodType PaymentMethod { get; set; }
    public string PaymentMethodName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? WorkerName { get; set; }
    public int? WorkerId { get; set; }
    public int? BankAccountId { get; set; }

    // --- Posting System Properties ---
    public FinancialStatus FinancialStatus { get; set; }
    public string FinancialStatusName => FinancialStatus switch
    {
        FinancialStatus.Draft => "مفتوح",
        FinancialStatus.Reviewed => "مراجَع",
        FinancialStatus.Posted => "مرحّل",
        FinancialStatus.Archived => "مؤرشف",
        _ => "غير معروف"
    };
    public string FinancialStatusColor => FinancialStatus switch
    {
        FinancialStatus.Draft => "#10b981", // Green
        FinancialStatus.Reviewed => "#f59e0b", // Amber
        FinancialStatus.Posted => "#6b7280", // Gray
        FinancialStatus.Archived => "#374151", // Dark Gray
        _ => "#000000"
    };
    public string FinancialStatusIcon => FinancialStatus switch
    {
        FinancialStatus.Draft => "LockOpenVariantOutline",
        FinancialStatus.Reviewed => "EyeCheckOutline",
        FinancialStatus.Posted => "Lock",
        FinancialStatus.Archived => "Archive",
        _ => "HelpCircleOutline"
    };
    public bool IsDraft => FinancialStatus == FinancialStatus.Draft;
    public bool IsPosted => FinancialStatus == FinancialStatus.Posted;
}

public partial class GeneralExpensesViewModel : ObservableObject
{
    private readonly IBankService _bankService;
    private readonly IOwnerDebtService _ownerDebtService;
    private readonly IWagesService _wagesService;
    private readonly ICashLedgerService _cashLedgerService;
    private readonly IPostingService _postingService;
    private readonly ISessionService _sessionService;

    public GeneralExpensesViewModel() : this(
        AppServiceProvider.Resolve<IBankService>(),
        AppServiceProvider.Resolve<IOwnerDebtService>(),
        AppServiceProvider.Resolve<IWagesService>(),
        AppServiceProvider.Resolve<ICashLedgerService>(),
        AppServiceProvider.Resolve<IPostingService>(),
        AppServiceProvider.Resolve<ISessionService>())
    {
    }

    public GeneralExpensesViewModel(
        IBankService bankService,
        IOwnerDebtService ownerDebtService,
        IWagesService wagesService,
        ICashLedgerService cashLedgerService,
        IPostingService postingService,
        ISessionService sessionService)
    {
        _bankService = bankService ?? throw new ArgumentNullException(nameof(bankService));
        _ownerDebtService = ownerDebtService ?? throw new ArgumentNullException(nameof(ownerDebtService));
        _wagesService = wagesService ?? throw new ArgumentNullException(nameof(wagesService));
        _cashLedgerService = cashLedgerService ?? throw new ArgumentNullException(nameof(cashLedgerService));
        _postingService = postingService ?? throw new ArgumentNullException(nameof(postingService));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));

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
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
            System.Diagnostics.Debug.WriteLine($"Error loading bank accounts: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task LoadPartnerNamesAsync()
    {
        try
        {
            var ownerDebtService = _ownerDebtService;
            var names = await ownerDebtService.GetPartnerNamesAsync();
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
            System.Diagnostics.Debug.WriteLine($"Error loading partner names: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task LoadWorkerNamesAsync()
    {
        try
        {
            var service = _wagesService;
            var names = await service.GetUniqueWorkerNamesAsync();
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
            System.Diagnostics.Debug.WriteLine($"Error loading worker names: {ex.Message}");
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
            using var db = new AppDbContext();
            IQueryable<Domain.Entities.GeneralExpense> query;

            if (ShowPostedArchive)
            {
                if (ArchiveLevel == 1 && SelectedArchivedMonth != null)
                {
                    var startFilter = new DateTime(SelectedArchivedMonth.Year, SelectedArchivedMonth.Month, 1);
                    var endFilter = startFilter.AddMonths(1).AddDays(-1);
                    query = db.GeneralExpenses
                        .Where(e => !e.IsDeleted && e.PaymentDate.Date >= startFilter && e.PaymentDate.Date <= endFilter &&
                                   (e.FinancialStatus == FinancialStatus.Posted || e.FinancialStatus == FinancialStatus.Archived));
                }
                else
                {
                    query = db.GeneralExpenses
                        .Where(e => !e.IsDeleted && e.PaymentDate.Date >= DateFrom.Date && e.PaymentDate.Date <= DateTo.Date &&
                                   (e.FinancialStatus == FinancialStatus.Posted || e.FinancialStatus == FinancialStatus.Archived));
                }
            }
            else
            {
                query = db.GeneralExpenses
                    .Where(e => !e.IsDeleted && e.PaymentDate.Date >= DateFrom.Date && e.PaymentDate.Date <= DateTo.Date &&
                               e.FinancialStatus != FinancialStatus.Posted && e.FinancialStatus != FinancialStatus.Archived);
            }

            if (SelectedFilterType.HasValue)
                query = query.Where(e => e.ExpenseType == SelectedFilterType.Value);

            if (SelectedFilterPaymentMethod.HasValue)
                query = query.Where(e => e.PaymentMethod == SelectedFilterPaymentMethod.Value);

            var data = query.OrderByDescending(e => e.PaymentDate).ToList();

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
            MessageBox.Show($"خطأ في تحميل المصاريف العامة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
        var dialog = new Presentation.Views.AddGeneralExpenseDialog
        {
            DataContext = this
        };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SaveExpenseAsync(System.Windows.Window window)
    {
        // --- Validation ---
        if (!InputAmount.HasValue || InputAmount.Value <= 0)
        {
            MessageBox.Show("يرجى إدخال مبلغ صحيح أكبر من صفر.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (InputPaymentDate > DateTime.Today)
        {
            MessageBox.Show("تاريخ الدفع لا يمكن أن يكون في المستقبل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedExpenseType == GeneralExpenseType.Salaries && IsDetailedWage && (SelectedWorkerWagesDetails == null || !SelectedWorkerWagesDetails.Any()))
        {
            MessageBox.Show("يرجى تحديد تفاصيل أجور حضور العمال.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        if (SelectedExpenseType == GeneralExpenseType.Other && string.IsNullOrWhiteSpace(InputCustomExpenseType))
        {
            MessageBox.Show("يرجى كتابة نوع المصروف اليدوي.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedPaymentMethod == PaymentMethodType.BankTransfer && SelectedBankAccountForExpense == null)
        {
            MessageBox.Show("يرجى اختيار الحساب البنكي للدفع عن طريق التحويل البنكي.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedPaymentMethod == PaymentMethodType.PersonalPartner && string.IsNullOrWhiteSpace(InputPartnerName))
        {
            MessageBox.Show("يرجى إدخال أو تحديد اسم الشريك الممول للمصروف الشخصي.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var db = new AppDbContext();
            var bankService = _bankService;
            var ownerDebtService = _ownerDebtService;

            if (EditingId.HasValue)
            {
                // --- تعديل ---
                var existing = db.GeneralExpenses.Find(EditingId.Value);
                if (existing != null)
                {
                    if (existing.FinancialStatus == FinancialStatus.Posted || existing.FinancialStatus == FinancialStatus.Archived)
                    {
                        MessageBox.Show("لا يمكن تعديل مصروف مرحّل مالياً.", "منع التعديل", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // إدارة الحركة البنكية القديمة (حذفها أولاً وإعادة بنائها إذا تطلب الأمر)
                    await bankService.DeleteTransactionBySourceAsync("GeneralExpense", existing.Id);

                    // حذف ديون المالك القديمة المرتبطة بهذا المصروف
                    var oldDebts = await db.OwnerDebts.Where(d => d.SourceType == "GeneralExpense" && d.SourceId == existing.Id && !d.IsDeleted).ToListAsync();
                    foreach (var debt in oldDebts)
                    {
                        await ownerDebtService.DeleteDebtAsync(debt.Id);
                    }

                    // عكس الحركة النقدية القديمة إن وجدت
                    var cashLedgerService = _cashLedgerService;
                    var oldCashMovement = await db.CashMovements.FindLiveForSourceAsync(SourceTypes.GeneralExpense, existing.Id);
                    if (oldCashMovement != null)
                    {
                        await cashLedgerService.ReverseMovementAsync(oldCashMovement.Id, "تعديل المصروف العام");
                    }

                    // حذف كافة حركات العمال القديمة المرتبطة بهذا المصروف
                    var oldWorkerTxs = await db.WorkerTransactions.Where(t => t.GeneralExpenseId == existing.Id && !t.IsDeleted).ToListAsync();
                    foreach (var tx in oldWorkerTxs)
                    {
                        tx.IsDeleted = true;
                        tx.UpdatedAt = DateTime.UtcNow;
                    }
                    await db.SaveChangesAsync();

                    existing.ExpenseType = SelectedExpenseType;
                    existing.CustomExpenseName = (SelectedExpenseType == GeneralExpenseType.Other) ? InputCustomExpenseType.Trim() : null;
                    existing.Amount = InputAmount.Value;
                    existing.PaymentDate = InputPaymentDate;
                    existing.PaymentMethod = SelectedPaymentMethod;
                    existing.Description = InputDescription;
                    existing.WorkerName = (SelectedExpenseType == GeneralExpenseType.Salaries) ? (IsDetailedWage ? "[متعدد]" : (!string.IsNullOrEmpty(InputWorkerName) ? InputWorkerName : null)) : null;
                    existing.WorkerId = (SelectedExpenseType == GeneralExpenseType.Salaries && !IsDetailedWage) ? InputWorkerId : null;
                    
                    if (SelectedPaymentMethod == PaymentMethodType.BankTransfer && SelectedBankAccountForExpense != null)
                    {
                        existing.BankAccountId = SelectedBankAccountForExpense.Id;
                    }
                    else
                    {
                        existing.BankAccountId = null;
                    }

                    existing.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();

                    // إعادة إنشاء حركات العمال التفصيلية الجديدة
                    if (SelectedExpenseType == GeneralExpenseType.Salaries && IsDetailedWage && SelectedWorkerWagesDetails != null)
                    {
                        foreach (var d in SelectedWorkerWagesDetails)
                        {
                            if (d.IsAttended)
                            {
                                var accrualTx = new WorkerTransaction
                                {
                                    WorkerId = d.WorkerId,
                                    WorkerName = d.WorkerName,
                                    TransactionDate = InputPaymentDate,
                                    Type = WorkerTransactionType.WageAccrual,
                                    DebitAmount = 0m,
                                    CreditAmount = d.ActualWage,
                                    GeneralExpenseId = existing.Id,
                                    Notes = string.IsNullOrWhiteSpace(d.Notes) ? $"استحقاق حضور - مصروف عام" : $"استحقاق: {d.Notes}"
                                };
                                db.WorkerTransactions.Add(accrualTx);

                                if (d.AmountPaid > 0)
                                {
                                    var payTx = new WorkerTransaction
                                    {
                                        WorkerId = d.WorkerId,
                                        WorkerName = d.WorkerName,
                                        TransactionDate = InputPaymentDate,
                                        Type = WorkerTransactionType.Payment,
                                        DebitAmount = d.AmountPaid,
                                        CreditAmount = 0m,
                                        GeneralExpenseId = existing.Id,
                                        Notes = string.IsNullOrWhiteSpace(d.Notes) ? $"سداد أجر - مصروف عام" : $"سداد: {d.Notes}"
                                    };
                                    db.WorkerTransactions.Add(payTx);
                                }
                            }

                            if (d.Advance > 0)
                            {
                                var advTx = new WorkerTransaction
                                {
                                    WorkerId = d.WorkerId,
                                    WorkerName = d.WorkerName,
                                    TransactionDate = InputPaymentDate,
                                    Type = WorkerTransactionType.Advance,
                                    DebitAmount = d.Advance,
                                    CreditAmount = 0m,
                                    GeneralExpenseId = existing.Id,
                                    Notes = string.IsNullOrWhiteSpace(d.Notes) ? $"سلفة - مصروف عام" : $"سلفة: {d.Notes}"
                                };
                                db.WorkerTransactions.Add(advTx);
                            }

                            if (d.Deduction > 0)
                            {
                                var dedTx = new WorkerTransaction
                                {
                                    WorkerId = d.WorkerId,
                                    WorkerName = d.WorkerName,
                                    TransactionDate = InputPaymentDate,
                                    Type = WorkerTransactionType.Deduction,
                                    DebitAmount = d.Deduction,
                                    CreditAmount = 0m,
                                    GeneralExpenseId = existing.Id,
                                    Notes = string.IsNullOrWhiteSpace(d.Notes) ? $"خصم وغرامة - مصروف عام" : $"خصم: {d.Notes}"
                                };
                                db.WorkerTransactions.Add(dedTx);
                            }
                        }
                        await db.SaveChangesAsync();
                    }

                    // تسجيل الحركة البنكية الجديدة
                    if (SelectedPaymentMethod == PaymentMethodType.BankTransfer && SelectedBankAccountForExpense != null)
                    {
                        var typeName = (SelectedExpenseType == GeneralExpenseType.Other) ? InputCustomExpenseType : GetExpenseTypeName(SelectedExpenseType);
                        var notes = $"مصروف عام: {typeName}" + (string.IsNullOrEmpty(InputDescription) ? "" : $" | {InputDescription}");
                        await bankService.RecordTransactionAsync(
                            SelectedBankAccountForExpense.Id,
                            BankTransactionType.ExpensePayment,
                            InputAmount.Value,
                            InputReferenceNumber,
                            notes,
                            "GeneralExpense",
                            existing.Id,
                            InputPaymentDate
                        );
                    }
                    else if (SelectedPaymentMethod == PaymentMethodType.PersonalPartner && !string.IsNullOrEmpty(InputPartnerName))
                    {
                        var typeName = (SelectedExpenseType == GeneralExpenseType.Other) ? InputCustomExpenseType : GetExpenseTypeName(SelectedExpenseType);
                        var notes = $"مصروف عام شخصي: {typeName}" + (string.IsNullOrEmpty(InputDescription) ? "" : $" | {InputDescription}");
                        await ownerDebtService.RecordDebtAsync(
                            InputPartnerName.Trim(),
                            InputAmount.Value,
                            SelectedExpenseType.ToString(),
                            notes,
                            InputPaymentDate,
                            "GeneralExpense",
                            existing.Id
                        );
                    }
                    else if (SelectedPaymentMethod == PaymentMethodType.Cash)
                    {
                        var typeName = (SelectedExpenseType == GeneralExpenseType.Other) ? InputCustomExpenseType : GetExpenseTypeName(SelectedExpenseType);
                        var notes = $"مصروف عام: {typeName}" + (string.IsNullOrEmpty(InputDescription) ? "" : $" | {InputDescription}");
                        await cashLedgerService.RecordMovementAsync(
                            CashMovementType.CashOut,
                            InputAmount.Value,
                            "GeneralExpense",
                            existing.Id,
                            notes,
                            InputPaymentDate
                        );
                    }

                    MessageBox.Show("تم تعديل المصروف بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                // --- إضافة ---
                var expense = new Domain.Entities.GeneralExpense
                {
                    ExpenseType = SelectedExpenseType,
                    CustomExpenseName = (SelectedExpenseType == GeneralExpenseType.Other) ? InputCustomExpenseType.Trim() : null,
                    Amount = InputAmount.Value,
                    PaymentDate = InputPaymentDate,
                    PaymentMethod = SelectedPaymentMethod,
                    Description = InputDescription,
                    WorkerName = (SelectedExpenseType == GeneralExpenseType.Salaries) ? (IsDetailedWage ? "[متعدد]" : (!string.IsNullOrEmpty(InputWorkerName) ? InputWorkerName : null)) : null,
                    WorkerId = (SelectedExpenseType == GeneralExpenseType.Salaries && !IsDetailedWage) ? InputWorkerId : null,
                    BankAccountId = (SelectedPaymentMethod == PaymentMethodType.BankTransfer && SelectedBankAccountForExpense != null) ? SelectedBankAccountForExpense.Id : null
                };
                db.GeneralExpenses.Add(expense);
                await db.SaveChangesAsync();

                // حفظ حركات العمال التفصيلية
                if (SelectedExpenseType == GeneralExpenseType.Salaries && IsDetailedWage && SelectedWorkerWagesDetails != null)
                {
                    foreach (var d in SelectedWorkerWagesDetails)
                    {
                        if (d.IsAttended)
                        {
                            var accrualTx = new WorkerTransaction
                            {
                                WorkerId = d.WorkerId,
                                WorkerName = d.WorkerName,
                                TransactionDate = InputPaymentDate,
                                Type = WorkerTransactionType.WageAccrual,
                                DebitAmount = 0m,
                                CreditAmount = d.ActualWage,
                                GeneralExpenseId = expense.Id,
                                Notes = string.IsNullOrWhiteSpace(d.Notes) ? $"استحقاق حضور - مصروف عام" : $"استحقاق: {d.Notes}"
                            };
                            db.WorkerTransactions.Add(accrualTx);

                            if (d.AmountPaid > 0)
                            {
                                var payTx = new WorkerTransaction
                                {
                                    WorkerId = d.WorkerId,
                                    WorkerName = d.WorkerName,
                                    TransactionDate = InputPaymentDate,
                                    Type = WorkerTransactionType.Payment,
                                    DebitAmount = d.AmountPaid,
                                    CreditAmount = 0m,
                                    GeneralExpenseId = expense.Id,
                                    Notes = string.IsNullOrWhiteSpace(d.Notes) ? $"سداد أجر - مصروف عام" : $"سداد: {d.Notes}"
                                };
                                db.WorkerTransactions.Add(payTx);
                            }
                        }

                        if (d.Advance > 0)
                        {
                            var advTx = new WorkerTransaction
                            {
                                WorkerId = d.WorkerId,
                                WorkerName = d.WorkerName,
                                TransactionDate = InputPaymentDate,
                                Type = WorkerTransactionType.Advance,
                                DebitAmount = d.Advance,
                                CreditAmount = 0m,
                                GeneralExpenseId = expense.Id,
                                Notes = string.IsNullOrWhiteSpace(d.Notes) ? $"سلفة - مصروف عام" : $"سلفة: {d.Notes}"
                            };
                            db.WorkerTransactions.Add(advTx);
                        }

                        if (d.Deduction > 0)
                        {
                            var dedTx = new WorkerTransaction
                            {
                                WorkerId = d.WorkerId,
                                WorkerName = d.WorkerName,
                                TransactionDate = InputPaymentDate,
                                Type = WorkerTransactionType.Deduction,
                                DebitAmount = d.Deduction,
                                CreditAmount = 0m,
                                GeneralExpenseId = expense.Id,
                                Notes = string.IsNullOrWhiteSpace(d.Notes) ? $"خصم وغرامة - مصروف عام" : $"خصم: {d.Notes}"
                            };
                            db.WorkerTransactions.Add(dedTx);
                        }
                    }
                    await db.SaveChangesAsync();
                }

                // تسجيل الحركة البنكية
                if (SelectedPaymentMethod == PaymentMethodType.BankTransfer && SelectedBankAccountForExpense != null)
                {
                    var typeName = (SelectedExpenseType == GeneralExpenseType.Other) ? InputCustomExpenseType : GetExpenseTypeName(SelectedExpenseType);
                    var notes = $"مصروف عام: {typeName}" + (string.IsNullOrEmpty(InputDescription) ? "" : $" | {InputDescription}");
                    await bankService.RecordTransactionAsync(
                        SelectedBankAccountForExpense.Id,
                        BankTransactionType.ExpensePayment,
                        InputAmount.Value,
                        InputReferenceNumber,
                        notes,
                        "GeneralExpense",
                        expense.Id,
                        InputPaymentDate
                    );
                }
                else if (SelectedPaymentMethod == PaymentMethodType.PersonalPartner && !string.IsNullOrEmpty(InputPartnerName))
                {
                    var typeName = (SelectedExpenseType == GeneralExpenseType.Other) ? InputCustomExpenseType : GetExpenseTypeName(SelectedExpenseType);
                    var notes = $"مصروف عام شخصي: {typeName}" + (string.IsNullOrEmpty(InputDescription) ? "" : $" | {InputDescription}");
                    await ownerDebtService.RecordDebtAsync(
                        InputPartnerName.Trim(),
                        InputAmount.Value,
                        typeName,
                        notes,
                        InputPaymentDate,
                        "GeneralExpense",
                        expense.Id
                    );
                }
                else if (SelectedPaymentMethod == PaymentMethodType.Cash)
                {
                    var cashLedgerService = _cashLedgerService;
                    var typeName = (SelectedExpenseType == GeneralExpenseType.Other) ? InputCustomExpenseType : GetExpenseTypeName(SelectedExpenseType);
                    var notes = $"مصروف عام: {typeName}" + (string.IsNullOrEmpty(InputDescription) ? "" : $" | {InputDescription}");
                    await cashLedgerService.RecordMovementAsync(
                        CashMovementType.CashOut,
                        InputAmount.Value,
                        "GeneralExpense",
                        expense.Id,
                        notes,
                        InputPaymentDate
                    );
                }

                MessageBox.Show("تم إضافة المصروف بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }

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
            MessageBox.Show($"خطأ في حفظ المصروف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task EditExpense(GeneralExpenseDisplayItem item)
    {
        if (item == null) return;
        if (item.ExpenseType == GeneralExpenseType.SupplierPayment)
        {
            MessageBox.Show("لا يمكن تعديل مصروف تسديد الموردين من هنا. يرجى إدارته من شاشة كشف حساب المورد المحدد.", "منع التعديل", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            using var db = new AppDbContext();
            var txs = db.WorkerTransactions
                .Where(t => t.GeneralExpenseId == item.Id && !t.IsDeleted)
                .ToList();

            var grouped = txs.GroupBy(t => t.WorkerId);
            SelectedWorkerWagesDetails = grouped.Select(g => {
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
            SelectedWorkerWagesDetails = new List<WorkerTransactionDetailDto>();
        }

        if (IsDetailedWage)
        {
            using var db = new AppDbContext();
            var txs = db.WorkerTransactions
                .Where(t => t.GeneralExpenseId == item.Id && !t.IsDeleted)
                .ToList();

            var grouped = txs.GroupBy(t => t.WorkerId);
            SelectedWorkerWagesDetails = grouped.Select(g => {
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
            SelectedWorkerWagesDetails = new List<WorkerTransactionDetailDto>();
        }

        InputReferenceNumber = string.Empty;
        InputPartnerName = string.Empty;

        if (SelectedPaymentMethod == PaymentMethodType.BankTransfer)
        {
            using var db = new AppDbContext();
            var bankTx = await db.BankTransactions.FirstOrDefaultAsync(t => t.SourceType == "GeneralExpense" && t.SourceId == item.Id && !t.IsDeleted);
            if (bankTx != null)
            {
                InputReferenceNumber = bankTx.ReferenceNumber ?? string.Empty;
            }
        }
        else if (SelectedPaymentMethod == PaymentMethodType.PersonalPartner)
        {
            using var db = new AppDbContext();
            var debt = await db.OwnerDebts.FirstOrDefaultAsync(t => t.SourceType == "GeneralExpense" && t.SourceId == item.Id && !t.IsDeleted);
            if (debt != null)
            {
                InputPartnerName = debt.PartnerName;
            }
        }

        _ = LoadWorkerNamesAsync();
        _ = LoadPartnerNamesAsync();

        var dialog = new Presentation.Views.AddGeneralExpenseDialog
        {
            DataContext = this
        };
        dialog.ShowDialog();
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

        if (SelectedPaymentMethod == PaymentMethodType.BankTransfer)
        {
            using var db = new AppDbContext();
            var bankTx = await db.BankTransactions.FirstOrDefaultAsync(t => t.SourceType == "GeneralExpense" && t.SourceId == item.Id && !t.IsDeleted);
            if (bankTx != null)
            {
                InputReferenceNumber = bankTx.ReferenceNumber ?? string.Empty;
            }
        }
        else if (SelectedPaymentMethod == PaymentMethodType.PersonalPartner)
        {
            using var db = new AppDbContext();
            var debt = await db.OwnerDebts.FirstOrDefaultAsync(t => t.SourceType == "GeneralExpense" && t.SourceId == item.Id && !t.IsDeleted);
            if (debt != null)
            {
                InputPartnerName = debt.PartnerName;
            }
        }

        _ = LoadWorkerNamesAsync();
        _ = LoadPartnerNamesAsync();

        var dialog = new Presentation.Views.AddGeneralExpenseDialog
        {
            DataContext = this
        };
        dialog.ShowDialog();
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
                using var db = new AppDbContext();
                var bankTx = await db.BankTransactions
                    .Include(t => t.BankAccount)
                    .FirstOrDefaultAsync(t => t.SourceType == "GeneralExpense" && t.SourceId == item.Id && !t.IsDeleted);

                if (bankTx == null && item.ExpenseType == GeneralExpenseType.SupplierPayment)
                {
                    var supplierTx = await db.SupplierTransactions
                        .FirstOrDefaultAsync(t => t.SourceType == TransactionSourceType.ExternalPayment && t.SourceId == item.Id && !t.IsDeleted);
                    
                    if (supplierTx != null)
                    {
                        bankTx = await db.BankTransactions
                            .Include(t => t.BankAccount)
                            .FirstOrDefaultAsync(t => t.SourceType == "SupplierTransaction" && t.SourceId == supplierTx.Id && !t.IsDeleted);
                    }
                }

                if (bankTx != null)
                {
                    string bankName = bankTx.BankAccount != null ? $"{bankTx.BankAccount.BankName} - {bankTx.BankAccount.FriendlyName}" : "غير محدد";
                    string last4 = bankTx.ReferenceNumber ?? "----";
                    bankDetails = $"\n🏦 الحساب البنكي: {bankName}\n🔢 رقم التحويل اخر 4 ارقام: {last4}\n";
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
                using var db = new AppDbContext();
                var debt = await db.OwnerDebts
                    .FirstOrDefaultAsync(t => t.SourceType == "GeneralExpense" && t.SourceId == item.Id && !t.IsDeleted);
                if (debt != null)
                {
                    bankDetails = $"\n👤 الشريك الممول: {debt.PartnerName}\n";
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

            MessageBox.Show(details, "تفاصيل المصروف العام", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء جلب التفاصيل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        ClearForm();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task OpenWorkerWagesDialogAsync()
    {
        var dialog = new Presentation.Views.WorkerWagesDialog();
        var vm = new WorkerWagesDialogViewModel();
        await vm.LoadWorkersAsync(SelectedWorkerWagesDetails);
        dialog.DataContext = vm;
        dialog.Owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (dialog.ShowDialog() == true)
        {
            SelectedWorkerWagesDetails = vm.ResultDetails;
            InputAmount = vm.TotalAmountPaid;
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeleteExpenseAsync(GeneralExpenseDisplayItem item)
    {
        if (item == null) return;

        if (item.ExpenseType == GeneralExpenseType.SupplierPayment)
        {
            MessageBox.Show("لا يمكن حذف مصروف تسديد الموردين من هنا. يرجى إدارته من شاشة كشف حساب المورد المحدد.", "منع الحذف", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"هل تريد حذف مصروف ({item.ExpenseTypeName}) بمبلغ {item.Amount:N2}؟",
            "تأكيد الحذف",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            using var db = new AppDbContext();
            var existing = db.GeneralExpenses.Find(item.Id);
            if (existing != null)
            {
                if (existing.FinancialStatus == FinancialStatus.Posted || existing.FinancialStatus == FinancialStatus.Archived)
                {
                    MessageBox.Show("لا يمكن حذف مصروف مرحّل مالياً.", "منع الحذف", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // حذف الحركة البنكية المرتبطة
                var bankService = _bankService;
                await bankService.DeleteTransactionBySourceAsync("GeneralExpense", existing.Id);

                // حذف ديون المالك المرتبطة
                var ownerDebtService = _ownerDebtService;
                var relatedDebts = await db.OwnerDebts.Where(d => d.SourceType == "GeneralExpense" && d.SourceId == existing.Id && !d.IsDeleted).ToListAsync();
                foreach (var debt in relatedDebts)
                {
                    await ownerDebtService.DeleteDebtAsync(debt.Id);
                }

                // عكس الحركة النقدية المرتبطة إن وجدت
                var cashLedgerService = _cashLedgerService;
                var oldCashMovement = await db.CashMovements.FindLiveForSourceAsync(SourceTypes.GeneralExpense, existing.Id);
                if (oldCashMovement != null)
                {
                    await cashLedgerService.ReverseMovementAsync(oldCashMovement.Id, "حذف المصروف العام");
                }

                existing.IsDeleted = true;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            LoadExpenses();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ في حذف المصروف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task PostExpense(GeneralExpenseDisplayItem item)
    {
        if (item == null || item.IsPosted) return;

        var result = MessageBox.Show(
            $"هل أنت متأكد من ترحيل المصروف بقيمة {item.Amount:N2}؟\nلن تتمكن من تعديله أو حذفه بعد الترحيل.",
            "تأكيد الترحيل", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var postingService = _postingService;
            var currentUserId = _sessionService.CurrentUserId;
            await postingService.PostEntityAsync<Domain.Entities.GeneralExpense>(item.Id, currentUserId);
            
            MessageBox.Show("تم ترحيل المصروف بنجاح. أصبحت الحركة مغلقة مالياً.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadExpenses();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ في الترحيل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task UnpostExpense(GeneralExpenseDisplayItem item)
    {
        if (item == null || !item.IsPosted) return;

        var sessionService = _sessionService;
        if (!sessionService.HasPermission("UnpostFinancial"))
        {
            MessageBox.Show("عذراً، هذا الإجراء متاح فقط للمدير العام.", "صلاحية غير كافية", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // تحقق إذا كان المصروف مرتبط بجلسة مقفلة/مسواة مالياً
        using (var context = new AppDbContext())
        {
            var exp = await context.GeneralExpenses.FindAsync(item.Id);
            if (exp != null && exp.PostingSessionId.HasValue)
            {
                var parentSession = await context.PostingSessions.FindAsync(exp.PostingSessionId.Value);
                if (parentSession != null && 
                    (parentSession.Status == PostingSessionStatus.Settled || parentSession.Status == PostingSessionStatus.ReSettled))
                {
                    MessageBox.Show(
                        "هذا المصروف يقع ضمن فترة مقفلة ومسواة مالياً مسبقاً.\nيجب إلغاء قفل الفترة أولاً من شاشة المبيعات (الكاش الحالي -> أرشيف التسويات) قبل التمكن من فك الترحيل.",
                        "فترة مغلقة ومسواة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }
        }

        // إظهار نافذة إدخال سبب فك الترحيل
        bool dialogResult = false;
        string selectedReason = string.Empty;
        string detailReason = string.Empty;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new Presentation.Views.PeriodUnlockDialog();
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

        var result = MessageBox.Show(
            $"هل أنت متأكد من فك ترحيل المصروف؟\nهذا الإجراء سيتم تسجيله في سجل التدقيق (Audit Log) باسمك.",
            "تأكيد فك الترحيل", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var postingService = _postingService;
            var currentUserId = sessionService.CurrentUserId;
            await postingService.UnpostEntityAsync<Domain.Entities.GeneralExpense>(item.Id, reason, currentUserId);
            
            MessageBox.Show("تم فك الترحيل بنجاح وتم تسجيل العملية في سجل التدقيق.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadExpenses();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ في فك الترحيل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void PrintPdf()
    {
        try
        {
            if (!Expenses.Any())
            {
                MessageBox.Show("لا توجد بيانات للطباعة.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show($"خطأ في إنشاء التقرير: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("يرجى تحديد مصروف واحد على الأقل للترحيل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirmResult = MessageBox.Show(
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
                MessageBox.Show(
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
                MessageBox.Show($"فشلت عملية الترحيل الجماعي:\n{errors}", "خطأ في الترحيل", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ غير متوقع أثناء الترحيل الجماعي: {ex.Message}", "خطأ قاتل", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task PostPeriodAsync()
    {
        DateTime startDate = DateTime.MinValue;
        DateTime endDate = DateTime.MinValue;
        bool dateSelected = false;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new Presentation.Views.DatePeriodSelectionDialog(DateFrom, DateTo);
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
            List<int> expensesInPeriod;
            using (var context = new AppDbContext())
            {
                // جلب المصاريف المفتوحة فقط في هذه الفترة
                expensesInPeriod = await context.GeneralExpenses
                    .Where(e => e.PaymentDate.Date >= startDate && e.PaymentDate.Date <= endDate && e.FinancialStatus == FinancialStatus.Draft)
                    .Select(e => e.Id)
                    .ToListAsync();
            }

            if (!expensesInPeriod.Any())
            {
                MessageBox.Show(
                    $"لا توجد أي مصاريف مفتوحة (غير مرحلة) في الفترة المحددة:\nمن: {startDate:dd-MM-yyyy} إلى: {endDate:dd-MM-yyyy}",
                    "لا توجد بيانات للترحيل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirmResult = MessageBox.Show(
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
                MessageBox.Show(
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
                MessageBox.Show($"فشلت عملية الترحيل الجماعي:\n{errors}", "خطأ في الترحيل", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ غير متوقع أثناء الترحيل الجماعي للفترة: {ex.Message}", "خطأ قاتل", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void LoadArchivedMonths()
    {
        try
        {
            using var db = new AppDbContext();
            var expenses = db.GeneralExpenses
                .Where(e => !e.IsDeleted && 
                           e.PaymentDate.Date >= DateFrom.Date && 
                           e.PaymentDate.Date <= DateTo.Date &&
                           (e.FinancialStatus == FinancialStatus.Posted || e.FinancialStatus == FinancialStatus.Archived))
                .OrderBy(e => e.PaymentDate)
                .ToList();

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
            MessageBox.Show($"خطأ في تحميل كروت أشهر الأرشيف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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

// --- Helper Classes ---
public class ExpenseTypeItem
{
    public string Name { get; set; } = string.Empty;
    public GeneralExpenseType? Value { get; set; }
}

public class PaymentMethodItem
{
    public string Name { get; set; } = string.Empty;
    public PaymentMethodType? Value { get; set; }
}

public class TypeSummaryItem
{
    public string TypeName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int Count { get; set; }
}

public partial class GeneralExpenseMonthCard : ObservableObject
{
    [ObservableProperty]
    private int sequence;

    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;

    [ObservableProperty]
    private decimal totalExpenses;

    [ObservableProperty]
    private int operationsCount;

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
