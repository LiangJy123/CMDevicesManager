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
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;

namespace CMDevicesManager.Pages
{
    // ================= Configuration Data Models =================
    public class CanvasConfiguration
    {
        public string ConfigName { get; set; } = "Untitled";
        public int CanvasSize { get; set; }
        public string BackgroundColor { get; set; } = "#FFFFFF";
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

        public string? VideoFramesCacheFolder { get; set; }   // NEW
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

   

    // ================= Main Page =================
    public partial class DeviceConfigPage : Page, INotifyPropertyChanged
    {

        private const long MaxMp4FileBytes = 10 * 1024 * 1024; // 10MB size limit

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
            CpuTemperature,   // NEW
            GpuTemperature,   // NEW
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

        private Color _backgroundColor = Colors.White;
        public Color BackgroundColor { get => _backgroundColor; set { _backgroundColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(BackgroundBrush)); BackgroundHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}"; } }
        public Brush BackgroundBrush => new SolidColorBrush(BackgroundColor);
        private string? _backgroundImagePath;
        public string? BackgroundImagePath { get => _backgroundImagePath; set { _backgroundImagePath = value; OnPropertyChanged(); } }
        private double _backgroundImageOpacity = 1.0;
        public double BackgroundImageOpacity { get => _backgroundImageOpacity; set { _backgroundImageOpacity = Math.Clamp(value, 0, 1); OnPropertyChanged(); } }
        private string _backgroundHex = "#FFFFFF";
        public string BackgroundHex { get => _backgroundHex; set { if (_backgroundHex != value) { _backgroundHex = value; if (TryParseHexColor(value, out var c)) BackgroundColor = c; OnPropertyChanged(); } } }

        private Border? _selected;
        private ScaleTransform? _selScale;
        private TranslateTransform? _selTranslate;

        private string _selectedInfo = "None";
        public string SelectedInfo { get => _selectedInfo; set { _selectedInfo = value; OnPropertyChanged(); } }
        public bool IsAnySelected => _selected != null;

        public bool IsUsageSelected =>
     _selected?.Tag is LiveInfoKind k &&
     (k == LiveInfoKind.CpuUsage ||
      k == LiveInfoKind.GpuUsage ||
      k == LiveInfoKind.CpuTemperature ||    // NEW
      k == LiveInfoKind.GpuTemperature);     // NEW

        public bool IsGaugeSelected => string.Equals(NormalizeUsageStyleString(SelectedUsageDisplayStyle), "Gauge", StringComparison.OrdinalIgnoreCase);
        public bool IsUsageVisualSelected
            => IsUsageSelected && !string.Equals(NormalizeUsageStyleString(SelectedUsageDisplayStyle), "Text", StringComparison.OrdinalIgnoreCase);
        private string? _loadedConfigFilePath;

