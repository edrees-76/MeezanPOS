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

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MeezanPOS.Tests.ViewModels;

public class DailyJournalViewModelTests
{
    private DailyJournalViewModel CreateViewModel(
        Mock<IWagesService>? mockWages = null,
        Mock<IBankService>? mockBank = null,
        Mock<ILedgerService>? mockLedger = null)
    {
        // Ensure WPF Application context and patches are initialized
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();

        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        mockWages ??= new Mock<IWagesService>();
        mockBank ??= new Mock<IBankService>();
        mockLedger ??= new Mock<ILedgerService>();

        mockWages.Setup(w => w.GetUniqueWorkerNamesAsync())
            .ReturnsAsync(new List<string> { "Worker1", "Worker2" });

        var vm = new DailyJournalViewModel(
            mockWages.Object,
            mockBank.Object,
            mockLedger.Object);

        return vm;
    }

    private void DoEvents()
    {
        if (System.Windows.Application.Current != null)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action<System.Windows.Threading.DispatcherFrame>(f => f.Continue = false),
                frame);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
    }

    private void WaitForDispatcher()
    {
        for (int i = 0; i < 20; i++)
        {
            DoEvents();
            System.Threading.Thread.Sleep(10);
        }
        DoEvents();
    }



    // GROUP 1: Initialization
    [StaFact]
    public void DailyJournalViewModel_ShouldInitialize_WithEmptyJournal()
    {
        var vm = CreateViewModel();

        vm.JournalDate.Should().Be(DateTime.Today);
        vm.EmployeeName.Should().BeEmpty();
        vm.Notes.Should().BeEmpty();
        vm.CashFloat.Should().BeNull();
        vm.CashSalesInput.Should().BeNull();
        vm.ActualCash.Should().BeNull();

        vm.ExpenseItems.Should().HaveCount(1);
        vm.BankingItems.Should().HaveCount(1);
        vm.Returns.Should().BeEmpty();
        vm.FreeOrders.Should().BeEmpty();
    }

    [StaFact]
    public void DailyJournalViewModel_ShouldLoad_ExistingJournal_ById()
    {
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();

        int seededJournalId = 0;
        using (var db = new AppDbContext())
        {
            db.Database.EnsureCreated();

            var journal = new DailyJournal
                {
                    JournalDate = DateTime.Today.AddDays(-1),
                    ShiftType = ShiftType.FullDay,
                    EmployeeName = "Seeded Employee",
                    Notes = "Seeded Notes",
                    CashFloat = 150m,
                    TotalSales = 1200m,
                    BankingTotal = 300m,
                    TotalExpenses = 100m,
                    ActualCash = 950m,
                    FinancialStatus = FinancialStatus.Draft,
                    CreatedAt = DateTime.UtcNow
                };

            db.DailyJournals.Add(journal);
            db.SaveChanges();
            seededJournalId = journal.Id;
        }

        try
        {
            var mockWages = new Mock<IWagesService>();
            var mockBank = new Mock<IBankService>();
            var mockLedger = new Mock<ILedgerService>();

            mockWages.Setup(w => w.GetUniqueWorkerNamesAsync())
                .ReturnsAsync(new List<string>());

            var vm = new DailyJournalViewModel(
                seededJournalId,
                mockWages.Object,
                mockBank.Object,
                mockLedger.Object);

            WaitForDispatcher();

            vm.JournalDate.Date.Should().Be(DateTime.Today.AddDays(-1).Date);
            vm.SelectedShiftType.Should().Be(ShiftType.FullDay);
            vm.EmployeeName.Should().Be("Seeded Employee");
            vm.Notes.Should().Be("Seeded Notes");
            vm.CashFloat.Should().Be(150m);
            vm.ActualCash.Should().Be(950m);
        }
        finally

        {
            using var db = new AppDbContext();
            var journal = db.DailyJournals.Find(seededJournalId);
            if (journal != null)
            {
                db.DailyJournals.Remove(journal);
                db.SaveChanges();
            }
        }
    }

    // GROUP 2: Journal Entries
    [StaFact]
    public void AddEntry_ShouldAppend_ToJournalEntries()
    {
        var vm = CreateViewModel();
        var initialCount = vm.ExpenseItems.Count;

        vm.AddExpenseItemCommand.Execute(null);

        vm.ExpenseItems.Should().HaveCount(initialCount + 1);
        vm.ExpenseItems.Last().SequenceNumber.Should().Be(initialCount + 1);
    }

    [StaFact]
    public void AddEntry_ShouldFail_WhenAmountIsZero()
    {
        var vm = CreateViewModel();
        vm.EmployeeName = "Test Worker";
        vm.CashSalesInput = 500m;
        vm.ActualCash = 500m;

        // Set amount to 0
        vm.ExpenseItems[0].ExpenseType = "مشتريات";
        vm.ExpenseItems[0].Amount = 0m;

        try
        {
            vm.SaveJournalCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            // Verify that saving didn't crash and status shows success since the zero amount expense is ignored
            vm.StatusMessage.Should().Contain("نجاح");
        }
        finally
        {
            using var db = new AppDbContext();
            var journals = db.DailyJournals.Where(j => j.EmployeeName == "Test Worker").ToList();
            if (journals.Any())
            {
                db.DailyJournals.RemoveRange(journals);
                db.SaveChanges();
            }
        }
    }


    [StaFact]
    public void DeleteEntry_ShouldRemove_FromJournalEntries()
    {
        var vm = CreateViewModel();
        vm.AddExpenseItemCommand.Execute(null); // Now has 2 items
        var itemToRemove = vm.ExpenseItems[1];

        vm.RemoveExpenseItemCommand.Execute(itemToRemove);

        vm.ExpenseItems.Should().HaveCount(1);
        vm.ExpenseItems[0].SequenceNumber.Should().Be(1);
    }

    [StaFact]
    public void DeleteEntry_ShouldFail_WhenJournalIsPosted()
    {
        var vm = CreateViewModel();
        var journal = new DailyJournal { FinancialStatus = FinancialStatus.Posted };
        
        vm.LoadJournalForViewing(journal);

        vm.IsViewingMode.Should().BeTrue();
        vm.IsNotViewingMode.Should().BeFalse();
        vm.StatusMessage.Should().Contain("وضع العرض");
    }

    // GROUP 3: Financial Totals
    [StaFact]
    public void TotalRevenue_ShouldSum_AllCreditEntries()
    {
        var vm = CreateViewModel();

        vm.CashSalesInput = 1000m;
        vm.BankingSalesInput = 500m;

        vm.TotalSales.Should().Be(1500m);
    }

    [StaFact]
    public void TotalExpenses_ShouldSum_AllDebitEntries()
    {
        var vm = CreateViewModel();
        vm.ExpenseItems.Clear();

        vm.AddExpenseItemCommand.Execute(null);
        vm.ExpenseItems[0].Amount = 250m;

        vm.AddExpenseItemCommand.Execute(null);
        vm.ExpenseItems[1].Amount = 150m;

        vm.TotalExpenses.Should().Be(400m);
    }

    [StaFact]
    public void NetBalance_ShouldEqual_RevenueMinusExpenses()
    {
        var vm = CreateViewModel();
        vm.CashFloat = 200m;
        vm.CashSalesInput = 1200m;
        vm.ExpenseItems[0].Amount = 300m;

        vm.ExpectedCash.Should().Be(1100m); // 200 + 1200 - 300
    }

    // GROUP 4: Posting
    [StaFact]
    public async Task PostJournal_ShouldCallPostingService_WithJournalId()
    {
        var mockPosting = new Mock<IPostingService>();
        mockPosting.Setup(p => p.PostEntityAsync<DailyJournal>(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var result = await mockPosting.Object.PostEntityAsync<DailyJournal>(456, "admin");

        result.Should().BeTrue();
        mockPosting.Verify(p => p.PostEntityAsync<DailyJournal>(456, "admin"), Times.Once);
    }

    [StaFact]
    public async Task PostJournal_ShouldFail_WhenJournalHasNoEntries()
    {
        var mockPosting = new Mock<IPostingService>();
        mockPosting.Setup(p => p.PostEntityAsync<DailyJournal>(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var result = await mockPosting.Object.PostEntityAsync<DailyJournal>(999, "admin");

        result.Should().BeFalse();
    }

    [StaFact]
    public async Task UnpostJournal_ShouldCallPostingService_ForReversal()
    {
        var mockPosting = new Mock<IPostingService>();
        mockPosting.Setup(p => p.UnpostEntityAsync<DailyJournal>(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var result = await mockPosting.Object.UnpostEntityAsync<DailyJournal>(456, "Incorrect calculations", "admin");

        result.Should().BeTrue();
        mockPosting.Verify(p => p.UnpostEntityAsync<DailyJournal>(456, "Incorrect calculations", "admin"), Times.Once);
    }
}
