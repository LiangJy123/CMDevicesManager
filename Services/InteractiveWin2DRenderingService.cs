using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using CMDevicesManager.Models;
using System.Numerics;
using WinUIColor = Windows.UI.Color;
using Point = Windows.Foundation.Point;
using Size = Windows.Foundation.Size;
using System.Windows.Threading;

namespace CMDevicesManager.Services
{
    public class InteractiveWin2DRenderingService : IDisposable
    {
        private CanvasDevice? _canvasDevice;
        private CanvasRenderTarget? _renderTarget;
        private CanvasBitmap? _backgroundImage;
        private readonly object _lockObject = new object();
        private readonly List<RenderElement> _elements = new();
        private readonly Dictionary<string, CanvasBitmap> _loadedImages = new();

        // Built-in render tick support
        private DispatcherTimer? _renderTimer;
        private bool _isAutoRenderingEnabled = false;
        private DateTime _lastRenderTime = DateTime.MinValue;

        // HID device integration
        private HidDeviceService? _hidDeviceService;
        private byte _transferId = 1;
        private bool _hidRealTimeModeEnabled = false;
        private int _hidFramesSent = 0;

        // Events
        public event Action<WriteableBitmap>? ImageRendered;
        public event Action<RenderElement>? ElementSelected;
        public event Action<RenderElement>? ElementMoved;
        public event Action<byte[]>? RawImageDataReady;
        public event Action<byte[]>? JpegDataSentToHid;
        public event Action<string>? HidStatusChanged;
        public event Action<Exception>? RenderingError;

        // Properties
        public int Width { get; private set; } = 800;
        public int Height { get; private set; } = 600;
        public RenderElement? SelectedElement { get; private set; }

        // Display options
        public bool ShowTime { get; set; } = true;
        public bool ShowDate { get; set; } = true;
        public bool ShowSystemInfo { get; set; } = true;
        public bool ShowAnimation { get; set; } = true;

        // Render tick properties
        public int TargetFPS { get; set; } = 30;
        public bool IsAutoRenderingEnabled => _isAutoRenderingEnabled;
        public TimeSpan RenderInterval => _renderTimer?.Interval ?? TimeSpan.FromMilliseconds(1000.0 / TargetFPS);

        // HID properties
        public bool SendToHidDevices { get; set; } = false;
        public bool UseSuspendMedia { get; set; } = false;
        public int JpegQuality { get; set; } = 85;
        public bool IsHidServiceConnected => _hidDeviceService?.IsInitialized == true;
        public bool IsHidRealTimeModeEnabled => _hidRealTimeModeEnabled;
        public int HidFramesSent => _hidFramesSent;

        public async Task InitializeAsync(int width = 800, int height = 600)
        {
            Width = width;
            Height = height;

            try
            {
                _canvasDevice = CanvasDevice.GetSharedDevice();
                if (_canvasDevice == null)
                {
                    throw new InvalidOperationException("Failed to get Canvas device. Win2D may not be properly installed.");
                }

                _renderTarget = new CanvasRenderTarget(_canvasDevice, width, height, 96);
                if (_renderTarget == null)
                {
                    throw new InvalidOperationException("Failed to create Canvas render target.");
                }

                // Load background image if it exists
                await LoadBackgroundImageAsync();

                // Initialize default elements
                InitializeDefaultElements();

                // Initialize render timer
                InitializeRenderTimer();

                // Initialize HID service connection
                await InitializeHidServiceAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize InteractiveWin2DRenderingService: {ex.Message}", ex);
            }
        }

