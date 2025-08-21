using CMDevicesManager.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;

namespace CMDevicesManager.Language
{
    static public class LanguageSwitch
    {
        static public void ChangeLanguage(string culture)
        {
            var dict = new ResourceDictionary();
            switch (culture.ToLowerInvariant())
            {
                case "en-us":
                    dict.Source = new Uri("Language/StringResources.en-US.xaml", UriKind.Relative);
                    break;
                case "zh-cn":
                default:
                    dict.Source = new Uri("Language/StringResources.xaml", UriKind.Relative);
                    break;
            }

            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dict);

            UserConfigManager.Current.Language = culture.ToLowerInvariant();
        }
    }
}
