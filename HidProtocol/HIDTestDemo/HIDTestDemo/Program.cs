using HID.DisplayController;
using HidApi;
using System.Text;
using System.Timers;

namespace MultiDeviceTestApp
{
    class Program
    {
        private static MultiDeviceManager? _multiDeviceManager;
        private static readonly object _consoleLock = new object();

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== MultiDeviceManager API Test Console ===");
            Console.WriteLine("This application tests the MultiDeviceManager API functionality");
            Console.WriteLine();

            try
            {
                // Initialize MultiDeviceManager
                _multiDeviceManager = new MultiDeviceManager(0x2516, 0x0228);
                SetupEventHandlers();

                // Start monitoring
                Console.WriteLine("Starting device monitoring...");
                _multiDeviceManager.StartMonitoring();

                // Wait a moment for initial device detection
                await Task.Delay(2000);

                // Show main menu
                await ShowMainMenu();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                CleanupAndExit();
            }
        }

        private static void SetupEventHandlers()
        {
            if (_multiDeviceManager == null) return;

            _multiDeviceManager.ControllerAdded += (sender, e) =>
            {
                lock (_consoleLock)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[DEVICE ADDED] {e.Device.ManufacturerString} {e.Device.ProductString}");
                    Console.WriteLine($"               Serial: {e.Device.SerialNumber}");
                    Console.WriteLine($"               Path: {e.Device.Path}");
                    Console.ResetColor();
                    Console.WriteLine();
                }
            };

