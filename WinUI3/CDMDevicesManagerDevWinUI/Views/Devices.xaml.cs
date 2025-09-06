using HID.DisplayController;
using HidApi;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CDMDevicesManagerDevWinUI.Views
{
    // Simple wrapper class for device information to support data binding
    public class DeviceInfoViewModel : INotifyPropertyChanged
    {
        public string ProductName { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string ManufacturerName { get; set; } = string.Empty;
        public string DevicePath { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed partial class Devices : Page, INotifyPropertyChanged
    {
        private MultiDeviceManager? _multiDeviceManager;
        
        // Observable collection for device information binding
        public ObservableCollection<DeviceInfoViewModel> ConnectedDevices { get; } = new ObservableCollection<DeviceInfoViewModel>();
        
        public Devices()
        {
            this.InitializeComponent();
            this.Loaded += Devices_Loaded;
        }

        private void Devices_Loaded(object sender, RoutedEventArgs e)
        {
            _multiDeviceManager = new MultiDeviceManager(0x2516, 0x0228);

            // Set up event handlers for device changes
            _multiDeviceManager.ControllerAdded += OnDeviceAdded;
            _multiDeviceManager.ControllerRemoved += OnDeviceRemoved;

            // Must call StartMonitoring to begin detection
            _multiDeviceManager.StartMonitoring();

            // Populate existing devices
            PopulateConnectedDevices();

            // Example: Set brightness on all devices when the page is loaded
            _ = GetAllDevicesAndSetBrightness();
        }

        /// <summary>
        /// Check if a device already exists in the ConnectedDevices collection
        /// </summary>
        /// <param name="devicePath">Device path to check</param>
        /// <returns>True if device exists, false otherwise</returns>
        private bool IsDeviceAlreadyConnected(string devicePath)
        {
            return ConnectedDevices.Any(d => string.Equals(d.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Check if a device with the same serial number already exists
        /// </summary>
        /// <param name="serialNumber">Serial number to check</param>
        /// <returns>True if device with same serial exists, false otherwise</returns>
        private bool IsSerialNumberAlreadyConnected(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber) || serialNumber == "No Serial")
                return false;
            
            return ConnectedDevices.Any(d => string.Equals(d.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Add device to collection with duplicate checking
        /// </summary>
        /// <param name="deviceInfo">Device information</param>
        /// <returns>True if device was added, false if duplicate was found</returns>
        private bool TryAddDevice(DeviceInfo deviceInfo)
        {
            var devicePath = deviceInfo.Path ?? "";
            var serialNumber = deviceInfo.SerialNumber ?? "No Serial";

            // Check for duplicates by device path (primary identifier)
            if (IsDeviceAlreadyConnected(devicePath))
            {
                System.Diagnostics.Debug.WriteLine($"Device already connected with path: {devicePath}");
                return false;
            }

            // Check for duplicates by serial number (secondary check for devices with valid serials)
            if (IsSerialNumberAlreadyConnected(serialNumber))
            {
                System.Diagnostics.Debug.WriteLine($"Device already connected with serial number: {serialNumber}");
                return false;
            }

            // Create a ViewModel wrapper for the device info
            var deviceViewModel = new DeviceInfoViewModel
            {
                ProductName = deviceInfo.ProductString ?? "Unknown Device",
                SerialNumber = serialNumber,
                ManufacturerName = deviceInfo.ManufacturerString ?? "Unknown",
                DevicePath = devicePath
            };

            ConnectedDevices.Add(deviceViewModel);
            System.Diagnostics.Debug.WriteLine($"Added device: {deviceViewModel.ProductName} (SN: {deviceViewModel.SerialNumber})");
            return true;
        }

        /// <summary>
        /// Remove device from collection by device path
        /// </summary>
        /// <param name="devicePath">Device path to remove</param>
        /// <returns>True if device was removed, false if not found</returns>
        private bool TryRemoveDevice(string devicePath)
        {
            var deviceToRemove = ConnectedDevices.FirstOrDefault(d => string.Equals(d.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));
            if (deviceToRemove != null)
            {
                ConnectedDevices.Remove(deviceToRemove);
                System.Diagnostics.Debug.WriteLine($"Removed device: {deviceToRemove.ProductName} (SN: {deviceToRemove.SerialNumber})");
                return true;
            }
            
            System.Diagnostics.Debug.WriteLine($"Device not found for removal: {devicePath}");
            return false;
        }

        /// <summary>
        /// Get device count statistics
        /// </summary>
        /// <returns>Tuple with total count and unique serial count</returns>
        private (int totalCount, int uniqueSerialCount) GetDeviceStatistics()
        {
            var totalCount = ConnectedDevices.Count;
            var uniqueSerialCount = ConnectedDevices
                .Where(d => !string.IsNullOrEmpty(d.SerialNumber) && d.SerialNumber != "No Serial")
                .Select(d => d.SerialNumber)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return (totalCount, uniqueSerialCount);
        }

        private void PopulateConnectedDevices()
        {
            var activeControllers = _multiDeviceManager?.GetActiveControllers() ?? new List<DisplayController>();

            // Clear existing devices and populate with current ones
            ConnectedDevices.Clear();
            
            var addedCount = 0;
            var duplicateCount = 0;

            foreach (var controller in activeControllers)
            {
                DeviceInfo deviceInfo = controller.DeviceInfo;
                
                if (TryAddDevice(deviceInfo))
                {
                    addedCount++;
                }
                else
                {
                    duplicateCount++;
                }
            }

            var (totalCount, uniqueSerialCount) = GetDeviceStatistics();
            System.Diagnostics.Debug.WriteLine($"PopulateConnectedDevices: Added {addedCount}, Skipped {duplicateCount} duplicates. Total: {totalCount} devices, {uniqueSerialCount} unique serials");

            // Notify UI that the collection has changed
            OnPropertyChanged(nameof(ConnectedDevices));
        }

        private void OnDeviceAdded(object? sender, DeviceControllerEventArgs e)
        {
            // Update UI on the main thread
            DispatcherQueue.TryEnqueue(() =>
            {
                if (TryAddDevice(e.Device))
                {
                    var (totalCount, uniqueSerialCount) = GetDeviceStatistics();
                    System.Diagnostics.Debug.WriteLine($"Device added via event. Total: {totalCount} devices, {uniqueSerialCount} unique serials");
                }
            });
        }

        private void OnDeviceRemoved(object? sender, DeviceControllerEventArgs e)
        {
            // Update UI on the main thread
            DispatcherQueue.TryEnqueue(() =>
            {
                if (TryRemoveDevice(e.Device.Path ?? ""))
                {
                    var (totalCount, uniqueSerialCount) = GetDeviceStatistics();
                    System.Diagnostics.Debug.WriteLine($"Device removed via event. Total: {totalCount} devices, {uniqueSerialCount} unique serials");
                }
            });
        }

        private async Task GetAllDevicesAndSetBrightness()
        {
            if (_multiDeviceManager == null)
                return;

            try
            {
                // Get all active controllers (devices)
                var activeControllers = _multiDeviceManager.GetActiveControllers();
                
                // Log device count
                System.Diagnostics.Debug.WriteLine($"Found {activeControllers.Count} active devices");
                
                // Set brightness to 80 on all devices
                var results = await _multiDeviceManager.SetBrightnessOnAllDevices(80);
                // set rotation example
                results = await _multiDeviceManager.SetRotationOnAllDevices(90);

                // Log results for each device
                foreach (var result in results)
                {
                    var status = result.Value ? "Success" : "Failed";
                    System.Diagnostics.Debug.WriteLine($"Device {result.Key}: Brightness set to 80 - {status}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting brightness: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
