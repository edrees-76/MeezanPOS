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

public class PostingServiceTests
{
    private (AppDbContext Context, PostingService Service, Mock<ISessionService> MockSession) CreateServices()
    {
        var context = TestDbContextFactory.Create();
        var mockSession = new Mock<ISessionService>();
        mockSession.Setup(s => s.CurrentUser).Returns(new User { Id = 1, Username = "admin" });
        mockSession.Setup(s => s.CurrentUsername).Returns("admin");

        var cashLedgerService = new CashLedgerService(context, mockSession.Object);
        var auditService = new AuditService(context);
        var postingService = new PostingService(context, cashLedgerService, auditService);

        return (context, postingService, mockSession);
    }

    // GROUP 1: PostDailyJournalAsync
    [Fact]
    public async Task PostJournal_ShouldChangeStatus_FromDraftToPosted()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var journal = TestDataBuilder.BuildDailyJournal(DateTime.UtcNow, FinancialStatus.Draft, 1000m, 300m);
        journal.ActualCash = 1000m;
        journal.CashFloat = 300m;
        context.DailyJournals.Add(journal);
        await context.SaveChangesAsync();

        // Act
        var result = await service.PostEntityAsync<DailyJournal>(journal.Id, "admin");

        // Assert
        result.Should().BeTrue();
        var dbJournal = await context.DailyJournals.FindAsync(journal.Id);
        dbJournal.Should().NotBeNull();
        dbJournal!.FinancialStatus.Should().Be(FinancialStatus.Posted);
        dbJournal.PostedByUserId.Should().Be("admin");
    }

    [Fact]
    public async Task PostJournal_ShouldFail_WhenJournalAlreadyPosted()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var journal = TestDataBuilder.BuildDailyJournal(DateTime.UtcNow, FinancialStatus.Posted, 1000m, 300m);
        context.DailyJournals.Add(journal);
        await context.SaveChangesAsync();

        // Act
        Func<Task> act = async () => await service.PostEntityAsync<DailyJournal>(journal.Id, "admin");

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("لا يمكن ترحيل حركة مرحّلة مسبقاً.");
    }

    [Fact]
    public async Task PostJournal_ShouldCreateCashMovement_ForNetAmount()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var journal = TestDataBuilder.BuildDailyJournal(DateTime.UtcNow, FinancialStatus.Draft, 1000m, 300m);
        journal.ActualCash = 1000m;
        journal.CashFloat = 300m;
        context.DailyJournals.Add(journal);
        await context.SaveChangesAsync();

        // Act
        var result = await service.PostEntityAsync<DailyJournal>(journal.Id, "admin");

        // Assert
        result.Should().BeTrue();
        var movement = await context.CashMovements
            .FirstOrDefaultAsync(m => m.SourceType == SourceTypes.DailyJournal && m.SourceId == journal.Id);
        movement.Should().NotBeNull();
        movement!.Amount.Should().Be(700m);
    }

    // GROUP 2: ReversePostingAsync
    [Fact]
    public async Task ReversePosting_ShouldCreateReversalMovement()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var journal = TestDataBuilder.BuildDailyJournal(DateTime.UtcNow, FinancialStatus.Posted, 1000m, 300m);
        journal.ActualCash = 1000m;
        journal.CashFloat = 300m;
        context.DailyJournals.Add(journal);
        await context.SaveChangesAsync();

        // Seed the original cash movement that was created when posting
        var originalMovement = TestDataBuilder.BuildCashMovement(700m, CashMovementType.CashIn, 700m, DateTime.UtcNow);
        originalMovement.SourceType = SourceTypes.DailyJournal;
        originalMovement.SourceId = journal.Id;
        context.CashMovements.Add(originalMovement);
        await context.SaveChangesAsync();

        // Act
        var reason = "This is a valid long reason for unposting";
        var result = await service.UnpostEntityAsync<DailyJournal>(journal.Id, reason, "admin");

        // Assert
        result.Should().BeTrue();
        
        // The journal should be set to Draft
        var dbJournal = await context.DailyJournals.FindAsync(journal.Id);
        dbJournal.Should().NotBeNull();
        dbJournal!.FinancialStatus.Should().Be(FinancialStatus.Draft);

        // The original movement should be marked as reversed
        var dbOriginal = await context.CashMovements.FindAsync(originalMovement.Id);
        dbOriginal.Should().NotBeNull();
        dbOriginal!.IsReversed.Should().BeTrue();

        // A new reversal movement (CashOut) should be created
        var reversal = await context.CashMovements
            .FirstOrDefaultAsync(m => m.SourceType == SourceTypes.DailyJournal && m.SourceId == journal.Id && m.Id != originalMovement.Id);
        reversal.Should().NotBeNull();
        reversal!.Type.Should().Be(CashMovementType.CashOut);
        reversal.Amount.Should().Be(700m);
    }

    [Fact]
    public async Task ReversePosting_ShouldFail_WhenJournalNotPosted()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var journal = TestDataBuilder.BuildDailyJournal(DateTime.UtcNow, FinancialStatus.Draft, 1000m, 300m);
        context.DailyJournals.Add(journal);
        await context.SaveChangesAsync();

        // Act
        var reason = "This is a valid long reason for unposting";
        Func<Task> act = async () => await service.UnpostEntityAsync<DailyJournal>(journal.Id, reason, "admin");

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("لا يمكن فك ترحيل حركة غير مرحّلة.");
    }

    // GROUP 3: Concurrency
    [Fact]
    public async Task PostJournal_ShouldHandleConcurrencyConflict_Gracefully()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var journal = TestDataBuilder.BuildDailyJournal(DateTime.UtcNow, FinancialStatus.Draft, 1000m, 300m);
        journal.ActualCash = 1000m;
        journal.CashFloat = 300m;
        context.DailyJournals.Add(journal);
        await context.SaveChangesAsync();

        // Enable concurrency conflict simulation on the database context
        var testContext = (TestAppDbContext)context;
        testContext.SimulateConcurrencyConflict = true;

        // Act
        var result = await service.PostEntityAsync<DailyJournal>(journal.Id, "admin");

        // Assert
        result.Should().BeTrue();

        var dbJournal = await context.DailyJournals.FindAsync(journal.Id);
        dbJournal.Should().NotBeNull();
        dbJournal!.FinancialStatus.Should().Be(FinancialStatus.Posted);
        dbJournal.PostedByUserId.Should().Be("admin");
        dbJournal.RowVersion.Should().Be(2); // Initial was 1, then self-heal increases it by 1 + database update
    }
}
