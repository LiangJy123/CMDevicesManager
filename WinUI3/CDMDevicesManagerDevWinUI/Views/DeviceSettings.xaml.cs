using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HID.DisplayController;
using System.Diagnostics;
using CMDevicesManager.Services;
using CMDevicesManager.Models;
using Windows.Storage.Pickers;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

namespace CDMDevicesManagerDevWinUI.Views
{
    /// <summary>
    /// Represents a suspend media slot in the UI
    /// </summary>
    public class SuspendMediaSlotViewModel : INotifyPropertyChanged
    {
        private bool _hasMedia;
        private string _fileName = string.Empty;
        private string _localPath = string.Empty;
        private bool _isImage;
        private bool _isVideo;

        public int SlotIndex { get; set; }
        public int SlotNumber => SlotIndex + 1;

        public bool HasMedia
        {
            get => _hasMedia;
            set
            {
                _hasMedia = value;
                OnPropertyChanged();
            }
        }

        public string FileName
        {
            get => _fileName;
            set
            {
                _fileName = value;
                OnPropertyChanged();
            }
        }

        public string LocalPath
        {
            get => _localPath;
            set
            {
                _localPath = value;
                OnPropertyChanged();
            }
        }

        public bool IsImage
        {
            get => _isImage;
            set
            {
                _isImage = value;
                OnPropertyChanged();
            }
        }

        public bool IsVideo
        {
            get => _isVideo;
            set
            {
                _isVideo = value;
                OnPropertyChanged();
            }
        }

