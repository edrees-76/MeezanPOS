using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Application.Services;

/// <summary>
/// قفل الفترات المسواة حسب التاريخ.
/// أي تاريخ يقع داخل فترة تسوية حالتها "مسواة" أو "معاد تسويتها" يعتبر مقفلاً:
/// لا يُنشأ فيه ولا يُعدَّل ولا يُحذف ولا يُفك ترحيل أي يومية أو مصروف حتى يُلغى قفل الفترة.
/// (سابقاً كان القفل يعتمد على PostingSessionId فقط، فالسجلات المرحلة فردياً داخل الفترة
/// أو المسودات الجديدة بتاريخ داخلها كانت تتجاوز القفل.)
/// </summary>
public static class PeriodLock
{
    public const string LockedMessage =
        "هذا التاريخ يقع ضمن فترة مقفلة ومسواة مالياً. يجب إلغاء قفل الفترة أولاً من شاشة المبيعات (أرشيف التسويات).";

    public static Task<bool> IsDateLockedAsync(AppDbContext context, DateTime date)
    {
        var day = date.Date;
        return context.PostingSessions.AnyAsync(s =>
            s.SessionType == PostingSessionType.Settlement &&
            (s.Status == PostingSessionStatus.Settled || s.Status == PostingSessionStatus.ReSettled) &&
            s.PeriodStartDate.Date <= day &&
            s.PeriodEndDate.Date >= day);
    }

    /// <exception cref="InvalidOperationException">إذا كان التاريخ داخل فترة مقفلة.</exception>
    public static async Task EnsureDateOpenAsync(AppDbContext context, DateTime date)
    {
        if (await IsDateLockedAsync(context, date))
            throw new InvalidOperationException(LockedMessage);
    }
}
