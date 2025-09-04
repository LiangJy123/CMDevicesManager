using CMDevicesManager.Models;
using CMDevicesManager.Services;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using WF = System.Windows.Forms;
using CMDevicesManager.Utilities;
using MessageBox = System.Windows.MessageBox;
using CMDevicesManager.Helper;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application = System.Windows.Application;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using static CMDevicesManager.Pages.DeviceConfigPage;
using ListBox = System.Windows.Controls.ListBox;

namespace CMDevicesManager.Pages
{
    // Configuration data models
    public class CanvasConfiguration
    {
        public string ConfigName { get; set; } = "Untitled";
        public int CanvasSize { get; set; }
        public string BackgroundColor { get; set; } = "#000000";
        public string? BackgroundImagePath { get; set; }
        public double BackgroundImageOpacity { get; set; }
        public List<ElementConfiguration> Elements { get; set; } = new();
    }

    public class ElementConfiguration
    {
        public string Type { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Scale { get; set; }
        public double Opacity { get; set; }
        public int ZIndex { get; set; }
        
        // Text properties
        public string? Text { get; set; }
        public double? FontSize { get; set; }
        public string? TextColor { get; set; }
        
        // Image properties
        public string? ImagePath { get; set; }
        
        // Live info properties
        public LiveInfoKind? LiveKind { get; set; }
        
        // Video properties
        public string? VideoPath { get; set; }
    }

    // Input dialog for configuration name
    public class ConfigNameDialog : Window
    {
        private TextBox _nameTextBox;
        public string ConfigName { get; private set; } = "";

        public ConfigNameDialog(string defaultName = "")
        {
            Title = Application.Current.FindResource("ConfigNameTitle")?.ToString() ?? "Configuration Name";
            Width = 400;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            
            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock 
            { 
                Text = Application.Current.FindResource("ConfigNamePrompt")?.ToString() ?? "Please enter configuration name:",
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 14
            };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            _nameTextBox = new TextBox 
            { 
                Text = defaultName,
                Margin = new Thickness(0, 0, 0, 20),
                FontSize = 14,
                Padding = new Thickness(5)
            };
            Grid.SetRow(_nameTextBox, 1);
            grid.Children.Add(_nameTextBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            
            var okButton = new Button 
            { 
                Content = Application.Current.FindResource("OkButton")?.ToString() ?? "OK",
                Width = 80, 
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) => 
            {
                ConfigName = _nameTextBox.Text?.Trim() ?? "";
                DialogResult = true;
            };
            
            var cancelButton = new Button 
            { 
                Content = Application.Current.FindResource("CancelButton")?.ToString() ?? "Cancel",
                Width = 80, 
                Height = 30,
                IsCancel = true
            };
            cancelButton.Click += (s, e) => DialogResult = false;
            
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            Content = grid;
            
            Loaded += (s, e) => 
            {
                _nameTextBox.SelectAll();
                _nameTextBox.Focus();
            };
        }
    }

    // Add this new dialog class after ConfigNameDialog
    public class ConfigSelectionDialog : Window
    {
        private ListBox _configListBox;
        public CanvasConfiguration? SelectedConfig { get; private set; }
        public string? SelectedConfigPath { get; private set; }

