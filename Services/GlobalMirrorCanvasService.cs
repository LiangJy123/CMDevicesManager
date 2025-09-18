using CMDevicesManager.Helper; // add if not present
using CMDevicesManager.Pages;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static CMDevicesManager.Pages.DeviceConfigPage;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control; // << 新增
using Image = System.Windows.Controls.Image;
using MessageBox = System.Windows.MessageBox;
using Panel = System.Windows.Controls.Panel;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;
using System.Threading;
using System.Threading.Tasks;
using CMDevicesManager.Utilities;   // for VideoConverter / VideoFrameData

namespace CMDevicesManager.Services
{
    public sealed class GlobalMirrorCanvasService : IDisposable
    {
        private static readonly Lazy<GlobalMirrorCanvasService> _lazy = new(() => new GlobalMirrorCanvasService());
        public static GlobalMirrorCanvasService Instance => _lazy.Value;

        private FrameworkElement? _rootVisual;
        private Rectangle? _bgRect;
        private Image? _bgImage;
        private Canvas? _designCanvas;

        private readonly CanvasRenderEngine _engine = CanvasRenderEngine.Instance;
        private CanvasRenderEngine.RenderResult? _lastResult;
        private CanvasConfiguration? _lastConfig;

        private readonly ISystemMetricsService _metrics = RealSystemMetricsService.Instance;
        private readonly DispatcherTimer _metricsTimer;
        private readonly DispatcherTimer _moveTimer;

        private double _moveSpeed = 100;
        private int _canvasSize = 512;
        private DateTime _lastMoveTick = DateTime.Now;

        private DispatcherTimer? _videoTimer;
        private List<VideoFrameData>? _videoFrames;
        private int _videoFrameIndex;
        private Image? _videoImage;
        private Border? _videoBorder;
        private CancellationTokenSource? _videoLoadCts;
        private bool _initialized;
        private bool _disposed;
        private string _outputRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CMDevicesManager");

        // ====== 自动截图相关字段 ======
        private bool _autoSnapshotEnabled;
        private string _autoSnapshotDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AutoSnapshots");
        private long _autoSnapshotSeq;
        private bool _snapshotInProgress;
        private DateTime _lastAutoSnapshot = DateTime.MinValue;
        private TimeSpan _autoSnapshotInterval = TimeSpan.FromMilliseconds(50); // 50ms

        private bool _hasActiveContent;
        public bool HasActiveContent => _hasActiveContent;

        private List<SequenceRuntimeItem>? _sequenceItems;
        private int _currentSequenceIndex = -1;
        private DispatcherTimer? _sequenceSequentialTimer;
        private DispatcherTimer? _sequencePollingTimer;
        private DateTime _sequenceStartUtc;
        private bool _sequenceUsingAbsoluteTimes;
        private bool _sequenceModeActive;

        private sealed class SequenceRuntimeItem
        {
            public string FilePath { get; set; } = "";
            public CanvasConfiguration? Config { get; set; }
            public TimeSpan Start { get; set; }
            public TimeSpan End { get; set; }          // 仅绝对模式使用
            public TimeSpan Duration { get; set; }     // 顺序模式或备用
            public override string ToString() => $"{Path.GetFileName(FilePath)} Start={Start} Dur={Duration} End={End}";
        }
        // ====== 移动调试日志控制 ======
        private bool _movementDebugLogging = false; // 默认关闭
        // 调试选项：强制为移动元素加可见背景/边框
        private bool _forceDebugVisualForMoving = false;
        public void EnableForceDebugVisual(bool enable)
        {
            _forceDebugVisualForMoving = enable;
            Logger.Info($"[MoveDbg] ForceDebugVisual {(enable ? "ON" : "OFF")}");
        }

        private GlobalMirrorCanvasService()
        {
            _metricsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _metricsTimer.Tick += MetricsTimer_Tick;

            _moveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _moveTimer.Tick += MoveTimer_Tick;
        }

        // 对外开放开启/关闭移动调试日志
        public void EnableMovementDebugLogging(bool enable)
        {
            _movementDebugLogging = enable;
            Logger.Info($"[MoveDbg] Movement debug logging {(enable ? "ENABLED" : "DISABLED")}");
        }

        private void ApplyDebugVisual(Border border)
        {
            if (!_forceDebugVisualForMoving) return;
            if (border.Background == null)
                border.Background = new SolidColorBrush(Color.FromArgb(160, 255, 0, 0)); // 半透明红
            if (border.BorderBrush == null)
            {
                border.BorderBrush = Brushes.Yellow;
                border.BorderThickness = new Thickness(2);
            }
        }

        public void Initialize(FrameworkElement rootVisual, Rectangle bgRect, Image bgImage, Canvas designCanvas)
        {
            _rootVisual = rootVisual;
            _bgRect = bgRect;
            _bgImage = bgImage;
            _designCanvas = designCanvas;
            _initialized = true;
        }

        private static string NormalizeColorOrWhite(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "#FFFFFF";
            input = input.Trim();
            if (input.StartsWith("#"))
            {
                if (input.Length == 7 || input.Length == 9 || input.Length == 4 || input.Length == 5)
                    return input;
                return "#FFFFFF";
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(input, "^[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$"))
                return "#" + input;
            return "#FFFFFF";
        }

        public void LoadInitialFromGlobalSequence()
        {
            if (!_initialized) return;
            StopSequencePlayback();  // 先停掉旧的

            try
            {
                var seqPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                           "CMDevicesManager", "globalconfig1.json");
                if (!File.Exists(seqPath))
                {
                    _hasActiveContent = false;
                    ClearToBlank();
                    return;
                }

                var json = File.ReadAllText(seqPath);
                var seq = JsonSerializer.Deserialize<GlobalConfigSequence>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (seq?.Items == null || seq.Items.Count == 0)
                {
                    _hasActiveContent = false;
                    ClearToBlank();
                    return;
                }

                // 新：构建序列
                BuildSequence(seq.Items);
                if (_sequenceItems == null || _sequenceItems.Count == 0)
                {
                    _hasActiveContent = false;
                    ClearToBlank();
                    return;
                }

                if (_sequenceItems.Count == 1)
                {
                    // 单一配置仍走原逻辑
                    var only = _sequenceItems[0];
                    ApplyConfig(only.Config!);
                    _sequenceModeActive = false;
                    return;
                }

                // 启动序列播放
                StartSequencePlayback();
            }
            catch (Exception ex)
            {
                Logger.Error("LoadInitialFromGlobalSequence failed", ex);
                _hasActiveContent = false;
                ClearToBlank();
            }
        }

        private sealed class GlobalConfigSequence
        {
            public List<GlobalConfigEntry> Items { get; set; } = new();
        }
        private sealed class GlobalConfigEntry
        {
            public string FilePath { get; set; } = "";

            // 可选：绝对开始秒（相对于序列开始）。若任一条目提供 StartSeconds，则进入绝对时间模式。
            public double? StartSeconds { get; set; }

