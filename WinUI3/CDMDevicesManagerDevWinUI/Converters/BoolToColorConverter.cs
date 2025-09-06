// Add to a new file: Converters\BoolToColorConverter.cs
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System;

namespace CDMDevicesManagerDevWinUI.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        public string TrueColor { get; set; } = "LimeGreen";
        public string FalseColor { get; set; } = "OrangeRed";

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isTrue = value is bool b && b;
            return new SolidColorBrush(isTrue ? Colors.LimeGreen : Colors.OrangeRed);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToStringConverter : IValueConverter
    {
        public string TrueValue { get; set; } = "True";
        public string FalseValue { get; set; } = "False";

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isTrue = value is bool b && b;
            return isTrue ? TrueValue : FalseValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}