using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MeezanPOS.Application.Services;

/// <summary>
/// التحقق من وجود إصدار أحدث في صفحة إصدارات المشروع على GitHub.
/// يُبلغ المستخدم فقط ولا ينزّل أو يثبّت شيئاً تلقائياً.
/// </summary>
public static class UpdateChecker
{
    public const string ReleasesPage = "https://github.com/edrees-76/MeezanPOS/releases/latest";
    private const string LatestReleaseApi = "https://api.github.com/repos/edrees-76/MeezanPOS/releases/latest";

    public sealed record UpdateInfo(Version Current, Version Latest, string Tag, string Url)
    {
        public bool IsNewer => Latest > Current;
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>"v1.2.0" أو "1.2" أو "v1.2.0-beta" ← 1.2.0. يعيد null لوسم غير صالح.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim().TrimStart('v', 'V');
        var dash = t.IndexOfAny(new[] { '-', '+', ' ' });
        if (dash >= 0) t = t[..dash];
        if (!Version.TryParse(t.Contains('.') ? t : t + ".0", out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    /// <summary>
    /// يتحقق ويعرض رسالة عند وجود إصدار أحدث. التحقق التلقائي مرة واحدة يومياً وبصمت عند الفشل؛
    /// اليدوي (من الإعدادات) يُبلغ بالنتيجة دائماً.
    /// </summary>
    public static async Task NotifyIfNewerAsync(bool manual)
    {
        var stampPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(MeezanPOS.Infrastructure.Data.AppDbContext.GetDatabasePath())!,
            "update.check");
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (!manual)
        {
            try
            {
                if (System.IO.File.Exists(stampPath) && System.IO.File.ReadAllText(stampPath).Trim() == today) return;
                System.IO.File.WriteAllText(stampPath, today);
            }
            catch { /* التحقق اليومي اختياري */ }
        }

        var info = await CheckAsync();
        if (info == null)
        {
            if (manual)
                Dialogs.Show("تعذر الوصول إلى صفحة الإصدارات أو لا توجد إصدارات منشورة بعد.", "التحديثات", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        if (!info.IsNewer)
        {
            if (manual)
                Dialogs.Show($"أنت تستخدم أحدث إصدار ({info.Current}).", "التحديثات", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var open = Dialogs.Show(
            $"يتوفر إصدار جديد من منظومة ميزان: {info.Latest} (الحالي {info.Current}).\nهل تريد فتح صفحة التنزيل؟",
            "تحديث متوفر", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
        if (open == System.Windows.MessageBoxResult.Yes)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.Url) { UseShellExecute = true });
    }

    /// <summary>يعيد null عند تعذر الاتصال أو عدم وجود إصدارات منشورة.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MeezanPOS-UpdateCheck");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var response = await http.GetAsync(LatestReleaseApi, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            var latest = ParseTag(tag);
            if (latest == null) return null;

            return new UpdateInfo(CurrentVersion, latest, tag!, url ?? ReleasesPage);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Serilog.Log.Information("تعذر التحقق من التحديثات: {Message}", ex.Message);
            return null;
        }
    }
}
