using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CMDevicesManager.Pages;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace CMDevicesManager.Windows
{
    public partial class ConfigSelectionDialog : Window
    {
        public sealed class ConfigListItem
        {
            public string DisplayName { get; set; } = "";
            public string Path { get; set; } = "";
            public CanvasConfiguration Config { get; set; } = null!;
            public BitmapSource? PreviewImage { get; set; }
        }

        private readonly List<ConfigListItem> _allItems;
        public bool ShowPathInList { get; }

        public CanvasConfiguration? SelectedConfig { get; private set; }
        public string? SelectedConfigPath { get; private set; }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        public ConfigSelectionDialog(IEnumerable<ConfigListItem> items, bool showPath)
        {
            ShowPathInList = showPath;
            _allItems = items.ToList();
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            ListViewConfigs.ItemsSource = _allItems;
            if (_allItems.Count > 0)
                ListViewConfigs.SelectedIndex = 0;
            SearchBox.Focus();
        }

        // Public static entry
        public static bool TrySelectConfig(Window owner,
            string configsFolder,
            out CanvasConfiguration? cfg,
            out string? path,
            bool showPathInList = false)
        {
            cfg = null;
            path = null;
            if (string.IsNullOrWhiteSpace(configsFolder) || !Directory.Exists(configsFolder))
                return false;

            var files = Directory.GetFiles(configsFolder, "*.json");
            if (files.Length == 0) return false;

            var items = new List<ConfigListItem>();
            foreach (var f in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var json = File.ReadAllText(f);
                    var c = JsonSerializer.Deserialize<CanvasConfiguration>(json, JsonOpts);
                    if (c == null) continue;
                    var baseName = Path.GetFileNameWithoutExtension(f);
                    var preview = LoadPreview(configsFolder, baseName);
                    items.Add(new ConfigListItem
                    {
                        DisplayName = string.IsNullOrWhiteSpace(c.ConfigName) ? baseName : c.ConfigName,
                        Path = f,
                        Config = c,
                        PreviewImage = preview
                    });
                }
                catch
                {
                    // ignore invalid
                }
            }

            if (items.Count == 0) return false;

            var dlg = new ConfigSelectionDialog(items, showPathInList)
            {
                Owner = owner
            };
            var result = dlg.ShowDialog();
            if (result == true && dlg.SelectedConfig != null && !string.IsNullOrEmpty(dlg.SelectedConfigPath))
            {
                cfg = dlg.SelectedConfig;
                path = dlg.SelectedConfigPath;
                return true;
            }
            return false;
        }

        private static BitmapSource? LoadPreview(string folder, string baseFile)
        {
            try
            {
                string jpg = Path.Combine(folder, baseFile + ".preview.jpg");
                string png = Path.Combine(folder, baseFile + ".preview.png");
                var candidate = File.Exists(jpg) ? jpg : (File.Exists(png) ? png : null);
                if (candidate == null) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(candidate, UriKind.Absolute);
                bmp.DecodePixelWidth = 200;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private void Filter()
        {
            var f = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(f))
            {
                ListViewConfigs.ItemsSource = _allItems;
            }
            else
            {
                ListViewConfigs.ItemsSource = _allItems.Where(i =>
                    i.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    i.Path.Contains(f, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Filter();

        private void Accept()
        {
            if (ListViewConfigs.SelectedItem is ConfigListItem item)
            {
                SelectedConfig = item.Config;
                SelectedConfigPath = item.Path;
                DialogResult = true;
            }
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e) => Accept();
        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void ListViewConfigs_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ListViewConfigs.SelectedItem != null)
                Accept();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
            else if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        }
    }
}