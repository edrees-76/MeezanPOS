using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Application.Services;

/// <summary>
/// خدمة التدقيق لتسجيل العمليات المالية الحرجة والتغييرات على الكيانات.
/// </summary>
public class AuditService
{
    private readonly AppDbContext _context;

    public AuditService(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// تسجيل عملية تدقيق جديدة في قاعدة البيانات.
    /// </summary>
    /// <param name="userIdOrName">
    /// رقم المستخدم (كما يمرره ISessionService.CurrentUserId) أو اسم الدخول.
    /// سابقاً كان البحث بالاسم فقط بينما معظم المستدعين يمررون الرقم، فتُنسب كل العمليات للمستخدم 1.
    /// </param>
    public async Task LogAsync(string userIdOrName, string operation, string entityType, int entityId, string? before, string? after)
    {
        int userId = await ResolveUserIdAsync(userIdOrName);

        var auditLog = new AuditLog
        {
            UserId = userId,
            Action = operation,
            EntityName = entityType,
            EntityId = entityId,
            Changes = $"Before: {before ?? "None"} | After: {after ?? "None"}",
            CreatedAt = DateTime.UtcNow
        };

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync();
    }

    private async Task<int> ResolveUserIdAsync(string userIdOrName)
    {
        if (int.TryParse(userIdOrName, out var id) && await _context.Users.AnyAsync(u => u.Id == id))
            return id;

        var byName = await _context.Users
            .Where(u => u.Username == userIdOrName)
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync();
        if (byName.HasValue)
            return byName.Value;

        // المستخدم المسجل حالياً، ثم 1 كحل أخير
        return AuditContext.CurrentUserId ?? 1;
    }
}
