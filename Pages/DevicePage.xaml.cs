using CMDevicesManager.Models;
using HID.DisplayController;
using HidApi;
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

        private MultiDeviceManager? _multiDeviceManager;
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
                    // NavigationService?.Navigate(new DeviceConfigPage(deviceViewModel.DeviceInfo));
                    NavigationService?.Navigate(new DeviceLive(deviceViewModel.DeviceInfo));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Navigation to DeviceConfigPage failed: {ex}");
                ShowStatusMessage("Failed to open device configuration.", true);
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                InitializeDeviceManager();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize device manager: {ex}");
                ShowStatusMessage($"Failed to initialize device manager: {ex.Message}", true);
            }
        }

        private void InitializeDeviceManager()
        {
            try
            {
                _multiDeviceManager = new MultiDeviceManager(0x2516, 0x0228);

                // Set up event handlers
                _multiDeviceManager.ControllerAdded += OnControllerAdded;
                _multiDeviceManager.ControllerRemoved += OnControllerRemoved;
                _multiDeviceManager.ControllerError += OnControllerError;

                // Start monitoring for devices
                _multiDeviceManager.StartMonitoring();

                // Populate existing devices
                PopulateConnectedDevices();

                ShowStatusMessage($"Device manager initialized. Found {DeviceViewModels.Count} connected device(s).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize device manager: {ex}");
                ShowStatusMessage($"Failed to initialize device manager: {ex.Message}", true);
            }
        }

        private void OnControllerAdded(object? sender, DeviceControllerEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // Check if device is already in the collection
                    if (!DeviceViewModels.Any(d => d.Path == e.Device.Path))
                    {
                        var deviceViewModel = CreateDeviceViewModel(e.Device);
                        DeviceViewModels.Add(deviceViewModel);
                        UpdateDeviceListVisibility();
                        ShowStatusMessage($"Device connected: {e.Device.ProductString}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error adding device to UI: {ex}");
                }
            });
        }

        private void OnControllerRemoved(object? sender, DeviceControllerEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var deviceToRemove = DeviceViewModels.FirstOrDefault(d => d.Path == e.Device.Path);
                    if (deviceToRemove != null)
                    {
                        DeviceViewModels.Remove(deviceToRemove);
                        UpdateDeviceListVisibility();
                        ShowStatusMessage($"Device disconnected: {e.Device.ProductString}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error removing device from UI: {ex}");
                }
            });
        }

        private void OnControllerError(object? sender, ControllerErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ShowStatusMessage($"Device Error - {e.Device.ProductString}: {e.Exception.Message}", true);
            });
        }

        private void PopulateConnectedDevices()
        {
            try
            {
                if (_multiDeviceManager == null) return;

                var activeControllers = _multiDeviceManager.GetActiveControllers();

                DeviceViewModels.Clear();

                foreach (var controller in activeControllers)
                {
                    try
                    {
                        var deviceInfo = controller.DeviceInfo;
                        if (deviceInfo != null)
                        {
                            var deviceViewModel = CreateDeviceViewModel(deviceInfo);
                            DeviceViewModels.Add(deviceViewModel);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error getting device info from controller: {ex}");
                    }
                }

                UpdateDeviceListVisibility();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error populating connected devices: {ex}");
                ShowStatusMessage($"Error loading connected devices: {ex.Message}", true);
            }
        }

        private DeviceViewModel CreateDeviceViewModel(DeviceInfo deviceInfo)
        {
            // Try to find a suitable image for the device
            string? imagePath = GetDeviceImagePath(deviceInfo);
            return new DeviceViewModel(deviceInfo, imagePath);
        }

        private string? GetDeviceImagePath(DeviceInfo deviceInfo)
        {
            // Check for device-specific images based on product name
            var productName = deviceInfo.ProductString?.ToLowerInvariant() ?? "";

            string? imagePath = null;

            if (productName.Contains("haf700"))
            {
                imagePath = "Resources/Devices/HAF700V2.jpg";
            }
            // Add more device-specific image mappings here as needed

            // Check if the image file exists, if not, use null (will show default icon)
            if (!string.IsNullOrEmpty(imagePath))
            {
                try
                {
                    var uri = new Uri($"pack://application:,,,/{imagePath}");
                    var resource = Application.GetResourceStream(uri);
                    if (resource == null)
                    {
                        imagePath = null; // Image not found, use default
                    }
                }
                catch
                {
                    imagePath = null; // Error loading image, use default
                }
            }

            return imagePath;
        }

        private void UpdateDeviceListVisibility()
        {
            bool hasDevices = DeviceViewModels.Count > 0;
            DevicesItemsControl.Visibility = hasDevices ? Visibility.Visible : Visibility.Collapsed;
            NoDevicesPanel.Visibility = hasDevices ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ShowStatusMessage(string message, bool isError = false)
        {
            try
            {
                Debug.WriteLine($"Status: {message}");

                StatusText.Text = message;
                StatusBorder.Background = new SolidColorBrush(isError ? Colors.Red : Color.FromRgb(33, 150, 243));
                StatusBorder.Visibility = Visibility.Visible;

                // Auto-hide status message after 3 seconds
                _statusTimer?.Stop();
                _statusTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                _statusTimer.Tick += (s, e) =>
                {
                    StatusBorder.Visibility = Visibility.Collapsed;
                    _statusTimer?.Stop();
                };
                _statusTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing status message: {ex}");
            }
        }

        // Clean up resources when page is unloaded
        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _statusTimer?.Stop();
                _statusTimer = null;

                if (_multiDeviceManager != null)
                {
                    _multiDeviceManager.ControllerAdded -= OnControllerAdded;
                    _multiDeviceManager.ControllerRemoved -= OnControllerRemoved;
                    _multiDeviceManager.ControllerError -= OnControllerError;

                    _multiDeviceManager.StopMonitoring();
                    _multiDeviceManager.Dispose();
                    _multiDeviceManager = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during page cleanup: {ex}");
            }
        }
    }
}