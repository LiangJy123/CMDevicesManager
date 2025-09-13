using MicaWPF.Controls;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;
using Canvas = System.Windows.Controls.Canvas;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
namespace CMDevicesManager.Windows
{
    public partial class ColorPickerWindow : Window
    {
        public Color SelectedColor { get; private set; }

        private bool _suppress;
        private bool _dragPicking;
        private BitmapSource? _wheelBmp;          // BGRA32 cached
        private Rect _displayRect = Rect.Empty;   // actual rendered image rect when Stretch=Uniform

        public ColorPickerWindow(Color? initialColor = null)
        {
            InitializeComponent();
            SelectedColor = initialColor ?? Colors.Orange;
            Loaded += OnLoaded;
            KeyDown += OnKeyDownSimple;
            UpdateUI();
        }
        private void WheelBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 保证左侧整体 Border 为正方形
            // 逻辑: 取当前宽度与可用高度(所在行的实际高度)中的最小值作为边长
            if (sender is not System.Windows.Controls.Border b) return;

            // 所在行的高度 = 外层 Grid 第2行的实际高度 (父 Grid.Row=1)
            if (b.Parent is System.Windows.Controls.Grid parentGrid)
            {
                // 找到父窗口中内容区域行 (Row=1)
                // 这里 b.ActualWidth 由列宽 (2*) 决定，我们只调节 Height；
                // 若宽度大于可用高度，会截成用高度的正方形；若宽度较小则用宽度。
                double availableHeight = parentGrid.ActualHeight;
                double side = Math.Min(b.ActualWidth, availableHeight);
                if (side > 0)
                {
                    b.Height = side;
                    // 更新显示区域矩形，用于取色映射（与之前逻辑保持兼容）
                    RecalcDisplayRect();
                }
            }
        }
        public static bool TryPick(Window owner, Color initial, out Color chosen)
        {
            var dlg = new ColorPickerWindow(initial) { Owner = owner };
            if (dlg.ShowDialog() == true)
            {
                chosen = dlg.SelectedColor;
                return true;
            }
            chosen = initial;
            return false;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            EnsureWheelLoaded();
            CacheWheel();
            RecalcDisplayRect();
        }

        #region Image / Cache
        private void EnsureWheelLoaded()
        {
            if (ColorWheel.Source != null) return;
            TrySetImage(() =>
                new BitmapImage(new Uri("pack://application:,,,/CMDevicesManager;component/Assets/ColorWheelPicker.png", UriKind.Absolute)));
            if (ColorWheel.Source != null) return;
            TrySetImage(() =>
                new BitmapImage(new Uri("/CMDevicesManager;component/Assets/ColorWheelPicker.png", UriKind.Relative)));
            if (ColorWheel.Source != null) return;

            try
            {
                var fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ColorWheelPicker.png");
                if (File.Exists(fallback))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(fallback, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    ColorWheel.Source = bmp;
                }
            }
            catch { }

            if (ColorWheel.Source == null)
            {
                ColorWheel.Opacity = 0.4;
                ColorWheel.ToolTip = "Color wheel not found.";
            }
        }

        private void TrySetImage(Func<BitmapImage> factory)
        {
            try
            {
                var bmp = factory();
                bmp?.Freeze();
                if (bmp != null) ColorWheel.Source = bmp;
            }
            catch { }
        }

        private void CacheWheel()
        {
            if (ColorWheel.Source is not BitmapSource src) return;
            if (src.Format != PixelFormats.Bgra32)
            {
                var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                conv.Freeze();
                _wheelBmp = conv;
            }
            else _wheelBmp = src;
        }
        #endregion

        #region Wheel Square + DisplayRect
        private void WheelContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 强制正方形：取较小的一边
            double side = Math.Min(WheelContainer.ActualWidth, WheelContainer.ActualHeight);
            if (side <= 0) return;
            // 为了避免布局抖动，只设置高度（宽度由列决定）；若高度小则容器内部居中
            WheelContainer.Height = side;

