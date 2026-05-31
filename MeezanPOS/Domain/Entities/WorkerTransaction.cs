using MeezanPOS.Domain.Enums;
using System;

namespace MeezanPOS.Domain.Entities;

/// <summary>
/// دفتر الأستاذ المساعد المالي للعمال لتسجيل كافة القيود المالية (مدين ودائن)
/// </summary>
public class WorkerTransaction : BaseEntity
{
    public int WorkerId { get; set; }
    public Worker? Worker { get; set; }

    public string WorkerName { get; set; } = string.Empty; // لقطة للاسم وقت المعاملة
    public DateTime TransactionDate { get; set; }
    public WorkerTransactionType Type { get; set; }

    // القيود المحاسبية للعملية
    public decimal DebitAmount { get; set; }  // مدين (ما عليه / مسحوبات ودفعات وسلف وخصومات)
    public decimal CreditAmount { get; set; } // دائن (ما له / أجور مستحقة وتسويات إيجابية)

    // الروابط البرمجية القوية (Strongly Typed References) للتدقيق والمنع
    public int? AttendanceId { get; set; }
    public WorkerAttendance? Attendance { get; set; }

    public int? DailyExpenseItemId { get; set; }
    public DailyExpenseItem? DailyExpenseItem { get; set; }

    public int? GeneralExpenseId { get; set; }
    public GeneralExpense? GeneralExpense { get; set; }

    public string? Notes { get; set; }

    // حقول التدقيق للحذف المنطقي
    public string? DeletedReason { get; set; }
    public string? DeletedBy { get; set; }
    public int? DeletedByUserId { get; set; }
    public DateTime? DeletedAt { get; set; }
}
