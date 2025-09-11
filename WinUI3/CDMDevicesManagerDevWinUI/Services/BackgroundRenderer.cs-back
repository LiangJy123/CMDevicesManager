using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
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

public class BackgroundRenderer
{
    private CanvasControl _canvasControl;
    private readonly List<RenderElement> _elements = new();
    private readonly DispatcherTimer _timer;
    private bool _isInitialized;

    public event EventHandler<CanvasControl> CanvasReady;
    public event EventHandler<SoftwareBitmap> ImageGenerated;

    public BackgroundRenderer()
    {
        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16); // 60 FPS
        _timer.Tick += OnTimerTick;
    }

    public void Initialize(CanvasControl canvasControl)
    {
        _canvasControl = canvasControl;
        _canvasControl.CreateResources += OnCreateResources;
        _canvasControl.Draw += OnDraw;
        _isInitialized = true;
        CanvasReady?.Invoke(this, _canvasControl);
    }

    public void StartRendering()
    {
        if (_isInitialized)
        {
            _timer.Start();
        }
    }

    public void StopRendering()
    {
        _timer.Stop();
    }

    public void AddTextElement(string text, Vector2 position, float fontSize = 24, Windows.UI.Color color = default)
    {
        if (color == default)
            color = Colors.White;

        _elements.Add(new TextElement
        {
            Text = text,
            Position = position,
            FontSize = fontSize,
            Color = color,
            IsVisible = true
        });
    }

    public void AddImageElement(CanvasBitmap image, Vector2 position, Vector2 size = default)
    {
        _elements.Add(new ImageElement
        {
            Image = image,
            Position = position,
            Size = size == default ? new Vector2(image.SizeInPixels.Width, image.SizeInPixels.Height) : size,
            IsVisible = true
        });
    }

    public void AddShapeElement(ShapeType shapeType, Vector2 position, Vector2 size, Windows.UI.Color color)
    {
        _elements.Add(new ShapeElement
        {
            ShapeType = shapeType,
            Position = position,
            Size = size,
            Color = color,
            IsVisible = true
        });
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
            DrawFrame(session);
        }

        var pixels = renderTarget.GetPixelBytes();
        var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            (int)renderTarget.SizeInPixels.Width,
            (int)renderTarget.SizeInPixels.Height,
            BitmapAlphaMode.Premultiplied);

        ImageGenerated?.Invoke(this, softwareBitmap);
        return softwareBitmap;
    }

    private void OnCreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
        // Resources created
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        DrawFrame(args.DrawingSession);
    }

    private void DrawFrame(CanvasDrawingSession session)
    {
        // Clear the canvas with a dark background
        session.Clear(Colors.Black);

        // Draw all elements
        foreach (var element in _elements)
        {
            if (!element.IsVisible) continue;

            switch (element)
            {
                case TextElement textElement:
                    DrawText(session, textElement);
                    break;
                case ImageElement imageElement:
                    DrawImage(session, imageElement);
                    break;
                case ShapeElement shapeElement:
                    DrawShape(session, shapeElement);
                    break;
            }
        }
    }

    private void DrawText(CanvasDrawingSession session, TextElement element)
    {
        var textFormat = new CanvasTextFormat
        {
            FontSize = element.FontSize,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        // Add dynamic time update for time text
        var text = element.Text;
        if (text.Contains("{TIME}"))
        {
            text = text.Replace("{TIME}", DateTime.Now.ToString("HH:mm:ss"));
        }
        if (text.Contains("{DATE}"))
        {
            text = text.Replace("{DATE}", DateTime.Now.ToString("yyyy-MM-dd"));
        }

        session.DrawText(text, element.Position, element.Color, textFormat);
    }

    private void DrawImage(CanvasDrawingSession session, ImageElement element)
    {
        if (element.Image != null)
        {
            var destRect = new Windows.Foundation.Rect(
                element.Position.X, 
                element.Position.Y, 
                element.Size.X, 
                element.Size.Y);
            session.DrawImage(element.Image, destRect);
        }
    }

    private void DrawShape(CanvasDrawingSession session, ShapeElement element)
    {
        switch (element.ShapeType)
        {
            case ShapeType.Rectangle:
                var rect = new Windows.Foundation.Rect(
                    element.Position.X, 
                    element.Position.Y, 
                    element.Size.X, 
                    element.Size.Y);
                session.FillRectangle(rect, element.Color);
                break;
            case ShapeType.Circle:
                session.FillCircle(
                    element.Position.X + element.Size.X / 2, 
                    element.Position.Y + element.Size.Y / 2, 
                    Math.Min(element.Size.X, element.Size.Y) / 2, 
                    element.Color);
                break;
            case ShapeType.Line:
                session.DrawLine(
                    element.Position, 
                    element.Position + element.Size, 
                    element.Color, 
                    2.0f);
                break;
        }
    }

    private void OnTimerTick(object sender, object e)
    {
        _canvasControl?.Invalidate();
    }

    public void Dispose()
    {
        _timer?.Stop();
        _elements.Clear();
    }
}

// Base class for render elements
public abstract class RenderElement
{
    public Vector2 Position { get; set; }
    public bool IsVisible { get; set; } = true;
}

// Text element for rendering live text
public class TextElement : RenderElement
{
    public string Text { get; set; }
    public float FontSize { get; set; } = 24;
    public Windows.UI.Color Color { get; set; } = Colors.White;
}

// Image element for rendering images
public class ImageElement : RenderElement
{
    public CanvasBitmap Image { get; set; }
    public Vector2 Size { get; set; }
}

// Shape element for rendering basic shapes
public class ShapeElement : RenderElement
{
    public ShapeType ShapeType { get; set; }
    public Vector2 Size { get; set; }
    public Windows.UI.Color Color { get; set; }
}

public enum ShapeType
{
    Rectangle,
    Circle,
    Line
}
