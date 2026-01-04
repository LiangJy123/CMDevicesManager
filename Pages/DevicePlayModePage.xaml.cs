// NEW: bring in config models + enum alias
using CMDevicesManager.Helper;
using CMDevicesManager.Language;
using CMDevicesManager.Models;
using CMDevicesManager.Pages; // contains CanvasConfiguration / ElementConfiguration
using CMDevicesManager.Services;
using CMDevicesManager.Utilities;
using CMDevicesManager.Windows;
using HID.DisplayController;
using HidApi;
//using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Image = System.Windows.Controls.Image;
using LiveInfoKindAlias = CMDevicesManager.Pages.DeviceConfigPage.LiveInfoKind;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;
using Timer = System.Timers.Timer;

namespace CMDevicesManager.Pages
{
    public partial class DevicePlayModePage : Page
    {
        public static event Action<CanvasConfiguration>? GlobalConfigRendered;
        private CanvasConfiguration? _lastAppliedConfig;
        private readonly string _globalSequencePath;
        private readonly object _globalSequenceLock = new();
        private System.Windows.Threading.DispatcherTimer? _videoTimer;
        private List<VideoFrameData>? _currentVideoFrames;
        private int _videoFrameIndex;
        private Image? _videoImage;


        private DeviceInfo? _deviceInfo;
        private HidDeviceService? _hidDeviceService;
        private bool _isHidServiceInitialized = false;
        // Video thumbnail cache to avoid regenerating thumbnails
        private Dictionary<string, BitmapImage> _videoThumbnailCache = new();

        private PlaybackMode _currentPlayMode = PlaybackMode.RealtimeConfig;
        private string LR(string key, string? fallback = null) =>
    Application.Current.TryFindResource(key) as string ?? fallback ?? key;
        private sealed class GlobalConfigSequence
        {
            public List<GlobalConfigEntry> Items { get; set; } = new();
        }
        private sealed class GlobalConfigEntry
        {
            public string FilePath { get; set; } = "";
            public int DurationSeconds { get; set; }
        }

        private sealed class UsageVisualItem
        {
            public Border HostBorder = null!;
            public LiveInfoKindAlias Kind;
            public TextBlock Text = null!;

            // ProgressBar
            public Rectangle? BarFill;
            public Border? BarBackground;
            public double BarTotalWidth;

            // Gauge
            public Line? GaugeNeedle;
            public RotateTransform? GaugeNeedleRotate;
            public List<Line> GaugeTicks = new();
            public List<TextBlock> GaugeLabels = new();
            public TextBlock? GaugeKindLabel;

            // Theme
            public Color StartColor;
            public Color EndColor;
            public Color NeedleColor;
            public Color BarBackgroundColor;

            public string DisplayStyle = "Text"; // Text / ProgressBar / Gauge
            public string? DateFormat;
        }

        // Gauge 常量（与配置页保持）
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
        private const double GaugeNeedleAngleOffset = 270; // 与配置页一致

        private static double GaugeAngleFromPercent(double percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            return GaugeStartAngle + GaugeSweep * (percent / 100.0);
        }
        private static double GaugeRotationFromPercent(double percent)
        {
            return GaugeAngleFromPercent(percent) - GaugeNeedleAngleOffset; // -120 ~ +120
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

        private readonly List<UsageVisualItem> _usageVisualItems = new(); // 若已存在旧列表，替换为此版本
        // 在类字段区域新增
        private string? _selectedVideoPath;
        private bool _isVideoStreaming;
        private readonly System.Windows.Threading.DispatcherTimer _autoMoveTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        private readonly Dictionary<Border, (double dx, double dy)> _movingDirections = new();
        private DateTime _lastMoveTick = DateTime.Now;
        private double _moveSpeed = 100;          // 来自配置的 MoveSpeed
        private int _currentCanvasSize = 512;     // 来自配置的 CanvasSize
        // Config 序列模型
        public class PlayConfigItem : INotifyPropertyChanged
        {
            private int _durationSeconds;
            private bool _isDurationEditable;
            public string DisplayName { get; set; } = "";
            public string FilePath { get; set; } = "";

            public int DurationSeconds
            {
                get => _durationSeconds;
                set
                {
                    if (value != _durationSeconds)
                    {
                        _durationSeconds = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationSeconds)));
                    }
                }
            }

            public bool IsDurationEditable
            {
                get => _isDurationEditable;
                set
                {
                    if (value != _isDurationEditable)
                    {
                        _isDurationEditable = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDurationEditable)));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        public ObservableCollection<PlayConfigItem> ConfigSequence { get; } = new();
        public bool IsMultiConfig => ConfigSequence.Count > 1;

        private CancellationTokenSource? _configSequenceCts;
        private bool _isConfigSequenceRunning = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private int _streamedImageCount;

        private int _currentRotation = 0;
        private int _currentBrightness = 80;
        private bool _isBrightnessSliderUpdating;

        // NEW: metrics + timer for live elements
        private readonly ISystemMetricsService _metrics = RealSystemMetricsService.Instance;
        private readonly System.Windows.Threading.DispatcherTimer _liveUpdateTimer;
        private readonly List<(TextBlock Text, LiveInfoKindAlias Kind, string? DateFormat)> _liveDynamicItems = new();

        // Base folder (same logic as DeviceConfigPage)
        private readonly string _outputFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CMDevicesManager");


        #region Properties

        public DeviceInfo? DeviceInfo
        {
            get => _deviceInfo;
            set
            {
                if (_deviceInfo != value)
                {
                    _deviceInfo = value;
                    // Initialize HID service when device info is set
                    _ = InitializeHidDeviceServiceAsync();
                    // Load playback mode from offline data service
                    LoadPlaybackModeFromService();
                    GetCurrentBrightnessToService();
                }
            }
        }

        /// <summary>
        /// Gets whether the HID device service is initialized and ready
        /// </summary>
        public bool IsHidServiceReady => _isHidServiceInitialized && _hidDeviceService?.IsInitialized == true;

        /// <summary>
        /// Gets the current HID device service instance
        /// </summary>
        public HidDeviceService? HidDeviceService => _hidDeviceService;

        #endregion


        private void AttachConfigItemEvents(PlayConfigItem item)
        {
            item.PropertyChanged += OnConfigItemPropertyChanged;
        }

        private void DetachConfigItemEvents(PlayConfigItem item)
        {
            item.PropertyChanged -= OnConfigItemPropertyChanged;
        }

