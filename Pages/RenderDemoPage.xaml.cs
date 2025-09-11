using CMDevicesManager.Models;
using CMDevicesManager.Services;



using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private BackgroundRenderingService? _backgroundService;
        private InteractiveWin2DRenderingService? _interactiveService;
        private System.Windows.Threading.DispatcherTimer? _renderTimer;
        private bool _isDragging;
        private RenderElement? _draggedElement;
        private WinFoundation.Point _lastMousePosition;
        private WinFoundation.Point _dragOffset;

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

                // Initialize interactive service
                _interactiveService = new InteractiveWin2DRenderingService();
                await _interactiveService.InitializeAsync(800, 600);

                _interactiveService.ImageRendered += OnFrameRendered;
                _interactiveService.ElementSelected += OnElementSelected;
                _interactiveService.ElementMoved += OnElementMoved;

                // Initialize background service as fallback
                _backgroundService = new BackgroundRenderingService();
                await _backgroundService.InitializeAsync(800, 600);

                _backgroundService.FrameRendered += OnFrameRendered;
                _backgroundService.RawImageDataReady += OnRawImageDataReady;
                _backgroundService.RenderingError += OnRenderingError;

                UpdateElementsList();
                UpdateLiveDataCheckboxes();

                StatusLabel.Content = "Services initialized - Interactive features available";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Failed to initialize: {ex.Message}";
                // MessageBox.Show($"Initialization failed: {ex.Message}", "Error", // MessageBoxButton.OK, // MessageBoxImage.Error);
            }
        }

        private void OnFrameRendered(WriteableBitmap bitmap)
        {
            if (Dispatcher.CheckAccess())
            {
                DisplayImage.Source = bitmap;
                FpsLabel.Content = $"Rendering at {_backgroundService?.TargetFPS ?? 30} FPS";
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    DisplayImage.Source = bitmap;
                    FpsLabel.Content = $"Rendering at {_backgroundService?.TargetFPS ?? 30} FPS";
                });
            }
        }

        private void OnElementSelected(RenderElement element)
        {
            Dispatcher.Invoke(() =>
            {
                // Select the element in the list
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
            // Element position updated
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

        // Mouse interaction handlers
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
                    // Start interactive rendering with timer
                    _renderTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(1000.0 / 30) // 30 FPS
                    };
                    _renderTimer.Tick += async (s, args) =>
                    {
                        try
                        {
                            await _interactiveService.RenderFrameAsync();
                        }
                        catch (Exception ex)
                        {
                            StatusLabel.Content = $"Render error: {ex.Message}";
                        }
                    };
                    _renderTimer.Start();

                    StartButton.IsEnabled = false;
                    StopButton.IsEnabled = true;
                    StatusLabel.Content = "Interactive rendering started";
                }
                else
                {
                    // MessageBox.Show("Services not initialized. Please wait and try again.", "Warning", // MessageBoxButton.OK, // MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                // MessageBox.Show($"Error starting rendering: {ex.Message}", "Error", // MessageBoxButton.OK, // MessageBoxImage.Error);
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_renderTimer != null)
                {
                    _renderTimer.Stop();
                    _renderTimer = null;
                }

                if (_backgroundService != null)
                {
                    await _backgroundService.StopAsync();
                }

                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusLabel.Content = "Rendering stopped";
            }
            catch (Exception ex)
            {
                // MessageBox.Show($"Error stopping rendering: {ex.Message}", "Error", // MessageBoxButton.OK, // MessageBoxImage.Error);
            }
        }

        private void FpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (_backgroundService != null)
                {
                    _backgroundService.TargetFPS = (int)e.NewValue;
                }

                if (_renderTimer != null)
                {
                    _renderTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / e.NewValue);
                }

                FpsLabel.Content = $"Target FPS: {(int)e.NewValue}";
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
                    FileName = $"interactive_render_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    if (_interactiveService != null)
                    {
                        await _interactiveService.SaveRenderedImageAsync(saveDialog.FileName);
                        StatusLabel.Content = $"Frame saved: {Path.GetFileName(saveDialog.FileName)}";
                    }
                    else
                    {
                        // MessageBox.Show("No rendering service available to save frame.", "Warning", // MessageBoxButton.OK, // MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Save failed: {ex.Message}";
                // MessageBox.Show($"Failed to save frame: {ex.Message}", "Error", // MessageBoxButton.OK, // MessageBoxImage.Error);
            }
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService != null)
            {
                _interactiveService.ClearElements();
                UpdateElementsList();
                StatusLabel.Content = "All elements cleared";
            }
        }

        private void AddTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_interactiveService == null) return;

            var text = TextContentBox.Text;
            var color = WinUIColor.FromArgb(255, 255, 255, 255); // White

            var textElement = new TextElement($"Text {DateTime.Now:HHmmss}")
            {
                Text = text,
                FontSize = 24,
                TextColor = color,
                Position = new WinFoundation.Point(100, 100),
                Size = new WinFoundation.Size(200, 50)
            };

            _interactiveService.AddElement(textElement);
            UpdateElementsList();
            StatusLabel.Content = "Text element added";
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
                        Scale = 1.0f
                    };

                    _interactiveService.AddElement(imageElement);
                    UpdateElementsList();
                    StatusLabel.Content = "Image element added";
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
                StrokeWidth = 2
            };

            _interactiveService.AddElement(shapeElement);
            UpdateElementsList();
            StatusLabel.Content = "Circle element added";
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
                StrokeWidth = 2
            };

            _interactiveService.AddElement(shapeElement);
            UpdateElementsList();
            StatusLabel.Content = "Rectangle element added";
        }

        private void LiveDataCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_interactiveService != null)
            {
                _interactiveService.ShowTime = ShowTimeCheckBox.IsChecked ?? false;
                _interactiveService.ShowDate = ShowDateCheckBox.IsChecked ?? false;
                _interactiveService.ShowSystemInfo = ShowSystemInfoCheckBox.IsChecked ?? false;
                _interactiveService.ShowAnimation = ShowAnimationCheckBox.IsChecked ?? false;
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
                    DeleteElementButton.IsEnabled = true;
                }
            }
            else
            {
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
                    UpdateElementsList();
                    StatusLabel.Content = "Element deleted";
                }
            }
        }

        // Helper methods
        private void UpdateElementsList()
        {
            if (_interactiveService == null) return;

            var elements = _interactiveService.GetElements();
            ElementsListBox.ItemsSource = elements.Select(e => $"{e.Name} ({e.Type})").ToList();
        }

        private void UpdateLiveDataCheckboxes()
        {
            if (_interactiveService != null)
            {
                ShowTimeCheckBox.IsChecked = _interactiveService.ShowTime;
                ShowDateCheckBox.IsChecked = _interactiveService.ShowDate;
                ShowSystemInfoCheckBox.IsChecked = _interactiveService.ShowSystemInfo;
                ShowAnimationCheckBox.IsChecked = _interactiveService.ShowAnimation;
            }
        }


        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _renderTimer?.Stop();
                _backgroundService?.Dispose();
                _interactiveService?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }

        }
    }
}