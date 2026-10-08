using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Application.Services;

/// <summary>
/// استعلامات موحدة لحركات الخزينة "السارية".
/// الحركة السارية: لم يتم عكسها، وليست هي نفسها حركة عكسية.
/// الحركة العكسية تحمل نفس SourceType/SourceId للحركة الأصلية، لذا يجب استبعادها
/// عند البحث عن الحركة الأصلية لمصدر معين وإلا قد يتم عكس الحركة الخاطئة.
/// </summary>
public static class CashMovementQueries
{
    /// <summary>بادئة البيان التي تميز الحركات العكسية (تُكتب في CashLedgerService.ReverseMovementAsync).</summary>
    public const string ReversalNotePrefix = "عكس حركة رقم";

    public static IQueryable<CashMovement> WhereLive(this IQueryable<CashMovement> query)
        => query.Where(m => !m.IsReversed && (m.Notes == null || !m.Notes.StartsWith(ReversalNotePrefix)));

    /// <summary>أحدث حركة سارية مرتبطة بمصدر معين، أو null.</summary>
    public static Task<CashMovement?> FindLiveForSourceAsync(this IQueryable<CashMovement> query, string sourceType, int sourceId)
        => query
            .Where(m => m.SourceType == sourceType && m.SourceId == sourceId)
            .WhereLive()
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync();
}
