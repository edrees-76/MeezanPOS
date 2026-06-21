using MeezanPOS.Domain.Enums;
using MeezanPOS.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MeezanPOS.Domain.Entities;

/// <summary>
/// الحركة اليومية - تسجيل ملخص يوم عمل كامل أو وردية
/// </summary>
public class DailyJournal : BaseEntity, IPostableEntity
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
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal CashSales => TotalSales - BankingTotal;  // المبيعات النقدية = الإجمالي - المصرفية
    // المرتجعات والطلبات المجانية
    public decimal ReturnsTotal { get; set; }
    public decimal FreeOrdersTotal { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal TotalAdjustments => ReturnsTotal + FreeOrdersTotal;

    // المصروفات
    public decimal TotalExpenses { get; set; }         // إجمالي المصروفات النثرية

    // المطابقة النهائية
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal ExpectedCash => CashFloat + CashSales - TotalExpenses - ReturnsTotal - FreeOrdersTotal;  // النقد المتوقع
    public decimal ActualCash { get; set; }            // النقد الفعلي المستلم
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal Difference => ActualCash - ExpectedCash;  // الفرق (عجز أو زيادة)

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string DifferenceText => Difference < 0 ? $"عجز {Math.Abs(Difference):N2}" : Difference > 0 ? $"زيادة {Difference:N2}" : "لا يوجد";

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string DifferenceColor => Difference < 0 ? "#ef4444" : Difference > 0 ? "#000000" : "#10b981";

    // تفاصيل المصروفات
    public ICollection<DailyExpenseItem> ExpenseItems { get; set; } = new List<DailyExpenseItem>();
    
    // تفاصيل الخدمات المصرفية
    public ICollection<BankingItem> BankingItems { get; set; } = new List<BankingItem>();

    // تفاصيل المرتجعات والمجانية
    public ICollection<OrderAdjustmentItem> Adjustments { get; set; } = new List<OrderAdjustmentItem>();

    // تقسيم مبيعات الخدمات المصرفية حسب المصرف
    public ICollection<DailyJournalBankSale> BankSales { get; set; } = new List<DailyJournalBankSale>();

    // IPostableEntity Implementation
    public FinancialStatus FinancialStatus { get; set; } = FinancialStatus.Draft;
    public DateTime? PostedDate { get; set; }
    public string? PostedByUserId { get; set; }
    public int? PostingSessionId { get; set; }
    
    [ConcurrencyCheck]
    public long RowVersion { get; set; }
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
    public string? PersonName { get; set; } // اسم الشخص (اختياري)
}

/// <summary>
/// بند مصروف نثري خلال اليوم
/// </summary>
public class DailyExpenseItem : BaseEntity
{
    public int DailyJournalId { get; set; }
    public DailyJournal? DailyJournal { get; set; }

    public int SequenceNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Category { get; set; }
    public string? CategoryName { get; set; }  // اسم النوع بالعربي (مشتريات، غاز...)

    public ExpenseType Type { get; set; } 
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string? SupplierName { get; set; }  // اسم المورد (للموردين غير المسجلين)
    public string? WorkerName { get; set; }    // اسم العامل (اختياري) في حال كان المصروف أجور تفصيلية من الصندوق
    public int? WorkerId { get; set; }        // معرف العامل (اختياري) للربط المالي
    public Worker? Worker { get; set; }
    public string? Notes { get; set; }
    public string? InvoiceNumber { get; set; }
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

    public int? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(50)]
    public string? ReferenceNumber { get; set; }

    public bool IsReconciled { get; set; }
}

/// <summary>
/// تقسيم مبيعات الخدمات المصرفية حسب المصرف في الحركة اليومية
/// </summary>
public class DailyJournalBankSale : BaseEntity
{
    public int DailyJournalId { get; set; }
    public DailyJournal? DailyJournal { get; set; }

    public int? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    [MaxLength(100)]
    public string BankName { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}
