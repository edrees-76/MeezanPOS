using System.IO;
using System.Runtime.CompilerServices;

namespace MeezanPOS.Tests.Helpers;

/// <summary>
/// يوجّه كل الاختبارات إلى مجلد بيانات مؤقت قبل تشغيل أي اختبار،
/// حتى لا تقرأ أو تحذف اختبارات التكامل قاعدة بيانات المستخدم الحقيقية في LocalAppData.
/// </summary>
internal static class TestDatabaseIsolation
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MeezanPOS.Tests", Environment.ProcessId.ToString());
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("MEEZANPOS_DATA_DIR", dir);
    }
}
