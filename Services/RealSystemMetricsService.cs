// CMDevicesManager - Hardware Monitoring Service
// This service reads hardware sensors for system performance display.
// Uses LibreHardwareMonitor library for legitimate hardware monitoring.
// All data is used locally for dashboard display only.

using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using CMDevicesManager.Helper;
using LibreHardwareMonitor.Hardware;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// Singleton-like hardware metrics provider.
    /// - A single background task refreshes sensors at a fixed interval.
    /// - Public Get* methods return cached values instantly (thread-safe).
    /// - Dispose() is intentionally a no-op so pages can "dispose" safely without shutting service down.
    /// - Call Shutdown() once on application exit if you want to really release resources.
    /// </summary>
    public sealed class RealSystemMetricsService : ISystemMetricsService
    {
        // ---------- Singleton ----------
        private static readonly Lazy<RealSystemMetricsService> _instance =
            new(() => new RealSystemMetricsService(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static RealSystemMetricsService Instance => _instance.Value;

        // ---------- LibreHardwareMonitor core ----------
        private readonly Computer _computer;
        private readonly object _sensorLock = new();          // 保护传感器解析
        private readonly object _nameLock = new();            // 保护名称赋值（一次性）
        private readonly TimeSpan _refreshInterval = TimeSpan.FromMilliseconds(500);

        // Raw sensor references
        private ISensor? _cpuTempSensor;
        private ISensor? _gpuTempSensor;
        private ISensor? _cpuPowerSensor;
        private ISensor? _gpuPowerSensor;
        private ISensor? _cpuLoadSensor;
        private ISensor? _gpuLoadSensor;
        private ISensor? _memLoadSensor;

        // Cached numeric values (volatile 简化读取无需锁)
        private  double _cpuTempValue;
        private  double _gpuTempValue;
        private  double _cpuPowerValue;
        private  double _gpuPowerValue;
        private  double _cpuLoadValue;
        private  double _gpuLoadValue;
        private  double _memLoadValue;
        private  double _netDownKBs;
        private  double _netUpKBs;

        // Names (once set基本不变)
        public string CpuName { get; private set; } = "CPU";
        public string PrimaryGpuName { get; private set; } = "GPU";
        public string MemoryName { get; private set; } = "Memory";

        // Network sampling
        private long _lastRecvBytes;
        private long _lastSentBytes;
        private DateTime _lastNetSample = DateTime.MinValue;

        // Control
        private readonly CancellationTokenSource _cts = new();
        private Task? _loopTask;
        private bool _shutdown;

        // Log-once flags (和原实现保持语义，防止刷日志)
        private bool _loggedCpuTemp, _loggedGpuTemp, _loggedCpuPower, _loggedGpuPower, _loggedCpuLoad, _loggedGpuLoad, _loggedMemLoad;
        private bool _loggedCpuName, _loggedGpuName, _loggedMemName;

        private RealSystemMetricsService()
        {
            Logger.Info("[HW] RealSystemMetricsService singleton initializing...");
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = true,
                    IsStorageEnabled = false,
                    IsNetworkEnabled = false
                };
                _computer.Open();

                // 初次解析名称 & 初次刷新
                ResolveHardwareNames();
                InitialSensorCache();
                ForceRefreshOnce();

                // 启动后台循环
                _loopTask = Task.Run(RefreshLoop);
                Logger.Info("[HW] RealSystemMetricsService initialized.");
            }
            catch (Exception ex)
            {
                Logger.Error("[HW] Failed to initialize RealSystemMetricsService singleton", ex);
                throw;
            }
        }

        // ========== Public API (cached reads) ==========
        public double GetCpuTemperature() => Round0(_cpuTempValue);
        public double GetGpuTemperature() => Round0(_gpuTempValue);
        public double GetCpuPower() => Round0(_cpuPowerValue);
        public double GetGpuPower() => Round0(_gpuPowerValue);
        public double GetCpuUsagePercent() => Round0(_cpuLoadValue);
        public double GetGpuUsagePercent() => Round0(_gpuLoadValue);
        public double GetMemoryUsagePercent() => Round0(_memLoadValue);
        public double GetNetDownloadKBs() => Round0(_netDownKBs);
        public double GetNetUploadKBs() => Round0(_netUpKBs);

        private static double Round0(double v) => Math.Round(v, 0);

        // ========== Background Loop ==========
        private async Task RefreshLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;
                try
                {
                    UpdateAllSensors();
                }
                catch (Exception ex)
                {
                    Logger.Info($"[HW] Refresh loop iteration failed: {ex.Message}");
                }

                var elapsed = DateTime.UtcNow - started;
                var delay = _refreshInterval - elapsed;
                if (delay < TimeSpan.FromMilliseconds(50))
                    delay = TimeSpan.FromMilliseconds(50);

                try
                {
                    await Task.Delay(delay, _cts.Token);
                }
                catch (TaskCanceledException) { }
            }
        }

        // ========== One-shot initial update ==========
        private void ForceRefreshOnce()
        {
            try { UpdateAllSensors(); } catch { /* ignore */ }
        }

        // ========== Update logic ==========
        private void UpdateAllSensors()
        {
            lock (_sensorLock)
            {
                foreach (var hw in _computer.Hardware)
                {
                    try
                    {
                        hw.Update();
                        // 递归子硬件
                        foreach (var sub in hw.SubHardware)
                        {
                            sub.Update();
                            foreach (var sub2 in sub.SubHardware)
                                sub2.Update();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"[HW] Hardware update failed: {hw?.Name} - {ex.Message}");
                    }
                }

                // 解析一次（如果之前没成功）
                if (CpuName == "CPU" || PrimaryGpuName == "GPU" || MemoryName == "Memory")
                    ResolveHardwareNames();

                // 传感器对象确保
                EnsureSensorsResolved();

                // 读取值（容错）
                _cpuTempValue = ReadSensorSafe(_cpuTempSensor, ref _loggedCpuTemp, "[HW] CPU temperature sensor not found");
                _gpuTempValue = ReadSensorSafe(_gpuTempSensor, ref _loggedGpuTemp, "[HW] GPU temperature sensor not found");
                _cpuPowerValue = ReadSensorSafe(_cpuPowerSensor, ref _loggedCpuPower, "[HW] CPU power sensor not found");
                _gpuPowerValue = ReadSensorSafe(_gpuPowerSensor, ref _loggedGpuPower, "[HW] GPU power sensor not found");
                _cpuLoadValue = ReadSensorSafe(_cpuLoadSensor, ref _loggedCpuLoad, "[HW] CPU load sensor not found");
                _gpuLoadValue = ReadSensorSafe(_gpuLoadSensor, ref _loggedGpuLoad, "[HW] GPU load sensor not found");
                _memLoadValue = ReadSensorSafe(_memLoadSensor, ref _loggedMemLoad, "[HW] Memory load sensor not found");

                UpdateNetworkRatesLocked();
            }
        }

        private void EnsureSensorsResolved()
        {
            _cpuTempSensor ??= FindCpuSensor(SensorType.Temperature, s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                ?? FindCpuSensor(SensorType.Temperature, _ => true);

            _gpuTempSensor ??= FindGpuSensor(SensorType.Temperature, s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                ?? FindGpuSensor(SensorType.Temperature, _ => true);

            _cpuPowerSensor ??= FindCpuSensor(SensorType.Power, s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                                 ?? FindCpuSensor(SensorType.Power, _ => true);

            _gpuPowerSensor ??= FindGpuSensor(SensorType.Power, s => s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                 ?? FindGpuSensor(SensorType.Power, _ => true);

            _cpuLoadSensor ??= FindCpuSensor(SensorType.Load, s => s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                                ?? FindCpuSensor(SensorType.Load, _ => true);

            _gpuLoadSensor ??= FindGpuSensor(SensorType.Load, s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                ?? FindGpuSensor(SensorType.Load, _ => true);

            _memLoadSensor ??= FindMemorySensor(SensorType.Load, s => s.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase))
                                ?? FindMemorySensor(SensorType.Load, _ => true);
        }

        private void InitialSensorCache()
        {
            lock (_sensorLock)
            {
                EnsureSensorsResolved();
            }
        }

        private void ResolveHardwareNames()
        {
            lock (_nameLock)
            {
                if (CpuName == "CPU")
                {
                    var c = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu)?.Name;
                    if (!string.IsNullOrWhiteSpace(c)) CpuName = c;
                    else if (!_loggedCpuName) { Logger.Info("[HW] CPU name not found."); _loggedCpuName = true; }
                }

                if (PrimaryGpuName == "GPU")
                {
                    var g = _computer.Hardware.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)?.Name;
                    if (!string.IsNullOrWhiteSpace(g)) PrimaryGpuName = g;
                    else if (!_loggedGpuName) { Logger.Info("[HW] GPU name not found."); _loggedGpuName = true; }
                }

                if (MemoryName == "Memory")
                {
                    var m = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory)?.Name;
                    if (!string.IsNullOrWhiteSpace(m)) MemoryName = m;
                    else if (!_loggedMemName) { Logger.Info("[HW] Memory device not found."); _loggedMemName = true; }
                }
            }
        }

        private double ReadSensorSafe(ISensor? sensor, ref bool loggedFlag, string logMsg)
        {
            try
            {
                var v = sensor?.Value;
                if (v.HasValue) return v.Value;
                if (!loggedFlag)
                {
                    Logger.Info(logMsg);
                    loggedFlag = true;
                }
            }
            catch (Exception ex)
            {
                if (!loggedFlag)
                {
                    Logger.Info($"{logMsg}. {ex.Message}");
                    loggedFlag = true;
                }
            }
            return 0;
        }

        // ---------- Network ----------
        private void UpdateNetworkRatesLocked()
        {
            var now = DateTime.UtcNow;
            if (_lastNetSample != DateTime.MinValue && (now - _lastNetSample).TotalMilliseconds < 400)
                return;

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
                    var stats = nic.GetIPv4Statistics();
                    recv += stats.BytesReceived;
                    sent += stats.BytesSent;
                }

                if (_lastNetSample == DateTime.MinValue)
                {
                    _lastRecvBytes = recv;
                    _lastSentBytes = sent;
                    _lastNetSample = now;
                    _netDownKBs = 0;
                    _netUpKBs = 0;
                    return;
                }

                var seconds = Math.Max(0.001, (now - _lastNetSample).TotalSeconds);
                _netDownKBs = (recv - _lastRecvBytes) / 1024.0 / seconds;
                _netUpKBs = (sent - _lastSentBytes) / 1024.0 / seconds;

                _lastRecvBytes = recv;
                _lastSentBytes = sent;
                _lastNetSample = now;
            }
            catch (Exception ex)
            {
                // 降级为 0
                _netDownKBs = 0;
                _netUpKBs = 0;
                Logger.Info("[NET] Network rate read failed: " + ex.Message);
            }
        }

        // ---------- Sensor find helpers ----------
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

        // ---------- Dispose / Shutdown ----------
        public void Dispose()
        {
            // No-op for per-page usage.
            // Use Shutdown() if you really want to terminate background loop.
        }

        /// <summary>
        /// Call once at application shutdown if you want to stop background thread and close hardware.
        /// </summary>
        public static void Shutdown()
        {
            if (!_instance.IsValueCreated) return;
            _instance.Value.InternalShutdown();
        }

        private void InternalShutdown()
        {
            if (_shutdown) return;
            _shutdown = true;
            try { _cts.Cancel(); } catch { }
            try { _loopTask?.Wait(1000); } catch { }
            try { _computer.Close(); } catch { }
            Logger.Info("[HW] RealSystemMetricsService shutdown complete.");
        }
    }
}