using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CMDevicesManager.Models;
using System.Numerics;
using System.Windows.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Point = Windows.Foundation.Point;
using Size = Windows.Foundation.Size;
using Rect = Windows.Foundation.Rect;
using WinUIColor = Windows.UI.Color;

namespace CMDevicesManager.Services
{
    public class InteractiveSkiaRenderingService : IDisposable
    {
        private SKSurface? _surface;
        private SKCanvas? _canvas;
        private SKBitmap? _canvasBitmap;
        private SKBitmap? _backgroundImage;
        private readonly object _lockObject = new object();
        private readonly List<RenderElement> _elements = new();
        private readonly Dictionary<string, SKBitmap> _loadedImages = new();
        private readonly Random _random = new Random();

        // Built-in render tick support
        private DispatcherTimer? _renderTimer;
        private bool _isAutoRenderingEnabled = false;
        private DateTime _lastRenderTime = DateTime.MinValue;

        // HID device integration
        private HidDeviceService? _hidDeviceService;
        private byte _transferId = 1;
        private bool _hidRealTimeModeEnabled = false;
        private int _hidFramesSent = 0;

        // Background configuration
        private BackgroundType _backgroundType = BackgroundType.Gradient;
        private SKColor _backgroundColor = new SKColor(30, 30, 60, 255);
        private SKColor _backgroundGradientEndColor = new SKColor(10, 10, 30, 255);
        private BackgroundScaleMode _backgroundScaleMode = BackgroundScaleMode.Stretch;
        private float _backgroundOpacity = 1.0f;
        private BackgroundGradientDirection _gradientDirection = BackgroundGradientDirection.TopToBottom;
        private string? _backgroundImagePath = null;

        // Events
        public event Action<WriteableBitmap>? ImageRendered;
        public event Action<RenderElement>? ElementSelected;
        public event Action<RenderElement>? ElementMoved;
        public event Action<byte[]>? RawImageDataReady;
        public event Action<byte[]>? JpegDataSentToHid;
        public event Action<string>? HidStatusChanged;
        public event Action<Exception>? RenderingError;
        public event Action<string>? BackgroundChanged;

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

        // Background properties
        public BackgroundType BackgroundType => _backgroundType;
        public SKColor BackgroundColor => _backgroundColor;
        public SKColor BackgroundGradientEndColor => _backgroundGradientEndColor;
        public BackgroundScaleMode BackgroundScaleMode => _backgroundScaleMode;
        public float BackgroundOpacity => _backgroundOpacity;
        public BackgroundGradientDirection GradientDirection => _gradientDirection;
        public string? BackgroundImagePath => _backgroundImagePath;
        public bool HasBackgroundImage => _backgroundImage != null;

        public async Task InitializeAsync(int width = 800, int height = 600)
        {
            Width = width;
            Height = height;

            try
            {
                // Create bitmap and surface for SkiaSharp
                _canvasBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                _surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), _canvasBitmap.GetPixels(), _canvasBitmap.RowBytes);
                _canvas = _surface.Canvas;

                if (_surface == null || _canvas == null)
                {
                    throw new InvalidOperationException("Failed to create SkiaSharp surface and canvas.");
                }

                // Load background image if it exists
                await LoadBackgroundImageAsync();

                // Initialize default elements
                InitializeDefaultElements();

