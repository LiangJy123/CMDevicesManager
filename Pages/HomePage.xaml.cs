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
using CMDevicesManager.ViewModels;
using CMDevicesManager.Services;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for HomePage.xaml
    /// </summary>
    public partial class HomePage : Page
    {
        public HomePage()
        {
            InitializeComponent();

            // Switch between fake and real hardware-backed service
            // For testing: Use FakeSystemMetricsService to verify dynamic updates work
            // For production: Use RealSystemMetricsService for actual hardware monitoring
            
            ISystemMetricsService service;
            
#if DEBUG
            // In debug builds, you can easily switch to fake service for testing
            bool useFakeService = false; // Set to true to test with fake data
            service = useFakeService ? new FakeSystemMetricsService() : new RealSystemMetricsService();
#else
            // In release builds, always use real hardware monitoring
            service = new RealSystemMetricsService();
#endif
            
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
