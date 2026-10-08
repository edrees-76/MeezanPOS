using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Tests.Helpers;

/// <summary>
/// سياق قاعدة بيانات SQLite حقيقية في الذاكرة.
/// على عكس مزود InMemory، هذا المزود يطبق المعاملات (Transactions) وترجمة الاستعلامات كما في الإنتاج،
/// لذا يُستخدم لاختبار مسارات المعاملات المتداخلة والاستعلامات التي لا يمكن ترجمتها.
/// </summary>
public class SqliteTestAppDbContext : AppDbContext
{
    private readonly SqliteConnection _connection;

    public SqliteTestAppDbContext(SqliteConnection connection)
    {
        _connection = connection;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(_connection);
    }

    public override void Dispose()
    {
        base.Dispose();
        _connection.Dispose();
    }
}

public static class SqliteTestDbContextFactory
{
    public static AppDbContext Create()
    {
        // الاتصال يبقى مفتوحاً طوال عمر السياق حتى لا تُحذف قاعدة البيانات الموجودة في الذاكرة
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var context = new SqliteTestAppDbContext(connection);
        context.Database.EnsureCreated();
        return context;
    }
}
