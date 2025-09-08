using CMDevicesManager.Utilities;
using HID.DisplayController;
using HidApi;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Timer = System.Timers.Timer;

// NEW: bring in config models + enum alias
using CMDevicesManager.Pages; // contains CanvasConfiguration / ElementConfiguration
using LiveInfoKindAlias = CMDevicesManager.Pages.DeviceConfigPage.LiveInfoKind;
using CMDevicesManager.Services;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;

namespace CMDevicesManager.Pages
{
    public partial class DevicePlayModePage : Page
    {
        // 在类字段区域新增
        private enum PlayMode
        {
            RealtimeConfig,
            OfflineVideo
        }
        private PlayMode _currentPlayMode = PlayMode.RealtimeConfig;
        private string? _selectedVideoPath;
        private bool _isVideoStreaming;

        // Config 序列模型
        public class PlayConfigItem : INotifyPropertyChanged
        {
            private int _durationSeconds;
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
        private readonly ISystemMetricsService _metrics = new RealSystemMetricsService();
        private readonly System.Windows.Threading.DispatcherTimer _liveUpdateTimer;
        private readonly List<(TextBlock Text, LiveInfoKindAlias Kind, string? DateFormat)> _liveDynamicItems = new();

        // Base folder (same logic as DeviceConfigPage)
        private readonly string _outputFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CMDevicesManager");

        public DevicePlayModePage()
        {
            InitializeComponent();
            InitializeDisplayControls();
            InitConfigSequence();
            _currentPlayMode = PlayMode.RealtimeConfig;
            UpdatePlayModeUI();

            // NEW: live timer
            _liveUpdateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _liveUpdateTimer.Tick += LiveUpdateTimer_Tick;
            _liveUpdateTimer.Start();
        }

        private void LiveUpdateTimer_Tick(object? sender, EventArgs e)
        {
            double cpu = _metrics.GetCpuUsagePercent();
            double gpu = _metrics.GetGpuUsagePercent();
            var now = DateTime.Now;

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
        }

        private void UpdatePlayModeUI()
        {
            bool isRealtime = _currentPlayMode == PlayMode.RealtimeConfig;

            ConfigsContainer.Visibility = isRealtime ? Visibility.Visible : Visibility.Collapsed;
            VideoContainer.Visibility = isRealtime ? Visibility.Collapsed : Visibility.Visible;

            ConfigActionButtons.Visibility = isRealtime ? Visibility.Visible : Visibility.Collapsed;
            VideoActionButtons.Visibility = isRealtime ? Visibility.Collapsed : Visibility.Visible;

            PlayContentTitleText.Text = isRealtime ? "Play Content" : "Video Playback";
            PlayContentHintText.Text = isRealtime
                ? "Single config → continuous. Multiple configs → loop with per-item duration."
                : "Select an MP4 file and start playback (loop until stopped).";

            // 还原按钮状态
            if (isRealtime)
            {
                StartSequenceButton.IsEnabled = !_isConfigSequenceRunning && ConfigSequence.Count > 0;
                StopSequenceButton.IsEnabled = _isConfigSequenceRunning;
            }
            else
            {
                StartVideoButton.IsEnabled = !_isVideoStreaming && !string.IsNullOrEmpty(_selectedVideoPath);
                StopVideoButton.IsEnabled = _isVideoStreaming;
                ClearVideoButton.IsEnabled = !_isVideoStreaming && !string.IsNullOrEmpty(_selectedVideoPath);
            }

            UpdateModeSelectionVisuals();
        }

        // 修改播放模式按钮事件（只做模式切换 + 停止当前模式）
        private void RealtimeStreamingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlayMode == PlayMode.RealtimeConfig) return;

