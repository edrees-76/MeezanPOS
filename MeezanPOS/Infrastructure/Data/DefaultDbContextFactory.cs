using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Infrastructure.Data;

/// <summary>
/// مصنع سياقات بالإعداد الافتراضي (مسار قاعدة البيانات من <see cref="AppDbContext.GetDatabasePath"/>).
/// تستخدمه خدمات الاستعلام عند إنشائها خارج حاوية الخدمات (مثل الاختبارات أو الشاشات القديمة).
/// </summary>
public sealed class DefaultDbContextFactory : IDbContextFactory<AppDbContext>
{
    public static readonly DefaultDbContextFactory Instance = new();

    public AppDbContext CreateDbContext() => new();
}
