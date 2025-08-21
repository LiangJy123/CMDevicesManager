using System;
using CMDevicesManager.Tests;

namespace CMDevicesManager
{
    /// <summary>
    /// Simple test runner to demonstrate sensor functionality
    /// </summary>
    public static class TestRunner
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("CMDevicesManager Sensor Test");
            Console.WriteLine("============================");
            
            // Test the fake service first (this should always work)
            SensorTest.TestFakeService();
            
            Console.WriteLine("\n" + new string('=', 50) + "\n");
            
            // Test the real service (this might fail in non-Windows environments)
            SensorTest.TestRealService();
            
            Console.WriteLine("\nTest completed. Press any key to exit...");
            Console.ReadKey();
        }
    }
}