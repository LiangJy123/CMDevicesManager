﻿using Microsoft.UI.Windowing;

namespace CDMDevicesManagerDevWinUI.Views
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

            App.Current.NavService
                .Initialize(NavView, NavFrame, NavigationPageMappings.PageDictionary)
                    .ConfigureDefaultPage(typeof(HomePage))
                    .ConfigureSettingsPage(typeof(SettingsPage))
                .ConfigureJsonFile("Assets/NavViewMenu/AppData.json")
                .ConfigureTitleBar(AppTitleBar)
                .ConfigureBreadcrumbBar(BreadCrumbNav, BreadcrumbPageMappings.PageDictionary);
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.ChangeThemeWithoutSave(App.MainWindow);
        }
    }
}
