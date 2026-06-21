using System;
using System.IO;
using PdfSharp.Fonts;

namespace MeezanPOS.Infrastructure.Reports;

public class AppFontResolver : IFontResolver
{
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        // Normalize family name
        string name = familyName.ToLowerInvariant();

        if (name.Contains("segoe"))
        {
            if (bold)
                return new FontResolverInfo("SegoeUI#Bold");
            return new FontResolverInfo("SegoeUI#Regular");
        }
        else if (name.Contains("tahoma"))
        {
            if (bold)
                return new FontResolverInfo("Tahoma#Bold");
            return new FontResolverInfo("Tahoma#Regular");
        }
        else
        {
            // Default/Fallback is Arial for maximum compatibility with Arabic presentation forms
            if (bold)
                return new FontResolverInfo("Arial#Bold");
            return new FontResolverInfo("Arial#Regular");
        }
    }

    public byte[]? GetFont(string faceName)
    {
        try
        {
            string systemFontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

            string fontFileName = faceName switch
            {
                "SegoeUI#Regular" => "segoeui.ttf",
                "SegoeUI#Bold" => "segoeuib.ttf",
                "Tahoma#Regular" => "tahoma.ttf",
                "Tahoma#Bold" => "tahomabd.ttf",
                "Arial#Regular" => "arial.ttf",
                "Arial#Bold" => "arialbd.ttf",
                _ => "arial.ttf"
            };

            string fontPath = Path.Combine(systemFontsDir, fontFileName);

            if (File.Exists(fontPath))
            {
                return File.ReadAllBytes(fontPath);
            }

            // Safe fallback if specific font file does not exist
            string fallbackPath = Path.Combine(systemFontsDir, "arial.ttf");
            if (File.Exists(fallbackPath))
            {
                return File.ReadAllBytes(fallbackPath);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error reading font file in AppFontResolver for face: {FaceName}", faceName);
        }

        return null;
    }
}
