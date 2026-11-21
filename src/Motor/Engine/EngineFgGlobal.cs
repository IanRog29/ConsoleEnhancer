using System;
using System.Globalization;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 21-11 (1.2.1, artefactos; el usuario: "intenta reducir los errores visuales del fg y de los upscalers"). Lo que recibe la generacion
    // DLSS (DLSS-G) con fg_vectores=3 (de serie desde la 1.0.7: un solo vector, el del giro de la camara):
    //
    // CAUSA (medida, test=<png 4K> desplazada test_giro px por imagen del juego, volcado de FgMvec): el vector "global" era la moda de los
    // vectores de bloque en casillas de 1 px de -32 a +32 (EngineRejas.cs FgGlobal). (1) Un paneo de mas de 32 px por imagen real (a 4K
    // en el modo 30, cualquier giro de camara de mas de ~1/4 de pantalla por segundo) salia SIEMPRE 32: con 100 px de verdad, 32; con 200,
    // 32. (2) Con paneos de 5-64 px a 4K los vectores de bloque van a 0 (mvlargo=0.008 de REJAS castiga cada px desde el vector de la imagen
    // anterior, y el giro de CutGrid solo entra desde 64 px): el global salia 0 con 24 px de verdad. DLSS-G recibia un vector equivocado
    // en casi todos los movimientos de camara y tiraba de su flujo optico (o lo mezclaba con el vector malo).
    // Medido en la tele (Desktop Duplication de cada imagen presentada, la inventada contra la interpolacion ideal de las dos reales
    // vistas, ag_FGFALLOS\tools\fgmide): con el vector exacto, paneo de 100 px 2,81 -> 0,18 de error medio y de 24 px 0,68 -> 0,48.
    //
    // ARREGLO (fg_global=1, de serie): el vector de la camara se busca aparte, sin las reglas de la busqueda por bloques: diferencia media
    // (truncada: lo que se mueve distinto o la interfaz no manda) de toda la imagen entre la actual y la anterior en el nivel 3 de la
    // piramide de luma (1/8) para +-48 x +-24 px de ese nivel (+-384 x +-192 de la entrada), el mejor, y +-6 px alrededor en el nivel 1
    // (1/2) con fraccion por parabola. Solo vale si explica el cambio claramente mejor que quieto (fg_global_ratio). 4 pasadas pequeñas.
    // fg_quieto=1 (de serie): con la camara moviendose, lo QUIETO con textura (interfaz, el personaje que sigue la camara) lleva vector 0
    // en vez del de la camara (por punto, su 3x3: casa en el mismo sitio y no desplazado). Y fg_quieto_prof=1: la profundidad para DLSS-G
    // por capas (lo quieto cerca, lo que va con la camara lejos) en vez de la de los bordes (fg_profundidad), para que en el borde entre
    // los dos sepa cual tapa a cual. fg_global=0 fg_quieto=0 fg_quieto_prof=0 = exactamente lo de antes
    partial class Settings
    {
        public bool FgGlobal = true;                // fg_global=1: el vector de la camara con su propia busqueda (0 = la moda de los bloques, como antes)
        public int FgGlobalRx = 48, FgGlobalRy = 24; // fg_global_radio=x,y: px del nivel 3 (1/8) alrededor de 0
        public double FgGlobalRatio = 0.9;          // fg_global_ratio: el vector solo si su diferencia es < esto x la de quieto
        public double FgGlobalTrunc = 0.08;         // fg_global_corte: diferencia por punto truncada (luma 0-1)
        public bool FgQuiet = true;                 // fg_quieto=1: lo quieto con textura a 0 (interfaz, lo que sigue la camara)
        public bool FgQuietDepth = true;            // fg_quieto_prof=1: profundidad por capas para DLSS-G (con fg_profundidad > 0)
        public double FgQuietTex = 6, FgQuietStill = 2, FgQuietRel = 0.5;   // fg_quieto_textura / _quieto (niveles) / _rel

        bool ApplyFgGlobal(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "fg_global": FgGlobal = Bv(val); return true;
                case "fg_global_radio": { string[] p = val.Split(','); FgGlobalRx = Math.Max(4, Math.Min(64, Iv(p[0]))); FgGlobalRy = p.Length > 1 ? Math.Max(2, Math.Min(64, Iv(p[1]))) : FgGlobalRx / 2; return true; }
                case "fg_global_ratio": FgGlobalRatio = Math.Max(0.1, Math.Min(1, Dv(val))); return true;
                case "fg_global_corte": FgGlobalTrunc = Math.Max(0.01, Math.Min(1, Dv(val))); return true;
                case "fg_quieto": FgQuiet = Bv(val); return true;
                case "fg_quieto_prof": FgQuietDepth = Bv(val); return true;
                case "fg_quieto_textura": FgQuietTex = Math.Max(0, Dv(val)); return true;
                case "fg_quieto_quieto": FgQuietStill = Math.Max(0, Dv(val)); return true;
                case "fg_quieto_rel": FgQuietRel = Math.Max(0.05, Math.Min(1, Dv(val))); return true;
            }
            return false;
        }
        static bool Bv(string v) { v = v.Trim().ToLowerInvariant(); return v == "1" || v == "true" || v == "si" || v == "on" || v == "yes"; }
        static int Iv(string v) { return int.Parse(v.Trim(), CultureInfo.InvariantCulture); }
        static double Dv(string v) { return double.Parse(v.Trim().Replace(',', '.'), CultureInfo.InvariantCulture); }
    }

    unsafe partial class Engine
    {
        // Tablas (EngineRejas.cs TRCount): A y C por juego de luma (actual/anterior), B y D una, la salida por juego
        const int TRGlobA = TRBase + 23 /* + juego */, TRGlobB = TRBase + 25, TRGlobC = TRBase + 26 /* + juego */, TRGlobD = TRBase + 28, TRGlobOut = TRBase + 29 /* + juego */;
        const int GlobRb = 6;      // +-px del nivel 1 alrededor de lo grueso
        ID3D12PipelineState psoGlobA, psoGlobB, psoGlobC, psoGlobD, psoGlobOut;
        ID3D12Resource gSadA, gSadC, gPick;
        int gRx, gRy;
        bool gSaid;

        bool FgGlobalNow { get { return s.FgGlobal && s.FgVec == 3 && Levels >= 4; } }
        // (la profundidad por capas sustituye a la de los bordes de fg_profundidad: EngineRejas.cs RecordFgDepth)
        bool FgLayerDepthNow { get { return s.FgQuiet && s.FgQuietDepth && s.FgDepth > 0 && FgGlobalNow && fgApplied; } }

        void EnsureGlobPsos()
        {
            if (psoGlobA != null) return;
            long t = Clock.Now;
            psoGlobA = Pso(CompileCs("fgglobala", CamShaders.GlobA));
            psoGlobB = Pso(CompileCs("fgglobalb", CamShaders.GlobB));
            psoGlobC = Pso(CompileCs("fgglobalc", CamShaders.GlobC));
            psoGlobD = Pso(CompileCs("fgglobald", CamShaders.GlobD));
            psoGlobOut = Pso(CompileCs("fgglobalsal", CamShaders.GlobOut));
            Log.Write("Generacion: shaders del vector de la camara (" + Clock.SinceMs(t).ToString("0") + " ms)");
        }

        // RejasViews (al cambiar el tamaño de la entrada): sus texturas y tablas
        void FgGlobalViews()
        {
            if (gSadA == null || gRx != s.FgGlobalRx || gRy != s.FgGlobalRy)
            {
                if (gSadA != null) Com.Release(ref gSadA);
                gRx = s.FgGlobalRx; gRy = s.FgGlobalRy;
                gSadA = Tex(DXGI_FORMAT.R32_FLOAT, 2 * gRx + 1, 2 * gRy + 1, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "camara: diferencias gruesas");
            }
            if (gSadC == null) gSadC = Tex(DXGI_FORMAT.R32_FLOAT, 2 * GlobRb + 1, 2 * GlobRb + 2, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "camara: diferencias finas");
            if (gPick == null) gPick = Tex(DXGI_FORMAT.R32G32B32A32_FLOAT, 1, 1, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "camara: lo grueso");
            for (int st = 0; st < 2; st++)
            {
                NullViews(TRGlobA + st);
                SrvTex(TRGlobA + st, 0, luma[st, 3], DXGI_FORMAT.R8_UNORM);
                SrvTex(TRGlobA + st, 1, luma[1 - st, 3], DXGI_FORMAT.R8_UNORM);
                UavTex(TRGlobA + st, U0, gSadA, DXGI_FORMAT.R32_FLOAT);
                NullViews(TRGlobC + st);
                SrvTex(TRGlobC + st, 0, luma[st, 1], DXGI_FORMAT.R8_UNORM);
                SrvTex(TRGlobC + st, 1, luma[1 - st, 1], DXGI_FORMAT.R8_UNORM);
                SrvTex(TRGlobC + st, 2, gPick, DXGI_FORMAT.R32G32B32A32_FLOAT);
                UavTex(TRGlobC + st, U0, gSadC, DXGI_FORMAT.R32_FLOAT);
                NullViews(TRGlobOut + st);
                SrvTex(TRGlobOut + st, 0, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(TRGlobOut + st, 1, luma[1 - st, 0], DXGI_FORMAT.R8_UNORM);
                if (gvec != null) SrvTex(TRGlobOut + st, 2, gvec, DXGI_FORMAT.R32G32_FLOAT);
                if (mvecFg != null) UavTex(TRGlobOut + st, U0, mvecFg, DXGI_FORMAT.R16G16_FLOAT);
                if (depthFg != null) UavTex(TRGlobOut + st, U0 + 1, depthFg, DXGI_FORMAT.R32_FLOAT);
            }
            NullViews(TRGlobB);
            SrvTex(TRGlobB, 0, gSadA, DXGI_FORMAT.R32_FLOAT);
            UavTex(TRGlobB, U0, gPick, DXGI_FORMAT.R32G32B32A32_FLOAT);
            NullViews(TRGlobD);
            SrvTex(TRGlobD, 0, gSadC, DXGI_FORMAT.R32_FLOAT);
            SrvTex(TRGlobD, 2, gPick, DXGI_FORMAT.R32G32B32A32_FLOAT);
            if (gvec != null) UavTex(TRGlobD, U0 + 1, gvec, DXGI_FORMAT.R32G32_FLOAT);
        }

        // RecordFgVec (EngineRejas.cs), imagen nueva con la anterior valida (las dos piramides de luma hechas): el vector de la camara -> gvec
        void RecordFgGlobal(int cur)
        {
            EnsureGlobPsos();
            if (gSadA == null || gPick == null || gSadC == null) return;   // (las hace RejasViews; fg_global_radio vale desde el siguiente cambio de tamaño)
            uint* c = stackalloc uint[16];
            // A) nivel 3: diferencia media truncada de unos 96x54 puntos para cada desplazamiento de +-Rx x +-Ry
            int nx3 = Math.Min(96, lw[3]), ny3 = Math.Min(54, lh[3]);
            Barrier(gSadA, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)lw[3]; c[1] = (uint)lh[3]; c[2] = (uint)gRx; c[3] = (uint)gRy;
            c[4] = (uint)nx3; c[5] = (uint)ny3;
            c[8] = F((float)s.FgGlobalTrunc);
            BindCompute(psoGlobA, TRGlobA + cur, c, 12);
            list.Dispatch(Groups(2 * gRx + 1, 8), Groups(2 * gRy + 1, 8), 1);
            Barrier(gSadA, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            // B) el mejor (un poco mejor cuanto mas corto, ante la duda)
            Barrier(gPick, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)gRx; c[1] = (uint)gRy; c[4] = F(0.002f);
            BindCompute(psoGlobB, TRGlobB, c, 8);
            list.Dispatch(1, 1, 1);
            Barrier(gPick, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            // C) nivel 1: +-6 px alrededor (y el 0, para la fiabilidad), unos 240x135 puntos
            int nx1 = Math.Min(240, lw[1]), ny1 = Math.Min(135, lh[1]);
            Barrier(gSadC, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)lw[1]; c[1] = (uint)lh[1]; c[2] = GlobRb;
            c[4] = (uint)nx1; c[5] = (uint)ny1; c[6] = F((float)lw[1] / Math.Max(1, lw[3]));
            c[8] = F((float)s.FgGlobalTrunc);
            BindCompute(psoGlobC, TRGlobC + cur, c, 12);
            list.Dispatch((uint)(2 * GlobRb + 1), (uint)(2 * GlobRb + 2), 1);
            Barrier(gSadC, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            // D) el mejor fino con fraccion -> gvec, en px de la entrada (0 si no explica el cambio mejor que quieto)
            Barrier(gvec, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = GlobRb; c[1] = F((float)lw[1] / Math.Max(1, lw[3]));
            c[4] = F((float)lw[0] / Math.Max(1, lw[1])); c[5] = F((float)s.FgGlobalRatio); c[6] = F(0.25f);
            BindCompute(psoGlobD, TRGlobD, c, 8);
            list.Dispatch(1, 1, 1);
            Barrier(gvec, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            if (!gSaid)
            {
                gSaid = true;
                Log.Write("Generacion: vector de la camara con su propia busqueda (fg_global=1: +-" + gRx * 8 + " x +-" + gRy * 8 + " px de la entrada" +
                          (s.FgQuiet ? "; lo quieto con textura a 0" + (FgLayerDepthNow ? " y delante (profundidad por capas)" : "") : "") + ")");
            }
        }

        // RecordFgVec, con fg_vectores=3: mvecFg = el vector de la camara, o 0 en lo quieto con textura (fg_quieto); y la profundidad por capas
        void RecordFgGlobalOut(int cur)
        {
            EnsureGlobPsos();
            uint* c = stackalloc uint[16];
            bool dep = FgLayerDepthNow && depthFg != null;
            Barrier(mvecFg, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            if (dep) Barrier(depthFg, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = s.FgQuiet ? 1u : 0u; c[3] = dep ? 1u : 0u;
            c[4] = F((float)(s.FgQuietTex / 255.0)); c[5] = F((float)(s.FgQuietStill / 255.0)); c[6] = F((float)s.FgQuietRel); c[7] = F(1f);
            c[8] = F((float)s.FgDepthNear); c[9] = F((float)s.FgDepthFar);
            BindCompute(psoGlobOut, TRGlobOut + cur, c, 12);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            Barrier(mvecFg, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            if (dep) Barrier(depthFg, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
        }
    }

    static class CamShaders
    {
        // A) c0: ancho y alto del nivel 3, Rx, Ry | c1: puntos en x / y | c2.x (float): corte. Un hilo = un desplazamiento; los puntos de la
        // actual, de 512 en 512 en la memoria del grupo
        public const string GlobA = @"
cbuffer C : register(b0) { uint LW, LH, RX, RY; uint NX, NY, P0, P1; float T, Q0, Q1, Q2; uint4 Pad3; }
Texture2D<float> Cur : register(t0);
Texture2D<float> Prev : register(t1);
RWTexture2D<float> Sad : register(u0);
groupshared float gv[512];
groupshared int2 gp[512];

[numthreads(8, 8, 1)]
void main(uint3 gid : SV_GroupID, uint3 gt : SV_GroupThreadID, uint gi : SV_GroupIndex)
{
    int2 di = int2(gid.xy) * 8 + int2(gt.xy);
    int2 d = di - int2((int)RX, (int)RY);
    bool valid = di.x <= 2 * (int)RX && di.y <= 2 * (int)RY;
    uint N = NX * NY;
    float sum = 0, cnt = 0;
    for (uint base = 0; base < N; base += 512)
    {
        GroupMemoryBarrierWithGroupSync();
        for (uint k = gi; k < 512; k += 64)
        {
            uint idx = base + k;
            if (idx < N)
            {
                uint ix = idx % NX, iy = idx / NX;
                int2 p = int2((ix * 2 + 1) * LW / (2 * NX), (iy * 2 + 1) * LH / (2 * NY));
                gp[k] = p;
                gv[k] = Cur.Load(int3(p, 0));
            }
        }
        GroupMemoryBarrierWithGroupSync();
        uint m = min(512u, N - base);
        if (valid)
        {
            for (uint j = 0; j < m; j++)
            {
                int2 q = gp[j] + d;
                if (q.x >= 0 && q.y >= 0 && q.x < (int)LW && q.y < (int)LH) { sum += min(abs(gv[j] - Prev.Load(int3(q, 0))), T); cnt += 1; }
            }
        }
    }
    if (valid) Sad[di] = cnt >= 0.25 * (float)N ? sum / cnt : 1.0;
}";

        // B) c0: Rx, Ry | c1.x (float): castigo por px. El menor -> Pick = (dx, dy, su diferencia, la de quieto)
        public const string GlobB = @"
cbuffer C : register(b0) { uint RX, RY, P0, P1; float Bias, Q0, Q1, Q2; uint4 Pad2; uint4 Pad3; }
Texture2D<float> Sad : register(t0);
RWTexture2D<float4> Pick : register(u0);
groupshared float bc[1024];
groupshared uint bi[1024];

[numthreads(1024, 1, 1)]
void main(uint3 gt : SV_GroupThreadID)
{
    uint t = gt.x;
    uint W = 2 * RX + 1, H = 2 * RY + 1, N = W * H;
    float best = 1e9;
    uint bidx = RY * W + RX;
    for (uint k = t; k < N; k += 1024)
    {
        int2 d = int2(k % W, k / W) - int2((int)RX, (int)RY);
        float v = Sad.Load(int3(k % W, k / W, 0)) * (1.0 + Bias * length(float2(d)));
        if (v < best) { best = v; bidx = k; }
    }
    bc[t] = best; bi[t] = bidx;
    GroupMemoryBarrierWithGroupSync();
    for (uint s = 512; s > 0; s >>= 1)
    {
        if (t < s && bc[t + s] < bc[t]) { bc[t] = bc[t + s]; bi[t] = bi[t + s]; }
        GroupMemoryBarrierWithGroupSync();
    }
    if (t == 0)
    {
        uint k = bi[0];
        int2 d = int2(k % W, k / W) - int2((int)RX, (int)RY);
        Pick[int2(0, 0)] = float4(float2(d), Sad.Load(int3(k % W, k / W, 0)), Sad.Load(int3(RX, RY, 0)));
    }
}";

        // C) c0: ancho y alto del nivel 1, R | c1: puntos x / y, escala del nivel 3 al 1 (float) | c2.x: corte. Un grupo = un
        // desplazamiento (lo grueso x escala +-R); la ultima fila, el 0
        public const string GlobC = @"
cbuffer C : register(b0) { uint LW, LH, RB, P0; uint NX, NY; float Scale; uint P1; float T, Q0, Q1, Q2; uint4 Pad3; }
Texture2D<float> Cur : register(t0);
Texture2D<float> Prev : register(t1);
Texture2D<float4> Pick : register(t2);
RWTexture2D<float> Sad : register(u0);
groupshared float ss[256];
groupshared float sc[256];

[numthreads(256, 1, 1)]
void main(uint3 gid : SV_GroupID, uint3 gt : SV_GroupThreadID)
{
    uint t = gt.x;
    bool skip = gid.y == 2 * RB + 1 && gid.x != 0;   // (la fila del 0: solo su primer grupo)
    int2 c = int2(round(Pick.Load(int3(0, 0, 0)).xy * Scale));
    int2 d = gid.y == 2 * RB + 1 ? int2(0, 0) : c + int2(gid.xy) - int2((int)RB, (int)RB);
    uint N = skip ? 0 : NX * NY;
    float sum = 0, cnt = 0;
    for (uint k = t; k < N; k += 256)
    {
        uint ix = k % NX, iy = k / NX;
        int2 p = int2((ix * 2 + 1) * LW / (2 * NX), (iy * 2 + 1) * LH / (2 * NY));
        int2 q = p + d;
        if (q.x >= 0 && q.y >= 0 && q.x < (int)LW && q.y < (int)LH) { sum += min(abs(Cur.Load(int3(p, 0)) - Prev.Load(int3(q, 0))), T); cnt += 1; }
    }
    ss[t] = sum; sc[t] = cnt;
    GroupMemoryBarrierWithGroupSync();
    for (uint s = 128; s > 0; s >>= 1)
    {
        if (t < s) { ss[t] += ss[t + s]; sc[t] += sc[t + s]; }
        GroupMemoryBarrierWithGroupSync();
    }
    if (t == 0 && !skip) Sad[int2(gid.xy)] = sc[0] >= 0.25 * (float)N ? ss[0] / sc[0] : 1.0;
}";

        // D) c0: R, escala del 3 al 1 (float) | c1 (float): escala del 1 a la entrada, ratio, largo minimo (px). -> G (px de la entrada)
        public const string GlobD = @"
cbuffer C : register(b0) { uint RB; float Scale; uint P0, P1; float Up, Ratio, MinLen, Q0; uint4 Pad2; uint4 Pad3; }
Texture2D<float> Sad : register(t0);
Texture2D<float4> Pick : register(t2);
RWTexture2D<float2> G : register(u1);
groupshared float bc[256];
groupshared uint bi[256];

[numthreads(256, 1, 1)]
void main(uint3 gt : SV_GroupThreadID)
{
    uint t = gt.x, W = 2 * RB + 1, N = W * W;
    float best = 1e9;
    uint bidx = RB * W + RB;
    if (t < N) { best = Sad.Load(int3(t % W, t / W, 0)); bidx = t; }
    bc[t] = best; bi[t] = bidx;
    GroupMemoryBarrierWithGroupSync();
    for (uint s = 128; s > 0; s >>= 1)
    {
        if (t < s && bc[t + s] < bc[t]) { bc[t] = bc[t + s]; bi[t] = bi[t + s]; }
        GroupMemoryBarrierWithGroupSync();
    }
    if (t != 0) return;
    uint k = bi[0];
    int ix = (int)(k % W), iy = (int)(k / W);
    float b = bc[0];
    float s0 = Sad.Load(int3(0, 2 * RB + 1, 0));
    int2 c = int2(round(Pick.Load(int3(0, 0, 0)).xy * Scale));
    float2 v = float2(c + int2(ix, iy) - int2((int)RB, (int)RB));
    if (ix > 0 && ix < (int)W - 1)
    {
        float l = Sad.Load(int3(ix - 1, iy, 0)), r = Sad.Load(int3(ix + 1, iy, 0)), a = l - 2 * b + r;
        if (a > 1e-7) v.x += clamp(0.5 * (l - r) / a, -0.5, 0.5);
    }
    if (iy > 0 && iy < (int)W - 1)
    {
        float u = Sad.Load(int3(ix, iy - 1, 0)), dn = Sad.Load(int3(ix, iy + 1, 0)), a = u - 2 * b + dn;
        if (a > 1e-7) v.y += clamp(0.5 * (u - dn) / a, -0.5, 0.5);
    }
    v *= Up;
    bool ok = b < Ratio * s0 && dot(v, v) >= MinLen * MinLen;
    G[int2(0, 0)] = ok ? v : float2(0, 0);
}";

        // Salida, por punto de la entrada: c0: ancho, alto, quieto (0/1), profundidad (0/1) | c1 (float): textura minima (max - min del
        // 3x3), diferencia maxima sin mover (media), proporcion frente a la desplazada, largo minimo del vector | c2: cerca, lejos.
        // Las tres teselas (actual, anterior en el sitio, anterior desplazada con el vector) en la memoria del grupo
        public const string GlobOut = @"
cbuffer C : register(b0) { uint W, H, Quiet, Depth; float TexT, StillT, Ratio, MinG; float Near, Far, Q0, Q1; uint4 Pad3; }
Texture2D<float> Cur : register(t0);
Texture2D<float> Prev : register(t1);
Texture2D<float2> G : register(t2);
RWTexture2D<float2> Out : register(u0);
RWTexture2D<float> Dep : register(u1);
SamplerState Lin : register(s0);
groupshared float tc[180];
groupshared float tp[180];
groupshared float tg[180];

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID, uint3 gt : SV_GroupThreadID, uint3 gid : SV_GroupID, uint gi : SV_GroupIndex)
{
    float2 g = G.Load(int3(0, 0, 0));
    int2 o = int2(gid.xy) * int2(16, 8) - 1;
    float2 inv = 1.0 / float2(W, H);
    for (uint k = gi; k < 180; k += 128)
    {
        int2 q = clamp(o + int2(k % 18, k / 18), int2(0, 0), int2((int)W - 1, (int)H - 1));
        tc[k] = Cur.Load(int3(q, 0));
        tp[k] = Prev.Load(int3(q, 0));
        tg[k] = Prev.SampleLevel(Lin, (float2(q) + 0.5 + g) * inv, 0);
    }
    GroupMemoryBarrierWithGroupSync();
    if (id.x >= W || id.y >= H) return;
    bool still = false;
    if (Quiet != 0 && dot(g, g) >= MinG * MinG)
    {
        float s0 = 0, sg = 0, mn = 1, mx = 0;
        [unroll] for (int j = 0; j < 3; j++)
            [unroll] for (int i = 0; i < 3; i++)
            {
                uint k2 = (gt.y + j) * 18 + gt.x + i;
                float cv = tc[k2];
                mn = min(mn, cv); mx = max(mx, cv);
                s0 += abs(cv - tp[k2]); sg += abs(cv - tg[k2]);
            }
        still = mx - mn >= TexT && s0 <= 9.0 * StillT && s0 < Ratio * sg;
    }
    Out[id.xy] = still ? float2(0, 0) : g;
    if (Depth != 0) Dep[id.xy] = still ? Near : Far;
}";
    }
}
