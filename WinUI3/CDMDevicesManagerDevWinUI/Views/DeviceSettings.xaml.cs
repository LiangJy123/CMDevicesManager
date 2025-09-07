using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading.Tasks;
using HID.DisplayController;
using System.Diagnostics;

namespace CDMDevicesManagerDevWinUI.Views
{
    /// <summary>
    /// Device Settings page for managing individual device configurations
    /// </summary>
    public sealed partial class DeviceSettings : Page
    {
        private DeviceInfoViewModel? _deviceViewModel;
        private DisplayController? _displayController;
        private bool _isOfflineModeChanging = false;
        private bool _isBrightnessChanging = false;
        private bool _isRotationChanging = false;

        public DeviceInfoViewModel? DeviceViewModel => _deviceViewModel;

        public DeviceSettings()
        {
            this.InitializeComponent();
        }

        // Constructor that accepts DeviceInfoViewModel for direct navigation
        public DeviceSettings(DeviceInfoViewModel deviceViewModel) : this()
        {
            _deviceViewModel = deviceViewModel;
            // Initialize after the component is loaded
            this.Loaded += (s, e) => 
            {
                UpdateDeviceInfoDisplay();
                InitializeDeviceController();
                _ = LoadDeviceInformationAsync();
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            if (e.Parameter is DeviceInfoViewModel deviceViewModel)
            {
                _deviceViewModel = deviceViewModel;
                UpdateDeviceInfoDisplay();
                InitializeDeviceController();
                _ = LoadDeviceInformationAsync();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CleanupDeviceController();
        }

        private void UpdateDeviceInfoDisplay()
        {
            if (_deviceViewModel != null)
            {
                ProductNameText.Text = _deviceViewModel.ProductName;
                SerialNumberText.Text = _deviceViewModel.SerialNumber;
                ManufacturerText.Text = _deviceViewModel.ManufacturerName;
            }
        }

        private void InitializeDeviceController()
        {
            if (_deviceViewModel?.DevicePath != null)
            {
                try
                {
                    _displayController = new DisplayController(_deviceViewModel.DevicePath);
                    _displayController.StartResponseListener();
                    Debug.WriteLine($"Initialized DisplayController for device: {_deviceViewModel.ProductName}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to initialize DisplayController: {ex.Message}");
                    _ = ShowErrorMessageAsync($"Failed to connect to device: {ex.Message}");
                }
            }
        }

        private void CleanupDeviceController()
        {
            try
            {
                _displayController?.StopResponseListener();
                _displayController?.Dispose();
                _displayController = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        private async Task LoadDeviceInformationAsync()
        {
            await LoadFirmwareInformationAsync();
            await LoadCurrentDeviceSettingsAsync();
        }

        private async Task LoadFirmwareInformationAsync()
        {
            if (_displayController == null) return;

            try
            {
                // Get device firmware and hardware info
                var deviceInfo = _displayController.DeviceFWInfo;
                if (deviceInfo != null)
                {
                    HardwareVersionText.Text = deviceInfo.HardwareVersion.ToString();
                    FirmwareVersionText.Text = deviceInfo.FirmwareVersion.ToString();
                }
                else
                {
                    HardwareVersionText.Text = "N/A";
                    FirmwareVersionText.Text = "N/A";
                }

                Debug.WriteLine($"Loaded device firmware info: HW={HardwareVersionText.Text}, FW={FirmwareVersionText.Text}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load firmware information: {ex.Message}");
                HardwareVersionText.Text = "Error";
                FirmwareVersionText.Text = "Error";
            }
        }

        private async Task LoadCurrentDeviceSettingsAsync()
        {
            if (_displayController == null) return;

            try
            {
                // Get current device status
                var deviceStatus = await _displayController.GetDeviceStatus();
                if (deviceStatus.HasValue)
                {
                    var status = deviceStatus.Value;
                    
                    // Update brightness slider
                    _isBrightnessChanging = true;
                    BrightnessSlider.Value = status.Brightness;
                    BrightnessValueText.Text = status.Brightness.ToString();
                    _isBrightnessChanging = false;

                    // Update rotation radio buttons
                    _isRotationChanging = true;
                    UpdateRotationSelection(status.Degree);
                    _isRotationChanging = false;

                    // Update status information
                    UpdateStatusDisplay(status);

                    Debug.WriteLine($"Loaded current device settings: Brightness={status.Brightness}%, Rotation={status.Degree}°");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load current device settings: {ex.Message}");
                _ = ShowErrorMessageAsync($"Failed to load device settings: {ex.Message}");
            }
        }

        private void UpdateRotationSelection(int degree)
        {
            Rotation0.IsChecked = degree == 0;
            Rotation90.IsChecked = degree == 90;
            Rotation180.IsChecked = degree == 180;
            Rotation270.IsChecked = degree == 270;
        }

        private void UpdateStatusDisplay(DeviceStatus status)
        {
            StatusInfoText.Text = $"Device Status Information:\n\n" +
                                  $"Brightness: {status.Brightness}%\n" +
                                  $"Rotation: {status.Degree}°\n" +
                                  $"OSD State: {(status.IsOsdActive ? "Active" : "Inactive")}\n" +
                                  $"Keep Alive Timeout: {status.KeepAliveTimeout}s\n" +
                                  $"Display in Sleep: {(status.IsDisplayInSleep ? "Yes" : "No")}\n" +
                                  $"Max Suspend Media: {status.MaxSuspendMediaCount}\n" +
                                  $"Active Suspend Media: {status.ActiveSuspendMediaCount}\n" +
                                  $"Suspend Media Indices: [{string.Join(", ", status.ActiveSuspendMediaIndices)}]";
        }

        // Event Handlers

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            CheckUpdateButton.Content = "Checking...";
            
            UpdateStatusIndicator.Fill = new SolidColorBrush(Colors.Orange);
            UpdateStatusText.Text = "Checking for updates...";

            try
            {
                // Simulate firmware update check
                await Task.Delay(2000);

                // For demo purposes, randomly decide if update is available
                bool updateAvailable = DateTime.Now.Millisecond % 3 == 0;

                if (updateAvailable)
                {
                    UpdateStatusIndicator.Fill = new SolidColorBrush(Colors.Gold);
                    UpdateStatusText.Text = "Update available";
                    InstallUpdateButton.IsEnabled = true;
                    CheckUpdateButton.Content = "Recheck";
                    
                    _ = ShowInfoMessageAsync("Firmware update available! Click 'Install Update' to proceed.");
                }
                else
                {
                    UpdateStatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
                    UpdateStatusText.Text = "Up to date";
                    InstallUpdateButton.IsEnabled = false;
                    CheckUpdateButton.Content = "Check for Updates";
                    
                    _ = ShowInfoMessageAsync("Device firmware is up to date.");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                UpdateStatusText.Text = "Check failed";
                CheckUpdateButton.Content = "Retry";
                _ = ShowErrorMessageAsync($"Failed to check for updates: {ex.Message}");
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
            }
        }

        private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            InstallUpdateButton.IsEnabled = false;
            InstallUpdateButton.Content = "Installing...";
            
            UpdateStatusIndicator.Fill = new SolidColorBrush(Colors.Orange);
            UpdateStatusText.Text = "Installing update...";

            try
            {
                // Simulate firmware update installation
                await Task.Delay(5000);

                UpdateStatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
                UpdateStatusText.Text = "Update completed";
                InstallUpdateButton.Content = "Install Update";
                
                _ = ShowInfoMessageAsync("Firmware update completed successfully! Device will restart.");
                
                // Simulate device restart
                await RestartDeviceAsync();
            }
            catch (Exception ex)
            {
                UpdateStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                UpdateStatusText.Text = "Update failed";
                InstallUpdateButton.Content = "Retry";
                _ = ShowErrorMessageAsync($"Failed to install update: {ex.Message}");
            }
            finally
            {
                InstallUpdateButton.IsEnabled = false;
            }
        }

        private async void RestartDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            var result = await ShowConfirmationDialogAsync("Restart Device", 
                "Are you sure you want to restart the device? This will temporarily disconnect the device.");
                
            if (result)
            {
                await RestartDeviceAsync();
            }
        }

        private async void FactoryResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = await ShowConfirmationDialogAsync("Factory Reset", 
                "Are you sure you want to perform a factory reset? This will erase all device settings and restore defaults.");
                
            if (result)
            {
                await PerformFactoryResetAsync();
            }
        }

        private async void RefreshInfoButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshInfoButton.IsEnabled = false;
            RefreshInfoButton.Content = "Refreshing...";

            try
            {
                await LoadDeviceInformationAsync();
                _ = ShowInfoMessageAsync("Device information refreshed successfully.");
            }
            catch (Exception ex)
            {
                _ = ShowErrorMessageAsync($"Failed to refresh device information: {ex.Message}");
            }
            finally
            {
                RefreshInfoButton.IsEnabled = true;
                RefreshInfoButton.Content = "Refresh Info";
            }
        }

        private async void DeviceStatusButton_Click(object sender, RoutedEventArgs e)
        {
            DeviceStatusButton.IsEnabled = false;
            DeviceStatusButton.Content = "Getting Status...";

            try
            {
                if (_displayController != null)
                {
                    var deviceStatus = await _displayController.GetDeviceStatus();
                    if (deviceStatus.HasValue)
                    {
                        UpdateStatusDisplay(deviceStatus.Value);
                        _ = ShowInfoMessageAsync("Device status retrieved successfully.");
                    }
                    else
                    {
                        StatusInfoText.Text = "Failed to retrieve device status.";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusInfoText.Text = $"Error retrieving status: {ex.Message}";
                _ = ShowErrorMessageAsync($"Failed to get device status: {ex.Message}");
            }
            finally
            {
                DeviceStatusButton.IsEnabled = true;
                DeviceStatusButton.Content = "Get Status";
            }
        }

        private async void OfflineModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isOfflineModeChanging) return;

            _isOfflineModeChanging = true;
            try
            {
                bool isOffline = OfflineModeToggle.IsOn;
                
                // TODO: Implement actual offline mode toggle
                Debug.WriteLine($"Offline mode {(isOffline ? "enabled" : "disabled")}");
                
                string message = isOffline ? 
                    "Device switched to offline mode. Network services are disabled." :
                    "Device switched to online mode. Network services are enabled.";
                    
                _ = ShowInfoMessageAsync(message);
            }
            catch (Exception ex)
            {
                _ = ShowErrorMessageAsync($"Failed to change offline mode: {ex.Message}");
                // Revert toggle state
                OfflineModeToggle.IsOn = !OfflineModeToggle.IsOn;
            }
            finally
            {
                _isOfflineModeChanging = false;
            }
        }

        private async void BrightnessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isBrightnessChanging || _displayController == null) return;

