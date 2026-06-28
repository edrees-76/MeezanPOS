using System;
using System.Collections.Generic;
using System.Linq;
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

public class WagesServiceTests
{
    private (AppDbContext Context, WagesService Service) CreateService()
    {
        var context = TestDbContextFactory.Create();
        var service = new WagesService(context, null);
        return (context, service);
    }

    // GROUP 1: Worker Transactions
    [Fact]
    public async Task AddAdvance_ShouldCreateDebitTransaction_ForWorker()
    {
        // Arrange
        var (context, service) = CreateService();
        var worker = TestDataBuilder.BuildWorker("Worker A", 100m);
        context.Workers.Add(worker);
        await context.SaveChangesAsync();

        var tx = new WorkerTransaction
        {
            WorkerId = worker.Id,
            Type = WorkerTransactionType.Advance,
            DebitAmount = 200m,
            CreditAmount = 0m,
            TransactionDate = DateTime.UtcNow
        };

        // Act
        await service.RecordTransactionAsync(tx);

        // Assert
        var dbTx = await context.WorkerTransactions.FindAsync(tx.Id);
        dbTx.Should().NotBeNull();
        dbTx!.Type.Should().Be(WorkerTransactionType.Advance);
        dbTx.DebitAmount.Should().Be(200m);
        dbTx.CreditAmount.Should().Be(0m);
    }

    [Fact]
    public async Task AddPayment_ShouldCreateCreditTransaction_ForWorker()
    {
        // Arrange
        var (context, service) = CreateService();
        var worker = TestDataBuilder.BuildWorker("Worker A", 100m);
        context.Workers.Add(worker);
        await context.SaveChangesAsync();

        var tx = new WorkerTransaction
        {
            WorkerId = worker.Id,
            Type = WorkerTransactionType.Payment,
            CreditAmount = 150m,
            DebitAmount = 0m,
            TransactionDate = DateTime.UtcNow
        };

        // Act
        await service.RecordTransactionAsync(tx);

        // Assert
        var dbTx = await context.WorkerTransactions.FindAsync(tx.Id);
        dbTx.Should().NotBeNull();
        dbTx!.Type.Should().Be(WorkerTransactionType.Payment);
        dbTx.CreditAmount.Should().Be(150m);
        dbTx.DebitAmount.Should().Be(0m);
    }

    // GROUP 2: Balance Calculation
    [Fact]
    public async Task GetWorkerBalance_ShouldReturnZero_ForNewWorker()
    {
        // Arrange
        var (context, service) = CreateService();
        var worker = TestDataBuilder.BuildWorker("New Worker", 100m);
        context.Workers.Add(worker);
        await context.SaveChangesAsync();

        // Act
        var summaries = await service.GetWorkerSummariesAsync();

        // Assert
        summaries.Should().NotBeEmpty();
        var summary = summaries.First(s => s.WorkerId == worker.Id);
        summary.Balance.Should().Be(0m);
    }

    [Fact]
    public async Task GetWorkerBalance_ShouldReflect_AdvancesAndPayments()
    {
        // Arrange
        var (context, service) = CreateService();
        var worker = TestDataBuilder.BuildWorker("Worker A", 100m);
        context.Workers.Add(worker);
        await context.SaveChangesAsync();

        // Seed some worker transactions
        var txAccrued = new WorkerTransaction
        {
            WorkerId = worker.Id,
            Type = WorkerTransactionType.WageAccrual,
            CreditAmount = 500m,
            DebitAmount = 0m,
            TransactionDate = DateTime.UtcNow
        };
        var txPaid = new WorkerTransaction
        {
            WorkerId = worker.Id,
            Type = WorkerTransactionType.Payment,
            CreditAmount = 0m,
            DebitAmount = 200m,
            TransactionDate = DateTime.UtcNow
        };
        context.WorkerTransactions.AddRange(txAccrued, txPaid);
        await context.SaveChangesAsync();

        // Act
        var summaries = await service.GetWorkerSummariesAsync();

        // Assert
        summaries.Should().NotBeEmpty();
        var summary = summaries.First(s => s.WorkerId == worker.Id);
        summary.TotalAccrued.Should().Be(500m);
        summary.TotalPaid.Should().Be(200m);
        summary.Balance.Should().Be(300m);
    }

