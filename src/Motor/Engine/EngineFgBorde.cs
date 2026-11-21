using System;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 21-11 (BORDE, fg_borde=1): los vectores que recibe la generacion (DLSS-G), a 0 en el borde entre dos movimientos distintos.
    // Halos del usuario desde la 1.0.5 (GTA a 4K, alrededor del personaje al girar): con el giro (corte_giro/mvglobal) o con los vectores
    // completos, DLSS-G recibe en el borde de lo que se mueve distinto que la camara vectores mezclados (medido con una capa quieta sobre un
    // paneo: hasta un 49 % del borde de dentro con el vector del fondo, y el de fuera a 0). DLSS-G se los cree y arrastra el fondo sobre el
    // personaje (o al reves): el halo. Con 0 en todas partes (lo de antes de la 1.0.5, sin halos) DLSS-G usa su propio flujo optico.
    // Aqui, solo para la generacion: un bloque de 8x8 con algun bloque a <= fg_borde_radio bloques cuyo vector difiera en mas de
    // fg_borde_dif px (+ fg_borde_rel x el mayor de los dos) pasa a 0 (su flujo optico decide); el resto, los vectores del motor (los giros).
    // En el peor caso (vectores malos en todas partes) queda todo a 0: lo de antes.
    //  A) por bloque: el vector del centro (de mvec o de mvecFg, lo que fuera a recibir la generacion)
    //  B) por bloque: 1 si hay un salto de movimiento cerca
    //  C) por punto: 0 donde hay salto, el vector donde no (a mvecGuard)
    unsafe partial class Engine
    {
        const int TRGuardA = TRBase + 18 /* + fuente (mvec, mvecFg) */, TRGuardB = TRBase + 20, TRGuardC = TRBase + 21 /* + fuente */;
        ID3D12PipelineState psoGuardA, psoGuardB, psoGuardC;
        ID3D12Resource guardA, guardB, mvecGuard;
        bool fgGuardValid;
        int fgGuardLog = -1;

        void FgGuardViews()
        {
            if (guardA != null) Com.Release(ref guardA);
            if (guardB != null) Com.Release(ref guardB);
            guardA = Tex(DXGI_FORMAT.R16G16_FLOAT, bx[0], by[0], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "borde generacion A");
            guardB = Tex(DXGI_FORMAT.R16G16_FLOAT, bx[0], by[0], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "borde generacion B");
            for (int k = 0; k < 2; k++)
            {
                ID3D12Resource src = k == 0 ? mvec : mvecFg;
                NullViews(TRGuardA + k);
                NullViews(TRGuardC + k);
                if (src == null) continue;
                SrvTex(TRGuardA + k, 0, src, DXGI_FORMAT.R16G16_FLOAT);
                UavTex(TRGuardA + k, U0, guardA, DXGI_FORMAT.R16G16_FLOAT);
                SrvTex(TRGuardC + k, 0, src, DXGI_FORMAT.R16G16_FLOAT);
                SrvTex(TRGuardC + k, 1, guardB, DXGI_FORMAT.R16G16_FLOAT);
                if (mvecGuard != null) UavTex(TRGuardC + k, U0, mvecGuard, DXGI_FORMAT.R16G16_FLOAT);
            }
            NullViews(TRGuardB);
            SrvTex(TRGuardB, 0, guardA, DXGI_FORMAT.R16G16_FLOAT);
            UavTex(TRGuardB, U0, guardB, DXGI_FORMAT.R16G16_FLOAT);
        }

        // Tras RecordFgFill (mvec y mvecFg en NON_PIXEL_SHADER_RESOURCE)
        void RecordFgGuard(bool on)
        {
            fgGuardValid = false;
            ID3D12Resource raw = FgMvecRaw;
            bool want = on && s.FgGuard && fgApplied && mvecGuard != null && guardA != null && raw != null && raw != mvecZero;
            if (!want) { LogFgGuard(0); return; }
            int k = raw == mvec ? 0 : raw == mvecFg ? 1 : -1;
            if (k < 0) { LogFgGuard(0); return; }
            if (psoGuardA == null)
            {
                long t = Clock.Now;
                psoGuardA = Pso(CompileCs("fgbordeA", GShaders.GuardA));
                psoGuardB = Pso(CompileCs("fgbordeB", GShaders.GuardB));
                psoGuardC = Pso(CompileCs("fgbordeC", GShaders.GuardC));
                Log.Write("Borde de movimiento para la generacion: shaders (" + Clock.SinceMs(t).ToString("0") + " ms)");
            }
            uint* c = stackalloc uint[16];
            Barrier(guardA, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)bx[0]; c[3] = (uint)by[0];
            BindCompute(psoGuardA, TRGuardA + k, c, 12);
            list.Dispatch(Groups(bx[0], 8), Groups(by[0], 8), 1);
            Barrier(guardA, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);

            Barrier(guardB, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)bx[0]; c[1] = (uint)by[0]; c[2] = (uint)Math.Max(1, Math.Min(16, s.FgGuardRad));
            c[8] = F((float)s.FgGuardDif); c[9] = F((float)s.FgGuardRel);
            BindCompute(psoGuardB, TRGuardB, c, 12);
            list.Dispatch(Groups(bx[0], 8), Groups(by[0], 8), 1);
            Barrier(guardB, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);

            Barrier(mvecGuard, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)bx[0]; c[3] = (uint)by[0];
            BindCompute(psoGuardC, TRGuardC + k, c, 12);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(mvecGuard, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            fgGuardValid = true;
            LogFgGuard(1);
        }

        void LogFgGuard(int st)
        {
            if (st == fgGuardLog) return;
            if (st == 1 || fgGuardLog == 1)
                Log.Write(st == 1 ? "Generacion: a 0 en los bordes de movimiento (fg_borde, radio " + s.FgGuardRad + " bloques, salto > " + s.FgGuardDif.ToString("0.#") + " px + " + s.FgGuardRel.ToString("0.##") + "x)"
                                  : "Generacion: sin el borde de movimiento");
            fgGuardLog = st;
        }
    }

    static class GShaders
    {
        // A) c0: ancho, alto, bloques x/y. Out: el vector del centro del bloque
        public const string GuardA = @"
cbuffer C : register(b0) { uint W, H, BX, BY; uint4 Pad1; uint4 Pad2; uint4 Pad3; }
Texture2D<float2> Mv : register(t0);
RWTexture2D<float2> Out : register(u0);
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= BX || id.y >= BY) return;
    Out[id.xy] = Mv.Load(int3(min(int2(id.xy) * 8 + 4, int2(W - 1, H - 1)), 0));
}";

        // B) c0: bloques x/y, radio | c2.x: salto (px), c2.y: relativo. Out.x = 1 si hay salto cerca
        public const string GuardB = @"
