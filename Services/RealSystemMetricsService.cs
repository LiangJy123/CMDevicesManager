using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using CMDevicesManager.Helper;
using LibreHardwareMonitor.Hardware;

namespace CMDevicesManager.Services
{
    public sealed class RealSystemMetricsService : ISystemMetricsService
    {
        private readonly Computer _computer;
        private readonly object _lock = new();

        private ISensor? _cpuTemp;
        private ISensor? _gpuTemp;
        private ISensor? _cpuPower;
        private ISensor? _gpuPower;
        private ISensor? _cpuLoad;
        private ISensor? _gpuLoad;
        private ISensor? _memLoad;

        private bool _loggedCpuTemp, _loggedGpuTemp, _loggedCpuPower, _loggedGpuPower, _loggedCpuLoad, _loggedGpuLoad, _loggedMemLoad;
        private bool _loggedCpuName, _loggedGpuName, _loggedMemName;

        private long _lastRecvBytes;
        private long _lastSentBytes;
        private DateTime _lastNetSample = DateTime.MinValue;
        private double _lastDownKbs, _lastUpKbs;

        public string CpuName { get; }
        public string PrimaryGpuName { get; }
        public string MemoryName { get; }

        public RealSystemMetricsService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,          // <- replace vendor-specific flags with this 
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = false,
                IsNetworkEnabled = false
            };
            _computer.Open();

            // Build initial sensor map
            lock (_lock)
            {
                RefreshAllHardware();

                CpuName = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu)?.Name ?? "CPU";
                if (CpuName == "CPU" && !_loggedCpuName) { Logger.Info("[HW] CPU name not found."); _loggedCpuName = true; }

