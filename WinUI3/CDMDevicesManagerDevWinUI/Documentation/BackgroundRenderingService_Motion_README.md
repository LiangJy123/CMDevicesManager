# BackgroundRenderingService Motion Enhancement

## Overview

The BackgroundRenderingService has been enhanced with comprehensive motion support, allowing you to create animated elements with various movement patterns. This enhancement maintains full backward compatibility while adding powerful animation capabilities.

## New Motion Features

### Motion Types

1. **Linear** - Elements move in a straight line with constant velocity
2. **Circular** - Elements rotate around a center point
3. **Oscillate** - Elements move back and forth along a direction
4. **Bounce** - Elements bounce off canvas boundaries
5. **Spiral** - Elements follow an expanding spiral pattern
6. **Random** - Elements move with random direction changes

### Motion Properties

- **Speed**: Controls how fast the element moves
- **Direction**: Vector determining movement direction (for linear motion)
- **Center**: Center point for circular/oscillating motion
- **Radius**: Radius for circular motion or amplitude for oscillation
- **RespectBoundaries**: Whether to keep elements within canvas bounds
- **ShowTrail**: Whether to display a motion trail effect

## Usage Examples

### Basic Setup

```csharp
// Initialize the service
var renderingService = new BackgroundRenderingService();
await renderingService.InitializeAsync();
await renderingService.StartRenderingAsync();
```

### Adding Moving Elements

#### 1. Bouncing Ball

```csharp
// Create a bouncing ball that moves at 150 pixels/second
var ballId = renderingService.AddBouncingBall(
    position: new Vector2(100, 100),
    radius: 20f,
    speed: 150f,
    color: Microsoft.UI.Colors.Orange
);
```

#### 2. Rotating Text

```csharp
// Create text that rotates around a center point
var rotatingTextId = renderingService.AddRotatingText(
    text: "Rotating Text",
    center: new Vector2(240, 240),  // Center of canvas
    radius: 80f,
    speed: 1f,  // 1 rotation per second
    color: Microsoft.UI.Colors.Cyan
);
```

#### 3. Pulsating Text

```csharp
// Create text that oscillates horizontally
var pulsatingTextId = renderingService.AddPulsatingText(
    text: "Pulsating",
    center: new Vector2(240, 150),
    amplitude: 30f,
    speed: 2f,  // 2 oscillations per second
    color: Microsoft.UI.Colors.LimeGreen
);
```

#### 4. Spiral Motion

```csharp
// Create text that follows a spiral pattern
var spiralTextId = renderingService.AddSpiralText(
    text: "Spiral",
    center: new Vector2(240, 350),
    initialRadius: 20f,
    speed: 1f,
    color: Microsoft.UI.Colors.Magenta
);
```

#### 5. Linear Movement with Trail

```csharp
// Create text that moves linearly with a trail effect
var linearTextId = renderingService.AddLinearMovingText(
    text: "Linear Motion",
    position: new Vector2(50, 200),
    direction: new Vector2(1, 0.5f),  // Move right and slightly down
    speed: 100f,
    showTrail: true,
    color: Microsoft.UI.Colors.Yellow
);
```

### Advanced Motion Configuration

For more control, you can use the detailed motion configuration:

```csharp
// Create custom motion configuration
var motionConfig = new ElementMotionConfig
{
    MotionType = MotionType.Circular,
    Speed = 2f,
    Center = new Vector2(240, 240),
    Radius = 100f,
    RespectBoundaries = false,
    ShowTrail = true
};

// Add text with custom motion
var customTextId = renderingService.AddTextElementWithMotion(
    text: "Custom Motion",
    position: new Vector2(240, 240),
    motionConfig: motionConfig
);

// Or add a circle with motion
var customCircleId = renderingService.AddCircleElementWithMotion(
    position: new Vector2(150, 150),
    radius: 15f,
    color: Microsoft.UI.Colors.Red,
    motionConfig: new ElementMotionConfig
    {
        MotionType = MotionType.Bounce,
        Speed = 200f,
        Direction = new Vector2(1, -1),
        RespectBoundaries = true
    }
);
```

### Runtime Motion Control

```csharp
// Change motion properties at runtime
renderingService.SetElementMotion(elementId, new ElementMotionConfig
{
    MotionType = MotionType.Linear,
    Speed = 150f,
    Direction = new Vector2(0, 1)  // Move downward
});

// Update just the speed
renderingService.UpdateElementSpeed(elementId, 200f);

// Update just the direction
renderingService.UpdateElementDirection(elementId, new Vector2(-1, 0));

// Stop motion
renderingService.StopElementMotion(elementId);

// Get current motion info
var motionInfo = renderingService.GetElementMotion(elementId);
if (motionInfo != null)
{
    Console.WriteLine($"Motion Type: {motionInfo.MotionType}, Speed: {motionInfo.Speed}");
}
```

