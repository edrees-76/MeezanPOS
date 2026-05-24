using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Enums;
using Microsoft.Win32;

namespace MeezanPOS.Application.ViewModels;

public partial class PartnerStatementViewModel : ObservableObject
{
    private readonly IOwnerDebtService _ownerDebtService;

    [ObservableProperty] private string _partnerName = string.Empty;
    [ObservableProperty] private ObservableCollection<PartnerStatementEntryDto> _statementEntries = new();
    [ObservableProperty] private decimal _totalDebts;
    [ObservableProperty] private decimal _totalSettlements;
    [ObservableProperty] private decimal _currentBalance;
    [ObservableProperty] private PartnerBalanceDirection _balanceDirection;
    [ObservableProperty] private string _balanceDirectionText = string.Empty;
    [ObservableProperty] private int _transactionCount;
    [ObservableProperty] private bool _isLoading;

    // فلاتر
    [ObservableProperty] private DateTime? _filterDateFrom;
    [ObservableProperty] private DateTime? _filterDateTo;
    [ObservableProperty] private string? _filterTransactionType;

    public PartnerStatementViewModel(IOwnerDebtService ownerDebtService)
    {
        _ownerDebtService = ownerDebtService;
    }

    public async Task LoadStatementAsync(string partnerName)
    {
        PartnerName = partnerName;
        IsLoading = true;
        try
        {
            var entries = await _ownerDebtService.GetPartnerStatementAsync(partnerName);

            // حساب المجاميع الإحصائية من الكشف الكامل
            TotalDebts = entries.Where(e => !e.IsSettlement).Sum(e => e.Amount);
            TotalSettlements = entries.Where(e => e.IsSettlement).Sum(e => e.Amount);
            CurrentBalance = TotalDebts - TotalSettlements;

            BalanceDirection = CurrentBalance > 0
                ? PartnerBalanceDirection.PartnerOwesRestaurant
                : CurrentBalance < 0
                    ? PartnerBalanceDirection.RestaurantOwesPartner
                    : PartnerBalanceDirection.Settled;

            BalanceDirectionText = BalanceDirection switch
            {
                PartnerBalanceDirection.PartnerOwesRestaurant => "الشريك مدين للمطعم",
                PartnerBalanceDirection.RestaurantOwesPartner => "المطعم مدين للشريك",
                _ => "الحسابات متوازنة"
            };

            CurrentBalance = Math.Abs(CurrentBalance);
            TransactionCount = entries.Count;

            // تطبيق الفلاتر على العرض فقط (الحسابات على الكل)
            var filtered = entries.AsEnumerable();

            if (FilterDateFrom.HasValue)
                filtered = filtered.Where(e => e.TransactionDate >= FilterDateFrom.Value);
            if (FilterDateTo.HasValue)
                filtered = filtered.Where(e => e.TransactionDate <= FilterDateTo.Value);
            if (!string.IsNullOrEmpty(FilterTransactionType) && FilterTransactionType != "الكل")
                filtered = filtered.Where(e => e.TransactionType == FilterTransactionType);

            StatementEntries = new ObservableCollection<PartnerStatementEntryDto>(filtered);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء تحميل كشف حساب الشريك:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ApplyFilter()
    {
        await LoadStatementAsync(PartnerName);
    }

    [RelayCommand]
    private async Task ResetFilter()
    {
        FilterDateFrom = null;
        FilterDateTo = null;
        FilterTransactionType = null;
        await LoadStatementAsync(PartnerName);
    }

    [RelayCommand]
    private void ExportToPdf()
    {
        if (StatementEntries == null || !StatementEntries.Any())
        {
            MessageBox.Show("لا توجد حركات للتصدير.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            Filter = "PDF Files | *.pdf",
            FileName = $"كشف_حساب_{PartnerName}_{DateTime.Now:yyyyMMdd}.pdf"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                // حساب رصيد البداية (رصيد ما قبل أول حركة في القائمة المعروضة)
                // ملاحظة: بما أننا نرسل الكشف الكامل، رصيد البداية هو 0 إلا إذا كان هناك رصيد افتتاحي
                // للحفاظ على البساطة، سنعتمد القيم المحسوبة في الفيو موديل
                
                PartnerStatementPdfReport.GeneratePdf(
                    saveFileDialog.FileName,
                    PartnerName,
                    0, // Opening Balance (Placeholder if not tracked separately)
                    TotalSettlements,
                    TotalDebts,
                    CurrentBalance,
                    BalanceDirection,
                    StatementEntries.ToList(),
                    FilterDateFrom ?? StatementEntries.Min(e => e.TransactionDate),
                    FilterDateTo ?? StatementEntries.Max(e => e.TransactionDate)
                );

                MessageBox.Show("تم تصدير كشف الحساب بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ أثناء التصدير:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
