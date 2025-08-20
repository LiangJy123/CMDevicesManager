using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application; 

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
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/icon/icon.ico")).Stream;
            
            _notifyIcon = new NotifyIcon
            {
                Icon = new Icon(stream),//File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application, 
                Visible = true,
                Text = "CMDevicesManager"
            };
            // 托盘菜单
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("打开主界面", null, (s, ev) => ShowMainWindow());
          //  contextMenu.Items.Add("设置", null, (s, ev) => ShowSettings());
            contextMenu.Items.Add("退出", null, (s, ev) => ExitApp());
            _notifyIcon.ContextMenuStrip = contextMenu;

            // 双击托盘图标 → 显示主界面
            _notifyIcon.DoubleClick += (s, ev) => ShowMainWindow();

           // ShowMainWindow();
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
                e.Cancel = true; // 阻止关闭
                MainWindow.Hide(); // 隐藏到托盘
            }
        }
    }
}
