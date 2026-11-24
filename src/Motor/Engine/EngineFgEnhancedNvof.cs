using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFgEnhancedNvof.cs - 24-11-2026 (1.3.3, decima ronda, agente EFGCAL). El flujo optico POR HARDWARE de NVIDIA (NVOF, el acelerador
    // de flujo de las RTX 20 / 30 / 40 / 50) como vectores de L0 de Enhanced FG al interpolar (modo calidad). El usuario tras jugar con la
    // 1.3.2: "mejora mucho los errores visuales que tienen el enhanced fg" / "sigue intentando mejorar el fg". Ver notas\EFGCAL.md.
    // Medido con video real contra la imagen de en medio de verdad (el simulador): -3 % de error en un giro rapido de Fallout 4 y -5 % en GTA V
    // base 30; lo mejor de todo lo probado (busqueda por zonas, PatchMatch, lo de fuera de la imagen: -1 a -3 %).
    // Como va (sin interfaz D3D12 de NVOF: su cabecera no es publica; por CUDA, la de nvofdump de EFGQ2):
    //  1. EfgFlowList, con la piramide de la actual hecha: la luma de L0 a 8 bits (EfgNvShaders.Pack) -> un buffer de lectura; se manda esa
    //     lista y el hilo de NVOF (CUDA, su contexto) espera esa valla, sube la luma, y saca el flujo de NVIDIA en los dos sentidos (dos sesiones,
    //     cada una con sus pistas temporales; rejilla 4x4, nivel medio) a un buffer de subida.
    //  2. Mientras, la grafica hace el flujo propio de siempre (otra lista).
    //  3. Con el flujo de L0 hecho (tras su apoyo): el hilo de Enhanced FG espera al de NVOF (hasta efg_nvof_ms) y, si ha llegado, copia la
    //     rejilla a una textura y EfgNvShaders.Inject pone en cada bloque de 8x8 de L0 (los dos sentidos) la media de sus 2x2 vectores de NVIDIA,
    //     salvo en la interfaz quieta (>= efg_nvof_hud puntos del bloque quietos con borde, con la cuenta de la anterior): su vector propio
    //     (lo de EFGQ2 "nv:17", lo medido). Lo demas (eleccion, inventada, paneo plano de EFGMAPA, ...) igual.
    //  Si no hay NVIDIA, CUDA o NVOF (o falla, o no llega a tiempo), el flujo propio de siempre.
    // Claves (motor): efg_nvof (0 = no, 1 = si, 2 = auto: si con NVIDIA), efg_nvof_perf (5 lento / 10 medio / 20 rapido), efg_nvof_ms (lo que se
    // espera como mucho a NVIDIA por pareja), efg_nvof_hud (puntos quietos con borde de un bloque para dejarle su vector; 0 = nunca).
    partial class Settings
    {
        public int EfgNvof = 0, EfgNvofPerf = 10, EfgNvofHud = 4;
        public float EfgNvofMs = 20;

        bool ApplyEfgNvof(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "efg_nvof": { string v = val.Trim().ToLowerInvariant(); EfgNvof = v == "auto" ? 2 : Math.Max(0, Math.Min(2, I(val))); return true; }
                case "efg_nvof_perf": { int p = I(val); EfgNvofPerf = p <= 5 ? 5 : p >= 20 ? 20 : 10; return true; }
                case "efg_nvof_ms": EfgNvofMs = (float)Math.Max(2, Math.Min(100, D(val))); return true;
                case "efg_nvof_hud": EfgNvofHud = Math.Max(0, Math.Min(64, I(val))); return true;
            }
            return false;
        }
    }

    static class EfgNvShaders
    {
        // ---- luma de L0 (R16_FLOAT) -> 8 bits (R8_UNORM) para NVOF
        public const string Pack = @"
cbuffer C : register(b0) { uint W, H, Q0, Q1; uint4 Q2; uint4 Q3; uint4 Q4; }
Texture2D<float> Src : register(t0);
RWTexture2D<unorm float> Dst : register(u0);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    Dst[id.xy] = saturate(Src.Load(int3(id.xy, 0)));
}";

        // ---- los vectores de NVIDIA (R16G16_SINT, 1/32 de punto, rejilla 4x4 de L0; FW x 2FH: C2P arriba, P2C debajo) a los bloques de 8x8 de L0
        public const string Inject = @"
