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

public class SupplierServiceTests
{
    private (AppDbContext Context, LedgerService Service, Mock<ISessionService> MockSession) CreateServices()
    {
        var context = TestDbContextFactory.Create();
        var mockSession = new Mock<ISessionService>();
        mockSession.Setup(s => s.CurrentUser).Returns(new User { Id = 1, Username = "admin" });
        mockSession.Setup(s => s.CurrentUsername).Returns("admin");

        var auditService = new AuditService(context);
        var bankService = new BankService(context, mockSession.Object, auditService);
        var ownerDebtService = new OwnerDebtService(context, mockSession.Object, bankService, auditService);
        var cashLedgerService = new CashLedgerService(context, mockSession.Object);

        var ledgerService = new LedgerService(
            context,
            mockSession.Object,
            bankService,
            ownerDebtService,
            cashLedgerService,
            auditService
        );

        return (context, ledgerService, mockSession);
    }

    // GROUP 1: Invoice Management
    [Fact]
    public async Task AddInvoice_ShouldIncreaseSupplierDebt()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var supplier = TestDataBuilder.BuildSupplier("Supplier X");
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var invoice = TestDataBuilder.BuildSupplierInvoice(1000m, supplier.Id, DateTime.UtcNow);

        // Act
        await service.PostInvoiceAsync(invoice);

        // Assert
        var tx = await context.SupplierTransactions
            .FirstOrDefaultAsync(t => t.SupplierId == supplier.Id && t.Type == SupplierTransactionType.IncreaseDebt);
        
