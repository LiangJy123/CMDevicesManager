using DevWinUIGallery.Services;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Numerics;

namespace DevWinUIGallery.Views
{
    /// <summary>
    /// Example page showing how to integrate the enhanced motion capabilities
    /// into an existing WinUI 3 application
    /// </summary>
    public sealed partial class EnhancedMotionPage : Page
    {
        private AdvancedBackgroundRenderer _renderer;
        private int _elementCounter = 0;

        public EnhancedMotionPage()
        {
            this.InitializeComponent();
            this.Loaded += OnPageLoaded;
            this.Unloaded += OnPageUnloaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            InitializeRenderer();
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            _renderer?.StopAnimation();
            _renderer?.Dispose();
        }

        private void InitializeRenderer()
        {
            _renderer = new AdvancedBackgroundRenderer();
            _renderer.Initialize(MainCanvas); // Assume MainCanvas is a CanvasControl in XAML
            _renderer.StartAnimation();
            
            UpdateStatusText("Enhanced motion renderer initialized - ready to add elements!");
        }

        // Event handlers for UI buttons (defined in XAML)
        
        private void OnAddLinearMotion_Click(object sender, RoutedEventArgs e)
        {
            var config = new ElementMotionConfig
            {
                MotionType = MotionType.Linear,
                Speed = 120f,
                Direction = GetRandomDirection(),
                RespectBoundaries = true,
                ShowTrail = true,
                TrailLength = 20
            };

            var position = GetRandomPosition();
            var elementId = _renderer.AddTextElementWithMotion(
                $"Linear #{++_elementCounter}", 
                position, 
                config);

            UpdateStatusText($"Added linear motion element #{_elementCounter}");
        }

        private void OnAddCircularMotion_Click(object sender, RoutedEventArgs e)
        {
            var center = GetRandomPosition();
            var config = new ElementMotionConfig
            {
                MotionType = MotionType.Circular,
                Speed = 1.5f + (float)(new Random().NextDouble() * 2.0), // 1.5-3.5 rad/s
                Center = center,
                Radius = 50f + new Random().Next(0, 100) // 50-150 radius
            };

            var textConfig = new LiveTextConfig
            {
                FontSize = 18,
                TextColor = Colors.Cyan,
                EnableGlow = true,
                GlowRadius = 3
            };

            var elementId = _renderer.AddTextElementWithMotion(
                $"Orbit #{++_elementCounter}", 
                center, 
                config, 
                textConfig);

            UpdateStatusText($"Added circular motion element #{_elementCounter}");
        }

        private void OnAddBouncingBall_Click(object sender, RoutedEventArgs e)
        {
            var colors = new[] { Colors.Red, Colors.Blue, Colors.Green, Colors.Orange, Colors.Purple };
            var random = new Random();
            
            var config = new ElementMotionConfig
            {
                MotionType = MotionType.Bounce,
                Speed = 100f + random.Next(0, 100),
                Direction = GetRandomDirection(),
                RespectBoundaries = true,
                ShowTrail = random.Next(0, 2) == 1
            };

            var position = GetRandomPosition();
            var color = colors[random.Next(colors.Length)];
            var radius = 8f + random.Next(0, 12);

            var elementId = _renderer.AddCircleElementWithMotion(position, radius, color, config);
            _elementCounter++;

            UpdateStatusText($"Added bouncing ball #{_elementCounter}");
        }

        private void OnAddSpiralMotion_Click(object sender, RoutedEventArgs e)
        {
            var center = GetRandomPosition();
            var config = new ElementMotionConfig
            {
                MotionType = MotionType.Spiral,
                Speed = 1.8f,
                Center = center,
                Radius = 15f,
                ShowTrail = true,
                TrailLength = 30
            };

            var textConfig = new LiveTextConfig
            {
                FontSize = 14,
                TextColor = Colors.LimeGreen,
                EnableBreathing = true,
                BreathingSpeed = 2.0f
            };

            var elementId = _renderer.AddTextElementWithMotion(
                $"Spiral #{++_elementCounter}", 
                center, 
                config, 
                textConfig);

            UpdateStatusText($"Added spiral motion element #{_elementCounter}");
        }

        private void OnAddOscillateMotion_Click(object sender, RoutedEventArgs e)
        {
            var random = new Random();
            var center = GetRandomPosition();
            var directions = new[] 
            { 
                new Vector2(1, 0),      // Horizontal
                new Vector2(0, 1),      // Vertical
                new Vector2(0.7f, 0.7f), // Diagonal
                new Vector2(-0.7f, 0.7f) // Other diagonal
            };
            
            var config = new ElementMotionConfig
            {
                MotionType = MotionType.Oscillate,
                Speed = 2.5f + (float)(random.NextDouble() * 2.0), // 2.5-4.5
                Direction = directions[random.Next(directions.Length)],
                Center = center,
                Radius = 40f + random.Next(0, 40) // 40-80 amplitude
            };

            var colors = new[] { Colors.Yellow, Colors.Pink, Colors.Orange, Colors.Cyan };
            var elementId = _renderer.AddCircleElementWithMotion(
                center, 
                10f, 
                colors[random.Next(colors.Length)], 
                config);
            _elementCounter++;

            UpdateStatusText($"Added oscillating element #{_elementCounter}");
        }

