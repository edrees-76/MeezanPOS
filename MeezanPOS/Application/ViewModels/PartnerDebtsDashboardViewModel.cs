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
    [ObservableProperty] private string _selectedStatusFilter = "الكل";
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
    partial void OnSelectedStatusFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = Partners.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(p => p.PartnerName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(SelectedStatusFilter) && SelectedStatusFilter != "الكل")
        {
            if (SelectedStatusFilter == "المطعم مدين للشريك")
            {
                filtered = filtered.Where(p => p.BalanceDirection == PartnerBalanceDirection.RestaurantOwesPartner);
            }
            else if (SelectedStatusFilter == "الشريك مدين للمطعم")
            {
                filtered = filtered.Where(p => p.BalanceDirection == PartnerBalanceDirection.PartnerOwesRestaurant);
            }
            else if (SelectedStatusFilter == "تمت التسوية")
            {
                filtered = filtered.Where(p => p.BalanceDirection == PartnerBalanceDirection.Settled);
            }
        }

        var list = filtered.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            list[i].SequenceNumber = i + 1;
        }

        FilteredPartners = new ObservableCollection<PartnerSummaryDto>(list);
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