                // Initialize render timer
                InitializeRenderTimer();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize InteractiveSkiaRenderingService: {ex.Message}", ex);
            }
        }

        #region Background Management

        /// <summary>
        /// Set a solid color background
        /// </summary>
        /// <param name="color">Background color</param>
        /// <param name="opacity">Background opacity (0.0 to 1.0)</param>
        public async Task SetBackgroundColorAsync(SKColor color, float opacity = 1.0f)
        {
            try
            {
                _backgroundType = BackgroundType.SolidColor;
                _backgroundColor = color.WithAlpha((byte)(255 * Math.Clamp(opacity, 0.0f, 1.0f)));
                _backgroundOpacity = Math.Clamp(opacity, 0.0f, 1.0f);
                
                // Clear any existing background image
                _backgroundImage?.Dispose();
                _backgroundImage = null;
                _backgroundImagePath = null;

                BackgroundChanged?.Invoke($"Background set to solid color: {color}");
            }
            catch (Exception ex)
            {
                RenderingError?.Invoke(new InvalidOperationException($"Failed to set background color: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// Set a gradient background
        /// </summary>
        /// <param name="startColor">Gradient start color</param>
        /// <param name="endColor">Gradient end color</param>
        /// <param name="direction">Gradient direction</param>
        /// <param name="opacity">Background opacity (0.0 to 1.0)</param>
        public async Task SetBackgroundGradientAsync(SKColor startColor, SKColor endColor, 
            BackgroundGradientDirection direction = BackgroundGradientDirection.TopToBottom, float opacity = 1.0f)
        {
            try
            {
                _backgroundType = BackgroundType.Gradient;
                _backgroundColor = startColor.WithAlpha((byte)(255 * Math.Clamp(opacity, 0.0f, 1.0f)));
                _backgroundGradientEndColor = endColor.WithAlpha((byte)(255 * Math.Clamp(opacity, 0.0f, 1.0f)));
                _gradientDirection = direction;
                _backgroundOpacity = Math.Clamp(opacity, 0.0f, 1.0f);
                
                // Clear any existing background image
                _backgroundImage?.Dispose();
                _backgroundImage = null;
                _backgroundImagePath = null;

                BackgroundChanged?.Invoke($"Background set to gradient: {startColor} to {endColor} ({direction})");
            }
            catch (Exception ex)
            {
                RenderingError?.Invoke(new InvalidOperationException($"Failed to set background gradient: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// Set a background image from file path
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <param name="scaleMode">How to scale the image</param>
        /// <param name="opacity">Background opacity (0.0 to 1.0)</param>
        public async Task SetBackgroundImageAsync(string imagePath, BackgroundScaleMode scaleMode = BackgroundScaleMode.Stretch, float opacity = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                throw new ArgumentException("Image path cannot be null or empty", nameof(imagePath));
            }

            if (!File.Exists(imagePath))
            {
                throw new FileNotFoundException($"Background image file not found: {imagePath}");
            }

            try
            {
                // Dispose existing background image
                _backgroundImage?.Dispose();

                // Load new background image
                _backgroundImage = SKBitmap.Decode(imagePath);
                _backgroundType = BackgroundType.Image;
                _backgroundScaleMode = scaleMode;
                _backgroundOpacity = Math.Clamp(opacity, 0.0f, 1.0f);
                _backgroundImagePath = imagePath;

                BackgroundChanged?.Invoke($"Background image set: {Path.GetFileName(imagePath)} (Scale: {scaleMode})");
            }
            catch (Exception ex)
            {
                _backgroundImage?.Dispose();
                _backgroundImage = null;
                _backgroundImagePath = null;
                RenderingError?.Invoke(new InvalidOperationException($"Failed to load background image '{imagePath}': {ex.Message}", ex));
                throw;
            }
        }

        /// <summary>
        /// Set a background image from byte array
        /// </summary>
        /// <param name="imageData">Image data as byte array</param>
        /// <param name="scaleMode">How to scale the image</param>
        /// <param name="opacity">Background opacity (0.0 to 1.0)</param>
        public async Task SetBackgroundImageAsync(byte[] imageData, BackgroundScaleMode scaleMode = BackgroundScaleMode.Stretch, float opacity = 1.0f)
        {
            if (imageData == null || imageData.Length == 0)
            {
                throw new ArgumentException("Image data cannot be null or empty", nameof(imageData));
            }

            try
            {
                // Dispose existing background image
                _backgroundImage?.Dispose();

                // Load background image from byte array
                _backgroundImage = SKBitmap.Decode(imageData);
                _backgroundType = BackgroundType.Image;
                _backgroundScaleMode = scaleMode;
                _backgroundOpacity = Math.Clamp(opacity, 0.0f, 1.0f);
                _backgroundImagePath = "<from byte array>";

                BackgroundChanged?.Invoke($"Background image set from byte array ({imageData.Length:N0} bytes, Scale: {scaleMode})");
            }
            catch (Exception ex)
            {
                _backgroundImage?.Dispose();
                _backgroundImage = null;
                _backgroundImagePath = null;
                RenderingError?.Invoke(new InvalidOperationException($"Failed to load background image from byte array: {ex.Message}", ex));
                throw;
            }
        }

        /// <summary>
        /// Clear background (transparent)
        /// </summary>
        public async Task ClearBackgroundAsync()
        {
            try
            {
                _backgroundType = BackgroundType.Transparent;
                _backgroundImage?.Dispose();
                _backgroundImage = null;
                _backgroundImagePath = null;

                BackgroundChanged?.Invoke("Background cleared (transparent)");
            }
            catch (Exception ex)
            {
                RenderingError?.Invoke(new InvalidOperationException($"Failed to clear background: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// Reset background to default gradient
        /// </summary>
        public async Task ResetBackgroundToDefaultAsync()
        {
            await SetBackgroundGradientAsync(
                new SKColor(30, 30, 60, 255),
                new SKColor(10, 10, 30, 255),
                BackgroundGradientDirection.TopToBottom);
        }

        #endregion

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
                    HidStatusChanged?.Invoke("HID service notinitialized");
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

                // Update live elements and motion elements
                UpdateElements();

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

        private void UpdateElements()
        {
            lock (_lockObject)
            {
                var currentTime = DateTime.Now;
                
                foreach (var element in _elements)
                {
                    // Update live elements
                    if (element is LiveElement)
                    {
                        // Live elements will automatically update their content when rendered
                        // This is handled in the RenderLiveElement method
                    }

                    // Update motion-enabled elements
                    if (element is IMotionElement motionElement)
                    {
                        UpdateMotionElement(motionElement, currentTime);
                    }
                }
            }
        }

        #region Motion Support

        /// <summary>
        /// Add a text element with motion capabilities
        /// </summary>
        public int AddTextElementWithMotion(string text, Point position, ElementMotionConfig? motionConfig = null, TextElementConfig? textConfig = null)
        {
            textConfig ??= new TextElementConfig();
            motionConfig ??= new ElementMotionConfig();

            var element = new MotionTextElement($"MotionText_{Guid.NewGuid()}")
            {
                Text = text,
                Position = position,
                OriginalPosition = position,
                Size = textConfig.Size,
                FontSize = textConfig.FontSize,
                FontFamily = textConfig.FontFamily,
                TextColor = textConfig.TextColor, // Use directly without conversion
                MotionConfig = motionConfig,
                IsDraggable = textConfig.IsDraggable,
                IsVisible = true
            };

            InitializeMotionElement(element, motionConfig);
            AddElement(element);
            
            return _elements.Count - 1;
        }

        /// <summary>
        /// Add a circle element with motion capabilities
        /// </summary>
        public int AddCircleElementWithMotion(Point position, float radius, WinUIColor color, ElementMotionConfig? motionConfig = null)
        {
            motionConfig ??= new ElementMotionConfig();

            var element = new MotionShapeElement($"MotionCircle_{Guid.NewGuid()}")
            {
                ShapeType = ShapeType.Circle,
                Position = position,
                OriginalPosition = position,
                Size = new Size(radius * 2, radius * 2),
                FillColor = color, // Use directly without conversion
                StrokeColor = WinUIColor.FromArgb(255, 255, 255, 255),
                StrokeWidth = 1,
                MotionConfig = motionConfig,
                IsDraggable = true,
                IsVisible = true
            };

            InitializeMotionElement(element, motionConfig);
            AddElement(element);
            
            return _elements.Count - 1;
        }

        private void InitializeMotionElement(IMotionElement element, ElementMotionConfig config)
        {
            element.LastUpdateTime = DateTime.Now;
            
            // Initialize motion-specific properties based on motion type
            switch (config.MotionType)
            {
                case MotionType.Bounce:
                    if (config.Direction == Vector2.Zero)
                    {
                        // Random direction if not specified
                        var angle = _random.NextDouble() * Math.PI * 2;
                        config.Direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                    }
                    break;

                case MotionType.Circular:
                case MotionType.Spiral:
                    if (config.Center == Vector2.Zero)
                    {
                        config.Center = new Vector2((float)element.Position.X, (float)element.Position.Y);
                    }
                    element.CurrentAngle = 0f;
                    break;

                case MotionType.Linear:
                    if (config.Direction == Vector2.Zero)
                    {
                        config.Direction = Vector2.UnitX; // Default right movement
                    }
                    break;
            }

            // Initialize trail system if enabled
            if (config.ShowTrail)
            {
                element.TrailPositions = new Queue<Vector2>();
            }
        }

        private void UpdateMotionElement(IMotionElement element, DateTime currentTime)
        {
            var config = element.MotionConfig;
            if (config.IsPaused) return;

            var deltaTime = (float)(currentTime - element.LastUpdateTime).TotalSeconds;
            element.LastUpdateTime = currentTime;

            var elapsedTime = (float)(currentTime - element.StartTime).TotalSeconds;

            switch (config.MotionType)
            {
                case MotionType.Linear:
                    UpdateLinearMotion(element, deltaTime, config);
                    break;
                case MotionType.Circular:
                    UpdateCircularMotion(element, elapsedTime, config);
                    break;
                case MotionType.Bounce:
                    UpdateBounceMotion(element, deltaTime, config);
                    break;
            }

            // Update trail if enabled
            if (config.ShowTrail && element.TrailPositions != null)
            {
                UpdateTrail(element);
            }
        }

        private void UpdateLinearMotion(IMotionElement element, float deltaTime, ElementMotionConfig config)
        {
            var velocity = config.Direction * config.Speed * deltaTime;
            var currentPos = new Vector2((float)element.Position.X, (float)element.Position.Y);
            var newPosition = currentPos + velocity;

            if (config.RespectBoundaries)
            {
                // Bounce off boundaries
                if (newPosition.X <= 0 || newPosition.X >= Width)
                {
                    config.Direction = new Vector2(-config.Direction.X, config.Direction.Y);
                }
                if (newPosition.Y <= 0 || newPosition.Y >= Height)
                {
                    config.Direction = new Vector2(config.Direction.X, -config.Direction.Y);
                }

                newPosition = Vector2.Clamp(newPosition, Vector2.Zero, new Vector2(Width, Height));
            }

            element.Position = new Point(newPosition.X, newPosition.Y);
        }

        private void UpdateCircularMotion(IMotionElement element, float elapsedTime, ElementMotionConfig config)
        {
            var angle = elapsedTime * config.Speed;
            element.CurrentAngle = angle;

            var x = config.Center.X + (float)Math.Cos(angle) * config.Radius;
            var y = config.Center.Y + (float)Math.Sin(angle) * config.Radius;

            element.Position = new Point(x, y);
        }

        private void UpdateBounceMotion(IMotionElement element, float deltaTime, ElementMotionConfig config)
        {
            var velocity = config.Direction * config.Speed * deltaTime;
            var currentPos = new Vector2((float)element.Position.X, (float)element.Position.Y);
            var newPosition = currentPos + velocity;

            // Check boundaries and bounce
            var bounced = false;
            if (newPosition.X <= 0 || newPosition.X >= Width)
            {
                config.Direction = new Vector2(-config.Direction.X, config.Direction.Y);
                bounced = true;
            }
            if (newPosition.Y <= 0 || newPosition.Y >= Height)
            {
                config.Direction = new Vector2(config.Direction.X, -config.Direction.Y);
                bounced = true;
            }

            if (bounced)
            {
                // Apply some randomness to prevent predictable bouncing patterns
                var randomFactor = 0.1f;
                config.Direction += new Vector2(
                    ((float)_random.NextDouble() - 0.5f) * randomFactor,
                    ((float)_random.NextDouble() - 0.5f) * randomFactor);
                config.Direction = Vector2.Normalize(config.Direction);
            }

            newPosition = Vector2.Clamp(newPosition, Vector2.Zero, new Vector2(Width, Height));
            element.Position = new Point(newPosition.X, newPosition.Y);
        }

        private void UpdateTrail(IMotionElement element)
        {
            if (element.TrailPositions == null) return;

            var currentPos = new Vector2((float)element.Position.X, (float)element.Position.Y);
            element.TrailPositions.Enqueue(currentPos);
            
            // Limit trail length
            var maxTrailLength = element.MotionConfig.TrailLength > 0 ? element.MotionConfig.TrailLength : 20;
            while (element.TrailPositions.Count > maxTrailLength)
            {
                element.TrailPositions.Dequeue();
            }
        }

        #endregion

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

        #endregion

        #region HID Device Integration

        /// <summary>
        /// Enable HID transfer functionality
        /// </summary>
        /// <param name="enable">Whether to enable HID transfer</param>
        /// <param name="useSuspendMedia">Whether to use suspend media mode</param>
        public async Task EnableHidTransfer(bool enable, bool useSuspendMedia = false)
        {
            SendToHidDevices = enable;
            UseSuspendMedia = useSuspendMedia;

            if (enable)
            {
                // Initialize HID service connection
                await InitializeHidServiceAsync();
                HidStatusChanged?.Invoke($"HID transfer enabled ({(useSuspendMedia ? "suspend media" : "real-time")} mode)");
            }
            else
            {
                HidStatusChanged?.Invoke("HID transfer disabled");
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
                    await SetBackgroundImageAsync(imagePath);
                }
                else
                {
                    // Set default gradient background
                    await ResetBackgroundToDefaultAsync();
                }
            }
            catch
            {
                // Fall back to default gradient
                await ResetBackgroundToDefaultAsync();
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
                
                // Update original position for motion elements
                if (element is IMotionElement motionElement)
                {
                    motionElement.OriginalPosition = newPosition;
                }
                
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
                var bitmap = SKBitmap.Decode(imagePath);
                _loadedImages[key] = bitmap;
            }

            return key;
        }

        public async Task<WriteableBitmap?> RenderFrameAsync()
        {
            if (_canvas == null || _canvasBitmap == null)
                return null;

            lock (_lockObject)
            {
                // Clear canvas
                _canvas.Clear(SKColors.Transparent);

                // Render background
                RenderBackground(_canvas);

                // Render all elements
                RenderElements(_canvas);

                // Render animated elements if enabled
                if (ShowAnimation)
                {
                    RenderAnimatedElements(_canvas);
                }

                // Render selection indicators
                RenderSelectionIndicators(_canvas);

                // Flush canvas
                _canvas.Flush();
            }

            var writeableBitmap = await ConvertToWriteableBitmapAsync(_canvasBitmap);
            ImageRendered?.Invoke(writeableBitmap);

            return writeableBitmap;
        }

        private void RenderBackground(SKCanvas canvas)
        {
            switch (_backgroundType)
            {
                case BackgroundType.Transparent:
                    // Clear with transparent
                    canvas.Clear(SKColors.Transparent);
                    break;

                case BackgroundType.SolidColor:
                    canvas.Clear(_backgroundColor);
                    break;

                case BackgroundType.Gradient:
                    RenderGradientBackground(canvas);
                    break;

                case BackgroundType.Image:
                    RenderImageBackground(canvas);
                    break;

                default:
                    // Fallback to default gradient
                    RenderDefaultGradientBackground(canvas);
                    break;
            }
        }

        private void RenderGradientBackground(SKCanvas canvas)
        {
            try
            {
                SKPoint startPoint, endPoint;
                
                switch (_gradientDirection)
                {
                    case BackgroundGradientDirection.LeftToRight:
                        startPoint = new SKPoint(0, Height / 2);
                        endPoint = new SKPoint(Width, Height / 2);
                        break;
                    case BackgroundGradientDirection.TopToBottom:
                        startPoint = new SKPoint(Width / 2, 0);
                        endPoint = new SKPoint(Width / 2, Height);
                        break;
                    case BackgroundGradientDirection.DiagonalTopLeftToBottomRight:
                        startPoint = new SKPoint(0, 0);
                        endPoint = new SKPoint(Width, Height);
                        break;
                    case BackgroundGradientDirection.DiagonalTopRightToBottomLeft:
                        startPoint = new SKPoint(Width, 0);
                        endPoint = new SKPoint(0, Height);
                        break;
                    case BackgroundGradientDirection.RadialFromCenter:
                        RenderRadialGradientBackground(canvas);
                        return;
                    default:
                        startPoint = new SKPoint(Width / 2, 0);
                        endPoint = new SKPoint(Width / 2, Height);
                        break;
                }

                var colors = new[] { _backgroundColor, _backgroundGradientEndColor };
                var positions = new float[] { 0f, 1f };

                using (var shader = SKShader.CreateLinearGradient(startPoint, endPoint, colors, positions, SKShaderTileMode.Clamp))
                using (var paint = new SKPaint { Shader = shader })
                {
                    canvas.DrawRect(0, 0, Width, Height, paint);
                }
            }
            catch (Exception ex)
            {
                // Fallback to solid color
                canvas.Clear(_backgroundColor);
                RenderingError?.Invoke(new InvalidOperationException($"Failed to render gradient background: {ex.Message}", ex));
            }
        }

        private void RenderRadialGradientBackground(SKCanvas canvas)
        {
            try
            {
                var center = new SKPoint(Width / 2, Height / 2);
                var radius = Math.Max(Width, Height) / 2.0f;

                var colors = new[] { _backgroundColor, _backgroundGradientEndColor };
                var positions = new float[] { 0f, 1f };

                using (var shader = SKShader.CreateRadialGradient(center, radius, colors, positions, SKShaderTileMode.Clamp))
                using (var paint = new SKPaint { Shader = shader })
                {
                    canvas.DrawRect(0, 0, Width, Height, paint);
                }
            }
            catch (Exception ex)
            {
                // Fallback to linear gradient
                RenderGradientBackground(canvas);
                RenderingError?.Invoke(new InvalidOperationException($"Failed to render radial gradient background: {ex.Message}", ex));
            }
        }

        private void RenderImageBackground(SKCanvas canvas)
        {
            if (_backgroundImage == null)
            {
                RenderDefaultGradientBackground(canvas);
                return;
            }

            try
            {
                var destRect = CalculateImageDestinationRect();
                
                using (var paint = new SKPaint())
                {
                    if (_backgroundOpacity < 1.0f)
                    {
                        paint.Color = paint.Color.WithAlpha((byte)(255 * _backgroundOpacity));
                    }
                    
                    canvas.DrawBitmap(_backgroundImage, destRect, paint);
                }
            }
            catch (Exception ex)
            {
                RenderDefaultGradientBackground(canvas);
                RenderingError?.Invoke(new InvalidOperationException($"Failed to render image background: {ex.Message}", ex));
            }
        }

        private SKRect CalculateImageDestinationRect()
        {
            if (_backgroundImage == null)
                return new SKRect(0, 0, Width, Height);

            var imageSize = new SKSize(_backgroundImage.Width, _backgroundImage.Height);
            var canvasAspect = (double)Width / Height;
            var imageAspect = imageSize.Width / imageSize.Height;

            switch (_backgroundScaleMode)
            {
                case BackgroundScaleMode.None:
                    // Center the image at original size
                    var x = (Width - imageSize.Width) / 2;
                    var y = (Height - imageSize.Height) / 2;
                    return new SKRect(x, y, x + imageSize.Width, y + imageSize.Height);

                case BackgroundScaleMode.Uniform:
                    // Scale to fit while maintaining aspect ratio
                    if (imageAspect > canvasAspect)
                    {
                        // Image is wider than canvas
                        var newHeight = Width / imageAspect;
                        var offsetY = (Height - newHeight) / 2;
                        return new SKRect(0, offsetY, Width, offsetY + newHeight);
                    }
                    else
                    {
                        // Image is taller than canvas
                        var newWidth = Height * imageAspect;
                        var offsetX = (Width - newWidth) / 2;
                        return new SKRect(offsetX, 0, offsetX + newWidth, Height);
                    }

                case BackgroundScaleMode.UniformToFill:
                    // Scale to fill while maintaining aspect ratio (may crop)
                    if (imageAspect > canvasAspect)
                    {
                        // Image is wider than canvas
                        var newWidth = Height * imageAspect;
                        var offsetX = (Width - newWidth) / 2;
                        return new SKRect(offsetX, 0, offsetX + newWidth, Height);
                    }
                    else
                    {
                        // Image is taller than canvas
                        var newHeight = Width / imageAspect;
                        var offsetY = (Height - newHeight) / 2;
                        return new SKRect(0, offsetY, Width, offsetY + newHeight);
                    }

                case BackgroundScaleMode.Stretch:
                default:
                    // Stretch to fill entire canvas
                    return new SKRect(0, 0, Width, Height);
            }
        }

        private void RenderDefaultGradientBackground(SKCanvas canvas)
        {
            // Create default gradient background
            var startColor = new SKColor(30, 30, 60, 255);
            var endColor = new SKColor(10, 10, 30, 255);
            var colors = new[] { startColor, endColor };
            var positions = new float[] { 0f, 1f };

            using (var shader = SKShader.CreateLinearGradient(new SKPoint(Width / 2, 0), new SKPoint(Width / 2, Height), colors, positions, SKShaderTileMode.Clamp))
            using (var paint = new SKPaint { Shader = shader })
            {
                canvas.DrawRect(0, 0, Width, Height, paint);
            }
        }

        private void RenderElements(SKCanvas canvas)
        {
            foreach (var element in _elements.Where(e => e.IsVisible))
            {
                RenderElement(canvas, element);
            }
        }

        private void RenderElement(SKCanvas canvas, RenderElement element)
        {
            switch (element)
            {
                case MotionTextElement motionTextElement:
                    RenderMotionTextElement(canvas, motionTextElement);
                    break;
                case MotionShapeElement motionShapeElement:
                    RenderMotionShapeElement(canvas, motionShapeElement);
                    break;
                case TextElement textElement:
                    RenderTextElement(canvas, textElement);
                    break;
                case LiveElement liveElement:
                    RenderLiveElement(canvas, liveElement);
                    break;
                case ImageElement imageElement:
                    RenderImageElement(canvas, imageElement);
                    break;
                case ShapeElement shapeElement:
                    RenderShapeElement(canvas, shapeElement);
                    break;
            }
        }

        private void RenderMotionTextElement(SKCanvas canvas, MotionTextElement element)
        {
            // Draw trail first
            if (element.MotionConfig.ShowTrail && element.TrailPositions != null)
            {
                DrawTrail(canvas, element.TrailPositions, ConvertToSKColor(element.TextColor));
            }

            using (var paint = new SKPaint())
            {
                paint.Color = ConvertToSKColor(element.TextColor).WithAlpha((byte)(element.TextColor.A * element.Opacity));
                paint.TextSize = element.FontSize;
                paint.IsAntialias = true;
                paint.Typeface = SKTypeface.FromFamilyName(element.FontFamily);

                var bounds = element.GetBounds();
                canvas.DrawText(element.Text, (float)bounds.X, (float)(bounds.Y + element.FontSize), paint);
            }
        }

        private void RenderMotionShapeElement(SKCanvas canvas, MotionShapeElement element)
        {
            // Draw trail first
            if (element.MotionConfig.ShowTrail && element.TrailPositions != null)
            {
                DrawTrail(canvas, element.TrailPositions, ConvertToSKColor(element.FillColor));
            }

            var bounds = element.GetBounds();
            
            switch (element.ShapeType)
            {
                case ShapeType.Circle:
                    var centerX = (float)(bounds.X + bounds.Width / 2);
                    var centerY = (float)(bounds.Y + bounds.Height / 2);
                    var radius = (float)(Math.Min(bounds.Width, bounds.Height) / 2);
                    
                    using (var fillPaint = new SKPaint())
                    {
                        fillPaint.Color = ConvertToSKColor(element.FillColor);
                        fillPaint.IsAntialias = true;
                        canvas.DrawCircle(centerX, centerY, radius, fillPaint);
                    }

                    if (element.StrokeWidth > 0)
                    {
                        using (var strokePaint = new SKPaint())
                        {
                            strokePaint.Color = ConvertToSKColor(element.StrokeColor);
                            strokePaint.Style = SKPaintStyle.Stroke;
                            strokePaint.StrokeWidth = element.StrokeWidth;
                            strokePaint.IsAntialias = true;
                            canvas.DrawCircle(centerX, centerY, radius, strokePaint);
                        }
                    }
                    break;
                
                case ShapeType.Rectangle:
                    var rect = new SKRect((float)bounds.X, (float)bounds.Y, (float)(bounds.X + bounds.Width), (float)(bounds.Y + bounds.Height));
                    
                    using (var fillPaint = new SKPaint())
                    {
                        fillPaint.Color = ConvertToSKColor(element.FillColor);
                        canvas.DrawRect(rect, fillPaint);
                    }

                    if (element.StrokeWidth > 0)
                    {
                        using (var strokePaint = new SKPaint())
                        {
                            strokePaint.Color = ConvertToSKColor(element.StrokeColor);
                            strokePaint.Style = SKPaintStyle.Stroke;
                            strokePaint.StrokeWidth = element.StrokeWidth;
                            canvas.DrawRect(rect, strokePaint);
                        }
                    }
                    break;
            }
        }

        private void DrawTrail(SKCanvas canvas, Queue<Vector2> trailPositions, SKColor baseColor)
        {
            if (trailPositions.Count < 2) return;

            var positions = trailPositions.ToArray();
            using (var paint = new SKPaint())
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 2f;
                paint.IsAntialias = true;

                for (int i = 1; i < positions.Length; i++)
                {
                    var alpha = (float)i / positions.Length; // Fade trail
                    paint.Color = baseColor.WithAlpha((byte)(baseColor.Alpha * alpha * 0.3f)); // Semi-transparent trail

                    canvas.DrawLine(positions[i - 1].X, positions[i - 1].Y, positions[i].X, positions[i].Y, paint);
                }
            }
        }

        private void RenderTextElement(SKCanvas canvas, TextElement element)
        {
            using (var paint = new SKPaint())
            {
                paint.Color = ConvertToSKColor(element.TextColor).WithAlpha((byte)(element.TextColor.A * element.Opacity));
                paint.TextSize = element.FontSize;
                paint.IsAntialias = true;
                paint.Typeface = SKTypeface.FromFamilyName(element.FontFamily);

                var bounds = element.GetBounds();
                canvas.DrawText(element.Text, (float)bounds.X, (float)(bounds.Y + element.FontSize), paint);
            }
        }

        private void RenderLiveElement(SKCanvas canvas, LiveElement element)
        {
            var text = element.GetCurrentText();
            
            // Add shadow effect for live elements
            using (var shadowPaint = new SKPaint())
            {
                shadowPaint.Color = new SKColor(0, 0, 0, 100);
                shadowPaint.TextSize = element.FontSize;
                shadowPaint.IsAntialias = true;
                shadowPaint.Typeface = SKTypeface.FromFamilyName(element.FontFamily);

                var bounds = element.GetBounds();
                canvas.DrawText(text, (float)(bounds.X + 2), (float)(bounds.Y + element.FontSize + 2), shadowPaint);
            }

            using (var paint = new SKPaint())
            {
                paint.Color = ConvertToSKColor(element.TextColor).WithAlpha((byte)(element.TextColor.A * element.Opacity));
                paint.TextSize = element.FontSize;
                paint.IsAntialias = true;
                paint.Typeface = SKTypeface.FromFamilyName(element.FontFamily);

                var bounds = element.GetBounds();
                canvas.DrawText(text, (float)bounds.X, (float)(bounds.Y + element.FontSize), paint);
            }
        }

        private void RenderImageElement(SKCanvas canvas, ImageElement element)
        {
            if (_loadedImages.TryGetValue(element.ImagePath, out var bitmap))
            {
                var bounds = element.GetBounds();
                var destRect = new SKRect((float)bounds.X, (float)bounds.Y, (float)(bounds.X + bounds.Width), (float)(bounds.Y + bounds.Height));

                using (var paint = new SKPaint())
                {
                    if (element.Opacity < 1.0f)
                    {
                        paint.Color = paint.Color.WithAlpha((byte)(255 * element.Opacity));
                    }
                    canvas.DrawBitmap(bitmap, destRect, paint);
                }
            }
        }

        private void RenderShapeElement(SKCanvas canvas, ShapeElement element)
        {
            var bounds = element.GetBounds();

            switch (element.ShapeType)
            {
                case ShapeType.Circle:
                    var centerX = (float)(bounds.X + bounds.Width / 2);
                    var centerY = (float)(bounds.Y + bounds.Height / 2);
                    var radius = (float)(Math.Min(bounds.Width, bounds.Height) / 2);

                    using (var fillPaint = new SKPaint())
                    {
                        fillPaint.Color = ConvertToSKColor(element.FillColor).WithAlpha((byte)(element.FillColor.A * element.Opacity));
                        fillPaint.IsAntialias = true;
                        canvas.DrawCircle(centerX, centerY, radius, fillPaint);
                    }

                    if (element.StrokeWidth > 0)
                    {
                        using (var strokePaint = new SKPaint())
                        {
                            strokePaint.Color = ConvertToSKColor(element.StrokeColor).WithAlpha((byte)(element.StrokeColor.A * element.Opacity));
                            strokePaint.Style = SKPaintStyle.Stroke;
                            strokePaint.StrokeWidth = element.StrokeWidth;
                            strokePaint.IsAntialias = true;
                            canvas.DrawCircle(centerX, centerY, radius, strokePaint);
                        }
                    }
                    break;

                case ShapeType.Rectangle:
                    var rect = new SKRect((float)bounds.X, (float)bounds.Y, (float)(bounds.X + bounds.Width), (float)(bounds.Y + bounds.Height));
                    
                    using (var fillPaint = new SKPaint())
                    {
                        fillPaint.Color = ConvertToSKColor(element.FillColor).WithAlpha((byte)(element.FillColor.A * element.Opacity));
                        canvas.DrawRect(rect, fillPaint);
                    }

                    if (element.StrokeWidth > 0)
                    {
                        using (var strokePaint = new SKPaint())
                        {
                            strokePaint.Color = ConvertToSKColor(element.StrokeColor).WithAlpha((byte)(element.StrokeColor.A * element.Opacity));
                            strokePaint.Style = SKPaintStyle.Stroke;
                            strokePaint.StrokeWidth = element.StrokeWidth;
                            canvas.DrawRect(rect, strokePaint);
                        }
                    }
                    break;
            }
        }

        private void RenderAnimatedElements(SKCanvas canvas)
        {
            var centerX = Width / 2;
            var centerY = Height / 2;
            var time = DateTime.Now.Millisecond / 1000.0f;

            // Animated circle
            var circleRadius = 20 + (float)(Math.Sin(time * Math.PI * 2) * 10);
            using (var paint = new SKPaint())
            {
                paint.Color = new SKColor(100, 200, 255, 150);
                paint.IsAntialias = true;
                canvas.DrawCircle(centerX - 100, centerY, circleRadius, paint);
            }

            // Animated rectangle
            var rectSize = 40 + (float)(Math.Cos(time * Math.PI * 2) * 15);
            using (var paint = new SKPaint())
            {
                paint.Color = new SKColor(255, 100, 100, 150);
                var rect = new SKRect(centerX + 100 - rectSize/2, centerY - rectSize/2, centerX + 100 + rectSize/2, centerY + rectSize/2);
                canvas.DrawRect(rect, paint);
            }
        }

        private void RenderSelectionIndicators(SKCanvas canvas)
        {
            if (SelectedElement != null && SelectedElement.IsVisible)
            {
                var bounds = SelectedElement.GetBounds();
                var expandedBounds = new SKRect((float)(bounds.X - 5), (float)(bounds.Y - 5), 
                                               (float)(bounds.X + bounds.Width + 5), (float)(bounds.Y + bounds.Height + 5));
                
                // Draw selection rectangle
                using (var paint = new SKPaint())
                {
                    paint.Color = new SKColor(255, 255, 0, 255);
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 2;
                    paint.IsAntialias = true;
                    canvas.DrawRect(expandedBounds, paint);
                }
                
                // Draw corner handles
                var handleSize = 8f;
                var handles = new[]
                {
                    new SKRect(expandedBounds.Left - handleSize/2, expandedBounds.Top - handleSize/2, 
                              expandedBounds.Left + handleSize/2, expandedBounds.Top + handleSize/2),
                    new SKRect(expandedBounds.Right - handleSize/2, expandedBounds.Top - handleSize/2, 
                              expandedBounds.Right + handleSize/2, expandedBounds.Top + handleSize/2),
                    new SKRect(expandedBounds.Left - handleSize/2, expandedBounds.Bottom - handleSize/2, 
                              expandedBounds.Left + handleSize/2, expandedBounds.Bottom + handleSize/2),
                    new SKRect(expandedBounds.Right - handleSize/2, expandedBounds.Bottom - handleSize/2, 
                              expandedBounds.Right + handleSize/2, expandedBounds.Bottom + handleSize/2)
                };
                
                foreach (var handle in handles)
                {
                    using (var fillPaint = new SKPaint())
                    {
                        fillPaint.Color = new SKColor(255, 255, 0, 255);
                        canvas.DrawRect(handle, fillPaint);
                    }
                    
                    using (var strokePaint = new SKPaint())
                    {
                        strokePaint.Color = new SKColor(0, 0, 0, 255);
                        strokePaint.Style = SKPaintStyle.Stroke;
                        strokePaint.StrokeWidth = 1;
                        canvas.DrawRect(handle, strokePaint);
                    }
                }
            }
        }

        private async Task<WriteableBitmap> ConvertToWriteableBitmapAsync(SKBitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var writeableBitmap = new WriteableBitmap(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null);
                    
                    // Get pixel data from SKBitmap
                    var pixels = bitmap.GetPixels();
                    var bytes = new byte[bitmap.RowBytes * bitmap.Height];
                    System.Runtime.InteropServices.Marshal.Copy(pixels, bytes, 0, bytes.Length);
                    
                    writeableBitmap.WritePixels(
                        new System.Windows.Int32Rect(0, 0, bitmap.Width, bitmap.Height),
                        bytes,
                        bitmap.RowBytes,
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
            if (_canvasBitmap == null) return null;
            
            lock (_lockObject)
            {
                var pixels = _canvasBitmap.GetPixels();
                var bytes = new byte[_canvasBitmap.RowBytes * _canvasBitmap.Height];
                System.Runtime.InteropServices.Marshal.Copy(pixels, bytes, 0, bytes.Length);
                return bytes;
            }
        }

        public async Task SaveRenderedImageAsync(string filePath)
        {
            if (_canvasBitmap == null) return;
            
            using (var image = SKImage.FromBitmap(_canvasBitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(filePath))
            {
                data.SaveTo(stream);
            }
        }

        // Helper method to convert WinUI Color to SKColor
        private SKColor ConvertToSKColor(WinUIColor color)
        {
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        // Helper method to convert SKColor to WinUI Color
        private WinUIColor ConvertToWinUIColor(SKColor color)
        {
            return WinUIColor.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
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
            _canvasBitmap?.Dispose();
            _surface?.Dispose();
            _renderTimer = null;
        }
    }
}