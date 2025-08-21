using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CMDevicesManager.ViewModels;
using CMDevicesManager.Services;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Test version of HomePage that uses FakeSystemMetricsService for demonstration
    /// </summary>
    public partial class HomePageTest : Page
    {
        public HomePageTest()
        {
            InitializeComponent();

            // Use fake service for testing/demonstration purposes
            ISystemMetricsService service = new FakeSystemMetricsService();
            DataContext = new HomeViewModel(service);

            // Swallow wheel/keyboard scrolling just in case a parent tries to scroll.
            PreviewMouseWheel += (_, e) => e.Handled = true;
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
                    e.Handled = true;
            };
        }
    }
}