        public ConfigSelectionDialog(List<(string path, CanvasConfiguration config)> configs)
        {
            Title = Application.Current.FindResource("SelectConfigTitle")?.ToString() ?? "Select Configuration";
            Width = 500;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Create ListBox for configs
            _configListBox = new ListBox
            {
                Margin = new Thickness(0, 0, 0, 20),
                DisplayMemberPath = "Name",
                SelectedValuePath = "Config"
            };

            // Create items for ListBox
            var items = configs.Select(c => new
            {
                Name = $"{c.config.ConfigName} - {Path.GetFileNameWithoutExtension(c.path)}",
                Config = c.config,
                Path = c.path
            }).ToList();

            _configListBox.ItemsSource = items;
            _configListBox.MouseDoubleClick += (s, e) => 
            {
                if (_configListBox.SelectedItem != null)
                {
                    AcceptSelection();
                }
            };

            Grid.SetRow(_configListBox, 0);
            grid.Children.Add(_configListBox);

            // Button panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var loadButton = new Button
            {
                Content = Application.Current.FindResource("LoadButton")?.ToString() ?? "Load",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            loadButton.Click += (s, e) => AcceptSelection();

            var cancelButton = new Button
            {
                Content = Application.Current.FindResource("CancelButton")?.ToString() ?? "Cancel",
                Width = 80,
                Height = 30,
                IsCancel = true
            };
            cancelButton.Click += (s, e) => DialogResult = false;

            buttonPanel.Children.Add(loadButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }

        private void AcceptSelection()
        {
            if (_configListBox.SelectedItem != null)
            {
                dynamic selected = _configListBox.SelectedItem;
                SelectedConfig = selected.Config;
                SelectedConfigPath = selected.Path;
                DialogResult = true;
            }
        }
    }

    public partial class DeviceConfigPage : Page, INotifyPropertyChanged
    {
        private readonly DeviceInfos _device;

        // System info service + live text update
        private readonly ISystemMetricsService _metrics;
        private readonly DispatcherTimer _liveTimer;

        // Dynamic system info buttons
        public ObservableCollection<SystemInfoItem> SystemInfoItems { get; } = new();

        // Live text registry
        private sealed class LiveTextItem
        {
            public Border Border { get; init; } = null!;
            public TextBlock Text { get; init; } = null!;
            public LiveInfoKind Kind { get; init; }
        }
        private readonly List<LiveTextItem> _liveItems = new();

        public enum LiveInfoKind
        {
            CpuUsage,
            GpuUsage,
            DateTime,
            VideoPlayback // <- Added new enum value for video playback
        }

        // Design surface config
        private int _canvasSize = 512;
        public int CanvasSize { get => _canvasSize; set { if (_canvasSize != value && value > 0) { _canvasSize = value; OnPropertyChanged(); } } }

        // Background
        private Color _backgroundColor = Colors.Black;
        public Color BackgroundColor { get => _backgroundColor; set { _backgroundColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(BackgroundBrush)); BackgroundHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}"; } }
        public Brush BackgroundBrush => new SolidColorBrush(BackgroundColor);
        private string? _backgroundImagePath;
        public string? BackgroundImagePath { get => _backgroundImagePath; set { _backgroundImagePath = value; OnPropertyChanged(); } }
        private double _backgroundImageOpacity = 1.0;
        public double BackgroundImageOpacity { get => _backgroundImageOpacity; set { _backgroundImageOpacity = Math.Clamp(value, 0, 1); OnPropertyChanged(); } }
        private string _backgroundHex = "#000000";
        public string BackgroundHex { get => _backgroundHex; set { if (_backgroundHex != value) { _backgroundHex = value; if (TryParseHexColor(value, out var c)) BackgroundColor = c; OnPropertyChanged(); } } }

        // Selection
        private Border? _selected;
        private ScaleTransform? _selScale;
        private TranslateTransform? _selTranslate;

        // Selected meta
        private string _selectedInfo = "None";
        public string SelectedInfo { get => _selectedInfo; set { _selectedInfo = value; OnPropertyChanged(); } }
        public bool IsAnySelected => _selected != null;
        public bool IsTextSelected => _selected?.Child is TextBlock;
        public bool IsSelectedTextReadOnly => _selected?.Tag is LiveInfoKind; // live text can't edit content

        // Selected props
        private double _selectedScale = 1.0;
        public double SelectedScale { get => _selectedScale; set { _selectedScale = Math.Clamp(value, 0.1, 5); ApplySelectedScale(); ClampSelectedIntoCanvas(); OnPropertyChanged(); } }
        private double _selectedOpacity = 1.0;
        public double SelectedOpacity { get => _selectedOpacity; set { _selectedOpacity = Math.Clamp(value, 0.1, 1); if (_selected != null) _selected.Opacity = _selectedOpacity; OnPropertyChanged(); } }

        // Text props
        private string _selectedText = string.Empty;
        public string SelectedText
        {
            get => _selectedText;
            set
            {
                _selectedText = value;
                if (_selected?.Child is TextBlock tb && _selected?.Tag is not LiveInfoKind)
                {
                    tb.Text = value;
                }
                UpdateSelectedInfo();
                OnPropertyChanged();
            }
        }
        private double _selectedFontSize = 24;
        public double SelectedFontSize { get => _selectedFontSize; set { _selectedFontSize = value; if (_selected?.Child is TextBlock tb) tb.FontSize = value; OnPropertyChanged(); } }
        private Color _selectedTextColor = Colors.White;
        public Color SelectedTextColor { get => _selectedTextColor; set { _selectedTextColor = value; if (_selected?.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(value); SelectedTextHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}"; OnPropertyChanged(); } }
        private string _selectedTextHex = "#FFFFFF";
        public string SelectedTextHex { get => _selectedTextHex; set { if (_selectedTextHex != value) { _selectedTextHex = value; if (TryParseHexColor(value, out var c)) SelectedTextColor = c; OnPropertyChanged(); } } }

        // Export
        private string _outputFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CMDevicesManager");
        public string OutputFolder { get => _outputFolder; set { _outputFolder = value; OnPropertyChanged(); } }

        // Dragging
        private bool _isDragging;
        private Point _dragStart;
        private double _dragStartX, _dragStartY;

        // Video playback fields
        private DispatcherTimer? _mp4Timer;
        private List<VideoFrameData>? _currentVideoFrames;
        private int _currentFrameIndex = 0;
        private Image? _currentVideoImage;
        private Border? _currentVideoBorder;

        // Current configuration
        private string _currentConfigName = "";
        private string CurrentConfigName 
        { 
            get => _currentConfigName; 
            set 
            { 
                _currentConfigName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConfigurationDisplayName));
            }
        }

        // Property for displaying configuration status
        public string ConfigurationDisplayName 
        {
            get => string.IsNullOrWhiteSpace(CurrentConfigName) ? (Application.Current.FindResource("UnsavedConfig")?.ToString() ?? "Unsaved Configuration") : CurrentConfigName;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
            if (p is nameof(_selected))
            {
                OnPropertyChanged(nameof(IsAnySelected));
                OnPropertyChanged(nameof(IsTextSelected));
                OnPropertyChanged(nameof(IsSelectedTextReadOnly));
            }
        }

        // Add this property for the resources directory
        private string ResourcesFolder => Path.Combine(OutputFolder, "Resources");

        // Update the constructor to create resources folder
        public DeviceConfigPage(DeviceInfos device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            InitializeComponent();
            DataContext = this;

            // Initialize localized strings
            _selectedInfo = Application.Current.FindResource("None")?.ToString() ?? "None";

            // Metrics service
            _metrics = new RealSystemMetricsService();

            // Build dynamic system info buttons
            BuildSystemInfoButtons();

            // Ensure default export folder and resources folder exist
            try 
            { 
                Directory.CreateDirectory(OutputFolder); 
                Directory.CreateDirectory(ResourcesFolder);
            } 
            catch { /* ignore */ }

            // Canvas handlers
            DesignCanvas.PreviewMouseLeftButtonDown += DesignCanvas_PreviewMouseLeftButtonDown;
            DesignCanvas.PreviewMouseWheel += DesignCanvas_PreviewMouseWheel;

            // Live timer
            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _liveTimer.Tick += LiveTimer_Tick;
            _liveTimer.Start();

            Unloaded += DeviceConfigPage_Unloaded;
        }

        // Add helper method to copy file with unique name
        private string CopyResourceToAppFolder(string sourcePath, string resourceType)
        {
            try
            {
                var fileName = Path.GetFileName(sourcePath);
                var extension = Path.GetExtension(fileName);
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                
                // Create subfolder for resource type
                var typeFolder = Path.Combine(ResourcesFolder, resourceType);
                Directory.CreateDirectory(typeFolder);
                
                // Generate unique filename
                var destPath = Path.Combine(typeFolder, fileName);
                int counter = 1;
                
                while (File.Exists(destPath))
                {
                    var newName = $"{nameWithoutExtension}_{counter}{extension}";
                    destPath = Path.Combine(typeFolder, newName);
                    counter++;
                }
                
                // Copy the file
                File.Copy(sourcePath, destPath, true);
                Logger.Info($"Copied {resourceType} resource to: {destPath}");
                
                return destPath;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to copy {resourceType} resource: {ex.Message}");
                // Return original path if copy fails
                return sourcePath;
            }
        }

        // Add helper method to get relative path
        private string GetRelativePath(string fullPath)
        {
            try
            {
                // If the path is within the Resources folder, make it relative to OutputFolder
                if (fullPath.StartsWith(ResourcesFolder, StringComparison.OrdinalIgnoreCase))
                {
                    // Get path relative to OutputFolder (not including "CMDevicesManager")
                    return Path.GetRelativePath(OutputFolder, fullPath);
                }
                
                // If the path is within OutputFolder but not in Resources
                if (fullPath.StartsWith(OutputFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetRelativePath(OutputFolder, fullPath);
                }
                
                // For paths outside OutputFolder, just return the filename
                return Path.GetFileName(fullPath);
            }
            catch
            {
                // If we can't make it relative, return the file name
                return Path.GetFileName(fullPath);
            }
        }

        // Add helper method to resolve relative path
        private string ResolveRelativePath(string relativePath)
        {
            try
            {
                // If it's already an absolute path and exists, return it
                if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
                    return relativePath;

                // Try to resolve relative to OutputFolder
                var fullPath = Path.Combine(OutputFolder, relativePath);
                if (File.Exists(fullPath))
                    return fullPath;

                // Try to resolve relative to Resources folder
                fullPath = Path.Combine(ResourcesFolder, relativePath);
                if (File.Exists(fullPath))
                    return fullPath;

                // Try each resource subfolder
                foreach (var subFolder in new[] { "Images", "Videos", "Backgrounds" })
                {
                    fullPath = Path.Combine(ResourcesFolder, subFolder, relativePath);
                    if (File.Exists(fullPath))
                        return fullPath;
                }

                // Return original if we can't find it
                return relativePath;
            }
            catch
            {
                return relativePath;
            }
        }

        public void PauseVideoPlayback()
        {
            _mp4Timer?.Stop();
        }

        public void ResumeVideoPlayback()
        {
            _mp4Timer?.Start();
        }

        public void SeekVideoFrame(int frameIndex)
        {
            if (_currentVideoFrames != null && frameIndex >= 0 && frameIndex < _currentVideoFrames.Count)
            {
                _currentFrameIndex = frameIndex;
                if (_currentVideoImage != null)
                {
                    UpdateVideoFrame(_currentVideoImage, _currentVideoFrames[_currentFrameIndex]);
                }
            }
        }

        private void DeviceConfigPage_Unloaded(object sender, RoutedEventArgs e)
        {
            try { _liveTimer.Stop(); } catch { }
            try { _metrics.Dispose(); } catch { }
            StopVideoPlayback(); // Stop video playback when page unloads
        }

        private void BuildSystemInfoButtons()
        {
            SystemInfoItems.Clear();

            // CPU is generally available
            SystemInfoItems.Add(new SystemInfoItem(LiveInfoKind.CpuUsage, $"CPU Usage ({_metrics.CpuName})"));

            // GPU: add only if likely present
            var gpuName = (_metrics as RealSystemMetricsService)?.PrimaryGpuName ?? "GPU";
            var gpuVal = _metrics.GetGpuUsagePercent();
            if (!string.Equals(gpuName, "GPU", StringComparison.OrdinalIgnoreCase) || gpuVal > 0)
            {
                SystemInfoItems.Add(new SystemInfoItem(LiveInfoKind.GpuUsage, $"GPU Usage ({gpuName})"));
            }
        }

        public sealed record SystemInfoItem(LiveInfoKind Kind, string DisplayName);

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
            else NavigationService?.Navigate(new DevicePage());
        }

        // ===================== Add Date (live, read-only) =====================
        private void AddClock_Click(object sender, RoutedEventArgs e)
        {
            var textBlock = new TextBlock
            {
                Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                FontSize = 20,
                Foreground = new SolidColorBrush(Colors.Black), 
                FontWeight = FontWeights.SemiBold
            };
            // Apply current app font family
            textBlock.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

            var border = AddElement(textBlock, Application.Current.FindResource("DateTime")?.ToString() ?? "Date/Time");

            // Mark as live and register
            border.Tag = LiveInfoKind.DateTime;
            _liveItems.Add(new LiveTextItem { Border = border, Text = textBlock, Kind = LiveInfoKind.DateTime });

            // If selected now, lock editing and reflect value
            if (_selected == border)
            {
                OnPropertyChanged(nameof(IsSelectedTextReadOnly));
                _selectedText = textBlock.Text;
                OnPropertyChanged(nameof(SelectedText));
            }
        }

        // ===================== System Info click -> add live text =====================
        private void AddSystemInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not LiveInfoKind kind) return;

            var displayText = kind == LiveInfoKind.CpuUsage ? $"CPU {Math.Round(_metrics.GetCpuUsagePercent())}%" : $"GPU {Math.Round(_metrics.GetGpuUsagePercent())}%";

            var textBlock = new TextBlock
            {
                Text = displayText,
                FontSize = 18,
                Foreground = new SolidColorBrush(Colors.Black), 
                Tag = kind,
                FontWeight = FontWeights.SemiBold
            };
            // Apply current app font family
            textBlock.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

            var cpuUsageText = Application.Current.FindResource("CpuUsage")?.ToString() ?? "CPU Usage";
            var gpuUsageText = Application.Current.FindResource("GpuUsage")?.ToString() ?? "GPU Usage";
            var border = AddElement(textBlock, kind == LiveInfoKind.CpuUsage ? cpuUsageText : gpuUsageText);

            // Mark as live and register
            border.Tag = kind;
            _liveItems.Add(new LiveTextItem { Border = border, Text = textBlock, Kind = kind });

            // If selected now, make text box read-only
            if (_selected == border)
            {
                OnPropertyChanged(nameof(IsSelectedTextReadOnly));
                _selectedText = textBlock.Text; // reflect into right panel
            }
        }

