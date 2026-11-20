// D3D12.cs - Direct3D 12 COM interop for Switch2Engine (C# 5, .NET Framework 4.8, x64 only).
// Source of truth: deps\streamline\external\dx-agility-sdk-headers-1.714.0-preview\include\d3d12.h
// (C-style vtables "typedef struct ID3D12xxxVtbl"), d3dcommon.h, dxgiformat.h.
//
// Conventions (see DESIGN.md):
//  * Interfaces do NOT use C# inheritance; every interface redeclares all inherited non-IUnknown methods in exact
//    vtable order. Every method is [PreserveSig] (also the void ones: without PreserveSig the CLR would treat the
//    garbage in RAX as an HRESULT and could throw).
//  * Methods that return a struct by value in C++ use the MSVC hidden return pointer:
//        [PreserveSig] IntPtr GetDesc(out D3D12_RESOURCE_DESC retVal);   // returns &retVal, ignore it
//  * Parameter mapping:
//        required const T*            -> [In] ref T
//        array const T* (count, ptr)  -> [In] ref T   (pass ref arr[0]; arrays are blittable, pinned, no copy)
//        optional T* (may be NULL)    -> T*           (pass null or &local; needs an unsafe context)
//        T* out                       -> out T
//        REFIID riid, void** ppv      -> [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv
//                                        (cast the object to the interface; riid = typeof(IFoo).GUID)
//        ID3D12Foo* (input)           -> ID3D12Foo (the marshaler QIs the RCW for that IID; null -> NULL)
//        ID3D12Foo* const* (arrays)   -> [In] ref IntPtr (raw pointers, see Com.BorrowPtr)
//        COM pointers inside structs  -> IntPtr (borrowed pointer, see Com.BorrowPtr)
//    Struct types that are not declared here (graphics PSO desc, tiles, DSV desc, command signature ...) are IntPtr.
//  * Enum members drop the C prefix: D3D12_RESOURCE_STATE_COPY_DEST -> D3D12_RESOURCE_STATES.COPY_DEST,
//    DXGI_FORMAT_R8G8B8A8_UNORM -> DXGI_FORMAT.R8G8B8A8_UNORM. If the rest would start with a digit the last prefix
//    word is kept: D3D_FEATURE_LEVEL_12_0 -> D3D_FEATURE_LEVEL.LEVEL_12_0, DXGI_FORMAT_420_OPAQUE -> FORMAT_420_OPAQUE.
//  * ID3D12CommandList::GetType is declared as GetCommandListType (name clash with object.GetType; slot unchanged).
// Compile together with Hr.cs, Dxgi.cs (DXGI_SAMPLE_DESC) and D3DCompiler.cs (ID3DBlob).
using System;
using System.Runtime.InteropServices;
using System.Security;

namespace ConsoleEnhancer.Motor.Native
{
    // ------------------------------------------------------------------------------------------------------------
    // Enums (generated from d3d12.h / d3dcommon.h / dxgiformat.h; *_FORCE_UINT members omitted)
    // ------------------------------------------------------------------------------------------------------------
    public enum D3D_FEATURE_LEVEL : int
    {
        LEVEL_1_0_GENERIC = 0x100,
        LEVEL_1_0_CORE = 0x1000,
        LEVEL_9_1 = 0x9100,
        LEVEL_9_2 = 0x9200,
        LEVEL_9_3 = 0x9300,
        LEVEL_10_0 = 0xa000,
        LEVEL_10_1 = 0xa100,
        LEVEL_11_0 = 0xb000,
        LEVEL_11_1 = 0xb100,
        LEVEL_12_0 = 0xc000,
        LEVEL_12_1 = 0xc100,
        LEVEL_12_2 = 0xc200,
    }

    public enum D3D_ROOT_SIGNATURE_VERSION : int
    {
        VERSION_1 = 0x1,
        VERSION_1_0 = 0x1,
        VERSION_1_1 = 0x2,
        VERSION_1_2 = 0x3,
    }

    public enum D3D12_COMMAND_LIST_TYPE : int
    {
        DIRECT = 0,
        BUNDLE = 1,
        COMPUTE = 2,
        COPY = 3,
        VIDEO_DECODE = 4,
        VIDEO_PROCESS = 5,
        VIDEO_ENCODE = 6,
        NONE = -1,
    }

    [Flags]
    public enum D3D12_COMMAND_QUEUE_FLAGS : int
    {
        NONE = 0,
        DISABLE_GPU_TIMEOUT = 0x1,
    }

    public enum D3D12_COMMAND_QUEUE_PRIORITY : int
    {
        NORMAL = 0,
        HIGH = 100,
        GLOBAL_REALTIME = 10000,
    }

    public enum D3D12_HEAP_TYPE : int
    {
        DEFAULT = 1,
        UPLOAD = 2,
        READBACK = 3,
        CUSTOM = 4,
        GPU_UPLOAD = 5,
    }

    public enum D3D12_CPU_PAGE_PROPERTY : int
    {
        UNKNOWN = 0,
        NOT_AVAILABLE = 1,
        WRITE_COMBINE = 2,
        WRITE_BACK = 3,
    }

    public enum D3D12_MEMORY_POOL : int
    {
        UNKNOWN = 0,
        L0 = 1,
        L1 = 2,
    }

    [Flags]
    public enum D3D12_HEAP_FLAGS : int
    {
        NONE = 0,
        SHARED = 0x1,
        DENY_BUFFERS = 0x4,
        ALLOW_DISPLAY = 0x8,
        SHARED_CROSS_ADAPTER = 0x20,
        DENY_RT_DS_TEXTURES = 0x40,
        DENY_NON_RT_DS_TEXTURES = 0x80,
        HARDWARE_PROTECTED = 0x100,
        ALLOW_WRITE_WATCH = 0x200,
        ALLOW_SHADER_ATOMICS = 0x400,
        CREATE_NOT_RESIDENT = 0x800,
        CREATE_NOT_ZEROED = 0x1000,
        TOOLS_USE_MANUAL_WRITE_TRACKING = 0x2000,
        ALLOW_ALL_BUFFERS_AND_TEXTURES = 0,
        ALLOW_ONLY_BUFFERS = 0xc0,
        ALLOW_ONLY_NON_RT_DS_TEXTURES = 0x44,
        ALLOW_ONLY_RT_DS_TEXTURES = 0x84,
    }

    public enum D3D12_RESOURCE_DIMENSION : int
    {
        UNKNOWN = 0,
        BUFFER = 1,
        TEXTURE1D = 2,
        TEXTURE2D = 3,
        TEXTURE3D = 4,
    }

    public enum D3D12_TEXTURE_LAYOUT : int
    {
        UNKNOWN = 0,
        ROW_MAJOR = 1,
        LAYOUT_64KB_UNDEFINED_SWIZZLE = 2,
        LAYOUT_64KB_STANDARD_SWIZZLE = 3,
    }

    [Flags]
    public enum D3D12_RESOURCE_FLAGS : int
    {
        NONE = 0,
        ALLOW_RENDER_TARGET = 0x1,
        ALLOW_DEPTH_STENCIL = 0x2,
        ALLOW_UNORDERED_ACCESS = 0x4,
        DENY_SHADER_RESOURCE = 0x8,
        ALLOW_CROSS_ADAPTER = 0x10,
        ALLOW_SIMULTANEOUS_ACCESS = 0x20,
        VIDEO_DECODE_REFERENCE_ONLY = 0x40,
        VIDEO_ENCODE_REFERENCE_ONLY = 0x80,
        RAYTRACING_ACCELERATION_STRUCTURE = 0x100,
    }

    [Flags]
    public enum D3D12_RESOURCE_STATES : uint
    {
        COMMON = 0,
        VERTEX_AND_CONSTANT_BUFFER = 0x1,
        INDEX_BUFFER = 0x2,
        RENDER_TARGET = 0x4,
        UNORDERED_ACCESS = 0x8,
        DEPTH_WRITE = 0x10,
        DEPTH_READ = 0x20,
        NON_PIXEL_SHADER_RESOURCE = 0x40,
        PIXEL_SHADER_RESOURCE = 0x80,
        STREAM_OUT = 0x100,
        INDIRECT_ARGUMENT = 0x200,
        COPY_DEST = 0x400,
        COPY_SOURCE = 0x800,
        RESOLVE_DEST = 0x1000,
        RESOLVE_SOURCE = 0x2000,
        RAYTRACING_ACCELERATION_STRUCTURE = 0x400000,
        SHADING_RATE_SOURCE = 0x1000000,
        RESERVED_INTERNAL_8000 = 0x8000,
        RESERVED_INTERNAL_4000 = 0x4000,
        RESERVED_INTERNAL_100000 = 0x100000,
        RESERVED_INTERNAL_40000000 = 0x40000000,
        RESERVED_INTERNAL_80000000 = 0x80000000,
        GENERIC_READ = ( ( ( ( ( 0x1 | 0x2 ) | 0x40 ) | 0x80 ) | 0x200 ) | 0x800 ),
        ALL_SHADER_RESOURCE = ( 0x40 | 0x80 ),
        PRESENT = 0,
        PREDICATION = 0x200,
        VIDEO_DECODE_READ = 0x10000,
        VIDEO_DECODE_WRITE = 0x20000,
        VIDEO_PROCESS_READ = 0x40000,
        VIDEO_PROCESS_WRITE = 0x80000,
        VIDEO_ENCODE_READ = 0x200000,
        VIDEO_ENCODE_WRITE = 0x800000,
    }

    public enum D3D12_RESOURCE_BARRIER_TYPE : int
    {
        TRANSITION = 0,
        ALIASING = ( TRANSITION + 1 ),
        UAV = ( ALIASING + 1 ),
    }

    [Flags]
    public enum D3D12_RESOURCE_BARRIER_FLAGS : int
    {
        NONE = 0,
        BEGIN_ONLY = 0x1,
        END_ONLY = 0x2,
    }

    public enum D3D12_DESCRIPTOR_HEAP_TYPE : int
    {
        CBV_SRV_UAV = 0,
        SAMPLER = ( CBV_SRV_UAV + 1 ),
        RTV = ( SAMPLER + 1 ),
        DSV = ( RTV + 1 ),
        NUM_TYPES = ( DSV + 1 ),
    }

    [Flags]
    public enum D3D12_DESCRIPTOR_HEAP_FLAGS : int
    {
        NONE = 0,
        SHADER_VISIBLE = 0x1,
    }

    public enum D3D12_SRV_DIMENSION : int
    {
        UNKNOWN = 0,
        BUFFER = 1,
        TEXTURE1D = 2,
        TEXTURE1DARRAY = 3,
        TEXTURE2D = 4,
        TEXTURE2DARRAY = 5,
        TEXTURE2DMS = 6,
        TEXTURE2DMSARRAY = 7,
        TEXTURE3D = 8,
        TEXTURECUBE = 9,
        TEXTURECUBEARRAY = 10,
        RAYTRACING_ACCELERATION_STRUCTURE = 11,
    }

