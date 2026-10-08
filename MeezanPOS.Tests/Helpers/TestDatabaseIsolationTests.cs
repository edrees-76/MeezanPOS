using System.IO;
using FluentAssertions;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Tests.Helpers;

public class TestDatabaseIsolationTests
{
    [Fact]
    public void DatabasePath_DuringTests_IsNotTheUsersRealDatabase()
    {
        var realDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeezanPOS");

        AppDbContext.GetDatabasePath().Should().NotStartWith(realDir);
    }
}
