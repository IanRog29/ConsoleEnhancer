// DirectShow.cs - interfaces COM de DirectShow para la deteccion de capturadoras (ConsoleEnhancer)
//
// Copiadas del motor (src\Motor\Capture\DsCapture.cs: vtables completas, sin herencia de interfaces de C#), solo las que hacen
// falta: enumerar dispositivos (sin abrirlos: se lee su "property bag", que esta en el registro) y, si se pide, los formatos de su pin de
// video (eso SI abre el dispositivo: BindToObject crea el filtro del driver).
using System;
using System.Runtime.InteropServices;

namespace ConsoleEnhancer.Deteccion
{
    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDsCreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator([In] ref Guid clsidDeviceClass, out IntPtr ppEnumMoniker, int dwFlags);
    }

    [ComImport, Guid("00000102-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDsEnumMonikers
    {
        [PreserveSig] int Next(uint celt, out IntPtr rgelt, out uint pceltFetched);
        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IntPtr ppenum);
    }

    [ComImport, Guid("0000000f-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDsMonikerX
    {
        [PreserveSig] int GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load(IntPtr pStm);
        [PreserveSig] int Save(IntPtr pStm, int fClearDirty);
        [PreserveSig] int GetSizeMax(out long pcbSize);
        [PreserveSig] int BindToObject(IntPtr pbc, IntPtr pmkToLeft, [In] ref Guid riidResult, out IntPtr ppvResult);
        [PreserveSig] int BindToStorage(IntPtr pbc, IntPtr pmkToLeft, [In] ref Guid riid, out IntPtr ppvObj);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDsPropertyBag
    {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar, IntPtr pErrorLog);
        [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar);
    }

    [ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDsBaseFilter
    {
        [PreserveSig] int GetClassID(out Guid pClassID);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long tStart);
        [PreserveSig] int GetState(int dwMilliSecsTimeout, out int state);
        [PreserveSig] int SetSyncSource(IntPtr pClock);
        [PreserveSig] int GetSyncSource(out IntPtr pClock);
        [PreserveSig] int EnumPins(out IntPtr ppEnum);
    }

    [ComImport, Guid("56a868a9-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDsGraphBuilder
    {
        [PreserveSig] int AddFilter([MarshalAs(UnmanagedType.Interface)] IDsBaseFilter pFilter, [MarshalAs(UnmanagedType.LPWStr)] string pName);
        [PreserveSig] int RemoveFilter([MarshalAs(UnmanagedType.Interface)] IDsBaseFilter pFilter);
    }

    [ComImport, Guid("56a86892-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDsEnumPins
    {
        [PreserveSig] int Next(uint cPins, out IntPtr ppPins, out uint pcFetched);
    }

    [ComImport, Guid("56a86891-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDsPin
    {
        [PreserveSig] int Connect(IntPtr pReceivePin, IntPtr pmt);
        [PreserveSig] int ReceiveConnection(IntPtr pConnector, IntPtr pmt);
        [PreserveSig] int Disconnect();
        [PreserveSig] int ConnectedTo(out IntPtr pPin);
        [PreserveSig] int ConnectionMediaType(IntPtr pmt);
        [PreserveSig] int QueryPinInfo(IntPtr pInfo);     // PIN_INFO: IBaseFilter* (8) + PIN_DIRECTION (4) + WCHAR[128]
        [PreserveSig] int QueryDirection(out int pPinDir);
    }

    [ComImport, Guid("C6E13340-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDsStreamConfig
    {
        [PreserveSig] int SetFormat(IntPtr pmt);
        [PreserveSig] int GetFormat(out IntPtr ppmt);
        [PreserveSig] int GetNumberOfCapabilities(out int piCount, out int piSize);
        [PreserveSig] int GetStreamCaps(int iIndex, out IntPtr ppmt, IntPtr pSCC);
    }

    // 88 bytes en x64
    [StructLayout(LayoutKind.Sequential)]
    struct DsMediaType
    {
        public Guid majortype;
        public Guid subtype;
        public int bFixedSizeSamples;
        public int bTemporalCompression;
        public uint lSampleSize;
        public Guid formattype;
        public IntPtr pUnk;
        public uint cbFormat;
        public IntPtr pbFormat;
    }

    static class Ds
    {
        public static readonly Guid CLSID_SystemDeviceEnum = new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
        public static readonly Guid CLSID_VideoInputDeviceCategory = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");
        public static readonly Guid CLSID_AudioInputDeviceCategory = new Guid("33D9A762-90C8-11d0-BD43-00A0C911CE86");
        public static readonly Guid CLSID_FilterGraph = new Guid("E436EBB3-524F-11CE-9F53-0020AF0BA770");
        public static readonly Guid IID_IBaseFilter = new Guid("56a86895-0ad4-11ce-b03a-0020af0ba770");
        public static readonly Guid IID_IPropertyBag = new Guid("55272A00-42CB-11CE-8135-00AA004BB851");
        public static readonly Guid MEDIATYPE_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid MEDIATYPE_Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        public static readonly Guid FORMAT_VideoInfo = new Guid("05589f80-c356-11ce-bf01-00aa0055595a");
        public static readonly Guid FORMAT_VideoInfo2 = new Guid("f72a76a0-eb0a-11d0-ace4-0000c0cc16ba");
        static readonly Guid RGB24 = new Guid("e436eb7d-524f-11ce-9f53-0020af0ba770"), RGB32 = new Guid("e436eb7e-524f-11ce-9f53-0020af0ba770"),
                             RGB565 = new Guid("e436eb7b-524f-11ce-9f53-0020af0ba770"), RGB555 = new Guid("e436eb7c-524f-11ce-9f53-0020af0ba770"),
                             ARGB32 = new Guid("773c9ac0-3274-11d0-b724-00aa006c1a01");

        public static object CoCreate(Guid clsid) { return Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, true)); }

        public static void Release(object o) { if (o != null && Marshal.IsComObject(o)) { try { Marshal.ReleaseComObject(o); } catch { } } }

        public static string ReadBag(IDsPropertyBag bag, string name)
        {
            object v = null;
            try { return bag.Read(name, ref v, IntPtr.Zero) >= 0 && v != null ? v.ToString() : null; } catch { return null; }
        }

        public static void DeleteMediaType(IntPtr pmt)
        {
            if (pmt == IntPtr.Zero) return;
            DsMediaType mt = (DsMediaType)Marshal.PtrToStructure(pmt, typeof(DsMediaType));
            if (mt.cbFormat != 0 && mt.pbFormat != IntPtr.Zero) Nat.CoTaskMemFree(mt.pbFormat);
            if (mt.pUnk != IntPtr.Zero) Marshal.Release(mt.pUnk);
            Nat.CoTaskMemFree(pmt);
        }

        // Nombre del subtipo: RGB24, YUY2, NV12, MJPG, H264...
        public static string Subtipo(Guid g, out bool comprimido)
        {
            comprimido = false;
            if (g == RGB24) return "RGB24";
            if (g == RGB32) return "RGB32";
            if (g == ARGB32) return "ARGB32";
            if (g == RGB565) return "RGB565";
            if (g == RGB555) return "RGB555";
            string s = g.ToString();
            if (s.EndsWith("-0000-0010-8000-00aa00389b71", StringComparison.OrdinalIgnoreCase))
            {
                byte[] b = g.ToByteArray();
                string cc = new string(new char[] { (char)b[0], (char)b[1], (char)b[2], (char)b[3] }).Trim('\0', ' ').ToUpperInvariant();
                comprimido = cc == "MJPG" || cc == "H264" || cc == "HEVC" || cc == "H265" || cc == "AVC1" || cc == "JPEG";
                return cc;
            }
            comprimido = true;
            return g.ToString("B");
        }
    }
}
