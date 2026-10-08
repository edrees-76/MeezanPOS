using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Application.Services;

public class CashLedgerService : ICashLedgerService
{
    private readonly AppDbContext _context;
    private readonly ISessionService _session;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public CashLedgerService(AppDbContext context, ISessionService session)
    {
        _context = context;
        _session = session;
    }

    /// <summary>
    /// تسجيل حركة نقدية جديدة في دفتر الخزينة.
    /// </summary>
    /// <remarks>
    /// ⚠️ الترتيب بـ Id فقط آمن حالياً لأن التطبيق Desktop محلي + SQLite مع AUTOINCREMENT.
    /// إذا أُضيف مستقبلاً Multi-branch / API / Queue: يجب الانتقال إلى عمود Sequence مُخزَّن
    /// مع CreatedAtUtc و Unique Constraint.
    /// 
    /// SemaphoreSlim يضمن Single Writer Pattern داخل العملية الواحدة.
    /// BEGIN IMMEDIATE يضمن Single Writer على مستوى SQLite.
    /// </remarks>
    public async Task<CashMovement> RecordMovementAsync(CashMovementType type, decimal amount, string? sourceType, int? sourceId, string? notes, DateTime? date = null)
    {
        if (amount <= 0)
            throw new ArgumentException("المبلغ يجب أن يكون أكبر من صفر.", nameof(amount));

        await _semaphore.WaitAsync();
        var hasActiveTransaction = _context.Database.CurrentTransaction != null;
        var transaction = !hasActiveTransaction ? await _context.Database.BeginTransactionAsync() : null;
        try
        {
            var txDate = date ?? DateTime.Now;
            
            // جلب رصيد آخر حركة مسجلة — الترتيب بـ Id فقط (AUTOINCREMENT متسلسل)
            var lastMovement = await _context.CashMovements
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync();

            decimal lastBalance = lastMovement?.BalanceAfter ?? 0m;
            decimal change = type == CashMovementType.CashIn ? amount : -amount;
            decimal newBalance = lastBalance + change;

            var movement = new CashMovement
            {
                TransactionDate = txDate,
                Type = type,
                Amount = amount,
                BalanceAfter = newBalance,
                SourceType = sourceType,
                SourceId = sourceId,
                Notes = notes,
                IsReversed = false
            };

            _context.CashMovements.Add(movement);

            // التحقق من الحاجة لإعادة بناء الدفتر بسبب إدخال بتاريخ قديم
            if (lastMovement != null && txDate < lastMovement.TransactionDate)
            {
                var rebuildSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "IsLedgerRebuildRequired");
                if (rebuildSetting == null)
                {
                    _context.Settings.Add(new Setting { Key = "IsLedgerRebuildRequired", Value = "true" });
                }
                else
                {
                    rebuildSetting.Value = "true";
                }
            }

            await _context.SaveChangesAsync();
            
            if (transaction != null)
            {
                await transaction.CommitAsync();
            }

            return movement;
        }
        catch
        {
            if (transaction != null)
            {
                try { await transaction.RollbackAsync(); } catch { }
            }
            throw;
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
            _semaphore.Release();
        }
    }

    public async Task<CashMovement> ReverseMovementAsync(int movementId, string? reason)
    {
        await _semaphore.WaitAsync();
        var hasActiveTransaction = _context.Database.CurrentTransaction != null;
        var transaction = !hasActiveTransaction ? await _context.Database.BeginTransactionAsync() : null;
        try
        {
            var original = await _context.CashMovements.FindAsync(movementId);
            if (original == null || original.IsReversed)
                throw new Exception("الحركة غير موجودة أو تم عكسها بالفعل.");

            original.IsReversed = true;
            original.UpdatedAt = DateTime.UtcNow;

            // جلب رصيد آخر حركة مسجلة — الترتيب بـ Id فقط
            var lastMovement = await _context.CashMovements
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync();

            decimal lastBalance = lastMovement?.BalanceAfter ?? 0m;
            
            // الحركة العكسية
            var reversedType = original.Type == CashMovementType.CashIn ? CashMovementType.CashOut : CashMovementType.CashIn;
            decimal change = reversedType == CashMovementType.CashIn ? original.Amount : -original.Amount;
            decimal newBalance = lastBalance + change;

            var reversal = new CashMovement
            {
                TransactionDate = DateTime.Now,
                Type = reversedType,
                Amount = original.Amount,
                BalanceAfter = newBalance,
                SourceType = original.SourceType,
                SourceId = original.SourceId,
                Notes = $"{CashMovementQueries.ReversalNotePrefix} {original.Id} - السبب: {reason ?? "إلغاء العملية"}",
                IsReversed = false // هذه حركة عكسية صالحة وليست ملغاة
            };

            _context.CashMovements.Add(reversal);

            // تعليم إعادة البناء مطلوبة بسبب حدوث عكس
            var rebuildSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "IsLedgerRebuildRequired");
            if (rebuildSetting == null)
            {
                _context.Settings.Add(new Setting { Key = "IsLedgerRebuildRequired", Value = "true" });
            }
            else
            {
                rebuildSetting.Value = "true";
            }

            await _context.SaveChangesAsync();
            
            if (transaction != null)
            {
                await transaction.CommitAsync();
            }

            return reversal;
        }
        catch
        {
            if (transaction != null)
            {
                try { await transaction.RollbackAsync(); } catch { }
            }
            throw;
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
            _semaphore.Release();
        }
    }

    public async Task RebuildLedgerAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            using var transaction = await _context.Database.BeginOrJoinTransactionAsync();

            // الترتيب بـ Id فقط لأنه AUTOINCREMENT متسلسل
            var movements = await _context.CashMovements
                .OrderBy(m => m.Id)
                .ToListAsync();

            decimal runningBalance = 0m;
            foreach (var movement in movements)
            {
                decimal change = movement.Type == CashMovementType.CashIn ? movement.Amount : -movement.Amount;
                runningBalance += change;
                movement.BalanceAfter = runningBalance;
                movement.UpdatedAt = DateTime.UtcNow;
            }

            var rebuildSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "IsLedgerRebuildRequired");
            if (rebuildSetting != null)
            {
                rebuildSetting.Value = "false";
            }

            // إضافة سجل التدقيق مباشرة قبل الحفظ لتجنب SaveChangesAsync مزدوج
            int userId = _session.CurrentUser?.Id ?? 1;
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "RebuildLedger",
                EntityName = "CashMovement",
                EntityId = 0,
                Changes = "Before: RebuildRequired | After: Rebuilt",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<decimal> GetCurrentBalanceAsync()
    {
        // الترتيب بـ Id فقط — AUTOINCREMENT متسلسل
        var lastMovement = await _context.CashMovements
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync();

        return lastMovement?.BalanceAfter ?? 0m;
    }

    public async Task<bool> IsRebuildRequiredAsync()
    {
        var setting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "IsLedgerRebuildRequired");
        return setting != null && setting.Value == "true";
    }
}
