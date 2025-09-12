# HID Device Integration with BackgroundRenderingService

## Overview

The `BackgroundRenderingService` has been enhanced with comprehensive HID device integration capabilities, allowing you to send rendered frames to connected HID devices in real-time or as suspend media files. This integration uses the `HidDeviceService` and its `TransferDataAsync` API for seamless data transmission.

## Key Features

### Real-Time JPEG Streaming
- **Live rendering**: Continuous frame rendering and transmission to HID devices
- **Configurable quality**: Adjustable JPEG compression (1-100% quality)
- **Frame rate control**: Configurable FPS (1-120 FPS)
- **Transfer ID management**: Automatic transfer ID cycling (1-59 range)
- **Multi-device support**: Simultaneous transmission to multiple HID devices

### Suspend Media Transfer
- **File-based transfer**: Save rendered frames as files and transfer using suspend media functionality
- **Batch operations**: Support for multiple file transfers
- **Suspend mode control**: Switch devices between real-time and suspend modes

### Event-Driven Architecture
- **Status monitoring**: Real-time status updates and error reporting
- **Transfer tracking**: Frame count and data size monitoring
- **Device events**: Connection/disconnection notifications

## Quick Start Guide

### 1. Basic Initialization

```csharp
using CMDevicesManager.Services;

// Create and initialize the service
var renderingService = new BackgroundRenderingService();
await renderingService.InitializeAsync(480, 480); // 480x480 resolution

// Check HID service availability
if (renderingService.IsHidServiceConnected)
{
    Console.WriteLine("HID devices are available for rendering");
}
```

### 2. Enable Real-Time HID Transfer

```csharp
// Configure rendering settings
renderingService.TargetFPS = 30;           // 30 FPS
renderingService.JpegQuality = 85;         // 85% JPEG quality

// Enable real-time HID transfer
renderingService.EnableHidTransfer(true, useSuspendMedia: false);

// Start rendering with automatic HID transmission
await renderingService.StartAsync();
```

### 3. Monitor Transfer Status

```csharp
// Subscribe to events for monitoring
renderingService.HidStatusChanged += (status) => {
    Console.WriteLine($"HID Status: {status}");
};

renderingService.JpegDataSentToHid += (jpegData) => {
    Console.WriteLine($"Frame sent: {jpegData.Length:N0} bytes");
};

renderingService.RenderingError += (ex) => {
    Console.WriteLine($"Error: {ex.Message}");
};
```

## API Reference

### Core Methods

#### `InitializeAsync(int width, int height)`
Initialize the rendering service with specified canvas dimensions.
```csharp
await renderingService.InitializeAsync(480, 480);
```

#### `EnableHidTransfer(bool enable, bool useSuspendMedia = false)`
Enable or disable HID transfer functionality.
```csharp
// Enable real-time mode
renderingService.EnableHidTransfer(true, useSuspendMedia: false);

// Enable suspend media mode
renderingService.EnableHidTransfer(true, useSuspendMedia: true);

// Disable HID transfer
renderingService.EnableHidTransfer(false);
```

#### `StartAsync()` / `StopAsync()`
Control the rendering loop.
```csharp
await renderingService.StartAsync();   // Start rendering
await renderingService.StopAsync();    // Stop rendering
```

#### `EnableHidRealTimeDisplayAsync(bool enable)`
Explicitly control real-time display mode on HID devices.
```csharp
var success = await renderingService.EnableHidRealTimeDisplayAsync(true);
if (success) {
    Console.WriteLine("Real-time mode enabled on devices");
}
```

#### `SaveCurrentFrameAndSendAsSuspendMediaAsync(string filePath)`
Save current frame and send as suspend media.
```csharp
var tempPath = Path.GetTempPath() + "frame.jpg";
var success = await renderingService.SaveCurrentFrameAndSendAsSuspendMediaAsync(tempPath);
```

#### `SetSuspendModeAsync()`
Switch HID devices to suspend mode.
```csharp
var success = await renderingService.SetSuspendModeAsync();
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsHidServiceConnected` | `bool` | Whether HID service is available |
| `IsHidRealTimeModeEnabled` | `bool` | Whether real-time mode is active |
| `SendToHidDevices` | `bool` | Whether HID transfer is enabled |
| `UseSuspendMedia` | `bool` | Whether to use suspend media mode |
| `TargetFPS` | `int` | Target frames per second (1-120) |
| `JpegQuality` | `int` | JPEG compression quality (1-100) |
| `HidFramesSent` | `int` | Number of frames sent to HID devices |
| `IsRunning` | `bool` | Whether rendering loop is active |
| `Width` / `Height` | `int` | Canvas dimensions |

### Events

| Event | Type | Description |
|-------|------|-------------|
| `HidStatusChanged` | `Action<string>` | HID status updates and messages |
| `JpegDataSentToHid` | `Action<byte[]>` | Fired when JPEG data is sent |
| `RenderingError` | `Action<Exception>` | Rendering errors |
| `FrameRendered` | `Action<WriteableBitmap>` | Frame rendered for UI display |
| `RawImageDataReady` | `Action<byte[]>` | Raw pixel data available |

## Usage Patterns

### 1. Real-Time Device Dashboard

```csharp
public class DeviceDashboard : IDisposable
{
    private BackgroundRenderingService _renderingService;
    
    public async Task StartDashboardAsync()
    {
        _renderingService = new BackgroundRenderingService();
        
        // Configure for smooth real-time updates
        _renderingService.TargetFPS = 15;
        _renderingService.JpegQuality = 80;
        
        await _renderingService.InitializeAsync(480, 480);
        
        // Enable real-time HID streaming
        _renderingService.EnableHidTransfer(true, useSuspendMedia: false);
        
        // Start rendering
        await _renderingService.StartAsync();
        
        Console.WriteLine("Dashboard streaming to HID devices...");
    }
    
    public void Dispose() => _renderingService?.Dispose();
}
```

