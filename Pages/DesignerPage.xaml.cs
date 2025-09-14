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
using CMDevicesManager.Models;
using CMDevicesManager.Services;
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
        //private InteractiveWin2DRenderingService? _renderService;
        private InteractiveSkiaRenderingService? _renderService;
        private bool _isDragging;
        private RenderElement? _draggedElement;
        private WinFoundation.Point _lastMousePosition;
        private WinFoundation.Point _dragOffset;
        private int _elementCounter = 0;

        // Unified element creation/editing state
        private RenderElement? _currentEditingElement;
        private bool _isUpdatingProperties = false;
        private bool _isCreatingNewElement = true; // True when creating new, false when editing existing
        private string _selectedImagePath = "";
        private readonly Random _random = new Random();

        // Real-time property update features
        private bool _isRealTimeUpdateEnabled = true;
        private DispatcherTimer? _textUpdateDebounceTimer;
        private readonly TimeSpan _textUpdateDelay = TimeSpan.FromMilliseconds(500);
        private bool _enableRealTimePreview = true;

        // Enhanced color selection state
        private WinUIColor _selectedColor = WinUIColor.FromArgb(255, 255, 100, 100);
        private bool _isCustomColorPickerOpen = false;
        private WpfRectangle? _currentSelectedColorRect;

        // Enhanced quick colors palette with better color organization
        private readonly WinUIColor[] _basicColors = new[]
        {
            // Primary Colors
            WinUIColor.FromArgb(255, 244, 67, 54),   // Red
            WinUIColor.FromArgb(255, 233, 30, 99),   // Pink
            WinUIColor.FromArgb(255, 156, 39, 176),  // Purple
            WinUIColor.FromArgb(255, 103, 58, 183),  // Deep Purple
            WinUIColor.FromArgb(255, 63, 81, 181),   // Indigo
            WinUIColor.FromArgb(255, 33, 150, 243),  // Blue
            WinUIColor.FromArgb(255, 3, 169, 244),   // Light Blue
            WinUIColor.FromArgb(255, 0, 188, 212),   // Cyan
            WinUIColor.FromArgb(255, 0, 150, 136),   // Teal
            WinUIColor.FromArgb(255, 76, 175, 80),   // Green
            WinUIColor.FromArgb(255, 139, 195, 74),  // Light Green
            WinUIColor.FromArgb(255, 205, 220, 57),  // Lime
            WinUIColor.FromArgb(255, 255, 235, 59),  // Yellow
            WinUIColor.FromArgb(255, 255, 193, 7),   // Amber
            WinUIColor.FromArgb(255, 255, 152, 0),   // Orange
            WinUIColor.FromArgb(255, 255, 87, 34),   // Deep Orange
            // Neutrals
            WinUIColor.FromArgb(255, 255, 255, 255), // White
            WinUIColor.FromArgb(255, 224, 224, 224), // Light Gray
            WinUIColor.FromArgb(255, 158, 158, 158), // Gray
            WinUIColor.FromArgb(255, 97, 97, 97),    // Dark Gray
            WinUIColor.FromArgb(255, 66, 66, 66),    // Very Dark Gray
            WinUIColor.FromArgb(255, 33, 33, 33),    // Near Black
            WinUIColor.FromArgb(255, 0, 0, 0),       // Black
        };

        // Material Design inspired colors
        private readonly WinUIColor[] _materialColors = new[]
        {
            // Soft pastels
            WinUIColor.FromArgb(255, 255, 205, 210), // Light Pink
            WinUIColor.FromArgb(255, 240, 195, 238), // Light Purple
            WinUIColor.FromArgb(255, 209, 196, 233), // Light Deep Purple
            WinUIColor.FromArgb(255, 197, 202, 233), // Light Indigo
            WinUIColor.FromArgb(255, 187, 222, 251), // Light Blue
            WinUIColor.FromArgb(255, 179, 229, 252), // Light Cyan
            WinUIColor.FromArgb(255, 178, 223, 219), // Light Teal
            WinUIColor.FromArgb(255, 200, 230, 201), // Light Green
            WinUIColor.FromArgb(255, 220, 237, 200), // Light Lime
            WinUIColor.FromArgb(255, 255, 249, 196), // Light Yellow
            WinUIColor.FromArgb(255, 255, 224, 178), // Light Orange
            WinUIColor.FromArgb(255, 255, 204, 188), // Light Deep Orange
            // Rich colors
            WinUIColor.FromArgb(255, 198, 40, 40),   // Dark Red
            WinUIColor.FromArgb(255, 142, 36, 170),  // Dark Purple
            WinUIColor.FromArgb(255, 26, 35, 126),   // Dark Indigo
            WinUIColor.FromArgb(255, 13, 71, 161),   // Dark Blue
            WinUIColor.FromArgb(255, 1, 87, 155),    // Dark Light Blue
            WinUIColor.FromArgb(255, 0, 96, 100),    // Dark Teal
            WinUIColor.FromArgb(255, 27, 94, 32),    // Dark Green
            WinUIColor.FromArgb(255, 130, 119, 23),  // Dark Lime
            WinUIColor.FromArgb(255, 245, 127, 23),  // Dark Orange
            WinUIColor.FromArgb(255, 191, 54, 12),   // Dark Deep Orange
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
            UpdatePropertyVisibility();
            UpdateElementInfo();
        }

        #region Real-Time Update System

        private void InitializeRealTimeUpdateSystem()
        {
            _textUpdateDebounceTimer = new DispatcherTimer
            {
                Interval = _textUpdateDelay
            };
            _textUpdateDebounceTimer.Tick += OnTextUpdateDebounceTimerTick;
            SetupRealTimeEventHandlers();
        }

        private void SetupRealTimeEventHandlers()
        {
            Loaded += (s, e) => AttachRealTimeEventHandlers();
        }

        private void AttachRealTimeEventHandlers()
        {
            try
            {
                // Real-time updates toggle
                var realTimeCheckBox = this.FindName("RealTimeUpdatesCheckBox") as System.Windows.Controls.CheckBox;
                if (realTimeCheckBox != null)
                {
                    realTimeCheckBox.Checked += RealTimeUpdatesCheckBox_Changed;
                    realTimeCheckBox.Unchecked += RealTimeUpdatesCheckBox_Changed;
                }

                // Element type selection
                var elementTypeComboBox = this.FindName("ElementTypeComboBox") as System.Windows.Controls.ComboBox;
                if (elementTypeComboBox != null)
                {
                    elementTypeComboBox.SelectionChanged += ElementTypeComboBox_SelectionChanged;
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

                // Combo boxes
                if (FontFamilyComboBox != null)
                {
                    FontFamilyComboBox.SelectionChanged += FontFamilyProperty_Changed;
                }
                if (ShapeTypeComboBox != null)
                {
                    ShapeTypeComboBox.SelectionChanged += ShapeTypeProperty_Changed;
                }
                if (MotionTypeComboBox != null)
                {
                    MotionTypeComboBox.SelectionChanged += MotionTypeProperty_Changed;
                }

                // Sliders - attach opacity slider to real-time updates
                var opacitySlider = this.FindName("OpacitySlider") as Slider;
                if (opacitySlider != null)
                {
                    opacitySlider.ValueChanged += OpacitySlider_ValueChanged;
                }

                // Checkboxes
                AttachCheckboxHandlers();

                UpdateStatus("Design Studio initialized - Real-time editing enabled!", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to initialize real-time updates: {ex.Message}", true);
            }
        }

        private void AttachCheckboxHandlers()
        {
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
        }

        private void ApplyRealTimePropertyUpdate()
        {
            if (!_isRealTimeUpdateEnabled || _isUpdatingProperties || _renderService == null)
                return;

            try
            {
                if (_isCreatingNewElement)
                {
                    // For new elements, just update the preview/status
                    UpdateStatus("Configure properties, then click Create", false);
                }
                else if (_currentEditingElement != null)
                {
                    // For existing elements, apply changes immediately
                    var properties = GatherElementPropertiesFromUI();
                    _renderService.UpdateElementProperties(_currentEditingElement, properties);
                    UpdateStatus("Properties updated", false);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Real-time update error: {ex.Message}", true);
            }
        }

        private void ApplyDebouncedPropertyUpdate()
        {
            if (!_isRealTimeUpdateEnabled)
                return;

            _textUpdateDebounceTimer?.Stop();
            _textUpdateDebounceTimer?.Start();
        }

        private void OnTextUpdateDebounceTimerTick(object? sender, EventArgs e)
        {
            _textUpdateDebounceTimer?.Stop();
            ApplyRealTimePropertyUpdate();
        }

        #endregion

        #region Real-Time Event Handlers

        private void TextProperty_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            ApplyDebouncedPropertyUpdate();
        }

        private void PositionProperty_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            ApplyDebouncedPropertyUpdate();
        }

        private void FontFamilyProperty_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            ApplyRealTimePropertyUpdate();
        }

        private void ShapeTypeProperty_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            ApplyRealTimePropertyUpdate();
        }

        private void MotionTypeProperty_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            ApplyRealTimePropertyUpdate();
        }

        private void CheckboxProperty_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            ApplyRealTimePropertyUpdate();
        }

        private void RealTimeUpdatesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var realTimeCheckBox = sender as System.Windows.Controls.CheckBox;
            if (realTimeCheckBox != null)
            {
                _isRealTimeUpdateEnabled = realTimeCheckBox.IsChecked == true;
                var status = _isRealTimeUpdateEnabled ? "enabled" : "disabled";
                UpdateStatus($"Real-time updates {status}", false);
                UpdateElementsList();
            }
        }

        private void ElementTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingProperties) return;
            UpdatePropertyVisibility();
            
            // Switch to creation mode when type is changed
            _isCreatingNewElement = true;
            _currentEditingElement = null;
            UpdateElementInfo();
        }

        #endregion

        private void InitializeComboBoxes()
        {
            // Initialize Motion Type ComboBox
            if (MotionTypeComboBox != null)
            {
                MotionTypeComboBox.Items.Clear();
                foreach (var motionType in Enum.GetValues<MotionType>())
                {
                    MotionTypeComboBox.Items.Add(motionType.ToString());
                }
                MotionTypeComboBox.SelectedIndex = 0; // None
            }

            // Initialize Shape Type ComboBox  
            if (ShapeTypeComboBox != null)
            {
                ShapeTypeComboBox.SelectedIndex = 0; // Rectangle
            }

            // Set default font family
            if (FontFamilyComboBox != null)
            {
                FontFamilyComboBox.SelectedIndex = 0; // Segoe UI
            }
        }

        private void InitializeColorPalette()
        {
            if (ColorPalette == null) return;
            
            ColorPalette.Children.Clear();
            
            foreach (var color in _basicColors)
            {
                var colorRect = new WpfRectangle
                {
                    Width = 24,
                    Height = 24,
                    Fill = new SolidColorBrush(WpfColor.FromArgb(color.A, color.R, color.G, color.B)),
                    Stroke = System.Windows.Media.Brushes.Gray,
                    StrokeThickness = 1,
                    Margin = new Thickness(3),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    RadiusX = 3,
                    RadiusY = 3,
                    Tag = color
                };

                colorRect.MouseLeftButtonDown += (s, e) =>
                {
                    if (s is WpfRectangle rect && rect.Tag is WinUIColor selectedColor)
                    {
                        _selectedColor = selectedColor;
                        UpdateColorPreviewsWithCurrentColor();
                        ApplyRealTimePropertyUpdate();
                    }
                };

                ColorPalette.Children.Add(colorRect);
            }
        }

        private void InitializeSliderEventHandlers()
        {
            // This method is kept for compatibility but event handlers are now attached in AttachRealTimeEventHandlers
        }

        #region UI Update Methods

        private void UpdatePropertyVisibility()
        {
            var elementTypeComboBox = this.FindName("ElementTypeComboBox") as System.Windows.Controls.ComboBox;
            if (elementTypeComboBox?.SelectedItem is ComboBoxItem selectedItem)
            {
                var elementType = selectedItem.Tag?.ToString() ?? "Text";
                
                // Hide all property sections first
                var textPropertiesSection = this.FindName("TextPropertiesSection") as StackPanel;
                var shapePropertiesSection = this.FindName("ShapePropertiesSection") as StackPanel;
                var imagePropertiesSection = this.FindName("ImagePropertiesSection") as StackPanel;
                var motionPropertiesSection = this.FindName("MotionPropertiesSection") as StackPanel;
                var commonPropertiesSection = this.FindName("CommonPropertiesSection") as StackPanel;
                
                if (textPropertiesSection != null) textPropertiesSection.Visibility = Visibility.Collapsed;
                if (shapePropertiesSection != null) shapePropertiesSection.Visibility = Visibility.Collapsed;
                if (imagePropertiesSection != null) imagePropertiesSection.Visibility = Visibility.Collapsed;
                if (motionPropertiesSection != null) motionPropertiesSection.Visibility = Visibility.Collapsed;
                
                // Show common properties when editing an existing element
                if (!_isCreatingNewElement && commonPropertiesSection != null)
                    commonPropertiesSection.Visibility = Visibility.Visible;

                // Show relevant property sections
                switch (elementType)
                {
                    case "Text":
                        if (textPropertiesSection != null) textPropertiesSection.Visibility = Visibility.Visible;
                        break;
                    case "Image":
                        if (imagePropertiesSection != null) imagePropertiesSection.Visibility = Visibility.Visible;
                        break;
                    case "Shape":
                        if (shapePropertiesSection != null) shapePropertiesSection.Visibility = Visibility.Visible;
                        break;
                    case "MotionText":
                        if (textPropertiesSection != null) textPropertiesSection.Visibility = Visibility.Visible;
                        if (motionPropertiesSection != null) motionPropertiesSection.Visibility = Visibility.Visible;
                        break;
                    case "MotionShape":
                        if (shapePropertiesSection != null) shapePropertiesSection.Visibility = Visibility.Visible;
                        if (motionPropertiesSection != null) motionPropertiesSection.Visibility = Visibility.Visible;
                        break;
                }
            }
        }

        private void UpdateElementInfo()
        {
            if (_isCreatingNewElement)
            {
                var elementType = "";
                var elementTypeComboBox = this.FindName("ElementTypeComboBox") as System.Windows.Controls.ComboBox;
                if (elementTypeComboBox?.SelectedItem is ComboBoxItem selectedItem)
                {
                    elementType = selectedItem.Content?.ToString() ?? "Element";
                }
                
                if (ElementNameText != null)
                    ElementNameText.Text = $"Create New {elementType}";
                if (ElementTypeText != null)
                    ElementTypeText.Text = "Configure properties below, then click Create";
                    
                // Update button states
                var createElementButton = this.FindName("CreateElementButton") as System.Windows.Controls.Button;
                var updateElementButton = this.FindName("UpdateElementButton") as System.Windows.Controls.Button;
                if (createElementButton != null) createElementButton.IsEnabled = true;
                if (updateElementButton != null) updateElementButton.IsEnabled = false;
            }
            else if (_currentEditingElement != null)
            {
                if (ElementNameText != null)
                    ElementNameText.Text = _currentEditingElement.Name;
                if (ElementTypeText != null)
                    ElementTypeText.Text = $"Editing {_currentEditingElement.Type} | Real-time: {(_isRealTimeUpdateEnabled ? "ON" : "OFF")}";

                // Update button states
                var createElementButton = this.FindName("CreateElementButton") as System.Windows.Controls.Button;
                var updateElementButton = this.FindName("UpdateElementButton") as System.Windows.Controls.Button;
                if (createElementButton != null) createElementButton.IsEnabled = true;
                if (updateElementButton != null) updateElementButton.IsEnabled = true;
            }
        }

        private void UpdateColorPreviewsWithCurrentColor()
        {
            var wpfColor = WpfColor.FromArgb(_selectedColor.A, _selectedColor.R, _selectedColor.G, _selectedColor.B);
            var brush = new SolidColorBrush(wpfColor);

            if (TextColorPreview != null)
                TextColorPreview.Fill = brush;
            if (ShapeColorPreview != null)
                ShapeColorPreview.Fill = brush;
                
            // Update color sliders to match selected color
            _isUpdatingProperties = true;
            try
            {
                if (TextColorRSlider != null) TextColorRSlider.Value = _selectedColor.R;
                if (TextColorGSlider != null) TextColorGSlider.Value = _selectedColor.G;
                if (TextColorBSlider != null) TextColorBSlider.Value = _selectedColor.B;
                
                if (ShapeColorRSlider != null) ShapeColorRSlider.Value = _selectedColor.R;
                if (ShapeColorGSlider != null) ShapeColorGSlider.Value = _selectedColor.G;
                if (ShapeColorBSlider != null) ShapeColorBSlider.Value = _selectedColor.B;
                
                UpdateTextColorLabels();
                UpdateShapeColorLabels();
            }
            finally
            {
                _isUpdatingProperties = false;
            }
        }

        #endregion

        private async void OnDesignerPageLoaded(object sender, RoutedEventArgs e)
        {
            await InitializeDesignerAsync();
        }

        private async Task InitializeDesignerAsync()
        {
            try
            {
                UpdateStatus("Initializing Design Studio...", false);

                //_renderService = new InteractiveWin2DRenderingService();
                _renderService = new InteractiveSkiaRenderingService();
                await _renderService.InitializeAsync(480, 480);

                // Subscribe to events
                _renderService.ImageRendered += OnFrameRendered;
                _renderService.ElementSelected += OnElementSelected;
                _renderService.ElementMoved += OnElementMoved;
                _renderService.HidStatusChanged += OnStatusChanged;
                _renderService.RenderingError += OnRenderingError;
                _renderService.BackgroundChanged += OnBackgroundChanged;

                await _renderService.ResetBackgroundToDefaultAsync();

                UpdateElementsList();
                UpdateColorPreviewsWithCurrentColor();
                UpdateStatus("🎨 Design Studio ready - Create your first element!", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to initialize: {ex.Message}", true);
            }
        }

        #region Unified Element Creation/Editing

        private void CreateElementButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            try
            {
                var elementTypeComboBox = this.FindName("ElementTypeComboBox") as System.Windows.Controls.ComboBox;
                if (elementTypeComboBox?.SelectedItem is ComboBoxItem selectedItem)
                {
                    var elementType = selectedItem.Tag?.ToString() ?? "Text";
                    
                    switch (elementType)
                    {
                        case "Text":
                            CreateTextElement();
                            break;
                        case "Image":
                            CreateImageElement();
                            break;
                        case "Shape":
                            CreateShapeElement();
                            break;
                        case "MotionText":
                            CreateMotionTextElement();
                            break;
                        case "MotionShape":
                            CreateMotionShapeElement();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to create element: {ex.Message}", true);
            }
        }

        private void UpdateElementButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditingElement == null || _renderService == null)
            {
                UpdateStatus("No element selected for update", true);
                return;
            }

            try
            {
                var properties = GatherElementPropertiesFromUI();
                _renderService.UpdateElementProperties(_currentEditingElement, properties);
                
                UpdateStatus($"✅ Updated {_currentEditingElement.Name}", false);
                UpdateElementsList();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to update element: {ex.Message}", true);
            }
        }

        private void CreateTextElement()
        {
            var text = string.IsNullOrWhiteSpace(TextContentTextBox.Text) ? "Sample Text" : TextContentTextBox.Text;
            var position = GetPositionFromUI();
            
            var textElement = new TextElement($"Text_{++_elementCounter}")
            {
                Text = text,
                Position = position,
                Size = new WinFoundation.Size(200, 40),
                FontSize = (float)(FontSizeSlider?.Value ?? 24),
                FontFamily = GetSelectedFontFamily(),
                TextColor = GetTextColor(),
                IsDraggable = DraggableCheckBox?.IsChecked ?? true,
                IsVisible = VisibleCheckBox?.IsChecked ?? true
            };

            _renderService.AddElement(textElement);
            
            // Auto-select the newly created element
            _renderService.SelectElement(textElement);
            
            // Apply opacity if available through rendering service
            var opacitySlider = this.FindName("OpacitySlider") as Slider;
            var opacityValue = (float)(opacitySlider?.Value ?? 1.0);
            if (opacityValue < 1.0f)
            {
                var properties = new Dictionary<string, object> { ["Opacity"] = opacityValue };
                _renderService.UpdateElementProperties(textElement, properties);
            }
            
            UpdateElementsList();
            UpdateStatus($"✨ Created text element: {text}", false);
        }

        private void CreateImageElement()
        {
            if (string.IsNullOrEmpty(_selectedImagePath))
            {
                UpdateStatus("Please select an image first", true);
                return;
            }

            // Image creation will be handled by the SelectImageButton_Click method
            UpdateStatus("Image element creation in progress...", false);
        }

        private void CreateShapeElement()
        {
            var position = GetPositionFromUI();
            var size = GetShapeSize();
            
            var shapeElement = new ShapeElement($"Shape_{++_elementCounter}")
            {
                ShapeType = GetSelectedShapeType(),
                Position = position,
                Size = size,
                FillColor = GetShapeColor(),
                StrokeColor = WinUIColor.FromArgb(255, 255, 255, 255),
                StrokeWidth = 2,
                IsDraggable = DraggableCheckBox?.IsChecked ?? true,
                IsVisible = VisibleCheckBox?.IsChecked ?? true
            };

            _renderService.AddElement(shapeElement);
            
            // Auto-select the newly created element
            _renderService.SelectElement(shapeElement);
            
            // Apply opacity if available through rendering service
            var opacitySlider = this.FindName("OpacitySlider") as Slider;
            var opacityValue = (float)(opacitySlider?.Value ?? 1.0);
            if (opacityValue < 1.0f)
            {
                var properties = new Dictionary<string, object> { ["Opacity"] = opacityValue };
                _renderService.UpdateElementProperties(shapeElement, properties);
            }
            
            UpdateElementsList();
            UpdateStatus($"✨ Created shape element", false);
        }

        private void CreateMotionTextElement()
        {
            var text = string.IsNullOrWhiteSpace(TextContentTextBox.Text) ? "Motion Text" : TextContentTextBox.Text;
            var position = GetPositionFromUI();
            
            var motionConfig = GetMotionConfig();
            var textConfig = new TextElementConfig
            {
                FontSize = (float)(FontSizeSlider?.Value ?? 24),
                TextColor = GetTextColor(),
                IsDraggable = DraggableCheckBox?.IsChecked ?? true
            };

            var elementIndex = _renderService.AddTextElementWithMotion(text, position, motionConfig, textConfig);
            
            if (elementIndex >= 0)
            {
                // Auto-select the newly created element
                SelectElementByIndex(elementIndex);
                
                // Apply opacity if available through rendering service
                var opacitySlider = this.FindName("OpacitySlider") as Slider;
                var opacityValue = (float)(opacitySlider?.Value ?? 1.0);
                if (opacityValue < 1.0f)
                {
                    var elements = _renderService.GetElements();
                    if (elementIndex < elements.Count)
                    {
                        var properties = new Dictionary<string, object> { ["Opacity"] = opacityValue };
                        _renderService.UpdateElementProperties(elements[elementIndex], properties);
                    }
                }
                
                UpdateElementsList();
                UpdateStatus($"✨ Created motion text element: {text}", false);
            }
        }

        private void CreateMotionShapeElement()
        {
            var position = GetPositionFromUI();
            var size = GetShapeSize();
            var motionConfig = GetMotionConfig();
            
            int elementIndex;
            if (GetSelectedShapeType() == ShapeType.Circle)
            {
                var radius = (float)(Math.Min(size.Width, size.Height) / 2);
                elementIndex = _renderService.AddCircleElementWithMotion(position, radius, GetShapeColor(), motionConfig);
            }
            else
            {
                elementIndex = _renderService.AddRectangleElementWithMotion(position, size, GetShapeColor(), motionConfig);
            }
            
            if (elementIndex >= 0)
            {
                // Auto-select the newly created element
                SelectElementByIndex(elementIndex);
                
                // Apply opacity if available through rendering service
                var opacitySlider = this.FindName("OpacitySlider") as Slider;
                var opacityValue = (float)(opacitySlider?.Value ?? 1.0);
                if (opacityValue < 1.0f)
                {
                    var elements = _renderService.GetElements();
                    if (elementIndex < elements.Count)
                    {
                        var properties = new Dictionary<string, object> { ["Opacity"] = opacityValue };
                        _renderService.UpdateElementProperties(elements[elementIndex], properties);
                    }
                }
                
                UpdateElementsList();
                UpdateStatus($"✨ Created motion shape element", false);
            }
        }
        #endregion

        #region Helper Methods for Element Creation

        private WinFoundation.Point GetPositionFromUI()
        {
            var x = double.TryParse(PositionXTextBox.Text, out var posX) ? posX : 100;
            var y = double.TryParse(PositionYTextBox.Text, out var posY) ? posY : 100;
            return new WinFoundation.Point(x, y);
        }

        private string GetSelectedFontFamily()
        {
            if (FontFamilyComboBox?.SelectedItem is ComboBoxItem selectedFont)
                return selectedFont.Content?.ToString() ?? "Segoe UI";
            return "Segoe UI";
        }

        private WinUIColor GetTextColor()
        {
            var r = (byte)(TextColorRSlider?.Value ?? 0);
            var g = (byte)(TextColorGSlider?.Value ?? 0);
            var b = (byte)(TextColorBSlider?.Value ?? 0);
            return WinUIColor.FromArgb(255, r, g, b);
        }

        private ShapeType GetSelectedShapeType()
        {
            return (ShapeType)(ShapeTypeComboBox?.SelectedIndex ?? 0);
        }

        private WinFoundation.Size GetShapeSize()
        {
            var width = ShapeWidthSlider?.Value ?? 60;
            var height = ShapeHeightSlider?.Value ?? 60;
            return new WinFoundation.Size(width, height);
        }

        private WinUIColor GetShapeColor()
        {
            var r = (byte)(ShapeColorRSlider?.Value ?? 173);
            var g = (byte)(ShapeColorGSlider?.Value ?? 216);
            var b = (byte)(ShapeColorBSlider?.Value ?? 230);
            return WinUIColor.FromArgb(255, r, g, b);
        }

        private ElementMotionConfig GetMotionConfig()
        {
            var motionType = MotionType.None;
            if (MotionTypeComboBox?.SelectedItem is string motionTypeStr)
            {
                Enum.TryParse<MotionType>(motionTypeStr, out motionType);
            }

            var position = GetPositionFromUI();
            
            return new ElementMotionConfig
            {
                MotionType = motionType,
                Speed = (float)(MotionSpeedSlider?.Value ?? 100),
                Direction = GetRandomDirection(),
                Center = new Vector2((float)position.X, (float)position.Y),
                Radius = 50f,
                RespectBoundaries = true,
                ShowTrail = ShowTrailCheckBox?.IsChecked ?? false,
                IsPaused = MotionPausedCheckBox?.IsChecked ?? false
            };
        }

        /// <summary>
        /// Helper method to auto-select a newly created element by its index
        /// </summary>
        /// <param name="elementIndex">Index of the element to select</param>
        private void SelectElementByIndex(int elementIndex)
        {
            if (_renderService == null || elementIndex < 0) return;
            
            var elements = _renderService.GetElements();
            if (elementIndex < elements.Count)
            {
                var createdElement = elements[elementIndex];
                _renderService.SelectElement(createdElement);
            }
        }

        #endregion

        #region Image Selection

        private async void SelectImageButton_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Title = "Select Image for Element"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    _selectedImagePath = openDialog.FileName;
                    
                    if (_renderService != null)
                    {
                        var imageKey = await _renderService.LoadImageAsync(openDialog.FileName);
                        var position = GetPositionFromUI();
                        var size = new WinFoundation.Size(
                            ImageScaleSlider?.Value * 100 ?? 100,
                            ImageScaleSlider?.Value * 100 ?? 100);

                        var imageElement = new ImageElement($"Image_{++_elementCounter}")
                        {
                            ImagePath = imageKey,
                            Position = position,
                            Size = size,
                            Scale = (float)(ImageScaleSlider?.Value ?? 1.0),
                            Rotation = (float)(ImageRotationSlider?.Value ?? 0.0),
                            IsDraggable = DraggableCheckBox?.IsChecked ?? true,
                            IsVisible = VisibleCheckBox?.IsChecked ?? true
                        };

                        _renderService.AddElement(imageElement);
                        
                        // Auto-select the newly created element
                        _renderService.SelectElement(imageElement);
                        
                        // Apply opacity if available through rendering service
                        var opacitySlider = this.FindName("OpacitySlider") as Slider;
                        var opacityValue = (float)(opacitySlider?.Value ?? 1.0);
                        if (opacityValue < 1.0f)
                        {
                            var properties = new Dictionary<string, object> { ["Opacity"] = opacityValue };
                            _renderService.UpdateElementProperties(imageElement, properties);
                        }
                        
                        UpdateElementsList();
                        UpdateStatus($"✨ Created image element: {Path.GetFileName(openDialog.FileName)}", false);
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Failed to load image: {ex.Message}", true);
                }
            }
        }

        #endregion

        #region Color Management

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
            ApplyRealTimePropertyUpdate();
            
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
            
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var opacityValueLabel = this.FindName("OpacityValueLabel") as TextBlock;
            if (opacityValueLabel != null)
                opacityValueLabel.Text = $"{(int)(e.NewValue * 100)}%";
            
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void ShapeSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ShapeWidthLabel != null && ShapeWidthSlider != null)
                ShapeWidthLabel.Text = ((int)ShapeWidthSlider.Value).ToString();
            if (ShapeHeightLabel != null && ShapeHeightSlider != null)
                ShapeHeightLabel.Text = ((int)ShapeHeightSlider.Value).ToString();
            
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void ImageScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ImageScaleLabel != null)
                ImageScaleLabel.Text = e.NewValue.ToString("F1") + "x";
            
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void ImageRotationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ImageRotationLabel != null)
                ImageRotationLabel.Text = $"{(int)e.NewValue}°";
            
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void MotionSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MotionSpeedLabel != null)
                MotionSpeedLabel.Text = ((int)e.NewValue).ToString();
            
            if (!_isUpdatingProperties)
                ApplyRealTimePropertyUpdate();
        }

        private void TextColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateTextColorPreview();
            UpdateTextColorLabels();
            
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

                // Switch to edit mode for the selected element
                _isCreatingNewElement = false;
                _currentEditingElement = element;
                UpdateElementPropertiesFromElement(element);
                UpdateElementInfo();
            });
        }

        private void OnElementMoved(RenderElement element)
        {
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
                await _renderService.EnableHidTransfer(true, useSuspendMedia: false);
                await _renderService.EnableHidRealTimeDisplayAsync(true);
                _renderService.StartAutoRendering(_renderService.TargetFPS);
                
                StartRenderingButton.IsEnabled = false;
                StopRenderingButton.IsEnabled = true;
                UpdateStatus("🎬 Rendering started - Live preview active!", false);
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
                UpdateStatus("⏸️ Rendering stopped", false);
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
            
            // Switch back to creation mode
            _isCreatingNewElement = true;
            _currentEditingElement = null;
            UpdateElementInfo();
            
            _elementCounter = 0;
            UpdateStatus("🗑️ Canvas cleared", false);
        }

        private void FpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var fps = (int)e.NewValue;
            if (FpsLabel != null) FpsLabel.Text = fps + " FPS";
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

        #region Quick Motion Elements

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

            var elementIndex = _renderService.AddCircleElementWithMotion(position, 12f, _selectedColor, motionConfig);

            if (elementIndex >= 0)
            {
                // Auto-select the newly created element
                SelectElementByIndex(elementIndex);
                UpdateElementsList();
                UpdateStatus($"🏀 Added bouncing ball", false);
            }
        }

        private void AddRotatingTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var text = string.IsNullOrWhiteSpace(TextContentTextBox.Text) ? "Rotating Text" : TextContentTextBox.Text;
            var center = new WinFoundation.Point(240, 240);

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
                // Auto-select the newly created element
                SelectElementByIndex(elementIndex);
                UpdateElementsList();
                UpdateStatus($"🔄 Added rotating text", false);
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
                // Auto-select the newly created element
                SelectElementByIndex(elementIndex);
                UpdateElementsList();
                UpdateStatus($"〰️ Added oscillating shape", false);
            }
        }

        private void PauseAllMotionButton_Click(object sender, RoutedEventArgs e)
        {
            _renderService?.PauseAllMotion();
            UpdateStatus("⏸️ All motion paused", false);
        }

        private void ResumeAllMotionButton_Click(object sender, RoutedEventArgs e)
        {
            _renderService?.ResumeAllMotion();
            UpdateStatus("▶️ All motion resumed", false);
        }

        #endregion

        #region Background Controls

        private async void SetSolidColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;
            var skSelectedColor = new SkiaSharp.SKColor(_selectedColor.R, _selectedColor.G, _selectedColor.B, _selectedColor.A);
            await _renderService.SetBackgroundColorAsync(skSelectedColor, (float)BackgroundOpacitySlider.Value);
        }

        private async void SetGradientButton_Click(object sender, RoutedEventArgs e)
        {
            if (_renderService == null) return;

            var gradients = new[]
            {
                (WinUIColor.FromArgb(255, 64, 0, 128), WinUIColor.FromArgb(255, 0, 64, 128)),
                (WinUIColor.FromArgb(255, 128, 64, 0), WinUIColor.FromArgb(255, 255, 128, 0)),
                (WinUIColor.FromArgb(255, 0, 64, 0), WinUIColor.FromArgb(255, 0, 128, 64)),
                (_selectedColor, WinUIColor.FromArgb(255, 32, 32, 32)),
            };

            var (startColor, endColor) = gradients[_random.Next(gradients.Length)];
            var skStartColor = new SkiaSharp.SKColor(startColor.R, startColor.G, startColor.B, startColor.A);
            var skEndColor = new SkiaSharp.SKColor(endColor.R, endColor.G, endColor.B, endColor.A);
            await _renderService.SetBackgroundGradientAsync(skStartColor, skEndColor, 
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
            if (BackgroundOpacityLabel != null) 
                BackgroundOpacityLabel.Text = opacity.ToString("F2");

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
                // Switch back to creation mode when clicking empty space
                _isCreatingNewElement = true;
                _currentEditingElement = null;
                UpdateElementInfo();
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
                    
                    // Switch back to creation mode
                    _isCreatingNewElement = true;
                    _currentEditingElement = null;
                    UpdateElementInfo();
                    
                    UpdateStatus("🗑️ Element deleted", false);
                }
            }
        }

        #endregion

        #region Element Properties Management

        private void UpdateElementPropertiesFromElement(RenderElement element)
        {
            _isUpdatingProperties = true;
            
            try
            {
                // Set element type in combo box
                var elementTypeTag = GetElementTypeTag(element);
                var elementTypeComboBox = this.FindName("ElementTypeComboBox") as System.Windows.Controls.ComboBox;
                if (elementTypeComboBox != null)
                {
                    for (int i = 0; i < elementTypeComboBox.Items.Count; i++)
                    {
                        if (elementTypeComboBox.Items[i] is ComboBoxItem item && 
                            item.Tag?.ToString() == elementTypeTag)
                        {
                            elementTypeComboBox.SelectedIndex = i;
                            break;
                        }
                    }
                }

                // Update property visibility
                UpdatePropertyVisibility();

                // Update common properties
                PositionXTextBox.Text = element.Position.X.ToString("F1");
                PositionYTextBox.Text = element.Position.Y.ToString("F1");
                VisibleCheckBox.IsChecked = element.IsVisible;
                DraggableCheckBox.IsChecked = element.IsDraggable;
                
                // Update opacity if the element has an Opacity property
                var opacitySlider = this.FindName("OpacitySlider") as Slider;
                var opacityProperty = element.GetType().GetProperty("Opacity");
                if (opacityProperty != null && opacitySlider != null)
                {
                    var opacityValue = opacityProperty.GetValue(element);
                    if (opacityValue is float opacity)
                    {
                        opacitySlider.Value = opacity;
                    }
                }
                else if (opacitySlider != null)
                {
                    opacitySlider.Value = 1.0; // Default opacity
                }

                // Update element-specific properties
                switch (element)
                {
                    case TextElement textElement:
                        UpdateTextElementPropertiesFromElement(textElement);
                        break;
                    case ImageElement imageElement:
                        UpdateImageElementPropertiesFromElement(imageElement);
                        break;
                    case ShapeElement shapeElement:
                        UpdateShapeElementPropertiesFromElement(shapeElement);
                        break;
                }

                // Update motion properties if applicable
                if (element is IMotionElement motionElement)
                {
                    UpdateMotionElementPropertiesFromElement(motionElement);
                }
            }
            finally
            {
                _isUpdatingProperties = false;
            }
        }

        private string GetElementTypeTag(RenderElement element)
        {
            return element switch
            {
                IMotionElement when element is TextElement => "MotionText",
                IMotionElement when element is ShapeElement => "MotionShape", 
                TextElement => "Text",
                ImageElement => "Image",
                ShapeElement => "Shape",
                _ => "Text"
            };
        }

        private void UpdateTextElementPropertiesFromElement(TextElement element)
        {
            TextContentTextBox.Text = element.Text;
            if (FontSizeSlider != null) FontSizeSlider.Value = element.FontSize;
            
            // Set font family
            var fontFamily = element.FontFamily;
            for (int i = 0; i < FontFamilyComboBox.Items.Count; i++)
            {
                if (FontFamilyComboBox.Items[i] is ComboBoxItem item &&
                    item.Content?.ToString() == fontFamily)
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

        private void UpdateImageElementPropertiesFromElement(ImageElement element)
        {
            if (ImageScaleSlider != null) ImageScaleSlider.Value = element.Scale;
            if (ImageRotationSlider != null) ImageRotationSlider.Value = element.Rotation;
        }

        private void UpdateShapeElementPropertiesFromElement(ShapeElement element)
        {
            ShapeTypeComboBox.SelectedIndex = (int)element.ShapeType;
            
            if (ShapeWidthSlider != null) ShapeWidthSlider.Value = element.Size.Width;
            if (ShapeHeightSlider != null) ShapeHeightSlider.Value = element.Size.Height;
            
            // Set fill color sliders
            if (ShapeColorRSlider != null) ShapeColorRSlider.Value = element.FillColor.R;
            if (ShapeColorGSlider != null) ShapeColorGSlider.Value = element.FillColor.G;
            if (ShapeColorBSlider != null) ShapeColorBSlider.Value = element.FillColor.B;
            
            UpdateShapeColorPreview();
        }

        private void UpdateMotionElementPropertiesFromElement(IMotionElement element)
        {
            // Find and select the motion type
            var motionType = element.MotionConfig.MotionType.ToString();
            for (int i = 0; i < MotionTypeComboBox.Items.Count; i++)
            {
                if (MotionTypeComboBox.Items[i].ToString() == motionType)
                {
                    MotionTypeComboBox.SelectedIndex = i;
                    break;
                }
            }
            
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

                properties["IsVisible"] = VisibleCheckBox?.IsChecked ?? true;
                properties["IsDraggable"] = DraggableCheckBox?.IsChecked ?? true;
                
                // Get opacity from slider
                var opacitySlider = this.FindName("OpacitySlider") as Slider;
                if (opacitySlider != null)
                {
                    properties["Opacity"] = (float)opacitySlider.Value;
                }

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
                properties["FontFamily"] = selectedFont.Content?.ToString() ?? "Segoe UI";

            properties["TextColor"] = GetTextColor();
        }

        private void GatherImageElementProperties(Dictionary<string, object> properties)
        {
            properties["Scale"] = (float)(ImageScaleSlider?.Value ?? 1.0);
            properties["Rotation"] = (float)(ImageRotationSlider?.Value ?? 0.0);
        }

        private void GatherShapeElementProperties(Dictionary<string, object> properties)
        {
            properties["ShapeType"] = GetSelectedShapeType();
            properties["Size"] = GetShapeSize();
            properties["FillColor"] = GetShapeColor();
        }

        private void GatherMotionElementProperties(Dictionary<string, object> properties)
        {
            if (MotionTypeComboBox.SelectedItem is string motionTypeStr &&
                Enum.TryParse<MotionType>(motionTypeStr, out var motionType))
                properties["MotionType"] = motionType;

            properties["Speed"] = (float)(MotionSpeedSlider?.Value ?? 100);
            properties["ShowTrail"] = ShowTrailCheckBox?.IsChecked ?? false;
            properties["IsPaused"] = MotionPausedCheckBox?.IsChecked ?? false;
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
                    UpdateStatus($"💾 Image saved: {Path.GetFileName(saveDialog.FileName)}", false);
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
                    UpdateStatus($"📤 Scene exported: {Path.GetFileName(saveDialog.FileName)}", false);
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
                            _isCreatingNewElement = true;
                            _currentEditingElement = null;
                            UpdateElementInfo();
                            UpdateStatus($"📥 Scene imported: {Path.GetFileName(openDialog.FileName)}", false);
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
                _textUpdateDebounceTimer?.Stop();
                _textUpdateDebounceTimer = null;
                _renderService?.StopAutoRendering();
                _renderService?.Dispose();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Cleanup error: {ex.Message}", true);
            }
        }

        #region Missing Essential Methods

        private void UpdateElementsList()
        {
            if (_renderService == null || ElementsListBox == null) return;

            var elements = _renderService.GetElements();
            ElementsListBox.ItemsSource = elements.Select(e =>
            {
                var motionInfo = e is IMotionElement motionElement ? $" ⚡{motionElement.MotionConfig.MotionType}" : "";
                var rtInfo = _isRealTimeUpdateEnabled ? " 🔴" : "⚫";
                return $"{e.Name}{motionInfo}{rtInfo}";
            }).ToList();

            ElementCountLabel.Text = $"Elements: {elements.Count} {(_isRealTimeUpdateEnabled ? "🔴 LIVE" : "⚫ MANUAL")}";
        }

        #endregion
    }
}
