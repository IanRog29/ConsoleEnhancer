namespace ConsoleEnhancer.Motor
{
    // EngineFgEnhancedGiroShaders.cs - 24-11-2026 (1.3.2, novena ronda, agente EFGGIRO). Los sombreadores del MOVIMIENTO GLOBAL de la camara de
    // Enhanced FG (EngineFgEnhancedGiro.cs): una busqueda de la traslacion de toda la imagen con mucho alcance en el nivel mas grueso de la
    // piramide de luma y su refinado en los dos niveles de debajo. Codigo propio en HLSL (cs_5_1, la firma de raiz comun del motor).
    // El resultado (EfgGiroShaders.Global) va a una textura R32_FLOAT de 8x1 que leen el flujo (EfgShaders.Flow, u1) y la eleccion
    // (EfgShaders.Sel, u1): [0] gx, [1] gy = el vector de C2P (actual -> anterior: A(p) = B(p + g)) en puntos de L0; [2] su coste; [3] el
    // coste del 0; [4] peso de "liso" (donde no hay textura, tirar hacia el global); [5] la textura de referencia; [6] umbral del "suave"
    // (si nada casa, el global por los dos lados); [7] 1 = valido.
    static class EfgGiroShaders
    {
        // ---- Coste de cada traslacion (dx, dy) de +-R en el nivel grueso: media de min(|A(p) - B(p + d)|, Cap) en lo que se solapa (si se solapa
        // al menos MinOv de cada lado) + Lam x |d|, en una rejilla de paso St. Un grupo de 64 hilos por traslacion (los puntos repartidos y sumados en el grupo)
        public const string Cost = @"
cbuffer C : register(b0) { uint LW, LH, R, N; float Cap, Lam, MinOv, Pad0; uint St, P2a, P2b, P2c; uint4 P3; }
Texture2D<float> A : register(t0);
Texture2D<float> B : register(t1);
RWTexture2D<float> Cost : register(u0);
groupshared float sv[64];
[numthreads(64, 1, 1)]
void main(uint3 gid : SV_GroupID, uint3 t : SV_GroupThreadID)
{
    int dx = (int)gid.x - (int)R, dy = (int)gid.y - (int)R;
    int w = (int)LW, h = (int)LH;
    int x0 = max(0, -dx), x1 = min(w, w - dx), y0 = max(0, -dy), y1 = min(h, h - dy);
    int st = max(1, (int)St);
    int nx = max(0, (x1 - x0 + st - 1) / st), ny = max(0, (y1 - y0 + st - 1) / st), n = nx * ny;
    bool ok = x1 - x0 >= (int)(w * MinOv) && y1 - y0 >= (int)(h * MinOv);
    float s = 0;
    if (ok)
        for (int i = (int)t.x; i < n; i += 64)
        {
            int x = x0 + (i % nx) * st, y = y0 + (i / nx) * st;
            s += min(abs(A.Load(int3(x, y, 0)) - B.Load(int3(x + dx, y + dy, 0))), Cap);
        }
    sv[t.x] = s;
    GroupMemoryBarrierWithGroupSync();
    [unroll] for (uint k = 32; k > 0; k >>= 1) { if (t.x < k) sv[t.x] += sv[t.x + k]; GroupMemoryBarrierWithGroupSync(); }
    if (t.x == 0) Cost[gid.xy] = ok && n > 0 ? sv[0] / n + Lam * length(float2(dx, dy)) : 1e6;
}";

        // ---- Un solo grupo: la mejor traslacion del nivel grueso (de Cost) y su refinado +-1 en el nivel de debajo (paso 2; con Dos != 0 tambien
        // en el siguiente), al vector de L0 (x Sc). Escribe tambien los parametros de la eleccion (los pone el motor en las constantes)
        public const string Global = @"
cbuffer C : register(b0) { uint N, R, W1, H1; uint W2, H2, Sc, Valid; float Cap, Mu, MuRef, Fb; uint Dos, P3a, P3b, P3c; }
Texture2D<float> A1 : register(t0);
Texture2D<float> B1 : register(t1);
Texture2D<float> A2 : register(t2);
Texture2D<float> B2 : register(t3);
RWTexture2D<float> G : register(u0);
RWTexture2D<float> Cost : register(u1);
groupshared float sv[256];
groupshared int si[256];
float Ref(Texture2D<float> A, Texture2D<float> B, int w, int h, int2 d, uint tid)
{
    int x0 = max(0, -d.x), x1 = min(w, w - d.x), y0 = max(0, -d.y), y1 = min(h, h - d.y);
    int nx = max(0, (x1 - x0 + 1) / 2), ny = max(0, (y1 - y0 + 1) / 2), n = nx * ny;
    float s = 0;
    for (int i = (int)tid; i < n; i += 256)
    {
        int x = x0 + (i % nx) * 2, y = y0 + (i / nx) * 2;
        s += min(abs(A.Load(int3(x, y, 0)) - B.Load(int3(x + d.x, y + d.y, 0))), Cap);
    }
    sv[tid] = s;
    GroupMemoryBarrierWithGroupSync();
    [unroll] for (uint k = 128; k > 0; k >>= 1) { if (tid < k) sv[tid] += sv[tid + k]; GroupMemoryBarrierWithGroupSync(); }
    float r = (n > 0 && (x1 - x0) * 2 >= w && (y1 - y0) * 2 >= h) ? sv[0] / n : 1e6;
    GroupMemoryBarrierWithGroupSync();
    return r;
}
[numthreads(256, 1, 1)]
void main(uint3 t : SV_GroupThreadID)
{
    uint tid = t.x;
    float bv = 1e9; int bi = (int)(R * N + R);
    for (uint i = tid; i < N * N; i += 256) { float c = Cost[uint2(i % N, i / N)]; if (c < bv) { bv = c; bi = (int)i; } }
    sv[tid] = bv; si[tid] = bi;
    GroupMemoryBarrierWithGroupSync();
    [unroll] for (uint k = 128; k > 0; k >>= 1)
    {
        if (tid < k && (sv[tid + k] < sv[tid] || (sv[tid + k] == sv[tid] && si[tid + k] < si[tid]))) { sv[tid] = sv[tid + k]; si[tid] = si[tid + k]; }
        GroupMemoryBarrierWithGroupSync();
    }
    int2 g = int2(si[0] % (int)N, si[0] / (int)N) - int2((int)R, (int)R);
    float c0 = Cost[uint2(R, R)];
    GroupMemoryBarrierWithGroupSync();
    float best = 1e9;
    // nivel de debajo (+-1 alrededor del doble) y el siguiente
    int2 c1 = g * 2, b1 = c1;
    [loop] for (int j = 0; j < 9; j++)
    {
        int2 d = c1 + int2(j % 3 - 1, j / 3 - 1);
        float c = Ref(A1, B1, (int)W1, (int)H1, d, tid);
        if (c < best) { best = c; b1 = d; }
    }
    int2 c2 = b1 * 2, b2 = b1;
    if (Dos != 0)
    {
    best = 1e9;
    [loop] for (int j2 = 0; j2 < 9; j2++)
    {
        int2 d = c2 + int2(j2 % 3 - 1, j2 / 3 - 1);
        float c = Ref(A2, B2, (int)W2, (int)H2, d, tid);
        if (c < best) { best = c; b2 = d; }
    }
    }
    if (tid == 0)
    {
        bool ok = Valid != 0 && best < 1e5;
        G[uint2(0, 0)] = ok ? (float)(b2.x * (int)Sc) : 0;
        G[uint2(1, 0)] = ok ? (float)(b2.y * (int)Sc) : 0;
        G[uint2(2, 0)] = best;
        G[uint2(3, 0)] = c0;
        G[uint2(4, 0)] = Mu;
        G[uint2(5, 0)] = MuRef;
        G[uint2(6, 0)] = Fb;
        G[uint2(7, 0)] = ok ? 1 : 0;
    }
}";
    }
}
