using System;
using System.ComponentModel.DataAnnotations.Schema;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Domain.Entities;

public enum CashMovementType
{
    CashIn = 1,   // وارد (+)
    CashOut = 2   // صادر (-)
}

public class CashMovement : BaseEntity
{
    public DateTime TransactionDate { get; set; }  // التاريخ والوقت الفعلي للحركة
    public CashMovementType Type { get; set; }      // نوع الحركة
    public decimal Amount { get; set; }            // القيمة المالية للحركة
    public decimal BalanceAfter { get; set; }       // الرصيد النقدي التراكمي المتبقي بعد الحركة مباشرة (مصدر الحقيقة)
    
    // ربط الحركات بمصادرها
    public string? SourceType { get; set; }         // نوع المصدر: "DailyJournal", "GeneralExpense", "SupplierTransaction"
    public int? SourceId { get; set; }              // معرف الحركة المصدر
    
    public string? Notes { get; set; }              // البيان والتفاصيل
    public bool IsReversed { get; set; }            // هل تم عكس الحركة؟

    [NotMapped]
    public int Sequence { get; set; }               // التسلسل (للعرض فقط)
}