        private void LiveTimer_Tick(object? sender, EventArgs e)
        {
            // Read once per tick
            double cpu = _metrics.GetCpuUsagePercent();
            double gpu = _metrics.GetGpuUsagePercent();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            foreach (var item in _liveItems.ToArray())
            {
                string text = item.Kind switch
                {
                    LiveInfoKind.CpuUsage => $"CPU {Math.Round(cpu)}%",
                    LiveInfoKind.GpuUsage => $"GPU {Math.Round(gpu)}%",
                    LiveInfoKind.DateTime => now,
                    _ => item.Text.Text
                };

                item.Text.Text = text;

                if (_selected == item.Border)
                {
                    // Update right panel display (read-only textbox)
                    _selectedText = text;
                    OnPropertyChanged(nameof(SelectedText));
                    UpdateSelectedInfo();
                }
            }
        }

        // ===================== Add / Clear =====================
        private void AddText_Click(object sender, RoutedEventArgs e)
        {
            var textBlock = new TextBlock
            {
                Text = Application.Current.FindResource("SampleText")?.ToString() ?? "Sample Text",
                FontSize = 24,
                Foreground = new SolidColorBrush(Colors.Black), 
                FontWeight = FontWeights.SemiBold
            };
            // Apply current app font family
            textBlock.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
            AddElement(textBlock, "Text");
        }

