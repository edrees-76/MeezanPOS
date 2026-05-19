using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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
    public int Sequence { get; set; }
    public int Id { get; set; }
    public GeneralExpenseType ExpenseType { get; set; }
    public string ExpenseTypeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string PaymentDateDisplay => PaymentDate.ToString("yyyy/MM/dd");
    public PaymentMethodType PaymentMethod { get; set; }
    public string PaymentMethodName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public partial class GeneralExpensesViewModel : ObservableObject
{
    // --- فلاتر البحث ---
    [ObservableProperty]
    private DateTime dateFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    private DateTime dateTo = DateTime.Today;

    [ObservableProperty]
    private GeneralExpenseType? selectedFilterType;

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

    // --- القوائم ---
    public ObservableCollection<ExpenseTypeItem> ExpenseTypes { get; } = new();
    public ObservableCollection<ExpenseTypeItem> FilterExpenseTypes { get; } = new();
    public ObservableCollection<PaymentMethodItem> PaymentMethods { get; } = new();

    // --- ملخصات حسب النوع ---
    public ObservableCollection<TypeSummaryItem> TypeSummaries { get; } = new();

    public GeneralExpensesViewModel()
    {
        InitializeLists();
        LoadExpenses();
    }

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
        ExpenseTypes.Add(new ExpenseTypeItem { Name = "أخرى", Value = GeneralExpenseType.Other });

        // فلتر الأنواع (مع خيار الكل)
        FilterExpenseTypes.Add(new ExpenseTypeItem { Name = "الكل", Value = null });
        foreach (var item in ExpenseTypes)
            FilterExpenseTypes.Add(item);

        // طرق الدفع
        PaymentMethods.Add(new PaymentMethodItem { Name = "نقدي", Value = PaymentMethodType.Cash });
        PaymentMethods.Add(new PaymentMethodItem { Name = "تحويل بنكي", Value = PaymentMethodType.BankTransfer });
        PaymentMethods.Add(new PaymentMethodItem { Name = "شيك", Value = PaymentMethodType.Cheque });
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
        GeneralExpenseType.Other => "أخرى",
        _ => "غير معروف"
    };

    public static string GetPaymentMethodName(PaymentMethodType method) => method switch
    {
        PaymentMethodType.Cash => "نقدي",
        PaymentMethodType.BankTransfer => "تحويل بنكي",
        PaymentMethodType.Cheque => "شيك",
        _ => "غير معروف"
    };

    [RelayCommand]
    private void LoadExpenses()
    {
        try
        {
            using var db = new AppDbContext();
            var query = db.GeneralExpenses
                .Where(e => e.PaymentDate.Date >= DateFrom.Date && e.PaymentDate.Date <= DateTo.Date);

            if (SelectedFilterType.HasValue)
                query = query.Where(e => e.ExpenseType == SelectedFilterType.Value);

            var data = query.OrderByDescending(e => e.PaymentDate).ToList();

            Expenses.Clear();
            int seq = 1;
            foreach (var item in data)
            {
                Expenses.Add(new GeneralExpenseDisplayItem
                {
                    Sequence = seq++,
                    Id = item.Id,
                    ExpenseType = item.ExpenseType,
                    ExpenseTypeName = GetExpenseTypeName(item.ExpenseType),
                    Amount = item.Amount,
                    PaymentDate = item.PaymentDate,
                    PaymentMethod = item.PaymentMethod,
                    PaymentMethodName = GetPaymentMethodName(item.PaymentMethod),
                    Description = item.Description
                });
            }

            TotalAmount = Expenses.Sum(e => e.Amount);
            TotalCount = Expenses.Count;
            UpdateTypeSummaries();
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
        LoadExpenses();
    }

    [RelayCommand]
    private void SaveExpense()
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

        try
        {
            using var db = new AppDbContext();

            if (EditingId.HasValue)
            {
                // --- تعديل ---
                var existing = db.GeneralExpenses.Find(EditingId.Value);
                if (existing != null)
                {
                    existing.ExpenseType = SelectedExpenseType;
                    existing.Amount = InputAmount.Value;
                    existing.PaymentDate = InputPaymentDate;
                    existing.PaymentMethod = SelectedPaymentMethod;
                    existing.Description = InputDescription;
                    existing.UpdatedAt = DateTime.Now;
                    db.SaveChanges();
                    MessageBox.Show("تم تعديل المصروف بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                // --- إضافة ---
                var expense = new Domain.Entities.GeneralExpense
                {
                    ExpenseType = SelectedExpenseType,
                    Amount = InputAmount.Value,
                    PaymentDate = InputPaymentDate,
                    PaymentMethod = SelectedPaymentMethod,
                    Description = InputDescription
                };
                db.GeneralExpenses.Add(expense);
                db.SaveChanges();
                MessageBox.Show("تم إضافة المصروف بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            ClearForm();
            LoadExpenses();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ في حفظ المصروف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void EditExpense(GeneralExpenseDisplayItem item)
    {
        if (item == null) return;
        IsEditing = true;
        EditingId = item.Id;
        SelectedExpenseType = item.ExpenseType;
        InputAmount = item.Amount;
        InputPaymentDate = item.PaymentDate;
        SelectedPaymentMethod = item.PaymentMethod;
        InputDescription = item.Description;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        ClearForm();
    }

    [RelayCommand]
    private void DeleteExpense(GeneralExpenseDisplayItem item)
    {
        if (item == null) return;

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
                existing.IsDeleted = true;
                existing.UpdatedAt = DateTime.Now;
                db.SaveChanges();
            }
            LoadExpenses();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ في حذف المصروف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"تقرير_المصاريف_العامة_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

            report.GeneratePdf(filePath);

            var openResult = MessageBox.Show(
                "تم إنشاء التقرير بنجاح. هل تريد فتحه؟",
                "نجاح",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (openResult == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ في إنشاء التقرير: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearForm()
    {
        IsEditing = false;
        EditingId = null;
        SelectedExpenseType = GeneralExpenseType.Rent;
        InputAmount = null;
        InputPaymentDate = DateTime.Today;
        SelectedPaymentMethod = PaymentMethodType.Cash;
        InputDescription = string.Empty;
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
    public PaymentMethodType Value { get; set; }
}

public class TypeSummaryItem
{
    public string TypeName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int Count { get; set; }
}
