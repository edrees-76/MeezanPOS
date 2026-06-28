using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Tests.Helpers;

namespace MeezanPOS.Tests.ViewModels;

public class GeneralExpensesViewModelTests
{
    private GeneralExpensesViewModel CreateViewModel(
        Mock<IBankService>? mockBank = null,
        Mock<IOwnerDebtService>? mockOwnerDebt = null,
        Mock<IWagesService>? mockWages = null,
        Mock<ICashLedgerService>? mockCashLedger = null,
        Mock<IPostingService>? mockPosting = null,
        Mock<ISessionService>? mockSession = null)
    {
        mockBank ??= new Mock<IBankService>();
        mockOwnerDebt ??= new Mock<IOwnerDebtService>();
        mockWages ??= new Mock<IWagesService>();
        mockCashLedger ??= new Mock<ICashLedgerService>();
        mockPosting ??= new Mock<IPostingService>();
        mockSession ??= new Mock<ISessionService>();

        mockSession.Setup(s => s.CurrentUser).Returns(new User { Id = 1, Username = "admin" });
        mockSession.Setup(s => s.CurrentUserId).Returns("1");
        mockSession.Setup(s => s.CurrentUsername).Returns("admin");

        // Setup empty returns for async methods called in constructor
        mockBank.Setup(b => b.GetAllAccountsAsync())
            .ReturnsAsync(new List<BankAccount>());
        mockOwnerDebt.Setup(o => o.GetPartnerNamesAsync())
            .ReturnsAsync(new List<string>());
        mockWages.Setup(w => w.GetUniqueWorkerNamesAsync())
            .ReturnsAsync(new List<string>());

        var vm = new GeneralExpensesViewModel(
            mockBank.Object,
            mockOwnerDebt.Object,
            mockWages.Object,
            mockCashLedger.Object,
            mockPosting.Object,
            mockSession.Object);

        return vm;
    }

    // GROUP 1: Loading
    [Fact]
    public void LoadExpenses_ShouldPopulate_ExpensesList()
    {
        // Arrange
        var vm = CreateViewModel();
        int seededId = 0;
        using (var db = new AppDbContext())
        {
            db.Database.EnsureCreated();
            var expense = new GeneralExpense
            {
                ExpenseType = GeneralExpenseType.Rent,
                Amount = 1200m,
                PaymentDate = DateTime.Today,
                PaymentMethod = PaymentMethodType.Cash,
                Description = "Seeded Rent Test",
                FinancialStatus = FinancialStatus.Draft
            };
            db.GeneralExpenses.Add(expense);
            db.SaveChanges();
            seededId = expense.Id;
        }

        try
        {
            // Act
            vm.LoadExpensesCommand.Execute(null);

            // Assert
            vm.Expenses.Should().NotBeEmpty();
            var item = vm.Expenses.FirstOrDefault(e => e.Id == seededId);
            item.Should().NotBeNull();
            item!.Amount.Should().Be(1200m);
            item.Description.Should().Be("Seeded Rent Test");
        }
        finally
        {
            // Clean up
            using var db = new AppDbContext();
            var expense = db.GeneralExpenses.Find(seededId);
            if (expense != null)
            {
                db.GeneralExpenses.Remove(expense);
                db.SaveChanges();
            }
        }
    }

    [Fact]
    public void LoadExpenses_ShouldFilter_BySelectedMonth()
    {
        // Arrange
        var vm = CreateViewModel();
        int currentMonthId = 0;
        int nextMonthId = 0;

        var today = DateTime.Today;
        var firstOfCurrent = new DateTime(today.Year, today.Month, 1);
        var firstOfNext = firstOfCurrent.AddMonths(1);

        using (var db = new AppDbContext())
        {
            db.Database.EnsureCreated();
            
            var exp1 = new GeneralExpense
            {
                ExpenseType = GeneralExpenseType.Rent,
                Amount = 500m,
                PaymentDate = firstOfCurrent,
                PaymentMethod = PaymentMethodType.Cash,
                Description = "Current Month Expense",
                FinancialStatus = FinancialStatus.Draft
            };
            var exp2 = new GeneralExpense
            {
                ExpenseType = GeneralExpenseType.Electricity,
                Amount = 300m,
                PaymentDate = firstOfNext,
                PaymentMethod = PaymentMethodType.Cash,
                Description = "Next Month Expense",
                FinancialStatus = FinancialStatus.Draft
            };

            db.GeneralExpenses.AddRange(exp1, exp2);
            db.SaveChanges();

            currentMonthId = exp1.Id;
            nextMonthId = exp2.Id;
        }

        try
        {
            // Act
            vm.DateFrom = firstOfCurrent;
            vm.DateTo = firstOfCurrent.AddMonths(1).AddDays(-1);
            vm.LoadExpensesCommand.Execute(null);

            // Assert
            var currentItem = vm.Expenses.FirstOrDefault(e => e.Id == currentMonthId);
            var nextItem = vm.Expenses.FirstOrDefault(e => e.Id == nextMonthId);

            currentItem.Should().NotBeNull();
            nextItem.Should().BeNull();
        }
        finally
        {
            using var db = new AppDbContext();
            var exp1 = db.GeneralExpenses.Find(currentMonthId);
            var exp2 = db.GeneralExpenses.Find(nextMonthId);
            if (exp1 != null) db.GeneralExpenses.Remove(exp1);
            if (exp2 != null) db.GeneralExpenses.Remove(exp2);
            db.SaveChanges();
        }
    }

