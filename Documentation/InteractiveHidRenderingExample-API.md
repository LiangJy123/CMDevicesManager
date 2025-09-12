# Interactive Win2D Rendering Service API

This document provides a comprehensive overview of the `InteractiveWin2DRenderingService` public API for creating interactive Win2D graphics with HID device integration, motion elements, and background management.

## Overview

The `InteractiveWin2DRenderingService` is a comprehensive graphics rendering service that provides:
- **Interactive Win2D Rendering**: High-performance 2D graphics with Win2D
- **Real-time HID Streaming**: Automatic streaming to connected HID devices
- **Motion System**: Advanced element animation with multiple motion types
- **Background Management**: Flexible background configuration (solid, gradient, image)
- **Scene Export/Import**: Complete scene serialization to JSON
- **Element Management**: Interactive draggable elements with hit testing

## Public API Reference

### Core Initialization

#### `InitializeAsync(int width = 800, int height = 600)`
Initializes the rendering service with specified canvas dimensions.

**Parameters:**
- `width`: Canvas width in pixels (default: 800)
- `height`: Canvas height in pixels (default: 600)

**Example:**
```csharp
var service = new InteractiveWin2DRenderingService();
await service.InitializeAsync(1920, 1080);
```

---

## Background Management API

### Basic Background Configuration

#### `SetBackgroundColorAsync(WinUIColor color, float opacity = 1.0f)`
Sets a solid color background.

**Parameters:**
- `color`: Background color (WinUI Color)
- `opacity`: Background opacity (0.0-1.0, default: 1.0)

**Example:**
```csharp
await service.SetBackgroundColorAsync(
    WinUIColor.FromArgb(255, 30, 144, 255), // DodgerBlue
    opacity: 0.8f
);
```

#### `SetBackgroundGradientAsync(WinUIColor startColor, WinUIColor endColor, BackgroundGradientDirection direction = BackgroundGradientDirection.TopToBottom, float opacity = 1.0f)`
Sets a gradient background with customizable direction.

**Parameters:**
- `startColor`: Gradient start color
- `endColor`: Gradient end color  
- `direction`: Gradient direction (TopToBottom, LeftToRight, Diagonal, RadialFromCenter)
- `opacity`: Background opacity (0.0-1.0)

**Example:**
```csharp
await service.SetBackgroundGradientAsync(
    WinUIColor.FromArgb(255, 255, 0, 0),   // Red
    WinUIColor.FromArgb(255, 0, 0, 255),   // Blue
    BackgroundGradientDirection.DiagonalTopLeftToBottomRight,
    opacity: 0.9f
);
```

### Image Background Management

#### `SetBackgroundImageAsync(string imagePath, BackgroundScaleMode scaleMode = BackgroundScaleMode.Stretch, float opacity = 1.0f)`
Sets a background image from file path.

**Parameters:**
- `imagePath`: Path to image file
- `scaleMode`: How to scale the image (None, Stretch, Uniform, UniformToFill)
- `opacity`: Background opacity (0.0-1.0)

**Example:**
```csharp
await service.SetBackgroundImageAsync(
    @"C:\Images\background.jpg",
    BackgroundScaleMode.UniformToFill,
    opacity: 0.7f
);
```

#### `SetBackgroundImageAsync(byte[] imageData, BackgroundScaleMode scaleMode = BackgroundScaleMode.Stretch, float opacity = 1.0f)`
Sets a background image from byte array.

**Parameters:**
- `imageData`: Image data as byte array
- `scaleMode`: How to scale the image
- `opacity`: Background opacity (0.0-1.0)

**Example:**
```csharp
byte[] imageBytes = File.ReadAllBytes(@"C:\Images\bg.png");
await service.SetBackgroundImageAsync(
    imageBytes, 
    BackgroundScaleMode.Uniform
);
```

### Background Utility Methods

#### `ClearBackgroundAsync()`
Clears background to transparent.

#### `ResetBackgroundToDefaultAsync()`
Resets background to default gradient.

#### `SetBackgroundScaleMode(BackgroundScaleMode scaleMode)`
Updates background image scale mode.

#### `SetBackgroundOpacity(float opacity)`
Updates background opacity.

#### `GetBackgroundInfo()`
**Returns:** `string` - Background information summary

---

## Motion System API

### Motion Element Creation

#### `AddTextElementWithMotion(string text, Point position, ElementMotionConfig? motionConfig = null, TextElementConfig? textConfig = null)`
Creates a text element with motion capabilities.

**Parameters:**
- `text`: Text content to display
- `position`: Initial position
- `motionConfig`: Motion configuration (optional)
- `textConfig`: Text styling configuration (optional)

**Returns:** `int` - Element index

