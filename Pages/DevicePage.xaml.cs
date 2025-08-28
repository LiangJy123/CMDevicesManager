using CMDevicesManager.Models;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for DevicePage.xaml
    /// </summary>
    public partial class DevicePage : Page
    {
        public ObservableCollection<DeviceInfo> Devices { get; } = new();

        public DevicePage()
        {
            InitializeComponent();

            // TODO: Replace with real device discovery/population.
            // Add as many devices as are available; each will render as a card automatically.
            Devices.Add(new DeviceInfo("haf700v2", "HAF700 V2", "Resources/Devices/HAF700V2.jpg"));
            // Devices.Add(new DeviceInfo("device-2", "Another Device", "Resources/Devices/Another.jpg"));

            DataContext = this;
        }

        private void DeviceCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.DataContext is DeviceInfo device)
                {
                    NavigationService?.Navigate(new DeviceConfigPage(device));
                }
            }
            catch (Exception ex)
            {
                // If you have a Logger available in scope, you can log it.
                System.Diagnostics.Debug.WriteLine($"Navigation to DeviceConfigPage failed: {ex}");
                MessageBox.Show("Failed to open device configuration.", "Navigation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