        private void AddImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                // Copy image to app folder
                var copiedPath = CopyResourceToAppFolder(dlg.FileName, "Images");
                
                var img = new Image
                {
                    Source = new BitmapImage(new Uri(copiedPath)),
                    Stretch = Stretch.Uniform
                };
                AddElement(img, System.IO.Path.GetFileName(copiedPath));
            }
        }

        private void ClearCanvas_Click(object sender, RoutedEventArgs e)
        {
            DesignCanvas.Children.Clear();
            _liveItems.Clear();
            SetSelected(null);
            CurrentConfigName = ""; // Reset configuration name
        }

        // ===================== Export =====================
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Store all borders' states
                var borderStates = new List<(Border border, Brush brush, Thickness thickness)>();
                
                // Hide all element borders before rendering
                foreach (UIElement child in DesignCanvas.Children)
                {
                    if (child is Border border)
                    {
                        borderStates.Add((border, border.BorderBrush, border.BorderThickness));
                        border.BorderBrush = Brushes.Transparent;
                        border.BorderThickness = new Thickness(0);
                    }
                }

                // Render the canvas
                var rtb = new RenderTargetBitmap(CanvasSize, CanvasSize, 96, 96, PixelFormats.Pbgra32);
                DesignRoot.Measure(new Size(CanvasSize, CanvasSize));
                DesignRoot.Arrange(new Rect(0, 0, CanvasSize, CanvasSize));
                rtb.Render(DesignRoot);

                // Restore all borders' states
                foreach (var (border, brush, thickness) in borderStates)
                {
                    border.BorderBrush = brush;
                    border.BorderThickness = thickness;
                }

                // Save the image
                Directory.CreateDirectory(OutputFolder);
                var name = (_device?.Name ?? "Device");
                var file = System.IO.Path.Combine(OutputFolder, $"{SanitizeFileName(name)}_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(file);
                encoder.Save(fs);

                System.Windows.MessageBox.Show($"Exported: {file}", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===================== Background ×=====================
        private void PickBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WF.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(BackgroundColor.A, BackgroundColor.R, BackgroundColor.G, BackgroundColor.B)
            };
            if (dlg.ShowDialog() == WF.DialogResult.OK)
            {
                BackgroundColor = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
            }
        }

