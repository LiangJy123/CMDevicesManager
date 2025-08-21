using CMDevicesManager.Helper;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application; // Ensure this using directive is present

namespace CMDevicesManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        
        public App()
        {
            this.Exit += App_Exit;
        }

        private void App_Exit(object sender, ExitEventArgs e)
        {
            Logger.Info("App Exit");
         
            // 保存配置
            UserConfigManager.Save();
            Logger.Shutdown();
        }

         
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Logger.Info("Application starting up");

            try
            {
                // Initialize language based on user configuration
                Logger.Info("Initializing language settings");
                CMDevicesManager.Language.LanguageSwitch.ChangeLanguage(UserConfigManager.Current.Language);

                // 显示启动窗口
                var splash = new SplashWindow();
                var main = new MainWindow();
                this.MainWindow = main;
                splash.Show();
                
                Logger.Info("Starting system tray integration");
                StartNotifyIcon();
                
                // 后台加载数据
                Task.Run(() =>
                {
                    Logger.Info("Loading application data in background");
                    // 模拟加载数据（2秒）
                    Thread.Sleep(2000);
                    //DataLoader.LoadAll(); // 这里写你的初始化逻辑
                    Logger.Info("Background loading completed");

                    // 切回 UI 线程，关闭 Splash，显示主界面
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            splash.Close();
                            ShowFirstMainWindow();
                            Logger.Info("Main window displayed successfully");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Failed to display main window", ex);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Application startup failed", ex);
                throw;
            }
        }
    }

}
