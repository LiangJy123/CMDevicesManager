using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace CDMDevicesManagerDevWinUI.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool boolValue = false;
            
            if (value is bool b)
            {
                boolValue = b;
            }
            
            // Check if parameter indicates inversion
            bool invert = parameter is string param && param.Equals("True", StringComparison.OrdinalIgnoreCase);
            
            if (invert)
            {
                boolValue = !boolValue;
            }
            
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Visibility visibility)
            {
                bool result = visibility == Visibility.Visible;
                
                // Check if parameter indicates inversion
                bool invert = parameter is string param && param.Equals("True", StringComparison.OrdinalIgnoreCase);
                
                if (invert)
                {
                    result = !result;
                }
                
                return result;
            }
            
            return false;
        }
    }
}