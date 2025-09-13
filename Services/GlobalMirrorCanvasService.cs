using CMDevicesManager.Pages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static CMDevicesManager.Pages.DeviceConfigPage;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Path = System.IO.Path;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;
using System.Diagnostics;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Panel = System.Windows.Controls.Panel;
using Control = System.Windows.Controls.Control; // << 新增
using System.Text.RegularExpressions;
using Microsoft.VisualBasic;
using MessageBox = System.Windows.MessageBox; // add if not present

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

        private readonly ISystemMetricsService _metrics = new RealSystemMetricsService();
        private readonly DispatcherTimer _metricsTimer;
        private readonly DispatcherTimer _moveTimer;

        private double _moveSpeed = 100;
        private int _canvasSize = 512;
        private DateTime _lastMoveTick = DateTime.Now;

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

        // ====== 移动调试日志控制 ======
        private bool _movementDebugLogging = true; // 默认关闭
        // 调试选项：强制为移动元素加可见背景/边框
        private bool _forceDebugVisualForMoving = false;
        public void EnableForceDebugVisual(bool enable)
        {
            _forceDebugVisualForMoving = enable;
            Debug.WriteLine($"[MoveDbg] ForceDebugVisual {(enable ? "ON" : "OFF")}");
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
            Debug.WriteLine($"[MoveDbg] Movement debug logging {(enable ? "ENABLED" : "DISABLED")}");
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
            try
            {
                var seqPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CMDevicesManager", "globalconfig1.json");
                if (!File.Exists(seqPath))
                {
                    ClearToBlank();
                    return;
                }

                var json = File.ReadAllText(seqPath);
                var seq = JsonSerializer.Deserialize<GlobalConfigSequence>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                var first = seq?.Items?.FirstOrDefault();
                if (first == null || string.IsNullOrWhiteSpace(first.FilePath) || !File.Exists(first.FilePath))
                {
                    ClearToBlank();
                    return;
                }

                CanvasConfiguration? cfg = null;
                try
                {
                    var cfgJson = File.ReadAllText(first.FilePath);
                    cfg = JsonSerializer.Deserialize<CanvasConfiguration>(cfgJson, new JsonSerializerOptions
                    {
                        Converters = { new JsonStringEnumConverter() }
                    });
                }
                catch { }

                if (cfg == null)
                {
                    ClearToBlank();
                    return;
                }

                cfg.BackgroundColor = NormalizeColorOrWhite(cfg.BackgroundColor);
                ApplyConfig(cfg);
            }
            catch
            {
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

            if (string.IsNullOrWhiteSpace(cfg.BackgroundColor))
                _bgRect.Fill = Brushes.White;
            if (_bgRect.Fill == null)
                _bgRect.Fill = Brushes.White;

            _canvasSize = _lastResult.CanvasSize;
            _moveSpeed = _lastResult.MoveSpeed;
            _lastMoveTick = DateTime.Now;

            StartTimers();
        }

        public void ReapplyLast()
        {
            if (_lastConfig != null)
                ApplyConfig(_lastConfig);
        }

        public void ClearToBlank()
        {
            if (_designCanvas == null || _bgRect == null || _bgImage == null) return;
            _designCanvas.Children.Clear();
            _bgRect.Fill = Brushes.White;
            _bgImage.Source = null;
            _lastResult = null;
            _lastConfig = null;
        }

    

        private void MetricsTimer_Tick(object? sender, EventArgs e) => UpdateMetricsOnce();

        private void UpdateMetricsOnce()
        {
            if (_lastResult == null) return;
            //double cpu = _metrics.GetCpuUsagePercent();
            //double gpu = _metrics.GetGpuUsagePercent();
            double cpu = 4.9f;
            double gpu = 56.0f;
            DateTime now = DateTime.Now;

            foreach (var item in _lastResult.UsageItems)
            {
                double raw = item.Kind switch
                {
                    LiveInfoKind.CpuUsage => cpu,
                    LiveInfoKind.GpuUsage => gpu,
                    _ => 0
                };

                switch (item.DisplayStyle)
                {
                    case "ProgressBar":
                        if (item.BarFill != null)
                        {
                            double percent = Math.Clamp(raw, 0, 100);
                            item.BarFill.Width = item.BarTotalWidth * (percent / 100.0);
                            string prefix = item.Kind == LiveInfoKind.CpuUsage ? "CPU" :
                                            item.Kind == LiveInfoKind.GpuUsage ? "GPU" : "";
                            item.Text.Text = string.IsNullOrEmpty(prefix)
                                ? $"{Math.Round(percent)}%"
                                : $"{prefix} {Math.Round(percent)}%";
                            var mid = LerpColor(item.StartColor, item.EndColor, percent / 100.0);
                            item.Text.Foreground = new SolidColorBrush(mid);
                        }
                        break;
                    case "Gauge":
                        if (item.GaugeNeedleRotate != null)
                        {
                            item.GaugeNeedleRotate.Angle = GaugeRotationFromPercent(raw);
                            item.Text.Text = $"{Math.Round(raw)}%";
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

                Debug.WriteLine($"[MoveDbg] Snapshot saved: {filePath} mode={_snapshotScaleMode} logical=({logicalW}x{logicalH}) px=({pixelW}x{pixelH}) dpi=({dpiX:0.#},{dpiY:0.#})");
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

                Debug.WriteLine($"[MoveDbg] Snapshot saved: {filePath}");
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
                Debug.WriteLine($"[MoveDbg] ---- {origin} | count={_lastResult.MovingDirections.Count} time={DateTime.Now:HH:mm:ss.fff} ----");
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

                    Debug.WriteLine(
                        $"[MoveDbg] #{idx++} name={name} pos=({tx:0.##},{ty:0.##}) size=({border.ActualWidth:0.##}x{border.ActualHeight:0.##}) vel=({vx:0.##},{vy:0.##}) bg={brushHex} opacity={border.Opacity:0.##} vis={border.Visibility} childCount={childCount} inView={partlyVisible}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MoveDbg] Log error: " + ex.Message);
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

            Debug.WriteLine($"[MoveDbg-Deep] ==== {origin} DeepProbe count={_lastResult.MovingDirections.Count} ====");
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

                Debug.WriteLine($"[MoveDbg-Deep] BorderChild type={childType} vis={childVis} size=({childW:0.##}x{childH:0.##}) opacity={childOpacity:0.##} scale=({scaleX:0.##},{scaleY:0.##}) rot={rot:0.##}");

                // 如果是 Image，输出像素源信息
                if (feChild is Image img && img.Source != null)
                {
                    try
                    {
                        Debug.WriteLine($"[MoveDbg-Deep]   ImageSource={img.Source} size=({img.Source.Width}x{img.Source.Height})");
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
                            Debug.WriteLine($"[MoveDbg-Deep]   (depth {depth}) more children truncated...");
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
                        Debug.WriteLine($"[MoveDbg-Deep]     {(new string(' ', depth * 2))}{info}");
                        Walk(child, depth + 1, ref siblingCount);
                    }
                }
                int sc = 0;
                Walk(root, 1, ref sc);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MoveDbg-Deep] DumpDescendants error: " + ex.Message);
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
            Debug.WriteLine($"[AutoSnap] CaptureBeforeMove={_captureBeforeMove}");
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
                Debug.WriteLine("[SnapDbg] Canvas-only snapshot saved: " + filePath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SnapDbg] Canvas-only snapshot failed: " + ex.Message);
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
            Debug.WriteLine($"[MoveDbg] UseCompositionRendering={_useCompositionRendering}");
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
                Debug.WriteLine($"[MoveDbg] Canvas snapshot saved: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MoveDbg] Canvas snapshot failed: " + ex.Message);
                return false;
            }
        }
        public string? MyCampture(Grid DesignRoot)
        {
            try
            {
                if (DesignRoot == null || DesignRoot.ActualWidth <= 0 || DesignRoot.ActualHeight <= 0)
                {
                    MessageBox.Show("当前画布尚未准备好。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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

                MessageBox.Show($"截图已保存:\n{fullPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"截图失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
            try { _metricsTimer.Stop(); } catch { }
            try { _moveTimer.Stop(); } catch { }
            UnhookRendering();
            _metrics.Dispose();
        }
    }
}