    public enum D3D12_UAV_DIMENSION : int
    {
        UNKNOWN = 0,
        BUFFER = 1,
        TEXTURE1D = 2,
        TEXTURE1DARRAY = 3,
        TEXTURE2D = 4,
        TEXTURE2DARRAY = 5,
        TEXTURE2DMS = 6,
        TEXTURE2DMSARRAY = 7,
        TEXTURE3D = 8,
    }

    public enum D3D12_RTV_DIMENSION : int
    {
        UNKNOWN = 0,
        BUFFER = 1,
        TEXTURE1D = 2,
        TEXTURE1DARRAY = 3,
        TEXTURE2D = 4,
        TEXTURE2DARRAY = 5,
        TEXTURE2DMS = 6,
        TEXTURE2DMSARRAY = 7,
        TEXTURE3D = 8,
    }

    [Flags]
    public enum D3D12_BUFFER_SRV_FLAGS : int
    {
        NONE = 0,
        RAW = 0x1,
    }

    [Flags]
    public enum D3D12_BUFFER_UAV_FLAGS : int
    {
        NONE = 0,
        RAW = 0x1,
    }

    public enum D3D12_TEXTURE_COPY_TYPE : int
    {
        SUBRESOURCE_INDEX = 0,
        PLACED_FOOTPRINT = 1,
    }

    public enum D3D12_QUERY_HEAP_TYPE : int
    {
        OCCLUSION = 0,
        TIMESTAMP = 1,
        PIPELINE_STATISTICS = 2,
        SO_STATISTICS = 3,
        VIDEO_DECODE_STATISTICS = 4,
        COPY_QUEUE_TIMESTAMP = 5,
        PIPELINE_STATISTICS1 = 7,
    }

    public enum D3D12_QUERY_TYPE : int
    {
        OCCLUSION = 0,
        BINARY_OCCLUSION = 1,
        TIMESTAMP = 2,
        PIPELINE_STATISTICS = 3,
        SO_STATISTICS_STREAM0 = 4,
        SO_STATISTICS_STREAM1 = 5,
        SO_STATISTICS_STREAM2 = 6,
        SO_STATISTICS_STREAM3 = 7,
        VIDEO_DECODE_STATISTICS = 8,
        PIPELINE_STATISTICS1 = 10,
    }

    public enum D3D12_ROOT_PARAMETER_TYPE : int
    {
        DESCRIPTOR_TABLE = 0,
        TYPE_32BIT_CONSTANTS = ( DESCRIPTOR_TABLE + 1 ),
        CBV = ( TYPE_32BIT_CONSTANTS + 1 ),
        SRV = ( CBV + 1 ),
        UAV = ( SRV + 1 ),
    }

    public enum D3D12_SHADER_VISIBILITY : int
    {
        ALL = 0,
        VERTEX = 1,
        HULL = 2,
        DOMAIN = 3,
        GEOMETRY = 4,
        PIXEL = 5,
        AMPLIFICATION = 6,
        MESH = 7,
    }

    public enum D3D12_DESCRIPTOR_RANGE_TYPE : int
    {
        SRV = 0,
        UAV = ( SRV + 1 ),
        CBV = ( UAV + 1 ),
        SAMPLER = ( CBV + 1 ),
    }

    [Flags]
    public enum D3D12_ROOT_SIGNATURE_FLAGS : int
    {
        NONE = 0,
        ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT = 0x1,
        DENY_VERTEX_SHADER_ROOT_ACCESS = 0x2,
        DENY_HULL_SHADER_ROOT_ACCESS = 0x4,
        DENY_DOMAIN_SHADER_ROOT_ACCESS = 0x8,
        DENY_GEOMETRY_SHADER_ROOT_ACCESS = 0x10,
        DENY_PIXEL_SHADER_ROOT_ACCESS = 0x20,
        ALLOW_STREAM_OUTPUT = 0x40,
        LOCAL_ROOT_SIGNATURE = 0x80,
        DENY_AMPLIFICATION_SHADER_ROOT_ACCESS = 0x100,
        DENY_MESH_SHADER_ROOT_ACCESS = 0x200,
        CBV_SRV_UAV_HEAP_DIRECTLY_INDEXED = 0x400,
        SAMPLER_HEAP_DIRECTLY_INDEXED = 0x800,
    }

    public enum D3D12_FILTER : int
    {
        MIN_MAG_MIP_POINT = 0,
        MIN_MAG_POINT_MIP_LINEAR = 0x1,
        MIN_POINT_MAG_LINEAR_MIP_POINT = 0x4,
        MIN_POINT_MAG_MIP_LINEAR = 0x5,
        MIN_LINEAR_MAG_MIP_POINT = 0x10,
        MIN_LINEAR_MAG_POINT_MIP_LINEAR = 0x11,
        MIN_MAG_LINEAR_MIP_POINT = 0x14,
        MIN_MAG_MIP_LINEAR = 0x15,
        MIN_MAG_ANISOTROPIC_MIP_POINT = 0x54,
        ANISOTROPIC = 0x55,
        COMPARISON_MIN_MAG_MIP_POINT = 0x80,
        COMPARISON_MIN_MAG_POINT_MIP_LINEAR = 0x81,
        COMPARISON_MIN_POINT_MAG_LINEAR_MIP_POINT = 0x84,
        COMPARISON_MIN_POINT_MAG_MIP_LINEAR = 0x85,
        COMPARISON_MIN_LINEAR_MAG_MIP_POINT = 0x90,
        COMPARISON_MIN_LINEAR_MAG_POINT_MIP_LINEAR = 0x91,
        COMPARISON_MIN_MAG_LINEAR_MIP_POINT = 0x94,
        COMPARISON_MIN_MAG_MIP_LINEAR = 0x95,
        COMPARISON_MIN_MAG_ANISOTROPIC_MIP_POINT = 0xd4,
        COMPARISON_ANISOTROPIC = 0xd5,
        MINIMUM_MIN_MAG_MIP_POINT = 0x100,
        MINIMUM_MIN_MAG_POINT_MIP_LINEAR = 0x101,
        MINIMUM_MIN_POINT_MAG_LINEAR_MIP_POINT = 0x104,
        MINIMUM_MIN_POINT_MAG_MIP_LINEAR = 0x105,
        MINIMUM_MIN_LINEAR_MAG_MIP_POINT = 0x110,
        MINIMUM_MIN_LINEAR_MAG_POINT_MIP_LINEAR = 0x111,
        MINIMUM_MIN_MAG_LINEAR_MIP_POINT = 0x114,
        MINIMUM_MIN_MAG_MIP_LINEAR = 0x115,
        MINIMUM_MIN_MAG_ANISOTROPIC_MIP_POINT = 0x154,
        MINIMUM_ANISOTROPIC = 0x155,
        MAXIMUM_MIN_MAG_MIP_POINT = 0x180,
        MAXIMUM_MIN_MAG_POINT_MIP_LINEAR = 0x181,
        MAXIMUM_MIN_POINT_MAG_LINEAR_MIP_POINT = 0x184,
        MAXIMUM_MIN_POINT_MAG_MIP_LINEAR = 0x185,
        MAXIMUM_MIN_LINEAR_MAG_MIP_POINT = 0x190,
        MAXIMUM_MIN_LINEAR_MAG_POINT_MIP_LINEAR = 0x191,
        MAXIMUM_MIN_MAG_LINEAR_MIP_POINT = 0x194,
        MAXIMUM_MIN_MAG_MIP_LINEAR = 0x195,
        MAXIMUM_MIN_MAG_ANISOTROPIC_MIP_POINT = 0x1d4,
        MAXIMUM_ANISOTROPIC = 0x1d5,
    }

    public enum D3D12_TEXTURE_ADDRESS_MODE : int
    {
        WRAP = 1,
        MIRROR = 2,
        CLAMP = 3,
        BORDER = 4,
        MIRROR_ONCE = 5,
    }

    public enum D3D12_COMPARISON_FUNC : int
    {
        NONE = 0,
        NEVER = 1,
        LESS = 2,
        EQUAL = 3,
        LESS_EQUAL = 4,
        GREATER = 5,
        NOT_EQUAL = 6,
        GREATER_EQUAL = 7,
        ALWAYS = 8,
    }

    public enum D3D12_STATIC_BORDER_COLOR : int
    {
        TRANSPARENT_BLACK = 0,
        OPAQUE_BLACK = ( TRANSPARENT_BLACK + 1 ),
        OPAQUE_WHITE = ( OPAQUE_BLACK + 1 ),
        OPAQUE_BLACK_UINT = ( OPAQUE_WHITE + 1 ),
        OPAQUE_WHITE_UINT = ( OPAQUE_BLACK_UINT + 1 ),
    }

    [Flags]
    public enum D3D12_PIPELINE_STATE_FLAGS : int
    {
        NONE = 0,
        TOOL_DEBUG = 0x1,
        DYNAMIC_DEPTH_BIAS = 0x4,
        DYNAMIC_INDEX_BUFFER_STRIP_CUT = 0x8,
    }

    [Flags]
    public enum D3D12_FENCE_FLAGS : int
    {
        NONE = 0,
        SHARED = 0x1,
        SHARED_CROSS_ADAPTER = 0x2,
        NON_MONITORED = 0x4,
    }

    public enum D3D12_FEATURE : int
    {
        D3D12_OPTIONS = 0,
        ARCHITECTURE = 1,
        FEATURE_LEVELS = 2,
        FORMAT_SUPPORT = 3,
        MULTISAMPLE_QUALITY_LEVELS = 4,
        FORMAT_INFO = 5,
        GPU_VIRTUAL_ADDRESS_SUPPORT = 6,
        SHADER_MODEL = 7,
        D3D12_OPTIONS1 = 8,
        PROTECTED_RESOURCE_SESSION_SUPPORT = 10,
        ROOT_SIGNATURE = 12,
        ARCHITECTURE1 = 16,
        D3D12_OPTIONS2 = 18,
        SHADER_CACHE = 19,
        COMMAND_QUEUE_PRIORITY = 20,
        D3D12_OPTIONS3 = 21,
        EXISTING_HEAPS = 22,
        D3D12_OPTIONS4 = 23,
        SERIALIZATION = 24,
        CROSS_NODE = 25,
        D3D12_OPTIONS5 = 27,
        DISPLAYABLE = 28,
        D3D12_OPTIONS6 = 30,
        QUERY_META_COMMAND = 31,
        D3D12_OPTIONS7 = 32,
        PROTECTED_RESOURCE_SESSION_TYPE_COUNT = 33,
        PROTECTED_RESOURCE_SESSION_TYPES = 34,
        D3D12_OPTIONS8 = 36,
        D3D12_OPTIONS9 = 37,
        D3D12_OPTIONS10 = 39,
        D3D12_OPTIONS11 = 40,
        D3D12_OPTIONS12 = 41,
        D3D12_OPTIONS13 = 42,
        D3D12_OPTIONS14 = 43,
        D3D12_OPTIONS15 = 44,
        D3D12_OPTIONS16 = 45,
        D3D12_OPTIONS17 = 46,
        D3D12_OPTIONS18 = 47,
        D3D12_OPTIONS19 = 48,
        D3D12_OPTIONS20 = 49,
        PREDICATION = 50,
        PLACED_RESOURCE_SUPPORT_INFO = 51,
        HARDWARE_COPY = 52,
        D3D12_OPTIONS21 = 53,
    }

