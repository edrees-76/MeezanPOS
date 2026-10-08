using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.Services.Queries;

/// <summary>تفاصيل مبيعات الخدمات المصرفية للورديات ومطابقتها (كانت داخل BankingServicesViewModel).</summary>
public interface IBankingQueryService
{
    Task<List<BankingItemDetailDto>> GetShiftBankingDetailsAsync(int bankAccountId, DateTime date);
    Task SetReconciledAsync(int bankingItemId, bool isReconciled);
}

public sealed class BankingQueryService : IBankingQueryService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public BankingQueryService(IDbContextFactory<AppDbContext>? factory = null)
        => _factory = factory ?? DefaultDbContextFactory.Instance;

    public async Task<List<BankingItemDetailDto>> GetShiftBankingDetailsAsync(int bankAccountId, DateTime date)
    {
        var targetDate = date.Date;
        await using var context = _factory.CreateDbContext();
        return await context.BankingItems
            .AsNoTracking()
            .Where(b => b.BankAccountId == bankAccountId
                     && !b.IsDeleted
                     && b.DailyJournal != null
                     && b.DailyJournal.JournalDate.Date == targetDate
                     && !b.DailyJournal.IsDeleted)
            .Select(b => new BankingItemDetailDto
            {
                Id = b.Id,
                CashierName = b.DailyJournal!.EmployeeName,
                ShiftName = b.DailyJournal!.ShiftType == ShiftType.FirstShift ? "الوردية الأولى" : b.DailyJournal!.ShiftType == ShiftType.SecondShift ? "الوردية الثانية" : "يوم كامل",
                Amount = b.Amount,
                InvoiceNumber = b.Description ?? "غير محدد",
                TransferReference = b.ReferenceNumber ?? "غير محدد",
                IsReconciled = b.IsReconciled
            })
            .ToListAsync();
    }

    public async Task SetReconciledAsync(int bankingItemId, bool isReconciled)
    {
        await using var context = _factory.CreateDbContext();
        var dbItem = await context.BankingItems.FirstOrDefaultAsync(b => b.Id == bankingItemId);
        if (dbItem == null) return;
        dbItem.IsReconciled = isReconciled;
        dbItem.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }
}
