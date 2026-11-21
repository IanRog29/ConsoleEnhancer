// MfCapture.cs - Media Foundation video capture (AVerMedia GC573) for Switch2Engine.
//
// One dedicated MTA thread owns the device. The source reader runs in ASYNC mode: its callback (an MF work-queue
// thread) only AddRefs the sample, stamps Stopwatch.GetTimestamp() and wakes the capture thread, which issues the
// next ReadSample at once and then hands the locked buffer to OnFrame. The capture thread waits with timeouts, so
// "no signal" (no samples) never blocks it, and Stop() never depends on the driver delivering a frame.
// A NATIVE media type is selected (no MF converter, MF_READWRITE_DISABLE_CONVERTERS), so the GPU unpacks the format.
//
// C# 5 / .NET Framework 4.8. Compile with /unsafe /platform:x64.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor.Native
{
    // ---------------------------------------------------------------------------------------------------------------
    // HRESULT helper (named MfHr so it never collides with the engine-wide Hr class)
    // ---------------------------------------------------------------------------------------------------------------
    public static class MfHr
    {
        public static void Check(int hr, string what)
        {
            if (hr < 0) throw new ConsoleEnhancer.Motor.Capture.CaptureException(what + " failed: " + Describe(hr), hr);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryExW(string name, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int FormatMessageW(uint flags, IntPtr source, int msgId, int langId, StringBuilder buf, int size, IntPtr args);

        static IntPtr mferror = IntPtr.Zero;
        static bool mferrorTried;

        // "0xC00D36B9 (No more types)" - looks the text up in mferror.dll (MF codes) and the system table.
        public static string Describe(int hr)
        {
            string hex = "0x" + hr.ToString("X8", CultureInfo.InvariantCulture);
            try
            {
                const uint FROM_HMODULE = 0x800, FROM_SYSTEM = 0x1000, IGNORE_INSERTS = 0x200;
                if (!mferrorTried) { mferrorTried = true; mferror = LoadLibraryExW("mferror.dll", IntPtr.Zero, 0x2 /*AS_DATAFILE*/); }
                StringBuilder sb = new StringBuilder(512);
                int n = 0;
                if (mferror != IntPtr.Zero)
                    n = FormatMessageW(FROM_HMODULE | IGNORE_INSERTS, mferror, hr, 0, sb, sb.Capacity, IntPtr.Zero);
                if (n == 0) n = FormatMessageW(FROM_SYSTEM | IGNORE_INSERTS, IntPtr.Zero, hr, 0, sb, sb.Capacity, IntPtr.Zero);
                if (n > 0) return hex + " (" + sb.ToString().Trim() + ")";
            }
            catch { }
            return hex;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Media Foundation COM interfaces. No C# interface inheritance: every interface redeclares its parents' methods in
    // vtable order (IMFAttributes = 30 methods after IUnknown).
    // ---------------------------------------------------------------------------------------------------------------
    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFAttributes
    {
        [PreserveSig] int GetItem([In] ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType([In] ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem([In] ref Guid guidKey, IntPtr value, out int pbResult);
        [PreserveSig] int Compare([MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, int matchType, out int pbResult);
        [PreserveSig] int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString([In] ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString([In] ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem([In] ref Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem([In] ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32([In] ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64([In] ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble([In] ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig] int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems([MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);
    }

    [ComImport, Guid("7FEE9E9A-4A89-47a6-899C-B6A53A70FB67"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFActivate
    {
        // IMFAttributes
        [PreserveSig] int GetItem([In] ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType([In] ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem([In] ref Guid guidKey, IntPtr value, out int pbResult);
        [PreserveSig] int Compare([MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, int matchType, out int pbResult);
        [PreserveSig] int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString([In] ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString([In] ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem([In] ref Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem([In] ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32([In] ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64([In] ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble([In] ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig] int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems([MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);
        // IMFActivate
        [PreserveSig] int ActivateObject([In] ref Guid riid, out IntPtr ppv);
        [PreserveSig] int ShutdownObject();
        [PreserveSig] int DetachObject();
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaType
    {
        // IMFAttributes
        [PreserveSig] int GetItem([In] ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType([In] ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem([In] ref Guid guidKey, IntPtr value, out int pbResult);
        [PreserveSig] int Compare([MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, int matchType, out int pbResult);
        [PreserveSig] int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString([In] ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString([In] ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem([In] ref Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem([In] ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32([In] ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64([In] ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble([In] ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig] int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems([MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);
        // IMFMediaType
        [PreserveSig] int GetMajorType(out Guid pguidMajorType);
        [PreserveSig] int IsCompressedFormat(out int pfCompressed);
        [PreserveSig] int IsEqual([MarshalAs(UnmanagedType.Interface)] IMFMediaType pIMediaType, out uint pdwFlags);
        [PreserveSig] int GetRepresentation(Guid guidRepresentation, out IntPtr ppvRepresentation);
        [PreserveSig] int FreeRepresentation(Guid guidRepresentation, IntPtr pvRepresentation);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out uint pcbCurrentLength);
        [PreserveSig] int SetCurrentLength(uint cbCurrentLength);
        [PreserveSig] int GetMaxLength(out uint pcbMaxLength);
    }

    [ComImport, Guid("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMF2DBuffer
    {
        [PreserveSig] int Lock2D(out IntPtr ppbScanline0, out int plPitch);
        [PreserveSig] int Unlock2D();
        [PreserveSig] int GetScanline0AndPitch(out IntPtr pbScanline0, out int plPitch);
        [PreserveSig] int IsContiguousFormat(out int pfIsContiguous);
        [PreserveSig] int GetContiguousLength(out uint pcbLength);
        [PreserveSig] int ContiguousCopyTo(IntPtr pbDestBuffer, uint cbDestBuffer);
        [PreserveSig] int ContiguousCopyFrom(IntPtr pbSrcBuffer, uint cbSrcBuffer);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSample
    {
        // IMFAttributes
        [PreserveSig] int GetItem([In] ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType([In] ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem([In] ref Guid guidKey, IntPtr value, out int pbResult);
        [PreserveSig] int Compare([MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, int matchType, out int pbResult);
        [PreserveSig] int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString([In] ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString([In] ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem([In] ref Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem([In] ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32([In] ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64([In] ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble([In] ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig] int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems([MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);
        // IMFSample
        [PreserveSig] int GetSampleFlags(out uint pdwSampleFlags);
        [PreserveSig] int SetSampleFlags(uint dwSampleFlags);
        [PreserveSig] int GetSampleTime(out long phnsSampleTime);
        [PreserveSig] int SetSampleTime(long hnsSampleTime);
        [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
        [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
        [PreserveSig] int GetBufferCount(out uint pdwBufferCount);
        [PreserveSig] int GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        [PreserveSig] int AddBuffer([MarshalAs(UnmanagedType.Interface)] IMFMediaBuffer pBuffer);
        [PreserveSig] int RemoveBufferByIndex(uint dwIndex);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out uint pcbTotalLength);
        [PreserveSig] int CopyToBuffer([MarshalAs(UnmanagedType.Interface)] IMFMediaBuffer pBuffer);
    }

    // IMFMediaEventGenerator (4) + IMFMediaSource (6). Only Shutdown is used.
    [ComImport, Guid("279a808d-aec7-40c8-9c6b-a6b492c78a66"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaSource
    {
        [PreserveSig] int GetEvent(uint dwFlags, out IntPtr ppEvent);
        [PreserveSig] int BeginGetEvent(IntPtr pCallback, IntPtr punkState);
        [PreserveSig] int EndGetEvent(IntPtr pResult, out IntPtr ppEvent);
        [PreserveSig] int QueueEvent(int met, [In] ref Guid guidExtendedType, int hrStatus, IntPtr pvValue);
        [PreserveSig] int GetCharacteristics(out uint pdwCharacteristics);
        [PreserveSig] int CreatePresentationDescriptor(out IntPtr ppPresentationDescriptor);
        [PreserveSig] int Start(IntPtr pPresentationDescriptor, IntPtr pguidTimeFormat, IntPtr pvarStartPosition);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Shutdown();
    }

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint dwStreamIndex, out int pfSelected);
        [PreserveSig] int SetStreamSelection(uint dwStreamIndex, int fSelected);
        [PreserveSig] int GetNativeMediaType(uint dwStreamIndex, uint dwMediaTypeIndex, out IMFMediaType ppMediaType);
        [PreserveSig] int GetCurrentMediaType(uint dwStreamIndex, out IMFMediaType ppMediaType);
        [PreserveSig] int SetCurrentMediaType(uint dwStreamIndex, IntPtr pdwReserved, [MarshalAs(UnmanagedType.Interface)] IMFMediaType pMediaType);
        [PreserveSig] int SetCurrentPosition([In] ref Guid guidTimeFormat, IntPtr varPosition);
        // Async mode: the four out pointers must be NULL (IntPtr.Zero)
        [PreserveSig] int ReadSample(uint dwStreamIndex, uint dwControlFlags, IntPtr pdwActualStreamIndex, IntPtr pdwStreamFlags, IntPtr pllTimestamp, IntPtr ppSample);
        [PreserveSig] int Flush(uint dwStreamIndex);
        [PreserveSig] int GetServiceForStream(uint dwStreamIndex, [In] ref Guid guidService, [In] ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int GetPresentationAttribute(uint dwStreamIndex, [In] ref Guid guidAttribute, IntPtr pvarAttribute);
    }

    // Implemented by managed code (CCW). Pointers are raw IntPtr so no RCW is created behind our back.
    [ComImport, Guid("deec8d99-fa1d-4d82-84c2-2c8969944867"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSourceReaderCallback
    {
        [PreserveSig] int OnReadSample(int hrStatus, uint dwStreamIndex, uint dwStreamFlags, long llTimestamp, IntPtr pSample);
        [PreserveSig] int OnFlush(uint dwStreamIndex);
        [PreserveSig] int OnEvent(uint dwStreamIndex, IntPtr pEvent);
    }

    public static class MfApi
    {
        public const uint MF_VERSION = 0x00020070;          // MF_SDK_VERSION 2 << 16 | MF_API_VERSION 0x70
        public const uint MFSTARTUP_LITE = 1;               // no sockets
        public const uint FIRST_VIDEO_STREAM = 0xFFFFFFFC;  // MF_SOURCE_READER_FIRST_VIDEO_STREAM
        public const uint ALL_STREAMS = 0xFFFFFFFE;         // MF_SOURCE_READER_ALL_STREAMS
        public const int MF_E_NO_MORE_TYPES = unchecked((int)0xC00D36B9);
        public const int MF_E_INVALIDSTREAMNUMBER = unchecked((int)0xC00D36B3);
        public const int MF_E_SHUTDOWN = unchecked((int)0xC00D3E85);
        public const int MF_E_ATTRIBUTENOTFOUND = unchecked((int)0xC00D36E6);

        // MF_SOURCE_READER_FLAG
        public const uint READERF_ERROR = 0x1;
        public const uint READERF_ENDOFSTREAM = 0x2;
        public const uint READERF_NEWSTREAM = 0x4;
        public const uint READERF_NATIVEMEDIATYPECHANGED = 0x10;
        public const uint READERF_CURRENTMEDIATYPECHANGED = 0x20;
        public const uint READERF_STREAMTICK = 0x100;
        public const uint READERF_ALLEFFECTSREMOVED = 0x200;

        [DllImport("mfplat.dll")] public static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll")] public static extern int MFShutdown();
        [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, uint cInitialSize);
        [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType ppMFType);
        [DllImport("mfplat.dll")] public static extern long MFGetSystemTime();
        [DllImport("mf.dll")] public static extern int MFEnumDeviceSources([MarshalAs(UnmanagedType.Interface)] IMFAttributes pAttributes, out IntPtr pppSourceActivate, out uint pcSourceActivate);
        [DllImport("mfreadwrite.dll")] public static extern int MFCreateSourceReaderFromMediaSource(IntPtr pMediaSource, [MarshalAs(UnmanagedType.Interface)] IMFAttributes pAttributes, out IMFSourceReader ppSourceReader);
        [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr pv);
        [DllImport("avrt.dll", CharSet = CharSet.Unicode)] public static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);
        [DllImport("avrt.dll")] public static extern int AvRevertMmThreadCharacteristics(IntPtr avrtHandle);

        // Attribute helpers (any MF object QIs to IMFAttributes)
        public static string GetString(IMFAttributes a, Guid key)
        {
            IntPtr s; uint len;
            if (a.GetAllocatedString(ref key, out s, out len) < 0 || s == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(s, (int)len); }
            finally { CoTaskMemFree(s); }
        }
        public static bool GetU32(IMFAttributes a, Guid key, out uint v) { return a.GetUINT32(ref key, out v) >= 0; }
        public static bool GetU64(IMFAttributes a, Guid key, out ulong v) { return a.GetUINT64(ref key, out v) >= 0; }
        public static bool GetGuid(IMFAttributes a, Guid key, out Guid v) { return a.GetGUID(ref key, out v) >= 0; }
        public static void SetU32(IMFAttributes a, Guid key, uint v, string what) { MfHr.Check(a.SetUINT32(ref key, v), what); }
        public static void SetGuid(IMFAttributes a, Guid key, Guid v, string what) { MfHr.Check(a.SetGUID(ref key, ref v), what); }

        // Drops ONE reference this code acquired (one per GetObjectForIUnknown / out-parameter / CoCreate). Not
        // FinalReleaseComObject: COM objects that are the same identity share one RCW (a device moniker IS its
        // property bag), and a final release would kill the other holder's RCW too.
        public static void Release(object o)
        {
            if (o != null && Marshal.IsComObject(o)) { try { Marshal.ReleaseComObject(o); } catch { } }
        }
    }

    public static class MfGuids
    {
        public static readonly Guid IID_IMFMediaSource = new Guid("279a808d-aec7-40c8-9c6b-a6b492c78a66");
        public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE = new Guid("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
        public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID = new Guid("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
        public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME = new Guid("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
        public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK = new Guid("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");
        public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_CATEGORY = new Guid("77f0ae69-c3bd-4509-941d-467e4d24899e");
        public static readonly Guid KSCATEGORY_VIDEO_CAMERA = new Guid("e5323777-f976-4f5b-9b55-b94699c46e44");
        public static readonly Guid KSCATEGORY_VIDEO = new Guid("6994ad05-93ef-11d0-a3cc-00a0c9223196");
        public static readonly Guid KSCATEGORY_CAPTURE = new Guid("65e8773d-8f56-11d0-a3b9-00a0c9223196");

        public static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid MF_MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid MF_MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid MF_MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid MF_MT_SAMPLE_SIZE = new Guid("dad3ab78-1990-408b-bce2-eba673dacc10");
        public static readonly Guid MF_MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00aa00389b71");

        public static readonly Guid MF_LOW_LATENCY = new Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee");
        public static readonly Guid MF_READWRITE_DISABLE_CONVERTERS = new Guid("98d5b065-1374-4847-8d5d-31520fee7156");
        public static readonly Guid MF_SOURCE_READER_ASYNC_CALLBACK = new Guid("1e3dbeac-bb43-4c35-b507-cd644464c965");
        public static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new Guid("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");

        public static readonly Guid MFSampleExtension_DeviceTimestamp = new Guid("8f3e35e7-2dcd-4887-8622-2a58baa652b0");
        public static readonly Guid MFSampleExtension_DeviceReferenceSystemTime = new Guid("6523775a-ba2d-405f-b2c5-01ff88e2e8f6");
        public static readonly Guid MFSampleExtension_Discontinuity = new Guid("9cdf01d9-a0f0-43ba-b077-eaa06cbd728a");

        // FourCC / D3DFORMAT based video subtype: XXXXXXXX-0000-0010-8000-00AA00389B71
        public static Guid FromFourCC(uint fcc) { return new Guid(fcc, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71); }
        public static uint FourCC(string s) { return (uint)s[0] | ((uint)s[1] << 8) | ((uint)s[2] << 16) | ((uint)s[3] << 24); }
        public static bool IsFourCCGuid(Guid g, out uint fcc)
        {
            byte[] b = g.ToByteArray();
            fcc = BitConverter.ToUInt32(b, 0);
            return b[4] == 0 && b[5] == 0 && b[6] == 0x10 && b[7] == 0 && b[8] == 0x80 && b[9] == 0 && b[10] == 0 &&
                   b[11] == 0xAA && b[12] == 0 && b[13] == 0x38 && b[14] == 0x9B && b[15] == 0x71;
        }
    }
}

namespace ConsoleEnhancer.Motor.Capture
{
    public sealed class CaptureException : Exception
    {
        public CaptureException(string message, int hr) : base(message) { HResult = hr; }
        public CaptureException(string message, int hr, Exception inner) : base(message, inner) { HResult = hr; }
        public CaptureException(string message) : base(message) { }
    }

    // Pixel formats a capture source may offer. Bytes per row / planes: see MfCapture.RowBytes / MfCapture.PlaneHeightFactor.
    public enum CaptureFormat
    {
        Unknown = 0,
        RGB24,        // B,G,R bytes (D3DFMT_R8G8B8); often bottom-up (negative pitch)
        RGB32,        // B,G,R,X
        ARGB32,       // B,G,R,A
        A2R10G10B10,
        RGB565,
        RGB555,
        YUY2,         // Y0 U Y1 V
        UYVY,         // U Y0 V Y1
        YVYU,
        NV12,         // Y plane, then interleaved UV plane (height/2) at data + pitch*height
        NV21,
        P010,         // like NV12, 16-bit little-endian words, 10 significant bits in the high bits
        P016,
        P210,         // 4:2:2 P010 (UV plane full height)
        Y210,
        Y216,
        V210,         // 10-bit 4:2:2 packed, 128 bytes per 48 pixels
        V410,
        Y410,
        AYUV,
        I420,
        IYUV,
        YV12,
        MJPG,
        H264,
        HEVC,
        L8
    }

    public enum CaptureEvent
    {
        Opening,        // detail: device + requested mode
        Opened,         // detail: selected native type
        FirstFrame,     // first sample after (re)open
        NoSignal,       // no sample for NoSignalMs
        SignalRestored, // samples again after NoSignal
        StreamTick,     // MF_SOURCE_READERF_STREAMTICK (gap announced by the source)
        Gap,            // timestamp jump > 1.5 frame durations (detail: estimated frames lost)
        FormatChanged,  // NATIVE/CURRENTMEDIATYPECHANGED (detail: new type)
        EndOfStream,
        Error,          // detail: what + HRESULT text; the device is reopened
        Reopening,
        Stopped
    }

    public enum CaptureState { Stopped, Opening, WaitingForFirstFrame, Running, NoSignal, Reopening }

    // data   : first byte of the TOP row (scanline 0).
    // pitch  : bytes from one row to the next row BELOW it. NEGATIVE = bottom-up image in memory (lowest address is
    //          data + pitch*(height-1)). Planar formats (NV12/P010): the chroma plane starts at data + pitch*height.
    // qpcArrival     : Stopwatch.GetTimestamp() when the source reader delivered the sample (OnReadSample entry).
    // sampleTime100ns: MF sample time (100 ns units).
    // sequence       : 0,1,2... frames delivered by this MfCapture instance (keeps counting across reopens).
    // The pointer is valid ONLY during the callback. Runs on the capture thread (MTA).
    public delegate void FrameCallback(IntPtr data, int pitch, int width, int height, CaptureFormat format,
                                       long qpcArrival, long sampleTime100ns, long sequence);

    public delegate void CaptureEventCallback(CaptureEvent ev, string detail);

    // Common surface of MfCapture (Media Foundation) and DsCapture (DirectShow, DsCapture.cs).
    public interface ICaptureSource : IDisposable
    {
        FrameCallback OnFrame { get; set; }               // set before Start
        CaptureEventCallback OnEvent { get; set; }        // set before Start
        CaptureOptions Options { get; }
        bool IsRunning { get; }
        CaptureState State { get; }
        CaptureMediaType CurrentType { get; }             // null until opened
        CaptureDeviceInfo CurrentDevice { get; }
        FrameExtra CurrentFrame { get; }                  // valid inside OnFrame
        CaptureStats GetStats();
        void Start();                                     // returns at once; the device opens on the capture thread
        bool Stop();                                      // releases the device; false = thread stuck past StopTimeoutMs
    }

    public sealed class CaptureDeviceInfo
    {
        public string FriendlyName;
        public string SymbolicLink;
        public Guid Category;      // Guid.Empty = MF default enumeration
        public override string ToString() { return FriendlyName + "  [" + SymbolicLink + "]"; }
    }

    public sealed class CaptureMediaType
    {
        public int Stream;
        public int Index;
        public Guid Subtype;
        public CaptureFormat Format;
        public string SubtypeName;
        public int Width, Height;
        public int FpsNum, FpsDen;
        public bool HasDefaultStride;
        public int DefaultStride;
        public int SampleSize;
        public bool Compressed;
        public double FpsMin, FpsMax;     // frame-rate range the type accepts (DirectShow caps); MF: both = Fps
        public string FormatType = "";    // DirectShow: "VIH" / "VIH2"
        public double Fps { get { return FpsDen == 0 ? 0 : (double)FpsNum / FpsDen; } }
        public override string ToString()
        {
            string s = SubtypeName + " " + Width + "x" + Height + " @ " + FpsNum + "/" + FpsDen + " (" +
                   Fps.ToString("0.###", CultureInfo.InvariantCulture) + ") stride " +
                   (HasDefaultStride ? DefaultStride.ToString(CultureInfo.InvariantCulture) : "n/a");
            if (FpsMax > 0 && Math.Abs(FpsMax - FpsMin) > 0.01)
                s += " range " + FpsMin.ToString("0.###", CultureInfo.InvariantCulture) + "-" + FpsMax.ToString("0.###", CultureInfo.InvariantCulture);
            return s;
        }
    }

    public sealed class CaptureOptions
    {
        public string DeviceName = "GC573";           // case-insensitive substring of the friendly name
        public string SymbolicLink = null;            // exact symbolic link wins over the name if set
        public int Width = 2560, Height = 1440;
        public int FpsNum = 60000, FpsDen = 1001;
        public CaptureFormat[] Preference = new CaptureFormat[] { CaptureFormat.RGB24, CaptureFormat.RGB32, CaptureFormat.YUY2, CaptureFormat.NV12 };
        public bool LowLatency = true;                // MF_LOW_LATENCY
        public int NoSignalMs = 1000;                 // no sample for this long -> NoSignal event
        public int ReopenOnSilenceMs = 5000;          // and reopen the device after this much silence (0 = never);
        public int ReopenOnSilenceMaxMs = 30000;      // the interval doubles per silent reopen up to this
        public int ReopenBackoffMinMs = 250, ReopenBackoffMaxMs = 5000;   // after errors
        public bool UseMmcss = true;                  // "Capture" MMCSS task for the capture thread
        public int StopTimeoutMs = 3000;
        // DirectShow only: call OnFrame directly on the driver's streaming thread (saves the thread hop, see
        // FrameExtra.DeliverQpc - qpcArrival). Default false = the dedicated MTA capture thread.
        public bool DeliverOnSourceThread = false;
        public CaptureOptions Clone() { CaptureOptions c = (CaptureOptions)MemberwiseClone(); c.Preference = (CaptureFormat[])Preference.Clone(); return c; }
    }

    public sealed class CaptureStats
    {
        public long Frames;               // delivered to OnFrame
        public long StreamTicks;
        public long Gaps;                 // timestamp jumps > 1.5 frame durations
        public long EstimatedLost;        // sum over gaps of (round(delta/frameDuration) - 1)
        public long DuplicateTimestamps;  // delta <= 0
        public long Discontinuities;      // MFSampleExtension_Discontinuity set
        public long LateDropped;          // more than one sample queued -> older ones skipped
        public long EmptySamples;         // zero-length samples (GC573: when another graph on the same pin stops)
        public long Errors;
        public long FormatChanges;
        public long Opens;
        public long NoSignalEvents;
        public long CallbackExceptions;
        public long DriverDropped = -1;   // DirectShow IAMDroppedFrames (-1 = not available)
        public long DriverNotDropped = -1;
        public long LastArrivalQpc;
        public CaptureState State;
        public CaptureStats Clone() { return (CaptureStats)MemberwiseClone(); }
    }

    // Extra facts about the frame being delivered (read it inside OnFrame through MfCapture.CurrentFrame).
    public struct FrameExtra
    {
        public long DeviceTimestamp100ns;        // MFSampleExtension_DeviceTimestamp (QPC domain, 100 ns), 0 if absent
        public long DeviceReferenceSystemTime;   // MFSampleExtension_DeviceReferenceSystemTime, 0 if absent
        public long SampleDuration100ns;
        public long BufferLength;                // bytes of the locked buffer (0 if unknown via Lock2D)
        public bool UsedLock2D;
        public bool Discontinuity;
        public long DeliverQpc;                  // Stopwatch ticks just before OnFrame (handoff cost = this - qpcArrival)
        // DirectShow: sample start time mapped to the QPC clock (100 ns) = graph tStart + sample time + clock->QPC
        // offset, and arrival - that = ClockDelay100ns. HasClockDelay false if the graph has no clock / tStart.
        public bool HasClockDelay;
        public long SampleTimeQpc100ns;
        public long ClockDelay100ns;
    }

    [ComVisible(true)]
    public sealed class MfReaderCallback : IMFSourceReaderCallback
    {
        readonly MfCapture owner;
        readonly int gen;
        internal MfReaderCallback(MfCapture owner, int gen) { this.owner = owner; this.gen = gen; }

        public int OnReadSample(int hrStatus, uint dwStreamIndex, uint dwStreamFlags, long llTimestamp, IntPtr pSample)
        {
            long qpc = Stopwatch.GetTimestamp();
            try { owner.OnSampleArrived(gen, hrStatus, dwStreamFlags, llTimestamp, pSample, qpc); } catch { }
            return 0;
        }
        public int OnFlush(uint dwStreamIndex) { try { owner.OnFlushDone(); } catch { } return 0; }
        public int OnEvent(uint dwStreamIndex, IntPtr pEvent) { return 0; }
    }

    // NOTE (2026-11-15): on this machine Media Foundation enumerates the GC573 but cannot create its media source
    // (MF_E_INVALIDMEDIATYPE 0xC00D36B4 from ActivateObject / MFCreateDeviceSource on both its KSCATEGORY_CAPTURE and
    // KSCATEGORY_VIDEO interfaces; it has no KSCATEGORY_VIDEO_CAMERA interface, so no Frame Server path either).
    // Use DsCapture (DirectShow) for the GC573; MfCapture stays for other devices / future drivers.
    public sealed class MfCapture : ICaptureSource
    {
        // ---- public surface ----
        FrameCallback onFrame;
        CaptureEventCallback onEvent;
        public FrameCallback OnFrame { get { return onFrame; } set { onFrame = value; } }            // set before Start
        public CaptureEventCallback OnEvent { get { return onEvent; } set { onEvent = value; } }     // capture thread

        public MfCapture(CaptureOptions options) { opt = (options ?? new CaptureOptions()).Clone(); }

        public CaptureOptions Options { get { return opt; } }
        public bool IsRunning { get { return thread != null && thread.IsAlive; } }
        public CaptureState State { get { lock (statLock) return stats.State; } }
        public CaptureMediaType CurrentType { get { return cur; } }      // null until opened
        public CaptureDeviceInfo CurrentDevice { get { return curDev; } }
        public FrameExtra CurrentFrame { get { return extra; } }          // valid inside OnFrame
        public CaptureStats GetStats() { lock (statLock) return stats.Clone(); }

        public void Start()
        {
            if (thread != null) throw new InvalidOperationException("MfCapture already started");
            stopEvent.Reset();
            thread = new Thread(ThreadMain);
            thread.Name = "MfCapture";
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.Highest;
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        // Releases the device. Returns false if the capture thread did not finish within the timeout (driver stuck).
        public bool Stop()
        {
            Thread t = thread;
            if (t == null) return true;
            stopEvent.Set();
            bool ok = t.Join(opt.StopTimeoutMs);
            if (ok) thread = null;
            return ok;
        }

        public void Dispose() { Stop(); }

        // ---- static helpers (each runs on its own MTA thread with a timeout, so they are safe from STA/UI threads) ----

        // MF video capture devices. allCategories also enumerates KSCATEGORY_VIDEO and KSCATEGORY_CAPTURE.
        public static List<CaptureDeviceInfo> ListDevices(bool allCategories = false, int timeoutMs = 8000)
        {
            return RunMta<List<CaptureDeviceInfo>>(delegate
            {
                MfHr.Check(MfApi.MFStartup(MfApi.MF_VERSION, MfApi.MFSTARTUP_LITE), "MFStartup");
                try
                {
                    List<CaptureDeviceInfo> all = new List<CaptureDeviceInfo>();
                    Guid[] cats = allCategories
                        ? new Guid[] { Guid.Empty, MfGuids.KSCATEGORY_VIDEO_CAMERA, MfGuids.KSCATEGORY_VIDEO, MfGuids.KSCATEGORY_CAPTURE }
                        : new Guid[] { Guid.Empty };
                    foreach (Guid c in cats)
                    {
                        List<KeyValuePair<CaptureDeviceInfo, IMFActivate>> l = EnumActivates(c);
                        foreach (KeyValuePair<CaptureDeviceInfo, IMFActivate> kv in l) { all.Add(kv.Key); MfApi.Release(kv.Value); }
                    }
                    return all;
                }
                finally { MfApi.MFShutdown(); }
            }, timeoutMs, "ListDevices");
        }

        // Every native media type of every video stream of the device (opens it briefly).
        public static List<CaptureMediaType> ListFormats(string deviceNameSubstring, string symbolicLink = null, int timeoutMs = 10000)
        {
            return RunMta<List<CaptureMediaType>>(delegate
            {
                MfHr.Check(MfApi.MFStartup(MfApi.MF_VERSION, MfApi.MFSTARTUP_LITE), "MFStartup");
                IMFActivate act = null; IntPtr src = IntPtr.Zero; IMFMediaSource srcObj = null; IMFSourceReader rd = null; IMFAttributes at = null;
                try
                {
                    CaptureDeviceInfo dev;
                    act = FindActivate(deviceNameSubstring, symbolicLink, out dev);
                    if (act == null) throw new CaptureException("Device '" + deviceNameSubstring + "' not found by Media Foundation");
                    Guid iid = MfGuids.IID_IMFMediaSource;
                    MfHr.Check(act.ActivateObject(ref iid, out src), "IMFActivate.ActivateObject");
                    srcObj = (IMFMediaSource)Marshal.GetObjectForIUnknown(src);
                    MfHr.Check(MfApi.MFCreateAttributes(out at, 2), "MFCreateAttributes");
                    MfApi.SetU32(at, MfGuids.MF_READWRITE_DISABLE_CONVERTERS, 1, "DISABLE_CONVERTERS");
                    MfHr.Check(MfApi.MFCreateSourceReaderFromMediaSource(src, at, out rd), "MFCreateSourceReaderFromMediaSource");
                    return EnumNativeTypes(rd);
                }
                finally
                {
                    MfApi.Release(at);
                    if (srcObj != null) { try { srcObj.Shutdown(); } catch { } }
                    MfApi.Release(rd);
                    MfApi.Release(srcObj);
                    if (src != IntPtr.Zero) Marshal.Release(src);
                    if (act != null) { try { act.ShutdownObject(); } catch { } MfApi.Release(act); }
                    MfApi.MFShutdown();
                }
            }, timeoutMs, "ListFormats");
        }

        public static string FormatName(CaptureFormat f) { return f.ToString(); }

        // Bytes of one row of the first plane.
        public static int RowBytes(CaptureFormat f, int width)
        {
            switch (f)
            {
                case CaptureFormat.RGB24: return width * 3;
                case CaptureFormat.RGB32: case CaptureFormat.ARGB32: case CaptureFormat.A2R10G10B10:
                case CaptureFormat.AYUV: case CaptureFormat.Y410: case CaptureFormat.V410:
                case CaptureFormat.Y210: case CaptureFormat.Y216: return width * 4;
                case CaptureFormat.RGB565: case CaptureFormat.RGB555: case CaptureFormat.YUY2:
                case CaptureFormat.UYVY: case CaptureFormat.YVYU: case CaptureFormat.P010:
                case CaptureFormat.P016: case CaptureFormat.P210: return width * 2;
                case CaptureFormat.V210: return ((width + 47) / 48) * 128;
                case CaptureFormat.NV12: case CaptureFormat.NV21: case CaptureFormat.I420:
                case CaptureFormat.IYUV: case CaptureFormat.YV12: case CaptureFormat.L8: return width;
                default: return 0;
            }
        }

        // Total rows of all planes, in units of the first plane's height (NV12 1.5, P210 2, packed 1).
        public static double PlaneHeightFactor(CaptureFormat f)
        {
            switch (f)
            {
                case CaptureFormat.NV12: case CaptureFormat.NV21: case CaptureFormat.P010: case CaptureFormat.P016:
                case CaptureFormat.I420: case CaptureFormat.IYUV: case CaptureFormat.YV12: return 1.5;
                case CaptureFormat.P210: return 2.0;
                default: return 1.0;
            }
        }

        public static long QpcTo100ns(long qpc)
        {
            long f = Stopwatch.Frequency;
            if (f == 10000000) return qpc;
            return (long)((decimal)qpc * 10000000m / f);
        }

        public static CaptureFormat FormatFromSubtype(Guid g, out string name)
        {
            uint fcc;
            if (MfGuids.IsFourCCGuid(g, out fcc))
            {
                switch (fcc)
                {
                    case 20: name = "RGB24"; return CaptureFormat.RGB24;
                    case 21: name = "ARGB32"; return CaptureFormat.ARGB32;
                    case 22: name = "RGB32"; return CaptureFormat.RGB32;
                    case 23: name = "RGB565"; return CaptureFormat.RGB565;
                    case 24: name = "RGB555"; return CaptureFormat.RGB555;
                    case 31: name = "A2R10G10B10"; return CaptureFormat.A2R10G10B10;
                    case 41: name = "RGB8"; return CaptureFormat.Unknown;
                    case 50: name = "L8"; return CaptureFormat.L8;
                    case 81: name = "L16"; return CaptureFormat.Unknown;
                    case 113: name = "A16B16G16R16F"; return CaptureFormat.Unknown;
                }
                string s = FourCCText(fcc);
                switch (s)
                {
                    case "YUY2": name = s; return CaptureFormat.YUY2;
                    case "UYVY": name = s; return CaptureFormat.UYVY;
                    case "YVYU": name = s; return CaptureFormat.YVYU;
                    case "NV12": name = s; return CaptureFormat.NV12;
                    case "NV21": name = s; return CaptureFormat.NV21;
                    case "P010": name = s; return CaptureFormat.P010;
                    case "P016": name = s; return CaptureFormat.P016;
                    case "P210": name = s; return CaptureFormat.P210;
                    case "Y210": name = s; return CaptureFormat.Y210;
                    case "Y216": name = s; return CaptureFormat.Y216;
                    case "v210": name = s; return CaptureFormat.V210;
                    case "v410": name = s; return CaptureFormat.V410;
                    case "Y410": name = s; return CaptureFormat.Y410;
                    case "AYUV": name = s; return CaptureFormat.AYUV;
                    case "I420": name = s; return CaptureFormat.I420;
                    case "IYUV": name = s; return CaptureFormat.IYUV;
                    case "YV12": name = s; return CaptureFormat.YV12;
                    case "MJPG": name = s; return CaptureFormat.MJPG;
                    case "H264": name = s; return CaptureFormat.H264;
                    case "HEVC": case "H265": name = s; return CaptureFormat.HEVC;
                }
                if (s != null) { name = s; return CaptureFormat.Unknown; }
                name = "fourcc 0x" + fcc.ToString("X8", CultureInfo.InvariantCulture);
                return CaptureFormat.Unknown;
            }
            name = g.ToString("B");
            return CaptureFormat.Unknown;
        }

        // ================================================================================================================
        // implementation
        // ================================================================================================================
        struct Pending
        {
            public int Hr;
            public uint Flags;
            public long Ts;
            public IntPtr Sample;   // AddRef'd, or Zero
            public long Qpc;
        }

        readonly CaptureOptions opt;
        Thread thread;
        readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        readonly AutoResetEvent sampleEvent = new AutoResetEvent(false);
        readonly ManualResetEvent flushEvent = new ManualResetEvent(false);
        readonly object qLock = new object();
        readonly Queue<Pending> queue = new Queue<Pending>();
        int gen;                 // guarded by qLock; bumped on every Close so late callbacks are ignored
        bool accepting;          // guarded by qLock

        readonly object statLock = new object();
        readonly CaptureStats stats = new CaptureStats();

        // capture-thread-only state
        IMFActivate activate;
        IntPtr sourcePtr;
        IMFMediaSource source;
        IMFSourceReader reader;
        MfReaderCallback callback;
        uint streamIndex;
        bool readPending;
        volatile CaptureMediaType cur;
        volatile CaptureDeviceInfo curDev;
        long frameDur100ns;
        long lastTs = long.MinValue;
        long lastSampleQpc, openedQpc;
        bool gotFirst, noSignal;
        long sequence;
        FrameExtra extra;
        int backoffMs;
        int silentReopenMs;
        long reopenAtQpc;        // 0 = no reopen scheduled

        internal void OnSampleArrived(int cbGen, int hr, uint flags, long ts, IntPtr pSample, long qpc)
        {
            lock (qLock)
            {
                if (cbGen != gen || !accepting) return;   // stale reader: MF keeps/releases its own reference
                if (pSample != IntPtr.Zero) Marshal.AddRef(pSample);
                Pending p; p.Hr = hr; p.Flags = flags; p.Ts = ts; p.Sample = pSample; p.Qpc = qpc;
                queue.Enqueue(p);
            }
            sampleEvent.Set();
        }

        internal void OnFlushDone() { flushEvent.Set(); }

        void Raise(CaptureEvent ev, string detail)
        {
            CaptureEventCallback cb = onEvent;
            if (cb == null) return;
            try { cb(ev, detail); }
            catch { lock (statLock) stats.CallbackExceptions++; }
        }

        void SetState(CaptureState s) { lock (statLock) stats.State = s; }

        static long Now() { return Stopwatch.GetTimestamp(); }
        static long MsToTicks(long ms) { return ms * Stopwatch.Frequency / 1000; }
        static long TicksToMs(long t) { return t * 1000 / Stopwatch.Frequency; }

        void ThreadMain()
        {
            IntPtr mmcss = IntPtr.Zero;
            bool mfStarted = false;
            try
            {
                if (opt.UseMmcss) { try { uint idx = 0; mmcss = MfApi.AvSetMmThreadCharacteristicsW("Capture", ref idx); } catch { } }
                MfHr.Check(MfApi.MFStartup(MfApi.MF_VERSION, MfApi.MFSTARTUP_LITE), "MFStartup");
                mfStarted = true;
                backoffMs = 0;
                reopenAtQpc = 0;
                noSignal = false;
                silentReopenMs = opt.ReopenOnSilenceMs;
                WaitHandle[] handles = new WaitHandle[] { stopEvent, sampleEvent };

                while (!stopEvent.WaitOne(0))
                {
                    if (reader == null)
                    {
                        long now = Now();
                        if (reopenAtQpc != 0 && now < reopenAtQpc)
                        {
                            int ms = (int)Math.Max(1, TicksToMs(reopenAtQpc - now));
                            if (stopEvent.WaitOne(ms)) break;
                            continue;
                        }
                        reopenAtQpc = 0;
                        try { Open(); }
                        catch (Exception ex)
                        {
                            lock (statLock) stats.Errors++;
                            Raise(CaptureEvent.Error, "open: " + ex.Message);
                            Close();
                            ScheduleReopen();
                            continue;
                        }
                        if (!IssueRead()) { Close(); ScheduleReopen(); continue; }
                    }

                    // wait for a sample, the stop request, or the next silence deadline
                    long nowT = Now();
                    long silentMs = TicksToMs(nowT - (gotFirst ? lastSampleQpc : openedQpc));
                    long waitMs = 250;
                    if (!noSignal) waitMs = Math.Min(waitMs, Math.Max(1, opt.NoSignalMs - silentMs));
                    if (silentReopenMs > 0) waitMs = Math.Min(waitMs, Math.Max(1, silentReopenMs - silentMs));
                    int w = WaitHandle.WaitAny(handles, (int)waitMs);
                    if (w == 0) break;
                    if (w == 1) { DrainQueue(); continue; }

                    // timeout: silence checks
                    silentMs = TicksToMs(Now() - (gotFirst ? lastSampleQpc : openedQpc));
                    if (!noSignal && silentMs >= opt.NoSignalMs)
                    {
                        noSignal = true;
                        SetState(CaptureState.NoSignal);
                        lock (statLock) stats.NoSignalEvents++;
                        Raise(CaptureEvent.NoSignal, "no sample for " + silentMs + " ms" + (gotFirst ? "" : " since open"));
                    }
                    if (silentReopenMs > 0 && silentMs >= silentReopenMs)
                    {
                        Raise(CaptureEvent.Reopening, "silent for " + silentMs + " ms");
                        Close();
                        SetState(CaptureState.Reopening);
                        // immediate reopen; the silence clock restarts at the new open and the interval doubles
                        // (up to ReopenOnSilenceMaxMs) until a frame arrives
                        silentReopenMs = Math.Min(Math.Max(opt.ReopenOnSilenceMs, opt.ReopenOnSilenceMaxMs), silentReopenMs * 2);
                        reopenAtQpc = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (statLock) stats.Errors++;
                Raise(CaptureEvent.Error, "capture thread: " + ex.Message);
            }
            finally
            {
                Close();
                if (mfStarted) MfApi.MFShutdown();
                if (mmcss != IntPtr.Zero) { try { MfApi.AvRevertMmThreadCharacteristics(mmcss); } catch { } }
                SetState(CaptureState.Stopped);
                Raise(CaptureEvent.Stopped, "");
            }
        }

        void ScheduleReopen()
        {
            backoffMs = backoffMs == 0 ? opt.ReopenBackoffMinMs : Math.Min(opt.ReopenBackoffMaxMs, backoffMs * 2);
            reopenAtQpc = Now() + MsToTicks(backoffMs);
            SetState(CaptureState.Reopening);
            Raise(CaptureEvent.Reopening, "in " + backoffMs + " ms");
        }

        void Open()
        {
            SetState(CaptureState.Opening);
            Raise(CaptureEvent.Opening, (opt.SymbolicLink ?? opt.DeviceName) + " " + opt.Width + "x" + opt.Height + " @ " +
                  opt.FpsNum + "/" + opt.FpsDen + " pref " + string.Join(",", Array.ConvertAll(opt.Preference, delegate(CaptureFormat f) { return f.ToString(); })));
            CaptureDeviceInfo dev;
            activate = FindActivate(opt.DeviceName, opt.SymbolicLink, out dev);
            if (activate == null) throw new CaptureException("device '" + (opt.SymbolicLink ?? opt.DeviceName) + "' not found by Media Foundation");
            curDev = dev;
            Guid iid = MfGuids.IID_IMFMediaSource;
            MfHr.Check(activate.ActivateObject(ref iid, out sourcePtr), "IMFActivate.ActivateObject");
            source = (IMFMediaSource)Marshal.GetObjectForIUnknown(sourcePtr);

            IMFAttributes at = null;
            try
            {
                MfHr.Check(MfApi.MFCreateAttributes(out at, 4), "MFCreateAttributes");
                int g;
                lock (qLock) g = gen;
                callback = new MfReaderCallback(this, g);
                Guid k = MfGuids.MF_SOURCE_READER_ASYNC_CALLBACK;
                MfHr.Check(at.SetUnknown(ref k, callback), "set MF_SOURCE_READER_ASYNC_CALLBACK");
                MfApi.SetU32(at, MfGuids.MF_READWRITE_DISABLE_CONVERTERS, 1, "set MF_READWRITE_DISABLE_CONVERTERS");
                MfApi.SetU32(at, MfGuids.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 0, "set ENABLE_VIDEO_PROCESSING");
                if (opt.LowLatency) MfApi.SetU32(at, MfGuids.MF_LOW_LATENCY, 1, "set MF_LOW_LATENCY");
                MfHr.Check(MfApi.MFCreateSourceReaderFromMediaSource(sourcePtr, at, out reader), "MFCreateSourceReaderFromMediaSource");
            }
            finally { MfApi.Release(at); }

            // choose the native type
            List<CaptureMediaType> types = EnumNativeTypes(reader);
            CaptureMediaType best = ChooseType(types, opt);
            if (best == null)
            {
                StringBuilder sb = new StringBuilder();
                foreach (CaptureMediaType t in types)
                    if (t.Width == opt.Width && t.Height == opt.Height) sb.Append(sb.Length > 0 ? ", " : "").Append(t.SubtypeName + "@" + t.Fps.ToString("0.##", CultureInfo.InvariantCulture));
                throw new CaptureException("no native " + opt.Width + "x" + opt.Height + " type in [" +
                    string.Join(",", Array.ConvertAll(opt.Preference, delegate(CaptureFormat f) { return f.ToString(); })) +
                    "]; offered at that size: " + (sb.Length > 0 ? sb.ToString() : "none") + " (" + types.Count + " types in total)");
            }
            streamIndex = (uint)best.Stream;
            chosenW = best.Width; chosenH = best.Height;   // (21-11, 1.2.1: puede no ser el pedido si no lo da: ChooseType)
            MfHr.Check(reader.SetStreamSelection(MfApi.ALL_STREAMS, 0), "SetStreamSelection(all,false)");
            MfHr.Check(reader.SetStreamSelection(streamIndex, 1), "SetStreamSelection(video,true)");
            IMFMediaType mt = null;
            try
            {
                MfHr.Check(reader.GetNativeMediaType(streamIndex, (uint)best.Index, out mt), "GetNativeMediaType");
                MfHr.Check(reader.SetCurrentMediaType(streamIndex, IntPtr.Zero, mt), "SetCurrentMediaType(native " + best + ")");
            }
            finally { MfApi.Release(mt); }
            RefreshCurrentType();

            lock (qLock) accepting = true;
            readPending = false;
            gotFirst = false;          // noSignal is kept across reopens: SignalRestored fires on the next frame
            lastTs = long.MinValue;
            openedQpc = Now();
            lock (statLock) { stats.Opens++; stats.State = noSignal ? CaptureState.NoSignal : CaptureState.WaitingForFirstFrame; }
            Raise(CaptureEvent.Opened, cur + " stream " + streamIndex + " on " + dev.FriendlyName);
        }

        static CaptureMediaType ChooseType(List<CaptureMediaType> types, CaptureOptions o)
        {
            double want = o.FpsDen == 0 ? 60 : (double)o.FpsNum / o.FpsDen;
            foreach (CaptureFormat f in o.Preference)
            {
                CaptureMediaType best = null;
                double bestDiff = double.MaxValue;
                foreach (CaptureMediaType t in types)
                {
                    if (t.Format != f || t.Width != o.Width || t.Height != o.Height) continue;
                    double d = Math.Abs(t.Fps - want);
                    if (d < bestDiff - 1e-6) { best = t; bestDiff = d; }
                }
                if (best != null) return best;
            }
            // 21-11 (1.2.1): ese tamaño no lo da (como DsCapture.Choose): el mas parecido (el mismo aspecto antes; luego el area mas cercana)
            // y en el la velocidad mas cercana. Acceptable() sigue pidiendo el tamaño pedido: un cambio de tipo en marcha reabre
            long wantArea = (long)o.Width * o.Height;
            foreach (CaptureFormat f in o.Preference)
            {
                CaptureMediaType best = null;
                double bestScore = double.MaxValue;
                foreach (CaptureMediaType t in types)
                {
                    if (t.Format != f || t.Width <= 0 || t.Height <= 0) continue;
                    double aspect = Math.Abs((long)t.Width * o.Height - (long)t.Height * o.Width) > wantArea / 50 ? 1e15 : 0;
                    double score = aspect + Math.Abs((long)t.Width * t.Height - wantArea) * 1000.0 + Math.Abs(t.Fps - want);
                    if (score < bestScore - 1e-6) { best = t; bestScore = score; }
                }
                if (best != null) return best;
            }
            return null;
        }

        void RefreshCurrentType()
        {
            IMFMediaType mt = null;
            try
            {
                MfHr.Check(reader.GetCurrentMediaType(streamIndex, out mt), "GetCurrentMediaType");
                CaptureMediaType t = ToInfo(mt, (int)streamIndex, -1);
                cur = t;
                frameDur100ns = t.FpsNum > 0 ? (long)Math.Round(10000000.0 * t.FpsDen / t.FpsNum) : 0;
            }
            finally { MfApi.Release(mt); }
        }

        int chosenW, chosenH;   // 21-11 (1.2.1): el tamaño elegido al abrir (el pedido o, si no lo da, el mas parecido)

        bool Acceptable(CaptureMediaType t)
        {
            if (t == null || !((t.Width == opt.Width && t.Height == opt.Height) || (t.Width == chosenW && t.Height == chosenH))) return false;
            return Array.IndexOf(opt.Preference, t.Format) >= 0;
        }

        bool IssueRead()
        {
            if (reader == null) return false;
            int hr = reader.ReadSample(streamIndex, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (hr < 0)
            {
                lock (statLock) stats.Errors++;
                Raise(CaptureEvent.Error, "ReadSample: " + MfHr.Describe(hr));
                readPending = false;
                return false;
            }
            readPending = true;
            return true;
        }

        void DrainQueue()
        {
            while (true)
            {
                Pending p;
                bool more;
                lock (qLock)
                {
                    if (queue.Count == 0) return;
                    p = queue.Dequeue();
                    more = queue.Count > 0;
                }
                readPending = false;
                if (more && p.Sample != IntPtr.Zero && (p.Flags & ~MfApi.READERF_STREAMTICK) == 0)
                {
                    // a newer result is already waiting: skip this frame (latency first)
                    Marshal.Release(p.Sample);
                    lock (statLock) stats.LateDropped++;
                    continue;
                }
                if (!Handle(p)) return;   // closed for reopen
            }
        }

        // Returns false when the reader was closed (reopen scheduled).
        bool Handle(Pending p)
        {
            try
            {
                if (p.Hr < 0)
                {
                    lock (statLock) stats.Errors++;
                    Raise(CaptureEvent.Error, "OnReadSample status " + MfHr.Describe(p.Hr));
                    ReleasePending(ref p);
                    Close(); ScheduleReopen();
                    return false;
                }
                if ((p.Flags & MfApi.READERF_ERROR) != 0)
                {
                    lock (statLock) stats.Errors++;
                    Raise(CaptureEvent.Error, "MF_SOURCE_READERF_ERROR");
                    ReleasePending(ref p);
                    Close(); ScheduleReopen();
                    return false;
                }
                if ((p.Flags & MfApi.READERF_ENDOFSTREAM) != 0)
                {
                    Raise(CaptureEvent.EndOfStream, "");
                    ReleasePending(ref p);
                    Close(); ScheduleReopen();
                    return false;
                }
                if ((p.Flags & (MfApi.READERF_NATIVEMEDIATYPECHANGED | MfApi.READERF_CURRENTMEDIATYPECHANGED)) != 0)
                {
                    lock (statLock) stats.FormatChanges++;
                    try { RefreshCurrentType(); } catch (Exception ex) { Raise(CaptureEvent.Error, "after format change: " + ex.Message); }
                    Raise(CaptureEvent.FormatChanged, "flags 0x" + p.Flags.ToString("X", CultureInfo.InvariantCulture) + " -> " + cur);
                    if (!Acceptable(cur))
                    {
                        ReleasePending(ref p);
                        Close(); backoffMs = 0; ScheduleReopen();
                        return false;
                    }
                    lastTs = long.MinValue;
                }
                if ((p.Flags & MfApi.READERF_STREAMTICK) != 0)
                {
                    lock (statLock) stats.StreamTicks++;
                    Raise(CaptureEvent.StreamTick, "t=" + p.Ts);
                }

                // keep one request outstanding while this frame is consumed
                if (!IssueRead())
                {
                    ReleasePending(ref p);
                    Close(); ScheduleReopen();
                    return false;
                }

                if (p.Sample != IntPtr.Zero) Deliver(p);
                return true;
            }
            finally { ReleasePending(ref p); }
        }

        static void ReleasePending(ref Pending p)
        {
            if (p.Sample != IntPtr.Zero) { Marshal.Release(p.Sample); p.Sample = IntPtr.Zero; }
        }

        void Deliver(Pending p)
        {
            CaptureMediaType t = cur;
            backoffMs = 0;
            silentReopenMs = opt.ReopenOnSilenceMs;
            lastSampleQpc = p.Qpc;
            if (noSignal) { noSignal = false; Raise(CaptureEvent.SignalRestored, ""); }
            if (!gotFirst)
            {
                gotFirst = true;
                Raise(CaptureEvent.FirstFrame, (TicksToMs(p.Qpc - openedQpc)) + " ms after open");
            }
            SetStateIfNot(CaptureState.Running);

            if (lastTs != long.MinValue)
            {
                long d = p.Ts - lastTs;
                if (d <= 0) { lock (statLock) stats.DuplicateTimestamps++; }
                else if (frameDur100ns > 0 && d > frameDur100ns * 3 / 2)
                {
                    long lost = (long)Math.Round((double)d / frameDur100ns) - 1;
                    if (lost < 1) lost = 1;
                    lock (statLock) { stats.Gaps++; stats.EstimatedLost += lost; }
                    Raise(CaptureEvent.Gap, (d / 10000.0).ToString("0.00", CultureInfo.InvariantCulture) + " ms, ~" + lost + " lost");
                }
            }
            lastTs = p.Ts;

            object rcw = null;
            IMFMediaBuffer buf = null;
            try
            {
                rcw = Marshal.GetObjectForIUnknown(p.Sample);
                IMFSample s = (IMFSample)rcw;
                IMFAttributes sa = (IMFAttributes)rcw;
                FrameExtra fx = new FrameExtra();
                ulong u64; uint u32; long dur;
                if (MfApi.GetU64(sa, MfGuids.MFSampleExtension_DeviceTimestamp, out u64)) fx.DeviceTimestamp100ns = (long)u64;
                if (MfApi.GetU64(sa, MfGuids.MFSampleExtension_DeviceReferenceSystemTime, out u64)) fx.DeviceReferenceSystemTime = (long)u64;
                if (MfApi.GetU32(sa, MfGuids.MFSampleExtension_Discontinuity, out u32) && u32 != 0) { fx.Discontinuity = true; lock (statLock) stats.Discontinuities++; }
                if (s.GetSampleDuration(out dur) >= 0) fx.SampleDuration100ns = dur;

                uint count;
                MfHr.Check(s.GetBufferCount(out count), "GetBufferCount");
                if (count == 1) MfHr.Check(s.GetBufferByIndex(0, out buf), "GetBufferByIndex");
                else MfHr.Check(s.ConvertToContiguousBuffer(out buf), "ConvertToContiguousBuffer");

                int w = t.Width, h = t.Height;
                IMF2DBuffer b2 = buf as IMF2DBuffer;
                IntPtr scan0; int pitch;
                if (b2 != null && b2.Lock2D(out scan0, out pitch) >= 0)
                {
                    try
                    {
                        fx.UsedLock2D = true;
                        uint cl; if (b2.GetContiguousLength(out cl) >= 0) fx.BufferLength = cl;
                        Invoke(scan0, pitch, w, h, t.Format, p, ref fx);
                    }
                    finally { b2.Unlock2D(); }
                }
                else
                {
                    IntPtr basePtr; uint max, len;
                    MfHr.Check(buf.Lock(out basePtr, out max, out len), "IMFMediaBuffer.Lock");
                    try
                    {
                        fx.BufferLength = len;
                        int stride = t.HasDefaultStride ? t.DefaultStride : RowBytes(t.Format, w);
                        if (t.Format == CaptureFormat.RGB24 && !t.HasDefaultStride) stride = (w * 3 + 3) & ~3;
                        long need = (long)Math.Abs(stride) * (long)Math.Ceiling(h * PlaneHeightFactor(t.Format));
                        if (stride == 0 || len < need)
                        {
                            lock (statLock) stats.Errors++;
                            Raise(CaptureEvent.Error, "buffer " + len + " bytes < expected " + need + " (stride " + stride + ")");
                            return;
                        }
                        IntPtr top = stride < 0 ? new IntPtr(basePtr.ToInt64() + (long)(-stride) * (h - 1)) : basePtr;
                        Invoke(top, stride, w, h, t.Format, p, ref fx);
                    }
                    finally { buf.Unlock(); }
                }
            }
            catch (Exception ex)
            {
                lock (statLock) stats.Errors++;
                Raise(CaptureEvent.Error, "deliver: " + ex.Message);
            }
            finally
            {
                MfApi.Release(buf);
                MfApi.Release(rcw);
            }
        }

        void Invoke(IntPtr data, int pitch, int w, int h, CaptureFormat f, Pending p, ref FrameExtra fx)
        {
            long seq = sequence++;
            fx.DeliverQpc = Now();
            extra = fx;
            lock (statLock) { stats.Frames++; stats.LastArrivalQpc = p.Qpc; }
            FrameCallback cb = onFrame;
            if (cb == null) return;
            try { cb(data, pitch, w, h, f, p.Qpc, p.Ts, seq); }
            catch { lock (statLock) stats.CallbackExceptions++; }
        }

        void SetStateIfNot(CaptureState s) { lock (statLock) if (stats.State != s) stats.State = s; }

        void Close()
        {
            lock (qLock)
            {
                accepting = false;
                gen++;
                while (queue.Count > 0) { Pending p = queue.Dequeue(); ReleasePending(ref p); }
            }
            if (reader != null && readPending)
            {
                flushEvent.Reset();
                try { if (reader.Flush(MfApi.ALL_STREAMS) >= 0) flushEvent.WaitOne(200); } catch { }
            }
            readPending = false;
            if (source != null) { try { source.Shutdown(); } catch { } }
            MfApi.Release(reader); reader = null;
            MfApi.Release(source); source = null;
            if (sourcePtr != IntPtr.Zero) { Marshal.Release(sourcePtr); sourcePtr = IntPtr.Zero; }
            if (activate != null) { try { activate.ShutdownObject(); } catch { } MfApi.Release(activate); activate = null; }
            callback = null;
            // drop anything a late callback queued between the drain above and the shutdown
            lock (qLock) { while (queue.Count > 0) { Pending p = queue.Dequeue(); ReleasePending(ref p); } }
        }

        // ---- enumeration helpers (MF must be started on the calling thread) ----

        static List<KeyValuePair<CaptureDeviceInfo, IMFActivate>> EnumActivates(Guid category)
        {
            List<KeyValuePair<CaptureDeviceInfo, IMFActivate>> res = new List<KeyValuePair<CaptureDeviceInfo, IMFActivate>>();
            IMFAttributes at = null;
            IntPtr arr = IntPtr.Zero;
            uint n = 0;
            try
            {
                MfHr.Check(MfApi.MFCreateAttributes(out at, 2), "MFCreateAttributes");
                MfApi.SetGuid(at, MfGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MfGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID, "set SOURCE_TYPE");
                if (category != Guid.Empty) MfApi.SetGuid(at, MfGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_CATEGORY, category, "set VIDCAP_CATEGORY");
                int hr = MfApi.MFEnumDeviceSources(at, out arr, out n);
                if (hr < 0) { if (category == Guid.Empty) MfHr.Check(hr, "MFEnumDeviceSources"); return res; }
                for (uint i = 0; i < n; i++)
                {
                    IntPtr p = Marshal.ReadIntPtr(arr, (int)i * IntPtr.Size);
                    if (p == IntPtr.Zero) continue;
                    IMFActivate a = (IMFActivate)Marshal.GetObjectForIUnknown(p);
                    Marshal.Release(p);
                    IMFAttributes aa = (IMFAttributes)a;
                    CaptureDeviceInfo d = new CaptureDeviceInfo();
                    d.FriendlyName = MfApi.GetString(aa, MfGuids.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME) ?? "";
                    d.SymbolicLink = MfApi.GetString(aa, MfGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK) ?? "";
                    d.Category = category;
                    res.Add(new KeyValuePair<CaptureDeviceInfo, IMFActivate>(d, a));
                }
            }
            finally
            {
                if (arr != IntPtr.Zero) MfApi.CoTaskMemFree(arr);
                MfApi.Release(at);
            }
            return res;
        }

        // First device matching the symbolic link (exact) or the name substring; tries the default MF category, then
        // KSCATEGORY_VIDEO and KSCATEGORY_CAPTURE. The others are released.
        static IMFActivate FindActivate(string nameSub, string symLink, out CaptureDeviceInfo dev)
        {
            dev = null;
            Guid[] cats = new Guid[] { Guid.Empty, MfGuids.KSCATEGORY_VIDEO, MfGuids.KSCATEGORY_CAPTURE };
            foreach (Guid c in cats)
            {
                IMFActivate found = null;
                foreach (KeyValuePair<CaptureDeviceInfo, IMFActivate> kv in EnumActivates(c))
                {
                    bool match = found == null && (
                        (!string.IsNullOrEmpty(symLink) && string.Equals(kv.Key.SymbolicLink, symLink, StringComparison.OrdinalIgnoreCase)) ||
                        (string.IsNullOrEmpty(symLink) && !string.IsNullOrEmpty(nameSub) &&
                         kv.Key.FriendlyName.IndexOf(nameSub, StringComparison.OrdinalIgnoreCase) >= 0));
                    if (match) { found = kv.Value; dev = kv.Key; }
                    else MfApi.Release(kv.Value);
                }
                if (found != null) return found;
            }
            return null;
        }

        static List<CaptureMediaType> EnumNativeTypes(IMFSourceReader rd)
        {
            List<CaptureMediaType> res = new List<CaptureMediaType>();
            for (uint s = 0; s < 16; s++)
            {
                int sel;
                if (rd.GetStreamSelection(s, out sel) < 0) break;   // MF_E_INVALIDSTREAMNUMBER: no more streams
                for (uint i = 0; ; i++)
                {
                    IMFMediaType mt;
                    int hr = rd.GetNativeMediaType(s, i, out mt);
                    if (hr < 0) break;                               // MF_E_NO_MORE_TYPES
                    try
                    {
                        Guid major;
                        if (mt.GetMajorType(out major) >= 0 && major != MfGuids.MFMediaType_Video) continue;
                        res.Add(ToInfo(mt, (int)s, (int)i));
                    }
                    finally { MfApi.Release(mt); }
                }
            }
            return res;
        }

        static CaptureMediaType ToInfo(IMFMediaType mt, int stream, int index)
        {
            IMFAttributes a = (IMFAttributes)mt;
            CaptureMediaType t = new CaptureMediaType();
            t.Stream = stream; t.Index = index;
            Guid sub;
            if (MfApi.GetGuid(a, MfGuids.MF_MT_SUBTYPE, out sub)) { t.Subtype = sub; t.Format = FormatFromSubtype(sub, out t.SubtypeName); }
            else t.SubtypeName = "?";
            ulong v;
            if (MfApi.GetU64(a, MfGuids.MF_MT_FRAME_SIZE, out v)) { t.Width = (int)(v >> 32); t.Height = (int)(v & 0xFFFFFFFF); }
            if (MfApi.GetU64(a, MfGuids.MF_MT_FRAME_RATE, out v)) { t.FpsNum = (int)(v >> 32); t.FpsDen = (int)(v & 0xFFFFFFFF); }
            t.FpsMin = t.FpsMax = t.Fps;
            uint u;
            if (MfApi.GetU32(a, MfGuids.MF_MT_DEFAULT_STRIDE, out u)) { t.HasDefaultStride = true; t.DefaultStride = unchecked((int)u); }
            if (MfApi.GetU32(a, MfGuids.MF_MT_SAMPLE_SIZE, out u)) t.SampleSize = (int)u;
            int comp;
            if (mt.IsCompressedFormat(out comp) >= 0) t.Compressed = comp != 0;
            return t;
        }

        static string FourCCText(uint fcc)
        {
            char[] c = new char[4];
            for (int i = 0; i < 4; i++)
            {
                c[i] = (char)((fcc >> (8 * i)) & 0xFF);
                if (c[i] < 32 || c[i] > 126) return null;
            }
            return new string(c);
        }

        static T RunMta<T>(Func<T> f, int timeoutMs, string what)
        {
            T result = default(T);
            Exception err = null;
            Thread t = new Thread(delegate() { try { result = f(); } catch (Exception ex) { err = ex; } });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            if (!t.Join(timeoutMs)) throw new TimeoutException(what + " did not finish within " + timeoutMs + " ms");
            if (err != null)
            {
                if (err is CaptureException) throw new CaptureException(err.Message, err.HResult, err);
                throw new CaptureException(what + ": " + err.Message, err.HResult, err);
            }
            return result;
        }
    }
}