        tx.Should().NotBeNull();
        tx!.Amount.Should().Be(1000m);
        tx.BalanceAfter.Should().Be(1000m);
    }

    [Fact]
    public async Task AddInvoice_ShouldBeReflected_InSupplierBalance()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var supplier = TestDataBuilder.BuildSupplier("Supplier X");
        supplier.CurrentBalance = 500m;
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var invoice = TestDataBuilder.BuildSupplierInvoice(1200m, supplier.Id, DateTime.UtcNow);

        // Act
        await service.PostInvoiceAsync(invoice);

        // Assert
        var dbSupplier = await context.Suppliers.FindAsync(supplier.Id);
        dbSupplier.Should().NotBeNull();
        dbSupplier!.CurrentBalance.Should().Be(1700m);
    }

    // GROUP 2: Payment Management
    [Fact]
    public async Task AddPayment_ShouldDecreaseSupplierDebt()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var supplier = TestDataBuilder.BuildSupplier("Supplier X");
        supplier.CurrentBalance = 1500m;
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        // Act
        await service.PostPaymentAsync(
            supplier.Id,
            500m,
            TransactionSourceType.ExternalPayment,
            1,
            DateTime.UtcNow,
            null,
            "REC-001",
            "Test Payment"
        );

        // Assert
        var dbSupplier = await context.Suppliers.FindAsync(supplier.Id);
        dbSupplier.Should().NotBeNull();
        dbSupplier!.CurrentBalance.Should().Be(1000m);

        var tx = await context.SupplierTransactions
            .FirstOrDefaultAsync(t => t.SupplierId == supplier.Id && t.Type == SupplierTransactionType.DecreaseDebt);
        tx.Should().NotBeNull();
        tx!.Amount.Should().Be(500m);
        tx.BalanceAfter.Should().Be(1000m);
    }

    [Fact]
    public async Task AddPayment_ShouldAllowOverpayment_AsNegativeDebt()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var supplier = TestDataBuilder.BuildSupplier("Supplier X");
        supplier.CurrentBalance = 100m;
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        // Act
        await service.PostPaymentAsync(
            supplier.Id,
            300m,
            TransactionSourceType.ExternalPayment,
            1,
            DateTime.UtcNow,
            null,
            "REC-002",
            "Overpayment"
        );

        // Assert
        var dbSupplier = await context.Suppliers.FindAsync(supplier.Id);
        dbSupplier.Should().NotBeNull();
        dbSupplier!.CurrentBalance.Should().Be(-200m);
    }

    [Fact]
    public async Task FullPayment_ShouldResultInZeroBalance()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var supplier = TestDataBuilder.BuildSupplier("Supplier X");
        supplier.CurrentBalance = 500m;
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        // Act
        await service.PostPaymentAsync(
            supplier.Id,
            500m,
            TransactionSourceType.ExternalPayment,
            1,
            DateTime.UtcNow,
            null,
            "REC-003",
            "Full Payment"
        );

        // Assert
        var dbSupplier = await context.Suppliers.FindAsync(supplier.Id);
        dbSupplier.Should().NotBeNull();
        dbSupplier!.CurrentBalance.Should().Be(0m);
    }

    // GROUP 3: Supplier Statement
    [Fact]
    public async Task GetSupplierStatement_ShouldReturnAllTransactions_InChronologicalOrder()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var supplier = TestDataBuilder.BuildSupplier("Supplier X");
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var date = DateTime.Today;
        var tx1 = TestDataBuilder.BuildSupplierTransaction(1000m, SupplierTransactionType.IncreaseDebt, supplier.Id, date.AddDays(-2), 1000m);
        var tx2 = TestDataBuilder.BuildSupplierTransaction(400m, SupplierTransactionType.DecreaseDebt, supplier.Id, date.AddDays(-1), 600m);
        context.SupplierTransactions.AddRange(tx1, tx2);
        await context.SaveChangesAsync();

        // Act
        var statement = await service.GetStatementAsync(supplier.Id, date.AddDays(-5), date);

        // Assert
        statement.Should().HaveCount(2);
        statement[0].Id.Should().Be(tx1.Id);
        statement[1].Id.Should().Be(tx2.Id);
    }

    [Fact]
    public async Task GetSupplierStatement_ShouldReturnEmpty_ForNewSupplier()
    {
        // Arrange
        var (context, service, _) = CreateServices();
        var supplier = TestDataBuilder.BuildSupplier("Supplier New");
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        // Act
        var statement = await service.GetStatementAsync(supplier.Id, DateTime.Today.AddDays(-5), DateTime.Today);

        // Assert
        statement.Should().BeEmpty();
    }

    [Fact]
    public async Task Cashier_CanPayASupplierFromTheJournal_ButCannotManageSuppliers()
    {
        var (context, service, session) = CreateServices();
        // كاشير: كل صلاحية لا يملكها دوره تُرفض كما في SessionService
        session.Setup(s => s.RequirePermission(It.Is<string>(p => !Permissions.RoleAllows(RoleType.Cashier, p))))
            .Throws<PermissionDeniedException>();
        var supplier = TestDataBuilder.BuildSupplier("Supplier X");
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        await service.PostPaymentAsync(supplier.Id, 100m, TransactionSourceType.DailyJournalPayment, 1, DateTime.Today);

        await FluentActions.Awaiting(() => service.PostPaymentAsync(supplier.Id, 100m, TransactionSourceType.ExternalPayment, 2, DateTime.Today))
            .Should().ThrowAsync<PermissionDeniedException>();
        await FluentActions.Awaiting(() => service.PostInvoiceAsync(TestDataBuilder.BuildSupplierInvoice(500m, supplier.Id, DateTime.Today)))
            .Should().ThrowAsync<PermissionDeniedException>();
        var payment = await context.SupplierTransactions.FirstAsync(t => t.SupplierId == supplier.Id);
        await FluentActions.Awaiting(() => service.UpdatePaymentAsync(payment.Id, 50m, DateTime.Today, null, null))
            .Should().ThrowAsync<PermissionDeniedException>();

        (await context.SupplierTransactions.CountAsync(t => t.SupplierId == supplier.Id)).Should().Be(1, "only the journal payment was recorded");
    }
}
