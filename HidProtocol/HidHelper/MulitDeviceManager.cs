using HID.DisplayController;
using HidApi;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace HID.DisplayController
{
    /// <summary>
    /// Manages multiple HID display controllers simultaneously
    /// </summary>
    public class MultiDeviceManager : IDisposable
    {
        private readonly HidDeviceMonitor _deviceMonitor;
        private readonly ConcurrentDictionary<string, DisplayController> _activeControllers;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        /// <summary>
        /// Event fired when a device controller is successfully created
        /// </summary>
        public event EventHandler<DeviceControllerEventArgs>? ControllerAdded;

        /// <summary>
        /// Event fired when a device controller is removed
        /// </summary>
        public event EventHandler<DeviceControllerEventArgs>? ControllerRemoved;

        /// <summary>
        /// Event fired when a controller operation fails
        /// </summary>
        public event EventHandler<ControllerErrorEventArgs>? ControllerError;

        public MultiDeviceManager(ushort vendorId = 0x2516, ushort productId = 0x0228, ushort usagePage = 0xFFFF)
        {
            _activeControllers = new ConcurrentDictionary<string, DisplayController>();
            _deviceMonitor = new HidDeviceMonitor();

            // Set up device monitoring for specific VID/PID
            _deviceMonitor.SetDeviceFilter(vendorId, productId, usagePage);

            // Wire up events
            _deviceMonitor.DeviceConnected += OnDeviceConnected;
            _deviceMonitor.DeviceDisconnected += OnDeviceDisconnected;
        }

        /// <summary>
        /// Start monitoring and managing multiple devices
        /// </summary>
        public void StartMonitoring()
        {
            Console.WriteLine("[MultiDeviceManager] Starting multi-device monitoring...");

            // Start monitoring for new devices
            _deviceMonitor.StartMonitoring();

            //Thread.Sleep(1000); // Give some time for initial detection
            // Initialize controllers for already connected devices
            InitializeExistingDevices();
        }

        /// <summary>
        /// Stop monitoring and dispose all controllers
        /// </summary>
        public void StopMonitoring()
        {
            Debug.WriteLine("[MultiDeviceManager] Stopping multi-device monitoring...");

            _deviceMonitor.StopMonitoring();

            // Dispose all active controllers
            lock (_lockObject)
            {
                foreach (var controller in _activeControllers.Values)
                {
                    try
                    {
                        controller.StopResponseListener();
                        controller.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error disposing controller: {ex.Message}");
                    }
                }
                _activeControllers.Clear();
            }
        }

        /// <summary>
        /// Get all active device controllers
        /// </summary>
        public List<DisplayController> GetActiveControllers()
        {
            return _activeControllers.Values.ToList();
        }

        /// <summary>
        /// Get controller by device path
        /// </summary>
        public DisplayController? GetController(string devicePath)
        {
            _activeControllers.TryGetValue(devicePath, out var controller);
            return controller;
        }

        /// <summary>
        /// Get controller by serial number
        /// </summary>
        public DisplayController? GetControllerBySerial(string serialNumber)
        {
            return _activeControllers.Values.FirstOrDefault(c =>
                string.Equals(c.DeviceFWInfo?.ToString(), serialNumber, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Execute command on all active controllers
        /// </summary>
        public async Task<Dictionary<string, bool>> ExecuteOnAllDevices<T>(
            Func<DisplayController, Task<T>> command,
            TimeSpan? timeout = null)
        {
            var results = new Dictionary<string, bool>();
            var controllers = GetActiveControllers();

            Console.WriteLine($"Executing command on {controllers.Count} devices...");

            var tasks = controllers.Select(async controller =>
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
                    return new { Controller = controller, Success = true };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Command failed for device: {ex.Message}");
                    return new { Controller = controller, Success = false };
                }
            });

            var completedTasks = await Task.WhenAll(tasks);

            foreach (var result in completedTasks)
            {
                var devicePath = GetDevicePathForController(result.Controller);
                results[devicePath ?? "unknown"] = result.Success;
            }

            return results;
        }

        /// <summary>
        /// Execute command on specific devices by serial numbers
        /// </summary>
        public async Task<Dictionary<string, bool>> ExecuteOnSpecificDevices<T>(
            IEnumerable<string> serialNumbers,
            Func<DisplayController, Task<T>> command,
            TimeSpan? timeout = null)
        {
            var results = new Dictionary<string, bool>();

            foreach (var serialNumber in serialNumbers)
            {
                var controller = GetControllerBySerial(serialNumber);
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
                        results[serialNumber] = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Command failed for device {serialNumber}: {ex.Message}");
                        results[serialNumber] = false;
                    }
                }
                else
                {
                    Console.WriteLine($"Device with serial {serialNumber} not found");
                    results[serialNumber] = false;
                }
            }

            return results;
        }

        /// <summary>
        /// Set brightness on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SetBrightnessOnAllDevices(int brightness)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdBrightnessWithResponse(brightness);
                return true;
            });
        }

        /// <summary>
        /// Set rotation on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SetRotationOnAllDevices(int degrees)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdRotateWithResponse(degrees);
                return true;
            });
        }

        /// <summary>
        /// Set display in sleep mode on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SetDisplayInSleepOnAllDevices(bool enable)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdDisplayInSleepWithResponse(enable);
                return true;
            });
        }

        /// <summary>
        /// Set suspend on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SetSuspendOnAllDevices(string fileName, int fileSize)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdSetSuspendWithResponse(fileName, fileSize);
                return true;
            });
        }

        /// <summary>
        /// Send suspend completed on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SendSuspendCompletedOnAllDevices(string fileName, string md5)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdSuspendCompletedWithResponse(fileName, md5);
                return true;
            });
        }

        /// <summary>
        /// Delete suspend on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> DeleteSuspendOnAllDevices(string fileName = "all")
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdDeleteSuspendWithResponse(fileName);
                return true;
            });
        }

        /// <summary>
        /// Send keep alive on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SendKeepAliveOnAllDevices(long timestamp)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdKeepAliveWithResponse(timestamp);
                return true;
            });
        }

        /// <summary>
        /// Set keep alive timer on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SetKeepAliveTimerOnAllDevices(int value)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdSetKeepAliveTimerWithResponse(value);
                return true;
            });
        }

        /// <summary>
        /// Read keep alive timer from all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> ReadKeepAliveTimerFromAllDevices()
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdReadKeepAliveTimerWithResponse();
                return true;
            });
        }

        /// <summary>
        /// Read max suspend media from all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> ReadMaxSuspendMediaFromAllDevices()
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdReadMaxSuspendMediaWithResponse();
                return true;
            });
        }

        /// <summary>
        /// Set background on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SetBackgroundOnAllDevices(string fileName, int fileSize)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdSetBackgroundWithResponse(fileName, fileSize);
                return true;
            });
        }

        /// <summary>
        /// Send background completed on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SendBackgroundCompletedOnAllDevices(string fileName, string md5)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdBackgroundCompletedWithResponse(fileName, md5);
                return true;
            });
        }

        /// <summary>
        /// Set real-time display on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SetRealTimeDisplayOnAllDevices(bool enable)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdRealTimeDisplayWithResponse(enable);
                return true;
            });
        }

        /// <summary>
        /// Read rotated angle from all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> ReadRotatedAngleFromAllDevices()
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdReadRotatedAngleWithResponse();
                return true;
            });
        }

        /// <summary>
        /// Read brightness from all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> ReadBrightnessFromAllDevices()
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdReadBrightnessWithResponse();
                return true;
            });
        }

        /// <summary>
        /// Send firmware upgrade on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SendFirmwareUpgradeOnAllDevices(string fileName, int fileSize)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdFirmwareUpgradeWithResponse(fileName, fileSize);
                return true;
            });
        }

        /// <summary>
        /// Send firmware upgrade completed on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SendFirmwareUpgradeCompletedOnAllDevices(string fileName, string md5)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdFirmwareUpgradeCompletedWithResponse(fileName, md5);
                return true;
            });
        }

        /// <summary>
        /// Set OSD on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SetOsdOnAllDevices(string fileName, int fileSize)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdSetOsdWithResponse(fileName, fileSize);
                return true;
            });
        }

        /// <summary>
        /// Send OSD completed on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SendOsdCompletedOnAllDevices(string fileName, string md5)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdOsdCompletedWithResponse(fileName, md5);
                return true;
            });
        }

        /// <summary>
        /// Set powerup media on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SetPowerupMediaOnAllDevices(string fileName, int fileSize)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdSetPowerupMediaWithResponse(fileName, fileSize);
                return true;
            });
        }

        /// <summary>
        /// Send powerup media completed on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SendPowerupMediaCompletedOnAllDevices(string fileName, string md5)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdPowerupMediaCompletedWithResponse(fileName, md5);
                return true;
            });
        }

        /// <summary>
        /// Set device serial number on all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> SetDeviceSerialNumberOnAllDevices(string serialNumber)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdSetDeviceSNWithResponse(serialNumber);
                return true;
            });
        }

        /// <summary>
        /// Get device serial number from all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> GetDeviceSerialNumberFromAllDevices()
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdGetDeviceSNWithResponse();
                return true;
            });
        }

        /// <summary>
        /// Set color SKU on all devices (string version)
        /// </summary>
        public async Task<Dictionary<string, bool>> SetColorSkuOnAllDevices(string color)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdSetColorSkuWithResponse(color);
                return true;
            });
        }

        /// <summary>
        /// Set color SKU on all devices (integer version)
        /// </summary>
        public async Task<Dictionary<string, bool>> SetColorSkuOnAllDevices(int colorValue)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdSetColorSkuWithResponse(colorValue);
                return true;
            });
        }

        /// <summary>
        /// Get color SKU from all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> GetColorSkuFromAllDevices()
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdGetColorSkuWithResponse();
                return true;
            });
        }

        /// <summary>
        /// Get status from all devices using the enhanced command
        /// </summary>
        public async Task<Dictionary<string, bool>> GetStatusWithResponseFromAllDevices()
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await controller.SendCmdGetStatusWithResponse();
                return true;
            });
        }

        /// <summary>
        /// Transfer file to all devices
        /// </summary>
        public async Task<Dictionary<string, bool>> TransferFileToAllDevices(string filePath)
        {
            return await ExecuteOnAllDevices(async controller =>
            {
                await Task.Run(() => controller.SendFileFromDisk(filePath, transferId: 1));
                return true;
            });
        }

        /// <summary>
        /// Get status from all devices
        /// </summary>
        public async Task<Dictionary<string, DeviceStatus?>> GetStatusFromAllDevices()
        {
            var results = new Dictionary<string, DeviceStatus?>();
            var controllers = GetActiveControllers();

            foreach (var controller in controllers)
            {
                try
                {
                    var status = await controller.GetDeviceStatus();
                    var devicePath = GetDevicePathForController(controller);
                    results[devicePath ?? "unknown"] = status;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get status from device: {ex.Message}");
                    var devicePath = GetDevicePathForController(controller);
                    results[devicePath ?? "unknown"] = null;
                }
            }

            return results;
        }

        private void InitializeExistingDevices()
        {
            var connectedDevices = _deviceMonitor.GetConnectedDevices();
            Console.WriteLine($"Found {connectedDevices.Count} already connected devices");

            foreach (var device in connectedDevices)
            {
                CreateControllerForDevice(device);
            }
        }

        private void OnDeviceConnected(object? sender, HidDeviceEventArgs e)
        {
            Console.WriteLine($"[MultiDeviceManager] Device connected: {e.Device.ProductString} (SN: {e.Device.SerialNumber})");
            CreateControllerForDevice(e.Device);
        }

        private void OnDeviceDisconnected(object? sender, HidDeviceEventArgs e)
        {
            Console.WriteLine($"[MultiDeviceManager] Device disconnected: {e.Device.ProductString} (SN: {e.Device.SerialNumber})");
            RemoveControllerForDevice(e.Device);
        }

        private void CreateControllerForDevice(DeviceInfo device)
        {
            try
            {
                // Skip if controller already exists
                if (_activeControllers.ContainsKey(device.Path))
                {
                    Console.WriteLine($"Controller already exists for device: {device.Path}");
                    return;
                }

                // Create new controller using device path
                var controller = new DisplayController(device.Path);
                // need call StartResponseListener first when send cmd wiith response.
                controller.StartResponseListener();

                if (_activeControllers.TryAdd(device.Path, controller))
                {
                    Console.WriteLine($"Successfully created controller for: {device.ProductString} (SN: {device.SerialNumber})");
                    ControllerAdded?.Invoke(this, new DeviceControllerEventArgs(controller, device));
                }
                else
                {
                    controller.StopResponseListener();
                    controller.Dispose();
                    Console.WriteLine($"Failed to add controller for device: {device.Path}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create controller for device {device.Path}: {ex.Message}");
                ControllerError?.Invoke(this, new ControllerErrorEventArgs(device, ex));
            }
        }

        private void RemoveControllerForDevice(DeviceInfo device)
        {
            if (_activeControllers.TryRemove(device.Path, out var controller))
            {
                try
                {
                    controller.StopResponseListener();
                    controller.Dispose();
                    Console.WriteLine($"Removed controller for: {device.ProductString} (SN: {device.SerialNumber})");
                    ControllerRemoved?.Invoke(this, new DeviceControllerEventArgs(controller, device));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error disposing controller for {device.Path}: {ex.Message}");
                }
            }
        }

        private string? GetDevicePathForController(DisplayController controller)
        {
            return _activeControllers.FirstOrDefault(kvp => kvp.Value == controller).Key;
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopMonitoring();
            _deviceMonitor.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Event arguments for device controller events
    /// </summary>
    public class DeviceControllerEventArgs : EventArgs
    {
        public DisplayController Controller { get; }
        public DeviceInfo Device { get; }

        public DeviceControllerEventArgs(DisplayController controller, DeviceInfo device)
        {
            Controller = controller;
            Device = device;
        }
    }

    /// <summary>
    /// Event arguments for controller error events
    /// </summary>
    public class ControllerErrorEventArgs : EventArgs
    {
        public DeviceInfo Device { get; }
        public Exception Exception { get; }

        public ControllerErrorEventArgs(DeviceInfo device, Exception exception)
        {
            Device = device;
            Exception = exception;
        }
    }
}