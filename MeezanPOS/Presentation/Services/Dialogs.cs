using System;
using System.Linq;
using System.Windows;

namespace MeezanPOS.Presentation.Services;

/// <summary>
/// واجهة رسائل التنبيه والتأكيد. تفصل منطق الشاشات عن MessageBox ليمكن استبدالها في الاختبارات.
/// </summary>
public interface IDialogService
{
    MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult);
}

/// <summary>
/// التنفيذ الفعلي: رسالة عربية من اليمين لليسار، مملوكة للنافذة النشطة، وتُعرض على خيط الواجهة
/// حتى لو استُدعيت من خيط خلفي.
/// </summary>
public sealed class MessageBoxDialogService : IDialogService
{
    private const MessageBoxOptions RtlOptions = MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign;

    public MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
    {
        var app = System.Windows.Application.Current;
        if (app == null)
            return MessageBox.Show(message, title, buttons, image, defaultResult, RtlOptions);

        if (!app.Dispatcher.CheckAccess())
            return app.Dispatcher.Invoke(() => Show(message, title, buttons, image, defaultResult));

        var owner = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
        return owner != null
            ? MessageBox.Show(owner, message, title, buttons, image, defaultResult, RtlOptions)
            : MessageBox.Show(message, title, buttons, image, defaultResult, RtlOptions);
    }
}

/// <summary>
/// نقطة الدخول الموحدة لكل رسائل البرنامج، بنفس توقيعات MessageBox.Show.
/// </summary>
public static class Dialogs
{
    public static IDialogService Current { get; set; } = new MessageBoxDialogService();

    public static MessageBoxResult Show(string message)
        => Current.Show(message, string.Empty, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(string message, string title)
        => Current.Show(message, title, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons)
        => Current.Show(message, title, buttons, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        => Current.Show(message, title, buttons, image, MessageBoxResult.None);

    public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
        => Current.Show(message, title, buttons, image, defaultResult);

    /// <summary>الخيارات تُتجاهل: الاتجاه من اليمين مفروض على كل الرسائل.</summary>
    public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult, MessageBoxOptions _)
        => Current.Show(message, title, buttons, image, defaultResult);

    /// <summary>المالك يُحدد تلقائياً (النافذة النشطة).</summary>
    public static MessageBoxResult Show(Window? _, string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        => Current.Show(message, title, buttons, image, MessageBoxResult.None);

    /// <summary>سؤال نعم/لا. يعيد true عند الموافقة.</summary>
    public static bool Confirm(string message, string title = "تأكيد", MessageBoxImage image = MessageBoxImage.Question)
        => Current.Show(message, title, MessageBoxButton.YesNo, image, MessageBoxResult.No) == MessageBoxResult.Yes;
}