    [Flags]
    public enum D3D12_FORMAT_SUPPORT1 : uint
    {
        NONE = 0,
        BUFFER = 0x1,
        IA_VERTEX_BUFFER = 0x2,
        IA_INDEX_BUFFER = 0x4,
        SO_BUFFER = 0x8,
        TEXTURE1D = 0x10,
        TEXTURE2D = 0x20,
        TEXTURE3D = 0x40,
        TEXTURECUBE = 0x80,
        SHADER_LOAD = 0x100,
        SHADER_SAMPLE = 0x200,
        SHADER_SAMPLE_COMPARISON = 0x400,
        SHADER_SAMPLE_MONO_TEXT = 0x800,
        MIP = 0x1000,
        RENDER_TARGET = 0x4000,
        BLENDABLE = 0x8000,
        DEPTH_STENCIL = 0x10000,
        MULTISAMPLE_RESOLVE = 0x40000,
        DISPLAY = 0x80000,
        CAST_WITHIN_BIT_LAYOUT = 0x100000,
        MULTISAMPLE_RENDERTARGET = 0x200000,
        MULTISAMPLE_LOAD = 0x400000,
        SHADER_GATHER = 0x800000,
        BACK_BUFFER_CAST = 0x1000000,
        TYPED_UNORDERED_ACCESS_VIEW = 0x2000000,
        SHADER_GATHER_COMPARISON = 0x4000000,
        DECODER_OUTPUT = 0x8000000,
        VIDEO_PROCESSOR_OUTPUT = 0x10000000,
        VIDEO_PROCESSOR_INPUT = 0x20000000,
        VIDEO_ENCODER = 0x40000000,
    }

    [Flags]
    public enum D3D12_FORMAT_SUPPORT2 : uint
    {
        NONE = 0,
        UAV_ATOMIC_ADD = 0x1,
        UAV_ATOMIC_BITWISE_OPS = 0x2,
        UAV_ATOMIC_COMPARE_STORE_OR_COMPARE_EXCHANGE = 0x4,
        UAV_ATOMIC_EXCHANGE = 0x8,
        UAV_ATOMIC_SIGNED_MIN_OR_MAX = 0x10,
        UAV_ATOMIC_UNSIGNED_MIN_OR_MAX = 0x20,
        UAV_TYPED_LOAD = 0x40,
        UAV_TYPED_STORE = 0x80,
        OUTPUT_MERGER_LOGIC_OP = 0x100,
        TILED = 0x200,
        MULTIPLANE_OVERLAY = 0x4000,
        SAMPLER_FEEDBACK = 0x8000,
    }

    public enum DXGI_FORMAT : int
    {
        UNKNOWN = 0,
        R32G32B32A32_TYPELESS = 1,
        R32G32B32A32_FLOAT = 2,
        R32G32B32A32_UINT = 3,
        R32G32B32A32_SINT = 4,
        R32G32B32_TYPELESS = 5,
        R32G32B32_FLOAT = 6,
        R32G32B32_UINT = 7,
        R32G32B32_SINT = 8,
        R16G16B16A16_TYPELESS = 9,
        R16G16B16A16_FLOAT = 10,
        R16G16B16A16_UNORM = 11,
        R16G16B16A16_UINT = 12,
        R16G16B16A16_SNORM = 13,
        R16G16B16A16_SINT = 14,
        R32G32_TYPELESS = 15,
        R32G32_FLOAT = 16,
        R32G32_UINT = 17,
        R32G32_SINT = 18,
        R32G8X24_TYPELESS = 19,
        D32_FLOAT_S8X24_UINT = 20,
        R32_FLOAT_X8X24_TYPELESS = 21,
        X32_TYPELESS_G8X24_UINT = 22,
        R10G10B10A2_TYPELESS = 23,
        R10G10B10A2_UNORM = 24,
        R10G10B10A2_UINT = 25,
        R11G11B10_FLOAT = 26,
        R8G8B8A8_TYPELESS = 27,
        R8G8B8A8_UNORM = 28,
        R8G8B8A8_UNORM_SRGB = 29,
        R8G8B8A8_UINT = 30,
        R8G8B8A8_SNORM = 31,
        R8G8B8A8_SINT = 32,
        R16G16_TYPELESS = 33,
        R16G16_FLOAT = 34,
        R16G16_UNORM = 35,
        R16G16_UINT = 36,
        R16G16_SNORM = 37,
        R16G16_SINT = 38,
        R32_TYPELESS = 39,
        D32_FLOAT = 40,
        R32_FLOAT = 41,
        R32_UINT = 42,
        R32_SINT = 43,
        R24G8_TYPELESS = 44,
        D24_UNORM_S8_UINT = 45,
        R24_UNORM_X8_TYPELESS = 46,
        X24_TYPELESS_G8_UINT = 47,
        R8G8_TYPELESS = 48,
        R8G8_UNORM = 49,
        R8G8_UINT = 50,
        R8G8_SNORM = 51,
        R8G8_SINT = 52,
        R16_TYPELESS = 53,
        R16_FLOAT = 54,
        D16_UNORM = 55,
        R16_UNORM = 56,
        R16_UINT = 57,
        R16_SNORM = 58,
        R16_SINT = 59,
        R8_TYPELESS = 60,
        R8_UNORM = 61,
        R8_UINT = 62,
        R8_SNORM = 63,
        R8_SINT = 64,
        A8_UNORM = 65,
        R1_UNORM = 66,
        R9G9B9E5_SHAREDEXP = 67,
        R8G8_B8G8_UNORM = 68,
        G8R8_G8B8_UNORM = 69,
        BC1_TYPELESS = 70,
        BC1_UNORM = 71,
        BC1_UNORM_SRGB = 72,
        BC2_TYPELESS = 73,
        BC2_UNORM = 74,
        BC2_UNORM_SRGB = 75,
        BC3_TYPELESS = 76,
        BC3_UNORM = 77,
        BC3_UNORM_SRGB = 78,
        BC4_TYPELESS = 79,
        BC4_UNORM = 80,
        BC4_SNORM = 81,
        BC5_TYPELESS = 82,
        BC5_UNORM = 83,
        BC5_SNORM = 84,
        B5G6R5_UNORM = 85,
        B5G5R5A1_UNORM = 86,
        B8G8R8A8_UNORM = 87,
        B8G8R8X8_UNORM = 88,
        R10G10B10_XR_BIAS_A2_UNORM = 89,
        B8G8R8A8_TYPELESS = 90,
        B8G8R8A8_UNORM_SRGB = 91,
        B8G8R8X8_TYPELESS = 92,
        B8G8R8X8_UNORM_SRGB = 93,
        BC6H_TYPELESS = 94,
        BC6H_UF16 = 95,
        BC6H_SF16 = 96,
        BC7_TYPELESS = 97,
        BC7_UNORM = 98,
        BC7_UNORM_SRGB = 99,
        AYUV = 100,
        Y410 = 101,
        Y416 = 102,
        NV12 = 103,
        P010 = 104,
        P016 = 105,
        FORMAT_420_OPAQUE = 106,
        YUY2 = 107,
        Y210 = 108,
        Y216 = 109,
        NV11 = 110,
        AI44 = 111,
        IA44 = 112,
        P8 = 113,
        A8P8 = 114,
        B4G4R4A4_UNORM = 115,
        P208 = 130,
        V208 = 131,
        V408 = 132,
        SAMPLER_FEEDBACK_MIN_MIP_OPAQUE = 189,
        SAMPLER_FEEDBACK_MIP_REGION_USED_OPAQUE = 190,
        A4B4G4R4_UNORM = 191,
    }

    public enum D3D12_CLEAR_FLAGS : int
    {
        DEPTH = 0x1,
        STENCIL = 0x2,
    }

    // ------------------------------------------------------------------------------------------------------------
    // Constants
    // ------------------------------------------------------------------------------------------------------------
    public static class D3D12Const
    {
        public const uint DEFAULT_SHADER_4_COMPONENT_MAPPING = 0x1688;     // ENCODE(0,1,2,3)
        public const uint DESCRIPTOR_RANGE_OFFSET_APPEND = 0xffffffff;
        public const uint RESOURCE_BARRIER_ALL_SUBRESOURCES = 0xffffffff;
        public const uint TEXTURE_DATA_PITCH_ALIGNMENT = 256;
        public const uint TEXTURE_DATA_PLACEMENT_ALIGNMENT = 512;
        public const uint CONSTANT_BUFFER_DATA_PLACEMENT_ALIGNMENT = 256;
        public const uint DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT = 65536;
        public const uint DEFAULT_MAX_ANISOTROPY = 16;
        public const float FLOAT32_MAX = 3.402823466e+38f;
        public const uint SDK_VERSION = 614;                               // D3D12_SDK_VERSION of these headers

