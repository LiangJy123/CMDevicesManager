using CMDevicesManager.Helper;
using System.Configuration;
using System.Data;
using System.Windows;

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

            // Initialize language based on user configuration
            CMDevicesManager.Language.LanguageSwitch.ChangeLanguage(UserConfigManager.Current.Language);

            // 显示启动窗口
            var splash = new SplashWindow();
            var main = new MainWindow();
            this.MainWindow = main;
            splash.Show();

            // 后台加载数据
            Task.Run(() =>
            {
                // 模拟加载数据（3秒）
                Thread.Sleep(3000);
                //DataLoader.LoadAll(); // 这里写你的初始化逻辑

                // 切回 UI 线程，关闭 Splash，显示主界面
                Dispatcher.Invoke(() =>
                {
                   
                    splash.Close();
                    main.Show();
                   
                });
            });
        }
    }

}
