using CMDevicesManager.Services;
using CMDevicesManager.Models;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows;
using WinFoundation = Windows.Foundation;
using WinUIColor = Windows.UI.Color;

namespace CMDevicesManager.Examples
{
    /// <summary>
    /// Example demonstrating enhanced motion capabilities for InteractiveWin2DRenderingService
    /// Shows how to create and control various types of animated elements with motion
    /// </summary>
    public class EnhancedMotionExample : IDisposable
    {
        private InteractiveWin2DRenderingService _renderingService;
        private readonly List<RenderElement> _motionElements = new();
        private bool _isInitialized = false;
        private bool _disposed = false;

        public event Action<string> StatusChanged;

        /// <summary>
        /// Initialize the motion example with HID integration
        /// </summary>
        public async Task InitializeAsync(int width = 800, int height = 600)
        {
            try
            {
                _renderingService = new InteractiveWin2DRenderingService();
                await _renderingService.InitializeAsync(width, height);

                // Subscribe to events
                _renderingService.ImageRendered += OnImageRendered;
                _renderingService.ElementSelected += OnElementSelected;
                _renderingService.ElementMoved += OnElementMoved;
                _renderingService.HidStatusChanged += OnHidStatusChanged;
                _renderingService.RenderingError += OnRenderingError;

                _isInitialized = true;
                StatusChanged?.Invoke("Enhanced motion system initialized successfully");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Failed to initialize motion system: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Start the motion demonstration with HID streaming
        /// </summary>
        public async Task StartMotionDemoAsync(int fps = 30, bool enableHidStreaming = true)
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("System not initialized. Call InitializeAsync first.");
            }

            try
            {
                // Configure rendering settings
                _renderingService.TargetFPS = fps;
                _renderingService.JpegQuality = 85;

                if (enableHidStreaming && _renderingService.IsHidServiceConnected)
                {
                    // Enable HID real-time streaming
                    _renderingService.EnableHidTransfer(true, useSuspendMedia: false);
                    var hidEnabled = await _renderingService.EnableHidRealTimeDisplayAsync(true);
                    
                    if (hidEnabled)
                    {
                        StatusChanged?.Invoke("HID real-time streaming enabled");
                    }
                    else
                    {
                        StatusChanged?.Invoke("HID streaming could not be enabled - continuing without HID");
                    }
                }

                // Start automatic rendering
                _renderingService.StartAutoRendering(fps);

                StatusChanged?.Invoke($"Motion demo started at {fps} FPS" + 
                    (enableHidStreaming ? " with HID streaming" : ""));
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Failed to start motion demo: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Stop the motion demonstration
        /// </summary>
        public async Task StopMotionDemoAsync()
        {
            try
            {
                // Stop HID streaming if enabled
                if (_renderingService.IsHidRealTimeModeEnabled)
                {
                    await _renderingService.EnableHidRealTimeDisplayAsync(false);
                }

                // Stop automatic rendering
                _renderingService.StopAutoRendering();

                StatusChanged?.Invoke("Motion demo stopped");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error stopping motion demo: {ex.Message}");
            }
        }

        #region Motion Demo Methods

        /// <summary>
        /// Add a bouncing ball with trail
        /// </summary>
        public void AddBouncingBall()
        {
            var random = new Random();
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
                random.Next(50, _renderingService.Width - 50),
                random.Next(50, _renderingService.Height - 50));

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Bounce,
                Speed = 120f + random.Next(0, 60), // 120-180 pixels per second
                Direction = GetRandomDirection(),
                RespectBoundaries = true,
                ShowTrail = true,
                TrailLength = 15
            };

            var color = colors[random.Next(colors.Length)];
            var radius = 8f + random.Next(0, 8);

            var elementIndex = _renderingService.AddCircleElementWithMotion(position, radius, color, motionConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _renderingService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    StatusChanged?.Invoke($"Added bouncing ball #{_motionElements.Count}");
                }
            }
        }

