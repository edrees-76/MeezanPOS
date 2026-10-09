using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Infrastructure.Data;
using Serilog;

namespace MeezanPOS.Application.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly AppDbContext _context;

    public const int MaxAttemptsBeforeLockout = 5;

    public AuthenticationService(AppDbContext context) => _context = context;

    public async Task<User?> AuthenticateAsync(string username, string password)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username == username && !u.IsDeleted && u.IsActive);

        if (user == null)
        {
            Log.Warning("محاولة دخول فاشلة: اسم مستخدم غير معروف (الطول {Length})", username?.Length ?? 0);
            return null;
        }

        // التحقق من الإقفال
        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
        {
            Log.Warning("المستخدم {Username} مقفل حتى {LockoutEnd}", username, user.LockoutEnd);
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxAttemptsBeforeLockout)
            {
                // قفل متصاعد: 5 دقائق، ثم يتضاعف مع كل محاولة فاشلة إضافية (بحد أقصى 24 ساعة).
                // العداد لا يُصفَّر بانتهاء القفل، بل عند الدخول الناجح فقط.
                var extra = Math.Min(user.FailedLoginAttempts - MaxAttemptsBeforeLockout, 9);
                var minutes = Math.Min(5 * Math.Pow(2, extra), 24 * 60);
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(minutes);
                Log.Warning("تم قفل المستخدم {Username} لمدة {Minutes} دقيقة بعد {Attempts} محاولات فاشلة",
                    username, minutes, user.FailedLoginAttempts);
                AddEvent(user.Id, ActivityLabels.LockedOut, $"محاولات فاشلة: {user.FailedLoginAttempts} | مدة القفل: {minutes:0} دقيقة");
            }
            else
            {
                AddEvent(user.Id, ActivityLabels.LoginFailed, $"محاولات فاشلة متتالية: {user.FailedLoginAttempts}");
            }
            await _context.SaveChangesAsync();
            return null;
        }

        // نجاح — إعادة تعيين المحاولات
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        Log.Information("دخول ناجح: {Username} ({Role})", username, user.Role?.Name);
        return user;
    }

    public string HashPassword(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public async Task RecordSessionEventAsync(int userId, string action)
    {
        AddEvent(userId, action, string.Empty);
        await _context.SaveChangesAsync();
    }

    private void AddEvent(int userId, string action, string details)
        => _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityName = nameof(User),
            EntityId = userId,
            Changes = details,
            CreatedAt = DateTime.UtcNow
        });
}
