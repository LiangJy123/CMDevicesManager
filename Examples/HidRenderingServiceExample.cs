using CMDevicesManager.Services;
using System;
using System.Threading.Tasks;

namespace CMDevicesManager.Examples
{
    /// <summary>
    /// Example demonstrating how to use BackgroundRenderingService with HID device integration
    /// for sending real-time rendered data to HID devices
    /// </summary>
    public class HidRenderingServiceExample : IDisposable
    {
        private BackgroundRenderingService? _renderingService;
        private bool _disposed = false;

        /// <summary>
        /// Initialize the rendering service with HID integration
        /// </summary>
        public async Task InitializeAsync(int width = 480, int height = 480)
        {
            try
            {
                Console.WriteLine("Initializing Background Rendering Service with HID integration...");
                
                _renderingService = new BackgroundRenderingService();
                
                // Subscribe to events for monitoring
                _renderingService.HidStatusChanged += OnHidStatusChanged;
                _renderingService.JpegDataSentToHid += OnJpegDataSentToHid;
                _renderingService.RenderingError += OnRenderingError;
                
                // Initialize the service
                await _renderingService.InitializeAsync(width, height);
                
                Console.WriteLine($"Service initialized. HID Service Connected: {_renderingService.IsHidServiceConnected}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Start real-time rendering and sending to HID devices
        /// </summary>
        public async Task StartRealTimeRenderingAsync(int fps = 30, int jpegQuality = 85)
        {
            if (_renderingService == null)
            {
                throw new InvalidOperationException("Service not initialized. Call InitializeAsync first.");
            }

            try
            {
                Console.WriteLine("Starting real-time rendering with HID transfer...");
                
                // Configure rendering settings
                _renderingService.TargetFPS = fps;
                _renderingService.JpegQuality = jpegQuality;
                
                // Enable HID transfer in real-time mode (not suspend media)
                _renderingService.EnableHidTransfer(true, useSuspendMedia: false);
                
                // Start the rendering loop
                await _renderingService.StartAsync();
                
                Console.WriteLine($"Real-time rendering started at {fps} FPS with {jpegQuality}% JPEG quality");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start real-time rendering: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Send current frame using suspend media functionality
        /// </summary>
        public async Task SendCurrentFrameAsSuspendMediaAsync()
        {
            if (_renderingService == null)
            {
                throw new InvalidOperationException("Service not initialized. Call InitializeAsync first.");
            }

            try
            {
                Console.WriteLine("Sending current frame as suspend media...");
                
                var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), 
                    $"example_frame_{DateTime.Now:yyyyMMddHHmmss}.jpg");
                
                var success = await _renderingService.SaveCurrentFrameAndSendAsSuspendMediaAsync(tempPath);
                
                if (success)
                {
                    Console.WriteLine("Frame sent as suspend media successfully");
                }
                else
                {
                    Console.WriteLine("Failed to send frame as suspend media");
                }
                
                // Cleanup
                try
                {
                    if (System.IO.File.Exists(tempPath))
                        System.IO.File.Delete(tempPath);
                }
                catch { /* Ignore cleanup errors */ }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending suspend media: {ex.Message}");
            }
        }

        /// <summary>
        /// Switch devices to suspend mode
        /// </summary>
        public async Task SetDevicesToSuspendModeAsync()
        {
            if (_renderingService == null)
            {
                throw new InvalidOperationException("Service not initialized. Call InitializeAsync first.");
            }

            try
            {
                Console.WriteLine("Setting devices to suspend mode...");
                
                var success = await _renderingService.SetSuspendModeAsync();
                
                if (success)
                {
                    Console.WriteLine("Devices switched to suspend mode successfully");
                }
                else
                {
                    Console.WriteLine("Failed to set devices to suspend mode");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting suspend mode: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop real-time rendering
        /// </summary>
        public async Task StopRenderingAsync()
        {
            if (_renderingService == null)
                return;

            try
            {
                Console.WriteLine("Stopping real-time rendering...");
                
                await _renderingService.StopAsync();
                
                Console.WriteLine("Rendering stopped");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping rendering: {ex.Message}");
            }
        }

        /// <summary>
        /// Get rendering statistics
        /// </summary>
        public void PrintStatistics()
        {
            if (_renderingService == null)
            {
                Console.WriteLine("Service not initialized");
                return;
            }

            Console.WriteLine("\n=== Rendering Service Statistics ===");
            Console.WriteLine($"HID Service Connected: {_renderingService.IsHidServiceConnected}");
            Console.WriteLine($"Real-Time Mode Enabled: {_renderingService.IsHidRealTimeModeEnabled}");
            Console.WriteLine($"Target FPS: {_renderingService.TargetFPS}");
            Console.WriteLine($"JPEG Quality: {_renderingService.JpegQuality}%");
            Console.WriteLine($"Frames Sent to HID: {_renderingService.HidFramesSent}");
            Console.WriteLine($"Send to HID Enabled: {_renderingService.SendToHidDevices}");
            Console.WriteLine($"Use Suspend Media: {_renderingService.UseSuspendMedia}");
            Console.WriteLine($"Canvas Size: {_renderingService.Width}x{_renderingService.Height}");
            Console.WriteLine($"Is Running: {_renderingService.IsRunning}");
        }

        // Event Handlers
        private void OnHidStatusChanged(string status)
        {
            Console.WriteLine($"[HID] {status}");
        }

        private void OnJpegDataSentToHid(byte[] jpegData)
        {
            Console.WriteLine($"[HID] JPEG frame sent - Size: {jpegData.Length:N0} bytes");
        }

        private void OnRenderingError(Exception ex)
        {
            Console.WriteLine($"[ERROR] Rendering error: {ex.Message}");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                StopRenderingAsync().Wait(3000);
            }
            catch
            {
                // Ignore errors during shutdown
            }

            _renderingService?.Dispose();
            
            Console.WriteLine("HidRenderingServiceExample disposed");
        }
    }

    /// <summary>
    /// Usage example for console applications
    /// </summary>
    public static class HidRenderingUsageExample
    {
        public static async Task RunExampleAsync()
        {
            using var example = new HidRenderingServiceExample();

            try
            {
                // Initialize with 480x480 resolution
                await example.InitializeAsync(480, 480);

                // Print initial statistics
                example.PrintStatistics();

                // Start real-time rendering at 15 FPS with 80% JPEG quality
                await example.StartRealTimeRenderingAsync(fps: 15, jpegQuality: 80);

                // Let it run for 10 seconds
                Console.WriteLine("Real-time rendering active for 10 seconds...");
                await Task.Delay(10000);

                // Print statistics after running
                example.PrintStatistics();

                // Send a single frame as suspend media
                await example.SendCurrentFrameAsSuspendMediaAsync();

                // Set devices to suspend mode
                await example.SetDevicesToSuspendModeAsync();

                // Stop rendering
                await example.StopRenderingAsync();

                Console.WriteLine("Example completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Example failed: {ex.Message}");
            }
        }
    }
}