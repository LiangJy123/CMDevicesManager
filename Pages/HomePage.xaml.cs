using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using CMDevicesManager.ViewModels;
using CMDevicesManager.Services;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for HomePage.xaml
    /// </summary>
    public partial class HomePage : Page
    {
        public HomePage()
        {
            InitializeComponent();

            // Switch from fake to real hardware-backed service
            ISystemMetricsService service = new RealSystemMetricsService();
            DataContext = new HomeViewModel(service);

            // Swallow wheel/keyboard scrolling just in case a parent tries to scroll.
            PreviewMouseWheel += (_, e) => e.Handled = true;
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
                    e.Handled = true;
            };
        }
    }
}
