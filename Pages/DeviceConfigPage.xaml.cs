using CMDevicesManager.Models;
using CMDevicesManager.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using WF = System.Windows.Forms;

namespace CMDevicesManager.Pages
{
    public partial class DeviceConfigPage : Page, INotifyPropertyChanged
    {
        private readonly DeviceInfos _device;

        // System info service + live text update
        private readonly ISystemMetricsService _metrics;
        private readonly DispatcherTimer _liveTimer;

        // Dynamic system info buttons
        public ObservableCollection<SystemInfoItem> SystemInfoItems { get; } = new();

        // Live text registry
        private sealed class LiveTextItem
        {
            public Border Border { get; init; } = null!;
            public TextBlock Text { get; init; } = null!;
            public LiveInfoKind Kind { get; init; }
        }
        private readonly List<LiveTextItem> _liveItems = new();

        public enum LiveInfoKind
        {
            CpuUsage,
            GpuUsage,
            DateTime
        }

        // Design surface config
        private int _canvasSize = 512;
        public int CanvasSize { get => _canvasSize; set { if (_canvasSize != value && value > 0) { _canvasSize = value; OnPropertyChanged(); } } }

        // Background
        private Color _backgroundColor = Colors.Black;
        public Color BackgroundColor { get => _backgroundColor; set { _backgroundColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(BackgroundBrush)); BackgroundHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}"; } }
        public Brush BackgroundBrush => new SolidColorBrush(BackgroundColor);
        private string? _backgroundImagePath;
        public string? BackgroundImagePath { get => _backgroundImagePath; set { _backgroundImagePath = value; OnPropertyChanged(); } }
        private double _backgroundImageOpacity = 1.0;
        public double BackgroundImageOpacity { get => _backgroundImageOpacity; set { _backgroundImageOpacity = Math.Clamp(value, 0, 1); OnPropertyChanged(); } }
        private string _backgroundHex = "#000000";
        public string BackgroundHex { get => _backgroundHex; set { if (_backgroundHex != value) { _backgroundHex = value; if (TryParseHexColor(value, out var c)) BackgroundColor = c; OnPropertyChanged(); } } }

        // Selection
        private Border? _selected;
        private ScaleTransform? _selScale;
        private TranslateTransform? _selTranslate;

        // Selected meta
        private string _selectedInfo = "None";
        public string SelectedInfo { get => _selectedInfo; set { _selectedInfo = value; OnPropertyChanged(); } }
        public bool IsAnySelected => _selected != null;
        public bool IsTextSelected => _selected?.Child is TextBlock;
        public bool IsSelectedTextReadOnly => _selected?.Tag is LiveInfoKind; // live text can't edit content

        // Selected props
        private double _selectedScale = 1.0;
        public double SelectedScale { get => _selectedScale; set { _selectedScale = Math.Clamp(value, 0.1, 5); ApplySelectedScale(); ClampSelectedIntoCanvas(); OnPropertyChanged(); } }
        private double _selectedOpacity = 1.0;
        public double SelectedOpacity { get => _selectedOpacity; set { _selectedOpacity = Math.Clamp(value, 0.1, 1); if (_selected != null) _selected.Opacity = _selectedOpacity; OnPropertyChanged(); } }

