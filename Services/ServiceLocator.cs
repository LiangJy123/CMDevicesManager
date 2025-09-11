using System;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// Simple service locator for accessing services throughout the application
    /// </summary>
    public static class ServiceLocator
    {
        private static HidDeviceService? _hidDeviceService;
        private static OfflineMediaDataService? _offlineMediaDataService;
        private static SystemSleepMonitorService? _systemSleepMonitorService;
        private static InteractiveWin2DRenderingService? _interactiveRenderingService;

        /// <summary>
        /// Gets the HID Device Service instance
        /// </summary>
        public static HidDeviceService HidDeviceService
        {
            get
            {
                if (_hidDeviceService == null)
                {
                    throw new InvalidOperationException("HidDeviceService is not initialized. Call Initialize() first.");
                }
                return _hidDeviceService;
            }
        }

        /// <summary>
        /// Gets the Offline Media Data Service instance
        /// </summary>
        public static OfflineMediaDataService OfflineMediaDataService
        {
            get
            {
                if (_offlineMediaDataService == null)
                {
                    throw new InvalidOperationException("OfflineMediaDataService is not initialized. Call Initialize() first.");
                }
                return _offlineMediaDataService;
            }
        }

        /// <summary>
        /// Gets the System Sleep Monitor Service instance
        /// </summary>
        public static SystemSleepMonitorService SystemSleepMonitorService
        {
            get
            {
                if (_systemSleepMonitorService == null)
                {
                    throw new InvalidOperationException("SystemSleepMonitorService is not initialized. Call Initialize() first.");
                }
                return _systemSleepMonitorService;
            }
        }

        /// <summary>
        /// Gets the Interactive Win2D Rendering Service instance
        /// </summary>
        public static InteractiveWin2DRenderingService InteractiveRenderingService
        {
            get
            {
                if (_interactiveRenderingService == null)
                {
                    throw new InvalidOperationException("InteractiveWin2DRenderingService is not initialized. Call Initialize() first.");
                }
                return _interactiveRenderingService;
            }
        }

        /// <summary>
        /// Initialize the service locator with the HID Device Service
        /// </summary>
        internal static void Initialize(HidDeviceService hidDeviceService)
        {
            _hidDeviceService = hidDeviceService;
        }

        /// <summary>
        /// Initialize the service locator with the Offline Media Data Service
        /// </summary>
        internal static void InitializeOfflineMediaService(OfflineMediaDataService offlineMediaDataService)
        {
            _offlineMediaDataService = offlineMediaDataService;
        }

        /// <summary>
        /// Initialize the System Sleep Monitor Service
        /// </summary>
        internal static void InitializeSystemSleepMonitorService(SystemSleepMonitorService systemSleepMonitorService)
        {
            _systemSleepMonitorService = systemSleepMonitorService;
        }

        /// <summary>
        /// Initialize the Interactive Win2D Rendering Service
        /// </summary>
        internal static void InitializeInteractiveRenderingService(InteractiveWin2DRenderingService interactiveRenderingService)
        {
            _interactiveRenderingService = interactiveRenderingService;
        }

        /// <summary>
        /// Initialize all services
        /// </summary>
        internal static void InitializeAll(
            HidDeviceService hidDeviceService, 
            OfflineMediaDataService offlineMediaDataService, 
            SystemSleepMonitorService? systemSleepMonitorService = null,
            InteractiveWin2DRenderingService? interactiveRenderingService = null)
        {
            _hidDeviceService = hidDeviceService;
            _offlineMediaDataService = offlineMediaDataService;
            _systemSleepMonitorService = systemSleepMonitorService;
            _interactiveRenderingService = interactiveRenderingService;
        }

        /// <summary>
        /// Gets whether all core services are initialized
        /// </summary>
        public static bool IsInitialized => _hidDeviceService != null && _offlineMediaDataService != null;

        /// <summary>
        /// Gets whether HID Device Service is initialized
        /// </summary>
        public static bool IsHidDeviceServiceInitialized => _hidDeviceService != null;

        /// <summary>
        /// Gets whether Offline Media Data Service is initialized
        /// </summary>
        public static bool IsOfflineMediaServiceInitialized => _offlineMediaDataService != null;

        /// <summary>
        /// Gets whether System Sleep Monitor Service is initialized
        /// </summary>
        public static bool IsSystemSleepMonitorServiceInitialized => _systemSleepMonitorService != null;

        /// <summary>
        /// Gets whether Interactive Win2D Rendering Service is initialized
        /// </summary>
        public static bool IsInteractiveRenderingServiceInitialized => _interactiveRenderingService != null;

        /// <summary>
        /// Try to get the Interactive Win2D Rendering Service without throwing an exception
        /// </summary>
        /// <returns>The service instance or null if not initialized</returns>
        public static InteractiveWin2DRenderingService? TryGetInteractiveRenderingService()
        {
            return _interactiveRenderingService;
        }

        /// <summary>
        /// Try to get the HID Device Service without throwing an exception
        /// </summary>
        /// <returns>The service instance or null if not initialized</returns>
        public static HidDeviceService? TryGetHidDeviceService()
        {
            return _hidDeviceService;
        }

        /// <summary>
        /// Cleanup all services
        /// </summary>
        internal static void Cleanup()
        {
            // Stop and dispose Interactive Rendering Service first
            if (_interactiveRenderingService != null)
            {
                try
                {
                    _interactiveRenderingService.StopAutoRendering();
                    _interactiveRenderingService.EnableHidTransfer(false);
                    _interactiveRenderingService.Dispose();
                }
                catch (Exception ex)
                {
                    // Log error but continue cleanup
                    System.Diagnostics.Debug.WriteLine($"Error disposing InteractiveWin2DRenderingService: {ex.Message}");
                }
                finally
                {
                    _interactiveRenderingService = null;
                }
            }

            // Cleanup other services
            _systemSleepMonitorService?.Dispose();
            _systemSleepMonitorService = null;

            _hidDeviceService?.Dispose();
            _hidDeviceService = null;

            _offlineMediaDataService?.Dispose();
            _offlineMediaDataService = null;
        }
    }
}