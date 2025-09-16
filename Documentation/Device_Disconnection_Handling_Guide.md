# Device Disconnection Handling - Usage Examples

## Overview
The enhanced `RealtimeJpegTransmissionService` now provides robust device disconnection handling that automatically pauses transmission when devices are disconnected and resumes when they reconnect.

## Key Features

### 1. **Automatic Pause/Resume**
- Service automatically pauses when no devices are connected
- Service automatically resumes when devices become available
- No manual intervention required

### 2. **Queue Management**
- Clears frame queue when devices disconnect (prevents stale data)
- Rejects new frames when no devices are connected
- Comprehensive logging of all operations

### 3. **Event Monitoring**
- New `DeviceConnectionChanged` event for real-time connectivity monitoring
- Integration with existing HidDeviceService events
- Application-level notifications for UI updates

## Usage Examples

### Basic Usage (No Changes Required)
```csharp
// Existing code works unchanged
var service = new RealtimeJpegTransmissionService(hidDeviceService);
service.QueueJpegData(jpegBytes, "MyFrame");

// Service automatically:
// - Pauses when devices disconnect  
// - Clears queue on disconnection
// - Resumes when devices reconnect
// - Manages real-time mode appropriately
```

### Enhanced Usage with Device Monitoring
```csharp
// Create service with monitoring
var service = new RealtimeJpegTransmissionService(
    hidDeviceService,
    targetFps: 25,
    maxQueueSize: 3,
    realtimeTimeoutSeconds: 5
);

// Monitor device connectivity changes
service.DeviceConnectionChanged += (sender, e) =>
{
    if (e.IsConnected)
    {
        Logger.Info($"Device connected: {e.Device.ProductString}");
        Logger.Info($"Total connected devices: {e.TotalConnectedDevices}");
        
        // Show "Device connected" notification
        if (e.TotalConnectedDevices == 1)
        {
            ShowNotification("Device connected - streaming resumed", NotificationLevel.Info);
        }
    }
    else
    {
        Logger.Info($"Device disconnected: {e.Device.ProductString}");
        Logger.Info($"Remaining connected devices: {e.TotalConnectedDevices}");
        
        // Show "Device disconnected" notification
        if (e.TotalConnectedDevices == 0)
        {
            ShowNotification("All devices disconnected - streaming paused", NotificationLevel.Warning);
        }
    }
};

// Monitor frame drops due to disconnection
service.FrameDropped += (sender, e) =>
{
    if (e.Reason.Contains("device") || e.Reason.Contains("connect"))
    {
        Logger.Warn($"Frame dropped due to connectivity: {e.Reason}");
    }
};

// Check service status
var stats = service.Statistics;
if (stats.IsPaused)
{
    Console.WriteLine($"Service paused - {stats.ConnectedDeviceCount} devices connected");
}
else
{
    Console.WriteLine($"Service active - {stats.ConnectedDeviceCount} devices connected");
}
```

### Application-Level Integration
```csharp
public class MediaStreamingApp
{
    private RealtimeJpegTransmissionService _jpegService;
    private bool _userStreamingEnabled = true;
    
    public void InitializeServices()
    {
        _jpegService = new RealtimeJpegTransmissionService(hidDeviceService);
        
        // Handle device connectivity for user experience
        _jpegService.DeviceConnectionChanged += OnDeviceConnectionChanged;
    }
    
    private void OnDeviceConnectionChanged(object sender, DeviceConnectionChangedEventArgs e)
    {
        // Update UI based on device connectivity
        Dispatcher.Invoke(() =>
        {
            if (e.TotalConnectedDevices == 0)
            {
                // No devices - show connection prompt
                ShowConnectionStatus("No devices connected", StatusLevel.Warning);
                DisableStreamingControls();
            }
            else
            {
                // Devices available - enable functionality
                ShowConnectionStatus($"{e.TotalConnectedDevices} device(s) connected", StatusLevel.Success);
                EnableStreamingControls();
            }
        });
    }
    
    public void StartStreaming()
    {
        if (_jpegService.ConnectedDeviceCount == 0)
        {
            ShowError("Cannot start streaming - no devices connected");
            return;
        }
        
        _userStreamingEnabled = true;
        // Start generating frames...
        Task.Run(StreamingLoop);
    }
    
    private async Task StreamingLoop()
    {
        while (_userStreamingEnabled)
        {
            var frame = GenerateFrame();
            
            // Service handles device connectivity automatically
            var success = _jpegService.QueueFrame(frame, "StreamFrame");
            
            if (!success && _jpegService.ConnectedDeviceCount == 0)
            {
                // Optional: Pause generation when no devices
                await Task.Delay(1000); // Wait before retry
                continue;
            }
            
            await Task.Delay(40); // ~25 FPS
        }
    }
}
```

## Event Flow Diagram

```
Device Disconnect ? HidDeviceService.DeviceDisconnected ? RealtimeJpegTransmissionService
                                                               ?
                              Clear Queue ? Update Pause State ? OnDeviceDisconnected
                                   ?              ?
                            Log Clear Count   Set _isPaused = true
                                   ?              ?
                         Fire FrameDropped   Fire DeviceConnectionChanged
                                   ?              ?  
                            App Event Handler ? Application UI Update

Device Connect ? HidDeviceService.DeviceConnected ? RealtimeJpegTransmissionService
                                                         ?
                   Resume Processing ? Update Pause State ? OnDeviceConnected
                          ?                   ?
                  Set _isPaused = false   Fire DeviceConnectionChanged
                          ?                   ?
                   Normal Operation    Application UI Update
```

## Benefits

### 1. **Automatic Resource Management**
- No manual pause/resume required
- Automatic queue cleanup prevents memory leaks
- Real-time mode management based on device availability

### 2. **Robust Error Handling**
- Graceful handling of device hot-plugging
- Clear logging for debugging connectivity issues  
- Prevents transmission to non-existent devices

### 3. **Enhanced User Experience**
- Real-time connectivity feedback through events
- Automatic service state management
- Clear indication of service availability

### 4. **Backward Compatibility**
- Existing code requires no changes
- All original functionality preserved
- Optional enhancements available for new code

## Testing Scenarios

### 1. **Basic Connectivity Testing**
```csharp
// Test: Start with no devices
var service = new RealtimeJpegTransmissionService(hidDeviceService);
Assert.IsTrue(service.IsPaused);
Assert.AreEqual(0, service.ConnectedDeviceCount);

// Queue frame - should be rejected
var success = service.QueueFrame(testFrame);
Assert.IsFalse(success);
```

### 2. **Device Reconnection Testing**
```csharp
// Test: Device disconnect during operation
var frameQueued = false;
service.DeviceConnectionChanged += (s, e) =>
{
    if (!e.IsConnected && e.TotalConnectedDevices == 0)
    {
        frameQueued = service.QueueFrame(testFrame);
    }
};

// Simulate device disconnect
// frameQueued should be false
Assert.IsFalse(frameQueued);
```

### 3. **Queue Clearing Testing**
```csharp
// Test: Queue clearing on disconnect
service.QueueFrame(frame1);
service.QueueFrame(frame2);
Assert.AreEqual(2, service.QueueSize);

// Simulate device disconnect
// Queue should be cleared automatically
Assert.AreEqual(0, service.QueueSize);
```

This enhanced device disconnection handling makes the `RealtimeJpegTransmissionService` much more robust and suitable for real-world applications where devices may be frequently connected and disconnected.