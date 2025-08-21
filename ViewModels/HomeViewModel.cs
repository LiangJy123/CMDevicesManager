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

            _timer = new Timer(_ => Update(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        private void Update()
        {
            // Marshal to UI thread to ensure proper binding updates
            _dispatcher.BeginInvoke(() =>
            {
                CoolingCards[0].Value = _service.GetCpuTemperature();
                CoolingCards[1].Value = _service.GetGpuTemperature();

                PowerCards[0].Value = _service.GetCpuPower();
                PowerCards[1].Value = _service.GetGpuPower();

                SystemCards[0].Value = _service.GetCpuUsagePercent();
                SystemCards[1].Value = _service.GetGpuUsagePercent();
                SystemCards[2].Value = _service.GetMemoryUsagePercent();

                NetworkCards[0].Value = _service.GetNetDownloadKBs();
                NetworkCards[1].Value = _service.GetNetUploadKBs();
            });
        }

        public void Dispose()
        {
            _timer.Dispose();
            _service.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}