            // 可选：持续秒数。绝对模式下用于计算 End（若未给，推断为直到下一个 Start 或默认值）。
            public double? DurationSeconds { get; set; }

            // 备用：支持 Duration / Start 其它命名（兼容可能的不同写法）
            public double? Start { get; set; }
            public double? Duration { get; set; }
        }
        private void BuildSequence(List<GlobalConfigEntry> rawItems)
        {
            _sequenceItems = null;
            if (rawItems == null || rawItems.Count == 0) return;

            bool anyStart = rawItems.Any(i => (i.StartSeconds ?? i.Start).HasValue);
            var list = new List<SequenceRuntimeItem>();

            // 预加载配置
            foreach (var entry in rawItems)
            {
                if (string.IsNullOrWhiteSpace(entry.FilePath)) continue;
                if (!File.Exists(entry.FilePath)) continue;

                CanvasConfiguration? cfg = null;
                try
                {
                    var cfgJson = File.ReadAllText(entry.FilePath);
                    cfg = JsonSerializer.Deserialize<CanvasConfiguration>(cfgJson, new JsonSerializerOptions
                    {
                        Converters = { new JsonStringEnumConverter() }
                    });
                }
                catch
                {
                    continue;
                }

                if (cfg == null) continue;
                cfg.BackgroundColor = NormalizeColorOrWhite(cfg.BackgroundColor);

                double? startRaw = entry.StartSeconds ?? entry.Start;
                double? durRaw = entry.DurationSeconds ?? entry.Duration;

                list.Add(new SequenceRuntimeItem
                {
                    FilePath = entry.FilePath,
                    Config = cfg,
                    Start = anyStart
                        ? TimeSpan.FromSeconds(startRaw ?? 0)
                        : TimeSpan.Zero, // 顺序模式中稍后再计算
                    Duration = TimeSpan.FromSeconds(durRaw.HasValue && durRaw.Value > 0 ? durRaw.Value : 5) // 默认 5 秒
                });
            }

            if (list.Count == 0) return;

            if (anyStart)
            {
                // 绝对模式：按 Start 排序
                list = list.OrderBy(i => i.Start).ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    var cur = list[i];
                    if (i < list.Count - 1)
                    {
                        var next = list[i + 1];
                        // 如果显式 Duration 给出则用，未给出则使用直到下一个 start 的差
                        if (cur.Duration.TotalSeconds <= 0.1)
                            cur.Duration = next.Start - cur.Start;
                        cur.End = cur.Start + cur.Duration;
                    }
                    else
                    {
                        // 最后一段：如果没有显式 Duration 且下一个不存在 -> 使用已有 Duration 或默认 5
                        if (cur.End == TimeSpan.Zero)
                            cur.End = cur.Start + (cur.Duration.TotalSeconds > 0.1 ? cur.Duration : TimeSpan.FromSeconds(5));
                    }
                }
                _sequenceUsingAbsoluteTimes = true;
            }
            else
            {
                // 顺序模式：依次累加
                TimeSpan cursor = TimeSpan.Zero;
                foreach (var item in list)
                {
                    item.Start = cursor;
                    item.End = cursor + item.Duration;
                    cursor = item.End;
                }
                _sequenceUsingAbsoluteTimes = false;
            }