        public DeviceMediaFile? MediaFile { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Device Settings page for managing individual device configurations
    /// </summary>
    public sealed partial class DeviceSettings : Page
    {
        private DeviceInfoViewModel? _deviceViewModel;
        private HidDeviceService? _hidService;
        private OfflineMediaDataService? _offlineMediaService;
        private bool _isSleepModeChanging = false;
        private bool _isBrightnessChanging = false;
        private bool _isRotationChanging = false;
        private bool _isOfflineModeChanging = false;
        private int _maxSuspendMediaCount = 5; // Default value
        private ObservableCollection<SuspendMediaSlotViewModel> _mediaSlots = new();

        public DeviceInfoViewModel? DeviceViewModel => _deviceViewModel;

        public DeviceSettings()
        {
            this.InitializeComponent();
            // Initialize status display to default state
            ClearStatusDisplay();
            
            // Initialize media slots collection
            InitializeMediaSlots();
        }

        // Constructor that accepts DeviceInfoViewModel for direct navigation
        public DeviceSettings(DeviceInfoViewModel deviceViewModel) : this()
        {
            _deviceViewModel = deviceViewModel;
            // Initialize after the component is loaded
            this.Loaded += (s, e) => 
            {
                UpdateDeviceInfoDisplay();
                InitializeServices();
                _ = LoadDeviceInformationAsync();
                _ = LoadOfflineModeSettingsAsync();
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            if (e.Parameter is DeviceInfoViewModel deviceViewModel)
            {
                _deviceViewModel = deviceViewModel;
                UpdateDeviceInfoDisplay();
                InitializeServices();
                _ = LoadDeviceInformationAsync();
                _ = LoadOfflineModeSettingsAsync();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CleanupServices();
        }

        private void InitializeMediaSlots()
        {
            _mediaSlots.Clear();
            for (int i = 0; i < _maxSuspendMediaCount; i++)
            {
                _mediaSlots.Add(new SuspendMediaSlotViewModel
                {
                    SlotIndex = i,
                    HasMedia = false
                });
            }
            
            // Set the ItemsSource for the ItemsRepeater
            SuspendMediaSlotsRepeater.ItemsSource = _mediaSlots;
        }

        private void UpdateMediaSlots()
        {
            try
            {
                if (_offlineMediaService == null || _deviceViewModel?.SerialNumber == null)
                {
                    // Clear all slots
                    foreach (var slot in _mediaSlots)
                    {
                        slot.HasMedia = false;
                        slot.MediaFile = null;
                    }
                    return;
                }

                var suspendMediaFiles = _offlineMediaService.GetSuspendMediaFiles(_deviceViewModel.SerialNumber);
                
                // Update each slot
                for (int i = 0; i < _mediaSlots.Count; i++)
                {
                    var slot = _mediaSlots[i];
                    var mediaFile = suspendMediaFiles.FirstOrDefault(f => f.SlotIndex == i && f.IsActive);
                    
                    if (mediaFile != null)
                    {
                        slot.HasMedia = true;
                        slot.FileName = System.IO.Path.GetFileNameWithoutExtension(mediaFile.FileName);
                        slot.LocalPath = mediaFile.LocalPath;
                        slot.IsImage = mediaFile.IsImageFile;
                        slot.IsVideo = mediaFile.IsVideoFile;
                        slot.MediaFile = mediaFile;
                        
                        // Load image if it's an image file
                        if (mediaFile.IsImageFile && File.Exists(mediaFile.LocalPath))
                        {
                            LoadImageForSlot(slot, mediaFile.LocalPath);
                        }
                    }
                    else
                    {
                        slot.HasMedia = false;
                        slot.FileName = string.Empty;
                        slot.LocalPath = string.Empty;
                        slot.IsImage = false;
                        slot.IsVideo = false;
                        slot.MediaFile = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating media slots: {ex.Message}");
            }
        }

        private void LoadImageForSlot(SuspendMediaSlotViewModel slot, string imagePath)
        {
            // This will be handled by the Image control's Source binding in the template
            // We can add custom logic here if needed
        }

        private async void AddMediaToSlotButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int slotIndex)
            {
                await AddMediaToSpecificSlotAsync(slotIndex);
            }
        }

        private async void RemoveMediaButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int slotIndex)
            {
                await RemoveMediaFromSlotAsync(slotIndex);
            }
        }

        private async Task AddMediaToSpecificSlotAsync(int slotIndex)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.ViewMode = PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".mp4");

                // For WinUI 3, we need to set the window handle
                var window = App.MainWindow;
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    StorageFile fileToAdd = file;

                    // Check if the selected file is an image
                    string fileExtension = System.IO.Path.GetExtension(file.Name).ToLower();
                    bool isImageFile = fileExtension == ".jpg" || fileExtension == ".jpeg" || 
                                     fileExtension == ".png" || fileExtension == ".bmp";

                    if (isImageFile)
                    {
                        // Show image crop dialog for image files
                        var croppedFile = await ShowImageCropDialogAsync(file, slotIndex);
                        if (croppedFile != null)
                        {
                            fileToAdd = croppedFile;
                        }
                        else
                        {
                            // User cancelled cropping, don't proceed
                            return;
                        }
                    }

                    await AddMediaFileToSlotAsync(fileToAdd, slotIndex);
                    UpdateMediaSlots();
                    Debug.WriteLine($"Added media file to slot {slotIndex + 1}.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding media to slot {slotIndex}: {ex.Message}");
            }
        }

        private async Task RemoveMediaFromSlotAsync(int slotIndex)
        {
            try
            {
                if (_offlineMediaService == null || _deviceViewModel?.SerialNumber == null)
                {
                    return;
                }

                // Remove directly without confirmation dialog
                _offlineMediaService.RemoveSuspendMediaFile(_deviceViewModel.SerialNumber, slotIndex);
                UpdateMediaSlots();
                Debug.WriteLine($"Removed media file from slot {slotIndex + 1}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing media from slot {slotIndex}: {ex.Message}");
            }
        }

