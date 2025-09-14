using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Point = Windows.Foundation.Point;
using Size = Windows.Foundation.Size;
using Rect = Windows.Foundation.Rect;
using WinUIColor = Windows.UI.Color;

namespace CMDevicesManager.Models
{

    #region Background Configuration Enums

    /// <summary>
    /// Background types supported by the rendering service
    /// </summary>
    public enum BackgroundType
    {
        Transparent,
        SolidColor,
        Gradient,
        Image
    }

    /// <summary>
    /// Background image scale modes
    /// </summary>
    public enum BackgroundScaleMode
    {
        None,           // Original size, centered
        Stretch,        // Stretch to fill canvas (may distort)
        Uniform,        // Scale to fit while maintaining aspect ratio
        UniformToFill   // Scale to fill while maintaining aspect ratio (may crop)
    }

    /// <summary>
    /// Gradient directions for background gradients
    /// </summary>
    public enum BackgroundGradientDirection
    {
        TopToBottom,
        LeftToRight,
        DiagonalTopLeftToBottomRight,
        DiagonalTopRightToBottomLeft,
        RadialFromCenter
    }

    #endregion

    #region Motion Configuration and Element Classes

    /// <summary>
    /// Configuration for element motion
    /// </summary>
    public class ElementMotionConfig
    {
        public MotionType MotionType { get; set; } = MotionType.None;
        public float Speed { get; set; } = 100.0f; // pixels per second or radians per second
        public Vector2 Direction { get; set; } = Vector2.Zero;
        public Vector2 Center { get; set; } = Vector2.Zero;
        public float Radius { get; set; } = 50.0f;
        public bool RespectBoundaries { get; set; } = false;
        public bool ShowTrail { get; set; } = false;
        public int TrailLength { get; set; } = 20;
        public bool IsPaused { get; set; } = false;
    }

    /// <summary>
    /// Configuration for text elements
    /// </summary>
    public class TextElementConfig
    {
        public Size Size { get; set; } = new Size(200, 50);
        public float FontSize { get; set; } = 16;
        public string FontFamily { get; set; } = "Segoe UI";
        public WinUIColor TextColor { get; set; } = WinUIColor.FromArgb(255, 255, 255, 255);
        public bool IsDraggable { get; set; } = true;
    }

    /// <summary>
    /// Configuration for image elements
    /// </summary>
    public class ImageElementConfig
    {
        public Size Size { get; set; } = new Size(100, 100);
        public float Scale { get; set; } = 1.0f;
        public float Rotation { get; set; } = 0.0f;
        public bool IsDraggable { get; set; } = true;
    }

    /// <summary>
    /// Interface for motion-enabled elements
    /// </summary>
    public interface IMotionElement
    {
        Point Position { get; set; }
        Point OriginalPosition { get; set; }
        ElementMotionConfig MotionConfig { get; set; }
        DateTime StartTime { get; set; }
        DateTime LastUpdateTime { get; set; }
        DateTime LastDirectionChange { get; set; }
        float CurrentAngle { get; set; }
        Queue<Vector2>? TrailPositions { get; set; }
    }

    /// <summary>
    /// Text element with motion capabilities
    /// </summary>
    public class MotionTextElement : TextElement, IMotionElement
    {
        public Point OriginalPosition { get; set; }
        public ElementMotionConfig MotionConfig { get; set; } = new ElementMotionConfig();
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime LastUpdateTime { get; set; } = DateTime.Now;
        public DateTime LastDirectionChange { get; set; } = DateTime.Now;
        public float CurrentAngle { get; set; }
        public Queue<Vector2>? TrailPositions { get; set; }

        public MotionTextElement(string name) : base(name) { }
    }

    /// <summary>
    /// Shape element with motion capabilities
    /// </summary>
    public class MotionShapeElement : ShapeElement, IMotionElement
    {
        public Point OriginalPosition { get; set; }
        public ElementMotionConfig MotionConfig { get; set; } = new ElementMotionConfig();
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime LastUpdateTime { get; set; } = DateTime.Now;
        public DateTime LastDirectionChange { get; set; } = DateTime.Now;
        public float CurrentAngle { get; set; }
        public Queue<Vector2>? TrailPositions { get; set; }

        public MotionShapeElement(string name) : base(name) { }
    }

    /// <summary>
    /// Image element with motion capabilities
    /// </summary>
    public class MotionImageElement : ImageElement, IMotionElement
    {
        public Point OriginalPosition { get; set; }
        public ElementMotionConfig MotionConfig { get; set; } = new ElementMotionConfig();
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime LastUpdateTime { get; set; } = DateTime.Now;
        public DateTime LastDirectionChange { get; set; } = DateTime.Now;
        public float CurrentAngle { get; set; }
        public Queue<Vector2>? TrailPositions { get; set; }

        public MotionImageElement(string name) : base(name) { }
    }

    /// <summary>
    /// Motion types for elements
    /// </summary>
    public enum MotionType
    {
        None,
        Linear,     // Straight-line movement
        Circular,   // Circular rotation
        Bounce,     // Physics-based bouncing
        Oscillate,  // Back-and-forth oscillation
        Spiral,     // Expanding circular motion
        Random,     // Random direction changes
        Wave,       // Horizontal with vertical wave
        Orbit       // Circular with radius variation
    }

    #endregion

