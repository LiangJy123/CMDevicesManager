using DevWinUIGallery.Services;
using Microsoft.Graphics.Canvas;
using System;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Examples
{
    /// <summary>
    /// Example demonstrating the enhanced image capabilities of BackgroundRenderingService
    /// </summary>
    public class BackgroundRenderingServiceImageExample
    {
        private BackgroundRenderingService _renderService;
        private bool _isInitialized = false;

        public event EventHandler<string> StatusChanged;

        /// <summary>
        /// Initialize the rendering service with image support
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            try
            {
                _renderService = new BackgroundRenderingService();
                
                // Subscribe to events
                _renderService.StatusChanged += (sender, status) => StatusChanged?.Invoke(this, status);
                _renderService.PixelFrameReady += OnPixelFrameReady;
                _renderService.HidFrameSent += OnHidFrameSent;

                // Initialize the service
                await _renderService.InitializeAsync();
                
                _isInitialized = true;
                StatusChanged?.Invoke(this, "BackgroundRenderingService initialized with image support");
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Failed to initialize: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Start rendering with optional HID real-time mode
        /// </summary>
        public async Task StartRenderingAsync(bool enableHidRealTime = false)
        {
            if (!_isInitialized) return;

            await _renderService.StartRenderingAsync(enableHidRealTime);
            StatusChanged?.Invoke(this, $"Rendering started{(enableHidRealTime ? " with HID real-time mode" : "")}");
        }

        /// <summary>
        /// Stop rendering
        /// </summary>
        public void StopRendering()
        {
            if (!_isInitialized) return;

            _renderService.StopRendering();
            StatusChanged?.Invoke(this, "Rendering stopped");
        }

        /// <summary>
        /// Add an image from file path
        /// </summary>
        /// <param name="filePath">Path to the image file</param>
        /// <param name="x">X position</param>
        /// <param name="y">Y position</param>
        /// <param name="width">Width (0 = original)</param>
        /// <param name="height">Height (0 = original)</param>
        /// <param name="opacity">Opacity (0.0 to 1.0)</param>
        /// <returns>Element ID or -1 if failed</returns>
        public async Task<int> AddImageFromFileAsync(string filePath, float x, float y, 
            float width = 0, float height = 0, float opacity = 1.0f)
        {
            if (!_isInitialized) return -1;

            var position = new Vector2(x, y);
            var elementId = await _renderService.AddImageFromFileAsync(filePath, position, width, height, opacity);
            
            if (elementId >= 0)
            {
                StatusChanged?.Invoke(this, $"Image added from file: {filePath} (ID: {elementId})");
            }
            
            return elementId;
        }

        /// <summary>
        /// Add an image from byte array
        /// </summary>
        /// <param name="imageData">Image data as byte array</param>
        /// <param name="x">X position</param>
        /// <param name="y">Y position</param>
        /// <param name="width">Width (0 = original)</param>
        /// <param name="height">Height (0 = original)</param>
        /// <param name="opacity">Opacity (0.0 to 1.0)</param>
        /// <returns>Element ID or -1 if failed</returns>
        public async Task<int> AddImageFromBytesAsync(byte[] imageData, float x, float y, 
            float width = 0, float height = 0, float opacity = 1.0f)
        {
            if (!_isInitialized) return -1;

            var position = new Vector2(x, y);
            var elementId = await _renderService.AddImageFromBytesAsync(imageData, position, width, height, opacity);
            
            if (elementId >= 0)
            {
                StatusChanged?.Invoke(this, $"Image added from byte array ({imageData.Length} bytes, ID: {elementId})");
            }
            
            return elementId;
        }

        /// <summary>
        /// Add text element
        /// </summary>
        /// <param name="text">Text content</param>
        /// <param name="x">X position</param>
        /// <param name="y">Y position</param>
        /// <param name="fontSize">Font size</param>
        /// <param name="color">Text color</param>
        /// <param name="opacity">Opacity</param>
        /// <returns>Element ID or -1 if failed</returns>
        public int AddText(string text, float x, float y, float fontSize = 24, 
            Windows.UI.Color? color = null, float opacity = 1.0f)
        {
            if (!_isInitialized) return -1;

            var position = new Vector2(x, y);
            var elementId = _renderService.AddLiveTextElement(text, position, fontSize, color, "Segoe UI", opacity);
            
            if (elementId >= 0)
            {
                StatusChanged?.Invoke(this, $"Text added: '{text}' (ID: {elementId})");
            }
            
            return elementId;
        }

        /// <summary>
        /// Create a sample layout with text and images
        /// </summary>
        public async Task CreateSampleLayoutAsync()
        {
            if (!_isInitialized) return;

            // Clear existing elements
            _renderService.ClearElements();

            // Add title text
            AddText("?? BackgroundRenderingService Image Demo", 50, 50, 32, Windows.UI.Colors.Cyan);

            // Add description
            AddText("Enhanced with direct image rendering support!", 50, 90, 18, Windows.UI.Colors.LightBlue);

            // Add sample images if available (these would need to exist in your project)
            var sampleImages = new[]
            {
                "Assets/sample1.png",
                "Assets/sample2.jpg",
                "Assets/icon.png"
            };

            float imageY = 150;
            for (int i = 0; i < sampleImages.Length; i++)
            {
                try
                {
                    var imageId = await AddImageFromFileAsync(sampleImages[i], 100 + i * 120, imageY, 100, 100, 0.8f);
                    if (imageId >= 0)
                    {
                        // Add label for each image
                        AddText($"Image {i + 1}", 110 + i * 120, imageY + 110, 14, Windows.UI.Colors.Yellow);
                    }
                }
                catch
                {
                    // Image not found, add placeholder text
                    AddText($"[IMG {i + 1}]", 120 + i * 120, imageY + 50, 16, Windows.UI.Colors.Gray);
                }
            }

            // Add info text
            AddText($"Canvas: {_renderService.TargetWidth}x{_renderService.TargetHeight}", 50, 300, 16, Windows.UI.Colors.LightGray);
            AddText($"Elements: {_renderService.ElementCount}", 50, 320, 16, Windows.UI.Colors.LightGray);

            StatusChanged?.Invoke(this, "Sample layout created with text and image elements");
        }

        /// <summary>
        /// Update element position (for animations)
        /// </summary>
        /// <param name="elementId">Element ID</param>
        /// <param name="x">New X position</param>
        /// <param name="y">New Y position</param>
        /// <returns>True if successful</returns>
        public bool UpdateElementPosition(int elementId, float x, float y)
        {
            if (!_isInitialized) return false;

            var newPosition = new Vector2(x, y);
            return _renderService.UpdateElementPosition(elementId, newPosition);
        }

        /// <summary>
        /// Update element opacity
        /// </summary>
        /// <param name="elementId">Element ID</param>
        /// <param name="opacity">New opacity (0.0 to 1.0)</param>
        /// <returns>True if successful</returns>
        public bool UpdateElementOpacity(int elementId, float opacity)
        {
            if (!_isInitialized) return false;

            return _renderService.UpdateElementOpacity(elementId, opacity);
        }

        /// <summary>
        /// Remove an element
        /// </summary>
        /// <param name="elementId">Element ID to remove</param>
        /// <returns>True if successful</returns>
        public bool RemoveElement(int elementId)
        {
            if (!_isInitialized) return false;

            var success = _renderService.RemoveElement(elementId);
            if (success)
            {
                StatusChanged?.Invoke(this, $"Element {elementId} removed");
            }
            
            return success;
        }

        /// <summary>
        /// Clear all elements
        /// </summary>
        public void ClearAllElements()
        {
            if (!_isInitialized) return;

            _renderService.ClearElements();
            StatusChanged?.Invoke(this, "All elements cleared");
        }

        /// <summary>
        /// Set render canvas size
        /// </summary>
        /// <param name="width">Canvas width</param>
        /// <param name="height">Canvas height</param>
        public void SetCanvasSize(int width, int height)
        {
            if (!_isInitialized) return;

            _renderService.SetRenderSize(width, height);
            StatusChanged?.Invoke(this, $"Canvas size set to {width}x{height}");
        }

        /// <summary>
        /// Set target FPS
        /// </summary>
        /// <param name="fps">Target frames per second</param>
        public void SetTargetFps(int fps)
        {
            if (!_isInitialized) return;

            _renderService.SetTargetFps(fps);
            StatusChanged?.Invoke(this, $"Target FPS set to {fps}");
        }

        /// <summary>
        /// Enable/disable HID real-time mode
        /// </summary>
        /// <param name="enable">True to enable</param>
        /// <returns>True if successful</returns>
        public async Task<bool> SetHidRealTimeModeAsync(bool enable)
        {
            if (!_isInitialized) return false;

            return await _renderService.SetHidRealTimeModeAsync(enable);
        }

        /// <summary>
        /// Set JPEG quality for HID streaming
        /// </summary>
        /// <param name="quality">Quality (1-100)</param>
        public void SetJpegQuality(int quality)
        {
            if (!_isInitialized) return;

            _renderService.JpegQuality = quality;
            StatusChanged?.Invoke(this, $"JPEG quality set to {quality}%");
        }

        /// <summary>
        /// Get current statistics
        /// </summary>
        /// <returns>Statistics string</returns>
        public string GetStatistics()
        {
            if (!_isInitialized) return "Service not initialized";

            return $"Elements: {_renderService.ElementCount}, " +
                   $"Size: {_renderService.TargetWidth}x{_renderService.TargetHeight}, " +
                   $"FPS: {_renderService.TargetFps}, " +
                   $"HID: {(_renderService.IsHidRealTimeModeEnabled ? "ON" : "OFF")}, " +
                   $"JPEG: {_renderService.JpegQuality}%";
        }

        /// <summary>
        /// Handle pixel frame ready event
        /// </summary>
        private void OnPixelFrameReady(object sender, PixelFrameEventArgs e)
        {
            // Frame ready - could be used for custom processing
            // StatusChanged?.Invoke(this, $"Frame ready: {e.Width}x{e.Height}, {e.PixelData.Length} bytes");
        }

        /// <summary>
        /// Handle HID frame sent event
        /// </summary>
        private void OnHidFrameSent(object sender, HidFrameSentEventArgs e)
        {
            StatusChanged?.Invoke(this, 
                $"HID Frame sent: {e.FrameSize:N0} bytes to {e.DevicesSucceeded}/{e.DevicesTotal} devices");
        }

        /// <summary>
        /// Dispose and cleanup
        /// </summary>
        public void Dispose()
        {
            _renderService?.Dispose();
            _isInitialized = false;
            StatusChanged?.Invoke(this, "BackgroundRenderingService disposed");
        }
    }
}

/* 
Usage Example:

var example = new BackgroundRenderingServiceImageExample();

// Initialize
await example.InitializeAsync();

// Subscribe to status updates
example.StatusChanged += (sender, status) => Console.WriteLine($"Status: {status}");

// Set canvas size and FPS
example.SetCanvasSize(800, 600);
example.SetTargetFps(30);

// Create sample layout
await example.CreateSampleLayoutAsync();

// Add custom images and text
var imageId = await example.AddImageFromFileAsync("path/to/image.jpg", 300, 200, 150, 150, 0.9f);
var textId = example.AddText("Custom Text", 300, 100, 24, Windows.UI.Colors.Green);

// Start rendering
await example.StartRenderingAsync(enableHidRealTime: true);

// Enable HID streaming with high quality
example.SetJpegQuality(95);

// Animate elements (call in a timer)
for (int i = 0; i < 100; i++)
{
    example.UpdateElementPosition(imageId, 300 + i * 2, 200);
    await Task.Delay(50);
}

// Clean up
example.Dispose();

*/