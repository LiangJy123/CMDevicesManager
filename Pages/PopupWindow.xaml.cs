using MicaWPF.Controls;
using System.Windows;
using System.Windows.Controls;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Popup window for displaying pages in a modal dialog
    /// </summary>
    public partial class PopupWindow : MicaWindow
    {
        public PopupWindow()
        {
            InitializeComponent();
        }

        public PopupWindow(Page page) : this()
        {
            PopupFrame.Navigate(page);
        }

        public PopupWindow(Page page, string title) : this(page)
        {
            Title = title;
        }
    }
}