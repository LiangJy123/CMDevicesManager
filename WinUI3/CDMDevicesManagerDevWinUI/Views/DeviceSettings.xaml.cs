using CDMDevicesManagerDevWinUI.Helpers;
using CMDevicesManager.Models;
using CMDevicesManager.Services;
using CommunityToolkit.WinUI.Controls;
using HID.DisplayController;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Path = System.IO.Path;

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
        
        // Video thumbnail cache to avoid regenerating thumbnails
        private Dictionary<string, BitmapImage> _videoThumbnailCache = new();
        private bool _isLoading = false;

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
        }

        private void UpdateMediaSlots()
        {
            try
            {
                if (_offlineMediaService == null || _deviceViewModel?.SerialNumber == null)
                {
                    // Clear all slots
                    for (int i = 0; i < _maxSuspendMediaCount; i++)
                    {
                        UpdateMediaSlotUI(i, null, false);
                    }
                    return;
                }

                var suspendMediaFiles = _offlineMediaService.GetSuspendMediaFiles(_deviceViewModel.SerialNumber);
                
                // Update each slot
                for (int i = 0; i < _maxSuspendMediaCount; i++)
                {
                    var mediaFile = suspendMediaFiles.FirstOrDefault(f => f.SlotIndex == i && f.IsActive);
                    
                    if (mediaFile != null)
                    {
                        UpdateMediaSlotUI(i, mediaFile.LocalPath, true);
                    }
                    else
                    {
                        UpdateMediaSlotUI(i, null, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating media slots: {ex.Message}");
            }
        }

        /// <summary>
        /// Update media slot UI to show media or empty state
        /// </summary>
        private async void UpdateMediaSlotUI(int slotIndex, string? filePath = null, bool hasMedia = false)
        {
            try
            {
                var slotNumber = slotIndex + 1;
                var thumbnail = this.FindName($"MediaThumbnail{slotNumber}") as Image;
                var placeholder = this.FindName($"AddMediaPlaceholder{slotNumber}") as StackPanel;
                var infoOverlay = this.FindName($"MediaInfoOverlay{slotNumber}") as Border;
                var fileName = this.FindName($"MediaFileName{slotNumber}") as TextBlock;
                var removeButton = this.FindName($"RemoveMediaButton{slotNumber}") as Button;
                var mediaSlot = this.FindName($"MediaSlot{slotNumber}") as Border;

                // Release existing image source to prevent memory leaks and file locks
                if (thumbnail?.Source is BitmapImage existingBitmap)
                {
                    try
                    {
                        thumbnail.Source = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to release existing image source for slot {slotNumber}: {ex.Message}");
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
                                var fileExtension = Path.GetExtension(filePath).ToLowerInvariant();
                                if (VideoThumbnailHelper.IsSupportedImageFile(filePath))
                                {
                                    // Display image directly with proper resource management
                                    var bitmap = new BitmapImage();
                                    using (var stream = File.OpenRead(filePath))
                                    {
                                        var randomAccessStream = stream.AsRandomAccessStream();
                                        await bitmap.SetSourceAsync(randomAccessStream);
                                    }
                                    
                                    thumbnail.Source = bitmap;
                                    thumbnail.Visibility = Visibility.Visible;
                                }
                                else if (VideoThumbnailHelper.IsSupportedVideoFile(filePath))
                                {
                                    // Check cache first for video thumbnails
                                    BitmapImage? videoThumbnail = null;
                                    
                                    if (_videoThumbnailCache.ContainsKey(filePath))
                                    {
                                        videoThumbnail = _videoThumbnailCache[filePath];
                                        Debug.WriteLine($"Using cached video thumbnail for slot {slotNumber}: {Path.GetFileName(filePath)}");
                                    }
                                    else
                                    {
                                        // Generate and cache video thumbnail
                                        videoThumbnail = await VideoThumbnailHelper.GenerateVideoThumbnailAsync(filePath, width: 100, height: 75);
                                        if (videoThumbnail != null)
                                        {
                                            _videoThumbnailCache[filePath] = videoThumbnail;
                                            Debug.WriteLine($"Generated and cached video thumbnail for slot {slotNumber}: {Path.GetFileName(filePath)}");
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
                                        Debug.WriteLine($"Failed to generate video thumbnail for slot {slotNumber}: {Path.GetFileName(filePath)}");
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
                            Debug.WriteLine($"Failed to load media for slot {slotNumber}: {ex.Message}");
                            thumbnail.Visibility = Visibility.Collapsed;
                        }
                    }

                    if (placeholder != null) 
                    {
                        // Show appropriate icon based on media type
                        var stackPanel = placeholder;
                        var iconElement = stackPanel.Children.OfType<FontIcon>().FirstOrDefault();
                        var textElement = stackPanel.Children.OfType<TextBlock>().FirstOrDefault();
                        
                        if (iconElement != null)
                        {
                            if (File.Exists(filePath))
                            {
                                var fileExtension = Path.GetExtension(filePath).ToLowerInvariant();
                                if (VideoThumbnailHelper.IsSupportedVideoFile(filePath))
                                {
                                    iconElement.Glyph = "\uE714"; // Video play icon
                                    iconElement.Foreground = new SolidColorBrush(Colors.LightCoral);
                                    iconElement.FontSize = 24; // Larger for video icon
                                }
                                else if (VideoThumbnailHelper.IsSupportedImageFile(filePath))
                                {
                                    iconElement.Glyph = "\uE91B"; // Image icon (backup)
                                    iconElement.Foreground = new SolidColorBrush(Colors.LightGreen);
                                }
                                else
                                {
                                    iconElement.Glyph = "\uE8A5"; // Generic media icon
                                    iconElement.Foreground = new SolidColorBrush(Colors.LightBlue);
                                }
                            }
                            else
                            {
                                // Device media
                                iconElement.Glyph = "\uE8B3"; // Device media icon
                                iconElement.Foreground = new SolidColorBrush(Colors.LightBlue);
                            }
                        }
                        
                        if (textElement != null)
                        {
                            if (File.Exists(filePath))
                            {
                                var fileExtension = Path.GetExtension(filePath).ToLowerInvariant();
                                if (VideoThumbnailHelper.IsSupportedVideoFile(filePath))
                                {
                                    textElement.Text = "Video Media";
                                    textElement.Foreground = new SolidColorBrush(Colors.LightCoral);
                                }
                                else if (VideoThumbnailHelper.IsSupportedImageFile(filePath))
                                {
                                    textElement.Text = "Image Media";
                                    textElement.Foreground = new SolidColorBrush(Colors.LightGreen);
                                }
                                else
                                {
                                    textElement.Text = "Media File";
                                    textElement.Foreground = new SolidColorBrush(Colors.LightBlue);
                                }
                            }
                            else
                            {
                                textElement.Text = "Device Media";
                                textElement.Foreground = new SolidColorBrush(Colors.LightBlue);
                            }
                        }
                        
                        // Show placeholder when image is not visible
                        bool showPlaceholder = thumbnail?.Visibility != Visibility.Visible;
                        placeholder.Visibility = showPlaceholder ? Visibility.Visible : Visibility.Collapsed;
                    }
                    
                    if (infoOverlay != null) infoOverlay.Visibility = Visibility.Visible;
                    if (fileName != null) 
                    {
                        fileName.Text = File.Exists(filePath) ? Path.GetFileName(filePath) : Path.GetFileName(filePath ?? "Unknown");
                    }
                    if (removeButton != null) removeButton.Visibility = Visibility.Visible;
                    if (mediaSlot != null) mediaSlot.BorderBrush = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    // Show empty slot - ensure image source is properly released
                    if (thumbnail != null) 
                    {
                        thumbnail.Source = null;
                        thumbnail.Visibility = Visibility.Collapsed;
                    }
                    if (placeholder != null) 
                    {
                        // Reset to add media state
                        var stackPanel = placeholder;
                        var iconElement = stackPanel.Children.OfType<FontIcon>().FirstOrDefault();
                        var textElement = stackPanel.Children.OfType<TextBlock>().FirstOrDefault();
                        
                        if (iconElement != null)
                        {
                            iconElement.Glyph = "\uE710"; // Add icon
                            iconElement.Foreground = new SolidColorBrush(Colors.Gray);
                            iconElement.FontSize = 24; // Reset font size
                        }
                        if (textElement != null)
                        {
                            textElement.Text = "Add Media";
                            textElement.Foreground = new SolidColorBrush(Colors.Gray);
                        }
                        
                        placeholder.Visibility = Visibility.Visible;
                    }
                    if (infoOverlay != null) infoOverlay.Visibility = Visibility.Collapsed;
                    if (removeButton != null) removeButton.Visibility = Visibility.Collapsed;
                    if (mediaSlot != null) mediaSlot.BorderBrush = new SolidColorBrush(Colors.Gray);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update media slot UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to find local media file for a given slot index
        /// </summary>
        private string? TryFindLocalMediaFile(int slotIndex)
        {
            try
            {
                // First check the offline media data service for this device
                if (_deviceViewModel?.SerialNumber != null && _offlineMediaService != null)
                {
                    var mediaFile = _offlineMediaService.GetSuspendMediaFile(_deviceViewModel.SerialNumber, slotIndex);
                    
                    if (mediaFile != null && File.Exists(mediaFile.LocalPath))
                    {
                        Debug.WriteLine($"Found media file in offline data for slot {slotIndex + 1}: {Path.GetFileName(mediaFile.LocalPath)}");
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
                        Debug.WriteLine($"Found local media file for slot {slotIndex + 1}: {Path.GetFileName(filePath)}");
                        return filePath;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error searching for local media file for slot {slotIndex + 1}: {ex.Message}");
            }
            
            return null;
        }

        #region Suspend Media Management Event Handlers

        /// <summary>
        /// Handle media slot button clicks for adding or viewing media
        /// </summary>
        private async void MediaSlotButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidService == null || _isLoading) return;

            // Tag is the slot index is string, convert to int and validate

            var button = sender as Button;
            if (button?.Tag is string tagStr && int.TryParse(tagStr, out int slotIndex))
            {
                //  add new media
                await AddMediaToSlot(slotIndex);
            }
        }

        /// <summary>
        /// Handle pointer enter for hover effects
        /// </summary>
        private void MediaSlot_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button button)
            {
                button.Opacity = 0.8;
            }
        }

        /// <summary>
        /// Handle pointer exit for hover effects
        /// </summary>
        private void MediaSlot_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button button)
            {
                button.Opacity = 1.0;
            }
        }

        /// <summary>
        /// Add media file to a specific slot
        /// </summary>
        private async Task AddMediaToSlot(int slotIndex)
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

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    StorageFile fileToAdd = file;

                    // Check if the selected file is an image
                    string fileExtension = Path.GetExtension(file.Name).ToLowerInvariant();
                    bool isImageFile = VideoThumbnailHelper.IsSupportedImageFile(file.Name);

                    if (isImageFile)
                    {
                        // Show image crop dialog for image files
                        var croppedFile = await ShowImageCropDialogAsync(file, slotIndex);
                        if (croppedFile != null)
                        {
                            fileToAdd = croppedFile;
                            Debug.WriteLine($"Using cropped image file: {croppedFile.Name}");
                        }
                        else
                        {
                            // User cancelled cropping, don't proceed
                            Debug.WriteLine("User cancelled image cropping");
                            return;
                        }
                    }
                    else
                    {
                        // For video files, just use the original file
                        Debug.WriteLine($"Selected video file: {file.Name}");
                    }

                    await AddMediaFileToSlot(fileToAdd, slotIndex);
                    UpdateMediaSlots();
                    Debug.WriteLine($"Added media file to slot {slotIndex + 1}.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding media to slot {slotIndex}: {ex.Message}");
            }
        }

        /// <summary>
        /// Add media file to device and update offline storage
        /// </summary>
        private async Task AddMediaFileToSlot(StorageFile file, int slotIndex)
        {
            try
            {
                if (_offlineMediaService == null || _deviceViewModel?.SerialNumber == null)
                {
                    throw new InvalidOperationException("Offline media service or device serial not available");
                }

                SetLoadingState(true, $"Adding media to slot {slotIndex + 1}...");

                // Create the proper filename with the required naming convention
                var fileExtension = Path.GetExtension(file.Name);
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
                    // Generate a unique transfer ID based on slot index
                    byte transferId = (byte)(slotIndex + 1);

                    //var transferResults = await _hidService.TransferFileAsync(file.Path, transferId);

                    // Transfer file to device using suspend media functionality
                    var transferResults = await _hidService.SendMultipleSuspendFilesAsync(
                        new List<string> { file.Path },
                        startingTransferId: (byte)(slotIndex + 1));

                    //var successCount = transferResults.Values.Count(r => r);

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
            finally
            {
                SetLoadingState(false);
            }
        }

        /// <summary>
        /// Remove media from a specific slot
        /// </summary>
        private async void RemoveMediaButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidService == null || _isLoading) return;

            if (sender is Button button && button.Tag is int slotIndex)
            {
                try
                {
                    SetLoadingState(true, $"Removing media from slot {slotIndex + 1}...");

                    // Release image source before deletion to avoid file locks
                    var slotNumber = slotIndex + 1;
                    var thumbnail = this.FindName($"MediaThumbnail{slotNumber}") as Image;
                    if (thumbnail?.Source is BitmapImage)
                    {
                        try
                        {
                            thumbnail.Source = null;
                            Debug.WriteLine($"Released image source for slot {slotNumber} before deletion");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to release image source for slot {slotNumber}: {ex.Message}");
                        }
                    }

                    // Get the actual filename from the offline media data
                    string? fileNameToDelete = null;
                    string? localMediaPath = null;
                    
                    if (_deviceViewModel?.SerialNumber != null && _offlineMediaService != null)
                    {
                        var mediaFile = _offlineMediaService.GetSuspendMediaFile(_deviceViewModel.SerialNumber, slotIndex);
                        
                        if (mediaFile != null)
                        {
                            fileNameToDelete = mediaFile.FileName;
                            localMediaPath = mediaFile.LocalPath;
                            Debug.WriteLine($"Found media file in offline data for deletion: {fileNameToDelete}");
                        }
                    }
                    
                    // Fallback to trying to find local media file
                    if (string.IsNullOrEmpty(fileNameToDelete))
                    {
                        localMediaPath = TryFindLocalMediaFile(slotIndex);
                        if (!string.IsNullOrEmpty(localMediaPath) && File.Exists(localMediaPath))
                        {
                            fileNameToDelete = Path.GetFileName(localMediaPath);
                            Debug.WriteLine($"Found local media file for deletion: {fileNameToDelete}");
                        }
                    }
                    
                    // Use fallback naming pattern if still no file found
                    if (string.IsNullOrEmpty(fileNameToDelete))
                    {
                        fileNameToDelete = $"suspend_{slotIndex}";
                        Debug.WriteLine($"Using fallback filename for deletion: {fileNameToDelete}");
                    }

                    // Delete specific suspend file from device
                    var results = await _hidService.DeleteSuspendFilesAsync(fileNameToDelete);
                    var successCount = results.Values.Count(r => r);
                    
                    if (successCount > 0)
                    {
                        // Remove from offline media data service
                        if (_deviceViewModel?.SerialNumber != null && _offlineMediaService != null)
                        {
                            _offlineMediaService.RemoveSuspendMediaFile(_deviceViewModel.SerialNumber, slotIndex);
                            Debug.WriteLine($"Removed media file from offline data: slot {slotIndex + 1}");
                        }
                        
                        // Remove from video thumbnail cache if it exists
                        if (!string.IsNullOrEmpty(localMediaPath) && _videoThumbnailCache.ContainsKey(localMediaPath))
                        {
                            try
                            {
                                _videoThumbnailCache.Remove(localMediaPath);
                                Debug.WriteLine($"Removed video thumbnail from cache: {Path.GetFileName(localMediaPath)}");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to remove cached thumbnail: {ex.Message}");
                            }
                        }
                        
                        // Update UI to show empty slot
                        UpdateMediaSlotUI(slotIndex, null, false);
                        Debug.WriteLine($"Media removed from slot {slotIndex + 1} successfully.");
                        
                        // Refresh device status to get updated suspend media info
                        await RefreshDeviceStatus();
                    }
                    else
                    {
                        Debug.WriteLine($"Failed to remove media from slot {slotIndex + 1}.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to remove media from slot {slotIndex + 1}: {ex.Message}");
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
        private async void AddAllMediaButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidService == null || _isLoading) return;

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
                    SetLoadingState(true, "Adding multiple media files...");

                    // Get available slots (empty slots)
                    var availableSlots = new List<int>();
                    for (int i = 0; i < _maxSuspendMediaCount; i++)
                    {
                        var localMediaPath = TryFindLocalMediaFile(i);
                        if (string.IsNullOrEmpty(localMediaPath))
                        {
                            availableSlots.Add(i);
                        }
                    }

                    int filesProcessed = 0;
                    int filesAdded = 0;
                    
                    for (int i = 0; i < files.Count && i < availableSlots.Count; i++)
                    {
                        var file = files[i];
                        var slotIndex = availableSlots[i];
                        filesProcessed++;
                        
                        try
                        {
                            StorageFile fileToAdd = file;
                            
                            // Check if the file is an image
                            bool isImageFile = VideoThumbnailHelper.IsSupportedImageFile(file.Name);
                            
                            if (isImageFile)
                            {
                                // Show crop dialog for each image
                                SetLoadingState(true, $"Processing image {filesProcessed} of {files.Count}: {file.Name}");
                                
                                var croppedFile = await ShowImageCropDialogAsync(file, slotIndex);
                                if (croppedFile != null)
                                {
                                    fileToAdd = croppedFile;
                                    Debug.WriteLine($"Using cropped/processed image for slot {slotIndex + 1}: {croppedFile.Name}");
                                }
                                else
                                {
                                    // User cancelled cropping, skip this file
                                    Debug.WriteLine($"User cancelled processing for image: {file.Name}");
                                    continue;
                                }
                            }
                            else
                            {
                                // For video files, just use the original
                                Debug.WriteLine($"Processing video file for slot {slotIndex + 1}: {file.Name}");
                            }
                            
                            SetLoadingState(true, $"Adding file {filesProcessed} of {files.Count} to slot {slotIndex + 1}...");
                            await AddMediaFileToSlot(fileToAdd, slotIndex);
                            filesAdded++;
                            
                            Debug.WriteLine($"Successfully added file {file.Name} to slot {slotIndex + 1}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to add file {file.Name} to slot {slotIndex}: {ex.Message}");
                        }
                    }

                    if (filesAdded > 0)
                    {
                        UpdateMediaSlots();
                        Debug.WriteLine($"Successfully added {filesAdded} out of {files.Count} files.");
                        
                        if (filesAdded < files.Count)
                        {
                            var skippedCount = files.Count - filesAdded;
                            Debug.WriteLine($"Skipped {skippedCount} files (cancelled by user or errors).");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("No files were added.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to add multiple media files: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        /// <summary>
        /// Clear all media files
        /// </summary>
        private async void ClearAllMediaButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsHidServiceReady || _hidService == null || _isLoading) return;

            var result = await ShowConfirmationDialogAsync("Clear All Media", 
                "Are you sure you want to remove all suspend media files? This action cannot be undone.");
                
            if (!result) return;

            try
            {
                SetLoadingState(true, "Clearing all media files...");

                // Release all image sources first to prevent file locks
                for (int i = 0; i < _maxSuspendMediaCount; i++)
                {
                    var slotNumber = i + 1;
                    var thumbnail = this.FindName($"MediaThumbnail{slotNumber}") as Image;
                    if (thumbnail?.Source is BitmapImage)
                    {
                        try
                        {
                            thumbnail.Source = null;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to release image source for slot {slotNumber}: {ex.Message}");
                        }
                    }
                }

                // Delete all suspend files from device
                var results = await _hidService.DeleteSuspendFilesAsync("all");
                var successCount = results.Values.Count(r => r);
                
                if (successCount > 0)
                {
                    // Clear from offline media data service
                    if (_deviceViewModel?.SerialNumber != null && _offlineMediaService != null)
                    {
                        _offlineMediaService.ClearAllSuspendMediaFiles(_deviceViewModel.SerialNumber);
                        Debug.WriteLine("Cleared all suspend media files from offline data");
                    }
                    
                    // Clear video thumbnail cache
                    _videoThumbnailCache.Clear();
                    Debug.WriteLine("Cleared video thumbnail cache");
                    
                    // Update UI to show all empty slots
                    UpdateMediaSlots();
                    
                    Debug.WriteLine("All media files cleared successfully.");
                    
                    // Refresh device status to get updated suspend media info
                    await RefreshDeviceStatus();
                }
                else
                {
                    Debug.WriteLine("Failed to clear media files.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to clear all media: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        /// <summary>
        /// Show media information for a slot
        /// </summary>
        private void ShowMediaInfo(int slotIndex)
        {
            var fileName = this.FindName($"MediaFileName{slotIndex + 1}") as TextBlock;
            var mediaName = fileName?.Text ?? $"Media {slotIndex + 1}";
            
            Debug.WriteLine($"Media: {mediaName} (Slot {slotIndex + 1})");
        }

        /// <summary>
        /// gets whether the HID device service is initialized and ready
        /// </summary>
        public bool IsHidServiceReady => _hidService?.IsInitialized == true;

        /// <summary>
        /// Set loading state for UI feedback
        /// </summary>
        private void SetLoadingState(bool isLoading, string message = "Loading...")
        {
            _isLoading = isLoading;
            // You can implement a loading overlay here if needed
            Debug.WriteLine($"Loading state: {isLoading} - {message}");
        }

        /// <summary>
        /// Refresh device status to update suspend media information
        /// </summary>
        private async Task RefreshDeviceStatus()
        {
            try
            {
                if (_hidService != null && _deviceViewModel?.DevicePath != null)
                {
                    var statusResults = await _hidService.GetDeviceStatusAsync();
                    
                    if (statusResults.TryGetValue(_deviceViewModel.DevicePath, out var deviceStatus) && deviceStatus.HasValue)
                    {
                        var status = deviceStatus.Value;
                        UpdateStatusDisplay(status);
                        
                        // Update suspend media display if offline mode is active
                        if (OfflineModeToggle.IsOn)
                        {
                            await ShowSuspendMediaSlotsAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to refresh device status: {ex.Message}");
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
                        
                        // Load suspend media slots based on device status
                        LoadSuspendMediaSlots(status);
                    }
                    else
                    {
                        // No device status, just show offline media
                        UpdateMediaSlots();
                    }
                }
                else
                {
                    // No HID service, just show offline media
                    UpdateMediaSlots();
                }

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

        /// <summary>
        /// Load and display suspend media slots based on device status
        /// </summary>
        private void LoadSuspendMediaSlots(DeviceStatus status)
        {
            try
            {
                // Ensure content area is visible
                var suspendMediaCard = this.FindName("SuspendMediaCard") as CommunityToolkit.WinUI.Controls.SettingsCard;
                if (suspendMediaCard != null) 
                    suspendMediaCard.Visibility = Visibility.Visible;
                
                // Use actual device status to determine which slots have media
                var suspendMediaActive = status.SuspendMediaActive ?? new int[0];
                var maxSlots = Math.Min(_maxSuspendMediaCount, status.MaxSuspendMediaCount);
                
                Debug.WriteLine($"Loading suspend media slots. Max slots: {maxSlots}, Active media: [{string.Join(", ", suspendMediaActive)}]");
                
                // Update each media slot based on device status
                for (int i = 0; i < _maxSuspendMediaCount; i++)
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
                            Debug.WriteLine($"Slot {i + 1}: Local media file found - {Path.GetFileName(localMediaPath)}");
                        }
                        else
                        {
                            // Show device media with placeholder
                            UpdateMediaSlotUI(i, $"suspend_media_{i + 1}", true);
                            Debug.WriteLine($"Slot {i + 1}: Device media found (no local file)");
                        }
                    }
                    else
                    {
                        // Show empty slot with add button
                        UpdateMediaSlotUI(i, null, false);
                        Debug.WriteLine($"Slot {i + 1}: Empty slot");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load suspend media slots: {ex.Message}");
                // Clear all slots on error
                for (int i = 0; i < _maxSuspendMediaCount; i++)
                {
                    UpdateMediaSlotUI(i, null, false);
                }
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

                // Release all image sources to prevent memory leaks
                for (int i = 0; i < _maxSuspendMediaCount; i++)
                {
                    var slotNumber = i + 1;
                    var thumbnail = this.FindName($"MediaThumbnail{slotNumber}") as Image;
                    if (thumbnail?.Source is BitmapImage)
                    {
                        try
                        {
                            thumbnail.Source = null;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to release image source for slot {slotNumber} during hide: {ex.Message}");
                        }
                    }
                }

                // Clear all media slots to show add state
                for (int i = 0; i < _maxSuspendMediaCount; i++)
                {
                    UpdateMediaSlotUI(i, null, false);
                }
                
                Debug.WriteLine("Suspend mode content hidden and media slots reset with image sources released");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error hiding suspend media slots: {ex.Message}");
            }
        }

        /// <summary>
        /// Clean up resources when page is unloaded
        /// </summary>
        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Release all image sources to prevent memory leaks
                for (int i = 1; i <= _maxSuspendMediaCount; i++)
                {
                    var thumbnail = this.FindName($"MediaThumbnail{i}") as Image;
                    if (thumbnail?.Source is BitmapImage)
                    {
                        try
                        {
                            thumbnail.Source = null;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to release image source for thumbnail {i}: {ex.Message}");
                        }
                    }
                }
                
                // Clear and dispose video thumbnail cache
                _videoThumbnailCache?.Clear();
                Debug.WriteLine("Disposed all cached video thumbnails during page cleanup");
                
                // Clean up services
                CleanupServices();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during page cleanup: {ex.Message}");
            }
        }

        #endregion

        #region Missing Methods from Original Implementation

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

        private async Task LoadDeviceInformationAsync()
        {
            await LoadFirmwareInformationAsync();
            await LoadCurrentDeviceSettingsAsync();
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
            } catch (Exception ex)
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
        
        private void ClearStatusDisplay()
        {
            // Reset all status displays to default/error state
            StatusBrightnessProgressBar.Value = 0;
            StatusBrightnessText.Text = "N/A";
            
            StatusRotationText.Text = "N/A";
            
            StatusSleepIndicator.Fill = new SolidColorBrush(Colors.Gray);
            StatusSleepText.Text = "Unknown";
            
            StatusOsdIndicator.Fill = new SolidColorBrush(Colors.Gray);
            StatusOsdText.Text = "Unknown";
            
            StatusKeepAliveText.Text = "N/A";
            
            StatusMediaProgressBar.Value = 0;
            StatusMediaProgressBar.Maximum = 1;
            StatusMediaText.Text = "N/A";
            
            MediaSlotsPanel.Children.Clear();
            
            StatusTimestampText.Text = "Never";
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

        #endregion

        #region Event Handlers

        /// <summary>
        /// Enables or disables real-time mode for HID devices
        /// </summary>
        /// <param name="enable">True to enable real-time mode</param>
        /// <returns>True if successful</returns>
        public async Task<bool> SetHidRealTimeModeAsync(bool enable)
        {
            if (_hidService == null)
            {
                Debug.WriteLine(this, "Cannot set HID real-time mode: HID service not available");
                return false;
            }

            try
            {
                var results = await _hidService.SetRealTimeDisplayAsync(enable);
                var successCount = results.Values.Count(r => r);
                var totalCount = results.Count;

                if (successCount > 0)
                {
                    Debug.WriteLine(this, $"HID real-time mode {(enable ? "enabled" : "disabled")} on {successCount}/{totalCount} devices");
                    return true;
                }
                else
                {
                    Debug.WriteLine(this, $"Failed to set HID real-time mode on any devices");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(this, $"Error setting HID real-time mode: {ex.Message}");
                return false;
            }
        }

        private async void OfflineModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isOfflineModeChanging) return;

            _isOfflineModeChanging = true;
            try
            {
                bool isOfflineMode = OfflineModeToggle.IsOn;

                // call hid service SetRealTimeDisplayAsync to enable/disable real-time display.
                bool hidResult = await SetHidRealTimeModeAsync(!isOfflineMode);
                if (!hidResult)
                {
                    // Revert toggle state on failure
                    OfflineModeToggle.IsOn = !OfflineModeToggle.IsOn;
                    Debug.WriteLine("Failed to change offline mode due to HID service error.");
                    return;
                }


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
                        "Device switched to sleep mode. LCD will turn on when system sleeps." :
                        "Device exited sleep mode. LCD will turn off when system sleeps.";

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
                    if (StatusRotationText != null)
                    {
                        StatusRotationText.Text = $"{rotation}°";
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
                var fileName = $"resized_{slotIndex}_{Path.GetFileNameWithoutExtension(originalFile.Name)}.png";
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
                        (uint)targetWidth,
                        (uint)targetHeight,
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

                Debug.WriteLine($"Successfully resized image to {targetWidth}x{targetHeight}: {tempFile.Name}");
                return tempFile;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resizing image: {ex.Message}");
                throw;
            }
        }        

        /// <summary>
        /// Shows image crop dialog using the ImageCrop UserControl and returns cropped image file
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
                var croppedFileName = $"cropped_{slotIndex}_{Path.GetFileNameWithoutExtension(originalFile.Name)}.jpg";
                var croppedImageFile = await tempFolder.CreateFileAsync(croppedFileName, CreationCollisionOption.ReplaceExisting);

                // Create the ImageCrop UserControl
                var imageCropControl = new CDMDevicesManagerDevWinUI.Controls.ImageCrop();

                // Set target size and aspect ratio for the cropper (480x480 = 1:1 aspect ratio)
                imageCropControl.Width = 480;
                imageCropControl.Height = 480; // Extra height for buttons
                imageCropControl.AspectRatio = 1.0; // Square aspect ratio for 480x480
                imageCropControl.CropShape = CommunityToolkit.WinUI.Controls.CropShape.Rectangular;
                // Initialize the ImageCrop control
                await imageCropControl.InitializeAsync(originalFile, croppedImageFile);
                // Create the ContentDialog
                var imageCrop = new ImageCropper
                {
                    AspectRatio = 1.0, // Square aspect ratio for 480x480
                    CropShape = CommunityToolkit.WinUI.Controls.CropShape.Rectangular,
                    Width = 480,
                    Height = 480
                };
                await imageCrop.LoadImageFromFile(originalFile);
                var cropDialog = new ContentDialog
                {
                    Title = "Crop Image for 480x480 Display",
                    PrimaryButtonText = "Save",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot,
                    Content = imageCrop

                };

                // Show the dialog and wait for user choice
                var result = await cropDialog.ShowAsync();
                
                if (result == ContentDialogResult.Primary)
                {
                    // User clicked Save, get the cropped image
                    //var croppedFile = await imageCrop.SaveAsync(croppedImageFile, BitmapFileFormat.Jpeg);
                }
                // User cancelled
                Debug.WriteLine("User cancelled image cropping");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in image crop dialog: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}
