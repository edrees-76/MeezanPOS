using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using HarmonyLib;

namespace MeezanPOS.Tests.Helpers;

public static class MessageBoxMock
{
    private static Harmony? _harmony;
    public static string? LastMessage { get; set; }
    public static MessageBoxResult ResultToReturn { get; set; } = MessageBoxResult.Yes;
    public static int CallCount { get; set; }

    private static void EnsureDatabaseCreated()
    {
        try
        {
            using var db = new MeezanPOS.Infrastructure.Data.AppDbContext();
            db.Database.EnsureCreated();
        }
        catch { }
    }

    public static void Initialize()
    {
        EnsureDatabaseCreated();
        MeezanPOS.Presentation.Services.Dialogs.Current = new FakeDialogService();
        if (MeezanPOS.Presentation.Services.AppWindows.Current is not FakeWindowService)
            MeezanPOS.Presentation.Services.AppWindows.Current = new FakeWindowService();
        if (_harmony != null) return;
        _harmony = new Harmony("com.meezanpos.tests.messageboxmock");

        // Overload 1: Show(string, string, MessageBoxButton, MessageBoxImage)
        var method1 = typeof(MessageBox).GetMethod("Show", new[] { typeof(string), typeof(string), typeof(MessageBoxButton), typeof(MessageBoxImage) });
        var prefix1 = typeof(MessageBoxMock).GetMethod(nameof(Prefix1), BindingFlags.NonPublic | BindingFlags.Static);
        if (method1 != null && prefix1 != null)
        {
            _harmony.Patch(method1, prefix: new HarmonyMethod(prefix1));
        }

        // Overload 2: Show(string, string, MessageBoxButton)
        var method2 = typeof(MessageBox).GetMethod("Show", new[] { typeof(string), typeof(string), typeof(MessageBoxButton) });
        var prefix2 = typeof(MessageBoxMock).GetMethod(nameof(Prefix2), BindingFlags.NonPublic | BindingFlags.Static);
        if (method2 != null && prefix2 != null)
        {
            _harmony.Patch(method2, prefix: new HarmonyMethod(prefix2));
        }

        // Overload 3: Show(string)
        var method3 = typeof(MessageBox).GetMethod("Show", new[] { typeof(string) });
        var prefix3 = typeof(MessageBoxMock).GetMethod(nameof(Prefix3), BindingFlags.NonPublic | BindingFlags.Static);
        if (method3 != null && prefix3 != null)
        {
            _harmony.Patch(method3, prefix: new HarmonyMethod(prefix3));
        }

        // Patch Process.Start(ProcessStartInfo) to prevent PDF reader launch in tests
        var processStartMethod = typeof(System.Diagnostics.Process).GetMethod("Start", new[] { typeof(System.Diagnostics.ProcessStartInfo) });
        var processStartPrefix = typeof(MessageBoxMock).GetMethod(nameof(ProcessStartPrefix), BindingFlags.NonPublic | BindingFlags.Static);
        if (processStartMethod != null && processStartPrefix != null)
        {
            _harmony.Patch(processStartMethod, prefix: new HarmonyMethod(processStartPrefix));
        }

        // Patch Dispatcher.Invoke(Action) to run synchronously in tests
        var dispatcherInvokeMethod = typeof(System.Windows.Threading.Dispatcher).GetMethod("Invoke", new[] { typeof(Action) });
        var dispatcherInvokePrefix = typeof(MessageBoxMock).GetMethod(nameof(DispatcherInvokePrefix), BindingFlags.NonPublic | BindingFlags.Static);
        if (dispatcherInvokeMethod != null && dispatcherInvokePrefix != null)
        {
            _harmony.Patch(dispatcherInvokeMethod, prefix: new HarmonyMethod(dispatcherInvokePrefix));
        }

        // Patch Dispatcher.InvokeAsync(Action) to run synchronously in tests
        var dispatcherInvokeAsyncMethod = typeof(System.Windows.Threading.Dispatcher).GetMethod("InvokeAsync", new[] { typeof(Action) });
        var dispatcherInvokeAsyncPrefix = typeof(MessageBoxMock).GetMethod(nameof(DispatcherInvokeAsyncPrefix), BindingFlags.NonPublic | BindingFlags.Static);
        if (dispatcherInvokeAsyncMethod != null && dispatcherInvokeAsyncPrefix != null)
        {
            _harmony.Patch(dispatcherInvokeAsyncMethod, prefix: new HarmonyMethod(dispatcherInvokeAsyncPrefix));
        }

        // Patch Dispatcher.InvokeAsync<Task>(Func<Task>) to run synchronously in tests
        var dispatcherInvokeAsyncGenericMethod = typeof(System.Windows.Threading.Dispatcher)
            .GetMethods()
            .FirstOrDefault(m => m.Name == "InvokeAsync" && m.IsGenericMethod && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(Func<>));
        if (dispatcherInvokeAsyncGenericMethod != null)
        {
            var targetGeneric = dispatcherInvokeAsyncGenericMethod.MakeGenericMethod(typeof(Task));
            var dispatcherInvokeAsyncGenericPrefix = typeof(MessageBoxMock).GetMethod(nameof(DispatcherInvokeAsyncGenericPrefix), BindingFlags.NonPublic | BindingFlags.Static);
            if (targetGeneric != null && dispatcherInvokeAsyncGenericPrefix != null)
            {
                _harmony.Patch(targetGeneric, prefix: new HarmonyMethod(dispatcherInvokeAsyncGenericPrefix));
            }
        }

        // Patch AppDbContext.GetDatabasePath() to redirect connection string in tests
        var dbPathMethod = typeof(MeezanPOS.Infrastructure.Data.AppDbContext).GetMethod("GetDatabasePath", BindingFlags.Public | BindingFlags.Static);
        var dbPathPrefix = typeof(MessageBoxMock).GetMethod(nameof(GetDatabasePathPrefix), BindingFlags.NonPublic | BindingFlags.Static);
        if (dbPathMethod != null && dbPathPrefix != null)
        {
            _harmony.Patch(dbPathMethod, prefix: new HarmonyMethod(dbPathPrefix));
        }
    }

