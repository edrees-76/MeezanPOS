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
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public CashLedgerService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CashMovement> RecordMovementAsync(CashMovementType type, decimal amount, string? sourceType, int? sourceId, string? notes, DateTime? date = null)
    {
        await _semaphore.WaitAsync();
        var hasActiveTransaction = _context.Database.CurrentTransaction != null;
        var transaction = hasActiveTransaction ? null : await _context.Database.BeginTransactionAsync();
        try
        {
            var txDate = date ?? DateTime.Now;
            
            // جلب رصيد آخر حركة مسجلة
            var lastMovement = await _context.CashMovements
                .OrderByDescending(m => m.TransactionDate)
                .ThenByDescending(m => m.Id)
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

            // Check if rebuild is required because of back-dated insertion
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
                await transaction.RollbackAsync();
            }
            throw;
        }
        finally
        {
            transaction?.Dispose();
            _semaphore.Release();
        }
    }

    public async Task<CashMovement> ReverseMovementAsync(int movementId, string? reason)
    {
        await _semaphore.WaitAsync();
        var hasActiveTransaction = _context.Database.CurrentTransaction != null;
        var transaction = hasActiveTransaction ? null : await _context.Database.BeginTransactionAsync();
        try
        {
            var original = await _context.CashMovements.FindAsync(movementId);
            if (original == null || original.IsReversed)
                throw new Exception("الحركة غير موجودة أو تم عكسها بالفعل.");

            original.IsReversed = true;
            original.UpdatedAt = DateTime.Now;

            // جلب رصيد آخر حركة مسجلة
            var lastMovement = await _context.CashMovements
                .OrderByDescending(m => m.TransactionDate)
                .ThenByDescending(m => m.Id)
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
                Notes = $"عكس حركة رقم {original.Id} - السبب: {reason ?? "إلغاء العملية"}",
                IsReversed = true
            };

            _context.CashMovements.Add(reversal);

            // Mark rebuild required because a reversal occurred
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
                await transaction.RollbackAsync();
            }
            throw;
        }
        finally
        {
            transaction?.Dispose();
            _semaphore.Release();
        }
    }

    public async Task RebuildLedgerAsync()
    {
        await _semaphore.WaitAsync();
        var hasActiveTransaction = _context.Database.CurrentTransaction != null;
        var transaction = hasActiveTransaction ? null : await _context.Database.BeginTransactionAsync();
        try
        {
            var movements = await _context.CashMovements
                .OrderBy(m => m.TransactionDate)
                .ThenBy(m => m.Id)
                .ToListAsync();

            decimal runningBalance = 0m;
            foreach (var movement in movements)
            {
                decimal change = movement.Type == CashMovementType.CashIn ? movement.Amount : -movement.Amount;
                runningBalance += change;
                movement.BalanceAfter = runningBalance;
                movement.UpdatedAt = DateTime.Now;
            }

            var rebuildSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "IsLedgerRebuildRequired");
            if (rebuildSetting != null)
            {
                rebuildSetting.Value = "false";
            }

            await _context.SaveChangesAsync();
            
            if (transaction != null)
            {
                await transaction.CommitAsync();
            }
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync();
            }
            throw;
        }
        finally
        {
            transaction?.Dispose();
            _semaphore.Release();
        }
    }

    public async Task<decimal> GetCurrentBalanceAsync()
    {
        var lastMovement = await _context.CashMovements
            .OrderByDescending(m => m.TransactionDate)
            .ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync();

        return lastMovement?.BalanceAfter ?? 0m;
    }

    public async Task<bool> IsRebuildRequiredAsync()
    {
        var setting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "IsLedgerRebuildRequired");
        return setting != null && setting.Value == "true";
    }
}
