using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CMDevicesManager.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
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
    private DispatcherQueue? _dispatcherQueue;
    private DispatcherQueueTimer? _renderTimer;
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

            _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();

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

        /// <summary>
        /// Update background image scale mode
        /// </summary>
        /// <param name="scaleMode">New scale mode</param>
        public void SetBackgroundScaleMode(BackgroundScaleMode scaleMode)
        {
            if (_backgroundType == BackgroundType.Image)
            {
                _backgroundScaleMode = scaleMode;
                BackgroundChanged?.Invoke($"Background scale mode changed to: {scaleMode}");
            }
        }

        /// <summary>
        /// Update background opacity
        /// </summary>
        /// <param name="opacity">New opacity (0.0 to 1.0)</param>
        public void SetBackgroundOpacity(float opacity)
        {
            _backgroundOpacity = Math.Clamp(opacity, 0.0f, 1.0f);
            BackgroundChanged?.Invoke($"Background opacity changed to: {_backgroundOpacity:F2}");
        }

        /// <summary>
        /// Get background information
        /// </summary>
        /// <returns>Background information string</returns>
        public string GetBackgroundInfo()
        {
            return _backgroundType switch
            {
                BackgroundType.SolidColor => $"Solid Color: {_backgroundColor} (Opacity: {_backgroundOpacity:F2})",
                BackgroundType.Gradient => $"Gradient: {_backgroundColor} to {_backgroundGradientEndColor} ({_gradientDirection}, Opacity: {_backgroundOpacity:F2})",
                BackgroundType.Image => $"Image: {_backgroundImagePath ?? "Unknown"} (Scale: {_backgroundScaleMode}, Opacity: {_backgroundOpacity:F2})",
                BackgroundType.Transparent => "Transparent",
                _ => "Unknown"
            };
        }

        #endregion

        private void InitializeRenderTimer()
        {
            _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();

            if (_dispatcherQueue == null)
            {
                throw new InvalidOperationException("DispatcherQueue is not available. Initialize the rendering service on the UI thread.");
            }

            _renderTimer = _dispatcherQueue.CreateTimer();
            _renderTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / TargetFPS);
            _renderTimer.Tick += OnRenderTimerTick;
        }

        private void OnRenderTimerTick(DispatcherQueueTimer sender, object args)
        {
            OnRenderTick(sender, EventArgs.Empty);
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

        /// <summary>
        /// Add a rectangle element with motion capabilities
        /// </summary>
        public int AddRectangleElementWithMotion(Point position, Size size, WinUIColor color, ElementMotionConfig? motionConfig = null)
        {
            motionConfig ??= new ElementMotionConfig();

            var element = new MotionShapeElement($"MotionRectangle_{Guid.NewGuid()}")
            {
                ShapeType = ShapeType.Rectangle,
                Position = position,
                OriginalPosition = position,
                Size = size,
                FillColor = color,
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

        /// <summary>
        /// Add an image element with motion capabilities
        /// </summary>
        public int AddImageElementWithMotion(string imagePath, Point position, ElementMotionConfig? motionConfig = null, ImageElementConfig? imageConfig = null)
        {
            imageConfig ??= new ImageElementConfig();
            motionConfig ??= new ElementMotionConfig();

            var element = new MotionImageElement($"MotionImage_{Guid.NewGuid()}")
            {
                ImagePath = imagePath,
                Position = position,
                OriginalPosition = position,
                Size = imageConfig.Size,
                Scale = imageConfig.Scale,
                Rotation = imageConfig.Rotation,
                MotionConfig = motionConfig,
                IsDraggable = imageConfig.IsDraggable,
                IsVisible = true
            };

            InitializeMotionElement(element, motionConfig);
            AddElement(element);

            return _elements.Count - 1;
        }

        /// <summary>
        /// Set motion configuration for an existing element
        /// </summary>
        public void SetElementMotion(RenderElement element, ElementMotionConfig motionConfig)
        {
            if (element is IMotionElement motionElement)
            {
                motionElement.MotionConfig = motionConfig;
                InitializeMotionElement(motionElement, motionConfig);
                HidStatusChanged?.Invoke($"Motion updated for element: {element.Name}");
            }
        }

        /// <summary>
        /// Pause motion for an element
        /// </summary>
        public void PauseElementMotion(RenderElement element)
        {
            if (element is IMotionElement motionElement)
            {
                motionElement.MotionConfig.IsPaused = true;
            }
        }

        /// <summary>
        /// Resume motion for an element
        /// </summary>
        public void ResumeElementMotion(RenderElement element)
        {
            if (element is IMotionElement motionElement)
            {
                motionElement.MotionConfig.IsPaused = false;
            }
        }

        /// <summary>
        /// Pause all motion
        /// </summary>
        public void PauseAllMotion()
        {
            lock (_lockObject)
            {
                foreach (var element in _elements.OfType<IMotionElement>())
                {
                    element.MotionConfig.IsPaused = true;
                }
            }
            HidStatusChanged?.Invoke("All motion paused");
        }

        /// <summary>
        /// Resume all motion
        /// </summary>
        public void ResumeAllMotion()
        {
            lock (_lockObject)
            {
                foreach (var element in _elements.OfType<IMotionElement>())
                {
                    element.MotionConfig.IsPaused = false;
                }
            }
            HidStatusChanged?.Invoke("All motion resumed");
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
            if (bgraData == null || bgraData.Length == 0)
            {
                return null;
            }

            try
            {
                return await Task.Run(() =>
                {
                    var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

                    using var bitmap = new SKBitmap(info);
                    var destination = bitmap.GetPixels();
                    Marshal.Copy(bgraData, 0, destination, bgraData.Length);

                    using var image = SKImage.FromBitmap(bitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
                    return data?.ToArray();
                }).ConfigureAwait(false);
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

            //var key = Path.GetFileName(imagePath);
            var key = imagePath;
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
                        fillPaint.Color = ConvertToSKColor(element.FillColor).WithAlpha((byte)(element.FillColor.A * element.Opacity));
                        fillPaint.IsAntialias = true;
                        canvas.DrawCircle(centerX, centerY, radius, fillPaint);
                    }

                    if (element.StrokeWidth > 0)
                    {
                        using (var strokePaint = new SKPaint())
                        {
                            strokePaint.Color = ConvertToSKColor(element.FillColor).WithAlpha((byte)(element.FillColor.A * element.Opacity));
                            //strokePaint.Color = ConvertToSKColor(element.StrokeColor);
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
                        //fillPaint.Color = ConvertToSKColor(element.FillColor);
                        canvas.DrawRect(rect, fillPaint);
                    }

                    if (element.StrokeWidth > 0)
                    {
                        using (var strokePaint = new SKPaint())
                        {
                            strokePaint.Color = ConvertToSKColor(element.FillColor).WithAlpha((byte)(element.FillColor.A * element.Opacity));
                            //strokePaint.Color = ConvertToSKColor(element.StrokeColor);
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

            // Ensure conversion happens on dispatcher thread when possible
            if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
            {
                var tcs = new TaskCompletionSource<WriteableBitmap>();

                if (!_dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        tcs.SetResult(CreateWriteableBitmap(bitmap));
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }))
                {
                    tcs.SetException(new InvalidOperationException("Failed to enqueue bitmap conversion on DispatcherQueue."));
                }

                return await tcs.Task.ConfigureAwait(false);
            }

            return CreateWriteableBitmap(bitmap);
        }

        private WriteableBitmap CreateWriteableBitmap(SKBitmap bitmap)
        {
            try
            {
                var writeableBitmap = new WriteableBitmap(bitmap.Width, bitmap.Height);

                using (var stream = writeableBitmap.PixelBuffer.AsStream())
                {
                    var pixelSpan = bitmap.GetPixelSpan();
                    stream.Write(pixelSpan);
                }

                return writeableBitmap;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create WriteableBitmap: {ex.Message}", ex);
            }
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

        #region Element Property Update Methods

        /// <summary>
        /// Update element properties from editor values
        /// </summary>
        /// <param name="element">Element to update</param>
        /// <param name="properties">Dictionary of property name-value pairs</param>
        public void UpdateElementProperties(RenderElement element, Dictionary<string, object> properties)
        {
            if (element == null || properties == null) return;

            try
            {
                lock (_lockObject)
                {
                    // Update common properties
                    if (properties.TryGetValue("Position", out var position) && position is Point newPosition)
                    {
                        element.Position = newPosition;
                        if (element is IMotionElement motionElement)
                        {
                            motionElement.OriginalPosition = newPosition;
                        }
                    }

                    if (properties.TryGetValue("IsVisible", out var visible) && visible is bool isVisible)
                    {
                        element.IsVisible = isVisible;
                    }

                    if (properties.TryGetValue("IsDraggable", out var draggable) && draggable is bool isDraggable)
                    {
                        element.IsDraggable = isDraggable;
                    }

                    if (properties.TryGetValue("ZIndex", out var zIndex) && zIndex is float zIndexValue)
                    {
                        element.ZIndex = zIndexValue;
                        // Re-sort elements by Z-index
                        _elements.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
                    }
                    // Opacity property
                    if (properties.TryGetValue("Opacity", out var opacity) && opacity is float opacityValue)
                    {
                        element.Opacity = Math.Clamp(opacityValue, 0.0f, 1.0f);
                    }

                    // Update element-specific properties
                    UpdateElementSpecificProperties(element, properties);
                }

                // Notify that element was updated
                ElementMoved?.Invoke(element);
            }
            catch (Exception ex)
            {
                RenderingError?.Invoke(new InvalidOperationException($"Failed to update element properties: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// Update properties specific to element type
        /// </summary>
        private void UpdateElementSpecificProperties(RenderElement element, Dictionary<string, object> properties)
        {
            switch (element)
            {
                case TextElement textElement:
                    UpdateTextElementProperties(textElement, properties);
                    break;
                case ShapeElement shapeElement:
                    UpdateShapeElementProperties(shapeElement, properties);
                    break;
                case ImageElement imageElement:
                    UpdateImageElementProperties(imageElement, properties);
                    break;
                case LiveElement liveElement:
                    UpdateLiveElementProperties(liveElement, properties);
                    break;
            }

            // Update motion properties if element has motion
            if (element is IMotionElement motionElement)
            {
                UpdateMotionElementProperties(motionElement, properties);
            }
        }

        /// <summary>
        /// Update text element specific properties
        /// </summary>
        private void UpdateTextElementProperties(TextElement element, Dictionary<string, object> properties)
        {
            if (properties.TryGetValue("Text", out var text) && text is string textValue)
            {
                element.Text = textValue;
            }

            if (properties.TryGetValue("FontSize", out var fontSize) && fontSize is float fontSizeValue)
            {
                element.FontSize = fontSizeValue;
            }

            if (properties.TryGetValue("FontFamily", out var fontFamily) && fontFamily is string fontFamilyValue)
            {
                element.FontFamily = fontFamilyValue;
            }

            if (properties.TryGetValue("TextColor", out var textColor) && textColor is WinUIColor textColorValue)
            {
                element.TextColor = textColorValue;
            }

            if (properties.TryGetValue("Size", out var size) && size is Size sizeValue)
            {
                element.Size = sizeValue;
            }
        }

        /// <summary>
        /// Update shape element specific properties
        /// </summary>
        private void UpdateShapeElementProperties(ShapeElement element, Dictionary<string, object> properties)
        {
            if (properties.TryGetValue("ShapeType", out var shapeType) && shapeType is ShapeType shapeTypeValue)
            {
                element.ShapeType = shapeTypeValue;
            }

            if (properties.TryGetValue("Size", out var size) && size is Size sizeValue)
            {
                element.Size = sizeValue;
            }

            if (properties.TryGetValue("FillColor", out var fillColor) && fillColor is WinUIColor fillColorValue)
            {
                element.FillColor = fillColorValue;
            }

            if (properties.TryGetValue("StrokeColor", out var strokeColor) && strokeColor is WinUIColor strokeColorValue)
            {
                element.StrokeColor = strokeColorValue;
            }

            if (properties.TryGetValue("StrokeWidth", out var strokeWidth) && strokeWidth is float strokeWidthValue)
            {
                element.StrokeWidth = strokeWidthValue;
            }
        }

        /// <summary>
        /// Update image element specific properties
        /// </summary>
        private void UpdateImageElementProperties(ImageElement element, Dictionary<string, object> properties)
        {
            if (properties.TryGetValue("Scale", out var scale) && scale is float scaleValue)
            {
                element.Scale = scaleValue;
            }

            if (properties.TryGetValue("Rotation", out var rotation) && rotation is float rotationValue)
            {
                element.Rotation = rotationValue;
            }

            if (properties.TryGetValue("Size", out var size) && size is Size sizeValue)
            {
                element.Size = sizeValue;
            }
        }

        /// <summary>
        /// Update live element specific properties
        /// </summary>
        private void UpdateLiveElementProperties(LiveElement element, Dictionary<string, object> properties)
        {
            if (properties.TryGetValue("Format", out var format) && format is string formatValue)
            {
                element.Format = formatValue;
            }

            if (properties.TryGetValue("FontSize", out var fontSize) && fontSize is float fontSizeValue)
            {
                element.FontSize = fontSizeValue;
            }

            if (properties.TryGetValue("FontFamily", out var fontFamily) && fontFamily is string fontFamilyValue)
            {
                element.FontFamily = fontFamilyValue;
            }

            if (properties.TryGetValue("TextColor", out var textColor) && textColor is WinUIColor textColorValue)
            {
                element.TextColor = textColorValue;
            }

            if (properties.TryGetValue("Size", out var size) && size is Size sizeValue)
            {
                element.Size = sizeValue;
            }
        }

        /// <summary>
        /// Update motion element specific properties
        /// </summary>
        private void UpdateMotionElementProperties(IMotionElement element, Dictionary<string, object> properties)
        {
            var config = element.MotionConfig;
            var configChanged = false;

            if (properties.TryGetValue("MotionType", out var motionType) && motionType is MotionType motionTypeValue)
            {
                config.MotionType = motionTypeValue;
                configChanged = true;
            }

            if (properties.TryGetValue("Speed", out var speed) && speed is float speedValue)
            {
                config.Speed = speedValue;
                configChanged = true;
            }

            if (properties.TryGetValue("Direction", out var direction) && direction is Vector2 directionValue)
            {
                config.Direction = directionValue;
                configChanged = true;
            }

            if (properties.TryGetValue("Center", out var center) && center is Vector2 centerValue)
            {
                config.Center = centerValue;
                configChanged = true;
            }

            if (properties.TryGetValue("Radius", out var radius) && radius is float radiusValue)
            {
                config.Radius = radiusValue;
                configChanged = true;
            }

            if (properties.TryGetValue("RespectBoundaries", out var respectBoundaries) && respectBoundaries is bool respectBoundariesValue)
            {
                config.RespectBoundaries = respectBoundariesValue;
                configChanged = true;
            }

            if (properties.TryGetValue("ShowTrail", out var showTrail) && showTrail is bool showTrailValue)
            {
                config.ShowTrail = showTrailValue;
                if (showTrailValue && element.TrailPositions == null)
                {
                    element.TrailPositions = new Queue<Vector2>();
                }
                configChanged = true;
            }

            if (properties.TryGetValue("TrailLength", out var trailLength) && trailLength is int trailLengthValue)
            {
                config.TrailLength = trailLengthValue;
                configChanged = true;
            }

            if (properties.TryGetValue("IsPaused", out var isPaused) && isPaused is bool isPausedValue)
            {
                config.IsPaused = isPausedValue;
                configChanged = true;
            }

            // Re-initialize motion element if configuration changed
            if (configChanged)
            {
                InitializeMotionElement(element, config);
            }
        }

        /// <summary>
        /// Get element properties for editor display
        /// </summary>
        /// <param name="element">Element to get properties from</param>
        /// <returns>Dictionary of property name-value pairs</returns>
        public Dictionary<string, object> GetElementProperties(RenderElement element)
        {
            if (element == null) return new Dictionary<string, object>();

            var properties = new Dictionary<string, object>();

            try
            {
                // Common properties
                properties["Id"] = element.Id;
                properties["Name"] = element.Name;
                properties["Type"] = element.Type.ToString();
                properties["Position"] = element.Position;
                properties["IsVisible"] = element.IsVisible;
                properties["IsDraggable"] = element.IsDraggable;
                properties["IsSelected"] = element.IsSelected;
                properties["ZIndex"] = element.ZIndex;

                // Element-specific properties
                switch (element)
                {
                    case TextElement textElement:
                        properties["Text"] = textElement.Text;
                        properties["FontSize"] = textElement.FontSize;
                        properties["FontFamily"] = textElement.FontFamily;
                        properties["TextColor"] = textElement.TextColor;
                        properties["Size"] = textElement.Size;
                        break;

                    case ShapeElement shapeElement:
                        properties["ShapeType"] = shapeElement.ShapeType;
                        properties["Size"] = shapeElement.Size;
                        properties["FillColor"] = shapeElement.FillColor;
                        properties["StrokeColor"] = shapeElement.StrokeColor;
                        properties["StrokeWidth"] = shapeElement.StrokeWidth;
                        break;

                    case ImageElement imageElement:
                        properties["ImagePath"] = imageElement.ImagePath;
                        properties["Size"] = imageElement.Size;
                        properties["Scale"] = imageElement.Scale;
                        properties["Rotation"] = imageElement.Rotation;
                        break;

                    case LiveElement liveElement:
                        properties["Format"] = liveElement.Format;
                        properties["FontSize"] = liveElement.FontSize;
                        properties["FontFamily"] = liveElement.FontFamily;
                        properties["TextColor"] = liveElement.TextColor;
                        properties["Size"] = liveElement.Size;
                        properties["CurrentText"] = liveElement.GetCurrentText();
                        break;
                }

                // Motion properties
                if (element is IMotionElement motionElement)
                {
                    properties["HasMotion"] = true;
                    properties["OriginalPosition"] = motionElement.OriginalPosition;
                    properties["MotionType"] = motionElement.MotionConfig.MotionType;
                    properties["Speed"] = motionElement.MotionConfig.Speed;
                    properties["Direction"] = motionElement.MotionConfig.Direction;
                    properties["Center"] = motionElement.MotionConfig.Center;
                    properties["Radius"] = motionElement.MotionConfig.Radius;
                    properties["RespectBoundaries"] = motionElement.MotionConfig.RespectBoundaries;
                    properties["ShowTrail"] = motionElement.MotionConfig.ShowTrail;
                    properties["TrailLength"] = motionElement.MotionConfig.TrailLength;
                    properties["IsPaused"] = motionElement.MotionConfig.IsPaused;
                    properties["StartTime"] = motionElement.StartTime;
                    properties["CurrentAngle"] = motionElement.CurrentAngle;
                }
                else
                {
                    properties["HasMotion"] = false;
                }
            }
            catch (Exception ex)
            {
                RenderingError?.Invoke(new InvalidOperationException($"Failed to get element properties: {ex.Message}", ex));
            }

            return properties;
        }

        /// <summary>
        /// Find element by ID
        /// </summary>
        /// <param name="elementId">Element ID</param>
        /// <returns>Element if found, null otherwise</returns>
        public RenderElement? FindElementById(Guid elementId)
        {
            lock (_lockObject)
            {
                return _elements.FirstOrDefault(e => e.Id == elementId);
            }
        }

        /// <summary>
        /// Find element by name
        /// </summary>
        /// <param name="elementName">Element name</param>
        /// <returns>Element if found, null otherwise</returns>
        public RenderElement? FindElementByName(string elementName)
        {
            if (string.IsNullOrWhiteSpace(elementName)) return null;

            lock (_lockObject)
            {
                return _elements.FirstOrDefault(e => string.Equals(e.Name, elementName, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Update element by ID
        /// </summary>
        /// <param name="elementId">Element ID</param>
        /// <param name="properties">Properties to update</param>
        /// <returns>True if element was found and updated</returns>
        public bool UpdateElementById(Guid elementId, Dictionary<string, object> properties)
        {
            var element = FindElementById(elementId);
            if (element == null) return false;

            UpdateElementProperties(element, properties);
            return true;
        }

        #endregion

        #region JSON Scene Export/Import

        /// <summary>
        /// Export current scene state to JSON
        /// </summary>
        /// <returns>JSON string containing complete scene data</returns>
        public async Task<string> ExportSceneToJsonAsync(string sceneID)
        {
            try
            {
                var sceneData = new SceneExportData
                {
                    SceneName = "Exported Scene",
                    SceneId = sceneID, // Add the SceneId here
                    ExportDate = DateTime.Now,
                    Version = "1.0",
                    CanvasWidth = Width,
                    CanvasHeight = Height,

                    // Display options
                    ShowTime = ShowTime,
                    ShowDate = ShowDate,
                    ShowSystemInfo = ShowSystemInfo,
                    ShowAnimation = ShowAnimation,

                    // Background configuration
                    BackgroundType = _backgroundType,
                    BackgroundOpacity = _backgroundOpacity,
                    BackgroundColor = SerializeColor(_backgroundColor),
                    BackgroundGradientEndColor = SerializeColor(_backgroundGradientEndColor),
                    BackgroundScaleMode = _backgroundScaleMode,
                    GradientDirection = _gradientDirection,
                    //BackgroundImagePath = _backgroundImagePath,
                    BackgroundImagePath = GetRelativeBackgroundImagePath(sceneID),


                    // Render settings
                    TargetFPS = TargetFPS,
                    JpegQuality = JpegQuality,

                    // Elements
                    Elements = await SerializeElementsAsync(sceneID)
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                };

                return JsonSerializer.Serialize(sceneData, options);
            }
            catch (Exception ex)
            {
                RenderingError?.Invoke(new InvalidOperationException($"Failed to export scene to JSON: {ex.Message}", ex));
                throw;
            }
        }

        /// <summary>
        /// Convert absolute background image path to relative path for export
        /// </summary>
        /// <param name="sceneID">Scene ID for building relative path</param>
        /// <returns>Relative path for background image</returns>
        private string? GetRelativeBackgroundImagePath(string sceneID)
        {
            if (string.IsNullOrEmpty(_backgroundImagePath) ||
                _backgroundImagePath == "<from byte array>")
                return _backgroundImagePath;

            // If it's already a relative path starting with "Scenes", return as-is
            if (_backgroundImagePath.StartsWith("Scenes" + Path.DirectorySeparatorChar) ||
                _backgroundImagePath.StartsWith("Scenes/"))
                return _backgroundImagePath;

            // Convert to relative path format: Scenes\sceneID\background\filename
            var filename = Path.GetFileName(_backgroundImagePath);
            return Path.Combine("Scenes", sceneID, "background", filename);
        }

        /// <summary>
        /// Import scene state from JSON
        /// </summary>
        /// <param name="jsonData">JSON string containing scene data</param>
        /// <returns>True if import was successful, false otherwise</returns>
        public async Task<bool> ImportSceneFromJsonAsync(string jsonData)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                };

                var sceneData = JsonSerializer.Deserialize<SceneExportData>(jsonData, options);
                if (sceneData == null)
                {
                    HidStatusChanged?.Invoke("Failed to deserialize scene data");
                    return false;
                }

                // Clear existing elements
                ClearElements();

                // Apply scene configuration
                await ApplySceneConfigurationAsync(sceneData);

                // Import elements
                await ImportElementsAsync(sceneData.Elements);

                HidStatusChanged?.Invoke($"Scene imported: {sceneData.SceneName} ({sceneData.Elements.Count} elements)");
                return true;
            }
            catch (Exception ex)
            {
                RenderingError?.Invoke(new InvalidOperationException($"Failed to import scene from JSON: {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>
        /// Get scene information as formatted string
        /// </summary>
        /// <returns>Formatted scene information</returns>
        public async Task<string> GetSceneInfoAsync()
        {
            try
            {
                var elements = GetElements();
                var motionElementCount = elements.Count(e => e is IMotionElement);

                var info = new StringBuilder();
                info.AppendLine("=== SCENE INFORMATION ===");
                info.AppendLine($"Scene Export Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                info.AppendLine($"Canvas Size: {Width} x {Height}");
                info.AppendLine();

                info.AppendLine("=== DISPLAY SETTINGS ===");
                info.AppendLine($"Show Time: {ShowTime}");
                info.AppendLine($"Show Date: {ShowDate}");
                info.AppendLine($"Show System Info: {ShowSystemInfo}");
                info.AppendLine($"Show Animation: {ShowAnimation}");
                info.AppendLine();

                info.AppendLine("=== BACKGROUND CONFIGURATION ===");
                info.AppendLine($"Background Type: {_backgroundType}");
                info.AppendLine($"Background Opacity: {_backgroundOpacity:F2}");

                switch (_backgroundType)
                {
                    case BackgroundType.SolidColor:
                        info.AppendLine($"Background Color: {_backgroundColor}");
                        break;
                    case BackgroundType.Gradient:
                        info.AppendLine($"Gradient Start: {_backgroundColor}");
                        info.AppendLine($"Gradient End: {_backgroundGradientEndColor}");
                        info.AppendLine($"Gradient Direction: {_gradientDirection}");
                        break;
                    case BackgroundType.Image:
                        info.AppendLine($"Image Path: {_backgroundImagePath ?? "Unknown"}");
                        info.AppendLine($"Scale Mode: {_backgroundScaleMode}");
                        break;
                }
                info.AppendLine();

                info.AppendLine("=== RENDER SETTINGS ===");
                info.AppendLine($"Target FPS: {TargetFPS}");
                info.AppendLine($"Auto Rendering: {IsAutoRenderingEnabled}");
                info.AppendLine($"JPEG Quality: {JpegQuality}%");
                info.AppendLine($"HID Service Connected: {IsHidServiceConnected}");
                info.AppendLine($"HID Real-time Mode: {IsHidRealTimeModeEnabled}");
                info.AppendLine($"HID Frames Sent: {HidFramesSent}");
                info.AppendLine();

                info.AppendLine("=== ELEMENTS ===");
                info.AppendLine($"Total Elements: {elements.Count}");
                info.AppendLine($"Motion Elements: {motionElementCount}");
                info.AppendLine($"Static Elements: {elements.Count - motionElementCount}");
                info.AppendLine();

                if (elements.Any())
                {
                    info.AppendLine("Element Details:");
                    foreach (var element in elements.OrderBy(e => e.ZIndex))
                    {
                        var motionInfo = element is IMotionElement me ? $" [Motion: {me.MotionConfig.MotionType}]" : "";
                        info.AppendLine($"  � {element.Name} ({element.Type}){motionInfo}");
                        info.AppendLine($"    Position: ({element.Position.X:F0}, {element.Position.Y:F0})");
                        info.AppendLine($"    Visible: {element.IsVisible}, Draggable: {element.IsDraggable}");

                        if (element is TextElement te)
                        {
                            var textPreview = te.Text.Length > 30 ? te.Text.Substring(0, 30) + "..." : te.Text;
                            info.AppendLine($"    Text: \"{textPreview}\" (Size: {te.FontSize})");
                        }
                        else if (element is ImageElement ie)
                        {
                            info.AppendLine($"    Image: {Path.GetFileName(ie.ImagePath)} (Scale: {ie.Scale})");
                        }
                        else if (element is ShapeElement se)
                        {
                            info.AppendLine($"    Shape: {se.ShapeType} (Size: {se.Size.Width:F0}x{se.Size.Height:F0})");
                        }

                        info.AppendLine();
                    }
                }

                return info.ToString();
            }
            catch (Exception ex)
            {
                RenderingError?.Invoke(new InvalidOperationException($"Failed to generate scene info: {ex.Message}", ex));
                return $"Error generating scene info: {ex.Message}";
            }
        }

        #endregion

        #region JSON Serialization Helper Methods

        private async Task<List<ElementExportData>> SerializeElementsAsync(string sceneID)
        {
            var elements = GetElements();
            var exportElements = new List<ElementExportData>();

            foreach (var element in elements)
            {
                var exportElement = new ElementExportData
                {
                    Id = element.Id.ToString(),
                    Name = element.Name,
                    Type = element.Type.ToString(),
                    Position = SerializePoint(element.Position),
                    IsVisible = element.IsVisible,
                    IsDraggable = element.IsDraggable,
                    ZIndex = element.ZIndex
                    // Opacity
                    , Opacity = element.Opacity
                };

                // Serialize element-specific properties
                switch (element)
                {
                    case TextElement textElement:
                        exportElement.TextData = new TextElementData
                        {
                            Text = textElement.Text,
                            FontSize = textElement.FontSize,
                            FontFamily = textElement.FontFamily,
                            TextColor = SerializeColor(textElement.TextColor),
                            Size = SerializeSize(textElement.Size)
                        };
                        break;

                    case ImageElement imageElement:
                        exportElement.ImageData = new ImageElementData
                        {
                            ImagePath = imageElement.ImagePath,
                            Size = SerializeSize(imageElement.Size),
                            Scale = imageElement.Scale,
                            Rotation = imageElement.Rotation
                        };
                        break;

                    case ShapeElement shapeElement:
                        exportElement.ShapeData = new ShapeElementData
                        {
                            ShapeType = shapeElement.ShapeType.ToString(),
                            Size = SerializeSize(shapeElement.Size),
                            FillColor = SerializeColor(shapeElement.FillColor),
                            StrokeColor = SerializeColor(shapeElement.StrokeColor),
                            StrokeWidth = shapeElement.StrokeWidth
                        };
                        break;

                    case LiveElement liveElement:
                        exportElement.LiveData = new LiveElementData
                        {
                            Format = liveElement.Format,
                            FontSize = liveElement.FontSize,
                            FontFamily = liveElement.FontFamily,
                            TextColor = SerializeColor(liveElement.TextColor),
                            Size = SerializeSize(liveElement.Size)
                        };
                        break;
                }

                // Serialize motion data if element has motion
                if (element is IMotionElement motionElement)
                {
                    exportElement.MotionData = new MotionElementData
                    {
                        OriginalPosition = SerializePoint(motionElement.OriginalPosition),
                        MotionConfig = SerializeMotionConfig(motionElement.MotionConfig),
                        StartTime = motionElement.StartTime,
                        CurrentAngle = motionElement.CurrentAngle
                    };
                }

                exportElements.Add(exportElement);
            }

            return exportElements;
        }

        private async Task ApplySceneConfigurationAsync(SceneExportData sceneData)
        {
            try
            {
                // Apply display settings
                ShowTime = sceneData.ShowTime;
                ShowDate = sceneData.ShowDate;
                ShowSystemInfo = sceneData.ShowSystemInfo;
                ShowAnimation = sceneData.ShowAnimation;

                // Apply render settings
                TargetFPS = sceneData.TargetFPS;
                JpegQuality = sceneData.JpegQuality;

                // Apply background settings
                switch (sceneData.BackgroundType)
                {
                    case BackgroundType.SolidColor:
                        var solidColor = DeserializeColor(sceneData.BackgroundColor);
                        var skSolidColor = new SkiaSharp.SKColor(solidColor.R, solidColor.G, solidColor.B, solidColor.A);
                        await SetBackgroundColorAsync(skSolidColor, sceneData.BackgroundOpacity);
                        break;

                    case BackgroundType.Gradient:
                        var startColor = DeserializeColor(sceneData.BackgroundColor);
                        var endColor = DeserializeColor(sceneData.BackgroundGradientEndColor);
                        var skStartColor = new SkiaSharp.SKColor(startColor.R, startColor.G, startColor.B, startColor.A);
                        var skEndColor = new SkiaSharp.SKColor(endColor.R, endColor.G, endColor.B, endColor.A);

                        await SetBackgroundGradientAsync(skEndColor, skEndColor, sceneData.GradientDirection, sceneData.BackgroundOpacity);
                        break;

                    case BackgroundType.Image:
                        if (!string.IsNullOrEmpty(sceneData.BackgroundImagePath) && File.Exists(sceneData.BackgroundImagePath))
                        {
                            await SetBackgroundImageAsync(sceneData.BackgroundImagePath, sceneData.BackgroundScaleMode, sceneData.BackgroundOpacity);
                        }
                        else
                        {
                            HidStatusChanged?.Invoke($"Background image not found: {sceneData.BackgroundImagePath}");
                        }
                        break;

                    case BackgroundType.Transparent:
                        await ClearBackgroundAsync();
                        break;
                }

                BackgroundChanged?.Invoke($"Scene configuration applied: {sceneData.SceneName}");
            }
            catch (Exception ex)
            {
                RenderingError?.Invoke(new InvalidOperationException($"Failed to apply scene configuration: {ex.Message}", ex));
            }
        }

        private async Task ImportElementsAsync(List<ElementExportData> elements)
        {
            foreach (var exportElement in elements.OrderBy(e => e.ZIndex))
            {
                try
                {
                    RenderElement? element = null;

                    // Create element based on type
                    switch (exportElement.Type)
                    {
                        case "Text":
                            if (exportElement.TextData != null)
                            {
                                element = new TextElement(exportElement.Name)
                                {
                                    Text = exportElement.TextData.Text,
                                    FontSize = exportElement.TextData.FontSize,
                                    FontFamily = exportElement.TextData.FontFamily,
                                    TextColor = DeserializeColor(exportElement.TextData.TextColor),
                                    Size = DeserializeSize(exportElement.TextData.Size)
                                };
                            }
                            break;

                        case "Image":
                            if (exportElement.ImageData != null)
                            {
                                element = new ImageElement(exportElement.Name)
                                {
                                    ImagePath = exportElement.ImageData.ImagePath,
                                    Size = DeserializeSize(exportElement.ImageData.Size),
                                    Scale = exportElement.ImageData.Scale,
                                    Rotation = exportElement.ImageData.Rotation
                                };

                                // Load image if it exists
                                if (File.Exists(exportElement.ImageData.ImagePath))
                                {
                                    await LoadImageAsync(exportElement.ImageData.ImagePath);
                                }
                            }
                            break;

                        case "Shape":
                            if (exportElement.ShapeData != null)
                            {
                                if (Enum.TryParse<ShapeType>(exportElement.ShapeData.ShapeType, out var shapeType))
                                {
                                    element = new ShapeElement(exportElement.Name)
                                    {
                                        ShapeType = shapeType,
                                        Size = DeserializeSize(exportElement.ShapeData.Size),
                                        FillColor = DeserializeColor(exportElement.ShapeData.FillColor),
                                        StrokeColor = DeserializeColor(exportElement.ShapeData.StrokeColor),
                                        StrokeWidth = exportElement.ShapeData.StrokeWidth
                                    };
                                }
                            }
                            break;

                        case "LiveTime":
                        case "LiveDate":
                        case "SystemInfo":
                            if (exportElement.LiveData != null && Enum.TryParse<ElementType>(exportElement.Type, out var liveType))
                            {
                                element = new LiveElement(liveType, exportElement.Name)
                                {
                                    Format = exportElement.LiveData.Format,
                                    FontSize = exportElement.LiveData.FontSize,
                                    FontFamily = exportElement.LiveData.FontFamily,
                                    TextColor = DeserializeColor(exportElement.LiveData.TextColor),
                                    Size = DeserializeSize(exportElement.LiveData.Size)
                                };
                            }
                            break;
                    }

                    if (element != null)
                    {
                        // Apply common properties
                        element.Position = DeserializePoint(exportElement.Position);
                        element.IsVisible = exportElement.IsVisible;
                        element.IsDraggable = exportElement.IsDraggable;
                        element.ZIndex = exportElement.ZIndex;
                        element.Opacity = exportElement.Opacity; // Apply opacity

                        // Convert to motion element if motion data exists
                        if (exportElement.MotionData != null)
                        {
                            element = await ConvertToMotionElementAsync(element, exportElement.MotionData);
                        }

                        AddElement(element);
                    }
                }
                catch (Exception ex)
                {
                    HidStatusChanged?.Invoke($"Failed to import element '{exportElement.Name}': {ex.Message}");
                }
            }
        }

        private async Task<RenderElement> ConvertToMotionElementAsync(RenderElement element, MotionElementData motionData)
        {
            var motionConfig = DeserializeMotionConfig(motionData.MotionConfig);

            switch (element)
            {
                case TextElement textElement:
                    var motionText = new MotionTextElement(textElement.Name)
                    {
                        Text = textElement.Text,
                        FontSize = textElement.FontSize,
                        FontFamily = textElement.FontFamily,
                        TextColor = textElement.TextColor,
                        Size = textElement.Size,
                        Position = textElement.Position,
                        OriginalPosition = DeserializePoint(motionData.OriginalPosition),
                        MotionConfig = motionConfig,
                        StartTime = motionData.StartTime,
                        CurrentAngle = motionData.CurrentAngle,
                        IsVisible = textElement.IsVisible,
                        IsDraggable = textElement.IsDraggable,
                        ZIndex = textElement.ZIndex
                    };
                    InitializeMotionElement(motionText, motionConfig);
                    return motionText;

                case ShapeElement shapeElement:
                    var motionShape = new MotionShapeElement(shapeElement.Name)
                    {
                        ShapeType = shapeElement.ShapeType,
                        Size = shapeElement.Size,
                        FillColor = shapeElement.FillColor,
                        StrokeColor = shapeElement.StrokeColor,
                        StrokeWidth = shapeElement.StrokeWidth,
                        Position = shapeElement.Position,
                        OriginalPosition = DeserializePoint(motionData.OriginalPosition),
                        MotionConfig = motionConfig,
                        StartTime = motionData.StartTime,
                        CurrentAngle = motionData.CurrentAngle,
                        IsVisible = shapeElement.IsVisible,
                        IsDraggable = shapeElement.IsDraggable,
                        ZIndex = shapeElement.ZIndex
                    };
                    InitializeMotionElement(motionShape, motionConfig);
                    return motionShape;

                case ImageElement imageElement:
                    var motionImage = new MotionImageElement(imageElement.Name)
                    {
                        ImagePath = imageElement.ImagePath,
                        Size = imageElement.Size,
                        Scale = imageElement.Scale,
                        Rotation = imageElement.Rotation,
                        Position = imageElement.Position,
                        OriginalPosition = DeserializePoint(motionData.OriginalPosition),
                        MotionConfig = motionConfig,
                        StartTime = motionData.StartTime,
                        CurrentAngle = motionData.CurrentAngle,
                        IsVisible = imageElement.IsVisible,
                        IsDraggable = imageElement.IsDraggable,
                        ZIndex = imageElement.ZIndex
                    };
                    InitializeMotionElement(motionImage, motionConfig);
                    return motionImage;

                default:
                    return element;
            }
        }
        #endregion

        #region Serialization Utility Methods

        private SerializedColor SerializeColor(WinUIColor color)
        {
            return new SerializedColor
            {
                A = color.A,
                R = color.R,
                G = color.G,
                B = color.B
            };
        }

        private SerializedColor SerializeColor(SKColor color)
        {
            return new SerializedColor
            {
                A = color.Alpha,
                R = color.Red,
                G = color.Green,
                B = color.Blue
            };
        }

        private WinUIColor DeserializeColor(SerializedColor color)
        {
            return WinUIColor.FromArgb(color.A, color.R, color.G, color.B);
        }

        private SerializedPoint SerializePoint(Point point)
        {
            return new SerializedPoint { X = point.X, Y = point.Y };
        }

        private Point DeserializePoint(SerializedPoint point)
        {
            return new Point(point.X, point.Y);
        }

        private SerializedSize SerializeSize(Size size)
        {
            return new SerializedSize { Width = size.Width, Height = size.Height };
        }

        private Size DeserializeSize(SerializedSize size)
        {
            return new Size(size.Width, size.Height);
        }

        private SerializedVector2 SerializeVector2(Vector2 vector)
        {
            return new SerializedVector2 { X = vector.X, Y = vector.Y };
        }

        private Vector2 DeserializeVector2(SerializedVector2 vector)
        {
            return new Vector2(vector.X, vector.Y);
        }

        private SerializedMotionConfig SerializeMotionConfig(ElementMotionConfig config)
        {
            return new SerializedMotionConfig
            {
                MotionType = config.MotionType.ToString(),
                Speed = config.Speed,
                Direction = SerializeVector2(config.Direction),
                Center = SerializeVector2(config.Center),
                Radius = config.Radius,
                RespectBoundaries = config.RespectBoundaries,
                ShowTrail = config.ShowTrail,
                TrailLength = config.TrailLength,
                IsPaused = config.IsPaused
            };
        }

        private ElementMotionConfig DeserializeMotionConfig(SerializedMotionConfig config)
        {
            Enum.TryParse<MotionType>(config.MotionType, out var motionType);

            return new ElementMotionConfig
            {
                MotionType = motionType,
                Speed = config.Speed,
                Direction = DeserializeVector2(config.Direction),
                Center = DeserializeVector2(config.Center),
                Radius = config.Radius,
                RespectBoundaries = config.RespectBoundaries,
                ShowTrail = config.ShowTrail,
                TrailLength = config.TrailLength,
                IsPaused = config.IsPaused
            };
        }


        #endregion // End of JSON Serialization Data Models
    }
}