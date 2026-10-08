using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Application.Services;

public interface IUserManagementService
{
    Task<List<User>> GetUsersAsync();
    Task ChangeOwnPasswordAsync(string currentPassword, string newPassword);
    Task<User> CreateUserAsync(string username, string fullName, string temporaryPassword, RoleType role);
    Task ResetPasswordAsync(int userId, string temporaryPassword);
    Task SetActiveAsync(int userId, bool isActive);
    Task ChangeRoleAsync(int userId, RoleType role);
}

/// <summary>
/// إدارة المستخدمين وكلمات المرور. كل عمليات الإدارة تتطلب صلاحية ManageUsers وتُسجَّل في سجل التدقيق.
/// </summary>
public class UserManagementService : IUserManagementService
{
    public const int MinPasswordLength = 6;

    private readonly AppDbContext _context;
    private readonly ISessionService _session;
    private readonly IAuthenticationService _auth;
    private readonly AuditService _audit;

    public UserManagementService(AppDbContext context, ISessionService session, IAuthenticationService auth, AuditService audit)
    {
        _context = context;
        _session = session;
        _auth = auth;
        _audit = audit;
    }

    /// <summary>قواعد كلمة المرور. يعيد رسالة الخطأ أو null إذا كانت مقبولة.</summary>
    public static string? ValidatePassword(string? password, string username)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
            return $"كلمة المرور يجب ألا تقل عن {MinPasswordLength} أحرف.";
        if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
            return "كلمة المرور يجب ألا تطابق اسم المستخدم.";
        if (password.Distinct().Count() < 3)
            return "كلمة المرور ضعيفة جداً (أحرف مكررة).";
        return null;
    }

    public static string RoleDisplayName(RoleType role) => role switch
    {
        RoleType.Admin => "مدير عام",
        RoleType.Manager => "مدير",
        RoleType.Cashier => "كاشير",
        _ => role.ToString()
    };

    public Task<List<User>> GetUsersAsync()
    {
        _session.RequirePermission(Permissions.ManageUsers);
        return _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => !u.IsDeleted)
            .OrderBy(u => u.Username)
            .ToListAsync();
    }

    public async Task ChangeOwnPasswordAsync(string currentPassword, string newPassword)
    {
        var current = _session.CurrentUser ?? throw new InvalidOperationException("لا يوجد مستخدم مسجل الدخول.");
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == current.Id)
                   ?? throw new InvalidOperationException("المستخدم غير موجود.");

        if (!BCrypt.Net.BCrypt.Verify(currentPassword ?? string.Empty, user.PasswordHash))
            throw new InvalidOperationException("كلمة المرور الحالية غير صحيحة.");

        var error = ValidatePassword(newPassword, user.Username);
        if (error != null) throw new InvalidOperationException(error);
        if (BCrypt.Net.BCrypt.Verify(newPassword, user.PasswordHash))
            throw new InvalidOperationException("كلمة المرور الجديدة يجب أن تختلف عن الحالية.");

        user.PasswordHash = _auth.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        current.MustChangePassword = false;
        await _audit.LogAsync(user.Id.ToString(), "ChangePassword", nameof(User), user.Id, null, "Changed by owner");
    }

    public async Task<User> CreateUserAsync(string username, string fullName, string temporaryPassword, RoleType role)
    {
        _session.RequirePermission(Permissions.ManageUsers);

        username = (username ?? string.Empty).Trim();
        if (username.Length < 3)
            throw new InvalidOperationException("اسم المستخدم يجب ألا يقل عن 3 أحرف.");
        if (await _context.Users.AnyAsync(u => u.Username == username && !u.IsDeleted))
            throw new InvalidOperationException("اسم المستخدم مستخدم مسبقاً.");

        var error = ValidatePassword(temporaryPassword, username);
        if (error != null) throw new InvalidOperationException(error);

        var roleEntity = await EnsureRoleAsync(role);
        var user = new User
        {
            Username = username,
            FullName = string.IsNullOrWhiteSpace(fullName) ? username : fullName.Trim(),
            PasswordHash = _auth.HashPassword(temporaryPassword),
            RoleId = roleEntity.Id,
            IsActive = true,
            MustChangePassword = true // كلمة مؤقتة يحددها المدير؛ يغيرها صاحبها عند أول دخول
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _audit.LogAsync(_session.CurrentUserId, "CreateUser", nameof(User), user.Id, null, $"{username} ({role})");
        return user;
    }

    public async Task ResetPasswordAsync(int userId, string temporaryPassword)
    {
        _session.RequirePermission(Permissions.ManageUsers);
        var user = await GetUserForUpdateAsync(userId);

        var error = ValidatePassword(temporaryPassword, user.Username);
        if (error != null) throw new InvalidOperationException(error);

        user.PasswordHash = _auth.HashPassword(temporaryPassword);
        user.MustChangePassword = true;
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _audit.LogAsync(_session.CurrentUserId, "ResetPassword", nameof(User), user.Id, null, user.Username);
    }

    public async Task SetActiveAsync(int userId, bool isActive)
    {
        _session.RequirePermission(Permissions.ManageUsers);
        var user = await GetUserForUpdateAsync(userId);

        if (!isActive)
        {
            if (user.Id == _session.CurrentUser?.Id)
                throw new InvalidOperationException("لا يمكنك إيقاف حسابك الحالي.");
            await EnsureAnotherActiveAdminAsync(user);
        }

        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _audit.LogAsync(_session.CurrentUserId, isActive ? "EnableUser" : "DisableUser", nameof(User), user.Id, null, user.Username);
    }

    public async Task ChangeRoleAsync(int userId, RoleType role)
    {
        _session.RequirePermission(Permissions.ManageUsers);
        var user = await GetUserForUpdateAsync(userId);
        var oldRole = user.Role?.Type;
        if (oldRole == role) return;

        if (oldRole == RoleType.Admin)
        {
            if (user.Id == _session.CurrentUser?.Id)
                throw new InvalidOperationException("لا يمكنك تخفيض صلاحيات حسابك الحالي.");
            await EnsureAnotherActiveAdminAsync(user);
        }

        var roleEntity = await EnsureRoleAsync(role);
        user.RoleId = roleEntity.Id;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _audit.LogAsync(_session.CurrentUserId, "ChangeRole", nameof(User), user.Id, oldRole?.ToString(), role.ToString());
    }

    private async Task<User> GetUserForUpdateAsync(int userId)
        => await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted)
           ?? throw new InvalidOperationException("المستخدم غير موجود.");

    /// <summary>منع ترك المنظومة بلا مدير عام فعّال.</summary>
    private async Task EnsureAnotherActiveAdminAsync(User user)
    {
        if (user.Role?.Type != RoleType.Admin) return;
        var otherAdmins = await _context.Users.CountAsync(u =>
            u.Id != user.Id && u.IsActive && !u.IsDeleted && u.Role != null && u.Role.Type == RoleType.Admin);
        if (otherAdmins == 0)
            throw new InvalidOperationException("يجب أن يبقى مدير عام واحد فعّال على الأقل.");
    }

    private async Task<Role> EnsureRoleAsync(RoleType type)
    {
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Type == type);
        if (role != null) return role;

        role = new Role { Type = type, Name = RoleDisplayName(type) };
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();
        return role;
    }
}
