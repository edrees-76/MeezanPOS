using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MeezanPOS.Application.Services.Queries;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Tests.Helpers;
using Xunit;

namespace MeezanPOS.Tests.Services;

/// <summary>
/// لوحة التحكم: الكاشير لا تُحسب له الأرقام المالية، عجز وزيادة الدرج، السيولة والالتزامات،
/// رسم المصروفات (دمج بالاسم وأهم البنود والمقارنة)، وتنبيه النسخ الاحتياطي.
/// </summary>
public class DashboardQueryServiceTests
{
    private static readonly DateTime Today = new(2026, 10, 15);
    private static readonly DashboardRange ThisMonth = DashboardRange.For(DashboardPeriod.ThisMonth, Today);

    private static DailyJournal Journal(DateTime date, string cashier, decimal sales, decimal actualCash, decimal expenses = 0)
    {
        var j = TestDataBuilder.BuildDailyJournal(date, FinancialStatus.Posted, sales, expenses);
        j.EmployeeName = cashier;
        j.ActualCash = actualCash;
        return j;
    }

    [Fact]
    public void Range_ComparesWithThePreviousPeriodOfTheSameLength()
    {
        ThisMonth.Start.Should().Be(new DateTime(2026, 10, 1));
        ThisMonth.PrevStart.Should().Be(new DateTime(2026, 9, 1));
        ThisMonth.PrevEnd.Should().Be(new DateTime(2026, 9, 16).AddTicks(-1), "month-to-date is compared with the same days of last month");
        DashboardRange.For(DashboardPeriod.ThisMonth, new DateTime(2026, 3, 31)).PrevEnd
            .Should().Be(new DateTime(2026, 3, 1).AddTicks(-1), "February is shorter");
        DashboardRange.For(DashboardPeriod.LastMonth, Today).PrevEnd
            .Should().Be(new DateTime(2026, 9, 1).AddTicks(-1), "a finished month is compared in full");

        var custom = DashboardRange.For(DashboardPeriod.Custom, Today, new DateTime(2026, 10, 10), new DateTime(2026, 10, 6));
        custom.Start.Should().Be(new DateTime(2026, 10, 6), "reversed dates are swapped");
        custom.PrevStart.Should().Be(new DateTime(2026, 10, 1), "five days before");
    }

    [Fact]
    public async Task Cashier_GetsSalesOnly_WithoutFinancialFigures()
    {
        using var db = new SharedSqliteDatabase();
        await using (var context = db.CreateDbContext())
        {
            context.DailyJournals.Add(Journal(Today, "سالم", 1000m, 900m, expenses: 100m));
            context.BankAccounts.Add(TestDataBuilder.BuildBankAccount("مصرف", 5000m));
            await context.SaveChangesAsync();
        }

        var snapshot = await new DashboardQueryService(db).GetAsync(ThisMonth, DashboardAudience.SalesOnly, Today);

        snapshot.TotalSales.Should().Be(1000m);
        snapshot.TotalExpenses.Should().Be(0);
        snapshot.NetProfit.Should().Be(0);
        snapshot.CashBalance.Should().Be(0);
        snapshot.Liquidity.Should().BeNull();
        snapshot.Drawer.Should().BeNull();
        snapshot.ExpenseBars.Should().BeEmpty();
        snapshot.Activities.Should().BeEmpty();
        snapshot.Alerts.Should().NotContain(a => a.Target == "Settings", "backup alerts are for management");
    }

    [Fact]
    public async Task DrawerVariance_SumsShortagesAndOveragesPerCashier()
    {
        using var db = new SharedSqliteDatabase();
        await using (var context = db.CreateDbContext())
        {
            // المتوقع = المبيعات النقدية - المصروفات (بلا فكة ولا مصرفية)
            context.DailyJournals.AddRange(
                Journal(Today, "سالم", 1000m, 950m),          // عجز 50
                Journal(Today.AddDays(-1), "سالم", 500m, 490m), // عجز 10
                Journal(Today, "هدى", 800m, 805m));            // زيادة 5
            var draft = Journal(Today, "سالم", 999m, 0m);
            draft.FinancialStatus = FinancialStatus.Draft;      // المسودات لا تدخل
            context.DailyJournals.Add(draft);
            await context.SaveChangesAsync();
        }

        var drawer = (await new DashboardQueryService(db).GetAsync(ThisMonth, DashboardAudience.Full, Today)).Drawer!;

        drawer.Net.Should().Be(-55m);
        drawer.TotalShort.Should().Be(60m);
        drawer.TotalOver.Should().Be(5m);
        drawer.Journals.Should().Be(3);
        drawer.ShortCount.Should().Be(2);
        drawer.ByCashier.First().Should().Be(new CashierVariance("سالم", -60m, 2, 2), "the largest shortage comes first");
        drawer.ByCashier.Last().Name.Should().Be("هدى");
    }

