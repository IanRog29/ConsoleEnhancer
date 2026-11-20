// Hr.cs - HRESULT checking and small COM helpers for the Switch2Engine interop layer (C# 5, .NET Framework 4.8).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ConsoleEnhancer.Motor.Native
{
    /// <summary>Thrown by Hr.Check. Exception.HResult holds the code.</summary>
    public sealed class HrException : Exception
    {
        public readonly string What;

        public HrException(int hr, string what)
            : base(what + " failed: " + Hr.Format(hr) + DeviceRemovedSuffix(hr))
        {
            HResult = hr;
            What = what;
        }

        static string DeviceRemovedSuffix(int hr)
        {
            if (!Hr.IsDeviceLost(hr) || Hr.DeviceRemovedReason == null) return "";
            try { return " [GetDeviceRemovedReason = " + Hr.Format(Hr.DeviceRemovedReason()) + "]"; }
            catch (Exception) { return ""; }
        }
    }

    public static class Hr
    {
        public const int S_OK = 0;
        public const int S_FALSE = 1;
        public const int E_NOTIMPL = unchecked((int)0x80004001);
        public const int E_NOINTERFACE = unchecked((int)0x80004002);
        public const int E_POINTER = unchecked((int)0x80004003);
        public const int E_ABORT = unchecked((int)0x80004004);
        public const int E_FAIL = unchecked((int)0x80004005);
        public const int E_UNEXPECTED = unchecked((int)0x8000FFFF);
        public const int E_ACCESSDENIED = unchecked((int)0x80070005);
        public const int E_HANDLE = unchecked((int)0x80070006);
        public const int E_OUTOFMEMORY = unchecked((int)0x8007000E);
        public const int E_INVALIDARG = unchecked((int)0x80070057);
        public const int DXGI_STATUS_OCCLUDED = 0x087A0001;
        public const int DXGI_STATUS_MODE_CHANGED = 0x087A0007;
        public const int DXGI_STATUS_MODE_CHANGE_IN_PROGRESS = 0x087A0008;
        public const int DXGI_ERROR_INVALID_CALL = unchecked((int)0x887A0001);
        public const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
        public const int DXGI_ERROR_MORE_DATA = unchecked((int)0x887A0003);
        public const int DXGI_ERROR_UNSUPPORTED = unchecked((int)0x887A0004);
        public const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
        public const int DXGI_ERROR_DEVICE_HUNG = unchecked((int)0x887A0006);
        public const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);
        public const int DXGI_ERROR_WAS_STILL_DRAWING = unchecked((int)0x887A000A);
        public const int DXGI_ERROR_FRAME_STATISTICS_DISJOINT = unchecked((int)0x887A000B);
        public const int DXGI_ERROR_GRAPHICS_VIDPN_SOURCE_IN_USE = unchecked((int)0x887A000C);
        public const int DXGI_ERROR_DRIVER_INTERNAL_ERROR = unchecked((int)0x887A0020);
        public const int DXGI_ERROR_NONEXCLUSIVE = unchecked((int)0x887A0021);
        public const int DXGI_ERROR_NOT_CURRENTLY_AVAILABLE = unchecked((int)0x887A0022);
        public const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
        public const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
        public const int DXGI_ERROR_SESSION_DISCONNECTED = unchecked((int)0x887A0028);
        public const int DXGI_ERROR_ACCESS_DENIED = unchecked((int)0x887A002B);
        public const int DXGI_ERROR_NAME_ALREADY_EXISTS = unchecked((int)0x887A002C);
        public const int DXGI_ERROR_SDK_COMPONENT_MISSING = unchecked((int)0x887A002D);   // e.g. D3D12GetDebugInterface without Graphics Tools
        public const int D3D12_ERROR_ADAPTER_NOT_FOUND = unchecked((int)0x887E0001);
        public const int D3D12_ERROR_DRIVER_VERSION_MISMATCH = unchecked((int)0x887E0002);
        public const int D3D12_ERROR_INVALID_REDIST = unchecked((int)0x887E0003);

        /// <summary>Optional: set to () => device.GetDeviceRemovedReason() so device-lost exceptions carry the reason.</summary>
        public static Func<int> DeviceRemovedReason;

        static readonly Dictionary<int, string> names = new Dictionary<int, string>();

        static Hr()
        {
            foreach (System.Reflection.FieldInfo f in typeof(Hr).GetFields())
                if (f.IsLiteral && f.FieldType == typeof(int))
                {
                    int v = (int)f.GetRawConstantValue();
                    if (!names.ContainsKey(v)) names[v] = f.Name;
                }
        }

        /// <summary>Throws HrException("what failed: 0x887A0005 (DXGI_ERROR_DEVICE_REMOVED)") when hr &lt; 0.</summary>
        public static void Check(int hr, string what)
        {
            if (hr < 0) throw new HrException(hr, what);
        }

        public static bool Failed(int hr) { return hr < 0; }
        public static bool Succeeded(int hr) { return hr >= 0; }

        public static bool IsDeviceLost(int hr)
        {
            return hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_HUNG || hr == DXGI_ERROR_DEVICE_RESET
                || hr == DXGI_ERROR_DRIVER_INTERNAL_ERROR;
        }

        public static string Name(int hr)
        {
            string n;
            return names.TryGetValue(hr, out n) ? n : null;
        }

        /// <summary>"0x887A0005 (DXGI_ERROR_DEVICE_REMOVED)" or just "0x80070057".</summary>
        public static string Format(int hr)
        {
            string n = Name(hr);
            string hex = "0x" + hr.ToString("X8");
            return n == null ? hex : hex + " (" + n + ")";
        }
    }

    /// <summary>COM helpers for RCWs (runtime callable wrappers) of native D3D12/DXGI objects.</summary>
    public static class Com
    {
        /// <summary>
        /// Raw interface pointer of an RCW for interface type <paramref name="iface"/> WITHOUT keeping a reference
        /// (QI + immediate Release). The pointer stays valid as long as the RCW is alive (not released / collected):
        /// keep the managed object referenced. Use it for COM pointers inside structs (barriers, copy locations, PSO
        /// descs), for ID3D12CommandList* / ID3D12DescriptorHeap* arrays, and for Streamline resource tags.
        /// </summary>
        public static IntPtr BorrowPtr(object rcw, Type iface)
        {
            if (rcw == null) return IntPtr.Zero;
            IntPtr p = Marshal.GetComInterfaceForObject(rcw, iface);
            Marshal.Release(p);
            return p;
        }

        /// <summary>Raw pointer WITH a reference (caller must Marshal.Release it).</summary>
        public static IntPtr AddRefPtr(object rcw, Type iface)
        {
            return rcw == null ? IntPtr.Zero : Marshal.GetComInterfaceForObject(rcw, iface);
        }

        /// <summary>Wraps a raw interface pointer (the caller keeps its own reference) and casts it.</summary>
        public static T FromPtr<T>(IntPtr p) where T : class
        {
            return p == IntPtr.Zero ? null : (T)Marshal.GetObjectForIUnknown(p);
        }

        /// <summary>
        /// Releases one RCW reference (Marshal.ReleaseComObject) and nulls the variable. Casting an RCW to another
        /// interface returns the SAME RCW, so release only once per object obtained from native code. Deliberately not
        /// FinalReleaseComObject: e.g. GetDevice() hands back the same RCW as the main device variable.
        /// </summary>
        public static void Release<T>(ref T obj) where T : class
        {
            if (obj != null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj);
            obj = null;
        }
    }
}
