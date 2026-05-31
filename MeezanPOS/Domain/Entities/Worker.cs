using System;

namespace MeezanPOS.Domain.Entities;

/// <summary>
/// تمثيل سجل بيانات العامل الأساسي في النظام
/// </summary>
public class Worker : BaseEntity
{
    public string WorkerName { get; set; } = string.Empty;
    public decimal DailyWage { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}
