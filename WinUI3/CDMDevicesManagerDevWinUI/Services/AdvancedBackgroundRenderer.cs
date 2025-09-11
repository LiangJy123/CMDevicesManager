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

    public event EventHandler<CanvasControl> CanvasReady;
    public event EventHandler<SoftwareBitmap> FrameCaptured;

    public float GlobalOpacity { get; set; } = 1.0f;
    public Vector2 GlobalOffset { get; set; } = Vector2.Zero;
    public float GlobalScale { get; set; } = 1.0f;
    public float GlobalRotation { get; set; } = 0.0f;

    public int ElementCount => _elements.Count;

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

    public void UpdateElementPosition(int index, Vector2 newPosition)
    {
        if (index >= 0 && index < _elements.Count)
        {
            _elements[index].Position = newPosition;
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

        // Draw all elements
        foreach (var element in _elements)
        {
            if (!element.IsVisible) continue;

            switch (element)
            {
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

            // Draw all elements
            foreach (var element in _elements)
            {
                if (!element.IsVisible) continue;

                switch (element)
                {
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

            // Debug: Add a simple indicator that RenderToSession was called
            if (_elements.Count > 0)
            {
                session.DrawText($"RenderToSession: {_elements.Count} elements rendered",
                    new Vector2(10, canvasSize.Y - 30), Colors.Lime);
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

// Configuration classes
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

// Animated element classes
public abstract class AnimatedElement
{
    public Vector2 Position { get; set; }
    public bool IsVisible { get; set; } = true;
    public DateTime StartTime { get; set; }
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

public class Particle
{
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public float Life { get; set; }
    public float MaxLife { get; set; }
    public float Size { get; set; }
}

public enum MovementType
{
    None,
    Horizontal,
    Vertical,
    Circular,
    Figure8
}
