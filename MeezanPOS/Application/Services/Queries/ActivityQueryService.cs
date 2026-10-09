using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.Services.Queries;

/// <summary>فلتر سجل النشاط.</summary>
public sealed record ActivityFilter(DateTime From, DateTime To, int? UserId = null, ActivityCategory Category = ActivityCategory.All);

/// <summary>سطر في سجل النشاط جاهز للعرض.</summary>
public sealed record ActivityRow(
    int Id, DateTime Time, string UserName, string ActionName, ActivityCategory Category,
    string EntityName, int EntityId, string Details)
{
    public string TimeText => Time.ToString("yyyy/MM/dd  HH:mm");
    public string RecordText => EntityId > 0 ? $"{EntityName} #{EntityId}" : EntityName;
}

public sealed record ActivityUser(int Id, string Name);

/// <summary>قراءة سجل التدقيق لشاشة "المستخدمون والنشاط". السجل نفسه للقراءة فقط.</summary>
public interface IActivityQueryService
{
    Task<List<ActivityRow>> GetActivityAsync(ActivityFilter filter, int maxRows = 5000);
    Task<List<ActivityUser>> GetUsersAsync();
}

public sealed class ActivityQueryService : IActivityQueryService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public ActivityQueryService(IDbContextFactory<AppDbContext>? factory = null)
        => _factory = factory ?? DefaultDbContextFactory.Instance;

    public async Task<List<ActivityRow>> GetActivityAsync(ActivityFilter filter, int maxRows = 5000)
    {
        await using var db = _factory.CreateDbContext();

        // CreatedAt مخزن بالتوقيت العالمي
        var fromUtc = filter.From.Date.ToUniversalTime();
        var toUtc = filter.To.Date.AddDays(1).ToUniversalTime();
        var query = db.AuditLogs.AsNoTracking().Where(a => a.CreatedAt >= fromUtc && a.CreatedAt < toUtc);

        if (filter.UserId.HasValue)
            query = query.Where(a => a.UserId == filter.UserId.Value);
        if (filter.Category != ActivityCategory.All)
        {
            var actions = ActivityLabels.ActionsIn(filter.Category).ToList();
            // العمليات غير المعروفة تُحسب مالية (انظر ActivityLabels.CategoryOf)
            query = filter.Category == ActivityCategory.Financial
                ? query.Where(a => actions.Contains(a.Action) || !AllKnown.Contains(a.Action))
                : query.Where(a => actions.Contains(a.Action));
        }

        var logs = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(maxRows)
            .Select(a => new { a.Id, a.CreatedAt, a.UserId, a.Action, a.EntityName, a.EntityId, a.Changes })
            .ToListAsync();

        var userIds = logs.Select(l => l.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username, u.FullName })
            .ToDictionaryAsync(u => u.Id);

        return logs.Select(l =>
        {
            var name = users.TryGetValue(l.UserId, out var u)
                ? (string.IsNullOrWhiteSpace(u.FullName) ? u.Username : $"{u.FullName} ({u.Username})")
                : $"مستخدم #{l.UserId}";
            return new ActivityRow(
                l.Id,
                DateTime.SpecifyKind(l.CreatedAt, DateTimeKind.Utc).ToLocalTime(),
                name,
                ActivityLabels.ActionName(l.Action),
                ActivityLabels.CategoryOf(l.Action),
                ActivityLabels.EntityName(l.EntityName),
                l.EntityId,
                l.Changes ?? string.Empty);
        }).ToList();
    }

    private static readonly List<string> AllKnown = Enum.GetValues<ActivityCategory>()
        .Where(c => c != ActivityCategory.All)
        .SelectMany(ActivityLabels.ActionsIn)
        .ToList();

    public async Task<List<ActivityUser>> GetUsersAsync()
    {
        await using var db = _factory.CreateDbContext();
        return await db.Users.AsNoTracking().IgnoreQueryFilters()
            .OrderBy(u => u.Username)
            .Select(u => new ActivityUser(u.Id, string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName + " (" + u.Username + ")"))
            .ToListAsync();
    }
}
