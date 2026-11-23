using System;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineSrBanco.cs - 23-11-2026 (1.2.4 ronda 7, agente SRBLUR; el usuario: "Mejora la borrosidad de los reescaladores"). Banco de la
    // borrosidad con VIDEO REAL de juego: el video 4K entra como captura 4K (test=archivo), el motor lo reduce a la entrada del escalador
    // (entrada=2560x1440 / 1920x1080) con un filtro de area de verdad (Lanczos-2 ancho, como saldria una imagen de 1440p de la consola, no
    // el Catmull-Rom de 4 puntos, que a 1,5x deja dientes) y el volcado guarda, ademas de la salida, la imagen 4K de la que salio (t_): la
    // verdad exacta de cada imagen, sin otro decodificador. Solo pruebas: de serie nada cambia.
    //   entrada_filtro=0|1   la reduccion de entrada= (0 = Catmull-Rom, como siempre; 1 = Lanczos-2 ancho)
    //   entrada_prueba=1     entrada= tambien con la captura igual a la salida (4K -> 1440p -> 4K; de serie no: en el HOME 4K nativo)
    //   volcado_verdad=1     con volcado=, el mismo recorte de la captura (t_<seq>.raw) si la captura es del tamaño de la salida
    //   sr_jitter=N          (prueba) "jitter propio": la entrada de DLSS SR remuestreada (Lanczos-3) a N posiciones de subpunto (Halton 2,3)
    //                        y el desplazamiento a DLSS (sr_jitter_signo=1|-1): la consola no hace jitter; esto mide si uno de mentira ayuda
    partial class Settings
    {
        public int EntradaFiltro = 0;
        public bool EntradaPrueba = false, DumpTruth = false;
        public int SrJitter = 0, SrJitterSign = 1;

        bool ApplySrBanco(string key, string val)
        {
            switch (key)
            {
                case "entrada_filtro": EntradaFiltro = I(val); return true;
                case "entrada_prueba": EntradaPrueba = B(val); return true;
                case "volcado_verdad": DumpTruth = B(val); return true;
                case "sr_jitter": SrJitter = Math.Max(0, Math.Min(64, I(val))); return true;
                case "sr_jitter_signo": SrJitterSign = I(val) < 0 ? -1 : 1; return true;
            }
            return ApplySrBlur(key, val);   // (EngineSrBlur.cs)
        }
    }

    unsafe partial class Engine
    {
        ID3D12PipelineState psoDownL;

        // Lanczos-2 con el soporte ensanchado a la escala (2 x escala puntos de la captura a cada lado): sin dientes ni "hervido" al reducir
        const string DownLanczosShader = @"
cbuffer C : register(b0) { uint DW, DH, P0, P1; float4 Src; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> In : register(t0);
RWTexture2D<unorm float4> Out : register(u0);
float L2(float x) { x = abs(x); if (x < 1e-4) return 1; if (x >= 2) return 0; float a = 3.14159265 * x; return 2 * sin(a) * sin(a * 0.5) / (a * a); }
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= DW || id.y >= DH) return;
    float2 sz = Src.zw, sc = sz / float2(DW, DH);
    float2 u = (float2(id.xy) + 0.5) * sc;
    int2 a = int2(floor(u - 2 * sc)), b = int2(ceil(u + 2 * sc));
    float3 acc = 0; float wsum = 0;
    [loop] for (int y = a.y; y <= b.y; y++)
    {
        float wy = L2(((float)y + 0.5 - u.y) / sc.y);
        if (wy == 0) continue;
        int yy = clamp(y, 0, (int)sz.y - 1);
        [loop] for (int x = a.x; x <= b.x; x++)
        {
            float wx = L2(((float)x + 0.5 - u.x) / sc.x);
            if (wx == 0) continue;
            float w = wx * wy;
            acc += In.Load(int3(clamp(x, 0, (int)sz.x - 1), yy, 0)).rgb * w; wsum += w;
        }
    }
    Out[id.xy] = float4(saturate(acc / max(wsum, 1e-4)), 1);
}";

        // Engine.cs (1-. la captura reducida a la entrada): la de siempre o la de area
        ID3D12PipelineState DownPso()
        {
            if (s.EntradaFiltro != 1) return psoScale;
            if (psoDownL == null)
            {
                try { psoDownL = Pso(CompileCs("entradalanczos", DownLanczosShader)); Log.Write("Entrada reducida con Lanczos-2 ancho (entrada_filtro=1, prueba)"); }
                catch (Exception ex) { s.EntradaFiltro = 0; Log.Write("entrada_filtro=1: " + ex.Message + " (Catmull-Rom)"); return psoScale; }
            }
            return psoDownL;
        }

        // ---- sr_jitter (prueba) ----
        const int TSrJit = TSrBlur + 2;   // (Engine.cs TablesCount): t0 = inColor, u0 = inJit
        ID3D12PipelineState psoJit;
        ID3D12Resource inJit, inJitFor;
        int jitIndex; float jitX, jitY;
        const string JitShader = @"