    [Fact]
    public async Task Liquidity_ComparesWhatTheRestaurantHasWithWhatItOwes()
    {
        using var db = new SharedSqliteDatabase();
        await using (var context = db.CreateDbContext())
        {
            context.BankAccounts.AddRange(TestDataBuilder.BuildBankAccount("أ", 3000m), TestDataBuilder.BuildBankAccount("ب", 1000m));
            var inactive = TestDataBuilder.BuildBankAccount("مغلق", 999m);
            inactive.IsActive = false;
            context.BankAccounts.Add(inactive);

            var owed = TestDataBuilder.BuildSupplier("مورد مستحق");
            owed.CurrentBalance = 1200m;
            var credit = TestDataBuilder.BuildSupplier("مورد دائن");
            credit.CurrentBalance = -300m; // رصيد لنا لا يُنقص المستحقات
            context.Suppliers.AddRange(owed, credit);

            var w1 = TestDataBuilder.BuildWorker("عامل 1", 50m);
            var w2 = TestDataBuilder.BuildWorker("عامل 2", 50m);
            context.Workers.AddRange(w1, w2);
            await context.SaveChangesAsync();
            context.WorkerTransactions.AddRange(
                new WorkerTransaction { WorkerId = w1.Id, WorkerName = w1.WorkerName, TransactionDate = Today, Type = WorkerTransactionType.WageAccrual, CreditAmount = 400m },
                new WorkerTransaction { WorkerId = w1.Id, WorkerName = w1.WorkerName, TransactionDate = Today, Type = WorkerTransactionType.Payment, DebitAmount = 150m },
                new WorkerTransaction { WorkerId = w2.Id, WorkerName = w2.WorkerName, TransactionDate = Today, Type = WorkerTransactionType.Advance, DebitAmount = 200m });

            context.OwnerDebts.Add(new OwnerDebt { PartnerName = "شريك", Amount = 700m, TransactionDate = Today });
            context.OwnerDebtSettlements.Add(new OwnerDebtSettlement { PartnerName = "شريك", Amount = 200m, SettlementDate = Today });
            context.CashMovements.Add(TestDataBuilder.BuildCashMovement(2500m, CashMovementType.CashIn, 2500m, Today));
            await context.SaveChangesAsync();
        }

        var liquidity = (await new DashboardQueryService(db).GetAsync(ThisMonth, DashboardAudience.Full, Today)).Liquidity!;

        liquidity.Cash.Should().Be(2500m);
        liquidity.Banks.Should().Be(4000m, "inactive accounts are excluded");
        liquidity.SupplierPayables.Should().Be(1200m);
        liquidity.WorkerWagesDue.Should().Be(250m, "one worker's advance does not reduce another's wages");
        liquidity.PartnersDue.Should().Be(500m);
        liquidity.Net.Should().Be(2500m + 4000m - 1200m - 250m - 500m);
    }

