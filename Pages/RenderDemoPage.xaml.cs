using CMDevicesManager.Models;
using CMDevicesManager.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinFoundation = Windows.Foundation;
using WinUIColor = Windows.UI.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace CMDevicesManager.Pages
{
    public partial class RenderDemoPage : Page
    {
        private InteractiveWin2DRenderingService? _interactiveService;
        private bool _isDragging;
        private RenderElement? _draggedElement;
        private WinFoundation.Point _lastMousePosition;
        private WinFoundation.Point _dragOffset;
        private int _hidFramesSent = 0;
        
        // Motion demo tracking
        private readonly List<RenderElement> _motionElements = new();
        private readonly Random _random = new Random();

        public RenderDemoPage()
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            await InitializeServicesAsync();
        }

        private async Task InitializeServicesAsync()
        {
            try
            {
                StatusLabel.Content = "Initializing services...";

                // Initialize enhanced interactive service with HID integration and built-in render tick
                _interactiveService = new InteractiveWin2DRenderingService();
                await _interactiveService.InitializeAsync(480, 480);

                // Subscribe to all events
                _interactiveService.ImageRendered += OnFrameRendered;
                _interactiveService.ElementSelected += OnElementSelected;
                _interactiveService.ElementMoved += OnElementMoved;
                _interactiveService.HidStatusChanged += OnHidStatusChanged;
                _interactiveService.JpegDataSentToHid += OnJpegDataSentToHid;
                _interactiveService.RenderingError += OnRenderingError;
                _interactiveService.RawImageDataReady += OnRawImageDataReady;
                _interactiveService.BackgroundChanged += OnBackgroundChanged;

                UpdateElementsList();
                UpdateLiveDataCheckboxes();

                StatusLabel.Content = $"Services initialized - HID Service: {(_interactiveService.IsHidServiceConnected ? "Connected" : "Not Available")} - Background: {_interactiveService.GetBackgroundInfo()}";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Failed to initialize: {ex.Message}";
            }
        }

        private void OnFrameRendered(WriteableBitmap bitmap)
        {
            if (Dispatcher.CheckAccess())
            {
                DisplayImage.Source = bitmap;
                FpsLabel.Content = $"Rendering at {_interactiveService?.TargetFPS ?? 30} FPS";
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    DisplayImage.Source = bitmap;
                    FpsLabel.Content = $"Rendering at {_interactiveService?.TargetFPS ?? 30} FPS";
                });
            }
        }

        private void OnHidStatusChanged(string status)
        {
            Dispatcher.Invoke(() =>
            {
                StatusLabel.Content = $"HID: {status}";
            });
        }

        private void OnJpegDataSentToHid(byte[] jpegData)
        {
            Dispatcher.Invoke(() =>
            {
                _hidFramesSent++;
                StatusLabel.Content = $"HID Frames sent: {_hidFramesSent} (Last: {jpegData.Length:N0} bytes)";
            });
        }

        private void OnElementSelected(RenderElement element)
        {
            Dispatcher.Invoke(() =>
            {
                var elements = _interactiveService?.GetElements();
                if (elements != null)
                {
                    var index = elements.FindIndex(e => e.Id == element.Id);
                    if (index >= 0)
                    {
                        ElementsListBox.SelectedIndex = index;
                    }
                }
            });
        }

        private void OnElementMoved(RenderElement element)
        {
            // Element position updated - could trigger additional actions here
        }

        private void OnRawImageDataReady(byte[] imageData)
        {
            Console.WriteLine($"Raw image data received: {imageData.Length} bytes");
        }

        private void OnRenderingError(Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                StatusLabel.Content = $"Rendering error: {ex.Message}";
            });
        }

        // Mouse interaction handlers for interactive service
        private void DisplayImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_interactiveService == null) return;

            var position = e.GetPosition(DisplayImage);
            var scaledPosition = ScalePointToRenderTarget(position);

            var hitElement = _interactiveService.HitTest(scaledPosition);
            if (hitElement != null)
            {
                _interactiveService.SelectElement(hitElement);
                _isDragging = true;
                _draggedElement = hitElement;
                _lastMousePosition = scaledPosition;
                _dragOffset = new WinFoundation.Point(
                    scaledPosition.X - hitElement.Position.X,
                    scaledPosition.Y - hitElement.Position.Y
                );
                DisplayImage.CaptureMouse();
            }
            else
            {
                _interactiveService.SelectElement(null);
            }
        }

        private void DisplayImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && _draggedElement != null && _interactiveService != null)
            {
                var position = e.GetPosition(DisplayImage);
                var scaledPosition = ScalePointToRenderTarget(position);

                var newPosition = new WinFoundation.Point(
                    Math.Max(0, scaledPosition.X - _dragOffset.X),
                    Math.Max(0, scaledPosition.Y - _dragOffset.Y)
                );

                _interactiveService.MoveElement(_draggedElement, newPosition);
                _lastMousePosition = scaledPosition;
            }
        }

        private void DisplayImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                _draggedElement = null;
                DisplayImage.ReleaseMouseCapture();
            }
        }

        private void DisplayImage_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_interactiveService == null) return;

            var position = e.GetPosition(DisplayImage);
            var scaledPosition = ScalePointToRenderTarget(position);

            var hitElement = _interactiveService.HitTest(scaledPosition);
            _interactiveService.SelectElement(hitElement);
        }

        private WinFoundation.Point ScalePointToRenderTarget(System.Windows.Point displayPoint)
        {
            if (_interactiveService == null)
                return new WinFoundation.Point(displayPoint.X, displayPoint.Y);

            var imageSize = DisplayImage.RenderSize;
            if (imageSize.Width == 0 || imageSize.Height == 0)
                return new WinFoundation.Point(displayPoint.X, displayPoint.Y);

            var scaleX = _interactiveService.Width / imageSize.Width;
            var scaleY = _interactiveService.Height / imageSize.Height;

            return new WinFoundation.Point(displayPoint.X * scaleX, displayPoint.Y * scaleY);
        }

        // UI Event Handlers
        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_interactiveService != null)
                {
                    // Enable HID transfer and start auto-rendering with built-in render tick
                    await _interactiveService.EnableHidTransfer(true, useSuspendMedia: false);
                    
                    // Enable real-time display mode on HID devices
                    await _interactiveService.EnableHidRealTimeDisplayAsync(true);
                    
                    // Start auto-rendering with built-in render tick
                    _interactiveService.StartAutoRendering(_interactiveService.TargetFPS);

                    StartButton.IsEnabled = false;
                    StopButton.IsEnabled = true;
                    StatusLabel.Content = "Interactive rendering started with built-in render tick and HID streaming";
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error starting rendering: {ex.Message}";
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_interactiveService != null)
                {
                    // Stop auto-rendering and disable HID transfer
                    _interactiveService.StopAutoRendering();
                    _interactiveService.EnableHidTransfer(false);
                    await _interactiveService.EnableHidRealTimeDisplayAsync(false);
                }

                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusLabel.Content = "Rendering stopped";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error stopping rendering: {ex.Message}";
            }
        }

        // HID-specific methods for demonstration
        public async Task EnableHidRealTimeAsync()
        {
            if (_interactiveService != null)
            {
                _interactiveService.EnableHidTransfer(true, useSuspendMedia: false);
                var success = await _interactiveService.EnableHidRealTimeDisplayAsync(true);
                
                if (success)
                {
                    StatusLabel.Content = "HID real-time mode enabled";
                    _hidFramesSent = 0; // Reset counter
                }
            }
        }

        public async Task SendSuspendMediaAsync()
        {
            if (_interactiveService != null)
            {
                var success = await _interactiveService.SendCurrentFrameAsSuspendMediaAsync();
                StatusLabel.Content = success ? "Current frame sent as suspend media" : "Failed to send suspend media";
            }
        }

        public async Task SetSuspendModeAsync()
        {
            if (_interactiveService != null)
            {
                var success = await _interactiveService.SetSuspendModeAsync();
                StatusLabel.Content = success ? "Devices switched to suspend mode" : "Failed to set suspend mode";
            }
        }

        private void FpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                var newFps = (int)e.NewValue;

                if (_interactiveService != null)
                {
                    if (_interactiveService.IsAutoRenderingEnabled)
                    {
                        // Update auto-rendering FPS dynamically
                        _interactiveService.SetAutoRenderingFPS(newFps);
                    }
                    else
                    {
                        _interactiveService.TargetFPS = newFps;
                    }
                }

                if(FpsLabel !=null) 
                    FpsLabel.Content = $"Target FPS: {newFps}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FPS change error: {ex.Message}");
            }
        }

        private async void SaveCurrentFrameButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "PNG Files (*.png)|*.png|JPEG Files (*.jpg)|*.jpg",
                    DefaultExt = "png",
                    FileName = $"render_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    bool saved = false;
                    
                    if (_interactiveService != null)
                    {
                        await _interactiveService.SaveRenderedImageAsync(saveDialog.FileName);
                        saved = true;
                    }

                    StatusLabel.Content = saved ? $"Frame saved: {Path.GetFileName(saveDialog.FileName)}" : "Failed to save frame";
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Save failed: {ex.Message}";
            }
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService != null)
            {
                _interactiveService.ClearElements();
                _motionElements.Clear();
                UpdateElementsList();
                StatusLabel.Content = "All elements cleared";
            }
        }

        private void AddTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            var text = TextContentBox?.Text ?? "Sample Text";
            var color = WinUIColor.FromArgb(255, 255, 255, 255); // White

            var textElement = new TextElement($"Text {DateTime.Now:HHmmss}")
            {
                Text = text,
                FontSize = 24,
                TextColor = color,
                Position = new WinFoundation.Point(100, 100),
                Size = new WinFoundation.Size(200, 50),
                IsDraggable = true // Make it interactive
            };

            _interactiveService.AddElement(textElement);
            UpdateElementsList();
            StatusLabel.Content = "Interactive text element added";
        }

        private async void LoadImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            var openDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Title = "Select Image File"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    var imageKey = await _interactiveService.LoadImageAsync(openDialog.FileName);

                    var imageElement = new ImageElement($"Image {DateTime.Now:HHmmss}")
                    {
                        ImagePath = imageKey,
                        Position = new WinFoundation.Point(200, 200),
                        Size = new WinFoundation.Size(100, 100),
                        Scale = 1.0f,
                        IsDraggable = true // Make it interactive
                    };

                    _interactiveService.AddElement(imageElement);
                    UpdateElementsList();
                    StatusLabel.Content = "Interactive image element added";
                }
                catch (Exception ex)
                {
                    StatusLabel.Content = $"Failed to load image: {ex.Message}";
                }
            }
        }

        private void AddCircleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            var shapeElement = new ShapeElement($"Circle {DateTime.Now:HHmmss}")
            {
                ShapeType = ShapeType.Circle,
                Position = new WinFoundation.Point(300, 200),
                Size = new WinFoundation.Size(80, 80),
                FillColor = WinUIColor.FromArgb(150, 100, 150, 255),
                StrokeColor = WinUIColor.FromArgb(255, 255, 255, 255),
                StrokeWidth = 2,
                IsDraggable = true // Make it interactive
            };

            _interactiveService.AddElement(shapeElement);
            UpdateElementsList();
            StatusLabel.Content = "Interactive circle element added";
        }

        private void AddRectangleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            var shapeElement = new ShapeElement($"Rectangle {DateTime.Now:HHmmss}")
            {
                ShapeType = ShapeType.Rectangle,
                Position = new WinFoundation.Point(400, 200),
                Size = new WinFoundation.Size(100, 60),
                FillColor = WinUIColor.FromArgb(150, 255, 100, 100),
                StrokeColor = WinUIColor.FromArgb(255, 255, 255, 255),
                StrokeWidth = 2,
                IsDraggable = true // Make it interactive
            };

            _interactiveService.AddElement(shapeElement);
            UpdateElementsList();
            StatusLabel.Content = "Interactive rectangle element added";
        }

        #region Motion Demo Methods

        /// <summary>
        /// Add a bouncing ball with trail
        /// </summary>
        private void AddBouncingBallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            var colors = new[] 
            { 
                WinUIColor.FromArgb(255, 255, 0, 0),     // Red
                WinUIColor.FromArgb(255, 0, 255, 0),     // Green  
                WinUIColor.FromArgb(255, 0, 0, 255),     // Blue
                WinUIColor.FromArgb(255, 255, 165, 0),   // Orange
                WinUIColor.FromArgb(255, 128, 0, 128),   // Purple
                WinUIColor.FromArgb(255, 0, 255, 255)    // Cyan
            };

            var position = new WinFoundation.Point(
                _random.Next(50, _interactiveService.Width - 50),
                _random.Next(50, _interactiveService.Height - 50));

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Bounce,
                Speed = 120f + _random.Next(0, 60), // 120-180 pixels per second
                Direction = GetRandomDirection(),
                RespectBoundaries = true,
                ShowTrail = true,
                TrailLength = 15
            };

            var color = colors[_random.Next(colors.Length)];
            var radius = 8f + _random.Next(0, 8);

            var elementIndex = _interactiveService.AddCircleElementWithMotion(position, radius, color, motionConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    UpdateElementsList();
                    StatusLabel.Content = $"Added bouncing ball #{_motionElements.Count}";
                }
            }
        }

        /// <summary>
        /// Add rotating text around center
        /// </summary>
        private void AddRotatingTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            var text = TextContentBox?.Text ?? $"Orbit #{_motionElements.Count + 1}";

            var center = new WinFoundation.Point(
                _interactiveService.Width / 2.0,
                _interactiveService.Height / 2.0);

            var radius = 60f + (_motionElements.Count % 4) * 30f; // Vary radius for multiple rings

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Circular,
                Speed = 1.5f, // 1.5 radians per second
                Center = new Vector2((float)center.X, (float)center.Y),
                Radius = radius,
                RespectBoundaries = false,
                ShowTrail = false
            };

            var textConfig = new TextElementConfig
            {
                FontSize = 18,
                TextColor = WinUIColor.FromArgb(255, 0, 255, 255), // Cyan
                IsDraggable = false // Don't allow dragging for rotating text
            };

            var elementIndex = _interactiveService.AddTextElementWithMotion(text, center, motionConfig, textConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    UpdateElementsList();
                    StatusLabel.Content = $"Added rotating text at radius {radius:F0}";
                }
            }
        }

        /// <summary>
        /// Add oscillating shapes
        /// </summary>
        private void AddOscillatingShapeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            var position = new WinFoundation.Point(
                _random.Next(100, _interactiveService.Width - 100),
                _random.Next(100, _interactiveService.Height - 100));

            var directions = new[]
            {
                new Vector2(1, 0),      // Horizontal
                new Vector2(0, 1),      // Vertical  
                new Vector2(0.7f, 0.7f),  // Diagonal
                new Vector2(-0.7f, 0.7f)  // Other diagonal
            };

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Oscillate,
                Speed = 2.0f + (float)(_random.NextDouble() * 2.0), // 2.0-4.0 oscillations per second
                Direction = directions[_random.Next(directions.Length)],
                Center = new Vector2((float)position.X, (float)position.Y),
                Radius = 30f + _random.Next(0, 20), // 30-50 amplitude
                RespectBoundaries = false,
                ShowTrail = false
            };

            var colors = new[]
            {
                WinUIColor.FromArgb(255, 255, 255, 0),   // Yellow
                WinUIColor.FromArgb(255, 255, 192, 203), // Pink
                WinUIColor.FromArgb(255, 255, 165, 0),   // Orange
                WinUIColor.FromArgb(255, 0, 255, 255)    // Cyan
            };

            var color = colors[_random.Next(colors.Length)];
            var size = new WinFoundation.Size(20 + _random.Next(0, 20), 20 + _random.Next(0, 20));

            var elementIndex = _interactiveService.AddRectangleElementWithMotion(position, size, color, motionConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    UpdateElementsList();
                    StatusLabel.Content = $"Added oscillating shape #{_motionElements.Count}";
                }
            }
        }

        /// <summary>
        /// Add spiral motion text
        /// </summary>
        private void AddSpiralTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            var text = TextContentBox?.Text ?? $"Spiral {_motionElements.Count + 1}";

            var center = new WinFoundation.Point(
                _random.Next(150, _interactiveService.Width - 150),
                _random.Next(150, _interactiveService.Height - 150));

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Spiral,
                Speed = 2.0f,
                Center = new Vector2((float)center.X, (float)center.Y),
                Radius = 15f,
                RespectBoundaries = false,
                ShowTrail = true,
                TrailLength = 30
            };

            var textConfig = new TextElementConfig
            {
                FontSize = 14,
                TextColor = WinUIColor.FromArgb(255, 144, 238, 144), // Light green
                IsDraggable = false
            };

            var elementIndex = _interactiveService.AddTextElementWithMotion(text, center, motionConfig, textConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    UpdateElementsList();
                    StatusLabel.Content = $"Added spiral text starting at ({center.X:F0}, {center.Y:F0})";
                }
            }
        }

        /// <summary>
        /// Add random walking elements
        /// </summary>
        private void AddRandomWalkerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            var position = new WinFoundation.Point(
                _random.Next(100, _interactiveService.Width - 100),
                _random.Next(100, _interactiveService.Height - 100));

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Random,
                Speed = 60f + _random.Next(0, 40), // 60-100 pixels per second
                RespectBoundaries = true,
                ShowTrail = true,
                TrailLength = 12
            };

            var colors = new[]
            {
                WinUIColor.FromArgb(255, 255, 20, 147),  // Deep pink
                WinUIColor.FromArgb(255, 50, 205, 50),   // Lime green
                WinUIColor.FromArgb(255, 255, 140, 0),   // Dark orange
                WinUIColor.FromArgb(255, 138, 43, 226)   // Blue violet
            };

            var color = colors[_random.Next(colors.Length)];
            var radius = 6f + _random.Next(0, 6);

            var elementIndex = _interactiveService.AddCircleElementWithMotion(position, radius, color, motionConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    UpdateElementsList();
                    StatusLabel.Content = $"Added random walker #{_motionElements.Count}";
                }
            }
        }

        /// <summary>
        /// Create a comprehensive motion showcase
        /// </summary>
        private void CreateMotionShowcaseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            try
            {
                // Clear existing elements
                _interactiveService.ClearElements();
                _motionElements.Clear();

                // Add bouncing balls
                for (int i = 0; i < 3; i++)
                {
                    AddBouncingBall();
                }

                // Add rotating texts
                AddRotatingText("CENTER");
                AddRotatingText("ORBIT 1");
                AddRotatingText("ORBIT 2");

                // Add oscillating shapes
                for (int i = 0; i < 2; i++)
                {
                    AddOscillatingShape();
                }

                // Add spiral texts
                AddSpiralText("SPIRAL A");
                AddSpiralText("SPIRAL B");

                // Add random walkers
                for (int i = 0; i < 2; i++)
                {
                    AddRandomWalker();
                }

                // Add wave text
                AddWaveText("WAVE MOTION");

                // Add orbital system
                AddOrbitalSystem();

                UpdateElementsList();
                StatusLabel.Content = $"Motion showcase created with {_motionElements.Count} animated elements";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error creating motion showcase: {ex.Message}";
            }
        }

        /// <summary>
        /// Convert existing element to motion element
        /// </summary>
        private void ConvertToMotionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null || ElementsListBox.SelectedIndex < 0) return;

            var elements = _interactiveService.GetElements();
            if (ElementsListBox.SelectedIndex >= elements.Count) return;

            var selectedElement = elements[ElementsListBox.SelectedIndex];
            
            if (selectedElement is IMotionElement)
            {
                StatusLabel.Content = "Element already has motion capabilities";
                return;
            }

            // Convert based on element type
            switch (selectedElement)
            {
                case TextElement textElement:
                    ConvertTextToMotion(textElement);
                    break;
                case ShapeElement shapeElement:
                    ConvertShapeToMotion(shapeElement);
                    break;
                default:
                    StatusLabel.Content = "Element type not supported for motion conversion";
                    break;
            }
        }

        #endregion

        #region Motion Control Methods

        private void PauseMotionButton_Click(object sender, RoutedEventArgs e)
        {
            _interactiveService?.PauseAllMotion();
            StatusLabel.Content = "All motion paused";
        }

        private void ResumeMotionButton_Click(object sender, RoutedEventArgs e)
        {
            _interactiveService?.ResumeAllMotion();
            StatusLabel.Content = "All motion resumed";
        }

        private void RandomizeMotionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_motionElements.Count == 0)
            {
                StatusLabel.Content = "No motion elements to randomize";
                return;
            }

            var element = _motionElements[_random.Next(_motionElements.Count)];

            if (element is IMotionElement motionElement)
            {
                var motionTypes = Enum.GetValues<MotionType>();
                var newMotionType = motionTypes[_random.Next(1, motionTypes.Length)]; // Skip None

                var newConfig = new ElementMotionConfig
                {
                    MotionType = newMotionType,
                    Speed = 80f + _random.Next(0, 80),
                    Direction = GetRandomDirection(),
                    Center = new Vector2((float)element.Position.X, (float)element.Position.Y),
                    Radius = 30f + _random.Next(0, 50),
                    RespectBoundaries = true,
                    ShowTrail = _random.Next(0, 2) == 1,
                    TrailLength = _random.Next(10, 25)
                };

                _interactiveService?.SetElementMotion(element, newConfig);
                StatusLabel.Content = $"Changed element motion to {newMotionType}";
            }
        }

        #endregion

        #region Helper Methods for Motion Demo

        private void AddBouncingBall()
        {
            if (_interactiveService == null) return;

            var colors = new[] 
            { 
                WinUIColor.FromArgb(255, 255, 0, 0),     // Red
                WinUIColor.FromArgb(255, 0, 255, 0),     // Green  
                WinUIColor.FromArgb(255, 0, 0, 255),     // Blue
                WinUIColor.FromArgb(255, 255, 165, 0),   // Orange
            };

            var position = new WinFoundation.Point(
                _random.Next(50, _interactiveService.Width - 50),
                _random.Next(50, _interactiveService.Height - 50));

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Bounce,
                Speed = 120f + _random.Next(0, 60),
                Direction = GetRandomDirection(),
                RespectBoundaries = true,
                ShowTrail = true,
                TrailLength = 15
            };

            var color = colors[_random.Next(colors.Length)];
            var radius = 8f + _random.Next(0, 8);

            var elementIndex = _interactiveService.AddCircleElementWithMotion(position, radius, color, motionConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                }
            }
        }

        private void AddRotatingText(string text)
        {
            if (_interactiveService == null) return;

            var center = new WinFoundation.Point(
                _interactiveService.Width / 2.0,
                _interactiveService.Height / 2.0);

            var radius = 60f + (_motionElements.Count % 4) * 30f;

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Circular,
                Speed = 1.5f,
                Center = new Vector2((float)center.X, (float)center.Y),
                Radius = radius,
                RespectBoundaries = false,
                ShowTrail = false
            };

            var textConfig = new TextElementConfig
            {
                FontSize = 18,
                TextColor = WinUIColor.FromArgb(255, 0, 255, 255),
                IsDraggable = false
            };

            var elementIndex = _interactiveService.AddTextElementWithMotion(text, center, motionConfig, textConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                }
            }
        }

        private void AddOscillatingShape()
        {
            if (_interactiveService == null) return;

            var position = new WinFoundation.Point(
                _random.Next(100, _interactiveService.Width - 100),
                _random.Next(100, _interactiveService.Height - 100));

            var directions = new[]
            {
                new Vector2(1, 0), new Vector2(0, 1), new Vector2(0.7f, 0.7f), new Vector2(-0.7f, 0.7f)
            };

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Oscillate,
                Speed = 2.0f + (float)(_random.NextDouble() * 2.0),
                Direction = directions[_random.Next(directions.Length)],
                Center = new Vector2((float)position.X, (float)position.Y),
                Radius = 30f + _random.Next(0, 20),
                RespectBoundaries = false,
                ShowTrail = false
            };

            var colors = new[]
            {
                WinUIColor.FromArgb(255, 255, 255, 0),
                WinUIColor.FromArgb(255, 255, 165, 0),
                WinUIColor.FromArgb(255, 0, 255, 255)
            };

            var color = colors[_random.Next(colors.Length)];
            var size = new WinFoundation.Size(20 + _random.Next(0, 20), 20 + _random.Next(0, 20));

            var elementIndex = _interactiveService.AddRectangleElementWithMotion(position, size, color, motionConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                }
            }
        }

        private void AddSpiralText(string text)
        {
            if (_interactiveService == null) return;

            var center = new WinFoundation.Point(
                _random.Next(150, _interactiveService.Width - 150),
                _random.Next(150, _interactiveService.Height - 150));

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Spiral,
                Speed = 2.0f,
                Center = new Vector2((float)center.X, (float)center.Y),
                Radius = 15f,
                RespectBoundaries = false,
                ShowTrail = true,
                TrailLength = 30
            };

            var textConfig = new TextElementConfig
            {
                FontSize = 14,
                TextColor = WinUIColor.FromArgb(255, 144, 238, 144),
                IsDraggable = false
            };

            var elementIndex = _interactiveService.AddTextElementWithMotion(text, center, motionConfig, textConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                }
            }
        }

        private void AddRandomWalker()
        {
            if (_interactiveService == null) return;

            var position = new WinFoundation.Point(
                _random.Next(100, _interactiveService.Width - 100),
                _random.Next(100, _interactiveService.Height - 100));

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Random,
                Speed = 60f + _random.Next(0, 40),
                RespectBoundaries = true,
                ShowTrail = true,
                TrailLength = 12
            };

            var colors = new[]
            {
                WinUIColor.FromArgb(255, 255, 20, 147),
                WinUIColor.FromArgb(255, 50, 205, 50),
                WinUIColor.FromArgb(255, 255, 140, 0),
                WinUIColor.FromArgb(255, 138, 43, 226)
            };

            var color = colors[_random.Next(colors.Length)];
            var radius = 6f + _random.Next(0, 6);

            var elementIndex = _interactiveService.AddCircleElementWithMotion(position, radius, color, motionConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                }
            }
        }

        private void AddWaveText(string text)
        {
            if (_interactiveService == null) return;

            var startY = _random.Next(100, _interactiveService.Height - 100);
            var position = new WinFoundation.Point(0, startY);

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Wave,
                Speed = 1.2f + (float)(_random.NextDouble() * 0.8),
                Radius = 20f + _random.Next(0, 20),
                RespectBoundaries = false,
                ShowTrail = false
            };

            var textConfig = new TextElementConfig
            {
                FontSize = 16,
                TextColor = WinUIColor.FromArgb(255, 0, 191, 255),
                IsDraggable = false
            };

            var elementIndex = _interactiveService.AddTextElementWithMotion(text, position, motionConfig, textConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                }
            }
        }

        private void AddOrbitalSystem()
        {
            if (_interactiveService == null) return;

            var center = new WinFoundation.Point(
                _interactiveService.Width / 2.0,
                _interactiveService.Height / 2.0);

            // Central "sun"
            var sunMotionConfig = new ElementMotionConfig { MotionType = MotionType.None };
            var sunIndex = _interactiveService.AddCircleElementWithMotion(
                center, 20f, WinUIColor.FromArgb(255, 255, 255, 0), sunMotionConfig);

            // Add "planets"
            var planetData = new[]
            {
                new { Radius = 50f, Speed = 2.2f, Size = 6f, Color = WinUIColor.FromArgb(255, 128, 128, 128) },
                new { Radius = 80f, Speed = 1.5f, Size = 8f, Color = WinUIColor.FromArgb(255, 173, 216, 230) },
                new { Radius = 110f, Speed = 1.0f, Size = 7f, Color = WinUIColor.FromArgb(255, 165, 42, 42) }
            };

            foreach (var planet in planetData)
            {
                var orbitConfig = new ElementMotionConfig
                {
                    MotionType = MotionType.Orbit,
                    Speed = planet.Speed,
                    Center = new Vector2((float)center.X, (float)center.Y),
                    Radius = planet.Radius,
                    ShowTrail = true,
                    TrailLength = 15
                };

                var planetIndex = _interactiveService.AddCircleElementWithMotion(center, planet.Size, planet.Color, orbitConfig);
                
                if (planetIndex >= 0)
                {
                    var elements = _interactiveService.GetElements();
                    if (planetIndex < elements.Count)
                    {
                        _motionElements.Add(elements[planetIndex]);
                    }
                }
            }

            if (sunIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (sunIndex < elements.Count)
                {
                    _motionElements.Add(elements[sunIndex]);
                }
            }
        }

        private void ConvertTextToMotion(TextElement textElement)
        {
            if (_interactiveService == null) return;

            var textMotionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Oscillate,
                Speed = 2.0f,
                Direction = Vector2.UnitX,
                Center = new Vector2((float)textElement.Position.X, (float)textElement.Position.Y),
                Radius = 30f
            };
            
            var textConfig = new TextElementConfig
            {
                FontSize = textElement.FontSize,
                TextColor = textElement.TextColor,
                IsDraggable = textElement.IsDraggable
            };

            _interactiveService.RemoveElement(textElement);
            var elementIndex = _interactiveService.AddTextElementWithMotion(textElement.Text, textElement.Position, textMotionConfig, textConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    UpdateElementsList();
                    StatusLabel.Content = "Converted text element to motion";
                }
            }
        }

        private void ConvertShapeToMotion(ShapeElement shapeElement)
        {
            if (_interactiveService == null) return;

            var shapeMotionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Random,
                Speed = 80f,
                RespectBoundaries = true,
                ShowTrail = true
            };

            _interactiveService.RemoveElement(shapeElement);

            int elementIndex;
            if (shapeElement.ShapeType == ShapeType.Circle)
            {
                var radius = (float)Math.Min(shapeElement.Size.Width, shapeElement.Size.Height) / 2;
                elementIndex = _interactiveService.AddCircleElementWithMotion(shapeElement.Position, radius, shapeElement.FillColor, shapeMotionConfig);
            }
            else
            {
                elementIndex = _interactiveService.AddRectangleElementWithMotion(shapeElement.Position, shapeElement.Size, shapeElement.FillColor, shapeMotionConfig);
            }
            
            if (elementIndex >= 0)
            {
                var elements = _interactiveService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    UpdateElementsList();
                    StatusLabel.Content = "Converted shape element to motion";
                }
            }
        }

        private Vector2 GetRandomDirection()
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            return new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        #endregion

        private void LiveDataCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_interactiveService != null)
            {
                _interactiveService.ShowTime = ShowTimeCheckBox?.IsChecked ?? false;
                _interactiveService.ShowDate = ShowDateCheckBox?.IsChecked ?? false;
                _interactiveService.ShowSystemInfo = ShowSystemInfoCheckBox?.IsChecked ?? false;
                _interactiveService.ShowAnimation = ShowAnimationCheckBox?.IsChecked ?? false;
            }
        }

        private void ElementsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ElementsListBox.SelectedIndex >= 0 && _interactiveService != null)
            {
                var elements = _interactiveService.GetElements();
                if (ElementsListBox.SelectedIndex < elements.Count)
                {
                    var element = elements[ElementsListBox.SelectedIndex];
                    _interactiveService.SelectElement(element);
                    if (DeleteElementButton != null)
                        DeleteElementButton.IsEnabled = true;
                }
            }
            else
            {
                if (DeleteElementButton != null)
                    DeleteElementButton.IsEnabled = false;
            }
        }

        private void DeleteElementButton_Click(object sender, RoutedEventArgs e)
        {
            if (ElementsListBox.SelectedIndex >= 0 && _interactiveService != null)
            {
                var elements = _interactiveService.GetElements();
                if (ElementsListBox.SelectedIndex < elements.Count)
                {
                    var element = elements[ElementsListBox.SelectedIndex];
                    _interactiveService.RemoveElement(element);
                    _motionElements.Remove(element);
                    UpdateElementsList();
                    StatusLabel.Content = "Element deleted";
                }
            }
        }

        // Helper methods
        private void UpdateElementsList()
        {
            if (_interactiveService == null || ElementsListBox == null) return;

            var elements = _interactiveService.GetElements();
            ElementsListBox.ItemsSource = elements.Select(e => 
            {
                var motionInfo = e is IMotionElement motionElement ? $"[{motionElement.MotionConfig.MotionType}]" : "";
                return $"{e.Name} ({e.Type}) {(e.IsDraggable ? "[Interactive]" : "")} {motionInfo}";
            }).ToList();
        }

        private void UpdateLiveDataCheckboxes()
        {
            if (_interactiveService != null)
            {
                if (ShowTimeCheckBox != null)
                    ShowTimeCheckBox.IsChecked = _interactiveService.ShowTime;
                if (ShowDateCheckBox != null)
                    ShowDateCheckBox.IsChecked = _interactiveService.ShowDate;
                if (ShowSystemInfoCheckBox != null)
                    ShowSystemInfoCheckBox.IsChecked = _interactiveService.ShowSystemInfo;
                if (ShowAnimationCheckBox != null)
                    ShowAnimationCheckBox.IsChecked = _interactiveService.ShowAnimation;
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Stop auto-rendering and cleanup
                _interactiveService?.StopAutoRendering();
                _interactiveService?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        #region Background Demo Methods

        /// <summary>
        /// Set solid color background
        /// </summary>
        private async void SetSolidColorBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            try
            {
                // Cycle through different solid colors
                var colors = new[]
                {
                    WinUIColor.FromArgb(255, 255, 0, 0),      // Red
                    WinUIColor.FromArgb(255, 0, 255, 0),      // Green
                    WinUIColor.FromArgb(255, 0, 0, 255),      // Blue
                    WinUIColor.FromArgb(255, 128, 0, 128),    // Purple
                    WinUIColor.FromArgb(255, 255, 165, 0),    // Orange
                    WinUIColor.FromArgb(255, 32, 32, 48)      // Dark blue-gray
                };

                var randomColor = colors[_random.Next(colors.Length)];
                await _interactiveService.SetBackgroundColorAsync(randomColor, 1.0f);
                
                StatusLabel.Content = $"Solid color background set: RGB({randomColor.R},{randomColor.G},{randomColor.B})";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error setting solid color background: {ex.Message}";
            }
        }

        /// <summary>
        /// Set gradient background with random colors and direction
        /// </summary>
        private async void SetGradientBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            try
            {
                var gradientColors = new[]
                {
                    (WinUIColor.FromArgb(255, 255, 0, 0), WinUIColor.FromArgb(255, 255, 165, 0)),     // Red to Orange
                    (WinUIColor.FromArgb(255, 0, 0, 255), WinUIColor.FromArgb(255, 128, 0, 128)),     // Blue to Purple
                    (WinUIColor.FromArgb(255, 0, 255, 0), WinUIColor.FromArgb(255, 255, 255, 0)),     // Green to Yellow
                    (WinUIColor.FromArgb(255, 255, 20, 147), WinUIColor.FromArgb(255, 138, 43, 226)), // Deep Pink to Blue Violet
                    (WinUIColor.FromArgb(255, 25, 25, 112), WinUIColor.FromArgb(255, 65, 105, 225)),  // Midnight Blue to Royal Blue
                    (WinUIColor.FromArgb(255, 255, 255, 255), WinUIColor.FromArgb(255, 0, 0, 0))      // White to Black
                };

                var directions = new[]
                {
                    BackgroundGradientDirection.TopToBottom,
                    BackgroundGradientDirection.LeftToRight,
                    BackgroundGradientDirection.DiagonalTopLeftToBottomRight,
                    BackgroundGradientDirection.DiagonalTopRightToBottomLeft,
                    BackgroundGradientDirection.RadialFromCenter
                };

                var (startColor, endColor) = gradientColors[_random.Next(gradientColors.Length)];
                var direction = directions[_random.Next(directions.Length)];

                await _interactiveService.SetBackgroundGradientAsync(startColor, endColor, direction, 1.0f);
                
                StatusLabel.Content = $"Gradient background set: {direction}";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error setting gradient background: {ex.Message}";
            }
        }

        /// <summary>
        /// Load and set background image
        /// </summary>
        private async void LoadBackgroundImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            var openDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Title = "Select Background Image"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    await _interactiveService.SetBackgroundImageAsync(openDialog.FileName, BackgroundScaleMode.UniformToFill, 1.0f);
                    StatusLabel.Content = $"Background image set: {Path.GetFileName(openDialog.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusLabel.Content = $"Failed to load background image: {ex.Message}";
                }
            }
        }

        /// <summary>
        /// Clear background (make transparent)
        /// </summary>
        private async void ClearBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            try
            {
                await _interactiveService.ClearBackgroundAsync();
                StatusLabel.Content = "Background cleared (transparent)";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error clearing background: {ex.Message}";
            }
        }

        /// <summary>
        /// Reset to default background
        /// </summary>
        private async void ResetDefaultBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            try
            {
                await _interactiveService.ResetBackgroundToDefaultAsync();
                StatusLabel.Content = "Background reset to default gradient";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error resetting background: {ex.Message}";
            }
        }

        /// <summary>
        /// Change background scale mode (for image backgrounds)
        /// </summary>
        private void ChangeScaleModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null || !_interactiveService.HasBackgroundImage) 
            {
                StatusLabel.Content = "No background image loaded";
                return;
            }

            try
            {
                var scaleModes = new[]
                {
                    BackgroundScaleMode.Stretch,
                    BackgroundScaleMode.Uniform,
                    BackgroundScaleMode.UniformToFill,
                    BackgroundScaleMode.None
                };

                var currentMode = _interactiveService.BackgroundScaleMode;
                var currentIndex = Array.IndexOf(scaleModes, currentMode);
                var nextIndex = (currentIndex + 1) % scaleModes.Length;
                var newMode = scaleModes[nextIndex];

                _interactiveService.SetBackgroundScaleMode(newMode);
                StatusLabel.Content = $"Background scale mode changed to: {newMode}";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error changing scale mode: {ex.Message}";
            }
        }

        /// <summary>
        /// Adjust background opacity
        /// </summary>
        private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_interactiveService == null) return;

            try
            {
                var opacity = (float)e.NewValue;
                _interactiveService.SetBackgroundOpacity(opacity);
                
                // Update status instead of label since we don't have the label control yet
                StatusLabel.Content = $"Background opacity set to: {opacity:F2}";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error setting background opacity: {ex.Message}";
            }
        }

        /// <summary>
        /// Get current background information
        /// </summary>
        private void GetBackgroundInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            try
            {
                var info = _interactiveService.GetBackgroundInfo();
                StatusLabel.Content = $"Background: {info}";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error getting background info: {ex.Message}";
            }
        }

        /// <summary>
        /// Run animated background demo
        /// </summary>
        private async void AnimatedBackgroundDemoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            try
            {
                StatusLabel.Content = "Starting animated background demo...";

                // Disable the button during demo
                if (sender is System.Windows.Controls.Button button)
                    button.IsEnabled = false;

                // Create a color-changing animation
                var colors = new[]
                {
                    (WinUIColor.FromArgb(255, 255, 0, 0), WinUIColor.FromArgb(255, 255, 165, 0)),     // Red to Orange
                    (WinUIColor.FromArgb(255, 255, 165, 0), WinUIColor.FromArgb(255, 255, 255, 0)),   // Orange to Yellow
                    (WinUIColor.FromArgb(255, 255, 255, 0), WinUIColor.FromArgb(255, 0, 255, 0)),     // Yellow to Green
                    (WinUIColor.FromArgb(255, 0, 255, 0), WinUIColor.FromArgb(255, 0, 255, 255)),     // Green to Cyan
                    (WinUIColor.FromArgb(255, 0, 255, 255), WinUIColor.FromArgb(255, 0, 0, 255)),     // Cyan to Blue
                    (WinUIColor.FromArgb(255, 0, 0, 255), WinUIColor.FromArgb(255, 128, 0, 128)),     // Blue to Purple
                    (WinUIColor.FromArgb(255, 128, 0, 128), WinUIColor.FromArgb(255, 255, 0, 0))      // Purple to Red
                };

                var directions = new[]
                {
                    BackgroundGradientDirection.TopToBottom,
                    BackgroundGradientDirection.LeftToRight,
                    BackgroundGradientDirection.DiagonalTopLeftToBottomRight,
                    BackgroundGradientDirection.DiagonalTopRightToBottomLeft,
                    BackgroundGradientDirection.RadialFromCenter
                };

                for (int cycle = 0; cycle < 2; cycle++) // 2 cycles
                {
                    foreach (var (startColor, endColor) in colors)
                    {
                        var direction = directions[cycle % directions.Length];
                        await _interactiveService.SetBackgroundGradientAsync(startColor, endColor, direction);
                        await Task.Delay(800); // Fast transitions
                    }
                }

                StatusLabel.Content = "Animated background demo completed";
                
                // Re-enable the button
                if (sender is System.Windows.Controls.Button btn)
                    btn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error in animated background demo: {ex.Message}";
                if (sender is System.Windows.Controls.Button btn)
                    btn.IsEnabled = true;
            }
        }

        /// <summary>
        /// Create a sample scene with background and elements
        /// </summary>
        private async void CreateSampleSceneButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            try
            {
                StatusLabel.Content = "Creating sample scene...";

                // Clear existing elements
                _interactiveService.ClearElements();
                _motionElements.Clear();

                // Set a nice gradient background
                await _interactiveService.SetBackgroundGradientAsync(
                    WinUIColor.FromArgb(255, 25, 25, 112),    // MidnightBlue
                    WinUIColor.FromArgb(255, 65, 105, 225),   // RoyalBlue
                    BackgroundGradientDirection.TopToBottom);

                // Add title text element
                var titleElement = new TextElement("Sample Scene Title")
                {
                    Text = "Background Demo Scene",
                    Position = new WinFoundation.Point(50, 50),
                    Size = new WinFoundation.Size(400, 60),
                    FontSize = 28,
                    TextColor = WinUIColor.FromArgb(255, 255, 255, 255),
                    IsDraggable = true
                };
                _interactiveService.AddElement(titleElement);

                // Add info text element
                var infoElement = new TextElement("Scene Info")
                {
                    Text = "This scene demonstrates background support",
                    Position = new WinFoundation.Point(50, 120),
                    Size = new WinFoundation.Size(400, 30),
                    FontSize = 16,
                    TextColor = WinUIColor.FromArgb(255, 200, 200, 255),
                    IsDraggable = true
                };
                _interactiveService.AddElement(infoElement);

                // Add background info element
                var backgroundInfoElement = new TextElement("Background Info")
                {
                    Text = $"Current background: {_interactiveService.BackgroundType}",
                    Position = new WinFoundation.Point(50, 160),
                    Size = new WinFoundation.Size(400, 20),
                    FontSize = 12,
                    TextColor = WinUIColor.FromArgb(255, 180, 180, 220),
                    IsDraggable = true
                };
                _interactiveService.AddElement(backgroundInfoElement);

                // Add some animated bouncing balls for visual interest
                for (int i = 0; i < 3; i++)
                {
                    var motionConfig = new ElementMotionConfig
                    {
                        MotionType = MotionType.Bounce,
                        Speed = 100f + i * 20f,
                        RespectBoundaries = true,
                        ShowTrail = true,
                        TrailLength = 15
                    };

                    var colors = new[] { 
                        WinUIColor.FromArgb(255, 255, 255, 0),   // Yellow
                        WinUIColor.FromArgb(255, 255, 0, 255),   // Magenta
                        WinUIColor.FromArgb(255, 0, 255, 255)    // Cyan
                    };

                    var elementIndex = _interactiveService.AddCircleElementWithMotion(
                        new WinFoundation.Point(100 + i * 50, 250 + i * 30),
                        15f + i * 5f,
                        colors[i],
                        motionConfig);

                    if (elementIndex >= 0)
                    {
                        var elements = _interactiveService.GetElements();
                        if (elementIndex < elements.Count)
                        {
                            _motionElements.Add(elements[elementIndex]);
                        }
                    }
                }

                // Add a rotating text element around the center
                var centerRotatingTextConfig = new ElementMotionConfig
                {
                    MotionType = MotionType.Circular,
                    Speed = 1.0f,
                    Center = new Vector2(_interactiveService.Width / 2, _interactiveService.Height / 2),
                    Radius = 80f,
                    RespectBoundaries = false,
                    ShowTrail = false
                };

                var centerTextConfig = new TextElementConfig
                {
                    FontSize = 16,
                    TextColor = WinUIColor.FromArgb(255, 255, 255, 255),
                    IsDraggable = false
                };

                var rotatingTextIndex = _interactiveService.AddTextElementWithMotion(
                    "Rotating Text",
                    new WinFoundation.Point(_interactiveService.Width / 2, _interactiveService.Height / 2),
                    centerRotatingTextConfig,
                    centerTextConfig);

                if (rotatingTextIndex >= 0)
                {
                    var elements = _interactiveService.GetElements();
                    if (rotatingTextIndex < elements.Count)
                    {
                        _motionElements.Add(elements[rotatingTextIndex]);
                    }
                }

                UpdateElementsList();
                StatusLabel.Content = "Sample scene created with gradient background and animated elements";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error creating sample scene: {ex.Message}";
            }
        }

        /// <summary>
        /// Handle background change events
        /// </summary>
        private void OnBackgroundChanged(string backgroundInfo)
        {
            Dispatcher.Invoke(() =>
            {
                // Show brief status message about the background change
                var shortInfo = backgroundInfo.Length > 50 ? backgroundInfo.Substring(0, 50) + "..." : backgroundInfo;
                StatusLabel.Content = $"Background: {shortInfo}";
            });
        }

        #endregion

        #region JSON Scene Export/Import

        /// <summary>
        /// Export current render scene to JSON
        /// </summary>
        private async void ExportSceneToJsonButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null)
            {
                StatusLabel.Content = "Service not initialized";
                return;
            }

            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "JSON Scene Files (*.json)|*.json|All Files (*.*)|*.*",
                    DefaultExt = "json",
                    FileName = $"Scene_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var sceneData = await _interactiveService.ExportSceneToJsonAsync();
                    await File.WriteAllTextAsync(saveDialog.FileName, sceneData);
                    
                    StatusLabel.Content = $"Scene exported: {Path.GetFileName(saveDialog.FileName)}";
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Export failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Import render scene from JSON
        /// </summary>
        private async void ImportSceneFromJsonButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null)
            {
                StatusLabel.Content = "Service not initialized";
                return;
            }

            try
            {
                var openDialog = new OpenFileDialog
                {
                    Filter = "JSON Scene Files (*.json)|*.json|All Files (*.*)|*.*",
                    Title = "Select Scene File"
                };

                if (openDialog.ShowDialog() == true)
                {
                    if (File.Exists(openDialog.FileName))
                    {
                        var jsonData = await File.ReadAllTextAsync(openDialog.FileName);
                        var success = await _interactiveService.ImportSceneFromJsonAsync(jsonData);
                        
                        if (success)
                        {
                            // Clear and update local tracking
                            _motionElements.Clear();
                            
                            // Re-populate motion elements from service
                            var elements = _interactiveService.GetElements();
                            foreach (var element in elements)
                            {
                                if (element is IMotionElement)
                                {
                                    _motionElements.Add(element);
                                }
                            }
                            
                            UpdateElementsList();
                            UpdateLiveDataCheckboxes();
                            StatusLabel.Content = $"Scene imported: {Path.GetFileName(openDialog.FileName)} ({elements.Count} elements)";
                        }
                        else
                        {
                            StatusLabel.Content = "Failed to import scene - invalid format or data";
                        }
                    }
                    else
                    {
                        StatusLabel.Content = "Selected file does not exist";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Import failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Save scene preset with custom name
        /// </summary>
        private async void SaveScenePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null)
            {
                StatusLabel.Content = "Service not initialized";
                return;
            }

            try
            {
                var presetName = ShowInputDialog("Save Scene Preset", "Enter preset name:", $"Preset_{DateTime.Now:yyyyMMdd_HHmmss}");

                if (!string.IsNullOrWhiteSpace(presetName))
                {
                    var presetsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RenderDemo", "Presets");
                    Directory.CreateDirectory(presetsDir);

                    var fileName = Path.Combine(presetsDir, $"{presetName.Trim()}.json");
                    var sceneData = await _interactiveService.ExportSceneToJsonAsync();
                    await File.WriteAllTextAsync(fileName, sceneData);
                    
                    StatusLabel.Content = $"Preset saved: {presetName}";
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Save preset failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Load scene preset from presets directory
        /// </summary>
        private async void LoadScenePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null)
            {
                StatusLabel.Content = "Service not initialized";
                return;
            }

            try
            {
                var presetsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RenderDemo", "Presets");
                
                if (!Directory.Exists(presetsDir))
                {
                    StatusLabel.Content = "No presets directory found";
                    return;
                }

                var presetFiles = Directory.GetFiles(presetsDir, "*.json");
                if (presetFiles.Length == 0)
                {
                    StatusLabel.Content = "No presets found";
                    return;
                }

                var presetNames = presetFiles.Select(f => Path.GetFileNameWithoutExtension(f)).ToList();
                var selectedPreset = ShowSelectionDialog("Load Scene Preset", "Select a preset to load:", presetNames);

                if (!string.IsNullOrWhiteSpace(selectedPreset))
                {
                    var presetFile = Path.Combine(presetsDir, $"{selectedPreset.Trim()}.json");
                    if (File.Exists(presetFile))
                    {
                        var jsonData = await File.ReadAllTextAsync(presetFile);
                        var success = await _interactiveService.ImportSceneFromJsonAsync(jsonData);
                        
                        if (success)
                        {
                            // Update local tracking
                            _motionElements.Clear();
                            var elements = _interactiveService.GetElements();
                            foreach (var element in elements)
                            {
                                if (element is IMotionElement)
                                {
                                    _motionElements.Add(element);
                                }
                            }
                            
                            UpdateElementsList();
                            UpdateLiveDataCheckboxes();
                            StatusLabel.Content = $"Preset loaded: {selectedPreset} ({elements.Count} elements)";
                        }
                        else
                        {
                            StatusLabel.Content = "Failed to load preset";
                        }
                    }
                    else
                    {
                        StatusLabel.Content = "Preset file not found";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Load preset failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Get current scene info as JSON preview
        /// </summary>
        private async void GetSceneInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null)
            {
                StatusLabel.Content = "Service not initialized";
                return;
            }

            try
            {
                var sceneInfo = await _interactiveService.GetSceneInfoAsync();
                
                var infoWindow = new Window
                {
                    Title = "Scene Information",
                    Width = 500,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this)
                };

                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(10)
                };

                var textBlock = new TextBlock
                {
                    Text = sceneInfo,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                };

                scrollViewer.Content = textBlock;
                infoWindow.Content = scrollViewer;
                infoWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Get scene info failed: {ex.Message}";
            }
        }

        #endregion

        #region Helper Dialog Methods

        /// <summary>
        /// Show input dialog for text entry
        /// </summary>
        private string? ShowInputDialog(string title, string prompt, string defaultValue = "")
        {
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new System.Windows.Controls.Label
            {
                Content = prompt,
                Margin = new Thickness(10)
            };
            Grid.SetRow(label, 0);

            var textBox = new System.Windows.Controls.TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(10),
                Padding = new Thickness(5)
            };
            Grid.SetRow(textBox, 1);

            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            var okButton = new System.Windows.Controls.Button
            {
                Content = "OK",
                Width = 75,
                Height = 25,
                Margin = new Thickness(5, 0, 0, 0),
                IsDefault = true
            };

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 75,
                Height = 25,
                Margin = new Thickness(5, 0, 0, 0),
                IsCancel = true
            };

            string? result = null;

            okButton.Click += (s, e) =>
            {
                result = textBox.Text;
                dialog.DialogResult = true;
                dialog.Close();
            };

            cancelButton.Click += (s, e) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;

            dialog.Loaded += (s, e) =>
            {
                textBox.SelectAll();
                textBox.Focus();
            };

            return dialog.ShowDialog() == true ? result : null;
        }

        /// <summary>
        /// Show selection dialog for choosing from a list
        /// </summary>
        private string? ShowSelectionDialog(string title, string prompt, List<string> items)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new System.Windows.Controls.Label
            {
                Content = prompt,
                Margin = new Thickness(10, 10, 10, 5)
            };
            Grid.SetRow(label, 0);

            var listBox = new System.Windows.Controls.ListBox
            {
                Margin = new Thickness(10, 5, 10, 10),
                ItemsSource = items
            };
            Grid.SetRow(listBox, 1);

            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            var okButton = new System.Windows.Controls.Button
            {
                Content = "OK",
                Width = 75,
                Height = 25,
                Margin = new Thickness(5, 0, 0, 0),
                IsDefault = true,
                IsEnabled = false
            };

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 75,
                Height = 25,
                Margin = new Thickness(5, 0, 0, 0),
                IsCancel = true
            };

            string? result = null;

            listBox.SelectionChanged += (s, e) =>
            {
                okButton.IsEnabled = listBox.SelectedItem != null;
            };

            listBox.MouseDoubleClick += (s, e) =>
            {
                if (listBox.SelectedItem != null)
                {
                    result = listBox.SelectedItem.ToString();
                    dialog.DialogResult = true;
                    dialog.Close();
                }
            };

            okButton.Click += (s, e) =>
            {
                if (listBox.SelectedItem != null)
                {
                    result = listBox.SelectedItem.ToString();
                    dialog.DialogResult = true;
                    dialog.Close();
                }
            };

            cancelButton.Click += (s, e) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(label);
            grid.Children.Add(listBox);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;

            return dialog.ShowDialog() == true ? result : null;
        }

        #endregion
    }
}