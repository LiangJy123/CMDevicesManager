using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DevWinUIGallery.Services;

public class AdvancedBackgroundRenderer
{
    private CanvasControl _canvasControl;
    private readonly List<AnimatedElement> _elements = new();
    private readonly DispatcherTimer _animationTimer;
    private DateTime _startTime;
    private CanvasBitmap _backgroundImage;
    private bool _isInitialized;
    private readonly Random _random = new Random();

    public event EventHandler<CanvasControl> CanvasReady;
    public event EventHandler<SoftwareBitmap> FrameCaptured;

    public float GlobalOpacity { get; set; } = 1.0f;
    public Vector2 GlobalOffset { get; set; } = Vector2.Zero;
    public float GlobalScale { get; set; } = 1.0f;
    public float GlobalRotation { get; set; } = 0.0f;

    public int ElementCount => _elements.Count;
    public float CanvasWidth { get; private set; } = 800f;
    public float CanvasHeight { get; private set; } = 600f;

    public AdvancedBackgroundRenderer()
    {
        _animationTimer = new DispatcherTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(16); // 60 FPS
        _animationTimer.Tick += OnAnimationTick;
        _startTime = DateTime.Now;
    }

    public void Initialize(CanvasControl canvasControl)
    {
        _canvasControl = canvasControl;
        _canvasControl.CreateResources += OnCreateResources;
        _canvasControl.Draw += OnDraw;

        CanvasWidth = (float)_canvasControl.Size.Width;
        CanvasHeight = (float)_canvasControl.Size.Height;

        _isInitialized = true;
        CanvasReady?.Invoke(this, _canvasControl);
    }

    public async Task LoadBackgroundImageAsync(StorageFile file)
    {
        if (_canvasControl != null && file != null)
        {
            _backgroundImage = await CanvasBitmap.LoadAsync(_canvasControl, file.Path);
        }
    }

    public void SetBackgroundImage(CanvasBitmap bitmap)
    {
        _backgroundImage = bitmap;
    }

    public void StartAnimation()
    {
        if (_isInitialized)
        {
            _startTime = DateTime.Now;
            _animationTimer.Start();
        }
    }

    public void StopAnimation()
    {
        _animationTimer.Stop();
    }

    #region Enhanced Element Creation Methods

    public int AddLiveTextElement(string text, Vector2 position, LiveTextConfig config = null)
    {
        config ??= new LiveTextConfig();

        var element = new LiveTextElement
        {
            BaseText = text,
            Position = position,
            Config = config,
            StartTime = DateTime.Now,
            IsVisible = true
        };

        _elements.Add(element);
        return _elements.Count - 1;
    }

    public int AddImageElement(CanvasBitmap image, Vector2 position, ImageConfig config = null)
    {
        config ??= new ImageConfig();

        var element = new AnimatedImageElement
        {
            Image = image,
            Position = position,
            Config = config,
            StartTime = DateTime.Now,
            IsVisible = true
        };

        _elements.Add(element);
        return _elements.Count - 1;
    }

    public int AddParticleSystem(Vector2 position, ParticleConfig config = null)
    {
        config ??= new ParticleConfig();

        var element = new ParticleSystemElement
        {
            Position = position,
            Config = config,
            Particles = new List<Particle>(),
            StartTime = DateTime.Now,
            IsVisible = true
        };

        // Initialize particles
        for (int i = 0; i < config.MaxParticles; i++)
        {
            element.Particles.Add(CreateParticle(position, config));
        }

        _elements.Add(element);
        return _elements.Count - 1;
    }

    // New methods for adding elements with advanced motion

    public int AddTextElementWithMotion(string text, Vector2 position, ElementMotionConfig motionConfig = null, LiveTextConfig textConfig = null)
    {
        textConfig ??= new LiveTextConfig();
        motionConfig ??= new ElementMotionConfig();

        var element = new MotionTextElement
        {
            BaseText = text,
            Position = position,
            OriginalPosition = position,
            Config = textConfig,
            MotionConfig = motionConfig,
            StartTime = DateTime.Now,
            IsVisible = true
        };

        // Initialize motion-specific properties
        InitializeMotionElement(element, motionConfig);

        _elements.Add(element);
        return _elements.Count - 1;
    }

    public int AddCircleElementWithMotion(Vector2 position, float radius, Windows.UI.Color color, ElementMotionConfig motionConfig = null)
    {
        motionConfig ??= new ElementMotionConfig();

        var element = new MotionShapeElement
        {
            Position = position,
            OriginalPosition = position,
            ShapeType = ShapeType.Circle,
            Radius = radius,
            Color = color,
            MotionConfig = motionConfig,
            StartTime = DateTime.Now,
            IsVisible = true
        };

        InitializeMotionElement(element, motionConfig);

        _elements.Add(element);
        return _elements.Count - 1;
    }