        private void OnConfigItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayConfigItem.DurationSeconds))
            {
                SaveGlobalSequence(); // Duration 改变即保存
            }
        }


        private void SaveGlobalSequence()
        {
            try
            {
                lock (_globalSequenceLock)
                {
                    var data = new GlobalConfigSequence
                    {
                        Items = ConfigSequence.Select(c => new GlobalConfigEntry
                        {
                            FilePath = c.FilePath,
                            DurationSeconds = c.DurationSeconds
                        }).ToList()
                    };
                    Directory.CreateDirectory(_outputFolder);
                    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(_globalSequencePath, json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PlayMode] Save global sequence failed: {ex.Message}");
            }
        }

        private void LiveUpdateTimer_Tick(object? sender, EventArgs e)
        {
            double cpu = _metrics.GetCpuUsagePercent();
            double gpu = _metrics.GetGpuUsagePercent();
            double cpuTemp = _metrics.GetCpuTemperature();
            double gpuTemp = _metrics.GetGpuTemperature();
            DateTime now = DateTime.Now;

            // 旧的纯文本集合（如果未来仍使用）保持原逻辑：这里只处理最初收集到的条目
            foreach (var (text, kind, fmt) in _liveDynamicItems)
            {
                switch (kind)
                {
                    case LiveInfoKindAlias.CpuUsage:
                        text.Text = $"CPU {Math.Round(cpu)}%";
                        break;
                    case LiveInfoKindAlias.GpuUsage:
                        text.Text = $"GPU {Math.Round(gpu)}%";
                        break;
                    case LiveInfoKindAlias.CpuTemperature:
                        text.Text = $"CPU {Math.Round(cpuTemp)}°C";
                        break;
                    case LiveInfoKindAlias.GpuTemperature:
                        text.Text = $"GPU {Math.Round(gpuTemp)}°C";
                        break;
                    case LiveInfoKindAlias.DateTime:
                        text.Text = now.ToString(string.IsNullOrWhiteSpace(fmt) ? "yyyy-MM-dd HH:mm:ss" : fmt);
                        break;
                }
            }

            foreach (var item in _usageVisualItems)
            {
                // 获取当前原始值
                double raw = item.Kind switch
                {
                    LiveInfoKindAlias.CpuUsage => cpu,
                    LiveInfoKindAlias.GpuUsage => gpu,
                    LiveInfoKindAlias.CpuTemperature => cpuTemp,
                    LiveInfoKindAlias.GpuTemperature => gpuTemp,
                    _ => 0
                };

                bool isTemp = item.Kind == LiveInfoKindAlias.CpuTemperature || item.Kind == LiveInfoKindAlias.GpuTemperature;

                switch (item.DisplayStyle)
                {
                    case "ProgressBar":
                        {
                            if (item.BarFill != null)
                            {
                                double percent = Math.Clamp(raw, 0, 100);
                                item.BarFill.Width = item.BarTotalWidth * (percent / 100.0);

                                string prefix = (item.Kind == LiveInfoKindAlias.CpuUsage || item.Kind == LiveInfoKindAlias.CpuTemperature) ? "CPU"
                                               : (item.Kind == LiveInfoKindAlias.GpuUsage || item.Kind == LiveInfoKindAlias.GpuTemperature) ? "GPU" : "";

                                string valuePart = isTemp
                                    ? $"{Math.Round(percent)}°C"
                                    : $"{Math.Round(percent)}%";

                                item.Text.Text = string.IsNullOrEmpty(prefix) ? valuePart : $"{prefix} {valuePart}";

                                double t = percent / 100.0;
                                var col = LerpColor(item.StartColor, item.EndColor, t);
                                item.Text.Foreground = new SolidColorBrush(col);
                            }
                            break;
                        }
                    case "Gauge":
                        {
                            double percent = Math.Clamp(raw, 0, 100);
                            if (item.GaugeNeedleRotate != null)
                            {
                                item.GaugeNeedleRotate.Angle = GaugeRotationFromPercent(percent);
                            }
                            item.Text.Text = isTemp
                                ? $"{Math.Round(percent)}°C"
                                : $"{Math.Round(percent)}%";
                            break;
                        }
                    case "Text":
                        {
                            if (item.Kind == LiveInfoKindAlias.DateTime)
                            {
                                item.Text.Text = now.ToString(string.IsNullOrWhiteSpace(item.DateFormat)
                                    ? "yyyy-MM-dd HH:mm:ss"
                                    : item.DateFormat);
                            }
                            else if (item.Kind == LiveInfoKindAlias.CpuUsage)
                            {
                                item.Text.Text = $"CPU {Math.Round(cpu)}%";
                            }
                            else if (item.Kind == LiveInfoKindAlias.GpuUsage)
                            {
                                item.Text.Text = $"GPU {Math.Round(gpu)}%";
                            }
                            else if (item.Kind == LiveInfoKindAlias.CpuTemperature)
                            {
                                item.Text.Text = $"CPU {Math.Round(cpuTemp)}°C";
                            }
                            else if (item.Kind == LiveInfoKindAlias.GpuTemperature)
                            {
                                item.Text.Text = $"GPU {Math.Round(gpuTemp)}°C";
                            }
                            break;
                        }
                }
            }
        }

   
        private void UpdateLivePreviewAvailability()
        {
            bool suspendMode = _currentPlayMode == PlaybackMode.OfflineVideo;
            if (LivePreviewContainer != null)
            {
                LivePreviewContainer.IsEnabled = !suspendMode;
                LivePreviewContainer.Opacity = suspendMode ? 0.45 : 1.0;
            }
        }

        private void RealtimeStreamingEnabled()
        {
            if (_currentPlayMode == PlaybackMode.RealtimeConfig) return;

            if (_isVideoStreaming)
            {
                _isVideoStreaming = false;
            }

            if (!_isConfigSequenceRunning && ConfigSequence.Count > 0)
                StartConfigSequence();
        }
        private void SuspendModeEnabled()
        {
            if (_currentPlayMode == PlaybackMode.OfflineVideo) return;

            if (_isConfigSequenceRunning)
            {
                _configSequenceCts?.Cancel();
                _isConfigSequenceRunning = false;
            }

            //if (!_isVideoStreaming && !string.IsNullOrEmpty(_selectedVideoPath))
            //    _ = StartVideoPlaybackInternalAsync();
        }

     


        // 在 UpdateButtonStates 内（如果保留）增加模式适配（可选）


        // 如果未调用 InitConfigSequence，请确保添加
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCurrentDeviceDisplaySettingsAsync();
            UpdatePlayModeUI();
            await RefreshSuspendMediaDisplay();


            // ✅ 初始化 ScreenToggle 状态
            InitializeScreenToggleState();
        }
        /// <summary>
        /// Initialize the ScreenToggle state based on saved brightness from offline data service
        /// </summary>
    
        private void InitializeScreenToggleState()
        {
            if (_deviceInfo?.SerialNumber == null || !ServiceLocator.IsOfflineMediaServiceInitialized)
                return;

            try
            {
                var offlineDataService = ServiceLocator.OfflineMediaDataService;
                var settings = offlineDataService.GetDeviceSettings(_deviceInfo.SerialNumber);
                var savedBrightness = settings.Brightness;

                // ✅ Use IsScreenOn from settings
                bool isScreenOn = settings.IsScreenOn;

                if (ScreenToggle != null)
                {
                    // 暂时移除事件处理程序，避免触发 Click 事件
                    ScreenToggle.Click -= ScreenToggle_Click;
                    // Unchecked = On, Checked = Off
                    ScreenToggle.IsChecked = !isScreenOn;
                    ScreenToggle.Click += ScreenToggle_Click;

                    UpdateDisplaySettingsState(isScreenOn);

                    Logger.Info($"Initialized ScreenToggle state: {(isScreenOn ? "ON (unchecked)" : "OFF (checked)")}, Saved brightness: {savedBrightness}%");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to initialize ScreenToggle state for device {_deviceInfo?.SerialNumber}: {ex.Message}");
            }
        }

        private void UpdateDisplaySettingsState(bool isScreenOn)
        {
            if (BrightnessSlider != null) BrightnessSlider.IsEnabled = isScreenOn;
            SetRotationButtonsEnabled(isScreenOn);
        }

        private void InitializeDisplayControls()
        {
            BrightnessSlider.Value = _currentBrightness;
            BrightnessValueText.Text = $"{_currentBrightness}%";
            CurrentRotationText.Text = $"Current: {_currentRotation}°";
            UpdateRotationButtonAppearance(_currentRotation);
        }

        private void UpdateDurationEditableStates()
        {
            bool editable = ConfigSequence.Count > 1;
            foreach (var item in ConfigSequence)
            {
                item.IsDurationEditable = editable;
            }
        }



        // 在构造函数结尾处调用
        // public DevicePlayModePage() { ... InitConfigSequence(); }

        private void AddConfigButton_Click_old(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select a configuration file",
                Filter = "Config Files (*.json;*.cfg)|*.json;*.cfg|All Files|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                string name = Path.GetFileNameWithoutExtension(ofd.FileName);
                var newItem = new PlayConfigItem
                {
                    DisplayName = name,
                    FilePath = ofd.FileName,
                    DurationSeconds = 5
                };
                AttachConfigItemEvents(newItem);
                ConfigSequence.Add(newItem);

                UpdateDurationEditableStates();
                SaveGlobalSequence();

                if (_currentPlayMode == PlaybackMode.RealtimeConfig && !_isConfigSequenceRunning)
                {
                    StartConfigSequence();
                }
            }
        }

        private void AddConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var configFolder = Path.Combine(_outputFolder, "Configs");
                Directory.CreateDirectory(configFolder);

                if (!ConfigSelectionDialog.TrySelectConfig(
                        Application.Current.MainWindow ?? Window.GetWindow(this),
                        configFolder,
                        out var cfg,
                        out var path,
                        showPathInList: false))
                {
                    return;
                }

                string displayName = !string.IsNullOrWhiteSpace(cfg!.ConfigName)
                    ? cfg.ConfigName
                    : System.IO.Path.GetFileNameWithoutExtension(path);

                var newItem = new PlayConfigItem
                {
                    DisplayName = displayName,
                    FilePath = path!,
                    DurationSeconds = 5
                };
                AttachConfigItemEvents(newItem);
                ConfigSequence.Add(newItem);

                UpdateDurationEditableStates();
                SaveGlobalSequence();

                if (_currentPlayMode == PlaybackMode.RealtimeConfig && !_isConfigSequenceRunning)
                    StartConfigSequence();
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(
                    string.Format(Application.Current.FindResource("AddConfigFailed")?.ToString() ?? "添加配置失败：{0}", ex.Message),
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error,
                    true);
            }
        }
        // 提取原 StartSequenceButton_Click 逻辑，供按钮和自动触发共同使用
        private async void StartConfigSequence()
        {
            if (_isConfigSequenceRunning || ConfigSequence.Count == 0) return;

            _isConfigSequenceRunning = true;
            _configSequenceCts = new CancellationTokenSource();

            try
            {
                await RunConfigSequenceAsync(_configSequenceCts.Token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _isConfigSequenceRunning = false;
                _configSequenceCts?.Dispose();
                _configSequenceCts = null;
            }
        }

        private void RemoveConfig_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlayConfigItem item)
            {
                ConfigSequence.Remove(item);

                if (ConfigSequence.Count == 0)
                {
                    ClearCanvasForNoConfigs();
                    return;
                }

                // 列表仍有元素，更新可编辑状态（避免第一项仍锁定）
                UpdateDurationEditableStates();
                SaveGlobalSequence();
            }
        }

        private void ClearConfigsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isConfigSequenceRunning)
            {
                _configSequenceCts?.Cancel();
            }
            ConfigSequence.Clear();
            ClearCanvasForNoConfigs();
            SaveGlobalSequence();
        }

        private void MoveConfigUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlayConfigItem item)
            {
                int idx = ConfigSequence.IndexOf(item);
                if (idx > 0) ConfigSequence.Move(idx, idx - 1);
                UpdateDurationEditableStates();
                SaveGlobalSequence();
            }
        }

        private void MoveConfigDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlayConfigItem item)
            {
                int idx = ConfigSequence.IndexOf(item);
                if (idx >= 0 && idx < ConfigSequence.Count - 1)
                    ConfigSequence.Move(idx, idx + 1);
            }
        }
        private void UpdateEmptyConfigsPlaceholder()
        {
            if (NoConfigsPlaceholder == null) return;
            NoConfigsPlaceholder.Visibility = ConfigSequence.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // In constructor (after InitializeComponent(), after TryLoadGlobalSequence()):
        public DevicePlayModePage()
        {
            InitializeComponent();
            InitializeDisplayControls();
            InitConfigSequence();
            _currentPlayMode = PlaybackMode.RealtimeConfig;
            UpdatePlayModeUI();

            _liveUpdateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _liveUpdateTimer.Tick += LiveUpdateTimer_Tick;
            _liveUpdateTimer.Start();
            _autoMoveTimer.Tick += AutoMoveTimer_Tick;
            _globalSequencePath = Path.Combine(_outputFolder, "globalconfig1.json");
            TryLoadGlobalSequence();
            UpdateDurationEditableStates();
            UpdateEmptyConfigsPlaceholder(); // NEW

            if (ConfigSequence.Count > 0 && !_isConfigSequenceRunning)
            {
                StartConfigSequence();
            }

            LanguageSwitch.LanguageChanged += OnGlobalLanguageChanged;
        }
        // ADD method inside class (e.g. below ApplyPlayModeLocalization):
        private void OnGlobalLanguageChanged(string culture)
        {
            Dispatcher.Invoke(ApplyPlayModeLocalization);
        }


        private void ApplyPlayModeLocalization()
        {
            bool isRealtime = _currentPlayMode == PlaybackMode.RealtimeConfig;
            if (PlayContentTitleText != null)
                PlayContentTitleText.Text = isRealtime ? LR("PlayMode_PlayContentTitle", "Play Content") : "";
            if (PlayContentHintText != null)
                PlayContentHintText.Text = LR(isRealtime ? "PlayMode_RealtimeHint" : "PlayMode_OfflineVideoHint",
                                              PlayContentHintText.Text);
        }
        public DevicePlayModePage(DeviceInfo deviceInfo) : this()
        {
            DeviceInfo = deviceInfo;
            //InitializeDevice();
        }
        // In InitConfigSequence(), inside CollectionChanged handler add:
        private void InitConfigSequence()
        {
            DataContext = this;
            ConfigSequence.CollectionChanged += (_, __) =>
            {
                Raise(nameof(IsMultiConfig));
                if (ConfigSequence.Count == 1)
                {
                    var item = ConfigSequence[0];
                    if (item.DurationSeconds <= 0) item.DurationSeconds = 5;
                }
                UpdateDurationEditableStates();
                UpdateEmptyConfigsPlaceholder(); // NEW
            };
        }

        // After TryLoadGlobalSequence() finishes loading list, ensure:
        private void TryLoadGlobalSequence()
        {
            try
            {
                if (!File.Exists(_globalSequencePath)) return;
                string json = File.ReadAllText(_globalSequencePath);
                var data = JsonSerializer.Deserialize<GlobalConfigSequence>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (data?.Items == null) return;

                ConfigSequence.Clear();
                foreach (var entry in data.Items)
                {
                    if (string.IsNullOrWhiteSpace(entry.FilePath)) continue;
                    var item = new PlayConfigItem
                    {
                        FilePath = entry.FilePath,
                        DisplayName = Path.GetFileNameWithoutExtension(entry.FilePath),
                        DurationSeconds = entry.DurationSeconds > 0 ? entry.DurationSeconds : 5
                    };
                    AttachConfigItemEvents(item);
                    ConfigSequence.Add(item);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PlayMode] Load global sequence failed: {ex.Message}");
            }
            finally
            {
                UpdateEmptyConfigsPlaceholder(); // NEW
            }
        }

        // In ClearCanvasForNoConfigs():
        private void ClearCanvasForNoConfigs()
        {
            StopVideoPlaybackTimer(disposeState: true);
            _configSequenceCts?.Cancel();

            DesignCanvas.Children.Clear();
            _liveDynamicItems.Clear();
            _usageVisualItems.Clear();
            _movingDirections.Clear();
            UpdateAutoMoveTimer();

            var blank = new CanvasConfiguration { CanvasSize = 512 };
            GlobalConfigRendered?.Invoke(blank);
            _lastAppliedConfig = blank;

            CurrentImageName.Text = LR("PlayMode_NoConfig");
            ImageDimensions.Text = "";
            SaveGlobalSequence();
            UpdateEmptyConfigsPlaceholder(); // NEW
        }

        private async void StartSequenceButton_Click(object sender, RoutedEventArgs e)
        {
            StartConfigSequence();
        }

        private void StopSequenceButton_Click(object sender, RoutedEventArgs e)
        {
            _configSequenceCts?.Cancel();
        }

        private async Task RunConfigSequenceAsync(CancellationToken token)
        {
            // 始终循环（直到 StopSequenceButton 取消）
            while (!token.IsCancellationRequested)
            {
                if (ConfigSequence.Count == 0) break;

                // 单一配置：持续显示，不再重复加载（除非被 Stop 或列表改变）
                if (ConfigSequence.Count == 1)
                {
                    var single = ConfigSequence[0];
                    await LoadConfigToCanvasAsync(single.FilePath, token);
                    try
                    {
                        // 挂起等待直到取消或列表变化（简单轮询 1s，避免阻塞 UI）
                        while (!token.IsCancellationRequested && ConfigSequence.Count == 1)
                            await Task.Delay(1000, token);
                    }
                    catch (TaskCanceledException) { }
                    continue; // 重新评估集合（可能加了更多配置）
                }

                // ✅ 修复：多配置顺序轮播 - 使用索引而不是快照遍历
                int currentIndex = 0;
                while (!token.IsCancellationRequested && ConfigSequence.Count > 1)
                {
                    // ✅ 每次循环时重新检查列表，而不是使用快照
                    if (currentIndex >= ConfigSequence.Count)
                    {
                        currentIndex = 0; // 重新开始循环
                    }

                    if (ConfigSequence.Count == 0) break;

                    var item = ConfigSequence[currentIndex];
                    token.ThrowIfCancellationRequested();

                    int dur = item.DurationSeconds;
                    if (dur <= 0) dur = 5;

                    await LoadConfigToCanvasAsync(item.FilePath, token);

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(dur), token);
                    }
                    catch (TaskCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    // ✅ 移动到下一个索引前再次检查列表（防止删除导致索引越界）
                    if (currentIndex < ConfigSequence.Count - 1)
                    {
                        currentIndex++;
                    }
                    else
                    {
                        currentIndex = 0; // 循环回到开头
                    }
                }
            }
        }

        // 数字限制 (Duration TextBox)
        private static readonly Regex _digitsRegex = new(@"^\d+$");
        private void DurationTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !_digitsRegex.IsMatch(e.Text);
        }

        // ============ NEW IMPLEMENTATION: Load & Render Config ============
        private async Task LoadConfigToCanvasAsync(string filePath, CancellationToken token)
        {
            if (!File.Exists(filePath))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    CurrentImageName.Text = LR("PlayMode_MissingConfig");
                    ImageDimensions.Text = "";
                });
                return;
            }

            CanvasConfiguration? cfg = null;
            try
            {
                var json = await File.ReadAllTextAsync(filePath, token);
                cfg = JsonSerializer.Deserialize<CanvasConfiguration>(json, new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter() }
                });
            }
            catch
            {
                // ignore parsing exceptions in play mode
            }

            if (cfg == null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    CurrentImageName.Text = LR("PlayMode_InvalidConfig");
                    ImageDimensions.Text = "";
                });
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                ApplyConfigurationToPreview(cfg);
                CurrentImageName.Text = System.IO.Path.GetFileName(filePath);
                ImageDimensions.Text = $"{cfg.CanvasSize}×{cfg.CanvasSize}";
            });
            token.ThrowIfCancellationRequested();
        }
        // CHANGE: revise StopVideoPlaybackTimer to optionally preserve decoded frames
        private void StopVideoPlaybackTimer(bool disposeState = false)
        {
            if (_videoTimer != null)
            {
                _videoTimer.Stop();
                _videoTimer.Tick -= VideoTimer_Tick;
                _videoTimer = null;
            }

            // Only clear decoded frames / image when full dispose requested
            if (disposeState)
            {
                _currentVideoFrames = null;
                _videoImage = null;
                _videoFrameIndex = 0;
            }
        }
        private void StartVideoPlaybackTimer(double frameRate)
        {
            if (_currentVideoFrames == null || _currentVideoFrames.Count == 0 || _videoImage == null) return;

            StopVideoPlaybackTimer(disposeState: false); // do NOT clear frames

            int interval = frameRate > 2 ? (int)(1000.0 / frameRate) : 33;
            _videoTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(interval)
            };
            _videoTimer.Tick += VideoTimer_Tick;
            _videoTimer.Start();
        }
        private void VideoTimer_Tick(object? sender, EventArgs e)
        {
            if (_currentVideoFrames == null || _currentVideoFrames.Count == 0 || _videoImage == null)
            {
                StopVideoPlaybackTimer();
                return;
            }
            _videoFrameIndex = (_videoFrameIndex + 1) % _currentVideoFrames.Count;
            UpdateVideoImageSource(_currentVideoFrames[_videoFrameIndex]);
        }
        private void UpdateVideoImageSource(VideoFrameData frame)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                using (var ms = new MemoryStream(frame.JpegData))
                {
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }
                bmp.Freeze();
                _videoImage!.Source = bmp;
            }
            catch { }
        }
        private List<VideoFrameData>? LoadCachedVideoFramesFolder(string relativeOrFull)
        {
            try
            {
                // Try resolve relative
                string full = relativeOrFull;
                if (!Path.IsPathRooted(full))
                {
                    full = Path.Combine(_outputFolder, relativeOrFull);
                    if (!Directory.Exists(full))
                        full = Path.Combine(_outputFolder, "Resources", relativeOrFull);
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
            catch
            {
                return null;
            }
        }

        private async void ScreenToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null) return;
            if (sender is ToggleButton toggle)
            {
                bool isScreenOff = toggle.IsChecked == true;

                if (isScreenOff)
                {
                    // ✅ Turn screen off - save current brightness and set to 0
                    try
                    {
                        SetLoadingState(true, LR("PlayMode_ClosingScreen", "Closing screen..."));

                        // Save current brightness level before turning off
                        SaveCurrentBrightnessToService();

                        // Set display brightness to 0 to turn off screen
                        var results = await _hidDeviceService.SetBrightnessAsync(0);
                        var successCount = results.Values.Count(r => r);

                        if (successCount > 0)
                        {                        
                            // Save IsScreenOn = false
                            SaveScreenStateToService(false);
                            UpdateDisplaySettingsState(false);

                            _currentBrightness = 0;
                            ApplyBrightnessToUi(_currentBrightness);
                           

                            Logger.Info($"Screen closed for {successCount} device(s)");
                        }
                        else
                        {
                            // ✅ Revert toggle state on failure
                            toggle.IsChecked = false;
                          

                            Logger.Warn("Failed to close screen on all devices");
                        }
                    }
                    catch (Exception ex)
                    {
                        // ✅ Revert toggle state on exception
                        toggle.IsChecked = false;
                        Debug.WriteLine($"Failed to close screen: {ex.Message}");
                        Logger.Error($"Failed to close screen: {ex.Message}", ex);
                        ShowNotification(
                            string.Format(
                                LR("PlayMode_ScreenClosedError", "Failed to close screen: {0}"),
                                ex.Message),
                            true,
                            4000);
                    }
                    finally
                    {
                        SetLoadingState(false);
                    }
                }
                else
                {
                    // ✅ Turn screen on - restore brightness (with minimum safety value)
                    try
                    {
                        SetLoadingState(true, LR("PlayMode_OpeningScreen", "Opening screen..."));

                        await RestoreBrightnessFromServiceAsync();
                        
                        // Save IsScreenOn = true
                        SaveScreenStateToService(true);
                        UpdateDisplaySettingsState(true);

                        Logger.Info("Screen opened and brightness restored");
                    }
                    catch (Exception ex)
                    {
                        // ✅ Revert toggle state on exception
                        toggle.IsChecked = true;
                        Debug.WriteLine($"Failed to open screen: {ex.Message}");
                        Logger.Error($"Failed to open screen: {ex.Message}", ex);
                        ShowNotification(
                            string.Format(
                                LR("PlayMode_ScreenOpenedError", "Failed to open screen: {0}"),
                                ex.Message),
                            true,
                            4000);
                    }
                    finally
                    {
                        SetLoadingState(false);
                    }
                }
            }
        }
        // 🆕 新增辅助方法：从左上角 X 坐标计算 TranslateTransform.X
        private double CalculateTranslateX(Border border, double topLeftX, double scaleX)
        {
            double actualW = border.ActualWidth;
            if (actualW <= 0) return topLeftX;

            double scaledW = actualW * scaleX;
            double offsetX = (actualW - scaledW) / 2.0;

            return topLeftX - offsetX;
        }

        // 🆕 新增辅助方法：从左上角 Y 坐标计算 TranslateTransform.Y
        private double CalculateTranslateY(Border border, double topLeftY, double scaleY)
        {
            double actualH = border.ActualHeight;
            if (actualH <= 0) return topLeftY;

            double scaledH = actualH * scaleY;
            double offsetY = (actualH - scaledH) / 2.0;

            return topLeftY - offsetY;
        }
        private void ApplyConfigurationToPreview(CanvasConfiguration cfg)
        {
            StopVideoPlaybackTimer(disposeState: true);
            _liveDynamicItems.Clear();
            _usageVisualItems.Clear();
            _movingDirections.Clear();
            DesignCanvas.Children.Clear();

            _currentCanvasSize = cfg.CanvasSize > 0 ? cfg.CanvasSize : 512;
            _moveSpeed = cfg.MoveSpeed > 0 ? cfg.MoveSpeed : 100;
            _lastMoveTick = DateTime.Now;

            // 背景颜色
            try
            {
                if (!string.IsNullOrWhiteSpace(cfg.BackgroundColor))
                {
                    var converter = new BrushConverter();
                    var brush = (Brush?)converter.ConvertFromString(cfg.BackgroundColor);
                    if (brush != null)
                    {
                        BgColorRect.Fill = brush;
                    }
                    else
                    {
                        BgColorRect.Fill = Brushes.White;
                    }
                }
                else
                {
                    BgColorRect.Fill = Brushes.White;
                }
            }
            catch { BgColorRect.Fill = Brushes.White; }

            // 背景图
            if (!string.IsNullOrWhiteSpace(cfg.BackgroundImagePath))
            {
                var resolved = ResolveRelativePath(cfg.BackgroundImagePath);
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
                        BgImage.Source = bmp;
                        BgImage.Opacity = cfg.BackgroundImageOpacity <= 0 ? 1 : cfg.BackgroundImageOpacity;
                    }
                    catch { BgImage.Source = null; }
                }
                else BgImage.Source = null;
            }
            else BgImage.Source = null;

            foreach (var elem in cfg.Elements.OrderBy(e => e.ZIndex))
            {
                var elemCopy = elem;

                FrameworkElement? content = null;
                Border? host = null;
                bool usageVisualCreated = false;

                switch (elem.Type)
                {
                    case "Text":
                        {
                            var tb = new TextBlock
                            {
                                Text = elem.Text ?? "Text",
                                FontSize = elem.FontSize ?? 24,
                                FontWeight = FontWeights.SemiBold
                            };
                            tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
                            ApplyTextColors(tb, elem.UseTextGradient, elem.TextColor, elem.TextColor2);
                            content = tb;
                            break;
                        }
                    case "LiveText":
                        {
                            if (!elem.LiveKind.HasValue) break;
                            var kind = elem.LiveKind.Value;
                            var tb = new TextBlock
                            {
                                FontSize = elem.FontSize ?? 18,
                                FontWeight = FontWeights.SemiBold
                            };
                            tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

                            // 初始文本
                            if (kind == LiveInfoKindAlias.DateTime)
                                tb.Text = DateTime.Now.ToString(string.IsNullOrWhiteSpace(elem.DateFormat) ? "yyyy-MM-dd HH:mm:ss" : elem.DateFormat);
                            else if (kind == LiveInfoKindAlias.CpuUsage)
                                tb.Text = $"CPU {Math.Round(_metrics.GetCpuUsagePercent())}%";
                            else if (kind == LiveInfoKindAlias.GpuUsage)
                                tb.Text = $"GPU {Math.Round(_metrics.GetGpuUsagePercent())}%";
                            else if (kind == LiveInfoKindAlias.CpuTemperature)
                                tb.Text = $"CPU {Math.Round(_metrics.GetCpuTemperature())}°C";
                            else if (kind == LiveInfoKindAlias.GpuTemperature)
                                tb.Text = $"GPU {Math.Round(_metrics.GetGpuTemperature())}°C";

                            var style = elem.UsageDisplayStyle?.Trim();
                            bool isUsageVisual =
                (kind == LiveInfoKindAlias.CpuUsage ||
                 kind == LiveInfoKindAlias.GpuUsage ||
                 kind == LiveInfoKindAlias.CpuTemperature ||
                 kind == LiveInfoKindAlias.GpuTemperature)
                && !string.IsNullOrWhiteSpace(style)
                && !style.Equals("Text", StringComparison.OrdinalIgnoreCase);

                            if (isUsageVisual)
                            {
                                var item = BuildUsageVisual(kind, style!,
                                    tb,
                                    elem.UsageStartColor,
                                    elem.UsageEndColor,
                                    elem.UsageNeedleColor,
                                    elem.UsageBarBackgroundColor);

                                _usageVisualItems.Add(item);
                                host = item.HostBorder;
                                usageVisualCreated = true;
                            }
                            else
                            {
                                ApplyTextColors(tb, elem.UseTextGradient, elem.TextColor, elem.TextColor2);
                                var item = new UsageVisualItem
                                {
                                    Kind = kind,
                                    Text = tb,
                                    DisplayStyle = "Text",
                                    DateFormat = elem.DateFormat
                                };
                                _usageVisualItems.Add(item);
                                content = tb;
                            }
                            break;
                        }
                    case "Image":
                        {
                            if (!string.IsNullOrEmpty(elem.ImagePath))
                            {
                                var resolved = ResolveRelativePath(elem.ImagePath);
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
                                        var img = new Image { Source = bmp, Stretch = Stretch.Uniform };
                                        content = img;
                                    }
                                    catch { }
                                }
                            }
                            break;
                        }
                    case "Video":
                        {
                            Image? videoImg = null;
                            List<VideoFrameData>? frames = null;
                            string? resolvedVideoPath = null;

                            if (!string.IsNullOrEmpty(elem.VideoFramesCacheFolder))
                            {
                                frames = LoadCachedVideoFramesFolder(elem.VideoFramesCacheFolder);
                            }

                            if (frames == null || frames.Count == 0)
                            {
                                if (!string.IsNullOrEmpty(elem.VideoPath))
                                {
                                    resolvedVideoPath = ResolveRelativePath(elem.VideoPath);
                                    if (File.Exists(resolvedVideoPath))
                                    {
                                        var tb = new TextBlock
                                        {
                                            Text = LR("PlayMode_LoadingVideo"),
                                            FontSize = 20,
                                            Foreground = Brushes.White,
                                            FontWeight = FontWeights.SemiBold,
                                            Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                                            Padding = new Thickness(8)
                                        };
                                        tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
                                        content = tb;

                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                var extracted = new List<VideoFrameData>();
                                                await foreach (var f in VideoConverter.ExtractMp4FramesToJpegRealTimeWithHWAccelAsync(resolvedVideoPath, 85, CancellationToken.None))
                                                {
                                                    extracted.Add(f);
                                                }
                                                if (extracted.Count > 0)
                                                {
                                                    await Dispatcher.InvokeAsync(() =>
                                                    {
                                                        _currentVideoFrames = extracted;
                                                        _videoFrameIndex = 0;
                                                        if (videoImg == null)
                                                        {
                                                            videoImg = new Image
                                                            {
                                                                Stretch = Stretch.Uniform
                                                            };
                                                        }
                                                        _videoImage = videoImg;
                                                        UpdateVideoImageSource(extracted[0]);
                                                        if (videoImg.Parent == null && content?.Parent is Border hostBorder)
                                                        {
                                                            hostBorder.Child = videoImg;
                                                            _videoImage = videoImg;
                                                        }
                                                        double fr = 30;
                                                        _ = Task.Run(async () =>
                                                        {
                                                            try
                                                            {
                                                                var vi = await VideoConverter.GetMp4InfoAsync(resolvedVideoPath);
                                                                if (vi != null && vi.FrameRate > 1) fr = vi.FrameRate;
                                                            }
                                                            catch { }
                                                            Dispatcher.Invoke(() => StartVideoPlaybackTimer(fr));
                                                        });
                                                    });
                                                }
                                            }
                                            catch { }
                                        });
                                    }
                                    else
                                    {
                                        var tb = new TextBlock
                                        {
                                            Text = LR("PlayMode_VideoMissing"),
                                            FontSize = 24,
                                            Foreground = Brushes.LightGray,
                                            FontWeight = FontWeights.Bold
                                        };
                                        tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
                                        content = tb;
                                    }
                                }
                            }
                            else
                            {
                                _currentVideoFrames = frames;
                                _videoFrameIndex = 0;
                                videoImg = new Image { Stretch = Stretch.Uniform };
                                _videoImage = videoImg;
                                UpdateVideoImageSource(frames[0]);

                                double frameRate = 30;
                                if (!string.IsNullOrEmpty(elem.VideoPath))
                                {
                                    resolvedVideoPath = ResolveRelativePath(elem.VideoPath);
                                    if (File.Exists(resolvedVideoPath))
                                    {
                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                var vi = await VideoConverter.GetMp4InfoAsync(resolvedVideoPath);
                                                if (vi != null && vi.FrameRate > 1) frameRate = vi.FrameRate;
                                            }
                                            catch { }
                                            Dispatcher.Invoke(() => StartVideoPlaybackTimer(frameRate));
                                        });
                                    }
                                    else
                                    {
                                        StartVideoPlaybackTimer(frameRate);
                                    }
                                }
                                else
                                {
                                    StartVideoPlaybackTimer(frameRate);
                                }
                                content = videoImg;
                            }
                            break;
                        }
                }

                if (!usageVisualCreated)
                {
                    if (content == null) continue;

                    host = new Border
                    {
                        Child = content,
                        BorderThickness = new Thickness(0),
                        RenderTransformOrigin = new Point(0.5, 0.5)
                    };
                }

                host!.Opacity = elem.Opacity <= 0 ? 1.0 : elem.Opacity;

                // ✅ Transform: 暂时使用原始坐标，稍后在 Loaded 中修正
                var tg = new TransformGroup();
                double scaleFactor = elem.Scale <= 0 ? 1.0 : elem.Scale;
                var scale = new ScaleTransform(scaleFactor, scaleFactor);
                tg.Children.Add(scale);

                if (elem.MirroredX == true)
                    tg.Children.Add(new ScaleTransform(-1, 1));
                if (elem.Rotation.HasValue && Math.Abs(elem.Rotation.Value) > 0.01)
                    tg.Children.Add(new RotateTransform(elem.Rotation.Value));

                var translate = new TranslateTransform(elem.X, elem.Y);
                tg.Children.Add(translate);

                host.RenderTransform = tg;
                Canvas.SetZIndex(host, elem.ZIndex);
                DesignCanvas.Children.Add(host);

                // 🔧 关键修复：为所有元素添加 Loaded 事件修正（包括视频）
                host.Loaded += (s, e) =>
                {
                    if (host.RenderTransform is TransformGroup tgLoaded)
                    {
                        var translateLoaded = tgLoaded.Children.OfType<TranslateTransform>().LastOrDefault();
                        var scaleLoaded = tgLoaded.Children.OfType<ScaleTransform>().FirstOrDefault();

                        if (translateLoaded != null && scaleLoaded != null)
                        {
                            double correctedX = CalculateTranslateX(host, elemCopy.X, scaleLoaded.ScaleX);
                            double correctedY = CalculateTranslateY(host, elemCopy.Y, scaleLoaded.ScaleY);
                            translateLoaded.X = correctedX;
                            translateLoaded.Y = correctedY;
                        }
                    }
                };

                // 自动移动
                if ((elem.Type == "Text" || elem.Type == "LiveText")
                    && elem.MoveDirX.HasValue && elem.MoveDirY.HasValue
                    && (Math.Abs(elem.MoveDirX.Value) > 0.0001 || Math.Abs(elem.MoveDirY.Value) > 0.0001))
                {
                    _movingDirections[host] = (elem.MoveDirX.Value, elem.MoveDirY.Value);
                }
            }

            UpdateAutoMoveTimer();
            _lastAppliedConfig = cfg;
            GlobalConfigRendered?.Invoke(cfg);
        }
        private UsageVisualItem BuildUsageVisual(
                    LiveInfoKindAlias kind,
                    string style,
                    TextBlock baseText,
                    string? startHex,
                    string? endHex,
                    string? needleHex,
                    string? barBgHex)
        {
            // 解析主题色
            var startColor = TryParseColor(startHex ?? "#50B450", out var sc) ? sc : Color.FromRgb(80, 180, 80);
            var endColor = TryParseColor(endHex ?? "#287828", out var ec) ? ec : Color.FromRgb(40, 120, 40);
            var needleColor = TryParseColor(needleHex ?? "#5AC85A", out var nc) ? nc : Color.FromRgb(90, 200, 90);
            var barBgColor = TryParseColor(barBgHex ?? "#28303A", out var bc) ? bc : Color.FromRgb(40, 46, 58);

            var item = new UsageVisualItem
            {
                Kind = kind,
                Text = baseText,
                StartColor = startColor,
                EndColor = endColor,
                NeedleColor = needleColor,
                BarBackgroundColor = barBgColor,
                DisplayStyle = style.Equals("Gauge", StringComparison.OrdinalIgnoreCase) ? "Gauge" :
                               style.Equals("ProgressBar", StringComparison.OrdinalIgnoreCase) ? "ProgressBar" : "Text"
            };

            FrameworkElement root;

            if (item.DisplayStyle == "ProgressBar")
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
                    Background = new SolidColorBrush(barBgColor),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(70, 80, 96)),
                    BorderThickness = new Thickness(1),
                    Margin = barMargin,
                    Height = 14
                };
                var barFill = new Rectangle
                {
                    Height = 14,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Fill = new LinearGradientBrush(startColor, endColor, 0),
                    Width = 0,
                    RadiusX = 5,
                    RadiusY = 5,
                    Margin = barMargin
                };
                item.BarFill = barFill;
                item.BarBackground = barBg;
                item.BarTotalWidth = 160 - barMargin.Left - barMargin.Right;

                baseText.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                baseText.VerticalAlignment = VerticalAlignment.Bottom;
                baseText.FontSize = 14;
                baseText.Margin = new Thickness(0, 0, 0, 2);
                baseText.Foreground = new SolidColorBrush(startColor);

                var layer = new Grid();
                layer.Children.Add(barBg);
                layer.Children.Add(barFill);

                container.Children.Add(layer);
                container.Children.Add(baseText);
                root = container;
            }
            else if (item.DisplayStyle == "Gauge")
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
                    var tickColor = LerpColor(startColor, endColor, t);
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
                    item.GaugeTicks.Add(tick);

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
                        item.GaugeLabels.Add(lbl);

                        if (percent == 50 &&
    (kind == LiveInfoKindAlias.CpuUsage ||
     kind == LiveInfoKindAlias.GpuUsage ||
     kind == LiveInfoKindAlias.CpuTemperature ||
     kind == LiveInfoKindAlias.GpuTemperature))
                        {
                            double label2Radius = labelRadius - 10;
                            double lx2 = GaugeCenterX + label2Radius * Math.Cos(rad);
                            double ly2 = GaugeCenterY + label2Radius * Math.Sin(rad);

                            var kindLabel = new TextBlock
                            {
                                Text = kind == LiveInfoKindAlias.CpuUsage ? "CPU" : "GPU",
                                FontSize = 10,
                                FontWeight = FontWeights.SemiBold,
                                Foreground = new SolidColorBrush(Color.FromRgb(140, 150, 165))
                            };
                            kindLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                            Canvas.SetLeft(kindLabel, lx2 - kindLabel.DesiredSize.Width / 2);
                            Canvas.SetTop(kindLabel, ly2 - kindLabel.DesiredSize.Height / 2 + 10);
                            canvas.Children.Add(kindLabel);
                            item.GaugeKindLabel = kindLabel;
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
                    Stroke = new SolidColorBrush(needleColor),
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

                baseText.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                baseText.VerticalAlignment = VerticalAlignment.Bottom;
                baseText.FontSize = 18;

                container.Children.Add(canvas);
                container.Children.Add(baseText);
                root = container;
            }
            else
            {
                // 不进入（调用方判断），留作兼容
                root = baseText;
            }

            var wrapper = new Border
            {
                Child = root,
                BorderThickness = new Thickness(0),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            item.HostBorder = wrapper;
            return item;
        }

    
        private void AutoMoveTimer_Tick(object? sender, EventArgs e)
        {
            if (_movingDirections.Count == 0) return;

            var now = DateTime.Now;
            double dt = _lastMoveTick == default ? 0.016 : (now - _lastMoveTick).TotalSeconds;
            _lastMoveTick = now;

            // 使用显式访问而不是 var (vx, vy) = kv.Value; 以避免语法 / 版本问题
            foreach (var kv in _movingDirections.ToList())
            {
                var border = kv.Key;
                if (!DesignCanvas.Children.Contains(border))
                {
                    _movingDirections.Remove(border);
                    continue;
                }

                var dir = kv.Value;
                double vx = dir.dx;
                double vy = dir.dy;
                if (Math.Abs(vx) < 0.0001 && Math.Abs(vy) < 0.0001) continue;

                if (border.RenderTransform is not TransformGroup tg) continue;
                var scale = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
                var translate = tg.Children.OfType<TranslateTransform>().FirstOrDefault();
                if (scale == null || translate == null) continue;

                double dx = vx * _moveSpeed * dt;
                double dy = vy * _moveSpeed * dt;
                translate.X += dx;
                translate.Y += dy;

                double w = border.ActualWidth * (scale.ScaleX == 0 ? 1 : scale.ScaleX);
                double h = border.ActualHeight * (scale.ScaleY == 0 ? 1 : scale.ScaleY);

                if (translate.X > _currentCanvasSize) translate.X = -w;
                else if (translate.X + w < 0) translate.X = _currentCanvasSize;

                if (translate.Y > _currentCanvasSize) translate.Y = -h;
                else if (translate.Y + h < 0) translate.Y = _currentCanvasSize;
            }

            UpdateAutoMoveTimer();
        }

        private void UpdateAutoMoveTimer()
        {
            bool any = _movingDirections.Values.Any(v => Math.Abs(v.dx) > 0.0001 || Math.Abs(v.dy) > 0.0001);
            if (any)
            {
                if (!_autoMoveTimer.IsEnabled) _autoMoveTimer.Start();
            }
            else
            {
                if (_autoMoveTimer.IsEnabled) _autoMoveTimer.Stop();
            }
        }

        private void RecolorGaugeTicks(UsageVisualItem item)
        {
            if (item.DisplayStyle != "Gauge") return;
            foreach (var line in item.GaugeTicks)
            {
                if (line.DataContext is double p)
                {
                    double t = p / 100.0;
                    var c = LerpColor(item.StartColor, item.EndColor, t);
                    if (line.Tag as string == "TickMinor")
                        c = Color.FromArgb(160, c.R, c.G, c.B);
                    line.Stroke = new SolidColorBrush(c);
                }
            }
        }

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
                else
                {
                    tb.Foreground = Brushes.White;
                }
            }
            catch
            {
                tb.Foreground = Brushes.White;
            }
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
            catch
            {
                return false;
            }
        }

        private string ResolveRelativePath(string relativePath)
        {
            try
            {
                if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
                    return relativePath;

                string candidate = Path.Combine(_outputFolder, relativePath);
                if (File.Exists(candidate)) return candidate;

                candidate = Path.Combine(_outputFolder, "Resources", relativePath);
                if (File.Exists(candidate)) return candidate;

                foreach (var sub in new[] { "Images", "Backgrounds", "Videos" })
                {
                    candidate = Path.Combine(_outputFolder, "Resources", sub, relativePath);
                    if (File.Exists(candidate)) return candidate;
                }

                return relativePath;
            }
            catch
            {
                return relativePath;
            }
        }
        // =================== (rest of original file unchanged below) ===================

        private void UpdateRotationButtonAppearance(int selectedRotation)
        {
            var normal = (Color)ColorConverter.ConvertFromString("#404040");
            var active = (Color)ColorConverter.ConvertFromString("#2A4A8B");
            Rotation0Button.Background = new SolidColorBrush(normal);
            Rotation90Button.Background = new SolidColorBrush(normal);
            Rotation180Button.Background = new SolidColorBrush(normal);
            Rotation270Button.Background = new SolidColorBrush(normal);
            switch (selectedRotation)
            {
                case 0: Rotation0Button.Background = new SolidColorBrush(active); break;
                case 90: Rotation90Button.Background = new SolidColorBrush(active); break;
                case 180: Rotation180Button.Background = new SolidColorBrush(active); break;
                case 270: Rotation270Button.Background = new SolidColorBrush(active); break;
            }
        }

        private async void RotationButton_Click(object sender, RoutedEventArgs e)
        {

            if (!IsHidServiceReady || _hidDeviceService == null) return;

            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int deg))
            {
                try
                {

                    SetRotationButtonsEnabled(false);

                    var results = await _hidDeviceService.SetRotationAsync(deg);
                    int ok = results.Values.Count(v => v);
                    if (ok == results.Count)
                    {
                        _currentRotation = deg;
                        CurrentRotationText.Text = $"Current: {deg}°";
                        UpdateRotationButtonAppearance(deg);

                    }

                }
                catch (Exception)
                {

                }
                finally
                {
                    SetRotationButtonsEnabled(true);
                }
            }
        }

        private void SetRotationButtonsEnabled(bool enabled)
        {
            Rotation0Button.IsEnabled =
                Rotation90Button.IsEnabled =
                Rotation180Button.IsEnabled =
                Rotation270Button.IsEnabled = enabled;
        }

        private async void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isBrightnessSliderUpdating || !IsHidServiceReady || _hidDeviceService == null) return;
            int newVal = (int)Math.Round(e.NewValue);
            BrightnessValueText.Text = $"{newVal}%";
            if (Math.Abs(newVal - _currentBrightness) < 1) return;

            try
            {
                BrightnessSlider.IsEnabled = false;
                var results = await _hidDeviceService.SetBrightnessAsync(newVal);
                int ok = results.Values.Count(v => v);
                if (ok == results.Count)
                {
                    _currentBrightness = newVal;
                    SaveCurrentBrightnessToService();
                    // ShowStatusMessage($"Brightness {newVal}% applied.");
                }
                else
                {
                    //ShowErrorMessage("Brightness partial failure");
                    _isBrightnessSliderUpdating = true;
                    BrightnessSlider.Value = _currentBrightness;
                    BrightnessValueText.Text = $"{_currentBrightness}%";
                    _isBrightnessSliderUpdating = false;
                }
            }
            catch (Exception ex)
            {
                //ShowErrorMessage($"Brightness failed: {ex.Message}");
                _isBrightnessSliderUpdating = true;
                BrightnessSlider.Value = _currentBrightness;
                BrightnessValueText.Text = $"{_currentBrightness}%";
                _isBrightnessSliderUpdating = false;
            }
            finally
            {
                BrightnessSlider.IsEnabled = true;
            }
        }

        private async void QuickBrightnessButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null) return;



            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int value))
            {
                try
                {
                    _isBrightnessSliderUpdating = true;
                    BrightnessSlider.Value = value;
                    BrightnessValueText.Text = $"{value}%";
                    _isBrightnessSliderUpdating = false;
                    SetQuickBrightnessButtonsEnabled(false);
                    var results = await _hidDeviceService.SetBrightnessAsync(value);
                    var successCount = results.Values.Count(r => r);

                    if (successCount > 0)
                    {
                        _currentBrightness = value;
                        SaveCurrentBrightnessToService();
                    }
                    else
                    {
                        //ShowErrorMessage("Brightness mixed result");
                        _isBrightnessSliderUpdating = true;
                        BrightnessSlider.Value = _currentBrightness;
                        BrightnessValueText.Text = $"{_currentBrightness}%";
                        _isBrightnessSliderUpdating = false;
                    }
                }
                catch (Exception ex)
                {
                    //ShowErrorMessage($"Brightness failed: {ex.Message}");
                    _isBrightnessSliderUpdating = true;
                    BrightnessSlider.Value = _currentBrightness;
                    BrightnessValueText.Text = $"{_currentBrightness}%";
                    _isBrightnessSliderUpdating = false;
                }
                finally
                {
                    SetQuickBrightnessButtonsEnabled(true);
                }
            }
        }

        private void SetQuickBrightnessButtonsEnabled(bool enabled)
        {
            foreach (var btn in FindVisualChildren<Button>(this)
                     .Where(b => b.Tag is string t && int.TryParse(t, out _)))
            {
                btn.IsEnabled = enabled;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T wanted) yield return wanted;
                foreach (var sub in FindVisualChildren<T>(child)) yield return sub;
            }
        }

        private void UpdateStreamingImageViewer(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath)) return;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                Dispatcher.Invoke(() =>
                {
                    StreamingImageViewer.Source = bmp;
                    StreamingImageViewer.Visibility = Visibility.Visible;
                    PlaceholderContent.Visibility = Visibility.Collapsed;
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                    CurrentImageName.Text = System.IO.Path.GetFileName(imagePath);
                    ImageDimensions.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight}";
                    _streamedImageCount++;
                    ImageCounter.Text = _streamedImageCount.ToString();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Viewer update failed: {ex.Message}");
            }
        }


        private bool PrepareStreaming(bool keepAlive = true)
        {

            return true;
        }

        private void FinishStreaming(Dictionary<DeviceInfo, bool> results)
        {
            int ok = results.Values.Count(v => v);

        }

        private void CleanupStreaming()
        {
        }


        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            StopVideoPlaybackTimer(disposeState: true);
            _configSequenceCts?.Cancel();

            try { _liveUpdateTimer.Stop(); } catch { }
            try { _autoMoveTimer.Stop(); } catch { }
            try { _metrics.Dispose(); } catch { }
            try { LanguageSwitch.LanguageChanged -= OnGlobalLanguageChanged; } catch { }
        }

        // MP4 advanced streaming pieces (copied essential parts)
        public delegate void Mp4DeviceDisplayCallback(VideoFrameData frameData, int frameIndex, byte transferId);

        private async Task<bool> SendMp4DataWithAdvancedDeviceSync(
            DisplayController controller,
            string mp4FilePath,
            int frameDelayMs,
            Mp4DeviceDisplayCallback? onDisplay,
            int cycleCount,
            CancellationToken token)
        {
            try
            {
                if (!File.Exists(mp4FilePath)) return false;
                var enable = await controller.SendCmdRealTimeDisplayWithResponse(true);
                if (enable?.IsSuccess != true) return false;
                await controller.SendCmdBrightnessWithResponse(80);
                await controller.SendCmdSetKeepAliveTimerWithResponse(60);

                const int BUFFER = 2;
                byte transferId = 0;
                Queue<(VideoFrameData frame, byte id)> deviceBuffer = new();

                for (int cycle = 0; cycle < cycleCount && !token.IsCancellationRequested; cycle++)
                {
                    await foreach (var frame in VideoConverter.ExtractMp4FramesToJpegRealTimeWithHWAccelAsync(mp4FilePath, 90, token))
                    {
                        if (token.IsCancellationRequested) break;
                        controller.SendFileTransfer(frame.JpegData, 1, transferId);
                        deviceBuffer.Enqueue((frame, transferId));

                        if (deviceBuffer.Count > BUFFER)
                        {
                            var (displayed, id) = deviceBuffer.Dequeue();
                            onDisplay?.Invoke(displayed, displayed.FrameIndex, id);
                        }
                        else if (deviceBuffer.Count == BUFFER)
                        {
                            var (first, id) = deviceBuffer.Peek();
                            onDisplay?.Invoke(first, first.FrameIndex, id);
                        }

                        await Task.Delay(frameDelayMs, token);
                        transferId++;
                        if (transferId > 59) transferId = 4;
                    }
                    while (deviceBuffer.Count > 0 && !token.IsCancellationRequested)
                    {
                        var (remaining, id) = deviceBuffer.Dequeue();
                        onDisplay?.Invoke(remaining, remaining.FrameIndex, id);
                        await Task.Delay(frameDelayMs, token);
                    }
                }
                await controller.SendCmdRealTimeDisplayWithResponse(false);
                return !token.IsCancellationRequested;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                Console.WriteLine($"MP4 advanced failed: {ex.Message}");
                return false;
            }
        }

        private void UpdateStreamingImageViewerFromFrameData(VideoFrameData frameData)
        {
            try
            {
                if (frameData?.JpegData == null || frameData.JpegData.Length == 0) return;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                using (var ms = new MemoryStream(frameData.JpegData))
                {
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }
                bmp.Freeze();
                Dispatcher.Invoke(() =>
                {
                    StreamingImageViewer.Source = bmp;
                    StreamingImageViewer.Visibility = Visibility.Visible;
                    PlaceholderContent.Visibility = Visibility.Collapsed;
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                    CurrentImageName.Text = $"MP4 Frame {frameData.FrameIndex}";
                    ImageDimensions.Text = $"{frameData.Width} × {frameData.Height}";
                    _streamedImageCount++;
                    ImageCounter.Text = _streamedImageCount.ToString();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Frame UI update failed: {ex.Message}");
            }
        }

        private void SaveCanvasButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DesignRoot == null || DesignRoot.ActualWidth <= 0 || DesignRoot.ActualHeight <= 0)
                {
                    LocalizedMessageBox.Show("CanvasNotReady", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                DesignRoot.UpdateLayout();

                int w = (int)Math.Ceiling(DesignRoot.ActualWidth);
                int h = (int)Math.Ceiling(DesignRoot.ActualHeight);

                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(DesignRoot);

                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "captureimage");
                Directory.CreateDirectory(dir);

                string baseName = string.IsNullOrWhiteSpace(_lastAppliedConfig?.ConfigName)
                    ? "PlayModeCanvas"
                    : _lastAppliedConfig!.ConfigName;

                foreach (var c in Path.GetInvalidFileNameChars())
                    baseName = baseName.Replace(c, '_');

                string file = Path.Combine(dir, $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = new FileStream(file, FileMode.Create, FileAccess.Write))
                    encoder.Save(fs);

                LocalizedMessageBox.Show(string.Format(Application.Current.FindResource("FileSaved")?.ToString() ?? "已保存:\n{0}", file), "Success", MessageBoxButton.OK, MessageBoxImage.Information, true);
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(string.Format(Application.Current.FindResource("SaveFailed")?.ToString() ?? "保存失败: {0}", ex.Message), "Error", MessageBoxButton.OK, MessageBoxImage.Error, true);
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (NavigationService?.CanGoBack == true)
                    NavigationService.GoBack();
                else
                    NavigationService?.Navigate(new DevicePage());
            }
            catch
            {
                // Swallow navigation exceptions silently
            }
        }


        #region HID Device Service Management

        /// <summary>
        /// Initialize the HID device service and set up device filtering
        /// </summary>
        private async Task InitializeHidDeviceServiceAsync()
        {
            if (_deviceInfo == null || string.IsNullOrEmpty(_deviceInfo.Path))
            {
                Logger.Warn("Cannot initialize HID service: DeviceInfo or device path is null");
                return;
            }

            try
            {
                Logger.Info($"Initializing HID Device Service for device: {_deviceInfo.ProductString} (Path: {_deviceInfo.Path})");

                // Get the service from ServiceLocator
                _hidDeviceService = ServiceLocator.HidDeviceService;

                if (!_hidDeviceService.IsInitialized)
                {
                    Logger.Warn("HID Device Service is not initialized. Make sure it's initialized in App.xaml.cs");
                    return;
                }

                // Set device path filter to only operate on this specific device
                _hidDeviceService.SetDevicePathFilter(_deviceInfo.Path, enableFilter: true);

                // Only subscribe to DeviceError events
                //_hidDeviceService.DeviceError += OnHidDeviceError;

                _isHidServiceInitialized = true;

                // Verify the device is available
                var targetDevices = _hidDeviceService.GetOperationTargetDevices();
                if (targetDevices.Any())
                {
                    var targetDevice = targetDevices.First();
                    Logger.Info($"Target device found: {targetDevice.ProductString} (Serial: {targetDevice.SerialNumber})");
                }
                else
                {
                    Logger.Warn($"Target device not found in connected devices. Device may be disconnected.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize HID Device Service: {ex.Message}", ex);
                _isHidServiceInitialized = false;
            }
        }

        #endregion


        #region Suspend Media Management Event Handlers

        private void ShowNotification(string message, bool isError = false, int durationMs = 5000)
        {
        }

        /// <summary>
        /// Try to find local media file for a given slot index
        /// </summary>
        /// <param name="slotIndex">Zero-based slot index</param>
        /// <returns>Local file path if found, null otherwise</returns>
        private string? TryFindLocalMediaFile(int slotIndex)
        {
            try
            {
                // First check the offline media data service for this device
                if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                {
                    var offlineDataService = ServiceLocator.OfflineMediaDataService;
                    var mediaFile = offlineDataService.GetSuspendMediaFile(_deviceInfo.SerialNumber, slotIndex);

                    if (mediaFile != null && File.Exists(mediaFile.LocalPath))
                    {
                        Logger.Info($"Found media file in offline data for slot {slotIndex + 1}: {Path.GetFileName(mediaFile.LocalPath)}");
                        return mediaFile.LocalPath;
                    }
                }

                // Fallback to searching the medias directory
                var mediasDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias");
                if (!Directory.Exists(mediasDir))
                    return null;

                // Look for files matching the pattern suspend_{slotIndex}.{extension}
                var searchPattern = $"suspend_{slotIndex}.*";
                var matchingFiles = Directory.GetFiles(mediasDir, searchPattern);

                if (matchingFiles.Length > 0)
                {
                    // Return the first matching file
                    var filePath = matchingFiles[0];
                    if (File.Exists(filePath))
                    {
                        Logger.Info($"Found local media file for slot {slotIndex + 1}: {Path.GetFileName(filePath)}");
                        return filePath;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error searching for local media file for slot {slotIndex + 1}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Handle media slot clicks for adding or viewing media
        /// </summary>
        private async void MediaSlot_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null) return;

            var border = sender as Border;
            if (border?.Tag is string tagStr && int.TryParse(tagStr, out int slotIndex))
            {
                // Check if this slot already has media
                var thumbnailImage = FindName($"MediaThumbnail{slotIndex + 1}") as Image;
                var placeholder = FindName($"AddMediaPlaceholder{slotIndex + 1}") as StackPanel;

                // Check if slot has media by looking at either thumbnail visibility or placeholder content
                bool hasMedia = thumbnailImage?.Visibility == Visibility.Visible ||
                               (placeholder != null && placeholder.Children.OfType<TextBlock>()
                                   .FirstOrDefault()?.Text != "\uE710"); // Not the add icon

                if (hasMedia)
                {
                    // Double-click or specific click handling for media preview
                    if (e.ClickCount == 2)
                    {
                        await OpenMediaPreview(slotIndex);
                    }
                    else
                    {
                        // Single click - show media info
                        ShowMediaInfo(slotIndex);
                    }
                }
                else
                {
                    // Slot is empty - add new media
                    await AddMediaToSlot(slotIndex);
                }
            }
        }

        /// <summary>
        /// Open media preview for a slot (for future enhancement)
        /// </summary>
        private async Task OpenMediaPreview(int slotIndex)
        {
            try
            {
                // Try to find local media file
                var localMediaPath = TryFindLocalMediaFile(slotIndex);
                if (!string.IsNullOrEmpty(localMediaPath) && File.Exists(localMediaPath))
                {
                    var fileExtension = Path.GetExtension(localMediaPath).ToLower();
                    if (IsImageFile(fileExtension) || IsVideoFile(fileExtension))
                    {
                        // For video files, try to get video info before opening
                        if (IsVideoFile(fileExtension))
                        {
                            try
                            {
                                var videoInfo = await VideoThumbnailHelper.GetVideoInfoAsync(localMediaPath);
                                if (videoInfo != null)
                                {
                                    var duration = videoInfo.Duration.ToString(@"mm\:ss");
                                    var resolution = $"{videoInfo.PrimaryVideoStream?.Width}x{videoInfo.PrimaryVideoStream?.Height}";
                                    Logger.Info($"Opening video: {Path.GetFileName(localMediaPath)} - Duration: {duration}, Resolution: {resolution}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to get video info: {ex.Message}");
                            }
                        }

                        // Open with default system application
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = localMediaPath,
                            UseShellExecute = true
                        });
                        Logger.Info($"Opened media preview for slot {slotIndex + 1}: {Path.GetFileName(localMediaPath)}");
                    }
                    else
                    {
                        ShowNotification($"Cannot preview this media type: {fileExtension}", true, 3000);
                    }
                }
                else
                {
                    ShowNotification($"No local media file available for preview (Slot {slotIndex + 1})", true, 3000);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to open media preview for slot {slotIndex + 1}: {ex.Message}");
                ShowNotification($"Failed to open media preview: {ex.Message}", true, 3000);
            }
        }

        /// <summary>
        /// Add media file to a specific slot
        /// </summary>
        private async Task AddMediaToSlot(int slotIndex)
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = $"Select Media File for Slot {slotIndex + 1}",
                    Filter = "Supported Media|*.mp4;*.jpg",
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var filePath = openFileDialog.FileName;
                    await AddMediaFile(filePath, slotIndex);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to add media to slot {slotIndex}: {ex.Message}");
                ShowNotification($"Failed to add media: {ex.Message}", true);
            }
        }

        /// <summary>
        /// Add media file to device and update UI
        /// </summary>
        private async Task AddMediaFile(string filePath, int slotIndex)
        {
            string tempConvertedPath = null;
            string sourceFilePath = filePath;

            try
            {
                SetLoadingState(true, string.Format(LR("PlayMode_AddingMediaToSlot"), slotIndex + 1));

                // Check if video needs conversion
                if (IsVideoFile(Path.GetExtension(filePath)))
                {
                    try 
                    {
                        var videoInfo = await VideoConverter.GetMp4InfoAsync(filePath);
                        if (videoInfo != null)
                        {
                            // Check if conversion needed: not 480x480 OR bitrate > 2.5Mbps
                            bool needsResize = videoInfo.Width != 480 || videoInfo.Height != 480;
                            bool needsBitrateReduction = videoInfo.BitRate > 1500000; 

                            if (needsResize || needsBitrateReduction)
                            {
                                SetLoadingState(true, LR("PlayMode_OptimizingVideo"));
                                
                                tempConvertedPath = Path.Combine(Path.GetTempPath(), $"converted_{Guid.NewGuid()}.mp4");
                                bool converted = await VideoConverter.ConvertVideoAsync(filePath, tempConvertedPath, 480, 480, 1500);
                                
                                if (converted)
                                {
                                    sourceFilePath = tempConvertedPath;
                                    Logger.Info($"Video converted: {filePath} -> {sourceFilePath}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to check/convert video: {ex.Message}");
                    }
                }

                // Need to rename the file name start suspend_1.jpg, suspend_2.mp4, etc.
                var fileExtension = Path.GetExtension(sourceFilePath).ToLower();
                var newFileName = $"suspend_{slotIndex}{fileExtension}";

                // Determine the target directory based on device serial number
                string targetDir;
                string newFilePath;

                if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                {
                    // Use device-specific directory
                    targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias", _deviceInfo.SerialNumber);
                    newFilePath = Path.Combine(targetDir, newFileName);
                }
                else
                {
                    // Use general medias folder
                    targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias");
                    newFilePath = Path.Combine(targetDir, newFileName);
                }

                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                // Update offline media data service if available
                if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                {
                    var offlineDataService = ServiceLocator.OfflineMediaDataService;
                    offlineDataService.AddSuspendMediaFile(
                        _deviceInfo.SerialNumber,
                        newFileName,
                        sourceFilePath,
                        slotIndex,
                        transferId: (byte)(slotIndex + 1)
                    );
                    Logger.Info($"Added media file to offline data: {newFileName}");
                }
                else
                {
                    File.Copy(sourceFilePath, newFilePath, true);
                }

                // Transfer file to device using suspend media functionality
                var results = await _hidDeviceService.SendMultipleSuspendFilesAsync(
                    new List<string> { newFilePath },
                    startingTransferId: (byte)(slotIndex + 1));

                var successCount = results.Values.Count(r => r);

                if (successCount > 0)
                {
                    await _hidDeviceService.SetRealTimeDisplayAsync(false);
                    //await _hidDeviceService.sen
                    // Update UI to show the media
                    UpdateMediaSlotUI(slotIndex, newFilePath, true);
                    ShowNotification($"Media added to slot {slotIndex + 1} successfully.");

                    // Refresh device status to get updated suspend media info
                    //await RefreshDeviceStatus();
                }
                else
                {
                    ShowNotification($"Failed to add media to slot {slotIndex + 1}.", true);

                    // Remove from offline data if device transfer failed
                    if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                    {
                        var offlineDataService = ServiceLocator.OfflineMediaDataService;
                        offlineDataService.RemoveSuspendMediaFile(_deviceInfo.SerialNumber, slotIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to add media file: {ex.Message}");
                Logger.Error($"Failed to add media file to slot {slotIndex + 1}: {ex.Message}", ex);
                ShowNotification($"Failed to add media: {ex.Message}", true);
            }
            finally
            {
                SetLoadingState(false);
                if (tempConvertedPath != null && File.Exists(tempConvertedPath))
                {
                    try { File.Delete(tempConvertedPath); } catch { }
                }
            }
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            // 关闭承载此 Page 的窗口
            Window.GetWindow(this)?.Close();
        }
        /// <summary>
        /// Update media slot UI to show media or empty state
        /// </summary>
        private async void UpdateMediaSlotUI(int slotIndex, string filePath = null, bool hasMedia = false)
        {
            try
            {
                var slotNumber = slotIndex + 1;
                var thumbnail = FindName($"MediaThumbnail{slotNumber}") as Image;
                var placeholder = FindName($"AddMediaPlaceholder{slotNumber}") as StackPanel;
                var infoOverlay = FindName($"MediaInfoOverlay{slotNumber}") as Border;
                var fileName = FindName($"MediaFileName{slotNumber}") as TextBlock;
                var removeButton = FindName($"RemoveMediaButton{slotNumber}") as Button;
                var mediaSlot = FindName($"MediaSlot{slotNumber}") as Border;

                // Release existing image source to prevent memory leaks and file locks
                if (thumbnail?.Source is BitmapImage existingBitmap)
                {
                    try
                    {
                        // Clear the source and dispose if possible
                        thumbnail.Source = null;

                        // Force garbage collection of the bitmap to release file handles
                        existingBitmap.StreamSource?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to release existing image source for slot {slotNumber}: {ex.Message}");
                    }
                }

                if (hasMedia && !string.IsNullOrEmpty(filePath))
                {
                    // Show media state
                    if (thumbnail != null)
                    {
                        // Try to load actual media content
                        try
                        {
                            if (File.Exists(filePath))
                            {
                                var fileExtension = Path.GetExtension(filePath).ToLower();
                                if (IsImageFile(fileExtension))
                                {
                                    // Display image directly with proper resource management
                                    var bitmap = new BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
                                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load immediately and release file handle
                                    bitmap.UriSource = new Uri(filePath);
                                    bitmap.DecodePixelWidth = 200; // Optimize for display
                                    bitmap.EndInit();
                                    bitmap.Freeze(); // Make it thread-safe and optimize memory

                                    thumbnail.Source = bitmap;
                                    thumbnail.Visibility = Visibility.Visible;
                                }
                                else if (IsVideoFile(fileExtension))
                                {
                                    // Check cache first for video thumbnails
                                    BitmapImage? videoThumbnail = null;

                                    if (_videoThumbnailCache.ContainsKey(filePath))
                                    {
                                        videoThumbnail = _videoThumbnailCache[filePath];
                                        Logger.Info($"Using cached video thumbnail for slot {slotNumber}: {Path.GetFileName(filePath)}");
                                    }
                                    else
                                    {
                                        // Generate and cache video thumbnail
                                        videoThumbnail = await VideoThumbnailHelper.GenerateVideoThumbnailAsync(filePath, width: 200, height: 150);
                                        if (videoThumbnail != null)
                                        {
                                            videoThumbnail.Freeze(); // Make it thread-safe and optimize memory
                                            _videoThumbnailCache[filePath] = videoThumbnail;
                                            Logger.Info($"Generated and cached video thumbnail for slot {slotNumber}: {Path.GetFileName(filePath)}");
                                        }
                                    }

                                    if (videoThumbnail != null)
                                    {
                                        thumbnail.Source = videoThumbnail;
                                        thumbnail.Visibility = Visibility.Visible;
                                    }
                                    else
                                    {
                                        // Fallback: hide thumbnail and show video icon in placeholder
                                        thumbnail.Visibility = Visibility.Collapsed;
                                        Logger.Warn($"Failed to generate video thumbnail for slot {slotNumber}: {Path.GetFileName(filePath)}");
                                    }
                                }
                                else
                                {
                                    // For other file types
                                    thumbnail.Visibility = Visibility.Collapsed;
                                }
                            }
                            else
                            {
                                // File doesn't exist locally (device media), show media icon instead
                                thumbnail.Visibility = Visibility.Collapsed;
                            }
                        }
                        catch (Exception ex)
                        {
                            // If media loading fails, hide the image
                            Logger.Warn($"Failed to load media for slot {slotNumber}: {ex.Message}");
                            thumbnail.Visibility = Visibility.Collapsed;
                        }
                    }

                    if (placeholder != null)
                    {
                        // Show appropriate icon based on media type
                        var iconBlock = placeholder.Children.OfType<TextBlock>().FirstOrDefault();
                        var textBlock = placeholder.Children.OfType<TextBlock>().LastOrDefault();

                        if (iconBlock != null)
                        {
                            if (File.Exists(filePath))
                            {
                                var fileExtension = Path.GetExtension(filePath).ToLower();
                                if (IsVideoFile(fileExtension))
                                {
                                    iconBlock.Text = "\uE714"; // Video play icon
                                    iconBlock.Foreground = new SolidColorBrush(Colors.LightCoral);
                                    iconBlock.FontSize = 24; // Larger for video icon
                                }
                                else if (IsImageFile(fileExtension))
                                {
                                    // For images, hide placeholder since we show the actual image
                                    iconBlock.Text = "\uE91B"; // Image icon (backup)
                                    iconBlock.Foreground = new SolidColorBrush(Colors.LightGreen);
                                }
                                else
                                {
                                    iconBlock.Text = "\uE8A5"; // Generic media icon
                                    iconBlock.Foreground = new SolidColorBrush(Colors.LightBlue);
                                }
                            }
                            else
                            {
                                // Device media
                                iconBlock.Text = "\uE8B3"; // Device media icon
                                iconBlock.Foreground = new SolidColorBrush(Colors.LightBlue);
                            }
                        }

                        if (textBlock != null && textBlock != iconBlock)
                        {
                            if (File.Exists(filePath))
                            {
                                var fileExtension = Path.GetExtension(filePath).ToLower();
                                if (IsVideoFile(fileExtension))
                                {
                                    textBlock.Text = LR("PlayMode_VideoMedia");
                                    textBlock.Foreground = new SolidColorBrush(Colors.LightCoral);
                                }
                                else if (IsImageFile(fileExtension))
                                {
                                    textBlock.Text = LR("PlayMode_ImageMedia");
                                    textBlock.Foreground = new SolidColorBrush(Colors.LightGreen);
                                }
                                else
                                {
                                    textBlock.Text = LR("PlayMode_MediaFile");
                                    textBlock.Foreground = new SolidColorBrush(Colors.LightBlue);
                                }
                            }
                            else
                            {
                                textBlock.Text = LR("PlayMode_DeviceMedia");
                                textBlock.Foreground = new SolidColorBrush(Colors.LightBlue);
                            }
                        }

                        // Show placeholder when image is not visible (for videos with thumbnails, hide placeholder)
                        // For videos with successful thumbnails, hide the placeholder; for failed thumbnails, show it
                        bool showPlaceholder = thumbnail?.Visibility != Visibility.Visible;
                        placeholder.Visibility = showPlaceholder ? Visibility.Visible : Visibility.Collapsed;
                    }

                    if (infoOverlay != null) infoOverlay.Visibility = Visibility.Visible;
                    if (fileName != null)
                    {
                        fileName.Text = File.Exists(filePath) ? Path.GetFileName(filePath) : Path.GetFileName(filePath);
                    }
                    if (removeButton != null) removeButton.Visibility = Visibility.Visible;
                    if (mediaSlot != null) mediaSlot.BorderBrush = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    // Show empty slot - ensure image source is properly released
                    if (thumbnail != null)
                    {
                        // Properly release the image source
                        if (thumbnail.Source is BitmapImage bitmapToRelease)
                        {
                            try
                            {
                                thumbnail.Source = null;
                                bitmapToRelease.StreamSource?.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to release image source for empty slot {slotNumber}: {ex.Message}");
                            }
                        }
                        else
                        {
                            thumbnail.Source = null;
                        }
                        thumbnail.Visibility = Visibility.Collapsed;
                    }
                    if (placeholder != null)
                    {
                        // Reset to add media state
                        var iconBlock = placeholder.Children.OfType<TextBlock>().FirstOrDefault();
                        var textBlock = placeholder.Children.OfType<TextBlock>().LastOrDefault();

                        if (iconBlock != null)
                        {
                            iconBlock.Text = "\uE710"; // Add icon
                            iconBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                            iconBlock.FontSize = 20; // Reset font size
                        }
                        if (textBlock != null && textBlock != iconBlock)
                        {
                            textBlock.Text = LR("PlayMode_AddMedia");
                            textBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                        }

                        placeholder.Visibility = Visibility.Visible;
                    }
                    if (infoOverlay != null) infoOverlay.Visibility = Visibility.Collapsed;
                    if (removeButton != null) removeButton.Visibility = Visibility.Collapsed;
                    if (mediaSlot != null) mediaSlot.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update media slot UI: {ex.Message}");
                Logger.Error($"Failed to update media slot {slotIndex + 1} UI: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Check if file extension is an image
        /// </summary>
        private bool IsImageFile(string extension)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp" };
            return imageExtensions.Contains(extension);
        }

        /// <summary>
        /// Check if file extension is a video
        /// </summary>
        private bool IsVideoFile(string extension)
        {
            return VideoThumbnailHelper.IsSupportedVideoFile($"dummy{extension}");
        }

        /// <summary>
        /// Show media information for a slot
        /// </summary>
        private void ShowMediaInfo(int slotIndex)
        {
            var fileName = FindName($"MediaFileName{slotIndex + 1}") as TextBlock;
            var mediaName = fileName?.Text ?? $"Media {slotIndex + 1}";

            ShowNotification($"Media: {mediaName} (Slot {slotIndex + 1})", false, 3000);
        }

        /// <summary>
        /// Remove media from a specific slot
        /// </summary>
        private async void RemoveMedia_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null) return;

            var button = sender as Button;
            if (button?.Tag is string tagStr && int.TryParse(tagStr, out int slotIndex))
            {
                try
                {
                    SetLoadingState(true, string.Format(LR("PlayMode_RemovingMediaFromSlot"), slotIndex + 1));

                    // Release image source before deletion to avoid file locks
                    var slotNumber = slotIndex + 1;
                    var thumbnail = FindName($"MediaThumbnail{slotNumber}") as Image;
                    if (thumbnail?.Source is BitmapImage bitmapToRelease)
                    {
                        try
                        {
                            thumbnail.Source = null;
                            bitmapToRelease.StreamSource?.Dispose();
                            Logger.Info($"Released image source for slot {slotNumber} before deletion");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to release image source for slot {slotNumber}: {ex.Message}");
                        }
                    }

                    // Get the actual filename from the offline media data or local media file
                    string? fileNameToDelete = null;
                    string? localMediaPath = null;

                    // First try to get from offline media data service
                    if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                    {
                        var offlineDataService = ServiceLocator.OfflineMediaDataService;
                        var mediaFile = offlineDataService.GetSuspendMediaFile(_deviceInfo.SerialNumber, slotIndex);

                        if (mediaFile != null)
                        {
                            fileNameToDelete = mediaFile.FileName;
                            localMediaPath = mediaFile.LocalPath;
                            Logger.Info($"Found media file in offline data for deletion: {fileNameToDelete}");
                        }
                    }

                    // Fallback to trying to find local media file
                    if (string.IsNullOrEmpty(fileNameToDelete))
                    {
                        localMediaPath = TryFindLocalMediaFile(slotIndex);
                        if (!string.IsNullOrEmpty(localMediaPath) && File.Exists(localMediaPath))
                        {
                            fileNameToDelete = Path.GetFileName(localMediaPath);
                            Logger.Info($"Found local media file for deletion: {fileNameToDelete}");
                        }
                    }

                    // Use fallback naming pattern if still no file found
                    if (string.IsNullOrEmpty(fileNameToDelete))
                    {
                        fileNameToDelete = $"suspend_{slotIndex}";
                        Logger.Info($"Using fallback filename for deletion: {fileNameToDelete}");
                    }

                    // Delete specific suspend file from device using the actual filename
                    var results = await _hidDeviceService.DeleteSuspendFilesAsync(fileNameToDelete);
                    var successCount = results.Values.Count(r => r);

                    if (successCount > 0)
                    {
                        // Remove from offline media data service
                        if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                        {
                            var offlineDataService = ServiceLocator.OfflineMediaDataService;
                            offlineDataService.RemoveSuspendMediaFile(_deviceInfo.SerialNumber, slotIndex);
                            Logger.Info($"Removed media file from offline data: slot {slotIndex + 1}");
                        }

                        // Remove from video thumbnail cache if it exists
                        if (!string.IsNullOrEmpty(localMediaPath) && _videoThumbnailCache.ContainsKey(localMediaPath))
                        {
                            try
                            {
                                var cachedThumbnail = _videoThumbnailCache[localMediaPath];
                                _videoThumbnailCache.Remove(localMediaPath);

                                // Dispose the cached thumbnail to free memory
                                cachedThumbnail?.StreamSource?.Dispose();
                                Logger.Info($"Removed and disposed video thumbnail from cache: {Path.GetFileName(localMediaPath)}");
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to dispose cached thumbnail: {ex.Message}");
                            }
                        }

                        // Also remove the local file if it exists and wasn't handled by offline service
                        if (!string.IsNullOrEmpty(localMediaPath) && File.Exists(localMediaPath))
                        {
                            try
                            {
                                // Small delay to ensure image source is fully released
                                await Task.Delay(100);

                                File.Delete(localMediaPath);
                                Logger.Info($"Deleted local media file: {Path.GetFileName(localMediaPath)}");
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to delete local media file: {ex.Message}");
                            }
                        }

                        // Update UI to show empty slot
                        UpdateMediaSlotUI(slotIndex, null, false);
                        ShowNotification($"Media removed from slot {slotIndex + 1} successfully.");

                        // Refresh device status to get updated suspend media info
                        //await RefreshDeviceStatus();
                    }
                    else
                    {
                        ShowNotification($"Failed to remove media from slot {slotIndex + 1}.", true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to remove media: {ex.Message}");
                    Logger.Error($"Failed to remove media from slot {slotIndex + 1}: {ex.Message}", ex);
                    ShowNotification($"Failed to remove media: {ex.Message}", true);
                }
                finally
                {
                    SetLoadingState(false);
                }
            }
        }

        /// <summary>
        /// Add multiple media files at once
        /// </summary>
        private async void AddAllMedia_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null) return;

            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Multiple Media Files",
                    Filter = "Supported Media|*.mp4;*.jpg;*.jpeg",
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = true
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var filePaths = openFileDialog.FileNames.Take(5).ToList(); // Limit to 5 files

                    SetLoadingState(true, LR("PlayMode_AddingMultipleMedia"));

                    // Process each file individually with proper naming
                    var processedFiles = new List<string>();
                    for (int i = 0; i < filePaths.Count && i < 5; i++)
                    {
                        var originalPath = filePaths[i];
                        var fileExtension = Path.GetExtension(originalPath).ToLower();
                        var newFileName = $"suspend_{i}{fileExtension}";

                        // Save to medias folder first
                        var mediasDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias");
                        if (!Directory.Exists(mediasDir))
                            Directory.CreateDirectory(mediasDir);
                        var newFilePath = Path.Combine(mediasDir, newFileName);
                        File.Copy(originalPath, newFilePath, true);

                        processedFiles.Add(newFilePath);
                    }

                    // Send all processed files to device
                    var results = await _hidDeviceService.SendMultipleSuspendFilesAsync(processedFiles, startingTransferId: 1);
                    var successCount = results.Values.Count(r => r);

                    if (successCount > 0)
                    {
                        ShowNotification($"Added {processedFiles.Count} media files successfully.");

                        // Refresh device status to get updated suspend media info
                        //await RefreshDeviceStatus();
                    }
                    else
                    {
                        ShowNotification("Failed to add media files.", true);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to add multiple media files: {ex.Message}");
                Logger.Error($"Failed to add multiple media files: {ex.Message}", ex);
                ShowNotification($"Failed to add media files: {ex.Message}", true);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        /// <summary>
        /// Clear all media files
        /// </summary>
        private async void ClearAllMedia_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null) return;

            try
            {
                SetLoadingState(true, LR("PlayMode_ClearingAllMedia"));

                // Release all image sources first to prevent file locks and update UI
                for (int i = 0; i < 5; i++)
                {
                    UpdateMediaSlotUI(i, null, false);
                }

                // Delete all suspend files from device
                var results = await _hidDeviceService.DeleteSuspendFilesAsync("all");
                var successCount = results.Values.Count(r => r);

                if (successCount > 0)
                {
                    // Clear from offline media data service
                    if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                    {
                        var offlineDataService = ServiceLocator.OfflineMediaDataService;
                        offlineDataService.ClearAllSuspendMediaFiles(_deviceInfo.SerialNumber);
                        Logger.Info("Cleared all suspend media files from offline data");
                    }

                    // Also clean up all local media files and cache
                    try
                    {
                        // First try device-specific directory
                        var deviceMediasDir = _deviceInfo?.SerialNumber != null
                            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias", _deviceInfo.SerialNumber)
                            : null;

                        var mediasDirectories = new List<string>();
                        if (deviceMediasDir != null && Directory.Exists(deviceMediasDir))
                        {
                            mediasDirectories.Add(deviceMediasDir);
                        }

                        // Also check general medias directory
                        var generalMediasDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias");
                        if (Directory.Exists(generalMediasDir))
                        {
                            mediasDirectories.Add(generalMediasDir);
                        }

                        // Clear video thumbnail cache and dispose cached thumbnails first
                        var thumbnailsToDispose = new List<BitmapImage>(_videoThumbnailCache.Values);
                        _videoThumbnailCache.Clear();

                        foreach (var cachedThumbnail in thumbnailsToDispose)
                        {
                            try
                            {
                                cachedThumbnail?.StreamSource?.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to dispose cached thumbnail: {ex.Message}");
                            }
                        }
                        Logger.Info("Disposed all cached video thumbnails");

                        // Small delay to ensure all image sources are released
                        await Task.Delay(200);

                        foreach (var mediasDir in mediasDirectories)
                        {
                            // Delete all suspend media files (suspend_0.*, suspend_1.*, etc.)
                            for (int i = 0; i < 5; i++)
                            {
                                var searchPattern = $"suspend_{i}.*";
                                var matchingFiles = Directory.GetFiles(mediasDir, searchPattern);

                                foreach (var file in matchingFiles)
                                {
                                    try
                                    {
                                        File.Delete(file);
                                        Logger.Info($"Deleted local media file: {Path.GetFileName(file)}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Warn($"Failed to delete local file {Path.GetFileName(file)}: {ex.Message}");
                                    }
                                }
                            }
                        }

                        Logger.Info("Cleared video thumbnail cache and local media files");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to clean up local media files: {ex.Message}");
                    }

                    ShowNotification("All media files cleared successfully.");

                    // Refresh device status to get updated suspend media info
                    //await RefreshDeviceStatus();
                }
                else
                {
                    ShowNotification("Failed to clear media files.", true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to clear all media: {ex.Message}");
                Logger.Error($"Failed to clear all media: {ex.Message}", ex);
                ShowNotification($"Failed to clear media files: {ex.Message}", true);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        // New methods for active suspend media display
        private void ShowActiveSuspendMedia()
        {
            try
            {
                // Ensure content area is visible
                var contentArea = FindName("SuspendModeContentArea") as FrameworkElement;
                if (contentArea != null) contentArea.Visibility = Visibility.Visible;

                // Load media slots instead of old list
                LoadSuspendMediaSlots(new DeviceStatus()); // Pass empty status for now
            }
            catch { /* UI elements may not be available */ }
        }

        private void ShowAddMediaButton()
        {
            try
            {
                // Ensure content area is visible
                var contentArea = FindName("SuspendModeContentArea") as FrameworkElement;
                if (contentArea != null) contentArea.Visibility = Visibility.Visible;

                // Clear all media slots to show add buttons
                for (int i = 0; i < 5; i++)
                {
                    UpdateMediaSlotUI(i, null, false);
                }
            }
            catch { /* UI elements may not be available */ }
        }

        private void LoadActiveSuspendMediaFiles()
        {
            // This method is now replaced by LoadSuspendMediaSlots
            // Keeping for backward compatibility but functionality moved to LoadSuspendMediaSlots
        }

        // TODO: Add method to get actual suspend media information from device
        private async Task<List<string>> GetActiveSuspendMediaFromDevice()
        {
            // This method is now replaced by GetSuspendMediaStatusFromDevice
            // Keeping for backward compatibility
            var activeFiles = new List<string>();

            try
            {
                var status = await GetSuspendMediaStatusFromDevice();
                // Convert status to file list if needed for backward compatibility
                // Implementation details would depend on the actual device status structure
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get suspend media from device: {ex.Message}");
            }

            return activeFiles;
        }

        #endregion
        private void SetLoadingState(bool isLoading, string? message = null)
        {
            if (message == null) message = LR("PlayMode_Loading");
            if (LoadingOverlay != null)
            {
                LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            }
            
            if (LoadingText != null)
            {
                LoadingText.Text = message;
            }

            if (isLoading)
            {
                // Disable controls if needed
            }
            else
            {
                // Enable controls if needed
            }
        }

        // Method to refresh suspend media display without full device status update
        private async Task RefreshSuspendMediaDisplay()
        {
            try
            {
                var suspendToggle = FindName("SuspendModeToggle") as ToggleButton;
                bool suspendModeActive = suspendToggle?.IsChecked == true;

                //// Update content area visibility
                //var contentArea = FindName("SuspendModeContentArea") as FrameworkElement;
                //if (contentArea != null)
                //{
                //    contentArea.Visibility = suspendModeActive ? Visibility.Visible : Visibility.Collapsed;
                //}

                if (suspendModeActive)
                {
                    // Get actual suspend media status from device
                    var suspendMediaStatus = await GetSuspendMediaStatusFromDevice();
                    if (suspendMediaStatus.MaxSuspendMediaCount > 0)
                    {
                        LoadSuspendMediaSlots(suspendMediaStatus);
                        Logger.Info($"Refreshed suspend media display. Found {suspendMediaStatus.ActiveSuspendMediaCount} active media files");
                    }
                    else
                    {
                        // No valid status, show empty slots
                        for (int i = 0; i < 5; i++)
                        {
                            UpdateMediaSlotUI(i, null, false);
                        }
                        Logger.Warn("No valid suspend media status received from device");
                    }
                }
                else
                {
                    HideSuspendModeContent();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to refresh suspend media display: {ex.Message}");
                Logger.Error($"Failed to refresh suspend media display: {ex.Message}", ex);
                HideSuspendModeContent();
            }
        }

        private async Task<DeviceStatus> GetSuspendMediaStatusFromDevice()
        {
            try
            {
                if (IsHidServiceReady && _hidDeviceService != null)
                {
                    var statusResults = await _hidDeviceService.GetDeviceStatusAsync();

                    if (statusResults.Any())
                    {
                        var result = statusResults.First();
                        if (result.Value.HasValue)
                        {
                            return result.Value.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get suspend media status from device: {ex.Message}");
            }

            return new DeviceStatus(); // Return empty status as fallback
        }
        /// <summary>
        /// Load and display suspend media slots based on device status
        /// </summary>
        private void LoadSuspendMediaSlots(DeviceStatus status)
        {
            try
            {
                // Ensure content area is visible
                var contentArea = FindName("SuspendModeContentArea") as FrameworkElement;
                if (contentArea != null) contentArea.Visibility = Visibility.Visible;

                // Use actual device status to determine which slots have media
                var suspendMediaActive = status.SuspendMediaActive ?? new int[0];
                var maxSlots = Math.Min(5, status.MaxSuspendMediaCount);

                Logger.Info($"Loading suspend media slots. Max slots: {maxSlots}, Active media: [{string.Join(", ", suspendMediaActive)}]");

                // Update each media slot based on device status
                for (int i = 0; i < 5; i++)
                {
                    bool hasMedia = i < suspendMediaActive.Length && suspendMediaActive[i] > 0;

                    if (hasMedia)
                    {
                        // Try to find corresponding local media file first
                        var localMediaPath = TryFindLocalMediaFile(i);
                        if (!string.IsNullOrEmpty(localMediaPath))
                        {
                            // Show local media with actual content
                            UpdateMediaSlotUI(i, localMediaPath, true);
                            Logger.Info($"Slot {i + 1}: Local media file found - {Path.GetFileName(localMediaPath)}");
                        }
                        else
                        {
                            // Show device media with placeholder name
                            string mediaName = $"suspend_media_{i + 1}";
                            UpdateMediaSlotUI(i, mediaName, true);
                            Logger.Info($"Slot {i + 1}: Device media found (no local file)");
                        }
                    }
                    else
                    {
                        // Show empty slot with add button
                        UpdateMediaSlotUI(i, null, false);
                        Logger.Info($"Slot {i + 1}: Empty slot");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load suspend media slots: {ex.Message}");
                Logger.Error($"Failed to load suspend media slots: {ex.Message}", ex);
                // Clear all slots on error
                for (int i = 0; i < 5; i++)
                {
                    UpdateMediaSlotUI(i, null, false);
                }
            }
        }


        // Hide all suspend mode content when suspend mode is off
        private void HideSuspendModeContent()
        {
            try
            {
                //var contentArea = FindName("SuspendModeContentArea") as FrameworkElement;
                //if (contentArea != null)
                //    contentArea.Visibility = Visibility.Collapsed;

                // Release image sources before clearing all media slots
                for (int i = 0; i < 5; i++)
                {
                    var slotNumber = i + 1;
                    var thumbnail = FindName($"MediaThumbnail{slotNumber}") as Image;
                    if (thumbnail?.Source is BitmapImage bitmapToRelease)
                    {
                        try
                        {
                            thumbnail.Source = null;
                            bitmapToRelease.StreamSource?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to release image source for slot {slotNumber} during hide: {ex.Message}");
                        }
                    }
                }

                // Clear all media slots to show add state
                for (int i = 0; i < 5; i++)
                {
                    UpdateMediaSlotUI(i, null, false);
                }

                Logger.Info("Suspend mode content hidden and media slots reset with image sources released");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to hide suspend mode content: {ex.Message}");
                Logger.Error($"Failed to hide suspend mode content: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Method that loads the playback mode when device info is set
        /// This should be called in the DeviceInfo property setter
        /// </summary>
        private void LoadPlaybackModeFromService()
        {
            if (_deviceInfo?.SerialNumber == null || !ServiceLocator.IsOfflineMediaServiceInitialized)
                return;

            try
            {
                var offlineDataService = ServiceLocator.OfflineMediaDataService;
                var savedPlaybackMode = offlineDataService.GetDevicePlaybackMode(_deviceInfo.SerialNumber);

                _currentPlayMode = savedPlaybackMode;
                UpdatePlayModeUI();

                Console.WriteLine($"Loaded playback mode for device {_deviceInfo.SerialNumber}: {savedPlaybackMode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load playback mode for device {_deviceInfo?.SerialNumber}: {ex.Message}");
                // Use default mode on error
                _currentPlayMode = PlaybackMode.RealtimeConfig;
            }
        }

        /// <summary>
        /// Method that saves the current playback mode
        /// This should be called whenever _currentPlayMode changes
        /// </summary>
        private void SavePlaybackModeToService()
        {
            if (_deviceInfo?.SerialNumber == null || !ServiceLocator.IsOfflineMediaServiceInitialized)
                return;

            try
            {
                var offlineDataService = ServiceLocator.OfflineMediaDataService;
                offlineDataService.UpdateDevicePlaybackMode(_deviceInfo.SerialNumber, _currentPlayMode);

                Console.WriteLine($"Saved playback mode for device {_deviceInfo.SerialNumber}: {_currentPlayMode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save playback mode for device {_deviceInfo?.SerialNumber}: {ex.Message}");
            }
        }

        /// <summary>
        /// Persists the current brightness value to the offline data service
        /// </summary>
        private void SaveCurrentBrightnessToService()
        {
            if (_deviceInfo?.SerialNumber == null || !ServiceLocator.IsOfflineMediaServiceInitialized)
                return;

            try
            {
                var offlineDataService = ServiceLocator.OfflineMediaDataService;
                offlineDataService.UpdateDeviceSetting(_deviceInfo.SerialNumber, "brightness", _currentBrightness);
                // Also ensure IsScreenOn is true if brightness > 0
                if (_currentBrightness > 0)
                {
                    offlineDataService.UpdateDeviceSetting(_deviceInfo.SerialNumber, "isscreenon", true);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to save brightness for device {_deviceInfo?.SerialNumber}: {ex.Message}");
            }
        }

        private void SaveScreenStateToService(bool isScreenOn)
        {
            if (_deviceInfo?.SerialNumber == null || !ServiceLocator.IsOfflineMediaServiceInitialized)
                return;

            try
            {
                var offlineDataService = ServiceLocator.OfflineMediaDataService;
                offlineDataService.UpdateDeviceSetting(_deviceInfo.SerialNumber, "isscreenon", isScreenOn);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to save screen state for device {_deviceInfo?.SerialNumber}: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads current brightness and rotation from the device via HID and updates the UI
        /// </summary>
        private async Task<bool> LoadCurrentDeviceDisplaySettingsAsync()
        {
            if (!IsHidServiceReady || _hidDeviceService == null || _deviceInfo == null)
                return false;

            try
            {
                var statusResults = await _hidDeviceService.GetDeviceStatusAsync();
                if (!statusResults.Any()) return false;

                DeviceStatus? status = null;

                if (!string.IsNullOrEmpty(_deviceInfo.Path) &&
                    statusResults.TryGetValue(_deviceInfo.Path, out var matchedStatus) &&
                    matchedStatus.HasValue)
                {
                    status = matchedStatus.Value;
                }
                else
                {
                    status = statusResults.Values.FirstOrDefault(s => s.HasValue);
                }

                if (!status.HasValue) return false;

                var deviceStatus = status.Value;
                int deviceBrightness = Math.Clamp(deviceStatus.Brightness, 0, 100);
                int normalizedRotation = NormalizeRotation(deviceStatus.Degree);

                _currentBrightness = deviceBrightness;
                ApplyBrightnessToUi(deviceBrightness);

                _currentRotation = normalizedRotation;
                UpdateRotationUi(normalizedRotation);

                // Update ScreenToggle based on actual device brightness
                if (ScreenToggle != null)
                {
                    bool isScreenOn = deviceBrightness > 0;
                    ScreenToggle.Click -= ScreenToggle_Click;
                    // Unchecked = On, Checked = Off
                    ScreenToggle.IsChecked = !isScreenOn;
                    ScreenToggle.Click += ScreenToggle_Click;

                    UpdateDisplaySettingsState(isScreenOn);

                    // Sync to service
                    SaveScreenStateToService(isScreenOn);
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load device display settings for {_deviceInfo?.SerialNumber}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Restores brightness to the value saved in the offline data service
        /// </summary>
        /// <summary>
        /// Restores brightness to the value saved in the offline data service
        /// </summary>
        private async Task RestoreBrightnessFromServiceAsync()
        {
            if (_deviceInfo?.SerialNumber == null || !ServiceLocator.IsOfflineMediaServiceInitialized)
                return;

            try
            {
                var offlineDataService = ServiceLocator.OfflineMediaDataService;
                var settings = offlineDataService.GetDeviceSettings(_deviceInfo.SerialNumber);
                var savedBrightness = settings.Brightness;

                // ✅ 关键修复：如果保存的亮度是 0，恢复时使用 100 作为安全默认值
                if (savedBrightness <= 0)
                {
                    savedBrightness = 100;
                    Logger.Info($"Saved brightness was {settings.Brightness}, using safe default value: 100");
                }
                else
                {
                    // Clamp to valid range
                    savedBrightness = Math.Clamp(savedBrightness, 1, 100);
                }

                if (!IsHidServiceReady || _hidDeviceService == null)
                {
                    _currentBrightness = savedBrightness;
                    ApplyBrightnessToUi(_currentBrightness);
                    return;
                }

                var results = await _hidDeviceService.SetBrightnessAsync(savedBrightness);
                int successCount = results.Values.Count(v => v);

                if (successCount > 0)
                {
                    _currentBrightness = savedBrightness;
                    ApplyBrightnessToUi(_currentBrightness);
                    Logger.Info($"Brightness restored to {savedBrightness}% for device {_deviceInfo.SerialNumber}");
                }
                else
                {
                    Logger.Warn($"Failed to restore brightness for device {_deviceInfo.SerialNumber}.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to restore brightness for device {_deviceInfo?.SerialNumber}: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies the provided brightness value to UI elements on the dispatcher thread
        /// </summary>
        private void ApplyBrightnessToUi(int brightness)
        {
            void update()
            {
                _isBrightnessSliderUpdating = true;
                if (BrightnessSlider != null)
                {
                    BrightnessSlider.Value = brightness;
                }
                if (BrightnessValueText != null)
                {
                    BrightnessValueText.Text = $"{brightness}%";
                }
                _isBrightnessSliderUpdating = false;
            }

            if (Dispatcher.CheckAccess())
            {
                update();
            }
            else
            {
                Dispatcher.Invoke(update);
            }
        }

        /// <summary>
        /// Updates rotation-related UI elements to match the provided rotation value
        /// </summary>
        private void UpdateRotationUi(int rotation)
        {
            void update()
            {
                if (CurrentRotationText != null)
                {
                    var rotationFormat = LR("PlayMode_CurrentRotationFormat", "Current: {0}°");
                    CurrentRotationText.Text = string.Format(rotationFormat, rotation);
                }

                UpdateRotationButtonAppearance(rotation);
            }

            if (Dispatcher.CheckAccess())
            {
                update();
            }
            else
            {
                Dispatcher.Invoke(update);
            }
        }

        /// <summary>
        /// Normalizes rotation degrees to canonical values supported by the device
        /// </summary>
        private static int NormalizeRotation(int rotation)
        {
            int normalized = rotation % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }

            return normalized switch
            {
                0 => 0,
                90 => 90,
                180 => 180,
                270 => 270,
                _ => 0
            };
        }

        private void UpdatePlayModeUI()
        {
            bool isRealtime = _currentPlayMode == PlaybackMode.RealtimeConfig;

            // binding the SuspendModeToggle state from current mode
            SuspendModeToggle.IsChecked = !isRealtime;

            // ✅ 关键修复：确保两个容器的可见性正确切换
            if (ConfigsContainer != null)
            {
                ConfigsContainer.Visibility = isRealtime ? Visibility.Visible : Visibility.Collapsed;
            }

            if (SuspendModeContentArea != null)
            {
                SuspendModeContentArea.Visibility = isRealtime ? Visibility.Collapsed : Visibility.Visible;
            }

            // 更新按钮组可见性
            if (ConfigActionButtons != null)
            {
                ConfigActionButtons.Visibility = isRealtime ? Visibility.Visible : Visibility.Collapsed;
            }

            // 更新标题和提示文本
            if (PlayContentTitleText != null)
            {
                PlayContentTitleText.Text = isRealtime ? "Play Content" : "";
            }

            if (PlayContentHintText != null)
            {
                PlayContentHintText.Text = isRealtime
                    ? "Add configs to auto play (loop). Remove all to stop automatically."
                    : "Configure offline media files below. Up to 5 media files can be added.";
            }

            // 🆕 Control overlay visibility
            UpdateOfflineModeOverlay(!isRealtime);

            UpdateLivePreviewAvailability();
            ApplyPlayModeLocalization();

            // ✅ 强制刷新布局
            Dispatcher.InvokeAsync(() =>
            {
                if (ConfigsContainer != null) ConfigsContainer.UpdateLayout();
                if (SuspendModeContentArea != null) SuspendModeContentArea.UpdateLayout();
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        /// <summary>
        /// Show or hide the offline mode overlay with animation
        /// </summary>
        private void UpdateOfflineModeOverlay(bool showOverlay)
        {
            if (OfflineModeOverlay == null) return;

            if (showOverlay)
            {
                // Show overlay when switching to offline mode
                OfflineModeOverlay.Visibility = Visibility.Visible;

                // Optional: Auto-hide after a few seconds
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    if (_currentPlayMode == PlaybackMode.OfflineVideo)
                    {
                        // Fade out the overlay
                        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 1.0,
                            To = 0.0,
                            Duration = TimeSpan.FromSeconds(0.5)
                        };
                        fadeOut.Completed += (_, __) => OfflineModeOverlay.Visibility = Visibility.Collapsed;
                        OfflineModeOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    }
                };
                timer.Start();
            }
            else
            {
                // Hide overlay when switching back to realtime mode
                OfflineModeOverlay.Visibility = Visibility.Collapsed;
                OfflineModeOverlay.Opacity = 1.0; // Reset opacity
            }
        }

        private async void SuspendModeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null) return;

            var toggle = sender as ToggleButton;
            if (toggle == null) return;

            bool isActivated = toggle.IsChecked == true;

            try
            {
                SetLoadingState(true, LR(isActivated ? "PlayMode_ActivatingSuspend" : "PlayMode_ActivatingRealtime",
                         isActivated ? "Activating offline mode..." : "Activating realtime mode..."));

                await RestoreBrightnessFromServiceAsync();
                var results = await _hidDeviceService.SetRealTimeDisplayAsync(!isActivated);
                var successCount = results.Values.Count(r => r);

                if (successCount > 0)
                {
                    ShowNotification(isActivated ? "Suspend mode activated successfully." : "RealTime mode activated successfully.");

                    // ✅ 关键：先更新模式状态
                    _currentPlayMode = isActivated ? PlaybackMode.OfflineVideo : PlaybackMode.RealtimeConfig;
                    SavePlaybackModeToService();

                    // ✅ 然后更新UI - 这会根据新的 _currentPlayMode 来切换显示
                    UpdatePlayModeUI();

                    // ✅ 最后执行模式特定的操作
                    if (isActivated)
                    {
                        SuspendModeEnabled();
                        await RefreshSuspendMediaDisplay();
                    }
                    else
                    {
                        RealtimeStreamingEnabled();
                        HideSuspendModeContent();
                    }
                }
                else
                {
                    ShowNotification($"Failed to {(isActivated ? "activate" : "clear")} suspend mode. Please try again.", true);
                    // ✅ 失败时恢复 toggle 状态
                    toggle.IsChecked = !isActivated;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to {(isActivated ? "activate" : "clear")} suspend mode: {ex.Message}");
                ShowNotification($"Failed to {(isActivated ? "activate" : "clear")} suspend mode: {ex.Message}", true);
                // ✅ 异常时恢复 toggle 状态
                toggle.IsChecked = !isActivated;
            }
            finally
            {
                SetLoadingState(false);
            }
        }



        /// <summary>
        /// Fetches the stored brightness value from the offline data service
        /// </summary>
        private void GetCurrentBrightnessToService()
        {
            if (_deviceInfo?.SerialNumber == null || !ServiceLocator.IsOfflineMediaServiceInitialized)
                return;

            try
            {
                var offlineDataService = ServiceLocator.OfflineMediaDataService;
                var settings = offlineDataService.GetDeviceSettings(_deviceInfo.SerialNumber);
                var savedBrightness = Math.Clamp(settings.Brightness, 0, 100);

                _currentBrightness = savedBrightness;
                ApplyBrightnessToUi(_currentBrightness);

                // ✅ 同步更新 ScreenToggle 状态
                //if (ScreenToggle != null)
                //{
                //    bool isScreenOn = savedBrightness > 0;
                //    ScreenToggle.Click -= ScreenToggle_Click;
                //    ScreenToggle.IsChecked = isScreenOn;
                //    ScreenToggle.Click += ScreenToggle_Click;
                //}
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load brightness for device {_deviceInfo?.SerialNumber}: {ex.Message}");
            }
        }



    }
}