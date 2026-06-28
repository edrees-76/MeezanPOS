using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Tests.Helpers;

namespace MeezanPOS.Tests.Services;

public class TestDbContextFactoryWrapper : IDbContextFactory<AppDbContext>
{
    private readonly AppDbContext _context;

    public TestDbContextFactoryWrapper(AppDbContext context)
    {
        _context = context;
    }

    public AppDbContext CreateDbContext() => _context;

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) 
        => Task.FromResult(_context);
}

public class FinancialReportingServiceTests
{
    private (AppDbContext Context, FinancialReportingService Service) CreateService()
    {
        var context = TestDbContextFactory.Create();
        var factory = new TestDbContextFactoryWrapper(context);
        var service = new FinancialReportingService(factory);
        return (context, service);
    }

    // GROUP 1: Cash Balance
    [Fact]
    public async Task OpeningBalance_ShouldBeZero_WhenNoTransactionsBeforePeriod()
    {
        // Arrange
        var (context, service) = CreateService();
        var start = DateTime.Today;
        var end = DateTime.Today.AddDays(1);

        // Act
        var summary = await service.GenerateSummaryAsync(start, end);

        // Assert
        summary.CashLedger.OpeningBalance.Should().Be(0m);
        summary.CashLedger.TotalCashIn.Should().Be(0m);
        summary.CashLedger.TotalCashOut.Should().Be(0m);
        summary.CashLedger.ClosingBalance.Should().Be(0m);
    }

    [Fact]
    public async Task OpeningBalance_ShouldMatchLastBalanceAfter_BeforePeriodStart()
    {
        // Arrange
        var (context, service) = CreateService();
        var start = DateTime.Today;
        var end = DateTime.Today.AddDays(1);

        // Seed prior cash movements
        var m1 = TestDataBuilder.BuildCashMovement(500m, CashMovementType.CashIn, 500m, start.AddDays(-2));
        var m2 = TestDataBuilder.BuildCashMovement(200m, CashMovementType.CashOut, 300m, start.AddDays(-1));
        context.CashMovements.AddRange(m1, m2);
        await context.SaveChangesAsync();

        // Act
        var summary = await service.GenerateSummaryAsync(start, end);

        // Assert
        summary.CashLedger.OpeningBalance.Should().Be(300m);
        summary.CashLedger.ClosingBalance.Should().Be(300m); // No activity in period
    }

    [Fact]
    public async Task ClosingBalance_ShouldMatchLastBalanceAfter_InPeriod()
    {
        // Arrange
        var (context, service) = CreateService();
        var start = DateTime.Today;
        var end = DateTime.Today.AddDays(1);

        // Seed prior and in-period movements
        var m1 = TestDataBuilder.BuildCashMovement(500m, CashMovementType.CashIn, 500m, start.AddDays(-1));
        var m2 = TestDataBuilder.BuildCashMovement(300m, CashMovementType.CashIn, 800m, start.AddHours(2));
        var m3 = TestDataBuilder.BuildCashMovement(150m, CashMovementType.CashOut, 650m, start.AddHours(4));
        context.CashMovements.AddRange(m1, m2, m3);
        await context.SaveChangesAsync();

        // Act
        var summary = await service.GenerateSummaryAsync(start, end);

        // Assert
        summary.CashLedger.OpeningBalance.Should().Be(500m);
        summary.CashLedger.TotalCashIn.Should().Be(300m);
        summary.CashLedger.TotalCashOut.Should().Be(150m);
        summary.CashLedger.ClosingBalance.Should().Be(650m);
    }

    // GROUP 2: Bank Balance
    [Fact]
    public async Task BankOpeningBalance_ShouldAccumulatePriorTransactions()
    {
        // Arrange
        var (context, service) = CreateService();
        var start = DateTime.Today;
        var end = DateTime.Today.AddDays(1);

        var account = TestDataBuilder.BuildBankAccount("Al-Aman Bank", 1000m);
        context.BankAccounts.Add(account);
        await context.SaveChangesAsync();

        // Seed transactions prior to start
        var tx1 = TestDataBuilder.BuildBankTransaction(500m, BankTransactionType.Deposit, account.Id, start.AddDays(-2));
        var tx2 = TestDataBuilder.BuildBankTransaction(200m, BankTransactionType.Withdrawal, account.Id, start.AddDays(-1));
        context.BankTransactions.AddRange(tx1, tx2);
        await context.SaveChangesAsync();

        // Act
        var summary = await service.GenerateSummaryAsync(start, end);

        // Assert
        summary.BankAccounts.Should().NotBeEmpty();
        var reportItem = summary.BankAccounts.First(b => b.BankAccountId == account.Id);
        reportItem.OpeningBalance.Should().Be(1300m); // 1000 + 500 - 200 = 1300
        reportItem.ClosingBalance.Should().Be(1300m); // No activity in period
    }

