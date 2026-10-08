using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Tests.Helpers;

namespace MeezanPOS.Tests.Services;

/// <summary>
/// الصلاحيات، إدارة المستخدمين، سجل التدقيق، والقفل المتصاعد.
/// AuditContext ثابت على مستوى التطبيق، لذا تُجمع الاختبارات التي تضبطه في مجموعة غير متوازية.
/// </summary>
[Collection(nameof(AuditContextCollection))]
public class SecurityTests
{
    private sealed record Env(AppDbContext Context, SessionService Session, UserManagementService Users, AuthenticationService Auth);

    private static async Task<Env> CreateAsync(RoleType currentRole = RoleType.Admin, string currentPassword = "Admin#2026")
    {
        var context = SqliteTestDbContextFactory.Create();
        var auth = new AuthenticationService(context);
        var role = new Role { Type = currentRole, Name = currentRole.ToString() };
        context.Roles.Add(role);
        await context.SaveChangesAsync();
        var me = new User { Username = "owner", FullName = "Owner", PasswordHash = auth.HashPassword(currentPassword), RoleId = role.Id };
        context.Users.Add(me);
        await context.SaveChangesAsync();
        me.Role = role;

        var session = new SessionService();
        session.SetUser(me);
        var users = new UserManagementService(context, session, auth, new AuditService(context));
        return new Env(context, session, users, auth);
    }

    [Theory]
    [InlineData(RoleType.Admin, Permissions.ManageUsers, true)]
    [InlineData(RoleType.Manager, Permissions.ManageUsers, false)]
    [InlineData(RoleType.Manager, Permissions.UnpostFinancial, false)]
    [InlineData(RoleType.Manager, Permissions.ManageSuppliers, true)]
    [InlineData(RoleType.Manager, Permissions.RestoreBackup, false)]
    [InlineData(RoleType.Cashier, Permissions.CreateJournal, true)]
    [InlineData(RoleType.Cashier, Permissions.ViewSettings, false)]
    [InlineData(RoleType.Cashier, Permissions.ManageExpenses, false)]
    public void RoleMatrix(RoleType role, string permission, bool expected)
        => Permissions.RoleAllows(role, permission).Should().Be(expected);

    [Theory]
    [InlineData("admin", "admin", false)]
    [InlineData("12345", "user1", false)]
    [InlineData("aaaaaaa", "user1", false)]
    [InlineData("Kitchen2026", "user1", true)]
    public void PasswordRules(string password, string username, bool ok)
        => (UserManagementService.ValidatePassword(password, username) == null).Should().Be(ok);

    [Fact]
    public async Task ChangeOwnPassword_RequiresCurrentPassword_AndClearsMustChange()
    {
        var env = await CreateAsync();
        env.Session.CurrentUser!.MustChangePassword = true;

        await FluentActions.Awaiting(() => env.Users.ChangeOwnPasswordAsync("wrong", "NewPass#1"))
            .Should().ThrowAsync<InvalidOperationException>();

        await env.Users.ChangeOwnPasswordAsync("Admin#2026", "NewPass#1");

        env.Session.CurrentUser.MustChangePassword.Should().BeFalse();
        (await env.Auth.AuthenticateAsync("owner", "NewPass#1")).Should().NotBeNull();
    }

    [Fact]
    public async Task Manager_CannotCreateUsers()
    {
        var env = await CreateAsync(RoleType.Manager);

        await FluentActions.Awaiting(() => env.Users.CreateUserAsync("cashier1", "Cashier", "Cash#2026", RoleType.Cashier))
            .Should().ThrowAsync<PermissionDeniedException>();
    }

    [Fact]
    public async Task Admin_CreatesUser_WithForcedPasswordChange_AndRoleIsCreatedOnDemand()
    {
        var env = await CreateAsync();

        var user = await env.Users.CreateUserAsync("cashier1", "كاشير الوردية", "Cash#2026", RoleType.Cashier);

        user.MustChangePassword.Should().BeTrue();
        (await env.Context.Roles.AnyAsync(r => r.Type == RoleType.Cashier)).Should().BeTrue();
        await FluentActions.Awaiting(() => env.Users.CreateUserAsync("cashier1", "x", "Other#2026", RoleType.Cashier))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*مستخدم مسبقاً*");
    }

