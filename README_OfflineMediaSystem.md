# Multi-Device Offline Media Management System

This system provides comprehensive support for managing offline mode media files across multiple devices using JSON data storage. Each device is uniquely identified by its serial number, enabling proper separation and management of media files for different devices.

## Features

### Core Functionality
- **Device-Specific Storage**: Each device has its own media file storage organized by serial number
- **JSON Database**: Persistent storage using JSON with automatic backup and versioning
- **Video Thumbnail Generation**: Automatic thumbnail generation for MP4 and other video files using FFMpegCore
- **Media Type Support**: Support for images (JPG, PNG, etc.) and videos (MP4, AVI, MOV, etc.)
- **Slot-Based Management**: Organize suspend media files in slots (0-4) for each device
- **Auto-Sync**: Automatic synchronization between local storage and device data
- **Multi-Device Support**: Manage multiple devices simultaneously with proper isolation

### Data Management
- **Automatic Backup**: Regular database backups with configurable retention
- **Export/Import**: Full database export and import with merge capabilities
- **Cleanup**: Automatic cleanup of stale device data
- **Statistics**: Comprehensive database statistics and monitoring
- **Event System**: Event-driven architecture for real-time updates

## Architecture

### Key Components

#### 1. OfflineMediaDataService
Main service for managing offline media data with JSON persistence.

```csharp
// Initialize the service
var offlineDataService = new OfflineMediaDataService();

// Add media file for a device
offlineDataService.AddSuspendMediaFile(
    deviceSerial: "DEV001A2B3C4D5E6F7",
    fileName: "suspend_0.mp4",
    originalPath: @"C:\media\video.mp4",
    slotIndex: 0,
    transferId: 1
);

// Get device media files
var mediaFiles = offlineDataService.GetSuspendMediaFiles("DEV001A2B3C4D5E6F7");
```

#### 2. Data Models

**DeviceOfflineData**: Represents a device and its associated media files
```csharp
public class DeviceOfflineData
{
    public string DeviceSerial { get; set; }
    public string? ProductName { get; set; }
    public bool IsConnected { get; set; }
    public List<DeviceMediaFile> SuspendMediaFiles { get; set; }
    public DeviceMediaFile? BackgroundMediaFile { get; set; }
    public DeviceOfflineSettings Settings { get; set; }
    // ... more properties
}
```

**DeviceMediaFile**: Represents a media file with metadata
```csharp
public class DeviceMediaFile
{
    public string FileName { get; set; }
    public string LocalPath { get; set; }
    public long FileSize { get; set; }
    public string MD5Hash { get; set; }
    public MediaType MediaType { get; set; }
    public int SlotIndex { get; set; }
    public bool IsVideoFile { get; }
    public bool IsImageFile { get; }
    // ... more properties
}
```

#### 3. Video Thumbnail Helper
Generates thumbnails for video files using FFMpegCore.

```csharp
// Generate video thumbnail
var thumbnail = await VideoThumbnailHelper.GenerateVideoThumbnailAsync(
    videoPath: @"C:\media\video.mp4",
    width: 200,
    height: 150
);

// Check if file is supported video
bool isVideo = VideoThumbnailHelper.IsSupportedVideoFile("video.mp4");
```

## Directory Structure

```
AppDirectory/
??? data/
?   ??? offline_media_data.json      # Main database file
?   ??? backups/                     # Automatic backups
?       ??? offline_media_data_backup_20241210_143022.json
?       ??? ...
??? medias/                          # Media files storage
?   ??? DEV001A2B3C4D5E6F7/         # Device-specific directory
?   ?   ??? suspend_0.mp4
?   ?   ??? suspend_1.jpg
?   ?   ??? background.jpg
?   ??? DEV002B3C4D5E6F7A8/         # Another device
?   ?   ??? suspend_0.png
?   ??? ...
??? exe/
    ??? ffmpeg.exe                   # For video processing
    ??? ffprobe.exe
```

## Usage Examples

### Basic Device Management

```csharp
// Get or create device data
var device = offlineDataService.GetOrCreateDevice("DEV001A2B3C4D5E6F7");

// Update device information
device.ProductName = "CM Display Controller HAF700V2";
device.IsConnected = true;

// Add suspend media to slot 0
offlineDataService.AddSuspendMediaFile(
    "DEV001A2B3C4D5E6F7",
    "suspend_0.mp4",
    @"C:\temp\video.mp4",
    slotIndex: 0
);

// Get media file from specific slot
var mediaFile = offlineDataService.GetSuspendMediaFile("DEV001A2B3C4D5E6F7", 0);
```

### Video Thumbnail Integration

```csharp
// In DeviceSettings.xaml.cs - video thumbnails are automatically generated
private async void UpdateMediaSlotUI(int slotIndex, string filePath, bool hasMedia)
{
    if (IsVideoFile(fileExtension))
    {
        // Check cache first
        if (_videoThumbnailCache.ContainsKey(filePath))
        {
            videoThumbnail = _videoThumbnailCache[filePath];
        }
        else
        {
            // Generate and cache thumbnail
            videoThumbnail = await VideoThumbnailHelper.GenerateVideoThumbnailAsync(
                filePath, width: 200, height: 150);
            if (videoThumbnail != null)
            {
                _videoThumbnailCache[filePath] = videoThumbnail;
            }
        }
    }
}
```

