using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CMDevicesManager.Pages;
using static CMDevicesManager.Pages.DeviceConfigPage;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// 通用 Canvas 配置渲染器（动态构建 512x512 画面）
    /// 提供移动元素与使用率视觉项集合，供调用方启动计时刷新（CPU/GPU等）。
    /// </summary>
    public sealed class CanvasRenderEngine
    {
        #region Singleton
        private static readonly Lazy<CanvasRenderEngine> _lazy = new(() => new CanvasRenderEngine());
        public static CanvasRenderEngine Instance => _lazy.Value;
        private CanvasRenderEngine() { }
        #endregion

        #region Gauge constants (与页面保持一致)
        private const double GaugeStartAngle = 150;
        private const double GaugeEndAngle = 390;
        private const double GaugeSweep = GaugeEndAngle - GaugeStartAngle; // 240°
        private const double GaugeRadiusOuter = 70;
        private const double GaugeRadiusInner = 60;
        private const double GaugeNeedleLength = 56;
        private const int GaugeMajorStep = 10;
        private const int GaugeMinorStep = 5;
        private const int GaugeLabelStep = 25;
        private const double GaugeCenterX = 80;
        private const double GaugeCenterY = 90;
        private const double GaugeNeedleAngleOffset = 270;
        private static double GaugeAngleFromPercent(double percent) =>
            GaugeStartAngle + GaugeSweep * (Math.Clamp(percent, 0, 100) / 100.0);
        private static double GaugeRotationFromPercent(double percent) =>
            GaugeAngleFromPercent(percent) - GaugeNeedleAngleOffset;
        #endregion

        public sealed class UsageVisualItem
        {
            public Border HostBorder = null!;
            public LiveInfoKind Kind;
            public TextBlock Text = null!;
            public Rectangle? BarFill;
            public double BarTotalWidth;
            public Line? GaugeNeedle;
            public RotateTransform? GaugeNeedleRotate;
            public List<Line> GaugeTicks = new();
            public string DisplayStyle = "Text";
            public string? DateFormat;
            // Colors
            public Color StartColor;
            public Color EndColor;
        }

        public record RenderResult(
            List<UsageVisualItem> UsageItems,
            Dictionary<Border, (double dx, double dy)> MovingDirections,
            int CanvasSize,
            double MoveSpeed);

        public record RenderContext(Rectangle BgColorRect, Image BgImage, Canvas DesignCanvas, string OutputFolder);
        // NEW: usage-like 判定（CPU/GPU 使用率 + 温度）
        private static bool IsUsageLike(LiveInfoKind kind) =>
            kind == LiveInfoKind.CpuUsage ||
            kind == LiveInfoKind.GpuUsage ||
            kind == LiveInfoKind.CpuTemperature ||
            kind == LiveInfoKind.GpuTemperature;


        // 🆕 新增辅助方法（放在 Apply 方法之前）
        private static double CalculateTranslateX(Border border, double topLeftX, double scaleX)
        {
            double actualW = border.ActualWidth;
            if (actualW <= 0) return topLeftX;

            double scaledW = actualW * scaleX;
            double offsetX = (actualW - scaledW) / 2.0;

            return topLeftX - offsetX;
        }

        private static double CalculateTranslateY(Border border, double topLeftY, double scaleY)
        {
            double actualH = border.ActualHeight;
            if (actualH <= 0) return topLeftY;

            double scaledH = actualH * scaleY;
            double offsetY = (actualH - scaledH) / 2.0;

            return topLeftY - offsetY;
        }


        public RenderResult Apply(
            CanvasConfiguration cfg,
            RenderContext ctx,
            Func<double>? getCpuPercent = null,
            Func<double>? getGpuPercent = null)
        {
            var usage = new List<UsageVisualItem>();
            var moving = new Dictionary<Border, (double dx, double dy)>();

            var designCanvas = ctx.DesignCanvas;
            designCanvas.Children.Clear();

            int canvasSize = cfg.CanvasSize > 0 ? cfg.CanvasSize : 512;
            designCanvas.Width = designCanvas.Height = canvasSize;
            double moveSpeed = cfg.MoveSpeed > 0 ? cfg.MoveSpeed : 100;

            // Background color
            try
            {
                if (!string.IsNullOrWhiteSpace(cfg.BackgroundColor))
                {
                    var brush = (Brush)new BrushConverter().ConvertFromString(cfg.BackgroundColor);
                    ctx.BgColorRect.Fill = brush;
                }
                else ctx.BgColorRect.Fill = Brushes.White;
            }
            catch { ctx.BgColorRect.Fill = Brushes.White; }

            // Background image
            if (!string.IsNullOrWhiteSpace(cfg.BackgroundImagePath))
            {
                string resolved = ResolvePath(cfg.BackgroundImagePath, ctx.OutputFolder);
                if (File.Exists(resolved))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(resolved, UriKind.Absolute);
                        bmp.EndInit();
                        bmp.Freeze();
                        ctx.BgImage.Source = bmp;
                        ctx.BgImage.Opacity = cfg.BackgroundImageOpacity <= 0 ? 1 : cfg.BackgroundImageOpacity;
                    }
                    catch { ctx.BgImage.Source = null; }
                }
                else ctx.BgImage.Source = null;
            }
            else ctx.BgImage.Source = null;

            foreach (var elem in cfg.Elements.OrderBy(e => e.ZIndex))
            {
                var elemCopy = elem; // 用于闭包（移动方向等）

                FrameworkElement? inner = null;
                Border? host = null;
                bool usageVisual = false;

                switch (elem.Type)
                {
                    case "Text":
                        inner = BuildText(elem);
                        break;
                    case "LiveText":
                        if (!elem.LiveKind.HasValue) break;
                        inner = BuildLive(elem, elem.LiveKind.Value, getCpuPercent, getGpuPercent,
                            usage, ref usageVisual);
                        break;
                    case "Image":
                        inner = BuildImage(elem, ctx.OutputFolder);
                        break;
                    case "Video":
                        inner = BuildVideoPlaceholder();
                        break;
                }

                if (inner == null) continue;

                if (!usageVisual)
                {
                    host = new Border
                    {
                        Child = inner,
                        BorderThickness = new Thickness(0),
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        Opacity = elem.Opacity <= 0 ? 1 : elem.Opacity
                    };
                }
                else
                {
                    host = usage.LastOrDefault()?.HostBorder;
                    if (host != null)
                        host.Opacity = elem.Opacity <= 0 ? 1 : elem.Opacity;
                }

                if (host == null) continue;

                // ✅ Transform: 直接使用配置中的值（无需转换）
                var tg = new TransformGroup();
                double scaleFactor = elem.Scale <= 0 ? 1.0 : elem.Scale;
                var scaleTransform = new ScaleTransform(scaleFactor, scaleFactor);
                tg.Children.Add(scaleTransform);

                if (elem.MirroredX == true)
                    tg.Children.Add(new ScaleTransform(-1, 1));
                if (elem.Rotation.HasValue && Math.Abs(elem.Rotation.Value) > 0.01)
                    tg.Children.Add(new RotateTransform(elem.Rotation.Value));

                // ✅ 直接使用保存的 TranslateTransform 值
                var translate = new TranslateTransform(elem.X, elem.Y);
                tg.Children.Add(translate);

                host.RenderTransform = tg;
                Canvas.SetZIndex(host, elem.ZIndex);
                designCanvas.Children.Add(host);

                // moving
                if ((elem.Type == "Text" || elem.Type == "LiveText")
                    && elem.MoveDirX.HasValue && elem.MoveDirY.HasValue
                    && (Math.Abs(elem.MoveDirX.Value) > 0.0001 || Math.Abs(elem.MoveDirY.Value) > 0.0001))
                {
                    moving[host] = (elem.MoveDirX.Value, elem.MoveDirY.Value);
                }
            }

            return new RenderResult(usage, moving, canvasSize, moveSpeed);
        }

        #region Element builders
        private FrameworkElement BuildText(ElementConfiguration e)
        {
            var tb = new TextBlock
            {
                Text = e.Text ?? "Text",
                FontSize = e.FontSize ?? 24,
                FontWeight = FontWeights.SemiBold
            };
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
            ApplyTextColors(tb, e.UseTextGradient, e.TextColor, e.TextColor2);
            return tb;
        }

        private FrameworkElement BuildLive(
            ElementConfiguration e,
            DeviceConfigPage.LiveInfoKind kind,
            Func<double>? getCpu,
            Func<double>? getGpu,
            List<UsageVisualItem> usageCollector,
            ref bool usageVisual)
        {
            var tb = new TextBlock
            {
                FontSize = e.FontSize ?? 18,
                FontWeight = FontWeights.SemiBold
            };
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

            // 初始值
            if (kind == DeviceConfigPage.LiveInfoKind.DateTime)
                tb.Text = DateTime.Now.ToString(string.IsNullOrWhiteSpace(e.DateFormat) ? "yyyy-MM-dd HH:mm:ss" : e.DateFormat);
            else if (kind == DeviceConfigPage.LiveInfoKind.CpuUsage)
                tb.Text = $"CPU {(getCpu?.Invoke() ?? 0):0}%";
            else if (kind == DeviceConfigPage.LiveInfoKind.GpuUsage)
                tb.Text = $"GPU {(getGpu?.Invoke() ?? 0):0}%";
            else if (kind == DeviceConfigPage.LiveInfoKind.CpuTemperature)
                tb.Text = "CPU 0°C"; // 初始占位，稍后定时器刷新
            else if (kind == DeviceConfigPage.LiveInfoKind.GpuTemperature)
                tb.Text = "GPU 0°C";

            string style = e.UsageDisplayStyle?.Trim() ?? "Text";
            bool isUsageStyle = IsUsageLike(kind) &&
                    !style.Equals("Text", StringComparison.OrdinalIgnoreCase);

            if (!isUsageStyle)
            {
                ApplyTextColors(tb, e.UseTextGradient, e.TextColor, e.TextColor2);
                var simple = new UsageVisualItem
                {
                    Kind = (LiveInfoKind)kind,
                    Text = tb,
                    DisplayStyle = "Text",
                    DateFormat = e.DateFormat
                };
                usageCollector.Add(simple);
                return tb;
            }

            usageVisual = true;
            var startColor = ParseColorOr(e.UsageStartColor, Color.FromRgb(80, 180, 80));
            var endColor = ParseColorOr(e.UsageEndColor, Color.FromRgb(40, 120, 40));
            var barBg = ParseColorOr(e.UsageBarBackgroundColor, Color.FromRgb(40, 46, 58));
            var needleColor = ParseColorOr(e.UsageNeedleColor, Color.FromRgb(90, 200, 90));

            if (style.Equals("ProgressBar", StringComparison.OrdinalIgnoreCase))
            {
                var container = new Grid { Width = 160, Height = 42 };
                var margin = new Thickness(10, 8, 10, 20);
                var bg = new Border
                {
                    Margin = margin,
                    Height = 14,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(barBg),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(70, 80, 96)),
                    BorderThickness = new Thickness(1)
                };
                var fill = new Rectangle
                {
                    Margin = margin,
                    Height = 14,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    RadiusX = 5,
                    RadiusY = 5,
                    Fill = new LinearGradientBrush(startColor, endColor, 0),
                    Width = 0
                };
                tb.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                tb.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
                tb.FontSize = 14;
                tb.Margin = new Thickness(0, 0, 0, 2);
                tb.Foreground = new SolidColorBrush(startColor);

                var layer = new Grid();
                layer.Children.Add(bg);
                layer.Children.Add(fill);
                container.Children.Add(layer);
                container.Children.Add(tb);

                var item = new UsageVisualItem
                {
                    Kind = (LiveInfoKind)kind,
                    Text = tb,
                    DisplayStyle = "ProgressBar",
                    BarFill = fill,
                    BarTotalWidth = 160 - margin.Left - margin.Right,
                    StartColor = startColor,
                    EndColor = endColor
                };
                usageCollector.Add(item);

                var host = new Border { Child = container };
                item.HostBorder = host;
                return container;
            }
            else // Gauge
            {
                var container = new Grid { Width = 160, Height = 120 };
                var canvas = new Canvas { Width = 160, Height = 120 };

                for (int p = 0; p <= 100; p += GaugeMinorStep)
                {
                    bool major = p % GaugeMajorStep == 0;
                    double angle = GaugeAngleFromPercent(p);
                    double rad = angle * Math.PI / 180.0;
                    double rOuter = GaugeRadiusOuter;
                    double rInner = major ? GaugeRadiusInner : GaugeRadiusInner + 4;

                    double x1 = GaugeCenterX + rOuter * Math.Cos(rad);
                    double y1 = GaugeCenterY + rOuter * Math.Sin(rad);
                    double x2 = GaugeCenterX + rInner * Math.Cos(rad);
                    double y2 = GaugeCenterY + rInner * Math.Sin(rad);
                    var tick = new Line
                    {
                        X1 = x1,
                        Y1 = y1,
                        X2 = x2,
                        Y2 = y2,
                        StrokeThickness = major ? 3 : 1.5,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Stroke = new SolidColorBrush(Color.FromRgb(120, 160, 120))
                    };
                    canvas.Children.Add(tick);
                }

                var needle = new Line
                {
                    X1 = GaugeCenterX,
                    Y1 = GaugeCenterY,
                    X2 = GaugeCenterX,
                    Y2 = GaugeCenterY - GaugeNeedleLength,
                    StrokeThickness = 5,
                    Stroke = new SolidColorBrush(needleColor),
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Triangle
                };
                var rt = new RotateTransform(GaugeRotationFromPercent(0), GaugeCenterX, GaugeCenterY);
                needle.RenderTransform = rt;
                canvas.Children.Add(needle);

                var cap = new Ellipse
                {
                    Width = 18,
                    Height = 18,
                    Fill = new SolidColorBrush(Color.FromRgb(45, 52, 63)),
                    Stroke = new SolidColorBrush(Color.FromRgb(160, 170, 185)),
                    StrokeThickness = 2
                };
                Canvas.SetLeft(cap, GaugeCenterX - 9);
                Canvas.SetTop(cap, GaugeCenterY - 9);
                canvas.Children.Add(cap);

                tb.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                tb.VerticalAlignment = VerticalAlignment.Bottom;
                tb.FontSize = 18;
                container.Children.Add(canvas);
                container.Children.Add(tb);

                var item = new UsageVisualItem
                {
                    Kind = (LiveInfoKind)kind,
                    Text = tb,
                    DisplayStyle = "Gauge",
                    GaugeNeedle = needle,
                    GaugeNeedleRotate = rt,
                    StartColor = startColor,
                    EndColor = endColor
                };
                usageCollector.Add(item);
                var host = new Border { Child = container };
                item.HostBorder = host;
                return container;
            }
        }

        private FrameworkElement? BuildImage(ElementConfiguration e, string root)
        {
            if (string.IsNullOrWhiteSpace(e.ImagePath)) return null;
            string resolved = ResolvePath(e.ImagePath, root);
            if (!File.Exists(resolved)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(resolved, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return new Image { Source = bmp, Stretch = Stretch.Uniform };
            }
            catch { return null; }
        }

        private FrameworkElement BuildVideoPlaceholder()
        {
            var tb = new TextBlock
            {
                Text = "VIDEO",
                FontSize = 24,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                Padding = new Thickness(10)
            };
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
            return tb;
        }
        #endregion

        #region Helpers
        private void ApplyTextColors(TextBlock tb, bool? useGradient, string? c1Hex, string? c2Hex)
        {
            try
            {
                if (useGradient == true &&
                    !string.IsNullOrWhiteSpace(c1Hex) &&
                    !string.IsNullOrWhiteSpace(c2Hex) &&
                    TryParseColor(c1Hex, out var c1) &&
                    TryParseColor(c2Hex, out var c2))
                {
                    var lg = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1)
                    };
                    lg.GradientStops.Add(new GradientStop(c1, 0));
                    lg.GradientStops.Add(new GradientStop(c2, 1));
                    tb.Foreground = lg;
                }
                else if (!string.IsNullOrWhiteSpace(c1Hex) && TryParseColor(c1Hex, out var cSolid))
                {
                    tb.Foreground = new SolidColorBrush(cSolid);
                }
                else tb.Foreground = Brushes.White;
            }
            catch { tb.Foreground = Brushes.White; }
        }

        private bool TryParseColor(string hex, out Color c)
        {
            c = Colors.White;
            try
            {
                if (!hex.StartsWith("#")) hex = "#" + hex;
                var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
                c = brush.Color;
                return true;
            }
            catch { return false; }
        }

        private Color ParseColorOr(string? hex, Color fallback)
            => (!string.IsNullOrWhiteSpace(hex) && TryParseColor(hex, out var c)) ? c : fallback;

        private string ResolvePath(string relative, string baseFolder)
        {
            try
            {
                if (Path.IsPathRooted(relative) && File.Exists(relative)) return relative;
                string candidate = Path.Combine(baseFolder, relative);
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(baseFolder, "Resources", relative);
                if (File.Exists(candidate)) return candidate;
                foreach (var sub in new[] { "Images", "Backgrounds", "Videos" })
                {
                    candidate = Path.Combine(baseFolder, "Resources", sub, relative);
                    if (File.Exists(candidate)) return candidate;
                }
                return relative;
            }
            catch { return relative; }
        }
        #endregion
    }
}