            try
            {
                int brightness = (int)e.NewValue;
                BrightnessValueText.Text = brightness.ToString();
                
                var response = await _displayController.SendCmdBrightnessWithResponse(brightness);
                
                if (response?.IsSuccess == true)
                {
                    Debug.WriteLine($"Brightness set to {brightness}%");
                }
                else
                {
                    Debug.WriteLine($"Failed to set brightness: {response?.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting brightness: {ex.Message}");
            }
        }

        private async void RotationChanged(object sender, RoutedEventArgs e)
        {
            if (_isRotationChanging || _displayController == null) return;

            try
            {
                int rotation = 0;
                if (Rotation90.IsChecked == true) rotation = 90;
                else if (Rotation180.IsChecked == true) rotation = 180;
                else if (Rotation270.IsChecked == true) rotation = 270;

                var response = await _displayController.SendCmdRotateWithResponse(rotation);
                
                if (response?.IsSuccess == true)
                {
                    Debug.WriteLine($"Rotation set to {rotation}°");
                }
                else
                {
                    Debug.WriteLine($"Failed to set rotation: {response?.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting rotation: {ex.Message}");
            }
        }

        // Helper Methods

        private async Task RestartDeviceAsync()
        {
            if (_displayController == null) return;

            RestartDeviceButton.IsEnabled = false;
            RestartDeviceButton.Content = "Restarting...";

            try
            {
                _displayController.Reboot();
                _ = ShowInfoMessageAsync("Device restart initiated. The device will reconnect shortly.");
                
                // Wait a bit for the device to restart
                await Task.Delay(3000);
            }
            catch (Exception ex)
            {
                _ = ShowErrorMessageAsync($"Failed to restart device: {ex.Message}");
            }
            finally
            {
                RestartDeviceButton.IsEnabled = true;
                RestartDeviceButton.Content = "Restart Device";
            }
        }

        private async Task PerformFactoryResetAsync()
        {
            if (_displayController == null) return;

            FactoryResetButton.IsEnabled = false;
            FactoryResetButton.Content = "Resetting...";

            try
            {
                _displayController.FactoryReset();
                _ = ShowInfoMessageAsync("Factory reset initiated. Device will restart with default settings.");
                
                // Wait a bit for the reset to complete
                await Task.Delay(3000);
                
                // Refresh device information after reset
                await LoadDeviceInformationAsync();
            }
            catch (Exception ex)
            {
                _ = ShowErrorMessageAsync($"Failed to perform factory reset: {ex.Message}");
            }
            finally
            {
                FactoryResetButton.IsEnabled = true;
                FactoryResetButton.Content = "Factory Reset";
            }
        }

        private async Task<bool> ShowConfirmationDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = "Yes",
                SecondaryButtonText = "No",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private async Task ShowInfoMessageAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Information",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            
            await dialog.ShowAsync();
        }

        private async Task ShowErrorMessageAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Error",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            
            await dialog.ShowAsync();
        }

        private async void CheckUpdateButton_Checked(object sender, RoutedEventArgs e)
        {
            var pb = sender as ProgressButton;
            if (pb.IsChecked.Value && !pb.IsIndeterminate)
            {
                pb.Progress = 0;
                while (true)
                {
                    pb.Progress += 1;
                    await Task.Delay(50);
                    if (pb.Progress == 100)
                    {
                        pb.IsChecked = false;
                        break;
                    }
                }
            }
        }
    }
}