### 2. Batch Media Transfer

```csharp
public async Task SendMediaBatchAsync(string[] imagePaths)
{
    var renderingService = new BackgroundRenderingService();
    await renderingService.InitializeAsync(480, 480);
    
    // Enable suspend media mode
    renderingService.EnableHidTransfer(true, useSuspendMedia: true);
    
    foreach (var imagePath in imagePaths)
    {
        var success = await renderingService.SaveCurrentFrameAndSendAsSuspendMediaAsync(imagePath);
        Console.WriteLine($"Sent {Path.GetFileName(imagePath)}: {success}");
        
        await Task.Delay(1000); // Wait between transfers
    }
    
    // Switch to suspend mode
    await renderingService.SetSuspendModeAsync();
    
    renderingService.Dispose();
}
```

### 3. Performance Monitoring

```csharp
public class RenderingMonitor
{
    private BackgroundRenderingService _service;
    private int _framesSent = 0;
    private DateTime _startTime;
    
    public async Task StartMonitoringAsync()
    {
        _service = new BackgroundRenderingService();
        _startTime = DateTime.Now;
        
        _service.JpegDataSentToHid += OnFrameSent;
        _service.HidStatusChanged += OnStatusChanged;
        
        await _service.InitializeAsync(480, 480);
        _service.EnableHidTransfer(true, useSuspendMedia: false);
        await _service.StartAsync();
        
        // Report statistics every 10 seconds
        var timer = new Timer(ReportStatistics, null, 
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }
    
    private void OnFrameSent(byte[] jpegData)
    {
        Interlocked.Increment(ref _framesSent);
    }
    
    private void OnStatusChanged(string status)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {status}");
    }
    
    private void ReportStatistics(object state)
    {
        var elapsed = DateTime.Now - _startTime;
        var avgFps = _framesSent / elapsed.TotalSeconds;
        
        Console.WriteLine($"Statistics: {_framesSent} frames sent, " +
                         $"Average FPS: {avgFps:F2}, " +
                         $"Runtime: {elapsed:mm\\:ss}");
    }
}
```

## Performance Considerations

### Optimal Settings for Different Use Cases

#### Real-Time Dashboard (Smooth Updates)
- **FPS**: 15-30
- **JPEG Quality**: 70-85%
- **Resolution**: 480x480 or device native

#### Status Monitoring (Low Bandwidth)
- **FPS**: 5-10
- **JPEG Quality**: 50-70%
- **Resolution**: 320x240 or smaller

#### High-Quality Display (Best Visual)
- **FPS**: 10-15
- **JPEG Quality**: 90-100%
- **Resolution**: Device native

### Memory Management
```csharp
// Proper disposal pattern
using (var renderingService = new BackgroundRenderingService())
{
    await renderingService.InitializeAsync(480, 480);
    // ... use service
    await renderingService.StopAsync();
} // Automatically disposed
```

### Error Handling
```csharp
_renderingService.RenderingError += (ex) => {
    if (ex is InvalidOperationException)
    {
        // Handle service state errors
        Console.WriteLine("Service state error - attempting restart...");
    }
    else if (ex is IOException)
    {
        // Handle file I/O errors
        Console.WriteLine("File I/O error - checking disk space...");
    }
};
```

## Integration with HidDeviceService

The `BackgroundRenderingService` integrates seamlessly with `HidDeviceService`:

### Transfer ID Management
- Automatic cycling through transfer IDs (1-59)
- Prevents ID conflicts in multi-frame scenarios
- Ensures protocol compliance

### Device Filtering
```csharp
// The HID service respects device path filters
var hidService = ServiceLocator.HidDeviceService;
hidService.SetDevicePathFilter(specificDevicePaths);

// Rendering service will only send to filtered devices
await renderingService.StartAsync();
```

### Real-Time Mode Control
```csharp
// Direct HID service control
await hidService.SetRealTimeDisplayAsync(true);

// Or through rendering service
await renderingService.EnableHidRealTimeDisplayAsync(true);
```

## Troubleshooting

### Common Issues

1. **"HID service not available"**
   - Ensure `ServiceLocator.Initialize()` was called
   - Check if HID devices are connected
   - Verify HID service initialization

2. **"Failed to send frame to any HID devices"**
   - Check device connection status
   - Verify real-time mode is enabled
   - Check transfer ID cycling

3. **High memory usage**
   - Reduce JPEG quality
   - Lower FPS
   - Ensure proper disposal

4. **Poor performance**
   - Check system resources
   - Optimize JPEG quality vs. size
   - Monitor transfer success rate

### Debug Information
```csharp
// Enable verbose logging
renderingService.HidStatusChanged += status => 
    Debug.WriteLine($"HID: {status}");

// Monitor frame size
renderingService.JpegDataSentToHid += jpegData => 
    Debug.WriteLine($"Frame: {jpegData.Length} bytes");

// Print statistics
renderingService.PrintStatistics(); // If using example class
```

## Example Application

See `Examples\HidRenderingServiceExample.cs` for a complete working example demonstrating:
- Service initialization
- Real-time rendering setup
- Suspend media transfer
- Error handling
- Performance monitoring
- Proper cleanup

## Thread Safety

The `BackgroundRenderingService` is designed to be thread-safe:
- Internal locking protects shared state
- Events are marshaled to appropriate threads
- HID operations are atomic
- Cancellation tokens handle clean shutdown

## Conclusion

The enhanced `BackgroundRenderingService` provides a powerful, flexible solution for sending rendered content to HID devices. Whether you need real-time streaming or batch media transfer, the service handles the complexity of device communication while providing comprehensive monitoring and error handling capabilities.