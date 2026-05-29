using System;
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
    public async Task LogAsync(string username, string operation, string entityType, int entityId, string? before, string? after)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        int userId = user?.Id ?? 1; // الافتراضي هو 1 (المشرف) إذا لم يتم العثور على المستخدم

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
}
