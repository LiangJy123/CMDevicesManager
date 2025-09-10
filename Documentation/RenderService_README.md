# SkiaSharp Render Service Documentation

## Overview

The SkiaSharp Render Service provides a powerful 2D rendering solution for the CMDevicesManager WPF application. It allows you to create dynamic graphics with text and images that can be rendered in real-time and sent to devices or exported as images.

## Key Features

- **Real-time Rendering**: Render frames at configurable FPS (1-120 FPS)
- **Text Support**: Add dynamic text with live system metrics
- **Image Support**: Add and manipulate images with transformations
- **Live Updates**: Automatically update CPU/GPU usage, date/time, and other metrics
- **Export Capabilities**: Export frames as PNG images
- **WPF Integration**: Convert rendered frames to WPF BitmapSource for display
- **Thread-Safe**: Concurrent operations support for smooth performance

## Architecture

### Core Components

1. **IRenderService**: Main interface for rendering operations
2. **SkiaRenderService**: Implementation using SkiaSharp
3. **RenderIntegrationHelper**: WPF integration helper
4. **RenderElement**: Base class for renderable elements
5. **ImageElement & TextElement**: Specific element implementations

### Dependencies

- SkiaSharp 2.88.6
- SkiaSharp.Views.WPF 2.88.6
- System.Drawing.Common 8.0.0

## Quick Start

### 1. Basic Setup

```csharp
// Initialize services
var renderService = new SkiaRenderService();
var metricsService = new RealSystemMetricsService();
var helper = new RenderIntegrationHelper(renderService, metricsService);

// Initialize with 512x512 canvas
helper.Initialize(512);
```

### 2. Adding Elements

```csharp
// Add live CPU usage text
helper.AddLiveCpuUsage("cpu_usage", new SKPoint(50, 50), 18);

// Add live GPU usage text
helper.AddLiveGpuUsage("gpu_usage", new SKPoint(50, 80), 18);

// Add live date/time
helper.AddLiveDateTime("datetime", new SKPoint(50, 110), 20);

// Add static text
helper.AddStaticText("title", "Device Monitor", new SKPoint(200, 50), 24, Colors.White);

// Add background image
helper.AddImageFromFile("background", "path/to/image.png", 
    new SKPoint(0, 0), new SKSize(512, 512), 0.3f);
```

### 3. Real-time Rendering

```csharp
// Start real-time rendering at 30 FPS
helper.StartRealtimeOutput(30, OnRenderReady);

// Handle render output
private void OnRenderReady(object sender, RenderOutputEventArgs e)
{
    // Send frame data to device or process further
    // e.FrameData contains PNG-encoded image data
    // e.RenderedImage contains SKImage for further processing
}

// Stop rendering
helper.StopRealtimeOutput();
```

## API Reference

### IRenderService Interface

#### Properties
- `SKSizeI CanvasSize`: Canvas dimensions
- `SKColor BackgroundColor`: Background color
- `bool IsRealtimeRenderingActive`: Realtime rendering status

#### Methods

**Element Management:**
- `AddImage(id, imageData, position, size, opacity, rotation)`: Add image element
- `AddText(id, text, position, fontSize, color, fontFamily, opacity, rotation)`: Add text element
- `UpdateElementPosition(id, position)`: Update element position
- `UpdateElementOpacity(id, opacity)`: Update element opacity
- `UpdateElementRotation(id, rotation)`: Update element rotation
- `UpdateTextContent(id, text)`: Update text content
- `UpdateTextColor(id, color)`: Update text color
- `RemoveElement(id)`: Remove element
- `ClearAll()`: Clear all elements

**Rendering:**
- `RenderFrame()`: Render current frame
- `GetRenderedFrameData()`: Get frame as byte array (PNG)
- `GetRenderedImage()`: Get frame as SKImage
- `StartRealtimeRendering(fps)`: Start realtime rendering
- `StopRealtimeRendering()`: Stop realtime rendering

### RenderIntegrationHelper

#### WPF Integration Methods
- `GetWpfBitmap()`: Convert rendered frame to WPF BitmapSource
- `ConvertColor(wpfColor)`: Convert WPF Color to SKColor
- `ExportToPng(path)`: Export frame to PNG file

#### Live Data Methods
- `AddLiveCpuUsage(id, position, fontSize)`: Add live CPU usage text
- `AddLiveGpuUsage(id, position, fontSize)`: Add live GPU usage text
- `AddLiveDateTime(id, position, fontSize)`: Add live date/time text
- `UpdateLiveData()`: Update all live elements

