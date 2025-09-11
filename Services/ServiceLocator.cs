using System;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// Simple service locator for accessing services throughout the application
    /// </summary>
    public static class ServiceLocator
    {
        private static HidDeviceService? _hidDeviceService;
<<<<<<< HEAD
=======
        private static OfflineMediaDataService? _offlineMediaDataService;
        private static SystemSleepMonitorService? _systemSleepMonitorService;
>>>>>>> eddcd56aea4c1497b4c62232999fcd43228fbc3d

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
<<<<<<< HEAD
=======
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
>>>>>>> eddcd56aea4c1497b4c62232999fcd43228fbc3d
        /// Initialize the service locator with the HID Device Service
        /// </summary>
        internal static void Initialize(HidDeviceService hidDeviceService)
        {
            _hidDeviceService = hidDeviceService;
        }

        /// <summary>
<<<<<<< HEAD
=======
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
        /// Initialize all services
        /// </summary>
        internal static void InitializeAll(HidDeviceService hidDeviceService, OfflineMediaDataService offlineMediaDataService, SystemSleepMonitorService? systemSleepMonitorService = null)
        {
            _hidDeviceService = hidDeviceService;
            _offlineMediaDataService = offlineMediaDataService;
            _systemSleepMonitorService = systemSleepMonitorService;
        }

        /// <summary>
        /// Gets whether all services are initialized
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
>>>>>>> eddcd56aea4c1497b4c62232999fcd43228fbc3d
        /// Cleanup all services
        /// </summary>
        internal static void Cleanup()
        {
<<<<<<< HEAD
            _hidDeviceService?.Dispose();
            _hidDeviceService = null;
=======
            _systemSleepMonitorService?.Dispose();
            _systemSleepMonitorService = null;

            _hidDeviceService?.Dispose();
            _hidDeviceService = null;

            _offlineMediaDataService?.Dispose();
            _offlineMediaDataService = null;
>>>>>>> eddcd56aea4c1497b4c62232999fcd43228fbc3d
        }
    }
}