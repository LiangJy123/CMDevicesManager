using CMDevicesManager.Models;
using HID.DisplayController;
using HidApi;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using Path = System.IO.Path;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for DevicePage.xaml
    /// </summary>
    public partial class DevicePage : Page
    {
        public ObservableCollection<DeviceInfos> Devices { get; } = new();


        private MultiDeviceManager? _multiDeviceManager;

        public DevicePage()
        {
            InitializeComponent();

            // TODO: Replace with real device discovery/population.
            // Add as many devices as are available; each will render as a card automatically.
            Devices.Add(new DeviceInfos("haf700v2", "HAF700 V2", "Resources/Devices/HAF700V2.jpg"));
            // Devices.Add(new DeviceInfo("device-2", "Another Device", "Resources/Devices/Another.jpg"));

            DataContext = this;
        }

        private void DeviceCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.DataContext is DeviceInfos device)
                {
                    NavigationService?.Navigate(new DeviceConfigPage(device));
                }
            }
            catch (Exception ex)
            {
                // If you have a Logger available in scope, you can log it.
                System.Diagnostics.Debug.WriteLine($"Navigation to DeviceConfigPage failed: {ex}");
                MessageBox.Show("Failed to open device configuration.", "Navigation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _multiDeviceManager = new MultiDeviceManager(0x2516, 0x0228);

                // Set up event handlers
                _multiDeviceManager.ControllerAdded += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            //// Add the DeviceInfo object to the list
                            //if (!DevicesListBox.Items.Cast<DeviceInfo>().Any(d => d.Path == ev.Device.Path))
                            //{
                            //    DevicesListBox.Items.Add(ev.Device);
                            //    HideErrorMessage(); // Hide error message when devices are successfully added
                            //    UpdateButtonStates(); // Update button states when devices change
                            //}
                        }
                        catch (Exception ex)
                        {
                            ShowStatusMessage($"Failed to add device: {ex.Message}");
                        }
                    });
                };

                _multiDeviceManager.ControllerRemoved += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            //// Remove the DeviceInfo object from the list
                            //var deviceToRemove = DevicesListBox.Items.Cast<DeviceInfo>()
                            //    .FirstOrDefault(d => d.Path == ev.Device.Path);

                            //if (deviceToRemove != null)
                            //{
                            //    DevicesListBox.Items.Remove(deviceToRemove);
                            //    UpdateButtonStates(); // Update button states when devices change
                            //}
                        }
                        catch (Exception ex)
                        {
                            ShowStatusMessage($"Failed to remove device: {ex.Message}");
                        }
                    });
                };

                _multiDeviceManager.ControllerError += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ShowStatusMessage($"Device Error - {ev.Device.ProductString}: {ev.Exception.Message}");
                    });
                };

                // Must call StartMonitoring to begin detection
                _multiDeviceManager.StartMonitoring();

                // Populate existing devices
                var activeControllers = _multiDeviceManager.GetActiveControllers();
                foreach (var controller in activeControllers)
                {
                    //// Add DeviceInfo objects to the list
                    //if (!DevicesListBox.Items.Cast<DeviceInfo>().Any(d => d.Path == controller.DeviceInfo.Path))
                    //{
                    //    DevicesListBox.Items.Add(controller.DeviceInfo);
                    //}
                }

            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Failed to initialize device manager: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle realtime streaming
        /// </summary>
        private async void RealtimeStreamingDemo()
        {

            if (_multiDeviceManager == null)
            {
                ShowStatusMessage("Device manager not initialized");
                return;
            }

            var activeControllers = _multiDeviceManager.GetActiveControllers();
            if (activeControllers.Count == 0)
            {
                ShowStatusMessage("No devices connected for realtime streaming");
                return;
            }

            try
            {

                // Show status message
                ShowStatusMessage($"Starting realtime streaming demo on {activeControllers.Count} device(s)...");

                // Set up image directory path (you may want to make this configurable)
                string imageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HidProtocol", "resources", "realtime");

                // Alternative paths to check for images
                string[] alternativePaths = {
                    @"E:\github\CMDevicesManager\HidProtocol\resources\realtime",
                    @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources\realtime",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CMDevicesManager", "images")
                };

                // Find a valid image directory
                foreach (string path in alternativePaths)
                {
                    if (Directory.Exists(path))
                    {
                        imageDirectory = path;
                        break;
                    }
                }

                // Execute realtime streaming on all devices
                var results = await _multiDeviceManager.ExecuteOnAllDevices(async controller =>
                {
                    try
                    {
                        bool success = await controller.SimpleRealTimeDisplayDemo(imageDirectory, 20);
                        return success;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Realtime streaming failed for device: {ex.Message}");
                        return false;
                    }
                }, timeout: TimeSpan.FromMinutes(5));

                // Show results
                int successCount = results.Values.Count(success => success);
                if (successCount == results.Count)
                {
                    ShowStatusMessage($"Realtime streaming demo completed successfully on all {successCount} device(s)!");
                }
                else
                {
                    ShowStatusMessage($"Realtime streaming completed with mixed results: {successCount}/{results.Count} devices succeeded");
                }
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Realtime streaming demo failed: {ex.Message}");
            }
            finally
            {
                ;
            }
        }

        /// <summary>
        /// Handle offline streaming
        /// </summary>
        private async void OfflineStreamingDemo()
        {

            if (_multiDeviceManager == null)
            {
                ShowStatusMessage("Device manager not initialized");
                return;
            }

            var activeControllers = _multiDeviceManager.GetActiveControllers();
            if (activeControllers.Count == 0)
            {
                ShowStatusMessage("No devices connected for offline streaming");
                return;
            }

            try
            {
                // Show status message
                ShowStatusMessage($"Starting offline streaming demo on {activeControllers.Count} device(s)...");

                // Execute offline streaming (suspend mode) on all devices
                var results = await _multiDeviceManager.ExecuteOnAllDevices(async controller =>
                {
                    try
                    {
                        Console.WriteLine("=== Simple Offline Display Demo ===");

                        // Step 1: Disable real-time display mode and entry to suspend mode
                        bool step1Response = await controller.SetSuspendModeWithResponse();
                        if (!step1Response)
                        {
                            return false;
                        }

                        // Init files list to transfer (you may want to make these paths configurable)
                        List<string> filesToTransfer = new List<string>();

                        // Try different base paths for the suspend files
                        string[] basePaths = {
                            @"E:\github\CMDevicesManager\HidProtocol\resources",
                            @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources",
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HidProtocol", "resources")
                        };

                        foreach (string basePath in basePaths)
                        {
                            string[] suspendFiles = {
                                Path.Combine(basePath, "suspend_0.jpg"),
                                Path.Combine(basePath, "suspend_1.jpg"),
                                Path.Combine(basePath, "suspend_2.mp4")
                            };

                            if (suspendFiles.Any(File.Exists))
                            {
                                filesToTransfer.AddRange(suspendFiles.Where(File.Exists));
                                break;
                            }
                        }

                        if (!filesToTransfer.Any())
                        {
                            Console.WriteLine("No suspend files found, using basic demo mode");
                            // Continue with basic suspend mode without files
                        }
                        else
                        {
                            // Step 2: Send multiple suspend files
                            bool filesSuccess = await controller.SendMultipleSuspendFilesWithResponse(filesToTransfer, startingTransferId: 2);
                            if (!filesSuccess)
                            {
                                Console.WriteLine("Failed to send suspend files");
                                return false;
                            }
                        }

                        // Step 3: Set brightness to 80
                        var brightnessResponse = await controller.SendCmdBrightnessWithResponse(80);
                        if (brightnessResponse?.IsSuccess != true)
                        {
                            Console.WriteLine($"Set brightness failed. Status: {brightnessResponse?.StatusCode}");
                        }

                        // Step 4: Set keep alive timer
                        var timerResponse = await controller.SendCmdSetKeepAliveTimerWithResponse(5);
                        if (timerResponse?.IsSuccess != true)
                        {
                            Console.WriteLine($"Set keep alive timer failed. Status: {timerResponse?.StatusCode}");
                        }

                        // Step 5: Get max suspend media count to check current status
                        var maxMediaResponse = await controller.SendCmdReadMaxSuspendMediaWithResponse();
                        if (maxMediaResponse?.IsSuccess == true && !string.IsNullOrEmpty(maxMediaResponse.Value.ResponseData))
                        {
                            Console.WriteLine($"Max suspend media response: {maxMediaResponse.Value.ResponseData}");
                        }

                        // Sleep for demonstration (shorter time for UI responsiveness)
                        Console.WriteLine("Entering suspend mode for 30 seconds...");
                        await Task.Delay(30000);

                        // Exit suspend mode
                        Console.WriteLine("Exiting suspend mode by deleting all suspend media...");
                        bool clearSuccess = await controller.ClearSuspendModeWithResponse();
                        if (!clearSuccess)
                        {
                            Console.WriteLine("Failed to clear suspend mode");
                            return false;
                        }

                        Console.WriteLine("Suspend mode demo completed successfully!");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Offline streaming failed for device: {ex.Message}");
                        return false;
                    }
                }, timeout: TimeSpan.FromMinutes(10));

                // Show results
                int successCount = results.Values.Count(success => success);
                if (successCount == results.Count)
                {
                    ShowStatusMessage($"Offline streaming demo completed successfully on all {successCount} device(s)!");
                }
                else
                {
                    ShowStatusMessage($"Offline streaming completed with mixed results: {successCount}/{results.Count} devices succeeded");
                }
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Offline streaming demo failed: {ex.Message}");
            }
            finally
            {
                ;
            }
        }

        private void ShowStatusMessage(string message)
        {
            Console.WriteLine($"Message: {message}");
        }

    }
}
