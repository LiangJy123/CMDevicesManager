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
    /// Partial App class – system tray (NotifyIcon) localization adapted for zh-CN / zh-TW / en-US.
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

                string appTitle = L("AppTitle", "LCDMaster");
                string trayTooltip = L("TrayTooltip", appTitle);

                _notifyIcon = new NotifyIcon
                {
                    Icon = new Icon(stream),
                    Visible = true,
                    Text = trayTooltip
                };

                // Context menu (localized)
                var contextMenu = new ContextMenuStrip();
                var showWindowText = L("ShowMainWindow", "Show Main Window");
                var exitAppText = L("ExitApplication", "Exit Application");
                contextMenu.Items.Add(showWindowText, null, (s, ev) => ShowMainWindow());
                contextMenu.Items.Add("-"); // Separator
                contextMenu.Items.Add(exitAppText, null, (s, ev) => ExitApp());
                _notifyIcon.ContextMenuStrip = contextMenu;

                // Double-click shows main window
                _notifyIcon.DoubleClick += (s, ev) => ShowMainWindow();

                // Initial balloon tip
                _notifyIcon.BalloonTipTitle = appTitle;
                _notifyIcon.BalloonTipText = L("AppRunningInTray",
                    "Application is running in system tray. Double-click to open or right-click for options.");
                _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                _notifyIcon.ShowBalloonTip(3000);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize system tray icon", ex);
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
                MainWindow = new MainWindow();

            MainWindow.Closing += MainWindow_Closing;
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }

        private void ShowSettings() => LocalizedMessageBox.Show("OpenSettingsPlaceholder");

        private void ExitApp()
        {
            _isExit = true;
            if (_notifyIcon != null) _notifyIcon.Visible = false;
            MainWindow?.Close();
            Shutdown();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isExit) return;

            var result = LocalizedMessageBox.Show("CloseConfirmationMessage", "CloseConfirmation",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    e.Cancel = true;
                    MainWindow.Hide();
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.BalloonTipTitle = L("AppTitle", "LCDMaster");
                        _notifyIcon.BalloonTipText = L("AppMinimizedToTray",
                            "Application minimized to system tray. Double-click the tray icon to restore.");
                        _notifyIcon.ShowBalloonTip(2000);
                    }
                    break;
                case MessageBoxResult.No:
                    _isExit = true; // allow close
                    break;
                case MessageBoxResult.Cancel:
                    e.Cancel = true;
                    break;
            }
        }

        /// <summary>
        /// Helper to get localized string with fallback.
        /// </summary>
        private static string L(string key, string fallback)
        {
            try
            {
                var v = Application.Current.FindResource(key)?.ToString();
                if (string.IsNullOrWhiteSpace(v)) return fallback;
                return v;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
