using CMDevicesManager.Helper;
using CMDevicesManager.Services;
using HID.DisplayController;
using HidApi;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Threading.Timer;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// Service for managing HID device operations across the application.
    /// 
    /// Device Path Filtering:
    /// - Device paths added to the filter persist throughout the application lifetime
    /// - Disconnected devices are NOT automatically removed from the filter
    /// - This allows filters to work seamlessly when devices reconnect
    /// - Use ClearDevicePathFilter() or RemoveDevicePathsFromFilter() to manually manage filters
    /// </summary>
    public class HidDeviceService : IDisposable
    {
        private MultiDeviceManager? _multiDeviceManager;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        // KeepAlive timer functionality
        private Timer? _keepAliveTimer;
        private volatile bool _isRealTimeDisplayEnabled = false;
        private readonly int _keepAliveIntervalMs = 4000; // 4 seconds
        private DateTime _lastKeepAliveTime = DateTime.MinValue;

        // Device path filtering
        private readonly HashSet<string> _filteredDevicePaths = new HashSet<string>();
        private bool _useDevicePathFilter = false;

        /// <summary>
        /// Observable collection of connected devices for UI binding
        /// </summary>
        public ObservableCollection<DeviceInfo> ConnectedDevices { get; } = new ObservableCollection<DeviceInfo>();

        /// <summary>
        /// Event fired when device list changes
        /// </summary>
        public event EventHandler<DeviceEventArgs>? DeviceListChanged;

        /// <summary>
        /// Event fired when a device is connected
        /// </summary>
        public event EventHandler<DeviceEventArgs>? DeviceConnected;

        /// <summary>
        /// Event fired when a device is disconnected
        /// </summary>
        public event EventHandler<DeviceEventArgs>? DeviceDisconnected;

        /// <summary>
        /// Event fired when a device operation fails
        /// </summary>
        public event EventHandler<DeviceErrorEventArgs>? DeviceError;

        /// <summary>
        /// Event fired when real-time display mode changes
        /// </summary>
        public event EventHandler<RealTimeDisplayEventArgs>? RealTimeDisplayModeChanged;

        /// <summary>
        /// Event fired when KeepAlive is sent to devices
        /// </summary>
        public event EventHandler<KeepAliveEventArgs>? KeepAliveSent;

        /// <summary>
        /// Event fired when device filter changes
        /// </summary>
        public event EventHandler<DeviceFilterEventArgs>? DeviceFilterChanged;

        /// <summary>
        /// Gets whether the service is initialized and monitoring
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Gets whether real-time display mode is currently enabled
        /// </summary>
        public bool IsRealTimeDisplayEnabled
        {
            get => _isRealTimeDisplayEnabled;
            private set
            {
                if (_isRealTimeDisplayEnabled != value)
                {
                    _isRealTimeDisplayEnabled = value;
                    Debug.WriteLine($"Real-time display mode {(value ? "enabled" : "disabled")}");
                    RealTimeDisplayModeChanged?.Invoke(this, new RealTimeDisplayEventArgs(value));
                    
                    if (value)
                    {
                        StartKeepAliveTimer();
                    }
                    else
                    {
                        StopKeepAliveTimer();
                    }
                }
            }
        }

        /// <summary>
        /// Gets whether device path filtering is enabled
        /// </summary>
        public bool IsDevicePathFilterEnabled => _useDevicePathFilter;

        /// <summary>
        /// Gets the current filtered device paths (read-only)
        /// </summary>
        public IReadOnlyCollection<string> FilteredDevicePaths
        {
            get
            {
                lock (_lockObject)
                {
                    return _filteredDevicePaths.ToList().AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Gets the count of connected devices
        /// </summary>
        public int ConnectedDeviceCount => ConnectedDevices.Count;

        /// <summary>
        /// Gets the count of filtered devices (devices that match the current filter)
        /// </summary>
        public int FilteredDeviceCount
        {
            get
            {
                if (!_useDevicePathFilter) return ConnectedDeviceCount;
                
                lock (_lockObject)
                {
                    return ConnectedDevices.Count(d => _filteredDevicePaths.Contains(d.Path));
                }
            }
        }

        /// <summary>
        /// Gets the KeepAlive interval in milliseconds
        /// </summary>
        public int KeepAliveIntervalMs => _keepAliveIntervalMs;

        /// <summary>
        /// Gets the last time KeepAlive was sent
        /// </summary>
        public DateTime LastKeepAliveTime => _lastKeepAliveTime;

        #region Device Path Filter Management

        /// <summary>
        /// Set device paths to filter operations to specific devices
        /// </summary>
        /// <param name="devicePaths">Collection of device paths to include in operations</param>
        /// <param name="enableFilter">Whether to enable the filter (default: true)</param>
        public void SetDevicePathFilter(IEnumerable<string> devicePaths, bool enableFilter = true)
        {
            lock (_lockObject)
            {
                _filteredDevicePaths.Clear();
                
                foreach (var path in devicePaths.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    _filteredDevicePaths.Add(path);
                }
                
                _useDevicePathFilter = enableFilter && _filteredDevicePaths.Any();
                
                Logger.Info($"Device path filter {(enableFilter ? "enabled" : "disabled")} with {_filteredDevicePaths.Count} paths");
                
                DeviceFilterChanged?.Invoke(this, new DeviceFilterEventArgs(_useDevicePathFilter, _filteredDevicePaths.ToList()));
            }
        }

        /// <summary>
        /// Set a single device path to filter operations
        /// </summary>
        /// <param name="devicePath">Device path to include in operations</param>
        /// <param name="enableFilter">Whether to enable the filter (default: true)</param>
        public void SetDevicePathFilter(string devicePath, bool enableFilter = true)
        {
            SetDevicePathFilter(new[] { devicePath }, enableFilter);
        }

        /// <summary>
        /// Add device paths to the existing filter
        /// </summary>
        /// <param name="devicePaths">Device paths to add</param>
        public void AddDevicePathsToFilter(IEnumerable<string> devicePaths)
        {
            lock (_lockObject)
            {
                var added = 0;
                foreach (var path in devicePaths.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    if (_filteredDevicePaths.Add(path))
                    {
                        added++;
                    }
                }
                
                if (added > 0)
                {
                    Logger.Info($"Added {added} device paths to filter. Total: {_filteredDevicePaths.Count}");
                    DeviceFilterChanged?.Invoke(this, new DeviceFilterEventArgs(_useDevicePathFilter, _filteredDevicePaths.ToList()));
                }
            }
        }

        /// <summary>
        /// Add a single device path to the existing filter
        /// </summary>
        /// <param name="devicePath">Device path to add</param>
        public void AddDevicePathToFilter(string devicePath)
        {
            AddDevicePathsToFilter(new[] { devicePath });
        }

        /// <summary>
        /// Remove device paths from the filter
        /// </summary>
        /// <param name="devicePaths">Device paths to remove</param>
        public void RemoveDevicePathsFromFilter(IEnumerable<string> devicePaths)
        {
            lock (_lockObject)
            {
                var removed = 0;
                foreach (var path in devicePaths)
                {
                    if (_filteredDevicePaths.Remove(path))
                    {
                        removed++;
                    }
                }
                
                if (removed > 0)
                {
                    Logger.Info($"Removed {removed} device paths from filter. Remaining: {_filteredDevicePaths.Count}");
                    DeviceFilterChanged?.Invoke(this, new DeviceFilterEventArgs(_useDevicePathFilter, _filteredDevicePaths.ToList()));
                }
            }
        }

        /// <summary>
        /// Remove a single device path from the filter
        /// </summary>
        /// <param name="devicePath">Device path to remove</param>
        public void RemoveDevicePathFromFilter(string devicePath)
        {
            RemoveDevicePathsFromFilter(new[] { devicePath });
        }

        /// <summary>
        /// Clear all device path filters
        /// Note: This is the only way to remove device paths from the filter.
        /// Device paths are NOT automatically removed when devices disconnect,
        /// allowing filters to persist across device disconnections/reconnections.
        /// </summary>
        public void ClearDevicePathFilter()
        {
            lock (_lockObject)
            {
                var count = _filteredDevicePaths.Count;
                _filteredDevicePaths.Clear();
                _useDevicePathFilter = false;
                
                Logger.Info($"Cleared device path filter ({count} paths removed)");
                DeviceFilterChanged?.Invoke(this, new DeviceFilterEventArgs(false, new List<string>()));
            }
        }

        /// <summary>
        /// Enable or disable the device path filter without changing the paths
        /// </summary>
        /// <param name="enable">Whether to enable the filter</param>
        public void EnableDevicePathFilter(bool enable)
        {
            lock (_lockObject)
            {
                if (_useDevicePathFilter != enable)
                {
                    _useDevicePathFilter = enable && _filteredDevicePaths.Any();
                    Logger.Info($"Device path filter {(enable ? "enabled" : "disabled")}");
                    DeviceFilterChanged?.Invoke(this, new DeviceFilterEventArgs(_useDevicePathFilter, _filteredDevicePaths.ToList()));
                }
            }
        }

        /// <summary>
        /// Get device paths that would be included in operations (considering current filter)
        /// </summary>
        /// <returns>List of device paths that operations would target</returns>
        public List<string> GetOperationTargetDevicePaths()
        {
            lock (_lockObject)
            {
                if (!_useDevicePathFilter)
                {
                    return ConnectedDevices.Select(d => d.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
                }
                
                return ConnectedDevices
                    .Where(d => !string.IsNullOrEmpty(d.Path) && _filteredDevicePaths.Contains(d.Path))
                    .Select(d => d.Path)
                    .ToList();
            }
        }

        /// <summary>
        /// Get device info objects that would be included in operations (considering current filter)
        /// </summary>
        /// <returns>List of DeviceInfo objects that operations would target</returns>
        public List<DeviceInfo> GetOperationTargetDevices()
        {
            lock (_lockObject)
            {
                if (!_useDevicePathFilter)
                {
                    return ConnectedDevices.ToList();
                }
                
                return ConnectedDevices
                    .Where(d => !string.IsNullOrEmpty(d.Path) && _filteredDevicePaths.Contains(d.Path))
                    .ToList();
            }
        }

        /// <summary>
        /// Check if a device path is included in the current filter
        /// </summary>
        /// <param name="devicePath">Device path to check</param>
        /// <returns>True if the device would be included in operations</returns>
        public bool IsDevicePathIncluded(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return false;
            
            lock (_lockObject)
            {
                return !_useDevicePathFilter || _filteredDevicePaths.Contains(devicePath);
            }
        }

        #endregion

        #region Modified Operation Methods (with filtering support)

        /// <summary>
        /// Execute command on devices (respects device path filter)
        /// </summary>
        private async Task<Dictionary<string, bool>> ExecuteOnFilteredDevicesAsync<T>(
            Func<DisplayController, Task<T>> command,
            TimeSpan? timeout = null,
            bool logInfo = true)
        {
            if (_multiDeviceManager == null)
            {
                throw new InvalidOperationException("HID Device Service is not initialized");
            }

            var targetPaths = GetOperationTargetDevicePaths();
            var results = new Dictionary<string, bool>();

            if (!targetPaths.Any())
            {
                Logger.Warn("No devices match the current filter for operation");
                return results;
            }

            if (logInfo)
            {
                Logger.Info($"Executing command on {targetPaths.Count} filtered devices");
            }

            foreach (var devicePath in targetPaths)
            {
                var controller = _multiDeviceManager.GetController(devicePath);
                if (controller != null)
                {
                    try
                    {
                        var task = command(controller);
                        if (timeout.HasValue)
                        {
                            await task.WaitAsync(timeout.Value);
                        }
                        else
                        {
                            await task;
                        }
                        results[devicePath] = true;
                        if (logInfo)
                        {
                            Logger.Info($"Command succeeded for device: {devicePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Command failed for device {devicePath}: {ex.Message}");
                        results[devicePath] = false;
                        
                        // Fire device error event
                        var deviceInfo = ConnectedDevices.FirstOrDefault(d => d.Path == devicePath);
                        if (deviceInfo != null)
                        {
                            DeviceError?.Invoke(this, new DeviceErrorEventArgs(deviceInfo, ex));
                        }
                    }
                }
                else
                {
                    Logger.Warn($"Controller not found for device: {devicePath}");
                    results[devicePath] = false;
                }
            }

            var successCount = results.Values.Count(r => r);
            if (logInfo)
            {
                Logger.Info($"Command completed on {successCount}/{targetPaths.Count} filtered devices");
            }

            return results;
        }

        /// <summary>
        /// Execute command with response on devices (respects device path filter)
        /// </summary>
        private async Task<Dictionary<string, bool>> ExecuteWithResponseOnFilteredDevicesAsync(
            Func<DisplayController, Task<bool>> command,
            TimeSpan? timeout = null,
            bool logInfo = true)
        {
            if (_multiDeviceManager == null)
            {
                throw new InvalidOperationException("HID Device Service is not initialized");
            }

            var targetPaths = GetOperationTargetDevicePaths();
            var results = new Dictionary<string, bool>();

            if (!targetPaths.Any())
            {
                Logger.Warn("No devices match the current filter for operation");
                return results;
            }

            if (logInfo)
            {
                Logger.Info($"Executing command with response on {targetPaths.Count} filtered devices");
            }

            foreach (var devicePath in targetPaths)
            {
                var controller = _multiDeviceManager.GetController(devicePath);
                if (controller != null)
                {
                    try
                    {
                        var task = command(controller);
                        bool result;
                        if (timeout.HasValue)
                        {
                            result = await task.WaitAsync(timeout.Value);
                        }
                        else
                        {
                            result = await task;
                        }
                        results[devicePath] = result;
                        if (logInfo)
                        {
                            Logger.Info($"Command with response succeeded for device: {devicePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Command with response failed for device {devicePath}: {ex.Message}");
                        results[devicePath] = false;
                        
                        // Fire device error event
                        var deviceInfo = ConnectedDevices.FirstOrDefault(d => d.Path == devicePath);
                        if (deviceInfo != null)
                        {
                            DeviceError?.Invoke(this, new DeviceErrorEventArgs(deviceInfo, ex));
                        }
                    }
                }
                else
                {
                    Logger.Warn($"Controller not found for device: {devicePath}");
                    results[devicePath] = false;
                }
            }

            var successCount = results.Values.Count(r => r);
            if (logInfo)
            {
                Logger.Info($"Command with response completed on {successCount}/{targetPaths.Count} filtered devices");
            }

            return results;
        }

        #endregion

        /// <summary>
        /// Initialize the HID device service
        /// </summary>
        /// <param name="vendorId">Vendor ID filter (default: 0x2516)</param>
        /// <param name="productId">Product ID filter (default: 0x0228)</param>
        /// <param name="usagePage">Usage Page filter (default: 0xFFFF)</param>
        public async Task InitializeAsync(ushort vendorId = 0x2516, ushort productId = 0x0228, ushort usagePage = 0xFFFF)
        {
            if (IsInitialized)
            {
                Logger.Warn("HidDeviceService is already initialized");
                return;
            }

            try
            {
                Logger.Info($"Initializing HID Device Service (VID: 0x{vendorId:X4}, PID: 0x{productId:X4})");

                _multiDeviceManager = new MultiDeviceManager(vendorId, productId, usagePage);

                // Subscribe to device manager events
                _multiDeviceManager.ControllerAdded += OnControllerAdded;
                _multiDeviceManager.ControllerRemoved += OnControllerRemoved;
                _multiDeviceManager.ControllerError += OnControllerError;

                // Start monitoring in a background task
                await Task.Run(() =>
                {
                    _multiDeviceManager.StartMonitoring();
                });

                // Wait a moment for initial device detection
                await Task.Delay(1000);

                IsInitialized = true;
                Logger.Info($"HID Device Service initialized successfully. Found {ConnectedDeviceCount} devices");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize HID Device Service", ex);
                throw;
            }
        }

        #region Device Information APIs

        /// <summary>
        /// Get device firmware and hardware information (respects device path filter)
        /// </summary>
        /// <returns>Dictionary of device paths and device firmware info</returns>
        public async Task<Dictionary<string, DeviceFWInfo?>> GetDeviceFirmwareInfoAsync()
        {
            var results = new Dictionary<string, DeviceFWInfo?>();
            var targetPaths = GetOperationTargetDevicePaths();

            foreach (var devicePath in targetPaths)
            {
                var controller = _multiDeviceManager?.GetController(devicePath);
                if (controller != null)
                {
                    try
                    {
                        results[devicePath] = controller.DeviceFWInfo;
                        Logger.Info($"Device firmware info retrieved for device: {devicePath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to get device firmware info from device {devicePath}: {ex.Message}");
                        results[devicePath] = null;
                    }
                }
                else
                {
                    Logger.Warn($"Controller not found for device: {devicePath}");
                    results[devicePath] = null;
                }
            }

            return results;
        }

        /// <summary>
        /// Get device capabilities (respects device path filter)
        /// </summary>
        /// <returns>Dictionary of device paths and device capabilities</returns>
        public async Task<Dictionary<string, DisplayCtrlCapabilities?>> GetDeviceCapabilitiesAsync()
        {
            var results = new Dictionary<string, DisplayCtrlCapabilities?>();
            var targetPaths = GetOperationTargetDevicePaths();

            foreach (var devicePath in targetPaths)
            {
                var controller = _multiDeviceManager?.GetController(devicePath);
                if (controller != null)
                {
                    try
                    {
                        results[devicePath] = controller.Capabilities;
                        Logger.Info($"Device capabilities retrieved for device: {devicePath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to get device capabilities from device {devicePath}: {ex.Message}");
                        results[devicePath] = null;
                    }
                }
                else
                {
                    Logger.Warn($"Controller not found for device: {devicePath}");
                    results[devicePath] = null;
                }
            }

            return results;
        }

        #endregion

        #region Display Control APIs

        /// <summary>
        /// Set real-time display mode on devices (respects device path filter)
        /// </summary>
        /// <param name="enable">True to enable real-time display mode</param>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> SetRealTimeDisplayAsync(bool enable)
        {
            try
            {
                Logger.Info($"Setting real-time display mode to {enable} on filtered devices");
                
                var results = await ExecuteOnFilteredDevicesAsync(async controller =>
                {
                    var res = await controller.SendCmdRealTimeDisplayWithResponse(enable);
                    return true;
                });
                
                // Update the internal state and manage timer only if any device succeeded
                if (results.Values.Any(r => r))
                {
                    IsRealTimeDisplayEnabled = enable;
                }
                
                var successCount = results.Values.Count(r => r);
                Logger.Info($"Real-time display mode set to {enable} on {successCount}/{results.Count} devices");
                
                return results;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to set real-time display mode: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Set display in sleep mode on devices (respects device path filter)
        /// </summary>
        /// <param name="enable">True to enable sleep mode</param>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> SetDisplayInSleepAsync(bool enable)
        {
            return await ExecuteWithResponseOnFilteredDevicesAsync(async controller =>
            {
                var response = await controller.SendCmdDisplayInSleepWithResponse(enable);
                return response?.IsSuccess == true;
            });
        }

        /// <summary>
        /// Set brightness on devices (respects device path filter)
        /// </summary>
        /// <param name="brightness">Brightness value (0-100)</param>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> SetBrightnessAsync(int brightness)
        {
            return await ExecuteOnFilteredDevicesAsync(async controller =>
            {
                await controller.SendCmdBrightnessWithResponse(brightness);
                return true;
            });
        }

        /// <summary>
        /// Set rotation on devices (respects device path filter)
        /// </summary>
        /// <param name="degrees">Rotation degrees</param>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> SetRotationAsync(int degrees)
        {
            return await ExecuteOnFilteredDevicesAsync(async controller =>
            {
                await controller.SendCmdRotateWithResponse(degrees);
                return true;
            });
        }

        #endregion

        #region Suspend Media APIs

        /// <summary>
        /// Send multiple suspend files to devices (respects device path filter)
        /// </summary>
        /// <param name="filePaths">List of file paths to send</param>
        /// <param name="startingTransferId">Starting transfer ID (default: 1)</param>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> SendMultipleSuspendFilesAsync(List<string> filePaths, byte startingTransferId = 1)
        {
            return await ExecuteOnFilteredDevicesAsync(async controller =>
            {
                return await controller.SendMultipleSuspendFilesWithResponse(filePaths, startingTransferId);
            }, logInfo: false);
        }

        /// <summary>
        /// Set suspend mode (disable real-time display) on devices (respects device path filter)
        /// </summary>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> SetSuspendModeAsync()
        {
            return await ExecuteOnFilteredDevicesAsync(async controller =>
            {
                return await controller.SetSuspendModeWithResponse();
            });
        }

        /// <summary>
        /// Delete suspend files on devices (respects device path filter)
        /// </summary>
        /// <param name="fileName">File name to delete (default: "all")</param>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> DeleteSuspendFilesAsync(string fileName = "all")
        {
            return await ExecuteOnFilteredDevicesAsync(async controller =>
            {
                return await controller.DeleteSuspendFilesWithResponse(fileName);
            });
        }

        #endregion

        #region Device Control APIs

        /// <summary>
        /// Reboot devices (respects device path filter)
        /// </summary>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> RebootDevicesAsync()
        {
            return await ExecuteOnFilteredDevicesAsync(async controller =>
            {
                await Task.Run(() => controller.Reboot());
                return true;
            });
        }

        /// <summary>
        /// Factory reset devices (respects device path filter)
        /// </summary>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> FactoryResetDevicesAsync()
        {
            return await ExecuteOnFilteredDevicesAsync(async controller =>
            {
                await Task.Run(() => controller.FactoryReset());
                return true;
            });
        }

        /// <summary>
        /// Update firmware on devices (respects device path filter)
        /// </summary>
        /// <param name="filePath">Path to firmware file</param>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> UpdateFirmwareAsync(string filePath)
        {
            return await ExecuteOnFilteredDevicesAsync(async controller =>
            {
                return await controller.FirmwareUpgradeWithFileAndResponse(filePath);
            });
        }

        #endregion

        /// <summary>
        /// Transfer file to devices (respects device path filter)
        /// </summary>
        /// <param name="filePath">Path to file to transfer</param>
        /// <param name="transferId">0-59, every call need make sure the value is not same </param>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> TransferFileAsync(string filePath, byte transferId)
        {
            return await ExecuteOnFilteredDevicesAsync(async controller =>
            {
                controller.SendFileFromDisk(filePath, transferId: transferId);
                return true;
            }, logInfo: false);
        }

        /// <summary>
        /// Transfer jpeg format data to devices (respects device path filter)
        /// </summary>
        /// <param name="jpegData">Path to file to transfer</param>
        /// <param name="transferId">0-59, every call need make sure the value is not same </param>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> TransferDataAsync(byte[] jpegData, byte transferId)
        {
            return await ExecuteOnFilteredDevicesAsync(async controller =>
            {
                controller.SendFileTransfer(jpegData, fileType:1, transferId: transferId);
                return true;
            }, logInfo: false);
        }

        /// <summary>
        /// Send KeepAlive to devices (respects device path filter)
        /// </summary>
        /// <param name="timestamp">Optional timestamp (uses current time if not specified)</param>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> SendKeepAliveAsync(long? timestamp = null)
        {
            var actualTimestamp = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _lastKeepAliveTime = DateTime.Now;
            
            var results = await ExecuteOnFilteredDevicesAsync(async controller =>
            {
                await controller.SendCmdKeepAliveWithResponse(actualTimestamp);
                return true;
            }, logInfo: false);
            
            var successCount = results.Values.Count(r => r);
            KeepAliveSent?.Invoke(this, new KeepAliveEventArgs(actualTimestamp, successCount, results.Count));
            
            return results;
        }

        /// <summary>
        /// Get device status from devices (respects device path filter)
        /// </summary>
        /// <returns>Dictionary of device paths and device status (null if failed)</returns>
        public async Task<Dictionary<string, DeviceStatus?>> GetDeviceStatusAsync()
        {
            var results = new Dictionary<string, DeviceStatus?>();
            var targetPaths = GetOperationTargetDevicePaths();

            foreach (var devicePath in targetPaths)
            {
                var controller = _multiDeviceManager?.GetController(devicePath);
                if (controller != null)
                {
                    try
                    {
                        var status = await controller.GetDeviceStatus();
                        results[devicePath] = status;
                        Logger.Info($"Status retrieved for device: {devicePath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to get status from device {devicePath}: {ex.Message}");
                        results[devicePath] = null;
                    }
                }
                else
                {
                    Logger.Warn($"Controller not found for device: {devicePath}");
                    results[devicePath] = null;
                }
            }

            return results;
        }

        /// <summary>
        /// Execute custom command on devices (respects device path filter)
        /// </summary>
        /// <typeparam name="T">Return type of the command</typeparam>
        /// <param name="command">Command to execute</param>
        /// <param name="timeout">Optional timeout for the operation</param>
        /// <returns>Dictionary mapping device paths to operation results</returns>
        public async Task<Dictionary<string, bool>> ExecuteOnAllDevicesAsync<T>(
            Func<DisplayController, Task<T>> command,
            TimeSpan? timeout = null)
        {
            return await ExecuteOnFilteredDevicesAsync(command, timeout);
        }

        /// <summary>
        /// Start the KeepAlive timer for real-time display mode
        /// </summary>
        private void StartKeepAliveTimer()
        {
            if (_keepAliveTimer != null)
            {
                StopKeepAliveTimer();
            }

            Debug.WriteLine($"Starting KeepAlive timer with {_keepAliveIntervalMs}ms interval");
            
            _keepAliveTimer = new Timer(
                callback: async _ => await SendKeepAliveToFilteredDevices(),
                state: null,
                dueTime: TimeSpan.FromMilliseconds(_keepAliveIntervalMs),
                period: TimeSpan.FromMilliseconds(_keepAliveIntervalMs)
            );
        }

        /// <summary>
        /// Stop the KeepAlive timer
        /// </summary>
        private void StopKeepAliveTimer()
        {
            if (_keepAliveTimer != null)
            {
                Debug.WriteLine("Stopping KeepAlive timer");
                _keepAliveTimer.Dispose();
                _keepAliveTimer = null;
            }
        }

        /// <summary>
        /// Send KeepAlive command to filtered devices
        /// </summary>
        private async Task SendKeepAliveToFilteredDevices()
        {
            if (_multiDeviceManager == null || !IsRealTimeDisplayEnabled)
            {
                return;
            }

            try
            {
                var results = await SendKeepAliveAsync();
                var successCount = results.Values.Count(r => r);
                Logger.Info($"Automatic KeepAlive sent to {successCount}/{results.Count} filtered devices");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to send automatic KeepAlive: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all active device controllers
        /// </summary>
        public List<DisplayController> GetActiveControllers()
        {
            return _multiDeviceManager?.GetActiveControllers() ?? new List<DisplayController>();
        }

        /// <summary>
        /// Get controller by device path
        /// </summary>
        public DisplayController? GetController(string devicePath)
        {
            return _multiDeviceManager?.GetController(devicePath);
        }

        /// <summary>
        /// Get controller by serial number
        /// </summary>
        public DisplayController? GetControllerBySerial(string serialNumber)
        {
            return _multiDeviceManager?.GetControllerBySerial(serialNumber);
        }

        /// <summary>
        /// Refresh device list manually
        /// </summary>
        public void RefreshDeviceList()
        {
            // The device monitor automatically handles device detection
            // This method can be used to trigger a manual refresh if needed
            Logger.Info("Manual device list refresh requested");
        }

        private void OnControllerAdded(object? sender, DeviceControllerEventArgs e)
        {
            lock (_lockObject)
            {
                var existingDevice = ConnectedDevices.FirstOrDefault(d => d.Path == e.Device.Path);
                if (existingDevice == null)
                {
                    // Use Application.Current.Dispatcher to ensure UI thread safety
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        ConnectedDevices.Add(e.Device);
                    });

                    Logger.Info($"Device connected: {e.Device.ProductString} (Serial: {e.Device.SerialNumber})");
                    DeviceConnected?.Invoke(this, new DeviceEventArgs(e.Device));
                    DeviceListChanged?.Invoke(this, new DeviceEventArgs(e.Device));

                    // if real-time display is enabled, start keepalive
                    if (IsRealTimeDisplayEnabled && ConnectedDeviceCount > 0)
                    {
                        StartKeepAliveTimer();
                    }
                }
            }
        }

        private void OnControllerRemoved(object? sender, DeviceControllerEventArgs e)
        {
            lock (_lockObject)
            {
                var deviceToRemove = ConnectedDevices.FirstOrDefault(d => d.Path == e.Device.Path);
                if (deviceToRemove != null)
                {
                    // Use Application.Current.Dispatcher to ensure UI thread safety
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        ConnectedDevices.Remove(deviceToRemove);
                    });

                    // Keep _filteredDevicePaths intact - don't remove disconnected devices from filter
                    // This allows the filter to persist across device disconnections/reconnections

                    Logger.Info($"Device disconnected: {e.Device.ProductString} (Serial: {e.Device.SerialNumber})");
                    DeviceDisconnected?.Invoke(this, new DeviceEventArgs(e.Device));
                    DeviceListChanged?.Invoke(this, new DeviceEventArgs(e.Device));

                    // if not device connected, stop keepalive
                    if (ConnectedDeviceCount == 0)
                    {
                        StopKeepAliveTimer();
                        IsRealTimeDisplayEnabled = false;
                    }
                }
            }
        }

        private void OnControllerError(object? sender, ControllerErrorEventArgs e)
        {
            Logger.Error($"Device error for {e.Device.ProductString}: {e.Exception.Message}", e.Exception);
            DeviceError?.Invoke(this, new DeviceErrorEventArgs(e.Device, e.Exception));
        }

        /// <summary>
        /// Shutdown the service
        /// </summary>
        public void Shutdown()
        {
            if (!IsInitialized) return;

            try
            {
                Logger.Info("Shutting down HID Device Service");
                
                // Stop KeepAlive timer
                StopKeepAliveTimer();
                IsRealTimeDisplayEnabled = false;
                
                // Clear filters
                ClearDevicePathFilter();
                
                _multiDeviceManager?.StopMonitoring();
                
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    ConnectedDevices.Clear();
                });

                IsInitialized = false;
                Logger.Info("HID Device Service shutdown completed");
            }
            catch (Exception ex)
            {
                Logger.Error("Error during HID Device Service shutdown", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            Shutdown();
            _multiDeviceManager?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Event arguments for device events
    /// </summary>
    public class DeviceEventArgs : EventArgs
    {
        public DeviceInfo Device { get; }
        public DateTime Timestamp { get; }

        public DeviceEventArgs(DeviceInfo device)
        {
            Device = device;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for device error events
    /// </summary>
    public class DeviceErrorEventArgs : EventArgs
    {
        public DeviceInfo Device { get; }
        public Exception Exception { get; }
        public DateTime Timestamp { get; }

        public DeviceErrorEventArgs(DeviceInfo device, Exception exception)
        {
            Device = device;
            Exception = exception;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for real-time display mode changes
    /// </summary>
    public class RealTimeDisplayEventArgs : EventArgs
    {
        public bool IsEnabled { get; }
        public DateTime Timestamp { get; }

        public RealTimeDisplayEventArgs(bool isEnabled)
        {
            IsEnabled = isEnabled;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for KeepAlive events
    /// </summary>
    public class KeepAliveEventArgs : EventArgs
    {
        public long Timestamp { get; }
        public int SuccessfulDevices { get; }
        public int TotalDevices { get; }
        public DateTime SentTime { get; }

        public KeepAliveEventArgs(long timestamp, int successfulDevices, int totalDevices)
        {
            Timestamp = timestamp;
            SuccessfulDevices = successfulDevices;
            TotalDevices = totalDevices;
            SentTime = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for device filter changes
    /// </summary>
    public class DeviceFilterEventArgs : EventArgs
    {
        public bool IsFilterEnabled { get; }
        public IReadOnlyList<string> FilteredPaths { get; }
        public DateTime Timestamp { get; }

        public DeviceFilterEventArgs(bool isFilterEnabled, IList<string> filteredPaths)
        {
            IsFilterEnabled = isFilterEnabled;
            FilteredPaths = filteredPaths.ToList().AsReadOnly();
            Timestamp = DateTime.Now;
        }
    }
}


/// Usage Examples:

//var hidService = ServiceLocator.HidDeviceService;

//// Filter to specific devices
//var devicePaths = new[] { "path1", "path2" };
//hidService.SetDevicePathFilter(devicePaths);

//// All operations now only affect filtered devices
//await hidService.SetBrightnessAsync(50);  // Only affects path1 and path2
//await hidService.SetRotationAsync(90);    // Only affects path1 and path2

//// Add another device to filter
//hidService.AddDevicePathToFilter("path3");

//// Check filter status
//Debug.WriteLine($"Filtered devices: {hidService.FilteredDeviceCount}");
//Debug.WriteLine($"Filter enabled: {hidService.IsDevicePathFilterEnabled}");

//// Temporarily disable filter (operations affect all devices)
//hidService.EnableDevicePathFilter(false);
//await hidService.SetBrightnessAsync(100); // Affects ALL devices

//// Re-enable filter
//hidService.EnableDevicePathFilter(true);
//await hidService.SetBrightnessAsync(50);  // Back to filtered devices only

//// Clear filter (operations affect all devices)
//hidService.ClearDevicePathFilter();
