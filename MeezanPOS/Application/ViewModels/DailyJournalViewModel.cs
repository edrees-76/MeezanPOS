using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Domain.Enums;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using QuestPDF.Fluent;

namespace MeezanPOS.Application.ViewModels;

public partial class OrderAdjustmentItemViewModel : ObservableObject
{
    [ObservableProperty]
    private int sequenceNumber;

    [ObservableProperty]
    private decimal? amount;

    [ObservableProperty]
    private string invoiceNumber = string.Empty;

    [ObservableProperty]
    private string notes = string.Empty;
}

public partial class WorkerWageItem : ObservableObject
{
    [ObservableProperty]
    private int sequenceNumber;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private decimal? wage;
}

public partial class ExpenseItemViewModel : ObservableObject
{
    [ObservableProperty]
    private int sequenceNumber;

    [ObservableProperty]
    private string expenseType = string.Empty;

    [ObservableProperty]
    private decimal? amount;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private string supplierName = string.Empty;

    [ObservableProperty]
    private string invoiceNumber = string.Empty;

    [ObservableProperty]
    private string workerName = string.Empty;

    [ObservableProperty]
    private string notes = string.Empty;

    // --- تفصيل أجور العمال ---
    [ObservableProperty]
    private bool isDetailedWage;  // هل يريد تفصيل العمال؟

    public ObservableCollection<WorkerWageItem> Workers { get; } = new();

    public decimal WorkersTotal => Workers.Sum(w => w.Wage ?? 0);

    [RelayCommand]
    private void AddWorker()
    {
        var worker = new WorkerWageItem
        {
            SequenceNumber = Workers.Count + 1
        };
        worker.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(WorkerWageItem.Wage))
            {
                Amount = WorkersTotal;
                OnPropertyChanged(nameof(WorkersTotal));
            }
        };
        Workers.Add(worker);
    }

    [RelayCommand]
    private void RemoveWorker(WorkerWageItem worker)
    {
        Workers.Remove(worker);
        for (int i = 0; i < Workers.Count; i++)
            Workers[i].SequenceNumber = i + 1;
        Amount = Workers.Count > 0 ? WorkersTotal : null;
        OnPropertyChanged(nameof(WorkersTotal));
    }

    partial void OnIsDetailedWageChanged(bool value)
    {
        if (value && Workers.Count == 0)
        {
            AddWorker();
        }
        if (!value)
        {
            Amount = null;  // يرجع للإدخال اليدوي
        }
    }

    // --- حقول الإظهار/الإخفاء حسب النوع ---
    // أي نوع غير (مشتريات/دفعة فاتورة/أجرة عامل) يعتبر بسيط (مبلغ + وصف)
    public bool IsSimpleExpense => !string.IsNullOrEmpty(ExpenseType) && !IsPurchase && !IsInvoicePayment && !IsWorkerWage;
    public bool IsPurchase => ExpenseType == "مشتريات";
    public bool IsInvoicePayment => ExpenseType == "دفعة فاتورة";
    public bool IsWorkerWage => ExpenseType == "أجرة عامل";
    public bool HasSupplier => IsPurchase || IsInvoicePayment;
    public bool HasNotes => IsPurchase || IsInvoicePayment;

    partial void OnExpenseTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSimpleExpense));
        OnPropertyChanged(nameof(IsPurchase));
        OnPropertyChanged(nameof(IsInvoicePayment));
        OnPropertyChanged(nameof(IsWorkerWage));
        OnPropertyChanged(nameof(HasSupplier));
        OnPropertyChanged(nameof(HasNotes));
        // إعادة تعيين التفصيل عند تغيير النوع
        if (!IsWorkerWage)
        {
            IsDetailedWage = false;
        }
    }
}

public partial class BankingItemViewModel : ObservableObject
{
    [ObservableProperty]
    private int sequenceNumber;  // رقم التسلسل

    [ObservableProperty]
    private decimal? amount;

    [ObservableProperty]
    private string invoiceNumber = string.Empty;  // رقم فاتورة المطعم

    [ObservableProperty]
    private string bankName = string.Empty;  // اسم المصرف

    [ObservableProperty]
    private string last4Digits = string.Empty;  // آخر 4 أرقام من عملية الخدمة
}

public partial class DailyJournalViewModel : ObservableObject
{
    // --- معلومات الوردية ---
    [ObservableProperty]
    private DateTime journalDate = DateTime.Today;

    [ObservableProperty]
    private int selectedShiftIndex = 0; // 0=أولى, 1=ثانية, 2=يوم كامل

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

