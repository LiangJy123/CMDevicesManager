using CMDevicesManager.Services;
using CMDevicesManager.Helper;

namespace CDMDevicesManagerDevWinUI
{
    public partial class App : Application
    {
        public new static App Current => (App)Application.Current;
        public static Window MainWindow = Window.Current;
        public static IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
        public JsonNavigationService NavService { get; set; }
        public IThemeService ThemeService { get; set; }


        private HidDeviceService? _hidDeviceService;
        private OfflineMediaDataService? _offlineMediaDataService;

        public App()
        {
            this.InitializeComponent();
            NavService = new JsonNavigationService();
            InitializeServices();

            // Enables Multicore JIT with the specified profile
            System.Runtime.ProfileOptimization.SetProfileRoot(Constants.RootDirectoryPath);
            System.Runtime.ProfileOptimization.StartProfile("Startup.Profile");
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();

            MainWindow.Title = MainWindow.AppWindow.Title = ProcessInfoHelper.ProductNameAndVersion;
            MainWindow.AppWindow.SetIcon("Assets/AppIcon.ico");

            ThemeService = new ThemeService(MainWindow);

            MainWindow.Activate();

            InitializeApp();
        }

        private async void InitializeApp()
        {
            if (RuntimeHelper.IsPackaged())
            {
                ContextMenuItem menu = new ContextMenuItem
                {
                    Title = "Open CDMDevicesManagerDevWinUI Here",
                    Param = @"""{path}""",
                    AcceptFileFlag = (int)FileMatchFlagEnum.All,
                    AcceptDirectoryFlag = (int)(DirectoryMatchFlagEnum.Directory | DirectoryMatchFlagEnum.Background | DirectoryMatchFlagEnum.Desktop),
                    AcceptMultipleFilesFlag = (int)FilesMatchFlagEnum.Each,
                    Index = 0,
                    Enabled = true,
                    Icon = ProcessInfoHelper.GetFileVersionInfo().FileName,
                    Exe = "CDMDevicesManagerDevWinUI.exe"
                };

                ContextMenuService menuService = new ContextMenuService();
                await menuService.SaveAsync(menu);
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

                // Initialize System Sleep Monitor Service
                Logger.Info("Initializing System Sleep Monitor Service");
                //var systemSleepMonitorService = new SystemSleepMonitorService(_hidDeviceService);

                // Initialize the service locator with all services
                //ServiceLocator.InitializeAll(_hidDeviceService, _offlineMediaDataService, systemSleepMonitorService);
                ServiceLocator.InitializeAll(_hidDeviceService, _offlineMediaDataService);

                // Start monitoring system sleep events
                //systemSleepMonitorService.StartMonitoring();

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
    }

}
