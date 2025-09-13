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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinFoundation = Windows.Foundation;
using WinUIColor = Windows.UI.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfColor = System.Windows.Media.Color;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using Path = System.IO.Path;
using System.Windows.Threading;

namespace CMDevicesManager.Pages
{
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

        // Real-time property update features
        private bool _isRealTimeUpdateEnabled = true;
        private DispatcherTimer? _textUpdateDebounceTimer;
        private readonly TimeSpan _textUpdateDelay = TimeSpan.FromMilliseconds(500); // 500ms debounce for text input
        private bool _enableRealTimePreview = true;

        // Color selection state
        private WinUIColor _selectedColor = WinUIColor.FromArgb(255, 255, 100, 100);
        private bool _isCustomColorPickerOpen = false;

        // Quick colors palette
        private readonly WinUIColor[] _quickColors = new[]
        {
            WinUIColor.FromArgb(255, 255, 100, 100), // Light Red
            WinUIColor.FromArgb(255, 100, 255, 100), // Light Green  
            WinUIColor.FromArgb(255, 100, 100, 255), // Light Blue
            WinUIColor.FromArgb(255, 255, 255, 100), // Yellow
            WinUIColor.FromArgb(255, 255, 100, 255), // Magenta
            WinUIColor.FromArgb(255, 100, 255, 255), // Cyan
            WinUIColor.FromArgb(255, 255, 165, 0),   // Orange
            WinUIColor.FromArgb(255, 128, 0, 128),   // Purple
            WinUIColor.FromArgb(255, 255, 255, 255), // White
            WinUIColor.FromArgb(255, 128, 128, 128), // Gray
            WinUIColor.FromArgb(255, 64, 64, 64),    // Dark Gray
            WinUIColor.FromArgb(255, 0, 0, 0),       // Black
        };

        public DesignerPage()
        {
            InitializeComponent();
            Loaded += OnDesignerPageLoaded;
            InitializeUI();
            InitializeRealTimeUpdateSystem();
        }

        private void InitializeUI()
        {
            InitializeComboBoxes();
            InitializeColorPalette();
            InitializeSliderEventHandlers();
        }

        #region Real-Time Update System

        /// <summary>
        /// Initialize the real-time property update system
        /// </summary>
        private void InitializeRealTimeUpdateSystem()
        {
            // Initialize debounce timer for text input
            _textUpdateDebounceTimer = new DispatcherTimer
            {
                Interval = _textUpdateDelay
            };
            _textUpdateDebounceTimer.Tick += OnTextUpdateDebounceTimerTick;

            // Set up real-time event handlers for all property controls
            SetupRealTimeEventHandlers();
        }

        /// <summary>
        /// Set up event handlers for real-time property updates
        /// </summary>
        private void SetupRealTimeEventHandlers()
        {
            // This will be called after InitializeComponent() when all controls are available
            Loaded += (s, e) => AttachRealTimeEventHandlers();
        }