    private static bool GetDatabasePathPrefix(ref string __result)
    {
        var tempDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestDb");
        System.IO.Directory.CreateDirectory(tempDir);
        __result = System.IO.Path.Combine(tempDir, "Meezan_Test.db");
        return false; // Skip original
    }

    public static void Reset()
    {
        EnsureDatabaseCreated();
        LastMessage = null;
        ResultToReturn = MessageBoxResult.Yes;
        CallCount = 0;
    }


    private static bool ProcessStartPrefix(System.Diagnostics.ProcessStartInfo startInfo, ref System.Diagnostics.Process? __result)
    {
        __result = new System.Diagnostics.Process();
        return false; // Skip original
    }

    private static bool DispatcherInvokePrefix(Action callback)
    {
        callback();
        return false; // Skip original
    }

    private static bool DispatcherInvokeAsyncPrefix(Action callback, ref object? __result)
    {
        callback();
        __result = null;
        return false; // Skip original
    }

    private static bool DispatcherInvokeAsyncGenericPrefix(Func<Task> callback, ref object? __result)
    {
        callback().GetAwaiter().GetResult();
        __result = null;
        return false; // Skip original
    }

    private static bool Prefix1(string messageBoxText, ref MessageBoxResult __result)
    {
        LastMessage = messageBoxText;
        CallCount++;
        __result = ResultToReturn;
        return false; // Skip original
    }

    private static bool Prefix2(string messageBoxText, ref MessageBoxResult __result)
    {
        LastMessage = messageBoxText;
        CallCount++;
        __result = ResultToReturn;
        return false; // Skip original
    }

    private static bool Prefix3(string messageBoxText, ref MessageBoxResult __result)
    {
        LastMessage = messageBoxText;
        CallCount++;
        __result = ResultToReturn;
        return false; // Skip original
    }
}

/// <summary>
/// بديل خدمة الرسائل في الاختبارات: يسجل آخر رسالة ويعيد النتيجة المضبوطة في MessageBoxMock.
/// </summary>
internal sealed class FakeDialogService : MeezanPOS.Presentation.Services.IDialogService
{
    public MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
    {
        MessageBoxMock.LastMessage = message;
        MessageBoxMock.CallCount++;
        return MessageBoxMock.ResultToReturn;
    }
}
