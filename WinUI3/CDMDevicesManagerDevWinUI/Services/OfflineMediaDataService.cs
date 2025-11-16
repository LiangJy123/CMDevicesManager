using CMDevicesManager.Models;
using CMDevicesManager.Helper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using Timer = System.Threading.Timer;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// Service for managing offline media data storage using JSON persistence
    /// </summary>
    public class OfflineMediaDataService : IDisposable
    {
        private readonly string _dataFilePath;
        private readonly string _backupDirectoryPath;
        private readonly string _mediasDirectoryPath;
        private OfflineMediaDatabase _database;
        private readonly object _lockObject = new object();
        private readonly Timer _autoSaveTimer;
        private readonly Timer _cleanupTimer;
        private bool _disposed = false;
        private bool _hasUnsavedChanges = false;

        /// <summary>
        /// Event fired when the database is saved
        /// </summary>
        public event EventHandler<DatabaseSavedEventArgs>? DatabaseSaved;

        /// <summary>
        /// Event fired when a device is added or updated
        /// </summary>
        public event EventHandler<DeviceDataChangedEventArgs>? DeviceDataChanged;

        /// <summary>
        /// Event fired when media files are added, updated, or removed
        /// </summary>
        public event EventHandler<MediaFileChangedEventArgs>? MediaFileChanged;

        /// <summary>
        /// Event fired when an error occurs
        /// </summary>
        public event EventHandler<DataServiceErrorEventArgs>? ErrorOccurred;

        public OfflineMediaDataService(string? dataDirectory = null)
        {
            var baseDirectory = dataDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            
            // Ensure data directory exists
            Directory.CreateDirectory(baseDirectory);
            
            _dataFilePath = Path.Combine(baseDirectory, "status_offline_media_data.json");
            _backupDirectoryPath = Path.Combine(baseDirectory, "backups");
            _mediasDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias");

            // Ensure backup and medias directories exist
            Directory.CreateDirectory(_backupDirectoryPath);
            Directory.CreateDirectory(_mediasDirectoryPath);

            // Initialize database
            _database = LoadDatabase();

            // Setup auto-save timer (every 30 seconds)
            _autoSaveTimer = new Timer(AutoSaveCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            // Setup cleanup timer (every hour)
            _cleanupTimer = new Timer(CleanupCallback, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

            Logger.Info($"OfflineMediaDataService initialized. Database: {_database.DeviceCount} devices, {_database.TotalMediaFileCount} media files");
        }

        #region Database Management

        /// <summary>
        /// Loads the database from disk, creating a new one if it doesn't exist
        /// </summary>
        private OfflineMediaDatabase LoadDatabase()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var jsonContent = File.ReadAllText(_dataFilePath);
                    var database = JsonSerializer.Deserialize<OfflineMediaDatabase>(jsonContent);
                    
                    if (database != null)
                    {
                        Logger.Info($"Loaded offline media database with {database.DeviceCount} devices");
                        return database;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load offline media database: {ex.Message}", ex);
                ErrorOccurred?.Invoke(this, new DataServiceErrorEventArgs("Database Load", ex));
                
                // Try to restore from backup
                var restoredDatabase = TryRestoreFromBackup();
                if (restoredDatabase != null)
                {
                    return restoredDatabase;
                }
            }

            Logger.Info("Creating new offline media database");
            return new OfflineMediaDatabase();
        }

        /// <summary>
        /// Saves the database to disk
        /// </summary>
        public async Task SaveDatabaseAsync()
        {
            try
            {
                lock (_lockObject)
                {
                    _database.Touch();
                    
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    var jsonContent = JsonSerializer.Serialize(_database, options);
                    
                    // Create backup before saving
                    if (File.Exists(_dataFilePath) && _database.GlobalSettings.EnableBackup)
                    {
                        CreateBackup();
                    }
                    
                    File.WriteAllText(_dataFilePath, jsonContent);
                    _hasUnsavedChanges = false;
                }

                Logger.Info($"Offline media database saved with {_database.DeviceCount} devices");
                DatabaseSaved?.Invoke(this, new DatabaseSavedEventArgs(_database.DeviceCount, _database.TotalMediaFileCount));
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save offline media database: {ex.Message}", ex);
                ErrorOccurred?.Invoke(this, new DataServiceErrorEventArgs("Database Save", ex));
                throw;
            }
        }

        /// <summary>
        /// Forces immediate save of the database
        /// </summary>
        public void SaveDatabase()
        {
            Task.Run(async () => await SaveDatabaseAsync());
        }

        /// <summary>
        /// Creates a backup of the current database
        /// </summary>
        private void CreateBackup()
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = $"offline_media_data_backup_{timestamp}.json";
                var backupFilePath = Path.Combine(_backupDirectoryPath, backupFileName);

                File.Copy(_dataFilePath, backupFilePath, overwrite: true);

                // Clean up old backups
                var backupFiles = Directory.GetFiles(_backupDirectoryPath, "offline_media_data_backup_*.json")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .Skip(_database.GlobalSettings.MaxBackupFiles)
                    .ToList();

                foreach (var oldBackup in backupFiles)
                {
                    try
                    {
                        File.Delete(oldBackup);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to delete old backup {oldBackup}: {ex.Message}");
                    }
                }

                _database.GlobalSettings.LastBackupDateTime = DateTime.Now;
                Logger.Info($"Database backup created: {backupFileName}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to create database backup: {ex.Message}");
            }
        }

        /// <summary>
        /// Tries to restore database from the most recent backup
        /// </summary>
        private OfflineMediaDatabase? TryRestoreFromBackup()
        {
            try
            {
                var backupFiles = Directory.GetFiles(_backupDirectoryPath, "offline_media_data_backup_*.json")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToList();

                foreach (var backupFile in backupFiles)
                {
                    try
                    {
                        var jsonContent = File.ReadAllText(backupFile);
                        var database = JsonSerializer.Deserialize<OfflineMediaDatabase>(jsonContent);
                        
                        if (database != null)
                        {
                            Logger.Info($"Restored database from backup: {Path.GetFileName(backupFile)}");
                            return database;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to restore from backup {backupFile}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to restore from backup: {ex.Message}", ex);
            }

            return null;
        }

        private void AutoSaveCallback(object? state)
        {
            if (_hasUnsavedChanges)
            {
                try
                {
                    SaveDatabase();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Auto-save failed: {ex.Message}", ex);
                }
            }
        }

        private void CleanupCallback(object? state)
        {
            try
            {
                if (_database.GlobalSettings.AutoCleanupEnabled)
                {
                    CleanupStaleDevices();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Cleanup failed: {ex.Message}", ex);
            }
        }

        #endregion

        #region Device Management

        /// <summary>
        /// Gets all device data
        /// </summary>
        public Dictionary<string, DeviceOfflineData> GetAllDevices()
        {
            lock (_lockObject)
            {
                return new Dictionary<string, DeviceOfflineData>(_database.Devices);
            }
        }

        /// <summary>
        /// Gets device data by serial number
        /// </summary>
        public DeviceOfflineData? GetDevice(string deviceSerial)
        {
            if (string.IsNullOrEmpty(deviceSerial)) return null;

            lock (_lockObject)
            {
                return _database.Devices.TryGetValue(deviceSerial, out var device) ? device : null;
            }
        }

        /// <summary>
        /// Gets or creates device data by serial number
        /// </summary>
        public DeviceOfflineData GetOrCreateDevice(string deviceSerial)
        {
            if (string.IsNullOrEmpty(deviceSerial))
                throw new ArgumentException("Device serial cannot be null or empty", nameof(deviceSerial));

            lock (_lockObject)
            {
                var device = _database.GetOrCreateDevice(deviceSerial);
                _hasUnsavedChanges = true;
                return device;
            }
        }

        /// <summary>
        /// Updates device information from DeviceInfo
        /// </summary>
        public void UpdateDeviceInfo(string deviceSerial, HidApi.DeviceInfo deviceInfo, bool isConnected = true)
        {
            lock (_lockObject)
            {
                var device = _database.GetOrCreateDevice(deviceSerial);
                
                device.DevicePath = deviceInfo.Path;
                device.ProductName = deviceInfo.ProductString;
                device.ManufacturerName = deviceInfo.ManufacturerString;
                device.IsConnected = isConnected;
                device.Touch();
                
                _hasUnsavedChanges = true;
                
                DeviceDataChanged?.Invoke(this, new DeviceDataChangedEventArgs(device, "DeviceInfo"));
            }
        }

        /// <summary>
        /// Updates device firmware information
        /// </summary>
        public void UpdateDeviceFirmwareInfo(string deviceSerial, HID.DisplayController.DeviceFWInfo firmwareInfo)
        {
            lock (_lockObject)
            {
                var device = _database.GetOrCreateDevice(deviceSerial);
                
                device.HardwareVersion = firmwareInfo.HardwareVersion.ToString();
                device.FirmwareVersion = firmwareInfo.FirmwareVersion.ToString();
                device.Touch();
                
                _hasUnsavedChanges = true;
                
                DeviceDataChanged?.Invoke(this, new DeviceDataChangedEventArgs(device, "FirmwareInfo"));
            }
        }

        /// <summary>
        /// Updates device connection status
        /// </summary>
        public void SetDeviceConnectionStatus(string deviceSerial, bool isConnected)
        {
            lock (_lockObject)
            {
                var device = GetDevice(deviceSerial);
                if (device != null)
                {
                    device.IsConnected = isConnected;
                    device.Touch();
                    _hasUnsavedChanges = true;
                    
                    DeviceDataChanged?.Invoke(this, new DeviceDataChangedEventArgs(device, "ConnectionStatus"));
                }
            }
        }

        /// <summary>
        /// Removes device data
        /// </summary>
        public bool RemoveDevice(string deviceSerial)
        {
            lock (_lockObject)
            {
                var device = GetDevice(deviceSerial);
                var removed = _database.RemoveDevice(deviceSerial);
                
                if (removed)
                {
                    _hasUnsavedChanges = true;
                    DeviceDataChanged?.Invoke(this, new DeviceDataChangedEventArgs(device!, "DeviceRemoved"));
                }
                
                return removed;
            }
        }

        /// <summary>
        /// Cleans up devices that haven't been seen for the configured maximum age
        /// </summary>
        public void CleanupStaleDevices()
        {
            lock (_lockObject)
            {
                var maxAge = TimeSpan.FromHours(_database.GlobalSettings.MaxDeviceAgeHours);
                var staleDevices = _database.GetStaleDevices(maxAge);
                
                foreach (var staleDevice in staleDevices)
                {
                    Logger.Info($"Removing stale device: {staleDevice.DeviceSerial} (last seen: {staleDevice.LastSeen})");
                    _database.RemoveDevice(staleDevice.DeviceSerial);
                }
                
                if (staleDevices.Count > 0)
                {
                    _hasUnsavedChanges = true;
                    Logger.Info($"Cleaned up {staleDevices.Count} stale devices");
                }
            }
        }

        #endregion

        #region Media File Management

        /// <summary>
        /// Adds or updates a suspend media file for a device
        /// </summary>
        public void AddSuspendMediaFile(string deviceSerial, string fileName, string originalPath, int slotIndex, byte transferId = 0)
        {
            lock (_lockObject)
            {
                var device = _database.GetOrCreateDevice(deviceSerial);
                
                // Calculate local path and other properties
                var fileExtension = Path.GetExtension(originalPath);
                var localFileName = $"suspend_{slotIndex}{fileExtension}";
                var localPath = Path.Combine(_mediasDirectoryPath, deviceSerial, localFileName);
                
                // Ensure device-specific medias directory exists
                var deviceMediasDir = Path.Combine(_mediasDirectoryPath, deviceSerial);
                Directory.CreateDirectory(deviceMediasDir);
                
                // Calculate file size and MD5 hash
                var fileInfo = new FileInfo(originalPath);
                var md5Hash = CalculateMD5Hash(originalPath);
                
                var mediaFile = new DeviceMediaFile(fileName, originalPath, localPath, fileInfo.Length, MediaType.Suspend, slotIndex)
                {
                    MD5Hash = md5Hash,
                    TransferId = transferId
                };
                
                // Copy file to local medias directory
                try
                {
                    File.Copy(originalPath, localPath, overwrite: true);
                    Logger.Info($"Copied media file to local storage: {localPath}");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to copy media file to local storage: {ex.Message}");
                }
                
                device.AddOrUpdateSuspendMedia(mediaFile);
                _hasUnsavedChanges = true;
                
                MediaFileChanged?.Invoke(this, new MediaFileChangedEventArgs(device, mediaFile, "SuspendMediaAdded"));
            }
        }

        /// <summary>
        /// Removes a suspend media file by slot index
        /// </summary>
        public bool RemoveSuspendMediaFile(string deviceSerial, int slotIndex)
        {
            lock (_lockObject)
            {
                var device = GetDevice(deviceSerial);
                if (device == null) return false;
                
                var mediaFile = device.GetSuspendMediaBySlot(slotIndex);
                var removed = device.RemoveSuspendMedia(slotIndex);
                
                if (removed)
                {
                    // Try to delete local file
                    if (mediaFile != null && File.Exists(mediaFile.LocalPath))
                    {
                        try
                        {
                            File.Delete(mediaFile.LocalPath);
                            Logger.Info($"Deleted local media file: {mediaFile.LocalPath}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to delete local media file: {ex.Message}");
                        }
                    }
                    
                    _hasUnsavedChanges = true;
                    MediaFileChanged?.Invoke(this, new MediaFileChangedEventArgs(device, mediaFile, "SuspendMediaRemoved"));
                }
                
                return removed;
            }
        }

        /// <summary>
        /// Clears all suspend media files for a device
        /// </summary>
        public void ClearAllSuspendMediaFiles(string deviceSerial)
        {
            lock (_lockObject)
            {
                var device = GetDevice(deviceSerial);
                if (device == null) return;
                
                // Delete local files
                foreach (var mediaFile in device.SuspendMediaFiles.ToList())
                {
                    if (File.Exists(mediaFile.LocalPath))
                    {
                        try
                        {
                            File.Delete(mediaFile.LocalPath);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to delete local media file: {ex.Message}");
                        }
                    }
                }
                
                device.ClearAllSuspendMedia();
                _hasUnsavedChanges = true;
                
                MediaFileChanged?.Invoke(this, new MediaFileChangedEventArgs(device, null, "AllSuspendMediaCleared"));
            }
        }

        /// <summary>
        /// Sets background media file for a device
        /// </summary>
        public void SetBackgroundMediaFile(string deviceSerial, string fileName, string originalPath, byte transferId = 0)
        {
            lock (_lockObject)
            {
                var device = _database.GetOrCreateDevice(deviceSerial);
                
                var fileExtension = Path.GetExtension(originalPath);
                var localFileName = $"background{fileExtension}";
                var localPath = Path.Combine(_mediasDirectoryPath, deviceSerial, localFileName);
                
                var deviceMediasDir = Path.Combine(_mediasDirectoryPath, deviceSerial);
                Directory.CreateDirectory(deviceMediasDir);
                
                var fileInfo = new FileInfo(originalPath);
                var md5Hash = CalculateMD5Hash(originalPath);
                
                var mediaFile = new DeviceMediaFile(fileName, originalPath, localPath, fileInfo.Length, MediaType.Background, -1)
                {
                    MD5Hash = md5Hash,
                    TransferId = transferId
                };
                
                try
                {
                    File.Copy(originalPath, localPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to copy background media file: {ex.Message}");
                }
                
                device.BackgroundMediaFile = mediaFile;
                device.Touch();
                _hasUnsavedChanges = true;
                
                MediaFileChanged?.Invoke(this, new MediaFileChangedEventArgs(device, mediaFile, "BackgroundMediaSet"));
            }
        }

        /// <summary>
        /// Gets all suspend media files for a device
        /// </summary>
        public List<DeviceMediaFile> GetSuspendMediaFiles(string deviceSerial)
        {
            lock (_lockObject)
            {
                var device = GetDevice(deviceSerial);
                return device?.SuspendMediaFiles.Where(f => f.IsActive).ToList() ?? new List<DeviceMediaFile>();
            }
        }

        /// <summary>
        /// Gets suspend media file by slot index
        /// </summary>
        public DeviceMediaFile? GetSuspendMediaFile(string deviceSerial, int slotIndex)
        {
            lock (_lockObject)
            {
                var device = GetDevice(deviceSerial);
                return device?.GetSuspendMediaBySlot(slotIndex);
            }
        }

        /// <summary>
        /// Syncs media files from local storage for a device
        /// </summary>
        public void SyncMediaFilesFromLocal(string deviceSerial)
        {
            try
            {
                var deviceMediasDir = Path.Combine(_mediasDirectoryPath, deviceSerial);
                if (!Directory.Exists(deviceMediasDir)) return;

                var suspendFiles = Directory.GetFiles(deviceMediasDir, "suspend_*.*");
                
                foreach (var file in suspendFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var match = System.Text.RegularExpressions.Regex.Match(fileName, @"suspend_(\d+)\..*");
                    
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int slotIndex))
                    {
                        // Check if this file is already tracked
                        var existingMedia = GetSuspendMediaFile(deviceSerial, slotIndex);
                        if (existingMedia == null || !File.Exists(existingMedia.LocalPath))
                        {
                            // Add this file to the database
                            AddSuspendMediaFile(deviceSerial, fileName, file, slotIndex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to sync media files from local storage for device {deviceSerial}: {ex.Message}", ex);
            }
        }

        #endregion

        #region Device Settings Management

        /// <summary>
        /// Updates device settings
        /// </summary>
        public void UpdateDeviceSettings(string deviceSerial, DeviceOfflineSettings settings)
        {
            lock (_lockObject)
            {
                var device = _database.GetOrCreateDevice(deviceSerial);
                device.Settings = settings;
                device.Touch();
                _hasUnsavedChanges = true;
                
                DeviceDataChanged?.Invoke(this, new DeviceDataChangedEventArgs(device, "SettingsUpdated"));
            }
        }

        /// <summary>
        /// Updates specific device setting
        /// </summary>
        public void UpdateDeviceSetting(string deviceSerial, string settingName, object value)
        {
            lock (_lockObject)
            {
                var device = _database.GetOrCreateDevice(deviceSerial);
                
                switch (settingName.ToLower())
                {
                    case "brightness":
                        if (value is int brightness) device.Settings.Brightness = brightness;
                        break;
                    case "rotation":
                        if (value is int rotation) device.Settings.Rotation = rotation;
                        break;
                    case "keepalivetimeout":
                        if (value is int timeout) device.Settings.KeepAliveTimeout = timeout;
                        break;
                    case "displayinsleep":
                        if (value is bool sleep) device.Settings.DisplayInSleep = sleep;
                        break;
                    case "realtimedisplayenabled":
                        if (value is bool realtime) device.Settings.RealTimeDisplayEnabled = realtime;
                        break;
                    case "suspendmodeenabled":
                        if (value is bool suspend) device.Settings.SuspendModeEnabled = suspend;
                        break;
                    case "autosyncenabled":
                        if (value is bool autoSync) device.Settings.AutoSyncEnabled = autoSync;
                        break;
                    case "playbackmode":
                        if (value is PlaybackMode playbackMode) device.Settings.PlaybackMode = playbackMode;
                        break;
                }
                
                device.Touch();
                _hasUnsavedChanges = true;
                
                DeviceDataChanged?.Invoke(this, new DeviceDataChangedEventArgs(device, $"Setting:{settingName}"));
            }
        }

        /// <summary>
        /// Updates device playback mode
        /// </summary>
        public void UpdateDevicePlaybackMode(string deviceSerial, PlaybackMode playbackMode)
        {
            lock (_lockObject)
            {
                var device = _database.GetOrCreateDevice(deviceSerial);
                device.Settings.PlaybackMode = playbackMode;
                device.Touch();
                _hasUnsavedChanges = true;
                
                DeviceDataChanged?.Invoke(this, new DeviceDataChangedEventArgs(device, "PlaybackMode"));
                Logger.Info($"Updated playback mode for device {deviceSerial} to {playbackMode}");
            }
        }

        /// <summary>
        /// Gets device playback mode
        /// </summary>
        public PlaybackMode GetDevicePlaybackMode(string deviceSerial)
        {
            lock (_lockObject)
            {
                var device = GetDevice(deviceSerial);
                return device?.Settings.PlaybackMode ?? PlaybackMode.RealtimeConfig;
            }
        }

        /// <summary>
        /// Gets device settings
        /// </summary>
        public DeviceOfflineSettings GetDeviceSettings(string deviceSerial)
        {
            lock (_lockObject)
            {
                var device = GetDevice(deviceSerial);
                return device?.Settings ?? new DeviceOfflineSettings();
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Calculates MD5 hash of a file
        /// </summary>
        private string CalculateMD5Hash(string filePath)
        {
            try
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(filePath);
                var hashBytes = md5.ComputeHash(stream);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to calculate MD5 hash for {filePath}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets database statistics
        /// </summary>
        public DatabaseStatistics GetStatistics()
        {
            lock (_lockObject)
            {
                return new DatabaseStatistics
                {
                    TotalDevices = _database.DeviceCount,
                    ConnectedDevices = _database.ConnectedDeviceCount,
                    TotalMediaFiles = _database.TotalMediaFileCount,
                    DatabaseSizeBytes = File.Exists(_dataFilePath) ? new FileInfo(_dataFilePath).Length : 0,
                    LastModified = _database.LastModified,
                    HasUnsavedChanges = _hasUnsavedChanges
                };
            }
        }

        /// <summary>
        /// Exports database to a specified file
        /// </summary>
        public async Task ExportDatabaseAsync(string exportPath)
        {
            try
            {
                lock (_lockObject)
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    var jsonContent = JsonSerializer.Serialize(_database, options);
                    File.WriteAllText(exportPath, jsonContent);
                }

                Logger.Info($"Database exported to: {exportPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to export database: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Imports database from a specified file
        /// </summary>
        public async Task ImportDatabaseAsync(string importPath, bool mergeWithExisting = false)
        {
            try
            {
                var jsonContent = File.ReadAllText(importPath);
                var importedDatabase = JsonSerializer.Deserialize<OfflineMediaDatabase>(jsonContent);
                
                if (importedDatabase == null)
                {
                    throw new InvalidOperationException("Failed to deserialize imported database");
                }

                lock (_lockObject)
                {
                    if (mergeWithExisting)
                    {
                        // Merge imported devices with existing ones
                        foreach (var kvp in importedDatabase.Devices)
                        {
                            var existingDevice = GetDevice(kvp.Key);
                            if (existingDevice == null)
                            {
                                _database.Devices[kvp.Key] = kvp.Value;
                            }
                            else
                            {
                                // Merge logic - prefer newer data
                                if (kvp.Value.LastUpdated > existingDevice.LastUpdated)
                                {
                                    _database.Devices[kvp.Key] = kvp.Value;
                                }
                            }
                        }
                    }
                    else
                    {
                        _database = importedDatabase;
                    }
                    
                    _hasUnsavedChanges = true;
                }

                Logger.Info($"Database imported from: {importPath}");
                await SaveDatabaseAsync();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to import database: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Updates device suspend media status from device status
        /// </summary>
        public void UpdateSuspendMediaStatus(string deviceSerial, int[] suspendMediaActive)
        {
            lock (_lockObject)
            {
                var device = GetDevice(deviceSerial);
                if (device == null) return;

                // Update the active status of suspend media files based on device status
                for (int i = 0; i < suspendMediaActive.Length && i < device.SuspendMediaFiles.Count; i++)
                {
                    var mediaFile = device.SuspendMediaFiles.FirstOrDefault(f => f.SlotIndex == i);
                    if (mediaFile != null)
                    {
                        bool shouldBeActive = suspendMediaActive[i] != 0;
                        if (mediaFile.IsActive != shouldBeActive)
                        {
                            mediaFile.IsActive = shouldBeActive;
                            mediaFile.Touch();
                            _hasUnsavedChanges = true;
                        }
                    }
                }

                device.Touch();
                
                if (_hasUnsavedChanges)
                {
                    MediaFileChanged?.Invoke(this, new MediaFileChangedEventArgs(device, null, "SuspendMediaStatusUpdated"));
                }
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Save any pending changes
                if (_hasUnsavedChanges)
                {
                    SaveDatabase();
                }

                _autoSaveTimer?.Dispose();
                _cleanupTimer?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during OfflineMediaDataService disposal: {ex.Message}", ex);
            }

            _disposed = true;
        }
    }

    #region Event Arguments and Supporting Classes

    /// <summary>
    /// Event arguments for database saved events
    /// </summary>
    public class DatabaseSavedEventArgs : EventArgs
    {
        public int DeviceCount { get; }
        public int MediaFileCount { get; }
        public DateTime Timestamp { get; }

        public DatabaseSavedEventArgs(int deviceCount, int mediaFileCount)
        {
            DeviceCount = deviceCount;
            MediaFileCount = mediaFileCount;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for device data changes
    /// </summary>
    public class DeviceDataChangedEventArgs : EventArgs
    {
        public DeviceOfflineData Device { get; }
        public string ChangeType { get; }
        public DateTime Timestamp { get; }

        public DeviceDataChangedEventArgs(DeviceOfflineData device, string changeType)
        {
            Device = device;
            ChangeType = changeType;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for media file changes
    /// </summary>
    public class MediaFileChangedEventArgs : EventArgs
    {
        public DeviceOfflineData Device { get; }
        public DeviceMediaFile? MediaFile { get; }
        public string ChangeType { get; }
        public DateTime Timestamp { get; }

        public MediaFileChangedEventArgs(DeviceOfflineData device, DeviceMediaFile? mediaFile, string changeType)
        {
            Device = device;
            MediaFile = mediaFile;
            ChangeType = changeType;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for data service errors
    /// </summary>
    public class DataServiceErrorEventArgs : EventArgs
    {
        public string Operation { get; }
        public Exception Exception { get; }
        public DateTime Timestamp { get; }

        public DataServiceErrorEventArgs(string operation, Exception exception)
        {
            Operation = operation;
            Exception = exception;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Database statistics information
    /// </summary>
    public class DatabaseStatistics
    {
        public int TotalDevices { get; set; }
        public int ConnectedDevices { get; set; }
        public int TotalMediaFiles { get; set; }
        public long DatabaseSizeBytes { get; set; }
        public DateTime LastModified { get; set; }
        public bool HasUnsavedChanges { get; set; }

        public string DatabaseSizeFormatted => FormatFileSize(DatabaseSizeBytes);

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    #endregion
}