        /// <summary>
        /// Attach real-time event handlers to all property controls
        /// </summary>
        private void AttachRealTimeEventHandlers()
        {
            try
            {
                // Real-time updates toggle - safely try to find and attach
                var realTimeCheckBox = this.FindName("RealTimeUpdatesCheckBox") as System.Windows.Controls.CheckBox;
                if (realTimeCheckBox != null)
                {
                    realTimeCheckBox.Checked += RealTimeUpdatesCheckBox_Changed;
                    realTimeCheckBox.Unchecked += RealTimeUpdatesCheckBox_Changed;
                }

                // Text content with debouncing
                if (TextContentTextBox != null)
                {
                    TextContentTextBox.TextChanged += TextProperty_Changed;
                }

                // Position text boxes with debouncing
                if (PositionXTextBox != null)
                {
                    PositionXTextBox.TextChanged += PositionProperty_Changed;
                }
                if (PositionYTextBox != null)
                {
                    PositionYTextBox.TextChanged += PositionProperty_Changed;
                }

                // Font family combo box
                if (FontFamilyComboBox != null)
                {
                    FontFamilyComboBox.SelectionChanged += FontFamilyProperty_Changed;
                }

                // Shape type combo box
                if (ShapeTypeComboBox != null)
                {
                    ShapeTypeComboBox.SelectionChanged += ShapeTypeProperty_Changed;
                }

                // Motion type combo box
                if (MotionTypeComboBox != null)
                {
                    MotionTypeComboBox.SelectionChanged += MotionTypeProperty_Changed;
                }

                // Checkboxes
                if (VisibleCheckBox != null)
                {
                    VisibleCheckBox.Checked += CheckboxProperty_Changed;
                    VisibleCheckBox.Unchecked += CheckboxProperty_Changed;
                }
                if (DraggableCheckBox != null)
                {
                    DraggableCheckBox.Checked += CheckboxProperty_Changed;
                    DraggableCheckBox.Unchecked += CheckboxProperty_Changed;
                }
                if (ShowTrailCheckBox != null)
                {
                    ShowTrailCheckBox.Checked += CheckboxProperty_Changed;
                    ShowTrailCheckBox.Unchecked += CheckboxProperty_Changed;
                }
                if (MotionPausedCheckBox != null)
                {
                    MotionPausedCheckBox.Checked += CheckboxProperty_Changed;
                    MotionPausedCheckBox.Unchecked += CheckboxProperty_Changed;
                }

                UpdateStatus("Real-time property updates initialized", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to initialize real-time updates: {ex.Message}", true);
            }
        }

        /// <summary>
        /// Toggle real-time property updates on/off
        /// </summary>
        private void ToggleRealTimeUpdates()
        {
            _isRealTimeUpdateEnabled = !_isRealTimeUpdateEnabled;
            
            var status = _isRealTimeUpdateEnabled ? "enabled" : "disabled";
            UpdateStatus($"Real-time updates {status}", false);
            
            // Update checkbox if it exists
            var realTimeCheckBox = this.FindName("RealTimeUpdatesCheckBox") as System.Windows.Controls.CheckBox;
            if (realTimeCheckBox != null && realTimeCheckBox.IsChecked != _isRealTimeUpdateEnabled)
            {
                realTimeCheckBox.IsChecked = _isRealTimeUpdateEnabled;
            }

            // Update elements list to show RT indicator
            UpdateElementsList();
        }

        /// <summary>
        /// Apply real-time property updates immediately (for sliders, checkboxes, etc.)
        /// </summary>
        private void ApplyRealTimePropertyUpdate()
        {
            if (!_isRealTimeUpdateEnabled || _isUpdatingProperties || _currentEditingElement == null || _renderService == null)
                return;

            try
            {
                var properties = GatherElementPropertiesFromUI();
                _renderService.UpdateElementProperties(_currentEditingElement, properties);
                
                // Optional: Show brief feedback
                if (_enableRealTimePreview)
                {
                    UpdateStatus("Properties updated", false);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Real-time update error: {ex.Message}", true);
            }
        }

        /// <summary>
        /// Apply real-time property updates with debouncing (for text input)
        /// </summary>
        private void ApplyDebouncedPropertyUpdate()
        {
            if (!_isRealTimeUpdateEnabled)
                return;

            // Reset and restart the debounce timer
            _textUpdateDebounceTimer?.Stop();
            _textUpdateDebounceTimer?.Start();
        }

        /// <summary>
        /// Handle debounced text property updates
        /// </summary>
        private void OnTextUpdateDebounceTimerTick(object? sender, EventArgs e)
        {
            _textUpdateDebounceTimer?.Stop();
            ApplyRealTimePropertyUpdate();
        }

        #endregion

        #region Real-Time Event Handlers

        /// <summary>
        /// Handle text property changes with debouncing
        /// </summary>
        private void TextProperty_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            ApplyDebouncedPropertyUpdate();
        }

        /// <summary>
        /// Handle position property changes with debouncing
        /// </summary>
        private void PositionProperty_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            ApplyDebouncedPropertyUpdate();
        }