cbuffer C : register(b0) { uint BX, BY, FW, FH; uint LW, LH, MinQ, Q0; float Thr, Grad, StillN, Q1; uint4 Q2; }
Texture2D<int2> Nv : register(t0);
Texture2D<float> Lc : register(t1);
Texture2D<float> Lp : register(t2);
Texture2D<float2> Sp : register(t3);
RWTexture2D<float4> F0 : register(u0);
RWTexture2D<float4> F1 : register(u1);
float2 Avg(int2 g0, int2 g1, int oy)
{
    int2 s = Nv.Load(int3(g0.x, g0.y + oy, 0)) + Nv.Load(int3(g1.x, g0.y + oy, 0)) + Nv.Load(int3(g0.x, g1.y + oy, 0)) + Nv.Load(int3(g1.x, g1.y + oy, 0));
    return float2(s) * (1.0 / 128);
}
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= BX || id.y >= BY) return;
    if (MinQ > 0)
    {
        // la interfaz quieta se queda con su vector propio: puntos del bloque quietos (con la cuenta de la anterior) y con borde
        uint nq = 0; int2 mx = int2(LW - 1, LH - 1);
        [loop] for (int y = 0; y < 8; y++)
            [loop] for (int x = 0; x < 8; x++)
            {
                int2 p = min(int2(id.xy) * 8 + int2(x, y), mx);
                float c = Lc.Load(int3(p, 0)), q = Lp.Load(int3(p, 0));
                float g = max(max(abs(Lc.Load(int3(min(p + int2(1, 0), mx), 0)) - c), abs(Lc.Load(int3(max(p - int2(1, 0), 0), 0)) - c)),
                              max(abs(Lc.Load(int3(min(p + int2(0, 1), mx), 0)) - c), abs(Lc.Load(int3(max(p - int2(0, 1), 0), 0)) - c)));
                float n = abs(c - q) <= Thr ? Sp.Load(int3(p, 0)).x + 1 : 0;
                if (n >= StillN && g >= Grad) nq++;
            }
        if (nq >= MinQ) return;
    }
    int2 lim = int2(FW - 1, FH - 1);
    int2 g0 = min(int2(id.xy) * 2, lim), g1 = min(g0 + 1, lim);
    float4 f = F0[id.xy]; f.xy = Avg(g0, g1, 0); F0[id.xy] = f;
    f = F1[id.xy]; f.xy = Avg(g0, g1, (int)FH); F1[id.xy] = f;
}";
    }

    unsafe partial class Engine
    {
        // ------------------------------------------------------------------ CUDA (driver) y NVOF 2.0 por CUDA (nvOpticalFlowCuda.h / Common.h)
        static class NvCu
        {
            [DllImport("nvcuda.dll")] public static extern int cuInit(uint flags);
            [DllImport("nvcuda.dll")] public static extern int cuDeviceGetCount(out int n);
            [DllImport("nvcuda.dll")] public static extern int cuDeviceGet(out int dev, int ordinal);
            [DllImport("nvcuda.dll")] public static extern int cuDeviceGetLuid(byte* luid, out uint mask, int dev);
            [DllImport("nvcuda.dll")] public static extern int cuCtxCreate_v2(out IntPtr ctx, uint flags, int dev);
            [DllImport("nvcuda.dll")] public static extern int cuCtxSynchronize();
            [DllImport("nvcuda.dll")] public static extern int cuMemcpy2D_v2(ref CUDA_MEMCPY2D p);
            [DllImport("nvofapi64.dll")] public static extern int NvOFAPICreateInstanceCuda(uint apiVer, ref FnList list);
            [StructLayout(LayoutKind.Sequential)]
            public struct CUDA_MEMCPY2D
            {
                public ulong srcXInBytes, srcY; public int srcMemoryType, pad0; public IntPtr srcHost; public ulong srcDevice; public IntPtr srcArray; public ulong srcPitch;
                public ulong dstXInBytes, dstY; public int dstMemoryType, pad1; public IntPtr dstHost; public ulong dstDevice; public IntPtr dstArray; public ulong dstPitch;
                public ulong WidthInBytes, Height;
            }
            [StructLayout(LayoutKind.Sequential)]
            public struct FnList
            {
                public IntPtr nvCreateOpticalFlowCuda, nvOFInit, nvOFCreateGPUBufferCuda, nvOFGPUBufferGetCUarray, nvOFGPUBufferGetCUdeviceptr, nvOFGPUBufferGetStrideInfo,
                              nvOFSetIOCudaStreams, nvOFExecute, nvOFDestroyGPUBufferCuda, nvOFDestroy, nvOFGetLastError, nvOFGetCaps;
            }
            [StructLayout(LayoutKind.Sequential)]
            public struct InitParams { public uint width, height; public int outGridSize, hintGridSize, mode, perfLevel, enableExternalHints, enableOutputCost; public IntPtr hPrivData; public int disparityRange, enableRoi; }
            [StructLayout(LayoutKind.Sequential)] public struct BufDesc { public uint width, height; public int usage, format; }
            [StructLayout(LayoutKind.Sequential)]
            public struct ExecIn { public IntPtr inputFrame, referenceFrame, externalHints; public int disableTemporalHints; public uint padding; public IntPtr hPrivData; public uint padding2, numRois; public IntPtr roiData; }
            [StructLayout(LayoutKind.Sequential)] public struct ExecOut { public IntPtr outputBuffer, outputCostBuffer, hPrivData; }
            [StructLayout(LayoutKind.Sequential)] public struct StrideInfo { public uint sx0, sy0, sx1, sy1, sx2, sy2, numPlanes; }
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int DCreate(IntPtr ctx, out IntPtr hOf);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int DInit(IntPtr hOf, ref InitParams p);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int DCreateBuf(IntPtr hOf, ref BufDesc d, int type, out IntPtr buf);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate ulong DGetPtr(IntPtr buf);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int DStride(IntPtr buf, out StrideInfo s);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int DExec(IntPtr hOf, ref ExecIn i, ref ExecOut o);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int DLastErr(IntPtr hOf, byte[] s, ref uint size);
            public static T Fn<T>(IntPtr p) where T : class { return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T)); }
        }

        class EfgNvSession { public IntPtr h, inA, inB, outF; public ulong pA, pB, pOut; public uint strideIn, strideOut; }

        // el hilo de NVOF (todo lo de CUDA va en el)
        Thread efgNvThread;
        readonly AutoResetEvent efgNvJobEv = new AutoResetEvent(false), efgNvFenceEv = new AutoResetEvent(false);
        readonly ManualResetEvent efgNvDoneEv = new ManualResetEvent(true);
        volatile bool efgNvBusy, efgNvOk, efgNvBroken, efgNvJobFlow;
        ulong efgNvJobFence;
        int efgNvFw, efgNvFh;
        NvCu.FnList efgNvApi; NvCu.DCreate efgNvCreate; NvCu.DInit efgNvInit; NvCu.DCreateBuf efgNvCreateBuf; NvCu.DGetPtr efgNvGetPtr; NvCu.DStride efgNvStride; NvCu.DExec efgNvExec; NvCu.DLastErr efgNvLastErr;
        EfgNvSession efgNvFwd, efgNvBwd;
        IntPtr efgNvCtx;
        bool efgNvHaveCur;
        // la grafica
        bool efgNvReady, efgNvPending;
        ID3D12PipelineState psoEfgNvPack, psoEfgNvInject;
        ID3D12Resource efgNvY, efgNvF, efgNvRb, efgNvUp;
        IntPtr efgNvRbPtr, efgNvUpPtr;
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT efgNvRbFoot, efgNvUpFoot;
        ID3D12DescriptorHeap efgNvHeap; IntPtr efgNvHeapPtr; D3D12_CPU_DESCRIPTOR_HANDLE efgNvCpu0; D3D12_GPU_DESCRIPTOR_HANDLE efgNvGpu0;
        const int EfgNvTabPack = 0, EfgNvTabInject = 1, EfgNvTabs = 2;
        // el registro
        long efgNvLogAt, efgNvPairs, efgNvLate, efgNvUsed; double efgNvMsSum, efgNvMsMax, efgNvWaitSum;
        volatile float efgNvLastMs;

        bool EfgNvWanted()
        {
            if (s.EfgNvof == 0 || efgNvBroken) return false;
            if (s.EfgNvof == 2 && !gpu.IsNvidia) return false;
            return true;
        }
        bool EfgNvOn() { return efgNvReady && !efgNvBroken && EfgNvWanted() && EfgPhaseOfMode(Math.Max(2, Math.Min(4, s.Efg))); }

        D3D12_CPU_DESCRIPTOR_HANDLE EfgNvCpu(int set, int tab, int slot) { return efgNvCpu0.Offset((set * EfgNvTabs + tab) * Shaders.TableSize + slot, inc); }
        D3D12_GPU_DESCRIPTOR_HANDLE EfgNvGpu(int set, int tab) { return efgNvGpu0.Offset((set * EfgNvTabs + tab) * Shaders.TableSize, inc); }
        void EfgNvSrv(int set, int tab, int slot, ID3D12Resource r, DXGI_FORMAT f) { D3D12_SHADER_RESOURCE_VIEW_DESC d = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(f); dev.CreateShaderResourceView(r, &d, EfgNvCpu(set, tab, slot)); }
        void EfgNvUav(int set, int tab, int slot, ID3D12Resource r, DXGI_FORMAT f) { D3D12_UNORDERED_ACCESS_VIEW_DESC d = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(f); dev.CreateUnorderedAccessView(r, null, &d, EfgNvCpu(set, tab, U0 + slot)); }

        // EfgEnsure, con lo demas creado: sus sombreadores, texturas, buffers, tablas y el hilo (que abre CUDA y NVOF); si algo falla, sin NVOF
        void EfgNvEnsure()
        {
            if (efgNvReady || !EfgNvWanted()) return;
            try
            {
                long t0 = Clock.Now;
                byte[] a = CompileCs("efg_nvof_pack", EfgNvShaders.Pack), b = CompileCs("efg_nvof_inyecta", EfgNvShaders.Inject);
                psoEfgNvPack = Pso(a); psoEfgNvInject = Pso(b);
                int W = efgLw[0], H = efgLh[0];
                efgNvFw = (W + 3) / 4; efgNvFh = (H + 3) / 4;
                efgNvY = Tex(DXGI_FORMAT.R8_UNORM, W, H, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "Enhanced FG: NVOF luma");
                efgNvF = Tex(DXGI_FORMAT.R16G16_SINT, efgNvFw, efgNvFh * 2, false, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "Enhanced FG: NVOF vectores");
                D3D12_RESOURCE_DESC rd; uint rows; ulong rowSize, total;
                efgNvY.GetDesc(out rd);
                fixed (D3D12_PLACED_SUBRESOURCE_FOOTPRINT* fp = &efgNvRbFoot) dev.GetCopyableFootprints(ref rd, 0, 1, 0, fp, &rows, &rowSize, &total);
                efgNvRb = MakeBuffer(D3D12_HEAP_TYPE.READBACK, (long)total, D3D12_RESOURCE_STATES.COPY_DEST, "Enhanced FG: NVOF lectura");
                Hr.Check(efgNvRb.Map(0, null, out efgNvRbPtr), "Map (NVOF lectura)");
                efgNvF.GetDesc(out rd);
                fixed (D3D12_PLACED_SUBRESOURCE_FOOTPRINT* fp = &efgNvUpFoot) dev.GetCopyableFootprints(ref rd, 0, 1, 0, fp, &rows, &rowSize, &total);
                efgNvUp = MakeBuffer(D3D12_HEAP_TYPE.UPLOAD, (long)total, D3D12_RESOURCE_STATES.GENERIC_READ, "Enhanced FG: NVOF subida");
                D3D12_RANGE none = new D3D12_RANGE();
                Hr.Check(efgNvUp.Map(0, &none, out efgNvUpPtr), "Map (NVOF subida)");
                D3D12_DESCRIPTOR_HEAP_DESC hd = new D3D12_DESCRIPTOR_HEAP_DESC();
                hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE.CBV_SRV_UAV;
                hd.NumDescriptors = (uint)(EfgSets * EfgNvTabs * Shaders.TableSize);
                hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.SHADER_VISIBLE;
                object o; Guid iid = typeof(ID3D12DescriptorHeap).GUID;
                Hr.Check(dev.CreateDescriptorHeap(ref hd, ref iid, out o), "CreateDescriptorHeap (NVOF)");
                efgNvHeap = (ID3D12DescriptorHeap)o;
                efgNvHeapPtr = Com.BorrowPtr(efgNvHeap, typeof(ID3D12DescriptorHeap));
                efgNvHeap.GetCPUDescriptorHandleForHeapStart(out efgNvCpu0);
                efgNvHeap.GetGPUDescriptorHandleForHeapStart(out efgNvGpu0);
                D3D12_SHADER_RESOURCE_VIEW_DESC nsd = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(ColorFmt);
                D3D12_UNORDERED_ACCESS_VIEW_DESC nud = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(ColorFmt);
                for (int t = 0; t < EfgSets * EfgNvTabs; t++)
                {
                    for (int i = 0; i < U0; i++) dev.CreateShaderResourceView(null, &nsd, efgNvCpu0.Offset(t * Shaders.TableSize + i, inc));
                    for (int i = U0; i < Shaders.TableSize; i++) dev.CreateUnorderedAccessView(null, null, &nud, efgNvCpu0.Offset(t * Shaders.TableSize + i, inc));
                }
                const DXGI_FORMAT L = DXGI_FORMAT.R16_FLOAT, F4 = DXGI_FORMAT.R16G16B16A16_FLOAT;
                for (int c = 0; c < EfgSets; c++)
                {
                    int p = (c + EfgSets - 1) % EfgSets;
                    EfgNvSrv(c, EfgNvTabPack, 0, efgL[c, 0], L); EfgNvUav(c, EfgNvTabPack, 0, efgNvY, DXGI_FORMAT.R8_UNORM);
                    EfgNvSrv(c, EfgNvTabInject, 0, efgNvF, DXGI_FORMAT.R16G16_SINT);
                    EfgNvSrv(c, EfgNvTabInject, 1, efgL[c, 0], L); EfgNvSrv(c, EfgNvTabInject, 2, efgL[p, 0], L);
                    EfgNvSrv(c, EfgNvTabInject, 3, efgSt[p], DXGI_FORMAT.R16G16_FLOAT);
                    EfgNvUav(c, EfgNvTabInject, 0, efgF[c, 0, 0], F4); EfgNvUav(c, EfgNvTabInject, 1, efgF[c, 1, 0], F4);
                }
                efgNvThread = new Thread(EfgNvLoop) { IsBackground = true, Name = "Enhanced FG NVOF", Priority = ThreadPriority.AboveNormal };
                efgNvThread.Start();
                efgNvReady = true;
                Log.Write("Enhanced FG NVOF: flujo de NVIDIA por hardware (CUDA) a " + W + "x" + H + ", rejilla 4 (" + efgNvFw + "x" + efgNvFh + "), nivel " + s.EfgNvofPerf +
                          ", espera hasta " + s.EfgNvofMs.ToString("0", IC) + " ms, interfaz " + s.EfgNvofHud + " (efg_nvof=" + s.EfgNvof + "; " + Clock.SinceMs(t0).ToString("0", IC) + " ms; CUDA en su hilo)");
            }
            catch (Exception ex) { efgNvBroken = true; Log.Write("Enhanced FG NVOF: sin el flujo de NVIDIA (" + ex.Message + ")"); }
        }

        // EfgFlowList, con la piramide de la actual hecha: su luma a 8 bits al buffer de lectura, se manda y el hilo de NVOF se pone
        void EfgNvKick(int c, bool flow)
        {
            efgNvPending = false;
            if (!EfgNvOn()) return;
            if (efgNvBusy) { efgNvLate++; efgNvHaveCurReset = true; return; }   // (el anterior aun no ha acabado: este sin NVOF)
            uint* k = stackalloc uint[16];
            efgList.SetDescriptorHeaps(1, ref efgNvHeapPtr);
            EfgZero(k); k[0] = (uint)efgLw[0]; k[1] = (uint)efgLh[0];
            efgList.SetPipelineState(psoEfgNvPack);
            efgList.SetComputeRootDescriptorTable(1, EfgNvGpu(c, EfgNvTabPack));
            efgList.SetComputeRoot32BitConstants(0, 16, k, 0);
            efgList.Dispatch(Groups(efgLw[0], 16), Groups(efgLh[0], 8), 1);
            EB(efgNvY, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.COPY_SOURCE);
            D3D12_TEXTURE_COPY_LOCATION dst = D3D12_TEXTURE_COPY_LOCATION.FromFootprint(efgNvRb, efgNvRbFoot);
            D3D12_TEXTURE_COPY_LOCATION src = D3D12_TEXTURE_COPY_LOCATION.FromSubresource(efgNvY, 0);
            efgList.CopyTextureRegion(ref dst, 0, 0, 0, ref src, null);
            EB(efgNvY, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            efgList.SetDescriptorHeaps(1, ref efgHeapPtr);
            ulong v = EfgSubmit();
            EfgBegin();
            efgNvJobFence = v; efgNvJobFlow = flow; efgNvOk = false; efgNvBusy = true;
            efgNvDoneEv.Reset();
            efgNvJobEv.Set();
            efgNvPending = flow;
        }
        volatile bool efgNvHaveCurReset;

        // EfgFlowList, con el flujo de L0 hecho (tras su apoyo): se espera a NVIDIA y, si llego, sus vectores a los bloques de L0
        void EfgNvInject(int c)
        {
            if (!efgNvPending) return;
            efgNvPending = false;
            EfgSubmit();   // (el flujo propio, ya a la grafica mientras se espera)
            long t0 = Clock.Now;
            bool got = efgNvDoneEv.WaitOne(TimeSpan.FromMilliseconds(s.EfgNvofMs)) && efgNvOk;
            double w = Clock.SinceMs(t0);
            EfgBegin();
            efgNvPairs++; efgNvWaitSum += w;
            float ms = efgNvLastMs; efgNvMsSum += ms; if (ms > efgNvMsMax) efgNvMsMax = ms;
            if (!got) { efgNvLate++; EfgNvLogMaybe(); return; }
            efgNvUsed++;
            EB(efgNvF, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_DEST);
            D3D12_TEXTURE_COPY_LOCATION dst = D3D12_TEXTURE_COPY_LOCATION.FromSubresource(efgNvF, 0);
            D3D12_TEXTURE_COPY_LOCATION src = D3D12_TEXTURE_COPY_LOCATION.FromFootprint(efgNvUp, efgNvUpFoot);
            efgList.CopyTextureRegion(ref dst, 0, 0, 0, ref src, null);
            EB(efgNvF, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            uint* k = stackalloc uint[16];
            EfgZero(k);
            k[0] = (uint)efgBx[0]; k[1] = (uint)efgBy[0]; k[2] = (uint)efgNvFw; k[3] = (uint)efgNvFh; k[4] = (uint)efgLw[0]; k[5] = (uint)efgLh[0]; k[6] = (uint)s.EfgNvofHud;
            k[8] = F(s.EfgStillDiff); k[9] = F(s.EfgStillGrad); k[10] = F(s.EfgStillN);
            for (int d = 0; d < 2; d++) EB(efgF[c, d, 0], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            efgList.SetDescriptorHeaps(1, ref efgNvHeapPtr);
            efgList.SetPipelineState(psoEfgNvInject);
            efgList.SetComputeRootDescriptorTable(1, EfgNvGpu(c, EfgNvTabInject));
            efgList.SetComputeRoot32BitConstants(0, 16, k, 0);
            efgList.Dispatch(Groups(efgBx[0], 8), Groups(efgBy[0], 8), 1);
            for (int d = 0; d < 2; d++) EB(efgF[c, d, 0], D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            efgList.SetDescriptorHeaps(1, ref efgHeapPtr);
            EfgNvLogMaybe();
        }

        void EfgNvLogMaybe()
        {
            if (efgNvLogAt != 0 && Clock.SinceMs(efgNvLogAt) < 10000) return;
            efgNvLogAt = Clock.Now;
            if (efgNvPairs == 0) return;
            Log.Write("Enhanced FG NVOF: " + efgNvUsed + " de " + efgNvPairs + " parejas con el flujo de NVIDIA (" + efgNvLate + " tarde); NVIDIA " +
                      (efgNvMsSum / efgNvPairs).ToString("0.0", IC) + " ms de media (mayor " + efgNvMsMax.ToString("0.0", IC) + "), espera del hilo " +
                      (efgNvWaitSum / efgNvPairs).ToString("0.0", IC) + " ms de media");
            efgNvPairs = 0; efgNvUsed = 0; efgNvLate = 0; efgNvMsSum = 0; efgNvMsMax = 0; efgNvWaitSum = 0;
        }

        // ------------------------------------------------------------------ el hilo de NVOF
        void EfgNvLoop()
        {
            try { EfgNvOpen(); }
            catch (Exception ex) { efgNvBroken = true; Log.Write("Enhanced FG NVOF: sin el flujo de NVIDIA (" + ex.Message + ")"); efgNvBusy = false; efgNvDoneEv.Set(); return; }
            while (!Program.Quit && !efgNvBroken)
            {
                if (!efgNvJobEv.WaitOne(500)) continue;
                try
                {
                    // la luma de la actual en el buffer de lectura (la valla de su lista)
                    if (efgFence.GetCompletedValue() < efgNvJobFence)
                    {
                        efgFence.SetEventOnCompletion(efgNvJobFence, efgNvFenceEv.SafeWaitHandle.DangerousGetHandle());
                        if (efgFence.GetCompletedValue() < efgNvJobFence) efgNvFenceEv.WaitOne(1000);
                    }
                    long t0 = Clock.Now;
                    bool pair = efgNvJobFlow && efgNvHaveCur && !efgNvHaveCurReset;
                    efgNvHaveCurReset = false;
                    EfgNvSession f = efgNvFwd, b = efgNvBwd;
                    int W = efgLw[0], H = efgLh[0];
                    // la anterior (la actual de la vez pasada, en f.inA) a f.inB y b.inA; la actual a f.inA y b.inB
                    if (pair) { NvD2D(f.pA, f.strideIn, f.pB, f.strideIn, W, H); NvD2D(f.pA, f.strideIn, b.pA, b.strideIn, W, H); }
                    NvCu.CUDA_MEMCPY2D up = new NvCu.CUDA_MEMCPY2D { srcMemoryType = 1, srcHost = efgNvRbPtr + (int)efgNvRbFoot.Offset, srcPitch = efgNvRbFoot.Footprint.RowPitch,
                                                                       dstMemoryType = 2, dstDevice = f.pA, dstPitch = f.strideIn, WidthInBytes = (ulong)W, Height = (ulong)H };
                    NvCk(NvCu.cuMemcpy2D_v2(ref up), "cuMemcpy2D (subir)");
                    efgNvHaveCur = true;
                    if (pair)
                    {
                        NvD2D(f.pA, f.strideIn, b.pB, b.strideIn, W, H);
                        NvCu.ExecIn ei = new NvCu.ExecIn { inputFrame = f.inA, referenceFrame = f.inB }; NvCu.ExecOut eo = new NvCu.ExecOut { outputBuffer = f.outF };
                        NvCk(efgNvExec(f.h, ref ei, ref eo), "nvOFExecute (C2P)");
                        NvCu.ExecIn bi = new NvCu.ExecIn { inputFrame = b.inA, referenceFrame = b.inB }; NvCu.ExecOut bo = new NvCu.ExecOut { outputBuffer = b.outF };
                        NvCk(efgNvExec(b.h, ref bi, ref bo), "nvOFExecute (P2C)");
                        ulong rp = efgNvUpFoot.Footprint.RowPitch; IntPtr up0 = efgNvUpPtr + (int)efgNvUpFoot.Offset;
                        NvCu.CUDA_MEMCPY2D d0 = new NvCu.CUDA_MEMCPY2D { srcMemoryType = 2, srcDevice = f.pOut, srcPitch = f.strideOut, dstMemoryType = 1, dstHost = up0, dstPitch = rp,
                                                                           WidthInBytes = (ulong)(efgNvFw * 4), Height = (ulong)efgNvFh };
                        NvCk(NvCu.cuMemcpy2D_v2(ref d0), "cuMemcpy2D (C2P)");
                        NvCu.CUDA_MEMCPY2D d1 = new NvCu.CUDA_MEMCPY2D { srcMemoryType = 2, srcDevice = b.pOut, srcPitch = b.strideOut, dstMemoryType = 1, dstHost = up0 + (int)(rp * (ulong)efgNvFh), dstPitch = rp,
                                                                           WidthInBytes = (ulong)(efgNvFw * 4), Height = (ulong)efgNvFh };
                        NvCk(NvCu.cuMemcpy2D_v2(ref d1), "cuMemcpy2D (P2C)");
                        efgNvLastMs = (float)Clock.SinceMs(t0);
                        efgNvOk = true;
                    }
                }
                catch (Exception ex) { efgNvBroken = true; Log.Write("Enhanced FG NVOF: error, sin el flujo de NVIDIA (" + ex.Message + ")"); }
                efgNvBusy = false;
                efgNvDoneEv.Set();
            }
        }

        static void NvCk(int r, string what) { if (r != 0) throw new Exception(what + " = " + r); }
        static void NvD2D(ulong src, uint sp, ulong dst, uint dp, int w, int h)
        {
            NvCu.CUDA_MEMCPY2D m = new NvCu.CUDA_MEMCPY2D { srcMemoryType = 2, srcDevice = src, srcPitch = sp, dstMemoryType = 2, dstDevice = dst, dstPitch = dp, WidthInBytes = (ulong)w, Height = (ulong)h };
            NvCk(NvCu.cuMemcpy2D_v2(ref m), "cuMemcpy2D (copia)");
        }

        void EfgNvOpen()
        {
            NvCk(NvCu.cuInit(0), "cuInit");
            int n; NvCk(NvCu.cuDeviceGetCount(out n), "cuDeviceGetCount");
            // la de CUDA que es la grafica del motor (por su LUID); si no se sabe, la primera
            int dv = 0;
            for (int i = 0; i < n; i++)
            {
                int d; if (NvCu.cuDeviceGet(out d, i) != 0) continue;
                byte* l = stackalloc byte[8]; uint mask;
                if (NvCu.cuDeviceGetLuid(l, out mask, d) == 0 && *(long*)l == gpu.Luid) { dv = d; break; }
            }
            NvCk(NvCu.cuCtxCreate_v2(out efgNvCtx, 0, dv), "cuCtxCreate");
            efgNvApi = new NvCu.FnList();
            NvCk(NvCu.NvOFAPICreateInstanceCuda(0x20, ref efgNvApi), "NvOFAPICreateInstanceCuda");
            efgNvCreate = NvCu.Fn<NvCu.DCreate>(efgNvApi.nvCreateOpticalFlowCuda); efgNvInit = NvCu.Fn<NvCu.DInit>(efgNvApi.nvOFInit);
            efgNvCreateBuf = NvCu.Fn<NvCu.DCreateBuf>(efgNvApi.nvOFCreateGPUBufferCuda); efgNvGetPtr = NvCu.Fn<NvCu.DGetPtr>(efgNvApi.nvOFGPUBufferGetCUdeviceptr);
            efgNvStride = NvCu.Fn<NvCu.DStride>(efgNvApi.nvOFGPUBufferGetStrideInfo); efgNvExec = NvCu.Fn<NvCu.DExec>(efgNvApi.nvOFExecute);
            efgNvLastErr = NvCu.Fn<NvCu.DLastErr>(efgNvApi.nvOFGetLastError);
            efgNvFwd = NvOpenSession(efgLw[0], efgLh[0]); efgNvBwd = NvOpenSession(efgLw[0], efgLh[0]);
            Log.Write("Enhanced FG NVOF: CUDA y NVOF abiertos (dispositivo " + dv + " de " + n + ")");
        }

        EfgNvSession NvOpenSession(int w, int h)
        {
            EfgNvSession s2 = new EfgNvSession();
            NvCk(efgNvCreate(efgNvCtx, out s2.h), "nvCreateOpticalFlowCuda");
            NvCu.InitParams ip = new NvCu.InitParams { width = (uint)w, height = (uint)h, outGridSize = 4, mode = 1, perfLevel = s.EfgNvofPerf };
            int r = efgNvInit(s2.h, ref ip);
            if (r != 0) { byte[] e = new byte[256]; uint en = 256; try { efgNvLastErr(s2.h, e, ref en); } catch { } throw new Exception("nvOFInit = " + r + " " + System.Text.Encoding.ASCII.GetString(e, 0, (int)Math.Min(en, 256)).TrimEnd('\0')); }
            NvCu.BufDesc din = new NvCu.BufDesc { width = (uint)w, height = (uint)h, usage = 1, format = 1 };   // INPUT, GRAYSCALE8
            NvCk(efgNvCreateBuf(s2.h, ref din, 2, out s2.inA), "NVOF buffer A"); NvCk(efgNvCreateBuf(s2.h, ref din, 2, out s2.inB), "NVOF buffer B");
            NvCu.BufDesc dout = new NvCu.BufDesc { width = (uint)efgNvFw, height = (uint)efgNvFh, usage = 2, format = 5 };   // OUTPUT, SHORT2
            NvCk(efgNvCreateBuf(s2.h, ref dout, 2, out s2.outF), "NVOF buffer de salida");
            s2.pA = efgNvGetPtr(s2.inA); s2.pB = efgNvGetPtr(s2.inB); s2.pOut = efgNvGetPtr(s2.outF);
            NvCu.StrideInfo si; NvCk(efgNvStride(s2.inA, out si), "stride entrada"); s2.strideIn = si.sx0;
            NvCk(efgNvStride(s2.outF, out si), "stride salida"); s2.strideOut = si.sx0;
            return s2;
        }
    }
}
