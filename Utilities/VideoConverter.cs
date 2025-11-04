using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Arguments;
using FFMpegCore.Enums;

namespace CMDevicesManager.Utilities
{
    /// <summary>
    /// Utility class for video format conversion and frame extraction
    /// </summary>
    public static class VideoConverter
    {
        /// <summary>
        /// Extract all frames from an MP4 video and save as JPEG files
        /// </summary>
        /// <param name="mp4FilePath">Path to the source MP4 file</param>
        /// <param name="outputDirectory">Directory where JPEG frames will be saved</param>
        /// <param name="quality">JPEG quality (1-100, default 90)</param>
        /// <param name="fileNamePrefix">Prefix for output files (default "frame")</param>
        /// <returns>Array of created JPEG file paths</returns>
        public static async Task<string[]> ExtractMp4FramesToJpegAsync(string mp4FilePath, string outputDirectory, int quality = 90, string fileNamePrefix = "frame")
        {
            try
            {
                if (!File.Exists(mp4FilePath))
                {
                    throw new FileNotFoundException($"MP4 file not found: {mp4FilePath}");
                }

                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var outputFiles = new List<string>();

                // Get video info
                var videoInfo = await FFProbe.AnalyseAsync(mp4FilePath);
                var frameRate = videoInfo.PrimaryVideoStream?.FrameRate ?? 30;
                var duration = videoInfo.Duration;
                var totalFrames = (int)(duration.TotalSeconds * frameRate);

                Console.WriteLine($"Extracting frames from MP4: {Path.GetFileName(mp4FilePath)}");
                Console.WriteLine($"Duration: {duration}, Frame Rate: {frameRate:F2} fps, Estimated Frames: {totalFrames}");

                // Extract frames using FFmpeg
                var outputPattern = Path.Combine(outputDirectory, $"{fileNamePrefix}_%04d.jpg");
                
                await FFMpegArguments
                    .FromFileInput(mp4FilePath)
                    .OutputToFile(outputPattern, overwrite: true, options => options
                        .WithVideoCodec("Mjpeg")
                        .WithArgument(new CustomArgument($"-q:v {100 - quality}"))) // FFmpeg quality is inverse (lower = better)
                    .ProcessAsynchronously();

                // Collect the generated files
                var files = Directory.GetFiles(outputDirectory, $"{fileNamePrefix}_*.jpg");
                Array.Sort(files); // Ensure proper order
                outputFiles.AddRange(files);

                Console.WriteLine($"Successfully extracted {outputFiles.Count} frames to: {outputDirectory}");
                return outputFiles.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting MP4 frames: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Extract frames at specific time intervals from an MP4 video
        /// </summary>
        /// <param name="mp4FilePath">Path to the source MP4 file</param>
        /// <param name="outputDirectory">Directory where JPEG frames will be saved</param>
        /// <param name="intervalSeconds">Interval between frames in seconds</param>
        /// <param name="quality">JPEG quality (1-100, default 90)</param>
        /// <param name="fileNamePrefix">Prefix for output files</param>
        /// <returns>Array of created JPEG file paths</returns>
        public static async Task<string[]> ExtractMp4FramesAtIntervalAsync(string mp4FilePath, string outputDirectory, double intervalSeconds = 1.0, int quality = 90, string fileNamePrefix = "frame")
        {
            try
            {
                if (!File.Exists(mp4FilePath))
                {
                    throw new FileNotFoundException($"MP4 file not found: {mp4FilePath}");
                }

                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var outputFiles = new List<string>();

                // Get video info
                var videoInfo = await FFProbe.AnalyseAsync(mp4FilePath);
                var duration = videoInfo.Duration;
                
                Console.WriteLine($"Extracting frames at {intervalSeconds}s intervals from: {Path.GetFileName(mp4FilePath)}");
                Console.WriteLine($"Video duration: {duration}");

                // Calculate number of frames to extract
                int frameCount = (int)(duration.TotalSeconds / intervalSeconds) + 1;
                
                for (int i = 0; i < frameCount; i++)
                {
                    double timeSeconds = i * intervalSeconds;
                    if (timeSeconds > duration.TotalSeconds) break;

                    var outputFileName = $"{fileNamePrefix}_{i:D4}.jpg";
                    var outputPath = Path.Combine(outputDirectory, outputFileName);

                    await FFMpegArguments
                        .FromFileInput(mp4FilePath)
                        .OutputToFile(outputPath, overwrite: true, options => options
                            .Seek(TimeSpan.FromSeconds(timeSeconds))
                            .WithCustomArgument("-vframes 1")
                            .WithVideoCodec("Mjpeg")
                            .WithArgument(new CustomArgument($"-q:v {100 - quality}")))
                        .ProcessAsynchronously();

                    if (File.Exists(outputPath))
                    {
                        outputFiles.Add(outputPath);
                    }
                }

                Console.WriteLine($"Successfully extracted {outputFiles.Count} frames at intervals to: {outputDirectory}");
                return outputFiles.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting MP4 frames at intervals: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Get video information from an MP4 file
        /// </summary>
        /// <param name="mp4FilePath">Path to the MP4 file</param>
        /// <returns>Video information object</returns>
        public static async Task<VideoInfo?> GetMp4InfoAsync(string mp4FilePath)
        {
            try
            {
                if (!File.Exists(mp4FilePath))
                {
                    throw new FileNotFoundException($"MP4 file not found: {mp4FilePath}");
                }

                var mediaInfo = await FFProbe.AnalyseAsync(mp4FilePath);
                var videoStream = mediaInfo.PrimaryVideoStream;
                var audioStream = mediaInfo.PrimaryAudioStream;

                if (videoStream == null)
                {
                    Console.WriteLine("No video stream found in the file");
                    return null;
                }

                var fileInfo = new FileInfo(mp4FilePath);

                return new VideoInfo
                {
                    FileName = Path.GetFileName(mp4FilePath),
                    FilePath = mp4FilePath,
                    FileSize = fileInfo.Length,
                    Duration = mediaInfo.Duration,
                    Width = videoStream.Width,
                    Height = videoStream.Height,
                    FrameRate = videoStream.FrameRate,
                    BitRate = videoStream.BitRate,
                    CodecName = videoStream.CodecName,
                    HasAudio = audioStream != null,
                    AudioCodec = audioStream?.CodecName,
                    TotalFrames = (int)(mediaInfo.Duration.TotalSeconds * videoStream.FrameRate)
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting MP4 info: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Convert MP4 to a sequence of images with frame timing information
        /// </summary>
        /// <param name="mp4FilePath">Path to the source MP4 file</param>
        /// <param name="outputDirectory">Directory where frames will be saved</param>
        /// <param name="quality">JPEG quality (1-100, default 90)</param>
        /// <returns>Array of frame information with timing</returns>
        public static async Task<VideoFrameInfo[]> ExtractMp4FramesWithTimingAsync(string mp4FilePath, string outputDirectory, int quality = 90)
        {
            try
            {
                var frameFiles = await ExtractMp4FramesToJpegAsync(mp4FilePath, outputDirectory, quality);
                var videoInfo = await GetMp4InfoAsync(mp4FilePath);

                if (videoInfo == null || frameFiles.Length == 0)
                {
                    return Array.Empty<VideoFrameInfo>();
                }

                var frameInfos = new List<VideoFrameInfo>();
                double frameDuration = 1000.0 / videoInfo.FrameRate; // Duration per frame in milliseconds

                for (int i = 0; i < frameFiles.Length; i++)
                {
                    frameInfos.Add(new VideoFrameInfo
                    {
                        FrameIndex = i,
                        FilePath = frameFiles[i],
                        TimeStampMs = i * frameDuration,
                        DurationMs = frameDuration,
                        Width = videoInfo.Width,
                        Height = videoInfo.Height
                    });
                }

                return frameInfos.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting MP4 frames with timing: {ex.Message}");
                return Array.Empty<VideoFrameInfo>();
            }
        }

        /// <summary>
        /// Extract all frames from an MP4 video and stream frame data in real-time as JPEG format
        /// </summary>
        /// <param name="mp4FilePath">Path to the source MP4 file</param>
        /// <param name="quality">JPEG quality (1-100, default 90)</param>
        /// <param name="cancellationToken">Cancellation token to stop the extraction</param>
        /// <returns>Async enumerable of JPEG frame data</returns>
        public static async IAsyncEnumerable<VideoFrameData> ExtractMp4FramesToJpegRealTimeAsync(
            string mp4FilePath,
            int quality = 90,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!File.Exists(mp4FilePath))
            {
                throw new FileNotFoundException($"MP4 file not found: {mp4FilePath}");
            }

            // Get video info
            var videoInfo = await FFProbe.AnalyseAsync(mp4FilePath);
            var videoStream = videoInfo.PrimaryVideoStream;

            if (videoStream == null)
            {
                yield break;
            }

            var frameRate = videoStream.FrameRate;
            var duration = videoInfo.Duration;
            var frameDuration = 1000.0 / frameRate;
            var totalFrames = (int)(duration.TotalSeconds * frameRate);

            Console.WriteLine($"Starting real-time frame extraction from: {Path.GetFileName(mp4FilePath)}");
            Console.WriteLine($"Duration: {duration}, Frame Rate: {frameRate:F2} fps, Total Frames: {totalFrames}");

            // Create temporary directory for frame extraction
            string tempDir = Path.Combine(Path.GetTempPath(), $"VideoFrames_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var outputPattern = Path.Combine(tempDir, "frame_%06d.jpg");

                // 使用 FFmpeg 多线程参数批量提取所有帧
                var extractTask = Task.Run(async () =>
                {
                    await FFMpegArguments
                        .FromFileInput(mp4FilePath)
                        .OutputToFile(outputPattern, overwrite: true, options => options
                            .WithCustomArgument($"-threads {Environment.ProcessorCount}") // 多线程
                            .WithCustomArgument("-vsync 0") // 不丢帧
                            .WithVideoCodec("mjpeg")
                            .WithArgument(new CustomArgument($"-q:v {100 - quality}")))
                        .ProcessAsynchronously();
                }, cancellationToken);

                // 等待一小段时间让 FFmpeg 开始生成文件
                await Task.Delay(200, cancellationToken);

                int frameIndex = 0;
                var framePath = Path.Combine(tempDir, $"frame_{frameIndex + 1:D6}.jpg");

                // 边生成边读取
                while (frameIndex < totalFrames && !cancellationToken.IsCancellationRequested)
                {
                    framePath = Path.Combine(tempDir, $"frame_{frameIndex + 1:D6}.jpg");

                    // 等待文件生成
                    int retries = 0;
                    while (!File.Exists(framePath) && retries < 100) // 最多等待10秒
                    {
                        await Task.Delay(100, cancellationToken);
                        retries++;

                        // 如果提取任务已完成但文件不存在，说明已经没有更多帧了
                        if (extractTask.IsCompleted && !File.Exists(framePath))
                        {
                            break;
                        }
                    }

                    if (!File.Exists(framePath))
                    {
                        break;
                    }

                    // 等待文件写入完成
                    byte[]? frameData = null;
                    retries = 0;
                    while (frameData == null && retries < 10)
                    {
                        try
                        {
                            frameData = await File.ReadAllBytesAsync(framePath, cancellationToken);
                        }
                        catch (IOException)
                        {
                            await Task.Delay(50, cancellationToken);
                            retries++;
                        }
                    }

                    if (frameData != null && frameData.Length > 0)
                    {
                        yield return new VideoFrameData
                        {
                            FrameIndex = frameIndex,
                            JpegData = frameData,
                            TimeStampMs = frameIndex * frameDuration,
                            DurationMs = frameDuration,
                            Width = videoStream.Width,
                            Height = videoStream.Height
                        };

                        // 读取后立即删除以节省空间
                        try
                        {
                            File.Delete(framePath);
                        }
                        catch { /* Ignore cleanup errors */ }
                    }

                    frameIndex++;
                }

                // 等待 FFmpeg 完成
                try
                {
                    await extractTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                }
            }
            finally
            {
                // Clean up temporary directory
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception cleanupEx)
                {
                    Console.WriteLine($"Warning: Could not clean up temporary directory: {cleanupEx.Message}");
                }
            }
        }
        /// <summary>
        /// Extract all frames using hardware acceleration if available
        /// </summary>
        public static async IAsyncEnumerable<VideoFrameData> ExtractMp4FramesToJpegRealTimeWithHWAccelAsync(
            string mp4FilePath,
            int quality = 90,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!File.Exists(mp4FilePath))
            {
                throw new FileNotFoundException($"MP4 file not found: {mp4FilePath}");
            }

            var videoInfo = await FFProbe.AnalyseAsync(mp4FilePath);
            var videoStream = videoInfo.PrimaryVideoStream;

            if (videoStream == null)
            {
                yield break;
            }

            var frameRate = videoStream.FrameRate;
            var duration = videoInfo.Duration;
            var frameDuration = 1000.0 / frameRate;
            var totalFrames = (int)(duration.TotalSeconds * frameRate);

            string tempDir = Path.Combine(Path.GetTempPath(), $"VideoFrames_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var outputPattern = Path.Combine(tempDir, "frame_%06d.jpg");

                var extractTask = Task.Run(async () =>
                {
                    await FFMpegArguments
                        .FromFileInput(mp4FilePath, true, options => options
                            .WithHardwareAcceleration() // 硬件加速
                            .WithCustomArgument($"-threads {Environment.ProcessorCount}"))
                        .OutputToFile(outputPattern, overwrite: true, options => options
                            .WithCustomArgument("-vsync 0")
                            .WithVideoCodec("mjpeg")
                            .WithArgument(new CustomArgument($"-q:v {100 - quality}")))
                        .ProcessAsynchronously();
                }, cancellationToken);

                await Task.Delay(200, cancellationToken);

                int frameIndex = 0;
                while (frameIndex < totalFrames && !cancellationToken.IsCancellationRequested)
                {
                    var framePath = Path.Combine(tempDir, $"frame_{frameIndex + 1:D6}.jpg");

                    int retries = 0;
                    while (!File.Exists(framePath) && retries < 100)
                    {
                        await Task.Delay(100, cancellationToken);
                        retries++;

                        if (extractTask.IsCompleted && !File.Exists(framePath))
                        {
                            break;
                        }
                    }

                    if (!File.Exists(framePath))
                    {
                        break;
                    }

                    byte[]? frameData = null;
                    retries = 0;
                    while (frameData == null && retries < 10)
                    {
                        try
                        {
                            frameData = await File.ReadAllBytesAsync(framePath, cancellationToken);
                        }
                        catch (IOException)
                        {
                            await Task.Delay(50, cancellationToken);
                            retries++;
                        }
                    }

                    if (frameData != null && frameData.Length > 0)
                    {
                        yield return new VideoFrameData
                        {
                            FrameIndex = frameIndex,
                            JpegData = frameData,
                            TimeStampMs = frameIndex * frameDuration,
                            DurationMs = frameDuration,
                            Width = videoStream.Width,
                            Height = videoStream.Height
                        };

                        try
                        {
                            File.Delete(framePath);
                        }
                        catch { }
                    }

                    frameIndex++;
                }

                try
                {
                    await extractTask;
                }
                catch (OperationCanceledException) { }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception cleanupEx)
                {
                    Console.WriteLine($"Warning: Could not clean up temporary directory: {cleanupEx.Message}");
                }
            }
        }
        /// <summary>
        /// Extract frames from an MP4 video with real-time progress reporting
        /// </summary>
        /// <param name="mp4FilePath">Path to the source MP4 file</param>
        /// <param name="quality">JPEG quality (1-100, default 90)</param>
        /// <param name="progressCallback">Callback for progress reporting (frameIndex, totalFrames, frameData)</param>
        /// <param name="cancellationToken">Cancellation token to stop the extraction</param>
        /// <returns>Array of all extracted frame data</returns>
        public static async Task<VideoFrameData[]> ExtractMp4FramesToJpegWithProgressAsync(
            string mp4FilePath,
            int quality = 90,
            Action<int, int, VideoFrameData>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            var frames = new List<VideoFrameData>();

            await foreach (var frame in ExtractMp4FramesToJpegRealTimeWithHWAccelAsync(mp4FilePath, quality, cancellationToken))
            {
                frames.Add(frame);
                
                // Report progress
                var videoInfo = await FFProbe.AnalyseAsync(mp4FilePath);
                var totalFrames = (int)(videoInfo.Duration.TotalSeconds * (videoInfo.PrimaryVideoStream?.FrameRate ?? 30));
                progressCallback?.Invoke(frame.FrameIndex + 1, totalFrames, frame);
            }

            return frames.ToArray();
        }
    }

    /// <summary>
    /// Information about a video file
    /// </summary>
    public class VideoInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public TimeSpan Duration { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
        public long BitRate { get; set; }
        public string CodecName { get; set; } = string.Empty;
        public bool HasAudio { get; set; }
        public string? AudioCodec { get; set; }
        public int TotalFrames { get; set; }

        public override string ToString()
        {
            return $"{FileName} - {Width}x{Height}, {Duration:mm\\:ss}, {FrameRate:F1}fps, {FileSize / 1024.0 / 1024.0:F1}MB, {CodecName}";
        }
    }

    /// <summary>
    /// Information about a single frame in a video
    /// </summary>
    public class VideoFrameInfo
    {
        public int FrameIndex { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public double TimeStampMs { get; set; }
        public double DurationMs { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public TimeSpan TimeStamp => TimeSpan.FromMilliseconds(TimeStampMs);

        public override string ToString()
        {
            return $"Frame {FrameIndex}: {TimeStamp:mm\\:ss\\.fff}, {Width}x{Height}, {Path.GetFileName(FilePath)}";
        }
    }

    /// <summary>
    /// Information about a single frame with JPEG data
    /// </summary>
    public class VideoFrameData
    {
        public int FrameIndex { get; set; }
        public byte[] JpegData { get; set; } = Array.Empty<byte>();
        public double TimeStampMs { get; set; }
        public double DurationMs { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public TimeSpan TimeStamp => TimeSpan.FromMilliseconds(TimeStampMs);

        /// <summary>
        /// Get the size of the JPEG data in bytes
        /// </summary>
        public int DataSize => JpegData.Length;

        public override string ToString()
        {
            return $"Frame {FrameIndex}: {TimeStamp:mm\\:ss\\.fff}, {Width}x{Height}, {DataSize / 1024.0:F1}KB";
        }
    }
}