        private async Task AddMediaFileToSlotAsync(StorageFile file, int slotIndex)
        {
            try
            {
                if (_offlineMediaService == null || _deviceViewModel?.SerialNumber == null)
                {
                    throw new InvalidOperationException("Offline media service or device serial not available");
                }

                // Create the proper filename with the required naming convention
                var fileExtension = System.IO.Path.GetExtension(file.Name);
                var targetFileName = $"suspend_{slotIndex}{fileExtension}";
                
                // Add the media file to offline service with the specific slot
                _offlineMediaService.AddSuspendMediaFile(
                    _deviceViewModel.SerialNumber,
                    targetFileName, // Use the standardized filename
                    file.Path,
                    slotIndex
                );

                // Transfer file to device using suspend media functionality via HID service
                if (_hidService != null)
                {
                    // Generate a unique transfer ID based on slot index and timestamp
                    // This ensures each transfer has a unique ID (0-59 range)
                    byte transferId = (byte)slotIndex;

                    var transferResults = await _hidService.TransferFileAsync(file.Path, transferId);

                    // Check if transfer was successful for our device
                    bool transferSuccess = false;
                    if (_deviceViewModel?.DevicePath != null &&
                        transferResults.TryGetValue(_deviceViewModel.DevicePath, out transferSuccess) &&
                        transferSuccess)
                    {
                        Debug.WriteLine($"Successfully transferred file {targetFileName} to device slot {slotIndex} with transfer ID {transferId}");
                    }
                    else
                    {
                        Debug.WriteLine($"Failed to transfer file {targetFileName} to device slot {slotIndex}");
                    }
                }
                else
                {
                    Debug.WriteLine("HID service not available for file transfer");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding media file to slot {slotIndex}: {ex.Message}");
                throw;
            }
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

        private void InitializeServices()
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
                }

                if (ServiceLocator.IsOfflineMediaServiceInitialized)
                {
                    _offlineMediaService = ServiceLocator.OfflineMediaDataService;
                    Debug.WriteLine("Initialized OfflineMediaDataService");
                }
                else
                {
                    Debug.WriteLine("OfflineMediaDataService is not initialized");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize services: {ex.Message}");
            }
        }

