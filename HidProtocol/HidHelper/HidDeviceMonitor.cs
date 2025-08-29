using HidApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Threading.Timer;

namespace HID.DisplayController
{
    /// <summary>
    /// Event arguments for HID device connection events
    /// </summary>
    public class HidDeviceEventArgs : EventArgs
    {
        public DeviceInfo Device { get; }
        public DateTime Timestamp { get; }

        public HidDeviceEventArgs(DeviceInfo device)
        {
            Device = device;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// HID Device Monitor for detecting device plug/unplug events
    /// </summary>
    public class HidDeviceMonitor : IDisposable
    {
        private readonly Timer _monitorTimer;
        private readonly HashSet<string> _previousDevicePaths;
        private readonly Dictionary<string, DeviceInfo> _previousDevices;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        /// <summary>
        /// Occurs when a new HID device is connected
        /// </summary>
        public event EventHandler<HidDeviceEventArgs>? DeviceConnected;

        /// <summary>
        /// Occurs when a HID device is disconnected
        /// </summary>
        public event EventHandler<HidDeviceEventArgs>? DeviceDisconnected;

        /// <summary>
        /// Occurs when any HID device change is detected
        /// </summary>
        public event EventHandler<EventArgs>? DeviceListChanged;

        /// <summary>
        /// Gets or sets the monitoring interval in milliseconds
        /// </summary>
        public int MonitoringInterval { get; set; } = 1000; // Default 1 second

        /// <summary>
        /// Gets or sets the vendor ID filter (0 = monitor all vendors)
        /// </summary>
        public ushort VendorIdFilter { get; set; } = 0;

        /// <summary>
        /// Gets or sets the product ID filter (0 = monitor all products)
        /// </summary>
        public ushort ProductIdFilter { get; set; } = 0;

        /// <summary>
        /// Gets whether the monitor is currently running
        /// </summary>
        public bool IsMonitoring { get; private set; }

        /// <summary>
        /// Gets the count of currently connected devices matching the filter
        /// </summary>
        public int ConnectedDeviceCount
        {
            get
            {
                lock (_lockObject)
                {
                    return _previousDevicePaths.Count;
                }
            }
        }

        public HidDeviceMonitor()
        {
            _previousDevicePaths = new HashSet<string>();
            _previousDevices = new Dictionary<string, DeviceInfo>();
            _monitorTimer = new Timer(CheckForDeviceChanges, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Start monitoring for HID device changes
        /// </summary>
        public void StartMonitoring()
        {
            if (_disposed) return;

            lock (_lockObject)
            {
                if (IsMonitoring) return;

                // Initialize with current device list
                RefreshDeviceList();

                // Start the timer
                _monitorTimer.Change(MonitoringInterval, MonitoringInterval);
                IsMonitoring = true;

                Console.WriteLine($"[HidMonitor] Started monitoring (VID:0x{VendorIdFilter:X4}, PID:0x{ProductIdFilter:X4}, Interval:{MonitoringInterval}ms)");
            }
        }

        /// <summary>
        /// Stop monitoring for HID device changes
        /// </summary>
        public void StopMonitoring()
        {
            if (_disposed) return;

            lock (_lockObject)
            {
                if (!IsMonitoring) return;

                _monitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
                IsMonitoring = false;

                Console.WriteLine("[HidMonitor] Stopped monitoring");
            }
        }

        /// <summary>
        /// Set device filter for monitoring specific devices
        /// </summary>
        /// <param name="vendorId">Vendor ID (0 for all)</param>
        /// <param name="productId">Product ID (0 for all)</param>
        public void SetDeviceFilter(ushort vendorId, ushort productId)
        {
            lock (_lockObject)
            {
                VendorIdFilter = vendorId;
                ProductIdFilter = productId;

                if (IsMonitoring)
                {
                    // Refresh the device list with new filter
                    RefreshDeviceList();
                    Console.WriteLine($"[HidMonitor] Filter updated (VID:0x{vendorId:X4}, PID:0x{productId:X4})");
                }
            }
        }

        /// <summary>
        /// Get list of currently connected devices matching the filter
        /// </summary>
        /// <returns>List of connected devices</returns>
        public List<DeviceInfo> GetConnectedDevices()
        {
            lock (_lockObject)
            {
                return _previousDevices.Values.ToList();
            }
        }

        /// <summary>
        /// Check if a specific device is currently connected
        /// </summary>
        /// <param name="vendorId">Vendor ID</param>
        /// <param name="productId">Product ID</param>
        /// <param name="serialNumber">Optional serial number</param>
        /// <returns>True if device is connected</returns>
        public bool IsDeviceConnected(ushort vendorId, ushort productId, string? serialNumber = null)
        {
            lock (_lockObject)
            {
                return _previousDevices.Values.Any(d =>
                    d.VendorId == vendorId &&
                    d.ProductId == productId &&
                    (serialNumber == null || string.Equals(d.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)));
            }
        }

        /// <summary>
        /// Manually trigger a device list refresh and check for changes
        /// </summary>
        public void RefreshNow()
        {
            if (_disposed) return;

            CheckForDeviceChanges(null);
        }

        private void CheckForDeviceChanges(object? state)
        {
            if (_disposed) return;

            try
            {
                lock (_lockObject)
                {
                    var currentDevices = GetCurrentDevices();
                    var currentPaths = new HashSet<string>(currentDevices.Select(d => d.Path));
                    var currentDeviceDict = currentDevices.ToDictionary(d => d.Path, d => d);

                    // Check for newly connected devices
                    var newDevicePaths = currentPaths.Except(_previousDevicePaths).ToList();
                    var newDevices = newDevicePaths.Select(path => currentDeviceDict[path]).ToList();

                    // Check for disconnected devices
                    var disconnectedPaths = _previousDevicePaths.Except(currentPaths).ToList();
                    var disconnectedDevices = disconnectedPaths
                        .Where(path => _previousDevices.ContainsKey(path))
                        .Select(path => _previousDevices[path])
                        .ToList();

                    bool hasChanges = newDevices.Any() || disconnectedDevices.Any();

                    // Raise events for new devices
                    foreach (var device in newDevices)
                    {
                        Console.WriteLine($"[HidMonitor] Device connected: {device.ManufacturerString} {device.ProductString} (VID:0x{device.VendorId:X4} PID:0x{device.ProductId:X4}) - {device.Path}");
                        DeviceConnected?.Invoke(this, new HidDeviceEventArgs(device));
                    }

                    // Raise events for disconnected devices
                    foreach (var device in disconnectedDevices)
                    {
                        Console.WriteLine($"[HidMonitor] Device disconnected: {device.ManufacturerString} {device.ProductString} (VID:0x{device.VendorId:X4} PID:0x{device.ProductId:X4}) - {device.Path}");
                        DeviceDisconnected?.Invoke(this, new HidDeviceEventArgs(device));
                    }

                    // Update the previous device lists
                    _previousDevicePaths.Clear();
                    _previousDevicePaths.UnionWith(currentPaths);

                    _previousDevices.Clear();
                    foreach (var device in currentDevices)
                    {
                        _previousDevices[device.Path] = device;
                    }

                    // Raise general change event if there were any changes
                    if (hasChanges)
                    {
                        DeviceListChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HidMonitor] Error during device monitoring: {ex.Message}");
            }
        }

        private List<DeviceInfo> GetCurrentDevices()
        {
            try
            {
                return Hid.Enumerate(VendorIdFilter, ProductIdFilter).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HidMonitor] Error enumerating HID devices: {ex.Message}");
                return new List<DeviceInfo>();
            }
        }

        private void RefreshDeviceList()
        {
            var currentDevices = GetCurrentDevices();
            _previousDevicePaths.Clear();
            _previousDevicePaths.UnionWith(currentDevices.Select(d => d.Path));

            _previousDevices.Clear();
            foreach (var device in currentDevices)
            {
                _previousDevices[device.Path] = device;
            }

            Console.WriteLine($"[HidMonitor] Initialized with {currentDevices.Count} devices");
        }

        /// <summary>
        /// Print current device list to console
        /// </summary>
        public void PrintConnectedDevices()
        {
            lock (_lockObject)
            {
                var devices = _previousDevices.Values.ToList();
                Console.WriteLine($"[HidMonitor] Currently connected devices ({devices.Count}):");

                for (int i = 0; i < devices.Count; i++)
                {
                    var device = devices[i];
                    Console.WriteLine($"  {i + 1:D2}. VID:0x{device.VendorId:X4} PID:0x{device.ProductId:X4} - {device.ManufacturerString} {device.ProductString}");
                    Console.WriteLine($"      Path: {device.Path}");
                    if (!string.IsNullOrEmpty(device.SerialNumber))
                    {
                        Console.WriteLine($"      Serial: {device.SerialNumber}");
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopMonitoring();
            _monitorTimer?.Dispose();
            _disposed = true;
        }
    }
}