# Temperature and Power Display Fix

## Problem Statement (问题说明)
读取温度，读取功耗的代码好像没有生效，UI上没有动态显示。
(The temperature and power reading code doesn't seem to be working, with no dynamic display in the UI.)

## Root Cause Analysis (根本原因分析)

The issue was caused by several problems in the `RealSystemMetricsService`:

1. **Inconsistent Sensor Patterns**: The `CacheSensors()` method used different search patterns compared to individual getter methods (e.g., `GetCpuTemperature()`), causing sensors to not be found during initialization but potentially findable during runtime.

2. **UI Thread Marshalling**: The timer in `HomeViewModel` was calling service methods on a background thread and directly updating UI-bound properties without marshalling to the UI thread.

3. **Sensor Discovery Robustness**: The sensor discovery logic didn't handle cases where sensors might become available after initial startup.

## Solution (解决方案)

### 1. Fixed Sensor Pattern Consistency
Updated `CacheSensors()` to use the same search patterns as the individual getter methods:

```csharp
// Before - inconsistent patterns
_cpuTemp = FindCpuSensor(SensorType.Temperature, s => s.Name.Contains("Package", ...));
// GetCpuTemperature used: Package OR Core

// After - consistent patterns  
_cpuTemp = FindCpuSensor(SensorType.Temperature, s => 
    s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || 
    s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
```

### 2. Improved ReadOrZero Method
Enhanced the sensor reading logic to handle re-discovery:

```csharp
private double ReadOrZero(ref ISensor? cache, Func<ISensor?> resolver)
{
    // Try to resolve sensor again if cache is null or invalid
    if (cache == null || cache.Hardware == null)
    {
        cache = resolver();
    }
    
    // If still no value, try one more time
    if (cache == null)
    {
        cache = resolver();
        // ... rest of logic
    }
}
```

### 3. Added UI Thread Marshalling
Fixed the `HomeViewModel.Update()` method to properly marshal updates to the UI thread:

```csharp
private void Update()
{
    // Marshal to UI thread to ensure proper binding updates
    _dispatcher.BeginInvoke(() =>
    {
        CoolingCards[0].Value = _service.GetCpuTemperature();
        CoolingCards[1].Value = _service.GetGpuTemperature();
        // ... other updates
    });
}
```

### 4. Enhanced Hardware Refresh
Improved the hardware update process to be more thorough and error-resistant:

```csharp
private void RefreshAllHardware()
{
    foreach (var h in _computer.Hardware)
    {
        try
        {
            h.Update();
            // Recursively update sub-hardware
            foreach (var sub in h.SubHardware) 
            {
                sub.Update();
                foreach (var subSub in sub.SubHardware)
                {
                    subSub.Update();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"[HW] Failed to update hardware {h.Name}: {ex.Message}");
        }
    }
}
```

### 5. Added Debug Support
- Created `HomePageTest` that uses `FakeSystemMetricsService` for testing
- Added comprehensive logging to track sensor availability
- Added option to switch between fake/real services in debug builds

## Testing (测试)

### Option 1: Use Fake Service for Testing
In `HomePage.xaml.cs`, temporarily change:
```csharp
bool useFakeService = true; // Set to true for testing
```

This will use `FakeSystemMetricsService` which generates random but realistic sensor values that change every second, demonstrating that the dynamic update mechanism works correctly.

### Option 2: Use Test Page
Navigate to `HomePageTest.xaml` which always uses the fake service and displays a clear indication that it's in test mode.

## Files Changed (修改的文件)

1. **Services/RealSystemMetricsService.cs**
   - Fixed sensor caching consistency
   - Improved ReadOrZero method
   - Enhanced hardware refresh
   - Added debugging support

2. **ViewModels/HomeViewModel.cs**
   - Added UI thread marshalling
   - Fixed timer update mechanism

3. **Pages/HomePage.xaml.cs**
   - Added option to switch between services
   - Better documentation

4. **Pages/HomePageTest.xaml/.cs** (New)
   - Test page using fake service
   - Demonstrates working dynamic updates

5. **Tests/SensorTest.cs** (New)
   - Console test for service verification

## Expected Result (预期结果)

After these changes:
- Temperature readings should display and update dynamically every second
- Power readings should display and update dynamically every second  
- All sensor values should refresh in real-time in the UI
- If hardware sensors are not available, the system will log appropriate messages and fall back to 0 values
- The fake service can be used to verify that the UI update mechanism works correctly

## Debugging (调试)

If sensors still show 0:
1. Check the application logs for "[HW]" messages to see which sensors are/aren't found
2. Use the fake service temporarily to verify the UI update mechanism works
3. Check if the application has appropriate permissions to access hardware sensors
4. Verify that LibreHardwareMonitor supports your specific hardware