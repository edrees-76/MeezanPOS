using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.Services.Queries;

/// <summary>أثر فك تسوية فترة: عدد السجلات المرتبطة وصافي المبلغ الذي سيُعكس.</summary>
public sealed record SettlementImpact(int JournalsCount, int ExpensesCount, decimal TotalAmount);

/// <summary>استعلامات شاشة المبيعات والخزينة (كانت داخل SalesViewModel).</summary>
public interface ISalesQueryService
{
    Task<List<DailyJournal>> GetAllJournalsWithItemsAsync();
    Task<List<int>> GetJournalIdsInMonthAsync(int year, int month, FinancialStatus status);
    Task<bool> MonthHasSettledJournalsAsync(int year, int month);
    Task<List<int>> GetDraftJournalIdsInRangeAsync(DateTime from, DateTime to);
    Task<List<CashMovement>> GetRecentCashMovementsAsync(int count);
    Task<SettlementImpact> GetSettlementImpactAsync(int sessionId);
}

public sealed class SalesQueryService : ISalesQueryService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public SalesQueryService(IDbContextFactory<AppDbContext>? factory = null)
        => _factory = factory ?? DefaultDbContextFactory.Instance;

    public async Task<List<DailyJournal>> GetAllJournalsWithItemsAsync()
    {
        await using var db = _factory.CreateDbContext();
        return await db.DailyJournals.AsNoTracking()
            .Include(j => j.ExpenseItems)
            .Include(j => j.BankingItems)
            .Include(j => j.Adjustments)
            .OrderBy(j => j.JournalDate)
            .ThenBy(j => j.Id)
            .ToListAsync();
    }

    public async Task<List<int>> GetJournalIdsInMonthAsync(int year, int month, FinancialStatus status)
    {
        await using var db = _factory.CreateDbContext();
        return await db.DailyJournals
            .Where(j => j.JournalDate.Year == year && j.JournalDate.Month == month && j.FinancialStatus == status)
            .Select(j => j.Id)
            .ToListAsync();
    }

    public async Task<bool> MonthHasSettledJournalsAsync(int year, int month)
    {
        await using var db = _factory.CreateDbContext();
        var settledSessionIds = await db.PostingSessions
            .Where(s => s.Status == PostingSessionStatus.Settled || s.Status == PostingSessionStatus.ReSettled)
            .Select(s => s.Id)
            .ToListAsync();
        return await db.DailyJournals.AnyAsync(j =>
            j.JournalDate.Year == year && j.JournalDate.Month == month &&
            j.PostingSessionId.HasValue && settledSessionIds.Contains(j.PostingSessionId.Value));
    }

    public async Task<List<int>> GetDraftJournalIdsInRangeAsync(DateTime from, DateTime to)
    {
        await using var db = _factory.CreateDbContext();
        return await db.DailyJournals
            .Where(j => j.JournalDate.Date >= from && j.JournalDate.Date <= to && j.FinancialStatus == FinancialStatus.Draft)
            .Select(j => j.Id)
            .ToListAsync();
    }

    /// <summary>آخر الحركات بالترتيب الزمني (الأقدم أولاً).</summary>
    public async Task<List<CashMovement>> GetRecentCashMovementsAsync(int count)
    {
        await using var db = _factory.CreateDbContext();
        var movements = await db.CashMovements.AsNoTracking()
            .OrderByDescending(m => m.TransactionDate)
            .ThenByDescending(m => m.Id)
            .Take(count)
            .ToListAsync();
        movements.Reverse();
        return movements;
    }

    public async Task<SettlementImpact> GetSettlementImpactAsync(int sessionId)
    {
        await using var db = _factory.CreateDbContext();
        var journalCash = await db.DailyJournals.Where(j => j.PostingSessionId == sessionId).Select(j => j.ActualCash - j.CashFloat).ToListAsync();
        var expenseAmounts = await db.GeneralExpenses.Where(e => e.PostingSessionId == sessionId).Select(e => e.Amount).ToListAsync();
        return new SettlementImpact(journalCash.Count, expenseAmounts.Count, journalCash.Sum() + expenseAmounts.Sum());
    }
}
