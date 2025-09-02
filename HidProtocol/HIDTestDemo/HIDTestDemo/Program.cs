// See https://aka.ms/new-console-template for more information
using HID.DisplayController;
using HidApi;
using System.Text;
using System.Timers;


Console.WriteLine("Hello, World!");

Device device = new Device(0x2516, 0x0228);
Console.WriteLine(device.GetManufacturer());

// Create a cancellation token to gracefully stop the thread
using var cancellationTokenSource = new CancellationTokenSource();
var cancellationToken = cancellationTokenSource.Token;

// Create and start the HID reading thread
var hidReadingThread = new Thread(() => HidReadingWorker(device, cancellationToken))
{
    Name = "HidReadingThread",
    IsBackground = true
};

hidReadingThread.Start();

// Main thread continues here - you can add other logic
Console.WriteLine("HID reading started on background thread. Press any key to stop...");
Console.ReadKey();

// Signal the thread to stop
cancellationTokenSource.Cancel();

// Wait for the thread to finish (with timeout)
if (!hidReadingThread.Join(TimeSpan.FromSeconds(5)))
{
    Console.WriteLine("Thread did not stop gracefully, forcing abort...");
}

Console.WriteLine("HID reading stopped.");

// Worker method for the HID reading thread
static void HidReadingWorker(Device device, CancellationToken cancellationToken)
{
    byte[] buffer = new byte[1024];

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            // Use ReadTimeout with timeout to avoid blocking indefinitely
            int result = device.ReadTimeout(buffer.AsSpan(), 1000); // 1 second timeout

            if (result > 0)
            {
                // debug out the first 32 bytes
                Console.WriteLine($"*****[DEBUG] Received {result} bytes: {Convert.ToHexString(buffer, 0, Math.Min(result, 32))}");
            }
            else if (result == 0)
            {
                Console.WriteLine("No data available");
            }
        }
        catch (HidException ex)
        {
            // HID specific exceptions
            Console.WriteLine($"HID read error: {ex.Message}");

            // If it's a critical error, you might want to break the loop
            if (ex.Message.Contains("device disconnected", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Device disconnected, stopping HID reading thread.");
                break;
            }
        }
        catch (Exception ex)
        {
            // Other exceptions
            Console.WriteLine($"Read error: {ex.Message}");
        }
    }

    Console.WriteLine("HID reading thread stopped.");
}


