using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Linq;
using System.Threading.Tasks;
using HID.DisplayController;
using System.Diagnostics;
using CMDevicesManager.Services;

namespace CDMDevicesManagerDevWinUI.Views
{
    /// <summary>
    /// Device Settings page for managing individual device configurations
    /// </summary>
    public sealed partial class DeviceSettings : Page
    {
        private DeviceInfoViewModel? _deviceViewModel;
        private HidDeviceService? _hidService;
        private bool _isOfflineModeChanging = false;
        private bool _isBrightnessChanging = false;
        private bool _isRotationChanging = false;

        public DeviceInfoViewModel? DeviceViewModel => _deviceViewModel;

        public DeviceSettings()
        {
            this.InitializeComponent();
            // Initialize status display to default state
            ClearStatusDisplay();
        }

        // Constructor that accepts DeviceInfoViewModel for direct navigation
        public DeviceSettings(DeviceInfoViewModel deviceViewModel) : this()
        {
            _deviceViewModel = deviceViewModel;
            // Initialize after the component is loaded
            this.Loaded += (s, e) => 
            {
                UpdateDeviceInfoDisplay();
                InitializeHidService();
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
                InitializeHidService();
                _ = LoadDeviceInformationAsync();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CleanupHidService();
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

        private void InitializeHidService()
        {
            try
            {
                if (ServiceLocator.IsHidDeviceServiceInitialized)
                {
                    _hidService = ServiceLocator.HidDeviceService;
                    
                    // Set device path filter to target only the current device
                    if (_deviceViewModel?.DevicePath != null)
                    {
                        _hidService.SetDevicePathFilter(_deviceViewModel.DevicePath);
                        Debug.WriteLine($"Initialized HidDeviceService for device: {_deviceViewModel.ProductName}");
                    }
                }
                else
                {
                    Debug.WriteLine("HidDeviceService is not initialized");
                    _ = ShowErrorMessageAsync("Device service is not available. Please restart the application.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize HidDeviceService: {ex.Message}");
                _ = ShowErrorMessageAsync($"Failed to connect to device service: {ex.Message}");
            }
        }

        private void CleanupHidService()
        {
            try
            {
                // Clear device path filter when leaving the page
                _hidService?.ClearDevicePathFilter();
                _hidService = null;
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
            if (_hidService == null) return;

            try
            {
                // Get device firmware and hardware info using HidService
                var firmwareInfoResults = await _hidService.GetDeviceFirmwareInfoAsync();
                
                if (firmwareInfoResults.Any() && _deviceViewModel?.DevicePath != null)
                {
                    if (firmwareInfoResults.TryGetValue(_deviceViewModel.DevicePath, out var deviceInfo) && deviceInfo != null)
                    {
                        HardwareVersionText.Text = deviceInfo.HardwareVersion.ToString();
                        FirmwareVersionText.Text = deviceInfo.FirmwareVersion.ToString();
                    }
                    else
                    {
                        HardwareVersionText.Text = "N/A";
                        FirmwareVersionText.Text = "N/A";
                    }
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
            if (_hidService == null) return;

            try
            {
                // Get current device status using HidService
                var statusResults = await _hidService.GetDeviceStatusAsync();
                
                if (statusResults.Any() && _deviceViewModel?.DevicePath != null)
                {
                    if (statusResults.TryGetValue(_deviceViewModel.DevicePath, out var deviceStatus) && deviceStatus.HasValue)
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
            // Update brightness display
            StatusBrightnessProgressBar.Value = status.Brightness;
            StatusBrightnessText.Text = $"{status.Brightness}%";
            
            // Update rotation display
            StatusRotationText.Text = $"{status.Degree}°";
            // Rotate the icon to match the rotation
            var rotateTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = status.Degree };
            StatusRotationIcon.RenderTransform = rotateTransform;
            
            // Update sleep mode display
            StatusSleepIndicator.Fill = new SolidColorBrush(status.IsDisplayInSleep ? Colors.Orange : Colors.LimeGreen);
            StatusSleepText.Text = status.IsDisplayInSleep ? "Sleeping" : "Active";
            
            // Update OSD state display
            StatusOsdIndicator.Fill = new SolidColorBrush(status.IsOsdActive ? Colors.LimeGreen : Colors.Gray);
            StatusOsdText.Text = status.IsOsdActive ? "Active" : "Inactive";
            
            // Update keep alive timeout
            StatusKeepAliveText.Text = $"{status.KeepAliveTimeout}s";
            
            // Update suspend media status
            StatusMediaProgressBar.Maximum = status.MaxSuspendMediaCount;
            StatusMediaProgressBar.Value = status.ActiveSuspendMediaCount;
            StatusMediaText.Text = $"{status.ActiveSuspendMediaCount}/{status.MaxSuspendMediaCount}";
            
            // Update media slots detail
            UpdateMediaSlotsDisplay(status);
            
            // Update timestamp
            StatusTimestampText.Text = DateTime.Now.ToString("HH:mm:ss");
        }
        
        private void UpdateMediaSlotsDisplay(DeviceStatus status)
        {
            // Clear existing slot indicators
            MediaSlotsPanel.Children.Clear();
            
            // Create visual indicators for each media slot
            var mediaIndices = status.ActiveSuspendMediaIndices.ToArray();
            for (int i = 0; i < status.MaxSuspendMediaCount; i++)
            {
                var slotContainer = new StackPanel 
                { 
                    Orientation = Orientation.Vertical, 
                    Spacing = 2,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                
                // Slot indicator
                var indicator = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                
                // Check if this slot is active
                bool isActive = i < mediaIndices.Length && mediaIndices[i] == 1;
                indicator.Fill = new SolidColorBrush(isActive ? Colors.LimeGreen : Colors.Gray);
                
                // Slot number label
                var label = new TextBlock
                {
                    Text = (i + 1).ToString(),
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Colors.Gray)
                };
                
                slotContainer.Children.Add(indicator);
                slotContainer.Children.Add(label);
                MediaSlotsPanel.Children.Add(slotContainer);
            }
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
                RefreshInfoButton.Content = "Refresh Information";
            }
        }

        private async void DeviceStatusButton_Click(object sender, RoutedEventArgs e)
        {
            DeviceStatusButton.IsEnabled = false;
            DeviceStatusButton.Content = "Getting Status...";

            try
            {
                if (_hidService != null)
                {
                    var statusResults = await _hidService.GetDeviceStatusAsync();
                    
                    if (statusResults.Any() && _deviceViewModel?.DevicePath != null)
                    {
                        if (statusResults.TryGetValue(_deviceViewModel.DevicePath, out var deviceStatus) && deviceStatus.HasValue)
                        {
                            UpdateStatusDisplay(deviceStatus.Value);
                            _ = ShowInfoMessageAsync("Device status retrieved successfully.");
                        }
                        else
                        {
                            // Clear status display and show error state
                            ClearStatusDisplay();
                            _ = ShowErrorMessageAsync("Failed to retrieve device status - no response from device.");
                        }
                    }
                    else
                    {
                        // Clear status display and show error state
                        ClearStatusDisplay();
                        _ = ShowErrorMessageAsync("No devices found or device not responding.");
                    }
                }
                else
                {
                    // Clear status display and show disconnected state
                    ClearStatusDisplay();
                    _ = ShowErrorMessageAsync("Device service not available.");
                }
            }
            catch (Exception ex)
            {
                // Clear status display and show error
                ClearStatusDisplay();
                _ = ShowErrorMessageAsync($"Failed to get device status: {ex.Message}");
            }
            finally
            {
                DeviceStatusButton.IsEnabled = true;
                DeviceStatusButton.Content = "Get Device Status";
            }
        }
        
        private void ClearStatusDisplay()
        {
            // Reset all status displays to default/error state
            StatusBrightnessProgressBar.Value = 0;
            StatusBrightnessText.Text = "N/A";
            
            StatusRotationText.Text = "N/A";
            StatusRotationIcon.RenderTransform = null;
            
            StatusSleepIndicator.Fill = new SolidColorBrush(Colors.Gray);
            StatusSleepText.Text = "Unknown";
            
            StatusOsdIndicator.Fill = new SolidColorBrush(Colors.Gray);
            StatusOsdText.Text = "Unknown";
            
            StatusKeepAliveText.Text = "N/A";
            
            StatusMediaProgressBar.Value = 0;
            StatusMediaProgressBar.Maximum = 1;
            StatusMediaText.Text = "N/A";
            
            MediaSlotsPanel.Children.Clear();
            
            StatusTimestampText.Text = "Error";
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

        private async void OfflineModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isOfflineModeChanging || _hidService == null) return;

            _isOfflineModeChanging = true;
            try
            {
                bool isOffline = OfflineModeToggle.IsOn;
                
                // Offline mode = Suspend mode (false for real-time display)
                // Online mode = Real-time mode (true for real-time display)
                bool enableRealTimeDisplay = !isOffline;
                
                var results = await _hidService.SetRealTimeDisplayAsync(enableRealTimeDisplay);
                
                // Check if the operation was successful for our device
                bool success = false;
                if (_deviceViewModel?.DevicePath != null && results.TryGetValue(_deviceViewModel.DevicePath, out success) && success)
                {
                    Debug.WriteLine($"Real-time display {(enableRealTimeDisplay ? "enabled" : "disabled")} successfully");
                    
                    string message = isOffline ? 
                        "Device switched to suspend mode. Real-time display is disabled." :
                        "Device switched to real-time mode. Live display is enabled.";
                        
                    _ = ShowInfoMessageAsync(message);
                }
                else
                {
                    Debug.WriteLine($"Failed to set real-time display mode");
                    
                    // Revert toggle state on failure
                    OfflineModeToggle.IsOn = !OfflineModeToggle.IsOn;
                    
                    _ = ShowErrorMessageAsync("Failed to change display mode.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error changing real-time display mode: {ex.Message}");
                
                // Revert toggle state on exception
                OfflineModeToggle.IsOn = !OfflineModeToggle.IsOn;
                
                _ = ShowErrorMessageAsync($"Failed to change display mode: {ex.Message}");
            }
            finally
            {
                _isOfflineModeChanging = false;
            }
        }

        private async void BrightnessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isBrightnessChanging || _hidService == null) return;

            try
            {
                int brightness = (int)e.NewValue;
                BrightnessValueText.Text = brightness.ToString();
                
                var results = await _hidService.SetBrightnessAsync(brightness);
                
                // Check if the operation was successful for our device
                bool success = false;
                if (_deviceViewModel?.DevicePath != null && results.TryGetValue(_deviceViewModel.DevicePath, out success) && success)
                {
                    Debug.WriteLine($"Brightness set to {brightness}%");
                    
                    // Update the status display brightness immediately if visible
                    if (StatusBrightnessProgressBar != null)
                    {
                        StatusBrightnessProgressBar.Value = brightness;
                        StatusBrightnessText.Text = $"{brightness}%";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting brightness: {ex.Message}");
            }
        }

        private async void RotationChanged(object sender, RoutedEventArgs e)
        {
            if (_isRotationChanging || _hidService == null) return;

            try
            {
                int rotation = 0;
                if (Rotation90.IsChecked == true) rotation = 90;
                else if (Rotation180.IsChecked == true) rotation = 180;
                else if (Rotation270.IsChecked == true) rotation = 270;

                var results = await _hidService.SetRotationAsync(rotation);
                
                // Check if the operation was successful for our device
                bool success = false;
                if (_deviceViewModel?.DevicePath != null && results.TryGetValue(_deviceViewModel.DevicePath, out success) && success)
                {
                    Debug.WriteLine($"Rotation set to {rotation}°");
                    
                    // Update the status display rotation immediately if visible
                    if (StatusRotationText != null && StatusRotationIcon != null)
                    {
                        StatusRotationText.Text = $"{rotation}°";
                        var rotateTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = rotation };
                        StatusRotationIcon.RenderTransform = rotateTransform;
                    }
                }
                else
                {
                    Debug.WriteLine($"Failed to set rotation");
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
            if (_hidService == null) return;

            RestartDeviceButton.IsEnabled = false;
            RestartDeviceButton.Content = "Restarting...";

            try
            {
                var results = await _hidService.RebootDevicesAsync();
                
                // Check if the operation was successful for our device
                bool success = false;
                if (_deviceViewModel?.DevicePath != null && results.TryGetValue(_deviceViewModel.DevicePath, out success) && success)
                {
                    _ = ShowInfoMessageAsync("Device restart initiated. The device will reconnect shortly.");
                    
                    // Wait a bit for the device to restart
                    await Task.Delay(3000);
                }
                else
                {
                    _ = ShowErrorMessageAsync("Failed to restart device.");
                }
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
            if (_hidService == null) return;

            FactoryResetButton.IsEnabled = false;
            FactoryResetButton.Content = "Resetting...";

            try
            {
                var results = await _hidService.FactoryResetDevicesAsync();
                
                // Check if the operation was successful for our device
                bool success = false;
                if (_deviceViewModel?.DevicePath != null && results.TryGetValue(_deviceViewModel.DevicePath, out success) && success)
                {
                    _ = ShowInfoMessageAsync("Factory reset initiated. Device will restart with default settings.");
                    
                    // Wait a bit for the reset to complete
                    await Task.Delay(3000);
                    
                    // Refresh device information after reset
                    await LoadDeviceInformationAsync();
                }
                else
                {
                    _ = ShowErrorMessageAsync("Failed to perform factory reset.");
                }
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
    }
}
