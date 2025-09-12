using Microsoft.Graphics.Canvas;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Media;
using System.Linq;

namespace CMDevicesManager.Services
{
    public class BackgroundRenderingService : IDisposable
    {
        private Win2DRenderingService? _renderingService;
        private HidDeviceService? _hidDeviceService;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _renderingTask;
        private readonly object _lockObject = new object();
        private byte _transferId = 1;
        private bool _hidRealTimeModeEnabled = false;
        private int _hidFramesSent = 0;
        
        public event Action<WriteableBitmap>? FrameRendered;
        public event Action<byte[]>? RawImageDataReady;
        public event Action<byte[]>? JpegDataSentToHid;
        public event Action<Exception>? RenderingError;
        public event Action<string>? HidStatusChanged;
        
        public bool IsRunning { get; private set; }
        public bool SendToHidDevices { get; set; } = false;
        public bool UseSuspendMedia { get; set; } = false;
        public int TargetFPS { get; set; } = 30;
        public int JpegQuality { get; set; } = 85;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool IsHidServiceConnected => _hidDeviceService?.IsInitialized == true;
        public bool IsHidRealTimeModeEnabled => _hidRealTimeModeEnabled;
        public int HidFramesSent => _hidFramesSent;
        
        public async Task InitializeAsync(int width = 800, int height = 600)
        {
            Width = width;
            Height = height;
            
            _renderingService = new Win2DRenderingService();
            await _renderingService.InitializeAsync(width, height);
            
            _renderingService.ImageRendered += OnImageRendered;
            
            // Initialize HID service connection
            await InitializeHidServiceAsync();
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
        
        public async Task StartAsync()
        {
            if (IsRunning || _renderingService == null)
                return;
                
            _cancellationTokenSource = new CancellationTokenSource();
            IsRunning = true;
            
            // Enable real-time display mode on HID devices if HID transfer is enabled
            if (SendToHidDevices && !UseSuspendMedia)
            {
                await EnableHidRealTimeDisplayAsync(true);
            }
            
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
            
            // Disable real-time display mode when stopping
            if (_hidRealTimeModeEnabled)
            {
                await EnableHidRealTimeDisplayAsync(false);
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
                        
                        // Get raw image data
                        var rawData = _renderingService.GetRenderedImageBytes();
                        if (rawData != null)
                        {
                            RawImageDataReady?.Invoke(rawData);
                            
                            // Send to HID devices if enabled
                            if (SendToHidDevices && _hidDeviceService?.IsInitialized == true)
                            {
                                if (_hidRealTimeModeEnabled)
                                {
                                    await SendFrameToHidDevicesAsync(rawData);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        RenderingError?.Invoke(ex);
                    }
                });
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
                    
                    // Send JPEG data to HID devices using the correct TransferDataAsync method
                    var results = await _hidDeviceService!.TransferDataAsync(jpegData, _transferId);
                    
                    // Log results
                    var successCount = results.Values.Count(r => r);
                    if (successCount > 0)
                    {
                        _hidFramesSent++;
                        JpegDataSentToHid?.Invoke(jpegData);
                        HidStatusChanged?.Invoke($"Frame #{_hidFramesSent} sent to {successCount}/{results.Count} devices (Transfer ID: {_transferId}, Size: {jpegData.Length:N0} bytes)");
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
        
        public async Task<bool> SaveCurrentFrameAndSendAsSuspendMediaAsync(string filePath)
        {
            if (_renderingService == null)
                return false;
                
            try
            {
                await _renderingService.SaveRenderedImageAsync(filePath);
                
                // Send as suspend media if HID is enabled
                if (SendToHidDevices && UseSuspendMedia && _hidDeviceService?.IsInitialized == true)
                {
                    var results = await _hidDeviceService.SendMultipleSuspendFilesAsync(new[] { filePath }.ToList(), _transferId);
                    var successCount = results.Values.Count(r => r);
                    
                    if (successCount > 0)
                    {
                        HidStatusChanged?.Invoke($"Saved frame sent as suspend media to {successCount}/{results.Count} devices");
                        
                        // Increment transfer ID
                        _transferId++;
                        if (_transferId > 59) _transferId = 1;
                        
                        return true;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                HidStatusChanged?.Invoke($"Error saving and sending frame: {ex.Message}");
                return false;
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
        
        public void ResetHidFrameCounter()
        {
            _hidFramesSent = 0;
            HidStatusChanged?.Invoke("HID frame counter reset");
        }
        
        public void Dispose()
        {
            StopAsync().Wait(1000);
            
            // Unsubscribe from HID service events
            if (_hidDeviceService != null)
            {
                _hidDeviceService.DeviceConnected -= OnHidDeviceConnected;
                _hidDeviceService.DeviceDisconnected -= OnHidDeviceDisconnected;
                _hidDeviceService.DeviceError -= OnHidDeviceError;
                _hidDeviceService.RealTimeDisplayModeChanged -= OnHidRealTimeDisplayModeChanged;
            }
            
            _cancellationTokenSource?.Dispose();
            _renderingService?.Dispose();
        }
    }
}