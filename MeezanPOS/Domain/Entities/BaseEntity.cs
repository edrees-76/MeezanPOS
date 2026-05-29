using System;

namespace MeezanPOS.Domain.Entities;

public abstract class BaseEntity
{
    public int Id { get; set; }

    /// <summary>
    /// التوقيت العالمي (UTC) لإنشاء السجل.
    /// ملاحظة ترحيل: البيانات القديمة (قبل v2 refactor) مخزنة بالتوقيت المحلي — لا تُعدَّل.
    /// عند العرض في الـ UI يجب التحويل إلى التوقيت المحلي: .ToLocalTime()
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}
