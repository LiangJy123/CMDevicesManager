using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.IO;
using System.Linq;
using CMDevicesManager.Services;

namespace DevWinUIGallery.Services
{
    /// <summary>
    /// Background rendering service that provides offscreen Win2D canvas rendering
    /// independent of UI thread. Renders to images that can be displayed in UI elements.
    /// Enhanced with HID device integration for real-time JPEG streaming.
    /// Now works directly with pixel data from render target without XAML WriteableBitmap dependency.
    /// </summary>
    public class BackgroundRenderingService : IDisposable
    {
        private CanvasDevice _canvasDevice;
        private CanvasRenderTarget _renderTarget;
        private AdvancedBackgroundRenderer _renderer;
        private readonly DispatcherTimer _renderTimer;
        private bool _isInitialized = false;
        private bool _isRunning = false;
        private readonly Queue<SoftwareBitmap> _frameQueue = new();
        private readonly object _lockObject = new();
        private readonly object _jpegLockObject = new(); // Dedicated lock for JPEG operations
        private DateTime _startTime;
        
        // Direct element management since we're not using AdvancedBackgroundRenderer's canvas
        private readonly List<SimpleRenderElement> _elements = new();
        private int _nextElementId = 0;
        
        // Drag functionality
        private SimpleRenderElement _draggedElement = null;
        private Vector2 _dragOffset = Vector2.Zero;
        private bool _isDragging = false;

        // HID Device Integration
        private HidDeviceService? _hidService;
        private bool _isHidRealTimeModeEnabled = false;
        private int _jpegQuality = 85; // JPEG compression quality (1-100)
        private DateTime _lastHidFrameTime = DateTime.MinValue;
        private readonly int _hidFrameIntervalMs = 100; // Send frame to HID every 100ms (10fps for HID)

        private byte _transferId = 0; //unique number, 0~59


        /// <summary>
        /// Fired when a new frame is ready for display (using WriteableBitmap for UI compatibility)
        /// </summary>
        public event EventHandler<WriteableBitmap> FrameReady;

        /// <summary>
        /// Fired when a new frame is ready as raw pixel data
        /// </summary>
        public event EventHandler<PixelFrameEventArgs> PixelFrameReady;
        
        /// <summary>
        /// Fired when the service status changes
        /// </summary>
        public event EventHandler<string> StatusChanged;
        
        /// <summary>
        /// Fired when FPS counter is updated
        /// </summary>
        public event EventHandler<int> FpsUpdated;

        /// <summary>
        /// Fired when a JPEG frame is sent to HID devices
        /// </summary>
        public event EventHandler<HidFrameSentEventArgs> HidFrameSent;

        // Configuration properties
        public int TargetWidth { get; set; } = 480;
        public int TargetHeight { get; set; } = 480;
        public int TargetFps { get; set; } = 15;

        public bool IsRunning => _isRunning;
        public int ElementCount => _elements.Count;
        public bool IsHidRealTimeModeEnabled => _isHidRealTimeModeEnabled;
        public int JpegQuality 
        { 
            get 
            {
                lock (_jpegLockObject)
                {
                    return _jpegQuality;
                }
            }
            set 
            {
                lock (_jpegLockObject)
                {
                    _jpegQuality = Math.Clamp(value, 1, 100);
                }
            }
        }

        public BackgroundRenderingService()
        {
            _renderTimer = new DispatcherTimer();
            _renderTimer.Tick += OnRenderTick;
            _startTime = DateTime.Now;
            
            // Initialize HID service reference
            InitializeHidService();
        }