            _multiDeviceManager.ControllerRemoved += (sender, e) =>
            {
                lock (_consoleLock)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[DEVICE REMOVED] {e.Device.ManufacturerString} {e.Device.ProductString}");
                    Console.WriteLine($"                 Serial: {e.Device.SerialNumber}");
                    Console.ResetColor();
                    Console.WriteLine();
                }
            };

            _multiDeviceManager.ControllerError += (sender, e) =>
            {
                lock (_consoleLock)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[DEVICE ERROR] {e.Device.ProductString}: {e.Exception.Message}");
                    Console.ResetColor();
                    Console.WriteLine();
                }
            };
        }

        private static async Task ShowMainMenu()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== MultiDeviceManager Test Menu ===");
                Console.WriteLine();

                // Show connected devices
                var controllers = _multiDeviceManager?.GetActiveControllers() ?? new List<DisplayController>();
                Console.WriteLine($"Connected Devices: {controllers.Count}");
                for (int i = 0; i < controllers.Count; i++)
                {
                    var device = controllers[i].DeviceInfo;
                    Console.WriteLine($"  {i + 1}. {device.ProductString} (SN: {device.SerialNumber})");
                }
                Console.WriteLine();

                Console.WriteLine("Test Categories:");
                Console.WriteLine("1. Basic Device Operations");
                Console.WriteLine("2. Display Control Tests");
                Console.WriteLine("3. File Transfer Tests");
                Console.WriteLine("4. Real-time Display Tests");
                Console.WriteLine("5. Suspend Mode Tests");
                Console.WriteLine("6. Device Information Tests");
                Console.WriteLine("7. Custom Command Tests");
                Console.WriteLine("8. Stress Tests");
                Console.WriteLine("9. Device Management");
                Console.WriteLine("0. Exit");
                Console.WriteLine();

                if (controllers.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("No devices connected! Please connect devices and try again.");
                    Console.ResetColor();
                }

                Console.Write("Select option: ");
                var input = Console.ReadLine();

                try
                {
                    switch (input)
                    {
                        case "1":
                            await BasicDeviceOperationsMenu();
                            break;
                        case "2":
                            await DisplayControlTestsMenu();
                            break;
                        case "3":
                            await FileTransferTestsMenu();
                            break;
                        case "4":
                            await RealTimeDisplayTestsMenu();
                            break;
                        case "5":
                            await SuspendModeTestsMenu();
                            break;
                        case "6":
                            await DeviceInformationTestsMenu();
                            break;
                        case "7":
                            await CustomCommandTestsMenu();
                            break;
                        case "8":
                            await StressTestsMenu();
                            break;
                        case "9":
                            await DeviceManagementMenu();
                            break;
                        case "0":
                            return;
                        default:
                            Console.WriteLine("Invalid option. Press any key to continue...");
                            Console.ReadKey();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error executing test: {ex.Message}");
                    Console.ResetColor();
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                }
            }
        }

        private static async Task BasicDeviceOperationsMenu()
        {
            Console.Clear();
            Console.WriteLine("=== Basic Device Operations ===");
            Console.WriteLine("1. Get Device Status from All");
            Console.WriteLine("2. Set Brightness on All (50%)");
            Console.WriteLine("3. Set Rotation on All (90°)");
            Console.WriteLine("4. Reset Rotation on All (0°)");
            Console.WriteLine("5. Test Keep Alive on All");
            Console.WriteLine("6. Read Current Settings");
            Console.WriteLine("0. Back to Main Menu");
            Console.WriteLine();

            Console.Write("Select option: ");
            var input = Console.ReadLine();

            switch (input)
            {
                case "1":
                    await TestGetDeviceStatus();
                    break;
                case "2":
                    await TestSetBrightness(50);
                    break;
                case "3":
                    await TestSetRotation(90);
                    break;
                case "4":
                    await TestSetRotation(0);
                    break;
                case "5":
                    await TestKeepAlive();
                    break;
                case "6":
                    await TestReadCurrentSettings();
                    break;
                case "0":
                    return;
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        private static async Task DisplayControlTestsMenu()
        {
            Console.Clear();
            Console.WriteLine("=== Display Control Tests ===");
            Console.WriteLine("1. Brightness Test (0% -> 20% -> 80% -> 50%)");
            Console.WriteLine("2. Rotation Test (0° -> 90° -> 180° -> 270° -> 0°)");
            Console.WriteLine("3. Display Sleep Test (Enable -> Wait -> Disable)");
            Console.WriteLine("4. Combined Display Settings Test");
            Console.WriteLine("5. Display Performance Test");
            Console.WriteLine("0. Back to Main Menu");
            Console.WriteLine();

            Console.Write("Select option: ");
            var input = Console.ReadLine();

            switch (input)
            {
                case "1":
                    await TestBrightnessSequence();
                    break;
                case "2":
                    await TestRotationSequence();
                    break;
                case "3":
                    await TestDisplaySleep();
                    break;
                case "4":
                    await TestCombinedDisplaySettings();
                    break;
                case "5":
                    await TestDisplayPerformance();
                    break;
                case "0":
                    return;
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        private static async Task FileTransferTestsMenu()
        {
            Console.Clear();
            Console.WriteLine("=== File Transfer Tests ===");
            Console.WriteLine("1. Transfer Test Image to All Devices");
            Console.WriteLine("2. Background Image Test");
            Console.WriteLine("3. OSD File Test");
            Console.WriteLine("4. Multiple File Transfer Test");
            Console.WriteLine("5. File Transfer Performance Test");
            Console.WriteLine("0. Back to Main Menu");
            Console.WriteLine();

            Console.Write("Select option: ");
            var input = Console.ReadLine();

            switch (input)
            {
                case "1":
                    await TestFileTransfer();
                    break;
                case "2":
                    await TestBackgroundImage();
                    break;
                case "3":
                    await TestOsdFile();
                    break;
                case "4":
                    await TestMultipleFileTransfer();
                    break;
                case "5":
                    await TestFileTransferPerformance();
                    break;
                case "0":
                    return;
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        private static async Task RealTimeDisplayTestsMenu()
        {
            Console.Clear();
            Console.WriteLine("=== Real-time Display Tests ===");
            Console.WriteLine("1. Simple Real-time Demo (5 cycles)");
            Console.WriteLine("2. Extended Real-time Demo (20 cycles)");
            Console.WriteLine("3. Real-time with Custom Images");
            Console.WriteLine("4. Real-time Performance Test");
            Console.WriteLine("5. Real-time Multi-device Sync Test");
            Console.WriteLine("0. Back to Main Menu");
            Console.WriteLine();

            Console.Write("Select option: ");
            var input = Console.ReadLine();

            switch (input)
            {
                case "1":
                    await TestRealTimeDisplay(5);
                    break;
                case "2":
                    await TestRealTimeDisplay(20);
                    break;
                case "3":
                    await TestRealTimeDisplayCustom();
                    break;
                case "4":
                    await TestRealTimePerformance();
                    break;
                case "5":
                    await TestRealTimeSync();
                    break;
                case "0":
                    return;
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        private static async Task SuspendModeTestsMenu()
        {
            Console.Clear();
            Console.WriteLine("=== Suspend Mode Tests ===");
            Console.WriteLine("1. Basic Suspend Mode Test");
            Console.WriteLine("2. Multiple Files Suspend Test");
            Console.WriteLine("3. Suspend Mode with Timer Test");
            Console.WriteLine("4. Clear Suspend Files Test");
            Console.WriteLine("5. Suspend Mode Performance Test");
            Console.WriteLine("0. Back to Main Menu");
            Console.WriteLine();

            Console.Write("Select option: ");
            var input = Console.ReadLine();

            switch (input)
            {
                case "1":
                    await TestBasicSuspendMode();
                    break;
                case "2":
                    await TestMultipleSuspendFiles();
                    break;
                case "3":
                    await TestSuspendModeWithTimer();
                    break;
                case "4":
                    await TestClearSuspendFiles();
                    break;
                case "5":
                    await TestSuspendModePerformance();
                    break;
                case "0":
                    return;
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        private static async Task DeviceInformationTestsMenu()
        {
            Console.Clear();
            Console.WriteLine("=== Device Information Tests ===");
            Console.WriteLine("1. Get All Device Serial Numbers");
            Console.WriteLine("2. Get All Device Color SKUs");
            Console.WriteLine("3. Get Device Firmware Info");
            Console.WriteLine("4. Get Device Capabilities");
            Console.WriteLine("5. Set Device Serial Numbers");
            Console.WriteLine("6. Set Color SKUs");
            Console.WriteLine("0. Back to Main Menu");
            Console.WriteLine();

            Console.Write("Select option: ");
            var input = Console.ReadLine();

            switch (input)
            {
                case "1":
                    await TestGetSerialNumbers();
                    break;
                case "2":
                    await TestGetColorSkus();
                    break;
                case "3":
                    await TestGetFirmwareInfo();
                    break;
                case "4":
                    await TestGetCapabilities();
                    break;
                case "5":
                    await TestSetSerialNumbers();
                    break;
                case "6":
                    await TestSetColorSkus();
                    break;
                case "0":
                    return;
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        private static async Task CustomCommandTestsMenu()
        {
            Console.Clear();
            Console.WriteLine("=== Custom Command Tests ===");
            Console.WriteLine("1. Execute Custom Lambda on All Devices");
            Console.WriteLine("2. Execute on Specific Devices (by Serial)");
            Console.WriteLine("3. Timeout Test");
            Console.WriteLine("4. Error Handling Test");
            Console.WriteLine("5. Parallel Execution Test");
            Console.WriteLine("0. Back to Main Menu");
            Console.WriteLine();

            Console.Write("Select option: ");
            var input = Console.ReadLine();

            switch (input)
            {
                case "1":
                    await TestCustomLambda();
                    break;
                case "2":
                    await TestSpecificDevices();
                    break;
                case "3":
                    await TestTimeout();
                    break;
                case "4":
                    await TestErrorHandling();
                    break;
                case "5":
                    await TestParallelExecution();
                    break;
                case "0":
                    return;
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        private static async Task StressTestsMenu()
        {
            Console.Clear();
            Console.WriteLine("=== Stress Tests ===");
            Console.WriteLine("1. Rapid Command Test (100 commands)");
            Console.WriteLine("2. Long Duration Test (10 minutes)");
            Console.WriteLine("3. Memory Usage Test");
            Console.WriteLine("4. Connection Stability Test");
            Console.WriteLine("5. Concurrent Operations Test");
            Console.WriteLine("0. Back to Main Menu");
            Console.WriteLine();

            Console.Write("Select option: ");
            var input = Console.ReadLine();

            switch (input)
            {
                case "1":
                    await TestRapidCommands();
                    break;
                case "2":
                    await TestLongDuration();
                    break;
                case "3":
                    await TestMemoryUsage();
                    break;
                case "4":
                    await TestConnectionStability();
                    break;
                case "5":
                    await TestConcurrentOperations();
                    break;
                case "0":
                    return;
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        private static async Task DeviceManagementMenu()
        {
            Console.Clear();
            Console.WriteLine("=== Device Management ===");
            Console.WriteLine("1. Refresh Device List");
            Console.WriteLine("2. Get Device Count");
            Console.WriteLine("3. List All Controllers");
            Console.WriteLine("4. Get Controller by Serial");
            Console.WriteLine("5. Test Event Handlers");
            Console.WriteLine("6. Restart Monitoring");
            Console.WriteLine("0. Back to Main Menu");
            Console.WriteLine();

            Console.Write("Select option: ");
            var input = Console.ReadLine();

            switch (input)
            {
                case "1":
                    await TestRefreshDeviceList();
                    break;
                case "2":
                    await TestGetDeviceCount();
                    break;
                case "3":
                    await TestListControllers();
                    break;
                case "4":
                    await TestGetControllerBySerial();
                    break;
                case "5":
                    await TestEventHandlers();
                    break;
                case "6":
                    await TestRestartMonitoring();
                    break;
                case "0":
                    return;
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        // Test implementations
        private static async Task TestGetDeviceStatus()
        {
            Console.WriteLine("Testing device status retrieval...");
            var results = await _multiDeviceManager!.GetStatusFromAllDevices();

            foreach (var result in results)
            {
                Console.WriteLine($"Device {result.Key}:");
                if (result.Value.HasValue)
                {
                    var status = result.Value.Value;
                    Console.WriteLine($"  Brightness: {status.Brightness}%");
                    Console.WriteLine($"  Rotation: {status.Degree}°");
                    Console.WriteLine($"  Keep Alive Timeout: {status.KeepAliveTimeout}s");
                    Console.WriteLine($"  Max Suspend Media: {status.MaxSuspendMediaCount}");
                }
                else
                {
                    Console.WriteLine("  Status: FAILED");
                }
            }
        }

        private static async Task TestSetBrightness(int brightness)
        {
            Console.WriteLine($"Setting brightness to {brightness}% on all devices...");
            var results = await _multiDeviceManager!.SetBrightnessOnAllDevices(brightness);
            PrintResults(results);
        }

        private static async Task TestSetRotation(int degrees)
        {
            Console.WriteLine($"Setting rotation to {degrees}° on all devices...");
            var results = await _multiDeviceManager!.SetRotationOnAllDevices(degrees);
            PrintResults(results);
        }

        private static async Task TestKeepAlive()
        {
            Console.WriteLine("Sending keep alive to all devices...");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var results = await _multiDeviceManager!.SendKeepAliveOnAllDevices(timestamp);
            PrintResults(results);
        }

        private static async Task TestReadCurrentSettings()
        {
            Console.WriteLine("Reading current settings from all devices...");

            Console.WriteLine("\n1. Reading brightness...");
            var brightnessResults = await _multiDeviceManager!.ReadBrightnessFromAllDevices();
            PrintResults(brightnessResults);

            Console.WriteLine("\n2. Reading rotation...");
            var rotationResults = await _multiDeviceManager!.ReadRotatedAngleFromAllDevices();
            PrintResults(rotationResults);

            Console.WriteLine("\n3. Reading keep alive timer...");
            var timerResults = await _multiDeviceManager!.ReadKeepAliveTimerFromAllDevices();
            PrintResults(timerResults);
        }

        private static async Task TestBrightnessSequence()
        {
            Console.WriteLine("Testing brightness sequence: 0% -> 20% -> 80% -> 50%");

            await TestSetBrightness(0);
            await Task.Delay(5000);

            await TestSetBrightness(20);
            await Task.Delay(2000);

            await TestSetBrightness(80);
            await Task.Delay(2000);

            await TestSetBrightness(50);
        }

        private static async Task TestRotationSequence()
        {
            Console.WriteLine("Testing rotation sequence: 0° -> 90° -> 180° -> 270° -> 0°");

            int[] rotations = { 0, 90, 180, 270, 0 };
            foreach (var rotation in rotations)
            {
                await TestSetRotation(rotation);
                await Task.Delay(2000);
            }
        }

        private static async Task TestDisplaySleep()
        {
            Console.WriteLine("Testing display sleep mode...");

            Console.WriteLine("Enabling display sleep...");
            var enableResults = await _multiDeviceManager!.SetDisplayInSleepOnAllDevices(true);
            PrintResults(enableResults);

            Console.WriteLine("Waiting 5 seconds...");
            await Task.Delay(5000);

            Console.WriteLine("Disabling display sleep...");
            var disableResults = await _multiDeviceManager!.SetDisplayInSleepOnAllDevices(false);
            PrintResults(disableResults);
        }

        private static async Task TestCombinedDisplaySettings()
        {
            Console.WriteLine("Testing combined display settings...");

            // Set brightness and rotation together
            Console.WriteLine("Setting brightness to 75% and rotation to 90°...");
            var brightnessTask = _multiDeviceManager!.SetBrightnessOnAllDevices(75);
            var rotationTask = _multiDeviceManager!.SetRotationOnAllDevices(90);

            await Task.WhenAll(brightnessTask, rotationTask);

            Console.WriteLine("Brightness results:");
            PrintResults(await brightnessTask);
            Console.WriteLine("Rotation results:");
            PrintResults(await rotationTask);
        }

        private static async Task TestDisplayPerformance()
        {
            Console.WriteLine("Testing display performance (rapid brightness changes)...");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < 10; i++)
            {
                var brightness = 20 + (i * 8); // 20, 28, 36, ..., 92
                Console.WriteLine($"Setting brightness to {brightness}%...");
                await _multiDeviceManager!.SetBrightnessOnAllDevices(brightness);
                await Task.Delay(500);
            }

            stopwatch.Stop();
            Console.WriteLine($"Performance test completed in {stopwatch.ElapsedMilliseconds}ms");
        }

        private static async Task TestFileTransfer()
        {
            Console.WriteLine("Testing file transfer...");

            string testImagePath = GetTestImagePath();
            if (string.IsNullOrEmpty(testImagePath))
            {
                Console.WriteLine("No test image found. Please ensure test images exist in resources folder.");
                return;
            }

            Console.WriteLine($"Transferring {Path.GetFileName(testImagePath)} to all devices...");
            var results = await _multiDeviceManager!.TransferFileToAllDevices(testImagePath);
            PrintResults(results);
        }

        private static async Task TestRealTimeDisplay(int cycles)
        {
            Console.WriteLine($"Testing real-time display with {cycles} cycles...");
            var imageDirectory = GetTestImageDirectory();

            var results = await _multiDeviceManager!.ExecuteOnAllDevices(async controller =>
            {
                return await controller.SimpleRealTimeDisplayDemo(
                    imageDirectory: imageDirectory,
                    cycleCount: cycles
                );
            }, timeout: TimeSpan.FromMinutes(5));

            PrintResults(results);
        }

        private static async Task TestBasicSuspendMode()
        {
            Console.WriteLine("Testing basic suspend mode...");

            var results = await _multiDeviceManager!.ExecuteOnAllDevices(async controller =>
            {
                return await controller.SetSuspendModeDemo();
            }, timeout: TimeSpan.FromMinutes(2));

            PrintResults(results);
        }

        private static async Task TestGetSerialNumbers()
        {
            Console.WriteLine("Getting serial numbers from all devices...");
            var results = await _multiDeviceManager!.GetDeviceSerialNumberFromAllDevices();
            PrintResults(results);
        }

        private static async Task TestCustomLambda()
        {
            Console.WriteLine("Executing custom lambda on all devices...");

            var results = await _multiDeviceManager!.ExecuteOnAllDevices(async controller =>
            {
                // Custom operation: Set brightness to 60%, then get status
                await controller.SendCmdBrightnessWithResponse(60);
                var status = await controller.GetDeviceStatus();
                Console.WriteLine($"Device brightness set to 60%, current status: {status?.Brightness}%");
                return true;
            });

            PrintResults(results);
        }

        private static async Task TestRapidCommands()
        {
            Console.WriteLine("Testing rapid commands (100 brightness changes)...");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < 100; i++)
            {
                var brightness = 20 + (i % 60); // Cycle between 20-80%
                await _multiDeviceManager!.SetBrightnessOnAllDevices(brightness);

                if (i % 10 == 0)
                {
                    Console.WriteLine($"Completed {i + 1}/100 commands...");
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"Rapid command test completed in {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"Average: {stopwatch.ElapsedMilliseconds / 100.0:F2}ms per command");
        }

        private static async Task TestRefreshDeviceList()
        {
            Console.WriteLine("Refreshing device list...");

            var beforeCount = _multiDeviceManager!.GetActiveControllers().Count;
            Console.WriteLine($"Devices before refresh: {beforeCount}");

            // Stop and restart monitoring to refresh
            _multiDeviceManager.StopMonitoring();
            await Task.Delay(1000);
            _multiDeviceManager.StartMonitoring();
            await Task.Delay(2000);

            var afterCount = _multiDeviceManager.GetActiveControllers().Count;
            Console.WriteLine($"Devices after refresh: {afterCount}");
        }

        // Helper methods
        private static void PrintResults(Dictionary<string, bool> results)
        {
            foreach (var result in results)
            {
                var status = result.Value ? "SUCCESS" : "FAILED";
                var color = result.Value ? ConsoleColor.Green : ConsoleColor.Red;

                Console.ForegroundColor = color;
                Console.WriteLine($"  {result.Key}: {status}");
                Console.ResetColor();
            }
        }

        private static string GetTestImagePath()
        {
            // First try the local resources directory
            var localPath = Path.Combine(AppContext.BaseDirectory, "resources", "test.jpg");
            if (File.Exists(localPath))
            {
                return localPath;
            }

            // If no local file found, prompt user for input
            Console.WriteLine();
            Console.WriteLine("No test image found in local resources directory.");
            Console.Write("Please enter the full path to a test image file (JPG/PNG): ");
            
            string? userPath = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(userPath))
            {
                Console.WriteLine("No path provided.");
                return string.Empty;
            }

            if (!File.Exists(userPath))
            {
                Console.WriteLine($"File not found: {userPath}");
                return string.Empty;
            }

            // Validate file extension
            var extension = Path.GetExtension(userPath).ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
            {
                Console.WriteLine("Invalid file type. Please provide a JPG or PNG file.");
                return string.Empty;
            }

            return userPath;
        }

        private static string GetTestImageDirectory()
        {
            // First try the local resources directory
            var localPath = Path.Combine(AppContext.BaseDirectory, "resources", "realtime");
            if (Directory.Exists(localPath))
            {
                return localPath;
            }

            // If no local directory found, prompt user for input
            Console.WriteLine();
            Console.WriteLine("No realtime images directory found in local resources.");
            Console.Write("Please enter the full path to a directory containing image files: ");
            
            string? userPath = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(userPath))
            {
                Console.WriteLine("No path provided.");
                return string.Empty;
            }

            if (!Directory.Exists(userPath))
            {
                Console.WriteLine($"Directory not found: {userPath}");
                return string.Empty;
            }

            // Check if directory contains image files
            var imageFiles = Directory.GetFiles(userPath, "*.*")
                .Where(file => file.ToLowerInvariant().EndsWith(".jpg") || 
                              file.ToLowerInvariant().EndsWith(".jpeg") || 
                              file.ToLowerInvariant().EndsWith(".png"))
                .ToArray();

            if (imageFiles.Length == 0)
            {
                Console.WriteLine($"No image files (JPG/PNG) found in directory: {userPath}");
                return string.Empty;
            }

            Console.WriteLine($"Found {imageFiles.Length} image files in the directory.");
            return userPath;
        }

        // Additional helper method for getting file paths with user prompts
        private static string GetUserFilePath(string fileType = "file")
        {
            Console.WriteLine();
            Console.Write($"Please enter the full path to the {fileType}: ");
            
            string? userPath = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(userPath))
            {
                Console.WriteLine("No path provided.");
                return string.Empty;
            }

            if (!File.Exists(userPath))
            {
                Console.WriteLine($"File not found: {userPath}");
                return string.Empty;
            }

            return userPath;
        }

        // Additional helper method for getting directory paths with user prompts
        private static string GetUserDirectoryPath(string directoryType = "directory")
        {
            Console.WriteLine();
            Console.Write($"Please enter the full path to the {directoryType}: ");
            
            string? userPath = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(userPath))
            {
                Console.WriteLine("No path provided.");
                return string.Empty;
            }

            if (!Directory.Exists(userPath))
            {
                Console.WriteLine($"Directory not found: {userPath}");
                return string.Empty;
            }

            return userPath;
        }

        private static void CleanupAndExit()
        {
            Console.WriteLine("\nCleaning up...");
            _multiDeviceManager?.Dispose();
            Console.WriteLine("Goodbye!");
        }

        // Placeholder implementations for remaining test methods
        private static async Task TestBackgroundImage() => Console.WriteLine("Background image test - Implementation needed");
        private static async Task TestOsdFile() => Console.WriteLine("OSD file test - Implementation needed");
        private static async Task TestMultipleFileTransfer() => Console.WriteLine("Multiple file transfer test - Implementation needed");
        private static async Task TestFileTransferPerformance() => Console.WriteLine("File transfer performance test - Implementation needed");
        private static async Task TestRealTimeDisplayCustom() => Console.WriteLine("Real-time display custom test - Implementation needed");
        private static async Task TestRealTimePerformance() => Console.WriteLine("Real-time performance test - Implementation needed");
        private static async Task TestRealTimeSync() => Console.WriteLine("Real-time sync test - Implementation needed");
        private static async Task TestMultipleSuspendFiles() => Console.WriteLine("Multiple suspend files test - Implementation needed");
        private static async Task TestSuspendModeWithTimer() => Console.WriteLine("Suspend mode with timer test - Implementation needed");
        private static async Task TestClearSuspendFiles() => Console.WriteLine("Clear suspend files test - Implementation needed");
        private static async Task TestSuspendModePerformance() => Console.WriteLine("Suspend mode performance test - Implementation needed");
        private static async Task TestGetColorSkus() => Console.WriteLine("Get color SKUs test - Implementation needed");
        private static async Task TestGetFirmwareInfo() => Console.WriteLine("Get firmware info test - Implementation needed");
        private static async Task TestGetCapabilities() => Console.WriteLine("Get capabilities test - Implementation needed");
        private static async Task TestSetSerialNumbers() => Console.WriteLine("Set serial numbers test - Implementation needed");
        private static async Task TestSetColorSkus() => Console.WriteLine("Set color SKUs test - Implementation needed");
        private static async Task TestSpecificDevices() => Console.WriteLine("Specific devices test - Implementation needed");
        private static async Task TestTimeout() => Console.WriteLine("Timeout test - Implementation needed");
        private static async Task TestErrorHandling() => Console.WriteLine("Error handling test - Implementation needed");
        private static async Task TestParallelExecution() => Console.WriteLine("Parallel execution test - Implementation needed");
        private static async Task TestLongDuration() => Console.WriteLine("Long duration test - Implementation needed");
        private static async Task TestMemoryUsage() => Console.WriteLine("Memory usage test - Implementation needed");
        private static async Task TestConnectionStability() => Console.WriteLine("Connection stability test - Implementation needed");
        private static async Task TestConcurrentOperations() => Console.WriteLine("Concurrent operations test - Implementation needed");
        private static async Task TestGetDeviceCount() => Console.WriteLine("Get device count test - Implementation needed");
        private static async Task TestListControllers() => Console.WriteLine("List controllers test - Implementation needed");
        private static async Task TestGetControllerBySerial() => Console.WriteLine("Get controller by serial test - Implementation needed");
        private static async Task TestEventHandlers() => Console.WriteLine("Event handlers test - Implementation needed");
        private static async Task TestRestartMonitoring() => Console.WriteLine("Restart monitoring test - Implementation needed");
    }

    // Helper class for keep-alive timer (reused from your original code)
    public class KeepAliveTimer : IDisposable
    {
        private readonly System.Timers.Timer _timer;
        private readonly Func<Task> _keepAliveAction;
        private bool _disposed;

        public KeepAliveTimer(Func<Task> keepAliveAction, double intervalSeconds = 4.0)
        {
            _keepAliveAction = keepAliveAction ?? throw new ArgumentNullException(nameof(keepAliveAction));

            _timer = new System.Timers.Timer(intervalSeconds * 1000);
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = true;
        }

        public async Task StartAsync()
        {
            await _keepAliveAction();
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
}