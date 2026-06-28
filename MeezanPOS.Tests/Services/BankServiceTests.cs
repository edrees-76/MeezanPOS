using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Tests.Helpers;

namespace MeezanPOS.Tests.Services;

public class BankServiceTests
{
    private (AppDbContext Context, BankService Service, Mock<ISessionService> MockSession) CreateServices()
    {
        var context = TestDbContextFactory.Create();
        var mockSession = new Mock<ISessionService>();
        mockSession.Setup(s => s.CurrentUser).Returns(new User { Id = 1, Username = "admin" });
        mockSession.Setup(s => s.CurrentUsername).Returns("admin");

        var auditService = new AuditService(context);
        var bankService = new BankService(context, mockSession.Object, auditService);

        return (context, bankService, mockSession);
    }

    // GROUP 1: BankTransaction Recording
    [Fact]
    public async Task AddDeposit_ShouldIncreaseBalance_WhenDepositRecorded()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var account = TestDataBuilder.BuildBankAccount("Friendly Account", 1000m);
        context.BankAccounts.Add(account);
        await context.SaveChangesAsync();

        // Act
        var result = await service.RecordTransactionAsync(
            account.Id,
            BankTransactionType.Deposit,
            500m,
            "REF-DEP",
            "Test Deposit",
            "Manual",
            null,
            DateTime.UtcNow
        );

        // Assert
        result.Should().NotBeNull();
        result.BalanceAfter.Should().Be(1500m);
        
        var dbAccount = await context.BankAccounts.FindAsync(account.Id);
        dbAccount.Should().NotBeNull();
        dbAccount!.CurrentBalance.Should().Be(1500m);
    }

    [Fact]
    public async Task AddWithdrawal_ShouldDecreaseBalance_WhenWithdrawalRecorded()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var account = TestDataBuilder.BuildBankAccount("Friendly Account", 1000m);
        context.BankAccounts.Add(account);
        await context.SaveChangesAsync();

        // Act
        var result = await service.RecordTransactionAsync(
            account.Id,
            BankTransactionType.Withdrawal,
            300m,
            "REF-WITH",
            "Test Withdrawal",
            "Manual",
            null,
            DateTime.UtcNow
        );

        // Assert
        result.Should().NotBeNull();
        result.BalanceAfter.Should().Be(700m);

        var dbAccount = await context.BankAccounts.FindAsync(account.Id);
        dbAccount.Should().NotBeNull();
        dbAccount!.CurrentBalance.Should().Be(700m);
    }

    [Fact]
    public async Task AddWithdrawal_ShouldAllowNegativeBalance_WhenExceedsBalance()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var account = TestDataBuilder.BuildBankAccount("Friendly Account", 100m);
        context.BankAccounts.Add(account);
        await context.SaveChangesAsync();

        // Act
        var result = await service.RecordTransactionAsync(
            account.Id,
            BankTransactionType.Withdrawal,
            300m,
            "REF-WITH-NEG",
            "Exceeding Withdrawal",
            "Manual",
            null,
            DateTime.UtcNow
        );

        // Assert
        result.Should().NotBeNull();
        result.BalanceAfter.Should().Be(-200m);

        var dbAccount = await context.BankAccounts.FindAsync(account.Id);
        dbAccount.Should().NotBeNull();
        dbAccount!.CurrentBalance.Should().Be(-200m);
    }

    // GROUP 2: InternalTransfer
    [Fact]
    public async Task InternalTransfer_ShouldDebitSourceAccount()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var accountSrc = TestDataBuilder.BuildBankAccount("Source Account", 1000m);
        var accountDst = TestDataBuilder.BuildBankAccount("Destination Account", 500m);
        context.BankAccounts.AddRange(accountSrc, accountDst);
        await context.SaveChangesAsync();

        // Act
        await service.RecordInternalTransferAsync(accountSrc.Id, accountDst.Id, 300m, "Transfer 300", DateTime.UtcNow);

        // Assert
        var dbSrc = await context.BankAccounts.FindAsync(accountSrc.Id);
        dbSrc.Should().NotBeNull();
        dbSrc!.CurrentBalance.Should().Be(700m);

        var dbTx = await context.BankTransactions
            .FirstOrDefaultAsync(t => t.BankAccountId == accountSrc.Id && t.Type == BankTransactionType.InternalTransferOut);
        dbTx.Should().NotBeNull();
        dbTx!.Amount.Should().Be(300m);
        dbTx.BalanceAfter.Should().Be(700m);
    }

    [Fact]
    public async Task InternalTransfer_ShouldCreditDestinationAccount()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var accountSrc = TestDataBuilder.BuildBankAccount("Source Account", 1000m);
        var accountDst = TestDataBuilder.BuildBankAccount("Destination Account", 500m);
        context.BankAccounts.AddRange(accountSrc, accountDst);
        await context.SaveChangesAsync();

        // Act
        await service.RecordInternalTransferAsync(accountSrc.Id, accountDst.Id, 300m, "Transfer 300", DateTime.UtcNow);

        // Assert
        var dbDst = await context.BankAccounts.FindAsync(accountDst.Id);
        dbDst.Should().NotBeNull();
        dbDst!.CurrentBalance.Should().Be(800m);

        var dbTx = await context.BankTransactions
            .FirstOrDefaultAsync(t => t.BankAccountId == accountDst.Id && t.Type == BankTransactionType.InternalTransferIn);
        dbTx.Should().NotBeNull();
        dbTx!.Amount.Should().Be(300m);
        dbTx.BalanceAfter.Should().Be(800m);
    }

    [Fact]
    public async Task InternalTransfer_ShouldFail_WhenSourceAccountNotFound()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var accountDst = TestDataBuilder.BuildBankAccount("Destination Account", 500m);
        context.BankAccounts.Add(accountDst);
        await context.SaveChangesAsync();

        // Act
        Func<Task> act = async () => await service.RecordInternalTransferAsync(9999, accountDst.Id, 300m, "Transfer 300", DateTime.UtcNow);

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("أحد الحسابات البنكية غير موجودة.");
    }

    // GROUP 3: Account Balance
    [Fact]
    public async Task GetAccountBalance_ShouldReturnZero_ForNewAccount()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var account = TestDataBuilder.BuildBankAccount("New Account", 0m);
        context.BankAccounts.Add(account);
        await context.SaveChangesAsync();

        // Act
        var dbAccount = await service.GetAccountByIdAsync(account.Id);

        // Assert
        dbAccount.Should().NotBeNull();
        dbAccount!.CurrentBalance.Should().Be(0m);
    }

    [Fact]
    public async Task GetAccountBalance_ShouldReturnCorrectBalance_AfterMultipleTransactions()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var account = TestDataBuilder.BuildBankAccount("Test Account", 1000m);
        context.BankAccounts.Add(account);
        await context.SaveChangesAsync();

        // Act
        await service.RecordDepositAsync(account.Id, 500m, "REF1", "Deposit", DateTime.UtcNow);
        await service.RecordWithdrawalAsync(account.Id, 200m, "REF2", "Withdrawal", DateTime.UtcNow);

        // Assert
        var dbAccount = await service.GetAccountByIdAsync(account.Id);
        dbAccount.Should().NotBeNull();
        dbAccount!.CurrentBalance.Should().Be(1300m);
    }
}
