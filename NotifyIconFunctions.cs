using CMDevicesManager.Helper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace CMDevicesManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private NotifyIcon _notifyIcon;
        private bool _isExit;
        private void StartNotifyIcon()
        {
            try
            {
                using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/icon/icon.ico")).Stream;
                
                _notifyIcon = new NotifyIcon
                {
                    Icon = new Icon(stream),
                    Visible = true,
                    Text = "CMDevicesManager - Hardware Monitoring Tool"
                };
                
                // 托盘菜单 with clearer descriptions
                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Show Main Window", null, (s, ev) => ShowMainWindow());
                contextMenu.Items.Add("-"); // Separator
                contextMenu.Items.Add("Exit Application", null, (s, ev) => ExitApp());
                _notifyIcon.ContextMenuStrip = contextMenu;

                // 双击托盘图标 → 显示主界面
                _notifyIcon.DoubleClick += (s, ev) => ShowMainWindow();

                // Show balloon tip to inform user about system tray functionality
                _notifyIcon.BalloonTipTitle = "CMDevicesManager";
                _notifyIcon.BalloonTipText = "Application is running in system tray. Double-click to open or right-click for options.";
                _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                _notifyIcon.ShowBalloonTip(3000); // Show for 3 seconds
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize system tray icon", ex);
                // Continue without system tray if it fails
            }
        }
        private void ShowMainWindow()
        {
            if (MainWindow == null)
            {
                MainWindow = new MainWindow();
                MainWindow.Closing += MainWindow_Closing;
            }

            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }

        private void ShowFirstMainWindow()
        {
            if (MainWindow == null)
            {
                MainWindow = new MainWindow();
                
            }
            MainWindow.Closing += MainWindow_Closing;
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }

        private void ShowSettings()
        {
            System.Windows.MessageBox.Show("这里打开设置窗口");
        }

        private void ExitApp()
        {
            _isExit = true;
            _notifyIcon.Visible = false;
            MainWindow?.Close();
            Shutdown();
        }
        // Update the MainWindow_Closing method signature to match the expected nullability
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExit)
            {
                // Ask user for confirmation before minimizing to tray
                var result = MessageBox.Show(
                    "Do you want to minimize to system tray or exit the application?\n\n" +
                    "Click 'Yes' to minimize to tray (app keeps running)\n" +
                    "Click 'No' to exit completely\n" +
                    "Click 'Cancel' to return to the application",
                    "Close Confirmation",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                switch (result)
                {
                    case MessageBoxResult.Yes:
                        e.Cancel = true; // 阻止关闭
                        MainWindow.Hide(); // 隐藏到托盘
                        
                        // Show balloon tip to remind user
                        if (_notifyIcon != null)
                        {
                            _notifyIcon.BalloonTipTitle = "CMDevicesManager";
                            _notifyIcon.BalloonTipText = "Application minimized to system tray. Double-click the tray icon to restore.";
                            _notifyIcon.ShowBalloonTip(2000);
                        }
                        break;
                    case MessageBoxResult.No:
                        _isExit = true;
                        // Let the application close normally
                        break;
                    case MessageBoxResult.Cancel:
                        e.Cancel = true; // Stay in the application
                        break;
                }
            }
        }
    }
}
