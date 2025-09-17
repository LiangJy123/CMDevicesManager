using CMDevicesManager.Helper;
using System;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using FontFamily = System.Windows.Media.FontFamily;

namespace CMDevicesManager.Language
{
    static public class FontSwitch
    {
        private static bool FontInstalled(string familyName) =>
        Fonts.SystemFontFamilies.Any(f =>
            string.Equals(f.Source, familyName, StringComparison.OrdinalIgnoreCase) ||
            f.FamilyNames.Values.Any(v => string.Equals(v, familyName, StringComparison.OrdinalIgnoreCase)));
        private static FontFamily ResolveCascadia()
        {
            // Try system first
            if (FontInstalled("Cascadia Mono"))
                return new FontFamily("Cascadia Mono");

            // Try embedded resource (#FamilyName from file)
            // If you embedded CascadiaCode-Regular.ttf it usually exposes "Cascadia Code" family
            return new FontFamily("./Fonts/#Cascadia Code");
        }
        static public void ChangeFont(string fontFamily)
        {
            string normalized = (fontFamily ?? "default").ToLowerInvariant();

            FontFamily selectedFont = normalized switch
            {
                // Sans Serif fonts
                "segoe-ui-variable" => new FontFamily("Segoe UI Variable"),
                "noto-sans" => new FontFamily("./Fonts/#Noto Sans"),
                "noto-sans-cjk" => new FontFamily("./Fonts/#Noto Sans CJK SC"),
                "inter" => new FontFamily("./Fonts/#Open Sans"), // Using OpenSans as Inter substitute
                "roboto" => new FontFamily("./Fonts/#Roboto"),
                "rubik" => new FontFamily("./Fonts/#Rubik"),
                
                // Serif fonts - using available fonts as substitutes
                "noto-serif" => new FontFamily("./Fonts/#Saira"), // Using Saira as serif substitute
                "noto-serif-cjk" => new FontFamily("./Fonts/#FangZheng"), // Using FangZheng as CJK serif
                
                // Monospace fonts
                "jetbrains-mono" => new FontFamily("Consolas"), // System monospace font as substitute
                "cascadia-mono" => new FontFamily("Cascadia Mono"),
                
                // Default now uses Cascadia Mono
                "default" or _ => ResolveCascadia()
            };

            Application.Current.Resources["AppFontFamily"] = selectedFont;
            UserConfigManager.Current.FontFamily = normalized;
        }
    }
}