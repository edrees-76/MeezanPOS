using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using FluentAssertions;
using MeezanPOS.Tests.Helpers;
using Xunit;

namespace MeezanPOS.Tests.ViewModels;

/// <summary>
/// يتحقق أن كل شاشة تُحمَّل دون أخطاء XAML (مثل مرجع لون/نمط غير موجود في الثيم).
/// هذه الأخطاء لا يكشفها البناء وتظهر فقط عند فتح الشاشة.
/// </summary>
public class ViewsLoadTests
{
    private static readonly string[] SkipTypes =
    {
        // تتطلب معاملات بناء أو حالة تشغيل لا تتوفر في الاختبار
    };

    /// <summary>
    /// موارد App.xaml (الثيم والأنماط) محمّلة في تطبيق الاختبار. App نفسه لا يمكن إنشاؤه
    /// لأن الاختبارات الأخرى تنشئ Application عاماً، فنقرأ قسم الموارد ونحمله بـ XamlReader.
    /// </summary>
    private static void EnsureAppResources()
    {
        if (System.Windows.Application.Current == null)
            new System.Windows.Application();
        var app = System.Windows.Application.Current!;
        if (app.Resources.Contains("Brush.Primary")) return;

        // XamlReader لا يحمل التجميعات من تلقاء نفسه لمساحات الأسماء بعناوين URL
        Assembly.Load("MaterialDesignThemes.Wpf");
        Assembly.Load("MaterialDesignColors");
        var appXaml = File.ReadAllText(FindAppXaml());
        var start = appXaml.IndexOf("<ResourceDictionary>", StringComparison.Ordinal);
        var end = appXaml.LastIndexOf("</Application.Resources>", StringComparison.Ordinal);
        var dict = appXaml.Substring(start, end - start);

        var header = Regex.Match(appXaml, "<Application (.*?)>", RegexOptions.Singleline).Groups[1].Value;
        header = Regex.Replace(header, "x:Class=\"[^\"]*\"", "");
        header = Regex.Replace(header, "clr-namespace:([\\w.]+)\"", "clr-namespace:$1;assembly=MeezanPOS\"");
        dict = dict.Replace("<ResourceDictionary>", "<ResourceDictionary " + header + ">");
        dict = dict.Replace("Source=\"Presentation/", "Source=\"pack://application:,,,/MeezanPOS;component/Presentation/");

        var resources = (ResourceDictionary)XamlReader.Parse(dict);
        app.Resources.MergedDictionaries.Add(resources);
    }

    private static string FindAppXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MeezanPOS", "App.xaml")))
            dir = dir.Parent;
        dir.Should().NotBeNull("App.xaml must be reachable from the test output folder");
        return Path.Combine(dir!.FullName, "MeezanPOS", "App.xaml");
    }

    public static IEnumerable<object[]> ViewTypes() =>
        typeof(MeezanPOS.App).Assembly.GetTypes()
            .Where(t => t.Namespace == "MeezanPOS.Presentation.Views"
                        && (typeof(System.Windows.Controls.UserControl).IsAssignableFrom(t) || typeof(Window).IsAssignableFrom(t))
                        && !t.IsAbstract
                        && t.GetConstructor(Type.EmptyTypes) != null
                        && !SkipTypes.Contains(t.Name))
            .OrderBy(t => t.Name)
            .Select(t => new object[] { t.Name });

    [StaTheory]
    [MemberData(nameof(ViewTypes))]
    public void View_LoadsWithoutXamlErrors(string typeName)
    {
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();
        EnsureAppResources();
        try { _ = MeezanPOS.Application.Services.AppServiceProvider.Provider; }
        catch (InvalidOperationException) { MeezanPOS.Application.Services.AppServiceProvider.Initialize(); }

        var type = typeof(MeezanPOS.App).Assembly.GetTypes().Single(t => t.Name == typeName && t.Namespace == "MeezanPOS.Presentation.Views");
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(type);
        }
        catch (TargetInvocationException ex)
        {
            // أخطاء XAML فقط هي موضوع هذا الاختبار. أخطاء البيانات (قاعدة الاختبار فارغة) أو الخدمات لا تهم هنا.
            var xamlError = Unwrap(ex).OfType<XamlParseException>().FirstOrDefault();
            xamlError.Should().BeNull($"{typeName} failed to parse XAML: {xamlError?.Message}");
        }
        // النوافذ لا تُعرض ولا تُغلق: الإغلاق يشغّل منطق OnClosing الذي يفترض خيط التطبيق الرئيسي
        GC.KeepAlive(instance);
    }

    /// <summary>
    /// القوالب (DataTemplate/ControlTemplate) تُحمَّل عند الحاجة فقط، فلا يكشف اختبار التحميل مراجعها المكسورة.
    /// هذا الفحص يتأكد أن كل مفتاح StaticResource في الشاشات معرَّف في موارد التطبيق أو في الملف نفسه.
    /// </summary>
    [StaFact]
    public void AllStaticResourceKeys_InViews_Exist()
    {
        MessageBoxMock.Initialize();
        EnsureAppResources();
        var viewsDir = Path.Combine(Path.GetDirectoryName(FindAppXaml())!, "Presentation", "Views");
        var missing = new List<string>();

        foreach (var file in Directory.GetFiles(viewsDir, "*.xaml"))
        {
            var xaml = File.ReadAllText(file);
            var localKeys = Regex.Matches(xaml, "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
            foreach (Match m in Regex.Matches(xaml, @"\{StaticResource\s+([^}\s]+)\s*\}"))
            {
                var key = m.Groups[1].Value;
                if (key.StartsWith("{")) continue; // مفتاح من نوع {x:Type ...}
                if (localKeys.Contains(key)) continue;
                if (System.Windows.Application.Current!.TryFindResource(key) != null) continue;
                missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }

        missing.Distinct().Should().BeEmpty();
    }

    private static IEnumerable<Exception> Unwrap(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            yield return e;
    }
}
