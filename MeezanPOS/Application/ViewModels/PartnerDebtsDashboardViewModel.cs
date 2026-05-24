using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Application.ViewModels;

/// <summary>
/// رسالة إشعار لتحديث داشبورد ديون الشركاء عند التغيير من وحدات أخرى
/// </summary>
public class PartnerDebtsChangedMessage { }
public class OpenPartnerDebtFormMessage { }
public class OpenPartnerSettlementFormMessage { }

public partial class PartnerDebtsDashboardViewModel : ObservableObject, IRecipient<PartnerDebtsChangedMessage>
{
    private readonly IOwnerDebtService _ownerDebtService;

    [ObservableProperty] private ObservableCollection<PartnerSummaryDto> _partners = new();
    [ObservableProperty] private ObservableCollection<PartnerSummaryDto> _filteredPartners = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private decimal _totalDebtsAllPartners;
    [ObservableProperty] private decimal _totalSettlementsAllPartners;
    [ObservableProperty] private decimal _netBalanceAllPartners;
    [ObservableProperty] private bool _isLoading;

    public PartnerDebtsDashboardViewModel(IOwnerDebtService ownerDebtService)
    {
        _ownerDebtService = ownerDebtService;
        WeakReferenceMessenger.Default.Register<PartnerDebtsChangedMessage>(this);
    }

    public void Receive(PartnerDebtsChangedMessage message)
    {
        _ = LoadDashboardAsync();
    }

    public async Task LoadDashboardAsync()
    {
        IsLoading = true;
        try
        {
            var summaries = await _ownerDebtService.GetPartnersSummaryAsync();
            Partners = new ObservableCollection<PartnerSummaryDto>(summaries);

            TotalDebtsAllPartners = summaries.Sum(s => s.DebtsTotal);
            TotalSettlementsAllPartners = summaries.Sum(s => s.SettlementsTotal);
            NetBalanceAllPartners = TotalDebtsAllPartners - TotalSettlementsAllPartners;

            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء تحميل بيانات الشركاء:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredPartners = new ObservableCollection<PartnerSummaryDto>(Partners);
        }
        else
        {
            var filtered = Partners
                .Where(p => p.PartnerName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
            FilteredPartners = new ObservableCollection<PartnerSummaryDto>(filtered);
        }
    }

    [RelayCommand]
    private void RecordDebt()
    {
        WeakReferenceMessenger.Default.Send(new OpenPartnerDebtFormMessage());
    }

    [RelayCommand]
    private void RecordSettlement()
    {
        WeakReferenceMessenger.Default.Send(new OpenPartnerSettlementFormMessage());
    }
}
