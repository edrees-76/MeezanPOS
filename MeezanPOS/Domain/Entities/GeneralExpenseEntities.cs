using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Domain.Entities;

/// <summary>
/// المصاريف العامة (الثابتة/الشهرية) مثل الإيجار والكهرباء والرواتب
/// </summary>
public class GeneralExpense : BaseEntity
{
    public GeneralExpenseType ExpenseType { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public PaymentMethodType PaymentMethod { get; set; } = PaymentMethodType.Cash;
    public string Description { get; set; } = string.Empty;
}