    #region JSON Serialization Data Models

    /// <summary>
    /// Complete scene export data structure
    /// </summary>
    public class SceneExportData
    {
        public string SceneName { get; set; } = "";
        public string? SceneId { get; set; } // Add SceneId property
        public DateTime ExportDate { get; set; }
        public string Version { get; set; } = "1.0";
        public int CanvasWidth { get; set; }
        public int CanvasHeight { get; set; }

        // Display options
        public bool ShowTime { get; set; }
        public bool ShowDate { get; set; }
        public bool ShowSystemInfo { get; set; }
        public bool ShowAnimation { get; set; }

        // Background configuration
        public BackgroundType BackgroundType { get; set; }
        public float BackgroundOpacity { get; set; }
        public SerializedColor BackgroundColor { get; set; } = new SerializedColor();
        public SerializedColor BackgroundGradientEndColor { get; set; } = new SerializedColor();
        public BackgroundScaleMode BackgroundScaleMode { get; set; }
        public BackgroundGradientDirection GradientDirection { get; set; }
        public string? BackgroundImagePath { get; set; }

        // Render settings
        public int TargetFPS { get; set; }
        public int JpegQuality { get; set; }

        // Elements
        public List<ElementExportData> Elements { get; set; } = new List<ElementExportData>();
    }

    /// <summary>
    /// Element export data structure
    /// </summary>
    public class ElementExportData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public SerializedPoint Position { get; set; } = new SerializedPoint();
        public bool IsVisible { get; set; } = true;
        public bool IsDraggable { get; set; } = true;
        public float ZIndex { get; set; } = 0;

        // Element-specific data (only one will be populated per element)
        public TextElementData? TextData { get; set; }
        public ImageElementData? ImageData { get; set; }
        public ShapeElementData? ShapeData { get; set; }
        public LiveElementData? LiveData { get; set; }

        // Motion data (optional)
        public MotionElementData? MotionData { get; set; }
    }

    /// <summary>
    /// Text element specific data
    /// </summary>
    public class TextElementData
    {
        public string Text { get; set; } = "";
        public float FontSize { get; set; } = 16;
        public string FontFamily { get; set; } = "Segoe UI";
        public SerializedColor TextColor { get; set; } = new SerializedColor();
        public SerializedSize Size { get; set; } = new SerializedSize();
    }

    /// <summary>
    /// Image element specific data
    /// </summary>
    public class ImageElementData
    {
        public string ImagePath { get; set; } = "";
        public SerializedSize Size { get; set; } = new SerializedSize();
        public float Scale { get; set; } = 1.0f;
        public float Rotation { get; set; } = 0.0f;
    }

    /// <summary>
    /// Shape element specific data
    /// </summary>
    public class ShapeElementData
    {
        public string ShapeType { get; set; } = "Rectangle";
        public SerializedSize Size { get; set; } = new SerializedSize();
        public SerializedColor FillColor { get; set; } = new SerializedColor();
        public SerializedColor StrokeColor { get; set; } = new SerializedColor();
        public float StrokeWidth { get; set; } = 1.0f;
    }

    /// <summary>
    /// Live element specific data
    /// </summary>
    public class LiveElementData
    {
        public string Format { get; set; } = "";
        public float FontSize { get; set; } = 16;
        public string FontFamily { get; set; } = "Segoe UI";
        public SerializedColor TextColor { get; set; } = new SerializedColor();
        public SerializedSize Size { get; set; } = new SerializedSize();
    }

    /// <summary>
    /// Motion element data
    /// </summary>
    public class MotionElementData
    {
        public SerializedPoint OriginalPosition { get; set; } = new SerializedPoint();
        public SerializedMotionConfig MotionConfig { get; set; } = new SerializedMotionConfig();
        public DateTime StartTime { get; set; }
        public float CurrentAngle { get; set; }
    }

    /// <summary>
    /// Serialized motion configuration
    /// </summary>
    public class SerializedMotionConfig
    {
        public string MotionType { get; set; } = "None";
        public float Speed { get; set; } = 100.0f;
        public SerializedVector2 Direction { get; set; } = new SerializedVector2();
        public SerializedVector2 Center { get; set; } = new SerializedVector2();
        public float Radius { get; set; } = 50.0f;
        public bool RespectBoundaries { get; set; } = false;
        public bool ShowTrail { get; set; } = false;
        public int TrailLength { get; set; } = 20;
        public bool IsPaused { get; set; } = false;
    }

    /// <summary>
    /// Serializable color structure
    /// </summary>
    public class SerializedColor
    {
        public byte A { get; set; } = 255;
        public byte R { get; set; } = 0;
        public byte G { get; set; } = 0;
        public byte B { get; set; } = 0;
    }

    /// <summary>
    /// Serializable point structure
    /// </summary>
    public class SerializedPoint
    {
        public double X { get; set; } = 0;
        public double Y { get; set; } = 0;
    }

    /// <summary>
    /// Serializable size structure
    /// </summary>
    public class SerializedSize
    {
        public double Width { get; set; } = 0;
        public double Height { get; set; } = 0;
    }

    /// <summary>
    /// Serializable Vector2 structure
    /// </summary>
    public class SerializedVector2
    {
        public float X { get; set; } = 0;
        public float Y { get; set; } = 0;
    }

    #endregion // End of JSON Serialization Data Models
}
