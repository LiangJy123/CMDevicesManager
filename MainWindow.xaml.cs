// CMDevicesManager - System Hardware Monitoring Application
// This application displays CPU, GPU, and memory performance metrics
// for system monitoring purposes. It does not transmit data externally.
// Source code available for transparency and security review.

using CMDevicesManager.Helper;
using CMDevicesManager.Models;
using CMDevicesManager.Pages;
using CMDevicesManager.Services;
using MicaWPF.Controls;
using System.Diagnostics;
using System.IO;
using System.Linq; // 若文件顶部尚未有
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;      // (optional, future cancellation)
using System.Threading.Tasks; // ADDED
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes; // ADDED for DispatcherTimer
using MessageBox = System.Windows.MessageBox;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace CMDevicesManager
{
    internal static class UIDebugFlags
    {
        // 为 false 时隐藏指定的导航按钮
        public const bool UI_Debug_without_deivce = false;
    }
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MicaWindow
    {
        public MainWindow? Instance => this;
        // Cache single instances to avoid reinitialization on repeated clicks
        private HomePage? _homePage;
        private DevicePage? _devicePage;
        private DevicePageDemo? _devicePageDemo;
        private SettingsPage? _settingsPage;
        private DevicePlayModePage? _devicePlayModePage;

        private ListBoxItem? _lastNavContentItem; // 记录最后一次真正的页面项
        private DeviceConfigPage? _deviceConfigPage;
        // ADDED: prevent concurrent sequence capture
        private bool _isSequenceCaptureInProgress;

        private RenderDemoPage? _renderDemoPage;

        // ===== ADDED: MainWindow real-time JPEG streaming (非 DeviceConfigPage 时启用) =====
        private DispatcherTimer? _mainRealtimeTimer;
        private bool _mainRealtimeActive;
        private const int MainRealtimeIntervalMs = 50;   // 50ms ≈ 20FPS
        private const int MainRealtimeSize = 480;        // 输出 480x480
        private FrameworkElement? _mainCaptureRoot;       // MirrorRoot

        public MainWindow()
        {
            InitializeComponent();

        
            ApplyUIDebugFlag();

            GlobalMirrorCanvasService.Instance.Initialize(
                MirrorRoot,
                MirrorBgRect,
                MirrorBgImage,
                MirrorDesignCanvas);

            GlobalMirrorCanvasService.Instance.LoadInitialFromGlobalSequence();

            Loaded += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 强制测量所有子元素
                    foreach (UIElement child in MirrorDesignCanvas.Children)
                    {
                        if (child is FrameworkElement fe)
                        {
                            fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                            fe.Arrange(new Rect(fe.DesiredSize));

                            // 递归处理
                            if (fe is Border border && border.Child is FrameworkElement childFe)
                            {
                                childFe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                                childFe.Arrange(new Rect(childFe.DesiredSize));
                            }
                        }
                    }

                    MirrorDesignCanvas.InvalidateVisual();
                    MirrorDesignCanvas.UpdateLayout();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };


            DevicePlayModePage.GlobalConfigRendered += cfg =>
            {
                Dispatcher.Invoke(() => GlobalMirrorCanvasService.Instance.ApplyConfig(cfg));
            };

            MainFrame.Navigated += MainFrame_Navigated;
            try
            {
                MainFrame.Navigate(GetHomePage());
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to navigate to HomePage", ex);
                LocalizedMessageBox.Show("HomePageLoadFailed", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

        }
        private void ApplyUIDebugFlag()
        {
            if (!UIDebugFlags.UI_Debug_without_deivce)
            {
                HideNavItem(ConfigNavItem);
                HideNavItem(DemoNavItem);
                HideNavItem(RenderNavItem);
                HideNavItem(PlaymodeNavItem);
                HideNavItem(SaveImageItem);
                HideNavItem(MirrorToggleItem);

                // 若当前选中的是被隐藏项，切回首页
                if (NavList.SelectedItem is ListBoxItem li && li.Visibility != Visibility.Visible)
                {
                    NavList.SelectedIndex = 0;
                }
            }
        }

        private static void HideNavItem(ListBoxItem? item)
        {
            if (item != null) item.Visibility = Visibility.Collapsed;
        }
        private void MainFrame_Navigated(object sender, NavigationEventArgs e)
        {
            var name = e.Content?.GetType().Name;
            if (name != "DevicePlayModePage" && name != "DeviceConfigPage")
            {
                GlobalMirrorCanvasService.Instance.ReapplyLast();
            }

            // 启停主窗口实时推送 (DeviceConfigPage 本身内部已推送, 此时关闭主窗口推送避免重复)
            if (e.Content is DeviceConfigPage)
            {
                StopMainRealtimeStreaming();
            }
            else
            {
                StartMainRealtimeStreaming();
            }
        }

        private HomePage GetHomePage() => _homePage ??= new HomePage();
        private DevicePage GetDevicePage() => _devicePage ??= new DevicePage();
        private DevicePageDemo GetDevicePageDemo() => _devicePageDemo ??= new DevicePageDemo();
        private SettingsPage GetSettingsPage() => _settingsPage ??= new SettingsPage();
        public DevicePlayModePage GetDevicePlayModePage() => _devicePlayModePage ??= new DevicePlayModePage();

        private DeviceConfigPage GetDeviceConfigPage() => _deviceConfigPage ??= new DeviceConfigPage();
        private RenderDemoPage GetRenderDemoPage() => _renderDemoPage ??= new RenderDemoPage();

        private void ValidateResources()
        {
            try
            {
                var backgroundUri = new Uri("pack://application:,,,/Resources/background.png");
                var backgroundResource = Application.GetResourceStream(backgroundUri);
                if (backgroundResource == null)
                {
                    Logger.Warn("Background image not found: /Resources/background.png");
                }

                var iconUri = new Uri("pack://application:,,,/Resources/icon/icon.ico");
                var iconResource = Application.GetResourceStream(iconUri);
                if (iconResource == null)
                {
                    Logger.Warn("Icon not found: /Resources/icon/icon.ico");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Resource validation failed", ex);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
          
        }

        private bool IsConfigOrPlayModePage(object? content) =>
            content is DeviceConfigPage || content is DevicePlayModePage;

        private void RestorePreviousSelection()
        {
            if (_lastNavContentItem != null)
            {
                NavList.SelectionChanged -= NavList_SelectionChanged;
                NavList.SelectedItem = _lastNavContentItem;
                NavList.SelectionChanged += NavList_SelectionChanged;
            }
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is ListBoxItem item)
            {
                if (Equals(item.Tag, "__MirrorToggle"))
                {
                    bool toShow = true;//GlobalMirrorHost.Visibility != Visibility.Visible;
                    GlobalMirrorHost.Visibility = toShow ? Visibility.Visible : Visibility.Hidden;
                    GlobalMirrorHost.IsHitTestVisible = toShow;

                    RestorePreviousSelection();
                    return;
                }

                if (Equals(item.Tag, "__MirrorSaveImage"))
                {
                    savecanvasglobal();
                    RestorePreviousSelection();
                    return;
                }

                if (item.Tag is string pagePath)
                {
                    try
                    {
                        pagePath = pagePath.Replace("Pages/", "").Replace(".xaml", "");

                        if (pagePath.Equals("HomePage") && MainFrame.Content is HomePage) { _lastNavContentItem = item; return; }
                        if (pagePath.Equals("DevicePage") && MainFrame.Content is DevicePage) { _lastNavContentItem = item; return; }
                        if (pagePath.Equals("DevicePageDemo") && MainFrame.Content is DevicePageDemo) { _lastNavContentItem = item; return; }
                        if (pagePath.Equals("SettingsPage") && MainFrame.Content is SettingsPage) { _lastNavContentItem = item; return; }
                        if (pagePath.Equals("DevicePlayModePage") && MainFrame.Content is DevicePlayModePage) { _lastNavContentItem = item; return; }
                        if (pagePath.Equals("DeviceConfigPage") && MainFrame.Content is DeviceConfigPage) { _lastNavContentItem = item; return; }
                        if (pagePath.Equals("RenderDemoPage") && MainFrame.Content is RenderDemoPage) { _lastNavContentItem = item; return; }

                        if (pagePath.Equals("HomePage")) MainFrame.Navigate(GetHomePage());
                        else if (pagePath.Equals("DevicePage")) MainFrame.Navigate(GetDevicePage());
                        else if (pagePath.Equals("DevicePageDemo")) MainFrame.Navigate(GetDevicePageDemo());
                        else if (pagePath.Equals("SettingsPage")) MainFrame.Navigate(GetSettingsPage());
                        else if (pagePath.Equals("DevicePlayModePage")) MainFrame.Navigate(GetDevicePlayModePage());
                        else if (pagePath.Equals("DeviceConfigPage")) MainFrame.Navigate(GetDeviceConfigPage());
                        else if (pagePath.Equals("RenderDemoPage")) MainFrame.Navigate(GetRenderDemoPage());
                        else MainFrame.Source = new Uri(pagePath, UriKind.Relative);

                        _lastNavContentItem = item;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Navigation failed to {pagePath}", ex);
                        MainFrame.Navigate(GetHomePage());
                        _lastNavContentItem = null;
                    }
                }
            }
        }

        private void savecanvasglobal_ori()
        {
            var DesignRoot = MirrorRoot;
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

                string baseName = "GlobalCanvas";

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

        private void savecanvasglobal()
        {
            var DesignRoot = MirrorRoot;
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

                string baseName = "GlobalCanvas";

                foreach (var c in Path.GetInvalidFileNameChars())
                    baseName = baseName.Replace(c, '_');

                string file = Path.Combine(dir, $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                var encoder = new JpegBitmapEncoder
                {
                    QualityLevel = 95
                };
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


        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 由于按钮区域已经阻止了事件冒泡，这里不会收到按钮的点击

            // 处理拖拽和双击
            if (e.ClickCount == 2)
            {
                ToggleWindowState();
            }
            else
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // 忽略异常
                }
            }
        }

        // 删除 TitleBarButton_PreviewMouseLeftButtonDown 方法（不再需要）

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleWindowState();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleWindowState()
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void ToggleMirrorPreviewButton_Checked(object sender, RoutedEventArgs e)
        {
            GlobalMirrorHost.Visibility = Visibility.Visible;
         
            GlobalMirrorHost.IsHitTestVisible = true;
        }

        private void ToggleMirrorPreviewButton_Unchecked(object sender, RoutedEventArgs e)
        {

            GlobalMirrorHost.IsHitTestVisible = false;
            GlobalMirrorHost.Visibility = Visibility.Hidden;
        }

        private void CloseMirrorPreview_Click(object sender, RoutedEventArgs e)
        {
    
            GlobalMirrorHost.IsHitTestVisible = false;
            GlobalMirrorHost.Visibility = Visibility.Hidden;
        }

        // ===== ADDED: Real-time JPEG streaming for non-DeviceConfig pages =====
        private void StartMainRealtimeStreaming()
        {
            if (_mainRealtimeActive) return;
            if (MainFrame.Content is DeviceConfigPage) return; // 该页面自身有发送逻辑

            _mainCaptureRoot ??= MirrorRoot;
            if (_mainCaptureRoot == null) return;

            _mainRealtimeTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(MainRealtimeIntervalMs)
            };
            _mainRealtimeTimer.Tick += MainRealtimeTimer_Tick;
            _mainRealtimeTimer.Start();
            _mainRealtimeActive = true;
        }

        private void StopMainRealtimeStreaming()
        {
            _mainRealtimeActive = false;
            if (_mainRealtimeTimer != null)
            {
                _mainRealtimeTimer.Stop();
                _mainRealtimeTimer.Tick -= MainRealtimeTimer_Tick;
                _mainRealtimeTimer = null;
            }
        }

        private void MainRealtimeTimer_Tick(object? sender, EventArgs e)
        {
            if (!_mainRealtimeActive) return;
            if (_mainCaptureRoot == null ||
                _mainCaptureRoot.ActualWidth < 1 ||
                _mainCaptureRoot.ActualHeight < 1) return;
            if (!GlobalMirrorCanvasService.Instance.HasActiveContent) return;

            if (!GetHidDeviceStatus())
            {
                // 没有连接的设备时不发送
                return;
            }

            var jpeg = CaptureElementToJpegFixedSquare(_mainCaptureRoot, MainRealtimeSize);
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

        private byte[]? CaptureElementToJpegFixedSquare(FrameworkElement element, int size)
        {
            try
            {
                element.UpdateLayout();

                int srcW = (int)System.Math.Ceiling(element.ActualWidth);
                int srcH = (int)System.Math.Ceiling(element.ActualHeight);
                if (srcW <= 0 || srcH <= 0) return null;

                var rtb = new RenderTargetBitmap(srcW, srcH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(element);

                double scale = System.Math.Min((double)size / srcW, (double)size / srcH);
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

                var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
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

        public void NavigateToPlayModePageAndSelectNav()
        {
            try
            {
                // 已经就是播放模式页面，仅更新选中项
                if (MainFrame.Content is DevicePlayModePage existing)
                {
                    SelectNavItemByTag("Pages/DevicePlayModePage.xaml");
                    return;
                }

                var page = GetDevicePlayModePage();
                MainFrame.Navigate(page);
                SelectNavItemByTag("Pages/DevicePlayModePage.xaml");
            }
            catch (Exception ex)
            {
                Logger.Error("NavigateToPlayModePageAndSelectNav failed", ex);
            }
        }

        private void SelectNavItemByTag(string tagKey)
        {
            ListBoxItem? target = null;
            foreach (var item in NavList.Items.OfType<ListBoxItem>())
            {
                if (item.Tag is string tag)
                {
                    var normalized = tag.Replace("Pages/", "").Replace(".xaml", "");
                    if (normalized.Equals("DevicePlayModePage", StringComparison.OrdinalIgnoreCase))
                    {
                        target = item;
                        break;
                    }
                }
            }
            if (target != null)
            {
                NavList.SelectionChanged -= NavList_SelectionChanged;
                NavList.SelectedItem = target;
                _lastNavContentItem = target;
                NavList.SelectionChanged += NavList_SelectionChanged;
            }
        }

        private bool GetHidDeviceStatus()
        {
            try
            {
                var hidService = ServiceLocator.HidDeviceService;
                if (hidService.ConnectedDeviceCount == 0)
                {
                    return false;
                }
                var connectedDevices = hidService?.ConnectedDevices;
                foreach (var dev in connectedDevices)
                {
                    if (dev != null)
                    {
                        return GetStoreRealTimeDisplayEnabled(dev.SerialNumber);
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("GetHidDeviceStatus failed", ex);
                return false;
            }
        }

        private bool GetStoreRealTimeDisplayEnabled(string deviceSerialNumber)
        {
            try
            {
                var offlineService = ServiceLocator.OfflineMediaDataService;
                var data = offlineService.GetDevicePlaybackMode(deviceSerialNumber);
                if (data == PlaybackMode.RealtimeConfig)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("GetHidDeviceCount failed", ex);
                return false;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            StopMainRealtimeStreaming();
            base.OnClosed(e);
        }
        #region Maximize WorkArea Fix

        // Win32 常量
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_DPICHANGED = 0x02E0;
        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;






        // Win32 结构定义
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        // P/Invoke
        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        #endregion

        #region Anti White Border Enhancement

        // 额外消息
        private const int WM_NCCALCSIZE = 0x0083;
        private const int DWMWA_BORDER_COLOR = 34;                 // Windows 11
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

            ApplyDwmAttributes(hwnd);

            // 绑定 StateChanged 以在最大化时移除内边框 & 圆角
            StateChanged += (_, __) => AdjustOuterBorderForState();
            Loaded += (_, __) => AdjustOuterBorderForState();
        }

        private void ApplyDwmAttributes(IntPtr hwnd)
        {
            // 使用深色模式（可选）
            uint useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(uint));

            // 设置边框颜色为透明(0) -> 去除白边
            uint transparent = 0x00000000;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref transparent, sizeof(uint));

            // (可选) 标题/文本颜色，虽然无原生标题栏，设置不影响也不出错
            // uint caption = 0x00000000; DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(uint));
            // uint text = 0x00FFFFFF;   DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(uint));
        }

        // 调整内边框和圆角
        private void AdjustOuterBorderForState()
        {
            if (RootOuterBorder == null) return;
            if (WindowState == WindowState.Maximized)
            {
                // 去掉你自己的 5px 内边 & 圆角，避免出现背景“框线”
                RootOuterBorder.BorderThickness = new Thickness(0);
                RootOuterBorder.CornerRadius = new CornerRadius(0);
            }
            else
            {
                RootOuterBorder.BorderThickness = new Thickness(5);
                RootOuterBorder.CornerRadius = new CornerRadius(8);
            }
        }

        // 修改现有 WndProc（整合新增 WM_NCCALCSIZE）
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_GETMINMAXINFO:
                    AdjustMaximizedBounds(hwnd, lParam);
                    // 不吞消息
                    break;

                case WM_NCCALCSIZE:
                    // 去掉系统 1px 非客户区：仅在最大化时处理（也可全部处理）
                    if (wParam != IntPtr.Zero)
                    {
                        handled = true;
                        return IntPtr.Zero;
                    }
                    break;

                case WM_DPICHANGED:
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (WindowState == WindowState.Maximized)
                        {
                            WindowState = WindowState.Normal;
                            WindowState = WindowState.Maximized;
                        }
                    });
                    break;
            }
            return IntPtr.Zero;
        }

        // 如果仍有 1px 亮线，可启用“微过画” (off-by-one) 调整：
        // 在 AdjustMaximizedBounds 末尾添加注释所示的 +2/-1 逻辑（按需取消注释）
        private void AdjustMaximizedBounds(IntPtr hwnd, IntPtr lParam)
        {
            if (lParam == IntPtr.Zero) return;
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                MONITORINFO mi = new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref mi))
                {
                    var work = mi.rcWork;
                    var monitorArea = mi.rcMonitor;

                    mmi.ptMaxPosition.x = work.Left - monitorArea.Left;
                    mmi.ptMaxPosition.y = work.Top - monitorArea.Top;
                    mmi.ptMaxSize.x = work.Right - work.Left;
                    mmi.ptMaxSize.y = work.Bottom - work.Top;

                    int minW = (int)Math.Min(mmi.ptMaxSize.x, Math.Max(0, (int)MinWidth));
                    int minH = (int)Math.Min(mmi.ptMaxSize.y, Math.Max(0, (int)MinHeight));
                    if (minW > 0) mmi.ptMinTrackSize.x = minW;
                    if (minH > 0) mmi.ptMinTrackSize.y = minH;

                    // 微过画补偿 (仅当仍看到细白线时启用)
                    // mmi.ptMaxPosition.x -= 1;
                    // mmi.ptMaxPosition.y -= 1;
                    // mmi.ptMaxSize.x += 2;
                    // mmi.ptMaxSize.y += 2;

                    Marshal.StructureToPtr(mmi, lParam, true);
                }
            }
        }

        #endregion

        // 将原方法名修改为 PreviewMouseLeftButtonDown
        // 在按钮上添加 PreviewMouseLeftButtonDown，阻止事件冒泡到拖拽区域
        private void TitleBarButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 标记事件已处理，防止冒泡到拖拽区域
            // 但不阻止按钮自身的 Click 事件
            e.Handled = false; // 允许 Click 事件继续
        }

        // 在类的任意位置添加此方法

        /// <summary>
        /// 在 Preview 阶段拦截按钮区域的鼠标按下事件
        /// 优先级高于 MicaWindow 的内部拖拽逻辑
        /// </summary>
        private void TitleBarButtonPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 完全阻止事件传播到 MicaWindow 的内部逻辑
            e.Handled = true;
        }

        /// <summary>
        /// 同样需要拦截鼠标释放事件，确保按钮的 Click 事件能正常触发
        /// </summary>
        private void TitleBarButtonPanel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 不阻止 MouseUp，让按钮的 Click 事件正常工作
            e.Handled = false;
        }
    }
}