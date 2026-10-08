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

/// <summary>
/// اختبارات صحة رصيد الخزينة في مسارات الترحيل وفك الترحيل والتقارير.
/// تعمل على SQLite حقيقية في الذاكرة لتطبيق المعاملات وترجمة الاستعلامات كما في الإنتاج.
/// </summary>
public class CashCorrectnessTests
{
    private const string Reason = "سبب اختباري معتمد لفك الترحيل المالي للوردية";

    private sealed record Services(
        AppDbContext Context,
        PostingService Posting,
        CashLedgerService Cash,
        LedgerService Ledger,
        FinancialReportingService Reporting);

    private static Services Create()
    {
        var context = SqliteTestDbContextFactory.Create();
        var session = new Mock<ISessionService>();
        session.Setup(s => s.CurrentUser).Returns(new User { Id = 1, Username = "admin" });
        session.Setup(s => s.CurrentUsername).Returns("admin");
        session.Setup(s => s.CurrentUserId).Returns("1");

        var audit = new AuditService(context);
        var cash = new CashLedgerService(context, session.Object);
        var bank = new BankService(context, session.Object, audit);
        var ownerDebt = new OwnerDebtService(context, session.Object, bank, audit);
        var ledger = new LedgerService(context, session.Object, bank, ownerDebt, cash, audit);
        var posting = new PostingService(context, cash, audit);
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(() => context);
        var reporting = new FinancialReportingService(factory.Object);

        return new Services(context, posting, cash, ledger, reporting);
    }

    private static async Task<DailyJournal> AddJournalAsync(AppDbContext context, decimal actualCash, decimal cashFloat, DateTime? date = null)
    {
        var journal = TestDataBuilder.BuildDailyJournal(date ?? DateTime.Today, FinancialStatus.Draft, actualCash, 0m);
        journal.ActualCash = actualCash;
        journal.CashFloat = cashFloat;
        context.DailyJournals.Add(journal);
        await context.SaveChangesAsync();
        return journal;
    }

    [Fact]
    public async Task RepeatedPostUnpostCycles_LeaveCashBalanceAtZero()
    {
        var s = Create();
        var journal = await AddJournalAsync(s.Context, actualCash: 1300m, cashFloat: 300m);

        for (int cycle = 0; cycle < 3; cycle++)
        {
            await s.Posting.PostEntityAsync<DailyJournal>(journal.Id, "1");
            (await s.Cash.GetCurrentBalanceAsync()).Should().Be(1000m, $"after post #{cycle + 1}");

            await s.Posting.UnpostEntityAsync<DailyJournal>(journal.Id, Reason, "1");
            (await s.Cash.GetCurrentBalanceAsync()).Should().Be(0m, $"after unpost #{cycle + 1}");
        }

        // لا توجد حركة سارية متبقية للوردية بعد آخر فك ترحيل
        var live = await s.Context.CashMovements
            .FindLiveForSourceAsync(SourceTypes.DailyJournal, journal.Id);
        live.Should().BeNull();

        // إعادة بناء الدفتر تعطي نفس الرصيد
        await s.Cash.RebuildLedgerAsync();
        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(0m);
    }

    [Fact]
    public async Task UnpostingExpense_DoesNotTouchCash()
    {
        var s = Create();
        var expense = new GeneralExpense
        {
            ExpenseType = GeneralExpenseType.Other,
            Amount = 200m,
            PaymentDate = DateTime.Today,
            PaymentMethod = PaymentMethodType.Cash,
            Description = "مصروف نقدي",
            FinancialStatus = FinancialStatus.Draft
        };
        s.Context.GeneralExpenses.Add(expense);
        await s.Context.SaveChangesAsync();
        // الحركة النقدية للمصروف تُسجل عند إنشائه
        await s.Cash.RecordMovementAsync(CashMovementType.CashOut, 200m, SourceTypes.GeneralExpense, expense.Id, "مصروف", DateTime.Today);

        await s.Posting.PostEntityAsync<GeneralExpense>(expense.Id, "1");
        await s.Posting.UnpostEntityAsync<GeneralExpense>(expense.Id, Reason, "1");
        await s.Posting.PostEntityAsync<GeneralExpense>(expense.Id, "1");

        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(-200m);
    }

    [Theory]
    [InlineData(300, 300, 0)]      // كل المبيعات بالبطاقة: لا حركة نقدية
    [InlineData(250, 300, -50)]    // عجز عن العهدة: صادر 50
    [InlineData(800, 300, 500)]
    public async Task PostingJournal_RecordsSignedNetCash(decimal actual, decimal cashFloat, decimal expectedBalance)
    {
        var s = Create();
        var journal = await AddJournalAsync(s.Context, actual, cashFloat);

        var ok = await s.Posting.PostEntityAsync<DailyJournal>(journal.Id, "1");

        ok.Should().BeTrue();
        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(expectedBalance);
        (await s.Context.CashMovements.AnyAsync(m => m.Amount <= 0)).Should().BeFalse();

        await s.Posting.UnpostEntityAsync<DailyJournal>(journal.Id, Reason, "1");
        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(0m);
    }

