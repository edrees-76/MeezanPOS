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
/// اختبارات بنود المراجعة المالية المتوسطة: تسوية ديون الشركاء، دفعات الموردين، كشف المورد،
/// التحقق من المبالغ، وأرصدة العمال.
/// </summary>
public class FinancialFixesTests
{
    private sealed record Services(
        AppDbContext Context,
        CashLedgerService Cash,
        BankService Bank,
        OwnerDebtService OwnerDebt,
        LedgerService Ledger,
        WagesService Wages,
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
        var bank = new BankService(context, session.Object, audit, cash);
        var ownerDebt = new OwnerDebtService(context, session.Object, bank, audit, cash);
        var ledger = new LedgerService(context, session.Object, bank, ownerDebt, cash, audit);
        var wages = new WagesService(context, session.Object);
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(() => context);
        var reporting = new FinancialReportingService(factory.Object);

        return new Services(context, cash, bank, ownerDebt, ledger, wages, reporting);
    }

    private static async Task<Supplier> AddSupplierAsync(AppDbContext context, decimal openingBalance = 0m)
    {
        var supplier = TestDataBuilder.BuildSupplier("مورد اختبار");
        supplier.OpeningBalance = openingBalance;
        supplier.CurrentBalance = openingBalance;
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();
        return supplier;
    }

    // التسوية من صندوق الكاشير تُسجل على يومية اليوم المسودة
    private static async Task<DailyJournal> AddDraftJournalAsync(AppDbContext context, DateTime? date = null)
    {
        var journal = new DailyJournal
        {
            JournalDate = date ?? DateTime.Today,
            ShiftType = ShiftType.FullDay,
            EmployeeName = "كاشير",
            TotalSales = 2000m,
            ActualCash = 2000m,
        };
        context.DailyJournals.Add(journal);
        await context.SaveChangesAsync();
        return journal;
    }

    // ── تسوية ديون الشركاء ──────────────────────────────────────────

    [Fact]
    public async Task PartialSettlement_KeepsDebtUnpaid_UntilFullyCovered()
    {
        var s = Create();
        await AddDraftJournalAsync(s.Context);
        var debt = await s.OwnerDebt.RecordDebtAsync("شريك", 1000m, null, null, DateTime.Today);

        await s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 400m, OwnerDebtSettlementSource.CashRegister, null, null, DateTime.Today);
        (await s.Context.OwnerDebts.FindAsync(debt.Id))!.Status.Should().Be(OwnerDebtStatus.Unpaid);

