using FFMpegCore;
using FFMpegCore.Enums;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CMDevicesManager.Helper
{
    /// <summary>
    /// Helper class for generating video thumbnails using FFMpegCore
    /// </summary>
    public static class VideoThumbnailHelper
    {
        /// <summary>
        /// Generate a thumbnail from a video file
        /// </summary>
        /// <param name="videoPath">Path to the video file</param>
        /// <param name="timeSpan">Time position to capture thumbnail (default: 00:00:01)</param>
        /// <param name="width">Thumbnail width (default: 200)</param>
        /// <param name="height">Thumbnail height (default: 150)</param>
        /// <returns>BitmapImage of the thumbnail or null if failed</returns>
        public static async Task<BitmapImage?> GenerateVideoThumbnailAsync(string videoPath, TimeSpan? timeSpan = null, int width = 200, int height = 150)
        {
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            {
                Logger.Warn($"Video file not found: {videoPath}");
                return null;
            }

            try
            {
                // Set default time position to 1 second
                var captureTime = timeSpan ?? TimeSpan.FromSeconds(1);

                // Create a temporary file for the thumbnail
                var tempThumbnailPath = Path.Combine(Path.GetTempPath(), $"thumb_{Guid.NewGuid():N}.jpg");

                try
                {
                    // Configure FFMpeg binary path if needed
                    ConfigureFFMpegPath();

                    // Generate thumbnail using FFMpegCore
                    var success = await FFMpegArguments
                        .FromFileInput(videoPath)
                        .OutputToFile(tempThumbnailPath, overwrite: true, options => options
                            .Seek(captureTime)
                            .WithVideoFilters(filterOptions => filterOptions
                                .Scale(width, height))
                            .WithFrameOutputCount(1))
                        .ProcessAsynchronously();

                    if (success && File.Exists(tempThumbnailPath))
                    {
                        // Load the generated thumbnail into a BitmapImage
                        var bitmapImage = new BitmapImage();
                        
                        using (var stream = File.OpenRead(tempThumbnailPath))
                        {
                            bitmapImage.BeginInit();
                            bitmapImage.StreamSource = stream;
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.DecodePixelWidth = width;
                            bitmapImage.DecodePixelHeight = height;
                            bitmapImage.EndInit();
                            bitmapImage.Freeze(); // Make it thread-safe
                        }

                        Logger.Info($"Video thumbnail generated successfully for: {Path.GetFileName(videoPath)}");
                        return bitmapImage;
                    }
                    else
                    {
                        Logger.Warn($"Failed to generate video thumbnail for: {Path.GetFileName(videoPath)}");
                        return null;
                    }
                }
                finally
                {
                    // Clean up temporary file
                    try
                    {
                        if (File.Exists(tempThumbnailPath))
                        {
                            File.Delete(tempThumbnailPath);
                        }
                    }
                    catch (Exception cleanupEx)
                    {
                        Logger.Warn($"Failed to delete temporary thumbnail file: {cleanupEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error generating video thumbnail for {Path.GetFileName(videoPath)}: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Configure FFMpeg binary path to use the bundled executable
        /// </summary>
        private static void ConfigureFFMpegPath()
        {
            try
            {
                var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var ffmpegPath = Path.Combine(appDirectory, "exe", "ffmpeg.exe");
                var ffprobePath = Path.Combine(appDirectory, "exe", "ffprobe.exe");

                if (File.Exists(ffmpegPath) && File.Exists(ffprobePath))
                {
                    // Set FFMpeg binary path
                    GlobalFFOptions.Configure(new FFOptions { BinaryFolder = Path.Combine(appDirectory, "exe") });
                    Logger.Info($"FFMpeg configured with bundled binaries at: {Path.Combine(appDirectory, "exe")}");
                }
                else
                {
                    Logger.Warn("Bundled FFMpeg binaries not found, using system PATH");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to configure FFMpeg path: {ex.Message}", ex);
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

            var extension = Path.GetExtension(filePath).ToLower();
            var supportedExtensions = new[] { ".mp4", ".avi", ".mov", ".wmv", ".mkv", ".flv", ".webm", ".m4v", ".3gp", ".mpg", ".mpeg" };
            
            return Array.Exists(supportedExtensions, ext => ext == extension);
        }

        /// <summary>
        /// Get video information (duration, resolution, etc.)
        /// </summary>
        /// <param name="videoPath">Path to the video file</param>
        /// <returns>Video information or null if failed</returns>
        public static async Task<IMediaAnalysis?> GetVideoInfoAsync(string videoPath)
        {
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
                return null;

            try
            {
                ConfigureFFMpegPath();
                var videoInfo = await FFProbe.AnalyseAsync(videoPath);
                Logger.Info($"Video info retrieved for: {Path.GetFileName(videoPath)} - Duration: {videoInfo.Duration}, Resolution: {videoInfo.PrimaryVideoStream?.Width}x{videoInfo.PrimaryVideoStream?.Height}");
                return videoInfo;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get video info for {Path.GetFileName(videoPath)}: {ex.Message}", ex);
                return null;
            }
        }
    }
}