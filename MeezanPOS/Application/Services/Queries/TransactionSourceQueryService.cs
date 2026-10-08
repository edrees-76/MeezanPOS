using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.Services.Queries;

/// <summary>
/// مصدر حركة مالية لنافذة تفاصيل الحركة: الحركة النقدية والسجل الذي أنشأها
/// (كانت النافذة تفتح قاعدة البيانات بنفسها في سبعة مواضع).
/// </summary>
public interface ITransactionSourceQueryService
{
    Task<CashMovement?> GetCashMovementAsync(string sourceType, int sourceId);
    Task<GeneralExpense?> GetGeneralExpenseAsync(int id);
    /// <summary>دفعة المورد مع المورد والفاتورة وأصنافها.</summary>
    Task<SupplierTransaction?> GetSupplierPaymentAsync(int id);
    Task<OwnerDebt?> GetOwnerDebtAsync(int id);
    Task<BankAccount?> GetBankAccountAsync(int id);
    Task<OwnerDebtSettlement?> GetSettlementAsync(int id);
    Task<DailyJournal?> GetJournalAsync(int id);
    /// <summary>اليومية مع بنودها لفتحها للعرض.</summary>
    Task<DailyJournal?> GetJournalWithItemsAsync(int id);
}

public sealed class TransactionSourceQueryService : ITransactionSourceQueryService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public TransactionSourceQueryService(IDbContextFactory<AppDbContext>? factory = null)
        => _factory = factory ?? DefaultDbContextFactory.Instance;

    public async Task<CashMovement?> GetCashMovementAsync(string sourceType, int sourceId)
    {
        await using var db = _factory.CreateDbContext();
        return await db.CashMovements.AsNoTracking()
            .FirstOrDefaultAsync(m => m.SourceType == sourceType && m.SourceId == sourceId);
    }

    public async Task<GeneralExpense?> GetGeneralExpenseAsync(int id)
    {
        await using var db = _factory.CreateDbContext();
        return await db.GeneralExpenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<SupplierTransaction?> GetSupplierPaymentAsync(int id)
    {
        await using var db = _factory.CreateDbContext();
        return await db.SupplierTransactions.AsNoTracking()
            .Include(t => t.Supplier)
            .Include(t => t.SupplierInvoice)
                .ThenInclude(i => i!.Items)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<OwnerDebt?> GetOwnerDebtAsync(int id)
    {
        await using var db = _factory.CreateDbContext();
        return await db.OwnerDebts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<BankAccount?> GetBankAccountAsync(int id)
    {
        await using var db = _factory.CreateDbContext();
        return await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<OwnerDebtSettlement?> GetSettlementAsync(int id)
    {
        await using var db = _factory.CreateDbContext();
        return await db.OwnerDebtSettlements.AsNoTracking()
            .Include(s => s.BankAccount)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<DailyJournal?> GetJournalAsync(int id)
    {
        await using var db = _factory.CreateDbContext();
        return await db.DailyJournals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<DailyJournal?> GetJournalWithItemsAsync(int id)
    {
        await using var db = _factory.CreateDbContext();
        return await db.DailyJournals.AsNoTracking()
            .Include(j => j.ExpenseItems)
            .Include(j => j.BankingItems)
            .Include(j => j.Adjustments)
            .FirstOrDefaultAsync(x => x.Id == id);
    }
}
