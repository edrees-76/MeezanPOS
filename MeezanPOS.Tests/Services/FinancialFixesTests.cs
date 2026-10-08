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
        var bank = new BankService(context, session.Object, audit);
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

    // ── تسوية ديون الشركاء ──────────────────────────────────────────

    [Fact]
    public async Task PartialSettlement_KeepsDebtUnpaid_UntilFullyCovered()
    {
        var s = Create();
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
        var debt = await s.OwnerDebt.RecordDebtAsync("شريك", 500m, null, null, DateTime.Today);
        await s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 300m, OwnerDebtSettlementSource.CashRegister, null, null, DateTime.Today);

        var act = () => s.OwnerDebt.RecordSettlementAsync(debt.Id, "شريك", 300m, OwnerDebtSettlementSource.CashRegister, null, null, DateTime.Today);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeletingSettlement_RecomputesStatusFromRemainingSettlements()
    {
        var s = Create();
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
}