    [Fact]
    public async Task WorkerBalance_ShouldBeNegative_WhenAdvancesExceedPayments()
    {
        // Arrange
        var (context, service) = CreateService();
        var worker = TestDataBuilder.BuildWorker("Worker A", 100m);
        context.Workers.Add(worker);
        await context.SaveChangesAsync();

        // Seed more advances (debits) than accruals (credits)
        var txAccrued = new WorkerTransaction
        {
            WorkerId = worker.Id,
            Type = WorkerTransactionType.WageAccrual,
            CreditAmount = 100m,
            DebitAmount = 0m,
            TransactionDate = DateTime.UtcNow
        };
        var txAdvance = new WorkerTransaction
        {
            WorkerId = worker.Id,
            Type = WorkerTransactionType.Advance,
            CreditAmount = 0m,
            DebitAmount = 300m,
            TransactionDate = DateTime.UtcNow
        };
        context.WorkerTransactions.AddRange(txAccrued, txAdvance);
        await context.SaveChangesAsync();

        // Act
        var summaries = await service.GetWorkerSummariesAsync();

        // Assert
        summaries.Should().NotBeEmpty();
        var summary = summaries.First(s => s.WorkerId == worker.Id);
        summary.TotalAccrued.Should().Be(100m);
        summary.TotalPaid.Should().Be(300m);
        summary.Balance.Should().Be(-200m);
    }

    // GROUP 3: Monthly Summary
    [Fact]
    public async Task GetMonthlySummary_ShouldGroupTransactions_ByWorker()
    {
        // Arrange
        var (context, service) = CreateService();
        var worker1 = TestDataBuilder.BuildWorker("Worker 1", 100m);
        var worker2 = TestDataBuilder.BuildWorker("Worker 2", 120m);
        context.Workers.AddRange(worker1, worker2);
        await context.SaveChangesAsync();

        var tx1 = new WorkerTransaction { WorkerId = worker1.Id, Type = WorkerTransactionType.WageAccrual, CreditAmount = 100m, DebitAmount = 0m, TransactionDate = DateTime.UtcNow };
        var tx2 = new WorkerTransaction { WorkerId = worker1.Id, Type = WorkerTransactionType.Payment, CreditAmount = 0m, DebitAmount = 40m, TransactionDate = DateTime.UtcNow };
        var tx3 = new WorkerTransaction { WorkerId = worker2.Id, Type = WorkerTransactionType.WageAccrual, CreditAmount = 200m, DebitAmount = 0m, TransactionDate = DateTime.UtcNow };
        context.WorkerTransactions.AddRange(tx1, tx2, tx3);
        await context.SaveChangesAsync();

        // Act
        var summaries = await service.GetWorkerSummariesAsync();

        // Assert
        summaries.Should().HaveCount(2);
        
        var summary1 = summaries.First(s => s.WorkerId == worker1.Id);
        summary1.TotalAccrued.Should().Be(100m);
        summary1.TotalPaid.Should().Be(40m);
        summary1.Balance.Should().Be(60m);

        var summary2 = summaries.First(s => s.WorkerId == worker2.Id);
        summary2.TotalAccrued.Should().Be(200m);
        summary2.TotalPaid.Should().Be(0m);
        summary2.Balance.Should().Be(200m);
    }

    [Fact]
    public async Task GetMonthlySummary_ShouldReturnEmpty_WhenNoTransactions()
    {
        // Arrange
        var (context, service) = CreateService();
        // Clear all workers to check if the summaries returned are empty
        var workers = await context.Workers.ToListAsync();
        context.Workers.RemoveRange(workers);
        await context.SaveChangesAsync();

        // Act
        var summaries = await service.GetWorkerSummariesAsync();

        // Assert
        summaries.Should().BeEmpty();
    }
}
