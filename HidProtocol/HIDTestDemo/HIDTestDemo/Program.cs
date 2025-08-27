// See https://aka.ms/new-console-template for more information
using HidApi;
using System.Text;

using HID.DisplayController;


Console.WriteLine("Hello, World!");

Console.WriteLine("=== HID Device Monitoring Demo ===");

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
deviceMonitor.SetDeviceFilter(0x2516, 0x0228);
deviceMonitor.MonitoringInterval = 500; // Check every 500ms

Console.WriteLine("Starting HID device monitoring...");
deviceMonitor.StartMonitoring();

// Wait for user input to stop monitoring
Console.WriteLine("Press Enter to stop monitoring...");
Console.ReadLine();

var displayController = new DisplayController(0x2516, 0x0228, "1870989178c08180");

// get device info
var deviceInfo = displayController.GetDeviceInfo();
if (deviceInfo != null)
{
    Console.WriteLine($"Device Info: {deviceInfo}");
}

// Get display capabilities
var capabilities = displayController.GetDisplayCtrlCapabilities(debug: false);
if (capabilities != null)
{
    Console.WriteLine($"Display Capabilities: {capabilities}");
}

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

// Example: real time mode.
if (false)
{
    // set SendCmdRealTimeDisplay 
    displayController.SendCmdRealTimeDisplay(true, sequenceNumber: 43);
    Thread.Sleep(500);
    // send displaybrightness 80
    displayController.SendCmdBrightness(80, sequenceNumber: 44);
    Thread.Sleep(500);

    // get timeout value
    displayController.SendCmdReadKeepAliveTimer(sequenceNumber: 46);
    Thread.Sleep(500);
    // set timeout value to 60 seconds
    int timeoutValue = 60;
    //displayController.SendCmdSetKeepAliveTimer(timeoutValue, sequenceNumber: 47);
    //Thread.Sleep(500);

    byte transferId = 3;
    int fileIndex = 1;
    while (true)
    {
        // send "E:\github\CMDevicesManager\HidProtocol\resources\osd3d-square-480.png" by SendFileFromDisk
        // fine file path based on fileIndex
        //string filePath = @"E:\github\CMDevicesManager\HidProtocol\resources\0 (1).jpg";
        // cycle through 0 (1).jpg to 0 (7).jpg or png
        //string filePath = $@"C:\Users\mxxmu\Desktop\MyLed\CMDevicesManager\HidProtocol\resources\0 ({fileIndex}).jpg";
        string filePath = $@"E:\github\CMDevicesManager\HidProtocol\resources\0 ({fileIndex}).jpg";
        // check if file exists
        if (!File.Exists(filePath))
        {
            filePath = filePath.Replace(".jpg", ".png");
        }

        Console.WriteLine($"Sending file: {filePath} with transferId: {transferId}");
        if (File.Exists(filePath))
        {
            displayController.SendFileFromDisk(filePath, transferId: transferId);

            // send time stamp
            displayController.SendCmdKeepAlive(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), sequenceNumber: 45);
            //Thread.Sleep((timeoutValue*1000)-50); // wait for 10 seconds before sending again
            Thread.Sleep(10);
        }
        else
        {
            Console.WriteLine($"File not found: {filePath}");
            break;
        }
        transferId++;
        // if transferId > 59, reset to 3
        if (transferId > 59) transferId = 3;
        fileIndex++;
        if (fileIndex > 7) fileIndex = 1;
    }

}

if(true)
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
