namespace ConsoleEnhancer.Motor
{
    // EngineFgEnhancedPlanoShaders.cs - 24-11-2026 (1.3.3, decima ronda, agente EFGMAPA). Los sombreadores del PANEO PLANO de Enhanced FG
    // (EngineFgEnhancedPlano.cs): un mapa / menu que se desplaza entero con el joystick. Codigo propio en HLSL (cs_5_1, la firma de raiz
    // comun del motor). Escriben en la textura del movimiento global de EFGGIRO (efgGiroG, R32_FLOAT de 16x1; [0]-[7] son de EFGGIRO):
    // [8] peso de la eleccion (0 = no es un paneo plano), [9] gx, [10] gy = el global PRECISO (C2P: actual -> anterior, puntos de L0),
    // [11] la parte de los bloques con textura que lo llevan, [12] cuantos bloques con textura, [13] castigo del 0 en la inventada,
    // [14] 1 = la parte llega (antes del refinado), [15] radio (puntos de L0) de "cerca de la interfaz quieta" (ni candidato ni castigo del 0), [16] cuantas reales seguidas sin cambiar hacen falta para eso (en un mapa
    // con grano un punto junto a un borde casa 3 seguidas por casualidad: con 10, no); [17], [18] la aceleracion del paneo (puntos de L0, movimiento), [19] 1 = valida, [20] traslacion fiable
    // (aunque sea 0). El coste usa [0]-[48] de su textura y [60]-[62] guardan el global de la pareja anterior
    static class EfgPlanoShaders
    {
        // ---- Un grupo: media recortada de los vectores de L0 (C2P, tras el apoyo) de los bloques con textura que casan, desde el global
        // grueso de EFGGIRO ([0], [1]), en tres vueltas (radio R0, 3 y 1 punto), y la parte que lo lleva
        public const string Fit = @"
cbuffer C : register(b0) { uint BX, BY, Sim, P1; float VarThr, CostThr, R0, FracThr; float CX, CY, P2a, P2b; uint4 P3; }
Texture2D<float4> F : register(t0);
RWTexture2D<float> G : register(u0);
groupshared float sa[9 * 256];
groupshared float4 mdl;
// (1.3.3 ronda 10d) con Sim: semejanza d(x) = t + s u + r perp(u), u = (centro del bloque - centro) / 512 (zoom + paneo, y giro si sale);
// sin Sim: traslacion (media recortada). Tres vueltas con los bloques a menos de R0 / 3 / 1 punto del modelo
[numthreads(256, 1, 1)]
void main(uint3 t : SV_GroupThreadID)
{
    uint tid = t.x, n = BX * BY;
    if (tid == 0) mdl = float4(G[uint2(7, 0)] > 0.5 ? float2(G[uint2(0, 0)], G[uint2(1, 0)]) : float2(0, 0), 0, 0);
    GroupMemoryBarrierWithGroupSync();
    float nin = 0, ntot = 0;
    [loop] for (int it = 0; it < 3; it++)
    {
        float r = it == 0 ? R0 : (it == 1 ? 3.0 : 1.0);
        float4 M = mdl;
        float a[9] = { 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        for (uint i = tid; i < n; i += 256)
        {
            float4 f = F.Load(int3(i % BX, i / BX, 0));
            if (f.w <= VarThr || f.z > CostThr) continue;
            a[8] += 1;
            float2 u = (float2(i % BX, i / BX) * 8 + 4 - float2(CX, CY)) / 512.0;
            float2 pr = M.xy + M.z * u + M.w * float2(-u.y, u.x);
            float2 d = f.xy - pr;
            if (dot(d, d) <= r * r)
            {
                a[0] += 1; a[1] += u.x; a[2] += u.y; a[3] += dot(u, u); a[4] += f.x; a[5] += f.y; a[6] += dot(u, f.xy); a[7] += -u.y * f.x + u.x * f.y;
            }
        }
        [unroll] for (int q = 0; q < 9; q++) sa[(q) * 256 + tid] = a[q];
        GroupMemoryBarrierWithGroupSync();
        [unroll] for (uint k = 128; k > 0; k >>= 1)
        {
            if (tid < k) { [unroll] for (int q2 = 0; q2 < 9; q2++) sa[(q2) * 256 + tid] += sa[(q2) * 256 + tid + k]; }
            GroupMemoryBarrierWithGroupSync();
        }
        if (tid == 0 && sa[(0) * 256 + 0] > 0)
        {
            float nn = sa[(0) * 256 + 0];
            float4 m2 = float4(sa[(4) * 256 + 0] / nn, sa[(5) * 256 + 0] / nn, 0, 0);
            if (Sim != 0 && nn >= 32)
            {
                // [n 0 Sux -Suy; 0 n Suy Sux; Sux Suy Suu 0; -Suy Sux 0 Suu] x = [Sdx Sdy Sud Spd]  (Gauss con pivote)
                float A[4][5] = { { nn, 0, sa[(1) * 256 + 0], -sa[(2) * 256 + 0], sa[(4) * 256 + 0] }, { 0, nn, sa[(2) * 256 + 0], sa[(1) * 256 + 0], sa[(5) * 256 + 0] },
                                  { sa[(1) * 256 + 0], sa[(2) * 256 + 0], sa[(3) * 256 + 0], 0, sa[(6) * 256 + 0] }, { -sa[(2) * 256 + 0], sa[(1) * 256 + 0], 0, sa[(3) * 256 + 0], sa[(7) * 256 + 0] } };
                // (ecuaciones normales: simetrica y definida positiva, sin pivote)
                bool ok = true;
                [unroll] for (int c = 0; c < 4; c++)
                {
                    ok = ok && abs(A[c][c]) > 1e-6;
                    float dc = ok ? A[c][c] : 1;
                    [unroll] for (int rr2 = 0; rr2 < 4; rr2++) if (rr2 != c) { float fct = A[rr2][c] / dc; [unroll] for (int j2 = 0; j2 < 5; j2++) A[rr2][j2] -= fct * A[c][j2]; }
                }
                if (ok) m2 = float4(A[0][4] / A[0][0], A[1][4] / A[1][1], A[2][4] / A[2][2], Sim > 1 ? A[3][4] / A[3][3] : 0);
            }
            mdl = m2;
        }
        if (sa[(0) * 256 + 0] > 0) nin = sa[(0) * 256 + 0];
        ntot = sa[(8) * 256 + 0];
        GroupMemoryBarrierWithGroupSync();
    }
    if (tid == 0)
    {
        float4 m = mdl;
        float frac = ntot > 0 ? nin / ntot : 0;
        float rz = length(float2(CX, CY)) / 512.0 * length(m.zw);   // (lo que mueve el zoom / giro en la esquina, puntos de L0)
        G[uint2(9, 0)] = m.x; G[uint2(10, 0)] = m.y; G[uint2(11, 0)] = frac; G[uint2(12, 0)] = ntot;
        G[uint2(21, 0)] = m.z; G[uint2(22, 0)] = m.w; G[uint2(23, 0)] = CX; G[uint2(24, 0)] = CY;
        G[uint2(14, 0)] = (G[uint2(7, 0)] > 0.5 && ntot > 16 && frac >= FracThr && (dot(m.xy, m.xy) > 0.25 || rz > 0.5)) ? 1 : 0;
        G[uint2(20, 0)] = (G[uint2(7, 0)] > 0.5 && ntot > 16 && frac >= FracThr) ? 1 : 0;   // (modelo fiable, aunque sea 0: para la aceleracion)
        G[uint2(8, 0)] = 0; G[uint2(13, 0)] = 0;
    }
}";

        // ---- Un grupo por desplazamiento (NxN alrededor del de Fit, pasos de Step puntos de L0): la diferencia SIMETRICA de luma en L0 (las
        // dos imagenes a medio camino: el mismo emborronado bilineal a los dos lados, sin sesgo hacia los puntos enteros) en una rejilla de
        // paso Sp, recortada a Cap. Al coste (u1)
        public const string Cost = @"
cbuffer C : register(b0) { uint W0, H0, N, Sp; float Step, Cap, P0, P1; uint4 P2; uint4 P3; }
Texture2D<float> A : register(t0);   // la actual
Texture2D<float> B : register(t1);   // la anterior
RWTexture2D<float> G : register(u0);
RWTexture2D<float> Cost : register(u1);
SamplerState Lin : register(s0);
groupshared float sv[256];
[numthreads(256, 1, 1)]
void main(uint3 gid : SV_GroupID, uint3 t : SV_GroupThreadID)
{
    uint tid = t.x;
    int h = (int)N / 2;
    float2 d0 = float2(G[uint2(9, 0)], G[uint2(10, 0)]) + float2((int)gid.x - h, (int)gid.y - h) * Step;
    float sz = G[uint2(21, 0)], rz = G[uint2(22, 0)]; float2 cz = float2(G[uint2(23, 0)], G[uint2(24, 0)]);
    float2 inv = 1.0 / float2(W0, H0);
    uint nx = (W0 - 16) / Sp, ny = (H0 - 16) / Sp, n = nx * ny;
    float s = 0;
    if (G[uint2(14, 0)] > 0.5)
        for (uint i = tid; i < n; i += 256)
        {
            float2 p = float2(8 + (i % nx) * Sp, 8 + (i / nx) * Sp) + 0.5;
            float2 u = (p - cz) / 512.0, d = d0 + sz * u + rz * float2(-u.y, u.x);
            float a = A.SampleLevel(Lin, (p - d * 0.5) * inv, 0), b = B.SampleLevel(Lin, (p + d * 0.5) * inv, 0);
            s += min(abs(a - b), Cap);
        }
    sv[tid] = s;
    GroupMemoryBarrierWithGroupSync();
    [unroll] for (uint k = 128; k > 0; k >>= 1) { if (tid < k) sv[tid] += sv[tid + k]; GroupMemoryBarrierWithGroupSync(); }
    if (tid == 0) Cost[uint2(gid.y * N + gid.x, 0)] = n > 0 ? sv[0] / n : 1e6;
}";

        // ---- Un hilo: el minimo de la rejilla de costes, parabola en x (su fila) y en y (su columna), y el peso de la eleccion y el castigo del 0
        public const string Fin = @"
cbuffer C : register(b0) { uint N, Acel, P1, P2; float Step, Mu, ZeroPen, Cerca; float CercaN, AcLim, I1, I2; uint4 P5; }
RWTexture2D<float> G : register(u0);
RWTexture2D<float> Cost : register(u1);
float Cs(int x, int y) { return Cost[uint2(y * (int)N + x, 0)]; }
[numthreads(1, 1, 1)]
void main()
{
    // (ronda 10b) la pareja anterior (global preciso y si era fiable) en Cost[60..62]: la aceleracion del paneo
    float2 pg = float2(Cost[uint2(60, 0)], Cost[uint2(61, 0)]); bool pv = Cost[uint2(62, 0)] > 0.5;
    float2 pzr = float2(Cost[uint2(63, 0)], Cost[uint2(64, 0)]), zr = float2(G[uint2(21, 0)], G[uint2(22, 0)]);
    G[uint2(19, 0)] = 0;
    if (G[uint2(14, 0)] < 0.5)
    {
        G[uint2(8, 0)] = 0; G[uint2(13, 0)] = 0;
        Cost[uint2(60, 0)] = G[uint2(9, 0)]; Cost[uint2(61, 0)] = G[uint2(10, 0)]; Cost[uint2(62, 0)] = G[uint2(20, 0)];
        Cost[uint2(63, 0)] = zr.x; Cost[uint2(64, 0)] = zr.y;
        return;
    }
    int n = (int)N, h = n / 2, bx = h, by = h;
    float bv = 1e9;
    for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float c = Cs(x, y); if (c < bv) { bv = c; bx = x; by = y; } }
    float ox = (bx - h) * Step, oy = (by - h) * Step;
    if (bx > 0 && bx < n - 1) { float l = Cs(bx - 1, by), r = Cs(bx + 1, by), den = l - 2 * bv + r; if (den > 1e-9) ox += Step * clamp(0.5 * (l - r) / den, -0.5, 0.5); }
    if (by > 0 && by < n - 1) { float u = Cs(bx, by - 1), dd = Cs(bx, by + 1), den = u - 2 * bv + dd; if (den > 1e-9) oy += Step * clamp(0.5 * (u - dd) / den, -0.5, 0.5); }
    G[uint2(9, 0)] = G[uint2(9, 0)] + ox; G[uint2(10, 0)] = G[uint2(10, 0)] + oy;
    float2 g = float2(G[uint2(9, 0)], G[uint2(10, 0)]);
    if (Acel != 0 && pv)
    {
        // con los intervalos de verdad (hora del contenido, ms): v = d / I, a = (v2 - v1) / ((I1 + I2) / 2) y A = a I2^2 (con I1 = I2,
        // A = M2 - M1; movimiento = -C2P), acotada a AcLim x (|M1| + |M2|): la inventada del instante T va a M T - A T (1 - T) / 2
        // (ronda 10d) con zoom: los 4 parametros del modelo a la vez (la aceleracion de cada punto es lineal en ellos); el tope se mira en las
        // cuatro esquinas y el centro y se aplica a todos
        float kk = 2 * I2 * I2 / (I1 + I2);
        float2 A = (-g / I2 + pg / I1) * kk, Az = (-zr / I2 + pzr / I1) * kk;
        float2 cz = float2(G[uint2(23, 0)], G[uint2(24, 0)]); float esc = 1;
        [unroll] for (int q = 0; q < 5; q++)
        {
            float2 u = q == 4 ? float2(0, 0) : float2((q & 1) ? 1 : -1, (q & 2) ? 1 : -1) * cz / 512.0, pu = float2(-u.y, u.x);
            float2 a = A + Az.x * u + Az.y * pu, d2 = g + zr.x * u + zr.y * pu, d1 = pg + pzr.x * u + pzr.y * pu;
            float lim = AcLim * sqrt(dot(d2, d2) + dot(d1, d1)), al = length(a);
            if (al > lim && al > 0) esc = min(esc, lim / al);
        }
        A *= esc; Az *= esc;
        G[uint2(17, 0)] = A.x; G[uint2(18, 0)] = A.y; G[uint2(25, 0)] = Az.x; G[uint2(26, 0)] = Az.y; G[uint2(19, 0)] = 1;
    }
    Cost[uint2(60, 0)] = g.x; Cost[uint2(61, 0)] = g.y; Cost[uint2(62, 0)] = 1; Cost[uint2(63, 0)] = zr.x; Cost[uint2(64, 0)] = zr.y;
    G[uint2(8, 0)] = Mu; G[uint2(13, 0)] = ZeroPen; G[uint2(15, 0)] = Cerca; G[uint2(16, 0)] = CercaN;
}";
    }
}