if (false)
{
    Console.WriteLine("=== HID Device Monitoring Demo  For Single Device===");

    // Create a device monitor to watch for all HID devices
    var deviceMonitor = new HidDeviceMonitor();

    // Set up event handlers
    deviceMonitor.DeviceConnected += (sender, e) =>
    {
        Console.WriteLine($"[CONNECTED] {e.Device.ManufacturerString} {e.Device.ProductString}");
        Console.WriteLine($"            VID:0x{e.Device.VendorId:X4} PID:0x{e.Device.ProductId:X4}");
        Console.WriteLine($"            Path: {e.Device.Path}");
        Console.WriteLine($"            Time: {e.Timestamp}");
        // SerialNumber
        Console.WriteLine($"            Serial#: {e.Device.SerialNumber}");
        Console.WriteLine();
    };

    deviceMonitor.DeviceDisconnected += (sender, e) =>
    {
        Console.WriteLine($"[DISCONNECTED] {e.Device.ManufacturerString} {e.Device.ProductString}");
        Console.WriteLine($"               VID:0x{e.Device.VendorId:X4} PID:0x{e.Device.ProductId:X4}");
        Console.WriteLine($"               Time: {e.Timestamp}");
        // SerialNumber
        Console.WriteLine($"            Serial#: {e.Device.SerialNumber}");
        Console.WriteLine();
    };

    deviceMonitor.DeviceListChanged += (sender, e) =>
    {
        Console.WriteLine($"[INFO] Device list changed. Currently connected: {deviceMonitor.ConnectedDeviceCount} devices");
    };

    // Monitor specific devices (your display controller VID/PID)
    deviceMonitor.SetDeviceFilter(0x2516, 0x0228,0xffff);
    deviceMonitor.MonitoringInterval = 500; // Check every 500ms

    Console.WriteLine("Starting HID device monitoring...");
    deviceMonitor.StartMonitoring();

    // Wait for user input to stop monitoring
    Console.WriteLine("Press Enter to stop monitoring...");
    Console.ReadLine();

    var displayController = new DisplayController(0x2516, 0x0228, "1870989178c08180");

    // output current device info
    var deviceFWInfo = displayController.DeviceFWInfo;
    var deviceCaps = displayController.Capabilities;
    Console.WriteLine($"Device FW Info: {deviceFWInfo}");
    Console.WriteLine($"Device Capabilities: {deviceCaps}");

    // Get device current status
    var deviceStatus = displayController.GetDeviceStatus();
    Console.WriteLine($"Device Status: {deviceStatus}");



    //Console.WriteLine("Performing factory reset...");
    //displayController.FactoryReset();
    //////Sleep to allow reboot
    //Thread.Sleep(15000);


    // Set up response event handler
    //displayController.ResponseReceived += (sender, response) =>
    //{
    //    Console.WriteLine($"[RESPONSE] {response.AckNumber} - Status: {response.StatusCode}");
    //    if (!string.IsNullOrEmpty(response.ResponseData))
    //    {
    //        Console.WriteLine($"[RESPONSE] Data: {response.ResponseData}");
    //    }
    //};

    // Start response listener
    displayController.StartResponseListener();


    // Example usage of enhanced commands with responses
    Console.WriteLine("Testing Enhanced SendCmd with Response Handling...");

    

    if (true)
    {
        try
        {
            await displayController.SimpleRealTimeDisplayDemo(
            imageDirectory: @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources\realtime",
            cycleCount: 30
        );

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during real-time display test: {ex.Message}");
        }
    }

    Thread.Sleep(1000);

    if (true)
    {
        try
        {
            // Example usage - basic version
            bool success = await displayController.SetSuspendModeDemo();
            if (success)
            {
                Console.WriteLine("Suspend mode configured successfully!");
            }
            else
            {
                Console.WriteLine("Failed to configure suspend mode!");
            }

            //// Test brightness with response
            //var brightnessResponse = await displayController.SendCmdBrightnessWithResponse(75);
            //if (brightnessResponse?.IsSuccess == true)
            //{
            //    Console.WriteLine("Brightness set successfully");
            //}
            //Thread.Sleep(1000);

            //// Test rotation with response
            //var rotationResponse = await displayController.SendCmdRotateWithResponse(180);
            //if (rotationResponse?.IsSuccess == true)
            //{
            //    Console.WriteLine("Rotation set successfully");
            //}
            //Thread.Sleep(1000);

            //// Get current device information with responses
            //var serialNumber = await displayController.GetDeviceSerialNumber();
            //if (!string.IsNullOrEmpty(serialNumber))
            //{
            //    Console.WriteLine($"Device Serial Number: {serialNumber}");
            //}

            //var colorSku = await displayController.GetColorSku();
            //if (!string.IsNullOrEmpty(colorSku))
            //{
            //    Console.WriteLine($"Color SKU: {colorSku}");
            //}

            //var currentBrightness = await displayController.GetCurrentBrightness();
            //if (currentBrightness.HasValue)
            //{
            //    Console.WriteLine($"Current Brightness: {currentBrightness.Value}%");
            //}

            //var currentRotation = await displayController.GetCurrentRotation();
            //if (currentRotation.HasValue)
            //{
            //    Console.WriteLine($"Current Rotation: {currentRotation.Value}°");
            //}
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during enhanced command testing: {ex.Message}");
        }
    }

    // wait for user input before exiting
    Console.WriteLine("Press Enter to exit...");
    Console.ReadLine();

    // Stop response listener before exit
    displayController.StopResponseListener();

    // reboot the device
    Console.WriteLine("Rebooting device...");
    displayController.Reboot();

}

