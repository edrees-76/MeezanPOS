namespace MeezanPOS.Infrastructure.Data;

/// <summary>
/// المستخدم الحالي لأغراض سجل التدقيق التلقائي داخل AppDbContext.
/// يُضبط من SessionService عند الدخول والخروج (تطبيق سطح مكتب بمستخدم واحد في كل مرة).
/// </summary>
public static class AuditContext
{
    public static int? CurrentUserId { get; set; }
}
