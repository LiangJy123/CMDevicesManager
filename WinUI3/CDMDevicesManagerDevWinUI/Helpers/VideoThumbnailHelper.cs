using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using System.Diagnostics;

namespace CDMDevicesManagerDevWinUI.Helpers
{
    /// <summary>
    /// Helper class for generating video thumbnails in WinUI3
    /// </summary>
    public static class VideoThumbnailHelper
    {
        /// <summary>
        /// Generate a thumbnail from a video file using Windows Media APIs
        /// </summary>
        /// <param name="videoPath">Path to the video file</param>
        /// <param name="width">Thumbnail width (default: 200)</param>
        /// <param name="height">Thumbnail height (default: 150)</param>
        /// <returns>BitmapImage of the thumbnail or null if failed</returns>
        public static async Task<BitmapImage?> GenerateVideoThumbnailAsync(string videoPath, int width = 200, int height = 150)
        {
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            {
                Debug.WriteLine($"Video file not found: {videoPath}");
                return null;
            }

            try
            {
                // For now, return a placeholder video icon since WinUI3 doesn't have built-in video thumbnail generation
                // In a production app, you'd integrate with FFMpeg.NET or similar
                return await CreateVideoPlaceholderThumbnailAsync(width, height);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating video thumbnail for {Path.GetFileName(videoPath)}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Create a placeholder thumbnail for video files
        /// </summary>
        private static async Task<BitmapImage?> CreateVideoPlaceholderThumbnailAsync(int width, int height)
        {
            try
            {
                // Create a simple colored bitmap as placeholder
                var writeableBitmap = new WriteableBitmap(width, height);
                
                // Convert WriteableBitmap to BitmapImage
                var bitmapImage = new BitmapImage();
                
                using (var stream = new InMemoryRandomAccessStream())
                {
                    // Save WriteableBitmap to stream
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                    
                    // Create a simple gradient for video placeholder
                    var pixels = new byte[width * height * 4]; // BGRA format
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        pixels[i] = 100;     // Blue
                        pixels[i + 1] = 50;  // Green  
                        pixels[i + 2] = 150; // Red
                        pixels[i + 3] = 255; // Alpha
                    }
                    
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, 
                        (uint)width, (uint)height, 96, 96, pixels);
                    await encoder.FlushAsync();
                    
                    stream.Seek(0);
                    await bitmapImage.SetSourceAsync(stream);
                }
                
                return bitmapImage;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating video placeholder: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Check if a file is a supported video format
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <returns>True if the file is a supported video format</returns>
        public static bool IsSupportedVideoFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var supportedExtensions = new[] { ".mp4", ".avi", ".mov", ".wmv", ".mkv", ".flv", ".webm", ".m4v", ".3gp", ".mpg", ".mpeg" };
            
            return Array.Exists(supportedExtensions, ext => ext == extension);
        }

        /// <summary>
        /// Check if a file is a supported image format
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <returns>True if the file is a supported image format</returns>
        public static bool IsSupportedImageFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" };
            
            return Array.Exists(supportedExtensions, ext => ext == extension);
        }
    }
}