using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CMDevicesManager.Converters
{
    public sealed class BooleanToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public bool Collapse { get; set; } = true;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool v = value is bool b && b;
            if (Invert) v = !v;
            if (v) return Visibility.Visible;
            return Collapse ? Visibility.Collapsed : Visibility.Hidden;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility vis
                ? (Invert ? vis != Visibility.Visible : vis == Visibility.Visible)
                : false;
    }
}