        private void OnAddRandomMotion_Click(object sender, RoutedEventArgs e)
        {
            var config = new ElementMotionConfig
            {
                MotionType = MotionType.Random,
                Speed = 80f,
                RespectBoundaries = true,
                ShowTrail = true,
                TrailLength = 15
            };

            var textConfig = new LiveTextConfig
            {
                FontSize = 16,
                TextColor = Colors.White,
                EnableGlow = true,
                GlowRadius = 4
            };

            var position = GetRandomPosition();
            var elementId = _renderer.AddTextElementWithMotion(
                $"Random #{++_elementCounter}", 
                position, 
                config, 
                textConfig);

            UpdateStatusText($"Added random motion element #{_elementCounter}");
        }

        private void OnAddWaveMotion_Click(object sender, RoutedEventArgs e)
        {
            var config = new ElementMotionConfig
            {
                MotionType = MotionType.Wave,
                Speed = 1.2f,
                Radius = 25f + new Random().Next(0, 30) // Wave amplitude
            };

            var textConfig = new LiveTextConfig
            {
                FontSize = 20,
                TextColor = Colors.Aqua,
                EnableBreathing = true
            };

            // Start waves from the left side
            var position = new Vector2(0, GetRandomPosition().Y);
            var elementId = _renderer.AddTextElementWithMotion(
                $"Wave #{++_elementCounter}", 
                position, 
                config, 
                textConfig);

            UpdateStatusText($"Added wave motion element #{_elementCounter}");
        }

        private void OnAddOrbitMotion_Click(object sender, RoutedEventArgs e)
        {
            var center = GetRandomPosition();
            var random = new Random();
            
            // Add central "planet"
            _renderer.AddCircleElementWithMotion(
                center, 15f, Colors.Yellow,
                new ElementMotionConfig { MotionType = MotionType.None });

            // Add orbiting "moons"
            var orbitData = new[]
            {
                new { Radius = 40f, Speed = 2.2f, Size = 6f, Color = Colors.Gray },
                new { Radius = 70f, Speed = 1.5f, Size = 8f, Color = Colors.LightBlue },
                new { Radius = 100f, Speed = 1.0f, Size = 5f, Color = Colors.Brown }
            };

            foreach (var orbit in orbitData)
            {
                var orbitConfig = new ElementMotionConfig
                {
                    MotionType = MotionType.Orbit,
                    Speed = orbit.Speed,
                    Center = center,
                    Radius = orbit.Radius,
                    ShowTrail = true,
                    TrailLength = 15
                };

                _renderer.AddCircleElementWithMotion(center, orbit.Size, orbit.Color, orbitConfig);
                _elementCounter++;
            }

            UpdateStatusText($"Added orbital system with {orbitData.Length + 1} elements");
        }

        private void OnCreateMotionShowcase_Click(object sender, RoutedEventArgs e)
        {
            _renderer.ClearElements();
            _elementCounter = 0;

            var center = new Vector2(400, 300);
            
            // Central rotating text
            var centralConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Circular,
                Speed = 0.8f,
                Center = center,
                Radius = 20f
            };
            
            var centralTextConfig = new LiveTextConfig
            {
                FontSize = 28,
                TextColor = Colors.White,
                EnableGlow = true,
                GlowRadius = 6,
                EnableBreathing = true
            };

            _renderer.AddTextElementWithMotion("MOTION", center, centralConfig, centralTextConfig);

            // Surrounding demonstration elements
            var demoElements = new[]
            {
                new { Type = MotionType.Bounce, Name = "Bounce", Radius = 120f, Speed = 150f, Color = Colors.Red },
                new { Type = MotionType.Linear, Name = "Linear", Radius = 150f, Speed = 100f, Color = Colors.Orange },
                new { Type = MotionType.Oscillate, Name = "Oscillate", Radius = 180f, Speed = 3.0f, Color = Colors.Yellow },
                new { Type = MotionType.Spiral, Name = "Spiral", Radius = 210f, Speed = 2.0f, Color = Colors.Green },
                new { Type = MotionType.Random, Name = "Random", Radius = 240f, Speed = 80f, Color = Colors.Cyan },
                new { Type = MotionType.Wave, Name = "Wave", Radius = 270f, Speed = 1.5f, Color = Colors.Blue },
                new { Type = MotionType.Orbit, Name = "Orbit", Radius = 300f, Speed = 1.8f, Color = Colors.Purple }
            };

            for (int i = 0; i < demoElements.Length; i++)
            {
                var angle = i * Math.PI * 2 / demoElements.Length;
                var element = demoElements[i];
                
                var regionCenter = center + new Vector2(
                    (float)Math.Cos(angle) * element.Radius,
                    (float)Math.Sin(angle) * element.Radius);

                var config = new ElementMotionConfig
                {
                    MotionType = element.Type,
                    Speed = element.Speed,
                    Center = regionCenter,
                    Radius = 30f,
                    Direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)),
                    RespectBoundaries = true,
                    ShowTrail = element.Type == MotionType.Spiral || element.Type == MotionType.Random
                };