if (false)
{

    Console.WriteLine("=== HID Multi-Device Management Demo ===");

    // Create multi-device manager
    var multiDeviceManager = new MultiDeviceManager(0x2516, 0x0228);

    // Set up event handlers if need.
    multiDeviceManager.ControllerAdded += (sender, e) =>
    {
        Console.WriteLine($"[CONTROLLER ADDED] {e.Device.ManufacturerString} {e.Device.ProductString}");
        Console.WriteLine($"                   Serial: {e.Device.SerialNumber}");
        Console.WriteLine($"                   Path: {e.Device.Path}");
    };

    multiDeviceManager.ControllerRemoved += (sender, e) =>
    {
        Console.WriteLine($"[CONTROLLER REMOVED] {e.Device.ManufacturerString} {e.Device.Path}");
    };

    multiDeviceManager.ControllerError += (sender, e) =>
    {
        Console.WriteLine($"[CONTROLLER ERROR] Device: {e.Device.ProductString}, Error: {e.Exception.Message}");
    };

    Console.WriteLine("Starting multi-device monitoring...");
    // Must call StartMonitoring to begin detection
    multiDeviceManager.StartMonitoring();

    //Console.WriteLine("Press Enter to test multi-device operations...");
    //Console.ReadLine();

    // Test multi-device operations
    var activeControllers = multiDeviceManager.GetActiveControllers();
    Console.WriteLine($"Active devices: {activeControllers.Count}");

    if (activeControllers.Count > 0)
    {
        Console.WriteLine("\n=== Testing Multi-Device Operations ===");

        // Test 1: Set brightness on all devices
        Console.WriteLine("Setting brightness to 75 on all devices...");
        var brightnessResults = await multiDeviceManager.SetBrightnessOnAllDevices(75);
        foreach (var result in brightnessResults)
        {
            Console.WriteLine($"  Device {result.Key}: {(result.Value ? "SUCCESS" : "FAILED")}");
        }

        // Test 2: Set rotation on all devices
        Console.WriteLine("\nSetting rotation to 90° on all devices...");
        var rotationResults = await multiDeviceManager.SetRotationOnAllDevices(90);
        foreach (var result in rotationResults)
        {
            Console.WriteLine($"  Device {result.Key}: {(result.Value ? "SUCCESS" : "FAILED")}");
        }

        // Test 3: Get status from all devices
        Console.WriteLine("\nGetting status from all devices...");
        var statusResults = await multiDeviceManager.GetStatusFromAllDevices();
        foreach (var result in statusResults)
        {
            Console.WriteLine($"  Device {result.Key}: {(result.Value?.ToString() ?? "FAILED")}");
        }

        //// Test 4: Transfer file to all devices (if you have a test image)
        //string testImagePath = @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources\test.jpg";
        //if (File.Exists(testImagePath))
        //{
        //    Console.WriteLine($"\nTransferring {Path.GetFileName(testImagePath)} to all devices...");
        //    var transferResults = await multiDeviceManager.TransferFileToAllDevices(testImagePath);
        //    foreach (var result in transferResults)
        //    {
        //        Console.WriteLine($"  Device {result.Key}: {(result.Value ? "SUCCESS" : "FAILED")}");
        //    }
        //}
    }

    Console.WriteLine("\nPress Enter to test realtime and Suspend mode...");
    Console.ReadLine();


    //// Targeted Device Operations
    //// Execute on specific devices by serial number
    //var serialNumbers = new[] { "1870989178c08180", "1870989178c08181" };
    //var results = await multiDeviceManager.ExecuteOnSpecificDevices(serialNumbers, async controller =>
    //{
    //    await controller.SendCmdBrightnessWithResponse(50);
    //    return true;
    //});

    //// Custom Batch Operations
    //// Custom operation on all devices
    //var results2 = await multiDeviceManager.ExecuteOnAllDevices(async controller =>
    //{
    //    return await controller.SendCmdRotateWithResponse(180);

    //}, timeout: TimeSpan.FromSeconds(10));

    //// Output results
    //foreach (var result in results2)
    //{
    //    Console.WriteLine($"Device {result.Key}: {(result.Value ? "SUCCESS" : "FAILED")}");
    //}

    KeepAliveTimer? keepAliveTimer = null;

    // Custom Batch Operations
    // Set realtime ModeDemo
    var results4 = await multiDeviceManager.ExecuteOnAllDevices(async controller =>
    {
        Console.WriteLine("=== Simple Real-Time Display Demo ===");
        // Get current app exe directory
        string appExeDirectory = AppContext.BaseDirectory;
        string imageDirectory = Path.Combine(appExeDirectory, "resources", "realtime");
        // check if directory exists, otherwise use hardcoded path
        if (!Directory.Exists(imageDirectory))
        {
            imageDirectory = @"E:\github\CMDevicesManager\HidProtocol\resources\realtime";
            //imageDirectory = @"E:\github\CMDevicesManager\HidProtocol\resources\gif\LCD-AFrame";
        }

        int cycleCount = 20; // Number of times to cycle through images
        try
        {
            List<string> imagePaths = new List<string>();
            // Init the images path list
            if (!string.IsNullOrEmpty(imageDirectory) && Directory.Exists(imageDirectory))
            {
                var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };
                var files = Directory.GetFiles(imageDirectory)
                                     .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
                                     .ToList();
                if (files.Count > 0)
                {
                    Console.WriteLine($"Found {files.Count} images in directory: {imageDirectory}");
                    imagePaths.AddRange(files);
                }
                else
                {
                    Console.WriteLine($"No supported image files found in directory: {imageDirectory}");
                }
            }
            else
            {
                Console.WriteLine("No valid image directory provided, using default images.");
            }


            // Step 1: Enable real-time display
            Console.WriteLine("Enabling real-time display...");
            var enableResponse = await controller.SendCmdRealTimeDisplayWithResponse(true);
            if (enableResponse?.IsSuccess != true)
            {
                Console.WriteLine("Failed to enable real-time display");
                return false;
            }


            // Create the keep-alive timer
            keepAliveTimer = new KeepAliveTimer(async () =>
            {
                var response = await controller.SendCmdKeepAliveWithResponse(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                Console.WriteLine($"Keep-alive sent. Success: {response?.IsSuccess}");
            }, intervalSeconds: 4.0);

            // Start the timer (sends first command immediately)
            await keepAliveTimer.StartAsync();


            // Step 2: Set brightness to 80 or other value.
            await controller.SendCmdBrightnessWithResponse(80);
            // Set optimal settings
            //await SendCmdSetKeepAliveTimerWithResponse(60: 23);
            //await Task.Delay(4000); // Short delay to ensure command is processed


            if (imagePaths.Count > 0)
            {
                Console.WriteLine($"Cycling through {imagePaths.Count} images, {cycleCount} times...");

                byte transferId = 2;
                for (int cycle = 0; cycle < cycleCount; cycle++)
                {
                    foreach (var imagePath in imagePaths)
                    {
                        Console.WriteLine($"[SENDING] Sending: {Path.GetFileName(imagePath)}");

                        // Step 3: Send image file, or can use SendFileTransfer to send image bytes array if image from memory.
                        controller.SendFileFromDisk(imagePath, transferId: transferId);
                        // Step 4: Send keep-alive command to display the image.
                        //await controller.SendCmdKeepAliveWithResponse(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                        transferId++;
                        if (transferId > 59) transferId = 2;

                        await Task.Delay(1200); // 1.5 second between images
                    }
                }
            }

            // Disable real-time display
            Console.WriteLine("Disabling real-time display...");
            await controller.SendCmdRealTimeDisplayWithResponse(false);


            // Clean up the timer
            keepAliveTimer?.Stop();
            keepAliveTimer?.Dispose();

            Console.WriteLine("=== Simple Real-Time Display Demo Completed ===");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Demo failed: {ex.Message}");
            // Clean up the timer
            keepAliveTimer?.Stop();
            keepAliveTimer?.Dispose();
            return false;
        }
    }, timeout: TimeSpan.FromSeconds(10));


    Console.WriteLine("Press Enter to run...");
    Console.ReadLine();


    // Custom Batch Operations
    // Set Suspend Mode Demo
    var results3 = await multiDeviceManager.ExecuteOnAllDevices(async controller =>
    {
        Console.WriteLine("=== Simple Offline Display Demo ===");
        // Step 1: Disable real-time display mode and entry to suspend mode by call SetSuspendModeWithResponse 
        bool step1Response = await controller.SetSuspendModeWithResponse();
        if (step1Response != true)
        {
            return false;
        }

        // Init files list to transfer
        //List<string> filesToTransfer = new List<string>
        //{
        //    @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources\suspend_0.jpg",
        //    @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources\suspend_1.jpg",
        //    @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources\suspend_2.mp4"
        //};

        List<string> filesToTransfer = new List<string>
        {
            @"E:\github\CMDevicesManager\HidProtocol\resources\suspend_0.jpg",
            @"E:\github\CMDevicesManager\HidProtocol\resources\suspend_1.jpg",
            @"E:\github\CMDevicesManager\HidProtocol\resources\suspend_2.mp4"
        };

        // Step 2: Send multiple suspend files/ or call SendSuspendFileWithResponse to send single file.
        bool filesSuccess = await controller.SendMultipleSuspendFilesWithResponse(filesToTransfer, startingTransferId: 2);
        if (!filesSuccess)
        {
            Console.WriteLine("Failed to send suspend files");
            return false;
        }

        // Step 3: Set brightness to 80 or other value.
        Console.WriteLine("Setting brightness to 80...");
        var brightnessResponse = await controller.SendCmdBrightnessWithResponse(80);
        if (brightnessResponse?.IsSuccess != true)
        {
            Console.WriteLine($"Set brightness failed. Status: {brightnessResponse?.StatusCode}");
            // Continue anyway as this might not be critical
        }
        else
        {
            Console.WriteLine("Set brightness successful");
        }

        // Step 4: Set keep alive timer, if have muitiple suspend files, the alive timer is each file duration.
        var timerResponse = await controller.SendCmdSetKeepAliveTimerWithResponse(5);
        if (timerResponse?.IsSuccess != true)
        {
            Console.WriteLine($"Set keep alive timer failed. Status: {timerResponse?.StatusCode}");
            // Continue anyway as this might not be critical
        }
        else
        {
            Console.WriteLine("Set keep alive timer successful");
        }

        // Step 5: (Optional) get max suspend media count to check current status. like below json response. suspendMediaActive array indicate which suspend media is active.
        // {"brightness": 80,"degree": 0,"osdState": 0,"keepAliveTimeout": 2,"maxSuspendMediaCount": 5,"displayInSleep": 0,"suspendMediaActive": [☻1,1,0,0,0]}
        var maxMediaResponse = await controller.SendCmdReadMaxSuspendMediaWithResponse();
        if (maxMediaResponse?.IsSuccess == true && !string.IsNullOrEmpty(maxMediaResponse.Value.ResponseData))
        {
            Console.WriteLine($"Max suspend media response: {maxMediaResponse.Value.ResponseData}");
        }

        // Sleep 20 seconds, then call delete suspend media command to exit suspend mode.
        Console.WriteLine("Entering suspend mode for 40 seconds...");
        Thread.Sleep(40000);

        Console.WriteLine("Exiting suspend mode by deleting all suspend media...");
        bool clearSuccess = await controller.ClearSuspendModeWithResponse();
        if (!clearSuccess)
        {
            Console.WriteLine("Failed to clear suspend mode");
            return false;
        }

        Console.WriteLine("Suspend mode demo completed successfully!");
        return true;
    }, timeout: TimeSpan.FromSeconds(10));

    // press enter to exit
    Console.WriteLine("Press Enter to exit...");
    Console.ReadLine();

    // Clean shutdown
    multiDeviceManager.Dispose();
}


// Add this class to handle the keep-alive timer
public class KeepAliveTimer : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly Func<Task> _keepAliveAction;
    private bool _disposed;

    public KeepAliveTimer(Func<Task> keepAliveAction, double intervalSeconds = 4.0)
    {
        _keepAliveAction = keepAliveAction ?? throw new ArgumentNullException(nameof(keepAliveAction));

        _timer = new System.Timers.Timer(intervalSeconds * 1000); // Convert to milliseconds
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
    }

    public async Task StartAsync()
    {
        // Send first keep-alive immediately
        await _keepAliveAction();

        // Start the timer for subsequent calls
        _timer.Start();
        Console.WriteLine($"Keep-alive timer started. Will send commands every {_timer.Interval / 1000} seconds.");
    }

    public void Stop()
    {
        _timer.Stop();
        Console.WriteLine("Keep-alive timer stopped.");
    }

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            await _keepAliveAction();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending keep-alive command: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _disposed = true;
        }
    }
}