    // --- المرتجعات والطلبات المجانية ---
    public ObservableCollection<OrderAdjustmentItemViewModel> Returns { get; } = new();
    public ObservableCollection<OrderAdjustmentItemViewModel> FreeOrders { get; } = new();

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
    public decimal RegularExpensesTotal => ExpenseItems.Where(e => e.ExpenseType == "مصروف عادي").Sum(e => e.Amount ?? 0);
    public decimal PurchasesTotal => ExpenseItems.Where(e => e.ExpenseType == "مشتريات").Sum(e => e.Amount ?? 0);
    public decimal InvoicePaymentsTotal => ExpenseItems.Where(e => e.ExpenseType == "دفعة فاتورة").Sum(e => e.Amount ?? 0);
    public decimal WorkerWagesTotal => ExpenseItems.Where(e => e.ExpenseType == "أجرة عامل").Sum(e => e.Amount ?? 0);
    public decimal OtherExpensesTotal => ExpenseItems.Where(e =>
        !string.IsNullOrEmpty(e.ExpenseType) &&
        e.ExpenseType != "مصروف عادي" && e.ExpenseType != "مشتريات" &&
        e.ExpenseType != "دفعة فاتورة" && e.ExpenseType != "أجرة عامل"
    ).Sum(e => e.Amount ?? 0);

    public bool HasRegularExpenses => RegularExpensesTotal > 0;
    public bool HasPurchases => PurchasesTotal > 0;
    public bool HasInvoicePayments => InvoicePaymentsTotal > 0;
    public bool HasWorkerWages => WorkerWagesTotal > 0;
    public bool HasOtherExpenses => OtherExpensesTotal > 0;

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

    // --- القوائم ---
    public ObservableCollection<ExpenseItemViewModel> ExpenseItems { get; } = new();
    public ObservableCollection<BankingItemViewModel> BankingItems { get; } = new();

    // --- أنواع الورديات ---
    public string[] ShiftTypes { get; } = { "الوردية الأولى", "الوردية الثانية", "يوم كامل" };

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

    // --- أنواع المصروفات (الأساسية + يمكن للمستخدم كتابة أي بيان آخر) ---
    public string[] ExpenseTypes { get; } = {
        "مصروف عادي",
        "مشتريات",
        "دفعة فاتورة",
        "أجرة عامل"
    };

    // --- رسائل ---
    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isSaved;

    public DailyJournalViewModel()
    {
        AddExpenseItem();
        AddBankingItem();
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
        OnPropertyChanged(nameof(RegularExpensesTotal));
        OnPropertyChanged(nameof(HasRegularExpenses));
        OnPropertyChanged(nameof(PurchasesTotal));
        OnPropertyChanged(nameof(HasPurchases));
        OnPropertyChanged(nameof(InvoicePaymentsTotal));
        OnPropertyChanged(nameof(HasInvoicePayments));
        OnPropertyChanged(nameof(WorkerWagesTotal));
        OnPropertyChanged(nameof(HasWorkerWages));
        OnPropertyChanged(nameof(OtherExpensesTotal));
        OnPropertyChanged(nameof(HasOtherExpenses));
        OnPropertyChanged(nameof(ExpectedCash));
        OnPropertyChanged(nameof(Difference));
        OnPropertyChanged(nameof(DifferenceStatus));
    }