                _renderer.AddCircleElementWithMotion(regionCenter, 8f, element.Color, config);
                _elementCounter++;

                // Add labels
                var labelPosition = regionCenter + new Vector2(0, 25);
                var labelConfig = new ElementMotionConfig { MotionType = MotionType.None };
                var labelTextConfig = new LiveTextConfig 
                { 
                    FontSize = 12, 
                    TextColor = element.Color,
                    EnableGlow = true,
                    GlowRadius = 2
                };
                
                _renderer.AddTextElementWithMotion(element.Name, labelPosition, labelConfig, labelTextConfig);
                _elementCounter++;
            }

            UpdateStatusText($"Created motion showcase with {_elementCounter} elements");
        }

        private void OnPauseAllMotion_Click(object sender, RoutedEventArgs e)
        {
            _renderer?.StopAllMotion();
            UpdateStatusText("All motion paused");
        }

        private void OnResumeAllMotion_Click(object sender, RoutedEventArgs e)
        {
            _renderer?.ResumeAllMotion();
            UpdateStatusText("All motion resumed");
        }

        private void OnClearElements_Click(object sender, RoutedEventArgs e)
        {
            _renderer?.ClearElements();
            _elementCounter = 0;
            UpdateStatusText("All elements cleared");
        }

        private void OnToggleAnimation_Click(object sender, RoutedEventArgs e)
        {
            if (_renderer == null) return;

            // This would need to be tracked with a field in a real implementation
            static bool isAnimating = true;
            
            if (isAnimating)
            {
                _renderer.StopAnimation();
                UpdateStatusText("Animation stopped");
            }
            else
            {
                _renderer.StartAnimation();
                UpdateStatusText("Animation started");
            }
            
            isAnimating = !isAnimating;
        }

        // Helper methods
        private Vector2 GetRandomPosition()
        {
            var random = new Random();
            return new Vector2(
                random.Next(50, 750),  // Assuming 800px width with margins
                random.Next(50, 550)   // Assuming 600px height with margins
            );
        }

        private Vector2 GetRandomDirection()
        {
            var random = new Random();
            var angle = random.NextDouble() * Math.PI * 2;
            return new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        private void UpdateStatusText(string message)
        {
            // Assume StatusTextBlock is defined in XAML
            if (StatusTextBlock != null)
            {
                StatusTextBlock.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
            }
        }
    }
}

/*
XAML for the page (EnhancedMotionPage.xaml):

<Page x:Class="DevWinUIGallery.Views.EnhancedMotionPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:canvas="using:Microsoft.Graphics.Canvas.UI.Xaml">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Main Canvas Area -->
        <canvas:CanvasControl x:Name="MainCanvas" 
                             Grid.Row="0" 
                             Background="Black" 
                             Margin="10"/>

        <!-- Control Buttons -->
        <Grid Grid.Row="1" Margin="10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <!-- Motion Type Buttons Row 1 -->
            <Button Grid.Row="0" Grid.Column="0" Content="Add Linear" Click="OnAddLinearMotion_Click" Margin="2"/>
            <Button Grid.Row="0" Grid.Column="1" Content="Add Circular" Click="OnAddCircularMotion_Click" Margin="2"/>
            <Button Grid.Row="0" Grid.Column="2" Content="Add Bouncing Ball" Click="OnAddBouncingBall_Click" Margin="2"/>
            <Button Grid.Row="0" Grid.Column="3" Content="Add Spiral" Click="OnAddSpiralMotion_Click" Margin="2"/>

            <!-- Motion Type Buttons Row 2 -->
            <Button Grid.Row="1" Grid.Column="0" Content="Add Oscillate" Click="OnAddOscillateMotion_Click" Margin="2"/>
            <Button Grid.Row="1" Grid.Column="1" Content="Add Random" Click="OnAddRandomMotion_Click" Margin="2"/>
            <Button Grid.Row="1" Grid.Column="2" Content="Add Wave" Click="OnAddWaveMotion_Click" Margin="2"/>
            <Button Grid.Row="1" Grid.Column="3" Content="Add Orbit" Click="OnAddOrbitMotion_Click" Margin="2"/>
        </Grid>

        <!-- Control and Status Row -->
        <Grid Grid.Row="2" Margin="10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <Button Grid.Column="0" Content="Motion Showcase" Click="OnCreateMotionShowcase_Click" Margin="2"/>
            <Button Grid.Column="1" Content="Pause All" Click="OnPauseAllMotion_Click" Margin="2"/>
            <Button Grid.Column="2" Content="Resume All" Click="OnResumeAllMotion_Click" Margin="2"/>
            <Button Grid.Column="3" Content="Clear All" Click="OnClearElements_Click" Margin="2"/>
            
            <TextBlock x:Name="StatusTextBlock" 
                       Grid.Column="4" 
                       Text="Ready to add motion elements..." 
                       VerticalAlignment="Center" 
                       Margin="10,0"/>
        </Grid>
    </Grid>
</Page>

*/