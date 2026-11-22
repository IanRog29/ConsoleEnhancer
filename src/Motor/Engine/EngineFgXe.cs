using System;
using System.Globalization;
using System.Runtime.InteropServices;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFgXe.cs - 22-11-2026 (1.2.4, agente MOTORFG). El usuario: "y no se podria pasar todo al motor para que funcione mejor?".
    // XeSS FG (Intel XeSS Frame Generation, libxess_fg.dll + XeLL libxell.dll de magpie\) en el motor propio: fg=1 fg_tipo=xess
    // fg_mult=2|3|4.
    //
    // Como FSR 3 FG (EngineFgFfx.cs): la cadena de presentacion la crea XeSS FG (xefgSwapChainD3D12InitFromSwapChainDesc: un proxy de
    // IDXGISwapChain que intercala las inventadas y lleva su ritmo) y el motor va sin Streamline (sus ganchos de DXGI chocarian con esa
    // cadena): sin DLSS SR ni DLSS-G ni Reflex con esta generacion; XeSS SR / FSR / FSR 1 como escalador y DLSS 5 (PrismaNr, NGX) si.
    // XeLL (la baja latencia de Intel, obligatoria para XeSS FG) hace de Reflex: xellSleep al empezar cada imagen y sus marcadores.
    // En cada imagen, en la lista del motor (antes de cerrarla): los vectores de la generacion (los mismos que DLSS-G / FSR 3 FG: mvecFg
    // de fg_vectores=2|3 o mvec, a la resolucion de la entrada, en pixeles), la profundidad (la de fg_profundidad si la hay), sus
    // constantes (sin camara: identidad; reset en los cortes) y el numero de la imagen; XeSS FG los copia (RV_ONLY_NOW).
    //
    // x3 / x4: en una grafica Intel XeSS FG los da (maxSupportedInterpolations); en las demas el runtime solo admite x2 (1 interpolada)
    // y el motor no lo cambia (la licencia de Intel no permite modificar la DLL ni su funcionamiento, tampoco en memoria): con
    // fg_mult=3|4 fuera de Intel se queda en x2 y lo dice (switch2-fgmult no) para que el lanzador decida.
    //
    // Estructuras de las cabeceras publicas de Intel (github.com/intel/xess, inc/xess_fg/xefg_swapchain.h, xefg_swapchain_d3d12.h,
    // inc/xell/xell.h, xell_d3d12.h; MIT), pack 8.
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    struct XefgInitParams
    {
        public IntPtr pApplicationSwapChain;
        public uint initFlags;
        public uint maxInterpolatedFrames;
        public uint creationNodeMask;
        public uint visibleNodeMask;
        public IntPtr pTempBufferHeap;
        public ulong bufferHeapOffset;
        public IntPtr pTempTextureHeap;
        public ulong textureHeapOffset;
        public IntPtr pPipelineLibrary;
        public int uiMode;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    struct XefgResourceData
    {
        public int type;          // xefg_swapchain_resource_type_t
        public int validity;      // xefg_swapchain_resource_validity_t
        public uint baseX, baseY;
        public uint sizeX, sizeY;
        public IntPtr pResource;
        public int incomingState; // D3D12_RESOURCE_STATES
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    unsafe struct XefgConstants
    {
        public fixed float viewMatrix[16];
        public fixed float projectionMatrix[16];
        public float jitterOffsetX, jitterOffsetY;
        public float motionVectorScaleX, motionVectorScaleY;
        public uint resetHistory;
        public float frameRenderTime;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    struct XefgProperties
    {
        public uint requiredDescriptorCount;
        public ulong tempBufferHeapSize;
        public ulong tempTextureHeapSize;
        public ulong constantBufferSize;
        public uint maxSupportedInterpolations;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    struct XefgPresentStatus { public uint framesPresented; public int frameGenResult; public uint isFrameGenEnabled; }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    struct XefgVersion { public ushort major, minor, patch, reserved; }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    struct XellSleepParams { public uint minimumIntervalUs; public uint bits; }   // bits: 1 = bLowLatencyMode, 2 = bLowLatencyBoost

    partial class Settings
    {
        public bool XeLowLatency = true;    // xefg_xell=0|1: XeLL en modo de baja latencia (como Reflex); 0 = sin dormir (solo sus marcadores)
        public bool XeHudless = false;      // xefg_hudless=1: 'final' (sin el aviso) como imagen sin interfaz para XeSS FG
        public double XeSceneThr = -1;      // xefg_corte=0..1: umbral de cambio de escena de XeSS FG (-1 = el suyo)
        public int XeTagMode = 0;           // xefg_modo=0..4 (pruebas): 0 entradas en la lista del motor (de serie), 1 sin lista hasta el Present, 2 sin entradas, 3 sin generar, 4 ni copiar a la cadena
        public bool XeRaw = true;           // xefg_directo=0|1: la cadena de XeSS FG por su puntero (sin QueryInterface)
        public bool XeHighPrio = false;     // xefg_prioridad=1: la cola de la grafica del motor en prioridad alta tambien con XeSS FG
        public bool XeNoXell = false;       // xefg_xell=2 (prueba): sin contexto de XeLL
        public int XeQueue = 0;             // xefg_cola=N: la cadena de XeSS FG con objeto de espera y como mucho N en cola; 0 = sin (de serie: medido 22-11, con 1 el motor se quedaba ~1 s parado en cada imagen)
        public int XeLogLevel = 2;          // xefg_log=0..3 (0 = todo lo de XeSS FG al log)
        public bool SlSoloDlss = true;      // fg_dlss_sr=0|1: con FSR 3 FG / XeSS FG y upscaler=dlss, DLSS SR por Streamline sin sus ganchos de DXGI (0 = sin Streamline, como la 1.2.3)
    }

    unsafe partial class Engine
    {
        const int XEFG_RES_HUDLESS = 0, XEFG_RES_DEPTH = 1, XEFG_RES_MV = 2, XEFG_RES_BACKBUFFER = 4;
        const int XEFG_RV_UNTIL_NEXT_PRESENT = 0, XEFG_RV_ONLY_NOW = 1;
        const int XEFG_UI_MODE_NONE = 1;
        const int XELL_SIMULATION_START = 0, XELL_SIMULATION_END = 1, XELL_RENDERSUBMIT_START = 2, XELL_RENDERSUBMIT_END = 3, XELL_PRESENT_START = 4, XELL_PRESENT_END = 5;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgCreateFn(IntPtr dev, out IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgGetPropsFn(IntPtr h, out XefgProperties p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgInitDescFn(IntPtr h, IntPtr hwnd, DXGI_SWAP_CHAIN_DESC1* desc, IntPtr fs, IntPtr queue, IntPtr factory, ref XefgInitParams p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgGetSwapFn(IntPtr h, ref Guid riid, out IntPtr sc);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgTagResFn(IntPtr h, IntPtr cl, uint presentId, ref XefgResourceData d);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgTagConstFn(IntPtr h, uint presentId, ref XefgConstants c);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgU32Fn(IntPtr h, uint v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgPtrFn(IntPtr h, IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgF32Fn(IntPtr h, float v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgHFn(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgStatusFn(IntPtr h, out XefgPresentStatus st);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgVersionFn(out XefgVersion v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void XefgLogCb(IntPtr msg, int level, IntPtr user);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XefgSetLogFn(IntPtr h, int level, IntPtr cb, IntPtr user);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XellCreateFn(IntPtr dev, out IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XellSleepModeFn(IntPtr h, ref XellSleepParams p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XellU32Fn(IntPtr h, uint frame);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XellMarkerFn(IntPtr h, uint frame, int marker);

        IntPtr xeFg, xeLl;
        IntPtr xeProxySc;            // la cadena de XeSS FG tal como la da GetSwapChainPtr (IDXGISwapChain3*)
        bool slSoloDlss;             // Streamline solo para DLSS SR (EngineUniversal.cs StartStreamline)
        bool xeFgOn, xeFgFailed, xeFgEnabledNow;
        int xeFgInterp = 1;          // interpoladas por real que hace (1 = x2)
        uint xeFgFrame;
        long xeFgLastAt;
        int xeFgErrs;
        XefgTagResFn xeTagRes; XefgTagConstFn xeTagConst; XefgU32Fn xeSetPresentId, xeSetEnabled, xeSetInterp; XefgStatusFn xeStatus;
        XefgF32Fn xeSceneThr; XefgHFn xeDestroy;
        XellU32Fn xlSleep; XellMarkerFn xlMarker; XellSleepModeFn xlSleepMode; XefgHFn xlDestroy;
        XefgLogCb xeLogCb;           // (se guarda: XeSS FG la llama desde sus hilos)
        int xeLogN;

        bool FgXe { get { return s.Fg && (s.FgTipo == "xess" || s.FgTipo == "xefg") && !xeFgFailed; } }
        // Las imagenes en la tele por cada real con XeSS FG puesta (para el ritmo y VRR)
        int XeFgMultNow { get { return xeFgOn && xeFgEnabledNow ? xeFgInterp + 1 : 1; } }
        // FSR 3 FG o XeSS FG (las dos sin Streamline, con los vectores de la generacion del motor)
        bool ExtFgOn { get { return ffxFgOn || (xeFgOn && xeFgEnabledNow); } }

        static void XeLogStatic(IntPtr msg, int level, IntPtr user) { }

        // InitDevice: la cadena de XeSS FG en vez de la de DXGI. false = no se pudo (la de siempre, sin esta generacion)
        bool XeFgCreateSwapChain(ref DXGI_SWAP_CHAIN_DESC1 scd, out IDXGISwapChain1 sc1)
        {
            sc1 = null;
            try
            {
                string wFg, wLl;
                IntPtr ll = SrNative.Load(s.SrDlls, "libxell.dll", out wLl);
                IntPtr fg = SrNative.Load(s.SrDlls, "libxess_fg.dll", out wFg);
                if (fg == IntPtr.Zero || ll == IntPtr.Zero) { Log.Write("XeSS FG: no estan libxess_fg.dll / libxell.dll (en " + s.SrDlls + "): sin esta generacion"); xeFgFailed = true; return false; }
                XefgCreateFn create = SrNative.Fn<XefgCreateFn>(fg, "xefgSwapChainD3D12CreateContext");
                XefgGetPropsFn props = SrNative.Fn<XefgGetPropsFn>(fg, "xefgSwapChainGetProperties");
                XefgInitDescFn init = SrNative.Fn<XefgInitDescFn>(fg, "xefgSwapChainD3D12InitFromSwapChainDesc");
                XefgGetSwapFn getSwap = SrNative.Fn<XefgGetSwapFn>(fg, "xefgSwapChainD3D12GetSwapChainPtr");
                XefgPtrFn setLl = SrNative.Fn<XefgPtrFn>(fg, "xefgSwapChainSetLatencyReduction");
                XefgSetLogFn setLog = SrNative.Fn<XefgSetLogFn>(fg, "xefgSwapChainSetLoggingCallback");
                XefgVersionFn ver = SrNative.Fn<XefgVersionFn>(fg, "xefgSwapChainGetVersion");
                xeTagRes = SrNative.Fn<XefgTagResFn>(fg, "xefgSwapChainD3D12TagFrameResource");
                xeTagConst = SrNative.Fn<XefgTagConstFn>(fg, "xefgSwapChainTagFrameConstants");
                xeSetPresentId = SrNative.Fn<XefgU32Fn>(fg, "xefgSwapChainSetPresentId");
                xeSetEnabled = SrNative.Fn<XefgU32Fn>(fg, "xefgSwapChainSetEnabled");
                xeSetInterp = SrNative.Fn<XefgU32Fn>(fg, "xefgSwapChainSetNumInterpolatedFrames");
                xeStatus = SrNative.Fn<XefgStatusFn>(fg, "xefgSwapChainGetLastPresentStatus");
                xeSceneThr = SrNative.Fn<XefgF32Fn>(fg, "xefgSwapChainSetSceneChangeThreshold");
                xeDestroy = SrNative.Fn<XefgHFn>(fg, "xefgSwapChainDestroy");
                XellCreateFn llCreate = SrNative.Fn<XellCreateFn>(ll, "xellD3D12CreateContext");
                xlSleepMode = SrNative.Fn<XellSleepModeFn>(ll, "xellSetSleepMode");
                xlSleep = SrNative.Fn<XellU32Fn>(ll, "xellSleep");
                xlMarker = SrNative.Fn<XellMarkerFn>(ll, "xellAddMarkerData");
                xlDestroy = SrNative.Fn<XefgHFn>(ll, "xellDestroyContext");
                if (create == null || init == null || getSwap == null || xeTagRes == null || xeTagConst == null || xeSetPresentId == null || xeSetEnabled == null ||
                    llCreate == null || xlSleep == null || xlMarker == null || setLl == null)
                { Log.Write("XeSS FG: " + wFg + " / " + wLl + " sin la API esperada: sin esta generacion"); xeFgFailed = true; return false; }
                XefgVersion v = new XefgVersion();
                if (ver != null) ver(out v);
                IntPtr devp = Com.BorrowPtr(dev, typeof(ID3D12Device));
                int r = s.XeNoXell ? 0 : llCreate(devp, out xeLl);
                if (!s.XeNoXell && (r != 0 || xeLl == IntPtr.Zero)) { Log.Write("XeSS FG: XeLL no se pudo crear (" + r + "): sin esta generacion"); xeFgFailed = true; return false; }
                XellSleepParams sp = new XellSleepParams(); sp.bits = s.XeLowLatency ? 1u : 0u;
                if (xlSleepMode != null && xeLl != IntPtr.Zero) { r = xlSleepMode(xeLl, ref sp); if (r != 0) Log.Write("XeSS FG: XeLL modo de baja latencia = " + r); }
                r = create(devp, out xeFg);
                if (r < 0 || xeFg == IntPtr.Zero) { Log.Write("XeSS FG: el contexto no se pudo crear (" + r + "): sin esta generacion"); XeFgFree(); xeFgFailed = true; return false; }
                if (setLog != null)
                {
                    xeLogCb = delegate (IntPtr msg, int level, IntPtr user)
                    {
                        if (xeLogN++ < 200) { try { Log.Write("XeSS FG (" + level + "): " + Marshal.PtrToStringAnsi(msg)); } catch { } }
                    };
                    setLog(xeFg, s.XeLogLevel, Marshal.GetFunctionPointerForDelegate(xeLogCb), IntPtr.Zero);   // (avisos y errores; xefg_log=0 todo)
                }
                r = xeLl == IntPtr.Zero ? 0 : setLl(xeFg, xeLl);
                if (r < 0) { Log.Write("XeSS FG: XeLL no se pudo enganchar (" + r + "): sin esta generacion"); XeFgFree(); xeFgFailed = true; return false; }
                uint maxI = 1;
                XefgProperties pr;
                if (props != null && props(xeFg, out pr) >= 0) maxI = Math.Max(1, pr.maxSupportedInterpolations);
                int want = Math.Max(1, Math.Min(3, s.FgMult - 1));
                xeFgInterp = (int)Math.Min((uint)want, maxI);
                XefgInitParams ip = new XefgInitParams();
                ip.maxInterpolatedFrames = (uint)xeFgInterp;
                ip.uiMode = XEFG_UI_MODE_NONE;
                ip.initFlags = 0;   // vectores a la resolucion de la entrada, en pixeles; profundidad normal (0 cerca)
                fixed (DXGI_SWAP_CHAIN_DESC1* pd = &scd)
                    r = init(xeFg, hwnd, pd, IntPtr.Zero, Com.BorrowPtr(queue, typeof(ID3D12CommandQueue)), Com.BorrowPtr(factory, typeof(IDXGIFactory2)), ref ip);
                if (r < 0) { Log.Write("XeSS FG: la cadena no se pudo crear (" + r + "): la de siempre, sin esta generacion"); XeFgFree(); xeFgFailed = true; return false; }
                // (la de XeSS FG pedida ya como IDXGISwapChain3, la que usa el motor: si el proxy reenviara QueryInterface a la cadena de
                // verdad, el motor presentaria por debajo de XeSS FG)
                Guid iid = typeof(IDXGISwapChain3).GUID;
                IntPtr sc;
                r = getSwap(xeFg, ref iid, out sc);
                if (r < 0 || sc == IntPtr.Zero) { Log.Write("XeSS FG: sin la cadena (" + r + ")"); XeFgFree(); xeFgFailed = true; return false; }
                object rcw = Marshal.GetObjectForIUnknown(sc);
                IntPtr q3 = IntPtr.Zero, unkA = IntPtr.Zero, unkB = IntPtr.Zero;
                try
                {
                    q3 = Marshal.GetComInterfaceForObject(rcw, typeof(IDXGISwapChain3));
                    Guid iu = new Guid("00000000-0000-0000-C000-000000000046");
                    Marshal.QueryInterface(sc, ref iu, out unkA);
                    Marshal.QueryInterface(q3, ref iu, out unkB);
                    Log.Write("XeSS FG: cadena " + sc.ToString("X") + ", por QueryInterface " + q3.ToString("X") + (q3 == sc ? " (la misma)" : " (OTRA)") + ", IUnknown " + (unkA == unkB ? "igual" : "distinto"));
                }
                catch (Exception ex) { Log.Write("XeSS FG: QueryInterface de la cadena: " + ex.Message); }
                if (q3 != IntPtr.Zero) Marshal.Release(q3);
                if (unkA != IntPtr.Zero) Marshal.Release(unkA);
                if (unkB != IntPtr.Zero) Marshal.Release(unkB);
                xeProxySc = sc;   // (su referencia se queda: Present va por aqui si la de QueryInterface es otra)
                sc1 = (IDXGISwapChain1)rcw;
                if (xeSetInterp != null) { r = xeSetInterp(xeFg, (uint)xeFgInterp); if (r < 0) Log.Write("XeSS FG: SetNumInterpolatedFrames(" + xeFgInterp + ") = " + r); }
                if (xeSceneThr != null && s.XeSceneThr >= 0) xeSceneThr(xeFg, (float)Math.Min(1, s.XeSceneThr));
                r = s.XeTagMode >= 3 ? -1 : xeSetEnabled(xeFg, 1);   // (xefg_modo=3: prueba, la cadena de XeSS FG sin generar)
                xeFgEnabledNow = r >= 0;
                xeFgOn = true;
                margenInventadas = xeFgInterp; margenFgTipo = "xess";   // (la reserva de EngineMargen.cs)
                Log.Write("XeSS FG " + v.major + "." + v.minor + "." + v.patch + " (" + wFg + "): cadena y generacion listas (" + outW + "x" + outH + "), x" + (xeFgInterp + 1) +
                          " (pedido x" + (want + 1) + ", la grafica admite x" + (maxI + 1) + "), XeLL " + (s.XeLowLatency ? "baja latencia" : "solo marcadores"));
                if (want > xeFgInterp)
                {
                    Log.Write("XeSS FG: x" + (want + 1) + " pedido, esta grafica solo da x" + (xeFgInterp + 1) + " con XeSS FG en el motor (x3 / x4 de XeSS FG fuera de Intel: no en el motor)");
                    try { Ev("switch2-fgmult", "no", (want + 1).ToString(CultureInfo.InvariantCulture), (xeFgInterp + 1).ToString(CultureInfo.InvariantCulture)); } catch { }
                }
                return true;
            }
            catch (Exception ex) { Log.Error("XeSS FG", ex); XeFgFree(); xeFgFailed = true; return false; }
        }

        void XeFgFree()
        {
            try { if (xeFg != IntPtr.Zero && xeDestroy != null) { if (xeSetEnabled != null) xeSetEnabled(xeFg, 0); xeDestroy(xeFg); } } catch { }
            try { if (xeLl != IntPtr.Zero && xlDestroy != null) xlDestroy(xeLl); } catch { }
            xeFg = xeLl = IntPtr.Zero;
            xeFgOn = false;
        }

        // Run, al empezar cada imagen (donde va el dormir de Reflex): el de XeLL
        void XeFgSleep()
        {
            if (!xeFgOn) return;
            xeFgFrame++;
            if (xeLl == IntPtr.Zero) return;
            if (s.XeLowLatency) { long tz = Clock.Now; xlSleep(xeLl, xeFgFrame); latSleepMs = Clock.SinceMs(tz); }
            XlMark( xeFgFrame, XELL_SIMULATION_START);
        }

        // Run, tras ConfigureFeatures: encender / apagar la generacion (fg=0 en marcha, espera)
        void XeFgEnable()
        {
            if (!xeFgOn) return;
            bool want = s.Fg && !inStandby && s.XeTagMode < 3;
            if (want == xeFgEnabledNow) return;
            int r = xeSetEnabled(xeFg, want ? 1u : 0u);
            Log.Write("XeSS FG " + (want ? "encendida" : "apagada") + (r < 0 ? " (" + r + ")" : ""));
            xeFgEnabledNow = want && r >= 0;
            if (want) xeFgLastAt = 0;
        }

        // RenderFrame, antes de cerrar la lista: las entradas de XeSS FG (las copia de la lista: RV_ONLY_NOW)
        void XeFgRecord()
        {
            if (!xeFgOn) return;
            XlMark( xeFgFrame, XELL_SIMULATION_END);
            XlMark( xeFgFrame, XELL_RENDERSUBMIT_START);
            if (!xeFgEnabledNow) { xeSetPresentId(xeFg, xeFgFrame); return; }
            // xefg_modo (prueba): 0 = en la lista del motor, copia ya (RV_ONLY_NOW); 1 = sin lista, valen hasta el Present; 2 = sin etiquetas
            int rv = s.XeTagMode == 1 ? XEFG_RV_UNTIL_NEXT_PRESENT : XEFG_RV_ONLY_NOW;
            IntPtr cl = s.XeTagMode == 1 ? IntPtr.Zero : Com.BorrowPtr(list, typeof(ID3D12CommandList));
            if (s.XeTagMode == 2) { xeSetPresentId(xeFg, xeFgFrame); return; }
            XefgResourceData mv = new XefgResourceData();
            mv.type = XEFG_RES_MV; mv.validity = rv;
            mv.sizeX = (uint)inW; mv.sizeY = (uint)inH;
            mv.pResource = Com.BorrowPtr(FfxFgVecNow && s.FgVec >= 2 && mvecFg != null ? mvecFg : FgMvec, typeof(ID3D12Resource));
            mv.incomingState = (int)D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE;
            int r = xeTagRes(xeFg, cl, xeFgFrame, ref mv);
            XefgResourceData dp = new XefgResourceData();
            dp.type = XEFG_RES_DEPTH; dp.validity = rv;
            dp.sizeX = (uint)inW; dp.sizeY = (uint)inH;
            dp.pResource = Com.BorrowPtr(s.FgDepth > 0 && depthFg != null && FfxFgVecNow ? depthFg : depth, typeof(ID3D12Resource));
            dp.incomingState = (int)D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE;
            if (r >= 0) r = xeTagRes(xeFg, cl, xeFgFrame, ref dp);
            if (r >= 0 && s.XeHudless)
            {
                XefgResourceData hl = new XefgResourceData();
                hl.type = XEFG_RES_HUDLESS; hl.validity = rv;
                hl.sizeX = (uint)outW; hl.sizeY = (uint)outH;
                hl.pResource = Com.BorrowPtr(final, typeof(ID3D12Resource));
                hl.incomingState = (int)D3D12_RESOURCE_STATES.COPY_SOURCE;
                r = xeTagRes(xeFg, cl, xeFgFrame, ref hl);
            }
            XefgConstants k = new XefgConstants();
            for (int i = 0; i < 4; i++) { k.viewMatrix[i * 5] = 1f; k.projectionMatrix[i * 5] = 1f; }
            k.motionVectorScaleX = 1f; k.motionVectorScaleY = 1f;
            double dt = xeFgLastAt == 0 ? 0 : Clock.SinceMs(xeFgLastAt);
            xeFgLastAt = Clock.Now;
            k.resetHistory = frameReset || dt == 0 || dt > 500 ? 1u : 0u;
            k.frameRenderTime = (float)Math.Max(0, Math.Min(500, dt));
            if (r >= 0) r = xeTagConst(xeFg, xeFgFrame, ref k);
            if (r >= 0) r = xeSetPresentId(xeFg, xeFgFrame);
            if (r < 0 && xeFgErrs++ < 5) Log.Write("XeSS FG: entradas de la imagen " + xeFgFrame + " = " + r);
        }

        // Run, alrededor del Present
        void XeFgBeforePresent()
        {
            if (!xeFgOn) return;
            XlMark( xeFgFrame, XELL_RENDERSUBMIT_END);
            XlMark( xeFgFrame, XELL_PRESENT_START);
        }
        // (1.2.4 ronda 5, XEFGM) Lo que dice XeSS FG de cada Present, contado: "presentadas 2, generacion 0" ERA BIEN (2 = la inventada y la
        // real; 0 = XEFG_SWAPCHAIN_RESULT_SUCCESS) y en la prueba del usuario se leyo como "no genera". Ahora cada 600 reales: cuantas
        // llevaron inventada, cuantas no y los resultados distintos de 0 (avisos > 0, errores < 0 de xefg_swapchain_result_t)
        int xeStN, xeStGen, xeStSolo, xeStMal, xeStUltimo;
        void XeFgAfterPresent()
        {
            if (!xeFgOn) return;
            XlMark( xeFgFrame, XELL_PRESENT_END);
            if (xeStatus == null) return;
            XefgPresentStatus st;
            if (xeStatus(xeFg, out st) < 0) return;
            xeStN++;
            if (st.framesPresented > 1) xeStGen++; else xeStSolo++;
            if (st.frameGenResult != 0) { xeStMal++; xeStUltimo = st.frameGenResult; }
            if ((xeFgFrame % 600) == 5 || xeStN >= 600)
            {
                Log.Write("XeSS FG: imagen " + xeFgFrame + ": de " + xeStN + " reales, " + xeStGen + " con su inventada (" + (xeFgInterp + 1) + " presentadas) y " + xeStSolo +
                          " solas" + (xeStMal > 0 ? ", " + xeStMal + " con resultado distinto de bien (ultimo " + xeStUltimo + ")" : ", resultado bien (0)") +
                          (st.isFrameGenEnabled != 0 ? "" : "; la generacion esta APAGADA"));
                xeStN = xeStGen = xeStSolo = xeStMal = 0;
            }
        }

        void XeFgBackbufLog()
        {
            try
            {
                for (int i = 0; i < BackBuffers; i++)
                {
                    D3D12_RESOURCE_DESC d; backbuf[i].GetDesc(out d);
                    Log.Write("XeSS FG: cadena " + i + ": " + d.Dimension + " " + d.Width + "x" + d.Height + " " + d.Format + " mips " + d.MipLevels + " muestras " + d.SampleDesc.Count + " flags " + d.Flags + " layout " + d.Layout);
                }
                if (final == null) return;
                D3D12_RESOURCE_DESC f; final.GetDesc(out f);
                Log.Write("XeSS FG: final: " + f.Dimension + " " + f.Width + "x" + f.Height + " " + f.Format + " flags " + f.Flags);
            }
            catch (Exception ex) { Log.Write("XeSS FG: cadena: " + ex.Message); }
        }

        // La cadena de XeSS FG por su puntero de GetSwapChainPtr, sin QueryInterface (xefg_directo=1, de serie): el proxy da otro puntero
        // por QueryInterface (medido 22-11: GetSwapChainPtr 1FF7DAC0, QueryInterface 692EAD40)
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ScPresentFn(IntPtr sc, uint sync, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ScGetBufferFn(IntPtr sc, uint i, ref Guid riid, out IntPtr res);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ScIndexFn(IntPtr sc);
        ScPresentFn scPresent; ScGetBufferFn scGetBuffer; ScIndexFn scIndex;
        static Delegate Slot(IntPtr com, int slot, Type t) { IntPtr vt = Marshal.ReadIntPtr(com); return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, slot * IntPtr.Size), t); }
        bool XeRawNow { get { return xeProxySc != IntPtr.Zero && s.XeRaw; } }
        object XeScBuffer(uint i)
        {
            if (scGetBuffer == null) scGetBuffer = (ScGetBufferFn)Slot(xeProxySc, 9, typeof(ScGetBufferFn));
            Guid rid = typeof(ID3D12Resource).GUID; IntPtr r;
            int hr = scGetBuffer(xeProxySc, i, ref rid, out r);
            if (hr < 0 || r == IntPtr.Zero) { Log.Write("XeSS FG: GetBuffer(" + i + ") = " + Hr.Format(hr)); return null; }
            object o = Marshal.GetObjectForIUnknown(r); Marshal.Release(r);
            return o;
        }
        uint ScIndex()
        {
            FphDrain();   // (1.2.4 ronda 5, XEFGM: el hilo de presentacion de la generacion propia, si va, acaba antes; EngineFgPropiaHilo.cs)
            if (!XeRawNow) return swap.GetCurrentBackBufferIndex();
            if (scIndex == null) scIndex = (ScIndexFn)Slot(xeProxySc, 36, typeof(ScIndexFn));
            return scIndex(xeProxySc);
        }
        int ScPresent(uint sync, uint flags)
        {
            FphDrain();   // (1.2.4 ronda 5, XEFGM: EngineFgPropiaHilo.cs)
            if (slProxyPresent != null) return slProxyPresent(slProxySc, sync, flags);   // (1.2.4 ronda 5, INTEGRA5: Streamline ve el Present, EngineSlProxy.cs)
            if (!XeRawNow) return swap.Present(sync, (DXGI_PRESENT)flags);
            if (scPresent == null) scPresent = (ScPresentFn)Slot(xeProxySc, 8, typeof(ScPresentFn));
            return scPresent(xeProxySc, sync, flags);
        }

        void XlMark(uint frame, int marker) { if (xeLl != IntPtr.Zero) xlMarker(xeLl, frame, marker); }

        // (1.2.4, integracion) XeSS FG pedido (fg=1 fg_tipo=xess) y no arrancado (sin las DLL, contexto o cadena fallidos): el motor va sin
        // generacion -sin Streamline o con el de solo DLSS SR, tampoco hay DLSS-G- y el lanzador creia XeSS FG x2 en el motor. Se le dice
        // una vez, como el x3 / x4 que no se puede (switch2-fgmult no 2 1: MotorFgEvento lo apunta y pasa la imagen a Magpie). Desde
        // EventTick (cada 0,5 s, con la tuberia del lanzador ya conectada; no en espera)
        bool xeFailSaid;
        void XeFgFailTick()
        {
            if (xeFailSaid || !xeFgFailed || mpvIpc == null || !(s.Fg && (s.FgTipo == "xess" || s.FgTipo == "xefg"))) return;
            if (mpvIpc.Event("switch2-fgmult", "no", "2", "1") > 0)
            {
                xeFailSaid = true;
                Log.Write("XeSS FG: no arranco en el motor: avisado el lanzador (switch2-fgmult no 2 1)");
            }
        }

        // PresentBlank (espera): la imagen negra sin generar
        void XeFgBlank()
        {
            if (!xeFgOn) return;
            if (xeFgEnabledNow) { xeSetEnabled(xeFg, 0); xeFgEnabledNow = false; }
            xeFgFrame++;
            xeSetPresentId(xeFg, xeFgFrame);
        }
    }
}
