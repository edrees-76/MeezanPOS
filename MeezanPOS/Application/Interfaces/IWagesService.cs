using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Application.Interfaces;

public class WorkerWageSummary
{
    public string WorkerName { get; set; } = string.Empty;
    public decimal TotalAccrued { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Balance => TotalAccrued - TotalPaid; // رصيد العامل (المستحق - المدفوع)
    public DateTime? LastActivity { get; set; }
}

public class WorkerLedgerEntry
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Source { get; set; } = string.Empty; // "حضور", "مصروف عام", "وردية"
    public string Description { get; set; } = string.Empty;
    public decimal AccruedAmount { get; set; } // مبلغ مستحق (له)
    public decimal PaidAmount { get; set; }    // مبلغ مدفوع (عليه)
    public decimal BalanceAfter { get; set; }  // الرصيد بعد الحركة
    public string Notes { get; set; } = string.Empty;
}

public interface IWagesService
{
    Task<List<string>> GetUniqueWorkerNamesAsync();
    Task<List<WorkerWageSummary>> GetWorkerSummariesAsync();
    Task<List<WorkerLedgerEntry>> GetWorkerLedgerAsync(string workerName);
    Task RecordAttendanceAsync(WorkerAttendance attendance);
    Task DeleteAttendanceAsync(int attendanceId);
}