        private void CleanupServices()
        {
            try
            {
                // Clear device path filter when leaving the page
                _hidService?.ClearDevicePathFilter();
                _hidService = null;
                _offlineMediaService = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        private async Task LoadOfflineModeSettingsAsync()
        {
            try
            {
                if (_offlineMediaService != null && _deviceViewModel?.SerialNumber != null)
                {
                    var deviceSettings = _offlineMediaService.GetDeviceSettings(_deviceViewModel.SerialNumber);
                    
                    _isOfflineModeChanging = true;
                    OfflineModeToggle.IsOn = deviceSettings.SuspendModeEnabled;
                    _isOfflineModeChanging = false;

                    // If offline mode is enabled, show the suspend media
                    if (deviceSettings.SuspendModeEnabled)
                    {
                        await ShowSuspendMediaSlotsAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load offline mode settings: {ex.Message}");
            }
        }

        private async Task ShowSuspendMediaSlotsAsync()
        {
            try
            {
                // Get current device status to determine max suspend media count
                if (_hidService != null && _deviceViewModel?.DevicePath != null)
                {
                    var statusResults = await _hidService.GetDeviceStatusAsync();
                    
                    if (statusResults.TryGetValue(_deviceViewModel.DevicePath, out var deviceStatus) && deviceStatus.HasValue)
                    {
                        var status = deviceStatus.Value;
                        _maxSuspendMediaCount = Math.Max(1, status.MaxSuspendMediaCount);
                        
                        // Update suspend media status in offline service
                        if (_offlineMediaService != null)
                        {
                            _offlineMediaService.UpdateSuspendMediaStatus(_deviceViewModel.SerialNumber!, status.SuspendMediaActive);
                        }
                    }
                }

                // Recreate media slots with the correct count
                InitializeMediaSlots();
                UpdateMediaSlots();
                
                // Show the suspend media card
                var suspendMediaCard = this.FindName("SuspendMediaCard") as CommunityToolkit.WinUI.Controls.SettingsCard;
                if (suspendMediaCard != null)
                {
                    suspendMediaCard.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing suspend media slots: {ex.Message}");
            }
        }

        private void HideSuspendMediaSlots()
        {
            try
            {
                var suspendMediaCard = this.FindName("SuspendMediaCard") as CommunityToolkit.WinUI.Controls.SettingsCard;
                if (suspendMediaCard != null)
                {
                    suspendMediaCard.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error hiding suspend media slots: {ex.Message}");
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
                    
                    Debug.WriteLine("Firmware update available! Click 'Install Update' to proceed.");
                }
                else
                {
                    UpdateStatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
                    UpdateStatusText.Text = "Up to date";
                    InstallUpdateButton.IsEnabled = false;
                    CheckUpdateButton.Content = "Check for Updates";
                    
                    Debug.WriteLine("Device firmware is up to date.");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                UpdateStatusText.Text = "Check failed";
                CheckUpdateButton.Content = "Retry";
                Debug.WriteLine($"Failed to check for updates: {ex.Message}");
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
                
                Debug.WriteLine("Firmware update completed successfully! Device will restart.");
                
                // Simulate device restart
                await RestartDeviceAsync();
            }
            catch (Exception ex)
            {
                UpdateStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                UpdateStatusText.Text = "Update failed";
                InstallUpdateButton.Content = "Retry";
                Debug.WriteLine($"Failed to install update: {ex.Message}");
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
                Debug.WriteLine("Device information refreshed successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to refresh device information: {ex.Message}");
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
                            Debug.WriteLine("Device status retrieved successfully.");
                        }
                        else
                        {
                            // Clear status display and show error state
                            ClearStatusDisplay();
                            Debug.WriteLine("Failed to retrieve device status - no response from device.");
                        }
                    }
                    else
                    {
                        // Clear status display and show error state
                        ClearStatusDisplay();
                        Debug.WriteLine("No devices found or device not responding.");
                    }
                }
                else
                {
                    // Clear status display and show disconnected state
                    ClearStatusDisplay();
                    Debug.WriteLine("Device service not available.");
                }
            }
            catch (Exception ex)
            {
                // Clear status display and show error
                ClearStatusDisplay();
                Debug.WriteLine($"Failed to get device status: {ex.Message}");
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

        private async void SleepModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isSleepModeChanging || _hidService == null) return;

            _isSleepModeChanging = true;
            try
            {
                bool isSleepMode = SleepModeToggle.IsOn;
                bool enableRealTimeDisplay = !isSleepMode;

                var results = await _hidService.SetDisplayInSleepAsync(enableRealTimeDisplay);

                // Check if the operation was successful for our device
                bool success = false;
                if (_deviceViewModel?.DevicePath != null && results.TryGetValue(_deviceViewModel.DevicePath, out success) && success)
                {
                    Debug.WriteLine($"Real-time display {(enableRealTimeDisplay ? "enabled" : "disabled")} successfully");

                    string message = isSleepMode ?
                        "Device switched to sleep mode. LCD will on when system in sleeps." :
                        "Device exit sleep mode. LCD will off when system in sleeps.";

                    Debug.WriteLine(message);
                }
                else
                {
                    Debug.WriteLine("Failed to set sleep mode");

                    // Revert toggle state on failure
                    SleepModeToggle.IsOn = !SleepModeToggle.IsOn;

                    Debug.WriteLine("Failed to change sleep mode.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error changing sleep mode: {ex.Message}");

                // Revert toggle state on exception
                SleepModeToggle.IsOn = !SleepModeToggle.IsOn;

                Debug.WriteLine($"Failed to change sleep mode: {ex.Message}");
            }
            finally
            {
                _isSleepModeChanging = false;
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
                    Debug.WriteLine("Device restart initiated. The device will reconnect shortly.");
                    
                    // Wait a bit for the device to restart
                    await Task.Delay(3000);
                }
                else
                {
                    Debug.WriteLine("Failed to restart device.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to restart device: {ex.Message}");
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
                    Debug.WriteLine("Factory reset initiated. Device will restart with default settings.");
                    
                    // Wait a bit for the reset to complete
                    await Task.Delay(3000);
                    
                    // Refresh device information after reset
                    await LoadDeviceInformationAsync();
                }
                else
                {
                    Debug.WriteLine("Failed to perform factory reset.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to perform factory reset: {ex.Message}");
            }
            finally
            {
                FactoryResetButton.IsEnabled = true;
                FactoryResetButton.Content = "Factory Reset";
            }
        }

        #region Offline Mode Implementation

        private async void OfflineModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isOfflineModeChanging) return;

            _isOfflineModeChanging = true;
            try
            {
                bool isOfflineMode = OfflineModeToggle.IsOn;

                if (isOfflineMode)
                {
                    // Enable offline mode and show suspend media slots
                    await ShowSuspendMediaSlotsAsync();
                }
                else
                {
                    // Disable offline mode and hide suspend media slots
                    HideSuspendMediaSlots();
                }

                // Update device setting using OfflineMediaDataService
                if (_offlineMediaService != null && _deviceViewModel?.SerialNumber != null)
                {
                    _offlineMediaService.UpdateDeviceSetting(_deviceViewModel.SerialNumber, "suspendModeEnabled", isOfflineMode);
                }

                // No dialog message - just update silently
                Debug.WriteLine($"Offline mode {(isOfflineMode ? "enabled" : "disabled")}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error changing offline mode: {ex.Message}");

                // Revert toggle state on exception
                OfflineModeToggle.IsOn = !OfflineModeToggle.IsOn;
            }
            finally
            {
                _isOfflineModeChanging = false;
            }
        }

        private async void AddMediaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.ViewMode = PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".mp4");

                // For WinUI 3, we need to set the window handle
                var window = App.MainWindow;
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

                var files = await picker.PickMultipleFilesAsync();
                if (files != null && files.Count > 0)
                {
                    await AddMultipleMediaFilesToSlotsAsync(files);
                    UpdateMediaSlots();
                    Debug.WriteLine($"Added {files.Count} media file(s) to available slots.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding media files: {ex.Message}");
            }
        }

