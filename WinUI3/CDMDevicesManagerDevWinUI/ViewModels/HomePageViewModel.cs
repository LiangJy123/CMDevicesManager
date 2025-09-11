using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using CMDevicesManager.Services;
using CMDevicesManager.Helper;

namespace CDMDevicesManagerDevWinUI.ViewModels
{
    public class HomePageViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly DispatcherQueueTimer _updateTimer;
        private readonly ISystemMetricsService _systemMetricsService;
        private readonly bool _useRealData;

        // Temperature values
        private double _cpuTemperature;
        private double _gpuTemperature;

        // Power values
        private double _cpuPower;
        private double _gpuPower;

        // Usage percentages for circular progress
        private double _cpuUsage;
        private double _gpuUsage;
        private double _memoryUsage;

        // Network speeds
        private double _downloadSpeed;
        private double _uploadSpeed;
        private double _networkSpeed1;
        private double _networkSpeed2;

        // Hardware names
        private string _cpuName;
        private string _gpuName;

        public HomePageViewModel()
        {
            try
            {
                // Initialize the real system metrics service
                Logger.Info("[HomePageViewModel] Initializing with real system metrics service");
                _systemMetricsService = new RealSystemMetricsService();
                _useRealData = true;
                
                // Get hardware names from the service
                _cpuName = _systemMetricsService.CpuName;
                _gpuName = _systemMetricsService.PrimaryGpuName;
                
                Logger.Info($"[HomePageViewModel] Hardware detected - CPU: {_cpuName}, GPU: {_gpuName}");
                
                var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                
                // Create timer for updating values periodically
                _updateTimer = dispatcherQueue.CreateTimer();
                _updateTimer.Interval = TimeSpan.FromSeconds(1); // Update every second for real data
                _updateTimer.Tick += UpdateTimer_Tick;
                
                // Get initial values
                UpdateSystemMetrics();
                
                // Start the timer
                _updateTimer.Start();
                Logger.Info("[HomePageViewModel] Real-time hardware monitoring started");
            }
            catch (Exception ex)
            {
                // If hardware monitoring fails, fall back to mock data
                Logger.Warn($"[HomePageViewModel] Failed to initialize hardware monitoring, falling back to demo mode: {ex.Message}");
                _useRealData = false;
                InitializeFallbackData();
                
                var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                _updateTimer = dispatcherQueue.CreateTimer();
                _updateTimer.Interval = TimeSpan.FromSeconds(2);
                _updateTimer.Tick += UpdateTimer_FallbackTick;
                _updateTimer.Start();
                Logger.Info("[HomePageViewModel] Demo mode started with simulated data");
            }
        }

        private void UpdateTimer_Tick(object sender, object e)
        {
            UpdateSystemMetrics();
        }

        private void UpdateSystemMetrics()
        {
            try
            {
                // Get real system metrics
                CpuTemperature = _systemMetricsService.GetCpuTemperature();
                GpuTemperature = _systemMetricsService.GetGpuTemperature();
                
                CpuPower = _systemMetricsService.GetCpuPower();
                GpuPower = _systemMetricsService.GetGpuPower();
                
                CpuUsage = _systemMetricsService.GetCpuUsagePercent();
                GpuUsage = _systemMetricsService.GetGpuUsagePercent();
                MemoryUsage = _systemMetricsService.GetMemoryUsagePercent();
                
                DownloadSpeed = _systemMetricsService.GetNetDownloadKBs();
                UploadSpeed = _systemMetricsService.GetNetUploadKBs();
                
                // For now, use the same upload/download speeds for NetworkSpeed1 and NetworkSpeed2
                // You can modify this if you have multiple network interfaces to monitor
                NetworkSpeed1 = Math.Round(DownloadSpeed * 0.7, 0); // Simulate different network metrics
                NetworkSpeed2 = Math.Round(UploadSpeed * 0.8, 0);
            }
            catch (Exception ex)
            {
                Logger.Error("[HomePageViewModel] Error updating system metrics", ex);
                // Keep previous values on error
            }
        }

        private void InitializeFallbackData()
        {
            // Fallback to mock data if hardware monitoring fails
            _cpuName = "Intel Xeon E5 1620";
            _gpuName = "NVIDIA GeForce RTX 2080 S...";
            
            _cpuTemperature = 71;
            _gpuTemperature = 47;
            _cpuPower = 69;
            _gpuPower = 10;
            _cpuUsage = 38;
            _gpuUsage = 16;
            _memoryUsage = 70;
            _downloadSpeed = 32;
            _uploadSpeed = 168;
            _networkSpeed1 = 16;
            _networkSpeed2 = 56;
        }

        private void UpdateTimer_FallbackTick(object sender, object e)
        {
            // Simulate some fluctuation in values for demo (fallback mode)
            var random = new Random();
            
            CpuTemperature = 68 + random.Next(8);
            GpuTemperature = 45 + random.Next(5);
            
            CpuPower = 65 + random.Next(10);
            GpuPower = 8 + random.Next(5);
            
            CpuUsage = 35 + random.Next(10);
            GpuUsage = 14 + random.Next(8);
            MemoryUsage = 68 + random.Next(6);
            
            DownloadSpeed = 30 + random.Next(10);
            UploadSpeed = 160 + random.Next(20);
            NetworkSpeed1 = 15 + random.Next(5);
            NetworkSpeed2 = 54 + random.Next(8);
        }

        public double CpuTemperature
        {
            get => _cpuTemperature;
            set => SetProperty(ref _cpuTemperature, value);
        }

        public double GpuTemperature
        {
            get => _gpuTemperature;
            set => SetProperty(ref _gpuTemperature, value);
        }

        public double CpuPower
        {
            get => _cpuPower;
            set => SetProperty(ref _cpuPower, value);
        }

        public double GpuPower
        {
            get => _gpuPower;
            set => SetProperty(ref _gpuPower, value);
        }

        public double CpuUsage
        {
            get => _cpuUsage;
            set => SetProperty(ref _cpuUsage, value);
        }

        public double GpuUsage
        {
            get => _gpuUsage;
            set => SetProperty(ref _gpuUsage, value);
        }

        public double MemoryUsage
        {
            get => _memoryUsage;
            set => SetProperty(ref _memoryUsage, value);
        }

        public double DownloadSpeed
        {
            get => _downloadSpeed;
            set => SetProperty(ref _downloadSpeed, value);
        }

        public double UploadSpeed
        {
            get => _uploadSpeed;
            set => SetProperty(ref _uploadSpeed, value);
        }

        public double NetworkSpeed1
        {
            get => _networkSpeed1;
            set => SetProperty(ref _networkSpeed1, value);
        }

        public double NetworkSpeed2
        {
            get => _networkSpeed2;
            set => SetProperty(ref _networkSpeed2, value);
        }

        public string CpuName
        {
            get => _cpuName;
            set => SetProperty(ref _cpuName, value);
        }

        public string GpuName
        {
            get => _gpuName;
            set => SetProperty(ref _gpuName, value);
        }

        /// <summary>
        /// Indicates whether the ViewModel is using real hardware data or simulated data
        /// </summary>
        public bool IsUsingRealData => _useRealData;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;

            backingStore = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public void Dispose()
        {
            try
            {
                _updateTimer?.Stop();
                _systemMetricsService?.Dispose();
                Logger.Info("[HomePageViewModel] Hardware monitoring service disposed");
            }
            catch (Exception ex)
            {
                Logger.Error("[HomePageViewModel] Error during disposal", ex);
            }
        }
    }
}