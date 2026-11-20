using System;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 21-11 (FG21, fg_relleno=1): lo liso con vector 0 que en realidad se mueve, solo para la generacion (DLSS-G).
    // Con fg_vectores_completos=1 DLSS-G se cree los vectores por punto del motor. En lo liso (cielo, asfalto, paredes, sombras) la busqueda
    // no tiene con que comparar y gana el 0 (mvsesgo/mvruido; la ventana de 16x16 de mvflat no llega a la textura en zonas grandes):
    // medido con GTA a 4K, paneo de 32 px por imagen: 24,6 % de los bloques lisos a 0 y 0,0 % de los que tienen detalle. Para DLSS 5 / DLSS SR
    // da igual (lo liso se ve igual se mueva o no); DLSS-G en cambio deja esas zonas quietas y rompe los bordes de lo que se mueve junto a
    // ellas (halos, trozos que se quedan atras): los "muchisimos fallos" del usuario del 21-11 a 4K y a 1440p.
    // Tres pasos en mvecFg (la textura solo de la generacion; DLSS 5 y DLSS SR siguen con mvec):
    //  A) por bloque de 8x8: el vector del centro y si es liso (desviacion media de la luma < fg_relleno_liso niveles)
    //  B) por bloque liso a 0: el primer bloque con detalle en 8 direcciones (hasta fg_relleno_radio bloques); la media de los mas cercanos
    //     (hasta 1,5 veces la distancia del primero), pesada por cercania. Si lo de alrededor esta quieto (la interfaz), sale 0: no cambia
    //  C) por punto: un punto con vector 0 de un bloque relleno coge ese vector solo si casa con la imagen anterior igual de bien que el 0
    //     (3x3, + fg_relleno_empate niveles): junto al borde de algo quieto (un panel de la interfaz) se queda en 0
    unsafe partial class Engine
    {
        const int TRFillA = TRBase + 13 /* + juego */, TRFillB = TRBase + 15, TRFillC = TRBase + 16 /* + juego */;
        ID3D12PipelineState psoFillA, psoFillB, psoFillC;
        ID3D12Resource fillA, fillB;
        bool fgFillValid;
        int fgFillLog = -1;

        bool FgFillNow { get { return s.FgFill && FgVecFullNow && mvecFg != null && fillA != null; } }

        void FgFillViews()
        {
            if (fillA != null) Com.Release(ref fillA);
            if (fillB != null) Com.Release(ref fillB);
            fillA = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, bx[0], by[0], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "relleno generacion A");
            fillB = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, bx[0], by[0], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "relleno generacion B");
            for (int st = 0; st < 2; st++)
            {
                NullViews(TRFillA + st);
                SrvTex(TRFillA + st, 0, mvec, DXGI_FORMAT.R16G16_FLOAT);
                SrvTex(TRFillA + st, 1, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                UavTex(TRFillA + st, U0, fillA, DXGI_FORMAT.R16G16B16A16_FLOAT);
                NullViews(TRFillC + st);
                SrvTex(TRFillC + st, 0, mvec, DXGI_FORMAT.R16G16_FLOAT);
                SrvTex(TRFillC + st, 1, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(TRFillC + st, 2, luma[1 - st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(TRFillC + st, 3, fillB, DXGI_FORMAT.R16G16B16A16_FLOAT);
                if (mvecFg != null) UavTex(TRFillC + st, U0, mvecFg, DXGI_FORMAT.R16G16_FLOAT);
            }
            NullViews(TRFillB);
            SrvTex(TRFillB, 0, fillA, DXGI_FORMAT.R16G16B16A16_FLOAT);
            UavTex(TRFillB, U0, fillB, DXGI_FORMAT.R16G16B16A16_FLOAT);
        }

        // Tras los vectores de la imagen (RecordMotion, mvec ya en NON_PIXEL_SHADER_RESOURCE)
        void RecordFgFill(int cur, bool on)
        {
            bool want = on && FgFillNow;
            if (!want) { fgFillValid = false; LogFgFill(0); return; }
            if (psoFillA == null)
            {
                long t = Clock.Now;
                psoFillA = Pso(CompileCs("fgrellenoA", FShaders.FillA));
                psoFillB = Pso(CompileCs("fgrellenoB", FShaders.FillB));
                psoFillC = Pso(CompileCs("fgrellenoC", FShaders.FillC));
                Log.Write("Relleno de lo liso para la generacion: shaders (" + Clock.SinceMs(t).ToString("0") + " ms)");
            }
            uint* c = stackalloc uint[16];
            // A) bloques: vector del centro y liso
            Barrier(fillA, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)bx[0]; c[3] = (uint)by[0];
            c[8] = F((float)(s.FgFillFlat / 255.0));
            BindCompute(psoFillA, TRFillA + cur, c, 12);
            list.Dispatch(Groups(bx[0], 8), Groups(by[0], 8), 1);
            Barrier(fillA, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            // B) bloques lisos a 0: lo de alrededor con detalle
            Barrier(fillB, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)bx[0]; c[1] = (uint)by[0]; c[2] = (uint)Math.Max(1, Math.Min(128, s.FgFillRad));
            BindCompute(psoFillB, TRFillB, c, 12);
            list.Dispatch(Groups(bx[0], 8), Groups(by[0], 8), 1);
            Barrier(fillB, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            // C) por punto, a mvecFg
            Barrier(mvecFg, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)bx[0]; c[3] = (uint)by[0];
            c[8] = F((float)(s.FgFillTie / 255.0));
            BindCompute(psoFillC, TRFillC + cur, c, 12);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(mvecFg, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            fgFillValid = true;
            LogFgFill(1);
        }

        void LogFgFill(int st)
        {
            if (st == fgFillLog) return;
            // (solo cuando cambia y si la generacion va con los completos: sin ellos no hay nada que rellenar)
            if (st == 1 || fgFillLog == 1)
                Log.Write(st == 1 ? "Generacion: lo liso con vector 0 sigue a lo de alrededor (fg_relleno, liso < " + s.FgFillFlat.ToString("0.#") + " niveles, hasta " + s.FgFillRad + " bloques)"
                                  : "Generacion: sin el relleno de lo liso (" + (!s.FgFill ? "fg_relleno=0" : "sin los vectores completos") + ")");
            fgFillLog = st;
        }
    }

    static class FShaders
    {
        // A) c0: ancho, alto, bloques x/y | c2.x (float): liso (luma 0-1). Out: xy = vector del centro, z = 1 liso, w = 0
        public const string FillA = @"
cbuffer C : register(b0) { uint W, H, BX, BY; uint4 Pad1; float Flat, P0, P1, P2; uint4 Pad3; }
Texture2D<float2> Mv : register(t0);
Texture2D<float> Cur : register(t1);
RWTexture2D<float4> Out : register(u0);
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= BX || id.y >= BY) return;
    int2 b = int2(id.xy) * 8;
    int2 mx = int2(W - 1, H - 1);
    float c[64];
    float mean = 0;
    [unroll] for (int y = 0; y < 8; y++)
        [unroll] for (int x = 0; x < 8; x++) { float v = Cur.Load(int3(clamp(b + int2(x, y), int2(0, 0), mx), 0)); c[y * 8 + x] = v; mean += v; }
    mean /= 64;
    float dev = 0;
    [unroll] for (int i = 0; i < 64; i++) dev += abs(c[i] - mean);
    dev /= 64;
    float2 v = Mv.Load(int3(clamp(b + int2(4, 4), int2(0, 0), mx), 0));
    Out[id.xy] = float4(v, dev < Flat ? 1 : 0, 0);
}";

        // B) c0: bloques x/y, radio (bloques). Out: xy = vector a usar, z = liso, w = 1 si es relleno
        public const string FillB = @"
cbuffer C : register(b0) { uint BX, BY, R, P0; uint4 Pad1; uint4 Pad2; uint4 Pad3; }
Texture2D<float4> A : register(t0);
RWTexture2D<float4> Out : register(u0);
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= BX || id.y >= BY) return;
    float4 a = A.Load(int3(id.xy, 0));
    if (a.z < 0.5 || abs(a.x) >= 0.25 || abs(a.y) >= 0.25) { Out[id.xy] = float4(a.xyz, 0); return; }
    float dist[8];
    float2 vec[8];
    float first = 1e9;
    [unroll] for (int d = 0; d < 8; d++)
    {
        int2 dir = d == 0 ? int2(1, 0) : d == 1 ? int2(-1, 0) : d == 2 ? int2(0, 1) : d == 3 ? int2(0, -1) : d == 4 ? int2(1, 1) : d == 5 ? int2(1, -1) : d == 6 ? int2(-1, 1) : int2(-1, -1);
        float step = (d < 4) ? 1.0 : 1.41421356;
        dist[d] = 1e9; vec[d] = float2(0, 0);
        for (int k = 1; k <= (int)R; k++)
        {
            int2 q = int2(id.xy) + dir * k;
            if (q.x < 0 || q.y < 0 || q.x >= (int)BX || q.y >= (int)BY) break;
            float4 n = A.Load(int3(q, 0));
            if (n.z < 0.5) { dist[d] = step * k; vec[d] = n.xy; break; }
        }
        first = min(first, dist[d]);
    }
    if (first > 1e8) { Out[id.xy] = float4(0, 0, 1, 0); return; }
    float2 sum = float2(0, 0);
    float wsum = 0;
    [unroll] for (int e = 0; e < 8; e++)
        if (dist[e] <= first * 1.5) { float w = 1.0 / dist[e]; sum += vec[e] * w; wsum += w; }
    float2 v = sum / wsum;
    Out[id.xy] = float4(v, 1, (abs(v.x) >= 0.25 || abs(v.y) >= 0.25) ? 1 : 0);
}";

        // C) c0: ancho, alto, bloques x/y | c2.x (float): empate (luma 0-1). Out (mvecFg): mvec, o el relleno si casa igual de bien que 0
        public const string FillC = @"
cbuffer C : register(b0) { uint W, H, BX, BY; uint4 Pad1; float Tie, P0, P1, P2; uint4 Pad3; }
Texture2D<float2> Mv : register(t0);
Texture2D<float> Cur : register(t1);
Texture2D<float> Prev : register(t2);
Texture2D<float4> Fill : register(t3);
RWTexture2D<float2> Out : register(u0);
SamplerState Lin : register(s0);
float Err(int2 p, float2 v)
{
    float s = 0;
    float2 inv = 1.0 / float2(W, H);
    [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
        {
            int2 q = clamp(p + int2(i, j), int2(0, 0), int2(W - 1, H - 1));
            s += abs(Cur.Load(int3(q, 0)) - Prev.SampleLevel(Lin, (float2(q) + 0.5 + v) * inv, 0));
        }
    return s * (1.0 / 9.0);
}
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int2 p = int2(id.xy);
    float2 v = Mv.Load(int3(p, 0));
    if (abs(v.x) >= 0.25 || abs(v.y) >= 0.25) { Out[id.xy] = v; return; }
    float4 f = Fill.Load(int3(min(p / 8, int2(BX - 1, BY - 1)), 0));
    if (f.w < 0.5) { Out[id.xy] = v; return; }
    if (Err(p, f.xy) <= Err(p, v) + Tie) v = f.xy;
    Out[id.xy] = v;
}";
    }
}