                PrimaryGpuName = _computer.Hardware.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)?.Name ?? "GPU";
                if (PrimaryGpuName == "GPU" && !_loggedGpuName) { Logger.Info("[HW] GPU name not found."); _loggedGpuName = true; }

                MemoryName = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory)?.Name ?? "Memory";
                if (MemoryName == "Memory" && !_loggedMemName) { Logger.Info("[HW] Memory device not found."); _loggedMemName = true; }

                CacheSensors();
            }
        }

        public double GetCpuTemperature() => ReadOrZero(ref _cpuTemp, () =>
            FindCpuSensor(SensorType.Temperature, s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)));

        public double GetGpuTemperature() => ReadOrZero(ref _gpuTemp, () =>
            FindGpuSensor(SensorType.Temperature, s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)));

        public double GetCpuPower() => ReadOrZero(ref _cpuPower, () =>
            FindCpuSensor(SensorType.Power, s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase)));

        public double GetGpuPower() => ReadOrZero(ref _gpuPower, () =>
            FindGpuSensor(SensorType.Power, s => s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)));

        public double GetCpuUsagePercent() => ReadOrZero(ref _cpuLoad, () =>
            FindCpuSensor(SensorType.Load, s => s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)));

        public double GetGpuUsagePercent() => ReadOrZero(ref _gpuLoad, () =>
            FindGpuSensor(SensorType.Load, s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)));

        public double GetMemoryUsagePercent() => ReadOrZero(ref _memLoad, () =>
            FindMemorySensor(SensorType.Load, s => s.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase)));

        public double GetNetDownloadKBs()
        {
            UpdateNetworkRates();
            return _lastDownKbs;
        }

        public double GetNetUploadKBs()
        {
            UpdateNetworkRates();
            return _lastUpKbs;
        }

        private void UpdateNetworkRates()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (_lastNetSample != DateTime.MinValue && (now - _lastNetSample).TotalMilliseconds < 400)
                    return; // throttle

                try
                {
                    var nics = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(n =>
                            n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

                    long recv = 0, sent = 0;
                    foreach (var nic in nics)
                    {
                        var s = nic.GetIPv4Statistics();
                        recv += s.BytesReceived;
                        sent += s.BytesSent;
                    }

                    if (_lastNetSample == DateTime.MinValue)
                    {
                        _lastRecvBytes = recv;
                        _lastSentBytes = sent;
                        _lastNetSample = now;
                        _lastDownKbs = 0;
                        _lastUpKbs = 0;
                        return;
                    }

                    var seconds = Math.Max(0.001, (now - _lastNetSample).TotalSeconds);
                    _lastDownKbs = Math.Round((recv - _lastRecvBytes) / 1024.0 / seconds, 0);
                    _lastUpKbs = Math.Round((sent - _lastSentBytes) / 1024.0 / seconds, 0);

                    _lastRecvBytes = recv;
                    _lastSentBytes = sent;
                    _lastNetSample = now;
                }
                catch (Exception ex)
                {
                    if (_lastDownKbs != 0 || _lastUpKbs != 0)
                        Logger.Info("[HW] Network rate read failed, falling back to 0. " + ex.Message);
                    _lastDownKbs = 0;
                    _lastUpKbs = 0;
                }
            }
        }

        private double ReadOrZero(ref ISensor? cache, Func<ISensor?> resolver)
        {
            lock (_lock)
            {
                try
                {
                    RefreshAllHardware();

                    cache ??= resolver();
                    cache?.Hardware?.Update();

                    var value = cache?.Value;
                    if (value.HasValue)
                        return Math.Round(value.Value, 0);

                    // Log once per sensor type
                    LogMissing(cache);
                    return 0;
                }
                catch (Exception ex)
                {
                    // Unexpected failure -> log once per sensor instance
                    LogMissing(cache, ex);
                    return 0;
                }
            }
        }

        private void CacheSensors()
        {
            _cpuTemp = FindCpuSensor(SensorType.Temperature, s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)) ??
                       FindCpuSensor(SensorType.Temperature, _ => true);

            _gpuTemp = FindGpuSensor(SensorType.Temperature, s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) ??
                       FindGpuSensor(SensorType.Temperature, _ => true);

            _cpuPower = FindCpuSensor(SensorType.Power, s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)) ??
                        FindCpuSensor(SensorType.Power, _ => true);

            _gpuPower = FindGpuSensor(SensorType.Power, s => s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)) ??
                        FindGpuSensor(SensorType.Power, _ => true);

            _cpuLoad = FindCpuSensor(SensorType.Load, s => s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase));
            _gpuLoad = FindGpuSensor(SensorType.Load, s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));

            _memLoad = FindMemorySensor(SensorType.Load, s => s.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase)) ??
                       FindMemorySensor(SensorType.Load, _ => true);
        }

        private ISensor? FindCpuSensor(SensorType type, Func<ISensor, bool> predicate) =>
            _computer.Hardware
                .FirstOrDefault(h => h.HardwareType == HardwareType.Cpu)?
                .Sensors.Where(s => s.SensorType == type)
                .OrderBy(s => s.Index)
                .FirstOrDefault(predicate);

        private ISensor? FindGpuSensor(SensorType type, Func<ISensor, bool> predicate) =>
            _computer.Hardware
                .FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)?
                .Sensors.Where(s => s.SensorType == type)
                .OrderBy(s => s.Index)
                .FirstOrDefault(predicate);

        private ISensor? FindMemorySensor(SensorType type, Func<ISensor, bool> predicate) =>
            _computer.Hardware
                .FirstOrDefault(h => h.HardwareType == HardwareType.Memory)?
                .Sensors.Where(s => s.SensorType == type)
                .OrderBy(s => s.Index)
                .FirstOrDefault(predicate);

        private void RefreshAllHardware()
        {
            foreach (var h in _computer.Hardware)
            {
                h.Update();
                foreach (var sub in h.SubHardware) sub.Update();
            }
        }

        private void LogMissing(ISensor? sensor, Exception? ex = null)
        {
            string msg;
            if (sensor == _cpuTemp && !_loggedCpuTemp)
            {
                msg = "[HW] CPU temperature sensor not found. Using 0.";
                Logger.Info(msg + (ex != null ? " " + ex.Message : string.Empty));
                _loggedCpuTemp = true;
            }
            else if (sensor == _gpuTemp && !_loggedGpuTemp)
            {
                msg = "[HW] GPU temperature sensor not found. Using 0.";
                Logger.Info(msg + (ex != null ? " " + ex.Message : string.Empty));
                _loggedGpuTemp = true;
            }
            else if (sensor == _cpuPower && !_loggedCpuPower)
            {
                msg = "[HW] CPU power sensor not found. Using 0.";
                Logger.Info(msg + (ex != null ? " " + ex.Message : string.Empty));
                _loggedCpuPower = true;
            }
            else if (sensor == _gpuPower && !_loggedGpuPower)
            {
                msg = "[HW] GPU power sensor not found. Using 0.";
                Logger.Info(msg + (ex != null ? " " + ex.Message : string.Empty));
                _loggedGpuPower = true;
            }
            else if (sensor == _cpuLoad && !_loggedCpuLoad)
            {
                msg = "[HW] CPU load sensor not found. Using 0%.";
                Logger.Info(msg + (ex != null ? " " + ex.Message : string.Empty));
                _loggedCpuLoad = true;
            }
            else if (sensor == _gpuLoad && !_loggedGpuLoad)
            {
                msg = "[HW] GPU load sensor not found. Using 0%.";
                Logger.Info(msg + (ex != null ? " " + ex.Message : string.Empty));
                _loggedGpuLoad = true;
            }
            else if (sensor == _memLoad && !_loggedMemLoad)
            {
                msg = "[HW] Memory load sensor not found. Using 0%.";
                Logger.Info(msg + (ex != null ? " " + ex.Message : string.Empty));
                _loggedMemLoad = true;
            }
        }

        public void Dispose()
        {
            try { _computer.Close(); } catch { /* ignore */ }
        }
    }
}