    // --- أوامر المصروفات ---
    [RelayCommand]
    private void AddExpenseItem()
    {
        var item = new ExpenseItemViewModel
        {
            SequenceNumber = ExpenseItems.Count + 1,
            ExpenseType = "مصروف عادي" // تعيين قيمة افتراضية حتى تظهر الحقول مباشرة
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
        item.PropertyChanged += (s, e) => RefreshCalculations();
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

        try
        {
            using var context = new MeezanPOS.Infrastructure.Data.AppDbContext();
            
            var journal = new MeezanPOS.Domain.Entities.DailyJournal
            {
                JournalDate = JournalDate,
                ShiftType = (MeezanPOS.Domain.Enums.ShiftType)SelectedShiftIndex,
                EmployeeName = EmployeeName,
                Notes = Notes,
                CashFloat = CashFloat ?? 0m,
                TotalSales = TotalSales,
                BankingTotal = EffectiveBankingTotal,
                TotalExpenses = TotalExpenses,
                ReturnsTotal = ReturnsAmount,
                FreeOrdersTotal = FreeOrdersAmount,
                ActualCash = ActualCash ?? 0m,
                CreatedAt = System.DateTime.Now
            };

            // إضافة المصروفات
            foreach (var exp in ExpenseItems)
            {
                if (exp.IsWorkerWage && exp.IsDetailedWage)
                {
                    foreach (var worker in exp.Workers)
                    {
                        if ((worker.Wage ?? 0m) > 0)
                        {
                            journal.ExpenseItems.Add(new MeezanPOS.Domain.Entities.DailyExpenseItem
                            {
                                Amount = worker.Wage ?? 0m,
                                Category = "أجرة عامل",
                                Description = !string.IsNullOrWhiteSpace(worker.Name) ? worker.Name : "أجرة عامل",
                                CreatedAt = System.DateTime.Now
                            });
                        }
                    }
                }
                else
                {
                    if ((exp.Amount ?? 0m) > 0)
                    {
                        string desc = exp.ExpenseType;
                        if (exp.IsPurchase || exp.IsInvoicePayment)
                        {
                            var parts = new[] { exp.SupplierName, exp.InvoiceNumber, exp.Notes }
                                .Where(p => !string.IsNullOrWhiteSpace(p));
                            if (parts.Any()) desc = string.Join(" - ", parts);
                        }
                        else if (exp.IsWorkerWage)
                        {
                            if (!string.IsNullOrWhiteSpace(exp.WorkerName)) desc = exp.WorkerName;
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(exp.Description)) desc = exp.Description;
                        }

                        journal.ExpenseItems.Add(new MeezanPOS.Domain.Entities.DailyExpenseItem
                        {
                            Amount = exp.Amount ?? 0m,
                            Category = exp.ExpenseType,
                            Description = desc,
                            CreatedAt = System.DateTime.Now
                        });
                    }
                }
            }

            // إضافة الخدمات المصرفية
            foreach (var bank in BankingItems)
            {
                if ((bank.Amount ?? 0m) > 0)
                {
                    string bankDesc = bank.BankName;
                    if (!string.IsNullOrWhiteSpace(bank.Last4Digits))
                    {
                        bankDesc += " - " + bank.Last4Digits;
                    }
                    journal.BankingItems.Add(new MeezanPOS.Domain.Entities.BankingItem
                    {
                        Amount = bank.Amount ?? 0m,
                        Description = bankDesc,
                        CreatedAt = System.DateTime.Now
                    });
                }
            }

            // إضافة المرتجعات
            foreach (var ret in Returns)
            {
                if ((ret.Amount ?? 0m) > 0)
                {
                    journal.Adjustments.Add(new MeezanPOS.Domain.Entities.OrderAdjustmentItem
                    {
                        IsFreeOrder = false,
                        Amount = ret.Amount ?? 0m,
                        InvoiceNumber = ret.InvoiceNumber,
                        Notes = ret.Notes,
                        CreatedAt = System.DateTime.Now
                    });
                }
            }

            // إضافة المجاني
            foreach (var free in FreeOrders)
            {
                if ((free.Amount ?? 0m) > 0)
                {
                    journal.Adjustments.Add(new MeezanPOS.Domain.Entities.OrderAdjustmentItem
                    {
                        IsFreeOrder = true,
                        Amount = free.Amount ?? 0m,
                        InvoiceNumber = free.InvoiceNumber,
                        Notes = free.Notes,
                        CreatedAt = System.DateTime.Now
                    });
                }
            }

            context.DailyJournals.Add(journal);
            await context.SaveChangesAsync();

            StatusMessage = "تم حفظ الحركة اليومية بنجاح ✓";
            IsSaved = true;
        }
        catch (System.Exception ex)
        {
            StatusMessage = "حدث خطأ أثناء الحفظ: " + (ex.InnerException?.Message ?? ex.Message);
        }
    }

    [ObservableProperty]
    private bool isViewingMode = false;

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
        EmployeeName = string.Empty;
        Notes = string.Empty;
        CashFloat = null;
        CashSalesInput = null;
        BankingSalesInput = null;
        ActualCash = null;
        
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
    }

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
            var filePath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), $"تقرير_حركة_يومية_{JournalDate:yyyyMMdd}_{System.Guid.NewGuid().ToString().Substring(0, 4)}.pdf");
            
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
        StatusMessage = "وضع العرض - لا يمكن تعديل حركة سابقة";
        
        JournalDate = journal.JournalDate;
        SelectedShiftIndex = journal.ShiftType == ShiftType.FirstShift ? 0 :
                             journal.ShiftType == ShiftType.SecondShift ? 1 : 2;
        EmployeeName = journal.EmployeeName;
        Notes = journal.Notes ?? "";
        CashFloat = journal.CashFloat;
        
        CashSalesInput = journal.TotalSales - journal.BankingTotal;
        BankingSalesInput = journal.BankingTotal;
        ActualCash = journal.ActualCash;

        ExpenseItems.Clear();
        if (journal.ExpenseItems != null)
        {
            foreach (var e in journal.ExpenseItems)
            {
                ExpenseItems.Add(new ExpenseItemViewModel
                {
                    SequenceNumber = ExpenseItems.Count + 1,
                    ExpenseType = e.Category ?? "",
                    Amount = e.Amount,
                    Description = e.Description ?? ""
                });
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
                    Notes = a.Notes ?? ""
                };
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
                string bankName = b.Description ?? "";
                string last4 = "";
                if (bankName.Contains(" - "))
                {
                    var parts = bankName.Split(new[] { " - " }, 2, StringSplitOptions.None);
                    bankName = parts[0];
                    last4 = parts[1];
                }

                BankingItems.Add(new BankingItemViewModel
                {
                    SequenceNumber = BankingItems.Count + 1,
                    Amount = b.Amount,
                    BankName = bankName,
                    InvoiceNumber = "",
                    Last4Digits = last4
                });
            }
        }

        RefreshCalculations();
    }
}
