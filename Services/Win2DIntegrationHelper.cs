using System;
using System.IO;
using System.Windows.Media;
using CMDevicesManager.Services;
using SkiaSharp;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// Helper class to integrate SkiaSharp rendering with WPF DeviceConfigPage
    /// </summary>
    public class RenderIntegrationHelper : IDisposable
    {
        private readonly IRenderService _renderService;
        private readonly ISystemMetricsService _metricsService;
        private bool _isDisposed;

        public RenderIntegrationHelper(IRenderService renderService, ISystemMetricsService metricsService)
        {
            _renderService = renderService ?? throw new ArgumentNullException(nameof(renderService));
            _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
        }

        /// <summary>
        /// Initialize the render service with canvas settings
        /// </summary>
        public void Initialize(int canvasSize = 512)
        {
            _renderService.CanvasSize = new SKSizeI(canvasSize, canvasSize);
            _renderService.BackgroundColor = SKColors.Black;
            _renderService.Initialize();
        }

        /// <summary>
        /// Convert WPF color to SKColor
        /// </summary>
        public static SKColor ConvertColor(System.Windows.Media.Color wpfColor)
        {
            return new SKColor(wpfColor.R, wpfColor.G, wpfColor.B, wpfColor.A);
        }

        /// <summary>
        /// Add live CPU usage text that updates automatically
        /// </summary>
        public void AddLiveCpuUsage(string id, SKPoint position, float fontSize = 18)
        {
            var cpuUsage = Math.Round(_metricsService.GetCpuUsagePercent());
            _renderService.AddText(id, $"CPU {cpuUsage}%", position, fontSize, SKColors.White, "Segoe UI");
        }

        /// <summary>
        /// Add live GPU usage text that updates automatically
        /// </summary>
        public void AddLiveGpuUsage(string id, SKPoint position, float fontSize = 18)
        {
            var gpuUsage = Math.Round(_metricsService.GetGpuUsagePercent());
            _renderService.AddText(id, $"GPU {gpuUsage}%", position, fontSize, SKColors.White, "Segoe UI");
        }

        /// <summary>
        /// Add live date/time text that updates automatically
        /// </summary>
        public void AddLiveDateTime(string id, SKPoint position, float fontSize = 20)
        {
            var dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _renderService.AddText(id, dateTime, position, fontSize, SKColors.White, "Segoe UI");
        }

        /// <summary>
        /// Add image from file path
        /// </summary>
        public void AddImageFromFile(string id, string filePath, SKPoint position, SKSize size, float opacity = 1.0f)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Image file not found: {filePath}");

            var imageData = File.ReadAllBytes(filePath);
            _renderService.AddImage(id, imageData, position, size, opacity);
        }

        /// <summary>
        /// Add static text
        /// </summary>
        public void AddStaticText(string id, string text, SKPoint position, float fontSize, System.Windows.Media.Color color, float opacity = 1.0f)
        {
            var skColor = ConvertColor(color);
            _renderService.AddText(id, text, position, fontSize, skColor, "Segoe UI", opacity);
        }

        /// <summary>
        /// Update live data for all live elements
        /// </summary>
        public void UpdateLiveData()
        {
            // This would typically be called by a timer
            // Update CPU usage for any elements with "cpu" in their ID
            var cpuUsage = Math.Round(_metricsService.GetCpuUsagePercent());
            _renderService.UpdateTextContent("live_cpu", $"CPU {cpuUsage}%");

            // Update GPU usage for any elements with "gpu" in their ID
            var gpuUsage = Math.Round(_metricsService.GetGpuUsagePercent());
            _renderService.UpdateTextContent("live_gpu", $"GPU {gpuUsage}%");

            // Update date/time for any elements with "datetime" in their ID
            var dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _renderService.UpdateTextContent("live_datetime", dateTime);
        }

        /// <summary>
        /// Set background color
        /// </summary>
        public void SetBackgroundColor(System.Windows.Media.Color color)
        {
            _renderService.BackgroundColor = ConvertColor(color);
        }

        /// <summary>
        /// Export current frame to PNG file
        /// </summary>
        public void ExportToPng(string outputPath)
        {
            _renderService.RenderFrame();
            var frameData = _renderService.GetRenderedFrameData();
            
            // SkiaSharp already provides PNG encoded data
            File.WriteAllBytes(outputPath, frameData);
        }

        /// <summary>
        /// Get current frame as WPF BitmapSource for display
        /// </summary>
        public System.Windows.Media.Imaging.BitmapSource? GetWpfBitmap()
        {
            try
            {
                _renderService.RenderFrame();
                using var image = _renderService.GetRenderedImage();
                if (image == null) return null;

                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                var bytes = data.ToArray();
                
                using var stream = new MemoryStream(bytes);
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Start realtime rendering output
        /// </summary>
        public void StartRealtimeOutput(int fps = 30, EventHandler<RenderOutputEventArgs>? outputHandler = null)
        {
            if (outputHandler != null)
            {
                _renderService.RenderOutputReady += outputHandler;
            }
            
            _renderService.StartRealtimeRendering(fps);
        }

        /// <summary>
        /// Stop realtime rendering output
        /// </summary>
        public void StopRealtimeOutput(EventHandler<RenderOutputEventArgs>? outputHandler = null)
        {
            _renderService.StopRealtimeRendering();
            
            if (outputHandler != null)
            {
                _renderService.RenderOutputReady -= outputHandler;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _renderService?.Dispose();
        }
    }
}