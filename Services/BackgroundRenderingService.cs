using Microsoft.Graphics.Canvas;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CMDevicesManager.Services
{
    public class BackgroundRenderingService : IDisposable
    {
        private Win2DRenderingService? _renderingService;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _renderingTask;
        private readonly object _lockObject = new object();
        
        public event Action<WriteableBitmap>? FrameRendered;
        public event Action<byte[]>? RawImageDataReady;
        public event Action<Exception>? RenderingError;
        
        public bool IsRunning { get; private set; }
        public int TargetFPS { get; set; } = 30;
        public int Width { get; private set; }
        public int Height { get; private set; }
        
        public async Task InitializeAsync(int width = 800, int height = 600)
        {
            Width = width;
            Height = height;
            
            _renderingService = new Win2DRenderingService();
            await _renderingService.InitializeAsync(width, height);
            
            _renderingService.ImageRendered += OnImageRendered;
        }
        
        public async Task StartAsync()
        {
            if (IsRunning || _renderingService == null)
                return;
                
            _cancellationTokenSource = new CancellationTokenSource();
            IsRunning = true;
            
            _renderingTask = Task.Run(async () => await RenderingLoopAsync(_cancellationTokenSource.Token));
            
            await Task.Delay(100); // Give the task a moment to start
        }
        
        public async Task StopAsync()
        {
            if (!IsRunning)
                return;
                
            IsRunning = false;
            _cancellationTokenSource?.Cancel();
            
            if (_renderingTask != null)
            {
                try
                {
                    await _renderingTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                }
            }
        }
        
        private async Task RenderingLoopAsync(CancellationToken cancellationToken)
        {
            var frameTime = TimeSpan.FromMilliseconds(1000.0 / TargetFPS);
            var lastFrameTime = DateTime.UtcNow;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var currentTime = DateTime.UtcNow;
                    var elapsed = currentTime - lastFrameTime;
                    
                    if (elapsed >= frameTime)
                    {
                        await RenderFrame();
                        lastFrameTime = currentTime;
                    }
                    
                    // Small delay to prevent excessive CPU usage
                    await Task.Delay(1, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    RenderingError?.Invoke(ex);
                    
                    // Wait a bit before retrying to avoid rapid error loops
                    await Task.Delay(100, cancellationToken);
                }
            }
        }
        
        private async Task RenderFrame()
        {
            if (_renderingService == null)
                return;
                
            lock (_lockObject)
            {
                // Render frame in background thread
                var _ = Task.Run(async () =>
                {
                    try
                    {
                        await _renderingService.RenderFrameAsync();
                        
                        // Also provide raw image data
                        var rawData = _renderingService.GetRenderedImageBytes();
                        if (rawData != null)
                        {
                            RawImageDataReady?.Invoke(rawData);
                        }
                    }
                    catch (Exception ex)
                    {
                        RenderingError?.Invoke(ex);
                    }
                });
            }
        }
        
        private void OnImageRendered(WriteableBitmap bitmap)
        {
            FrameRendered?.Invoke(bitmap);
        }
        
        public byte[]? GetCurrentFrameData()
        {
            return _renderingService?.GetRenderedImageBytes();
        }
        
        public async Task<bool> SaveCurrentFrameAsync(string filePath)
        {
            if (_renderingService == null)
                return false;
                
            try
            {
                await _renderingService.SaveRenderedImageAsync(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        public void Dispose()
        {
            StopAsync().Wait(1000);
            _cancellationTokenSource?.Dispose();
            _renderingService?.Dispose();
        }
    }
}