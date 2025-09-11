using DevWinUIGallery.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using System;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace DevWinUIGallery.Examples
{
    /// <summary>
    /// Example demonstrating the enhanced motion capabilities of AdvancedBackgroundRenderer
    /// This shows how to create elements with various types of motion including:
    /// - Linear motion with boundary bouncing
    /// - Circular and orbital motion
    /// - Oscillating and wave motion
    /// - Spiral motion with expanding radius
    /// - Random motion with direction changes
    /// - Trail effects and motion controls
    /// </summary>
    public class MotionDemoExample : IDisposable
    {
        private AdvancedBackgroundRenderer _renderer;
        private readonly DispatcherTimer _demoTimer;
        private bool _disposed = false;
        private int _demoStep = 0;

        public event EventHandler<string> StatusChanged;

        public MotionDemoExample()
        {
            _demoTimer = new DispatcherTimer();
            _demoTimer.Interval = TimeSpan.FromSeconds(5); // Change demo every 5 seconds
            _demoTimer.Tick += OnDemoTick;
        }

        /// <summary>
        /// Initialize the motion demo with a canvas control
        /// </summary>
        public void Initialize(Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl canvasControl)
        {
            _renderer = new AdvancedBackgroundRenderer();
            _renderer.Initialize(canvasControl);

            StatusChanged?.Invoke(this, "Motion demo initialized - starting animation showcase");
        }

        /// <summary>
        /// Start the motion demonstration
        /// </summary>
        public void StartDemo()
        {
            if (_renderer == null)
            {
                throw new InvalidOperationException("Initialize must be called first");
            }

            _renderer.StartAnimation();
            _demoTimer.Start();
            
            // Start with the basic motion showcase
            ShowBasicMotionTypes();
        }

        /// <summary>
        /// Stop the motion demonstration
        /// </summary>
        public void StopDemo()
        {
            _demoTimer.Stop();
            _renderer?.StopAnimation();
            _renderer?.ClearElements();
            
            StatusChanged?.Invoke(this, "Motion demo stopped");
        }

        #region Demo Scenarios

        /// <summary>
        /// Demonstrate basic motion types
        /// </summary>
        public void ShowBasicMotionTypes()
        {
            _renderer.ClearElements();
            StatusChanged?.Invoke(this, "Demo 1/8: Basic Motion Types");

            var center = new Vector2(400, 300);

            // Linear motion with bouncing
            var linearConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Linear,
                Speed = 150f,
                Direction = new Vector2(1, 0.5f).Normalized(),
                RespectBoundaries = true,
                ShowTrail = true
            };
            _renderer.AddTextElementWithMotion("Linear Motion", new Vector2(100, 100), linearConfig);

            // Circular motion
            var circularConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Circular,
                Speed = 2.0f,
                Center = center,
                Radius = 80f
            };
            _renderer.AddTextElementWithMotion("Circular", center, circularConfig);

            // Bouncing ball
            var bounceConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Bounce,
                Speed = 200f,
                Direction = new Vector2(0.7f, -0.3f).Normalized(),
                RespectBoundaries = true
            };
            _renderer.AddCircleElementWithMotion(new Vector2(200, 200), 15f, Colors.Red, bounceConfig);
        }

        /// <summary>
        /// Demonstrate oscillation and wave motion
        /// </summary>
        public void ShowOscillationDemo()
        {
            _renderer.ClearElements();
            StatusChanged?.Invoke(this, "Demo 2/8: Oscillation & Wave Motion");

            // Horizontal oscillation
            var horizontalOscillate = new ElementMotionConfig
            {
                MotionType = MotionType.Oscillate,
                Speed = 3.0f,
                Direction = new Vector2(1, 0),
                Center = new Vector2(200, 150),
                Radius = 100f
            };
            _renderer.AddTextElementWithMotion("Horizontal Oscillate", new Vector2(200, 150), horizontalOscillate);

            // Vertical oscillation
            var verticalOscillate = new ElementMotionConfig
            {
                MotionType = MotionType.Oscillate,
                Speed = 2.5f,
                Direction = new Vector2(0, 1),
                Center = new Vector2(400, 200),
                Radius = 80f
            };
            _renderer.AddCircleElementWithMotion(new Vector2(400, 200), 12f, Colors.Blue, verticalOscillate);

            // Wave motion
            var waveConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Wave,
                Speed = 1.5f,
                Radius = 30f
            };
            _renderer.AddTextElementWithMotion("Wave Motion", new Vector2(50, 350), waveConfig);
        }

        /// <summary>
        /// Demonstrate spiral motion
        /// </summary>
        public void ShowSpiralDemo()
        {
            _renderer.ClearElements();
            StatusChanged?.Invoke(this, "Demo 3/8: Spiral Motion");

            var center = new Vector2(400, 300);

            // Expanding spiral
            var spiralConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Spiral,
                Speed = 2.0f,
                Center = center,
                Radius = 20f,
                ShowTrail = true,
                TrailLength = 30
            };
            _renderer.AddTextElementWithMotion("Expanding Spiral", center, spiralConfig);

            // Multiple spirals with different speeds
            var colors = new[] { Colors.Red, Colors.Green, Colors.Blue };
            for (int i = 0; i < 3; i++)
            {
                var config = new ElementMotionConfig
                {
                    MotionType = MotionType.Spiral,
                    Speed = 1.5f + i * 0.5f,
                    Center = new Vector2(200 + i * 200, 300),
                    Radius = 10f + i * 5f,
                    ShowTrail = true
                };
                _renderer.AddCircleElementWithMotion(new Vector2(200 + i * 200, 300), 8f, colors[i], config);
            }
        }

        /// <summary>
        /// Demonstrate random motion
        /// </summary>
        public void ShowRandomMotionDemo()
        {
            _renderer.ClearElements();
            StatusChanged?.Invoke(this, "Demo 4/8: Random Motion");

            var random = new Random();

            // Multiple random walkers
            for (int i = 0; i < 5; i++)
            {
                var randomConfig = new ElementMotionConfig
                {
                    MotionType = MotionType.Random,
                    Speed = 80f + random.Next(0, 40),
                    RespectBoundaries = true
                };

                var position = new Vector2(
                    random.Next(100, 700),
                    random.Next(100, 500));

                var colors = new[] { Colors.Orange, Colors.Purple, Colors.Cyan, Colors.Pink, Colors.LightGreen };
                _renderer.AddCircleElementWithMotion(position, 10f, colors[i], randomConfig);
            }

            // Random text walker
            var textRandomConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Random,
                Speed = 60f,
                RespectBoundaries = true,
                ShowTrail = true
            };
            _renderer.AddTextElementWithMotion("Random Walker", new Vector2(400, 300), textRandomConfig);
        }

        /// <summary>
        /// Demonstrate orbital motion
        /// </summary>
        public void ShowOrbitDemo()
        {
            _renderer.ClearElements();
            StatusChanged?.Invoke(this, "Demo 5/8: Orbital Motion");

            var center = new Vector2(400, 300);

            // Central "sun"
            _renderer.AddCircleElementWithMotion(center, 20f, Colors.Yellow, new ElementMotionConfig { MotionType = MotionType.None });

            // Orbiting "planets"
            var planetColors = new[] { Colors.Red, Colors.Blue, Colors.Green, Colors.Purple };
            for (int i = 0; i < 4; i++)
            {
                var orbitConfig = new ElementMotionConfig
                {
                    MotionType = MotionType.Orbit,
                    Speed = 1.0f + i * 0.3f,
                    Center = center,
                    Radius = 60f + i * 40f,
                    ShowTrail = true
                };
                _renderer.AddCircleElementWithMotion(center, 8f + i * 2f, planetColors[i], orbitConfig);
            }

            // Add orbital text
            var textOrbitConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Orbit,
                Speed = 0.8f,
                Center = center,
                Radius = 150f
            };
            _renderer.AddTextElementWithMotion("Orbital Text", center, textOrbitConfig);
        }

        /// <summary>
        /// Demonstrate motion controls and trail effects
        /// </summary>
        public void ShowTrailEffectsDemo()
        {
            _renderer.ClearElements();
            StatusChanged?.Invoke(this, "Demo 6/8: Trail Effects");

            // Linear motion with long trail
            var longTrailConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Linear,
                Speed = 100f,
                Direction = new Vector2(1, 0),
                RespectBoundaries = true,
                ShowTrail = true,
                TrailLength = 50
            };
            _renderer.AddCircleElementWithMotion(new Vector2(50, 100), 8f, Colors.Cyan, longTrailConfig);

            // Circular motion with trail
            var circularTrailConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Circular,
                Speed = 3.0f,
                Center = new Vector2(400, 200),
                Radius = 80f,
                ShowTrail = true,
                TrailLength = 30
            };
            _renderer.AddCircleElementWithMotion(new Vector2(400, 200), 6f, Colors.Orange, circularTrailConfig);

            // Bounce with trail
            var bounceTrailConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Bounce,
                Speed = 150f,
                Direction = new Vector2(0.6f, -0.8f).Normalized(),
                RespectBoundaries = true,
                ShowTrail = true,
                TrailLength = 25
            };
            _renderer.AddCircleElementWithMotion(new Vector2(200, 400), 10f, Colors.LimeGreen, bounceTrailConfig);
        }

        /// <summary>
        /// Demonstrate complex motion combinations
        /// </summary>
        public void ShowComplexMotionDemo()
        {
            _renderer.ClearElements();
            StatusChanged?.Invoke(this, "Demo 7/8: Complex Motion Combinations");

            var center = new Vector2(400, 300);

            // Central rotating text with breathing effect
            var centerTextConfig = new LiveTextConfig
            {
                FontSize = 32,
                TextColor = Colors.White,
                EnableBreathing = true,
                BreathingSpeed = 1.5f,
                EnableGlow = true,
                GlowRadius = 5
            };
            var centerMotionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Circular,
                Speed = 1.0f,
                Center = center,
                Radius = 30f
            };
            _renderer.AddTextElementWithMotion("CENTER", center, centerMotionConfig, centerTextConfig);

            // Orbiting elements with different motion types
            for (int i = 0; i < 8; i++)
            {
                var angle = i * Math.PI / 4;
                var position = center + new Vector2(
                    (float)Math.Cos(angle) * 120f,
                    (float)Math.Sin(angle) * 120f);

                var motionTypes = new[] { 
                    MotionType.Bounce, MotionType.Oscillate, MotionType.Spiral, MotionType.Random,
                    MotionType.Linear, MotionType.Wave, MotionType.Orbit, MotionType.Circular 
                };

                var config = new ElementMotionConfig
                {
                    MotionType = motionTypes[i],
                    Speed = 80f + i * 10f,
                    Center = position,
                    Radius = 20f + i * 5f,
                    RespectBoundaries = i % 2 == 0,
                    ShowTrail = i % 3 == 0,
                    Direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle))
                };

                var colors = new[] { 
                    Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green,
                    Colors.Cyan, Colors.Blue, Colors.Purple, Colors.Pink 
                };
                
                _renderer.AddCircleElementWithMotion(position, 6f + i, colors[i], config);
            }
        }

        /// <summary>
        /// Demonstrate motion showcase with all types
        /// </summary>
        public void ShowMotionShowcase()
        {
            _renderer.ClearElements();
            StatusChanged?.Invoke(this, "Demo 8/8: Complete Motion Showcase");

            // Create a comprehensive showcase of all motion types
            var center = new Vector2(400, 300);

            // Central hub
            _renderer.AddTextElementWithMotion("MOTION HUB", center, new ElementMotionConfig
            {
                MotionType = MotionType.Circular,
                Speed = 0.5f,
                Center = center,
                Radius = 10f
            });

            // Surrounding motion demonstrations
            var demoConfigs = new[]
            {
                new { Type = MotionType.Linear, Name = "Linear", Color = Colors.Red },
                new { Type = MotionType.Bounce, Name = "Bounce", Color = Colors.Orange },
                new { Type = MotionType.Oscillate, Name = "Oscillate", Color = Colors.Yellow },
                new { Type = MotionType.Spiral, Name = "Spiral", Color = Colors.Green },
                new { Type = MotionType.Random, Name = "Random", Color = Colors.Cyan },
                new { Type = MotionType.Wave, Name = "Wave", Color = Colors.Blue },
                new { Type = MotionType.Orbit, Name = "Orbit", Color = Colors.Purple }
            };

            for (int i = 0; i < demoConfigs.Length; i++)
            {
                var angle = i * Math.PI * 2 / demoConfigs.Length;
                var regionCenter = center + new Vector2(
                    (float)Math.Cos(angle) * 150f,
                    (float)Math.Sin(angle) * 150f);

                var config = new ElementMotionConfig
                {
                    MotionType = demoConfigs[i].Type,
                    Speed = 100f,
                    Center = regionCenter,
                    Radius = 30f,
                    RespectBoundaries = true,
                    ShowTrail = demoConfigs[i].Type == MotionType.Spiral || demoConfigs[i].Type == MotionType.Random,
                    Direction = new Vector2((float)Math.Cos(angle + Math.PI/2), (float)Math.Sin(angle + Math.PI/2))
                };

                _renderer.AddCircleElementWithMotion(regionCenter, 8f, demoConfigs[i].Color, config);
                
                // Add label
                var labelConfig = new ElementMotionConfig { MotionType = MotionType.None };
                var textConfig = new LiveTextConfig { FontSize = 12, TextColor = demoConfigs[i].Color };
                _renderer.AddTextElementWithMotion(demoConfigs[i].Name, 
                    regionCenter + new Vector2(0, 40), labelConfig, textConfig);
            }
        }

        #endregion

        #region Motion Control Methods

        /// <summary>
        /// Pause all motion in the current demo
        /// </summary>
        public void PauseAllMotion()
        {
            _renderer?.StopAllMotion();
            StatusChanged?.Invoke(this, "All motion paused");
        }

        /// <summary>
        /// Resume all motion in the current demo
        /// </summary>
        public void ResumeAllMotion()
        {
            _renderer?.ResumeAllMotion();
            StatusChanged?.Invoke(this, "All motion resumed");
        }

        /// <summary>
        /// Add a random bouncing ball to the current scene
        /// </summary>
        public void AddRandomBouncingBall()
        {
            if (_renderer == null) return;

            var random = new Random();
            var colors = new[] { Colors.Red, Colors.Blue, Colors.Green, Colors.Orange, Colors.Purple };
            
            var config = new ElementMotionConfig
            {
                MotionType = MotionType.Bounce,
                Speed = 100f + random.Next(0, 100),
                Direction = new Vector2(
                    (float)(random.NextDouble() - 0.5) * 2,
                    (float)(random.NextDouble() - 0.5) * 2).Normalized(),
                RespectBoundaries = true,
                ShowTrail = random.Next(0, 2) == 1
            };

            var position = new Vector2(
                random.Next(50, 750),
                random.Next(50, 550));

            _renderer.AddCircleElementWithMotion(position, 8f + random.Next(0, 8), 
                colors[random.Next(colors.Length)], config);

            StatusChanged?.Invoke(this, "Added random bouncing ball");
        }

        /// <summary>
        /// Add a spiral text element
        /// </summary>
        public void AddSpiralText(string text)
        {
            if (_renderer == null) return;

            var random = new Random();
            var center = new Vector2(
                random.Next(100, 700),
                random.Next(100, 500));

            var config = new ElementMotionConfig
            {
                MotionType = MotionType.Spiral,
                Speed = 2.0f,
                Center = center,
                Radius = 15f,
                ShowTrail = true,
                TrailLength = 25
            };

            var textConfig = new LiveTextConfig
            {
                FontSize = 16,
                TextColor = Colors.LightBlue,
                EnableGlow = true
            };

            _renderer.AddTextElementWithMotion(text, center, config, textConfig);
            StatusChanged?.Invoke(this, $"Added spiral text: {text}");
        }

        #endregion

        private void OnDemoTick(object sender, object e)
        {
            _demoStep = (_demoStep + 1) % 8;

            switch (_demoStep)
            {
                case 0: ShowBasicMotionTypes(); break;
                case 1: ShowOscillationDemo(); break;
                case 2: ShowSpiralDemo(); break;
                case 3: ShowRandomMotionDemo(); break;
                case 4: ShowOrbitDemo(); break;
                case 5: ShowTrailEffectsDemo(); break;
                case 6: ShowComplexMotionDemo(); break;
                case 7: ShowMotionShowcase(); break;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopDemo();
            _demoTimer?.Stop();
            _renderer?.Dispose();
        }
    }

    /// <summary>
    /// Extension methods for Vector2 operations
    /// </summary>
    public static class Vector2Extensions
    {
        public static Vector2 Normalized(this Vector2 vector)
        {
            return Vector2.Normalize(vector);
        }
    }
}

