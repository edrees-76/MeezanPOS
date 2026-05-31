using MeezanPOS.Domain.Enums;
using System;

namespace MeezanPOS.Domain.Entities;

/// <summary>
/// تسجيل حضور العمال واستحقاق أجورهم اليومية
/// </summary>
public class WorkerAttendance : BaseEntity
{
    public int? WorkerId { get; set; }
    public Worker? Worker { get; set; }

    public string WorkerName { get; set; } = string.Empty; // لقطة للاسم وقت التسجيل لضمان ثبات البيانات التاريخية
    public DateTime WorkDate { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;
    public ShiftType ShiftType { get; set; } = ShiftType.FullDay;
    public decimal SnapshotDailyWage { get; set; } // اليومية الفعلية المطبقة لهذا اليوم
    public decimal AccruedWage { get; set; } // الأجر المستحق الفعلي لهذا اليوم (مثال: نصف يوم = اليومية / 2)
    public string? Notes { get; set; }
    public bool IsPaid { get; set; } = false; // هل تم سداد هذا اليوم بالكامل؟

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal DeductionAmount { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal AdvanceDeducted { get; set; }
}
