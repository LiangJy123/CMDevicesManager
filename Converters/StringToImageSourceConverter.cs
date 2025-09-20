using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CMDevicesManager.Converters
{
    public sealed class StringToImageSourceConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                // Resolve relative path against app base directory (simple fallback).
                if (!Path.IsPathRooted(path))
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var combined = Path.Combine(baseDir, path);
                    if (File.Exists(combined))
                        path = combined;
                }

                if (!File.Exists(path))
                    return null;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null; // swallow – just no image
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Editing not needed; return null so BackgroundImagePath becomes null when user clears image in UI (if ever bound two-way).
            return null;
        }
    }
}