# CMDevicesManager

A legitimate system monitoring application for Windows that displays hardware performance metrics.

## Purpose

CMDevicesManager is a hardware monitoring tool designed to help users monitor their computer's performance by displaying real-time information about:

- CPU temperature and usage
- GPU temperature and usage  
- Memory usage
- Network bandwidth usage

## Features

- Real-time hardware monitoring dashboard
- System tray integration for background monitoring
- Multi-language support (English/Chinese)
- Clean and modern user interface
- Resource-efficient monitoring

## Privacy and Security

### What this application does:
- Reads hardware sensor data (CPU, GPU, memory temperatures and usage)
- Monitors network bandwidth for display purposes only
- Logs application events for debugging (stored locally only)
- Runs in system tray for convenient access

### What this application does NOT do:
- Does not transmit any data over the internet
- Does not store personal information
- Does not monitor user activity or keystrokes
- Does not access files outside its own directory
- Does not perform any malicious activities

### Antivirus Detection

Some antivirus software may flag this application due to:

1. **Hardware Monitoring**: The application uses LibreHardwareMonitor to read CPU/GPU sensors, which some antivirus software may consider suspicious
2. **System Tray Persistence**: The application can run in the background via system tray
3. **Network Statistics**: Basic network bandwidth monitoring for display purposes

These are all legitimate features for a system monitoring application.

## Technical Details

- Built with .NET 8 and WPF
- Uses LibreHardwareMonitor for hardware sensor access
- Requires Windows with .NET 8 runtime
- No elevated privileges required for basic functionality

## Building from Source

1. Install .NET 8 SDK
2. Clone this repository
3. Open CMDevicesManager.sln in Visual Studio or Rider
4. Build the solution

## License

This project is open source. Please see the license file for details.

## Support

If you encounter false positive detections from antivirus software, you can:

1. Add the application to your antivirus whitelist
2. Report the false positive to your antivirus vendor
3. Build the application from source code to verify its legitimacy

The source code is available for inspection to confirm the application's legitimate purpose.