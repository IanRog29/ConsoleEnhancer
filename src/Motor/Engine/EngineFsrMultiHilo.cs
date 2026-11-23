using System;
using System.Collections.Generic;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFsrMultiHilo.cs - 23-11-2026 (1.2.4 ronda 6, agente FGMULTI). El HILO DE PRESENTACION de FSR FG x2 / x3 / x4 del motor
    // (EngineFsrMulti.cs). Sale del de la generacion propia de la 1.2.4 (EngineFgPropiaHilo.cs, XEFGM), que se quita en esta ronda, con dos
    // cambios medidos: (1) la generacion va en una cola de calculo asincrono (no en la de la imagen) y (2) dos juegos de texturas, asi el hilo
    // de pintado no espera a que se presente lo de la real anterior para mandar lo de la siguiente.
    // Por cada real (un trabajo): espera (CPU) a que la cola de calculo acabe sus inventadas y las presenta cada intervalo real / k (copia a la
    // cadena en la cola de la imagen + Present) y luego la real, con la sincronizacion si hace falta (aqui esperar un refresco no frena el
    // pintado). Si la grafica acaba tarde, las inventadas se aprietan (hasta 3/4 del paso, con 4 ms de holgura) para no retrasar la real.
    unsafe partial class Engine
    {
        struct FmhJob { public int Set, K; public bool Gen; public double Step; public uint Sy, Fl, SyReal, FlReal; public ulong Cv, MainFence, Cv1, Cv2, Cv3; public long Arrival; }
        System.IO.StreamWriter fmhCsv;
        readonly Queue<FmhJob> fmhJobs = new Queue<FmhJob>();
        readonly object fmhLock = new object();
        Thread fmhThread;
        readonly AutoResetEvent fmhJobEv = new AutoResetEvent(false), fmhOwnEv = new AutoResetEvent(false), fmhCEv = new AutoResetEvent(false);
        readonly ManualResetEvent[] fmhFree = new ManualResetEvent[] { new ManualResetEvent(true), new ManualResetEvent(true) };   // por juego: sus copias ya mandadas
        readonly ManualResetEvent fmhIdle = new ManualResetEvent(true);   // sin nada (ni Present) a medias: el hilo de pintado puede presentar
        readonly ID3D12CommandAllocator[] fmhAlloc = new ID3D12CommandAllocator[8];
        readonly ulong[] fmhAllocFence = new ulong[8];
        int fmhAi;
        ID3D12GraphicsCommandList fmhList;
        ID3D12Fence fmhFence;
        ulong fmhFenceValue;
        volatile int fmhHr;
        double fmhGpuWaitMs;
        long fmhPrevReal;
        double fmhHz; long fmhHzAt;
        long fmhSqueezed, fmhSqueezedSaid, fmGensSaid;
        long fmhLag;   // (ticks) de la llegada de la ultima real con inventadas a su Present

        // (23-11: la copia a la cadena en una cola de COPIA aparte -para que no esperara a la grafica- hacia perder la grafica igual que
        // escribirla desde la cola de la imagen, DXGI_ERROR_DEVICE_REMOVED nada mas empezar: la cadena solo se escribe desde su cola)

        void FmhEnsure()
        {
            if (fmhThread != null) return;
            object o;
            Guid iid;
            D3D12_COMMAND_LIST_TYPE lt = D3D12_COMMAND_LIST_TYPE.DIRECT;
            iid = typeof(ID3D12CommandAllocator).GUID;
            for (int i = 0; i < fmhAlloc.Length; i++)
            {
                Hr.Check(dev.CreateCommandAllocator(lt, ref iid, out o), "CreateCommandAllocator (presentacion de FSR FG)");
                fmhAlloc[i] = (ID3D12CommandAllocator)o;
            }
            iid = typeof(ID3D12GraphicsCommandList).GUID;
            Hr.Check(dev.CreateCommandList(0, lt, fmhAlloc[0], null, ref iid, out o), "CreateCommandList (presentacion de FSR FG)");
            fmhList = (ID3D12GraphicsCommandList)o;
            Hr.Check(fmhList.Close(), "Close");
            iid = typeof(ID3D12Fence).GUID;
            Hr.Check(dev.CreateFence(0, D3D12_FENCE_FLAGS.NONE, ref iid, out o), "CreateFence (presentacion de FSR FG)");
            fmhFence = (ID3D12Fence)o;
            fmhThread = new Thread(FmhLoop);
            fmhThread.IsBackground = true;
            fmhThread.Name = "presentacion de FSR FG";
            fmhThread.Priority = ThreadPriority.Highest;
            fmhThread.Start();
        }

        bool FmhSetFree(int b, int ms) { return fmhFree[b].WaitOne(ms); }

        // Antes de que el hilo de pintado presente el mismo, cambie la cadena o espere a la grafica entera: que el de presentacion acabe
        void FmhDrain()
        {
            if (fmhThread == null) return;
            long t0 = Clock.Now;
            while (true)
            {
                int n;
                lock (fmhLock) n = fmhJobs.Count;
                if (n == 0 && fmhIdle.WaitOne(0)) return;
                if (Clock.SinceMs(t0) > 600) { Log.Write("FSR FG: el hilo de presentacion no acaba (600 ms)"); return; }
                fmhIdle.WaitOne(20);
            }
        }

        // Run, en vez de ScPresent cuando FmTakeReal se quedo la real: mandar lo de FSR a la cola de calculo y el trabajo al hilo
        int FmPresent(uint sync, uint flags)
        {
            if (fmhHr < 0) { int e = fmhHr; fmhHr = 0; fmHold = false; return e; }
            if (!fmHold) { FmhDrain(); return swap.Present(sync, (DXGI_PRESENT)flags); }
            fmHold = false;
            int k = FmK;
            double real = 1000.0 / Math.Max(1.0, (s.FpsNum > 0 ? s.FpsNum / (double)Math.Max(1, s.FpsDen) : 59.94) * (s.Mode == 30 ? 0.5 : 1));
            double step = real / k;
            if (fmhHzAt == 0 || Clock.SinceMs(fmhHzAt) > 2000) { fmhHz = DisplayHz(); fmhHzAt = Clock.Now; }
            bool vs = s.FsrMultiVsync == 1 || (s.FsrMultiVsync < 0 && (sync > 0 || (k * 1000.0 / real) > fmhHz * 0.9));
            uint sy = vs ? 1u : 0u, fl = vs ? 0u : flags;
            try
            {
                FmhEnsure();
                ulong cv = fmNoFsr ? 0 : FmSubmitGenerate(k, fenceValue);
                if (!fmNoFsr) fmFrames++;
                fmLastRealAt = Clock.Now;
                if (fmFrames % 300 == 0)
                {
                    Log.Write(string.Format(IC, "FSR FG x{0}: de 300 reales, sus inventadas listas {1:0.0} ms (media) tras mandarlas; apretadas {2}, inventadas {3}",
                        k, fmhGpuWaitMs, fmhSqueezed - fmhSqueezedSaid, fmGens - fmGensSaid));
                    fmhSqueezedSaid = fmhSqueezed; fmGensSaid = fmGens;
                }
                FmhJob j = new FmhJob();
                j.Set = fmSet; j.K = k; j.Gen = fmGen; j.Step = step; j.Sy = sy; j.Fl = fl; j.SyReal = sync; j.FlReal = flags; j.Cv = cv; j.MainFence = fenceValue; j.Arrival = fmArrival; j.Cv1 = fmCvs[0]; j.Cv2 = fmCvs[1]; j.Cv3 = fmCvs[2];
                fmhFree[fmSet].Reset();
                lock (fmhLock) { fmhJobs.Enqueue(j); fmhIdle.Reset(); }
                fmhJobEv.Set();
                return 0;
            }
            catch (Exception ex)
            {
                fmBroken = true;
                Log.Write("FSR FG x" + k + ": fallo (" + ex.Message + "): se sigue sin ella");
                fmhFree[fmSet].Set();
                return 0;
            }
        }

        static void FmWaitUntil(long at)
        {
            while (true)
            {
                double left = -Clock.SinceMs(at);
                if (left <= 0) return;
                if (left > 1.6) Thread.Sleep(1); else Thread.SpinWait(200);
            }
        }

        void FmhLoop()
        {
            while (!Program.Quit)
            {
                FmhJob job;
                bool have;
                lock (fmhLock) { have = fmhJobs.Count > 0; job = have ? fmhJobs.Dequeue() : new FmhJob(); if (!have) fmhIdle.Set(); }
                if (!have) { fmhJobEv.WaitOne(200); continue; }
                try
                {
                    bool gen = job.Gen; int k = job.K; double step = job.Step; int b = job.Set;
                    // las inventadas de esta real hechas en la cola de calculo (la real no espera a eso si no las hay)
                    long tw = Clock.Now;
                    ulong first = job.Cv1 != 0 ? job.Cv1 : job.Cv;   // (con una lista por inventada, basta la 1a para empezar)
                    if (gen && fmCFence.GetCompletedValue() < first)
                    {
                        fmCFence.SetEventOnCompletion(first, fmhCEv.SafeWaitHandle.DangerousGetHandle());
                        if (!fmhCEv.WaitOne(1000)) Log.Write("FSR FG: la grafica no acaba las inventadas (1 s)");
                    }
                    double wms = Clock.SinceMs(tw);
                    fmhGpuWaitMs = fmhGpuWaitMs * 0.95 + wms * 0.05;
                    long g = Clock.Now;
                    // la real tras las k-1 inventadas a su paso; si acabaron tarde la real no pasa de la cadencia (la anterior + un intervalo
                    // real + 4 ms): se aprietan las inventadas (hasta 3/4 del paso) en vez de retrasar la real
                    double realMs = step * k, full = gen ? (k - 1) * step : 0;
                    long R = g + (long)(full / Clock.TickMs);
                    if (fmhPrevReal != 0 && Clock.Ms(fmhPrevReal, g) < 3 * realMs)
                    {
                        long hi = fmhPrevReal + (long)((realMs + 4) / Clock.TickMs);
                        if (R > hi) { R = Math.Max(hi, g + (long)(0.75 * full / Clock.TickMs)); fmhSqueezed++; }
                    }
                    // sin inventadas (corte de escena, la 1a, con aviso) pero con la generacion puesta: la real con el MISMO retraso desde su
                    // llegada que las de antes (asi sigue la cadencia sin mover las siguientes); al momento salia pegada a la anterior (medido:
                    // dos reales a 0,6 ms y luego 33 ms sin nada) y a la anterior + un intervalo se retrasaba todo lo de despues (+3 ms)
                    if (!gen && k > 1 && fmhLag > 0 && job.Arrival > 0 && fmhPrevReal != 0 && Clock.Ms(fmhPrevReal, g) < 2 * realMs)
                        R = Math.Max(R, job.Arrival + fmhLag);
                    double s2 = gen && k > 1 ? Math.Min(step, Math.Max(0, Clock.Ms(g, R)) / (k - 1)) : 0;
                    int hr = 0;
                    if (gen)
                        for (int j = 1; j < k && hr >= 0; j++)
                        {
                            FmWaitUntil(R - (long)((k - j) * s2 / Clock.TickMs));
                            ulong need = j == 1 ? job.Cv1 : j == 2 ? job.Cv2 : job.Cv3;
                            if (need == 0) need = job.Cv;
                            if (fmCFence.GetCompletedValue() < need)
                            {
                                fmCFence.SetEventOnCompletion(need, fmhCEv.SafeWaitHandle.DangerousGetHandle());
                                if (!fmhCEv.WaitOne(1000)) Log.Write("FSR FG: la grafica no acaba la inventada " + j + " (1 s)");
                            }
                            hr = FmhCopyPresent(fmOut[b, j - 1], job.Sy, job.Fl, -1, job.Arrival, j, job.MainFence);
                            if (hr >= 0) fmGens++;
                        }
                    if (hr >= 0)
                    {
                        FmWaitUntil(R);
                        hr = FmhCopyPresent(fmReal[b], gen ? job.Sy : job.SyReal, gen ? job.Fl : job.FlReal, b, job.Arrival, 0, job.MainFence);
                        fmhPrevReal = R;
                        if (gen && job.Arrival > 0) fmhLag = R - job.Arrival;
                    }
                    if (hr < 0) fmhHr = hr;
                }
                catch (Exception ex) { Log.Write("FSR FG: hilo de presentacion: " + ex.Message); }
                finally { fmhFree[job.Set].Set(); }
            }
        }

        // (hilo de presentacion) src a la cadena y Present. freeSet >= 0 = la real: tras mandar su copia, ese juego ya se puede pisar
        int FmhCopyPresent(ID3D12Resource src, uint sy, uint fl, int freeSet, long arrival, int j, ulong mainFence)
        {
            fmhAi = (fmhAi + 1) % fmhAlloc.Length;
            ulong v = fmhAllocFence[fmhAi];
            if (v != 0 && fmhFence.GetCompletedValue() < v)
            {
                fmhFence.SetEventOnCompletion(v, fmhOwnEv.SafeWaitHandle.DangerousGetHandle());
                fmhOwnEv.WaitOne(1000);
            }
            Hr.Check(fmhAlloc[fmhAi].Reset(), "Allocator.Reset (presentacion de FSR FG)");
            Hr.Check(fmhList.Reset(fmhAlloc[fmhAi], null), "List.Reset (presentacion de FSR FG)");
            ID3D12Resource bb = backbuf[swap.GetCurrentBackBufferIndex()];
            D3D12_RESOURCE_BARRIER b = D3D12_RESOURCE_BARRIER.CreateTransition(bb, D3D12_RESOURCE_STATES.PRESENT, D3D12_RESOURCE_STATES.COPY_DEST);
            fmhList.ResourceBarrier(1, ref b);
            fmhList.CopyResource(bb, src);
            b = D3D12_RESOURCE_BARRIER.CreateTransition(bb, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.PRESENT);
            fmhList.ResourceBarrier(1, ref b);
            Hr.Check(fmhList.Close(), "Close (presentacion de FSR FG)");
            IntPtr lp = Com.BorrowPtr(fmhList, typeof(ID3D12CommandList));
            ID3D12CommandQueue q = ScQ;
            if (scQueue != null) q.Wait(fence, mainFence);   // (la real la copio la cola de la imagen; las inventadas, la de calculo: ya hechas)
            q.ExecuteCommandLists(1, ref lp);
            fmhFenceValue++;
            q.Signal(fmhFence, fmhFenceValue);
            fmhAllocFence[fmhAi] = fmhFenceValue;
            if (freeSet >= 0) fmhFree[freeSet].Set();
            long tp = Clock.Now;
            int hr = swap.Present(sy, (DXGI_PRESENT)fl);
            if (s.FsrMultiCsv.Length > 0)
            {
                try
                {
                    if (fmhCsv == null) { fmhCsv = new System.IO.StreamWriter(s.FsrMultiCsv, false); fmhCsv.WriteLine("tipo;j;llegada;present;vuelta;sync"); }
                    fmhCsv.WriteLine((j == 0 ? "r" : "i") + ";" + j + ";" + arrival + ";" + tp + ";" + Clock.Now + ";" + sy);
                    if (j == 0 && fmFrames % 60 == 0) fmhCsv.Flush();
                }
                catch { }
            }
            return hr;
        }

        string FmhBench() { return fmhThread != null ? " hilo=1 espera_inventadas=" + fmhGpuWaitMs.ToString("0.0", IC) + "ms apretadas=" + fmhSqueezed : ""; }
    }
}
