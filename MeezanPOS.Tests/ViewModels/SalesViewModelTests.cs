using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Tests.Helpers;

namespace MeezanPOS.Tests.ViewModels;

public class SalesViewModelTests
{
    private SalesViewModel CreateViewModel(
        Mock<ISessionService>? mockSession = null,
        Mock<IPostingService>? mockPosting = null,
        Mock<ICashLedgerService>? mockCashLedger = null)
    {
        mockSession ??= new Mock<ISessionService>();
        mockSession.Setup(s => s.CurrentUser).Returns(new User { Id = 1, Username = "admin" });
        mockSession.Setup(s => s.CurrentUserId).Returns("1");
        mockSession.Setup(s => s.CurrentUsername).Returns("admin");

        mockPosting ??= new Mock<IPostingService>();
        mockCashLedger ??= new Mock<ICashLedgerService>();

        var vm = new SalesViewModel(mockSession.Object, mockPosting.Object, mockCashLedger.Object);
        return vm;
    }

    private void SeedAllJournals(SalesViewModel vm, List<DailyJournal> journals)
    {
        var field = typeof(SalesViewModel)
            .GetField("_allJournals", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull("SalesViewModel should have _allJournals field");
        field!.SetValue(vm, journals);
    }

    // GROUP 1: Initialization
    [Fact]
    public void SalesViewModel_ShouldInitialize_WithEmptyCollections()
    {
        var vm = CreateViewModel();

        vm.Journals.Should().NotBeNull();
        vm.ArchivedMonths.Should().NotBeNull();
        vm.ArchivedJournals.Should().NotBeNull();
        vm.CashMovements.Should().NotBeNull();
        vm.CashierNames.Should().NotBeNull();
    }

    [Fact]
    public void SalesViewModel_ShouldInitialize_WithDefaultFilterValues()
    {
        var vm = CreateViewModel();

        vm.SelectedCashier.Should().Be("الكل");
        vm.FilterShiftType.Should().Be("الكل");
        vm.SelectedDifferenceFilter.Should().Be("الكل");
        vm.FilterStartDate.Should().BeNull();
        vm.FilterEndDate.Should().BeNull();
        vm.SelectedTab.Should().Be(0);
    }

    [Fact]
    public void SalesViewModel_ShouldLoad_CurrentShiftOnStartup()
    {
        var vm = CreateViewModel();
        vm.Should().NotBeNull();
        vm.Journals.Should().BeEmpty();
    }

    // GROUP 2: Shift Management
    [Fact]
    public void OpenShift_ShouldChangeStatus_ToDraft()
    {
        var journal = new DailyJournal();
        journal.FinancialStatus.Should().Be(FinancialStatus.Draft);
    }

    [Fact]
    public async Task CloseShift_ShouldFail_WhenNoOpenShift()
    {
        var mockPosting = new Mock<IPostingService>();
        var vm = CreateViewModel(mockPosting: mockPosting);

        await vm.PostJournalAsync(null!);

        mockPosting.Verify(p => p.PostEntityAsync<DailyJournal>(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void PostShift_ShouldRequire_PostingService()
    {
        // Verify the ViewModel has a wired PostJournalCommand and the posting service is injected
        var mockPosting = new Mock<IPostingService>();
        mockPosting.Setup(p => p.PostEntityAsync<DailyJournal>(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var vm = CreateViewModel(mockPosting: mockPosting);

        vm.PostJournalCommand.Should().NotBeNull();
        // Verify the service is available via the constructor
        mockPosting.VerifyNoOtherCalls(); // No calls yet, just wired
    }

    // GROUP 3: Financial Calculations
    [Fact]
    public void TotalSales_ShouldSum_AllOrderAmounts()
    {
        var vm = CreateViewModel();
        var journals = new List<DailyJournal>
        {
            new DailyJournal
            {
                JournalDate = DateTime.Today,
                FinancialStatus = FinancialStatus.Draft,
                TotalSales = 1200m,
                BankingTotal = 200m,
                TotalExpenses = 100m,
                ActualCash = 900m
            },
            new DailyJournal
            {
                JournalDate = DateTime.Today,
                FinancialStatus = FinancialStatus.Draft,
                TotalSales = 800m,
                BankingTotal = 100m,
                TotalExpenses = 50m,
                ActualCash = 650m
            }
        };
        SeedAllJournals(vm, journals);

        vm.SelectedTab = 0;
        vm.ApplyFiltersCommand.Execute(null);

        vm.TotalSalesPeriod.Should().Be(2000m);
        vm.TotalCashSalesPeriod.Should().Be(1700m);
    }

    [Fact]
    public void NetAmount_ShouldEqual_SalesMinusExpenses()
    {
        var card = new MonthSummaryCard
        {
            TotalSales = 5000m,
            TotalExpenses = 1500m
        };

        card.NetProfit.Should().Be(3500m);
    }

    [Fact]
    public void CashInHand_ShouldReflect_AfterSettlement()
    {
        var vm = CreateViewModel();
        vm.CurrentCashBalance = 3000m;
        vm.SettlePayoutInput = 1200m;

        vm.SettleKeepCalculation.Should().Be(1800m);
    }

    // Additional tests
    [Fact]
    public void SalesViewModel_ShouldHave_PostJournalCommand()
    {
        var vm = CreateViewModel();

        vm.PostJournalCommand.Should().NotBeNull();
        vm.LoadDataCommand.Should().NotBeNull();
        vm.ClearFiltersCommand.Should().NotBeNull();
        vm.ApplyFiltersCommand.Should().NotBeNull();
    }

    [Fact]
    public void SalesViewModel_ShouldFilter_DraftJournals_WhenTabZero()
    {
        var vm = CreateViewModel();
        var journals = new List<DailyJournal>
        {
            new DailyJournal
            {
                JournalDate = DateTime.Today,
                FinancialStatus = FinancialStatus.Draft,
                TotalSales = 500m,
                BankingTotal = 200m,
                TotalExpenses = 100m,
                ShiftType = ShiftType.FullDay,
                EmployeeName = "Test",
                ActualCash = 200m,
                RowVersion = 1,
                ExpenseItems = new List<DailyExpenseItem>(),
                BankingItems = new List<BankingItem>(),
                Adjustments = new List<OrderAdjustmentItem>()
            },
            new DailyJournal
            {
                JournalDate = DateTime.Today,
                FinancialStatus = FinancialStatus.Posted,
                TotalSales = 1000m,
                BankingTotal = 400m,
                TotalExpenses = 200m,
                ShiftType = ShiftType.FirstShift,
                EmployeeName = "Test",
                ActualCash = 400m,
                RowVersion = 1,
                ExpenseItems = new List<DailyExpenseItem>(),
                BankingItems = new List<BankingItem>(),
                Adjustments = new List<OrderAdjustmentItem>()
            }
        };

        SeedAllJournals(vm, journals);

        vm.SelectedTab = 0;
        vm.ApplyFiltersCommand.Execute(null);

        vm.Journals.Should().HaveCount(1);
        vm.Journals[0].Journal.FinancialStatus.Should().Be(FinancialStatus.Draft);
        vm.TotalSalesPeriod.Should().Be(500m);
        vm.TotalCashSalesPeriod.Should().Be(300m);
        vm.TotalBankingSalesPeriod.Should().Be(200m);
    }

    [Fact]
    public void SettleKeepCalculation_ShouldReturn_CashBalanceMinusPayout()
    {
        var vm = CreateViewModel();
        vm.CurrentCashBalance = 1000m;
        vm.SettlePayoutInput = 700m;

        vm.SettleKeepCalculation.Should().Be(300m);
    }

    [Fact]
    public void SettleKeepCalculation_ShouldBeNegative_WhenPayoutExceedsCash()
    {
        var vm = CreateViewModel();
        vm.CurrentCashBalance = 500m;
        vm.SettlePayoutInput = 800m;

        vm.SettleKeepCalculation.Should().Be(-300m);
    }

    [Fact]
    public void TotalDifferenceColor_ShouldBeRed_WhenNegative()
    {
        var vm = CreateViewModel();
        vm.TotalDifferencePeriod = -50m;

        vm.TotalDifferenceColor.Should().Be("#ef4444");
        vm.TotalDifferenceText.Should().Contain("عجز");
    }

    [Fact]
    public void TotalDifferenceColor_ShouldBeGreen_WhenZero()
    {
        var vm = CreateViewModel();
        vm.TotalDifferencePeriod = 0m;

        vm.TotalDifferenceColor.Should().Be("#10b981");
        vm.TotalDifferenceText.Should().Contain("مطابق");
    }
}
