using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfPoint = System.Windows.Point;
using WpfSize  = System.Windows.Size;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace CMDevicesManager.Controls
{
    public partial class RingProgressBar : System.Windows.Controls.UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingProgressBar),
                new PropertyMetadata(0d, OnVisualPropertyChanged));

        public static readonly DependencyProperty ThicknessProperty =
            DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(RingProgressBar),
                new PropertyMetadata(6d, OnVisualPropertyChanged));

        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(nameof(TrackBrush), typeof(WpfBrush), typeof(RingProgressBar),
                new PropertyMetadata(new SolidColorBrush(WpfColor.FromRgb(0x2E, 0x37, 0x47))));

        // New: font sizes for the text inside the ring
        public static readonly DependencyProperty ValueFontSizeProperty =
            DependencyProperty.Register(nameof(ValueFontSize), typeof(double), typeof(RingProgressBar),
                new PropertyMetadata(26d)); // default larger

        public static readonly DependencyProperty PercentFontSizeProperty =
            DependencyProperty.Register(nameof(PercentFontSize), typeof(double), typeof(RingProgressBar),
                new PropertyMetadata(12d));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Thickness
        {
            get => (double)GetValue(ThicknessProperty);
            set => SetValue(ThicknessProperty, value);
        }

        public WpfBrush TrackBrush
        {
            get => (WpfBrush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public double ValueFontSize
        {
            get => (double)GetValue(ValueFontSizeProperty);
            set => SetValue(ValueFontSizeProperty, value);
        }

        public double PercentFontSize
        {
            get => (double)GetValue(PercentFontSizeProperty);
            set => SetValue(PercentFontSizeProperty, value);
        }

        public RingProgressBar()
        {
            InitializeComponent();
            SizeChanged += (_, __) => UpdateArc();
            Loaded += (_, __) => UpdateArc();
        }

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RingProgressBar ring) ring.UpdateArc();
        }

        private void UpdateArc()
        {
            double w = Math.Max(0, ActualWidth - Thickness);
            double h = Math.Max(0, ActualHeight - Thickness);
            double radiusX = w / 2.0;
            double radiusY = h / 2.0;
            double centerX = ActualWidth / 2.0;
            double centerY = ActualHeight / 2.0;

            double pct = Math.Clamp(Value, 0, 100);
            double angle = pct / 100.0 * 359.999;

            if (BaseRing != null)
            {
                BaseRing.Width = w;
                BaseRing.Height = h;
                BaseRing.Margin = new Thickness(Thickness / 2.0);
            }

            double startAngle = -90;
            double endAngle = startAngle + angle;

            WpfPoint startPoint = PointOnCircle(centerX, centerY, radiusX, radiusY, startAngle);
            WpfPoint endPoint   = PointOnCircle(centerX, centerY, radiusX, radiusY, endAngle);

            bool isLargeArc = angle > 180;

            var fig = new PathFigure { StartPoint = startPoint, IsClosed = false, IsFilled = false };
            fig.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new WpfSize(radiusX, radiusY),
                IsLargeArc = isLargeArc,
                SweepDirection = SweepDirection.Clockwise
            });

            var geom = new PathGeometry();
            geom.Figures.Add(fig);
            ArcPath.Data = geom;
        }

        private static WpfPoint PointOnCircle(double cx, double cy, double rx, double ry, double angleDeg)
        {
            double a = angleDeg * Math.PI / 180.0;
            return new WpfPoint(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a));
        }
    }
}