cbuffer C : register(b0) { uint BX, BY, R, P0; uint4 Pad1; float Dif, Rel, P1, P2; uint4 Pad3; }
Texture2D<float2> A : register(t0);
RWTexture2D<float2> Out : register(u0);
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= BX || id.y >= BY) return;
    float2 v = A.Load(int3(id.xy, 0));
    float lv = length(v);
    int r = (int)R;
    float hit = 0;
    for (int j = -r; j <= r && hit == 0; j++)
        for (int i = -r; i <= r; i++)
        {
            if (i * i + j * j > r * r) continue;
            int2 q = int2(id.xy) + int2(i, j);
            if (q.x < 0 || q.y < 0 || q.x >= (int)BX || q.y >= (int)BY) continue;
            float2 n = A.Load(int3(q, 0));
            if (length(n - v) > Dif + Rel * max(lv, length(n))) { hit = 1; break; }
        }
    Out[id.xy] = float2(hit, 0);
}";

        // C) c0: ancho, alto, bloques x/y. Out (mvecGuard): 0 donde hay salto cerca, el vector donde no
        public const string GuardC = @"
cbuffer C : register(b0) { uint W, H, BX, BY; uint4 Pad1; uint4 Pad2; uint4 Pad3; }
Texture2D<float2> Mv : register(t0);
Texture2D<float2> G : register(t1);
RWTexture2D<float2> Out : register(u0);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    float g = G.Load(int3(min(int2(id.xy) / 8, int2(BX - 1, BY - 1)), 0)).x;
    Out[id.xy] = g > 0.5 ? float2(0, 0) : Mv.Load(int3(id.xy, 0));
}";
    }
}
