using CMDevicesManager.Models;
using CMDevicesManager.Services;
using System.Windows.Media.Animation;
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
using WF = System.Windows.Forms;
using CMDevicesManager.Utilities;
using MessageBox = System.Windows.MessageBox;
using CMDevicesManager.Helper;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application = System.Windows.Application;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using static CMDevicesManager.Pages.DeviceConfigPage;
using ListBox = System.Windows.Controls.ListBox;
using System.Threading;
using System.Threading.Tasks;

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

        // Live
        public LiveInfoKind? LiveKind { get; set; }

        // Video
        public string? VideoPath { get; set; }

        // DateTime format
        public string? DateFormat { get; set; }
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
        private readonly DeviceInfos _device;
        private readonly ISystemMetricsService _metrics;
        private readonly DispatcherTimer _liveTimer;

        public ObservableCollection<SystemInfoItem> SystemInfoItems { get; } = new();

        private sealed class LiveTextItem
        {
            public Border Border { get; init; } = null!;
            public TextBlock Text { get; init; } = null!;
            public LiveInfoKind Kind { get; init; }
            public string DateFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
        }
        private readonly List<LiveTextItem> _liveItems = new();

        public enum LiveInfoKind
        {
            CpuUsage,
            GpuUsage,
            DateTime,
            VideoPlayback
        }

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
        public bool IsTextSelected => _selected?.Child is TextBlock;
        public bool IsSelectedTextReadOnly => _selected?.Tag is LiveInfoKind;

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
                if (_selected?.Child is TextBlock tb && _selected.Tag is not LiveInfoKind)
                    tb.Text = value;
                UpdateSelectedInfo();
                OnPropertyChanged();
            }
        }
        private double _selectedFontSize = 24;
        public double SelectedFontSize { get => _selectedFontSize; set { _selectedFontSize = value; if (_selected?.Child is TextBlock tb) tb.FontSize = value; OnPropertyChanged(); } }
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

        // Auto-move (multi-element)
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

        // 多元素方向存储
        private readonly Dictionary<Border, (double dirX, double dirY)> _movingDirections = new();

        // Image selection property
        public bool IsImageSelected => _selected?.Child is Image && _selected.Tag is not VideoElementInfo;

        // Date format collection
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

        public bool IsDateTimeSelected => _selected?.Tag is LiveInfoKind kind && kind == LiveInfoKind.DateTime;

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
            }
        }

        private string ResourcesFolder => Path.Combine(OutputFolder, "Resources");

        public DeviceConfigPage(DeviceInfos device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            InitializeComponent();
            DataContext = this;

            _selectedInfo = Application.Current.FindResource("None")?.ToString() ?? "None";
            _metrics = new RealSystemMetricsService();
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

            Unloaded += DeviceConfigPage_Unloaded;
        }

        // ================= System Info Buttons =================
        private void BuildSystemInfoButtons()
        {
            SystemInfoItems.Clear();
            SystemInfoItems.Add(new SystemInfoItem(LiveInfoKind.CpuUsage, $"CPU Usage ({_metrics.CpuName})"));
            var gpuName = (_metrics as RealSystemMetricsService)?.PrimaryGpuName ?? "GPU";
            var gpuVal = _metrics.GetGpuUsagePercent();
            if (!string.Equals(gpuName, "GPU", StringComparison.OrdinalIgnoreCase) || gpuVal > 0)
                SystemInfoItems.Add(new SystemInfoItem(LiveInfoKind.GpuUsage, $"GPU Usage ({gpuName})"));
        }
        public sealed record SystemInfoItem(LiveInfoKind Kind, string DisplayName);

        // ================= Navigation =================
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
            else NavigationService?.Navigate(new DevicePage());
        }

        // ================= Live Text + Date =================
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
            _liveItems.Add(new LiveTextItem { Border = border, Text = textBlock, Kind = kind });

            if (_selected == border)
            {
                OnPropertyChanged(nameof(IsSelectedTextReadOnly));
                _selectedText = textBlock.Text;
            }
        }

        private void LiveTimer_Tick(object? sender, EventArgs e)
        {
            double cpu = _metrics.GetCpuUsagePercent();
            double gpu = _metrics.GetGpuUsagePercent();
            DateTime now = DateTime.Now;

            foreach (var item in _liveItems.ToArray())
            {
                string text = item.Kind switch
                {
                    LiveInfoKind.CpuUsage => $"CPU {Math.Round(cpu)}%",
                    LiveInfoKind.GpuUsage => $"GPU {Math.Round(gpu)}%",
                    LiveInfoKind.DateTime => now.ToString(item.DateFormat ?? "yyyy-MM-dd HH:mm:ss"),
                    _ => item.Text.Text
                };

                item.Text.Text = text;

                if (_selected == item.Border)
                {
                    _selectedText = text;
                    OnPropertyChanged(nameof(SelectedText));
                    UpdateSelectedInfo();
                }
            }
        }

        // ================= Add Elements =================
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
            if (_selected == border)
            {
                ResetMoveDirection();
            }
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
                AddElement(img, Path.GetFileName(copiedPath));
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
        }

        // ================= Color Pickers =================
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
                var copiedPath = CopyResourceToAppFolder(dlg.FileName, "Backgrounds");
                BackgroundImagePath = copiedPath;
            }
        }

        private void ClearBackgroundImage_Click(object sender, RoutedEventArgs e) => BackgroundImagePath = null;

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
                SelectedTextColor = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
        }

        private void PickSelectedTextColor2_Click(object sender, RoutedEventArgs e)
        {
            if (_selected?.Child is not TextBlock) return;
            using var dlg = new WF.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(SelectedTextColor2.A, SelectedTextColor2.R, SelectedTextColor2.G, SelectedTextColor2.B)
            };
            if (dlg.ShowDialog() == WF.DialogResult.OK)
                SelectedTextColor2 = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
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

            if (_selected.Child is TextBlock tb)
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

            // 恢复该元素的方向到摇杆
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
            else if (_selected.Child is TextBlock)
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

        // ================= Video Playback =================
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
                    CurrentConfigName = dialog.ConfigName;
                }

                var config = new CanvasConfiguration
                {
                    ConfigName = CurrentConfigName,
                    CanvasSize = CanvasSize,
                    BackgroundColor = BackgroundHex,
                    BackgroundImagePath = BackgroundImagePath != null ? GetRelativePath(BackgroundImagePath) : null,
                    BackgroundImageOpacity = BackgroundImageOpacity
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

                        if (border.Child is TextBlock tb)
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
                            else if (tb.Foreground is SolidColorBrush brush)
                            {
                                elemConfig.UseTextGradient = false;
                                var c = brush.Color;
                                elemConfig.TextColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                            }

                            if (border.Tag is LiveInfoKind liveKind)
                            {
                                elemConfig.Type = "LiveText";
                                elemConfig.LiveKind = liveKind;

                                if (liveKind == LiveInfoKind.DateTime)
                                {
                                    var liveItem = _liveItems.FirstOrDefault(i => i.Border == border);
                                    if (liveItem != null)
                                        elemConfig.DateFormat = liveItem.DateFormat;
                                }
                            }
                        }
                        else if (border.Child is Image img)
                        {
                            if (border.Tag is VideoElementInfo videoInfo)
                            {
                                elemConfig.Type = "Video";
                                elemConfig.VideoPath = GetRelativePath(videoInfo.FilePath);
                            }
                            else if (img.Source is BitmapImage bitmapImg)
                            {
                                var imagePath = bitmapImg.UriSource?.LocalPath ?? bitmapImg.UriSource?.ToString();
                                if (!string.IsNullOrEmpty(imagePath))
                                {
                                    elemConfig.Type = "Image";
                                    elemConfig.ImagePath = GetRelativePath(imagePath);
                                }
                            }
                        }

                        config.Elements.Add(elemConfig);
                    }
                }

                var configFolder = Path.Combine(OutputFolder, "Configs");
                Directory.CreateDirectory(configFolder);
                var fileName = $"config_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var filePath = Path.Combine(configFolder, fileName);

                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                });
                File.WriteAllText(filePath, json);

                var msg = Application.Current.FindResource("ConfigSaved")?.ToString() ?? "Configuration saved";
                var title = Application.Current.FindResource("SaveSuccessful")?.ToString() ?? "Save Successful";
                MessageBox.Show($"{msg}: {CurrentConfigName}", title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var errorMsg = Application.Current.FindResource("Error")?.ToString() ?? "Error";
                MessageBox.Show($"保存配置失败: {ex.Message}", errorMsg, MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                    var noConfigMsg = Application.Current.FindResource("NoConfigFilesFound")?.ToString() ?? "No configuration files found";
                    var noticeMsg = Application.Current.FindResource("Notice")?.ToString() ?? "Notice";
                    MessageBox.Show(noConfigMsg, noticeMsg, MessageBoxButton.OK, MessageBoxImage.Information);
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
                    var noValidConfigMsg = Application.Current.FindResource("NoValidConfigFilesFound")?.ToString() ?? "No valid configuration files found";
                    var noticeMsg = Application.Current.FindResource("Notice")?.ToString() ?? "Notice";
                    MessageBox.Show(noValidConfigMsg, noticeMsg, MessageBoxButton.OK, MessageBoxImage.Information);
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
                                        FontSize = elemConfig.FontSize ?? 20,
                                        FontWeight = FontWeights.SemiBold
                                    };
                                    liveTb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

                                    if (elemConfig.UseTextGradient == true &&
                                        !string.IsNullOrEmpty(elemConfig.TextColor) &&
                                        !string.IsNullOrEmpty(elemConfig.TextColor2) &&
                                        TryParseHexColor(elemConfig.TextColor, out var lc1) &&
                                        TryParseHexColor(elemConfig.TextColor2, out var lc2))
                                    {
                                        var gradientBrush = new LinearGradientBrush
                                        {
                                            StartPoint = new Point(0, 0),
                                            EndPoint = new Point(1, 1)
                                        };
                                        gradientBrush.GradientStops.Add(new GradientStop(lc1, 0.0));
                                        gradientBrush.GradientStops.Add(new GradientStop(lc2, 1.0));
                                        liveTb.Foreground = gradientBrush;
                                    }
                                    else if (!string.IsNullOrEmpty(elemConfig.TextColor) &&
                                             TryParseHexColor(elemConfig.TextColor, out var liveTextColor))
                                    {
                                        liveTb.Foreground = new SolidColorBrush(liveTextColor);
                                    }
                                    else
                                    {
                                        liveTb.Foreground = new SolidColorBrush(Colors.Black);
                                    }

                                    if (elemConfig.LiveKind.Value == LiveInfoKind.DateTime)
                                    {
                                        string fmt = string.IsNullOrWhiteSpace(elemConfig.DateFormat)
                                            ? "yyyy-MM-dd HH:mm:ss"
                                            : elemConfig.DateFormat;
                                        liveTb.Text = DateTime.Now.ToString(fmt);
                                        element = liveTb;
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
                                        element = liveTb;
                                    }
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
                                    else
                                    {
                                        Logger.Info($"Image file not found: {resolvedPath}");
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
                            var dateFmt = elemConfig.LiveKind.Value == LiveInfoKind.DateTime
                                ? (elemConfig.DateFormat ?? "yyyy-MM-dd HH:mm:ss")
                                : "yyyy-MM-dd HH:mm:ss";
                            _liveItems.Add(new LiveTextItem
                            {
                                Border = border,
                                Text = (TextBlock)element,
                                Kind = elemConfig.LiveKind.Value,
                                DateFormat = dateFmt
                            });
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
                    }
                }

                var loadedMsg = Application.Current.FindResource("ConfigLoaded")?.ToString() ?? "Configuration loaded";
                var okTitle = Application.Current.FindResource("LoadSuccessful")?.ToString() ?? "Load Successful";
                MessageBox.Show($"{loadedMsg}: {config.ConfigName}", okTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var failedMsg = Application.Current.FindResource("LoadConfigFailed")?.ToString() ?? "Failed to load configuration";
                var errorMsg = Application.Current.FindResource("Error")?.ToString() ?? "Error";
                MessageBox.Show($"{failedMsg}: {ex.Message}", errorMsg, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================= Apply Text Color / Gradient =================
        private void ApplyTextColorOrGradient()
        {
            if (_selected?.Child is not TextBlock tb) return;

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

        // ================= Auto Move (multi-element) =================
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
                if (border.Child is not TextBlock) continue;
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

            if (_selected?.Child is TextBlock)
            {
                if (Math.Abs(newDirX) < 0.0001 && Math.Abs(newDirY) < 0.0001)
                    _movingDirections[_selected] = (0, 0);
                else
                    _movingDirections[_selected] = (newDirX, newDirY);
            }

            UpdateAutoMoveTimer();
        }

        // Helper: retrieve (or create) ScaleTransform & TranslateTransform
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

        // ================= Unloaded =================
        private void DeviceConfigPage_Unloaded(object sender, RoutedEventArgs e)
        {
            try { _liveTimer.Stop(); } catch { }
            try { _metrics.Dispose(); } catch { }
            try { _autoMoveTimer.Stop(); } catch { }
            StopVideoPlayback();
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
        private void RotateImageRight_Click(object sender, RoutedEventArgs e) => RotateSelectedImage(90);
        private void MirrorImageHorizontal_Click(object sender, RoutedEventArgs e) => MirrorSelectedImageHorizontal();
    }
}