        // Text props
        private string _selectedText = string.Empty;
        public string SelectedText
        {
            get => _selectedText;
            set
            {
                _selectedText = value;
                if (_selected?.Child is TextBlock tb && _selected?.Tag is not LiveInfoKind)
                {
                    tb.Text = value;
                }
                UpdateSelectedInfo();
                OnPropertyChanged();
            }
        }
        private double _selectedFontSize = 24;
        public double SelectedFontSize { get => _selectedFontSize; set { _selectedFontSize = value; if (_selected?.Child is TextBlock tb) tb.FontSize = value; OnPropertyChanged(); } }
        private Color _selectedTextColor = Colors.White;
        public Color SelectedTextColor { get => _selectedTextColor; set { _selectedTextColor = value; if (_selected?.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(value); SelectedTextHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}"; OnPropertyChanged(); } }
        private string _selectedTextHex = "#FFFFFF";
        public string SelectedTextHex { get => _selectedTextHex; set { if (_selectedTextHex != value) { _selectedTextHex = value; if (TryParseHexColor(value, out var c)) SelectedTextColor = c; OnPropertyChanged(); } } }

        // Export
        private string _outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CMDevicesManager");
        public string OutputFolder { get => _outputFolder; set { _outputFolder = value; OnPropertyChanged(); } }

        // Dragging
        private bool _isDragging;
        private Point _dragStart;
        private double _dragStartX, _dragStartY;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
            if (p is nameof(_selected))
            {
                OnPropertyChanged(nameof(IsAnySelected));
                OnPropertyChanged(nameof(IsTextSelected));
                OnPropertyChanged(nameof(IsSelectedTextReadOnly));
            }
        }

        public DeviceConfigPage(DeviceInfos device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            InitializeComponent();
            DataContext = this;

            // Metrics service
            _metrics = new RealSystemMetricsService();

            // Build dynamic system info buttons
            BuildSystemInfoButtons();

            // Ensure default export folder exists
            try { Directory.CreateDirectory(OutputFolder); } catch { /* ignore */ }

            // Canvas handlers
            DesignCanvas.PreviewMouseLeftButtonDown += DesignCanvas_PreviewMouseLeftButtonDown;
            DesignCanvas.PreviewMouseWheel += DesignCanvas_PreviewMouseWheel;

            // Live timer
            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _liveTimer.Tick += LiveTimer_Tick;
            _liveTimer.Start();

            Unloaded += DeviceConfigPage_Unloaded;
        }

        private void DeviceConfigPage_Unloaded(object sender, RoutedEventArgs e)
        {
            try { _liveTimer.Stop(); } catch { }
            try { _metrics.Dispose(); } catch { }
        }

        private void BuildSystemInfoButtons()
        {
            SystemInfoItems.Clear();

            // CPU is generally available
            SystemInfoItems.Add(new SystemInfoItem(LiveInfoKind.CpuUsage, $"CPU Usage ({_metrics.CpuName})"));

            // GPU: add only if likely present
            var gpuName = (_metrics as RealSystemMetricsService)?.PrimaryGpuName ?? "GPU";
            var gpuVal = _metrics.GetGpuUsagePercent();
            if (!string.Equals(gpuName, "GPU", StringComparison.OrdinalIgnoreCase) || gpuVal > 0)
            {
                SystemInfoItems.Add(new SystemInfoItem(LiveInfoKind.GpuUsage, $"GPU Usage ({gpuName})"));
            }
        }

        public sealed record SystemInfoItem(LiveInfoKind Kind, string DisplayName);

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
            else NavigationService?.Navigate(new DevicePage());
        }

        // ===================== Add Date (live, read-only) =====================
        private void AddClock_Click(object sender, RoutedEventArgs e)
        {
            var tb = new TextBlock
            {
                Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Foreground = Brushes.White,
                FontSize = 32,
                FontWeight = FontWeights.SemiBold
            };
            // Apply current app font family
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

            var border = AddElement(tb, "Date/Time");

            // Mark as live and register
            border.Tag = LiveInfoKind.DateTime;
            _liveItems.Add(new LiveTextItem { Border = border, Text = tb, Kind = LiveInfoKind.DateTime });

            // If selected now, lock editing and reflect value
            if (_selected == border)
            {
                OnPropertyChanged(nameof(IsSelectedTextReadOnly));
                _selectedText = tb.Text;
                OnPropertyChanged(nameof(SelectedText));
            }
        }

        // ===================== System Info click -> add live text =====================
        private void AddSystemInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not LiveInfoKind kind) return;

            var tb = new TextBlock
            {
                Text = kind == LiveInfoKind.CpuUsage ? $"CPU {Math.Round(_metrics.GetCpuUsagePercent())}%" : $"GPU {Math.Round(_metrics.GetGpuUsagePercent())}%",
                Foreground = Brushes.White,
                FontSize = 32,
                FontWeight = FontWeights.SemiBold
            };
            // Apply current app font family
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

            var border = AddElement(tb, kind == LiveInfoKind.CpuUsage ? "CPU Usage" : "GPU Usage");

            // Mark as live and register
            border.Tag = kind;
            _liveItems.Add(new LiveTextItem { Border = border, Text = tb, Kind = kind });

