using CMDevicesManager.Helper;
using CMDevicesManager.Language;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for SettingsPage.xaml
    /// </summary>
    public partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent();
            LoadCurrentLanguageSettings();
        }

        private void LoadCurrentLanguageSettings()
        {
            // Set the current language radio button based on user config
            string currentLanguage = UserConfigManager.Current.Language.ToLowerInvariant();
            
            switch (currentLanguage)
            {
                case "en-us":
                    EnglishRadio.IsChecked = true;
                    break;
                case "zh-cn":
                default:
                    ChineseRadio.IsChecked = true;
                    break;
            }
        }

        private void LanguageRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton radio && radio.Tag is string languageCode)
            {
                // Change language immediately
                LanguageSwitch.ChangeLanguage(languageCode);
                
                // Save the configuration
                UserConfigManager.Save();
                
                Logger.Info($"Language changed to: {languageCode}");
            }
        }
    }
}
