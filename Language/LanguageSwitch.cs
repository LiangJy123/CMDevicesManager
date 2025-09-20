using CMDevicesManager.Helper;
using System;
using System.Windows;
using Application = System.Windows.Application;

namespace CMDevicesManager.Language
{
    public static class LanguageSwitch
    {
        // NEW: language changed event
        public static event Action<string>? LanguageChanged;

        public static void ChangeLanguage(string culture)
        {
            var dict = new ResourceDictionary();
            switch (culture.ToLowerInvariant())
            {
                case "en-us":
                    dict.Source = new Uri("Language/StringResources.en-US.xaml", UriKind.Relative);
                    break;
                case "zh-tw":
                    dict.Source = new Uri("Language/StringResources.zh-TW.xaml", UriKind.Relative);
                    break;
                case "zh-cn":
                default:
                    dict.Source = new Uri("Language/StringResources.xaml", UriKind.Relative);
                    break;
            }

            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dict);

            var normalized = culture.ToLowerInvariant();
            UserConfigManager.Current.Language = normalized;

            // RAISE EVENT
            LanguageChanged?.Invoke(normalized);
        }
    }
}
