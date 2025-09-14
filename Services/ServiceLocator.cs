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
        private static RealtimeJpegTransmissionService? _realtimeJpegTransmissionService;
        private static HidSwapChainService? _hidSwapChainService;
        private static HidRealTimeRenderer? _hidRealTimeRenderer;

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
        /// Gets the Realtime JPEG Transmission Service instance
        /// </summary>
        public static RealtimeJpegTransmissionService RealtimeJpegTransmissionService
        {
            get
            {
                if (_realtimeJpegTransmissionService == null)
                {
                    throw new InvalidOperationException("RealtimeJpegTransmissionService is not initialized. Call InitializeRealtimeJpegService() first.");
                }
                return _realtimeJpegTransmissionService;
            }
        }

        /// <summary>
        /// Gets the HID SwapChain Service instance
        /// </summary>
        public static HidSwapChainService HidSwapChainService
        {
            get
            {
                if (_hidSwapChainService == null)
                {
                    throw new InvalidOperationException("HidSwapChainService is not initialized. Call InitializeHidSwapChainService() first.");
                }
                return _hidSwapChainService;
            }
        }

        /// <summary>
        /// Gets the HID Real-Time Renderer instance
        /// </summary>
        public static HidRealTimeRenderer HidRealTimeRenderer
        {
            get
            {
                if (_hidRealTimeRenderer == null)
                {
                    throw new InvalidOperationException("HidRealTimeRenderer is not initialized. Call InitializeHidRealTimeRenderer() first.");
                }
                return _hidRealTimeRenderer;
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
        /// Initialize the Realtime JPEG Transmission Service
        /// </summary>
        /// <param name="realtimeJpegTransmissionService">The service instance to register</param>
        internal static void InitializeRealtimeJpegTransmissionService(RealtimeJpegTransmissionService realtimeJpegTransmissionService)
        {
            _realtimeJpegTransmissionService = realtimeJpegTransmissionService;
        }

        /// <summary>
        /// Initialize the HID SwapChain Service
        /// </summary>
        /// <param name="bufferCount">Number of frame buffers (2=double, 3=triple buffering)</param>
        /// <param name="swapChainMode">SwapChain presentation mode</param>
        /// <param name="presentMode">Present synchronization mode</param>
        /// <param name="targetRefreshRate">Target refresh rate in Hz (default: 30)</param>
        /// <returns>The created HidSwapChainService instance</returns>
        internal static HidSwapChainService InitializeHidSwapChainService(
            int bufferCount = 3,
            SwapChainMode swapChainMode = SwapChainMode.Discard,
            PresentMode presentMode = PresentMode.Immediate,
            int targetRefreshRate = 30)
        {
            if (_hidDeviceService == null)
            {
                throw new InvalidOperationException("HidDeviceService must be initialized before HidSwapChainService.");
            }

            _hidSwapChainService = new HidSwapChainService(
                _hidDeviceService,
                bufferCount,
                swapChainMode,
                presentMode,
                targetRefreshRate
            );

            return _hidSwapChainService;
        }

        /// <summary>
        /// Initialize the HID Real-Time Renderer
        /// </summary>
        /// <param name="targetFps">Target frame rate (default: 30 FPS)</param>
        /// <param name="bufferCount">Number of frame buffers (2=double, 3=triple buffering)</param>
        /// <param name="enableVsync">Enable VSync presentation mode</param>
        /// <returns>The created HidRealTimeRenderer instance</returns>
        internal static HidRealTimeRenderer InitializeHidRealTimeRenderer(
            int targetFps = 30,
            int bufferCount = 3,
            bool enableVsync = false)
        {
            if (_hidDeviceService == null)
            {
                throw new InvalidOperationException("HidDeviceService must be initialized before HidRealTimeRenderer.");
            }

            _hidRealTimeRenderer = new HidRealTimeRenderer(
                _hidDeviceService,
                targetFps,
                bufferCount,
                enableVsync
            );

            return _hidRealTimeRenderer;
        }

        /// <summary>
        /// Initialize the Realtime JPEG Transmission Service with automatic creation
        /// </summary>
        /// <param name="processingIntervalMs">Processing interval in milliseconds (default: 33ms ~30FPS)</param>
        /// <param name="maxQueueSize">Maximum queue size before dropping frames (default: 10)</param>
        /// <param name="maxRetryAttempts">Maximum retry attempts for failed transmissions (default: 3)</param>
        /// <returns>The created service instance</returns>
        internal static RealtimeJpegTransmissionService InitializeRealtimeJpegTransmissionService(
            int processingIntervalMs = 33,
            int maxQueueSize = 10,
            int maxRetryAttempts = 3)
        {
            if (_hidDeviceService == null)
            {
                throw new InvalidOperationException("HidDeviceService must be initialized before RealtimeJpegTransmissionService. Call Initialize() first.");
            }

            _realtimeJpegTransmissionService = new RealtimeJpegTransmissionService(
                _hidDeviceService,
                processingIntervalMs,
                maxQueueSize,
                maxRetryAttempts
            );

            return _realtimeJpegTransmissionService;
        }

        /// <summary>
        /// Initialize all services
        /// </summary>
        internal static void InitializeAll(
            HidDeviceService hidDeviceService, 
            OfflineMediaDataService offlineMediaDataService, 
            SystemSleepMonitorService? systemSleepMonitorService = null,
            InteractiveWin2DRenderingService? interactiveRenderingService = null,
            RealtimeJpegTransmissionService? realtimeJpegTransmissionService = null,
            HidSwapChainService? hidSwapChainService = null,
            HidRealTimeRenderer? hidRealTimeRenderer = null)
        {
            _hidDeviceService = hidDeviceService;
            _offlineMediaDataService = offlineMediaDataService;
            _systemSleepMonitorService = systemSleepMonitorService;
            _interactiveRenderingService = interactiveRenderingService;
            _realtimeJpegTransmissionService = realtimeJpegTransmissionService;
            _hidSwapChainService = hidSwapChainService;
            _hidRealTimeRenderer = hidRealTimeRenderer;
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
        /// Gets whether Realtime JPEG Transmission Service is initialized
        /// </summary>
        public static bool IsRealtimeJpegTransmissionServiceInitialized => _realtimeJpegTransmissionService != null;

        /// <summary>
        /// Gets whether HID SwapChain Service is initialized
        /// </summary>
        public static bool IsHidSwapChainServiceInitialized => _hidSwapChainService != null;

        /// <summary>
        /// Gets whether HID Real-Time Renderer is initialized
        /// </summary>
        public static bool IsHidRealTimeRendererInitialized => _hidRealTimeRenderer != null;

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
        /// Try to get the Realtime JPEG Transmission Service without throwing an exception
        /// </summary>
        /// <returns>The service instance or null if not initialized</returns>
        public static RealtimeJpegTransmissionService? TryGetRealtimeJpegTransmissionService()
        {
            return _realtimeJpegTransmissionService;
        }

        /// <summary>
        /// Try to get the HID SwapChain Service without throwing an exception
        /// </summary>
        /// <returns>The service instance or null if not initialized</returns>
        public static HidSwapChainService? TryGetHidSwapChainService()
        {
            return _hidSwapChainService;
        }

        /// <summary>
        /// Try to get the HID Real-Time Renderer without throwing an exception
        /// </summary>
        /// <returns>The service instance or null if not initialized</returns>
        public static HidRealTimeRenderer? TryGetHidRealTimeRenderer()
        {
            return _hidRealTimeRenderer;
        }

        /// <summary>
        /// Create and initialize the Realtime JPEG Transmission Service if not already created
        /// </summary>
        /// <param name="processingIntervalMs">Processing interval in milliseconds (default: 33ms ~30FPS)</param>
        /// <param name="maxQueueSize">Maximum queue size before dropping frames (default: 10)</param>
        /// <param name="maxRetryAttempts">Maximum retry attempts for failed transmissions (default: 3)</param>
        /// <returns>The service instance (existing or newly created)</returns>
        public static RealtimeJpegTransmissionService GetOrCreateRealtimeJpegTransmissionService(
            int processingIntervalMs = 33,
            int maxQueueSize = 10,
            int maxRetryAttempts = 3)
        {
            if (_realtimeJpegTransmissionService != null)
            {
                return _realtimeJpegTransmissionService;
            }

            return InitializeRealtimeJpegTransmissionService(processingIntervalMs, maxQueueSize, maxRetryAttempts);
        }

        /// <summary>
        /// Create and initialize the HID SwapChain Service if not already created
        /// </summary>
        /// <param name="bufferCount">Number of frame buffers (2=double, 3=triple buffering)</param>
        /// <param name="swapChainMode">SwapChain presentation mode</param>
        /// <param name="presentMode">Present synchronization mode</param>
        /// <param name="targetRefreshRate">Target refresh rate in Hz (default: 30)</param>
        /// <returns>The service instance (existing or newly created)</returns>
        public static HidSwapChainService GetOrCreateHidSwapChainService(
            int bufferCount = 3,
            SwapChainMode swapChainMode = SwapChainMode.Discard,
            PresentMode presentMode = PresentMode.Immediate,
            int targetRefreshRate = 30)
        {
            if (_hidSwapChainService != null)
            {
                return _hidSwapChainService;
            }

            return InitializeHidSwapChainService(bufferCount, swapChainMode, presentMode, targetRefreshRate);
        }

        /// <summary>
        /// Create and initialize the HID Real-Time Renderer if not already created
        /// </summary>
        /// <param name="targetFps">Target frame rate (default: 30 FPS)</param>
        /// <param name="bufferCount">Number of frame buffers (2=double, 3=triple buffering)</param>
        /// <param name="enableVsync">Enable VSync presentation mode</param>
        /// <returns>The service instance (existing or newly created)</returns>
        public static HidRealTimeRenderer GetOrCreateHidRealTimeRenderer(
            int targetFps = 30,
            int bufferCount = 3,
            bool enableVsync = false)
        {
            if (_hidRealTimeRenderer != null)
            {
                return _hidRealTimeRenderer;
            }

            return InitializeHidRealTimeRenderer(targetFps, bufferCount, enableVsync);
        }

        /// <summary>
        /// Cleanup all services
        /// </summary>
        internal static void Cleanup()
        {
            // Stop and dispose HID Real-Time Renderer first
            if (_hidRealTimeRenderer != null)
            {
                try
                {
                    _hidRealTimeRenderer.StopRendering();
                    _hidRealTimeRenderer.Dispose();
                }
                catch (Exception ex)
                {
                    // Log error but continue cleanup
                    System.Diagnostics.Debug.WriteLine($"Error disposing HidRealTimeRenderer: {ex.Message}");
                }
                finally
                {
                    _hidRealTimeRenderer = null;
                }
            }

            // Stop and dispose HID SwapChain Service
            if (_hidSwapChainService != null)
            {
                try
                {
                    _hidSwapChainService.StopPresentation();
                    _hidSwapChainService.Dispose();
                }
                catch (Exception ex)
                {
                    // Log error but continue cleanup
                    System.Diagnostics.Debug.WriteLine($"Error disposing HidSwapChainService: {ex.Message}");
                }
                finally
                {
                    _hidSwapChainService = null;
                }
            }

            // Stop and dispose Realtime JPEG Transmission Service
            if (_realtimeJpegTransmissionService != null)
            {
                try
                {
                    _realtimeJpegTransmissionService.ClearQueue();
                    _realtimeJpegTransmissionService.Dispose();
                }
                catch (Exception ex)
                {
                    // Log error but continue cleanup
                    System.Diagnostics.Debug.WriteLine($"Error disposing RealtimeJpegTransmissionService: {ex.Message}");
                }
                finally
                {
                    _realtimeJpegTransmissionService = null;
                }
            }

            // Stop and dispose Interactive Rendering Service
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