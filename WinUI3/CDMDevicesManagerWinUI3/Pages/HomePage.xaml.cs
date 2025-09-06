// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using CDMDevicesManagerWinUI3.Helpers;
using CDMDevicesManagerWinUI3.Models;
using HID.DisplayController;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using System.Linq;

namespace CDMDevicesManagerWinUI3.Pages;

public sealed partial class HomePage : ItemsPageBase
{
    IReadOnlyList<ControlInfoDataItem> RecentlyVisitedSamplesList;
    IReadOnlyList<ControlInfoDataItem> RecentlyAddedOrUpdatedSamplesList;
    IReadOnlyList<ControlInfoDataItem> FavoriteSamplesList;

    private MultiDeviceManager? _multiDeviceManager;
    public HomePage()
    {
        this.InitializeComponent();


        _multiDeviceManager = new MultiDeviceManager(0x2516, 0x0228);

        // Must call StartMonitoring to begin detection
        _multiDeviceManager.StartMonitoring();

        // Populate existing devices
        var activeControllers = _multiDeviceManager.GetActiveControllers();
        foreach (var controller in activeControllers)
        {

        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ((NavigationViewItem)App.MainWindow.NavigationView.MenuItems.First()).IsSelected = true;

        Items = ControlInfoDataSource.Instance.Groups
            .SelectMany(g => g.Items)
            .OrderBy(i => i.Title)
            .ToList();

        RecentlyVisitedSamplesList = GetValidItems(SettingsKeys.RecentlyVisited);
        RecentlyAddedOrUpdatedSamplesList = Items.Where(i => i.IsNew || i.IsUpdated).ToList();
        FavoriteSamplesList = GetValidItems(SettingsKeys.Favorites);

        VisualStateManager.GoToState(this, RecentlyVisitedSamplesList.Count > 0 ? "Recent" : "NoRecent", true);
        VisualStateManager.GoToState(this, FavoriteSamplesList.Count > 0 ? "Favorites" : "NoFavorites", true);
    }

    public List<ControlInfoDataItem> GetValidItems(string settingsKey)
    {
        List<string> keyList = SettingsHelper.GetList(settingsKey);

        if (keyList == null || keyList.Count == 0)
            return new List<ControlInfoDataItem>();

        Dictionary<string, ControlInfoDataItem> itemMap = Items.ToDictionary(i => i.UniqueId);

        List<ControlInfoDataItem> result = new();

        foreach (string id in keyList)
        {
            if (itemMap.TryGetValue(id, out var item))
            {
                result.Add(item);
            }
            else
            {
                SettingsHelper.TryRemoveItem(settingsKey, id);
            }
        }

        return result;
    }
}