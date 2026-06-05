using MeezanPOS.Domain.Enums;
using MeezanPOS.Domain.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace MeezanPOS.Domain.Entities;

/// <summary>
/// المصاريف العامة (الثابتة/الشهرية) مثل الإيجار والكهرباء والرواتب
/// </summary>
public class GeneralExpense : BaseEntity, IPostableEntity
{
    public GeneralExpenseType ExpenseType { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public PaymentMethodType PaymentMethod { get; set; } = PaymentMethodType.Cash;
    public string Description { get; set; } = string.Empty;
    public string? CustomExpenseName { get; set; } // اسم المصروف اليدوي (في حال اختيار أخرى)
    public string? WorkerName { get; set; } // اسم العامل (اختياري) في حال كان المصروف أجور تفصيلية
    public int? WorkerId { get; set; } // معرف العامل (اختياري) للربط المالي
    public Worker? Worker { get; set; }
    public int? BankAccountId { get; set; } // معرف الحساب البنكي (اختياري) عند الدفع بتحويل بنكي
    public BankAccount? BankAccount { get; set; }

    // IPostableEntity Implementation
    public FinancialStatus FinancialStatus { get; set; } = FinancialStatus.Draft;
    public DateTime? PostedDate { get; set; }
    public string? PostedByUserId { get; set; }
    public int? PostingSessionId { get; set; }
    
    [ConcurrencyCheck]
    public long RowVersion { get; set; }
}
