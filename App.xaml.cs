using CMDevicesManager.Helper;
using CMDevicesManager.Models;
using CMDevicesManager.Services;
using FFMpegCore;
using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application; // Ensure this using directive is present

namespace CMDevicesManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private HidDeviceService? _hidDeviceService;
        private OfflineMediaDataService? _offlineMediaDataService;
        private InteractiveWin2DRenderingService? _interactiveRenderingService;
        private RealtimeJpegTransmissionService? _realtimeJpegTransmissionService;

        public App()
        {
            this.Exit += App_Exit;
        }

        private void App_Exit(object sender, ExitEventArgs e)
        {
            Logger.Info("App Exit");
         
            // Cleanup services
            ServiceLocator.Cleanup();
            
            // 保存配置
            UserConfigManager.Save();
            Logger.Shutdown();
        }

         
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Logger.Info("Application starting up");

            try
            {
                // Initialize language based on user configuration
                Logger.Info("Initializing language settings");
                CMDevicesManager.Language.LanguageSwitch.ChangeLanguage(UserConfigManager.Current.Language);

                // Initialize font based on user configuration
                Logger.Info("Initializing font settings");
                CMDevicesManager.Language.FontSwitch.ChangeFont(UserConfigManager.Current.FontFamily);

                var ffDir = Path.Combine(AppContext.BaseDirectory, "exe");
                GlobalFFOptions.Configure(new FFOptions
                {
                    BinaryFolder = ffDir,
                    TemporaryFilesFolder = Path.GetTempPath(),
                    UseCache = true
                });

                // Initialize services
                InitializeServices();

                // 显示启动窗口
                var splash = new SplashWindow();
                var main = new MainWindow();
                this.MainWindow = main;
                splash.Show();
                
                Logger.Info("Starting system tray integration");
                StartNotifyIcon();
                
                // 后台加载数据
                Task.Run(() =>
                {
                    Logger.Info("Loading application data in background");
                    // 模拟加载数据（2秒）
                    Thread.Sleep(2000);
                    //DataLoader.LoadAll(); // 这里写你的初始化逻辑
                    Logger.Info("Background loading completed");

                    // 切回 UI 线程，关闭 Splash，显示主界面
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            splash.Close();
                            ShowFirstMainWindow();
                            Logger.Info("Main window displayed successfully");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Failed to display main window", ex);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Application startup failed", ex);
                throw;
            }
        }

        private async void InitializeServices()
        {
            try
            {
                Logger.Info("Initializing application services");

                // Initialize Offline Media Data Service first
                Logger.Info("Initializing Offline Media Data Service");
                _offlineMediaDataService = new OfflineMediaDataService();

                // Initialize HID Device Service
                Logger.Info("Initializing HID Device Service");
                _hidDeviceService = new HidDeviceService();
                
                // Initialize the HID service with default VID/PID values
                // You can customize these values based on your devices
                await _hidDeviceService.InitializeAsync(
                    vendorId: 0x2516,   // Your device's vendor ID
                    productId: 0x0228,  // Your device's product ID
                    usagePage: 0xFFFF   // Your device's usage page
                );

                // Initialize Realtime JPEG Transmission Service (simplified)
                Logger.Info("Initializing Realtime JPEG Transmission Service");
                _realtimeJpegTransmissionService = new RealtimeJpegTransmissionService(
                    _hidDeviceService,
                    targetFps: 30,                   // ~30 FPS for balanced performance
                    maxQueueSize: 8,                 // Moderate queue size for smooth operation
                    realtimeTimeoutSeconds: 5        // 5 second timeout for real-time mode
                );

                // Set up event handlers for the simplified service
                _realtimeJpegTransmissionService.FrameProcessed += OnRealtimeJpegFrameProcessed;
                _realtimeJpegTransmissionService.FrameDropped += OnRealtimeJpegFrameDropped;
                _realtimeJpegTransmissionService.RealTimeModeChanged += OnRealtimeJpegRealTimeModeChanged;
                _realtimeJpegTransmissionService.ServiceError += OnRealtimeJpegServiceError;
                _realtimeJpegTransmissionService.DeviceConnectionChanged += OnRealtimeJpegDeviceConnectionChanged;

                Logger.Info("Realtime JPEG Transmission Service initialized successfully");

                // Initialize Interactive Win2D Rendering Service
                Logger.Info("Initializing Interactive Win2D Rendering Service");
                _interactiveRenderingService = new InteractiveWin2DRenderingService();
                
                // Initialize with optimal settings for HID streaming
                await _interactiveRenderingService.InitializeAsync(
                    width: 480,   // Standard display width
                    height: 480   // Standard display height
                );

                // Configure default settings for optimal performance
                _interactiveRenderingService.TargetFPS = 25;        // Balanced performance
                _interactiveRenderingService.JpegQuality = 85;      // Good quality/size ratio
                _interactiveRenderingService.ShowTime = true;       // Enable live time display
                _interactiveRenderingService.ShowDate = true;       // Enable live date display
                _interactiveRenderingService.ShowSystemInfo = true; // Enable system info
                _interactiveRenderingService.ShowAnimation = true;  // Enable animations

                // Set up event handlers for service monitoring
                _interactiveRenderingService.HidStatusChanged += OnInteractiveRenderingHidStatusChanged;
                _interactiveRenderingService.RenderingError += OnInteractiveRenderingError;

                Logger.Info("Interactive Win2D Rendering Service initialized successfully");

                // Initialize System Sleep Monitor Service
                Logger.Info("Initializing System Sleep Monitor Service");
                var systemSleepMonitorService = new SystemSleepMonitorService(_hidDeviceService);
                
                // Initialize the service locator with all services (including RealtimeJpegTransmissionService)
                ServiceLocator.InitializeAll(
                    _hidDeviceService, 
                    _offlineMediaDataService, 
                    systemSleepMonitorService, 
                    _interactiveRenderingService,
                    _realtimeJpegTransmissionService
                );
                
                // Start monitoring system sleep events
                systemSleepMonitorService.StartMonitoring();

                // Set up event handlers for device connection/disconnection to update offline data
                _hidDeviceService.DeviceConnected += OnDeviceConnected;
                _hidDeviceService.DeviceDisconnected += OnDeviceDisconnected;
                
                // Set up event handlers for system sleep monitoring
                systemSleepMonitorService.SystemEnteringSleep += OnSystemEnteringSleep;
                systemSleepMonitorService.SystemResumingFromSleep += OnSystemResumingFromSleep;
                systemSleepMonitorService.DeviceSleepModeChanged += OnDeviceSleepModeChanged;
                
                Logger.Info("All services initialized successfully");
                Logger.Info($"Services status: HID={_hidDeviceService.IsInitialized}, " +
                          $"InteractiveRendering=True, " +
                          $"OfflineMedia={_offlineMediaDataService != null}, " +
                          $"SleepMonitor={systemSleepMonitorService != null}, " +
                          $"RealtimeJpeg={_realtimeJpegTransmissionService != null}");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize services", ex);
                // Don't throw here - let the app continue with reduced functionality
            }
        }

        #region Interactive Rendering Service Event Handlers

        private void OnInteractiveRenderingHidStatusChanged(string status)
        {
            try
            {
                Logger.Info($"Interactive Rendering HID Status: {status}");
                
                // You can add application-level logic here based on HID status
                // For example, show notifications or adjust UI based on device connectivity
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling interactive rendering HID status change: {ex.Message}", ex);
            }
        }

        private void OnInteractiveRenderingError(Exception ex)
        {
            try
            {
                Logger.Error($"Interactive Rendering Error: {ex.Message}", ex);
                
                // Implement error recovery logic if needed
                // For example, restart the service or show user notification
            }
            catch (Exception logEx)
            {
                Logger.Error($"Error handling interactive rendering error: {logEx.Message}", logEx);
            }
        }

        #endregion

        #region Device Event Handlers

        private async void OnDeviceConnected(object? sender, DeviceEventArgs e)
        {
            try
            {
                Logger.Info($"Device connected: {e.Device.ProductString} (Serial: {e.Device.SerialNumber})");
                
                if (_offlineMediaDataService != null && !string.IsNullOrEmpty(e.Device.SerialNumber))
                {
                    // Update device information in offline data
                    _offlineMediaDataService.UpdateDeviceInfo(e.Device.SerialNumber, e.Device, isConnected: true);
                    
                    // Sync media files from local storage for this device
                    _offlineMediaDataService.SyncMediaFilesFromLocal(e.Device.SerialNumber);

                    // Get device GetDevicePlaybackMode
                    var playbackMode = _offlineMediaDataService.GetDevicePlaybackMode(e.Device.SerialNumber);
                    // if is offline mode, switch to online mode
                    if (playbackMode == PlaybackMode.OfflineVideo)
                    {
                        await Task.Delay(10000); // Wait a moment for device to stabilize
                        Logger.Info($"Switching device {e.Device.SerialNumber} to Online mode");
                        await _hidDeviceService.SetRealTimeDisplayAsync(false); // Disable real-time mode
                    }

                }

                // Notify Interactive Rendering Service about device connection
                if (_interactiveRenderingService != null)
                {
                    // The service will automatically detect the new device through ServiceLocator.HidDeviceService
                    // No additional action needed as the service polls for device status
                    Logger.Info("Interactive Rendering Service will automatically detect the new device");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling device connection: {ex.Message}", ex);
            }
        }

        private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
        {
            try
            {
                Logger.Info($"Device disconnected: {e.Device.ProductString} (Serial: {e.Device.SerialNumber})");
                
                if (_offlineMediaDataService != null && !string.IsNullOrEmpty(e.Device.SerialNumber))
                {
                    // Update connection status in offline data
                    _offlineMediaDataService.SetDeviceConnectionStatus(e.Device.SerialNumber, isConnected: false);
                }

                // Interactive Rendering Service will automatically handle disconnection
                // through its event handlers subscribed to HidDeviceService
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling device disconnection: {ex.Message}", ex);
            }
        }

        #endregion

        #region System Sleep Event Handlers

        private void OnSystemEnteringSleep(object? sender, SystemSleepEventArgs e)
        {
            try
            {
                Logger.Info($"System entering sleep mode at {e.Timestamp}");
                
                // Pause Interactive Rendering Service during sleep to save resources
                if (_interactiveRenderingService?.IsAutoRenderingEnabled == true)
                {
                    _interactiveRenderingService.StopAutoRendering();
                    Logger.Info("Interactive rendering paused for system sleep");
                }

                // Pause Realtime JPEG Transmission Service during sleep
                if (_realtimeJpegTransmissionService != null)
                {
                    // Clear any pending queue to prevent stale data after resume
                    var queueSize = _realtimeJpegTransmissionService.QueueSize;
                    if (queueSize > 0)
                    {
                        _realtimeJpegTransmissionService.ClearQueue();
                        Logger.Info($"Cleared {queueSize} items from JPEG transmission queue for system sleep");
                    }

                    // The service will automatically disable real-time mode when queue becomes empty
                    Logger.Info("Realtime JPEG transmission service prepared for system sleep");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling system entering sleep: {ex.Message}", ex);
            }
        }

        private void OnSystemResumingFromSleep(object? sender, SystemSleepEventArgs e)
        {
            try
            {
                Logger.Info($"System resuming from sleep mode at {e.Timestamp}");
                
                // Resume Interactive Rendering Service after sleep if it was running
                // Note: This is optional - you might want to let individual pages control this
                Logger.Info("Interactive rendering service available for resumption by individual pages");

                // Realtime JPEG Transmission Service will automatically resume when new data is queued
                if (_realtimeJpegTransmissionService != null)
                {
                    Logger.Info("Realtime JPEG transmission service ready for resumption");
                    
                    // Log current service status
                    var stats = _realtimeJpegTransmissionService.Statistics;
                    Logger.Info($"JPEG Service Status: {stats}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling system resuming from sleep: {ex.Message}", ex);
            }
        }

        private void OnDeviceSleepModeChanged(object? sender, DeviceSleepModeEventArgs e)
        {
            try
            {
                Logger.Info($"Device sleep mode changed: enabled={e.SleepModeEnabled}, successful={e.SuccessfulDevices}/{e.TotalDevices} devices");
                
                // Log individual device results if needed
                foreach (var result in e.DeviceResults)
                {
                    if (result.Value)
                    {
                        Logger.Info($"Device {result.Key}: sleep mode {(e.SleepModeEnabled ? "enabled" : "disabled")} successfully");
                    }
                    else
                    {
                        Logger.Warn($"Device {result.Key}: failed to {(e.SleepModeEnabled ? "enable" : "disable")} sleep mode");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling device sleep mode change: {ex.Message}", ex);
            }
        }

        #endregion

        #region Simplified Realtime JPEG Transmission Service Event Handlers

        private void OnRealtimeJpegFrameProcessed(object? sender, JpegFrameProcessedEventArgs e)
        {
            try
            {
                // Log successful transmissions (but avoid too much noise - only log periodically)
                if (e.TransferId % 30 == 0) // Log every 30th transmission
                {
                    Logger.Info($"JPEG Frame Sent: ID={e.TransferId}, Size={e.Frame.Data.Length} bytes, " +
                              $"Success={e.SuccessfulDevices}/{e.TotalDevices} devices, Metadata={e.Frame.Metadata}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling realtime JPEG frame processed: {ex.Message}", ex);
            }
        }

        private void OnRealtimeJpegFrameDropped(object? sender, JpegFrameDroppedEventArgs e)
        {
            try
            {
                Logger.Warn($"JPEG Frame Dropped: Size={e.Frame.Data.Length} bytes, " +
                          $"Reason={e.Reason}, Metadata={e.Frame.Metadata}, Time={e.Timestamp:HH:mm:ss.fff}");
                
                // Application-level logic for dropped frames
                // You might want to adjust quality settings or notify user
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling realtime JPEG frame dropped: {ex.Message}", ex);
            }
        }

        private void OnRealtimeJpegRealTimeModeChanged(object? sender, RealtimeModeChangedEventArgs e)
        {
            try
            {
                Logger.Info($"JPEG Transmission Real-time Mode {(e.IsEnabled ? "ENABLED" : "DISABLED")}: " +
                          $"Success on {e.SuccessfulDevices}/{e.TotalDevices} devices at {e.Timestamp:HH:mm:ss.fff}");
                
                // You can add application-level logic here
                // For example, notify other services or update UI indicators
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling realtime JPEG real-time mode change: {ex.Message}", ex);
            }
        }

        private void OnRealtimeJpegServiceError(object? sender, RealtimeServiceErrorEventArgs e)
        {
            try
            {
                Logger.Error($"JPEG Transmission Error: {e.Context} - {e.Exception.Message}", e.Exception);
                
                // Application-level error recovery
                // You might want to restart services or notify user of issues
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling realtime JPEG service error: {ex.Message}", ex);
            }
        }

        private void OnRealtimeJpegDeviceConnectionChanged(object? sender, DeviceConnectionChangedEventArgs e)
        {
            try
            {
                Logger.Info($"JPEG Transmission Service: Device {(e.IsConnected ? "connected" : "disconnected")} - " +
                          $"{e.Device.ProductString} (Serial: {e.Device.SerialNumber}). " +
                          $"Total connected devices: {e.TotalConnectedDevices}");
                
                if (!e.IsConnected && e.TotalConnectedDevices == 0)
                {
                    Logger.Info("JPEG Transmission Service paused - no devices connected");
                }
                else if (e.IsConnected && e.TotalConnectedDevices == 1)
                {
                    Logger.Info("JPEG Transmission Service resumed - first device connected");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling realtime JPEG device connection change: {ex.Message}", ex);
            }
        }

        #endregion

    }

}
