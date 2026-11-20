// Dxgi.cs - DXGI 1.0..1.6 COM interop for Switch2Engine (C# 5, .NET Framework 4.8, x64 only).
// No dxgi.h on disk: vtables follow dxgi.h / dxgi1_2.h .. dxgi1_6.h (IDXGIObject(4) -> IDXGIFactory(5) -> 1(2) ->
// 2(11) -> 3(1) -> 4(2) -> 5(1) -> 6(1) -> 7(2); IDXGIDeviceSubObject(1) -> IDXGISwapChain(10) -> 1(11) -> 2(7) ->
// 3(4) -> 4(1)); IDXGISwapChain4 and IDXGIFactory7 were cross-checked against the Streamline interposer proxies
// (deps\streamline\source\core\sl.interposer\dxgi). Same conventions as D3D12.cs (no C# interface inheritance,
// every method [PreserveSig], riid/void** -> out object, optional pointers -> T*).
// DXGI_FORMAT is declared in D3D12.cs. Note IDXGIObject's order differs from ID3D12Object's
// (SetPrivateData, SetPrivateDataInterface, GetPrivateData, GetParent).
using System;
using System.Runtime.InteropServices;
using System.Security;

namespace ConsoleEnhancer.Motor.Native
{
    // ------------------------------------------------------------------------------------------------------------
    // Enums / constants
    // ------------------------------------------------------------------------------------------------------------
    public enum DXGI_SWAP_EFFECT : int
    {
        DISCARD = 0,
        SEQUENTIAL = 1,
        FLIP_SEQUENTIAL = 3,
        FLIP_DISCARD = 4,
    }

    [Flags]
    public enum DXGI_SWAP_CHAIN_FLAG : uint
    {
        NONE = 0,
        NONPREROTATED = 1,
        ALLOW_MODE_SWITCH = 2,
        GDI_COMPATIBLE = 4,
        RESTRICTED_CONTENT = 8,
        RESTRICT_SHARED_RESOURCE_DRIVER = 16,
        DISPLAY_ONLY = 32,
        FRAME_LATENCY_WAITABLE_OBJECT = 64,
        FOREGROUND_LAYER = 128,
        FULLSCREEN_VIDEO = 256,
        YUV_VIDEO = 512,
        HW_PROTECTED = 1024,
        ALLOW_TEARING = 2048,
        RESTRICTED_TO_ALL_HOLOGRAPHIC_DISPLAYS = 4096,
    }

    [Flags]
    public enum DXGI_PRESENT : uint
    {
        NONE = 0,
        TEST = 0x1,
        DO_NOT_SEQUENCE = 0x2,
        RESTART = 0x4,
        DO_NOT_WAIT = 0x8,
        STEREO_PREFER_RIGHT = 0x10,
        STEREO_TEMPORARY_MONO = 0x20,
        RESTRICT_TO_OUTPUT = 0x40,
        USE_DURATION = 0x100,
        ALLOW_TEARING = 0x200,
    }

    [Flags]
    public enum DXGI_USAGE : uint
    {
        NONE = 0,
        CPU_ACCESS_NONE = 0,
        CPU_ACCESS_DYNAMIC = 1,
        CPU_ACCESS_READ_WRITE = 2,
        CPU_ACCESS_SCRATCH = 3,
        CPU_ACCESS_FIELD = 15,
        SHADER_INPUT = 0x10,
        RENDER_TARGET_OUTPUT = 0x20,
        BACK_BUFFER = 0x40,
        SHARED = 0x80,
        READ_ONLY = 0x100,
        DISCARD_ON_PRESENT = 0x200,
        UNORDERED_ACCESS = 0x400,
    }

    [Flags]
    public enum DXGI_MWA : uint                            // MakeWindowAssociation flags
    {
        NONE = 0,
        NO_WINDOW_CHANGES = 1,
        NO_ALT_ENTER = 2,
        NO_PRINT_SCREEN = 4,
    }

    [Flags]
    public enum DXGI_CREATE_FACTORY : uint
    {
        NONE = 0,
        DEBUG = 1,
    }

    public enum DXGI_SCALING : int { STRETCH = 0, NONE = 1, ASPECT_RATIO_STRETCH = 2 }
    public enum DXGI_ALPHA_MODE : int { UNSPECIFIED = 0, PREMULTIPLIED = 1, STRAIGHT = 2, IGNORE = 3 }
    public enum DXGI_MODE_SCANLINE_ORDER : int { UNSPECIFIED = 0, PROGRESSIVE = 1, UPPER_FIELD_FIRST = 2, LOWER_FIELD_FIRST = 3 }
    public enum DXGI_MODE_SCALING : int { UNSPECIFIED = 0, CENTERED = 1, STRETCHED = 2 }
    public enum DXGI_MODE_ROTATION : int { UNSPECIFIED = 0, IDENTITY = 1, ROTATE90 = 2, ROTATE180 = 3, ROTATE270 = 4 }
    public enum DXGI_GPU_PREFERENCE : int { UNSPECIFIED = 0, MINIMUM_POWER = 1, HIGH_PERFORMANCE = 2 }
    public enum DXGI_MEMORY_SEGMENT_GROUP : int { LOCAL = 0, NON_LOCAL = 1 }   // dxgi1_4.h
    public enum DXGI_FEATURE : int { PRESENT_ALLOW_TEARING = 0 }
    public enum DXGI_HDR_METADATA_TYPE : int { NONE = 0, HDR10 = 1, HDR10PLUS = 2 }

    [Flags]
    public enum DXGI_ADAPTER_FLAG : uint { NONE = 0, REMOTE = 1, SOFTWARE = 2 }

    public enum DXGI_COLOR_SPACE_TYPE : int
    {
        RGB_FULL_G22_NONE_P709 = 0,
        RGB_FULL_G10_NONE_P709 = 1,
        RGB_STUDIO_G22_NONE_P709 = 2,
        RGB_STUDIO_G22_NONE_P2020 = 3,
        RESERVED = 4,
        YCBCR_FULL_G22_NONE_P709_X601 = 5,
        YCBCR_STUDIO_G22_LEFT_P601 = 6,
        YCBCR_FULL_G22_LEFT_P601 = 7,
        YCBCR_STUDIO_G22_LEFT_P709 = 8,
        YCBCR_FULL_G22_LEFT_P709 = 9,
        YCBCR_STUDIO_G22_LEFT_P2020 = 10,
        YCBCR_FULL_G22_LEFT_P2020 = 11,
        RGB_FULL_G2084_NONE_P2020 = 12,
        YCBCR_STUDIO_G2084_LEFT_P2020 = 13,
        RGB_STUDIO_G2084_NONE_P2020 = 14,
        YCBCR_STUDIO_G22_TOPLEFT_P2020 = 15,
        YCBCR_STUDIO_G2084_TOPLEFT_P2020 = 16,
        RGB_FULL_G22_NONE_P2020 = 17,
        YCBCR_STUDIO_GHLG_TOPLEFT_P2020 = 18,
        YCBCR_FULL_GHLG_TOPLEFT_P2020 = 19,
        RGB_STUDIO_G24_NONE_P709 = 20,
        RGB_STUDIO_G24_NONE_P2020 = 21,
        YCBCR_STUDIO_G24_LEFT_P709 = 22,
        YCBCR_STUDIO_G24_LEFT_P2020 = 23,
        YCBCR_STUDIO_G24_TOPLEFT_P2020 = 24,
    }

