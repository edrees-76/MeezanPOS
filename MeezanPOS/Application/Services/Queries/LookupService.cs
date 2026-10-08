using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.Services.Queries;

/// <summary>
/// قوائم مرجعية تحتاجها عدة شاشات (موردون، عمال، حسابات مصرفية...) وحالة السجلات.
/// كل استدعاء يفتح سياقاً قصير العمر، مثل ما كانت تفعله الشاشات مباشرة.
/// </summary>
public interface ILookupService
{
    Task<List<Supplier>> GetActiveSuppliersAsync();
    Task<List<Worker>> GetActiveWorkersAsync();
    Task<List<BankAccount>> GetActiveBankAccountsAsync();
    Task<FinancialStatus?> GetJournalStatusAsync(int journalId);
}

public sealed class LookupService : ILookupService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public LookupService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public static LookupService Default { get; } = new(DefaultDbContextFactory.Instance);

    public async Task<List<Supplier>> GetActiveSuppliersAsync()
    {
        await using var db = _factory.CreateDbContext();
        return await db.Suppliers.AsNoTracking().Where(s => s.IsActive && !s.IsDeleted).ToListAsync();
    }

    public async Task<List<Worker>> GetActiveWorkersAsync()
    {
        await using var db = _factory.CreateDbContext();
        return await db.Workers.AsNoTracking()
            .Where(w => w.IsActive && !w.IsDeleted)
            .OrderBy(w => w.WorkerName)
            .ToListAsync();
    }

    public async Task<List<BankAccount>> GetActiveBankAccountsAsync()
    {
        await using var db = _factory.CreateDbContext();
        return await db.BankAccounts.AsNoTracking().Where(a => a.IsActive && !a.IsDeleted).ToListAsync();
    }

    public async Task<FinancialStatus?> GetJournalStatusAsync(int journalId)
    {
        await using var db = _factory.CreateDbContext();
        return await db.DailyJournals.Where(j => j.Id == journalId).Select(j => (FinancialStatus?)j.FinancialStatus).FirstOrDefaultAsync();
    }
}
