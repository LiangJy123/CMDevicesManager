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

        public event Action<WriteableBitmap>? ImageRendered;
        public event Action<RenderElement>? ElementSelected;
        public event Action<RenderElement>? ElementMoved;

        public int Width { get; private set; } = 800;
        public int Height { get; private set; } = 600;
        public RenderElement? SelectedElement { get; private set; }

        // Display options
        public bool ShowTime { get; set; } = true;
        public bool ShowDate { get; set; } = true;
        public bool ShowSystemInfo { get; set; } = true;
        public bool ShowAnimation { get; set; } = true;

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
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize InteractiveWin2DRenderingService: {ex.Message}", ex);
            }
        }

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
            foreach (var bitmap in _loadedImages.Values)
            {
                bitmap?.Dispose();
            }
            _loadedImages.Clear();
            
            _backgroundImage?.Dispose();
            _renderTarget?.Dispose();
            _canvasDevice?.Dispose();
        }
    }
}