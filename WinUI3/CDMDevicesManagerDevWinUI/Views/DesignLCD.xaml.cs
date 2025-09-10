using DevWinUIGallery.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CDMDevicesManagerDevWinUI.Views
{
    /// <summary>
    /// DesignLCD demonstrates the new background rendering architecture where:
    /// - CanvasControl rendering happens in a background service (BackgroundRenderingService)
    /// - UI displays rendered images via Image element instead of direct CanvasControl
    /// - Service provides offscreen rendering with configurable size and FPS
    /// - Service can run independently of UI thread for better performance
    /// - Enhanced with HID device integration for real-time JPEG streaming
    /// </summary>
    public sealed partial class DesignLCD : Page
    {
        private BackgroundRenderingService _renderingService;
        private bool _isRendering = false;
        private readonly List<int> _elementIds = new();
        private int _fpsCounter = 0;
        private DateTime _lastFpsUpdate = DateTime.Now;
        private CanvasBitmap _sampleImage;
        private int _hidFramesSent = 0;

        public DesignLCD()
        {
            this.InitializeComponent();
            InitializeService();
        }

        private async void InitializeService()
        {
            try
            {
                _renderingService = new BackgroundRenderingService();
                _renderingService.FrameReady += OnFrameReady;
                _renderingService.StatusChanged += OnStatusChanged;
                _renderingService.FpsUpdated += OnFpsUpdated;
                _renderingService.HidFrameSent += OnHidFrameSent;

                await _renderingService.InitializeAsync();

                // Load sample image directly using the service's canvas
                await LoadSampleImageAsync();

                // Start FPS counter
                var fpsTimer = new DispatcherTimer();
                fpsTimer.Interval = TimeSpan.FromSeconds(1);
                fpsTimer.Tick += UpdateFpsCounter;
                fpsTimer.Start();

                UpdateUI();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to initialize service: {ex.Message}";
            }
        }

        private async Task LoadSampleImageAsync()
        {
            try
            {
                // We'll load the sample image using a temporary CanvasDevice
                // since we need a device context to load CanvasBitmap
                var device = CanvasDevice.GetSharedDevice();
                var uri = new Uri("ms-appx:///Assets/sample.jpg");
                _sampleImage = await CanvasBitmap.LoadAsync(device, uri);
                StatusText.Text = "Background rendering service ready - you can start adding elements!";
            }
            catch (Exception ex)
            {
                // Sample image not available, we'll handle this gracefully
                StatusText.Text = $"Background rendering service ready - sample image loading failed: {ex.Message}";
            }
        }

        private void OnFrameReady(object sender, WriteableBitmap bitmap)
        {
            // Debug: Log that a frame was received
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            // Update the display image on UI thread
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    RenderDisplay.Source = bitmap;
                    StatusText.Text = $"Frame updated at {timestamp} - Size: {bitmap?.PixelWidth}x{bitmap?.PixelHeight} | HID Frames: {_hidFramesSent}";
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Error setting frame: {ex.Message}";
                }
            });
        }

        private void OnStatusChanged(object sender, string status)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                StatusText.Text = status;
            });
        }

        private void OnFpsUpdated(object sender, int fps)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                FpsText.Text = $"FPS: {fps}";
            });
        }

        private void OnHidFrameSent(object sender, HidFrameSentEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _hidFramesSent++;
                var status = $"HID Frame #{_hidFramesSent}: {e.FrameSize:N0} bytes sent to {e.DevicesSucceeded}/{e.DevicesTotal} devices at {e.Timestamp:HH:mm:ss.fff}";
                StatusText.Text = status;
            });
        }

        private async void OnStartStopClick(object sender, RoutedEventArgs e)
        {
            if (_isRendering)
            {
                await StopRenderingAsync();
            }
            else
            {
                await StartRenderingAsync();
            }
        }

        private async Task StartRenderingAsync()
        {
            try
            {
                // Start with HID real-time mode enabled by default
                await _renderingService.StartRenderingAsync(enableHidRealTime: true);
                _isRendering = true;
                StartStopButton.Content = "Stop Rendering";
                UpdateUI();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to start rendering: {ex.Message}";
            }
        }

        private async Task StopRenderingAsync()
        {
            // First disable HID real-time mode if enabled
            if (_renderingService.IsHidRealTimeModeEnabled)
            {
                await _renderingService.SetHidRealTimeModeAsync(false);
            }
            
            _renderingService.StopRendering();
            _isRendering = false;
            StartStopButton.Content = "Start Rendering";
            UpdateUI();
        }

        private async void OnCaptureFrameClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var bitmap = await _renderingService.CaptureFrameAsync();
                if (bitmap != null)
                {
                    // Optionally save the captured frame
                    var picker = new FileSavePicker();
                    picker.DefaultFileExtension = ".png";
                    picker.FileTypeChoices.Add("PNG Image", new List<string> { ".png" });
                    picker.FileTypeChoices.Add("JPEG Image", new List<string> { ".jpg", ".jpeg" });

                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                    var file = await picker.PickSaveFileAsync();
                    if (file != null)
                    {
                        if (file.FileType.ToLower() == ".png")
                        {
                            await SaveWriteableBitmapToFileAsync(bitmap, file);
                        }
                        else
                        {
                            await SaveWriteableBitmapAsJpegAsync(bitmap, file);
                        }
                        StatusText.Text = $"Frame saved to {file.Name} (Quality: {_renderingService.JpegQuality}%)";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error capturing frame: {ex.Message}";
            }
        }

        private void OnClearElementsClick(object sender, RoutedEventArgs e)
        {
            _renderingService.ClearElements();
            _elementIds.Clear();
            UpdateUI();
        }

        private void OnRenderSizeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_renderingService == null || RenderSizeComboBox.SelectedItem == null) return;

            var selectedItem = (ComboBoxItem)RenderSizeComboBox.SelectedItem;
            var sizeText = selectedItem.Content.ToString();

            switch (sizeText)
            {
                case "480x480":
                    _renderingService.SetRenderSize(480, 480);
                    break;
                case "800x600":
                    _renderingService.SetRenderSize(800, 600);
                    break;
                case "1024x768":
                    _renderingService.SetRenderSize(1024, 768);
                    break;
                case "1280x720":
                    _renderingService.SetRenderSize(1280, 720);
                    break;
            }

            RenderSizeText.Text = $"Size: {sizeText}";
        }

        private void OnFpsChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_renderingService == null || FpsComboBox.SelectedItem == null) return;

            var selectedItem = (ComboBoxItem)FpsComboBox.SelectedItem;
            var fpsText = selectedItem.Content.ToString();

            if (int.TryParse(fpsText, out int fps))
            {
                _renderingService.SetTargetFps(fps);
            }
        }

        private void OnAddLiveTimeClick(object sender, RoutedEventArgs e)
        {
            var config = new LiveTextConfig
            {
                FontSize = 48,
                TextColor = Colors.Cyan,
                TimeFormat = "HH:mm:ss",
                EnableGlow = GlowEffectCheckBox.IsChecked == true,
                GlowRadius = 5,
                GlowIntensity = 1.0f,
                EnableBreathing = BreathingEffectCheckBox.IsChecked == true,
                BreathingSpeed = 2.0f,
                MovementType = MovementEffectCheckBox.IsChecked == true ? MovementType.Horizontal : MovementType.None,
                MovementSpeed = 1.0f,
                MovementAmplitude = 20.0f
            };

            var center = new Vector2(_renderingService.TargetWidth / 2f - 100, _renderingService.TargetHeight / 2f - 100);
            var id = _renderingService.AddLiveTextElement("Current Time: {TIME}", center, config);

            if (id >= 0)
            {
                _elementIds.Add(id);
                UpdateUI();
                StatusText.Text = "Live time element added.";
            }
        }

        private void OnAddFloatingTextClick(object sender, RoutedEventArgs e)
        {
            var random = new Random();
            var config = new LiveTextConfig
            {
                FontSize = 24,
                TextColor = Colors.Yellow,
                EnableGlow = GlowEffectCheckBox.IsChecked == true,
                GlowRadius = 3,
                EnableBreathing = BreathingEffectCheckBox.IsChecked == true,
                MovementType = MovementEffectCheckBox.IsChecked == true ? MovementType.Circular : MovementType.None,
                MovementSpeed = 0.5f,
                MovementAmplitude = 30.0f
            };

            var position = new Vector2(
                random.Next(50, _renderingService.TargetWidth - 200),
                random.Next(50, _renderingService.TargetHeight - 100));

            var id = _renderingService.AddLiveTextElement($"Floating Text #{_elementIds.Count + 1}", position, config);

            if (id >= 0)
            {
                _elementIds.Add(id);
                UpdateUI();
                StatusText.Text = "Floating text element added.";
            }
        }

        private void OnAddParticleSystemClick(object sender, RoutedEventArgs e)
        {
            var random = new Random();
            var config = new ParticleConfig
            {
                MaxParticles = 50,
                ParticleLifetime = 3.0f,
                MinParticleSize = 2.0f,
                MaxParticleSize = 6.0f,
                MinSpeed = 20.0f,
                MaxSpeed = 80.0f,
                EmissionRadius = 30.0f,
                Gravity = new Vector2(0, 30.0f),
                ParticleColor = Colors.Orange
            };

            var position = new Vector2(
                random.Next(100, _renderingService.TargetWidth - 100),
                random.Next(100, _renderingService.TargetHeight - 100));

            var id = _renderingService.AddParticleSystem(position, config);

            if (id >= 0)
            {
                _elementIds.Add(id);
                UpdateUI();
                StatusText.Text = "Particle system added.";
            }
        }

        private void OnAddSampleImageClick(object sender, RoutedEventArgs e)
        {
            if (_sampleImage == null)
            {
                StatusText.Text = "No sample image available.";
                return;
            }

            var random = new Random();
            var config = new ImageConfig
            {
                Size = new Vector2(100, 100),
                BaseOpacity = 0.8f,
                EnablePulsing = true,
                PulseSpeed = 1.5f,
                RotationSpeed = 0.5f,
                MovementType = MovementEffectCheckBox.IsChecked == true ? MovementType.Figure8 : MovementType.None,
                MovementSpeed = 0.3f,
                MovementAmplitude = 50.0f
            };

            var position = new Vector2(
                random.Next(50, _renderingService.TargetWidth - 150),
                random.Next(50, _renderingService.TargetHeight - 150));

            var id = _renderingService.AddImageElement(_sampleImage, position, config);

            if (id >= 0)
            {
                _elementIds.Add(id);
                UpdateUI();
                StatusText.Text = "Sample image added.";
            }
        }

        private void OnTestSimpleTextClick(object sender, RoutedEventArgs e)
        {
            var id = _renderingService.AddSimpleText("Hello HID Devices! 🎨📱");

            if (id >= 0)
            {
                _elementIds.Add(id);
                UpdateUI();
                StatusText.Text = "Simple test text added for HID streaming.";
            }
            else
            {
                StatusText.Text = "Failed to add simple text - check service initialization.";
            }
        }

        private void OnGlowEffectChanged(object sender, RoutedEventArgs e)
        {
            StatusText.Text = $"Glow effect {(GlowEffectCheckBox.IsChecked == true ? "enabled" : "disabled")} for new elements.";
        }

        private void OnBreathingEffectChanged(object sender, RoutedEventArgs e)
        {
            StatusText.Text = $"Breathing animation {(BreathingEffectCheckBox.IsChecked == true ? "enabled" : "disabled")} for new elements.";
        }

        private void OnMovementEffectChanged(object sender, RoutedEventArgs e)
        {
            StatusText.Text = $"Movement animation {(MovementEffectCheckBox.IsChecked == true ? "enabled" : "disabled")} for new elements.";
        }

        private async void OnExportSettingsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileSavePicker();
                picker.DefaultFileExtension = ".json";
                picker.FileTypeChoices.Add("JSON Configuration", new List<string> { ".json" });

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    // Export current renderer settings
                    var settings = new
                    {
                        ElementCount = _elementIds.Count,
                        IsRendering = _isRendering,
                        RenderSize = new { Width = _renderingService.TargetWidth, Height = _renderingService.TargetHeight },
                        TargetFps = _renderingService.TargetFps,
                        HidIntegration = new
                        {
                            RealTimeModeEnabled = _renderingService.IsHidRealTimeModeEnabled,
                            JpegQuality = _renderingService.JpegQuality,
                            HidFramesSent = _hidFramesSent
                        },
                        EffectsEnabled = new
                        {
                            Glow = GlowEffectCheckBox.IsChecked,
                            Breathing = BreathingEffectCheckBox.IsChecked,
                            Movement = MovementEffectCheckBox.IsChecked
                        },
                        ExportedAt = DateTime.Now
                    };

                    var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    await FileIO.WriteTextAsync(file, json);

                    StatusText.Text = $"Settings exported to {file.Name}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error exporting settings: {ex.Message}";
            }
        }

        private void UpdateFpsCounter(object sender, object e)
        {
            if (_isRendering)
            {
                _fpsCounter++;
                var now = DateTime.Now;
                if ((now - _lastFpsUpdate).TotalSeconds >= 1.0)
                {
                    // FPS is now handled by the background service
                    _fpsCounter = 0;
                    _lastFpsUpdate = now;
                }
            }
            else
            {
                FpsText.Text = "FPS: 0";
            }
        }

        private void UpdateUI()
        {
            ElementCountText.Text = $"Elements: {_renderingService?.ElementCount ?? 0}";
            CaptureButton.IsEnabled = _isRendering;
            
            // Update HID status display
            if (_renderingService?.IsHidRealTimeModeEnabled == true)
            {
                StatusText.Text += $" | HID: Real-time ACTIVE | Quality: {_renderingService.JpegQuality}%";
            }
        }

        private async Task SaveWriteableBitmapToFileAsync(WriteableBitmap bitmap, StorageFile file)
        {
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                    Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);

                // Get pixel data from WriteableBitmap
                var pixelBuffer = bitmap.PixelBuffer;
                var pixels = pixelBuffer.ToArray();

                encoder.SetPixelData(
                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth,
                    (uint)bitmap.PixelHeight,
                    96, 96, pixels);

                await encoder.FlushAsync();
            }
        }

        private async Task SaveWriteableBitmapAsJpegAsync(WriteableBitmap bitmap, StorageFile file)
        {
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                    Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId, stream);

                // Get pixel data from WriteableBitmap
                var pixelBuffer = bitmap.PixelBuffer;
                var pixels = pixelBuffer.ToArray();

                encoder.SetPixelData(
                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth,
                    (uint)bitmap.PixelHeight,
                    96, 96, pixels);

                // Note: WinRT BitmapEncoder doesn't support SetPropertiesAsync for JPEG quality in the same way
                // The quality setting would need to be handled differently if precise control is needed
                
                await encoder.FlushAsync();
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _renderingService?.Dispose();
        }

        // Drag functionality event handlers
        private void OnRenderDisplayPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_renderingService == null) return;

            var image = sender as Image;
            var position = e.GetCurrentPoint(image).Position;

            // Convert UI coordinates to render target coordinates
            var renderPosition = ConvertUIToRenderCoordinates(position, image);

            if (_renderingService.OnPointerPressed(renderPosition))
            {
                image.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void OnRenderDisplayPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_renderingService == null || !_renderingService.IsDragging) return;

            var image = sender as Image;
            var position = e.GetCurrentPoint(image).Position;

            // Convert UI coordinates to render target coordinates
            var renderPosition = ConvertUIToRenderCoordinates(position, image);

            if (_renderingService.OnPointerMoved(renderPosition))
            {
                e.Handled = true;
            }
        }

        private void OnRenderDisplayPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_renderingService == null) return;

            var image = sender as Image;

            if (_renderingService.OnPointerReleased())
            {
                image.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Converts UI coordinates to render target coordinates
        /// </summary>
        /// <param name="uiPosition">Position in UI coordinates</param>
        /// <param name="image">The image element</param>
        /// <returns>Position in render target coordinates</returns>
        private Vector2 ConvertUIToRenderCoordinates(Windows.Foundation.Point uiPosition, Image image)
        {
            if (image.Source is not WriteableBitmap bitmap)
                return new Vector2((float)uiPosition.X, (float)uiPosition.Y);

            // Get the actual image size and display size
            var imageWidth = bitmap.PixelWidth;
            var imageHeight = bitmap.PixelHeight;
            var displayWidth = image.ActualWidth;
            var displayHeight = image.ActualHeight;

            // Calculate scaling factors considering Stretch="Uniform"
            var scaleX = displayWidth / imageWidth;
            var scaleY = displayHeight / imageHeight;
            var scale = Math.Min(scaleX, scaleY); // Uniform scaling uses the smaller scale

            // Calculate the actual displayed image area
            var displayedImageWidth = imageWidth * scale;
            var displayedImageHeight = imageHeight * scale;

            // Calculate offsets for centering
            var offsetX = (displayWidth - displayedImageWidth) / 2;
            var offsetY = (displayHeight - displayedImageHeight) / 2;

            // Convert UI position to render coordinates
            var renderX = (uiPosition.X - offsetX) / scale;
            var renderY = (uiPosition.Y - offsetY) / scale;

            return new Vector2((float)renderX, (float)renderY);
        }

        private void OnJpegQualityChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_renderingService == null || sender is not ComboBox comboBox || comboBox.SelectedItem == null) return;

            var selectedItem = (ComboBoxItem)comboBox.SelectedItem;
            var qualityText = selectedItem.Content.ToString().Replace("%", "");

            if (int.TryParse(qualityText, out int quality))
            {
                _renderingService.JpegQuality = quality;
                StatusText.Text = $"JPEG quality set to {quality}% for HID streaming";
            }
        }

        private async void OnHidRealTimeModeChanged(object sender, RoutedEventArgs e)
        {
            if (_renderingService == null || sender is not CheckBox checkBox) return;

            try
            {
                bool enableRealTime = checkBox.IsChecked == true;
                bool success = await _renderingService.SetHidRealTimeModeAsync(enableRealTime);
                
                if (success)
                {
                    StatusText.Text = $"HID real-time mode {(enableRealTime ? "enabled" : "disabled")} successfully";
                }
                else
                {
                    // Revert checkbox state if operation failed
                    checkBox.IsChecked = !enableRealTime;
                    StatusText.Text = $"Failed to {(enableRealTime ? "enable" : "disable")} HID real-time mode";
                }
            }
            catch (Exception ex)
            {
                // Revert checkbox state on exception
                checkBox.IsChecked = !checkBox.IsChecked;
                StatusText.Text = $"Error changing HID real-time mode: {ex.Message}";
            }
        }
    }
}