        await s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 600m, OwnerDebtSettlementSource.CashRegister, null, null, DateTime.Today);
        (await s.Context.OwnerDebts.FindAsync(debt.Id))!.Status.Should().Be(OwnerDebtStatus.Paid);
    }

    [Fact]
    public async Task Settlement_AboveRemainingDebt_IsRejected()
    {
        var s = Create();
        await AddDraftJournalAsync(s.Context);
        var debt = await s.OwnerDebt.RecordDebtAsync("شريك", 500m, null, null, DateTime.Today);
        await s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 300m, OwnerDebtSettlementSource.CashRegister, null, null, DateTime.Today);

        var act = () => s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 300m, OwnerDebtSettlementSource.CashRegister, null, null, DateTime.Today);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeletingSettlement_RecomputesStatusFromRemainingSettlements()
    {
        var s = Create();
        await AddDraftJournalAsync(s.Context);
        var debt = await s.OwnerDebt.RecordDebtAsync("شريك", 1000m, null, null, DateTime.Today);
        await s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 1000m, OwnerDebtSettlementSource.CashRegister, null, null, DateTime.Today);
        (await s.Context.OwnerDebts.FindAsync(debt.Id))!.Status.Should().Be(OwnerDebtStatus.Paid);

        var settlement = await s.Context.OwnerDebtSettlements.SingleAsync();
        await s.OwnerDebt.DeleteSettlementAsync(settlement.Id);

        (await s.Context.OwnerDebts.FindAsync(debt.Id))!.Status.Should().Be(OwnerDebtStatus.Unpaid);
    }

    [Fact]
    public async Task PettyCashSettlement_RecordsCashOut_AndDeleteReversesIt()
    {
        var s = Create();
        await s.Cash.RecordMovementAsync(CashMovementType.CashIn, 2000m, "Manual", null, "رصيد", DateTime.Today);
        var debt = await s.OwnerDebt.RecordDebtAsync("شريك", 700m, null, null, DateTime.Today);

        var settlement = await s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 700m, OwnerDebtSettlementSource.PettyCash, null, null, DateTime.Today);
        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(1300m);

        await s.OwnerDebt.DeleteSettlementAsync(settlement.Id);
        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(2000m);
    }

    [Fact]
    public async Task CashRegisterSettlement_IsTrackedOnTheDaysJournal_NotAsShortage()
    {
        var s = Create();
        var journal = await AddDraftJournalAsync(s.Context);
        var debt = await s.OwnerDebt.RecordDebtAsync("شريك", 700m, null, null, DateTime.Today);
        // الكاشير صرف 300 من الدرج: النقد الفعلي أقل بـ 300
        journal.ActualCash = 1700m;
        await s.Context.SaveChangesAsync();

        var settlement = await s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 300m, OwnerDebtSettlementSource.CashRegister, null, null, DateTime.Today.AddHours(15));

        settlement.DailyJournalId.Should().Be(journal.Id);
        var after = await s.Context.DailyJournals.FindAsync(journal.Id);
        after!.DrawerPayouts.Should().Be(300m);
        after.Difference.Should().Be(0m, "the payout explains the missing cash instead of showing a shortage");
        after.TotalExpenses.Should().Be(0m, "a partner settlement is not an expense");
        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(0m, "drawer cash reaches the restaurant cash only when the journal is posted");

        await s.OwnerDebt.DeleteSettlementAsync(settlement.Id);
        (await s.Context.DailyJournals.FindAsync(journal.Id))!.DrawerPayouts.Should().Be(0m);
    }

    [Fact]
    public async Task CashRegisterSettlement_WithoutDraftJournalThatDay_IsRejected()
    {
        var s = Create();
        await AddDraftJournalAsync(s.Context, DateTime.Today.AddDays(-3));
        var debt = await s.OwnerDebt.RecordDebtAsync("شريك", 700m, null, null, DateTime.Today);

        var act = () => s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 300m, OwnerDebtSettlementSource.CashRegister, null, null, DateTime.Today);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await s.Context.OwnerDebtSettlements.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CashRegisterSettlement_OnPostedJournal_CannotBeDeleted()
    {
        var s = Create();
        var journal = await AddDraftJournalAsync(s.Context);
        var debt = await s.OwnerDebt.RecordDebtAsync("شريك", 700m, null, null, DateTime.Today);
        var settlement = await s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 300m, OwnerDebtSettlementSource.CashRegister, null, null, DateTime.Today);
        journal.FinancialStatus = FinancialStatus.Posted;
        await s.Context.SaveChangesAsync();

        var act = () => s.OwnerDebt.DeleteSettlementAsync(settlement.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public async Task NonPositiveDebtOrSettlement_IsRejected(decimal amount)
    {
        var s = Create();
        await FluentActions.Awaiting(() => s.OwnerDebt.RecordDebtAsync("شريك", amount, null, null, DateTime.Today))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => s.OwnerDebt.RecordSettlementAsync(null, "شريك", amount, OwnerDebtSettlementSource.CashRegister, null, null, DateTime.Today))
            .Should().ThrowAsync<ArgumentException>();
    }

    // ── دفعات الموردين ──────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task SupplierPayment_NonPositiveAmount_IsRejected(decimal amount)
    {
        var s = Create();
        var supplier = await AddSupplierAsync(s.Context);

        var act = () => s.Ledger.PostPaymentAsync(supplier.Id, amount, TransactionSourceType.ExternalPayment, 0, DateTime.Today);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SupplierPayment_AboveInvoiceRemaining_IsRejected()
    {
        var s = Create();
        var supplier = await AddSupplierAsync(s.Context);
        var invoice = TestDataBuilder.BuildSupplierInvoice(1000m, supplier.Id, DateTime.Today);
        await s.Ledger.PostInvoiceAsync(invoice);
        await s.Ledger.PostPaymentAsync(supplier.Id, 600m, TransactionSourceType.ExternalPayment, 0, DateTime.Today, invoice.Id);

        var act = () => s.Ledger.PostPaymentAsync(supplier.Id, 500m, TransactionSourceType.ExternalPayment, 0, DateTime.Today, invoice.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await s.Context.SupplierInvoices.FindAsync(invoice.Id))!.PaidAmount.Should().Be(600m);
    }

    [Fact]
    public async Task UpdatingPartnerPaidSupplierPayment_DoesNotTouchUnrelatedExpenseWithSameId()
    {
        var s = Create();
        var supplier = await AddSupplierAsync(s.Context, openingBalance: 5000m);

        // مصروف عام لا علاقة له بالدفعة، وسيحمل نفس رقم دين الشريك (1)
        var unrelated = new GeneralExpense
        {
            ExpenseType = GeneralExpenseType.SupplierPayment,
            Amount = 999m,
            PaymentDate = DateTime.Today,
            PaymentMethod = PaymentMethodType.Cash,
            Description = "مصروف آخر",
            FinancialStatus = FinancialStatus.Draft
        };
        s.Context.GeneralExpenses.Add(unrelated);
        await s.Context.SaveChangesAsync();

        await s.Ledger.PostPaymentAsync(supplier.Id, 300m, TransactionSourceType.ExternalPayment, 0, DateTime.Today, partnerName: "شريك");
        var payment = await s.Context.SupplierTransactions.SingleAsync(t => t.Type == SupplierTransactionType.DecreaseDebt);
        var debt = await s.Context.OwnerDebts.SingleAsync();
        payment.SourceId.Should().Be(debt.Id);
        debt.Id.Should().Be(unrelated.Id, "the scenario needs the debt id to collide with the unrelated expense id");

        // تحويل الدفعة إلى نقدية: يجب ألا يُعدَّل أو يُحذف المصروف غير المرتبط
        await s.Ledger.UpdatePaymentAsync(payment.Id, 300m, DateTime.Today, null, null);

        var reloaded = await s.Context.GeneralExpenses.AsNoTracking().SingleAsync(e => e.Id == unrelated.Id);
        reloaded.Amount.Should().Be(999m);
        reloaded.Description.Should().Be("مصروف آخر");
        reloaded.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task UpdatingDailyJournalPayment_FromSupplierScreen_IsRejected()
    {
        var s = Create();
        var supplier = await AddSupplierAsync(s.Context, openingBalance: 1000m);
        await s.Ledger.PostPaymentAsync(supplier.Id, 200m, TransactionSourceType.DailyJournalPayment, 42, DateTime.Today);
        var payment = await s.Context.SupplierTransactions.SingleAsync();

        var act = () => s.Ledger.UpdatePaymentAsync(payment.Id, 250m, DateTime.Today, null, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── كشف المورد ─────────────────────────────────────────────────

    [Fact]
    public async Task SupplierReport_IncludesOpeningBalance_WhenNoPriorTransactions()
    {
        var s = Create();
        var supplier = await AddSupplierAsync(s.Context, openingBalance: 2500m);
        var invoice = TestDataBuilder.BuildSupplierInvoice(1000m, supplier.Id, DateTime.Today);
        await s.Ledger.PostInvoiceAsync(invoice);

        var summary = await s.Reporting.GenerateSummaryAsync(DateTime.Today.AddDays(-7), DateTime.Today.AddDays(1));

        var row = summary.Suppliers.Single(r => r.SupplierId == supplier.Id);
        row.OpeningBalance.Should().Be(2500m);
        row.ClosingBalance.Should().Be(3500m);
    }

    // ── البنوك ─────────────────────────────────────────────────────

    [Fact]
    public async Task InternalTransfer_ToSameAccount_IsRejected()
    {
        var s = Create();
        var account = TestDataBuilder.BuildBankAccount("مصرف", 1000m);
        s.Context.BankAccounts.Add(account);
        await s.Context.SaveChangesAsync();

        var act = () => s.Bank.RecordInternalTransferAsync(account.Id, account.Id, 100m, null, DateTime.Today);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task BankTransaction_NonPositiveAmount_IsRejected()
    {
        var s = Create();
        var account = TestDataBuilder.BuildBankAccount("مصرف", 1000m);
        s.Context.BankAccounts.Add(account);
        await s.Context.SaveChangesAsync();

        var act = () => s.Bank.RecordDepositAsync(account.Id, -10m, null, null, DateTime.Today);

        await act.Should().ThrowAsync<ArgumentException>();
        (await s.Context.BankAccounts.FindAsync(account.Id))!.CurrentBalance.Should().Be(1000m);
    }

    // ── أرصدة العمال ──────────────────────────────────────────────

    [Fact]
    public async Task WorkerSummaries_SumExactlyInDecimal()
    {
        var s = Create();
        var worker = TestDataBuilder.BuildWorker("عامل", 33.33m);
        s.Context.Workers.Add(worker);
        await s.Context.SaveChangesAsync();

        for (int i = 0; i < 30; i++)
        {
            await s.Wages.RecordTransactionAsync(new WorkerTransaction
            {
                WorkerId = worker.Id,
                TransactionDate = DateTime.Today,
                Type = WorkerTransactionType.WageAccrual,
                CreditAmount = 0.1m
            });
        }

        var summary = (await s.Wages.GetWorkerSummariesAsync()).Single(w => w.WorkerId == worker.Id);
        summary.TotalAccrued.Should().Be(3.0m);
    }

    // ── إصلاحات المراجعة الخارجية (Codex / Gemini) ──────────────────

    [Fact]
    public void IncomeStatement_NetProfit_DeductsReturnsAndFreeOrders()
    {
        var report = new IncomeStatementReport
        {
            TotalSales = 1000m, ReturnsTotal = 100m, FreeOrdersTotal = 50m,
            DailyExpenses = 150m, GeneralExpenses = 50m,
        };

        report.NetSales.Should().Be(850m);
        report.NetProfit.Should().Be(650m, "profit uses the same net sales as the daily journal");
    }

    [Fact]
    public async Task AdvanceDeductedFromWage_DoesNotIncreaseWorkerDebt()
    {
        var s = Create();
        var worker = TestDataBuilder.BuildWorker("عامل", 60m);
        s.Context.Workers.Add(worker);
        await s.Context.SaveChangesAsync();
        // سلفة 100
        await s.Wages.RecordTransactionAsync(new WorkerTransaction
        {
            WorkerId = worker.Id, TransactionDate = DateTime.Today.AddDays(-1),
            Type = WorkerTransactionType.Advance, DebitAmount = 100m,
        });

        // حضر واستحق 60، احتُجز منها 20 للسلفة
        await s.Wages.SaveAttendanceBatchAsync(new() { new WorkerAttendance
        {
            WorkerId = worker.Id, WorkerName = worker.WorkerName, WorkDate = DateTime.Today,
            Status = AttendanceStatus.Present, SnapshotDailyWage = 60m, AdvanceDeducted = 20m,
        }});
        // وقبض الباقي 40 نقداً
        await s.Wages.RecordTransactionAsync(new WorkerTransaction
        {
            WorkerId = worker.Id, TransactionDate = DateTime.Today,
            Type = WorkerTransactionType.Payment, DebitAmount = 40m,
        });

        var summary = (await s.Wages.GetWorkerSummariesAsync()).Single(w => w.WorkerId == worker.Id);
        (summary.TotalAccrued - summary.TotalPaid).Should().Be(-80m, "100 advance - 20 kept from the wage = 80 still owed");

        var reloaded = (await s.Wages.GetAttendanceForDateAsync(DateTime.Today)).Single(a => a.WorkerId == worker.Id);
        reloaded.AdvanceDeducted.Should().Be(20m, "the deduction is kept on the attendance record");
    }

    [Fact]
    public async Task SupplierPayment_OnAnotherSuppliersInvoice_IsRejected()
    {
        var s = Create();
        var supplierA = await AddSupplierAsync(s.Context);
        var supplierB = await AddSupplierAsync(s.Context);
        var invoiceOfA = TestDataBuilder.BuildSupplierInvoice(100m, supplierA.Id, DateTime.Today);
        await s.Ledger.PostInvoiceAsync(invoiceOfA);

        var act = () => s.Ledger.PostPaymentAsync(supplierB.Id, 100m, TransactionSourceType.ExternalPayment, 0, DateTime.Today, invoiceOfA.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await s.Context.SupplierInvoices.FindAsync(invoiceOfA.Id))!.PaidAmount.Should().Be(0m);
        (await s.Context.Suppliers.FindAsync(supplierB.Id))!.CurrentBalance.Should().Be(0m);
    }

    [Fact]
    public async Task Settlement_OfADebt_IsAlwaysForTheDebtOwner()
    {
        var s = Create();
        var debt = await s.OwnerDebt.RecordDebtAsync("أ", 100m, null, null, DateTime.Today);

        var settlement = await s.OwnerDebt.RecordSettlementAsync(debt.Id, "ب", 100m, OwnerDebtSettlementSource.PettyCash, null, null, DateTime.Today);

        settlement.PartnerName.Should().Be("أ");
    }

    [Fact]
    public async Task BackdatedBankDeposit_KeepsRunningBalancesInDateOrder()
    {
        var s = Create();
        var bank = TestDataBuilder.BuildBankAccount("مصرف", 0m);
        s.Context.BankAccounts.Add(bank);
        await s.Context.SaveChangesAsync();
        var day2 = new DateTime(2026, 10, 2);

        await s.Bank.RecordDepositAsync(bank.Id, 100m, null, null, day2);
        await s.Bank.RecordDepositAsync(bank.Id, 50m, null, null, day2.AddDays(-1));

        var txs = await s.Context.BankTransactions.Where(t => t.BankAccountId == bank.Id).OrderBy(t => t.TransactionDate).ToListAsync();
        txs.Select(t => t.BalanceAfter).Should().Equal(50m, 150m);
    }

    [Fact]
    public async Task PartnerFunding_ToTreasury_EntersCash_AndDeleteReversesIt()
    {
        var s = Create();
        var debt = await s.OwnerDebt.RecordFundingAsync("شريك", 1000m, null, DateTime.Today, OwnerFundingDestination.Treasury, null, false, null);
        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(1000m);

        await s.OwnerDebt.DeleteFundingAsync(debt.Id);

        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(0m);
        (await s.Context.OwnerDebts.FindAsync(debt.Id))!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task PartnerFunding_ByTransferWithoutReference_CreatesNothing()
    {
        var s = Create();
        var bank = TestDataBuilder.BuildBankAccount("مصرف", 0m);
        s.Context.BankAccounts.Add(bank);
        await s.Context.SaveChangesAsync();

        var act = () => s.OwnerDebt.RecordFundingAsync("شريك", 1000m, null, DateTime.Today, OwnerFundingDestination.Bank, bank.Id, true, " ");

        await act.Should().ThrowAsync<ArgumentException>();
        (await s.Context.OwnerDebts.CountAsync()).Should().Be(0, "the debt used to be saved before the reference was checked");
    }

    [Fact]
    public async Task DeletingSettledBankFunding_IsRefused_BeforeTouchingTheBank()
    {
        var s = Create();
        var bank = TestDataBuilder.BuildBankAccount("مصرف", 0m);
        s.Context.BankAccounts.Add(bank);
        await s.Context.SaveChangesAsync();
        var debt = await s.OwnerDebt.RecordFundingAsync("شريك", 1000m, null, DateTime.Today, OwnerFundingDestination.Bank, bank.Id, false, null);
        await s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 200m, OwnerDebtSettlementSource.Bank, bank.Id, null, DateTime.Today);
        (await s.Context.BankAccounts.FindAsync(bank.Id))!.CurrentBalance.Should().Be(800m);

        var act = () => s.OwnerDebt.DeleteFundingAsync(debt.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await s.Context.BankAccounts.FindAsync(bank.Id))!.CurrentBalance.Should().Be(800m, "the deposit must not be removed when the delete is refused");
    }

    [Fact]
    public async Task PartnerFunding_IntoCashierDrawer_IsTrackedOnTheDaysJournal()
    {
        var s = Create();
        var journal = await AddDraftJournalAsync(s.Context);
        journal.ActualCash = 2100m; // الشريك وضع 100 في الدرج
        await s.Context.SaveChangesAsync();

        var debt = await s.OwnerDebt.RecordFundingAsync("شريك", 100m, null, DateTime.Today, OwnerFundingDestination.CashierDrawer, null, false, null);

        var after = await s.Context.DailyJournals.FindAsync(journal.Id);
        after!.DrawerPayouts.Should().Be(-100m);
        after.Difference.Should().Be(0m, "the partner's cash explains the extra money in the drawer");

        await s.OwnerDebt.DeleteFundingAsync(debt.Id);
        (await s.Context.DailyJournals.FindAsync(journal.Id))!.DrawerPayouts.Should().Be(0m);
    }

    [Fact]
    public async Task BankWithdrawal_ToRestaurantCash_RecordsBothSides()
    {
        var s = Create();
        var bank = TestDataBuilder.BuildBankAccount("مصرف", 1000m);
        s.Context.BankAccounts.Add(bank);
        await s.Context.SaveChangesAsync();

        await s.Bank.RecordWithdrawalAsync(bank.Id, 200m, null, null, DateTime.Today, toRestaurantCash: true);

        (await s.Context.BankAccounts.FindAsync(bank.Id))!.CurrentBalance.Should().Be(800m);
        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(200m);
    }

    private static async Task<PostingSession> AddSettledPeriodAsync(AppDbContext context, DateTime from, DateTime to)
    {
        var session = new PostingSession
        {
            CreatedBy = "admin", PeriodStartDate = from, PeriodEndDate = to,
            SessionType = PostingSessionType.Settlement, Status = PostingSessionStatus.Settled,
        };
        context.PostingSessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }

    [Fact]
    public async Task SettledPeriod_BlocksSupplierBankPartnerAndWageWrites()
    {
        var s = Create();
        var day = DateTime.Today.AddDays(-10);
        var supplier = await AddSupplierAsync(s.Context, openingBalance: 500m);
        var bank = TestDataBuilder.BuildBankAccount("مصرف", 1000m);
        s.Context.BankAccounts.Add(bank);
        var worker = TestDataBuilder.BuildWorker("عامل", 50m);
        s.Context.Workers.Add(worker);
        await s.Context.SaveChangesAsync();
        var debt = await s.OwnerDebt.RecordDebtAsync("شريك", 300m, null, null, day);
        await AddSettledPeriodAsync(s.Context, day.AddDays(-5), day.AddDays(5));

        await FluentActions.Awaiting(() => s.Ledger.PostPaymentAsync(supplier.Id, 50m, TransactionSourceType.ExternalPayment, 0, day))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 30m, OwnerDebtSettlementSource.PettyCash, null, null, day))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => s.Bank.RecordDepositAsync(bank.Id, 10m, null, null, day))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => s.Wages.RecordTransactionAsync(new WorkerTransaction
            { WorkerId = worker.Id, TransactionDate = day, Type = WorkerTransactionType.Advance, DebitAmount = 20m }))
            .Should().ThrowAsync<InvalidOperationException>();

        (await s.Cash.GetCurrentBalanceAsync()).Should().Be(0m);
        (await s.Context.Suppliers.FindAsync(supplier.Id))!.CurrentBalance.Should().Be(500m);
    }

    [Fact]
    public async Task UnlockingAPeriod_WithoutAValidApprover_IsRefusedByTheService()
    {
        var s = Create();
        var period = await AddSettledPeriodAsync(s.Context, DateTime.Today.AddDays(-30), DateTime.Today.AddDays(-1));
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((User?)null);
        var posting = new PostingService(s.Context, s.Cash, new AuditService(s.Context), null, auth.Object);

        var act = () => posting.UnlockPeriodAsync(period.Id, "تصحيح", "تصحيح خطأ في إدخال مصروفات الشهر", "1", "someone", "wrong");

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await s.Context.PostingSessions.FindAsync(period.Id))!.Status.Should().Be(PostingSessionStatus.Settled);
    }

    [Fact]
    public async Task SupplierLedgerRebuild_WorksWithPostedJournalPayments_UnderTheRealTriggers()
    {
        var s = Create();
        PostedRecordTriggers.Recreate(s.Context.Database.GetDbConnection());
        var supplier = await AddSupplierAsync(s.Context, openingBalance: 100m);
        var journal = TestDataBuilder.BuildDailyJournal(DateTime.Today, FinancialStatus.Draft, 100m, 10m);
        var item = new DailyExpenseItem { SequenceNumber = 1, Amount = 10m, Category = "دفعة مورد", Type = ExpenseType.SupplierPayment, SupplierId = supplier.Id };
        journal.ExpenseItems.Add(item);
        s.Context.DailyJournals.Add(journal);
        await s.Context.SaveChangesAsync();
        await s.Ledger.PostPaymentAsync(supplier.Id, 10m, TransactionSourceType.DailyJournalPayment, item.Id, DateTime.Today);
        journal.FinancialStatus = FinancialStatus.Posted;
        await s.Context.SaveChangesAsync();

        // دفعة بتاريخ أقدم تغيّر رصيد الحركة المرحّلة المشتق
        await s.Ledger.PostPaymentAsync(supplier.Id, 20m, TransactionSourceType.ExternalPayment, 0, DateTime.Today.AddDays(-3));
        await s.Ledger.RebuildSupplierLedgerAsync(supplier.Id);

        var posted = await s.Context.SupplierTransactions.SingleAsync(t => t.SourceType == TransactionSourceType.DailyJournalPayment);
        posted.BalanceAfter.Should().Be(70m);

        // الحقول المالية للحركة المرحّلة ما زالت محمية
        var act = () => s.Context.Database.ExecuteSqlRawAsync("UPDATE SupplierTransactions SET Amount = '1' WHERE Id = {0}", posted.Id);
        await act.Should().ThrowAsync<Exception>();
    }
}