/// <summary>
/// Usage example for integrating the motion demo into a WinUI 3 page
/// </summary>
/*
public sealed partial class MotionDemoPage : Page
{
    private MotionDemoExample _motionDemo;

    public MotionDemoPage()
    {
        this.InitializeComponent();
        this.Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _motionDemo = new MotionDemoExample();
        _motionDemo.StatusChanged += OnMotionDemoStatusChanged;
        _motionDemo.Initialize(MotionCanvas); // MotionCanvas is a CanvasControl in XAML
    }

    private void OnMotionDemoStatusChanged(object sender, string status)
    {
        StatusTextBlock.Text = status; // StatusTextBlock is a TextBlock in XAML
    }

    private void StartDemo_Click(object sender, RoutedEventArgs e)
    {
        _motionDemo?.StartDemo();
    }

    private void StopDemo_Click(object sender, RoutedEventArgs e)
    {
        _motionDemo?.StopDemo();
    }

    private void PauseMotion_Click(object sender, RoutedEventArgs e)
    {
        _motionDemo?.PauseAllMotion();
    }

    private void ResumeMotion_Click(object sender, RoutedEventArgs e)
    {
        _motionDemo?.ResumeAllMotion();
    }

    private void AddBouncingBall_Click(object sender, RoutedEventArgs e)
    {
        _motionDemo?.AddRandomBouncingBall();
    }

    private void AddSpiralText_Click(object sender, RoutedEventArgs e)
    {
        _motionDemo?.AddSpiralText("SPIRAL!");
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _motionDemo?.Dispose();
    }
}
*/