            RecalcDisplayRect();
        }

        private void RecalcDisplayRect()
        {
            if (ColorWheel.Source is not BitmapSource bmp)
            {
                _displayRect = Rect.Empty;
                return;
            }

            double hostW = WheelContainer.ActualWidth;
            double hostH = WheelContainer.ActualHeight;
            if (hostW < 2 || hostH < 2)
            {
                _displayRect = Rect.Empty;
                return;
            }

            // Stretch=Uniform → 计算 letterbox 区域
            double imgW = bmp.PixelWidth;
            double imgH = bmp.PixelHeight;
            double imgAspect = imgW / imgH;
            double hostAspect = hostW / hostH;

            double drawW, drawH, offsetX, offsetY;
            if (hostAspect > imgAspect)
            {
                drawH = hostH;
                drawW = drawH * imgAspect;
                offsetX = (hostW - drawW) / 2.0;
                offsetY = 0;
            }
            else
            {
                drawW = hostW;
                drawH = drawW / imgAspect;
                offsetX = 0;
                offsetY = (hostH - drawH) / 2.0;
            }
            _displayRect = new Rect(offsetX, offsetY, drawW, drawH);
        }
        #endregion

        #region Picking
        private void ColorWheel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_wheelBmp == null) return;
            _dragPicking = true;
            ColorWheel.CaptureMouse();
            Sample(e.GetPosition(WheelContainer));
        }

        private void ColorWheel_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragPicking && e.LeftButton == MouseButtonState.Pressed)
                Sample(e.GetPosition(WheelContainer));
        }

        private void ColorWheel_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragPicking)
            {
                _dragPicking = false;
                ColorWheel.ReleaseMouseCapture();
            }
        }

        private void Sample(Point p)
        {
            if (_wheelBmp == null || _displayRect == Rect.Empty) return;
            if (!_displayRect.Contains(p)) return;

            double nx = (p.X - _displayRect.X) / _displayRect.Width;
            double ny = (p.Y - _displayRect.Y) / _displayRect.Height;

            nx = Math.Clamp(nx, 0, 1);
            ny = Math.Clamp(ny, 0, 1);

            int px = (int)(nx * (_wheelBmp.PixelWidth - 1));
            int py = (int)(ny * (_wheelBmp.PixelHeight - 1));
            if (px < 0 || py < 0 || px >= _wheelBmp.PixelWidth || py >= _wheelBmp.PixelHeight) return;

            try
            {
                var cb = new CroppedBitmap(_wheelBmp, new Int32Rect(px, py, 1, 1));
                byte[] buf = new byte[4];
                cb.CopyPixels(buf, 4, 0);
                SelectedColor = Color.FromRgb(buf[2], buf[1], buf[0]);
                UpdateUI();
                PositionMarker(p);
            }
            catch { }
        }

        private void PositionMarker(Point p)
        {
            // Clamp inside display rect
            double x = Math.Max(_displayRect.X, Math.Min(_displayRect.Right, p.X));
            double y = Math.Max(_displayRect.Y, Math.Min(_displayRect.Bottom, p.Y));
            Canvas.SetLeft(Marker, x - Marker.Width / 2);
            Canvas.SetTop(Marker, y - Marker.Height / 2);
            Marker.Visibility = Visibility.Visible;
        }
        #endregion
        private void WindowDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
        #region Bars (0-255 full)
        private void BarHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateBars();

        private void UpdateBars()
        {
            if (!_isLoadedBarsWidthReady()) return;

            SetBarWidth(BarR, BarRHost, SelectedColor.R);
            SetBarWidth(BarG, BarGHost, SelectedColor.G);
            SetBarWidth(BarB, BarBHost, SelectedColor.B);
        }

        private bool _isLoadedBarsWidthReady()
            => BarRHost.ActualWidth > 1 && BarGHost.ActualWidth > 1 && BarBHost.ActualWidth > 1;

        private void SetBarWidth(FrameworkElement bar, FrameworkElement host, int value0To255)
        {
            double w = host.ActualWidth * (value0To255 / 255.0);
            bar.Width = w < 0 ? 0 : w;
        }
        #endregion

        #region UI
        private void UpdateUI()
        {
            _suppress = true;
            TxtR.Text = SelectedColor.R.ToString();
            TxtG.Text = SelectedColor.G.ToString();
            TxtB.Text = SelectedColor.B.ToString();
            TxtHex.Text = $"{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
            SmallPreviewBrush.Color = SelectedColor;
            LargePreviewBrush.Color = SelectedColor;
            PreviewLabel.Text = $"#{TxtHex.Text}";
            _suppress = false;
            UpdateBars();
        }
        #endregion

        #region Events (RGB / Hex / Keys / Presets)
        private static bool TryByte(string s, out byte b)
        {
            b = 0;
            if (!int.TryParse(s, out int v)) return false;
            if (v < 0 || v > 255) return false;
            b = (byte)v;
            return true;
        }

        private void Rgb_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppress) return;
            if (!TryByte(TxtR.Text, out var r)) return;
            if (!TryByte(TxtG.Text, out var g)) return;
            if (!TryByte(TxtB.Text, out var b)) return;
            SelectedColor = Color.FromRgb(r, g, b);
            UpdateUI();
        }

        private void TxtHex_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppress) return;
            var hex = TxtHex.Text.Trim().TrimStart('#');
            if (Regex.IsMatch(hex, "^[0-9A-Fa-f]{6}$"))
            {
                byte r = Convert.ToByte(hex[..2], 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                SelectedColor = Color.FromRgb(r, g, b);
                UpdateUI();
            }
        }

        private void Rgb_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Background: SolidColorBrush scb })
            {
                SelectedColor = scb.Color;
                UpdateUI();
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void OnKeyDownSimple(object? sender, KeyEventArgs e)
        {
            bool changed = false;
            int r = SelectedColor.R, g = SelectedColor.G, b = SelectedColor.B;

            switch (e.Key)
            {
                case Key.Up: r = Math.Min(255, r + 1); changed = true; break;
                case Key.Down: r = Math.Max(0, r - 1); changed = true; break;
                case Key.Right: g = Math.Min(255, g + 1); changed = true; break;
                case Key.Left: g = Math.Max(0, g - 1); changed = true; break;
                case Key.PageUp: b = Math.Min(255, b + 1); changed = true; break;
                case Key.PageDown: b = Math.Max(0, b - 1); changed = true; break;
            }

            if (changed)
            {
                SelectedColor = Color.FromRgb((byte)r, (byte)g, (byte)b);
                UpdateUI();
                e.Handled = true;
            }
        }
        #endregion
    }
}