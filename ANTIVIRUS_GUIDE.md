# Antivirus Configuration Guide for CMDevicesManager

## If Your Antivirus Software Flags CMDevicesManager

CMDevicesManager is a legitimate system monitoring application that may be flagged by some antivirus software due to its hardware monitoring capabilities. This is a false positive.

### Why This Happens

1. **Hardware Monitoring**: The app reads CPU/GPU sensors, which some antivirus software considers suspicious
2. **System Tray**: The app can run in the background via system tray
3. **Network Statistics**: Basic network bandwidth monitoring for display purposes only

These are all normal features for a system monitoring application.

### How to Whitelist CMDevicesManager

#### Windows Defender

1. Open Windows Security (Windows Defender)
2. Go to "Virus & threat protection"
3. Click "Manage settings" under "Virus & threat protection settings"
4. Scroll down to "Exclusions" and click "Add or remove exclusions"
5. Click "Add an exclusion" → "Folder"
6. Browse to your CMDevicesManager installation folder
7. Click "Select Folder"

#### Other Popular Antivirus Software

**Avast:**
1. Open Avast
2. Go to Settings → General → Exceptions
3. Add the CMDevicesManager.exe file or folder

**AVG:**
1. Open AVG
2. Go to Settings → Components → Web Shield → Exceptions
3. Add the application path

**McAfee:**
1. Open McAfee
2. Go to Real-Time Protection → Excluded Files
3. Add the CMDevicesManager folder

**Norton:**
1. Open Norton
2. Go to Settings → Antivirus → Scans and Risks → Exclusions/Low Risks
3. Configure items to exclude from scans

### Verifying the Application is Safe

1. **Source Code**: This application is open source - you can review the entire source code
2. **Build from Source**: You can build the application yourself from the source code
3. **VirusTotal**: You can upload the executable to VirusTotal.com for analysis by multiple antivirus engines

### Contact Support

If you continue to have issues:

1. Report the false positive to your antivirus vendor
2. Contact us through the GitHub repository for assistance
3. Consider building from source if you prefer

### Security Best Practices

- Only download CMDevicesManager from official sources
- Verify file checksums if provided
- Keep your antivirus software updated
- Review the source code if you have security concerns