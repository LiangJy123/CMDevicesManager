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
            Console.WriteLine("Supported Features: Real-time Display, Suspend Mode, Get Device Status");
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

                Console.WriteLine("Available Features:");
                Console.WriteLine("1. Get Device Status from All Devices");
                Console.WriteLine("2. Real-time Display Tests");
                Console.WriteLine("3. Suspend Mode Tests");
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
                            await GetDeviceStatusMenu();
                            break;
                        case "2":
                            await RealTimeDisplayTestsMenu();
                            break;
                        case "3":
                            await SuspendModeTestsMenu();
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

        private static async Task GetDeviceStatusMenu()
        {
            Console.Clear();
            Console.WriteLine("=== Get Device Status ===");
            Console.WriteLine();

            Console.WriteLine("Getting device status from all connected devices...");
            await TestGetDeviceStatus();

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        private static async Task RealTimeDisplayTestsMenu()
        {
            Console.Clear();
            Console.WriteLine("=== Real-time Display Tests ===");
            Console.WriteLine("1. Simple Real-time Demo (5 cycles)");
            Console.WriteLine("2. Extended Real-time Demo (20 cycles)");
            Console.WriteLine("3. Real-time with Custom Images Directory");
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
            Console.WriteLine("1. Suspend Mode with Custom Folder");
            Console.WriteLine("0. Back to Main Menu");
            Console.WriteLine();

            Console.Write("Select option: ");
            var input = Console.ReadLine();

            switch (input)
            {
                case "1":
                    await TestSuspendModeWithCustomFolder();
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
            Console.WriteLine("Retrieving device status from all connected devices...");
            Console.WriteLine();

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
                    Console.WriteLine($"  Display In Sleep: {(status.IsDisplayInSleep ? "Yes" : "No")}");
                    Console.WriteLine($"  Active Suspend Media Count: {status.ActiveSuspendMediaCount}");
                    if (status.HasActiveSuspendMedia)
                    {
                        var activeIndices = string.Join(", ", status.ActiveSuspendMediaIndices);
                        Console.WriteLine($"  Active Suspend Media Indices: [{activeIndices}]");
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  Status: FAILED TO RETRIEVE");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }
        }

        private static async Task TestRealTimeDisplay(int cycles)
        {
            Console.WriteLine($"Testing real-time display with {cycles} cycles...");

            // Get image directory or use default
            var imageDirectory = GetTestImageDirectory();
            if (string.IsNullOrEmpty(imageDirectory))
            {
                Console.WriteLine("No image directory provided. Cannot proceed with real-time display test.");
                return;
            }

            Console.WriteLine($"Using image directory: {imageDirectory}");
            Console.WriteLine("Starting real-time display demo on all devices...");

            var results = await _multiDeviceManager!.ExecuteOnAllDevices(async controller =>
            {
                return await controller.SimpleRealTimeDisplayDemo(
                    imageDirectory: imageDirectory,
                    cycleCount: cycles
                );
            }, timeout: TimeSpan.FromMinutes(5));

            PrintResults(results);
        }

        private static async Task TestRealTimeDisplayCustom()
        {
            Console.WriteLine("Real-time Display with Custom Images Directory");
            Console.WriteLine();

            // Get custom directory from user
            string? customDirectory = GetUserDirectoryPath("images directory for real-time display");
            if (string.IsNullOrEmpty(customDirectory))
            {
                Console.WriteLine("No directory provided. Test cancelled.");
                return;
            }

            // Check if directory contains image files
            var imageFiles = Directory.GetFiles(customDirectory, "*.*")
                .Where(file => file.ToLowerInvariant().EndsWith(".jpg") ||
                              file.ToLowerInvariant().EndsWith(".jpeg") ||
                              file.ToLowerInvariant().EndsWith(".png"))
                .ToArray();

            if (imageFiles.Length == 0)
            {
                Console.WriteLine($"No image files (JPG/PNG) found in directory: {customDirectory}");
                return;
            }

            Console.WriteLine($"Found {imageFiles.Length} image files in the directory.");
            Console.Write("Enter number of cycles (default 5): ");
            var cyclesInput = Console.ReadLine();
            var cycles = int.TryParse(cyclesInput, out var c) ? c : 5;

            Console.WriteLine($"Starting real-time display with {cycles} cycles...");

            var results = await _multiDeviceManager!.ExecuteOnAllDevices(async controller =>
            {
                return await controller.SimpleRealTimeDisplayDemo(
                    imageDirectory: customDirectory,
                    cycleCount: cycles
                );
            }, timeout: TimeSpan.FromMinutes(10));

            PrintResults(results);
        }

        private static async Task TestSuspendModeWithCustomFolder()
        {
            Console.WriteLine("Suspend Mode with Custom Folder");
            Console.WriteLine();
            Console.WriteLine("Please provide a folder containing suspend media files (images/videos):");

            string? folderPath = GetUserDirectoryPath("suspend media folder");
            if (string.IsNullOrEmpty(folderPath))
            {
                Console.WriteLine("No folder path provided. Test cancelled.");
                return;
            }

            // Get supported media files from the folder
            var mediaFiles = Directory.GetFiles(folderPath, "*.*")
                .Where(file =>
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".mp4";
                })
                .ToList();

            if (mediaFiles.Count == 0)
            {
                Console.WriteLine($"No supported media files found in folder: {folderPath}");
                Console.WriteLine("Supported formats: JPG, PNG, MP4");
                return;
            }

            Console.WriteLine($"Found {mediaFiles.Count} media files:");
            foreach (var file in mediaFiles)
            {
                Console.WriteLine($"  - {Path.GetFileName(file)}");
            }

            Console.WriteLine();
            Console.WriteLine("Starting suspend mode with custom files on all devices...");

            var results = await _multiDeviceManager!.ExecuteOnAllDevices(async controller =>
            {
                try
                {
                    // Step 1: Enter suspend mode
                    bool suspendModeSet = await controller.SetSuspendModeWithResponse();
                    if (!suspendModeSet)
                    {
                        Console.WriteLine("Failed to set suspend mode");
                        return false;
                    }

                    // Step 2: Send media files
                    bool filesSuccess = await controller.SendMultipleSuspendFilesWithResponse(mediaFiles, startingTransferId: 2);
                    if (!filesSuccess)
                    {
                        Console.WriteLine("Failed to send suspend files");
                        return false;
                    }

                    // Step 3: Set brightness and keep alive timer
                    await controller.SendCmdBrightnessWithResponse(80);
                    await controller.SendCmdSetKeepAliveTimerWithResponse(5);

                    Console.WriteLine("Suspend mode activated. Will run for 60 seconds...");
                    await Task.Delay(60000); // Run for 60 seconds

                    // Step 4: Clear suspend mode
                    bool clearSuccess = await controller.ClearSuspendModeWithResponse();
                    if (!clearSuccess)
                    {
                        Console.WriteLine("Failed to clear suspend mode");
                        return false;
                    }

                    Console.WriteLine("Suspend mode completed successfully!");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in suspend mode: {ex.Message}");
                    return false;
                }
            }, timeout: TimeSpan.FromMinutes(5));

            PrintResults(results);
        }


        // Helper methods
        private static void PrintResults(Dictionary<string, bool> results)
        {
            Console.WriteLine();
            Console.WriteLine("Results:");
            foreach (var result in results)
            {
                var status = result.Value ? "SUCCESS" : "FAILED";
                var color = result.Value ? ConsoleColor.Green : ConsoleColor.Red;

                Console.ForegroundColor = color;
                Console.WriteLine($"  {result.Key}: {status}");
                Console.ResetColor();
            }
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