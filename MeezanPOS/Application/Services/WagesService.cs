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
    private readonly ISessionService? _sessionService;
    private readonly ICashLedgerService? _cashLedger;

    public WagesService(AppDbContext context, ISessionService? sessionService = null, ICashLedgerService? cashLedger = null)
    {
        _context = context;
        _sessionService = sessionService;
        _cashLedger = cashLedger;
    }

    // --- إدارة بيانات العمال (Worker CRUD) ---
    public async Task<List<Worker>> GetAllWorkersAsync(bool includeInactive = false)
    {
        return await _context.Workers
            .Where(w => !w.IsDeleted && (includeInactive || w.IsActive))
            .OrderBy(w => w.WorkerName)
            .ToListAsync();
    }

    public async Task<Worker?> GetWorkerByIdAsync(int id)
    {
        return await _context.Workers.FirstOrDefaultAsync(w => w.Id == id && !w.IsDeleted);
    }

    public async Task SaveWorkerAsync(Worker worker)
    {
        if (worker == null) throw new ArgumentNullException(nameof(worker));
        if (string.IsNullOrWhiteSpace(worker.WorkerName)) throw new Exception("اسم العامل مطلوب.");

        worker.WorkerName = worker.WorkerName.Trim();

        if (worker.Id == 0)
        {
            var exists = await _context.Workers.AnyAsync(w => w.WorkerName.ToLower() == worker.WorkerName.ToLower() && !w.IsDeleted);
            if (exists) throw new Exception("اسم العامل مسجل مسبقاً.");

            worker.CreatedAt = DateTime.UtcNow;
            _context.Workers.Add(worker);
        }
        else
        {
            worker.UpdatedAt = DateTime.UtcNow;
            _context.Entry(worker).State = EntityState.Modified;
        }

        await _context.SaveChangesAsync();
    }

    public async Task ToggleWorkerActiveStatusAsync(int workerId)
    {
        var worker = await _context.Workers.FindAsync(workerId);
        if (worker != null)
        {
            worker.IsActive = !worker.IsActive;
            worker.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task DeleteWorkerAsync(int workerId)
    {
        var worker = await _context.Workers.FindAsync(workerId);
        if (worker != null)
        {
            worker.IsDeleted = true;
            worker.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    // --- الحضور والغياب (Attendance) ---
    public async Task<List<WorkerAttendance>> GetAttendanceForDateAsync(DateTime date)
    {
        var targetDate = date.Date;
        // ملاحظة: لا يمكن استخدام .Include() هنا لدمج الاستعلامين لأن الكيان WorkerAttendance لا يحتوي على خاصية ربط (Navigation Property) لجدول الحركات WorkerTransaction.
        var attendances = await _context.WorkerAttendances
            .AsNoTracking()
            .Where(a => a.WorkDate.Date == targetDate && !a.IsDeleted)
            .ToListAsync();

        if (attendances.Any())
        {
            var attendanceIds = attendances.Select(a => a.Id).ToList();
            var txs = await _context.WorkerTransactions
                .AsNoTracking()
                .Where(t => t.AttendanceId != null && attendanceIds.Contains(t.AttendanceId.Value) && !t.IsDeleted)
                .ToListAsync();

            foreach (var att in attendances)
            {
                var deductionTx = txs.FirstOrDefault(t => t.AttendanceId == att.Id && t.Type == WorkerTransactionType.Deduction);
                if (deductionTx != null)
                {
                    att.DeductionAmount = deductionTx.DebitAmount;
                }

                // بيانات قديمة: كان الخصم يُحفظ حركةً مدينة في حساب العامل
                var settlementTx = txs.FirstOrDefault(t => t.AttendanceId == att.Id && t.Type == WorkerTransactionType.Adjustment && t.Notes != null && t.Notes.Contains("تسوية سلفة"));
                if (settlementTx != null && att.AdvanceDeducted == 0)
                {
                    att.AdvanceDeducted = settlementTx.DebitAmount;
                }
            }
        }

        return attendances;
    }

    private string GetShiftNameArabic(ShiftType shift) => shift switch
    {
        ShiftType.FirstShift => "الوردية الأولى (صباحية)",
        ShiftType.SecondShift => "الوردية الثانية (مسائية)",
        _ => "يوم كامل"
    };

    public async Task SaveAttendanceBatchAsync(List<WorkerAttendance> attendances)
    {
        if (attendances == null || !attendances.Any()) return;
        await PeriodLock.EnsureDateOpenAsync(_context, attendances.First().WorkDate);

        // الحضور وحركاته المالية تُحفظ معاً أو لا تُحفظ
        await using var batchTx = await _context.Database.BeginOrJoinTransactionAsync();
        var targetDate = attendances.First().WorkDate.Date;

        foreach (var att in attendances)
        {
            if (att.WorkerId == 0 || att.WorkerId == null) continue;

            att.WorkDate = targetDate;

            // حساب الاستحقاق الفعلي بناء على حالة الحضور
            att.AccruedWage = att.Status switch
            {
                AttendanceStatus.Present => att.SnapshotDailyWage,
                AttendanceStatus.HalfDay => att.SnapshotDailyWage / 2m,
                _ => 0m
            };

            if (att.Id == 0)
            {
                att.CreatedAt = DateTime.UtcNow;
                _context.WorkerAttendances.Add(att);
            }
            else
            {
                att.UpdatedAt = DateTime.UtcNow;
                _context.Entry(att).State = EntityState.Modified;
            }
        }

        // حفظ التغيرات لتوليد المعرفات
        await _context.SaveChangesAsync();

        // ترحيل الحركات المالية إلى الأستاذ المساعد
        foreach (var att in attendances)
        {
            if (att.WorkerId == null) continue;

            // 1. حركة استحقاق الأجر (WageAccrual)
            var existingAccrual = await _context.WorkerTransactions
                .FirstOrDefaultAsync(t => t.WorkerId == att.WorkerId.Value &&
                                          t.AttendanceId == att.Id &&
                                          t.Type == WorkerTransactionType.WageAccrual &&
                                          !t.IsDeleted);

            if (att.Status == AttendanceStatus.Present || att.Status == AttendanceStatus.HalfDay)
            {
                if (existingAccrual == null)
                {
                    var accrualTx = new WorkerTransaction
                    {
                        WorkerId = att.WorkerId.Value,
                        WorkerName = att.WorkerName,
                        TransactionDate = targetDate,
                        Type = WorkerTransactionType.WageAccrual,
                        CreditAmount = att.AccruedWage,
                        DebitAmount = 0m,
                        AttendanceId = att.Id,
                        Notes = $"استحقاق أجر - {GetShiftNameArabic(att.ShiftType)} ({att.Status})"
                    };
                    _context.WorkerTransactions.Add(accrualTx);
                }
                else
                {
                    existingAccrual.CreditAmount = att.AccruedWage;
                    existingAccrual.WorkerName = att.WorkerName;
                    existingAccrual.Notes = $"استحقاق أجر - {GetShiftNameArabic(att.ShiftType)} ({att.Status})";
                    existingAccrual.UpdatedAt = DateTime.UtcNow;
                    _context.Entry(existingAccrual).State = EntityState.Modified;
                }
            }
            else
            {
                if (existingAccrual != null)
                {
                    existingAccrual.IsDeleted = true;
                    existingAccrual.UpdatedAt = DateTime.UtcNow;
                }
            }

            // 2. حركة الخصم (Deduction)
            var existingDeduction = await _context.WorkerTransactions
                .FirstOrDefaultAsync(t => t.WorkerId == att.WorkerId.Value &&
                                          t.AttendanceId == att.Id &&
                                          t.Type == WorkerTransactionType.Deduction &&
                                          !t.IsDeleted);

            if (att.DeductionAmount > 0)
            {
                if (existingDeduction == null)
                {
                    var deductionTx = new WorkerTransaction
                    {
                        WorkerId = att.WorkerId.Value,
                        WorkerName = att.WorkerName,
                        TransactionDate = targetDate,
                        Type = WorkerTransactionType.Deduction,
                        CreditAmount = 0m,
                        DebitAmount = att.DeductionAmount,
                        AttendanceId = att.Id,
                        Notes = $"خصم/غرامة حضور وغياب - {GetShiftNameArabic(att.ShiftType)}"
                    };
                    _context.WorkerTransactions.Add(deductionTx);
                }
                else
                {
                    existingDeduction.DebitAmount = att.DeductionAmount;
                    existingDeduction.WorkerName = att.WorkerName;
                    existingDeduction.UpdatedAt = DateTime.UtcNow;
                    _context.Entry(existingDeduction).State = EntityState.Modified;
                }
            }
            else
            {
                if (existingDeduction != null)
                {
                    existingDeduction.IsDeleted = true;
                    existingDeduction.UpdatedAt = DateTime.UtcNow;
                }
            }

            // 3. حركة تسوية السلفة (Advance Settlement)
            var existingAdvanceSettlement = await _context.WorkerTransactions
                .FirstOrDefaultAsync(t => t.WorkerId == att.WorkerId.Value &&
                                          t.AttendanceId == att.Id &&
                                          t.Type == WorkerTransactionType.Adjustment &&
                                          t.Notes != null && t.Notes.Contains("تسوية سلفة") &&
                                          !t.IsDeleted);

            // خصم السلفة بيان محفوظ في سجل الحضور فقط. كان يُسجل حركة مدينة فيزيد دين العامل بدل أن ينقصه
            // (السلفة مدينة، والأجر المستحق دائن يسقطها؛ الحركة الإضافية تحسب الخصم مرتين).
            // تُحذف أي حركة قديمة من هذا النوع عند إعادة حفظ اليوم.
            if (existingAdvanceSettlement != null)
            {
                existingAdvanceSettlement.IsDeleted = true;
                existingAdvanceSettlement.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
        await batchTx.CommitAsync();
    }

    // --- العمليات المالية والأستاذ المساعد ---
    public async Task RecordTransactionAsync(WorkerTransaction transaction)
    {
        if (transaction == null) throw new ArgumentNullException(nameof(transaction));
        if (transaction.WorkerId <= 0) throw new Exception("يجب تحديد العامل.");
        if (transaction.DebitAmount < 0 || transaction.CreditAmount < 0) throw new Exception("المبالغ يجب أن تكون موجبة.");
        await PeriodLock.EnsureDateOpenAsync(_context, transaction.TransactionDate);

        var worker = await _context.Workers.FindAsync(transaction.WorkerId);
        if (worker == null) throw new Exception("العامل غير موجود.");

        transaction.WorkerName = worker.WorkerName;
        if (transaction.Id == 0)
        {
            transaction.CreatedAt = DateTime.UtcNow;
            _context.WorkerTransactions.Add(transaction);
        }
        else
        {
            transaction.UpdatedAt = DateTime.UtcNow;
            _context.Entry(transaction).State = EntityState.Modified;
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// كان هذا الكود داخل WagesManagementViewModel. العملية كلها في معاملة واحدة:
    /// الصرف من الكاشير يُسجل بنداً في مصروفات يومية اليوم المسودة (فيُنقص نقدها المتوقع)،
    /// والصرف من نقدية المطعم يُسجل مصروفاً عاماً وحركة نقدية صادرة.
    /// </summary>
    public async Task<OperationResult> PayWorkerAsync(WorkerPaymentRequest request)
    {
        if (request.Amount <= 0) return OperationResult.Fail("يرجى إدخال مبلغ صحيح أكبر من الصفر للصرف.", "تنبيه");

        await using var payTx = await _context.Database.BeginOrJoinTransactionAsync();

        var paymentDay = request.Date.Date;
        if (await PeriodLock.IsDateLockedAsync(_context, paymentDay))
            return OperationResult.Fail(PeriodLock.LockedMessage, "تنبيه");

        var description = !string.IsNullOrWhiteSpace(request.Notes)
            ? request.Notes.Trim()
            : $"صرف مستحقات للعامل {request.WorkerName}";

        if (request.FromCashier)
        {
            // على يومية مسودة بتاريخ الصرف نفسه (آخر وردية في ذلك اليوم).
            // سابقاً كانت تُضاف لآخر مسودة أياً كان تاريخها، فتظهر في يوم قديم.
            var nextDay = paymentDay.AddDays(1);
            var openJournal = await _context.DailyJournals
                .Where(j => j.FinancialStatus == FinancialStatus.Draft && j.JournalDate >= paymentDay && j.JournalDate < nextDay)
                .OrderByDescending(j => j.ShiftType)
                .ThenByDescending(j => j.Id)
                .FirstOrDefaultAsync();

            if (openJournal == null)
                return OperationResult.Fail($"لا توجد يومية مسودة بتاريخ {paymentDay:yyyy/MM/dd} لتسجيل الصرف عليها. أنشئ يومية هذا اليوم أولاً أو اصرف كـ (مصروف عام).", "تنبيه");

            // المصروف يُنقص النقد المتوقع لليومية، فيجب أن يدخل في إجمالي مصروفاتها
            openJournal.TotalExpenses += request.Amount;
            openJournal.UpdatedAt = DateTime.UtcNow;
            openJournal.RowVersion++;

            _context.DailyExpenseItems.Add(new DailyExpenseItem
            {
                DailyJournalId = openJournal.Id,
                SequenceNumber = await _context.DailyExpenseItems.CountAsync(e => e.DailyJournalId == openJournal.Id) + 1,
                Amount = request.Amount,
                Category = "يومية عامل",
                CategoryName = "يومية عامل",
                Description = description,
                Type = ExpenseType.WorkerWage,
                WorkerName = request.WorkerName,
                WorkerId = request.WorkerId,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            var cashLedger = _cashLedger
                ?? throw new InvalidOperationException("خدمة دفتر النقدية غير متاحة لتسجيل الصرف من نقدية المطعم.");

            var generalExpense = new GeneralExpense
            {
                ExpenseType = GeneralExpenseType.Salaries,
                Amount = request.Amount,
                PaymentDate = request.Date,
                PaymentMethod = PaymentMethodType.Cash,
                Description = description,
                WorkerName = request.WorkerName,
                WorkerId = request.WorkerId,
                CreatedAt = DateTime.UtcNow
            };

            _context.GeneralExpenses.Add(generalExpense);
            await _context.SaveChangesAsync();

            // الصرف من نقدية المطعم يُخرج نقداً فعلياً (مثل بقية المصروفات العامة النقدية)
            await cashLedger.RecordMovementAsync(
                CashMovementType.CashOut,
                request.Amount,
                SourceTypes.GeneralExpense,
                generalExpense.Id,
                $"مصروف عام: رواتب | {generalExpense.Description}",
                paymentDay);
        }

        await _context.SaveChangesAsync();
        await payTx.CommitAsync();
        return OperationResult.Ok;
    }

    public async Task DeleteTransactionAsync(int transactionId, string reason, string deletedBy, int? deletedByUserId)
    {
        var tx = await _context.WorkerTransactions
            .Include(t => t.GeneralExpense)
            .Include(t => t.DailyExpenseItem)
                .ThenInclude(d => d!.DailyJournal) // تفادي تحذير CS8602 (إلغاء إشارة مرجعية فارغة محتملة) باستخدام معامل السماح بالقيم الفارغة (!)
            .FirstOrDefaultAsync(t => t.Id == transactionId);

        if (tx != null)
        {
            await PeriodLock.EnsureDateOpenAsync(_context, tx.TransactionDate);
            bool isPosted = false;
            if (tx.GeneralExpense != null && (tx.GeneralExpense.FinancialStatus == FinancialStatus.Posted || tx.GeneralExpense.FinancialStatus == FinancialStatus.Archived))
            {
                isPosted = true;
            }
            else if (tx.DailyExpenseItem != null && tx.DailyExpenseItem.DailyJournal != null && (tx.DailyExpenseItem.DailyJournal.FinancialStatus == FinancialStatus.Posted || tx.DailyExpenseItem.DailyJournal.FinancialStatus == FinancialStatus.Archived))
            {
                isPosted = true;
            }

            bool isManualDirect = tx.AttendanceId == null && tx.DailyExpenseItemId == null && tx.GeneralExpenseId == null;

            if (isPosted)
            {
                throw new InvalidOperationException("لا يمكن حذف حركة مالية مرحّلة مالياً.");
            }

            if (!isManualDirect)
            {
                throw new InvalidOperationException("لا يمكن حذف حركات النظام التلقائية من شاشة كشف حساب العامل مباشرة.");
            }

            tx.IsDeleted = true;
            tx.DeletedReason = reason;
            tx.DeletedBy = deletedBy;
            tx.DeletedByUserId = deletedByUserId;
            tx.DeletedAt = DateTime.UtcNow;
            tx.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<WorkerLedgerEntry>> GetWorkerLedgerAsync(int workerId)
    {
        var txs = await _context.WorkerTransactions
            .AsNoTracking()
            .Include(t => t.GeneralExpense)
            .Include(t => t.DailyExpenseItem)
                .ThenInclude(d => d!.DailyJournal) // تفادي تحذير CS8602 (إلغاء إشارة مرجعية فارغة محتملة) باستخدام معامل السماح بالقيم الفارغة (!)
            .Where(t => t.WorkerId == workerId && !t.IsDeleted)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.Id)
            .ToListAsync();

        var ledger = new List<WorkerLedgerEntry>();
        decimal runningBalance = 0;

        foreach (var t in txs)
        {
            runningBalance += t.CreditAmount - t.DebitAmount;

            string source = t.Type switch
            {
                WorkerTransactionType.WageAccrual => "استحقاق حضور",
                WorkerTransactionType.Payment => "سداد نقدي",
                WorkerTransactionType.Advance => "سلفة عمال",
                WorkerTransactionType.Deduction => "خصم وغرامة",
                WorkerTransactionType.Adjustment => "تسوية يدوية",
                _ => "أخرى"
            };

            string description = t.Notes ?? string.Empty;
            if (string.IsNullOrEmpty(description))
            {
                description = t.Type switch
                {
                    WorkerTransactionType.WageAccrual => "استحقاق يومية العمل",
                    WorkerTransactionType.Payment => "دفعة نقدية مسددة للعامِل",
                    WorkerTransactionType.Advance => "صرف سلفة مالية",
                    WorkerTransactionType.Deduction => "خصم/غرامة حضور وغياب",
                    WorkerTransactionType.Adjustment => "تسوية رصيد حساب",
                    _ => ""
                };
            }

            bool isPosted = false;
            if (t.GeneralExpense != null && (t.GeneralExpense.FinancialStatus == FinancialStatus.Posted || t.GeneralExpense.FinancialStatus == FinancialStatus.Archived))
            {
                isPosted = true;
            }
            else if (t.DailyExpenseItem != null && t.DailyExpenseItem.DailyJournal != null && (t.DailyExpenseItem.DailyJournal.FinancialStatus == FinancialStatus.Posted || t.DailyExpenseItem.DailyJournal.FinancialStatus == FinancialStatus.Archived))
            {
                isPosted = true;
            }

            bool isManualDirect = t.AttendanceId == null && t.DailyExpenseItemId == null && t.GeneralExpenseId == null;

            ledger.Add(new WorkerLedgerEntry
            {
                Id = t.Id,
                Date = t.TransactionDate,
                Source = source,
                Description = description,
                AccruedAmount = t.CreditAmount,
                PaidAmount = t.DebitAmount,
                BalanceAfter = runningBalance,
                Notes = (t.Notes != description) ? (t.Notes ?? string.Empty) : string.Empty,
                IsPosted = isPosted,
                IsManualDirect = isManualDirect
            });
        }

        return ledger;
    }

    public async Task<List<WorkerWageSummary>> GetWorkerSummariesAsync()
    {
        var workers = await _context.Workers.AsNoTracking().Where(w => !w.IsDeleted).ToListAsync();

        // الجمع في الذاكرة بالنوع decimal: SQLite لا يجمع decimal، والتحويل إلى double كان يُدخل أخطاء تقريب في الأرصدة
        var balances = (await _context.WorkerTransactions
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .Select(t => new { t.WorkerId, t.CreditAmount, t.DebitAmount, t.TransactionDate })
            .ToListAsync())
            .GroupBy(t => t.WorkerId)
            .Select(g => new
            {
                WorkerId = g.Key,
                TotalAccrued = g.Sum(t => t.CreditAmount),
                TotalPaid = g.Sum(t => t.DebitAmount),
                LastActivity = g.Max(t => (DateTime?)t.TransactionDate)
            })
            .ToDictionary(b => b.WorkerId);

        var summaries = new List<WorkerWageSummary>();

        foreach (var w in workers)
        {
            balances.TryGetValue(w.Id, out var bal);

            summaries.Add(new WorkerWageSummary
            {
                WorkerId = w.Id,
                WorkerName = w.WorkerName,
                DailyWage = w.DailyWage,
                IsActive = w.IsActive,
                TotalAccrued = bal?.TotalAccrued ?? 0,
                TotalPaid = bal?.TotalPaid ?? 0,
                Notes = w.Notes ?? string.Empty,
                LastActivity = bal?.LastActivity
            });
        }

        return summaries.OrderByDescending(s => s.Balance != 0).ThenBy(s => s.WorkerName).ToList();
    }

    public async Task<List<string>> GetUniqueWorkerNamesAsync()
    {
        return await _context.Workers
            .Where(w => !w.IsDeleted && w.IsActive)
            .Select(w => w.WorkerName)
            .OrderBy(name => name)
            .ToListAsync();
    }
}
