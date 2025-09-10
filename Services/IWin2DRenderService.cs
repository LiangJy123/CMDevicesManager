using System;
using System.Collections.Generic;
using System.Numerics;
using SkiaSharp;

namespace CMDevicesManager.Services
{
    public interface IRenderService : IDisposable
    {
        /// <summary>
        /// Gets or sets the canvas size for rendering
        /// </summary>
        SKSizeI CanvasSize { get; set; }

        /// <summary>
        /// Gets or sets the background color
        /// </summary>
        SKColor BackgroundColor { get; set; }

        /// <summary>
        /// Event fired when render output is ready
        /// </summary>
        event EventHandler<RenderOutputEventArgs>? RenderOutputReady;

        /// <summary>
        /// Initialize the render service
        /// </summary>
        void Initialize();

        /// <summary>
        /// Add or update an image element
        /// </summary>
        void AddImage(string id, byte[] imageData, SKPoint position, SKSize size, float opacity = 1.0f, float rotation = 0.0f);

        /// <summary>
        /// Add or update a text element
        /// </summary>
        void AddText(string id, string text, SKPoint position, float fontSize, SKColor color, string fontFamily = "Segoe UI", float opacity = 1.0f, float rotation = 0.0f);

        /// <summary>
        /// Update element position
        /// </summary>
        void UpdateElementPosition(string id, SKPoint position);

        /// <summary>
        /// Update element opacity
        /// </summary>
        void UpdateElementOpacity(string id, float opacity);

        /// <summary>
        /// Update element rotation
        /// </summary>
        void UpdateElementRotation(string id, float rotation);

        /// <summary>
        /// Update text content (only for text elements)
        /// </summary>
        void UpdateTextContent(string id, string text);

        /// <summary>
        /// Update text color (only for text elements)
        /// </summary>
        void UpdateTextColor(string id, SKColor color);

        /// <summary>
        /// Remove an element
        /// </summary>
        void RemoveElement(string id);

        /// <summary>
        /// Clear all elements
        /// </summary>
        void ClearAll();

        /// <summary>
        /// Render the current frame and output the result
        /// </summary>
        void RenderFrame();

        /// <summary>
        /// Get the current rendered frame as byte array (BGRA format)
        /// </summary>
        byte[] GetRenderedFrameData();

        /// <summary>
        /// Get the current rendered frame as SKImage
        /// </summary>
        SKImage? GetRenderedImage();

        /// <summary>
        /// Start realtime rendering at specified FPS
        /// </summary>
        void StartRealtimeRendering(int fps = 30);

        /// <summary>
        /// Stop realtime rendering
        /// </summary>
        void StopRealtimeRendering();

        /// <summary>
        /// Check if realtime rendering is active
        /// </summary>
        bool IsRealtimeRenderingActive { get; }
    }

    public class RenderOutputEventArgs : EventArgs
    {
        public byte[] FrameData { get; }
        public SKSizeI FrameSize { get; }
        public SKImage? RenderedImage { get; }
        public DateTime Timestamp { get; }

        public RenderOutputEventArgs(byte[] frameData, SKSizeI frameSize, SKImage? renderedImage = null)
        {
            FrameData = frameData;
            FrameSize = frameSize;
            RenderedImage = renderedImage;
            Timestamp = DateTime.Now;
        }
    }

    public abstract class RenderElement
    {
        public string Id { get; set; } = string.Empty;
        public SKPoint Position { get; set; }
        public float Opacity { get; set; } = 1.0f;
        public float Rotation { get; set; } = 0.0f;
        public int ZIndex { get; set; } = 0;

        public abstract void Render(SKCanvas canvas);
    }

    public class ImageElement : RenderElement
    {
        public SKBitmap? Bitmap { get; set; }
        public SKSize Size { get; set; }

        public override void Render(SKCanvas canvas)
        {
            if (Bitmap == null) return;

            canvas.Save();

            // Apply transformations
            canvas.Translate(Position.X + Size.Width / 2, Position.Y + Size.Height / 2);
            canvas.RotateDegrees(Rotation);
            canvas.Scale(Size.Width / Bitmap.Width, Size.Height / Bitmap.Height);

            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)(255 * Opacity)),
                FilterQuality = SKFilterQuality.High
            };

            canvas.DrawBitmap(Bitmap, -Bitmap.Width / 2, -Bitmap.Height / 2, paint);
            canvas.Restore();
        }
    }

    public class TextElement : RenderElement
    {
        public string Text { get; set; } = string.Empty;
        public float FontSize { get; set; } = 16.0f;
        public SKColor Color { get; set; } = SKColors.White;
        public string FontFamily { get; set; } = "Segoe UI";

        public override void Render(SKCanvas canvas)
        {
            if (string.IsNullOrEmpty(Text)) return;

            using var paint = new SKPaint
            {
                Color = Color.WithAlpha((byte)(255 * Opacity)),
                TextSize = FontSize,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName(FontFamily)
            };

            canvas.Save();
            canvas.Translate(Position.X, Position.Y);
            canvas.RotateDegrees(Rotation);
            
            canvas.DrawText(Text, 0, 0, paint);
            canvas.Restore();
        }
    }
}