    // ------------------------------------------------------------------------------------------------------------
    // Structs
    // ------------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_RATIONAL                            // 8
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SAMPLE_DESC                         // 8
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_MODE_DESC                           // 28
    {
        public uint Width;
        public uint Height;
        public DXGI_RATIONAL RefreshRate;
        public DXGI_FORMAT Format;
        public DXGI_MODE_SCANLINE_ORDER ScanlineOrdering;
        public DXGI_MODE_SCALING Scaling;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SWAP_CHAIN_DESC                     // 72 (legacy CreateSwapChain / GetDesc)
    {
        public DXGI_MODE_DESC BufferDesc;
        public DXGI_SAMPLE_DESC SampleDesc;
        public DXGI_USAGE BufferUsage;
        public uint BufferCount;
        public IntPtr OutputWindow;
        public int Windowed;
        public DXGI_SWAP_EFFECT SwapEffect;
        public DXGI_SWAP_CHAIN_FLAG Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SWAP_CHAIN_DESC1                    // 48
    {
        public uint Width;
        public uint Height;
        public DXGI_FORMAT Format;
        public int Stereo;                                 // BOOL
        public DXGI_SAMPLE_DESC SampleDesc;
        public DXGI_USAGE BufferUsage;
        public uint BufferCount;
        public DXGI_SCALING Scaling;
        public DXGI_SWAP_EFFECT SwapEffect;
        public DXGI_ALPHA_MODE AlphaMode;
        public DXGI_SWAP_CHAIN_FLAG Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SWAP_CHAIN_FULLSCREEN_DESC          // 20
    {
        public DXGI_RATIONAL RefreshRate;
        public DXGI_MODE_SCANLINE_ORDER ScanlineOrdering;
        public DXGI_MODE_SCALING Scaling;
        public int Windowed;                               // BOOL
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_FRAME_STATISTICS                    // 32
    {
        public uint PresentCount;
        public uint PresentRefreshCount;
        public uint SyncRefreshCount;
        public long SyncQPCTime;                           // LARGE_INTEGER (QueryPerformanceCounter units)
        public long SyncGPUTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_PRESENT_PARAMETERS                  // 32
    {
        public uint DirtyRectsCount;
        public IntPtr pDirtyRects;                         // RECT*
        public IntPtr pScrollRect;                         // RECT*
        public IntPtr pScrollOffset;                       // POINT*
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_RGBA                                // 16
    {
        public float r, g, b, a;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_MATRIX_3X2_F                        // 24
    {
        public float _11, _12, _21, _22, _31, _32;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DXGI_HDR_METADATA_HDR10           // 28 (SetHDRMetaData(HDR10, 28, &md))
    {
        public fixed ushort RedPrimary[2];
        public fixed ushort GreenPrimary[2];
        public fixed ushort BluePrimary[2];
        public fixed ushort WhitePoint[2];
        public uint MaxMasteringLuminance;
        public uint MinMasteringLuminance;
        public ushort MaxContentLightLevel;
        public ushort MaxFrameAverageLightLevel;
    }

    // CharSet.Unicode is REQUIRED on the structs below: ByValTStr would otherwise be marshaled as 1-byte ANSI and the
    // struct would be 64/16 bytes too small.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_ADAPTER_DESC                        // 304
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public LUID AdapterLuid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_ADAPTER_DESC1                       // 312
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public LUID AdapterLuid;
        public DXGI_ADAPTER_FLAG Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_OUTPUT_DESC                         // 96
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public RECT DesktopCoordinates;
        public int AttachedToDesktop;                      // BOOL
        public DXGI_MODE_ROTATION Rotation;
        public IntPtr Monitor;                             // HMONITOR
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_OUTPUT_DESC1                        // 152 (IDXGIOutput6::GetDesc1)
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public RECT DesktopCoordinates;
        public int AttachedToDesktop;                      // BOOL
        public DXGI_MODE_ROTATION Rotation;
        public IntPtr Monitor;                             // HMONITOR
        public uint BitsPerColor;
        public DXGI_COLOR_SPACE_TYPE ColorSpace;
        public float RedPrimaryX, RedPrimaryY;
        public float GreenPrimaryX, GreenPrimaryY;
        public float BluePrimaryX, BluePrimaryY;
        public float WhitePointX, WhitePointY;
        public float MinLuminance;
        public float MaxLuminance;
        public float MaxFullFrameLuminance;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Interfaces (slot numbers in /*NN*/ include IUnknown 0-2)
    // ------------------------------------------------------------------------------------------------------------
    // IDXGIObject : IUnknown (7 slots)
    [ComImport, Guid("aec22fb8-76f3-4639-9be0-28eb43a67a2e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIObject
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
    }

    // IDXGIDeviceSubObject : IDXGIObject (8 slots)
    [ComImport, Guid("3d3e0379-f9de-4d58-bb6c-18d62992f1a6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIDeviceSubObject
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppDevice);
    }

    // IDXGIFactory : IDXGIObject (12 slots)
    [ComImport, Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIFactory
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int EnumAdapters(uint Adapter, out IDXGIAdapter ppAdapter);
        /* 8*/ [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, DXGI_MWA Flags);
        /* 9*/ [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
        /*10*/ [PreserveSig] int CreateSwapChain([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC pDesc, out IDXGISwapChain ppSwapChain);
        /*11*/ [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IDXGIAdapter ppAdapter);
    }

    // IDXGIFactory1 : IDXGIFactory (14 slots)
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIFactory1
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int EnumAdapters(uint Adapter, out IDXGIAdapter ppAdapter);
        /* 8*/ [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, DXGI_MWA Flags);
        /* 9*/ [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
        /*10*/ [PreserveSig] int CreateSwapChain([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC pDesc, out IDXGISwapChain ppSwapChain);
        /*11*/ [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IDXGIAdapter ppAdapter);
        /*12*/ [PreserveSig] int EnumAdapters1(uint Adapter, out IDXGIAdapter1 ppAdapter);
        /*13*/ [PreserveSig] int IsCurrent();
    }

    // IDXGIFactory2 : IDXGIFactory1 (25 slots)
    [ComImport, Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIFactory2
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int EnumAdapters(uint Adapter, out IDXGIAdapter ppAdapter);
        /* 8*/ [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, DXGI_MWA Flags);
        /* 9*/ [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
        /*10*/ [PreserveSig] int CreateSwapChain([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC pDesc, out IDXGISwapChain ppSwapChain);
        /*11*/ [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IDXGIAdapter ppAdapter);
        /*12*/ [PreserveSig] int EnumAdapters1(uint Adapter, out IDXGIAdapter1 ppAdapter);
        /*13*/ [PreserveSig] int IsCurrent();
        /*14*/ [PreserveSig] int IsWindowedStereoEnabled();
        /*15*/ [PreserveSig] int CreateSwapChainForHwnd([MarshalAs(UnmanagedType.IUnknown)] object pDevice, IntPtr hWnd, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pFullscreenDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*16*/ [PreserveSig] int CreateSwapChainForCoreWindow([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [MarshalAs(UnmanagedType.IUnknown)] object pWindow, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*17*/ [PreserveSig] int GetSharedResourceAdapterLuid(IntPtr hResource, out LUID pLuid);
        /*18*/ [PreserveSig] int RegisterStereoStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
        /*19*/ [PreserveSig] int RegisterStereoStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*20*/ [PreserveSig] void UnregisterStereoStatus(uint dwCookie);
        /*21*/ [PreserveSig] int RegisterOcclusionStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
        /*22*/ [PreserveSig] int RegisterOcclusionStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*23*/ [PreserveSig] void UnregisterOcclusionStatus(uint dwCookie);
        /*24*/ [PreserveSig] int CreateSwapChainForComposition([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
    }

    // IDXGIFactory3 : IDXGIFactory2 (26 slots)
    [ComImport, Guid("25483823-cd46-4c7d-86ca-47aa95b837bd"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIFactory3
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int EnumAdapters(uint Adapter, out IDXGIAdapter ppAdapter);
        /* 8*/ [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, DXGI_MWA Flags);
        /* 9*/ [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
        /*10*/ [PreserveSig] int CreateSwapChain([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC pDesc, out IDXGISwapChain ppSwapChain);
        /*11*/ [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IDXGIAdapter ppAdapter);
        /*12*/ [PreserveSig] int EnumAdapters1(uint Adapter, out IDXGIAdapter1 ppAdapter);
        /*13*/ [PreserveSig] int IsCurrent();
        /*14*/ [PreserveSig] int IsWindowedStereoEnabled();
        /*15*/ [PreserveSig] int CreateSwapChainForHwnd([MarshalAs(UnmanagedType.IUnknown)] object pDevice, IntPtr hWnd, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pFullscreenDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*16*/ [PreserveSig] int CreateSwapChainForCoreWindow([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [MarshalAs(UnmanagedType.IUnknown)] object pWindow, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*17*/ [PreserveSig] int GetSharedResourceAdapterLuid(IntPtr hResource, out LUID pLuid);
        /*18*/ [PreserveSig] int RegisterStereoStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
        /*19*/ [PreserveSig] int RegisterStereoStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*20*/ [PreserveSig] void UnregisterStereoStatus(uint dwCookie);
        /*21*/ [PreserveSig] int RegisterOcclusionStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
        /*22*/ [PreserveSig] int RegisterOcclusionStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*23*/ [PreserveSig] void UnregisterOcclusionStatus(uint dwCookie);
        /*24*/ [PreserveSig] int CreateSwapChainForComposition([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*25*/ [PreserveSig] uint GetCreationFlags();
    }

    // IDXGIFactory4 : IDXGIFactory3 (28 slots)
    [ComImport, Guid("1bc6ea02-ef36-464f-bf0c-21ca39e5168a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIFactory4
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int EnumAdapters(uint Adapter, out IDXGIAdapter ppAdapter);
        /* 8*/ [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, DXGI_MWA Flags);
        /* 9*/ [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
        /*10*/ [PreserveSig] int CreateSwapChain([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC pDesc, out IDXGISwapChain ppSwapChain);
        /*11*/ [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IDXGIAdapter ppAdapter);
        /*12*/ [PreserveSig] int EnumAdapters1(uint Adapter, out IDXGIAdapter1 ppAdapter);
        /*13*/ [PreserveSig] int IsCurrent();
        /*14*/ [PreserveSig] int IsWindowedStereoEnabled();
        /*15*/ [PreserveSig] int CreateSwapChainForHwnd([MarshalAs(UnmanagedType.IUnknown)] object pDevice, IntPtr hWnd, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pFullscreenDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*16*/ [PreserveSig] int CreateSwapChainForCoreWindow([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [MarshalAs(UnmanagedType.IUnknown)] object pWindow, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*17*/ [PreserveSig] int GetSharedResourceAdapterLuid(IntPtr hResource, out LUID pLuid);
        /*18*/ [PreserveSig] int RegisterStereoStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
        /*19*/ [PreserveSig] int RegisterStereoStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*20*/ [PreserveSig] void UnregisterStereoStatus(uint dwCookie);
        /*21*/ [PreserveSig] int RegisterOcclusionStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
        /*22*/ [PreserveSig] int RegisterOcclusionStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*23*/ [PreserveSig] void UnregisterOcclusionStatus(uint dwCookie);
        /*24*/ [PreserveSig] int CreateSwapChainForComposition([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*25*/ [PreserveSig] uint GetCreationFlags();
        /*26*/ [PreserveSig] int EnumAdapterByLuid(LUID AdapterLuid, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvAdapter);
        /*27*/ [PreserveSig] int EnumWarpAdapter([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvAdapter);
    }

    // IDXGIFactory5 : IDXGIFactory4 (29 slots)
    [ComImport, Guid("7632e1f5-ee65-4dca-87fd-84cd75f8838d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIFactory5
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int EnumAdapters(uint Adapter, out IDXGIAdapter ppAdapter);
        /* 8*/ [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, DXGI_MWA Flags);
        /* 9*/ [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
        /*10*/ [PreserveSig] int CreateSwapChain([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC pDesc, out IDXGISwapChain ppSwapChain);
        /*11*/ [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IDXGIAdapter ppAdapter);
        /*12*/ [PreserveSig] int EnumAdapters1(uint Adapter, out IDXGIAdapter1 ppAdapter);
        /*13*/ [PreserveSig] int IsCurrent();
        /*14*/ [PreserveSig] int IsWindowedStereoEnabled();
        /*15*/ [PreserveSig] int CreateSwapChainForHwnd([MarshalAs(UnmanagedType.IUnknown)] object pDevice, IntPtr hWnd, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pFullscreenDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*16*/ [PreserveSig] int CreateSwapChainForCoreWindow([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [MarshalAs(UnmanagedType.IUnknown)] object pWindow, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*17*/ [PreserveSig] int GetSharedResourceAdapterLuid(IntPtr hResource, out LUID pLuid);
        /*18*/ [PreserveSig] int RegisterStereoStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
        /*19*/ [PreserveSig] int RegisterStereoStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*20*/ [PreserveSig] void UnregisterStereoStatus(uint dwCookie);
        /*21*/ [PreserveSig] int RegisterOcclusionStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
        /*22*/ [PreserveSig] int RegisterOcclusionStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*23*/ [PreserveSig] void UnregisterOcclusionStatus(uint dwCookie);
        /*24*/ [PreserveSig] int CreateSwapChainForComposition([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*25*/ [PreserveSig] uint GetCreationFlags();
        /*26*/ [PreserveSig] int EnumAdapterByLuid(LUID AdapterLuid, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvAdapter);
        /*27*/ [PreserveSig] int EnumWarpAdapter([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvAdapter);
        /*28*/ [PreserveSig] int CheckFeatureSupport(DXGI_FEATURE Feature, void* pFeatureSupportData, uint FeatureSupportDataSize);
    }

    // IDXGIFactory6 : IDXGIFactory5 (30 slots)
    [ComImport, Guid("c1b6694f-ff09-44a9-b03c-77900a0a1d17"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIFactory6
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int EnumAdapters(uint Adapter, out IDXGIAdapter ppAdapter);
        /* 8*/ [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, DXGI_MWA Flags);
        /* 9*/ [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
        /*10*/ [PreserveSig] int CreateSwapChain([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC pDesc, out IDXGISwapChain ppSwapChain);
        /*11*/ [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IDXGIAdapter ppAdapter);
        /*12*/ [PreserveSig] int EnumAdapters1(uint Adapter, out IDXGIAdapter1 ppAdapter);
        /*13*/ [PreserveSig] int IsCurrent();
        /*14*/ [PreserveSig] int IsWindowedStereoEnabled();
        /*15*/ [PreserveSig] int CreateSwapChainForHwnd([MarshalAs(UnmanagedType.IUnknown)] object pDevice, IntPtr hWnd, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pFullscreenDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*16*/ [PreserveSig] int CreateSwapChainForCoreWindow([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [MarshalAs(UnmanagedType.IUnknown)] object pWindow, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*17*/ [PreserveSig] int GetSharedResourceAdapterLuid(IntPtr hResource, out LUID pLuid);
        /*18*/ [PreserveSig] int RegisterStereoStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
        /*19*/ [PreserveSig] int RegisterStereoStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*20*/ [PreserveSig] void UnregisterStereoStatus(uint dwCookie);
        /*21*/ [PreserveSig] int RegisterOcclusionStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
        /*22*/ [PreserveSig] int RegisterOcclusionStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*23*/ [PreserveSig] void UnregisterOcclusionStatus(uint dwCookie);
        /*24*/ [PreserveSig] int CreateSwapChainForComposition([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*25*/ [PreserveSig] uint GetCreationFlags();
        /*26*/ [PreserveSig] int EnumAdapterByLuid(LUID AdapterLuid, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvAdapter);
        /*27*/ [PreserveSig] int EnumWarpAdapter([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvAdapter);
        /*28*/ [PreserveSig] int CheckFeatureSupport(DXGI_FEATURE Feature, void* pFeatureSupportData, uint FeatureSupportDataSize);
        /*29*/ [PreserveSig] int EnumAdapterByGpuPreference(uint Adapter, DXGI_GPU_PREFERENCE GpuPreference, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvAdapter);
    }

    // IDXGIFactory7 : IDXGIFactory6 (32 slots)
    [ComImport, Guid("a4966eed-76db-44da-84c1-ee9a7afb20a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIFactory7
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int EnumAdapters(uint Adapter, out IDXGIAdapter ppAdapter);
        /* 8*/ [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, DXGI_MWA Flags);
        /* 9*/ [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
        /*10*/ [PreserveSig] int CreateSwapChain([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC pDesc, out IDXGISwapChain ppSwapChain);
        /*11*/ [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IDXGIAdapter ppAdapter);
        /*12*/ [PreserveSig] int EnumAdapters1(uint Adapter, out IDXGIAdapter1 ppAdapter);
        /*13*/ [PreserveSig] int IsCurrent();
        /*14*/ [PreserveSig] int IsWindowedStereoEnabled();
        /*15*/ [PreserveSig] int CreateSwapChainForHwnd([MarshalAs(UnmanagedType.IUnknown)] object pDevice, IntPtr hWnd, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pFullscreenDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*16*/ [PreserveSig] int CreateSwapChainForCoreWindow([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [MarshalAs(UnmanagedType.IUnknown)] object pWindow, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*17*/ [PreserveSig] int GetSharedResourceAdapterLuid(IntPtr hResource, out LUID pLuid);
        /*18*/ [PreserveSig] int RegisterStereoStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
        /*19*/ [PreserveSig] int RegisterStereoStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*20*/ [PreserveSig] void UnregisterStereoStatus(uint dwCookie);
        /*21*/ [PreserveSig] int RegisterOcclusionStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
        /*22*/ [PreserveSig] int RegisterOcclusionStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*23*/ [PreserveSig] void UnregisterOcclusionStatus(uint dwCookie);
        /*24*/ [PreserveSig] int CreateSwapChainForComposition([MarshalAs(UnmanagedType.IUnknown)] object pDevice, [In] ref DXGI_SWAP_CHAIN_DESC1 pDesc, IDXGIOutput pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
        /*25*/ [PreserveSig] uint GetCreationFlags();
        /*26*/ [PreserveSig] int EnumAdapterByLuid(LUID AdapterLuid, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvAdapter);
        /*27*/ [PreserveSig] int EnumWarpAdapter([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvAdapter);
        /*28*/ [PreserveSig] int CheckFeatureSupport(DXGI_FEATURE Feature, void* pFeatureSupportData, uint FeatureSupportDataSize);
        /*29*/ [PreserveSig] int EnumAdapterByGpuPreference(uint Adapter, DXGI_GPU_PREFERENCE GpuPreference, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvAdapter);
        /*30*/ [PreserveSig] int RegisterAdaptersChangedEvent(IntPtr hEvent, out uint pdwCookie);
        /*31*/ [PreserveSig] int UnregisterAdaptersChangedEvent(uint dwCookie);
    }

    // IDXGIAdapter : IDXGIObject (10 slots)
    [ComImport, Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIAdapter
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int EnumOutputs(uint Output, out IDXGIOutput ppOutput);
        /* 8*/ [PreserveSig] int GetDesc(out DXGI_ADAPTER_DESC pDesc);
        /* 9*/ [PreserveSig] int CheckInterfaceSupport([In] ref Guid InterfaceName, out long pUMDVersion);
    }

    // IDXGIAdapter1 : IDXGIAdapter (11 slots)
    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIAdapter1
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int EnumOutputs(uint Output, out IDXGIOutput ppOutput);
        /* 8*/ [PreserveSig] int GetDesc(out DXGI_ADAPTER_DESC pDesc);
        /* 9*/ [PreserveSig] int CheckInterfaceSupport([In] ref Guid InterfaceName, out long pUMDVersion);
        /*10*/ [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
    }

    // DXGI_QUERY_VIDEO_MEMORY_INFO (dxgi1_4.h): 4 x UINT64 = 32 bytes
    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_QUERY_VIDEO_MEMORY_INFO
    {
        public ulong Budget;
        public ulong CurrentUsage;                 // de este proceso
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }

    // IDXGIAdapter3 : IDXGIAdapter2 : IDXGIAdapter1 (18 slots; dxgi1_4.h). IDXGIAdapter2 adds GetDesc2 (slot 11; its
    // DXGI_ADAPTER_DESC2 is not declared here: IntPtr). 16-11: used for QueryVideoMemoryInfo (VRAM of this process, logged
    // when the engine goes to standby)
    [ComImport, Guid("645967a4-1392-4310-a798-8053ce3e93fd"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIAdapter3
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int EnumOutputs(uint Output, out IDXGIOutput ppOutput);
        /* 8*/ [PreserveSig] int GetDesc(out DXGI_ADAPTER_DESC pDesc);
        /* 9*/ [PreserveSig] int CheckInterfaceSupport([In] ref Guid InterfaceName, out long pUMDVersion);
        /*10*/ [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
        /*11*/ [PreserveSig] int GetDesc2(IntPtr pDesc);
        /*12*/ [PreserveSig] int RegisterHardwareContentProtectionTeardownStatusEvent(IntPtr hEvent, out uint pdwCookie);
        /*13*/ [PreserveSig] void UnregisterHardwareContentProtectionTeardownStatus(uint dwCookie);
        /*14*/ [PreserveSig] int QueryVideoMemoryInfo(uint NodeIndex, DXGI_MEMORY_SEGMENT_GROUP MemorySegmentGroup, out DXGI_QUERY_VIDEO_MEMORY_INFO pVideoMemoryInfo);
        /*15*/ [PreserveSig] int SetVideoMemoryReservation(uint NodeIndex, DXGI_MEMORY_SEGMENT_GROUP MemorySegmentGroup, ulong Reservation);
        /*16*/ [PreserveSig] int RegisterVideoMemoryBudgetChangeNotificationEvent(IntPtr hEvent, out uint pdwCookie);
        /*17*/ [PreserveSig] void UnregisterVideoMemoryBudgetChangeNotification(uint dwCookie);
    }

    // IDXGIOutput : IDXGIObject (19 slots)
    [ComImport, Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIOutput
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int GetDesc(out DXGI_OUTPUT_DESC pDesc);
        /* 8*/ [PreserveSig] int GetDisplayModeList(DXGI_FORMAT EnumFormat, uint Flags, ref uint pNumModes, DXGI_MODE_DESC* pDesc);
        /* 9*/ [PreserveSig] int FindClosestMatchingMode([In] ref DXGI_MODE_DESC pModeToMatch, out DXGI_MODE_DESC pClosestMatch, [MarshalAs(UnmanagedType.IUnknown)] object pConcernedDevice);
        /*10*/ [PreserveSig] int WaitForVBlank();
        /*11*/ [PreserveSig] int TakeOwnership([MarshalAs(UnmanagedType.IUnknown)] object pDevice, int Exclusive);
        /*12*/ [PreserveSig] void ReleaseOwnership();
        /*13*/ [PreserveSig] int GetGammaControlCapabilities(IntPtr pGammaCaps);
        /*14*/ [PreserveSig] int SetGammaControl(IntPtr pArray);
        /*15*/ [PreserveSig] int GetGammaControl(IntPtr pArray);
        /*16*/ [PreserveSig] int SetDisplaySurface(IntPtr pScanoutSurface);
        /*17*/ [PreserveSig] int GetDisplaySurfaceData(IntPtr pDestination);
        /*18*/ [PreserveSig] int GetFrameStatistics(out DXGI_FRAME_STATISTICS pStats);
    }

    // IDXGIOutput6 : IDXGIOutput5..1 : IDXGIOutput (29 slots; 1..5 methods declared with IntPtr where unused)
    [ComImport, Guid("068346e8-aaec-4b84-add7-137f513f77a1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGIOutput6
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int GetDesc(out DXGI_OUTPUT_DESC pDesc);
        /* 8*/ [PreserveSig] int GetDisplayModeList(DXGI_FORMAT EnumFormat, uint Flags, ref uint pNumModes, DXGI_MODE_DESC* pDesc);
        /* 9*/ [PreserveSig] int FindClosestMatchingMode([In] ref DXGI_MODE_DESC pModeToMatch, out DXGI_MODE_DESC pClosestMatch, [MarshalAs(UnmanagedType.IUnknown)] object pConcernedDevice);
        /*10*/ [PreserveSig] int WaitForVBlank();
        /*11*/ [PreserveSig] int TakeOwnership([MarshalAs(UnmanagedType.IUnknown)] object pDevice, int Exclusive);
        /*12*/ [PreserveSig] void ReleaseOwnership();
        /*13*/ [PreserveSig] int GetGammaControlCapabilities(IntPtr pGammaCaps);
        /*14*/ [PreserveSig] int SetGammaControl(IntPtr pArray);
        /*15*/ [PreserveSig] int GetGammaControl(IntPtr pArray);
        /*16*/ [PreserveSig] int SetDisplaySurface(IntPtr pScanoutSurface);
        /*17*/ [PreserveSig] int GetDisplaySurfaceData(IntPtr pDestination);
        /*18*/ [PreserveSig] int GetFrameStatistics(out DXGI_FRAME_STATISTICS pStats);
        /*19*/ [PreserveSig] int GetDisplayModeList1(DXGI_FORMAT EnumFormat, uint Flags, ref uint pNumModes, IntPtr pDesc);
        /*20*/ [PreserveSig] int FindClosestMatchingMode1(IntPtr pModeToMatch, IntPtr pClosestMatch, [MarshalAs(UnmanagedType.IUnknown)] object pConcernedDevice);
        /*21*/ [PreserveSig] int GetDisplaySurfaceData1(IntPtr pDestination);
        /*22*/ [PreserveSig] int DuplicateOutput([MarshalAs(UnmanagedType.IUnknown)] object pDevice, out IntPtr ppOutputDuplication);
        /*23*/ [PreserveSig] int SupportsOverlays();
        /*24*/ [PreserveSig] int CheckOverlaySupport(DXGI_FORMAT EnumFormat, [MarshalAs(UnmanagedType.IUnknown)] object pConcernedDevice, out uint pFlags);
        /*25*/ [PreserveSig] int CheckOverlayColorSpaceSupport(DXGI_FORMAT Format, DXGI_COLOR_SPACE_TYPE ColorSpace, [MarshalAs(UnmanagedType.IUnknown)] object pConcernedDevice, out uint pFlags);
        /*26*/ [PreserveSig] int DuplicateOutput1([MarshalAs(UnmanagedType.IUnknown)] object pDevice, uint Flags, uint SupportedFormatsCount, DXGI_FORMAT* pSupportedFormats, out IntPtr ppOutputDuplication);
        /*27*/ [PreserveSig] int GetDesc1(out DXGI_OUTPUT_DESC1 pDesc);
        /*28*/ [PreserveSig] int CheckHardwareCompositionSupport(out uint pFlags);
    }

    // IDXGISwapChain : IDXGIDeviceSubObject (18 slots)
    [ComImport, Guid("310d36a0-d2e7-4c0a-aa04-6a9d23b8886a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGISwapChain
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppDevice);
        /* 8*/ [PreserveSig] int Present(uint SyncInterval, DXGI_PRESENT Flags);
        /* 9*/ [PreserveSig] int GetBuffer(uint Buffer, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppSurface);
        /*10*/ [PreserveSig] int SetFullscreenState(int Fullscreen, IDXGIOutput pTarget);
        /*11*/ [PreserveSig] int GetFullscreenState(out int pFullscreen, out IDXGIOutput ppTarget);
        /*12*/ [PreserveSig] int GetDesc(out DXGI_SWAP_CHAIN_DESC pDesc);
        /*13*/ [PreserveSig] int ResizeBuffers(uint BufferCount, uint Width, uint Height, DXGI_FORMAT NewFormat, DXGI_SWAP_CHAIN_FLAG SwapChainFlags);
        /*14*/ [PreserveSig] int ResizeTarget([In] ref DXGI_MODE_DESC pNewTargetParameters);
        /*15*/ [PreserveSig] int GetContainingOutput(out IDXGIOutput ppOutput);
        /*16*/ [PreserveSig] int GetFrameStatistics(out DXGI_FRAME_STATISTICS pStats);
        /*17*/ [PreserveSig] int GetLastPresentCount(out uint pLastPresentCount);
    }

    // IDXGISwapChain1 : IDXGISwapChain (29 slots)
    [ComImport, Guid("790a45f7-0d42-4876-983a-0a55cfe6f4aa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGISwapChain1
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppDevice);
        /* 8*/ [PreserveSig] int Present(uint SyncInterval, DXGI_PRESENT Flags);
        /* 9*/ [PreserveSig] int GetBuffer(uint Buffer, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppSurface);
        /*10*/ [PreserveSig] int SetFullscreenState(int Fullscreen, IDXGIOutput pTarget);
        /*11*/ [PreserveSig] int GetFullscreenState(out int pFullscreen, out IDXGIOutput ppTarget);
        /*12*/ [PreserveSig] int GetDesc(out DXGI_SWAP_CHAIN_DESC pDesc);
        /*13*/ [PreserveSig] int ResizeBuffers(uint BufferCount, uint Width, uint Height, DXGI_FORMAT NewFormat, DXGI_SWAP_CHAIN_FLAG SwapChainFlags);
        /*14*/ [PreserveSig] int ResizeTarget([In] ref DXGI_MODE_DESC pNewTargetParameters);
        /*15*/ [PreserveSig] int GetContainingOutput(out IDXGIOutput ppOutput);
        /*16*/ [PreserveSig] int GetFrameStatistics(out DXGI_FRAME_STATISTICS pStats);
        /*17*/ [PreserveSig] int GetLastPresentCount(out uint pLastPresentCount);
        /*18*/ [PreserveSig] int GetDesc1(out DXGI_SWAP_CHAIN_DESC1 pDesc);
        /*19*/ [PreserveSig] int GetFullscreenDesc(out DXGI_SWAP_CHAIN_FULLSCREEN_DESC pDesc);
        /*20*/ [PreserveSig] int GetHwnd(out IntPtr pHwnd);
        /*21*/ [PreserveSig] int GetCoreWindow([In] ref Guid refiid, [MarshalAs(UnmanagedType.IUnknown)] out object ppUnk);
        /*22*/ [PreserveSig] int Present1(uint SyncInterval, DXGI_PRESENT PresentFlags, [In] ref DXGI_PRESENT_PARAMETERS pPresentParameters);
        /*23*/ [PreserveSig] int IsTemporaryMonoSupported();
        /*24*/ [PreserveSig] int GetRestrictToOutput(out IDXGIOutput ppRestrictToOutput);
        /*25*/ [PreserveSig] int SetBackgroundColor([In] ref DXGI_RGBA pColor);
        /*26*/ [PreserveSig] int GetBackgroundColor(out DXGI_RGBA pColor);
        /*27*/ [PreserveSig] int SetRotation(DXGI_MODE_ROTATION Rotation);
        /*28*/ [PreserveSig] int GetRotation(out DXGI_MODE_ROTATION pRotation);
    }

    // IDXGISwapChain2 : IDXGISwapChain1 (36 slots)
    [ComImport, Guid("a8be2ac4-199f-4946-b331-79599fb98de7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGISwapChain2
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppDevice);
        /* 8*/ [PreserveSig] int Present(uint SyncInterval, DXGI_PRESENT Flags);
        /* 9*/ [PreserveSig] int GetBuffer(uint Buffer, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppSurface);
        /*10*/ [PreserveSig] int SetFullscreenState(int Fullscreen, IDXGIOutput pTarget);
        /*11*/ [PreserveSig] int GetFullscreenState(out int pFullscreen, out IDXGIOutput ppTarget);
        /*12*/ [PreserveSig] int GetDesc(out DXGI_SWAP_CHAIN_DESC pDesc);
        /*13*/ [PreserveSig] int ResizeBuffers(uint BufferCount, uint Width, uint Height, DXGI_FORMAT NewFormat, DXGI_SWAP_CHAIN_FLAG SwapChainFlags);
        /*14*/ [PreserveSig] int ResizeTarget([In] ref DXGI_MODE_DESC pNewTargetParameters);
        /*15*/ [PreserveSig] int GetContainingOutput(out IDXGIOutput ppOutput);
        /*16*/ [PreserveSig] int GetFrameStatistics(out DXGI_FRAME_STATISTICS pStats);
        /*17*/ [PreserveSig] int GetLastPresentCount(out uint pLastPresentCount);
        /*18*/ [PreserveSig] int GetDesc1(out DXGI_SWAP_CHAIN_DESC1 pDesc);
        /*19*/ [PreserveSig] int GetFullscreenDesc(out DXGI_SWAP_CHAIN_FULLSCREEN_DESC pDesc);
        /*20*/ [PreserveSig] int GetHwnd(out IntPtr pHwnd);
        /*21*/ [PreserveSig] int GetCoreWindow([In] ref Guid refiid, [MarshalAs(UnmanagedType.IUnknown)] out object ppUnk);
        /*22*/ [PreserveSig] int Present1(uint SyncInterval, DXGI_PRESENT PresentFlags, [In] ref DXGI_PRESENT_PARAMETERS pPresentParameters);
        /*23*/ [PreserveSig] int IsTemporaryMonoSupported();
        /*24*/ [PreserveSig] int GetRestrictToOutput(out IDXGIOutput ppRestrictToOutput);
        /*25*/ [PreserveSig] int SetBackgroundColor([In] ref DXGI_RGBA pColor);
        /*26*/ [PreserveSig] int GetBackgroundColor(out DXGI_RGBA pColor);
        /*27*/ [PreserveSig] int SetRotation(DXGI_MODE_ROTATION Rotation);
        /*28*/ [PreserveSig] int GetRotation(out DXGI_MODE_ROTATION pRotation);
        /*29*/ [PreserveSig] int SetSourceSize(uint Width, uint Height);
        /*30*/ [PreserveSig] int GetSourceSize(out uint pWidth, out uint pHeight);
        /*31*/ [PreserveSig] int SetMaximumFrameLatency(uint MaxLatency);
        /*32*/ [PreserveSig] int GetMaximumFrameLatency(out uint pMaxLatency);
        /*33*/ [PreserveSig] IntPtr GetFrameLatencyWaitableObject();
        /*34*/ [PreserveSig] int SetMatrixTransform([In] ref DXGI_MATRIX_3X2_F pMatrix);
        /*35*/ [PreserveSig] int GetMatrixTransform(out DXGI_MATRIX_3X2_F pMatrix);
    }

    // IDXGISwapChain3 : IDXGISwapChain2 (40 slots)
    [ComImport, Guid("94d99bdb-f1f8-4ab0-b236-7da0170edab1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGISwapChain3
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppDevice);
        /* 8*/ [PreserveSig] int Present(uint SyncInterval, DXGI_PRESENT Flags);
        /* 9*/ [PreserveSig] int GetBuffer(uint Buffer, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppSurface);
        /*10*/ [PreserveSig] int SetFullscreenState(int Fullscreen, IDXGIOutput pTarget);
        /*11*/ [PreserveSig] int GetFullscreenState(out int pFullscreen, out IDXGIOutput ppTarget);
        /*12*/ [PreserveSig] int GetDesc(out DXGI_SWAP_CHAIN_DESC pDesc);
        /*13*/ [PreserveSig] int ResizeBuffers(uint BufferCount, uint Width, uint Height, DXGI_FORMAT NewFormat, DXGI_SWAP_CHAIN_FLAG SwapChainFlags);
        /*14*/ [PreserveSig] int ResizeTarget([In] ref DXGI_MODE_DESC pNewTargetParameters);
        /*15*/ [PreserveSig] int GetContainingOutput(out IDXGIOutput ppOutput);
        /*16*/ [PreserveSig] int GetFrameStatistics(out DXGI_FRAME_STATISTICS pStats);
        /*17*/ [PreserveSig] int GetLastPresentCount(out uint pLastPresentCount);
        /*18*/ [PreserveSig] int GetDesc1(out DXGI_SWAP_CHAIN_DESC1 pDesc);
        /*19*/ [PreserveSig] int GetFullscreenDesc(out DXGI_SWAP_CHAIN_FULLSCREEN_DESC pDesc);
        /*20*/ [PreserveSig] int GetHwnd(out IntPtr pHwnd);
        /*21*/ [PreserveSig] int GetCoreWindow([In] ref Guid refiid, [MarshalAs(UnmanagedType.IUnknown)] out object ppUnk);
        /*22*/ [PreserveSig] int Present1(uint SyncInterval, DXGI_PRESENT PresentFlags, [In] ref DXGI_PRESENT_PARAMETERS pPresentParameters);
        /*23*/ [PreserveSig] int IsTemporaryMonoSupported();
        /*24*/ [PreserveSig] int GetRestrictToOutput(out IDXGIOutput ppRestrictToOutput);
        /*25*/ [PreserveSig] int SetBackgroundColor([In] ref DXGI_RGBA pColor);
        /*26*/ [PreserveSig] int GetBackgroundColor(out DXGI_RGBA pColor);
        /*27*/ [PreserveSig] int SetRotation(DXGI_MODE_ROTATION Rotation);
        /*28*/ [PreserveSig] int GetRotation(out DXGI_MODE_ROTATION pRotation);
        /*29*/ [PreserveSig] int SetSourceSize(uint Width, uint Height);
        /*30*/ [PreserveSig] int GetSourceSize(out uint pWidth, out uint pHeight);
        /*31*/ [PreserveSig] int SetMaximumFrameLatency(uint MaxLatency);
        /*32*/ [PreserveSig] int GetMaximumFrameLatency(out uint pMaxLatency);
        /*33*/ [PreserveSig] IntPtr GetFrameLatencyWaitableObject();
        /*34*/ [PreserveSig] int SetMatrixTransform([In] ref DXGI_MATRIX_3X2_F pMatrix);
        /*35*/ [PreserveSig] int GetMatrixTransform(out DXGI_MATRIX_3X2_F pMatrix);
        /*36*/ [PreserveSig] uint GetCurrentBackBufferIndex();
        /*37*/ [PreserveSig] int CheckColorSpaceSupport(DXGI_COLOR_SPACE_TYPE ColorSpace, out uint pColorSpaceSupport);
        /*38*/ [PreserveSig] int SetColorSpace1(DXGI_COLOR_SPACE_TYPE ColorSpace);
        /*39*/ [PreserveSig] int ResizeBuffers1(uint BufferCount, uint Width, uint Height, DXGI_FORMAT Format, DXGI_SWAP_CHAIN_FLAG SwapChainFlags, uint* pCreationNodeMask, IntPtr* ppPresentQueue);
    }

    // IDXGISwapChain4 : IDXGISwapChain3 (41 slots)
    [ComImport, Guid("3d585d5a-bd4a-489e-b1f4-3dbcb6452ffb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface IDXGISwapChain4
    {
        /* 3*/ [PreserveSig] int SetPrivateData([In] ref Guid Name, uint DataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        /* 5*/ [PreserveSig] int GetPrivateData([In] ref Guid Name, ref uint pDataSize, void* pData);
        /* 6*/ [PreserveSig] int GetParent([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppDevice);
        /* 8*/ [PreserveSig] int Present(uint SyncInterval, DXGI_PRESENT Flags);
        /* 9*/ [PreserveSig] int GetBuffer(uint Buffer, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppSurface);
        /*10*/ [PreserveSig] int SetFullscreenState(int Fullscreen, IDXGIOutput pTarget);
        /*11*/ [PreserveSig] int GetFullscreenState(out int pFullscreen, out IDXGIOutput ppTarget);
        /*12*/ [PreserveSig] int GetDesc(out DXGI_SWAP_CHAIN_DESC pDesc);
        /*13*/ [PreserveSig] int ResizeBuffers(uint BufferCount, uint Width, uint Height, DXGI_FORMAT NewFormat, DXGI_SWAP_CHAIN_FLAG SwapChainFlags);
        /*14*/ [PreserveSig] int ResizeTarget([In] ref DXGI_MODE_DESC pNewTargetParameters);
        /*15*/ [PreserveSig] int GetContainingOutput(out IDXGIOutput ppOutput);
        /*16*/ [PreserveSig] int GetFrameStatistics(out DXGI_FRAME_STATISTICS pStats);
        /*17*/ [PreserveSig] int GetLastPresentCount(out uint pLastPresentCount);
        /*18*/ [PreserveSig] int GetDesc1(out DXGI_SWAP_CHAIN_DESC1 pDesc);
        /*19*/ [PreserveSig] int GetFullscreenDesc(out DXGI_SWAP_CHAIN_FULLSCREEN_DESC pDesc);
        /*20*/ [PreserveSig] int GetHwnd(out IntPtr pHwnd);
        /*21*/ [PreserveSig] int GetCoreWindow([In] ref Guid refiid, [MarshalAs(UnmanagedType.IUnknown)] out object ppUnk);
        /*22*/ [PreserveSig] int Present1(uint SyncInterval, DXGI_PRESENT PresentFlags, [In] ref DXGI_PRESENT_PARAMETERS pPresentParameters);
        /*23*/ [PreserveSig] int IsTemporaryMonoSupported();
        /*24*/ [PreserveSig] int GetRestrictToOutput(out IDXGIOutput ppRestrictToOutput);
        /*25*/ [PreserveSig] int SetBackgroundColor([In] ref DXGI_RGBA pColor);
        /*26*/ [PreserveSig] int GetBackgroundColor(out DXGI_RGBA pColor);
        /*27*/ [PreserveSig] int SetRotation(DXGI_MODE_ROTATION Rotation);
        /*28*/ [PreserveSig] int GetRotation(out DXGI_MODE_ROTATION pRotation);
        /*29*/ [PreserveSig] int SetSourceSize(uint Width, uint Height);
        /*30*/ [PreserveSig] int GetSourceSize(out uint pWidth, out uint pHeight);
        /*31*/ [PreserveSig] int SetMaximumFrameLatency(uint MaxLatency);
        /*32*/ [PreserveSig] int GetMaximumFrameLatency(out uint pMaxLatency);
        /*33*/ [PreserveSig] IntPtr GetFrameLatencyWaitableObject();
        /*34*/ [PreserveSig] int SetMatrixTransform([In] ref DXGI_MATRIX_3X2_F pMatrix);
        /*35*/ [PreserveSig] int GetMatrixTransform(out DXGI_MATRIX_3X2_F pMatrix);
        /*36*/ [PreserveSig] uint GetCurrentBackBufferIndex();
        /*37*/ [PreserveSig] int CheckColorSpaceSupport(DXGI_COLOR_SPACE_TYPE ColorSpace, out uint pColorSpaceSupport);
        /*38*/ [PreserveSig] int SetColorSpace1(DXGI_COLOR_SPACE_TYPE ColorSpace);
        /*39*/ [PreserveSig] int ResizeBuffers1(uint BufferCount, uint Width, uint Height, DXGI_FORMAT Format, DXGI_SWAP_CHAIN_FLAG SwapChainFlags, uint* pCreationNodeMask, IntPtr* ppPresentQueue);
        /*40*/ [PreserveSig] int SetHDRMetaData(DXGI_HDR_METADATA_TYPE Type, uint Size, void* pMetaData);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Exports
    // ------------------------------------------------------------------------------------------------------------
    public static class DxgiApi
    {
        /// <summary>Native System32\dxgi.dll. riid = typeof(IDXGIFactory2..7).GUID; cast ppFactory.</summary>
        [DllImport("dxgi.dll", EntryPoint = "CreateDXGIFactory2", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CreateDXGIFactory2(DXGI_CREATE_FACTORY Flags, [In] ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppFactory);

        /// <summary>Streamline interposer (sl.interposer.dll next to the exe): returns the SL proxy factory
        /// (its swap chains are SL proxies, required for DLSS-G / Reflex).</summary>
        [DllImport("sl.interposer.dll", EntryPoint = "CreateDXGIFactory2", ExactSpelling = true)]
        public static extern int CreateDXGIFactory2SL(DXGI_CREATE_FACTORY Flags, [In] ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppFactory);
    }
}
