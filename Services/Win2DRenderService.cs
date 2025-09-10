using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SkiaSharp;

namespace CMDevicesManager.Services
{
    public class SkiaRenderService : IRenderService
    {
        private readonly ConcurrentDictionary<string, RenderElement> _elements = new();
        private SKSurface? _surface;
        private System.Threading.Timer? _realtimeTimer;
        private bool _isDisposed;

        public SKSizeI CanvasSize { get; set; } = new SKSizeI(512, 512);
        public SKColor BackgroundColor { get; set; } = SKColors.Black;
        public bool IsRealtimeRenderingActive { get; private set; }

        public event EventHandler<RenderOutputEventArgs>? RenderOutputReady;

        public void Initialize()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SkiaRenderService));

            try
            {
                CreateSurface();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to initialize SkiaSharp render service.", ex);
            }
        }

        private void CreateSurface()
        {
            _surface?.Dispose();
            
            var info = new SKImageInfo(CanvasSize.Width, CanvasSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            _surface = SKSurface.Create(info);
            
            if (_surface == null)
                throw new InvalidOperationException("Failed to create SkiaSharp surface");
        }

        public void AddImage(string id, byte[] imageData, SKPoint position, SKSize size, float opacity = 1.0f, float rotation = 0.0f)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SkiaRenderService));

            try
            {
                using var stream = new MemoryStream(imageData);
                var bitmap = SKBitmap.Decode(stream);
                
                if (bitmap == null)
                    throw new InvalidOperationException("Failed to decode image data");

                var element = new ImageElement
                {
                    Id = id,
                    Position = position,
                    Size = size,
                    Opacity = Math.Clamp(opacity, 0.0f, 1.0f),
                    Rotation = rotation,
                    Bitmap = bitmap
                };

                _elements.AddOrUpdate(id, element, (key, oldValue) =>
                {
                    if (oldValue is ImageElement oldImage)
                        oldImage.Bitmap?.Dispose();
                    return element;
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to add image element '{id}': {ex.Message}", ex);
            }
        }

        public void AddText(string id, string text, SKPoint position, float fontSize, SKColor color, string fontFamily = "Segoe UI", float opacity = 1.0f, float rotation = 0.0f)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SkiaRenderService));

            var element = new TextElement
            {
                Id = id,
                Text = text,
                Position = position,
                FontSize = Math.Max(fontSize, 1.0f),
                Color = color,
                FontFamily = fontFamily,
                Opacity = Math.Clamp(opacity, 0.0f, 1.0f),
                Rotation = rotation
            };

            _elements.AddOrUpdate(id, element, (key, oldValue) =>
            {
                if (oldValue is ImageElement oldImage)
                    oldImage.Bitmap?.Dispose();
                return element;
            });
        }

        public void UpdateElementPosition(string id, SKPoint position)
        {
            if (_elements.TryGetValue(id, out var element))
            {
                element.Position = position;
            }
        }

        public void UpdateElementOpacity(string id, float opacity)
        {
            if (_elements.TryGetValue(id, out var element))
            {
                element.Opacity = Math.Clamp(opacity, 0.0f, 1.0f);
            }
        }

        public void UpdateElementRotation(string id, float rotation)
        {
            if (_elements.TryGetValue(id, out var element))
            {
                element.Rotation = rotation;
            }
        }

        public void UpdateTextContent(string id, string text)
        {
            if (_elements.TryGetValue(id, out var element) && element is TextElement textElement)
            {
                textElement.Text = text;
            }
        }

        public void UpdateTextColor(string id, SKColor color)
        {
            if (_elements.TryGetValue(id, out var element) && element is TextElement textElement)
            {
                textElement.Color = color;
            }
        }

        public void RemoveElement(string id)
        {
            if (_elements.TryRemove(id, out var element))
            {
                if (element is ImageElement imageElement)
                    imageElement.Bitmap?.Dispose();
            }
        }

        public void ClearAll()
        {
            foreach (var kvp in _elements)
            {
                if (kvp.Value is ImageElement imageElement)
                    imageElement.Bitmap?.Dispose();
            }
            _elements.Clear();
        }

        public void RenderFrame()
        {
            if (_isDisposed || _surface?.Canvas == null)
                return;

            try
            {
                var canvas = _surface.Canvas;
                
                // Clear background
                canvas.Clear(BackgroundColor);

                // Render elements in Z-order
                var sortedElements = _elements.Values.OrderBy(e => e.ZIndex).ToArray();
                foreach (var element in sortedElements)
                {
                    try
                    {
                        element.Render(canvas);
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue rendering other elements
                        System.Diagnostics.Debug.WriteLine($"Error rendering element {element.Id}: {ex.Message}");
                    }
                }

                canvas.Flush();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to render frame: {ex.Message}", ex);
            }
        }

        public byte[] GetRenderedFrameData()
        {
            if (_isDisposed || _surface == null)
                return Array.Empty<byte>();

            try
            {
                using var image = _surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data.ToArray();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to get rendered frame data: {ex.Message}", ex);
            }
        }

        public SKImage? GetRenderedImage()
        {
            if (_isDisposed || _surface == null)
                return null;

            try
            {
                return _surface.Snapshot();
            }
            catch
            {
                return null;
            }
        }

        public void StartRealtimeRendering(int fps = 30)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SkiaRenderService));

            if (IsRealtimeRenderingActive)
                return;

            fps = Math.Clamp(fps, 1, 120);
            var interval = TimeSpan.FromMilliseconds(1000.0 / fps);

            _realtimeTimer = new System.Threading.Timer(OnRealtimeRender, null, TimeSpan.Zero, interval);
            IsRealtimeRenderingActive = true;
        }

        public void StopRealtimeRendering()
        {
            _realtimeTimer?.Dispose();
            _realtimeTimer = null;
            IsRealtimeRenderingActive = false;
        }

        private void OnRealtimeRender(object? state)
        {
            try
            {
                RenderFrame();
                var frameData = GetRenderedFrameData();
                var renderedImage = GetRenderedImage();
                RenderOutputReady?.Invoke(this, new RenderOutputEventArgs(frameData, CanvasSize, renderedImage));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in realtime render: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            StopRealtimeRendering();
            ClearAll();

            _surface?.Dispose();
            _surface = null;
        }
    }
}