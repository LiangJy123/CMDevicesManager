using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace HIDAPIHelper
{



    internal static class HidApi
    {
        // Adjust for platform if needed:
        // On Windows: "hidapi.dll" (or "hidapi-winapi.dll" depending on how you built)
        // On Linux: "hidapi-hidraw" or "hidapi-libusb"
        // On macOS: "hidapi"
#if WINDOWS
    private const string LIB = "hidapi.dll";
#else
        private const string LIB = "hidapi";
#endif

        // hid_device is opaque
        internal sealed class HidDeviceHandle : SafeHandle
        {
            private HidDeviceHandle() : base(IntPtr.Zero, true) { }
            public override bool IsInvalid => handle == IntPtr.Zero;
            protected override bool ReleaseHandle()
            {
                hid_close(handle);
                return true;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct hid_device_info
        {
            public IntPtr path;                 // char*
            public ushort vendor_id;
            public ushort product_id;
            public IntPtr serial_number;        // wchar_t*
            public ushort release_number;
            public IntPtr manufacturer_string;  // wchar_t*
            public IntPtr product_string;       // wchar_t*
            public ushort usage_page;
            public ushort usage;
            public int interface_number;
            public IntPtr next;                 // struct hid_device_info*
            public int bus_type;                // enum hid_bus_type
        }

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_init();

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_exit();

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr hid_enumerate(ushort vendor_id, ushort product_id);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void hid_free_enumeration(IntPtr devs);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern HidDeviceHandle hid_open(ushort vendor_id, ushort product_id, string? serial_number);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern HidDeviceHandle hid_open_path([MarshalAs(UnmanagedType.LPStr)] string path);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void hid_close(IntPtr dev);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_write(HidDeviceHandle dev, byte[] data, IntPtr length);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_read(HidDeviceHandle dev, byte[] data, IntPtr length);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_read_timeout(HidDeviceHandle dev, byte[] data, IntPtr length, int milliseconds);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_set_nonblocking(HidDeviceHandle dev, int nonblock);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_send_feature_report(HidDeviceHandle dev, byte[] data, IntPtr length);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_get_feature_report(HidDeviceHandle dev, byte[] data, IntPtr length);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr hid_error(HidDeviceHandle? dev);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr hid_read_error(HidDeviceHandle dev); // if using >=0.15

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_get_manufacturer_string(HidDeviceHandle dev, IntPtr buf, IntPtr maxlen);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_get_product_string(HidDeviceHandle dev, IntPtr buf, IntPtr maxlen);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_get_serial_number_string(HidDeviceHandle dev, IntPtr buf, IntPtr maxlen);

        internal static string? PtrToAnsiString(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(p);
        internal static string? PtrToUniString(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringUni(p);

        internal static string GetLastError(HidDeviceHandle? dev)
        {
            IntPtr p = hid_error(dev);
            return PtrToUniString(p) ?? string.Empty;
        }

        public static IEnumerable<hid_device_info> Enumerate(ushort vid = 0, ushort pid = 0)
        {
            IntPtr head = hid_enumerate(vid, pid);
            try
            {
                IntPtr cur = head;
                while (cur != IntPtr.Zero)
                {
                    var info = Marshal.PtrToStructure<hid_device_info>(cur);
                    yield return info;
                    cur = info.next;
                }
            }
            finally
            {
                if (head != IntPtr.Zero) hid_free_enumeration(head);
            }
        }
    }
}