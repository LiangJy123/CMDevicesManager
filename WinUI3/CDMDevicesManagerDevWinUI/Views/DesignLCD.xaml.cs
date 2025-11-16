using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using CMDevicesManager.Models;
using CMDevicesManager.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using SkiaSharp;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinUIColor = Windows.UI.Color;
using Point = Windows.Foundation.Point;

namespace CDMDevicesManagerDevWinUI.Views
{
    public sealed partial class DesignLCD : Page
    {
        private const int CanvasWidth = 480;
        private const int CanvasHeight = 480;

        private readonly InteractiveSkiaRenderingService _renderService = new();
        private readonly ObservableCollection<string> _assets = new();
        private readonly List<WinUIColor> _quickColors = new()
        {
            WinUIColor.FromArgb(255, 255, 255, 255),
            WinUIColor.FromArgb(255, 240, 240, 240),
            WinUIColor.FromArgb(255, 52, 152, 219),
            WinUIColor.FromArgb(255, 46, 204, 113),
            WinUIColor.FromArgb(255, 155, 89, 182),
            WinUIColor.FromArgb(255, 230, 126, 34),
            WinUIColor.FromArgb(255, 231, 76, 60),
            WinUIColor.FromArgb(255, 41, 128, 185),
            WinUIColor.FromArgb(255, 26, 188, 156),
            WinUIColor.FromArgb(255, 243, 156, 18)
        };
        private readonly Random _random = new();
        private readonly DispatcherQueue _dispatcherQueue;

        private RenderElement? _currentElement;
        private RenderElement? _draggedElement;
        private bool _isDragging;
        private Point _dragStart;
        private Point _elementStart;

        private WinUIColor _primaryColor = WinUIColor.FromArgb(255, 255, 255, 255);
        private WinUIColor _shapeColor = WinUIColor.FromArgb(255, 52, 152, 219);
        private string? _selectedImagePath;

    private Guid? _sceneId;

        public DesignLCD()
        {
            InitializeComponent();

            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            ElementsListView.DisplayMemberPath = nameof(ElementListItem.DisplayName);
            AssetsListView.ItemsSource = _assets;
            ElementTypeComboBox.SelectionChanged += ElementTypeComboBox_SelectionChanged;

            InitializeQuickColors();
            UpdatePrimaryColorPreview();
            UpdateShapeColorPreview();
            UpdatePropertyEditorVisibility();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await InitializeDesignerAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _renderService.ImageRendered -= OnImageRendered;
            _renderService.ElementSelected -= OnElementSelected;
            _renderService.ElementMoved -= OnElementMoved;
            _renderService.RenderingError -= OnRenderingError;
            _renderService.BackgroundChanged -= OnBackgroundChanged;

            _renderService.StopAutoRendering();
            _renderService.Dispose();
        }

        private async Task InitializeDesignerAsync()
        {
            try
            {
                UpdateStatus("Initializing renderer...");
                await _renderService.InitializeAsync(CanvasWidth, CanvasHeight);

                _renderService.ImageRendered += OnImageRendered;
                _renderService.ElementSelected += OnElementSelected;
                _renderService.ElementMoved += OnElementMoved;
                _renderService.RenderingError += OnRenderingError;
                _renderService.BackgroundChanged += OnBackgroundChanged;

                await _renderService.ResetBackgroundToDefaultAsync();
                await _renderService.RenderFrameAsync();

                UpdateElementsList();
                UpdateStatus("Designer ready");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to initialize renderer: {ex.Message}", isError: true);
            }
        }

        #region Event plumbing

        private void OnImageRendered(WriteableBitmap bitmap)
        {
            _dispatcherQueue.TryEnqueue(() => CanvasDisplay.Source = bitmap);
        }

