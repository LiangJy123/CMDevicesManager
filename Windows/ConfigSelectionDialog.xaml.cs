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
using System.Collections.ObjectModel;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using Application = System.Windows.Application;

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

        private readonly ObservableCollection<ConfigListItem> _allItems;
        private readonly List<ConfigListItem> _allItemsBackup;
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
            var itemsList = items.ToList();
            _allItems = new ObservableCollection<ConfigListItem>(itemsList);
            _allItemsBackup = new List<ConfigListItem>(itemsList);
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
                var filtered = _allItems.Where(i =>
                    i.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    i.Path.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
                ListViewConfigs.ItemsSource = filtered;
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

        /// <summary>
        /// 删除配置文件及其预览图
        /// </summary>
        private void DeleteConfig_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ConfigListItem item)
                return;

            // 从资源获取本地化字符串
            var confirmTitle = (string?)Application.Current.TryFindResource("DeleteConfig_ConfirmTitle") ?? "Confirm Delete";
            var confirmMessageFormat = (string?)Application.Current.TryFindResource("DeleteConfig_ConfirmMessage")
                ?? "Are you sure you want to delete configuration \"{0}\"?\n\nThis operation will delete both the configuration file and preview images, and cannot be undone.";
            var confirmMessage = string.Format(confirmMessageFormat, item.DisplayName);

            // 确认对话框
            var result = MessageBox.Show(
                this,
                confirmMessage,
                confirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                // 1. 删除配置文件
                if (File.Exists(item.Path))
                {
                    File.Delete(item.Path);
                }

                // 2. 删除预览图（.preview.jpg 和 .preview.png）
                var directory = Path.GetDirectoryName(item.Path);
                var baseFileName = Path.GetFileNameWithoutExtension(item.Path);

                if (!string.IsNullOrEmpty(directory) && !string.IsNullOrEmpty(baseFileName))
                {
                    var previewJpg = Path.Combine(directory, baseFileName + ".preview.jpg");
                    var previewPng = Path.Combine(directory, baseFileName + ".preview.png");

                    if (File.Exists(previewJpg))
                        File.Delete(previewJpg);

                    if (File.Exists(previewPng))
                        File.Delete(previewPng);
                }

                // 3. 从列表中移除
                _allItems.Remove(item);
                _allItemsBackup.Remove(item);

                // 4. 刷新筛选结果
                Filter();

                // 5. 如果列表为空，提示用户
                if (_allItems.Count == 0)
                {
                    var allDeletedTitle = (string?)Application.Current.TryFindResource("DeleteConfig_AllDeletedTitle") ?? "Notice";
                    var allDeletedMessage = (string?)Application.Current.TryFindResource("DeleteConfig_AllDeletedMessage") ?? "All configurations have been deleted.";
                    MessageBox.Show(this, allDeletedMessage, allDeletedTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = false;
                }
                else
                {
                    var successTitle = (string?)Application.Current.TryFindResource("DeleteConfig_SuccessTitle") ?? "Delete Successful";
                    var successMessage = (string?)Application.Current.TryFindResource("DeleteConfig_SuccessMessage") ?? "Configuration deleted successfully.";
                    MessageBox.Show(this, successMessage, successTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                var errorTitle = (string?)Application.Current.TryFindResource("DeleteConfig_ErrorTitle") ?? "Delete Failed";
                var errorMessageFormat = (string?)Application.Current.TryFindResource("DeleteConfig_ErrorMessage")
                    ?? "An error occurred while deleting configuration:\n{0}";
                var errorMessage = string.Format(errorMessageFormat, ex.Message);

                MessageBox.Show(
                    this,
                    errorMessage,
                    errorTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}