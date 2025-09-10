# Win2D Render Service TestDemo Usage Guide

## Overview

The TestDemo page provides a comprehensive demonstration of the SkiaSharp render service capabilities. This guide shows how to use and test all the features.

## Getting Started

### 1. Navigation to TestDemo Page

The TestDemo page should be accessible through your main navigation. If it's not already included in your navigation menu, you can navigate to it programmatically:

```csharp
// In your main window or navigation handler
MainFrame.Navigate(new Pages.TestDemo());
```

### 2. Basic Usage Steps

1. **Initialize the Service**
   - Click "Initialize Service" button
   - Select desired canvas size (256x256, 512x512, or 1024x1024)
   - Watch the service status change to "Initialized ?"

2. **Add Elements**
   - Click "Add Live CPU Usage" to add real-time CPU monitoring
   - Click "Add Live GPU Usage" to add real-time GPU monitoring  
   - Click "Add Live Date/Time" to add current date/time display
   - Click "Add Custom Text" to add static text elements
   - Click "Add Background Image" to add images (tries Resources/background.png first)

3. **Start Real-time Rendering**
   - Click "Start Real-time (30 FPS)" to begin live rendering
   - Watch the preview update in real-time
   - Monitor FPS counter and frame count
   - View live system metrics on the right panel

4. **Export Results**
   - Click "Export to PNG" to save current frame
   - Click "Save Debug Frames" to save next 10 frames for analysis

## Features Demonstrated

### Core Rendering Features
- ? **Real-time 2D rendering** at configurable FPS
- ? **Live system metrics** (CPU, GPU, Memory usage)
- ? **Dynamic text elements** with automatic updates
- ? **Image rendering** with opacity and scaling
- ? **Multi-threaded rendering** with UI thread synchronization

### Interactive Controls
- ? **Canvas size selection** (256, 512, 1024 pixels)
- ? **Background color changing** (Black, Blue, White)
- ? **Element management** (Add/Remove elements dynamically)
- ? **Real-time preview** with live updates
- ? **Performance monitoring** (FPS, frame count)

### Export Capabilities
- ? **PNG export** with file dialog
- ? **Debug frame capture** for analysis
- ? **Automatic timestamping** for exported files

## Advanced Usage

### Custom Integration Example

```csharp
// Example: Add TestDemo to your existing DeviceConfigPage
private TestDemo? _testDemo;

private void ShowRenderDemo_Click(object sender, RoutedEventArgs e)
{
    if (_testDemo == null)
    {
        _testDemo = new TestDemo();
    }
    
    // Navigate to demo or show in popup
    MainFrame.Navigate(_testDemo);
}

// Example: Use render service directly
private void SetupCustomRendering()
{
    var renderService = new SkiaRenderService();
    var metricsService = new RealSystemMetricsService();
    var helper = new RenderIntegrationHelper(renderService, metricsService);
    
    helper.Initialize(512);
    
    // Add elements programmatically
    helper.AddLiveCpuUsage("cpu1", new SKPoint(10, 10), 18);
    helper.AddStaticText("title", "Custom Display", new SKPoint(100, 10), 24, Colors.White);
    
    // Start real-time output
    helper.StartRealtimeOutput(30, OnFrameReady);
}

private void OnFrameReady(object sender, RenderOutputEventArgs e)
{
    // Send frame data to device or process further
    // e.FrameData contains PNG-encoded image data
    SendToDevice(e.FrameData);
}
```

### Device Integration Pattern

```csharp
public class DeviceRenderManager
{
    private readonly RenderIntegrationHelper _helper;
    private readonly IDeviceService _deviceService;
    
    public DeviceRenderManager(IDeviceService deviceService)
    {
        _deviceService = deviceService;
        
        var renderService = new SkiaRenderService();
        var metricsService = new RealSystemMetricsService();
        _helper = new RenderIntegrationHelper(renderService, metricsService);
    }
    
    public void StartDeviceDisplay(int canvasSize = 512)
    {
        // Initialize with device resolution
        _helper.Initialize(canvasSize);
        
        // Setup device-specific layout
        SetupDeviceLayout();
        
        // Start real-time rendering
        _helper.StartRealtimeOutput(30, OnDeviceFrameReady);
    }
    
    private void SetupDeviceLayout()
    {
        // Add system monitoring elements
        _helper.AddLiveCpuUsage("cpu", new SKPoint(10, 10), 16);
        _helper.AddLiveGpuUsage("gpu", new SKPoint(10, 30), 16);
        _helper.AddLiveDateTime("time", new SKPoint(10, 60), 14);
        
        // Add device-specific info
        _helper.AddStaticText("device_name", _deviceService.DeviceName, 
            new SKPoint(10, 100), 18, Colors.Cyan);
    }
    
    private void OnDeviceFrameReady(object sender, RenderOutputEventArgs e)
    {
        try
        {
            // Send rendered frame to device
            _deviceService.SendFrame(e.FrameData);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Device frame send failed: {ex.Message}");
        }
    }
}
```

## Performance Tips

### Optimal Settings
- **Canvas Size**: Use device resolution (typically 512x512 or smaller)
- **FPS**: 30 FPS for smooth animation, 10-15 FPS for static content
- **Element Count**: Limit to essential elements for best performance
- **Update Frequency**: Update live data every 1-2 seconds

### Memory Management
- Always dispose services when done
- Remove unused elements to free memory
- Monitor frame count for memory leaks

### Debugging
- Use "Save Debug Frames" to analyze rendering output
- Monitor log output for errors and performance info
- Check FPS counter for rendering performance

## Common Use Cases

### 1. Device Status Display
```csharp
// Setup device monitoring display
helper.AddLiveCpuUsage("cpu", new SKPoint(10, 10), 16);
helper.AddLiveGpuUsage("gpu", new SKPoint(10, 30), 16);
helper.AddStaticText("status", "Device Online", new SKPoint(10, 60), 14, Colors.Green);
```

### 2. System Dashboard
```csharp
// Create system overview
helper.AddLiveDateTime("time", new SKPoint(200, 10), 20);
helper.AddLiveCpuUsage("cpu", new SKPoint(10, 50), 18);
helper.AddLiveGpuUsage("gpu", new SKPoint(10, 80), 18);
helper.AddStaticText("title", "System Dashboard", new SKPoint(50, 120), 24, Colors.White);
```

### 3. Custom Animations
```csharp
// Add animated elements
var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
var angle = 0.0f;

timer.Tick += (s, e) =>
{
    var x = 256 + (float)(Math.Cos(angle) * 100);
    var y = 256 + (float)(Math.Sin(angle) * 100);
    
    renderService.UpdateElementPosition("moving_text", new SKPoint(x, y));
    angle += 0.1f;
};
timer.Start();
```

## Troubleshooting

### Common Issues
1. **Service not initializing**: Check SkiaSharp package installation
2. **No preview update**: Ensure real-time rendering is started
3. **Performance issues**: Reduce FPS or canvas size
4. **Export failures**: Check file permissions and disk space

### Debug Steps
1. Check log output in the demo for error messages
2. Verify system metrics are updating
3. Test with smaller canvas sizes first
4. Monitor memory usage during extended runs

This TestDemo provides a complete testing environment for the Win2D render service and serves as a reference implementation for integrating real-time 2D rendering into your device management application.