        /// <summary>
        /// Handle font family property changes immediately
        /// </summary>
        private void FontFamilyProperty_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            ApplyRealTimePropertyUpdate();
        }

        /// <summary>
        /// Handle shape type property changes immediately
        /// </summary>
        private void ShapeTypeProperty_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            ApplyRealTimePropertyUpdate();
        }

        /// <summary>
        /// Handle motion type property changes immediately
        /// </summary>
        private void MotionTypeProperty_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            ApplyRealTimePropertyUpdate();
        }

        /// <summary>
        /// Handle checkbox property changes immediately
        /// </summary>
        private void CheckboxProperty_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            ApplyRealTimePropertyUpdate();
        }

        /// <summary>
        /// Handle real-time updates checkbox changes
        /// </summary>
        private void RealTimeUpdatesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var realTimeCheckBox = sender as System.Windows.Controls.CheckBox;
            if (realTimeCheckBox != null)
            {
                _isRealTimeUpdateEnabled = realTimeCheckBox.IsChecked == true;
                var status = _isRealTimeUpdateEnabled ? "enabled" : "disabled";
                UpdateStatus($"Real-time updates {status}", false);
                
                // Update elements list to show RT indicator
                UpdateElementsList();
                
                // Update element properties display to show current mode
                if (_currentEditingElement != null)
                {
                    UpdateElementProperties(_currentEditingElement);
                }
            }
        }

        #endregion

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

        private void InitializeColorPalette()
        {
            if (ColorPalette == null) return;
            
            ColorPalette.Children.Clear();
            
            foreach (var color in _quickColors)
            {
                var colorRect = new WpfRectangle
                {
                    Width = 20,
                    Height = 20,
                    Fill = new SolidColorBrush(WpfColor.FromArgb(color.A, color.R, color.G, color.B)),
                    Stroke = System.Windows.Media.Brushes.Gray,
                    StrokeThickness = 1,
                    Margin = new Thickness(2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = color
                };

                colorRect.MouseLeftButtonDown += (s, e) =>
                {
                    if (s is WpfRectangle rect && rect.Tag is WinUIColor selectedColor)
                    {
                        _selectedColor = selectedColor;
                        UpdateColorPreviewsWithCurrentColor();
                    }
                };

                ColorPalette.Children.Add(colorRect);
            }
        }

        private void InitializeSliderEventHandlers()
        {
            // Default element size slider
            if (DefaultElementSizeSlider != null)
            {
                DefaultElementSizeSlider.ValueChanged += (s, e) =>
                {
                    if (DefaultElementSizeLabel != null)
                        DefaultElementSizeLabel.Text = ((int)e.NewValue).ToString();
                };
            }
        }

        private async void OnDesignerPageLoaded(object sender, RoutedEventArgs e)
        {
            await InitializeDesignerAsync();
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
                UpdateColorPreviewsWithCurrentColor();
                UpdateStatus("Designer ready - Real-time updates enabled!", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to initialize: {ex.Message}", true);
            }
        }

        #region Color Management

        private void UpdateColorPreviewsWithCurrentColor()
        {
            var wpfColor = WpfColor.FromArgb(_selectedColor.A, _selectedColor.R, _selectedColor.G, _selectedColor.B);
            var brush = new SolidColorBrush(wpfColor);

            if (TextColorPreview != null)
                TextColorPreview.Fill = brush;
            if (ShapeColorPreview != null)
                ShapeColorPreview.Fill = brush;
        }

        private void CustomColorButton_Click(object sender, RoutedEventArgs e)
        {
            _isCustomColorPickerOpen = true;
            if (ColorPickerOverlay != null)
                ColorPickerOverlay.Visibility = Visibility.Visible;
            
            // Set sliders to current selected color
            if (CustomColorRSlider != null) CustomColorRSlider.Value = _selectedColor.R;
            if (CustomColorGSlider != null) CustomColorGSlider.Value = _selectedColor.G;
            if (CustomColorBSlider != null) CustomColorBSlider.Value = _selectedColor.B;
            
            UpdateCustomColorPreview();
        }

        private void CustomColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateCustomColorPreview();
            
            if (CustomColorRSlider != null && CustomColorRLabel != null) 
                CustomColorRLabel.Text = ((int)CustomColorRSlider.Value).ToString();
            if (CustomColorGSlider != null && CustomColorGLabel != null) 
                CustomColorGLabel.Text = ((int)CustomColorGSlider.Value).ToString();
            if (CustomColorBSlider != null && CustomColorBLabel != null) 
                CustomColorBLabel.Text = ((int)CustomColorBSlider.Value).ToString();
        }

        private void UpdateCustomColorPreview()
        {
            if (CustomColorPreview == null || CustomColorRSlider == null || 
                CustomColorGSlider == null || CustomColorBSlider == null) return;
            
            var r = (byte)CustomColorRSlider.Value;
            var g = (byte)CustomColorGSlider.Value;
            var b = (byte)CustomColorBSlider.Value;
            
            var color = WpfColor.FromArgb(255, r, g, b);
            CustomColorPreview.Fill = new SolidColorBrush(color);
        }

        private void AcceptCustomColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (CustomColorRSlider == null || CustomColorGSlider == null || CustomColorBSlider == null) return;
            
            var r = (byte)CustomColorRSlider.Value;
            var g = (byte)CustomColorGSlider.Value;
            var b = (byte)CustomColorBSlider.Value;
            
            _selectedColor = WinUIColor.FromArgb(255, r, g, b);
            UpdateColorPreviewsWithCurrentColor();
            
            if (ColorPickerOverlay != null)
                ColorPickerOverlay.Visibility = Visibility.Collapsed;
            _isCustomColorPickerOpen = false;
        }

        private void CancelCustomColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (ColorPickerOverlay != null)
                ColorPickerOverlay.Visibility = Visibility.Collapsed;
            _isCustomColorPickerOpen = false;
        }

        #endregion

        #region Slider Event Handlers

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FontSizeValueLabel != null)
                FontSizeValueLabel.Text = ((int)e.NewValue).ToString();
            
            // Apply real-time update for font size changes
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void ShapeSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ShapeWidthLabel != null && ShapeWidthSlider != null)
                ShapeWidthLabel.Text = ((int)ShapeWidthSlider.Value).ToString();
            if (ShapeHeightLabel != null && ShapeHeightSlider != null)
                ShapeHeightLabel.Text = ((int)ShapeHeightSlider.Value).ToString();
            
            // Apply real-time update for shape size changes
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void ImageScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ImageScaleLabel != null)
                ImageScaleLabel.Text = e.NewValue.ToString("F1");
            
            // Apply real-time update for image scale changes
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void ImageRotationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ImageRotationLabel != null)
                ImageRotationLabel.Text = $"{(int)e.NewValue}°";
            
            // Apply real-time update for image rotation changes
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void MotionSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MotionSpeedLabel != null)
                MotionSpeedLabel.Text = ((int)e.NewValue).ToString();
            
            // Apply real-time update for motion speed changes
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void TextColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateTextColorPreview();
            UpdateTextColorLabels();
            
            // Apply real-time update for text color changes
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void UpdateTextColorPreview()
        {
            if (TextColorPreview == null || TextColorRSlider == null || 
                TextColorGSlider == null || TextColorBSlider == null) return;
            
            var r = (byte)TextColorRSlider.Value;
            var g = (byte)TextColorGSlider.Value;
            var b = (byte)TextColorBSlider.Value;
            
            var color = WpfColor.FromArgb(255, r, g, b);
            TextColorPreview.Fill = new SolidColorBrush(color);
        }

        private void UpdateTextColorLabels()
        {
            if (TextColorRLabel != null && TextColorRSlider != null) 
                TextColorRLabel.Text = ((int)TextColorRSlider.Value).ToString();
            if (TextColorGLabel != null && TextColorGSlider != null) 
                TextColorGLabel.Text = ((int)TextColorGSlider.Value).ToString();
            if (TextColorBLabel != null && TextColorBSlider != null) 
                TextColorBLabel.Text = ((int)TextColorBSlider.Value).ToString();
        }

        private void ShapeColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateShapeColorPreview();
            UpdateShapeColorLabels();
            
            // Apply real-time update for shape color changes
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void UpdateShapeColorPreview()
        {
            if (ShapeColorPreview == null || ShapeColorRSlider == null || 
                ShapeColorGSlider == null || ShapeColorBSlider == null) return;
            
            var r = (byte)ShapeColorRSlider.Value;
            var g = (byte)ShapeColorGSlider.Value;
            var b = (byte)ShapeColorBSlider.Value;
            
            var color = WpfColor.FromArgb(255, r, g, b);
            ShapeColorPreview.Fill = new SolidColorBrush(color);
        }

        private void UpdateShapeColorLabels()
        {
            if (ShapeColorRLabel != null && ShapeColorRSlider != null) 
                ShapeColorRLabel.Text = ((int)ShapeColorRSlider.Value).ToString();
            if (ShapeColorGLabel != null && ShapeColorGSlider != null) 
                ShapeColorGLabel.Text = ((int)ShapeColorGSlider.Value).ToString();
            if (ShapeColorBLabel != null && ShapeColorBSlider != null) 
                ShapeColorBLabel.Text = ((int)ShapeColorBSlider.Value).ToString();
        }

        #endregion

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

                _renderService.StartAutoRendering(_renderService.TargetFPS);
                StartRenderingButton.IsEnabled = false;
                StopRenderingButton.IsEnabled = true;
                UpdateStatus("Rendering started - Real-time updates active", false);
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
                FontSize =(float)(FontSizeSlider?.Value ?? 24),
                TextColor = _selectedColor,
                IsDraggable = true,
                IsVisible = true
            };

            _renderService.AddElement(textElement);
            UpdateElementsList();
            UpdateStatus($"Added text element: {text} (Real-time editing enabled)", false);
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
                    var size = (int)(DefaultElementSizeSlider?.Value ?? 80);

                    var imageElement = new ImageElement($"Image_{++_elementCounter}")
                    {
                        ImagePath = imageKey,
                        Position = position,
                        Size = new WinFoundation.Size(size, size),
                        Scale = 1.0f,
                        IsDraggable = true,
                        IsVisible = true
                    };

                    _renderService.AddElement(imageElement);
                    UpdateElementsList();
                    UpdateStatus($"Added image element: {Path.GetFileName(openDialog.FileName)} (Real-time editing enabled)", false);
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
            var size = (int)(DefaultElementSizeSlider?.Value ?? 60);

            var shapeElement = new ShapeElement($"Shape_{++_elementCounter}")
            {
                ShapeType = ShapeType.Circle,
                Position = position,
                Size = new WinFoundation.Size(size, size),
                FillColor = _selectedColor,
                StrokeColor = WinUIColor.FromArgb(255, 255, 255, 255),
                StrokeWidth = 2,
                IsDraggable = true,
                IsVisible = true
            };

            _renderService.AddElement(shapeElement);
            UpdateElementsList();
            UpdateStatus($"Added shape element (Real-time editing enabled)", false);
        }

        #endregion

        #region Motion Elements

        private void AddBouncingBallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var position = new WinFoundation.Point(_random.Next(50, 400), _random.Next(50, 400));

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
                position, 12f, _selectedColor, motionConfig);

            if (elementIndex >= 0)
            {
                UpdateElementsList();
                UpdateStatus($"Added bouncing ball (Real-time motion editing enabled)", false);
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
                FontSize = (float)(FontSizeSlider?.Value ?? 24),
                TextColor = _selectedColor,
                IsDraggable = false
            };

            var elementIndex = _renderService.AddTextElementWithMotion(text, center, motionConfig, textConfig);

            if (elementIndex >= 0)
            {
                UpdateElementsList();
                UpdateStatus($"Added rotating text (Real-time motion editing enabled)", false);
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

            var size = new WinFoundation.Size(30, 30);

            var elementIndex = _renderService.AddRectangleElementWithMotion(position, size, _selectedColor, motionConfig);

            if (elementIndex >= 0)
            {
                UpdateElementsList();
                UpdateStatus($"Added oscillating shape (Real-time motion editing enabled)", false);
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
            UpdateStatus("Converted element to motion (Real-time motion editing enabled)", false);
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
            await _renderService.SetBackgroundColorAsync(_selectedColor, (float)BackgroundOpacitySlider.Value);
        }

        private async void SetGradientButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var gradients = new[]
            {
                (WinUIColor.FromArgb(255, 64, 0, 128), WinUIColor.FromArgb(255, 0, 64, 128)),   // Purple to Blue
                (WinUIColor.FromArgb(255, 128, 64, 0), WinUIColor.FromArgb(255, 255, 128, 0)),  // Brown to Orange
                (WinUIColor.FromArgb(255, 0, 64, 0), WinUIColor.FromArgb(255, 0, 128, 64)),     // Dark Green to Green
                (_selectedColor, WinUIColor.FromArgb(255, 32, 32, 32)),   // Selected color to dark
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
            if (_renderService == null || _isCustomColorPickerOpen) return;

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
            if (_renderService == null || _isCustomColorPickerOpen) return;

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
                var rtInfo = _isRealTimeUpdateEnabled ? " [RT]" : "";
                return $"{e.Name} ({e.Type}){motionInfo}{rtInfo}";
            }).ToList();

            ElementCountLabel.Text = $"Elements: {elements.Count} (Real-time: {(_isRealTimeUpdateEnabled ? "ON" : "OFF")})";
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
                ElementTypeText.Text = $"Type: {element.Type} | ID: {element.Id.ToString()[..8]}... | Real-time: {(_isRealTimeUpdateEnabled ? "ON" : "OFF")}";
                RenderInfoLabel.Text = $"Rendering at {_renderService?.TargetFPS ?? 30} FPS";

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
            if (FontSizeSlider != null) FontSizeSlider.Value = element.FontSize;
            
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

            // Set color sliders
            if (TextColorRSlider != null) TextColorRSlider.Value = element.TextColor.R;
            if (TextColorGSlider != null) TextColorGSlider.Value = element.TextColor.G;
            if (TextColorBSlider != null) TextColorBSlider.Value = element.TextColor.B;
            
            UpdateTextColorPreview();
        }

        private void UpdateImageElementProperties(ImageElement element)
        {
            ImagePropertiesPanel.Visibility = Visibility.Visible;
            if (ImageScaleSlider != null) ImageScaleSlider.Value = element.Scale;
            if (ImageRotationSlider != null) ImageRotationSlider.Value = element.Rotation;
        }

        private void UpdateShapeElementProperties(ShapeElement element)
        {
            ShapePropertiesPanel.Visibility = Visibility.Visible;
            
            // Set shape type
            ShapeTypeComboBox.SelectedIndex = (int)element.ShapeType;
            
            if (ShapeWidthSlider != null) ShapeWidthSlider.Value = element.Size.Width;
            if (ShapeHeightSlider != null) ShapeHeightSlider.Value = element.Size.Height;
            
            // Set fill color sliders
            if (ShapeColorRSlider != null) ShapeColorRSlider.Value = element.FillColor.R;
            if (ShapeColorGSlider != null) ShapeColorGSlider.Value = element.FillColor.G;
            if (ShapeColorBSlider != null) ShapeColorBSlider.Value = element.FillColor.B;
            
            UpdateShapeColorPreview();
        }

        private void UpdateMotionElementProperties(IMotionElement element)
        {
            MotionPropertiesPanel.Visibility = Visibility.Visible;
            
            MotionTypeComboBox.SelectedItem = element.MotionConfig.MotionType.ToString();
            if (MotionSpeedSlider != null) MotionSpeedSlider.Value = element.MotionConfig.Speed;
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
            // For compatibility with manual mode - properties are now handled by real-time system
            if (!_isRealTimeUpdateEnabled)
            {
                // Properties are staged but not applied until Apply is clicked (legacy behavior)
            }
        }

        /// <summary>
        /// Manual apply changes button - now shows informational message about real-time mode
        /// </summary>
        private void ApplyChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRealTimeUpdateEnabled)
            {
                UpdateStatus("Real-time updates are enabled - changes apply automatically!", false);
                return;
            }

            if (_currentEditingElement == null || _renderService == null)
            {
                UpdateStatus("No element selected for editing", true);
                return;
            }

            try
            {
                var properties = GatherElementPropertiesFromUI();
                _renderService.UpdateElementProperties(_currentEditingElement, properties);
                
                UpdateStatus("Element changes applied manually", false);
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
            properties["FontSize"] = (float)(FontSizeSlider?.Value ?? 24);

            if (FontFamilyComboBox.SelectedItem is ComboBoxItem selectedFont)
                properties["FontFamily"] = selectedFont.Content.ToString();

            if (TextColorRSlider != null && TextColorGSlider != null && TextColorBSlider != null)
            {
                var r = (byte)TextColorRSlider.Value;
                var g = (byte)TextColorGSlider.Value;
                var b = (byte)TextColorBSlider.Value;
                properties["TextColor"] = WinUIColor.FromArgb(255, r, g, b);
            }
        }

        private void GatherImageElementProperties(Dictionary<string, object> properties)
        {
            properties["Scale"] = (float)(ImageScaleSlider?.Value ?? 1.0);
            properties["Rotation"] = (float)(ImageRotationSlider?.Value ?? 0.0);
        }

        private void GatherShapeElementProperties(Dictionary<string, object> properties)
        {
            if (ShapeTypeComboBox.SelectedIndex >= 0)
                properties["ShapeType"] = (ShapeType)ShapeTypeComboBox.SelectedIndex;

            if (ShapeWidthSlider != null && ShapeHeightSlider != null)
            {
                properties["Size"] = new WinFoundation.Size(ShapeWidthSlider.Value, ShapeHeightSlider.Value);
            }

            if (ShapeColorRSlider != null && ShapeColorGSlider != null && ShapeColorBSlider != null)
            {
                var r = (byte)ShapeColorRSlider.Value;
                var g = (byte)ShapeColorGSlider.Value;
                var b = (byte)ShapeColorBSlider.Value;
                properties["FillColor"] = WinUIColor.FromArgb(255, r, g, b);
            }
        }

        private void GatherMotionElementProperties(Dictionary<string, object> properties)
        {
            if (MotionTypeComboBox.SelectedItem is string motionTypeStr &&
                Enum.TryParse<MotionType>(motionTypeStr, out var motionType))
                properties["MotionType"] = motionType;

            properties["Speed"] = (float)MotionSpeedSlider.Value;
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
                            UpdateStatus($"Scene imported: {Path.GetFileName(openDialog.FileName)} (Real-time editing enabled)", false);
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
                    new SolidColorBrush(Colors.Red) :
                    new SolidColorBrush(Colors.LightGreen);
            }
        }

        #endregion

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Stop debounce timer
                _textUpdateDebounceTimer?.Stop();
                _textUpdateDebounceTimer = null;

                // Stop rendering service
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