## Integration with DeviceConfigPage

### Method 1: Direct Integration

```csharp
public partial class DeviceConfigPage : Page
{
    private RenderServiceExample? _renderExample;
    
    private void InitializeRenderService()
    {
        _renderExample = this.CreateRenderService();
        _renderExample.StartLiveUpdates();
    }
    
    private void ExportWithRenderService()
    {
        var outputPath = Path.Combine(OutputFolder, "rendered_output.png");
        _renderExample?.ExportFrame(outputPath);
    }
}
```

### Method 2: Background Rendering

```csharp
// Setup background rendering that mirrors the WPF canvas
private void SetupBackgroundRender()
{
    var renderService = new SkiaRenderService();
    var helper = new RenderIntegrationHelper(renderService, _metrics);
    
    helper.Initialize(CanvasSize);
    helper.SetBackgroundColor(BackgroundColor);
    
    // Mirror WPF elements to render service
    foreach (UIElement element in DesignCanvas.Children)
    {
        if (element is Border border && border.Child is TextBlock text)
        {
            var position = new SKPoint(
                (float)Canvas.GetLeft(border),
                (float)Canvas.GetTop(border)
            );
            helper.AddStaticText(border.Name ?? Guid.NewGuid().ToString(), 
                text.Text, position, (float)text.FontSize, 
                ((SolidColorBrush)text.Foreground).Color);
        }
    }
    
    // Start realtime output for device transmission
    helper.StartRealtimeOutput(30, SendToDevice);
}

private void SendToDevice(object sender, RenderOutputEventArgs e)
{
    // Send e.FrameData to your device here
    // This is called 30 times per second with fresh rendered data
}
```

## Performance Considerations

### Optimization Tips

1. **Element Management**: Remove unused elements to free memory
2. **Image Handling**: Dispose of bitmaps when removing image elements
3. **Render Frequency**: Use appropriate FPS for your use case (30 FPS for smooth animation, lower for static content)
4. **Canvas Size**: Use appropriate canvas size for your device resolution
5. **Live Updates**: Update live data at reasonable intervals (1-2 seconds for system metrics)

### Memory Management

```csharp
// Always dispose when done
using var renderService = new SkiaRenderService();
using var helper = new RenderIntegrationHelper(renderService, metricsService);

// Or explicitly dispose
helper.Dispose();
renderService.Dispose();
```

## Error Handling

The render service includes comprehensive error handling:

- **Invalid image data**: Throws `InvalidOperationException` with details
- **Disposed service**: Throws `ObjectDisposedException`
- **Render errors**: Individual element errors are logged but don't stop rendering
- **File operations**: File not found and access errors are properly handled

## Examples

### Live System Monitor

```csharp
var example = new RenderServiceExample();
example.SetupElements(); // Adds CPU, GPU, DateTime
example.StartLiveUpdates(); // Starts 1-second update timer
example.StartRealtime(30); // 30 FPS rendering

// Export snapshot
example.ExportFrame("system_monitor.png");
```

### Custom Device Display

```csharp
var renderService = new SkiaRenderService();
renderService.Initialize();

// Setup custom layout
renderService.AddText("temp", "CPU: 45°C", new SKPoint(10, 10), 16, SKColors.Green);
renderService.AddText("usage", "Usage: 75%", new SKPoint(10, 30), 16, SKColors.Yellow);

// Add device-specific graphics
var deviceIcon = File.ReadAllBytes("device_icon.png");
renderService.AddImage("icon", deviceIcon, new SKPoint(100, 10), new SKSize(32, 32));

// Render and get data for device transmission
renderService.RenderFrame();
var frameData = renderService.GetRenderedFrameData();
// Send frameData to device...
```

## Troubleshooting

### Common Issues

1. **SkiaSharp not found**: Ensure NuGet packages are properly installed
2. **Image loading fails**: Check image format support (PNG, JPEG, BMP, GIF)
3. **Performance issues**: Reduce FPS or canvas size, optimize element count
4. **Memory leaks**: Ensure proper disposal of services and elements

### Debug Tips

- Use `System.Diagnostics.Debug.WriteLine()` in render callbacks
- Check `IsRealtimeRenderingActive` property
- Monitor memory usage with element count
- Test with smaller canvas sizes first

## Future Enhancements

Potential improvements for future versions:

- Animation support with keyframes
- Vector graphics support
- GPU acceleration options
- More image transformation options
- Video output capabilities
- Network streaming support