using CommunityToolkit.Mvvm.ComponentModel;
using MeezanPOS.Domain.Enums;
using System.Collections.Generic;

namespace MeezanPOS.Application.ViewModels;

// بنود شاشة اليومية: المصروفات، الخدمات المصرفية، مبيعات المصارف، المرتجعات والمجاني.

/// <summary>
/// نموذج بيانات لعرض خيارات الورديات في القائمة المنسدلة
/// </summary>
public record ShiftOption(ShiftType Type, string DisplayName);

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

    [ObservableProperty]
    private string personName = string.Empty;
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
    private int? selectedSupplierId;

    [ObservableProperty]
    private string supplierName = string.Empty;

    [ObservableProperty]
    private string invoiceNumber = string.Empty;

    [ObservableProperty]
    private string workerName = string.Empty;

    [ObservableProperty]
    private int? workerId;

    [ObservableProperty]
    private bool isDetailedWage = false;

    [ObservableProperty]
    private System.Collections.Generic.List<WorkerTransactionDetailDto> selectedWorkerWagesDetails = new();

    [ObservableProperty]
    private string notes = string.Empty;

    partial void OnIsDetailedWageChanged(bool value)
    {
        if (!value)
        {
            WorkerName = string.Empty;
            SelectedWorkerWagesDetails = new();
        }
    }



    // --- حقول الإظهار/الإخفاء حسب النوع ---
    // الأنواع البسيطة: نظافة، صيانة، مواصلات، مصروف نثري، ويومية عامل (مبلغ + وصف فقط)
    public bool IsSimpleExpense => !string.IsNullOrEmpty(ExpenseType) && !IsPurchase && !IsInvoicePayment && !IsSupplierPayment && !IsGas && !IsCoal && !IsBread;
    public bool IsPurchase => ExpenseType == "مشتريات";
    public bool IsInvoicePayment => ExpenseType == "دفعة فاتورة";
    public bool IsWorkerWage => ExpenseType == "أجرة عامل" || ExpenseType == "يومية عامل";
    public bool IsSupplierPayment => ExpenseType == "دفعة مورد";
    public bool IsGas => ExpenseType == "غاز";
    public bool IsCoal => ExpenseType == "فحم";
    public bool IsBread => ExpenseType == "الخبزة";
    // المورد: يظهر للمشتريات والغاز والفحم والخبزة ودفعة المورد ودفعة الفاتورة (للمطابقة مع الموردين)
    public bool HasSupplier => IsPurchase || IsGas || IsCoal || IsBread || IsSupplierPayment || IsInvoicePayment;
    // رقم الفاتورة: يظهر للمشتريات، دفعة مورد، ودفعة فاتورة
    public bool HasInvoiceNumber => IsPurchase || IsSupplierPayment || IsInvoicePayment;
    // الملاحظات: تظهر لكل الأنواع
    public bool HasNotes => !string.IsNullOrEmpty(ExpenseType);

    partial void OnExpenseTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSimpleExpense));
        OnPropertyChanged(nameof(IsPurchase));
        OnPropertyChanged(nameof(IsInvoicePayment));
        OnPropertyChanged(nameof(IsWorkerWage));
        OnPropertyChanged(nameof(IsSupplierPayment));
        OnPropertyChanged(nameof(IsGas));
        OnPropertyChanged(nameof(IsCoal));
        OnPropertyChanged(nameof(IsBread));
        OnPropertyChanged(nameof(HasSupplier));
        OnPropertyChanged(nameof(HasInvoiceNumber));
        OnPropertyChanged(nameof(HasNotes));
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
    private string last4Digits = string.Empty;  // رقم التحويل اخر 4 ارقام من عملية الخدمة

    [ObservableProperty]
    private int? bankAccountId;
}

public partial class BankSaleInputViewModel : ObservableObject
{
    [ObservableProperty]
    private int bankAccountId;

    [ObservableProperty]
    private string bankFriendlyName = string.Empty;

    [ObservableProperty]
    private string bankName = string.Empty;

    [ObservableProperty]
    private decimal? amount;
}
