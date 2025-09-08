using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using HID.DisplayController;
using System.Collections.Generic;

namespace CDMDevicesManagerDevWinUI.Views
{
    /// <summary>
    /// Media item model for the media list
    /// </summary>
    public class MediaItem : INotifyPropertyChanged
    {
        private string _fileName = string.Empty;
        private string _filePath = string.Empty;
        private string _fileSize = string.Empty;
        private string _resolution = string.Empty;
        private string _thumbnailPath = string.Empty;
        private string _statusText = "Ready";
        private Brush _statusColor = new SolidColorBrush(Colors.Gray);
        private string _typeIcon = "&#xE91B;";
        private Visibility _showIcon = Visibility.Visible;

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        public string FileSize
        {
            get => _fileSize;
            set { _fileSize = value; OnPropertyChanged(); }
        }

        public string Resolution
        {
            get => _resolution;
            set { _resolution = value; OnPropertyChanged(); }
        }

        public string ThumbnailPath
        {
            get => _thumbnailPath;
            set 
            { 
                _thumbnailPath = value; 
                OnPropertyChanged(); 
                ShowIcon = string.IsNullOrEmpty(value) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }

        public string TypeIcon
        {
            get => _typeIcon;
            set { _typeIcon = value; OnPropertyChanged(); }
        }

        public Visibility ShowIcon
        {
            get => _showIcon;
            set { _showIcon = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Device suspend media item model
    /// </summary>
    public class DeviceMediaItem : INotifyPropertyChanged
    {
        private string _mediaName = string.Empty;
        private string _slotIndex = string.Empty;
        private string _status = string.Empty;
        private Brush _activeColor = new SolidColorBrush(Colors.Gray);
        private bool _isActive;

        public string MediaName
        {
            get => _mediaName;
            set { _mediaName = value; OnPropertyChanged(); }
        }

        public string SlotIndex
        {
            get => _slotIndex;
            set { _slotIndex = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public Brush ActiveColor
        {
            get => _activeColor;
            set { _activeColor = value; OnPropertyChanged(); }
        }

        public bool IsActive
        {
            get => _isActive;
            set 
            { 
                _isActive = value; 
                OnPropertyChanged();
                ActiveColor = new SolidColorBrush(value ? Colors.LimeGreen : Colors.Gray);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Device Show page for media management and live preview
    /// </summary>
    public sealed partial class DeviceShow : Page, INotifyPropertyChanged
    {
        private DeviceInfoViewModel? _deviceViewModel;
        private DisplayController? _displayController;
        private bool _isModeChanging = false;
        private bool _isBrightnessChanging = false;
        private bool _isRotationChanging = false;
        private bool _isRealTimeMode = true;

        // Collections
        public ObservableCollection<MediaItem> MediaItems { get; } = new();
        public ObservableCollection<DeviceMediaItem> DeviceMediaItems { get; } = new();

        // Properties
        public DeviceInfoViewModel? DeviceViewModel => _deviceViewModel;

        public DeviceShow()
        {
            this.InitializeComponent();
            this.DataContext = this;
            
            // Initialize UI state after controls are loaded
            this.Loaded += DeviceShow_Loaded;
        }

        // Constructor that accepts DeviceInfoViewModel for direct navigation
        public DeviceShow(DeviceInfoViewModel deviceViewModel) : this()
        {
            _deviceViewModel = deviceViewModel;
        }

        private async void DeviceShow_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize UI state
            InitializeUI();
            
            // Update device info if we have a device
            UpdateDeviceInfoDisplay();
            
            // Initialize device controller if we have a device
            if (_deviceViewModel != null)
            {
                InitializeDeviceController();
            }
            
            // Load sample media for demonstration
            await LoadSampleMediaAsync();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            if (e.Parameter is DeviceInfoViewModel deviceViewModel)
            {
                _deviceViewModel = deviceViewModel;
                UpdateDeviceInfoDisplay();
                InitializeDeviceController();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CleanupDeviceController();
        }

        private void InitializeUI()
        {
            // Set initial mode
            if (ModeToggle != null)
            {
                ModeToggle.IsOn = _isRealTimeMode;
            }
            UpdateModeUI();
            
            // Initialize device connection state
            UpdateConnectionState(false);
            
            // Bind media lists
            if (MediaListView != null)
            {
                MediaListView.ItemsSource = MediaItems;
            }
            if (DeviceMediaListView != null)
            {
                DeviceMediaListView.ItemsSource = DeviceMediaItems;
            }
        }

        private void UpdateDeviceInfoDisplay()
        {
            if (DeviceNameText != null)
            {
                if (_deviceViewModel != null)
                {
                    DeviceNameText.Text = $"{_deviceViewModel.ProductName} ({_deviceViewModel.SerialNumber})";
                    if (ConnectionStatusText != null)
                    {
                        ConnectionStatusText.Text = "Ready to connect";
                    }
                }
                else
                {
                    DeviceNameText.Text = "No Device Selected";
                    if (ConnectionStatusText != null)
                    {
                        ConnectionStatusText.Text = "No device";
                    }
                }
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
                    
                    // Update connection state
                    UpdateConnectionState(true);
                    
                    // Load current device settings and media
                    _ = LoadDeviceSettingsAsync();
                    _ = LoadDeviceMediaAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to initialize DisplayController: {ex.Message}");
                    _ = ShowErrorMessageAsync($"Failed to connect to device: {ex.Message}");
                    UpdateConnectionState(false);
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
                UpdateConnectionState(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        private async Task LoadDeviceSettingsAsync()
        {
            if (_displayController == null) return;

            try
            {
                var deviceStatus = await _displayController.GetDeviceStatus();
                if (deviceStatus.HasValue)
                {
                    var status = deviceStatus.Value;
                    
                    // Update brightness slider
                    if (BrightnessSlider != null && BrightnessValueText != null)
                    {
                        _isBrightnessChanging = true;
                        BrightnessSlider.Value = status.Brightness;
                        BrightnessValueText.Text = $"{status.Brightness}%";
                        _isBrightnessChanging = false;
                    }

                    // Update rotation selection
                    _isRotationChanging = true;
                    UpdateRotationSelection(status.Degree);
                    _isRotationChanging = false;

                    // Update status display
                    UpdateDeviceStatus(status);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load device settings: {ex.Message}");
            }
        }

        private async Task LoadDeviceMediaAsync()
        {
            if (_displayController == null) 
            {
                UpdateDeviceMediaUI(false, 0);
                return;
            }

            try
            {
                var deviceStatus = await _displayController.GetDeviceStatus();
                if (deviceStatus.HasValue)
                {
                    var status = deviceStatus.Value;
                    
                    // Clear existing items
                    DeviceMediaItems.Clear();
                    
                    // Process suspend media active indices
                    var activeIndices = status.ActiveSuspendMediaIndices.ToArray();
                    var mediaCount = status.ActiveSuspendMediaCount;
                    var maxCount = status.MaxSuspendMediaCount;
                    
                    // Create device media items based on suspend media info
                    for (int i = 0; i < maxCount; i++)
                    {
                        bool isActive = i < activeIndices.Length && activeIndices[i] == 1;
                        
                        if (isActive || mediaCount > 0) // Show slots with media or if there's any media
                        {
                            var deviceMediaItem = new DeviceMediaItem
                            {
                                MediaName = isActive ? $"Suspend Media {i + 1}" : $"Empty Slot {i + 1}",
                                SlotIndex = $"Slot {i + 1}",
                                Status = isActive ? "Active" : "Empty",
                                IsActive = isActive
                            };
                            
                            DeviceMediaItems.Add(deviceMediaItem);
                        }
                    }
                    
                    UpdateDeviceMediaUI(true, mediaCount);
                    Debug.WriteLine($"Loaded device media: {mediaCount} active files out of {maxCount} slots");
                }
                else
                {
                    UpdateDeviceMediaUI(true, 0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load device media: {ex.Message}");
                UpdateDeviceMediaUI(true, 0);
            }
        }

        private void UpdateDeviceMediaUI(bool isConnected, int mediaCount)
        {
            if (DeviceMediaCountText != null)
            {
                DeviceMediaCountText.Text = $"{mediaCount} files";
            }

            if (DeviceDisconnectedPanel != null)
            {
                DeviceDisconnectedPanel.Visibility = isConnected ? Visibility.Collapsed : Visibility.Visible;
            }

            if (NoDeviceMediaPanel != null)
            {
                NoDeviceMediaPanel.Visibility = (isConnected && mediaCount == 0) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (DeviceMediaListView != null)
            {
                DeviceMediaListView.Visibility = (isConnected && mediaCount > 0) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateRotationSelection(int degree)
        {
            if (Rotation0 != null) Rotation0.IsChecked = degree == 0;
            if (Rotation90 != null) Rotation90.IsChecked = degree == 90;
            if (Rotation180 != null) Rotation180.IsChecked = degree == 180;
            if (Rotation270 != null) Rotation270.IsChecked = degree == 270;
        }

        private void UpdateDeviceStatus(DeviceStatus status)
        {
            if (StatusIndicator != null)
            {
                StatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
            }
            if (StatusText != null)
            {
                StatusText.Text = "Active";
            }
            
            // Update device status indicator in header
            if (DeviceStatusIndicator != null)
            {
                DeviceStatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
            }
            if (DeviceStatusText != null)
            {
                DeviceStatusText.Text = "Connected";
            }
            
            // Update current media info
            if (CurrentMediaText != null)
            {
                if (status.HasActiveSuspendMedia)
                {
                    CurrentMediaText.Text = $"{status.ActiveSuspendMediaCount} files";
                }
                else
                {
                    CurrentMediaText.Text = "None";
                }
            }
        }

        private void UpdateConnectionState(bool isConnected)
        {
            if (isConnected)
            {
                if (DeviceStatusIndicator != null)
                {
                    DeviceStatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
                }
                if (DeviceStatusText != null)
                {
                    DeviceStatusText.Text = "Connected";
                }
                if (ConnectionStatusText != null)
                {
                    ConnectionStatusText.Text = "Connected";
                }
                if (ConnectButton != null && ConnectButtonText != null && ConnectButtonIcon != null)
                {
                    ConnectButtonText.Text = "Disconnect";
                    ConnectButtonIcon.Glyph = "&#xE7BA;"; // Disconnect icon
                    ConnectButton.IsEnabled = true;
                }
                
                // Enable device controls
                if (BrightnessSlider != null) BrightnessSlider.IsEnabled = true;
                if (Rotation0 != null) Rotation0.IsEnabled = true;
                if (Rotation90 != null) Rotation90.IsEnabled = true;
                if (Rotation180 != null) Rotation180.IsEnabled = true;
                if (Rotation270 != null) Rotation270.IsEnabled = true;
                if (LiveDisplayToggle != null) LiveDisplayToggle.IsEnabled = true;
                if (SendToDeviceButton != null && MediaListView != null)
                {
                    SendToDeviceButton.IsEnabled = MediaListView.SelectedItem != null;
                }
            }
            else
            {
                if (DeviceStatusIndicator != null)
                {
                    DeviceStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                }
                if (DeviceStatusText != null)
                {
                    DeviceStatusText.Text = "Disconnected";
                }
                if (ConnectionStatusText != null)
                {
                    ConnectionStatusText.Text = "Disconnected";
                }
                if (ConnectButton != null && ConnectButtonText != null && ConnectButtonIcon != null)
                {
                    ConnectButtonText.Text = "Connect";
                    ConnectButtonIcon.Glyph = "&#xE8C8;"; // Connect icon
                    ConnectButton.IsEnabled = _deviceViewModel != null;
                }
                
                // Disable device controls
                if (BrightnessSlider != null) BrightnessSlider.IsEnabled = false;
                if (Rotation0 != null) Rotation0.IsEnabled = false;
                if (Rotation90 != null) Rotation90.IsEnabled = false;
                if (Rotation180 != null) Rotation180.IsEnabled = false;
                if (Rotation270 != null) Rotation270.IsEnabled = false;
                if (LiveDisplayToggle != null) LiveDisplayToggle.IsEnabled = false;
                if (SendToDeviceButton != null) SendToDeviceButton.IsEnabled = false;
                
                // Update status
                if (StatusIndicator != null)
                {
                    StatusIndicator.Fill = new SolidColorBrush(Colors.Gray);
                }
                if (StatusText != null)
                {
                    StatusText.Text = "Disconnected";
                }
                if (CurrentMediaText != null)
                {
                    CurrentMediaText.Text = "Unknown";
                }

                // Update device media UI
                UpdateDeviceMediaUI(false, 0);
            }
        }

        private void UpdateModeUI()
        {
            if (_isRealTimeMode)
            {
                // Real-time mode - Show preview, hide device media
                if (PreviewModeIcon != null)
                {
                    PreviewModeIcon.Glyph = "&#xE8B3;"; // Live icon
                }
                if (PreviewModeText != null)
                {
                    PreviewModeText.Text = "Real-time Preview";
                }
                if (ModeControlsExpander != null)
                {
                    ModeControlsExpander.Header = "Real-time Mode";
                    ModeControlsExpander.Description = "Live display controls";
                }
                if (ModeControlsIcon != null)
                {
                    ModeControlsIcon.Glyph = "&#xE8B3;";
                }
                
                if (RealTimeModeCard != null) RealTimeModeCard.Visibility = Visibility.Visible;
                if (SuspendModeCard != null) SuspendModeCard.Visibility = Visibility.Collapsed;

                // Show real-time preview panel, hide suspend mode panel
                if (RealTimePreviewPanel != null) RealTimePreviewPanel.Visibility = Visibility.Visible;
                if (SuspendModePanel != null) SuspendModePanel.Visibility = Visibility.Collapsed;
                if (PreviewInfoPanel != null) PreviewInfoPanel.Visibility = Visibility.Visible;
            }
            else
            {
                // Suspend mode - Hide preview, show device media
                if (PreviewModeIcon != null)
                {
                    PreviewModeIcon.Glyph = "&#xE7C4;"; // Pause icon
                }
                if (PreviewModeText != null)
                {
                    PreviewModeText.Text = "Suspend Mode";
                }
                if (ModeControlsExpander != null)
                {
                    ModeControlsExpander.Header = "Suspend Mode";
                    ModeControlsExpander.Description = "Suspend media controls";
                }
                if (ModeControlsIcon != null)
                {
                    ModeControlsIcon.Glyph = "&#xE7C4;";
                }
                
                if (RealTimeModeCard != null) RealTimeModeCard.Visibility = Visibility.Collapsed;
                if (SuspendModeCard != null) SuspendModeCard.Visibility = Visibility.Visible;

                // Hide real-time preview panel, show suspend mode panel
                if (RealTimePreviewPanel != null) RealTimePreviewPanel.Visibility = Visibility.Collapsed;
                if (SuspendModePanel != null) SuspendModePanel.Visibility = Visibility.Visible;
                if (PreviewInfoPanel != null) PreviewInfoPanel.Visibility = Visibility.Collapsed;

                // Load device media when switching to suspend mode
                _ = LoadDeviceMediaAsync();
            }
        }

        private async Task LoadSampleMediaAsync()
        {
            // Add some sample media items for demonstration
            var sampleItems = new[]
            {
                new MediaItem 
                { 
                    FileName = "sample_image.jpg", 
                    FileSize = "2.3 MB", 
                    Resolution = "1920x1080",
                    TypeIcon = "&#xE91B;",
                    StatusText = "Ready",
                    StatusColor = new SolidColorBrush(Colors.LimeGreen)
                },
                new MediaItem 
                { 
                    FileName = "demo_video.mp4", 
                    FileSize = "15.7 MB", 
                    Resolution = "1280x720",
                    TypeIcon = "&#xE714;",
                    StatusText = "Ready",
                    StatusColor = new SolidColorBrush(Colors.LimeGreen)
                },
                new MediaItem 
                { 
                    FileName = "background.png", 
                    FileSize = "5.1 MB", 
                    Resolution = "2560x1440",
                    TypeIcon = "&#xE91B;",
                    StatusText = "On Device",
                    StatusColor = new SolidColorBrush(Colors.Orange)
                }
            };

            foreach (var item in sampleItems)
            {
                MediaItems.Add(item);
            }
        }

        // Event Handlers

        private void ModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isModeChanging || ModeToggle == null) return;

            _isRealTimeMode = ModeToggle.IsOn;
            UpdateModeUI();
            
            // Apply mode change to device
            _ = ApplyModeChangeAsync();
        }

        private async Task ApplyModeChangeAsync()
        {
            if (_displayController == null) return;

            _isModeChanging = true;
            try
            {
                var response = await _displayController.SendCmdRealTimeDisplayWithResponse(_isRealTimeMode);
                
                if (response?.IsSuccess == true)
                {
                    string message = _isRealTimeMode ? 
                        "Device switched to real-time mode." :
                        "Device switched to suspend mode.";
                        
                    Debug.WriteLine(message);
                }
                else
                {
                    // Revert toggle on failure
                    if (ModeToggle != null)
                    {
                        ModeToggle.IsOn = !_isRealTimeMode;
                        _isRealTimeMode = !_isRealTimeMode;
                        UpdateModeUI();
                    }
                    
                    _ = ShowErrorMessageAsync("Failed to change device mode.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error changing device mode: {ex.Message}");
                
                // Revert toggle on error
                if (ModeToggle != null)
                {
                    ModeToggle.IsOn = !_isRealTimeMode;
                    _isRealTimeMode = !_isRealTimeMode;
                    UpdateModeUI();
                }
                
                _ = ShowErrorMessageAsync($"Failed to change device mode: {ex.Message}");
            }
            finally
            {
                _isModeChanging = false;
            }
        }

        private async void RefreshDeviceMediaButton_Click(object sender, RoutedEventArgs e)
        {
            if (RefreshDeviceMediaButton != null)
            {
                RefreshDeviceMediaButton.IsEnabled = false;
            }

            try
            {
                await LoadDeviceMediaAsync();
                _ = ShowInfoMessageAsync("Device media refreshed successfully.");
            }
            catch (Exception ex)
            {
                _ = ShowErrorMessageAsync($"Failed to refresh device media: {ex.Message}");
            }
            finally
            {
                if (RefreshDeviceMediaButton != null)
                {
                    RefreshDeviceMediaButton.IsEnabled = true;
                }
            }
        }

        private async void AddMediaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                
                // Get the current window's HWND
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
                
                picker.ViewMode = PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".mp4");
                picker.FileTypeFilter.Add(".avi");
                picker.FileTypeFilter.Add(".mov");

                var files = await picker.PickMultipleFilesAsync();
                if (files != null && files.Count > 0)
                {
                    foreach (var file in files)
                    {
                        await AddMediaFileAsync(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding media files: {ex.Message}");
                _ = ShowErrorMessageAsync($"Failed to add media files: {ex.Message}");
            }
        }

        private async Task AddMediaFileAsync(StorageFile file)
        {
            try
            {
                var properties = await file.GetBasicPropertiesAsync();
                var fileSize = FormatFileSize((ulong)properties.Size);
                
                // Determine file type icon
                string typeIcon = "&#xE91B;"; // Default image icon
                if (file.FileType.ToLower().Contains("mp4") || 
                    file.FileType.ToLower().Contains("avi") || 
                    file.FileType.ToLower().Contains("mov"))
                {
                    typeIcon = "&#xE714;"; // Video icon
                }

                var mediaItem = new MediaItem
                {
                    FileName = file.Name,
                    FilePath = file.Path,
                    FileSize = fileSize,
                    Resolution = "Unknown",
                    TypeIcon = typeIcon,
                    StatusText = "Ready",
                    StatusColor = new SolidColorBrush(Colors.LimeGreen)
                };

                MediaItems.Add(mediaItem);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing media file {file.Name}: {ex.Message}");
            }
        }

        private static string FormatFileSize(ulong bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        private void RefreshMediaButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowInfoMessageAsync("Media list refreshed.");
        }

        private void MediaListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MediaListView == null) return;

            var selectedItem = MediaListView.SelectedItem as MediaItem;
            
            if (selectedItem != null)
            {
                // Update preview only in real-time mode
                if (_isRealTimeMode)
                {
                    _ = LoadPreviewAsync(selectedItem);
                }
                
                // Enable/disable buttons
                if (SendToDeviceButton != null)
                {
                    SendToDeviceButton.IsEnabled = _displayController != null;
                }
                if (RemoveMediaButton != null)
                {
                    RemoveMediaButton.IsEnabled = true;
                }
                
                // Update preview info only in real-time mode
                if (_isRealTimeMode)
                {
                    if (PreviewInfoText != null)
                    {
                        PreviewInfoText.Text = selectedItem.FileName;
                    }
                    if (PreviewDetailsText != null)
                    {
                        PreviewDetailsText.Text = $"{selectedItem.FileSize} • {selectedItem.Resolution}";
                    }
                }
            }
            else
            {
                // Clear preview
                if (_isRealTimeMode)
                {
                    ClearPreview();
                }
                
                // Disable buttons
                if (SendToDeviceButton != null)
                {
                    SendToDeviceButton.IsEnabled = false;
                }
                if (RemoveMediaButton != null)
                {
                    RemoveMediaButton.IsEnabled = false;
                }
                
                // Clear preview info
                if (_isRealTimeMode)
                {
                    if (PreviewInfoText != null)
                    {
                        PreviewInfoText.Text = "No file selected";
                    }
                    if (PreviewDetailsText != null)
                    {
                        PreviewDetailsText.Text = "";
                    }
                }
            }
        }

        private async Task LoadPreviewAsync(MediaItem mediaItem)
        {
            if (!_isRealTimeMode) return; // Only load preview in real-time mode

            try
            {
                ShowLoading("Loading preview...");
                
                if (NoContentPanel != null)
                {
                    NoContentPanel.Visibility = Visibility.Collapsed;
                }
                
                if (File.Exists(mediaItem.FilePath) && PreviewImage != null)
                {
                    var bitmap = new BitmapImage();
                    using var stream = File.OpenRead(mediaItem.FilePath);
                    await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                    
                    PreviewImage.Source = bitmap;
                    
                    if (mediaItem.Resolution == "Unknown")
                    {
                        mediaItem.Resolution = $"{bitmap.PixelWidth}x{bitmap.PixelHeight}";
                        if (PreviewDetailsText != null)
                        {
                            PreviewDetailsText.Text = $"{mediaItem.FileSize} • {mediaItem.Resolution}";
                        }
                    }
                }
                
                HideLoading();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading preview: {ex.Message}");
                HideLoading();
                ShowNoContent("Failed to load preview");
            }
        }

        private void ClearPreview()
        {
            if (PreviewImage != null)
            {
                PreviewImage.Source = null;
            }
            ShowNoContent();
        }

        private void ShowLoading(string message = "Loading...")
        {
            if (LoadingText != null)
            {
                LoadingText.Text = message;
            }
            if (LoadingPanel != null)
            {
                LoadingPanel.Visibility = Visibility.Visible;
            }
            if (NoContentPanel != null)
            {
                NoContentPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void HideLoading()
        {
            if (LoadingPanel != null)
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowNoContent(string message = "No media selected")
        {
            if (NoContentPanel != null)
            {
                NoContentPanel.Visibility = Visibility.Visible;
                
                var textBlocks = NoContentPanel.Children.OfType<TextBlock>().ToArray();
                if (textBlocks.Length > 0)
                {
                    textBlocks[0].Text = message;
                }
            }
            if (LoadingPanel != null)
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void SendToDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (MediaListView == null) return;

            var selectedItem = MediaListView.SelectedItem as MediaItem;
            if (selectedItem == null || _displayController == null) return;

            try
            {
                if (SendToDeviceButton != null)
                {
                    SendToDeviceButton.IsEnabled = false;
                }
                
                selectedItem.StatusText = "Sending...";
                selectedItem.StatusColor = new SolidColorBrush(Colors.Orange);

                bool success = false;
                
                if (_isRealTimeMode)
                {
                    success = await _displayController.SetBackgroundWithFileAndResponse(selectedItem.FilePath);
                }
                else
                {
                    success = await _displayController.SendSuspendFileWithResponse(selectedItem.FilePath);
                }

                if (success)
                {
                    selectedItem.StatusText = "On Device";
                    selectedItem.StatusColor = new SolidColorBrush(Colors.LimeGreen);
                    _ = ShowInfoMessageAsync($"Successfully sent {selectedItem.FileName} to device.");
                    
                    // Refresh device media if in suspend mode
                    if (!_isRealTimeMode)
                    {
                        _ = LoadDeviceMediaAsync();
                    }
                }
                else
                {
                    selectedItem.StatusText = "Failed";
                    selectedItem.StatusColor = new SolidColorBrush(Colors.Red);
                    _ = ShowErrorMessageAsync($"Failed to send {selectedItem.FileName} to device.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending media to device: {ex.Message}");
                if (selectedItem != null)
                {
                    selectedItem.StatusText = "Error";
                    selectedItem.StatusColor = new SolidColorBrush(Colors.Red);
                }
                _ = ShowErrorMessageAsync($"Error sending media to device: {ex.Message}");
            }
            finally
            {
                if (SendToDeviceButton != null)
                {
                    SendToDeviceButton.IsEnabled = true;
                }
            }
        }

        private void RemoveMediaButton_Click(object sender, RoutedEventArgs e)
        {
            if (MediaListView != null)
            {
                var selectedItem = MediaListView.SelectedItem as MediaItem;
                if (selectedItem != null)
                {
                    MediaItems.Remove(selectedItem);
                    if (_isRealTimeMode)
                    {
                        ClearPreview();
                    }
                }
            }
        }

        private async void BrightnessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isBrightnessChanging || _displayController == null) return;

            try
            {
                int brightness = (int)e.NewValue;
                if (BrightnessValueText != null)
                {
                    BrightnessValueText.Text = $"{brightness}%";
                }
                
                var response = await _displayController.SendCmdBrightnessWithResponse(brightness);
                
                if (response?.IsSuccess == true)
                {
                    Debug.WriteLine($"Brightness set to {brightness}%");
                }
                else
                {
                    Debug.WriteLine($"Failed to set brightness");
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
                if (Rotation90?.IsChecked == true) rotation = 90;
                else if (Rotation180?.IsChecked == true) rotation = 180;
                else if (Rotation270?.IsChecked == true) rotation = 270;

                var response = await _displayController.SendCmdRotateWithResponse(rotation);
                
                if (response?.IsSuccess == true)
                {
                    Debug.WriteLine($"Rotation set to {rotation}°");
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

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_displayController != null)
            {
                // Disconnect
                CleanupDeviceController();
            }
            else
            {
                // Connect
                InitializeDeviceController();
            }
        }

        private async void LiveDisplayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_displayController == null || LiveDisplayToggle == null) return;

            try
            {
                bool enableLive = LiveDisplayToggle.IsOn;
                var response = await _displayController.SendCmdRealTimeDisplayWithResponse(enableLive);
                
                if (response?.IsSuccess == true)
                {
                    Debug.WriteLine($"Live display {(enableLive ? "enabled" : "disabled")}");
                }
                else
                {
                    LiveDisplayToggle.IsOn = !enableLive;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error toggling live display: {ex.Message}");
                if (LiveDisplayToggle != null)
                {
                    LiveDisplayToggle.IsOn = !LiveDisplayToggle.IsOn;
                }
            }
        }

        private async void ClearSuspendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_displayController == null) return;

            try
            {
                var result = await ShowConfirmationDialogAsync("Clear Suspend Media", 
                    "Are you sure you want to clear all suspend media from the device?");
                    
                if (result)
                {
                    var success = await _displayController.ClearSuspendModeWithResponse();
                    if (success)
                    {
                        _ = ShowInfoMessageAsync("All suspend media cleared from device.");
                        
                        foreach (var item in MediaItems.Where(i => i.StatusText == "On Device"))
                        {
                            item.StatusText = "Ready";
                            item.StatusColor = new SolidColorBrush(Colors.LimeGreen);
                        }
                        
                        // Refresh device media
                        _ = LoadDeviceMediaAsync();
                    }
                    else
                    {
                        _ = ShowErrorMessageAsync("Failed to clear suspend media from device.");
                    }
                }
            }
            catch (Exception ex)
            {
                _ = ShowErrorMessageAsync($"Error clearing suspend media: {ex.Message}");
            }
        }

        private async void RefreshSuspendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_displayController == null) return;

            try
            {
                await LoadDeviceMediaAsync();
                _ = ShowInfoMessageAsync("Suspend media refreshed.");
            }
            catch (Exception ex)
            {
                _ = ShowErrorMessageAsync($"Failed to refresh suspend media: {ex.Message}");
            }
        }

        private void FitToScreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (PreviewImage?.Source != null)
            {
                PreviewImage.Stretch = Stretch.Uniform;
            }
        }

        private void ActualSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (PreviewImage?.Source != null)
            {
                PreviewImage.Stretch = Stretch.None;
            }
        }

        private async void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
        {
            if (_displayController == null) return;

            try
            {
                if (RefreshStatusButton != null)
                {
                    RefreshStatusButton.IsEnabled = false;
                }

                var deviceStatus = await _displayController.GetDeviceStatus();
                if (deviceStatus.HasValue)
                {
                    UpdateDeviceStatus(deviceStatus.Value);
                    _ = ShowInfoMessageAsync("Device status refreshed successfully.");
                }
                else
                {
                    _ = ShowErrorMessageAsync("Failed to get device status.");
                }
            }
            catch (Exception ex)
            {
                _ = ShowErrorMessageAsync($"Failed to refresh device status: {ex.Message}");
            }
            finally
            {
                if (RefreshStatusButton != null)
                {
                    RefreshStatusButton.IsEnabled = true;
                }
            }
        }

        private void DeviceSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_deviceViewModel != null)
            {
                Frame.Navigate(typeof(DeviceSettings), _deviceViewModel);
            }
        }

        // Helper Methods

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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
