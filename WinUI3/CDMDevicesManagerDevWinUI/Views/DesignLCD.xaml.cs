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
using System.Linq;

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
            this.Loaded +=DesignLCD_Loaded;
            
        }

        private void DesignLCD_Loaded(object sender, RoutedEventArgs e)
        {
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
                SafeUpdateStatusText($"Failed to initialize service: {ex.Message}");
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
                SafeUpdateStatusText("Background rendering service ready - you can start adding elements!");
            }
            catch (Exception ex)
            {
                // Sample image not available, we'll handle this gracefully
                SafeUpdateStatusText($"Background rendering service ready - sample image loading failed: {ex.Message}");
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
                    if (RenderDisplay != null)
                    {
                        RenderDisplay.Source = bitmap;
                    }
                    SafeUpdateStatusText($"Frame updated at {timestamp} - Size: {bitmap?.PixelWidth}x{bitmap?.PixelHeight} | HID Frames: {_hidFramesSent}");
                }
                catch (Exception ex)
                {
                    SafeUpdateStatusText($"Error setting frame: {ex.Message}");
                }
            });
        }

        private void OnStatusChanged(object sender, string status)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                SafeUpdateStatusText(status);
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
                SafeUpdateStatusText(status);
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
                SafeUpdateStatusText($"Failed to start rendering: {ex.Message}");
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
                        SafeUpdateStatusText($"Frame saved to {file.Name} (Quality: {_renderingService.JpegQuality}%)");
                    }
                }
            }
            catch (Exception ex)
            {
                SafeUpdateStatusText($"Error capturing frame: {ex.Message}");
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
            // Use simplified config without referencing missing UI elements
            var config = new LiveTextConfig
            {
                FontSize = 48,
                TextColor = Colors.Cyan,
                TimeFormat = "HH:mm:ss",
                EnableGlow = true, // Default to true for demo
                GlowRadius = 5,
                GlowIntensity = 1.0f,
                EnableBreathing = true, // Default to true for demo
                BreathingSpeed = 2.0f,
                MovementType = MovementType.Horizontal, // Default movement
                MovementSpeed = 1.0f,
                MovementAmplitude = 20.0f
            };

            var center = new Vector2(_renderingService.TargetWidth / 2f - 100, _renderingService.TargetHeight / 2f - 100);
            var id = _renderingService.AddLiveTextElement("Current Time: {TIME}", center, config);

            if (id >= 0)
            {
                _elementIds.Add(id);
                UpdateUI();
                SafeUpdateStatusText("Live time element added with glow and breathing effects.");
            }
        }

        private void OnAddFloatingTextClick(object sender, RoutedEventArgs e)
        {
            var random = new Random();
            var config = new LiveTextConfig
            {
                FontSize = 24,
                TextColor = Colors.Yellow,
                EnableGlow = true, // Default to true for demo
                GlowRadius = 3,
                EnableBreathing = true, // Default to true for demo
                MovementType = MovementType.Circular, // Default to circular movement
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
                SafeUpdateStatusText("Floating text element added with circular movement.");
            }
        }

        private void OnAddSampleImageClick(object sender, RoutedEventArgs e)
        {
            if (_sampleImage == null)
            {
                SafeUpdateStatusText("No sample image available.");
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
                MovementType = MovementType.Figure8, // Default movement
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
                SafeUpdateStatusText("Sample image added with figure-8 movement.");
            }
        }

        private void OnGlowEffectChanged(object sender, RoutedEventArgs e)
        {
            SafeUpdateStatusText("Glow effect setting changed for new elements.");
        }

        private void OnBreathingEffectChanged(object sender, RoutedEventArgs e)
        {
            SafeUpdateStatusText("Breathing animation setting changed for new elements.");
        }

        private void OnMovementEffectChanged(object sender, RoutedEventArgs e)
        {
            SafeUpdateStatusText("Movement animation setting changed for new elements.");
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
                            Glow = true, // Default values since UI controls don't exist
                            Breathing = true,
                            Movement = true
                        },
                        ExportedAt = DateTime.Now
                    };

                    var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    await FileIO.WriteTextAsync(file, json);

                    SafeUpdateStatusText($"Settings exported to {file.Name}");
                }
            }
            catch (Exception ex)
            {
                SafeUpdateStatusText($"Error exporting settings: {ex.Message}");
            }
        }

        private void OnJpegQualityChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_renderingService == null || sender is not ComboBox comboBox || comboBox.SelectedItem == null) return;

            var selectedItem = (ComboBoxItem)comboBox.SelectedItem;
            var qualityText = selectedItem.Content.ToString().Replace("%", "");

            if (int.TryParse(qualityText, out int quality))
            {
                _renderingService.JpegQuality = quality;
                SafeUpdateStatusText($"JPEG quality set to {quality}% for HID streaming");
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
                    SafeUpdateStatusText($"HID real-time mode {(enableRealTime ? "enabled" : "disabled")} successfully");
                }
                else
                {
                    // Revert checkbox state if operation failed
                    checkBox.IsChecked = !enableRealTime;
                    SafeUpdateStatusText($"Failed to {(enableRealTime ? "enable" : "disable")} HID real-time mode");
                }
            }
            catch (Exception ex)
            {
                // Revert checkbox state on exception
                checkBox.IsChecked = !checkBox.IsChecked;
                SafeUpdateStatusText($"Error changing HID real-time mode: {ex.Message}");
            }
        }

        private void OnMotionSpeedChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            // Update the text display
            if (MotionSpeedText != null)
            {
                MotionSpeedText.Text = $"{e.NewValue:F0}";
            }
            
            // Update status text to show the new speed value - use safe method
            SafeUpdateStatusText($"Motion speed changed to {e.NewValue:F0} px/s (applies to new elements)");
        }

        private void OnTestSimpleTextClick(object sender, RoutedEventArgs e)
        {
            var id = _renderingService.AddSimpleText("Hello HID Devices! 🎨📱");

            if (id >= 0)
            {
                _elementIds.Add(id);
                UpdateUI();
                SafeUpdateStatusText("Simple test text added for HID streaming.");
            }
            else
            {
                SafeUpdateStatusText("Failed to add simple text - check service initialization.");
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
                SafeUpdateStatusText("Particle system added.");
            }
        }

        // Motion Demo Methods

        private void OnAddBouncingBallClick(object sender, RoutedEventArgs e)
        {
            var random = new Random();
            var colors = new[] { 
                Microsoft.UI.Colors.Red, Microsoft.UI.Colors.Blue, Microsoft.UI.Colors.Green, 
                Microsoft.UI.Colors.Orange, Microsoft.UI.Colors.Purple, Microsoft.UI.Colors.Cyan 
            };
            
            var position = new Vector2(
                random.Next(50, _renderingService.TargetWidth - 100),
                random.Next(50, _renderingService.TargetHeight - 100));
            
            var speed = (float)MotionSpeedSlider.Value + random.Next(-20, 20);
            var color = colors[random.Next(colors.Length)];
            
            // Create bouncing motion config
            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Bounce,
                Speed = speed,
                Direction = new Vector2(
                    (float)(random.NextDouble() - 0.5) * 2,
                    (float)(random.NextDouble() - 0.5) * 2),
                RespectBoundaries = true,
                ShowTrail = false
            };
            
            var ballId = _renderingService.AddCircleElementWithMotion(position, 15f, color, motionConfig);
            
            if (ballId >= 0)
            {
                _elementIds.Add(ballId);
                UpdateUI();
                SafeUpdateStatusText($"Added bouncing ball with speed {speed:F0} px/s");
            }
        }

        private void OnAddRotatingTextClick(object sender, RoutedEventArgs e)
        {
            var center = new Vector2(_renderingService.TargetWidth / 2f, _renderingService.TargetHeight / 2f);
            var speed = (float)MotionSpeedSlider.Value / 100f; // Convert to rotations per second
            var radius = 80f + (_elementIds.Count % 3) * 20f; // Vary radius for multiple rings
            
            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Circular,
                Speed = speed,
                Center = center,
                Radius = radius,
                RespectBoundaries = false,
                ShowTrail = false
            };
            
            var textId = _renderingService.AddTextElementWithMotion(
                text: $"Orbit #{_elementIds.Count + 1}",
                position: center,
                motionConfig: motionConfig);
            
            if (textId >= 0)
            {
                _elementIds.Add(textId);
                UpdateUI();
                SafeUpdateStatusText($"Added rotating text at radius {radius:F0} with speed {speed:F1} rot/s");
            }
        }

        private void OnAddPulsatingTextClick(object sender, RoutedEventArgs e)
        {
            var random = new Random();
            var position = new Vector2(
                random.Next(100, _renderingService.TargetWidth - 100),
                random.Next(100, _renderingService.TargetHeight - 100));
            
            var speed = (float)MotionSpeedSlider.Value / 50f; // Convert to oscillations per second
            var amplitude = 25f + random.Next(0, 20);
            
            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Oscillate,
                Speed = speed,
                Direction = new Vector2(1, 0), // Horizontal oscillation
                Center = position,
                Radius = amplitude,
                RespectBoundaries = false,
                ShowTrail = false
            };
            
            var textId = _renderingService.AddTextElementWithMotion(
                text: $"Pulse {_elementIds.Count + 1}",
                position: position,
                motionConfig: motionConfig);
            
            if (textId >= 0)
            {
                _elementIds.Add(textId);
                UpdateUI();
                SafeUpdateStatusText($"Added pulsating text with amplitude {amplitude:F0} and speed {speed:F1} osc/s");
            }
        }

        private void OnAddSpiralTextClick(object sender, RoutedEventArgs e)
        {
            var random = new Random();
            var center = new Vector2(
                random.Next(150, _renderingService.TargetWidth - 150),
                random.Next(150, _renderingService.TargetHeight - 150));
            
            var speed = (float)MotionSpeedSlider.Value / 100f;
            var initialRadius = 20f + random.Next(0, 15);
            
            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Spiral,
                Speed = speed,
                Center = center,
                Radius = initialRadius,
                RespectBoundaries = false,
                ShowTrail = true
            };
            
            var textId = _renderingService.AddTextElementWithMotion(
                text: $"Spiral {_elementIds.Count + 1}",
                position: center,
                motionConfig: motionConfig);
            
            if (textId >= 0)
            {
                _elementIds.Add(textId);
                UpdateUI();
                SafeUpdateStatusText($"Added spiral text starting at radius {initialRadius:F0} with speed {speed:F1}");
            }
        }

        private void OnAddLinearTrailClick(object sender, RoutedEventArgs e)
        {
            var random = new Random();
            var position = new Vector2(
                random.Next(50, _renderingService.TargetWidth - 200),
                random.Next(50, _renderingService.TargetHeight - 50));
            
            // Random direction
            var angle = random.NextDouble() * Math.PI * 2;
            var direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            
            var speed = (float)MotionSpeedSlider.Value;
            
            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Linear,
                Speed = speed,
                Direction = direction,
                RespectBoundaries = true,
                ShowTrail = true
            };
            
            var textId = _renderingService.AddTextElementWithMotion(
                text: $"Trail {_elementIds.Count + 1}",
                position: position,
                motionConfig: motionConfig);
            
            if (textId >= 0)
            {
                _elementIds.Add(textId);
                UpdateUI();
                SafeUpdateStatusText($"Added linear motion with trail, direction ({direction.X:F2}, {direction.Y:F2})");
            }
        }

        private void OnAddRandomMovementClick(object sender, RoutedEventArgs e)
        {
            var random = new Random();
            var position = new Vector2(
                random.Next(100, _renderingService.TargetWidth - 100),
                random.Next(100, _renderingService.TargetHeight - 100));
            
            var speed = (float)MotionSpeedSlider.Value * 0.5f; // Slower for random movement
            
            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Random,
                Speed = speed,
                RespectBoundaries = true,
                ShowTrail = false
            };
            
            var textId = _renderingService.AddTextElementWithMotion(
                text: $"Random {_elementIds.Count + 1}",
                position: position,
                motionConfig: motionConfig);
            
            if (textId >= 0)
            {
                _elementIds.Add(textId);
                UpdateUI();
                SafeUpdateStatusText($"Added random movement with speed {speed:F0} px/s");
            }
        }

        private void OnAddMotionShowcaseClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var centerX = _renderingService.TargetWidth / 2f;
                var centerY = _renderingService.TargetHeight / 2f;
                var baseSpeed = (float)MotionSpeedSlider.Value;
                
                // Add a variety of motion elements to create a showcase
                
                // Central rotating text
                var rotatingConfig = new ElementMotionConfig
                {
                    MotionType = MotionType.Circular,
                    Speed = baseSpeed / 100f,
                    Center = new Vector2(centerX, centerY),
                    Radius = 100f,
                    RespectBoundaries = false,
                    ShowTrail = false
                };
                
                var rotatingId = _renderingService.AddTextElementWithMotion(
                    "MOTION SHOWCASE", 
                    new Vector2(centerX, centerY), 
                    rotatingConfig);
                _elementIds.Add(rotatingId);
                
                // Bouncing balls in corners
                var corners = new[]
                {
                    new Vector2(80, 80),
                    new Vector2(_renderingService.TargetWidth - 80, 80),
                    new Vector2(80, _renderingService.TargetHeight - 80),
                    new Vector2(_renderingService.TargetWidth - 80, _renderingService.TargetHeight - 80)
                };
                
                var ballColors = new[] { Microsoft.UI.Colors.Red, Microsoft.UI.Colors.Blue, Microsoft.UI.Colors.Green, Microsoft.UI.Colors.Purple };
                var random = new Random();
                
                for (int i = 0; i < corners.Length; i++)
                {
                    var bounceConfig = new ElementMotionConfig
                    {
                        MotionType = MotionType.Bounce,
                        Speed = baseSpeed * 1.2f,
                        Direction = new Vector2(
                            (float)(random.NextDouble() - 0.5) * 2,
                            (float)(random.NextDouble() - 0.5) * 2),
                        RespectBoundaries = true,
                        ShowTrail = false
                    };
                    
                    var ballId = _renderingService.AddCircleElementWithMotion(corners[i], 12f, ballColors[i], bounceConfig);
                    _elementIds.Add(ballId);
                }
                
                // Pulsating text around the center
                var positions = new[]
                {
                    new Vector2(centerX, centerY - 150),
                    new Vector2(centerX + 130, centerY),
                    new Vector2(centerX, centerY + 150),
                    new Vector2(centerX - 130, centerY)
                };
                
                var pulseTexts = new[] { "PULSE N", "PULSE E", "PULSE S", "PULSE W" };
                var directions = new[] { new Vector2(0, 1), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 0) };
                
                for (int i = 0; i < positions.Length; i++)
                {
                    var pulseConfig = new ElementMotionConfig
                    {
                        MotionType = MotionType.Oscillate,
                        Speed = baseSpeed / 50f,
                        Direction = directions[i],
                        Center = positions[i],
                        Radius = 20f,
                        RespectBoundaries = false,
                        ShowTrail = false
                    };
                    
                    var pulseId = _renderingService.AddTextElementWithMotion(
                        pulseTexts[i], 
                        positions[i], 
                        pulseConfig);
                    _elementIds.Add(pulseId);
                }
                
                // Spiral elements
                var spiralPositions = new[]
                {
                    new Vector2(centerX - 200, centerY - 100),
                    new Vector2(centerX + 200, centerY + 100)
                };
                
                for (int i = 0; i < spiralPositions.Length; i++)
                {
                    var spiralConfig = new ElementMotionConfig
                    {
                        MotionType = MotionType.Spiral,
                        Speed = baseSpeed / 80f,
                        Center = spiralPositions[i],
                        Radius = 15f,
                        RespectBoundaries = false,
                        ShowTrail = true
                    };
                    
                    var spiralId = _renderingService.AddTextElementWithMotion(
                        $"Spiral {i + 1}", 
                        spiralPositions[i], 
                        spiralConfig);
                    _elementIds.Add(spiralId);
                }
                
                UpdateUI();
                SafeUpdateStatusText($"Motion showcase created! Added {corners.Length + positions.Length + spiralPositions.Length + 1} animated elements.");
            }
            catch (Exception ex)
            {
                SafeUpdateStatusText($"Error creating motion showcase: {ex.Message}");
            }
        }

        private void OnStopAllMotionClick(object sender, RoutedEventArgs e)
        {
            // For demo purposes, we'll clear all elements and inform the user
            var currentElementCount = _renderingService.ElementCount;
            _renderingService.ClearElements();
            _elementIds.Clear();
            
            SafeUpdateStatusText($"Cleared all {currentElementCount} elements (including motion)");
            UpdateUI();
        }

        private void OnResumeAllMotionClick(object sender, RoutedEventArgs e)
        {
            // For this demo, add some default moving elements
            var center = new Vector2(_renderingService.TargetWidth / 2f, _renderingService.TargetHeight / 2f);
            
            // Add a rotating text element
            var rotatingConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Circular,
                Speed = (float)MotionSpeedSlider.Value / 100f,
                Center = center,
                Radius = 60f,
                RespectBoundaries = false,
                ShowTrail = false
            };
            
            var textId = _renderingService.AddTextElementWithMotion("Resumed Motion", center, rotatingConfig);
            if (textId >= 0)
            {
                _elementIds.Add(textId);
            }
            
            UpdateUI();
            SafeUpdateStatusText("Added default rotating motion element");
        }

        // Missing essential methods that need to be restored

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
                if (FpsText != null)
                {
                    FpsText.Text = "FPS: 0";
                }
            }
        }

        private void UpdateUI()
        {
            if (ElementCountText != null)
            {
                ElementCountText.Text = $"Elements: {_renderingService?.ElementCount ?? 0}";
            }
            
            if (CaptureButton != null)
            {
                CaptureButton.IsEnabled = _isRendering;
            }
            
            // Update HID status display
            if (_renderingService?.IsHidRealTimeModeEnabled == true)
            {
                SafeUpdateStatusText($"Elements: {_renderingService.ElementCount} | HID: Real-time ACTIVE | Quality: {_renderingService.JpegQuality}%");
            }
        }

        /// <summary>
        /// Safely updates the status text with null checks
        /// </summary>
        /// <param name="message">The message to display</param>
        private void SafeUpdateStatusText(string message)
        {
            if (StatusText != null)
            {
                StatusText.Text = message;
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
    }
}