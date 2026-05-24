using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Application.Services;

public class OwnerDebtService : IOwnerDebtService
{
    private readonly AppDbContext _context;
    private readonly IBankService _bankService;

    public OwnerDebtService(AppDbContext context, IBankService bankService)
    {
        _context = context;
        _bankService = bankService;
    }

    public async Task<List<OwnerDebt>> GetDebtsAsync(string? partnerName = null, OwnerDebtStatus? status = null)
    {
        var query = _context.OwnerDebts
            .Where(d => !d.IsDeleted);

        if (!string.IsNullOrEmpty(partnerName))
        {
            query = query.Where(d => d.PartnerName == partnerName);
        }

        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        return await query.OrderByDescending(d => d.TransactionDate)
            .ThenByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<OwnerDebt> RecordDebtAsync(string partnerName, decimal amount, string? expenseCategory, string? notes, DateTime date, string? sourceType = null, int? sourceId = null)
    {
        var debt = new OwnerDebt
        {
            PartnerName = partnerName,
            Amount = amount,
            ExpenseCategory = expenseCategory,
            Notes = notes,
            TransactionDate = date,
            Status = OwnerDebtStatus.Unpaid,
            SourceType = sourceType,
            SourceId = sourceId
        };

        _context.OwnerDebts.Add(debt);
        await _context.SaveChangesAsync();
        return debt;
    }

    public async Task DeleteDebtAsync(int debtId)
    {
        var debt = await _context.OwnerDebts.FindAsync(debtId);
        if (debt == null || debt.IsDeleted)
            throw new Exception("الدين غير موجود.");

        var hasSettlements = await _context.OwnerDebtSettlements
            .AnyAsync(s => s.OwnerDebtId == debtId && !s.IsDeleted);

        if (hasSettlements)
            throw new Exception("لا يمكن حذف دين تم تسويته بالفعل. الرجاء حذف التسويات المرتبطة به أولاً.");

        debt.IsDeleted = true;
        debt.UpdatedAt = DateTime.Now;
        await _context.SaveChangesAsync();
    }

    public async Task<List<OwnerDebtSettlement>> GetSettlementsAsync(string? partnerName = null)
    {
        var query = _context.OwnerDebtSettlements
            .Include(s => s.BankAccount)
            .Include(s => s.OwnerDebt)
            .Where(s => !s.IsDeleted);

        if (!string.IsNullOrEmpty(partnerName))
        {
            query = query.Where(s => s.PartnerName == partnerName);
        }

        return await query.OrderByDescending(s => s.SettlementDate)
            .ThenByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<OwnerDebtSettlement> RecordSettlementAsync(int? debtId, string partnerName, decimal amount, OwnerDebtSettlementSource source, int? bankAccountId, string? notes, DateTime date)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            OwnerDebt? debt = null;
            if (debtId.HasValue)
            {
                debt = await _context.OwnerDebts.FindAsync(debtId.Value);
                if (debt == null || debt.IsDeleted)
                    throw new Exception("الدين المحدد غير موجود.");

                debt.Status = OwnerDebtStatus.Paid;
                debt.UpdatedAt = DateTime.Now;
            }

            var settlement = new OwnerDebtSettlement
            {
                OwnerDebtId = debtId,
                PartnerName = partnerName,
                Amount = amount,
                SettlementSource = source,
                BankAccountId = bankAccountId,
                Notes = notes,
                SettlementDate = date
            };

            _context.OwnerDebtSettlements.Add(settlement);
            await _context.SaveChangesAsync();

            // إذا كان التمويل من الحساب البنكي، نقوم بتسجيل حركة بنكية
            if (source == OwnerDebtSettlementSource.Bank)
            {
                if (!bankAccountId.HasValue)
                    throw new Exception("يجب تحديد الحساب البنكي للتسوية المصرفية.");

                var bankTxNotes = $"تسوية مستحقات المالك {partnerName}" + (string.IsNullOrEmpty(notes) ? "" : $" - {notes}");
                await _bankService.RecordTransactionAsync(
                    bankAccountId.Value,
                    BankTransactionType.OwnerDebtSettlement,
                    amount,
                    null,
                    bankTxNotes,
                    "OwnerDebtSettlement",
                    settlement.Id,
                    date
                );
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return settlement;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteSettlementAsync(int settlementId)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var settlement = await _context.OwnerDebtSettlements.FindAsync(settlementId);
            if (settlement == null || settlement.IsDeleted)
                throw new Exception("التسوية غير موجودة.");

            // 1. إذا كانت التسوية مرتبطة بدين معين، نعيد الدين إلى الحالة "غير مسدد"
            if (settlement.OwnerDebtId.HasValue)
            {
                var debt = await _context.OwnerDebts.FindAsync(settlement.OwnerDebtId.Value);
                if (debt != null && !debt.IsDeleted)
                {
                    debt.Status = OwnerDebtStatus.Unpaid;
                    debt.UpdatedAt = DateTime.Now;
                }
            }

            // 2. إذا كانت التسوية من البنك، نقوم بحذف الحركة البنكية المرتبطة بها
            if (settlement.SettlementSource == OwnerDebtSettlementSource.Bank)
            {
                await _bankService.DeleteTransactionBySourceAsync("OwnerDebtSettlement", settlement.Id);
            }

            // 3. حذف التسوية
            settlement.IsDeleted = true;
            settlement.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<decimal> GetTotalOwnerDebtsAsync(string? partnerName = null)
    {
        var query = _context.OwnerDebts.Where(d => !d.IsDeleted);
        if (!string.IsNullOrEmpty(partnerName))
        {
            query = query.Where(d => d.PartnerName == partnerName);
        }
        var amounts = await query.Select(d => d.Amount).ToListAsync();
        return amounts.Sum();
    }

    public async Task<decimal> GetTotalOwnerSettlementsAsync(string? partnerName = null)
    {
        var query = _context.OwnerDebtSettlements.Where(s => !s.IsDeleted);
        if (!string.IsNullOrEmpty(partnerName))
        {
            query = query.Where(s => s.PartnerName == partnerName);
        }
        var amounts = await query.Select(s => s.Amount).ToListAsync();
        return amounts.Sum();
    }

    public async Task<decimal> GetNetOwnerBalanceAsync(string? partnerName = null)
    {
        decimal debts = await GetTotalOwnerDebtsAsync(partnerName);
        decimal settlements = await GetTotalOwnerSettlementsAsync(partnerName);
        return debts - settlements;
    }

    public async Task<List<string>> GetPartnerNamesAsync()
    {
        var debtPartners = await _context.OwnerDebts
            .Where(d => !d.IsDeleted)
            .Select(d => d.PartnerName)
            .Distinct()
            .ToListAsync();

        var settlementPartners = await _context.OwnerDebtSettlements
            .Where(s => !s.IsDeleted)
            .Select(s => s.PartnerName)
            .Distinct()
            .ToListAsync();

        return debtPartners.Union(settlementPartners)
            .Where(p => !string.IsNullOrEmpty(p))
            .OrderBy(p => p)
            .ToList();
    }
}
