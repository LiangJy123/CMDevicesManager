using CMDevicesManager.Helper;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms; // Add this for SystemInformation

namespace CMDevicesManager.Services
{
    /// <summary>
    /// Service that monitors system power state changes and manages device sleep mode
    /// </summary>
    public class SystemSleepMonitorService : IDisposable
    {
        private readonly HidDeviceService _hidDeviceService;
        private bool _isMonitoring = false;
        private bool _disposed = false;

        /// <summary>
        /// Event fired when system enters sleep mode
        /// </summary>
        public event EventHandler<SystemSleepEventArgs>? SystemEnteringSleep;

        /// <summary>
        /// Event fired when system resumes from sleep mode
        /// </summary>
        public event EventHandler<SystemSleepEventArgs>? SystemResumingFromSleep;

        /// <summary>
        /// Event fired when device sleep mode is successfully set
        /// </summary>
        public event EventHandler<DeviceSleepModeEventArgs>? DeviceSleepModeChanged;

        /// <summary>
        /// Gets whether the service is currently monitoring system sleep events
        /// </summary>
        public bool IsMonitoring => _isMonitoring;

        /// <summary>
        /// Gets whether system sleep monitoring is enabled (controls whether devices are notified)
        /// </summary>
        public bool IsSystemSleepMonitoringEnabled { get; set; } = true;

        public SystemSleepMonitorService(HidDeviceService hidDeviceService)
        {
            _hidDeviceService = hidDeviceService ?? throw new ArgumentNullException(nameof(hidDeviceService));
        }

        /// <summary>
        /// Start monitoring system power state changes
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoring)
            {
                Logger.Warn("System sleep monitoring is already active");
                return;
            }

