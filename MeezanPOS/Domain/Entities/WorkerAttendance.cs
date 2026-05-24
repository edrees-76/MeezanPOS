using MeezanPOS.Domain.Enums;
using System;

namespace MeezanPOS.Domain.Entities;

/// <summary>
/// تسجيل حضور العمال واستحقاق أجورهم اليومية (التزامات مستحقة)
/// </summary>
public class WorkerAttendance : BaseEntity
{
    public string WorkerName { get; set; } = string.Empty; // كتابة اسم العامل مباشرة بيدك
    public DateTime WorkDate { get; set; }
    public ShiftType ShiftType { get; set; }
    public decimal AccruedWage { get; set; } // الأجر المستحق عن هذا اليوم/الوردية
    public string? Notes { get; set; }
    public bool IsPaid { get; set; } = false; // هل تم سداد هذا اليوم بالكامل؟
}
