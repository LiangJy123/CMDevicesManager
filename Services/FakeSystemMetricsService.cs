using System;

namespace CMDevicesManager.Services
{
    // Replace with real sensors (OpenHardwareMonitor, LibreHardwareMonitor, NVML, PDH/PerformanceCounter, etc.)
    public sealed class FakeSystemMetricsService : ISystemMetricsService
    {
        private readonly Random _r = new();

        public string CpuName => "Intel Core i7 11700K";
        public string PrimaryGpuName => "NVIDIA T600";
        public string MemoryName => "DDR4";

        public double GetCpuTemperature() => NextRange(35, 85);
        public double GetGpuTemperature() => NextRange(30, 80);

        public double GetCpuPower() => NextRange(20, 130);
        public double GetGpuPower() => NextRange(15, 160);

        public double GetCpuUsagePercent() => NextRange(1, 100);
        public double GetGpuUsagePercent() => NextRange(0, 100);
        public double GetMemoryUsagePercent() => NextRange(10, 95);

        public double GetNetDownloadKBs() => NextRange(0, 5000);
        public double GetNetUploadKBs() => NextRange(0, 2000);

        private double NextRange(double min, double max) => Math.Round(min + _r.NextDouble() * (max - min), 0);

        public void Dispose() { }
    }
}