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
using Button = System.Windows.Controls.Button;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for DevicePageDemo.xaml
    /// </summary>
    public partial class DevicePageDemo : Page
    {
        private MultiDeviceManager? _multiDeviceManager;
        private bool _isStreamingInProgress = false;
        private int _streamedImageCount = 0;
        private CancellationTokenSource? _streamingCancellationSource;

        // Keep-alive timer functionality
        private System.Timers.Timer? _keepAliveTimer;
        private readonly object _keepAliveTimerLock = new object();
        private bool _keepAliveEnabled = false;

        // Display control state tracking
        private int _currentRotation = 0;
        private int _currentBrightness = 80;
        private bool _isBrightnessSliderUpdating = false;

        public DevicePageDemo()
        {
            InitializeComponent();
            // Initialize display controls
            InitializeDisplayControls();
            // ExtractGifFramesExample();

            //ExtractMp4FramesExample();
        }

        /// <summary>
        /// Initialize display control states
        /// </summary>
        private void InitializeDisplayControls()
        {
            // Set initial brightness slider value
            BrightnessSlider.Value = _currentBrightness;
            BrightnessValueText.Text = $"{_currentBrightness}%";

            // Set initial rotation display
            CurrentRotationText.Text = $"Current: {_currentRotation}°";

            // Update rotation button appearance to show current selection
            UpdateRotationButtonAppearance(_currentRotation);
        }

        /// <summary>
        /// Update the appearance of rotation buttons to highlight the selected one
        /// </summary>
        /// <param name="selectedRotation">Currently selected rotation value</param>
        private void UpdateRotationButtonAppearance(int selectedRotation)
        {
            // Reset all buttons to default style
            Rotation0Button.Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            Rotation90Button.Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            Rotation180Button.Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            Rotation270Button.Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));

            // Highlight the selected button
            var selectedColor = new SolidColorBrush(Color.FromRgb(0x2A, 0x4A, 0x8B));
            switch (selectedRotation)
            {
                case 0:
                    Rotation0Button.Background = selectedColor;
                    break;
                case 90:
                    Rotation90Button.Background = selectedColor;
                    break;
                case 180:
                    Rotation180Button.Background = selectedColor;
                    break;
                case 270:
                    Rotation270Button.Background = selectedColor;
                    break;
            }
        }

        /// <summary>
        /// Handle rotation button clicks
        /// </summary>
        private async void RotationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_multiDeviceManager == null)
            {
                ShowErrorMessage("Device manager not initialized");
                return;
            }

            var activeControllers = _multiDeviceManager.GetActiveControllers();
            if (activeControllers.Count == 0)
            {
                ShowErrorMessage("No devices connected for rotation control");
                return;
            }

            if (sender is Button button && button.Tag is string rotationString)
            {
                if (int.TryParse(rotationString, out int rotation))
                {
                    try
                    {
                        HideErrorMessage();

                        // Disable rotation buttons during operation
                        SetRotationButtonsEnabled(false);

                        ShowStatusMessage($"Setting rotation to {rotation}° on {activeControllers.Count} device(s)...");

                        // Send rotation command to all devices
                        var results = await _multiDeviceManager.SetRotationOnAllDevices(rotation);

                        // Check results
                        int successCount = results.Values.Count(success => success);
                        if (successCount == results.Count)
                        {
                            _currentRotation = rotation;
                            CurrentRotationText.Text = $"Current: {rotation}°";
                            UpdateRotationButtonAppearance(rotation);
                            ShowStatusMessage($"Rotation set to {rotation}° on all {successCount} device(s) successfully!");
                        }
                        else
                        {
                            ShowErrorMessage($"Rotation setting completed with mixed results: {successCount}/{results.Count} devices succeeded");
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowErrorMessage($"Failed to set rotation: {ex.Message}");
                    }
                    finally
                    {
                        // Re-enable rotation buttons
                        SetRotationButtonsEnabled(true);
                    }
                }
                else
                {
                    ShowErrorMessage("Invalid rotation value");
                }
            }
        }

        /// <summary>
        /// Enable or disable rotation buttons
        /// </summary>
        /// <param name="enabled">Whether buttons should be enabled</param>
        private void SetRotationButtonsEnabled(bool enabled)
        {
            Rotation0Button.IsEnabled = enabled;
            Rotation90Button.IsEnabled = enabled;
            Rotation180Button.IsEnabled = enabled;
            Rotation270Button.IsEnabled = enabled;
        }

        /// <summary>
        /// Handle brightness slider value changes
        /// </summary>
        private async void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isBrightnessSliderUpdating || _multiDeviceManager == null)
                return;

            int newBrightness = (int)Math.Round(e.NewValue);

            // Update the display text immediately for responsive UI
            BrightnessValueText.Text = $"{newBrightness}%";

            // Only send command if the value actually changed significantly
            if (Math.Abs(newBrightness - _currentBrightness) < 1)
                return;

            try
            {
                HideErrorMessage();

                var activeControllers = _multiDeviceManager.GetActiveControllers();
                if (activeControllers.Count == 0)
                {
                    ShowErrorMessage("No devices connected for brightness control");
                    return;
                }

                // Disable slider during operation to prevent multiple rapid calls
                BrightnessSlider.IsEnabled = false;

                // Send brightness command to all devices
                var results = await _multiDeviceManager.SetBrightnessOnAllDevices(newBrightness);

                // Check results
                int successCount = results.Values.Count(success => success);
                if (successCount == results.Count)
                {
                    _currentBrightness = newBrightness;
                    ShowStatusMessage($"Brightness set to {newBrightness}% on all {successCount} device(s) successfully!");
                }
                else
                {
                    ShowErrorMessage($"Brightness setting completed with mixed results: {successCount}/{results.Count} devices succeeded");

                    // Revert slider to previous value on failure
                    _isBrightnessSliderUpdating = true;
                    BrightnessSlider.Value = _currentBrightness;
                    BrightnessValueText.Text = $"{_currentBrightness}%";
                    _isBrightnessSliderUpdating = false;
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Failed to set brightness: {ex.Message}");

                // Revert slider to previous value on error
                _isBrightnessSliderUpdating = true;
                BrightnessSlider.Value = _currentBrightness;
                BrightnessValueText.Text = $"{_currentBrightness}%";
                _isBrightnessSliderUpdating = false;
            }
            finally
            {
                // Re-enable slider
                BrightnessSlider.IsEnabled = true;
            }
        }

        /// <summary>
        /// Handle quick brightness button clicks
        /// </summary>
        private async void QuickBrightnessButton_Click(object sender, RoutedEventArgs e)
        {
            if (_multiDeviceManager == null)
            {
                ShowErrorMessage("Device manager not initialized");
                return;
            }

            var activeControllers = _multiDeviceManager.GetActiveControllers();
            if (activeControllers.Count == 0)
            {
                ShowErrorMessage("No devices connected for brightness control");
                return;
            }

            if (sender is Button button && button.Tag is string brightnessString)
            {
                if (int.TryParse(brightnessString, out int brightness))
                {
                    try
                    {
                        HideErrorMessage();

                        // Update UI immediately
                        _isBrightnessSliderUpdating = true;
                        BrightnessSlider.Value = brightness;
                        BrightnessValueText.Text = $"{brightness}%";
                        _isBrightnessSliderUpdating = false;

                        // Disable quick brightness buttons during operation
                        SetQuickBrightnessButtonsEnabled(false);

                        ShowStatusMessage($"Setting brightness to {brightness}% on {activeControllers.Count} device(s)...");

                        // Send brightness command to all devices
                        var results = await _multiDeviceManager.SetBrightnessOnAllDevices(brightness);

                        // Check results
                        int successCount = results.Values.Count(success => success);
                        if (successCount == results.Count)
                        {
                            _currentBrightness = brightness;
                            ShowStatusMessage($"Brightness set to {brightness}% on all {successCount} device(s) successfully!");
                        }
                        else
                        {
                            ShowErrorMessage($"Brightness setting completed with mixed results: {successCount}/{results.Count} devices succeeded");

                            // Revert UI to previous value on failure
                            _isBrightnessSliderUpdating = true;
                            BrightnessSlider.Value = _currentBrightness;
                            BrightnessValueText.Text = $"{_currentBrightness}%";
                            _isBrightnessSliderUpdating = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowErrorMessage($"Failed to set brightness: {ex.Message}");

                        // Revert UI to previous value on error
                        _isBrightnessSliderUpdating = true;
                        BrightnessSlider.Value = _currentBrightness;
                        BrightnessValueText.Text = $"{_currentBrightness}%";
                        _isBrightnessSliderUpdating = false;
                    }
                    finally
                    {
                        // Re-enable quick brightness buttons
                        SetQuickBrightnessButtonsEnabled(true);
                    }
                }
                else
                {
                    ShowErrorMessage("Invalid brightness value");
                }
            }
        }

        /// <summary>
        /// Enable or disable quick brightness buttons
        /// </summary>
        /// <param name="enabled">Whether buttons should be enabled</param>
        private void SetQuickBrightnessButtonsEnabled(bool enabled)
        {
            // Find and enable/disable all quick brightness buttons
            var quickBrightnessButtons = new List<Button>();

            // Use a helper method to find buttons by their Tag values
            FindQuickBrightnessButtons(this, quickBrightnessButtons);

            foreach (var btn in quickBrightnessButtons)
            {
                btn.IsEnabled = enabled;
            }
        }

        /// <summary>
        /// Helper method to recursively find quick brightness buttons
        /// </summary>
        /// <param name="parent">Parent element to search</param>
        /// <param name="buttons">List to add found buttons to</param>
        private void FindQuickBrightnessButtons(DependencyObject parent, List<Button> buttons)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is Button button && button.Tag is string tag)
                {
                    // Check if this is a quick brightness button (has numeric tag for brightness values)
                    if (int.TryParse(tag, out int value) && value >= 0 && value <= 100
                        && !object.ReferenceEquals(button, BrightnessSlider)) // Make sure it's not the slider itself
                    {
                        buttons.Add(button);
                    }
                }

                FindQuickBrightnessButtons(child, buttons);
            }
        }

        /// <summary>
        /// Load current device settings and update UI accordingly
        /// </summary>
        private async void LoadCurrentDeviceSettings()
        {
            if (_multiDeviceManager == null)
                return;

            var activeControllers = _multiDeviceManager.GetActiveControllers();
            if (activeControllers.Count == 0)
                return;

            try
            {
                // Get current settings from the first device (assuming all devices have the same settings)
                var firstController = activeControllers.First();

                // Read current rotation
                var rotationResponse = await firstController.SendCmdReadRotatedAngleWithResponse();
                if (rotationResponse?.IsSuccess == true && !string.IsNullOrEmpty(rotationResponse.Value.ResponseData))
                {
                    try
                    {
                        var jsonResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(rotationResponse.Value.ResponseData);
                        if (jsonResponse?.ContainsKey("degree") == true)
                        {
                            if (int.TryParse(jsonResponse["degree"].ToString(), out int currentRotation))
                            {
                                _currentRotation = currentRotation;
                                Dispatcher.Invoke(() =>
                                {
                                    CurrentRotationText.Text = $"Current: {currentRotation}°";
                                    UpdateRotationButtonAppearance(currentRotation);
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to parse rotation response: {ex.Message}");
                    }
                }

                // Read current brightness
                var brightnessResponse = await firstController.SendCmdReadBrightnessWithResponse();
                if (brightnessResponse?.IsSuccess == true && !string.IsNullOrEmpty(brightnessResponse.Value.ResponseData))
                {
                    try
                    {
                        var jsonResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(brightnessResponse.Value.ResponseData);
                        if (jsonResponse?.ContainsKey("brightness") == true)
                        {
                            if (int.TryParse(jsonResponse["brightness"].ToString(), out int currentBrightness))
                            {
                                _currentBrightness = currentBrightness;
                                Dispatcher.Invoke(() =>
                                {
                                    _isBrightnessSliderUpdating = true;
                                    BrightnessSlider.Value = currentBrightness;
                                    BrightnessValueText.Text = $"{currentBrightness}%";
                                    _isBrightnessSliderUpdating = false;
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to parse brightness response: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load current device settings: {ex.Message}");
            }
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

                                // Load current device settings when a device is added
                                Task.Run(LoadCurrentDeviceSettings);
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

                // Load current device settings if devices are already connected
                if (activeControllers.Count > 0)
                {
                    Task.Run(LoadCurrentDeviceSettings);
                }
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
        private async Task<bool> EnhancedRealTimeDisplayDemov(DisplayController controller, string? imageDirectory = null, int cycleCount = 5, CancellationToken cancellationToken = default)
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


                            await Task.Delay(2000, cancellationToken); // 2 seconds for first image


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
        /// Updated EnhancedRealTimeDisplayDemo to use device synchronization
        /// </summary>
        private async Task<bool> EnhancedRealTimeDisplayDemo(DisplayController controller, string? imageDirectory = null, int cycleCount = 5, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("=== Enhanced Real-Time Display Demo with Device Synchronization ===");

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
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine("No valid image directory provided");
                    return false;
                }

                // Ensure device is awake
                var wakeResponse = await controller.SendCmdDisplayInSleepWithResponse(false);
                if (wakeResponse?.IsSuccess != true)
                {
                    Console.WriteLine("Failed to wake device from sleep");
                    return false;
                }
                await Task.Delay(1500);

                // Define device synchronization callback
                DeviceDisplayCallback deviceSyncCallback = (imagePath, imageIndex, transferId) =>
                {
                    try
                    {
                        // Update UI to show what the device is actually displaying
                        UpdateStreamingImageViewer(imagePath);

                        Console.WriteLine($"[UI SYNC] UI updated to match device display: {Path.GetFileName(imagePath)}");

                        // Update UI thread with synchronized information
                        Dispatcher.Invoke(() =>
                        {
                            // You can add additional UI updates here to show sync status
                            // For example, update a status label or progress indicator
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in device sync callback: {ex.Message}");
                    }
                };

                // Use the device-synchronized sending function
                bool success = await SendImagesWithDeviceSync(
                    controller,
                    imagePaths,
                    33,
                    deviceSyncCallback,
                    cycleCount,
                    cancellationToken);

                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Enhanced demo with device sync failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Delegate for image display callback when device starts showing images
        /// </summary>
        /// <param name="imagePath">Path to the image that the device is now displaying</param>
        /// <param name="imageIndex">Sequential index of the image</param>
        /// <param name="transferId">Transfer ID used for the image</param>
        public delegate void DeviceDisplayCallback(string imagePath, int imageIndex, byte transferId);

        /// <summary>
        /// Send images to device with callback triggered when device starts displaying (after 3 images buffered)
        /// </summary>
        /// <param name="controller">Display controller</param>
        /// <param name="imagePaths">List of image paths to send</param>
        /// <param name="onDeviceDisplaying">Callback triggered when device starts displaying an image</param>
        /// <param name="cycleCount">Number of cycles to repeat</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if completed successfully</returns>
        private async Task<bool> SendImagesWithDeviceSync(
            DisplayController controller,
            List<string> imagePaths,
            int delayTimeMs = 33,
            DeviceDisplayCallback? onDeviceDisplaying = null,
            int cycleCount = 1,
            CancellationToken cancellationToken = default)
        {
            Console.WriteLine("=== Sending Images with Device Synchronization ===");

            try
            {
                if (imagePaths == null || imagePaths.Count == 0)
                {
                    Console.WriteLine("No images provided");
                    return false;
                }

                // Enable real-time display
                var enableResponse = await controller.SendCmdRealTimeDisplayWithResponse(true);
                if (enableResponse?.IsSuccess != true)
                {
                    Console.WriteLine("Failed to enable real-time display");
                    return false;
                }

                // Set brightness and keep-alive timer
                await controller.SendCmdBrightnessWithResponse(80);
                await controller.SendCmdSetKeepAliveTimerWithResponse(60);

                Console.WriteLine($"Sending {imagePaths.Count} images, {cycleCount} cycles");
                Console.WriteLine("Device will start displaying after receiving 3 images");

                const int DEVICE_BUFFER_SIZE = 2; // Device starts displaying after 3 images
                byte transferId = 0;
                int totalImagesSent = 0;

                // Queue to track sent images for device synchronization
                Queue<(string path, int index, byte id)> deviceBuffer = new Queue<(string, int, byte)>();

                for (int cycle = 0; cycle < cycleCount && !cancellationToken.IsCancellationRequested; cycle++)
                {
                    for (int i = 0; i < imagePaths.Count; i++)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        string imagePath = imagePaths[i];
                        Console.WriteLine($"[SENDING] Image {totalImagesSent + 1}: {Path.GetFileName(imagePath)} (Transfer ID: {transferId})");

                        // Send image file to device
                        controller.SendFileFromDisk(imagePath, transferId: transferId);

                        // Add to device buffer queue
                        deviceBuffer.Enqueue((imagePath, totalImagesSent, transferId));
                        totalImagesSent++;

                        // Check if device has started displaying images
                        if (deviceBuffer.Count > DEVICE_BUFFER_SIZE)
                        {
                            // Device is now displaying the image that was sent 3 images ago
                            var (displayedPath, displayedIndex, displayedId) = deviceBuffer.Dequeue();

                            // Trigger callback - device is displaying this image
                            onDeviceDisplaying?.Invoke(displayedPath, displayedIndex, displayedId);

                            Console.WriteLine($"[DEVICE SYNC] Device now displaying: {Path.GetFileName(displayedPath)} (Index: {displayedIndex})");
                        }
                        else if (totalImagesSent == DEVICE_BUFFER_SIZE)
                        {
                            // Device just started displaying - show the first image sent
                            var (firstPath, firstIndex, firstId) = deviceBuffer.Peek();

                            // Trigger callback for first displayed image
                            onDeviceDisplaying?.Invoke(firstPath, firstIndex, firstId);

                            Console.WriteLine($"[DEVICE SYNC] Device started displaying: {Path.GetFileName(firstPath)} (Index: {firstIndex})");
                            Console.WriteLine("*** Device display synchronization established ***");
                        }


                        await Task.Delay(delayTimeMs, cancellationToken); // Quick send to fill device buffer

                        //// Timing control for proper synchronization
                        //if (transferId < 3)
                        //{
                        //    await Task.Delay(100, cancellationToken); // Quick send to fill device buffer
                        //}
                        //else
                        //{
                        //    await Task.Delay(2000, cancellationToken); // Normal display timing
                        //}

                        transferId++;
                        if (transferId > 59) transferId = 4;
                    }
                }

                // Process remaining images in device buffer
                Console.WriteLine("Processing final images in device buffer...");
                while (deviceBuffer.Count > 0)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var (remainingPath, remainingIndex, remainingId) = deviceBuffer.Dequeue();

                    // Trigger callback for remaining buffered images
                    onDeviceDisplaying?.Invoke(remainingPath, remainingIndex, remainingId);

                    Console.WriteLine($"[DEVICE SYNC] Device displaying buffered image: {Path.GetFileName(remainingPath)} (Index: {remainingIndex})");

                    // Wait for device to process the image
                    await Task.Delay(delayTimeMs, cancellationToken);
                }

                // Disable real-time display
                await controller.SendCmdRealTimeDisplayWithResponse(false);

                Console.WriteLine("=== Device synchronized image sending completed ===");
                return true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Device synchronized image sending was cancelled");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Device synchronized image sending failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send MP4 video frames to device with real-time decoding and device synchronization
        /// </summary>
        /// <param name="controller">Display controller</param>
        /// <param name="mp4FilePath">Path to the MP4 file to decode and send</param>
        /// <param name="frameDelayMs">Delay between frames in milliseconds (default: 33ms for ~30fps)</param>
        /// <param name="onDeviceDisplaying">Callback triggered when device starts displaying a frame</param>
        /// <param name="cycleCount">Number of times to repeat the video</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if completed successfully</returns>
        private async Task<bool> SendMp4DataWithDeviceSync(
            DisplayController controller,
            string mp4FilePath,
            int frameDelayMs = 33,
            DeviceDisplayCallback? onDeviceDisplaying = null,
            int cycleCount = 1,
            CancellationToken cancellationToken = default)
        {
            Console.WriteLine("=== Sending MP4 Data with Device Synchronization ===");

            try
            {
                if (!File.Exists(mp4FilePath))
                {
                    Console.WriteLine($"MP4 file not found: {mp4FilePath}");
                    return false;
                }

                // Get video information
                var videoInfo = await VideoConverter.GetMp4InfoAsync(mp4FilePath);
                if (videoInfo == null)
                {
                    Console.WriteLine("Failed to get MP4 video information");
                    return false;
                }

                Console.WriteLine($"Video Info: {videoInfo}");
                Console.WriteLine($"Total estimated frames: {videoInfo.TotalFrames}");

                // Enable real-time display
                var enableResponse = await controller.SendCmdRealTimeDisplayWithResponse(true);
                if (enableResponse?.IsSuccess != true)
                {
                    Console.WriteLine("Failed to enable real-time display");
                    return false;
                }

                // Set brightness and keep-alive timer
                await controller.SendCmdBrightnessWithResponse(80);
                await controller.SendCmdSetKeepAliveTimerWithResponse(60);

                Console.WriteLine($"Starting MP4 frame streaming, {cycleCount} cycles");
                Console.WriteLine("Device will start displaying after receiving 3 frames");

                const int DEVICE_BUFFER_SIZE = 2; // Device starts displaying after 3 frames
                byte transferId = 0;
                int totalFramesSent = 0;

                // Queue to track sent frames for device synchronization
                Queue<(int frameIndex, byte id)> deviceBuffer = new Queue<(int, byte)>();

                for (int cycle = 0; cycle < cycleCount && !cancellationToken.IsCancellationRequested; cycle++)
                {
                    Console.WriteLine($"Starting cycle {cycle + 1}/{cycleCount}");

                    // Extract and send frames in real-time
                    await foreach (var frameData in VideoConverter.ExtractMp4FramesToJpegRealTimeAsync(
                        mp4FilePath, 
                        quality: 90, 
                        cancellationToken))
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        Console.WriteLine($"[SENDING] Frame {frameData.FrameIndex} at {frameData.TimeStamp:mm\\:ss\\.fff} (Transfer ID: {transferId}, Size: {frameData.DataSize} bytes)");

                        // Send frame data to device
                        controller.SendFileTransfer(frameData.JpegData, fileType: 1, transferId: transferId);

                        // Add to device buffer queue
                        deviceBuffer.Enqueue((frameData.FrameIndex, transferId));
                        totalFramesSent++;

                        // Check if device has started displaying frames
                        if (deviceBuffer.Count > DEVICE_BUFFER_SIZE)
                        {
                            // Device is now displaying the frame that was sent 3 frames ago
                            var (displayedFrameIndex, displayedId) = deviceBuffer.Dequeue();

                            // Trigger callback - device is displaying this frame
                            // For MP4 frames, we use a synthetic path based on frame index
                            string syntheticPath = $"mp4_frame_{displayedFrameIndex:D6}.jpg";
                            onDeviceDisplaying?.Invoke(syntheticPath, displayedFrameIndex, displayedId);

                            Console.WriteLine($"[DEVICE SYNC] Device now displaying frame {displayedFrameIndex} (Transfer ID: {displayedId})");
                        }
                        else if (totalFramesSent == DEVICE_BUFFER_SIZE)
                        {
                            // Device just started displaying - show the first frame sent
                            var (firstFrameIndex, firstId) = deviceBuffer.Peek();

                            // Trigger callback for first displayed frame
                            string firstSyntheticPath = $"mp4_frame_{firstFrameIndex:D6}.jpg";
                            onDeviceDisplaying?.Invoke(firstSyntheticPath, firstFrameIndex, firstId);

                            Console.WriteLine($"[DEVICE SYNC] Device started displaying frame {firstFrameIndex} (Transfer ID: {firstId})");
                            Console.WriteLine("*** Device display synchronization established ***");
                        }

                        // Control frame rate
                        await Task.Delay(frameDelayMs, cancellationToken);

                        transferId++;
                        if (transferId > 59) transferId = 4;
                    }

                    // Process remaining frames in device buffer for this cycle
                    Console.WriteLine($"Processing final frames in device buffer for cycle {cycle + 1}...");
                    while (deviceBuffer.Count > 0)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        var (remainingFrameIndex, remainingId) = deviceBuffer.Dequeue();

                        // Trigger callback for remaining buffered frames
                        string syntheticPath = $"mp4_frame_{remainingFrameIndex:D6}.jpg";
                        onDeviceDisplaying?.Invoke(syntheticPath, remainingFrameIndex, remainingId);

                        Console.WriteLine($"[DEVICE SYNC] Device displaying buffered frame {remainingFrameIndex} (Transfer ID: {remainingId})");

                        // Wait for device to process the frame
                        await Task.Delay(frameDelayMs, cancellationToken);
                    }
                }

                // Disable real-time display
                await controller.SendCmdRealTimeDisplayWithResponse(false);

                Console.WriteLine("=== MP4 device synchronized streaming completed ===");
                return true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("MP4 device synchronized streaming was cancelled");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MP4 device synchronized streaming failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Enhanced MP4 streaming callback that provides frame data along with synchronization info
        /// </summary>
        /// <param name="frameData">The actual frame data being displayed</param>
        /// <param name="frameIndex">Sequential index of the frame</param>
        /// <param name="transferId">Transfer ID used for the frame</param>
        public delegate void Mp4DeviceDisplayCallback(VideoFrameData frameData, int frameIndex, byte transferId);

        /// <summary>
        /// Advanced MP4 streaming with full frame data in callback
        /// </summary>
        /// <param name="controller">Display controller</param>
        /// <param name="mp4FilePath">Path to the MP4 file to decode and send</param>
        /// <param name="frameDelayMs">Delay between frames in milliseconds</param>
        /// <param name="onDeviceDisplaying">Callback triggered when device starts displaying a frame with full frame data</param>
        /// <param name="cycleCount">Number of times to repeat the video</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if completed successfully</returns>
        private async Task<bool> SendMp4DataWithAdvancedDeviceSync(
            DisplayController controller,
            string mp4FilePath,
            int frameDelayMs = 33,
            Mp4DeviceDisplayCallback? onDeviceDisplaying = null,
            int cycleCount = 1,
            CancellationToken cancellationToken = default)
        {
            Console.WriteLine("=== Advanced MP4 Data Streaming with Full Frame Sync ===");

            try
            {
                if (!File.Exists(mp4FilePath))
                {
                    Console.WriteLine($"MP4 file not found: {mp4FilePath}");
                    return false;
                }

                // Get video information
                var videoInfo = await VideoConverter.GetMp4InfoAsync(mp4FilePath);
                if (videoInfo == null)
                {
                    Console.WriteLine("Failed to get MP4 video information");
                    return false;
                }

                Console.WriteLine($"Video Info: {videoInfo}");

                // Enable real-time display
                var enableResponse = await controller.SendCmdRealTimeDisplayWithResponse(true);
                if (enableResponse?.IsSuccess != true)
                {
                    Console.WriteLine("Failed to enable real-time display");
                    return false;
                }

                await controller.SendCmdBrightnessWithResponse(80);
                await controller.SendCmdSetKeepAliveTimerWithResponse(60);

                const int DEVICE_BUFFER_SIZE = 2;
                byte transferId = 0;

                // Queue to track sent frames with full frame data for device synchronization
                Queue<(VideoFrameData frameData, byte id)> deviceBuffer = new Queue<(VideoFrameData, byte)>();

                for (int cycle = 0; cycle < cycleCount && !cancellationToken.IsCancellationRequested; cycle++)
                {
                    Console.WriteLine($"Starting advanced cycle {cycle + 1}/{cycleCount}");

                    await foreach (var frameData in VideoConverter.ExtractMp4FramesToJpegRealTimeAsync(
                        mp4FilePath, 
                        quality: 90, 
                        cancellationToken))
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        Console.WriteLine($"[SENDING] Frame {frameData.FrameIndex} at {frameData.TimeStamp:mm\\:ss\\.fff} (Size: {frameData.DataSize} bytes)");

                        // Send frame data to device
                        controller.SendFileTransfer(frameData.JpegData, fileType: 1, transferId: transferId);

                        // Store complete frame data in buffer
                        deviceBuffer.Enqueue((frameData, transferId));

                        // Check device synchronization
                        if (deviceBuffer.Count > DEVICE_BUFFER_SIZE)
                        {
                            var (displayedFrameData, displayedId) = deviceBuffer.Dequeue();
                            
                            // Trigger callback with full frame data
                            onDeviceDisplaying?.Invoke(displayedFrameData, displayedFrameData.FrameIndex, displayedId);

                            Console.WriteLine($"[DEVICE SYNC] Device displaying frame {displayedFrameData.FrameIndex} at {displayedFrameData.TimeStamp:mm\\:ss\\.fff}");
                        }
                        else if (deviceBuffer.Count == DEVICE_BUFFER_SIZE)
                        {
                            var (firstFrameData, firstId) = deviceBuffer.Peek();
                            onDeviceDisplaying?.Invoke(firstFrameData, firstFrameData.FrameIndex, firstId);
                            Console.WriteLine("*** Advanced device display synchronization established ***");
                        }

                        await Task.Delay(frameDelayMs, cancellationToken);
                        transferId++;
                        if (transferId > 59) transferId = 4;
                    }

                    // Process remaining buffered frames
                    while (deviceBuffer.Count > 0)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        var (remainingFrameData, remainingId) = deviceBuffer.Dequeue();
                        onDeviceDisplaying?.Invoke(remainingFrameData, remainingFrameData.FrameIndex, remainingId);
                        
                        await Task.Delay(frameDelayMs, cancellationToken);
                    }
                }

                await controller.SendCmdRealTimeDisplayWithResponse(false);

                Console.WriteLine("=== Advanced MP4 device synchronized streaming completed ===");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Advanced MP4 streaming failed: {ex.Message}");
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

            // Also update display control states
            SetRotationButtonsEnabled(canOperate);
            BrightnessSlider.IsEnabled = canOperate;
            SetQuickBrightnessButtonsEnabled(canOperate);
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

        // Functon to demo extracting mp4 frames
        private async Task ExtractMp4FramesExample()
        {
            // Real-time frame extraction with streaming
            string mp4Path = @"E:\github\CMDevicesManager\HidProtocol\resources\suspend_2.mp4";
            var cts = new CancellationTokenSource();

            await foreach (var frame in VideoConverter.ExtractMp4FramesToJpegRealTimeAsync(mp4Path, quality: 90, cts.Token))
            {
                Console.WriteLine($"Received frame {frame.FrameIndex}: {frame.DataSize} bytes");

                // Process frame data immediately (e.g., display, analyze, save)
                // ProcessFrame(frame.JpegData);

                // You can cancel at any time
                if (frame.FrameIndex > 100)
                {
                    cts.Cancel();
                    break;
                }
            }

            //// Extract with real-time progress reporting
            //var frames = await VideoConverter.ExtractMp4FramesToJpegWithProgressAsync(
            //    mp4Path,
            //    quality: 90,
            //    progressCallback: (current, total, frame) =>
            //    {
            //        double percentage = (double)current / total * 100;
            //        Console.WriteLine($"Progress: {percentage:F1}% - Frame {current}/{total}");
            //        Console.WriteLine($"Frame info: {frame}");
            //    }
            //);

            //Console.WriteLine($"Extraction complete! Total frames: {frames.Length}");
        }

        /// <summary>
        /// Handle MP4 streaming button click (add this button to your XAML)
        /// </summary>
        private async void Mp4StreamingButton_Click(object sender, RoutedEventArgs e)
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
                ShowErrorMessage("No devices connected for MP4 streaming");
                return;
            }

            try
            {
                _isStreamingInProgress = true;
                _streamingCancellationSource = new CancellationTokenSource();
                UpdateButtonStates();
                HideErrorMessage();
                ShowStreamingStatus(true);

                // Start the keep-alive timer
                StartKeepAliveTimer();

                // Set MP4 file path (make this configurable)
                string mp4FilePath = @"E:\github\CMDevicesManager\HidProtocol\resources\suspend_2.mp4";

                // Get the video info for display
                var videoInfo = await VideoConverter.GetMp4InfoAsync(mp4FilePath);
                if (videoInfo != null)
                {
                    Console.WriteLine($"MP4 Video Info: {videoInfo}");
                    ShowStatusMessage($"Streaming MP4: {videoInfo.Width}x{videoInfo.Height}, {videoInfo.FrameRate:F2}fps, {videoInfo.TotalFrames} frames");
                }

                int frameDelayMs = videoInfo != null ? (int)(1000 / videoInfo.FrameRate) : 33;

                ShowStatusMessage($"Starting MP4 streaming on {activeControllers.Count} device(s)...");

                // Execute MP4 streaming with device synchronization
                var results = await _multiDeviceManager.ExecuteOnAllDevices(async controller =>
                {
                    try
                    {
                        // Define callback for device synchronization
                        DeviceDisplayCallback deviceSyncCallback = (framePath, frameIndex, transferId) =>
                        {
                            try
                            {
                                // For MP4 frames, we can't directly update with file path since frames are generated in memory
                                // Instead, we update UI to show frame information
                                Dispatcher.Invoke(() =>
                                {
                                    CurrentImageName.Text = $"MP4 Frame {frameIndex}";
                                    ImageDimensions.Text = "MP4 Video Frame";
                                    
                                    // Update frame counter
                                    _streamedImageCount++;
                                    ImageCounter.Text = _streamedImageCount.ToString();
                                    // update image viewer with placeholder or frame info
                                    StreamingImageViewer.Source = null; // Or set to a placeholder image

                                });

                                Console.WriteLine($"[UI SYNC] UI updated for MP4 frame {frameIndex}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error in MP4 device sync callback: {ex.Message}");
                            }
                        };
                        // Update the Mp4StreamingButton_Click to use the advanced sync with frame data:
                        Mp4DeviceDisplayCallback advancedDeviceSyncCallback = (frameData, frameIndex, transferId) =>
                        {
                            try
                            {
                                // Update UI with actual frame data
                                UpdateStreamingImageViewerFromFrameData(frameData);
                                Console.WriteLine($"[UI SYNC] UI updated with actual frame data for frame {frameIndex}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error in advanced MP4 device sync callback: {ex.Message}");
                            }
                        };

                        // Use the MP4 device-synchronized streaming function
                        bool success = await SendMp4DataWithAdvancedDeviceSync(
                            controller,
                            mp4FilePath,
                            frameDelayMs: frameDelayMs, // Adjust frame rate as needed
                            advancedDeviceSyncCallback,
                            cycleCount: 1,
                            _streamingCancellationSource.Token);

                        return success;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"MP4 streaming failed for device: {ex.Message}");
                        return false;
                    }
                }, timeout: TimeSpan.FromMinutes(10));

                // Show results
                int successCount = results.Values.Count(success => success);
                if (successCount == results.Count)
                {
                    ShowStatusMessage($"MP4 streaming completed successfully on all {successCount} device(s)!");
                }
                else
                {
                    ShowErrorMessage($"MP4 streaming completed with mixed results: {successCount}/{results.Count} devices succeeded");
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"MP4 streaming failed: {ex.Message}");
            }
            finally
            {
                StopKeepAliveTimer();
                _isStreamingInProgress = false;
                ShowStreamingStatus(false);
                UpdateButtonStates();
                _streamingCancellationSource?.Dispose();
                _streamingCancellationSource = null;
            }
        }

        /// <summary>
        /// Update streaming image viewer with JPEG byte data
        /// </summary>
        /// <param name="jpegData">JPEG image data as byte array</param>
        /// <param name="frameName">Optional name for the frame (for display purposes)</param>
        private void UpdateStreamingImageViewerFromBytes(byte[] jpegData, string frameName = "Frame")
        {
            try
            {
                if (jpegData == null || jpegData.Length == 0)
                {
                    Console.WriteLine("No JPEG data provided");
                    return;
                }

                // Create BitmapImage from byte array
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                
                using (var stream = new MemoryStream(jpegData))
                {
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                
                bitmap.Freeze(); // Make thread-safe

                // Update UI on the UI thread
                Dispatcher.Invoke(() =>
                {
                    StreamingImageViewer.Source = bitmap;
                    StreamingImageViewer.Visibility = Visibility.Visible;
                    PlaceholderContent.Visibility = Visibility.Collapsed;
                    LoadingIndicator.Visibility = Visibility.Collapsed;

                    // Update image info
                    CurrentImageName.Text = frameName;
                    ImageDimensions.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight}";

                    // Update image counter
                    _streamedImageCount++;
                    ImageCounter.Text = _streamedImageCount.ToString();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update streaming image viewer from bytes: {ex.Message}");
            }
        }

        /// <summary>
        /// Update streaming image viewer with VideoFrameData
        /// </summary>
        /// <param name="frameData">Video frame data containing JPEG bytes</param>
        private void UpdateStreamingImageViewerFromFrameData(VideoFrameData frameData)
        {
            try
            {
                if (frameData?.JpegData == null || frameData.JpegData.Length == 0)
                {
                    Console.WriteLine("No frame data provided");
                    return;
                }

                // Create BitmapImage from frame's JPEG data
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                
                using (var stream = new MemoryStream(frameData.JpegData))
                {
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                
                bitmap.Freeze(); // Make thread-safe

                // Update UI on the UI thread
                Dispatcher.Invoke(() =>
                {
                    StreamingImageViewer.Source = bitmap;
                    StreamingImageViewer.Visibility = Visibility.Visible;
                    PlaceholderContent.Visibility = Visibility.Collapsed;
                    LoadingIndicator.Visibility = Visibility.Collapsed;

                    // Update image info with frame details
                    CurrentImageName.Text = $"MP4 Frame {frameData.FrameIndex}";
                    ImageDimensions.Text = $"{frameData.Width} × {frameData.Height}";

                    // Update image counter
                    _streamedImageCount++;
                    ImageCounter.Text = _streamedImageCount.ToString();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update streaming image viewer from frame data: {ex.Message}");
            }
        }
    }
}