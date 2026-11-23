using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // Motor: captura -> GPU -> DLSS SR (Streamline) -> CAS -> aviso -> pantalla, con Reflex; generacion: XeSS FG / FSR 3 FG (su propia
    // cadena) o Smooth Motion del driver (1.2.4 ronda 6, QUITAFG: sin DLSS-G)
    unsafe partial class Engine : IDisposable
    {
        const int FramesInFlight = 2, BackBuffers = 3, Slots = 3;
        const DXGI_FORMAT ColorFmt = DXGI_FORMAT.R8G8B8A8_UNORM;
        static readonly Guid IidResource = typeof(ID3D12Resource).GUID;

        readonly Settings s;
        readonly IntPtr hwnd;
        readonly int outW, outH;
        public int ExitCode;
        readonly Stats stats = new Stats();
        readonly Osd osd = new Osd();

        // ---- Streamline / D3D12
        bool slOn, dlssOk, reflexOk, pclOk;   // (1.2.4 ronda 6, QUITAFG: fuera fgOk, DLSS-G ya no se carga)
        IDXGIFactory2 factory;
        ID3D12Device dev;
        ID3D12CommandQueue queue;
        readonly ID3D12CommandAllocator[] alloc = new ID3D12CommandAllocator[FramesInFlight];
        readonly ulong[] allocFence = new ulong[FramesInFlight];
        ID3D12GraphicsCommandList list;
        ID3D12Fence fence;
        ulong fenceValue;
        readonly AutoResetEvent fenceEvent = new AutoResetEvent(false);
        IDXGISwapChain3 swap;
        readonly ID3D12Resource[] backbuf = new ID3D12Resource[BackBuffers];
        ID3D12RootSignature rootSig;
        ID3D12PipelineState psoUnpack, psoScale, psoCas, psoOsd, psoLuma, psoSearch, psoExpand, psoAa;
        ID3D12DescriptorHeap heap;
        IntPtr heapPtr;
        uint inc;
        D3D12_CPU_DESCRIPTOR_HANDLE cpu0;
        D3D12_GPU_DESCRIPTOR_HANDLE gpu0;
        ID3D12QueryHeap qheap;
        ID3D12Resource tsReadback;
        IntPtr tsPtr;
        double tsFreq;

        // Tablas de 4 descriptores [t0 t1 u0 u1]
        const int TUnpack = 0, TScale = 3, TCasOut = 4, TCasIn = 5, TOsd = 6, TLuma = 7, TSearch = 15, TExpand = 23, TAa = 24, TDown = TQBase + TQCount, TablesCount = TQBase + TQCount + 1 + TRCount + 14;   // (23-11 DLSSSR: + TSrVec x2 y TSrBlur x2, EngineSrVec.cs; ronda 7 SRBLUR: + TSrJit, EngineSrBanco.cs) (22-11 DLSS5Q: + TD5qMed, TD5qHold, EngineDlss5Q.cs) (20-11: + TLinIn, TLinOut, EngineLinear.cs) (16-11: TQ* = las de calidad, EngineMotion.cs; 17-11: TR* = las de las rejas, EngineRejas.cs; 20-11: + TNr, TNrDown, TNrPrep, TNrAf0/1, EngineDlss5.cs)
        const int U0 = Shaders.SlotU0;

        // ---- Recursos
        readonly ID3D12Resource[] upload = new ID3D12Resource[Slots];
        readonly IntPtr[] uploadPtr = new IntPtr[Slots];
        long slotBytes;
        int inW, inH;                                   // tamaño de inColor/depth/mvec
        ID3D12Resource rawColor, inColor, depth, mvec, outColor, final, composed, osdTex;
        ID3D12Resource rawSmall;    // 16-11 noche (entrada=): la captura reducida
        // Los vectores y la profundidad de la etiqueta de la imagen (DLSS SR). (1.2.4 ronda 6, QUITAFG: sin DLSS-G son los del motor; antes
        // DLSS-G podia llevar otros -fg_vectores=0 a cero, fg_borde, fg_vectores_completos / fg_relleno, fg_profundidad-: fuera mvecZero,
        // mvecGuard, EngineFgBorde.cs y EngineFgRelleno.cs. XeSS FG / FSR 3 FG cogen mvecFg y depthFg en su propio sitio, EngineFgXe.cs /
        // EngineFgFfx.cs)
        ID3D12Resource FgMvec { get { return mvec; } }
        // 17-11 15:20 (fg_profundidad=R): profundidad aproximada para la generacion (a <= R px de un borde fuerte, delante; lo liso, detras)
        ID3D12Resource depthFg;
        ID3D12Resource FgDepthTex { get { return depth; } }
        ID3D12Resource mvecFg, gvec;   // 17-11 13:40 (REJAS, fg_vectores=2|3): vectores solo para la generacion (suavizados o uno global) y el global (1x1)
        int capW, capH;             // tamaño de rawColor (la captura)
        readonly ID3D12Resource[] osdUpload = new ID3D12Resource[FramesInFlight];
        readonly IntPtr[] osdUploadPtr = new IntPtr[FramesInFlight];
        int osdPitch, osdW, osdH, osdSeen = -1;

        // ---- Ranuras de captura (hilo de captura <-> hilo de pintado)
        const int Free = 0, Writing = 1, Ready = 2, InFlight = 3;
        readonly object slotLock = new object();
        readonly int[] slotState = new int[Slots];
        readonly long[] slotSeq = new long[Slots], slotArrival = new long[Slots];
        readonly ulong[] slotFence = new ulong[Slots];
        readonly int[] slotFmt = new int[Slots], slotW = new int[Slots], slotH = new int[Slots], slotPitch = new int[Slots], slotUv = new int[Slots];
        readonly AutoResetEvent frameEvent = new AutoResetEvent(false);
        IFrameSource source;
        readonly object sourceLock = new object();
        long lastCapture;                                // ultima imagen recibida (para "sin señal")

        // Repetidas: huella de filas sueltas de la ultima imagen
        byte[] dupSig;
        int dupSigLen;

        // ---- Estado vivo
        readonly object pendingLock = new object();
        readonly List<KeyValuePair<string, string>> pending = new List<KeyValuePair<string, string>>();
        string dlssApplied = "";
        // (1.2.4 ronda 6, QUITAFG; el usuario: "como el dlss fg va fatal, directamente quitalo") DLSS-G puesto: ya nunca. Se queda como
        // propiedad siempre falsa para no tocar cada condicion de los vectores / latencia / ritmo (cambios pequeños frente a las otras ramas)
        bool fgApplied { get { return false; } }
        bool fgSinTipoSaid;   // fg=1 sin fg_tipo (o con uno que no es xess / fsr3): dicho una vez
        int reflexApplied = -1, reflexLimitApplied = 0;
        bool resetHistory = true;
        uint frameIndex;
        readonly SlTagSet tags = new SlTagSet(8);
        readonly SlViewportHandle vp = SlViewportHandle.Create(0);
        PipeServer pipe;
        string grabPath, grabMode;
        volatile bool grabWanted;
        long lastSecond = Clock.Now;
        string lastStatus = "";
        long benchStart;
        bool benchRecording;

        public Engine(Settings s, IntPtr hwnd, int outW, int outH)
        {
            this.s = s;
            this.hwnd = hwnd;
            this.outW = s.OutW > 0 ? s.OutW : outW;
            this.outH = s.OutH > 0 ? s.OutH : outH;
        }

        // =============================================================================================== arranque
        // ConsoleEnhancer: un aviso de Streamline que se repite (p. ej. "RSYNC: Inconsistency detected" en cada imagen con la sincronizacion
        // forzada por el perfil de Smooth Motion: miles de lineas por minuto) va al log las 5 primeras veces y luego 1 de cada 1000
        static readonly Dictionary<string, int> slRepes = new Dictionary<string, int>();
        static void SlLogLimitado(string m)
        {
            string k = m;
            int p = m.IndexOf("][", StringComparison.Ordinal);
            int q = m.LastIndexOf('[');
            if (q > 0 && q + 1 < m.Length) k = m.Substring(q);   // sin la hora ni el hilo: "[updateFromNVAPI] RSYNC..."
            int n;
            lock (slRepes)
            {
                slRepes.TryGetValue(k, out n);
                n++;
                if (slRepes.Count < 500 || slRepes.ContainsKey(k)) slRepes[k] = n;
            }
            if (n <= 5 || n % 1000 == 0) Log.Write(n > 5 ? m + " (x" + n + ")" : m);
        }

        void InitStreamline()
        {
            string dir = Program.Dir;
            if (!File.Exists(Path.Combine(dir, "sl.interposer.dll"))) { Log.Write("Sin sl.interposer.dll: sin DLSS ni generacion"); return; }
            Sl.LogSink = SlLogLimitado; // (ConsoleEnhancer: los avisos repetidos de Streamline, como RSYNC, sin llenar el log)
            Sl.ForwardSlInfo = false;
            SlInitSettings si = new SlInitSettings();
            si.ProjectId = "f0ab2f6e-7610-4001-afe3-ed00e8270716";
            si.EngineVersion = "1.0.0";
            si.LogDirectory = Program.DataDir;   // (18-11, MOTOR-CE: los logs de Streamline en datos=; antes la carpeta del exe)
            si.LogLevel = SlLogLevel.eDefault;
            // 22-11 (1.2.4 MOTORFG): con FSR 3 FG / XeSS FG (su propia cadena) y DLSS de escalador, Streamline solo para DLSS SR, sin sus
            // ganchos de DXGI (enganche manual: la fabrica, el dispositivo y la cadena son los del sistema)
            if (slSoloDlss) { si.Features = new SlFeature[] { SlFeature.DLSS }; si.Flags |= SlPreferenceFlags.eUseManualHooking; }
            long t = Clock.Now;
            DvcFeatures(si);   // 22-11 (1.2.4, DLSS5LEGAL: EngineDvc.cs): Vibrancia RTX y DLSS 5 oficial de Streamline, si estan junto al motor
            SlResult r = Sl.Init(si);
            Log.Write("slInit: " + r + " (" + Clock.SinceMs(t).ToString("0") + " ms)");
            slOn = r == SlResult.eOk;
        }

        void InitDevice()
        {
            object o;
            Guid iid = typeof(IDXGIFactory2).GUID;
            Hr.Check(slOn && !slSoloDlss ? DxgiApi.CreateDXGIFactory2SL(DXGI_CREATE_FACTORY.NONE, ref iid, out o)
                          : DxgiApi.CreateDXGIFactory2(DXGI_CREATE_FACTORY.NONE, ref iid, out o), "CreateDXGIFactory2");
            factory = (IDXGIFactory2)o;
            // 18-11 (MOTOR-CE): la de StartStreamline (adapter_luid= o la de alto rendimiento), por su LUID
            object adapter = ChooseAdapter(o);
            long luid = 0;
            dxgiAdapter = adapter;   // (16-11: para la memoria de la grafica del proceso al entrar en espera)
            if (adapter != null)
            {
                DXGI_ADAPTER_DESC1 ad;
                if (((IDXGIAdapter1)adapter).GetDesc1(out ad) >= 0)
                {
                    Log.Write("GPU: " + ad.Description + ", " + (ad.DedicatedVideoMemory.ToUInt64() >> 20) + " MB");
                    luid = ((long)ad.AdapterLuid.HighPart << 32) | (uint)ad.AdapterLuid.LowPart;
                }
            }
            iid = typeof(ID3D12Device).GUID;
            long t = Clock.Now;
            Hr.Check(slOn && !slSoloDlss ? D3D12Api.D3D12CreateDeviceSL(adapter, D3D_FEATURE_LEVEL.LEVEL_12_0, ref iid, out o)
                          : D3D12Api.D3D12CreateDevice(adapter, D3D_FEATURE_LEVEL.LEVEL_12_0, ref iid, out o), "D3D12CreateDevice");
            dev = (ID3D12Device)o;
            if (slOn)
            {
                SlResult r = Sl.SetD3DDevice(Com.BorrowPtr(dev, typeof(ID3D12Device)));
                bool l;
                dlssOk = Sl.IsFeatureSupported(SlFeature.DLSS, luid) == SlResult.eOk && Sl.IsFeatureLoaded(SlFeature.DLSS, out l) == SlResult.eOk && l;
                reflexOk = Sl.IsFeatureSupported(SlFeature.Reflex, luid) == SlResult.eOk && Sl.IsFeatureLoaded(SlFeature.Reflex, out l) == SlResult.eOk && l;
                pclOk = Sl.IsFeatureLoaded(SlFeature.PCL, out l) == SlResult.eOk && l;
                if (slSoloDlss) { reflexOk = pclOk = false; }   // (1.2.4 MOTORFG: solo DLSS SR; la generacion y la baja latencia son las de FSR 3 FG / XeSS FG)
                Log.Write("slSetD3DDevice: " + r + " (" + Clock.SinceMs(t).ToString("0") + " ms); DLSS " + dlssOk + ", Reflex " + reflexOk + ", PCL " + pclOk);
                DvcCheck(luid);   // 22-11 (1.2.4, DLSS5LEGAL: EngineDvc.cs)
            }
            D3D12_COMMAND_QUEUE_DESC qd = new D3D12_COMMAND_QUEUE_DESC();
            qd.Type = D3D12_COMMAND_LIST_TYPE.DIRECT;
            qd.Priority = EfgMainQueuePriority(FgXe && !s.XeHighPrio ? 0 : 100); // HIGH (1.2.4 ronda 6, ENHANCED: con la cola propia de Enhanced FG, normal) (1.2.4 MOTORFG: con XeSS FG la normal, como la que espera XeSS FG; xefg_prioridad=1 = alta)
            iid = typeof(ID3D12CommandQueue).GUID;
            Hr.Check(dev.CreateCommandQueue(ref qd, ref iid, out o), "CreateCommandQueue");
            queue = (ID3D12CommandQueue)o;
            ulong freq;
            Hr.Check(queue.GetTimestampFrequency(out freq), "GetTimestampFrequency");
            tsFreq = freq;
            iid = typeof(ID3D12CommandAllocator).GUID;
            for (int i = 0; i < FramesInFlight; i++)
            {
                Hr.Check(dev.CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.DIRECT, ref iid, out o), "CreateCommandAllocator");
                alloc[i] = (ID3D12CommandAllocator)o;
            }
            iid = typeof(ID3D12GraphicsCommandList).GUID;
            Hr.Check(dev.CreateCommandList(0, D3D12_COMMAND_LIST_TYPE.DIRECT, alloc[0], null, ref iid, out o), "CreateCommandList");
            list = (ID3D12GraphicsCommandList)o;
            Hr.Check(list.Close(), "Close");
            iid = typeof(ID3D12Fence).GUID;
            Hr.Check(dev.CreateFence(0, D3D12_FENCE_FLAGS.NONE, ref iid, out o), "CreateFence");
            fence = (ID3D12Fence)o;

            DXGI_SWAP_CHAIN_DESC1 scd = new DXGI_SWAP_CHAIN_DESC1();
            scd.Width = (uint)outW;
            scd.Height = (uint)outH;
            scd.Format = ColorFmt;
            scd.SampleDesc.Count = 1;
            scd.BufferUsage = DXGI_USAGE.RENDER_TARGET_OUTPUT;
            scd.BufferCount = BackBuffers;
            scd.Scaling = DXGI_SCALING.STRETCH;
            scd.SwapEffect = DXGI_SWAP_EFFECT.FLIP_DISCARD;
            scd.AlphaMode = DXGI_ALPHA_MODE.IGNORE;
            queueMax = QueueMaxNow();   // 18-11 (MOTOR-CE): cola_max=-1 de serie = 1 sin Reflex (EngineUniversal.cs)
            if (queueMax > 0) scd.Flags = DXGI_SWAP_CHAIN_FLAG.FRAME_LATENCY_WAITABLE_OBJECT;   // 18-11 (LATENCIA): cola_max=
            scd.Flags |= VrrSwapFlags();   // 20-11 (LATENCIA4, vrr=1: ALLOW_TEARING; EngineLatencia.cs)
            IDXGISwapChain1 sc1;
            // 21-11 (EngineFgFfx.cs): con fg_tipo=fsr3 la cadena la crea FidelityFX (intercala las imagenes generadas)
            // 22-11 (1.2.4 MOTORFG, EngineFgXe.cs): con fg_tipo=xess la crea XeSS FG
            if (!((FgFfx && FfxFgCreateSwapChain(ref scd, out sc1)) || (FgXe && XeFgCreateSwapChain(ref scd, out sc1))))
                Hr.Check(factory.CreateSwapChainForHwnd(s.Efg >= 2 ? EfgSwapQueue() : FmScQueueFor(), hwnd, ref scd, null, null, out sc1), "CreateSwapChainForHwnd");   // (23-11, FGMULTI: con FSR FG x3 / x4, su cola de presentacion, EngineFsrMulti.cs; ENHANCED: con efg, la cola propia de Enhanced FG, EngineFgEnhanced.cs)
            factory.MakeWindowAssociation(hwnd, DXGI_MWA.NO_ALT_ENTER | DXGI_MWA.NO_WINDOW_CHANGES);
            swap = (IDXGISwapChain3)sc1;
            LatQueueInit();
            for (uint i = 0; i < BackBuffers; i++)
            {
                Guid rid = IidResource;
                if (xeProxySc != IntPtr.Zero && s.XeRaw) { o = XeScBuffer(i); if (o == null) throw new Exception("GetBuffer (XeSS FG)"); }   // (1.2.4 MOTORFG)
                else Hr.Check(swap.GetBuffer(i, ref rid, out o), "GetBuffer");
                backbuf[i] = (ID3D12Resource)o;
            }
            Log.Write("Swapchain " + outW + "x" + outH + " flip x" + BackBuffers + (slOn ? " (Streamline)" : ""));
            if (xeFgOn) XeFgBackbufLog();   // (1.2.4 MOTORFG)
            SlProxyCreate();   // (1.2.4 ronda 5, INTEGRA5: con el enganche manual, el Present por el proxy de Streamline; EngineSlProxy.cs)
        }

        byte[] CompileCs(string name, string src)
        {
            string full = "#define RS \"" + Shaders.RootSig + "\"\n" + src.Replace("[numthreads", "[RootSignature(RS)]\n[numthreads");
            string cacheDir = Path.Combine(Program.DataDir, "cache");   // (18-11, MOTOR-CE: en datos=)
            string key = name + "_" + ((uint)full.GetHashCode()).ToString("x8") + ".cso";
            string path = Path.Combine(cacheDir, key);
            try { if (File.Exists(path)) return File.ReadAllBytes(path); } catch { }
            string msgs;
            byte[] b = D3DCompilerApi.Compile(full, "main", "cs_5_1", D3DCompilerApi.D3DCOMPILE_OPTIMIZATION_LEVEL3, out msgs, name + ".hlsl");
            if (msgs.Length > 0) Log.Write("Shader " + name + ": " + msgs);
            try { Directory.CreateDirectory(cacheDir); File.WriteAllBytes(path, b); } catch { }
            return b;
        }

        ID3D12PipelineState Pso(byte[] cs)
        {
            object o;
            fixed (byte* p = cs)
            {
                D3D12_COMPUTE_PIPELINE_STATE_DESC pd = new D3D12_COMPUTE_PIPELINE_STATE_DESC();
                pd.pRootSignature = Com.BorrowPtr(rootSig, typeof(ID3D12RootSignature));
                pd.CS.pShaderBytecode = (IntPtr)p;
                pd.CS.BytecodeLength = new UIntPtr((uint)cs.Length);
                Guid iid = typeof(ID3D12PipelineState).GUID;
                Hr.Check(dev.CreateComputePipelineState(ref pd, ref iid, out o), "CreateComputePipelineState");
            }
            return (ID3D12PipelineState)o;
        }

        void InitPipelines()
        {
            long t = Clock.Now;
            byte[] unpack = CompileCs("unpack", Shaders.Unpack), scale = CompileCs("scale", Shaders.Scale),
                   cas = CompileCs("cas", Shaders.Cas), os = CompileCs("osd", Shaders.Osd);
            object o;
            Guid iid = typeof(ID3D12RootSignature).GUID;
            fixed (byte* p = unpack)
                Hr.Check(dev.CreateRootSignature(0, (IntPtr)p, new UIntPtr((uint)unpack.Length), ref iid, out o), "CreateRootSignature");
            rootSig = (ID3D12RootSignature)o;
            psoUnpack = Pso(unpack); psoScale = Pso(scale); psoCas = Pso(cas); psoOsd = Pso(os);
            psoLuma = Pso(CompileCs("luma", Shaders.Luma)); psoSearch = Pso(CompileCs("search", Shaders.Search)); psoExpand = Pso(CompileCs("mvexpand", Shaders.MvExpand)); psoAa = Pso(CompileCs("aa", Shaders.Aa));
            psoEasu = Pso(CompileCs("easu", Shaders.Easu));   // 18-11 (MOTOR-CE): FSR 1

            D3D12_DESCRIPTOR_HEAP_DESC hd = new D3D12_DESCRIPTOR_HEAP_DESC();
            hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE.CBV_SRV_UAV;
            hd.NumDescriptors = (uint)(TablesCount * Shaders.TableSize);
            hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.SHADER_VISIBLE;
            iid = typeof(ID3D12DescriptorHeap).GUID;
            Hr.Check(dev.CreateDescriptorHeap(ref hd, ref iid, out o), "CreateDescriptorHeap");
            heap = (ID3D12DescriptorHeap)o;
            heapPtr = Com.BorrowPtr(heap, typeof(ID3D12DescriptorHeap));
            heap.GetCPUDescriptorHandleForHeapStart(out cpu0);
            heap.GetGPUDescriptorHandleForHeapStart(out gpu0);
            inc = dev.GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE.CBV_SRV_UAV);

            D3D12_QUERY_HEAP_DESC qhd = new D3D12_QUERY_HEAP_DESC();
            qhd.Type = D3D12_QUERY_HEAP_TYPE.TIMESTAMP;
            qhd.Count = FramesInFlight * 2;
            iid = typeof(ID3D12QueryHeap).GUID;
            Hr.Check(dev.CreateQueryHeap(ref qhd, ref iid, out o), "CreateQueryHeap");
            qheap = (ID3D12QueryHeap)o;
            tsReadback = MakeBuffer(D3D12_HEAP_TYPE.READBACK, 8 * FramesInFlight * 2, D3D12_RESOURCE_STATES.COPY_DEST, "tiempos");
            Hr.Check(tsReadback.Map(0, null, out tsPtr), "Map tiempos");
            Log.Write("Shaders y estados: " + Clock.SinceMs(t).ToString("0") + " ms");
            // 17-11 (rejas): los de calidad ya al arrancar (antes al entrar en el juego: si tardaban, la app creia la consola apagada)
            if (s.MvPx || s.MvMask > 0 || s.MvCapas) EnsureQualityPsos();
            if (s.MvCons > 0 || s.MvCapas || s.MvTruth > 0 || s.MvApert > 0 || s.MvEns > 0 || s.FgVec >= 2) EnsureRejasPsos();   // 17-11 (REJAS): tambien al arrancar
            if (s.Efg >= 2) EfgPsos();   // (1.2.4 ronda 6, ENHANCED: Enhanced FG, EngineFgEnhanced.cs)
        }

        ID3D12Resource MakeBuffer(D3D12_HEAP_TYPE type, long size, D3D12_RESOURCE_STATES state, string name)
        {
            D3D12_HEAP_PROPERTIES hp = new D3D12_HEAP_PROPERTIES(type);
            D3D12_RESOURCE_DESC rd = D3D12_RESOURCE_DESC.Buffer((ulong)size);
            object o;
            Guid iid = IidResource;
            Hr.Check(dev.CreateCommittedResource(ref hp, D3D12_HEAP_FLAGS.NONE, ref rd, state, null, ref iid, out o), "Buffer " + name);
            ID3D12Resource r = (ID3D12Resource)o;
            r.SetName(name);
            return r;
        }

        ID3D12Resource Tex(DXGI_FORMAT f, int w, int h, bool uav, D3D12_RESOURCE_STATES state, string name)
        {
            D3D12_HEAP_PROPERTIES hp = new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE.DEFAULT);
            D3D12_RESOURCE_DESC rd = D3D12_RESOURCE_DESC.Tex2D(f, (ulong)w, (uint)h, uav ? D3D12_RESOURCE_FLAGS.ALLOW_UNORDERED_ACCESS : D3D12_RESOURCE_FLAGS.NONE);
            object o;
            Guid iid = IidResource;
            Hr.Check(dev.CreateCommittedResource(ref hp, D3D12_HEAP_FLAGS.NONE, ref rd, state, null, ref iid, out o), "Textura " + name);
            ID3D12Resource r = (ID3D12Resource)o;
            r.SetName(name);
            return r;
        }

        D3D12_CPU_DESCRIPTOR_HANDLE Cpu(int table, int slot) { return cpu0.Offset(table * Shaders.TableSize + slot, inc); }
        D3D12_GPU_DESCRIPTOR_HANDLE Gpu(int table) { return gpu0.Offset(table * Shaders.TableSize, inc); }

        void SrvTex(int table, int slot, ID3D12Resource r, DXGI_FORMAT f)
        {
            D3D12_SHADER_RESOURCE_VIEW_DESC d = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(f);
            dev.CreateShaderResourceView(r, &d, Cpu(table, slot));
        }
        void UavTex(int table, int slot, ID3D12Resource r, DXGI_FORMAT f)
        {
            D3D12_UNORDERED_ACCESS_VIEW_DESC d = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(f);
            dev.CreateUnorderedAccessView(r, null, &d, Cpu(table, slot));
        }
        void SrvRaw(int table, int slot, ID3D12Resource r, long bytes)
        {
            D3D12_SHADER_RESOURCE_VIEW_DESC d = new D3D12_SHADER_RESOURCE_VIEW_DESC();
            d.Format = DXGI_FORMAT.R32_TYPELESS;
            d.ViewDimension = D3D12_SRV_DIMENSION.BUFFER;
            d.Shader4ComponentMapping = D3D12Const.DEFAULT_SHADER_4_COMPONENT_MAPPING;
            d.Buffer.FirstElement = 0;
            d.Buffer.NumElements = (uint)(bytes / 4);
            d.Buffer.Flags = D3D12_BUFFER_SRV_FLAGS.RAW;
            dev.CreateShaderResourceView(r, &d, Cpu(table, slot));
        }
        // Huecos de las tablas que el shader no usa: descriptores nulos validos
        void NullViews(int table)
        {
            D3D12_SHADER_RESOURCE_VIEW_DESC sd = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(ColorFmt);
            D3D12_UNORDERED_ACCESS_VIEW_DESC ud = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(ColorFmt);
            for (int i = 0; i < U0; i++) dev.CreateShaderResourceView(null, &sd, Cpu(table, i));
            for (int i = U0; i < Shaders.TableSize; i++) dev.CreateUnorderedAccessView(null, null, &ud, Cpu(table, i));
        }

        void InitFixedResources()
        {
            for (int t = 0; t < TablesCount; t++) NullViews(t);
            outColor = Tex(ColorFmt, outW, outH, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "salida DLSS");
            final = Tex(ColorFmt, outW, outH, true, D3D12_RESOURCE_STATES.COPY_SOURCE, "final");
            composed = Tex(ColorFmt, outW, outH, true, D3D12_RESOURCE_STATES.COPY_SOURCE, "con aviso");
            osdTex = Tex(DXGI_FORMAT.B8G8R8A8_UNORM, Osd.MaxW, Osd.MaxH, false, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "aviso");
            osdPitch = (Osd.MaxW * 4 + 255) & ~255;
            for (int i = 0; i < FramesInFlight; i++)
            {
                osdUpload[i] = MakeBuffer(D3D12_HEAP_TYPE.UPLOAD, (long)osdPitch * Osd.MaxH, D3D12_RESOURCE_STATES.GENERIC_READ, "aviso subida " + i);
                Hr.Check(osdUpload[i].Map(0, null, out osdUploadPtr[i]), "Map aviso");
            }
            // CAS: salida DLSS -> final; o la captura (1:1) -> final
            SrvTex(TCasOut, 0, outColor, ColorFmt); UavTex(TCasOut, U0, final, ColorFmt);
            UavTex(TCasIn, U0, final, ColorFmt);
            SrvTex(TOsd, 0, osdTex, DXGI_FORMAT.B8G8R8A8_UNORM); UavTex(TOsd, U0, composed, ColorFmt);
            UavTex(TScale, U0, outColor, ColorFmt);
        }

        // Lo que depende del tamaño de la captura
        void EnsureInputResources(int w, int h, int bytes)
        {
            if (bytes > slotBytes)
            {
                // Las ranuras solo crecen; se esperan las que esten en uso (GPU) y las que se estan copiando (hilo de captura)
                WaitIdle();
                lock (slotLock) reallocating = true;
                for (int spin = 0; spin < 500; spin++)
                {
                    bool writing = false;
                    lock (slotLock) for (int i = 0; i < Slots; i++) if (slotState[i] == Writing) writing = true;
                    if (!writing) break;
                    Thread.Sleep(1);
                }
                CapIdle();   // 20-11 (LATENCIA7): y sin bandas copiandose ni subiendo (EngineCopia.cs)
                lock (slotLock)
                {
                    long size = ((long)bytes + 65535) & ~65535L;
                    for (int i = 0; i < Slots; i++)
                    {
                        if (upload[i] != null) { upload[i].Unmap(0, null); Com.Release(ref upload[i]); }
                        upload[i] = MakeBuffer(D3D12_HEAP_TYPE.UPLOAD, size, D3D12_RESOURCE_STATES.GENERIC_READ, "captura " + i);
                        IntPtr p;
                        Hr.Check(upload[i].Map(0, null, out p), "Map captura");
                        uploadPtr[i] = p;
                        // (16-11: con la copia asincrona la descompresion lee su copia en la memoria de la grafica: EngineCopia.cs)
                        if (!SlotVram(i, size)) SrvRaw(TUnpack + i, 0, upload[i], size);
                        slotState[i] = Free;
                    }
                    slotBytes = size;
                    reallocating = false;
                }
            }
            // 16-11 noche (entrada=WxH): la captura reducida a WxH antes de todo lo demas (bordes, vectores, DLSS) si es mayor y no es la
            // de salida: DLSS 4.5 (M) a 60 con generacion no cabe con 1440p de entrada (51,6 reales) y si con 1080p (8,8 ms)
            int iw = w, ih = h;
            if (s.InW > 0 && s.InH > 0 && w > s.InW && h > s.InH && (s.EntradaPrueba || !(w >= s.OutW && h >= s.OutH))) { iw = s.InW; ih = s.InH; }
            if (w == capW && h == capH && iw == inW && ih == inH) return;
            WaitIdle();
            if (inColor != null)
            {
                // La generacion (su propia cola) puede seguir leyendo lo etiquetado hasta su Present: se suelta a los 3 s
                Bury(rawColor); Bury(inColor); Bury(depth); Bury(mvec); Bury(rawSmall);
                if (mvecFg != null) { Bury(mvecFg); mvecFg = null; }   // 17-11 13:40 (REJAS)
                if (depthFg != null) { Bury(depthFg); depthFg = null; }   // 17-11 15:20 (fg_profundidad)
                rawColor = inColor = depth = mvec = rawSmall = null;
            }
            rawColor = Tex(ColorFmt, w, h, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "captura RGBA");
            if (iw != w || ih != h) rawSmall = Tex(ColorFmt, iw, ih, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "captura reducida");
            inColor = Tex(ColorFmt, iw, ih, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "captura con bordes suavizados");
            depth = Tex(DXGI_FORMAT.R32_FLOAT, iw, ih, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "profundidad");   // 17-11 (REJAS): con UAV (profundidad por capas)
            mvec = Tex(DXGI_FORMAT.R16G16_FLOAT, iw, ih, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "movimiento");
            mvecFg = Tex(DXGI_FORMAT.R16G16_FLOAT, iw, ih, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "movimiento para la generacion");   // 17-11 13:40 (REJAS)
            depthFg = Tex(DXGI_FORMAT.R32_FLOAT, iw, ih, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "profundidad para la generacion");   // 17-11 15:20 (fg_profundidad)
            if (gvec == null) gvec = Tex(DXGI_FORMAT.R32G32_FLOAT, 1, 1, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "vector global");
            CreateMotionResources(iw, ih);
            for (int i = 0; i < Slots; i++) UavTex(TUnpack + i, U0, rawColor, ColorFmt);
            if (rawSmall != null) { SrvTex(TDown, 0, rawColor, ColorFmt); UavTex(TDown, U0, rawSmall, ColorFmt); }
            SrvTex(TAa, 0, rawSmall ?? rawColor, ColorFmt); UavTex(TAa, U0, inColor, ColorFmt);
            SrvTex(TScale, 0, inColor, ColorFmt);
            SrvTex(TCasIn, 0, inColor, ColorFmt);
            capW = w; capH = h;
            inW = iw; inH = ih;
            if (rawSmall != null) Log.Write("Entrada reducida: captura " + w + "x" + h + " -> " + iw + "x" + ih + " (entrada=)");
            dlssApplied = "";      // tamaño nuevo: DLSS se vuelve a configurar
            resetHistory = true;
            Log.Write("Entrada " + w + "x" + h);
        }

        void WaitIdle()
        {
            if (queue == null) return;
            FmhDrain();   // (23-11, FGMULTI: el hilo de presentacion de FSR FG x2 / x3 / x4 acaba lo suyo, EngineFsrMultiHilo.cs)
            FmScIdle();   // (y su cola de presentacion)
            EfgDrain();   // (1.2.4 ronda 6, ENHANCED: el hilo de presentacion de Enhanced FG acaba lo suyo, EngineFgEnhancedHilo.cs)
            fenceValue++;
            queue.Signal(fence, fenceValue);
            WaitFence(fenceValue);
        }

        void WaitFence(ulong v)
        {
            if (v == 0 || fence.GetCompletedValue() >= v) return;
            fence.SetEventOnCompletion(v, fenceEvent.SafeWaitHandle.DangerousGetHandle());
            if (!fenceEvent.WaitOne(3000)) Log.Write("La GPU no termina (valla " + v + ")");
        }

        // =============================================================================================== fuentes
        void StartSource()
        {
            lock (sourceLock)
            {
                if (source != null) { try { source.Dispose(); } catch (Exception ex) { Log.Error("Cerrando la fuente", ex); } source = null; }
                IFrameSource src;
                if (s.IsFileTest)
                {
                    // 22-11 (1.2.4 arreglos, RELEVO): test=archivo:<ruta>, un video como si fuera la capturadora (FileSource.cs)
                    FileSource fsrc = new FileSource(s);
                    try { fsrc.Frame += OnFrame; fsrc.Start(); }
                    catch (Exception ex) { fsrc.Frame -= OnFrame; Log.Error("Fuente de prueba (archivo)", ex); try { fsrc.Dispose(); } catch { } return; }
                    tirCapPrev = 0;
                    source = fsrc;
                    Log.Write("Fuente: " + fsrc.Name);
                    return;
                }
                if (s.Test.Length > 0)
                {
                    TestSource ts = new TestSource(s.Test, s.CapW, s.CapH, s.FpsNum, s.FpsDen, s.Mode == 30 ? 30 : 60);
                    ts.Scroll = s.TestScroll; ts.Hud = s.TestHud; ts.Overlay = s.TestOverlay; ts.ImageB = s.TestB; ts.Noise = s.TestNoise;
                    ts.Holds = s.TestHolds;
                    ts.ParRatio = s.TestPar; ts.ParSpeed = s.TestVel;   // 17-11 (REJAS): test=paralaje
                    ts.Lockstep = s.TestLockstep; ts.SeqStart = s.TestFrom;
                    ts.ZoomTexture = Path.IsPathRooted(s.TestTexture) ? s.TestTexture : Path.Combine(Program.Dir, s.TestTexture); ts.ZoomKind = s.TestZoomKind;   // (18-11: relativa al exe)
                    ts.Probe = s.Probe; ts.ProbeBand = s.ProbeBand;   // 18-11 (LATENCIA): sonda=
                    ts.Yuy2 = s.TestYuy2;   // 20-11 (LATENCIA7): test_yuy2=1 (la prueba en YUY2, como la PS5 a 4K)
                    Parallax.Fine = s.TestFine;
                    Parallax.Hud = s.TestHud && s.Test == "paralaje";   // 20-11 (FGFALLOS): interfaz quieta encima del paralaje
                    Finas.Hud = s.TestHud && Finas.KindOf(s.Test) >= 0; ts.Dir = s.TestDir;   // 22-11 (1.2.2, CALIDAD): test=finas / cabeza
                    src = ts;
                }
                else
                    src = CaptureSource.Open(s);
                if (src == null) { Log.Write("No hay fuente de imagen"); return; }
                tirCapPrev = 0;     // (16-11 tirones: el corte de abrir otra fuente no es un hueco de la capturadora)
                src.Frame += OnFrame;
                src.Start();
                source = src;
                Log.Write("Fuente: " + src.Name);
            }
        }

        bool DedupeNow { get { return s.Dedupe == 1 || (s.Dedupe < 0 && (s.Mode == 30 || s.Fg)); } }

        // ¿Igual que la anterior? Compara 1 de cada 8 filas (la captura HDMI es exacta: una imagen repetida es identica)
        bool IsDuplicate(byte* p, int pitch, int rowBytes, int h)
        {
            int rows = (h + 7) / 8;
            int need = rows * rowBytes;
            bool same = dupSig != null && dupSigLen == need;
            if (dupSig == null || dupSig.Length < need) { dupSig = new byte[need]; same = false; }
            dupSigLen = need;
            if (s.DupThreads && need >= (1 << 20)) return IsDuplicateParallel(p, pitch, rowBytes, rows, same);   // 20-11 (LATENCIA7): repetidas_hilos (EngineCopia.cs)
            fixed (byte* sig = dupSig)
            {
                byte* d = sig;
                for (int y = 0; y < h; y += 8)
                {
                    byte* row = p + (long)y * pitch;
                    if (same)
                    {
                        long* a = (long*)row, b = (long*)d;
                        int n = rowBytes / 8;
                        for (int i = 0; i < n; i++) if (a[i] != b[i]) { same = false; break; }
                    }
                    Buffer.MemoryCopy(row, d, rowBytes, rowBytes);
                    d += rowBytes;
                }
            }
            return same;
        }

        static int RowBytes(int fmt, int w) { return fmt == Shaders.FmtBgr24 ? w * 3 : fmt == Shaders.FmtBgrx32 ? w * 4 : fmt == Shaders.FmtYuy2 ? w * 2 : w; }

        void OnFrame(IntPtr data, int pitch, int w, int h, int fmt, long arrival, long seq)
        {
            MmcssCaptureThread();   // 21-11 (1.2.1, mmcss=1: EngineLatencia.cs)
            lastCapture = arrival;
            TirCapture(arrival);    // (16-11 tirones: huecos de la capturadora)
            int rowBytes = RowBytes(fmt, w);
            int dstPitch = (rowBytes + 3) & ~3;
            int rows = fmt == Shaders.FmtNv12 ? h + h / 2 : h;
            byte* src = (byte*)data;
            FrameHook(src, pitch, w, h, fmt, arrival);
            if (NoSignalCongela(src, pitch, w, h, fmt)) { stats.Captured(true); return; }   // (21-11: el corte corto sin la pantalla No Signal)
            if (grabWanted) Grab(src, pitch, w, h, fmt);
            // Como el filtro de mpv: sin las repetidas (una copia si llevan 2 sin pasar); en el modo 30, ademas, como mucho
            // 1 de cada 2 (una nueva si han pasado 2 desde la ultima que paso, o cualquiera a las 3)
            capN++;
            if (DedupeNow && fmt != Shaders.FmtNv12)
            {
                bool isNew = !IsDuplicate(src, pitch, rowBytes, h);
                long since = capN - lastPassed;
                bool pass = s.Mode == 30 ? (s.Dedupe30 >= 2 ? Pass30(isNew, since) : (isNew && since >= 2) || since >= 3) : isNew || since >= 2;
                if (!pass) { stats.Captured(true); return; }
                lastPassedNew = isNew;
                TirPass(since);     // (16-11 tirones: saltos de cadencia del juego)
            }
            else lastPassedNew = true;
            lastPassed = capN;
            passedCount++;
            bool cut = SceneCut(src, pitch, w, h, fmt);
            stats.Captured(false);
            long bytes = (long)dstPitch * rows;
            if (bytes > slotBytes) { pendingSize = (int)bytes; frameEvent.Set(); return; }   // el hilo de pintado agranda las ranuras
            int slot = -1;
            lock (slotLock)
            {
                ulong done = fence.GetCompletedValue();
                for (int i = 0; i < Slots; i++)
                    if (slotState[i] == InFlight && slotFence[i] <= done) slotState[i] = Free;
                for (int i = 0; i < Slots && slot < 0; i++) if (slotState[i] == Free) slot = i;
                if (slot < 0)
                {
                    // Una lista que nadie recogio: se pisa la mas vieja
                    long oldest = long.MaxValue;
                    for (int i = 0; i < Slots; i++) if (slotState[i] == Ready && slotSeq[i] < oldest) { oldest = slotSeq[i]; slot = i; }
                }
                if (slot < 0 || reallocating) { stats.Late(); return; }
                slotState[slot] = Writing;
            }
            long tCopy = Clock.Now;   // 20-11 (LATENCIA4): lo de antes de la copia y la copia, para la linea del banco
            if (CopyBands(slot, src, pitch, dstPitch, rowBytes, rows, w, h, fmt, seq, arrival, cut, tCopy)) return;   // 20-11 (LATENCIA7): copia_bandas (EngineCopia.cs)
            CopyRows(src, pitch, (byte*)uploadPtr[slot], dstPitch, rowBytes, rows);
            if (stats.Recording) LatOnFrame(Clock.Ms(arrival, tCopy), Clock.SinceMs(tCopy));
            lock (slotLock)
            {
                slotSeq[slot] = seq; slotArrival[slot] = arrival; slotFmt[slot] = fmt;
                slotW[slot] = w; slotH[slot] = h; slotPitch[slot] = dstPitch; slotUv[slot] = dstPitch * h; slotCut[slot] = cut;
                GiroSlot(slot);   // 20-11 (GIRO): el giro de la camara va con su ranura (EngineGiro.cs)
                slotState[slot] = Ready;
            }
            frameEvent.Set();
        }
        volatile int pendingSize;
        readonly bool[] slotCut = new bool[Slots];

        // Corte de escena (abrir un menu, un fundido, cambio de plano): la rejilla de luma de 32x18 cambia de golpe. DLSS y la generacion
        // empiezan entonces sin la historia de la imagen anterior (si no, mezclan el juego con el menu unas imagenes)
        float[] cutGrid = new float[32 * 18];
        bool cutValid;
        bool SceneCut(byte* src, int pitch, int w, int h, int fmt)
        {
            bool gc; if (GiroCut(src, pitch, w, h, fmt, out gc)) return gc;   // 20-11 (GIRO): corte_giro=1 / mvglobal / corte_prueba (EngineGiro.cs)
            if (fmt == Shaders.FmtNv12) return false;
            int bpp = fmt == Shaders.FmtBgr24 ? 3 : fmt == Shaders.FmtYuy2 ? 2 : 4;
            double diff = 0;
            // 17-11 (REJAS, corte_medias=1): la media de luma de cada casilla (8x8 puntos) en vez de un punto suelto por casilla. Con un
            // punto, un paneo rapido de algo de mucho contraste y repetido (los barrotes oscuros sobre el cielo de Ocarina, ~16 px por
            // imagen a 1080p) cambiaba casi todos los puntos (barrote <-> cielo) y se tomaba por un corte de escena EN CADA IMAGEN: DLSS y la
            // generacion sin historia, vectores a 0 (paralaje a doble velocidad: el 100 % de las imagenes). Un corte de verdad (menu, cambio
            // de plano) cambia tambien las medias
            int sub = s.CutMeans ? 8 : 1;
            for (int j = 0; j < 18; j++)
            {
                for (int i = 0; i < 32; i++)
                {
                    float l = 0;
                    for (int sj = 0; sj < sub; sj++)
                    {
                        byte* row = src + (long)((j * 2 * sub + 2 * sj + 1) * h / (36 * sub)) * pitch;
                        for (int si = 0; si < sub; si++)
                        {
                            byte* p = row + ((i * 2 * sub + 2 * si + 1) * w / (64 * sub)) * bpp;
                            l += fmt == Shaders.FmtYuy2 ? p[0] : (p[0] * 0.114f + p[1] * 0.587f + p[2] * 0.299f);
                        }
                    }
                    l /= sub * sub;
                    int k = j * 32 + i;
                    diff += Math.Abs(l - cutGrid[k]);
                    cutGrid[k] = l;
                }
            }
            bool cut = cutValid && diff / (32 * 18) > 28;
            cutValid = true;
            if (cut) { cutCount++; if (Clock.SinceMs(cutLogAt) > 5000) { Log.Write("Corte de escena (" + cutCount + " desde el anterior aviso; luma media cambia " + (diff / (32 * 18)).ToString("0") + ")"); cutLogAt = Clock.Now; cutCount = 0; } }
            return cut;
        }
        int cutCount; long cutLogAt;
        bool reallocating;     // (bajo slotLock) se estan agrandando las ranuras

        // Copia de la imagen a la memoria de subida (write-combined) repartida en varios nucleos: un hilo copia ~1,5 GB/s ahi y a 4K
        // (25 MB por imagen) no llegaba a tiempo (53 de 60 por segundo). Repartida, varias veces mas rapida y la imagen sale antes
        static readonly int CopyThreads = Math.Max(2, Math.Min(6, Environment.ProcessorCount / 2));
        public static int CopyThreadsSet = 0;   // 20-11 (LATENCIA4): copia_hilos=N (0 = lo de arriba)
        static void CopyRows(byte* src, int pitch, byte* dst, int dstPitch, int rowBytes, int rows)
        {
            long total = (long)rowBytes * rows;
            if (total < 2 * 1024 * 1024)
            {
                for (int y = 0; y < rows; y++) Buffer.MemoryCopy(src + (long)y * pitch, dst + (long)y * dstPitch, dstPitch, rowBytes);
                return;
            }
            long s = (long)src, d = (long)dst;
            int n = CopyThreadsSet > 0 ? CopyThreadsSet : CopyThreads;   // (20-11 LATENCIA4: copia_hilos=N)
            System.Threading.Tasks.Parallel.For(0, n, delegate (int k)
            {
                int y0 = rows * k / n, y1 = rows * (k + 1) / n;
                byte* ps = (byte*)s, pd = (byte*)d;
                if (pitch == dstPitch && pitch > 0)
                {
                    long len = (long)(y1 - y0) * pitch;
                    Buffer.MemoryCopy(ps + (long)y0 * pitch, pd + (long)y0 * dstPitch, len, len);
                }
                else
                    for (int y = y0; y < y1; y++) Buffer.MemoryCopy(ps + (long)y * pitch, pd + (long)y * dstPitch, dstPitch, rowBytes);
            });
        }

        // Imagen para el lanzador (lee el HOME): BGRA de 4 bytes, como el screenshot-raw de mpv; "light" = 1 de cada 2 px
        // En el hilo de captura solo se copian las filas que hacen falta (memcpy); convertir a BGR0 y escribir el fichero, en otro hilo
        // 16-11 (tirones): los dos bufferes de la captura se reutilizan (antes, ~5 MB nuevos por captura: con las del lanzador jugando,
        // cada 1,2 s, 6 recolecciones completas de memoria cada 20 s y alguna imagen 5 ms tarde). Si la anterior aun se esta escribiendo,
        // bufferes nuevos como antes
        byte[] grabRawBuf, grabOutBuf;
        int grabBusy;

        void Grab(byte* src, int pitch, int w, int h, int fmt)
        {
            grabWanted = false;
            string path = grabPath, mode = grabMode;
            bool reuse = Interlocked.CompareExchange(ref grabBusy, 1, 0) == 0;
            bool queued = false;
            try
            {
                int step = mode == "light" ? Math.Max(1, (int)Math.Round(w / 1280.0)) : 1;
                int ow = w / step, oh = h / step;
                int bpp = fmt == Shaders.FmtBgr24 ? 3 : fmt == Shaders.FmtYuy2 ? 2 : fmt == Shaders.FmtNv12 ? 1 : 4;
                int rowBytes = w * bpp;
                // (23-11, SINMPV: con NV12, detras de las filas de luma la fila de croma de cada una, para la captura en color: EngineGrabColor.cs)
                bool nv12 = fmt == Shaders.FmtNv12 && !GrabColor.Gray;
                long rawLen = (long)rowBytes * oh * (nv12 ? 2 : 1);
                byte[] raw = reuse && grabRawBuf != null && grabRawBuf.LongLength >= rawLen ? grabRawBuf : new byte[rawLen];
                if (reuse) grabRawBuf = raw;
                fixed (byte* d = raw)
                {
                    for (int y = 0; y < oh; y++) Buffer.MemoryCopy(src + (long)y * step * pitch, d + (long)y * rowBytes, rowBytes, rowBytes);
                    if (nv12)
                        for (int y = 0; y < oh; y++) Buffer.MemoryCopy(src + (long)h * pitch + (long)(y * step / 2) * pitch, d + (long)(oh + y) * rowBytes, rowBytes, rowBytes);
                }
                queued = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        int outLen = ow * oh * 4;
                        byte[] o = reuse && grabOutBuf != null && grabOutBuf.Length >= outLen ? grabOutBuf : new byte[outLen];
                        if (reuse) grabOutBuf = o;
                        // (23-11, SINMPV: en color como el screenshot-raw de mpv -YUY2 / NV12 a RGB BT.709, rango limitado-; antes en gris)
                        fixed (byte* d = o)
                        fixed (byte* r0 = raw)
                            GrabColor.Image(r0, rowBytes, nv12 ? fmt : (fmt == Shaders.FmtNv12 ? -1 : fmt), bpp, ow, oh, step, d);
                        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) fs.Write(o, 0, outLen);
                        lastGrab = string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} bgr0", path, ow, oh, ow * 4);
                        GrabDone(path, ow, oh);
                    }
                    catch (Exception ex) { lastGrab = ""; Log.Error("Captura para el lanzador", ex); }
                    finally { if (reuse) Interlocked.Exchange(ref grabBusy, 0); }
                    grabDone.Set();
                });
            }
            catch (Exception ex) { lastGrab = ""; Log.Error("Captura para el lanzador", ex); grabDone.Set(); }
            finally { if (reuse && !queued) Interlocked.Exchange(ref grabBusy, 0); }
        }
        volatile string lastGrab = "";
        readonly AutoResetEvent grabDone = new AutoResetEvent(false);

        // =============================================================================================== ajustes vivos
        void ApplyPending()
        {
            List<KeyValuePair<string, string>> p;
            lock (pendingLock) { if (pending.Count == 0) return; p = new List<KeyValuePair<string, string>>(pending); pending.Clear(); }
            bool restartSource = false;
            foreach (KeyValuePair<string, string> kv in p)
            {
                string k = kv.Key.ToLowerInvariant();
                int oldW = s.CapW, oldH = s.CapH, oldMode = s.Mode, oldFn = s.FpsNum, oldFd = s.FpsDen;
                string oldTest = s.Test, oldFmt = s.CapFormat;
                try { if (!s.Apply(kv.Key, kv.Value)) Log.Write("set: no existe " + kv.Key); }
                catch (Exception ex) { Log.Write("set " + kv.Key + ": " + ex.Message); }
                // 20-11 (HZ120): al cambiar la velocidad de captura (p. ej. 60 -> 120 para esports) hay que reabrir la fuente para
                // pedirle a la GC573 el nuevo intervalo de fotograma (IAMStreamConfig); antes solo se reabria por tamaño o formato
                if (s.CapW != oldW || s.CapH != oldH || s.Test != oldTest || s.CapFormat != oldFmt || s.FpsNum != oldFn || s.FpsDen != oldFd || (s.Test.Length > 0 && s.Mode != oldMode)) restartSource = true;
            }
            string cap;
            if (presetCap.TryGetValue(CapKey, out cap) && Rank(cap) < Rank(s.Preset))
            {
                Log.Write("DLSS " + s.Preset + " no cabia en este modo: se sigue con " + cap);
                s.Preset = cap;
            }
            AdaptaTrasAjustes(p);   // (ConsoleEnhancer: lo aprendido de este modo, EngineAdapta.cs)
            Log.Write("Ajustes: " + s.Describe());
            if (power != null) { power.HoldMs = s.NosigHoldMs; power.HoldLaunchMs = s.NosigHoldLaunchMs; power.BlackSecs = s.BlackSecs; }   // (16-11: espera de "No Signal"; 18-11: black_secs)
            if (restartSource)
            {
                resetHistory = true;
                // 16-11: en espera la capturadora la tiene mpv: no se abre aqui, se abre al despertar con estos ajustes
                if (inStandby) Log.Write("En espera: captura " + s.CapW + "x" + s.CapH + " guardada para al despertar (no se abre ahora)");
                else StartSource();
            }
        }

        void ConfigureFeatures()
        {
            // Reflex: el del ini (reflex=). (1.2.4 ronda 6, QUITAFG: antes forzado con DLSS-G -reflex_fg-, que lo exigia; DLSS-G ya no esta)
            int reflex = !reflexOk ? 0 : s.Reflex;
            // (18-11, MOTOR-CE: sin Reflex ninguna llamada a Streamline: con streamline=0 ni se carga sl.interposer.dll; antes aqui un
            // slReflexSetOptions(eOff) que la cargaba y daba eErrorNotInitialized)
            if (!reflexOk) { reflexApplied = 0; reflexLimitApplied = s.ReflexLimitUs; }
            else if (reflex != reflexApplied || s.ReflexLimitUs != reflexLimitApplied)
            {
                SlReflexOptions ro = SlReflexOptions.Create();
                ro.mode = reflex == 2 ? SlReflexMode.eLowLatencyWithBoost : reflex == 1 ? SlReflexMode.eLowLatency : SlReflexMode.eOff;
                ro.useMarkersToOptimize = 1;
                ro.frameLimitUs = (uint)Math.Max(0, s.ReflexLimitUs);   // 17-11 15:15 (reflex_limite): con FG a 120 Hz el motor solo pinta 58 de 60
                SlResult r = Sl.ReflexSetOptions(ref ro);
                Log.Write("Reflex " + ro.mode + (ro.frameLimitUs > 0 ? " (limite " + ro.frameLimitUs + " us)" : "") + ": " + r);
                reflexApplied = reflex;
                reflexLimitApplied = s.ReflexLimitUs;
            }
            string key = "dlss|" + s.Preset + "|" + inW + "x" + inH + (s.DlssLinear ? "|lineal" : "");   // (18-11, MOTOR-CE: upscaler=auto igual que dlss; 20-11: dlss_lineal, EngineLinear.cs)
            if (dlssOk && WantDlss && key != dlssApplied && (inW != outW || inH != outH))
            {
                SlDLSSOptions o = SlDLSSOptions.Create();
                double ratio = (double)outW / inW;
                o.mode = ratio < 1.25 ? SlDLSSMode.eDLAA : ratio < 1.6 ? SlDLSSMode.eMaxQuality : ratio < 1.85 ? SlDLSSMode.eBalanced : ratio < 2.5 ? SlDLSSMode.eMaxPerformance : SlDLSSMode.eUltraPerformance;
                o.outputWidth = (uint)outW; o.outputHeight = (uint)outH;
                o.colorBuffersHDR = s.DlssLinear ? SlBoolean.eTrue : SlBoolean.eFalse;   // (20-11: luz lineal FP16 = HDR + auto exposicion)
                o.useAutoExposure = s.DlssLinear ? SlBoolean.eTrue : SlBoolean.eFalse;
                o.preExposure = 1f;
                o.SetAllPresets(PresetOf(s.Preset));
                long t = Clock.Now;
                SlResult r = Sl.DLSSSetOptions(vp, ref o);
                dlssBroken = r != SlResult.eOk;
                evalFailsRow = 0;
                Log.Write("DLSS " + o.mode + " preset " + s.Preset + " " + inW + "x" + inH + " -> " + outW + "x" + outH + ": " + r + " (" + Clock.SinceMs(t).ToString("0") + " ms)");
                dlssApplied = key;
                presetSetAt = Clock.Now;   // 16-11 noche: AutoPresetTick no juzga los primeros 5 s (arranque de los modelos)
                resetHistory = true;
            }
            // (1.2.4 ronda 6, QUITAFG) la generacion: XeSS FG (fg_tipo=xess, EngineFgXe.cs) o FSR 3 FG (fg_tipo=fsr3, EngineFgFfx.cs), con su
            // propia cadena, o Smooth Motion del driver (sm=, fg=0). DLSS-G (fg=1 sin fg_tipo) ya no esta: sin generacion, dicho una vez
            if (s.Fg && !fgSinTipoSaid && s.FgTipo != "xess" && s.FgTipo != "xefg" && s.FgTipo != "fsr3")
            {
                fgSinTipoSaid = true;
                Log.Write("fg=1 sin fg_tipo" + (s.FgTipo.Length > 0 ? " (fg_tipo=" + s.FgTipo + ")" : "") + ": sin generacion (DLSS FG quitado en la 1.2.4)");
            }
        }

        static SlDLSSPreset PresetOf(string p)
        {
            switch (p)
            {
                case "E": return SlDLSSPreset.ePresetE;
                case "F": return SlDLSSPreset.ePresetF;
                case "J": return SlDLSSPreset.ePresetJ;
                case "K": return SlDLSSPreset.ePresetK;
                case "L": return SlDLSSPreset.ePresetL;
                case "M": return SlDLSSPreset.ePresetM;
                default: return SlDLSSPreset.eDefault;
            }
        }

        // =============================================================================================== pintado
        void Barrier(ID3D12Resource r, D3D12_RESOURCE_STATES a, D3D12_RESOURCE_STATES b)
        {
            D3D12_RESOURCE_BARRIER x = D3D12_RESOURCE_BARRIER.CreateTransition(r, a, b);
            list.ResourceBarrier(1, ref x);
        }
        void UavBarrier(ID3D12Resource r)
        {
            D3D12_RESOURCE_BARRIER x = D3D12_RESOURCE_BARRIER.CreateUav(r);
            list.ResourceBarrier(1, ref x);
        }

        void BindCompute(ID3D12PipelineState pso, int table, uint* c, uint n)
        {
            list.SetDescriptorHeaps(1, ref heapPtr);
            list.SetComputeRootSignature(rootSig);
            list.SetPipelineState(pso);
            list.SetComputeRootDescriptorTable(1, Gpu(table));
            list.SetComputeRoot32BitConstants(0, n, c, 0);
        }

        static uint F(float f) { return *(uint*)&f; }
        static uint Groups(int n, int g) { return (uint)((n + g - 1) / g); }

        // Una imagen: slot >= 0 = imagen nueva; -1 = repetir la ultima (aviso que se mueve, sin señal)
        void RenderFrame(int slot, IntPtr token, int fi)
        {
            uint* c = stackalloc uint[16];
            rfFi = fi;   // (22-11 DLSS5Q: la lista de esta imagen, para enviar la creacion de DLSS 5 a mitad: EngineDlss5Q.cs)
            Hr.Check(alloc[fi].Reset(), "Allocator.Reset");
            Hr.Check(list.Reset(alloc[fi], null), "List.Reset");
            list.EndQuery(qheap, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * 2));
            CpuBegin();   // 22-11 (1.2.4 LATENCIA, cpu_pasos=1: EngineEnvio.cs)
            PassBegin(slot >= 0);   // (16-11: tiempos por pasada, solo con stats=1 o en el banco)

            // Aviso nuevo -> textura
            byte[] opx; int ow, oh;
            if (osd.GetPixels(outH, ref osdSeen, out opx, out ow, out oh) && opx != null)
            {
                byte* dst = (byte*)osdUploadPtr[fi];
                fixed (byte* sp = opx)
                    for (int y = 0; y < oh; y++) Buffer.MemoryCopy(sp + y * ow * 4, dst + (long)y * osdPitch, osdPitch, ow * 4);
                osdW = ow; osdH = oh;
                Barrier(osdTex, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_DEST);
                D3D12_PLACED_SUBRESOURCE_FOOTPRINT fp = new D3D12_PLACED_SUBRESOURCE_FOOTPRINT();
                fp.Footprint.Format = DXGI_FORMAT.B8G8R8A8_UNORM;
                fp.Footprint.Width = (uint)ow; fp.Footprint.Height = (uint)oh; fp.Footprint.Depth = 1; fp.Footprint.RowPitch = (uint)osdPitch;
                D3D12_TEXTURE_COPY_LOCATION ds = D3D12_TEXTURE_COPY_LOCATION.FromSubresource(osdTex, 0);
                D3D12_TEXTURE_COPY_LOCATION ss = D3D12_TEXTURE_COPY_LOCATION.FromFootprint(osdUpload[fi], fp);
                list.CopyTextureRegion(ref ds, 0, 0, 0, ref ss, null);
                Barrier(osdTex, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            }

            // 1. Captura -> RGBA
            if (slot >= 0 && BandsGpu(slot, fi, c)) { }   // 20-11 (LATENCIA7, bandas_grafica): descompresion y bordes banda a banda (EngineCopia.cs)
            else if (slot >= 0)
            {
                Barrier(rawColor, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                c[0] = (uint)slotW[slot]; c[1] = (uint)slotH[slot]; c[2] = (uint)slotPitch[slot]; c[3] = (uint)slotFmt[slot];
                c[4] = 1; c[5] = 0; c[6] = (uint)slotUv[slot]; c[7] = 0;
                c[8] = (uint)s.Chroma; c[9] = c[10] = c[11] = 0;   // 21-11 (croma=)
                BindCompute(psoUnpack, TUnpack + slot, c, 12);
                list.Dispatch(Groups((slotW[slot] + 3) / 4, 16), Groups(slotH[slot], 8), 1);
                Barrier(rawColor, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                PassMark(1);
                DumpRaw();   // (16-11: test_volcado, prueba de la copia asincrona)
                // 1-. (entrada=) la captura reducida a la entrada de DLSS (Catmull-Rom)
                if (rawSmall != null)
                {
                    Barrier(rawSmall, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                    c[0] = (uint)inW; c[1] = (uint)inH; c[2] = 0; c[3] = 0;
                    c[4] = F(1f / capW); c[5] = F(1f / capH); c[6] = F(capW); c[7] = F(capH);
                    BindCompute(DownPso(), TDown, c, 8);   // (23-11 ronda 7, SRBLUR: entrada_filtro=1, EngineSrBanco.cs)
                    list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
                    Barrier(rawSmall, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                }
                // 1a. Bordes y tramado (el s2_aa.glsl de mpv); sin el, copia
                Barrier(inColor, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                c[0] = (uint)inW; c[1] = (uint)inH; c[2] = DeditherNow ? 1u : 0u; c[3] = AaNow ? 1u : 0u;   // (23-11 DLSSSR: con el escalador temporal sin DLSS 5, sr_liso / sr_aa / sr_tramado; EngineSrVec.cs)
                c[4] = (uint)Math.Max(0, Math.Min(80, (int)Math.Round(FlatNow * 10)));   // 16-11 noche: liso (Pad1.x)
                c[5] = 0; c[6] = 0;   // 20-11 (LATENCIA7): primera y ultima fila (Pad1.yz; 0 = la imagen entera)
                BindCompute(psoAa, TAa, c, 7);
                list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
                Barrier(inColor, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                PassMark(2);
            }
            // 1b. Vectores de movimiento (para DLSS SR y la generacion)
            if (slot >= 0) curSeq = slotSeq[slot];   // 17-11 (REJAS)
            GiroTake(slot);   // 20-11 (GIRO): el giro de la camara para la busqueda de vectores (EngineGiro.cs)
            CpuMark(0);
            RecordMotion(slot >= 0);
            CpuMark(1);
            D5qMeasure(slot >= 0, 0);   // 22-11 (1.2.4 DLSS5Q, dlss5_medida=: EngineDlss5Q.cs) el color antes de DLSS 5
            RecordDlss5(slot >= 0);   // 20-11 (DLSS 5 EN EL MOTOR, EngineDlss5.cs): DLSSNR sobre inColor antes de DLSS SR
            RecordDvc(slot >= 0, token);   // 22-11 (1.2.4, DLSS5LEGAL: EngineDvc.cs): Vibrancia RTX (oficial de NVIDIA) en el mismo sitio, si no va DLSS 5
            D5qMeasure(slot >= 0, 1);   // (DLSS5Q) y despues
            CpuMark(2);
            PassMark(3);

            // 2. Escalado: DLSS, FSR 1, Catmull-Rom o nada (captura ya a la resolucion de salida). 18-11 (MOTOR-CE): el de UpdateScaler
            bool native = inW == outW && inH == outH;
            bool useDlss = !native && dlssOk && scalerNow == "dlss" && !dlssBroken;
            if (useDlss)
            {
                tags.Clear();
                SlExtent inExt = new SlExtent(); inExt.width = (uint)inW; inExt.height = (uint)inH;
                SlExtent outExt = new SlExtent(); outExt.width = (uint)outW; outExt.height = (uint)outH;
                bool lin = s.DlssLinear && EnsureLinear();   // (20-11: dlss_lineal, EngineLinear.cs)
                if (lin) RecordToLinear();
                ID3D12Resource jin = lin ? null : SrJitterInput(c);   // (23-11 ronda 7, SRBLUR: sr_jitter, prueba)
                tags.Add(SlBufferType.ScalingInputColor, Com.BorrowPtr(lin ? inLinear : jin ?? inColor, typeof(ID3D12Resource)), (uint)D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, SlResourceLifecycle.eValidUntilEvaluate, inExt);
                tags.Add(SlBufferType.ScalingOutputColor, Com.BorrowPtr(lin ? outLinear : outColor, typeof(ID3D12Resource)), (uint)D3D12_RESOURCE_STATES.UNORDERED_ACCESS, SlResourceLifecycle.eValidUntilEvaluate, outExt);
                tags.Add(SlBufferType.Depth, Com.BorrowPtr(FgDepthTex, typeof(ID3D12Resource)), (uint)D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, SlResourceLifecycle.eValidUntilPresent, inExt);
                tags.Add(SlBufferType.MotionVectors, Com.BorrowPtr(FgMvec, typeof(ID3D12Resource)), (uint)D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, SlResourceLifecycle.eValidUntilPresent, inExt);
                AddQualityTags(inExt, outExt, true);   // 16-11 (calidad): mascara sin historia (DLSS SR)
                IntPtr cl = Com.BorrowPtr(list, typeof(ID3D12GraphicsCommandList));
                Sl.SetTagForFrame(token, vp, tags, cl);
                SlResult r = EvaluateDlss(token, cl);   // 16-11 (calidad): con mvmask_modo=1, sus propios vectores (etiqueta local)
                if (r != SlResult.eOk) { evalFails++; if (++evalFailsRow >= 8 && !dlssBroken) { dlssBroken = true; Log.Write("DLSS falla (" + r + "): se escala sin DLSS hasta el proximo cambio"); } }
                else evalFailsRow = 0;
                if (lin) RecordToSrgb();   // (20-11: la salida de DLSS en luz lineal -> outColor en sRGB)
                UavBarrier(outColor);
            }
            else if (!native && (scalerNow == "xess" || scalerNow == "ffx") && (scalerNow == "xess" ? !xessBroken && xessCtx != IntPtr.Zero : !ffxBroken && ffxHasCtx))
            {
                // 21-11 (EngineSr.cs): XeSS SR / FSR 3.1-4 en el motor
                if (scalerNow == "xess") RecordXess(); else RecordFfx();
                UavBarrier(outColor);
            }
            else if (!native)
            {
                if (scalerNow == "fsr1" || scalerNow == "xess" || scalerNow == "ffx") RecordEasu(c);   // 18-11 (MOTOR-CE): FSR 1 EASU (EngineUniversal.cs); 21-11: si XeSS / FSR fallan
                else
                {
                    c[0] = (uint)outW; c[1] = (uint)outH; c[2] = 0; c[3] = 0;
                    c[4] = F(1f / inW); c[5] = F(1f / inH); c[6] = F(inW); c[7] = F(inH);
                    BindCompute(psoScale, TScale, c, 8);
                    list.Dispatch(Groups(outW, 16), Groups(outH, 8), 1);
                }
                UavBarrier(outColor);
            }
            PassMark(4);
            CpuMark(3);

            // 3. CAS -> final (con nitidez 0, copia)
            if (!native) Barrier(outColor, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(final, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            c[0] = (uint)outW; c[1] = (uint)outH; c[2] = 0; c[3] = 0;
            c[4] = F((float)Math.Max(0, Math.Min(1, s.Sharp))); c[5] = F(s.Sharp <= 0.001 ? 1f : 0f); c[6] = F(noSignalScreen ? 1f : 0f); c[7] = F(RcasNow ? (s.Sharpen == "rcasl" ? 2f : 1f) : 0f);   // (18-11: c[7] = RCAS; 23-11 ronda 7: 2 = RCAS lineal, enfoque=rcasl)
            uint casN; ID3D12PipelineState casPso = CasPso(native, c, out casN);   // (23-11 ronda 7, SRBLUR: sr_nitidez_mov, EngineSrBlur.cs)
            BindCompute(casPso, native ? TCasIn : TCasOut, c, casN);
            list.Dispatch(Groups(outW, 16), Groups(outH, 8), 1);
            Barrier(final, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.COPY_SOURCE);
            if (!native) Barrier(outColor, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            RejasDump(slot);   // 17-11 (REJAS): volcado= (la salida y los vectores a ficheros, para medir)
            PassMark(5);

            // 4. Aviso encima (sobre una copia: 'final' queda sin aviso para la generacion)
            ID3D12Resource show = final;
            double slide;
            double op = osd.Opacity(out slide);
            if (op > 0.003 && osdW > 0)
            {
                Barrier(composed, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.COPY_DEST);
                list.CopyResource(composed, final);
                Barrier(composed, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                int ox = Math.Max(0, (outW - osdW) / 2), oy = noSignalScreen ? (outH - osdH) / 2 : Math.Max(0, (int)(outH * 0.035 + slide * outH / 1080.0));
                c[0] = (uint)ox; c[1] = (uint)oy; c[2] = (uint)Math.Min(osdW, outW - ox); c[3] = (uint)Math.Min(osdH, outH - oy);
                c[4] = F((float)op); c[5] = 0; c[6] = 0; c[7] = 0;
                BindCompute(psoOsd, TOsd, c, 8);
                list.Dispatch(Groups(osdW, 16), Groups(osdH, 8), 1);
                Barrier(composed, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.COPY_SOURCE);
                show = composed;
            }

            FfxFgRecord();   // 21-11 (EngineFgFfx.cs): FSR 3 FG, configurar y preparar sus entradas
            XeFgRecord();    // 22-11 (1.2.4 MOTORFG, EngineFgXe.cs): XeSS FG, sus entradas
            FmRecordPrepare(slot);   // 23-11 (1.2.4 ronda 6, FGMULTI, EngineFsrMulti.cs): FSR FG x3 / x4, lo que se prepara de cada real

            // 5. A la pantalla (1.2.4 ronda 6, QUITAFG: sin la generacion propia -EngineFgPropia.cs-, siempre aqui; FGMULTI: salvo FSR FG x2 / x3 / x4,
            // que la enseña su hilo de presentacion, EngineFsrMulti.cs; ENHANCED: y salvo Enhanced FG, que la copia a su ranura y la presenta su hilo)
            if (!EfgTakeReal(show, slot) && !FmTakeReal(show, slot))
            {
                uint bi = ScIndex();
                ID3D12Resource bb = backbuf[bi];
                if (!(xeFgOn && s.XeTagMode == 4))   // (xefg_modo=4: prueba, sin copiar a la cadena)
                {
                    Barrier(bb, D3D12_RESOURCE_STATES.PRESENT, D3D12_RESOURCE_STATES.COPY_DEST);
                    list.CopyResource(bb, show);
                    Barrier(bb, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.PRESENT);
                }
            }
            PassEnd();
            CpuMark(4);
            list.EndQuery(qheap, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * 2 + 1));
            list.ResolveQueryData(qheap, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * 2), 2, tsReadback, (ulong)(fi * 16));
            Hr.Check(list.Close(), "Close");
            IntPtr lp = Com.BorrowPtr(list, typeof(ID3D12CommandList));
            CopyWait();   // (16-11: la copia de la captura de esta imagen, en su cola, antes que esta lista)
            queue.ExecuteCommandLists(1, ref lp);
            CpuEnd();
        }
        int evalFails;

        // =============================================================================================== bucle
        public void Run()
        {
            // (16-11: en espera, Streamline siempre: los ajustes de la sesion llegan al despertar)
            // 18-11 (MOTOR-CE): la grafica y Streamline segun streamline=auto|0|1 (EngineUniversal.cs)
            StartStreamline();
            InitDevice();
            InitCopy();     // (16-11: cola de copia de la captura, EngineCopia.cs)
            InitPipelines();
            InitFixedResources();
            if (!s.Standby) EnsureInputResources(s.CapW, s.CapH, ((RowBytes(Shaders.FmtBgrx32, s.CapW) + 3) & ~3) * s.CapH);
            pipe = new PipeServer(s.Pipe, OnCommand);
            pipe.Start();
            StartMpvCompat();
            if (s.Standby) StandbyReady();   // sin captura ni ventana ni pintar hasta switch2-wake (EngineStandby.cs)
            else StartSource();
            if (s.BenchSecs > 0) benchStart = Clock.Now;
            if (!s.Standby) Log.Write("En marcha: " + s.Describe());

            long lastShown = 0;
            int fi = 0;
            IntPtr mmRender = MmcssOn("Games", "de pintado", true);   // 21-11 (1.2.1, mmcss=1: EngineLatencia.cs)
            while (!Program.Quit)
            {
                // 16-11: ordenes switch2-standby / switch2-wake y, en espera, el bucle de espera (sin pintar)
                if (inStandby || standbyPending > 0) { StandbyStep(ref lastShown, ref fi); continue; }
                // 16-11 (sin telon): switch2-reabrir, la capturadora otra vez ya (despierto mientras mpv la tenia y no pudo abrirla)
                if (reopenAsked)
                {
                    reopenAsked = false;
                    Log.Write("Captura: se vuelve a abrir ya (switch2-reabrir; " + (signalSeen ? "ya llegaban imagenes" : "sin imagen aun") + ")");
                    StartSource();
                }
                ApplyPending();
                if (pendingSize > slotBytes) { EnsureInputResources(capW, capH, pendingSize); pendingSize = 0; }
                frameIndex++;
                IntPtr token = IntPtr.Zero;
                if (slOn) Sl.GetNewFrameToken(frameIndex, out token);
                LatQueueWait();   // 18-11 (LATENCIA, cola_max=): la swapchain con espera
                // 18-11 (LATENCIA, reflex_dormir=0): sin dormir de Reflex (1.2.4 ronda 6: antes siempre con DLSS-G, que lo necesitaba)
                latSleepMs = 0;
                if (reflexOk && token != IntPtr.Zero && s.ReflexSleep) { long tz = Clock.Now; Sl.ReflexSleep(token); latSleepMs = Clock.SinceMs(tz); }
                XeFgSleep();   // 22-11 (1.2.4 MOTORFG, EngineFgXe.cs): XeLL en vez de Reflex con XeSS FG

                // La imagen mas nueva (las anteriores que no se pintaron se sueltan)
                int slot = -1;
                bool repaint = false;
                while (!Program.Quit)
                {
                    lock (slotLock)
                    {
                        long best = -1;
                        for (int i = 0; i < Slots; i++) if (slotState[i] == Ready && slotSeq[i] > best) { best = slotSeq[i]; slot = i; }
                        if (slot >= 0)
                        {
                            if (slotCut[slot]) resetHistory = true;
                            for (int i = 0; i < Slots; i++) if (i != slot && slotState[i] == Ready) { slotState[i] = Free; stats.Late(); }
                            slotState[slot] = InFlight;
                            TestSource.Consumed.Set();   // 17-11 (REJAS): test_paso=1, la fuente da la siguiente
                            slotFence[slot] = ulong.MaxValue;
                        }
                    }
                    if (slot >= 0) { latPickup = Clock.Now; break; }
                    if (frameEvent.WaitOne(20)) { if (pendingSize > slotBytes || standbyPending > 0 || reopenAsked) break; continue; }
                    // Sin imagen nueva: se repinta la ultima si el aviso se mueve o lleva 100 ms quieta (para que la tele no se quede sin nada)
                    // 18-11 (LATENCIA): con aviso, solo si no llega imagen real en aviso_repinta_ms (100). Antes se repintaba a los 20 ms: en el
                    // modo 30 (o con repetidas quitadas) una imagen de mas entre cada dos reales mientras dura el aviso, con DLSS otra vez cada
                    // una, y con la sincronizacion la cola de la tele crecia y tardaba ~1 min en vaciarse (ver EngineLatencia.cs)
                    double sl;
                    bool osdRepaint = osd.Opacity(out sl) > 0 && (s.OsdRepaintMs <= 0 || latLastReal == 0 || Clock.SinceMs(latLastReal) > s.OsdRepaintMs);
                    if (lastShown != 0 && (osdRepaint || Clock.SinceMs(lastShown) > 250)) { repaint = true; break; }
                    if (lastShown == 0 && Clock.SinceMs(lastSecond) > 1000) { repaint = inW > 0; break; }
                    ApplyPending();
                    SecondTick();
                    if (BenchDone()) break;
                }
                if (Program.Quit || BenchDone()) break;
                if (slot < 0 && !repaint) continue;
                // 18-11 (LATENCIA): test_paron= (prueba) y cola_tirar_ms= (la imagen no se pinta si la cola de la tele va llena)
                if (slot >= 0 && LatSkip(slot)) continue;
                if (slot < 0 && LatSkipRepaint()) continue;   // (23-11, CAIDAS: con Smooth Motion y la cola llena, sin repintar; EngineCaidas.cs)
                if (slot >= 0)
                {
                    int w = slotW[slot], h = slotH[slot];
                    // (16-11 noche: tambien si cambia entrada=; EnsureInputResources sale enseguida si nada cambia)
                    EnsureInputResources(w, h, slotPitch[slot] * h);
                }
                ConfigureFeatures();
                XeFgEnable();   // (1.2.4 MOTORFG)
                UpdateScaler();   // 18-11 (MOTOR-CE): dlss / fsr1 / catrom (al cambiar, sin historia en esta imagen)

                WaitFence(allocFence[fi]);
                CopySlot(slot, fi);   // (16-11: la captura a la grafica ya, con el motor de copia, mientras se graba la imagen)
                // Tiempo de GPU de la vez anterior que se uso esta ranura
                ulong* ts = (ulong*)tsPtr + fi * 2;
                double gpuMs = allocFence[fi] != 0 && ts[1] > ts[0] ? (ts[1] - ts[0]) * 1000.0 / tsFreq : 0;
                if (allocFence[fi] != 0) { if (gpuMs > 0) gpuMs += BandsBusyMs(fi); LatGpuEnd(fi, ts[1]); }   // 20-11 (LATENCIA7): + las bandas de antes (bandas_grafica); llegada -> fin de la grafica (banco)

                long arrival = slot >= 0 ? slotArrival[slot] : Clock.Now;
                if (pclOk && token != IntPtr.Zero) Sl.PCLSetMarker(SlPCLMarker.eSimulationStart, token);
                if (slOn && token != IntPtr.Zero)
                {
                    SlConstants k = SlConstants.CreateVideo((uint)inW, (uint)inH, (uint)outW, (uint)outH, resetHistory);
                    SrJitterConstants(ref k, slot);   // (23-11 ronda 7, SRBLUR: sr_jitter, prueba; EngineSrBanco.cs)
                    Sl.SetConstants(ref k, token, vp);
                }
                frameReset = resetHistory;
                resetHistory = false;
                if (pclOk && token != IntPtr.Zero) { Sl.PCLSetMarker(SlPCLMarker.eSimulationEnd, token); Sl.PCLSetMarker(SlPCLMarker.eRenderSubmitStart, token); }
                RenderFrame(slot, token, fi);
                latFiArrival[fi] = slot >= 0 ? arrival : 0;   // 20-11 (LATENCIA7)
                QueueGpuMark(fi);   // 23-11 (ronda 6, SMOOTH): la cola de la tele con la hora a la que acaba la grafica (EngineLatencia.cs)
                fenceValue++;
                queue.Signal(fence, fenceValue);
                allocFence[fi] = fenceValue;
                D5qSubmitted(fenceValue);   // (22-11 DLSS5Q, dlss5_medida=)
                if (slot >= 0) lock (slotLock) slotFence[slot] = fenceValue;
                if (pclOk && token != IntPtr.Zero) { Sl.PCLSetMarker(SlPCLMarker.eRenderSubmitEnd, token); Sl.PCLSetMarker(SlPCLMarker.ePresentStart, token); }
                // 17-11 15:45 (vsync_fg=0): con la generacion y la sincronizacion, Reflex limita la salida a 116 (120 - 120^2/3600): 58 reales de
                // 59,94 y 2 imagenes de la consola perdidas cada segundo (tirones). Sin sincronizacion (la cadena no admite cortes: sin
                // ALLOW_TEARING) van las 59,94. Sin generacion, la de siempre (vsync)
                // 20-11 (LATENCIA4): con vrr=1, Present(0, ALLOW_TEARING) sin generacion y la sincronizacion de vrr_fg_vsync con ella
                // (EngineLatencia.cs VrrPresentArgs); vrr=0 como antes
                uint syncNow, flagsNow;
                VrrPresentArgs(out syncNow, out flagsNow);
                long submitAt = Clock.Now;
                XeFgBeforePresent();
                // (ScPresent = la cadena de XeSS FG, la de Streamline o la de siempre; FmPresent = FSR FG x2 / x3 / x4 del motor, EngineFsrMultiHilo.cs;
                // EfgPresent = Enhanced FG, EngineFgEnhanced.cs)
                int hr = s.NoPresent ? 0 : efgHold ? EfgPresent(syncNow, flagsNow) : fmHold ? FmPresent(syncNow, flagsNow) : ScPresent(syncNow, flagsNow);   // (17-11 REJAS: presentar=0, pruebas con la tele apagada)
                if (pclOk && token != IntPtr.Zero) Sl.PCLSetMarker(SlPCLMarker.ePresentEnd, token);
                XeFgAfterPresent();
                if (!s.NoPresent) LatQueuePresent();   // 21-11 (1.2.1, cola_cuenta=1: EngineLatencia.cs)
                long now = Clock.Now;
                if (hr < 0) { int drr = 0; try { drr = dev.GetDeviceRemovedReason(); } catch { } Log.Write("Present: " + Hr.Format(hr) + (drr != 0 ? " (GetDeviceRemovedReason " + Hr.Format(drr) + ")" : "")); if (Hr.IsDeviceLost(hr)) { ExitCode = 3; break; } }
                LatPresented(slot, arrival, submitAt, now, gpuMs);   // 18-11 (LATENCIA)
                if (slot >= 0) { stats.Presented(arrival, now, gpuMs); lastPresentedArrival = arrival; presentedFrames++; if (presentedFrames == 1) PaintedFirst(now); else if (presentedFrames == 4) PaintedSteady(4); }   // (1.2.4 arreglos, RELEVO: switch2-pintada)
                if (slot >= 0 && wakeAt != 0) WakeFirstImage(arrival, now);
                if (slot >= 0) TirFrame(arrival, now, gpuMs);   // (16-11 tirones)
                lastShown = now;
                fi = (fi + 1) % FramesInFlight;
                SecondTick();
                if (BenchDone()) break;
            }
            WaitIdle();
            RejasDumpFinish();   // 17-11 (REJAS)
            LatFinish();         // 18-11 (LATENCIA)
            MmcssOff(mmRender);  // 21-11 (1.2.1)
        }

        bool BenchDone()
        {
            if (s.BenchSecs <= 0) return false;
            double t = Clock.SinceMs(benchStart) / 1000.0;
            if (!benchRecording && t >= s.BenchWarm) { benchRecording = true; stats.StartRecording(); }
            if (t < s.BenchWarm + s.BenchSecs && !(s.DumpDir.Length > 0 && dumpTaken >= s.DumpN && t >= s.BenchWarm + 1)) return false;   // (17-11 REJAS: con volcado, al acabarlo)
            string line = stats.Summary(s.BenchLabel, s) + "	dlss=" + (dlssOk && scalerNow == "dlss") + "	fg=" + fgApplied + "	reflex=" + reflexApplied + "	fallos_dlss=" + evalFails + "	" + FgState() + QualityBench() + PassesBench() + LatBench() + UniversalBench() + FmBench() + EfgBench();
            Log.Write("BANCO " + line);
            if (s.BenchOut.Length > 0) try { File.AppendAllText(Program.DataPath(s.BenchOut), line + "\r\n"); } catch (Exception ex) { Log.Error("Banco", ex); }   // (18-11: relativo a datos=)
            Program.Quit = true;
            return true;
        }

        // (la columna del banco de siempre; 1.2.4 ronda 6, QUITAFG: sin DLSS-G, siempre "no")
        string FgState() { return "dlssg=no"; }

        void SecondTick()
        {
            string t = stats.Tick();
            if (t == null) return;
            lastSecond = Clock.Now;
            TirSecond();    // (16-11 tirones: una linea cada ~20 s si ha pasado algo)
            LatSecond();    // (18-11 LATENCIA: lat_csv al fichero)
            AdaptaTick();   // (ConsoleEnhancer: escalas que se adaptan, EngineAdapta.cs)
            FpsVigila();    // (21-11: si no se llega a los fps del modo por el PC, aviso al lanzador, EngineFps.cs)
            AutoPresetTick();
            Unbury(false);
            // (1.2.4 ronda 6, QUITAFG: fuera la recuperacion del foco de DLSS-G -se apagaba sola sin el foco-: XeSS FG / FSR 3 FG no la necesitan)
            string extra = "";
            if (reflexOk && reflexApplied > 0)
            {
                SlReflexState rs = SlReflexState.Create();
                if (Sl.ReflexGetState(ref rs) == SlResult.eOk)
                {
                    double sum = 0; int n = 0;
                    for (int i = 0; i < 64; i++)
                    {
                        SlReflexReport rep = rs.GetFrameReport(i);
                        if (rep.frameID == 0 || rep.gpuRenderEndTime == 0 || rep.simStartTime == 0) continue;
                        sum += (rep.gpuRenderEndTime - rep.simStartTime) / 1000.0; n++;
                    }
                    if (n > 0) extra = string.Format(CultureInfo.InvariantCulture, " | Reflex PC {0:0.0} ms", sum / n);
                }
            }
            lastStatus = t + extra;
            if (s.ShowStats || s.BenchSecs > 0) Log.Write(lastStatus);
            if (lastCapture != 0 && Clock.SinceMs(lastCapture) > 2000 && !noSignalLogged) { Log.Write("Sin imagen de la fuente desde hace 2 s"); noSignalLogged = true; }
            else if (lastCapture != 0 && Clock.SinceMs(lastCapture) < 2000) noSignalLogged = false;
        }
        bool noSignalLogged;

        // Modelo que no cabe en el tiempo del modo (medido en la grafica: lo nuestro; con generacion, ademas, la de NVIDIA, que no se
        // ve aqui: margen medido, K 8,5 ms cabe y M 12,4 no): 2 segundos seguidos pasado -> el siguiente mas ligero (L > M > K > E).
        // Solo baja; vuelve a lo pedido cuando el lanzador cambia algo (modo, generacion, modelo)
        int overSecs;
        long presetSetAt;
        readonly Dictionary<string, string> presetCap = new Dictionary<string, string>();
        string CapKey { get { return s.Mode + "|" + (s.Fg ? 1 : 0) + "|" + inW + "x" + inH; } }
        static int Rank(string p) { return p == "L" ? 3 : p == "M" ? 2 : p == "K" || p == "J" ? 1 : 0; }
        int evalFailsRow;
        bool dlssBroken, frameReset;
        long lastPresentedArrival, presentedFrames;

        // Recursos que la generacion puede seguir usando: se sueltan a los 3 s
        readonly List<KeyValuePair<long, ID3D12Resource>> graveyard = new List<KeyValuePair<long, ID3D12Resource>>();
        void Bury(ID3D12Resource r) { if (r != null) graveyard.Add(new KeyValuePair<long, ID3D12Resource>(Clock.Now, r)); }
        void Unbury(bool all)
        {
            for (int i = graveyard.Count - 1; i >= 0; i--)
                if (all || Clock.SinceMs(graveyard[i].Key) > 3000)
                {
                    ID3D12Resource r = graveyard[i].Value;
                    Com.Release(ref r);
                    graveyard.RemoveAt(i);
                }
        }
        string autoFrom = "";
        void AutoPresetTick()
        {
            if (!s.AutoPreset || !dlssOk || scalerNow != "dlss" || (inW == outW && inH == outH) || stats.LastGpuP95 <= 0) { overSecs = 0; return; }
            // 16-11 (sin telon): mientras el lanzador cambia de imagen (FocusHold) la grafica la comparte con Magpie arrancando o escalando: su
            // tiempo medido no es el del motor solo (no se rebaja el modelo por eso)
            if (Program.FocusHold) { overSecs = 0; return; }
            // 16-11 noche: recien puesto el modelo (o cambiado el tamaño) se pierden imagenes unos segundos aunque quepa (M a 1080p con
            // generacion: 4 perdidas/s a los 2 s con la grafica en 9,8 ms de 11): no se juzga hasta los 5 s
            if (Clock.SinceMs(presetSetAt) < 5000) { overSecs = 0; return; }
            if (AutoPresetEspera()) { overSecs = 0; return; }   // (23-11 ronda 7, SRBLUR: con DLSS 5 baja antes Adapta, y nada tras rehacer DLSS 5; EngineSrBlur.cs)
            double limit = TopeGpu(); // 20-11 (HZ120): a 120 el hueco por fotograma es ~8,3 ms. (1.2.2, UNIVERSAL: el 85 %, EngineMargen.cs; antes 26/31/8,0/11,0/15,5 fijos)
            TopeGpuLog();
            // 16-11 (tirones): con la generacion lo de NVIDIA no se ve en lo medido: si ademas se pierden imagenes de la captura (3 o mas
            // por segundo), tampoco cabe. Medido: 1440p + K con los vectores de calidad, 10,1 ms (p95 10,8: por debajo del tope) pero 6
            // perdidas/s, 54 reales y 13 % de imagenes dobles en la tele
            int lost = fgApplied && s.Mode == 60 ? stats.LastLate : 0;
            // 17-11 16:50: con la sincronizacion, Reflex limita la salida a 116 a 120 Hz: ~2 perdidas/s (3 en algun segundo) aunque la grafica
            // sobre. Contaba como "no cabe" y L pasaba solo a M (16:45:14, p95 10,7 de 11 ms) con mas rastro en la reja: con sincronizacion, 5
            int lostMax = fgApplied && s.VsyncFg ? 5 : 3;
            if (stats.LastGpuP95 > limit || lost >= lostMax) overSecs++; else overSecs = 0;
            if (overSecs < 2) return;
            overSecs = 0;
            if (MvSrFallback(stats.LastGpuP95, limit, lost >= lostMax)) return;   // 20-11 (SR45): primero los vectores completos de DLSS SR (EngineMotion.cs)
            string next = s.Preset == "L" ? "M" : s.Preset == "M" ? "K" : s.Preset == "K" || s.Preset == "J" ? "E" : null;
            if (next == null) return;
            Log.Write(string.Format(CultureInfo.InvariantCulture, "DLSS {0} no cabe aqui (grafica p95 {1:0.0} ms, tope {2:0.0}{3}): se usa {4}", s.Preset, stats.LastGpuP95, limit,
                lost >= 3 ? "; con la generacion se pierden " + lost + " imagenes/s" : "", next));
            osd.Show("DLSS " + s.Preset + " no llega aquí · se usa " + next + " (más ligero)", 3000, null);
            s.Preset = next;
            presetCap[CapKey] = next;
        }
        volatile bool reopenAsked;          // 16-11 (sin telon): switch2-reabrir

        // =============================================================================================== tuberia
        string OnCommand(string line)
        {
            if (line.Length == 0) return "ok";
            string cmd = line, arg = "";
            int sp = line.IndexOf(' ');
            if (sp > 0) { cmd = line.Substring(0, sp); arg = line.Substring(sp + 1).Trim(); }
            switch (cmd.ToLowerInvariant())
            {
                case "osd":
                    {
                        int ms = 2500;
                        int sp2 = arg.IndexOf(' ');
                        if (sp2 > 0 && int.TryParse(arg.Substring(0, sp2), out ms)) arg = arg.Substring(sp2 + 1);
                        osd.Show(arg, ms, null);
                        frameEvent.Set();
                        return "ok";
                    }
                case "osd_off": osd.Hide(); return "ok";
                case "set":
                    lock (pendingLock)
                        foreach (string kv in arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            int i = kv.IndexOf('=');
                            if (i > 0) pending.Add(new KeyValuePair<string, string>(kv.Substring(0, i), kv.Substring(i + 1)));
                        }
                    frameEvent.Set();
                    return "ok";
                case "status":
                    return "ok " + lastStatus + " | " + s.Describe() + " | dlss=" + dlssOk +  (xeFgOn ? " xefg=x" + XeFgMultNow : "") + (ffxFgOn ? " fsr3fg=x2" : "") + " reflex=" + reflexApplied + " escalador=" + scalerNow + " streamline=" + (slOn ? 1 : 0) + (inStandby ? " | en espera" : "");
                // 16-11: lo mismo que script-message switch2-standby / switch2-wake (para probar sin el lanzador)
                case "standby": AskStandby(false); return "ok";
                case "wake": AskStandby(true, arg == "detras"); return "ok";
                // 16-11 (sin telon): lo mismo que script-message switch2-delante / switch2-encima <0|1|2> / switch2-reabrir
                case "delante": return Program.Front() ? "ok" : "error ventana";
                case "encima":
                    {
                        int mode = 1;
                        int.TryParse(arg, out mode);
                        return Program.SetOnTop(Math.Max(0, Math.Min(2, mode))) ? "ok" : "error ventana";
                    }
                case "reabrir": reopenAsked = true; frameEvent.Set(); return "ok";
                case "grab":
                    {
                        string[] a = arg.Split(' ');
                        grabPath = a[0]; grabMode = a.Length > 1 ? a[1] : "full";
                        grabDone.Reset();
                        grabWanted = true;
                        if (!grabDone.WaitOne(1500)) { grabWanted = false; return "error sin imagen"; }
                        return "ok " + lastGrab;
                    }
                case "emit":
                    // Prueba: un aviso como los del script de mpv al lanzador ("emit switch2-pad fg")
                    Ev(arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                    return "ok";
                case "quit": Program.CloseWindow(); frameEvent.Set(); return "ok";
                default: return "error orden desconocida";
            }
        }

        public void Dispose()
        {
            try { if (pipe != null) pipe.Stop(); if (mpvIpc != null) mpvIpc.Stop(); if (eventTimer != null) eventTimer.Dispose(); } catch { }
            lock (sourceLock) { if (source != null) { try { source.Dispose(); } catch { } source = null; } }
            try { WaitIdle(); } catch { }
            DisposeDlss5();   // 20-11 (DLSS 5 EN EL MOTOR)
            if (slOn)
            {
                try
                {
                    SlProxyRelease();   // (1.2.4 ronda 5, INTEGRA5: el proxy de la cadena, antes de slShutdown; EngineSlProxy.cs)
                    Sl.Shutdown();
                    Unbury(true);
                }
                catch (Exception ex) { Log.Error("slShutdown", ex); }
            }
            tags.Dispose();
        }
    }
}
