using CommunityToolkit.Mvvm.ComponentModel;

namespace MeezanPOS.Application.ViewModels;

/// <summary>
/// كائن نقل البيانات (DTO) لعرض تفاصيل مبيعات الخدمات المصرفية لكل وردية وكاشير.
/// </summary>
public partial class BankingItemDetailDto : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    private string cashierName = string.Empty;

    [ObservableProperty]
    private string shiftName = string.Empty;

    [ObservableProperty]
    private decimal amount;

    [ObservableProperty]
    private string invoiceNumber = string.Empty;

    [ObservableProperty]
    private string transferReference = string.Empty;

    [ObservableProperty]
    private bool isReconciled;
}
