using System.Runtime.CompilerServices;

namespace MeezanPOS.Infrastructure.Reports;

/// <summary>
/// ضبط ترخيص QuestPDF مرة واحدة عند تحميل التجميعة (يشمل التطبيق والاختبارات).
/// </summary>
/// <remarks>
/// ترخيص Community مجاني للمنشآت التي يقل دخلها السنوي عن مليون دولار. إذا تجاوزه المطعم
/// يجب شراء ترخيص Professional وتغيير القيمة هنا فقط.
/// </remarks>
internal static class PdfLicensing
{
    [ModuleInitializer]
    internal static void Configure()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }
}
