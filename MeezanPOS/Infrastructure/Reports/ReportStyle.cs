using System;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MeezanPOS.Infrastructure.Reports;

/// <summary>
/// عناصر مشتركة لتقارير لوحة التحكم والحساب الختامي: الألوان، الترويسة بالشعار، البطاقات، الجداول والتذييل.
/// </summary>
internal static class ReportStyle
{
    public const string Page = "#F8F9FF";
    public const string Ink = "#0B1C30";
    public const string Muted = "#76777D";
    public const string Line = "#E5EEFF";
    public const string Border = "#E2E8F0";
    public const string HeaderBg = "#F1F5F9";
    public const string HeaderText = "#475569";
    public const string Slate = "#0F172A";
    public const string SlateMuted = "#64748B";
    public const string Green = "#15803D";
    public const string Red = "#B91C1C";
    public const string Blue = "#1D4ED8";
    public const string Amber = "#B45309";
    public const string Orange = "#D97706";

    public static string Money(decimal value) => $"{value:N2} د.ل";

    /// <summary>إعداد الصفحة: A4، خلفية فاتحة، اتجاه من اليمين، وتذييل بترقيم الصفحات.</summary>
    public static void SetupPage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(40);
        page.PageColor(Page);
        page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial).FontSize(9).FontColor(Ink));
        page.ContentFromRightToLeft();
        page.Footer().Element(Footer);
    }

    private static void Footer(IContainer container)
    {
        container.BorderTop(1).BorderColor(Line).PaddingTop(5).Row(row =>
        {
            row.RelativeItem().AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                t.Span("صفحة ");
                t.CurrentPageNumber();
                t.Span(" من ");
                t.TotalPages();
            });
            row.RelativeItem().AlignLeft().Text(MeezanPOS.Infrastructure.Data.RestaurantContext.ReportFooter).FontSize(8).FontColor(Muted);
        });
    }

    /// <summary>الترويسة: الشعار، العنوان، سطر فرعي، وتاريخ ووقت التصدير.</summary>
    public static void Header(IContainer container, string title, string subtitle, DateTime exportTime, string continuationTitle)
    {
        container.Column(outer =>
        {
        outer.Item().ShowOnce().Column(col =>
        {
            col.Item().Row(row =>
            {
                var logo = LoadLogo();
                if (logo != null)
                    row.ConstantItem(45).Height(45).Image(logo).FitArea();
                else
                    row.ConstantItem(45).Height(45).Background(Slate).AlignCenter().AlignMiddle()
                        .Text("ميزان").FontSize(10).Bold().FontColor(Colors.White);

                row.ConstantItem(15);
                row.RelativeItem().Column(c =>
                {
                    if (!string.IsNullOrEmpty(MeezanPOS.Infrastructure.Data.RestaurantContext.DisplayName))
                        c.Item().PaddingBottom(2).Text(MeezanPOS.Infrastructure.Data.RestaurantContext.DisplayName).FontSize(10).SemiBold().FontColor(Muted);
                    c.Item().Text(title).FontSize(15).Bold();
                    c.Item().PaddingTop(4).Text(subtitle).FontSize(9.5f).FontColor(Muted);
                });
                row.ConstantItem(150).AlignLeft().Column(c =>
                {
                    c.Item().AlignLeft().Text($"تاريخ التصدير: {exportTime:yyyy-MM-dd}").FontSize(8).FontColor(Muted);
                    c.Item().AlignLeft().Text($"وقت التصدير: {exportTime:HH:mm:ss}").FontSize(8).FontColor(Muted);
                });
            });
            col.Item().PaddingTop(12).LineHorizontal(1.5f).LineColor(Line);
            col.Item().Height(12);
        });

        // في الصفحات التالية: سطر "تابع" بدل الترويسة الكاملة
        outer.Item().SkipOnce().Column(col =>
        {
            col.Item().Text(continuationTitle).FontSize(10).Bold();
            col.Item().PaddingTop(5).LineHorizontal(1).LineColor(Line);
            col.Item().Height(10);
        });
        });
    }

    /// <summary>بطاقة بيضاء بحدود وزوايا خفيفة.</summary>
    public static IContainer Card(IContainer container, string? background = null)
        => container.Background(background ?? Colors.White).Border(1).BorderColor(Line).Padding(8);

    public static IContainer HeaderCell(IContainer container)
        => container.Background(HeaderBg).Border(1).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(4);

    public static IContainer BodyCell(IContainer container)
        => container.Border(1).BorderColor(Border).Background(Colors.White).PaddingVertical(4).PaddingHorizontal(4);

    public static void SectionTitle(IContainer container, string text)
        => container.PaddingBottom(6).Text(text).FontSize(10).Bold();

    private static byte[]? LoadLogo()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] paths =
            {
                Path.Combine(baseDir, "Assets", "Images", "mizan_3d_icon_final_transparent.png"),
                Path.Combine(baseDir, "mizan_3d_icon_final_transparent.png"),
                Path.Combine(Directory.GetParent(baseDir)?.Parent?.Parent?.Parent?.FullName ?? "", "mizan_3d_icon_final_transparent.png"),
            };
            foreach (var path in paths)
                if (File.Exists(path))
                    return File.ReadAllBytes(path);
        }
        catch { /* الشعار اختياري */ }
        return null;
    }
}
