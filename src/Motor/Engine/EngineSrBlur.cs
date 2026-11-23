using System;
using System.Globalization;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    partial class Settings
    {
        // 23-11 (ronda 7, SRBLUR) sr_nitidez_mov=X: con un escalador temporal que reescala, enfoque CAS de mas donde la imagen se mueve
        // (x la velocidad de sus vectores entre sr_nitidez_mov_px=a,b px de la entrada): en movimiento el escalador pierde detalle (historia
        // rechazada o remuestreada) y quieto no (alli el enfoque solo aumenta el "hervido"). 0 = el enfoque de siempre (sharp=)
        public double SrSharpMov = 0, SrSharpMovA = 0.5, SrSharpMovB = 3;

        bool ApplySrBlur(string key, string val)
        {
            switch (key)
            {
                case "sr_nitidez_mov": SrSharpMov = Math.Max(0, Math.Min(1, D(val))); return true;
                case "sr_nitidez_mov_px": { string[] p = val.Split(','); SrSharpMovA = Math.Max(0, D(p[0])); if (p.Length > 1) SrSharpMovB = Math.Max(SrSharpMovA + 0.1, D(p[1])); return true; }
            }
            return false;
        }
    }

    // EngineSrBlur.cs - 23-11-2026 (1.2.4 ronda 7, agente SRBLUR; el usuario, tras probar con su PS5: "Mejora la borrosidad de los
    // reescaladores"; antes: "todos los dlss siguen teniendo borrosidades en sitios al girar la camara y en el suelo sin ni siquiera girar").
    unsafe partial class Engine
    {
        // (Engine.cs AutoPresetTick) El modelo elegido no se cambia por el pico de rehacer DLSS 5. Log del usuario 23-11 12:10:40-41 y
        // 12:11:43-44 (1440p, DLSS M, DLSS 5 nivel 4): Adapta bajo DLSS 5 del 90 al 80 % (lo que tocaba: con DLSS 5 primero se baja su %) y
        // al segundo siguiente "DLSS M no cabe aqui (grafica p95 46,1 / 91,2 ms, tope 23,4 / 30)": el p95 era el de la imagen que rehacia
        // DLSS 5. AdaptaBajar ponia overSecs = 0, pero AutoPresetTick va detras en el mismo segundo y lo volvia a contar: al siguiente, M -> K, y
        // se aprendio (scales_engine.txt "preset=K pct=80 pedido=M": las veces siguientes, K de entrada; 12:14:21 "DLSS M -> K").
        // Ahora: con adapta=1 y DLSS 5 que aun puede bajar (mas del 25 % o mas de una pasada), lo que no cabe lo baja Adapta (como dice
        // EngineAdapta.cs) y el modelo no; y nunca en los 5 s tras rehacer DLSS 5 ni en los 8 s tras un cambio de Adapta (los suyos)
        bool AutoPresetEspera()
        {
            if (nrCreatedAt != 0 && Clock.SinceMs(nrCreatedAt) < 5000) return true;
            if (!s.Adapta) return false;
            if (adCambioAt != 0 && Clock.SinceMs(adCambioAt) < 8000) return true;
            return AdNrActivo && (s.NrPct > 25 || s.NrPasses > 1);
        }

        // ---- sr_nitidez_mov: el CAS de Shaders.cs con la nitidez por punto (t1 = mvec, a la resolucion de la entrada) ----
        const string CasMovShader = @"
cbuffer C : register(b0) { uint W, H, P0, P1; float4 S; float4 M; uint4 Pad2; }
Texture2D<float4> In : register(t0);
Texture2D<float2> Mv : register(t1);
RWTexture2D<unorm float4> Out : register(u0);
float3 L(int2 p) { return In.Load(int3(clamp(p, int2(0, 0), int2(W - 1, H - 1)), 0)).rgb; }
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int2 p = int2(id.xy);
    float2 v = Mv.Load(int3(int2(float2(id.xy) * M.w), 0));
    float sx = saturate(S.x + M.x * smoothstep(M.y, M.z, length(v)));
    if (sx <= 0.001) { Out[id.xy] = float4(L(p), 1); return; }
    float3 a = L(p + int2(-1, -1)), b = L(p + int2(0, -1)), c = L(p + int2(1, -1));
    float3 d = L(p + int2(-1, 0)),  e = L(p),               f = L(p + int2(1, 0));
    float3 g = L(p + int2(-1, 1)),  h = L(p + int2(0, 1)),  i = L(p + int2(1, 1));
    float3 mn = min(min(min(d, e), min(f, b)), h);
    float3 mn2 = min(mn, min(min(a, c), min(g, i)));
    mn += mn2;
    float3 mx = max(max(max(d, e), max(f, b)), h);
    float3 mx2 = max(mx, max(max(a, c), max(g, i)));
    mx += mx2;
    float3 amp = saturate(min(mn, 2.0 - mx) / max(mx, 1e-5));
    amp = sqrt(amp);
    float peak = -1.0 / lerp(8.0, 5.0, sx);
    float3 w = amp * peak;
    float3 rw = 1.0 / (1.0 + 4.0 * w);
    Out[id.xy] = float4(saturate((b * w + d * w + f * w + h * w + e) * rw), 1);
}";
        ID3D12PipelineState psoCasMov;
        bool casMovBroken, casMovSaid;
        ID3D12Resource casMovMv;

        // Engine.cs (3. CAS): con sr_nitidez_mov y un escalador temporal que reescala (no con RCAS ni en la pantalla sin señal), el CAS por
        // punto; si no, el de siempre. c[0..7] ya puestos (los de siempre); aqui c[8..11]
        ID3D12PipelineState CasPso(bool native, uint* c, out uint n)
        {
            n = 8;
            bool tmp = !native && (scalerNow == "dlss" || scalerNow == "xess" || scalerNow == "ffx") && mvec != null;
            if (s.SrSharpMov <= 0 || casMovBroken || !tmp || noSignalScreen || RcasNow) return psoCas;
            if (psoCasMov == null)
            {
                try { psoCasMov = Pso(CompileCs("casmov", CasMovShader)); }
                catch (Exception ex) { casMovBroken = true; Log.Write("sr_nitidez_mov: " + ex.Message + " (el enfoque de siempre)"); return psoCas; }
            }
            if (!ReferenceEquals(casMovMv, mvec)) { SrvTex(TCasOut, 1, mvec, DXGI_FORMAT.R16G16_FLOAT); casMovMv = mvec; }
            if (!casMovSaid)
            {
                casMovSaid = true;
                Log.Write(string.Format(CultureInfo.InvariantCulture, "Enfoque: CAS {0:0.00} quieto y hasta {1:0.00} en movimiento ({2:0.#}-{3:0.#} px de la entrada por imagen; sr_nitidez_mov)",
                    s.Sharp, Math.Min(1, s.Sharp + s.SrSharpMov), s.SrSharpMovA, s.SrSharpMovB));
            }
            c[5] = F(0f);   // (con nitidez 0 quieto tambien: lo decide el shader por punto)
            c[8] = F((float)s.SrSharpMov); c[9] = F((float)s.SrSharpMovA); c[10] = F((float)s.SrSharpMovB); c[11] = F((float)inW / outW);
            n = 12;
            return psoCasMov;
        }
    }
}
