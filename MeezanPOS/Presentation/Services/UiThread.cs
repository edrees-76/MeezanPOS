using System;

namespace MeezanPOS.Presentation.Services;

/// <summary>
/// تنفيذ تحديثات الواجهة على خيطها. بلا تطبيق WPF (الاختبارات) يُنفذ مباشرة،
/// بدل NullReferenceException من Application.Current الذي كانت تبتلعه كتل catch صامتة.
/// </summary>
public static class UiThread
{
    public static void Run(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}
