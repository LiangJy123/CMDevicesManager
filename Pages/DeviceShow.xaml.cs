using Microsoft.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
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
using HID.DisplayController;
using HidApi;
using CMDevicesManager.Services;

using Path = System.IO.Path;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for DeviceShow.xaml
    /// </summary>
    public partial class DeviceShow : Page, INotifyPropertyChanged
    {
        private readonly string? _appFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        private Dictionary<string, SceneInfo> _scenes = new();

        // HID Device Control fields
        private HidDeviceService? _hidDeviceService;
        private int _currentRotation = 0;
        private int _currentBrightness = 80;
        private bool _isBrightnessSliderUpdating = false;

        private InteractiveSkiaRenderingService? _renderService;

        // Observable collection for data binding
        public ObservableCollection<SceneInfo> Scenes { get; } = new ObservableCollection<SceneInfo>();

        public DeviceShow()
        {
            InitializeComponent();
            DataContext = this; // Set DataContext for binding
            this.Loaded += DeviceShow_Loaded;
            this.Unloaded += Page_Unloaded; // Add unloaded event handler
            InitializeDisplayControls();
        }

        /// <summary>
        /// Initialize HID device display control states
        /// </summary>
        private void InitializeDisplayControls()
        {
            // Set initial brightness slider value - with null check
            if (BrightnessSlider != null)
            {
                BrightnessSlider.Value = _currentBrightness;
            }
            if (BrightnessValueText != null)
            {
                BrightnessValueText.Text = $"{_currentBrightness}%";
            }

            // Set initial rotation display - with null check
            if (CurrentRotationText != null)
            {
                CurrentRotationText.Text = $"Current: {_currentRotation}°";
            }

            // Update rotation button appearance to show current selection
            UpdateRotationButtonAppearance(_currentRotation);

        }

        private async void DeviceShow_Loaded(object sender, RoutedEventArgs e)
        {
            // Get all scenes from the Scenes folder
            var scenesFolder = Path.Combine(_appFolder ?? string.Empty, "Scenes");

            _scenes = GetScenesWithJsonFiles(scenesFolder);
            
            // Populate the observable collection for data binding
            Scenes.Clear();
            foreach (var scene in _scenes.Values)
            {
                Scenes.Add(scene);
            }
            
            Console.WriteLine($"Total scenes with JSON files: {_scenes.Count}");

            // Initialize HID device service
            InitializeHidDeviceService();

            await InitializeDesignerAsync();
            // if want to load a default scene, do it here
        }

        private async Task InitializeDesignerAsync()
        {
            try
            {

                //_renderService = new InteractiveWin2DRenderingService();
                _renderService = new InteractiveSkiaRenderingService();
                await _renderService.InitializeAsync(480, 480);

                // Subscribe to events
                _renderService.ImageRendered += OnFrameRendered;

                await _renderService.ResetBackgroundToDefaultAsync();

            }
            catch (Exception ex)
            {
            }
        }

        private void OnFrameRendered(WriteableBitmap bitmap)
        {
            if (Dispatcher.CheckAccess())
            {
                DeviceImageViewer.Source = bitmap;
                //RenderInfoLabel.Text = $"Rendering at {_renderService?.TargetFPS ?? 30} FPS";
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    DeviceImageViewer.Source = bitmap;
                    //RenderInfoLabel.Text = $"Rendering at {_renderService?.TargetFPS ?? 30} FPS";
                });
            }
        }


        /// <summary>
        /// Loads a scene from the specified JSON file path
        /// </summary>
        /// <param name="jsonFilePath">Full path to the JSON scene file</param>
        private async Task LoadSceneFromPath(string jsonFilePath)
        {
            if (_renderService == null)
            {
                Debug.WriteLine("Render service not initialized", true);
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(jsonFilePath))
                {
                    Debug.WriteLine("Invalid scene file path", true);
                    return;
                }

                if (!File.Exists(jsonFilePath))
                {
                    Debug.WriteLine($"Scene file not found: {Path.GetFileName(jsonFilePath)}", true);
                    return;
                }

                Debug.WriteLine($"Loading scene: {Path.GetFileName(jsonFilePath)}...", false);

                // Read the scene JSON data
                var jsonData = await File.ReadAllTextAsync(jsonFilePath);

                if (string.IsNullOrWhiteSpace(jsonData))
                {
                    Debug.WriteLine("Scene file is empty or invalid", true);
                    return;
                }

                // Start rendering if not already started
                if (!_renderService.IsAutoRenderingEnabled)
                {
                    StartRendering();
                }

                // Clear any existing elements before importing
                _renderService.ClearElements();

                // Import the scene data
                var success = await _renderService.ImportSceneFromJsonAsync(jsonData);

                if (success)
                {

                    // Log successful loading
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded scene from: {jsonFilePath}");
                }
                else
                {
                    Debug.WriteLine($"Failed to import scene: {Path.GetFileName(jsonFilePath)}", true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Scene loading error: {ex}");
            }
        }

        #region Helper Methods
        private async void StartRendering()
        {
            if (_renderService == null) return;

            try
            {
                await _renderService.EnableHidTransfer(true, useSuspendMedia: false);
                await _renderService.EnableHidRealTimeDisplayAsync(true);
                _renderService.StartAutoRendering(_renderService.TargetFPS);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start rendering: {ex.Message}", true);
            }
        }

        private async void StopRendering()
        {
            if (_renderService == null) return;

            try
            {
                _renderService.StopAutoRendering();
                await _renderService.EnableHidTransfer(false);
                await _renderService.EnableHidRealTimeDisplayAsync(false);

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to stop rendering: {ex.Message}", true);
            }
        }

        #endregion



        /// <summary>
        /// Initialize HID device service for controlling devices
        /// </summary>
        private void InitializeHidDeviceService()
        {
            try
            {
                // Use HidDeviceService from ServiceLocator instead of creating new MultiDeviceManager
                _hidDeviceService = ServiceLocator.HidDeviceService;

                // Set up event handlers for HidDeviceService events
                _hidDeviceService.DeviceConnected += OnDeviceConnected;
                _hidDeviceService.DeviceDisconnected += OnDeviceDisconnected;
                _hidDeviceService.DeviceError += OnDeviceError;

                // Load current device settings if devices are already connected
                if (_hidDeviceService.ConnectedDeviceCount > 0)
                {
                    Task.Run(LoadCurrentDeviceSettings);
                }

                Debug.WriteLine($"HID Device Service initialized. Connected devices: {_hidDeviceService.ConnectedDeviceCount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize HID Device Service: {ex.Message}");
            }
        }

        // Event handlers for HidDeviceService
        private void OnDeviceConnected(object? sender, DeviceEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Debug.WriteLine($"HID Device connected: {e.Device.ProductString}");
                // Load current device settings when a device is added
                Task.Run(LoadCurrentDeviceSettings);
            });
        }

        private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Debug.WriteLine($"HID Device disconnected: {e.Device.ProductString}");
            });
        }

        private void OnDeviceError(object? sender, DeviceErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Debug.WriteLine($"HID Device Error - {e.Device.ProductString}: {e.Exception.Message}");
            });
        }

        /// <summary>
        /// Load current device settings and update UI accordingly
        /// </summary>
        private async void LoadCurrentDeviceSettings()
        {
            if (_hidDeviceService == null || !_hidDeviceService.IsInitialized)
                return;

            if (_hidDeviceService.ConnectedDeviceCount == 0)
                return;

            try
            {
                // Get current settings from the first device using HidDeviceService GetActiveControllers
                var activeControllers = _hidDeviceService.GetActiveControllers();
                if (activeControllers.Count == 0)
                    return;

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
                                    if (CurrentRotationText != null)
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
                                    if (BrightnessSlider != null)
                                        BrightnessSlider.Value = currentBrightness;
                                    if (BrightnessValueText != null)
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

        #region HID Device Controls

        /// <summary>
        /// Update the appearance of rotation buttons to highlight the selected one
        /// </summary>
        /// <param name="selectedRotation">Currently selected rotation value</param>
        private void UpdateRotationButtonAppearance(int selectedRotation)
        {
            // Reset all buttons to default style - with null checks
            var defaultColor = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            var selectedColor = new SolidColorBrush(Color.FromRgb(0x2A, 0x4A, 0x8B));

            if (Rotation0Button != null) Rotation0Button.Background = defaultColor;
            if (Rotation90Button != null) Rotation90Button.Background = defaultColor;
            if (Rotation180Button != null) Rotation180Button.Background = defaultColor;
            if (Rotation270Button != null) Rotation270Button.Background = defaultColor;

            // Highlight the selected button
            switch (selectedRotation)
            {
                case 0:
                    if (Rotation0Button != null) Rotation0Button.Background = selectedColor;
                    break;
                case 90:
                    if (Rotation90Button != null) Rotation90Button.Background = selectedColor;
                    break;
                case 180:
                    if (Rotation180Button != null) Rotation180Button.Background = selectedColor;
                    break;
                case 270:
                    if (Rotation270Button != null) Rotation270Button.Background = selectedColor;
                    break;
            }
        }

        /// <summary>
        /// Handle rotation button clicks
        /// </summary>
        private async void RotationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_hidDeviceService == null || !_hidDeviceService.IsInitialized)
            {
                Debug.WriteLine("HID Device Service not initialized");
                return;
            }

            if (_hidDeviceService.ConnectedDeviceCount == 0)
            {
                Debug.WriteLine("No devices connected for rotation control");
                return;
            }

            if (sender is Button button && button.Tag is string rotationString)
            {
                if (int.TryParse(rotationString, out int rotation))
                {
                    try
                    {
                        // Disable rotation buttons during operation
                        SetRotationButtonsEnabled(false);

                        Debug.WriteLine($"Setting rotation to {rotation}° on {_hidDeviceService.ConnectedDeviceCount} device(s)...");

                        // Send rotation command to all devices using HidDeviceService
                        var results = await _hidDeviceService.SetRotationAsync(rotation);

                        // Check results
                        int successCount = results.Values.Count(success => success);
                        if (successCount == results.Count)
                        {
                            _currentRotation = rotation;
                            if (CurrentRotationText != null)
                                CurrentRotationText.Text = $"Current: {rotation}°";
                            UpdateRotationButtonAppearance(rotation);
                            Debug.WriteLine($"Rotation set to {rotation}° on all {successCount} device(s) successfully!");
                        }
                        else
                        {
                            Debug.WriteLine($"Rotation setting completed with mixed results: {successCount}/{results.Count} devices succeeded");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to set rotation: {ex.Message}");
                    }
                    finally
                    {
                        // Re-enable rotation buttons
                        SetRotationButtonsEnabled(true);
                    }
                }
                else
                {
                    Debug.WriteLine("Invalid rotation value");
                }
            }
        }

        /// <summary>
        /// Enable or disable rotation buttons
        /// </summary>
        /// <param name="enabled">Whether buttons should be enabled</param>
        private void SetRotationButtonsEnabled(bool enabled)
        {
            if (Rotation0Button != null) Rotation0Button.IsEnabled = enabled;
            if (Rotation90Button != null) Rotation90Button.IsEnabled = enabled;
            if (Rotation180Button != null) Rotation180Button.IsEnabled = enabled;
            if (Rotation270Button != null) Rotation270Button.IsEnabled = enabled;
        }

        /// <summary>
        /// Handle brightness slider value changes
        /// </summary>
        private async void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isBrightnessSliderUpdating || _hidDeviceService == null || !_hidDeviceService.IsInitialized)
                return;

            int newBrightness = (int)Math.Round(e.NewValue);

            // Update the display text immediately for responsive UI
            if (BrightnessValueText != null)
                BrightnessValueText.Text = $"{newBrightness}%";

            // Only send command if the value actually changed significantly
            if (Math.Abs(newBrightness - _currentBrightness) < 1)
                return;

            try
            {
                if (_hidDeviceService.ConnectedDeviceCount == 0)
                {
                    Debug.WriteLine("No devices connected for brightness control");
                    return;
                }

                // Disable slider during operation to prevent multiple rapid calls
                if (BrightnessSlider != null)
                    BrightnessSlider.IsEnabled = false;

                // Send brightness command to all devices using HidDeviceService
                var results = await _hidDeviceService.SetBrightnessAsync(newBrightness);

                // Check results
                int successCount = results.Values.Count(success => success);
                if (successCount == results.Count)
                {
                    _currentBrightness = newBrightness;
                    Debug.WriteLine($"Brightness set to {newBrightness}% on all {successCount} device(s) successfully!");
                }
                else
                {
                    Debug.WriteLine($"Brightness setting completed with mixed results: {successCount}/{results.Count} devices succeeded");

                    // Revert slider to previous value on failure
                    _isBrightnessSliderUpdating = true;
                    if (BrightnessSlider != null)
                        BrightnessSlider.Value = _currentBrightness;
                    if (BrightnessValueText != null)
                        BrightnessValueText.Text = $"{_currentBrightness}%";
                    _isBrightnessSliderUpdating = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set brightness: {ex.Message}");

                // Revert slider to previous value on error
                _isBrightnessSliderUpdating = true;
                if (BrightnessSlider != null)
                    BrightnessSlider.Value = _currentBrightness;
                if (BrightnessValueText != null)
                    BrightnessValueText.Text = $"{_currentBrightness}%";
                _isBrightnessSliderUpdating = false;
            }
            finally
            {
                // Re-enable slider
                if (BrightnessSlider != null)
                    BrightnessSlider.IsEnabled = true;
            }
        }

        /// <summary>
        /// Handle quick brightness button clicks
        /// </summary>
        private async void QuickBrightnessButton_Click(object sender, RoutedEventArgs e)
        {
            if (_hidDeviceService == null || !_hidDeviceService.IsInitialized)
            {
                Debug.WriteLine("HID Device Service not initialized");
                return;
            }

            if (_hidDeviceService.ConnectedDeviceCount == 0)
            {
                Debug.WriteLine("No devices connected for brightness control");
                return;
            }

            if (sender is Button button && button.Tag is string brightnessString)
            {
                if (int.TryParse(brightnessString, out int brightness))
                {
                    try
                    {
                        // Update UI immediately
                        _isBrightnessSliderUpdating = true;
                        if (BrightnessSlider != null)
                            BrightnessSlider.Value = brightness;
                        if (BrightnessValueText != null)
                            BrightnessValueText.Text = $"{brightness}%";
                        _isBrightnessSliderUpdating = false;

                        Debug.WriteLine($"Setting brightness to {brightness}% on {_hidDeviceService.ConnectedDeviceCount} device(s)...");

                        // Send brightness command to all devices using HidDeviceService
                        var results = await _hidDeviceService.SetBrightnessAsync(brightness);

                        // Check results
                        int successCount = results.Values.Count(success => success);
                        if (successCount == results.Count)
                        {
                            _currentBrightness = brightness;
                            Debug.WriteLine($"Brightness set to {brightness}% on all {successCount} device(s) successfully!");
                        }
                        else
                        {
                            Debug.WriteLine($"Brightness setting completed with mixed results: {successCount}/{results.Count} devices succeeded");

                            // Revert UI to previous value on failure
                            _isBrightnessSliderUpdating = true;
                            if (BrightnessSlider != null)
                                BrightnessSlider.Value = _currentBrightness;
                            if (BrightnessValueText != null)
                                BrightnessValueText.Text = $"{_currentBrightness}%";
                            _isBrightnessSliderUpdating = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to set brightness: {ex.Message}");

                        // Revert UI to previous value on error
                        _isBrightnessSliderUpdating = true;
                        if (BrightnessSlider != null)
                            BrightnessSlider.Value = _currentBrightness;
                        if (BrightnessValueText != null)
                            BrightnessValueText.Text = $"{_currentBrightness}%";
                        _isBrightnessSliderUpdating = false;
                    }
                }
                else
                {
                    Debug.WriteLine("Invalid brightness value");
                }
            }
        }

        #endregion

        /// <summary>
        /// Cleanup resources when page is unloaded
        /// </summary>
        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Unsubscribe from HidDeviceService events when page is unloaded
                if (_hidDeviceService != null)
                {
                    _hidDeviceService.DeviceConnected -= OnDeviceConnected;
                    _hidDeviceService.DeviceDisconnected -= OnDeviceDisconnected;
                    _hidDeviceService.DeviceError -= OnDeviceError;
                }

                StopRendering();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during page cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Event handler for Edit button click - navigates to DesignerPage to edit the selected scene
        /// </summary>
        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button btn && btn.DataContext is SceneInfo sceneInfo)
                {

                    // Construct the full path to the scene JSON file
                    var sceneFilePath = Path.Combine(_appFolder ?? string.Empty, "Scenes", sceneInfo.SceneId, sceneInfo.SceneFileName);
                    
                    if (File.Exists(sceneFilePath))
                    {
                        // Navigate to DesignerPage with the JSON file path for automatic loading
                        var designerPage = new DesignerPage(sceneFilePath);
                        NavigationService?.Navigate(designerPage);
                        
                        Debug.WriteLine($"Navigating to DesignerPage to edit scene: {sceneInfo.DisplayName} (ID: {sceneInfo.SceneId})");
                        Debug.WriteLine($"Scene file path: {sceneFilePath}");
                    }
                    else
                    {
                        // File not found - show error and navigate to empty designer
                        var errorMessage = $"Scene file not found: {sceneInfo.SceneFileName}\n\nWould you like to open the Designer anyway?";
                        var result = System.Windows.MessageBox.Show(errorMessage, "File Not Found", 
                            MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        
                        if (result == MessageBoxResult.Yes)
                        {
                            var designerPage = new DesignerPage();
                            NavigationService?.Navigate(designerPage);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Navigation to DesignerPage failed: {ex}");
                System.Windows.MessageBox.Show($"Failed to open scene for editing: {ex.Message}", "Navigation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Event handler for Load button click - loads/plays the selected scene using InteractiveSkiaRenderingService
        /// </summary>
        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button btn && btn.DataContext is SceneInfo sceneInfo)
                {

                    Debug.WriteLine($"Loading scene: {sceneInfo.DisplayName} (ID: {sceneInfo.SceneId})");

                    // Construct the full path to the scene JSON file
                    var sceneFilePath = Path.Combine(_appFolder ?? string.Empty, "Scenes", sceneInfo.SceneId, sceneInfo.SceneFileName);
                    if (!string.IsNullOrEmpty(sceneFilePath))
                    {
                        await LoadSceneFromPath(sceneFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load scene: {ex}");
            }
        }

        private Dictionary<string, SceneInfo> GetScenesWithJsonFiles(string scenesFolder)
        {
            var scenes = new Dictionary<string, SceneInfo>();

            try
            {
                if (!Directory.Exists(scenesFolder))
                {
                    Console.WriteLine($"Scenes folder not found: {scenesFolder}");
                    return scenes;
                }

                var sceneDirectories = Directory.GetDirectories(scenesFolder);

                foreach (var sceneDir in sceneDirectories)
                {
                    try
                    {
                        var sceneId = Path.GetFileName(sceneDir);
                        var jsonFiles = Directory.GetFiles(sceneDir, "*.json", SearchOption.TopDirectoryOnly);

                        if (jsonFiles.Length > 0)
                        {
                            // Parse the first JSON file found in the scene directory
                            var sceneInfo = ParseSceneJsonFile(jsonFiles[0], sceneId, sceneDir);
                            if (sceneInfo != null)
                            {
                                scenes[sceneId] = sceneInfo;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing scene directory {sceneDir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing scenes folder {scenesFolder}: {ex.Message}");
            }

            return scenes;
        }

        private SceneInfo? ParseSceneJsonFile(string jsonFilePath, string directorySceneId, string sceneDirectory)
        {
            try
            {
                if (!File.Exists(jsonFilePath))
                {
                    Console.WriteLine($"JSON file not found: {jsonFilePath}");
                    return null;
                }

                var jsonContent = File.ReadAllText(jsonFilePath);

                // Parse JSON using System.Text.Json
                using var document = JsonDocument.Parse(jsonContent);
                var root = document.RootElement;

                var sceneInfo = new SceneInfo
                {
                    SceneId = directorySceneId // Use directory name as fallback
                };

                // Parse required fields from JSON
                if (root.TryGetProperty("sceneName", out var sceneNameElement))
                {
                    sceneInfo.SceneName = sceneNameElement.GetString() ?? "Unknown Scene";
                }

                if (root.TryGetProperty("sceneId", out var sceneIdElement))
                {
                    var jsonSceneId = sceneIdElement.GetString();
                    if (!string.IsNullOrEmpty(jsonSceneId))
                    {
                        sceneInfo.SceneId = jsonSceneId; // Override with JSON sceneId if available
                    }
                }

                if (root.TryGetProperty("coverImagePath", out var coverImagePathElement))
                {
                    var coverImagePath = coverImagePathElement.GetString();
                    if (!string.IsNullOrEmpty(coverImagePath))
                    {
                        // Convert relative path to absolute path
                        sceneInfo.CoverImagePath = Path.IsPathRooted(coverImagePath)
                            ? coverImagePath
                            : Path.Combine(_appFolder ?? string.Empty, coverImagePath);
                    }
                }

                if (root.TryGetProperty("sceneFileName", out var sceneFileNameElement))
                {
                    sceneInfo.SceneFileName = sceneFileNameElement.GetString() ?? Path.GetFileName(jsonFilePath);
                }

                return sceneInfo;
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"JSON parsing error in file {jsonFilePath}: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing scene JSON file {jsonFilePath}: {ex.Message}");
            }

            return null;
        }

        private List<string> GetAllSceneJsonFiles(string scenesFolder)
        {
            var jsonFiles = new List<string>();

            try
            {
                if (!Directory.Exists(scenesFolder))
                {
                    Console.WriteLine($"Scenes folder not found: {scenesFolder}");
                    return jsonFiles;
                }

                // Get all subdirectories in the Scenes folder (each represents a scene)
                var sceneDirectories = Directory.GetDirectories(scenesFolder);

                foreach (var sceneDir in sceneDirectories)
                {
                    try
                    {
                        // Look for JSON files in each scene directory root
                        var jsonFilesInScene = Directory.GetFiles(sceneDir, "*.json", SearchOption.TopDirectoryOnly);
                        jsonFiles.AddRange(jsonFilesInScene);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reading JSON files from scene directory {sceneDir}: {ex.Message}");
                    }
                }

                Console.WriteLine($"Found {jsonFiles.Count} JSON files in scenes folder");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing scenes folder {scenesFolder}: {ex.Message}");
            }

            return jsonFiles;
        }

        private void ProcessSceneJsonFiles(List<string> jsonFiles)
        {
            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    // Get the scene directory name (GUID)
                    var sceneDirectory = Path.GetDirectoryName(jsonFile);
                    var sceneId = Path.GetFileName(sceneDirectory);
                    var fileName = Path.GetFileName(jsonFile);

                    Console.WriteLine($"Scene: {sceneId}");
                    Console.WriteLine($"  JSON File: {fileName}");
                    Console.WriteLine($"  Full Path: {jsonFile}");

                    // Read and process JSON content if needed
                    if (File.Exists(jsonFile))
                    {
                        var jsonContent = File.ReadAllText(jsonFile);
                        Console.WriteLine($"  File Size: {new FileInfo(jsonFile).Length} bytes");

                        // You can deserialize the JSON here if needed
                        // var sceneData = System.Text.Json.JsonSerializer.Deserialize<YourSceneModel>(jsonContent);
                    }

                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing JSON file {jsonFile}: {ex.Message}");
                }
            }
        }

        // Enhanced helper class to hold comprehensive scene information
        public class SceneInfo : INotifyPropertyChanged
        {
            private string _sceneId = string.Empty;
            private string _sceneName = string.Empty;
            private string _coverImagePath = string.Empty;
            private string _sceneFileName = string.Empty;

            // Required fields from JSON
            public string SceneId 
            { 
                get => _sceneId;
                set
                {
                    _sceneId = value;
                    OnPropertyChanged();
                }
            }
            
            public string SceneName 
            { 
                get => _sceneName;
                set
                {
                    _sceneName = value;
                    OnPropertyChanged();
                }
            }
            
            public string CoverImagePath 
            { 
                get => _coverImagePath;
                set
                {
                    _coverImagePath = value;
                    OnPropertyChanged();
                }
            }
            
            public string SceneFileName 
            { 
                get => _sceneFileName;
                set
                {
                    _sceneFileName = value;
                    OnPropertyChanged();
                }
            }

            // Display property for UI binding
            public string DisplayName => string.IsNullOrEmpty(SceneName) ? "Unknown Scene" : SceneName;

            public event PropertyChangedEventHandler? PropertyChanged;
            protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                
                // Notify DisplayName when SceneName changes
                if (propertyName == nameof(SceneName))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}