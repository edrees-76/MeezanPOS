using System;
using System.Collections.Generic;
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

namespace MeezanPOS.Tests.Integration;

public class FinancialCycleTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly Mock<ISessionService> _mockSession;
    private readonly AuditService _auditService;
    private readonly CashLedgerService _cashLedgerService;
    private readonly PostingService _postingService;
    private readonly BankService _bankService;
    private readonly OwnerDebtService _ownerDebtService;
    private readonly WagesService _wagesService;
    private readonly LedgerService _ledgerService;
    private readonly FinancialReportingService _reportingService;

    public FinancialCycleTests()
    {
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();

        // Redirect db path to Meezan_Test.db, then delete and recreate
        var dbPath = AppDbContext.GetDatabasePath();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (System.IO.File.Exists(dbPath))
        {
            try
            {
                System.IO.File.Delete(dbPath);
            }
            catch { }
        }

        _context = new AppDbContext();
        _context.Database.EnsureCreated();

        _mockSession = new Mock<ISessionService>();
        _mockSession.Setup(s => s.CurrentUser).Returns(new User { Id = 1, Username = "admin" });
        _mockSession.Setup(s => s.CurrentUserId).Returns("1");
        _mockSession.Setup(s => s.CurrentUsername).Returns("admin");

        _auditService = new AuditService(_context);
        _cashLedgerService = new CashLedgerService(_context, _mockSession.Object);
        _postingService = new PostingService(_context, _cashLedgerService, _auditService);
        _bankService = new BankService(_context, _mockSession.Object, _auditService);
        _ownerDebtService = new OwnerDebtService(_context, _mockSession.Object, _bankService, _auditService);
        _wagesService = new WagesService(_context, _mockSession.Object);
        _ledgerService = new LedgerService(_context, _mockSession.Object, _bankService, _ownerDebtService, _cashLedgerService, _auditService);

        var mockFactory = new Mock<IDbContextFactory<AppDbContext>>();
        mockFactory.Setup(f => f.CreateDbContext()).Returns(() => new AppDbContext());
        mockFactory.Setup(f => f.CreateDbContextAsync(default)).ReturnsAsync(() => new AppDbContext());

        _reportingService = new FinancialReportingService(mockFactory.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var dbPath = AppDbContext.GetDatabasePath();
        if (System.IO.File.Exists(dbPath))
        {
            try
            {
                System.IO.File.Delete(dbPath);
            }
            catch { }
        }
    }

    [Fact]
    public async Task FullCycle_CreateJournal_Post_Report_ShouldMatchTotals()
    {
        // Arrange
        var journal = TestDataBuilder.BuildDailyJournal(DateTime.Today, FinancialStatus.Draft, 1200m, 300m);
        journal.ActualCash = 900m;
        journal.CashFloat = 200m;
        journal.ShiftType = ShiftType.FullDay;
        journal.EmployeeName = "Admin";
        _context.DailyJournals.Add(journal);
        await _context.SaveChangesAsync();

        // Act 1: Post
        var postResult = await _postingService.PostEntityAsync<DailyJournal>(journal.Id, "1");
        postResult.Should().BeTrue();

        // Act 2: Report
        var report = await _reportingService.GenerateSummaryAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));

        // Assert
        report.IncomeStatement.TotalSales.Should().Be(1200m);
        report.IncomeStatement.DailyExpenses.Should().Be(300m);
    }

    [Fact]
    public async Task FullCycle_MultipleJournals_ReportShouldAggregateCorrectly()
    {
        // Arrange
        var j1 = TestDataBuilder.BuildDailyJournal(DateTime.Today.AddDays(-5), FinancialStatus.Draft, 1000m, 100m);
        j1.ActualCash = 900m;
        j1.CashFloat = 200m;
        j1.ShiftType = ShiftType.FirstShift;
        j1.EmployeeName = "Admin";

        var j2 = TestDataBuilder.BuildDailyJournal(DateTime.Today.AddDays(-3), FinancialStatus.Draft, 2000m, 200m);
        j2.ActualCash = 1800m;
        j2.CashFloat = 200m;
        j2.ShiftType = ShiftType.SecondShift;
        j2.EmployeeName = "Admin";

        var j3 = TestDataBuilder.BuildDailyJournal(DateTime.Today.AddDays(-1), FinancialStatus.Draft, 1500m, 150m);
        j3.ActualCash = 1350m;
        j3.CashFloat = 200m;
        j3.ShiftType = ShiftType.FullDay;
        j3.EmployeeName = "Admin";

        _context.DailyJournals.AddRange(j1, j2, j3);
        await _context.SaveChangesAsync();

        // Act: post all 3
        await _postingService.PostEntityAsync<DailyJournal>(j1.Id, "1");
        await _postingService.PostEntityAsync<DailyJournal>(j2.Id, "1");
        await _postingService.PostEntityAsync<DailyJournal>(j3.Id, "1");

        var report = await _reportingService.GenerateSummaryAsync(DateTime.Today.AddDays(-10), DateTime.Today);

        // Assert
        report.IncomeStatement.TotalSales.Should().Be(4500m); // 1000 + 2000 + 1500
        report.IncomeStatement.DailyExpenses.Should().Be(450m); // 100 + 200 + 150
    }

    [Fact]
    public async Task FullCycle_PostThenReverse_ReportShouldShowZeroNet()
    {
        // Arrange
        var journal = TestDataBuilder.BuildDailyJournal(DateTime.Today, FinancialStatus.Draft, 1000m, 100m);
        journal.ActualCash = 900m;
        journal.CashFloat = 200m;
        journal.ShiftType = ShiftType.FullDay;
        journal.EmployeeName = "Admin";
        _context.DailyJournals.Add(journal);
        await _context.SaveChangesAsync();

        // Act: Post the journal
        await _postingService.PostEntityAsync<DailyJournal>(journal.Id, "1");

        // Act: Reverse the posting (Unpost)
        var unpostResult = await _postingService.UnpostEntityAsync<DailyJournal>(
            journal.Id,
            "Incorrect calculations and adjustments needed.", // must be at least 20 chars
            "1"
        );
        unpostResult.Should().BeTrue();

        // Act: Generate report
        var report = await _reportingService.GenerateSummaryAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));

        // Assert
        report.IncomeStatement.TotalSales.Should().Be(0m);
        report.IncomeStatement.DailyExpenses.Should().Be(0m);
    }

    [Fact]
    public async Task FullCycle_BankTransfer_ShouldUpdateBothAccounts()
    {
        // Arrange
        var src = TestDataBuilder.BuildBankAccount("Source Bank", 5000m);
        var dest = TestDataBuilder.BuildBankAccount("Dest Bank", 1000m);
        _context.BankAccounts.AddRange(src, dest);
        await _context.SaveChangesAsync();

        // Act: Perform transfer of 1500
        await _bankService.RecordInternalTransferAsync(src.Id, dest.Id, 1500m, "Internal transfer", DateTime.Today);

        // Assert
        var updatedSrc = await _context.BankAccounts.FindAsync(src.Id);
        var updatedDest = await _context.BankAccounts.FindAsync(dest.Id);

        updatedSrc!.CurrentBalance.Should().Be(3500m);
        updatedDest!.CurrentBalance.Should().Be(2500m);
    }

    [Fact]
    public async Task FullCycle_SupplierInvoiceAndPayment_ShouldNetToZero()
    {
        // Arrange
        var supplier = TestDataBuilder.BuildSupplier("Supplier Alpha");
        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync();

        // Act 1: Add invoice of 1000
        var invoice = TestDataBuilder.BuildSupplierInvoice(1000m, supplier.Id, DateTime.Today);
        await _ledgerService.PostInvoiceAsync(invoice);

        // Act 2: Add payment of 1000
        await _ledgerService.PostPaymentAsync(
            supplier.Id,
            1000m,
            TransactionSourceType.ExternalPayment,
            1,
            DateTime.Today,
            null,
            "REC-001",
            "Settle Invoice"
        );

        // Assert
        var updatedSupplier = await _context.Suppliers.FindAsync(supplier.Id);
        updatedSupplier!.CurrentBalance.Should().Be(0m);

        var report = await _reportingService.GenerateSummaryAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        var supplierReport = report.Suppliers.FirstOrDefault(s => s.SupplierId == supplier.Id);
        supplierReport.Should().NotBeNull();
        supplierReport!.ClosingBalance.Should().Be(0m);
    }

    [Fact]
    public async Task FullCycle_WorkerAdvanceAndWage_ShouldCalculateNetCorrectly()
    {
        // Arrange
        var worker = TestDataBuilder.BuildWorker("Worker John", 100m);
        _context.Workers.Add(worker);
        await _context.SaveChangesAsync();

        // Act 1: Add advance 500
        var advanceTx = new WorkerTransaction
        {
            WorkerId = worker.Id,
            Type = WorkerTransactionType.Advance,
            DebitAmount = 500m,
            CreditAmount = 0m,
            TransactionDate = DateTime.Today
        };
        await _wagesService.RecordTransactionAsync(advanceTx);

        // Act 2: Add wage 1500
        var wageTx = new WorkerTransaction
        {
            WorkerId = worker.Id,
            Type = WorkerTransactionType.WageAccrual,
            DebitAmount = 0m,
            CreditAmount = 1500m,
            TransactionDate = DateTime.Today
        };
        await _wagesService.RecordTransactionAsync(wageTx);

        // Assert
        var report = await _reportingService.GenerateSummaryAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        var workerUnpaid = report.Liabilities.WorkerUnpaidWageDetails.FirstOrDefault(w => w.WorkerName == "Worker John");
        workerUnpaid.Should().NotBeNull();
        workerUnpaid!.UnpaidAmount.Should().Be(1000m); // 1500 - 500
    }
}
