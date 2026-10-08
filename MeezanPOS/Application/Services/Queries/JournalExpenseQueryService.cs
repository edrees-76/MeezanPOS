using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.Services.Queries;

/// <summary>فلتر يوميات شاشة مصروفات الورديات.</summary>
public sealed record JournalExpenseFilter(
    bool PostedArchive,
    DateTime From,
    DateTime To,
    ShiftType? Shift = null,
    string? CashierName = null,
    bool IncludeSuppliers = false);

/// <summary>مجاميع يومية مرحّلة لبطاقات أشهر الأرشيف.</summary>
public sealed record ArchivedJournalTotals(
    DateTime JournalDate, FinancialStatus FinancialStatus, decimal TotalSales, decimal BankingTotal, decimal TotalExpenses);

/// <summary>استعلامات شاشة مصروفات الورديات وشاشة المجاني والمرتجعات (كانت داخل الشاشات).</summary>
public interface IJournalExpenseQueryService
{
    List<string> GetCashierNames();

    /// <summary>كل اليوميات حسب حالة الأرشيف (لبناء تسلسل الأيام الثابت).</summary>
    List<DailyJournal> GetJournalsWithExpenses(bool postedArchive);

    List<DailyJournal> GetJournalsWithExpenses(JournalExpenseFilter filter);
    Task<List<ArchivedJournalTotals>> GetArchivedJournalTotalsAsync();
    Task<List<DailyJournal>> GetJournalsWithAdjustmentsAsync();
}

public sealed class JournalExpenseQueryService : IJournalExpenseQueryService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public JournalExpenseQueryService(IDbContextFactory<AppDbContext>? factory = null)
        => _factory = factory ?? DefaultDbContextFactory.Instance;

    private static IQueryable<DailyJournal> ByArchive(IQueryable<DailyJournal> q, bool postedArchive) => postedArchive
        ? q.Where(j => j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived)
        : q.Where(j => j.FinancialStatus != FinancialStatus.Posted && j.FinancialStatus != FinancialStatus.Archived);

    public List<string> GetCashierNames()
    {
        using var db = _factory.CreateDbContext();
        return db.DailyJournals
            .Where(j => !string.IsNullOrEmpty(j.EmployeeName))
            .Select(j => j.EmployeeName!)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
    }

    public List<DailyJournal> GetJournalsWithExpenses(bool postedArchive)
    {
        using var db = _factory.CreateDbContext();
        return ByArchive(db.DailyJournals.AsNoTracking().Include(j => j.ExpenseItems), postedArchive).ToList();
    }

    public List<DailyJournal> GetJournalsWithExpenses(JournalExpenseFilter filter)
    {
        using var db = _factory.CreateDbContext();
        IQueryable<DailyJournal> query = filter.IncludeSuppliers
            ? db.DailyJournals.AsNoTracking().Include(j => j.ExpenseItems).ThenInclude(e => e.Supplier)
            : db.DailyJournals.AsNoTracking().Include(j => j.ExpenseItems);

        query = ByArchive(query.Where(j => j.JournalDate >= filter.From && j.JournalDate <= filter.To), filter.PostedArchive);

        if (filter.Shift.HasValue)
            query = query.Where(j => j.ShiftType == filter.Shift.Value);

        if (!string.IsNullOrEmpty(filter.CashierName) && filter.CashierName != "الكل")
            query = query.Where(j => j.EmployeeName == filter.CashierName);

        return query.ToList();
    }

    public async Task<List<ArchivedJournalTotals>> GetArchivedJournalTotalsAsync()
    {
        await using var db = _factory.CreateDbContext();
        // الأعمدة المطلوبة فقط، بلا تحميل بنود كل اليوميات المؤرشفة
        return await db.DailyJournals.AsNoTracking()
            .Where(j => j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived)
            .Select(j => new ArchivedJournalTotals(j.JournalDate, j.FinancialStatus, j.TotalSales, j.BankingTotal, j.TotalExpenses))
            .ToListAsync();
    }

    public async Task<List<DailyJournal>> GetJournalsWithAdjustmentsAsync()
    {
        await using var db = _factory.CreateDbContext();
        return await db.DailyJournals.AsNoTracking()
            .Include(j => j.Adjustments)
            .OrderByDescending(j => j.JournalDate)
            .ToListAsync();
    }
}
