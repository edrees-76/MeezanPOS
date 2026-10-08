using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MeezanPOS.Tests.Services;

/// <summary>حفظ اليومية عبر الخدمة (كان داخل الشاشة): بنود، دفعات موردين، أجور، وتكرار الوردية.</summary>
public class DailyJournalServiceTests
{
    private static readonly DateTime Day = DateTime.Today.AddDays(-3);

    private static JournalSaveRequest Request(int? id = null, ShiftType shift = ShiftType.FullDay, int? supplierId = null) => new()
    {
        EditingJournalId = id,
        JournalDate = Day,
        Shift = shift,
        ShiftDisplayName = "يوم كامل",
        EmployeeName = "كاشير",
        TotalSales = 1000m,
        TotalExpenses = 200m,
        ActualCash = 800m,
        ExpenseItems = supplierId == null
            ? new() { new DailyExpenseItem { SequenceNumber = 1, Amount = 200m, Category = "نظافة", Type = ExpenseType.Cleaning } }
            : new() { new DailyExpenseItem { SequenceNumber = 1, Amount = 200m, Category = "دفعة مورد", Type = ExpenseType.SupplierPayment, SupplierId = supplierId } },
    };

    [Fact]
    public async Task Save_NewJournal_PersistsItems_AndDatesSupplierPaymentWithJournalDate()
    {
        using var db = new SharedSqliteDatabase();
        using var sp = db.BuildServices();
        int supplierId;
        await using (var ctx = db.CreateDbContext())
        {
            var supplier = new Supplier { Name = "مورد", OpeningBalance = 1000m, CurrentBalance = 1000m, IsActive = true };
            ctx.Suppliers.Add(supplier);
            await ctx.SaveChangesAsync();
            supplierId = supplier.Id;
        }
        var service = new DailyJournalService(db, sp.GetRequiredService<IServiceScopeFactory>());

        var result = await service.SaveAsync(Request(supplierId: supplierId));

        result.Success.Should().BeTrue(result.Error);
        await using var check = db.CreateDbContext();
        var journal = await check.DailyJournals.Include(j => j.ExpenseItems).SingleAsync();
        journal.ExpenseItems.Should().ContainSingle();
        var payment = await check.SupplierTransactions.SingleAsync();
        payment.TransactionDate.Date.Should().Be(Day.Date);
        (await check.Suppliers.FindAsync(supplierId))!.CurrentBalance.Should().Be(800m);
    }

    [Fact]
    public async Task Save_DuplicateShiftSameDay_IsRejected()
    {
        using var db = new SharedSqliteDatabase();
        using var sp = db.BuildServices();
        var service = new DailyJournalService(db, sp.GetRequiredService<IServiceScopeFactory>());

        (await service.SaveAsync(Request())).Success.Should().BeTrue();
        var second = await service.SaveAsync(Request());

        second.Success.Should().BeFalse();
        await using var check = db.CreateDbContext();
        (await check.DailyJournals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Save_WorkerDetails_AreLinkedToTheirExpenseItem()
    {
        using var db = new SharedSqliteDatabase();
        using var sp = db.BuildServices();
        int workerId;
        await using (var ctx = db.CreateDbContext())
        {
            var worker = new Worker { WorkerName = "عامل", DailyWage = 50m, IsActive = true };
            ctx.Workers.Add(worker);
            await ctx.SaveChangesAsync();
            workerId = worker.Id;
        }
        var service = new DailyJournalService(db, sp.GetRequiredService<IServiceScopeFactory>());
        var request = Request();
        request.ExpenseItems.Add(new DailyExpenseItem { SequenceNumber = 2, Amount = 50m, Category = "يومية عامل", Type = ExpenseType.WorkerWage, WorkerName = "[متعدد]" });
        request.WorkerTransactionsBySequence[2] = new List<WorkerTransaction>
        {
            new() { WorkerId = workerId, WorkerName = "عامل", TransactionDate = Day, Type = WorkerTransactionType.WageAccrual, CreditAmount = 50m },
        };

        (await service.SaveAsync(request)).Success.Should().BeTrue();

        await using var check = db.CreateDbContext();
        var item = await check.DailyExpenseItems.SingleAsync(e => e.SequenceNumber == 2);
        var tx = await check.WorkerTransactions.SingleAsync(t => t.Type == WorkerTransactionType.WageAccrual);
        tx.DailyExpenseItemId.Should().Be(item.Id);
    }
}