### Multi-Device Scenarios

```csharp
// Get all devices
var allDevices = offlineDataService.GetAllDevices();

// Find devices with video files
var devicesWithVideo = allDevices.Values
    .Where(d => d.SuspendMediaFiles.Any(f => f.IsVideoFile))
    .ToList();

// Find offline devices
var offlineDevices = allDevices.Values
    .Where(d => !d.IsConnected)
    .ToList();

// Update settings for a specific device
offlineDataService.UpdateDeviceSetting("DEV001A2B3C4D5E6F7", "brightness", 85);
```

### Event Handling

```csharp
// Subscribe to events
offlineDataService.DeviceDataChanged += (sender, e) =>
{
    Console.WriteLine($"Device {e.Device.DeviceSerial} changed: {e.ChangeType}");
};

offlineDataService.MediaFileChanged += (sender, e) =>
{
    Console.WriteLine($"Media file changed for {e.Device.DeviceSerial}: {e.ChangeType}");
};

offlineDataService.DatabaseSaved += (sender, e) =>
{
    Console.WriteLine($"Database saved with {e.DeviceCount} devices, {e.MediaFileCount} media files");
};
```

## Integration with DeviceSettings

The system is integrated with the existing DeviceSettings page:

### Media File Management
- **Add Media**: Automatically saves to device-specific directory and updates offline data
- **Remove Media**: Removes from both device and offline data, cleans up local files
- **Clear All**: Clears all media from device and offline data
- **Video Thumbnails**: Automatically generated and cached for video files

### Device Synchronization
- **Connection Events**: Automatically updates device status when devices connect/disconnect
- **Local Sync**: Syncs media files from local storage when devices come online
- **Settings Persistence**: Device settings are automatically saved and restored

## Configuration

### Global Settings
```csharp
public class GlobalOfflineSettings
{
    public bool AutoCleanupEnabled { get; set; } = true;
    public int MaxDeviceAgeHours { get; set; } = 720; // 30 days
    public string MediasDirectoryPath { get; set; } = "medias";
    public bool EnableBackup { get; set; } = true;
    public int MaxBackupFiles { get; set; } = 10;
}
```

### Device Settings
```csharp
public class DeviceOfflineSettings
{
    public int Brightness { get; set; } = 80;
    public int Rotation { get; set; } = 0;
    public int KeepAliveTimeout { get; set; } = 5;
    public bool DisplayInSleep { get; set; } = false;
    public bool SuspendModeEnabled { get; set; } = false;
    public bool AutoSyncEnabled { get; set; } = true;
}
```

## Database Schema

### JSON Structure
```json
{
  "version": "1.0.0",
  "created": "2024-12-10T14:30:22.123Z",
  "lastModified": "2024-12-10T15:45:33.456Z",
  "devices": {
    "DEV001A2B3C4D5E6F7": {
      "deviceSerial": "DEV001A2B3C4D5E6F7",
      "productName": "CM Display Controller HAF700V2",
      "isConnected": true,
      "suspendMediaFiles": [
        {
          "fileName": "suspend_0.mp4",
          "localPath": "medias/DEV001A2B3C4D5E6F7/suspend_0.mp4",
          "fileSize": 1048576,
          "md5Hash": "d41d8cd98f00b204e9800998ecf8427e",
          "mediaType": 0,
          "slotIndex": 0,
          "isActive": true
        }
      ],
      "deviceSettings": {
        "brightness": 80,
        "rotation": 0,
        "suspendModeEnabled": true
      }
    }
  },
  "globalSettings": {
    "autoCleanupEnabled": true,
    "maxDeviceAgeHours": 720,
    "enableBackup": true
  }
}
```

## Error Handling

The system includes comprehensive error handling:

- **File Operations**: Graceful handling of file I/O errors with logging
- **JSON Serialization**: Backup and recovery mechanisms for corrupted data
- **Device Communication**: Proper error reporting for device operation failures
- **Video Processing**: Fallback handling when video thumbnail generation fails

## Performance Considerations

- **Lazy Loading**: Media files are loaded on-demand
- **Caching**: Video thumbnails are cached to avoid regeneration
- **Async Operations**: All database operations are asynchronous
- **Background Processing**: Auto-save and cleanup run on background timers
- **Memory Management**: Proper disposal of resources and cache cleanup

## Testing

Example test scenarios are provided in `Examples/OfflineMediaDataServiceExample.cs`:

- Multi-device media management
- Device synchronization
- Export/import operations
- Cleanup operations
- Error handling scenarios

## Future Enhancements

- **Compression**: Optional compression for large media files
- **Cloud Sync**: Integration with cloud storage services
- **Media Optimization**: Automatic media optimization for device capabilities
- **Batch Operations**: Bulk operations for multiple devices
- **Advanced Search**: Query system for finding media files across devices

## Dependencies

- **.NET 8**: Target framework
- **System.Text.Json**: JSON serialization
- **FFMpegCore**: Video thumbnail generation
- **HidApi.Net**: HID device communication
- **WPF**: User interface framework

## License

This system is part of the CMDevicesManager application and follows the same licensing terms.