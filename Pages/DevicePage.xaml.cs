using CMDevicesManager.Models;
using CMDevicesManager.Services;
using HID.DisplayController;
using HidApi;
using HidSharp;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using Path = System.IO.Path;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Device view model for UI binding
    /// </summary>
    public class DeviceViewModel : INotifyPropertyChanged
    {
        public DeviceInfo DeviceInfo { get; }
        public string ProductString => DeviceInfo.ProductString ?? "Unknown Device";
        public string SerialNumber => DeviceInfo.SerialNumber ?? "No Serial";
        public string ManufacturerString => DeviceInfo.ManufacturerString ?? "Unknown";
        public string Path => DeviceInfo.Path ?? "";

        private string? _imagePath;
        public string? ImagePath
        {
            get => _imagePath;
            set
            {
                _imagePath = value;
                OnPropertyChanged();
            }
        }

        public DeviceViewModel(DeviceInfo deviceInfo, string? imagePath = null)
        {
            DeviceInfo = deviceInfo;
            _imagePath = imagePath;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Interaction logic for DevicePage.xaml
    /// </summary>
    public partial class DevicePage : Page
    {
        public ObservableCollection<DeviceViewModel> DeviceViewModels { get; } = new();

        private DispatcherTimer? _statusTimer;

        public DevicePage()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void DeviceCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.DataContext is DeviceViewModel deviceViewModel)
                {
                     NavigationService?.Navigate(new DeviceConfigPage(deviceViewModel.DeviceInfo));
                    //NavigationService?.Navigate(new DeviceLive(deviceViewModel.DeviceInfo));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Navigation to DeviceConfigPage failed: {ex}");
                ShowStatusMessage("Failed to open device configuration.", true);
            }
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.DataContext is DeviceViewModel deviceViewModel)
                {
                    NavigationService?.Navigate(new DeviceConfigPage(deviceViewModel.DeviceInfo));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Navigation to DeviceConfigPage failed: {ex}");
                ShowStatusMessage("Failed to open device configuration.", true);
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.DataContext is DeviceViewModel deviceViewModel)
                {
<<<<<<< HEAD
                    // For now, navigate to a general settings page or device-specific settings
                    // You can modify this to navigate to a device-specific settings page if needed
                    NavigationService?.Navigate(new SettingsPage());
                    
                    // Alternative: Create a device-specific settings page
                    // NavigationService?.Navigate(new DeviceSettingsPage(deviceViewModel.DeviceInfo));
=======
                    // Create device settings page
                    var deviceSettingsPage = new DeviceSettings(deviceViewModel.DeviceInfo);
                    
                    // Create and show popup window
                    var popupWindow = new PopupWindow(deviceSettingsPage, $"Device Settings - {deviceViewModel.ProductString}")
                    {
                        Owner = Window.GetWindow(this)
                    };
                    
                    popupWindow.ShowDialog(); // Modal popup
>>>>>>> eddcd56aea4c1497b4c62232999fcd43228fbc3d
                }
            }
            catch (Exception ex)
            {
<<<<<<< HEAD
                Debug.WriteLine($"Navigation to SettingsPage failed: {ex}");
=======
                Debug.WriteLine($"Failed to open device settings popup: {ex}");
>>>>>>> eddcd56aea4c1497b4c62232999fcd43228fbc3d
                ShowStatusMessage("Failed to open device settings.", true);
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use the HID Device Service instead of creating a new MultiDeviceManager
                InitializeWithHidService();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Page_Loaded failed: {ex}");
                ShowStatusMessage("Failed to initialize device page.", true);
            }
        }

        private void InitializeWithHidService()
        {
            try
            {
                var hidService = ServiceLocator.HidDeviceService;
                
                // Subscribe to device events
                hidService.DeviceListChanged += OnDeviceListChanged;
                hidService.DeviceConnected += OnDeviceConnected;
                hidService.DeviceDisconnected += OnDeviceDisconnected;
                hidService.DeviceError += OnDeviceError;

                // Load existing devices
                LoadDevicesFromService();

                ShowStatusMessage($"Monitoring {hidService.ConnectedDeviceCount} HID devices", false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize with HID service: {ex}");
                ShowStatusMessage("HID Device Service not available", true);
            }
        }

        private void LoadDevicesFromService()
        {
            try
            {
                var hidService = ServiceLocator.HidDeviceService;
                
                DeviceViewModels.Clear();
                
                foreach (var device in hidService.ConnectedDevices)
                {
                    var viewModel = new DeviceViewModel(device, GetDeviceImagePath(device));
                    DeviceViewModels.Add(viewModel);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load devices from service: {ex}");
            }
        }

        private void OnDeviceListChanged(object? sender, DeviceEventArgs e)
        {
            // Refresh the entire device list
            Dispatcher.Invoke(() =>
            {
                LoadDevicesFromService();
            });
        }

        private void OnDeviceConnected(object? sender, DeviceEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ShowStatusMessage($"Device connected: {e.Device.ProductString}", false);
            });
        }

        private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ShowStatusMessage($"Device disconnected: {e.Device.ProductString}", false);
            });
        }

        private void OnDeviceError(object? sender, DeviceErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ShowStatusMessage($"Device error: {e.Exception.Message}", true);
            });
        }

        private string GetDeviceImagePath(DeviceInfo device)
        {
            // Your existing logic for determining device image paths
            return "pack://application:,,,/Resources/device-default.png";
        }

        private void ShowStatusMessage(string message, bool isError)
        {
            // Your existing status message logic
            Debug.WriteLine($"Status: {message}");
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Unsubscribe from events when page is unloaded
                var hidService = ServiceLocator.HidDeviceService;
                hidService.DeviceListChanged -= OnDeviceListChanged;
                hidService.DeviceConnected -= OnDeviceConnected;
                hidService.DeviceDisconnected -= OnDeviceDisconnected;
                hidService.DeviceError -= OnDeviceError;

                _statusTimer?.Stop();
                _statusTimer = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Page_Unloaded cleanup failed: {ex}");
            }
        }

        // Example method to set brightness on all devices
        private async void SetBrightnessButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var hidService = ServiceLocator.HidDeviceService;
                var results = await hidService.SetBrightnessAsync(50); // Set to 50%
                
                var successCount = results.Values.Count(r => r);
                ShowStatusMessage($"Brightness set on {successCount}/{results.Count} devices", false);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Failed to set brightness: {ex.Message}", true);
            }
        }
    }
}