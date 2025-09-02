using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;
using CMDevicesManager.Models;
using CMDevicesManager.Services;
using Timer = System.Threading.Timer;

namespace CMDevicesManager.ViewModels
{
    public sealed class HomeViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ISystemMetricsService _service;
        private readonly Timer _timer;
        private readonly Dispatcher _dispatcher;

        private int _isUpdating; // prevent overlapping timer ticks
        private volatile bool _disposed;

        public ObservableCollection<SensorCard> CoolingCards { get; } = new();
        public ObservableCollection<SensorCard> PowerCards { get; } = new();
        public ObservableCollection<SensorCard> SystemCards { get; } = new();
        public ObservableCollection<SensorCard> NetworkCards { get; } = new();

        public HomeViewModel(ISystemMetricsService service)
        {
            _service = service;
            _dispatcher = Dispatcher.CurrentDispatcher;

            CoolingCards.Add(new SensorCard("CPU", _service.CpuName, "°C", "\uE9CA"));
            CoolingCards.Add(new SensorCard("GPU", _service.PrimaryGpuName, "°C", "\uE9CA"));

            PowerCards.Add(new SensorCard("CPU", _service.CpuName, "W", "\uE945"));
            PowerCards.Add(new SensorCard("GPU", _service.PrimaryGpuName, "W", "\uE945"));

            SystemCards.Add(new SensorCard("CPU", _service.CpuName, "%", "\uE9D9"));
            SystemCards.Add(new SensorCard("GPU", _service.PrimaryGpuName, "%", "\uE9D9"));
            SystemCards.Add(new SensorCard("Memory", _service.MemoryName, "%", "\uEABA"));

            NetworkCards.Add(new SensorCard("Download", "KB/s", "KB/s", "\uE839"));
            NetworkCards.Add(new SensorCard("Upload", "KB/s", "KB/s", "\uE83A"));

            // Issue when switch page to device page, disable timer to avoid exception
            //_timer = new Timer(_ => Update(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
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

                // Only marshal the assignment (which raises PropertyChanged) to the UI thread
                _dispatcher.BeginInvoke(() =>
                {
                    if (_disposed) return;

                    CoolingCards[0].Value = cpuTemp;
                    CoolingCards[1].Value = gpuTemp;

                    PowerCards[0].Value = cpuPower;
                    PowerCards[1].Value = gpuPower;

                    SystemCards[0].Value = cpuUsage;
                    SystemCards[1].Value = gpuUsage;
                    SystemCards[2].Value = memUsage;

                    NetworkCards[0].Value = netDown;
                    NetworkCards[1].Value = netUp;
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

            //try { _timer.Change(Timeout.Infinite, Timeout.Infinite); } catch { /* ignore */ }
            //_timer.Dispose();
            _service.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}