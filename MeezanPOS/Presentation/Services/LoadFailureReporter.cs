using System;
using System.Threading;
using System.Windows;

namespace MeezanPOS.Presentation.Services;

/// <summary>
/// تبليغ فشل تحميل بيانات شاشة: يُسجل كل فشل في السجل، ويُظهر للمستخدم رسالة واحدة فقط
/// لكل شاشة (تحميل القوائم المرجعية يفشل عادة معاً، فلا نغرق المستخدم بالرسائل).
/// كانت هذه الأخطاء تُكتب في نافذة التصحيح فقط، فتظهر القوائم فارغة بلا تفسير.
/// </summary>
public sealed class LoadFailureReporter
{
    private int _shown;

    /// <param name="what">ما الذي فشل تحميله، بصيغة تكمل "تعذر تحميل ..."</param>
    public void Report(Exception ex, string what)
    {
        Serilog.Log.Error(ex, "تعذر تحميل {What}", what);
        if (Interlocked.Exchange(ref _shown, 1) == 1) return;

        Dialogs.Show(
            $"تعذر تحميل {what}:\n{ex.Message}\n\nقد تظهر بعض القوائم في هذه الشاشة ناقصة. أعد فتح الشاشة، وإن تكرر الخطأ راجع ملف السجل.",
            "خطأ في التحميل", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
