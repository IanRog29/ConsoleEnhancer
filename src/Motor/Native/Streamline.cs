// Streamline.cs - NVIDIA Streamline SDK v2.14.16 interop (sl.interposer.dll) for C# 5 / .NET Framework 4.8 x64.
//
// Ported 1:1 from deps\streamline\include (sl_struct.h, sl_consts.h, sl_core_types.h, sl_core_api.h, sl_result.h,
// sl_version.h, sl_dlss.h, sl_dlss_g.h, sl_reflex.h, sl_pcl.h). Every C++ type keeps its name with an "Sl" prefix
// (sl::Preferences -> SlPreferences, sl::Result -> SlResult ...) so nothing collides with the D3D12/DXGI interop
// that lives in the same namespace. Enum members keep the C++ spelling (eOk, eErrorOSDisabledHWS ...) so that
// ToString() matches the SL log and docs.
//
// ABI notes
// - Every SL struct derives from sl::BaseStructure {next*, StructType(GUID), size_t structVersion} = 32 bytes. Here
//   it is the first field "Base". Create() fills it exactly like the C++ constructor (GUID + version from the
//   SL_STRUCT_BEGIN macro) and sets the same member defaults as the C++ in-class initializers.
// - C++ references (const T&) are pointers: "ref T" for structs, IntPtr for FrameToken (opaque, owned by SL).
// - bool (C++) = byte, sl::Boolean = byte enum (enum Boolean : char), enum class without base = int.
// - All structs are blittable (SlReflexState uses fixed buffers for its 64-entry report arrays) so P/Invoke pins
//   them and nothing is copied or allocated per call.
// - Feature functions (slDLSS*, slDLSSG*, slReflex*, slPCL*) are not exports: they are fetched by name through
//   slGetFeatureFunction, which only works after slSetD3DDevice (see SlFns).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace ConsoleEnhancer.Motor.Native
{
    // ------------------------------------------------------------------------------------------------ constants

    public static class SlConst
    {
        public const uint SL_VERSION_MAJOR = 2;
        public const uint SL_VERSION_MINOR = 14;
        public const uint SL_VERSION_PATCH = 16;
        public const ulong kSDKVersionMagic = 0xfedc;
        // (major << 48) | (minor << 32) | (patch << 16) | magic = 0x0002000E0010FEDC
        public const ulong kSDKVersion = ((ulong)SL_VERSION_MAJOR << 48) | ((ulong)SL_VERSION_MINOR << 32) |
                                         ((ulong)SL_VERSION_PATCH << 16) | kSDKVersionMagic;

        public const float INVALID_FLOAT = 3.40282346638528859811704183484516925440e38f;
        public const uint INVALID_UINT = 0xffffffff;
        public const uint MAX_FRAMES_IN_FLIGHT = 6;
        public const int kReflexFrameReportCount = 64;

        public const uint kStructVersion1 = 1, kStructVersion2 = 2, kStructVersion3 = 3, kStructVersion4 = 4,
                          kStructVersion5 = 5, kStructVersion6 = 6, kStructVersion7 = 7;

        // sl::FrameToken {830A0F35-DB84-4171-A804-59B206499B18}. Opaque here; layout inside SL (MSVC):
        // vfptr @0, BaseStructure @8, std::atomic<uint32_t> counter @40.
        public static readonly Guid FrameTokenType = SlGuid.Make(0x830a0f35, 0xdb84, 0x4171, 0xa8, 0x04, 0x59, 0xb2, 0x06, 0x49, 0x9b, 0x18);

        internal static readonly Guid ResourceAllocationDescType = SlGuid.Make(0x00bb57e5, 0x49a2, 0x4c23, 0xa5, 0x19, 0xab, 0x92, 0x86, 0xe7, 0x40, 0x14);
    }

    static class SlGuid
    {
        // Same argument order as the C++ StructType({ d1, d2, d3, { b0..b7 } }) initializers.
        public static Guid Make(uint d1, ushort d2, ushort d3, byte b0, byte b1, byte b2, byte b3, byte b4, byte b5, byte b6, byte b7)
        {
            return new Guid(d1, d2, d3, b0, b1, b2, b3, b4, b5, b6, b7);
        }
    }

    // ------------------------------------------------------------------------------------------------ enums

    // sl_result.h: enum class Result (int)
    public enum SlResult : int
    {
        eOk,
        eErrorIO,
        eErrorDriverOutOfDate,
        eErrorOSOutOfDate,
        eErrorOSDisabledHWS,
        eErrorDeviceNotCreated,
        eErrorNoSupportedAdapterFound,
        eErrorAdapterNotSupported,
        eErrorNoPlugins,
        eErrorVulkanAPI,
        eErrorDXGIAPI,
        eErrorD3DAPI,
        eErrorNRDAPI,           // NRD was removed
        eErrorNVAPI,
        eErrorReflexAPI,
        eErrorNGXFailed,
        eErrorJSONParsing,
        eErrorMissingProxy,
        eErrorMissingResourceState,
        eErrorInvalidIntegration,
        eErrorMissingInputParameter,
        eErrorNotInitialized,
        eErrorComputeFailed,
        eErrorInitNotCalled,
        eErrorExceptionHandler,
        eErrorInvalidParameter,
        eErrorMissingConstants,
        eErrorDuplicatedConstants,
        eErrorMissingOrInvalidAPI,
        eErrorCommonConstantsMissing,
        eErrorUnsupportedInterface,
        eErrorFeatureMissing,
        eErrorFeatureNotSupported,
        eErrorFeatureMissingHooks,
        eErrorFeatureFailedToLoad,
        eErrorFeatureWrongPriority,
        eErrorFeatureMissingDependency,
        eErrorFeatureManagerInvalidState,
        eErrorInvalidState,
        eWarnOutOfVRAM
    }

    // sl_core_types.h: using Feature = uint32_t (kFeature* constants)
    public enum SlFeature : uint
    {
        DLSS = 0,
        NRD_INVALID = 1,        // removed
        NIS = 2,
        Reflex = 3,
        PCL = 4,
        DeepDVC = 5,
        Latewarp = 6,
        DLSS_G = 1000,   // (1.2.4 ronda 6, QUITAFG: el motor ya no la pide)
        DLSS_RR = 1001,
        NvPerf = 1002,
        DirectSR = 1003,
        DLSS_NR = 1004,
        ImGUI = 9999,
        Common = 0xffffffff     // kFeatureCommon, not to be used directly
    }

    // sl_core_types.h: using BufferType = uint32_t (kBufferType* constants)
    public enum SlBufferType : uint
    {
        Depth = 0,
        MotionVectors = 1,
        HUDLessColor = 2,
        ScalingInputColor = 3,
        ScalingOutputColor = 4,
        Normals = 5,
        Roughness = 6,
        Albedo = 7,
        SpecularAlbedo = 8,
        IndirectAlbedo = 9,
        SpecularMotionVectors = 10,
        DisocclusionMask = 11,
        Emissive = 12,
        Exposure = 13,
        NormalRoughness = 14,
        DiffuseHitNoisy = 15,
        DiffuseHitDenoised = 16,
        SpecularHitNoisy = 17,
        SpecularHitDenoised = 18,
        ShadowNoisy = 19,
        ShadowDenoised = 20,
        AmbientOcclusionNoisy = 21,
        AmbientOcclusionDenoised = 22,
        UIColorAndAlpha = 23,
        ShadowHint = 24,
        ReflectionHint = 25,
        ParticleHint = 26,
        TransparencyHint = 27,
        AnimatedTextureHint = 28,
        BiasCurrentColorHint = 29,
        RaytracingDistance = 30,
        ReflectionMotionVectors = 31,
        Position = 32,
        InvalidDepthMotionHint = 33,
        Alpha = 34,
        OpaqueColor = 35,
        ReactiveMaskHint = 36,
        TransparencyAndCompositionMaskHint = 37,
        ReflectedAlbedo = 38,
        ColorBeforeParticles = 39,
        ColorBeforeTransparency = 40,
        ColorBeforeFog = 41,
        SpecularHitDistance = 42,
        SpecularRayDirectionHitDistance = 43,
        SpecularRayDirection = 44,
        DiffuseHitDistance = 45,
        DiffuseRayDirectionHitDistance = 46,
        DiffuseRayDirection = 47,
        HiResDepth = 48,
        LinearDepth = 49,
        BidirectionalDistortionField = 50,
        TransparencyLayer = 51,
        TransparencyLayerOpacity = 52,
        Backbuffer = 53,
        NoWarpMask = 54,
        ColorAfterParticles = 55,
        ColorAfterTransparency = 56,
        ColorAfterFog = 57,
        ScreenSpaceSubsurfaceScatteringGuide = 58,
        ColorBeforeScreenSpaceSubsurfaceScattering = 59,
        ColorAfterScreenSpaceSubsurfaceScattering = 60,
        ScreenSpaceRefractionGuide = 61,
        ColorBeforeScreenSpaceRefraction = 62,
        ColorAfterScreenSpaceRefraction = 63,
        DepthOfFieldGuide = 64,
        ColorBeforeDepthOfField = 65,
        ColorAfterDepthOfField = 66,
        ScalingOutputAlpha = 67,
        ResponsivityMask = 68,
        UIAlpha = 69,
        UpliftInputColor = 70,
        UpliftOutputColor = 71,
        UpliftControlMask = 72
    }

    // enum class ResourceType : char
    public enum SlResourceType : byte
    {
        eTex2d, eBuffer, eCommandQueue, eCommandBuffer, eCommandPool, eFence, eSwapchain, eHostFence, eUnknown, eCount
    }

    // enum ResourceLifecycle (plain C enum = int)
    public enum SlResourceLifecycle : int
    {
        eOnlyValidNow,          // SL copies the resource if a later feature (DLSS-G at Present) needs it
        eValidUntilPresent,     // untouched until Present (SL holds a reference until a null tag is set)
        eValidUntilEvaluate     // untouched until slEvaluateFeature returns
    }

    // enum class LogLevel : uint32_t
    public enum SlLogLevel : uint { eOff, eDefault, eVerbose, eCount }

    // enum class LogType : uint32_t
    public enum SlLogType : uint { eInfo, eWarn, eError, eCount }

    // enum class PreferenceFlags : uint64_t
    [Flags]
    public enum SlPreferenceFlags : ulong
    {
        None = 0,
        eDisableCLStateTracking = 1 << 0,
        eDisableDebugText = 1 << 1,
        eUseManualHooking = 1 << 2,
        eAllowOTA = 1 << 3,
        eBypassOSVersionCheck = 1 << 4,
        eUseDXGIFactoryProxy = 1 << 5,
        eLoadDownloadedPlugins = 1 << 6,
        eUseFrameBasedResourceTagging = 1 << 7,
        eAll = eDisableCLStateTracking | eDisableDebugText | eUseManualHooking | eAllowOTA | eBypassOSVersionCheck |
               eUseDXGIFactoryProxy | eLoadDownloadedPlugins | eUseFrameBasedResourceTagging
    }

    // sl_appidentity.h: enum class EngineType : uint32_t
    public enum SlEngineType : uint { eCustom, eUnreal, eUnity, eCount }

    // sl_device_wrappers.h: enum class RenderAPI : uint32_t
    public enum SlRenderAPI : uint { eD3D11, eD3D12, eVulkan, eCount }

    // enum class FeatureRequirementFlags : uint32_t
    [Flags]
    public enum SlFeatureRequirementFlags : uint
    {
        None = 0,
        eD3D11Supported = 1 << 0,
        eD3D12Supported = 1 << 1,
        eVulkanSupported = 1 << 2,
        eVSyncOffRequired = 1 << 3,
        eHardwareSchedulingRequired = 1 << 4,
        eAll = eD3D11Supported | eD3D12Supported | eVulkanSupported | eVSyncOffRequired | eHardwareSchedulingRequired
    }

    // sl_consts.h: enum Boolean : char
    public enum SlBoolean : byte { eFalse, eTrue, eInvalid }

    // PrecisionInfo::PrecisionFormula : uint32_t
    public enum SlPrecisionFormula : uint { eNoTransform = 0, eLinearTransform }

    // sl_dlss.h
    public enum SlDLSSMode : uint
    {
        eOff, eMaxPerformance, eBalanced, eMaxQuality, eUltraPerformance, eUltraQuality, eDLAA, eCount
    }

    public enum SlDLSSPreset : uint
    {
        eDefault = 0,
        // ePresetA..D removed in this SDK ("use presets J or K")
        ePresetE = 5,   // Deprecated (CNN)
        ePresetF = 6,   // Deprecated (CNN)
        ePresetG = 7,   // Reverts to default, not recommended
        ePresetH = 8,   // Reverts to default, not recommended
        ePresetI = 9,   // Reverts to default, not recommended
        ePresetJ = 10,  // Transformer, like K with slightly less ghosting / more flicker
        ePresetK = 11,  // Transformer, default for DLAA/Balanced/Quality
        ePresetL = 12,  // Transformer 2nd gen, default for UltraPerformance (sharper, more expensive)
        ePresetM = 13,  // Transformer 2nd gen, default for Performance (L quality, closer to K speed)
        ePresetN = 14,  // Reverts to default, not recommended
        ePresetO = 15,  // Reverts to default, not recommended
        eCount
    }

    // (sl_dlss_g.h: 1.2.4 ronda 6, QUITAFG: fuera los tipos de DLSS-G, el motor ya no la usa)

    // sl_reflex.h: enum ReflexMode (plain C enum = int)
    public enum SlReflexMode : int { eOff, eLowLatency, eLowLatencyWithBoost, ReflexMode_eCount }

    // sl_pcl.h
    public enum SlPCLHotKey : short { eUsePingMessage = 0, eVK_F13 = 0x7C, eVK_F14 = 0x7D, eVK_F15 = 0x7E }

    public enum SlPCLMarker : uint
    {
        eSimulationStart = 0,
        eSimulationEnd = 1,
        eRenderSubmitStart = 2,
        eRenderSubmitEnd = 3,
        ePresentStart = 4,
        ePresentEnd = 5,
        // eInputSample = 6 deprecated
        eTriggerFlash = 7,
        ePCLatencyPing = 8,
        eOutOfBandRenderSubmitStart = 9,
        eOutOfBandRenderSubmitEnd = 10,
        eOutOfBandPresentStart = 11,
        eOutOfBandPresentEnd = 12,
        eControllerInputSample = 13,
        eDeltaTCalculation = 14,
        eLateWarpPresentStart = 15,
        eLateWarpPresentEnd = 16,
        eCameraConstructed = 17,
        eLateWarpRenderSubmitStart = 18,
        eLateWarpRenderSubmitEnd = 19,
        eVendorInternalAsyncPresentStart = 20,
        eVendorInternalAsyncPresentEnd = 21,
        eNumPresentsInBatch = 22,
        eMaximum
    }

    // ------------------------------------------------------------------------------------------------ basic types

    // sl::BaseStructure: next @0, structType @8 (16-byte GUID, align 4), structVersion (size_t) @24. Size 32.
    [StructLayout(LayoutKind.Sequential)]
    public struct SlBaseStructure
    {
        public IntPtr next;
        public Guid structType;
        public UIntPtr structVersion;

        public SlBaseStructure(Guid type, uint version)
        {
            next = IntPtr.Zero;
            structType = type;
            structVersion = new UIntPtr(version);
        }
    }

    // sl::Version {major, minor, build} 12 bytes
    [StructLayout(LayoutKind.Sequential)]
    public struct SlVersion
    {
        public uint major, minor, build;
        public override string ToString() { return major + "." + minor + "." + build; }
    }

    // sl::Extent - NOTE the order: top, left, width, height. All zero = whole resource.
    [StructLayout(LayoutKind.Sequential)]
    public struct SlExtent
    {
        public uint top, left, width, height;
        public SlExtent(uint left, uint top, uint width, uint height) { this.top = top; this.left = left; this.width = width; this.height = height; }
        public override string ToString() { return left + "," + top + " " + width + "x" + height; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SlUInt2 { public uint x, y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SlFloat2
    {
        public float x, y;
        public SlFloat2(float x, float y) { this.x = x; this.y = y; }
        public static SlFloat2 Invalid { get { return new SlFloat2(SlConst.INVALID_FLOAT, SlConst.INVALID_FLOAT); } }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SlFloat3
    {
        public float x, y, z;
        public SlFloat3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static SlFloat3 Invalid { get { return new SlFloat3(SlConst.INVALID_FLOAT, SlConst.INVALID_FLOAT, SlConst.INVALID_FLOAT); } }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SlFloat4
    {
        public float x, y, z, w;
        public SlFloat4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static SlFloat4 Invalid { get { return new SlFloat4(SlConst.INVALID_FLOAT, SlConst.INVALID_FLOAT, SlConst.INVALID_FLOAT, SlConst.INVALID_FLOAT); } }
    }

    // Row-major 4x4 (sl::float4x4 { float4 row[4]; }) 64 bytes
    [StructLayout(LayoutKind.Sequential)]
    public struct SlFloat4x4
    {
        public SlFloat4 row0, row1, row2, row3;
        public static SlFloat4x4 Identity
        {
            get
            {
                SlFloat4x4 m;
                m.row0 = new SlFloat4(1, 0, 0, 0);
                m.row1 = new SlFloat4(0, 1, 0, 0);
                m.row2 = new SlFloat4(0, 0, 1, 0);
                m.row3 = new SlFloat4(0, 0, 0, 1);
                return m;
            }
        }
        // What a default-constructed C++ float4x4 holds (each float4() = INVALID_FLOAT)
        public static SlFloat4x4 Invalid
        {
            get { SlFloat4x4 m; m.row0 = m.row1 = m.row2 = m.row3 = SlFloat4.Invalid; return m; }
        }
    }

    // ------------------------------------------------------------------------------------------------ core structs

    // sl::Preferences v1 {1CA10965-BF8E-432B-8DA1-6716D879FB14}. Size 144, renderAPI @136 (static_assert in sl.cpp).
    [StructLayout(LayoutKind.Sequential)]
    public struct SlPreferences
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0x1ca10965, 0xbf8e, 0x432b, 0x8d, 0xa1, 0x67, 0x16, 0xd8, 0x79, 0xfb, 0x14);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public byte showConsole;                    // bool
        public SlLogLevel logLevel;
        public IntPtr pathsToPlugins;               // const wchar_t**
        public uint numPathsToPlugins;
        public IntPtr pathToLogsAndData;            // const wchar_t* (absolute path, null = no log file)
        public IntPtr allocateCallback;             // PFun_ResourceAllocateCallback*
        public IntPtr releaseCallback;              // PFun_ResourceReleaseCallback*
        public IntPtr logMessageCallback;           // PFun_LogMessageCallback*
        public SlPreferenceFlags flags;
        public IntPtr featuresToLoad;               // const Feature*
        public uint numFeaturesToLoad;
        public uint applicationId;
        public SlEngineType engine;
        public IntPtr engineVersion;                // const char*
        public IntPtr projectId;                    // const char* GUID string
        public SlRenderAPI renderAPI;

        public static SlPreferences Create()
        {
            SlPreferences p = new SlPreferences();
            p.Base = new SlBaseStructure(TypeGuid, Version);
            p.showConsole = 0;
            p.logLevel = SlLogLevel.eDefault;
            p.flags = SlPreferenceFlags.eDisableCLStateTracking | SlPreferenceFlags.eAllowOTA | SlPreferenceFlags.eLoadDownloadedPlugins;
            p.engine = SlEngineType.eCustom;
            p.renderAPI = SlRenderAPI.eD3D12;
            return p;
        }
    }

    // sl::AdapterInfo v1 {0677315F-A746-4492-9F42-CB6142C9C3D4}. Size 56, vkPhysicalDevice @48 (static_assert).
    [StructLayout(LayoutKind.Sequential)]
    public struct SlAdapterInfo
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0x0677315f, 0xa746, 0x4492, 0x9f, 0x42, 0xcb, 0x61, 0x42, 0xc9, 0xc3, 0xd4);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public IntPtr deviceLUID;                   // uint8_t* -> LUID bytes
        public uint deviceLUIDSizeInBytes;
        public IntPtr vkPhysicalDevice;

        public static SlAdapterInfo Create()
        {
            SlAdapterInfo a = new SlAdapterInfo();
            a.Base = new SlBaseStructure(TypeGuid, Version);
            return a;
        }
    }

    // sl::Resource v1 {3A9D70CF-2418-4B72-8391-13F8721C7261}. Size 112, internalFlags @104, reserved @106 (static_assert).
    [StructLayout(LayoutKind.Sequential)]
    public struct SlResource
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0x3a9d70cf, 0x2418, 0x4b72, 0x83, 0x91, 0x13, 0xf8, 0x72, 0x1c, 0x72, 0x61);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public SlResourceType type;
        public IntPtr native;                       // ID3D12Resource*
        public IntPtr memory;                       // Vulkan only
        public IntPtr view;                         // Vulkan only
        public uint state;                          // D3D12_RESOURCE_STATES when SL uses it (MANDATORY)
        public uint width;
        public uint height;
        public uint nativeFormat;
        public uint mipLevels;
        public uint arrayLayers;
        public ulong gpuVirtualAddress;
        public uint flags;
        public uint usage;
        public ushort internalFlags;                // do not modify
        public ushort reserved;

        // Resource(ResourceType _type, void* _native, uint32_t _state = UINT_MAX)
        public static SlResource Create(SlResourceType type, IntPtr native, uint state)
        {
            SlResource r = new SlResource();
            r.Base = new SlBaseStructure(TypeGuid, Version);
            r.type = type;
            r.native = native;
            r.state = state;
            return r;
        }
        public static SlResource Create() { return Create(SlResourceType.eTex2d, IntPtr.Zero, SlConst.INVALID_UINT); }
        public static SlResource Tex2d(IntPtr d3d12Resource, uint d3d12State) { return Create(SlResourceType.eTex2d, d3d12Resource, d3d12State); }
    }

    // sl::ResourceTag v1 {4C6A5AAD-B445-496C-87FF-1AF3845BE653}. Size 64, extent @48 (static_assert).
    [StructLayout(LayoutKind.Sequential)]
    public struct SlResourceTag
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0x4c6a5aad, 0xb445, 0x496c, 0x87, 0xff, 0x1a, 0xf3, 0x84, 0x5b, 0xe6, 0x53);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public IntPtr resource;                     // SlResource* (null = remove tag / release SL's reference)
        public SlBufferType type;
        public SlResourceLifecycle lifecycle;
        public SlExtent extent;                     // zero = whole resource

        // ResourceTag(Resource* r, BufferType t, ResourceLifecycle l, const Extent* e = nullptr)
        public static SlResourceTag Create(IntPtr resource, SlBufferType type, SlResourceLifecycle lifecycle, SlExtent extent)
        {
            SlResourceTag t = new SlResourceTag();
            t.Base = new SlBaseStructure(TypeGuid, Version);
            t.resource = resource;
            t.type = type;
            t.lifecycle = lifecycle;
            t.extent = extent;
            return t;
        }
        public static SlResourceTag Create() { return Create(IntPtr.Zero, SlBufferType.Depth, SlResourceLifecycle.eOnlyValidNow, new SlExtent()); }
    }

    // sl::PrecisionInfo v1 {98F6E9BA-8D16-4831-A802-4D3B52FF26BF} (chain extension of ResourceTag). Size 48.
    [StructLayout(LayoutKind.Sequential)]
    public struct SlPrecisionInfo
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0x98f6e9ba, 0x8d16, 0x4831, 0xa8, 0x02, 0x4d, 0x3b, 0x52, 0xff, 0x26, 0xbf);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public SlPrecisionFormula conversionFormula;
        public float bias;
        public float scale;

        public static SlPrecisionInfo Create(SlPrecisionFormula formula, float bias, float scale)
        {
            SlPrecisionInfo p = new SlPrecisionInfo();
            p.Base = new SlBaseStructure(TypeGuid, Version);
            p.conversionFormula = formula;
            p.bias = bias;
            p.scale = scale;
            return p;
        }
        public static SlPrecisionInfo Create() { return Create(SlPrecisionFormula.eNoTransform, 0f, 1f); }
    }

    // sl::Constants v2 {DCD35AD7-4E4A-4BAD-A90C-E0C49EB23AFE}. Size 456, motionVectorsJittered @450 (static_assert).
    [StructLayout(LayoutKind.Sequential)]
    public struct SlConstants
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0xdcd35ad7, 0x4e4a, 0x4bad, 0xa9, 0x0c, 0xe0, 0xc4, 0x9e, 0xb2, 0x3a, 0xfe);
        public const uint Version = SlConst.kStructVersion2;

        public SlBaseStructure Base;
        public SlFloat4x4 cameraViewToClip;         // @32
        public SlFloat4x4 clipToCameraView;         // @96
        public SlFloat4x4 clipToLensClip;           // @160 optional
        public SlFloat4x4 clipToPrevClip;           // @224
        public SlFloat4x4 prevClipToClip;           // @288
        public SlFloat2 jitterOffset;               // @352 pixel space
        public SlFloat2 mvecScale;                  // @360 scale so that mvec*scale is in [-1,1]
        public SlFloat2 cameraPinholeOffset;        // @368 optional
        public SlFloat3 cameraPos;                  // @376
        public SlFloat3 cameraUp;                   // @388
        public SlFloat3 cameraRight;                // @400
        public SlFloat3 cameraFwd;                  // @412
        public float cameraNear;                    // @424
        public float cameraFar;                     // @428
        public float cameraFOV;                     // @432 radians
        public float cameraAspectRatio;             // @436
        public float motionVectorsInvalidValue;     // @440 only if cameraMotionIncluded == false
        public SlBoolean depthInverted;             // @444
        public SlBoolean cameraMotionIncluded;      // @445
        public SlBoolean motionVectors3D;           // @446
        public SlBoolean reset;                     // @447
        public SlBoolean orthographicProjection;    // @448
        public SlBoolean motionVectorsDilated;      // @449
        public SlBoolean motionVectorsJittered;     // @450
        public float minRelativeLinearDepthObjectSeparation; // @452 (v2)

        // Same values as a default-constructed C++ sl::Constants (everything "invalid" until the host fills it).
        public static SlConstants Create()
        {
            SlConstants c = new SlConstants();
            c.Base = new SlBaseStructure(TypeGuid, Version);
            c.cameraViewToClip = SlFloat4x4.Invalid;
            c.clipToCameraView = SlFloat4x4.Invalid;
            c.clipToLensClip = SlFloat4x4.Invalid;
            c.clipToPrevClip = SlFloat4x4.Invalid;
            c.prevClipToClip = SlFloat4x4.Invalid;
            c.jitterOffset = SlFloat2.Invalid;
            c.mvecScale = SlFloat2.Invalid;
            c.cameraPinholeOffset = SlFloat2.Invalid;
            c.cameraPos = SlFloat3.Invalid;
            c.cameraUp = SlFloat3.Invalid;
            c.cameraRight = SlFloat3.Invalid;
            c.cameraFwd = SlFloat3.Invalid;
            c.cameraNear = SlConst.INVALID_FLOAT;
            c.cameraFar = SlConst.INVALID_FLOAT;
            c.cameraFOV = SlConst.INVALID_FLOAT;
            c.cameraAspectRatio = SlConst.INVALID_FLOAT;
            c.motionVectorsInvalidValue = SlConst.INVALID_FLOAT;
            c.depthInverted = SlBoolean.eInvalid;
            c.cameraMotionIncluded = SlBoolean.eInvalid;
            c.motionVectors3D = SlBoolean.eInvalid;
            c.reset = SlBoolean.eInvalid;
            c.orthographicProjection = SlBoolean.eFalse;
            c.motionVectorsDilated = SlBoolean.eFalse;
            c.motionVectorsJittered = SlBoolean.eFalse;
            c.minRelativeLinearDepthObjectSeparation = 40.0f;
            return c;
        }

        // Constants for a video source: no camera, no jitter, zero 2D motion vectors in render-res pixel units,
        // zero depth (not inverted). Static identity camera; cameraMotionIncluded = true so SL does NOT run its
        // "compute camera motion from depth" pass. Set reset = true on the first frame and on every discontinuity.
        public static SlConstants CreateVideo(uint renderWidth, uint renderHeight, uint outputWidth, uint outputHeight, bool reset)
        {
            SlConstants c = Create();
            c.cameraViewToClip = SlFloat4x4.Identity;
            c.clipToCameraView = SlFloat4x4.Identity;
            c.clipToLensClip = SlFloat4x4.Identity;
            c.clipToPrevClip = SlFloat4x4.Identity;
            c.prevClipToClip = SlFloat4x4.Identity;
            c.jitterOffset = new SlFloat2(0f, 0f);
            c.mvecScale = new SlFloat2(1f / Math.Max(1u, renderWidth), 1f / Math.Max(1u, renderHeight));
            c.cameraPinholeOffset = new SlFloat2(0f, 0f);
            c.cameraPos = new SlFloat3(0f, 0f, 0f);
            c.cameraUp = new SlFloat3(0f, 1f, 0f);
            c.cameraRight = new SlFloat3(1f, 0f, 0f);
            c.cameraFwd = new SlFloat3(0f, 0f, 1f);
            c.cameraNear = 0.1f;
            c.cameraFar = 1000f;
            c.cameraFOV = 1.0471976f; // 60 degrees
            c.cameraAspectRatio = (float)Math.Max(1u, outputWidth) / Math.Max(1u, outputHeight);
            c.depthInverted = SlBoolean.eFalse;
            c.cameraMotionIncluded = SlBoolean.eTrue;
            c.motionVectors3D = SlBoolean.eFalse;
            c.reset = reset ? SlBoolean.eTrue : SlBoolean.eFalse;
            c.orthographicProjection = SlBoolean.eFalse;
            c.motionVectorsDilated = SlBoolean.eFalse;
            c.motionVectorsJittered = SlBoolean.eFalse;
            return c;
        }
    }

    // sl::FeatureRequirements v2 {66714097-AC6D-4BC6-8915-1E0F55A6B61F}. Size 184, vkNumOpticalFlowQueuesRequired @176 (static_assert).
    [StructLayout(LayoutKind.Sequential)]
    public struct SlFeatureRequirements
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0x66714097, 0xac6d, 0x4bc6, 0x89, 0x15, 0x1e, 0x0f, 0x55, 0xa6, 0xb6, 0x1f);
        public const uint Version = SlConst.kStructVersion2;

        public SlBaseStructure Base;
        public SlFeatureRequirementFlags flags;
        public uint maxNumCPUThreads;
        public uint maxNumViewports;
        public uint numRequiredTags;
        public IntPtr requiredTags;                 // const BufferType* (owned by SL)
        public SlVersion osVersionDetected;
        public SlVersion osVersionRequired;
        public SlVersion driverVersionDetected;
        public SlVersion driverVersionRequired;
        public uint vkNumComputeQueuesRequired;
        public uint vkNumGraphicsQueuesRequired;
        public uint vkNumDeviceExtensions;
        public IntPtr vkDeviceExtensions;
        public uint vkNumInstanceExtensions;
        public IntPtr vkInstanceExtensions;
        public uint vkNumFeatures12;
        public IntPtr vkFeatures12;
        public uint vkNumFeatures13;
        public IntPtr vkFeatures13;
        public uint vkNumOpticalFlowQueuesRequired;

        public static SlFeatureRequirements Create()
        {
            SlFeatureRequirements r = new SlFeatureRequirements();
            r.Base = new SlBaseStructure(TypeGuid, Version);
            return r;
        }

        public SlBufferType[] GetRequiredTags()
        {
            SlBufferType[] tags = new SlBufferType[numRequiredTags];
            for (int i = 0; i < tags.Length && requiredTags != IntPtr.Zero; i++)
                tags[i] = (SlBufferType)(uint)Marshal.ReadInt32(requiredTags, i * 4);
            return tags;
        }
    }

    // sl::FeatureVersion v1 {6D5B51F0-076B-486D-9995-5A561043F5C1}. Size 56, versionNGX @44 (static_assert).
    [StructLayout(LayoutKind.Sequential)]
    public struct SlFeatureVersion
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0x6d5b51f0, 0x076b, 0x486d, 0x99, 0x95, 0x5a, 0x56, 0x10, 0x43, 0xf5, 0xc1);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public SlVersion versionSL;
        public SlVersion versionNGX;

        public static SlFeatureVersion Create()
        {
            SlFeatureVersion v = new SlFeatureVersion();
            v.Base = new SlBaseStructure(TypeGuid, Version);
            return v;
        }
    }

    // sl::ViewportHandle v1 {171B6435-9B3C-4FC8-9994-FBE52569AAA4}. Size 40. Passed by reference everywhere.
    [StructLayout(LayoutKind.Sequential)]
    public struct SlViewportHandle
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0x171b6435, 0x9b3c, 0x4fc8, 0x99, 0x94, 0xfb, 0xe5, 0x25, 0x69, 0xaa, 0xa4);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public uint value;                          // default UINT_MAX; the app picks a unique id (e.g. 0)

        public static SlViewportHandle Create(uint id)
        {
            SlViewportHandle v = new SlViewportHandle();
            v.Base = new SlBaseStructure(TypeGuid, Version);
            v.value = id;
            return v;
        }
        public static SlViewportHandle Create() { return Create(SlConst.INVALID_UINT); }
    }

    // sl::APIError { union { HRESULT hres; VkResult vkRes; } } 4 bytes
    [StructLayout(LayoutKind.Sequential)]
    public struct SlAPIError { public int hres; }

    // ------------------------------------------------------------------------------------------------ DLSS (SR)

    // sl::DLSSOptions v3 {6AC826E4-4C61-4101-A92D-638D421057B8}. Size 88.
    [StructLayout(LayoutKind.Sequential)]
    public struct SlDLSSOptions
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0x6ac826e4, 0x4c61, 0x4101, 0xa9, 0x2d, 0x63, 0x8d, 0x42, 0x10, 0x57, 0xb8);
        public const uint Version = SlConst.kStructVersion3;

        public SlBaseStructure Base;
        public SlDLSSMode mode;
        public uint outputWidth;
        public uint outputHeight;
        public float sharpness;                     // deprecated
        public float preExposure;
        public float exposureScale;
        public SlBoolean colorBuffersHDR;
        public SlBoolean indicatorInvertAxisX;
        public SlBoolean indicatorInvertAxisY;
        public SlDLSSPreset dlaaPreset;
        public SlDLSSPreset qualityPreset;
        public SlDLSSPreset balancedPreset;
        public SlDLSSPreset performancePreset;
        public SlDLSSPreset ultraPerformancePreset;
        public SlDLSSPreset ultraQualityPreset;
        public SlBoolean useAutoExposure;
        public SlBoolean alphaUpscalingEnabled;

        public static SlDLSSOptions Create()
        {
            SlDLSSOptions o = new SlDLSSOptions();
            o.Base = new SlBaseStructure(TypeGuid, Version);
            o.mode = SlDLSSMode.eOff;
            o.outputWidth = SlConst.INVALID_UINT;
            o.outputHeight = SlConst.INVALID_UINT;
            o.sharpness = 0f;
            o.preExposure = 1f;
            o.exposureScale = 1f;
            o.colorBuffersHDR = SlBoolean.eTrue;
            o.indicatorInvertAxisX = SlBoolean.eFalse;
            o.indicatorInvertAxisY = SlBoolean.eFalse;
            o.useAutoExposure = SlBoolean.eFalse;
            o.alphaUpscalingEnabled = SlBoolean.eFalse;
            return o;
        }

        public void SetAllPresets(SlDLSSPreset p)
        {
            dlaaPreset = qualityPreset = balancedPreset = performancePreset = ultraPerformancePreset = ultraQualityPreset = p;
        }
    }

    // sl::DLSSOptimalSettings v1 {EF1D0957-FD58-4DF7-B504-8B69D8AA6B76}. Size 64.
    [StructLayout(LayoutKind.Sequential)]
    public struct SlDLSSOptimalSettings
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0xef1d0957, 0xfd58, 0x4df7, 0xb5, 0x04, 0x8b, 0x69, 0xd8, 0xaa, 0x6b, 0x76);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public uint optimalRenderWidth;
        public uint optimalRenderHeight;
        public float optimalSharpness;
        public uint renderWidthMin;
        public uint renderHeightMin;
        public uint renderWidthMax;
        public uint renderHeightMax;

        public static SlDLSSOptimalSettings Create()
        {
            SlDLSSOptimalSettings s = new SlDLSSOptimalSettings();
            s.Base = new SlBaseStructure(TypeGuid, Version);
            return s;
        }
    }

    // sl::DLSSState v1 {9366B056-8C01-463C-BB91-E68782636CE9}. Size 40.
    [StructLayout(LayoutKind.Sequential)]
    public struct SlDLSSState
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0x9366b056, 0x8c01, 0x463c, 0xbb, 0x91, 0xe6, 0x87, 0x82, 0x63, 0x6c, 0xe9);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public ulong estimatedVRAMUsageInBytes;

        public static SlDLSSState Create()
        {
            SlDLSSState s = new SlDLSSState();
            s.Base = new SlBaseStructure(TypeGuid, Version);
            return s;
        }
    }

    // ------------------------------------------------------------------------------------------------ Reflex

    // sl::ReflexOptions v1 {F03AF81A-6D0B-4902-A651-C4965E215434}. Size 48.
    [StructLayout(LayoutKind.Sequential)]
    public struct SlReflexOptions
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0xf03af81a, 0x6d0b, 0x4902, 0xa6, 0x51, 0xc4, 0x96, 0x5e, 0x21, 0x54, 0x34);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public SlReflexMode mode;                   // @32
        public uint frameLimitUs;                   // @36 0 = no cap
        public byte useMarkersToOptimize;           // @40 bool
        public ushort virtualKey;                   // @42 0, VK_F13..F15
        public uint idThread;                       // @44

        public static SlReflexOptions Create()
        {
            SlReflexOptions o = new SlReflexOptions();
            o.Base = new SlBaseStructure(TypeGuid, Version);
            o.mode = SlReflexMode.eOff;
            return o;
        }
    }

    // sl::ReflexReport v1 {0D569B37-A1C8-4453-BE4D-40F4DE57952B}. Size 152. Timestamps in microseconds (NvAPI_D3D_GetLatency).
    [StructLayout(LayoutKind.Sequential)]
    public struct SlReflexReport
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0x0d569b37, 0xa1c8, 0x4453, 0xbe, 0x4d, 0x40, 0xf4, 0xde, 0x57, 0x95, 0x2b);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public ulong frameID;
        public ulong inputSampleTime;
        public ulong simStartTime;
        public ulong simEndTime;
        public ulong renderSubmitStartTime;
        public ulong renderSubmitEndTime;
        public ulong presentStartTime;
        public ulong presentEndTime;
        public ulong driverStartTime;
        public ulong driverEndTime;
        public ulong osRenderQueueStartTime;
        public ulong osRenderQueueEndTime;
        public ulong gpuRenderStartTime;
        public ulong gpuRenderEndTime;
        public uint gpuActiveRenderTimeUs;
        public uint gpuFrameTimeUs;
    }

    // sl::ReflexReport2 v1 {68BB0632-5E1C-402B-899D-B49F633C56C2}. Size 48.
    [StructLayout(LayoutKind.Sequential)]
    public struct SlReflexReport2
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0x68bb0632, 0x5e1c, 0x402b, 0x89, 0x9d, 0xb4, 0x9f, 0x63, 0x3c, 0x56, 0xc2);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public ulong cameraConstructedTime;
        public uint crossAdapterCopyTimeUs;
    }

    // sl::ReflexState v2 {F0BB5985-DAF9-4728-B2FD-AE80A2BD7989}. Size 12848.
    //   frameReport[64] @40 (64 x 152), flashIndicatorDriverControlled @9768, frameReport2[64] @9776 (64 x 48).
    // The arrays are fixed ulong buffers (8-byte aligned like the C++ arrays) so the struct stays blittable; use
    // GetFrameReport(i)/GetFrameReport2(i). Reuse one instance: slReflexGetState overwrites it in place.
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SlReflexState
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0xf0bb5985, 0xdaf9, 0x4728, 0xb2, 0xfd, 0xae, 0x80, 0xa2, 0xbd, 0x79, 0x89);
        public const uint Version = SlConst.kStructVersion2;
        public const int ReportSize = 152, Report2Size = 48;

        public SlBaseStructure Base;
        public byte lowLatencyAvailable;            // @32 bool
        public byte latencyReportAvailable;         // @33 bool
        public uint statsWindowMessage;             // @36
        public fixed ulong frameReportRaw[SlConst.kReflexFrameReportCount * ReportSize / 8];   // @40 SlReflexReport[64]
        public byte flashIndicatorDriverControlled; // @9768 bool
        public fixed ulong frameReport2Raw[SlConst.kReflexFrameReportCount * Report2Size / 8]; // @9776 SlReflexReport2[64]

        public static SlReflexState Create()
        {
            SlReflexState s = new SlReflexState();
            s.Base = new SlBaseStructure(TypeGuid, Version);
            SlBaseStructure b1 = new SlBaseStructure(SlReflexReport.TypeGuid, SlReflexReport.Version);
            SlBaseStructure b2 = new SlBaseStructure(SlReflexReport2.TypeGuid, SlReflexReport2.Version);
            SlReflexReport* r1 = (SlReflexReport*)s.frameReportRaw;
            SlReflexReport2* r2 = (SlReflexReport2*)s.frameReport2Raw;
            for (int i = 0; i < SlConst.kReflexFrameReportCount; i++) { r1[i].Base = b1; r2[i].Base = b2; }
            return s;
        }

        public SlReflexReport GetFrameReport(int i)
        {
            if ((uint)i >= (uint)SlConst.kReflexFrameReportCount) throw new ArgumentOutOfRangeException("i");
            fixed (ulong* p = frameReportRaw) { return ((SlReflexReport*)p)[i]; }
        }

        public SlReflexReport2 GetFrameReport2(int i)
        {
            if ((uint)i >= (uint)SlConst.kReflexFrameReportCount) throw new ArgumentOutOfRangeException("i");
            fixed (ulong* p = frameReport2Raw) { return ((SlReflexReport2*)p)[i]; }
        }
    }

    // ------------------------------------------------------------------------------------------------ PCL

    // sl::PCLOptions v1 {CFA32F9B-023C-420E-9056-6832B74F89B4}. Size 40.
    [StructLayout(LayoutKind.Sequential)]
    public struct SlPCLOptions
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0xcfa32f9b, 0x023c, 0x420e, 0x90, 0x56, 0x68, 0x32, 0xb7, 0x4f, 0x89, 0xb4);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public SlPCLHotKey virtualKey;              // @32 int16
        public uint idThread;                       // @36

        public static SlPCLOptions Create()
        {
            SlPCLOptions o = new SlPCLOptions();
            o.Base = new SlBaseStructure(TypeGuid, Version);
            o.virtualKey = SlPCLHotKey.eUsePingMessage;
            return o;
        }
    }

    // sl::PCLState v1 {CFA32F9B-023C-420E-9056-6832B74F89B5}. Size 40.
    [StructLayout(LayoutKind.Sequential)]
    public struct SlPCLState
    {
        public static readonly Guid TypeGuid = SlGuid.Make(0xcfa32f9b, 0x023c, 0x420e, 0x90, 0x56, 0x68, 0x32, 0xb7, 0x4f, 0x89, 0xb5);
        public const uint Version = SlConst.kStructVersion1;

        public SlBaseStructure Base;
        public uint statsWindowMessage;             // PCL ping window message id (if virtualKey == 0)

        public static SlPCLState Create()
        {
            SlPCLState s = new SlPCLState();
            s.Base = new SlBaseStructure(TypeGuid, Version);
            return s;
        }
    }

    // ------------------------------------------------------------------------------------------------ callbacks

    // using PFun_LogMessageCallback = void(LogType type, const char* msg);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SlLogMessageCallback(SlLogType type, IntPtr msg);

    // using PFunOnAPIErrorCallback = void(const APIError& lastError);  (called on the DLSS-G present thread)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SlOnAPIErrorCallback(ref SlAPIError lastError);

    // ------------------------------------------------------------------------------------------------ exports

    // Raw exports of sl.interposer.dll (exports.def). Loaded from the exe folder (production DLLs copied there).
    public static class SlApi
    {
        public const string Dll = "sl.interposer.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slInit(ref SlPreferences pref, ulong sdkVersion);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slShutdown();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slIsFeatureSupported(SlFeature feature, ref SlAdapterInfo adapterInfo);

        // bool& loaded -> 1-byte C++ bool
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slIsFeatureLoaded(SlFeature feature, out byte loaded);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slSetFeatureLoaded(SlFeature feature, [MarshalAs(UnmanagedType.U1)] bool loaded);

        // const sl::BaseStructure** inputs: pointer to an array of pointers (viewport, local tags, options ...)
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slEvaluateFeature(SlFeature feature, IntPtr frameToken, IntPtr inputs, uint numInputs, IntPtr cmdBuffer);

        [DllImport(Dll, EntryPoint = "slEvaluateFeature", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slEvaluateFeatureArray(SlFeature feature, IntPtr frameToken, IntPtr[] inputs, uint numInputs, IntPtr cmdBuffer);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slAllocateResources(IntPtr cmdBuffer, SlFeature feature, ref SlViewportHandle viewport);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slFreeResources(SlFeature feature, ref SlViewportHandle viewport);

        // Deprecated (still exported). Fails with eErrorInvalidIntegration if eUseFrameBasedResourceTagging is set.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slSetTag(ref SlViewportHandle viewport, IntPtr tags, uint numTags, IntPtr cmdBuffer);

        // Requires PreferenceFlags.eUseFrameBasedResourceTagging. tags = SlResourceTag*, each tag.resource = SlResource*.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slSetTagForFrame(IntPtr frameToken, ref SlViewportHandle viewport, IntPtr tags, uint numTags, IntPtr cmdBuffer);

        [DllImport(Dll, EntryPoint = "slSetTagForFrame", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slSetTagForFrameArray(IntPtr frameToken, ref SlViewportHandle viewport, [In] SlResourceTag[] tags, uint numTags, IntPtr cmdBuffer);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slSetConstants(ref SlConstants values, IntPtr frameToken, ref SlViewportHandle viewport);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slGetFeatureRequirements(SlFeature feature, ref SlFeatureRequirements requirements);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slGetFeatureVersion(SlFeature feature, ref SlFeatureVersion version);

        // void** baseInterface: replaced in place by the SL proxy (manual hooking / 3rd-party SDKs)
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slUpgradeInterface(ref IntPtr baseInterface);

        // Returns an AddRef'd native interface (Release it).
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slGetNativeInterface(IntPtr proxyInterface, out IntPtr baseInterface);

        // void*& function
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
        public static extern SlResult slGetFeatureFunction(SlFeature feature, [MarshalAs(UnmanagedType.LPStr)] string functionName, out IntPtr function);

        // FrameToken*& token, const uint32_t* frameIndex (null = SL counts frames)
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slGetNewFrameToken(out IntPtr token, IntPtr frameIndexOrNull);

        [DllImport(Dll, EntryPoint = "slGetNewFrameToken", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slGetNewFrameTokenIndexed(out IntPtr token, ref uint frameIndex);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern SlResult slSetD3DDevice(IntPtr d3dDevice);

        // --- DXGI / D3D12 entry points exported by sl.interposer.dll (the "proxy" integration). They load the
        //     system dxgi.dll / d3d12.dll and wrap the result. Same signatures as the system functions.
        [DllImport(Dll, EntryPoint = "D3D12CreateDevice", ExactSpelling = true)]
        public static extern int D3D12CreateDevice(IntPtr adapter, int minimumFeatureLevel, ref Guid riid, out IntPtr device);

        [DllImport(Dll, EntryPoint = "CreateDXGIFactory1", ExactSpelling = true)]
        public static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

        [DllImport(Dll, EntryPoint = "CreateDXGIFactory2", ExactSpelling = true)]
        public static extern int CreateDXGIFactory2(uint flags, ref Guid riid, out IntPtr factory);
    }

    // ------------------------------------------------------------------------------------------------ feature functions

    // Feature functions obtained with slGetFeatureFunction(feature, "name") like SL_FEATURE_FUN_IMPORT_STATIC does.
    // Only valid after slSetD3DDevice (else eErrorNotInitialized). Cached; Reset() is called by Sl.Shutdown().
    public static class SlFns
    {
        // sl_dlss.h
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate SlResult PFun_slDLSSGetOptimalSettings(ref SlDLSSOptions options, ref SlDLSSOptimalSettings settings);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate SlResult PFun_slDLSSGetState(ref SlViewportHandle viewport, ref SlDLSSState state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate SlResult PFun_slDLSSSetOptions(ref SlViewportHandle viewport, ref SlDLSSOptions options);
        // sl_reflex.h
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate SlResult PFun_slReflexGetState(ref SlReflexState state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate SlResult PFun_slReflexSleep(IntPtr frameToken);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate SlResult PFun_slReflexSetOptions(ref SlReflexOptions options);
        // sl_pcl.h
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate SlResult PFun_slPCLSetMarker(SlPCLMarker marker, IntPtr frameToken);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate SlResult PFun_slPCLGetState(ref SlPCLState state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate SlResult PFun_slPCLSetOptions(ref SlPCLOptions options);

        static PFun_slDLSSGetOptimalSettings s_dlssGetOptimalSettings;
        static PFun_slDLSSGetState s_dlssGetState;
        static PFun_slDLSSSetOptions s_dlssSetOptions;
        static PFun_slReflexGetState s_reflexGetState;
        static PFun_slReflexSleep s_reflexSleep;
        static PFun_slReflexSetOptions s_reflexSetOptions;
        static PFun_slPCLSetMarker s_pclSetMarker;
        static PFun_slPCLGetState s_pclGetState;
        static PFun_slPCLSetOptions s_pclSetOptions;

        static readonly Dictionary<string, IntPtr> s_raw = new Dictionary<string, IntPtr>();

        public static SlResult Get(out PFun_slDLSSGetOptimalSettings fn) { return Fetch(SlFeature.DLSS, "slDLSSGetOptimalSettings", ref s_dlssGetOptimalSettings, out fn); }
        public static SlResult Get(out PFun_slDLSSGetState fn) { return Fetch(SlFeature.DLSS, "slDLSSGetState", ref s_dlssGetState, out fn); }
        public static SlResult Get(out PFun_slDLSSSetOptions fn) { return Fetch(SlFeature.DLSS, "slDLSSSetOptions", ref s_dlssSetOptions, out fn); }
        public static SlResult Get(out PFun_slReflexGetState fn) { return Fetch(SlFeature.Reflex, "slReflexGetState", ref s_reflexGetState, out fn); }
        public static SlResult Get(out PFun_slReflexSleep fn) { return Fetch(SlFeature.Reflex, "slReflexSleep", ref s_reflexSleep, out fn); }
        public static SlResult Get(out PFun_slReflexSetOptions fn) { return Fetch(SlFeature.Reflex, "slReflexSetOptions", ref s_reflexSetOptions, out fn); }
        public static SlResult Get(out PFun_slPCLSetMarker fn) { return Fetch(SlFeature.PCL, "slPCLSetMarker", ref s_pclSetMarker, out fn); }
        public static SlResult Get(out PFun_slPCLGetState fn) { return Fetch(SlFeature.PCL, "slPCLGetState", ref s_pclGetState, out fn); }
        public static SlResult Get(out PFun_slPCLSetOptions fn) { return Fetch(SlFeature.PCL, "slPCLSetOptions", ref s_pclSetOptions, out fn); }

        // Raw function pointer (diagnostics), IntPtr.Zero if not fetched yet.
        public static IntPtr RawPointer(string name)
        {
            lock (s_raw) { IntPtr p; return s_raw.TryGetValue(name, out p) ? p : IntPtr.Zero; }
        }

        public static void Reset()
        {
            s_dlssGetOptimalSettings = null; s_dlssGetState = null; s_dlssSetOptions = null;
            s_reflexGetState = null; s_reflexSleep = null; s_reflexSetOptions = null;
            s_pclSetMarker = null; s_pclGetState = null; s_pclSetOptions = null;
            lock (s_raw) s_raw.Clear();
        }

        static SlResult Fetch<T>(SlFeature feature, string name, ref T cache, out T fn) where T : class
        {
            T c = cache;
            if (c == null)
            {
                IntPtr p;
                SlResult r = SlApi.slGetFeatureFunction(feature, name, out p);
                if (r != SlResult.eOk) { fn = null; return r; }
                if (p == IntPtr.Zero) { fn = null; return SlResult.eErrorMissingOrInvalidAPI; }
                c = (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
                lock (s_raw) s_raw[name] = p;
                cache = c;
            }
            fn = c;
            return SlResult.eOk;
        }
    }

    // ------------------------------------------------------------------------------------------------ tag block

    // N (SlResource, SlResourceTag) pairs in unmanaged memory so tag.resource pointers stay valid. Reuse per frame:
    // Clear(), Add(...), then Sl.SetTagForFrame / Sl.EvaluateFeature. Not thread safe.
    public sealed unsafe class SlTagSet : IDisposable
    {
        static readonly int ResSize = Marshal.SizeOf(typeof(SlResource));
        static readonly int TagSize = Marshal.SizeOf(typeof(SlResourceTag));
        readonly int capacity;
        IntPtr mem;
        int count;

        public SlTagSet(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException("capacity");
            this.capacity = capacity;
            mem = Marshal.AllocHGlobal(capacity * (ResSize + TagSize));
        }

        public int Count { get { return count; } }
        public IntPtr TagsPointer { get { return mem; } }                       // SlResourceTag[count]
        public IntPtr TagPointer(int i) { return mem + i * TagSize; }
        IntPtr ResPointer(int i) { return mem + capacity * TagSize + i * ResSize; }

        public void Clear() { count = 0; }

        // nativeResource = ID3D12Resource*, d3d12State = state the resource is in when SL uses it.
        public int Add(SlBufferType type, IntPtr nativeResource, uint d3d12State, SlResourceLifecycle lifecycle, SlExtent extent)
        {
            if (count >= capacity) throw new InvalidOperationException("SlTagSet full");
            int i = count++;
            IntPtr res = ResPointer(i);
            *(SlResource*)res = SlResource.Tex2d(nativeResource, d3d12State);
            *(SlResourceTag*)TagPointer(i) = SlResourceTag.Create(res, type, lifecycle, extent);
            return i;
        }

        // Null tag: tells SL to drop the tag (and release its reference to an eValidUntilPresent resource).
        public int AddNull(SlBufferType type)
        {
            if (count >= capacity) throw new InvalidOperationException("SlTagSet full");
            int i = count++;
            *(SlResourceTag*)TagPointer(i) = SlResourceTag.Create(IntPtr.Zero, type, SlResourceLifecycle.eValidUntilPresent, new SlExtent());
            return i;
        }

        public void Dispose()
        {
            if (mem != IntPtr.Zero) { Marshal.FreeHGlobal(mem); mem = IntPtr.Zero; }
            GC.SuppressFinalize(this);
        }
        ~SlTagSet() { if (mem != IntPtr.Zero) Marshal.FreeHGlobal(mem); }
    }

    // ------------------------------------------------------------------------------------------------ convenience

    public sealed class SlInitSettings
    {
        public SlFeature[] Features;
        public SlRenderAPI RenderApi;
        public SlEngineType Engine;
        // Production DLLs without an NVIDIA application id: NGX (DLSS SR + DLSS-G) only initializes when BOTH
        // EngineVersion and ProjectId are non-empty (sl.common commonEntry.cpp, NVSDK_NGX_D3D12_Init_with_ProjectID).
        public string EngineVersion;
        public string ProjectId;
        public uint ApplicationId;
        public SlLogLevel LogLevel;
        public string LogDirectory;     // absolute path for sl.log (null = no file)
        public string[] PluginPaths;    // null = plugins next to the exe
        public bool ShowConsole;
        public bool InstallLogCallback;
        public SlPreferenceFlags Flags;

        public SlInitSettings()
        {
            // (1.2.4 ronda 6, QUITAFG: sin DLSS_G; el motor no carga sl.dlss_g.dll ni nvngx_dlssg.dll)
            Features = new SlFeature[] { SlFeature.DLSS, SlFeature.Reflex, SlFeature.PCL };
            RenderApi = SlRenderAPI.eD3D12;
            Engine = SlEngineType.eCustom;
            EngineVersion = "1.0.0";
            LogLevel = SlLogLevel.eDefault;
            InstallLogCallback = true;
            // Engine default: frame-based tagging (slSetTagForFrame), host restores CL state, no OTA download/load.
            Flags = SlPreferenceFlags.eDisableCLStateTracking | SlPreferenceFlags.eUseFrameBasedResourceTagging;
        }
    }

    public static class Sl
    {
        // Where failures and SL log lines go (engine: Sl.LogSink = Log.Write). Must be thread safe: SL logs
        // from its own threads (DLSS-G present thread, OTA, NGX).
        public static Action<string> LogSink = DefaultSink;
        // Raw SL log callback consumer; when null, messages go to LogSink ("[SL warn] ...", info only if verbose).
        public static Action<SlLogType, string> SlMessage;
        public static bool ForwardSlInfo = true;

        static readonly SlLogMessageCallback s_logCallback = OnSlLog;   // static: never collected
        static readonly List<IntPtr> s_initAllocs = new List<IntPtr>();
        static readonly Dictionary<string, int> s_failCounts = new Dictionary<string, int>();
        static bool s_initialized;

        public static bool Initialized { get { return s_initialized; } }

        static void DefaultSink(string s) { try { Console.WriteLine(s); } catch { } }

        static void Emit(string s)
        {
            Action<string> sink = LogSink;
            if (sink == null) return;
            try { sink(s); } catch { }
        }

        static void OnSlLog(SlLogType type, IntPtr msg)
        {
            try
            {
                string s = msg == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(msg);
                s = s.TrimEnd('\r', '\n', ' ');
                Action<SlLogType, string> h = SlMessage;
                if (h != null) { h(type, s); return; }
                if (type == SlLogType.eInfo && !ForwardSlInfo) return;
                Emit(type == SlLogType.eError ? "[SL error] " + s : type == SlLogType.eWarn ? "[SL warn] " + s : "[SL] " + s);
            }
            catch { }   // never throw into native code
        }

        // Logs a failure (rate limited per call site: first 5, then every 1000th) and returns r unchanged.
        public static SlResult Check(SlResult r, string what)
        {
            if (r == SlResult.eOk) return r;
            int n;
            lock (s_failCounts)
            {
                s_failCounts.TryGetValue(what, out n);
                s_failCounts[what] = ++n;
            }
            if (n <= 5 || n % 1000 == 0)
                Emit("SL: " + what + " failed: " + r + (n > 1 ? " (x" + n + ")" : ""));
            return r;
        }

        public static string Name(SlResult r) { return r.ToString(); }

        // ---- lifecycle

        // Call before ANY D3D12/DXGI object is created (and from a normal thread, not DllMain).
        public static SlResult Init(SlInitSettings s)
        {
            if (s == null) throw new ArgumentNullException("s");
            FreeInitMemory();
            SlPreferences p = SlPreferences.Create();
            p.showConsole = (byte)(s.ShowConsole ? 1 : 0);
            p.logLevel = s.LogLevel;
            p.flags = s.Flags;
            p.applicationId = s.ApplicationId;
            p.engine = s.Engine;
            p.renderAPI = s.RenderApi;
            if (s.InstallLogCallback) p.logMessageCallback = Marshal.GetFunctionPointerForDelegate(s_logCallback);
            if (s.Features != null && s.Features.Length > 0)
            {
                IntPtr f = Alloc(4 * s.Features.Length);
                for (int i = 0; i < s.Features.Length; i++) Marshal.WriteInt32(f, 4 * i, unchecked((int)(uint)s.Features[i]));
                p.featuresToLoad = f;
                p.numFeaturesToLoad = (uint)s.Features.Length;
            }
            if (!string.IsNullOrEmpty(s.LogDirectory))
            {
                string dir = Path.GetFullPath(s.LogDirectory);
                p.pathToLogsAndData = Keep(Marshal.StringToHGlobalUni(dir));
            }
            if (s.PluginPaths != null && s.PluginPaths.Length > 0)
            {
                IntPtr arr = Alloc(IntPtr.Size * s.PluginPaths.Length);
                for (int i = 0; i < s.PluginPaths.Length; i++)
                    Marshal.WriteIntPtr(arr, IntPtr.Size * i, Keep(Marshal.StringToHGlobalUni(Path.GetFullPath(s.PluginPaths[i]))));
                p.pathsToPlugins = arr;
                p.numPathsToPlugins = (uint)s.PluginPaths.Length;
            }
            if (!string.IsNullOrEmpty(s.EngineVersion)) p.engineVersion = Keep(Marshal.StringToHGlobalAnsi(s.EngineVersion));
            if (!string.IsNullOrEmpty(s.ProjectId)) p.projectId = Keep(Marshal.StringToHGlobalAnsi(s.ProjectId));

            SlResult r = Check(SlApi.slInit(ref p, SlConst.kSDKVersion), "slInit");
            s_initialized = r == SlResult.eOk;
            return r;
        }

        // Call before releasing the device / swap chain.
        public static SlResult Shutdown()
        {
            SlResult r = Check(SlApi.slShutdown(), "slShutdown");
            SlFns.Reset();
            s_initialized = false;
            FreeInitMemory();
            return r;
        }

        static IntPtr Alloc(int bytes) { return Keep(Marshal.AllocHGlobal(bytes)); }
        static IntPtr Keep(IntPtr p) { s_initAllocs.Add(p); return p; }
        static void FreeInitMemory()
        {
            foreach (IntPtr p in s_initAllocs) Marshal.FreeHGlobal(p);
            s_initAllocs.Clear();
        }

        // ---- support / requirements

        // General support (OS, driver, HWS) without an adapter.
        public static SlResult IsFeatureSupported(SlFeature feature)
        {
            SlAdapterInfo ai = SlAdapterInfo.Create();
            return Check(SlApi.slIsFeatureSupported(feature, ref ai), "slIsFeatureSupported(" + feature + ")");
        }

        // adapterLuid = DXGI_ADAPTER_DESC.AdapterLuid as 64 bits: (HighPart << 32) | LowPart.
        public static unsafe SlResult IsFeatureSupported(SlFeature feature, long adapterLuid)
        {
            long luid = adapterLuid;
            SlAdapterInfo ai = SlAdapterInfo.Create();
            ai.deviceLUID = (IntPtr)(&luid);
            ai.deviceLUIDSizeInBytes = 8;
            return Check(SlApi.slIsFeatureSupported(feature, ref ai), "slIsFeatureSupported(" + feature + ",luid)");
        }

        public static SlResult IsFeatureLoaded(SlFeature feature, out bool loaded)
        {
            byte b;
            SlResult r = Check(SlApi.slIsFeatureLoaded(feature, out b), "slIsFeatureLoaded(" + feature + ")");
            loaded = r == SlResult.eOk && b != 0;
            return r;
        }

        public static SlResult SetFeatureLoaded(SlFeature feature, bool loaded)
        {
            return Check(SlApi.slSetFeatureLoaded(feature, loaded), "slSetFeatureLoaded(" + feature + ")");
        }

        public static SlResult GetFeatureRequirements(SlFeature feature, out SlFeatureRequirements req)
        {
            req = SlFeatureRequirements.Create();
            return Check(SlApi.slGetFeatureRequirements(feature, ref req), "slGetFeatureRequirements(" + feature + ")");
        }

        public static SlResult GetFeatureVersion(SlFeature feature, out SlFeatureVersion ver)
        {
            ver = SlFeatureVersion.Create();
            return Check(SlApi.slGetFeatureVersion(feature, ref ver), "slGetFeatureVersion(" + feature + ")");
        }

        // ---- device / interfaces

        // Must be called right after the main ID3D12Device is created (proxy or native pointer). Initializes the
        // plugins (NGX init happens here); feature functions work only after this.
        public static SlResult SetD3DDevice(IntPtr device)
        {
            SlFns.Reset();
            return Check(SlApi.slSetD3DDevice(device), "slSetD3DDevice");
        }

        public static SlResult UpgradeInterface(ref IntPtr iface) { return Check(SlApi.slUpgradeInterface(ref iface), "slUpgradeInterface"); }

        public static SlResult GetNativeInterface(IntPtr proxy, out IntPtr native)
        {
            return Check(SlApi.slGetNativeInterface(proxy, out native), "slGetNativeInterface");
        }

        // ---- frames

        public static SlResult GetNewFrameToken(out IntPtr token)
        {
            return Check(SlApi.slGetNewFrameToken(out token, IntPtr.Zero), "slGetNewFrameToken");
        }

        // Host-provided frame index (same index -> same token, as SL documents).
        public static SlResult GetNewFrameToken(uint frameIndex, out IntPtr token)
        {
            return Check(SlApi.slGetNewFrameTokenIndexed(out token, ref frameIndex), "slGetNewFrameToken(index)");
        }

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        delegate uint FrameTokenIndexFn(IntPtr self);
        sealed class TokenFn { public IntPtr Ptr; public FrameTokenIndexFn Fn; }
        static TokenFn s_tokenFn;

        // Frame index of a token: calls the virtual "operator uint32_t()" (first vtable slot) like *token in C++.
        public static unsafe uint GetFrameIndex(IntPtr token)
        {
            if (token == IntPtr.Zero) return 0;
            IntPtr vtbl = *(IntPtr*)token;
            IntPtr fn = *(IntPtr*)vtbl;
            TokenFn t = s_tokenFn;
            if (t == null || t.Ptr != fn)
            {
                t = new TokenFn();
                t.Ptr = fn;
                t.Fn = (FrameTokenIndexFn)Marshal.GetDelegateForFunctionPointer(fn, typeof(FrameTokenIndexFn));
                s_tokenFn = t;
            }
            return t.Fn(token);
        }

        // ---- per-frame core

        public static SlResult SetConstants(ref SlConstants c, IntPtr token, SlViewportHandle viewport)
        {
            return Check(SlApi.slSetConstants(ref c, token, ref viewport), "slSetConstants");
        }

        public static SlResult SetTagForFrame(IntPtr token, SlViewportHandle viewport, SlTagSet tags, IntPtr cmdList)
        {
            if (tags == null || tags.Count == 0) return SlResult.eErrorInvalidParameter;
            return Check(SlApi.slSetTagForFrame(token, ref viewport, tags.TagsPointer, (uint)tags.Count, cmdList), "slSetTagForFrame");
        }

        public static unsafe SlResult EvaluateFeature(SlFeature feature, IntPtr token, SlViewportHandle viewport, IntPtr cmdList)
        {
            SlViewportHandle vp = viewport;
            IntPtr* inputs = stackalloc IntPtr[1];
            inputs[0] = (IntPtr)(&vp);
            return Check(SlApi.slEvaluateFeature(feature, token, (IntPtr)inputs, 1, cmdList), EvalName(feature));
        }

        // 16-11 (calidad, E2): con etiquetas locales (solo para esta evaluacion: el plugin las busca antes que las globales; con
        // eValidUntilPresent no sustituyen a la global de la imagen, que sigue siendo la que lee DLSS-G en el Present)
        public static unsafe SlResult EvaluateFeature(SlFeature feature, IntPtr token, SlViewportHandle viewport, IntPtr cmdList, SlTagSet local)
        {
            if (local == null || local.Count == 0) return EvaluateFeature(feature, token, viewport, cmdList);
            SlViewportHandle vp = viewport;
            int n = 1 + local.Count;
            IntPtr* inputs = stackalloc IntPtr[n];
            inputs[0] = (IntPtr)(&vp);
            for (int i = 0; i < local.Count; i++) inputs[1 + i] = local.TagPointer(i);
            return Check(SlApi.slEvaluateFeature(feature, token, (IntPtr)inputs, (uint)n, cmdList), EvalName(feature));
        }

        // Literal per feature: no per-frame string allocation on the hot path.
        static string EvalName(SlFeature f)
        {
            switch (f)
            {
                case SlFeature.DLSS: return "slEvaluateFeature(DLSS)";
                case SlFeature.NIS: return "slEvaluateFeature(NIS)";
                default: return "slEvaluateFeature";
            }
        }

        // Evaluate with local tags (not visible to the global slSetTagForFrame scope).
        public static unsafe SlResult EvaluateFeature(SlFeature feature, IntPtr token, SlViewportHandle viewport, SlTagSet localTags, IntPtr cmdList)
        {
            int n = 1 + (localTags != null ? localTags.Count : 0);
            SlViewportHandle vp = viewport;
            IntPtr* inputs = stackalloc IntPtr[n];
            inputs[0] = (IntPtr)(&vp);
            for (int i = 1; i < n; i++) inputs[i] = localTags.TagPointer(i - 1);
            return Check(SlApi.slEvaluateFeature(feature, token, (IntPtr)inputs, (uint)n, cmdList), EvalName(feature));
        }

        public static SlResult AllocateResources(IntPtr cmdList, SlFeature feature, SlViewportHandle viewport)
        {
            return Check(SlApi.slAllocateResources(cmdList, feature, ref viewport), "slAllocateResources(" + feature + ")");
        }

        public static SlResult FreeResources(SlFeature feature, SlViewportHandle viewport)
        {
            return Check(SlApi.slFreeResources(feature, ref viewport), "slFreeResources(" + feature + ")");
        }

        // ---- DLSS SR

        public static SlResult DLSSGetOptimalSettings(ref SlDLSSOptions options, out SlDLSSOptimalSettings settings)
        {
            settings = SlDLSSOptimalSettings.Create();
            SlFns.PFun_slDLSSGetOptimalSettings fn;
            SlResult r = SlFns.Get(out fn);
            if (r != SlResult.eOk) return Check(r, "slDLSSGetOptimalSettings(fetch)");
            return Check(fn(ref options, ref settings), "slDLSSGetOptimalSettings");
        }

        public static SlResult DLSSGetState(SlViewportHandle viewport, out SlDLSSState state)
        {
            state = SlDLSSState.Create();
            SlFns.PFun_slDLSSGetState fn;
            SlResult r = SlFns.Get(out fn);
            if (r != SlResult.eOk) return Check(r, "slDLSSGetState(fetch)");
            return Check(fn(ref viewport, ref state), "slDLSSGetState");
        }

        public static SlResult DLSSSetOptions(SlViewportHandle viewport, ref SlDLSSOptions options)
        {
            SlFns.PFun_slDLSSSetOptions fn;
            SlResult r = SlFns.Get(out fn);
            if (r != SlResult.eOk) return Check(r, "slDLSSSetOptions(fetch)");
            return Check(fn(ref viewport, ref options), "slDLSSSetOptions");
        }

        // ---- (DLSS-G: 1.2.4 ronda 6, QUITAFG, fuera)

        // ---- Reflex

        // Reuse one SlReflexState (12.8 KB, created with SlReflexState.Create()); it is filled in place.
        public static SlResult ReflexGetState(ref SlReflexState state)
        {
            SlFns.PFun_slReflexGetState fn;
            SlResult r = SlFns.Get(out fn);
            if (r != SlResult.eOk) return Check(r, "slReflexGetState(fetch)");
            return Check(fn(ref state), "slReflexGetState");
        }

        public static SlResult ReflexSleep(IntPtr token)
        {
            SlFns.PFun_slReflexSleep fn;
            SlResult r = SlFns.Get(out fn);
            if (r != SlResult.eOk) return Check(r, "slReflexSleep(fetch)");
            return Check(fn(token), "slReflexSleep");
        }

        public static SlResult ReflexSetOptions(ref SlReflexOptions options)
        {
            SlFns.PFun_slReflexSetOptions fn;
            SlResult r = SlFns.Get(out fn);
            if (r != SlResult.eOk) return Check(r, "slReflexSetOptions(fetch)");
            return Check(fn(ref options), "slReflexSetOptions");
        }

        // ---- PCL

        public static SlResult PCLSetMarker(SlPCLMarker marker, IntPtr token)
        {
            SlFns.PFun_slPCLSetMarker fn;
            SlResult r = SlFns.Get(out fn);
            if (r != SlResult.eOk) return Check(r, "slPCLSetMarker(fetch)");
            return Check(fn(marker, token), "slPCLSetMarker");
        }

        public static SlResult PCLGetState(out SlPCLState state)
        {
            state = SlPCLState.Create();
            SlFns.PFun_slPCLGetState fn;
            SlResult r = SlFns.Get(out fn);
            if (r != SlResult.eOk) return Check(r, "slPCLGetState(fetch)");
            return Check(fn(ref state), "slPCLGetState");
        }

        public static SlResult PCLSetOptions(ref SlPCLOptions options)
        {
            SlFns.PFun_slPCLSetOptions fn;
            SlResult r = SlFns.Get(out fn);
            if (r != SlResult.eOk) return Check(r, "slPCLSetOptions(fetch)");
            return Check(fn(ref options), "slPCLSetOptions");
        }
    }
}