cbuffer C : register(b0) { uint W, H, P0, P1; float JX, JY, Q0, Q1; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> In : register(t0);
RWTexture2D<unorm float4> Out : register(u0);
float L3(float x) { x = abs(x); if (x < 1e-4) return 1; if (x >= 3) return 0; float a = 3.14159265 * x; return 3 * sin(a) * sin(a / 3) / (a * a); }
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    float3 acc = 0; float ws = 0;
    [unroll] for (int j = -3; j <= 3; j++)
    {
        float wy = L3(j - JY);
        [unroll] for (int i = -3; i <= 3; i++)
        {
            float w = L3(i - JX) * wy;
            acc += In.Load(int3(clamp(int2(id.xy) + int2(i, j), int2(0, 0), int2(W - 1, H - 1)), 0)).rgb * w; ws += w;
        }
    }
    Out[id.xy] = float4(saturate(acc / ws), 1);
}";

        static float Halton(int i, int b) { float f = 1, r = 0; while (i > 0) { f /= b; r += f * (i % b); i /= b; } return r; }

        bool SrJitterNow { get { return s.SrJitter > 0 && scalerNow == "dlss" && (inW != outW || inH != outH) && inColor != null; } }

        // Run, antes de Sl.SetConstants: el desplazamiento de esta imagen (avanza con cada imagen nueva)
        void SrJitterConstants(ref SlConstants k, int slot)
        {
            if (!SrJitterNow) { jitX = jitY = 0; return; }
            if (slot >= 0) jitIndex = (jitIndex + 1) % s.SrJitter;
            jitX = Halton(jitIndex + 1, 2) - 0.5f; jitY = Halton(jitIndex + 1, 3) - 0.5f;
            k.jitterOffset = new SlFloat2(s.SrJitterSign * jitX, s.SrJitterSign * jitY);
        }

        // RenderFrame, antes de etiquetar la entrada de DLSS SR: la entrada remuestreada (null = inColor tal cual)
        ID3D12Resource SrJitterInput(uint* c)
        {
            if (!SrJitterNow) return null;
            if (inJit == null || !ReferenceEquals(inJitFor, inColor))
            {
                if (inJit != null) Bury(inJit);
                inJit = Tex(ColorFmt, inW, inH, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "entrada con jitter (sr_jitter, prueba)");
                inJitFor = inColor;
                NullViews(TSrJit);
                SrvTex(TSrJit, 0, inColor, ColorFmt);
                UavTex(TSrJit, U0, inJit, ColorFmt);
                if (psoJit == null) { psoJit = Pso(CompileCs("srjitter", JitShader)); Log.Write("DLSS SR con jitter de mentira (sr_jitter=" + s.SrJitter + ", signo " + s.SrJitterSign + "; prueba)"); }
            }
            Barrier(inJit, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[4] = F(jitX); c[5] = F(jitY);
            BindCompute(psoJit, TSrJit, c, 8);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(inJit, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            return inJit;
        }
    }
}
