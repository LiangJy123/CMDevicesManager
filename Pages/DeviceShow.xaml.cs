using Microsoft.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Path = System.IO.Path;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for DeviceShow.xaml
    /// </summary>
    public partial class DeviceShow : Page, INotifyPropertyChanged
    {
        private readonly string? _appFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        private Dictionary<string, SceneInfo> _scens = new();

        // Observable collection for data binding
        public ObservableCollection<SceneInfo> Scenes { get; } = new ObservableCollection<SceneInfo>();

        public DeviceShow()
        {
            InitializeComponent();
            DataContext = this; // Set DataContext for binding
            this.Loaded += DeviceShow_Loaded;
        }

        private void DeviceShow_Loaded(object sender, RoutedEventArgs e)
        {
            // Get all scenes from the Scenes folder: ap
            var scenesFolder = Path.Combine(_appFolder ?? string.Empty, "Scenes");

            _scens = GetScenesWithJsonFiles(scenesFolder);
            
            // Populate the observable collection for data binding
            Scenes.Clear();
            foreach (var scene in _scens.Values)
            {
                Scenes.Add(scene);
            }
            
            Console.WriteLine($"Total scenes with JSON files: {_scens.Count}");
        }

        /// <summary>
        /// Event handler for Edit button click - navigates to DesignerPage to edit the selected scene
        /// </summary>
        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button btn && btn.DataContext is SceneInfo sceneInfo)
                {
                    // Construct the full path to the scene JSON file
                    var sceneFilePath = Path.Combine(_appFolder ?? string.Empty, "Scenes", sceneInfo.SceneId, sceneInfo.SceneFileName);
                    
                    if (File.Exists(sceneFilePath))
                    {
                        // Navigate to DesignerPage with the JSON file path for automatic loading
                        var designerPage = new DesignerPage(sceneFilePath);
                        NavigationService?.Navigate(designerPage);
                        
                        Debug.WriteLine($"Navigating to DesignerPage to edit scene: {sceneInfo.DisplayName} (ID: {sceneInfo.SceneId})");
                        Debug.WriteLine($"Scene file path: {sceneFilePath}");
                    }
                    else
                    {
                        // File not found - show error and navigate to empty designer
                        var errorMessage = $"Scene file not found: {sceneInfo.SceneFileName}\n\nWould you like to open the Designer anyway?";
                        var result = System.Windows.MessageBox.Show(errorMessage, "File Not Found", 
                            MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        
                        if (result == MessageBoxResult.Yes)
                        {
                            var designerPage = new DesignerPage();
                            NavigationService?.Navigate(designerPage);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Navigation to DesignerPage failed: {ex}");
                System.Windows.MessageBox.Show($"Failed to open scene for editing: {ex.Message}", "Navigation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Event handler for Load button click - loads/plays the selected scene
        /// </summary>
        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button btn && btn.DataContext is SceneInfo sceneInfo)
                {
                    Debug.WriteLine($"Loading scene: {sceneInfo.DisplayName} (ID: {sceneInfo.SceneId})");
                    
                    // For now, show a message indicating the scene would be loaded
                    // This could be extended to actually load the scene into a play mode or device
                    System.Windows.MessageBox.Show($"Loading scene: {sceneInfo.DisplayName}\n\nThis feature will load the scene for playback or device display.", 
                        "Load Scene", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // TODO: Implement actual scene loading logic here
                    // This might involve:
                    // - Loading the scene into a rendering service
                    // - Sending the scene data to connected devices
                    // - Starting playback mode
                    // - Navigating to a scene playback page
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load scene: {ex}");
                System.Windows.MessageBox.Show($"Failed to load scene: {ex.Message}", "Load Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private Dictionary<string, SceneInfo> GetScenesWithJsonFiles(string scenesFolder)
        {
            var scenes = new Dictionary<string, SceneInfo>();

            try
            {
                if (!Directory.Exists(scenesFolder))
                {
                    Console.WriteLine($"Scenes folder not found: {scenesFolder}");
                    return scenes;
                }

                var sceneDirectories = Directory.GetDirectories(scenesFolder);

                foreach (var sceneDir in sceneDirectories)
                {
                    try
                    {
                        var sceneId = Path.GetFileName(sceneDir);
                        var jsonFiles = Directory.GetFiles(sceneDir, "*.json", SearchOption.TopDirectoryOnly);

                        if (jsonFiles.Length > 0)
                        {
                            // Parse the first JSON file found in the scene directory
                            var sceneInfo = ParseSceneJsonFile(jsonFiles[0], sceneId, sceneDir);
                            if (sceneInfo != null)
                            {
                                scenes[sceneId] = sceneInfo;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing scene directory {sceneDir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing scenes folder {scenesFolder}: {ex.Message}");
            }

            return scenes;
        }

        private SceneInfo? ParseSceneJsonFile(string jsonFilePath, string directorySceneId, string sceneDirectory)
        {
            try
            {
                if (!File.Exists(jsonFilePath))
                {
                    Console.WriteLine($"JSON file not found: {jsonFilePath}");
                    return null;
                }

                var jsonContent = File.ReadAllText(jsonFilePath);

                // Parse JSON using System.Text.Json
                using var document = JsonDocument.Parse(jsonContent);
                var root = document.RootElement;

                var sceneInfo = new SceneInfo
                {
                    SceneId = directorySceneId // Use directory name as fallback
                };

                // Parse required fields from JSON
                if (root.TryGetProperty("sceneName", out var sceneNameElement))
                {
                    sceneInfo.SceneName = sceneNameElement.GetString() ?? "Unknown Scene";
                }

                if (root.TryGetProperty("sceneId", out var sceneIdElement))
                {
                    var jsonSceneId = sceneIdElement.GetString();
                    if (!string.IsNullOrEmpty(jsonSceneId))
                    {
                        sceneInfo.SceneId = jsonSceneId; // Override with JSON sceneId if available
                    }
                }

                if (root.TryGetProperty("coverImagePath", out var coverImagePathElement))
                {
                    var coverImagePath = coverImagePathElement.GetString();
                    if (!string.IsNullOrEmpty(coverImagePath))
                    {
                        // Convert relative path to absolute path
                        sceneInfo.CoverImagePath = Path.IsPathRooted(coverImagePath)
                            ? coverImagePath
                            : Path.Combine(_appFolder ?? string.Empty, coverImagePath);
                    }
                }

                if (root.TryGetProperty("sceneFileName", out var sceneFileNameElement))
                {
                    sceneInfo.SceneFileName = sceneFileNameElement.GetString() ?? Path.GetFileName(jsonFilePath);
                }

                return sceneInfo;
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"JSON parsing error in file {jsonFilePath}: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing scene JSON file {jsonFilePath}: {ex.Message}");
            }

            return null;
        }

        private List<string> GetAllSceneJsonFiles(string scenesFolder)
        {
            var jsonFiles = new List<string>();

            try
            {
                if (!Directory.Exists(scenesFolder))
                {
                    Console.WriteLine($"Scenes folder not found: {scenesFolder}");
                    return jsonFiles;
                }

                // Get all subdirectories in the Scenes folder (each represents a scene)
                var sceneDirectories = Directory.GetDirectories(scenesFolder);

                foreach (var sceneDir in sceneDirectories)
                {
                    try
                    {
                        // Look for JSON files in each scene directory root
                        var jsonFilesInScene = Directory.GetFiles(sceneDir, "*.json", SearchOption.TopDirectoryOnly);
                        jsonFiles.AddRange(jsonFilesInScene);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reading JSON files from scene directory {sceneDir}: {ex.Message}");
                    }
                }

                Console.WriteLine($"Found {jsonFiles.Count} JSON files in scenes folder");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing scenes folder {scenesFolder}: {ex.Message}");
            }

            return jsonFiles;
        }

        private void ProcessSceneJsonFiles(List<string> jsonFiles)
        {
            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    // Get the scene directory name (GUID)
                    var sceneDirectory = Path.GetDirectoryName(jsonFile);
                    var sceneId = Path.GetFileName(sceneDirectory);
                    var fileName = Path.GetFileName(jsonFile);

                    Console.WriteLine($"Scene: {sceneId}");
                    Console.WriteLine($"  JSON File: {fileName}");
                    Console.WriteLine($"  Full Path: {jsonFile}");

                    // Read and process JSON content if needed
                    if (File.Exists(jsonFile))
                    {
                        var jsonContent = File.ReadAllText(jsonFile);
                        Console.WriteLine($"  File Size: {new FileInfo(jsonFile).Length} bytes");

                        // You can deserialize the JSON here if needed
                        // var sceneData = System.Text.Json.JsonSerializer.Deserialize<YourSceneModel>(jsonContent);
                    }

                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing JSON file {jsonFile}: {ex.Message}");
                }
            }
        }

        // Enhanced helper class to hold comprehensive scene information
        public class SceneInfo : INotifyPropertyChanged
        {
            private string _sceneId = string.Empty;
            private string _sceneName = string.Empty;
            private string _coverImagePath = string.Empty;
            private string _sceneFileName = string.Empty;

            // Required fields from JSON
            public string SceneId 
            { 
                get => _sceneId;
                set
                {
                    _sceneId = value;
                    OnPropertyChanged();
                }
            }
            
            public string SceneName 
            { 
                get => _sceneName;
                set
                {
                    _sceneName = value;
                    OnPropertyChanged();
                }
            }
            
            public string CoverImagePath 
            { 
                get => _coverImagePath;
                set
                {
                    _coverImagePath = value;
                    OnPropertyChanged();
                }
            }
            
            public string SceneFileName 
            { 
                get => _sceneFileName;
                set
                {
                    _sceneFileName = value;
                    OnPropertyChanged();
                }
            }

            // Display property for UI binding
            public string DisplayName => string.IsNullOrEmpty(SceneName) ? "Unknown Scene" : SceneName;

            public event PropertyChangedEventHandler? PropertyChanged;
            protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                
                // Notify DisplayName when SceneName changes
                if (propertyName == nameof(SceneName))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}