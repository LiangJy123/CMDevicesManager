using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System;

namespace CDMDevicesManagerDevWinUI.Converters
{
    public class NetworkSpeedToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double speed)
            {
                // Define color thresholds for network speed
                if (speed >= 100) // High speed - Green
                    return new SolidColorBrush(Colors.LimeGreen);
                else if (speed >= 50) // Medium speed - Cyan
                    return new SolidColorBrush(Colors.Cyan);
                else if (speed >= 20) // Low speed - Orange
                    return new SolidColorBrush(Colors.Orange);
                else // Very low speed - Red
                    return new SolidColorBrush(Colors.Red);
            }
            
            // Default color - Gray
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}