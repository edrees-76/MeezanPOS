using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace MeezanPOS.Application.ViewModels;

/// <summary>تفاصيل الخدمات المصرفية لورديات يوم الحركة ومطابقتها.</summary>
public partial class BankingServicesViewModel
{
    // --- تفاصيل حركات الخدمات المصرفية اليومية للورديات ---
    private List<BankingItemDetailDto> _allTransactionDetails = new();

    [ObservableProperty]
    private ObservableCollection<BankingItemDetailDto> selectedTransactionDetails = new();

    [ObservableProperty]
    private DateTime selectedTransactionDate;

    [ObservableProperty]
    private decimal selectedTransactionTotalAmount;

    [ObservableProperty]
    private bool hasNoDetails;

    [ObservableProperty]
    private ObservableCollection<string> cashierFilters = new();

    [ObservableProperty]
    private ObservableCollection<string> shiftFilters = new();

    [ObservableProperty]
    private string selectedCashierFilter = "الكل";

    [ObservableProperty]
    private string selectedShiftFilter = "الكل";

    public class ShiftTotalDto
    {
        public string ShiftName { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
    }

    [ObservableProperty]
    private ObservableCollection<ShiftTotalDto> shiftTotals = new();

    partial void OnSelectedCashierFilterChanged(string value) => ApplyFilters();
    partial void OnSelectedShiftFilterChanged(string value) => ApplyFilters();

    private void ApplyFilters()
    {
        var filtered = _allTransactionDetails.AsEnumerable();

        if (SelectedCashierFilter != "الكل" && !string.IsNullOrEmpty(SelectedCashierFilter))
        {
            filtered = filtered.Where(x => x.CashierName == SelectedCashierFilter);
        }

        if (SelectedShiftFilter != "الكل" && !string.IsNullOrEmpty(SelectedShiftFilter))
        {
            filtered = filtered.Where(x => x.ShiftName == SelectedShiftFilter);
        }

        SelectedTransactionDetails = new ObservableCollection<BankingItemDetailDto>(filtered);

        // احتساب إجماليات الورديات الحالية المعروضة
        var totals = SelectedTransactionDetails
            .GroupBy(x => x.ShiftName)
            .Select(g => new ShiftTotalDto
            {
                ShiftName = g.Key,
                TotalAmount = g.Sum(x => x.Amount)
            })
            .ToList();

        ShiftTotals = new ObservableCollection<ShiftTotalDto>(totals);
    }

    [RelayCommand]
    public async Task LoadTransactionDetailsAsync(BankTransaction? transaction)
    {
        if (transaction == null || transaction.Type != BankTransactionType.CardSalesDeposit)
        {
            _allTransactionDetails.Clear();
            SelectedTransactionDetails.Clear();
            SelectedTransactionTotalAmount = 0;
            HasNoDetails = true;
            CashierFilters.Clear();
            ShiftFilters.Clear();
            ShiftTotals.Clear();
            return;
        }

        SelectedTransactionDate = transaction.TransactionDate;
        SelectedTransactionTotalAmount = transaction.Amount;
        SelectedTransactionDetails.Clear();
        HasNoDetails = false;

        IsLoading = true;
        try
        {
            // بنود المعاملات البنكية التفصيلية من الورديات لنفس اليوم ولنفس الحساب
            // خارج خيط الواجهة: مزود SQLite ينفذ الاستعلامات غير المتزامنة بشكل متزامن فعلياً
            var details = await Task.Run(() => _bankingQueries.GetShiftBankingDetailsAsync(transaction.BankAccountId, transaction.TransactionDate));

            _allTransactionDetails = details;

            // ملء قائمة خيارات الكاشيرز
            var cashiers = new List<string> { "الكل" };
            cashiers.AddRange(details.Select(x => x.CashierName).Distinct().OrderBy(x => x));
            CashierFilters = new ObservableCollection<string>(cashiers);

            // ملء قائمة خيارات الورديات
            var shifts = new List<string> { "الكل" };
            shifts.AddRange(details.Select(x => x.ShiftName).Distinct().OrderBy(x => x));
            ShiftFilters = new ObservableCollection<string>(shifts);

            // إعادة تعيين الفلاتر الافتراضية
            SelectedCashierFilter = "الكل";
            SelectedShiftFilter = "الكل";
            OnPropertyChanged(nameof(SelectedCashierFilter));
            OnPropertyChanged(nameof(SelectedShiftFilter));

            ApplyFilters();

            HasNoDetails = !details.Any();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء تحميل تفاصيل الوردية:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleReconciliationAsync(BankingItemDetailDto? item)
    {
        if (item == null) return;
        try
        {
            await Task.Run(() => _bankingQueries.SetReconciledAsync(item.Id, item.IsReconciled));
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء تحديث حالة المطابقة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void PrintDetailsPdf()
    {
        if (SelectedTransactionDetails == null || !SelectedTransactionDetails.Any())
        {
            Dialogs.Show("لا توجد حركات لطباعتها.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var report = new BankingServicesDetailsPdfReport(
                SelectedTransactionDate,
                StatementAccount,
                SelectedTransactionDetails.ToList(),
                ShiftTotals.ToList()
            );

            string bankName = StatementAccount?.DisplayName ?? "حساب بنكي";
            // تنظيف الحروف غير الصالحة لأسماء الملفات في ويندوز
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                bankName = bankName.Replace(c, '_');
            }

            string dateStr = SelectedTransactionDate.ToString("yyyy-MM-dd");
            string timeStr = DateTime.Now.ToString("HH_mm_ss");
            string fileName = $"{bankName} - {dateStr} - {timeStr}.pdf";

            var filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);
            report.GeneratePdf(filePath);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء طباعة تقرير التفاصيل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SelectedCashierFilter = "الكل";
        SelectedShiftFilter = "الكل";
    }

    private static bool IsStatementDepositType(BankTransaction tx) =>
        tx.Type == BankTransactionType.CardSalesDeposit ||
        tx.Type == BankTransactionType.Deposit;

    private static string GetStatementTypeDisplayName(BankTransactionType type) => type switch
    {
        BankTransactionType.CardSalesDeposit => "خدمات مصرفية",
        BankTransactionType.Deposit => "إيداع نقدي",
        BankTransactionType.Withdrawal => "سحب نقدي",
        BankTransactionType.InternalTransfer => "تحويل داخلي",
        BankTransactionType.SupplierPayment => "سداد مورد",
        BankTransactionType.ExpensePayment => "مصروف عام",
        BankTransactionType.OwnerDebtSettlement => "تسوية مالك",
        BankTransactionType.ExchangeDifference => "فروقات",
        _ => "أخرى"
    };
}
