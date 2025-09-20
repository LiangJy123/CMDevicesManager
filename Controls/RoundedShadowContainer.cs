using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace CMDevicesManager.Controls
{
    public class RoundedShadowContainer : Decorator
    {
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(RoundedShadowContainer),
                new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShadowSizeProperty =
            DependencyProperty.Register(nameof(ShadowSize), typeof(double), typeof(RoundedShadowContainer),
                new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShadowColorProperty =
            DependencyProperty.Register(nameof(ShadowColor), typeof(Color), typeof(RoundedShadowContainer),
                new FrameworkPropertyMetadata(Colors.Black, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShadowOpacityProperty =
            DependencyProperty.Register(nameof(ShadowOpacity), typeof(double), typeof(RoundedShadowContainer),
                new FrameworkPropertyMetadata(0.55, FrameworkPropertyMetadataOptions.AffectsRender));

        public double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public double ShadowSize
        {
            get => (double)GetValue(ShadowSizeProperty);
            set => SetValue(ShadowSizeProperty, value);
        }

        public Color ShadowColor
        {
            get => (Color)GetValue(ShadowColorProperty);
            set => SetValue(ShadowColorProperty, value);
        }

        public double ShadowOpacity
        {
            get => (double)GetValue(ShadowOpacityProperty);
            set => SetValue(ShadowOpacityProperty, value);
        }

        protected override Size MeasureOverride(Size constraint)
        {
            if (Child != null)
            {
                var sz = new Size(
                    Math.Max(0, constraint.Width - ShadowSize * 2),
                    Math.Max(0, constraint.Height - ShadowSize * 2));
                Child.Measure(sz);
                var desired = Child.DesiredSize;
                return new Size(desired.Width + ShadowSize * 2, desired.Height + ShadowSize * 2);
            }
            return base.MeasureOverride(constraint);
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            Child?.Arrange(new Rect(ShadowSize, ShadowSize,
                Math.Max(0, arrangeSize.Width - ShadowSize * 2),
                Math.Max(0, arrangeSize.Height - ShadowSize * 2)));
            return arrangeSize;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            double s = ShadowSize;
            double cr = CornerRadius;
            double w = ActualWidth;
            double h = ActualHeight;

            var shadowColor = ShadowColor;
            shadowColor.A = (byte)(ShadowOpacity * 255);

            // INSET AREA (content bounds)
            Rect contentRect = new Rect(s, s, Math.Max(0, w - 2 * s), Math.Max(0, h - 2 * s));

            // 1. Draw edges (linear gradients)
            if (contentRect.Width > 0 && contentRect.Height > 0)
            {
                // Top
                dc.DrawRectangle(
                    new LinearGradientBrush(
                        Color.FromArgb(0, shadowColor.R, shadowColor.G, shadowColor.B),
                        shadowColor,
                        new Point(0, 1), new Point(0, 0)),
                    null,
                    new Rect(contentRect.Left + cr, 0, contentRect.Width - 2 * cr, s));

                // Bottom
                dc.DrawRectangle(
                    new LinearGradientBrush(
                        Color.FromArgb(0, shadowColor.R, shadowColor.G, shadowColor.B),
                        shadowColor,
                        new Point(0, 0), new Point(0, 1)),
                    null,
                    new Rect(contentRect.Left + cr, contentRect.Bottom, contentRect.Width - 2 * cr, s));

                // Left
                dc.DrawRectangle(
                    new LinearGradientBrush(
                        Color.FromArgb(0, shadowColor.R, shadowColor.G, shadowColor.B),
                        shadowColor,
                        new Point(1, 0), new Point(0, 0)),
                    null,
                    new Rect(0, contentRect.Top + cr, s, contentRect.Height - 2 * cr));

                // Right
                dc.DrawRectangle(
                    new LinearGradientBrush(
                        Color.FromArgb(0, shadowColor.R, shadowColor.G, shadowColor.B),
                        shadowColor,
                        new Point(0, 0), new Point(1, 0)),
                    null,
                    new Rect(contentRect.Right, contentRect.Top + cr, s, contentRect.Height - 2 * cr));
            }

            // 2. Corner radial gradients
            DrawCorner(dc, new Point(contentRect.Left + cr, contentRect.Top + cr), cr, s, shadowColor, Corner.TopLeft);
            DrawCorner(dc, new Point(contentRect.Right - cr, contentRect.Top + cr), cr, s, shadowColor, Corner.TopRight);
            DrawCorner(dc, new Point(contentRect.Left + cr, contentRect.Bottom - cr), cr, s, shadowColor, Corner.BottomLeft);
            DrawCorner(dc, new Point(contentRect.Right - cr, contentRect.Bottom - cr), cr, s, shadowColor, Corner.BottomRight);
        }

        private enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        private void DrawCorner(DrawingContext dc, Point innerCornerCenter, double cr, double s, Color finalColor, Corner corner)
        {
            double outerR = cr + s;
            double diameter = outerR * 2;

            // We use a radial gradient whose center is at the inner corner center.
            // The inner part (up to radius = cr) stays transparent; from there to outerR fades to shadow color.
            double innerStop = cr / outerR;

            var brush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, finalColor.R, finalColor.G, finalColor.B), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, finalColor.R, finalColor.G, finalColor.B), innerStop));
            brush.GradientStops.Add(new GradientStop(finalColor, 1.0));
            brush.Freeze();

            // Compute the top-left of the gradient rectangle depending on corner
            double x = innerCornerCenter.X - outerR;
            double y = innerCornerCenter.Y - outerR;

            var rect = new Rect(x, y, diameter, diameter);

            // Clip to the quadrant so we only keep outside content.
            Geometry clip = corner switch
            {
                Corner.TopLeft => new RectangleGeometry(new Rect(0, 0, innerCornerCenter.X, innerCornerCenter.Y)),
                Corner.TopRight => new RectangleGeometry(new Rect(innerCornerCenter.X - cr - s, 0, cr + s + (outerR - cr), innerCornerCenter.Y)),
                Corner.BottomLeft => new RectangleGeometry(new Rect(0, innerCornerCenter.Y - cr - s, innerCornerCenter.X, cr + s + (outerR - cr))),
                Corner.BottomRight => new RectangleGeometry(new Rect(innerCornerCenter.X - cr - s, innerCornerCenter.Y - cr - s,
                                                                     cr + s + (outerR - cr), cr + s + (outerR - cr))),
                _ => null!
            };

            dc.PushClip(clip);
            dc.DrawEllipse(brush, null, new Point(x + outerR, y + outerR), outerR, outerR);
            dc.Pop();
        }
    }
}