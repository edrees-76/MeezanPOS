using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Application.Interfaces;

public class WorkerWageSummary
{
    public int WorkerId { get; set; }
    public string WorkerName { get; set; } = string.Empty;
    public decimal DailyWage { get; set; }
    public bool IsActive { get; set; }
    public decimal TotalAccrued { get; set; } // إجمالي مستحقات (له) - Credit
    public decimal TotalPaid { get; set; }    // إجمالي مدفوعات وسلف وخصومات (عليه) - Debit
    public decimal Balance => TotalAccrued - TotalPaid; // الرصيد المتبقي
    public string Notes { get; set; } = string.Empty;
    public DateTime? LastActivity { get; set; }
}

public class WorkerLedgerEntry
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Source { get; set; } = string.Empty; // "حضور", "مصروف عام", "وردية", "سلفة", "تعديل"
    public string Description { get; set; } = string.Empty;
    public decimal AccruedAmount { get; set; } // مبلغ مستحق (له) - Credit
    public decimal PaidAmount { get; set; }    // مبلغ مدفوع (عليه) - Debit
    public decimal BalanceAfter { get; set; }  // الرصيد التراكمي بعد الحركة
    public string Notes { get; set; } = string.Empty;
}

public interface IWagesService
{
    // --- إدارة بيانات العمال (Worker CRUD) ---
    Task<List<Worker>> GetAllWorkersAsync(bool includeInactive = false);
    Task<Worker?> GetWorkerByIdAsync(int id);
    Task SaveWorkerAsync(Worker worker);
    Task ToggleWorkerActiveStatusAsync(int workerId);
    Task DeleteWorkerAsync(int workerId);

    // --- الحضور والغياب (Attendance) ---
    Task<List<WorkerAttendance>> GetAttendanceForDateAsync(DateTime date);
    Task SaveAttendanceBatchAsync(List<WorkerAttendance> attendances);

    // --- العمليات المالية والأستاذ المساعد (Transactions & Ledger) ---
    Task RecordTransactionAsync(WorkerTransaction transaction);
    Task DeleteTransactionAsync(int transactionId);
    Task<List<WorkerLedgerEntry>> GetWorkerLedgerAsync(int workerId);
    Task<List<WorkerWageSummary>> GetWorkerSummariesAsync();
    
    // التوافق التاريخي مع بقية أجزاء النظام
    Task<List<string>> GetUniqueWorkerNamesAsync();
}
