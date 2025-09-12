using CMDevicesManager.Services;
using CMDevicesManager.Models;
using System;
using System.Threading.Tasks;
using WinFoundation = Windows.Foundation;
using WinUIColor = Windows.UI.Color;

namespace CMDevicesManager.Examples
{
    /// <summary>
    /// Example demonstrating how to use the enhanced InteractiveWin2DRenderingService
    /// with built-in render tick and HID device integration
    /// </summary>
    public class InteractiveHidRenderingExample : IDisposable
    {
        private InteractiveWin2DRenderingService? _renderingService;
        private bool _disposed = false;

        /// <summary>
        /// Initialize the interactive rendering service with HID integration
        /// </summary>
        public async Task InitializeAsync(int width = 480, int height = 480)
        {
            try
            {
                Console.WriteLine("Initializing Interactive Win2D Rendering Service with HID integration...");
                
                _renderingService = new InteractiveWin2DRenderingService();
                
                // Subscribe to events for monitoring
                _renderingService.HidStatusChanged += OnHidStatusChanged;
                _renderingService.JpegDataSentToHid += OnJpegDataSentToHid;
                _renderingService.RenderingError += OnRenderingError;
                _renderingService.ImageRendered += OnImageRendered;
                _renderingService.ElementSelected += OnElementSelected;
                _renderingService.ElementMoved += OnElementMoved;
                
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
        /// Start interactive rendering with built-in render tick and HID streaming
        /// </summary>
        public async Task StartInteractiveRenderingWithHidAsync(int fps = 30, int jpegQuality = 85)
        {
            if (_renderingService == null)
            {
                throw new InvalidOperationException("Service not initialized. Call InitializeAsync first.");
            }

            try
            {
                Console.WriteLine("Starting interactive rendering with HID streaming...");

                // Configure rendering and HID settings
                _renderingService.TargetFPS = fps;
                _renderingService.JpegQuality = jpegQuality;

                // Enable HID transfer in real-time mode
                _renderingService.EnableHidTransfer(true, useSuspendMedia: false);

                // Enable real-time display mode on HID devices
                var hidEnabled = await _renderingService.EnableHidRealTimeDisplayAsync(true);
                if (!hidEnabled)
                {
                    Console.WriteLine("Warning: Could not enable HID real-time mode");
                }

                // Start automatic rendering with built-in render tick
                _renderingService.StartAutoRendering(fps);

                Console.WriteLine($"Interactive rendering started at {fps} FPS with {jpegQuality}% JPEG quality");
                Console.WriteLine("Auto render tick is now active - frames will be sent to HID devices automatically");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start interactive rendering: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Add some sample interactive elements
        /// </summary>
        public void AddSampleElements()
        {
            if (_renderingService == null) return;

            Console.WriteLine("Adding sample interactive elements...");

            // Add a draggable text element
            var textElement = new TextElement("Sample Text")
            {
                Text = "Drag Me!",
                Position = new WinFoundation.Point(50, 50),
                Size = new WinFoundation.Size(150, 40),
                FontSize = 20,
                TextColor = WinUIColor.FromArgb(255, 255, 255, 0), // Yellow
                IsDraggable = true
            };
            _renderingService.AddElement(textElement);

            // Add a draggable circle shape
            var circleElement = new ShapeElement("Interactive Circle")
            {
                ShapeType = ShapeType.Circle,
                Position = new WinFoundation.Point(200, 150),
                Size = new WinFoundation.Size(80, 80),
                FillColor = WinUIColor.FromArgb(150, 0, 255, 100), // Semi-transparent green
                StrokeColor = WinUIColor.FromArgb(255, 255, 255, 255), // White border
                StrokeWidth = 3,
                IsDraggable = true
            };
            _renderingService.AddElement(circleElement);

            // Add a rectangle shape
            var rectangleElement = new ShapeElement("Interactive Rectangle")
            {
                ShapeType = ShapeType.Rectangle,
                Position = new WinFoundation.Point(300, 250),
                Size = new WinFoundation.Size(120, 80),
                FillColor = WinUIColor.FromArgb(180, 255, 50, 50), // Semi-transparent red
                StrokeColor = WinUIColor.FromArgb(255, 255, 255, 255), // White border
                StrokeWidth = 2,
                IsDraggable = true
            };
            _renderingService.AddElement(rectangleElement);

            Console.WriteLine("Sample elements added - they are now interactive and will be streamed to HID devices");
        }

        /// <summary>
        /// Demonstrate manual frame sending as suspend media
        /// </summary>
        public async Task SendFrameAsSuspendMediaAsync()
        {
            if (_renderingService == null) return;

            try
            {
                Console.WriteLine("Sending current frame as suspend media...");
                
                var success = await _renderingService.SendCurrentFrameAsSuspendMediaAsync();
                
                if (success)
                {
                    Console.WriteLine("Frame sent as suspend media successfully");
                }
                else
                {
                    Console.WriteLine("Failed to send frame as suspend media");
                }
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
            if (_renderingService == null) return;

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
        /// Stop interactive rendering
        /// </summary>
        public void StopInteractiveRendering()
        {
            if (_renderingService == null) return;

            try
            {
                Console.WriteLine("Stopping interactive rendering...");
                
                // Stop auto rendering
                _renderingService.StopAutoRendering();
                
                // Disable HID transfer
                _renderingService.EnableHidTransfer(false);
                
                Console.WriteLine("Interactive rendering stopped");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping rendering: {ex.Message}");
            }
        }

        /// <summary>
        /// Update rendering FPS dynamically
        /// </summary>
        public void UpdateRenderingFPS(int newFps)
        {
            if (_renderingService == null) return;

            Console.WriteLine($"Updating rendering FPS to {newFps}...");
            _renderingService.SetAutoRenderingFPS(newFps);
        }

        /// <summary>
        /// Update JPEG quality for HID transfer
        /// </summary>
        public void UpdateJpegQuality(int quality)
        {
            if (_renderingService == null) return;

            _renderingService.JpegQuality = quality;
            Console.WriteLine($"JPEG quality updated to {quality}%");
        }

        /// <summary>
        /// Get rendering and HID statistics
        /// </summary>
        public void PrintStatistics()
        {
            if (_renderingService == null)
            {
                Console.WriteLine("Service not initialized");
                return;
            }

            Console.WriteLine("\n=== Interactive Rendering Service Statistics ===");
            Console.WriteLine($"HID Service Connected: {_renderingService.IsHidServiceConnected}");
            Console.WriteLine($"Real-Time Mode Enabled: {_renderingService.IsHidRealTimeModeEnabled}");
            Console.WriteLine($"Auto Rendering Enabled: {_renderingService.IsAutoRenderingEnabled}");
            Console.WriteLine($"Target FPS: {_renderingService.TargetFPS}");
            Console.WriteLine($"Render Interval: {_renderingService.RenderInterval.TotalMilliseconds}ms");
            Console.WriteLine($"JPEG Quality: {_renderingService.JpegQuality}%");
            Console.WriteLine($"Frames Sent to HID: {_renderingService.HidFramesSent}");
            Console.WriteLine($"Send to HID Enabled: {_renderingService.SendToHidDevices}");
            Console.WriteLine($"Use Suspend Media: {_renderingService.UseSuspendMedia}");
            Console.WriteLine($"Canvas Size: {_renderingService.Width}x{_renderingService.Height}");
            Console.WriteLine($"Element Count: {_renderingService.GetElements().Count}");
            Console.WriteLine($"Selected Element: {(_renderingService.SelectedElement?.Name ?? "None")}");
        }

        /// <summary>
        /// Simulate mouse interactions for testing
        /// </summary>
        public void SimulateMouseInteraction()
        {
            if (_renderingService == null) return;

            Console.WriteLine("Simulating mouse interactions...");

            // Hit test at a known location
            var hitPoint = new WinFoundation.Point(60, 60); // Should hit the text element
            var hitElement = _renderingService.HitTest(hitPoint);
            
            if (hitElement != null)
            {
                Console.WriteLine($"Hit element: {hitElement.Name}");
                _renderingService.SelectElement(hitElement);
                
                // Move the element to a new position
                var newPosition = new WinFoundation.Point(150, 150);
                _renderingService.MoveElement(hitElement, newPosition);
                Console.WriteLine($"Moved element to ({newPosition.X}, {newPosition.Y})");
            }
            else
            {
                Console.WriteLine("No element found at hit test location");
            }
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

        private void OnImageRendered(System.Windows.Media.Imaging.WriteableBitmap bitmap)
        {
            // This event is fired every time a frame is rendered (useful for UI updates)
            // In a real application, you might update a WPF Image control here
        }

        private void OnElementSelected(RenderElement element)
        {
            Console.WriteLine($"[INTERACTION] Element selected: {element.Name}");
        }

        private void OnElementMoved(RenderElement element)
        {
            Console.WriteLine($"[INTERACTION] Element moved: {element.Name} to ({element.Position.X:F1}, {element.Position.Y:F1})");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                StopInteractiveRendering();
            }
            catch
            {
                // Ignore errors during shutdown
            }

            _renderingService?.Dispose();
            
            Console.WriteLine("InteractiveHidRenderingExample disposed");
        }
    }

    /// <summary>
    /// Usage example for interactive rendering with HID integration
    /// </summary>
    public static class InteractiveHidRenderingUsageExample
    {
        public static async Task RunExampleAsync()
        {
            using var example = new InteractiveHidRenderingExample();

            try
            {
                // Initialize with 480x480 resolution
                await example.InitializeAsync(480, 480);

                // Add some interactive elements
                example.AddSampleElements();

                // Print initial statistics
                example.PrintStatistics();

                // Start interactive rendering with HID streaming at 20 FPS
                await example.StartInteractiveRenderingWithHidAsync(fps: 20, jpegQuality: 80);

                // Simulate some mouse interactions
                await Task.Delay(2000); // Wait 2 seconds
                example.SimulateMouseInteraction();

                // Let it run for 10 seconds with auto rendering
                Console.WriteLine("Interactive rendering with auto-tick active for 10 seconds...");
                await Task.Delay(10000);

                // Update FPS dynamically
                example.UpdateRenderingFPS(15);
                await Task.Delay(3000);

                // Update JPEG quality
                example.UpdateJpegQuality(90);
                await Task.Delay(2000);

                // Print statistics after running
                example.PrintStatistics();

                // Send a frame as suspend media
                await example.SendFrameAsSuspendMediaAsync();

                // Set devices to suspend mode
                await example.SetDevicesToSuspendModeAsync();

                // Stop rendering
                example.StopInteractiveRendering();

                Console.WriteLine("Interactive rendering example completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Example failed: {ex.Message}");
            }
        }
    }
}