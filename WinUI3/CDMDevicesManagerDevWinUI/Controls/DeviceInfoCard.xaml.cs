using HidApi;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CDMDevicesManagerDevWinUI.Views
{
    public sealed partial class DeviceInfoCard : UserControl, INotifyPropertyChanged
    {
        #region Properties

        private DeviceInfoViewModel? _deviceInfo;
        public DeviceInfoViewModel? DeviceInfo
        {
            get => _deviceInfo;
            set
            {
                if (_deviceInfo != value)
                {
                    _deviceInfo = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProductName));
                    OnPropertyChanged(nameof(SerialNumber));
                    OnPropertyChanged(nameof(ManufacturerName));
                    OnPropertyChanged(nameof(DevicePath));
                }
            }
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged();
                }
            }
        }

        // Helper properties for binding
        public string ProductName => DeviceInfo?.ProductName ?? "Unknown Device";
        public string SerialNumber => DeviceInfo?.SerialNumber ?? "No Serial";
        public string ManufacturerName => DeviceInfo?.ManufacturerName ?? "Unknown";
        public string DevicePath => DeviceInfo?.DevicePath ?? "";

        #endregion

        #region Events

        public event EventHandler<DeviceActionEventArgs>? SettingsRequested;
        public event EventHandler<DeviceActionEventArgs>? ConfigRequested;
        public event EventHandler<DeviceActionEventArgs>? LiveViewRequested;
        public event EventHandler<DeviceActionEventArgs>? DiagnosticsRequested;
        public event EventHandler<DeviceActionEventArgs>? FirmwareUpdateRequested;
        public event EventHandler<DeviceActionEventArgs>? ResetRequested;
        public event EventHandler<DeviceActionEventArgs>? RefreshRequested;
        public event EventHandler<DeviceActionEventArgs>? TestRequested;

        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Constructor

        public DeviceInfoCard()
        {
            this.InitializeComponent();
        }

        #endregion

        #region Button Event Handlers

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceInfo != null)
                SettingsRequested?.Invoke(this, new DeviceActionEventArgs(DeviceInfo, DeviceAction.Settings));
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceInfo != null)
                ConfigRequested?.Invoke(this, new DeviceActionEventArgs(DeviceInfo, DeviceAction.Config));
        }

        private void LiveViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceInfo != null)
                LiveViewRequested?.Invoke(this, new DeviceActionEventArgs(DeviceInfo, DeviceAction.LiveView));
        }

        private void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceInfo != null)
                DiagnosticsRequested?.Invoke(this, new DeviceActionEventArgs(DeviceInfo, DeviceAction.Diagnostics));
        }

        private void FirmwareUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceInfo != null)
                FirmwareUpdateRequested?.Invoke(this, new DeviceActionEventArgs(DeviceInfo, DeviceAction.FirmwareUpdate));
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceInfo != null)
                ResetRequested?.Invoke(this, new DeviceActionEventArgs(DeviceInfo, DeviceAction.Reset));
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceInfo != null)
                RefreshRequested?.Invoke(this, new DeviceActionEventArgs(DeviceInfo, DeviceAction.Refresh));
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceInfo != null)
                TestRequested?.Invoke(this, new DeviceActionEventArgs(DeviceInfo, DeviceAction.Test));
        }

        #endregion

        #region Helper Methods

        public string GetStatusText(bool isConnected)
        {
            return isConnected ? "Connected" : "Disconnected";
        }

        public Brush GetStatusBrush(bool isConnected)
        {
            return isConnected ?
                new SolidColorBrush(Colors.LimeGreen) :
                new SolidColorBrush(Colors.OrangeRed);
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    #region Event Args and Enums

    public class DeviceActionEventArgs : EventArgs
    {
        public DeviceInfoViewModel DeviceInfo { get; }
        public DeviceAction Action { get; }

        public DeviceActionEventArgs(DeviceInfoViewModel deviceInfo, DeviceAction action)
        {
            DeviceInfo = deviceInfo;
            Action = action;
        }
    }

    public enum DeviceAction
    {
        Settings,
        Config,
        LiveView,
        Diagnostics,
        FirmwareUpdate,
        Reset,
        Refresh,
        Test
    }

    #endregion
}