        private async Task AddMultipleMediaFilesToSlotsAsync(IReadOnlyList<StorageFile> files)
        {
            try
            {
                if (_offlineMediaService == null || _deviceViewModel?.SerialNumber == null)
                {
                    throw new InvalidOperationException("Offline media service or device serial not available");
                }

                // Get available slots (empty slots)
                var availableSlots = new List<int>();
                for (int i = 0; i < _mediaSlots.Count; i++)
                {
                    if (!_mediaSlots[i].HasMedia)
                    {
                        availableSlots.Add(i);
                    }
                }

                int filesAdded = 0;
                for (int i = 0; i < files.Count && i < availableSlots.Count; i++)
                {
                    var file = files[i];
                    var slotIndex = availableSlots[i];
                    
                    await AddMediaFileToSlotAsync(file, slotIndex);
                    filesAdded++;
                }

                if (filesAdded < files.Count)
                {
                    Debug.WriteLine($"Added {filesAdded} files. {files.Count - filesAdded} files were skipped due to insufficient available slots.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding multiple media files to slots: {ex.Message}");
                throw;
            }
        }

        private string FormatFileSize(long bytes)
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

        /// <summary>
        /// Shows image crop dialog using the enhanced ImageCrop UserControl and returns cropped image file
        /// </summary>
        /// <param name="originalFile">Original image file to crop</param>
        /// <param name="slotIndex">Slot index for naming the temporary file</param>
        /// <returns>Cropped image file or null if cancelled</returns>
        private async Task<StorageFile?> ShowImageCropDialogAsync(StorageFile originalFile, int slotIndex)
        {
            try
            {
                // Create temporary cropped image file
                var tempFolder = ApplicationData.Current.TemporaryFolder;
                var croppedFileName = $"cropped_{slotIndex}_{System.IO.Path.GetFileNameWithoutExtension(originalFile.Name)}.png";
                var croppedImageFile = await tempFolder.CreateFileAsync(croppedFileName, CreationCollisionOption.ReplaceExisting);

                // Create the ContentDialog
                var cropDialog = new ContentDialog
                {
                    Title = "Crop Image for 480x480 Display",
                    PrimaryButtonText = "Use Cropped Image",
                    SecondaryButtonText = "Use Original", 
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                // Create the ImageCrop UserControl
                var imageCropControl = new CDMDevicesManagerDevWinUI.Controls.ImageCrop();
                
                // Set target size for the cropper
                imageCropControl.Width = 480;
                imageCropControl.Height = 520; // Extra height for buttons
                imageCropControl.AspectRatio = 1.0; // Square aspect ratio for 480x480
                
                // Create container with instructions
                var containerPanel = new StackPanel 
                { 
                    Spacing = 12 
                };
                
                // Add instruction text
                containerPanel.Children.Add(new TextBlock 
                { 
                    Text = "Adjust the crop area for your 480x480 display. Use the Save button to apply changes or Reset to start over:", 
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                });

                // Add file info
                containerPanel.Children.Add(new TextBlock 
                { 
                    Text = $"Original: {originalFile.Name}", 
                    FontSize = 12,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                });
                
                // Add the ImageCrop control
                containerPanel.Children.Add(imageCropControl);
                
                // Add button descriptions
                var buttonDescPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
                buttonDescPanel.Children.Add(new TextBlock 
                { 
                    Text = "• Use Cropped Image: Apply the current crop selection and resize to 480x480",
                    FontSize = 11,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
                });
                buttonDescPanel.Children.Add(new TextBlock 
                { 
                    Text = "• Use Original: Use the original image without cropping (resized to 480x480)",
                    FontSize = 11,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
                });
                buttonDescPanel.Children.Add(new TextBlock 
                { 
                    Text = "• Cancel: Don't add this image",
                    FontSize = 11,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
                });
                containerPanel.Children.Add(buttonDescPanel);
                
                // Set the content
                cropDialog.Content = containerPanel;

                // Initialize the ImageCrop control
                await imageCropControl.InitializeAsync(originalFile, croppedImageFile);

                bool imageSaved = false;

                // Handle the ImageSaved event
                imageCropControl.ImageSaved += (sender, args) =>
                {
                    imageSaved = true;
                    Debug.WriteLine($"Image saved to: {args.CroppedImagePath}");
                };

                // Show the dialog
                var result = await cropDialog.ShowAsync();
                
                if (result == ContentDialogResult.Primary)
                {
                    // User chose "Use Cropped Image"
                    if (imageSaved)
                    {
                        // The image was already saved, return the cropped file
                        return croppedImageFile;
                    }
                    else
                    {
                        // Save the current crop selection
                        bool saveSuccess = await imageCropControl.SaveCroppedImageAsync();
                        if (saveSuccess)
                        {
                            return croppedImageFile;
                        }
                        else
                        {
                            // Fallback to resizing original
                            return await ResizeImageToTargetSize(originalFile, slotIndex, 480, 480);
                        }
                    }
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    // User chose "Use Original" - just resize it
                    return await ResizeImageToTargetSize(originalFile, slotIndex, 480, 480);
                }
                
                return null; // User cancelled
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in image crop dialog: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resizes an image to the target size and saves it as a new file
        /// </summary>
        /// <param name="originalFile">Original image file</param>
        /// <param name="slotIndex">Slot index for naming</param>
        /// <param name="targetWidth">Target width</param>
        /// <param name="targetHeight">Target height</param>
        /// <returns>Resized image file</returns>
        private async Task<StorageFile> ResizeImageToTargetSize(StorageFile originalFile, int slotIndex, int targetWidth, int targetHeight)
        {
            try
            {
                var tempFolder = ApplicationData.Current.TemporaryFolder;
                var fileName = $"resized_{slotIndex}_{System.IO.Path.GetFileNameWithoutExtension(originalFile.Name)}.png";
                var tempFile = await tempFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                using (var originalStream = await originalFile.OpenReadAsync())
                using (var outputStream = await tempFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    // Decode original image
                    var decoder = await BitmapDecoder.CreateAsync(originalStream);
                    
                    // Create encoder for output
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream);
                    
                    // Get pixel data
                    var pixelData = await decoder.GetPixelDataAsync();
                    
                    // Set pixel data
                    encoder.SetPixelData(
                        decoder.BitmapPixelFormat,
                        decoder.BitmapAlphaMode,
                        decoder.PixelWidth,
                        decoder.PixelHeight,
                        decoder.DpiX,
                        decoder.DpiY,
                        pixelData.DetachPixelData()
                    );
                    
                    // Apply scaling transform
                    encoder.BitmapTransform.ScaledWidth = (uint)targetWidth;
                    encoder.BitmapTransform.ScaledHeight = (uint)targetHeight;
                    encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
                    
                    // Save the resized image
                    await encoder.FlushAsync();
                }

                return tempFile;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resizing image: {ex.Message}");
                throw;
            }
        }

        #endregion
    }
}
