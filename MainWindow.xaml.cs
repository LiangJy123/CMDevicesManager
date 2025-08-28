// CMDevicesManager - System Hardware Monitoring Application
// This application displays CPU, GPU, and memory performance metrics
// for system monitoring purposes. It does not transmit data externally.
// Source code available for transparency and security review.

using CMDevicesManager.Helper;
using CMDevicesManager.Pages;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace CMDevicesManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Cache single instances to avoid reinitialization on repeated clicks
        private HomePage? _homePage;
        private DevicePage? _devicePage;
        private SettingsPage? _settingsPage;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                Logger.Info("App Launched");
                
                // Validate resources exist before using them
                ValidateResources();
                
                // Navigate to homepage with error handling (use cached instance)
                try
                {
                    MainFrame.Navigate(GetHomePage());
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to navigate to HomePage", ex);
                    // Show error message to user but don't crash
                    MessageBox.Show("Warning: Failed to load home page. Some features may not work correctly.", 
                                   "Navigation Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize MainWindow", ex);
                throw; // Re-throw as this is a critical error
            }
        }

        private HomePage GetHomePage() => _homePage ??= new HomePage();
        private DevicePage GetDevicePage() => _devicePage ??= new DevicePage();
        private SettingsPage GetSettingsPage() => _settingsPage ??= new SettingsPage();

        private void ValidateResources()
        {
            try
            {
                // Check if background image exists
                var backgroundUri = new Uri("pack://application:,,,/Resources/background.png");
                var backgroundResource = Application.GetResourceStream(backgroundUri);
                if (backgroundResource == null)
                {
                    Logger.Warn("Background image not found: /Resources/background.png");
                }

                // Check if icon exists
                var iconUri = new Uri("pack://application:,,,/Resources/icon/icon.ico");
                var iconResource = Application.GetResourceStream(iconUri);
                if (iconResource == null)
                {
                    Logger.Warn("Icon not found: /Resources/icon/icon.ico");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Resource validation failed", ex);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is ListBoxItem item && item.Tag is string pagePath)
            {
                try
                {
                    // Short-circuit if already on the requested page
                    if (pagePath.Contains("HomePage") && MainFrame.Content is HomePage) return;
                    if (pagePath.Contains("DevicePage") && MainFrame.Content is DevicePage) return;
                    if (pagePath.Contains("SettingsPage") && MainFrame.Content is SettingsPage) return;

                    // Use cached instances to avoid re-initializing pages/services/timers
                    if (pagePath.Contains("HomePage"))
                    {
                        MainFrame.Navigate(GetHomePage());
                    }
                    else if (pagePath.Contains("DevicePage"))
                    {
                        MainFrame.Navigate(GetDevicePage());
                    }
                    else if (pagePath.Contains("SettingsPage"))
                    {
                        MainFrame.Navigate(GetSettingsPage());
                    }
                    else
                    {
                        // Fallback to URI navigation for XAML files
                        MainFrame.Source = new Uri(pagePath, UriKind.Relative);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Navigation failed to {pagePath}", ex);
                    // Fallback to HomePage if navigation fails
                    MainFrame.Navigate(GetHomePage());
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)  // 双击标题栏切换最大化/还原
            {
                ToggleWindowState();
            }
            else
            {
                DragMove();
            }
        }
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleWindowState();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleWindowState()
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }
    }
}