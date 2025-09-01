// CMDevicesManager - User Configuration Management
// This handles basic application settings like language and theme preferences.
// No sensitive data is stored, only user interface preferences.

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
        public string Language { get; set; } = "zh-cn";
        // Default to Cascadia Mono
        public string FontFamily { get; set; } = "cascadia-mono";
    }
    public static class UserConfigManager
    {
        private static readonly string ConfigFile = "userconfig.json";
        private static AppConfig _config = new AppConfig(); // Initialize to avoid CS8618

        static UserConfigManager()
        {
            Load();
        }

        public static AppConfig Current => _config;

        public static void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFile, json);
                Logger.Info("User configuration saved successfully");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save user configuration", ex);
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    string json = File.ReadAllText(ConfigFile);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config == null)
                    {
                        _config = new AppConfig();
                        Logger.Warn("Configuration file exists but couldn't be parsed, using defaults");
                    }
                    else
                    {
                        _config = config;
                        Logger.Info("User configuration loaded successfully");
                    }
                }
                else
                {
                    _config = new AppConfig();
                    Logger.Info("No configuration file found, creating default configuration");
                    Save();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load user configuration, using defaults", ex);
                _config = new AppConfig();
            }
        }
    }
}
