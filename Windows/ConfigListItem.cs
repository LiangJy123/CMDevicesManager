namespace CMDevicesManager.Windows
{
    public sealed class ConfigListItem
    {
        public string DisplayName { get; set; } = "";
        public string Path { get; set; } = "";
        public CMDevicesManager.Pages.CanvasConfiguration Config { get; set; } = null!;
        public System.Windows.Media.Imaging.BitmapSource? PreviewImage { get; set; }
    }
}