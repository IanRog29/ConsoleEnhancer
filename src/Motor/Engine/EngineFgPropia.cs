using System;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 22-11-2026 (1.2.4, DLSS5LEGAL 2a parte): GENERACION PROPIA x2 / x3 / x4 en el motor, para CUALQUIER grafica y sin nada de terceros:
    // codigo propio + los vectores que el motor ya calcula (mvec: "anterior - actual", en px de la entrada). Lo legal de los demas no da
    // x3 / x4 fuera de RTX 50 / Intel: DLSS FG x3/x4 solo RTX 50 (Streamline lo dice), XeSS FG x3/x4 solo Intel (fuera, parche en memoria:
    // lo prohibe su licencia), FSR 3/4 FG solo x2 (API sin instantes), la FRUC del SDK de Optical Flow de NVIDIA no se puede repartir (su
    // licencia solo deja repartir el codigo de ejemplo) y solo es DX11/CUDA, y la VFG de NVIDIA Video Effects (x2-x8 en cualquier instante)
    // es solo RTX 40/50, CUDA y sin permiso claro de reparto.
    // Como va: con la imagen real N lista (final), antes de enseñarla se inventan k-1 entre N-1 y N en los instantes j/k (j = 1..k-1) y se
    // presentan a su hora (cada intervalo real / k), y luego N. Cada inventada: el punto x del instante t viene del punto p de la actual
    // con p = x - (1-t) v(p) (dos vueltas) y del q = p + v de la anterior; si las dos muestras casan se mezclan (por el tiempo), si no se
    // coge la mas cercana en el tiempo. Cuesta (k-1)/k de un intervalo real de retraso en la imagen real (como cualquier generacion que
    // interpola) y una pasada de grafica muy ligera por inventada.
    // Claves: fg_propia=0|2|3|4 (de serie 0; solo sin fg=1: DLSS-G / FSR 3 FG / XeSS FG mandan), fg_propia_umbral=0.06 (diferencia de
    // color 0-1 a partir de la cual las dos muestras "no casan"), fg_propia_vsync=-1|0|1 (auto: con sincronizacion si k x las reales pasan
    // del 90 % del refresco de la tele; si no, con VRR, cada una a su hora sin sincronizacion), fg_propia_candidatos=1, fg_propia_radio=32.
    // Medido (22-11, 4060 Ti, 4K, escenas de CALIDAD + fgcal2): cabeza x3 1,04 (halo 4,3), x4 2,4 (halo 5,7); finas x3 11,0 (fina 7,2); por el
    // motor DLSS FG x2 0,35 / 4,0 y Magpie DLSS FG x4 0,81 / 8,3: mejor que nada y que el XeSS FG x2 de Magpie en la cabeza, peor que DLSS-G.
    // Retraso de la real (llegada -> presentada): x3 ~27 ms, x4 31-55 ms (con sincronizacion); Magpie x3/x4 ~70 ms.
    partial class Settings
    {
        public int FgOwn = 0;
        public float FgOwnThr = 0.06f;
        public int FgOwnVsync = -1;
        public bool FgOwnCands = true;              // fg_propia_candidatos=1 (v2)
        public int FgOwnR = 32;                     // fg_propia_radio=32 (px de la salida; medido en la cabeza x4: 32 -> halo 5,7, 16 -> 9,6)
        public int FgOwnGpuWait = -1;               // fg_propia_espera=-1|0|1 (1.2.4, integracion: ver FpPresent)

        bool ApplyFgOwn(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "fg_propia": { int k = I(val); FgOwn = k <= 1 ? 0 : Math.Min(4, k); return true; }
                case "fg_propia_umbral": FgOwnThr = (float)Math.Max(0.005, Math.Min(1, D(val))); return true;
                case "fg_propia_vsync": FgOwnVsync = I(val); return true;
                case "fg_propia_candidatos": FgOwnCands = B(val); return true;
                case "fg_propia_radio": FgOwnR = Math.Max(1, Math.Min(128, I(val))); return true;
                case "fg_propia_espera": FgOwnGpuWait = I(val); return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        // v2 (22-11 tras la 1a medida: halo 10,7 en la cabeza con x4): CANDIDATOS. Ademas del vector del punto (con su vuelta), los de 4
        // vecinos a R px (Pad1.x; de serie 32): en el borde de lo que se mueve, el del fondo y el de la figura. Se queda el que mejor casa
        // las dos muestras (anterior y actual, 3 puntos en horizontal): asi lo que se destapa coge el vector del fondo y no el de la figura
        // (fg_propia_candidatos=0: como la v1, solo el del punto)
        const string FpShader = @"
cbuffer C : register(b0) { uint W, H, IW, IH; float T, SX, SY, Thr; uint R, Cands, P2, P3; uint4 Pad2; }
Texture2D<float4> Prev : register(t0);
Texture2D<float4> Cur : register(t1);
Texture2D<float2> Mv : register(t2);
RWTexture2D<unorm float4> Out : register(u0);
SamplerState Lin : register(s0);
float2 MvOut(float2 p) { return Mv.SampleLevel(Lin, p / float2(W, H), 0) * float2(SX, SY); }
float Y(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
float Err(float2 x, float2 v, float2 sz)
{
    float2 p = x - (1 - T) * v, q = x + T * v, e = float2(2, 0);
    float a = abs(Y(Cur.SampleLevel(Lin, p / sz, 0).rgb) - Y(Prev.SampleLevel(Lin, q / sz, 0).rgb));
    float b = abs(Y(Cur.SampleLevel(Lin, (p - e) / sz, 0).rgb) - Y(Prev.SampleLevel(Lin, (q - e) / sz, 0).rgb));
    float c = abs(Y(Cur.SampleLevel(Lin, (p + e) / sz, 0).rgb) - Y(Prev.SampleLevel(Lin, (q + e) / sz, 0).rgb));
    return a + b + c;
}
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    float2 sz = float2(W, H);
    float2 x = id.xy + 0.5;
    float2 v = MvOut(x);
    v = MvOut(x - (1 - T) * v);
    if (Cands != 0)
    {
        float best = Err(x, v, sz) - 0.01;   // (un poco de ventaja al del punto)
        float r = (float)R;
        float2 o[4] = { float2(r, 0), float2(-r, 0), float2(0, r), float2(0, -r) };
        [unroll] for (int i = 0; i < 4; i++)
        {
            float2 w = MvOut(x + o[i]);
            float e = Err(x, w, sz);
            if (e < best) { best = e; v = w; }
        }
    }
    float2 p = x - (1 - T) * v;
    float2 q = p + v;
    float3 c = Cur.SampleLevel(Lin, p / sz, 0).rgb;
    float3 a = Prev.SampleLevel(Lin, q / sz, 0).rgb;
    float3 d = abs(c - a);
    float agree = 1 - smoothstep(Thr, Thr * 3, max(d.r, max(d.g, d.b)));
    float3 near = T >= 0.5 ? c : a;
    Out[id.xy] = float4(saturate(lerp(near, lerp(a, c, T), agree)), 1);
}";
        const int FpAllocs = 8;
        ID3D12PipelineState psoFp;
        ID3D12DescriptorHeap fpHeap;
        IntPtr fpHeapPtr;
        D3D12_CPU_DESCRIPTOR_HANDLE fpCpu0;
        D3D12_GPU_DESCRIPTOR_HANDLE fpGpu0;
        readonly ID3D12CommandAllocator[] fpAlloc = new ID3D12CommandAllocator[FpAllocs];
        readonly ulong[] fpAllocFence = new ulong[FpAllocs];
        int fpAi;
        ID3D12GraphicsCommandList fpList;
        ID3D12Resource fpPrev, fpOut, fpTabPrev, fpTabCur, fpTabMv;   // (fpTab*: lo que tiene la tabla de descriptores)
        bool fpPrevValid, fpBroken, fpSaid;
        int fpW, fpH;
        ID3D12Resource fpShow;              // la real de esta imagen (final o con aviso): la enseña FpPresent
        bool fpHold;                        // RenderFrame no la ha copiado a la cadena: lo hace FpPresent
        bool fpGen;                         // esta imagen lleva inventadas (si no, solo se guarda como anterior)
        long fpLastRealAt;
        long fpGens, fpFrames;

        bool FpWanted() { return s.FgOwn >= 2 && !s.Fg && !fpBroken; }

        // RenderFrame, paso 5 (con 'show' = lo que se enseñaria): true = FpPresent la enseña (y no se copia aqui a la cadena)
        bool FpTakeReal(ID3D12Resource show, int slot)
        {
            fpHold = false; fpGen = false;
            if (!FpWanted() || slot < 0 || s.NoPresent) return false;
            try { FpEnsure(); }
            catch (Exception ex) { fpBroken = true; Log.Write("Generacion propia no disponible: " + ex.Message + " (se sigue sin ella)"); return false; }
            fpShow = show;
            fpHold = true;
            // inventadas solo con la anterior guardada, vectores de esta imagen, sin corte de escena y sin aviso encima (las inventadas no
            // lo llevarian: parpadearia)
            fpGen = fpPrevValid && mvec != null && !frameReset && ReferenceEquals(show, final);
            return true;
        }

        void FpEnsure()
        {
            if (psoFp == null)
            {
                psoFp = Pso(CompileCs("fgpropia", FpShader));
                D3D12_DESCRIPTOR_HEAP_DESC hd = new D3D12_DESCRIPTOR_HEAP_DESC();
                hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE.CBV_SRV_UAV;
                hd.NumDescriptors = (uint)Shaders.TableSize;
                hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.SHADER_VISIBLE;
                object o;
                Guid iid = typeof(ID3D12DescriptorHeap).GUID;
                Hr.Check(dev.CreateDescriptorHeap(ref hd, ref iid, out o), "CreateDescriptorHeap (generacion propia)");
                fpHeap = (ID3D12DescriptorHeap)o;
                fpHeapPtr = Com.BorrowPtr(fpHeap, typeof(ID3D12DescriptorHeap));
                fpHeap.GetCPUDescriptorHandleForHeapStart(out fpCpu0);
                fpHeap.GetGPUDescriptorHandleForHeapStart(out fpGpu0);
                D3D12_SHADER_RESOURCE_VIEW_DESC sd = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(ColorFmt);
                D3D12_UNORDERED_ACCESS_VIEW_DESC ud = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(ColorFmt);
                for (int i = 0; i < U0; i++) dev.CreateShaderResourceView(null, &sd, fpCpu0.Offset(i, inc));
                for (int i = U0; i < Shaders.TableSize; i++) dev.CreateUnorderedAccessView(null, null, &ud, fpCpu0.Offset(i, inc));
                iid = typeof(ID3D12CommandAllocator).GUID;
                for (int i = 0; i < FpAllocs; i++)
                {
                    Hr.Check(dev.CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.DIRECT, ref iid, out o), "CreateCommandAllocator (generacion propia)");
                    fpAlloc[i] = (ID3D12CommandAllocator)o;
                }
                iid = typeof(ID3D12GraphicsCommandList).GUID;
                Hr.Check(dev.CreateCommandList(0, D3D12_COMMAND_LIST_TYPE.DIRECT, fpAlloc[0], null, ref iid, out o), "CreateCommandList (generacion propia)");
                fpList = (ID3D12GraphicsCommandList)o;
                Hr.Check(fpList.Close(), "Close");
            }
            if (fpPrev == null || fpW != outW || fpH != outH)
            {
                WaitIdle();
                if (fpPrev != null) Bury(fpPrev);
                if (fpOut != null) Bury(fpOut);
                fpPrev = Tex(ColorFmt, outW, outH, false, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "generacion propia: anterior");
                fpOut = Tex(ColorFmt, outW, outH, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "generacion propia: inventada");
                fpW = outW; fpH = outH; fpPrevValid = false;
                fpTabPrev = fpTabCur = fpTabMv = null;
            }
            if (!ReferenceEquals(fpTabPrev, fpPrev) || !ReferenceEquals(fpTabCur, final) || !ReferenceEquals(fpTabMv, mvec))
            {
                WaitIdle();   // (raro: al cambiar de tamaño; la tabla la puede estar leyendo una lista anterior)
                D3D12_SHADER_RESOURCE_VIEW_DESC sd = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(ColorFmt);
                dev.CreateShaderResourceView(fpPrev, &sd, fpCpu0.Offset(0, inc));
                dev.CreateShaderResourceView(final, &sd, fpCpu0.Offset(1, inc));
                if (mvec != null)
                {
                    D3D12_SHADER_RESOURCE_VIEW_DESC md = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(DXGI_FORMAT.R16G16_FLOAT);
                    dev.CreateShaderResourceView(mvec, &md, fpCpu0.Offset(2, inc));
                }
                D3D12_UNORDERED_ACCESS_VIEW_DESC ud = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(ColorFmt);
                dev.CreateUnorderedAccessView(fpOut, null, &ud, fpCpu0.Offset(U0, inc));
                fpTabPrev = fpPrev; fpTabCur = final; fpTabMv = mvec;
            }
            if (!fpSaid)
            {
                fpSaid = true;
                Log.Write("Generacion propia si (x" + s.FgOwn + ", codigo propio con los vectores del motor; umbral " + s.FgOwnThr.ToString("0.000", IC) + (s.FgOwnCands ? ", candidatos a " + s.FgOwnR + " px" : ", solo el vector del punto") + ")");
            }
        }

        void FpBarrier(ID3D12Resource r, D3D12_RESOURCE_STATES a, D3D12_RESOURCE_STATES b)
        {
            D3D12_RESOURCE_BARRIER x = D3D12_RESOURCE_BARRIER.CreateTransition(r, a, b);
            fpList.ResourceBarrier(1, ref x);
        }

        void FpBegin()
        {
            fpAi = (fpAi + 1) % FpAllocs;
            WaitFence(fpAllocFence[fpAi]);
            Hr.Check(fpAlloc[fpAi].Reset(), "Allocator.Reset (generacion propia)");
            Hr.Check(fpList.Reset(fpAlloc[fpAi], null), "List.Reset (generacion propia)");
        }

        void FpSubmit()
        {
            Hr.Check(fpList.Close(), "Close (generacion propia)");
            IntPtr lp = Com.BorrowPtr(fpList, typeof(ID3D12CommandList));
            queue.ExecuteCommandLists(1, ref lp);
            fenceValue++;
            queue.Signal(fence, fenceValue);
            fpAllocFence[fpAi] = fenceValue;
        }

        // Una inventada en el instante t (0 = anterior, 1 = actual) a la cadena (final ya en NON_PIXEL_SHADER_RESOURCE)
        void FpRecordGen(float t)
        {
            uint* c = stackalloc uint[16];
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)outW; c[1] = (uint)outH; c[2] = (uint)inW; c[3] = (uint)inH;
            c[4] = F(t); c[5] = F((float)outW / Math.Max(1, inW)); c[6] = F((float)outH / Math.Max(1, inH)); c[7] = F(s.FgOwnThr);
            c[8] = (uint)s.FgOwnR; c[9] = s.FgOwnCands ? 1u : 0u;
            fpList.SetDescriptorHeaps(1, ref fpHeapPtr);
            fpList.SetComputeRootSignature(rootSig);
            fpList.SetPipelineState(psoFp);
            fpList.SetComputeRootDescriptorTable(1, fpGpu0);
            fpList.SetComputeRoot32BitConstants(0, 12, c, 0);
            fpList.Dispatch(Groups(outW, 16), Groups(outH, 8), 1);
            FpBarrier(fpOut, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.COPY_SOURCE);
            ID3D12Resource bb = backbuf[swap.GetCurrentBackBufferIndex()];
            FpBarrier(bb, D3D12_RESOURCE_STATES.PRESENT, D3D12_RESOURCE_STATES.COPY_DEST);
            fpList.CopyResource(bb, fpOut);
            FpBarrier(bb, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.PRESENT);
            FpBarrier(fpOut, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
        }

        // Hasta la hora 'at' (Clock): dormir y lo ultimo esperando activo (la hora de cada inventada importa mas que 1 ms de CPU)
        static void FpWaitUntil(long at)
        {
            while (true)
            {
                double left = -Clock.SinceMs(at);
                if (left <= 0) return;
                if (left > 1.6) Thread.Sleep(1); else Thread.SpinWait(200);
            }
        }

        // Run, en vez de swap.Present: sin la generacion propia, como siempre; con ella, las inventadas (cada una a su hora) y luego la real
        int FpPresent(uint sync, uint flags)
        {
            if (!fpHold) return swap.Present(sync, (DXGI_PRESENT)flags);
            fpHold = false;
            int k = s.FgOwn, hr = 0;
            double real = 1000.0 / Math.Max(1.0, (s.FpsNum > 0 ? s.FpsNum / (double)Math.Max(1, s.FpsDen) : 59.94) * (s.Mode == 30 ? 0.5 : 1));
            double step = real / k;
            // con sincronizacion si k x las reales no caben con margen bajo el refresco (x4 de 30 en 120 Hz); si no, cada una a su hora
            // (1.2.4, integracion: la espera a la grafica -abajo- solo si la real cabe en el 70 % de un hueco; si no -p. ej. DLSS 5 con x3-, por
            // VRR las inventadas se veian juntas -26 / 1,7 / 5,9 ms medido- y con sincronizacion salen a 1 / 1 / 2 refrescos: con sincronizacion)
            bool espera = s.FgOwnGpuWait == 1 || (s.FgOwnGpuWait < 0 && stats.LastGpuP95 > 0 && stats.LastGpuP95 < step * 0.7);
            bool vs = s.FgOwnVsync == 1 || (s.FgOwnVsync < 0 && (sync > 0 || (k * 1000.0 / real) > DisplayHz() * 0.9 || !espera));
            uint sy = vs ? 1u : 0u, fl = vs ? 0u : flags;
            long t0 = Clock.Now;
            try
            {
                if (fpGen)
                {
                    FpBegin();
                    FpBarrier(final, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                    for (int j = 1; j < k; j++)
                    {
                        if (j > 1) FpBegin();
                        FpRecordGen((float)j / k);
                        FpSubmit();
                        // (1.2.4, integracion, medido en la app entera 22-11, 4K modo 30 x3 por VRR: las presentaciones salian cada 11,1 ms pero
                        // en la tele 2,5 / 11,2 / 19,5 ms -con DLSS 5, 26 / 1,7 / 5,9-: la 1a inventada no se ve hasta que la grafica acaba la
                        // real, que va delante en la cola. Con la grafica de la real por debajo del 70 % de un hueco -fg_propia_espera=-1, auto-
                        // se espera a que la 1a inventada este hecha y el reparto empieza ahi; con mas, como estaba -si no, el hilo no llegaria a
                        // la siguiente real-)
                        if (j == 1 && espera)
                        { WaitFence(fpAllocFence[fpAi]); t0 = Clock.Now; }
                        if (j > 1) FpWaitUntil(t0 + (long)((j - 1) * step / Clock.TickMs));
                        hr = swap.Present(sy, (DXGI_PRESENT)fl);
                        if (hr < 0) return hr;
                        fpGens++;
                    }
                    FpBegin();
                    FpBarrier(final, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_SOURCE);
                }
                else FpBegin();
                // la real (con su aviso si lo hay) y, para la siguiente, guardada como anterior
                ID3D12Resource bb = backbuf[swap.GetCurrentBackBufferIndex()];
                FpBarrier(bb, D3D12_RESOURCE_STATES.PRESENT, D3D12_RESOURCE_STATES.COPY_DEST);
                fpList.CopyResource(bb, fpShow ?? final);
                FpBarrier(bb, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.PRESENT);
                FpBarrier(fpPrev, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_DEST);
                fpList.CopyResource(fpPrev, final);
                FpBarrier(fpPrev, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                FpSubmit();
                fpPrevValid = true;
                if (fpGen) FpWaitUntil(t0 + (long)((k - 1) * step / Clock.TickMs));
                hr = swap.Present(fpGen ? sy : sync, (DXGI_PRESENT)(fpGen ? fl : flags));
                fpFrames++;
                fpLastRealAt = Clock.Now;
                return hr;
            }
            catch (Exception ex)
            {
                fpBroken = true;
                Log.Write("Generacion propia: fallo (" + ex.Message + "): se sigue sin ella");
                return hr;
            }
        }

        // (1.2.4, integracion) Rota en esta sesion (FpEnsure / FpPresent fallaron): el lanzador lo sabe una vez (switch2-fgpropia rota N) y
        // pasa x3 / x4 a otro camino. Desde EventTick (EngineMpv.cs), como XeFgFailTick
        bool fpBrokenSaid;
        void FpFailTick()
        {
            if (fpBrokenSaid || !fpBroken || s.FgOwn < 2 || mpvIpc == null) return;
            if (mpvIpc.Event("switch2-fgpropia", "rota", s.FgOwn.ToString(IC)) > 0) { fpBrokenSaid = true; Log.Write("Generacion propia: rota, avisado el lanzador"); }
        }

        string FpBench() { return s.FgOwn >= 2 ? "\tfg_propia=x" + s.FgOwn + " inventadas=" + fpGens + " reales=" + fpFrames + (fpBroken ? " rota" : "") : ""; }
    }
}