            _sequenceItems = list;
            Logger.Info($"[Sequence] Built sequence items={_sequenceItems.Count} Mode={(_sequenceUsingAbsoluteTimes ? "Absolute" : "Sequential")}");
            foreach (var it in _sequenceItems)
                Logger.Info($"[Sequence] {it}");
        }
        private void StartSequencePlayback()
        {
            if (_sequenceItems == null || _sequenceItems.Count == 0) return;
            _sequenceModeActive = true;
            _currentSequenceIndex = -1;
            _sequenceStartUtc = DateTime.UtcNow;

            if (_sequenceUsingAbsoluteTimes)
            {
                // 轮询模式：500ms 检查当前该显示哪个
                _sequencePollingTimer?.Stop();
                _sequencePollingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _sequencePollingTimer.Tick += SequencePollingTimer_Tick;
                _sequencePollingTimer.Start();
            }
            else
            {
                // 顺序模式：使用逐段定时
                PlayNextSequential();
            }
        }

        private void StopSequencePlayback()
        {
            _sequenceModeActive = false;
            _sequencePollingTimer?.Stop();
            _sequencePollingTimer = null;
            _sequenceSequentialTimer?.Stop();
            _sequenceSequentialTimer = null;
            _currentSequenceIndex = -1;
        }

        private void SequencePollingTimer_Tick(object? sender, EventArgs e)
        {
            if (!_sequenceModeActive || !_sequenceUsingAbsoluteTimes || _sequenceItems == null) return;

            var elapsed = DateTime.UtcNow - _sequenceStartUtc;
            // 找到最后一个 Start <= elapsed < End
            var idx = _sequenceItems
                .Select((v, i) => new { v, i })
                .Where(x => elapsed >= x.v.Start && elapsed < x.v.End)
                .Select(x => x.i)
                .DefaultIfEmpty(-1)
                .Last();

            if (idx >= 0 && idx != _currentSequenceIndex)
                ApplySequenceIndex(idx);

            // 如果超过最后一个，循环（可按需求改为停止）
            if (elapsed > _sequenceItems.Last().End)
            {
                _sequenceStartUtc = DateTime.UtcNow;
                _currentSequenceIndex = -1;
            }
        }

        private void PlayNextSequential()
        {
            if (!_sequenceModeActive || _sequenceUsingAbsoluteTimes) return;
            if (_sequenceItems == null || _sequenceItems.Count == 0) return;

            int next = _currentSequenceIndex + 1;
            if (next >= _sequenceItems.Count)
            {
                // 循环播放
                next = 0;
            }

            ApplySequenceIndex(next);

            var dur = _sequenceItems[next].Duration;
            _sequenceSequentialTimer?.Stop();
            _sequenceSequentialTimer = new DispatcherTimer { Interval = dur <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : dur };
            _sequenceSequentialTimer.Tick += (s, e) => PlayNextSequential();
            _sequenceSequentialTimer.Start();
        }

        private void ApplySequenceIndex(int idx)
        {
            if (_sequenceItems == null || idx < 0 || idx >= _sequenceItems.Count) return;
            _currentSequenceIndex = idx;
            var item = _sequenceItems[idx];
            if (item.Config != null)
            {
                Logger.Info($"[Sequence] Apply index={idx} file={Path.GetFileName(item.FilePath)} Start={item.Start} Dur={item.Duration}");
                ApplyConfig(item.Config);
            }
        }


        public void ApplyConfig(CanvasConfiguration cfg)
        {
            if (!_initialized || _bgRect == null || _bgImage == null || _designCanvas == null) return;

            cfg.BackgroundColor = NormalizeColorOrWhite(cfg.BackgroundColor);
            _lastConfig = cfg;

            var ctx = new CanvasRenderEngine.RenderContext(_bgRect, _bgImage, _designCanvas, _outputRoot);
            _lastResult = _engine.Apply(cfg, ctx,
                getCpuPercent: () => _metrics.GetCpuUsagePercent(),
                getGpuPercent: () => _metrics.GetGpuUsagePercent());

            // 初始化温度文本（避免第一秒未刷新前显示空或旧值）
            //if (_lastResult != null)
            //{
            //    double cpuTemp = _metrics.GetCpuTemperature();
            //    double gpuTemp = _metrics.GetGpuTemperature();
            //    foreach (var u in _lastResult.UsageItems)
            //    {
            //        if (u.DisplayStyle == "Text")
            //        {
            //            if (u.Kind == LiveInfoKind.CpuTemperature)
            //                u.Text.Text = $"CPU {Math.Round(cpuTemp)}°C";
            //            else if (u.Kind == LiveInfoKind.GpuTemperature)
            //                u.Text.Text = $"GPU {Math.Round(gpuTemp)}°C";
            //        }
            //    }
            //}

            if (string.IsNullOrWhiteSpace(cfg.BackgroundColor))
                _bgRect.Fill = Brushes.White;
            if (_bgRect.Fill == null)
                _bgRect.Fill = Brushes.White;

            _canvasSize = _lastResult.CanvasSize;
            _moveSpeed = _lastResult.MoveSpeed;
            _lastMoveTick = DateTime.Now;

            _hasActiveContent = (cfg.Elements?.Count ?? 0) > 0;   // NEW

            StartTimers();
            SetupVideoElements(cfg);
        }

        public void ReapplyLast()
        {
            if (_lastConfig != null)
                ApplyConfig(_lastConfig);
        }

        public void ClearToBlank()
        {
            StopSequencePlayback();
            if (_designCanvas == null || _bgRect == null || _bgImage == null) return;
            StopVideoPlayback(disposeState: true);
            _designCanvas.Children.Clear();
            _bgRect.Fill = Brushes.White;
            _bgImage.Source = null;
            _lastResult = null;
            _lastConfig = null;
            _hasActiveContent = false;   // NEW
        }

        private void StopVideoPlayback(bool disposeState = false)
        {
            if (_videoTimer != null)
            {
                _videoTimer.Stop();
                _videoTimer.Tick -= VideoTimer_Tick;
                _videoTimer = null;
            }
            if (disposeState)
            {
                _videoFrames = null;
                _videoImage = null;
                _videoBorder = null;
                _videoFrameIndex = 0;
            }
        }

        private void StartVideoPlayback(double frameRate)
        {
            if (_videoFrames == null || _videoFrames.Count == 0 || _videoImage == null)
                return;

            StopVideoPlayback(disposeState: false); // only restart timer
            int interval = frameRate > 2 ? (int)(1000.0 / frameRate) : 33;
            _videoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
            _videoTimer.Tick += VideoTimer_Tick;
            _videoTimer.Start();
        }

        private void VideoTimer_Tick(object? sender, EventArgs e)
        {
            if (_videoFrames == null || _videoFrames.Count == 0 || _videoImage == null)
            {
                StopVideoPlayback();
                return;
            }
            _videoFrameIndex = (_videoFrameIndex + 1) % _videoFrames.Count;
            UpdateVideoImageSource(_videoFrames[_videoFrameIndex]);
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
                string full = relativeOrFull;
                if (!Path.IsPathRooted(full))
                {
                    full = Path.Combine(_outputRoot, relativeOrFull);
                    if (!Directory.Exists(full))
                        full = Path.Combine(_outputRoot, "Resources", relativeOrFull);
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
            catch { return null; }
        }

        private string ResolveRelativePath(string relativePath)
        {
            try
            {
                if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
                    return relativePath;

                string candidate = Path.Combine(_outputRoot, relativePath);
                if (File.Exists(candidate)) return candidate;

                candidate = Path.Combine(_outputRoot, "Resources", relativePath);
                if (File.Exists(candidate)) return candidate;

                foreach (var sub in new[] { "Images", "Backgrounds", "Videos" })
                {
                    candidate = Path.Combine(_outputRoot, "Resources", sub, relativePath);
                    if (File.Exists(candidate)) return candidate;
                }
                return relativePath;
            }
            catch { return relativePath; }
        }

        private void ApplyVideoElementTransform(Border host, ElementConfiguration elem)
        {
            var tg = new TransformGroup();
            var scale = new ScaleTransform(elem.Scale <= 0 ? 1.0 : elem.Scale, elem.Scale <= 0 ? 1.0 : elem.Scale);
            tg.Children.Add(scale);

            if (elem.MirroredX == true)
                tg.Children.Add(new ScaleTransform(-1, 1));
            if (elem.Rotation.HasValue && Math.Abs(elem.Rotation.Value) > 0.01)
                tg.Children.Add(new RotateTransform(elem.Rotation.Value));
            tg.Children.Add(new TranslateTransform(elem.X, elem.Y));
            host.RenderTransform = tg;
        }

        private async void SetupVideoElements(CanvasConfiguration cfg)
        {
            // Only handle a single video element for now
            var videoElem = cfg.Elements.FirstOrDefault(e => e.Type == "Video" && !string.IsNullOrEmpty(e.VideoPath));
            StopVideoPlayback(disposeState: true);
            _videoLoadCts?.Cancel();
            _videoLoadCts = new CancellationTokenSource();

            if (videoElem == null || _designCanvas == null) return;

            // Placeholder border
            var placeholder = new TextBlock
            {
                Text = "Loading Video...",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                Padding = new Thickness(8),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            placeholder.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

            var host = new Border
            {
                Child = placeholder,
                Opacity = videoElem.Opacity <= 0 ? 1.0 : videoElem.Opacity,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            ApplyVideoElementTransform(host, videoElem);
            Canvas.SetZIndex(host, videoElem.ZIndex);
            _designCanvas.Children.Add(host);
            _videoBorder = host;

            // Try cache first
            List<VideoFrameData>? frames = null;
            if (!string.IsNullOrEmpty(videoElem.VideoFramesCacheFolder))
                frames = LoadCachedVideoFramesFolder(videoElem.VideoFramesCacheFolder);

            if (frames != null && frames.Count > 0)
            {
                _videoFrames = frames;
                _videoFrameIndex = 0;
                _videoImage = new Image { Stretch = Stretch.Uniform };
                host.Child = _videoImage;
                UpdateVideoImageSource(frames[0]);

                double frameRate = 30;
                if (!string.IsNullOrEmpty(videoElem.VideoPath))
                {
                    var resolvedForInfo = ResolveRelativePath(videoElem.VideoPath);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var info = await VideoConverter.GetMp4InfoAsync(resolvedForInfo);
                            if (info != null && info.FrameRate > 1) frameRate = info.FrameRate;
                        }
                        catch { }
                        Application.Current.Dispatcher.Invoke(() => StartVideoPlayback(frameRate));
                    });
                }
                else
                {
                    StartVideoPlayback(frameRate);
                }
                return;
            }

            // Decode frames asynchronously
            if (!string.IsNullOrEmpty(videoElem.VideoPath))
            {
                var resolvedPath = ResolveRelativePath(videoElem.VideoPath);
                if (!File.Exists(resolvedPath)) return;

                try
                {
                    var token = _videoLoadCts.Token;
                    var collected = new List<VideoFrameData>();
                    double frameRate = 30;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await foreach (var frame in VideoConverter.ExtractMp4FramesToJpegRealTimeAsync(resolvedPath, 85, token))
                            {
                                collected.Add(frame);
                            }
                            if (collected.Count > 0 && !token.IsCancellationRequested)
                            {
                                var info = await VideoConverter.GetMp4InfoAsync(resolvedPath);
                                if (info != null && info.FrameRate > 1) frameRate = info.FrameRate;

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    if (_videoBorder != host) return; // config changed
                                    _videoFrames = collected;
                                    _videoFrameIndex = 0;
                                    _videoImage = new Image { Stretch = Stretch.Uniform };
                                    host.Child = _videoImage;
                                    UpdateVideoImageSource(collected[0]);
                                    StartVideoPlayback(frameRate);
                                });
                            }
                        }
                        catch { /* swallow */ }
                    }, token);
                }
                catch { }
            }
        }

        private void MetricsTimer_Tick(object? sender, EventArgs e) => UpdateMetricsOnce();

        private void UpdateMetricsOnce()
        {
            if (_lastResult == null) return;

            double cpu = _metrics.GetCpuUsagePercent();
            double gpu = _metrics.GetGpuUsagePercent();
            double cpuTemp = _metrics.GetCpuTemperature();
            double gpuTemp = _metrics.GetGpuTemperature();
            DateTime now = DateTime.Now;

            foreach (var item in _lastResult.UsageItems)
            {
                double raw = item.Kind switch
                {
                    LiveInfoKind.CpuUsage => cpu,
                    LiveInfoKind.GpuUsage => gpu,
                    LiveInfoKind.CpuTemperature => cpuTemp,
                    LiveInfoKind.GpuTemperature => gpuTemp,
                    _ => 0
                };

                bool isTemp = item.Kind == LiveInfoKind.CpuTemperature || item.Kind == LiveInfoKind.GpuTemperature;

                switch (item.DisplayStyle)
                {
                    case "ProgressBar":
                        if (item.BarFill != null)
                        {
                            double percent = Math.Clamp(raw, 0, 100);
                            item.BarFill.Width = item.BarTotalWidth * (percent / 100.0);

                            string prefix =
                                (item.Kind == LiveInfoKind.CpuUsage || item.Kind == LiveInfoKind.CpuTemperature) ? "CPU" :
                                (item.Kind == LiveInfoKind.GpuUsage || item.Kind == LiveInfoKind.GpuTemperature) ? "GPU" : "";

                            string valuePart = isTemp
                                ? $"{Math.Round(percent)}°C"
                                : $"{Math.Round(percent)}%";

                            item.Text.Text = string.IsNullOrEmpty(prefix)
                                ? valuePart
                                : $"{prefix} {valuePart}";

                            var mid = LerpColor(item.StartColor, item.EndColor, percent / 100.0);
                            item.Text.Foreground = new SolidColorBrush(mid);
                        }
                        break;

                    case "Gauge":
                        if (item.GaugeNeedleRotate != null)
                        {
                            double percent = Math.Clamp(raw, 0, 100);
                            item.GaugeNeedleRotate.Angle = GaugeRotationFromPercent(percent);
                            item.Text.Text = isTemp
                                ? $"{Math.Round(percent)}°C"
                                : $"{Math.Round(percent)}%";
                        }
                        break;

                    case "Text":
                        if (item.Kind == LiveInfoKind.DateTime)
                        {
                            string fmt = string.IsNullOrWhiteSpace(item.DateFormat)
                                ? "yyyy-MM-dd HH:mm:ss"
                                : item.DateFormat;
                            item.Text.Text = now.ToString(fmt);
                        }
                        else if (item.Kind == LiveInfoKind.CpuUsage)
                        {
                            item.Text.Text = $"CPU {Math.Round(cpu)}%";
                        }
                        else if (item.Kind == LiveInfoKind.GpuUsage)
                        {
                            item.Text.Text = $"GPU {Math.Round(gpu)}%";
                        }
                        else if (item.Kind == LiveInfoKind.CpuTemperature)
                        {
                            item.Text.Text = $"CPU {Math.Round(cpuTemp)}°C";
                        }
                        else if (item.Kind == LiveInfoKind.GpuTemperature)
                        {
                            item.Text.Text = $"GPU {Math.Round(gpuTemp)}°C";
                        }
                        break;
                }
            }
        }

        private void UpdateMovementOnce(double? dtOverrideSeconds = null)
        {
            if (_lastResult == null || !_lastResult.MovingDirections.Any() || _designCanvas == null) return;

            var now = DateTime.Now;
            double dt = dtOverrideSeconds ?? (now - _lastMoveTick).TotalSeconds;
            if (dt <= 0 && !dtOverrideSeconds.HasValue) dt = 0.016;
            if (!dtOverrideSeconds.HasValue)
                _lastMoveTick = now;

            foreach (var kv in _lastResult.MovingDirections.ToList())
            {
                var border = kv.Key;
                if (!_designCanvas.Children.Contains(border))
                {
                    _lastResult.MovingDirections.Remove(border);
                    continue;
                }
                var (vx, vy) = kv.Value;
                if (border.RenderTransform is not TransformGroup tg) continue;
                var translate = tg.Children.OfType<TranslateTransform>().LastOrDefault();
                var scale = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
                if (translate == null) continue;

                ApplyDebugVisual(border);

                bool frozen = dtOverrideSeconds.HasValue && dtOverrideSeconds.Value == 0.0;

                if (!frozen)
                {
                    double dx = vx * _moveSpeed * dt;
                    double dy = vy * _moveSpeed * dt;
                    translate.X += dx;
                    translate.Y += dy;
                }

                double w = border.ActualWidth * (scale?.ScaleX == 0 ? 1 : scale?.ScaleX ?? 1);
                double h = border.ActualHeight * (scale?.ScaleY == 0 ? 1 : scale?.ScaleY ?? 1);

                // 包装逻辑：如果完全跑到左边外面，放到右侧边缘内一点点，避免整块全在外面
                if (translate.X + w < 0)
                    translate.X = _canvasSize - 2; // 原来是 _canvasSize，改为进入 2px，确保可见
                else if (translate.X > _canvasSize)
                    translate.X = -w; // 另一方向保持

                if (translate.Y > _canvasSize) translate.Y = -h;
                else if (translate.Y + h < 0) translate.Y = _canvasSize;
            }

            LogMovingElements(dtOverrideSeconds.HasValue
                ? $"UpdateMovementOnce(dt={dtOverrideSeconds.Value})"
                : $"UpdateMovementOnce(real dt={dt:0.000})");

            DebugProbeMovingBorders("AfterMove");
        }

        private void MoveTimer_Tick(object? sender, EventArgs e)
        {
            if (_captureBeforeMove)
                TryAutoSnapshot();          // 先截旧位置（上一帧）——更容易留住部分可见内容

            UpdateMovementOnce();            // 推进一帧

            if (_lastResult != null && !_lastResult.MovingDirections.Any() && _moveTimer.IsEnabled)
                _moveTimer.Stop();

            if (!_captureBeforeMove)
                TryAutoSnapshot();          // 如果需要先移动再截（原行为）
        }

        // ====== 新增：开启 / 关闭自动截图的公共方法 ======
        public void StartAutoSnapshots(string? directory = null, int? intervalMs = null)
        {
            if (directory != null)
                _autoSnapshotDir = directory;
            if (intervalMs.HasValue && intervalMs.Value >= 10)
                _autoSnapshotInterval = TimeSpan.FromMilliseconds(intervalMs.Value);

            Directory.CreateDirectory(_autoSnapshotDir);
            _autoSnapshotSeq = 0;
            _lastAutoSnapshot = DateTime.MinValue;
            _autoSnapshotEnabled = true;
            Console.WriteLine($"[AutoSnap] Started -> dir={_autoSnapshotDir}, interval={_autoSnapshotInterval.TotalMilliseconds}ms");
        }

        public void StopAutoSnapshots()
        {
            _autoSnapshotEnabled = false;
            Console.WriteLine("[AutoSnap] Stopped");
        }

        // ====== 新增：尝试执行一次自动截图 ======
        private void TryAutoSnapshot()
        {
            if (!_autoSnapshotEnabled) return;
            if (_rootVisual == null) return;
            if (_snapshotInProgress) return;

            var now = DateTime.UtcNow;
            if (_lastAutoSnapshot != DateTime.MinValue &&
                (now - _lastAutoSnapshot) < _autoSnapshotInterval)
                return;

            _lastAutoSnapshot = now;
            _snapshotInProgress = true;
            try
            {
                string file = Path.Combine(
                    _autoSnapshotDir,
                    $"snap_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{_autoSnapshotSeq++:D6}.jpg");

                // 强制使用不受原始低透明度影响的截图
                SaveSnapshotInvisible(file, overlayHost: null, forceRootOpacity: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AutoSnap] Failed: " + ex.Message);
            }
            finally
            {
                _snapshotInProgress = false;
            }
        }

        public bool SaveSnapshotInvisible(string filePath, FrameworkElement? overlayHost = null)
        {
            return SaveSnapshotCore(filePath, overlayHost, forceRootOpacity: false);
        }

        public bool SaveSnapshotInvisible(string filePath, FrameworkElement? overlayHost, bool forceRootOpacity)
        {
            return SaveSnapshotCore(filePath, overlayHost, forceRootOpacity);
        }
            
        // 在 GlobalMirrorCanvasService 类里增加（放在其它字段附近）
        public enum SnapshotScaleMode
        {
            LogicalCanvas,   // 逻辑尺寸 (w,h) + 96 DPI（不放大）
            DevicePixels,    // 按显示器缩放放大像素 & DPI （清晰大图）
            ForceDpi         // 指定自定义 DPI（像素不变，仅写入元数据，不推荐）
        }

        private SnapshotScaleMode _snapshotScaleMode = SnapshotScaleMode.LogicalCanvas;
        private double _forcedDpi = 96;

        public void SetSnapshotScaleMode(SnapshotScaleMode mode, double forcedDpi = 96)
        {
            _snapshotScaleMode = mode;
            _forcedDpi = forcedDpi <= 0 ? 96 : forcedDpi;
        }

        public enum MovementCaptureMode
        {
            Freeze,      // 截图时不推进位置（当前默认行为），但会重置时间避免跳跃
            RealDelta,   // 使用真实经过时间推进一帧
            FixedDelta   // 使用指定固定 dt 推进
        }
        private MovementCaptureMode _movementCaptureMode = MovementCaptureMode.Freeze;
        private double _fixedSnapshotDeltaSeconds = 1.0 / 60.0;

        public void SetSnapshotMovementMode(MovementCaptureMode mode, double fixedDeltaSeconds = 1.0 / 60.0)
        {
            _movementCaptureMode = mode;
            if (fixedDeltaSeconds > 0) _fixedSnapshotDeltaSeconds = fixedDeltaSeconds;
        }

        // 替换 SaveSnapshotCore 方法
        private bool SaveSnapshotCore(string filePath, FrameworkElement? overlayHost, bool forceRootOpacity)
        {
            if (_rootVisual == null) return false;

            double? originalRootOpacity = null;
            bool hostProvided = overlayHost != null;
            bool hostWasHidden = false;
            Visibility prevHostVisibility = Visibility.Visible;
            double prevHostOpacity = 1.0;
            bool prevHostHitTest = true;

            try
            {
                // 外层显示
                if (hostProvided)
                {
                    prevHostVisibility = overlayHost!.Visibility;
                    prevHostOpacity = overlayHost.Opacity;
                    prevHostHitTest = overlayHost.IsHitTestVisible;

                    if (overlayHost.Visibility != Visibility.Visible)
                    {
                        hostWasHidden = true;
                        overlayHost.Visibility = Visibility.Visible;
                        overlayHost.Opacity = 0;
                        overlayHost.IsHitTestVisible = false;
                    }
                }

                if (forceRootOpacity)
                {
                    originalRootOpacity = _rootVisual.Opacity;
                    _rootVisual.Opacity = 1.0;
                }

                // 打开隐藏父级
                var changedParents = new List<FrameworkElement>();
                FrameworkElement? p = _rootVisual.Parent as FrameworkElement;
                while (p != null && p is not Window)
                {
                    if (p.Visibility != Visibility.Visible)
                    {
                        changedParents.Add(p);
                        p.Visibility = Visibility.Visible;
                    }
                    p = p.Parent as FrameworkElement;
                }

                // 尺寸（优先 Actual）
                double aw = _rootVisual.ActualWidth;
                double ah = _rootVisual.ActualHeight;
                if (aw <= 0 || ah <= 0)
                {
                    aw = double.IsNaN(_rootVisual.Width) || _rootVisual.Width <= 0 ? 512 : _rootVisual.Width;
                    ah = double.IsNaN(_rootVisual.Height) || _rootVisual.Height <= 0 ? 512 : _rootVisual.Height;
                }
                int logicalW = (int)Math.Ceiling(aw);
                int logicalH = (int)Math.Ceiling(ah);

                // 布局
                _rootVisual.Measure(new Size(logicalW, logicalH));
                _rootVisual.Arrange(new Rect(0, 0, logicalW, logicalH));
                _rootVisual.UpdateLayout();

                // 同步数据 & 冻结位置
                UpdateMetricsOnce();
                switch (_movementCaptureMode)
                {
                    case MovementCaptureMode.Freeze:
                        // 冻结一帧，但更新 _lastMoveTick 防止下一帧真实更新时发生大跳
                        UpdateMovementOnce(dtOverrideSeconds: 0.0);
                        _lastMoveTick = DateTime.Now;
                        break;
                    case MovementCaptureMode.RealDelta:
                        // 真实经过时间
                        UpdateMovementOnce(); // 内部会更新 _lastMoveTick
                        break;
                    case MovementCaptureMode.FixedDelta:
                        // 固定步长前进
                        UpdateMovementOnce(dtOverrideSeconds: _fixedSnapshotDeltaSeconds);
                        _lastMoveTick = DateTime.Now;
                        break;
                }
                ForceCanvasLayoutRefresh();
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                _rootVisual.UpdateLayout();

                // DPI & 像素策略
                double dpiX = 96;
                double dpiY = 96;
                int pixelW = logicalW;
                int pixelH = logicalH;

                var dpiInfo = VisualTreeHelper.GetDpi(_rootVisual);
                double scaleX = dpiInfo.DpiScaleX;
                double scaleY = dpiInfo.DpiScaleY;

                switch (_snapshotScaleMode)
                {
                    case SnapshotScaleMode.LogicalCanvas:
                        // 逻辑 1:1，不做任何放大
                        dpiX = 96;
                        dpiY = 96;
                        pixelW = logicalW;
                        pixelH = logicalH;
                        break;

                    case SnapshotScaleMode.DevicePixels:
                        // 按显示器缩放输出高分辨率
                        pixelW = (int)Math.Round(logicalW * scaleX);
                        pixelH = (int)Math.Round(logicalH * scaleY);
                        dpiX = 96 * scaleX;
                        dpiY = 96 * scaleY;
                        break;

                    case SnapshotScaleMode.ForceDpi:
                        // 不改变像素尺寸，只写入自定义 DPI 元数据（容易造成查看器缩放差异）
                        dpiX = _forcedDpi;
                        dpiY = _forcedDpi;
                        pixelW = logicalW;
                        pixelH = logicalH;
                        break;
                }

                var rtb = new RenderTargetBitmap(pixelW, pixelH, dpiX, dpiY, PixelFormats.Pbgra32);
                rtb.Render(_rootVisual);

                BitmapEncoder encoder = Path.GetExtension(filePath).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                    ".bmp" => new BmpBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };
                encoder.Frames.Add(BitmapFrame.Create(rtb));

                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                    encoder.Save(fs);

                foreach (var cp in changedParents)
                    cp.Visibility = Visibility.Hidden;

                if (hostProvided && hostWasHidden)
                {
                    overlayHost!.Visibility = prevHostVisibility;
                    overlayHost.Opacity = prevHostOpacity;
                    overlayHost.IsHitTestVisible = prevHostHitTest;
                }

                Logger.Info($"[MoveDbg] Snapshot saved: {filePath} mode={_snapshotScaleMode} logical=({logicalW}x{logicalH}) px=({pixelW}x{pixelH}) dpi=({dpiX:0.#},{dpiY:0.#})");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GlobalMirror] SaveSnapshotInvisible failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (originalRootOpacity.HasValue && _rootVisual != null)
                    _rootVisual.Opacity = originalRootOpacity.Value;
            }
        }

      
        public bool SaveSnapshot(string filePath)
        {
            if (_rootVisual == null) return false;
            try
            {
                bool madeHostVisible = false;
                FrameworkElement? host = _rootVisual.Parent as FrameworkElement;
                while (host != null && host is not Window)
                {
                    if (host.Visibility != Visibility.Visible)
                    {
                        host.Tag = "__TEMP_VIS_RESTORE__";
                        host.Visibility = Visibility.Visible;
                        madeHostVisible = true;
                    }
                    host = host.Parent as FrameworkElement;
                }

                bool rootWasHidden = _rootVisual.Visibility != Visibility.Visible;
                if (rootWasHidden)
                {
                    _rootVisual.Visibility = Visibility.Visible;
                }

                int w = (int)(_rootVisual.Width > 0 ? _rootVisual.Width : 512);
                int h = (int)(_rootVisual.Height > 0 ? _rootVisual.Height : 512);
                _rootVisual.Measure(new Size(w, h));
                _rootVisual.Arrange(new Rect(0, 0, w, h));
                _rootVisual.UpdateLayout();

                UpdateMetricsOnce();
                UpdateMovementOnce(dtOverrideSeconds: 0.0);

                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                _rootVisual.UpdateLayout();

                // 追加：单独强制刷新设计画布，确保文字 Glyph 生成
                ForceCanvasLayoutRefresh();
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

                LogMovingElements("Snapshot(visible beforeRender)");
                DebugProbeMovingBorders("Snapshot(visible beforeRender)");
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(_rootVisual);

                BitmapEncoder encoder = Path.GetExtension(filePath).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                    ".bmp" => new BmpBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };
                encoder.Frames.Add(BitmapFrame.Create(rtb));

                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }

                if (rootWasHidden)
                    _rootVisual.Visibility = Visibility.Hidden;

                FrameworkElement? parent = _rootVisual.Parent as FrameworkElement;
                while (parent != null && parent is not Window)
                {
                    if (Equals(parent.Tag, "__TEMP_VIS_RESTORE__"))
                    {
                        parent.Visibility = Visibility.Hidden;
                        parent.Tag = null;
                    }
                    parent = parent.Parent as FrameworkElement;
                }

                Logger.Info($"[MoveDbg] Snapshot saved: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GlobalMirror] SaveSnapshot failed: {ex.Message}");
                return false;
            }
        }

        // ========== 基础移动日志（缺失补回） ==========
        private void LogMovingElements(string origin)
        {
            if (!_movementDebugLogging) return;
            if (_lastResult == null || _designCanvas == null) return;

            try
            {
                int idx = 0;
                Logger.Info($"[MoveDbg] ---- {origin} | count={_lastResult.MovingDirections.Count} time={DateTime.Now:HH:mm:ss.fff} ----");
                foreach (var kv in _lastResult.MovingDirections)
                {
                    var border = kv.Key;
                    var (vx, vy) = kv.Value;

                    string name = !string.IsNullOrEmpty(border.Name) ? border.Name : "(no-name)";
                    string brushHex = GetBrushHex(border.Background);
                    double tx = 0, ty = 0;

                    if (border.RenderTransform is TransformGroup tg)
                    {
                        var translate = tg.Children.OfType<TranslateTransform>().LastOrDefault();
                        if (translate != null)
                        {
                            tx = translate.X;
                            ty = translate.Y;
                        }
                    }

                    bool partlyVisible = tx + border.ActualWidth > 0 && tx < _canvasSize &&
                                         ty + border.ActualHeight > 0 && ty < _canvasSize;

                    int childCount = 0;
                    if (border.Child is Panel panel)
                        childCount = panel.Children.Count;
                    else if (border.Child != null)
                        childCount = 1;

                    Logger.Info(
                        $"[MoveDbg] #{idx++} name={name} pos=({tx:0.##},{ty:0.##}) size=({border.ActualWidth:0.##}x{border.ActualHeight:0.##}) vel=({vx:0.##},{vy:0.##}) bg={brushHex} opacity={border.Opacity:0.##} vis={border.Visibility} childCount={childCount} inView={partlyVisible}");
                }
            }
            catch (Exception ex)
            {
                Logger.Info("[MoveDbg] Log error: " + ex.Message);
            }
        }

        private static string GetBrushHex(Brush? b)
        {
            if (b is SolidColorBrush sc)
            {
                var c = sc.Color;
                return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return b == null ? "(null)" : b.ToString();
        }

        // ===== 调试增强：可配置 =====
        private bool _deepVisualDebug = true;              // 是否输出子元素详细信息
        private bool _forceChildDebugBackground = true;    // 是否给 Child 强制加一个调试背景（不持久覆盖，仅当其无背景）

        public void EnableDeepVisualDebug(bool enable) => _deepVisualDebug = enable;
        public void EnableForceChildDebugBackground(bool enable) => _forceChildDebugBackground = enable;

        // 截图或移动前调用
        private void DebugProbeMovingBorders(string origin)
        {
            if (!_movementDebugLogging || !_deepVisualDebug) return;
            if (_lastResult == null) return;

            Logger.Info($"[MoveDbg-Deep] ==== {origin} DeepProbe count={_lastResult.MovingDirections.Count} ====");
            foreach (var kv in _lastResult.MovingDirections)
            {
                var border = kv.Key;
                if (border == null) continue;

                var feChild = border.Child as FrameworkElement;
                string childType = feChild?.GetType().Name ?? border.Child?.GetType().Name ?? "(null)";
                string childVis = feChild?.Visibility.ToString() ?? "N/A";
                double childW = feChild?.ActualWidth ?? -1;
                double childH = feChild?.ActualHeight ?? -1;
                double childOpacity = feChild?.Opacity ?? -1;

                // Transform 细节
                double scaleX = 1, scaleY = 1, rot = 0;
                if (border.RenderTransform is TransformGroup tg)
                {
                    foreach (var t in tg.Children)
                    {
                        if (t is ScaleTransform s)
                        {
                            scaleX = s.ScaleX;
                            scaleY = s.ScaleY;
                        }
                        else if (t is RotateTransform r)
                        {
                            rot = r.Angle;
                        }
                    }
                }

                if (_forceChildDebugBackground && feChild is Panel cp && cp.Background == null)
                {
                    cp.Background = new SolidColorBrush(Color.FromArgb(120, 0, 255, 0));
                }
                else if (_forceChildDebugBackground && feChild is Control ctrl && ctrl.Background == null)
                {
                    ctrl.Background = new SolidColorBrush(Color.FromArgb(120, 0, 255, 0));
                }

                Logger.Info($"[MoveDbg-Deep] BorderChild type={childType} vis={childVis} size=({childW:0.##}x{childH:0.##}) opacity={childOpacity:0.##} scale=({scaleX:0.##},{scaleY:0.##}) rot={rot:0.##}");

                // 如果是 Image，输出像素源信息
                if (feChild is Image img && img.Source != null)
                {
                    try
                    {
                        Logger.Info($"[MoveDbg-Deep]   ImageSource={img.Source} size=({img.Source.Width}x{img.Source.Height})");
                    }
                    catch { }
                }

                // 向下枚举前若干后代
                DumpDescendants(feChild, maxDepth: 2, maxPerLevel: 8);
            }
        }

        private void DumpDescendants(FrameworkElement? root, int maxDepth, int maxPerLevel)
        {
            if (root == null) return;
            try
            {
                void Walk(DependencyObject v, int depth, ref int siblingCount)
                {
                    if (depth > maxDepth) return;
                    int children = VisualTreeHelper.GetChildrenCount(v);
                    for (int i = 0; i < children; i++)
                    {
                        if (i >= maxPerLevel) { 
                            Logger.Info($"[MoveDbg-Deep]   (depth {depth}) more children truncated...");
                            break;
                        }
                        var child = VisualTreeHelper.GetChild(v, i);
                        siblingCount++;
                        string name = (child as FrameworkElement)?.Name;
                        var fe = child as FrameworkElement;
                        string info = $"{child.GetType().Name}";
                        if (!string.IsNullOrEmpty(name)) info += $"(Name={name})";
                        if (fe != null)
                            info += $" Vis={fe.Visibility} Sz=({fe.ActualWidth:0.#}x{fe.ActualHeight:0.#}) Op={fe.Opacity:0.##}";
                        Logger.Info($"[MoveDbg-Deep]     {(new string(' ', depth * 2))}{info}");
                        Walk(child, depth + 1, ref siblingCount);
                    }
                }
                int sc = 0;
                Walk(root, 1, ref sc);
            }
            catch (Exception ex)
            {
                Logger.Info("[MoveDbg-Deep] DumpDescendants error: " + ex.Message);
            }
        }

        // ========== Helper: 颜色插值 ==========
        private static Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        // ========== Helper: 仪表盘指针角度换算 (0-100%) ==========
        private static double GaugeRotationFromPercent(double percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            double startAngle = 150;   // 起始角（你的 Gauge 设计）
            double sweep = 240;        // 总扫过角度
            double angle = startAngle + sweep * (percent / 100.0);
            return angle - 270;        // 与 RotateTransform 期望的坐标系对齐
        }

        // 新增字段与方法（放到其它字段附近，比如 _autoSnapshotEnabled 下面）
        private bool _captureBeforeMove = true; // true: 先截后动；false: 先动后截
        public void SetAutoSnapshotPhase(bool captureBeforeMove)
        {
            _captureBeforeMove = captureBeforeMove;
            Logger.Info($"[AutoSnap] CaptureBeforeMove={_captureBeforeMove}");
        }

        // 强制刷新 designCanvas 及其子元素（文字）
        private void ForceCanvasLayoutRefresh()
        {
            if (_designCanvas == null) return;
            double w = _canvasSize > 0 ? _canvasSize : 512;
            double h = w;
            _designCanvas.Measure(new Size(w, h));
            _designCanvas.Arrange(new Rect(0, 0, w, h));
            _designCanvas.InvalidateVisual();
            _designCanvas.UpdateLayout();
        }

        // 仅截取 designCanvas（调试用），看看单独画面是否有文字
        public bool SaveDesignCanvasSnapshot(string filePath)
        {
            if (_designCanvas == null) return false;
            try
            {
                ForceCanvasLayoutRefresh();
                Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                var rtb = new RenderTargetBitmap(_canvasSize, _canvasSize, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(_designCanvas);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                using var fs = File.Create(filePath);
                encoder.Save(fs);
                Logger.Info("[SnapDbg] Canvas-only snapshot saved: " + filePath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Info("[SnapDbg] Canvas-only snapshot failed: " + ex.Message);
                return false;
            }
        }

        // ===== 新增字段（放到其它私有字段附近） =====
        private bool _useCompositionRendering = true;        // 是否使用 CompositionTarget.Rendering
        private EventHandler? _renderingHandler;
        private TimeSpan _lastRenderingTime = TimeSpan.Zero;
        private bool _suspendMovement = false;               // 截图冻结时用

        // ===== 对外开关 =====
        public void UseCompositionRendering(bool enable)
        {
            if (_useCompositionRendering == enable) return;
            _useCompositionRendering = enable;
            if (enable)
            {
                if (_moveTimer.IsEnabled) _moveTimer.Stop();
                HookRendering();
            }
            else
            {
                UnhookRendering();
                StartTimers(); // 回到原先 timer 逻辑
            }
            Logger.Info($"[MoveDbg] UseCompositionRendering={_useCompositionRendering}");
        }

        // ===== Hook / Unhook =====
        private void HookRendering()
        {
            if (_renderingHandler != null) return;
            _renderingHandler = OnCompositionRendering;
            CompositionTarget.Rendering += _renderingHandler;
            _lastRenderingTime = TimeSpan.Zero;
        }
        private void UnhookRendering()
        {
            if (_renderingHandler != null)
            {
                CompositionTarget.Rendering -= _renderingHandler;
                _renderingHandler = null;
            }
        }

        // ===== 新增：在 StartTimers 里加入 =====
        // 原 StartTimers 末尾追加：
        private void StartTimers()
        {
            if (!_metricsTimer.IsEnabled) _metricsTimer.Start();

            if (_useCompositionRendering)
            {
                // 使用 CompositionTarget.Rendering，不再启用 _moveTimer
                if (_moveTimer.IsEnabled) _moveTimer.Stop();
                HookRendering();
            }
            else
            {
                if (_lastResult != null && _lastResult.MovingDirections.Any())
                {
                    if (!_moveTimer.IsEnabled) _moveTimer.Start();
                }
                else
                {
                    if (_moveTimer.IsEnabled) _moveTimer.Stop();
                }
            }
        }

        // ===== 新增：CompositionTarget.Rendering 回调 =====
        private void OnCompositionRendering(object? sender, EventArgs e)
        {
            if (_suspendMovement) return;
            if (_lastResult == null || !_lastResult.MovingDirections.Any()) return;

            if (e is RenderingEventArgs re)
            {
                if (_lastRenderingTime == TimeSpan.Zero)
                {
                    _lastRenderingTime = re.RenderingTime;
                    return;
                }
                var dt = (re.RenderingTime - _lastRenderingTime).TotalSeconds;
                if (dt < 0) dt = 0;
                // 钳制极端大帧（最小化追帧跳动）
                if (dt > 0.25) dt = 0.25;

                _lastRenderingTime = re.RenderingTime;
                UpdateMovementOnce(dtOverrideSeconds: dt);
            }
            else
            {
                // 无 RenderingEventArgs 时退化使用一个近似帧时间
                UpdateMovementOnce(dtOverrideSeconds: 1.0 / 60.0);
            }
        }

        // ===== 截图冻结辅助（可在 SaveSnapshot* 里临时调用）
        private IDisposable FreezeMovementScope()
        {
            return new MovementFreeze(this);
        }
        private sealed class MovementFreeze : IDisposable
        {
            private readonly GlobalMirrorCanvasService _svc;
            private readonly bool _old;
            public MovementFreeze(GlobalMirrorCanvasService svc)
            {
                _svc = svc;
                _old = _svc._suspendMovement;
                _svc._suspendMovement = true;
            }
            public void Dispose()
            {
                _svc._suspendMovement = _old;
                // 重置时间，防止长时间冻结后瞬间追帧
                _svc._lastRenderingTime = TimeSpan.Zero;
            }
        }

        // ===== 新增：直接渲染 designCanvas 的简单截图 =====
        public bool SaveCanvasSnapshot(string filePath, bool includeBackground = true, bool freezeMovement = true)
        {
            if (_designCanvas == null) return false;
            try
            {
                using var _ = freezeMovement ? FreezeMovementScope() : null;

                // 冻结逻辑下再手动更新一次布局
                ForceCanvasLayoutRefresh();
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

                int size = _canvasSize > 0 ? _canvasSize : 512;
                var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);

                if (includeBackground && _rootVisual != null && _bgRect != null)
                {
                    // 合成背景 + 设计层：用 DrawingVisual 减少依赖 root 可见性
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        // 背景色
                        if (_bgRect.Fill is Brush bgBrush)
                            dc.DrawRectangle(bgBrush, null, new Rect(0, 0, size, size));

                        // 背景图
                        if (_bgImage?.Source != null)
                        {
                            dc.PushOpacity(_bgImage.Opacity);
                            dc.DrawImage(_bgImage.Source, new Rect(0, 0, size, size));
                            dc.Pop();
                        }

                        // 设计层
                        var vb = new VisualBrush(_designCanvas) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
                        dc.DrawRectangle(vb, null, new Rect(0, 0, size, size));
                    }
                    rtb.Render(dv);
                }
                else
                {
                    rtb.Render(_designCanvas);
                }

                BitmapEncoder encoder = Path.GetExtension(filePath).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                    ".bmp" => new BmpBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                encoder.Save(fs);
                Logger.Info($"[MoveDbg] Canvas snapshot saved: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Info("[MoveDbg] Canvas snapshot failed: " + ex.Message);
                return false;
            }
        }
        public string? MyCampture(Grid DesignRoot)
        {
            try
            {
                if (DesignRoot == null || DesignRoot.ActualWidth <= 0 || DesignRoot.ActualHeight <= 0)
                {
                    LocalizedMessageBox.Show("CurrentCanvasNotReady", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                    return null;
                }

             

                // 强制刷新布局，确保最新渲染
                DesignRoot.UpdateLayout();

                int pixelWidth = (int)Math.Ceiling(DesignRoot.ActualWidth);
                int pixelHeight = (int)Math.Ceiling(DesignRoot.ActualHeight);

                var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(DesignRoot);

                // 恢复选中边框
              

                string captureDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "captureimage");
                Directory.CreateDirectory(captureDir);

                string safeName = "1";
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
            return null;
        }


        // === NEW: standard capture (mirror) ===
        public string? SaveStandardCaptureToFolder()
        {
            if (_rootVisual == null) return null;

            try
            {
                string captureDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "captureimage");
                Directory.CreateDirectory(captureDir);

                string baseName = string.IsNullOrWhiteSpace(_lastConfig?.ConfigName)
                    ? "MirrorCanvas"
                    : _lastConfig!.ConfigName;

                string safeName = MakeSafeFileBaseLocal(baseName);
                string fileName = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.jpeg";
                string fullPath = Path.Combine(captureDir, fileName);

                bool ok = false;
                // ensure UI-thread render
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ok = SaveCanvasSnapshot(fullPath, includeBackground: true, freezeMovement: true);
                });

                return ok ? fullPath : null;
            }
            catch
            {
                return null;
            }
        }

        private static string MakeSafeFileBaseLocal(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "MirrorCanvas";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = Regex.Replace(name, @"\s+", "_");
            return name;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopSequencePlayback();
            try { _metricsTimer.Stop(); } catch { }
            try { _moveTimer.Stop(); } catch { }
            StopVideoPlayback(disposeState: true);
            _videoLoadCts?.Cancel();
            UnhookRendering();
            _metrics.Dispose();
        }
    }
}