        public static uint Encode4ComponentMapping(uint src0, uint src1, uint src2, uint src3)
        {
            return (src0 & 7) | ((src1 & 7) << 3) | ((src2 & 7) << 6) | ((src3 & 7) << 9) | (1u << 12);
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Basic Windows structs
    // ------------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct LUID                                     // 8
    {
        public uint LowPart;
        public int HighPart;
        public long ToInt64() { return ((long)HighPart << 32) | LowPart; }
        public override string ToString() { return HighPart.ToString("X8") + ":" + LowPart.ToString("X8"); }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT                                     // 16 (also D3D12_RECT)
    {
        public int left, top, right, bottom;
        public RECT(int l, int t, int r, int b) { left = l; top = t; right = r; bottom = b; }
        public override string ToString() { return "(" + left + "," + top + ")-(" + right + "," + bottom + ")"; }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Queue / heaps / resources
    // ------------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_COMMAND_QUEUE_DESC                 // 16
    {
        public D3D12_COMMAND_LIST_TYPE Type;
        public int Priority;                               // D3D12_COMMAND_QUEUE_PRIORITY or any INT
        public D3D12_COMMAND_QUEUE_FLAGS Flags;
        public uint NodeMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_HEAP_PROPERTIES                    // 20
    {
        public D3D12_HEAP_TYPE Type;
        public D3D12_CPU_PAGE_PROPERTY CPUPageProperty;
        public D3D12_MEMORY_POOL MemoryPoolPreference;
        public uint CreationNodeMask;
        public uint VisibleNodeMask;

        public D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE type)
        {
            Type = type;
            CPUPageProperty = D3D12_CPU_PAGE_PROPERTY.UNKNOWN;
            MemoryPoolPreference = D3D12_MEMORY_POOL.UNKNOWN;
            CreationNodeMask = 1;
            VisibleNodeMask = 1;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_HEAP_DESC                          // 48
    {
        public ulong SizeInBytes;
        public D3D12_HEAP_PROPERTIES Properties;
        public ulong Alignment;
        public D3D12_HEAP_FLAGS Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_RESOURCE_DESC                      // 56
    {
        public D3D12_RESOURCE_DIMENSION Dimension;
        public ulong Alignment;
        public ulong Width;
        public uint Height;
        public ushort DepthOrArraySize;
        public ushort MipLevels;
        public DXGI_FORMAT Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public D3D12_TEXTURE_LAYOUT Layout;
        public D3D12_RESOURCE_FLAGS Flags;

        public static D3D12_RESOURCE_DESC Buffer(ulong sizeInBytes, D3D12_RESOURCE_FLAGS flags = D3D12_RESOURCE_FLAGS.NONE)
        {
            D3D12_RESOURCE_DESC d = new D3D12_RESOURCE_DESC();
            d.Dimension = D3D12_RESOURCE_DIMENSION.BUFFER;
            d.Alignment = 0;
            d.Width = sizeInBytes;
            d.Height = 1;
            d.DepthOrArraySize = 1;
            d.MipLevels = 1;
            d.Format = DXGI_FORMAT.UNKNOWN;
            d.SampleDesc.Count = 1;
            d.SampleDesc.Quality = 0;
            d.Layout = D3D12_TEXTURE_LAYOUT.ROW_MAJOR;
            d.Flags = flags;
            return d;
        }

        public static D3D12_RESOURCE_DESC Tex2D(DXGI_FORMAT format, ulong width, uint height,
            D3D12_RESOURCE_FLAGS flags = D3D12_RESOURCE_FLAGS.NONE, ushort arraySize = 1, ushort mipLevels = 1)
        {
            D3D12_RESOURCE_DESC d = new D3D12_RESOURCE_DESC();
            d.Dimension = D3D12_RESOURCE_DIMENSION.TEXTURE2D;
            d.Alignment = 0;
            d.Width = width;
            d.Height = height;
            d.DepthOrArraySize = arraySize;
            d.MipLevels = mipLevels;
            d.Format = format;
            d.SampleDesc.Count = 1;
            d.SampleDesc.Quality = 0;
            d.Layout = D3D12_TEXTURE_LAYOUT.UNKNOWN;
            d.Flags = flags;
            return d;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_DEPTH_STENCIL_VALUE                // 8
    {
        public float Depth;
        public byte Stencil;
    }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    public unsafe struct D3D12_CLEAR_VALUE                 // 20: Format + union { FLOAT Color[4]; DEPTH_STENCIL_VALUE }
    {
        [FieldOffset(0)] public DXGI_FORMAT Format;
        [FieldOffset(4)] public fixed float Color[4];
        [FieldOffset(4)] public D3D12_DEPTH_STENCIL_VALUE DepthStencil;

        public static D3D12_CLEAR_VALUE FromColor(DXGI_FORMAT format, float r, float g, float b, float a)
        {
            D3D12_CLEAR_VALUE v = new D3D12_CLEAR_VALUE();
            v.Format = format;
            v.Color[0] = r; v.Color[1] = g; v.Color[2] = b; v.Color[3] = a;
            return v;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_RESOURCE_ALLOCATION_INFO           // 16
    {
        public ulong SizeInBytes;
        public ulong Alignment;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_RANGE                              // 16
    {
        public UIntPtr Begin;
        public UIntPtr End;
        public D3D12_RANGE(ulong begin, ulong end) { Begin = new UIntPtr(begin); End = new UIntPtr(end); }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_BOX                                // 24
    {
        public uint left, top, front, right, bottom, back;
        public D3D12_BOX(uint l, uint t, uint r, uint b) { left = l; top = t; front = 0; right = r; bottom = b; back = 1; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_VIEWPORT                           // 24
    {
        public float TopLeftX, TopLeftY, Width, Height, MinDepth, MaxDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_FEATURE_DATA_FORMAT_SUPPORT        // 12 (CheckFeatureSupport(FORMAT_SUPPORT))
    {
        public DXGI_FORMAT Format;
        public D3D12_FORMAT_SUPPORT1 Support1;
        public D3D12_FORMAT_SUPPORT2 Support2;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Resource barriers (union -> explicit layout, sizeof = 32)
    // ------------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_RESOURCE_TRANSITION_BARRIER        // 24
    {
        public IntPtr pResource;                           // ID3D12Resource* (borrowed)
        public uint Subresource;
        public D3D12_RESOURCE_STATES StateBefore;
        public D3D12_RESOURCE_STATES StateAfter;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_RESOURCE_ALIASING_BARRIER          // 16
    {
        public IntPtr pResourceBefore;
        public IntPtr pResourceAfter;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_RESOURCE_UAV_BARRIER               // 8
    {
        public IntPtr pResource;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct D3D12_RESOURCE_BARRIER                   // 32
    {
        [FieldOffset(0)] public D3D12_RESOURCE_BARRIER_TYPE Type;
        [FieldOffset(4)] public D3D12_RESOURCE_BARRIER_FLAGS Flags;
        [FieldOffset(8)] public D3D12_RESOURCE_TRANSITION_BARRIER Transition;
        [FieldOffset(8)] public D3D12_RESOURCE_ALIASING_BARRIER Aliasing;
        [FieldOffset(8)] public D3D12_RESOURCE_UAV_BARRIER UAV;

        public static D3D12_RESOURCE_BARRIER CreateTransition(IntPtr resource, D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after,
            uint subresource = D3D12Const.RESOURCE_BARRIER_ALL_SUBRESOURCES, D3D12_RESOURCE_BARRIER_FLAGS flags = D3D12_RESOURCE_BARRIER_FLAGS.NONE)
        {
            D3D12_RESOURCE_BARRIER b = new D3D12_RESOURCE_BARRIER();
            b.Type = D3D12_RESOURCE_BARRIER_TYPE.TRANSITION;
            b.Flags = flags;
            b.Transition.pResource = resource;
            b.Transition.Subresource = subresource;
            b.Transition.StateBefore = before;
            b.Transition.StateAfter = after;
            return b;
        }

        public static D3D12_RESOURCE_BARRIER CreateTransition(ID3D12Resource resource, D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after,
            uint subresource = D3D12Const.RESOURCE_BARRIER_ALL_SUBRESOURCES, D3D12_RESOURCE_BARRIER_FLAGS flags = D3D12_RESOURCE_BARRIER_FLAGS.NONE)
        {
            return CreateTransition(Com.BorrowPtr(resource, typeof(ID3D12Resource)), before, after, subresource, flags);
        }

        /// <summary>UAV barrier; resource may be IntPtr.Zero (= any UAV access).</summary>
        public static D3D12_RESOURCE_BARRIER CreateUav(IntPtr resource)
        {
            D3D12_RESOURCE_BARRIER b = new D3D12_RESOURCE_BARRIER();
            b.Type = D3D12_RESOURCE_BARRIER_TYPE.UAV;
            b.UAV.pResource = resource;
            return b;
        }

        public static D3D12_RESOURCE_BARRIER CreateUav(ID3D12Resource resource)
        {
            return CreateUav(resource == null ? IntPtr.Zero : Com.BorrowPtr(resource, typeof(ID3D12Resource)));
        }

        public static D3D12_RESOURCE_BARRIER CreateAliasing(IntPtr before, IntPtr after)
        {
            D3D12_RESOURCE_BARRIER b = new D3D12_RESOURCE_BARRIER();
            b.Type = D3D12_RESOURCE_BARRIER_TYPE.ALIASING;
            b.Aliasing.pResourceBefore = before;
            b.Aliasing.pResourceAfter = after;
            return b;
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Descriptor heaps and handles
    // ------------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_DESCRIPTOR_HEAP_DESC               // 16
    {
        public D3D12_DESCRIPTOR_HEAP_TYPE Type;
        public uint NumDescriptors;
        public D3D12_DESCRIPTOR_HEAP_FLAGS Flags;
        public uint NodeMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_CPU_DESCRIPTOR_HANDLE              // 8 (SIZE_T ptr)
    {
        public IntPtr ptr;
        public D3D12_CPU_DESCRIPTOR_HANDLE Offset(int index, uint incrementSize)
        {
            D3D12_CPU_DESCRIPTOR_HANDLE h;
            h.ptr = new IntPtr(ptr.ToInt64() + (long)index * incrementSize);
            return h;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_GPU_DESCRIPTOR_HANDLE              // 8 (UINT64 ptr)
    {
        public ulong ptr;
        public D3D12_GPU_DESCRIPTOR_HANDLE Offset(int index, uint incrementSize)
        {
            D3D12_GPU_DESCRIPTOR_HANDLE h;
            h.ptr = (ulong)((long)ptr + (long)index * incrementSize);
            return h;
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // View descriptions (unions -> explicit layout)
    // ------------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_CONSTANT_BUFFER_VIEW_DESC          // 16
    {
        public ulong BufferLocation;                       // D3D12_GPU_VIRTUAL_ADDRESS
        public uint SizeInBytes;
    }

    [StructLayout(LayoutKind.Sequential)] public struct D3D12_BUFFER_SRV { public ulong FirstElement; public uint NumElements; public uint StructureByteStride; public D3D12_BUFFER_SRV_FLAGS Flags; } // 24
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX1D_SRV { public uint MostDetailedMip; public uint MipLevels; public float ResourceMinLODClamp; }                                    // 12
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX1D_ARRAY_SRV { public uint MostDetailedMip; public uint MipLevels; public uint FirstArraySlice; public uint ArraySize; public float ResourceMinLODClamp; } // 20
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX2D_SRV { public uint MostDetailedMip; public uint MipLevels; public uint PlaneSlice; public float ResourceMinLODClamp; }               // 16
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX2D_ARRAY_SRV { public uint MostDetailedMip; public uint MipLevels; public uint FirstArraySlice; public uint ArraySize; public uint PlaneSlice; public float ResourceMinLODClamp; } // 24
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX2DMS_SRV { public uint UnusedField_NothingToDefine; }                                                                                // 4
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX2DMS_ARRAY_SRV { public uint FirstArraySlice; public uint ArraySize; }                                                               // 8
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX3D_SRV { public uint MostDetailedMip; public uint MipLevels; public float ResourceMinLODClamp; }                                    // 12
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEXCUBE_SRV { public uint MostDetailedMip; public uint MipLevels; public float ResourceMinLODClamp; }                                  // 12
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEXCUBE_ARRAY_SRV { public uint MostDetailedMip; public uint MipLevels; public uint First2DArrayFace; public uint NumCubes; public float ResourceMinLODClamp; } // 20
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_RAYTRACING_ACCELERATION_STRUCTURE_SRV { public ulong Location; }                                                                       // 8

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct D3D12_SHADER_RESOURCE_VIEW_DESC          // 40: Format, ViewDimension, Shader4ComponentMapping, union @16
    {
        [FieldOffset(0)] public DXGI_FORMAT Format;
        [FieldOffset(4)] public D3D12_SRV_DIMENSION ViewDimension;
        [FieldOffset(8)] public uint Shader4ComponentMapping;
        [FieldOffset(16)] public D3D12_BUFFER_SRV Buffer;
        [FieldOffset(16)] public D3D12_TEX1D_SRV Texture1D;
        [FieldOffset(16)] public D3D12_TEX1D_ARRAY_SRV Texture1DArray;
        [FieldOffset(16)] public D3D12_TEX2D_SRV Texture2D;
        [FieldOffset(16)] public D3D12_TEX2D_ARRAY_SRV Texture2DArray;
        [FieldOffset(16)] public D3D12_TEX2DMS_SRV Texture2DMS;
        [FieldOffset(16)] public D3D12_TEX2DMS_ARRAY_SRV Texture2DMSArray;
        [FieldOffset(16)] public D3D12_TEX3D_SRV Texture3D;
        [FieldOffset(16)] public D3D12_TEXCUBE_SRV TextureCube;
        [FieldOffset(16)] public D3D12_TEXCUBE_ARRAY_SRV TextureCubeArray;
        [FieldOffset(16)] public D3D12_RAYTRACING_ACCELERATION_STRUCTURE_SRV RaytracingAccelerationStructure;

        public static D3D12_SHADER_RESOURCE_VIEW_DESC Tex2D(DXGI_FORMAT format, uint mipLevels = 1)
        {
            D3D12_SHADER_RESOURCE_VIEW_DESC d = new D3D12_SHADER_RESOURCE_VIEW_DESC();
            d.Format = format;
            d.ViewDimension = D3D12_SRV_DIMENSION.TEXTURE2D;
            d.Shader4ComponentMapping = D3D12Const.DEFAULT_SHADER_4_COMPONENT_MAPPING;
            d.Texture2D.MipLevels = mipLevels;
            return d;
        }
    }

    [StructLayout(LayoutKind.Sequential)] public struct D3D12_BUFFER_UAV { public ulong FirstElement; public uint NumElements; public uint StructureByteStride; public ulong CounterOffsetInBytes; public D3D12_BUFFER_UAV_FLAGS Flags; } // 32
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX1D_UAV { public uint MipSlice; }                                                                    // 4
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX1D_ARRAY_UAV { public uint MipSlice; public uint FirstArraySlice; public uint ArraySize; }          // 12
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX2D_UAV { public uint MipSlice; public uint PlaneSlice; }                                            // 8
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX2D_ARRAY_UAV { public uint MipSlice; public uint FirstArraySlice; public uint ArraySize; public uint PlaneSlice; } // 16
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX2DMS_UAV { public uint UnusedField_NothingToDefine; }                                             // 4
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX2DMS_ARRAY_UAV { public uint FirstArraySlice; public uint ArraySize; }                            // 8
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX3D_UAV { public uint MipSlice; public uint FirstWSlice; public uint WSize; }                       // 12

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct D3D12_UNORDERED_ACCESS_VIEW_DESC         // 40: Format, ViewDimension, union @8 (BUFFER_UAV is 32)
    {
        [FieldOffset(0)] public DXGI_FORMAT Format;
        [FieldOffset(4)] public D3D12_UAV_DIMENSION ViewDimension;
        [FieldOffset(8)] public D3D12_BUFFER_UAV Buffer;
        [FieldOffset(8)] public D3D12_TEX1D_UAV Texture1D;
        [FieldOffset(8)] public D3D12_TEX1D_ARRAY_UAV Texture1DArray;
        [FieldOffset(8)] public D3D12_TEX2D_UAV Texture2D;
        [FieldOffset(8)] public D3D12_TEX2D_ARRAY_UAV Texture2DArray;
        [FieldOffset(8)] public D3D12_TEX2DMS_UAV Texture2DMS;
        [FieldOffset(8)] public D3D12_TEX2DMS_ARRAY_UAV Texture2DMSArray;
        [FieldOffset(8)] public D3D12_TEX3D_UAV Texture3D;

        public static D3D12_UNORDERED_ACCESS_VIEW_DESC Tex2D(DXGI_FORMAT format, uint mipSlice = 0)
        {
            D3D12_UNORDERED_ACCESS_VIEW_DESC d = new D3D12_UNORDERED_ACCESS_VIEW_DESC();
            d.Format = format;
            d.ViewDimension = D3D12_UAV_DIMENSION.TEXTURE2D;
            d.Texture2D.MipSlice = mipSlice;
            return d;
        }
    }

    [StructLayout(LayoutKind.Sequential)] public struct D3D12_BUFFER_RTV { public ulong FirstElement; public uint NumElements; }                                       // 16
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX1D_RTV { public uint MipSlice; }                                                                    // 4
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX1D_ARRAY_RTV { public uint MipSlice; public uint FirstArraySlice; public uint ArraySize; }          // 12
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX2D_RTV { public uint MipSlice; public uint PlaneSlice; }                                            // 8
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX2D_ARRAY_RTV { public uint MipSlice; public uint FirstArraySlice; public uint ArraySize; public uint PlaneSlice; } // 16
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX2DMS_RTV { public uint UnusedField_NothingToDefine; }                                             // 4
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX2DMS_ARRAY_RTV { public uint FirstArraySlice; public uint ArraySize; }                            // 8
    [StructLayout(LayoutKind.Sequential)] public struct D3D12_TEX3D_RTV { public uint MipSlice; public uint FirstWSlice; public uint WSize; }                       // 12

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct D3D12_RENDER_TARGET_VIEW_DESC            // 24: Format, ViewDimension, union @8 (BUFFER_RTV is 16)
    {
        [FieldOffset(0)] public DXGI_FORMAT Format;
        [FieldOffset(4)] public D3D12_RTV_DIMENSION ViewDimension;
        [FieldOffset(8)] public D3D12_BUFFER_RTV Buffer;
        [FieldOffset(8)] public D3D12_TEX1D_RTV Texture1D;
        [FieldOffset(8)] public D3D12_TEX1D_ARRAY_RTV Texture1DArray;
        [FieldOffset(8)] public D3D12_TEX2D_RTV Texture2D;
        [FieldOffset(8)] public D3D12_TEX2D_ARRAY_RTV Texture2DArray;
        [FieldOffset(8)] public D3D12_TEX2DMS_RTV Texture2DMS;
        [FieldOffset(8)] public D3D12_TEX2DMS_ARRAY_RTV Texture2DMSArray;
        [FieldOffset(8)] public D3D12_TEX3D_RTV Texture3D;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct D3D12_SAMPLER_DESC                // 52
    {
        public D3D12_FILTER Filter;
        public D3D12_TEXTURE_ADDRESS_MODE AddressU;
        public D3D12_TEXTURE_ADDRESS_MODE AddressV;
        public D3D12_TEXTURE_ADDRESS_MODE AddressW;
        public float MipLODBias;
        public uint MaxAnisotropy;
        public D3D12_COMPARISON_FUNC ComparisonFunc;
        public fixed float BorderColor[4];
        public float MinLOD;
        public float MaxLOD;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Copies
    // ------------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_SUBRESOURCE_FOOTPRINT              // 20
    {
        public DXGI_FORMAT Format;
        public uint Width;
        public uint Height;
        public uint Depth;
        public uint RowPitch;                              // multiple of 256
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_PLACED_SUBRESOURCE_FOOTPRINT       // 32
    {
        public ulong Offset;                               // multiple of 512
        public D3D12_SUBRESOURCE_FOOTPRINT Footprint;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct D3D12_TEXTURE_COPY_LOCATION              // 48: pResource, Type, union @16
    {
        [FieldOffset(0)] public IntPtr pResource;          // ID3D12Resource* (borrowed)
        [FieldOffset(8)] public D3D12_TEXTURE_COPY_TYPE Type;
        [FieldOffset(16)] public D3D12_PLACED_SUBRESOURCE_FOOTPRINT PlacedFootprint;
        [FieldOffset(16)] public uint SubresourceIndex;

        public static D3D12_TEXTURE_COPY_LOCATION FromSubresource(IntPtr resource, uint subresource)
        {
            D3D12_TEXTURE_COPY_LOCATION l = new D3D12_TEXTURE_COPY_LOCATION();
            l.pResource = resource;
            l.Type = D3D12_TEXTURE_COPY_TYPE.SUBRESOURCE_INDEX;
            l.SubresourceIndex = subresource;
            return l;
        }

        public static D3D12_TEXTURE_COPY_LOCATION FromSubresource(ID3D12Resource resource, uint subresource)
        {
            return FromSubresource(Com.BorrowPtr(resource, typeof(ID3D12Resource)), subresource);
        }

        public static D3D12_TEXTURE_COPY_LOCATION FromFootprint(IntPtr buffer, D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint)
        {
            D3D12_TEXTURE_COPY_LOCATION l = new D3D12_TEXTURE_COPY_LOCATION();
            l.pResource = buffer;
            l.Type = D3D12_TEXTURE_COPY_TYPE.PLACED_FOOTPRINT;
            l.PlacedFootprint = footprint;
            return l;
        }

        public static D3D12_TEXTURE_COPY_LOCATION FromFootprint(ID3D12Resource buffer, D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint)
        {
            return FromFootprint(Com.BorrowPtr(buffer, typeof(ID3D12Resource)), footprint);
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Queries
    // ------------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_QUERY_HEAP_DESC                    // 12
    {
        public D3D12_QUERY_HEAP_TYPE Type;
        public uint Count;
        public uint NodeMask;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Pipeline state
    // ------------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_SHADER_BYTECODE                    // 16
    {
        public IntPtr pShaderBytecode;
        public UIntPtr BytecodeLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_CACHED_PIPELINE_STATE              // 16
    {
        public IntPtr pCachedBlob;
        public UIntPtr CachedBlobSizeInBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_COMPUTE_PIPELINE_STATE_DESC        // 56
    {
        public IntPtr pRootSignature;                      // ID3D12RootSignature* (borrowed)
        public D3D12_SHADER_BYTECODE CS;
        public uint NodeMask;
        public D3D12_CACHED_PIPELINE_STATE CachedPSO;
        public D3D12_PIPELINE_STATE_FLAGS Flags;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Root signature v1.0
    // ------------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_DESCRIPTOR_RANGE                   // 20
    {
        public D3D12_DESCRIPTOR_RANGE_TYPE RangeType;
        public uint NumDescriptors;
        public uint BaseShaderRegister;
        public uint RegisterSpace;
        public uint OffsetInDescriptorsFromTableStart;     // D3D12Const.DESCRIPTOR_RANGE_OFFSET_APPEND

        public D3D12_DESCRIPTOR_RANGE(D3D12_DESCRIPTOR_RANGE_TYPE type, uint num, uint baseRegister, uint space = 0,
            uint offset = D3D12Const.DESCRIPTOR_RANGE_OFFSET_APPEND)
        {
            RangeType = type; NumDescriptors = num; BaseShaderRegister = baseRegister; RegisterSpace = space;
            OffsetInDescriptorsFromTableStart = offset;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_ROOT_DESCRIPTOR_TABLE              // 16
    {
        public uint NumDescriptorRanges;
        public IntPtr pDescriptorRanges;                   // const D3D12_DESCRIPTOR_RANGE* (must stay pinned until serialized)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_ROOT_CONSTANTS                     // 12
    {
        public uint ShaderRegister;
        public uint RegisterSpace;
        public uint Num32BitValues;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_ROOT_DESCRIPTOR                    // 8
    {
        public uint ShaderRegister;
        public uint RegisterSpace;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct D3D12_ROOT_PARAMETER                     // 32: ParameterType, union @8 (table = 16), ShaderVisibility @24
    {
        [FieldOffset(0)] public D3D12_ROOT_PARAMETER_TYPE ParameterType;
        [FieldOffset(8)] public D3D12_ROOT_DESCRIPTOR_TABLE DescriptorTable;
        [FieldOffset(8)] public D3D12_ROOT_CONSTANTS Constants;
        [FieldOffset(8)] public D3D12_ROOT_DESCRIPTOR Descriptor;
        [FieldOffset(24)] public D3D12_SHADER_VISIBILITY ShaderVisibility;

        public static D3D12_ROOT_PARAMETER Table(uint numRanges, IntPtr pRanges, D3D12_SHADER_VISIBILITY vis = D3D12_SHADER_VISIBILITY.ALL)
        {
            D3D12_ROOT_PARAMETER p = new D3D12_ROOT_PARAMETER();
            p.ParameterType = D3D12_ROOT_PARAMETER_TYPE.DESCRIPTOR_TABLE;
            p.DescriptorTable.NumDescriptorRanges = numRanges;
            p.DescriptorTable.pDescriptorRanges = pRanges;
            p.ShaderVisibility = vis;
            return p;
        }

        public static D3D12_ROOT_PARAMETER Constants32(uint shaderRegister, uint registerSpace, uint num32BitValues,
            D3D12_SHADER_VISIBILITY vis = D3D12_SHADER_VISIBILITY.ALL)
        {
            D3D12_ROOT_PARAMETER p = new D3D12_ROOT_PARAMETER();
            p.ParameterType = D3D12_ROOT_PARAMETER_TYPE.TYPE_32BIT_CONSTANTS;
            p.Constants.ShaderRegister = shaderRegister;
            p.Constants.RegisterSpace = registerSpace;
            p.Constants.Num32BitValues = num32BitValues;
            p.ShaderVisibility = vis;
            return p;
        }

        /// <summary>Root CBV / SRV / UAV (type = CBV, SRV or UAV).</summary>
        public static D3D12_ROOT_PARAMETER RootDescriptor(D3D12_ROOT_PARAMETER_TYPE type, uint shaderRegister, uint registerSpace = 0,
            D3D12_SHADER_VISIBILITY vis = D3D12_SHADER_VISIBILITY.ALL)
        {
            D3D12_ROOT_PARAMETER p = new D3D12_ROOT_PARAMETER();
            p.ParameterType = type;
            p.Descriptor.ShaderRegister = shaderRegister;
            p.Descriptor.RegisterSpace = registerSpace;
            p.ShaderVisibility = vis;
            return p;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_STATIC_SAMPLER_DESC                // 52
    {
        public D3D12_FILTER Filter;
        public D3D12_TEXTURE_ADDRESS_MODE AddressU;
        public D3D12_TEXTURE_ADDRESS_MODE AddressV;
        public D3D12_TEXTURE_ADDRESS_MODE AddressW;
        public float MipLODBias;
        public uint MaxAnisotropy;
        public D3D12_COMPARISON_FUNC ComparisonFunc;
        public D3D12_STATIC_BORDER_COLOR BorderColor;
        public float MinLOD;
        public float MaxLOD;
        public uint ShaderRegister;
        public uint RegisterSpace;
        public D3D12_SHADER_VISIBILITY ShaderVisibility;

        public static D3D12_STATIC_SAMPLER_DESC Create(uint shaderRegister, D3D12_FILTER filter, D3D12_TEXTURE_ADDRESS_MODE address,
            uint registerSpace = 0, D3D12_SHADER_VISIBILITY vis = D3D12_SHADER_VISIBILITY.ALL)
        {
            D3D12_STATIC_SAMPLER_DESC s = new D3D12_STATIC_SAMPLER_DESC();
            s.Filter = filter;
            s.AddressU = address; s.AddressV = address; s.AddressW = address;
            s.MipLODBias = 0;
            s.MaxAnisotropy = 16;
            s.ComparisonFunc = D3D12_COMPARISON_FUNC.LESS_EQUAL;
            s.BorderColor = D3D12_STATIC_BORDER_COLOR.OPAQUE_WHITE;
            s.MinLOD = 0;
            s.MaxLOD = D3D12Const.FLOAT32_MAX;
            s.ShaderRegister = shaderRegister;
            s.RegisterSpace = registerSpace;
            s.ShaderVisibility = vis;
            return s;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D12_ROOT_SIGNATURE_DESC                // 40
    {
        public uint NumParameters;
        public IntPtr pParameters;                         // const D3D12_ROOT_PARAMETER*
        public uint NumStaticSamplers;
        public IntPtr pStaticSamplers;                     // const D3D12_STATIC_SAMPLER_DESC*
        public D3D12_ROOT_SIGNATURE_FLAGS Flags;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Interfaces (slot numbers in /*NN*/ include IUnknown 0-2)
    // ------------------------------------------------------------------------------------------------------------
    // ID3D12Object : IUnknown (7 slots)
    [ComImport, Guid("c4fec28f-7966-4e95-9f94-f431cb56c3b8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12Object
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
    }

    // ID3D12DeviceChild : ID3D12Object (8 slots)
    [ComImport, Guid("905db94b-a00c-4140-9df5-2b64ca9ea357"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12DeviceChild
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
    }

    // ID3D12Pageable : ID3D12DeviceChild (8 slots)
    [ComImport, Guid("63ee58fb-1268-4835-86da-f008ce62f0d6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12Pageable
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
    }

    // ID3D12RootSignature : ID3D12DeviceChild (8 slots)
    [ComImport, Guid("c54a6b66-72df-4ee8-8be5-a946a1429214"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12RootSignature
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
    }

    // ID3D12QueryHeap : ID3D12Pageable (8 slots)
    [ComImport, Guid("0d9658ae-ed45-469e-a61d-970ec583cab4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12QueryHeap
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
    }

    // ID3D12Heap : ID3D12Pageable (9 slots)
    [ComImport, Guid("6b3b2502-6e51-45b3-90ee-9884265e8df3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12Heap
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
        /* 8*/ [PreserveSig] IntPtr GetDesc(out D3D12_HEAP_DESC retVal);
    }

    // ID3D12Resource : ID3D12Pageable (15 slots)
    [ComImport, Guid("696442be-a72e-4059-bc79-5b5c98040fad"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12Resource
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
        /* 8*/ [PreserveSig] int Map(uint Subresource, D3D12_RANGE* pReadRange, out IntPtr ppData);
        /* 9*/ [PreserveSig] void Unmap(uint Subresource, D3D12_RANGE* pWrittenRange);
        /*10*/ [PreserveSig] IntPtr GetDesc(out D3D12_RESOURCE_DESC retVal);
        /*11*/ [PreserveSig] ulong GetGPUVirtualAddress();
        /*12*/ [PreserveSig] int WriteToSubresource(uint DstSubresource, D3D12_BOX* pDstBox, void* pSrcData, uint SrcRowPitch, uint SrcDepthPitch);
        /*13*/ [PreserveSig] int ReadFromSubresource(void* pDstData, uint DstRowPitch, uint DstDepthPitch, uint SrcSubresource, D3D12_BOX* pSrcBox);
        /*14*/ [PreserveSig] int GetHeapProperties(D3D12_HEAP_PROPERTIES* pHeapProperties, D3D12_HEAP_FLAGS* pHeapFlags);
    }

    // ID3D12CommandAllocator : ID3D12Pageable (9 slots)
    [ComImport, Guid("6102dee4-af59-4b09-b999-b44d73f09b24"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12CommandAllocator
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
        /* 8*/ [PreserveSig] int Reset();
    }

    // ID3D12Fence : ID3D12Pageable (11 slots)
    [ComImport, Guid("0a753dcf-c4d8-4b91-adf6-be5a60d95a76"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12Fence
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
        /* 8*/ [PreserveSig] ulong GetCompletedValue();
        /* 9*/ [PreserveSig] int SetEventOnCompletion(ulong Value, IntPtr hEvent);
        /*10*/ [PreserveSig] int Signal(ulong Value);
    }

    // ID3D12PipelineState : ID3D12Pageable (9 slots)
    [ComImport, Guid("765a30f3-f624-4c6f-a828-ace948622445"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12PipelineState
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
        /* 8*/ [PreserveSig] int GetCachedBlob(out ID3DBlob ppBlob);
    }

    // ID3D12DescriptorHeap : ID3D12Pageable (11 slots)
    [ComImport, Guid("8efb471d-616c-4f49-90f7-127bb763fa51"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12DescriptorHeap
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
        /* 8*/ [PreserveSig] IntPtr GetDesc(out D3D12_DESCRIPTOR_HEAP_DESC retVal);
        /* 9*/ [PreserveSig] IntPtr GetCPUDescriptorHandleForHeapStart(out D3D12_CPU_DESCRIPTOR_HANDLE retVal);
        /*10*/ [PreserveSig] IntPtr GetGPUDescriptorHandleForHeapStart(out D3D12_GPU_DESCRIPTOR_HANDLE retVal);
    }

    // ID3D12CommandList : ID3D12DeviceChild (9 slots). Slot 8 = C++ GetType.
    [ComImport, Guid("7116d91c-e7e4-47ce-b8c6-ec8168f437e5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12CommandList
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
        /* 8*/ [PreserveSig] D3D12_COMMAND_LIST_TYPE GetCommandListType();
    }

    // ID3D12GraphicsCommandList : ID3D12CommandList (60 slots). Slot 8 = C++ GetType.
    [ComImport, Guid("5b160d0f-ac1b-4185-8ba8-b3ae42a5a455"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12GraphicsCommandList
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
        /* 8*/ [PreserveSig] D3D12_COMMAND_LIST_TYPE GetCommandListType();
        /* 9*/ [PreserveSig] int Close();
        /*10*/ [PreserveSig] int Reset(ID3D12CommandAllocator pAllocator, ID3D12PipelineState pInitialState);
        /*11*/ [PreserveSig] void ClearState(ID3D12PipelineState pPipelineState);
        /*12*/ [PreserveSig] void DrawInstanced(uint VertexCountPerInstance, uint InstanceCount, uint StartVertexLocation, uint StartInstanceLocation);
        /*13*/ [PreserveSig] void DrawIndexedInstanced(uint IndexCountPerInstance, uint InstanceCount, uint StartIndexLocation, int BaseVertexLocation, uint StartInstanceLocation);
        /*14*/ [PreserveSig] void Dispatch(uint ThreadGroupCountX, uint ThreadGroupCountY, uint ThreadGroupCountZ);
        /*15*/ [PreserveSig] void CopyBufferRegion(ID3D12Resource pDstBuffer, ulong DstOffset, ID3D12Resource pSrcBuffer, ulong SrcOffset, ulong NumBytes);
        /*16*/ [PreserveSig] void CopyTextureRegion([In] ref D3D12_TEXTURE_COPY_LOCATION pDst, uint DstX, uint DstY, uint DstZ, [In] ref D3D12_TEXTURE_COPY_LOCATION pSrc, D3D12_BOX* pSrcBox);
        /*17*/ [PreserveSig] void CopyResource(ID3D12Resource pDstResource, ID3D12Resource pSrcResource);
        /*18*/ [PreserveSig] void CopyTiles(ID3D12Resource pTiledResource, IntPtr pTileRegionStartCoordinate, IntPtr pTileRegionSize, ID3D12Resource pBuffer, ulong BufferStartOffsetInBytes, int Flags);
        /*19*/ [PreserveSig] void ResolveSubresource(ID3D12Resource pDstResource, uint DstSubresource, ID3D12Resource pSrcResource, uint SrcSubresource, DXGI_FORMAT Format);
        /*20*/ [PreserveSig] void IASetPrimitiveTopology(int PrimitiveTopology);
        /*21*/ [PreserveSig] void RSSetViewports(uint NumViewports, [In] ref D3D12_VIEWPORT pViewports);
        /*22*/ [PreserveSig] void RSSetScissorRects(uint NumRects, [In] ref RECT pRects);
        /*23*/ [PreserveSig] void OMSetBlendFactor([In, MarshalAs(UnmanagedType.LPArray, SizeConst = 4)] float[] BlendFactor);
        /*24*/ [PreserveSig] void OMSetStencilRef(uint StencilRef);
        /*25*/ [PreserveSig] void SetPipelineState(ID3D12PipelineState pPipelineState);
        /*26*/ [PreserveSig] void ResourceBarrier(uint NumBarriers, [In] ref D3D12_RESOURCE_BARRIER pBarriers);
        /*27*/ [PreserveSig] void ExecuteBundle(ID3D12GraphicsCommandList pCommandList);
        /*28*/ [PreserveSig] void SetDescriptorHeaps(uint NumDescriptorHeaps, [In] ref IntPtr ppDescriptorHeaps);
        /*29*/ [PreserveSig] void SetComputeRootSignature(ID3D12RootSignature pRootSignature);
        /*30*/ [PreserveSig] void SetGraphicsRootSignature(ID3D12RootSignature pRootSignature);
        /*31*/ [PreserveSig] void SetComputeRootDescriptorTable(uint RootParameterIndex, D3D12_GPU_DESCRIPTOR_HANDLE BaseDescriptor);
        /*32*/ [PreserveSig] void SetGraphicsRootDescriptorTable(uint RootParameterIndex, D3D12_GPU_DESCRIPTOR_HANDLE BaseDescriptor);
        /*33*/ [PreserveSig] void SetComputeRoot32BitConstant(uint RootParameterIndex, uint SrcData, uint DestOffsetIn32BitValues);
        /*34*/ [PreserveSig] void SetGraphicsRoot32BitConstant(uint RootParameterIndex, uint SrcData, uint DestOffsetIn32BitValues);
        /*35*/ [PreserveSig] void SetComputeRoot32BitConstants(uint RootParameterIndex, uint Num32BitValuesToSet, void* pSrcData, uint DestOffsetIn32BitValues);
        /*36*/ [PreserveSig] void SetGraphicsRoot32BitConstants(uint RootParameterIndex, uint Num32BitValuesToSet, void* pSrcData, uint DestOffsetIn32BitValues);
        /*37*/ [PreserveSig] void SetComputeRootConstantBufferView(uint RootParameterIndex, ulong BufferLocation);
        /*38*/ [PreserveSig] void SetGraphicsRootConstantBufferView(uint RootParameterIndex, ulong BufferLocation);
        /*39*/ [PreserveSig] void SetComputeRootShaderResourceView(uint RootParameterIndex, ulong BufferLocation);
        /*40*/ [PreserveSig] void SetGraphicsRootShaderResourceView(uint RootParameterIndex, ulong BufferLocation);
        /*41*/ [PreserveSig] void SetComputeRootUnorderedAccessView(uint RootParameterIndex, ulong BufferLocation);
        /*42*/ [PreserveSig] void SetGraphicsRootUnorderedAccessView(uint RootParameterIndex, ulong BufferLocation);
        /*43*/ [PreserveSig] void IASetIndexBuffer(IntPtr pView);
        /*44*/ [PreserveSig] void IASetVertexBuffers(uint StartSlot, uint NumViews, IntPtr pViews);
        /*45*/ [PreserveSig] void SOSetTargets(uint StartSlot, uint NumViews, IntPtr pViews);
        /*46*/ [PreserveSig] void OMSetRenderTargets(uint NumRenderTargetDescriptors, D3D12_CPU_DESCRIPTOR_HANDLE* pRenderTargetDescriptors, int RTsSingleHandleToDescriptorRange, D3D12_CPU_DESCRIPTOR_HANDLE* pDepthStencilDescriptor);
        /*47*/ [PreserveSig] void ClearDepthStencilView(D3D12_CPU_DESCRIPTOR_HANDLE DepthStencilView, D3D12_CLEAR_FLAGS ClearFlags, float Depth, byte Stencil, uint NumRects, RECT* pRects);
        /*48*/ [PreserveSig] void ClearRenderTargetView(D3D12_CPU_DESCRIPTOR_HANDLE RenderTargetView, [In, MarshalAs(UnmanagedType.LPArray, SizeConst = 4)] float[] ColorRGBA, uint NumRects, RECT* pRects);
        /*49*/ [PreserveSig] void ClearUnorderedAccessViewUint(D3D12_GPU_DESCRIPTOR_HANDLE ViewGPUHandleInCurrentHeap, D3D12_CPU_DESCRIPTOR_HANDLE ViewCPUHandle, ID3D12Resource pResource, [In, MarshalAs(UnmanagedType.LPArray, SizeConst = 4)] uint[] Values, uint NumRects, RECT* pRects);
        /*50*/ [PreserveSig] void ClearUnorderedAccessViewFloat(D3D12_GPU_DESCRIPTOR_HANDLE ViewGPUHandleInCurrentHeap, D3D12_CPU_DESCRIPTOR_HANDLE ViewCPUHandle, ID3D12Resource pResource, [In, MarshalAs(UnmanagedType.LPArray, SizeConst = 4)] float[] Values, uint NumRects, RECT* pRects);
        /*51*/ [PreserveSig] void DiscardResource(ID3D12Resource pResource, IntPtr pRegion);
        /*52*/ [PreserveSig] void BeginQuery(ID3D12QueryHeap pQueryHeap, D3D12_QUERY_TYPE Type, uint Index);
        /*53*/ [PreserveSig] void EndQuery(ID3D12QueryHeap pQueryHeap, D3D12_QUERY_TYPE Type, uint Index);
        /*54*/ [PreserveSig] void ResolveQueryData(ID3D12QueryHeap pQueryHeap, D3D12_QUERY_TYPE Type, uint StartIndex, uint NumQueries, ID3D12Resource pDestinationBuffer, ulong AlignedDestinationBufferOffset);
        /*55*/ [PreserveSig] void SetPredication(ID3D12Resource pBuffer, ulong AlignedBufferOffset, int Operation);
        /*56*/ [PreserveSig] void SetMarker(uint Metadata, void* pData, uint Size);
        /*57*/ [PreserveSig] void BeginEvent(uint Metadata, void* pData, uint Size);
        /*58*/ [PreserveSig] void EndEvent();
        /*59*/ [PreserveSig] void ExecuteIndirect(IntPtr pCommandSignature, uint MaxCommandCount, ID3D12Resource pArgumentBuffer, ulong ArgumentBufferOffset, ID3D12Resource pCountBuffer, ulong CountBufferOffset);
    }

    // ID3D12CommandQueue : ID3D12Pageable (19 slots)
    [ComImport, Guid("0ec870a6-5d7e-4c22-8cfc-5baae07616ed"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12CommandQueue
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] int GetDevice([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDevice);
        /* 8*/ [PreserveSig] void UpdateTileMappings(ID3D12Resource pResource, uint NumResourceRegions, IntPtr pResourceRegionStartCoordinates, IntPtr pResourceRegionSizes, ID3D12Heap pHeap, uint NumRanges, IntPtr pRangeFlags, uint* pHeapRangeStartOffsets, uint* pRangeTileCounts, int Flags);
        /* 9*/ [PreserveSig] void CopyTileMappings(ID3D12Resource pDstResource, IntPtr pDstRegionStartCoordinate, ID3D12Resource pSrcResource, IntPtr pSrcRegionStartCoordinate, IntPtr pRegionSize, int Flags);
        /*10*/ [PreserveSig] void ExecuteCommandLists(uint NumCommandLists, [In] ref IntPtr ppCommandLists);
        /*11*/ [PreserveSig] void SetMarker(uint Metadata, void* pData, uint Size);
        /*12*/ [PreserveSig] void BeginEvent(uint Metadata, void* pData, uint Size);
        /*13*/ [PreserveSig] void EndEvent();
        /*14*/ [PreserveSig] int Signal(ID3D12Fence pFence, ulong Value);
        /*15*/ [PreserveSig] int Wait(ID3D12Fence pFence, ulong Value);
        /*16*/ [PreserveSig] int GetTimestampFrequency(out ulong pFrequency);
        /*17*/ [PreserveSig] int GetClockCalibration(out ulong pGpuTimestamp, out ulong pCpuTimestamp);
        /*18*/ [PreserveSig] IntPtr GetDesc(out D3D12_COMMAND_QUEUE_DESC retVal);
    }

    // ID3D12Device : ID3D12Object (44 slots, base interface only)
    [ComImport, Guid("189819f1-1db6-4b57-be54-1821339b85f7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12Device
    {
        /* 3*/ [PreserveSig] int GetPrivateData([In] ref Guid guid, ref uint pDataSize, void* pData);
        /* 4*/ [PreserveSig] int SetPrivateData([In] ref Guid guid, uint DataSize, void* pData);
        /* 5*/ [PreserveSig] int SetPrivateDataInterface([In] ref Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object pData);
        /* 6*/ [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string Name);
        /* 7*/ [PreserveSig] uint GetNodeCount();
        /* 8*/ [PreserveSig] int CreateCommandQueue([In] ref D3D12_COMMAND_QUEUE_DESC pDesc, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppCommandQueue);
        /* 9*/ [PreserveSig] int CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE type, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppCommandAllocator);
        /*10*/ [PreserveSig] int CreateGraphicsPipelineState(IntPtr pDesc, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppPipelineState);
        /*11*/ [PreserveSig] int CreateComputePipelineState([In] ref D3D12_COMPUTE_PIPELINE_STATE_DESC pDesc, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppPipelineState);
        /*12*/ [PreserveSig] int CreateCommandList(uint nodeMask, D3D12_COMMAND_LIST_TYPE type, ID3D12CommandAllocator pCommandAllocator, ID3D12PipelineState pInitialState, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppCommandList);
        /*13*/ [PreserveSig] int CheckFeatureSupport(D3D12_FEATURE Feature, void* pFeatureSupportData, uint FeatureSupportDataSize);
        /*14*/ [PreserveSig] int CreateDescriptorHeap([In] ref D3D12_DESCRIPTOR_HEAP_DESC pDescriptorHeapDesc, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvHeap);
        /*15*/ [PreserveSig] uint GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE DescriptorHeapType);
        /*16*/ [PreserveSig] int CreateRootSignature(uint nodeMask, IntPtr pBlobWithRootSignature, UIntPtr blobLengthInBytes, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvRootSignature);
        /*17*/ [PreserveSig] void CreateConstantBufferView(D3D12_CONSTANT_BUFFER_VIEW_DESC* pDesc, D3D12_CPU_DESCRIPTOR_HANDLE DestDescriptor);
        /*18*/ [PreserveSig] void CreateShaderResourceView(ID3D12Resource pResource, D3D12_SHADER_RESOURCE_VIEW_DESC* pDesc, D3D12_CPU_DESCRIPTOR_HANDLE DestDescriptor);
        /*19*/ [PreserveSig] void CreateUnorderedAccessView(ID3D12Resource pResource, ID3D12Resource pCounterResource, D3D12_UNORDERED_ACCESS_VIEW_DESC* pDesc, D3D12_CPU_DESCRIPTOR_HANDLE DestDescriptor);
        /*20*/ [PreserveSig] void CreateRenderTargetView(ID3D12Resource pResource, D3D12_RENDER_TARGET_VIEW_DESC* pDesc, D3D12_CPU_DESCRIPTOR_HANDLE DestDescriptor);
        /*21*/ [PreserveSig] void CreateDepthStencilView(ID3D12Resource pResource, IntPtr pDesc, D3D12_CPU_DESCRIPTOR_HANDLE DestDescriptor);
        /*22*/ [PreserveSig] void CreateSampler(D3D12_SAMPLER_DESC* pDesc, D3D12_CPU_DESCRIPTOR_HANDLE DestDescriptor);
        /*23*/ [PreserveSig] void CopyDescriptors(uint NumDestDescriptorRanges, D3D12_CPU_DESCRIPTOR_HANDLE* pDestDescriptorRangeStarts, uint* pDestDescriptorRangeSizes, uint NumSrcDescriptorRanges, D3D12_CPU_DESCRIPTOR_HANDLE* pSrcDescriptorRangeStarts, uint* pSrcDescriptorRangeSizes, D3D12_DESCRIPTOR_HEAP_TYPE DescriptorHeapsType);
        /*24*/ [PreserveSig] void CopyDescriptorsSimple(uint NumDescriptors, D3D12_CPU_DESCRIPTOR_HANDLE DestDescriptorRangeStart, D3D12_CPU_DESCRIPTOR_HANDLE SrcDescriptorRangeStart, D3D12_DESCRIPTOR_HEAP_TYPE DescriptorHeapsType);
        /*25*/ [PreserveSig] IntPtr GetResourceAllocationInfo(out D3D12_RESOURCE_ALLOCATION_INFO retVal, uint visibleMask, uint numResourceDescs, [In] ref D3D12_RESOURCE_DESC pResourceDescs);
        /*26*/ [PreserveSig] IntPtr GetCustomHeapProperties(out D3D12_HEAP_PROPERTIES retVal, uint nodeMask, D3D12_HEAP_TYPE heapType);
        /*27*/ [PreserveSig] int CreateCommittedResource([In] ref D3D12_HEAP_PROPERTIES pHeapProperties, D3D12_HEAP_FLAGS HeapFlags, [In] ref D3D12_RESOURCE_DESC pDesc, D3D12_RESOURCE_STATES InitialResourceState, D3D12_CLEAR_VALUE* pOptimizedClearValue, [In] ref Guid riidResource, [MarshalAs(UnmanagedType.IUnknown)] out object ppvResource);
        /*28*/ [PreserveSig] int CreateHeap([In] ref D3D12_HEAP_DESC pDesc, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvHeap);
        /*29*/ [PreserveSig] int CreatePlacedResource(ID3D12Heap pHeap, ulong HeapOffset, [In] ref D3D12_RESOURCE_DESC pDesc, D3D12_RESOURCE_STATES InitialState, D3D12_CLEAR_VALUE* pOptimizedClearValue, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvResource);
        /*30*/ [PreserveSig] int CreateReservedResource([In] ref D3D12_RESOURCE_DESC pDesc, D3D12_RESOURCE_STATES InitialState, D3D12_CLEAR_VALUE* pOptimizedClearValue, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvResource);
        /*31*/ [PreserveSig] int CreateSharedHandle(ID3D12DeviceChild pObject, IntPtr pAttributes, uint Access, [MarshalAs(UnmanagedType.LPWStr)] string Name, out IntPtr pHandle);
        /*32*/ [PreserveSig] int OpenSharedHandle(IntPtr NTHandle, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObj);
        /*33*/ [PreserveSig] int OpenSharedHandleByName([MarshalAs(UnmanagedType.LPWStr)] string Name, uint Access, out IntPtr pNTHandle);
        /*34*/ [PreserveSig] int MakeResident(uint NumObjects, [In] ref IntPtr ppObjects);
        /*35*/ [PreserveSig] int Evict(uint NumObjects, [In] ref IntPtr ppObjects);
        /*36*/ [PreserveSig] int CreateFence(ulong InitialValue, D3D12_FENCE_FLAGS Flags, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppFence);
        /*37*/ [PreserveSig] int GetDeviceRemovedReason();
        /*38*/ [PreserveSig] void GetCopyableFootprints([In] ref D3D12_RESOURCE_DESC pResourceDesc, uint FirstSubresource, uint NumSubresources, ulong BaseOffset, D3D12_PLACED_SUBRESOURCE_FOOTPRINT* pLayouts, uint* pNumRows, ulong* pRowSizeInBytes, ulong* pTotalBytes);
        /*39*/ [PreserveSig] int CreateQueryHeap([In] ref D3D12_QUERY_HEAP_DESC pDesc, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvHeap);
        /*40*/ [PreserveSig] int SetStablePowerState(int Enable);
        /*41*/ [PreserveSig] int CreateCommandSignature(IntPtr pDesc, ID3D12RootSignature pRootSignature, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvCommandSignature);
        /*42*/ [PreserveSig] void GetResourceTiling(ID3D12Resource pTiledResource, uint* pNumTilesForEntireResource, IntPtr pPackedMipDesc, IntPtr pStandardTileShapeForNonPackedMips, uint* pNumSubresourceTilings, uint FirstSubresourceTilingToGet, IntPtr pSubresourceTilingsForNonPackedMips);
        /*43*/ [PreserveSig] IntPtr GetAdapterLuid(out LUID retVal);
    }

    // ID3D12Debug : IUnknown (4 slots, d3d12sdklayers.h)
    [ComImport, Guid("344488b7-6846-474b-b989-f027448245e0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), SuppressUnmanagedCodeSecurity]
    public unsafe interface ID3D12Debug
    {
        /* 3*/ [PreserveSig] void EnableDebugLayer();
    }

    // ------------------------------------------------------------------------------------------------------------
    // Exports
    // ------------------------------------------------------------------------------------------------------------
    public static class D3D12Api
    {
        /// <summary>Native runtime (System32\d3d12.dll). pAdapter may be null (default adapter).
        /// riid = typeof(ID3D12Device).GUID; cast ppDevice to ID3D12Device.</summary>
        [DllImport("d3d12.dll", EntryPoint = "D3D12CreateDevice", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int D3D12CreateDevice([MarshalAs(UnmanagedType.IUnknown)] object pAdapter,
            D3D_FEATURE_LEVEL MinimumFeatureLevel, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppDevice);

        /// <summary>Streamline interposer (sl.interposer.dll next to the exe): returns the SL proxy device.</summary>
        [DllImport("sl.interposer.dll", EntryPoint = "D3D12CreateDevice", ExactSpelling = true)]
        public static extern int D3D12CreateDeviceSL([MarshalAs(UnmanagedType.IUnknown)] object pAdapter,
            D3D_FEATURE_LEVEL MinimumFeatureLevel, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppDevice);

        /// <summary>riid = typeof(ID3D12Debug).GUID. Fails (E_NOINTERFACE) if the Graphics Tools feature is not installed.</summary>
        [DllImport("d3d12.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int D3D12GetDebugInterface([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvDebug);

        /// <summary>Root signature 1.0. The desc's pParameters / pDescriptorRanges / pStaticSamplers must stay pinned
        /// during the call. ppErrorBlob is null on success.</summary>
        [DllImport("d3d12.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int D3D12SerializeRootSignature([In] ref D3D12_ROOT_SIGNATURE_DESC pRootSignature,
            D3D_ROOT_SIGNATURE_VERSION Version, out ID3DBlob ppBlob, out ID3DBlob ppErrorBlob);
    }
}
