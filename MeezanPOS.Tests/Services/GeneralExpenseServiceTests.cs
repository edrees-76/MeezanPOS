using System;
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

/// <summary>حفظ وتعديل وحذف المصروفات العامة عبر الخدمة، مع حركاتها النقدية والبنكية.</summary>
public class GeneralExpenseServiceTests
{
    private static readonly DateTime Day = DateTime.Today.AddDays(-2);

    private static GeneralExpenseSaveRequest Cash(decimal amount, int? id = null) => new()
    {
        EditingId = id,
        ExpenseType = GeneralExpenseType.Electricity,
        TypeDisplayName = "كهرباء",
        Amount = amount,
        PaymentDate = Day,
        PaymentMethod = PaymentMethodType.Cash,
    };

    private static decimal LiveCash(Microsoft.EntityFrameworkCore.DbContext ctx)
        => ((MeezanPOS.Infrastructure.Data.AppDbContext)ctx).CashMovements.WhereLive().AsEnumerable()
            .Sum(m => m.Type == CashMovementType.CashIn ? m.Amount : -m.Amount);

    [Fact]
    public async Task Add_Edit_Delete_KeepCashMovementsConsistent()
    {
        using var db = new SharedSqliteDatabase();
        using var sp = db.BuildServices();
        var service = new GeneralExpenseService(db, sp.GetRequiredService<IServiceScopeFactory>());

        (await service.SaveAsync(Cash(300m))).Success.Should().BeTrue();
        int id;
        await using (var c = db.CreateDbContext())
        {
            id = (await c.GeneralExpenses.SingleAsync()).Id;
            LiveCash(c).Should().Be(-300m);
        }

        (await service.SaveAsync(Cash(450m, id))).Success.Should().BeTrue();
        await using (var c = db.CreateDbContext())
        {
            (await c.GeneralExpenses.SingleAsync()).Amount.Should().Be(450m);
            LiveCash(c).Should().Be(-450m, "the old cash-out is reversed when editing");
        }

        (await service.DeleteAsync(id)).Success.Should().BeTrue();
        await using (var c = db.CreateDbContext())
        {
            LiveCash(c).Should().Be(0m);
            (await c.GeneralExpenses.CountAsync()).Should().Be(0, "deleted expenses are filtered out");
        }
    }

    [Fact]
    public async Task SwitchingCashToBank_MovesTheMoneyToTheBankAccount()
    {
        using var db = new SharedSqliteDatabase();
        using var sp = db.BuildServices();
        int bankId;
        await using (var c = db.CreateDbContext())
        {
            var bank = new BankAccount { FriendlyName = "مصرف", OpeningBalance = 1000m, CurrentBalance = 1000m, IsActive = true };
            c.BankAccounts.Add(bank);
            await c.SaveChangesAsync();
            bankId = bank.Id;
        }
        var service = new GeneralExpenseService(db, sp.GetRequiredService<IServiceScopeFactory>());
        await service.SaveAsync(Cash(200m));
        int id;
        await using (var c = db.CreateDbContext()) id = (await c.GeneralExpenses.SingleAsync()).Id;

        var toBank = new GeneralExpenseSaveRequest
        {
            EditingId = id, ExpenseType = GeneralExpenseType.Electricity, TypeDisplayName = "كهرباء", Amount = 200m,
            PaymentDate = Day, PaymentMethod = PaymentMethodType.BankTransfer, BankAccountId = bankId, BankReferenceNumber = "1234",
        };
        (await service.SaveAsync(toBank)).Success.Should().BeTrue();

        await using var check = db.CreateDbContext();
        LiveCash(check).Should().Be(0m);
        (await check.BankAccounts.FindAsync(bankId))!.CurrentBalance.Should().Be(800m);
        (await service.GetPaymentInfoAsync(id, false)).ReferenceNumber.Should().Be("1234");
    }

    [Fact]
    public async Task SupplierPaymentExpense_CannotBeDeletedOrEditedFromTheExpenseService()
    {
        using var db = new SharedSqliteDatabase();
        using var sp = db.BuildServices();
        int id;
        await using (var c = db.CreateDbContext())
        {
            var expense = new GeneralExpense
            {
                ExpenseType = GeneralExpenseType.SupplierPayment, Amount = 200m, PaymentDate = Day,
                PaymentMethod = PaymentMethodType.Cash, Description = "تسديد مورد",
            };
            c.GeneralExpenses.Add(expense);
            await c.SaveChangesAsync();
            id = expense.Id;
        }
        var service = new GeneralExpenseService(db, sp.GetRequiredService<IServiceScopeFactory>());

        (await service.DeleteAsync(id)).Success.Should().BeFalse("the payment lives in the supplier statement");
        (await service.SaveAsync(Cash(250m, id))).Success.Should().BeFalse();
        await using var check = db.CreateDbContext();
        (await check.GeneralExpenses.SingleAsync()).Amount.Should().Be(200m);
    }
}
