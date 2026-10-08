using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MeezanPOS.Tests.Services;

/// <summary>صرف مستحقات العامل عبر WagesService (كان داخل شاشة الأجور).</summary>
public class WorkerPaymentTests
{
    private static readonly DateTime Day = DateTime.Today.AddDays(-1);

    private static async Task<int> SeedWorkerAndJournalAsync(SharedSqliteDatabase db, ServiceProvider sp)
    {
        int workerId;
        await using (var ctx = db.CreateDbContext())
        {
            var worker = new Worker { WorkerName = "عامل", DailyWage = 50m, IsActive = true };
            ctx.Workers.Add(worker);
            await ctx.SaveChangesAsync();
            workerId = worker.Id;
        }

        var journals = new DailyJournalService(db, sp.GetRequiredService<IServiceScopeFactory>());
        var saved = await journals.SaveAsync(new JournalSaveRequest
        {
            JournalDate = Day,
            Shift = ShiftType.FullDay,
            ShiftDisplayName = "يوم كامل",
            EmployeeName = "كاشير",
            TotalSales = 1000m,
            ActualCash = 1000m,
        });
        saved.Success.Should().BeTrue(saved.Error);
        return workerId;
    }

    private static WagesService Service(ServiceProvider sp, IServiceScope scope)
        => new(scope.ServiceProvider.GetRequiredService<MeezanPOS.Infrastructure.Data.AppDbContext>(),
               scope.ServiceProvider.GetRequiredService<ISessionService>(),
               scope.ServiceProvider.GetRequiredService<ICashLedgerService>());

    [Fact]
    public async Task FromCashier_AddsExpenseToThatDaysDraftJournal()
    {
        using var db = new SharedSqliteDatabase();
        using var sp = db.BuildServices();
        var workerId = await SeedWorkerAndJournalAsync(db, sp);

        using (var scope = sp.CreateScope())
        {
            var result = await Service(sp, scope).PayWorkerAsync(new WorkerPaymentRequest(workerId, "عامل", 120m, Day, null, FromCashier: true));
            result.Success.Should().BeTrue(result.Error);
        }

        await using var check = db.CreateDbContext();
        var journal = await check.DailyJournals.Include(j => j.ExpenseItems).SingleAsync();
        journal.TotalExpenses.Should().Be(120m);
        journal.ExpenseItems.Should().ContainSingle(e => e.Type == ExpenseType.WorkerWage && e.WorkerId == workerId && e.Amount == 120m);
        (await check.CashMovements.CountAsync()).Should().Be(0, "the drawer payout shows in the journal, not in the restaurant cash");
    }

    [Fact]
    public async Task FromCashier_WithoutDraftJournalThatDay_Fails()
    {
        using var db = new SharedSqliteDatabase();
        using var sp = db.BuildServices();
        var workerId = await SeedWorkerAndJournalAsync(db, sp);

        using var scope = sp.CreateScope();
        var result = await Service(sp, scope).PayWorkerAsync(new WorkerPaymentRequest(workerId, "عامل", 120m, Day.AddDays(-5), null, FromCashier: true));

        result.Success.Should().BeFalse();
        (await db.CreateDbContext().DailyExpenseItems.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task FromRestaurantCash_CreatesGeneralExpenseAndCashOut()
    {
        using var db = new SharedSqliteDatabase();
        using var sp = db.BuildServices();
        var workerId = await SeedWorkerAndJournalAsync(db, sp);

        using (var scope = sp.CreateScope())
        {
            var result = await Service(sp, scope).PayWorkerAsync(new WorkerPaymentRequest(workerId, "عامل", 75m, Day, "دفعة", FromCashier: false));
            result.Success.Should().BeTrue(result.Error);
        }

        await using var check = db.CreateDbContext();
        var expense = await check.GeneralExpenses.SingleAsync();
        expense.ExpenseType.Should().Be(GeneralExpenseType.Salaries);
        expense.WorkerId.Should().Be(workerId);
        var movement = await check.CashMovements.SingleAsync();
        movement.Type.Should().Be(CashMovementType.CashOut);
        movement.Amount.Should().Be(75m);
        movement.SourceId.Should().Be(expense.Id);
    }
}
