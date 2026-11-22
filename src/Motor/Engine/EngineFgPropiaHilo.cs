using System;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFgPropiaHilo.cs - 22-11-2026 noche (1.2.4, ronda de arreglos, agente XEFGM). La generacion propia x3 / x4 (EngineFgPropia.cs)
    // con un HILO DE PRESENTACION (fg_propia_hilo=1).
    // Lo que pasaba en la prueba del usuario (GTA VI a 4K, modo 30, DLSS 5 por PrismaNr, x4): "salida 50,8 de 119,9, reales 12,7 de 29,97"
    // y despues 22 reales/s con 107 perdidas en 20 s, con la grafica DENTRO del tope (peor 32,6 ms). FpPresent hacia todo en el hilo de
    // pintado: las 3 inventadas y la real, cada una a su hora y con la sincronizacion de 120 Hz (4 x 30 = 120: no cabe sin ella), asi que el
    // hilo se quedaba ~33 ms en los Present (cola llena: cada uno espera un refresco) y no le quedaba tiempo para la imagen siguiente
    // (DLSS 5, vectores...): se perdian reales.
    // Ahora: el hilo de pintado graba en UNA lista las k-1 inventadas (cada una en su textura) y la copia de la real, la manda y sigue con la
    // siguiente; el hilo de presentacion espera a que la grafica acabe esa lista, y entonces presenta las inventadas y la real cada
    // intervalo real / k (copia a la cadena + Present), con la sincronizacion si hace falta (ahi esperar un refresco no frena nada mas).
    // Las inventadas salen igual de separadas aunque la grafica tarde (antes, con DLSS 5, la 1a salia cuando la grafica acababa la real y
    // las otras detras: 26 / 1,7 / 5,9 ms medido por la integracion). Coste: 2 texturas mas (x3) o 3 (x4) de la salida y la copia de la real.
    // fg_propia_hilo=0: como antes (todo en el hilo de pintado).
    partial class Settings
    {
        public bool FgOwnThread = false;  // fg_propia_hilo=0|1 (EngineFgPropia.cs ApplyFgOwn)
    }

    unsafe partial class Engine
    {
        ID3D12Resource[] fphOut = new ID3D12Resource[3];
        ID3D12Resource fphReal;
        ID3D12DescriptorHeap fphHeap;
        IntPtr fphHeapPtr;
        D3D12_CPU_DESCRIPTOR_HANDLE fphCpu0;
        D3D12_GPU_DESCRIPTOR_HANDLE fphGpu0;
        ID3D12Resource fphTabPrev, fphTabCur, fphTabMv;
        int fphW, fphH;
        // el hilo y lo suyo (su lista, sus allocators, su valla y su evento: no toca los del hilo de pintado)
        Thread fphThread;
        readonly AutoResetEvent fphJobEv = new AutoResetEvent(false), fphFenceEv = new AutoResetEvent(false), fphOwnEv = new AutoResetEvent(false);
        readonly ManualResetEvent fphFree = new ManualResetEvent(true);   // sin trabajo a medias (lo que graba la siguiente real ya se puede pisar)
        readonly ManualResetEvent fphIdle = new ManualResetEvent(true);   // sin nada (ni Present) a medias: el hilo de pintado puede presentar
        ID3D12CommandAllocator[] fphAlloc = new ID3D12CommandAllocator[8];
        readonly ulong[] fphAllocFence = new ulong[8];
        int fphAi;
        ID3D12GraphicsCommandList fphList;
        ID3D12Fence fphFence;
        ulong fphFenceValue;
        // el trabajo de una real (lo pone el hilo de pintado con fphFree puesto)
        volatile bool fphJob;
        bool fphJGen; int fphJK; double fphJStep; uint fphJSy, fphJFl, fphJSyReal, fphJFlReal; ulong fphJReady;
        volatile int fphHr;               // el ultimo Present que fallo (lo devuelve el hilo de pintado)
        long fphLate;                     // (banco) reales que el hilo de presentacion empezo tarde
        double fphGpuWaitMs;              // (banco) lo que espero a la grafica de media

        void FphEnsure()
        {
            if (fphThread == null)
            {
                object o;
                Guid iid = typeof(ID3D12CommandAllocator).GUID;
                for (int i = 0; i < fphAlloc.Length; i++)
                {
                    Hr.Check(dev.CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.DIRECT, ref iid, out o), "CreateCommandAllocator (presentacion propia)");
                    fphAlloc[i] = (ID3D12CommandAllocator)o;
                }
                iid = typeof(ID3D12GraphicsCommandList).GUID;
                Hr.Check(dev.CreateCommandList(0, D3D12_COMMAND_LIST_TYPE.DIRECT, fphAlloc[0], null, ref iid, out o), "CreateCommandList (presentacion propia)");
                fphList = (ID3D12GraphicsCommandList)o;
                Hr.Check(fphList.Close(), "Close");
                iid = typeof(ID3D12Fence).GUID;
                Hr.Check(dev.CreateFence(0, D3D12_FENCE_FLAGS.NONE, ref iid, out o), "CreateFence (presentacion propia)");
                fphFence = (ID3D12Fence)o;
                D3D12_DESCRIPTOR_HEAP_DESC hd = new D3D12_DESCRIPTOR_HEAP_DESC();
                hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE.CBV_SRV_UAV;
                hd.NumDescriptors = (uint)(3 * Shaders.TableSize);
                hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.SHADER_VISIBLE;
                iid = typeof(ID3D12DescriptorHeap).GUID;
                Hr.Check(dev.CreateDescriptorHeap(ref hd, ref iid, out o), "CreateDescriptorHeap (presentacion propia)");
                fphHeap = (ID3D12DescriptorHeap)o;
                fphHeapPtr = Com.BorrowPtr(fphHeap, typeof(ID3D12DescriptorHeap));
                fphHeap.GetCPUDescriptorHandleForHeapStart(out fphCpu0);
                fphHeap.GetGPUDescriptorHandleForHeapStart(out fphGpu0);
                D3D12_SHADER_RESOURCE_VIEW_DESC sd = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(ColorFmt);
                D3D12_UNORDERED_ACCESS_VIEW_DESC ud = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(ColorFmt);
                for (int j = 0; j < 3; j++)
                {
                    for (int i = 0; i < U0; i++) dev.CreateShaderResourceView(null, &sd, fphCpu0.Offset(j * Shaders.TableSize + i, inc));
                    for (int i = U0; i < Shaders.TableSize; i++) dev.CreateUnorderedAccessView(null, null, &ud, fphCpu0.Offset(j * Shaders.TableSize + i, inc));
                }
                fphThread = new Thread(FphLoop);
                fphThread.IsBackground = true;
                fphThread.Name = "presentacion propia";
                fphThread.Priority = ThreadPriority.Highest;
                fphThread.Start();
                Log.Write("Generacion propia: con hilo de presentacion (las inventadas y la real a su hora sin parar el hilo de pintado; fg_propia_hilo=0 = sin el)");
            }
            if (fphReal == null || fphW != outW || fphH != outH)
            {
                FphDrain();
                WaitIdle();
                for (int j = 0; j < 3; j++) { if (fphOut[j] != null) Bury(fphOut[j]); fphOut[j] = null; }
                if (fphReal != null) Bury(fphReal);
                fphReal = Tex(ColorFmt, outW, outH, false, D3D12_RESOURCE_STATES.COPY_SOURCE, "generacion propia: la real para el hilo");
                fphW = outW; fphH = outH;
                fphTabPrev = fphTabCur = fphTabMv = null;
            }
            int need = Math.Max(1, Math.Min(3, s.FgOwn - 1));
            bool views = false;
            for (int j = 0; j < need; j++)
                if (fphOut[j] == null) { fphOut[j] = Tex(ColorFmt, outW, outH, true, D3D12_RESOURCE_STATES.COPY_SOURCE, "generacion propia: inventada " + (j + 1)); views = true; }
            if (views || !ReferenceEquals(fphTabPrev, fpPrev) || !ReferenceEquals(fphTabCur, final) || !ReferenceEquals(fphTabMv, mvec))
            {
                FphDrain();
                WaitIdle();   // (raro: al cambiar de tamaño; las tablas las puede estar leyendo una lista anterior)
                D3D12_SHADER_RESOURCE_VIEW_DESC sd = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(ColorFmt);
                D3D12_SHADER_RESOURCE_VIEW_DESC md = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(DXGI_FORMAT.R16G16_FLOAT);
                D3D12_UNORDERED_ACCESS_VIEW_DESC ud = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(ColorFmt);
                for (int j = 0; j < 3; j++)
                {
                    int b = j * Shaders.TableSize;
                    dev.CreateShaderResourceView(fpPrev, &sd, fphCpu0.Offset(b + 0, inc));
                    dev.CreateShaderResourceView(final, &sd, fphCpu0.Offset(b + 1, inc));
                    if (mvec != null) dev.CreateShaderResourceView(mvec, &md, fphCpu0.Offset(b + 2, inc));
                    if (fphOut[j] != null) dev.CreateUnorderedAccessView(fphOut[j], null, &ud, fphCpu0.Offset(b + U0, inc));
                }
                fphTabPrev = fpPrev; fphTabCur = final; fphTabMv = mvec;
            }
        }

        // Antes de que el hilo de pintado presente el mismo, cambie la cadena o espere a la grafica entera: que el de presentacion acabe
        void FphDrain()
        {
            if (fphThread == null) return;
            if (!fphIdle.WaitOne(500)) Log.Write("Generacion propia: el hilo de presentacion no acaba (500 ms)");
        }

        // Run (por FpPresent con fg_propia_hilo=1): grabar las inventadas y la real y dejarselas al hilo de presentacion
        int FpPresentHilo(uint sync, uint flags)
        {
            if (fphHr < 0) { int e = fphHr; fphHr = 0; fpHold = false; return e; }
            if (!fpHold) { FphDrain(); return swap.Present(sync, (DXGI_PRESENT)flags); }
            fpHold = false;
            int k = s.FgOwn;
            double real = 1000.0 / Math.Max(1.0, (s.FpsNum > 0 ? s.FpsNum / (double)Math.Max(1, s.FpsDen) : 59.94) * (s.Mode == 30 ? 0.5 : 1));
            double step = real / k;
            // sincronizacion: si k x las reales no caben con margen bajo el refresco (x4 de 30 en 120 Hz); con el hilo esperar un refresco
            // no frena el pintado. Sin ella (VRR), cada una a su hora
            bool vs = s.FgOwnVsync == 1 || (s.FgOwnVsync < 0 && (sync > 0 || (k * 1000.0 / real) > DisplayHz() * 0.9));
            uint sy = vs ? 1u : 0u, fl = vs ? 0u : flags;
            try
            {
                FphEnsure();
                // lo que graba esta real pisa las inventadas y la real de la anterior: que el hilo de presentacion ya las haya copiado
                if (!fphFree.WaitOne(250)) { Log.Write("Generacion propia: el hilo de presentacion va tarde (250 ms): se espera"); fphFree.WaitOne(1000); }
                FpBegin();
                if (fpGen)
                {
                    FpBarrier(final, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                    uint* c = stackalloc uint[16];
                    for (int j = 1; j < k; j++)
                    {
                        ID3D12Resource o = fphOut[j - 1];
                        FpBarrier(o, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                        for (int i = 0; i < 16; i++) c[i] = 0;
                        c[0] = (uint)outW; c[1] = (uint)outH; c[2] = (uint)inW; c[3] = (uint)inH;
                        c[4] = F((float)j / k); c[5] = F((float)outW / Math.Max(1, inW)); c[6] = F((float)outH / Math.Max(1, inH)); c[7] = F(s.FgOwnThr);
                        c[8] = (uint)s.FgOwnR; c[9] = s.FgOwnCands ? 1u : 0u;
                        fpList.SetDescriptorHeaps(1, ref fphHeapPtr);
                        fpList.SetComputeRootSignature(rootSig);
                        fpList.SetPipelineState(psoFp);
                        fpList.SetComputeRootDescriptorTable(1, fphGpu0.Offset((j - 1) * Shaders.TableSize, inc));
                        fpList.SetComputeRoot32BitConstants(0, 12, c, 0);
                        fpList.Dispatch(Groups(outW, 16), Groups(outH, 8), 1);
                        FpBarrier(o, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.COPY_SOURCE);
                    }
                    FpBarrier(final, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_SOURCE);
                }
                // la real (con su aviso si lo hay) para el hilo y, para la siguiente, guardada como anterior
                FpBarrier(fphReal, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.COPY_DEST);
                fpList.CopyResource(fphReal, fpShow ?? final);
                FpBarrier(fphReal, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.COPY_SOURCE);
                FpBarrier(fpPrev, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_DEST);
                fpList.CopyResource(fpPrev, final);
                FpBarrier(fpPrev, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                FpSubmit();
                fpPrevValid = true;
                fpFrames++;
                fpLastRealAt = Clock.Now;
                // al hilo
                fphJGen = fpGen; fphJK = k; fphJStep = step; fphJSy = sy; fphJFl = fl; fphJSyReal = sync; fphJFlReal = flags; fphJReady = fpAllocFence[fpAi];
                fphFree.Reset(); fphIdle.Reset();
                fphJob = true;
                fphJobEv.Set();
                return 0;
            }
            catch (Exception ex)
            {
                fpBroken = true;
                Log.Write("Generacion propia (con hilo): fallo (" + ex.Message + "): se sigue sin ella");
                fphFree.Set(); fphIdle.Set();
                return 0;
            }
        }

        void FphLoop()
        {
            while (!Program.Quit)
            {
                if (!fphJobEv.WaitOne(200)) continue;
                if (!fphJob) continue;
                fphJob = false;
                try
                {
                    bool gen = fphJGen; int k = fphJK; double step = fphJStep;
                    // la grafica: esta real, sus inventadas y su copia hechas (asi salen separadas lo mismo aunque la grafica tarde)
                    long tw = Clock.Now;
                    if (fence.GetCompletedValue() < fphJReady)
                    {
                        fence.SetEventOnCompletion(fphJReady, fphFenceEv.SafeWaitHandle.DangerousGetHandle());
                        if (!fphFenceEv.WaitOne(1000)) Log.Write("Generacion propia: la grafica no acaba la imagen (1 s)");
                    }
                    double wms = Clock.SinceMs(tw);
                    fphGpuWaitMs = fphGpuWaitMs * 0.95 + wms * 0.05;
                    long t0 = Clock.Now;
                    int hr = 0;
                    if (gen)
                        for (int j = 1; j < k && hr >= 0; j++)
                        {
                            if (j > 1) FpWaitUntil(t0 + (long)((j - 1) * step / Clock.TickMs));
                            hr = FphCopyPresent(fphOut[j - 1], fphJSy, fphJFl, false);
                            if (hr >= 0) fpGens++;
                        }
                    if (hr >= 0)
                    {
                        if (gen) FpWaitUntil(t0 + (long)((k - 1) * step / Clock.TickMs));
                        hr = FphCopyPresent(fphReal, gen ? fphJSy : fphJSyReal, gen ? fphJFl : fphJFlReal, true);
                    }
                    if (hr < 0) { fphHr = hr; fphFree.Set(); }
                }
                catch (Exception ex) { Log.Write("Generacion propia: hilo de presentacion: " + ex.Message); fphFree.Set(); }
                finally { fphIdle.Set(); }
            }
        }

        // (hilo de presentacion) src a la cadena y Present. last = la real: tras grabarla, lo de esta real ya se puede pisar (fphFree)
        int FphCopyPresent(ID3D12Resource src, uint sy, uint fl, bool last)
        {
            fphAi = (fphAi + 1) % fphAlloc.Length;
            ulong v = fphAllocFence[fphAi];
            if (v != 0 && fphFence.GetCompletedValue() < v)
            {
                fphFence.SetEventOnCompletion(v, fphOwnEv.SafeWaitHandle.DangerousGetHandle());
                fphOwnEv.WaitOne(1000);
            }
            Hr.Check(fphAlloc[fphAi].Reset(), "Allocator.Reset (presentacion propia)");
            Hr.Check(fphList.Reset(fphAlloc[fphAi], null), "List.Reset (presentacion propia)");
            ID3D12Resource bb = backbuf[swap.GetCurrentBackBufferIndex()];
            D3D12_RESOURCE_BARRIER b = D3D12_RESOURCE_BARRIER.CreateTransition(bb, D3D12_RESOURCE_STATES.PRESENT, D3D12_RESOURCE_STATES.COPY_DEST);
            fphList.ResourceBarrier(1, ref b);
            fphList.CopyResource(bb, src);
            b = D3D12_RESOURCE_BARRIER.CreateTransition(bb, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.PRESENT);
            fphList.ResourceBarrier(1, ref b);
            Hr.Check(fphList.Close(), "Close (presentacion propia)");
            IntPtr lp = Com.BorrowPtr(fphList, typeof(ID3D12CommandList));
            queue.ExecuteCommandLists(1, ref lp);
            fphFenceValue++;
            queue.Signal(fphFence, fphFenceValue);
            fphAllocFence[fphAi] = fphFenceValue;
            if (last) fphFree.Set();
            return swap.Present(sy, (DXGI_PRESENT)fl);
        }

        string FphBench() { return fphThread != null ? " hilo=1 espera_grafica=" + fphGpuWaitMs.ToString("0.0", IC) + "ms" : ""; }
    }
}
