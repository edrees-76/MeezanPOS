using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace MeezanPOS.Infrastructure.Data;

/// <summary>
/// معاملة "تنضم" لمعاملة قائمة إن وُجدت بدلاً من فتح معاملة متداخلة
/// (EF Core يرفض BeginTransaction إذا كان الاتصال داخل معاملة بالفعل).
/// إذا كانت المعاملة منضمة فإن Commit/Rollback لا يفعلان شيئاً، ويُترك القرار للمعاملة الخارجية
/// (الاستثناء يصعد إليها فتتراجع عن كل شيء).
/// </summary>
public sealed class ScopedTransaction : IDisposable, IAsyncDisposable
{
    private readonly IDbContextTransaction? _owned;

    internal ScopedTransaction(IDbContextTransaction? owned) => _owned = owned;

    /// <summary>هل هذه المعاملة هي المالكة (الخارجية)؟</summary>
    public bool IsOwner => _owned != null;

    public Task CommitAsync() => _owned?.CommitAsync() ?? Task.CompletedTask;

    public Task RollbackAsync() => _owned?.RollbackAsync() ?? Task.CompletedTask;

    public void Dispose() => _owned?.Dispose();

    public ValueTask DisposeAsync() => _owned?.DisposeAsync() ?? ValueTask.CompletedTask;
}

public static class TransactionExtensions
{
    public static async Task<ScopedTransaction> BeginOrJoinTransactionAsync(this DatabaseFacade database, IsolationLevel? isolationLevel = null)
    {
        if (database.CurrentTransaction != null)
            return new ScopedTransaction(null);

        var tx = isolationLevel.HasValue
            ? await database.BeginTransactionAsync(isolationLevel.Value)
            : await database.BeginTransactionAsync();
        return new ScopedTransaction(tx);
    }
}
