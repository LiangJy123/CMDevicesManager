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
        private readonly Dispatcher _dispatcher;

        private Timer? _timer;
        private bool _isStarted;
        private int _isUpdating;
        private bool _disposed;

        public ObservableCollection<SensorCard> CoolingCards { get; } = new();
        public ObservableCollection<SensorCard> PowerCards { get; } = new();
        public ObservableCollection<SensorCard> SystemCards { get; } = new();
        public ObservableCollection<SensorCard> NetworkCards { get; } = new();

        public HomeViewModel()
            : this(RealSystemMetricsService.Instance) // convenience if you want parameterless usage
        {
        }

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

            StartTimer(); // start initially
        }

        private void StartTimer()
        {
            if (_disposed || _isStarted) return;
            _timer = new Timer(_ => Update(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            _isStarted = true;
        }

        private void StopTimer()
        {
            if (!_isStarted) return;
            try { _timer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            _timer?.Dispose();
            _timer = null;
            _isStarted = false;
        }

        public void OnNavigatedTo()
        {
            _disposed = false;
            StartTimer();
        }

        public void OnNavigatedFrom()
        {
            // Optionally pause to save resources while off-screen
            StopTimer();
        }

        private void Update()
        {
            if (_disposed || !_isStarted) return;
            if (Interlocked.Exchange(ref _isUpdating, 1) == 1) return;

            try
            {
                double cpuTemp = _service.GetCpuTemperature();
                double gpuTemp = _service.GetGpuTemperature();
                double cpuPower = _service.GetCpuPower();
                double gpuPower = _service.GetGpuPower();
                double cpuUsage = _service.GetCpuUsagePercent();
                double gpuUsage = _service.GetGpuUsagePercent();
                double memUsage = _service.GetMemoryUsagePercent();
                double netDown = _service.GetNetDownloadKBs();
                double netUp = _service.GetNetUploadKBs();

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
                // swallow or log
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
            StopTimer();
            // Do NOT dispose the singleton metrics service here (shared globally).
            // If you still inject a non-singleton implementation in tests, you can optionally dispose it:
            // _service.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}