    // GROUP 2: Adding Expenses
    [Fact]
    public async Task AddExpense_ShouldCallService_WithCorrectData()
    {
        // Arrange
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();
        MessageBoxMock.ResultToReturn = System.Windows.MessageBoxResult.Yes;

        var mockCashLedger = new Mock<ICashLedgerService>();
        mockCashLedger.Setup(c => c.RecordMovementAsync(It.IsAny<CashMovementType>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new CashMovement());

        var vm = CreateViewModel(mockCashLedger: mockCashLedger);
        vm.SelectedExpenseType = GeneralExpenseType.Rent;
        vm.InputAmount = 150m;
        vm.InputPaymentDate = DateTime.Today;
        vm.SelectedPaymentMethod = PaymentMethodType.Cash;
        vm.InputDescription = "AddExpense Test Description";

        int seededId = 0;
        try
        {
            // Act
            await vm.SaveExpenseCommand.ExecuteAsync(null!);

            // Assert
            mockCashLedger.Verify(c => c.RecordMovementAsync(
                CashMovementType.CashOut,
                150m,
                "GeneralExpense",
                It.IsAny<int?>(),
                It.Is<string>(s => s.Contains("Rent") || s.Contains("إيجار")),
                It.IsAny<DateTime>()
            ), Times.Once);

            using var db = new AppDbContext();
            var created = db.GeneralExpenses.FirstOrDefault(e => e.Description == "AddExpense Test Description");
            created.Should().NotBeNull();
            seededId = created!.Id;
        }
        finally
        {
            if (seededId > 0)
            {
                using var db = new AppDbContext();
                var created = db.GeneralExpenses.Find(seededId);
                if (created != null)
                {
                    db.GeneralExpenses.Remove(created);
                    db.SaveChanges();
                }
            }
        }
    }

    [Fact]
    public async Task AddExpense_ShouldFail_WhenAmountIsZero()
    {
        // Arrange
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();

        var mockCashLedger = new Mock<ICashLedgerService>();
        var vm = CreateViewModel(mockCashLedger: mockCashLedger);
        vm.InputAmount = 0m;

        // Act
        await vm.SaveExpenseCommand.ExecuteAsync(null!);

        // Assert
        MessageBoxMock.CallCount.Should().Be(1);
        MessageBoxMock.LastMessage.Should().Contain("صفر");
        mockCashLedger.Verify(c => c.RecordMovementAsync(It.IsAny<CashMovementType>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task AddExpense_ShouldFail_WhenDescriptionIsEmpty()
    {
        // Arrange
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();

        var mockCashLedger = new Mock<ICashLedgerService>();
        var vm = CreateViewModel(mockCashLedger: mockCashLedger);
        vm.InputAmount = 100m;
        vm.SelectedExpenseType = GeneralExpenseType.Other;
        vm.InputCustomExpenseType = ""; // Empty custom type description

        // Act
        await vm.SaveExpenseCommand.ExecuteAsync(null!);

        // Assert
        MessageBoxMock.CallCount.Should().Be(1);
        MessageBoxMock.LastMessage.Should().Contain("نوع المصروف");
        mockCashLedger.Verify(c => c.RecordMovementAsync(It.IsAny<CashMovementType>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    // GROUP 3: Posting
    [Fact]
    public async Task PostExpense_ShouldCallPostingService()
    {
        // Arrange
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();
        MessageBoxMock.ResultToReturn = System.Windows.MessageBoxResult.Yes;

        var mockPosting = new Mock<IPostingService>();
        mockPosting.Setup(p => p.PostEntityAsync<GeneralExpense>(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var vm = CreateViewModel(mockPosting: mockPosting);
        var item = new GeneralExpenseDisplayItem { Id = 77, Amount = 200m, FinancialStatus = FinancialStatus.Draft };

        // Act
        await vm.PostExpenseCommand.ExecuteAsync(item);

        // Assert
        mockPosting.Verify(p => p.PostEntityAsync<GeneralExpense>(77, "1"), Times.Once);
        MessageBoxMock.CallCount.Should().Be(2); // Confirm + Success
    }

    [Fact]
    public async Task PostExpense_ShouldFail_WhenExpenseAlreadyPosted()
    {
        // Arrange
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();

        var mockPosting = new Mock<IPostingService>();
        var vm = CreateViewModel(mockPosting: mockPosting);
        var item = new GeneralExpenseDisplayItem { Id = 77, Amount = 200m, FinancialStatus = FinancialStatus.Posted };

        // Act
        await vm.PostExpenseCommand.ExecuteAsync(item);

        // Assert
        mockPosting.Verify(p => p.PostEntityAsync<GeneralExpense>(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        MessageBoxMock.CallCount.Should().Be(0);
    }

    // GROUP 4: Deletion
    [Fact]
    public async Task DeleteExpense_ShouldRemove_FromExpensesList()
    {
        // Arrange
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();
        MessageBoxMock.ResultToReturn = System.Windows.MessageBoxResult.Yes;

        var vm = CreateViewModel();
        
        int seededId = 0;
        using (var db = new AppDbContext())
        {
            db.Database.EnsureCreated();
            var expense = new GeneralExpense
            {
                ExpenseType = GeneralExpenseType.Rent,
                Amount = 500m,
                PaymentDate = DateTime.Today,
                PaymentMethod = PaymentMethodType.Cash,
                Description = "Expense To Delete",
                FinancialStatus = FinancialStatus.Draft
            };
            db.GeneralExpenses.Add(expense);
            db.SaveChanges();
            seededId = expense.Id;
        }

        var item = new GeneralExpenseDisplayItem
        {
            Id = seededId,
            ExpenseType = GeneralExpenseType.Rent,
            Amount = 500m,
            Description = "Expense To Delete",
            FinancialStatus = FinancialStatus.Draft
        };
        vm.Expenses.Add(item);

        try
        {
            // Act
            await vm.DeleteExpenseCommand.ExecuteAsync(item);

            // Assert
            using var db = new AppDbContext();
            var deleted = db.GeneralExpenses.Find(seededId);
            deleted.Should().BeNull();
            MessageBoxMock.CallCount.Should().Be(1);
        }
        finally
        {
            using var db = new AppDbContext();
            var existing = db.GeneralExpenses.Find(seededId);
            if (existing != null)
            {
                db.GeneralExpenses.Remove(existing);
                db.SaveChanges();
            }
        }
    }

    [Fact]
    public async Task DeleteExpense_ShouldFail_WhenExpenseIsPosted()
    {
        // Arrange
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();
        MessageBoxMock.ResultToReturn = System.Windows.MessageBoxResult.Yes;

        var vm = CreateViewModel();

        int seededId = 0;
        using (var db = new AppDbContext())
        {
            db.Database.EnsureCreated();
            var expense = new GeneralExpense
            {
                ExpenseType = GeneralExpenseType.Rent,
                Amount = 500m,
                PaymentDate = DateTime.Today,
                PaymentMethod = PaymentMethodType.Cash,
                Description = "Posted Expense",
                FinancialStatus = FinancialStatus.Posted
            };
            db.GeneralExpenses.Add(expense);
            db.SaveChanges();
            seededId = expense.Id;
        }

        var item = new GeneralExpenseDisplayItem
        {
            Id = seededId,
            ExpenseType = GeneralExpenseType.Rent,
            Amount = 500m,
            Description = "Posted Expense",
            FinancialStatus = FinancialStatus.Posted
        };

        try
        {
            // Act
            await vm.DeleteExpenseCommand.ExecuteAsync(item);

            // Assert
            using var db = new AppDbContext();
            var existing = db.GeneralExpenses.Find(seededId);
            existing.Should().NotBeNull("Posted expense should not be deleted");
            
            MessageBoxMock.LastMessage.Should().Contain("مرحّل");
        }
        finally
        {
            using var db = new AppDbContext();
            var existing = db.GeneralExpenses.Find(seededId);
            if (existing != null)
            {
                existing.FinancialStatus = FinancialStatus.Draft;
                db.SaveChanges();
                
                db.GeneralExpenses.Remove(existing);
                db.SaveChanges();
            }
        }
    }

    // Existing Initialization & Static Tests
    [Fact]
    public void GeneralExpensesVM_ShouldInitialize_WithExpenseTypes()
    {
        var vm = CreateViewModel();
        vm.ExpenseTypes.Should().NotBeEmpty();
        vm.ExpenseTypes.Should().HaveCountGreaterThanOrEqualTo(10);
        vm.ExpenseTypes.First().Name.Should().Be("إيجار");
    }

    [Fact]
    public void GeneralExpensesVM_ShouldInitialize_WithPaymentMethods()
    {
        var vm = CreateViewModel();
        vm.PaymentMethods.Should().NotBeEmpty();
        vm.PaymentMethods.Should().HaveCountGreaterThanOrEqualTo(3);
        vm.PaymentMethods.First().Name.Should().Be("نقدي");
    }

    [Fact]
    public void GeneralExpensesVM_ShouldInitialize_WithFilterTypes()
    {
        var vm = CreateViewModel();
        vm.FilterExpenseTypes.Should().NotBeEmpty();
        vm.FilterExpenseTypes.First().Name.Should().Be("الكل");
        vm.FilterPaymentMethods.Should().NotBeEmpty();
        vm.FilterPaymentMethods.First().Name.Should().Be("الكل");
    }

    [Fact]
    public void GeneralExpensesVM_ShouldInitialize_WithDefaultDateRange()
    {
        var vm = CreateViewModel();
        vm.DateFrom.Should().Be(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        vm.DateTo.Date.Should().Be(DateTime.Today.Date);
    }

    [Fact]
    public void GeneralExpensesVM_ShouldHave_RequiredCommands()
    {
        var vm = CreateViewModel();
        vm.LoadExpensesCommand.Should().NotBeNull();
        vm.SetShowPostedArchiveCommand.Should().NotBeNull();
    }

    [Fact]
    public void GeneralExpensesVM_Constructor_ShouldThrow_WhenBankServiceNull()
    {
        Action act = () => new GeneralExpensesViewModel(
            null!,
            Mock.Of<IOwnerDebtService>(),
            Mock.Of<IWagesService>(),
            Mock.Of<ICashLedgerService>(),
            Mock.Of<IPostingService>(),
            Mock.Of<ISessionService>());

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("bankService");
    }

    [Fact]
    public void GeneralExpensesVM_Constructor_ShouldThrow_WhenSessionServiceNull()
    {
        Action act = () => new GeneralExpensesViewModel(
            Mock.Of<IBankService>(),
            Mock.Of<IOwnerDebtService>(),
            Mock.Of<IWagesService>(),
            Mock.Of<ICashLedgerService>(),
            Mock.Of<IPostingService>(),
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("sessionService");
    }

    [Fact]
    public void GetExpenseTypeName_ShouldReturnCorrectArabicNames()
    {
        GeneralExpensesViewModel.GetExpenseTypeName(GeneralExpenseType.Rent).Should().Be("إيجار");
        GeneralExpensesViewModel.GetExpenseTypeName(GeneralExpenseType.Electricity).Should().Be("كهرباء");
        GeneralExpensesViewModel.GetExpenseTypeName(GeneralExpenseType.Water).Should().Be("ماء");
        GeneralExpensesViewModel.GetExpenseTypeName(GeneralExpenseType.Salaries).Should().Be("رواتب");
        GeneralExpensesViewModel.GetExpenseTypeName(GeneralExpenseType.Internet).Should().Be("إنترنت");
        GeneralExpensesViewModel.GetExpenseTypeName(GeneralExpenseType.Maintenance).Should().Be("صيانة");
        GeneralExpensesViewModel.GetExpenseTypeName(GeneralExpenseType.Insurance).Should().Be("تأمين");
        GeneralExpensesViewModel.GetExpenseTypeName(GeneralExpenseType.Taxes).Should().Be("ضرائب/رسوم");
        GeneralExpensesViewModel.GetExpenseTypeName(GeneralExpenseType.SupplierPayment).Should().Be("تسديد قيمة");
        GeneralExpensesViewModel.GetExpenseTypeName(GeneralExpenseType.Other).Should().Be("أخرى");
    }

    [Fact]
    public void GetPaymentMethodName_ShouldReturnCorrectArabicNames()
    {
        GeneralExpensesViewModel.GetPaymentMethodName(PaymentMethodType.Cash).Should().Be("نقدي");
        GeneralExpensesViewModel.GetPaymentMethodName(PaymentMethodType.BankTransfer).Should().Be("تحويل");
        GeneralExpensesViewModel.GetPaymentMethodName(PaymentMethodType.Cheque).Should().Be("شيك");
        GeneralExpensesViewModel.GetPaymentMethodName(PaymentMethodType.PersonalPartner).Should().Be("شخصي (شريك)");
    }
}
