using System;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineSrBanco.cs - 23-11-2026 (1.2.4 ronda 7, agente SRBLUR; el usuario: "Mejora la borrosidad de los reescaladores"). Banco de la
    // borrosidad con VIDEO REAL de juego: el video 4K entra como captura 4K (test=archivo), el motor lo reduce a la entrada del escalador
    // (entrada=2560x1440 / 1920x1080) con un filtro de area (cada punto = la media de lo que cubre, como saldria una imagen de 1440p de la
    // consola, no el Catmull-Rom de 4 puntos, que a 1,5x deja dientes) y el volcado guarda, ademas de la salida, la imagen 4K de la que salio
    // (t_): la verdad exacta de cada imagen, sin otro decodificador. Solo pruebas: de serie nada cambia.
    //   entrada_filtro=0|1   la reduccion de entrada= (0 = Catmull-Rom, como siempre; 1 = area)
    //   entrada_prueba=1     entrada= tambien con la captura igual a la salida (4K -> 1440p -> 4K; de serie no: en el HOME 4K nativo)
    //   volcado_verdad=1     con volcado=, el mismo recorte de la captura (t_<seq>.raw) si la captura es del tamaño de la salida
    // (1.2.4 ronda 7, INTEGRA7: sin el "jitter de mentira" que probo SRBLUR -sr_jitter-: medido, no ayuda)
    partial class Settings
    {
        public int EntradaFiltro = 0;
        public bool EntradaPrueba = false, DumpTruth = false;

        bool ApplySrBanco(string key, string val)
        {
            switch (key)
            {
                case "entrada_filtro": EntradaFiltro = I(val); return true;
                case "entrada_prueba": EntradaPrueba = B(val); return true;
                case "volcado_verdad": DumpTruth = B(val); return true;
            }
            return ApplySrBlur(key, val);   // (EngineSrBlur.cs)
        }
    }

    unsafe partial class Engine
    {
        ID3D12PipelineState psoDownL;

        // Area: cada punto de la entrada = la media exacta de los puntos de la captura que cubre (con su parte en los bordes)
        const string DownAreaShader = @"
cbuffer C : register(b0) { uint DW, DH, P0, P1; float4 Src; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> In : register(t0);
RWTexture2D<unorm float4> Out : register(u0);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= DW || id.y >= DH) return;
    float2 sz = Src.zw, sc = sz / float2(DW, DH);
    float2 u0 = float2(id.xy) * sc, u1 = u0 + sc;
    int2 a = int2(floor(u0)), b = int2(ceil(u1)) - 1;
    float3 acc = 0; float wsum = 0;
    [loop] for (int y = a.y; y <= b.y; y++)
    {
        float wy = min(u1.y, y + 1.0) - max(u0.y, (float)y);
        if (wy <= 0) continue;
        int yy = clamp(y, 0, (int)sz.y - 1);
        [loop] for (int x = a.x; x <= b.x; x++)
        {
            float wx = min(u1.x, x + 1.0) - max(u0.x, (float)x);
            if (wx <= 0) continue;
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
                try { psoDownL = Pso(CompileCs("entradaarea", DownAreaShader)); Log.Write("Entrada reducida por area (entrada_filtro=1, prueba)"); }
                catch (Exception ex) { s.EntradaFiltro = 0; Log.Write("entrada_filtro=1: " + ex.Message + " (Catmull-Rom)"); return psoScale; }
            }
            return psoDownL;
        }
    }
}
