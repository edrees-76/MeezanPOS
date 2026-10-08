using System;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace MeezanPOS.Tests.Helpers;

/// <summary>
/// قاعدة SQLite في الذاكرة يتشاركها أكثر من سياق (اتصال واحد مفتوح لا يُغلق مع السياقات).
/// تُستخدم لاختبار خدمات تفتح سياقات قصيرة العمر أو نطاقات خدمات مثل التطبيق الحقيقي.
/// </summary>
public sealed class SharedSqliteDatabase : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;

    public SharedSqliteDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var ctx = CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new SharedConnectionContext(_connection);

    /// <summary>حاوية خدمات مثل AppServiceProvider لكن على هذه القاعدة.</summary>
    public ServiceProvider BuildServices()
    {
        var session = new Mock<ISessionService>();
        session.Setup(s => s.CurrentUser).Returns(new MeezanPOS.Domain.Entities.User { Id = 1, Username = "admin" });
        session.Setup(s => s.CurrentUsername).Returns("admin");
        session.Setup(s => s.CurrentUserId).Returns("1");

        var services = new ServiceCollection();
        services.AddScoped<AppDbContext>(_ => CreateDbContext());
        services.AddSingleton(session.Object);
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<IBankService, BankService>();
        services.AddScoped<ICashLedgerService, CashLedgerService>();
        services.AddScoped<IOwnerDebtService, OwnerDebtService>();
        services.AddScoped<AuditService>();
        return services.BuildServiceProvider();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class SharedConnectionContext : AppDbContext
    {
        private readonly SqliteConnection _connection;
        public SharedConnectionContext(SqliteConnection connection) => _connection = connection;
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlite(_connection);
    }
}
