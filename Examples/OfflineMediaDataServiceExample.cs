using CMDevicesManager.Models;
using CMDevicesManager.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CMDevicesManager.Examples
{
    /// <summary>
    /// Example demonstrating the usage of OfflineMediaDataService for managing multi-device media files
    /// </summary>
    public class OfflineMediaDataServiceExample
    {
        private readonly OfflineMediaDataService _offlineDataService;

        public OfflineMediaDataServiceExample()
        {
            // Initialize the service - this would normally be done in App.xaml.cs
            _offlineDataService = new OfflineMediaDataService();
        }

        /// <summary>
        /// Example: Managing media files for multiple devices
        /// </summary>
        public async Task MultiDeviceMediaManagementExample()
        {
            Console.WriteLine("=== Multi-Device Media Management Example ===");

            // Simulate three different devices
            var device1Serial = "DEV001A2B3C4D5E6F7";
            var device2Serial = "DEV002B3C4D5E6F7A8";
            var device3Serial = "DEV003C4D5E6F7A8B9";

            // Create device data for each device
            var device1 = _offlineDataService.GetOrCreateDevice(device1Serial);
            device1.ProductName = "CM Display Controller HAF700V2";
            device1.ManufacturerName = "CM Devices";
            device1.IsConnected = true;

            var device2 = _offlineDataService.GetOrCreateDevice(device2Serial);
            device2.ProductName = "CM Display Controller HAF700V2";
            device2.ManufacturerName = "CM Devices";
            device2.IsConnected = false; // Offline device

            var device3 = _offlineDataService.GetOrCreateDevice(device3Serial);
            device3.ProductName = "CM Display Controller HAF700V2";
            device3.ManufacturerName = "CM Devices";
            device3.IsConnected = true;

            Console.WriteLine($"Created data for {_offlineDataService.GetAllDevices().Count} devices");

            // Add media files for each device
            await AddMediaFilesForDevice(device1Serial, "Device 1");
            await AddMediaFilesForDevice(device2Serial, "Device 2");
            await AddMediaFilesForDevice(device3Serial, "Device 3");

            // Display device information
            DisplayDeviceInformation();

            // Demonstrate device-specific media queries
            await DeviceSpecificMediaQueries(device1Serial);

            // Demonstrate global queries
            await GlobalMediaQueries();

            // Save the database
            await _offlineDataService.SaveDatabaseAsync();
            Console.WriteLine("Database saved successfully!");
        }

        /// <summary>
        /// Add sample media files for a device
        /// </summary>
        private async Task AddMediaFilesForDevice(string deviceSerial, string deviceName)
        {
            Console.WriteLine($"\n--- Adding media files for {deviceName} ({deviceSerial}) ---");

            // Simulate adding suspend media files to different slots
            for (int slot = 0; slot < 3; slot++)
            {
                var mediaType = slot % 2 == 0 ? "image.jpg" : "video.mp4";
                var fileName = $"suspend_{slot}.{(slot % 2 == 0 ? "jpg" : "mp4")}";
                var originalPath = $@"C:\temp\{deviceName}\{fileName}";
                
                // Create dummy file for demonstration
                CreateDummyMediaFile(originalPath, deviceSerial, slot);
                
                _offlineDataService.AddSuspendMediaFile(
                    deviceSerial, 
                    fileName, 
                    originalPath, 
                    slot, 
                    transferId: (byte)(slot + 1)
                );
                
                Console.WriteLine($"  Added {fileName} to slot {slot}");
            }

            // Add background media
            var backgroundPath = $@"C:\temp\{deviceName}\background.jpg";
            CreateDummyMediaFile(backgroundPath, deviceSerial, -1);
            _offlineDataService.SetBackgroundMediaFile(deviceSerial, "background.jpg", backgroundPath);
            Console.WriteLine($"  Added background media");

            // Update device settings
            var settings = new DeviceOfflineSettings
            {
                Brightness = 75,
                Rotation = 90,
                KeepAliveTimeout = 5,
                SuspendModeEnabled = true,
                AutoSyncEnabled = true
            };
            _offlineDataService.UpdateDeviceSettings(deviceSerial, settings);
            Console.WriteLine($"  Updated device settings");
        }

        /// <summary>
        /// Create a dummy media file for demonstration
        /// </summary>
        private void CreateDummyMediaFile(string filePath, string deviceSerial, int slot)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                
                // Create a dummy file with some content
                var content = $"Dummy media file for device {deviceSerial}, slot {slot}, created at {DateTime.Now}";
                File.WriteAllText(filePath, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create dummy file {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Display information about all devices
        /// </summary>
        private void DisplayDeviceInformation()
        {
            Console.WriteLine("\n=== Device Information ===");
            
            var allDevices = _offlineDataService.GetAllDevices();
            foreach (var kvp in allDevices)
            {
                var device = kvp.Value;
                Console.WriteLine($"\nDevice: {device.ProductName} ({device.DeviceSerial})");
                Console.WriteLine($"  Connected: {device.IsConnected}");
                Console.WriteLine($"  Last Seen: {device.LastSeen:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Suspend Media Files: {device.ActiveSuspendMediaCount}");
                Console.WriteLine($"  Background Media: {(device.BackgroundMediaFile != null ? "Yes" : "No")}");
                Console.WriteLine($"  Settings: {device.Settings}");
                
                if (device.SuspendMediaFiles.Any())
                {
                    Console.WriteLine("  Media Files:");
                    foreach (var mediaFile in device.SuspendMediaFiles)
                    {
                        Console.WriteLine($"    Slot {mediaFile.SlotIndex}: {mediaFile.FileName} ({mediaFile.FileSize} bytes)");
                    }
                }
            }
        }

        /// <summary>
        /// Demonstrate device-specific media queries
        /// </summary>
        private async Task DeviceSpecificMediaQueries(string deviceSerial)
        {
            Console.WriteLine($"\n=== Device-Specific Queries for {deviceSerial} ===");

            // Get all suspend media files for the device
            var suspendFiles = _offlineDataService.GetSuspendMediaFiles(deviceSerial);
            Console.WriteLine($"Total suspend media files: {suspendFiles.Count}");

            // Get specific media file by slot
            var slot1Media = _offlineDataService.GetSuspendMediaFile(deviceSerial, 1);
            if (slot1Media != null)
            {
                Console.WriteLine($"Slot 1 media: {slot1Media.FileName} ({slot1Media.FileSize} bytes)");
                Console.WriteLine($"  Is Video: {slot1Media.IsVideoFile}");
                Console.WriteLine($"  Added: {slot1Media.AddedDateTime:yyyy-MM-dd HH:mm:ss}");
            }

            // Get device settings
            var settings = _offlineDataService.GetDeviceSettings(deviceSerial);
            Console.WriteLine($"Device settings: Brightness={settings.Brightness}%, Rotation={settings.Rotation}°");

            // Update a specific setting
            _offlineDataService.UpdateDeviceSetting(deviceSerial, "brightness", 85);
            Console.WriteLine("Updated brightness to 85%");
        }

        /// <summary>
        /// Demonstrate global queries across all devices
        /// </summary>
        private async Task GlobalMediaQueries()
        {
            Console.WriteLine("\n=== Global Queries ===");

            // Get database statistics
            var stats = _offlineDataService.GetStatistics();
            Console.WriteLine($"Database Statistics:");
            Console.WriteLine($"  Total Devices: {stats.TotalDevices}");
            Console.WriteLine($"  Connected Devices: {stats.ConnectedDevices}");
            Console.WriteLine($"  Total Media Files: {stats.TotalMediaFiles}");
            Console.WriteLine($"  Database Size: {stats.DatabaseSizeFormatted}");
            Console.WriteLine($"  Last Modified: {stats.LastModified:yyyy-MM-dd HH:mm:ss}");

            // Find all devices with video files
            var allDevices = _offlineDataService.GetAllDevices();
            var devicesWithVideo = allDevices.Values
                .Where(d => d.SuspendMediaFiles.Any(f => f.IsVideoFile))
                .ToList();
            
            Console.WriteLine($"\nDevices with video files: {devicesWithVideo.Count}");
            foreach (var device in devicesWithVideo)
            {
                var videoFiles = device.SuspendMediaFiles.Where(f => f.IsVideoFile).ToList();
                Console.WriteLine($"  {device.DeviceSerial}: {videoFiles.Count} video files");
            }

            // Find offline devices (not connected)
            var offlineDevices = allDevices.Values.Where(d => !d.IsConnected).ToList();
            Console.WriteLine($"\nOffline devices: {offlineDevices.Count}");
            foreach (var device in offlineDevices)
            {
                Console.WriteLine($"  {device.DeviceSerial} - Last seen: {device.LastSeen:yyyy-MM-dd HH:mm:ss}");
            }
        }

        /// <summary>
        /// Example: Device synchronization scenario
        /// </summary>
        public async Task DeviceSynchronizationExample()
        {
            Console.WriteLine("\n=== Device Synchronization Example ===");

            var deviceSerial = "DEV001A2B3C4D5E6F7";
            
            // Simulate device coming online
            Console.WriteLine("Device coming online...");
            _offlineDataService.SetDeviceConnectionStatus(deviceSerial, true);
            
            // Sync media files from local storage
            Console.WriteLine("Syncing media files from local storage...");
            _offlineDataService.SyncMediaFilesFromLocal(deviceSerial);
            
            // Get the device data to see what was synced
            var device = _offlineDataService.GetDevice(deviceSerial);
            if (device != null)
            {
                Console.WriteLine($"Device {deviceSerial} now has {device.ActiveSuspendMediaCount} media files");
            }
        }

        /// <summary>
        /// Example: Export and Import operations
        /// </summary>
        public async Task ExportImportExample()
        {
            Console.WriteLine("\n=== Export/Import Example ===");

            var exportPath = Path.Combine(Path.GetTempPath(), "device_media_backup.json");
            
            // Export database
            Console.WriteLine($"Exporting database to: {exportPath}");
            await _offlineDataService.ExportDatabaseAsync(exportPath);
            
            if (File.Exists(exportPath))
            {
                var fileInfo = new FileInfo(exportPath);
                Console.WriteLine($"Export completed successfully! File size: {fileInfo.Length} bytes");
                
                // Import it back (demonstrating merge functionality)
                Console.WriteLine("Importing database back (merge mode)...");
                await _offlineDataService.ImportDatabaseAsync(exportPath, mergeWithExisting: true);
                Console.WriteLine("Import completed successfully!");
                
                // Clean up
                File.Delete(exportPath);
            }
        }

        /// <summary>
        /// Example: Cleanup operations
        /// </summary>
        public void CleanupExample()
        {
            Console.WriteLine("\n=== Cleanup Example ===");

            // Remove a specific device
            var deviceToRemove = "DEV002B3C4D5E6F7A8";
            Console.WriteLine($"Removing device: {deviceToRemove}");
            var removed = _offlineDataService.RemoveDevice(deviceToRemove);
            Console.WriteLine($"Device removed: {removed}");

            // Clean up stale devices (this is normally done automatically)
            Console.WriteLine("Cleaning up stale devices...");
            _offlineDataService.CleanupStaleDevices();
            
            var remainingDevices = _offlineDataService.GetAllDevices().Count;
            Console.WriteLine($"Remaining devices: {remainingDevices}");
        }

        /// <summary>
        /// Run all examples
        /// </summary>
        public async Task RunAllExamples()
        {
            try
            {
                await MultiDeviceMediaManagementExample();
                await DeviceSynchronizationExample();
                await ExportImportExample();
                CleanupExample();
                
                Console.WriteLine("\n=== All Examples Completed Successfully! ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running examples: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                // Clean up
                _offlineDataService?.Dispose();
            }
        }
    }

    /// <summary>
    /// Example program entry point
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("Starting Offline Media Data Service Examples...");
            
            var example = new OfflineMediaDataServiceExample();
            await example.RunAllExamples();
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}