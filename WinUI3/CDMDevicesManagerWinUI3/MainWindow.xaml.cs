using CDMDevicesManagerWinUI3.Helpers;
using CDMDevicesManagerWinUI3.Pages;
using HID.DisplayController;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CDMDevicesManagerWinUI3
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private MultiDeviceManager? _multiDeviceManager;
        public NavigationView NavigationView
        {
            get { return nvView; }
        }
        public MainWindow()
        {
            InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;



            _multiDeviceManager = new MultiDeviceManager(0x2516, 0x0228);

            // Must call StartMonitoring to begin detection
            _multiDeviceManager.StartMonitoring();

            // Populate existing devices
            var activeControllers = _multiDeviceManager.GetActiveControllers();

            // Debug out the number of active controllers found
            System.Diagnostics.Debug.WriteLine($"Found {activeControllers.Count} active devices");

            foreach (var controller in activeControllers)
            {
                
            }
        }

        public Action NavigationViewLoaded { get; set; }

        /// <summary>
        /// Gets the frame of the StartupWindow.
        /// </summary>
        /// <returns>The frame of the StartupWindow.</returns>
        /// <exception cref="Exception">Thrown if the window doesn't have a frame with the name "rootFrame".</exception>
        public Frame GetRootFrame()
        {
            //SuspensionManager.RegisterFrame(rootFrame, "AppFrame");
            rootFrame.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
            //rootFrame.NavigationFailed += OnNavigationFailed;
            return rootFrame;
        }


        // Wraps a call to rootFrame.Navigate to give the Page a way to know which NavigationRootPage is navigating.
        // Please call this function rather than rootFrame.Navigate to navigate the rootFrame.
        public void Navigate(Type pageType, object targetPageArguments = null, NavigationTransitionInfo navigationTransitionInfo = null)
        {
            rootFrame.Navigate(pageType, targetPageArguments, navigationTransitionInfo);

            // Ensure the NavigationView selection is set to the correct item to mark the sample's page as visited
            if (pageType.Equals(typeof(ItemPage)) && targetPageArguments != null)
            {
                // Mark the item sample's page visited
                SettingsHelper.TryAddItem(SettingsKeys.RecentlyVisited, targetPageArguments.ToString(), InsertPosition.First, SettingsHelper.MaxRecentlyVisitedSamples);
            }
        }

        private void OnNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                if (rootFrame.CurrentSourcePageType != typeof(SettingsPage))
                {
                    Navigate(typeof(SettingsPage));
                }
            }
            else
            {
                var selectedItem = args.SelectedItemContainer;
                if (selectedItem == Home)
                {
                    if (rootFrame.CurrentSourcePageType != typeof(HomePage))
                    {
                        Navigate(typeof(HomePage));
                    }
                }
                else if(selectedItem == Devices)
                    {
                        if (rootFrame.CurrentSourcePageType != typeof(AllControlsPage))
                        {
                            Navigate(typeof(AllControlsPage));
                        }
                    }
            }
        }
    }
}
