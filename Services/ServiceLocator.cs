using System;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// Simple service locator for accessing services throughout the application
    /// </summary>
    public static class ServiceLocator
    {
        private static HidDeviceService? _hidDeviceService;

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
        /// Initialize the service locator with the HID Device Service
        /// </summary>
        internal static void Initialize(HidDeviceService hidDeviceService)
        {
            _hidDeviceService = hidDeviceService;
        }

        /// <summary>
        /// Cleanup all services
        /// </summary>
        internal static void Cleanup()
        {
            _hidDeviceService?.Dispose();
            _hidDeviceService = null;
        }
    }
}