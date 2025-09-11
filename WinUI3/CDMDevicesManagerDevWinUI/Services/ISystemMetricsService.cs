using System;

namespace CMDevicesManager.Services
{
    public interface ISystemMetricsService : IDisposable
    {
        string CpuName { get; }
        string PrimaryGpuName { get; }
        string MemoryName { get; }

        double GetCpuTemperature();
        double GetGpuTemperature();

        double GetCpuPower();
        double GetGpuPower();

        double GetCpuUsagePercent();
        double GetGpuUsagePercent();
        double GetMemoryUsagePercent();

        double GetNetDownloadKBs();
        double GetNetUploadKBs();

        // Disk I/O methods
        double GetDiskReadKBs();
        double GetDiskWriteKBs();
    }
}