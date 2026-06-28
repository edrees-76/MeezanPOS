using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Tests.Helpers;

namespace MeezanPOS.Tests.Services;

public class CashLedgerServiceTests
{
    private readonly Mock<ISessionService> _mockSession;

    public CashLedgerServiceTests()
    {
        _mockSession = new Mock<ISessionService>();
        _mockSession.Setup(s => s.CurrentUser).Returns(new User { Id = 1, Username = "admin" });
        _mockSession.Setup(s => s.CurrentUsername).Returns("admin");
    }

    // GROUP 1: AddCashMovementAsync (which maps to RecordMovementAsync)
    [Fact]
    public async Task AddCashMovement_ShouldIncreaseBalance_WhenCashIn()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new CashLedgerService(context, _mockSession.Object);

        // Act
        var result = await service.RecordMovementAsync(CashMovementType.CashIn, 1000m, "Test", 1, "Notes");

        // Assert
        result.BalanceAfter.Should().Be(1000m);
        var dbMovement = context.CashMovements.FirstOrDefault(m => m.Id == result.Id);
        dbMovement.Should().NotBeNull();
        dbMovement!.BalanceAfter.Should().Be(1000m);
    }

    [Fact]
    public async Task AddCashMovement_ShouldDecreaseBalance_WhenCashOut()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new CashLedgerService(context, _mockSession.Object);
        // Seed first movement
        await service.RecordMovementAsync(CashMovementType.CashIn, 500m, "Test", 1, "Initial");

        // Act
        var result = await service.RecordMovementAsync(CashMovementType.CashOut, 200m, "Test", 2, "Out");

        // Assert
        result.BalanceAfter.Should().Be(300m);
        (await service.GetCurrentBalanceAsync()).Should().Be(300m);
    }

    [Fact]
    public async Task AddCashMovement_ShouldAllowNegativeBalance_WhenWithdrawalExceedsBalance()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new CashLedgerService(context, _mockSession.Object);

        // Act
        var result = await service.RecordMovementAsync(CashMovementType.CashOut, 500m, "Test", 1, "Notes");

        // Assert
        result.BalanceAfter.Should().Be(-500m);
        (await service.GetCurrentBalanceAsync()).Should().Be(-500m);
    }

    // GROUP 2: RebuildLedgerAsync
    [Fact]
    public async Task RebuildLedger_ShouldRecalculateBalances_InChronologicalOrder()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new CashLedgerService(context, _mockSession.Object);

        // Seed movements with wrong BalanceAfter values directly to DbContext
        var date = DateTime.UtcNow;
        var m1 = new CashMovement { Type = CashMovementType.CashIn, Amount = 100m, BalanceAfter = 9999m, TransactionDate = date.AddMinutes(-5) };
        var m2 = new CashMovement { Type = CashMovementType.CashOut, Amount = 30m, BalanceAfter = 9999m, TransactionDate = date.AddMinutes(-4) };
        var m3 = new CashMovement { Type = CashMovementType.CashIn, Amount = 50m, BalanceAfter = 9999m, TransactionDate = date.AddMinutes(-3) };

        context.CashMovements.AddRange(m1, m2, m3);
        await context.SaveChangesAsync();

        // Act
        await service.RebuildLedgerAsync();

        // Assert
        var movements = context.CashMovements.OrderBy(m => m.Id).ToList();
        movements[0].BalanceAfter.Should().Be(100m);
        movements[1].BalanceAfter.Should().Be(70m);
        movements[2].BalanceAfter.Should().Be(120m);
    }

    [Fact]
    public async Task RebuildLedger_ShouldHandleEmptyLedger_WithoutError()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new CashLedgerService(context, _mockSession.Object);

        // Act
        Func<Task> act = async () => await service.RebuildLedgerAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    // GROUP 3: GetCurrentBalanceAsync
    [Fact]
    public async Task GetCurrentBalance_ShouldReturnZero_WhenNoMovements()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new CashLedgerService(context, _mockSession.Object);

        // Act
        var balance = await service.GetCurrentBalanceAsync();

        // Assert
        balance.Should().Be(0m);
    }

    [Fact]
    public async Task GetCurrentBalance_ShouldReturnCorrectBalance_AfterMultipleMovements()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new CashLedgerService(context, _mockSession.Object);

        // Act
        await service.RecordMovementAsync(CashMovementType.CashIn, 1000m, "Test", 1, "Notes");
        await service.RecordMovementAsync(CashMovementType.CashOut, 300m, "Test", 2, "Notes");
        await service.RecordMovementAsync(CashMovementType.CashIn, 500m, "Test", 3, "Notes");

        // Assert
        var balance = await service.GetCurrentBalanceAsync();
        balance.Should().Be(1200m);
    }
}
