using System;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 16-11 (1440p con generacion): la imagen de la captura pasa a la memoria de la grafica con el motor de copia (DMA, en su propia
    // cola: va a la vez que lo que la grafica aun hace de la imagen anterior, p. ej. la generacion) y la descompresion lee de alli.
    // Antes el shader de descompresion leia la memoria de subida (RAM) por PCIe: medido 1,9-2,0 ms de la cola de la imagen a 1440p
    // (11 MB a ~5,8 GB/s) y 1,0 ms a 1080p, y a 1440p con la generacion no cabia. copia_asincrona=0: como antes.
    // Sincronizacion: copia en la cola de copia -> valla -> la cola de la imagen espera esa valla antes de su lista. Los bufferes de la
    // grafica estan en COMMON (los bufferes pasan solos a COPY_DEST en la copia y a lectura en la imagen, y vuelven a COMMON al acabar
    // cada lista). Una ranura no se vuelve a copiar hasta que su imagen ha terminado (slotFence): sin carreras con la lectura.
    unsafe partial class Engine
    {
        ID3D12CommandQueue copyQueue;
        readonly ID3D12CommandAllocator[] copyAlloc = new ID3D12CommandAllocator[FramesInFlight];
        ID3D12GraphicsCommandList copyList;
        ID3D12Fence copyFence;
        ulong copyFenceValue;
        readonly ID3D12Resource[] vramIn = new ID3D12Resource[Slots];
        bool copyOn;                // la cola de copia esta lista y se usa
        ulong copyWaitValue;        // la valla de copia de la imagen que se graba (0 = sin copia)

        // Tras InitDevice
        void InitCopy()
        {
            if (!s.AsyncCopy) { Log.Write("Captura: la descompresion lee de la memoria de subida (copia_asincrona=0)"); return; }
            try
            {
                object o;
                D3D12_COMMAND_QUEUE_DESC qd = new D3D12_COMMAND_QUEUE_DESC();
                qd.Type = D3D12_COMMAND_LIST_TYPE.COPY;
                qd.Priority = 100; // HIGH (como la de la imagen)
                Guid iid = typeof(ID3D12CommandQueue).GUID;
                Hr.Check(dev.CreateCommandQueue(ref qd, ref iid, out o), "CreateCommandQueue copia");
                copyQueue = (ID3D12CommandQueue)o;
                iid = typeof(ID3D12CommandAllocator).GUID;
                for (int i = 0; i < FramesInFlight; i++)
                {
                    Hr.Check(dev.CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.COPY, ref iid, out o), "CreateCommandAllocator copia");
                    copyAlloc[i] = (ID3D12CommandAllocator)o;
                }
                iid = typeof(ID3D12GraphicsCommandList).GUID;
                Hr.Check(dev.CreateCommandList(0, D3D12_COMMAND_LIST_TYPE.COPY, copyAlloc[0], null, ref iid, out o), "CreateCommandList copia");
                copyList = (ID3D12GraphicsCommandList)o;
                Hr.Check(copyList.Close(), "Close copia");
                iid = typeof(ID3D12Fence).GUID;
                Hr.Check(dev.CreateFence(0, D3D12_FENCE_FLAGS.NONE, ref iid, out o), "CreateFence copia");
                copyFence = (ID3D12Fence)o;
                copyOn = true;
                Log.Write("Captura: a la grafica con el motor de copia (cola propia) y descompresion desde la memoria de la grafica");
                InitCopyBands();   // 20-11 (LATENCIA7): copia_bandas
            }
            catch (Exception ex)
            {
                Log.Error("Cola de copia (se lee de la memoria de subida, como antes)", ex);
                copyOn = false;
            }
        }

        // EnsureInputResources, al (re)crear las ranuras: el buffer de la grafica de la ranura y su vista para la descompresion.
        // false = sin copia asincrona (la vista va a la memoria de subida, como antes)
        bool SlotVram(int i, long size)
        {
            if (!copyOn) return false;
            if (vramIn[i] != null) Com.Release(ref vramIn[i]);
            vramIn[i] = MakeBuffer(D3D12_HEAP_TYPE.DEFAULT, size, D3D12_RESOURCE_STATES.COMMON, "captura en la grafica " + i);
            SrvRaw(TUnpack + i, 0, vramIn[i], size);
            return true;
        }

        // Bucle de pintado, con la ranura elegida y su lista libre (tras WaitFence(allocFence[fi])): la copia ya, en su cola
        void CopySlot(int slot, int fi)
        {
            copyWaitValue = 0;
            copyWaitCap = false;   // 20-11 (LATENCIA7)
            cbN = 0;               // 20-11 (LATENCIA7)
            if (slot >= 0 && slotCapWait[slot] != 0) { copyWaitValue = slotCapWait[slot]; copyWaitCap = true; cbBase = slotCapBase[slot]; cbN = slotCapN[slot]; return; }   // 20-11 (LATENCIA7): ya subida por bandas (CopyBands)
            if (!copyOn || slot < 0 || vramIn[slot] == null) return;
            int h = slotH[slot];
            long bytes = (long)slotPitch[slot] * (slotFmt[slot] == Shaders.FmtNv12 ? h + h / 2 : h);
            if (bytes <= 0 || bytes > slotBytes) return;
            Hr.Check(copyAlloc[fi].Reset(), "Allocator.Reset copia");
            Hr.Check(copyList.Reset(copyAlloc[fi], null), "List.Reset copia");
            copyList.CopyBufferRegion(vramIn[slot], 0, upload[slot], 0, (ulong)bytes);
            Hr.Check(copyList.Close(), "Close copia");
            IntPtr lp = Com.BorrowPtr(copyList, typeof(ID3D12CommandList));
            copyQueue.ExecuteCommandLists(1, ref lp);
            copyFenceValue++;
            copyQueue.Signal(copyFence, copyFenceValue);
            copyWaitValue = copyFenceValue;
        }

        // RenderFrame, justo antes de enviar su lista: la cola de la imagen espera a la copia de esta imagen
        void CopyWait()
        {
            if (copyWaitValue == 0) return;
            queue.Wait(copyWaitCap ? capFence : copyFence, copyWaitValue);   // (20-11 LATENCIA7: o la de las bandas)
            copyWaitValue = 0;
        }

        // En espera (ReleaseSessionResources): fuera con las ranuras (al despertar se rehacen con EnsureInputResources)
        void ReleaseCopySlots()
        {
            CapIdle();   // 20-11 (LATENCIA7): sin subidas por bandas en marcha
            for (int i = 0; i < Slots; i++) if (vramIn[i] != null) Com.Release(ref vramIn[i]);
        }

        // ---- 20-11 (LATENCIA7) copia_bandas=N: la imagen de la captura pasa a la memoria de subida y de ahi a la de la grafica POR BANDAS
        // y desde el hilo de la captura: mientras la CPU copia la banda k + 1, el motor de copia ya sube la k; y el hilo de pintado coge la
        // imagen en cuanto esta la primera banda (graba su lista mientras llegan las demas: la grafica espera a la ultima con la valla de
        // las bandas). Antes, todo seguido: la copia de la CPU entera (4K 1,0-1,5 ms) -> el hilo de pintado coge la imagen -> la subida
        // entera (4K ~1,2-1,9 ms, PCIe 4.0 x8) -> la grafica empieza. Bandas de 2 MB o mas (CopyRows las reparte en hilos). 0 = como antes
        ID3D12CommandQueue capQueue;                         // cola de copia propia del hilo de la captura (la otra es del hilo de pintado)
        readonly ID3D12CommandAllocator[] capAlloc = new ID3D12CommandAllocator[Slots];
        ID3D12GraphicsCommandList capList;
        ID3D12Fence capFence;
        ulong capFenceValue;                                 // (hilo de la captura) la ultima valla pedida
        readonly ulong[] capSlotDone = new ulong[Slots];     // (hilo de la captura) la valla de la ultima subida de cada ranura (su allocator)
        readonly ulong[] slotCapWait = new ulong[Slots];     // (slotLock) la valla que espera la imagen de la ranura; 0 = la sube CopySlot
        readonly AutoResetEvent capEvent = new AutoResetEvent(false), capIdleEvent = new AutoResetEvent(false);   // (una por hilo)
        volatile int capBusy;                               // el hilo de la captura esta copiando / subiendo bandas
        bool copyWaitCap;                                    // la imagen que se graba espera a capFence (y no a copyFence)
        volatile int capBandsUsed;
        readonly ulong[] slotCapBase = new ulong[Slots];     // (slotLock) bandas_grafica: la banda b de la ranura esta subida en capFence >= base + b + 1
        readonly int[] slotCapN = new int[Slots];            // (slotLock) y cuantas bandas
        ulong cbBase; int cbN;                               // las de la imagen que se graba (CopySlot -> BandsGpu); cbN 0 = no por bandas

        void InitCopyBands()
        {
            try
            {
                object o;
                D3D12_COMMAND_QUEUE_DESC qd = new D3D12_COMMAND_QUEUE_DESC();
                qd.Type = D3D12_COMMAND_LIST_TYPE.COPY;
                qd.Priority = 100; // HIGH
                Guid iid = typeof(ID3D12CommandQueue).GUID;
                Hr.Check(dev.CreateCommandQueue(ref qd, ref iid, out o), "CreateCommandQueue bandas");
                ID3D12CommandQueue q = (ID3D12CommandQueue)o;
                iid = typeof(ID3D12CommandAllocator).GUID;
                for (int i = 0; i < Slots; i++)
                {
                    Hr.Check(dev.CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.COPY, ref iid, out o), "CreateCommandAllocator bandas");
                    capAlloc[i] = (ID3D12CommandAllocator)o;
                }
                iid = typeof(ID3D12GraphicsCommandList).GUID;
                Hr.Check(dev.CreateCommandList(0, D3D12_COMMAND_LIST_TYPE.COPY, capAlloc[0], null, ref iid, out o), "CreateCommandList bandas");
                capList = (ID3D12GraphicsCommandList)o;
                Hr.Check(capList.Close(), "Close bandas");
                iid = typeof(ID3D12Fence).GUID;
                Hr.Check(dev.CreateFence(0, D3D12_FENCE_FLAGS.NONE, ref iid, out o), "CreateFence bandas");
                capFence = (ID3D12Fence)o;
                capQueue = q;
                if (s.CopyBands >= 2) Log.Write("Captura: copia y subida por bandas desde el hilo de la captura (copia_bandas=" + s.CopyBands + ")");
            }
            catch (Exception ex) { Log.Error("Cola de copia por bandas (se sube entera, como antes)", ex); capQueue = null; }
        }

        // OnFrame (hilo de la captura), con la ranura en Writing. true = copiada, subida y lista aqui; false = lo de siempre
        bool CopyBands(int slot, byte* src, int pitch, int dstPitch, int rowBytes, int rows, int w, int h, int fmt, long seq, long arrival, bool cut, long tCopy)
        {
            long total = (long)dstPitch * rows;
            int n = (int)Math.Min(s.CopyBands, total / (2L << 20));   // bandas de 2 MB o mas
            if (n < 2 || capQueue == null || !copyOn || vramIn[slot] == null || total > slotBytes) { lock (slotLock) slotCapWait[slot] = 0; return false; }
            capBusy = 1;
            ulong target = 0;
            bool ready = false;
            try
            {
                // El allocator de la ranura: su subida anterior ya acabo (siempre, salvo una imagen tirada hace nada)
                ulong prev = capSlotDone[slot];
                if (prev != 0 && capFence.GetCompletedValue() < prev)
                {
                    capFence.SetEventOnCompletion(prev, capEvent.SafeWaitHandle.DangerousGetHandle());
                    if (!capEvent.WaitOne(1000)) Log.Write("Copia por bandas: la subida anterior de la ranura " + slot + " no acaba");
                }
                Hr.Check(capAlloc[slot].Reset(), "Allocator.Reset bandas");
                ulong fbase = capFenceValue;   // la banda b queda subida en fbase + b + 1 (bandas_grafica); la ultima = target
                target = fbase + (ulong)n;
                IntPtr lp = Com.BorrowPtr(capList, typeof(ID3D12CommandList));
                byte* dst = (byte*)uploadPtr[slot];
                int r0 = 0;
                for (int b = 0; b < n; b++)
                {
                    int r1 = (int)((long)rows * (b + 1) / n);
                    CopyRows(src + (long)r0 * pitch, pitch, dst + (long)r0 * dstPitch, dstPitch, rowBytes, r1 - r0);
                    Hr.Check(capList.Reset(capAlloc[slot], null), "List.Reset bandas");
                    capList.CopyBufferRegion(vramIn[slot], (ulong)r0 * (ulong)dstPitch, upload[slot], (ulong)r0 * (ulong)dstPitch, (ulong)(r1 - r0) * (ulong)dstPitch);
                    Hr.Check(capList.Close(), "Close bandas");
                    capQueue.ExecuteCommandLists(1, ref lp);
                    capQueue.Signal(capFence, fbase + (ulong)b + 1);
                    if (b == 0)
                    {
                        // Lista ya: el hilo de pintado la graba mientras se copian y suben las demas (la grafica espera a target)
                        lock (slotLock)
                        {
                            slotSeq[slot] = seq; slotArrival[slot] = arrival; slotFmt[slot] = fmt;
                            slotW[slot] = w; slotH[slot] = h; slotPitch[slot] = dstPitch; slotUv[slot] = dstPitch * h; slotCut[slot] = cut;
                            slotCapWait[slot] = target; slotCapBase[slot] = fbase; slotCapN[slot] = rows == h ? n : 0;   // (bandas_grafica: no con NV12)
                            GiroSlot(slot);   // 20-11 (fusion GIRO + LATENCIA7): el giro de la camara tambien con la copia por bandas (si no, mvglobal no veia el giro a 4K)
                            slotState[slot] = Ready;
                        }
                        ready = true;
                        frameEvent.Set();
                    }
                    r0 = r1;
                }
            }
            catch (Exception ex) { Log.Error("Copia por bandas", ex); }
            finally
            {
                // La valla siempre (si no, la grafica esperaria para siempre a una imagen ya en su lista)
                if (target != 0) { try { capQueue.Signal(capFence, target); } catch { } capFenceValue = target; capSlotDone[slot] = target; }
                capBusy = 0;
            }
            if (!ready) { lock (slotLock) slotCapWait[slot] = 0; return false; }   // (fallo antes de la primera banda: lo de siempre)
            capBandsUsed = n;
            if (stats.Recording) LatOnFrame(Clock.Ms(arrival, tCopy), Clock.SinceMs(tCopy));
            return true;
        }

        // ---- 20-11 (LATENCIA7) bandas_grafica=1 (con copia_bandas): en RenderFrame, la descompresion y los bordes (s2_aa) de cada banda en
        // cuanto esta subida: la lista va a la grafica a trozos, cada uno tras la valla de su banda (queue.Wait), y los bordes de la banda
        // b van con la descompresion de la b + 1 (leen hasta ~24 filas por debajo). Asi, cuando acaba la subida de la ultima banda (lo que
        // manda: PCIe), solo queda lo de esa banda. El tiempo de la grafica de la imagen (gpu_ms, el de AutoPreset y el guardian) se cuenta
        // desde el trozo de la ultima banda mas lo que trabajaron los de antes (BandsBusyMs; sin las esperas a las subidas). false = la
        // imagen entera, como siempre
        bool BandsGpu(int slot, int fi, uint* c)
        {
            int n = cbN;
            if (!s.BandsGpu || n < 2 || copyWaitValue == 0 || !copyWaitCap || rawSmall != null || slotFmt[slot] == Shaders.FmtNv12 || slotW[slot] != inW || slotH[slot] != inH) return false;
            int w = slotW[slot], h = slotH[slot];
            if (bqHeap == null)
            {
                D3D12_QUERY_HEAP_DESC qhd = new D3D12_QUERY_HEAP_DESC();
                qhd.Type = D3D12_QUERY_HEAP_TYPE.TIMESTAMP;
                qhd.Count = FramesInFlight * BqPer;
                object o;
                Guid iid = typeof(ID3D12QueryHeap).GUID;
                Hr.Check(dev.CreateQueryHeap(ref qhd, ref iid, out o), "CreateQueryHeap bandas");
                bqHeap = (ID3D12QueryHeap)o;
                bqRead = MakeBuffer(D3D12_HEAP_TYPE.READBACK, 8 * FramesInFlight * BqPer, D3D12_RESOURCE_STATES.COPY_DEST, "tiempos de las bandas");
                Hr.Check(bqRead.Map(0, null, out bqPtr), "Map tiempos de las bandas");
            }
            int nq = Math.Min(n - 1, BqPer / 2);   // (los trozos antes del de la ultima banda: su trabajo se suma al tiempo de la grafica)
            for (int b = 0; b <= n; b++)
            {
                if (b < nq) list.EndQuery(bqHeap, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * BqPer + 2 * b));
                if (b < n)
                {
                    int y0 = h * b / n, y1 = h * (b + 1) / n;
                    if (b == n - 1)
                    {
                        list.EndQuery(qheap, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * 2));   // (otra vez: el tiempo de la grafica, desde aqui)
                        PassMark(0);
                    }
                    Barrier(rawColor, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                    c[0] = (uint)w; c[1] = (uint)y1; c[2] = (uint)slotPitch[slot]; c[3] = (uint)slotFmt[slot];
                    c[4] = 1; c[5] = 0; c[6] = (uint)slotUv[slot]; c[7] = (uint)y0;   // (H = la ultima fila + 1, Pad0 = la primera)
                    c[8] = (uint)s.Chroma; c[9] = c[10] = c[11] = 0;   // 21-11 (croma=)
                    BindCompute(psoUnpack, TUnpack + slot, c, 12);
                    list.Dispatch(Groups((w + 3) / 4, 16), Groups(y1 - y0, 8), 1);
                    Barrier(rawColor, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                    if (b == n - 1) PassMark(1);
                }
                if (b > 0) BandAa(h * (b - 1) / n, h * b / n, c);
                if (b < nq) list.EndQuery(bqHeap, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * BqPer + 2 * b + 1));
                if (b < n)
                {
                    // Este trozo, tras la subida de su banda
                    Hr.Check(list.Close(), "Close (banda)");
                    IntPtr lp = Com.BorrowPtr(list, typeof(ID3D12CommandList));
                    queue.Wait(capFence, cbBase + (ulong)b + 1);
                    queue.ExecuteCommandLists(1, ref lp);
                    Hr.Check(list.Reset(alloc[fi], null), "List.Reset (banda)");
                }
            }
            copyWaitValue = 0;   // (ya esperada banda a banda)
            if (nq > 0) list.ResolveQueryData(bqHeap, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * BqPer), (uint)(2 * nq), bqRead, (ulong)(fi * BqPer * 8));
            bqN[fi] = nq;
            PassMark(2);
            DumpRaw();
            return true;
        }
        const int BqPer = 32;
        ID3D12QueryHeap bqHeap;
        ID3D12Resource bqRead;
        IntPtr bqPtr;
        readonly int[] bqN = new int[FramesInFlight];

        // Run, con la lista fi ya acabada: lo que la grafica trabajo en los trozos de las bandas de antes de la ultima (se suma al tiempo de
        // la grafica de la imagen, que BandsGpu cuenta desde la ultima banda: asi AutoPreset, el guardian y los tirones lo ven todo)
        double BandsBusyMs(int fi)
        {
            int nq = bqN[fi];
            bqN[fi] = 0;
            if (nq <= 0 || bqPtr == IntPtr.Zero) return 0;
            ulong* t = (ulong*)bqPtr + fi * BqPer;
            double sum = 0;
            for (int i = 0; i < nq; i++) if (t[2 * i + 1] > t[2 * i]) sum += (t[2 * i + 1] - t[2 * i]) * 1000.0 / tsFreq;
            return sum;
        }

        void BandAa(int y0, int y1, uint* c)
        {
            Barrier(inColor, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = DeditherNow ? 1u : 0u; c[3] = AaNow ? 1u : 0u;   // (23-11 DLSSSR: con el escalador temporal sin DLSS 5, sr_liso / sr_aa / sr_tramado; EngineSrVec.cs)
            c[4] = (uint)Math.Max(0, Math.Min(80, (int)Math.Round(FlatNow * 10)));
            c[5] = (uint)y0; c[6] = (uint)y1;
            BindCompute(psoAa, TAa, c, 7);
            list.Dispatch(Groups(inW, 16), Groups(y1 - y0, 8), 1);
            Barrier(inColor, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
        }

        // Antes de soltar o rehacer las ranuras: sin bandas copiandose ni subiendo
        void CapIdle()
        {
            if (capQueue == null) return;
            for (int spin = 0; spin < 500 && capBusy != 0; spin++) Thread.Sleep(1);
            ulong v = capFenceValue;
            if (v != 0 && capFence.GetCompletedValue() < v)
            {
                capFence.SetEventOnCompletion(v, capIdleEvent.SafeWaitHandle.DangerousGetHandle());
                if (!capIdleEvent.WaitOne(3000)) Log.Write("Copia por bandas: la grafica no acaba la subida (valla " + v + ")");
            }
        }

        // 20-11 (LATENCIA7) repetidas_hilos=1: la comparacion con la imagen anterior (IsDuplicate, 1 de cada 8 filas) repartida en los hilos
        // de la copia: lo mismo (igual si todas las filas miradas son iguales; la firma pasa a ser la de esta), en menos tiempo. Esta en el
        // camino de cada imagen nueva antes de copiarla (a 4K BGR24, 3,1 MB leidos y escritos con un hilo)
        bool IsDuplicateParallel(byte* p, int pitch, int rowBytes, int rows, bool same)
        {
            int diff = same ? 0 : 1;
            long ps = (long)p;
            int n = CopyThreadsSet > 0 ? CopyThreadsSet : CopyThreads;
            fixed (byte* sig0 = dupSig)
            {
                long sg = (long)sig0;
                System.Threading.Tasks.Parallel.For(0, n, delegate (int k)
                {
                    int r0 = rows * k / n, r1 = rows * (k + 1) / n, m = rowBytes / 8;
                    for (int r = r0; r < r1; r++)
                    {
                        byte* row = (byte*)ps + (long)r * 8 * pitch, d = (byte*)sg + (long)r * rowBytes;
                        if (Volatile.Read(ref diff) == 0)
                        {
                            long* a = (long*)row, b = (long*)d;
                            for (int i = 0; i < m; i++) if (a[i] != b[i]) { Volatile.Write(ref diff, 1); break; }
                        }
                        Buffer.MemoryCopy(row, d, rowBytes, rowBytes);
                    }
                });
            }
            return diff == 0;
        }
    }
}
