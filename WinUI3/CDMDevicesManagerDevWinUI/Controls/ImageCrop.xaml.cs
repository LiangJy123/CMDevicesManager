using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using Windows.Graphics.Imaging;

namespace CDMDevicesManagerDevWinUI.Controls
{
    public sealed partial class ImageCrop : UserControl
    {
        private StorageFile? _originalImageFile;
        private StorageFile? _croppedImageFile;
        private string? _originalImagePath;
        private string? _croppedImagePath;

        // Events for parent to handle
        public event EventHandler<ImageCropSavedEventArgs>? ImageSaved;
        public event EventHandler? ImageReset;

        public ImageCrop()
        {
            InitializeComponent();
            
            // Set default properties for the ImageCropper
            ImageCropper.AspectRatio = 1.0; // Square aspect ratio for 480x480
            ImageCropper.CropShape = CommunityToolkit.WinUI.Controls.CropShape.Rectangular;
        }

        /// <summary>
        /// Initialize the ImageCrop control with original and cropped image paths
        /// </summary>
        /// <param name="originalImagePath">Path to the original image</param>
        /// <param name="croppedImagePath">Path where the cropped image will be saved</param>
        public async Task InitializeAsync(string originalImagePath, string croppedImagePath)
        {
            try
            {
                _originalImagePath = originalImagePath;
                _croppedImagePath = croppedImagePath;

                // Load the original image file
                _originalImageFile = await StorageFile.GetFileFromPathAsync(originalImagePath);
                
                // Create or get the cropped image file
                var croppedFile = await StorageFile.GetFileFromPathAsync(croppedImagePath);
                _croppedImageFile = croppedFile;

                // Load the image into the cropper
                await LoadOriginalImageAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing ImageCrop: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize with StorageFile objects
        /// </summary>
        /// <param name="originalImageFile">Original image StorageFile</param>
        /// <param name="croppedImageFile">Target cropped image StorageFile</param>
        public async Task InitializeAsync(StorageFile originalImageFile, StorageFile croppedImageFile)
        {
            try
            {
                _originalImageFile = originalImageFile;
                _croppedImageFile = croppedImageFile;
                _originalImagePath = originalImageFile.Path;
                _croppedImagePath = croppedImageFile.Path;

                // Load the image into the cropper
                await LoadOriginalImageAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing ImageCrop with StorageFiles: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets or sets the aspect ratio for cropping
        /// </summary>
        public double AspectRatio
        {
            get => ImageCropper.AspectRatio ?? 1.0;
            set => ImageCropper.AspectRatio = value;
        }

        /// <summary>
        /// Gets or sets the crop shape
        /// </summary>
        public CommunityToolkit.WinUI.Controls.CropShape CropShape
        {
            get => ImageCropper.CropShape;
            set => ImageCropper.CropShape = value;
        }

        /// <summary>
        /// Loads the original image into the cropper
        /// </summary>
        private async Task LoadOriginalImageAsync()
        {
            if (_originalImageFile != null)
            {
                await ImageCropper.LoadImageFromFile(_originalImageFile);
                UpdateButtonStates();
            }
        }

        /// <summary>
        /// Updates the enabled state of buttons
        /// </summary>
        private void UpdateButtonStates()
        {
            bool hasImage = _originalImageFile != null;
            
            if (SaveButton != null)
                SaveButton.IsEnabled = hasImage && _croppedImageFile != null;
            if (ResetButton != null)
                ResetButton.IsEnabled = hasImage;
        }

        /// <summary>
        /// Save button click handler
        /// </summary>
        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveCroppedImageAsync();
        }

        /// <summary>
        /// Reset button click handler
        /// </summary>
        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            await ResetToOriginalAsync();
        }

        /// <summary>
        /// Save the cropped image using the ImageCropper's SaveAsync method
        /// </summary>
        public async Task<bool> SaveCroppedImageAsync()
        {
            try
            {
                if (_croppedImageFile == null)
                {
                    Debug.WriteLine("No cropped image file specified");
                    return false;
                }

                if (SaveButton != null)
                {
                    SaveButton.IsEnabled = false;
                    SaveButton.Content = "Saving...";
                }

                // Use the ImageCropper's SaveAsync method as requested
                using (var fileStream = await _croppedImageFile.OpenAsync(FileAccessMode.ReadWrite, StorageOpenOptions.None))
                {
                    // Clear the stream first
                    fileStream.Size = 0;
                    
                    // Save the cropped image as PNG - using CommunityToolkit's BitmapFileFormat
                    await ImageCropper.SaveAsync(fileStream, CommunityToolkit.WinUI.Controls.BitmapFileFormat.Jpeg);
                }

                Debug.WriteLine($"Successfully saved cropped image to: {_croppedImageFile.Path}");

                // Fire the ImageSaved event
                ImageSaved?.Invoke(this, new ImageCropSavedEventArgs
                {
                    OriginalImagePath = _originalImagePath,
                    CroppedImagePath = _croppedImagePath,
                    CroppedImageFile = _croppedImageFile
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving cropped image: {ex.Message}");
                return false;
            }
            finally
            {
                if (SaveButton != null)
                {
                    SaveButton.IsEnabled = true;
                    SaveButton.Content = "Save";
                }
            }
        }

        /// <summary>
        /// Reset the cropper to show the original image
        /// </summary>
        public async Task ResetToOriginalAsync()
        {
            try
            {
                if (_originalImageFile != null)
                {
                    if (ResetButton != null)
                    {
                        ResetButton.IsEnabled = false;
                        ResetButton.Content = "Resetting...";
                    }

                    await ImageCropper.LoadImageFromFile(_originalImageFile);
                    
                    Debug.WriteLine("Reset to original image");

                    // Fire the ImageReset event
                    ImageReset?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resetting image: {ex.Message}");
            }
            finally
            {
                if (ResetButton != null)
                {
                    ResetButton.IsEnabled = true;
                    ResetButton.Content = "Reset";
                }
            }
        }

        /// <summary>
        /// Gets access to the underlying ImageCropper control for advanced operations
        /// </summary>
        public CommunityToolkit.WinUI.Controls.ImageCropper ImageCropperControl => ImageCropper;

        /// <summary>
        /// Gets the original image file
        /// </summary>
        public StorageFile? OriginalImageFile => _originalImageFile;

        /// <summary>
        /// Gets the cropped image file
        /// </summary>
        public StorageFile? CroppedImageFile => _croppedImageFile;
    }

    /// <summary>
    /// Event args for when an image is saved
    /// </summary>
    public class ImageCropSavedEventArgs : EventArgs
    {
        public string? OriginalImagePath { get; set; }
        public string? CroppedImagePath { get; set; }
        public StorageFile? CroppedImageFile { get; set; }
    }
}