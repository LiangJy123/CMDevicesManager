using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CMDevicesManager.Services;
using SkiaSharp;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Demo page showcasing the SkiaSharp render service capabilities
    /// </summary>
    public partial class TestDemo : Page, INotifyPropertyChanged
    {
        // Services
        private IRenderService? _renderService;
        private RenderIntegrationHelper? _helper;
        private ISystemMetricsService? _metricsService;

        // Timers
        private DispatcherTimer? _metricsTimer;
        private DispatcherTimer? _fpsTimer;

        // State tracking
        private int _frameCount = 0;
        private DateTime _lastFpsUpdate = DateTime.Now;
        private int _elementCounter = 0;

        // Data binding properties
        private string _serviceStatus = "Not Initialized";
        public string ServiceStatus { get => _serviceStatus; set { _serviceStatus = value; OnPropertyChanged(); } }

        private string _renderingStatus = "Stopped";
        public string RenderingStatus { get => _renderingStatus; set { _renderingStatus = value; OnPropertyChanged(); } }

        private string _elementCount = "Elements: 0";
        public string ElementCount { get => _elementCount; set { _elementCount = value; OnPropertyChanged(); } }

        private string _cpuUsage = "CPU: --";
        public string CpuUsage { get => _cpuUsage; set { _cpuUsage = value; OnPropertyChanged(); } }

        private string _gpuUsage = "GPU: --";
        public string GpuUsage { get => _gpuUsage; set { _gpuUsage = value; OnPropertyChanged(); } }

        private string _memoryUsage = "Memory: --";
        public string MemoryUsage { get => _memoryUsage; set { _memoryUsage = value; OnPropertyChanged(); } }

        private string _currentTime = DateTime.Now.ToString("HH:mm:ss");
        public string CurrentTime { get => _currentTime; set { _currentTime = value; OnPropertyChanged(); } }

        // Element tracking
        public ObservableCollection<RenderElementInfo> RenderElements { get; } = new();

        public TestDemo()
        {
            InitializeComponent();
            DataContext = this;
            
            // Setup timers
            InitializeTimers();
            
            // Wire up events
            Loaded += TestDemo_Loaded;
            Unloaded += TestDemo_Unloaded;
            
            AddLog("TestDemo initialized - Ready to start render service demo");
        }

        #region Control Access Helpers

        private System.Windows.Controls.Button? GetButton(string name) => FindName(name) as System.Windows.Controls.Button;
        private TextBlock? GetTextBlock(string name) => FindName(name) as TextBlock;
        private System.Windows.Controls.Image? GetImage(string name) => FindName(name) as System.Windows.Controls.Image;
        private System.Windows.Controls.ComboBox? GetComboBox(string name) => FindName(name) as System.Windows.Controls.ComboBox;
        private System.Windows.Controls.ListBox? GetListBox(string name) => FindName(name) as System.Windows.Controls.ListBox;
        private ScrollViewer? GetScrollViewer(string name) => FindName(name) as ScrollViewer;

        #endregion

        #region Event Handlers

        private void TestDemo_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize collections after controls are loaded
            var elementsList = GetListBox("ElementsList");
            if (elementsList is not null)
            {
                elementsList.ItemsSource = RenderElements;
            }
            
            AddLog("TestDemo page loaded");
        }

        private void TestDemo_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _metricsTimer?.Stop();
                _fpsTimer?.Stop();
                _helper?.StopRealtimeOutput();
                _helper?.Dispose();
                _metricsService?.Dispose();
                AddLog("Services disposed successfully");
            }
            catch (Exception ex)
            {
                AddLog($"Cleanup error: {ex.Message}");
            }
        }

        #endregion

        #region Initialization

        private void InitializeTimers()
        {
            // Metrics update timer (1 second)
            _metricsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _metricsTimer.Tick += MetricsTimer_Tick;

            // FPS calculation timer (1 second)
            _fpsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _fpsTimer.Tick += FpsTimer_Tick;
        }

        private void InitializeBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog("Initializing render service...");

                // Initialize services
                _renderService = new SkiaRenderService();
                _metricsService = new RealSystemMetricsService();
                _helper = new RenderIntegrationHelper(_renderService, _metricsService);

                // Get canvas size from combo box
                var canvasSize = GetSelectedCanvasSize();
                _helper.Initialize(canvasSize);

                ServiceStatus = "Initialized ✓";
                AddLog($"Service initialized with {canvasSize}x{canvasSize} canvas");

                // Start metrics timer
                _metricsTimer?.Start();
                UpdateSystemMetrics();

                EnableControls(true);
            }
            catch (Exception ex)
            {
                AddLog($"Failed to initialize: {ex.Message}");
                ServiceStatus = "Failed ✗";
            }
        }

        private void EnableControls(bool enabled)
        {
            var startBtn = GetButton("StartRenderBtn");
            if (startBtn is not null) startBtn.IsEnabled = enabled;
            
            var addCpuBtn = GetButton("AddCpuBtn");
            if (addCpuBtn is not null) addCpuBtn.IsEnabled = enabled;
            
            var addGpuBtn = GetButton("AddGpuBtn");
            if (addGpuBtn is not null) addGpuBtn.IsEnabled = enabled;
            
            var addDateTimeBtn = GetButton("AddDateTimeBtn");
            if (addDateTimeBtn is not null) addDateTimeBtn.IsEnabled = enabled;
            
            var addTextBtn = GetButton("AddTextBtn");
            if (addTextBtn is not null) addTextBtn.IsEnabled = enabled;
            
            var addImageBtn = GetButton("AddImageBtn");
            if (addImageBtn is not null) addImageBtn.IsEnabled = enabled;
            
            var renderOnceBtn = GetButton("RenderOnceBtn");
            if (renderOnceBtn is not null) renderOnceBtn.IsEnabled = enabled;
            
            var exportPngBtn = GetButton("ExportPngBtn");
            if (exportPngBtn is not null) exportPngBtn.IsEnabled = enabled;
        }

        #endregion

        #region Render Controls

        private void StartRenderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_helper == null) return;

            try
            {
                AddLog("Starting real-time rendering at 30 FPS...");
                _helper.StartRealtimeOutput(30, OnRenderOutputReady);
                
                RenderingStatus = "Running (30 FPS) ✓";
                _fpsTimer?.Start();
                _frameCount = 0;
                _lastFpsUpdate = DateTime.Now;

                var startBtn = GetButton("StartRenderBtn");
                if (startBtn is not null) startBtn.IsEnabled = false;
                
                var stopBtn = GetButton("StopRenderBtn");
                if (stopBtn is not null) stopBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLog($"Failed to start rendering: {ex.Message}");
            }
        }

        private void StopRenderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_helper == null) return;

            try
            {
                AddLog("Stopping real-time rendering...");
                _helper.StopRealtimeOutput(OnRenderOutputReady);
                
                RenderingStatus = "Stopped";
                _fpsTimer?.Stop();
                var fpsText = GetTextBlock("FpsTxt");
                if (fpsText is not null) fpsText.Text = "0";

                var startBtn = GetButton("StartRenderBtn");
                if (startBtn is not null) startBtn.IsEnabled = true;
                
                var stopBtn = GetButton("StopRenderBtn");
                if (stopBtn is not null) stopBtn.IsEnabled = false;
            }
            catch (Exception ex)
            {
                AddLog($"Failed to stop rendering: {ex.Message}");
            }
        }

        private void RenderOnceBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_helper == null) return;

            try
            {
                AddLog("Rendering single frame...");
                var bitmap = _helper.GetWpfBitmap();
                if (bitmap != null)
                {
                    var previewImage = GetImage("PreviewImage");
                    if (previewImage is not null)
                    {
                        previewImage.Source = bitmap;
                    }
                    AddLog("Single frame rendered successfully");
                }
                else
                {
                    AddLog("Failed to render frame");
                }
            }
            catch (Exception ex)
            {
                AddLog($"Single frame render failed: {ex.Message}");
            }
        }

        #endregion

        #region Element Management

        private void AddCpuBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_helper == null) return;

            try
            {
                var id = $"cpu_{++_elementCounter}";
                var position = GetRandomPosition();
                _helper.AddLiveCpuUsage(id, position, 18);
                
                AddElement(id, "CPU Usage", position);
                AddLog($"Added live CPU usage element at {position}");
            }
            catch (Exception ex)
            {
                AddLog($"Failed to add CPU element: {ex.Message}");
            }
        }

        private void AddGpuBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_helper == null) return;

            try
            {
                var id = $"gpu_{++_elementCounter}";
                var position = GetRandomPosition();
                _helper.AddLiveGpuUsage(id, position, 18);
                
                AddElement(id, "GPU Usage", position);
                AddLog($"Added live GPU usage element at {position}");
            }
            catch (Exception ex)
            {
                AddLog($"Failed to add GPU element: {ex.Message}");
            }
        }

        private void AddDateTimeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_helper == null) return;

            try
            {
                var id = $"datetime_{++_elementCounter}";
                var position = GetRandomPosition();
                _helper.AddLiveDateTime(id, position, 16);
                
                AddElement(id, "Date/Time", position);
                AddLog($"Added live date/time element at {position}");
            }
            catch (Exception ex)
            {
                AddLog($"Failed to add date/time element: {ex.Message}");
            }
        }

        private void AddTextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_helper == null) return;

            try
            {
                var id = $"text_{++_elementCounter}";
                var position = GetRandomPosition();
                var text = $"Demo Text #{_elementCounter}";
                _helper.AddStaticText(id, text, position, 20, Colors.Cyan);
                
                AddElement(id, "Static Text", position);
                AddLog($"Added static text '{text}' at {position}");
            }
            catch (Exception ex)
            {
                AddLog($"Failed to add text element: {ex.Message}");
            }
        }

        private void AddImageBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_helper == null) return;

            try
            {
                // Try to find background image
                var imagePath = "Resources/background.png";
                if (File.Exists(imagePath))
                {
                    var id = $"image_{++_elementCounter}";
                    var position = new SKPoint(0, 0);
                    var size = new SKSize(GetSelectedCanvasSize(), GetSelectedCanvasSize());
                    _helper.AddImageFromFile(id, imagePath, position, size, 0.3f);
                    
                    AddElement(id, "Background", position);
                    AddLog($"Added background image from {imagePath}");
                }
                else
                {
                    // Open file dialog
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Select Image",
                        Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*"
                    };
                    
                    if (dialog.ShowDialog() == true)
                    {
                        var id = $"image_{++_elementCounter}";
                        var position = GetRandomPosition();
                        var size = new SKSize(100, 100);
                        _helper.AddImageFromFile(id, dialog.FileName, position, size);
                        
                        AddElement(id, "Image", position);
                        AddLog($"Added image from {dialog.FileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"Failed to add image: {ex.Message}");
            }
        }

        private void RemoveElement_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string elementId)
            {
                try
                {
                    _renderService?.RemoveElement(elementId);
                    
                    var elementToRemove = RenderElements.FirstOrDefault(el => el.Id == elementId);
                    if (elementToRemove != null)
                    {
                        RenderElements.Remove(elementToRemove);
                    }
                    
                    UpdateElementCount();
                    AddLog($"Removed element: {elementId}");
                }
                catch (Exception ex)
                {
                    AddLog($"Failed to remove element {elementId}: {ex.Message}");
                }
            }
        }

        private void AddElement(string id, string type, SKPoint position)
        {
            RenderElements.Add(new RenderElementInfo
            {
                Id = id,
                Type = type,
                Position = $"{position.X:F0},{position.Y:F0}"
            });
            UpdateElementCount();
        }

        private void UpdateElementCount()
        {
            ElementCount = $"Elements: {RenderElements.Count}";
        }

        #endregion

        #region Canvas Settings

        private void BgColor_Click(object sender, RoutedEventArgs e)
        {
            if (_helper == null || sender is not System.Windows.Controls.Button btn || btn.Tag is not string colorName) return;

            try
            {
                var color = colorName switch
                {
                    "Black" => Colors.Black,
                    "Blue" => Colors.DarkBlue,
                    "White" => Colors.White,
                    _ => Colors.Black
                };

                _helper.SetBackgroundColor(color);
                AddLog($"Background color changed to {colorName}");
            }
            catch (Exception ex)
            {
                AddLog($"Failed to change background: {ex.Message}");
            }
        }

        private int GetSelectedCanvasSize()
        {
            var combo = GetComboBox("CanvasSizeCombo");
            return combo?.SelectedIndex switch
            {
                0 => 256,
                1 => 512,
                2 => 1024,
                _ => 512
            };
        }

        #endregion

        #region Export

        private void ExportPngBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_helper == null) return;

            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export PNG",
                    Filter = "PNG Files|*.png|All Files|*.*",
                    DefaultExt = "png",
                    FileName = $"render_demo_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                };

                if (dialog.ShowDialog() == true)
                {
                    _helper.ExportToPng(dialog.FileName);
                    var statusText = GetTextBlock("ExportStatusTxt");
                    if (statusText is not null)
                    {
                        statusText.Text = $"Exported: {Path.GetFileName(dialog.FileName)}";
                    }
                    AddLog($"Exported to: {dialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"Export failed: {ex.Message}");
                var statusText = GetTextBlock("ExportStatusTxt");
                if (statusText is not null)
                {
                    statusText.Text = "Export failed";
                }
            }
        }

        private void SaveFramesBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "RenderDemo");
                Directory.CreateDirectory(outputDir);
                
                // Save next 10 frames
                var saveBtn = GetButton("SaveFramesBtn");
                if (saveBtn is not null)
                {
                    saveBtn.IsEnabled = false;
                    saveBtn.Content = "Saving...";
                }
                
                var framesSaved = 0;
                var maxFrames = 10;
                
                EventHandler<RenderOutputEventArgs>? frameHandler = null;
                frameHandler = (s, e) =>
                {
                    try
                    {
                        var filename = Path.Combine(outputDir, $"debug_frame_{framesSaved:D3}.png");
                        File.WriteAllBytes(filename, e.FrameData);
                        framesSaved++;
                        
                        Dispatcher.Invoke(() =>
                        {
                            var statusText = GetTextBlock("ExportStatusTxt");
                            if (statusText is not null)
                            {
                                statusText.Text = $"Saved {framesSaved}/{maxFrames} frames";
                            }
                        });
                        
                        if (framesSaved >= maxFrames)
                        {
                            _renderService!.RenderOutputReady -= frameHandler;
                            Dispatcher.Invoke(() =>
                            {
                                var saveBtnFinal = GetButton("SaveFramesBtn");
                                if (saveBtnFinal is not null)
                                {
                                    saveBtnFinal.IsEnabled = true;
                                    saveBtnFinal.Content = "Save Debug Frames";
                                }
                                AddLog($"Saved {framesSaved} debug frames to {outputDir}");
                            });
                        }
                    }
                    catch { }
                };
                
                _renderService!.RenderOutputReady += frameHandler;
            }
            catch (Exception ex)
            {
                AddLog($"Debug frame save failed: {ex.Message}");
            }
        }

        #endregion

        #region Render Event Handlers

        private void OnRenderOutputReady(object? sender, RenderOutputEventArgs e)
        {
            _frameCount++;
            
            // Update preview on UI thread
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // Convert frame data to WPF bitmap
                    using var stream = new MemoryStream(e.FrameData);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    
                    var previewImage = GetImage("PreviewImage");
                    if (previewImage is not null)
                    {
                        previewImage.Source = bitmap;
                    }

                    var frameCountText = GetTextBlock("FrameCountTxt");
                    if (frameCountText is not null)
                    {
                        frameCountText.Text = _frameCount.ToString();
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"Preview update failed: {ex.Message}");
                }
            });
        }

        private void MetricsTimer_Tick(object? sender, EventArgs e)
        {
            UpdateSystemMetrics();
            _helper?.UpdateLiveData();
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
        }

        private void FpsTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            var elapsed = (now - _lastFpsUpdate).TotalSeconds;
            var fps = _frameCount / elapsed;
            
            var fpsText = GetTextBlock("FpsTxt");
            if (fpsText is not null)
            {
                fpsText.Text = $"{fps:F1}";
            }
            _frameCount = 0;
            _lastFpsUpdate = now;
        }

        private void UpdateSystemMetrics()
        {
            if (_metricsService == null) return;

            try
            {
                var cpu = _metricsService.GetCpuUsagePercent();
                var gpu = _metricsService.GetGpuUsagePercent();
                var memory = _metricsService.GetMemoryUsagePercent();

                CpuUsage = $"CPU: {cpu:F1}%";
                GpuUsage = $"GPU: {gpu:F1}%";
                MemoryUsage = $"Memory: {memory:F1}%";
            }
            catch (Exception ex)
            {
                AddLog($"Metrics update failed: {ex.Message}");
            }
        }

        #endregion

        #region Utilities

        private SKPoint GetRandomPosition()
        {
            var random = new Random();
            var canvasSize = GetSelectedCanvasSize();
            return new SKPoint(
                random.Next(50, canvasSize - 150),
                random.Next(50, canvasSize - 50)
            );
        }

        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}\n";
            
            Dispatcher.Invoke(() =>
            {
                var logOutput = GetTextBlock("LogOutput");
                var logScrollViewer = GetScrollViewer("LogScrollViewer");
                
                if (logOutput is not null)
                {
                    logOutput.Text += logEntry;
                    
                    // Keep log under 5000 characters
                    if (logOutput.Text.Length > 5000)
                    {
                        var lines = logOutput.Text.Split('\n');
                        logOutput.Text = string.Join('\n', lines.Skip(Math.Max(0, lines.Length - 50)));
                    }
                }
                
                logScrollViewer?.ScrollToEnd();
            });
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;
        
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    #region Helper Classes

    public class RenderElementInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
    }

    #endregion
}
