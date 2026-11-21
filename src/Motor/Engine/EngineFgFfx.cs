using System;
using System.Globalization;
using System.Runtime.InteropServices;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFgFfx.cs - 21-11-2026 (el usuario: "integra esos reescalados y fg en el motor propio"): FSR 3 FG (AMD FidelityFX frame
    // generation, amd_fidelityfx_framegeneration_dx12.dll de magpie\) en el motor propio, para cualquier grafica (antes solo por mpv +
    // Magpie). fg=1 fg_tipo=fsr3.
    //
    // Como en las muestras de FidelityFX: la cadena de presentacion la crea FidelityFX (FRAMEGENERATIONSWAPCHAIN_FOR_HWND_DX12, una
    // IDXGISwapChain4 que intercala las imagenes generadas y lleva su ritmo), el contexto de generacion (FRAMEGENERATION) y en cada imagen:
    // configurarlo (ffxConfigureDescFrameGeneration, con la funcion que lanza la generacion) y preparar sus entradas en la lista del motor
    // (ffxDispatchDescFrameGenerationPrepare: los vectores y la profundidad del motor a la resolucion de la entrada). Sin Streamline (sus
    // ganchos de DXGI chocarian con la cadena de FidelityFX): sin Reflex ni DLSS con esta generacion (XeSS / FSR / FSR 1 como escalador si).
    //
    // Estructuras de GPUOpen FidelityFX-SDK (Kits/FidelityFX/framegeneration/include/ffx_framegeneration.h y dx12/ffx_api_framegeneration_dx12.h),
    // alineacion natural x64.
    [StructLayout(LayoutKind.Sequential)]
    struct FfxCreateContextDescFgSwapChainForHwndDX12
    {
        public FfxHeader header;
        public IntPtr swapchain;        // IDXGISwapChain4**
        public IntPtr hwnd;
        public IntPtr desc;             // DXGI_SWAP_CHAIN_DESC1*
        public IntPtr fullscreenDesc;   // DXGI_SWAP_CHAIN_FULLSCREEN_DESC* (null: ventana)
        public IntPtr dxgiFactory;
        public IntPtr gameQueue;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FfxCreateContextDescFrameGeneration
    {
        public FfxHeader header;
        public uint flags;
        public FfxDim2D displaySize, maxRenderSize;
        public uint backBufferFormat;
    }

    [StructLayout(LayoutKind.Sequential)] struct FfxRect2D { public int left, top, width, height; }

    [StructLayout(LayoutKind.Sequential)]
    struct FfxConfigureDescFrameGeneration
    {
        public FfxHeader header;
        public IntPtr swapChain;
        public IntPtr presentCallback, presentCallbackUserContext;
        public IntPtr frameGenerationCallback, frameGenerationCallbackUserContext;
        public byte frameGenerationEnabled, allowAsyncWorkloads;
        public FfxResource HUDLessColor;
        public uint flags;
        public byte onlyPresentGenerated;
        public FfxRect2D generationRect;
        public ulong frameID;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FfxDispatchDescFrameGenerationPrepare
    {
        public FfxHeader header;
        public ulong frameID;
        public uint flags;
        public IntPtr commandList;
        public FfxDim2D renderSize;
        public FfxFloat2 jitterOffset, motionVectorScale;
        public float frameTimeDelta;
        public byte unused_reset;
        public float cameraNear, cameraFar, cameraFovAngleVertical, viewSpaceToMetersFactor;
        public FfxResource depth, motionVectors;
    }

    partial class Settings
    {
        public string FgTipo = "";   // fg_tipo=fsr3: la generacion de FidelityFX en vez de DLSS-G (EngineFgFfx.cs)
    }

    unsafe partial class Engine
    {
        const ulong FFX_API_CREATE_CONTEXT_DESC_TYPE_FGSWAPCHAIN_FOR_HWND_DX12 = 0x00030006u;
        const ulong FFX_API_CREATE_CONTEXT_DESC_TYPE_FRAMEGENERATION = 0x00020001u;
        const ulong FFX_API_CONFIGURE_DESC_TYPE_FRAMEGENERATION = 0x00020002u;
        const ulong FFX_API_DISPATCH_DESC_TYPE_FRAMEGENERATION_PREPARE = 0x00020004u;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FfxFgDispatchCb(IntPtr dispatchParams, IntPtr userCtx);

        IntPtr ffxScBox, ffxFgBox;   // los contextos (cadena y generacion) en memoria fija
        bool ffxFgOn, ffxFgFailed;
        ulong ffxFgFrame;
        long ffxFgLastAt;
        FfxFgDispatchCb ffxFgCb;     // (se guarda: FidelityFX la llama desde su hilo)
        IntPtr ffxFgCbPtr;

        bool FgFfx { get { return s.Fg && s.FgTipo == "fsr3" && !ffxFgFailed; } }

        // InitDevice: la cadena de FidelityFX en vez de la de DXGI. false = no se pudo (la de siempre, sin esta generacion)
        bool FfxFgCreateSwapChain(ref DXGI_SWAP_CHAIN_DESC1 scd, out IDXGISwapChain1 sc1)
        {
            sc1 = null;
            if (!FfxLoad()) { ffxFgFailed = true; return false; }
            ffxScBox = Marshal.AllocHGlobal(8); Marshal.WriteIntPtr(ffxScBox, IntPtr.Zero);
            ffxFgBox = Marshal.AllocHGlobal(8); Marshal.WriteIntPtr(ffxFgBox, IntPtr.Zero);
            IntPtr outSwap = IntPtr.Zero;
            uint r;
            fixed (DXGI_SWAP_CHAIN_DESC1* pd = &scd)
            {
                FfxCreateContextDescFgSwapChainForHwndDX12 d = new FfxCreateContextDescFgSwapChainForHwndDX12();
                d.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_FGSWAPCHAIN_FOR_HWND_DX12;
                d.swapchain = (IntPtr)(&outSwap);
                d.hwnd = hwnd;
                d.desc = (IntPtr)pd;
                d.dxgiFactory = Com.BorrowPtr(factory, typeof(IDXGIFactory2));
                d.gameQueue = Com.BorrowPtr(queue, typeof(ID3D12CommandQueue));
                r = ffxCreate(ffxScBox, &d, IntPtr.Zero);
            }
            if (r != 0 || outSwap == IntPtr.Zero)
            {
                Log.Write("FSR 3 FG: la cadena de FidelityFX no se pudo crear (ffxCreateContext = " + r + "): la de siempre, sin esta generacion");
                ffxFgFailed = true;
                return false;
            }
            sc1 = (IDXGISwapChain1)Marshal.GetObjectForIUnknown(outSwap);
            Marshal.Release(outSwap);
            // El contexto de la generacion
            FfxCreateBackendDX12Desc be = new FfxCreateBackendDX12Desc();
            be.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_BACKEND_DX12;
            be.device = Com.BorrowPtr(dev, typeof(ID3D12Device));
            FfxCreateContextDescFrameGeneration fd = new FfxCreateContextDescFrameGeneration();
            fd.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_FRAMEGENERATION;
            fd.header.pNext = (IntPtr)(&be);
            fd.flags = 0;
            fd.displaySize.width = (uint)outW; fd.displaySize.height = (uint)outH;
            fd.maxRenderSize.width = (uint)outW; fd.maxRenderSize.height = (uint)outH;   // (la entrada cambia con la captura: como mucho la salida)
            fd.backBufferFormat = FFX_FMT_R8G8B8A8_UNORM;
            r = ffxCreate(ffxFgBox, &fd, IntPtr.Zero);
            if (r != 0)
            {
                Log.Write("FSR 3 FG: el contexto de generacion no se pudo crear (ffxCreateContext = " + r + "): la cadena de FidelityFX sin generar");
                return true;   // (la cadena ya es la de FidelityFX: sirve igual, sin generacion)
            }
            ffxFgCb = delegate (IntPtr p, IntPtr u) { return ffxDispatch(ffxFgBox, (void*)p); };
            ffxFgCbPtr = Marshal.GetFunctionPointerForDelegate(ffxFgCb);
            ffxFgOn = true;
            Log.Write("FSR 3 FG: cadena y generacion de FidelityFX listas (" + outW + "x" + outH + ")");
            return true;
        }

        // Cada imagen, antes de presentar: la configuracion (con la generacion encendida) y la preparacion de sus entradas en la lista.
        // Desde RenderFrame, antes de cerrar la lista (vectores y profundidad en NON_PIXEL_SHADER_RESOURCE)
        void FfxFgRecord()
        {
            if (!ffxFgOn) return;
            ffxFgFrame++;
            FfxConfigureDescFrameGeneration c = new FfxConfigureDescFrameGeneration();
            c.header.type = FFX_API_CONFIGURE_DESC_TYPE_FRAMEGENERATION;
            c.swapChain = Com.BorrowPtr(swap, typeof(IDXGISwapChain3));
            c.frameGenerationCallback = ffxFgCbPtr;
            c.frameGenerationCallbackUserContext = IntPtr.Zero;
            c.frameGenerationEnabled = (!inStandby && s.Fg) ? (byte)1 : (byte)0;   // (fg=0 en marcha: solo las reales)
            c.allowAsyncWorkloads = 0;
            c.generationRect.width = outW; c.generationRect.height = outH;
            c.frameID = ffxFgFrame;
            uint r = ffxConfigureFn != null ? ffxConfigureFn(ffxFgBox, &c) : 1;
            if (r != 0 && !ffxFgCfgSaid) { ffxFgCfgSaid = true; Log.Write("FSR 3 FG: ffxConfigure = " + r); }
            FfxDispatchDescFrameGenerationPrepare d = new FfxDispatchDescFrameGenerationPrepare();
            d.header.type = FFX_API_DISPATCH_DESC_TYPE_FRAMEGENERATION_PREPARE;
            d.frameID = ffxFgFrame;
            d.commandList = Com.BorrowPtr(list, typeof(ID3D12GraphicsCommandList));
            d.renderSize.width = (uint)inW; d.renderSize.height = (uint)inH;
            d.motionVectorScale.x = 1f; d.motionVectorScale.y = 1f;   // vectores en pixeles de la entrada
            double dt = ffxFgLastAt == 0 ? 16.7 : Clock.SinceMs(ffxFgLastAt);
            ffxFgLastAt = Clock.Now;
            d.frameTimeDelta = (float)Math.Max(1, Math.Min(100, dt));
            d.cameraNear = 0.1f; d.cameraFar = 1000f; d.cameraFovAngleVertical = 1.0471976f; d.viewSpaceToMetersFactor = 1f;
            d.depth = FfxRes(depth, FFX_FMT_R32_FLOAT, inW, inH, false);
            d.motionVectors = FfxRes(FfxFgVecNow && s.FgVec >= 2 && mvecFg != null ? mvecFg : FgMvec, FFX_FMT_R16G16_FLOAT, inW, inH, false);   // (22-11 1.2.1: los de la generacion, EngineFgGlobal.cs)
            r = ffxDispatch(ffxFgBox, &d);
            if (r != 0 && !ffxFgPrepSaid) { ffxFgPrepSaid = true; Log.Write("FSR 3 FG: preparacion = " + r); }
        }
        bool ffxFgCfgSaid, ffxFgPrepSaid;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FfxConfigureFn(IntPtr pContext, void* desc);
        FfxConfigureFn ffxConfigureFn;
    }
}