        private void InitializeRenderTimer()
        {
            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / TargetFPS)
            };
            _renderTimer.Tick += OnRenderTick;
        }

        private async Task InitializeHidServiceAsync()
        {
            try
            {
                _hidDeviceService = ServiceLocator.HidDeviceService;

                if (_hidDeviceService.IsInitialized)
                {
                    HidStatusChanged?.Invoke($"Connected to HID service - {_hidDeviceService.ConnectedDeviceCount} devices available");

                    // Subscribe to HID service events
                    _hidDeviceService.DeviceConnected += OnHidDeviceConnected;
                    _hidDeviceService.DeviceDisconnected += OnHidDeviceDisconnected;
                    _hidDeviceService.DeviceError += OnHidDeviceError;
                    _hidDeviceService.RealTimeDisplayModeChanged += OnHidRealTimeDisplayModeChanged;
                }
                else
                {
                    HidStatusChanged?.Invoke("HID service not initialized");
                }
            }
            catch (Exception ex)
            {
                HidStatusChanged?.Invoke($"Failed to connect to HID service: {ex.Message}");
            }
        }

        private async void OnRenderTick(object? sender, EventArgs e)
        {
            try
            {
                _lastRenderTime = DateTime.Now;

                // Update live elements
                UpdateLiveElements();

                // Render frame
                var bitmap = await RenderFrameAsync();
                
                // Send to HID devices if enabled
                if (SendToHidDevices && _hidDeviceService?.IsInitialized == true && _hidRealTimeModeEnabled)
                {
                    var rawData = GetRenderedImageBytes();
                    if (rawData != null)
                    {
                        RawImageDataReady?.Invoke(rawData);
                        await SendFrameToHidDevicesAsync(rawData);
                    }
                }
            }
            catch (Exception ex)
            {
                RenderingError?.Invoke(ex);
            }
        }

        private void UpdateLiveElements()
        {
            lock (_lockObject)
            {
                foreach (var element in _elements.OfType<LiveElement>())
                {
                    // Live elements will automatically update their content when rendered
                    // This is handled in the RenderLiveElement method
                }
            }
        }

        #region Auto Rendering Control

        /// <summary>
        /// Start automatic rendering with built-in render tick
        /// </summary>
        /// <param name="fps">Target frames per second (1-120)</param>
        public void StartAutoRendering(int fps = 30)
        {
            if (_renderTimer == null)
            {
                InitializeRenderTimer();
            }

            TargetFPS = Math.Clamp(fps, 1, 120);
            _renderTimer!.Interval = TimeSpan.FromMilliseconds(1000.0 / TargetFPS);

            if (!_isAutoRenderingEnabled)
            {
                _renderTimer.Start();
                _isAutoRenderingEnabled = true;
                HidStatusChanged?.Invoke($"Auto rendering started at {TargetFPS} FPS");
            }
        }

        /// <summary>
        /// Stop automatic rendering
        /// </summary>
        public void StopAutoRendering()
        {
            if (_isAutoRenderingEnabled && _renderTimer != null)
            {
                _renderTimer.Stop();
                _isAutoRenderingEnabled = false;
                HidStatusChanged?.Invoke("Auto rendering stopped");
            }
        }

        /// <summary>
        /// Update the FPS for auto rendering
        /// </summary>
        /// <param name="fps">New target FPS</param>
        public void SetAutoRenderingFPS(int fps)
        {
            TargetFPS = Math.Clamp(fps, 1, 120);

            if (_renderTimer != null)
            {
                _renderTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / TargetFPS);
                HidStatusChanged?.Invoke($"Auto rendering FPS updated to {TargetFPS}");
            }
        }

        #endregion

        #region HID Device Integration

        /// <summary>
        /// Enable HID transfer functionality
        /// </summary>
        /// <param name="enable">Whether to enable HID transfer</param>
        /// <param name="useSuspendMedia">Whether to use suspend media mode</param>
        public void EnableHidTransfer(bool enable, bool useSuspendMedia = false)
        {
            SendToHidDevices = enable;
            UseSuspendMedia = useSuspendMedia;

            if (enable)
            {
                HidStatusChanged?.Invoke($"HID transfer enabled ({(useSuspendMedia ? "suspend media" : "real-time")} mode)");
            }
            else
            {
                HidStatusChanged?.Invoke("HID transfer disabled");
            }
        }

        /// <summary>
        /// Enable real-time display mode on HID devices
        /// </summary>
        public async Task<bool> EnableHidRealTimeDisplayAsync(bool enable)
        {
            if (_hidDeviceService == null || !_hidDeviceService.IsInitialized)
            {
                HidStatusChanged?.Invoke("HID service not available");
                return false;
            }

            try
            {
                var results = await _hidDeviceService.SetRealTimeDisplayAsync(enable);
                var successCount = results.Values.Count(r => r);

                if (successCount > 0)
                {
                    _hidRealTimeModeEnabled = enable;
                    HidStatusChanged?.Invoke($"Real-time display {(enable ? "enabled" : "disabled")} on {successCount}/{results.Count} devices");
                    return true;
                }
                else
                {
                    HidStatusChanged?.Invoke("Failed to set real-time display mode on any device");
                    return false;
                }
            }
            catch (Exception ex)
            {
                HidStatusChanged?.Invoke($"Error setting real-time display mode: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send current frame to HID devices using suspend media
        /// </summary>
        public async Task<bool> SendCurrentFrameAsSuspendMediaAsync()
        {
            if (_hidDeviceService == null || !_hidDeviceService.IsInitialized)
            {
                HidStatusChanged?.Invoke("HID service not available");
                return false;
            }

            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"interactive_frame_{_transferId}_{DateTime.Now:yyyyMMddHHmmss}.jpg");
                await SaveRenderedImageAsync(tempPath);

                var results = await _hidDeviceService.SendMultipleSuspendFilesAsync(new[] { tempPath }.ToList(), _transferId);
                var successCount = results.Values.Count(r => r);

                if (successCount > 0)
                {
                    _hidFramesSent++;
                    HidStatusChanged?.Invoke($"Frame sent as suspend media to {successCount}/{results.Count} devices");

                    // Increment transfer ID
                    _transferId++;
                    if (_transferId > 59) _transferId = 1;

                    // Cleanup temp file
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch { /* Ignore cleanup errors */ }

                    return true;
                }
                else
                {
                    HidStatusChanged?.Invoke("Failed to send suspend media to any device");
                    return false;
                }
            }
            catch (Exception ex)
            {
                HidStatusChanged?.Invoke($"Error sending suspend media: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Set HID devices to suspend mode
        /// </summary>
        public async Task<bool> SetSuspendModeAsync()
        {
            if (_hidDeviceService == null || !_hidDeviceService.IsInitialized)
            {
                HidStatusChanged?.Invoke("HID service not available");
                return false;
            }

            try
            {
                var results = await _hidDeviceService.SetSuspendModeAsync();
                var successCount = results.Values.Count(r => r);

                if (successCount > 0)
                {
                    HidStatusChanged?.Invoke($"Suspend mode enabled on {successCount}/{results.Count} devices");
                    return true;
                }
                else
                {
                    HidStatusChanged?.Invoke("Failed to enable suspend mode on any device");
                    return false;
                }
            }
            catch (Exception ex)
            {
                HidStatusChanged?.Invoke($"Error setting suspend mode: {ex.Message}");
                return false;
            }
        }

        private async Task SendFrameToHidDevicesAsync(byte[] rawImageData)
        {
            try
            {
                // Convert raw BGRA pixel data to JPEG
                var jpegData = await ConvertToJpegAsync(rawImageData, Width, Height);
                if (jpegData != null && jpegData.Length > 0)
                {
                    // Increment transfer ID (1-59 range as per HID service requirement)
                    _transferId++;
                    if (_transferId > 59) _transferId = 1;

                    // Send JPEG data to HID devices using TransferDataAsync
                    var results = await _hidDeviceService!.TransferDataAsync(jpegData, _transferId);

                    // Log results
                    var successCount = results.Values.Count(r => r);
                    if (successCount > 0)
                    {
                        _hidFramesSent++;
                        JpegDataSentToHid?.Invoke(jpegData);
                        HidStatusChanged?.Invoke($"Frame #{_hidFramesSent} sent to {successCount}/{results.Count} devices (ID: {_transferId}, Size: {jpegData.Length:N0} bytes)");
                    }
                    else
                    {
                        HidStatusChanged?.Invoke("Failed to send frame to any HID devices");
                    }
                }
            }
            catch (Exception ex)
            {
                HidStatusChanged?.Invoke($"Error sending frame to HID devices: {ex.Message}");
                RenderingError?.Invoke(ex);
            }
        }

        private async Task<byte[]?> ConvertToJpegAsync(byte[] bgraData, int width, int height)
        {
            try
            {
                return await Task.Run(() =>
                {
                    // Convert BGRA to JPEG using WPF's BitmapEncoder
                    var stride = width * 4; // 4 bytes per pixel (BGRA)
                    var bitmap = BitmapSource.Create(width, height, 96, 96,
                        PixelFormats.Bgra32, null, bgraData, stride);

                    using (var stream = new MemoryStream())
                    {
                        var encoder = new JpegBitmapEncoder();
                        encoder.QualityLevel = JpegQuality;
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        encoder.Save(stream);
                        return stream.ToArray();
                    }
                });
            }
            catch (Exception ex)
            {
                HidStatusChanged?.Invoke($"Error converting to JPEG: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reset HID frame counter
        /// </summary>
        public void ResetHidFrameCounter()
        {
            _hidFramesSent = 0;
            HidStatusChanged?.Invoke("HID frame counter reset");
        }

        // HID Event Handlers
        private void OnHidDeviceConnected(object? sender, DeviceEventArgs e)
        {
            HidStatusChanged?.Invoke($"HID Device connected: {e.Device.ProductString}");
        }

        private void OnHidDeviceDisconnected(object? sender, DeviceEventArgs e)
        {
            HidStatusChanged?.Invoke($"HID Device disconnected: {e.Device.ProductString}");
        }

        private void OnHidDeviceError(object? sender, DeviceErrorEventArgs e)
        {
            HidStatusChanged?.Invoke($"HID Device error: {e.Exception.Message}");
        }

        private void OnHidRealTimeDisplayModeChanged(object? sender, RealTimeDisplayEventArgs e)
        {
            _hidRealTimeModeEnabled = e.IsEnabled;
            HidStatusChanged?.Invoke($"Real-time display mode {(e.IsEnabled ? "enabled" : "disabled")}");
        }

        #endregion

        private async Task LoadBackgroundImageAsync()
        {
            try
            {
                var imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "background.jpg");
                if (File.Exists(imagePath))
                {
                    _backgroundImage = await CanvasBitmap.LoadAsync(_canvasDevice!, imagePath);
                }
            }
            catch
            {
                _backgroundImage = null;
            }
        }

        private void InitializeDefaultElements()
        {
            if (ShowTime)
            {
                var timeElement = new LiveElement(ElementType.LiveTime, "Live Time")
                {
                    Position = new Point(Width / 2 - 150, 50),
                    Size = new Size(300, 60),
                    FontSize = 48,
                    FontFamily = "Segoe UI",
                    TextColor = WinUIColor.FromArgb(255, 255, 255, 255)
                };
                AddElement(timeElement);
            }

            if (ShowDate)
            {
                var dateElement = new LiveElement(ElementType.LiveDate, "Live Date")
                {
                    Position = new Point(Width / 2 - 200, 120),
                    Size = new Size(400, 30),
                    FontSize = 24,
                    FontFamily = "Segoe UI",
                    TextColor = WinUIColor.FromArgb(255, 192, 192, 192)
                };
                AddElement(dateElement);
            }

            if (ShowSystemInfo)
            {
                var systemInfoElement = new LiveElement(ElementType.SystemInfo, "System Info")
                {
                    Position = new Point(10, Height - 30),
                    Size = new Size(200, 20),
                    FontSize = 16,
                    FontFamily = "Consolas",
                    TextColor = WinUIColor.FromArgb(255, 192, 192, 192)
                };
                AddElement(systemInfoElement);
            }
        }

        public void AddElement(RenderElement element)
        {
            lock (_lockObject)
            {
                _elements.Add(element);
                _elements.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
            }
        }

        public void RemoveElement(RenderElement element)
        {
            lock (_lockObject)
            {
                _elements.Remove(element);
                if (SelectedElement == element)
                {
                    SelectedElement = null;
                }
            }
        }

        public void ClearElements()
        {
            lock (_lockObject)
            {
                _elements.Clear();
                SelectedElement = null;
            }
        }

        public List<RenderElement> GetElements()
        {
            lock (_lockObject)
            {
                return new List<RenderElement>(_elements);
            }
        }

        public RenderElement? HitTest(Point point)
        {
            lock (_lockObject)
            {
                // Test from top to bottom (highest Z-index first)
                for (int i = _elements.Count - 1; i >= 0; i--)
                {
                    var element = _elements[i];
                    if (element.IsVisible && element.HitTest(point))
                    {
                        return element;
                    }
                }
                return null;
            }
        }

        public void SelectElement(RenderElement? element)
        {
            if (SelectedElement != null)
            {
                SelectedElement.IsSelected = false;
            }

            SelectedElement = element;
            if (SelectedElement != null)
            {
                SelectedElement.IsSelected = true;
                ElementSelected?.Invoke(SelectedElement);
            }
        }

        public void MoveElement(RenderElement element, Point newPosition)
        {
            if (element.IsDraggable)
            {
                element.Position = newPosition;
                ElementMoved?.Invoke(element);
            }
        }

        public async Task<string> LoadImageAsync(string imagePath)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Image file not found");

            var key = Path.GetFileName(imagePath);
            if (!_loadedImages.ContainsKey(key))
            {
                var bitmap = await CanvasBitmap.LoadAsync(_canvasDevice!, imagePath);
                _loadedImages[key] = bitmap;
            }

            return key;
        }

        public async Task<WriteableBitmap?> RenderFrameAsync()
        {
            if (_canvasDevice == null || _renderTarget == null)
                return null;

            lock (_lockObject)
            {
                using (var session = _renderTarget.CreateDrawingSession())
                {
                    // Clear background
                    RenderBackground(session);

                    // Render all elements
                    RenderElements(session);

                    // Render animated elements if enabled
                    if (ShowAnimation)
                    {
                        RenderAnimatedElements(session);
                    }

                    // Render selection indicators
                    RenderSelectionIndicators(session);
                }
            }

            var writeableBitmap = await ConvertToWriteableBitmapAsync(_renderTarget);
            ImageRendered?.Invoke(writeableBitmap);

            return writeableBitmap;
        }

        private void RenderBackground(CanvasDrawingSession session)
        {
            if (_backgroundImage == null)
            {
                // Create gradient background
                using (var brush1 = new CanvasSolidColorBrush(_canvasDevice, WinUIColor.FromArgb(255, 30, 30, 60)))
                {
                    session.FillRectangle(0, 0, Width, Height, brush1);
                    
                    // Add gradient effect
                    for (int i = 0; i < Height; i += 10)
                    {
                        var alpha = (byte)(255 * i / Height);
                        using (var gradientBrush = new CanvasSolidColorBrush(_canvasDevice, WinUIColor.FromArgb(alpha, 10, 10, 30)))
                        {
                            session.FillRectangle(0, i, Width, 10, gradientBrush);
                        }
                    }
                }
            }
            else
            {
                session.DrawImage(_backgroundImage, new Rect(0, 0, Width, Height));
            }
        }

        private void RenderElements(CanvasDrawingSession session)
        {
            foreach (var element in _elements.Where(e => e.IsVisible))
            {
                RenderElement(session, element);
            }
        }

        private void RenderElement(CanvasDrawingSession session, RenderElement element)
        {
            switch (element)
            {
                case TextElement textElement:
                    RenderTextElement(session, textElement);
                    break;
                case LiveElement liveElement:
                    RenderLiveElement(session, liveElement);
                    break;
                case ImageElement imageElement:
                    RenderImageElement(session, imageElement);
                    break;
                case ShapeElement shapeElement:
                    RenderShapeElement(session, shapeElement);
                    break;
            }
        }

        private void RenderTextElement(CanvasDrawingSession session, TextElement element)
        {
            var textFormat = new CanvasTextFormat()
            {
                FontSize = element.FontSize,
                FontFamily = element.FontFamily,
                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                VerticalAlignment = CanvasVerticalAlignment.Top
            };

            var bounds = element.GetBounds();
            session.DrawText(element.Text, bounds, element.TextColor, textFormat);
        }

        private void RenderLiveElement(CanvasDrawingSession session, LiveElement element)
        {
            var textFormat = new CanvasTextFormat()
            {
                FontSize = element.FontSize,
                FontFamily = element.FontFamily,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center
            };

            var bounds = element.GetBounds();
            var text = element.GetCurrentText();
            
            // Add shadow effect for live elements
            session.DrawText(text, new Rect(bounds.X + 2, bounds.Y + 2, bounds.Width, bounds.Height), 
                WinUIColor.FromArgb(100, 0, 0, 0), textFormat);
            session.DrawText(text, bounds, element.TextColor, textFormat);
        }

        private void RenderImageElement(CanvasDrawingSession session, ImageElement element)
        {
            if (_loadedImages.TryGetValue(element.ImagePath, out var bitmap))
            {
                var bounds = element.GetBounds();
                session.DrawImage(bitmap, bounds);
            }
        }

        private void RenderShapeElement(CanvasDrawingSession session, ShapeElement element)
        {
            var bounds = element.GetBounds();
            
            switch (element.ShapeType)
            {
                case ShapeType.Circle:
                    var centerX = (float)(bounds.X + bounds.Width / 2);
                    var centerY = (float)(bounds.Y + bounds.Height / 2);
                    var radius = (float)(Math.Min(bounds.Width, bounds.Height) / 2);
                    
                    session.FillCircle(centerX, centerY, radius, element.FillColor);
                    if (element.StrokeWidth > 0)
                    {
                        session.DrawCircle(centerX, centerY, radius, element.StrokeColor, element.StrokeWidth);
                    }
                    break;
                
                case ShapeType.Rectangle:
                    session.FillRectangle(bounds, element.FillColor);
                    if (element.StrokeWidth > 0)
                    {
                        session.DrawRectangle(bounds, element.StrokeColor, element.StrokeWidth);
                    }
                    break;
                
                case ShapeType.Triangle:
                    // Simple triangle implementation using rectangle for now
                    session.FillRectangle(bounds, element.FillColor);
                    if (element.StrokeWidth > 0)
                    {
                        session.DrawRectangle(bounds, element.StrokeColor, element.StrokeWidth);
                    }
                    break;
            }
        }

        private void RenderAnimatedElements(CanvasDrawingSession session)
        {
            var centerX = Width / 2;
            var centerY = Height / 2;
            var time = DateTime.Now.Millisecond / 1000.0f;

            // Animated circle
            var circleRadius = 20 + (float)(Math.Sin(time * Math.PI * 2) * 10);
            session.FillCircle(centerX - 100, centerY, circleRadius, 
                WinUIColor.FromArgb(150, 100, 200, 255));

            // Animated rectangle
            var rectSize = 40 + (float)(Math.Cos(time * Math.PI * 2) * 15);
            session.FillRectangle(centerX + 100 - rectSize/2, centerY - rectSize/2, rectSize, rectSize, 
                WinUIColor.FromArgb(150, 255, 100, 100));
        }

        private void RenderSelectionIndicators(CanvasDrawingSession session)
        {
            if (SelectedElement != null && SelectedElement.IsVisible)
            {
                var bounds = SelectedElement.GetBounds();
                var expandedBounds = new Rect(bounds.X - 5, bounds.Y - 5, bounds.Width + 10, bounds.Height + 10);
                
                // Draw selection rectangle
                session.DrawRectangle(expandedBounds, WinUIColor.FromArgb(255, 255, 255, 0), 2);
                
                // Draw corner handles
                var handleSize = 8;
                var handles = new[]
                {
                    new Rect(expandedBounds.X - handleSize/2, expandedBounds.Y - handleSize/2, handleSize, handleSize),
                    new Rect(expandedBounds.X + expandedBounds.Width - handleSize/2, expandedBounds.Y - handleSize/2, handleSize, handleSize),
                    new Rect(expandedBounds.X - handleSize/2, expandedBounds.Y + expandedBounds.Height - handleSize/2, handleSize, handleSize),
                    new Rect(expandedBounds.X + expandedBounds.Width - handleSize/2, expandedBounds.Y + expandedBounds.Height - handleSize/2, handleSize, handleSize)
                };
                
                foreach (var handle in handles)
                {
                    session.FillRectangle(handle, WinUIColor.FromArgb(255, 255, 255, 0));
                    session.DrawRectangle(handle, WinUIColor.FromArgb(255, 0, 0, 0), 1);
                }
            }
        }

        private async Task<WriteableBitmap> ConvertToWriteableBitmapAsync(CanvasRenderTarget renderTarget)
        {
            if (renderTarget == null)
                throw new ArgumentNullException(nameof(renderTarget));

            var bytes = renderTarget.GetPixelBytes();
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("Failed to get pixel data from render target.");

            return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var writeableBitmap = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, null);
                    writeableBitmap.WritePixels(
                        new System.Windows.Int32Rect(0, 0, Width, Height),
                        bytes,
                        Width * 4,
                        0);
                    return writeableBitmap;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to create WriteableBitmap: {ex.Message}", ex);
                }
            });
        }

        public byte[]? GetRenderedImageBytes()
        {
            if (_renderTarget == null) return null;
            
            lock (_lockObject)
            {
                return _renderTarget.GetPixelBytes();
            }
        }

        public async Task SaveRenderedImageAsync(string filePath)
        {
            if (_renderTarget == null) return;
            
            await _renderTarget.SaveAsync(filePath);
        }

        public void Dispose()
        {
            // Stop auto rendering
            StopAutoRendering();
            _renderTimer?.Stop();

            // Unsubscribe from HID service events
            if (_hidDeviceService != null)
            {
                _hidDeviceService.DeviceConnected -= OnHidDeviceConnected;
                _hidDeviceService.DeviceDisconnected -= OnHidDeviceDisconnected;
                _hidDeviceService.DeviceError -= OnHidDeviceError;
                _hidDeviceService.RealTimeDisplayModeChanged -= OnHidRealTimeDisplayModeChanged;
            }

            // Dispose resources
            foreach (var bitmap in _loadedImages.Values)
            {
                bitmap?.Dispose();
            }
            _loadedImages.Clear();
            
            _backgroundImage?.Dispose();
            _renderTarget?.Dispose();
            _canvasDevice?.Dispose();
            _renderTimer = null;
        }
    }
}