**Example:**
```csharp
var motionConfig = new ElementMotionConfig
{
    MotionType = MotionType.Circular,
    Speed = 2.0f,
    Radius = 100f,
    ShowTrail = true
};

int elementIndex = service.AddTextElementWithMotion(
    "Flying Text!", 
    new Point(100, 100), 
    motionConfig
);
```

#### `AddCircleElementWithMotion(Point position, float radius, WinUIColor color, ElementMotionConfig? motionConfig = null)`
Creates a circle element with motion capabilities.

**Parameters:**
- `position`: Initial position
- `radius`: Circle radius
- `color`: Fill color
- `motionConfig`: Motion configuration (optional)

**Returns:** `int` - Element index

**Example:**
```csharp
var bounceConfig = new ElementMotionConfig
{
    MotionType = MotionType.Bounce,
    Speed = 150f,
    RespectBoundaries = true,
    ShowTrail = true,
    TrailLength = 15
};

int circleIndex = service.AddCircleElementWithMotion(
    new Point(200, 200), 
    30f, 
    WinUIColor.FromArgb(180, 255, 100, 100), 
    bounceConfig
);
```

#### `AddRectangleElementWithMotion(Point position, Size size, WinUIColor color, ElementMotionConfig? motionConfig = null)`
Creates a rectangle element with motion capabilities.

#### `AddImageElementWithMotion(string imagePath, Point position, ElementMotionConfig? motionConfig = null, ImageElementConfig? imageConfig = null)`
Creates an image element with motion capabilities.

### Motion Control Methods

#### `SetElementMotion(RenderElement element, ElementMotionConfig motionConfig)`
Updates motion configuration for an existing element.

#### `PauseElementMotion(RenderElement element)`
Pauses motion for a specific element.

#### `ResumeElementMotion(RenderElement element)`
Resumes motion for a specific element.

#### `PauseAllMotion()`
Pauses motion for all motion-enabled elements.

#### `ResumeAllMotion()`
Resumes motion for all motion-enabled elements.

---

## Auto Rendering Control API

#### `StartAutoRendering(int fps = 30)`
Starts automatic rendering with built-in render tick.

**Parameters:**
- `fps`: Target frames per second (1-120, default: 30)

**Example:**
```csharp
service.StartAutoRendering(60); // 60 FPS rendering
```

#### `StopAutoRendering()`
Stops automatic rendering.

#### `SetAutoRenderingFPS(int fps)`
Updates the FPS for auto rendering during runtime.

**Parameters:**
- `fps`: New target FPS (1-120)

---

## HID Device Integration API

#### `EnableHidTransfer(bool enable, bool useSuspendMedia = false)`
Enables or disables HID transfer functionality.

**Parameters:**
- `enable`: Whether to enable HID transfer
- `useSuspendMedia`: Whether to use suspend media mode

**Example:**
```csharp
await service.EnableHidTransfer(true, useSuspendMedia: false);
```

#### `EnableHidRealTimeDisplayAsync(bool enable)`
Enables real-time display mode on HID devices.

**Parameters:**
- `enable`: Whether to enable real-time mode

**Returns:** `Task<bool>` - Success status

**Example:**
```csharp
bool success = await service.EnableHidRealTimeDisplayAsync(true);
```

#### `SendCurrentFrameAsSuspendMediaAsync()`
Sends current frame to HID devices using suspend media.

**Returns:** `Task<bool>` - Success status

#### `SetSuspendModeAsync()`
Sets HID devices to suspend mode.

**Returns:** `Task<bool>` - Success status

#### `ResetHidFrameCounter()`
Resets the HID frame counter to zero.

---

## Element Management API

#### `AddElement(RenderElement element)`
Adds an element to the rendering canvas.

#### `RemoveElement(RenderElement element)`
Removes an element from the rendering canvas.

#### `ClearElements()`
Removes all elements from the canvas.

#### `GetElements()`
**Returns:** `List<RenderElement>` - All current elements

#### `HitTest(Point point)`
Performs hit testing to find element at specified point.

**Returns:** `RenderElement?` - Hit element or null

#### `SelectElement(RenderElement? element)`
Selects an element (shows selection indicators).

#### `MoveElement(RenderElement element, Point newPosition)`
Moves an element to a new position (if draggable).

#### `LoadImageAsync(string imagePath)`
Loads an image for use in image elements.

**Returns:** `Task<string>` - Image key for referencing

---

## Rendering and Output API

#### `RenderFrameAsync()`
Renders the current frame to a WritableBitmap.

**Returns:** `Task<WriteableBitmap?>` - Rendered frame

#### `GetRenderedImageBytes()`
Gets the raw image bytes from the last render.

**Returns:** `byte[]?` - Raw BGRA pixel data

#### `SaveRenderedImageAsync(string filePath)`
Saves the current rendered frame to a file.

**Parameters:**
- `filePath`: Output file path

---

## Scene Export/Import API

