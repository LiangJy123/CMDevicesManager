using CMDevicesManager.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;

namespace CMDevicesManager.Language
{
    static public class FontSwitch
    {
        static public void ChangeFont(string fontFamily)
        {
            FontFamily selectedFont = fontFamily.ToLowerInvariant() switch
            {
                "noto-sans" => new FontFamily("./Fonts/#Noto Sans"),
                "noto-sans-cjk" => new FontFamily("./Fonts/#Noto Sans CJK SC"),
                "rubik" => new FontFamily("./Fonts/#Rubik"),
                "default" or _ => new FontFamily("Segoe UI") // System default
            };

            // Apply font globally to application resources
            Application.Current.Resources["AppFontFamily"] = selectedFont;

            UserConfigManager.Current.FontFamily = fontFamily.ToLowerInvariant();
        }
    }
}