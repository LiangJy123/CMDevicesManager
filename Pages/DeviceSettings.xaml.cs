using HID.DisplayController;
using HidApi;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

            var confirmed = await ShowConfirmationAsync(
                "Confirm Device Reboot",
                "Are you sure you want to reboot the device?\n\nThe device will restart and may temporarily disconnect.",
                "Reboot",
                "Cancel",
                true);

            if (!confirmed) return;

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

            var confirmed = await ShowConfirmationAsync(
                "Confirm Factory Reset",
                "WARNING: This will reset the device to factory defaults!\n\n" +
                "All settings, configurations, and stored data will be lost.\n" +
                "This action cannot be undone.\n\n" +
                "Are you sure you want to continue?",
                "Factory Reset",
                "Cancel",
                true);

            if (!confirmed) return;

            // Double confirmation for factory reset
            var doubleConfirmed = await ShowConfirmationAsync(
                "Factory Reset - Final Confirmation",
                "FINAL CONFIRMATION:\n\n" +
                "This will permanently erase all device data and settings.\n" +
                "Click 'RESET NOW' only if you are absolutely certain.",
                "RESET NOW",
                "Cancel",
                true);

            if (!doubleConfirmed) return;

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

            var confirmed = await ShowConfirmationAsync(
                "Confirm Firmware Update",
                $"You are about to update the device firmware.\n\n" +
                $"File: {fileInfo.Name}\n" +
                $"Size: {fileInfo.Length / 1024.0:F1} KB\n\n" +
                $"WARNING: Do not disconnect the device during firmware update!\n" +
                $"Interrupting the update process may permanently damage the device.\n\n" +
                $"Continue with firmware update?",
                "Update Firmware",
                "Cancel",
                true);

            if (!confirmed) return;

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
            
            // Show confirmation for enabling sleep mode
            if (isEnabled)
            {
                var confirmed = await ShowConfirmationAsync(
                    "Enable Sleep Mode",
                    "Are you sure you want to enable sleep mode?\n\nThe display will enter sleep mode when idle.",
                    "Enable",
                    "Cancel");

                if (!confirmed)
                {
                    // Revert toggle state if user cancels
                    toggle.IsChecked = false;
                    return;
                }
            }

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
            
            // Show confirmation for activating suspend mode
            if (isActivated)
            {
                var confirmed = await ShowConfirmationAsync(
                    "Activate Suspend Mode",
                    "Are you sure you want to activate suspend mode?\n\n" +
                    "This will put the device into suspend mode for offline media playback.\n" +
                    "The device may become temporarily unresponsive during this process.",
                    "Activate",
                    "Cancel",
                    true);

                if (!confirmed)
                {
                    // Revert toggle state if user cancels
                    toggle.IsChecked = false;
                    return;
                }
            }

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
                    
                    if (!isActivated)
                    {
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