    public int AddRectangleElementWithMotion(Vector2 position, Vector2 size, Windows.UI.Color color, ElementMotionConfig motionConfig = null)
    {
        motionConfig ??= new ElementMotionConfig();

        var element = new MotionShapeElement
        {
            Position = position,
            OriginalPosition = position,
            ShapeType = ShapeType.Rectangle,
            Size = size,
            Color = color,
            MotionConfig = motionConfig,
            StartTime = DateTime.Now,
            IsVisible = true
        };

        InitializeMotionElement(element, motionConfig);

        _elements.Add(element);
        return _elements.Count - 1;
    }

    public int AddImageElementWithMotion(CanvasBitmap image, Vector2 position, ElementMotionConfig motionConfig = null, ImageConfig imageConfig = null)
    {
        imageConfig ??= new ImageConfig();
        motionConfig ??= new ElementMotionConfig();

        var element = new MotionImageElement
        {
            Image = image,
            Position = position,
            OriginalPosition = position,
            Config = imageConfig,
            MotionConfig = motionConfig,
            StartTime = DateTime.Now,
            IsVisible = true
        };

        InitializeMotionElement(element, motionConfig);

        _elements.Add(element);
        return _elements.Count - 1;
    }

    private void InitializeMotionElement(IMotionElement element, ElementMotionConfig config)
    {
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
                    config.Center = element.Position;
                }
                element.CurrentAngle = 0f;
                break;

            case MotionType.Oscillate:
                if (config.Center == Vector2.Zero)
                {
                    config.Center = element.Position;
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

    #endregion

    #region Motion Management

    public void SetElementMotion(int index, ElementMotionConfig motionConfig)
    {
        if (index >= 0 && index < _elements.Count && _elements[index] is IMotionElement motionElement)
        {
            motionElement.MotionConfig = motionConfig;
            InitializeMotionElement(motionElement, motionConfig);
        }
    }

    public void PauseElementMotion(int index)
    {
        if (index >= 0 && index < _elements.Count && _elements[index] is IMotionElement motionElement)
        {
            motionElement.MotionConfig.IsPaused = true;
        }
    }

    public void ResumeElementMotion(int index)
    {
        if (index >= 0 && index < _elements.Count && _elements[index] is IMotionElement motionElement)
        {
            motionElement.MotionConfig.IsPaused = false;
        }
    }

    public void StopAllMotion()
    {
        foreach (var element in _elements)
        {
            if (element is IMotionElement motionElement)
            {
                motionElement.MotionConfig.IsPaused = true;
            }
        }
    }

    public void ResumeAllMotion()
    {
        foreach (var element in _elements)
        {
            if (element is IMotionElement motionElement)
            {
                motionElement.MotionConfig.IsPaused = false;
            }
        }
    }

    #endregion

    #region Element Management

    public void UpdateElementPosition(int index, Vector2 newPosition)
    {
        if (index >= 0 && index < _elements.Count)
        {
            _elements[index].Position = newPosition;

            // Update original position for motion elements
            if (_elements[index] is IMotionElement motionElement)
            {
                motionElement.OriginalPosition = newPosition;
            }
        }
    }

    public void SetElementVisibility(int index, bool isVisible)
    {
        if (index >= 0 && index < _elements.Count)
        {
            _elements[index].IsVisible = isVisible;
        }
    }

    public void RemoveElement(int index)
    {
        if (index >= 0 && index < _elements.Count)
        {
            _elements.RemoveAt(index);
        }
    }

    public void ClearElements()
    {
        _elements.Clear();
    }

    #endregion

    public async Task<SoftwareBitmap> CaptureFrameAsync()
    {
        if (_canvasControl == null) return null;

        var renderTarget = new CanvasRenderTarget(_canvasControl,
            (float)_canvasControl.Size.Width,
            (float)_canvasControl.Size.Height, 96);

        using (var session = renderTarget.CreateDrawingSession())
        {
            DrawFrame(session, DateTime.Now);
        }

        var pixels = renderTarget.GetPixelBytes();
        var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            (int)renderTarget.SizeInPixels.Width,
            (int)renderTarget.SizeInPixels.Height,
            BitmapAlphaMode.Premultiplied);

        FrameCaptured?.Invoke(this, softwareBitmap);
        return softwareBitmap;
    }

