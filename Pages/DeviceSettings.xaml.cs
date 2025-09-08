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
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;

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
        }

        private void EnableAllControls()
        {
            RebootButton.IsEnabled = true;
            FactoryResetButton.IsEnabled = true;
            UpdateFirmwareButton.IsEnabled = true;
            RefreshStatusButton.IsEnabled = true;
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
                    ShowErrorMessage("Invalid device path");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize device: {ex.Message}");
                ShowErrorMessage($"Failed to connect to device: {ex.Message}");
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
                ShowErrorMessage($"Failed to load device information: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void UpdateCapabilitiesDisplay(DisplayCtrlCapabilities capabilities)
        {
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
                }
                else
                {
                    // Set all status to N/A if failed to get status
                    BrightnessText.Text = "N/A";
                    RotationText.Text = "N/A";
                    OsdStateText.Text = "N/A";
                    KeepAliveText.Text = "N/A";
                    DisplaySleepText.Text = "N/A";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to refresh device status: {ex.Message}");
                ShowErrorMessage($"Failed to refresh device status: {ex.Message}");
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

        private void ShowErrorMessage(string message)
        {
            MessageBox.Show(message, "Device Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ShowSuccessMessage(string message)
        {
            MessageBox.Show(message, "Device Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDeviceStatus();
        }

        private async void RebootButton_Click(object sender, RoutedEventArgs e)
        {
            if (_displayController == null || _isLoading) return;

            var result = MessageBox.Show(
                "Are you sure you want to reboot the device?\n\nThe device will restart and may temporarily disconnect.",
                "Confirm Device Reboot",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                SetLoadingState(true, "Rebooting device...");

                _displayController.Reboot();
                
                // Wait a moment for the command to be sent
                await Task.Delay(1000);

                ShowSuccessMessage("Reboot command sent successfully. The device will restart shortly.");
                
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
                ShowErrorMessage($"Failed to reboot device: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async void FactoryResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_displayController == null || _isLoading) return;

            var result = MessageBox.Show(
                "WARNING: This will reset the device to factory defaults!\n\n" +
                "All settings, configurations, and stored data will be lost.\n" +
                "This action cannot be undone.\n\n" +
                "Are you sure you want to continue?",
                "Confirm Factory Reset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            // Double confirmation
            var confirmResult = MessageBox.Show(
                "FINAL CONFIRMATION:\n\n" +
                "This will permanently erase all device data and settings.\n" +
                "Click YES only if you are absolutely certain.",
                "Factory Reset - Final Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (confirmResult != MessageBoxResult.Yes) return;

            try
            {
                SetLoadingState(true, "Performing factory reset...");

                _displayController.FactoryReset();
                
                // Wait a moment for the command to be sent
                await Task.Delay(1000);

                ShowSuccessMessage("Factory reset command sent successfully. The device will reset to defaults and restart.");
                
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
                ShowErrorMessage($"Failed to perform factory reset: {ex.Message}");
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

            var result = MessageBox.Show(
                $"You are about to update the device firmware.\n\n" +
                $"File: {fileInfo.Name}\n" +
                $"Size: {fileInfo.Length / 1024.0:F1} KB\n\n" +
                $"WARNING: Do not disconnect the device during firmware update!\n" +
                $"Interrupting the update process may permanently damage the device.\n\n" +
                $"Continue with firmware update?",
                "Confirm Firmware Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                SetLoadingState(true, "Updating firmware... DO NOT DISCONNECT!");

                bool success = await _displayController.FirmwareUpgradeWithFileAndResponse(filePath);
                
                if (success)
                {
                    ShowSuccessMessage(
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
                    ShowErrorMessage("Firmware update failed. Please check the firmware file and try again.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update firmware: {ex.Message}");
                ShowErrorMessage($"Failed to update firmware: {ex.Message}");
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
