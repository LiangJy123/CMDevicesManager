using CMDevicesManager.Helper;
using CMDevicesManager.Models;
using CMDevicesManager.Services;
using CMDevicesManager.Utilities;
using CMDevicesManager.Windows;
using HID.DisplayController;
using HidApi;
using Instances;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static CMDevicesManager.Pages.DeviceConfigPage;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;
using WF = System.Windows.Forms;

namespace CMDevicesManager.Pages
{
    // ================= Configuration Data Models =================
    public class CanvasConfiguration
    {
        public string ConfigName { get; set; } = "Untitled";
        public int CanvasSize { get; set; }
        public string BackgroundColor { get; set; } = "#000000";
        public string? BackgroundImagePath { get; set; }
        public double BackgroundImageOpacity { get; set; }
        public double MoveSpeed { get; set; } = 100;              // NEW: global auto-move speed
        public List<ElementConfiguration> Elements { get; set; } = new();
    }

    public class ElementConfiguration
    {
        public string Type { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Scale { get; set; }
        public double Opacity { get; set; }
        public int ZIndex { get; set; }

        // Text
        public string? Text { get; set; }
        public double? FontSize { get; set; }
        public string? TextColor { get; set; }
        public bool? UseTextGradient { get; set; }
        public string? TextColor2 { get; set; }

        // Image
        public string? ImagePath { get; set; }
        public double? Rotation { get; set; }        // NEW: image rotation
        public bool? MirroredX { get; set; }         // NEW: image horizontal mirror

        // Live
        public LiveInfoKind? LiveKind { get; set; }

        // Video
        public string? VideoPath { get; set; }

        // DateTime format
        public string? DateFormat { get; set; }

        // Usage display style
        public string? UsageDisplayStyle { get; set; }
        public string? UsageStartColor { get; set; }
        public string? UsageEndColor { get; set; }
        public string? UsageNeedleColor { get; set; }
        public string? UsageBarBackgroundColor { get; set; }

        // Auto-move (plain text)
        public double? MoveDirX { get; set; }        // NEW: per-element move vector X
        public double? MoveDirY { get; set; }        // NEW: per-element move vector Y
    }

    // ================= Simple Dialogs =================
    public class ConfigNameDialog : Window
    {
        private TextBox _nameTextBox;
        public string ConfigName { get; private set; } = "";

        public ConfigNameDialog(string defaultName = "")
        {
            Title = Application.Current.FindResource("ConfigNameTitle")?.ToString() ?? "Configuration Name";
            Width = 400;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = Application.Current.FindResource("ConfigNamePrompt")?.ToString() ?? "Please enter configuration name:",
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 14
            };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            _nameTextBox = new TextBox
            {
                Text = defaultName,
                Margin = new Thickness(0, 0, 0, 20),
                FontSize = 14,
                Padding = new Thickness(5)
            };
            Grid.SetRow(_nameTextBox, 1);
            grid.Children.Add(_nameTextBox);

            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var okButton = new Button
            {
                Content = Application.Current.FindResource("OkButton")?.ToString() ?? "OK",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) =>
            {
                ConfigName = _nameTextBox.Text?.Trim() ?? "";
                DialogResult = true;
            };

            var cancelButton = new Button
            {
                Content = Application.Current.FindResource("CancelButton")?.ToString() ?? "Cancel",
                Width = 80,
                Height = 30,
                IsCancel = true
            };
            cancelButton.Click += (s, e) => DialogResult = false;

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            Content = grid;

            Loaded += (s, e) =>
            {
                _nameTextBox.SelectAll();
                _nameTextBox.Focus();
            };
        }
    }

    public class ConfigSelectionDialog : Window
    {
        private ListBox _configListBox;
        public CanvasConfiguration? SelectedConfig { get; private set; }
        public string? SelectedConfigPath { get; private set; }

        public ConfigSelectionDialog(List<(string path, CanvasConfiguration config)> configs)
        {
            Title = Application.Current.FindResource("SelectConfigTitle")?.ToString() ?? "Select Configuration";
            Width = 500;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _configListBox = new ListBox
            {
                Margin = new Thickness(0, 0, 0, 20),
                DisplayMemberPath = "Name",
                SelectedValuePath = "Config"
            };

            var items = configs.Select(c => new
            {
                Name = $"{c.config.ConfigName} - {Path.GetFileNameWithoutExtension(c.path)}",
                Config = c.config,
                Path = c.path
            }).ToList();

            _configListBox.ItemsSource = items;
            _configListBox.MouseDoubleClick += (s, e) =>
            {
                if (_configListBox.SelectedItem != null) AcceptSelection();
            };

            Grid.SetRow(_configListBox, 0);
            grid.Children.Add(_configListBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var loadButton = new Button
            {
                Content = Application.Current.FindResource("LoadButton")?.ToString() ?? "Load",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            loadButton.Click += (s, e) => AcceptSelection();

            var cancelButton = new Button
            {
                Content = Application.Current.FindResource("CancelButton")?.ToString() ?? "Cancel",
                Width = 80,
                Height = 30,
                IsCancel = true
            };
            cancelButton.Click += (s, e) => DialogResult = false;

            buttonPanel.Children.Add(loadButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }

        private void AcceptSelection()
        {
            if (_configListBox.SelectedItem != null)
            {
                dynamic selected = _configListBox.SelectedItem;
                SelectedConfig = selected.Config;
                SelectedConfigPath = selected.Path;
                DialogResult = true;
            }
        }
    }

    // ================= Main Page =================
    public partial class DeviceConfigPage : Page, INotifyPropertyChanged
    {
        private const string GlobalConfigFileName = "globalconfig1.json";
        private static string UserPrefsFilePath => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userprefs.json");
        private sealed class UserPrefsModel
        {
            public bool SuppressPlayModePrompt { get; set; }
        }
        private UserPrefsModel _userPrefsCache;
        //private readonly DeviceInfo _device;
        private readonly ISystemMetricsService _metrics;
        private readonly DispatcherTimer _liveTimer;

        private DeviceInfo? _deviceInfo;

        public ObservableCollection<SystemInfoItem> SystemInfoItems { get; } = new();

        private const double GaugeStartAngle = 150;   // 0%  左下
        private const double GaugeEndAngle = 390;   // 100% 右下(30°+360 用于插值走上方大弧)
        private const double GaugeSweep = GaugeEndAngle - GaugeStartAngle; // 240°
        private const double GaugeRadiusOuter = 70;
        private const double GaugeRadiusInner = 60;
        private const double GaugeNeedleLength = 56;
        private const int GaugeMajorStep = 10;
        private const int GaugeMinorStep = 5;
        private const int GaugeLabelStep = 25;
        private const double GaugeCenterX = 80;
        private const double GaugeCenterY = 90;    // 中心下移，让上弧更靠容器顶部显示

        // 角度插值工具
        private static double GaugeAngleFromPercent(double percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            return GaugeStartAngle + GaugeSweep * (percent / 100.0);
        }
        private static Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        private void RecolorGaugeTicks(LiveTextItem item)
        {
            if (item.DisplayStyle != UsageDisplayStyle.Gauge) return;
            if (item.Border.Child is not Grid root) return;
            var canvas = root.Children.OfType<Canvas>().FirstOrDefault();
            if (canvas == null) return;

            foreach (var line in canvas.Children.OfType<Line>())
            {
                if (ReferenceEquals(line, item.GaugeNeedle)) continue;
                if (line.Tag is string tag && (tag == "TickMajor" || tag == "TickMinor"))
                {
                    // 从 Tag 解析百分比
                    if (line.DataContext is double p)
                    {
                        double t = p / 100.0;
                        var c = LerpColor(item.StartColor, item.EndColor, t);
                        if (tag == "TickMinor") c = Color.FromArgb(160, c.R, c.G, c.B); // 小刻度稍微淡一点
                        line.Stroke = new SolidColorBrush(c);
                    }
                }
            }
        }
        // 在 Gauge 常量后增加一个旋转偏移工具（放在 GaugeAngleFromPercent 下面即可）
        private const double GaugeNeedleAngleOffset = 270; // 线初始竖直向上(数学 270°)，需减去此偏移

        private static double GaugeRotationFromPercent(double percent)
        {
            // 将几何角(数学坐标系)转换为针在 WPF RotateTransform 中的角度
            return GaugeAngleFromPercent(percent) - GaugeNeedleAngleOffset; // 结果区间：-120° ~ +120°
        }

        private bool _loadedFromConfigFile;
        public bool LoadedFromConfigFile
        {
            get => _loadedFromConfigFile;
            private set { _loadedFromConfigFile = value; OnPropertyChanged(); }
        }

        public enum LiveInfoKind
        {
            CpuUsage,
            GpuUsage,
            DateTime,
            VideoPlayback
        }

        public enum UsageDisplayStyle
        {
            Text,
            ProgressBar,
            Gauge
        }

        private sealed class LiveTextItem
        {
            public Border Border { get; init; } = null!;
            public TextBlock Text { get; set; } = null!;
            public LiveInfoKind Kind { get; init; }
            public string DateFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";

            public UsageDisplayStyle DisplayStyle { get; set; } = UsageDisplayStyle.Text;

            // Theme colors
            public Color StartColor { get; set; } = Color.FromRgb(80, 180, 80);
            public Color EndColor { get; set; } = Color.FromRgb(40, 120, 40);
            public Color NeedleColor { get; set; } = Color.FromRgb(90, 200, 90);

            public Color BarBackgroundColor { get; set; } = Color.FromRgb(40, 46, 58);
            public Border? BarBackgroundBorder { get; set; }

            // ProgressBar visuals
            public Rectangle? BarFill { get; set; }

            // Gauge visuals
            public Line? GaugeNeedle { get; set; }
            public RotateTransform? GaugeNeedleRotate { get; set; }
        }

        private readonly List<LiveTextItem> _liveItems = new();

        private int _canvasSize = 512;
        public int CanvasSize { get => _canvasSize; set { if (_canvasSize != value && value > 0) { _canvasSize = value; OnPropertyChanged(); } } }

        private Color _backgroundColor = Colors.Black;
        public Color BackgroundColor { get => _backgroundColor; set { _backgroundColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(BackgroundBrush)); BackgroundHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}"; } }
        public Brush BackgroundBrush => new SolidColorBrush(BackgroundColor);
        private string? _backgroundImagePath;
        public string? BackgroundImagePath { get => _backgroundImagePath; set { _backgroundImagePath = value; OnPropertyChanged(); } }
        private double _backgroundImageOpacity = 1.0;
        public double BackgroundImageOpacity { get => _backgroundImageOpacity; set { _backgroundImageOpacity = Math.Clamp(value, 0, 1); OnPropertyChanged(); } }
        private string _backgroundHex = "#000000";
        public string BackgroundHex { get => _backgroundHex; set { if (_backgroundHex != value) { _backgroundHex = value; if (TryParseHexColor(value, out var c)) BackgroundColor = c; OnPropertyChanged(); } } }

        private Border? _selected;
        private ScaleTransform? _selScale;
        private TranslateTransform? _selTranslate;

        private string _selectedInfo = "None";
        public string SelectedInfo { get => _selectedInfo; set { _selectedInfo = value; OnPropertyChanged(); } }
        public bool IsAnySelected => _selected != null;

        public bool IsUsageSelected =>
            _selected?.Tag is LiveInfoKind k &&
            (k == LiveInfoKind.CpuUsage || k == LiveInfoKind.GpuUsage);

        public bool IsGaugeSelected => string.Equals(NormalizeUsageStyleString(SelectedUsageDisplayStyle), "Gauge", StringComparison.OrdinalIgnoreCase);
        public bool IsUsageVisualSelected
            => IsUsageSelected && !string.Equals(NormalizeUsageStyleString(SelectedUsageDisplayStyle), "Text", StringComparison.OrdinalIgnoreCase);
        private string? _loadedConfigFilePath;

        public bool IsTextSelected => GetCurrentTextBlock() != null && !IsUsageSelected;
        public bool IsSelectedTextReadOnly => _selected?.Tag is LiveInfoKind kind &&
                                              (kind == LiveInfoKind.CpuUsage || kind == LiveInfoKind.GpuUsage || kind == LiveInfoKind.DateTime);
        public bool IsImageSelected => _selected?.Child is Image && _selected.Tag is not VideoElementInfo;
        public bool IsDateTimeSelected => _selected?.Tag is LiveInfoKind kind && kind == LiveInfoKind.DateTime;

        private double _selectedScale = 1.0;
        public double SelectedScale
        {
            get => _selectedScale;
            set
            {
                _selectedScale = Math.Clamp(value, 0.1, 5);
                ApplySelectedScale();
                if (_selected?.Child is not Image)
                    ClampSelectedIntoCanvas();
                OnPropertyChanged();
            }
        }

        private double _selectedOpacity = 1.0;
        public double SelectedOpacity { get => _selectedOpacity; set { _selectedOpacity = Math.Clamp(value, 0.1, 1); if (_selected != null) _selected.Opacity = _selectedOpacity; OnPropertyChanged(); } }

        private string _selectedText = string.Empty;
        public string SelectedText
        {
            get => _selectedText;
            set
            {
                _selectedText = value;
                var tb = GetCurrentTextBlock();
                if (tb != null && !IsSelectedTextReadOnly) tb.Text = value;
                UpdateSelectedInfo();
                OnPropertyChanged();
            }
        }
        private double _selectedFontSize = 24;
        public double SelectedFontSize { get => _selectedFontSize; set { _selectedFontSize = value; var tb = GetCurrentTextBlock(); if (tb != null) tb.FontSize = value; OnPropertyChanged(); } }

        private Color _selectedTextColor = Colors.White;
        public Color SelectedTextColor
        {
            get => _selectedTextColor;
            set
            {
                _selectedTextColor = value;
                ApplyTextColorOrGradient();
                SelectedTextHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                OnPropertyChanged();
            }
        }
        private string _selectedTextHex = "#FFFFFF";
        public string SelectedTextHex { get => _selectedTextHex; set { if (_selectedTextHex != value) { _selectedTextHex = value; if (TryParseHexColor(value, out var c)) SelectedTextColor = c; OnPropertyChanged(); } } }

        private bool _useTextGradient = false;
        public bool UseTextGradient
        {
            get => _useTextGradient;
            set
            {
                _useTextGradient = value;
                ApplyTextColorOrGradient();
                OnPropertyChanged();
            }
        }

        private Color _selectedTextColor2 = Colors.White;
        public Color SelectedTextColor2
        {
            get => _selectedTextColor2;
            set
            {
                _selectedTextColor2 = value;
                ApplyTextColorOrGradient();
                SelectedTextHex2 = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                OnPropertyChanged();
            }
        }

        private string _selectedTextHex2 = "#FFFFFF";
        public string SelectedTextHex2
        {
            get => _selectedTextHex2;
            set
            {
                if (_selectedTextHex2 != value)
                {
                    _selectedTextHex2 = value;
                    if (TryParseHexColor(value, out var c))
                        SelectedTextColor2 = c;
                    OnPropertyChanged();
                }
            }
        }
        private Color _usageBarBackgroundColor = Color.FromRgb(40, 46, 58);
        public Color UsageBarBackgroundColor
        {
            get => _usageBarBackgroundColor;
            set
            {
                _usageBarBackgroundColor = value;
                UsageBarBackgroundHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                ApplyUsageTheme();
                OnPropertyChanged();
            }
        }

        private string _usageBarBackgroundHex = "#000000";
        public string UsageBarBackgroundHex
        {
            get => _usageBarBackgroundHex;
            set
            {
                if (_usageBarBackgroundHex != value)
                {
                    _usageBarBackgroundHex = value;
                    if (TryParseHexColor(value, out var c)) UsageBarBackgroundColor = c;
                    OnPropertyChanged();
                }
            }
        }

        // Usage Theme Properties
        private Color _usageStartColor = Color.FromRgb(80, 180, 80);
        public Color UsageStartColor
        {
            get => _usageStartColor;
            set
            {
                _usageStartColor = value;
                UsageStartHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                ApplyUsageTheme();
                OnPropertyChanged();
            }
        }
        private string _usageStartHex = "#50B450";
        public string UsageStartHex
        {
            get => _usageStartHex;
            set
            {
                if (_usageStartHex != value)
                {
                    _usageStartHex = value;
                    if (TryParseHexColor(value, out var c)) UsageStartColor = c;
                    OnPropertyChanged();
                }
            }
        }

        private Color _usageEndColor = Color.FromRgb(40, 120, 40);
        public Color UsageEndColor
        {
            get => _usageEndColor;
            set
            {
                _usageEndColor = value;
                UsageEndHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                ApplyUsageTheme();
                OnPropertyChanged();
            }
        }
        private string _usageEndHex = "#287828";
        public string UsageEndHex
        {
            get => _usageEndHex;
            set
            {
                if (_usageEndHex != value)
                {
                    _usageEndHex = value;
                    if (TryParseHexColor(value, out var c)) UsageEndColor = c;
                    OnPropertyChanged();
                }
            }
        }

        private Color _usageNeedleColor = Color.FromRgb(90, 200, 90);
        public Color UsageNeedleColor
        {
            get => _usageNeedleColor;
            set
            {
                _usageNeedleColor = value;
                UsageNeedleHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                ApplyUsageTheme();
                OnPropertyChanged();
            }
        }
        private string _usageNeedleHex = "#5AC85A";
        public string UsageNeedleHex
        {
            get => _usageNeedleHex;
            set
            {
                if (_usageNeedleHex != value)
                {
                    _usageNeedleHex = value;
                    if (TryParseHexColor(value, out var c)) UsageNeedleColor = c;
                    OnPropertyChanged();
                }
            }
        }

        private string _selectedUsageDisplayStyle = "Text";
        public string SelectedUsageDisplayStyle
        {
            get => _selectedUsageDisplayStyle;
            set
            {
                if (value == null) return;
                var normalized = NormalizeUsageStyleString(value);
                if (_selectedUsageDisplayStyle == normalized) return;
                _selectedUsageDisplayStyle = normalized;
                OnPropertyChanged();
                ApplySelectedUsageStyle();
                OnPropertyChanged(nameof(IsGaugeSelected));
                OnPropertyChanged(nameof(IsUsageVisualSelected));
            }
        }

        private string _outputFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CMDevicesManager");
        public string OutputFolder { get => _outputFolder; set { _outputFolder = value; OnPropertyChanged(); } }

        private bool _isDragging;
        private Point _dragStart;
        private double _dragStartX, _dragStartY;

        private DispatcherTimer? _mp4Timer;
        private List<VideoFrameData>? _currentVideoFrames;
        private int _currentFrameIndex = 0;
        private Image? _currentVideoImage;
        private Border? _currentVideoBorder;

        private string _currentConfigName = "";
        private string CurrentConfigName
        {
            get => _currentConfigName;
            set
            {
                _currentConfigName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConfigurationDisplayName));
            }
        }

        public string ConfigurationDisplayName
            => string.IsNullOrWhiteSpace(CurrentConfigName)
                ? (Application.Current.FindResource("UnsavedConfig")?.ToString() ?? "Unsaved Configuration")
                : CurrentConfigName;

        // Auto-move
        private DispatcherTimer _autoMoveTimer;
        private DateTime _lastAutoMoveTime = DateTime.MinValue;
        private bool _isJoystickDragging;
        private const double JoystickBaseSize = 120.0;
        private const double JoystickKnobSize = 32.0;
        private const double JoystickMaxOffset = (JoystickBaseSize - JoystickKnobSize) / 2.0;

        private double _moveSpeed = 100;
        public double MoveSpeed
        {
            get => _moveSpeed;
            set
            {
                if (Math.Abs(_moveSpeed - value) > 0.001)
                {
                    _moveSpeed = Math.Max(0, value);
                    OnPropertyChanged();
                }
            }
        }
        private double _moveDirX = 0;
        public double MoveDirX
        {
            get => _moveDirX;
            private set { if (Math.Abs(_moveDirX - value) > 0.0001) { _moveDirX = value; OnPropertyChanged(); } }
        }
        private double _moveDirY = 0;
        public double MoveDirY
        {
            get => _moveDirY;
            private set { if (Math.Abs(_moveDirY - value) > 0.0001) { _moveDirY = value; OnPropertyChanged(); } }
        }

        private readonly Dictionary<Border, (double dirX, double dirY)> _movingDirections = new();

        public ObservableCollection<string> DateFormats { get; } = new()
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy/MM/dd HH:mm",
            "yyyy-MM-dd",
            "MM-dd HH:mm",
            "HH:mm:ss",
            "HH:mm",
            "yyyy年MM月dd日 HH:mm:ss",
            "ddd HH:mm:ss"
        };

