// CMDevicesManager - System Hardware Monitoring Application
// This application displays CPU, GPU, and memory performance metrics
// for system monitoring purposes. It does not transmit data externally.
// Source code available for transparency and security review.

using CMDevicesManager.Helper;
using CMDevicesManager.Pages;
using CMDevicesManager.Services;
using MicaWPF.Controls;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using System.IO;
using Path = System.IO.Path;
using System.Threading.Tasks; // ADDED
using System.Threading;      // (optional, future cancellation)

namespace CMDevicesManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MicaWindow
    {
        // Cache single instances to avoid reinitialization on repeated clicks
        private HomePage? _homePage;
        private DevicePage? _devicePage;
        private DevicePageDemo? _devicePageDemo;
        private SettingsPage? _settingsPage;
        private DevicePlayModePage? _devicePlayModePage;
        private ListBoxItem? _lastNavContentItem; // 记录最后一次真正的页面项

        // ADDED: prevent concurrent sequence capture
        private bool _isSequenceCaptureInProgress;

        public MainWindow()
        {
            InitializeComponent();

            GlobalMirrorCanvasService.Instance.Initialize(
                MirrorRoot,
                MirrorBgRect,
                MirrorBgImage,
                MirrorDesignCanvas);

            GlobalMirrorCanvasService.Instance.LoadInitialFromGlobalSequence();
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
                MessageBox.Show("Warning: Failed to load home page. Some features may not work correctly.",
                               "Navigation Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MainFrame_Navigated(object sender, NavigationEventArgs e)
        {
            var name = e.Content?.GetType().Name;
            if (name != "DevicePlayModePage" && name != "DeviceConfigPage")
            {
                GlobalMirrorCanvasService.Instance.ReapplyLast();
            }
        }

        private HomePage GetHomePage() => _homePage ??= new HomePage();
        private DevicePage GetDevicePage() => _devicePage ??= new DevicePage();
        private DevicePageDemo GetDevicePageDemo() => _devicePageDemo ??= new DevicePageDemo();
        private SettingsPage GetSettingsPage() => _settingsPage ??= new SettingsPage();
        private DevicePlayModePage GetDevicePlayModePage() => _devicePlayModePage ??= new DevicePlayModePage();

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
                    GlobalMirrorHost.Opacity = toShow ? 1 : 0;
                    GlobalMirrorHost.IsHitTestVisible = toShow;

                    RestorePreviousSelection();
                    return;
                }

                if (Equals(item.Tag, "__MirrorSaveImage"))
                {
                    // Only when not in config/play pages
                    if (!IsConfigOrPlayModePage(MainFrame.Content))
                    {
                        var path = GlobalMirrorCanvasService.Instance.MyCampture(MirrorRoot);
                        if (path != null)
                            MessageBox.Show($"截图已保存:\n{path}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        else
                            MessageBox.Show("截图失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    RestorePreviousSelection();
                    return;
                }

                // Normal navigation
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

                        if (pagePath.Equals("HomePage")) MainFrame.Navigate(GetHomePage());
                        else if (pagePath.Equals("DevicePage")) MainFrame.Navigate(GetDevicePage());
                        else if (pagePath.Equals("DevicePageDemo")) MainFrame.Navigate(GetDevicePageDemo());
                        else if (pagePath.Equals("SettingsPage")) MainFrame.Navigate(GetSettingsPage());
                        else if (pagePath.Equals("DevicePlayModePage")) MainFrame.Navigate(GetDevicePlayModePage());
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

        // ADDED: sequence capture logic
        private void StartMirrorSequenceCapture()
        {
            if (_isSequenceCaptureInProgress)
                return;

            _isSequenceCaptureInProgress = true;

            try
            {
                // Keep logical 1:1 size
                GlobalMirrorCanvasService.Instance.SetSnapshotScaleMode(
                    GlobalMirrorCanvasService.SnapshotScaleMode.LogicalCanvas);

                // Ensure movement advances between frames
                GlobalMirrorCanvasService.Instance.SetSnapshotMovementMode(
                    GlobalMirrorCanvasService.MovementCaptureMode.RealDelta);

                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AutoSequence");
                Directory.CreateDirectory(root);
                string dir = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(dir);

                int frameCount = 60;
                int intervalMs = 50; // 50ms

                Task.Run(async () =>
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        string file = Path.Combine(dir, $"frame_{i:D3}.png");
                        try
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                // Canvas-only capture:
                                // includeBackground=true -> background color/image + design elements
                                // set to false if you want only design layer
                                GlobalMirrorCanvasService.Instance
                                    .SaveCanvasSnapshot(file, includeBackground: true, freezeMovement: false);
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Frame capture failed", ex);
                        }

                        if (i < frameCount - 1)
                            await Task.Delay(intervalMs);
                    }

                    _isSequenceCaptureInProgress = false;
                });
            }
            catch (Exception ex)
            {
                _isSequenceCaptureInProgress = false;
                Logger.Error("Failed to start sequence capture", ex);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleWindowState();
            }
            else
            {
                DragMove();
            }
        }

        // Mirror 浮层内的保存按钮
        private void SaveMirrorInline_Click(object sender, RoutedEventArgs e)
        {
            DoSaveMirrorSnapshot();
        }

        // 单张保存（仍保留）
        private void DoSaveMirrorSnapshot()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Mirror Snapshot",
                Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp",
                FileName = "MirrorCanvas.png"
            };
            if (dlg.ShowDialog() == true)
            {
                bool ok = GlobalMirrorCanvasService.Instance
                    .SaveCanvasSnapshot(dlg.FileName, includeBackground: true, freezeMovement: true);
                if (!ok)
                {
                    MessageBox.Show("Save failed.", "Mirror Snapshot",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

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

        private void SaveHiddenCanvasButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save Hidden Canvas Snapshot",
                Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp",
                FileName = "HiddenCanvas.png"
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (!GlobalMirrorCanvasService.Instance.SaveSnapshot(dlg.FileName))
                {
                    MessageBox.Show("Save failed.", "Snapshot", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ToggleMirrorPreviewButton_Checked(object sender, RoutedEventArgs e)
        {
            GlobalMirrorHost.Visibility = Visibility.Visible;
            GlobalMirrorHost.Opacity = 1;
            GlobalMirrorHost.IsHitTestVisible = true;
        }

        private void ToggleMirrorPreviewButton_Unchecked(object sender, RoutedEventArgs e)
        {
            GlobalMirrorHost.Opacity = 0;
            GlobalMirrorHost.IsHitTestVisible = false;
            GlobalMirrorHost.Visibility = Visibility.Hidden;
        }

        private void CloseMirrorPreview_Click(object sender, RoutedEventArgs e)
        {
            GlobalMirrorHost.Opacity = 0;
            GlobalMirrorHost.IsHitTestVisible = false;
            GlobalMirrorHost.Visibility = Visibility.Hidden;
        }

        // NEW: auto save mirror canvas (no dialog)
        private void SaveMirrorAuto_Click(object sender, RoutedEventArgs e)
        {
            var path = GlobalMirrorCanvasService.Instance.SaveStandardCaptureToFolder();
            if (path != null)
                MessageBox.Show($"截图已保存:\n{path}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show("截图失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}