        private void OnElementSelected(RenderElement element)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _currentElement = element;
                UpdateEditorFromElement(element);
                SelectElementInList(element);
            });
        }

        private void OnElementMoved(RenderElement element)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_currentElement?.Id == element.Id)
                {
                    UpdateEditorFromElement(element, suppressTypeUpdate: true);
                }
                UpdateElementsList();
            });
        }

        private void OnRenderingError(Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() => UpdateStatus(ex.Message, isError: true));
        }

        private void OnBackgroundChanged(string message)
        {
            _dispatcherQueue.TryEnqueue(() => UpdateStatus(message));
        }

        #endregion

        #region UI Helpers

        private void UpdateStatus(string message, bool isError = false)
        {
            StatusLabel.Text = message;
            StatusLabel.Foreground = isError
                ? new SolidColorBrush(WinUIColor.FromArgb(255, 231, 76, 60))
                : new SolidColorBrush(WinUIColor.FromArgb(255, 46, 204, 113));
        }

        private void UpdatePrimaryColorPreview()
        {
            PrimaryColorPreview.Fill = new SolidColorBrush(_primaryColor);
        }

        private void UpdateShapeColorPreview()
        {
            ShapeColorPreview.Fill = new SolidColorBrush(_shapeColor);
        }

        private void InitializeQuickColors()
        {
            QuickColorItems.Items.Clear();
            foreach (var color in _quickColors)
            {
                var button = new Button
                {
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(4),
                    Background = new SolidColorBrush(color),
                    BorderBrush = new SolidColorBrush(WinUIColor.FromArgb(64, 0, 0, 0)),
                    Tag = color
                };
                button.Click += (s, _) =>
                {
                    _primaryColor = color;
                    UpdatePrimaryColorPreview();
                };
                QuickColorItems.Items.Add(button);
            }
        }

        private void UpdatePropertyEditorVisibility()
        {
            var type = GetSelectedElementType();
            TextPropertiesPanel.Visibility = type == ElementType.Text || type == ElementType.LiveTime || type == ElementType.LiveDate || type == ElementType.SystemInfo
                ? Visibility.Visible
                : Visibility.Collapsed;
            ImagePropertiesPanel.Visibility = type == ElementType.Image ? Visibility.Visible : Visibility.Collapsed;
            ShapePropertiesPanel.Visibility = type == ElementType.Shape ? Visibility.Visible : Visibility.Collapsed;
        }

        private string GetSelectedFontFamily()
        {
            if (FontFamilyComboBox.SelectedItem is ComboBoxItem combo && combo.Content is string value)
            {
                return value;
            }
            return "Segoe UI";
        }

        private ElementType GetSelectedElementType()
        {
            if (ElementTypeComboBox.SelectedItem is ComboBoxItem combo)
            {
                return combo.Content?.ToString() switch
                {
                    "Text" => ElementType.Text,
                    "Image" => ElementType.Image,
                    "Shape" => ElementType.Shape,
                    "Live" => ElementType.LiveTime,
                    _ => ElementType.Text
                };
            }
            return ElementType.Text;
        }

        private Point GetPositionFromInputs()
        {
            var x = double.TryParse(PositionXTextBox.Text, out var px) ? px : 40;
            var y = double.TryParse(PositionYTextBox.Text, out var py) ? py : 40;
            return new Point(Math.Clamp(px, 0, CanvasWidth), Math.Clamp(py, 0, CanvasHeight));
        }

        private void UpdatePositionInputs(Point point)
        {
            PositionXTextBox.Text = ((int)point.X).ToString();
            PositionYTextBox.Text = ((int)point.Y).ToString();
        }

        private void UpdateOpacityLabel()
        {
            if (OpacitySlider is null || OpacityValueLabel is null)
            {
                return;
            }

            var value = OpacitySlider.Value;
            if (double.IsNaN(value))
            {
                value = 1.0;
            }

            value = Math.Clamp(value, OpacitySlider.Minimum, OpacitySlider.Maximum);
            OpacityValueLabel.Text = $"{Math.Round(value * 100)}%";
        }

        private void UpdateFontSizeLabel()
        {
            if (FontSizeSlider is null || FontSizeValueLabel is null)
            {
                return;
            }

            var value = FontSizeSlider.Value;
            if (double.IsNaN(value))
            {
                value = 32;
            }

            value = Math.Clamp(value, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
            FontSizeValueLabel.Text = ((int)value).ToString();
        }

        private void UpdateShapeSizeLabels()
        {
            ShapeWidthLabel.Text = ((int)ShapeWidthSlider.Value).ToString();
            ShapeHeightLabel.Text = ((int)ShapeHeightSlider.Value).ToString();
        }

        private void UpdateImageLabels()
        {
            ImageScaleLabel.Text = $"{ImageScaleSlider.Value:F1}x";
            ImageRotationLabel.Text = $"{ImageRotationSlider.Value:F0}°";
        }

        private void UpdateBackgroundOpacityLabel()
        {
            BackgroundOpacityLabel.Text = BackgroundOpacitySlider.Value.ToString("F2");
        }

        #endregion

        #region Element management

        private async void CreateElementButton_Click(object sender, RoutedEventArgs e)
        {
            var type = GetSelectedElementType();
            var element = await CreateElementFromInputsAsync(type);
            if (element == null)
            {
                return;
            }

            _renderService.AddElement(element);
            _renderService.SelectElement(element);
            UpdateElementsList();
            await _renderService.RenderFrameAsync();
            UpdateStatus($"Created {element.Type} element");
        }

        private async Task<RenderElement?> CreateElementFromInputsAsync(ElementType type)
        {
            var position = GetPositionFromInputs();
            var isVisible = VisibleCheckBox.IsChecked ?? true;
            var isDraggable = DraggableCheckBox.IsChecked ?? true;
            var opacity = (float)Math.Clamp(OpacitySlider.Value, 0.0, 1.0);

            switch (type)
            {
                case ElementType.Text:
                    return new TextElement(GenerateElementName("Text"))
                    {
                        Text = string.IsNullOrWhiteSpace(TextContentTextBox.Text) ? "Sample text" : TextContentTextBox.Text,
                        Position = position,
                        Size = new Size(240, 80),
                        FontFamily = GetSelectedFontFamily(),
                        FontSize = (float)FontSizeSlider.Value,
                        TextColor = _primaryColor,
                        IsVisible = isVisible,
                        IsDraggable = isDraggable,
                        Opacity = opacity
                    };

                case ElementType.Shape:
                    return new ShapeElement(GenerateElementName("Shape"))
                    {
                        Position = position,
                        Size = new Size(ShapeWidthSlider.Value, ShapeHeightSlider.Value),
                        ShapeType = GetSelectedShapeType(),
                        FillColor = _shapeColor,
                        StrokeColor = WinUIColor.FromArgb(255, 255, 255, 255),
                        StrokeWidth = 2f,
                        IsVisible = isVisible,
                        IsDraggable = isDraggable,
                        Opacity = opacity
                    };

                case ElementType.Image:
                    if (string.IsNullOrWhiteSpace(_selectedImagePath))
                    {
                        UpdateStatus("Select an image before creating an image element", true);
                        return null;
                    }

                    var imageSize = await GetImageSizeAsync(_selectedImagePath);
                    return new ImageElement(GenerateElementName("Image"))
                    {
                        Position = position,
                        Size = imageSize,
                        ImagePath = _selectedImagePath,
                        Scale = (float)ImageScaleSlider.Value,
                        Rotation = (float)ImageRotationSlider.Value,
                        IsVisible = isVisible,
                        IsDraggable = isDraggable,
                        Opacity = opacity
                    };

                case ElementType.LiveTime:
                case ElementType.LiveDate:
                case ElementType.SystemInfo:
                    return new LiveElement(type, GenerateElementName("Live"))
                    {
                        Position = position,
                        Size = new Size(240, 64),
                        FontFamily = GetSelectedFontFamily(),
                        FontSize = (float)FontSizeSlider.Value,
                        TextColor = _primaryColor,
                        Format = "HH:mm:ss",
                        IsVisible = isVisible,
                        IsDraggable = isDraggable,
                        Opacity = opacity
                    };
            }

            return null;
        }

        private async void UpdateElementButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentElement == null)
            {
                UpdateStatus("Select an element to update", true);
                return;
            }

            var properties = await BuildPropertyDictionaryAsync(_currentElement);
            _renderService.UpdateElementProperties(_currentElement, properties);
            await _renderService.RenderFrameAsync();
            UpdateStatus($"Updated {_currentElement.Name}");
        }

        private async Task<Dictionary<string, object>> BuildPropertyDictionaryAsync(RenderElement element)
        {
            var dict = new Dictionary<string, object>
            {
                ["Position"] = GetPositionFromInputs(),
                ["IsVisible"] = VisibleCheckBox.IsChecked ?? true,
                ["IsDraggable"] = DraggableCheckBox.IsChecked ?? true,
                ["Opacity"] = (float)Math.Clamp(OpacitySlider.Value, 0.0, 1.0)
            };

            switch (element)
            {
                case TextElement:
                case LiveElement:
                    dict["Text"] = string.IsNullOrWhiteSpace(TextContentTextBox.Text) ? "Sample text" : TextContentTextBox.Text;
                    dict["FontFamily"] = GetSelectedFontFamily();
                    dict["FontSize"] = (float)FontSizeSlider.Value;
                    dict["TextColor"] = _primaryColor;
                    break;
                case ShapeElement shape:
                    dict["ShapeType"] = GetSelectedShapeType();
                    dict["Size"] = new Size(ShapeWidthSlider.Value, ShapeHeightSlider.Value);
                    dict["FillColor"] = _shapeColor;
                    dict["StrokeWidth"] = shape.StrokeWidth;
                    break;
                case ImageElement:
                    if (!string.IsNullOrWhiteSpace(_selectedImagePath))
                    {
                        dict["ImagePath"] = _selectedImagePath;
                    }
                    dict["Scale"] = (float)ImageScaleSlider.Value;
                    dict["Rotation"] = (float)ImageRotationSlider.Value;
                    dict["Size"] = await GetImageSizeAsync(((ImageElement)element).ImagePath);
                    break;
            }

            return dict;
        }

        private async Task<Size> GetImageSizeAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var bitmap = SKBitmap.Decode(path);
                    if (bitmap != null)
                    {
                        return new Size(bitmap.Width, bitmap.Height);
                    }
                }
                catch
                {
                    // Ignore and fall back to default
                }
                return new Size(200, 200);
            });
        }

        private ElementType GetElementType(RenderElement element) => element.Type;

        private ShapeType GetSelectedShapeType()
        {
            if (ShapeTypeComboBox.SelectedItem is ComboBoxItem combo)
            {
                return combo.Content?.ToString() switch
                {
                    "Circle" => ShapeType.Circle,
                    "Triangle" => ShapeType.Triangle,
                    _ => ShapeType.Rectangle
                };
            }
            return ShapeType.Rectangle;
        }

        private void DuplicateElementButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentElement == null)
            {
                UpdateStatus("Select an element to duplicate", true);
                return;
            }

            var clone = CloneElement(_currentElement);
            clone.Position = new Point(
                Math.Clamp(_currentElement.Position.X + 20, 0, CanvasWidth - 10),
                Math.Clamp(_currentElement.Position.Y + 20, 0, CanvasHeight - 10));

            _renderService.AddElement(clone);
            _renderService.SelectElement(clone);
            UpdateElementsList();
            UpdateStatus($"Duplicated {_currentElement.Name}");
        }

        private RenderElement CloneElement(RenderElement element)
        {
            switch (element)
            {
                case TextElement text:
                    return new TextElement(GenerateElementName(text.Name))
                    {
                        Text = text.Text,
                        FontFamily = text.FontFamily,
                        FontSize = text.FontSize,
                        TextColor = text.TextColor,
                        Position = text.Position,
                        Size = text.Size,
                        Opacity = text.Opacity,
                        IsVisible = text.IsVisible,
                        IsDraggable = text.IsDraggable,
                        ZIndex = text.ZIndex
                    };
                case ShapeElement shape:
                    return new ShapeElement(GenerateElementName(shape.Name))
                    {
                        ShapeType = shape.ShapeType,
                        Position = shape.Position,
                        Size = shape.Size,
                        FillColor = shape.FillColor,
                        StrokeColor = shape.StrokeColor,
                        StrokeWidth = shape.StrokeWidth,
                        Opacity = shape.Opacity,
                        IsVisible = shape.IsVisible,
                        IsDraggable = shape.IsDraggable,
                        ZIndex = shape.ZIndex
                    };
                case ImageElement image:
                    return new ImageElement(GenerateElementName(image.Name))
                    {
                        ImagePath = image.ImagePath,
                        Position = image.Position,
                        Size = image.Size,
                        Scale = image.Scale,
                        Rotation = image.Rotation,
                        Opacity = image.Opacity,
                        IsVisible = image.IsVisible,
                        IsDraggable = image.IsDraggable,
                        ZIndex = image.ZIndex
                    };
                case LiveElement live:
                    return new LiveElement(live.Type, GenerateElementName(live.Name))
                    {
                        Position = live.Position,
                        Size = live.Size,
                        FontFamily = live.FontFamily,
                        FontSize = live.FontSize,
                        TextColor = live.TextColor,
                        Format = live.Format,
                        Opacity = live.Opacity,
                        IsVisible = live.IsVisible,
                        IsDraggable = live.IsDraggable,
                        ZIndex = live.ZIndex
                    };
            }

            throw new NotSupportedException("Unsupported element type");
        }

        private void DeleteElementButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentElement == null)
            {
                UpdateStatus("No element selected", true);
                return;
            }

            _renderService.RemoveElement(_currentElement);
            _currentElement = null;
            UpdateElementsList();
            UpdateStatus("Element removed");
        }

        private void AddBouncingTextButton_Click(object sender, RoutedEventArgs e)
        {
            var text = new MotionTextElement(GenerateElementName("Bouncing"))
            {
                Text = "Bouncing text",
                Position = new Point(100, 100),
                FontFamily = "Segoe UI",
                FontSize = 28,
                TextColor = WinUIColor.FromArgb(255, 255, 215, 0),
                Size = new Size(260, 72),
                MotionConfig = new ElementMotionConfig
                {
                    MotionType = MotionType.Bounce,
                    Speed = 150,
                    RespectBoundaries = true
                }
            };

            _renderService.AddElement(text);
            _renderService.SelectElement(text);
            UpdateElementsList();
            UpdateStatus("Added bouncing text");
        }

        private void AddSampleShapeButton_Click(object sender, RoutedEventArgs e)
        {
            var shape = new ShapeElement(GenerateElementName("Shape"))
            {
                ShapeType = ShapeType.Circle,
                Size = new Size(140, 140),
                Position = new Point(220, 220),
                FillColor = WinUIColor.FromArgb(255, 52, 152, 219),
                Opacity = 0.85f
            };

            _renderService.AddElement(shape);
            _renderService.SelectElement(shape);
            UpdateElementsList();
            UpdateStatus("Added sample shape");
        }

        private void AddSampleImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_selectedImagePath))
            {
                UpdateStatus("Select an image first", true);
                return;
            }

            var image = new ImageElement(GenerateElementName("Image"))
            {
                ImagePath = _selectedImagePath,
                Position = new Point(150, 150),
                Size = new Size(200, 200),
                Scale = 1.0f
            };

            _renderService.AddElement(image);
            _renderService.SelectElement(image);
            UpdateElementsList();
            UpdateStatus("Added sample image");
        }

        private string GenerateElementName(string prefix)
        {
            var elements = _renderService.GetElements();
            var index = 1;
            string candidate;
            do
            {
                candidate = $"{prefix}{index++}";
            }
            while (elements.Any(e => e.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)));

            return candidate;
        }

        private void ElementsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ElementsListView.SelectedItem is ElementListItem item)
            {
                _renderService.SelectElement(item.Element);
            }
        }

        private void UpdateElementsList()
        {
            var elements = _renderService.GetElements()
                .OrderBy(e => e.ZIndex)
                .Select(e => new ElementListItem(e))
                .ToList();

            ElementsListView.ItemsSource = elements;
            ElementCountLabel.Text = $"Elements: {elements.Count}";

            if (_currentElement != null)
            {
                var match = elements.FirstOrDefault(e => e.Id == _currentElement.Id);
                if (match != null)
                {
                    ElementsListView.SelectedItem = match;
                }
            }
        }

        private void SelectElementInList(RenderElement element)
        {
            if (ElementsListView.ItemsSource is IEnumerable<ElementListItem> list)
            {
                var match = list.FirstOrDefault(i => i.Id == element.Id);
                if (match != null)
                {
                    ElementsListView.SelectedItem = match;
                }
            }
        }

        private void MoveElementUpButton_Click(object sender, RoutedEventArgs e) => AdjustZIndex(1);

        private void MoveElementDownButton_Click(object sender, RoutedEventArgs e) => AdjustZIndex(-1);

        private void DeleteSelectedElementButton_Click(object sender, RoutedEventArgs e) => DeleteElementButton_Click(sender, e);

        private void AdjustZIndex(int delta)
        {
            if (_currentElement == null)
            {
                UpdateStatus("Select an element", true);
                return;
            }

            var properties = new Dictionary<string, object>
            {
                ["ZIndex"] = _currentElement.ZIndex + delta
            };
            _renderService.UpdateElementProperties(_currentElement, properties);
            UpdateElementsList();
        }

        #endregion

        #region Canvas interaction

        private void CanvasDisplay_PointerPressed(object sender, PointerRoutedEventArgs e) => HandlePointerPressed(e);
        private void CanvasDisplay_PointerReleased(object sender, PointerRoutedEventArgs e) => HandlePointerReleased(e);
        private void CanvasDisplay_PointerMoved(object sender, PointerRoutedEventArgs e) => HandlePointerMoved(e);
        private void CanvasOverlay_PointerPressed(object sender, PointerRoutedEventArgs e) => HandlePointerPressed(e);
        private void CanvasOverlay_PointerReleased(object sender, PointerRoutedEventArgs e) => HandlePointerReleased(e);
        private void CanvasOverlay_PointerMoved(object sender, PointerRoutedEventArgs e) => HandlePointerMoved(e);

        private void HandlePointerPressed(PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(InteractionOverlay).Position;
            MousePositionLabel.Text = $"Mouse: ({(int)point.X}, {(int)point.Y})";

            var element = _renderService.HitTest(point);
            if (element != null)
            {
                _renderService.SelectElement(element);
                _draggedElement = element;
                _elementStart = element.Position;
                _dragStart = point;
                _isDragging = true;
                InteractionOverlay.CapturePointer(e.Pointer);
            }
        }

        private void HandlePointerReleased(PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                InteractionOverlay.ReleasePointerCapture(e.Pointer);
            }
        }

        private void HandlePointerMoved(PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(InteractionOverlay).Position;
            MousePositionLabel.Text = $"Mouse: ({(int)point.X}, {(int)point.Y})";

            if (_isDragging && _draggedElement != null)
            {
                var delta = new Point(point.X - _dragStart.X, point.Y - _dragStart.Y);
                var newPosition = new Point(
                    Math.Clamp(_elementStart.X + delta.X, 0, CanvasWidth),
                    Math.Clamp(_elementStart.Y + delta.Y, 0, CanvasHeight));

                _renderService.MoveElement(_draggedElement, newPosition);
                UpdatePositionInputs(newPosition);
            }
        }

        #endregion

        #region Rendering controls

        private void StartRenderingButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(FpsLabel.Text, out var fps))
            {
                fps = 30;
            }
            _renderService.TargetFPS = fps;
            _renderService.StartAutoRendering(fps);
            UpdateStatus($"Rendering at {fps} FPS");
        }

        private void StopRenderingButton_Click(object sender, RoutedEventArgs e)
        {
            _renderService.StopAutoRendering();
            UpdateStatus("Rendering paused");
        }

        private void ClearCanvasButton_Click(object sender, RoutedEventArgs e)
        {
            _renderService.ClearElements();
            _currentElement = null;
            UpdateElementsList();
            UpdateStatus("Canvas cleared");
        }

        private async void CreateSceneButton_Click(object sender, RoutedEventArgs e)
        {
            ClearCanvasButton_Click(sender, e);
            AddBouncingTextButton_Click(sender, e);
            AddSampleShapeButton_Click(sender, e);
            if (!string.IsNullOrWhiteSpace(_selectedImagePath))
            {
                AddSampleImageButton_Click(sender, e);
            }
            await _renderService.RenderFrameAsync();
            UpdateStatus("Sample scene created");
        }

        private void FpsSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            FpsLabel.Text = ((int)e.NewValue).ToString();
        }

        #endregion

        #region Property editors

        private void ElementTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePropertyEditorVisibility();
        }

        private void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateOpacityLabel();
        }

        private void FontSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateFontSizeLabel();
        }

        private async void PrimaryColorButton_Click(object sender, RoutedEventArgs e)
        {
            var color = await PickColorAsync(_primaryColor);
            if (color.HasValue)
            {
                _primaryColor = color.Value;
                UpdatePrimaryColorPreview();
            }
        }

        private async void ShapeColorButton_Click(object sender, RoutedEventArgs e)
        {
            var color = await PickColorAsync(_shapeColor);
            if (color.HasValue)
            {
                _shapeColor = color.Value;
                UpdateShapeColorPreview();
            }
        }

        private async Task<WinUIColor?> PickColorAsync(WinUIColor initial)
        {
            var picker = new ColorPicker
            {
                IsMoreButtonVisible = false,
                IsHexInputVisible = true,
                Color = initial
            };

            var dialog = new ContentDialog
            {
                Title = "Select color",
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot,
                Content = picker
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? picker.Color : null;
        }

        private void ShapeSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateShapeSizeLabels();
        }

        private void ImageScaleSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateImageLabels();
        }

        private void ImageRotationSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateImageLabels();
        }

        private async void SelectImageButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");
            InitializePicker(picker);
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            _selectedImagePath = file.Path;
            SelectedImageText.Text = file.Name;
            UpdateStatus($"Image loaded: {file.Name}");
        }

        private async void OpenColorPickerButton_Click(object sender, RoutedEventArgs e)
        {
            var color = await PickColorAsync(_primaryColor);
            if (color.HasValue)
            {
                _primaryColor = color.Value;
                UpdatePrimaryColorPreview();
            }
        }

        #endregion

        #region Background controls

        private async void SetSolidBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            await _renderService.SetBackgroundColorAsync(ToSkColor(_primaryColor), (float)BackgroundOpacitySlider.Value);
            await _renderService.RenderFrameAsync();
        }

        private async void SetGradientBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            var secondary = _quickColors[_random.Next(_quickColors.Count)];
            await _renderService.SetBackgroundGradientAsync(ToSkColor(_primaryColor), ToSkColor(secondary), BackgroundGradientDirection.DiagonalTopLeftToBottomRight, (float)BackgroundOpacitySlider.Value);
            await _renderService.RenderFrameAsync();
        }

        private async void LoadBackgroundImageButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            InitializePicker(picker);
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            await _renderService.SetBackgroundImageAsync(file.Path, BackgroundScaleMode.UniformToFill, (float)BackgroundOpacitySlider.Value);
            await _renderService.RenderFrameAsync();
        }

        private async void ClearBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            await _renderService.ResetBackgroundToDefaultAsync();
            await _renderService.RenderFrameAsync();
        }

        private void BackgroundOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateBackgroundOpacityLabel();
            _renderService.SetBackgroundOpacity((float)e.NewValue);
        }

    private static SKColor ToSkColor(WinUIColor color) => new(color.R, color.G, color.B, color.A);

        #endregion

        #region Scene & export

        private async void ImportSceneButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            InitializePicker(picker);
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var json = await FileIO.ReadTextAsync(file);
            await _renderService.ImportSceneFromJsonAsync(json);
            UpdateElementsList();
            UpdateStatus($"Imported scene {file.Name}");
        }

        private async void ExportSceneButton_Click(object sender, RoutedEventArgs e)
        {
            _sceneId ??= Guid.NewGuid();
            var picker = new FileSavePicker
            {
                SuggestedFileName = $"scene_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            picker.FileTypeChoices.Add("Scene", new List<string> { ".json" });
            InitializePicker(picker);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var json = await _renderService.ExportSceneToJsonAsync(_sceneId.Value.ToString());
            await FileIO.WriteTextAsync(file, json);
            UpdateStatus($"Scene exported to {file.Name}");
        }

        private async void SaveImageButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = $"render_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });
            InitializePicker(picker);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            await _renderService.SaveRenderedImageAsync(file.Path);
            UpdateStatus($"Saved image to {file.Name}");
        }

        #endregion

        #region Assets

        private async void ImportAssetButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".gif");
            InitializePicker(picker);
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            if (!_assets.Contains(file.Path))
            {
                _assets.Add(file.Path);
            }
        }

        private void RemoveAssetButton_Click(object sender, RoutedEventArgs e)
        {
            if (AssetsListView.SelectedItem is string asset)
            {
                _assets.Remove(asset);
            }
        }

        #endregion

        private void UpdateEditorFromElement(RenderElement element, bool suppressTypeUpdate = false)
        {
            _currentElement = element;
            ElementNameText.Text = element.Name;
            ElementTypeText.Text = element.Type.ToString();
            VisibleCheckBox.IsChecked = element.IsVisible;
            DraggableCheckBox.IsChecked = element.IsDraggable;
            OpacitySlider.Value = element.Opacity;
            UpdateOpacityLabel();
            UpdatePositionInputs(element.Position);

            if (!suppressTypeUpdate)
            {
                ElementTypeComboBox.SelectedIndex = element.Type switch
                {
                    ElementType.Text => 0,
                    ElementType.Image => 1,
                    ElementType.Shape => 2,
                    _ => 0
                };
            }

            switch (element)
            {
                case TextElement text:
                    TextContentTextBox.Text = text.Text;
                    FontSizeSlider.Value = text.FontSize;
                    UpdateFontSizeLabel();
                    _primaryColor = text.TextColor;
                    UpdatePrimaryColorPreview();
                    break;
                case ShapeElement shape:
                    ShapeWidthSlider.Value = shape.Size.Width;
                    ShapeHeightSlider.Value = shape.Size.Height;
                    UpdateShapeSizeLabels();
                    _shapeColor = shape.FillColor;
                    UpdateShapeColorPreview();
                    break;
                case ImageElement image:
                    ImageScaleSlider.Value = image.Scale;
                    ImageRotationSlider.Value = image.Rotation;
                    UpdateImageLabels();
                    SelectedImageText.Text = System.IO.Path.GetFileName(image.ImagePath);
                    _selectedImagePath = image.ImagePath;
                    break;
            }

            UpdatePropertyEditorVisibility();
        }

        private static void InitializePicker(object picker)
        {
            var hwnd = App.Hwnd;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        private sealed record ElementListItem(RenderElement Element)
        {
            public Guid Id => Element.Id;
            public string DisplayName => $"{Element.Name} ({Element.Type})";
        }
    }
}