        public bool IsTextSelected => GetCurrentTextBlock() != null && !IsUsageSelected;
        public bool IsSelectedTextReadOnly => _selected?.Tag is LiveInfoKind kind &&
            (kind == LiveInfoKind.CpuUsage ||
             kind == LiveInfoKind.GpuUsage ||
             kind == LiveInfoKind.CpuTemperature ||   // NEW
             kind == LiveInfoKind.GpuTemperature ||   // NEW
             kind == LiveInfoKind.DateTime);
        public bool IsImageSelected => _selected?.Child is Image && _selected.Tag is not VideoElementInfo;
        public bool IsDateTimeSelected => _selected?.Tag is LiveInfoKind kind && kind == LiveInfoKind.DateTime;
        // 在现有属性附近添加新属性
        public bool IsUsageTextModeSelected =>
            IsUsageSelected && string.Equals(NormalizeUsageStyleString(SelectedUsageDisplayStyle), "Text", StringComparison.OrdinalIgnoreCase);

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
                if (IsUsageTextModeSelected)
                {
                    ApplyTextColorOrGradient();
                }
                else
                {
                    ApplyUsageTheme();
                }

               
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
                if (IsUsageTextModeSelected)
                {
                    ApplyTextColorOrGradient();
                }
                else
                {
                    ApplyUsageTheme();
                }
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
                OnPropertyChanged(nameof(IsUsageTextModeSelected));
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
                OnPropertyChanged(nameof(IsUsageTextModeSelected));
            }
        }
        private void OpenColorPicker(Color initial, Action<Color> apply)
        {
            var dlg = new ColorPickerWindow(initial);
            dlg.Owner = Application.Current?.MainWindow;

            dlg.WindowStartupLocation = WindowStartupLocation.Manual;

            var owner = Application.Current?.MainWindow;
            if (owner != null)
            {
                // Position relative to owner window's right side
                dlg.Left = owner.Left + owner.ActualWidth - dlg.Width - 20;
                dlg.Top = owner.Top + (owner.ActualHeight - dlg.Height) / 2;
            }

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
        private void BuildSystemInfoButtons()
        {
            SystemInfoItems.Clear();
            SystemInfoItems.Add(new SystemInfoItem(
                LiveInfoKind.CpuUsage,
                $"{Application.Current.FindResource("CpuUsage")?.ToString() ?? "CPU Usage"} ({_metrics.CpuName})"));

            var gpuName = (_metrics as RealSystemMetricsService)?.PrimaryGpuName ?? "GPU";
            var gpuVal = _metrics.GetGpuUsagePercent();
            if (!string.Equals(gpuName, "GPU", StringComparison.OrdinalIgnoreCase) || gpuVal > 0)
                SystemInfoItems.Add(new SystemInfoItem(
                    LiveInfoKind.GpuUsage,
                    $"{Application.Current.FindResource("GpuUsage")?.ToString() ?? "GPU Usage"} ({gpuName})"));

            double cpuTemp = _metrics.GetCpuTemperature();
            if (!double.IsNaN(cpuTemp) && cpuTemp > 0)
            {
                var cpuTempLabel = Application.Current.FindResource("CpuTemp")?.ToString() ?? "CPU Temp";
                SystemInfoItems.Add(new SystemInfoItem(LiveInfoKind.CpuTemperature, $"{cpuTempLabel} ({cpuTemp:F0}°C)"));
            }

            double gpuTemp = _metrics.GetGpuTemperature();
            if (!double.IsNaN(gpuTemp) && gpuTemp > 0)
            {
                var gpuTempLabel = Application.Current.FindResource("GpuTemp")?.ToString() ?? "GPU Temp";
                SystemInfoItems.Add(new SystemInfoItem(LiveInfoKind.GpuTemperature, $"{gpuTempLabel} ({gpuTemp:F0}°C)"));
            }
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

            string displayText = kind switch
            {
                LiveInfoKind.CpuUsage => $"CPU {Math.Round(_metrics.GetCpuUsagePercent())}%",
                LiveInfoKind.GpuUsage => $"GPU {Math.Round(_metrics.GetGpuUsagePercent())}%",
                LiveInfoKind.CpuTemperature => $"CPU {Math.Round(_metrics.GetCpuTemperature())}°C",   // NEW
                LiveInfoKind.GpuTemperature => $"GPU {Math.Round(_metrics.GetGpuTemperature())}°C",   // NEW
                _ => "N/A"
            };

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
            var cpuTempText = Application.Current.FindResource("CpuTemp")?.ToString() ?? "CPU Temp"; ; // 可视需要再加资源
            var gpuTempText = Application.Current.FindResource("GpuTemp")?.ToString() ?? "GPU Temp"; ;

            string header = kind switch
            {
                LiveInfoKind.CpuUsage => cpuUsageText,
                LiveInfoKind.GpuUsage => gpuUsageText,
                LiveInfoKind.CpuTemperature => cpuTempText,
                LiveInfoKind.GpuTemperature => gpuTempText,
                _ => "Info"
            };

            var border = AddElement(textBlock, header);
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
        // 在 Color Pickers 区域添加新方法
        private void SetBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            OpenColorPicker(BackgroundColor, c =>
            {
                BackgroundColor = c;
                BackgroundHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            });
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
            if (!IsUsageVisualSelected && !IsUsageTextModeSelected) return;
            OpenColorPicker(UsageStartColor, c => UsageStartColor = c);
        }

        private void PickUsageEndColor_Click(object sender, RoutedEventArgs e)
        {
            if (!IsUsageVisualSelected && !IsUsageTextModeSelected) return;
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

                    // 如果是文本模式，从 TextBlock 的 Foreground 判断是否使用渐变
                    if (liveItem.DisplayStyle == UsageDisplayStyle.Text)
                    {
                        var tb_tmp = GetCurrentTextBlock();
                        if (tb_tmp.Foreground is LinearGradientBrush)
                        {
                            UseTextGradient = true;
                        }
                        else
                        {
                            UseTextGradient = false;
                        }
                    }
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
                        LiveInfoKind.CpuTemperature => "CPU Temp",   // NEW （可根据需要接入资源）
                        LiveInfoKind.GpuTemperature => "GPU Temp",   // NEW
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
                var fi = new FileInfo(dlg.FileName);
                if (!fi.Exists)
                {
                    LocalizedMessageBox.Show("FileNotExist", "FileError", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (fi.Length > MaxMp4FileBytes)
                {
                    LocalizedMessageBox.Show(
                        string.Format(Application.Current.FindResource("VideoFileExceedsLimit")?.ToString() ?? "视频文件超过10MB限制。\n大小：{0} MB", (fi.Length / 1024.0 / 1024.0).ToString("F2")),
                        "FileTooLarge",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning,
                        true);
                    return;
                }
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(string.Format(Application.Current.FindResource("FileSizeValidationFailed")?.ToString() ?? "文件大小验证失败：{0}", ex.Message), "Error", MessageBoxButton.OK, MessageBoxImage.Error, true);
                return;
            }
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var copiedPath = CopyResourceToAppFolder(dlg.FileName, "Videos");

                var videoInfo = await VideoConverter.GetMp4InfoAsync(copiedPath);
                if (videoInfo == null)
                {
                    LocalizedMessageBox.Show("ReadVideoInfoFailed", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var frames = await ExtractMp4FramesToMemory(copiedPath);
                if (frames == null || frames.Count == 0)
                {
                    LocalizedMessageBox.Show("ExtractVideoFramesFailed", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                LocalizedMessageBox.Show(string.Format(Application.Current.FindResource("LoadMp4Failed")?.ToString() ?? "加载MP4失败：{0}", ex.Message), "Error", MessageBoxButton.OK, MessageBoxImage.Error, true);
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
        private string? SaveVideoFramesCache(VideoElementInfo videoInfo, List<VideoFrameData> frames)
        {
            try
            {
                if (frames == null || frames.Count == 0) return null;
                var framesRoot = Path.Combine(ResourcesFolder, "VideoFrames");
                Directory.CreateDirectory(framesRoot);

                var baseName = Path.GetFileNameWithoutExtension(videoInfo.FilePath);
                var targetFolder = Path.Combine(framesRoot, baseName + "_frames");

                bool needWrite = true;
                if (Directory.Exists(targetFolder))
                {
                    var existing = Directory.GetFiles(targetFolder, "frame_*.jpg").Length;
                    if (existing == frames.Count) needWrite = false;
                }

                if (needWrite)
                {
                    Directory.CreateDirectory(targetFolder);
                    for (int i = 0; i < frames.Count; i++)
                    {
                        var framePath = Path.Combine(targetFolder, $"frame_{i:D5}.jpg");
                        if (!File.Exists(framePath))
                            File.WriteAllBytes(framePath, frames[i].JpegData);
                    }
                }

                return GetRelativePath(targetFolder);
            }
            catch (Exception ex)
            {
                Logger.Info("SaveVideoFramesCache failed: " + ex.Message);
                return null;
            }
        }

        // VIDEO CACHE: load previously saved frames
        private List<VideoFrameData>? LoadCachedVideoFrames(string relativeOrFullFolder)
        {
            try
            {
                string full = relativeOrFullFolder;
                if (!Path.IsPathRooted(full))
                {
                    full = Path.Combine(OutputFolder, relativeOrFullFolder);
                    if (!Directory.Exists(full))
                        full = Path.Combine(ResourcesFolder, relativeOrFullFolder);
                }
                if (!Directory.Exists(full)) return null;

                var files = Directory.GetFiles(full, "frame_*.jpg")
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (files.Count == 0) return null;

                var list = new List<VideoFrameData>(files.Count);
                for (int i = 0; i < files.Count; i++)
                {
                    var data = File.ReadAllBytes(files[i]);
                    list.Add(new VideoFrameData
                    {
                        FrameIndex = i,
                        JpegData = data,
                        TimeStampMs = i,
                        DurationMs = 0,
                        Width = 0,
                        Height = 0
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                Logger.Info("LoadCachedVideoFrames failed: " + ex.Message);
                return null;
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

                LocalizedMessageBox.Show(string.Format("{0}: {1}", msg, CurrentConfigName), title, MessageBoxButton.OK, MessageBoxImage.Information, true);
                if (IsGlobalPlayModeEmpty())
                    CheckAndPromptPlayMode();
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(string.Format(Application.Current.FindResource("ConfigSaveFailed")?.ToString() ?? "保存配置失败：{0}", ex.Message), "Error", MessageBoxButton.OK, MessageBoxImage.Error, true);
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

                LocalizedMessageBox.Show(string.Format("{0}: {1}", msg, newName), title, MessageBoxButton.OK, MessageBoxImage.Information, true);
                if (IsGlobalPlayModeEmpty())
                    CheckAndPromptPlayMode();
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(string.Format(Application.Current.FindResource("SaveAsFailed")?.ToString() ?? "另存为失败：{0}", ex.Message), "Error", MessageBoxButton.OK, MessageBoxImage.Error, true);
            }
        }

        private string SaveConfigCore(string configName, bool deletePreviousLoaded)
        {
            var configFolder = Path.Combine(OutputFolder, "Configs");
            Directory.CreateDirectory(configFolder);

            string safeName = MakeSafeFileBase(configName);
            string baseFileName = safeName + ".json";
            string targetPath = Path.Combine(configFolder, baseFileName);

            // If previous loaded file should be deleted AND it's different from target (old timestamped behavior)
            if (deletePreviousLoaded &&
                _loadedConfigFilePath != null &&
                !string.Equals(Path.GetFullPath(_loadedConfigFilePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
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

            // Handle name collision (if not the same file already loaded)
            if (File.Exists(targetPath))
            {
                // Ask user overwrite / rename / cancel
                var overwriteResult = LocalizedMessageBox.Show(
                    string.Format(Application.Current.FindResource("ConfigFileExistsPrompt")?.ToString() ?? "配置文件 \"{0}\" 已存在。\n\n是: 覆盖\n否: 重新命名\n取消: 中止保存", baseFileName),
                    "FileExists",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question,
                    true);

                if (overwriteResult == MessageBoxResult.Cancel)
                    throw new OperationCanceledException(Application.Current.FindResource("UserCancelledSave")?.ToString() ?? "用户取消保存。");

                if (overwriteResult == MessageBoxResult.No)
                {
                    // Rename loop
                    bool gotUnique = false;
                    while (!gotUnique)
                    {
                        var renameDialog = new ConfigNameDialog(safeName + "_Copy");
                        if (Application.Current.MainWindow != null)
                            renameDialog.Owner = Application.Current.MainWindow;

                        if (renameDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(renameDialog.ConfigName))
                            throw new OperationCanceledException(Application.Current.FindResource("UserCancelledRename")?.ToString() ?? "用户取消重命名。");

                        safeName = MakeSafeFileBase(renameDialog.ConfigName.Trim());
                        baseFileName = safeName + ".json";
                        targetPath = Path.Combine(configFolder, baseFileName);

                        if (!File.Exists(targetPath))
                        {
                            // Update outward visible ConfigName if user chose new name
                            CurrentConfigName = renameDialog.ConfigName.Trim();
                            configName = CurrentConfigName;
                            gotUnique = true;
                        }
                        else
                        {
                            LocalizedMessageBox.Show(string.Format(Application.Current.FindResource("NameAlreadyExists")?.ToString() ?? "名称 \"{0}\" 仍已存在，请重新输入。", baseFileName), "NameConflict", MessageBoxButton.OK, MessageBoxImage.Warning, true);
                        }
                    }
                }
                // If Yes -> overwrite: continue
            }

            // Build config object
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
                            if (_currentVideoBorder == border && _currentVideoFrames != null && _currentVideoFrames.Count > 0)
                            {
                                var cacheRel = SaveVideoFramesCache(videoInfo, _currentVideoFrames);
                                if (!string.IsNullOrEmpty(cacheRel))
                                    elemConfig.VideoFramesCacheFolder = cacheRel;
                            }
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

            // Serialize & write (overwrite if chosen)
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });
            File.WriteAllText(targetPath, json);
            SaveConfigPreview(safeName, configFolder);
            _loadedConfigFilePath = targetPath;
            return targetPath;
        }

        private void SaveConfigPreview(string safeBaseName, string configFolder)
        {
            try
            {
                var data = CaptureCanvasAsStream();
                if (data == null || data.Length == 0) return;

                // 简单直接保存 JPEG（若需缩放可后续扩展 DecodePixelWidth）
                var previewPath = Path.Combine(configFolder, safeBaseName + ".preview.jpg");
                File.WriteAllBytes(previewPath, data);
            }
            catch (Exception ex)
            {
                Logger.Info("SaveConfigPreview failed: " + ex.Message);
            }
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
        // === Add below (if not already added) ===
        private static Color ParseColorOr(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            return TryParseHexColor(hex, out var c) ? c : fallback;
        }

        private Border CreateBaseBorder(FrameworkElement child)
        {
            var border = new Border
            {
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Child = child,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Cursor = Cursors.SizeAll
            };
            border.PreviewMouseLeftButtonDown += Item_PreviewMouseLeftButtonDown;
            border.PreviewMouseLeftButtonUp += Item_PreviewMouseLeftButtonUp;
            border.PreviewMouseMove += Item_PreviewMouseMove;
            border.MouseDown += (_, __) => SelectElement(border);
            border.MouseEnter += (_, __) => { if (_selected != border) border.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 30, 144, 255)); };
            border.MouseLeave += (_, __) => { if (_selected != border) border.BorderBrush = Brushes.Transparent; };
            return border;
        }

        private void ApplyTransform(Border border, double scale, double x, double y)
        {
            var tg = new TransformGroup();
            var sc = new ScaleTransform(scale <= 0 ? 1 : scale, scale <= 0 ? 1 : scale);
            var tr = new TranslateTransform(x, y);
            tg.Children.Add(sc);
            tg.Children.Add(tr);
            border.RenderTransform = tg;
        }

        private void RestoreTextElement(ElementConfiguration ec)
        {
            var tb = new TextBlock
            {
                Text = ec.Text ?? "Text",
                FontSize = ec.FontSize ?? 24,
                FontWeight = FontWeights.SemiBold
            };
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

            if (ec.UseTextGradient == true &&
                !string.IsNullOrWhiteSpace(ec.TextColor) &&
                !string.IsNullOrWhiteSpace(ec.TextColor2) &&
                TryParseHexColor(ec.TextColor, out var c1) &&
                TryParseHexColor(ec.TextColor2, out var c2))
            {
                tb.Foreground = new LinearGradientBrush(c1, c2, 45);
            }
            else if (!string.IsNullOrWhiteSpace(ec.TextColor) && TryParseHexColor(ec.TextColor, out var cSolid))
            {
                tb.Foreground = new SolidColorBrush(cSolid);
            }
            else tb.Foreground = Brushes.White;

            var border = CreateBaseBorder(tb);
            border.Opacity = ec.Opacity <= 0 ? 1 : ec.Opacity;
            ApplyTransform(border, ec.Scale, ec.X, ec.Y);
            Canvas.SetZIndex(border, ec.ZIndex);
            DesignCanvas.Children.Add(border);

            if (ec.MoveDirX.HasValue && ec.MoveDirY.HasValue &&
                (Math.Abs(ec.MoveDirX.Value) > 0.0001 || Math.Abs(ec.MoveDirY.Value) > 0.0001))
                _movingDirections[border] = (ec.MoveDirX.Value, ec.MoveDirY.Value);
        }

        private void RestoreLiveElement(ElementConfiguration ec)
        {
            if (!ec.LiveKind.HasValue) return;
            var kind = ec.LiveKind.Value;

            var tb = new TextBlock
            {
                FontSize = ec.FontSize ?? 18,
                FontWeight = FontWeights.SemiBold,
                Text = "..."
            };
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

            var border = CreateBaseBorder(tb);
            border.Opacity = ec.Opacity <= 0 ? 1 : ec.Opacity;
            border.Tag = kind;
            ApplyTransform(border, ec.Scale, ec.X, ec.Y);
            Canvas.SetZIndex(border, ec.ZIndex);
            DesignCanvas.Children.Add(border);

            var item = new LiveTextItem
            {
                Border = border,
                Text = tb,
                Kind = kind,
                DisplayStyle = UsageDisplayStyle.Text
            };

            // Usage style?
            string style = ec.UsageDisplayStyle ?? "Text";
            if (!Enum.TryParse<UsageDisplayStyle>(style, true, out var parsed))
                parsed = UsageDisplayStyle.Text;
            item.DisplayStyle = parsed;

            // Theme
            item.StartColor = ParseColorOr(ec.UsageStartColor, Color.FromRgb(80, 180, 80));
            item.EndColor = ParseColorOr(ec.UsageEndColor, Color.FromRgb(40, 120, 40));
            item.NeedleColor = ParseColorOr(ec.UsageNeedleColor, Color.FromRgb(90, 200, 90));
            item.BarBackgroundColor = ParseColorOr(ec.UsageBarBackgroundColor, Color.FromRgb(40, 46, 58));
            if (kind == LiveInfoKind.DateTime)
                item.DateFormat = ec.DateFormat ?? "yyyy-MM-dd HH:mm:ss";

            _liveItems.Add(item);

            if (item.DisplayStyle != UsageDisplayStyle.Text)
            {
                RebuildUsageVisual(item);
                ApplyUsageTheme();
            }
            else
            {
                // plain text style coloring
                if (ec.UseTextGradient == true &&
                    !string.IsNullOrWhiteSpace(ec.TextColor) &&
                    !string.IsNullOrWhiteSpace(ec.TextColor2) &&
                    TryParseHexColor(ec.TextColor, out var c1) &&
                    TryParseHexColor(ec.TextColor2, out var c2))
                {
                    tb.Foreground = new LinearGradientBrush(c1, c2, 45);
                }
                else if (!string.IsNullOrWhiteSpace(ec.TextColor) && TryParseHexColor(ec.TextColor, out var cSolid))
                {
                    tb.Foreground = new SolidColorBrush(cSolid);
                }
                else tb.Foreground = Brushes.White;
            }

            // Initial text
            switch (kind)
            {
                case LiveInfoKind.CpuUsage: tb.Text = "CPU 0%"; break;
                case LiveInfoKind.GpuUsage: tb.Text = "GPU 0%"; break;
                case LiveInfoKind.CpuTemperature: tb.Text = "CPU 0°C"; break;
                case LiveInfoKind.GpuTemperature: tb.Text = "GPU 0°C"; break;
                case LiveInfoKind.DateTime: tb.Text = DateTime.Now.ToString(item.DateFormat); break;
            }

            if (ec.MoveDirX.HasValue && ec.MoveDirY.HasValue &&
                (Math.Abs(ec.MoveDirX.Value) > 0.0001 || Math.Abs(ec.MoveDirY.Value) > 0.0001))
                _movingDirections[border] = (ec.MoveDirX.Value, ec.MoveDirY.Value);
        }

        private void RestoreImageElement(ElementConfiguration ec)
        {
            if (string.IsNullOrWhiteSpace(ec.ImagePath)) return;
            string resolved = ResolveRelativePath(ec.ImagePath);
            if (!File.Exists(resolved)) return;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(resolved, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                var img = new Image { Source = bmp, Stretch = Stretch.Uniform };
                var border = CreateBaseBorder(img);
                border.Opacity = ec.Opacity <= 0 ? 1 : ec.Opacity;

                var tg = new TransformGroup();
                tg.Children.Add(new ScaleTransform(ec.Scale <= 0 ? 1 : ec.Scale, ec.Scale <= 0 ? 1 : ec.Scale));
                if (ec.MirroredX == true) tg.Children.Add(new ScaleTransform(-1, 1));
                if (ec.Rotation.HasValue && Math.Abs(ec.Rotation.Value) > 0.01)
                    tg.Children.Add(new RotateTransform(ec.Rotation.Value));
                tg.Children.Add(new TranslateTransform(ec.X, ec.Y));
                border.RenderTransform = tg;

                Canvas.SetZIndex(border, ec.ZIndex);
                DesignCanvas.Children.Add(border);
            }
            catch { }
        }

        // === 添加：恢复视频元素 ===
        // 放在其它 RestoreXXX 方法附近，或文件中部任意合适位置（类内部）
        // 依赖已有: ResolveRelativePath, LoadCachedVideoFrames, VideoElementInfo, StartVideoPlayback,
        // _currentVideoFrames, _currentVideoImage, _currentVideoBorder, _currentFrameIndex

        private void RestoreVideoElement(ElementConfiguration ec)
        {
            if (string.IsNullOrWhiteSpace(ec.VideoPath))
                return;

            try
            {
                // 先清理当前正在播放的视频（保持与 AddMp4_Click 单一视频的语义一致）
                if (_currentVideoBorder != null && DesignCanvas.Children.Contains(_currentVideoBorder))
                {
                    StopVideoPlayback();
                    DesignCanvas.Children.Remove(_currentVideoBorder);
                    _currentVideoFrames = null;
                    _currentVideoImage = null;
                    _currentVideoBorder = null;
                }

                string resolved = ResolveRelativePath(ec.VideoPath);
                // 可以不存在（用户可能四处移动文件），仍然给个占位
                List<VideoFrameData>? cached = null;
                if (!string.IsNullOrWhiteSpace(ec.VideoFramesCacheFolder))
                    cached = LoadCachedVideoFrames(ec.VideoFramesCacheFolder);

                Border host;
                Image? videoImage = null;

                if (cached != null && cached.Count > 0)
                {
                    // 使用缓存帧
                    _currentVideoFrames = cached;
                    _currentFrameIndex = 0;
                    videoImage = new Image { Stretch = Stretch.Uniform };
                    UpdateVideoFrame(videoImage, cached[0]); // 利用已有方法
                    host = CreateBaseBorder(videoImage);
                    _currentVideoImage = videoImage;
                    _currentVideoBorder = host;

                    // 异步探测帧率再启动，否则默认 30
                    double frameRate = 30;
                    if (File.Exists(resolved))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var info = await VideoConverter.GetMp4InfoAsync(resolved);
                                if (info != null && info.FrameRate > 1)
                                    frameRate = info.FrameRate;
                            }
                            catch { }
                            Dispatcher.Invoke(() => StartVideoPlayback(frameRate));
                        });
                    }
                    else
                    {
                        StartVideoPlayback(frameRate);
                    }
                }
                else
                {
                    // 没有缓存或者缓存为空 -> 放一个占位文本，后台异步读取视频
                    var placeholder = new TextBlock
                    {
                        Text = File.Exists(resolved) ? "Loading Video..." : "VIDEO (missing)",
                        FontSize = 20,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.SemiBold,
                        Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                        Padding = new Thickness(10)
                    };
                    placeholder.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
                    host = CreateBaseBorder(placeholder);

                    if (File.Exists(resolved))
                    {
                        // 后台解码帧
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var frames = new List<VideoFrameData>();
                                await foreach (var f in VideoConverter.ExtractMp4FramesToJpegRealTimeAsync(
                                    resolved,
                                    quality: 85,
                                    CancellationToken.None))
                                {
                                    frames.Add(f);
                                }

                                if (frames.Count > 0)
                                {
                                    await Dispatcher.InvokeAsync(() =>
                                    {
                                        if (!DesignCanvas.Children.Contains(host))
                                            return; // 已被清理

                                        _currentVideoFrames = frames;
                                        _currentFrameIndex = 0;

                                        var img = new Image { Stretch = Stretch.Uniform };
                                        UpdateVideoFrame(img, frames[0]);
                                        _currentVideoImage = img;
                                        _currentVideoBorder = host;
                                        host.Child = img;

                                        double fr = 30;
                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                var info = await VideoConverter.GetMp4InfoAsync(resolved);
                                                if (info != null && info.FrameRate > 1) fr = info.FrameRate;
                                            }
                                            catch { }
                                            Dispatcher.Invoke(() => StartVideoPlayback(fr));
                                        });
                                    });
                                }
                                else
                                {
                                    await Dispatcher.InvokeAsync(() =>
                                    {
                                        if (host.Child is TextBlock tb)
                                            tb.Text = "VIDEO (decode failed)";
                                    });
                                }
                            }
                            catch
                            {
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    if (host.Child is TextBlock tb)
                                        tb.Text = "VIDEO (error)";
                                });
                            }
                        });
                    }
                }