    [Fact]
    public async Task ExpenseBars_MergeByName_KeepTheTopFive_AndCompareWithThePreviousPeriod()
    {
        using var db = new SharedSqliteDatabase();
        await using (var context = db.CreateDbContext())
        {
            var journal = Journal(Today, "سالم", 5000m, 0m, expenses: 1890m); // مجموع البنود أدناه
            var prevJournal = Journal(Today.AddMonths(-1), "سالم", 5000m, 0m, expenses: 600m);
            context.DailyJournals.AddRange(journal, prevJournal);
            await context.SaveChangesAsync();

            void Item(DailyJournal j, string name, decimal amount) => context.DailyExpenseItems.Add(
                new DailyExpenseItem { DailyJournalId = j.Id, CategoryName = name, Description = name, Amount = amount });
            Item(journal, "صيانة", 100m);
            Item(journal, "غاز", 300m);
            Item(journal, "خضار", 500m);
            Item(journal, "لحوم", 900m);
            Item(journal, "خبز", 40m);
            Item(journal, "منظفات", 30m);
            Item(journal, "تغليف", 20m);
            Item(prevJournal, "لحوم", 600m);
            // الصيانة سُجّلت أيضاً كمصروف عام: تُدمج مع بند اليومية في الرسم
            context.GeneralExpenses.Add(new GeneralExpense
            {
                ExpenseType = GeneralExpenseType.Maintenance, Amount = 250m, PaymentDate = Today, Description = "صيانة مكيف",
                FinancialStatus = FinancialStatus.Posted,
            });
            await context.SaveChangesAsync();
        }

        var snapshot = await new DashboardQueryService(db).GetAsync(ThisMonth, DashboardAudience.Full, Today);
        var bars = snapshot.ExpenseBars;

        bars.Select(b => b.Name).Should().Equal("لحوم", "خضار", "صيانة", "غاز", "خبز", DashboardQueryService.OtherExpensesName);
        bars.Single(b => b.Name == "صيانة").Amount.Should().Be(350m);
        bars.Last().Amount.Should().Be(50m, "منظفات + تغليف");
        bars.First().BarRatio.Should().Be(1);
        bars.First().ChangePercent.Should().Be(50m, "900 vs 600");
        bars.Single(b => b.Name == "غاز").IsNew.Should().BeTrue();
        bars.Sum(b => b.Amount).Should().Be(snapshot.TotalExpenses);
        snapshot.ExpenseDetails.Should().Contain(d => d.CategoryName == "صيانة" && d.SourceType == "عام", "the PDF keeps the daily/general split");
    }

    [Fact]
    public async Task BackupAlert_WhenNoExternalBackupOrAnOldOne()
    {
        using var db = new SharedSqliteDatabase();
        var service = new DashboardQueryService(db);

        (await service.GetAsync(ThisMonth, DashboardAudience.Full, Today)).Alerts
            .Should().ContainSingle(a => a.Target == "Settings" && a.Type == "Danger");

        await using (var context = db.CreateDbContext())
        {
            context.Settings.Add(new Setting { Key = DashboardQueryService.LastExternalBackupKey, Value = Today.AddDays(-10).ToString("yyyy-MM-dd HH:mm:ss") });
            await context.SaveChangesAsync();
        }
        (await service.GetAsync(ThisMonth, DashboardAudience.Full, Today)).Alerts
            .Should().ContainSingle(a => a.Target == "Settings" && a.Type == "Warning");

        await using (var context = db.CreateDbContext())
        {
            context.Settings.Add(new Setting { Key = DashboardQueryService.LastAutoBackupKey, Value = Today.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss") });
            await context.SaveChangesAsync();
        }
        (await service.GetAsync(ThisMonth, DashboardAudience.Full, Today)).Alerts
            .Should().NotContain(a => a.Target == "Settings", "the newest of the two dates counts");
    }

    [Fact]
    public async Task UnsettledPreviousMonth_IsFlagged_UntilASettlementCoversIt()
    {
        using var db = new SharedSqliteDatabase();
        await using (var context = db.CreateDbContext())
        {
            context.DailyJournals.Add(Journal(new DateTime(2026, 9, 20), "سالم", 100m, 100m));
            await context.SaveChangesAsync();
        }
        var service = new DashboardQueryService(db);

        (await service.GetAsync(ThisMonth, DashboardAudience.Full, Today)).Alerts.Should().Contain(a => a.Icon == "CalendarAlert");

        await using (var context = db.CreateDbContext())
        {
            context.PostingSessions.Add(new PostingSession
            {
                SessionType = PostingSessionType.Settlement, Status = PostingSessionStatus.Settled,
                PeriodStartDate = new DateTime(2026, 9, 1), PeriodEndDate = new DateTime(2026, 9, 30),
            });
            await context.SaveChangesAsync();
        }
        (await service.GetAsync(ThisMonth, DashboardAudience.Full, Today)).Alerts.Should().NotContain(a => a.Icon == "CalendarAlert");
    }
}
