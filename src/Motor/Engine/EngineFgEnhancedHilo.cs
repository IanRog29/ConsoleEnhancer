using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFgEnhancedHilo.cs - 23-11-2026 (1.2.4, sexta ronda, agente ENHANCED). El hilo de presentacion de Enhanced FG (EngineFgEnhanced.cs).
    // Por cada real: espera (CPU) a que la grafica la tenga en su ranura, manda la piramide de luma + el flujo en los dos sentidos + lo quieto +
    // el corte (una lista, en la cola de presentacion) y presenta las k imagenes (k-1 inventadas y la real) cada intervalo real / k a partir de
    // B = llegada de la captura + L. L se adapta: lo que tarda la real en estar lista (p90 de las ultimas 32) + el flujo + la primera inventada +
    // efg_margen; sube enseguida y baja despacio (asi la hora de cada imagen no depende de cuanto tarde la grafica en cada una). Cada inventada se
    // graba y se manda justo antes de su hora (su eleccion a un cuarto de resolucion + la imagen a la de salida + la copia a la cadena), se espera
    // a que la grafica la acabe y se presenta a su hora. Si llega otra real antes de acabar (la grafica no da), las inventadas que quedan se saltan.
    unsafe partial class Engine
    {
        class EfgJob { public int W, K; public long Arrival, Seq, ReadyAt; public ulong Fence; public bool Reset, Show; public uint Sync, Flags; public double Phase, RealMs; public ManualResetEvent Ready; }

        Thread efgThread;
        readonly Queue<EfgJob> efgJobs = new Queue<EfgJob>();
        readonly AutoResetEvent efgJobEv = new AutoResetEvent(false), efgMainEv = new AutoResetEvent(false), efgOwnEv = new AutoResetEvent(false);
        // (1.2.4 ronda 7, EFGFIX) EfgDrain (hilo de pintado) con su propio evento: compartia efgOwnEv con el hilo de presentacion y, si los dos
        // esperaban a la vez, una de las dos señales se perdia (un AutoResetEvent ya puesto no cuenta dos) y uno se quedaba esperando hasta su
        // tope; y una señal vieja (de una espera que ya habia vencido) despertaba antes de tiempo la siguiente
        readonly AutoResetEvent efgDrainEv = new AutoResetEvent(false);
        readonly ManualResetEvent efgIdle = new ManualResetEvent(true);
        readonly ID3D12CommandAllocator[] efgAlloc = new ID3D12CommandAllocator[8];
        readonly ulong[] efgAllocFence = new ulong[8];
        int efgAi;
        ID3D12GraphicsCommandList efgList;
        ulong efgFenceValue;              // (solo el hilo de presentacion)
        volatile int efgHr;
        long efgPrevSeq = -10;            // la ultima real procesada (su ranura es la "anterior" de la siguiente)
        readonly bool[] efgFlowOk = new bool[EfgSets];
        readonly bool[] efgStillOk = new bool[EfgSets];
        long efgReals, efgGens, efgDups, efgLate, efgSkipped;
        double efgLcur, efgCopyMs = 0.3;
        readonly double[] efgReadyWin = new double[32];
        int efgReadyN;
        long efgLastPresent, efgLastLog;
        double efgHz; long efgHzAt;
        readonly StringBuilder efgCsvBuf = new StringBuilder();
        long efgCsvAt;
        // el vigilante: la hora EXACTA a la que la grafica acaba cada real (si no, con el hilo de presentacion ocupado con la anterior, "lista"
        // se veia tarde, L subia y todo iba aun mas tarde: x4 medido a las 05:29 con L 112 ms)
        Thread efgWatch;
        readonly Queue<EfgJob> efgWatchQ = new Queue<EfgJob>();
        readonly AutoResetEvent efgWatchEv = new AutoResetEvent(false), efgWatchFenceEv = new AutoResetEvent(false);
        // tiempos de la grafica de lo propio (marcas de tiempo en la cola de presentacion): 0-1 el flujo, 2-3 la ultima inventada
        ID3D12QueryHeap efgQheap;
        ID3D12Resource efgTsBuf;
        IntPtr efgTsPtr;
        double efgTsFreq;
        double efgFlowGpu = 1.0, efgGenGpu = 1.0;

        void EfgThreadStart()
        {
            object o;
            Guid iid = typeof(ID3D12CommandAllocator).GUID;
            for (int i = 0; i < efgAlloc.Length; i++)
            {
                Hr.Check(dev.CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.DIRECT, ref iid, out o), "CreateCommandAllocator (Enhanced FG)");
                efgAlloc[i] = (ID3D12CommandAllocator)o;
            }
            iid = typeof(ID3D12GraphicsCommandList).GUID;
            Hr.Check(dev.CreateCommandList(0, D3D12_COMMAND_LIST_TYPE.DIRECT, efgAlloc[0], null, ref iid, out o), "CreateCommandList (Enhanced FG)");
            efgList = (ID3D12GraphicsCommandList)o;
            Hr.Check(efgList.Close(), "Close");
            D3D12_QUERY_HEAP_DESC qhd = new D3D12_QUERY_HEAP_DESC();
            qhd.Type = D3D12_QUERY_HEAP_TYPE.TIMESTAMP;
            qhd.Count = 4;
            iid = typeof(ID3D12QueryHeap).GUID;
            Hr.Check(dev.CreateQueryHeap(ref qhd, ref iid, out o), "CreateQueryHeap (Enhanced FG)");
            efgQheap = (ID3D12QueryHeap)o;
            efgTsBuf = MakeBuffer(D3D12_HEAP_TYPE.READBACK, 8 * 4, D3D12_RESOURCE_STATES.COPY_DEST, "Enhanced FG: tiempos");
            Hr.Check(efgTsBuf.Map(0, null, out efgTsPtr), "Map (Enhanced FG)");
            ulong tf; efgTsFreq = EfgPq.GetTimestampFrequency(out tf) >= 0 && tf > 0 ? tf : 1e7;
            efgWatch = new Thread(EfgWatchLoop);
            efgWatch.IsBackground = true;
            efgWatch.Name = "Enhanced FG (vigilante)";
            efgWatch.Priority = ThreadPriority.Highest;
            efgWatch.Start();
            efgThread = new Thread(EfgLoop);
            efgThread.IsBackground = true;
            efgThread.Name = "Enhanced FG";
            efgThread.Priority = ThreadPriority.Highest;
            efgThread.Start();
        }

        // Que el hilo acabe lo que tiene (antes de que el hilo de pintado presente el mismo, cambie la cadena o espere a la grafica entera)
        void EfgDrain()
        {
            if (efgThread == null) return;
            if (!efgIdle.WaitOne(600)) Log.Write("Enhanced FG: el hilo de presentacion no acaba (600 ms)");
            if (efgFence != null && efgFenceValue > 0) EfgFenceWait(efgFenceValue, 1000, efgDrainEv);   // (EFGFIX: su evento)
        }

        // ---------------------------------------------------------------------------------------------- listas
        void EfgBegin()
        {
            efgAi = (efgAi + 1) % efgAlloc.Length;
            ulong v = efgAllocFence[efgAi];
            // (EFGFIX) la lista de esta ranura tiene que estar ACABADA de verdad antes de Reset (antes, con la señal perdida o vieja, se podia
            // seguir con la grafica aun leyendola: memoria de la lista pisada). Hasta 3 s; si no, la grafica esta parada: fuera
            if (v != 0 && !EfgFenceWait(v, 3000, efgOwnEv))
                throw new Exception("la grafica no acaba la lista de hace " + efgAlloc.Length + " (Enhanced FG, 3 s)");
            Hr.Check(efgAlloc[efgAi].Reset(), "Allocator.Reset (Enhanced FG)");
            Hr.Check(efgList.Reset(efgAlloc[efgAi], null), "List.Reset (Enhanced FG)");
            efgList.SetDescriptorHeaps(1, ref efgHeapPtr);
            efgList.SetComputeRootSignature(rootSig);
        }

        ulong EfgSubmit()
        {
            Hr.Check(efgList.Close(), "Close (Enhanced FG)");
            IntPtr lp = Com.BorrowPtr(efgList, typeof(ID3D12CommandList));
            EfgPq.ExecuteCommandLists(1, ref lp);
            efgFenceValue++;
            EfgPq.Signal(efgFence, efgFenceValue);
            efgAllocFence[efgAi] = efgFenceValue;
            return efgFenceValue;
        }

        void EB(ID3D12Resource r, D3D12_RESOURCE_STATES a, D3D12_RESOURCE_STATES b)
        {
            D3D12_RESOURCE_BARRIER x = D3D12_RESOURCE_BARRIER.CreateTransition(r, a, b);
            efgList.ResourceBarrier(1, ref x);
        }

        void EfgRun(ID3D12PipelineState pso, int set, int tab, uint* c, uint gx, uint gy, ID3D12Resource dst)
        {
            if (dst != null) EB(dst, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            efgList.SetPipelineState(pso);
            efgList.SetComputeRootDescriptorTable(1, EfgGpu(set, tab));
            efgList.SetComputeRoot32BitConstants(0, 16, c, 0);
            efgList.Dispatch(gx, gy, 1);
            if (dst != null) EB(dst, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
        }

        static void EfgZero(uint* c) { for (int i = 0; i < 16; i++) c[i] = 0; }

        // La piramide de la real 'c' y, si hay anterior, el flujo en los dos sentidos, lo quieto y el corte (marcas 0-1)
        ulong EfgFlowList(int c, bool flow)
        {
            uint* k = stackalloc uint[16];
            int p = (c + EfgSets - 1) % EfgSets;
            EfgBegin();
            EfgStamp(0);
            EB(efgRing[c], D3D12_RESOURCE_STATES.COMMON, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            EfgZero(k); k[0] = (uint)efgLw[0]; k[1] = (uint)efgLh[0]; k[2] = (uint)outW; k[3] = (uint)outH;
            EfgRun(psoEfgLuma, c, EfgTabDown(0), k, Groups(efgLw[0], 16), Groups(efgLh[0], 8), efgL[c, 0]);
            EB(efgRing[c], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COMMON);
            for (int l = 1; l < efgLv; l++)
            {
                EfgZero(k); k[0] = (uint)efgLw[l]; k[1] = (uint)efgLh[l]; k[2] = (uint)efgLw[l - 1]; k[3] = (uint)efgLh[l - 1];
                EfgRun(psoEfgDown, c, EfgTabDown(l), k, Groups(efgLw[l], 16), Groups(efgLh[l], 8), efgL[c, l]);
            }
            if (flow)
            {
                bool tmp = efgFlowOk[p];
                int top = efgLv - 1;
                for (int l = top; l >= 0; l--)
                    for (int d = 0; d < 2; d++)
                    {
                        EfgZero(k);
                        k[0] = (uint)efgLw[l]; k[1] = (uint)efgLh[l]; k[2] = (uint)efgBx[l]; k[3] = (uint)efgBy[l];
                        k[4] = l == top ? (uint)s.EfgRadius : 1u;
                        k[5] = (l < top ? 1u : 0u) | (l == 0 && tmp ? 2u : 0u) | (l == 0 ? 8u : 0u);
                        k[6] = l < top ? (uint)efgBx[l + 1] : 1u; k[7] = l < top ? (uint)efgBy[l + 1] : 1u;
                        k[8] = F(s.EfgLambda); k[9] = F(0.002f); k[10] = F(d == 0 ? 1f : -1f);
                        k[12] = l == 0 ? 1u : (uint)s.EfgSearch;
                        EfgRun(psoEfgFlow, c, EfgTabFlow(d, l), k, Groups(efgBx[l], 8), Groups(efgBy[l], 8), l == 0 ? efgFRaw[c, d] : efgPropAll ? efgFRawL[d, l] : efgF[c, d, l]);
                        if (efgPropAll && l > 0)
                        {
                            // (EFG2) apoyo entre vecinos tambien en los niveles gruesos: los vectores sueltos no bajan a los finos
                            EfgZero(k); k[0] = (uint)efgLw[l]; k[1] = (uint)efgLh[l]; k[2] = (uint)efgBx[l]; k[3] = (uint)efgBy[l]; k[4] = F(s.EfgBeta);
                            EfgRun(psoEfgProp, c, EfgTabPropL(d, l), k, Groups(efgBx[l], 8), Groups(efgBy[l], 8), efgF[c, d, l]);
                        }
                    }
                for (int d = 0; d < 2; d++)
                {
                    EfgZero(k); k[0] = (uint)efgLw[0]; k[1] = (uint)efgLh[0]; k[2] = (uint)efgBx[0]; k[3] = (uint)efgBy[0]; k[4] = F(s.EfgBeta);
                    EfgRun(psoEfgProp, c, EfgTabProp(d), k, Groups(efgBx[0], 8), Groups(efgBy[0], 8), efgF[c, d, 0]);
                }
                EfgZero(k); k[0] = (uint)efgBx[0]; k[1] = (uint)efgBy[0]; k[4] = F(0.06f); k[5] = F(0.02f);
                EfgRun(psoEfgCut, c, EfgTabCut, k, 1, 1, efgCut[c]);
            }
            EfgZero(k); k[0] = (uint)efgLw[0]; k[1] = (uint)efgLh[0]; k[2] = flow && efgStillOk[p] ? 1u : 0u;
            k[4] = F(s.EfgStillDiff); k[5] = F(s.EfgStillGrad); k[6] = F(60f);
            EfgRun(psoEfgStill, c, EfgTabStill, k, Groups(efgLw[0], 16), Groups(efgLh[0], 8), efgSt[c]);
            EfgStamp(1);
            efgFlowOk[c] = flow; efgStillOk[c] = true;
            ulong v = EfgSubmit();
            SetSlotDone(c, v);
            return v;
        }

        void SetSlotDone(int c, ulong v) { Interlocked.Exchange(ref efgSlotDone[c], (long)v); }

        // Una inventada en el instante t (> 1 = hacia delante) a efgGen (marcas 2-3). dup = sin flujo: la real mas cercana en el tiempo
        ulong EfgGenList(int c, float t, bool dup, bool hasPrev, bool cutCpu)
        {
            uint* k = stackalloc uint[16];
            int p = (c + EfgSets - 1) % EfgSets;
            EfgBegin();
            EfgStamp(2);
            EB(efgRing[p], D3D12_RESOURCE_STATES.COMMON, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            EB(efgRing[c], D3D12_RESOURCE_STATES.COMMON, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            bool extrap = t > 1.0001f;
            if (!dup)
            {
                EfgZero(k);
                k[0] = (uint)efgQw; k[1] = (uint)efgQh; k[2] = (uint)efgBx[0]; k[3] = (uint)efgBy[0];
                k[4] = F(t); k[5] = F(s.EfgKappa); k[6] = F(s.EfgTau); k[7] = F(s.EfgStillN);
                k[8] = (uint)efgLw[0]; k[9] = (uint)efgLh[0]; k[10] = extrap ? 1u : 0u;
                k[12] = (uint)efgBx[1]; k[13] = (uint)efgBy[1]; k[14] = (uint)efgBx[3]; k[15] = (uint)efgBy[3];
                EfgRun(psoEfgSel, c, EfgTabSel, k, Groups(efgQw, 16), Groups(efgQh, 8), efgSel);
            }
            EfgZero(k);
            k[0] = (uint)outW; k[1] = (uint)outH; k[2] = (uint)efgQw; k[3] = (uint)efgQh;
            k[4] = F(hasPrev ? t : 1f); k[5] = F(s.EfgZeroPen); k[6] = F(s.EfgStillN); k[7] = F(s.EfgSigma);
            k[8] = extrap || !hasPrev ? 1u : 0u; k[9] = dup || cutCpu ? 1u : 0u; k[10] = (uint)efgLw[0]; k[11] = (uint)efgLh[0];
            k[12] = F(s.EfgCutThr); k[13] = F(s.EfgTau);
            EfgRun(psoEfgFin, c, EfgTabFin, k, Groups(outW, 16), Groups(outH, 8), null);
            EB(efgRing[p], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COMMON);
            EB(efgRing[c], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COMMON);
            D3D12_RESOURCE_BARRIER u = D3D12_RESOURCE_BARRIER.CreateUav(efgGen);
            efgList.ResourceBarrier(1, ref u);
            EfgStamp(3);
            EfgStampsResolve();
            ulong v = EfgSubmit();
            SetSlotDone(c, v); SetSlotDone(p, v);
            return v;
        }

        // La imagen a la cadena: la real (su ranura) o la inventada (efgGen)
        ulong EfgCopyList(int c, bool gen)
        {
            EfgBegin();
            ID3D12Resource src = gen ? efgGen : efgRing[c];
            D3D12_RESOURCE_STATES rest = gen ? D3D12_RESOURCE_STATES.UNORDERED_ACCESS : D3D12_RESOURCE_STATES.COMMON;
            EB(src, rest, D3D12_RESOURCE_STATES.COPY_SOURCE);
            ID3D12Resource bb = backbuf[swap.GetCurrentBackBufferIndex()];
            EB(bb, D3D12_RESOURCE_STATES.PRESENT, D3D12_RESOURCE_STATES.COPY_DEST);
            efgList.CopyResource(bb, src);
            EB(bb, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.PRESENT);
            EB(src, D3D12_RESOURCE_STATES.COPY_SOURCE, rest);
            ulong v = EfgSubmit();
            if (!gen) SetSlotDone(c, v);
            return v;
        }

        // ---------------------------------------------------------------------------------------------- espera
        bool EfgWaitOwn(ulong v, double maxMs) { return EfgFenceWait(v, maxMs, efgOwnEv); }

        // (1.2.4 ronda 7, EFGFIX) Espera a que la valla propia llegue a v (como mucho maxMs): vuelve a mirar la valla al despertar (una señal vieja
        // no vale) y apunta las esperas largas
        bool EfgFenceWait(ulong v, double maxMs, AutoResetEvent ev)
        {
            if (efgFence.GetCompletedValue() >= v) return true;
            long t0 = Clock.Now;
            while (true)
            {
                double left = maxMs - Clock.SinceMs(t0);
                if (left <= 0) break;
                efgFence.SetEventOnCompletion(v, ev.SafeWaitHandle.DangerousGetHandle());
                ev.WaitOne((int)Math.Max(1, Math.Ceiling(left)));
                if (efgFence.GetCompletedValue() >= v) { EfgEsperaLarga("la grafica (Enhanced FG)", Clock.SinceMs(t0)); return true; }
            }
            EfgEsperaLarga("la grafica (Enhanced FG), vencida", Clock.SinceMs(t0));
            return efgFence.GetCompletedValue() >= v;
        }

        // (EFGFIX) Para encontrar los paros: una espera de mas de 150 ms al log (como mucho una linea cada 2 s)
        long efgLargaAt;
        void EfgEsperaLarga(string que, double ms)
        {
            if (ms < 150 || (efgLargaAt != 0 && Clock.SinceMs(efgLargaAt) < 2000)) return;
            efgLargaAt = Clock.Now;
            Log.Write("Enhanced FG: espera larga de " + ms.ToString("0", IC) + " ms (" + que + ")");
        }

        static void EfgWaitUntil(long at)
        {
            while (true)
            {
                double left = -Clock.SinceMs(at);
                if (left <= 0) return;
                if (left > 1.6) Thread.Sleep(1); else Thread.SpinWait(200);
            }
        }

        static long EfgAt(long t, double ms) { return t + (long)(ms / Clock.TickMs); }

        double EfgReadyP90()
        {
            int n = Math.Min(efgReadyN, efgReadyWin.Length);
            if (n == 0) return 0;
            double[] a = new double[n];
            Array.Copy(efgReadyWin, a, n);
            Array.Sort(a);
            return a[Math.Min(n - 1, (int)(n * 0.9))];
        }

        // ---------------------------------------------------------------------------------------------- el vigilante
        void EfgWatchLoop()
        {
            while (!Program.Quit)
            {
                EfgJob j = null;
                lock (efgWatchQ) { if (efgWatchQ.Count > 0) j = efgWatchQ.Dequeue(); }
                if (j == null) { efgWatchEv.WaitOne(100); continue; }
                try
                {
                    if (fence.GetCompletedValue() < j.Fence)
                    {
                        fence.SetEventOnCompletion(j.Fence, efgWatchFenceEv.SafeWaitHandle.DangerousGetHandle());
                        efgWatchFenceEv.WaitOne(1000);
                    }
                }
                catch { }
                j.ReadyAt = Clock.Now;
                try { j.Ready.Set(); } catch { }
            }
        }

        void EfgStamp(uint i) { if (s.EfgStamps) efgList.EndQuery(efgQheap, D3D12_QUERY_TYPE.TIMESTAMP, i); if (i == 1) efgFlowStamped = true; }
        bool efgFlowStamped;
        void EfgStampsResolve() { if (s.EfgStamps && efgFlowStamped) efgList.ResolveQueryData(efgQheap, D3D12_QUERY_TYPE.TIMESTAMP, 0, 4, efgTsBuf, 0); }
        // (con las listas acabadas) lo que tardaron en la grafica el flujo y la ultima inventada
        void EfgStampsRead(bool flow, bool gen)
        {
            if (!s.EfgStamps || !efgFlowStamped) return;
            ulong* t = (ulong*)efgTsPtr;
            if (flow && t[1] > t[0]) { double ms = (t[1] - t[0]) * 1000.0 / efgTsFreq; if (ms < 50) efgFlowGpu = efgFlowGpu * 0.9 + ms * 0.1; }
            if (gen && t[3] > t[2]) { double ms = (t[3] - t[2]) * 1000.0 / efgTsFreq; if (ms < 50) efgGenGpu = efgGenGpu * 0.9 + ms * 0.1; }
        }

        // ---------------------------------------------------------------------------------------------- el hilo
        void EfgLoop()
        {
            IntPtr mm = MmcssOn("Games", "Enhanced FG", true);
            try
            {
                while (!Program.Quit)
                {
                    EfgJob j = null;
                    bool stale = false;
                    lock (efgJobs)
                    {
                        if (efgJobs.Count > 0) { j = efgJobs.Dequeue(); stale = efgJobs.Count > 0; }
                        else efgIdle.Set();
                    }
                    if (j == null) { efgJobEv.WaitOne(100); continue; }
                    try { EfgDoJob(j, stale); }
                    catch (Exception ex) { Log.Write("Enhanced FG: hilo de presentacion: " + ex.Message); if (Hr.IsDeviceLost(ex.HResult)) efgHr = ex.HResult; }
                }
            }
            finally
            {
                efgIdle.Set(); MmcssOff(mm);
                if (s.EfgCsv.Length > 0 && efgCsvBuf.Length > 0) try { File.AppendAllText(Program.DataPath(s.EfgCsv), efgCsvBuf.ToString()); } catch { }
            }
        }

        // (1.2.4 ronda 6, INTEGRA6) Solo enseñar la imagen de la ranura (repintado, Enhanced FG apagada en marcha, la espera): copia a la cadena
        // desde la cola propia y Present como lo pidio el bucle; sin inventar ni cambiar la "anterior" de la siguiente real
        void EfgDoShow(EfgJob j)
        {
            try
            {
                if (!j.Ready.WaitOne(1000)) Log.Write("Enhanced FG: la grafica no acaba la imagen a enseñar (1 s)");
                if (efgQueue != null) efgQueue.Wait(fence, j.Fence);
                ulong v = EfgCopyList(j.W, false);
                EfgWaitOwn(v, 100);
                int hr = swap.Present(j.Sync, (DXGI_PRESENT)j.Flags);
                efgLastPresent = Clock.Now;
                efgShows++;
                if (hr < 0) { Log.Write("Enhanced FG: Present (enseñar): " + Hr.Format(hr)); if (Hr.IsDeviceLost(hr)) efgHr = hr; }
            }
            finally { Interlocked.Decrement(ref efgShowPend); }
        }

        void EfgDoJob(EfgJob j, bool stale)
        {
            if (j.Show) { EfgDoShow(j); return; }
            // 1. la real en su ranura: la hora a la que la grafica la acabo (el vigilante)
            long tw = Clock.Now;
            if (!j.Ready.WaitOne(1000)) Log.Write("Enhanced FG: la grafica no acaba la real (1 s)");
            EfgEsperaLarga("la real del motor", Clock.SinceMs(tw));
            long tReady = j.ReadyAt != 0 ? j.ReadyAt : Clock.Now;
            if (efgQueue != null) efgQueue.Wait(fence, j.Fence);
            double readyMs = Clock.Ms(j.Arrival, tReady);
            efgReadyWin[efgReadyN % efgReadyWin.Length] = Math.Max(0, Math.Min(100, readyMs));
            efgReadyN++;
            int c = j.W;
            bool hasPrev = efgPrevSeq == j.Seq - 1;
            bool flow = hasPrev && !j.Reset;
            int k = j.K;
            double step = j.RealMs / k;
            int nReal = (int)Math.Round(j.Phase * k);
            nReal = Math.Max(0, Math.Min(k - 1, nReal));
            // sincronizacion: si k x las reales no caben con margen bajo el refresco; con VRR, cada una a su hora
            if (efgHzAt == 0 || Clock.SinceMs(efgHzAt) > 2000) { efgHz = DisplayHz(); efgHzAt = Clock.Now; }
            bool vs = s.EfgVsync == 1 || (s.EfgVsync < 0 && (j.Sync > 0 || (k * 1000.0 / j.RealMs) > (efgHz > 20 ? efgHz : 60) * 0.9));
            uint sy = vs ? 1u : 0u, fl = vs ? 0u : j.Flags;
            // 2. la hora de la primera (B) desde la llegada: lo que tarda la real (p90) + lo que hace falta para la primera + el margen
            double need0 = (nReal > 0 ? efgFlowGpu + efgGenGpu + 0.4 : 0) + efgCopyMs;
            double want = EfgReadyP90() + need0 + s.EfgMarginMs;
            if (efgLcur <= 0 || want > efgLcur) efgLcur = want; else efgLcur += (want - efgLcur) * 0.06;
            long B = EfgAt(j.Arrival, efgLcur);
            long early = EfgAt(Clock.Now, need0);
            if (B < early) { B = early; efgLate++; }
            if (efgLastPresent != 0 && B < EfgAt(efgLastPresent, step * 0.5)) B = EfgAt(efgLastPresent, step * 0.5);
            if (stale) B = EfgAt(Clock.Now, efgCopyMs - nReal * step);   // (detras viene otra: esta real ya, sin inventadas)
            // (1.2.4 ronda 7, EFGFIX) nunca mas lejos que una imagen real: una llegada con la hora mal (la fuente al despertar) dejaba el hilo
            // esperando sin presentar nada
            double ahead = Clock.Ms(Clock.Now, B);
            if (ahead > j.RealMs + 20)
            {
                EfgEsperaLarga("la hora de la primera iba a " + ahead.ToString("0", IC) + " ms: se trae", ahead);
                B = EfgAt(Clock.Now, need0 + s.EfgMarginMs);
            }
            // 3. por delante: con la real detras (calidad), el flujo y la primera inventada ya (la de cada siguiente, tras presentar la anterior)
            bool flowSent = false, genReady = false;
            if (nReal > 0)
            {
                EfgFlowList(c, flow); flowSent = true;
                if (!stale) { EfgGenList(c, (float)(1.0 - nReal / (double)k), !flow, hasPrev, j.Reset); genReady = true; }
            }
            for (int i = 0; i < k; i++)
            {
                bool real = i == nReal;
                float t = (float)(1.0 + (i - nReal) / (double)k);
                bool dup = !flow;
                bool skip = !real && (stale || !genReady);
                long ti = EfgAt(B, i * step);
                // (otra real esperando: lo de despues de esta sobra. 1.2.4 ronda 7, EFGFIX: solo si iria a la vez o despues de la primera de la
                // siguiente -su llegada + L-: la siguiente entra en la cola en cuanto el motor la manda, mucho antes de estar; con la baja latencia
                // x4 + DLSS 5 se saltaban 2 de cada 3 inventadas -49 de 90 por segundo con DLSS 5 al 45 %, app entera a 4K- y el vigilante de x4
                // bajaba DLSS 5 hasta el 25 %)
                if (!skip && !real && i > nReal)
                    lock (efgJobs)
                    {
                        if (efgJobs.Count > 0)
                        {
                            EfgJob nx = efgJobs.Peek();
                            if (nx.Show || !s.EfgSaltoFino || ti + (long)(step * 0.5 / Clock.TickMs) > EfgAt(nx.Arrival, efgLcur)) skip = true;
                        }
                    }
                if (!skip && !real && Clock.SinceMs(ti) > step * 0.5) skip = true;   // (ya es tarde para esta)
                if (skip) { efgSkipped++; genReady = false; }
                else
                {
                    EfgWaitUntil(EfgAt(ti, -(efgCopyMs + 0.5)));
                    long tSub = Clock.Now;
                    ulong v = EfgCopyList(c, !real);
                    bool ok = EfgWaitOwn(v, Math.Max(4, Clock.Ms(Clock.Now, ti) + 30));
                    if (ok) efgCopyMs = efgCopyMs * 0.9 + Math.Min(Clock.SinceMs(tSub), 10) * 0.1;
                    if (!real) EfgStampsRead(i == 0 && nReal > 0 && flow, !dup);
                    EfgWaitUntil(ti);
                    long call = Clock.Now;
                    int hr = swap.Present(sy, (DXGI_PRESENT)fl);
                    efgLastPresent = Clock.Now;
                    if (hr < 0) { Log.Write("Enhanced FG: Present: " + Hr.Format(hr)); if (Hr.IsDeviceLost(hr)) { efgHr = hr; return; } }
                    if (real) efgReals++; else if (dup) efgDups++; else efgGens++;
                    EfgCsvRow(j, i, real ? 'r' : dup ? 'd' : 'g', t, call, readyMs);
                    genReady = false;
                }
                // lo de la siguiente: el flujo (modo latencia: tras la real) y su inventada, mientras esta se ve
                if (!flowSent) { EfgFlowList(c, flow); flowSent = true; }
                if (i + 1 < k && i + 1 != nReal && !stale)
                {
                    EfgGenList(c, (float)(1.0 + (i + 1 - nReal) / (double)k), !flow, hasPrev, j.Reset);
                    genReady = true;
                }
            }
            if (!flowSent) EfgFlowList(c, flow);
            efgPrevSeq = j.Seq;
            EfgTick(j, k, nReal, vs);
        }

        void EfgCsvRow(EfgJob j, int i, char kind, float t, long call, double readyMs)
        {
            if (s.EfgCsv.Length == 0) return;
            efgCsvBuf.Append(kind).Append(';').Append(j.Seq.ToString(IC)).Append(';').Append(i.ToString(IC)).Append(';').Append(t.ToString("0.000", IC)).Append(';')
                     .Append(j.Arrival.ToString(IC)).Append(';').Append(call.ToString(IC)).Append(';').Append(readyMs.ToString("0.00", IC)).Append(';')
                     .Append(efgLcur.ToString("0.00", IC)).Append("\r\n");
            if (efgCsvAt == 0) efgCsvAt = Clock.Now;
            if (Clock.SinceMs(efgCsvAt) > 250 || efgCsvBuf.Length > 64 * 1024)
            {
                efgCsvAt = Clock.Now;
                try { File.AppendAllText(Program.DataPath(s.EfgCsv), efgCsvBuf.ToString()); } catch { }
                efgCsvBuf.Length = 0;
            }
        }

        void EfgTick(EfgJob j, int k, int nReal, bool vs)
        {
            if (efgLastLog == 0) efgLastLog = Clock.Now;
            if (Clock.SinceMs(efgLastLog) < 10000) return;
            efgLastLog = Clock.Now;
            Log.Write("Enhanced FG x" + k + " (" + EfgModeName() + ", la real en la " + (nReal + 1) + "a de " + k + (vs ? ", con sincronizacion" : ", sin sincronizacion") + "): reales " + efgReals +
                      ", inventadas " + efgGens + ", repetidas " + efgDups + ", saltadas " + efgSkipped + ", tarde " + efgLate + "; L " + efgLcur.ToString("0.0", IC) +
                      " ms (lista p90 " + EfgReadyP90().ToString("0.0", IC) + "), grafica: flujo " + efgFlowGpu.ToString("0.00", IC) + " ms, inventada " + efgGenGpu.ToString("0.00", IC) + " ms, copia " + efgCopyMs.ToString("0.00", IC) + " ms");
        }
    }
}