    [Fact]
    public async Task UnpostPeriod_ReversesEachJournal_AndSurvivesRebuild()
    {
        var s = Create();
        var j1 = await AddJournalAsync(s.Context, 1300m, 300m);
        var j2 = await AddJournalAsync(s.Context, 800m, 300m);
        await s.Posting.PostEntityAsync<DailyJournal>(j1.Id, "1");
        await s.Posting.PostEntityAsync<DailyJournal>(j2.Id, "1");

        var expense = new GeneralExpense
        {
            ExpenseType = GeneralExpenseType.Other,
            Amount = 100m,
            PaymentDate = DateTime.Today,
            PaymentMethod = PaymentMethodType.Cash,
            Description = "مصروف",
            FinancialStatus = FinancialStatus.Posted
        };
        s.Context.GeneralExpenses.Add(expense);
        await s.Context.SaveChangesAsync();
        await s.Cash.RecordMovementAsync(CashMovementType.CashOut, 100m, SourceTypes.GeneralExpense, expense.Id, "مصروف", DateTime.Today);
        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(1400m);

        var session = new PostingSession
        {
            CreatedBy = "1",
            PeriodStartDate = DateTime.Today.AddDays(-1),
            PeriodEndDate = DateTime.Today.AddDays(1),
            SessionType = PostingSessionType.Settlement,
            Status = PostingSessionStatus.Unlocked
        };
        s.Context.PostingSessions.Add(session);
        await s.Context.SaveChangesAsync();
        j1.PostingSessionId = session.Id;
        j2.PostingSessionId = session.Id;
        expense.PostingSessionId = session.Id;
        await s.Context.SaveChangesAsync();

        await s.Posting.UnpostPeriodAsync(session.Id, Reason, "1");

        // تبقى حركة المصروف فقط (لأنها تخص إنشاءه لا ترحيله)
        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(-100m);
        (await s.Context.CashMovements.AnyAsync(m => m.Amount <= 0)).Should().BeFalse();

        await s.Cash.RebuildLedgerAsync();
        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(-100m);
    }

    [Fact]
    public async Task SettlementHistory_LoadsWithoutOwnerWithdrawal()
    {
        var s = Create();
        await s.Cash.RecordMovementAsync(CashMovementType.CashIn, 500m, "Manual", null, "رصيد", DateTime.Today.AddDays(-2));
        s.Context.PostingSessions.Add(new PostingSession
        {
            CreatedBy = "1",
            PeriodStartDate = DateTime.Today.AddDays(-7),
            PeriodEndDate = DateTime.Today,
            SessionType = PostingSessionType.Settlement,
            Status = PostingSessionStatus.Settled
        });
        await s.Context.SaveChangesAsync();

        var history = await s.Posting.GetSettlementHistoryAsync();

        history.Should().ContainSingle();
        history[0].PayoutAmount.Should().Be(0m);
        history[0].KeepAmount.Should().Be(500m);
    }

    [Fact]
    public async Task SupplierPayment_PaidPersonallyByPartner_Succeeds()
    {
        var s = Create();
        var supplier = TestDataBuilder.BuildSupplier("مورد");
        supplier.CurrentBalance = 1000m;
        s.Context.Suppliers.Add(supplier);
        await s.Context.SaveChangesAsync();

        await s.Ledger.PostPaymentAsync(
            supplier.Id, 400m, TransactionSourceType.ExternalPayment, 0, DateTime.Today,
            partnerName: "الشريك");

        var reloaded = await s.Context.Suppliers.AsNoTracking().FirstAsync(x => x.Id == supplier.Id);
        reloaded.CurrentBalance.Should().Be(600m);
        (await s.Context.OwnerDebts.CountAsync()).Should().Be(1);
        (await s.Context.OwnerDebts.SumAsync(d => (double)d.Amount)).Should().Be(400d);
    }

    [Fact]
    public async Task CashReport_ReconcilesAndIgnoresReversalPairs()
    {
        var s = Create();
        var start = DateTime.Today.AddDays(-1);
        var end = DateTime.Today.AddDays(1);
        await s.Cash.RecordMovementAsync(CashMovementType.CashIn, 1000m, "Manual", null, "قبل الفترة", start.AddDays(-5));
        var journal = await AddJournalAsync(s.Context, 800m, 300m);
        await s.Posting.PostEntityAsync<DailyJournal>(journal.Id, "1");
        await s.Posting.UnpostEntityAsync<DailyJournal>(journal.Id, Reason, "1");
        await s.Cash.RecordMovementAsync(CashMovementType.CashOut, 200m, "Manual", null, "داخل الفترة", DateTime.Today);

        var summary = await s.Reporting.GenerateSummaryAsync(start, end);

        summary.CashLedger.OpeningBalance.Should().Be(1000m);
        summary.CashLedger.TotalCashIn.Should().Be(0m);
        summary.CashLedger.TotalCashOut.Should().Be(200m);
        summary.CashLedger.ClosingBalance.Should().Be(800m);
    }
}