    [Fact]
    public async Task LastActiveAdmin_CannotBeDisabledOrDemoted()
    {
        var env = await CreateAsync();
        var second = await env.Users.CreateUserAsync("admin2", "Second", "Second#2026", RoleType.Admin);

        // تعطيل المدير الثاني مسموح (يبقى owner)، ثم لا يمكن تخفيض owner لأنه آخر مدير فعّال
        await env.Users.SetActiveAsync(second.Id, false);
        await FluentActions.Awaiting(() => env.Users.SetActiveAsync(env.Session.CurrentUser!.Id, false))
            .Should().ThrowAsync<InvalidOperationException>();

        await env.Users.SetActiveAsync(second.Id, true);
        await env.Users.ChangeRoleAsync(second.Id, RoleType.Manager);
        await FluentActions.Awaiting(() => env.Users.ChangeRoleAsync(env.Session.CurrentUser!.Id, RoleType.Cashier))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AuditService_AttributesNumericUserIdCorrectly()
    {
        var env = await CreateAsync();
        var other = await env.Users.CreateUserAsync("manager1", "Manager", "Mgr#20261", RoleType.Manager);

        await new AuditService(env.Context).LogAsync(other.Id.ToString(), "Post", "DailyJournal", 7, "Draft", "Posted");

        var log = await env.Context.AuditLogs.OrderByDescending(a => a.Id).FirstAsync(a => a.Action == "Post");
        log.UserId.Should().Be(other.Id);
    }

    [Fact]
    public async Task SoftDeleteAndAmountChanges_AreAuditedAutomatically()
    {
        var env = await CreateAsync();
        var expense = new GeneralExpense
        {
            ExpenseType = GeneralExpenseType.Other,
            Amount = 150m,
            PaymentDate = DateTime.Today,
            PaymentMethod = PaymentMethodType.Cash,
            Description = "x"
        };
        env.Context.GeneralExpenses.Add(expense);
        await env.Context.SaveChangesAsync();

        expense.Amount = 175m;
        await env.Context.SaveChangesAsync();
        expense.IsDeleted = true;
        await env.Context.SaveChangesAsync();

        var logs = await env.Context.AuditLogs.Where(a => a.EntityName == nameof(GeneralExpense) && a.EntityId == expense.Id).ToListAsync();
        logs.Should().Contain(l => l.Action == "AutoUpdate" && l.Changes.Contains("150") && l.Changes.Contains("175"));
        logs.Should().Contain(l => l.Action == "AutoSoftDelete");
        logs.Should().OnlyContain(l => l.UserId == env.Session.CurrentUser!.Id);

        env.Session.ClearSession();
    }

    [Fact]
    public async Task Lockout_EscalatesAfterRepeatedFailures()
    {
        var env = await CreateAsync();
        env.Session.ClearSession();

        for (int i = 0; i < AuthenticationService.MaxAttemptsBeforeLockout; i++)
            await env.Auth.AuthenticateAsync("owner", "bad");
        var first = (await env.Context.Users.AsNoTracking().FirstAsync(u => u.Username == "owner")).LockoutEnd!.Value;

        // انتهاء القفل الأول ثم محاولة فاشلة جديدة: قفل أطول
        var user = await env.Context.Users.FirstAsync(u => u.Username == "owner");
        user.LockoutEnd = DateTime.UtcNow.AddSeconds(-1);
        await env.Context.SaveChangesAsync();
        await env.Auth.AuthenticateAsync("owner", "bad");
        var second = (await env.Context.Users.AsNoTracking().FirstAsync(u => u.Username == "owner")).LockoutEnd!.Value;

        (first - DateTime.UtcNow).TotalMinutes.Should().BeApproximately(5, 0.5);
        (second - DateTime.UtcNow).TotalMinutes.Should().BeApproximately(10, 0.5);
    }

    [Fact]
    public async Task PostingService_DeniesUnpostForManager()
    {
        var env = await CreateAsync(RoleType.Manager);
        var cash = new CashLedgerService(env.Context, env.Session);
        var posting = new PostingService(env.Context, cash, new AuditService(env.Context), env.Session);

        await FluentActions.Awaiting(() => posting.UnpostEntityAsync<DailyJournal>(1, "سبب طويل بما يكفي لفك ترحيل الوردية", "1"))
            .Should().ThrowAsync<PermissionDeniedException>();

        env.Session.ClearSession();
    }
}

[CollectionDefinition(nameof(AuditContextCollection), DisableParallelization = true)]
public class AuditContextCollection { }