        private string _selectedDateFormat = "yyyy-MM-dd HH:mm:ss";
        public string SelectedDateFormat
        {
            get => _selectedDateFormat;
            set
            {
                if (_selectedDateFormat == value) return;
                _selectedDateFormat = value;
                if (IsDateTimeSelected)
                {
                    var item = _liveItems.FirstOrDefault(i => i.Border == _selected);
                    if (item != null && item.Kind == LiveInfoKind.DateTime)
                    {
                        item.DateFormat = value;
                        var now = DateTime.Now.ToString(value);
                        item.Text.Text = now;
                        _selectedText = now;
                        OnPropertyChanged(nameof(SelectedText));
                        UpdateSelectedInfo();
                    }
                }
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
            if (p == nameof(_selected))
            {
                OnPropertyChanged(nameof(IsAnySelected));
                OnPropertyChanged(nameof(IsTextSelected));
                OnPropertyChanged(nameof(IsSelectedTextReadOnly));
                OnPropertyChanged(nameof(IsImageSelected));
                OnPropertyChanged(nameof(IsDateTimeSelected));
                OnPropertyChanged(nameof(IsUsageSelected));
                OnPropertyChanged(nameof(IsUsageVisualSelected));
                OnPropertyChanged(nameof(IsGaugeSelected));
            }
        }
        private void OpenColorPicker(Color initial, Action<Color> apply)
        {
            var dlg = new ColorPickerWindow(initial);
            dlg.Owner = Application.Current?.MainWindow;
            if (dlg.ShowDialog() == true)
                apply(dlg.SelectedColor);
        }
        private string ResourcesFolder => Path.Combine(OutputFolder, "Resources");

        #region Properties

        public DeviceInfo? DeviceInfo
        {
            get => _deviceInfo;
            set
            {
                if (_deviceInfo != value)
                {
                    _deviceInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion
        // 读取用户偏好
        private UserPrefsModel LoadUserPrefs()
        {
            if (_userPrefsCache != null) return _userPrefsCache;
            try
            {
                if (File.Exists(UserPrefsFilePath))
                {
                    var json = File.ReadAllText(UserPrefsFilePath);
                    _userPrefsCache = System.Text.Json.JsonSerializer.Deserialize<UserPrefsModel>(json) ?? new UserPrefsModel();
                }
                else
                {
                    _userPrefsCache = new UserPrefsModel();
                }
            }
            catch
            {
                _userPrefsCache = new UserPrefsModel();
            }
            return _userPrefsCache;
        }
        // 检测 globalconfig1.json 是否“为空”
// 判定标准：不存在 -> 空；存在且反序列化后包含可识别的 items/entries/count == 0 -> 空
private bool IsGlobalPlayModeEmpty()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, GlobalConfigFileName);
                if (!File.Exists(path)) return true;

                var text = File.ReadAllText(path).Trim();
                if (string.IsNullOrWhiteSpace(text)) return true;

                using var doc = System.Text.Json.JsonDocument.Parse(text);
                var root = doc.RootElement;

                // 兼容几种可能结构：
                // 1) { "Items":[ ... ] }
                if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (root.TryGetProperty("Items", out var itemsProp) && itemsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        return itemsProp.GetArrayLength() == 0;

                    if (root.TryGetProperty("Sequences", out var seqProp) && seqProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        return seqProp.GetArrayLength() == 0;

                    // 没有已知数组字段，视为空 -> 让提示更积极
                    return true;
                }
                if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    return root.GetArrayLength() == 0;
                }
                // 其它类型直接认为空
                return true;
            }
            catch
            {
                // 解析失败，保守地认为空(给予提示)
                return true;
            }
        }

        // 保存后调用：如需提示，引导用户
        private void CheckAndPromptPlayMode()
        {
            var prefs = LoadUserPrefs();
            if (prefs.SuppressPlayModePrompt) return;

            if (!IsGlobalPlayModeEmpty()) return;

            try
            {
                var dlg = new Windows.PlayModePromptDialog
                {
                    Owner = Application.Current?.MainWindow
                };
                var result = dlg.ShowDialog();
                if (dlg.SuppressFuture)
                {
                    prefs.SuppressPlayModePrompt = true;
                    SaveUserPrefs();
                }

                if (result == true && dlg.GoToPlayMode)
                {
                    // 导航到播放模式页
                    // 如果当前 Page 在 NavigationService 里，直接导航
                    if (Application.Current?.MainWindow is MainWindow mw)
                    {
                        // 统一走主窗口封装，确保 NavList 选中同步
                        mw.NavigateToPlayModePageAndSelectNav();
                    }
                   
                }
            }
            catch (Exception ex)
            {
                Logger.Info("PlayMode prompt failed: " + ex.Message);
            }
        }
        private void SaveUserPrefs()
        {
            if (_userPrefsCache == null) return;
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(_userPrefsCache, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(UserPrefsFilePath, json);
            }
            catch { /* ignore */ }
        }
        // ===== 实时发送相关字段 =====
        private DispatcherTimer? _realtimeJpegTimer;
        private const int RealtimeIntervalMs = 50; // 约 20FPS
        private const int RealtimeJpegSize = 480;
        private bool _realtimeActive;
        private FrameworkElement? _captureRoot; // 缓存要截取的可视元素
        private bool _deviceErrorSubscribed = false;
        public DeviceConfigPage(DeviceInfo deviceInfo) : this()
        {
            DeviceInfo = deviceInfo;
        }
        public DeviceConfigPage()
        {
            InitializeComponent();
            DataContext = this;

            _selectedInfo = Application.Current.FindResource("None")?.ToString() ?? "None";
            _metrics = RealSystemMetricsService.Instance;
            BuildSystemInfoButtons();

            try
            {
                Directory.CreateDirectory(OutputFolder);
                Directory.CreateDirectory(ResourcesFolder);
            }
            catch { }

            DesignCanvas.PreviewMouseLeftButtonDown += DesignCanvas_PreviewMouseLeftButtonDown;
            DesignCanvas.PreviewMouseWheel += DesignCanvas_PreviewMouseWheel;

            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _liveTimer.Tick += LiveTimer_Tick;
            _liveTimer.Start();

            _autoMoveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _autoMoveTimer.Tick += AutoMoveTimer_Tick;

            Loaded += DeviceConfigPage_Loaded;          // 新增：进入页面恢复资源
            Unloaded += DeviceConfigPage_Unloaded;      // 退出时释放资源
        }
        // 新增：Loaded 回调，恢复 Unloaded 中释放的内容
        private async void DeviceConfigPage_Loaded(object? sender, RoutedEventArgs e)
        {
            // 1. 恢复 HID 服务（如果之前被释放或过滤移除）
            if (DeviceInfo != null)
            {
            }

            // 2. 重新启动实时使用的定时器
            if (!_liveTimer.IsEnabled) _liveTimer.Start();

            // 3. 自动移动（仅当有需要移动的元素）
            UpdateAutoMoveTimer();

            // 4. 恢复视频播放
            if (_currentVideoFrames != null && _currentVideoFrames.Count > 0)
            {
                ResumeVideoPlayback();
            }

            StartRealtimeJpegStreaming();

            // 6. 如果当前已经加载配置，重新着色或刷新一次文本（确保 UI 立即同步）
            foreach (var live in _liveItems)
            {
                if (live.DisplayStyle == UsageDisplayStyle.Gauge && live.GaugeNeedleRotate != null)
                {
                    // 强制一次 UI 值更新
                    live.GaugeNeedleRotate.Angle = GaugeRotationFromPercent(0);
                }
            }
            // 主动触发一次 Live 刷新文本
            LiveTimer_Tick(null, EventArgs.Empty);
        }

        private static string NormalizeUsageStyleString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Text";
            // 处理 "System.Windows.Controls.ComboBoxItem: Text"
            var idx = raw.LastIndexOf(':');
            if (idx >= 0 && idx < raw.Length - 1)
                raw = raw[(idx + 1)..];
            return raw.Trim();
        }

