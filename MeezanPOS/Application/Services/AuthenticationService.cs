using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Infrastructure.Data;
using Serilog;

namespace MeezanPOS.Application.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly AppDbContext _context;

    public AuthenticationService(AppDbContext context) => _context = context;

    public async Task<User?> AuthenticateAsync(string username, string password)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username == username && !u.IsDeleted);

        if (user == null)
        {
            Log.Warning("محاولة دخول فاشلة: المستخدم {Username} غير موجود", username);
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
            if (user.FailedLoginAttempts >= 5)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(5);
                Log.Warning("تم قفل المستخدم {Username} بعد 5 محاولات فاشلة", username);
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
}
