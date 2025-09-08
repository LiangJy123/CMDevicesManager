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
using System.Windows.Threading;
using Color = System.Windows.Media.Color;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for DeviceSettings.xaml
    /// </summary>
    public partial class DeviceSettings : Page
    {
        private DeviceInfo? _deviceInfo;
        private DisplayController? _displayController;
        private bool _isLoading = false;

        // Notification system
        private DispatcherTimer? _notificationTimer;
        private TaskCompletionSource<bool>? _confirmationResult;
        
        // Suspend media tracking
        private List<string> _activeSuspendMediaFiles = new();

        public DeviceSettings()
        {
            InitializeComponent();
            DisableAllControls();
        }

        public DeviceSettings(DeviceInfo deviceInfo) : this()
        {
            _deviceInfo = deviceInfo;
            InitializeDevice();
        }

        private void DisableAllControls()
        {
            RebootButton.IsEnabled = false;
            FactoryResetButton.IsEnabled = false;
            UpdateFirmwareButton.IsEnabled = false;
            RefreshStatusButton.IsEnabled = false;
            
            // Disable display control toggles
            try
            {
                SleepModeToggle.IsEnabled = false;
                SuspendModeToggle.IsEnabled = false;
                if (AddSuspendMediaButton != null) AddSuspendMediaButton.IsEnabled = false;
            }
            catch { /* Toggles may not be available yet */ }
        }

        private void EnableAllControls()
        {
            RebootButton.IsEnabled = true;
            FactoryResetButton.IsEnabled = true;
            UpdateFirmwareButton.IsEnabled = true;
            RefreshStatusButton.IsEnabled = true;
            
            // Enable display control toggles
            try
            {
                SleepModeToggle.IsEnabled = true;
                SuspendModeToggle.IsEnabled = true;
                if (AddSuspendMediaButton != null) AddSuspendMediaButton.IsEnabled = true;
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

                // Initialize display controller
                if (!string.IsNullOrEmpty(_deviceInfo.Path))
                {
                    _displayController = new DisplayController(_deviceInfo.Path);
                    _displayController.StartResponseListener();

                    // Load device firmware info and capabilities
                    await LoadDeviceInformation();

                    // Load current device status
                    await RefreshDeviceStatus();

                    EnableAllControls();
                    SetConnectionStatus(true);
                }
                else
                {
                    ShowNotification("Invalid device path", true);
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

        private async Task LoadDeviceInformation()
        {
            if (_displayController == null) return;

            try
            {
                SetLoadingState(true, "Loading device information...");

                // Get firmware and hardware versions
                var deviceFWInfo = _displayController.DeviceFWInfo;
                if (deviceFWInfo != null)
                {
                    HardwareVersionText.Text = deviceFWInfo.HardwareVersion.ToString();
                    FirmwareVersionText.Text = deviceFWInfo.FirmwareVersion.ToString();
                }

                // Get device capabilities
                var capabilities = _displayController.Capabilities;
                if (capabilities != null)
                {
                    UpdateCapabilitiesDisplay(capabilities);
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
            
            // The following code is commented out since the UI elements are hidden:
            /*
            // Display modes
            OffModeText.Text = $"Off Mode: {(capabilities.OffModeSupported ? "Supported" : "Not Supported")}";
            SsrModeText.Text = $"SSR Mode: {(capabilities.SsrModeSupported ? "Supported" : "Not Supported")}";

            // Resolution and performance
            ResolutionText.Text = $"{capabilities.SsrVsWidth}x{capabilities.SsrVsHeight}";
            MaxFpsText.Text = $"Max FPS: {capabilities.SsrVsMaxFps}";

            // Hardware features
            var hwDecodeFormats = new List<string>();
            if (capabilities.H264DecodeSupported) hwDecodeFormats.Add("H.264");
            if (capabilities.JpegDecodeSupported) hwDecodeFormats.Add("JPEG");
            
            HwDecodeText.Text = $"Hardware Decode: {(hwDecodeFormats.Count > 0 ? string.Join(", ", hwDecodeFormats) : "None")}";
            OverlayText.Text = $"Overlay Support: {(capabilities.SsrVsHwSupportOverlay != 0 ? "Yes" : "No")}";

            // File limits
            MaxFileSizeText.Text = $"Max File Size: {capabilities.SsrVsMaxFileSize} MB";
            MaxFrameCountText.Text = $"Max Frame Count: {capabilities.SsrVsMaxFrameCnt}";
            */
            
            Debug.WriteLine("Device capabilities loaded but UI elements are hidden");
        }

        private async Task RefreshDeviceStatus()
        {
            if (_displayController == null) return;

            try
            {
                SetLoadingState(true, "Refreshing device status...");

                var deviceStatus = await _displayController.GetDeviceStatus();
                if (deviceStatus.HasValue)
                {
                    var status = deviceStatus.Value;
                    
                    BrightnessText.Text = $"{status.Brightness}%";
                    RotationText.Text = $"{status.Degree}°";
                    OsdStateText.Text = status.IsOsdActive ? "Active" : "Inactive";
                    KeepAliveText.Text = $"{status.KeepAliveTimeout}s";
                    DisplaySleepText.Text = status.IsDisplayInSleep ? "Enabled" : "Disabled";
                    SuspendModeText.Text = "Unknown"; // Suspend mode status not available in device status
                    
                    // Update toggle switches state based on device status
                    try
                    {
                        SleepModeToggle.IsChecked = status.IsDisplayInSleep;
                        
                        // Parse suspend media information from device status
                        // The device status contains suspendMediaActive array [1,0,0,1,1]
                        // We need to extract this information from the status
                        UpdateSuspendModeDisplay(status);
                    }
                    catch { /* Toggle may not be available yet */ }
                }
                else
                {
                    // Set all status to N/A if failed to get status
                    BrightnessText.Text = "N/A";
                    RotationText.Text = "N/A";
                    OsdStateText.Text = "N/A";
                    KeepAliveText.Text = "N/A";
                    DisplaySleepText.Text = "N/A";
                    SuspendModeText.Text = "N/A";
                    
                    // Reset toggle switches
                    try
                    {
                        SleepModeToggle.IsChecked = false;
                        SuspendModeToggle.IsChecked = false;
                        
                        // Update suspend mode display for reset state
                        UpdateSuspendModeDisplay(null);
                    }
                    catch { /* Toggles may not be available yet */ }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to refresh device status: {ex.Message}");
                ShowNotification($"Failed to refresh device status: {ex.Message}", true);
            }
            finally
            {
                SetLoadingState(false);
            }
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
            else if (_displayController != null)
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
                if (status.HasValue)
                {
                    // Check if device is in suspend mode by examining if any suspend media is active
                    // This is inferred from the presence of active suspend media
                    bool hasSuspendMedia = false;
                    _activeSuspendMediaFiles.Clear();

                    // Since DeviceStatus doesn't directly expose suspend media info in the struct,
                    // we'll need to make a separate call to get the actual suspend media status
                    // For now, we'll use the toggle state and some heuristics
                    bool suspendModeActive = SuspendModeToggle?.IsChecked == true;
                    
                    if (suspendModeActive)
                    {
                        // When suspend mode is active, try to get the actual suspend media files
                        // This would require a separate API call, but for now we'll simulate
                        LoadActiveSuspendMediaFiles();
                        hasSuspendMedia = _activeSuspendMediaFiles.Any();
                    }

                    // Update UI visibility based on suspend mode state
                    if (hasSuspendMedia)
                    {
                        ShowActiveSuspendMedia();
                    }
                    else
                    {
                        ShowAddMediaButton();
                    }
                }
                else
                {
                    // No device status available, show add media button
                    ShowAddMediaButton();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update suspend mode display: {ex.Message}");
                ShowAddMediaButton(); // Fallback to add media button
            }
        }

        private void LoadActiveSuspendMediaFiles()
        {
            // This method would ideally make an API call to get actual suspend media files
            // For now, we'll simulate with some sample data when suspend mode is active
            _activeSuspendMediaFiles.Clear();
            
            if (SuspendModeToggle?.IsChecked == true)
            {
                // In a real implementation, this would:
                // 1. Parse the suspendMediaActive array from device status
                // 2. Map active indices to actual file names
                // 3. Display the real file information
                
                // For demonstration, we'll use simulated data
                // The actual implementation would call an API like:
                // var mediaInfo = await _displayController.GetSuspendMediaListWithResponse();
                
                _activeSuspendMediaFiles.AddRange(new[]
                {
                    "suspend_media_1.mp4",
                    "suspend_media_2.mp4", 
                    "suspend_media_3.mp4"
                });
            }
        }

        // TODO: Add method to get actual suspend media information from device
        private async Task<List<string>> GetActiveSuspendMediaFromDevice()
        {
            var activeFiles = new List<string>();
            
            try
            {
                if (_displayController != null)
                {
                    // This would be the actual API call to get suspend media information
                    // For now, we'll use the existing status information
                    var deviceStatus = await _displayController.GetDeviceStatus();
                    
                    if (deviceStatus.HasValue)
                    {
                        // Parse the suspend media information from device status
                        // The device status JSON contains: "suspendMediaActive":[1,0,0,1,1]
                        // This indicates which suspend media slots are active
                        // We would need to map these to actual file names
                        
                        // For now, simulate based on toggle state
                        if (SuspendModeToggle?.IsChecked == true)
                        {
                            activeFiles.AddRange(new[]
                            {
                                "Device Media File 1.mp4",
                                "Device Media File 2.mp4"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get suspend media from device: {ex.Message}");
            }
            
            return activeFiles;
        }

        // Method to refresh suspend media display without full device status update
        private async Task RefreshSuspendMediaDisplay()
        {
            try
            {
                if (SuspendModeToggle?.IsChecked == true)
                {
                    // Get actual suspend media files from device
                    _activeSuspendMediaFiles = await GetActiveSuspendMediaFromDevice();
                    
                    if (_activeSuspendMediaFiles.Any())
                    {
                        ShowActiveSuspendMedia();
                    }
                    else
                    {
                        ShowAddMediaButton();
                    }
                }
                else
                {
                    ShowAddMediaButton();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to refresh suspend media display: {ex.Message}");
                ShowAddMediaButton();
            }
        }

        private void ShowActiveSuspendMedia()
        {
            try
            {
                if (ActiveSuspendMediaPanel != null) ActiveSuspendMediaPanel.Visibility = Visibility.Visible;
                if (AddMediaButtonPanel != null) AddMediaButtonPanel.Visibility = Visibility.Collapsed;
                
                // Update the list of active suspend media files
                if (SuspendMediaList != null) SuspendMediaList.ItemsSource = _activeSuspendMediaFiles;
            }
            catch { /* UI elements may not be available */ }
        }

        private void ShowAddMediaButton()
        {
            try
            {
                if (ActiveSuspendMediaPanel != null) ActiveSuspendMediaPanel.Visibility = Visibility.Collapsed;
                if (AddMediaButtonPanel != null) AddMediaButtonPanel.Visibility = Visibility.Visible;
                
                // Clear the media list
                if (SuspendMediaList != null) SuspendMediaList.ItemsSource = null;
            }
            catch { /* UI elements may not be available */ }
        }

        // Notification System Methods
        private void ShowNotification(string message, bool isError = false, int durationMs = 5000)
        {
            Dispatcher.Invoke(() =>
            {
                NotificationMessage.Text = message;
                
                if (isError)
                {
                    NotificationIcon.Text = "\uE783"; // Error icon
                    NotificationIcon.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                }
                else
                {
                    NotificationIcon.Text = "\uE7BA"; // Success icon
                    NotificationIcon.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                }
                
                NotificationPanel.Visibility = Visibility.Visible;
                
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
            NotificationPanel.Visibility = Visibility.Collapsed;
            _notificationTimer?.Stop();
        }

        // PRESERVED FOR FUTURE USE: Confirmation Dialog System
        // These methods and UI elements are kept for potential future requirements
        // Currently not used - tooltips provide information instead of confirmation dialogs
        private Task<bool> ShowConfirmationAsync(String title, String message, String confirmText = "Confirm", 
            String cancelText = "Cancel", Boolean isWarning = false)
        {
            _confirmationResult = new TaskCompletionSource<bool>();
            
            Dispatcher.Invoke(() =>
            {
                ConfirmationTitle.Text = title;
                ConfirmationMessage.Text = message;
                ConfirmationConfirmText.Text = confirmText;
                
                if (isWarning)
                {
                    ConfirmationIcon.Text = "\uE7BA"; // Warning icon
                    ConfirmationIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                    ConfirmationButtonBorder.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                }
                else
                {
                    ConfirmationIcon.Text = "\uE8FD"; // Question icon
                    ConfirmationIcon.Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243)); // Blue
                    ConfirmationButtonBorder.Background = new SolidColorBrush(Color.FromRgb(33, 150, 243)); // Blue
                }
                
                ConfirmationDialog.Visibility = Visibility.Visible;
            });
            
            return _confirmationResult.Task;
        }

        private void HideConfirmation(bool result)
        {
            ConfirmationDialog.Visibility = Visibility.Collapsed;
            _confirmationResult?.SetResult(result);
            _confirmationResult = null;
        }

        // Event handlers for notification and confirmation dialogs
        private void NotificationCloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideNotification();
        }

        private void ConfirmationCancelButton_Click(object sender, RoutedEventArgs e)
        {
            HideConfirmation(false);
        }

        private void ConfirmationConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            HideConfirmation(true);
        }

        private async void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDeviceStatus();
        }

        private async void RebootButton_Click(object sender, RoutedEventArgs e)
        {
            if (_displayController == null || _isLoading) return;

            // No confirmation dialog needed - tooltip provides the info
            try
            {
                SetLoadingState(true, "Rebooting device...");

                _displayController.Reboot();
                
                // Wait a moment for the command to be sent
                await Task.Delay(1000);

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
            if (_displayController == null || _isLoading) return;

            // No confirmation dialog needed - tooltip provides the warning
            try
            {
                SetLoadingState(true, "Performing factory reset...");

                _displayController.FactoryReset();
                
                // Wait a moment for the command to be sent
                await Task.Delay(1000);

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
            if (_displayController == null || _isLoading) return;

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

                bool success = await _displayController.FirmwareUpgradeWithFileAndResponse(filePath);
                
                if (success)
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
            if (_displayController == null || _isLoading) return;
            
            var toggle = sender as ToggleButton;
            if (toggle == null) return;
            
            bool isEnabled = toggle.IsChecked == true;
            
            // No confirmation dialog needed - tooltip provides the info
            try
            {
                SetLoadingState(true, isEnabled ? "Enabling sleep mode..." : "Disabling sleep mode...");

                var response = await _displayController.SendCmdDisplayInSleepWithResponse(isEnabled);
                
                if (response?.IsSuccess == true)
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
            if (_displayController == null || _isLoading) return;
            
            var toggle = sender as ToggleButton;
            if (toggle == null) return;
            
            bool isActivated = toggle.IsChecked == true;
            
            // No confirmation dialog needed - tooltip provides the info
            try
            {
                SetLoadingState(true, isActivated ? "Activating suspend mode..." : "Clearing suspend mode...");

                bool success;
                if (isActivated)
                {
                    success = await _displayController.SetSuspendModeWithResponse();
                }
                else
                {
                    success = await _displayController.ClearSuspendModeWithResponse();
                }
                
                if (success)
                {
                    ShowNotification(isActivated ? "Suspend mode activated successfully." : "Suspend mode cleared successfully.");
                    
                    // Update suspend mode display based on new state
                    if (isActivated)
                    {
                        // When activating suspend mode, refresh the display
                        await RefreshSuspendMediaDisplay();
                    }
                    else
                    {
                        // When clearing suspend mode, show add media button
                        ShowAddMediaButton();
                        
                        // Wait a moment for device to exit suspend mode
                        await Task.Delay(2000);
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

        // Add Suspend Media Event Handler
        private async void AddSuspendMediaButton_Click(object sender, RoutedEventArgs e)
        {
            if (_displayController == null || _isLoading) return;

            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Suspend Media Files",
                Filter = "Video Files (*.mp4;*.avi;*.mov;*.wmv)|*.mp4;*.avi;*.mov;*.wmv|Image Files (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All Files (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (openFileDialog.ShowDialog() != true) return;

            var filePaths = openFileDialog.FileNames.ToList();
            if (!filePaths.Any()) return;

            try
            {
                SetLoadingState(true, "Adding suspend media files...");

                // Send multiple suspend files
                bool success = await _displayController.SendMultipleSuspendFilesWithResponse(filePaths);
                
                if (success)
                {
                    ShowNotification($"Successfully added {filePaths.Count} suspend media file(s).");
                    
                    // Update the local list with added files
                    _activeSuspendMediaFiles.Clear();
                    _activeSuspendMediaFiles.AddRange(filePaths.Select(Path.GetFileName));
                    
                    // Activate suspend mode if not already active
                    if (SuspendModeToggle?.IsChecked != true)
                    {
                        SuspendModeToggle.IsChecked = true;
                        await _displayController.SetSuspendModeWithResponse();
                    }
                    
                    // Show the active media files
                    ShowActiveSuspendMedia();
                }
                else
                {
                    ShowNotification("Failed to add suspend media files. Please try again.", true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to add suspend media files: {ex.Message}");
                ShowNotification($"Failed to add suspend media files: {ex.Message}", true);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clean up timers
                _notificationTimer?.Stop();
                _notificationTimer = null;
                
                // Clean up display controller
                _displayController?.StopResponseListener();
                _displayController?.Dispose();
                _displayController = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during page cleanup: {ex.Message}");
            }
        }
    }
}