    [Fact]
    public async Task BankClosingBalance_ShouldEqual_OpeningPlusDepositMinusWithdrawal()
    {
        // Arrange
        var (context, service) = CreateService();
        var start = DateTime.Today;
        var end = DateTime.Today.AddDays(1);

        var account = TestDataBuilder.BuildBankAccount("Al-Aman Bank", 1000m);
        context.BankAccounts.Add(account);
        await context.SaveChangesAsync();

        // Seed period transactions
        var tx1 = TestDataBuilder.BuildBankTransaction(600m, BankTransactionType.Deposit, account.Id, start.AddHours(2));
        var tx2 = TestDataBuilder.BuildBankTransaction(300m, BankTransactionType.Withdrawal, account.Id, start.AddHours(4));
        context.BankTransactions.AddRange(tx1, tx2);
        await context.SaveChangesAsync();

        // Act
        var summary = await service.GenerateSummaryAsync(start, end);

        // Assert
        summary.BankAccounts.Should().NotBeEmpty();
        var reportItem = summary.BankAccounts.First(b => b.BankAccountId == account.Id);
        reportItem.OpeningBalance.Should().Be(1000m);
        reportItem.TotalDeposits.Should().Be(600m);
        reportItem.TotalWithdrawals.Should().Be(300m);
        reportItem.ClosingBalance.Should().Be(1300m);
    }

    [Fact]
    public async Task BankBalance_ShouldHandleNewAccount_WithNoPriorTransactions()
    {
        // Arrange
        var (context, service) = CreateService();
        var start = DateTime.Today;
        var end = DateTime.Today.AddDays(1);

        var account = TestDataBuilder.BuildBankAccount("New Bank Account", 500m);
        context.BankAccounts.Add(account);
        await context.SaveChangesAsync();

        // Act
        var summary = await service.GenerateSummaryAsync(start, end);

        // Assert
        summary.BankAccounts.Should().NotBeEmpty();
        var reportItem = summary.BankAccounts.First(b => b.BankAccountId == account.Id);
        reportItem.OpeningBalance.Should().Be(500m);
        reportItem.TotalDeposits.Should().Be(0m);
        reportItem.TotalWithdrawals.Should().Be(0m);
        reportItem.ClosingBalance.Should().Be(500m);
    }

    // GROUP 3: Supplier Balances
    [Fact]
    public async Task SupplierBalance_ShouldIncrease_OnNewInvoice()
    {
        // Arrange
        var (context, service) = CreateService();
        var start = DateTime.Today;
        var end = DateTime.Today.AddDays(1);

        var supplier = TestDataBuilder.BuildSupplier("Supplier A");
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        // Seed a transaction representing an invoice (increase debt)
        var tx = TestDataBuilder.BuildSupplierTransaction(1000m, SupplierTransactionType.IncreaseDebt, supplier.Id, start.AddHours(2), 1000m);
        context.SupplierTransactions.Add(tx);
        await context.SaveChangesAsync();

        // Act
        var summary = await service.GenerateSummaryAsync(start, end);

        // Assert
        summary.Suppliers.Should().NotBeEmpty();
        var reportItem = summary.Suppliers.First(s => s.SupplierId == supplier.Id);
        reportItem.OpeningBalance.Should().Be(0m);
        reportItem.TotalPurchases.Should().Be(1000m);
        reportItem.TotalPayments.Should().Be(0m);
        reportItem.ClosingBalance.Should().Be(1000m);
    }

    [Fact]
    public async Task SupplierBalance_ShouldDecrease_OnPayment()
    {
        // Arrange
        var (context, service) = CreateService();
        var start = DateTime.Today;
        var end = DateTime.Today.AddDays(1);

        var supplier = TestDataBuilder.BuildSupplier("Supplier A");
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        // Seed prior invoice transaction
        var txPrior = TestDataBuilder.BuildSupplierTransaction(1000m, SupplierTransactionType.IncreaseDebt, supplier.Id, start.AddDays(-1), 1000m);
        
        // Seed period payment transaction
        var txPayment = TestDataBuilder.BuildSupplierTransaction(400m, SupplierTransactionType.DecreaseDebt, supplier.Id, start.AddHours(2), 600m);
        
        context.SupplierTransactions.AddRange(txPrior, txPayment);
        await context.SaveChangesAsync();

        // Act
        var summary = await service.GenerateSummaryAsync(start, end);

        // Assert
        summary.Suppliers.Should().NotBeEmpty();
        var reportItem = summary.Suppliers.First(s => s.SupplierId == supplier.Id);
        reportItem.OpeningBalance.Should().Be(1000m);
        reportItem.TotalPurchases.Should().Be(0m);
        reportItem.TotalPayments.Should().Be(400m);
        reportItem.ClosingBalance.Should().Be(600m);
    }

    [Fact]
    public async Task SupplierBalance_ShouldBeZero_WhenFullyPaid()
    {
        // Arrange
        var (context, service) = CreateService();
        var start = DateTime.Today;
        var end = DateTime.Today.AddDays(1);

        var supplier = TestDataBuilder.BuildSupplier("Supplier A");
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        // Seed prior invoice transaction
        var txPrior = TestDataBuilder.BuildSupplierTransaction(1000m, SupplierTransactionType.IncreaseDebt, supplier.Id, start.AddDays(-1), 1000m);

        // Seed period payment transaction bringing balance to zero
        var txPayment = TestDataBuilder.BuildSupplierTransaction(1000m, SupplierTransactionType.DecreaseDebt, supplier.Id, start.AddHours(2), 0m);

        context.SupplierTransactions.AddRange(txPrior, txPayment);
        await context.SaveChangesAsync();

        // Act
        var summary = await service.GenerateSummaryAsync(start, end);

        // Assert
        summary.Suppliers.Should().NotBeEmpty();
        var reportItem = summary.Suppliers.First(s => s.SupplierId == supplier.Id);
        reportItem.OpeningBalance.Should().Be(1000m);
        reportItem.TotalPurchases.Should().Be(0m);
        reportItem.TotalPayments.Should().Be(1000m);
        reportItem.ClosingBalance.Should().Be(0m);
    }
}
