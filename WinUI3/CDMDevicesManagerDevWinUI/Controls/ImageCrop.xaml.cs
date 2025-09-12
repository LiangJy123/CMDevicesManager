using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

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

                // Check if ImageCropper has an image loaded
                if (ImageCropper.Source == null)
                {
                    Debug.WriteLine("No image loaded in ImageCropper");
                    return false;
                }

                // Update UI on UI thread
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (SaveButton != null)
                    {
                        SaveButton.IsEnabled = false;
                        SaveButton.Content = "Saving...";
                    }
                });

                // Use the ImageCropper's SaveAsync method with the correct API
                using (var fileStream = await _croppedImageFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    // Clear the stream first
                    fileStream.Size = 0;
                    
                    try
                    {
                        // Try to save using the correct CommunityToolkit API
                        await ImageCropper.SaveAsync(fileStream, CommunityToolkit.WinUI.Controls.BitmapFileFormat.Jpeg);
                    }
                    catch (Exception saveEx)
                    {
                        Debug.WriteLine($"CommunityToolkit SaveAsync failed: {saveEx.Message}");
                        
                        // Fallback: try alternative method using Windows.Graphics.Imaging
                        fileStream.Seek(0);
                        fileStream.Size = 0;
                        
                        // Get the current cropped area manually
                        var croppedBounds = ImageCropper.CroppedRegion;
                        if (croppedBounds.IsEmpty)
                        {
                            throw new InvalidOperationException("No valid crop region selected");
                        }

                        // Load the original image and crop it manually
                        if (_originalImageFile != null)
                        {
                            using (var originalStream = await _originalImageFile.OpenReadAsync())
                            {
                                var decoder = await BitmapDecoder.CreateAsync(originalStream);
                                
                                // Calculate crop bounds
                                var cropX = (uint)Math.Max(0, croppedBounds.X);
                                var cropY = (uint)Math.Max(0, croppedBounds.Y);
                                var cropWidth = (uint)Math.Min(croppedBounds.Width, decoder.PixelWidth - cropX);
                                var cropHeight = (uint)Math.Min(croppedBounds.Height, decoder.PixelHeight - cropY);
                                
                                // Create bounds for cropping
                                var bounds = new BitmapBounds()
                                {
                                    X = cropX,
                                    Y = cropY,
                                    Width = cropWidth,
                                    Height = cropHeight
                                };
                                
                                // Create transform for cropping
                                var transform = new BitmapTransform()
                                {
                                    Bounds = bounds
                                };
                                
                                // Get pixel data for the cropped area
                                var pixelData = await decoder.GetPixelDataAsync(
                                    BitmapPixelFormat.Bgra8,
                                    BitmapAlphaMode.Straight,
                                    transform,
                                    ExifOrientationMode.RespectExifOrientation,
                                    ColorManagementMode.DoNotColorManage);
                                
                                // Create encoder and save as JPEG
                                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, fileStream);
                                encoder.SetPixelData(
                                    BitmapPixelFormat.Bgra8,
                                    BitmapAlphaMode.Straight,
                                    cropWidth,
                                    cropHeight,
                                    decoder.DpiX,
                                    decoder.DpiY,
                                    pixelData.DetachPixelData());
                                
                                await encoder.FlushAsync();
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("Original image file not available for manual cropping");
                        }
                    }
                }

                Debug.WriteLine($"Successfully saved cropped image to: {_croppedImageFile.Path}");

                // Fire the ImageSaved event on UI thread
                DispatcherQueue.TryEnqueue(() =>
                {
                    ImageSaved?.Invoke(this, new ImageCropSavedEventArgs
                    {
                        OriginalImagePath = _originalImagePath,
                        CroppedImagePath = _croppedImagePath,
                        CroppedImageFile = _croppedImageFile
                    });
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving cropped image: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
            finally
            {
                // Restore UI state on UI thread
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (SaveButton != null)
                    {
                        SaveButton.IsEnabled = true;
                        SaveButton.Content = "Save";
                    }
                });
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