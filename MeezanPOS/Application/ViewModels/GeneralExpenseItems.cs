using CommunityToolkit.Mvvm.ComponentModel;
using MeezanPOS.Domain.Enums;
using System;

namespace MeezanPOS.Application.ViewModels;

// عناصر عرض شاشة المصروفات العامة: صف المصروف، خيارات الأنواع وطرق الدفع، ملخصات الأنواع وكروت الأشهر.

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
