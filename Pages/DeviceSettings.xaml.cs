using CMDevicesManager.Helper;
using CMDevicesManager.Models;
using CMDevicesManager.Services;
using HID.DisplayController;
using HidApi;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for DeviceSettings.xaml
    /// </summary>
    public partial class DeviceSettings : Page
    {
        private DeviceInfo? _deviceInfo;
        private HidDeviceService? _hidDeviceService;
        private bool _isHidServiceInitialized = false;

        private bool _isLoading = false;

        // Notification system
        private DispatcherTimer? _notificationTimer;
        private TaskCompletionSource<bool>? _confirmationResult;
        
        // Suspend media tracking
        private List<string> _activeSuspendMediaFiles = new();
        
        // Video thumbnail cache to avoid regenerating thumbnails
        private Dictionary<string, BitmapImage> _videoThumbnailCache = new();

        private PlaybackMode _currentPlayMode = PlaybackMode.RealtimeConfig;

        #region Properties

        public DeviceInfo? DeviceInfo
        {
            get => _deviceInfo;
            set
            {
                if (_deviceInfo != value)
                {
                    _deviceInfo = value;
                    // Initialize HID service when device info is set
                    _ = InitializeHidDeviceServiceAsync();
                    // Load playback mode from offline data service
                    LoadPlaybackModeFromService();
                }
            }
        }

        /// <summary>
        /// Gets whether the HID device service is initialized and ready
        /// </summary>
        public bool IsHidServiceReady => _isHidServiceInitialized && _hidDeviceService?.IsInitialized == true;

        /// <summary>
        /// Gets the current HID device service instance
        /// </summary>
        public HidDeviceService? HidDeviceService => _hidDeviceService;

        #endregion

        public DeviceSettings()
        {
            InitializeComponent();
            DisableAllControls();
        }

        public DeviceSettings(DeviceInfo deviceInfo) : this()
        {
            DeviceInfo = deviceInfo;
            InitializeDevice();
        }

        #region HID Device Service Management

        /// <summary>
        /// Initialize the HID device service and set up device filtering
        /// </summary>
        private async Task InitializeHidDeviceServiceAsync()
        {
            if (_deviceInfo == null || string.IsNullOrEmpty(_deviceInfo.Path))
            {
                Logger.Warn("Cannot initialize HID service: DeviceInfo or device path is null");
                return;
            }

            try
            {
                Logger.Info($"Initializing HID Device Service for device: {_deviceInfo.ProductString} (Path: {_deviceInfo.Path})");

                // Get the service from ServiceLocator
                _hidDeviceService = ServiceLocator.HidDeviceService;

                if (!_hidDeviceService.IsInitialized)
                {
                    Logger.Warn("HID Device Service is not initialized. Make sure it's initialized in App.xaml.cs");
                    return;
                }

                // Set device path filter to only operate on this specific device
                _hidDeviceService.SetDevicePathFilter(_deviceInfo.Path, enableFilter: true);

                // Only subscribe to DeviceError events
                _hidDeviceService.DeviceError += OnHidDeviceError;

                _isHidServiceInitialized = true;

                // Verify the device is available
                var targetDevices = _hidDeviceService.GetOperationTargetDevices();
                if (targetDevices.Any())
                {
                    var targetDevice = targetDevices.First();
                    Logger.Info($"Target device found: {targetDevice.ProductString} (Serial: {targetDevice.SerialNumber})");
                }
                else
                {
                    Logger.Warn($"Target device not found in connected devices. Device may be disconnected.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize HID Device Service: {ex.Message}", ex);
                _isHidServiceInitialized = false;
            }
        }

        #endregion

        #region HID Service Event Handlers

        private void OnHidDeviceError(object? sender, DeviceErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.Device.Path == _deviceInfo?.Path)
                {
                    Logger.Error($"Device error for {e.Device.ProductString}: {e.Exception.Message}", e.Exception);
                    // Handle device error (could update UI status, retry operations, etc.)
                    // No MessageBox - just log the error
                }
            });
        }

        #endregion

        private void DisableAllControls()
        {
            RebootButton.IsEnabled = false;
            FactoryResetButton.IsEnabled = false;
            UpdateFirmwareButton.IsEnabled = false;
            RefreshStatusButton.IsEnabled = false;
            
            // Try to disable display control toggles if they exist
            try
            {
                if (FindName("SleepModeToggle") is ToggleButton sleepToggle)
                    sleepToggle.IsEnabled = false;
                if (FindName("SuspendModeToggle") is ToggleButton suspendToggle)
                    suspendToggle.IsEnabled = false;
                    
                // Make sure suspend mode content is hidden initially
                //if (FindName("SuspendModeContentArea") is FrameworkElement contentArea)
                //    contentArea.Visibility = Visibility.Collapsed;
            }
            catch { /* Toggles may not be available yet */ }
        }

        private void EnableAllControls()
        {
            RebootButton.IsEnabled = true;
            FactoryResetButton.IsEnabled = true;
            UpdateFirmwareButton.IsEnabled = true;
            RefreshStatusButton.IsEnabled = true;
            
            // Try to enable display control toggles if they exist
            try
            {
                if (FindName("SleepModeToggle") is ToggleButton sleepToggle)
                    sleepToggle.IsEnabled = true;
                if (FindName("SuspendModeToggle") is ToggleButton suspendToggle)
                    suspendToggle.IsEnabled = true;
            }
            catch { /* Toggles may not be available yet */ }
        }

        private async void InitializeDevice()
        {
            if (_deviceInfo == null) return;

            try
            {
                SetLoadingState(true, "Connecting to device...");

                // Update device info display
                UpdateDeviceInfoDisplay();

                // Wait for HID service initialization
                if (IsHidServiceReady)
                {
                    // Load device firmware info and capabilities
                    await LoadDeviceInformation();

                    // Load current device status
                    await RefreshDeviceStatus();

                    EnableAllControls();
                    SetConnectionStatus(true);
                }
                else
                {
                    ShowNotification("HID Device Service not ready", true);
                    SetConnectionStatus(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize device: {ex.Message}");
                ShowNotification($"Failed to connect to device: {ex.Message}", true);
                SetConnectionStatus(false);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void UpdateDeviceInfoDisplay()
        {
            if (_deviceInfo == null) return;

            DeviceNameText.Text = _deviceInfo.ProductString ?? "Unknown Device";
            DevicePathText.Text = _deviceInfo.Path ?? "Unknown Path";
            SerialNumberText.Text = _deviceInfo.SerialNumber ?? "N/A";
            ManufacturerText.Text = _deviceInfo.ManufacturerString ?? "N/A";
            ProductNameText.Text = _deviceInfo.ProductString ?? "N/A";
        }


        /// <summary>
        /// Method that loads the playback mode when device info is set
        /// This should be called in the DeviceInfo property setter
        /// </summary>
        private void LoadPlaybackModeFromService()
        {
            if (_deviceInfo?.SerialNumber == null || !ServiceLocator.IsOfflineMediaServiceInitialized)
                return;

            try
            {
                var offlineDataService = ServiceLocator.OfflineMediaDataService;
                var savedPlaybackMode = offlineDataService.GetDevicePlaybackMode(_deviceInfo.SerialNumber);

                _currentPlayMode = savedPlaybackMode;

                bool isRealtime = _currentPlayMode == PlaybackMode.RealtimeConfig;

                // binding the SuspendModeToggle state from current mode
                SuspendModeToggle.IsChecked = !isRealtime;

                Console.WriteLine($"Loaded playback mode for device {_deviceInfo.SerialNumber}: {savedPlaybackMode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load playback mode for device {_deviceInfo?.SerialNumber}: {ex.Message}");
                // Use default mode on error
                _currentPlayMode = PlaybackMode.RealtimeConfig;
            }
        }

        private async Task LoadDeviceInformation()
        {
            if (!IsHidServiceReady || _hidDeviceService == null) return;

            try
            {
                SetLoadingState(true, "Loading device information...");

                // Get firmware and hardware versions using HID service
                var firmwareInfoResults = await _hidDeviceService.GetDeviceFirmwareInfoAsync();
                if (firmwareInfoResults.Any())
                {
                    var result = firmwareInfoResults.First();
                    if (result.Value != null)
                    {
                        var deviceFWInfo = result.Value;
                        HardwareVersionText.Text = deviceFWInfo.HardwareVersion.ToString();
                        FirmwareVersionText.Text = deviceFWInfo.FirmwareVersion.ToString();
                    }
                }

                // Get device capabilities using HID service
                var capabilitiesResults = await _hidDeviceService.GetDeviceCapabilitiesAsync();
                if (capabilitiesResults.Any())
                {
                    var result = capabilitiesResults.First();
                    if (result.Value != null)
                    {
                        UpdateCapabilitiesDisplay(result.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load device information: {ex.Message}");
                ShowNotification($"Failed to load device information: {ex.Message}", true);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void UpdateCapabilitiesDisplay(DisplayCtrlCapabilities capabilities)
        {
            // Device Capabilities section has been hidden in the UI
            // This method is kept for compatibility but the UI elements are commented out
            Debug.WriteLine("Device capabilities loaded but UI elements are hidden");
        }

        private async Task RefreshDeviceStatus()
        {
            if (!IsHidServiceReady || _hidDeviceService == null) return;

            try
            {
                SetLoadingState(true, "Refreshing device status...");

                var statusResults = await _hidDeviceService.GetDeviceStatusAsync();
                if (statusResults.Any())
                {
                    var result = statusResults.First();
                    if (result.Value.HasValue)
                    {
                        var status = result.Value.Value;
                        
                        BrightnessText.Text = $"{status.Brightness}%";
                        RotationText.Text = $"{status.Degree}°";
                        OsdStateText.Text = status.IsOsdActive ? "Active" : "Inactive";
                        KeepAliveText.Text = $"{status.KeepAliveTimeout}s";
                        DisplaySleepText.Text = status.IsDisplayInSleep ? "Enabled" : "Disabled";
                        
                        // Update toggle switches state based on device status if they exist
                        try
                        {
                            if (FindName("SleepModeToggle") is ToggleButton sleepToggle)
                                sleepToggle.IsChecked = status.IsDisplayInSleep;
                            
                            // Update suspend mode display
                            UpdateSuspendModeDisplay(status);
                        }
                        catch { /* Toggle may not be available yet */ }
                    }
                    else
                    {
                        // Set all status to N/A if failed to get status
                        SetStatusToNA();
                    }
                }
                else
                {
                    SetStatusToNA();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to refresh device status: {ex.Message}");
                ShowNotification($"Failed to refresh device status: {ex.Message}", true);
                SetStatusToNA();
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void SetStatusToNA()
        {
            BrightnessText.Text = "N/A";
            RotationText.Text = "N/A";
            OsdStateText.Text = "N/A";
            KeepAliveText.Text = "N/A";
            DisplaySleepText.Text = "N/A";
            
            // Reset toggle switches if they exist
            try
            {
                if (FindName("SleepModeToggle") is ToggleButton sleepToggle)
                    sleepToggle.IsChecked = false;
                if (FindName("SuspendModeToggle") is ToggleButton suspendToggle)
                    suspendToggle.IsChecked = false;
                
                // Update suspend mode display for reset state
                UpdateSuspendModeDisplay(null);
            }
            catch { /* Toggles may not be available yet */ }
        }

        private void SetLoadingState(bool isLoading, string message = "Loading...")
        {
            _isLoading = isLoading;
            LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            LoadingText.Text = message;
            
            if (isLoading)
            {
                DisableAllControls();
            }
            else if (IsHidServiceReady)
            {
                EnableAllControls();
            }
        }

        private void SetConnectionStatus(bool isConnected)
        {
            ConnectionStatusBorder.Background = new SolidColorBrush(
                isConnected ? Colors.Green : Colors.Red);
        }

        // Suspend Mode Display Management
        private void UpdateSuspendModeDisplay(DeviceStatus? status)
        {
            try
            {
                var suspendToggle = FindName("SuspendModeToggle") as ToggleButton;
                bool suspendModeActive = suspendToggle?.IsChecked == true;
                
                // Update suspend mode content area visibility
                var contentArea = FindName("SuspendModeContentArea") as FrameworkElement;
                if (contentArea != null)
                {
                    contentArea.Visibility = suspendModeActive ? Visibility.Visible : Visibility.Collapsed;
                }
                
                if (status.HasValue && suspendModeActive)
                {
                    // When suspend mode is active, load the media slots with actual device data
                    LoadSuspendMediaSlots(status.Value);
                    Logger.Info($"Suspend mode display updated with device status. Active media count: {status.Value.ActiveSuspendMediaCount}");
                }
                else if (!suspendModeActive)
                {
                    // When suspend mode is off, hide all suspend content
                    HideSuspendModeContent();
                    Logger.Info("Suspend mode content hidden");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update suspend mode display: {ex.Message}");
                Logger.Error($"Failed to update suspend mode display: {ex.Message}", ex);
                HideSuspendModeContent(); // Fallback to hiding content
            }
        }

        // Hide all suspend mode content when suspend mode is off
        private void HideSuspendModeContent()
        {
            try
            {
                var contentArea = FindName("SuspendModeContentArea") as FrameworkElement;
                if (contentArea != null) 
                    contentArea.Visibility = Visibility.Collapsed;
                
                // Release image sources before clearing all media slots
                for (int i = 0; i < 5; i++)
                {
                    var slotNumber = i + 1;
                    var thumbnail = FindName($"MediaThumbnail{slotNumber}") as Image;
                    if (thumbnail?.Source is BitmapImage bitmapToRelease)
                    {
                        try
                        {
                            thumbnail.Source = null;
                            bitmapToRelease.StreamSource?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to release image source for slot {slotNumber} during hide: {ex.Message}");
                        }
                    }
                }
                    
                // Clear all media slots to show add state
                for (int i = 0; i < 5; i++)
                {
                    UpdateMediaSlotUI(i, null, false);
                }
                
                Logger.Info("Suspend mode content hidden and media slots reset with image sources released");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to hide suspend mode content: {ex.Message}");
                Logger.Error($"Failed to hide suspend mode content: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Load and display suspend media slots based on device status
        /// </summary>
        private void LoadSuspendMediaSlots(DeviceStatus status)
        {
            try
            {
                // Ensure content area is visible
                var contentArea = FindName("SuspendModeContentArea") as FrameworkElement;
                if (contentArea != null) contentArea.Visibility = Visibility.Visible;
                
                // Use actual device status to determine which slots have media
                var suspendMediaActive = status.SuspendMediaActive ?? new int[0];
                var maxSlots = Math.Min(5, status.MaxSuspendMediaCount);
                
                Logger.Info($"Loading suspend media slots. Max slots: {maxSlots}, Active media: [{string.Join(", ", suspendMediaActive)}]");
                
                // Update each media slot based on device status
                for (int i = 0; i < 5; i++)
                {
                    bool hasMedia = i < suspendMediaActive.Length && suspendMediaActive[i] > 0;
                    
                    if (hasMedia)
                    {
                        // Try to find corresponding local media file first
                        var localMediaPath = TryFindLocalMediaFile(i);
                        if (!string.IsNullOrEmpty(localMediaPath))
                        {
                            // Show local media with actual content
                            UpdateMediaSlotUI(i, localMediaPath, true);
                            Logger.Info($"Slot {i + 1}: Local media file found - {Path.GetFileName(localMediaPath)}");
                        }
                        else
                        {
                            // Show device media with placeholder name
                            string mediaName = $"suspend_media_{i + 1}";
                            UpdateMediaSlotUI(i, mediaName, true);
                            Logger.Info($"Slot {i + 1}: Device media found (no local file)");
                        }
                    }
                    else
                    {
                        // Show empty slot with add button
                        UpdateMediaSlotUI(i, null, false);
                        Logger.Info($"Slot {i + 1}: Empty slot");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load suspend media slots: {ex.Message}");
                Logger.Error($"Failed to load suspend media slots: {ex.Message}", ex);
                // Clear all slots on error
                for (int i = 0; i < 5; i++)
                {
                    UpdateMediaSlotUI(i, null, false);
                }
            }
        }

        /// <summary>
        /// Try to find local media file for a given slot index
        /// </summary>
        /// <param name="slotIndex">Zero-based slot index</param>
        /// <returns>Local file path if found, null otherwise</returns>
        private string? TryFindLocalMediaFile(int slotIndex)
        {
            try
            {
                // First check the offline media data service for this device
                if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                {
                    var offlineDataService = ServiceLocator.OfflineMediaDataService;
                    var mediaFile = offlineDataService.GetSuspendMediaFile(_deviceInfo.SerialNumber, slotIndex);
                    
                    if (mediaFile != null && File.Exists(mediaFile.LocalPath))
                    {
                        Logger.Info($"Found media file in offline data for slot {slotIndex + 1}: {Path.GetFileName(mediaFile.LocalPath)}");
                        return mediaFile.LocalPath;
                    }
                }

                // Fallback to searching the medias directory
                var mediasDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias");
                if (!Directory.Exists(mediasDir))
                    return null;

                // Look for files matching the pattern suspend_{slotIndex}.{extension}
                var searchPattern = $"suspend_{slotIndex}.*";
                var matchingFiles = Directory.GetFiles(mediasDir, searchPattern);
                
                if (matchingFiles.Length > 0)
                {
                    // Return the first matching file
                    var filePath = matchingFiles[0];
                    if (File.Exists(filePath))
                    {
                        Logger.Info($"Found local media file for slot {slotIndex + 1}: {Path.GetFileName(filePath)}");
                        return filePath;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error searching for local media file for slot {slotIndex + 1}: {ex.Message}");
            }
            
            return null;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clean up timers
                _notificationTimer?.Stop();
                _notificationTimer = null;
                
                // Release all image sources to prevent memory leaks
                for (int i = 1; i <= 5; i++)
                {
                    var thumbnail = FindName($"MediaThumbnail{i}") as Image;
                    if (thumbnail?.Source is BitmapImage bitmapToRelease)
                    {
                        try
                        {
                            thumbnail.Source = null;
                            bitmapToRelease.StreamSource?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to release image source for thumbnail {i}: {ex.Message}");
                        }
                    }
                }
                
                // Clear and dispose video thumbnail cache
                if (_videoThumbnailCache?.Count > 0)
                {
                    var thumbnailsToDispose = new List<BitmapImage>(_videoThumbnailCache.Values);
                    _videoThumbnailCache.Clear();
                    
                    foreach (var cachedThumbnail in thumbnailsToDispose)
                    {
                        try
                        {
                            cachedThumbnail?.StreamSource?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to dispose cached thumbnail during cleanup: {ex.Message}");
                        }
                    }
                    Logger.Info("Disposed all cached video thumbnails during page cleanup");
                }
                
                // Unsubscribe from HID service events
                if (_hidDeviceService != null)
                {
                    _hidDeviceService.DeviceError -= OnHidDeviceError;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during page cleanup: {ex.Message}");
            }
        }

        // Notification System Methods
        private void ShowNotification(string message, bool isError = false, int durationMs = 5000)
        {
            Dispatcher.Invoke(() =>
            {
                var notificationMessage = FindName("NotificationMessage") as TextBlock;
                var notificationIcon = FindName("NotificationIcon") as TextBlock;
                var notificationPanel = FindName("NotificationPanel") as FrameworkElement;
                
                if (notificationMessage != null)
                    notificationMessage.Text = message;
                
                if (notificationIcon != null)
                {
                    if (isError)
                    {
                        notificationIcon.Text = "\uE783"; // Error icon
                        notificationIcon.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                    }
                    else
                    {
                        notificationIcon.Text = "\uE7BA"; // Success icon
                        notificationIcon.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    }
                }
                
                if (notificationPanel != null)
                    notificationPanel.Visibility = Visibility.Visible;
                
                // Auto-hide after specified duration
                _notificationTimer?.Stop();
                _notificationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(durationMs)
                };
                _notificationTimer.Tick += (s, e) =>
                {
                    HideNotification();
                    _notificationTimer.Stop();
                };
                _notificationTimer.Start();
            });
        }

        private void HideNotification()
        {
            var notificationPanel = FindName("NotificationPanel") as FrameworkElement;
            if (notificationPanel != null)
                notificationPanel.Visibility = Visibility.Collapsed;
            _notificationTimer?.Stop();
        }

        // Event handlers for notification and confirmation dialogs
        private void NotificationCloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideNotification();
        }

        private void ConfirmationCancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for confirmation dialog - preserved for future use
        }

        private void ConfirmationConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for confirmation dialog - preserved for future use
        }

        private async void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDeviceStatus();
        }

        private async void RebootButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;

            // No confirmation dialog needed - tooltip provides the info
            try
            {
                SetLoadingState(true, "Rebooting device...");

                var results = await _hidDeviceService.RebootDevicesAsync();
                var successCount = results.Values.Count(r => r);
                
                if (successCount > 0)
                {
                    ShowNotification("Reboot command sent successfully. The device will restart shortly.");
                    
                    // Wait for device to reboot and try to reconnect
                    await Task.Delay(3000);
                    
                    // Try to refresh status after reboot
                    try
                    {
                        await RefreshDeviceStatus();
                    }
                    catch
                    {
                        // Device might still be rebooting
                        Debug.WriteLine("Device still rebooting...");
                    }
                }
                else
                {
                    ShowNotification("Failed to send reboot command.", true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to reboot device: {ex.Message}");
                ShowNotification($"Failed to reboot device: {ex.Message}", true);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async void FactoryResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;

            // No confirmation dialog needed - tooltip provides the warning
            try
            {
                SetLoadingState(true, "Performing factory reset...");

                var results = await _hidDeviceService.FactoryResetDevicesAsync();
                var successCount = results.Values.Count(r => r);
                
                if (successCount > 0)
                {
                    ShowNotification("Factory reset command sent successfully. The device will reset to defaults and restart.");
                    
                    // Wait for device to reset and try to reconnect
                    await Task.Delay(5000);
                    
                    // Try to refresh status after reset
                    try
                    {
                        await RefreshDeviceStatus();
                    }
                    catch
                    {
                        // Device might still be resetting
                        Debug.WriteLine("Device still resetting...");
                    }
                }
                else
                {
                    ShowNotification("Failed to send factory reset command.", true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to perform factory reset: {ex.Message}");
                ShowNotification($"Failed to perform factory reset: {ex.Message}", true);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async void UpdateFirmwareButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;

            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Firmware File",
                Filter = "Firmware Files (*.bin;*.hex;*.fw)|*.bin;*.hex;*.fw|All Files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (openFileDialog.ShowDialog() != true) return;

            var filePath = openFileDialog.FileName;
            var fileInfo = new FileInfo(filePath);

            // No confirmation dialog needed - tooltip provides the warning
            try
            {
                SetLoadingState(true, "Updating firmware... DO NOT DISCONNECT!");

                var results = await _hidDeviceService.UpdateFirmwareAsync(filePath);
                var successCount = results.Values.Count(r => r);
                
                if (successCount > 0)
                {
                    ShowNotification(
                        "Firmware update completed successfully!\n\n" +
                        "The device may restart automatically. Please wait for the process to complete.");
                    
                    // Wait for device to restart and try to reconnect
                    await Task.Delay(10000);
                    
                    // Reload device information after firmware update
                    await LoadDeviceInformation();
                    await RefreshDeviceStatus();
                }
                else
                {
                    ShowNotification("Firmware update failed. Please check the firmware file and try again.", true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update firmware: {ex.Message}");
                ShowNotification($"Failed to update firmware: {ex.Message}", true);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        // Display Control Settings Event Handlers
        private async void SleepModeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;
            
            var toggle = sender as ToggleButton;
            if (toggle == null) return;
            
            bool isEnabled = toggle.IsChecked == true;
            
            // No confirmation dialog needed - tooltip provides the info
            try
            {
                SetLoadingState(true, isEnabled ? "Enabling sleep mode..." : "Disabling sleep mode...");

                var results = await _hidDeviceService.SetDisplayInSleepAsync(isEnabled);
                var successCount = results.Values.Count(r => r);
                
                if (successCount > 0)
                {
                    ShowNotification(isEnabled ? "Sleep mode enabled successfully." : "Sleep mode disabled successfully.");
                    // Refresh status to update display
                    await RefreshDeviceStatus();
                }
                else
                {
                    ShowNotification($"Failed to {(isEnabled ? "enable" : "disable")} sleep mode. Please try again.", true);
                    // Revert toggle state on failure
                    toggle.IsChecked = !isEnabled;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to {(isEnabled ? "enable" : "disable")} sleep mode: {ex.Message}");
                ShowNotification($"Failed to {(isEnabled ? "enable" : "disable")} sleep mode: {ex.Message}", true);
                // Revert toggle state on exception
                toggle.IsChecked = !isEnabled;
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async void SuspendModeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;
            
            var toggle = sender as ToggleButton;
            if (toggle == null) return;
            
            bool isActivated = toggle.IsChecked == true;
            
            // No confirmation dialog needed - tooltip provides the info
            try
            {
                SetLoadingState(true, isActivated ? "Activating suspend mode..." : "Activating RealTime mode...");

                var results = await _hidDeviceService.SetRealTimeDisplayAsync(!isActivated);
                var successCount = results.Values.Count(r => r);
                
                if (successCount > 0)
                {
                    ShowNotification(isActivated ? "Suspend mode activated successfully." : "RealTime mode activated successfully.");

                    _currentPlayMode = isActivated ? PlaybackMode.OfflineVideo : PlaybackMode.RealtimeConfig;
                    SavePlaybackModeToService(); // Save to offline data service
                    // Update suspend mode display based on new state
                    if (isActivated)
                    {
                        // When activating suspend mode, refresh the display
                        await RefreshSuspendMediaDisplay();
                    }
                    else
                    {
                        // When clearing suspend mode, hide all suspend content
                        HideSuspendModeContent();
                        
                        // Refresh status to update display
                        await RefreshDeviceStatus();
                    }
                }
                else
                {
                    ShowNotification($"Failed to {(isActivated ? "activate" : "clear")} suspend mode. Please try again.", true);
                    // Revert toggle state on failure
                    toggle.IsChecked = !isActivated;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to {(isActivated ? "activate" : "clear")} suspend mode: {ex.Message}");
                ShowNotification($"Failed to {(isActivated ? "activate" : "clear")} suspend mode: {ex.Message}", true);
                // Revert toggle state on exception
                toggle.IsChecked = !isActivated;
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        // Method to refresh suspend media display without full device status update
        private async Task RefreshSuspendMediaDisplay()
        {
            try
            {
                var suspendToggle = FindName("SuspendModeToggle") as ToggleButton;
                bool suspendModeActive = suspendToggle?.IsChecked == true;
                
                // Update content area visibility
                var contentArea = FindName("SuspendModeContentArea") as FrameworkElement;
                if (contentArea != null)
                {
                    contentArea.Visibility = suspendModeActive ? Visibility.Visible : Visibility.Collapsed;
                }
                
                if (suspendModeActive)
                {
                    // Get actual suspend media status from device
                    var suspendMediaStatus = await GetSuspendMediaStatusFromDevice();
                    if (suspendMediaStatus.MaxSuspendMediaCount > 0)
                    {
                        LoadSuspendMediaSlots(suspendMediaStatus);
                        Logger.Info($"Refreshed suspend media display. Found {suspendMediaStatus.ActiveSuspendMediaCount} active media files");
                    }
                    else
                    {
                        // No valid status, show empty slots
                        for (int i = 0; i < 5; i++)
                        {
                            UpdateMediaSlotUI(i, null, false);
                        }
                        Logger.Warn("No valid suspend media status received from device");
                    }
                }
                else
                {
                    HideSuspendModeContent();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to refresh suspend media display: {ex.Message}");
                Logger.Error($"Failed to refresh suspend media display: {ex.Message}", ex);
                HideSuspendModeContent();
            }
        }

        /// <summary>
        /// Get suspend media status from device
        /// </summary>
        private async Task<DeviceStatus> GetSuspendMediaStatusFromDevice()
        {
            try
            {
                if (IsHidServiceReady && _hidDeviceService != null)
                {
                    var statusResults = await _hidDeviceService.GetDeviceStatusAsync();
                    
                    if (statusResults.Any())
                    {
                        var result = statusResults.First();
                        if (result.Value.HasValue)
                        {
                            return result.Value.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get suspend media status from device: {ex.Message}");
            }
            
            return new DeviceStatus(); // Return empty status as fallback
        }

        #region Suspend Media Management Event Handlers

        /// <summary>
        /// Handle media slot clicks for adding or viewing media
        /// </summary>
        private async void MediaSlot_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;

            var border = sender as Border;
            if (border?.Tag is string tagStr && int.TryParse(tagStr, out int slotIndex))
            {
                // Check if this slot already has media
                var thumbnailImage = FindName($"MediaThumbnail{slotIndex + 1}") as Image;
                var placeholder = FindName($"AddMediaPlaceholder{slotIndex + 1}") as StackPanel;
                
                // Check if slot has media by looking at either thumbnail visibility or placeholder content
                bool hasMedia = thumbnailImage?.Visibility == Visibility.Visible || 
                               (placeholder != null && placeholder.Children.OfType<TextBlock>()
                                   .FirstOrDefault()?.Text != "\uE710"); // Not the add icon
                 
                if (hasMedia)
                {
                    // Double-click or specific click handling for media preview
                    if (e.ClickCount == 2)
                    {
                        await OpenMediaPreview(slotIndex);
                    }
                    else
                    {
                        // Single click - show media info
                        ShowMediaInfo(slotIndex);
                    }
                }
                else
                {
                    // Slot is empty - add new media
                    await AddMediaToSlot(slotIndex);
                }
            }
        }

        /// <summary>
        /// Open media preview for a slot (for future enhancement)
        /// </summary>
        private async Task OpenMediaPreview(int slotIndex)
        {
            try
            {
                // Try to find local media file
                var localMediaPath = TryFindLocalMediaFile(slotIndex);
                if (!string.IsNullOrEmpty(localMediaPath) && File.Exists(localMediaPath))
                {
                    var fileExtension = Path.GetExtension(localMediaPath).ToLower();
                    if (IsImageFile(fileExtension) || IsVideoFile(fileExtension))
                    {
                        // For video files, try to get video info before opening
                        if (IsVideoFile(fileExtension))
                        {
                            try
                            {
                                var videoInfo = await VideoThumbnailHelper.GetVideoInfoAsync(localMediaPath);
                                if (videoInfo != null)
                                {
                                    var duration = videoInfo.Duration.ToString(@"mm\:ss");
                                    var resolution = $"{videoInfo.PrimaryVideoStream?.Width}x{videoInfo.PrimaryVideoStream?.Height}";
                                    Logger.Info($"Opening video: {Path.GetFileName(localMediaPath)} - Duration: {duration}, Resolution: {resolution}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to get video info: {ex.Message}");
                            }
                        }

                        // Open with default system application
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = localMediaPath,
                            UseShellExecute = true
                        });
                        Logger.Info($"Opened media preview for slot {slotIndex + 1}: {Path.GetFileName(localMediaPath)}");
                    }
                    else
                    {
                        ShowNotification($"Cannot preview this media type: {fileExtension}", true, 3000);
                    }
                }
                else
                {
                    ShowNotification($"No local media file available for preview (Slot {slotIndex + 1})", true, 3000);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to open media preview for slot {slotIndex + 1}: {ex.Message}");
                ShowNotification($"Failed to open media preview: {ex.Message}", true, 3000);
            }
        }

        /// <summary>
        /// Add media file to a specific slot
        /// </summary>
        private async Task AddMediaToSlot(int slotIndex)
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = $"Select Media File for Slot {slotIndex + 1}",
                    Filter = "Supported Media|*.mp4;*.jpg;*.jpeg",
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var filePath = openFileDialog.FileName;
                    await AddMediaFile(filePath, slotIndex);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to add media to slot {slotIndex}: {ex.Message}");
                ShowNotification($"Failed to add media: {ex.Message}", true);
            }
        }

        /// <summary>
        /// Add media file to device and update UI
        /// </summary>
        private async Task AddMediaFile(string filePath, int slotIndex)
        {
            string tempConvertedPath = null;
            string sourceFilePath = filePath;

            try
            {
                SetLoadingState(true, $"Adding media to slot {slotIndex + 1}...");

                // Check if video needs conversion
                if (IsVideoFile(Path.GetExtension(filePath)))
                {
                    try 
                    {
                        var videoInfo = await CMDevicesManager.Utilities.VideoConverter.GetMp4InfoAsync(filePath);
                        if (videoInfo != null)
                        {
                            // Check if conversion needed: not 480x480 OR bitrate > 2.5Mbps
                            bool needsResize = videoInfo.Width != 480 || videoInfo.Height != 480;
                            bool needsBitrateReduction = videoInfo.BitRate > 1500000; 

                            if (needsResize || needsBitrateReduction)
                            {
                                SetLoadingState(true, $"Optimizing video for device (480x480)...");
                                
                                tempConvertedPath = Path.Combine(Path.GetTempPath(), $"converted_{Guid.NewGuid()}.mp4");
                                bool converted = await CMDevicesManager.Utilities.VideoConverter.ConvertVideoAsync(filePath, tempConvertedPath, 480, 480, 1500);
                                
                                if (converted)
                                {
                                    sourceFilePath = tempConvertedPath;
                                    Logger.Info($"Video converted: {filePath} -> {sourceFilePath}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to check/convert video: {ex.Message}");
                    }
                }

                // Need to rename the file name start suspend_1.jpg, suspend_2.mp4, etc.
                var fileExtension = Path.GetExtension(sourceFilePath).ToLower();
                var newFileName = $"suspend_{slotIndex}{fileExtension}";

                // Determine the target directory based on device serial number
                string targetDir;
                string newFilePath;
                
                if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                {
                    // Use device-specific directory
                    targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias", _deviceInfo.SerialNumber);
                    newFilePath = Path.Combine(targetDir, newFileName);
                }
                else
                {
                    // Use general medias folder
                    targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias");
                    newFilePath = Path.Combine(targetDir, newFileName);
                }
                
                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);
                
                // Update offline media data service if available
                if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                {
                    var offlineDataService = ServiceLocator.OfflineMediaDataService;
                    offlineDataService.AddSuspendMediaFile(
                        _deviceInfo.SerialNumber,
                        newFileName,
                        sourceFilePath,
                        slotIndex,
                        transferId: (byte)(slotIndex + 1)
                    );
                    Logger.Info($"Added media file to offline data: {newFileName}");
                }
                else
                {
                    File.Copy(sourceFilePath, newFilePath, true);
                }

                // Transfer file to device using suspend media functionality
                var results = await _hidDeviceService.SendMultipleSuspendFilesAsync(
                    new List<string> { newFilePath }, 
                    startingTransferId: (byte)(slotIndex + 1));

                var successCount = results.Values.Count(r => r);
                
                if (successCount > 0)
                {
                    await _hidDeviceService.SetRealTimeDisplayAsync(false);
                    // Update UI to show the media
                    UpdateMediaSlotUI(slotIndex, newFilePath, true);
                    ShowNotification($"Media added to slot {slotIndex + 1} successfully.");
                    
                    // Refresh device status to get updated suspend media info
                    await RefreshDeviceStatus();
                }
                else
                {
                    ShowNotification($"Failed to add media to slot {slotIndex + 1}.", true);
                    
                    // Remove from offline data if device transfer failed
                    if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                    {
                        var offlineDataService = ServiceLocator.OfflineMediaDataService;
                        offlineDataService.RemoveSuspendMediaFile(_deviceInfo.SerialNumber, slotIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to add media file: {ex.Message}");
                Logger.Error($"Failed to add media file to slot {slotIndex + 1}: {ex.Message}", ex);
                ShowNotification($"Failed to add media: {ex.Message}", true);
            }
            finally
            {
                SetLoadingState(false);
                if (tempConvertedPath != null && File.Exists(tempConvertedPath))
                {
                    try { File.Delete(tempConvertedPath); } catch { }
                }
            }
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            // 关闭承载此 Page 的窗口
            Window.GetWindow(this)?.Close();
        }
        /// <summary>
        /// Update media slot UI to show media or empty state
        /// </summary>
        private async void UpdateMediaSlotUI(int slotIndex, string filePath = null, bool hasMedia = false)
        {
            try
            {
                var slotNumber = slotIndex + 1;
                var thumbnail = FindName($"MediaThumbnail{slotNumber}") as Image;
                var placeholder = FindName($"AddMediaPlaceholder{slotNumber}") as StackPanel;
                var infoOverlay = FindName($"MediaInfoOverlay{slotNumber}") as Border;
                var fileName = FindName($"MediaFileName{slotNumber}") as TextBlock;
                var removeButton = FindName($"RemoveMediaButton{slotNumber}") as Button;
                var mediaSlot = FindName($"MediaSlot{slotNumber}") as Border;

                // Release existing image source to prevent memory leaks and file locks
                if (thumbnail?.Source is BitmapImage existingBitmap)
                {
                    try
                    {
                        // Clear the source and dispose if possible
                        thumbnail.Source = null;
                        
                        // Force garbage collection of the bitmap to release file handles
                        existingBitmap.StreamSource?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to release existing image source for slot {slotNumber}: {ex.Message}");
                    }
                }

                if (hasMedia && !string.IsNullOrEmpty(filePath))
                {
                    // Show media state
                    if (thumbnail != null)
                    {
                        // Try to load actual media content
                        try
                        {
                            if (File.Exists(filePath))
                            {
                                var fileExtension = Path.GetExtension(filePath).ToLower();
                                if (IsImageFile(fileExtension))
                                {
                                    // Display image directly with proper resource management
                                    var bitmap = new BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load immediately and release file handle
                                    bitmap.UriSource = new Uri(filePath);
                                    bitmap.DecodePixelWidth = 200; // Optimize for display
                                    bitmap.EndInit();
                                    bitmap.Freeze(); // Make it thread-safe and optimize memory
                                    
                                    thumbnail.Source = bitmap;
                                    thumbnail.Visibility = Visibility.Visible;
                                }
                                else if (IsVideoFile(fileExtension))
                                {
                                    // Check cache first for video thumbnails
                                    BitmapImage? videoThumbnail = null;
                                    
                                    if (_videoThumbnailCache.ContainsKey(filePath))
                                    {
                                        videoThumbnail = _videoThumbnailCache[filePath];
                                        Logger.Info($"Using cached video thumbnail for slot {slotNumber}: {Path.GetFileName(filePath)}");
                                    }
                                    else
                                    {
                                        // Generate and cache video thumbnail
                                        videoThumbnail = await VideoThumbnailHelper.GenerateVideoThumbnailAsync(filePath, width: 200, height: 150);
                                        if (videoThumbnail != null)
                                        {
                                            videoThumbnail.Freeze(); // Make it thread-safe and optimize memory
                                            _videoThumbnailCache[filePath] = videoThumbnail;
                                            Logger.Info($"Generated and cached video thumbnail for slot {slotNumber}: {Path.GetFileName(filePath)}");
                                        }
                                    }
                                    
                                    if (videoThumbnail != null)
                                    {
                                        thumbnail.Source = videoThumbnail;
                                        thumbnail.Visibility = Visibility.Visible;
                                    }
                                    else
                                    {
                                        // Fallback: hide thumbnail and show video icon in placeholder
                                        thumbnail.Visibility = Visibility.Collapsed;
                                        Logger.Warn($"Failed to generate video thumbnail for slot {slotNumber}: {Path.GetFileName(filePath)}");
                                    }
                                }
                                else
                                {
                                    // For other file types
                                    thumbnail.Visibility = Visibility.Collapsed;
                                }
                            }
                            else
                            {
                                // File doesn't exist locally (device media), show media icon instead
                                thumbnail.Visibility = Visibility.Collapsed;
                            }
                        }
                        catch (Exception ex)
                        {
                            // If media loading fails, hide the image
                            Logger.Warn($"Failed to load media for slot {slotNumber}: {ex.Message}");
                            thumbnail.Visibility = Visibility.Collapsed;
                        }
                    }

                    if (placeholder != null) 
                    {
                        // Show appropriate icon based on media type
                        var iconBlock = placeholder.Children.OfType<TextBlock>().FirstOrDefault();
                        var textBlock = placeholder.Children.OfType<TextBlock>().LastOrDefault();
                        
                        if (iconBlock != null)
                        {
                            if (File.Exists(filePath))
                            {
                                var fileExtension = Path.GetExtension(filePath).ToLower();
                                if (IsVideoFile(fileExtension))
                                {
                                    iconBlock.Text = "\uE714"; // Video play icon
                                    iconBlock.Foreground = new SolidColorBrush(Colors.LightCoral);
                                    iconBlock.FontSize = 24; // Larger for video icon
                                }
                                else if (IsImageFile(fileExtension))
                                {
                                    // For images, hide placeholder since we show the actual image
                                    iconBlock.Text = "\uE91B"; // Image icon (backup)
                                    iconBlock.Foreground = new SolidColorBrush(Colors.LightGreen);
                                }
                                else
                                {
                                    iconBlock.Text = "\uE8A5"; // Generic media icon
                                    iconBlock.Foreground = new SolidColorBrush(Colors.LightBlue);
                                }
                            }
                            else
                            {
                                // Device media
                                iconBlock.Text = "\uE8B3"; // Device media icon
                                iconBlock.Foreground = new SolidColorBrush(Colors.LightBlue);
                            }
                        }
                        
                        if (textBlock != null && textBlock != iconBlock)
                        {
                            if (File.Exists(filePath))
                            {
                                var fileExtension = Path.GetExtension(filePath).ToLower();
                                if (IsVideoFile(fileExtension))
                                {
                                    textBlock.Text = "Video Media";
                                    textBlock.Foreground = new SolidColorBrush(Colors.LightCoral);
                                }
                                else if (IsImageFile(fileExtension))
                                {
                                    textBlock.Text = "Image Media";
                                    textBlock.Foreground = new SolidColorBrush(Colors.LightGreen);
                                }
                                else
                                {
                                    textBlock.Text = "Media File";
                                    textBlock.Foreground = new SolidColorBrush(Colors.LightBlue);
                                }
                            }
                            else
                            {
                                textBlock.Text = "Device Media";
                                textBlock.Foreground = new SolidColorBrush(Colors.LightBlue);
                            }
                        }
                        
                        // Show placeholder when image is not visible (for videos with thumbnails, hide placeholder)
                        // For videos with successful thumbnails, hide the placeholder; for failed thumbnails, show it
                        bool showPlaceholder = thumbnail?.Visibility != Visibility.Visible;
                        placeholder.Visibility = showPlaceholder ? Visibility.Visible : Visibility.Collapsed;
                    }
                    
                    if (infoOverlay != null) infoOverlay.Visibility = Visibility.Visible;
                    if (fileName != null) 
                    {
                        fileName.Text = File.Exists(filePath) ? Path.GetFileName(filePath) : Path.GetFileName(filePath);
                    }
                    if (removeButton != null) removeButton.Visibility = Visibility.Visible;
                    if (mediaSlot != null) mediaSlot.BorderBrush = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    // Show empty slot - ensure image source is properly released
                    if (thumbnail != null) 
                    {
                        // Properly release the image source
                        if (thumbnail.Source is BitmapImage bitmapToRelease)
                        {
                            try
                            {
                                thumbnail.Source = null;
                                bitmapToRelease.StreamSource?.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to release image source for empty slot {slotNumber}: {ex.Message}");
                            }
                        }
                        else
                        {
                            thumbnail.Source = null;
                        }
                        thumbnail.Visibility = Visibility.Collapsed;
                    }
                    if (placeholder != null) 
                    {
                        // Reset to add media state
                        var iconBlock = placeholder.Children.OfType<TextBlock>().FirstOrDefault();
                        var textBlock = placeholder.Children.OfType<TextBlock>().LastOrDefault();
                        
                        if (iconBlock != null)
                        {
                            iconBlock.Text = "\uE710"; // Add icon
                            iconBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                            iconBlock.FontSize = 20; // Reset font size
                        }
                        if (textBlock != null && textBlock != iconBlock)
                        {
                            textBlock.Text = "Add Media";
                            textBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                        }
                        
                        placeholder.Visibility = Visibility.Visible;
                    }
                    if (infoOverlay != null) infoOverlay.Visibility = Visibility.Collapsed;
                    if (removeButton != null) removeButton.Visibility = Visibility.Collapsed;
                    if (mediaSlot != null) mediaSlot.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update media slot UI: {ex.Message}");
                Logger.Error($"Failed to update media slot {slotIndex + 1} UI: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Check if file extension is an image
        /// </summary>
        private bool IsImageFile(string extension)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp" };
            return imageExtensions.Contains(extension);
        }

        /// <summary>
        /// Check if file extension is a video
        /// </summary>
        private bool IsVideoFile(string extension)
        {
            return VideoThumbnailHelper.IsSupportedVideoFile($"dummy{extension}");
        }

        /// <summary>
        /// Show media information for a slot
        /// </summary>
        private void ShowMediaInfo(int slotIndex)
        {
            var fileName = FindName($"MediaFileName{slotIndex + 1}") as TextBlock;
            var mediaName = fileName?.Text ?? $"Media {slotIndex + 1}";
            
            ShowNotification($"Media: {mediaName} (Slot {slotIndex + 1})", false, 3000);
        }

        /// <summary>
        /// Remove media from a specific slot
        /// </summary>
        private async void RemoveMedia_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;

            var button = sender as Button;
            if (button?.Tag is string tagStr && int.TryParse(tagStr, out int slotIndex))
            {
                try
                {
                    SetLoadingState(true, $"Removing media from slot {slotIndex + 1}...");

                    // Release image source before deletion to avoid file locks
                    var slotNumber = slotIndex + 1;
                    var thumbnail = FindName($"MediaThumbnail{slotNumber}") as Image;
                    if (thumbnail?.Source is BitmapImage bitmapToRelease)
                    {
                        try
                        {
                            thumbnail.Source = null;
                            bitmapToRelease.StreamSource?.Dispose();
                            Logger.Info($"Released image source for slot {slotNumber} before deletion");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to release image source for slot {slotNumber}: {ex.Message}");
                        }
                    }

                    // Get the actual filename from the offline media data or local media file
                    string? fileNameToDelete = null;
                    string? localMediaPath = null;
                    
                    // First try to get from offline media data service
                    if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                    {
                        var offlineDataService = ServiceLocator.OfflineMediaDataService;
                        var mediaFile = offlineDataService.GetSuspendMediaFile(_deviceInfo.SerialNumber, slotIndex);
                        
                        if (mediaFile != null)
                        {
                            fileNameToDelete = mediaFile.FileName;
                            localMediaPath = mediaFile.LocalPath;
                            Logger.Info($"Found media file in offline data for deletion: {fileNameToDelete}");
                        }
                    }
                    
                    // Fallback to trying to find local media file
                    if (string.IsNullOrEmpty(fileNameToDelete))
                    {
                        localMediaPath = TryFindLocalMediaFile(slotIndex);
                        if (!string.IsNullOrEmpty(localMediaPath) && File.Exists(localMediaPath))
                        {
                            fileNameToDelete = Path.GetFileName(localMediaPath);
                            Logger.Info($"Found local media file for deletion: {fileNameToDelete}");
                        }
                    }
                    
                    // Use fallback naming pattern if still no file found
                    if (string.IsNullOrEmpty(fileNameToDelete))
                    {
                        fileNameToDelete = $"suspend_{slotIndex}";
                        Logger.Info($"Using fallback filename for deletion: {fileNameToDelete}");
                    }

                    // Delete specific suspend file from device using the actual filename
                    var results = await _hidDeviceService.DeleteSuspendFilesAsync(fileNameToDelete);
                    var successCount = results.Values.Count(r => r);
                    
                    if (successCount > 0)
                    {
                        // Remove from offline media data service
                        if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                        {
                            var offlineDataService = ServiceLocator.OfflineMediaDataService;
                            offlineDataService.RemoveSuspendMediaFile(_deviceInfo.SerialNumber, slotIndex);
                            Logger.Info($"Removed media file from offline data: slot {slotIndex + 1}");
                        }
                        
                        // Remove from video thumbnail cache if it exists
                        if (!string.IsNullOrEmpty(localMediaPath) && _videoThumbnailCache.ContainsKey(localMediaPath))
                        {
                            try
                            {
                                var cachedThumbnail = _videoThumbnailCache[localMediaPath];
                                _videoThumbnailCache.Remove(localMediaPath);
                                
                                // Dispose the cached thumbnail to free memory
                                cachedThumbnail?.StreamSource?.Dispose();
                                Logger.Info($"Removed and disposed video thumbnail from cache: {Path.GetFileName(localMediaPath)}");
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to dispose cached thumbnail: {ex.Message}");
                            }
                        }
                        
                        // Also remove the local file if it exists and wasn't handled by offline service
                        if (!string.IsNullOrEmpty(localMediaPath) && File.Exists(localMediaPath))
                        {
                            try
                            {
                                // Small delay to ensure image source is fully released
                                await Task.Delay(100);
                                
                                File.Delete(localMediaPath);
                                Logger.Info($"Deleted local media file: {Path.GetFileName(localMediaPath)}");
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to delete local media file: {ex.Message}");
                            }
                        }
                        
                        // Update UI to show empty slot
                        UpdateMediaSlotUI(slotIndex, null, false);
                        ShowNotification($"Media removed from slot {slotIndex + 1} successfully.");
                        
                        // Refresh device status to get updated suspend media info
                        await RefreshDeviceStatus();
                    }
                    else
                    {
                        ShowNotification($"Failed to remove media from slot {slotIndex + 1}.", true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to remove media: {ex.Message}");
                    Logger.Error($"Failed to remove media from slot {slotIndex + 1}: {ex.Message}", ex);
                    ShowNotification($"Failed to remove media: {ex.Message}", true);
                }
                finally
                {
                    SetLoadingState(false);
                }
            }
        }

        /// <summary>
        /// Add multiple media files at once
        /// </summary>
        private async void AddAllMedia_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;

            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Multiple Media Files",
                    Filter = "Supported Media|*.mp4;*.jpg;*.jpeg;*.png;*.gif;*.bmp|" +
                            "Video Files|*.mp4|" +
                            "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp|" +
                            "All Files|*.*",
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = true
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var filePaths = openFileDialog.FileNames.Take(5).ToList(); // Limit to 5 files
                    
                    SetLoadingState(true, "Adding multiple media files...");

                    // Process each file individually with proper naming
                    var processedFiles = new List<string>();
                    for (int i = 0; i < filePaths.Count && i < 5; i++)
                    {
                        var originalPath = filePaths[i];
                        var fileExtension = Path.GetExtension(originalPath).ToLower();
                        var newFileName = $"suspend_{i}{fileExtension}";
                        
                        // Save to medias folder first
                        var mediasDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias");
                        if (!Directory.Exists(mediasDir))
                            Directory.CreateDirectory(mediasDir);
                        var newFilePath = Path.Combine(mediasDir, newFileName);
                        File.Copy(originalPath, newFilePath, true);
                        
                        processedFiles.Add(newFilePath);
                    }

                    // Send all processed files to device
                    var results = await _hidDeviceService.SendMultipleSuspendFilesAsync(processedFiles, startingTransferId: 1);
                    var successCount = results.Values.Count(r => r);
                    
                    if (successCount > 0)
                    {
                        ShowNotification($"Added {processedFiles.Count} media files successfully.");
                        
                        // Refresh device status to get updated suspend media info
                        await RefreshDeviceStatus();
                    }
                    else
                    {
                        ShowNotification("Failed to add media files.", true);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to add multiple media files: {ex.Message}");
                Logger.Error($"Failed to add multiple media files: {ex.Message}", ex);
                ShowNotification($"Failed to add media files: {ex.Message}", true);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        /// <summary>
        /// Clear all media files
        /// </summary>
        private async void ClearAllMedia_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidDeviceService == null || _isLoading) return;

            try
            {
                SetLoadingState(true, "Clearing all media files...");

                // Release all image sources first to prevent file locks
                for (int i = 0; i < 5; i++)
                {
                    var slotNumber = i + 1;
                    var thumbnail = FindName($"MediaThumbnail{slotNumber}") as Image;
                    if (thumbnail?.Source is BitmapImage bitmapToRelease)
                    {
                        try
                        {
                            thumbnail.Source = null;
                            bitmapToRelease.StreamSource?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to release image source for slot {slotNumber}: {ex.Message}");
                        }
                    }
                }

                // Delete all suspend files from device
                var results = await _hidDeviceService.DeleteSuspendFilesAsync("all");
                var successCount = results.Values.Count(r => r);
                
                if (successCount > 0)
                {
                    // Clear from offline media data service
                    if (_deviceInfo?.SerialNumber != null && ServiceLocator.IsOfflineMediaServiceInitialized)
                    {
                        var offlineDataService = ServiceLocator.OfflineMediaDataService;
                        offlineDataService.ClearAllSuspendMediaFiles(_deviceInfo.SerialNumber);
                        Logger.Info("Cleared all suspend media files from offline data");
                    }
                    
                    // Also clean up all local media files and cache
                    try
                    {
                        // First try device-specific directory
                        var deviceMediasDir = _deviceInfo?.SerialNumber != null 
                            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias", _deviceInfo.SerialNumber)
                            : null;
                        
                        var mediasDirectories = new List<string>();
                        if (deviceMediasDir != null && Directory.Exists(deviceMediasDir))
                        {
                            mediasDirectories.Add(deviceMediasDir);
                        }
                        
                        // Also check general medias directory
                        var generalMediasDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias");
                        if (Directory.Exists(generalMediasDir))
                        {
                            mediasDirectories.Add(generalMediasDir);
                        }
                        
                        // Clear video thumbnail cache and dispose cached thumbnails first
                        var thumbnailsToDispose = new List<BitmapImage>(_videoThumbnailCache.Values);
                        _videoThumbnailCache.Clear();
                        
                        foreach (var cachedThumbnail in thumbnailsToDispose)
                        {
                            try
                            {
                                cachedThumbnail?.StreamSource?.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to dispose cached thumbnail: {ex.Message}");
                            }
                        }
                        Logger.Info("Disposed all cached video thumbnails");
                        
                        // Small delay to ensure all image sources are released
                        await Task.Delay(200);
                        
                        foreach (var mediasDir in mediasDirectories)
                        {
                            // Delete all suspend media files (suspend_0.*, suspend_1.*, etc.)
                            for (int i = 0; i < 5; i++)
                            {
                                var searchPattern = $"suspend_{i}.*";
                                var matchingFiles = Directory.GetFiles(mediasDir, searchPattern);
                                
                                foreach (var file in matchingFiles)
                                {
                                    try
                                    {
                                        File.Delete(file);
                                        Logger.Info($"Deleted local media file: {Path.GetFileName(file)}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Warn($"Failed to delete local file {Path.GetFileName(file)}: {ex.Message}");
                                    }
                                }
                            }
                        }
                        
                        Logger.Info("Cleared video thumbnail cache and local media files");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to clean up local media files: {ex.Message}");
                    }
                    
                    ShowNotification("All media files cleared successfully.");
                    
                    // Refresh device status to get updated suspend media info
                    await RefreshDeviceStatus();
                }
                else
                {
                    ShowNotification("Failed to clear media files.", true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to clear all media: {ex.Message}");
                Logger.Error($"Failed to clear all media: {ex.Message}", ex);
                ShowNotification($"Failed to clear media files: {ex.Message}", true);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        // New methods for active suspend media display
        private void ShowActiveSuspendMedia()
        {
            try
            {
                // Ensure content area is visible
                var contentArea = FindName("SuspendModeContentArea") as FrameworkElement;
                if (contentArea != null) contentArea.Visibility = Visibility.Visible;
                
                // Load media slots instead of old list
                LoadSuspendMediaSlots(new DeviceStatus()); // Pass empty status for now
            }
            catch { /* UI elements may not be available */ }
        }

        private void ShowAddMediaButton()
        {
            try
            {
                // Ensure content area is visible
                var contentArea = FindName("SuspendModeContentArea") as FrameworkElement;
                if (contentArea != null) contentArea.Visibility = Visibility.Visible;
                
                // Clear all media slots to show add buttons
                for (int i = 0; i < 5; i++)
                {
                    UpdateMediaSlotUI(i, null, false);
                }
            }
            catch { /* UI elements may not be available */ }
        }

        private void LoadActiveSuspendMediaFiles()
        {
            // This method is now replaced by LoadSuspendMediaSlots
            // Keeping for backward compatibility but functionality moved to LoadSuspendMediaSlots
        }

        // TODO: Add method to get actual suspend media information from device
        private async Task<List<string>> GetActiveSuspendMediaFromDevice()
        {
            // This method is now replaced by GetSuspendMediaStatusFromDevice
            // Keeping for backward compatibility
            var activeFiles = new List<string>();
            
            try
            {
                var status = await GetSuspendMediaStatusFromDevice();
                // Convert status to file list if needed for backward compatibility
                // Implementation details would depend on the actual device status structure
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get suspend media from device: {ex.Message}");
            }
            
            return activeFiles;
        }

        /// <summary>
        /// Method that saves the current playback mode
        /// This should be called whenever _currentPlayMode changes
        /// </summary>
        private void SavePlaybackModeToService()
        {
            if (_deviceInfo?.SerialNumber == null || !ServiceLocator.IsOfflineMediaServiceInitialized)
                return;

            try
            {
                var offlineDataService = ServiceLocator.OfflineMediaDataService;
                offlineDataService.UpdateDevicePlaybackMode(_deviceInfo.SerialNumber, _currentPlayMode);

                Console.WriteLine($"Saved playback mode for device {_deviceInfo.SerialNumber}: {_currentPlayMode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save playback mode for device {_deviceInfo?.SerialNumber}: {ex.Message}");
            }
        }

        #endregion
    }
}
