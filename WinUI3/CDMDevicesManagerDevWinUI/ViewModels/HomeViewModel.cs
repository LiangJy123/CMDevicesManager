using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using CDMDevicesManagerDevWinUI.Models;
using CMDevicesManager.Services;
using Microsoft.UI.Dispatching; // Add this using directive
using Timer = System.Threading.Timer;

namespace CDMDevicesManagerDevWinUI.ViewModels
{
    public sealed class HomeViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ISystemMetricsService _service;
        private readonly Timer _timer;
        private readonly DispatcherQueue _dispatcherQueue; // Change from Dispatcher to DispatcherQueue

        private int _isUpdating; // prevent overlapping timer ticks
        private volatile bool _disposed;

        // Individual properties for XAML binding
        private double _cpuTemperature;
        private double _gpuTemperature;
        private double _cpuPower;
        private double _gpuPower;
        private double _cpuUsage;
        private double _gpuUsage;
        private double _memoryUsage;
        private double _downloadSpeed;
        private double _uploadSpeed;
        private double _networkSpeed1;
        private double _networkSpeed2;
        private double _diskReadSpeed;
        private double _diskWriteSpeed;
        private string _cpuName;
        private string _gpuName;

        public ObservableCollection<SensorCard> CoolingCards { get; } = new();
        public ObservableCollection<SensorCard> PowerCards { get; } = new();
        public ObservableCollection<SensorCard> SystemCards { get; } = new();
        public ObservableCollection<SensorCard> NetworkCards { get; } = new();
        public ObservableCollection<SensorCard> StorageCards { get; } = new();

        // Individual properties for XAML binding
        public double CpuTemperature { get => _cpuTemperature; set { _cpuTemperature = value; OnPropertyChanged(); } }
        public double GpuTemperature { get => _gpuTemperature; set { _gpuTemperature = value; OnPropertyChanged(); } }
        public double CpuPower { get => _cpuPower; set { _cpuPower = value; OnPropertyChanged(); } }
        public double GpuPower { get => _gpuPower; set { _gpuPower = value; OnPropertyChanged(); } }
        public double CpuUsage { get => _cpuUsage; set { _cpuUsage = value; OnPropertyChanged(); } }
        public double GpuUsage { get => _gpuUsage; set { _gpuUsage = value; OnPropertyChanged(); } }
        public double MemoryUsage { get => _memoryUsage; set { _memoryUsage = value; OnPropertyChanged(); } }
        public double DownloadSpeed { get => _downloadSpeed; set { _downloadSpeed = value; OnPropertyChanged(); } }
        public double UploadSpeed { get => _uploadSpeed; set { _uploadSpeed = value; OnPropertyChanged(); } }
        public double NetworkSpeed1 { get => _networkSpeed1; set { _networkSpeed1 = value; OnPropertyChanged(); } }
        public double NetworkSpeed2 { get => _networkSpeed2; set { _networkSpeed2 = value; OnPropertyChanged(); } }
        public double DiskReadSpeed { get => _diskReadSpeed; set { _diskReadSpeed = value; OnPropertyChanged(); } }
        public double DiskWriteSpeed { get => _diskWriteSpeed; set { _diskWriteSpeed = value; OnPropertyChanged(); } }
        public string CpuName { get => _cpuName; set { _cpuName = value; OnPropertyChanged(); } }
        public string GpuName { get => _gpuName; set { _gpuName = value; OnPropertyChanged(); } }

        public HomeViewModel(ISystemMetricsService service)
        {
            _service = service;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread(); // Change from Dispatcher.CurrentDispatcher

            // Initialize hardware names
            _cpuName = _service.CpuName;
            _gpuName = _service.PrimaryGpuName;

            CoolingCards.Add(new SensorCard("CPU", _service.CpuName, "°C", "\uE9CA"));
            CoolingCards.Add(new SensorCard("GPU", _service.PrimaryGpuName, "°C", "\uE9CA"));

            PowerCards.Add(new SensorCard("CPU", _service.CpuName, "W", "\uE945"));
            PowerCards.Add(new SensorCard("GPU", _service.PrimaryGpuName, "W", "\uE945"));

            SystemCards.Add(new SensorCard("CPU", _service.CpuName, "%", "\uE9D9"));
            SystemCards.Add(new SensorCard("GPU", _service.PrimaryGpuName, "%", "\uE9D9"));
            SystemCards.Add(new SensorCard("Memory", _service.MemoryName, "%", "\uEABA"));

            NetworkCards.Add(new SensorCard("Download", "Network", "KB/s", "\uF0AE"));
            NetworkCards.Add(new SensorCard("Upload", "Network", "KB/s", "\uF0AD"));

            // Add storage cards for disk I/O
            StorageCards.Add(new SensorCard("Read", "Disk", "KB/s", "\uE8B7"));
            StorageCards.Add(new SensorCard("Write", "Disk", "KB/s", "\uE8C3"));

            // Issue when switch page to device page, disable timer to avoid exception
            _timer = new Timer(_ => Update(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        private void Update()
        {
            if (_disposed) return;

            // Ensure we don't overlap timer callbacks if a previous one is still running
            if (Interlocked.Exchange(ref _isUpdating, 1) == 1) return;

            try
            {
                // Collect metrics off the UI thread (Timer already runs on a ThreadPool thread)
                double cpuTemp = _service.GetCpuTemperature();
                double gpuTemp = _service.GetGpuTemperature();

                double cpuPower = _service.GetCpuPower();
                double gpuPower = _service.GetGpuPower();

                double cpuUsage = _service.GetCpuUsagePercent();
                double gpuUsage = _service.GetGpuUsagePercent();
                double memUsage = _service.GetMemoryUsagePercent();

                double netDown = _service.GetNetDownloadKBs();
                double netUp = _service.GetNetUploadKBs();

                double diskRead = _service.GetDiskReadKBs();
                double diskWrite = _service.GetDiskWriteKBs();

                // Only marshal the assignment (which raises PropertyChanged) to the UI thread
                _dispatcherQueue.TryEnqueue(() => // Change from _dispatcher.BeginInvoke
                {
                    if (_disposed) return;

                    // Update SensorCard collections
                    CoolingCards[0].Value = cpuTemp;
                    CoolingCards[1].Value = gpuTemp;

                    PowerCards[0].Value = cpuPower;
                    PowerCards[1].Value = gpuPower;

                    SystemCards[0].Value = cpuUsage;
                    SystemCards[1].Value = gpuUsage;
                    SystemCards[2].Value = memUsage;

                    NetworkCards[0].Value = netDown;
                    NetworkCards[1].Value = netUp;

                    StorageCards[0].Value = diskRead;
                    StorageCards[1].Value = diskWrite;

                    // Update individual properties for XAML binding
                    CpuTemperature = cpuTemp;
                    GpuTemperature = gpuTemp;
                    CpuPower = cpuPower;
                    GpuPower = gpuPower;
                    CpuUsage = cpuUsage;
                    GpuUsage = gpuUsage;
                    MemoryUsage = memUsage;
                    DownloadSpeed = netDown;
                    UploadSpeed = netUp;
                    NetworkSpeed1 = netDown; // You may want to adjust these mappings
                    NetworkSpeed2 = netUp;
                    DiskReadSpeed = diskRead;
                    DiskWriteSpeed = diskWrite;
                });
            }
            catch
            {
                // Swallow to keep UI responsive; optionally log if you have a logger available here
            }
            finally
            {
                Interlocked.Exchange(ref _isUpdating, 0);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _timer.Change(Timeout.Infinite, Timeout.Infinite); } catch { /* ignore */ }
            _timer.Dispose();
            _service.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}