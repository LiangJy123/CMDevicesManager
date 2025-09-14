using CMDevicesManager.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace CMDevicesManager.Examples
{
    /// <summary>
    /// Example demonstrating how to use the new HidSwapChainService and HidRealTimeRenderer
    /// for D3D-style frame presentation to HID devices.
    /// </summary>
    public class HidSwapChainExample : IDisposable
    {
        private HidRealTimeRenderer? _renderer;
        private bool _disposed = false;
        
        /// <summary>
        /// Initialize and demonstrate SwapChain-based HID rendering
        /// </summary>
        public async Task RunSwapChainDemoAsync()
        {
            Console.WriteLine("=== HID SwapChain Service Demo ===\n");
            
            try
            {
                // Get HID service (assumes ServiceLocator is initialized)
                var hidService = ServiceLocator.HidDeviceService;
                if (hidService == null)
                {
                    Console.WriteLine("? HID Service not available");
                    return;
                }
                
                // Create real-time renderer with SwapChain
                await InitializeRendererAsync(hidService);
                
                // Run various demonstration scenarios
                await DemoBasicFrameRenderingAsync();
                await DemoHighFrameRateRenderingAsync();
                await DemoImmediatePresentationAsync();
                await DemoPerformanceMonitoringAsync();
                
                // Show final statistics
                ShowFinalStatistics();
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Demo failed: {ex.Message}");
            }
            finally
            {
                Cleanup();
            }
        }
        
        /// <summary>
        /// Initialize the HID Real-Time Renderer with SwapChain
        /// </summary>
        private async Task InitializeRendererAsync(HidDeviceService hidService)
        {
            Console.WriteLine("?? Initializing HID Real-Time Renderer with SwapChain...");
            
            // Create renderer with triple buffering and 30 FPS
            _renderer = new HidRealTimeRenderer(
                hidService,
                targetFps: 30,
                bufferCount: 3,  // Triple buffering for smooth performance
                enableVsync: false // Use immediate mode for lower latency
            );
            
            // Subscribe to events
            _renderer.FrameRendered += OnFrameRendered;
            _renderer.RenderError += OnRenderError;
            _renderer.StatisticsUpdated += OnStatisticsUpdated;
            
            // Initialize the renderer
            var success = await _renderer.InitializeAsync();
            if (success)
            {
                Console.WriteLine("? Renderer initialized successfully");
                Console.WriteLine($"   - Triple buffering enabled");
                Console.WriteLine($"   - Target: 30 FPS");
                Console.WriteLine($"   - Present mode: Immediate");
            }
            else
            {
                throw new InvalidOperationException("Failed to initialize HID renderer");
            }
        }
        
        /// <summary>
        /// Demonstrate basic frame rendering with SwapChain
        /// </summary>
        private async Task DemoBasicFrameRenderingAsync()
        {
            Console.WriteLine("\n???  Demo 1: Basic Frame Rendering");
            Console.WriteLine("   Rendering 10 frames with SwapChain buffering...");
            
            // Start rendering
            _renderer!.StartRendering();
            
            for (int i = 1; i <= 10; i++)
            {
                // Create a simple test frame (colored rectangle as JPEG)
                var frameData = CreateTestFrame(i, $"Frame {i}");
                
                var success = await _renderer.RenderFrameAsync(frameData, $"BasicDemo-Frame{i}");
                if (success)
                {
                    Console.WriteLine($"   ? Frame {i} rendered and queued for presentation");
                }
                else
                {
                    Console.WriteLine($"   ? Frame {i} failed to render");
                }
                
                // Wait between frames to see buffering in action
                await Task.Delay(100);
            }
            
            // Let the presentation queue process
            await Task.Delay(1000);
            _renderer.StopRendering();
            
            Console.WriteLine("   Basic rendering demo completed\n");
        }
        
        /// <summary>
        /// Demonstrate high frame rate rendering with buffer management
        /// </summary>
        private async Task DemoHighFrameRateRenderingAsync()
        {
            Console.WriteLine("?? Demo 2: High Frame Rate Rendering (60 FPS burst)");
            Console.WriteLine("   Testing SwapChain buffer management under load...");
            
            _renderer!.StartRendering();
            
            var startTime = DateTime.Now;
            var frameCount = 0;
            var successCount = 0;
            
            // Render at 60 FPS for 3 seconds
            while ((DateTime.Now - startTime).TotalSeconds < 3.0)
            {
                frameCount++;
                var frameData = CreateTestFrame(frameCount % 10, $"HighFPS-{frameCount}");
                
                var success = await _renderer.RenderFrameAsync(frameData, $"HighFPS-Frame{frameCount}");
                if (success)
                {
                    successCount++;
                }
                
                // 60 FPS timing
                await Task.Delay(16); // ~60 FPS
            }
            
            _renderer.StopRendering();
            
            var actualDuration = (DateTime.Now - startTime).TotalSeconds;
            var actualFps = frameCount / actualDuration;
            
            Console.WriteLine($"   ?? Results:");
            Console.WriteLine($"   - Frames attempted: {frameCount}");
            Console.WriteLine($"   - Frames successful: {successCount}");
            Console.WriteLine($"   - Success rate: {(double)successCount / frameCount * 100:F1}%");
            Console.WriteLine($"   - Actual FPS: {actualFps:F1}");
            Console.WriteLine($"   - Duration: {actualDuration:F1}s");
            Console.WriteLine("   High frame rate demo completed\n");
        }
        
        /// <summary>
        /// Demonstrate immediate presentation (bypass queue)
        /// </summary>
        private async Task DemoImmediatePresentationAsync()
        {
            Console.WriteLine("? Demo 3: Immediate Presentation");
            Console.WriteLine("   Bypassing SwapChain queue for critical frames...");
            
            for (int i = 1; i <= 5; i++)
            {
                var frameData = CreateTestFrame(i, $"Immediate {i}");
                
                var startTime = DateTime.Now;
                var success = await _renderer!.RenderFrameImmediateAsync(frameData, $"Immediate-Frame{i}");
                var latency = (DateTime.Now - startTime).TotalMilliseconds;
                
                if (success)
                {
                    Console.WriteLine($"   ? Immediate frame {i} presented in {latency:F1}ms");
                }
                else
                {
                    Console.WriteLine($"   ? Immediate frame {i} failed (latency: {latency:F1}ms)");
                }
                
                await Task.Delay(200);
            }
            
            Console.WriteLine("   Immediate presentation demo completed\n");
        }
        
        /// <summary>
        /// Demonstrate performance monitoring and statistics
        /// </summary>
        private async Task DemoPerformanceMonitoringAsync()
        {
            Console.WriteLine("?? Demo 4: Performance Monitoring");
            Console.WriteLine("   Monitoring SwapChain performance in real-time...");
            
            _renderer!.StartRendering();
            
            // Render frames while monitoring performance
            for (int i = 1; i <= 30; i++)
            {
                var frameData = CreateTestFrame(i % 5, $"Perf-{i}");
                await _renderer.RenderFrameAsync(frameData, $"PerfDemo-Frame{i}");
                
                // Show statistics every 10 frames
                if (i % 10 == 0)
                {
                    var status = _renderer.GetStatusReport();
                    Console.WriteLine($"   ?? {status}");
                }
                
                await Task.Delay(33); // ~30 FPS
            }
            
            _renderer.StopRendering();
            
            // Show detailed performance report
            var stats = _renderer.GetRenderStatistics();
            Console.WriteLine("\n   ?? Detailed Performance Report:");
            Console.WriteLine(stats.GetDetailedReport());
            Console.WriteLine();
        }
        
        /// <summary>
        /// Create a test frame (simple colored image as JPEG)
        /// </summary>
        private byte[] CreateTestFrame(int colorIndex, string text)
        {
            try
            {
                // Create a simple 480x480 test image
                var width = 480;
                var height = 480;
                
                var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                
                // Fill with a color based on index
                var colors = new uint[]
                {
                    0xFF0000FF, // Red
                    0xFF00FF00, // Green  
                    0xFFFF0000, // Blue
                    0xFF00FFFF, // Yellow
                    0xFFFF00FF, // Magenta
                    0xFFFFFF00, // Cyan
                    0xFFFFFFFF, // White
                    0xFF808080, // Gray
                    0xFF000080, // Dark Red
                    0xFF008000  // Dark Green
                };
                
                var color = colors[colorIndex % colors.Length];
                
                bitmap.Lock();
                try
                {
                    // Use Marshal.Copy for safe memory access
                    var pixelData = new uint[width * height];
                    for (int i = 0; i < pixelData.Length; i++)
                    {
                        pixelData[i] = color;
                    }
                    
                    var stride = width * 4; // 4 bytes per pixel
                    var buffer = new byte[stride * height];
                    Buffer.BlockCopy(pixelData, 0, buffer, 0, buffer.Length);
                    
                    System.Runtime.InteropServices.Marshal.Copy(buffer, 0, bitmap.BackBuffer, buffer.Length);
                    bitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
                }
                finally
                {
                    bitmap.Unlock();
                }
                
                // Convert to JPEG bytes
                using (var stream = new MemoryStream())
                {
                    var encoder = new JpegBitmapEncoder();
                    encoder.QualityLevel = 85;
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(stream);
                    return stream.ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ??  Error creating test frame: {ex.Message}");
                // Return a minimal JPEG as fallback
                return new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }; // Minimal JPEG header/footer
            }
        }
        
        /// <summary>
        /// Show final statistics and summary
        /// </summary>
        private void ShowFinalStatistics()
        {
            if (_renderer == null) return;
            
            Console.WriteLine("?? Final Statistics Summary");
            Console.WriteLine("=" + new string('=', 50));
            
            var stats = _renderer.GetRenderStatistics();
            Console.WriteLine(stats.GetDetailedReport());
            
            Console.WriteLine("\n?? Key Takeaways:");
            Console.WriteLine($"   - SwapChain buffering enables smooth frame presentation");
            Console.WriteLine($"   - Triple buffering provides {stats.BufferUtilization:F1}% buffer utilization");
            Console.WriteLine($"   - Achieved {stats.EffectiveFrameRate:F1} effective FPS");
            Console.WriteLine($"   - {stats.OverallSuccessRate:F1}% overall success rate");
            Console.WriteLine($"   - Total frames rendered: {stats.TotalFramesRendered:N0}");
        }
        
        /// <summary>
        /// Cleanup resources
        /// </summary>
        private void Cleanup()
        {
            Console.WriteLine("\n?? Cleaning up resources...");
            _renderer?.Dispose();
            Console.WriteLine("? Cleanup completed");
        }
        
        #region Event Handlers
        
        private void OnFrameRendered(object? sender, RenderFrameEventArgs e)
        {
            // Optionally log frame renders (disabled to reduce noise)
            // Console.WriteLine($"   Frame rendered: {e.FrameSize:N0} bytes");
        }
        
        private void OnRenderError(object? sender, RenderErrorEventArgs e)
        {
            Console.WriteLine($"   ? Render error: {e.Context} - {e.Exception.Message}");
        }
        
        private void OnStatisticsUpdated(object? sender, RenderStatisticsEventArgs e)
        {
            // Statistics are updated when rendering stops
            Console.WriteLine($"   ?? Statistics updated: {e.Statistics.GetSummary()}");
        }
        
        #endregion
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _renderer?.Dispose();
                _disposed = true;
            }
        }
    }
}

// Example usage in a console application or WPF app:
/*
public partial class MainWindow : Window
{
    public async void TestSwapChain()
    {
        using (var example = new HidSwapChainExample())
        {
            await example.RunSwapChainDemoAsync();
        }
    }
}
*/