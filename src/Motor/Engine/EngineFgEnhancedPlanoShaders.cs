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
cbuffer C : register(b0) { uint BX, BY, P0, P1; float VarThr, CostThr, R0, FracThr; uint4 P2; uint4 P3; }
Texture2D<float4> F : register(t0);
RWTexture2D<float> G : register(u0);
groupshared float sx[256], sy[256], sn[256], stt[256];
[numthreads(256, 1, 1)]
void main(uint3 t : SV_GroupThreadID)
{
    uint tid = t.x, n = BX * BY;
    float2 m = G[uint2(7, 0)] > 0.5 ? float2(G[uint2(0, 0)], G[uint2(1, 0)]) : float2(0, 0);
    float nin = 0, ntot = 0;
    [loop] for (int it = 0; it < 3; it++)
    {
        float r = it == 0 ? R0 : (it == 1 ? 3.0 : 1.0);
        float ax = 0, ay = 0, an = 0, at = 0;
        for (uint i = tid; i < n; i += 256)
        {
            float4 f = F.Load(int3(i % BX, i / BX, 0));
            if (f.w <= VarThr || f.z > CostThr) continue;
            at += 1;
            float2 d = f.xy - m;
            if (dot(d, d) <= r * r) { ax += f.x; ay += f.y; an += 1; }
        }
        sx[tid] = ax; sy[tid] = ay; sn[tid] = an; stt[tid] = at;
        GroupMemoryBarrierWithGroupSync();
        [unroll] for (uint k = 128; k > 0; k >>= 1)
        {
            if (tid < k) { sx[tid] += sx[tid + k]; sy[tid] += sy[tid + k]; sn[tid] += sn[tid + k]; stt[tid] += stt[tid + k]; }
            GroupMemoryBarrierWithGroupSync();
        }
        if (sn[0] > 0) { m = float2(sx[0], sy[0]) / sn[0]; nin = sn[0]; }
        ntot = stt[0];
        GroupMemoryBarrierWithGroupSync();
    }
    if (tid == 0)
    {
        float frac = ntot > 0 ? nin / ntot : 0;
        G[uint2(9, 0)] = m.x; G[uint2(10, 0)] = m.y; G[uint2(11, 0)] = frac; G[uint2(12, 0)] = ntot;
        G[uint2(14, 0)] = (G[uint2(7, 0)] > 0.5 && ntot > 16 && frac >= FracThr && dot(m, m) > 0.25) ? 1 : 0;
        G[uint2(20, 0)] = (G[uint2(7, 0)] > 0.5 && ntot > 16 && frac >= FracThr) ? 1 : 0;   // (traslacion fiable, aunque sea 0: para la aceleracion)
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
    float2 d = float2(G[uint2(9, 0)], G[uint2(10, 0)]) + float2((int)gid.x - h, (int)gid.y - h) * Step;
    float2 inv = 1.0 / float2(W0, H0);
    uint nx = (W0 - 16) / Sp, ny = (H0 - 16) / Sp, n = nx * ny;
    float s = 0;
    if (G[uint2(14, 0)] > 0.5)
        for (uint i = tid; i < n; i += 256)
        {
            float2 p = float2(8 + (i % nx) * Sp, 8 + (i / nx) * Sp) + 0.5;
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
    G[uint2(19, 0)] = 0;
    if (G[uint2(14, 0)] < 0.5)
    {
        G[uint2(8, 0)] = 0; G[uint2(13, 0)] = 0;
        Cost[uint2(60, 0)] = G[uint2(9, 0)]; Cost[uint2(61, 0)] = G[uint2(10, 0)]; Cost[uint2(62, 0)] = G[uint2(20, 0)];
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
        float2 A = (-g / I2 + pg / I1) * (2 * I2 * I2 / (I1 + I2)); float lim = AcLim * sqrt(dot(g, g) + dot(pg, pg)), al = length(A);
        if (al > lim && al > 0) A *= lim / al;
        G[uint2(17, 0)] = A.x; G[uint2(18, 0)] = A.y; G[uint2(19, 0)] = 1;
    }
    Cost[uint2(60, 0)] = g.x; Cost[uint2(61, 0)] = g.y; Cost[uint2(62, 0)] = 1;
    G[uint2(8, 0)] = Mu; G[uint2(13, 0)] = ZeroPen; G[uint2(15, 0)] = Cerca; G[uint2(16, 0)] = CercaN;
}";
    }
}