    private void OnCreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
        // Resources will be created when needed
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        DrawFrame(args.DrawingSession, DateTime.Now);
    }

    private void DrawFrame(CanvasDrawingSession session, DateTime currentTime)
    {
        // Apply global transforms
        var transform = Matrix3x2.CreateScale(GlobalScale) *
                       Matrix3x2.CreateRotation(GlobalRotation) *
                       Matrix3x2.CreateTranslation(GlobalOffset);
        session.Transform = transform;

        // Clear with background
        session.Clear(Colors.Transparent);

        // Draw background image if available
        if (_backgroundImage != null)
        {
            var canvasSize = _canvasControl.Size;
            session.DrawImage(_backgroundImage, new Windows.Foundation.Rect(0, 0, canvasSize.Width, canvasSize.Height));
        }

        // Update and draw all elements
        foreach (var element in _elements)
        {
            if (!element.IsVisible) continue;

            // Update motion for motion elements
            if (element is IMotionElement motionElement)
            {
                UpdateMotionElement(motionElement, currentTime);
            }

            // Draw the element
            switch (element)
            {
                case MotionTextElement motionTextElement:
                    DrawMotionText(session, motionTextElement, currentTime);
                    break;
                case MotionShapeElement motionShapeElement:
                    DrawMotionShape(session, motionShapeElement, currentTime);
                    break;
                case MotionImageElement motionImageElement:
                    DrawMotionImage(session, motionImageElement, currentTime);
                    break;
                case LiveTextElement textElement:
                    DrawLiveText(session, textElement, currentTime);
                    break;
                case AnimatedImageElement imageElement:
                    DrawAnimatedImage(session, imageElement, currentTime);
                    break;
                case ParticleSystemElement particleElement:
                    DrawParticleSystem(session, particleElement, currentTime);
                    break;
            }
        }
    }

    #region Motion Update Logic

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

    private void UpdateLinearMotion(IMotionElement element, float deltaTime, ElementMotionConfig config)
    {
        var velocity = config.Direction * config.Speed * deltaTime;
        var newPosition = element.Position + velocity;

        if (config.RespectBoundaries)
        {
            // Bounce off boundaries
            if (newPosition.X <= 0 || newPosition.X >= CanvasWidth)
            {
                config.Direction = new Vector2(-config.Direction.X, config.Direction.Y);
            }
            if (newPosition.Y <= 0 || newPosition.Y >= CanvasHeight)
            {
                config.Direction = new Vector2(config.Direction.X, -config.Direction.Y);
            }

            newPosition = Vector2.Clamp(newPosition, Vector2.Zero, new Vector2(CanvasWidth, CanvasHeight));
        }

        element.Position = newPosition;
    }

    private void UpdateCircularMotion(IMotionElement element, float elapsedTime, ElementMotionConfig config)
    {
        var angle = elapsedTime * config.Speed;
        element.CurrentAngle = angle;

        var x = config.Center.X + (float)Math.Cos(angle) * config.Radius;
        var y = config.Center.Y + (float)Math.Sin(angle) * config.Radius;

        element.Position = new Vector2(x, y);
    }

    private void UpdateBounceMotion(IMotionElement element, float deltaTime, ElementMotionConfig config)
    {
        var velocity = config.Direction * config.Speed * deltaTime;
        var newPosition = element.Position + velocity;

        // Check boundaries and bounce
        var bounced = false;
        if (newPosition.X <= 0 || newPosition.X >= CanvasWidth)
        {
            config.Direction = new Vector2(-config.Direction.X, config.Direction.Y);
            bounced = true;
        }
        if (newPosition.Y <= 0 || newPosition.Y >= CanvasHeight)
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

        element.Position = Vector2.Clamp(newPosition, Vector2.Zero, new Vector2(CanvasWidth, CanvasHeight));
    }

    private void UpdateOscillateMotion(IMotionElement element, float elapsedTime, ElementMotionConfig config)
    {
        var offset = (float)Math.Sin(elapsedTime * config.Speed) * config.Radius;
        element.Position = config.Center + config.Direction * offset;
    }

    private void UpdateSpiralMotion(IMotionElement element, float elapsedTime, ElementMotionConfig config)
    {
        var angle = elapsedTime * config.Speed;
        element.CurrentAngle = angle;

        // Expand spiral radius over time
        var currentRadius = config.Radius + elapsedTime * 5f; // Grow by 5 units per second

        var x = config.Center.X + (float)Math.Cos(angle) * currentRadius;
        var y = config.Center.Y + (float)Math.Sin(angle) * currentRadius;

        element.Position = new Vector2(x, y);
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
        var newPosition = element.Position + velocity;

        if (config.RespectBoundaries)
        {
            // Bounce off boundaries and change direction
            if (newPosition.X <= 0 || newPosition.X >= CanvasWidth ||
                newPosition.Y <= 0 || newPosition.Y >= CanvasHeight)
            {
                config.Direction = GetRandomDirection();
                element.LastDirectionChange = currentTime;
            }

            newPosition = Vector2.Clamp(newPosition, Vector2.Zero, new Vector2(CanvasWidth, CanvasHeight));
        }

        element.Position = newPosition;
    }

    private void UpdateWaveMotion(IMotionElement element, float elapsedTime, ElementMotionConfig config)
    {
        // Horizontal movement with vertical wave
        var x = element.OriginalPosition.X + config.Speed * elapsedTime * 20f; // Move horizontally
        var y = element.OriginalPosition.Y + (float)Math.Sin(elapsedTime * config.Speed * 3f) * config.Radius;

        element.Position = new Vector2(x, y);

        // Wrap around screen
        if (element.Position.X > CanvasWidth)
        {
            element.Position = new Vector2(-50, element.Position.Y);
            element.OriginalPosition = new Vector2(-50, element.OriginalPosition.Y);
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

        element.Position = new Vector2(x, y);
    }

    private void UpdateTrail(IMotionElement element)
    {
        if (element.TrailPositions == null) return;

        element.TrailPositions.Enqueue(element.Position);

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

    #region Drawing Methods for Motion Elements

    private void DrawMotionText(CanvasDrawingSession session, MotionTextElement element, DateTime currentTime)
    {
        // Draw trail first
        if (element.MotionConfig.ShowTrail && element.TrailPositions != null)
        {
            DrawTrail(session, element.TrailPositions, element.Config.TextColor);
        }

        // Use the same drawing logic as LiveTextElement but at the current position
        var config = element.Config;
        var elapsedTime = (currentTime - element.StartTime).TotalSeconds;

        // Update text content with live data
        var text = element.BaseText
            .Replace("{TIME}", DateTime.Now.ToString(config.TimeFormat))
            .Replace("{DATE}", DateTime.Now.ToString(config.DateFormat))
            .Replace("{COUNTER}", ((int)elapsedTime).ToString());

        // Calculate opacity with breathing effect
        var opacity = config.BaseOpacity;
        if (config.EnableBreathing && !element.MotionConfig.IsPaused)
        {
            opacity *= (float)(0.5 + 0.5 * Math.Sin(elapsedTime * config.BreathingSpeed));
        }

        // Create text format
        var textFormat = new CanvasTextFormat
        {
            FontSize = config.FontSize,
            FontFamily = config.FontFamily,
            FontWeight = config.FontWeight,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        // Apply effects
        if (config.EnableGlow)
        {
            DrawTextWithGlow(session, text, element.Position, config.TextColor, opacity, textFormat, config.GlowRadius, config.GlowIntensity);
        }
        else
        {
            var color = Windows.UI.Color.FromArgb(
                (byte)(config.TextColor.A * opacity * GlobalOpacity),
                config.TextColor.R,
                config.TextColor.G,
                config.TextColor.B);
            session.DrawText(text, element.Position, color, textFormat);
        }
    }

    private void DrawMotionShape(CanvasDrawingSession session, MotionShapeElement element, DateTime currentTime)
    {
        // Draw trail first
        if (element.MotionConfig.ShowTrail && element.TrailPositions != null)
        {
            DrawTrail(session, element.TrailPositions, element.Color);
        }

        var color = Windows.UI.Color.FromArgb(
            (byte)(element.Color.A * GlobalOpacity),
            element.Color.R,
            element.Color.G,
            element.Color.B);

        switch (element.ShapeType)
        {
            case ShapeType.Circle:
                session.FillCircle(element.Position, element.Radius, color);
                break;
            case ShapeType.Rectangle:
                var rect = new Windows.Foundation.Rect(
                    element.Position.X - element.Size.X / 2,
                    element.Position.Y - element.Size.Y / 2,
                    element.Size.X, element.Size.Y);
                session.FillRectangle(rect, color);
                break;
        }
    }

    private void DrawMotionImage(CanvasDrawingSession session, MotionImageElement element, DateTime currentTime)
    {
        if (element.Image == null) return;

        // Draw trail first
        if (element.MotionConfig.ShowTrail && element.TrailPositions != null)
        {
            DrawTrail(session, element.TrailPositions, Windows.UI.Colors.White);
        }

        var config = element.Config;
        var elapsedTime = (currentTime - element.StartTime).TotalSeconds;

        // Calculate rotation
        var rotation = config.BaseRotation + (float)(elapsedTime * config.RotationSpeed);

        // Calculate scale
        var scale = config.BaseScale;
        if (config.EnablePulsing && !element.MotionConfig.IsPaused)
        {
            scale *= (float)(0.8 + 0.2 * Math.Sin(elapsedTime * config.PulseSpeed));
        }

        // Calculate opacity
        var opacity = config.BaseOpacity * GlobalOpacity;

        // Apply transforms
        var centerX = element.Position.X + config.Size.X / 2;
        var centerY = element.Position.Y + config.Size.Y / 2;
        var oldTransform = session.Transform;

        session.Transform = Matrix3x2.CreateScale(scale, new Vector2(centerX, centerY)) *
                           Matrix3x2.CreateRotation(rotation, new Vector2(centerX, centerY)) *
                           oldTransform;

        // Draw image
        var destRect = new Windows.Foundation.Rect(element.Position.X, element.Position.Y, config.Size.X, config.Size.Y);
        session.DrawImage(element.Image, destRect, new Windows.Foundation.Rect(0, 0, element.Image.SizeInPixels.Width, element.Image.SizeInPixels.Height), opacity);

        session.Transform = oldTransform;
    }

    private void DrawTrail(CanvasDrawingSession session, Queue<Vector2> trailPositions, Windows.UI.Color baseColor)
    {
        if (trailPositions.Count < 2) return;

        var positions = trailPositions.ToArray();
        for (int i = 1; i < positions.Length; i++)
        {
            var alpha = (float)i / positions.Length; // Fade trail
            var trailColor = Windows.UI.Color.FromArgb(
                (byte)(baseColor.A * alpha * 0.3f), // Semi-transparent trail
                baseColor.R, baseColor.G, baseColor.B);

            session.DrawLine(positions[i - 1], positions[i], trailColor, 2f);
        }
    }

    #endregion

    private void DrawLiveText(CanvasDrawingSession session, LiveTextElement element, DateTime currentTime)
    {
        var config = element.Config;
        var elapsedTime = (currentTime - element.StartTime).TotalSeconds;

        // Update text content with live data
        var text = element.BaseText
            .Replace("{TIME}", DateTime.Now.ToString(config.TimeFormat))
            .Replace("{DATE}", DateTime.Now.ToString(config.DateFormat))
            .Replace("{COUNTER}", ((int)elapsedTime).ToString());

        // Calculate animated position
        var animatedPosition = element.Position;
        if (config.MovementType != MovementType.None)
        {
            animatedPosition += CalculateMovement(config.MovementType, elapsedTime, config.MovementSpeed, config.MovementAmplitude);
        }

        // Calculate opacity with breathing effect
        var opacity = config.BaseOpacity;
        if (config.EnableBreathing)
        {
            opacity *= (float)(0.5 + 0.5 * Math.Sin(elapsedTime * config.BreathingSpeed));
        }

        // Create text format
        var textFormat = new CanvasTextFormat
        {
            FontSize = config.FontSize,
            FontFamily = config.FontFamily,
            FontWeight = config.FontWeight,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        // Apply effects
        if (config.EnableGlow)
        {
            DrawTextWithGlow(session, text, animatedPosition, config.TextColor, opacity, textFormat, config.GlowRadius, config.GlowIntensity);
        }
        else
        {
            var color = Windows.UI.Color.FromArgb(
                (byte)(config.TextColor.A * opacity * GlobalOpacity),
                config.TextColor.R,
                config.TextColor.G,
                config.TextColor.B);
            session.DrawText(text, animatedPosition, color, textFormat);
        }
    }

    private void DrawAnimatedImage(CanvasDrawingSession session, AnimatedImageElement element, DateTime currentTime)
    {
        if (element.Image == null) return;

        var config = element.Config;
        var elapsedTime = (currentTime - element.StartTime).TotalSeconds;

        // Calculate animated position
        var animatedPosition = element.Position;
        if (config.MovementType != MovementType.None)
        {
            animatedPosition += CalculateMovement(config.MovementType, elapsedTime, config.MovementSpeed, config.MovementAmplitude);
        }

        // Calculate rotation
        var rotation = config.BaseRotation + (float)(elapsedTime * config.RotationSpeed);

        // Calculate scale
        var scale = config.BaseScale;
        if (config.EnablePulsing)
        {
            scale *= (float)(0.8 + 0.2 * Math.Sin(elapsedTime * config.PulseSpeed));
        }

        // Calculate opacity
        var opacity = config.BaseOpacity * GlobalOpacity;

        // Apply transforms
        var centerX = animatedPosition.X + config.Size.X / 2;
        var centerY = animatedPosition.Y + config.Size.Y / 2;
        var oldTransform = session.Transform;

        session.Transform = Matrix3x2.CreateScale(scale, new Vector2(centerX, centerY)) *
                           Matrix3x2.CreateRotation(rotation, new Vector2(centerX, centerY)) *
                           oldTransform;

        // Draw image
        var destRect = new Windows.Foundation.Rect(animatedPosition.X, animatedPosition.Y, config.Size.X, config.Size.Y);
        session.DrawImage(element.Image, destRect, new Windows.Foundation.Rect(0, 0, element.Image.SizeInPixels.Width, element.Image.SizeInPixels.Height), opacity);

        session.Transform = oldTransform;
    }

    private void DrawParticleSystem(CanvasDrawingSession session, ParticleSystemElement element, DateTime currentTime)
    {
        var config = element.Config;
        var elapsedTime = (float)(currentTime - element.StartTime).TotalSeconds;

        // Update particles
        UpdateParticles(element, elapsedTime);

        // Draw particles
        foreach (var particle in element.Particles)
        {
            if (particle.Life <= 0) continue;

            var opacity = particle.Life / config.ParticleLifetime;
            var color = Windows.UI.Color.FromArgb(
                (byte)(config.ParticleColor.A * opacity * GlobalOpacity),
                config.ParticleColor.R,
                config.ParticleColor.G,
                config.ParticleColor.B);

            session.FillCircle(particle.Position, particle.Size, color);
        }
    }

    private void DrawTextWithGlow(CanvasDrawingSession session, string text, Vector2 position,
        Windows.UI.Color color, float opacity, CanvasTextFormat textFormat, float glowRadius, float glowIntensity)
    {
        // Draw glow effect by drawing multiple copies with slight offsets
        for (int i = 1; i <= glowRadius; i++)
        {
            var glowOpacity = (glowIntensity / glowRadius) * (glowRadius - i + 1) / glowRadius * opacity;
            var glowColor = Windows.UI.Color.FromArgb(
                (byte)(color.A * glowOpacity * GlobalOpacity),
                color.R, color.G, color.B);

            for (int angle = 0; angle < 360; angle += 45)
            {
                var offsetX = (float)(Math.Cos(angle * Math.PI / 180) * i);
                var offsetY = (float)(Math.Sin(angle * Math.PI / 180) * i);
                session.DrawText(text, position + new Vector2(offsetX, offsetY), glowColor, textFormat);
            }
        }

        // Draw main text
        var mainColor = Windows.UI.Color.FromArgb(
            (byte)(color.A * opacity * GlobalOpacity),
            color.R, color.G, color.B);
        session.DrawText(text, position, mainColor, textFormat);
    }

    private Vector2 CalculateMovement(MovementType type, double elapsedTime, float speed, float amplitude)
    {
        switch (type)
        {
            case MovementType.Horizontal:
                return new Vector2((float)(Math.Sin(elapsedTime * speed) * amplitude), 0);
            case MovementType.Vertical:
                return new Vector2(0, (float)(Math.Sin(elapsedTime * speed) * amplitude));
            case MovementType.Circular:
                return new Vector2(
                    (float)(Math.Cos(elapsedTime * speed) * amplitude),
                    (float)(Math.Sin(elapsedTime * speed) * amplitude));
            case MovementType.Figure8:
                return new Vector2(
                    (float)(Math.Sin(elapsedTime * speed) * amplitude),
                    (float)(Math.Sin(elapsedTime * speed * 2) * amplitude / 2));
            default:
                return Vector2.Zero;
        }
    }

    private Particle CreateParticle(Vector2 position, ParticleConfig config)
    {
        var random = new Random();
        var angle = random.NextDouble() * Math.PI * 2;
        var speed = config.MinSpeed + (float)(random.NextDouble() * (config.MaxSpeed - config.MinSpeed));

        return new Particle
        {
            Position = position + new Vector2(
                (float)(random.NextDouble() * config.EmissionRadius * 2 - config.EmissionRadius),
                (float)(random.NextDouble() * config.EmissionRadius * 2 - config.EmissionRadius)),
            Velocity = new Vector2((float)(Math.Cos(angle) * speed), (float)(Math.Sin(angle) * speed)),
            Life = config.ParticleLifetime,
            MaxLife = config.ParticleLifetime,
            Size = config.MinParticleSize + (float)(random.NextDouble() * (config.MaxParticleSize - config.MinParticleSize))
        };
    }

    private void UpdateParticles(ParticleSystemElement element, float deltaTime)
    {
        var config = element.Config;

        for (int i = 0; i < element.Particles.Count; i++)
        {
            var particle = element.Particles[i];

            if (particle.Life <= 0)
            {
                // Respawn particle
                element.Particles[i] = CreateParticle(element.Position, config);
                continue;
            }

            // Update particle
            particle.Position += particle.Velocity * (deltaTime * 0.016f); // Approximate frame time
            particle.Velocity += config.Gravity * (deltaTime * 0.016f);
            particle.Life -= deltaTime * 0.016f;
        }
    }

    private void OnAnimationTick(object sender, object e)
    {
        _canvasControl?.Invalidate();
    }

    public void Dispose()
    {
        _animationTimer?.Stop();
        _elements.Clear();
        _backgroundImage?.Dispose();
    }

    /// <summary>
    /// Renders the current frame to an external drawing session
    /// This allows the BackgroundRenderingService to use the renderer's drawing logic
    /// </summary>
    /// <param name="session">The canvas drawing session to render to</param>
    /// <param name="currentTime">The current time for animations</param>
    /// <param name="canvasSize">The size of the canvas being rendered to</param>
    public void RenderToSession(CanvasDrawingSession session, DateTime currentTime, Vector2 canvasSize)
    {
        try
        {
            // Update canvas size for boundary calculations
            CanvasWidth = canvasSize.X;
            CanvasHeight = canvasSize.Y;

            // Apply global transforms
            var transform = Matrix3x2.CreateScale(GlobalScale) *
                           Matrix3x2.CreateRotation(GlobalRotation) *
                           Matrix3x2.CreateTranslation(GlobalOffset);
            session.Transform = transform;

            // Clear with background
            session.Clear(Colors.Transparent);

            // Draw background image if available
            if (_backgroundImage != null)
            {
                session.DrawImage(_backgroundImage, new Windows.Foundation.Rect(0, 0, canvasSize.X, canvasSize.Y));
            }

            // Update and draw all elements
            foreach (var element in _elements)
            {
                if (!element.IsVisible) continue;

                // Update motion for motion elements
                if (element is IMotionElement motionElement)
                {
                    UpdateMotionElement(motionElement, currentTime);
                }

                // Draw the element
                switch (element)
                {
                    case MotionTextElement motionTextElement:
                        DrawMotionText(session, motionTextElement, currentTime);
                        break;
                    case MotionShapeElement motionShapeElement:
                        DrawMotionShape(session, motionShapeElement, currentTime);
                        break;
                    case MotionImageElement motionImageElement:
                        DrawMotionImage(session, motionImageElement, currentTime);
                        break;
                    case LiveTextElement textElement:
                        DrawLiveText(session, textElement, currentTime);
                        break;
                    case AnimatedImageElement imageElement:
                        DrawAnimatedImage(session, imageElement, currentTime);
                        break;
                    case ParticleSystemElement particleElement:
                        DrawParticleSystem(session, particleElement, currentTime);
                        break;
                }
            }

            // Debug: Add indicators for motion elements
            var motionCount = _elements.Count(e => e is IMotionElement);
            if (motionCount > 0)
            {
                session.DrawText($"Motion Elements: {motionCount}/{_elements.Count}",
                    new Vector2(10, canvasSize.Y - 50), Colors.LimeGreen);
            }
        }
        catch (Exception ex)
        {
            // Draw error message if something goes wrong
            session.Clear(Colors.DarkRed);
            session.DrawText($"RenderToSession Error: {ex.Message}",
                new Vector2(10, 10), Colors.White);
        }
    }
}

#region Enhanced Configuration Classes

// Enhanced configuration classes
public class LiveTextConfig
{
    public float FontSize { get; set; } = 24;
    public string FontFamily { get; set; } = "Segoe UI";
    public Windows.UI.Text.FontWeight FontWeight { get; set; } = new Windows.UI.Text.FontWeight { Weight = 400 };
    public Windows.UI.Color TextColor { get; set; } = Colors.White;
    public float BaseOpacity { get; set; } = 1.0f;
    public string TimeFormat { get; set; } = "HH:mm:ss";
    public string DateFormat { get; set; } = "yyyy-MM-dd";

    public MovementType MovementType { get; set; } = MovementType.None;
    public float MovementSpeed { get; set; } = 1.0f;
    public float MovementAmplitude { get; set; } = 10.0f;

    public bool EnableBreathing { get; set; } = false;
    public float BreathingSpeed { get; set; } = 2.0f;

    public bool EnableGlow { get; set; } = false;
    public float GlowRadius { get; set; } = 3.0f;
    public float GlowIntensity { get; set; } = 0.8f;
}

public class ImageConfig
{
    public Vector2 Size { get; set; } = new Vector2(100, 100);
    public float BaseOpacity { get; set; } = 1.0f;
    public float BaseScale { get; set; } = 1.0f;
    public float BaseRotation { get; set; } = 0.0f;
    public float RotationSpeed { get; set; } = 0.0f;

    public MovementType MovementType { get; set; } = MovementType.None;
    public float MovementSpeed { get; set; } = 1.0f;
    public float MovementAmplitude { get; set; } = 10.0f;

    public bool EnablePulsing { get; set; } = false;
    public float PulseSpeed { get; set; } = 2.0f;
}

public class ParticleConfig
{
    public int MaxParticles { get; set; } = 100;
    public float ParticleLifetime { get; set; } = 3.0f;
    public float MinParticleSize { get; set; } = 2.0f;
    public float MaxParticleSize { get; set; } = 5.0f;
    public float MinSpeed { get; set; } = 10.0f;
    public float MaxSpeed { get; set; } = 50.0f;
    public float EmissionRadius { get; set; } = 20.0f;
    public Vector2 Gravity { get; set; } = new Vector2(0, 20.0f);
    public Windows.UI.Color ParticleColor { get; set; } = Colors.White;
}

// New motion configuration class
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

#endregion

#region Enhanced Element Classes

// Enhanced animated element classes
public abstract class AnimatedElement
{
    public Vector2 Position { get; set; }
    public bool IsVisible { get; set; } = true;
    public DateTime StartTime { get; set; }
}

// Interface for motion-enabled elements
public interface IMotionElement
{
    Vector2 Position { get; set; }
    Vector2 OriginalPosition { get; set; }
    ElementMotionConfig MotionConfig { get; set; }
    DateTime StartTime { get; set; }
    DateTime LastUpdateTime { get; set; }
    DateTime LastDirectionChange { get; set; }
    float CurrentAngle { get; set; }
    Queue<Vector2>? TrailPositions { get; set; }
}

public class LiveTextElement : AnimatedElement
{
    public string BaseText { get; set; }
    public LiveTextConfig Config { get; set; }
}

public class AnimatedImageElement : AnimatedElement
{
    public CanvasBitmap Image { get; set; }
    public ImageConfig Config { get; set; }
}

public class ParticleSystemElement : AnimatedElement
{
    public ParticleConfig Config { get; set; }
    public List<Particle> Particles { get; set; }
}

// New motion-enabled element classes
public class MotionTextElement : AnimatedElement, IMotionElement
{
    public string BaseText { get; set; }
    public LiveTextConfig Config { get; set; }
    public Vector2 OriginalPosition { get; set; }
    public ElementMotionConfig MotionConfig { get; set; } = new ElementMotionConfig();
    public DateTime LastUpdateTime { get; set; }
    public DateTime LastDirectionChange { get; set; }
    public float CurrentAngle { get; set; }
    public Queue<Vector2>? TrailPositions { get; set; }
}

public class MotionShapeElement : AnimatedElement, IMotionElement
{
    public ShapeType ShapeType { get; set; }
    public float Radius { get; set; } = 10f;
    public Vector2 Size { get; set; } = new Vector2(20, 20);
    public Windows.UI.Color Color { get; set; } = Colors.White;
    public Vector2 OriginalPosition { get; set; }
    public ElementMotionConfig MotionConfig { get; set; } = new ElementMotionConfig();
    public DateTime LastUpdateTime { get; set; }
    public DateTime LastDirectionChange { get; set; }
    public float CurrentAngle { get; set; }
    public Queue<Vector2>? TrailPositions { get; set; }
}

public class MotionImageElement : AnimatedElement, IMotionElement
{
    public CanvasBitmap Image { get; set; }
    public ImageConfig Config { get; set; }
    public Vector2 OriginalPosition { get; set; }
    public ElementMotionConfig MotionConfig { get; set; } = new ElementMotionConfig();
    public DateTime LastUpdateTime { get; set; }
    public DateTime LastDirectionChange { get; set; }
    public float CurrentAngle { get; set; }
    public Queue<Vector2>? TrailPositions { get; set; }
}

public class Particle
{
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public float Life { get; set; }
    public float MaxLife { get; set; }
    public float Size { get; set; }
}

#endregion

#region Enums

public enum MovementType
{
    None,
    Horizontal,
    Vertical,
    Circular,
    Figure8
}

// Enhanced motion types
public enum MotionType
{
    None,
    Linear,
    Circular,
    Bounce,
    Oscillate,
    Spiral,
    Random,
    Wave,
    Orbit
}

public enum ShapeType
{
    Circle,
    Rectangle,
    Triangle
}

#endregion