        // ================= System Info Buttons =================
        public sealed record SystemInfoItem(LiveInfoKind Kind, string DisplayName);

        private void BuildSystemInfoButtons()
        {
            SystemInfoItems.Clear();
            SystemInfoItems.Add(new SystemInfoItem(LiveInfoKind.CpuUsage, $"CPU Usage ({_metrics.CpuName})"));
            var gpuName = (_metrics as RealSystemMetricsService)?.PrimaryGpuName ?? "GPU";
            var gpuVal = _metrics.GetGpuUsagePercent();
            if (!string.Equals(gpuName, "GPU", StringComparison.OrdinalIgnoreCase) || gpuVal > 0)
                SystemInfoItems.Add(new SystemInfoItem(LiveInfoKind.GpuUsage, $"GPU Usage ({gpuName})"));
        }

        // ================= Navigation =================
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            var currentData = DateTime.Now.ToString("HH:mm:ss.fff");
            Debug.WriteLine($"Back button clicked at {currentData}, NavigationService.CanGoBack={NavigationService?.CanGoBack}");
            if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
            else NavigationService?.Navigate(new DevicePage());
        }

        private void PickUsageBarBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            if (!IsUsageSelected) return;
            OpenColorPicker(UsageBarBackgroundColor, c => UsageBarBackgroundColor = c);
        }

        // ================= Helpers =================
        private TextBlock? GetCurrentTextBlock()
        {
            if (_selected == null) return null;
            if (_selected.Child is TextBlock tb1) return tb1;
            if (_selected.Child is Panel panel) return panel.Children.OfType<TextBlock>().FirstOrDefault();
            return null;
        }

        // ================= Add Elements =================
        private void AddClock_Click(object sender, RoutedEventArgs e)
        {
            var defaultFormat = "yyyy-MM-dd HH:mm:ss";
            var textBlock = new TextBlock
            {
                Text = DateTime.Now.ToString(defaultFormat),
                FontSize = 20,
                Foreground = new SolidColorBrush(Colors.Black),
                FontWeight = FontWeights.SemiBold
            };
            textBlock.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
            var border = AddElement(textBlock, Application.Current.FindResource("DateTime")?.ToString() ?? "Date/Time");
            border.Tag = LiveInfoKind.DateTime;
            _liveItems.Add(new LiveTextItem
            {
                Border = border,
                Text = textBlock,
                Kind = LiveInfoKind.DateTime,
                DateFormat = defaultFormat
            });
            if (_selected == border)
            {
                OnPropertyChanged(nameof(IsSelectedTextReadOnly));
                _selectedText = textBlock.Text;
                _selectedDateFormat = defaultFormat;
                OnPropertyChanged(nameof(SelectedText));
                OnPropertyChanged(nameof(SelectedDateFormat));
            }
        }

        private void AddSystemInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not LiveInfoKind kind) return;

            var displayText = kind == LiveInfoKind.CpuUsage
                ? $"CPU {Math.Round(_metrics.GetCpuUsagePercent())}%"
                : $"GPU {Math.Round(_metrics.GetGpuUsagePercent())}%";

            var textBlock = new TextBlock
            {
                Text = displayText,
                FontSize = 18,
                Foreground = new SolidColorBrush(Colors.Black),
                Tag = kind,
                FontWeight = FontWeights.SemiBold
            };
            textBlock.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

            var cpuUsageText = Application.Current.FindResource("CpuUsage")?.ToString() ?? "CPU Usage";
            var gpuUsageText = Application.Current.FindResource("GpuUsage")?.ToString() ?? "GPU Usage";
            var border = AddElement(textBlock, kind == LiveInfoKind.CpuUsage ? cpuUsageText : gpuUsageText);
            border.Tag = kind;
            var item = new LiveTextItem { Border = border, Text = textBlock, Kind = kind };
            _liveItems.Add(item);

            if (_selected == border)
            {
                OnPropertyChanged(nameof(IsSelectedTextReadOnly));
                _selectedText = textBlock.Text;
            }
        }

        private void AddText_Click(object sender, RoutedEventArgs e)
        {
            var textBlock = new TextBlock
            {
                Text = Application.Current.FindResource("SampleText")?.ToString() ?? "Sample Text",
                FontSize = 24,
                Foreground = new SolidColorBrush(Colors.Black),
                FontWeight = FontWeights.SemiBold
            };
            textBlock.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
            var border = AddElement(textBlock, "Text");
            if (_selected == border) ResetMoveDirection();
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
                var copiedPath = CopyResourceToAppFolder(dlg.FileName, "Images");
                var img = new Image
                {
                    Source = new BitmapImage(new Uri(copiedPath)),
                    Stretch = Stretch.Uniform
                };
                AddElement(img, System.IO.Path.GetFileName(copiedPath));
            }
        }

        private void ClearCanvas_Click(object sender, RoutedEventArgs e)
        {
            DesignCanvas.Children.Clear();
            _liveItems.Clear();
            _movingDirections.Clear();
            SetSelected(null);
            ResetMoveDirection();
            CurrentConfigName = "";
            LoadedFromConfigFile = false;
        }

        // ================= Color Pickers =================
        private void PickBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            OpenColorPicker(BackgroundColor, c =>
            {
                BackgroundColor = c;
                BackgroundHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            });
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
                var copiedPath = CopyResourceToAppFolder(dlg.FileName, "Backgrounds");
                BackgroundImagePath = copiedPath;
            }
        }

        private void ClearBackgroundImage_Click(object sender, RoutedEventArgs e) => BackgroundImagePath = null;

        private void PickSelectedTextColor_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentTextBlock() == null) return;
            OpenColorPicker(SelectedTextColor, c => SelectedTextColor = c);
        }

        private void PickSelectedTextColor2_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentTextBlock() == null) return;
            OpenColorPicker(SelectedTextColor2, c => SelectedTextColor2 = c);
        }

        private void PickUsageStartColor_Click(object sender, RoutedEventArgs e)
        {
            if (!IsUsageVisualSelected) return;
            OpenColorPicker(UsageStartColor, c => UsageStartColor = c);
        }

        private void PickUsageEndColor_Click(object sender, RoutedEventArgs e)
        {
            if (!IsUsageVisualSelected) return;
            OpenColorPicker(UsageEndColor, c => UsageEndColor = c);
        }

        private void PickUsageNeedleColor_Click(object sender, RoutedEventArgs e)
        {
            if (!IsGaugeSelected) return;
            OpenColorPicker(UsageNeedleColor, c => UsageNeedleColor = c);
        }

        // ================= Z-Order / Delete =================
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

            _movingDirections.Remove(_selected);

            if (_selected == _currentVideoBorder)
            {
                StopVideoPlayback();
                _currentVideoFrames = null;
                _currentVideoImage = null;
                _currentVideoBorder = null;
            }

            var live = _liveItems.FirstOrDefault(i => i.Border == _selected);
            if (live != null) _liveItems.Remove(live);

            DesignCanvas.Children.Remove(_selected);
            SetSelected(null);
            UpdateAutoMoveTimer();
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
                OutputFolder = dlg.SelectedPath;
        }

        // ================= Element Creation / Selection =================
        private Border AddElement(FrameworkElement element, string label)
        {
            element.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            element.VerticalAlignment = System.Windows.VerticalAlignment.Top;

            var border = new Border
            {
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
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
                        ClampIntoCanvas(border);
                    }
                }

                UpdateSelectedInfo();
            };

            return border;
        }

        private void SelectElement(Border border)
        {
            if (_selected != null && _selected != border)
                _selected.BorderBrush = Brushes.Transparent;

            _selected = border;
            _selected.BorderBrush = Brushes.DodgerBlue;

            if (GetTransforms(border, out var scale, out var translate))
            {
                _selScale = scale;
                _selTranslate = translate;
            }

            SelectedOpacity = _selected.Opacity;
            SelectedScale = _selScale?.ScaleX ?? 1.0;

            var tb = GetCurrentTextBlock();
            if (tb != null)
            {
                SelectedText = tb.Text;
                SelectedFontSize = tb.FontSize;

                if (tb.Foreground is LinearGradientBrush lgb && lgb.GradientStops.Count >= 2)
                {
                    UseTextGradient = true;
                    SelectedTextColor = lgb.GradientStops[0].Color;
                    SelectedTextColor2 = lgb.GradientStops[1].Color;
                }
                else if (tb.Foreground is SolidColorBrush scb)
                {
                    UseTextGradient = false;
                    var c = scb.Color;
                    SelectedTextColor = Color.FromArgb(c.A, c.R, c.G, c.B);
                }
                else
                {
                    UseTextGradient = false;
                    SelectedTextColor = Colors.White;
                }
            }

            // Usage 项目主题回填
            if (IsUsageSelected)
            {
                var liveItem = _liveItems.FirstOrDefault(i => i.Border == _selected);
                if (liveItem != null)
                {
                    SelectedUsageDisplayStyle = liveItem.DisplayStyle.ToString();
                    UsageStartColor = liveItem.StartColor;
                    UsageEndColor = liveItem.EndColor;
                    UsageNeedleColor = liveItem.NeedleColor;
                    UsageBarBackgroundColor = liveItem.BarBackgroundColor;
                }
            }

            // 关键修复：DateTime 元素在重新选择时恢复其保存的格式与文本
            if (_selected?.Tag is LiveInfoKind liveKind && liveKind == LiveInfoKind.DateTime)
            {
                var dtItem = _liveItems.FirstOrDefault(i => i.Border == _selected && i.Kind == LiveInfoKind.DateTime);
                if (dtItem != null)
                {
                    // 回填格式
                    _selectedDateFormat = dtItem.DateFormat;
                    OnPropertyChanged(nameof(SelectedDateFormat));

                    // 立即按格式刷新显示文本（避免看起来“未导入”）
                    var nowStr = DateTime.Now.ToString(dtItem.DateFormat ?? "yyyy-MM-dd HH:mm:ss");
                    dtItem.Text.Text = nowStr;
                    _selectedText = nowStr;
                    OnPropertyChanged(nameof(SelectedText));
                }
            }
            else
            {
                // 若当前不是日期元素，保持原逻辑
            }

            // 恢复自动移动方向（若有）
            if (_movingDirections.TryGetValue(border, out var storedDir))
            {
                MoveDirX = storedDir.dirX;
                MoveDirY = storedDir.dirY;
                if (FindName("JoystickKnobTransform") is TranslateTransform tt2)
                {
                    tt2.X = MoveDirX * JoystickMaxOffset;
                    tt2.Y = MoveDirY * JoystickMaxOffset;
                }
            }
            else
            {
                MoveDirX = 0;
                MoveDirY = 0;
                if (FindName("JoystickKnobTransform") is TranslateTransform tt3)
                {
                    tt3.X = 0;
                    tt3.Y = 0;
                }
            }

            UpdateSelectedInfo();
            OnPropertyChanged(nameof(_selected));
            UpdateAutoMoveTimer();
        }

        private void SetSelected(Border? border)
        {
            if (_selected != null)
                _selected.BorderBrush = Brushes.Transparent;

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
                SelectedInfo = Application.Current.FindResource("None")?.ToString() ?? "None";
            }

            OnPropertyChanged(nameof(_selected));
            UpdateAutoMoveTimer();
        }

        private void UpdateSelectedInfo()
        {
            if (_selected == null)
            {
                SelectedInfo = Application.Current.FindResource("None")?.ToString() ?? "None";
                return;
            }

            if (_selected.Tag is VideoElementInfo videoInfo)
            {
                SelectedInfo = $"Video: Frame {_currentFrameIndex + 1}/{videoInfo.TotalFrames}";
            }
            else if (GetCurrentTextBlock() != null)
            {
                if (_selected.Tag is LiveInfoKind k)
                {
                    SelectedInfo = k switch
                    {
                        LiveInfoKind.CpuUsage => Application.Current.FindResource("CpuUsage")?.ToString() ?? "CPU Usage",
                        LiveInfoKind.GpuUsage => Application.Current.FindResource("GpuUsage")?.ToString() ?? "GPU Usage",
                        LiveInfoKind.DateTime => Application.Current.FindResource("DateTime")?.ToString() ?? "Date/Time",
                        _ => Application.Current.FindResource("LiveText")?.ToString() ?? "Live Text"
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

        // ================= Mouse Handling =================
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

            if (_selected.Child is Image)
            {
                _selTranslate.X = newX;
                _selTranslate.Y = newY;
            }
            else
            {
                (newX, newY) = GetClampedPosition(_selected, newX, newY);
                _selTranslate.X = newX;
                _selTranslate.Y = newY;
            }

            e.Handled = true;
        }

        private void Item_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b)
            {
                _isDragging = false;
                b.ReleaseMouseCapture();
                if (b.Child is not Image)
                    ClampIntoCanvas(b);
                e.Handled = true;
            }
        }

        private void DesignCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source == DesignCanvas) SetSelected(null);
        }

        private void DesignCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (_selected == null) return;

            var delta = e.Delta > 0 ? 0.05 : -0.05;
            SelectedScale = Math.Clamp(SelectedScale + delta, 0.1, 5.0);
            e.Handled = true;
        }

        // ================= Clamp Helpers =================
        private (double X, double Y) GetClampedPosition(Border border, double x, double y)
        {
            if (!GetTransforms(border, out var sc, out _)) return (x, y);

            double scaledW = border.ActualWidth * sc.ScaleX;
            double scaledH = border.ActualHeight * sc.ScaleY;

            // Default clamp (left edge >= 0, right edge <= CanvasSize)
            double extraRightAllowance = 0;

            // Allow ProgressBar usage item to move further right so“可见进度条主体”能贴紧画布右边
            // (ProgressBar 外容器宽度包含左右各 10 像素的外边距 barMargin；我们给出向右额外的 10 以便条本身贴边)
            if (border.Tag is LiveInfoKind &&
                _liveItems.FirstOrDefault(i => i.Border == border)?.DisplayStyle == UsageDisplayStyle.ProgressBar)
            {
                // 与 RebuildUsageVisual 中 progressbar 使用的 var barMargin = new Thickness(10,8,10,20) 对应
                const double progressBarHorizontalOuterRightMargin = 0;
                extraRightAllowance = progressBarHorizontalOuterRightMargin;
            }

            // 计算范围：
            // minX: 仍保持 0（不允许拖出左外）
            // maxX: 允许再多出 extraRightAllowance，这样内部条背景能贴紧右侧
            double minX = Math.Min(0, CanvasSize - scaledW);
            double maxX = Math.Max(0, CanvasSize - scaledW + extraRightAllowance);

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

        // ================= Color / Scale Helpers =================
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
            catch { }
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

        // ================= Video Playback (unchanged functional) =================
        private async void AddMp4_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select MP4 Video",
                Filter = "MP4 Files|*.mp4|All Files|*.*"
            };

            if (dlg.ShowDialog() != true)
                return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var copiedPath = CopyResourceToAppFolder(dlg.FileName, "Videos");

                var videoInfo = await VideoConverter.GetMp4InfoAsync(copiedPath);
                if (videoInfo == null)
                {
                    MessageBox.Show("Failed to read video information", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var frames = await ExtractMp4FramesToMemory(copiedPath);
                if (frames == null || frames.Count == 0)
                {
                    MessageBox.Show("Failed to extract video frames", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _currentVideoFrames = frames;
                _currentFrameIndex = 0;

                var image = new Image
                {
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top
                };
                UpdateVideoFrame(image, _currentVideoFrames[0]);

                var border = AddElement(image, $"MP4: {Path.GetFileName(copiedPath)}");
                _currentVideoImage = image;
                _currentVideoBorder = border;

                border.Tag = new VideoElementInfo
                {
                    Kind = LiveInfoKind.VideoPlayback,
                    VideoInfo = videoInfo,
                    FilePath = copiedPath,
                    TotalFrames = frames.Count
                };

                StartVideoPlayback(videoInfo.FrameRate);
                if (_selected == border)
                {
                    OnPropertyChanged(nameof(IsSelectedTextReadOnly));
                    UpdateSelectedInfo();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load MP4: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private sealed class VideoElementInfo
        {
            public LiveInfoKind Kind { get; init; }
            public VideoInfo? VideoInfo { get; init; }
            public string FilePath { get; init; } = string.Empty;
            public int TotalFrames { get; init; }
        }

        private async Task<List<VideoFrameData>> ExtractMp4FramesToMemory(string mp4Path)
        {
            var frames = new List<VideoFrameData>();
            try
            {
                await foreach (var frame in VideoConverter.ExtractMp4FramesToJpegRealTimeAsync(
                    mp4Path,
                    quality: 85,
                    CancellationToken.None))
                {
                    frames.Add(frame);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to extract MP4 frames: {ex.Message}");
                return new List<VideoFrameData>();
            }
            return frames;
        }

        private void UpdateVideoFrame(Image image, VideoFrameData frameData)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                using (var stream = new MemoryStream(frameData.JpegData))
                {
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                bitmap.Freeze();
                image.Source = bitmap;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to update video frame: {ex.Message}");
            }
        }

        private void StartVideoPlayback(double frameRate)
        {
            StopVideoPlayback();
            int intervalMs = (int)(1000.0 / frameRate);
            _mp4Timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
            _mp4Timer.Tick += Mp4Timer_Tick;
            _mp4Timer.Start();
        }

        private void StopVideoPlayback()
        {
            if (_mp4Timer != null)
            {
                _mp4Timer.Stop();
                _mp4Timer.Tick -= Mp4Timer_Tick;
                _mp4Timer = null;
            }
        }

        private void Mp4Timer_Tick(object? sender, EventArgs e)
        {
            if (_currentVideoFrames == null || _currentVideoImage == null)
            {
                StopVideoPlayback();
                return;
            }
            _currentFrameIndex++;
            if (_currentFrameIndex >= _currentVideoFrames.Count)
                _currentFrameIndex = 0;

            UpdateVideoFrame(_currentVideoImage, _currentVideoFrames[_currentFrameIndex]);
            if (_selected == _currentVideoBorder) UpdateSelectedInfo();
        }

        public void PauseVideoPlayback() => _mp4Timer?.Stop();
        public void ResumeVideoPlayback() => _mp4Timer?.Start();
        public void SeekVideoFrame(int frameIndex)
        {
            if (_currentVideoFrames != null && frameIndex >= 0 && frameIndex < _currentVideoFrames.Count)
            {
                _currentFrameIndex = frameIndex;
                if (_currentVideoImage != null)
                    UpdateVideoFrame(_currentVideoImage, _currentVideoFrames[_currentFrameIndex]);
            }
        }

        // ================= Resource Copy Helpers =================
        private string CopyResourceToAppFolder(string sourcePath, string resourceType)
        {
            try
            {
                var fileName = Path.GetFileName(sourcePath);
                var extension = Path.GetExtension(fileName);
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

                var typeFolder = Path.Combine(ResourcesFolder, resourceType);
                Directory.CreateDirectory(typeFolder);

                var destPath = Path.Combine(typeFolder, fileName);
                int counter = 1;
                while (File.Exists(destPath))
                {
                    var newName = $"{nameWithoutExtension}_{counter}{extension}";
                    destPath = Path.Combine(typeFolder, newName);
                    counter++;
                }

                File.Copy(sourcePath, destPath, true);
                Logger.Info($"Copied {resourceType} resource to: {destPath}");
                return destPath;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to copy {resourceType} resource: {ex.Message}");
                return sourcePath;
            }
        }

        private string GetRelativePath(string fullPath)
        {
            try
            {
                if (fullPath.StartsWith(ResourcesFolder, StringComparison.OrdinalIgnoreCase))
                    return Path.GetRelativePath(OutputFolder, fullPath);

                if (fullPath.StartsWith(OutputFolder, StringComparison.OrdinalIgnoreCase))
                    return Path.GetRelativePath(OutputFolder, fullPath);

                return Path.GetFileName(fullPath);
            }
            catch
            {
                return Path.GetFileName(fullPath);
            }
        }

        private string ResolveRelativePath(string relativePath)
        {
            try
            {
                if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
                    return relativePath;

                var fullPath = Path.Combine(OutputFolder, relativePath);
                if (File.Exists(fullPath)) return fullPath;

                fullPath = Path.Combine(ResourcesFolder, relativePath);
                if (File.Exists(fullPath)) return fullPath;

                foreach (var subFolder in new[] { "Images", "Videos", "Backgrounds" })
                {
                    fullPath = Path.Combine(ResourcesFolder, subFolder, relativePath);
                    if (File.Exists(fullPath)) return fullPath;
                }

                return relativePath;
            }
            catch
            {
                return relativePath;
            }
        }

        // ================= Save Config =================
        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(CurrentConfigName))
                {
                    var dialog = new ConfigNameDialog();
                    if (Application.Current.MainWindow != null)
                        dialog.Owner = Application.Current.MainWindow;
                    if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ConfigName))
                        return;
                    CurrentConfigName = dialog.ConfigName.Trim();
                }

                // 传 true: 允许删除之前 load 的旧文件
                SaveConfigCore(CurrentConfigName, deletePreviousLoaded: true);
                LoadedFromConfigFile = true;

                var msg = Application.Current.FindResource("ConfigSaved")?.ToString() ?? "Configuration saved";
                var title = Application.Current.FindResource("SaveSuccessful")?.ToString() ?? "Save Successful";
              
                MessageBox.Show($"{msg}: {CurrentConfigName}", title, MessageBoxButton.OK, MessageBoxImage.Information);
                CheckAndPromptPlayMode();
            }
            catch (Exception ex)
            {
                var errorMsg = Application.Current.FindResource("Error")?.ToString() ?? "Error";
                MessageBox.Show($"保存配置失败: {ex.Message}", errorMsg, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveAsConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var baseName = string.IsNullOrWhiteSpace(CurrentConfigName) ? "NewConfig" : CurrentConfigName;
                var dialog = new ConfigNameDialog($"{baseName}_Copy");
                if (Application.Current.MainWindow != null)
                    dialog.Owner = Application.Current.MainWindow;

                if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ConfigName))
                    return;

                var newName = dialog.ConfigName.Trim();
                CurrentConfigName = newName;

                // 传 false: 另存为不删除旧文件
                SaveConfigCore(newName, deletePreviousLoaded: false);
                LoadedFromConfigFile = true;

                var msg = Application.Current.FindResource("ConfigSaved")?.ToString() ?? "Configuration saved";
                var title = Application.Current.FindResource("SaveSuccessful")?.ToString() ?? "Save Successful";
                
                MessageBox.Show($"{msg}: {newName}", title, MessageBoxButton.OK, MessageBoxImage.Information);
                CheckAndPromptPlayMode();
            }
            catch (Exception ex)
            {
                var errorMsg = Application.Current.FindResource("Error")?.ToString() ?? "Error";
                MessageBox.Show($"另存为失败: {ex.Message}", errorMsg, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string SaveConfigCore(string configName, bool deletePreviousLoaded)
        {
            // 删除之前 load 的旧配置文件（仅当指定删除且文件仍存在且不是我们即将写入的名字）
            if (deletePreviousLoaded && _loadedConfigFilePath != null)
            {
                try
                {
                    if (File.Exists(_loadedConfigFilePath))
                        File.Delete(_loadedConfigFilePath);
                }
                catch (Exception ex)
                {
                    Logger.Info($"删除旧配置文件失败: {_loadedConfigFilePath} - {ex.Message}");
                }
            }

            var config = new CanvasConfiguration
            {
                ConfigName = configName,
                CanvasSize = CanvasSize,
                BackgroundColor = BackgroundHex,
                BackgroundImagePath = BackgroundImagePath != null ? GetRelativePath(BackgroundImagePath) : null,
                BackgroundImageOpacity = BackgroundImageOpacity,
                MoveSpeed = MoveSpeed
            };

            foreach (UIElement child in DesignCanvas.Children)
            {
                if (child is Border border && GetTransforms(border, out var scale, out var translate))
                {
                    var elemConfig = new ElementConfiguration
                    {
                        X = translate.X,
                        Y = translate.Y,
                        Scale = scale.ScaleX,
                        Opacity = border.Opacity,
                        ZIndex = Canvas.GetZIndex(border)
                    };

                    if (_movingDirections.TryGetValue(border, out var dir) &&
                        (Math.Abs(dir.dirX) > 0.0001 || Math.Abs(dir.dirY) > 0.0001))
                    {
                        elemConfig.MoveDirX = dir.dirX;
                        elemConfig.MoveDirY = dir.dirY;
                    }

                    if (border.Child is TextBlock tb && border.Tag is not LiveInfoKind)
                    {
                        elemConfig.Type = "Text";
                        elemConfig.Text = tb.Text;
                        elemConfig.FontSize = tb.FontSize;
                        if (tb.Foreground is LinearGradientBrush lgb && lgb.GradientStops.Count >= 2)
                        {
                            elemConfig.UseTextGradient = true;
                            var c1 = lgb.GradientStops[0].Color;
                            var c2 = lgb.GradientStops[1].Color;
                            elemConfig.TextColor = $"#{c1.R:X2}{c1.G:X2}{c1.B:X2}";
                            elemConfig.TextColor2 = $"#{c2.R:X2}{c2.G:X2}{c2.B:X2}";
                        }
                        else if (tb.Foreground is SolidColorBrush scb)
                        {
                            elemConfig.UseTextGradient = false;
                            var c = scb.Color;
                            elemConfig.TextColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                        }
                    }
                    else if (border.Tag is LiveInfoKind lk && border.Child != null)
                    {
                        elemConfig.Type = "LiveText";
                        elemConfig.LiveKind = lk;
                        var liveItem = _liveItems.FirstOrDefault(i => i.Border == border);
                        if (liveItem != null)
                        {
                            elemConfig.FontSize = liveItem.Text.FontSize;
                            elemConfig.UsageDisplayStyle = liveItem.DisplayStyle.ToString();
                            elemConfig.UsageStartColor = $"#{liveItem.StartColor.R:X2}{liveItem.StartColor.G:X2}{liveItem.StartColor.B:X2}";
                            elemConfig.UsageEndColor = $"#{liveItem.EndColor.R:X2}{liveItem.EndColor.G:X2}{liveItem.EndColor.B:X2}";
                            elemConfig.UsageNeedleColor = $"#{liveItem.NeedleColor.R:X2}{liveItem.NeedleColor.G:X2}{liveItem.NeedleColor.B:X2}";
                            elemConfig.UsageBarBackgroundColor = $"#{liveItem.BarBackgroundColor.R:X2}{liveItem.BarBackgroundColor.G:X2}{liveItem.BarBackgroundColor.B:X2}";
                            if (lk == LiveInfoKind.DateTime)
                                elemConfig.DateFormat = liveItem.DateFormat;

                            if (liveItem.DisplayStyle == UsageDisplayStyle.Text)
                            {
                                var fore = liveItem.Text.Foreground;
                                if (fore is LinearGradientBrush glb && glb.GradientStops.Count >= 2)
                                {
                                    elemConfig.UseTextGradient = true;
                                    var c1 = glb.GradientStops[0].Color;
                                    var c2 = glb.GradientStops[1].Color;
                                    elemConfig.TextColor = $"#{c1.R:X2}{c1.G:X2}{c1.B:X2}";
                                    elemConfig.TextColor2 = $"#{c2.R:X2}{c2.G:X2}{c2.B:X2}";
                                }
                                else if (fore is SolidColorBrush sbrush)
                                {
                                    elemConfig.UseTextGradient = false;
                                    var c = sbrush.Color;
                                    elemConfig.TextColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                                }
                            }
                        }
                    }
                    else if (border.Child is Image img2)
                    {
                        if (border.Tag is VideoElementInfo videoInfo)
                        {
                            elemConfig.Type = "Video";
                            elemConfig.VideoPath = GetRelativePath(videoInfo.FilePath);
                        }
                        else if (img2.Source is BitmapImage bitmapImg)
                        {
                            var imagePath = bitmapImg.UriSource?.LocalPath ?? bitmapImg.UriSource?.ToString();
                            if (!string.IsNullOrEmpty(imagePath))
                            {
                                elemConfig.Type = "Image";
                                elemConfig.ImagePath = GetRelativePath(imagePath);
                            }
                            if (border.RenderTransform is TransformGroup tgg)
                            {
                                var rotate = tgg.Children.OfType<RotateTransform>().FirstOrDefault();
                                if (rotate != null) elemConfig.Rotation = rotate.Angle;
                                var scales = tgg.Children.OfType<ScaleTransform>().ToList();
                                if (scales.Count > 1 && scales[1].ScaleX < 0)
                                    elemConfig.MirroredX = true;
                            }
                        }
                    }

                    config.Elements.Add(elemConfig);
                }
            }

            var configFolder = Path.Combine(OutputFolder, "Configs");
            Directory.CreateDirectory(configFolder);
            var safeName = MakeSafeFileBase(configName);
            var fileName = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var filePath = Path.Combine(configFolder, fileName);

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });
            File.WriteAllText(filePath, json);

            _loadedConfigFilePath = filePath; // 记录当前最新文件
            return filePath;
        }

        private static string MakeSafeFileBase(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "config";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            // optional: collapse spaces
            name = Regex.Replace(name, @"\s+", "_");
            return name;
        }

        // ================= Load Config =================
        private async void LoadConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var configFolder = Path.Combine(OutputFolder, "Configs");
                if (!Directory.Exists(configFolder))
                    Directory.CreateDirectory(configFolder);

                var configFiles = Directory.GetFiles(configFolder, "*.json");
                if (configFiles.Length == 0)
                {
                    MessageBox.Show("No configuration files found", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var configs = new List<(string path, CanvasConfiguration config)>();
                foreach (var file in configFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var cfg = JsonSerializer.Deserialize<CanvasConfiguration>(json, new JsonSerializerOptions
                        {
                            Converters = { new JsonStringEnumConverter() }
                        });
                        if (cfg != null)
                            configs.Add((file, cfg));
                    }
                    catch (Exception ex2)
                    {
                        Logger.Info($"Failed to load config file {file}: {ex2.Message}");
                    }
                }

                if (configs.Count == 0)
                {
                    MessageBox.Show("No valid configuration files found", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var selectionDialog = new ConfigSelectionDialog(configs);
                if (Application.Current.MainWindow != null)
                    selectionDialog.Owner = Application.Current.MainWindow;

                if (selectionDialog.ShowDialog() != true || selectionDialog.SelectedConfig == null)
                    return;

                var config = selectionDialog.SelectedConfig;

                DesignCanvas.Children.Clear();
                _liveItems.Clear();
                _movingDirections.Clear();
                SetSelected(null);
                StopVideoPlayback();
                ResetMoveDirection();

                CurrentConfigName = config.ConfigName;
                CanvasSize = config.CanvasSize;
                BackgroundHex = config.BackgroundColor;
                BackgroundImagePath = !string.IsNullOrEmpty(config.BackgroundImagePath)
                    ? ResolveRelativePath(config.BackgroundImagePath)
                    : null;
                BackgroundImageOpacity = config.BackgroundImageOpacity;

                if (config.MoveSpeed > 0) MoveSpeed = config.MoveSpeed; // NEW: restore global move speed

                foreach (var elemConfig in config.Elements.OrderBy(ei => ei.ZIndex))
                {
                    FrameworkElement? element = null;
                    Border? border = null;

                    switch (elemConfig.Type)
                    {
                        case "Text":
                            {
                                var tb = new TextBlock
                                {
                                    Text = elemConfig.Text ?? "Text",
                                    FontSize = elemConfig.FontSize ?? 24,
                                    FontWeight = FontWeights.SemiBold
                                };
                                tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

                                if (elemConfig.UseTextGradient == true &&
                                    !string.IsNullOrEmpty(elemConfig.TextColor) &&
                                    !string.IsNullOrEmpty(elemConfig.TextColor2) &&
                                    TryParseHexColor(elemConfig.TextColor, out var c1) &&
                                    TryParseHexColor(elemConfig.TextColor2, out var c2))
                                {
                                    var gradientBrush = new LinearGradientBrush
                                    {
                                        StartPoint = new Point(0, 0),
                                        EndPoint = new Point(1, 1)
                                    };
                                    gradientBrush.GradientStops.Add(new GradientStop(c1, 0.0));
                                    gradientBrush.GradientStops.Add(new GradientStop(c2, 1.0));
                                    tb.Foreground = gradientBrush;
                                }
                                else if (!string.IsNullOrEmpty(elemConfig.TextColor) &&
                                         TryParseHexColor(elemConfig.TextColor, out var tc))
                                {
                                    tb.Foreground = new SolidColorBrush(tc);
                                }
                                else
                                {
                                    tb.Foreground = new SolidColorBrush(Colors.Black);
                                }
                                element = tb;
                                break;
                            }
                        case "LiveText":
                            {
                                if (elemConfig.LiveKind.HasValue)
                                {
                                    var liveTb = new TextBlock
                                    {
                                        FontSize = elemConfig.FontSize ?? 18,
                                        FontWeight = FontWeights.SemiBold
                                    };
                                    liveTb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
                                    liveTb.Foreground = new SolidColorBrush(Colors.Black);

                                    if (elemConfig.LiveKind.Value == LiveInfoKind.DateTime)
                                    {
                                        string fmt = string.IsNullOrWhiteSpace(elemConfig.DateFormat)
                                            ? "yyyy-MM-dd HH:mm:ss"
                                            : elemConfig.DateFormat;
                                        liveTb.Text = DateTime.Now.ToString(fmt);
                                    }
                                    else
                                    {
                                        switch (elemConfig.LiveKind.Value)
                                        {
                                            case LiveInfoKind.CpuUsage:
                                                liveTb.Text = $"CPU {Math.Round(_metrics.GetCpuUsagePercent())}%";
                                                break;
                                            case LiveInfoKind.GpuUsage:
                                                liveTb.Text = $"GPU {Math.Round(_metrics.GetGpuUsagePercent())}%";
                                                break;
                                        }
                                    }

                                    // NEW: restore text color / gradient when style is plain text
                                    if (elemConfig.UseTextGradient == true &&
                                        !string.IsNullOrEmpty(elemConfig.TextColor) &&
                                        !string.IsNullOrEmpty(elemConfig.TextColor2) &&
                                        TryParseHexColor(elemConfig.TextColor, out var dtc1) &&
                                        TryParseHexColor(elemConfig.TextColor2, out var dtc2))
                                    {
                                        var g = new LinearGradientBrush
                                        {
                                            StartPoint = new Point(0, 0),
                                            EndPoint = new Point(1, 1)
                                        };
                                        g.GradientStops.Add(new GradientStop(dtc1, 0));
                                        g.GradientStops.Add(new GradientStop(dtc2, 1));
                                        liveTb.Foreground = g;
                                    }
                                    else if (!string.IsNullOrEmpty(elemConfig.TextColor) &&
                                             TryParseHexColor(elemConfig.TextColor, out var dtSolid))
                                    {
                                        liveTb.Foreground = new SolidColorBrush(dtSolid);
                                    }

                                    element = liveTb;
                                }
                                break;
                            }
                        case "Image":
                            {
                                if (!string.IsNullOrEmpty(elemConfig.ImagePath))
                                {
                                    var resolvedPath = ResolveRelativePath(elemConfig.ImagePath);
                                    if (File.Exists(resolvedPath))
                                    {
                                        try
                                        {
                                            var bitmap = new BitmapImage();
                                            bitmap.BeginInit();
                                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                            bitmap.UriSource = new Uri(resolvedPath, UriKind.Absolute);
                                            bitmap.EndInit();
                                            bitmap.Freeze();

                                            var img = new Image
                                            {
                                                Source = bitmap,
                                                Stretch = Stretch.Uniform
                                            };
                                            element = img;
                                        }
                                        catch (Exception ex)
                                        {
                                            Logger.Error($"Failed to load image from {resolvedPath}: {ex.Message}");
                                        }
                                    }
                                }
                                break;
                            }
                        case "Video":
                            {
                                if (!string.IsNullOrEmpty(elemConfig.VideoPath))
                                {
                                    var resolvedPath = ResolveRelativePath(elemConfig.VideoPath);
                                    if (File.Exists(resolvedPath))
                                    {
                                        var videoInfo = await VideoConverter.GetMp4InfoAsync(resolvedPath);
                                        if (videoInfo != null)
                                        {
                                            Mouse.OverrideCursor = Cursors.Wait;
                                            var frames = await ExtractMp4FramesToMemory(resolvedPath);
                                            Mouse.OverrideCursor = null;

                                            if (frames != null && frames.Count > 0)
                                            {
                                                _currentVideoFrames = frames;
                                                _currentFrameIndex = 0;

                                                var videoImage = new Image
                                                {
                                                    Stretch = Stretch.Uniform,
                                                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                                                    VerticalAlignment = System.Windows.VerticalAlignment.Top
                                                };
                                                UpdateVideoFrame(videoImage, frames[0]);
                                                element = videoImage;
                                            }
                                        }
                                    }
                                }
                                break;
                            }
                    }

                    if (element != null)
                    {
                        element.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                        element.VerticalAlignment = VerticalAlignment.Top;

                        border = new Border
                        {
                            BorderBrush = Brushes.Transparent,
                            BorderThickness = new Thickness(0),
                            Child = element,
                            RenderTransformOrigin = new Point(0.5, 0.5),
                            Cursor = Cursors.SizeAll,
                            Opacity = elemConfig.Opacity
                        };

                        var tg = new TransformGroup();
                        var scale = new ScaleTransform(elemConfig.Scale, elemConfig.Scale);
                        var translate = new TranslateTransform(elemConfig.X, elemConfig.Y);
                        tg.Children.Add(scale);
                        tg.Children.Add(translate);
                        border.RenderTransform = tg;

                        // NEW: restore mirror & rotation for images
                        if (elemConfig.Type == "Image" && (elemConfig.Rotation.HasValue || elemConfig.MirroredX == true))
                        {
                            if (border.RenderTransform is TransformGroup tgg)
                            {
                                var contentScale = tgg.Children.OfType<ScaleTransform>().First();
                                var translateT = tgg.Children.OfType<TranslateTransform>().First();

                                if (elemConfig.MirroredX == true)
                                {
                                    var mirrorScale = new ScaleTransform(-1, 1);
                                    int insertIndex = tg.Children.IndexOf(contentScale) + 1;
                                    tg.Children.Insert(insertIndex, mirrorScale);
                                }

                                if (elemConfig.Rotation.HasValue)
                                {
                                    var rotate = new RotateTransform(elemConfig.Rotation.Value);
                                    int translateIndex = tg.Children.IndexOf(translateT);
                                    tg.Children.Insert(translateIndex, rotate);
                                }
                            }
                        }

                        border.PreviewMouseLeftButtonDown += Item_PreviewMouseLeftButtonDown;
                        border.PreviewMouseLeftButtonUp += Item_PreviewMouseLeftButtonUp;
                        border.PreviewMouseMove += Item_PreviewMouseMove;
                        border.MouseDown += (_, __) => SelectElement(border);
                        border.MouseEnter += (_, __) => { if (_selected != border) border.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 30, 144, 255)); };
                        border.MouseLeave += (_, __) => { if (_selected != border) border.BorderBrush = Brushes.Transparent; };

                        DesignCanvas.Children.Add(border);
                        Canvas.SetZIndex(border, elemConfig.ZIndex);

                        if (elemConfig.Type == "LiveText" && elemConfig.LiveKind.HasValue)
                        {
                            border.Tag = elemConfig.LiveKind.Value;
                            var newItem = new LiveTextItem
                            {
                                Border = border,
                                Text = (TextBlock)element,
                                Kind = elemConfig.LiveKind.Value,
                                DateFormat = (elemConfig.LiveKind.Value == LiveInfoKind.DateTime)
                                    ? (elemConfig.DateFormat ?? "yyyy-MM-dd HH:mm:ss")
                                    : "yyyy-MM-dd HH:mm:ss"
                            };

                            if (elemConfig.LiveKind.Value == LiveInfoKind.CpuUsage ||
                                elemConfig.LiveKind.Value == LiveInfoKind.GpuUsage)
                            {
                                if (Enum.TryParse<UsageDisplayStyle>(elemConfig.UsageDisplayStyle ?? "Text", out var ds)) newItem.DisplayStyle = ds;
                                if (TryParseHexColor(elemConfig.UsageStartColor, out var usc)) newItem.StartColor = usc;
                                if (TryParseHexColor(elemConfig.UsageEndColor, out var uec)) newItem.EndColor = uec;
                                if (TryParseHexColor(elemConfig.UsageNeedleColor, out var unc)) newItem.NeedleColor = unc;
                                if (TryParseHexColor(elemConfig.UsageBarBackgroundColor, out var ubc)) newItem.BarBackgroundColor = ubc;
                            }

                            _liveItems.Add(newItem);
                            if (newItem.DisplayStyle != UsageDisplayStyle.Text)
                            {
                                RebuildUsageVisual(newItem);
                                // Re-apply restored theme
                                if (newItem.DisplayStyle == UsageDisplayStyle.ProgressBar)
                                {
                                    if (newItem.BarBackgroundBorder != null)
                                        newItem.BarBackgroundBorder.Background = new SolidColorBrush(newItem.BarBackgroundColor);
                                    if (newItem.BarFill != null)
                                        newItem.BarFill.Fill = new LinearGradientBrush(newItem.StartColor, newItem.EndColor, 0);
                                }
                                else if (newItem.DisplayStyle == UsageDisplayStyle.Gauge)
                                {
                                    if (newItem.GaugeNeedle != null)
                                        newItem.GaugeNeedle.Stroke = new SolidColorBrush(newItem.NeedleColor);
                                    RecolorGaugeTicks(newItem);
                                }
                            }
                        }

                        if (elemConfig.Type == "Video" && element is Image videoImg)
                        {
                            _currentVideoImage = videoImg;
                            _currentVideoBorder = border;

                            var resolvedVideoPath = ResolveRelativePath(elemConfig.VideoPath!);
                            var videoInfo = await VideoConverter.GetMp4InfoAsync(resolvedVideoPath);
                            if (videoInfo != null)
                            {
                                border.Tag = new VideoElementInfo
                                {
                                    Kind = LiveInfoKind.VideoPlayback,
                                    VideoInfo = videoInfo,
                                    FilePath = resolvedVideoPath,
                                    TotalFrames = _currentVideoFrames?.Count ?? 0
                                };
                                StartVideoPlayback(videoInfo.FrameRate);
                            }
                        }

                        // NEW: restore per-element auto-move direction (only plain text)
                        if ((elemConfig.Type == "Text" || elemConfig.Type == "LiveText") &&
     elemConfig.MoveDirX.HasValue && elemConfig.MoveDirY.HasValue &&
     (Math.Abs(elemConfig.MoveDirX.Value) > 0.0001 || Math.Abs(elemConfig.MoveDirY.Value) > 0.0001))
                        {
                            _movingDirections[border] = (elemConfig.MoveDirX.Value, elemConfig.MoveDirY.Value);
                        }
                    }
                }

                UpdateAutoMoveTimer(); // ensure timer runs if any moving text restored
                LoadedFromConfigFile = true;
                _loadedConfigFilePath = selectionDialog.SelectedConfigPath;
                MessageBox.Show($"Configuration loaded: {config.ConfigName}", "Load Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================= Apply Text Color / Gradient =================
        private void ApplyTextColorOrGradient()
        {
            var tb = GetCurrentTextBlock();
            if (tb == null || IsUsageSelected) return;

            if (UseTextGradient)
            {
                var brush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1)
                };
                brush.GradientStops.Add(new GradientStop(SelectedTextColor, 0));
                brush.GradientStops.Add(new GradientStop(SelectedTextColor2, 1));
                tb.Foreground = brush;
            }
            else
            {
                tb.Foreground = new SolidColorBrush(SelectedTextColor);
            }
        }

        // ================= Live Timer =================
        private void LiveTimer_Tick(object? sender, EventArgs e)
        {
            double cpu = _metrics.GetCpuUsagePercent();
            double gpu = _metrics.GetGpuUsagePercent();
            
            DateTime now = DateTime.Now;

            foreach (var item in _liveItems.ToArray())
            {
                double rawVal = 0;
                string text = item.Kind switch
                {
                    LiveInfoKind.CpuUsage => (rawVal = cpu, $"CPU {Math.Round(cpu)}%").Item2,
                    LiveInfoKind.GpuUsage => (rawVal = gpu, $"GPU {Math.Round(gpu)}%").Item2,
                    LiveInfoKind.DateTime => (rawVal = 0, now.ToString(item.DateFormat ?? "yyyy-MM-dd HH:mm:ss")).Item2,
                    _ => item.Text.Text
                };

                switch (item.DisplayStyle)
                {
                    case UsageDisplayStyle.Text:
                        item.Text.Text = text;
                        break;

                    case UsageDisplayStyle.ProgressBar:
                        {
                            if (item.BarFill != null)
                            {
                                double percent = Math.Clamp(rawVal, 0, 100);
                                double totalWidth = 140;
                                item.BarFill.Width = totalWidth * (percent / 100.0);

                                // 显示类别 + 百分比，例如 "CPU 40%"
                                string prefix = item.Kind == LiveInfoKind.CpuUsage ? "CPU" :
                                                item.Kind == LiveInfoKind.GpuUsage ? "GPU" : "";
                                item.Text.Text = string.IsNullOrEmpty(prefix)
                                    ? $"{Math.Round(percent)}%"
                                    : $"{prefix} {Math.Round(percent)}%";

                                // 文本颜色跟随进度（按 StartColor → EndColor 线性插值）
                                double t = percent / 100.0;
                                var col = LerpColor(item.StartColor, item.EndColor, t);
                                item.Text.Foreground = new SolidColorBrush(col);
                            }
                            break;
                        }

                    case UsageDisplayStyle.Gauge:
                        {
                            if (item.GaugeNeedleRotate != null)
                            {
                                double targetAngle = GaugeRotationFromPercent(rawVal);
                                double current = item.GaugeNeedleRotate.Angle;
                                if (Math.Abs(current - targetAngle) > 0.05)
                                {
                                    var anim = new DoubleAnimation
                                    {
                                        From = current,
                                        To = targetAngle,
                                        Duration = TimeSpan.FromMilliseconds(300),
                                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                                    };
                                    item.GaugeNeedleRotate.BeginAnimation(
                                        RotateTransform.AngleProperty,
                                        anim,
                                        HandoffBehavior.SnapshotAndReplace);
                                }
                            }
                            // Gauge 仍只显示百分比，不加 CPU/GPU 前缀（按需求只改 ProgressBar）
                            item.Text.Text = $"{Math.Round(rawVal)}%";
                            break;
                        }
                }

                if (_selected == item.Border)
                {
                    _selectedText = item.Text.Text;
                    OnPropertyChanged(nameof(SelectedText));
                    UpdateSelectedInfo();
                }
            }
        }

        // ================= Usage Style Handling =================
        private void ApplySelectedUsageStyle()
        {
            if (!IsUsageSelected || _selected == null) return;
            var liveItem = _liveItems.FirstOrDefault(i => i.Border == _selected);
            if (liveItem == null) return;

            var styleString = NormalizeUsageStyleString(SelectedUsageDisplayStyle);
            if (!Enum.TryParse<UsageDisplayStyle>(styleString, true, out var style))
                style = UsageDisplayStyle.Text;

            if (liveItem.DisplayStyle == style)
            {
                // 仅主题改变
                ApplyUsageTheme();
                return;
            }

            liveItem.DisplayStyle = style;
            RebuildUsageVisual(liveItem);
            ApplyUsageTheme();
        }

        private void ApplyUsageTheme()
        {
            if (!IsUsageSelected || _selected == null) return;
            var item = _liveItems.FirstOrDefault(i => i.Border == _selected);
            if (item == null) return;

            item.StartColor = UsageStartColor;
            item.EndColor = UsageEndColor;
            item.NeedleColor = UsageNeedleColor;
            item.BarBackgroundColor = UsageBarBackgroundColor;

            if (item.DisplayStyle == UsageDisplayStyle.ProgressBar)
            {
                if (item.BarFill != null)
                    item.BarFill.Fill = new LinearGradientBrush(item.StartColor, item.EndColor, 0);

                if (item.BarBackgroundBorder != null)
                    item.BarBackgroundBorder.Background = new SolidColorBrush(item.BarBackgroundColor);
            }
            else if (item.DisplayStyle == UsageDisplayStyle.Gauge && item.GaugeNeedle != null)
            {
                item.GaugeNeedle.Stroke = new SolidColorBrush(item.NeedleColor);
                RecolorGaugeTicks(item);
            }
        }

        private void RebuildUsageVisual(LiveTextItem item)
        {
            var border = item.Border;

            border.Child = null;

            TextBlock newText = new TextBlock
            {
                Text = item.Text.Text,
                FontSize = item.Text.FontSize,
                FontWeight = item.Text.FontWeight,
                Foreground = item.Text.Foreground,
                FontFamily = item.Text.FontFamily,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            item.Text = newText;
            item.BarFill = null;
            item.GaugeNeedle = null;
            item.GaugeNeedleRotate = null;

            FrameworkElement root;

            switch (item.DisplayStyle)
            {
                case UsageDisplayStyle.Text:
                    root = newText;
                    break;

                case UsageDisplayStyle.ProgressBar:
                    {
                        var container = new Grid
                        {
                            Width = 160,
                            Height = 42
                        };

                        var barMargin = new Thickness(10, 8, 10, 20);

                        var barBg = new Border
                        {
                            CornerRadius = new CornerRadius(5),
                            Background = new SolidColorBrush(item.BarBackgroundColor),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 80, 96)),
                            BorderThickness = new Thickness(1),
                            Margin = barMargin,
                            Height = 14
                        };
                        item.BarBackgroundBorder = barBg;

                        var barFill = new Rectangle
                        {
                            Height = 14,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                            Fill = new LinearGradientBrush(item.StartColor, item.EndColor, 0),
                            Width = 0,
                            RadiusX = 5,
                            RadiusY = 5,
                            Margin = barMargin
                        };
                        item.BarFill = barFill;

                        var barLayer = new Grid();
                        barLayer.Children.Add(barBg);
                        barLayer.Children.Add(barFill);

                        newText.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                        newText.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
                        newText.FontSize = 14;
                        newText.Margin = new Thickness(0, 0, 0, 2);

                        container.Children.Add(barLayer);
                        container.Children.Add(newText);
                        root = container;
                        break;
                    }

                case UsageDisplayStyle.Gauge:
                    {
                        var container = new Grid
                        {
                            Width = 160,
                            Height = 120,
                            ClipToBounds = false
                        };

                        var canvas = new Canvas
                        {
                            Width = 160,
                            Height = 120,
                            ClipToBounds = false
                        };

                        for (int percent = 0; percent <= 100; percent += GaugeMinorStep)
                        {
                            bool major = percent % GaugeMajorStep == 0;
                            double angle = GaugeAngleFromPercent(percent);
                            double rad = angle * Math.PI / 180.0;

                            double rOuter = GaugeRadiusOuter;
                            double rInner = major ? GaugeRadiusInner : (GaugeRadiusInner + 4);

                            double x1 = GaugeCenterX + rOuter * Math.Cos(rad);
                            double y1 = GaugeCenterY + rOuter * Math.Sin(rad);
                            double x2 = GaugeCenterX + rInner * Math.Cos(rad);
                            double y2 = GaugeCenterY + rInner * Math.Sin(rad);

                            double t = percent / 100.0;
                            var tickColor = LerpColor(item.StartColor, item.EndColor, t);
                            if (!major) tickColor = Color.FromArgb(160, tickColor.R, tickColor.G, tickColor.B);

                            var tick = new Line
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Stroke = new SolidColorBrush(tickColor),
                                StrokeThickness = major ? 3 : 1.5,
                                StrokeStartLineCap = PenLineCap.Round,
                                StrokeEndLineCap = PenLineCap.Round,
                                Tag = major ? "TickMajor" : "TickMinor",
                                DataContext = (double)percent
                            };
                            canvas.Children.Add(tick);

                            if (major && percent % GaugeLabelStep == 0)
                            {
                                double labelRadius = GaugeRadiusInner - 18;
                                double lx = GaugeCenterX + labelRadius * Math.Cos(rad);
                                double ly = GaugeCenterY + labelRadius * Math.Sin(rad);

                                var lbl = new TextBlock
                                {
                                    Text = percent.ToString(),
                                    FontSize = 12,
                                    Foreground = new SolidColorBrush(Color.FromRgb(200, 205, 215))
                                };
                                lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                                Canvas.SetLeft(lbl, lx - lbl.DesiredSize.Width / 2);
                                Canvas.SetTop(lbl, ly - lbl.DesiredSize.Height / 2);
                                canvas.Children.Add(lbl);

                                if (percent == 50 &&
                                    (item.Kind == LiveInfoKind.CpuUsage || item.Kind == LiveInfoKind.GpuUsage))
                                {
                                    double label2Radius = labelRadius - 10;
                                    double lx2 = GaugeCenterX + label2Radius * Math.Cos(rad);
                                    double ly2 = GaugeCenterY + label2Radius * Math.Sin(rad);

                                    var kindLabel = new TextBlock
                                    {
                                        Text = item.Kind == LiveInfoKind.CpuUsage ? "CPU" : "GPU",
                                        FontSize = 10,
                                        FontWeight = FontWeights.SemiBold,
                                        Foreground = new SolidColorBrush(Color.FromRgb(140, 150, 165))
                                    };
                                    kindLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                                    Canvas.SetLeft(kindLabel, lx2 - kindLabel.DesiredSize.Width / 2);
                                    Canvas.SetTop(kindLabel, ly2 - kindLabel.DesiredSize.Height / 2 + 10);
                                    canvas.Children.Add(kindLabel);
                                }
                            }
                        }

                        var needle = new Line
                        {
                            X1 = GaugeCenterX,
                            Y1 = GaugeCenterY,
                            X2 = GaugeCenterX,
                            Y2 = GaugeCenterY - GaugeNeedleLength,
                            StrokeThickness = 5,
                            Stroke = new SolidColorBrush(item.NeedleColor),
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap = PenLineCap.Triangle
                        };
                        var rt = new RotateTransform(GaugeRotationFromPercent(0), GaugeCenterX, GaugeCenterY);
                        needle.RenderTransform = rt;
                        item.GaugeNeedle = needle;
                        item.GaugeNeedleRotate = rt;
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

                        newText.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                        newText.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
                        newText.FontSize = 18;
                        newText.Margin = new Thickness(0, 0, 0, 0);

                        container.Children.Add(canvas);
                        container.Children.Add(newText);
                        root = container;
                        break;
                    }

                default:
                    root = newText;
                    break;
            }

            root.PreviewMouseLeftButtonDown += Item_PreviewMouseLeftButtonDown;
            border.Child = root;

            if (_selected == border)
            {
                OnPropertyChanged(nameof(IsTextSelected));
                UpdateSelectedInfo();
            }
        }

        // Helper to safely detach a UIElement from its current logical parent
        private static void DetachIfAttached(UIElement element)
        {
            if (element == null) return;
            var parent = LogicalTreeHelper.GetParent(element);
            switch (parent)
            {
                case Panel panel:
                    panel.Children.Remove(element);
                    break;
                case Border border:
                    if (border.Child == element) border.Child = null;
                    break;
                case ContentControl cc:
                    if (cc.Content == element) cc.Content = null;
                    break;
            }
        }

        // ================= Auto Move (Text only) =================
        private void UpdateAutoMoveTimer()
        {
            bool anyMoving = _movingDirections.Values.Any(v => Math.Abs(v.dirX) > 0.0001 || Math.Abs(v.dirY) > 0.0001);
            if (anyMoving)
            {
                if (!_autoMoveTimer.IsEnabled)
                    _autoMoveTimer.Start();
            }
            else
            {
                if (_autoMoveTimer.IsEnabled)
                    _autoMoveTimer.Stop();
            }
        }

        private void AutoMoveTimer_Tick(object? sender, EventArgs e)
        {
            if (_movingDirections.Count == 0) return;

            var now = DateTime.Now;
            var dt = (_lastAutoMoveTime == DateTime.MinValue ? 0.016 : (now - _lastAutoMoveTime).TotalSeconds);
            if (dt <= 0) return;
            _lastAutoMoveTime = now;

            var entries = _movingDirections.ToList();

            foreach (var (border, dir) in entries)
            {
                if (border.Child is not FrameworkElement) continue;
                if (!DesignCanvas.Children.Contains(border)) { _movingDirections.Remove(border); continue; }
                if (Math.Abs(dir.dirX) < 0.0001 && Math.Abs(dir.dirY) < 0.0001) continue;
                if (!GetTransforms(border, out var scale, out var translate)) continue;

                double dx = dir.dirX * MoveSpeed * dt;
                double dy = dir.dirY * MoveSpeed * dt;

                translate.X += dx;
                translate.Y += dy;

                double scaledW = border.ActualWidth * scale.ScaleX;
                double scaledH = border.ActualHeight * scale.ScaleY;

                if (translate.X > CanvasSize) translate.X = -scaledW;
                else if (translate.X + scaledW < 0) translate.X = CanvasSize;

                if (translate.Y > CanvasSize) translate.Y = -scaledH;
                else if (translate.Y + scaledH < 0) translate.Y = CanvasSize;
            }

            UpdateAutoMoveTimer();
        }

        private void JoystickBase_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isJoystickDragging = true;
            (sender as UIElement)?.CaptureMouse();
            UpdateJoystick(e.GetPosition((IInputElement)sender));
        }

        private void JoystickBase_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isJoystickDragging) return;
            UpdateJoystick(e.GetPosition((IInputElement)sender));
        }

        private void JoystickBase_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isJoystickDragging)
            {
                _isJoystickDragging = false;
                (sender as UIElement)?.ReleaseMouseCapture();
            }
        }

        private void JoystickCenter_Click(object sender, RoutedEventArgs e)
        {
            ResetMoveDirection();
        }

        private void ResetMoveDirection()
        {
            if (_selected != null)
            {
                if (_movingDirections.ContainsKey(_selected))
                    _movingDirections[_selected] = (0, 0);
            }
            MoveDirX = 0;
            MoveDirY = 0;
            if (FindName("JoystickKnobTransform") is TranslateTransform tt)
            {
                tt.X = 0;
                tt.Y = 0;
            }
            UpdateAutoMoveTimer();
        }

        private void UpdateJoystick(Point p)
        {
            double cx = JoystickBaseSize / 2.0;
            double cy = JoystickBaseSize / 2.0;

            double dx = p.X - cx;
            double dy = p.Y - cy;

            double dist = Math.Sqrt(dx * dx + dy * dy);
            double maxDist = JoystickMaxOffset;

            if (dist > maxDist)
            {
                double scale = maxDist / dist;
                dx *= scale;
                dy *= scale;
            }

            double newDirX, newDirY;
            if (dist < 1.5)
            {
                newDirX = 0;
                newDirY = 0;
            }
            else
            {
                var len = Math.Sqrt(dx * dx + dy * dy);
                newDirX = dx / len;
                newDirY = dy / len;
            }

            MoveDirX = newDirX;
            MoveDirY = newDirY;

            if (FindName("JoystickKnobTransform") is TranslateTransform tt)
            {
                tt.X = dx;
                tt.Y = dy;
            }

            if (_selected != null && !IsUsageSelected && GetCurrentTextBlock() != null)
            {
                if (Math.Abs(newDirX) < 0.0001 && Math.Abs(newDirY) < 0.0001)
                    _movingDirections[_selected] = (0, 0);
                else
                    _movingDirections[_selected] = (newDirX, newDirY);
            }

            UpdateAutoMoveTimer();
        }

        private static bool GetTransforms(Border border, out ScaleTransform scale, out TranslateTransform translate)
        {
            if (border.RenderTransform is TransformGroup tg)
            {
                scale = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
                translate = tg.Children.OfType<TranslateTransform>().FirstOrDefault();

                if (scale != null && translate != null)
                    return true;

                if (scale == null)
                {
                    scale = new ScaleTransform(1, 1);
                    tg.Children.Insert(0, scale);
                }
                if (translate == null)
                {
                    translate = new TranslateTransform(0, 0);
                    tg.Children.Add(translate);
                }
                return false;
            }

            scale = new ScaleTransform(1, 1);
            translate = new TranslateTransform(0, 0);
            var newGroup = new TransformGroup();
            newGroup.Children.Add(scale);
            newGroup.Children.Add(translate);
            border.RenderTransform = newGroup;
            return false;
        }

        // ================= Usage Theme Update for Selection Change =================
        private void ApplyUsageThemeToSelection()
        {
            if (!IsUsageSelected) return;
            var liveItem = _liveItems.FirstOrDefault(i => i.Border == _selected);
            if (liveItem == null) return;

            UsageStartColor = liveItem.StartColor;
            UsageEndColor = liveItem.EndColor;
            UsageNeedleColor = liveItem.NeedleColor;
            SelectedUsageDisplayStyle = liveItem.DisplayStyle.ToString();
            UsageBarBackgroundColor = liveItem.BarBackgroundColor;
        }

        // ================= Cleanup =================
        private void DeviceConfigPage_Unloaded(object sender, RoutedEventArgs e)
        {
            try { _liveTimer.Stop(); } catch { }
            try { _autoMoveTimer.Stop(); } catch { }
            StopRealtimeJpegStreaming();
            StopVideoPlayback();
            // 不再 Dispose 单例 _metrics（RealSystemMetricsService.Instance 是全局的） 
        }
        private void StartRealtimeJpegStreaming()
        {
            if (_realtimeActive) return;
            // 尝试获取设计画布（按你工程中的名称调整：DesignCanvas / MirrorRoot / RootCanvas …）
            _captureRoot ??= (FrameworkElement?)this.FindName("DesignCanvas")
                          ?? (FrameworkElement?)this.FindName("MirrorRoot")
                          ?? this.Content as FrameworkElement;

            if (_captureRoot == null)
                return;

            _realtimeJpegTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(RealtimeIntervalMs)
            };
            _realtimeJpegTimer.Tick += RealtimeJpegTimer_Tick;
            _realtimeJpegTimer.Start();
            _realtimeActive = true;
        }

        private void StopRealtimeJpegStreaming()
        {
            _realtimeActive = false;
            if (_realtimeJpegTimer != null)
            {
                _realtimeJpegTimer.Stop();
                _realtimeJpegTimer.Tick -= RealtimeJpegTimer_Tick;
                _realtimeJpegTimer = null;
            }
        }

        private async void RealtimeJpegTimer_Tick(object? sender, EventArgs e)
        {
            if (!_realtimeActive) return;
            if (_captureRoot == null || _captureRoot.ActualWidth < 1 || _captureRoot.ActualHeight < 1)
                return;

            var jpeg = CaptureElementToJpegFixedSquare(_captureRoot, RealtimeJpegSize);
            if (jpeg == null || jpeg.Length == 0) return;

            try
            {
                var realtimeService = ServiceLocator.RealtimeJpegTransmissionService;
                realtimeService?.QueueJpegData(jpeg, "MainPreview");
            }
            catch
            {
                // swallow to keep timer running
            }
        }

        /// <summary>
        /// 将指定可视元素渲染为固定尺寸(方形)JPEG（保持内容比例，居中填充黑色边框）。
        /// </summary>
        private byte[]? CaptureElementToJpegFixedSquare(FrameworkElement element, int size)
        {
            try
            {
                // 强制最新布局
                element.UpdateLayout();

                int srcW = (int)Math.Ceiling(element.ActualWidth);
                int srcH = (int)Math.Ceiling(element.ActualHeight);
                if (srcW <= 0 || srcH <= 0) return null;

                // 原始渲染
                var rtb = new RenderTargetBitmap(srcW, srcH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(element);

                // 目标 DrawingVisual（统一缩放到 size x size）
                double scale = Math.Min((double)size / srcW, (double)size / srcH);
                double drawW = srcW * scale;
                double drawH = srcH * scale;
                double offsetX = (size - drawW) / 2.0;
                double offsetY = (size - drawH) / 2.0;

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, size, size));
                    dc.DrawImage(rtb, new Rect(offsetX, offsetY, drawW, drawH));
                }

                var finalBmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                finalBmp.Render(dv);

                // 编码 JPEG
                var encoder = new JpegBitmapEncoder
                {
                    QualityLevel = 80 // 可按需来自配置
                };
                encoder.Frames.Add(BitmapFrame.Create(finalBmp));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        // ================= Image Rotate / Mirror =================
        private void EnsureImageExtendedTransforms(Border border, out ScaleTransform mirrorScale, out RotateTransform rotate)
        {
            mirrorScale = null!;
            rotate = null!;
            if (border.RenderTransform is not TransformGroup tg) return;

            var contentScale = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
            if (contentScale == null)
            {
                contentScale = new ScaleTransform(1, 1);
                tg.Children.Insert(0, contentScale);
            }

            var translate = tg.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (translate == null)
            {
                translate = new TranslateTransform(0, 0);
                tg.Children.Add(translate);
            }

            mirrorScale = tg.Children
                            .OfType<ScaleTransform>()
                            .Skip(1)
                            .FirstOrDefault();
            if (mirrorScale == null)
            {
                mirrorScale = new ScaleTransform(1, 1);
                int insertIndex = tg.Children.IndexOf(contentScale) + 1;
                tg.Children.Insert(insertIndex, mirrorScale);
            }

            rotate = tg.Children.OfType<RotateTransform>().FirstOrDefault();
            if (rotate == null)
            {
                rotate = new RotateTransform(0);
                int translateIndex = tg.Children.IndexOf(translate);
                if (translateIndex < 0) translateIndex = tg.Children.Count;
                tg.Children.Insert(translateIndex, rotate);
            }
            else
            {
                int mirrorIndex = tg.Children.IndexOf(mirrorScale);
                int rotateIndex = tg.Children.IndexOf(rotate);
                int translateIndex = tg.Children.IndexOf(translate);
                if (!(mirrorIndex < rotateIndex && rotateIndex < translateIndex))
                {
                    tg.Children.Remove(rotate);
                    mirrorIndex = tg.Children.IndexOf(mirrorScale);
                    tg.Children.Insert(mirrorIndex + 1, rotate);
                }
            }
        }

        private void RotateSelectedImage(double deltaAngle)
        {
            if (_selected?.Child is not Image || _selected.Tag is VideoElementInfo)
                return;

            if (_selected.RenderTransform is TransformGroup)
            {
                EnsureImageExtendedTransforms(_selected, out _, out var rotate);
                rotate.Angle = (rotate.Angle + deltaAngle) % 360;
            }
        }

        private void MirrorSelectedImageHorizontal()
        {
            if (_selected?.Child is not Image || _selected.Tag is VideoElementInfo)
                return;

            if (_selected.RenderTransform is TransformGroup)
            {
                EnsureImageExtendedTransforms(_selected, out var mirrorScale, out _);
                mirrorScale.ScaleX *= -1;
            }
        }

        private void RotateImageLeft_Click(object sender, RoutedEventArgs e) => RotateSelectedImage(-90);

        private void CaptureCanvas_Click(object sender, RoutedEventArgs e)
        {
            CaptureCanvas();
        }

        private byte[] CaptureCanvasAsStream()
        {
            try
            {

                // Ensure we're on the UI thread
                if (!Dispatcher.CheckAccess())
                {
                    Logger.Warn("CaptureCanvasAsStream called from non-UI thread");
                    return Dispatcher.Invoke(() => CaptureCanvasAsStream());
                }

                if (DesignRoot == null || DesignRoot.ActualWidth <= 0 || DesignRoot.ActualHeight <= 0)
                {
                    return null;
                }

                // 记录当前选中的元素边框颜色以便恢复，避免截图出现高亮
                Border? prevSelected = _selected;
                Brush? prevBrush = null;
                if (prevSelected != null)
                {
                    prevBrush = prevSelected.BorderBrush;
                    prevSelected.BorderBrush = Brushes.Transparent;
                }

                // 强制刷新布局，确保最新渲染
                DesignRoot.UpdateLayout();

                int pixelWidth = (int)Math.Ceiling(DesignRoot.ActualWidth);
                int pixelHeight = (int)Math.Ceiling(DesignRoot.ActualHeight);

                var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(DesignRoot);

                // 恢复选中边框
                if (prevSelected != null && prevBrush != null)
                    prevSelected.BorderBrush = prevBrush;

                var encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));

                // get image data as byte array
                using var ms = new MemoryStream();
                encoder.Save(ms);
                byte[] imageData = ms.ToArray();

                return imageData;

            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private void CaptureCanvas()
        {
            try
            {
                if (DesignRoot == null || DesignRoot.ActualWidth <= 0 || DesignRoot.ActualHeight <= 0)
                {
                    MessageBox.Show("当前画布尚未准备好。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 记录当前选中的元素边框颜色以便恢复，避免截图出现高亮
                Border? prevSelected = _selected;
                Brush? prevBrush = null;
                if (prevSelected != null)
                {
                    prevBrush = prevSelected.BorderBrush;
                    prevSelected.BorderBrush = Brushes.Transparent;
                }

                // 强制刷新布局，确保最新渲染
                DesignRoot.UpdateLayout();

                int pixelWidth = (int)Math.Ceiling(DesignRoot.ActualWidth);
                int pixelHeight = (int)Math.Ceiling(DesignRoot.ActualHeight);

                var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(DesignRoot);

                // 恢复选中边框
                if (prevSelected != null && prevBrush != null)
                    prevSelected.BorderBrush = prevBrush;

                string captureDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "captureimage");
                Directory.CreateDirectory(captureDir);

                string safeName = string.IsNullOrWhiteSpace(CurrentConfigName) ? "CanvasCapture" : MakeSafeFileBase(CurrentConfigName);
                string fileName = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.jpeg";
                string fullPath = Path.Combine(captureDir, fileName);

                var encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }

                MessageBox.Show($"截图已保存:\n{fullPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"截图失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            StartRealtimeJpegStreaming();
        }

        private void RotateImageRight_Click(object sender, RoutedEventArgs e) => RotateSelectedImage(90);
        private void MirrorImageHorizontal_Click(object sender, RoutedEventArgs e) => MirrorSelectedImageHorizontal();
    }
}