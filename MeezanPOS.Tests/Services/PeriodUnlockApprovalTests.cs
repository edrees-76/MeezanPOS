using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Tests.Helpers;
using Xunit;

namespace MeezanPOS.Tests.Services;

/// <summary>
/// اعتماد فك قفل الفترة: كلمة مرور صحيحة، صلاحية فك القفل، ومبدأ الشخصين عند وجود مخوَّل آخر.
/// </summary>
[Collection(nameof(AuditContextCollection))]
public class PeriodUnlockApprovalTests
{
    private const string Password = "Strong#2026";

    private static async Task<(AppDbContext Context, PeriodUnlockApproval Approval, AuthenticationService Auth)> CreateAsync()
    {
        var context = SqliteTestDbContextFactory.Create();
        var auth = new AuthenticationService(context);
        context.Roles.AddRange(
            new Role { Type = RoleType.Admin, Name = "Admin" },
            new Role { Type = RoleType.Manager, Name = "Manager" });
        await context.SaveChangesAsync();
        return (context, new PeriodUnlockApproval(context, auth), auth);
    }

    private static async Task<User> AddUserAsync(AppDbContext context, AuthenticationService auth, string username, RoleType role)
    {
        var roleId = context.Roles.Local.Single(r => r.Type == role).Id;
        var user = new User { Username = username, FullName = username, PasswordHash = auth.HashPassword(Password), RoleId = roleId };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task SoleAdmin_CanApproveOwnUnlock_WithPassword()
    {
        var (context, approval, auth) = await CreateAsync();
        var admin = await AddUserAsync(context, auth, "admin", RoleType.Admin);

        var result = await approval.VerifyAsync("admin", Password, admin.Id);

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task WrongPassword_IsRejected()
    {
        var (context, approval, auth) = await CreateAsync();
        var admin = await AddUserAsync(context, auth, "admin", RoleType.Admin);

        var result = await approval.VerifyAsync("admin", "wrong-password", admin.Id);

        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task WhenAnotherAdminExists_SelfApprovalIsRejected_AndOtherAdminApproves()
    {
        var (context, approval, auth) = await CreateAsync();
        var admin = await AddUserAsync(context, auth, "admin", RoleType.Admin);
        await AddUserAsync(context, auth, "owner2", RoleType.Admin);

        (await approval.VerifyAsync("admin", Password, admin.Id)).IsApproved.Should().BeFalse();
        (await approval.VerifyAsync("owner2", Password, admin.Id)).IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task ApproverWithoutUnlockPermission_IsRejected()
    {
        var (context, approval, auth) = await CreateAsync();
        var admin = await AddUserAsync(context, auth, "admin", RoleType.Admin);
        await AddUserAsync(context, auth, "manager", RoleType.Manager);

        var result = await approval.VerifyAsync("manager", Password, admin.Id);

        result.IsApproved.Should().BeFalse();
    }
}
