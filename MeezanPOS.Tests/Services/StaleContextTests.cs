using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MeezanPOS.Tests.Services;

/// <summary>
/// الشاشات تحتفظ بسياق طويل العمر؛ ما تسجله شاشة أخرى في الأثناء يجب ألا يُمحى
/// (رصيد مصرف قديم في الذاكرة، أو يومية عُدّلت بعد فتحها).
/// </summary>
public class StaleContextTests
{
    [Fact]
    public async Task BankDeposits_FromTwoLongLivedContexts_AreBothKeptInTheBalance()
    {
        using var db = new SharedSqliteDatabase();
        using var sp = db.BuildServices();
        int bankId;
        await using (var c = db.CreateDbContext())
        {
            var bank = new BankAccount { FriendlyName = "مصرف", OpeningBalance = 0m, CurrentBalance = 0m, IsActive = true };
            c.BankAccounts.Add(bank);
            await c.SaveChangesAsync();
            bankId = bank.Id;
        }

        using var screenA = sp.CreateScope();
        using var screenB = sp.CreateScope();
        var bankA = screenA.ServiceProvider.GetRequiredService<IBankService>();
        var bankB = screenB.ServiceProvider.GetRequiredService<IBankService>();
        // الشاشتان تقرآن الحساب ورصيده صفر
        await bankA.GetAccountByIdAsync(bankId);
        await bankB.GetAccountByIdAsync(bankId);

        await bankA.RecordDepositAsync(bankId, 100m, null, null, DateTime.Today);
        await bankB.RecordDepositAsync(bankId, 200m, null, null, DateTime.Today);

        await using var check = db.CreateDbContext();
        (await check.BankAccounts.FindAsync(bankId))!.CurrentBalance.Should().Be(300m, "the second screen must not overwrite the first deposit");
    }

    [Fact]
    public async Task SavingAJournalChangedAfterItWasOpened_IsRejected()
    {
        using var db = new SharedSqliteDatabase();
        using var sp = db.BuildServices();
        var journals = new DailyJournalService(db, sp.GetRequiredService<IServiceScopeFactory>());
        var created = await journals.SaveAsync(new JournalSaveRequest
        {
            JournalDate = DateTime.Today, Shift = ShiftType.FullDay, ShiftDisplayName = "يوم كامل",
            EmployeeName = "كاشير", TotalSales = 1000m, ActualCash = 1000m,
        });
        long openedVersion;
        await using (var c = db.CreateDbContext())
            openedVersion = (await c.DailyJournals.SingleAsync()).RowVersion;

        // أثناء فتح اليومية: صرف أجر عامل من الدرج
        int workerId;
        await using (var c = db.CreateDbContext())
        {
            var worker = new Worker { WorkerName = "عامل", DailyWage = 50m, IsActive = true };
            c.Workers.Add(worker);
            await c.SaveChangesAsync();
            workerId = worker.Id;
        }
        using (var scope = sp.CreateScope())
        {
            var wages = new WagesService(scope.ServiceProvider.GetRequiredService<AppDbContext>(), null,
                scope.ServiceProvider.GetRequiredService<ICashLedgerService>());
            (await wages.PayWorkerAsync(new MeezanPOS.Application.Interfaces.WorkerPaymentRequest(workerId, "عامل", 50m, DateTime.Today, null, true)))
                .Success.Should().BeTrue();
        }

        var result = await journals.SaveAsync(new JournalSaveRequest
        {
            EditingJournalId = created.JournalId, ExpectedRowVersion = openedVersion,
            JournalDate = DateTime.Today, Shift = ShiftType.FullDay, ShiftDisplayName = "يوم كامل",
            EmployeeName = "كاشير", TotalSales = 1200m, ActualCash = 1200m,
        });

        result.Success.Should().BeFalse("saving would delete the worker payout added meanwhile");
        await using var check = db.CreateDbContext();
        var journal = await check.DailyJournals.Include(j => j.ExpenseItems).SingleAsync();
        journal.ExpenseItems.Should().ContainSingle(e => e.Type == ExpenseType.WorkerWage);
        journal.TotalExpenses.Should().Be(50m);
    }
}
