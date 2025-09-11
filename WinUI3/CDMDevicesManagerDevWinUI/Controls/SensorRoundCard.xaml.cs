using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using CDMDevicesManagerDevWinUI.Models;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CDMDevicesManagerDevWinUI.Controls
{
    public sealed partial class SensorRoundCard : UserControl
    {
        public static readonly DependencyProperty SensorDataProperty =
            DependencyProperty.Register(nameof(SensorData), typeof(Models.SensorCard), typeof(SensorRoundCard), new PropertyMetadata(null));

        public static readonly DependencyProperty StrokeColorProperty =
            DependencyProperty.Register(nameof(StrokeColor), typeof(Brush), typeof(SensorRoundCard), new PropertyMetadata(null));

        public Models.SensorCard SensorData
        {
            get => (Models.SensorCard)GetValue(SensorDataProperty);
            set => SetValue(SensorDataProperty, value);
        }

        public Brush StrokeColor
        {
            get => (Brush)GetValue(StrokeColorProperty);
            set => SetValue(StrokeColorProperty, value);
        }

        public SensorRoundCard()
        {
            InitializeComponent();
        }

        // Helper method for calculating circular progress stroke dash offset
        public double CalculateStrokeDashOffset(double percentage)
        {
            // Circle circumference = 2?r = 2?(40/2) = 80? ? 251.2
            const double circumference = 251.2;
            return circumference * (1 - (percentage / 100.0));
        }
    }
}
