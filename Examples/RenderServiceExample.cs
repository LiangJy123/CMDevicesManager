using System;
using System.Windows;
using System.Windows.Threading;
using CMDevicesManager.Services;
using SkiaSharp;

namespace CMDevicesManager.Examples
{
    /// <summary>
    /// Example showing how to integrate the SkiaSharp render service with DeviceConfigPage
    /// </summary>
    public class RenderServiceExample
    {
        private readonly IRenderService _renderService;
        private readonly RenderIntegrationHelper _helper;
        private readonly ISystemMetricsService _metricsService;
        private readonly DispatcherTimer _updateTimer;

        public RenderServiceExample()
        {
            // Initialize services
            _renderService = new SkiaRenderService();
            _metricsService = new RealSystemMetricsService();
            _helper = new RenderIntegrationHelper(_renderService, _metricsService);

            // Initialize render service
            _helper.Initialize(512);

            // Setup live data update timer
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _updateTimer.Tick += UpdateTimer_Tick;
        }

        /// <summary>
        /// Setup basic elements similar to DeviceConfigPage
        /// </summary>
        public void SetupElements()
        {
            // Add live CPU usage
            _helper.AddLiveCpuUsage("live_cpu", new SKPoint(50, 50), 18);

            // Add live GPU usage
            _helper.AddLiveGpuUsage("live_gpu", new SKPoint(50, 80), 18);

            // Add live date/time
            _helper.AddLiveDateTime("live_datetime", new SKPoint(50, 110), 20);

            // Add static text
            _helper.AddStaticText("title", "Device Monitor", new SKPoint(200, 50), 24, 
                System.Windows.Media.Colors.White);

            // Example: Add background image if available
            try
            {
                var imagePath = "Resources/background.png";
                if (System.IO.File.Exists(imagePath))
                {
                    _helper.AddImageFromFile("background", imagePath, 
                        new SKPoint(0, 0), new SKSize(512, 512), 0.3f);
                }
            }
            catch
            {
                // Background image not available
            }
        }

        /// <summary>
        /// Start live updates and realtime rendering
        /// </summary>
        public void StartLiveUpdates()
        {
            _updateTimer.Start();
            
            // Start realtime rendering at 30 FPS
            _helper.StartRealtimeOutput(30, OnRenderOutputReady);
        }

        /// <summary>
        /// Stop live updates and realtime rendering
        /// </summary>
        public void StopLiveUpdates()
        {
            _updateTimer.Stop();
            _helper.StopRealtimeOutput(OnRenderOutputReady);
        }

        /// <summary>
        /// Handle render output (can be used to send to device or display)
        /// </summary>
        private void OnRenderOutputReady(object? sender, RenderOutputEventArgs e)
        {
            // This is where you would send the rendered frame to your device
            // or process it further
            
            // Example: Save frame to file (for debugging)
            if (DateTime.Now.Second % 10 == 0) // Save every 10 seconds
            {
                try
                {
                    var filename = $"frame_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    System.IO.File.WriteAllBytes(filename, e.FrameData);
                }
                catch { }
            }
        }

        /// <summary>
        /// Update live data
        /// </summary>
        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            _helper.UpdateLiveData();
        }

        /// <summary>
        /// Export current frame to PNG
        /// </summary>
        public void ExportFrame(string outputPath)
        {
            _helper.ExportToPng(outputPath);
        }

        /// <summary>
        /// Get current frame as WPF bitmap for display
        /// </summary>
        public System.Windows.Media.Imaging.BitmapSource? GetDisplayBitmap()
        {
            return _helper.GetWpfBitmap();
        }

        /// <summary>
        /// Add custom text element
        /// </summary>
        public void AddCustomText(string id, string text, SKPoint position, float fontSize, 
            System.Windows.Media.Color color)
        {
            _helper.AddStaticText(id, text, position, fontSize, color);
        }

        /// <summary>
        /// Update element position (useful for animations)
        /// </summary>
        public void UpdateElementPosition(string id, SKPoint newPosition)
        {
            _renderService.UpdateElementPosition(id, newPosition);
        }

        /// <summary>
        /// Set background color
        /// </summary>
        public void SetBackground(System.Windows.Media.Color color)
        {
            _helper.SetBackgroundColor(color);
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            StopLiveUpdates();
            _updateTimer?.Stop();
            _helper?.Dispose();
            _metricsService?.Dispose();
        }
    }

    /// <summary>
    /// Extension methods to integrate with existing DeviceConfigPage
    /// </summary>
    public static class DeviceConfigPageExtensions
    {
        /// <summary>
        /// Create a render service that mirrors the current DeviceConfigPage content
        /// </summary>
        public static RenderServiceExample CreateRenderService(this Pages.DeviceConfigPage page)
        {
            var renderExample = new RenderServiceExample();
            
            // Setup basic elements
            renderExample.SetupElements();
            
            // Mirror current page background color
            renderExample.SetBackground(page.BackgroundColor);
            
            return renderExample;
        }
    }
}