### Element Management

```csharp
// Get information about all elements
var elementsInfo = renderingService.GetElementsInfo();
foreach (var element in elementsInfo)
{
    Console.WriteLine($"Element {element.Id}: {element.Type}, Moving: {element.HasMotion}");
}

// Get motion statuses
var motionStatuses = renderingService.GetElementMotionStatuses();
var movingCount = motionStatuses.Values.Count(x => x);
Console.WriteLine($"Moving elements: {movingCount}/{motionStatuses.Count}");

// Clear all elements
renderingService.ClearElements();
```

## Events and Monitoring

The service provides events to monitor rendering and motion:

```csharp
// Monitor status changes
renderingService.StatusChanged += (sender, message) =>
{
    Console.WriteLine($"Status: {message}");
};

// Monitor pixel frame updates
renderingService.PixelFrameReady += (sender, frameArgs) =>
{
    // Frame data is available for processing
    Console.WriteLine($"Frame ready: {frameArgs.Width}x{frameArgs.Height}");
};

// Monitor HID frame transmission (if enabled)
renderingService.HidFrameSent += (sender, hidArgs) =>
{
    Console.WriteLine($"HID frame sent: {hidArgs.FrameSize} bytes to {hidArgs.DevicesSucceeded} devices");
};
```

## Performance Considerations

1. **Frame Rate**: Use appropriate FPS for your needs (15-30 FPS for smooth motion)
2. **Element Count**: More moving elements require more processing
3. **Motion Trails**: Trail effects use additional memory for position history
4. **Boundary Checking**: Can impact performance with many bouncing elements

```csharp
// Configure performance settings
renderingService.SetTargetFps(30);  // 30 FPS for smooth animation
renderingService.SetRenderSize(480, 480);  // Optimize canvas size
```

## Integration with HID Devices

Motion-enhanced rendering works seamlessly with HID real-time streaming:

```csharp
// Start rendering with HID real-time mode
await renderingService.StartRenderingAsync(enableHidRealTime: true);

// Configure JPEG quality for HID transmission
renderingService.JpegQuality = 75;  // Balance quality vs. transmission speed

// The animated content will be automatically streamed to connected HID devices
```

## Backward Compatibility

All existing methods continue to work without changes:

```csharp
// Existing static element methods still work
var textId = renderingService.AddSimpleText("Static Text");
var liveTextId = renderingService.AddLiveTextElement("Live Text", new Vector2(100, 100));

// Elements can be upgraded to have motion later
renderingService.SetElementMotion(textId, new ElementMotionConfig
{
    MotionType = MotionType.Circular,
    Speed = 1f,
    Center = new Vector2(240, 240),
    Radius = 50f
});
```

## Best Practices

1. **Use appropriate motion types** for your content (bouncing for game-like elements, circular for indicators)
2. **Limit trail effects** to important elements to preserve performance
3. **Consider boundary behavior** when elements reach canvas edges
4. **Start with lower speeds** and adjust based on visual preference
5. **Monitor element count** - remove unused elements to optimize performance
6. **Use events** to respond to motion state changes and frame updates

## Example: Complete Motion Demo

```csharp
public async Task CreateMotionDemo()
{
    var service = new BackgroundRenderingService();
    await service.InitializeAsync();
    
    // Add various motion types
    service.AddBouncingBall(new Vector2(100, 100), 15f, 120f, Microsoft.UI.Colors.Red);
    service.AddRotatingText("Orbit", new Vector2(240, 240), 60f, 1.5f, Microsoft.UI.Colors.Blue);
    service.AddPulsatingText("Pulse", new Vector2(240, 100), 25f, 3f, Microsoft.UI.Colors.Green);
    service.AddSpiralText("Spiral", new Vector2(240, 380), 15f, 0.8f, Microsoft.UI.Colors.Purple);
    service.AddLinearMovingText("Linear", new Vector2(50, 200), new Vector2(1, 0), 80f, true, Microsoft.UI.Colors.Orange);
    
    // Start rendering
    await service.StartRenderingAsync();
    
    // The demo will show multiple elements with different motion patterns
}
```

This enhanced BackgroundRenderingService provides a powerful foundation for creating engaging animated content that can be rendered to UI elements or streamed to HID devices in real-time.