#### `ExportSceneToJsonAsync()`
Exports the complete scene state to JSON.

**Returns:** `Task<string>` - JSON scene data

**Example:**
```csharp
string sceneJson = await service.ExportSceneToJsonAsync();
File.WriteAllText("my_scene.json", sceneJson);
```

#### `ImportSceneFromJsonAsync(string jsonData)`
Imports scene state from JSON data.

**Parameters:**
- `jsonData`: JSON string containing scene data

**Returns:** `Task<bool>` - Success status

**Example:**
```csharp
string sceneJson = File.ReadAllText("my_scene.json");
bool success = await service.ImportSceneFromJsonAsync(sceneJson);
```

#### `GetSceneInfoAsync()`
Gets formatted scene information string.

**Returns:** `Task<string>` - Detailed scene information

---

## Background Settings Export/Import API

#### `ExportBackgroundSettingsToJsonAsync()`
Exports only background settings to JSON.

**Returns:** `Task<string>` - JSON background settings

#### `ImportBackgroundSettingsFromJsonAsync(string jsonData, bool applyImageData = true)`
Imports background settings from JSON.

**Parameters:**
- `jsonData`: JSON string containing background settings
- `applyImageData`: Whether to apply embedded image data

**Returns:** `Task<bool>` - Success status

#### `GetBackgroundSettingsInfoAsync()`
Gets detailed background settings information.

**Returns:** `Task<string>` - Formatted background information

#### `CreateBackgroundPresetAsync(string presetName, string? description = null)`
Creates a background preset and saves to JSON.

**Parameters:**
- `presetName`: Name for the preset
- `description`: Optional description

**Returns:** `Task<string>` - JSON preset data

#### `ApplyBackgroundPresetAsync(string presetJsonData)`
Applies a background preset from JSON.

**Parameters:**
- `presetJsonData`: JSON data containing background preset

**Returns:** `Task<bool>` - Success status

---

## Properties API

### Canvas Properties
- `int Width` (get) - Canvas width
- `int Height` (get) - Canvas height
- `RenderElement? SelectedElement` (get) - Currently selected element

### Display Options
- `bool ShowTime` (get/set) - Show time element
- `bool ShowDate` (get/set) - Show date element  
- `bool ShowSystemInfo` (get/set) - Show system info element
- `bool ShowAnimation` (get/set) - Show animated elements

### Rendering Properties
- `int TargetFPS` (get/set) - Target frames per second
- `bool IsAutoRenderingEnabled` (get) - Auto rendering status
- `TimeSpan RenderInterval` (get) - Current render interval

### HID Properties
- `bool SendToHidDevices` (get/set) - HID transfer enabled
- `bool UseSuspendMedia` (get/set) - Use suspend media mode
- `int JpegQuality` (get/set) - JPEG quality (1-100)
- `bool IsHidServiceConnected` (get) - HID service connection status
- `bool IsHidRealTimeModeEnabled` (get) - Real-time mode status
- `int HidFramesSent` (get) - Number of frames sent to HID devices

### Background Properties
- `BackgroundType BackgroundType` (get) - Current background type
- `WinUIColor BackgroundColor` (get) - Background color
- `WinUIColor BackgroundGradientEndColor` (get) - Gradient end color
- `BackgroundScaleMode BackgroundScaleMode` (get) - Image scale mode
- `float BackgroundOpacity` (get) - Background opacity
- `BackgroundGradientDirection GradientDirection` (get) - Gradient direction
- `string? BackgroundImagePath` (get) - Background image path
- `bool HasBackgroundImage` (get) - Has background image loaded

---

## Events API

### Rendering Events
- `Action<WriteableBitmap>? ImageRendered` - Fired when frame is rendered
- `Action<Exception>? RenderingError` - Fired when rendering error occurs

### Element Interaction Events  
- `Action<RenderElement>? ElementSelected` - Fired when element is selected
- `Action<RenderElement>? ElementMoved` - Fired when element is moved

### HID Events
- `Action<byte[]>? RawImageDataReady` - Fired when raw image data is ready
- `Action<byte[]>? JpegDataSentToHid` - Fired when JPEG sent to HID devices
- `Action<string>? HidStatusChanged` - Fired when HID status changes

### Background Events
- `Action<string>? BackgroundChanged` - Fired when background changes

---

## Motion Types and Configuration

### ElementMotionConfig Properties
```csharp
public class ElementMotionConfig
{
    public MotionType MotionType { get; set; } = MotionType.None;
    public float Speed { get; set; } = 100.0f;          // pixels/sec or radians/sec
    public Vector2 Direction { get; set; } = Vector2.Zero;
    public Vector2 Center { get; set; } = Vector2.Zero;
    public float Radius { get; set; } = 50.0f;
    public bool RespectBoundaries { get; set; } = false;
    public bool ShowTrail { get; set; } = false;
    public int TrailLength { get; set; } = 20;
    public bool IsPaused { get; set; } = false;
}
```

