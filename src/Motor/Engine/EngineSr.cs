using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineSr.cs - 21-11-2026 (el usuario: "integra esos reescalados y fg en el motor propio"): XeSS SR (Intel, libxess.dll) y FSR 3.1 / 4
    // (AMD FidelityFX API, amd_fidelityfx_loader_dx12.dll + amd_fidelityfx_upscaler_dx12.dll) en el motor propio, ademas de DLSS. Antes el
    // motor solo tenia DLSS, FSR 1 EASU y Catmull-Rom: con escalador=xess / fsr4 el lanzador mandaba la imagen a mpv + Magpie.
    //
    // Las DLL son las que trae Magpie (la carpeta magpie\ de la app, dlls_sr=, que pasa el lanzador; si no, junto al exe o ..\magpie):
    // no van en el repositorio (licencias de Intel y AMD). Se cargan con LoadLibraryEx por ruta y las funciones por GetProcAddress.
    //
    // Entradas (las mismas que DLSS SR): inColor (R8G8B8A8_UNORM, sRGB: color "LDR / no lineal"), los vectores del motor (R16G16_FLOAT, en
    // pixeles de la entrada, de la imagen de ahora a la anterior: escala 1,1, como el mvecScale 1/inW de Streamline) y la profundidad del
    // motor (R32_FLOAT, plana o por capas). Sin jitter (la consola no lo hace). Salida: outColor (UAV). Los estados: entradas en
    // NON_PIXEL_SHADER_RESOURCE, salida en UNORDERED_ACCESS (como en la evaluacion de DLSS SR). Las dos usan su propio descriptor heap y
    // ponen el suyo en la lista: el motor vuelve a poner el suyo en cada pasada (BindCompute), asi que no hace falta nada mas.
    //
    // Estructuras copiadas de las cabeceras publicas: intel/xess inc/xess/xess.h y xess_d3d12.h (pack 8); GPUOpen FidelityFX-SDK
    // Kits/FidelityFX/api/include/ffx_api.h, ffx_api_types.h, dx12/ffx_api_dx12.h y upscalers/include/ffx_upscale.h (alineacion natural x64).
    static class SrNative
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr LoadLibraryExW(string path, IntPtr h, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] public static extern IntPtr GetProcAddress(IntPtr m, string name);
        const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x8;

        // La DLL en la carpeta pedida, junto al exe o en ..\magpie (la de la app). where = la ruta cargada
        public static IntPtr Load(string dir, string name, out string where)
        {
            where = null;
            List<string> c = new List<string>();
            if (!string.IsNullOrEmpty(dir)) c.Add(Path.Combine(dir, name));
            c.Add(Path.Combine(Program.Dir, name));
            c.Add(Path.GetFullPath(Path.Combine(Program.Dir, @"..\magpie\" + name)));
            foreach (string p in c)
            {
                if (!File.Exists(p)) continue;
                IntPtr h = LoadLibraryExW(p, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
                if (h != IntPtr.Zero) { where = p; return h; }
                Log.Write("Escalado: " + p + " no se pudo cargar (error " + Marshal.GetLastWin32Error() + ")");
            }
            return IntPtr.Zero;
        }

        public static T Fn<T>(IntPtr lib, string name) where T : class
        {
            IntPtr p = lib == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(lib, name);
            if (p == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }
    }

    // ---------------------------------------------------------------- XeSS (xess.h / xess_d3d12.h, XESS_PACK_B = pack 8)
    [StructLayout(LayoutKind.Sequential)] struct Xess2D { public uint x, y; public Xess2D(uint a, uint b) { x = a; y = b; } }
    [StructLayout(LayoutKind.Sequential)] struct XessVersion { public ushort major, minor, patch, reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct XessD3D12InitParams
    {
        public Xess2D outputResolution;
        public int qualitySetting;
        public uint initFlags;
        public uint creationNodeMask;
        public uint visibleNodeMask;
        public IntPtr pTempBufferHeap;
        public ulong bufferHeapOffset;
        public IntPtr pTempTextureHeap;
        public ulong textureHeapOffset;
        public IntPtr pPipelineLibrary;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XessD3D12ExecuteParams
    {
        public IntPtr pColorTexture, pVelocityTexture, pDepthTexture, pExposureScaleTexture, pResponsivePixelMaskTexture, pOutputTexture;
        public float jitterOffsetX, jitterOffsetY, exposureScale;
        public uint resetHistory, inputWidth, inputHeight;
        public Xess2D inputColorBase, inputMotionVectorBase, inputDepthBase, inputResponsiveMaskBase, reserved0, outputColorBase;
        public IntPtr pDescriptorHeap;
        public uint descriptorHeapOffset;
    }

    // ---------------------------------------------------------------- FidelityFX API (ffx_api*.h, ffx_upscale.h)
    [StructLayout(LayoutKind.Sequential)] struct FfxHeader { public ulong type; public IntPtr pNext; }
    [StructLayout(LayoutKind.Sequential)] struct FfxDim2D { public uint width, height; }
    [StructLayout(LayoutKind.Sequential)] struct FfxFloat2 { public float x, y; }

    [StructLayout(LayoutKind.Sequential)]
    struct FfxResourceDesc { public uint type, format, width, height, depth, mipCount, flags, usage; }

    [StructLayout(LayoutKind.Sequential)]
    struct FfxResource { public IntPtr resource; public FfxResourceDesc description; public uint state; }

    [StructLayout(LayoutKind.Sequential)]
    struct FfxCreateBackendDX12Desc { public FfxHeader header; public IntPtr device; }

    [StructLayout(LayoutKind.Sequential)]
    struct FfxCreateContextDescUpscale
    {
        public FfxHeader header;
        public uint flags;
        public FfxDim2D maxRenderSize, maxUpscaleSize;
        public IntPtr fpMessage;
    }

    [StructLayout(LayoutKind.Sequential)] struct FfxOverrideVersion { public FfxHeader header; public ulong versionId; }

    [StructLayout(LayoutKind.Sequential)]
    struct FfxQueryDescGetVersions
    {
        public FfxHeader header;
        public ulong createDescType;
        public IntPtr device;
        public IntPtr outputCount;     // uint64_t*
        public IntPtr versionIds;      // uint64_t*
        public IntPtr versionNames;    // const char**
    }

    [StructLayout(LayoutKind.Sequential)] struct FfxQueryGetProviderVersion { public FfxHeader header; public ulong versionId; public IntPtr versionName; }

    [StructLayout(LayoutKind.Sequential)]
    struct FfxDispatchDescUpscale
    {
        public FfxHeader header;
        public IntPtr commandList;
        public FfxResource color, depth, motionVectors, exposure, reactive, transparencyAndComposition, output;
        public FfxFloat2 jitterOffset, motionVectorScale;
        public FfxDim2D renderSize, upscaleSize;
        public byte enableSharpening;
        public float sharpness, frameTimeDelta, preExposure;
        public byte reset;
        public float cameraNear, cameraFar, cameraFovAngleVertical, viewSpaceToMetersFactor;
        public uint flags;
    }

    unsafe partial class Engine
    {
        // ---- XeSS
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XessCreateFn(IntPtr device, out IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XessInitFn(IntPtr ctx, ref XessD3D12InitParams p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XessExecFn(IntPtr ctx, IntPtr cmdList, ref XessD3D12ExecuteParams p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XessDestroyFn(IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XessOptimalFn(IntPtr ctx, ref Xess2D outRes, int quality, out Xess2D opt, out Xess2D min, out Xess2D max);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XessVersionFn(out XessVersion v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XessScaleFn(IntPtr ctx, float x, float y);

        IntPtr xessLib, xessCtx;
        XessCreateFn xessCreate; XessInitFn xessInit; XessExecFn xessExec; XessDestroyFn xessDestroy; XessOptimalFn xessOptimal; XessScaleFn xessVelScale;
        bool xessTried, xessBroken;
        int xessIn = -1, xessInH = -1, xessOut = -1, xessOutH = -1, xessFails;
        string xessQualityName = "", xessNoKey = "";

        const int XESS_INIT_FLAG_LDR_INPUT_COLOR = 1 << 6;
        static readonly int[] XessQualities = { 100, 101, 102, 103, 104, 105, 106 };
        static readonly string[] XessQualityNames = { "ultra rendimiento", "rendimiento", "equilibrado", "calidad", "ultra calidad", "ultra calidad+", "AA nativo" };

        bool WantXess { get { return s.Upscaler == "xess"; } }
        bool WantFfx { get { return s.Upscaler == "fsr4" || s.Upscaler == "fsr3" || s.Upscaler == "ffx"; } }

        bool XessLoad()
        {
            if (xessTried) return xessLib != IntPtr.Zero && !xessBroken;
            xessTried = true;
            string where;
            xessLib = SrNative.Load(s.SrDlls, "libxess.dll", out where);
            if (xessLib == IntPtr.Zero) { Log.Write("XeSS: no esta libxess.dll (dlls_sr=" + s.SrDlls + ", junto al exe ni en ..\\magpie): sin XeSS"); return false; }
            xessCreate = SrNative.Fn<XessCreateFn>(xessLib, "xessD3D12CreateContext");
            xessInit = SrNative.Fn<XessInitFn>(xessLib, "xessD3D12Init");
            xessExec = SrNative.Fn<XessExecFn>(xessLib, "xessD3D12Execute");
            xessDestroy = SrNative.Fn<XessDestroyFn>(xessLib, "xessDestroyContext");
            xessOptimal = SrNative.Fn<XessOptimalFn>(xessLib, "xessGetOptimalInputResolution");
            xessVelScale = SrNative.Fn<XessScaleFn>(xessLib, "xessSetVelocityScale");
            XessVersionFn ver = SrNative.Fn<XessVersionFn>(xessLib, "xessGetVersion");
            if (xessCreate == null || xessInit == null || xessExec == null || xessDestroy == null)
            { Log.Write("XeSS: " + where + " sin las funciones de D3D12: sin XeSS"); xessBroken = true; return false; }
            string v = "?";
            XessVersion xv;
            if (ver != null && ver(out xv) == 0) v = xv.major + "." + xv.minor + "." + xv.patch;
            Log.Write("XeSS " + v + " cargado (" + where + ")");
            return true;
        }

        // El contexto para estos tamaños (se rehace al cambiar la entrada o la salida). false = no va (se escala con FSR 1)
        bool XessEnsure()
        {
            if (!XessLoad()) return false;
            string key = inW + "x" + inH + ">" + outW + "x" + outH;
            if (key == xessNoKey) return false;   // (este tamaño ya se vio que no: no se reintenta cada imagen)
            if (xessCtx != IntPtr.Zero && xessIn == inW && xessInH == inH && xessOut == outW && xessOutH == outH) return true;
            XessRelease();
            IntPtr devp = Com.BorrowPtr(dev, typeof(ID3D12Device));
            IntPtr ctx;
            int r = xessCreate(devp, out ctx);
            if (r < 0 || ctx == IntPtr.Zero) { Log.Write("XeSS: xessD3D12CreateContext = " + r + ": sin XeSS"); xessBroken = true; return false; }
            // La calidad cuyo margen de entrada (min..max) incluye la entrada de ahora, la de la optima mas cercana
            Xess2D o = new Xess2D((uint)outW, (uint)outH);
            int q = -1; double best = 1e9; string why = "";
            for (int i = 0; i < XessQualities.Length && xessOptimal != null; i++)
            {
                Xess2D opt, mn, mx;
                if (xessOptimal(ctx, ref o, XessQualities[i], out opt, out mn, out mx) < 0) continue;
                why += XessQualityNames[i] + " " + mn.x + "-" + mx.x + "; ";
                if (inW < mn.x || inW > mx.x || inH < mn.y || inH > mx.y) continue;
                double d = Math.Abs((double)opt.x - inW);
                if (d < best) { best = d; q = i; }
            }
            if (q < 0)
            {
                Log.Write("XeSS: ninguna calidad admite " + inW + "x" + inH + " -> " + outW + "x" + outH + " (" + why.TrimEnd(' ', ';') + "): sin XeSS para este tamaño");
                xessDestroy(ctx);
                xessNoKey = key;
                return false;
            }
            XessD3D12InitParams ip = new XessD3D12InitParams();
            ip.outputResolution = o;
            ip.qualitySetting = XessQualities[q];
            ip.initFlags = XESS_INIT_FLAG_LDR_INPUT_COLOR;   // color R8G8B8A8 en sRGB; vectores en pixeles de la entrada (por defecto)
            long t0 = Clock.Now;
            r = xessInit(ctx, ref ip);
            if (r < 0) { Log.Write("XeSS: xessD3D12Init = " + r + ": sin XeSS"); xessDestroy(ctx); xessBroken = true; return false; }
            if (xessVelScale != null) xessVelScale(ctx, 1f, 1f);
            xessCtx = ctx; xessIn = inW; xessInH = inH; xessOut = outW; xessOutH = outH; xessFails = 0;
            xessQualityName = XessQualityNames[q];
            Log.Write("XeSS listo: " + inW + "x" + inH + " -> " + outW + "x" + outH + ", calidad " + xessQualityName + " (" +
                      Clock.SinceMs(t0).ToString("0", CultureInfo.InvariantCulture) + " ms)" + (r > 0 ? " (aviso " + r + ")" : ""));
            return true;
        }

        void XessRelease()
        {
            if (xessCtx == IntPtr.Zero) return;
            WaitIdle();
            try { xessDestroy(xessCtx); } catch { }
            xessCtx = IntPtr.Zero; xessIn = xessInH = xessOut = xessOutH = -1;
        }

        // RenderFrame, paso 2 con xess: inColor -> outColor (outColor ya en UNORDERED_ACCESS)
        void RecordXess()
        {
            XessD3D12ExecuteParams p = new XessD3D12ExecuteParams();
            p.pColorTexture = Com.BorrowPtr(inColor, typeof(ID3D12Resource));
            p.pVelocityTexture = Com.BorrowPtr(SrMvec, typeof(ID3D12Resource));
            p.pDepthTexture = Com.BorrowPtr(depth, typeof(ID3D12Resource));
            p.pOutputTexture = Com.BorrowPtr(outColor, typeof(ID3D12Resource));
            p.exposureScale = 1f;
            p.resetHistory = frameReset ? 1u : 0u;
            p.inputWidth = (uint)inW; p.inputHeight = (uint)inH;
            int r = xessExec(xessCtx, Com.BorrowPtr(list, typeof(ID3D12GraphicsCommandList)), ref p);
            if (r < 0 && ++xessFails >= 8 && !xessBroken) { xessBroken = true; Log.Write("XeSS falla (" + r + "): se escala con FSR 1"); }
            else if (r >= 0) xessFails = 0;
        }

        // Los vectores para un escalador que no es DLSS (los del motor; los de la generacion pueden ir a cero: fg_vectores)
        ID3D12Resource SrMvec { get { return srVecValid && mvecSr != null ? mvecSr : mvec; } }

        // ---- FidelityFX (FSR 3.1 / FSR 4)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FfxCreateFn(IntPtr pContext, void* desc, IntPtr memCb);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FfxDestroyFn(IntPtr pContext, IntPtr memCb);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FfxQueryFn(IntPtr pContext, void* desc);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FfxDispatchFn(IntPtr pContext, void* desc);

        IntPtr ffxLib, ffxUpLib;
        IntPtr ffxCtxBox;   // ffxContext (void*) en memoria fija: las funciones piden su direccion
        FfxCreateFn ffxCreate; FfxDestroyFn ffxDestroy; FfxQueryFn ffxQuery; FfxDispatchFn ffxDispatch;
        bool ffxTried, ffxBroken, ffxHasCtx;
        int ffxIn = -1, ffxInH = -1, ffxOut = -1, ffxOutH = -1, ffxFails;
        string ffxWant = "", ffxName = "";
        long ffxLastAt;

        const ulong FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE = 0x00010000u;
        const ulong FFX_API_DISPATCH_DESC_TYPE_UPSCALE = 0x00010001u;
        const ulong FFX_API_CREATE_CONTEXT_DESC_TYPE_BACKEND_DX12 = 0x00000002u;
        const ulong FFX_API_QUERY_DESC_TYPE_GET_VERSIONS = 4u;
        const ulong FFX_API_DESC_TYPE_OVERRIDE_VERSION = 5u;
        const ulong FFX_API_QUERY_DESC_TYPE_GET_PROVIDER_VERSION = 6u;
        const uint FFX_FMT_R8G8B8A8_UNORM = 10, FFX_FMT_R16G16_FLOAT = 18, FFX_FMT_R32_FLOAT = 28, FFX_TEX2D = 2;
        const uint FFX_STATE_UAV = 1 << 1, FFX_STATE_COMPUTE_READ = 1 << 2, FFX_USAGE_UAV = 1 << 1;
        const uint FFX_UPSCALE_ENABLE_NON_LINEAR_COLORSPACE = 1 << 8, FFX_UPSCALE_FLAG_NON_LINEAR_COLOR_SRGB = 1 << 1;

        bool FfxLoad()
        {
            if (ffxTried) return ffxLib != IntPtr.Zero && !ffxBroken;
            ffxTried = true;
            string where;
            // El escalador primero, por su ruta: el cargador lo busca por nombre y, desde la carpeta del motor, no lo encontraria (sin el:
            // ffxCreateContext = 2, "tipo desconocido"). Luego el cargador (elige la version: FSR 4 en RX 9000, FSR 3.1 en las demas); si no
            // esta, la API del escalador directamente
            string whereUp, whereFg;
            IntPtr up = SrNative.Load(s.SrDlls, "amd_fidelityfx_upscaler_dx12.dll", out whereUp);
            SrNative.Load(s.SrDlls, "amd_fidelityfx_framegeneration_dx12.dll", out whereFg);   // (FSR 3 FG, EngineFgFfx.cs: el cargador la busca por nombre)
            ffxLib = SrNative.Load(s.SrDlls, "amd_fidelityfx_loader_dx12.dll", out where);
            if (ffxLib == IntPtr.Zero) { ffxLib = up; where = whereUp; }
            ffxUpLib = up;
            if (ffxLib == IntPtr.Zero) { Log.Write("FSR: no estan amd_fidelityfx_loader_dx12.dll ni amd_fidelityfx_upscaler_dx12.dll: sin FSR 3/4"); return false; }
            ffxCreate = SrNative.Fn<FfxCreateFn>(ffxLib, "ffxCreateContext");
            ffxDestroy = SrNative.Fn<FfxDestroyFn>(ffxLib, "ffxDestroyContext");
            ffxQuery = SrNative.Fn<FfxQueryFn>(ffxLib, "ffxQuery");
            ffxDispatch = SrNative.Fn<FfxDispatchFn>(ffxLib, "ffxDispatch");
            ffxConfigureFn = SrNative.Fn<FfxConfigureFn>(ffxLib, "ffxConfigure");
            if (ffxCreate == null || ffxDestroy == null || ffxDispatch == null) { Log.Write("FSR: " + where + " sin la API de FidelityFX"); ffxBroken = true; return false; }
            ffxCtxBox = Marshal.AllocHGlobal(8);
            Marshal.WriteIntPtr(ffxCtxBox, IntPtr.Zero);
            Log.Write("FidelityFX cargado (" + where + ")");
            return true;
        }

        // Las versiones del escalador que hay para esta grafica ("FSR 4.0.x", "FSR 3.1.x"...): id y nombre
        List<KeyValuePair<ulong, string>> FfxVersions(IntPtr devp)
        {
            List<KeyValuePair<ulong, string>> r = new List<KeyValuePair<ulong, string>>();
            if (ffxQuery == null) return r;
            ulong count = 0;
            FfxQueryDescGetVersions q = new FfxQueryDescGetVersions();
            q.header.type = FFX_API_QUERY_DESC_TYPE_GET_VERSIONS;
            q.createDescType = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
            q.device = devp;
            q.outputCount = (IntPtr)(&count);
            if (ffxQuery(IntPtr.Zero, &q) != 0 || count == 0 || count > 32) return r;
            ulong* ids = stackalloc ulong[(int)count];
            IntPtr* names = stackalloc IntPtr[(int)count];
            q.versionIds = (IntPtr)ids;
            q.versionNames = (IntPtr)names;
            if (ffxQuery(IntPtr.Zero, &q) != 0) return r;
            for (int i = 0; i < (int)count; i++)
                r.Add(new KeyValuePair<ulong, string>(ids[i], names[i] != IntPtr.Zero ? Marshal.PtrToStringAnsi(names[i]) : "?"));
            return r;
        }

        bool FfxEnsure()
        {
            if (!FfxLoad()) return false;
            if (ffxHasCtx && ffxIn == inW && ffxInH == inH && ffxOut == outW && ffxOutH == outH && ffxWant == s.Upscaler) return true;
            FfxRelease();
            IntPtr devp = Com.BorrowPtr(dev, typeof(ID3D12Device));
            List<KeyValuePair<ulong, string>> vers = FfxVersions(devp);
            string lista = "";
            foreach (KeyValuePair<ulong, string> kv in vers) lista += (lista.Length > 0 ? ", " : "") + kv.Value;
            // fsr4: la 4 si la hay (RX 9000; en las demas el cargador no la ofrece); fsr3: la 3.1 mas nueva; ffx: la que elija el cargador
            ulong over = 0; string overName = "";
            foreach (KeyValuePair<ulong, string> kv in vers)
            {
                string n = kv.Value ?? "";
                bool four = n.Contains(" 4.") || n.StartsWith("4.") || n.Contains("FSR4") || n.Contains("FSR 4");
                bool three = n.Contains(" 3.") || n.StartsWith("3.") || n.Contains("FSR3") || n.Contains("FSR 3");
                if (s.Upscaler == "fsr4" && four && over == 0) { over = kv.Key; overName = n; }
                if (s.Upscaler == "fsr3" && three && over == 0) { over = kv.Key; overName = n; }
            }
            FfxOverrideVersion ov = new FfxOverrideVersion();
            ov.header.type = FFX_API_DESC_TYPE_OVERRIDE_VERSION;
            ov.versionId = over;
            FfxCreateBackendDX12Desc be = new FfxCreateBackendDX12Desc();
            be.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_BACKEND_DX12;
            be.device = devp;
            be.header.pNext = over != 0 ? (IntPtr)(&ov) : IntPtr.Zero;
            FfxCreateContextDescUpscale cd = new FfxCreateContextDescUpscale();
            cd.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
            cd.header.pNext = (IntPtr)(&be);
            cd.flags = FFX_UPSCALE_ENABLE_NON_LINEAR_COLORSPACE;   // color sRGB de 8 bits (sin HDR); exposicion 1 (sin textura)
            cd.maxRenderSize.width = (uint)inW; cd.maxRenderSize.height = (uint)inH;
            cd.maxUpscaleSize.width = (uint)outW; cd.maxUpscaleSize.height = (uint)outH;
            Marshal.WriteIntPtr(ffxCtxBox, IntPtr.Zero);
            long t0 = Clock.Now;
            uint r = ffxCreate(ffxCtxBox, &cd, IntPtr.Zero);
            if (r != 0 && over != 0)
            {
                Log.Write("FSR: con " + overName + " ffxCreateContext = " + r + ": se prueba la que elija el cargador");
                be.header.pNext = IntPtr.Zero; overName = "";
                Marshal.WriteIntPtr(ffxCtxBox, IntPtr.Zero);
                r = ffxCreate(ffxCtxBox, &cd, IntPtr.Zero);
            }
            if (r != 0 && ffxUpLib != IntPtr.Zero && ffxUpLib != ffxLib)
            {
                Log.Write("FSR: con el cargador ffxCreateContext = " + r + ": se prueba la API de amd_fidelityfx_upscaler_dx12.dll directamente");
                ffxLib = ffxUpLib;
                ffxCreate = SrNative.Fn<FfxCreateFn>(ffxLib, "ffxCreateContext");
                ffxDestroy = SrNative.Fn<FfxDestroyFn>(ffxLib, "ffxDestroyContext");
                ffxQuery = SrNative.Fn<FfxQueryFn>(ffxLib, "ffxQuery");
                ffxDispatch = SrNative.Fn<FfxDispatchFn>(ffxLib, "ffxDispatch");
                be.header.pNext = IntPtr.Zero; overName = "";
                Marshal.WriteIntPtr(ffxCtxBox, IntPtr.Zero);
                r = ffxCreate(ffxCtxBox, &cd, IntPtr.Zero);
            }
            if (r != 0) { Log.Write("FSR: ffxCreateContext = " + r + " (versiones: " + (lista.Length > 0 ? lista : "ninguna") + "): sin FSR 3/4"); ffxBroken = true; return false; }
            ffxHasCtx = true; ffxIn = inW; ffxInH = inH; ffxOut = outW; ffxOutH = outH; ffxWant = s.Upscaler; ffxFails = 0;
            // La version que se usa de verdad
            ffxName = overName;
            if (ffxQuery != null)
            {
                FfxQueryGetProviderVersion pv = new FfxQueryGetProviderVersion();
                pv.header.type = FFX_API_QUERY_DESC_TYPE_GET_PROVIDER_VERSION;
                if (ffxQuery(ffxCtxBox, &pv) == 0 && pv.versionName != IntPtr.Zero) ffxName = Marshal.PtrToStringAnsi(pv.versionName);
            }
            Log.Write("FSR listo: " + (ffxName.Length > 0 ? ffxName : "?") + ", " + inW + "x" + inH + " -> " + outW + "x" + outH + " (" +
                      Clock.SinceMs(t0).ToString("0", CultureInfo.InvariantCulture) + " ms; pedido " + s.Upscaler + "; hay: " + (lista.Length > 0 ? lista : "?") + ")");
            if (s.Upscaler == "fsr4" && ffxName.IndexOf("4.", StringComparison.Ordinal) < 0)
                Log.Write("FSR 4 no esta para esta grafica (solo RX 9000 de serie): va " + (ffxName.Length > 0 ? ffxName : "la que hay"));
            return true;
        }

        void FfxRelease()
        {
            if (!ffxHasCtx) return;
            WaitIdle();
            try { ffxDestroy(ffxCtxBox, IntPtr.Zero); } catch { }
            ffxHasCtx = false; ffxIn = ffxInH = ffxOut = ffxOutH = -1;
        }

        static FfxResource FfxRes(ID3D12Resource r, uint fmt, int w, int h, bool uav)
        {
            FfxResource x = new FfxResource();
            if (r == null) return x;
            x.resource = Com.BorrowPtr(r, typeof(ID3D12Resource));
            x.description.type = FFX_TEX2D;
            x.description.format = fmt;
            x.description.width = (uint)w; x.description.height = (uint)h;
            x.description.depth = 1; x.description.mipCount = 1;
            x.description.usage = uav ? FFX_USAGE_UAV : 0;
            x.state = uav ? FFX_STATE_UAV : FFX_STATE_COMPUTE_READ;
            return x;
        }

        // RenderFrame, paso 2 con FSR 3/4: inColor -> outColor (outColor ya en UNORDERED_ACCESS)
        void RecordFfx()
        {
            FfxDispatchDescUpscale d = new FfxDispatchDescUpscale();
            d.header.type = FFX_API_DISPATCH_DESC_TYPE_UPSCALE;
            d.commandList = Com.BorrowPtr(list, typeof(ID3D12GraphicsCommandList));
            d.color = FfxRes(inColor, FFX_FMT_R8G8B8A8_UNORM, inW, inH, false);
            d.depth = FfxRes(depth, FFX_FMT_R32_FLOAT, inW, inH, false);
            d.motionVectors = FfxRes(SrMvec, FFX_FMT_R16G16_FLOAT, inW, inH, false);
            d.output = FfxRes(outColor, FFX_FMT_R8G8B8A8_UNORM, outW, outH, true);
            d.motionVectorScale.x = 1f; d.motionVectorScale.y = 1f;   // ya en pixeles de la entrada
            d.renderSize.width = (uint)inW; d.renderSize.height = (uint)inH;
            d.upscaleSize.width = (uint)outW; d.upscaleSize.height = (uint)outH;
            double dt = ffxLastAt == 0 ? 16.7 : Clock.SinceMs(ffxLastAt);
            ffxLastAt = Clock.Now;
            d.frameTimeDelta = (float)Math.Max(1, Math.Min(100, dt));
            d.preExposure = 1f;
            d.reset = frameReset ? (byte)1 : (byte)0;
            d.cameraNear = 0.1f; d.cameraFar = 1000f; d.cameraFovAngleVertical = 1.0471976f; d.viewSpaceToMetersFactor = 1f;
            d.flags = FFX_UPSCALE_FLAG_NON_LINEAR_COLOR_SRGB;
            uint r = ffxDispatch(ffxCtxBox, &d);
            if (r != 0 && ++ffxFails >= 8 && !ffxBroken) { ffxBroken = true; Log.Write("FSR falla (" + r + "): se escala con FSR 1"); }
            else if (r == 0) ffxFails = 0;
        }

        // Al cerrar el motor
        void SrShutdown()
        {
            try { XessRelease(); } catch { }
            try { FfxRelease(); } catch { }
        }
    }
}
