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
using WinFoundation = Windows.Foundation;
using WinUIColor = Windows.UI.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for DesignerPage.xaml
    /// </summary>
    public partial class DesignerPage : Page
    {
        private InteractiveWin2DRenderingService? _renderService;
        private bool _isDragging;
        private RenderElement? _draggedElement;
        private WinFoundation.Point _lastMousePosition;
        private WinFoundation.Point _dragOffset;
        private int _elementCounter = 0;

        // Element editing state
        private RenderElement? _currentEditingElement;
        private bool _isUpdatingProperties = false;
        private readonly Random _random = new Random();

        public DesignerPage()
        {
            InitializeComponent();
            Loaded += OnDesignerPageLoaded;
            InitializeComboBoxes();
        }

        private async void OnDesignerPageLoaded(object sender, RoutedEventArgs e)
        {
            await InitializeDesignerAsync();
        }

        private void InitializeComboBoxes()
        {
            // Initialize Motion Type ComboBox
            MotionTypeComboBox.Items.Clear();
            foreach (var motionType in Enum.GetValues<MotionType>())
            {
                MotionTypeComboBox.Items.Add(motionType.ToString());
            }

            // Initialize Shape Type ComboBox
            ShapeTypeComboBox.Items.Clear();
            foreach (var shapeType in Enum.GetValues<ShapeType>())
            {
                ShapeTypeComboBox.Items.Add(shapeType.ToString());
            }

            // Set default selections
            FontFamilyComboBox.SelectedIndex = 0; // Segoe UI
            MotionTypeComboBox.SelectedIndex = 0; // None
            ShapeTypeComboBox.SelectedIndex = 0; // Rectangle
        }

        private async Task InitializeDesignerAsync()
        {
            try
            {
                UpdateStatus("Initializing Designer...", false);

                _renderService = new InteractiveWin2DRenderingService();
                await _renderService.InitializeAsync(480, 480); // Fixed 480x480 canvas

                // Subscribe to events
                _renderService.ImageRendered += OnFrameRendered;
                _renderService.ElementSelected += OnElementSelected;
                _renderService.ElementMoved += OnElementMoved;
                _renderService.HidStatusChanged += OnStatusChanged;
                _renderService.RenderingError += OnRenderingError;
                _renderService.BackgroundChanged += OnBackgroundChanged;

                // Set default gradient background
                await _renderService.ResetBackgroundToDefaultAsync();

                UpdateElementsList();
                UpdateStatus("Designer ready - Start adding elements!", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to initialize: {ex.Message}", true);
            }
        }

        #region Event Handlers

        private void OnFrameRendered(WriteableBitmap bitmap)
        {
            if (Dispatcher.CheckAccess())
            {
                CanvasDisplay.Source = bitmap;
                RenderInfoLabel.Text = $"Rendering at {_renderService?.TargetFPS ?? 30} FPS";
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    CanvasDisplay.Source = bitmap;
                    RenderInfoLabel.Text = $"Rendering at {_renderService?.TargetFPS ?? 30} FPS";
                });
            }
        }

        private void OnElementSelected(RenderElement element)
        {
            Dispatcher.Invoke(() =>
            {
                var elements = _renderService?.GetElements();
                if (elements != null)
                {
                    var index = elements.FindIndex(e => e.Id == element.Id);
                    if (index >= 0)
                    {
                        ElementsListBox.SelectedIndex = index;
                    }
                }

                UpdateElementProperties(element);
            });
        }

        private void OnElementMoved(RenderElement element)
        {
            // Update position fields if this is the currently selected element
            if (_currentEditingElement == element)
            {
                UpdateElementPositionFields(element);
            }
        }

        private void OnStatusChanged(string status)
        {
            Dispatcher.Invoke(() => UpdateStatus(status, false));
        }

        private void OnRenderingError(Exception ex)
        {
            Dispatcher.Invoke(() => UpdateStatus($"Rendering error: {ex.Message}", true));
        }

        private void OnBackgroundChanged(string backgroundInfo)
        {
            Dispatcher.Invoke(() => UpdateStatus($"Background: {backgroundInfo}", false));
        }

        #endregion

        #region Canvas Controls

        private async void StartRenderingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            try
            {

                // Enable HID transfer and start auto-rendering with built-in render tick
                await _renderService.EnableHidTransfer(true, useSuspendMedia: false);

                // Enable real-time display mode on HID devices
                await _renderService.EnableHidRealTimeDisplayAsync(true);

                // Start auto-rendering with built-in render tick
                _renderService.StartAutoRendering(_renderService.TargetFPS);
                StartRenderingButton.IsEnabled = false;
                StopRenderingButton.IsEnabled = true;
                UpdateStatus("Rendering started", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to start rendering: {ex.Message}", true);
            }
        }

        private async void StopRenderingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            try
            {
                _renderService.StopAutoRendering();
                await _renderService.EnableHidTransfer(false);
                await _renderService.EnableHidRealTimeDisplayAsync(false);
                StartRenderingButton.IsEnabled = true;
                StopRenderingButton.IsEnabled = false;
                UpdateStatus("Rendering stopped", false);
                RenderInfoLabel.Text = "Rendering stopped";
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to stop rendering: {ex.Message}", true);
            }
        }

        private void ClearCanvasButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            _renderService.ClearElements();
            UpdateElementsList();
            ClearElementProperties();
            _elementCounter = 0;
            UpdateStatus("Canvas cleared", false);
        }

        private void FpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var fps = (int)e.NewValue;
            if (FpsLabel != null) FpsLabel.Text = fps.ToString();
            if (FpsDisplayLabel != null) FpsDisplayLabel.Text = $"FPS: {fps}";

            if (_renderService != null)
            {
                _renderService.TargetFPS = fps;
                if (_renderService.IsAutoRenderingEnabled)
                {
                    _renderService.SetAutoRenderingFPS(fps);
                }
            }
        }

        #endregion

        #region Add Elements

        private void AddTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var text = string.IsNullOrWhiteSpace(NewTextContent.Text) ? "Sample Text" : NewTextContent.Text;
            var position = new WinFoundation.Point(50 + (_elementCounter * 20) % 300, 50 + (_elementCounter * 20) % 300);

            var textElement = new TextElement($"Text_{++_elementCounter}")
            {
                Text = text,
                Position = position,
                Size = new WinFoundation.Size(200, 40),
                FontSize = 24,
                TextColor = WinUIColor.FromArgb(255, 255, 255, 255),
                IsDraggable = true,
                IsVisible = true
            };

            _renderService.AddElement(textElement);
            UpdateElementsList();
            UpdateStatus($"Added text element: {text}", false);
        }

        private async void AddImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var openDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Title = "Select Image"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    var imageKey = await _renderService.LoadImageAsync(openDialog.FileName);
                    var position = new WinFoundation.Point(100 + (_elementCounter * 25) % 250, 100 + (_elementCounter * 25) % 250);

                    var imageElement = new ImageElement($"Image_{++_elementCounter}")
                    {
                        ImagePath = imageKey,
                        Position = position,
                        Size = new WinFoundation.Size(80, 80),
                        Scale = 1.0f,
                        IsDraggable = true,
                        IsVisible = true
                    };

                    _renderService.AddElement(imageElement);
                    UpdateElementsList();
                    UpdateStatus($"Added image element: {Path.GetFileName(openDialog.FileName)}", false);
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Failed to add image: {ex.Message}", true);
                }
            }
        }

        private void AddShapeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var position = new WinFoundation.Point(150 + (_elementCounter * 30) % 200, 150 + (_elementCounter * 30) % 200);
            var colors = new[]
            {
                WinUIColor.FromArgb(255, 255, 100, 100), // Light Red
                WinUIColor.FromArgb(255, 100, 255, 100), // Light Green
                WinUIColor.FromArgb(255, 100, 100, 255), // Light Blue
                WinUIColor.FromArgb(255, 255, 255, 100), // Yellow
                WinUIColor.FromArgb(255, 255, 100, 255), // Magenta
                WinUIColor.FromArgb(255, 100, 255, 255), // Cyan
            };

            var shapeElement = new ShapeElement($"Shape_{++_elementCounter}")
            {
                ShapeType = ShapeType.Circle,
                Position = position,
                Size = new WinFoundation.Size(60, 60),
                FillColor = colors[_elementCounter % colors.Length],
                StrokeColor = WinUIColor.FromArgb(255, 255, 255, 255),
                StrokeWidth = 2,
                IsDraggable = true,
                IsVisible = true
            };

            _renderService.AddElement(shapeElement);
            UpdateElementsList();
            UpdateStatus($"Added shape element", false);
        }

        #endregion

        #region Motion Elements

        private void AddBouncingBallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var position = new WinFoundation.Point(_random.Next(50, 400), _random.Next(50, 400));
            var colors = new[]
            {
                WinUIColor.FromArgb(255, 255, 0, 0),   // Red
                WinUIColor.FromArgb(255, 0, 255, 0),   // Green
                WinUIColor.FromArgb(255, 0, 0, 255),   // Blue
                WinUIColor.FromArgb(255, 255, 165, 0), // Orange
            };

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Bounce,
                Speed = 100f + _random.Next(0, 50),
                Direction = GetRandomDirection(),
                RespectBoundaries = true,
                ShowTrail = true,
                TrailLength = 15
            };

            var elementIndex = _renderService.AddCircleElementWithMotion(
                position, 12f, colors[_random.Next(colors.Length)], motionConfig);

            if (elementIndex >= 0)
            {
                UpdateElementsList();
                UpdateStatus($"Added bouncing ball", false);
            }
        }

        private void AddRotatingTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var text = string.IsNullOrWhiteSpace(NewTextContent.Text) ? "Rotating Text" : NewTextContent.Text;
            var center = new WinFoundation.Point(240, 240); // Canvas center

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Circular,
                Speed = 1.0f,
                Center = new Vector2((float)center.X, (float)center.Y),
                Radius = 80f,
                RespectBoundaries = false,
                ShowTrail = false
            };

            var textConfig = new TextElementConfig
            {
                FontSize = 16,
                TextColor = WinUIColor.FromArgb(255, 0, 255, 255),
                IsDraggable = false
            };

            var elementIndex = _renderService.AddTextElementWithMotion(text, center, motionConfig, textConfig);

            if (elementIndex >= 0)
            {
                UpdateElementsList();
                UpdateStatus($"Added rotating text", false);
            }
        }

        private void AddOscillatingShapeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var position = new WinFoundation.Point(_random.Next(100, 350), _random.Next(100, 350));

            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Oscillate,
                Speed = 2.0f,
                Direction = Vector2.UnitX,
                Center = new Vector2((float)position.X, (float)position.Y),
                Radius = 40f,
                RespectBoundaries = false,
                ShowTrail = false
            };

            var color = WinUIColor.FromArgb(255, 255, 255, 0); // Yellow
            var size = new WinFoundation.Size(30, 30);

            var elementIndex = _renderService.AddRectangleElementWithMotion(position, size, color, motionConfig);

            if (elementIndex >= 0)
            {
                UpdateElementsList();
                UpdateStatus($"Added oscillating shape", false);
            }
        }

        private void ConvertToMotionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null || ElementsListBox.SelectedIndex < 0) return;

            var elements = _renderService.GetElements();
            if (ElementsListBox.SelectedIndex >= elements.Count) return;

            var selectedElement = elements[ElementsListBox.SelectedIndex];

            if (selectedElement is IMotionElement)
            {
                UpdateStatus("Element already has motion", false);
                return;
            }

            // Create a simple motion configuration
            var motionConfig = new ElementMotionConfig
            {
                MotionType = MotionType.Oscillate,
                Speed = 1.5f,
                Direction = Vector2.UnitX,
                Center = new Vector2((float)selectedElement.Position.X, (float)selectedElement.Position.Y),
                Radius = 30f
            };

            _renderService.SetElementMotion(selectedElement, motionConfig);
            UpdateElementsList();
            UpdateStatus("Converted element to motion", false);
        }

        private void PauseAllMotionButton_Click(object sender, RoutedEventArgs e)
        {
            _renderService?.PauseAllMotion();
            UpdateStatus("All motion paused", false);
        }

        private void ResumeAllMotionButton_Click(object sender, RoutedEventArgs e)
        {
            _renderService?.ResumeAllMotion();
            UpdateStatus("All motion resumed", false);
        }

        #endregion

        #region Background Controls

        private async void SetSolidColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var colors = new[]
            {
                WinUIColor.FromArgb(255, 64, 64, 64),    // Dark Gray
                WinUIColor.FromArgb(255, 0, 64, 128),    // Dark Blue
                WinUIColor.FromArgb(255, 64, 0, 64),     // Dark Purple
                WinUIColor.FromArgb(255, 0, 64, 0),      // Dark Green
                WinUIColor.FromArgb(255, 64, 32, 0),     // Dark Brown
            };

            var randomColor = colors[_random.Next(colors.Length)];
            await _renderService.SetBackgroundColorAsync(randomColor, (float)BackgroundOpacitySlider.Value);
        }

        private async void SetGradientButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var gradients = new[]
            {
                (WinUIColor.FromArgb(255, 64, 0, 128), WinUIColor.FromArgb(255, 0, 64, 128)),   // Purple to Blue
                (WinUIColor.FromArgb(255, 128, 64, 0), WinUIColor.FromArgb(255, 255, 128, 0)),  // Brown to Orange
                (WinUIColor.FromArgb(255, 0, 64, 0), WinUIColor.FromArgb(255, 0, 128, 64)),     // Dark Green to Green
                (WinUIColor.FromArgb(255, 64, 64, 64), WinUIColor.FromArgb(255, 32, 32, 32)),   // Gray gradient
            };

            var (startColor, endColor) = gradients[_random.Next(gradients.Length)];
            await _renderService.SetBackgroundGradientAsync(startColor, endColor, 
                BackgroundGradientDirection.TopToBottom, (float)BackgroundOpacitySlider.Value);
        }

        private async void LoadBackgroundImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var openDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Title = "Select Background Image"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    await _renderService.SetBackgroundImageAsync(openDialog.FileName, 
                        BackgroundScaleMode.UniformToFill, (float)BackgroundOpacitySlider.Value);
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Failed to load background image: {ex.Message}", true);
                }
            }
        }

        private async void ClearBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;
            await _renderService.ClearBackgroundAsync();
        }

        private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var opacity = e.NewValue;
            if (BackgroundOpacityLabel != null) BackgroundOpacityLabel.Text = opacity.ToString("F2");

            if (_renderService != null)
            {
                _renderService.SetBackgroundOpacity((float)opacity);
            }
        }

        #endregion

        #region Mouse Interaction

        private void CanvasDisplay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_renderService == null) return;

            var position = e.GetPosition(CanvasDisplay);
            var scaledPosition = ScalePointToCanvas(position);

            var hitElement = _renderService.HitTest(scaledPosition);
            if (hitElement != null)
            {
                _renderService.SelectElement(hitElement);
                _isDragging = true;
                _draggedElement = hitElement;
                _lastMousePosition = scaledPosition;
                _dragOffset = new WinFoundation.Point(
                    scaledPosition.X - hitElement.Position.X,
                    scaledPosition.Y - hitElement.Position.Y
                );
                CanvasDisplay.CaptureMouse();
            }
            else
            {
                _renderService.SelectElement(null);
                ClearElementProperties();
            }

            UpdateMousePosition(position);
        }

        private void CanvasDisplay_MouseMove(object sender, MouseEventArgs e)
        {
            var position = e.GetPosition(CanvasDisplay);
            UpdateMousePosition(position);

            if (_isDragging && _draggedElement != null && _renderService != null)
            {
                var scaledPosition = ScalePointToCanvas(position);
                var newPosition = new WinFoundation.Point(
                    Math.Max(0, Math.Min(480, scaledPosition.X - _dragOffset.X)),
                    Math.Max(0, Math.Min(480, scaledPosition.Y - _dragOffset.Y))
                );

                _renderService.MoveElement(_draggedElement, newPosition);
            }
        }

        private void CanvasDisplay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                _draggedElement = null;
                CanvasDisplay.ReleaseMouseCapture();
            }
        }

        private void CanvasDisplay_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_renderService == null) return;

            var position = e.GetPosition(CanvasDisplay);
            var scaledPosition = ScalePointToCanvas(position);

            var hitElement = _renderService.HitTest(scaledPosition);
            _renderService.SelectElement(hitElement);
        }

        private WinFoundation.Point ScalePointToCanvas(Point displayPoint)
        {
            // Since we're using a fixed 480x480 canvas and the Image control is also 480x480,
            // the scaling should be 1:1
            return new WinFoundation.Point(displayPoint.X, displayPoint.Y);
        }

        private void UpdateMousePosition(Point position)
        {
            if (MousePositionLabel != null)
            {
                MousePositionLabel.Text = $"Mouse: ({position.X:F0}, {position.Y:F0})";
            }
        }

        #endregion

        #region Element Management

        private void ElementsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ElementsListBox.SelectedIndex >= 0 && _renderService != null)
            {
                var elements = _renderService.GetElements();
                if (ElementsListBox.SelectedIndex < elements.Count)
                {
                    var element = elements[ElementsListBox.SelectedIndex];
                    _renderService.SelectElement(element);
                    DeleteElementButton.IsEnabled = true;
                }
            }
            else
            {
                _renderService?.SelectElement(null);
                ClearElementProperties();
                DeleteElementButton.IsEnabled = false;
            }
        }

        private void DeleteElementButton_Click(object sender, RoutedEventArgs e)
        {
            if (ElementsListBox.SelectedIndex >= 0 && _renderService != null)
            {
                var elements = _renderService.GetElements();
                if (ElementsListBox.SelectedIndex < elements.Count)
                {
                    var element = elements[ElementsListBox.SelectedIndex];
                    _renderService.RemoveElement(element);
                    UpdateElementsList();
                    ClearElementProperties();
                    UpdateStatus("Element deleted", false);
                }
            }
        }

        private void UpdateElementsList()
        {
            if (_renderService == null || ElementsListBox == null) return;

            var elements = _renderService.GetElements();
            ElementsListBox.ItemsSource = elements.Select(e =>
            {
                var motionInfo = e is IMotionElement motionElement ? $" [{motionElement.MotionConfig.MotionType}]" : "";
                return $"{e.Name} ({e.Type}){motionInfo}";
            }).ToList();

            ElementCountLabel.Text = $"Elements: {elements.Count}";
        }

        #endregion

        #region Element Properties

        private void UpdateElementProperties(RenderElement? element)
        {
            _isUpdatingProperties = true;
            _currentEditingElement = element;

            try
            {
                if (element == null)
                {
                    ClearElementProperties();
                    return;
                }

                ShowElementProperties();

                // Update element info
                ElementNameText.Text = element.Name;
                ElementTypeText.Text = $"Type: {element.Type} | ID: {element.Id.ToString()[..8]}...";

                // Update common properties
                PositionXTextBox.Text = element.Position.X.ToString("F1");
                PositionYTextBox.Text = element.Position.Y.ToString("F1");
                VisibleCheckBox.IsChecked = element.IsVisible;
                DraggableCheckBox.IsChecked = element.IsDraggable;

                // Hide all specific property panels
                TextPropertiesPanel.Visibility = Visibility.Collapsed;
                ImagePropertiesPanel.Visibility = Visibility.Collapsed;
                ShapePropertiesPanel.Visibility = Visibility.Collapsed;
                MotionPropertiesPanel.Visibility = Visibility.Collapsed;

                // Show element-specific properties
                switch (element)
                {
                    case TextElement textElement:
                        UpdateTextElementProperties(textElement);
                        break;
                    case ImageElement imageElement:
                        UpdateImageElementProperties(imageElement);
                        break;
                    case ShapeElement shapeElement:
                        UpdateShapeElementProperties(shapeElement);
                        break;
                }

                // Show motion properties if applicable
                if (element is IMotionElement motionElement)
                {
                    UpdateMotionElementProperties(motionElement);
                }
            }
            finally
            {
                _isUpdatingProperties = false;
            }
        }

        private void ClearElementProperties()
        {
            NoSelectionText.Visibility = Visibility.Visible;
            CommonPropertiesPanel.Visibility = Visibility.Collapsed;
            TextPropertiesPanel.Visibility = Visibility.Collapsed;
            ImagePropertiesPanel.Visibility = Visibility.Collapsed;
            ShapePropertiesPanel.Visibility = Visibility.Collapsed;
            MotionPropertiesPanel.Visibility = Visibility.Collapsed;
            PropertyButtonsPanel.Visibility = Visibility.Collapsed;
            DeleteElementButton.IsEnabled = false;
        }

        private void ShowElementProperties()
        {
            NoSelectionText.Visibility = Visibility.Collapsed;
            CommonPropertiesPanel.Visibility = Visibility.Visible;
            PropertyButtonsPanel.Visibility = Visibility.Visible;
        }

        private void UpdateTextElementProperties(TextElement element)
        {
            TextPropertiesPanel.Visibility = Visibility.Visible;
            TextContentTextBox.Text = element.Text;
            FontSizeTextBox.Text = element.FontSize.ToString();
            
            // Set font family
            var fontFamily = element.FontFamily;
            for (int i = 0; i < FontFamilyComboBox.Items.Count; i++)
            {
                if (((ComboBoxItem)FontFamilyComboBox.Items[i]).Content.ToString() == fontFamily)
                {
                    FontFamilyComboBox.SelectedIndex = i;
                    break;
                }
            }

            // Set color
            ColorRTextBox.Text = element.TextColor.R.ToString();
            ColorGTextBox.Text = element.TextColor.G.ToString();
            ColorBTextBox.Text = element.TextColor.B.ToString();
        }

        private void UpdateImageElementProperties(ImageElement element)
        {
            ImagePropertiesPanel.Visibility = Visibility.Visible;
            ImageScaleTextBox.Text = element.Scale.ToString("F2");
            ImageRotationTextBox.Text = element.Rotation.ToString("F1");
        }

        private void UpdateShapeElementProperties(ShapeElement element)
        {
            ShapePropertiesPanel.Visibility = Visibility.Visible;
            
            // Set shape type
            ShapeTypeComboBox.SelectedIndex = (int)element.ShapeType;
            
            ShapeWidthTextBox.Text = element.Size.Width.ToString("F1");
            ShapeHeightTextBox.Text = element.Size.Height.ToString("F1");
            
            // Set fill color
            FillColorRTextBox.Text = element.FillColor.R.ToString();
            FillColorGTextBox.Text = element.FillColor.G.ToString();
            FillColorBTextBox.Text = element.FillColor.B.ToString();
        }

        private void UpdateMotionElementProperties(IMotionElement element)
        {
            MotionPropertiesPanel.Visibility = Visibility.Visible;
            
            MotionTypeComboBox.SelectedItem = element.MotionConfig.MotionType.ToString();
            MotionSpeedTextBox.Text = element.MotionConfig.Speed.ToString("F1");
            ShowTrailCheckBox.IsChecked = element.MotionConfig.ShowTrail;
            MotionPausedCheckBox.IsChecked = element.MotionConfig.IsPaused;
        }

        private void UpdateElementPositionFields(RenderElement element)
        {
            if (_isUpdatingProperties || _currentEditingElement != element) return;

            _isUpdatingProperties = true;
            try
            {
                PositionXTextBox.Text = element.Position.X.ToString("F1");
                PositionYTextBox.Text = element.Position.Y.ToString("F1");
            }
            finally
            {
                _isUpdatingProperties = false;
            }
        }

        private void ElementProperty_Changed(object sender, RoutedEventArgs e)
        {
            // Properties are staged but not applied until Apply is clicked
            // This provides better user control
        }

        private void ApplyChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditingElement == null || _renderService == null)
            {
                UpdateStatus("No element selected for editing", true);
                return;
            }

            try
            {
                var properties = GatherElementPropertiesFromUI();
                _renderService.UpdateElementProperties(_currentEditingElement, properties);
                
                UpdateStatus("Element changes applied", false);
                UpdateElementsList();
                UpdateElementProperties(_currentEditingElement); // Refresh with updated values
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error applying changes: {ex.Message}", true);
            }
        }

        private void ResetChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditingElement == null)
            {
                UpdateStatus("No element selected for reset", true);
                return;
            }

            UpdateElementProperties(_currentEditingElement);
            UpdateStatus("Properties reset to current values", false);
        }

        private Dictionary<string, object> GatherElementPropertiesFromUI()
        {
            var properties = new Dictionary<string, object>();

            try
            {
                // Common properties
                if (double.TryParse(PositionXTextBox.Text, out var posX) &&
                    double.TryParse(PositionYTextBox.Text, out var posY))
                {
                    properties["Position"] = new WinFoundation.Point(posX, posY);
                }

                properties["IsVisible"] = VisibleCheckBox.IsChecked ?? true;
                properties["IsDraggable"] = DraggableCheckBox.IsChecked ?? true;

                // Element-specific properties
                if (_currentEditingElement != null)
                {
                    switch (_currentEditingElement)
                    {
                        case TextElement:
                            GatherTextElementProperties(properties);
                            break;
                        case ImageElement:
                            GatherImageElementProperties(properties);
                            break;
                        case ShapeElement:
                            GatherShapeElementProperties(properties);
                            break;
                    }

                    // Motion properties
                    if (_currentEditingElement is IMotionElement)
                    {
                        GatherMotionElementProperties(properties);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error gathering properties: {ex.Message}", true);
            }

            return properties;
        }

        private void GatherTextElementProperties(Dictionary<string, object> properties)
        {
            properties["Text"] = TextContentTextBox.Text ?? "";

            if (float.TryParse(FontSizeTextBox.Text, out var fontSize))
                properties["FontSize"] = fontSize;

            if (FontFamilyComboBox.SelectedItem is ComboBoxItem selectedFont)
                properties["FontFamily"] = selectedFont.Content.ToString();

            if (byte.TryParse(ColorRTextBox.Text, out var r) &&
                byte.TryParse(ColorGTextBox.Text, out var g) &&
                byte.TryParse(ColorBTextBox.Text, out var b))
            {
                properties["TextColor"] = WinUIColor.FromArgb(255, r, g, b);
            }
        }

        private void GatherImageElementProperties(Dictionary<string, object> properties)
        {
            if (float.TryParse(ImageScaleTextBox.Text, out var scale))
                properties["Scale"] = scale;

            if (float.TryParse(ImageRotationTextBox.Text, out var rotation))
                properties["Rotation"] = rotation;
        }

        private void GatherShapeElementProperties(Dictionary<string, object> properties)
        {
            if (ShapeTypeComboBox.SelectedIndex >= 0)
                properties["ShapeType"] = (ShapeType)ShapeTypeComboBox.SelectedIndex;

            if (double.TryParse(ShapeWidthTextBox.Text, out var width) &&
                double.TryParse(ShapeHeightTextBox.Text, out var height))
            {
                properties["Size"] = new WinFoundation.Size(width, height);
            }

            if (byte.TryParse(FillColorRTextBox.Text, out var r) &&
                byte.TryParse(FillColorGTextBox.Text, out var g) &&
                byte.TryParse(FillColorBTextBox.Text, out var b))
            {
                properties["FillColor"] = WinUIColor.FromArgb(255, r, g, b);
            }
        }

        private void GatherMotionElementProperties(Dictionary<string, object> properties)
        {
            if (MotionTypeComboBox.SelectedItem is string motionTypeStr &&
                Enum.TryParse<MotionType>(motionTypeStr, out var motionType))
                properties["MotionType"] = motionType;

            if (float.TryParse(MotionSpeedTextBox.Text, out var speed))
                properties["Speed"] = speed;

            properties["ShowTrail"] = ShowTrailCheckBox.IsChecked ?? false;
            properties["IsPaused"] = MotionPausedCheckBox.IsChecked ?? false;
        }

        #endregion

        #region File Operations

        private async void SaveImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var saveDialog = new SaveFileDialog
            {
                Filter = "PNG Files (*.png)|*.png|JPEG Files (*.jpg)|*.jpg",
                DefaultExt = "png",
                FileName = $"design_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    await _renderService.SaveRenderedImageAsync(saveDialog.FileName);
                    UpdateStatus($"Image saved: {Path.GetFileName(saveDialog.FileName)}", false);
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Save failed: {ex.Message}", true);
                }
            }
        }

        private async void ExportSceneButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var saveDialog = new SaveFileDialog
            {
                Filter = "JSON Scene Files (*.json)|*.json",
                DefaultExt = "json",
                FileName = $"scene_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var sceneData = await _renderService.ExportSceneToJsonAsync();
                    await File.WriteAllTextAsync(saveDialog.FileName, sceneData);
                    UpdateStatus($"Scene exported: {Path.GetFileName(saveDialog.FileName)}", false);
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Export failed: {ex.Message}", true);
                }
            }
        }

        private async void ImportSceneButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var openDialog = new OpenFileDialog
            {
                Filter = "JSON Scene Files (*.json)|*.json",
                Title = "Select Scene File"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    if (File.Exists(openDialog.FileName))
                    {
                        var jsonData = await File.ReadAllTextAsync(openDialog.FileName);
                        var success = await _renderService.ImportSceneFromJsonAsync(jsonData);

                        if (success)
                        {
                            UpdateElementsList();
                            ClearElementProperties();
                            UpdateStatus($"Scene imported: {Path.GetFileName(openDialog.FileName)}", false);
                        }
                        else
                        {
                            UpdateStatus("Failed to import scene", true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Import failed: {ex.Message}", true);
                }
            }
        }

        #endregion

        #region Helper Methods

        private Vector2 GetRandomDirection()
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            return new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        private void UpdateStatus(string message, bool isError)
        {
            if (StatusLabel != null)
            {
                StatusLabel.Text = message;
                StatusLabel.Foreground = isError ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGreen);
            }
        }

        #endregion

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _renderService?.StopAutoRendering();
                _renderService?.Dispose();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Cleanup error: {ex.Message}", true);
            }
        }
    }
}