### Motion Types Available
- `MotionType.None` - No motion
- `MotionType.Linear` - Straight-line movement
- `MotionType.Circular` - Circular rotation around center
- `MotionType.Bounce` - Physics-based bouncing with boundary collision
- `MotionType.Oscillate` - Back-and-forth oscillation
- `MotionType.Spiral` - Expanding circular motion
- `MotionType.Random` - Random direction changes
- `MotionType.Wave` - Horizontal movement with vertical wave
- `MotionType.Orbit` - Circular with radius variation

---

## Background Types and Configuration

### BackgroundType Enum
- `BackgroundType.Transparent` - No background
- `BackgroundType.SolidColor` - Single solid color
- `BackgroundType.Gradient` - Color gradient
- `BackgroundType.Image` - Background image

### BackgroundScaleMode Enum
- `BackgroundScaleMode.None` - Original size, centered
- `BackgroundScaleMode.Stretch` - Stretch to fill (may distort)
- `BackgroundScaleMode.Uniform` - Scale to fit maintaining aspect ratio
- `BackgroundScaleMode.UniformToFill` - Scale to fill maintaining aspect ratio (may crop)

### BackgroundGradientDirection Enum
- `BackgroundGradientDirection.TopToBottom`
- `BackgroundGradientDirection.LeftToRight`
- `BackgroundGradientDirection.DiagonalTopLeftToBottomRight`
- `BackgroundGradientDirection.DiagonalTopRightToBottomLeft`
- `BackgroundGradientDirection.RadialFromCenter`

---

## Usage Examples

### Complete Setup Example
```csharp
// Initialize service
var service = new InteractiveWin2DRenderingService();
await service.InitializeAsync(1920, 1080);

// Set gradient background
await service.SetBackgroundGradientAsync(
    WinUIColor.FromArgb(255, 25, 25, 112),  // MidnightBlue
    WinUIColor.FromArgb(255, 0, 0, 0),      // Black
    BackgroundGradientDirection.TopToBottom
);

// Add bouncing circle with trail
var motionConfig = new ElementMotionConfig
{
    MotionType = MotionType.Bounce,
    Speed = 200f,
    RespectBoundaries = true,
    ShowTrail = true,
    TrailLength = 25
};

service.AddCircleElementWithMotion(
    new Point(100, 100), 
    25f, 
    WinUIColor.FromArgb(200, 255, 69, 0), 
    motionConfig
);

// Start auto rendering and HID streaming
service.StartAutoRendering(30);
await service.EnableHidTransfer(true);
await service.EnableHidRealTimeDisplayAsync(true);
```

### Scene Management Example
```csharp
// Export current scene
string sceneJson = await service.ExportSceneToJsonAsync();
await File.WriteAllTextAsync("game_scene.json", sceneJson);

// Later, import the scene
string loadedScene = await File.ReadAllTextAsync("game_scene.json");
await service.ImportSceneFromJsonAsync(loadedScene);
```

### Background Preset Example  
```csharp
// Create and save a background preset
string preset = await service.CreateBackgroundPresetAsync(
    "Sunset Theme",
    "Beautiful sunset gradient background"
);
await File.WriteAllTextAsync("sunset_preset.json", preset);

// Apply the preset later
string presetData = await File.ReadAllTextAsync("sunset_preset.json");
await service.ApplyBackgroundPresetAsync(presetData);
```

---

## Performance Guidelines

### Optimal FPS Settings
- **Smooth Animation**: 30-60 FPS
- **Standard UI**: 15-30 FPS  
- **Status Display**: 5-10 FPS
- **Battery Saving**: 1-5 FPS

### JPEG Quality Guidelines
- **High Quality**: 85-100% (larger file size)
- **Balanced**: 70-85% (good quality/size ratio)
- **Compressed**: 50-70% (smaller size, some quality loss)
- **Minimal**: 20-50% (small size, noticeable quality loss)

### Memory Optimization
- Use `Dispose()` to properly cleanup resources
- Limit motion elements with trails to avoid memory buildup
- Consider canvas size impact on performance
- Monitor HID frame send rate for network efficiency

---

## Error Handling

All async methods include comprehensive error handling and will raise `RenderingError` events for exceptions. Key error scenarios:

- **Initialization Failures**: Win2D device creation issues
- **File Operations**: Image loading, scene import/export errors  
- **HID Communication**: Device connection and transfer failures
- **Resource Constraints**: Memory or performance limitations

Monitor the `RenderingError` event for proactive error handling in your application.

---

## Thread Safety

The service is designed to be thread-safe:
- Internal locking protects shared element collections
- HID operations are properly synchronized
- Event handlers are marshaled to appropriate threads
- Disposal is safe to call from any thread