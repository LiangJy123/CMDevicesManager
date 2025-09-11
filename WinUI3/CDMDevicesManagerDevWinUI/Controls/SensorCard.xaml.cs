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
    public sealed partial class SensorCard : UserControl
    {
        public static readonly DependencyProperty SensorDataProperty =
            DependencyProperty.Register(nameof(SensorData), typeof(Models.SensorCard), typeof(SensorCard), new PropertyMetadata(null));

        public Models.SensorCard SensorData
        {
            get => (Models.SensorCard)GetValue(SensorDataProperty);
            set => SetValue(SensorDataProperty, value);
        }

        public SensorCard()
        {
            InitializeComponent();
        }
    }
}
