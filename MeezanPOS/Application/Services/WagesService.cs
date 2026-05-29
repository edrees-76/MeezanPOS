using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.Services;

public class WagesService : IWagesService
{
    private readonly AppDbContext _context;

    public WagesService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<string>> GetUniqueWorkerNamesAsync()
    {
        // جلب الأسماء الفريدة من سجلات الحضور والمصاريف العامة والمصاريف اليومية
        var namesFromAttendance = await _context.WorkerAttendances
            .Where(w => !w.IsDeleted && !string.IsNullOrWhiteSpace(w.WorkerName))
            .Select(w => w.WorkerName.Trim())
            .Distinct()
            .ToListAsync();

        var namesFromGeneralExpenses = await _context.GeneralExpenses
            .Where(e => !e.IsDeleted && !string.IsNullOrWhiteSpace(e.WorkerName))
            .Select(e => e.WorkerName!.Trim())
            .Distinct()
            .ToListAsync();

        var namesFromDailyExpenses = await _context.DailyExpenseItems
            .Where(e => !e.IsDeleted && !string.IsNullOrWhiteSpace(e.WorkerName))
            .Select(e => e.WorkerName!.Trim())
            .Distinct()
            .ToListAsync();

        return namesFromAttendance
            .Union(namesFromGeneralExpenses)
            .Union(namesFromDailyExpenses)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();
    }

    public async Task<List<WorkerWageSummary>> GetWorkerSummariesAsync()
    {
        // 1. تجميع الاستحقاقات من سجلات الحضور (استعلام خفيف للمستندات ثم التجميع بالذاكرة)
        var attendances = await _context.WorkerAttendances
            .Where(w => !w.IsDeleted && !string.IsNullOrWhiteSpace(w.WorkerName))
            .Select(w => new { w.WorkerName, w.AccruedWage, w.WorkDate })
            .ToListAsync();

        var attendanceGroup = attendances
            .GroupBy(w => w.WorkerName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                WorkerName = g.Key,
                TotalAccrued = g.Sum(x => x.AccruedWage),
                LastActivity = g.Max(x => x.WorkDate)
            })
            .ToList();

        // 2. تجميع المدفوعات من المصاريف العامة (نوع رواتب)
        var generalExpenses = await _context.GeneralExpenses
            .Where(e => !e.IsDeleted && e.ExpenseType == GeneralExpenseType.Salaries && !string.IsNullOrWhiteSpace(e.WorkerName))
            .Select(e => new { e.WorkerName, e.Amount, e.PaymentDate })
            .ToListAsync();