            try
            {
                Logger.Info("Starting system sleep monitoring");

                // Subscribe to system power mode changed events
                SystemEvents.PowerModeChanged += OnPowerModeChanged;

                _isMonitoring = true;
                Logger.Info("System sleep monitoring started successfully");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to start system sleep monitoring", ex);
                throw;
            }
        }

        /// <summary>
        /// Stop monitoring system power state changes
        /// </summary>
        public void StopMonitoring()
        {
            if (!_isMonitoring)
            {
                Logger.Warn("System sleep monitoring is not active");
                return;
            }

            try
            {
                Logger.Info("Stopping system sleep monitoring");

                // Unsubscribe from system power mode changed events
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;

                _isMonitoring = false;
                Logger.Info("System sleep monitoring stopped successfully");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to stop system sleep monitoring", ex);
            }
        }

        /// <summary>
        /// Handle system power mode changes
        /// </summary>
        private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (!IsSystemSleepMonitoringEnabled)
            {
                Logger.Info($"System power mode changed to {e.Mode}, but monitoring is disabled");
                return;
            }

            try
            {
                Logger.Info($"System power mode changed to: {e.Mode}");

                switch (e.Mode)
                {
                    case PowerModes.Suspend:
                        await HandleSystemEnteringSleep();
                        break;

                    case PowerModes.Resume:
                        await HandleSystemResumingFromSleep();
                        break;

                    case PowerModes.StatusChange:
                        Logger.Info("System power status changed (battery/AC)");
                        break;

                    default:
                        Logger.Info($"Unhandled power mode: {e.Mode}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling power mode change ({e.Mode})", ex);
            }
        }

        /// <summary>
        /// Handle system entering sleep mode
        /// </summary>
        private async Task HandleSystemEnteringSleep()
        {
            try
            {
                Logger.Info("System is entering sleep mode - notifying devices");

                // Fire event before processing
                SystemEnteringSleep?.Invoke(this, new SystemSleepEventArgs(PowerModes.Suspend));

                // Check if HID service is available and initialized
                if (!_hidDeviceService.IsInitialized)
                {
                    Logger.Warn("HID Device Service is not initialized - cannot notify devices of sleep mode");
                    return;
                }

                // Get the count of devices that will be affected
                var targetDevices = _hidDeviceService.GetOperationTargetDevices();
                if (!targetDevices.Any())
                {
                    Logger.Info("No devices available to notify of sleep mode");
                    return;
                }

                Logger.Info($"Sending sleep mode command to {targetDevices.Count} devices");

                // Send display in sleep command to all filtered devices
                var results = await _hidDeviceService.SetDisplayInSleepAsync(true);

                // Process results
                var successCount = results.Values.Count(r => r);
                var failureCount = results.Count - successCount;

                Logger.Info($"Sleep mode notification completed: {successCount} successful, {failureCount} failed");

                // Fire success event
                DeviceSleepModeChanged?.Invoke(this, new DeviceSleepModeEventArgs(
                    sleepModeEnabled: true,
                    successfulDevices: successCount,
                    totalDevices: results.Count,
                    deviceResults: results
                ));

                // Log individual device results if there were failures
                if (failureCount > 0)
                {
                    foreach (var result in results.Where(r => !r.Value))
                    {
                        Logger.Warn($"Failed to set sleep mode on device: {result.Key}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to handle system entering sleep mode", ex);
            }
        }

        /// <summary>
        /// Handle system resuming from sleep mode
        /// </summary>
        private async Task HandleSystemResumingFromSleep()
        {
            try
            {
                Logger.Info("System is resuming from sleep mode - notifying devices");

                // Fire event before processing
                SystemResumingFromSleep?.Invoke(this, new SystemSleepEventArgs(PowerModes.Resume));

                // Check if HID service is available and initialized
                if (!_hidDeviceService.IsInitialized)
                {
                    Logger.Warn("HID Device Service is not initialized - cannot notify devices of wake mode");
                    return;
                }

                // Get the count of devices that will be affected
                var targetDevices = _hidDeviceService.GetOperationTargetDevices();
                if (!targetDevices.Any())
                {
                    Logger.Info("No devices available to notify of wake mode");
                    return;
                }

                Logger.Info($"Sending wake mode command to {targetDevices.Count} devices");

                // Send display wake command to all filtered devices
                var results = await _hidDeviceService.SetDisplayInSleepAsync(false);

                // Process results
                var successCount = results.Values.Count(r => r);
                var failureCount = results.Count - successCount;

                Logger.Info($"Wake mode notification completed: {successCount} successful, {failureCount} failed");

                // Fire success event
                DeviceSleepModeChanged?.Invoke(this, new DeviceSleepModeEventArgs(
                    sleepModeEnabled: false,
                    successfulDevices: successCount,
                    totalDevices: results.Count,
                    deviceResults: results
                ));

                // Log individual device results if there were failures
                if (failureCount > 0)
                {
                    foreach (var result in results.Where(r => !r.Value))
                    {
                        Logger.Warn($"Failed to set wake mode on device: {result.Key}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to handle system resuming from sleep mode", ex);
            }
        }

        /// <summary>
        /// Manually trigger sleep mode on devices (for testing or manual control)
        /// </summary>
        /// <param name="enable">True to enable sleep mode, false to disable</param>
        /// <returns>Dictionary of device paths and operation results</returns>
        public async Task<Dictionary<string, bool>> SetDeviceSleepModeManuallyAsync(bool enable)
        {
            try
            {
                Logger.Info($"Manually setting device sleep mode to: {enable}");

                if (!_hidDeviceService.IsInitialized)
                {
                    throw new InvalidOperationException("HID Device Service is not initialized");
                }

                var results = await _hidDeviceService.SetDisplayInSleepAsync(enable);

                var successCount = results.Values.Count(r => r);
                Logger.Info($"Manual sleep mode change completed: {successCount}/{results.Count} devices successful");

                // Fire event
                DeviceSleepModeChanged?.Invoke(this, new DeviceSleepModeEventArgs(
                    sleepModeEnabled: enable,
                    successfulDevices: successCount,
                    totalDevices: results.Count,
                    deviceResults: results
                ));

                return results;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to manually set device sleep mode to {enable}", ex);
                throw;
            }
        }

        /// <summary>
        /// Get the current power status information
        /// </summary>
        /// <returns>Current system power status</returns>
        public SystemPowerStatus GetCurrentPowerStatus()
        {
            try
            {
                var powerStatus = SystemInformation.PowerStatus;
                return new SystemPowerStatus
                {
                    ACLineStatus = powerStatus.PowerLineStatus,
                    BatteryChargeStatus = powerStatus.BatteryChargeStatus,
                    BatteryLifePercent = powerStatus.BatteryLifePercent,
                    BatteryLifeRemaining = powerStatus.BatteryLifeRemaining,
                    BatteryFullLifetime = powerStatus.BatteryFullLifetime
                };
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get current power status", ex);
                throw;
            }
        }

        /// <summary>
        /// Check if the system is currently running on battery power
        /// </summary>
        /// <returns>True if on battery power, false if on AC power</returns>
        public bool IsOnBatteryPower()
        {
            try
            {
                return SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to check battery power status", ex);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                StopMonitoring();
            }
            catch (Exception ex)
            {
                Logger.Error("Error during SystemSleepMonitorService disposal", ex);
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Event arguments for system sleep events
    /// </summary>
    public class SystemSleepEventArgs : EventArgs
    {
        public PowerModes PowerMode { get; }
        public DateTime Timestamp { get; }

        public SystemSleepEventArgs(PowerModes powerMode)
        {
            PowerMode = powerMode;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for device sleep mode changes
    /// </summary>
    public class DeviceSleepModeEventArgs : EventArgs
    {
        public bool SleepModeEnabled { get; }
        public int SuccessfulDevices { get; }
        public int TotalDevices { get; }
        public Dictionary<string, bool> DeviceResults { get; }
        public DateTime Timestamp { get; }

        public DeviceSleepModeEventArgs(bool sleepModeEnabled, int successfulDevices, int totalDevices, Dictionary<string, bool> deviceResults)
        {
            SleepModeEnabled = sleepModeEnabled;
            SuccessfulDevices = successfulDevices;
            TotalDevices = totalDevices;
            DeviceResults = deviceResults;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// System power status information
    /// </summary>
    public class SystemPowerStatus
    {
        public PowerLineStatus ACLineStatus { get; set; }
        public BatteryChargeStatus BatteryChargeStatus { get; set; }
        public float BatteryLifePercent { get; set; }
        public int BatteryLifeRemaining { get; set; }
        public int BatteryFullLifetime { get; set; }

        public override string ToString()
        {
            return $"AC: {ACLineStatus}, Battery: {BatteryChargeStatus} ({BatteryLifePercent:P0}), " +
                   $"Remaining: {BatteryLifeRemaining}s, Full Lifetime: {BatteryFullLifetime}s";
        }
    }
}