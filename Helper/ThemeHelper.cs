using System.Linq;
using System.Windows;
using MaterialDesignThemes.Wpf;
namespace CMDevicesManager.Helper
{
    public static class ThemeHelper
    {
        public static void SetDarkMode(bool dark)
        {
            var theme = System.Windows.Application.Current.Resources.MergedDictionaries.OfType<BundledTheme>().FirstOrDefault();
            if (theme != null)
                theme.BaseTheme = dark ? BaseTheme.Dark : BaseTheme.Light;
        }
    }
}