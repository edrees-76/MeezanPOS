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
using MeezanPOS.Tests.Helpers;

namespace MeezanPOS.Tests.ViewModels;

public class ClosingAccountViewModelTests
{
    private ClosingAccountViewModel CreateViewModel(
        Mock<IFinancialReportingService>? mockReporting = null,
        Mock<IPostingService>? mockPosting = null,
        Mock<ISessionService>? mockSession = null,
        Mock<IAuthenticationService>? mockAuth = null)
    {
        // Ensure WPF Application context and patches are initialized
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();

        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        if (mockReporting == null)
        {
            mockReporting = new Mock<IFinancialReportingService>();
            mockReporting
                .Setup(r => r.GenerateSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new ClosingAccountSummary());
        }
        mockPosting ??= new Mock<IPostingService>();
        mockSession ??= new Mock<ISessionService>();
        mockAuth ??= new Mock<IAuthenticationService>();

        mockSession.Setup(s => s.CurrentUser).Returns(new User { Id = 1, Username = "admin" });
        mockSession.Setup(s => s.CurrentUserId).Returns("1");
        mockSession.Setup(s => s.CurrentUsername).Returns("admin");

        var vm = new ClosingAccountViewModel(
            mockReporting.Object,
            mockPosting.Object,
            mockSession.Object,
            mockAuth.Object);

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

    private void WaitForLoad(ClosingAccountViewModel vm)
    {
        // Wait for IsLoading to complete
        int timeout = 0;
        while (vm.IsLoading && timeout < 100)
        {
            DoEvents();
            System.Threading.Thread.Sleep(10);
            timeout++;
        }
        DoEvents();
    }

    // GROUP 1: Report Loading
    [StaFact]
    public void LoadReport_ShouldCallReportingService_WithDateRange()
    {
        var mockReporting = new Mock<IFinancialReportingService>();
        mockReporting
            .Setup(r => r.GenerateSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new ClosingAccountSummary());

        var vm = CreateViewModel(mockReporting: mockReporting);
        WaitForLoad(vm);

        vm.SelectedPeriod = DashboardPeriod.Today;
        vm.LoadReportAsync().GetAwaiter().GetResult();
        WaitForLoad(vm);

        mockReporting.Verify(r => r.GenerateSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.AtLeastOnce);
    }

    [StaFact]
    public void LoadReport_ShouldPopulate_AllFiveTabs()
    {
        var summary = new ClosingAccountSummary
        {
            IncomeStatement = new IncomeStatementReport { TotalSales = 5000m },
            ExpensesByCategory = new List<ExpenseCategoryReportItem> { new() { CategoryName = "Rent", Amount = 1000m } },
            Suppliers = new List<SupplierReportItem> { new() { SupplierName = "Supplier A", ClosingBalance = 500m } },
            Liabilities = new LiabilitiesSummary { TotalSupplierDebt = 500m },
            BankAccounts = new List<BankAccountReportItem> { new() { AccountName = "Bank A", ClosingBalance = 1500m } }
        };

        var mockReporting = new Mock<IFinancialReportingService>();
        mockReporting
            .Setup(r => r.GenerateSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(summary);

        var vm = CreateViewModel(mockReporting: mockReporting);
        WaitForLoad(vm);

        vm.Summary.Should().NotBeNull();
        vm.Summary.IncomeStatement.TotalSales.Should().Be(5000m);
        vm.Summary.ExpensesByCategory.Should().NotBeEmpty();
        vm.Summary.Suppliers.Should().NotBeEmpty();
        vm.Summary.Liabilities.TotalSupplierDebt.Should().Be(500m);
        vm.Summary.BankAccounts.Should().NotBeEmpty();
    }

    [StaFact]
    public void LoadReport_ShouldHandleEmptyData_WithoutCrash()
    {
        var mockReporting = new Mock<IFinancialReportingService>();
        mockReporting
            .Setup(r => r.GenerateSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new ClosingAccountSummary());

        var vm = CreateViewModel(mockReporting: mockReporting);
        WaitForLoad(vm);

        vm.Summary.Should().NotBeNull();
        vm.Summary.IncomeStatement.Should().NotBeNull();
    }

    // GROUP 2: Date Range Validation
    [StaFact]
    public void SetDateRange_ShouldFail_WhenEndDateBeforeStartDate()
    {
        var mockReporting = new Mock<IFinancialReportingService>();
        DateTime? capturedStart = null;
        DateTime? capturedEnd = null;

        mockReporting
            .Setup(r => r.GenerateSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Callback<DateTime, DateTime>((start, end) =>
            {
                capturedStart = start;
                capturedEnd = end;
            })
            .ReturnsAsync(new ClosingAccountSummary());

        var vm = CreateViewModel(mockReporting: mockReporting);
        WaitForLoad(vm);

        vm.SelectedPeriod = DashboardPeriod.Custom;
        // End date before start date
        vm.CustomStartDate = DateTime.Today.AddDays(5);
        vm.CustomEndDate = DateTime.Today;

        vm.LoadReportAsync().GetAwaiter().GetResult();
        WaitForLoad(vm);

        // Verify start and end dates were swapped
        capturedStart.Should().NotBeNull();
        capturedStart.Should().BeBefore(capturedEnd!.Value);
        capturedStart!.Value.Date.Should().Be(DateTime.Today.Date);
    }

    [StaFact]
    public void SetDateRange_ShouldTriggerReload_WhenDatesChange()
    {
        var mockReporting = new Mock<IFinancialReportingService>();
        var callCount = 0;

        mockReporting
            .Setup(r => r.GenerateSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Callback(() => callCount++)
            .ReturnsAsync(new ClosingAccountSummary());

        var vm = CreateViewModel(mockReporting: mockReporting);
        WaitForLoad(vm);

        vm.SelectedPeriod = DashboardPeriod.Custom;
        vm.CustomStartDate = DateTime.Today.AddDays(-10);
        vm.CustomEndDate = DateTime.Today;

        vm.LoadReportAsync().GetAwaiter().GetResult();
        WaitForLoad(vm);

        callCount.Should().Be(2); // Constructor load + Manual reload
    }

    // GROUP 3: Settlement (Closing)
    [StaFact]
    public void ConfirmSettlement_ShouldFail_WhenPasswordIsEmpty()
    {
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();

        var vm = CreateViewModel();
        WaitForLoad(vm);

        var passwordBox = new System.Windows.Controls.PasswordBox();
        passwordBox.Password = "";

        vm.ConfirmSettleAsync(passwordBox).GetAwaiter().GetResult();

        vm.SettleIsProcessing.Should().BeFalse();
        MessageBoxMock.LastMessage.Should().Contain("الرجاء إدخال كلمة مرور المدير");
    }

    [StaFact]
    public void ConfirmSettlement_ShouldFail_WhenPasswordIsWrong()
    {
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();

        var mockAuth = new Mock<IAuthenticationService>();
        mockAuth.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((User?)null);

        var vm = CreateViewModel(mockAuth: mockAuth);
        WaitForLoad(vm);

        var passwordBox = new System.Windows.Controls.PasswordBox();
        passwordBox.Password = "wrong_password";

        vm.ConfirmSettleAsync(passwordBox).GetAwaiter().GetResult();

        vm.SettleIsProcessing.Should().BeFalse();
        MessageBoxMock.LastMessage.Should().Contain("كلمة المرور غير صحيحة");
    }

    [StaFact]
    public void ConfirmSettlement_ShouldSucceed_WithCorrectPassword()
    {
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();

        var mockAuth = new Mock<IAuthenticationService>();
        mockAuth.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new User { Id = 1, Username = "admin" });

        var mockPosting = new Mock<IPostingService>();
        mockPosting.Setup(p => p.SettleAndLockPeriodAsync(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new PostingBatchResult { Success = true, PdfPath = "temp.pdf" });

        var vm = CreateViewModel(mockAuth: mockAuth, mockPosting: mockPosting);
        WaitForLoad(vm);

        vm.SettlePayoutAmount = 1000m;
        vm.SettleKeepAmount = 500m;
        vm.SettleCashBalance = 2000m;

        var passwordBox = new System.Windows.Controls.PasswordBox();
        passwordBox.Password = "correct_password";

        vm.ConfirmSettleAsync(passwordBox).GetAwaiter().GetResult();

        mockPosting.Verify(p => p.SettleAndLockPeriodAsync(1000m, 500m, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        MessageBoxMock.LastMessage.Should().Contain("بنجاح");
    }

    // GROUP 4: PDF Export
    [StaFact]
    public void ExportPdf_ShouldCallPdfExporter_WithCurrentReport()
    {
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();

        var vm = CreateViewModel();
        WaitForLoad(vm);

        vm.Summary = new ClosingAccountSummary
        {
            IncomeStatement = new IncomeStatementReport { TotalSales = 1000m }
        };

        vm.ExportPdfAsync("Comprehensive").GetAwaiter().GetResult();
        vm.IsExporting.Should().BeFalse();
    }

    [StaFact]
    public void ExportPdf_ShouldFail_WhenReportNotLoaded()
    {
        var vm = CreateViewModel();
        WaitForLoad(vm);

        vm.Summary = null!;

        vm.ExportPdfAsync("Comprehensive").GetAwaiter().GetResult();
        vm.IsExporting.Should().BeFalse();
    }

    // GROUP 5: Bank Accounts Tab
    [StaFact]
    public void BankAccountsTab_ShouldDisplay_AllActiveAccounts()
    {
        var summary = new ClosingAccountSummary
        {
            BankAccounts = new List<BankAccountReportItem>
            {
                new() { AccountName = "Account 1", BankName = "Bank A" },
                new() { AccountName = "Account 2", BankName = "Bank B" }
            }
        };

        var mockReporting = new Mock<IFinancialReportingService>();
        mockReporting
            .Setup(r => r.GenerateSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(summary);

        var vm = CreateViewModel(mockReporting: mockReporting);
        WaitForLoad(vm);

        vm.Summary.BankAccounts.Should().HaveCount(2);
        vm.Summary.BankAccounts[0].AccountName.Should().Be("Account 1");
        vm.Summary.BankAccounts[1].AccountName.Should().Be("Account 2");
    }

    [StaFact]
    public void BankAccountsTab_ShouldShowCorrectBalances_ForEachAccount()
    {
        var summary = new ClosingAccountSummary
        {
            BankAccounts = new List<BankAccountReportItem>
            {
                new() { AccountName = "Account 1", OpeningBalance = 1000m, ClosingBalance = 1500m },
                new() { AccountName = "Account 2", OpeningBalance = 2000m, ClosingBalance = 1800m }
            }
        };

        var mockReporting = new Mock<IFinancialReportingService>();
        mockReporting
            .Setup(r => r.GenerateSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(summary);

        var vm = CreateViewModel(mockReporting: mockReporting);
        WaitForLoad(vm);

        vm.Summary.BankAccounts[0].OpeningBalance.Should().Be(1000m);
        vm.Summary.BankAccounts[0].ClosingBalance.Should().Be(1500m);
        vm.Summary.BankAccounts[1].OpeningBalance.Should().Be(2000m);
        vm.Summary.BankAccounts[1].ClosingBalance.Should().Be(1800m);
    }
}
