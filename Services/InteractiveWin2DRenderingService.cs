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
            
            var radiusVariation = (float)Math.Sin(elapsedTime * 2f) * 10f; // ±10 radius variation
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
    }

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
}