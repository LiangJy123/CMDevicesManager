using System;
using Windows.Foundation;
using Point = Windows.Foundation.Point;
using Size = Windows.Foundation.Size;
using WinUIColor = Windows.UI.Color;

namespace CMDevicesManager.Models
{
    public enum ElementType
    {
        Text,
        Image,
        Shape,
        LiveTime,
        LiveDate,
        SystemInfo
    }

    public enum ShapeType
    {
        Circle,
        Rectangle,
        Triangle
    }

    public abstract class RenderElement
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public ElementType Type { get; protected set; }
        public Point Position { get; set; }
        public bool IsVisible { get; set; } = true;
        public bool IsSelected { get; set; }
        public bool IsDraggable { get; set; } = true;
        public float ZIndex { get; set; } = 0;

        protected RenderElement(ElementType type, string name)
        {
            Type = type;
            Name = name;
        }

        public abstract Rect GetBounds();
        public abstract bool HitTest(Point point);
    }

    public class TextElement : RenderElement
    {
        public string Text { get; set; } = "";
        public float FontSize { get; set; } = 24;
        public string FontFamily { get; set; } = "Segoe UI";
        public WinUIColor TextColor { get; set; } = WinUIColor.FromArgb(255, 255, 255, 255);
        public Size Size { get; set; }

        public TextElement(string name = "Text") : base(ElementType.Text, name) { }

        public override Rect GetBounds()
        {
            return new Rect(Position.X, Position.Y, Size.Width, Size.Height);
        }

        public override bool HitTest(Point point)
        {
            var bounds = GetBounds();
            return point.X >= bounds.Left && point.X <= bounds.Right &&
                   point.Y >= bounds.Top && point.Y <= bounds.Bottom;
        }
    }

    public class ImageElement : RenderElement
    {
        public string ImagePath { get; set; } = "";
        public Size Size { get; set; }
        public float Scale { get; set; } = 1.0f;
        public float Rotation { get; set; } = 0f;

        public ImageElement(string name = "Image") : base(ElementType.Image, name) { }

        public override Rect GetBounds()
        {
            var scaledSize = new Size(Size.Width * Scale, Size.Height * Scale);
            return new Rect(Position.X, Position.Y, scaledSize.Width, scaledSize.Height);
        }

        public override bool HitTest(Point point)
        {
            var bounds = GetBounds();
            return point.X >= bounds.Left && point.X <= bounds.Right &&
                   point.Y >= bounds.Top && point.Y <= bounds.Bottom;
        }
    }

    public class ShapeElement : RenderElement
    {
        public ShapeType ShapeType { get; set; }
        public Size Size { get; set; }
        public WinUIColor FillColor { get; set; } = WinUIColor.FromArgb(255, 0, 0, 255);
        public WinUIColor StrokeColor { get; set; } = WinUIColor.FromArgb(255, 255, 255, 255);
        public float StrokeWidth { get; set; } = 2f;

        public ShapeElement(string name = "Shape") : base(ElementType.Shape, name) { }

        public override Rect GetBounds()
        {
            return new Rect(Position.X, Position.Y, Size.Width, Size.Height);
        }

        public override bool HitTest(Point point)
        {
            var bounds = GetBounds();
            switch (ShapeType)
            {
                case ShapeType.Circle:
                    var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                    var radius = Math.Min(bounds.Width, bounds.Height) / 2;
                    var distance = Math.Sqrt(Math.Pow(point.X - center.X, 2) + Math.Pow(point.Y - center.Y, 2));
                    return distance <= radius;
                
                case ShapeType.Rectangle:
                case ShapeType.Triangle:
                default:
                    return point.X >= bounds.Left && point.X <= bounds.Right &&
                           point.Y >= bounds.Top && point.Y <= bounds.Bottom;
            }
        }
    }

    public class LiveElement : RenderElement
    {
        public string Format { get; set; } = "";
        public float FontSize { get; set; } = 24;
        public string FontFamily { get; set; } = "Segoe UI";
        public WinUIColor TextColor { get; set; } = WinUIColor.FromArgb(255, 255, 255, 255);
        public Size Size { get; set; }

        public LiveElement(ElementType type, string name) : base(type, name) { }

        public override Rect GetBounds()
        {
            return new Rect(Position.X, Position.Y, Size.Width, Size.Height);
        }

        public override bool HitTest(Point point)
        {
            var bounds = GetBounds();
            return point.X >= bounds.Left && point.X <= bounds.Right &&
                   point.Y >= bounds.Top && point.Y <= bounds.Bottom;
        }

        public string GetCurrentText()
        {
            return Type switch
            {
                ElementType.LiveTime => DateTime.Now.ToString(string.IsNullOrEmpty(Format) ? "HH:mm:ss" : Format),
                ElementType.LiveDate => DateTime.Now.ToString(string.IsNullOrEmpty(Format) ? "dddd, MMMM dd, yyyy" : Format),
                ElementType.SystemInfo => $"Memory: {GC.GetTotalMemory(false) / 1024 / 1024:F1} MB",
                _ => ""
            };
        }
    }
}