        /// <summary>
        /// Initialize HID service reference for device communication
        /// </summary>
        private void InitializeHidService()
        {
            try
            {
                if (ServiceLocator.IsHidDeviceServiceInitialized)
                {
                    _hidService = ServiceLocator.HidDeviceService;
                    StatusChanged?.Invoke(this, "HID service connected to background renderer");
                }
                else
                {
                    StatusChanged?.Invoke(this, "HID service not available - renderer will work in display-only mode");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Failed to connect to HID service: {ex.Message}");
            }
        }

        /// <summary>
        /// Enables or disables real-time mode for HID devices
        /// </summary>
        /// <param name="enable">True to enable real-time mode</param>
        /// <returns>True if successful</returns>
        public async Task<bool> SetHidRealTimeModeAsync(bool enable)
        {
            if (_hidService == null)
            {
                StatusChanged?.Invoke(this, "Cannot set HID real-time mode: HID service not available");
                return false;
            }

            try
            {
                var results = await _hidService.SetRealTimeDisplayAsync(enable);
                var successCount = results.Values.Count(r => r);
                var totalCount = results.Count;

                if (successCount > 0)
                {
                    _isHidRealTimeModeEnabled = enable;
                    StatusChanged?.Invoke(this, $"HID real-time mode {(enable ? "enabled" : "disabled")} on {successCount}/{totalCount} devices");
                    return true;
                }
                else
                {
                    StatusChanged?.Invoke(this, $"Failed to set HID real-time mode on any devices");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error setting HID real-time mode: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Asynchronously initializes the background rendering service
        /// </summary>
        /// <returns></returns>
        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            try
            {
                // Create canvas device directly
                _canvasDevice = CanvasDevice.GetSharedDevice();
                
                // Create render target
                _renderTarget = new CanvasRenderTarget(_canvasDevice, TargetWidth, TargetHeight, 96);

                // Initialize renderer directly - we'll handle drawing ourselves
                _renderer = new AdvancedBackgroundRenderer();
                
                StatusChanged?.Invoke(this, "Renderer created without canvas dependency");

                // Setup render timer
                _renderTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / TargetFps);

                _isInitialized = true;
                StatusChanged?.Invoke(this, "Background rendering service initialized");
                
                // Capture an initial frame to show the service is ready
                try
                {
                    var initialPixelFrame = await CapturePixelFrameAsync();
                    if (initialPixelFrame != null)
                    {
                        StatusChanged?.Invoke(this, "Initial frame captured successfully");
                        PixelFrameReady?.Invoke(this, initialPixelFrame);
                        
                        // Create WriteableBitmap for UI compatibility if FrameReady event has subscribers
                        if (FrameReady != null)
                        {
                            var bitmap = await ConvertPixelDataToWriteableBitmapAsync(initialPixelFrame);
                            if (bitmap != null)
                            {
                                FrameReady?.Invoke(this, bitmap);
                            }
                        }
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, "Initial frame capture returned null");
                    }
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"Initial frame capture failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Initialization failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Starts the background rendering process
        /// </summary>
        /// <param name="enableHidRealTime">Whether to enable HID real-time mode automatically</param>
        /// <returns></returns>
        public async Task StartRenderingAsync(bool enableHidRealTime = true)
        {
            if (!_isInitialized || _isRunning) return;

            try
            {
                // Reset start time for animations
                _startTime = DateTime.Now;
                
                // Start the renderer's animation timer
                _renderer?.StartAnimation();
                
                // Enable HID real-time mode if requested and HID service is available
                if (enableHidRealTime && _hidService != null)
                {
                    StatusChanged?.Invoke(this, "Enabling HID real-time mode...");
                    await SetHidRealTimeModeAsync(true);
                }
                
                // Start our render timer
                _renderTimer.Start();
                _isRunning = true;
                
                StatusChanged?.Invoke(this, $"Background rendering started{(_isHidRealTimeModeEnabled ? " with HID real-time mode" : "")}");
                
                // Immediately capture a frame to show initial content
                await Task.Run(async () =>
                {
                    await Task.Delay(100); // Small delay to ensure everything is initialized
                    var initialPixelFrame = await CapturePixelFrameAsync();
                    if (initialPixelFrame != null)
                    {
                        PixelFrameReady?.Invoke(this, initialPixelFrame);
                        
                        // Create WriteableBitmap for UI compatibility if FrameReady event has subscribers
                        if (FrameReady != null)
                        {
                            var bitmap = await ConvertPixelDataToWriteableBitmapAsync(initialPixelFrame);
                            if (bitmap != null)
                            {
                                FrameReady?.Invoke(this, bitmap);
                            }
                        }
                        
                        // Send initial frame to HID devices if real-time mode is enabled
                        if (_isHidRealTimeModeEnabled)
                        {
                            // await SendPixelFrameToHidDevicesAsync(initialPixelFrame);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Failed to start rendering: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops the background rendering process
        /// </summary>
        public void StopRendering()
        {
            if (!_isRunning) return;

            _renderTimer.Stop();
            _renderer?.StopAnimation();
            _isRunning = false;
            StatusChanged?.Invoke(this, "Background rendering stopped");
            
            // Disable HID real-time mode if it was enabled (fire and forget)
            if (_isHidRealTimeModeEnabled && _hidService != null)
            {
                StatusChanged?.Invoke(this, "Disabling HID real-time mode...");
                _ = Task.Run(async () => await SetHidRealTimeModeAsync(false));
            }
        }

        /// <summary>
        /// Clears all elements from the rendering
        /// </summary>
        public void ClearElements()
        {
            _elements.Clear();
            StatusChanged?.Invoke(this, "All elements cleared");
            TriggerFrameUpdateAndHidSend("element clearing");
        }

        /// <summary>
        /// Sets the render size for the offscreen canvas
        /// </summary>
        /// <param name="width">The new width</param>
        /// <param name="height">The new height</param>
        public void SetRenderSize(int width, int height)
        {
            if (width <= 0 || height <= 0) return;

            TargetWidth = width;
            TargetHeight = height;

            if (_canvasDevice != null)
            {
                // Recreate render target with new size
                _renderTarget?.Dispose();
                _renderTarget = new CanvasRenderTarget(_canvasDevice, width, height, 96);
            }
        }

        /// <summary>
        /// Sets the target frames per second (FPS) for the rendering
        /// </summary>
        /// <param name="fps">The new target FPS</param>
        public void SetTargetFps(int fps)
        {
            if (fps <= 0 || fps > 120) return;

            TargetFps = fps;
            _renderTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
        }

        private async void OnRenderTick(object sender, object e)
        {
            if (!_isRunning || _renderTarget == null) return;

            try
            {
                // Capture frame directly from render target as pixel data
                var pixelFrame = await CapturePixelFrameAsync();
                if (pixelFrame != null)
                {
                    // Fire pixel frame event first (primary event)
                    PixelFrameReady?.Invoke(this, pixelFrame);
                    
                    // Create WriteableBitmap for UI compatibility if FrameReady event has subscribers
                    if (FrameReady != null)
                    {
                        var bitmap = await ConvertPixelDataToWriteableBitmapAsync(pixelFrame);
                        if (bitmap != null)
                        {
                            FrameReady?.Invoke(this, bitmap);
                        }
                    }
                    
                    // Send frame to HID devices if real-time mode is enabled and enough time has passed
                    if (_isHidRealTimeModeEnabled)
                    {
                        var now = DateTime.Now;
                        if ((now - _lastHidFrameTime).TotalMilliseconds >= _hidFrameIntervalMs)
                        {
                            _lastHidFrameTime = now;
                            
                            // Send to HID devices in background to avoid blocking the render loop
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await SendPixelFrameToHidDevicesAsync(pixelFrame).ConfigureAwait(false);
                                }
                                catch (Exception hidEx)
                                {
                                    // Log HID errors but don't stop rendering
                                    StatusChanged?.Invoke(this, $"HID frame send error: {hidEx.Message}");
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Render tick failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Converts pixel data to WriteableBitmap for UI compatibility
        /// </summary>
        /// <param name="pixelFrame">The pixel frame data</param>
        /// <returns>WriteableBitmap for UI display</returns>
        private async Task<WriteableBitmap> ConvertPixelDataToWriteableBitmapAsync(PixelFrameEventArgs pixelFrame)
        {
            if (pixelFrame?.PixelData == null) return null;

            try
            {
                // Ensure we're on the UI thread for WriteableBitmap operations
                var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                if (dispatcherQueue == null)
                {
                    // We're not on the UI thread, we need to switch to it
                    WriteableBitmap result = null;
                    var tcs = new TaskCompletionSource<WriteableBitmap>();
                    
                    // Find the main window's dispatcher queue
                    Microsoft.UI.Dispatching.DispatcherQueue mainDispatcher = null;
                    try
                    {
                        // Try to get dispatcher from current application
                        var currentApp = Microsoft.UI.Xaml.Application.Current;
                        if (currentApp != null)
                        {
                            // Use reflection to safely access MainWindow without knowing the exact App type
                            var mainWindowProperty = currentApp.GetType().GetProperty("MainWindow");
                            if (mainWindowProperty?.GetValue(currentApp) is Microsoft.UI.Xaml.Window mainWindow)
                            {
                                mainDispatcher = mainWindow.DispatcherQueue;
                            }
                        }
                    }
                    catch
                    {
                        // Fallback - we'll handle this gracefully
                        mainDispatcher = null;
                    }
                    
                    if (mainDispatcher != null)
                    {
                        mainDispatcher.TryEnqueue(() =>
                        {
                            try
                            {
                                result = ConvertPixelDataToWriteableBitmapOnUIThread(pixelFrame);
                                tcs.SetResult(result);
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        });
                        
                        return await tcs.Task;
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, "Cannot convert pixel data to WriteableBitmap: No UI thread available");
                        return null;
                    }
                }
                else
                {
                    // We're already on the UI thread
                    return ConvertPixelDataToWriteableBitmapOnUIThread(pixelFrame);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Pixel data to WriteableBitmap conversion failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Internal method to convert pixel data to WriteableBitmap on UI thread
        /// </summary>
        private WriteableBitmap ConvertPixelDataToWriteableBitmapOnUIThread(PixelFrameEventArgs pixelFrame)
        {
            // Create WriteableBitmap
            var writeableBitmap = new WriteableBitmap(pixelFrame.Width, pixelFrame.Height);
            
            // Copy pixels to WriteableBitmap
            using (var stream = writeableBitmap.PixelBuffer.AsStream())
            {
                stream.Write(pixelFrame.PixelData, 0, pixelFrame.PixelData.Length);
            }

            return writeableBitmap;
        }

        /// <summary>
        /// Converts pixel data directly to JPEG format byte array
        /// </summary>
        /// <param name="pixelFrame">The pixel frame data</param>
        /// <returns>JPEG data as byte array</returns>
        private async Task<byte[]> ConvertPixelDataToJpegAsync(PixelFrameEventArgs pixelFrame)
        {
            if (pixelFrame?.PixelData == null) return null;

            try
            {
                // Lock to ensure thread-safe JPEG conversion
                return await Task.Run(async () =>
                {
                    lock (_jpegLockObject)
                    {
                        // Perform JPEG conversion in a thread-safe manner
                        return ConvertPixelDataToJpegSync(pixelFrame);
                    }
                });
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Pixel data to JPEG conversion failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Synchronous JPEG conversion method to be used within lock
        /// </summary>
        /// <param name="pixelFrame">The pixel frame data</param>
        /// <returns>JPEG data as byte array</returns>
        private byte[] ConvertPixelDataToJpegSync(PixelFrameEventArgs pixelFrame)
        {
            try
            {
                using (var stream = new InMemoryRandomAccessStream())
                {
                    // Create JPEG encoder
                    var encoder = BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream).GetAwaiter().GetResult();
                    
                    // Set pixel data directly from our pixel frame
                    encoder.SetPixelData(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        (uint)pixelFrame.Width,
                        (uint)pixelFrame.Height,
                        96, 96, pixelFrame.PixelData);

                    // Encode to stream
                    encoder.FlushAsync().GetAwaiter().GetResult();
                    
                    // Convert stream to byte array
                    var buffer = new byte[stream.Size];
                    stream.ReadAsync(buffer.AsBuffer(), (uint)stream.Size, InputStreamOptions.None).GetAwaiter().GetResult();
                    
                    return buffer;
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Synchronous JPEG conversion failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sends the current frame as JPEG data to HID devices using pixel data directly
        /// </summary>
        /// <param name="pixelFrame">The pixel frame to send</param>
        /// <returns>True if successful</returns>
        private async Task<bool> SendPixelFrameToHidDevicesAsync(PixelFrameEventArgs pixelFrame)
        {
            if (!_isHidRealTimeModeEnabled || _hidService == null || pixelFrame?.PixelData == null)
                return false;

            byte[] jpegData = null;
            byte transferId;
            
            try
            {
                // Convert pixel data to JPEG directly with locking
                jpegData = await ConvertPixelDataToJpegAsync(pixelFrame);
                if (jpegData == null || jpegData.Length == 0)
                {
                    StatusChanged?.Invoke(this, "Failed to convert pixel frame to JPEG");
                    return false;
                }

                // Lock during JPEG data operations and transfer ID management
                lock (_jpegLockObject)
                {
                    // Create a protected copy of jpegData to prevent modification during transmission
                    var jpegDataCopy = new byte[jpegData.Length];
                    Array.Copy(jpegData, jpegDataCopy, jpegData.Length);
                    jpegData = jpegDataCopy;
                    
                    // Get current transfer ID safely
                    transferId = _transferId;
                    
                    // Update transfer ID
                    _transferId++;
                    if (_transferId > 59) _transferId = 1;
                }

                // Send data to HID devices using TransferDataAsync (outside of lock to prevent blocking)
                var results = await _hidService.TransferDataAsync(jpegData, transferId);
                var successCount = results.Values.Count(r => r);
                var totalCount = results.Count;

                if (successCount > 0)
                {
                    var eventArgs = new HidFrameSentEventArgs
                    {
                        FrameSize = jpegData.Length,
                        DevicesSucceeded = successCount,
                        DevicesTotal = totalCount,
                        JpegQuality = JpegQuality, // Use property instead of direct field access
                        Timestamp = DateTime.Now
                    };
                    
                    HidFrameSent?.Invoke(this, eventArgs);
                    return true;
                }
                else
                {
                    StatusChanged?.Invoke(this, $"Failed to send frame to any HID devices");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error sending pixel frame to HID devices: {ex.Message}");
                return false;
            }
            finally
            {
                // Clear jpegData reference to help GC
                jpegData = null;
            }
        }

        /// <summary>
        /// Helper method to trigger frame update and HID sending in a non-blocking way
        /// </summary>
        /// <param name="operationName">Name of the operation for logging</param>
        private void TriggerFrameUpdateAndHidSend(string operationName)
        {
            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            dispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
            {
                try
                {
                    var pixelFrame = await CapturePixelFrameAsync();
                    if (pixelFrame != null)
                    {
                        PixelFrameReady?.Invoke(this, pixelFrame);
                        
                        // Create WriteableBitmap for UI compatibility if FrameReady event has subscribers
                        if (FrameReady != null)
                        {
                            var bitmap = await ConvertPixelDataToWriteableBitmapAsync(pixelFrame);
                            if (bitmap != null)
                            {
                                FrameReady?.Invoke(this, bitmap);
                            }
                        }
                        
                        // Send to HID devices if real-time mode is enabled (non-blocking)
                        if (_isHidRealTimeModeEnabled)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    // await SendPixelFrameToHidDevicesAsync(pixelFrame).ConfigureAwait(false);
                                }
                                catch (Exception hidEx)
                                {
                                    StatusChanged?.Invoke(this, $"HID frame send error in {operationName}: {hidEx.Message}");
                                }
                            });
                        }
                        
                        StatusChanged?.Invoke(this, $"Frame updated after {operationName}");
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, $"Failed to capture frame after {operationName}");
                    }
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"Error updating frame in {operationName}: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Captures the current frame as pixel data directly from render target
        /// </summary>
        /// <returns>Pixel frame data</returns>
        public async Task<PixelFrameEventArgs> CapturePixelFrameAsync()
        {
            if (!_isInitialized || _renderer == null || _renderTarget == null) return null;

            try
            {
                // Render using the simplified drawing logic
                using (var session = _renderTarget.CreateDrawingSession())
                {
                    DrawFrameUsingRenderer(session, DateTime.Now);
                }

                // Get pixel data directly from render target
                var pixels = _renderTarget.GetPixelBytes();
                
                return new PixelFrameEventArgs
                {
                    PixelData = pixels,
                    Width = (int)_renderTarget.SizeInPixels.Width,
                    Height = (int)_renderTarget.SizeInPixels.Height,
                    Format = PixelFormat.Bgra8,
                    Timestamp = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Pixel frame capture failed: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Captures the current frame as a WriteableBitmap (for backward compatibility)
        /// </summary>
        /// <returns></returns>
        public async Task<WriteableBitmap> CaptureFrameAsync()
        {
            var pixelFrame = await CapturePixelFrameAsync();
            if (pixelFrame == null) return null;

            return await ConvertPixelDataToWriteableBitmapAsync(pixelFrame);
        }

        /// <summary>
        /// Simplified frame drawing for demo purposes
        /// </summary>
        private void DrawFrameUsingRenderer(CanvasDrawingSession session, DateTime currentTime)
        {
            try
            {
                // Clear with background
                session.Clear(Microsoft.UI.Colors.Black);
                
                if (_elements.Count > 0)
                {
                    // Draw all elements
                    foreach (var element in _elements)
                    {
                        if (!element.IsVisible) continue;
                        
                        try
                        {
                            // Simplified drawing
                            var textFormat = new CanvasTextFormat
                            {
                                FontSize = 24,
                                FontFamily = "Segoe UI"
                            };

                            var text = element.Text ?? "Sample Text";
                            session.DrawText(text, element.Position, Microsoft.UI.Colors.White, textFormat);
                        }
                        catch (Exception ex)
                        {
                            StatusChanged?.Invoke(this, $"Error drawing element {element.Id}: {ex.Message}");
                        }
                    }
                    
                    // Add debug info overlay
                    var debugFormat = new CanvasTextFormat
                    {
                        FontSize = 16,
                        FontFamily = "Segoe UI",
                        HorizontalAlignment = CanvasHorizontalAlignment.Right
                    };
                    
                    var debugX = TargetWidth - 250;
                    session.DrawText($"Elements: {_elements.Count}", 
                        new Vector2(debugX, 10), Microsoft.UI.Colors.LightGray, debugFormat);
                    session.DrawText($"Size: {TargetWidth}x{TargetHeight}", 
                        new Vector2(debugX, 30), Microsoft.UI.Colors.LightGray, debugFormat);
                    session.DrawText($"Time: {currentTime:HH:mm:ss.fff}", 
                        new Vector2(debugX, 50), Microsoft.UI.Colors.LimeGreen, debugFormat);
                    session.DrawText($"Direct Pixel Mode", 
                        new Vector2(debugX, 110), Microsoft.UI.Colors.Magenta, debugFormat);
                    
                    // Show HID status
                    if (_isHidRealTimeModeEnabled)
                    {
                        session.DrawText($"HID: Real-time ON", 
                            new Vector2(debugX, 70), Microsoft.UI.Colors.Cyan, debugFormat);
                        session.DrawText($"JPEG: {JpegQuality}%", 
                            new Vector2(debugX, 90), Microsoft.UI.Colors.Cyan, debugFormat);
                    }
                    else
                    {
                        session.DrawText($"HID: Display only", 
                            new Vector2(debugX, 70), Microsoft.UI.Colors.Orange, debugFormat);
                    }
                }
                else
                {
                    // Show placeholder content
                    var titleFormat = new CanvasTextFormat
                    {
                        FontSize = 32,
                        FontFamily = "Segoe UI",
                        HorizontalAlignment = CanvasHorizontalAlignment.Center
                    };
                    
                    var textFormat = new CanvasTextFormat
                    {
                        FontSize = 18,
                        FontFamily = "Segoe UI",
                        HorizontalAlignment = CanvasHorizontalAlignment.Center
                    };

                    var centerX = TargetWidth / 2f;
                    var startY = TargetHeight / 2f - 100;

                    session.DrawText("🎨 Direct Pixel Rendering Service", 
                        new Vector2(centerX, startY), Microsoft.UI.Colors.White, titleFormat);
                    session.DrawText("Working with raw pixel data from render target!", 
                        new Vector2(centerX, startY + 50), Microsoft.UI.Colors.LightBlue, textFormat);
                    session.DrawText($"Canvas Size: {TargetWidth} × {TargetHeight} pixels", 
                        new Vector2(centerX, startY + 80), Microsoft.UI.Colors.Gray, textFormat);
                    
                    // Show HID integration status
                    if (_hidService != null)
                    {
                        var hidStatus = _isHidRealTimeModeEnabled ? "🔄 HID Real-time Mode ACTIVE" : "🔌 HID Service Connected";
                        session.DrawText(hidStatus, 
                            new Vector2(centerX, startY + 120), 
                            _isHidRealTimeModeEnabled ? Microsoft.UI.Colors.LimeGreen : Microsoft.UI.Colors.Yellow, 
                            textFormat);
                        
                        if (_isHidRealTimeModeEnabled)
                        {
                            session.DrawText($"JPEG Quality: {_jpegQuality}% | Frame Rate: {_hidFrameIntervalMs}ms", 
                                new Vector2(centerX, startY + 140), Microsoft.UI.Colors.Cyan, textFormat);
                        }
                    }
                    else
                    {
                        session.DrawText("❌ HID Service Not Available", 
                            new Vector2(centerX, startY + 120), Microsoft.UI.Colors.Red, textFormat);
                    }
                    
                    // Show pixel mode indicator
                    session.DrawText("✨ Direct Pixel Mode: No WriteableBitmap dependency", 
                        new Vector2(centerX, startY + 170), Microsoft.UI.Colors.Magenta, textFormat);
                    
                    // Draw animated elements to show the service is working
                    var elapsedTime = (DateTime.Now - _startTime).TotalSeconds;
                    
                    var animatedX = centerX + (float)(Math.Sin(elapsedTime * 2) * 100);
                    session.FillCircle(new Vector2(animatedX, startY + 200), 12, Microsoft.UI.Colors.Orange);
                    
                    var pulseSize = 8 + (float)(Math.Sin(elapsedTime * 3) * 4);
                    session.FillCircle(new Vector2(centerX - 50, startY + 200), pulseSize, Microsoft.UI.Colors.LimeGreen);
                    
                    var rotateAngle = elapsedTime * 1.5;
                    var rotateX = centerX + 50 + (float)(Math.Cos(rotateAngle) * 30);
                    var rotateY = startY + 200 + (float)(Math.Sin(rotateAngle) * 30);
                    session.FillCircle(new Vector2(rotateX, rotateY), 8, Microsoft.UI.Colors.HotPink);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Drawing error: {ex.Message}");
                
                // Fallback drawing
                session.Clear(Microsoft.UI.Colors.DarkRed);
                var errorFormat = new CanvasTextFormat { FontSize = 16, FontFamily = "Segoe UI" };
                session.DrawText($"Rendering Error: {ex.Message}", 
                    new Vector2(50, 50), Microsoft.UI.Colors.White, errorFormat);
            }
        }

        /// <summary>
        /// Simple method to add a basic text element for testing
        /// </summary>
        /// <param name="text">The text to display</param>
        /// <returns>The element ID</returns>
        public int AddSimpleText(string text)
        {
            if (!_isInitialized)
            {
                StatusChanged?.Invoke(this, "Cannot add element: Service not initialized");
                return -1;
            }

            var element = new SimpleRenderElement
            {
                Id = _nextElementId++,
                Type = "LiveText",
                Position = new Vector2(100, 100),
                Text = text,
                StartTime = DateTime.Now,
                IsVisible = true
            };

            _elements.Add(element);
            StatusChanged?.Invoke(this, $"Added simple text element (ID: {element.Id}). Total elements: {_elements.Count}");
            TriggerFrameUpdateAndHidSend("simple text addition");
            return element.Id;
        }

        /// <summary>
        /// Adds a live text element to the rendering
        /// </summary>
        /// <param name="text">The text content</param>
        /// <param name="position">The position on screen</param>
        /// <param name="config">Optional configuration for the text element</param>
        /// <returns>The element ID</returns>
        public int AddLiveTextElement(string text, Vector2 position, object config = null)
        {
            if (!_isInitialized)
            {
                StatusChanged?.Invoke(this, "Cannot add element: Service not initialized");
                return -1;
            }

            var element = new SimpleRenderElement
            {
                Id = _nextElementId++,
                Type = "LiveText",
                Position = position,
                Text = text,
                StartTime = DateTime.Now,
                IsVisible = true
            };

            _elements.Add(element);
            StatusChanged?.Invoke(this, $"Added live text element (ID: {element.Id}). Total elements: {_elements.Count}");
            TriggerFrameUpdateAndHidSend("live text element addition");
            return element.Id;
        }

        /// <summary>
        /// Adds an image element to the rendering
        /// </summary>
        /// <param name="image">The image bitmap</param>
        /// <param name="position">The position on screen</param>
        /// <param name="config">Optional configuration for the image element</param>
        /// <returns>The element ID</returns>
        public int AddImageElement(CanvasBitmap image, Vector2 position, object config = null)
        {
            if (!_isInitialized)
            {
                StatusChanged?.Invoke(this, "Cannot add element: Service not initialized");
                return -1;
            }

            var element = new SimpleRenderElement
            {
                Id = _nextElementId++,
                Type = "Image",
                Position = position,
                Image = image,
                StartTime = DateTime.Now,
                IsVisible = true
            };

            _elements.Add(element);
            StatusChanged?.Invoke(this, $"Added image element (ID: {element.Id}). Total elements: {_elements.Count}");
            TriggerFrameUpdateAndHidSend("image element addition");
            return element.Id;
        }

        /// <summary>
        /// Adds a particle system to the rendering
        /// </summary>
        /// <param name="position">The position on screen</param>
        /// <param name="config">Optional configuration for the particle system</param>
        /// <returns>The element ID</returns>
        public int AddParticleSystem(Vector2 position, object config = null)
        {
            if (!_isInitialized)
            {
                StatusChanged?.Invoke(this, "Cannot add element: Service not initialized");
                return -1;
            }

            var element = new SimpleRenderElement
            {
                Id = _nextElementId++,
                Type = "ParticleSystem",
                Position = position,
                StartTime = DateTime.Now,
                IsVisible = true
            };

            _elements.Add(element);
            StatusChanged?.Invoke(this, $"Added particle system (ID: {element.Id}). Total elements: {_elements.Count}");
            TriggerFrameUpdateAndHidSend("particle system addition");
            return element.Id;
        }

        /// <summary>
        /// Handles pointer pressed event for drag functionality
        /// </summary>
        /// <param name="pointerPosition">The pointer position relative to the render target</param>
        /// <returns>True if an element was hit and drag started</returns>
        public bool OnPointerPressed(Vector2 pointerPosition)
        {
            // Find the topmost element at the pointer position (reverse order for z-ordering)
            for (int i = _elements.Count - 1; i >= 0; i--)
            {
                var element = _elements[i];
                if (!element.IsVisible) continue;

                if (IsPointerOverElement(pointerPosition, element))
                {
                    _draggedElement = element;
                    _dragOffset = pointerPosition - element.Position;
                    _isDragging = true;
                    
                    StatusChanged?.Invoke(this, $"Started dragging element {element.Id} ({element.Type})");
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Handles pointer moved event for drag functionality
        /// </summary>
        /// <param name="pointerPosition">The current pointer position</param>
        /// <returns>True if dragging is active</returns>
        public bool OnPointerMoved(Vector2 pointerPosition)
        {
            if (_isDragging && _draggedElement != null)
            {
                var newPosition = pointerPosition - _dragOffset;
                
                // Clamp position to render target bounds
                newPosition.X = Math.Max(0, Math.Min(TargetWidth - 100, newPosition.X));
                newPosition.Y = Math.Max(0, Math.Min(TargetHeight - 50, newPosition.Y));
                
                _draggedElement.Position = newPosition;
                
                // Trigger frame update
                TriggerFrameUpdateAndHidSend("element drag");
                
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Handles pointer released event for drag functionality
        /// </summary>
        /// <returns>True if drag operation was completed</returns>
        public bool OnPointerReleased()
        {
            if (_isDragging && _draggedElement != null)
            {
                StatusChanged?.Invoke(this, $"Finished dragging element {_draggedElement.Id} to position ({_draggedElement.Position.X:F1}, {_draggedElement.Position.Y:F1})");
                
                _draggedElement = null;
                _isDragging = false;
                _dragOffset = Vector2.Zero;
                
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Checks if dragging is currently active
        /// </summary>
        public bool IsDragging => _isDragging;

        /// <summary>
        /// Checks if pointer is over a specific element
        /// </summary>
        /// <param name="pointerPosition">The pointer position</param>
        /// <param name="element">The element to check</param>
        /// <returns>True if pointer is over the element</returns>
        private bool IsPointerOverElement(Vector2 pointerPosition, SimpleRenderElement element)
        {
            // Simple bounds checking - assuming text/elements are roughly 100x50 pixels
            var elementBounds = new Windows.Foundation.Rect(element.Position.X, element.Position.Y, 100, 50);
            
            return pointerPosition.X >= elementBounds.X && 
                   pointerPosition.X <= elementBounds.X + elementBounds.Width &&
                   pointerPosition.Y >= elementBounds.Y && 
                   pointerPosition.Y <= elementBounds.Y + elementBounds.Height;
        }
        
        /// <summary>
        /// Disposes the service, stopping all rendering and clearing resources
        /// </summary>
        public void Dispose()
        {
            // Stop rendering and disable HID real-time mode
            if (_isRunning)
            {
                StopRendering();
            }
            
            _renderer?.Dispose();
            _renderTarget?.Dispose();
            _canvasDevice?.Dispose();
            
            // Clear frame queue
            lock (_lockObject)
            {
                while (_frameQueue.Count > 0)
                {
                    _frameQueue.Dequeue()?.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Event arguments for pixel frame events
    /// </summary>
    public class PixelFrameEventArgs : EventArgs
    {
        public byte[] PixelData { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public PixelFormat Format { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Pixel format enumeration
    /// </summary>
    public enum PixelFormat
    {
        Bgra8,
        Rgba8,
        Rgb24,
        Gray8
    }

    /// <summary>
    /// Event arguments for HID frame sent events
    /// </summary>
    public class HidFrameSentEventArgs : EventArgs
    {
        public int FrameSize { get; set; }
        public int DevicesSucceeded { get; set; }
        public int DevicesTotal { get; set; }
        public int JpegQuality { get; set; }
        public DateTime Timestamp { get; set; }
    }

    // Simple render element for direct rendering
    public class SimpleRenderElement
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public Vector2 Position { get; set; }
        public string Text { get; set; }
        public CanvasBitmap Image { get; set; }
        public DateTime StartTime { get; set; }
        public bool IsVisible { get; set; } = true;
    }
}