        /// <summary>
        /// Add rotating text around a center point
        /// </summary>
        public void AddRotatingText(string text = null)
        {
            text ??= $"Orbit #{_motionElements.Count + 1}";

            var center = new WinFoundation.Point(
                _renderingService.Width / 2.0,
                _renderingService.Height / 2.0);

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

            var elementIndex = _renderingService.AddTextElementWithMotion(text, center, motionConfig, textConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _renderingService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    StatusChanged?.Invoke($"Added rotating text at radius {radius:F0}");
                }
            }
        }

        /// <summary>
        /// Add oscillating shapes
        /// </summary>
        public void AddOscillatingShape()
        {
            var random = new Random();
            var position = new WinFoundation.Point(
                random.Next(100, _renderingService.Width - 100),
                random.Next(100, _renderingService.Height - 100));

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
                Speed = 2.0f + (float)(random.NextDouble() * 2.0), // 2.0-4.0 oscillations per second
                Direction = directions[random.Next(directions.Length)],
                Center = new Vector2((float)position.X, (float)position.Y),
                Radius = 30f + random.Next(0, 20), // 30-50 amplitude
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

            var color = colors[random.Next(colors.Length)];
            var size = new WinFoundation.Size(20 + random.Next(0, 20), 20 + random.Next(0, 20));

            var elementIndex = _renderingService.AddRectangleElementWithMotion(position, size, color, motionConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _renderingService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    StatusChanged?.Invoke($"Added oscillating shape #{_motionElements.Count}");
                }
            }
        }

        /// <summary>
        /// Add spiral motion text with expanding trail
        /// </summary>
        public void AddSpiralText(string text = null)
        {
            text ??= $"Spiral {_motionElements.Count + 1}";

            var random = new Random();
            var center = new WinFoundation.Point(
                random.Next(150, _renderingService.Width - 150),
                random.Next(150, _renderingService.Height - 150));

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

            var elementIndex = _renderingService.AddTextElementWithMotion(text, center, motionConfig, textConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _renderingService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    StatusChanged?.Invoke($"Added spiral text starting at ({center.X:F0}, {center.Y:F0})");
                }
            }
        }

        /// <summary>
        /// Add random walking elements
        /// </summary>
        public void AddRandomWalker()
        {
            var random = new Random();
            var position = new WinFoundation.Point(
                random.Next(100, _renderingService.Width - 100),
                random.Next(100, _renderingService.Height - 100));

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Random,
                Speed = 60f + random.Next(0, 40), // 60-100 pixels per second
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

            var color = colors[random.Next(colors.Length)];
            var radius = 6f + random.Next(0, 6);

            var elementIndex = _renderingService.AddCircleElementWithMotion(position, radius, color, motionConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _renderingService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    StatusChanged?.Invoke($"Added random walker #{_motionElements.Count}");
                }
            }
        }

        /// <summary>
        /// Add wave motion text
        /// </summary>
        public void AddWaveText(string text = null)
        {
            text ??= $"Wave {_motionElements.Count + 1}";

            var random = new Random();
            var startY = random.Next(100, _renderingService.Height - 100);
            var position = new WinFoundation.Point(0, startY);

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Wave,
                Speed = 1.2f + (float)(random.NextDouble() * 0.8), // 1.2-2.0
                Radius = 20f + random.Next(0, 20), // Wave amplitude
                RespectBoundaries = false,
                ShowTrail = false
            };

            var textConfig = new TextElementConfig
            {
                FontSize = 16,
                TextColor = WinUIColor.FromArgb(255, 0, 191, 255), // Deep sky blue
                IsDraggable = false
            };

            var elementIndex = _renderingService.AddTextElementWithMotion(text, position, motionConfig, textConfig);
            
            if (elementIndex >= 0)
            {
                var elements = _renderingService.GetElements();
                if (elementIndex < elements.Count)
                {
                    _motionElements.Add(elements[elementIndex]);
                    StatusChanged?.Invoke($"Added wave text with amplitude {motionConfig.Radius:F0}");
                }
            }
        }

        /// <summary>
        /// Add orbital motion system (like planets around sun)
        /// </summary>
        public void AddOrbitalSystem()
        {
            var center = new WinFoundation.Point(
                _renderingService.Width / 2.0,
                _renderingService.Height / 2.0);

            // Central "sun"
            var sunMotionConfig = new ElementMotionConfig { MotionType = MotionType.None };
            var sunIndex = _renderingService.AddCircleElementWithMotion(
                center, 20f, WinUIColor.FromArgb(255, 255, 255, 0), sunMotionConfig);

            // Add "planets"
            var planetData = new[]
            {
                new { Radius = 50f, Speed = 2.2f, Size = 6f, Color = WinUIColor.FromArgb(255, 128, 128, 128) },
                new { Radius = 80f, Speed = 1.5f, Size = 8f, Color = WinUIColor.FromArgb(255, 173, 216, 230) },
                new { Radius = 110f, Speed = 1.0f, Size = 7f, Color = WinUIColor.FromArgb(255, 165, 42, 42) },
                new { Radius = 140f, Speed = 0.7f, Size = 9f, Color = WinUIColor.FromArgb(255, 255, 140, 0) }
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

                var planetIndex = _renderingService.AddCircleElementWithMotion(center, planet.Size, planet.Color, orbitConfig);
                
                if (planetIndex >= 0)
                {
                    var elements = _renderingService.GetElements();
                    if (planetIndex < elements.Count)
                    {
                        _motionElements.Add(elements[planetIndex]);
                    }
                }
            }

            if (sunIndex >= 0)
            {
                var elements = _renderingService.GetElements();
                if (sunIndex < elements.Count)
                {
                    _motionElements.Add(elements[sunIndex]);
                }
            }

            StatusChanged?.Invoke($"Added orbital system with {planetData.Length + 1} objects");
        }

        /// <summary>
        /// Create a comprehensive motion showcase
        /// </summary>
        public void CreateMotionShowcase()
        {
            try
            {
                // Clear existing elements
                _renderingService.ClearElements();
                _motionElements.Clear();

                // Add various motion demonstrations
                AddBouncingBall();
                AddBouncingBall();
                AddBouncingBall();

                AddRotatingText("CENTER");
                AddRotatingText("ORBIT 1");
                AddRotatingText("ORBIT 2");

                AddOscillatingShape();
                AddOscillatingShape();

                AddSpiralText("SPIRAL A");
                AddSpiralText("SPIRAL B");

                AddRandomWalker();
                AddRandomWalker();

                AddWaveText("WAVE MOTION");

                AddOrbitalSystem();

                StatusChanged?.Invoke($"Motion showcase created with {_motionElements.Count} animated elements");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error creating motion showcase: {ex.Message}");
            }
        }

        #endregion

        #region Motion Control

        /// <summary>
        /// Pause all motion
        /// </summary>
        public void PauseAllMotion()
        {
            _renderingService.PauseAllMotion();
        }

        /// <summary>
        /// Resume all motion
        /// </summary>
        public void ResumeAllMotion()
        {
            _renderingService.ResumeAllMotion();
        }

        /// <summary>
        /// Clear all motion elements
        /// </summary>
        public void ClearAllElements()
        {
            _renderingService.ClearElements();
            _motionElements.Clear();
            StatusChanged?.Invoke("All motion elements cleared");
        }

        /// <summary>
        /// Change motion type for a random element
        /// </summary>
        public void RandomizeElementMotion()
        {
            if (_motionElements.Count == 0)
            {
                StatusChanged?.Invoke("No motion elements to randomize");
                return;
            }

            var random = new Random();
            var element = _motionElements[random.Next(_motionElements.Count)];

            if (element is IMotionElement motionElement)
            {
                var motionTypes = Enum.GetValues<MotionType>();
                var newMotionType = motionTypes[random.Next(1, motionTypes.Length)]; // Skip None

                var newConfig = new ElementMotionConfig
                {
                    MotionType = newMotionType,
                    Speed = 80f + random.Next(0, 80),
                    Direction = GetRandomDirection(),
                    Center = new Vector2((float)element.Position.X, (float)element.Position.Y),
                    Radius = 30f + random.Next(0, 50),
                    RespectBoundaries = true,
                    ShowTrail = random.Next(0, 2) == 1,
                    TrailLength = random.Next(10, 25)
                };

                _renderingService.SetElementMotion(element, newConfig);
                StatusChanged?.Invoke($"Changed element motion to {newMotionType}");
            }
        }

        #endregion

        #region Event Handlers

        private void OnImageRendered(System.Windows.Media.Imaging.WriteableBitmap bitmap)
        {
            // Frame rendered - could update UI here
        }

        private void OnElementSelected(RenderElement element)
        {
            StatusChanged?.Invoke($"Selected element: {element.Name}");
        }

        private void OnElementMoved(RenderElement element)
        {
            StatusChanged?.Invoke($"Moved element: {element.Name} to ({element.Position.X:F0}, {element.Position.Y:F0})");
        }

        private void OnHidStatusChanged(string status)
        {
            StatusChanged?.Invoke($"HID: {status}");
        }

        private void OnRenderingError(Exception ex)
        {
            StatusChanged?.Invoke($"Rendering error: {ex.Message}");
        }

        #endregion

        #region Helper Methods

        private Vector2 GetRandomDirection()
        {
            var random = new Random();
            var angle = random.NextDouble() * Math.PI * 2;
            return new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        public int GetMotionElementCount() => _motionElements.Count;

        public bool IsInitialized => _isInitialized;

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _renderingService?.StopAutoRendering();
                _renderingService?.Dispose();
                _motionElements.Clear();
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error during disposal: {ex.Message}");
            }
        }
    }
}

