using System;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineSrVec.cs - 23-11-2026 (1.2.4 ronda 6, agente DLSSSR; el usuario: "Los reescalados dlss a partir del 4 dejan mucha borrosidad al
    // mover la camara"). Vectores propios de DLSS SR (y de XeSS / FSR 3.1-4 de escalador: SrMvec), afinados a fracciones de punto, en
    // mvecSr (la etiqueta local de EvaluateDlss, EngineMotion.cs). Los de siempre (mvec) NO cambian: DLSS 5 y su antiparpadeo, la generacion
    // y su vector de la camara siguen con ellos exactamente igual.
    // Por que: la busqueda por bloques de 8x8 (Shaders.cs Search) da enteros + una fraccion por la V del SAD, y prefiere el 0 si casi casa
    // (mvsesgo / mvruido: 2 niveles por punto de margen; solo un giro de >= 64 px de la entrada se lo salta). Con camara lenta y textura
    // suave el bloque se queda a 0 o con la fraccion corrida y los modelos transformer (K/L/M) traen la historia desalineada: emborronan.
    // Aqui, por casilla de 4x4 con el mismo vector, Lucas-Kanade (gradiente de la imagen de ahora, la anterior con muestreo bilineal, las
    // muestras de una ventana que llevan ese mismo vector) unas pocas vueltas desde el vector de la busqueda; se queda con el que mejor casa
    // y solo si casa claramente mejor que el de la busqueda (sr_vec_ganancia). Las casillas con vectores distintos (bordes de la interfaz,
    // del personaje) pasan tal cual.
    // Claves (motor_extra o la linea del motor): sr_vectores=0|1, sr_vec_radio (px de la ventana), sr_vec_paso, sr_vec_vueltas,
    // sr_vec_max (px), sr_vec_ganancia, sr_vec_lambda.
    partial class Settings
    {
        public int SrVec = 0;
        public int SrVecRadius = 6, SrVecStep = 2, SrVecIter = 3;
        public double SrVecMax = 1.5, SrVecGain = 0.7, SrVecLambda = 6e-5;

        bool ApplySrVec(string key, string val)
        {
            switch (key)
            {
                case "sr_vectores": SrVec = I(val); return true;
                case "sr_vec_radio": SrVecRadius = Math.Max(2, Math.Min(16, I(val))); return true;
                case "sr_vec_paso": SrVecStep = Math.Max(1, Math.Min(4, I(val))); return true;
                case "sr_vec_vueltas": SrVecIter = Math.Max(1, Math.Min(6, I(val))); return true;
                case "sr_vec_max": SrVecMax = Math.Max(0.1, Math.Min(4, D(val))); return true;
                case "sr_vec_ganancia": SrVecGain = Math.Max(0.1, Math.Min(1, D(val))); return true;
                case "sr_vec_lambda": SrVecLambda = Math.Max(0, D(val)); return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        const int TSrVec = TNr + 9;   // + juego de luma (2): t0 = luma de ahora, t1 = la anterior, t2 = mvec, u0 = mvecSr (Engine.cs: TablesCount)
        ID3D12PipelineState psoSrVec;

        const string SrVecShader = @"
cbuffer C : register(b0) { uint W, H, TW, TH; float MaxD, Gain, Lambda, P0; uint Iter, WinR, Step, P1; uint4 Pad; }
Texture2D<float> Cur : register(t0);
Texture2D<float> Prev : register(t1);
Texture2D<float2> Mv : register(t2);
RWTexture2D<float2> Out : register(u0);
SamplerState Lin : register(s0);

float LdC(int2 p) { return Cur.Load(int3(clamp(p, int2(0, 0), int2(W - 1, H - 1)), 0)); }
float2 MvAt(int2 p) { return Mv.Load(int3(clamp(p, int2(0, 0), int2(W - 1, H - 1)), 0)); }

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= TW || id.y >= TH) return;
    int2 t0 = int2(id.xy) * 4;
    float2 v0 = MvAt(t0 + int2(2, 2));
    bool uni = true;
    [unroll] for (int j = 0; j < 4; j++)
        [unroll] for (int i = 0; i < 4; i++) { float2 d = MvAt(t0 + int2(i, j)) - v0; if (abs(d.x) + abs(d.y) > 0.05) uni = false; }
    float2 best = v0;
    if (uni)
    {
        float2 v = v0, inv = 1.0 / float2(W, H);
        float ssd0 = 0, bestSsd = 1e9;
        int r = (int)WinR, st = (int)Step;
        int2 c = t0 + int2(2, 2);
        [loop] for (uint it = 0; it <= Iter; it++)
        {
            float bx = 0, by = 0, e = 0, hxx = 0, hxy = 0, hyy = 0, n = 0;
            [loop] for (int y = -r; y < r; y += st)
                [loop] for (int x = -r; x < r; x += st)
                {
                    int2 q = c + int2(x, y);
                    if (q.x < 1 || q.y < 1 || q.x >= (int)W - 1 || q.y >= (int)H - 1) continue;
                    float2 dq = MvAt(q) - v0;
                    if (abs(dq.x) + abs(dq.y) > 0.5) continue;   // (otra cosa: otro movimiento)
                    float cc = LdC(q);
                    float gx = 0.5 * (LdC(q + int2(1, 0)) - LdC(q - int2(1, 0)));
                    float gy = 0.5 * (LdC(q + int2(0, 1)) - LdC(q - int2(0, 1)));
                    float pp = Prev.SampleLevel(Lin, (float2(q) + 0.5 + v) * inv, 0);
                    float rr = cc - pp;
                    e += rr * rr; bx += gx * rr; by += gy * rr;
                    hxx += gx * gx; hxy += gx * gy; hyy += gy * gy; n += 1;
                }
            if (n < 8) break;
            if (it == 0) ssd0 = e;
            if (e < bestSsd) { bestSsd = e; best = v; }
            if (it == Iter) break;
            float l = Lambda * n, a = hxx + l, dd = hyy + l, det = a * dd - hxy * hxy;
            if (det <= 1e-12) break;
            float2 d = float2((dd * bx - hxy * by) / det, (a * by - hxy * bx) / det);
            v += clamp(d, -1.0, 1.0);
            float2 dv = v - v0; float ld = length(dv);
            if (ld > MaxD) v = v0 + dv * (MaxD / ld);
        }
        if (!(bestSsd < Gain * ssd0)) best = v0;
    }
    [unroll] for (int j2 = 0; j2 < 4; j2++)
        [unroll] for (int i2 = 0; i2 < 4; i2++)
        {
            int2 p = t0 + int2(i2, j2);
            if (p.x < (int)W && p.y < (int)H) Out[p] = uni ? best : MvAt(p);
        }
}";

        // CreateMotionResources (EngineMotion.cs), con la piramide, mvec y mvecSr ya hechos
        void SrVecViews()
        {
            for (int st = 0; st < 2; st++)
            {
                int t = TSrVec + st;
                NullViews(t);
                SrvTex(t, 0, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(t, 1, luma[1 - st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(t, 2, mvec, DXGI_FORMAT.R16G16_FLOAT);
                UavTex(t, U0, mvecSr, DXGI_FORMAT.R16G16_FLOAT);
            }
        }

        // RecordMotion (EngineMotion.cs), con mvec ya hecho de esta imagen (en NON_PIXEL_SHADER_RESOURCE) y cur = el juego de luma de ahora.
        // Solo para un escalador temporal que reescala (DLSS SR; XeSS / FSR 3.1-4 leen SrMvec); con mvmask_modo=1 no (usa mvecSr)
        void RecordSrVec(int cur, bool on, uint* c)
        {
            if (s.SrVec <= 0 || !on || srVecValid || s.MvMaskMode == 1 || s.MvFixed || mvecSr == null) return;
            bool sr = (inW != outW || inH != outH) && (scalerNow == "dlss" || scalerNow == "xess" || scalerNow == "ffx");
            if (!sr) return;
            if (psoSrVec == null)
            {
                long t = Clock.Now;
                psoSrVec = Pso(CompileCs("srvec", SrVecShader));
                Log.Write("Vectores de DLSS SR afinados (sr_vectores=" + s.SrVec + "): shader en " + Clock.SinceMs(t).ToString("0") + " ms");
            }
            int tw = (inW + 3) / 4, th = (inH + 3) / 4;
            Barrier(mvecSr, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)tw; c[3] = (uint)th;
            c[4] = F((float)s.SrVecMax); c[5] = F((float)s.SrVecGain); c[6] = F((float)s.SrVecLambda);
            c[8] = (uint)s.SrVecIter; c[9] = (uint)s.SrVecRadius; c[10] = (uint)s.SrVecStep;
            BindCompute(psoSrVec, TSrVec + cur, c, 12);
            list.Dispatch(Groups(tw, 8), Groups(th, 8), 1);
            Barrier(mvecSr, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            srVecValid = true;
        }
    }
}
