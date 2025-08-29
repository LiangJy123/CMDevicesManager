using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Threading;
using System.Timers; // Add this for Timer

using HID.DisplayController;
using HidApi;
using Path = System.IO.Path;
using Color = System.Windows.Media.Color;

using CMDevicesManager.Utilities;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for DevicePage.xaml
    /// </summary>
    public partial class DevicePage : Page
    {
        private MultiDeviceManager? _multiDeviceManager;
        private bool _isStreamingInProgress = false;
        private int _streamedImageCount = 0;
        private CancellationTokenSource? _streamingCancellationSource;

        // Keep-alive timer functionality
        private System.Timers.Timer? _keepAliveTimer;
        private readonly object _keepAliveTimerLock = new object();
        private bool _keepAliveEnabled = false;

        public DevicePage()
        {
            InitializeComponent();
            // ExtractGifFramesExample();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _multiDeviceManager = new MultiDeviceManager(0x2516, 0x0228);

                // Set up event handlers
                _multiDeviceManager.ControllerAdded += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // Add the DeviceInfo object to the list
                            if (!DevicesListBox.Items.Cast<DeviceInfo>().Any(d => d.Path == ev.Device.Path))
                            {
                                DevicesListBox.Items.Add(ev.Device);
                                HideErrorMessage(); // Hide error message when devices are successfully added
                                UpdateButtonStates(); // Update button states when devices change
                                UpdateActiveDeviceCount(); // Update the device count in image viewer
                            }
                        }
                        catch (Exception ex)
                        {
                            ShowErrorMessage($"Failed to add device: {ex.Message}");
                        }
                    });
                };

                _multiDeviceManager.ControllerRemoved += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // Remove the DeviceInfo object from the list
                            var deviceToRemove = DevicesListBox.Items.Cast<DeviceInfo>()
                                .FirstOrDefault(d => d.Path == ev.Device.Path);

                            if (deviceToRemove != null)
                            {
                                DevicesListBox.Items.Remove(deviceToRemove);
                                UpdateButtonStates(); // Update button states when devices change
                                UpdateActiveDeviceCount(); // Update the device count in image viewer
                            }
                        }
                        catch (Exception ex)
                        {
                            ShowErrorMessage($"Failed to remove device: {ex.Message}");
                        }
                    });
                };

                _multiDeviceManager.ControllerError += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ShowErrorMessage($"Device Error - {ev.Device.ProductString}: {ev.Exception.Message}");
                    });
                };

                // Must call StartMonitoring to begin detection
                _multiDeviceManager.StartMonitoring();

                // Populate existing devices
                var activeControllers = _multiDeviceManager.GetActiveControllers();
                foreach (var controller in activeControllers)
                {
                    // Add DeviceInfo objects to the list
                    if (!DevicesListBox.Items.Cast<DeviceInfo>().Any(d => d.Path == controller.DeviceInfo.Path))
                    {
                        DevicesListBox.Items.Add(controller.DeviceInfo);
                    }
                }

                // Update button states initially
                UpdateButtonStates();
                UpdateActiveDeviceCount();
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Failed to initialize device manager: {ex.Message}");
            }
        }

        /// <summary>
        /// Start the keep-alive timer to send packets every 4 seconds
        /// </summary>
        private void StartKeepAliveTimer()
        {
            lock (_keepAliveTimerLock)
            {
                if (_keepAliveTimer != null)
                {
                    _keepAliveTimer.Stop();
                    _keepAliveTimer.Dispose();
                }

                _keepAliveEnabled = true;
                _keepAliveTimer = new System.Timers.Timer(4000); // 4 seconds interval
                _keepAliveTimer.Elapsed += OnKeepAliveTimerElapsed;
                _keepAliveTimer.AutoReset = true;
                _keepAliveTimer.Start();

                Console.WriteLine("Keep-alive timer started (4-second intervals)");
            }
        }

        /// <summary>
        /// Stop the keep-alive timer
        /// </summary>
        private void StopKeepAliveTimer()
        {
            lock (_keepAliveTimerLock)
            {
                _keepAliveEnabled = false;

                if (_keepAliveTimer != null)
                {
                    _keepAliveTimer.Stop();
                    _keepAliveTimer.Dispose();
                    _keepAliveTimer = null;
                    Console.WriteLine("Keep-alive timer stopped");
                }
            }
        }

        /// <summary>
        /// Handle keep-alive timer elapsed event
        /// </summary>
        private async void OnKeepAliveTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (!_keepAliveEnabled || _multiDeviceManager == null)
            {
                return;
            }

            try
            {
                // Send keep-alive packet to all devices
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var results = await _multiDeviceManager.SendKeepAliveOnAllDevices(timestamp);

                int successCount = results.Values.Count(success => success);
                Console.WriteLine($"Keep-alive sent to {successCount}/{results.Count} devices at {DateTime.Now:HH:mm:ss}");

                // Optionally update UI to show keep-alive status
                Dispatcher.Invoke(() =>
                {
                    // You could update a status indicator here if needed
                    // For example, briefly flash the streaming indicator or update a timestamp
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending keep-alive: {ex.Message}");
            }
        }

        /// <summary>
        /// Send an immediate keep-alive packet to all devices
        /// </summary>
        private async Task SendKeepAlivePacket()
        {
            if (_multiDeviceManager == null)
            {
                return;
            }

            try
            {
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var results = await _multiDeviceManager.SendKeepAliveOnAllDevices(timestamp);

                int successCount = results.Values.Count(success => success);
                Console.WriteLine($"Manual keep-alive sent to {successCount}/{results.Count} devices");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending manual keep-alive: {ex.Message}");
            }
        }

        /// <summary>
        /// Update the active device count in the image viewer
        /// </summary>
        private void UpdateActiveDeviceCount()
        {
            ActiveDeviceCount.Text = DevicesListBox.Items.Count.ToString();
        }

        /// <summary>
        /// Update streaming image viewer with current image
        /// </summary>
        /// <param name="imagePath">Path to the current image being streamed</param>
        private void UpdateStreamingImageViewer(string imagePath)
        {
            try
            {
                if (File.Exists(imagePath))
                {
                    // Create BitmapImage to display the current streaming image
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load immediately to avoid file locks
                    bitmap.EndInit();
                    bitmap.Freeze(); // Make thread-safe

                    // Update UI on the UI thread
                    Dispatcher.Invoke(() =>
                    {
                        StreamingImageViewer.Source = bitmap;
                        StreamingImageViewer.Visibility = Visibility.Visible;
                        PlaceholderContent.Visibility = Visibility.Collapsed;
                        LoadingIndicator.Visibility = Visibility.Collapsed;

                        // Update image info
                        CurrentImageName.Text = Path.GetFileName(imagePath);
                        ImageDimensions.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight}";

                        // Update image counter
                        _streamedImageCount++;
                        ImageCounter.Text = _streamedImageCount.ToString();
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update streaming image viewer: {ex.Message}");
            }
        }

        /// <summary>
        /// Show streaming status indicator
        /// </summary>
        /// <param name="isStreaming">Whether streaming is active</param>
        private void ShowStreamingStatus(bool isStreaming)
        {
            Dispatcher.Invoke(() =>
            {
                if (isStreaming)
                {
                    StreamingStatusIndicator.Visibility = Visibility.Visible;
                    LoadingIndicator.Visibility = Visibility.Visible;
                    PlaceholderContent.Visibility = Visibility.Collapsed;
                }
                else
                {
                    StreamingStatusIndicator.Visibility = Visibility.Collapsed;
                    LoadingIndicator.Visibility = Visibility.Collapsed;

                    // Reset to placeholder if no image is loaded
                    if (StreamingImageViewer.Source == null)
                    {
                        PlaceholderContent.Visibility = Visibility.Visible;
                        StreamingImageViewer.Visibility = Visibility.Collapsed;
                    }
                }
            });
        }

        /// <summary>
        /// Reset the streaming image viewer
        /// </summary>
        private void ResetStreamingImageViewer()
        {
            Dispatcher.Invoke(() =>
            {
                StreamingImageViewer.Source = null;
                StreamingImageViewer.Visibility = Visibility.Collapsed;
                PlaceholderContent.Visibility = Visibility.Visible;
                LoadingIndicator.Visibility = Visibility.Collapsed;
                StreamingStatusIndicator.Visibility = Visibility.Collapsed;

                CurrentImageName.Text = "No image loaded";
                ImageDimensions.Text = "";
                _streamedImageCount = 0;
                ImageCounter.Text = "0";
            });
        }

        /// <summary>
        /// Enhanced realtime streaming with synchronized image viewer and keep-alive timer
        /// </summary>
        private async void RealtimeStreamingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isStreamingInProgress)
            {
                ShowErrorMessage("Streaming operation already in progress. Please wait...");
                return;
            }

            if (_multiDeviceManager == null)
            {
                ShowErrorMessage("Device manager not initialized");
                return;
            }

            var activeControllers = _multiDeviceManager.GetActiveControllers();
            if (activeControllers.Count == 0)
            {
                ShowErrorMessage("No devices connected for realtime streaming");
                return;
            }

            try
            {
                _isStreamingInProgress = true;
                _streamingCancellationSource = new CancellationTokenSource();
                UpdateButtonStates();
                HideErrorMessage();
                ShowStreamingStatus(true);

                // Start the keep-alive timer for automatic 4-second intervals
                StartKeepAliveTimer();

                // Show status message
                ShowStatusMessage($"Starting realtime streaming with keep-alive on {activeControllers.Count} device(s)...");

                // Set up image directory path (you may want to make this configurable)
                string imageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HidProtocol", "resources", "realtime");

                // Alternative paths to check for images
                string[] alternativePaths = {
                    @"E:\github\CMDevicesManager\HidProtocol\resources\realtime",
                    @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources\realtime",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CMDevicesManager", "images")
                };

                // Find a valid image directory
                foreach (string path in alternativePaths)
                {
                    if (Directory.Exists(path))
                    {
                        imageDirectory = path;
                        break;
                    }
                }

                // Execute enhanced realtime streaming with synchronized image viewer
                var results = await _multiDeviceManager.ExecuteOnAllDevices(async controller =>
                {
                    try
                    {
                        bool success = await EnhancedRealTimeDisplayDemo(controller, imageDirectory, 20, _streamingCancellationSource.Token);
                        return success;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Realtime streaming failed for device: {ex.Message}");
                        return false;
                    }
                }, timeout: TimeSpan.FromMinutes(5));

                // Show results
                int successCount = results.Values.Count(success => success);
                if (successCount == results.Count)
                {
                    ShowStatusMessage($"Realtime streaming demo completed successfully on all {successCount} device(s)!");
                }
                else
                {
                    ShowErrorMessage($"Realtime streaming completed with mixed results: {successCount}/{results.Count} devices succeeded");
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Realtime streaming demo failed: {ex.Message}");
            }
            finally
            {
                // Stop the keep-alive timer when streaming ends
                StopKeepAliveTimer();

                _isStreamingInProgress = false;
                ShowStreamingStatus(false);
                UpdateButtonStates();
                _streamingCancellationSource?.Dispose();
                _streamingCancellationSource = null;
            }
        }

        /// <summary>
        /// Enhanced real-time display demo with synchronized image viewer updates and keep-alive support
        /// </summary>
        /// <param name="controller">Display controller</param>
        /// <param name="imageDirectory">Directory containing images</param>
        /// <param name="cycleCount">Number of image cycles</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if demo completed successfully</returns>
        private async Task<bool> EnhancedRealTimeDisplayDemo(DisplayController controller, string? imageDirectory = null, int cycleCount = 5, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("=== Enhanced Real-Time Display Demo with Keep-Alive Support ===");

            try
            {
                List<string> imagePaths = new List<string>();

                // Initialize the images path list
                if (!string.IsNullOrEmpty(imageDirectory) && Directory.Exists(imageDirectory))
                {
                    var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };
                    var files = Directory.GetFiles(imageDirectory)
                                         .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
                                         .ToList();
                    if (files.Count > 0)
                    {
                        Console.WriteLine($"Found {files.Count} images in directory: {imageDirectory}");
                        imagePaths.AddRange(files);
                    }
                    else
                    {
                        Console.WriteLine($"No supported image files found in directory: {imageDirectory}");
                    }
                }
                else
                {
                    Console.WriteLine("No valid image directory provided, using default images.");
                }

                // Ensure device is awake
                var wakeResponse = await controller.SendCmdDisplayInSleepWithResponse(false);
                if (wakeResponse?.IsSuccess != true)
                {
                    Console.WriteLine("Failed to wake device from sleep");
                    return false;
                }
                await Task.Delay(1500); // Wait for device to wake up

                // Step 1: Enable real-time display
                Console.WriteLine("Enabling real-time display...");
                var enableResponse = await controller.SendCmdRealTimeDisplayWithResponse(true);
                if (enableResponse?.IsSuccess != true)
                {
                    Console.WriteLine("Failed to enable real-time display");
                    return false;
                }

                // Send initial keep-alive packet
                await controller.SendCmdKeepAliveWithResponse(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                Console.WriteLine("Initial keep-alive packet sent");

                // Step 2: Set brightness to 80
                await controller.SendCmdBrightnessWithResponse(80);

                // Set keep-alive timer to a reasonable value (higher than our 4-second interval)
                await controller.SendCmdSetKeepAliveTimerWithResponse(60); // 10 seconds device timeout

                if (imagePaths.Count > 0)
                {
                    Console.WriteLine($"Cycling through {imagePaths.Count} images, {cycleCount} times...");
                    Console.WriteLine("Keep-alive packets will be sent automatically every 4 seconds");

                    byte transferId = 0;
                    for (int cycle = 0; cycle < cycleCount && !cancellationToken.IsCancellationRequested; cycle++)
                    {
                        foreach (var imagePath in imagePaths)
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            Console.WriteLine($"[SENDING] Sending: {Path.GetFileName(imagePath)}");

                            // Step 3: Send image file
                            controller.SendFileFromDisk(imagePath, transferId: transferId);

                            // Update the synchronized image viewer
                            UpdateStreamingImageViewer(imagePath);

                            if (transferId < 4)
                            {
                                // For the first image, wait a bit longer to ensure device is ready
                                await Task.Delay(10, cancellationToken); // 3 seconds for first image
                            }
                            else
                            {
                                // Wait a shorter time for subsequent images
                                await Task.Delay(1000, cancellationToken); // 2 seconds for other images
                            }

                            transferId++;
                            if (transferId > 59) transferId = 4;
                        }
                    }
                }

                // Disable real-time display
                Console.WriteLine("Disabling real-time display...");
                await controller.SendCmdRealTimeDisplayWithResponse(false);

                Console.WriteLine("=== Enhanced Real-Time Display Demo Completed ===");
                return true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Real-time display demo was cancelled");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Enhanced demo failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Handle offline streaming button click
        /// </summary>
        private async void OfflineStreamingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isStreamingInProgress)
            {
                ShowErrorMessage("Streaming operation already in progress. Please wait...");
                return;
            }

            if (_multiDeviceManager == null)
            {
                ShowErrorMessage("Device manager not initialized");
                return;
            }

            var activeControllers = _multiDeviceManager.GetActiveControllers();
            if (activeControllers.Count == 0)
            {
                ShowErrorMessage("No devices connected for offline streaming");
                return;
            }

            try
            {
                _isStreamingInProgress = true;
                UpdateButtonStates();
                HideErrorMessage();

                // Reset image viewer for offline mode
                ResetStreamingImageViewer();

                // Show status message
                ShowStatusMessage($"Starting offline streaming demo on {activeControllers.Count} device(s)...");

                // Execute offline streaming (suspend mode) on all devices
                var results = await _multiDeviceManager.ExecuteOnAllDevices(async controller =>
                {
                    try
                    {
                        Console.WriteLine("=== Simple Offline Display Demo ===");

                        // Step 1: Disable real-time display mode and entry to suspend mode
                        bool step1Response = await controller.SetSuspendModeWithResponse();
                        if (!step1Response)
                        {
                            return false;
                        }

                        // Init files list to transfer (you may want to make these paths configurable)
                        List<string> filesToTransfer = new List<string>();

                        // Try different base paths for the suspend files
                        string[] basePaths = {
                            @"E:\github\CMDevicesManager\HidProtocol\resources",
                            @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources",
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HidProtocol", "resources")
                        };

                        foreach (string basePath in basePaths)
                        {
                            string[] suspendFiles = {
                                Path.Combine(basePath, "suspend_0.jpg"),
                                Path.Combine(basePath, "suspend_1.jpg"),
                                Path.Combine(basePath, "suspend_2.mp4")
                            };

                            if (suspendFiles.Any(File.Exists))
                            {
                                filesToTransfer.AddRange(suspendFiles.Where(File.Exists));
                                break;
                            }
                        }

                        if (!filesToTransfer.Any())
                        {
                            Console.WriteLine("No suspend files found, using basic demo mode");
                            // Continue with basic suspend mode without files
                        }
                        else
                        {
                            // Step 2: Send multiple suspend files
                            bool filesSuccess = await controller.SendMultipleSuspendFilesWithResponse(filesToTransfer, startingTransferId: 2);
                            if (!filesSuccess)
                            {
                                Console.WriteLine("Failed to send suspend files");
                                return false;
                            }
                        }

                        // Step 3: Set brightness to 80
                        var brightnessResponse = await controller.SendCmdBrightnessWithResponse(80);
                        if (brightnessResponse?.IsSuccess != true)
                        {
                            Console.WriteLine($"Set brightness failed. Status: {brightnessResponse?.StatusCode}");
                        }

                        // Step 4: Set keep alive timer
                        var timerResponse = await controller.SendCmdSetKeepAliveTimerWithResponse(5);
                        if (timerResponse?.IsSuccess != true)
                        {
                            Console.WriteLine($"Set keep alive timer failed. Status: {timerResponse?.StatusCode}");
                        }

                        // Step 5: Get max suspend media count to check current status
                        var maxMediaResponse = await controller.SendCmdReadMaxSuspendMediaWithResponse();
                        if (maxMediaResponse?.IsSuccess == true && !string.IsNullOrEmpty(maxMediaResponse.Value.ResponseData))
                        {
                            Console.WriteLine($"Max suspend media response: {maxMediaResponse.Value.ResponseData}");
                        }

                        // Sleep for demonstration (shorter time for UI responsiveness)
                        Console.WriteLine("Entering suspend mode for 30 seconds...");
                        await Task.Delay(30000);

                        // Exit suspend mode
                        Console.WriteLine("Exiting suspend mode by deleting all suspend media...");
                        bool clearSuccess = await controller.ClearSuspendModeWithResponse();
                        if (!clearSuccess)
                        {
                            Console.WriteLine("Failed to clear suspend mode");
                            return false;
                        }

                        Console.WriteLine("Suspend mode demo completed successfully!");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Offline streaming failed for device: {ex.Message}");
                        return false;
                    }
                }, timeout: TimeSpan.FromMinutes(10));

                // Show results
                int successCount = results.Values.Count(success => success);
                if (successCount == results.Count)
                {
                    ShowStatusMessage($"Offline streaming demo completed successfully on all {successCount} device(s)!");
                }
                else
                {
                    ShowErrorMessage($"Offline streaming completed with mixed results: {successCount}/{results.Count} devices succeeded");
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Offline streaming demo failed: {ex.Message}");
            }
            finally
            {
                _isStreamingInProgress = false;
                UpdateButtonStates();
            }
        }

        /// <summary>
        /// Update button enabled/disabled states based on current conditions
        /// </summary>
        private void UpdateButtonStates()
        {
            bool hasDevices = DevicesListBox.Items.Count > 0;
            bool canOperate = hasDevices && !_isStreamingInProgress;

            RealtimeStreamingButton.IsEnabled = canOperate;
            OfflineStreamingButton.IsEnabled = canOperate;
        }

        /// <summary>
        /// Show a status message (success/info style)
        /// </summary>
        private void ShowStatusMessage(string message)
        {
            ErrorMessageTextBlock.Text = message;
            ErrorMessageBorder.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x44, 0xFF, 0x44)); // Green tint
            ErrorMessageBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x66, 0xFF, 0x66)); // Green border
            ErrorMessageBorder.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Displays an error message to the user
        /// </summary>
        /// <param name="message">The error message to display</param>
        private void ShowErrorMessage(string message)
        {
            ErrorMessageTextBlock.Text = message;
            ErrorMessageBorder.Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0x44, 0x44)); // Red tint
            ErrorMessageBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x66, 0x66)); // Red border
            ErrorMessageBorder.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Hides the error message
        /// </summary>
        private void HideErrorMessage()
        {
            ErrorMessageBorder.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Cleanup resources when page is unloaded
        /// </summary>
        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            // Stop keep-alive timer when page is unloaded
            StopKeepAliveTimer();
        }

        private async Task ExtractGifFramesExample()
        {
            string gifPath = @"E:\github\CMDevicesManager\HidProtocol\resources\gif\LCD-A.gif";
            string outputDir = @"E:\github\CMDevicesManager\HidProtocol\resources\gif\LCD-AFrame";

            Console.WriteLine("=== GIF Frame Extraction Example ===");

            int[] frameDurations = MyImageConverter.GetGifFrameDurations(gifPath);

            var gifInfo = MyImageConverter.GetGifInfo(gifPath);

            // Extract all frames
            string[] frameFiles = await MyImageConverter.ExtractGifFramesToJpegAsync(
                gifPath,
                outputDir,
                quality: 90,
                fileNamePrefix: "frame"
            );

            Console.WriteLine($"Extracted {frameFiles.Length} frames:");
            foreach (var frame in frameFiles)
            {
                Console.WriteLine($"  - {Path.GetFileName(frame)}");
            }
        }
    }
}