            // If selected now, make text box read-only
            if (_selected == border)
            {
                OnPropertyChanged(nameof(IsSelectedTextReadOnly));
                _selectedText = tb.Text; // reflect into right panel
            }
        }

        private void LiveTimer_Tick(object? sender, EventArgs e)
        {
            // Read once per tick
            double cpu = _metrics.GetCpuUsagePercent();
            double gpu = _metrics.GetGpuUsagePercent();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            foreach (var item in _liveItems.ToArray())
            {
                string text = item.Kind switch
                {
                    LiveInfoKind.CpuUsage => $"CPU {Math.Round(cpu)}%",
                    LiveInfoKind.GpuUsage => $"GPU {Math.Round(gpu)}%",
                    LiveInfoKind.DateTime => now,
                    _ => item.Text.Text
                };

                item.Text.Text = text;

                if (_selected == item.Border)
                {
                    // Update right panel display (read-only textbox)
                    _selectedText = text;
                    OnPropertyChanged(nameof(SelectedText));
                    UpdateSelectedInfo();
                }
            }
        }

        // ===================== Add / Clear =====================
        private void AddText_Click(object sender, RoutedEventArgs e)
        {
            var tb = new TextBlock
            {
                Text = "Text",
                Foreground = Brushes.White,
                FontSize = 32,
                FontWeight = FontWeights.SemiBold
            };
            // Apply current app font family
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
            AddElement(tb, "Text");
        }

        private void AddImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                var img = new Image
                {
                    Source = new BitmapImage(new Uri(dlg.FileName)),
                    Stretch = Stretch.Uniform
                };
                AddElement(img, System.IO.Path.GetFileName(dlg.FileName));
            }
        }

        private void ClearCanvas_Click(object sender, RoutedEventArgs e)
        {
            DesignCanvas.Children.Clear();
            _liveItems.Clear();
            SetSelected(null);
        }

        // ===================== Export =====================
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var prevBorderBrush = _selected?.BorderBrush;
                var prevBorderThickness = _selected?.BorderThickness;
                if (_selected != null)
                {
                    _selected.BorderBrush = Brushes.Transparent;
                    _selected.BorderThickness = new Thickness(0);
                }

                var rtb = new RenderTargetBitmap(CanvasSize, CanvasSize, 96, 96, PixelFormats.Pbgra32);
                DesignRoot.Measure(new Size(CanvasSize, CanvasSize));
                DesignRoot.Arrange(new Rect(0, 0, CanvasSize, CanvasSize));
                rtb.Render(DesignRoot);

                if (_selected != null && prevBorderBrush != null && prevBorderThickness != null)
                {
                    _selected.BorderBrush = prevBorderBrush;
                    _selected.BorderThickness = prevBorderThickness.Value;
                }

                Directory.CreateDirectory(OutputFolder);
                var name = (_device?.Name ?? "Device");
                var file = System.IO.Path.Combine(OutputFolder, $"{SanitizeFileName(name)}_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(file);
                encoder.Save(fs);

                System.Windows.MessageBox.Show($"Exported: {file}", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===================== Background =====================
        private void PickBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WF.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(BackgroundColor.A, BackgroundColor.R, BackgroundColor.G, BackgroundColor.B)
            };
            if (dlg.ShowDialog() == WF.DialogResult.OK)
            {
                BackgroundColor = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
            }
        }

        private void PickBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Background Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                BackgroundImagePath = dlg.FileName;
            }
        }

        private void ClearBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            BackgroundImagePath = null;
        }

        private void PickSelectedTextColor_Click(object sender, RoutedEventArgs e)
        {
            if (_selected?.Child is not TextBlock) return;

            using var dlg = new WF.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(SelectedTextColor.A, SelectedTextColor.R, SelectedTextColor.G, SelectedTextColor.B)
            };
            if (dlg.ShowDialog() == WF.DialogResult.OK)
            {
                SelectedTextColor = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
            }
        }

        // ===================== Z-Order / Delete =====================
        private void BringToFront_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            int max = 0;
            foreach (UIElement child in DesignCanvas.Children) max = Math.Max(max, Canvas.GetZIndex(child));
            Canvas.SetZIndex(_selected, max + 1);
        }

        private void SendToBack_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            int min = 0;
            foreach (UIElement child in DesignCanvas.Children) min = Math.Min(min, Canvas.GetZIndex(child));
            Canvas.SetZIndex(_selected, min - 1);
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;

            // Unregister live item if any
            var live = _liveItems.FirstOrDefault(i => i.Border == _selected);
            if (live != null) _liveItems.Remove(live);

            DesignCanvas.Children.Remove(_selected);
            SetSelected(null);
        }

        private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WF.FolderBrowserDialog
            {
                Description = "Select output folder",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(OutputFolder) ? OutputFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };
            if (dlg.ShowDialog() == WF.DialogResult.OK)
            {
                OutputFolder = dlg.SelectedPath;
            }
        }

        // ===================== Element creation / selection =====================
        private Border AddElement(FrameworkElement element, string label)
        {
            element.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            element.VerticalAlignment = VerticalAlignment.Top;

            var border = new Border
            {
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Child = element,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Cursor = Cursors.SizeAll
            };

            var tg = new TransformGroup();
            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform(0, 0);
            tg.Children.Add(scale);
            tg.Children.Add(translate);
            border.RenderTransform = tg;

            border.PreviewMouseLeftButtonDown += Item_PreviewMouseLeftButtonDown;
            border.PreviewMouseLeftButtonUp += Item_PreviewMouseLeftButtonUp;
            border.PreviewMouseMove += Item_PreviewMouseMove;
            border.MouseDown += (_, __) => SelectElement(border);
            border.MouseEnter += (_, __) => { if (_selected != border) border.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 30, 144, 255)); };
            border.MouseLeave += (_, __) => { if (_selected != border) border.BorderBrush = Brushes.Transparent; };

            DesignCanvas.Children.Add(border);
            border.Loaded += (_, __) =>
            {
                SelectElement(border);
                SelectedOpacity = 1.0;

                double w = border.ActualWidth;
                double h = border.ActualHeight;

                if (GetTransforms(border, out var sc, out var tr))
                {
                    if (element is Image)
                    {
                        // Cover canvas & center
                        double scaleToCover = (w > 0 && h > 0) ? Math.Max(CanvasSize / w, CanvasSize / h) : 1.0;
                        sc.ScaleX = sc.ScaleY = scaleToCover;
                        SelectedScale = scaleToCover;

                        double scaledW = w * scaleToCover;
                        double scaledH = h * scaleToCover;
                        tr.X = (CanvasSize - scaledW) / 2.0;
                        tr.Y = (CanvasSize - scaledH) / 2.0;
                    }
                    else
                    {
                        SelectedScale = 1.0;
                        tr.X = (CanvasSize - w) / 2.0;
                        tr.Y = (CanvasSize - h) / 2.0;
                    }
                }

                ClampIntoCanvas(border);
                UpdateSelectedInfo();
            };

            return border;
        }

        private void SelectElement(Border border)
        {
            if (_selected != null && _selected != border)
            {
                _selected.BorderBrush = Brushes.Transparent;
            }

            _selected = border;
            _selected.BorderBrush = Brushes.DodgerBlue;

            if (GetTransforms(border, out var scale, out var translate))
            {
                _selScale = scale;
                _selTranslate = translate;
            }

            SelectedOpacity = _selected.Opacity;
            SelectedScale = _selScale?.ScaleX ?? 1.0;

            if (_selected.Child is TextBlock tb)
            {
                SelectedText = tb.Text;
                SelectedFontSize = tb.FontSize;
                if (tb.Foreground is SolidColorBrush scb)
                {
                    var c = scb.Color;
                    SelectedTextColor = Color.FromArgb(c.A, c.R, c.G, c.B);
                }
                else
                {
                    SelectedTextColor = Colors.White;
                }
            }

            UpdateSelectedInfo();
            OnPropertyChanged(nameof(_selected));
        }

        private void SetSelected(Border? border)
        {
            if (_selected != null)
            {
                _selected.BorderBrush = Brushes.Transparent;
            }
            _selected = border;
            if (_selected != null) _selected.BorderBrush = Brushes.DodgerBlue;

            if (_selected != null && GetTransforms(_selected, out var sc, out var tr))
            {
                _selScale = sc;
                _selTranslate = tr;
                SelectedOpacity = _selected.Opacity;
                SelectedScale = _selScale.ScaleX;
            }
            else
            {
                _selScale = null;
                _selTranslate = null;
                SelectedInfo = "None";
            }

            OnPropertyChanged(nameof(_selected));
        }

        private void UpdateSelectedInfo()
        {
            if (_selected == null)
            {
                SelectedInfo = "None";
                return;
            }
            if (_selected.Child is TextBlock)
            {
                if (_selected.Tag is LiveInfoKind k)
                {
                    SelectedInfo = k switch
                    {
                        LiveInfoKind.CpuUsage => "CPU Usage",
                        LiveInfoKind.GpuUsage => "GPU Usage",
                        LiveInfoKind.DateTime => "Date/Time",
                        _ => "Live Text"
                    };
                }
                else
                {
                    SelectedInfo = $"Text: \"{SelectedText}\"";
                }
            }
            else if (_selected.Child is Image img)
            {
                var src = img.Source as BitmapSource;
                SelectedInfo = src != null ? $"Image: {src.PixelWidth}x{src.PixelHeight}" : "Image";
            }
            else
            {
                SelectedInfo = "Element";
            }
        }

        private static bool GetTransforms(Border border, out ScaleTransform scale, out TranslateTransform translate)
        {
            if (border.RenderTransform is TransformGroup tg)
            {
                scale = tg.Children.OfType<ScaleTransform>().FirstOrDefault() ?? new ScaleTransform(1, 1);
                translate = tg.Children.OfType<TranslateTransform>().FirstOrDefault() ?? new TranslateTransform(0, 0);
                return true;
            }
            scale = new ScaleTransform(1, 1);
            translate = new TranslateTransform(0, 0);
            return false;
        }

        // ===================== Dragging & Canvas constraints =====================
        private void Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border b) return;
            SelectElement(b);

            if (GetTransforms(b, out _, out var tr))
            {
                _isDragging = true;
                _dragStart = e.GetPosition(DesignCanvas);
                _dragStartX = tr.X;
                _dragStartY = tr.Y;
                b.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Item_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _selected == null || _selTranslate == null) return;

            var p = e.GetPosition(DesignCanvas);
            var dx = p.X - _dragStart.X;
            var dy = p.Y - _dragStart.Y;

            var newX = _dragStartX + dx;
            var newY = _dragStartY + dy;

            (newX, newY) = GetClampedPosition(_selected, newX, newY);

            _selTranslate.X = newX;
            _selTranslate.Y = newY;
            e.Handled = true;
        }

        private void Item_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b)
            {
                _isDragging = false;
                b.ReleaseMouseCapture();
                ClampIntoCanvas(b);
                e.Handled = true;
            }
        }

        private void DesignCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source == DesignCanvas)
            {
                SetSelected(null);
            }
        }

        private void DesignCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (_selected == null) return;

            var delta = e.Delta > 0 ? 0.05 : -0.05;
            SelectedScale = Math.Clamp(SelectedScale + delta, 0.1, 5.0);
            e.Handled = true;
        }

        private (double X, double Y) GetClampedPosition(Border border, double x, double y)
        {
            double scaledW = border.ActualWidth * (_selScale?.ScaleX ?? 1.0);
            double scaledH = border.ActualHeight * (_selScale?.ScaleY ?? 1.0);

            // Allow negative offsets when element is larger than the canvas
            double minX = Math.Min(0, CanvasSize - scaledW);
            double maxX = Math.Max(0, CanvasSize - scaledW);
            double minY = Math.Min(0, CanvasSize - scaledH);
            double maxY = Math.Max(0, CanvasSize - scaledH);

            x = Math.Clamp(x, minX, maxX);
            y = Math.Clamp(y, minY, maxY);
            return (x, y);
        }

        private void ClampIntoCanvas(Border border)
        {
            if (!GetTransforms(border, out _, out var tr)) return;
            (tr.X, tr.Y) = GetClampedPosition(border, tr.X, tr.Y);
        }

        private void ClampSelectedIntoCanvas()
        {
            if (_selected == null) return;
            ClampIntoCanvas(_selected);
        }

        // ===================== Helpers =====================
        private static bool TryParseHexColor(string? hex, out Color color)
        {
            color = Colors.Transparent;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            hex = hex.Trim();
            if (hex.StartsWith("#")) hex = hex[1..];

            try
            {
                if (hex.Length == 6)
                {
                    byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    color = Color.FromRgb(r, g, b);
                    return true;
                }
                if (hex.Length == 8)
                {
                    byte a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    byte r = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
            }
            catch { /* ignore */ }

            return false;
        }

        private void ApplySelectedScale()
        {
            if (_selScale != null)
            {
                _selScale.ScaleX = _selectedScale;
                _selScale.ScaleY = _selectedScale;
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}