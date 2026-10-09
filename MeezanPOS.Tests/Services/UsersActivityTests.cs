using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MeezanPOS.Application.Services;
using MeezanPOS.Application.Services.Queries;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MeezanPOS.Tests.Services;

/// <summary>
/// قسم "المستخدمون والنشاط": تسجيل الدخول والخروج والمحاولات الفاشلة، فك القفل، تسجيل الإضافة،
/// وحماية السجل من التعديل والحذف. AuditContext ثابت، لذا الاختبارات في المجموعة غير المتوازية.
/// </summary>
[Collection(nameof(AuditContextCollection))]
public class UsersActivityTests
{
    private sealed record Env(SharedSqliteDatabase Db, AppDbContext Context, SessionService Session, AuthenticationService Auth,
        UserManagementService Users, User Admin) : IDisposable
    {
        public void Dispose()
        {
            Session.ClearSession();
            Context.Dispose();
            Db.Dispose();
        }
    }

    private static async Task<Env> CreateAsync()
    {
        var db = new SharedSqliteDatabase();
        var context = db.CreateDbContext();
        var auth = new AuthenticationService(context);
        var role = new Role { Type = RoleType.Admin, Name = "Admin" };
        context.Roles.Add(role);
        await context.SaveChangesAsync();
        var admin = new User { Username = "owner", FullName = "المالك", PasswordHash = auth.HashPassword("Admin#2026"), RoleId = role.Id };
        var cashier = new User { Username = "cashier", FullName = "كاشير", PasswordHash = auth.HashPassword("Cash#2026"), RoleId = role.Id };
        context.Users.AddRange(admin, cashier);
        await context.SaveChangesAsync();
        admin.Role = role;

        var session = new SessionService();
        session.SetUser(admin);
        var users = new UserManagementService(context, session, auth, new AuditService(context));
        return new Env(db, context, session, auth, users, admin);
    }

    [Fact]
    public async Task FailedLogins_AreLogged_AndLockoutCanBeLiftedByAdmin()
    {
        using var env = await CreateAsync();

        for (int i = 0; i < AuthenticationService.MaxAttemptsBeforeLockout; i++)
            (await env.Auth.AuthenticateAsync("cashier", "wrong")).Should().BeNull();

        var cashier = await env.Context.Users.SingleAsync(u => u.Username == "cashier");
        var events = await env.Context.AuditLogs.Where(a => a.UserId == cashier.Id).Select(a => a.Action).ToListAsync();
        events.Count(a => a == ActivityLabels.LoginFailed).Should().Be(AuthenticationService.MaxAttemptsBeforeLockout - 1);
        events.Should().Contain(ActivityLabels.LockedOut);
        (await env.Auth.AuthenticateAsync("cashier", "Cash#2026")).Should().BeNull("the account is locked");

        await env.Users.UnlockAsync(cashier.Id);

        (await env.Auth.AuthenticateAsync("cashier", "Cash#2026")).Should().NotBeNull();
        (await env.Context.AuditLogs.AnyAsync(a => a.Action == ActivityLabels.UnlockUser && a.EntityId == cashier.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task CreatingAFinancialRecord_IsLoggedWithItsId()
    {
        using var env = await CreateAsync();
        var expense = new GeneralExpense
        {
            ExpenseType = GeneralExpenseType.Electricity, Amount = 120m, PaymentDate = DateTime.Today,
            PaymentMethod = PaymentMethodType.Cash, Description = "كهرباء",
        };

        env.Context.GeneralExpenses.Add(expense);
        await env.Context.SaveChangesAsync();

        var log = await env.Context.AuditLogs.SingleAsync(a => a.Action == ActivityLabels.AutoCreate);
        log.EntityName.Should().Be(nameof(GeneralExpense));
        log.EntityId.Should().Be(expense.Id);
        log.UserId.Should().Be(env.Admin.Id);
        log.Changes.Should().Contain("Amount=120");
    }

    [Fact]
    public async Task ActivityLog_CannotBeEditedOrDeleted()
    {
        using var env = await CreateAsync();
        PostedRecordTriggers.Recreate(env.Context.Database.GetDbConnection());
        await env.Auth.RecordSessionEventAsync(env.Admin.Id, ActivityLabels.Login);
        var id = (await env.Context.AuditLogs.SingleAsync()).Id;

        await FluentActions.Awaiting(() => env.Context.Database.ExecuteSqlRawAsync("UPDATE AuditLogs SET Action = 'x' WHERE Id = {0}", id))
            .Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => env.Context.Database.ExecuteSqlRawAsync("DELETE FROM AuditLogs WHERE Id = {0}", id))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ActivityQuery_FiltersByCategoryAndUser_WithArabicNames()
    {
        using var env = await CreateAsync();
        var cashier = await env.Context.Users.SingleAsync(u => u.Username == "cashier");
        await env.Auth.RecordSessionEventAsync(env.Admin.Id, ActivityLabels.Login);
        await env.Auth.RecordSessionEventAsync(cashier.Id, ActivityLabels.Login);
        await env.Users.SetActiveAsync(cashier.Id, false);
        var query = new ActivityQueryService(env.Db);

        var sessions = await query.GetActivityAsync(new ActivityFilter(DateTime.Today, DateTime.Today, null, ActivityCategory.Sessions));
        sessions.Should().HaveCount(2).And.OnlyContain(r => r.ActionName == "تسجيل دخول");

        var cashierOnly = await query.GetActivityAsync(new ActivityFilter(DateTime.Today, DateTime.Today, cashier.Id));
        cashierOnly.Should().ContainSingle().Which.UserName.Should().Be("كاشير (cashier)");

        var userAdmin = await query.GetActivityAsync(new ActivityFilter(DateTime.Today, DateTime.Today, null, ActivityCategory.Users));
        userAdmin.Should().ContainSingle(r => r.ActionName == "إيقاف حساب" && r.EntityName == "مستخدم");
    }

    [Fact]
    public void RoleProfiles_ExplainExactlyTheEnforcedPermissions()
    {
        foreach (var profile in RoleProfiles.All)
            (profile.Allowed.Count + profile.Denied.Count).Should().Be(RoleProfiles.All[0].Allowed.Count + RoleProfiles.All[0].Denied.Count);

        RoleProfiles.For(RoleType.Admin).Denied.Should().BeEmpty();
        RoleProfiles.For(RoleType.Cashier).Denied.Should().Contain("إدارة المستخدمين وسجل النشاط");
        RoleProfiles.For(RoleType.Cashier).Allowed.Should().Contain("تسجيل اليوميات");
        RoleProfiles.For(RoleType.Manager).Denied.Should().Contain("إعادة ضبط المنظومة");
    }
}
