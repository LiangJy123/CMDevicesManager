using CMDevicesManager.Models;
using HID.DisplayController;
using HidApi;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace CMDevicesManager.Pages
{
    public partial class DevicePage : Page
    {
        public ObservableCollection<DeviceInfos> Devices { get; } = new();

        private MultiDeviceManager? _multiDeviceManager;

        public DevicePage()
        {
            InitializeComponent();

            Devices.Add(new DeviceInfos("haf700v2", "HAF700 V2", "Resources/Devices/HAF700V2.jpg"));

            DataContext = this;
        }

        // New: Screen customization navigation
        private void ScreenCustomize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is DeviceInfos device)
                {
                    NavigationService?.Navigate(new DeviceConfigPage(device));
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation failed: {ex}");
                MessageBox.Show("Failed to open device configuration.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Placeholder implementations for other actions
        private void PlaybackMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DeviceInfos device)
            {
                ShowStatusMessage($"Playback mode clicked for {device.Name}");
                e.Handled = true;
            }
        }

        private void FirmwareInfo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DeviceInfos device)
            {
                ShowStatusMessage($"Firmware info clicked for {device.Name}");
                e.Handled = true;
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _multiDeviceManager = new MultiDeviceManager(0x2516, 0x0228);
                _multiDeviceManager.ControllerAdded += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try { } catch (Exception ex) { ShowStatusMessage($"Failed to add device: {ex.Message}"); }
                    });
                };
                _multiDeviceManager.ControllerRemoved += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try { } catch (Exception ex) { ShowStatusMessage($"Failed to remove device: {ex.Message}"); }
                    });
                };
                _multiDeviceManager.ControllerError += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ShowStatusMessage($"Device Error - {ev.Device.ProductString}: {ev.Exception.Message}");
                    });
                };
                _multiDeviceManager.StartMonitoring();
                var activeControllers = _multiDeviceManager.GetActiveControllers();
                foreach (var controller in activeControllers)
                {
                    // Potentially map to Devices collection in future
                }
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Failed to initialize device manager: {ex.Message}");
            }
        }

        private void ShowStatusMessage(string message)
        {
            Console.WriteLine($"Message: {message}");
        }
    }
}
