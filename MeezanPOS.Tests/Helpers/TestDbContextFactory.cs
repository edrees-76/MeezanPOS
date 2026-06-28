using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Tests.Helpers;

public class TestAppDbContext : AppDbContext
{
    private readonly string _dbName;
    public bool SimulateConcurrencyConflict { get; set; }

    public TestAppDbContext(string dbName)
    {
        _dbName = dbName;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (SimulateConcurrencyConflict)
        {
            SimulateConcurrencyConflict = false;
            throw new DbUpdateConcurrencyException("Simulated concurrency conflict.");
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}

public static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var dbName = Guid.NewGuid().ToString();
        var context = new TestAppDbContext(dbName);
        context.Database.EnsureCreated();
        return context;
    }
}
