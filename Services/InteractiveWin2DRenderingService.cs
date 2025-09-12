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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

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
        private WinUIColor _backgroundColor = WinUIColor.FromArgb(255, 30, 30, 60);
        private WinUIColor _backgroundGradientEndColor = WinUIColor.FromArgb(255, 10, 10, 30);
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
        public WinUIColor BackgroundColor => _backgroundColor;
        public WinUIColor BackgroundGradientEndColor => _backgroundGradientEndColor;
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

            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize InteractiveWin2DRenderingService: {ex.Message}", ex);
            }
        }

        #region Background Management

        /// <summary>
        /// Set a solid color background
        /// </summary>
        /// <param name="color">Background color</param>
        /// <param name="opacity">Background opacity (0.0 to 1.0)</param>
        public async Task SetBackgroundColorAsync(WinUIColor color, float opacity = 1.0f)
        {
            try
            {
                _backgroundType = BackgroundType.SolidColor;
                _backgroundColor = color;
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
        public async Task SetBackgroundGradientAsync(WinUIColor startColor, WinUIColor endColor, 
            BackgroundGradientDirection direction = BackgroundGradientDirection.TopToBottom, float opacity = 1.0f)
        {
            try
            {
                _backgroundType = BackgroundType.Gradient;
                _backgroundColor = startColor;
                _backgroundGradientEndColor = endColor;
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
        /// Set a background image with improved transparency handling
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <param name="scaleMode">How to scale the image</param>
        /// <param name="opacity">Background opacity (0.0 to 1.0)</param>
        /// <param name="fillTransparentAreas">Whether to fill transparent areas with a solid color for better opacity blending</param>
        /// <param name="fillColor">Color to use for transparent areas (default: black)</param>
        public async Task SetBackgroundImageWithTransparencyAsync(string imagePath,
            BackgroundScaleMode scaleMode = BackgroundScaleMode.Stretch,
            float opacity = 1.0f,
            bool fillTransparentAreas = false,
            WinUIColor? fillColor = null)
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

                // Load the image
                var originalImage = await CanvasBitmap.LoadAsync(_canvasDevice!, imagePath);

                if (fillTransparentAreas && opacity < 1.0f)
                {
                    // Create a new render target to pre-composite the image with a solid background
                    using (var tempTarget = new CanvasRenderTarget(_canvasDevice!, (int)originalImage.Size.Width, (int)originalImage.Size.Height, 96))
                    {
                        using (var tempSession = tempTarget.CreateDrawingSession())
                        {
                            // Fill with the specified color or default black
                            var bgColor = fillColor ?? WinUIColor.FromArgb(255, 0, 0, 0);
                            tempSession.Clear(bgColor);

                            // Draw the original image on top
                            tempSession.DrawImage(originalImage);
                        }

                        // Use the composited image as the background
                        _backgroundImage = CanvasBitmap.CreateFromBytes(_canvasDevice!, tempTarget.GetPixelBytes(),
                            (int)tempTarget.Size.Width, (int)tempTarget.Size.Height, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
                    }

                    originalImage.Dispose();
                }
                else
                {
                    _backgroundImage = originalImage;
                }

                _backgroundType = BackgroundType.Image;
                _backgroundScaleMode = scaleMode;
                _backgroundOpacity = Math.Clamp(opacity, 0.0f, 1.0f);
                _backgroundImagePath = imagePath;

                BackgroundChanged?.Invoke($"Background image set with transparency handling: {Path.GetFileName(imagePath)} (Scale: {scaleMode}, Opacity: {opacity:F2})");
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
                _backgroundImage = await CanvasBitmap.LoadAsync(_canvasDevice!, imagePath);
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

                // Create a temporary file to load the image
                var tempPath = Path.Combine(Path.GetTempPath(), $"bg_temp_{Guid.NewGuid():N}.tmp");
                await File.WriteAllBytesAsync(tempPath, imageData);

                try
                {
                    // Load background image from temporary file
                    _backgroundImage = await CanvasBitmap.LoadAsync(_canvasDevice!, tempPath);
                    _backgroundType = BackgroundType.Image;
                    _backgroundScaleMode = scaleMode;
                    _backgroundOpacity = Math.Clamp(opacity, 0.0f, 1.0f);
                    _backgroundImagePath = "<from byte array>";

                    BackgroundChanged?.Invoke($"Background image set from byte array ({imageData.Length:N0} bytes, Scale: {scaleMode})");
                }
                finally
                {
                    // Clean up temporary file
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch { /* Ignore cleanup errors */ }
                }
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
                WinUIColor.FromArgb(255, 30, 30, 60),
                WinUIColor.FromArgb(255, 10, 10, 30),
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
                TextColor = textConfig.TextColor,
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

                case MotionType.Oscillate:
                    if (config.Center == Vector2.Zero)
                    {
                        config.Center = new Vector2((float)element.Position.X, (float)element.Position.Y);
                    }
                    if (config.Direction == Vector2.Zero)
                    {
                        config.Direction = Vector2.UnitX; // Default horizontal oscillation
                    }
                    break;

                case MotionType.Random:
                    element.LastDirectionChange = DateTime.Now;
                    config.Direction = GetRandomDirection();
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
                case MotionType.Oscillate:
                    UpdateOscillateMotion(element, elapsedTime, config);
                    break;
                case MotionType.Spiral:
                    UpdateSpiralMotion(element, elapsedTime, config);
                    break;
                case MotionType.Random:
                    UpdateRandomMotion(element, deltaTime, config, currentTime);
                    break;
                case MotionType.Wave:
                    UpdateWaveMotion(element, elapsedTime, config);
                    break;
                case MotionType.Orbit:
                    UpdateOrbitMotion(element, elapsedTime, config);
                    break;
            }

            // Update trail if enabled
            if (config.ShowTrail && element.TrailPositions != null)
            {
                UpdateTrail(element);
            }
        }

        #endregion

        #region Motion Update Methods

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

        private void UpdateOscillateMotion(IMotionElement element, float elapsedTime, ElementMotionConfig config)
        {
            var offset = (float)Math.Sin(elapsedTime * config.Speed) * config.Radius;
            var newPos = config.Center + config.Direction * offset;
            element.Position = new Point(newPos.X, newPos.Y);
        }

        private void UpdateSpiralMotion(IMotionElement element, float elapsedTime, ElementMotionConfig config)
        {
            var angle = elapsedTime * config.Speed;
            element.CurrentAngle = angle;
            
            // Expand spiral radius over time
            var currentRadius = config.Radius + elapsedTime * 5f; // Grow by 5 units per second

            var x = config.Center.X + (float)Math.Cos(angle) * currentRadius;
            var y = config.Center.Y + (float)Math.Sin(angle) * currentRadius;

            element.Position = new Point(x, y);
        }

        private void UpdateRandomMotion(IMotionElement element, float deltaTime, ElementMotionConfig config, DateTime currentTime)
        {
            // Change direction periodically
            if ((currentTime - element.LastDirectionChange).TotalSeconds > 2.0) // Change direction every 2 seconds
            {
                config.Direction = GetRandomDirection();
                element.LastDirectionChange = currentTime;
            }

            var velocity = config.Direction * config.Speed * deltaTime;
            var currentPos = new Vector2((float)element.Position.X, (float)element.Position.Y);
            var newPosition = currentPos + velocity;

            if (config.RespectBoundaries)
            {
                // Bounce off boundaries and change direction
                if (newPosition.X <= 0 || newPosition.X >= Width || 
                    newPosition.Y <= 0 || newPosition.Y >= Height)
                {
                    config.Direction = GetRandomDirection();
                    element.LastDirectionChange = currentTime;
                }

                newPosition = Vector2.Clamp(newPosition, Vector2.Zero, new Vector2(Width, Height));
            }

            element.Position = new Point(newPosition.X, newPosition.Y);
        }

        private void UpdateWaveMotion(IMotionElement element, float elapsedTime, ElementMotionConfig config)
        {
            // Horizontal movement with vertical wave
            var x = element.OriginalPosition.X + config.Speed * elapsedTime * 20f; // Move horizontally
            var y = element.OriginalPosition.Y + (float)Math.Sin(elapsedTime * config.Speed * 3f) * config.Radius;

            element.Position = new Point(x, y);

            // Wrap around screen
            if (element.Position.X > Width)
            {
                element.Position = new Point(-50, element.Position.Y);
                element.OriginalPosition = new Point(-50, element.OriginalPosition.Y);
            }
        }

        private void UpdateOrbitMotion(IMotionElement element, float elapsedTime, ElementMotionConfig config)
        {
            // Similar to circular but with varying radius
            var angle = elapsedTime * config.Speed;
            element.CurrentAngle = angle;
            
            var radiusVariation = (float)Math.Sin(elapsedTime * 2f) * 10f; // �10 radius variation
            var currentRadius = config.Radius + radiusVariation;

            var x = config.Center.X + (float)Math.Cos(angle) * currentRadius;
            var y = config.Center.Y + (float)Math.Sin(angle) * currentRadius;

            element.Position = new Point(x, y);
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

        private Vector2 GetRandomDirection()
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            return new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
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
            switch (_backgroundType)
            {
                case BackgroundType.Transparent:
                    // Clear with transparent
                    session.Clear(WinUIColor.FromArgb(0, 0, 0, 0));
                    break;

                case BackgroundType.SolidColor:
                    var solidColor = WinUIColor.FromArgb(
                        (byte)(_backgroundColor.A * _backgroundOpacity),
                        _backgroundColor.R,
                        _backgroundColor.G,
                        _backgroundColor.B);
                    session.Clear(solidColor);
                    break;

                case BackgroundType.Gradient:
                    RenderGradientBackground(session);
                    break;

                case BackgroundType.Image:
                    RenderImageBackground(session);
                    break;

                default:
                    // Fallback to default gradient
                    RenderDefaultGradientBackground(session);
                    break;
            }
        }

        private void RenderGradientBackground(CanvasDrawingSession session)
        {
            try
            {
                Vector2 startPoint, endPoint;
                
                switch (_gradientDirection)
                {
                    case BackgroundGradientDirection.LeftToRight:
                        startPoint = new Vector2(0, Height / 2);
                        endPoint = new Vector2(Width, Height / 2);
                        break;
                    case BackgroundGradientDirection.TopToBottom:
                        startPoint = new Vector2(Width / 2, 0);
                        endPoint = new Vector2(Width / 2, Height);
                        break;
                    case BackgroundGradientDirection.DiagonalTopLeftToBottomRight:
                        startPoint = new Vector2(0, 0);
                        endPoint = new Vector2(Width, Height);
                        break;
                    case BackgroundGradientDirection.DiagonalTopRightToBottomLeft:
                        startPoint = new Vector2(Width, 0);
                        endPoint = new Vector2(0, Height);
                        break;
                    case BackgroundGradientDirection.RadialFromCenter:
                        RenderRadialGradientBackground(session);
                        return;
                    default:
                        startPoint = new Vector2(Width / 2, 0);
                        endPoint = new Vector2(Width / 2, Height);
                        break;
                }

                var startColor = WinUIColor.FromArgb(
                    (byte)(_backgroundColor.A * _backgroundOpacity),
                    _backgroundColor.R,
                    _backgroundColor.G,
                    _backgroundColor.B);

                var endColor = WinUIColor.FromArgb(
                    (byte)(_backgroundGradientEndColor.A * _backgroundOpacity),
                    _backgroundGradientEndColor.R,
                    _backgroundGradientEndColor.G,
                    _backgroundGradientEndColor.B);

                using (var gradientBrush = new CanvasLinearGradientBrush(_canvasDevice, startColor, endColor))
                {
                    gradientBrush.StartPoint = startPoint;
                    gradientBrush.EndPoint = endPoint;
                    session.FillRectangle(0, 0, Width, Height, gradientBrush);
                }
            }
            catch (Exception ex)
            {
                // Fallback to solid color
                var fallbackColor = WinUIColor.FromArgb(
                    (byte)(_backgroundColor.A * _backgroundOpacity),
                    _backgroundColor.R,
                    _backgroundColor.G,
                    _backgroundColor.B);
                session.Clear(fallbackColor);
                RenderingError?.Invoke(new InvalidOperationException($"Failed to render gradient background: {ex.Message}", ex));
            }
        }

        private void RenderRadialGradientBackground(CanvasDrawingSession session)
        {
            try
            {
                var center = new Vector2(Width / 2, Height / 2);
                var radius = Math.Max(Width, Height) / 2.0f;

                var startColor = WinUIColor.FromArgb(
                    (byte)(_backgroundColor.A * _backgroundOpacity),
                    _backgroundColor.R,
                    _backgroundColor.G,
                    _backgroundColor.B);

                var endColor = WinUIColor.FromArgb(
                    (byte)(_backgroundGradientEndColor.A * _backgroundOpacity),
                    _backgroundGradientEndColor.R,
                    _backgroundGradientEndColor.G,
                    _backgroundGradientEndColor.B);

                using (var gradientBrush = new CanvasRadialGradientBrush(_canvasDevice, startColor, endColor))
                {
                    gradientBrush.Center = center;
                    gradientBrush.RadiusX = radius;
                    gradientBrush.RadiusY = radius;
                    session.FillRectangle(0, 0, Width, Height, gradientBrush);
                }
            }
            catch (Exception ex)
            {
                // Fallback to linear gradient
                RenderGradientBackground(session);
                RenderingError?.Invoke(new InvalidOperationException($"Failed to render radial gradient background: {ex.Message}", ex));
            }
        }

        private void RenderImageBackground(CanvasDrawingSession session)
        {
            if (_backgroundImage == null)
            {
                RenderDefaultGradientBackground(session);
                return;
            }

            try
            {
                var destRect = CalculateImageDestinationRect();
                
                if (_backgroundOpacity < 1.0f)
                {
                    session.DrawImage(_backgroundImage, destRect, new Rect(0, 0, _backgroundImage.Size.Width, _backgroundImage.Size.Height), _backgroundOpacity);
                }
                else
                {
                    session.DrawImage(_backgroundImage, destRect);
                }
            }
            catch (Exception ex)
            {
                RenderDefaultGradientBackground(session);
                RenderingError?.Invoke(new InvalidOperationException($"Failed to render image background: {ex.Message}", ex));
            }
        }

        private Rect CalculateImageDestinationRect()
        {
            if (_backgroundImage == null)
                return new Rect(0, 0, Width, Height);

            var imageSize = _backgroundImage.Size;
            var canvasAspect = (double)Width / Height;
            var imageAspect = imageSize.Width / imageSize.Height;

            switch (_backgroundScaleMode)
            {
                case BackgroundScaleMode.None:
                    // Center the image at original size
                    var x = (Width - imageSize.Width) / 2;
                    var y = (Height - imageSize.Height) / 2;
                    return new Rect(x, y, imageSize.Width, imageSize.Height);

                case BackgroundScaleMode.Uniform:
                    // Scale to fit while maintaining aspect ratio
                    if (imageAspect > canvasAspect)
                    {
                        // Image is wider than canvas
                        var newHeight = Width / imageAspect;
                        var offsetY = (Height - newHeight) / 2;
                        return new Rect(0, offsetY, Width, newHeight);
                    }
                    else
                    {
                        // Image is taller than canvas
                        var newWidth = Height * imageAspect;
                        var offsetX = (Width - newWidth) / 2;
                        return new Rect(offsetX, 0, newWidth, Height);
                    }

                case BackgroundScaleMode.UniformToFill:
                    // Scale to fill while maintaining aspect ratio (may crop)
                    if (imageAspect > canvasAspect)
                    {
                        // Image is wider than canvas
                        var newWidth = Height * imageAspect;
                        var offsetX = (Width - newWidth) / 2;
                        return new Rect(offsetX, 0, newWidth, Height);
                    }
                    else
                    {
                        // Image is taller than canvas
                        var newHeight = Width / imageAspect;
                        var offsetY = (Height - newHeight) / 2;
                        return new Rect(0, offsetY, Width, newHeight);
                    }

                case BackgroundScaleMode.Stretch:
                default:
                    // Stretch to fill entire canvas
                    return new Rect(0, 0, Width, Height);
            }
        }

        private void RenderDefaultGradientBackground(CanvasDrawingSession session)
        {
            // Create default gradient background
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
                case MotionTextElement motionTextElement:
                    RenderMotionTextElement(session, motionTextElement);
                    break;
                case MotionShapeElement motionShapeElement:
                    RenderMotionShapeElement(session, motionShapeElement);
                    break;
                case MotionImageElement motionImageElement:
                    RenderMotionImageElement(session, motionImageElement);
                    break;
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

        #region Motion Element Rendering

        private void RenderMotionTextElement(CanvasDrawingSession session, MotionTextElement element)
        {
            // Draw trail first
            if (element.MotionConfig.ShowTrail && element.TrailPositions != null)
            {
                DrawTrail(session, element.TrailPositions, element.TextColor);
            }

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

        private void RenderMotionShapeElement(CanvasDrawingSession session, MotionShapeElement element)
        {
            // Draw trail first
            if (element.MotionConfig.ShowTrail && element.TrailPositions != null)
            {
                DrawTrail(session, element.TrailPositions, element.FillColor);
            }

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

        private void RenderMotionImageElement(CanvasDrawingSession session, MotionImageElement element)
        {
            // Draw trail first
            if (element.MotionConfig.ShowTrail && element.TrailPositions != null)
            {
                DrawTrail(session, element.TrailPositions, WinUIColor.FromArgb(255, 255, 255, 255));
            }

            if (_loadedImages.TryGetValue(element.ImagePath, out var bitmap))
            {
                var bounds = element.GetBounds();
                session.DrawImage(bitmap, bounds);
            }
        }

        private void DrawTrail(CanvasDrawingSession session, Queue<Vector2> trailPositions, WinUIColor baseColor)
        {
            if (trailPositions.Count < 2) return;

            var positions = trailPositions.ToArray();
            for (int i = 1; i < positions.Length; i++)
            {
                var alpha = (float)i / positions.Length; // Fade trail
                var trailColor = WinUIColor.FromArgb(
                    (byte)(baseColor.A * alpha * 0.3f), // Semi-transparent trail
                    baseColor.R, baseColor.G, baseColor.B);

                session.DrawLine(positions[i - 1], positions[i], trailColor, 2f);
            }
        }

        #endregion

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
        public async Task<string> ExportSceneToJsonAsync()
        {
            try
            {
                var sceneData = new SceneExportData
                {
                    SceneName = "Exported Scene",
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
                    BackgroundImagePath = _backgroundImagePath,
                    
                    // Render settings
                    TargetFPS = TargetFPS,
                    JpegQuality = JpegQuality,
                    
                    // Elements
                    Elements = await SerializeElementsAsync()
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

        private async Task<List<ElementExportData>> SerializeElementsAsync()
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
                        await SetBackgroundColorAsync(solidColor, sceneData.BackgroundOpacity);
                        break;

                    case BackgroundType.Gradient:
                        var startColor = DeserializeColor(sceneData.BackgroundColor);
                        var endColor = DeserializeColor(sceneData.BackgroundGradientEndColor);
                        await SetBackgroundGradientAsync(startColor, endColor, sceneData.GradientDirection, sceneData.BackgroundOpacity);
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

    #region Background Configuration Enums

    /// <summary>
    /// Background types supported by the rendering service
    /// </summary>
    public enum BackgroundType
    {
        Transparent,
        SolidColor,
        Gradient,
        Image
    }

    /// <summary>
    /// Background image scale modes
    /// </summary>
    public enum BackgroundScaleMode
    {
        None,           // Original size, centered
        Stretch,        // Stretch to fill canvas (may distort)
        Uniform,        // Scale to fit while maintaining aspect ratio
        UniformToFill   // Scale to fill while maintaining aspect ratio (may crop)
    }

    /// <summary>
    /// Gradient directions for background gradients
    /// </summary>
    public enum BackgroundGradientDirection
    {
        TopToBottom,
        LeftToRight,
        DiagonalTopLeftToBottomRight,
        DiagonalTopRightToBottomLeft,
        RadialFromCenter
    }

    #endregion

    #region Motion Configuration and Element Classes

    /// <summary>
    /// Configuration for element motion
    /// </summary>
    public class ElementMotionConfig
    {
        public MotionType MotionType { get; set; } = MotionType.None;
        public float Speed { get; set; } = 100.0f; // pixels per second or radians per second
        public Vector2 Direction { get; set; } = Vector2.Zero;
        public Vector2 Center { get; set; } = Vector2.Zero;
        public float Radius { get; set; } = 50.0f;
        public bool RespectBoundaries { get; set; } = false;
        public bool ShowTrail { get; set; } = false;
        public int TrailLength { get; set; } = 20;
        public bool IsPaused { get; set; } = false;
    }

    /// <summary>
    /// Configuration for text elements
    /// </summary>
    public class TextElementConfig
    {
        public Size Size { get; set; } = new Size(200, 50);
        public float FontSize { get; set; } = 16;
        public string FontFamily { get; set; } = "Segoe UI";
        public WinUIColor TextColor { get; set; } = WinUIColor.FromArgb(255, 255, 255, 255);
        public bool IsDraggable { get; set; } = true;
    }

    /// <summary>
    /// Configuration for image elements
    /// </summary>
    public class ImageElementConfig
    {
        public Size Size { get; set; } = new Size(100, 100);
        public float Scale { get; set; } = 1.0f;
        public float Rotation { get; set; } = 0.0f;
        public bool IsDraggable { get; set; } = true;
    }

    /// <summary>
    /// Interface for motion-enabled elements
    /// </summary>
    public interface IMotionElement
    {
        Point Position { get; set; }
        Point OriginalPosition { get; set; }
        ElementMotionConfig MotionConfig { get; set; }
        DateTime StartTime { get; set; }
        DateTime LastUpdateTime { get; set; }
        DateTime LastDirectionChange { get; set; }
        float CurrentAngle { get; set; }
        Queue<Vector2>? TrailPositions { get; set; }
    }

    /// <summary>
    /// Text element with motion capabilities
    /// </summary>
    public class MotionTextElement : TextElement, IMotionElement
    {
        public Point OriginalPosition { get; set; }
        public ElementMotionConfig MotionConfig { get; set; } = new ElementMotionConfig();
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime LastUpdateTime { get; set; } = DateTime.Now;
        public DateTime LastDirectionChange { get; set; } = DateTime.Now;
        public float CurrentAngle { get; set; }
        public Queue<Vector2>? TrailPositions { get; set; }

        public MotionTextElement(string name) : base(name) { }
    }

    /// <summary>
    /// Shape element with motion capabilities
    /// </summary>
    public class MotionShapeElement : ShapeElement, IMotionElement
    {
        public Point OriginalPosition { get; set; }
        public ElementMotionConfig MotionConfig { get; set; } = new ElementMotionConfig();
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime LastUpdateTime { get; set; } = DateTime.Now;
        public DateTime LastDirectionChange { get; set; } = DateTime.Now;
        public float CurrentAngle { get; set; }
        public Queue<Vector2>? TrailPositions { get; set; }

        public MotionShapeElement(string name) : base(name) { }
    }

    /// <summary>
    /// Image element with motion capabilities
    /// </summary>
    public class MotionImageElement : ImageElement, IMotionElement
    {
        public Point OriginalPosition { get; set; }
        public ElementMotionConfig MotionConfig { get; set; } = new ElementMotionConfig();
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime LastUpdateTime { get; set; } = DateTime.Now;
        public DateTime LastDirectionChange { get; set; } = DateTime.Now;
        public float CurrentAngle { get; set; }
        public Queue<Vector2>? TrailPositions { get; set; }

        public MotionImageElement(string name) : base(name) { }
    }

    /// <summary>
    /// Motion types for elements
    /// </summary>
    public enum MotionType
    {
        None,
        Linear,     // Straight-line movement
        Circular,   // Circular rotation
        Bounce,     // Physics-based bouncing
        Oscillate,  // Back-and-forth oscillation
        Spiral,     // Expanding circular motion
        Random,     // Random direction changes
        Wave,       // Horizontal with vertical wave
        Orbit       // Circular with radius variation
    }

    #endregion

    #region JSON Serialization Data Models

    /// <summary>
    /// Complete scene export data structure
    /// </summary>
    public class SceneExportData
    {
        public string SceneName { get; set; } = "";
        public DateTime ExportDate { get; set; }
        public string Version { get; set; } = "1.0";
        public int CanvasWidth { get; set; }
        public int CanvasHeight { get; set; }
        
        // Display options
        public bool ShowTime { get; set; }
        public bool ShowDate { get; set; }
        public bool ShowSystemInfo { get; set; }
        public bool ShowAnimation { get; set; }
        
        // Background configuration
        public BackgroundType BackgroundType { get; set; }
        public float BackgroundOpacity { get; set; }
        public SerializedColor BackgroundColor { get; set; } = new SerializedColor();
        public SerializedColor BackgroundGradientEndColor { get; set; } = new SerializedColor();
        public BackgroundScaleMode BackgroundScaleMode { get; set; }
        public BackgroundGradientDirection GradientDirection { get; set; }
        public string? BackgroundImagePath { get; set; }
        
        // Render settings
        public int TargetFPS { get; set; }
        public int JpegQuality { get; set; }
        
        // Elements
        public List<ElementExportData> Elements { get; set; } = new List<ElementExportData>();
    }

    /// <summary>
    /// Element export data structure
    /// </summary>
    public class ElementExportData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public SerializedPoint Position { get; set; } = new SerializedPoint();
        public bool IsVisible { get; set; } = true;
        public bool IsDraggable { get; set; } = true;
        public float ZIndex { get; set; } = 0;
        
        // Element-specific data (only one will be populated per element)
        public TextElementData? TextData { get; set; }
        public ImageElementData? ImageData { get; set; }
        public ShapeElementData? ShapeData { get; set; }
        public LiveElementData? LiveData { get; set; }
        
        // Motion data (optional)
        public MotionElementData? MotionData { get; set; }
    }

    /// <summary>
    /// Text element specific data
    /// </summary>
    public class TextElementData
    {
        public string Text { get; set; } = "";
        public float FontSize { get; set; } = 16;
        public string FontFamily { get; set; } = "Segoe UI";
        public SerializedColor TextColor { get; set; } = new SerializedColor();
        public SerializedSize Size { get; set; } = new SerializedSize();
    }

    /// <summary>
    /// Image element specific data
    /// </summary>
    public class ImageElementData
    {
        public string ImagePath { get; set; } = "";
        public SerializedSize Size { get; set; } = new SerializedSize();
        public float Scale { get; set; } = 1.0f;
        public float Rotation { get; set; } = 0.0f;
    }

    /// <summary>
    /// Shape element specific data
    /// </summary>
    public class ShapeElementData
    {
        public string ShapeType { get; set; } = "Rectangle";
        public SerializedSize Size { get; set; } = new SerializedSize();
        public SerializedColor FillColor { get; set; } = new SerializedColor();
        public SerializedColor StrokeColor { get; set; } = new SerializedColor();
        public float StrokeWidth { get; set; } = 1.0f;
    }

    /// <summary>
    /// Live element specific data
    /// </summary>
    public class LiveElementData
    {
        public string Format { get; set; } = "";
        public float FontSize { get; set; } = 16;
        public string FontFamily { get; set; } = "Segoe UI";
        public SerializedColor TextColor { get; set; } = new SerializedColor();
        public SerializedSize Size { get; set; } = new SerializedSize();
    }

    /// <summary>
    /// Motion element data
    /// </summary>
    public class MotionElementData
    {
        public SerializedPoint OriginalPosition { get; set; } = new SerializedPoint();
        public SerializedMotionConfig MotionConfig { get; set; } = new SerializedMotionConfig();
        public DateTime StartTime { get; set; }
        public float CurrentAngle { get; set; }
    }

    /// <summary>
    /// Serialized motion configuration
    /// </summary>
    public class SerializedMotionConfig
    {
        public string MotionType { get; set; } = "None";
        public float Speed { get; set; } = 100.0f;
        public SerializedVector2 Direction { get; set; } = new SerializedVector2();
        public SerializedVector2 Center { get; set; } = new SerializedVector2();
        public float Radius { get; set; } = 50.0f;
        public bool RespectBoundaries { get; set; } = false;
        public bool ShowTrail { get; set; } = false;
        public int TrailLength { get; set; } = 20;
        public bool IsPaused { get; set; } = false;
    }

    /// <summary>
    /// Serializable color structure
    /// </summary>
    public class SerializedColor
    {
        public byte A { get; set; } = 255;
        public byte R { get; set; } = 0;
        public byte G { get; set; } = 0;
        public byte B { get; set; } = 0;
    }

    /// <summary>
    /// Serializable point structure
    /// </summary>
    public class SerializedPoint
    {
        public double X { get; set; } = 0;
        public double Y { get; set; } = 0;
    }

    /// <summary>
    /// Serializable size structure
    /// </summary>
    public class SerializedSize
    {
        public double Width { get; set; } = 0;
        public double Height { get; set; } = 0;
    }

    /// <summary>
    /// Serializable Vector2 structure
    /// </summary>
    public class SerializedVector2
    {
        public float X { get; set; } = 0;
        public float Y { get; set; } = 0;
    }

    #endregion // End of JSON Serialization Data Models
}