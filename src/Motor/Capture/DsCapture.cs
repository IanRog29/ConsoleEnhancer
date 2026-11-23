// DsCapture.cs - DirectShow video capture (AVerMedia GC573) for Switch2Engine. Same API as MfCapture (ICaptureSource);
// shared types (CaptureOptions, CaptureFormat, FrameCallback, ...) live in MfCapture.cs, so compile both files.
//
// Why DirectShow: Media Foundation enumerates the GC573 but cannot create its media source (MF_E_INVALIDMEDIATYPE),
// see the note on MfCapture.
//
// Graph: GC573 capture filter, video pin only ("Capturar"/"0", format set with IAMStreamConfig, ConnectDirect so no
// converter is ever inserted) -> Sample Grabber (qedit.dll, callback mode, no buffering) -> Null Renderer (its clock is
// removed so it never waits). The audio pins are not touched, so another process (the launcher's audio mpv) can open
// them on its own filter instance at the same time.
// Sample Grabber calls SampleCB on the driver's streaming thread: by default the sample is AddRef'd and handed to the
// dedicated MTA capture thread, which calls OnFrame (CaptureOptions.DeliverOnSourceThread = true calls OnFrame right
// there instead). The capture thread waits with timeouts: no signal never blocks it.
//
// C# 5 / .NET Framework 4.8. Compile with /unsafe /platform:x64.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor.Native
{
    // ---------------------------------------------------------------------------------------------------------------
    // DirectShow COM interfaces (full vtables, no C# inheritance; IDispatch slots are placeholders)
    // ---------------------------------------------------------------------------------------------------------------
    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator([In] ref Guid clsidDeviceClass, out IntPtr ppEnumMoniker, int dwFlags);
    }

    [ComImport, Guid("00000102-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDsEnumMoniker   // IEnumMoniker
    {
        [PreserveSig] int Next(uint celt, out IntPtr rgelt, out uint pceltFetched);
        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IntPtr ppenum);
    }

    [ComImport, Guid("0000000f-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDsMoniker       // IMoniker : IPersistStream : IPersist
    {
        [PreserveSig] int GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load(IntPtr pStm);
        [PreserveSig] int Save(IntPtr pStm, int fClearDirty);
        [PreserveSig] int GetSizeMax(out long pcbSize);
        [PreserveSig] int BindToObject(IntPtr pbc, IntPtr pmkToLeft, [In] ref Guid riidResult, out IntPtr ppvResult);
        [PreserveSig] int BindToStorage(IntPtr pbc, IntPtr pmkToLeft, [In] ref Guid riid, out IntPtr ppvObj);
        [PreserveSig] int Reduce(IntPtr pbc, int dwReduceHowFar, IntPtr ppmkToLeft, out IntPtr ppmkReduced);
        [PreserveSig] int ComposeWith(IntPtr pmkRight, int fOnlyIfNotGeneric, out IntPtr ppmkComposite);
        [PreserveSig] int Enum(int fForward, out IntPtr ppenumMoniker);
        [PreserveSig] int IsEqual(IntPtr pmkOtherMoniker);
        [PreserveSig] int Hash(out int pdwHash);
        [PreserveSig] int IsRunning(IntPtr pbc, IntPtr pmkToLeft, IntPtr pmkNewlyRunning);
        [PreserveSig] int GetTimeOfLastChange(IntPtr pbc, IntPtr pmkToLeft, out long pFileTime);
        [PreserveSig] int Inverse(out IntPtr ppmk);
        [PreserveSig] int CommonPrefixWith(IntPtr pmkOther, out IntPtr ppmkPrefix);
        [PreserveSig] int RelativePathTo(IntPtr pmkOther, out IntPtr ppmkRelPath);
        [PreserveSig] int GetDisplayName(IntPtr pbc, IntPtr pmkToLeft, out IntPtr ppszDisplayName);
        [PreserveSig] int ParseDisplayName(IntPtr pbc, IntPtr pmkToLeft, IntPtr pszDisplayName, out int pchEaten, out IntPtr ppmkOut);
        [PreserveSig] int IsSystemMoniker(out int pdwMksys);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyBag
    {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar, IntPtr pErrorLog);
        [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar);
    }

    [ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IBaseFilter      // IBaseFilter : IMediaFilter : IPersist
    {
        [PreserveSig] int GetClassID(out Guid pClassID);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long tStart);
        [PreserveSig] int GetState(int dwMilliSecsTimeout, out int state);
        [PreserveSig] int SetSyncSource(IntPtr pClock);
        [PreserveSig] int GetSyncSource(out IntPtr pClock);
        [PreserveSig] int EnumPins(out IntPtr ppEnum);
        [PreserveSig] int FindPin([MarshalAs(UnmanagedType.LPWStr)] string Id, out IntPtr ppPin);
        [PreserveSig] int QueryFilterInfo(IntPtr pInfo);
        [PreserveSig] int JoinFilterGraph(IntPtr pGraph, [MarshalAs(UnmanagedType.LPWStr)] string pName);
        [PreserveSig] int QueryVendorInfo(out IntPtr pVendorInfo);
    }

    [ComImport, Guid("56a86899-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMediaFilter     // IMediaFilter : IPersist
    {
        [PreserveSig] int GetClassID(out Guid pClassID);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long tStart);
        [PreserveSig] int GetState(int dwMilliSecsTimeout, out int state);
        [PreserveSig] int SetSyncSource(IntPtr pClock);
        [PreserveSig] int GetSyncSource(out IntPtr pClock);
    }

    [ComImport, Guid("56a868a9-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IGraphBuilder    // IGraphBuilder : IFilterGraph
    {
        [PreserveSig] int AddFilter([MarshalAs(UnmanagedType.Interface)] IBaseFilter pFilter, [MarshalAs(UnmanagedType.LPWStr)] string pName);
        [PreserveSig] int RemoveFilter([MarshalAs(UnmanagedType.Interface)] IBaseFilter pFilter);
        [PreserveSig] int EnumFilters(out IntPtr ppEnum);
        [PreserveSig] int FindFilterByName([MarshalAs(UnmanagedType.LPWStr)] string pName, out IntPtr ppFilter);
        [PreserveSig] int ConnectDirect([MarshalAs(UnmanagedType.Interface)] IPin ppinOut, [MarshalAs(UnmanagedType.Interface)] IPin ppinIn, IntPtr pmt);
        [PreserveSig] int Reconnect([MarshalAs(UnmanagedType.Interface)] IPin ppin);
        [PreserveSig] int Disconnect([MarshalAs(UnmanagedType.Interface)] IPin ppin);
        [PreserveSig] int SetDefaultSyncSource();
        [PreserveSig] int Connect([MarshalAs(UnmanagedType.Interface)] IPin ppinOut, [MarshalAs(UnmanagedType.Interface)] IPin ppinIn);
        [PreserveSig] int Render([MarshalAs(UnmanagedType.Interface)] IPin ppinOut);
        [PreserveSig] int RenderFile([MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFile, [MarshalAs(UnmanagedType.LPWStr)] string lpcwstrPlayList);
        [PreserveSig] int AddSourceFilter([MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFileName, [MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFilterName, out IntPtr ppFilter);
        [PreserveSig] int SetLogFile(IntPtr hFile);
        [PreserveSig] int Abort();
        [PreserveSig] int ShouldOperationContinue();
    }

    [ComImport, Guid("56a868b1-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMediaControl    // dual: IDispatch slots first
    {
        [PreserveSig] int _GetTypeInfoCount(out uint n);
        [PreserveSig] int _GetTypeInfo(uint i, uint lcid, out IntPtr ti);
        [PreserveSig] int _GetIDsOfNames(IntPtr riid, IntPtr names, uint count, uint lcid, IntPtr ids);
        [PreserveSig] int _Invoke(int id, IntPtr riid, uint lcid, ushort flags, IntPtr dp, IntPtr res, IntPtr ex, IntPtr arg);
        [PreserveSig] int Run();
        [PreserveSig] int Pause();
        [PreserveSig] int Stop();
        [PreserveSig] int GetState(int msTimeout, out int pfs);
        [PreserveSig] int RenderFile(IntPtr strFilename);
        [PreserveSig] int AddSourceFilter(IntPtr strFilename, out IntPtr ppUnk);
        [PreserveSig] int get_FilterCollection(out IntPtr ppUnk);
        [PreserveSig] int get_RegFilterCollection(out IntPtr ppUnk);
        [PreserveSig] int StopWhenReady();
    }

    [ComImport, Guid("56a868b6-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMediaEvent      // dual: IDispatch slots first
    {
        [PreserveSig] int _GetTypeInfoCount(out uint n);
        [PreserveSig] int _GetTypeInfo(uint i, uint lcid, out IntPtr ti);
        [PreserveSig] int _GetIDsOfNames(IntPtr riid, IntPtr names, uint count, uint lcid, IntPtr ids);
        [PreserveSig] int _Invoke(int id, IntPtr riid, uint lcid, ushort flags, IntPtr dp, IntPtr res, IntPtr ex, IntPtr arg);
        [PreserveSig] int GetEventHandle(out IntPtr hEvent);
        [PreserveSig] int GetEvent(out int lEventCode, out IntPtr lParam1, out IntPtr lParam2, int msTimeout);
        [PreserveSig] int WaitForCompletion(int msTimeout, out int pEvCode);
        [PreserveSig] int CancelDefaultHandling(int lEvCode);
        [PreserveSig] int RestoreDefaultHandling(int lEvCode);
        [PreserveSig] int FreeEventParams(int lEvCode, IntPtr lParam1, IntPtr lParam2);
    }

    [ComImport, Guid("56a86892-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumPins
    {
        [PreserveSig] int Next(uint cPins, out IntPtr ppPins, out uint pcFetched);   // cPins = 1
        [PreserveSig] int Skip(uint cPins);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IntPtr ppEnum);
    }

    [ComImport, Guid("56a86891-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPin
    {
        [PreserveSig] int Connect(IntPtr pReceivePin, IntPtr pmt);
        [PreserveSig] int ReceiveConnection(IntPtr pConnector, IntPtr pmt);
        [PreserveSig] int Disconnect();
        [PreserveSig] int ConnectedTo(out IntPtr pPin);
        [PreserveSig] int ConnectionMediaType(IntPtr pmt);
        [PreserveSig] int QueryPinInfo(IntPtr pInfo);     // PIN_INFO: IBaseFilter* (8) + PIN_DIRECTION (4) + WCHAR[128] -> 272 bytes
        [PreserveSig] int QueryDirection(out int pPinDir);
        [PreserveSig] int QueryId(out IntPtr Id);
        [PreserveSig] int QueryAccept(IntPtr pmt);
        [PreserveSig] int EnumMediaTypes(out IntPtr ppEnum);
        [PreserveSig] int QueryInternalConnections(IntPtr apPin, ref uint nPin);
        [PreserveSig] int EndOfStream();
        [PreserveSig] int BeginFlush();
        [PreserveSig] int EndFlush();
        [PreserveSig] int NewSegment(long tStart, long tStop, double dRate);
    }

    [ComImport, Guid("C6E13340-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAMStreamConfig
    {
        [PreserveSig] int SetFormat(IntPtr pmt);
        [PreserveSig] int GetFormat(out IntPtr ppmt);
        [PreserveSig] int GetNumberOfCapabilities(out int piCount, out int piSize);
        [PreserveSig] int GetStreamCaps(int iIndex, out IntPtr ppmt, IntPtr pSCC);   // pSCC: VIDEO_STREAM_CONFIG_CAPS (128 bytes)
    }

    [ComImport, Guid("C6E13344-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAMDroppedFrames
    {
        [PreserveSig] int GetNumDropped(out int plDropped);
        [PreserveSig] int GetNumNotDropped(out int plNotDropped);
        [PreserveSig] int GetDroppedInfo(int lSize, IntPtr plArray, out int plNumCopied);
        [PreserveSig] int GetAverageFrameSize(out int plAverageSize);
    }

    [ComImport, Guid("62EA93BA-EC62-11d2-B770-00C04FB6BD3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAMLatency
    {
        [PreserveSig] int GetLatency(out long prtLatency);
    }

    [ComImport, Guid("56a86897-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IReferenceClock
    {
        [PreserveSig] int GetTime(out long pTime);
        [PreserveSig] int AdviseTime(long baseTime, long streamTime, IntPtr hEvent, out IntPtr pdwAdviseCookie);
        [PreserveSig] int AdvisePeriodic(long startTime, long periodTime, IntPtr hSemaphore, out IntPtr pdwAdviseCookie);
        [PreserveSig] int Unadvise(IntPtr dwAdviseCookie);
    }

    [ComImport, Guid("56a8689a-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMediaSample
    {
        [PreserveSig] int GetPointer(out IntPtr ppBuffer);
        [PreserveSig] int GetSize();                          // returns long, not HRESULT
        [PreserveSig] int GetTime(out long pTimeStart, out long pTimeEnd);
        [PreserveSig] int SetTime(IntPtr pTimeStart, IntPtr pTimeEnd);
        [PreserveSig] int IsSyncPoint();
        [PreserveSig] int SetSyncPoint(int bIsSyncPoint);
        [PreserveSig] int IsPreroll();
        [PreserveSig] int SetPreroll(int bIsPreroll);
        [PreserveSig] int GetActualDataLength();              // returns long, not HRESULT
        [PreserveSig] int SetActualDataLength(int len);
        [PreserveSig] int GetMediaType(out IntPtr ppMediaType);
        [PreserveSig] int SetMediaType(IntPtr pMediaType);
        [PreserveSig] int IsDiscontinuity();
        [PreserveSig] int SetDiscontinuity(int bDiscontinuity);
        [PreserveSig] int GetMediaTime(out long pTimeStart, out long pTimeEnd);
        [PreserveSig] int SetMediaTime(IntPtr pTimeStart, IntPtr pTimeEnd);
    }

    [ComImport, Guid("6B652FFF-11FE-4fce-92AD-0266B5D7C78F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISampleGrabber
    {
        [PreserveSig] int SetOneShot(int OneShot);
        [PreserveSig] int SetMediaType(IntPtr pType);
        [PreserveSig] int GetConnectedMediaType(IntPtr pType);
        [PreserveSig] int SetBufferSamples(int BufferThem);
        [PreserveSig] int GetCurrentBuffer(ref int pBufferSize, IntPtr pBuffer);
        [PreserveSig] int GetCurrentSample(out IntPtr ppSample);
        [PreserveSig] int SetCallback([MarshalAs(UnmanagedType.Interface)] ISampleGrabberCB pCallback, int WhichMethodToCallback);
    }

    [ComImport, Guid("0579154A-2B53-4994-B0D0-E773148EFF85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISampleGrabberCB
    {
        [PreserveSig] int SampleCB(double SampleTime, IntPtr pSample);
        [PreserveSig] int BufferCB(double SampleTime, IntPtr pBuffer, int BufferLen);
    }

    // 88 bytes on x64 (GUID align 4: 16+16+4+4+4 = 44, formattype 44..60, pUnk 64, cbFormat 72, pbFormat 80)
    [StructLayout(LayoutKind.Sequential)]
    public struct AM_MEDIA_TYPE
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

    public static class DsApi
    {
        public static readonly Guid CLSID_SystemDeviceEnum = new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
        public static readonly Guid CLSID_VideoInputDeviceCategory = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");
        public static readonly Guid CLSID_FilterGraph = new Guid("E436EBB3-524F-11CE-9F53-0020AF0BA770");
        public static readonly Guid CLSID_SystemClock = new Guid("E436EBB1-524F-11CE-9F53-0020AF0BA770");
        public static readonly Guid CLSID_SampleGrabber = new Guid("C1F400A0-3F08-11d3-9F0B-006008039E37");
        public static readonly Guid CLSID_NullRenderer = new Guid("C1F400A4-3F08-11d3-9F0B-006008039E37");
        public static readonly Guid IID_IBaseFilter = new Guid("56a86895-0ad4-11ce-b03a-0020af0ba770");
        public static readonly Guid IID_IPropertyBag = new Guid("55272A00-42CB-11CE-8135-00AA004BB851");
        public static readonly Guid MEDIATYPE_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid MEDIASUBTYPE_RGB24 = new Guid("e436eb7d-524f-11ce-9f53-0020af0ba770");
        public static readonly Guid MEDIASUBTYPE_RGB32 = new Guid("e436eb7e-524f-11ce-9f53-0020af0ba770");
        public static readonly Guid MEDIASUBTYPE_RGB565 = new Guid("e436eb7b-524f-11ce-9f53-0020af0ba770");
        public static readonly Guid MEDIASUBTYPE_RGB555 = new Guid("e436eb7c-524f-11ce-9f53-0020af0ba770");
        public static readonly Guid MEDIASUBTYPE_ARGB32 = new Guid("773c9ac0-3274-11d0-b724-00aa006c1a01");
        public static readonly Guid FORMAT_VideoInfo = new Guid("05589f80-c356-11ce-bf01-00aa0055595a");
        public static readonly Guid FORMAT_VideoInfo2 = new Guid("f72a76a0-eb0a-11d0-ace4-0000c0cc16ba");

        public const int PINDIR_INPUT = 0, PINDIR_OUTPUT = 1;
        public const int S_FALSE = 1;
        public const int VFW_S_NO_STOP_TIME = 0x00040270;
        public const int E_ABORT = unchecked((int)0x80004004);

        public const int EC_COMPLETE = 0x01, EC_USERABORT = 0x02, EC_ERRORABORT = 0x03, EC_STREAM_ERROR_STOPPED = 0x06,
                         EC_STREAM_ERROR_STILLPLAYING = 0x07, EC_ERROR_STILLPLAYING = 0x08, EC_VIDEO_SIZE_CHANGED = 0x0A,
                         EC_CLOCK_CHANGED = 0x0D, EC_PAUSED = 0x0E, EC_DEVICE_LOST = 0x1F, EC_ERRORABORTEX = 0x45;

        public static object CoCreate(Guid clsid) { return Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, true)); }

        // Frees the contents of an AM_MEDIA_TYPE (FreeMediaType)
        public static void FreeMediaType(IntPtr pmt)
        {
            if (pmt == IntPtr.Zero) return;
            AM_MEDIA_TYPE mt = (AM_MEDIA_TYPE)Marshal.PtrToStructure(pmt, typeof(AM_MEDIA_TYPE));
            if (mt.pbFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(mt.pbFormat);
            if (mt.pUnk != IntPtr.Zero) Marshal.Release(mt.pUnk);
            mt.pbFormat = IntPtr.Zero; mt.cbFormat = 0; mt.pUnk = IntPtr.Zero;
            Marshal.StructureToPtr(mt, pmt, false);
        }

        // Frees an AM_MEDIA_TYPE allocated by DirectShow (DeleteMediaType)
        public static void DeleteMediaType(IntPtr pmt)
        {
            if (pmt == IntPtr.Zero) return;
            FreeMediaType(pmt);
            Marshal.FreeCoTaskMem(pmt);
        }

        public static string ReadBagString(IPropertyBag bag, string name)
        {
            object v = null;
            if (bag.Read(name, ref v, IntPtr.Zero) < 0) return null;
            return v as string;
        }

        public static string PinName(IPin pin)
        {
            IntPtr info = Marshal.AllocHGlobal(272);
            try
            {
                if (pin.QueryPinInfo(info) < 0) return "?";
                IntPtr filt = Marshal.ReadIntPtr(info, 0);
                if (filt != IntPtr.Zero) Marshal.Release(filt);
                return Marshal.PtrToStringUni(new IntPtr(info.ToInt64() + 12));
            }
            finally { Marshal.FreeHGlobal(info); }
        }

        public static string PinId(IPin pin)
        {
            IntPtr s;
            if (pin.QueryId(out s) < 0 || s == IntPtr.Zero) return "?";
            try { return Marshal.PtrToStringUni(s); } finally { Marshal.FreeCoTaskMem(s); }
        }

        // First pin of the filter with that direction (RCW; caller releases)
        public static IPin FindPin(IBaseFilter f, int dir)
        {
            IntPtr pe;
            MfHr.Check(f.EnumPins(out pe), "EnumPins");
            IEnumPins en = (IEnumPins)Marshal.GetObjectForIUnknown(pe);
            Marshal.Release(pe);
            try
            {
                while (true)
                {
                    IntPtr pp; uint got;
                    if (en.Next(1, out pp, out got) != 0 || got == 0 || pp == IntPtr.Zero) return null;
                    IPin pin = (IPin)Marshal.GetObjectForIUnknown(pp);
                    Marshal.Release(pp);
                    int d;
                    if (pin.QueryDirection(out d) >= 0 && d == dir) return pin;
                    MfApi.Release(pin);
                }
            }
            finally { MfApi.Release(en); }
        }
    }

    sealed class RawWaitHandle : WaitHandle
    {
        public RawWaitHandle(IntPtr h) { SafeWaitHandle = new SafeWaitHandle(h, false); }   // not owned: the graph closes it
    }
}

namespace ConsoleEnhancer.Motor.Capture
{
    [ComVisible(true)]
    public sealed class DsGrabberCallback : ISampleGrabberCB
    {
        readonly DsCapture owner;
        readonly int gen;
        internal DsGrabberCallback(DsCapture owner, int gen) { this.owner = owner; this.gen = gen; }
        public int SampleCB(double SampleTime, IntPtr pSample)
        {
            long qpc = Stopwatch.GetTimestamp();
            try { owner.OnSample(gen, pSample, qpc); } catch { }
            return 0;
        }
        public int BufferCB(double SampleTime, IntPtr pBuffer, int BufferLen) { return 0; }
    }

    public sealed class DsCapture : ICaptureSource
    {
        // ---- public surface (see ICaptureSource) ----
        FrameCallback onFrame;
        CaptureEventCallback onEvent;
        public FrameCallback OnFrame { get { return onFrame; } set { onFrame = value; } }
        public CaptureEventCallback OnEvent { get { return onEvent; } set { onEvent = value; } }

        public DsCapture(CaptureOptions options) { opt = (options ?? new CaptureOptions()).Clone(); }

        public CaptureOptions Options { get { return opt; } }
        public bool IsRunning { get { return thread != null && thread.IsAlive; } }
        public CaptureState State { get { lock (statLock) return stats.State; } }
        public CaptureMediaType CurrentType { get { return cur; } }
        public CaptureDeviceInfo CurrentDevice { get { return curDev; } }
        public FrameExtra CurrentFrame { get { return extra; } }
        public CaptureStats GetStats() { lock (statLock) return stats.Clone(); }
        public long ReportedLatency100ns { get { return latency100ns; } }   // IAMLatency of the capture pin (-1 = n/a)

        public void Start()
        {
            if (thread != null) throw new InvalidOperationException("DsCapture already started");
            stopEvent.Reset();
            thread = new Thread(ThreadMain);
            thread.Name = "DsCapture";
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.Highest;
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

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

        // DirectShow video input devices (FriendlyName + DevicePath as SymbolicLink).
        public static List<CaptureDeviceInfo> ListDevices(int timeoutMs = 8000)
        {
            return RunMta<List<CaptureDeviceInfo>>(delegate
            {
                List<CaptureDeviceInfo> res = new List<CaptureDeviceInfo>();
                ForEachDevice(delegate(CaptureDeviceInfo d, IDsMoniker m) { res.Add(d); return false; });
                return res;
            }, timeoutMs, "DsCapture.ListDevices");
        }

        // Every capability (IAMStreamConfig::GetStreamCaps) of the device's video output pin.
        public static List<CaptureMediaType> ListFormats(string deviceNameSubstring, string devicePath = null, int timeoutMs = 10000)
        {
            return RunMta<List<CaptureMediaType>>(delegate
            {
                CaptureDeviceInfo dev;
                IBaseFilter f = BindDevice(deviceNameSubstring, devicePath, out dev);
                if (f == null) throw new CaptureException("DirectShow device '" + (devicePath ?? deviceNameSubstring) + "' not found");
                IPin pin = null;
                IGraphBuilder g = null;
                try
                {
                    // ksproxy pins only expose IAMStreamConfig once the filter is in a graph
                    g = (IGraphBuilder)DsApi.CoCreate(DsApi.CLSID_FilterGraph);
                    MfHr.Check(g.AddFilter(f, "Capture"), "AddFilter(capture)");
                    string pinName;
                    pin = FindVideoOutputPin(f, out pinName);
                    if (pin == null) throw new CaptureException("no video output pin with IAMStreamConfig");
                    List<CaptureMediaType> res = new List<CaptureMediaType>();
                    foreach (DsType t in EnumCaps((IAMStreamConfig)pin)) res.Add(t.Info);
                    return res;
                }
                finally
                {
                    MfApi.Release(pin);
                    if (g != null) { try { g.RemoveFilter(f); } catch { } }
                    MfApi.Release(g);
                    MfApi.Release(f);
                }
            }, timeoutMs, "DsCapture.ListFormats");
        }

        // ================================================================================================================
        // implementation
        // ================================================================================================================
        sealed class DsType
        {
            public CaptureMediaType Info;
            public int CapsIndex;
            public long DefaultInterval, MinInterval, MaxInterval;
            public int Stride;           // bytes per row (positive)
            public bool BottomUp;
            public Guid FormatTypeGuid;
        }

        struct Pending { public IntPtr Sample; public long Qpc; }

        readonly CaptureOptions opt;
        Thread thread;
        readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        readonly AutoResetEvent sampleEvent = new AutoResetEvent(false);
        readonly object qLock = new object();
        readonly Queue<Pending> queue = new Queue<Pending>();
        int gen;
        bool accepting;

        readonly object statLock = new object();
        readonly CaptureStats stats = new CaptureStats();
        readonly object bookLock = new object();     // per-sample bookkeeping (streaming thread vs capture thread)

        // graph (capture thread)
        IGraphBuilder graph;
        IBaseFilter capFilter, grabFilter, nullFilter;
        IPin capOut, grabIn, grabOut, nullIn;
        // 23-11 (1.2.4, SINMPV): MJPEG (las capturadoras USB 2.0 tipo MS2109 a 1080p, antes por mpv): el "MJPEG Decompressor" de Windows
        // (quartz.dll) entre la capturadora y el Sample Grabber, que recibe RGB32 (o lo que el decompresor de: RGB24 / YUY2)
        IBaseFilter decFilter;
        IPin decIn, decOut;
        static readonly Guid CLSID_MjpegDec = new Guid("301056D0-6DFF-11D2-9EEB-006008039E37");
        ISampleGrabber grabber;
        IReferenceClock clock;
        DsGrabberCallback callback;
        RawWaitHandle graphEvent;
        WaitHandle[] waitSet, idleSet;   // {stop, sample, graph events} while open / {stop, sample}
        bool haveTStart;
        long tStart, clockToQpc100;

        volatile CaptureMediaType cur;
        volatile CaptureDeviceInfo curDev;
        volatile DsType curDs;
        long frameDur100ns;
        long lastTs = long.MinValue;
        long lastSampleQpc, openedQpc;
        bool gotFirst, noSignal;
        long sequence;
        FrameExtra extra;
        int backoffMs, silentReopenMs;
        long reopenAtQpc;
        long latency100ns = -1;
        long lastDropPollQpc;

        static long Now() { return Stopwatch.GetTimestamp(); }
        static long MsToTicks(long ms) { return ms * Stopwatch.Frequency / 1000; }
        static long TicksToMs(long t) { return t * 1000 / Stopwatch.Frequency; }

        void Raise(CaptureEvent ev, string detail)
        {
            CaptureEventCallback cb = onEvent;
            if (cb == null) return;
            try { cb(ev, detail); }
            catch { lock (statLock) stats.CallbackExceptions++; }
        }

        void SetState(CaptureState s) { lock (statLock) stats.State = s; }

        void ThreadMain()
        {
            IntPtr mmcss = IntPtr.Zero;
            try
            {
                if (opt.UseMmcss) { try { uint idx = 0; mmcss = MfApi.AvSetMmThreadCharacteristicsW("Capture", ref idx); } catch { } }
                backoffMs = 0; reopenAtQpc = 0; noSignal = false;
                silentReopenMs = opt.ReopenOnSilenceMs;
                idleSet = new WaitHandle[] { stopEvent, sampleEvent };

                while (!stopEvent.WaitOne(0))
                {
                    if (graph == null)
                    {
                        long now = Now();
                        if (reopenAtQpc != 0 && now < reopenAtQpc)
                        {
                            if (stopEvent.WaitOne((int)Math.Max(1, TicksToMs(reopenAtQpc - now)))) break;
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
                    }

                    long silentMs;
                    lock (bookLock) silentMs = TicksToMs(Now() - (gotFirst ? lastSampleQpc : openedQpc));
                    long waitMs = 250;
                    if (!noSignal) waitMs = Math.Min(waitMs, Math.Max(1, opt.NoSignalMs - silentMs));
                    if (silentReopenMs > 0) waitMs = Math.Min(waitMs, Math.Max(1, silentReopenMs - silentMs));
                    WaitHandle[] handles = waitSet ?? idleSet;
                    int w = WaitHandle.WaitAny(handles, (int)waitMs);
                    if (w == 0) break;
                    if (w == 1) { DrainQueue(); PollDropped(false); continue; }
                    if (w == 2) { HandleGraphEvents(); continue; }

                    // timeout: driver drop counters + silence checks
                    PollDropped(false);
                    bool raiseNoSignal = false;
                    lock (bookLock)
                    {
                        silentMs = TicksToMs(Now() - (gotFirst ? lastSampleQpc : openedQpc));
                        if (!noSignal && silentMs >= opt.NoSignalMs) { noSignal = true; raiseNoSignal = true; }
                    }
                    if (raiseNoSignal)
                    {
                        SetState(CaptureState.NoSignal);
                        lock (statLock) stats.NoSignalEvents++;
                        Raise(CaptureEvent.NoSignal, "no sample for " + silentMs + " ms" + (gotFirst ? "" : " since open"));
                    }
                    if (silentReopenMs > 0 && silentMs >= silentReopenMs)
                    {
                        Raise(CaptureEvent.Reopening, "silent for " + silentMs + " ms");
                        Close();
                        SetState(CaptureState.Reopening);
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
                  opt.FpsNum + "/" + opt.FpsDen + " pref " + PrefText());
            CaptureDeviceInfo dev;
            capFilter = BindDevice(opt.DeviceName, opt.SymbolicLink, out dev);
            if (capFilter == null) throw new CaptureException("DirectShow device '" + (opt.SymbolicLink ?? opt.DeviceName) + "' not found");
            curDev = dev;

            graph = (IGraphBuilder)DsApi.CoCreate(DsApi.CLSID_FilterGraph);
            MfHr.Check(graph.AddFilter(capFilter, "Capture"), "AddFilter(capture)");
            string pinName;
            capOut = FindVideoOutputPin(capFilter, out pinName);
            if (capOut == null) throw new CaptureException("no video output pin with IAMStreamConfig");
            IAMStreamConfig sc = (IAMStreamConfig)capOut;

            List<DsType> types = EnumCaps(sc);
            long want = opt.FpsNum > 0 ? (long)Math.Round(10000000.0 * opt.FpsDen / opt.FpsNum) : 166833;
            DsType best = Choose(types, want);
            if (best == null)
            {
                StringBuilder sb = new StringBuilder();
                foreach (DsType t in types)
                    if (t.Info.Width == opt.Width && t.Info.Height == opt.Height)
                        sb.Append(sb.Length > 0 ? ", " : "").Append(t.Info.SubtypeName + "@" + t.Info.FpsMin.ToString("0.##", CultureInfo.InvariantCulture) +
                                  "-" + t.Info.FpsMax.ToString("0.##", CultureInfo.InvariantCulture));
                throw new CaptureException("no " + opt.Width + "x" + opt.Height + " capability in [" + PrefText() + "]; offered at that size: " +
                                           (sb.Length > 0 ? sb.ToString() : "none") + " (" + types.Count + " caps)");
            }

            IntPtr pmt = IntPtr.Zero, scc = Marshal.AllocHGlobal(256), gmt = IntPtr.Zero, cmt = IntPtr.Zero;
            try
            {
                MfHr.Check(sc.GetStreamCaps(best.CapsIndex, out pmt, scc), "GetStreamCaps");
                AM_MEDIA_TYPE mt = (AM_MEDIA_TYPE)Marshal.PtrToStructure(pmt, typeof(AM_MEDIA_TYPE));
                // 21-11 (1.2.1): fuera del margen de ese formato (p. ej. 120 fps a 4K en la GC573, que a 4K da 24-60), lo mas cercano que da
                // (su minimo o su maximo) en vez del de serie del formato
                long interval = (want >= best.MinInterval - 2 && want <= best.MaxInterval + 2) ? want :
                                best.MinInterval > 0 && want < best.MinInterval ? best.MinInterval :
                                best.MaxInterval > 0 && want > best.MaxInterval ? best.MaxInterval : best.DefaultInterval;
                if (interval != want && want > 0 && interval > 0)
                    Raise(CaptureEvent.Opening, "no " + opt.Width + "x" + opt.Height + " @ " + (10000000.0 / want).ToString("0.##", CultureInfo.InvariantCulture) +
                          " fps: the closest it offers, " + best.Info.Width + "x" + best.Info.Height + " @ " + (10000000.0 / interval).ToString("0.##", CultureInfo.InvariantCulture));
                if (mt.pbFormat != IntPtr.Zero && mt.cbFormat >= 48) Marshal.WriteInt64(mt.pbFormat, 40, interval);   // AvgTimePerFrame (VIH and VIH2)
                MfHr.Check(sc.SetFormat(pmt), "IAMStreamConfig.SetFormat(" + best.Info.SubtypeName + " " + best.Info.Width + "x" + best.Info.Height + ")");

                // Sample Grabber: accepts exactly this major/subtype/format type; callback mode, no internal buffer copy
                // (23-11, SINMPV: con MJPEG, lo que sale del decompresor: RGB32, de tamaño fijo)
                bool mjpg = best.Info.Format == CaptureFormat.MJPG;
                grabFilter = (IBaseFilter)DsApi.CoCreate(DsApi.CLSID_SampleGrabber);
                grabber = (ISampleGrabber)grabFilter;
                AM_MEDIA_TYPE g = new AM_MEDIA_TYPE();
                g.majortype = DsApi.MEDIATYPE_Video; g.subtype = mjpg ? DsApi.MEDIASUBTYPE_RGB32 : mt.subtype; g.formattype = mjpg ? DsApi.FORMAT_VideoInfo : mt.formattype;
                g.bFixedSizeSamples = mjpg ? 1 : mt.bFixedSizeSamples; g.lSampleSize = mjpg ? (uint)(best.Info.Width * best.Info.Height * 4) : mt.lSampleSize;
                gmt = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(AM_MEDIA_TYPE)));
                Marshal.StructureToPtr(g, gmt, false);
                MfHr.Check(grabber.SetMediaType(gmt), "ISampleGrabber.SetMediaType");
                MfHr.Check(grabber.SetBufferSamples(0), "SetBufferSamples");
                MfHr.Check(grabber.SetOneShot(0), "SetOneShot");
                int cbGen;
                lock (qLock) cbGen = gen;
                callback = new DsGrabberCallback(this, cbGen);
                MfHr.Check(grabber.SetCallback(callback, 0), "ISampleGrabber.SetCallback");
                MfHr.Check(graph.AddFilter(grabFilter, "Grabber"), "AddFilter(grabber)");
                nullFilter = (IBaseFilter)DsApi.CoCreate(DsApi.CLSID_NullRenderer);
                MfHr.Check(graph.AddFilter(nullFilter, "Null"), "AddFilter(null renderer)");
                grabIn = DsApi.FindPin(grabFilter, DsApi.PINDIR_INPUT);
                grabOut = DsApi.FindPin(grabFilter, DsApi.PINDIR_OUTPUT);
                nullIn = DsApi.FindPin(nullFilter, DsApi.PINDIR_INPUT);
                if (grabIn == null || grabOut == null || nullIn == null) throw new CaptureException("grabber/null renderer pins not found");
                if (mjpg)
                {
                    // capturadora (MJPEG) -> MJPEG Decompressor -> grabber (RGB32: el decompresor lo da sin convertidores de por medio)
                    decFilter = (IBaseFilter)DsApi.CoCreate(CLSID_MjpegDec);
                    MfHr.Check(graph.AddFilter(decFilter, "MJPEG"), "AddFilter(MJPEG Decompressor)");
                    decIn = DsApi.FindPin(decFilter, DsApi.PINDIR_INPUT);
                    decOut = DsApi.FindPin(decFilter, DsApi.PINDIR_OUTPUT);
                    if (decIn == null || decOut == null) throw new CaptureException("MJPEG Decompressor pins not found");
                    MfHr.Check(graph.ConnectDirect(capOut, decIn, pmt), "ConnectDirect(capture MJPG -> MJPEG Decompressor)");
                    int ch = graph.ConnectDirect(decOut, grabIn, IntPtr.Zero);
                    if (ch < 0)
                    {
                        // (si no da RGB32 directo: lo que acepte, YUY2 / RGB24, con el grabber sin subtipo fijo)
                        AM_MEDIA_TYPE g2 = new AM_MEDIA_TYPE(); g2.majortype = DsApi.MEDIATYPE_Video;
                        Marshal.StructureToPtr(g2, gmt, false);
                        MfHr.Check(grabber.SetMediaType(gmt), "ISampleGrabber.SetMediaType(video)");
                        MfHr.Check(graph.ConnectDirect(decOut, grabIn, IntPtr.Zero), "ConnectDirect(MJPEG Decompressor -> grabber)");
                    }
                    Raise(CaptureEvent.Opening, "MJPEG: decompressed by the Windows MJPEG Decompressor");
                }
                else MfHr.Check(graph.ConnectDirect(capOut, grabIn, pmt), "ConnectDirect(capture -> grabber)");
                MfHr.Check(graph.ConnectDirect(grabOut, nullIn, IntPtr.Zero), "ConnectDirect(grabber -> null)");

                // what was really connected
                cmt = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(AM_MEDIA_TYPE)));
                for (int i = 0; i < Marshal.SizeOf(typeof(AM_MEDIA_TYPE)); i += 8) Marshal.WriteInt64(cmt, i, 0);
                DsType ct = null;
                if (grabber.GetConnectedMediaType(cmt) >= 0) ct = ParseVideoType(cmt, IntPtr.Zero, best.CapsIndex);
                if (ct == null || ct.Info.Width == 0) { if (mjpg) throw new CaptureException("MJPEG Decompressor: unknown output type"); ct = best; }
                if (mjpg) { ct.DefaultInterval = best.DefaultInterval; ct.MinInterval = best.MinInterval; ct.MaxInterval = best.MaxInterval; }
                ct.Info.FpsMin = best.Info.FpsMin; ct.Info.FpsMax = best.Info.FpsMax;
                ApplyType(ct);
            }
            finally
            {
                Marshal.FreeHGlobal(scc);
                DsApi.DeleteMediaType(pmt);
                if (gmt != IntPtr.Zero) Marshal.FreeHGlobal(gmt);
                if (cmt != IntPtr.Zero) { DsApi.FreeMediaType(cmt); Marshal.FreeHGlobal(cmt); }
            }

            // latency the driver reports, drop counters
            latency100ns = -1;
            IAMLatency lat = capOut as IAMLatency;
            long l;
            if (lat != null && lat.GetLatency(out l) >= 0) latency100ns = l;
            lock (statLock) { stats.DriverDropped = -1; stats.DriverNotDropped = -1; }

            // clock: keep the graph clock (timestamps), but the null renderer gets none so it never waits for one
            IMediaFilter gmf = (IMediaFilter)graph;
            graph.SetDefaultSyncSource();
            IntPtr clk;
            if (gmf.GetSyncSource(out clk) >= 0 && clk == IntPtr.Zero)
            {
                object sys = DsApi.CoCreate(DsApi.CLSID_SystemClock);
                IntPtr sp = Marshal.GetComInterfaceForObject(sys, typeof(IReferenceClock));
                gmf.SetSyncSource(sp);
                Marshal.Release(sp);
                MfApi.Release(sys);
                gmf.GetSyncSource(out clk);
            }
            if (clk != IntPtr.Zero) { clock = (IReferenceClock)Marshal.GetObjectForIUnknown(clk); Marshal.Release(clk); }
            nullFilter.SetSyncSource(IntPtr.Zero);
            haveTStart = false;
            if (clock != null) clockToQpc100 = CalibrateClock(clock);

            IMediaEvent me = (IMediaEvent)graph;
            IntPtr hev;
            if (me.GetEventHandle(out hev) >= 0 && hev != IntPtr.Zero)
            {
                graphEvent = new RawWaitHandle(hev);
                waitSet = new WaitHandle[] { stopEvent, sampleEvent, graphEvent };
            }

            lock (qLock) accepting = true;
            lock (bookLock) { gotFirst = false; lastTs = long.MinValue; openedQpc = Now(); }
            IMediaControl mc = (IMediaControl)graph;
            int hr = mc.Pause();
            if (hr < 0) throw new CaptureException("IMediaControl.Pause failed: " + MfHr.Describe(hr), hr);
            long tRun;
            if (clock != null && clock.GetTime(out tRun) >= 0 && gmf.Run(tRun) >= 0) { tStart = tRun; haveTStart = true; }
            else
            {
                hr = mc.Run();
                if (hr < 0) throw new CaptureException("IMediaControl.Run failed: " + MfHr.Describe(hr), hr);
            }
            lock (bookLock) openedQpc = Now();
            lock (statLock) { stats.Opens++; stats.State = noSignal ? CaptureState.NoSignal : CaptureState.WaitingForFirstFrame; }
            Raise(CaptureEvent.Opened, cur + " pin '" + pinName + "' on " + dev.FriendlyName +
                  (latency100ns >= 0 ? ", driver latency " + (latency100ns / 10000.0).ToString("0.00", CultureInfo.InvariantCulture) + " ms" : ", no IAMLatency") +
                  ((capOut as IAMDroppedFrames) != null || (capFilter as IAMDroppedFrames) != null ? "" : ", no IAMDroppedFrames") +
                  (haveTStart ? "" : ", no tStart (delay unknown)"));
        }

        string PrefText() { return string.Join(",", Array.ConvertAll(opt.Preference, delegate(CaptureFormat f) { return f.ToString(); })); }

        void ApplyType(DsType t)
        {
            curDs = t;
            cur = t.Info;
            frameDur100ns = t.DefaultInterval > 0 ? t.DefaultInterval : 0;
        }

        DsType Choose(List<DsType> types, long want)
        {
            LastSizeSwap = null;
            // 22-11 (1.2.2, UNIVERSAL): primero un formato de la preferencia que admita ESA velocidad en ese tamaño; antes el primero de la
            // preferencia aunque fuera a otra (con format=auto: RGB24 a 4K a 30 antes que NV12 a 4K a 60, o un YUY2 a 5 fps)
            foreach (CaptureFormat f in opt.Preference)
            {
                DsType t = ChooseAt(types, f, opt.Width, opt.Height, want);
                if (t != null && want >= t.MinInterval - 2 && want <= t.MaxInterval + 2) return t;
            }
            foreach (CaptureFormat f in opt.Preference)
            {
                DsType t = ChooseAt(types, f, opt.Width, opt.Height, want);
                if (t != null) return t;
            }
            // 21-11 (1.2.1): ese tamaño no lo da la capturadora (antes: "no WxH capability", el motor sin imagen y el lanzador lo cerraba a
            // los 30 s contandolo como fallo): el tamaño mas parecido que da (el mismo aspecto antes que otro; luego el area mas cercana, uno
            // mas grande antes que uno mas pequeño: reducir pierde menos que agrandar), con el formato de la preferencia y la velocidad mas cercana
            foreach (CaptureFormat f in opt.Preference)
            {
                DsType near = null;
                long bestScore = long.MaxValue;
                long wantArea = (long)opt.Width * opt.Height;
                foreach (DsType t in types)
                {
                    if (t.Info.Format != f || t.Info.Width <= 0 || t.Info.Height <= 0) continue;
                    long aspect = Math.Abs((long)t.Info.Width * opt.Height - (long)t.Info.Height * opt.Width) > wantArea / 50 ? 1L << 40 : 0;
                    long area = (long)t.Info.Width * t.Info.Height;
                    long score = aspect + (area >= wantArea ? area - wantArea : 2 * (wantArea - area));
                    if (score < bestScore) { bestScore = score; near = t; }
                }
                if (near == null) continue;
                DsType r = ChooseAt(types, f, near.Info.Width, near.Info.Height, want);
                if (r == null) continue;
                LastSizeSwap = opt.Width + "x" + opt.Height + ">" + r.Info.Width + "x" + r.Info.Height;
                Raise(CaptureEvent.Opening, "no " + opt.Width + "x" + opt.Height + " " + f + " capability: the closest one offered, " + r.Info.Width + "x" + r.Info.Height);
                return r;
            }
            return null;
        }

        // 21-11 (1.2.1): el ultimo cambio de tamaño por no darlo la capturadora ("pedido>elegido"; null = ninguno). El motor lo usa para dar
        // por hecho un cambio de captura cuyas imagenes llegan con el tamaño elegido (EngineMpv.cs, FrameHook)
        public static volatile string LastSizeSwap;

        // En ese tamaño y formato: el que admite esa velocidad o, si ninguno, el de velocidad de serie mas cercana
        static DsType ChooseAt(List<DsType> types, CaptureFormat f, int w, int h, long want)
        {
            DsType inRange = null, closest = null;
            long bestDiff = long.MaxValue;
            foreach (DsType t in types)
            {
                if (t.Info.Format != f || t.Info.Width != w || t.Info.Height != h) continue;
                if (inRange == null && want >= t.MinInterval - 2 && want <= t.MaxInterval + 2) inRange = t;
                long d = Math.Abs(t.DefaultInterval - want);
                if (d < bestDiff) { bestDiff = d; closest = t; }
            }
            return inRange ?? closest;
        }

        // Offset QPC(100 ns) - reference clock, taken at a clock tick edge (best of a few edges, <= ~60 ms)
        static long CalibrateClock(IReferenceClock c)
        {
            long best = long.MaxValue;
            long deadline = Now() + MsToTicks(60);
            for (int e = 0; e < 5 && Now() < deadline; e++)
            {
                long c0, c1, q = 0;
                if (c.GetTime(out c0) < 0) return 0;
                c1 = c0;
                while (Now() < deadline)
                {
                    c.GetTime(out c1);
                    q = MfCapture.QpcTo100ns(Now());
                    if (c1 != c0) break;
                }
                if (c1 != c0 && q - c1 < best) best = q - c1;
            }
            return best == long.MaxValue ? 0 : best;
        }

        // ---- samples ----
        internal void OnSample(int cbGen, IntPtr pSample, long qpc)
        {
            if (pSample == IntPtr.Zero) return;
            if (opt.DeliverOnSourceThread)
            {
                lock (qLock) { if (cbGen != gen || !accepting) return; }
                ProcessSample(pSample, qpc);
                return;
            }
            lock (qLock)
            {
                if (cbGen != gen || !accepting) return;
                Marshal.AddRef(pSample);
                Pending p; p.Sample = pSample; p.Qpc = qpc;
                queue.Enqueue(p);
            }
            sampleEvent.Set();
        }

        void DrainQueue()
        {
            while (true)
            {
                Pending p; bool more;
                lock (qLock)
                {
                    if (queue.Count == 0) return;
                    p = queue.Dequeue();
                    more = queue.Count > 0;
                }
                try
                {
                    if (more) { lock (statLock) stats.LateDropped++; continue; }   // newer frame waiting: latency first
                    ProcessSample(p.Sample, p.Qpc);
                }
                finally { Marshal.Release(p.Sample); }
            }
        }

        void ProcessSample(IntPtr pSample, long qpc)
        {
            object rcw = null;
            try
            {
                rcw = Marshal.GetObjectForIUnknown(pSample);
                IMediaSample s = (IMediaSample)rcw;

                IntPtr nmt;
                if (s.GetMediaType(out nmt) == 0 && nmt != IntPtr.Zero)
                {
                    try
                    {
                        DsType nt = ParseVideoType(nmt, IntPtr.Zero, -1);
                        if (nt != null && nt.Info.Width > 0) { nt.Info.FpsMin = cur.FpsMin; nt.Info.FpsMax = cur.FpsMax; ApplyType(nt); }
                    }
                    finally { DsApi.DeleteMediaType(nmt); }
                    lock (statLock) stats.FormatChanges++;
                    Raise(CaptureEvent.FormatChanged, "sample media type -> " + cur);
                    lock (bookLock) lastTs = long.MinValue;
                }

                long st = 0, en = 0;
                int hrT = s.GetTime(out st, out en);
                bool hasTime = hrT >= 0;
                bool hasEnd = hrT == 0;
                bool disc = s.IsDiscontinuity() == 0;
                IntPtr buf;
                MfHr.Check(s.GetPointer(out buf), "IMediaSample.GetPointer");
                int len = s.GetActualDataLength();
                // zero-length samples (seen when another graph on the same pin stops): not a frame, not signal, not an error
                if (len == 0) { lock (statLock) stats.EmptySamples++; return; }

                bool raiseRestored = false, raiseFirst = false; long firstMs = 0;
                long gapLost = 0; double gapMs = 0;
                lock (bookLock)
                {
                    lastSampleQpc = qpc;
                    if (noSignal) { noSignal = false; raiseRestored = true; }
                    if (!gotFirst) { gotFirst = true; raiseFirst = true; firstMs = TicksToMs(qpc - openedQpc); }
                    if (hasTime)
                    {
                        if (lastTs != long.MinValue)
                        {
                            long d = st - lastTs;
                            if (d <= 0) { lock (statLock) stats.DuplicateTimestamps++; }
                            else if (frameDur100ns > 0 && d > frameDur100ns * 3 / 2)
                            {
                                gapLost = Math.Max(1, (long)Math.Round((double)d / frameDur100ns) - 1);
                                gapMs = d / 10000.0;
                                lock (statLock) { stats.Gaps++; stats.EstimatedLost += gapLost; }
                            }
                        }
                        lastTs = st;
                    }
                }
                backoffMs = 0;
                silentReopenMs = opt.ReopenOnSilenceMs;
                if (raiseRestored) Raise(CaptureEvent.SignalRestored, "");
                if (raiseFirst) Raise(CaptureEvent.FirstFrame, firstMs + " ms after Run");
                if (gapLost > 0) Raise(CaptureEvent.Gap, gapMs.ToString("0.00", CultureInfo.InvariantCulture) + " ms, ~" + gapLost + " lost");
                lock (statLock) { if (stats.State != CaptureState.Running) stats.State = CaptureState.Running; }
                if (disc) lock (statLock) stats.Discontinuities++;

                DsType t = curDs;
                CaptureMediaType info = t.Info;
                int h = info.Height, w = info.Width;
                long need = (long)t.Stride * (long)Math.Ceiling(h * MfCapture.PlaneHeightFactor(info.Format));
                if (buf == IntPtr.Zero || len < need)
                {
                    lock (statLock) stats.Errors++;
                    Raise(CaptureEvent.Error, "sample " + len + " bytes < expected " + need + " (stride " + t.Stride + ")");
                    return;
                }
                IntPtr top = t.BottomUp ? new IntPtr(buf.ToInt64() + (long)t.Stride * (h - 1)) : buf;
                int pitch = t.BottomUp ? -t.Stride : t.Stride;

                FrameExtra fx = new FrameExtra();
                fx.BufferLength = len;
                fx.Discontinuity = disc;
                if (hasEnd) fx.SampleDuration100ns = en - st;
                if (haveTStart && hasTime && clock != null)
                {
                    fx.HasClockDelay = true;
                    fx.SampleTimeQpc100ns = tStart + st + clockToQpc100;
                    fx.ClockDelay100ns = MfCapture.QpcTo100ns(qpc) - fx.SampleTimeQpc100ns;
                }
                long seq = Interlocked.Increment(ref sequence) - 1;
                fx.DeliverQpc = Now();
                extra = fx;
                lock (statLock) { stats.Frames++; stats.LastArrivalQpc = qpc; }
                FrameCallback cb = onFrame;
                if (cb != null)
                {
                    try { cb(top, pitch, w, h, info.Format, qpc, hasTime ? st : 0, seq); }
                    catch { lock (statLock) stats.CallbackExceptions++; }
                }
            }
            catch (Exception ex)
            {
                lock (statLock) stats.Errors++;
                Raise(CaptureEvent.Error, "sample: " + ex.Message);
            }
            finally { MfApi.Release(rcw); }
        }

        void HandleGraphEvents()
        {
            IMediaEvent me = graph as IMediaEvent;
            if (me == null) return;
            int code; IntPtr p1, p2;
            while (graph != null && me.GetEvent(out code, out p1, out p2, 0) == 0)
            {
                me.FreeEventParams(code, p1, p2);
                string what = null;
                bool reopen = false;
                switch (code)
                {
                    case DsApi.EC_DEVICE_LOST: what = p2 == IntPtr.Zero ? "EC_DEVICE_LOST (removed)" : "EC_DEVICE_LOST (available again)"; reopen = true; break;
                    case DsApi.EC_ERRORABORT: what = "EC_ERRORABORT " + MfHr.Describe(p1.ToInt32()); reopen = true; break;
                    case DsApi.EC_ERRORABORTEX: what = "EC_ERRORABORTEX " + MfHr.Describe(p1.ToInt32()); reopen = true; break;
                    case DsApi.EC_STREAM_ERROR_STOPPED: what = "EC_STREAM_ERROR_STOPPED " + MfHr.Describe(p1.ToInt32()); reopen = true; break;
                    case DsApi.EC_STREAM_ERROR_STILLPLAYING: what = "EC_STREAM_ERROR_STILLPLAYING " + MfHr.Describe(p1.ToInt32()); break;
                    case DsApi.EC_ERROR_STILLPLAYING: what = "EC_ERROR_STILLPLAYING " + MfHr.Describe(p1.ToInt32()); break;
                    case DsApi.EC_USERABORT: what = "EC_USERABORT"; reopen = true; break;
                    case DsApi.EC_COMPLETE:
                        Raise(CaptureEvent.EndOfStream, "EC_COMPLETE");
                        Close(); ScheduleReopen();
                        return;
                    case DsApi.EC_VIDEO_SIZE_CHANGED:
                        lock (statLock) stats.FormatChanges++;
                        Raise(CaptureEvent.FormatChanged, "EC_VIDEO_SIZE_CHANGED " + (p1.ToInt64() & 0xFFFF) + "x" + ((p1.ToInt64() >> 16) & 0xFFFF));
                        break;
                }
                if (what != null)
                {
                    lock (statLock) stats.Errors++;
                    Raise(CaptureEvent.Error, what);
                }
                if (reopen) { Close(); ScheduleReopen(); return; }
            }
        }

        void PollDropped(bool force)
        {
            long now = Now();
            if (!force && now - lastDropPollQpc < Stopwatch.Frequency) return;
            lastDropPollQpc = now;
            IAMDroppedFrames df = (capOut as IAMDroppedFrames) ?? (capFilter as IAMDroppedFrames);
            if (df == null) return;
            int d, nd;
            if (df.GetNumDropped(out d) >= 0 && df.GetNumNotDropped(out nd) >= 0)
                lock (statLock) { stats.DriverDropped = d; stats.DriverNotDropped = nd; }
        }

        void Close()
        {
            lock (qLock)
            {
                accepting = false;
                gen++;
                while (queue.Count > 0) Marshal.Release(queue.Dequeue().Sample);
            }
            bool hadGraph = graph != null;
            if (graph != null)
            {
                try { PollDropped(true); } catch { }
                try { ((IMediaControl)graph).Stop(); } catch { }
            }
            if (grabber != null) { try { grabber.SetCallback(null, 0); } catch { } }
            if (graph != null)
            {
                IBaseFilter[] fs = new IBaseFilter[] { nullFilter, grabFilter, decFilter, capFilter };
                foreach (IBaseFilter f in fs) if (f != null) { try { graph.RemoveFilter(f); } catch { } }
            }
            graphEvent = null;   // handle belongs to the graph
            waitSet = null;
            MfApi.Release(clock); clock = null;
            MfApi.Release(nullIn); nullIn = null;
            MfApi.Release(grabOut); grabOut = null;
            MfApi.Release(grabIn); grabIn = null;
            MfApi.Release(capOut); capOut = null;
            MfApi.Release(decIn); decIn = null;
            MfApi.Release(decOut); decOut = null;
            MfApi.Release(decFilter); decFilter = null;
            MfApi.Release(nullFilter); nullFilter = null;
            MfApi.Release(grabFilter); grabFilter = null; grabber = null;
            MfApi.Release(capFilter); capFilter = null;
            MfApi.Release(graph); graph = null;
            callback = null;
            haveTStart = false;
            if (hadGraph)
            {
                // safety net: an RCW not released by hand must not keep the device open. No WaitForPendingFinalizers:
                // it can block forever when the host has a non-pumping STA.
                GC.Collect();
            }
            lock (qLock) { while (queue.Count > 0) Marshal.Release(queue.Dequeue().Sample); }
        }

        // ---- enumeration helpers ----
        delegate bool DeviceVisitor(CaptureDeviceInfo d, IDsMoniker m);   // return true to stop; m is released afterwards

        static void ForEachDevice(DeviceVisitor visit)
        {
            ICreateDevEnum de = (ICreateDevEnum)DsApi.CoCreate(DsApi.CLSID_SystemDeviceEnum);
            IDsEnumMoniker em = null;
            try
            {
                Guid cat = DsApi.CLSID_VideoInputDeviceCategory;
                IntPtr pe;
                int hr = de.CreateClassEnumerator(ref cat, out pe, 0);
                if (hr != 0 || pe == IntPtr.Zero) return;   // S_FALSE: category empty
                em = (IDsEnumMoniker)Marshal.GetObjectForIUnknown(pe);
                Marshal.Release(pe);
                while (true)
                {
                    IntPtr pm; uint got;
                    if (em.Next(1, out pm, out got) != 0 || got == 0 || pm == IntPtr.Zero) break;
                    IDsMoniker m = (IDsMoniker)Marshal.GetObjectForIUnknown(pm);
                    Marshal.Release(pm);
                    try
                    {
                        CaptureDeviceInfo d = new CaptureDeviceInfo();
                        d.Category = DsApi.CLSID_VideoInputDeviceCategory;
                        Guid iid = DsApi.IID_IPropertyBag;
                        IntPtr pb;
                        if (m.BindToStorage(IntPtr.Zero, IntPtr.Zero, ref iid, out pb) >= 0 && pb != IntPtr.Zero)
                        {
                            IPropertyBag bag = (IPropertyBag)Marshal.GetObjectForIUnknown(pb);
                            Marshal.Release(pb);
                            d.FriendlyName = DsApi.ReadBagString(bag, "FriendlyName") ?? "";
                            d.SymbolicLink = DsApi.ReadBagString(bag, "DevicePath") ?? "";
                            MfApi.Release(bag);
                        }
                        if (visit(d, m)) return;
                    }
                    finally { MfApi.Release(m); }
                }
            }
            finally { MfApi.Release(em); MfApi.Release(de); }
        }

        static IBaseFilter BindDevice(string nameSub, string devicePath, out CaptureDeviceInfo dev)
        {
            IBaseFilter result = null;
            CaptureDeviceInfo found = null;
            ForEachDevice(delegate(CaptureDeviceInfo d, IDsMoniker m)
            {
                bool match = !string.IsNullOrEmpty(devicePath)
                    ? string.Equals(d.SymbolicLink, devicePath, StringComparison.OrdinalIgnoreCase)
                    : !string.IsNullOrEmpty(nameSub) && d.FriendlyName.IndexOf(nameSub, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match) return false;
                Guid iid = DsApi.IID_IBaseFilter;
                IntPtr pf;
                MfHr.Check(m.BindToObject(IntPtr.Zero, IntPtr.Zero, ref iid, out pf), "IMoniker.BindToObject(" + d.FriendlyName + ")");
                result = (IBaseFilter)Marshal.GetObjectForIUnknown(pf);
                Marshal.Release(pf);
                found = d;
                return true;    // first match wins; the moniker is released, the filter RCW stays
            });
            dev = found;
            return result;
        }

        // The output pin whose IAMStreamConfig caps are video (the GC573 also has two audio output pins: never touched).
        static IPin FindVideoOutputPin(IBaseFilter f, out string name)
        {
            name = null;
            IntPtr pe;
            MfHr.Check(f.EnumPins(out pe), "EnumPins");
            IEnumPins en = (IEnumPins)Marshal.GetObjectForIUnknown(pe);
            Marshal.Release(pe);
            try
            {
                while (true)
                {
                    IntPtr pp; uint got;
                    if (en.Next(1, out pp, out got) != 0 || got == 0 || pp == IntPtr.Zero) return null;
                    IPin pin = (IPin)Marshal.GetObjectForIUnknown(pp);
                    Marshal.Release(pp);
                    bool isVideo = false;
                    try
                    {
                        int dir;
                        IAMStreamConfig sc = pin as IAMStreamConfig;
                        if (pin.QueryDirection(out dir) >= 0 && dir == DsApi.PINDIR_OUTPUT && sc != null)
                        {
                            int n, sz;
                            if (sc.GetNumberOfCapabilities(out n, out sz) >= 0 && n > 0)
                            {
                                IntPtr scc = Marshal.AllocHGlobal(Math.Max(sz, 256));
                                IntPtr pmt;
                                try
                                {
                                    if (sc.GetStreamCaps(0, out pmt, scc) >= 0 && pmt != IntPtr.Zero)
                                    {
                                        AM_MEDIA_TYPE mt = (AM_MEDIA_TYPE)Marshal.PtrToStructure(pmt, typeof(AM_MEDIA_TYPE));
                                        isVideo = mt.majortype == DsApi.MEDIATYPE_Video;
                                        DsApi.DeleteMediaType(pmt);
                                    }
                                }
                                finally { Marshal.FreeHGlobal(scc); }
                            }
                        }
                        if (isVideo) { name = DsApi.PinName(pin) + "/" + DsApi.PinId(pin); return pin; }
                    }
                    finally { if (!isVideo) MfApi.Release(pin); }
                }
            }
            finally { MfApi.Release(en); }
        }

        static List<DsType> EnumCaps(IAMStreamConfig sc)
        {
            List<DsType> res = new List<DsType>();
            int n, sz;
            MfHr.Check(sc.GetNumberOfCapabilities(out n, out sz), "GetNumberOfCapabilities");
            IntPtr scc = Marshal.AllocHGlobal(Math.Max(sz, 256));
            try
            {
                for (int i = 0; i < n; i++)
                {
                    IntPtr pmt;
                    if (sc.GetStreamCaps(i, out pmt, scc) < 0 || pmt == IntPtr.Zero) continue;
                    try
                    {
                        DsType t = ParseVideoType(pmt, sz >= 128 ? scc : IntPtr.Zero, i);
                        if (t != null) res.Add(t);
                    }
                    finally { DsApi.DeleteMediaType(pmt); }
                }
            }
            finally { Marshal.FreeHGlobal(scc); }
            return res;
        }

        // AM_MEDIA_TYPE (+ optional VIDEO_STREAM_CONFIG_CAPS) -> DsType. VIH: AvgTimePerFrame @40, BITMAPINFOHEADER @48;
        // VIH2: AvgTimePerFrame @40, BITMAPINFOHEADER @72. Caps: MinFrameInterval @104, MaxFrameInterval @112.
        static DsType ParseVideoType(IntPtr pmt, IntPtr scc, int capsIndex)
        {
            AM_MEDIA_TYPE mt = (AM_MEDIA_TYPE)Marshal.PtrToStructure(pmt, typeof(AM_MEDIA_TYPE));
            if (mt.majortype != DsApi.MEDIATYPE_Video) return null;
            DsType t = new DsType();
            CaptureMediaType info = new CaptureMediaType();
            t.Info = info; t.CapsIndex = capsIndex; t.FormatTypeGuid = mt.formattype;
            info.Stream = 0; info.Index = capsIndex;
            info.Subtype = mt.subtype;
            info.Format = DsFormat(mt.subtype, out info.SubtypeName);
            info.Compressed = mt.bTemporalCompression != 0;
            int bmi;
            if (mt.formattype == DsApi.FORMAT_VideoInfo && mt.cbFormat >= 88) { bmi = 48; info.FormatType = "VIH"; }
            else if (mt.formattype == DsApi.FORMAT_VideoInfo2 && mt.cbFormat >= 112) { bmi = 72; info.FormatType = "VIH2"; }
            else { info.FormatType = mt.formattype.ToString("B"); return t; }
            IntPtr f = mt.pbFormat;
            int tl = Marshal.ReadInt32(f, 16), tt = Marshal.ReadInt32(f, 20), tr = Marshal.ReadInt32(f, 24), tb = Marshal.ReadInt32(f, 28);
            long avg = Marshal.ReadInt64(f, 40);
            int biWidth = Marshal.ReadInt32(f, bmi + 4);
            int biHeight = Marshal.ReadInt32(f, bmi + 8);
            int bitCount = Marshal.ReadInt16(f, bmi + 14);
            int sizeImage = Marshal.ReadInt32(f, bmi + 20);
            bool rgb = info.Format == CaptureFormat.RGB24 || info.Format == CaptureFormat.RGB32 || info.Format == CaptureFormat.ARGB32 ||
                       info.Format == CaptureFormat.RGB565 || info.Format == CaptureFormat.RGB555;
            info.Width = (tr > tl) ? tr - tl : biWidth;
            info.Height = (tb > tt) ? tb - tt : Math.Abs(biHeight);
            t.Stride = rgb ? ((biWidth * bitCount + 31) / 32) * 4 : MfCapture.RowBytes(info.Format, biWidth);
            t.BottomUp = rgb && biHeight > 0;     // YUV is always top-down
            info.HasDefaultStride = t.Stride > 0;
            info.DefaultStride = t.BottomUp ? -t.Stride : t.Stride;
            info.SampleSize = sizeImage > 0 ? sizeImage : (int)mt.lSampleSize;
            t.DefaultInterval = avg;
            t.MinInterval = t.MaxInterval = avg;
            if (scc != IntPtr.Zero)
            {
                long mn = Marshal.ReadInt64(scc, 104), mx = Marshal.ReadInt64(scc, 112);
                if (mn > 0 && mx >= mn) { t.MinInterval = mn; t.MaxInterval = mx; }
            }
            IntervalToRational(avg, out info.FpsNum, out info.FpsDen);
            info.FpsMax = t.MinInterval > 0 ? 1e7 / t.MinInterval : 0;
            info.FpsMin = t.MaxInterval > 0 ? 1e7 / t.MaxInterval : 0;
            return t;
        }

        static void IntervalToRational(long avg, out int num, out int den)
        {
            num = 0; den = 1;
            if (avg <= 0) return;
            int[] ntsc = { 24, 30, 48, 60, 120, 144, 240 };
            foreach (int k in ntsc)
            {
                double iv = 1e7 * 1001.0 / (k * 1000.0);
                if (Math.Abs(avg - iv) <= 1.0) { num = k * 1000; den = 1001; return; }
                double iv2 = 1e7 / k;
                if (Math.Abs(avg - iv2) <= 1.0) { num = k; den = 1; return; }
            }
            long g = Gcd(10000000, avg);
            num = (int)(10000000 / g); den = (int)(avg / g);
        }

        static long Gcd(long a, long b) { while (b != 0) { long t = a % b; a = b; b = t; } return a; }

        static CaptureFormat DsFormat(Guid sub, out string name)
        {
            if (sub == DsApi.MEDIASUBTYPE_RGB24) { name = "RGB24"; return CaptureFormat.RGB24; }
            if (sub == DsApi.MEDIASUBTYPE_RGB32) { name = "RGB32"; return CaptureFormat.RGB32; }
            if (sub == DsApi.MEDIASUBTYPE_ARGB32) { name = "ARGB32"; return CaptureFormat.ARGB32; }
            if (sub == DsApi.MEDIASUBTYPE_RGB565) { name = "RGB565"; return CaptureFormat.RGB565; }
            if (sub == DsApi.MEDIASUBTYPE_RGB555) { name = "RGB555"; return CaptureFormat.RGB555; }
            return MfCapture.FormatFromSubtype(sub, out name);     // FourCC subtypes (YUY2, NV12, P010, ...)
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
