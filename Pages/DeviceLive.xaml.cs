//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Data;
//using System.Windows.Documents;
//using System.Windows.Input;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;
//using System.Windows.Navigation;
//using System.Windows.Shapes;

//namespace CMDevicesManager.Pages
//{
//    /// <summary>
//    /// Interaction logic for DeviceLive.xaml
//    /// </summary>
//    public partial class DeviceLive : Page
//    {
//        public DeviceLive()
//        {
//            InitializeComponent();
//        }
//    }
//}


using CMDevicesManager.Models;
using HID.DisplayController;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HidApi;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Timer = System.Timers.Timer;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Device show and control page for managing individual device settings
    /// </summary>
    public partial class DeviceLive : Page, INotifyPropertyChanged
    {
        #region Fields

        private DeviceInfo? _deviceInfo;
        private DisplayController? _displayController;
        private MultiDeviceManager? _multiDeviceManager;
        private Timer? _statusTimer;
        private Timer? _keepAliveTimer;
        private readonly object _keepAliveTimerLock = new object();
        private bool _keepAliveEnabled = false;

        // Control state tracking
        private int _currentRotation = 0;
        private int _currentBrightness = 80;
        private bool _isBrightnessSliderUpdating = false;
        private bool _isStreamingInProgress = false;
        private int _streamedImageCount = 0;
        private CancellationTokenSource? _streamingCancellationSource;

        // Image/video paths
        private string? _selectedImagesFolder;
        private List<string> _selectedOfflineFiles = new List<string>();

        #endregion

        #region Properties

        public DeviceInfo? DeviceInfo
        {
            get => _deviceInfo;
            set
            {
                if (_deviceInfo != value)
                {
                    _deviceInfo = value;
                    OnPropertyChanged();
                    UpdateDeviceInformation();
                }
            }
        }

        #endregion

        #region Constructor

        public DeviceLive()
        {
            InitializeComponent();
            DataContext = this;
            InitializeDisplayControls();
        }

        public DeviceLive(DeviceInfo deviceInfo) : this()
        {
            DeviceInfo = deviceInfo;
        }

        #endregion

        #region Initialization

        private void InitializeDisplayControls()
        {
            // Set initial brightness slider value
            BrightnessSlider.Value = _currentBrightness;
            BrightnessValueText.Text = $"{_currentBrightness}%";

            // Set initial rotation display
            CurrentRotationText.Text = $"Current: {_currentRotation}°";

            // Update rotation button appearance to show current selection
            UpdateRotationButtonAppearance(_currentRotation);

            // Initialize streaming preview
            ResetStreamingImageViewer();
        }

        private void UpdateDeviceInformation()
        {
            if (_deviceInfo != null)
            {
                ProductNameText.Text = _deviceInfo.ProductString ?? "Unknown Device";
                ManufacturerText.Text = _deviceInfo.ManufacturerString ?? "Unknown Manufacturer";
                SerialNumberText.Text = _deviceInfo.SerialNumber ?? "No Serial Number";

                ConnectionStatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
                ConnectionStatusText.Text = "Connected";
                ConnectionStatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
            }
            else
            {
                ProductNameText.Text = "No device selected";
                ManufacturerText.Text = "-";
                SerialNumberText.Text = "-";

                ConnectionStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                ConnectionStatusText.Text = "Disconnected";
                ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        #endregion

        #region Page Events

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                InitializeDeviceManager();
                if (_deviceInfo != null)
                {
                    Task.Run(LoadCurrentDeviceSettings);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize device manager: {ex}");
                ShowErrorMessage($"Failed to initialize device manager: {ex.Message}");
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                StopKeepAliveTimer();
                _statusTimer?.Stop();
                _statusTimer = null;

                if (_multiDeviceManager != null)
                {
                    _multiDeviceManager.StopMonitoring();
                    _multiDeviceManager.Dispose();
                    _multiDeviceManager = null;
                }

                if (_displayController != null)
                {
                    _displayController.StopResponseListener();
                    _displayController.Dispose();
                    _displayController = null;
                }

                _streamingCancellationSource?.Cancel();
                _streamingCancellationSource?.Dispose();
                _streamingCancellationSource = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during page cleanup: {ex}");
            }
        }

        #endregion

        #region Device Management

        private void InitializeDeviceManager()
        {
            try
            {
                if (_deviceInfo != null)
                {
                    // Create controller for the specific device
                    _displayController = new DisplayController(_deviceInfo.Path);
                    _displayController.StartResponseListener();

                    // Also create multi-device manager for broader operations
                    _multiDeviceManager = new MultiDeviceManager(0x2516, 0x0228);
                    _multiDeviceManager.StartMonitoring();

                    UpdateActiveDeviceCount();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize device manager: {ex}");
                throw;
            }
        }

        private async void LoadCurrentDeviceSettings()
        {
            if (_displayController == null)
                return;

            try
            {
                // Read current rotation
                var rotationResponse = await _displayController.SendCmdReadRotatedAngleWithResponse();
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
                        Debug.WriteLine($"Failed to parse rotation response: {ex.Message}");
                    }
                }

                // Read current brightness
                var brightnessResponse = await _displayController.SendCmdReadBrightnessWithResponse();
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
                        Debug.WriteLine($"Failed to parse brightness response: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load current device settings: {ex.Message}");
            }
        }

        #endregion

        #region Display Controls

        private void UpdateRotationButtonAppearance(int selectedRotation)
        {
            // Reset all buttons to default style
            var defaultColor = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            var selectedColor = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));

            Rotation0Button.Background = defaultColor;
            Rotation90Button.Background = defaultColor;
            Rotation180Button.Background = defaultColor;
            Rotation270Button.Background = defaultColor;

            // Highlight the selected button
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

        private async void RotationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_displayController == null)
            {
                ShowErrorMessage("Device not connected");
                return;
            }

            if (sender is Button button && button.Tag is string rotationString)
            {
                if (int.TryParse(rotationString, out int rotation))
                {
                    try
                    {
                        HideErrorMessage();
                        SetRotationButtonsEnabled(false);

                        ShowStatusMessage($"Setting rotation to {rotation}°...");

                        var response = await _displayController.SendCmdRotateWithResponse(rotation);
                        if (response?.IsSuccess == true)
                        {
                            _currentRotation = rotation;
                            CurrentRotationText.Text = $"Current: {rotation}°";
                            UpdateRotationButtonAppearance(rotation);
                            ShowStatusMessage($"Rotation set to {rotation}° successfully!");
                        }
                        else
                        {
                            ShowErrorMessage($"Failed to set rotation to {rotation}°");
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowErrorMessage($"Failed to set rotation: {ex.Message}");
                    }
                    finally
                    {
                        SetRotationButtonsEnabled(true);
                    }
                }
            }
        }

        private void SetRotationButtonsEnabled(bool enabled)
        {
            Rotation0Button.IsEnabled = enabled;
            Rotation90Button.IsEnabled = enabled;
            Rotation180Button.IsEnabled = enabled;
            Rotation270Button.IsEnabled = enabled;
        }

        private async void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isBrightnessSliderUpdating || _displayController == null)
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
                BrightnessSlider.IsEnabled = false;

                var response = await _displayController.SendCmdBrightnessWithResponse(newBrightness);
                if (response?.IsSuccess == true)
                {
                    _currentBrightness = newBrightness;
                    ShowStatusMessage($"Brightness set to {newBrightness}% successfully!");
                }
                else
                {
                    ShowErrorMessage($"Failed to set brightness to {newBrightness}%");

                    // Revert slider to previous value
                    _isBrightnessSliderUpdating = true;
                    BrightnessSlider.Value = _currentBrightness;
                    BrightnessValueText.Text = $"{_currentBrightness}%";
                    _isBrightnessSliderUpdating = false;
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Failed to set brightness: {ex.Message}");

                // Revert slider to previous value
                _isBrightnessSliderUpdating = true;
                BrightnessSlider.Value = _currentBrightness;
                BrightnessValueText.Text = $"{_currentBrightness}%";
                _isBrightnessSliderUpdating = false;
            }
            finally
            {
                BrightnessSlider.IsEnabled = true;
            }
        }

        private async void QuickBrightnessButton_Click(object sender, RoutedEventArgs e)
        {
            if (_displayController == null)
            {
                ShowErrorMessage("Device not connected");
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

                        ShowStatusMessage($"Setting brightness to {brightness}%...");

                        var response = await _displayController.SendCmdBrightnessWithResponse(brightness);
                        if (response?.IsSuccess == true)
                        {
                            _currentBrightness = brightness;
                            ShowStatusMessage($"Brightness set to {brightness}% successfully!");
                        }
                        else
                        {
                            ShowErrorMessage($"Failed to set brightness to {brightness}%");

                            // Revert UI to previous value
                            _isBrightnessSliderUpdating = true;
                            BrightnessSlider.Value = _currentBrightness;
                            BrightnessValueText.Text = $"{_currentBrightness}%";
                            _isBrightnessSliderUpdating = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowErrorMessage($"Failed to set brightness: {ex.Message}");

                        // Revert UI to previous value
                        _isBrightnessSliderUpdating = true;
                        BrightnessSlider.Value = _currentBrightness;
                        BrightnessValueText.Text = $"{_currentBrightness}%";
                        _isBrightnessSliderUpdating = false;
                    }
                }
            }
        }

        #endregion

        #region Streaming Controls

        private async void RealtimeStreamingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isStreamingInProgress)
            {
                ShowErrorMessage("Streaming operation already in progress. Please wait...");
                return;
            }

            if (_displayController == null)
            {
                ShowErrorMessage("Device not connected");
                return;
            }

            if (string.IsNullOrEmpty(_selectedImagesFolder))
            {
                ShowErrorMessage("Please select an images folder first");
                return;
            }

            try
            {
                _isStreamingInProgress = true;
                _streamingCancellationSource = new CancellationTokenSource();
                UpdateControlStates();
                HideErrorMessage();
                ShowStreamingStatus(true);

                StartKeepAliveTimer();

                ShowStatusMessage("Starting real-time streaming...");

                // Get image files from selected folder
                var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };
                var imageFiles = Directory.GetFiles(_selectedImagesFolder)
                                         .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
                                         .ToList();

                if (imageFiles.Count == 0)
                {
                    ShowErrorMessage("No supported image files found in selected folder");
                    return;
                }

                bool success = await EnhancedRealTimeDisplayDemo(_displayController, imageFiles, 5, _streamingCancellationSource.Token);

                if (success)
                {
                    ShowStatusMessage("Real-time streaming completed successfully!");
                }
                else
                {
                    ShowErrorMessage("Real-time streaming failed");
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Real-time streaming failed: {ex.Message}");
            }
            finally
            {
                StopKeepAliveTimer();
                _isStreamingInProgress = false;
                ShowStreamingStatus(false);
                UpdateControlStates();
                _streamingCancellationSource?.Dispose();
                _streamingCancellationSource = null;
            }
        }

        private async void Mp4StreamingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isStreamingInProgress)
            {
                ShowErrorMessage("Streaming operation already in progress. Please wait...");
                return;
            }

            if (_displayController == null)
            {
                ShowErrorMessage("Device not connected");
                return;
            }

            // Let user select MP4 file
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select MP4 Video File",
                Filter = "MP4 Video Files (*.mp4)|*.mp4|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() != true)
                return;

            try
            {
                _isStreamingInProgress = true;
                _streamingCancellationSource = new CancellationTokenSource();
                UpdateControlStates();
                HideErrorMessage();
                ShowStreamingStatus(true);

                StartKeepAliveTimer();

                ShowStatusMessage("Starting MP4 streaming...");

                bool success = await SendMp4DataWithDeviceSync(
                    _displayController,
                    openFileDialog.FileName,
                    33, // 30fps
                    1,
                    _streamingCancellationSource.Token);

                if (success)
                {
                    ShowStatusMessage("MP4 streaming completed successfully!");
                }
                else
                {
                    ShowErrorMessage("MP4 streaming failed");
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
                UpdateControlStates();
                _streamingCancellationSource?.Dispose();
                _streamingCancellationSource = null;
            }
        }

        private async void OfflineStreamingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isStreamingInProgress)
            {
                ShowErrorMessage("Streaming operation already in progress. Please wait...");
                return;
            }

            if (_displayController == null)
            {
                ShowErrorMessage("Device not connected");
                return;
            }

            if (_selectedOfflineFiles.Count == 0)
            {
                ShowErrorMessage("Please select files for offline streaming first");
                return;
            }

            try
            {
                _isStreamingInProgress = true;
                UpdateControlStates();
                HideErrorMessage();

                ShowStatusMessage("Setting up offline streaming...");

                // Execute offline streaming setup
                bool success = await SetupOfflineStreaming(_displayController, _selectedOfflineFiles);

                if (success)
                {
                    ShowStatusMessage("Offline streaming setup completed successfully!");
                }
                else
                {
                    ShowErrorMessage("Offline streaming setup failed");
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Offline streaming setup failed: {ex.Message}");
            }
            finally
            {
                _isStreamingInProgress = false;
                UpdateControlStates();
            }
        }

        private void SelectImagesButton_Click(object sender, RoutedEventArgs e)
        {
            // Use WPF FolderBrowserDialog alternative
            var folderDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select folder containing images for real-time streaming",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Folder Selection"
            };

            if (folderDialog.ShowDialog() == true)
            {
                _selectedImagesFolder = System.IO.Path.GetDirectoryName(folderDialog.FileName);
                if (!string.IsNullOrEmpty(_selectedImagesFolder))
                {
                    ImagesFolderPath.Text = _selectedImagesFolder;

                    // Count supported image files
                    var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };
                    var imageCount = Directory.GetFiles(_selectedImagesFolder)
                                             .Count(f => supportedExtensions.Contains(Path.GetExtension(f)));

                    ShowStatusMessage($"Selected folder with {imageCount} image files");
                }
            }
        }

        private void SelectOfflineFilesButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Files for Offline Streaming",
                Filter = "Image and Video Files (*.jpg;*.jpeg;*.png;*.bmp;*.mp4)|*.jpg;*.jpeg;*.png;*.bmp;*.mp4|Image Files (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|Video Files (*.mp4)|*.mp4|All Files (*.*)|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _selectedOfflineFiles = openFileDialog.FileNames.ToList();
                ShowStatusMessage($"Selected {_selectedOfflineFiles.Count} files for offline streaming");
            }
        }

        private async void ClearOfflineFilesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_displayController == null)
            {
                ShowErrorMessage("Device not connected");
                return;
            }

            try
            {
                ShowStatusMessage("Clearing offline files...");

                bool success = await _displayController.DeleteSuspendFilesWithResponse();

                if (success)
                {
                    ShowStatusMessage("Offline files cleared successfully!");
                }
                else
                {
                    ShowErrorMessage("Failed to clear offline files");
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Failed to clear offline files: {ex.Message}");
            }
        }

        #endregion

        #region Streaming Implementation

        private async Task<bool> EnhancedRealTimeDisplayDemo(DisplayController controller, List<string> imagePaths, int cycleCount = 5, CancellationToken cancellationToken = default)
        {
            try
            {
                // Ensure device is awake
                var wakeResponse = await controller.SendCmdDisplayInSleepWithResponse(false);
                if (wakeResponse?.IsSuccess != true)
                {
                    Debug.WriteLine("Failed to wake device from sleep");
                    return false;
                }
                await Task.Delay(1500, cancellationToken);

                // Enable real-time display
                var enableResponse = await controller.SendCmdRealTimeDisplayWithResponse(true);
                if (enableResponse?.IsSuccess != true)
                {
                    Debug.WriteLine("Failed to enable real-time display");
                    return false;
                }

                // Set brightness and keep-alive timer
                await controller.SendCmdBrightnessWithResponse(80);
                await controller.SendCmdSetKeepAliveTimerWithResponse(60);

                byte transferId = 0;
                for (int cycle = 0; cycle < cycleCount && !cancellationToken.IsCancellationRequested; cycle++)
                {
                    foreach (var imagePath in imagePaths)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        Debug.WriteLine($"[SENDING] Sending: {Path.GetFileName(imagePath)}");

                        controller.SendFileFromDisk(imagePath, transferId: transferId);

                        // Update the UI with current image
                        UpdateStreamingImageViewer(imagePath);

                        await Task.Delay(2000, cancellationToken);

                        transferId++;
                        if (transferId > 59) transferId = 4;
                    }
                }

                // Disable real-time display
                await controller.SendCmdRealTimeDisplayWithResponse(false);

                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Real-time display demo was cancelled");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Enhanced demo failed: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SendMp4DataWithDeviceSync(DisplayController controller, string mp4FilePath, int frameDelayMs = 33, int cycleCount = 1, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(mp4FilePath))
                {
                    Debug.WriteLine($"MP4 file not found: {mp4FilePath}");
                    return false;
                }

                // Enable real-time display
                var enableResponse = await controller.SendCmdRealTimeDisplayWithResponse(true);
                if (enableResponse?.IsSuccess != true)
                {
                    Debug.WriteLine("Failed to enable real-time display");
                    return false;
                }

                await controller.SendCmdBrightnessWithResponse(80);
                await controller.SendCmdSetKeepAliveTimerWithResponse(60);

                // For this simplified version, we'll just send the MP4 file directly
                // In a real implementation, you would decode frames and send them individually
                byte transferId = 0;

                ShowStatusMessage("Note: MP4 streaming requires video processing. This is a simplified implementation.");

                // Send the MP4 file as-is for now
                controller.SendFileFromDisk(mp4FilePath, transferId: transferId);

                await Task.Delay(frameDelayMs * 30, cancellationToken); // Simulate some processing time

                await controller.SendCmdRealTimeDisplayWithResponse(false);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MP4 streaming failed: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SetupOfflineStreaming(DisplayController controller, List<string> filePaths)
        {
            try
            {
                // Set suspend mode
                bool suspendSuccess = await controller.SetSuspendModeWithResponse();
                if (!suspendSuccess)
                {
                    return false;
                }

                // Send multiple suspend files
                bool filesSuccess = await controller.SendMultipleSuspendFilesWithResponse(filePaths, startingTransferId: 2);
                if (!filesSuccess)
                {
                    Debug.WriteLine("Failed to send suspend files");
                    return false;
                }

                // Set brightness and keep alive timer
                await controller.SendCmdBrightnessWithResponse(80);
                await controller.SendCmdSetKeepAliveTimerWithResponse(60);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Offline streaming setup failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Keep Alive Timer

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
                _keepAliveTimer = new Timer(4000); // 4 seconds interval
                _keepAliveTimer.Elapsed += OnKeepAliveTimerElapsed;
                _keepAliveTimer.AutoReset = true;
                _keepAliveTimer.Start();

                Debug.WriteLine("Keep-alive timer started (4-second intervals)");
            }
        }

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
                    Debug.WriteLine("Keep-alive timer stopped");
                }
            }
        }

        private async void OnKeepAliveTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (!_keepAliveEnabled || _displayController == null)
            {
                return;
            }

            try
            {
                // Send keep-alive packet to device
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var response = await _displayController.SendCmdKeepAliveWithResponse(timestamp);

                Debug.WriteLine($"Keep-alive sent at {DateTime.Now:HH:mm:ss}, Status: {response?.IsSuccess}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending keep-alive: {ex.Message}");
            }
        }

        #endregion

        #region UI Updates

        private void UpdateStreamingImageViewer(string imagePath)
        {
            try
            {
                if (File.Exists(imagePath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    Dispatcher.Invoke(() =>
                    {
                        StreamingImageViewer.Source = bitmap;
                        StreamingImageViewer.Visibility = Visibility.Visible;
                        PlaceholderContent.Visibility = Visibility.Collapsed;
                        LoadingIndicator.Visibility = Visibility.Collapsed;

                        CurrentImageName.Text = Path.GetFileName(imagePath);
                        ImageDimensions.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight}";

                        _streamedImageCount++;
                        ImageCounter.Text = _streamedImageCount.ToString();
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update streaming image viewer: {ex.Message}");
            }
        }

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

                    if (StreamingImageViewer.Source == null)
                    {
                        PlaceholderContent.Visibility = Visibility.Visible;
                        StreamingImageViewer.Visibility = Visibility.Collapsed;
                    }
                }
            });
        }

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

        private void UpdateActiveDeviceCount()
        {
            var count = _multiDeviceManager?.GetActiveControllers().Count ?? (_displayController != null ? 1 : 0);
            ActiveDeviceCount.Text = count.ToString();
        }

        private void UpdateControlStates()
        {
            bool canOperate = !_isStreamingInProgress && _displayController != null;

            // Update button states
            RealtimeStreamingButton.IsEnabled = canOperate && !string.IsNullOrEmpty(_selectedImagesFolder);
            Mp4StreamingButton.IsEnabled = canOperate;
            OfflineStreamingButton.IsEnabled = canOperate && _selectedOfflineFiles.Count > 0;

            SelectImagesButton.IsEnabled = canOperate;
            SelectOfflineFilesButton.IsEnabled = canOperate;
            ClearOfflineFilesButton.IsEnabled = canOperate;

            // Update display control states
            SetRotationButtonsEnabled(canOperate);
            BrightnessSlider.IsEnabled = canOperate;

            // Update streaming button text based on state
            if (_isStreamingInProgress)
            {
                RealtimeStreamingButton.Content = "Streaming...";
                Mp4StreamingButton.Content = "Streaming MP4...";
                OfflineStreamingButton.Content = "Setting up...";
            }
            else
            {
                RealtimeStreamingButton.Content = "Start Real-time Streaming";
                Mp4StreamingButton.Content = "Stream MP4 Video";
                OfflineStreamingButton.Content = "Setup Offline Streaming";
            }
        }

        #endregion

        #region Status Messages

        private void ShowStatusMessage(string message)
        {
            try
            {
                Debug.WriteLine($"Status: {message}");

                StatusText.Text = message;
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(33, 150, 243));
                StatusBorder.Visibility = Visibility.Visible;

                // Auto-hide status message after 3 seconds
                _statusTimer?.Stop();
                _statusTimer = new Timer(3000);
                _statusTimer.Elapsed += (s, e) =>
                {
                    Dispatcher.Invoke(() => StatusBorder.Visibility = Visibility.Collapsed);
                    _statusTimer?.Stop();
                };
                _statusTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing status message: {ex}");
            }
        }

        private void ShowErrorMessage(string message)
        {
            try
            {
                Debug.WriteLine($"Error: {message}");

                ErrorMessageTextBlock.Text = message;
                ErrorMessageBorder.Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0x44, 0x44));
                ErrorMessageBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x66, 0x66));
                ErrorMessageBorder.Visibility = Visibility.Visible;

                // Auto-hide error message after 5 seconds
                _statusTimer?.Stop();
                _statusTimer = new Timer(5000);
                _statusTimer.Elapsed += (s, e) =>
                {
                    Dispatcher.Invoke(() => ErrorMessageBorder.Visibility = Visibility.Collapsed);
                    _statusTimer?.Stop();
                };
                _statusTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing error message: {ex}");
            }
        }

        private void HideErrorMessage()
        {
            ErrorMessageBorder.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}