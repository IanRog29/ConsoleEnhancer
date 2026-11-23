using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFsrMulti.cs - 23-11-2026 (1.2.4 ronda 6, agente FGMULTI). FSR FG x3 / x4 (y x2) LEGAL en el motor, para cualquier grafica.
    // El usuario: "podrias intentar crear un modo smooth motion 3x y 4x, con fsr y xess tambien, y haciendo que vayan muy bien y sin latencia".
    // Lo legal que habia no daba x3 / x4 fuera de RTX 50 / Intel: DLSS FG x3/x4 solo RTX 50, XeSS FG x3/x4 solo Intel (fuera, parche en
    // memoria: lo prohibe su licencia), Smooth Motion x3/x4 = parchear el driver (no), y la API de FSR FG de AMD (amd_fidelityfx_
    // framegeneration_dx12.dll, la que carga EngineFgFfx.cs) solo da UNA inventada, a mitad. Pero el codigo de la generacion de FSR 3.1 es
    // MIT: engine\CeFsrFg.dll (src\CeFsrFg, build_fsrfg.bat) es la interpolacion + flujo optico de AMD (FidelityFX SDK 1.1.4) compilada por
    // nosotros y cambiada para inventar en cualquier instante (parches\fidelityfx-fi-multi.patch): x3 = 1/3 y 2/3, x4 = 1/4, 2/4 y 3/4, todas
    // con el flujo optico y los vectores preparados de ese par (lo que no depende del instante se hace una vez).
    // Como va en el motor: con fsrfg_multi=k (y fg=0: sin DLSS-G / FSR 3 FG / XeSS FG en la cadena), en cada imagen real N, dentro de la
    // lista del motor: CeFg_Prepare con la profundidad y los vectores de la generacion (los mismos que FSR 3 FG / XeSS FG del motor:
    // mvecFg con fg_vectores=3 y depthFg con fg_profundidad) y la copia de la real (con y sin aviso) a uno de DOS juegos de texturas; al
    // presentar, en una COLA DE CALCULO ASINCRONO propia (que espera a la lista del motor en la grafica): CeFg_Generate = flujo optico
    // N-1 -> N y las k-1 inventadas a las texturas de ese juego. Un hilo de presentacion (EngineFsrMultiHilo.cs) espera a que acabe y las
    // presenta cada intervalo real / k y luego la real. Asi la FSR de N+1 (12-15 ms a 4K con x4) va a la vez que se presentan las de N: con
    // una sola cola las copias de las ultimas inventadas de N esperaban detras de ella (medido: 23,9 reales/s con x4). Sin cadena de terceros:
    // el Present es el del motor (VRR / sincronizacion como la generacion propia de la 1.2.4).
    // Claves (motor): fsrfg_multi=0|2|3|4 (de serie 0), fsrfg_multi_vsync=-1|0|1 (auto: con sincronizacion si k x las reales pasan del 90 %
    // del refresco), fsrfg_multi_prof=0|1 (1: la profundidad de la generacion, depthFg, si la hay), fsrfg_multi_dll=<ruta> (pruebas).
    partial class Settings
    {
        public int FsrMulti = 0;
        public int FsrMultiVsync = -1;
        public int FsrMultiDepth = 1;
        public string FsrMultiDll = "";
        public bool FsrMultiScQueue = true;  // fsrfg_multi_cola_cadena=0|1: la cadena (y las copias a ella) en una cola de presentacion propia
        public int FsrMultiQueue = 1;     // fsrfg_multi_cola=0|1|2: cola de calculo de prioridad normal / alta (de serie) / la de la imagen (sin asincrono)
        public string FsrMultiCsv = "";   // fsrfg_multi_csv=<fichero> (pruebas): cada Present del hilo (real / inventada, llegada de la real, hora del Present)

        bool ApplyFsrMulti(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "fsrfg_multi": { int k = I(val); FsrMulti = k <= 1 ? 0 : Math.Min(4, k); return true; }
                case "fsrfg_multi_vsync": FsrMultiVsync = I(val); return true;
                case "fsrfg_multi_prof": FsrMultiDepth = I(val); return true;
                case "fsrfg_multi_dll": FsrMultiDll = val.Trim(); return true;
                case "fsrfg_multi_csv": FsrMultiCsv = val.Trim(); return true;
                case "fsrfg_multi_cola": FsrMultiQueue = Math.Max(0, Math.Min(2, I(val))); return true;
                case "fsrfg_multi_cola_cadena": FsrMultiScQueue = B(val); return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CeFgVersionFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CeFgCreateFn(IntPtr dev, int dispW, int dispH, int maxRW, int maxRH, int fmt, int flags, out IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CeFgPrepareFn(IntPtr ctx, IntPtr list, IntPtr depth, IntPtr mvec, int rw, int rh, float sx, float sy, float dtMs, ulong frameId);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CeFgGenerateFn(IntPtr ctx, IntPtr list, IntPtr color, int colorState, int n, IntPtr* outs, int outState, float* times, int reset, ulong frameId, float dtMs, int rw, int rh);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void CeFgDestroyFn(IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] delegate int CeFgErrFn(System.Text.StringBuilder buf, int len);

        IntPtr fmLib, fmCtx;
        CeFgCreateFn fmCreate; CeFgPrepareFn fmPrepare; CeFgGenerateFn fmGenerate; CeFgDestroyFn fmDestroy; CeFgErrFn fmErr;
        bool fmBroken, fmSaid, fmLoadTried;
        int fmW, fmH;
        ulong fmFrameId, fmPreparedId;          // fmPreparedId = la imagen real que se preparo en esta RenderFrame (0 = ninguna)
        readonly ulong[] fmPrepCv = new ulong[2];   // por juego de lo preparado (frameId & 1): el calculo que lo lee
        long fmLastRealAt;
        long fmGens, fmFrames;
        bool fmHold;                            // RenderFrame no la ha copiado a la cadena: lo hace el hilo
        bool fmGen;                             // esta imagen lleva inventadas
        bool fmReset;                           // corte / sin preparar: FSR sin inventar (solo guarda la actual como anterior)
        int fmSet;                              // el juego de texturas de esta real (0/1)
        long fmArrival;                         // la llegada de esta real (para fsrfg_multi_csv)
        // dos juegos: la entrada de FSR (la real sin aviso), la real a enseñar (con aviso si lo hay) y las k-1 inventadas
        readonly ID3D12Resource[] fmSrc = new ID3D12Resource[2], fmReal = new ID3D12Resource[2];
        readonly ID3D12Resource[,] fmOut = new ID3D12Resource[2, 3];
        int fmTexW, fmTexH;
        // la cola de calculo asincrono y lo suyo
        ID3D12CommandQueue fmCq;
        ID3D12Fence fmCFence;
        ulong fmCVal;
        readonly ID3D12CommandAllocator[] fmCAlloc = new ID3D12CommandAllocator[6];
        readonly ulong[] fmCAllocCv = new ulong[6];
        int fmCAi;
        ID3D12GraphicsCommandList fmCList;
        readonly AutoResetEvent fmCEv = new AutoResetEvent(false);

        bool FmWanted() { return s.FsrMulti >= 2 && !s.Fg && s.FgOwn < 2 && !fmBroken; }
        bool fmRepaint;                         // esta imagen es un repintado (sin imagen nueva): solo la real, sin FSR

        // COLA DE PRESENTACION propia (fsrfg_multi_cola_cadena=1): la cadena se crea con ella y las copias a la cadena y los Present van por
        // ella; asi las inventadas salen a su hora aunque la cola de la imagen este ocupada (DLSS 5, la FSR de la real siguiente...)
        ID3D12CommandQueue scQueue;
        ID3D12Fence fmScFence;
        ulong fmScVal;
        ID3D12CommandQueue ScQ { get { return scQueue ?? queue; } }
        ID3D12CommandQueue FmScQueueFor()
        {
            if (!(s.FsrMulti >= 2 && !s.Fg && s.FgOwn < 2 && s.FsrMultiScQueue)) return queue;
            if (scQueue == null)
            {
                object o;
                D3D12_COMMAND_QUEUE_DESC qd = new D3D12_COMMAND_QUEUE_DESC();
                qd.Type = D3D12_COMMAND_LIST_TYPE.DIRECT;
                qd.Priority = 100;
                Guid iid = typeof(ID3D12CommandQueue).GUID;
                Hr.Check(dev.CreateCommandQueue(ref qd, ref iid, out o), "CreateCommandQueue (FSR FG, presentacion)");
                scQueue = (ID3D12CommandQueue)o;
                iid = typeof(ID3D12Fence).GUID;
                Hr.Check(dev.CreateFence(0, D3D12_FENCE_FLAGS.NONE, ref iid, out o), "CreateFence (FSR FG, presentacion)");
                fmScFence = (ID3D12Fence)o;
                Log.Write("FSR FG: la cadena con su propia cola de presentacion (prioridad alta)");
            }
            return scQueue;
        }
        // ScPresent (lo que presenta el hilo de pintado): en la cola de presentacion, tras lo que ha grabado la de la imagen
        void FmScWait() { if (scQueue != null) scQueue.Wait(fence, fenceValue); }
        // WaitIdle: que la cola de presentacion tambien acabe
        void FmScIdle()
        {
            if (scQueue == null) return;
            fmScVal++;
            scQueue.Signal(fmScFence, fmScVal);
            if (fmScFence.GetCompletedValue() < fmScVal) { fmScFence.SetEventOnCompletion(fmScVal, fmCEv.SafeWaitHandle.DangerousGetHandle()); fmCEv.WaitOne(1000); }
        }
        // para la cadena de vectores del motor (ExtFgOn, EngineFgXe.cs): los mismos vectores y profundidad que FSR 3 FG / XeSS FG
        bool FmVecOn { get { return FmWanted(); } }
        int FmK { get { return FmWanted() ? s.FsrMulti : 1; } }
        ID3D12Resource FmVec { get { return FfxFgVecNow && s.FgVec >= 2 && mvecFg != null ? mvecFg : FgMvec; } }
        ID3D12Resource FmDepth { get { return s.FsrMultiDepth > 0 && s.FgDepth > 0 && depthFg != null && FfxFgVecNow ? depthFg : depth; } }

        string FmErrText()
        {
            if (fmErr == null) return "";
            System.Text.StringBuilder b = new System.Text.StringBuilder(512);
            fmErr(b, 512);
            return b.ToString();
        }

        bool FmLoad()
        {
            if (fmLib != IntPtr.Zero) return true;
            if (fmLoadTried) return false;
            fmLoadTried = true;
            string p = s.FsrMultiDll.Length > 0 ? s.FsrMultiDll : Path.Combine(Program.Dir, "CeFsrFg.dll");
            if (!File.Exists(p)) { Log.Write("FSR FG x" + s.FsrMulti + ": no esta " + p + " (build_fsrfg.bat): sin ella"); return false; }
            fmLib = SrNative.LoadLibraryExW(p, IntPtr.Zero, 0x8);
            if (fmLib == IntPtr.Zero) { Log.Write("FSR FG x" + s.FsrMulti + ": " + p + " no se pudo cargar (error " + Marshal.GetLastWin32Error() + ")"); return false; }
            CeFgVersionFn ver = SrNative.Fn<CeFgVersionFn>(fmLib, "CeFg_Version");
            fmCreate = SrNative.Fn<CeFgCreateFn>(fmLib, "CeFg_Create");
            fmPrepare = SrNative.Fn<CeFgPrepareFn>(fmLib, "CeFg_Prepare");
            fmGenerate = SrNative.Fn<CeFgGenerateFn>(fmLib, "CeFg_Generate");
            fmDestroy = SrNative.Fn<CeFgDestroyFn>(fmLib, "CeFg_Destroy");
            fmErr = SrNative.Fn<CeFgErrFn>(fmLib, "CeFg_LastError");
            if (ver == null || fmCreate == null || fmPrepare == null || fmGenerate == null || fmDestroy == null || ver() != 1)
            { Log.Write("FSR FG x" + s.FsrMulti + ": " + p + " no es la que espera el motor (CeFg_* version 1)"); fmLib = IntPtr.Zero; return false; }
            Log.Write("FSR FG x" + s.FsrMulti + ": " + p + " (generacion de FSR 3.1 de AMD, MIT, compilada por ConsoleEnhancer para varios instantes)");
            return true;
        }

        // La cola de calculo (prioridad normal: la de la imagen y la presentacion es alta) y su lista
        void FmEnsureCompute()
        {
            if (fmCq != null) return;
            object o;
            Guid iid;
            D3D12_COMMAND_LIST_TYPE lt = s.FsrMultiQueue == 2 ? D3D12_COMMAND_LIST_TYPE.DIRECT : D3D12_COMMAND_LIST_TYPE.COMPUTE;
            if (s.FsrMultiQueue == 2) fmCq = queue;   // (pruebas: en la cola de la imagen, sin asincrono)
            else
            {
                D3D12_COMMAND_QUEUE_DESC qd = new D3D12_COMMAND_QUEUE_DESC();
                qd.Type = D3D12_COMMAND_LIST_TYPE.COMPUTE;
                qd.Priority = s.FsrMultiQueue == 1 ? 100 : 0;
                iid = typeof(ID3D12CommandQueue).GUID;
                Hr.Check(dev.CreateCommandQueue(ref qd, ref iid, out o), "CreateCommandQueue (FSR FG, calculo)");
                fmCq = (ID3D12CommandQueue)o;
            }
            iid = typeof(ID3D12CommandAllocator).GUID;
            for (int i = 0; i < fmCAlloc.Length; i++)
            {
                Hr.Check(dev.CreateCommandAllocator(lt, ref iid, out o), "CreateCommandAllocator (FSR FG, calculo)");
                fmCAlloc[i] = (ID3D12CommandAllocator)o;
            }
            iid = typeof(ID3D12GraphicsCommandList).GUID;
            Hr.Check(dev.CreateCommandList(0, lt, fmCAlloc[0], null, ref iid, out o), "CreateCommandList (FSR FG, calculo)");
            fmCList = (ID3D12GraphicsCommandList)o;
            Hr.Check(fmCList.Close(), "Close");
            iid = typeof(ID3D12Fence).GUID;
            Hr.Check(dev.CreateFence(0, D3D12_FENCE_FLAGS.NONE, ref iid, out o), "CreateFence (FSR FG, calculo)");
            fmCFence = (ID3D12Fence)o;
        }

        // Espera (CPU) a que la cola de calculo haya hecho hasta v
        void FmWaitCompute(ulong v, int ms)
        {
            if (v == 0 || fmCFence == null || fmCFence.GetCompletedValue() >= v) return;
            fmCFence.SetEventOnCompletion(v, fmCEv.SafeWaitHandle.DangerousGetHandle());
            if (!fmCEv.WaitOne(ms)) Log.Write("FSR FG: el calculo no acaba (" + ms + " ms)");
        }

        // El contexto de AMD al tamaño de la salida (se rehace si cambia; la entrada puede ir de 720p a 4K sin rehacer)
        bool FmEnsureCtx()
        {
            if (fmCtx != IntPtr.Zero && fmW == outW && fmH == outH) return true;
            if (!FmLoad()) { fmBroken = true; return false; }
            FmEnsureCompute();
            if (fmCtx != IntPtr.Zero) { FmhDrain(); FmWaitCompute(fmCVal, 2000); WaitIdle(); fmDestroy(fmCtx); fmCtx = IntPtr.Zero; }
            long t0 = Clock.Now;
            IntPtr c;
            int r = fmCreate(Com.BorrowPtr(dev, typeof(ID3D12Device)), outW, outH, outW, outH, (int)ColorFmt, 0, out c);
            if (r != 0 || c == IntPtr.Zero)
            {
                Log.Write("FSR FG x" + s.FsrMulti + ": no se pudo crear (" + r + ": " + FmErrText() + "): sin ella");
                fmBroken = true;
                return false;
            }
            fmCtx = c; fmW = outW; fmH = outH; fmFrameId = 0;
            Log.Write("FSR FG x" + s.FsrMulti + ": contexto de AMD (interpolacion + flujo optico) a " + outW + "x" + outH + " en " + Clock.SinceMs(t0).ToString("0", IC) + " ms, " + (s.FsrMultiQueue == 2 ? "en la cola de la imagen" : "en una cola de calculo asincrono de prioridad " + (s.FsrMultiQueue == 1 ? "alta" : "normal")));
            return true;
        }

        // Los dos juegos de texturas al tamaño de la salida
        void FmEnsureTex(int k)
        {
            if (fmTexW != outW || fmTexH != outH)
            {
                FmhDrain();
                FmWaitCompute(fmCVal, 2000);
                WaitIdle();
                for (int b = 0; b < 2; b++)
                {
                    if (fmSrc[b] != null) Bury(fmSrc[b]);
                    if (fmReal[b] != null) Bury(fmReal[b]);
                    fmSrc[b] = fmReal[b] = null;
                    for (int j = 0; j < 3; j++) { if (fmOut[b, j] != null) Bury(fmOut[b, j]); fmOut[b, j] = null; }
                }
                fmTexW = outW; fmTexH = outH;
            }
            int need = Math.Max(1, Math.Min(3, k - 1));
            for (int b = 0; b < 2; b++)
            {
                if (fmSrc[b] == null) fmSrc[b] = Tex(ColorFmt, outW, outH, false, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "FSR FG: la real (entrada) " + b);
                if (fmReal[b] == null) fmReal[b] = Tex(ColorFmt, outW, outH, false, D3D12_RESOURCE_STATES.COPY_SOURCE, "FSR FG: la real (a enseñar) " + b);
                for (int j = 0; j < need; j++)
                    if (fmOut[b, j] == null) fmOut[b, j] = Tex(ColorFmt, outW, outH, true, D3D12_RESOURCE_STATES.COPY_SOURCE, "FSR FG: inventada " + (j + 1) + " / " + b);
            }
        }

        // RenderFrame, tras XeFgRecord (profundidad y vectores en NON_PIXEL_SHADER_RESOURCE en la lista del motor): lo que FSR prepara de cada
        // imagen real (profundidad y vectores dilatados, la profundidad de la anterior reconstruida) a su juego (frameId & 1)
        void FmRecordPrepare(int slot)
        {
            fmPreparedId = 0;
            if (!FmWanted() || slot < 0 || s.NoPresent) return;
            ID3D12Resource mv = FmVec, dp = FmDepth;
            if (mv == null || dp == null) return;
            try
            {
                if (!FmEnsureCtx()) return;
                ulong id = fmFrameId + 1;
                FmWaitCompute(fmPrepCv[id & 1], 200);   // (el calculo de hace dos reales aun leia este juego: casi nunca espera)
                double dt = fmLastRealAt == 0 ? 33.4 : Clock.SinceMs(fmLastRealAt);
                int r = fmPrepare(fmCtx, Com.BorrowPtr(list, typeof(ID3D12GraphicsCommandList)), Com.BorrowPtr(dp, typeof(ID3D12Resource)), Com.BorrowPtr(mv, typeof(ID3D12Resource)),
                                  inW, inH, 1f, 1f, (float)Math.Max(1, Math.Min(100, dt)), id);
                if (r != 0) { Log.Write("FSR FG x" + s.FsrMulti + ": preparar = " + r + " (" + FmErrText() + "): sin ella"); fmBroken = true; return; }
                fmPreparedId = id;
            }
            catch (Exception ex) { fmBroken = true; Log.Write("FSR FG x" + s.FsrMulti + ": fallo al preparar (" + ex.Message + "): sin ella"); }
        }

        // RenderFrame, paso 5 (con 'show' = lo que se enseñaria): true = el hilo de FSR FG la enseña (y no se copia aqui a la cadena). En la
        // lista del motor: la real (sin aviso) como entrada de FSR y la que se enseña (con aviso), al juego libre
        bool FmTakeReal(ID3D12Resource show, int slot)
        {
            fmHold = false; fmGen = false; fmRepaint = false;
            if (!FmWanted() || s.NoPresent || fmCtx == IntPtr.Zero) return false;
            if (slot < 0 && scQueue == null) return false;   // (sin cola de presentacion propia el repintado va como siempre)
            int k = FmK;
            try
            {
                FmEnsureTex(k);
                int b = (fmSet + 1) % 2;
                // que el hilo de presentacion ya haya mandado las copias de este juego (las de hace dos reales)
                if (!FmhSetFree(b, 250)) { Log.Write("FSR FG: el hilo de presentacion va tarde (250 ms): se espera"); FmhSetFree(b, 1000); }
                fmSet = b;
                D3D12_RESOURCE_BARRIER x;
                x = D3D12_RESOURCE_BARRIER.CreateTransition(fmSrc[b], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_DEST); list.ResourceBarrier(1, ref x);
                list.CopyResource(fmSrc[b], final);
                x = D3D12_RESOURCE_BARRIER.CreateTransition(fmSrc[b], D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE); list.ResourceBarrier(1, ref x);
                x = D3D12_RESOURCE_BARRIER.CreateTransition(fmReal[b], D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.COPY_DEST); list.ResourceBarrier(1, ref x);
                list.CopyResource(fmReal[b], show);
                x = D3D12_RESOURCE_BARRIER.CreateTransition(fmReal[b], D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.COPY_SOURCE); list.ResourceBarrier(1, ref x);
            }
            catch (Exception ex) { fmBroken = true; Log.Write("FSR FG x" + k + ": fallo (" + ex.Message + "): se sigue sin ella"); return false; }
            fmHold = true;
            fmRepaint = slot < 0;
            fmArrival = slot >= 0 ? slotArrival[slot] : Clock.Now;
            // inventadas solo con esta imagen preparada, una anterior (la 1a solo se guarda), sin corte y sin aviso encima (las inventadas no
            // lo llevarian: parpadearia)
            fmReset = fmPreparedId == 0 || frameReset || fmFrameId == 0;
            fmGen = !fmRepaint && !fmReset && ReferenceEquals(show, final);
            return true;
        }

        // Run (FmPresent, EngineFsrMultiHilo.cs): en la cola de calculo, tras la lista del motor de esta real (mainFence): el flujo optico y
        // las k-1 inventadas de FSR al juego fmSet (o, sin inventar, solo que FSR guarde la actual como anterior). Devuelve el valor de su valla
        ulong FmSubmitGenerate(int k, ulong mainFence)
        {
            int b = fmSet;
            int n = fmGen ? k - 1 : 1;
            IntPtr* outs = stackalloc IntPtr[4];
            float* times = stackalloc float[4];
            for (int j = 0; j < n; j++) { outs[j] = Com.BorrowPtr(fmOut[b, j], typeof(ID3D12Resource)); times[j] = fmGen ? (float)(j + 1) / k : 0.5f; }
            fmCAi = (fmCAi + 1) % fmCAlloc.Length;
            FmWaitCompute(fmCAllocCv[fmCAi], 1000);
            Hr.Check(fmCAlloc[fmCAi].Reset(), "Allocator.Reset (FSR FG, calculo)");
            Hr.Check(fmCList.Reset(fmCAlloc[fmCAi], null), "List.Reset (FSR FG, calculo)");
            D3D12_RESOURCE_BARRIER x;
            for (int j = 0; j < n; j++) { x = D3D12_RESOURCE_BARRIER.CreateTransition(fmOut[b, j], D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS); fmCList.ResourceBarrier(1, ref x); }
            double dt = fmLastRealAt == 0 ? 33.4 : Clock.SinceMs(fmLastRealAt);
            ulong id = fmPreparedId != 0 ? fmPreparedId : fmFrameId + 1;
            int r = fmGenerate(fmCtx, Com.BorrowPtr(fmCList, typeof(ID3D12GraphicsCommandList)), Com.BorrowPtr(fmSrc[b], typeof(ID3D12Resource)),
                               (int)D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, n, outs, (int)D3D12_RESOURCE_STATES.UNORDERED_ACCESS, times,
                               fmReset ? 1 : 0, id, (float)Math.Max(1, Math.Min(100, dt)), inW, inH);
            fmFrameId = id;
            for (int j = 0; j < n; j++) { x = D3D12_RESOURCE_BARRIER.CreateTransition(fmOut[b, j], D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.COPY_SOURCE); fmCList.ResourceBarrier(1, ref x); }
            Hr.Check(fmCList.Close(), "Close (FSR FG, calculo)");
            if (r != 0) throw new InvalidOperationException("CeFg_Generate = " + r + " (" + FmErrText() + ")");
            fmCq.Wait(fence, mainFence);   // (en la grafica: tras la lista del motor de esta real; la CPU no espera)
            IntPtr lp = Com.BorrowPtr(fmCList, typeof(ID3D12CommandList));
            fmCq.ExecuteCommandLists(1, ref lp);
            fmCVal++;
            fmCq.Signal(fmCFence, fmCVal);
            fmCAllocCv[fmCAi] = fmCVal;
            fmPrepCv[id & 1] = fmCVal;
            if (!fmSaid)
            {
                fmSaid = true;
                Log.Write("FSR FG si (x" + k + ", interpolacion de FSR 3.1 en " + (k == 2 ? "1/2" : k == 3 ? "1/3 y 2/3" : "1/4, 2/4 y 3/4") + " con un flujo optico por real, en la cola de calculo; vectores " +
                          (ReferenceEquals(FmVec, mvecFg) ? "de la generacion" : "del motor") + ", profundidad " + (ReferenceEquals(FmDepth, depthFg) ? "de la generacion" : "plana") + ")");
            }
            return fmCVal;
        }

        // (1.2.4 ronda 6) Rota en esta sesion: el lanzador lo sabe una vez (switch2-fsrmulti rota N) y pasa a otro camino. Desde EventTick
        bool fmBrokenSaid;
        void FmFailTick()
        {
            if (fmBrokenSaid || !fmBroken || s.FsrMulti < 2 || mpvIpc == null) return;
            if (mpvIpc.Event("switch2-fsrmulti", "rota", s.FsrMulti.ToString(IC)) > 0) { fmBrokenSaid = true; Log.Write("FSR FG multiple: rota, avisado el lanzador"); }
        }

        string FmBench() { return s.FsrMulti >= 2 ? "\tfsrfg_multi=x" + s.FsrMulti + " inventadas=" + fmGens + " reales=" + fmFrames + (fmBroken ? " rota" : "") + FmhBench() : ""; }
    }
}
