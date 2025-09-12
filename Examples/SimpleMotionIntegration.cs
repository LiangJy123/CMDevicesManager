using CMDevicesManager.Services;
using CMDevicesManager.Models;
using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows;
using WinFoundation = Windows.Foundation;
using WinUIColor = Windows.UI.Color;

namespace CMDevicesManager.Examples
{
    /// <summary>
    /// Simple integration example for adding motion to existing InteractiveWin2DRenderingService usage
    /// This example shows how to enhance existing code with motion capabilities
    /// </summary>
    public class SimpleMotionIntegration
    {
        private InteractiveWin2DRenderingService _service;

        /// <summary>
        /// Initialize the service with motion capabilities
        /// </summary>
        public async Task InitializeWithMotionAsync(int width = 800, int height = 600)
        {
            _service = new InteractiveWin2DRenderingService();
            await _service.InitializeAsync(width, height);

            // Enable HID streaming if available
            if (_service.IsHidServiceConnected)
            {
                _service.EnableHidTransfer(true, useSuspendMedia: false);
                await _service.EnableHidRealTimeDisplayAsync(true);
            }

            // Start auto rendering for motion updates
            _service.StartAutoRendering(30);
        }

        /// <summary>
        /// Add a simple bouncing ball - most basic motion example
        /// </summary>
        public void AddSimpleBouncingBall()
        {
            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Bounce,
                Speed = 150f,
                Direction = Vector2.Normalize(new Vector2(1, -0.7f)),
                RespectBoundaries = true,
                ShowTrail = true
            };

            _service.AddCircleElementWithMotion(
                new WinFoundation.Point(100, 100), 
                15f, 
                WinUIColor.FromArgb(255, 255, 0, 0), 
                motionConfig);
        }

        /// <summary>
        /// Add rotating text around center - demonstrates circular motion
        /// </summary>
        public void AddRotatingText()
        {
            var center = new WinFoundation.Point(_service.Width / 2.0, _service.Height / 2.0);
            
            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Circular,
                Speed = 1.5f, // radians per second
                Center = new Vector2((float)center.X, (float)center.Y),
                Radius = 100f
            };

            var textConfig = new TextElementConfig
            {
                FontSize = 20,
                TextColor = WinUIColor.FromArgb(255, 0, 255, 255),
                IsDraggable = false
            };

            _service.AddTextElementWithMotion(
                "Rotating Text", 
                center, 
                motionConfig, 
                textConfig);
        }

        /// <summary>
        /// Convert existing static elements to motion elements
        /// </summary>
        public void ConvertToMotionElement(RenderElement existingElement)
        {
            if (existingElement is IMotionElement)
            {
                // Already has motion capabilities
                return;
            }

            // Remove the existing static element
            _service.RemoveElement(existingElement);

            // Add a new motion-enabled version
            switch (existingElement)
            {
                case TextElement textElement:
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

                    _service.AddTextElementWithMotion(textElement.Text, textElement.Position, textMotionConfig, textConfig);
                    break;

                case ShapeElement shapeElement:
                    var shapeMotionConfig = new ElementMotionConfig
                    {
                        MotionType = MotionType.Random,
                        Speed = 80f,
                        RespectBoundaries = true,
                        ShowTrail = true
                    };

                    if (shapeElement.ShapeType == ShapeType.Circle)
                    {
                        var radius = (float)Math.Min(shapeElement.Size.Width, shapeElement.Size.Height) / 2;
                        _service.AddCircleElementWithMotion(shapeElement.Position, radius, shapeElement.FillColor, shapeMotionConfig);
                    }
                    else
                    {
                        _service.AddRectangleElementWithMotion(shapeElement.Position, shapeElement.Size, shapeElement.FillColor, shapeMotionConfig);
                    }
                    break;
            }
        }

        /// <summary>
        /// Add motion to all existing elements
        /// </summary>
        public void AddMotionToAllElements()
        {
            var elements = _service.GetElements();
            var elementsToConvert = new System.Collections.Generic.List<RenderElement>(elements);
            
            foreach (var element in elementsToConvert)
            {
                ConvertToMotionElement(element);
            }
        }

        /// <summary>
        /// Quick demo method - adds several motion elements
        /// </summary>
        public void CreateQuickDemo()
        {
            // Clear existing elements
            _service.ClearElements();

            // Add bouncing balls
            for (int i = 0; i < 3; i++)
            {
                AddSimpleBouncingBall();
            }

            // Add rotating text
            AddRotatingText();

            // Add oscillating shapes
            var colors = new[]
            {
                WinUIColor.FromArgb(255, 255, 255, 0),   // Yellow
                WinUIColor.FromArgb(255, 255, 165, 0),   // Orange
                WinUIColor.FromArgb(255, 0, 255, 255)    // Cyan
            };

            for (int i = 0; i < colors.Length; i++)
            {
                var oscillateConfig = new ElementMotionConfig
                {
                    MotionType = MotionType.Oscillate,
                    Speed = 2.0f + i * 0.5f,
                    Direction = new Vector2(i % 2 == 0 ? 1 : 0, i % 2 == 1 ? 1 : 0), // Alternate horizontal/vertical
                    Center = new Vector2(200 + i * 200, 300),
                    Radius = 50f
                };

                _service.AddRectangleElementWithMotion(
                    new WinFoundation.Point(200 + i * 200, 300),
                    new WinFoundation.Size(30, 30),
                    colors[i],
                    oscillateConfig);
            }
        }

        /// <summary>
        /// Simple motion control methods
        /// </summary>
        public void PauseAllMotion() => _service.PauseAllMotion();
        public void ResumeAllMotion() => _service.ResumeAllMotion();
        public void ClearAll() => _service.ClearElements();

        /// <summary>
        /// Change speed of all motion elements
        /// </summary>
        public void ChangeMotionSpeed(float speedMultiplier)
        {
            var elements = _service.GetElements();
            
            foreach (var element in elements)
            {
                if (element is IMotionElement motionElement)
                {
                    motionElement.MotionConfig.Speed *= speedMultiplier;
                }
            }
        }

        public void Dispose()
        {
            _service?.StopAutoRendering();
            _service?.Dispose();
        }
    }
}

/*
Usage in existing code:

// In your existing page or window that uses InteractiveWin2DRenderingService:

public partial class ExistingPage : Page 
{
    private SimpleMotionIntegration _motionIntegration;

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Replace your existing service initialization with:
        _motionIntegration = new SimpleMotionIntegration();
        await _motionIntegration.InitializeWithMotionAsync(800, 600);
        
        // Add some motion elements
        _motionIntegration.CreateQuickDemo();
    }

    // Add button event handlers:
    private void OnPauseMotion_Click(object sender, RoutedEventArgs e)
    {
        _motionIntegration?.PauseAllMotion();
    }

    private void OnResumeMotion_Click(object sender, RoutedEventArgs e)
    {
        _motionIntegration?.ResumeAllMotion();
    }

    private void OnAddBouncingBall_Click(object sender, RoutedEventArgs e)
    {
        _motionIntegration?.AddSimpleBouncingBall();
    }

    private void OnSpeedUp_Click(object sender, RoutedEventArgs e)
    {
        _motionIntegration?.ChangeMotionSpeed(1.5f); // 50% faster
    }

    private void OnSlowDown_Click(object sender, RoutedEventArgs e)
    {
        _motionIntegration?.ChangeMotionSpeed(0.7f); // 30% slower
    }
}
*/