        var generalExpenseGroup = generalExpenses
            .GroupBy(e => e.WorkerName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                WorkerName = g.Key,
                TotalPaid = g.Sum(x => x.Amount),
                LastActivity = g.Max(x => x.PaymentDate)
            })
            .ToList();

        // 3. تجميع المدفوعات من مصاريف الورديات اليومية (نوع أجور عمال)
        var dailyExpenses = await _context.DailyExpenseItems
            .Include(e => e.DailyJournal)
            .Where(e => !e.IsDeleted && e.Type == ExpenseType.WorkerWage && !string.IsNullOrWhiteSpace(e.WorkerName))
            .Select(e => new { e.WorkerName, e.Amount, JournalDate = e.DailyJournal != null ? e.DailyJournal.JournalDate : e.CreatedAt })
            .ToListAsync();

        var dailyExpenseGroup = dailyExpenses
            .GroupBy(e => e.WorkerName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                WorkerName = g.Key,
                TotalPaid = g.Sum(x => x.Amount),
                LastActivity = g.Max(x => x.JournalDate)
            })
            .ToList();

        // دمج النتائج
        var allWorkerNames = attendanceGroup.Select(x => x.WorkerName)
            .Union(generalExpenseGroup.Select(x => x.WorkerName))
            .Union(dailyExpenseGroup.Select(x => x.WorkerName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var summaries = new List<WorkerWageSummary>();

        foreach (var name in allWorkerNames)
        {
            var att = attendanceGroup.FirstOrDefault(x => string.Equals(x.WorkerName, name, StringComparison.OrdinalIgnoreCase));
            var gen = generalExpenseGroup.FirstOrDefault(x => string.Equals(x.WorkerName, name, StringComparison.OrdinalIgnoreCase));
            var daily = dailyExpenseGroup.FirstOrDefault(x => string.Equals(x.WorkerName, name, StringComparison.OrdinalIgnoreCase));

            decimal totalAccrued = att?.TotalAccrued ?? 0;
            decimal totalPaid = (gen?.TotalPaid ?? 0) + (daily?.TotalPaid ?? 0);

            // تحديد آخر تاريخ نشاط
            var dates = new List<DateTime>();
            if (att != null) dates.Add(att.LastActivity);
            if (gen != null) dates.Add(gen.LastActivity);
            if (daily != null) dates.Add(daily.LastActivity);

            summaries.Add(new WorkerWageSummary
            {
                WorkerName = name,
                TotalAccrued = totalAccrued,
                TotalPaid = totalPaid,
                LastActivity = dates.Count > 0 ? dates.Max() : null
            });
        }

        return summaries.OrderByDescending(s => s.Balance != 0).ThenBy(s => s.WorkerName).ToList();
    }

    public async Task<List<WorkerLedgerEntry>> GetWorkerLedgerAsync(string workerName)
    {
        if (string.IsNullOrWhiteSpace(workerName))
            return new List<WorkerLedgerEntry>();

        var trimmedName = workerName.Trim();

        // 1. جلب استحقاقات الحضور
        var attendances = await _context.WorkerAttendances
            .Where(w => !w.IsDeleted && w.WorkerName.Trim().ToLower() == trimmedName.ToLower())
            .ToListAsync();

        var attendanceEntries = attendances.Select(w =>
        {
            string shiftStr = w.ShiftType switch
            {
                ShiftType.FirstShift => "صباحية",
                ShiftType.SecondShift => "مسائية",
                _ => "يوم كامل"
            };

            return new WorkerLedgerEntry
            {
                Id = w.Id,
                Date = w.WorkDate,
                Source = "حضور واستحقاق",
                Description = $"تسجيل حضور - وردية {shiftStr}",
                AccruedAmount = w.AccruedWage,
                PaidAmount = 0,
                Notes = w.Notes ?? string.Empty
            };
        });

        // 2. جلب المدفوعات من المصاريف العامة
        var generalExpenses = await _context.GeneralExpenses
            .Where(e => !e.IsDeleted && e.ExpenseType == GeneralExpenseType.Salaries &&
                        e.WorkerName != null && e.WorkerName.Trim().ToLower() == trimmedName.ToLower())
            .ToListAsync();

        var genExpenseEntries = generalExpenses.Select(e => new WorkerLedgerEntry
        {
            Id = e.Id,
            Date = e.PaymentDate,
            Source = "مصروف عام",
            Description = $"دفعة مسددة - {(e.PaymentMethod == PaymentMethodType.Cash ? "نقدي" : e.PaymentMethod == PaymentMethodType.BankTransfer ? "تحويل" : e.PaymentMethod == PaymentMethodType.PersonalPartner ? "شخصي (شريك)" : "شيك")}",
            AccruedAmount = 0,
            PaidAmount = e.Amount,
            Notes = e.Description ?? string.Empty
        });

        // 3. جلب المدفوعات من مصاريف الورديات اليومية
        var dailyExpenses = await _context.DailyExpenseItems
            .Include(e => e.DailyJournal)
            .Where(e => !e.IsDeleted && e.Type == ExpenseType.WorkerWage &&
                        e.WorkerName != null && e.WorkerName.Trim().ToLower() == trimmedName.ToLower())
            .ToListAsync();

        var dailyExpenseEntries = dailyExpenses.Select(e => new WorkerLedgerEntry
        {
            Id = e.Id,
            Date = e.DailyJournal?.JournalDate ?? e.CreatedAt,
            Source = "وردية يومية",
            Description = $"صرف نقدية من الصندوق - وردية {(e.DailyJournal?.ShiftType == ShiftType.FirstShift ? "صباحية" : e.DailyJournal?.ShiftType == ShiftType.SecondShift ? "مسائية" : "يوم كامل")}",
            AccruedAmount = 0,
            PaidAmount = e.Amount,
            Notes = e.Description ?? e.Notes ?? string.Empty
        });

        // دمج وترتيب
        var allEntries = attendanceEntries
            .Concat(genExpenseEntries)
            .Concat(dailyExpenseEntries)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Id)
            .ToList();

        // حساب الرصيد المتراكم
        decimal runningBalance = 0;
        foreach (var entry in allEntries)
        {
            runningBalance += entry.AccruedAmount - entry.PaidAmount;
            entry.BalanceAfter = runningBalance;
        }

        return allEntries;
    }

    public async Task RecordAttendanceAsync(WorkerAttendance attendance)
    {
        if (attendance == null) throw new ArgumentNullException(nameof(attendance));
        if (string.IsNullOrWhiteSpace(attendance.WorkerName)) throw new Exception("يجب تحديد اسم العامل.");
        if (attendance.AccruedWage <= 0) throw new Exception("يجب تحديد أجر مستحق أكبر من الصفر.");

        attendance.WorkerName = attendance.WorkerName.Trim();
        attendance.CreatedAt = DateTime.UtcNow;
        _context.WorkerAttendances.Add(attendance);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAttendanceAsync(int attendanceId)
    {
        var attendance = await _context.WorkerAttendances.FindAsync(attendanceId);
        if (attendance != null)
        {
            attendance.IsDeleted = true;
            attendance.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
}
