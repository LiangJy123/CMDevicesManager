using CMDevicesManager.Helper;
using CMDevicesManager.Services;
using System.Configuration;
using System.Data;
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
                
                // Initialize the service locator with both services
                ServiceLocator.InitializeAll(_hidDeviceService, _offlineMediaDataService);
                
                // Initialize the HID service with default VID/PID values
                // You can customize these values based on your devices
                await _hidDeviceService.InitializeAsync(
                    vendorId: 0x2516,   // Your device's vendor ID
                    productId: 0x0228,  // Your device's product ID
                    usagePage: 0xFFFF   // Your device's usage page
                );

                // Set up event handlers for device connection/disconnection to update offline data
                _hidDeviceService.DeviceConnected += OnDeviceConnected;
                _hidDeviceService.DeviceDisconnected += OnDeviceDisconnected;
                
                Logger.Info("All services initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize services", ex);
                // Don't throw here - let the app continue with reduced functionality
            }
        }

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
                    
                    // Get device controller and update firmware info if available
                    var controller = _hidDeviceService?.GetController(e.Device.Path);
                    if (controller?.DeviceFWInfo != null)
                    {
                        _offlineMediaDataService.UpdateDeviceFirmwareInfo(e.Device.SerialNumber, controller.DeviceFWInfo);
                    }
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
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling device disconnection: {ex.Message}", ex);
            }
        }

        private async void InitializeHidDeviceService()
        {
            try
            {
                Logger.Info("Initializing HID Device Service");
                
                _hidDeviceService = new HidDeviceService();
                
                // Initialize the service locator
                ServiceLocator.Initialize(_hidDeviceService);
                
                // Initialize the service with default VID/PID values
                // You can customize these values based on your devices
                await _hidDeviceService.InitializeAsync(
                    vendorId: 0x2516,   // Your device's vendor ID
                    productId: 0x0228,  // Your device's product ID
                    usagePage: 0xFFFF   // Your device's usage page
                );
                
                Logger.Info("HID Device Service initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize HID Device Service", ex);
                // Don't throw here - let the app continue without HID functionality
            }
        }
    }

}
