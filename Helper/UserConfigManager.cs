using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;

namespace CMDevicesManager.Helper
{
    public class AppConfig
    {
        public string BackgroundPath { get; set; } = string.Empty;
        public string Theme { get; set; } = "Light";

        public string Language { get; set; } = "en-us"; //"zh-cn"
    }
    public static class UserConfigManager
    {

       
        private static readonly string ConfigFile = "userconfig.json";
        private static AppConfig _config;

        static UserConfigManager()
        {
            Load();
        }

        public static AppConfig Current => _config;

        public static void Save()
        {
            string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        }

        public static void Load()
        {
            if (File.Exists(ConfigFile))
            {
                string json = File.ReadAllText(ConfigFile);
                _config = JsonSerializer.Deserialize<AppConfig>(json);
                if (_config == null)
                {
                    _config = new AppConfig();
                    Logger.Warn("config is null");
                }
            }
            else
            {
                _config = new AppConfig();
                Save();
            }
        }


    }
}
