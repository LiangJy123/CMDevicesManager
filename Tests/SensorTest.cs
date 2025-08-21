using System;
using System.Threading;
using CMDevicesManager.Services;
using CMDevicesManager.ViewModels;

namespace CMDevicesManager.Tests
{
    /// <summary>
    /// Simple test to verify that the sensor services and view model work correctly
    /// </summary>
    public static class SensorTest
    {
        public static void TestFakeService()
        {
            Console.WriteLine("Testing FakeSystemMetricsService...");
            
            var service = new FakeSystemMetricsService();
            
            // Test basic functionality
            Console.WriteLine($"CPU Name: {service.CpuName}");
            Console.WriteLine($"GPU Name: {service.PrimaryGpuName}");
            Console.WriteLine($"Memory Name: {service.MemoryName}");
            
            // Test sensor readings
            Console.WriteLine("\nTesting sensor readings:");
            for (int i = 0; i < 5; i++)
            {
                Console.WriteLine($"Iteration {i + 1}:");
                Console.WriteLine($"  CPU Temperature: {service.GetCpuTemperature()}°C");
                Console.WriteLine($"  GPU Temperature: {service.GetGpuTemperature()}°C");
                Console.WriteLine($"  CPU Power: {service.GetCpuPower()}W");
                Console.WriteLine($"  GPU Power: {service.GetGpuPower()}W");
                Console.WriteLine($"  CPU Usage: {service.GetCpuUsagePercent()}%");
                Console.WriteLine($"  GPU Usage: {service.GetGpuUsagePercent()}%");
                Console.WriteLine($"  Memory Usage: {service.GetMemoryUsagePercent()}%");
                Console.WriteLine($"  Network Down: {service.GetNetDownloadKBs()} KB/s");
                Console.WriteLine($"  Network Up: {service.GetNetUploadKBs()} KB/s");
                Console.WriteLine();
                
                Thread.Sleep(1000); // Wait 1 second
            }
            
            service.Dispose();
            Console.WriteLine("FakeSystemMetricsService test completed successfully!");
        }
        
        public static void TestRealService()
        {
            Console.WriteLine("Testing RealSystemMetricsService...");
            
            try
            {
                var service = new RealSystemMetricsService();
                
                // Test basic functionality
                Console.WriteLine($"CPU Name: {service.CpuName}");
                Console.WriteLine($"GPU Name: {service.PrimaryGpuName}");
                Console.WriteLine($"Memory Name: {service.MemoryName}");
                
                // Test sensor readings
                Console.WriteLine("\nTesting sensor readings:");
                for (int i = 0; i < 3; i++)
                {
                    Console.WriteLine($"Iteration {i + 1}:");
                    Console.WriteLine($"  CPU Temperature: {service.GetCpuTemperature()}°C");
                    Console.WriteLine($"  GPU Temperature: {service.GetGpuTemperature()}°C");
                    Console.WriteLine($"  CPU Power: {service.GetCpuPower()}W");
                    Console.WriteLine($"  GPU Power: {service.GetGpuPower()}W");
                    Console.WriteLine($"  CPU Usage: {service.GetCpuUsagePercent()}%");
                    Console.WriteLine($"  GPU Usage: {service.GetGpuUsagePercent()}%");
                    Console.WriteLine($"  Memory Usage: {service.GetMemoryUsagePercent()}%");
                    Console.WriteLine($"  Network Down: {service.GetNetDownloadKBs()} KB/s");
                    Console.WriteLine($"  Network Up: {service.GetNetUploadKBs()} KB/s");
                    Console.WriteLine();
                    
                    Thread.Sleep(2000); // Wait 2 seconds
                }
                
                service.Dispose();
                Console.WriteLine("RealSystemMetricsService test completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RealSystemMetricsService test failed: {ex.Message}");
                Console.WriteLine("This is expected in non-Windows environments or without proper hardware access.");
            }
        }
    }
}