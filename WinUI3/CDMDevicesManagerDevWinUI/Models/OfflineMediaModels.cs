using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace CMDevicesManager.Models
{
    /// <summary>
    /// Represents a media file stored for offline mode on a device
    /// </summary>
    public class DeviceMediaFile
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("originalPath")]
        public string OriginalPath { get; set; } = string.Empty;

        [JsonPropertyName("localPath")]
        public string LocalPath { get; set; } = string.Empty;

        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }

        [JsonPropertyName("md5Hash")]
        public string MD5Hash { get; set; } = string.Empty;

        [JsonPropertyName("mediaType")]
        public MediaType MediaType { get; set; }

        [JsonPropertyName("slotIndex")]
        public int SlotIndex { get; set; }

        [JsonPropertyName("addedDateTime")]
        public DateTime AddedDateTime { get; set; }

        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("transferId")]
        public byte TransferId { get; set; }

        /// <summary>
        /// Gets the file extension
        /// </summary>
        [JsonIgnore]
        public string FileExtension => System.IO.Path.GetExtension(FileName).ToLowerInvariant();

        /// <summary>
        /// Gets whether this is a video file
        /// </summary>
        [JsonIgnore]
        public bool IsVideoFile
        {
            get
            {
                var videoExtensions = new[] { ".mp4" };
                return Array.Exists(videoExtensions, ext => ext == FileExtension);
            }
        }

        /// <summary>
        /// Gets whether this is an image file
        /// </summary>
        [JsonIgnore]
        public bool IsImageFile
        {
            get
            {
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp" };
                return Array.Exists(imageExtensions, ext => ext == FileExtension);
            }
        }

        public DeviceMediaFile()
        {
            AddedDateTime = DateTime.Now;
            LastModified = DateTime.Now;
        }

        public DeviceMediaFile(string fileName, string originalPath, string localPath, long fileSize, MediaType mediaType, int slotIndex)
        {
            FileName = fileName;
            OriginalPath = originalPath;
            LocalPath = localPath;
            FileSize = fileSize;
            MediaType = mediaType;
            SlotIndex = slotIndex;
            AddedDateTime = DateTime.Now;
            LastModified = DateTime.Now;
            IsActive = true;
        }

        /// <summary>
        /// Updates the last modified timestamp
        /// </summary>
        public void Touch()
        {
            LastModified = DateTime.Now;
        }

        public override string ToString()
        {
            return $"{FileName} ({MediaType}, Slot {SlotIndex}, {FileSize} bytes)";
        }
    }

    /// <summary>
    /// Represents device offline mode configuration and media files
    /// </summary>
    public class DeviceOfflineData
    {
        [JsonPropertyName("deviceSerial")]
        public string DeviceSerial { get; set; } = string.Empty;

        [JsonPropertyName("devicePath")]
        public string? DevicePath { get; set; }

        [JsonPropertyName("productName")]
        public string? ProductName { get; set; }

        [JsonPropertyName("manufacturerName")]
        public string? ManufacturerName { get; set; }

        [JsonPropertyName("hardwareVersion")]
        public string? HardwareVersion { get; set; }

        [JsonPropertyName("firmwareVersion")]
        public string? FirmwareVersion { get; set; }

        [JsonPropertyName("lastSeen")]
        public DateTime LastSeen { get; set; }

        [JsonPropertyName("isConnected")]
        public bool IsConnected { get; set; }

        [JsonPropertyName("suspendMediaFiles")]
        public List<DeviceMediaFile> SuspendMediaFiles { get; set; } = new();

        [JsonPropertyName("backgroundMediaFile")]
        public DeviceMediaFile? BackgroundMediaFile { get; set; }

        [JsonPropertyName("logoMediaFile")]
        public DeviceMediaFile? LogoMediaFile { get; set; }

        [JsonPropertyName("osdMediaFile")]
        public DeviceMediaFile? OsdMediaFile { get; set; }

        [JsonPropertyName("deviceSettings")]
        public DeviceOfflineSettings Settings { get; set; } = new();

        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Gets the count of active suspend media files
        /// </summary>
        [JsonIgnore]
        public int ActiveSuspendMediaCount => SuspendMediaFiles.Count(f => f.IsActive);

        /// <summary>
        /// Gets the maximum slot index used
        /// </summary>
        [JsonIgnore]
        public int MaxSlotIndex => SuspendMediaFiles.Count > 0 ? SuspendMediaFiles.Max(f => f.SlotIndex) : -1;

        /// <summary>
        /// Gets available slot indices for new media
        /// </summary>
        [JsonIgnore]
        public List<int> AvailableSlots
        {
            get
            {
                var usedSlots = SuspendMediaFiles.Where(f => f.IsActive).Select(f => f.SlotIndex).ToHashSet();
                var availableSlots = new List<int>();
                for (int i = 0; i < 5; i++) // Assuming max 5 slots
                {
                    if (!usedSlots.Contains(i))
                    {
                        availableSlots.Add(i);
                    }
                }
                return availableSlots;
            }
        }

        public DeviceOfflineData()
        {
            LastSeen = DateTime.Now;
            LastUpdated = DateTime.Now;
        }

        public DeviceOfflineData(string deviceSerial)
        {
            DeviceSerial = deviceSerial;
            LastSeen = DateTime.Now;
            LastUpdated = DateTime.Now;
        }

        /// <summary>
        /// Updates the last seen and last updated timestamps
        /// </summary>
        public void Touch()
        {
            LastSeen = DateTime.Now;
            LastUpdated = DateTime.Now;
        }

        /// <summary>
        /// Adds or updates a suspend media file
        /// </summary>
        public void AddOrUpdateSuspendMedia(DeviceMediaFile mediaFile)
        {
            // Remove existing media at the same slot
            SuspendMediaFiles.RemoveAll(f => f.SlotIndex == mediaFile.SlotIndex);
            
            // Add the new media file
            SuspendMediaFiles.Add(mediaFile);
            Touch();
        }

        /// <summary>
        /// Removes suspend media file by slot index
        /// </summary>
        public bool RemoveSuspendMedia(int slotIndex)
        {
            var removed = SuspendMediaFiles.RemoveAll(f => f.SlotIndex == slotIndex) > 0;
            if (removed)
            {
                Touch();
            }
            return removed;
        }

        /// <summary>
        /// Clears all suspend media files
        /// </summary>
        public void ClearAllSuspendMedia()
        {
            SuspendMediaFiles.Clear();
            Touch();
        }

        /// <summary>
        /// Gets suspend media file by slot index
        /// </summary>
        public DeviceMediaFile? GetSuspendMediaBySlot(int slotIndex)
        {
            return SuspendMediaFiles.FirstOrDefault(f => f.SlotIndex == slotIndex && f.IsActive);
        }

        public override string ToString()
        {
            return $"Device {DeviceSerial} ({ProductName}) - {ActiveSuspendMediaCount} media files";
        }
    }

    /// <summary>
    /// Represents device offline mode settings
    /// </summary>
    public class DeviceOfflineSettings
    {
        [JsonPropertyName("brightness")]
        public int Brightness { get; set; } = 80;

        [JsonPropertyName("rotation")]
        public int Rotation { get; set; } = 0;

        [JsonPropertyName("keepAliveTimeout")]
        public int KeepAliveTimeout { get; set; } = 5;

        [JsonPropertyName("displayInSleep")]
        public bool DisplayInSleep { get; set; } = false;

        [JsonPropertyName("realTimeDisplayEnabled")]
        public bool RealTimeDisplayEnabled { get; set; } = false;

        [JsonPropertyName("suspendModeEnabled")]
        public bool SuspendModeEnabled { get; set; } = false;

        [JsonPropertyName("autoSyncEnabled")]
        public bool AutoSyncEnabled { get; set; } = true;

        [JsonPropertyName("playbackMode")]
        public PlaybackMode PlaybackMode { get; set; } = PlaybackMode.RealtimeConfig;

        [JsonPropertyName("lastSyncDateTime")]
        public DateTime? LastSyncDateTime { get; set; }

        public override string ToString()
        {
            return $"Brightness: {Brightness}%, Rotation: {Rotation}°, KeepAlive: {KeepAliveTimeout}s, PlaybackMode: {PlaybackMode}";
        }
    }

    /// <summary>
    /// Represents the complete offline media database
    /// </summary>
    public class OfflineMediaDatabase
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("created")]
        public DateTime Created { get; set; }

        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; }

        [JsonPropertyName("devices")]
        public Dictionary<string, DeviceOfflineData> Devices { get; set; } = new();

        [JsonPropertyName("globalSettings")]
        public GlobalOfflineSettings GlobalSettings { get; set; } = new();

        /// <summary>
        /// Gets the count of tracked devices
        /// </summary>
        [JsonIgnore]
        public int DeviceCount => Devices.Count;

        /// <summary>
        /// Gets the count of connected devices
        /// </summary>
        [JsonIgnore]
        public int ConnectedDeviceCount => Devices.Values.Count(d => d.IsConnected);

        /// <summary>
        /// Gets the total count of all media files across all devices
        /// </summary>
        [JsonIgnore]
        public int TotalMediaFileCount => Devices.Values.Sum(d => d.SuspendMediaFiles.Count + 
            (d.BackgroundMediaFile != null ? 1 : 0) + 
            (d.LogoMediaFile != null ? 1 : 0) + 
            (d.OsdMediaFile != null ? 1 : 0));

        public OfflineMediaDatabase()
        {
            Created = DateTime.Now;
            LastModified = DateTime.Now;
        }

        /// <summary>
        /// Updates the last modified timestamp
        /// </summary>
        public void Touch()
        {
            LastModified = DateTime.Now;
        }

        /// <summary>
        /// Gets or creates device offline data by serial number
        /// </summary>
        public DeviceOfflineData GetOrCreateDevice(string deviceSerial)
        {
            if (Devices.TryGetValue(deviceSerial, out var device))
            {
                return device;
            }

            device = new DeviceOfflineData(deviceSerial);
            Devices[deviceSerial] = device;
            Touch();
            return device;
        }

        /// <summary>
        /// Removes device data by serial number
        /// </summary>
        public bool RemoveDevice(string deviceSerial)
        {
            var removed = Devices.Remove(deviceSerial);
            if (removed)
            {
                Touch();
            }
            return removed;
        }

        /// <summary>
        /// Gets devices that haven't been seen for a specified duration
        /// </summary>
        public List<DeviceOfflineData> GetStaleDevices(TimeSpan maxAge)
        {
            var cutoffTime = DateTime.Now - maxAge;
            return Devices.Values.Where(d => d.LastSeen < cutoffTime).ToList();
        }

        public override string ToString()
        {
            return $"OfflineMediaDatabase: {DeviceCount} devices, {TotalMediaFileCount} media files";
        }
    }

    /// <summary>
    /// Global settings for offline media management
    /// </summary>
    public class GlobalOfflineSettings
    {
        [JsonPropertyName("autoCleanupEnabled")]
        public bool AutoCleanupEnabled { get; set; } = true;

        [JsonPropertyName("maxDeviceAgeHours")]
        public int MaxDeviceAgeHours { get; set; } = 720; // 30 days

        [JsonPropertyName("mediasDirectoryPath")]
        public string MediasDirectoryPath { get; set; } = "medias";

        [JsonPropertyName("enableBackup")]
        public bool EnableBackup { get; set; } = true;

        [JsonPropertyName("maxBackupFiles")]
        public int MaxBackupFiles { get; set; } = 10;

        [JsonPropertyName("compressionEnabled")]
        public bool CompressionEnabled { get; set; } = false;

        [JsonPropertyName("lastBackupDateTime")]
        public DateTime? LastBackupDateTime { get; set; }
    }

    /// <summary>
    /// Playback mode enumeration for device play mode page
    /// </summary>
    public enum PlaybackMode
    {
        [Description("Realtime Config")]
        RealtimeConfig = 0,

        [Description("Offline Video")]
        OfflineVideo = 1
    }

    /// <summary>
    /// Types of media files
    /// </summary>
    public enum MediaType
    {
        [Description("Suspend Media")]
        Suspend = 0,

        [Description("Background Image")]
        Background = 1,

        [Description("Logo/Powerup Media")]
        Logo = 2,

        [Description("OSD Media")]
        Osd = 3,

        [Description("Firmware")]
        Firmware = 4,

        [Description("Real-time Media")]
        RealTime = 5
    }

    /// <summary>
    /// Media file status enumeration
    /// </summary>
    public enum MediaFileStatus
    {
        [Description("Unknown")]
        Unknown = 0,

        [Description("Local Only")]
        LocalOnly = 1,

        [Description("Device Only")]
        DeviceOnly = 2,

        [Description("Synchronized")]
        Synchronized = 3,

        [Description("Sync Pending")]
        SyncPending = 4,

        [Description("Sync Failed")]
        SyncFailed = 5,

        [Description("Missing")]
        Missing = 6
    }
}