using CMDevicesManager.Helper;
using CMDevicesManager.Language;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;
using RadioButton = System.Windows.Controls.RadioButton;

namespace CMDevicesManager.Pages
{
    public partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent();
            LoadCurrentLanguageSettings();
            LoadCurrentFontSettings();
        }

        private void LoadCurrentLanguageSettings()
        {
            string currentLanguage = UserConfigManager.Current.Language.ToLowerInvariant();
            switch (currentLanguage)
            {
                case "en-us": EnglishRadio.IsChecked = true; break;
                case "zh-tw": TraditionalChineseRadio.IsChecked = true; break;
                case "zh-cn":
                default: ChineseRadio.IsChecked = true; break;
            }
        }

        private void LoadCurrentFontSettings()
        {
            string currentFont = UserConfigManager.Current.FontFamily.ToLowerInvariant();
            switch (currentFont)
            {
                //case "segoe-ui-variable": SegoeUIVariableFontRadio.IsChecked = true; break;
                case "noto-sans": NotoSansFontRadio.IsChecked = true; break;
                case "noto-sans-cjk": NotoSansCJKFontRadio.IsChecked = true; break;
                //case "inter": InterFontRadio.IsChecked = true; break;
                //case "roboto": RobotoFontRadio.IsChecked = true; break;
                case "rubik": RubikFontRadio.IsChecked = true; break;
                //case "noto-serif": NotoSerifFontRadio.IsChecked = true; break;
                //case "noto-serif-cjk": NotoSerifCJKFontRadio.IsChecked = true; break;
                //case "jetbrains-mono": JetBrainsMonoFontRadio.IsChecked = true; break;
                //case "cascadia-mono": CascadiaMonoFontRadio.IsChecked = true; break;
                case "default":
                default: DefaultFontRadio.IsChecked = true; break;
            }
        }

        private void LanguageRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio && radio.Tag is string languageCode)
            {
                LanguageSwitch.ChangeLanguage(languageCode);
                UserConfigManager.Save();
                Logger.Info($"Language changed to: {languageCode}");
            }
        }

        private void FontRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio && radio.Tag is string fontFamily)
            {
                FontSwitch.ChangeFont(fontFamily);
                UserConfigManager.Save();
                Logger.Info($"Font changed to: {fontFamily}");
            }
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            const string url = "https://www.coolermaster.com.cn/";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                Logger.Info("Opened update URL in default browser");
            }
            catch (System.Exception ex)
            {
                Logger.Error("Failed to open update URL", ex);
                MessageBox.Show("Unable to open the website.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
