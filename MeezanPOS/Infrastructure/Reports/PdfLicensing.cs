using System;
using System.Runtime.CompilerServices;
using QuestPDF.Infrastructure;

namespace MeezanPOS.Infrastructure.Reports;

/// <summary>
/// نوع ترخيص مكتبة التقارير QuestPDF.
/// </summary>
/// <remarks>
/// Community مجاني للمنشآت التي يقل دخلها السنوي الإجمالي عن مليون دولار. إذا تجاوزه المطعم
/// يُشترى ترخيص Professional أو Enterprise ويُختار من الإعدادات (يُحفظ في جدول Settings بالمفتاح
/// <see cref="SettingKey"/> ويُطبّق عند بدء التشغيل) دون أي تعديل في الكود.
/// </remarks>
public static class PdfLicensing
{
    public const string SettingKey = "QuestPdfLicense";

    public static LicenseType Current { get; private set; } = LicenseType.Community;

    /// <summary>الافتراضي عند تحميل التجميعة (يشمل الاختبارات) قبل قراءة الإعداد.</summary>
    [ModuleInitializer]
    internal static void Configure() => Apply(LicenseType.Community);

    public static void Apply(LicenseType license)
    {
        Current = license;
        QuestPDF.Settings.License = license;
    }

    /// <summary>تطبيق القيمة المحفوظة في الإعدادات؛ القيم غير المعروفة تعود إلى Community.</summary>
    public static LicenseType Apply(string? storedValue)
    {
        var license = Enum.TryParse<LicenseType>(storedValue, ignoreCase: true, out var parsed) ? parsed : LicenseType.Community;
        Apply(license);
        return license;
    }
}
