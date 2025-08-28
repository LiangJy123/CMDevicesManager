using HidApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HID.DisplayController
{

    public struct DeviceVersion
    {
        public byte Major { get; init; }
        public byte Minor { get; init; }

        public DeviceVersion(byte major, byte minor)
        {
            Major = major;
            Minor = minor;
        }

        public bool IsAvailable => Major != 0xFF && Minor != 0xFF;

        public override string ToString()
        {
            return IsAvailable ? $"{Major}.{Minor}" : "N/A";
        }
    }

    public struct FirmwareVersion
    {
        public byte Major { get; init; }
        public byte Minor { get; init; }
        public byte Revision { get; init; }
        public byte Build { get; init; }

        public FirmwareVersion(byte major, byte minor, byte revision, byte build)
        {
            Major = major;
            Minor = minor;
            Revision = revision;
            Build = build;
        }

        public bool IsAvailable => Major != 0xFF;

        public override string ToString()
        {
            return IsAvailable ? $"{Major}.{Minor}.{Revision}.{Build}" : "N/A";
        }
    }

    public class DeviceFWInfo
    {
        public DeviceVersion HardwareVersion { get; init; }
        public FirmwareVersion FirmwareVersion { get; init; }

        public DeviceFWInfo(DeviceVersion hardwareVersion, FirmwareVersion firmwareVersion)
        {
            HardwareVersion = hardwareVersion;
            FirmwareVersion = firmwareVersion;
        }

        public override string ToString()
        {
            return $"Hardware Version {HardwareVersion}, Firmware Version {FirmwareVersion}";
        }
    }

    public record DisplayCtrlCapabilities
    {
        public bool OffModeSupported { get; init; }
        public bool SsrModeSupported { get; init; }
        public byte SsrVsInterface { get; init; }
        public ushort SsrVsWidth { get; init; }
        public ushort SsrVsHeight { get; init; }
        public byte SsrVsFormat { get; init; }
        public byte SsrVsMaxFps { get; init; }
        public byte SsrVsTransferInterface { get; init; }
        public byte SsrVsFwSupportRotation { get; init; }
        public uint SsrVsMaxFileSize { get; init; }
        public ushort SsrVsMaxFrameCnt { get; init; }
        public ushort SsrVsHwDecodeSupport { get; init; }
        public byte SsrVsHwSupportOverlay { get; init; }
        public byte SsrVsCmdFormat { get; init; }

        // Helper properties for decode support
        public bool H264DecodeSupported => (SsrVsHwDecodeSupport & 0x01) != 0;
        public bool JpegDecodeSupported => (SsrVsHwDecodeSupport & 0x02) != 0;

        public override string ToString()
        {
            var decodeFormats = new List<string>();
            if (H264DecodeSupported) decodeFormats.Add("H.264");
            if (JpegDecodeSupported) decodeFormats.Add("JPEG");

            return $"Display Mode: Off={OffModeSupported}, SSR={SsrModeSupported}, " +
                   $"Resolution: {SsrVsWidth}x{SsrVsHeight}, Format: {SsrVsFormat:X2}, " +
                   $"MaxFPS: {SsrVsMaxFps}, Interface: {SsrVsInterface}, " +
                   $"Rotation: {SsrVsFwSupportRotation:X2}, MaxFileSize: {SsrVsMaxFileSize}MB, " +
                   $"HW Decode: [{string.Join(", ", decodeFormats)}]";
        }
    }

    /// <summary>
    /// Device status information structure for handling status JSON responses
    /// </summary>
    public struct DeviceStatus
    {
        public int Brightness { get; init; }
        public int Degree { get; init; }
        public int OsdState { get; init; }
        public int KeepAliveTimeout { get; init; }
        public int MaxSuspendMediaCount { get; init; }
        public int DisplayInSleep { get; init; }
        public int[] SuspendMediaActive { get; init; }

        public DeviceStatus(int brightness, int degree, int osdState, int keepAliveTimeout, int maxSuspendMediaCount, int displayInSleep, int[] suspendMediaActive)
        {
            Brightness = brightness;
            Degree = degree;
            OsdState = osdState;
            KeepAliveTimeout = keepAliveTimeout;
            MaxSuspendMediaCount = maxSuspendMediaCount;
            DisplayInSleep = displayInSleep;
            SuspendMediaActive = suspendMediaActive ?? new int[5];
        }

        /// <summary>
        /// Gets whether the display is currently in sleep mode
        /// </summary>
        public bool IsDisplayInSleep => DisplayInSleep != 0;

        /// <summary>
        /// Gets whether OSD is active
        /// </summary>
        public bool IsOsdActive => OsdState != 0;

        /// <summary>
        /// Gets the count of active suspend media files
        /// </summary>
        public int ActiveSuspendMediaCount => SuspendMediaActive?.Count(x => x != 0) ?? 0;

        /// <summary>
        /// Gets whether any suspend media is active
        /// </summary>
        public bool HasActiveSuspendMedia => ActiveSuspendMediaCount > 0;

        /// <summary>
        /// Gets the indices of active suspend media (0-based)
        /// </summary>
        public IEnumerable<int> ActiveSuspendMediaIndices
        {
            get
            {
                if (SuspendMediaActive == null) yield break;

                for (int i = 0; i < SuspendMediaActive.Length; i++)
                {
                    if (SuspendMediaActive[i] != 0)
                        yield return i;
                }
            }
        }

        /// <summary>
        /// Creates a DeviceStatus from JSON response data
        /// </summary>
        /// <param name="jsonData">JSON string containing device status</param>
        /// <returns>DeviceStatus instance or null if parsing fails</returns>
        public static DeviceStatus? FromJson(string jsonData)
        {
            try
            {
                var jsonResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonData);
                if (jsonResponse == null) return null;

                int brightness = 0, degree = 0, osdState = 0, keepAliveTimeout = 0, maxSuspendMediaCount = 0, displayInSleep = 0;
                int[] suspendMediaActive = new int[5];

                if (jsonResponse.ContainsKey("brightness"))
                    int.TryParse(jsonResponse["brightness"].ToString(), out brightness);

                if (jsonResponse.ContainsKey("degree"))
                    int.TryParse(jsonResponse["degree"].ToString(), out degree);

                if (jsonResponse.ContainsKey("osdState"))
                    int.TryParse(jsonResponse["osdState"].ToString(), out osdState);

                if (jsonResponse.ContainsKey("keepAliveTimeout"))
                    int.TryParse(jsonResponse["keepAliveTimeout"].ToString(), out keepAliveTimeout);

                if (jsonResponse.ContainsKey("maxSuspendMediaCount"))
                    int.TryParse(jsonResponse["maxSuspendMediaCount"].ToString(), out maxSuspendMediaCount);

                if (jsonResponse.ContainsKey("displayInSleep"))
                    int.TryParse(jsonResponse["displayInSleep"].ToString(), out displayInSleep);

                if (jsonResponse.ContainsKey("suspendMediaActive"))
                {
                    try
                    {
                        var activeArrayJson = jsonResponse["suspendMediaActive"].ToString();
                        if (!string.IsNullOrEmpty(activeArrayJson))
                        {
                            var activeArray = System.Text.Json.JsonSerializer.Deserialize<int[]>(activeArrayJson);
                            if (activeArray != null && activeArray.Length <= 5)
                            {
                                for (int i = 0; i < Math.Min(activeArray.Length, suspendMediaActive.Length); i++)
                                {
                                    suspendMediaActive[i] = activeArray[i];
                                }
                            }
                        }
                    }
                    catch
                    {
                        // If parsing the array fails, keep default values
                    }
                }

                return new DeviceStatus(brightness, degree, osdState, keepAliveTimeout, maxSuspendMediaCount, displayInSleep, suspendMediaActive);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to parse DeviceStatus from JSON: {ex.Message}");
                return null;
            }
        }

        public override string ToString()
        {
            var activeIndices = string.Join(",", ActiveSuspendMediaIndices);
            return $"Brightness: {Brightness}%, Rotation: {Degree}°, OSD: {(IsOsdActive ? "Active" : "Inactive")}, " +
                   $"KeepAlive: {KeepAliveTimeout}s, DisplaySleep: {(IsDisplayInSleep ? "On" : "Off")}, " +
                   $"SuspendMedia: {ActiveSuspendMediaCount}/{MaxSuspendMediaCount} active" +
                   (HasActiveSuspendMedia ? $" [indices: {activeIndices}]" : "");
        }
    }

    public class DisplayController
    {
        private readonly Device _device;
        private const byte StartEndMarker5A = 0x5A;
        // display-ctrl-ssr-command report ID 0x1E.
        private const byte DisplayCtrlSsrCommandReportID = 0x1E;

        // Report ID for device-info 0x1.
        private const byte DeviceInfoReportID = 0x01;

        // Report ID for display-ctrl-capabilities 0x20
        private const byte DisplayCtrlCapabilitiesReportID = 0x14;

        // Define global SeqNumber for commands
        private uint _currentSeqNumber = 1;    // 0 ~ (0xFFFFFFFF-1)

        /// <summary>
        /// Device firmware and hardware information
        /// </summary>
        public DeviceFWInfo? DeviceFWInfo { get; private set; }

        /// <summary>
        /// Device display control capabilities
        /// </summary>
        public DisplayCtrlCapabilities? Capabilities { get; private set; }

        // DeviceInfo property for reference
        public DeviceInfo DeviceInfo => _device.GetDeviceInfo();

        /// <summary>
        /// Enhanced dispose method
        /// </summary>
        public void Dispose()
        {
            StopResponseListener();
            _cancellationTokenSource?.Dispose();
            _device?.Dispose();
        }

        public DisplayController(ushort vendorId, ushort productId, string serialNumber)
        {
            try
            {
                _device = new Device(vendorId, productId, serialNumber);
                // Initialize device info and capabilities during construction
                InitializeDeviceProperties();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DisplayController] Error opening HID device (VID=0x{vendorId:X4}, PID=0x{productId:X4}, SN={serialNumber}): {ex.Message}");
                throw; // Rethrow so callers can handle initialization failure
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DisplayController"/> class with the specified device path.
        /// </summary>
        /// <param name="devicePath">The path to the device to be controlled. This must be a valid path to a HID device.</param>
        public DisplayController(string devicePath)
        {
            try
            {
                _device = new Device(devicePath);
                // Initialize device info and capabilities during construction
                InitializeDeviceProperties();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DisplayController] Error opening HID device at path '{devicePath}': {ex.Message}");
                throw; // Rethrow so callers can handle initialization failure
            }
        }

        /// <summary>
        /// Initialize device properties (DeviceInfo and Capabilities)
        /// </summary>
        private void InitializeDeviceProperties()
        {
            try
            {
                // Load device firmware info
                DeviceFWInfo = GetDeviceInfo();

                // Load device capabilities
                Capabilities = GetDisplayCtrlCapabilities(debug: false);

                Console.WriteLine($"[DisplayController] Device initialized successfully");
                if (DeviceFWInfo != null)
                {
                    Console.WriteLine($"[DisplayController] Device Info: {DeviceFWInfo}");
                }
                if (Capabilities != null)
                {
                    Console.WriteLine($"[DisplayController] Capabilities: {Capabilities}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DisplayController] Warning: Failed to initialize device properties: {ex.Message}");
                // Set to null if initialization fails
                DeviceFWInfo = null;
                Capabilities = null;
            }
        }

        /// <summary>
        /// Refresh device properties (useful if device capabilities change)
        /// </summary>
        public void RefreshDeviceProperties()
        {
            InitializeDeviceProperties();
        }

        /// <summary>
        /// Get current device status with parsed DeviceStatus structure
        /// {"brightness":100,"degree":0,"osdState":0,"keepAliveTimeout":5,"maxSuspendMediaCount":5,"displayInSleep":0,"suspendMediaActive":[1,0,0,1,1]}
        /// </summary>
        /// <returns>DeviceStatus instance with current device state, or null if failed</returns>
        public async Task<DeviceStatus?> GetDeviceStatus()
        {
            try
            {
                var response = await SendCmdGetStatusWithResponse();

                if (response?.IsSuccess == true && !string.IsNullOrEmpty(response.Value.ResponseData))
                {
                    Console.WriteLine($"Device status raw response: {response.Value.ResponseData}");

                    // Parse the JSON response into DeviceStatus
                    var deviceStatus = DeviceStatus.FromJson(response.Value.ResponseData);

                    if (deviceStatus.HasValue)
                    {
                        Console.WriteLine($"Device status parsed: {deviceStatus.Value}");
                        return deviceStatus.Value;
                    }
                    else
                    {
                        Console.WriteLine("Failed to parse device status from response");
                    }
                }
                else
                {
                    Console.WriteLine($"Failed to get device status. Status code: {response?.StatusCode}");
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting device status: {ex.Message}");
                return null;
            }
        }

        private DisplayCtrlCapabilities? GetDisplayCtrlCapabilities(bool debug = false)
        {
            var data = _device.GetFeatureReport(DisplayCtrlCapabilitiesReportID, 1024);

            if (data == null || data.Length < 3)
            {
                Console.WriteLine("Failed to get display capabilities or data is too short.");
                return null;
            }

            if (debug)
            {
                // Debug: Print raw data
                Console.WriteLine($"Raw capabilities data length: {data.Length}");
                Console.WriteLine($"Raw data: {Convert.ToHexString(data)}");
            }

            // Parse the TLV (Tag-Length-Value) structure starting from byte 1
            int offset = 1; // Skip report ID

            // Initialize all values
            bool offModeSupported = false;
            bool ssrModeSupported = false;
            byte ssrVsInterface = 0;
            ushort ssrVsWidth = 0;
            ushort ssrVsHeight = 0;
            byte ssrVsFormat = 0;
            byte ssrVsMaxFps = 0;
            byte ssrVsTransferInterface = 0;
            byte ssrVsFwSupportRotation = 0;
            uint ssrVsMaxFileSize = 0;
            ushort ssrVsMaxFrameCnt = 0;
            ushort ssrVsHwDecodeSupport = 0;
            byte ssrVsHwSupportOverlay = 0;
            byte ssrVsCmdFormat = 0;

            // Parse TLV structures in a loop
            while (offset < data.Length - 1) // Need at least 2 bytes for Tag and Length
            {
                byte tag = data[offset];
                byte length = data[offset + 1];

                // Check if we have enough data for this TLV entry
                if (offset + 2 + length > data.Length)
                {
                    if (debug) Console.WriteLine($"Insufficient data for TLV entry: tag=0x{tag:X2}, length={length}, remaining={data.Length - offset - 2}");
                    break;
                }

                if (debug) Console.WriteLine($"Parsing TLV: tag=0x{tag:X2}, length={length}, offset={offset}");

                // Parse based on tag
                switch (tag)
                {
                    case 0x01: // display-mode
                        if (length == 2)
                        {
                            byte displayModeValue = data[offset + 3]; // Skip tag, length, and first byte
                            offModeSupported = (displayModeValue & 0x01) != 0;
                            ssrModeSupported = (displayModeValue & 0x02) != 0;
                            if (debug) Console.WriteLine($"Display mode: off={offModeSupported}, ssr={ssrModeSupported}");
                        }
                        break;

                    case 0x02: // ssr-vs-interface
                        if (length == 1)
                        {
                            ssrVsInterface = data[offset + 2];
                            if (debug) Console.WriteLine($"SSR VS Interface: {ssrVsInterface}");
                        }
                        break;

                    case 0x03: // ssr-vs-width
                        if (length == 2)
                        {
                            ssrVsWidth = (ushort)((data[offset + 3] << 8) | data[offset + 2]);
                            if (debug) Console.WriteLine($"SSR VS Width: {ssrVsWidth}");
                        }
                        break;

                    case 0x04: // ssr-vs-height
                        if (length == 2)
                        {
                            ssrVsHeight = (ushort)((data[offset + 3] << 8) | data[offset + 2]);
                            if (debug) Console.WriteLine($"SSR VS Height: {ssrVsHeight}");
                        }
                        break;

                    case 0x05: // ssr-vs-format
                        if (length == 1)
                        {
                            ssrVsFormat = data[offset + 2];
                            if (debug) Console.WriteLine($"SSR VS Format: 0x{ssrVsFormat:X2}");
                        }
                        break;

                    case 0x06: // ssr-vs-max-fps
                        if (length == 1)
                        {
                            ssrVsMaxFps = data[offset + 2];
                            if (debug) Console.WriteLine($"SSR VS Max FPS: {ssrVsMaxFps}");
                        }
                        break;

                    case 0x07: // ssr-vs-transfer-interface
                        if (length == 1)
                        {
                            ssrVsTransferInterface = data[offset + 2];
                            if (debug) Console.WriteLine($"SSR VS Transfer Interface: {ssrVsTransferInterface}");
                        }
                        break;

                    case 0x08: // ssr-vs-fw-support-rotation
                        if (length == 1)
                        {
                            ssrVsFwSupportRotation = data[offset + 2];
                            if (debug) Console.WriteLine($"SSR VS FW Support Rotation: 0x{ssrVsFwSupportRotation:X2}");
                        }
                        break;

                    case 0x09: // ssr-vs-max-file-size
                        if (length == 4)
                        {
                            ssrVsMaxFileSize = (uint)((data[offset + 5] << 24) | (data[offset + 4] << 16) | (data[offset + 3] << 8) | data[offset + 2]);
                            if (debug) Console.WriteLine($"SSR VS Max File Size: {ssrVsMaxFileSize} MB");
                        }
                        break;

                    case 0x0A: // ssr-vs-max-frame-cnt
                        if (length == 2)
                        {
                            ssrVsMaxFrameCnt = (ushort)((data[offset + 3] << 8) | data[offset + 2]);
                            if (debug) Console.WriteLine($"SSR VS Max Frame Count: {ssrVsMaxFrameCnt}");
                        }
                        break;

                    case 0x0B: // ssr-vs-hw-decode-support
                        if (length == 2)
                        {
                            ssrVsHwDecodeSupport = (ushort)((data[offset + 2] << 8) | data[offset + 3]);
                            if (debug) Console.WriteLine($"SSR VS HW Decode Support: 0x{ssrVsHwDecodeSupport:X4}");
                        }
                        break;

                    case 0x0C: // ssr-vs-hw-support-overlay
                        if (length == 1)
                        {
                            ssrVsHwSupportOverlay = data[offset + 2];
                            if (debug) Console.WriteLine($"SSR VS HW Support Overlay: {ssrVsHwSupportOverlay}");
                        }
                        break;

                    case 0x0D: // ssr-vs-cmd-format
                        if (length == 1)
                        {
                            ssrVsCmdFormat = data[offset + 2];
                            if (debug) Console.WriteLine($"SSR VS Cmd Format: {ssrVsCmdFormat}");
                        }
                        break;

                    default:
                        if (debug) Console.WriteLine($"Unknown TLV tag: 0x{tag:X2}");
                        break;
                }

                // Move to next TLV entry
                offset += 2 + length; // Skip tag, length, and value
            }

            return new DisplayCtrlCapabilities
            {
                OffModeSupported = offModeSupported,
                SsrModeSupported = ssrModeSupported,
                SsrVsInterface = ssrVsInterface,
                SsrVsWidth = ssrVsWidth,
                SsrVsHeight = ssrVsHeight,
                SsrVsFormat = ssrVsFormat,
                SsrVsMaxFps = ssrVsMaxFps,
                SsrVsTransferInterface = ssrVsTransferInterface,
                SsrVsFwSupportRotation = ssrVsFwSupportRotation,
                SsrVsMaxFileSize = ssrVsMaxFileSize,
                SsrVsMaxFrameCnt = ssrVsMaxFrameCnt,
                SsrVsHwDecodeSupport = ssrVsHwDecodeSupport,
                SsrVsHwSupportOverlay = ssrVsHwSupportOverlay,
                SsrVsCmdFormat = ssrVsCmdFormat
            };
        }

        private DeviceFWInfo? GetDeviceInfo()
        {
            var info = _device.GetFeatureReport(DeviceInfoReportID, 1024);
            // parse the device info
            // 0 report-id, 1
            // Hardware Version (hw-ver) 1 - 2 hw-ver, R/O,
            //  0 major, [0-99], 0xFF if the hardware version data is not available on the flash storage
            //  1 minor, [0-99], 0xFF if the hardware version data is not available on the flash storage
            // Firmware Version (fw-ver)  3 - 6 fw-ver, R/O, 
            // 0 major, [0-99]. 1 minor, [0-9]. 2 revision, [0-9]. 3 build, [0-255].
            if (info == null || info.Length < 7)
            {
                Console.WriteLine("Failed to get device info or data is too short.");
                return null;
            }

            var hardwareVersion = new DeviceVersion(info[1], info[2]);
            var firmwareVersion = new FirmwareVersion(info[3], info[4], info[5], info[6]);

            return new DeviceFWInfo(hardwareVersion, firmwareVersion);
        }

        public void Reboot()
        {
            SendSubcommand(0x02);
        }

        public void FactoryReset()
        {
            SendSubcommand(0x01);
        }

        //  Subcommand (device-subcommnad)
        // byte 0: report-id:2
        // byte 1: subcommand-id.
        //  0x01 - reset, perform a device factory default.
        //  0x02 - reboot
        private void SendSubcommand(byte subcommandId)
        {
            try
            {
                byte[] buffer = new byte[3];
                buffer[0] = 0x02; // Report ID for device-subcommand
                buffer[1] = subcommandId;
                _device.Write(buffer.AsSpan(0, buffer.Length));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DisplayController] Error sending subcommand 0x{subcommandId:X2}: {ex.Message}");
            }
        }

        public string GetManufacturer()
        {
            try
            {
                return _device.GetManufacturer();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DisplayController] Error getting manufacturer: {ex.Message}");
                return "Unknown";
            }
        }

        // Software Screen Rendering-Command (display-ctrl-ssr-command)
        private void SendDisplayCtrlSsrCommandCommand(string jsonPayload)
        {
            try
            {
                // [0] = Report ID (0x1E)
                // [1] = 0x5A (start marker)
                // [2-3] = Length (big-endian) - from 1st "5a" to 2nd "5a", include the length bytes, payload, and checksum and the two "5a" bytes
                // [4~n] = JSON payload (response data)
                // [n+1] = Checksum (sum of length + payload, lowest 8 bits)
                // [last] = 0x5A (end marker)

                // Length of payload (ASCII bytes)
                int jsonPayloadLen = Encoding.ASCII.GetByteCount(jsonPayload);

                // Create initial buffer without translation
                byte[] tempBuffer = new byte[2 + jsonPayloadLen + 1]; // length(2) + payload + checksum(1)

                // Length (high byte first = 0, then low byte)
                tempBuffer[0] = 0x00;            // high byte
                tempBuffer[1] = (byte)(jsonPayloadLen + 3 + 2); // low byte

                // Copy payload
                Encoding.ASCII.GetBytes(jsonPayload, 0, jsonPayloadLen, tempBuffer, 2);

                // Compute checksum on untranslated data
                byte checksum = 0;
                for (int i = 0; i < tempBuffer.Length - 1; i++)
                {
                    unchecked
                    {
                        checksum += tempBuffer[i];
                    }
                }
                tempBuffer[tempBuffer.Length - 1] = checksum;

                // Apply payload translation to length, JSON payload, and checksum
                var translatedData = TranslatePayload(tempBuffer);

                // Calculate final command length: reportId(1) + start marker(1) + translatedData + end marker(1)
                int commandLen = 1 + 1 + translatedData.Length + 1;
                byte[] buffer = new byte[commandLen];

                // Report ID
                buffer[0] = DisplayCtrlSsrCommandReportID;
                // Start marker (not translated)
                buffer[1] = StartEndMarker5A;

                // Copy translated data
                Array.Copy(translatedData, 0, buffer, 2, translatedData.Length);

                // End marker (not translated)
                buffer[buffer.Length - 1] = StartEndMarker5A;

                // Send the command
                _device.Write(buffer.AsSpan(0, commandLen));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DisplayController] Error sending display control SSR command: {ex.Message}");
            }
        }

        // Software Screen Rendering-Command (display-ctrl-ssr-command)
    

        //
        // Helper methods
        //
        private byte[] TranslatePayload(byte[] data)
        {
            var translatedList = new List<byte>();

            foreach (byte b in data)
            {
                if (b == 0x5A)
                {
                    // 0x5A --> 0x5B 0x01
                    translatedList.Add(0x5B);
                    translatedList.Add(0x01);
                }
                else if (b == 0x5B)
                {
                    // 0x5B --> 0x5B 0x02
                    translatedList.Add(0x5B);
                    translatedList.Add(0x02);
                }
                else
                {
                    translatedList.Add(b);
                }
            }

            return translatedList.ToArray();
        }

        private byte[] ReverseTranslatePayload(byte[] translatedData)
        {
            var originalList = new List<byte>();

            for (int i = 0; i < translatedData.Length; i++)
            {
                if (translatedData[i] == 0x5B && i + 1 < translatedData.Length)
                {
                    if (translatedData[i + 1] == 0x01)
                    {
                        // 0x5B 0x01 --> 0x5A
                        originalList.Add(0x5A);
                        i++; // Skip next byte
                    }
                    else if (translatedData[i + 1] == 0x02)
                    {
                        // 0x5B 0x02 --> 0x5B
                        originalList.Add(0x5B);
                        i++; // Skip next byte
                    }
                    else
                    {
                        originalList.Add(translatedData[i]);
                    }
                }
                else
                {
                    originalList.Add(translatedData[i]);
                }
            }

            return originalList.ToArray();
        }

        private bool ValidateReceivedData(byte[] receivedBuffer)
        {
            // Extract translated data (skip reportId and start marker, remove end marker)
            if (receivedBuffer.Length < 4 ||
                receivedBuffer[0] != DisplayCtrlSsrCommandReportID ||
                receivedBuffer[1] != StartEndMarker5A ||
                receivedBuffer[receivedBuffer.Length - 1] != StartEndMarker5A)
            {
                return false;
            }

            byte[] translatedData = new byte[receivedBuffer.Length - 3]; // Remove reportId, start, end
            Array.Copy(receivedBuffer, 2, translatedData, 0, translatedData.Length);

            // Reverse translate to get original data
            byte[] originalData = ReverseTranslatePayload(translatedData);

            if (originalData.Length < 3) return false;

            // Extract length, payload, and checksum from original data
            ushort length = (ushort)((originalData[0] << 8) | originalData[1]);
            byte receivedChecksum = originalData[originalData.Length - 1];

            // Verify length matches actual payload + overhead
            int expectedPayloadLen = originalData.Length - 3; // Total - length(2) - checksum(1)
            if (length != expectedPayloadLen + 3 + 2) return false;

            // Verify checksum
            byte calculatedChecksum = 0;
            for (int i = 0; i < originalData.Length - 1; i++)
            {
                unchecked
                {
                    calculatedChecksum += originalData[i];
                }
            }

            return calculatedChecksum == receivedChecksum;
        }

        //
        // File Transfer Constants
        //
        private const byte FileTransferReportID = 0x1F; // Report ID 31 for file transfer

        /// <summary>
        /// File transfer metadata structure
        /// </summary>
        public struct FileTransferMetadata
        {
            public byte Id { get; init; }        // Unique number 0-59
            public ushort Count { get; init; }   // Total block count (1-65535)
            public ushort Index { get; init; }   // Current block index, 0-(count-1)
            public byte Type { get; init; }      // 0=don't care, 1=jpg, 2=png
            public byte[] Reserved { get; init; } // 14 bytes reserved (need checksum)

            public FileTransferMetadata(byte id, ushort count, ushort index, byte type)
            {
                Id = id;
                Count = count;
                Index = index;
                Type = type;
                Reserved = new byte[14]; // Will be filled with checksum data
            }
        }

        /// <summary>
        /// Send file data using the Software Screen Rendering File Transfer protocol
        /// </summary>
        /// <param name="fileData">Complete file data to transfer</param>
        /// <param name="fileType">File type: 0=don't care, 1=jpg, 2=png</param>
        /// <param name="transferId">Unique transfer ID (0-59)</param>
        /// <param name="blockSize">Size of each data block (default 1000 bytes)</param>
        public void SendFileTransfer(byte[] fileData, byte fileType = 0, byte transferId = 0, int blockSize = 1000)
        {
            if (fileData == null || fileData.Length == 0)
            {
                throw new ArgumentException("File data cannot be null or empty", nameof(fileData));
            }

            if (transferId > 59)
            {
                throw new ArgumentOutOfRangeException(nameof(transferId), "Transfer ID must be between 0 and 59");
            }

            if (blockSize < 1 || blockSize > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(blockSize), "Block size must be between 1 and 1000 bytes");
            }

            // Calculate total number of blocks needed
            int totalBlocks = (fileData.Length + blockSize - 1) / blockSize; // Ceiling division

            if (totalBlocks > 65535)
            {
                throw new ArgumentException("File too large - would require more than 65535 blocks", nameof(fileData));
            }

            Console.WriteLine($"Starting file transfer: {fileData.Length} bytes in {totalBlocks} blocks");

            // Send each block
            for (int blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
            {
                int offset = blockIndex * blockSize;
                int currentBlockSize = Math.Min(blockSize, fileData.Length - offset);

                byte[] blockData = new byte[currentBlockSize];
                Array.Copy(fileData, offset, blockData, 0, currentBlockSize);

                SendFileTransferBlock(transferId, (ushort)totalBlocks, (ushort)blockIndex, fileType, blockData);

                //Console.WriteLine($"Sent block {blockIndex + 1}/{totalBlocks} ({currentBlockSize} bytes)");

                // Small delay between blocks to avoid overwhelming the device
                //Thread.Sleep(10);
            }

            Console.WriteLine("File transfer completed");
        }

        /// <summary>
        /// Send a single file transfer block
        /// </summary>
        /// <param name="transferId">Unique transfer ID (0-59)</param>
        /// <param name="totalBlocks">Total number of blocks</param>
        /// <param name="blockIndex">Current block index</param>
        /// <param name="fileType">File type</param>
        /// <param name="blockData">Data for this block</param>
        private void SendFileTransferBlock(byte transferId, ushort totalBlocks, ushort blockIndex, byte fileType, byte[] blockData)
        {
            // Create metadata
            var metadata = new FileTransferMetadata(transferId, totalBlocks, blockIndex, fileType);

            // Calculate total payload size: 1 byte (0x5c) + 2 bytes (length) + 20 bytes (metadata) + block data
            int payloadSize = 1 + 2 + 20 + blockData.Length;
            byte[] payload = new byte[payloadSize];

            int offset = 0;

            // Byte 1: 0x5c
            payload[offset++] = 0x5C;

            // Bytes 2-3: Length (little-endian: low byte first, then high byte)
            ushort length = (ushort)(20 + blockData.Length); // metadata + payload
            payload[offset++] = (byte)((length >> 8) & 0xFF); // High byte
            payload[offset++] = (byte)(length & 0xFF);        // Low byte

            // Bytes 4-23: Metadata (20 bytes)
            payload[offset++] = metadata.Id;

            // Count (little-endian)
            payload[offset++] = (byte)((metadata.Count >> 8) & 0xFF);
            payload[offset++] = (byte)(metadata.Count & 0xFF);

            // Index (little-endian)
            payload[offset++] = (byte)((metadata.Index >> 8) & 0xFF);
            payload[offset++] = (byte)(metadata.Index & 0xFF);

            // Type
            payload[offset++] = metadata.Type;

            // Reserved (14 bytes) - fill with zeros for now
            for (int i = 0; i < 14; i++)
            {
                payload[offset++] = 0x00;
            }

            // Bytes 24+: Block data
            Array.Copy(blockData, 0, payload, offset, blockData.Length);

            // Send using HID report
            SendFileTransferReport(payload);
        }

        /// <summary>
        /// Send file transfer data via HID report
        /// </summary>
        /// <param name="payload">Complete payload to send</param>
        private void SendFileTransferReport(byte[] payload)
        {
            try
            {
                // Create HID report: Report ID + payload
                byte[] report = new byte[payload.Length + 1];
                report[0] = FileTransferReportID; // Report ID 31 (0x1F)
                Array.Copy(payload, 0, report, 1, payload.Length);
                // Send the report
                _device.Write(report.AsSpan());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DisplayController] Error sending file transfer report: {ex.Message}");
            }
        }

        /// <summary>
        /// Transfer a file from disk using the file transfer protocol
        /// </summary>
        /// <param name="filePath">Path to the file to transfer</param>
        /// <param name="transferId">Unique transfer ID (0-59)</param>
        /// <param name="blockSize">Size of each data block (default 1000 bytes)</param>
        public void SendFileFromDisk(string filePath, byte transferId = 0, int blockSize = 1000)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            // Determine file type from extension
            byte fileType = 0; // Default: don't care
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    fileType = 1;
                    break;
                case ".png":
                    fileType = 2;
                    break;
            }

            // Read file data
            byte[] fileData = File.ReadAllBytes(filePath);

            Console.WriteLine($"Transferring file: {Path.GetFileName(filePath)} ({fileData.Length} bytes, type: {fileType})");

            // Send the file
            SendFileTransfer(fileData, fileType, transferId, blockSize);
        }


        /// <summary>
        /// Calculate MD5 hash of a file
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <returns>MD5 hash as hexadecimal string</returns>
        private string CalculateMD5Hash(string filePath)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hashBytes = md5.ComputeHash(stream);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }


        //
        // Response handling constants and structures
        //
        private const byte ResponseReportID = 0x20; // Same as file transfer for responses

        /// <summary>
        /// Response structure for display control commands
        /// </summary>
        public struct DisplayResponse
        {
            public byte ReportId { get; init; }
            public ushort Length { get; init; }
            public string Command { get; init; }
            public int AckNumber { get; init; }
            public int StatusCode { get; init; }
            public string? ResponseData { get; init; }
            public DateTime Timestamp { get; init; }

            public DisplayResponse(byte reportId, ushort length, string command, int ackNumber, int statusCode, string? data = null)
            {
                ReportId = reportId;
                Length = length;
                Command = command;
                AckNumber = ackNumber;
                StatusCode = statusCode;
                ResponseData = data;
                Timestamp = DateTime.Now;
            }

            public bool IsSuccess => StatusCode == 200;
            public bool IsError => StatusCode >= 400;
        }

        /// <summary>
        /// Event handler for received responses
        /// </summary>
        public event EventHandler<DisplayResponse>? ResponseReceived;

        /// <summary>
        /// Start listening for responses from the device
        /// </summary>
        public void StartResponseListener()
        {
            Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await ListenForResponse();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Response listener error: {ex.Message}");
                        await Task.Delay(1000); // Wait before retrying
                    }
                }
            });
        }

        private readonly CancellationTokenSource _cancellationTokenSource = new();

        /// <summary>
        /// Stop the response listener
        /// </summary>
        public void StopResponseListener()
        {
            _cancellationTokenSource.Cancel();
        }


        /// <summary>
        /// Listen for a single response
        /// </summary>
        private async Task ListenForResponse()
        {
            byte[] buffer = new byte[1024];

            // Read response using the correct method signature
            await Task.Run(() =>
            {
                try
                {
                    // Use ReadTimeout with timeout to avoid blocking indefinitely
                    int result = _device.ReadTimeout(buffer.AsSpan(), 1000); // 1 second timeout
                    if (result > 0)
                    {
                        ProcessResponse(buffer, result);
                    }
                }
                catch (HidException ex)
                {
                    // HID specific exceptions
                    Console.WriteLine($"HID read error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    // Other exceptions
                    Console.WriteLine($"Read error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Process received response data
        /// </summary>
        /// <param name="buffer">Response buffer</param>
        /// <param name="length">Actual length of response</param>
        private void ProcessResponse(byte[] buffer, int length)
        {
            if (length < 3) return; // Minimum response size

            try
            {
                byte reportId = buffer[0];

                // Check if this is a display control response
                if (reportId == ResponseReportID)
                {
                    // Get the last byte value is 0x5A as actual length.
                    int actualLength = length;
                    if (!ValidateAndParseResponse(buffer, length, ref actualLength))
                    {
                        Console.WriteLine("[DEBUG] Invalid response data");
                        return;
                    }


                    ParseDisplayControlResponse(buffer, actualLength);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing response: {ex.Message}");
            }
        }

        /// <summary>
        /// Validate response data before parsing
        /// </summary>
        /// <param name="buffer">Response buffer</param>
        /// <param name="length">Actual data length</param>
        /// <param name="actualLength">Reference to actual length (will be updated to valid data length)</param>
        /// <returns>True if data is valid</returns>
        private bool ValidateAndParseResponse(byte[] buffer, int length, ref int actualLength)
        {
            if (length < 6) // Minimum: ReportID + 0x5A + length(2) + checksum + end 0x5A
            {
                Console.WriteLine($"[DEBUG] Response too short: {length} bytes");
                return false;
            }

            // Check the expected response format according to the image:
            // [0] = Report ID (0x20)
            // [1] = 0x5A (start marker)
            // [2-3] = Length (big-endian) - from 1st "5a" to 2nd "5a"
            // [4~n] = JSON payload (response data)
            // [n+1] = Checksum (sum of length + payload, lowest 8 bits)
            // [last] = 0x5A (end marker)

            byte reportId = buffer[0];
            if (reportId != ResponseReportID)
            {
                Console.WriteLine($"[DEBUG] Wrong report ID: 0x{reportId:X2}, expected: 0x{ResponseReportID:X2}");
                return false;
            }

            byte startMarker = buffer[1];
            if (startMarker != StartEndMarker5A)
            {
                Console.WriteLine($"[DEBUG] Wrong start marker: 0x{startMarker:X2}, expected: 0x{StartEndMarker5A:X2}");
                return false;
            }

            // Read the length field (big-endian: [2] = high byte, [3] = low byte)
            // Length represents the distance from 1st "5a" to 2nd "5a" (Ex: length is 0x009A, highbyte is 0x00, lowbyte is 0x9A)
            ushort declaredLength = (ushort)((buffer[2] << 8) | buffer[3]);
            //Console.WriteLine($"[DEBUG] Declared length: {declaredLength} (from 1st 5A to 2nd 5A)");

            // Find the last 0x5A byte (end marker) in the buffer
            int endMarkerIndex = Array.LastIndexOf(buffer, StartEndMarker5A, length - 1);

            if (endMarkerIndex == -1 || endMarkerIndex <= 1) // Should not be the start marker
            {
                Console.WriteLine($"[DEBUG] End marker 0x5A not found or invalid position");
                return false;
            }

            //Console.WriteLine($"[DEBUG] End marker found at index: {endMarkerIndex}");

            // Validate the declared length against actual structure
            // declaredLength should equal the distance from start marker (index 1) to end marker
            int actualDistanceFrom5ATo5A = endMarkerIndex - 1; // Distance from start 5A to end 5A

            if (declaredLength != actualDistanceFrom5ATo5A)
            {
                //Console.WriteLine($"[DEBUG] Length mismatch: declared={declaredLength}, actual distance from 5A to 5A={actualDistanceFrom5ATo5A}");
                // Allow small tolerance for different implementations
                if (Math.Abs(declaredLength - actualDistanceFrom5ATo5A) > 2)
                {
                    return false;
                }
            }

            // Calculate JSON payload area: from index 4 to (endMarkerIndex - 1 - 1) 
            // Structure: [0]=ReportID, [1]=StartMarker, [2-3]=Length, [4 to n]=JSON, [n+1]=Checksum, [last]=EndMarker
            int jsonStartIndex = 4;
            int checksumIndex = endMarkerIndex - 1;
            int jsonEndIndex = checksumIndex - 1;

            if (jsonEndIndex < jsonStartIndex)
            {
                Console.WriteLine($"[DEBUG] Invalid payload structure: json area too small");
                return false;
            }

            int jsonPayloadLength = jsonEndIndex - jsonStartIndex + 1;
            //Console.WriteLine($"[DEBUG] JSON payload length: {jsonPayloadLength}, checksum at index: {checksumIndex}");

            // Validate checksum: sum of length(2 bytes) + JSON payload, take lowest 8 bits
            byte expectedChecksum = 0;

            // Add length bytes (big-endian)
            unchecked
            {
                expectedChecksum += buffer[2]; // High byte of length
                expectedChecksum += buffer[3]; // Low byte of length

                // Add JSON payload bytes
                for (int i = jsonStartIndex; i <= jsonEndIndex; i++)
                {
                    expectedChecksum += buffer[i];
                }
            }

            byte actualChecksum = buffer[checksumIndex];

            if (expectedChecksum != actualChecksum)
            {
                //Console.WriteLine($"[DEBUG] Checksum mismatch: expected=0x{expectedChecksum:X2}, actual=0x{actualChecksum:X2}");
                //return false;
            }

            // Update actualLength to the position right after the end marker
            actualLength = endMarkerIndex + 1;

            //Console.WriteLine($"[DEBUG] Response validation successful!");
            //Console.WriteLine($"[DEBUG] Structure: ReportID[{buffer[0]:X2}] + StartMarker[{buffer[1]:X2}] + Length[{declaredLength}] + JSON[{jsonPayloadLength}] + Checksum[{actualChecksum:X2}] + EndMarker[{buffer[endMarkerIndex]:X2}]");

            return true;
        }

        /// <summary>
        /// Parse display control response
        /// </summary>
        /// <param name="buffer">Response buffer</param>
        /// <param name="length">Buffer length</param>
        private void ParseDisplayControlResponse(byte[] buffer, int length)
        {
            if (length < 10) return; // Minimum for a meaningful response

            // Validate basic structure first
            if (buffer[0] != ResponseReportID || buffer[1] != StartEndMarker5A)
            {
                Console.WriteLine("[DEBUG] Invalid response structure");
                return;
            }

            try
            {
                // Extract translated data (skip reportId and start marker, remove end marker)
                byte[] translatedData = new byte[length - 3]; // Remove reportId, start, end
                Array.Copy(buffer, 2, translatedData, 0, translatedData.Length);

                // Call ReverseTranslatePayload to decode the received data
                byte[] originalData = ReverseTranslatePayload(translatedData);

                if (originalData.Length < 3) return; // Need at least length(2) + some data

                // Now parse the original (untranslated) data
                int offset = 0;

                // Read length (2 bytes, big-endian in original data)
                ushort responseLength = (ushort)((originalData[offset] << 8) | originalData[offset + 1]);
                offset += 2;

                // Subtract the overhead (length field + checksum) from responseLength
                responseLength -= 4;

                // The actual response content length should not exceed available data
                int availableDataLength = originalData.Length - offset - 1; // Subtract checksum byte
                int actualContentLength = Math.Min(responseLength, availableDataLength);

                if (actualContentLength <= 0) return;

                // Parse the response content (assuming HTTP-like format)
                string responseText = Encoding.UTF8.GetString(originalData, offset, actualContentLength);
                responseText = responseText.Trim();

                // Parse HTTP-like response
                var response = ParseHttpResponse(responseText);

                // if AckNumber not 0, then it's a valid response
                if (response.HasValue && response.Value.AckNumber > 0)
                {
                    // Fire the response received event
                    ResponseReceived?.Invoke(this, response.Value);

                    Console.WriteLine($"Response received, AckNumber: {response.Value.AckNumber} - Status: {response.Value.StatusCode}");
                    if (!string.IsNullOrEmpty(response.Value.ResponseData))
                    {
                        Console.WriteLine($"Response data: {response.Value.ResponseData}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing display control response: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse HTTP-like response format
        /// </summary>
        /// <param name="responseText">Response text</param>
        /// <returns>Parsed response or null</returns>
        private DisplayResponse? ParseHttpResponse(string responseText)
        {
            try
            {
                // Clean up the response text (remove null terminators and trim)
                responseText = responseText.TrimEnd('\0').Trim();

                var lines = responseText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return null;

                // Parse status line (e.g., "1 200")
                var statusLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (statusLine.Length < 2) return null;

                string command = statusLine[0]; // First part is the command/version
                if (!int.TryParse(statusLine[1], out int statusCode)) return null;

                int ackNumber = 0;
                string? contentType = null;
                int contentLength = 0;
                string? responseData = null;

                // Parse headers and body
                bool inBody = false;
                var bodyLines = new List<string>();

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];

                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }
                    // if the line have {}, then it's body
                    if (line.Contains("{"))
                    {
                        inBody = true;
                    }


                    if (!inBody)
                    {
                        // Parse headers
                        if (line.StartsWith("AckNumber="))
                        {
                            int.TryParse(line.Substring(10), out ackNumber);
                        }
                        else if (line.StartsWith("ContentType="))
                        {
                            contentType = line.Substring(12);
                            if (string.IsNullOrEmpty(contentType)) contentType = null;
                        }
                        else if (line.StartsWith("ContentLength="))
                        {
                            int.TryParse(line.Substring(14), out contentLength);
                        }
                    }
                    else
                    {
                        // Body content
                        bodyLines.Add(line);
                    }
                }

                // If there's content length specified and we have body data
                if (bodyLines.Count > 0)
                {
                    responseData = string.Join("\n", bodyLines);
                }
                else if (contentLength > 0)
                {
                    // If content length is specified but no body found, it might be in a different format
                    // You might need to handle this case based on your protocol
                }

                return new DisplayResponse(ResponseReportID, (ushort)responseText.Length, command, ackNumber, statusCode, responseData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing HTTP response: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Send command and wait for response
        /// </summary>
        /// <param name="jsonPayload">Command payload</param>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <returns>Response or null if timeout</returns>
        public async Task<DisplayResponse?> SendCommandAndWaitResponse(string jsonPayload, uint sequenceNumber, int timeoutMs = 5000)
        {
            DisplayResponse? response = null;
            var responseReceived = false;
            var waitHandle = new ManualResetEventSlim(false);

            // Set up temporary event handler
            EventHandler<DisplayResponse> handler = (sender, resp) =>
            {
                if (resp.AckNumber == (sequenceNumber + 1))
                {
                    response = resp;
                    responseReceived = true;
                    waitHandle.Set();
                }
            };

            ResponseReceived += handler;

            try
            {
                // Send the command
                SendDisplayCtrlSsrCommandCommand(jsonPayload);

                // Wait for response
                await Task.Run(() =>
                {
                    waitHandle.Wait(timeoutMs);
                });

                return responseReceived ? response : null;
            }
            finally
            {
                ResponseReceived -= handler;
                waitHandle.Dispose();
            }
        }

        /// <summary>
        /// Send command with automatic response handling
        /// </summary>
        /// <param name="command">Command name</param>
        /// <param name="parameters">Command parameters</param>
        /// <param name="waitForResponse">Whether to wait for response</param>
        /// <returns>Response if waiting enabled, null otherwise</returns>
        public async Task<DisplayResponse?> SendCmdWithResponse(string command, object? parameters = null, bool waitForResponse = false)
        {
            uint currentSeqNumber = _currentSeqNumber;
            string jsonContent = parameters != null ? System.Text.Json.JsonSerializer.Serialize(parameters) : "";
            int contentLength = Encoding.UTF8.GetByteCount(jsonContent);
            string jsonPayload = $"POST {command} 1\r\nSeqNumber={currentSeqNumber}\r\nContentType=json\r\nContentLength={contentLength}\r\n\r\n{jsonContent}";
            _currentSeqNumber++;
            if (_currentSeqNumber == (uint.MaxValue - 1))
            {
                _currentSeqNumber = 1;
            }

            if (waitForResponse)
            {
                return await SendCommandAndWaitResponse(jsonPayload, currentSeqNumber);
            }
            else
            {
                SendDisplayCtrlSsrCommandCommand(jsonPayload);
                return null;
            }
        }

        /// <summary>
        /// Enhanced brightness command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdBrightnessWithResponse(int value)
        {
            if (value < 0 || value > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Brightness value must be between 0 and 100");
            }

            return await SendCmdWithResponse("brightness", new { value = value }, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced rotation command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdRotateWithResponse(int degree)
        {
            if (degree != 0 && degree != 90 && degree != 180 && degree != 270)
            {
                throw new ArgumentException("Degree must be 0, 90, 180, or 270", nameof(degree));
            }

            return await SendCmdWithResponse("rotate", new { degree = degree }, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced display in sleep command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdDisplayInSleepWithResponse(bool enable)
        {
            return await SendCmdWithResponse("displayInSleep", new { enable = enable }, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced set suspend command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdSetSuspendWithResponse(string fileName, int fileSize)
        {
            var parameters = new { type = "suspend", fileName = fileName, fileSize = fileSize };
            return await SendCmdWithResponse("transport", parameters, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced suspend completed command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdSuspendCompletedWithResponse(string fileName, string md5)
        {
            var parameters = new { fileName = fileName, md5 = md5 };
            return await SendCmdWithResponse("transported", parameters, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced delete suspend command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdDeleteSuspendWithResponse(string fileName = "all")
        {
            var parameters = new { type = "suspend", fileName = fileName };
            return await SendCmdWithResponse("delMedia", parameters, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced keep alive command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdKeepAliveWithResponse(long timestamp)
        {
            uint currentSeqNumber = _currentSeqNumber;
            var parameters = new { timestamp = timestamp };
            string jsonContent = System.Text.Json.JsonSerializer.Serialize(parameters);
            int contentLength = Encoding.UTF8.GetByteCount(jsonContent);
            string jsonPayload = $"STATE timestamp 1\r\nSeqNumber={currentSeqNumber}\r\nContentType=json\r\nContentLength={contentLength}\r\n\r\n{jsonContent}";
            _currentSeqNumber++;
            // if _currentSeqNumber exceeds uint.MaxValue, reset to 1
            if (_currentSeqNumber == (uint.MaxValue - 1))
            {
                _currentSeqNumber = 1;
            }

            return await SendCommandAndWaitResponse(jsonPayload, currentSeqNumber);
        }

        /// <summary>
        /// Enhanced set keep alive timer command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdSetKeepAliveTimerWithResponse(int value)
        {
            return await SendCmdWithResponse("timeout", new { value = value }, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced read keep alive timer command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdReadKeepAliveTimerWithResponse()
        {
            return await SendCmdWithResponse("param", null, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced read maximum suspend media command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdReadMaxSuspendMediaWithResponse()
        {
            return await SendCmdWithResponse("param", null, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced set background command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdSetBackgroundWithResponse(string fileName, int fileSize)
        {
            var parameters = new { type = "media", fileName = fileName, fileSize = fileSize };
            return await SendCmdWithResponse("transport", parameters, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced background completed command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdBackgroundCompletedWithResponse(string fileName, string md5)
        {
            var parameters = new { fileName = fileName, md5 = md5 };
            return await SendCmdWithResponse("transported", parameters, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced real-time display command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdRealTimeDisplayWithResponse(bool enable)
        {
            return await SendCmdWithResponse("realtimeDisplay", new { enable = enable }, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced read rotated angle command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdReadRotatedAngleWithResponse()
        {
            return await SendCmdWithResponse("param", null, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced read brightness command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdReadBrightnessWithResponse()
        {
            return await SendCmdWithResponse("param", null, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced firmware upgrade command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdFirmwareUpgradeWithResponse(string fileName, int fileSize)
        {
            var parameters = new { type = "firmware", fileName = fileName, fileSize = fileSize };
            return await SendCmdWithResponse("transport", parameters, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced firmware upgrade completed command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdFirmwareUpgradeCompletedWithResponse(string fileName, string md5)
        {
            var parameters = new { fileName = fileName, md5 = md5 };
            return await SendCmdWithResponse("transported", parameters, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced set powerup media command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdSetPowerupMediaWithResponse(string fileName, int fileSize)
        {
            var parameters = new { type = "logo", fileName = fileName, fileSize = fileSize };
            return await SendCmdWithResponse("transport", parameters, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced powerup media completed command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdPowerupMediaCompletedWithResponse(string fileName, string md5)
        {
            var parameters = new { fileName = fileName, md5 = md5 };
            return await SendCmdWithResponse("transported", parameters, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced set device serial number command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdSetDeviceSNWithResponse(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber))
            {
                throw new ArgumentException("Serial number cannot be null or empty", nameof(serialNumber));
            }

            // Validate that it's a valid hex string and has proper length
            if (serialNumber.Length != 32 || !System.Text.RegularExpressions.Regex.IsMatch(serialNumber, "^[0-9A-Fa-f]+$"))
            {
                throw new ArgumentException("Serial number must be a 32-character hexadecimal string", nameof(serialNumber));
            }

            return await SendCmdWithResponse("setDeviceSN", new { sn = serialNumber.ToUpper() }, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced get device serial number command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdGetDeviceSNWithResponse()
        {
            return await SendCmdWithResponse("getDeviceSN", null, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced set color SKU command with response (string version)
        /// </summary>
        public async Task<DisplayResponse?> SendCmdSetColorSkuWithResponse(string color)
        {
            if (string.IsNullOrEmpty(color))
            {
                throw new ArgumentException("Color cannot be null or empty", nameof(color));
            }

            // Ensure color starts with "0x" and is valid hex
            if (!color.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Color must start with '0x'", nameof(color));
            }

            string hexPart = color.Substring(2);
            if (!System.Text.RegularExpressions.Regex.IsMatch(hexPart, "^[0-9A-Fa-f]+$"))
            {
                throw new ArgumentException("Color must be a valid hexadecimal value", nameof(color));
            }

            return await SendCmdWithResponse("setSKUColor", new { color = color }, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced set color SKU command with response (integer version)
        /// </summary>
        public async Task<DisplayResponse?> SendCmdSetColorSkuWithResponse(int colorValue)
        {
            string color = $"0x{colorValue:X4}";
            return await SendCmdSetColorSkuWithResponse(color);
        }

        /// <summary>
        /// Enhanced get color SKU command with response
        /// </summary>
        public async Task<DisplayResponse?> SendCmdGetColorSkuWithResponse()
        {
            return await SendCmdWithResponse("getSKUColor", null, waitForResponse: true);
        }

        /// <summary>
        /// Get device capabilities with response parsing
        /// </summary>
        public async Task<DisplayResponse?> SendCmdGetStatusWithResponse()
        {
            return await SendCmdWithResponse("param", null, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced device serial number request with response
        /// </summary>
        public async Task<string?> GetDeviceSerialNumber()
        {
            var response = await SendCmdWithResponse("getDeviceSN", null, waitForResponse: true);

            if (response?.IsSuccess == true && !string.IsNullOrEmpty(response.Value.ResponseData))
            {
                try
                {
                    var jsonResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(response.Value.ResponseData);
                    if (jsonResponse?.ContainsKey("sn") == true)
                    {
                        return jsonResponse["sn"].ToString();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to parse serial number response: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// Enhanced color SKU request with response
        /// </summary>
        public async Task<string?> GetColorSku()
        {
            var response = await SendCmdWithResponse("getSKUColor", null, waitForResponse: true);

            if (response?.IsSuccess == true && !string.IsNullOrEmpty(response.Value.ResponseData))
            {
                try
                {
                    var jsonResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(response.Value.ResponseData);
                    if (jsonResponse?.ContainsKey("color") == true)
                    {
                        return jsonResponse["color"].ToString();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to parse color SKU response: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// Complete background workflow with response handling
        /// </summary>
        /// <param name="filePath">Path to the background file</param>
        /// <param name="transferId">Unique transfer ID (0-59)</param>
        /// <returns>True if all steps completed successfully</returns>
        public async Task<bool> SetBackgroundWithFileAndResponse(string filePath, byte transferId = 1)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Background file not found: {filePath}");
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                string fileName = fileInfo.Name;
                int fileSize = (int)fileInfo.Length;

                Console.WriteLine($"Setting background with response handling: {fileName} ({fileSize} bytes)");

                // Step 1: Send background transport command with response
                var transportResponse = await SendCmdSetBackgroundWithResponse(fileName, fileSize);
                if (transportResponse?.IsSuccess != true)
                {
                    Console.WriteLine($"Background transport command failed. Status: {transportResponse?.StatusCode}");
                    return false;
                }
                Console.WriteLine($"Background transport command successful. Response: {transportResponse.Value.ResponseData}");

                // Wait for device to process
                Thread.Sleep(1500);

                // Step 2: Transfer the actual file data
                SendFileFromDisk(filePath, transferId);
                Console.WriteLine("Background file data transfer completed");

                // Step 3: Send completion command with response
                string md5Hash = CalculateMD5Hash(filePath);
                var completionResponse = await SendCmdBackgroundCompletedWithResponse(fileName, md5Hash + 1);
                if (completionResponse?.IsSuccess != true)
                {
                    Console.WriteLine($"Background completion command failed. Status: {completionResponse?.StatusCode}");
                    return false;
                }
                Console.WriteLine($"Background completion command successful. Response: {completionResponse.Value.ResponseData}");

                Console.WriteLine("Background setup with response handling completed successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in background setup with response: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Complete powerup media workflow with response handling
        /// </summary>
        /// <param name="filePath">Path to the powerup media file</param>
        /// <param name="transferId">Unique transfer ID (0-59)</param>
        /// <returns>True if all steps completed successfully</returns>
        public async Task<bool> SetPowerupMediaWithFileAndResponse(string filePath, byte transferId = 1)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Powerup media file not found: {filePath}");
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                string fileName = fileInfo.Name;
                int fileSize = (int)fileInfo.Length;

                Console.WriteLine($"Setting powerup media with response handling: {fileName} ({fileSize} bytes)");

                // Step 1: Send powerup media transport command with response
                var transportResponse = await SendCmdSetPowerupMediaWithResponse(fileName, fileSize);
                if (transportResponse?.IsSuccess != true)
                {
                    Console.WriteLine($"Powerup media transport command failed. Status: {transportResponse?.StatusCode}");
                    return false;
                }
                Console.WriteLine($"Powerup media transport command successful. Response: {transportResponse.Value.ResponseData}");

                // Wait for device to process
                Thread.Sleep(1500);

                // Step 2: Transfer the actual file data
                SendFileFromDisk(filePath, transferId);
                Console.WriteLine("Powerup media file data transfer completed");

                // Step 3: Send completion command with response
                string md5Hash = CalculateMD5Hash(filePath);
                var completionResponse = await SendCmdPowerupMediaCompletedWithResponse(fileName, md5Hash + 1);
                if (completionResponse?.IsSuccess != true)
                {
                    Console.WriteLine($"Powerup media completion command failed. Status: {completionResponse?.StatusCode}");
                    return false;
                }
                Console.WriteLine($"Powerup media completion command successful. Response: {completionResponse.Value.ResponseData}");

                Console.WriteLine("Powerup media setup with response handling completed successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in powerup media setup with response: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Complete firmware upgrade workflow with response handling
        /// </summary>
        /// <param name="filePath">Path to the firmware file</param>
        /// <param name="transferId">Unique transfer ID (0-59)</param>
        /// <returns>True if all steps completed successfully</returns>
        public async Task<bool> FirmwareUpgradeWithFileAndResponse(string filePath, byte transferId = 1)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Firmware file not found: {filePath}");
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                string fileName = fileInfo.Name;
                int fileSize = (int)fileInfo.Length;

                Console.WriteLine($"Starting firmware upgrade with response handling: {fileName} ({fileSize} bytes)");

                // Step 1: Send firmware upgrade transport command with response
                var transportResponse = await SendCmdFirmwareUpgradeWithResponse(fileName, fileSize);
                if (transportResponse?.IsSuccess != true)
                {
                    Console.WriteLine($"Firmware upgrade transport command failed. Status: {transportResponse?.StatusCode}");
                    return false;
                }
                Console.WriteLine($"Firmware upgrade transport command successful. Response: {transportResponse.Value.ResponseData}");

                // Step 2: Transfer the actual file data
                SendFileFromDisk(filePath, transferId);
                Console.WriteLine("Firmware file data transfer completed");

                // Step 3: Send completion command with response
                string md5Hash = CalculateMD5Hash(filePath);
                var completionResponse = await SendCmdFirmwareUpgradeCompletedWithResponse(fileName, md5Hash + 1);
                if (completionResponse?.IsSuccess != true)
                {
                    Console.WriteLine($"Firmware upgrade completion command failed. Status: {completionResponse?.StatusCode}");
                    return false;
                }
                Console.WriteLine($"Firmware upgrade completion command successful. Response: {completionResponse.Value.ResponseData}");

                Console.WriteLine("Firmware upgrade with response handling completed successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in firmware upgrade with response: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Enhanced OSD command with response
        /// </summary>
        /// <param name="fileName">OSD file name</param>
        /// <param name="fileSize">File size in bytes</param>
        /// <returns>Response from the device</returns>
        public async Task<DisplayResponse?> SendCmdSetOsdWithResponse(string fileName, int fileSize)
        {
            var parameters = new { type = "media", fileName = fileName, fileSize = fileSize };
            return await SendCmdWithResponse("transport", parameters, waitForResponse: true);
        }

        /// <summary>
        /// Enhanced OSD completed command with response
        /// </summary>
        /// <param name="fileName">OSD file name</param>
        /// <param name="md5">MD5 hash of the file</param>
        /// <returns>Response from the device</returns>
        public async Task<DisplayResponse?> SendCmdOsdCompletedWithResponse(string fileName, string md5)
        {
            var parameters = new { fileName = fileName, md5 = md5 };
            return await SendCmdWithResponse("transported", parameters, waitForResponse: true);
        }

        /// <summary>
        /// Complete OSD workflow with response handling
        /// </summary>
        /// <param name="filePath">Path to the OSD file</param>
        /// <param name="transferId">Unique transfer ID (0-59)</param>
        /// <returns>True if all steps completed successfully</returns>
        public async Task<bool> SetOsdWithFileAndResponse(string filePath, byte transferId = 1)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"OSD file not found: {filePath}");
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                string fileName = fileInfo.Name;
                int fileSize = (int)fileInfo.Length;

                Console.WriteLine($"Setting OSD with response handling: {fileName} ({fileSize} bytes)");

                // Step 1: Send OSD transport command with response
                var transportResponse = await SendCmdSetOsdWithResponse(fileName, fileSize);
                if (transportResponse?.IsSuccess != true)
                {
                    Console.WriteLine($"OSD transport command failed. Status: {transportResponse?.StatusCode}");
                    return false;
                }
                Console.WriteLine($"OSD transport command successful. Response: {transportResponse.Value.ResponseData}");

                // Wait for device to process
                Thread.Sleep(1500);

                // Step 2: Transfer the actual file data
                SendFileFromDisk(filePath, transferId);
                Console.WriteLine("OSD file data transfer completed");

                // Step 3: Send completion command with response
                string md5Hash = CalculateMD5Hash(filePath);
                var completionResponse = await SendCmdOsdCompletedWithResponse(fileName, md5Hash + 1);
                if (completionResponse?.IsSuccess != true)
                {
                    Console.WriteLine($"OSD completion command failed. Status: {completionResponse?.StatusCode}");
                    return false;
                }
                Console.WriteLine($"OSD completion command successful. Response: {completionResponse.Value.ResponseData}");

                Console.WriteLine("OSD setup with response handling completed successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OSD setup with response: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Complete workflow to set suspend mode with file transfer and response handling
        /// </summary>
        /// <param name="filePath">Path to the suspend video file</param>
        /// <param name="transferId">Unique transfer ID (0-59)</param>
        /// <returns>True if all steps completed successfully</returns>
        public async Task<bool> SetSuspendModeWithResponse()
        {
            var step1Response = await SendCmdRealTimeDisplayWithResponse(false);
            if (step1Response?.IsSuccess != true)
            {
                Console.WriteLine($"SetSuspendModeWithResponse. Status: {step1Response?.StatusCode}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Send a suspend file with complete workflow including transport, file transfer, and completion
        /// </summary>
        /// <param name="filePath">Path to the suspend file</param>
        /// <param name="transferId">Unique transfer ID (0-59)</param>
        /// <returns>True if all steps completed successfully</returns>
        public async Task<bool> SendSuspendFileWithResponse(string filePath, byte transferId = 1)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Suspend file not found: {filePath}");
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                string fileName = fileInfo.Name;
                int fileSize = (int)fileInfo.Length;

                Console.WriteLine($"Sending suspend file with response handling: {fileName} ({fileSize} bytes)");

                // Step 1: Set suspend file configuration
                Console.WriteLine($"Step 1: Setting suspend file configuration for {fileName}...");
                var step1Response = await SendCmdSetSuspendWithResponse(fileName, fileSize);
                if (step1Response?.IsSuccess != true)
                {
                    Console.WriteLine($"Step 1 failed. Status: {step1Response?.StatusCode}");
                    return false;
                }
                Console.WriteLine("Step 1 successful");

                // Step 2: Transfer the actual file data
                Console.WriteLine("Step 2: Transferring suspend file data...");
                SendFileFromDisk(filePath, transferId);
                Console.WriteLine("Suspend file data transfer completed");

                // Step 3: Send suspend completion command with MD5 hash
                Console.WriteLine("Step 3: Sending suspend completion command...");
                string md5Hash = CalculateMD5Hash(filePath);
                var step3Response = await SendCmdSuspendCompletedWithResponse(fileName, md5Hash + 1);
                if (step3Response?.IsSuccess != true)
                {
                    Console.WriteLine($"Step 3 failed. Status: {step3Response?.StatusCode}");
                    return false;
                }
                Console.WriteLine("Step 3 successful");

                Console.WriteLine($"Suspend file {fileName} sent successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending suspend file: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send multiple suspend files with complete workflow
        /// </summary>
        /// <param name="filePaths">List of file paths to send</param>
        /// <param name="startingTransferId">Starting transfer ID (0-59)</param>
        /// <returns>True if all files were sent successfully</returns>
        public async Task<bool> SendMultipleSuspendFilesWithResponse(List<string> filePaths, byte startingTransferId = 1)
        {
            if (filePaths == null || filePaths.Count == 0)
            {
                Console.WriteLine("No files provided to send");
                return false;
            }

            Console.WriteLine($"Sending {filePaths.Count} suspend files...");

            for (int i = 0; i < filePaths.Count; i++)
            {
                string filePath = filePaths[i];
                byte transferId = (byte)((startingTransferId + i) % 60); // Ensure within valid range

                Console.WriteLine($"Processing file {i + 1}/{filePaths.Count}: {Path.GetFileName(filePath)}");

                bool success = await SendSuspendFileWithResponse(filePath, transferId);
                if (!success)
                {
                    Console.WriteLine($"Failed to send file {i + 1}: {Path.GetFileName(filePath)}");
                    return false;
                }

                // Small delay between files to avoid overwhelming the device
                await Task.Delay(100);
            }

            Console.WriteLine("All suspend files sent successfully!");
            return true;
        }

        /// <summary>
        /// Delete suspend media files with response handling
        /// </summary>
        /// <param name="fileName">Suspend file name to delete (defaults to "all" to delete all files)</param>
        /// <returns>True if deletion was successful</returns>
        public async Task<bool> DeleteSuspendFilesWithResponse(string fileName = "all")
        {
            try
            {
                Console.WriteLine($"Deleting suspend media: {fileName}");

                var deleteResponse = await SendCmdDeleteSuspendWithResponse(fileName);
                if (deleteResponse?.IsSuccess != true)
                {
                    Console.WriteLine($"Delete suspend media failed. Status: {deleteResponse?.StatusCode}");
                    if (!string.IsNullOrEmpty(deleteResponse?.ResponseData))
                    {
                        Console.WriteLine($"Error details: {deleteResponse.Value.ResponseData}");
                    }
                    return false;
                }

                Console.WriteLine($"Successfully deleted suspend media: {fileName}");
                if (!string.IsNullOrEmpty(deleteResponse.Value.ResponseData))
                {
                    Console.WriteLine($"Response: {deleteResponse.Value.ResponseData}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting suspend files: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Delete multiple specific suspend files with response handling
        /// </summary>
        /// <param name="fileNames">List of suspend file names to delete</param>
        /// <param name="startingSequenceNumber">Starting sequence number for commands</param>
        /// <returns>True if all deletions were successful</returns>
        public async Task<bool> DeleteMultipleSuspendFilesWithResponse(List<string> fileNames, int startingSequenceNumber = 42)
        {
            if (fileNames == null || fileNames.Count == 0)
            {
                Console.WriteLine("No files provided to delete");
                return false;
            }

            Console.WriteLine($"Deleting {fileNames.Count} suspend files...");

            for (int i = 0; i < fileNames.Count; i++)
            {
                string fileName = fileNames[i];

                Console.WriteLine($"Deleting file {i + 1}/{fileNames.Count}: {fileName}");

                bool success = await DeleteSuspendFilesWithResponse(fileName);
                if (!success)
                {
                    Console.WriteLine($"Failed to delete file {i + 1}: {fileName}");
                    return false;
                }

                // Small delay between commands to avoid overwhelming the device
                await Task.Delay(100);
            }

            Console.WriteLine("All suspend files deleted successfully!");
            return true;
        }

        /// <summary>
        /// Clear all suspend media and exit suspend mode
        /// </summary>
        /// <returns>True if operation was successful</returns>
        public async Task<bool> ClearSuspendModeWithResponse()
        {
            try
            {
                Console.WriteLine("Clearing suspend mode by deleting all suspend media...");

                // Delete all suspend media
                bool deleteSuccess = await DeleteSuspendFilesWithResponse("all");
                if (!deleteSuccess)
                {
                    return false;
                }

                // Sleep 100
                Thread.Sleep(100);

                // Optional: Read status to confirm
                Console.WriteLine("Reading suspend media status after deletion...");
                var statusResponse = await SendCmdReadMaxSuspendMediaWithResponse();
                if (statusResponse?.IsSuccess == true && !string.IsNullOrEmpty(statusResponse.Value.ResponseData))
                {
                    Console.WriteLine($"Suspend media status: {statusResponse.Value.ResponseData}");
                    // {"brightness": 80,"degree": 0,"osdState": 0,"keepAliveTimeout": 5,"maxSuspendMediaCount": 5,"displayInSleep": 0,"suspendMediaActive": [☻0,0,0,0,0]}
                    // Get the [☻0,0,0,0,0] value
                    try
                    {
                        var jsonResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(statusResponse.Value.ResponseData);
                        if (jsonResponse?.ContainsKey("suspendMediaActive") == true)
                        {
                            var activeArray = jsonResponse["suspendMediaActive"].ToString();
                            Console.WriteLine($"Current suspend media active status: {activeArray}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to parse suspend media status response: {ex.Message}");
                    }
                }

                Console.WriteLine("Suspend mode cleared successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing suspend mode: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Delete specific suspend files by their indices (1-based)
        /// </summary>
        /// <param name="indices">List of 1-based indices to delete (e.g., [1, 3, 5])</param>
        /// <param name="startingSequenceNumber">Starting sequence number for commands</param>
        /// <returns>True if all deletions were successful</returns>
        public async Task<bool> DeleteSuspendFilesByIndexWithResponse(List<int> indices, int startingSequenceNumber = 42)
        {
            if (indices == null || indices.Count == 0)
            {
                Console.WriteLine("No indices provided to delete");
                return false;
            }

            // Convert indices to file names (assuming format like "suspend_0.jpg", "suspend_1.jpg", etc.)
            var fileNames = new List<string>();
            foreach (int index in indices)
            {
                if (index < 1)
                {
                    Console.WriteLine($"Invalid index: {index}. Indices must be 1-based.");
                    return false;
                }

                // Convert 1-based index to 0-based file name
                string fileName = $"suspend_{index - 1}.jpg"; // You can adjust the naming convention as needed
                fileNames.Add(fileName);
            }

            Console.WriteLine($"Deleting suspend files by indices: [{string.Join(", ", indices)}]");
            return await DeleteMultipleSuspendFilesWithResponse(fileNames, startingSequenceNumber);
        }


        /// 
        /// Demo workflow sample
        /// 
        public async Task<bool> SetSuspendModeDemo()
        {
            // Step 1: Disable real-time display mode and entry to suspend mode by call SetSuspendModeWithResponse 
            bool step1Response = await SetSuspendModeWithResponse();
            if (step1Response != true)
            {
                return false;
            }

            // Init files list to transfer
            List<string> filesToTransfer = new List<string>
        {
            @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources\suspend_0.jpg",
            @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources\suspend_1.jpg",
            @"C:\out\MyLed\CMDevicesManager\HidProtocol\resources\suspend_2.mp4"
        };

            // Step 2: Send multiple suspend files/ or call SendSuspendFileWithResponse to send single file.
            bool filesSuccess = await SendMultipleSuspendFilesWithResponse(filesToTransfer, startingTransferId: 2);
            if (!filesSuccess)
            {
                Console.WriteLine("Failed to send suspend files");
                return false;
            }

            // Step 3: Set brightness to 80 or other value.
            Console.WriteLine("Setting brightness to 80...");
            var brightnessResponse = await SendCmdBrightnessWithResponse(80);
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
            var timerResponse = await SendCmdSetKeepAliveTimerWithResponse(5);
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
            var maxMediaResponse = await SendCmdReadMaxSuspendMediaWithResponse();
            if (maxMediaResponse?.IsSuccess == true && !string.IsNullOrEmpty(maxMediaResponse.Value.ResponseData))
            {
                Console.WriteLine($"Max suspend media response: {maxMediaResponse.Value.ResponseData}");
            }

            // Sleep 20 seconds, then call delete suspend media command to exit suspend mode.
            Console.WriteLine("Entering suspend mode for 40 seconds...");
            Thread.Sleep(40000);

            Console.WriteLine("Exiting suspend mode by deleting all suspend media...");
            bool clearSuccess = await ClearSuspendModeWithResponse();
            if (!clearSuccess)
            {
                Console.WriteLine("Failed to clear suspend mode");
                return false;
            }

            Console.WriteLine("Suspend mode demo completed successfully!");
            return true;
        }

        /// <summary>
        /// Complete workflow to set suspend mode with file transfer and response handling
        /// </summary>
        /// <param name="filePath">Path to the suspend video file</param>
        /// <param name="transferId">Unique transfer ID (0-59)</param>
        /// <returns>True if all steps completed successfully</returns>
        public async Task<bool> SetSuspendModeWithResponse_back(string filePath, byte transferId = 1)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Suspend file not found: {filePath}");
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                string fileName = fileInfo.Name;
                int fileSize = (int)fileInfo.Length;

                Console.WriteLine($"Setting suspend mode with response handling: {fileName} ({fileSize} bytes)");

                //// Pre-step: Delete all existing suspend media
                //Console.WriteLine("Pre-step: Deleting all existing suspend media...");
                //var deleteResponse = await SendCmdDeleteSuspendWithResponse("all" - 4);
                //if (deleteResponse?.IsSuccess != true)
                //{
                //    Console.WriteLine($"Delete suspend media failed. Status: {deleteResponse?.StatusCode}");
                //    // Continue anyway as this might not be critical
                //}
                //else
                //{
                //    Console.WriteLine("Delete suspend media successful");
                //}

                //// Pre-step: Set rotation to 0
                //Console.WriteLine("Pre-step: Setting rotation to 0...");
                //var rotationResponse = await SendCmdRotateWithResponse(0 - 3);
                //if (rotationResponse?.IsSuccess != true)
                //{
                //    Console.WriteLine($"Set rotation failed. Status: {rotationResponse?.StatusCode}");
                //    // Continue anyway as this might not be critical
                //}
                //else
                //{
                //    Console.WriteLine("Set rotation successful");
                //}


                //// Pre-step: Read maximum suspend media count
                //Console.WriteLine("Pre-step: Reading maximum suspend media count...");
                //var maxMediaResponse = await SendCmdReadMaxSuspendMediaWithResponse(sequenceNumber - 1);
                //if (maxMediaResponse?.IsSuccess == true && !string.IsNullOrEmpty(maxMediaResponse.Value.ResponseData))
                //{
                //    Console.WriteLine($"Max suspend media response: {maxMediaResponse.Value.ResponseData}");
                //}

                // Step 1: Disable real-time display mode
                Console.WriteLine("Step 1: Disabling real-time display mode...");
                var step1Response = await SendCmdRealTimeDisplayWithResponse(false);
                if (step1Response?.IsSuccess != true)
                {
                    Console.WriteLine($"Step 1 failed. Status: {step1Response?.StatusCode}");
                    return false;
                }
                Console.WriteLine("Step 1 successful");

                // Step 2: Set suspend file configuration
                Console.WriteLine($"Step 2: Setting suspend file configuration for {fileName}...");
                var step2Response = await SendCmdSetSuspendWithResponse(fileName, fileSize + 1);
                if (step2Response?.IsSuccess != true)
                {
                    Console.WriteLine($"Step 2 failed. Status: {step2Response?.StatusCode}");
                    return false;
                }
                Console.WriteLine("Step 2 successful");

                // Step 3: Transfer the actual file data
                Console.WriteLine("Step 3: Transferring suspend file data...");
                SendFileFromDisk(filePath, transferId);
                Console.WriteLine("Suspend file data transfer completed");

                // Step 4: Send suspend completion command with MD5 hash
                Console.WriteLine("Step 4: Sending suspend completion command...");
                string md5Hash = CalculateMD5Hash(filePath);
                var step4Response = await SendCmdSuspendCompletedWithResponse(fileName, md5Hash + 2);
                if (step4Response?.IsSuccess != true)
                {
                    Console.WriteLine($"Step 4 failed. Status: {step4Response?.StatusCode}");
                    return false;
                }
                Console.WriteLine("Step 4 successful");

                Thread.Sleep(200);

                // Step 5: Disable real-time display mode again
                Console.WriteLine("Step 5: Disabling real-time display mode again...");
                var step5Response = await SendCmdRealTimeDisplayWithResponse(false);
                if (step5Response?.IsSuccess != true)
                {
                    Console.WriteLine($"Step 5 failed. Status: {step5Response?.StatusCode}");
                    return false;
                }
                Console.WriteLine("Step 5 successful");


                // Pre-step: Set brightness to 80
                Console.WriteLine("Pre-step: Setting brightness to 80...");
                var brightnessResponse = await SendCmdBrightnessWithResponse(80 - 2);
                if (brightnessResponse?.IsSuccess != true)
                {
                    Console.WriteLine($"Set brightness failed. Status: {brightnessResponse?.StatusCode}");
                    // Continue anyway as this might not be critical
                }
                else
                {
                    Console.WriteLine("Set brightness successful");
                }

                // Step 6: Set keep alive timer
                Console.WriteLine("Step 6: Setting keep alive timer to 2 seconds...");
                var timerResponse = await SendCmdSetKeepAliveTimerWithResponse(2 + 4);
                if (timerResponse?.IsSuccess != true)
                {
                    Console.WriteLine($"Step 6 failed. Status: {timerResponse?.StatusCode}");
                    // Continue anyway as this might not be critical
                }
                else
                {
                    Console.WriteLine("Step 6 successful");
                }

                // Step 7: Read maximum suspend media count again
                Console.WriteLine("Step 7: Reading maximum suspend media count again...");
                var step7Response = await SendCmdReadMaxSuspendMediaWithResponse();
                if (step7Response?.IsSuccess != true)
                {
                    Console.WriteLine($"Step 7 failed. Status: {step7Response?.StatusCode}");
                    // Continue anyway as this is just a status check
                }
                else
                {
                    Console.WriteLine($"Step 7 successful. Response: {step7Response.Value.ResponseData}");
                }

                Console.WriteLine("Suspend mode setup with response handling completed successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting suspend mode: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Simple real-time display demo with basic image cycling
        /// </summary>
        /// <param name="imageDirectory">Directory containing images (optional)</param>
        /// <param name="cycleCount">Number of image cycles</param>
        /// <returns>True if demo completed successfully</returns>
        public async Task<bool> SimpleRealTimeDisplayDemo(string? imageDirectory = null, int cycleCount = 5)
        {
            Console.WriteLine("=== Simple Real-Time Display Demo ===");

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
                var enableResponse = await SendCmdRealTimeDisplayWithResponse(true);
                if (enableResponse?.IsSuccess != true)
                {
                    Console.WriteLine("Failed to enable real-time display");
                    return false;
                }

                // Step 2: Set brightness to 80 or other value.
                await SendCmdBrightnessWithResponse(80);
                // Set optimal settings
                //await SendCmdSetKeepAliveTimerWithResponse(60: 23);


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
                            SendFileFromDisk(imagePath, transferId: transferId);
                            // Step 4: Send keep-alive command to display the image.
                            await SendCmdKeepAliveWithResponse(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + cycle);

                            transferId++;
                            if (transferId > 59) transferId = 2;

                            await Task.Delay(50); // 1.5 second between images
                        }
                    }
                }

                // Disable real-time display
                Console.WriteLine("Disabling real-time display...");
                await SendCmdRealTimeDisplayWithResponse(false);

                Console.WriteLine("=== Simple Real-Time Display Demo Completed ===");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Demo failed: {ex.Message}");
                return false;
            }
        }

    }


}