                // 设置公共属性（Transform / ZIndex / Opacity）
                host.Opacity = ec.Opacity <= 0 ? 1 : ec.Opacity;

                var tg = new TransformGroup();
                var scale = new ScaleTransform(ec.Scale <= 0 ? 1 : ec.Scale, ec.Scale <= 0 ? 1 : ec.Scale);
                tg.Children.Add(scale);
                tg.Children.Add(new TranslateTransform(ec.X, ec.Y));
                host.RenderTransform = tg;
                Canvas.SetZIndex(host, ec.ZIndex);

                // 给 Tag 标记视频信息（与保存逻辑匹配）
                host.Tag = new VideoElementInfo
                {
                    Kind = LiveInfoKind.VideoPlayback,
                    FilePath = resolved,
                    TotalFrames = _currentVideoFrames?.Count ?? 0,
                    VideoInfo = null
                };

                DesignCanvas.Children.Add(host);
            }
            catch (Exception ex)
            {
                Logger.Info($"Restore video element failed: {ex.Message}");
            }
        }


        // ================= Load Config =================
        private async void LoadConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var configFolder = Path.Combine(OutputFolder, "Configs");
                Directory.CreateDirectory(configFolder);

                if (!ConfigSelectionDialog.TrySelectConfig(
                        Application.Current.MainWindow ?? Window.GetWindow(this),
                        configFolder,
                        out var config,
                        out var selectedPath,
                        showPathInList: false))
                {
                    LocalizedMessageBox.Show("NoValidConfigFilesFound", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Clear old
                DesignCanvas.Children.Clear();
                _liveItems.Clear();
                _movingDirections.Clear();
                SetSelected(null);
                StopVideoPlayback();
                ResetMoveDirection();

                // Basic props
                CurrentConfigName = config.ConfigName;
                CanvasSize = config.CanvasSize;
                BackgroundHex = config.BackgroundColor;
                BackgroundImagePath = !string.IsNullOrEmpty(config.BackgroundImagePath)
                    ? ResolveRelativePath(config.BackgroundImagePath)
                    : null;
                BackgroundImageOpacity = config.BackgroundImageOpacity;
                if (config.MoveSpeed > 0) MoveSpeed = config.MoveSpeed;

                // Restore each element
                foreach (var ec in config.Elements.OrderBy(e2 => e2.ZIndex))
                {
                    try
                    {
                        switch (ec.Type)
                        {
                            case "Text":
                                RestoreTextElement(ec);
                                break;
                            case "LiveText":
                                RestoreLiveElement(ec);
                                break;
                            case "Image":
                                RestoreImageElement(ec);
                                break;
                            case "Video":
                                RestoreVideoElement(ec);
                                break;
                        }
                    }
                    catch (Exception exInner)
                    {
                        Logger.Info($"Restore element failed: {exInner.Message}");
                    }
                }

                UpdateAutoMoveTimer();
                LoadedFromConfigFile = true;
                _loadedConfigFilePath = selectedPath;

                LocalizedMessageBox.Show(
                    string.Format(Application.Current.FindResource("ConfigurationLoaded")?.ToString()
                                  ?? "配置已加载：{0}", config.ConfigName),
                    "LoadSuccessful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    true);
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(
                    string.Format(Application.Current.FindResource("LoadConfigurationFailed")?.ToString()
                                  ?? "加载配置失败：{0}", ex.Message),
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error,
                    true);
            }
        }
        // ================= Apply Text Color / Gradient =================
        private void ApplyTextColorOrGradient()
        {
            var tb = GetCurrentTextBlock();
            if (tb == null) return;

            // 如果是 systeminfo 文本模式，使用 UsageStartColor/UsageEndColor
            if (IsUsageTextModeSelected)
            {
                if (UseTextGradient)
                {
                    var brush = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1)
                    };
                    brush.GradientStops.Add(new GradientStop(UsageStartColor, 0));
                    brush.GradientStops.Add(new GradientStop(UsageEndColor, 1));
                    tb.Foreground = brush;
                }
                else
                {
                    tb.Foreground = new SolidColorBrush(UsageStartColor);
                }

                // 更新 LiveTextItem 的颜色（用于保存配置）
                var item = _liveItems.FirstOrDefault(i => i.Border == _selected);
                if (item != null)
                {
                    item.StartColor = UsageStartColor;
                    item.EndColor = UsageEndColor;
                }
                return;
            }

            // 普通文本的颜色逻辑保持不变
            if (IsUsageSelected) return;

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
            double cpuTemp = _metrics.GetCpuTemperature();
            double gpuTemp = _metrics.GetGpuTemperature();
            DateTime now = DateTime.Now;

            foreach (var item in _liveItems.ToArray())
            {
                double rawVal = 0;
                string text = item.Kind switch
                {
                    LiveInfoKind.CpuUsage => (rawVal = cpu, $"CPU {Math.Round(cpu)}%").Item2,
                    LiveInfoKind.GpuUsage => (rawVal = gpu, $"GPU {Math.Round(gpu)}%").Item2,
                    LiveInfoKind.CpuTemperature => (rawVal = cpuTemp, $"CPU {Math.Round(cpuTemp)}°C").Item2,   // Temperature 0–100 映射同百分比
                    LiveInfoKind.GpuTemperature => (rawVal = gpuTemp, $"GPU {Math.Round(gpuTemp)}°C").Item2,
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
                                // 温度也按 0~100 直接映射宽度
                                double percent = Math.Clamp(rawVal, 0, 100);
                                double totalWidth = 140;
                                item.BarFill.Width = totalWidth * (percent / 100.0);

                                bool isTemp = item.Kind == LiveInfoKind.CpuTemperature || item.Kind == LiveInfoKind.GpuTemperature;
                                string prefix = (item.Kind == LiveInfoKind.CpuUsage || item.Kind == LiveInfoKind.CpuTemperature) ? "CPU"
                                               : (item.Kind == LiveInfoKind.GpuUsage || item.Kind == LiveInfoKind.GpuTemperature) ? "GPU" : "";

                                string valuePart = isTemp
                                    ? $"{Math.Round(percent)}°C"
                                    : $"{Math.Round(percent)}%";

                                item.Text.Text = string.IsNullOrEmpty(prefix)
                                    ? valuePart
                                    : $"{prefix} {valuePart}";

                                double t = percent / 100.0;
                                var col = LerpColor(item.StartColor, item.EndColor, t);
                                item.Text.Foreground = new SolidColorBrush(col);
                            }
                            break;
                        }

                    case UsageDisplayStyle.Gauge:
                        {
                            double percent = Math.Clamp(rawVal, 0, 100);
                            if (item.GaugeNeedleRotate != null)
                            {
                                double targetAngle = GaugeRotationFromPercent(percent);
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

                            bool isTemp = item.Kind == LiveInfoKind.CpuTemperature || item.Kind == LiveInfoKind.GpuTemperature;
                            item.Text.Text = isTemp
                                ? $"{Math.Round(percent)}°C"
                                : $"{Math.Round(percent)}%";
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
                                    (item.Kind == LiveInfoKind.CpuUsage ||
                                     item.Kind == LiveInfoKind.GpuUsage ||
                                     item.Kind == LiveInfoKind.CpuTemperature ||          // NEW
                                     item.Kind == LiveInfoKind.GpuTemperature))           // NEW
                                {
                                    double label2Radius = labelRadius - 10;
                                    double lx2 = GaugeCenterX + label2Radius * Math.Cos(rad);
                                    double ly2 = GaugeCenterY + label2Radius * Math.Sin(rad);

                                    var kindLabel = new TextBlock
                                    {
                                        Text = (item.Kind == LiveInfoKind.CpuUsage || item.Kind == LiveInfoKind.CpuTemperature) ? "CPU" : "GPU",
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
            _captureRoot ??= (FrameworkElement?)this.FindName("DesignRoot")
                            ??(FrameworkElement?)this.FindName("DesignCanvas")
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
                    LocalizedMessageBox.Show("CurrentCanvasNotReady", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
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

                LocalizedMessageBox.Show(string.Format(Application.Current.FindResource("ScreenshotSaved")?.ToString() ?? "截图已保存:\n{0}", fullPath), "Success", MessageBoxButton.OK, MessageBoxImage.Information, true);
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(string.Format(Application.Current.FindResource("ScreenshotFailed")?.ToString() ?? "截图失败: {0}", ex.Message), "Error", MessageBoxButton.OK, MessageBoxImage.Error, true);
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