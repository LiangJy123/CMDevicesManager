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
    }

}