/*
Example usage in a WPF application:

public partial class MotionDemoWindow : Window
{
    private EnhancedMotionExample _motionExample;

    public MotionDemoWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _motionExample = new EnhancedMotionExample();
            _motionExample.StatusChanged += OnStatusChanged;

            await _motionExample.InitializeAsync(800, 600);
            await _motionExample.StartMotionDemoAsync(30, enableHidStreaming: true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to initialize: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClosed(object sender, EventArgs e)
    {
        _motionExample?.Dispose();
    }

    private void OnStatusChanged(string status)
    {
        Dispatcher.Invoke(() =>
        {
            StatusTextBlock.Text = status;
        });
    }

    // Button event handlers
    private void AddBouncingBall_Click(object sender, RoutedEventArgs e)
    {
        _motionExample?.AddBouncingBall();
    }

    private void AddRotatingText_Click(object sender, RoutedEventArgs e)
    {
        _motionExample?.AddRotatingText();
    }

    private void CreateShowcase_Click(object sender, RoutedEventArgs e)
    {
        _motionExample?.CreateMotionShowcase();
    }

    private void PauseMotion_Click(object sender, RoutedEventArgs e)
    {
        _motionExample?.PauseAllMotion();
    }

    private void ResumeMotion_Click(object sender, RoutedEventArgs e)
    {
        _motionExample?.ResumeAllMotion();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _motionExample?.ClearAllElements();
    }
}
*/