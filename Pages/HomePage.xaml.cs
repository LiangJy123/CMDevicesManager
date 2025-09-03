using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CMDevicesManager.ViewModels;
using CMDevicesManager.Services;
using System.Windows.Navigation;

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

            // Do not keep the Page alive in the navigation journal to avoid piling up timers/VMs
            JournalEntry.SetKeepAlive(this, false);

            ISystemMetricsService service = new RealSystemMetricsService();
            DataContext = new HomeViewModel(service);

            // Swallow wheel/keyboard scrolling just in case a parent tries to scroll.
            PreviewMouseWheel += (_, e) => e.Handled = true;
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
                    e.Handled = true;
            };

            // Ensure resources are released when leaving the page
            Unloaded += (_, __) =>
            {
                if (DataContext is HomeViewModel vm)
                {
                    vm.Dispose();
                }
            };

            // In your ViewModel or code-behind
            var metricsService = new RealSystemMetricsService();

            // Get temperature data
            double cpuTemp = metricsService.GetCpuTemperature();     // °C
            double gpuTemp = metricsService.GetGpuTemperature();     // °C

            // Get power consumption
            double cpuPower = metricsService.GetCpuPower();          // Watts
            double gpuPower = metricsService.GetGpuPower();          // Watts

            // Get usage percentages
            double cpuUsage = metricsService.GetCpuUsagePercent();   // %
            double gpuUsage = metricsService.GetGpuUsagePercent();   // %

            // Get device names
            string cpuName = metricsService.CpuName;
            string gpuName = metricsService.PrimaryGpuName;
        }
    }
}
