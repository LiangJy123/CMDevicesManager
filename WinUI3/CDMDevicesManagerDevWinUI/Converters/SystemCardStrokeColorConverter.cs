using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace CDMDevicesManagerDevWinUI.Converters
{
    public class SystemCardStrokeColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string name)
            {
                return name switch
                {
                    "CPU" => new SolidColorBrush(Color.FromArgb(255, 0, 188, 212)), // #FF00BCD4 - Cyan
                    "GPU" => new SolidColorBrush(Color.FromArgb(255, 0, 188, 212)), // #FF00BCD4 - Cyan  
                    "Memory" => new SolidColorBrush(Color.FromArgb(255, 233, 30, 99)), // #FFE91E63 - Pink
                    _ => new SolidColorBrush(Color.FromArgb(255, 0, 188, 212)) // Default to Cyan
                };
            }
            
            return new SolidColorBrush(Color.FromArgb(255, 0, 188, 212)); // Default to Cyan
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}