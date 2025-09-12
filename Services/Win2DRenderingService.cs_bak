using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Foundation;

namespace CMDevicesManager.Services
{
    public class Win2DRenderingService
    {
        private CanvasDevice? _canvasDevice;
        private CanvasRenderTarget? _renderTarget;
        private CanvasBitmap? _backgroundImage;
        private readonly object _lockObject = new object();
        
        public event Action<WriteableBitmap>? ImageRendered;
        
        public int Width { get; private set; } = 480;
        public int Height { get; private set; } = 480;
        
        public async Task InitializeAsync(int width = 480, int height = 480)
        {
            Width = width;
            Height = height;
            
            _canvasDevice = CanvasDevice.GetSharedDevice();
            _renderTarget = new CanvasRenderTarget(_canvasDevice, width, height, 96);
            
            // Load a sample background image if it exists
            await LoadBackgroundImageAsync();
        }
        
        private async Task LoadBackgroundImageAsync()
        {
            try
            {
                // Try to load a background image from the project directory
                var imagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "background.jpg");
                if (File.Exists(imagePath))
                {
                    _backgroundImage = await CanvasBitmap.LoadAsync(_canvasDevice!, imagePath);
                }
            }
            catch
            {
                // Background image is optional
                _backgroundImage = null;
            }
        }
        
        public async Task<WriteableBitmap?> RenderFrameAsync()
        {
            if (_canvasDevice == null || _renderTarget == null)
                return null;
                
            lock (_lockObject)
            {
                using (var session = _renderTarget.CreateDrawingSession())
                {
                    // Clear with a gradient background if no image is available
                    if (_backgroundImage == null)
                    {
                        // Create a simple gradient using solid brushes
                        using (var brush1 = new CanvasSolidColorBrush(_canvasDevice, Windows.UI.Color.FromArgb(255, 30, 30, 60)))
                        using (var brush2 = new CanvasSolidColorBrush(_canvasDevice, Windows.UI.Color.FromArgb(255, 10, 10, 30)))
                        {
                            session.FillRectangle(0, 0, Width, Height, brush1);
                            // Create a simple gradient effect by drawing overlapping rectangles
                            for (int i = 0; i < Height; i += 10)
                            {
                                var alpha = (byte)(255 * i / Height);
                                using (var gradientBrush = new CanvasSolidColorBrush(_canvasDevice, Windows.UI.Color.FromArgb(alpha, 10, 10, 30)))
                                {
                                    session.FillRectangle(0, i, Width, 10, gradientBrush);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Draw background image
                        session.DrawImage(_backgroundImage, new Rect(0, 0, Width, Height));
                    }
                    
                    // Render live time
                    RenderTime(session);
                    
                    // Render sample text and shapes
                    RenderSampleContent(session);
                }
            }
            
            // Convert to WriteableBitmap for WPF display
            var writeableBitmap = await ConvertToWriteableBitmapAsync(_renderTarget);
            ImageRendered?.Invoke(writeableBitmap);
            
            return writeableBitmap;
        }
        
        private void RenderTime(CanvasDrawingSession session)
        {
            var currentTime = DateTime.Now.ToString("HH:mm:ss");
            var timeFormat = new CanvasTextFormat()
            {
                FontSize = 48,
                FontFamily = "Segoe UI",
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center
            };
            
            var timeRect = new Rect(Width / 2 - 150, 50, 300, 60);
            
            // Draw time with shadow effect
            session.DrawText(currentTime, timeRect, Windows.UI.Color.FromArgb(100, 0, 0, 0), timeFormat);
            session.DrawText(currentTime, new Rect(timeRect.X - 2, timeRect.Y - 2, timeRect.Width, timeRect.Height), 
                Windows.UI.Color.FromArgb(255, 255, 255, 255), timeFormat);
        }
        
        private void RenderSampleContent(CanvasDrawingSession session)
        {
            // Draw date
            var currentDate = DateTime.Now.ToString("dddd, MMMM dd, yyyy");
            var dateFormat = new CanvasTextFormat()
            {
                FontSize = 24,
                FontFamily = "Segoe UI",
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center
            };
            
            session.DrawText(currentDate, new Rect(0, 120, Width, 30), 
                Windows.UI.Color.FromArgb(200, 255, 255, 255), dateFormat);
            
            // Draw some animated elements
            var centerX = Width / 2;
            var centerY = Height / 2;
            var time = DateTime.Now.Millisecond / 1000.0f;
            
            // Animated circle
            var circleRadius = 20 + (float)(Math.Sin(time * Math.PI * 2) * 10);
            session.FillCircle(centerX - 100, centerY, circleRadius, 
                Windows.UI.Color.FromArgb(150, 100, 200, 255));
            
            // Animated rectangle
            var rectSize = 40 + (float)(Math.Cos(time * Math.PI * 2) * 15);
            session.FillRectangle(centerX + 100 - rectSize/2, centerY - rectSize/2, rectSize, rectSize, 
                Windows.UI.Color.FromArgb(150, 255, 100, 100));
            
            // System info
            var systemInfo = $"Memory: {GC.GetTotalMemory(false) / 1024 / 1024:F1} MB";
            var infoFormat = new CanvasTextFormat()
            {
                FontSize = 16,
                FontFamily = "Consolas"
            };
            
            session.DrawText(systemInfo, new Rect(10, Height - 30, Width - 20, 20), 
                Windows.UI.Color.FromArgb(180, 200, 200, 200), infoFormat);
        }
        
        private async Task<WriteableBitmap> ConvertToWriteableBitmapAsync(CanvasRenderTarget renderTarget)
        {
            var bytes = renderTarget.GetPixelBytes();
            return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var writeableBitmap = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, null);
                writeableBitmap.WritePixels(
                    new System.Windows.Int32Rect(0, 0, Width, Height),
                    bytes,
                    Width * 4,
                    0);
                return writeableBitmap;
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
            _backgroundImage?.Dispose();
            _renderTarget?.Dispose();
            _canvasDevice?.Dispose();
        }
    }
}