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
        /// Initialize all services
        /// </summary>
        internal static void InitializeAll(HidDeviceService hidDeviceService, OfflineMediaDataService offlineMediaDataService)
        {
            _hidDeviceService = hidDeviceService;
            _offlineMediaDataService = offlineMediaDataService;
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
        /// Cleanup all services
        /// </summary>
        internal static void Cleanup()
        {
            _hidDeviceService?.Dispose();
            _hidDeviceService = null;

            _offlineMediaDataService?.Dispose();
            _offlineMediaDataService = null;
        }
    }
}