            if (_isVideoStreaming)
            {
                //ShowStatusMessage("Stopping video playback...");
                _streamingCancellationSource?.Cancel();
                _isVideoStreaming = false;
            }
            _currentPlayMode = PlayMode.RealtimeConfig;
            UpdatePlayModeUI();
            //ShowStatusMessage("Switched to real-time (config sequence) mode.");
        }
        private void UpdateModeSelectionVisuals()
        {
            if (RealtimeStreamingButton == null || Mp4StreamingButton == null) return;
            RealtimeStreamingButton.IsChecked = _currentPlayMode == PlayMode.RealtimeConfig;
            Mp4StreamingButton.IsChecked = _currentPlayMode == PlayMode.OfflineVideo;
        }


        private void Mp4StreamingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlayMode == PlayMode.OfflineVideo) return;

            if (_isConfigSequenceRunning)
            {
                //ShowStatusMessage("Stopping config sequence...");
                _configSequenceCts?.Cancel();
                _isConfigSequenceRunning = false;
            }
            _currentPlayMode = PlayMode.OfflineVideo;
            UpdatePlayModeUI();
            //ShowStatusMessage("Switched to video offline playback mode.");
        }

        // 选择视频
        private void SelectVideoButton_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select MP4 Video",
                Filter = "MP4 Video (*.mp4)|*.mp4|All Files|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                _selectedVideoPath = ofd.FileName;
                SelectedVideoNameText.Text = System.IO.Path.GetFileName(_selectedVideoPath);
                VideoInfoText.Text = "Ready. Click Start to begin streaming frames.";
                StartVideoButton.IsEnabled = true;
                ClearVideoButton.IsEnabled = true;
                //ShowStatusMessage($"Video selected: {SelectedVideoNameText.Text}");
            }
        }

        // 清除视频选择
        private void ClearVideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isVideoStreaming) return;
            _selectedVideoPath = null;
            SelectedVideoNameText.Text = "(None)";
            VideoInfoText.Text = "Please click 'Select Video' to choose an MP4 file.";
            StartVideoButton.IsEnabled = false;
            ClearVideoButton.IsEnabled = false;
        }

        // 启动视频播放
        private async void StartVideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedVideoPath))
            {
                //ShowErrorMessage("No video selected.");
                return;
            }
            if (_isVideoStreaming)
            {
                //ShowErrorMessage("Video already streaming.");
                return;
            }
            if (_multiDeviceManager == null)
            {
                _multiDeviceManager = new MultiDeviceManager(0x2516, 0x0228);
                _multiDeviceManager.StartMonitoring();
            }

            var devices = _multiDeviceManager.GetActiveControllers();
            if (devices.Count == 0)
            {
                //ShowErrorMessage("No devices connected.");
                return;
            }

            _isVideoStreaming = true;
            _streamingCancellationSource = new CancellationTokenSource();
            UpdatePlayModeUI();
            //ShowStatusMessage($"Starting video playback: {SelectedVideoNameText.Text}");

            try
            {
                StartKeepAliveTimer();
                int frameDelayMs = 33; // 默认 ~30fps, 可根据实际帧率改进
                var results = await _multiDeviceManager.ExecuteOnAllDevices(async controller =>
                {
                    try
                    {
                        bool ok = await SendMp4DataWithAdvancedDeviceSync(
                            controller,
                            _selectedVideoPath!,
                            frameDelayMs,
                            (frameData, frameIndex, transferId) =>
                            {
                                try { UpdateStreamingImageViewerFromFrameData(frameData); }
                                catch { /* ignore */ }
                            },
                            cycleCount: int.MaxValue, // 无限循环，直到外部取消
                            _streamingCancellationSource.Token);
                        return ok;
                    }
                    catch
                    {
                        return false;
                    }
                }, timeout: TimeSpan.FromHours(6));
                int okCount = results.Values.Count(v => v);
               
            }
            catch (OperationCanceledException)
            {
               // ShowStatusMessage("Video playback stopped.");
            }
            catch (Exception ex)
            {
               // ShowErrorMessage("Video playback failed: " + ex.Message);
            }
            finally
            {
                StopKeepAliveTimer();
                _streamingCancellationSource?.Dispose();
                _streamingCancellationSource = null;
                _isVideoStreaming = false;
                UpdatePlayModeUI();
            }
        }

        // 停止视频播放
        private void StopVideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isVideoStreaming)
            {
                _streamingCancellationSource?.Cancel();
                //ShowStatusMessage("Stopping video...");
            }
        }

        // 在 UpdateButtonStates 内（如果保留）增加模式适配（可选）
        private void UpdateButtonStates()
        {
            // 原有配置（若之前为空可忽略）
            if (_currentPlayMode == PlayMode.RealtimeConfig)
            {
                StartSequenceButton.IsEnabled = !_isConfigSequenceRunning && ConfigSequence.Count > 0;
                StopSequenceButton.IsEnabled = _isConfigSequenceRunning;
            }
            else
            {
                StartVideoButton.IsEnabled = !_isVideoStreaming && !string.IsNullOrEmpty(_selectedVideoPath);
                StopVideoButton.IsEnabled = _isVideoStreaming;
                ClearVideoButton.IsEnabled = !_isVideoStreaming && !string.IsNullOrEmpty(_selectedVideoPath);
            }
        }

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
        private void InitConfigSequence()
        {
            DataContext = this;
            ConfigSequence.CollectionChanged += (_, __) =>
            {
                Raise(nameof(IsMultiConfig));
                // 自动给刚添加的单个配置设为 5 秒（UI 显示时可能禁用）
                if (ConfigSequence.Count == 1)
                {
                    var item = ConfigSequence[0];
                    if (item.DurationSeconds <= 0) item.DurationSeconds = 5;
                }
            };
        }

        // 在构造函数结尾处调用
        // public DevicePlayModePage() { ... InitConfigSequence(); }

        private void AddConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select a configuration file",
                Filter = "Config Files (*.json;*.cfg)|*.json;*.cfg|All Files|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(ofd.FileName);
                ConfigSequence.Add(new PlayConfigItem
                {
                    DisplayName = name,
                    FilePath = ofd.FileName,
                    DurationSeconds = 5
                });

                // 新增：自动开始播放序列（无需用户再点 Start）
                if (_currentPlayMode == PlayMode.RealtimeConfig && !_isConfigSequenceRunning && ConfigSequence.Count > 0)
                {
                    StartConfigSequence(); // 异步 fire-and-forget
                }
            }
        }
        // 提取原 StartSequenceButton_Click 逻辑，供按钮和自动触发共同使用
        private async void StartConfigSequence()
        {
            if (_isConfigSequenceRunning) return;
            if (ConfigSequence.Count == 0) return;

            _isConfigSequenceRunning = true;
            _configSequenceCts = new CancellationTokenSource();
            StartSequenceButton.IsEnabled = false;
            StopSequenceButton.IsEnabled = true;

            try
            {
                await RunConfigSequenceAsync(_configSequenceCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _isConfigSequenceRunning = false;
                _configSequenceCts?.Dispose();
                _configSequenceCts = null;
                StartSequenceButton.IsEnabled = true;
                StopSequenceButton.IsEnabled = false;
            }
        }

        private void RemoveConfig_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlayConfigItem item)
            {
                ConfigSequence.Remove(item);
            }
        }

        private void ClearConfigsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isConfigSequenceRunning)
            {
                //ShowErrorMessage("Stop sequence first.");
                return;
            }
            ConfigSequence.Clear();
            DesignCanvas.Children.Clear();
            _liveDynamicItems.Clear();
        }

        private void MoveConfigUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlayConfigItem item)
            {
                int idx = ConfigSequence.IndexOf(item);
                if (idx > 0) ConfigSequence.Move(idx, idx - 1);
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

        private void ApplyConfigurationToPreview(CanvasConfiguration cfg)
        {
            _liveDynamicItems.Clear();
            DesignCanvas.Children.Clear();

            // Background color
            try
            {
                if (!string.IsNullOrWhiteSpace(cfg.BackgroundColor))
                {
                    var brush = (Brush)new BrushConverter().ConvertFromString(cfg.BackgroundColor);
                    BgColorRect.Fill = brush;
                }
            }
            catch
            {
                BgColorRect.Fill = Brushes.Black;
            }

            // Background image
            if (!string.IsNullOrWhiteSpace(cfg.BackgroundImagePath))
            {
                string resolved = ResolveRelativePath(cfg.BackgroundImagePath);
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
                        BgImage.Opacity = cfg.BackgroundImageOpacity <= 0 ? 1.0 : cfg.BackgroundImageOpacity;
                    }
                    catch
                    {
                        BgImage.Source = null;
                    }
                }
                else
                {
                    BgImage.Source = null;
                }
            }
            else
            {
                BgImage.Source = null;
            }

            foreach (var elem in cfg.Elements.OrderBy(e => e.ZIndex))
            {
                FrameworkElement? content = null;

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
                            if (elem.LiveKind.HasValue)
                            {
                                var kind = elem.LiveKind.Value;
                                var tb = new TextBlock
                                {
                                    FontSize = elem.FontSize ?? 18,
                                    FontWeight = FontWeights.SemiBold
                                };
                                tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

                                // Initial text
                                switch (kind)
                                {
                                    case LiveInfoKindAlias.CpuUsage:
                                        tb.Text = $"CPU {Math.Round(_metrics.GetCpuUsagePercent())}%";
                                        break;
                                    case LiveInfoKindAlias.GpuUsage:
                                        tb.Text = $"GPU {Math.Round(_metrics.GetGpuUsagePercent())}%";
                                        break;
                                    case LiveInfoKindAlias.DateTime:
                                        tb.Text = DateTime.Now.ToString(string.IsNullOrWhiteSpace(elem.DateFormat) ? "yyyy-MM-dd HH:mm:ss" : elem.DateFormat);
                                        break;
                                    default:
                                        tb.Text = "Live";
                                        break;
                                }

                                // For play mode we simplify usage visuals to text.
                                ApplyTextColors(tb, elem.UseTextGradient, elem.TextColor, elem.TextColor2);

                                content = tb;
                                _liveDynamicItems.Add((tb, kind, elem.DateFormat));
                            }
                            break;
                        }
                    case "Image":
                        {
                            if (!string.IsNullOrEmpty(elem.ImagePath))
                            {
                                string resolved = ResolveRelativePath(elem.ImagePath);
                                if (File.Exists(resolved))
                                {
                                    try
                                    {
                                        var img = new Image { Stretch = Stretch.Uniform };
                                        var bmp = new BitmapImage();
                                        bmp.BeginInit();
                                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                                        bmp.UriSource = new Uri(resolved, UriKind.Absolute);
                                        bmp.EndInit();
                                        bmp.Freeze();
                                        img.Source = bmp;
                                        content = img;
                                    }
                                    catch { }
                                }
                            }
                            break;
                        }
                    case "Video":
                        {
                            // Placeholder (extend to real playback if required)
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
                            content = tb;
                            break;
                        }
                }

                if (content != null)
                {
                    var host = new Border
                    {
                        Child = content,
                        Opacity = elem.Opacity <= 0 ? 1.0 : elem.Opacity,
                        BorderThickness = new Thickness(0)
                    };

                    var tg = new TransformGroup();
                    tg.Children.Add(new ScaleTransform(elem.Scale <= 0 ? 1.0 : elem.Scale, elem.Scale <= 0 ? 1.0 : elem.Scale));
                    tg.Children.Add(new TranslateTransform(elem.X, elem.Y));
                    host.RenderTransform = tg;
                    host.RenderTransformOrigin = new Point(0.5, 0.5);

                    Canvas.SetZIndex(host, elem.ZIndex);
                    DesignCanvas.Children.Add(host);
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
            StopKeepAliveTimer();
            _streamingCancellationSource?.Cancel();

            // NEW: dispose metrics + stop timer
            try { _liveUpdateTimer.Stop(); } catch { }
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
    }
}