        private void PickBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Background Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                // Copy background image to app folder
                var copiedPath = CopyResourceToAppFolder(dlg.FileName, "Backgrounds");
                BackgroundImagePath = copiedPath;
            }
        }

        private void ClearBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            BackgroundImagePath = null;
        }

        private void PickSelectedTextColor_Click(object sender, RoutedEventArgs e)
        {
            if (_selected?.Child is not TextBlock) return;

            using var dlg = new WF.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(SelectedTextColor.A, SelectedTextColor.R, SelectedTextColor.G, SelectedTextColor.B)
            };
            if (dlg.ShowDialog() == WF.DialogResult.OK)
            {
                SelectedTextColor = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
            }
        }

        // ===================== Z-Order / Delete =====================
        private void BringToFront_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            int max = 0;
            foreach (UIElement child in DesignCanvas.Children) max = Math.Max(max, Canvas.GetZIndex(child));
            Canvas.SetZIndex(_selected, max + 1);
        }

        private void SendToBack_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            int min = 0;
            foreach (UIElement child in DesignCanvas.Children) min = Math.Min(min, Canvas.GetZIndex(child));
            Canvas.SetZIndex(_selected, min - 1);
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;

            // Stop video playback if deleting a video element
            if (_selected == _currentVideoBorder)
            {
                StopVideoPlayback();
                _currentVideoFrames = null;
                _currentVideoImage = null;
                _currentVideoBorder = null;
            }

            // Unregister live item if any
            var live = _liveItems.FirstOrDefault(i => i.Border == _selected);
            if (live != null) _liveItems.Remove(live);

            DesignCanvas.Children.Remove(_selected);
            SetSelected(null);
        }

        private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WF.FolderBrowserDialog
            {
                Description = "Select output folder",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(OutputFolder) ? OutputFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };
            if (dlg.ShowDialog() == WF.DialogResult.OK)
            {
                OutputFolder = dlg.SelectedPath;
            }
        }

        // ===================== Element creation / selection =====================
        private Border AddElement(FrameworkElement element, string label)
        {
            element.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            element.VerticalAlignment = VerticalAlignment.Top;

            var border = new Border
            {
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Child = element,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Cursor = Cursors.SizeAll
            };

            var tg = new TransformGroup();
            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform(0, 0);
            tg.Children.Add(scale);
            tg.Children.Add(translate);
            border.RenderTransform = tg;

            border.PreviewMouseLeftButtonDown += Item_PreviewMouseLeftButtonDown;
            border.PreviewMouseLeftButtonUp += Item_PreviewMouseLeftButtonUp;
            border.PreviewMouseMove += Item_PreviewMouseMove;
            border.MouseDown += (_, __) => SelectElement(border);
            border.MouseEnter += (_, __) => { if (_selected != border) border.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 30, 144, 255)); };
            border.MouseLeave += (_, __) => { if (_selected != border) border.BorderBrush = Brushes.Transparent; };

            DesignCanvas.Children.Add(border);
            border.Loaded += (_, __) =>
            {
                SelectElement(border);
                SelectedOpacity = 1.0;

                double w = border.ActualWidth;
                double h = border.ActualHeight;

                if (GetTransforms(border, out var sc, out var tr))
                {
                    if (element is Image)
                    {
                        // Cover canvas & center
                        double scaleToCover = (w > 0 && h > 0) ? Math.Max(CanvasSize / w, CanvasSize / h) : 1.0;
                        sc.ScaleX = sc.ScaleY = scaleToCover;
                        SelectedScale = scaleToCover;

                        double scaledW = w * scaleToCover;
                        double scaledH = h * scaleToCover;
                        tr.X = (CanvasSize - scaledW) / 2.0;
                        tr.Y = (CanvasSize - scaledH) / 2.0;
                    }
                    else
                    {
                        SelectedScale = 1.0;
                        tr.X = (CanvasSize - w) / 2.0;
                        tr.Y = (CanvasSize - h) / 2.0;
                    }
                }

                ClampIntoCanvas(border);
                UpdateSelectedInfo();
            };

            return border;
        }

        private void SelectElement(Border border)
        {
            if (_selected != null && _selected != border)
            {
                _selected.BorderBrush = Brushes.Transparent;
            }

            _selected = border;
            _selected.BorderBrush = Brushes.DodgerBlue;

            if (GetTransforms(border, out var scale, out var translate))
            {
                _selScale = scale;
                _selTranslate = translate;
            }

            SelectedOpacity = _selected.Opacity;
            SelectedScale = _selScale?.ScaleX ?? 1.0;

            if (_selected.Child is TextBlock tb)
            {
                SelectedText = tb.Text;
                SelectedFontSize = tb.FontSize;
                if (tb.Foreground is SolidColorBrush scb)
                {
                    var c = scb.Color;
                    SelectedTextColor = Color.FromArgb(c.A, c.R, c.G, c.B);
                }
                else
                {
                    SelectedTextColor = Colors.White;
                }
            }

            UpdateSelectedInfo();
            OnPropertyChanged(nameof(_selected));
        }

        private void SetSelected(Border? border)
        {
            if (_selected != null)
            {
                _selected.BorderBrush = Brushes.Transparent;
            }
            _selected = border;
            if (_selected != null) _selected.BorderBrush = Brushes.DodgerBlue;

            if (_selected != null && GetTransforms(_selected, out var sc, out var tr))
            {
                _selScale = sc;
                _selTranslate = tr;
                SelectedOpacity = _selected.Opacity;
                SelectedScale = _selScale.ScaleX;
            }
            else
            {
                _selScale = null;
                _selTranslate = null;
                SelectedInfo = Application.Current.FindResource("None")?.ToString() ?? "None";
            }

            OnPropertyChanged(nameof(_selected));
        }

        private void UpdateSelectedInfo()
        {
            if (_selected == null)
            {
                SelectedInfo = Application.Current.FindResource("None")?.ToString() ?? "None";
                return;
            }
            
            if (_selected.Tag is VideoElementInfo videoInfo)
            {
                SelectedInfo = $"Video: Frame {_currentFrameIndex + 1}/{videoInfo.TotalFrames}";
            }
            else if (_selected.Child is TextBlock)
            {
                if (_selected.Tag is LiveInfoKind k)
                {
                    SelectedInfo = k switch
                    {
                        LiveInfoKind.CpuUsage => Application.Current.FindResource("CpuUsage")?.ToString() ?? "CPU Usage",
                        LiveInfoKind.GpuUsage => Application.Current.FindResource("GpuUsage")?.ToString() ?? "GPU Usage",
                        LiveInfoKind.DateTime => Application.Current.FindResource("DateTime")?.ToString() ?? "Date/Time",
                        _ => Application.Current.FindResource("LiveText")?.ToString() ?? "Live Text"
                    };
                }
                else
                {
                    SelectedInfo = $"Text: \"{SelectedText}\"";
                }
            }
            else if (_selected.Child is Image img)
            {
                var src = img.Source as BitmapSource;
                SelectedInfo = src != null ? $"Image: {src.PixelWidth}x{src.PixelHeight}" : "Image";
            }
            else
            {
                SelectedInfo = "Element";
            }
        }

        private static bool GetTransforms(Border border, out ScaleTransform scale, out TranslateTransform translate)
        {
            if (border.RenderTransform is TransformGroup tg)
            {
                scale = tg.Children.OfType<ScaleTransform>().FirstOrDefault() ?? new ScaleTransform(1, 1);
                translate = tg.Children.OfType<TranslateTransform>().FirstOrDefault() ?? new TranslateTransform(0, 0);
                return true;
            }
            scale = new ScaleTransform(1, 1);
            translate = new TranslateTransform(0, 0);
            return false;
        }

        // ===================== Dragging & Canvas constraints =====================
        private void Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border b) return;
            SelectElement(b);

            if (GetTransforms(b, out _, out var tr))
            {
                _isDragging = true;
                _dragStart = e.GetPosition(DesignCanvas);
                _dragStartX = tr.X;
                _dragStartY = tr.Y;
                b.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Item_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _selected == null || _selTranslate == null) return;

            var p = e.GetPosition(DesignCanvas);
            var dx = p.X - _dragStart.X;
            var dy = p.Y - _dragStart.Y;

            var newX = _dragStartX + dx;
            var newY = _dragStartY + dy;

            (newX, newY) = GetClampedPosition(_selected, newX, newY);

            _selTranslate.X = newX;
            _selTranslate.Y = newY;
            e.Handled = true;
        }

        private void Item_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b)
            {
                _isDragging = false;
                b.ReleaseMouseCapture();
                ClampIntoCanvas(b);
                e.Handled = true;
            }
        }

        private void DesignCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source == DesignCanvas)
            {
                SetSelected(null);
            }
        }

        private void DesignCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (_selected == null) return;

            var delta = e.Delta > 0 ? 0.05 : -0.05;
            SelectedScale = Math.Clamp(SelectedScale + delta, 0.1, 5.0);
            e.Handled = true;
        }

        private (double X, double Y) GetClampedPosition(Border border, double x, double y)
        {
            double scaledW = border.ActualWidth * (_selScale?.ScaleX ?? 1.0);
            double scaledH = border.ActualHeight * (_selScale?.ScaleY ?? 1.0);

            // Allow negative offsets when element is larger than the canvas
            double minX = Math.Min(0, CanvasSize - scaledW);
            double maxX = Math.Max(0, CanvasSize - scaledW);
            double minY = Math.Min(0, CanvasSize - scaledH);
            double maxY = Math.Max(0, CanvasSize - scaledH);

            x = Math.Clamp(x, minX, maxX);
            y = Math.Clamp(y, minY, maxY);
            return (x, y);
        }

        private void ClampIntoCanvas(Border border)
        {
            if (!GetTransforms(border, out _, out var tr)) return;
            (tr.X, tr.Y) = GetClampedPosition(border, tr.X, tr.Y);
        }

        private void ClampSelectedIntoCanvas()
        {
            if (_selected == null) return;
            ClampIntoCanvas(_selected);
        }

        // ===================== Helpers =====================
        private static bool TryParseHexColor(string? hex, out Color color)
        {
            color = Colors.Transparent;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            hex = hex.Trim();
            if (hex.StartsWith("#")) hex = hex[1..];

            try
            {
                if (hex.Length == 6)
                {
                    byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    color = Color.FromRgb(r, g, b);
                    return true;
                }
                if (hex.Length == 8)
                {
                    byte a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    byte r = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
            }
            catch { /* ignore */ }

            return false;
        }

        private void ApplySelectedScale()
        {
            if (_selScale != null)
            {
                _selScale.ScaleX = _selectedScale;
                _selScale.ScaleY = _selectedScale;
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        // ===================== Video Playback =====================
        private async void AddMp4_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select MP4 Video",
                Filter = "MP4 Files|*.mp4|All Files|*.*"
            };
            
            if (dlg.ShowDialog() != true)
                return;

            try
            {
                // Copy video to app folder first
                Mouse.OverrideCursor = Cursors.Wait;
                var copiedPath = CopyResourceToAppFolder(dlg.FileName, "Videos");
                
                // Get video information
                var videoInfo = await VideoConverter.GetMp4InfoAsync(copiedPath);
                if (videoInfo == null)
                {
                    MessageBox.Show("Failed to read video information", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Extract all frames to memory
                var frames = await ExtractMp4FramesToMemory(copiedPath);
                if (frames == null || frames.Count == 0)
                {
                    MessageBox.Show("Failed to extract video frames", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _currentVideoFrames = frames;
                _currentFrameIndex = 0;

                // Create image element for video display
                var image = new Image
                {
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                };
                Mouse.OverrideCursor = Cursors.Arrow;
                // Display first frame
                UpdateVideoFrame(image, _currentVideoFrames[0]);

                // Add to canvas
                var border = AddElement(image, $"MP4: {System.IO.Path.GetFileName(copiedPath)}");
                
                // Store references
                _currentVideoImage = image;
                _currentVideoBorder = border;

                // Mark as video element with copied path
                border.Tag = new VideoElementInfo 
                { 
                    Kind = LiveInfoKind.VideoPlayback,
                    VideoInfo = videoInfo,
                    FilePath = copiedPath, // Use the copied path
                    TotalFrames = frames.Count
                };

                // Start frame timer
                StartVideoPlayback(videoInfo.FrameRate);

                // Update UI
                if (_selected == border)
                {
                    OnPropertyChanged(nameof(IsSelectedTextReadOnly));
                    UpdateSelectedInfo();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load MP4: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private sealed class VideoElementInfo
        {
            public LiveInfoKind Kind { get; init; }
            public VideoInfo? VideoInfo { get; init; }
            public string FilePath { get; init; } = string.Empty;
            public int TotalFrames { get; init; }
        }

        private async Task<List<VideoFrameData>> ExtractMp4FramesToMemory(string mp4Path)
        {
            var frames = new List<VideoFrameData>();
            
            try
            {
                await foreach (var frame in VideoConverter.ExtractMp4FramesToJpegRealTimeAsync(
                    mp4Path, 
                    quality: 85,
                    CancellationToken.None))
                {
                    frames.Add(frame);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to extract MP4 frames: {ex.Message}");
                return new List<VideoFrameData>();
            }

            return frames;
        }

        private void UpdateVideoFrame(Image image, VideoFrameData frameData)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                
                using (var stream = new System.IO.MemoryStream(frameData.JpegData))
                {
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                
                bitmap.Freeze();
                image.Source = bitmap;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to update video frame: {ex.Message}");
            }
        }

        private void StartVideoPlayback(double frameRate)
        {
            StopVideoPlayback();
            
            int intervalMs = (int)(1000.0 / frameRate);
            _mp4Timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs)
            };
            
            _mp4Timer.Tick += Mp4Timer_Tick;
            _mp4Timer.Start();
        }

        private void StopVideoPlayback()
        {
            if (_mp4Timer != null)
            {
                _mp4Timer.Stop();
                _mp4Timer.Tick -= Mp4Timer_Tick;
                _mp4Timer = null;
            }
        }

        private void Mp4Timer_Tick(object? sender, EventArgs e)
        {
            if (_currentVideoFrames == null || _currentVideoImage == null)
            {
                StopVideoPlayback();
                return;
            }

            // Move to next frame
            _currentFrameIndex++;
            if (_currentFrameIndex >= _currentVideoFrames.Count)
            {
                _currentFrameIndex = 0; // Loop back to start
            }

            // Update displayed frame
            UpdateVideoFrame(_currentVideoImage, _currentVideoFrames[_currentFrameIndex]);

            // Update info if this video is selected
            if (_selected == _currentVideoBorder)
            {
                UpdateSelectedInfo();
            }
        }

        // Configuration Save/Load Methods
        // Update SaveConfig_Click to use timestamp-based filename
        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ask for config name if not set
                if (string.IsNullOrWhiteSpace(CurrentConfigName))
                {
                    var dialog = new ConfigNameDialog();
                    if (Application.Current.MainWindow != null)
                    {
                        dialog.Owner = Application.Current.MainWindow;
                    }
                    
                    if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ConfigName))
                    {
                        return;
                    }
                    CurrentConfigName = dialog.ConfigName;
                }

                var config = new CanvasConfiguration
                {
                    ConfigName = CurrentConfigName,
                    CanvasSize = CanvasSize,
                    BackgroundColor = BackgroundHex,
                    BackgroundImagePath = BackgroundImagePath != null ? GetRelativePath(BackgroundImagePath) : null,
                    BackgroundImageOpacity = BackgroundImageOpacity
                };

                // Save all canvas elements
                foreach (UIElement child in DesignCanvas.Children)
                {
                    if (child is Border border && GetTransforms(border, out var scale, out var translate))
                    {
                        var elemConfig = new ElementConfiguration
                        {
                            X = translate.X,
                            Y = translate.Y,
                            Scale = scale.ScaleX,
                            Opacity = border.Opacity,
                            ZIndex = Canvas.GetZIndex(border)
                        };

                        // Handle different element types
                        if (border.Child is TextBlock tb)
                        {
                            elemConfig.Type = "Text";
                            elemConfig.Text = tb.Text;
                            elemConfig.FontSize = tb.FontSize;
                            
                            if (tb.Foreground is SolidColorBrush brush)
                            {
                                var color = brush.Color;
                                elemConfig.TextColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                            }

                            // Handle live info
                            if (border.Tag is LiveInfoKind liveKind)
                            {
                                elemConfig.Type = "LiveText";
                                elemConfig.LiveKind = liveKind;
                            }
                        }
                        else if (border.Child is Image img)
                        {
                            if (border.Tag is VideoElementInfo videoInfo)
                            {
                                elemConfig.Type = "Video";
                                elemConfig.VideoPath = GetRelativePath(videoInfo.FilePath);
                            }
                            else if (img.Source is BitmapImage bitmapImg)
                            {
                                var imagePath = bitmapImg.UriSource?.LocalPath ?? bitmapImg.UriSource?.ToString();
                                if (!string.IsNullOrEmpty(imagePath))
                                {
                                    elemConfig.Type = "Image";
                                    elemConfig.ImagePath = GetRelativePath(imagePath);
                                }
                            }
                        }

                        config.Elements.Add(elemConfig);
                    }
                }

                // Save to file with timestamp-based filename
                var configFolder = Path.Combine(OutputFolder, "Configs");
                Directory.CreateDirectory(configFolder);
                
                // Use timestamp for filename to avoid conflicts and special characters
                var fileName = $"config_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var filePath = Path.Combine(configFolder, fileName);
                
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                });
                
                File.WriteAllText(filePath, json);
                
                var configSavedMsg = Application.Current.FindResource("ConfigSaved")?.ToString() ?? "Configuration saved";
                var saveSuccessfulMsg = Application.Current.FindResource("SaveSuccessful")?.ToString() ?? "Save Successful";
                MessageBox.Show($"{configSavedMsg}: {CurrentConfigName}", saveSuccessfulMsg, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var errorMsg = Application.Current.FindResource("Error")?.ToString() ?? "Error";
                MessageBox.Show($"保存配置失败: {ex.Message}", errorMsg, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LoadConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var configFolder = Path.Combine(OutputFolder, "Configs");
                if (!Directory.Exists(configFolder))
                {
                    Directory.CreateDirectory(configFolder);
                }

                // Load all config files
                var configFiles = Directory.GetFiles(configFolder, "*.json");
                if (configFiles.Length == 0)
                {
                    var noConfigMsg = Application.Current.FindResource("NoConfigFilesFound")?.ToString() ?? "No configuration files found";
                    var noticeMsg = Application.Current.FindResource("Notice")?.ToString() ?? "Notice";
                    MessageBox.Show(noConfigMsg, noticeMsg, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Parse all configs
                var configs = new List<(string path, CanvasConfiguration config)>();
                foreach (var file in configFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var config_tmp = JsonSerializer.Deserialize<CanvasConfiguration>(json, new JsonSerializerOptions
                        {
                            Converters = { new JsonStringEnumConverter() }
                        });

                        if (config_tmp != null)
                        {
                            configs.Add((file, config_tmp));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"Failed to load config file {file}: {ex.Message}");
                    }
                }

                if (configs.Count == 0)
                {
                    var noValidConfigMsg = Application.Current.FindResource("NoValidConfigFilesFound")?.ToString() ?? "No valid configuration files found";
                    var noticeMsg = Application.Current.FindResource("Notice")?.ToString() ?? "Notice";
                    MessageBox.Show(noValidConfigMsg, noticeMsg, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Show selection dialog
                var selectionDialog = new ConfigSelectionDialog(configs);
                if (Application.Current.MainWindow != null)
                {
                    selectionDialog.Owner = Application.Current.MainWindow;
                }

                if (selectionDialog.ShowDialog() != true || selectionDialog.SelectedConfig == null)
                {
                    return;
                }

                var config = selectionDialog.SelectedConfig;

                // Clear current canvas
                DesignCanvas.Children.Clear();
                _liveItems.Clear();
                SetSelected(null);
                StopVideoPlayback();

                // Apply configuration
                CurrentConfigName = config.ConfigName;
                CanvasSize = config.CanvasSize;
                BackgroundHex = config.BackgroundColor;
                
                // Resolve relative path for background image
                if (!string.IsNullOrEmpty(config.BackgroundImagePath))
                {
                    BackgroundImagePath = ResolveRelativePath(config.BackgroundImagePath);
                }
                else
                {
                    BackgroundImagePath = null;
                }
                
                BackgroundImageOpacity = config.BackgroundImageOpacity;

                // Restore elements
                foreach (var elemConfig in config.Elements.OrderBy(e => e.ZIndex))
                {
                    FrameworkElement? element = null;
                    Border? border = null;
                    
                    switch (elemConfig.Type)
                    {
                        case "Text":
                            var textBlock = new TextBlock
                            {
                                Text = elemConfig.Text ?? "Text",
                                FontSize = elemConfig.FontSize ?? 24,
                                FontWeight = FontWeights.SemiBold
                            };
                            textBlock.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
                            
                            if (!string.IsNullOrEmpty(elemConfig.TextColor) && TryParseHexColor(elemConfig.TextColor, out var textColor))
                            {
                                textBlock.Foreground = new SolidColorBrush(textColor);
                            }
                            else
                            {
                                textBlock.Foreground = new SolidColorBrush(Colors.Black);
                            }
                            
                            element = textBlock;
                            break;
                            
                        case "LiveText":
                            if (elemConfig.LiveKind.HasValue)
                            {
                                // Create live text manually without using Add methods
                                var liveTextBlock = new TextBlock
                                {
                                    FontSize = elemConfig.FontSize ?? 20,
                                    FontWeight = FontWeights.SemiBold
                                };
                                liveTextBlock.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
                                
                                if (!string.IsNullOrEmpty(elemConfig.TextColor) && TryParseHexColor(elemConfig.TextColor, out var liveTextColor))
                                {
                                    liveTextBlock.Foreground = new SolidColorBrush(liveTextColor);
                                }
                                else
                                {
                                    liveTextBlock.Foreground = new SolidColorBrush(Colors.Black);
                                }
                                
                                // Set initial text based on type
                                switch (elemConfig.LiveKind.Value)
                                {
                                    case LiveInfoKind.DateTime:
                                        liveTextBlock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                                        break;
                                    case LiveInfoKind.CpuUsage:
                                        liveTextBlock.Text = $"CPU {Math.Round(_metrics.GetCpuUsagePercent())}%";
                                        liveTextBlock.Tag = LiveInfoKind.CpuUsage;
                                        break;
                                    case LiveInfoKind.GpuUsage:
                                        liveTextBlock.Text = $"GPU {Math.Round(_metrics.GetGpuUsagePercent())}%";
                                        liveTextBlock.Tag = LiveInfoKind.GpuUsage;
                                        break;
                                }
                                
                                element = liveTextBlock;
                            }
                            break;
                            
                        case "Image":
                            if (!string.IsNullOrEmpty(elemConfig.ImagePath))
                            {
                                var resolvedPath = ResolveRelativePath(elemConfig.ImagePath);
                                if (File.Exists(resolvedPath))
                                {
                                    try
                                    {
                                        var bitmap = new BitmapImage();
                                        bitmap.BeginInit();
                                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                        bitmap.UriSource = new Uri(resolvedPath, UriKind.Absolute);
                                        bitmap.EndInit();
                                        bitmap.Freeze();
                                        
                                        var img = new Image
                                        {
                                            Source = bitmap,
                                            Stretch = Stretch.Uniform
                                        };
                                        element = img;
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Error($"Failed to load image from {resolvedPath}: {ex.Message}");
                                    }
                                }
                                else
                                {
                                    Logger.Info($"Image file not found: {resolvedPath}");
                                }
                            }
                            break;
                            
                        case "Video":
                            if (!string.IsNullOrEmpty(elemConfig.VideoPath))
                            {
                                var resolvedPath = ResolveRelativePath(elemConfig.VideoPath);
                                if (File.Exists(resolvedPath))
                                {
                                    // Manually trigger video loading for the stored path
                                    var videoInfo = await VideoConverter.GetMp4InfoAsync(resolvedPath);
                                    if (videoInfo != null)
                                    {
                                        Mouse.OverrideCursor = Cursors.Wait;
                                        var frames = await ExtractMp4FramesToMemory(resolvedPath);
                                        Mouse.OverrideCursor = null;
                                        
                                        if (frames != null && frames.Count > 0)
                                        {
                                            _currentVideoFrames = frames;
                                            _currentFrameIndex = 0;

                                            var videoImage = new Image
                                            {
                                                Stretch = Stretch.Uniform,
                                                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                                                VerticalAlignment = VerticalAlignment.Top
                                            };
                                            
                                            UpdateVideoFrame(videoImage, frames[0]);
                                            element = videoImage;
                                        }
                                    }
                                }
                                else
                                {
                                    Logger.Info($"Video file not found: {resolvedPath}");
                                }
                            }
                            break;
                    }

                    if (element != null)
                    {
                        // Create border manually to control position
                        element.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                        element.VerticalAlignment = VerticalAlignment.Top;

                        border = new Border
                        {
                            BorderBrush = Brushes.Transparent,
                            BorderThickness = new Thickness(0),
                            Child = element,
                            RenderTransformOrigin = new Point(0.5, 0.5),
                            Cursor = Cursors.SizeAll,
                            Opacity = elemConfig.Opacity
                        };

                        // Apply saved transforms
                        var tg = new TransformGroup();
                        var scale = new ScaleTransform(elemConfig.Scale, elemConfig.Scale);
                        var translate = new TranslateTransform(elemConfig.X, elemConfig.Y);
                        tg.Children.Add(scale);
                        tg.Children.Add(translate);
                        border.RenderTransform = tg;

                        // Set up event handlers
                        border.PreviewMouseLeftButtonDown += Item_PreviewMouseLeftButtonDown;
                        border.PreviewMouseLeftButtonUp += Item_PreviewMouseLeftButtonUp;
                        border.PreviewMouseMove += Item_PreviewMouseMove;
                        border.MouseDown += (_, __) => SelectElement(border);
                        border.MouseEnter += (_, __) => { if (_selected != border) border.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 30, 144, 255)); };
                        border.MouseLeave += (_, __) => { if (_selected != border) border.BorderBrush = Brushes.Transparent; };

                        // Add to canvas and set Z-index
                        DesignCanvas.Children.Add(border);
                        Canvas.SetZIndex(border, elemConfig.ZIndex);
                        
                        // Handle live text registration
                        if (elemConfig.Type == "LiveText" && elemConfig.LiveKind.HasValue)
                        {
                            border.Tag = elemConfig.LiveKind.Value;
                            _liveItems.Add(new LiveTextItem 
                            { 
                                Border = border, 
                                Text = (TextBlock)element, 
                                Kind = elemConfig.LiveKind.Value 
                            });
                        }
                        
                        // Handle video setup
                        if (elemConfig.Type == "Video" && element is Image videoImg)
                        {
                            _currentVideoImage = videoImg;
                            _currentVideoBorder = border;
                            
                            var resolvedVideoPath = ResolveRelativePath(elemConfig.VideoPath!);
                            var videoInfo = await VideoConverter.GetMp4InfoAsync(resolvedVideoPath);
                            if (videoInfo != null)
                            {
                                border.Tag = new VideoElementInfo
                                {
                                    Kind = LiveInfoKind.VideoPlayback,
                                    VideoInfo = videoInfo,
                                    FilePath = resolvedVideoPath,
                                    TotalFrames = _currentVideoFrames?.Count ?? 0
                                };
                                
                                StartVideoPlayback(videoInfo.FrameRate);
                            }
                        }
                    }
                }

                var configLoadedMsg = Application.Current.FindResource("ConfigLoaded")?.ToString() ?? "Configuration loaded";
                var loadSuccessfulMsg = Application.Current.FindResource("LoadSuccessful")?.ToString() ?? "Load Successful";
                MessageBox.Show($"{configLoadedMsg}: {config.ConfigName}", loadSuccessfulMsg, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var loadFailedMsg = Application.Current.FindResource("LoadConfigFailed")?.ToString() ?? "Failed to load configuration";
                var errorMsg = Application.Current.FindResource("Error")?.ToString() ?? "Error";
                MessageBox.Show($"{loadFailedMsg}: {ex.Message}", errorMsg, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}