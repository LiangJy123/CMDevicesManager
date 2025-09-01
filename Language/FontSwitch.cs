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
        static public void ChangeFont(string fontFamily)
        {
            string normalized = (fontFamily ?? "default").ToLowerInvariant();

            FontFamily selectedFont = normalized switch
            {
                "noto-sans" => new FontFamily("./Fonts/#Noto Sans"),
                "noto-sans-cjk" => new FontFamily("./Fonts/#Noto Sans CJK SC"),
                "rubik" => new FontFamily("./Fonts/#Rubik"),
                "cascadia-mono" => new FontFamily("Cascadia Mono"),
                // Default now uses Cascadia Mono
                "default" or _ => new FontFamily("Cascadia Mono")
            };

            Application.Current.Resources["AppFontFamily"] = selectedFont;
            UserConfigManager.Current.FontFamily = normalized;
        }
    }
}