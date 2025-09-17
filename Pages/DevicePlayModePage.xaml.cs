// NEW: bring in config models + enum alias
using CMDevicesManager.Helper;
using CMDevicesManager.Models;
using CMDevicesManager.Helper;
using CMDevicesManager.Pages; // contains CanvasConfiguration / ElementConfiguration
using CMDevicesManager.Services;
using CMDevicesManager.Utilities;
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
        private bool _isLoading = false;
        // Video thumbnail cache to avoid regenerating thumbnails
        private Dictionary<string, BitmapImage> _videoThumbnailCache = new();

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
        private enum PlayMode
        {
            RealtimeConfig,
            OfflineVideo
        }
        private PlayMode _currentPlayMode = PlayMode.RealtimeConfig;
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

        private MultiDeviceManager? _multiDeviceManager;
        private bool _isStreamingInProgress;
        private int _streamedImageCount;
        private CancellationTokenSource? _streamingCancellationSource;

        private Timer? _keepAliveTimer;
        private readonly object _keepAliveTimerLock = new();
        private bool _keepAliveEnabled;

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
            //double cpu = 4.9f;
            //double gpu = 56.0f;
            DateTime now = DateTime.Now;

            // 纯文本/日期（无样式）
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
                    case LiveInfoKindAlias.DateTime:
                        text.Text = now.ToString(string.IsNullOrWhiteSpace(fmt) ? "yyyy-MM-dd HH:mm:ss" : fmt);
                        break;
                }
            }

            // 带 ProgressBar / Gauge
            foreach (var item in _usageVisualItems)
            {
                double raw = item.Kind == LiveInfoKindAlias.CpuUsage ? cpu :
                             item.Kind == LiveInfoKindAlias.GpuUsage ? gpu : 0;

                switch (item.DisplayStyle)
                {
                    case "ProgressBar":
                        {
                            if (item.BarFill != null)
                            {
                                double percent = Math.Clamp(raw, 0, 100);
                                item.BarFill.Width = item.BarTotalWidth * (percent / 100.0);
                                string prefix = item.Kind == LiveInfoKindAlias.CpuUsage ? "CPU" :
                                                item.Kind == LiveInfoKindAlias.GpuUsage ? "GPU" : "";
                                item.Text.Text = string.IsNullOrEmpty(prefix)
                                    ? $"{Math.Round(percent)}%"
                                    : $"{prefix} {Math.Round(percent)}%";

                                // 文字颜色渐变
                                double t = percent / 100.0;
                                var col = LerpColor(item.StartColor, item.EndColor, t);
                                item.Text.Foreground = new SolidColorBrush(col);
                            }
                            break;
                        }
                    case "Gauge":
                        {
                            if (item.GaugeNeedleRotate != null)
                            {
                                double target = GaugeRotationFromPercent(raw);
                                item.GaugeNeedleRotate.Angle = target; // 可加动画，播放页直接赋值即可
                            }
                            item.Text.Text = $"{Math.Round(raw)}%";
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
                            break;
                        }
                }
            }
        }

        private void UpdatePlayModeUI()
        {
            bool isRealtime = _currentPlayMode == PlayMode.RealtimeConfig;

            ConfigsContainer.Visibility = isRealtime ? Visibility.Visible : Visibility.Collapsed;
            SuspendModeContentArea.Visibility = isRealtime ? Visibility.Collapsed : Visibility.Visible;

            // 原来的 ConfigActionButtons / VideoActionButtons 如果只包含 Start/Stop，可在 XAML 中移除或设置 Visibility=Collapsed
            ConfigActionButtons.Visibility = isRealtime ? Visibility.Visible : Visibility.Collapsed;
            //VideoActionButtons.Visibility = isRealtime ? Visibility.Collapsed : Visibility.Visible;

            PlayContentTitleText.Text = isRealtime ? "Play Content" : "Video Playback";
            PlayContentHintText.Text = isRealtime
                ? "Add configs to auto play (loop). Remove all to stop automatically."
                : "Select an MP4 file to auto start playback. Clear to stop.";

        }

        // 修改播放模式按钮事件（只做模式切换 + 停止当前模式）
        private void RealtimeStreamingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlayMode == PlayMode.RealtimeConfig) return;

            if (_isVideoStreaming)
            {
                _streamingCancellationSource?.Cancel();
                _isVideoStreaming = false;
            }
            _currentPlayMode = PlayMode.RealtimeConfig;
            UpdatePlayModeUI();

            if (!_isConfigSequenceRunning && ConfigSequence.Count > 0)
                StartConfigSequence();
        }

        private void RealtimeStreamingEnabled()
        {
            if (_currentPlayMode == PlayMode.RealtimeConfig) return;

            if (_isVideoStreaming)
            {
                _streamingCancellationSource?.Cancel();
                _isVideoStreaming = false;
            }

            if (!_isConfigSequenceRunning && ConfigSequence.Count > 0)
                StartConfigSequence();
        }
        private  void SuspendModeEnabled()
        {
            if (_currentPlayMode == PlayMode.OfflineVideo) return;

            if (_isConfigSequenceRunning)
            {
                _configSequenceCts?.Cancel();
                _isConfigSequenceRunning = false;
            }

            //if (!_isVideoStreaming && !string.IsNullOrEmpty(_selectedVideoPath))
            //    _ = StartVideoPlaybackInternalAsync();
        }
        
        private async void Mp4StreamingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlayMode == PlayMode.OfflineVideo) return;

            if (_isConfigSequenceRunning)
            {
                _configSequenceCts?.Cancel();
                _isConfigSequenceRunning = false;
            }
            _currentPlayMode = PlayMode.OfflineVideo;
            UpdatePlayModeUI();

            //if (!_isVideoStreaming && !string.IsNullOrEmpty(_selectedVideoPath))
            //    _ = StartVideoPlaybackInternalAsync();
        }

        private async void SuspendModeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;


            var toggle = sender as ToggleButton;
            //var toggle = sender as ToggleButton;
            if (toggle == null) return;

            bool isActivated = toggle.IsChecked == true;

            // No confirmation dialog needed - tooltip provides the info
            try
            {
                SetLoadingState(true, isActivated ? "Activating suspend mode..." : "Activating RealTime mode...");

                var results = await _hidDeviceService.SetRealTimeDisplayAsync(!isActivated);
                var successCount = results.Values.Count(r => r);

                if (successCount > 0)
                {
                    ShowNotification(isActivated ? "Suspend mode activated successfully." : "RealTime mode activated successfully.");

                    _currentPlayMode =isActivated? PlayMode.OfflineVideo: PlayMode.RealtimeConfig;
                    UpdatePlayModeUI();
                    // Update suspend mode display based on new state
                    if (isActivated)
                    {
                        SuspendModeEnabled();
                        // When activating suspend mode, refresh the display
                        await RefreshSuspendMediaDisplay();
                    }
                    else
                    {
                        RealtimeStreamingEnabled();
                        // When clearing suspend mode, hide all suspend content
                        HideSuspendModeContent();

                        // Refresh status to update display
                        //await RefreshDeviceStatus();
                    }
                }
                else
                {
                    ShowNotification($"Failed to {(isActivated ? "activate" : "clear")} suspend mode. Please try again.", true);
                    // Revert toggle state on failure
                    toggle.IsChecked = !isActivated;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to {(isActivated ? "activate" : "clear")} suspend mode: {ex.Message}");
                ShowNotification($"Failed to {(isActivated ? "activate" : "clear")} suspend mode: {ex.Message}", true);
                // Revert toggle state on exception
                toggle.IsChecked = !isActivated;
            }
            finally
            {
                SetLoadingState(false);
            }
        }


        // 在 UpdateButtonStates 内（如果保留）增加模式适配（可选）


        // 如果未调用 InitConfigSequence，请确保添加
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_multiDeviceManager == null)
            {
                try
                {
                    _multiDeviceManager = new MultiDeviceManager(0x2516, 0x0228);
                    _multiDeviceManager.StartMonitoring();
                }
                catch (Exception ex)
                {
                    //  ShowErrorMessage("Device manager init failed: " + ex.Message);
                }
            }
            UpdatePlayModeUI();
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

                if (_currentPlayMode == PlayMode.RealtimeConfig && !_isConfigSequenceRunning)
                {
                    StartConfigSequence();
                }
            }
        }

        private void AddConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Reuse same folder structure as DeviceConfigPage
                var configFolder = Path.Combine(_outputFolder, "Configs");
                if (!Directory.Exists(configFolder))
                {
                    LocalizedMessageBox.Show("NoConfigFolderFoundDetail", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var files = Directory.GetFiles(configFolder, "*.json");
                if (files.Length == 0)
                {
                    LocalizedMessageBox.Show("NoConfigFilesFound", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var list = new List<(string path, CanvasConfiguration config)>();
                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var cfg = JsonSerializer.Deserialize<CanvasConfiguration>(json, new JsonSerializerOptions
                        {
                            Converters = { new JsonStringEnumConverter() }
                        });
                        if (cfg != null)
                            list.Add((file, cfg));
                    }
                    catch
                    {
                        // Skip invalid file
                    }
                }

                if (list.Count == 0)
                {
                    LocalizedMessageBox.Show("NoValidConfigFilesLoaded", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new ConfigSelectionDialog(list);
                if (Application.Current.MainWindow != null)
                    dialog.Owner = Application.Current.MainWindow;

                if (dialog.ShowDialog() == true && dialog.SelectedConfigPath != null)
                {
                    var cfg = dialog.SelectedConfig;
                    string displayName = !string.IsNullOrWhiteSpace(cfg?.ConfigName)
                        ? cfg!.ConfigName
                        : Path.GetFileNameWithoutExtension(dialog.SelectedConfigPath);

                    var newItem = new PlayConfigItem
                    {
                        DisplayName = displayName,
                        FilePath = dialog.SelectedConfigPath,
                        DurationSeconds = 5
                    };
                    AttachConfigItemEvents(newItem);
                    ConfigSequence.Add(newItem);

                    UpdateDurationEditableStates();
                    SaveGlobalSequence();

                    if (_currentPlayMode == PlayMode.RealtimeConfig && !_isConfigSequenceRunning)
                        StartConfigSequence();
                }
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(string.Format(Application.Current.FindResource("AddConfigFailed")?.ToString() ?? "添加配置失败：{0}", ex.Message), "Error", MessageBoxButton.OK, MessageBoxImage.Error, true);
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
            _currentPlayMode = PlayMode.RealtimeConfig;
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

            CurrentImageName.Text = "No config";
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
                    //ShowStatusMessage($"Loaded: {single.DisplayName} (continuous)");
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

                // 多配置顺序轮播
                foreach (var item in ConfigSequence.ToList())
                {
                    token.ThrowIfCancellationRequested();

                    int dur = item.DurationSeconds;
                    if (dur <= 0) dur = 5;

                    //ShowStatusMessage($"Loading: {item.DisplayName} ({dur}s)");
                    await LoadConfigToCanvasAsync(item.FilePath, token);

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(dur), token);
                    }
                    catch (TaskCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                    }
                }
            }
            //ShowStatusMessage("Sequence stopped.");
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
                    CurrentImageName.Text = "(Missing config)";
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
                    CurrentImageName.Text = "(Invalid config)";
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
                    var brush = (Brush)new BrushConverter().ConvertFromString(cfg.BackgroundColor);
                    BgColorRect.Fill = brush;
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

                            var style = elem.UsageDisplayStyle?.Trim();
                            bool isUsageVisual = (kind == LiveInfoKindAlias.CpuUsage || kind == LiveInfoKindAlias.GpuUsage)
                                                 && !string.IsNullOrWhiteSpace(style)
                                                 && !style.Equals("Text", StringComparison.OrdinalIgnoreCase);

                            if (isUsageVisual)
                            {
                                // 直接使用 BuildUsageVisual 返回的 HostBorder，避免将其子元素再放入新的 Border 造成重复逻辑父节点异常
                                var item = BuildUsageVisual(kind, style!,
                                    tb,
                                    elem.UsageStartColor,
                                    elem.UsageEndColor,
                                    elem.UsageNeedleColor,
                                    elem.UsageBarBackgroundColor);

                                _usageVisualItems.Add(item);
                                host = item.HostBorder;   // 直接作为最终 host
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
                                content = tb; // 后面统一包装成 border
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
                            // Try cached frames first
                            Image? videoImg = null;
                            List<VideoFrameData>? frames = null;
                            string? resolvedVideoPath = null;
                            if (!string.IsNullOrEmpty(elem.VideoFramesCacheFolder))
                            {
                                frames = LoadCachedVideoFramesFolder(elem.VideoFramesCacheFolder);
                            }
                            if (frames == null || frames.Count == 0)
                            {
                                // Fallback: decode frames asynchronously (non-blocking) if video file provided
                                if (!string.IsNullOrEmpty(elem.VideoPath))
                                {
                                    resolvedVideoPath = ResolveRelativePath(elem.VideoPath);
                                    if (File.Exists(resolvedVideoPath))
                                    {
                                        // Placeholder image while decoding
                                        var tb = new TextBlock
                                        {
                                            Text = "Loading Video...",
                                            FontSize = 20,
                                            Foreground = Brushes.White,
                                            FontWeight = FontWeights.SemiBold,
                                            Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                                            Padding = new Thickness(8)
                                        };
                                        tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
                                        content = tb;

                                        // Fire & forget extraction
                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                var extracted = new List<VideoFrameData>();
                                                await foreach (var f in VideoConverter.ExtractMp4FramesToJpegRealTimeAsync(resolvedVideoPath, 85, CancellationToken.None))
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
                                                        // Replace placeholder border child (host already added later)
                                                        if (videoImg.Parent == null && content?.Parent is Border hostBorder)
                                                        {
                                                            hostBorder.Child = videoImg;
                                                            _videoImage = videoImg;
                                                        }
                                                        // Try get frame rate
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
                                        // No file; simple placeholder
                                        var tb = new TextBlock
                                        {
                                            Text = "VIDEO (missing)",
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
                                // Cached frames available
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

                // 如果是 usageVisualCreated，则 host 已经完整；否则需要用 content 创建 host
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

                // 共用：设置 Opacity
                host!.Opacity = elem.Opacity <= 0 ? 1.0 : elem.Opacity;

                // Transform: Scale → (Mirror) → (Rotate) → Translate
                var tg = new TransformGroup();
                var scale = new ScaleTransform(elem.Scale <= 0 ? 1.0 : elem.Scale, elem.Scale <= 0 ? 1.0 : elem.Scale);
                tg.Children.Add(scale);

                if (elem.MirroredX == true)
                {
                    tg.Children.Add(new ScaleTransform(-1, 1));
                }
                if (elem.Rotation.HasValue && Math.Abs(elem.Rotation.Value) > 0.01)
                {
                    tg.Children.Add(new RotateTransform(elem.Rotation.Value));
                }
                tg.Children.Add(new TranslateTransform(elem.X, elem.Y));
                host.RenderTransform = tg;

                Canvas.SetZIndex(host, elem.ZIndex);
                DesignCanvas.Children.Add(host);

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

                        if (percent == 50 && (kind == LiveInfoKindAlias.CpuUsage || kind == LiveInfoKindAlias.GpuUsage))
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

            var wrapper = new Border { Child = root };
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

            var actives = _multiDeviceManager.GetActiveControllers();

            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int deg))
            {
                try
                {

                    SetRotationButtonsEnabled(false);

                    var results = await _multiDeviceManager.SetRotationOnAllDevices(deg);
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
                Rotation270Button.IsEnabled = enabled && !_isStreamingInProgress;
        }

        private async void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isBrightnessSliderUpdating || _multiDeviceManager == null) return;
            int newVal = (int)Math.Round(e.NewValue);
            BrightnessValueText.Text = $"{newVal}%";
            if (Math.Abs(newVal - _currentBrightness) < 1) return;
            var actives = _multiDeviceManager.GetActiveControllers();
            if (actives.Count == 0) { return; }
            try
            {
                BrightnessSlider.IsEnabled = false;
                var results = await _multiDeviceManager.SetBrightnessOnAllDevices(newVal);
                int ok = results.Values.Count(v => v);
                if (ok == results.Count)
                {
                    _currentBrightness = newVal;
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
            if (_multiDeviceManager == null) { return; }
            var actives = _multiDeviceManager.GetActiveControllers();
            if (actives.Count == 0) { return; }
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int value))
            {
                try
                {
                    _isBrightnessSliderUpdating = true;
                    BrightnessSlider.Value = value;
                    BrightnessValueText.Text = $"{value}%";
                    _isBrightnessSliderUpdating = false;
                    SetQuickBrightnessButtonsEnabled(false);
                    var results = await _multiDeviceManager.SetBrightnessOnAllDevices(value);
                    int ok = results.Values.Count(v => v);
                    if (ok == results.Count)
                    {
                        _currentBrightness = value;
                        //ShowStatusMessage($"Brightness {value}% applied.");
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
                btn.IsEnabled = enabled && !_isStreamingInProgress;
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

        private async void LoadCurrentDeviceSettings()
        {
            if (_multiDeviceManager == null) return;
            var first = _multiDeviceManager.GetActiveControllers().FirstOrDefault();
            if (first == null) return;
            try
            {
                var rot = await first.SendCmdReadRotatedAngleWithResponse();
                if (rot?.IsSuccess == true && rot.Value.ResponseData != null)
                {
                    try
                    {
                        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(rot.Value.ResponseData);
                        if (dict != null && dict.TryGetValue("degree", out var degObj) && int.TryParse(degObj.ToString(), out int deg))
                        {
                            _currentRotation = deg;
                            Dispatcher.Invoke(() =>
                            {
                                CurrentRotationText.Text = $"Current: {deg}°";
                                UpdateRotationButtonAppearance(deg);
                            });
                        }
                    }
                    catch { }
                }
                var bri = await first.SendCmdReadBrightnessWithResponse();
                if (bri?.IsSuccess == true && bri.Value.ResponseData != null)
                {
                    try
                    {
                        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(bri.Value.ResponseData);
                        if (dict != null && dict.TryGetValue("brightness", out var bObj) && int.TryParse(bObj.ToString(), out int val))
                        {
                            _currentBrightness = val;
                            Dispatcher.Invoke(() =>
                            {
                                _isBrightnessSliderUpdating = true;
                                BrightnessSlider.Value = val;
                                BrightnessValueText.Text = $"{val}%";
                                _isBrightnessSliderUpdating = false;
                            });
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Load settings failed: {ex.Message}");
            }
        }

        private void ShowStreamingStatus(bool active)
        {
            Dispatcher.Invoke(() =>
            {
                if (active)
                {
                    StreamingStatusIndicator.Visibility = Visibility.Visible;
                    LoadingIndicator.Visibility = Visibility.Visible;
                    PlaceholderContent.Visibility = Visibility.Collapsed;
                }
                else
                {
                    StreamingStatusIndicator.Visibility = Visibility.Collapsed;
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                    if (StreamingImageViewer.Source == null)
                        PlaceholderContent.Visibility = Visibility.Visible;
                }
            });
        }

        private void ResetStreamingViewer()
        {
            Dispatcher.Invoke(() =>
            {
                StreamingImageViewer.Source = null;
                StreamingImageViewer.Visibility = Visibility.Collapsed;
                PlaceholderContent.Visibility = Visibility.Visible;
                LoadingIndicator.Visibility = Visibility.Collapsed;
                StreamingStatusIndicator.Visibility = Visibility.Collapsed;
                CurrentImageName.Text = "No image loaded";
                ImageDimensions.Text = "";
                _streamedImageCount = 0;
                ImageCounter.Text = "0";
            });
        }

        private void StartKeepAliveTimer()
        {
            lock (_keepAliveTimerLock)
            {
                _keepAliveTimer?.Stop();
                _keepAliveTimer?.Dispose();
                _keepAliveEnabled = true;
                _keepAliveTimer = new Timer(4000);
                _keepAliveTimer.Elapsed += OnKeepAliveTimerElapsed;
                _keepAliveTimer.AutoReset = true;
                _keepAliveTimer.Start();
            }
        }

        private void StopKeepAliveTimer()
        {
            lock (_keepAliveTimerLock)
            {
                _keepAliveEnabled = false;
                _keepAliveTimer?.Stop();
                _keepAliveTimer?.Dispose();
                _keepAliveTimer = null;
            }
        }

        private async void OnKeepAliveTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (!_keepAliveEnabled || _multiDeviceManager == null) return;
            try
            {
                long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _multiDeviceManager.SendKeepAliveOnAllDevices(ts);
            }
            catch { }
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

        private void StopStreamingButton_Click(object sender, RoutedEventArgs e)
        {
            _streamingCancellationSource?.Cancel();

        }







        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            StopVideoPlaybackTimer(disposeState: true);
            StopKeepAliveTimer();
            _streamingCancellationSource?.Cancel();
            _configSequenceCts?.Cancel();

            try { _liveUpdateTimer.Stop(); } catch { }
            try { _autoMoveTimer.Stop(); } catch { }
            try { _metrics.Dispose(); } catch { }
        }

        private async Task<bool> EnhancedRealTimeDisplayDemo(DisplayController controller, string imageDirectory, int cycleCount, CancellationToken token)
        {
            try
            {
                var images = Directory.Exists(imageDirectory)
                    ? Directory.GetFiles(imageDirectory)
                        .Where(f => new[] { ".jpg", ".jpeg", ".png", ".bmp" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                        .ToList()
                    : new List<string>();

                if (images.Count == 0) return false;

                var wake = await controller.SendCmdDisplayInSleepWithResponse(false);
                if (wake?.IsSuccess != true) return false;
                await Task.Delay(1200, token);

                var enable = await controller.SendCmdRealTimeDisplayWithResponse(true);
                if (enable?.IsSuccess != true) return false;

                await controller.SendCmdBrightnessWithResponse(80);
                await controller.SendCmdSetKeepAliveTimerWithResponse(60);
                await controller.SendCmdKeepAliveWithResponse(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                byte transferId = 0;
                for (int c = 0; c < cycleCount && !token.IsCancellationRequested; c++)
                {
                    foreach (var img in images)
                    {
                        if (token.IsCancellationRequested) break;
                        controller.SendFileFromDisk(img, transferId);
                        UpdateStreamingImageViewer(img);
                        await Task.Delay(1000, token);
                        transferId++;
                        if (transferId > 59) transferId = 4;
                    }
                }
                await controller.SendCmdRealTimeDisplayWithResponse(false);
                return !token.IsCancellationRequested;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                Console.WriteLine($"Realtime failed: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RunOfflineSuspendDemo(DisplayController controller)
        {
            try
            {
                if (!await controller.SetSuspendModeWithResponse()) return false;
                var candidates = new[]
                {
                    @"E:\github\CMDevicesManager\HidProtocol\resources",
                    @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"HidProtocol","resources")
                };
                List<string> files = new();
                foreach (var basePath in candidates)
                {
                    var f = new[]
                    {
                        Path.Combine(basePath,"suspend_0.jpg"),
                        Path.Combine(basePath,"suspend_1.jpg"),
                        Path.Combine(basePath,"suspend_2.mp4")
                    }.Where(File.Exists).ToList();
                    if (f.Any()) { files.AddRange(f); break; }
                }
                if (files.Any())
                {
                    if (!await controller.SendMultipleSuspendFilesWithResponse(files, 2))
                        return false;
                }
                await controller.SendCmdBrightnessWithResponse(80);
                await controller.SendCmdSetKeepAliveTimerWithResponse(5);
                await Task.Delay(5000); // shorter wait
                return await controller.ClearSuspendModeWithResponse();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Offline demo failed: {ex.Message}");
                return false;
            }
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
                    await foreach (var frame in VideoConverter.ExtractMp4FramesToJpegRealTimeAsync(mp4FilePath, 90, token))
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
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;

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
                    Filter = "Supported Media|*.mp4;*.jpg;*.jpeg",
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
            try
            {
                SetLoadingState(true, $"Adding media to slot {slotIndex + 1}...");

                // Need to rename the file name start suspend_1.jpg, suspend_2.mp4, etc.
                var fileExtension = Path.GetExtension(filePath).ToLower();
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

                File.Copy(filePath, newFilePath, true);

                // Update offline media data service if available
                if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                {
                    var offlineDataService = ServiceLocator.OfflineMediaDataService;
                    offlineDataService.AddSuspendMediaFile(
                        _deviceInfo.SerialNumber,
                        newFileName,
                        filePath,
                        slotIndex,
                        transferId: (byte)(slotIndex + 1)
                    );
                    Logger.Info($"Added media file to offline data: {newFileName}");
                }

                // Transfer file to device using suspend media functionality
                var results = await _hidDeviceService.SendMultipleSuspendFilesAsync(
                    new List<string> { newFilePath },
                    startingTransferId: (byte)(slotIndex + 1));

                var successCount = results.Values.Count(r => r);

                if (successCount > 0)
                {
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
                                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
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
                                    textBlock.Text = "Video Media";
                                    textBlock.Foreground = new SolidColorBrush(Colors.LightCoral);
                                }
                                else if (IsImageFile(fileExtension))
                                {
                                    textBlock.Text = "Image Media";
                                    textBlock.Foreground = new SolidColorBrush(Colors.LightGreen);
                                }
                                else
                                {
                                    textBlock.Text = "Media File";
                                    textBlock.Foreground = new SolidColorBrush(Colors.LightBlue);
                                }
                            }
                            else
                            {
                                textBlock.Text = "Device Media";
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
                            textBlock.Text = "Add Media";
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
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;

            var button = sender as Button;
            if (button?.Tag is string tagStr && int.TryParse(tagStr, out int slotIndex))
            {
                try
                {
                    SetLoadingState(true, $"Removing media from slot {slotIndex + 1}...");

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
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;

            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Multiple Media Files",
                    Filter = "Supported Media|*.mp4;*.jpg;*.jpeg;*.png;*.gif;*.bmp|" +
                            "Video Files|*.mp4|" +
                            "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp|" +
                            "All Files|*.*",
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = true
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var filePaths = openFileDialog.FileNames.Take(5).ToList(); // Limit to 5 files

                    SetLoadingState(true, "Adding multiple media files...");

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
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;

            try
            {
                SetLoadingState(true, "Clearing all media files...");

                // Release all image sources first to prevent file locks
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
                            Logger.Warn($"Failed to release image source for slot {slotNumber}: {ex.Message}");
                        }
                    }
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
        private void SetLoadingState(bool isLoading, string message = "Loading...")
        {
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


    }
}