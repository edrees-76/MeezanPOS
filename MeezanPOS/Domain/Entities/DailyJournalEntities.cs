using MeezanPOS.Domain.Enums;
using System;
using System.Collections.Generic;

namespace MeezanPOS.Domain.Entities;

/// <summary>
/// الحركة اليومية - تسجيل ملخص يوم عمل كامل أو وردية
/// </summary>
public class DailyJournal : BaseEntity
{
    public DateTime JournalDate { get; set; }
    public ShiftType ShiftType { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string ShiftName => ShiftType == MeezanPOS.Domain.Enums.ShiftType.FirstShift ? "صباحية" : ShiftType == MeezanPOS.Domain.Enums.ShiftType.SecondShift ? "مسائية" : "يوم كامل";
    public string EmployeeName { get; set; } = string.Empty;
    public string? Notes { get; set; }

    // المبالغ الأساسية
    public decimal CashFloat { get; set; }           // مبلغ الصرف (الفكة)
    public decimal TotalSales { get; set; }           // إجمالي المبيعات من منظومة الكاشير
    public decimal BankingTotal { get; set; }          // إجمالي الخدمات المصرفية (من الدفتر)
    
    // الحسابات التلقائية
    public decimal CashSales => TotalSales - BankingTotal;  // المبيعات النقدية = الإجمالي - المصرفية
    // المرتجعات والطلبات المجانية
    public decimal ReturnsTotal { get; set; }
    public decimal FreeOrdersTotal { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal TotalAdjustments => ReturnsTotal + FreeOrdersTotal;

    // المصروفات
    public decimal TotalExpenses { get; set; }         // إجمالي المصروفات النثرية

    // المطابقة النهائية
    public decimal ExpectedCash => CashFloat + CashSales - TotalExpenses - ReturnsTotal;  // النقد المتوقع
    public decimal ActualCash { get; set; }            // النقد الفعلي المستلم
    public decimal Difference => ActualCash - ExpectedCash;  // الفرق (عجز أو زيادة)

    // تفاصيل المصروفات
    public ICollection<DailyExpenseItem> ExpenseItems { get; set; } = new List<DailyExpenseItem>();
    
    // تفاصيل الخدمات المصرفية
    public ICollection<BankingItem> BankingItems { get; set; } = new List<BankingItem>();

    // تفاصيل المرتجعات والمجانية
    public ICollection<OrderAdjustmentItem> Adjustments { get; set; } = new List<OrderAdjustmentItem>();
}

/// <summary>
/// بند مرتجع أو طلب مجاني
/// </summary>
public class OrderAdjustmentItem : BaseEntity
{
    public int? DailyJournalId { get; set; }
    public DailyJournal? DailyJournal { get; set; }

    public bool IsFreeOrder { get; set; } // true للمجاني, false للمرتجع
    public decimal Amount { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// بند مصروف نثري خلال اليوم
/// </summary>
public class DailyExpenseItem : BaseEntity
{
    public int DailyJournalId { get; set; }
    public DailyJournal? DailyJournal { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Category { get; set; }
}

/// <summary>
/// بند عملية مصرفية (بطاقة/شبكة)
/// </summary>
public class BankingItem : BaseEntity
{
    public int DailyJournalId { get; set; }
    public DailyJournal? DailyJournal { get; set; }

    public decimal Amount { get; set; }
    public string? Description { get; set; }
}
