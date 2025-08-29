using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CMDevicesManager.Utilities
{
    /// <summary>
    /// Utility class for image format conversion operations
    /// </summary>
    public static class MyImageConverter
    {
        /// <summary>
        /// Convert GIF to JPEG format
        /// </summary>
        /// <param name="gifFilePath">Path to the source GIF file</param>
        /// <param name="jpegFilePath">Path where the JPEG file will be saved</param>
        /// <param name="quality">JPEG quality (1-100, default 90)</param>
        /// <param name="frameIndex">Frame index to extract from GIF (default 0 = first frame)</param>
        /// <returns>True if conversion was successful</returns>
        public static bool ConvertGifToJpeg(string gifFilePath, string jpegFilePath, int quality = 90, int frameIndex = 0)
        {
            try
            {
                if (!File.Exists(gifFilePath))
                {
                    throw new FileNotFoundException($"GIF file not found: {gifFilePath}");
                }

                if (quality < 1 || quality > 100)
                {
                    throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 1 and 100");
                }

                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(jpegFilePath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                using (var gifImage = Image.FromFile(gifFilePath))
                {
                    // Get the specified frame (or first frame if index is 0)
                    if (gifImage.GetFrameCount(FrameDimension.Time) > frameIndex)
                    {
                        gifImage.SelectActiveFrame(FrameDimension.Time, frameIndex);
                    }

                    // Create a new bitmap to ensure proper format conversion
                    using (var bitmap = new Bitmap(gifImage.Width, gifImage.Height, PixelFormat.Format24bppRgb))
                    {
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            // Set high quality rendering
                            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                            // Fill with white background (in case GIF has transparency)
                            graphics.Clear(Color.White);
                            
                            // Draw the GIF frame onto the bitmap
                            graphics.DrawImage(gifImage, 0, 0, gifImage.Width, gifImage.Height);
                        }

                        // Set up JPEG encoder with quality settings
                        var jpegCodec = ImageCodecInfo.GetImageEncoders()
                            .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

                        if (jpegCodec == null)
                        {
                            throw new NotSupportedException("JPEG encoder not found");
                        }

                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);

                        // Save as JPEG
                        bitmap.Save(jpegFilePath, jpegCodec, encoderParams);
                    }
                }

                Console.WriteLine($"Successfully converted GIF to JPEG: {Path.GetFileName(gifFilePath)} -> {Path.GetFileName(jpegFilePath)}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting GIF to JPEG: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Convert GIF to JPEG format asynchronously
        /// </summary>
        /// <param name="gifFilePath">Path to the source GIF file</param>
        /// <param name="jpegFilePath">Path where the JPEG file will be saved</param>
        /// <param name="quality">JPEG quality (1-100, default 90)</param>
        /// <param name="frameIndex">Frame index to extract from GIF (default 0 = first frame)</param>
        /// <returns>Task that returns true if conversion was successful</returns>
        public static async Task<bool> ConvertGifToJpegAsync(string gifFilePath, string jpegFilePath, int quality = 90, int frameIndex = 0)
        {
            return await Task.Run(() => ConvertGifToJpeg(gifFilePath, jpegFilePath, quality, frameIndex));
        }

        /// <summary>
        /// Extract all frames from an animated GIF and save as separate JPEG files
        /// </summary>
        /// <param name="gifFilePath">Path to the source GIF file</param>
        /// <param name="outputDirectory">Directory where JPEG frames will be saved</param>
        /// <param name="quality">JPEG quality (1-100, default 90)</param>
        /// <param name="fileNamePrefix">Prefix for output files (default "frame")</param>
        /// <returns>Array of created JPEG file paths</returns>
        public static string[] ExtractGifFramesToJpeg(string gifFilePath, string outputDirectory, int quality = 90, string fileNamePrefix = "frame")
        {
            try
            {
                if (!File.Exists(gifFilePath))
                {
                    throw new FileNotFoundException($"GIF file not found: {gifFilePath}");
                }

                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var outputFiles = new List<string>();

                using (var gifImage = Image.FromFile(gifFilePath))
                {
                    int frameCount = gifImage.GetFrameCount(FrameDimension.Time);
                    Console.WriteLine($"Extracting {frameCount} frames from GIF: {Path.GetFileName(gifFilePath)}");

                    for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                    {
                        gifImage.SelectActiveFrame(FrameDimension.Time, frameIndex);

                        var outputFileName = $"{fileNamePrefix}_{frameIndex:D4}.jpg";
                        var outputPath = Path.Combine(outputDirectory, outputFileName);

                        using (var bitmap = new Bitmap(gifImage.Width, gifImage.Height, PixelFormat.Format24bppRgb))
                        {
                            using (var graphics = Graphics.FromImage(bitmap))
                            {
                                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                                graphics.Clear(Color.White);
                                graphics.DrawImage(gifImage, 0, 0, gifImage.Width, gifImage.Height);
                            }

                            var jpegCodec = ImageCodecInfo.GetImageEncoders()
                                .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

                            var encoderParams = new EncoderParameters(1);
                            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);

                            bitmap.Save(outputPath, jpegCodec, encoderParams);
                            outputFiles.Add(outputPath);
                        }
                    }
                }

                Console.WriteLine($"Successfully extracted {outputFiles.Count} frames to: {outputDirectory}");
                return outputFiles.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting GIF frames: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Extract all frames from an animated GIF and save as separate JPEG files asynchronously
        /// </summary>
        /// <param name="gifFilePath">Path to the source GIF file</param>
        /// <param name="outputDirectory">Directory where JPEG frames will be saved</param>
        /// <param name="quality">JPEG quality (1-100, default 90)</param>
        /// <param name="fileNamePrefix">Prefix for output files (default "frame")</param>
        /// <returns>Task that returns array of created JPEG file paths</returns>
        public static async Task<string[]> ExtractGifFramesToJpegAsync(string gifFilePath, string outputDirectory, int quality = 90, string fileNamePrefix = "frame")
        {
            return await Task.Run(() => ExtractGifFramesToJpeg(gifFilePath, outputDirectory, quality, fileNamePrefix));
        }

        /// <summary>
        /// Get detailed frame duration information for each frame in a GIF
        /// </summary>
        /// <param name="gifFilePath">Path to the GIF file</param>
        /// <returns>Array of frame durations in milliseconds</returns>
        public static int[] GetGifFrameDurations(string gifFilePath)
        {
            try
            {
                if (!File.Exists(gifFilePath))
                {
                    throw new FileNotFoundException($"GIF file not found: {gifFilePath}");
                }

                using (var gifImage = Image.FromFile(gifFilePath))
                {
                    int frameCount = gifImage.GetFrameCount(FrameDimension.Time);
                    var frameDelays = new int[frameCount];

                    try
                    {
                        // PropertyTagFrameDelay (0x5100) contains frame delays in 1/100th of a second
                        var delayProperty = gifImage.GetPropertyItem(0x5100);
                        if (delayProperty?.Value != null && delayProperty.Value.Length >= frameCount * 4)
                        {
                            for (int i = 0; i < frameCount; i++)
                            {
                                // Each delay is stored as a 32-bit integer (4 bytes)
                                // The value is in 1/100th of a second, so multiply by 10 to get milliseconds
                                int delayInCentiseconds = BitConverter.ToInt32(delayProperty.Value, i * 4);
                                
                                // Convert to milliseconds (minimum 10ms to avoid too fast animation)
                                int delayInMilliseconds = Math.Max(delayInCentiseconds * 10, 10);
                                frameDelays[i] = delayInMilliseconds;
                            }
                        }
                        else
                        {
                            // Fallback: use default delay if property is not available
                            for (int i = 0; i < frameCount; i++)
                            {
                                frameDelays[i] = 100; // Default 100ms per frame
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not read frame delays, using defaults: {ex.Message}");
                        // Fallback: use default delays
                        for (int i = 0; i < frameCount; i++)
                        {
                            frameDelays[i] = 100; // Default 100ms per frame
                        }
                    }

                    return frameDelays;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting GIF frame durations: {ex.Message}");
                return Array.Empty<int>();
            }
        }

        /// <summary>
        /// Get information about a GIF file with improved frame duration extraction
        /// </summary>
        /// <param name="gifFilePath">Path to the GIF file</param>
        /// <returns>GIF information object</returns>
        public static GifInfo? GetGifInfo(string gifFilePath)
        {
            try
            {
                if (!File.Exists(gifFilePath))
                {
                    throw new FileNotFoundException($"GIF file not found: {gifFilePath}");
                }

                using (var gifImage = Image.FromFile(gifFilePath))
                {
                    int frameCount = gifImage.GetFrameCount(FrameDimension.Time);
                    var fileInfo = new FileInfo(gifFilePath);

                    // Get frame durations using the improved method
                    var frameDelays = GetGifFrameDurations(gifFilePath);

                    // Get loop count if available
                    int loopCount = GetGifLoopCount(gifImage);

                    return new GifInfo
                    {
                        FileName = Path.GetFileName(gifFilePath),
                        FilePath = gifFilePath,
                        FileSize = fileInfo.Length,
                        Width = gifImage.Width,
                        Height = gifImage.Height,
                        FrameCount = frameCount,
                        FrameDelays = frameDelays,
                        IsAnimated = frameCount > 1,
                        LoopCount = loopCount
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting GIF info: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the loop count of a GIF animation
        /// </summary>
        /// <param name="gifImage">The GIF image</param>
        /// <returns>Loop count (0 = infinite loop, -1 = unknown)</returns>
        private static int GetGifLoopCount(Image gifImage)
        {
            try
            {
                // PropertyTagLoopCount (0x5101) contains the loop count
                var loopProperty = gifImage.GetPropertyItem(0x5101);
                if (loopProperty?.Value != null && loopProperty.Value.Length >= 2)
                {
                    return BitConverter.ToUInt16(loopProperty.Value, 0);
                }
            }
            catch
            {
                // Property not available or error reading it
            }

            return -1; // Unknown loop count
        }

        /// <summary>
        /// Get detailed frame information for each frame in a GIF
        /// </summary>
        /// <param name="gifFilePath">Path to the GIF file</param>
        /// <returns>Array of frame information objects</returns>
        public static GifFrameInfo[] GetGifFrameDetails(string gifFilePath)
        {
            try
            {
                if (!File.Exists(gifFilePath))
                {
                    throw new FileNotFoundException($"GIF file not found: {gifFilePath}");
                }

                using (var gifImage = Image.FromFile(gifFilePath))
                {
                    int frameCount = gifImage.GetFrameCount(FrameDimension.Time);
                    var frameInfos = new GifFrameInfo[frameCount];
                    var frameDelays = GetGifFrameDurations(gifFilePath);

                    for (int i = 0; i < frameCount; i++)
                    {
                        gifImage.SelectActiveFrame(FrameDimension.Time, i);

                        frameInfos[i] = new GifFrameInfo
                        {
                            FrameIndex = i,
                            Duration = i < frameDelays.Length ? frameDelays[i] : 100,
                            Width = gifImage.Width,
                            Height = gifImage.Height
                        };
                    }

                    return frameInfos;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting GIF frame details: {ex.Message}");
                return Array.Empty<GifFrameInfo>();
            }
        }

        /// <summary>
        /// Convert multiple image formats to JPEG
        /// </summary>
        /// <param name="inputFilePath">Path to the source image file</param>
        /// <param name="jpegFilePath">Path where the JPEG file will be saved</param>
        /// <param name="quality">JPEG quality (1-100, default 90)</param>
        /// <returns>True if conversion was successful</returns>
        public static bool ConvertToJpeg(string inputFilePath, string jpegFilePath, int quality = 90)
        {
            try
            {
                if (!File.Exists(inputFilePath))
                {
                    throw new FileNotFoundException($"Input file not found: {inputFilePath}");
                }

                string extension = Path.GetExtension(inputFilePath).ToLowerInvariant();
                
                // Handle GIF files specially to extract first frame
                if (extension == ".gif")
                {
                    return ConvertGifToJpeg(inputFilePath, jpegFilePath, quality, 0);
                }

                // Handle other image formats
                var supportedFormats = new[] { ".png", ".bmp", ".tiff", ".tif", ".webp" };
                if (!supportedFormats.Contains(extension))
                {
                    throw new NotSupportedException($"Unsupported image format: {extension}");
                }

                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(jpegFilePath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                using (var image = Image.FromFile(inputFilePath))
                using (var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                        graphics.Clear(Color.White);
                        graphics.DrawImage(image, 0, 0, image.Width, image.Height);
                    }

                    var jpegCodec = ImageCodecInfo.GetImageEncoders()
                        .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

                    if (jpegCodec == null)
                    {
                        throw new NotSupportedException("JPEG encoder not found");
                    }

                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);

                    bitmap.Save(jpegFilePath, jpegCodec, encoderParams);
                }

                Console.WriteLine($"Successfully converted {extension.ToUpper()} to JPEG: {Path.GetFileName(inputFilePath)} -> {Path.GetFileName(jpegFilePath)}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting to JPEG: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Create a BitmapImage from a file path for WPF display
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <returns>BitmapImage for WPF binding</returns>
        public static BitmapImage? LoadBitmapImage(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    return null;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading bitmap image: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Information about a GIF file
    /// </summary>
    public class GifInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int FrameCount { get; set; }
        public int[] FrameDelays { get; set; } = Array.Empty<int>();
        public bool IsAnimated { get; set; }
        public int LoopCount { get; set; } = -1; // -1 = unknown, 0 = infinite loop

        public double TotalDurationSeconds => FrameDelays.Sum() / 1000.0;

        /// <summary>
        /// Get the duration of a specific frame
        /// </summary>
        /// <param name="frameIndex">Frame index (0-based)</param>
        /// <returns>Duration in milliseconds</returns>
        public int GetFrameDuration(int frameIndex)
        {
            if (frameIndex >= 0 && frameIndex < FrameDelays.Length)
            {
                return FrameDelays[frameIndex];
            }
            return 100; // Default 100ms
        }

        /// <summary>
        /// Get the cumulative time at which a specific frame starts
        /// </summary>
        /// <param name="frameIndex">Frame index (0-based)</param>
        /// <returns>Start time in milliseconds</returns>
        public int GetFrameStartTime(int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= FrameDelays.Length)
            {
                return 0;
            }

            int startTime = 0;
            for (int i = 0; i < frameIndex; i++)
            {
                startTime += FrameDelays[i];
            }
            return startTime;
        }

        public override string ToString()
        {
            string loopInfo = LoopCount switch
            {
                -1 => "unknown loops",
                0 => "infinite loop",
                _ => $"{LoopCount} loops"
            };

            return $"{FileName} - {Width}x{Height}, {FrameCount} frames, {FileSize / 1024.0:F1} KB" +
                   (IsAnimated ? $", {TotalDurationSeconds:F1}s duration, {loopInfo}" : "");
        }
    }

    /// <summary>
    /// Information about a single frame in a GIF
    /// </summary>
    public class GifFrameInfo
    {
        public int FrameIndex { get; set; }
        public int Duration { get; set; } // Duration in milliseconds
        public int Width { get; set; }
        public int Height { get; set; }

        public override string ToString()
        {
            return $"Frame {FrameIndex}: {Duration}ms, {Width}x{Height}";
        }
    }
}