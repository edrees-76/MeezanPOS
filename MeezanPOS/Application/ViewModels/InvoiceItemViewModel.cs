using CommunityToolkit.Mvvm.ComponentModel;

namespace MeezanPOS.Application.ViewModels;

/// <summary>
/// ViewModel لكل سطر (صنف) في تفاصيل فاتورة المشتريات.
/// يحسب القيمة تلقائياً عند تغيير الكمية أو سعر الوحدة.
/// </summary>
public partial class InvoiceItemViewModel : ObservableObject
{
    [ObservableProperty]
    private int sequence;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalValue))]
    private string description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalValue))]
    private decimal quantity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalValue))]
    private decimal unitPrice;

    /// <summary>
    /// القيمة = الكمية × سعر الوحدة (محسوبة تلقائياً)
    /// </summary>
    public decimal TotalValue => Quantity * UnitPrice;

    /// <summary>
    /// الحدث الذي يُطلق عند تغيير أي قيمة تؤثر على الإجمالي
    /// </summary>
    public event Action? OnTotalChanged;

    partial void OnQuantityChanged(decimal value) => OnTotalChanged?.Invoke();
    partial void OnUnitPriceChanged(decimal value) => OnTotalChanged?.Invoke();
}
