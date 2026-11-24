namespace ConsoleEnhancer.Motor
{
    // EngineFgEnhancedShaders.cs - 23-11-2026 (1.2.4, sexta ronda, agente ENHANCED). Los sombreadores de "Enhanced FG" (EngineFgEnhanced.cs):
    // codigo propio en HLSL de DirectCompute (cs_5_1, la firma de raiz comun del motor: b0 = 16 constantes, t0-t7, u0-u1, s0 lineal), sin nada de
    // NVIDIA, AMD ni Intel: va en cualquier grafica con D3D12.
    // Coordenadas: "L0" = media resolucion de la salida (W/2 x H/2), donde vive el flujo; los bloques son de 8x8 puntos de cada nivel (16x16 a la
    // salida en L0). Un campo de flujo A->B guarda por bloque de A el desplazamiento d tal que A(p) = B(p + d) (en puntos de su nivel), su coste
    // (diferencia media de luma 0-1) y lo texturado que es el bloque. "C2P" = de la actual a la anterior (en los bloques de la actual), "P2C" = de la
    // anterior a la actual. El movimiento m (de la anterior a la actual) es -C2P o P2C.
    static class EfgShaders
    {
        // ---- RGBA de la salida -> luma a media resolucion (un punto = la media de 2x2, una muestra bilineal en su centro)
        public const string Luma = @"
cbuffer C : register(b0) { uint W0, H0, W, H; uint4 P1; uint4 P2; uint4 P3; }
Texture2D<float4> Src : register(t0);
RWTexture2D<float> Dst : register(u0);
SamplerState Lin : register(s0);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W0 || id.y >= H0) return;
    float3 c = Src.SampleLevel(Lin, (float2(id.xy) * 2 + 1) / float2(W, H), 0).rgb;
    Dst[id.xy] = dot(c, float3(0.299, 0.587, 0.114));
}";

        // ---- luma de un nivel -> el siguiente (mitad)
        public const string Down = @"
cbuffer C : register(b0) { uint DW, DH, SW, SH; uint4 P1; uint4 P2; uint4 P3; }
Texture2D<float> Src : register(t0);
RWTexture2D<float> Dst : register(u0);
SamplerState Lin : register(s0);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= DW || id.y >= DH) return;
    Dst[id.xy] = Src.SampleLevel(Lin, (float2(id.xy) * 2 + 1) / float2(SW, SH), 0);
}";

        // ---- Flujo de un nivel (de lo grueso a lo fino), por bloques de 8x8 de A buscados en B. Candidatos: en el nivel mas grueso busqueda
        // entera de +-R; en los demas el vector del bloque padre y de sus 4 vecinos (x2), el 0 y el de la pareja anterior (temporal, solo L0);
        // despues +-SR alrededor del mejor (EFG2: efg_busqueda en los niveles gruesos, de serie 1; +-1 en L0) y, en L0, medio y cuarto de punto. Coste = diferencia
        // media + Lambda x lo que se aparta del padre.
        // Flags: 1 = hay padre, 2 = hay temporal, 8 = subpunto. (1.3.2, EFGGIRO) GFlags: 1 = el movimiento global (u1) como candidato
        public const string Flow = @"
cbuffer C : register(b0) { uint LW, LH, BX, BY; uint R, Flags, PBX, PBY; float Lambda, ZeroBias, TmpSign, GScale; uint SR, GFlags, Pad2, Pad3; }
Texture2D<float> A : register(t0);
Texture2D<float> B : register(t1);
Texture2D<float4> Par : register(t2);
Texture2D<float4> Tmp : register(t3);
RWTexture2D<float4> Out : register(u0);
RWTexture2D<float> Glob : register(u1);   // (1.3.2, EFGGIRO) el movimiento global de la camara (EfgGiroShaders.Global), con GFlags
SamplerState Lin : register(s0);
static float a[64];
static float2 inv, o0, pred, bd;
static float best;
float Sad(float2 d)
{
    float s = 0;
    [unroll] for (int y = 0; y < 8; y++)
        [unroll] for (int x = 0; x < 8; x++)
            s += abs(a[y * 8 + x] - B.SampleLevel(Lin, (o0 + float2(x, y) + d) * inv, 0));
    return s * (1.0 / 64);
}
void Try(float2 d)
{
    float c = Sad(d) + Lambda * length(d - pred);
    if (c < best) { best = c; bd = d; }
}
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= BX || id.y >= BY) return;
    inv = 1.0 / float2(LW, LH);
    int2 p0 = int2(id.xy) * 8;
    o0 = float2(p0) + 0.5;
    float mean = 0;
    [unroll] for (int y = 0; y < 8; y++)
        [unroll] for (int x = 0; x < 8; x++)
        {
            float v = A.Load(int3(min(p0 + int2(x, y), int2(LW - 1, LH - 1)), 0));
            a[y * 8 + x] = v; mean += v;
        }
    mean *= 1.0 / 64;
    float var = 0;
    [unroll] for (int i = 0; i < 64; i++) var += abs(a[i] - mean);
    var *= 1.0 / 64;
    pred = 0; bd = 0;
    if (Flags & 1)
    {
        int2 pb = min(int2(id.xy) / 2, int2(PBX - 1, PBY - 1));
        pred = Par.Load(int3(pb, 0)).xy * 2;
    }
    best = Sad(0) + Lambda * length(pred) - ZeroBias;
    if (Flags & 1)
    {
        int2 pb = min(int2(id.xy) / 2, int2(PBX - 1, PBY - 1));
        Try(pred);
        int2 o[4] = { int2(1, 0), int2(-1, 0), int2(0, 1), int2(0, -1) };
        [unroll] for (int k = 0; k < 4; k++)
        {
            int2 q = clamp(pb + o[k], int2(0, 0), int2(PBX - 1, PBY - 1));
            Try(Par.Load(int3(q, 0)).xy * 2);
        }
    }
    else
    {
        int r = (int)R;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                Try(float2(dx, dy));
    }
    if (Flags & 2) Try(Tmp.Load(int3(id.xy, 0)).xy * TmpSign);
    if (GFlags & 1)
    {
        // (1.3.2, EFGGIRO) el movimiento global como candidato: en el nivel mas grueso con +-2 alrededor (llega mas lejos que la busqueda
        // de +-R: los giros rapidos con base 30), en los demas tal cual (el padre puede haberse equivocado)
        float2 g = round(float2(Glob[uint2(0, 0)], Glob[uint2(1, 0)]) * GScale * TmpSign);
        if (Glob[uint2(7, 0)] > 0.5)
        {
            if (Flags & 1) Try(g);
            else
                [loop] for (int gy = -2; gy <= 2; gy++)
                    [loop] for (int gx = -2; gx <= 2; gx++)
                        Try(g + float2(gx, gy));
        }
    }
    float2 c0 = round(bd);
    if (Flags & 1)
    {
        int sr = max(1, (int)SR);
        for (int dy = -sr; dy <= sr; dy++)
            for (int dx = -sr; dx <= sr; dx++)
                if (dx != 0 || dy != 0) Try(c0 + float2(dx, dy));
    }
    if (Flags & 8)
    {
        float2 c1 = bd;
        Try(c1 + float2(0.5, 0)); Try(c1 - float2(0.5, 0)); Try(c1 + float2(0, 0.5)); Try(c1 - float2(0, 0.5));
        float2 c2 = bd;
        Try(c2 + float2(0.25, 0)); Try(c2 - float2(0.25, 0)); Try(c2 + float2(0, 0.25)); Try(c2 - float2(0, 0.25));
    }
    Out[id.xy] = float4(bd, Sad(bd), var);
}";

        // ---- Propagacion en L0: cada bloque se queda con su vector o el de un vecino de 3x3, el que menos cuesta descontando el apoyo (cuantos
        // de los 9 lo llevan): quita los vectores sueltos sin borrar los bordes de lo que se mueve
        public const string Prop = @"
cbuffer C : register(b0) { uint LW, LH, BX, BY; float Beta, Pad0, Pad1, Pad2; uint4 P2; uint4 P3; }
Texture2D<float> A : register(t0);
Texture2D<float> B : register(t1);
Texture2D<float4> Raw : register(t2);
RWTexture2D<float4> Out : register(u0);
SamplerState Lin : register(s0);
static float a[64];
static float2 inv, o0;
float Sad(float2 d)
{
    float s = 0;
    [unroll] for (int y = 0; y < 8; y++)
        [unroll] for (int x = 0; x < 8; x++)
            s += abs(a[y * 8 + x] - B.SampleLevel(Lin, (o0 + float2(x, y) + d) * inv, 0));
    return s * (1.0 / 64);
}
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= BX || id.y >= BY) return;
    inv = 1.0 / float2(LW, LH);
    int2 p0 = int2(id.xy) * 8;
    o0 = float2(p0) + 0.5;
    [unroll] for (int y = 0; y < 8; y++)
        [unroll] for (int x = 0; x < 8; x++)
            a[y * 8 + x] = A.Load(int3(min(p0 + int2(x, y), int2(LW - 1, LH - 1)), 0));
    float2 c[9];
    float4 own = Raw.Load(int3(id.xy, 0));
    [unroll] for (int i = 0; i < 9; i++)
    {
        int2 q = clamp(int2(id.xy) + int2(i % 3 - 1, i / 3 - 1), int2(0, 0), int2(BX - 1, BY - 1));
        c[i] = Raw.Load(int3(q, 0)).xy;
    }
    float bestC = 1e9, bestS = own.z; float2 bestD = own.xy;
    [unroll] for (int i = 0; i < 9; i++)
    {
        float sup = 0;
        [unroll] for (int j = 0; j < 9; j++) sup += length(c[j] - c[i]) < 0.75 ? 1 : 0;
        float s = i == 4 ? own.z : Sad(c[i]);
        float cc = s - Beta * sup * (1.0 / 9) - (i == 4 ? 0.002 : 0);
        if (cc < bestC) { bestC = cc; bestS = s; bestD = c[i]; }
    }
    Out[id.xy] = float4(bestD, bestS, own.w);
}";

        // ---- Quieto en L0: imagenes seguidas sin cambiar (x) y si tiene borde (y): la interfaz quieta con texto, marcos...
        public const string Still = @"
cbuffer C : register(b0) { uint W0, H0, Valid, Pad0; float Thr, Grad, MaxN, Pad1; uint4 P2; uint4 P3; }
Texture2D<float> Lc : register(t0);
Texture2D<float> Lp : register(t1);
Texture2D<float2> Old : register(t2);
RWTexture2D<float2> Dst : register(u0);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W0 || id.y >= H0) return;
    int2 p = int2(id.xy), mx = int2(W0 - 1, H0 - 1);
    float c = Lc.Load(int3(p, 0)), q = Lp.Load(int3(p, 0));
    float g = max(max(abs(Lc.Load(int3(min(p + int2(1, 0), mx), 0)) - c), abs(Lc.Load(int3(max(p - int2(1, 0), 0), 0)) - c)),
                  max(abs(Lc.Load(int3(min(p + int2(0, 1), mx), 0)) - c), abs(Lc.Load(int3(max(p - int2(0, 1), 0), 0)) - c)));
    float n = Valid != 0 ? Old.Load(int3(p, 0)).x : 0;
    n = abs(c - q) <= Thr ? min(n + 1, MaxN) : 0;
    Dst[id.xy] = float2(n, g >= Grad ? 1 : 0);
}";

        // ---- Corte de escena (un solo grupo): de los bloques con textura, la parte que no casa ni con su mejor vector
        public const string Cut = @"
cbuffer C : register(b0) { uint BX, BY, P0, P1; float CostThr, VarThr, Pad0, Pad1; uint4 P2; uint4 P3; }
Texture2D<float4> F : register(t0);
RWTexture2D<float> Dst : register(u0);
groupshared uint nt[256], nb[256];
[numthreads(256, 1, 1)]
void main(uint3 tid : SV_GroupThreadID)
{
    uint t = 0, b = 0, n = BX * BY;
    for (uint i = tid.x; i < n; i += 256)
    {
        float4 f = F.Load(int3(i % BX, i / BX, 0));
        if (f.w > VarThr) { t++; if (f.z > CostThr) b++; }
    }
    nt[tid.x] = t; nb[tid.x] = b;
    GroupMemoryBarrierWithGroupSync();
    for (uint s = 128; s > 0; s >>= 1)
    {
        if (tid.x < s) { nt[tid.x] += nt[tid.x + s]; nb[tid.x] += nb[tid.x + s]; }
        GroupMemoryBarrierWithGroupSync();
    }
    if (tid.x == 0) Dst[uint2(0, 0)] = nt[0] > 16 ? (float)nb[0] / nt[0] : 0;
}";

        // ---- Eleccion del movimiento a un cuarto de resolucion para el instante T (0 = anterior, 1 = actual; > 1 = hacia delante desde la actual).
        // Para cada candidato m (de los dos campos alrededor, el 0 y dos proyectados) el punto del instante T esta en la anterior en x - T m y en la
        // actual en x + (1 - T) m: si las dos muestras casan (5 tomas de luma) se ve en las dos (se mezclan por el tiempo); si no, es una oclusion y
        // vale el lado cuyo campo dice lo mismo que m (el "lado bueno"): lo destapado sale de la actual y lo tapado de la anterior. Lo quieto con
        // borde (interfaz) cuenta como algo que tapa: un m que no es 0 no puede coger su muestra de ahi. Sel = (m en L0, peso de la actual, coste)
        public const string Sel = @"
cbuffer C : register(b0) { uint QW, QH, BX, BY; float T, Kappa, Tau, StillN; uint W0, H0, Extrap, Opts; uint BX1, BY1, BX3, BY3; }
Texture2D<float> Lp : register(t0);
Texture2D<float> Lc : register(t1);
Texture2D<float4> Fp : register(t2);
Texture2D<float4> Fc : register(t3);
Texture2D<float2> St : register(t4);
Texture2D<float4> Fp1 : register(t5);   // los de L1 (bloques de 32 puntos de la salida): el fondo entre barrotes finos
Texture2D<float4> Fc1 : register(t6);
Texture2D<float4> Fc3 : register(t7);   // y uno de L3 (128 puntos): casi el de la camara
RWTexture2D<float4> Sel : register(u0);
RWTexture2D<float> Glob : register(u1);  // (1.3.2, EFGGIRO) el movimiento global de la camara (EfgGiroShaders.Global), con Opts 512 / 1024 / 2048
SamplerState Lin : register(s0);
static float2 inv0, invB;
static float bestS, bestW;
static float2 bestM;
static float pw;
static float2 pm;
float Lp1(float2 p) { return Lp.SampleLevel(Lin, p * inv0, 0); }
float Lc1(float2 p) { return Lc.SampleLevel(Lin, p * inv0, 0); }
bool StillAt(float2 p)
{
    float2 s = St.Load(int3(clamp(int2(p), int2(0, 0), int2(W0 - 1, H0 - 1)), 0));
    return s.x >= StillN && s.y > 0.5;
}
void Try(float2 x, float2 m)
{
    float2 qp = x - T * m, qc = x + (1 - T) * m;
    float D = abs(Lp1(qp) - Lc1(qc));
    D += abs(Lp1(qp + float2(1.5, 0)) - Lc1(qc + float2(1.5, 0)));
    D += abs(Lp1(qp - float2(1.5, 0)) - Lc1(qc - float2(1.5, 0)));
    D += abs(Lp1(qp + float2(0, 1.5)) - Lc1(qc + float2(0, 1.5)));
    D += abs(Lp1(qp - float2(0, 1.5)) - Lc1(qc - float2(0, 1.5)));
    D *= 0.2;
    float2 mp = Fp.SampleLevel(Lin, qp * invB, 0).xy;
    float2 mc = -Fc.SampleLevel(Lin, qc * invB, 0).xy;
    float Ep = length(mp - m), Ec = length(mc - m);
    bool moving = dot(m, m) > 0.25;
    float2 lim = float2(W0, H0);
    if ((moving && StillAt(qp)) || any(qp < 0) || any(qp > lim)) Ep += 100;
    if ((moving && StillAt(qc)) || any(qc < 0) || any(qc > lim)) Ec += 100;
    float S2 = D + Kappa * 0.25 * min(Ep + Ec, 16) + (Ep >= 100 || Ec >= 100 ? 1 : 0);
    float S1 = Tau + Kappa * min(Extrap != 0 ? Ec : min(Ep, Ec), 16);
    float s, w;
    if (S2 <= S1) { s = S2; w = T; } else { s = S1; w = Ec <= Ep ? 1 : 0; }
    if (Extrap != 0) w = 1;
    s += pw * min(length(m - pm), 16);
    if (s < bestS) { bestS = s; bestM = m; bestW = w; }
}
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= QW || id.y >= QH) return;
    inv0 = 1.0 / float2(W0, H0);
    invB = 1.0 / (8.0 * float2(BX, BY));
    float2 x = float2(id.xy) * 2 + 1;
    int2 b0 = int2(floor((x - 4) / 8));
    bestS = 1e9; bestW = T; bestM = 0;
    // (1.3.2, EFGGIRO) el movimiento global de la camara: candidato (512), liso = donde el bloque no tiene textura se tira hacia el (1024),
    // suave = si nada casa ni por un lado, el global por los dos lados (2048). Solo al interpolar
    bool gOk = Extrap == 0 && (Opts & 3584) != 0 && Glob[uint2(7, 0)] > 0.5;
    float2 gm = gOk ? -float2(Glob[uint2(0, 0)], Glob[uint2(1, 0)]) : float2(0, 0);
    pw = 0; pm = gm;
    if (gOk && (Opts & 1024))
    {
        int2 bq = clamp(int2(floor(x / 8 - 0.5)), int2(0, 0), int2(BX - 1, BY - 1));
        pw = Glob[uint2(4, 0)] * max(0, 1 - Fc.Load(int3(bq, 0)).w / max(Glob[uint2(5, 0)], 1e-4));
    }
    // (1.3.3, EFGMAPA) paneo plano (un mapa / menu que se desplaza entero, EngineFgEnhancedPlano.cs): el global preciso es candidato y los
    // demas pagan por apartarse de el (las lineas rectas no resbalan), salvo en la interfaz quieta. Solo al interpolar
    bool cerca = false;
    if (Extrap == 0 && (Opts & 4096) != 0 && Glob[uint2(8, 0)] > 0)
        [unroll] for (int ci = 0; ci < 9; ci++)
        {
            float2 sq = St.Load(int3(clamp(int2(x + float2(ci % 3 - 1, ci / 3 - 1) * Glob[uint2(15, 0)]), int2(0, 0), int2(W0 - 1, H0 - 1)), 0));
            cerca = cerca || (sq.x >= Glob[uint2(16, 0)] && sq.y > 0.5);
        }
    if (Extrap == 0 && (Opts & 4096) != 0 && Glob[uint2(8, 0)] > 0 && !cerca)
    {
        pw = Glob[uint2(8, 0)]; pm = -float2(Glob[uint2(9, 0)], Glob[uint2(10, 0)]);
        Try(x, pm);
    }
    if (Opts & 256)
    {
        // (EFGCAL) los 3x3 bloques alrededor (no solo los 2x2 mas cercanos): el vector bueno de un borde a veces es de un bloque mas alla
        int2 cb = int2(floor(x / 8));
        [loop] for (uint i = 0; i < 9; i++)
        {
            int2 b = clamp(cb + int2((int)(i % 3) - 1, (int)(i / 3) - 1), int2(0, 0), int2(BX - 1, BY - 1));
            Try(x, Fp.Load(int3(b, 0)).xy);
            Try(x, -Fc.Load(int3(b, 0)).xy);
        }
    }
    else
    [unroll] for (int i = 0; i < 4; i++)
    {
        int2 b = clamp(b0 + int2(i & 1, i >> 1), int2(0, 0), int2(BX - 1, BY - 1));
        Try(x, Fp.Load(int3(b, 0)).xy);
        Try(x, -Fc.Load(int3(b, 0)).xy);
    }
    Try(x, float2(0, 0));
    if (gOk && (Opts & 512)) Try(x, gm);
    float2 u1 = x / (16.0 * float2(BX1, BY1));
    Try(x, Fp1.SampleLevel(Lin, u1, 0).xy * 2);
    Try(x, -Fc1.SampleLevel(Lin, u1, 0).xy * 2);
    Try(x, -Fc3.SampleLevel(Lin, x / (64.0 * float2(BX3, BY3)), 0).xy * 8);
    float2 m0 = bestM;
    Try(x, Fp.SampleLevel(Lin, (x - T * m0) * invB, 0).xy);
    Try(x, -Fc.SampleLevel(Lin, (x + (1 - T) * m0) * invB, 0).xy);
    int loc = (int)(Opts & 255);
    if (loc > 0)
    {
        // (EFGCAL) busqueda local +-loc puntos de L0 alrededor del elegido: lo fino que se mueve distinto de su bloque (postes, barras)
        float2 c0 = bestM;
        [loop] for (int dy = -loc; dy <= loc; dy++)
            [loop] for (int dx = -loc; dx <= loc; dx++)
                if (dx != 0 || dy != 0) Try(x, c0 + float2(dx, dy));
    }
    if (gOk && (Opts & 2048) && bestS > Glob[uint2(6, 0)]) { bestM = gm; bestW = T; }
    Sel[id.xy] = float4(bestM, bestW, bestS);
}";

        // ---- La inventada a la resolucion de salida: de las 4 elecciones de un cuarto de resolucion que la rodean (y el 0), la que mejor casa en
        // ESTE punto (las dos muestras de color; las de oclusion con su coste), mezcladas por lo bien que casan. Con un corte de escena, la real
        // mas cercana en el tiempo
        public const string Fin = @"
cbuffer C : register(b0) { uint W, H, QW, QH; float T, ZeroPen, StillN, Sigma; uint Extrap, CutCpu, W0, H0; float CutThr, TauF; uint FOpts; float Pad3; }
Texture2D<float4> Prev : register(t0);
Texture2D<float4> Cur : register(t1);
Texture2D<float4> Sel : register(t2);
Texture2D<float2> St : register(t3);
Texture2D<float> CutT : register(t4);
RWTexture2D<unorm float4> Out : register(u0);
RWTexture2D<float> Glob : register(u1);   // (1.3.3, EFGMAPA) la del movimiento global / paneo plano: [13] castigo del 0, con FOpts 2
SamplerState Lin : register(s0);
float Y(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
// (1.3.1, EFGCAL) Catmull-Rom con 5 muestras bilineales (sin las 4 esquinas, que pesan casi nada): la textura fina no se lava al llevarla
// a medio punto (la bilineal es una media de 2 puntos ahi)
float3 Cr(Texture2D<float4> tx, float2 p, float2 sz)
{
    float2 t1 = floor(p - 0.5) + 0.5, f = p - t1;
    float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f)), w1 = 1.0 + f * f * (-2.5 + 1.5 * f), w2 = f * (0.5 + f * (2.0 - 1.5 * f)), w3 = f * f * (-0.5 + 0.5 * f);
    float2 w12 = w1 + w2, t12 = (t1 + w2 / w12) / sz, t0 = (t1 - 1) / sz, t3 = (t1 + 2) / sz;
    float3 r = tx.SampleLevel(Lin, float2(t12.x, t0.y), 0).rgb * (w12.x * w0.y) + tx.SampleLevel(Lin, float2(t0.x, t12.y), 0).rgb * (w0.x * w12.y) +
               tx.SampleLevel(Lin, t12, 0).rgb * (w12.x * w12.y) + tx.SampleLevel(Lin, float2(t3.x, t12.y), 0).rgb * (w3.x * w12.y) +
               tx.SampleLevel(Lin, float2(t12.x, t3.y), 0).rgb * (w12.x * w3.y);
    return r / (w12.x * w0.y + w0.x * w12.y + w12.x * w12.y + w3.x * w12.y + w12.x * w3.y);
}
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int3 P = int3(id.xy, 0);
    if (CutCpu != 0 || CutT.Load(int3(0, 0, 0)) > CutThr)
    {
        Out[id.xy] = float4((Extrap != 0 || T >= 0.5 ? Cur.Load(P) : Prev.Load(P)).rgb, 1);
        return;
    }
    float2 X = float2(id.xy) + 0.5, inv = 1.0 / float2(W, H);
    float2 qf = X * 0.25 - 0.5;
    int2 q0 = int2(floor(qf));
    float2 fr = qf - float2(q0);
    float3 o[5]; float e[5]; float bw[5]; float2 Mk[4]; float Wk[4];
    [unroll] for (int i = 0; i < 4; i++)
    {
        int2 q = clamp(q0 + int2(i & 1, i >> 1), int2(0, 0), int2(QW - 1, QH - 1));
        float4 s = Sel.Load(int3(q, 0));
        float2 M = s.xy * 2;
        float3 cp = Prev.SampleLevel(Lin, (X - T * M) * inv, 0).rgb;
        float3 cc = Cur.SampleLevel(Lin, (X + (1 - T) * M) * inv, 0).rgb;
        bool two = Extrap != 0 ? s.w < TauF * 0.9 : abs(s.z - T) < 0.001;
        e[i] = two ? abs(Y(cp) - Y(cc)) : s.w;
        o[i] = lerp(cp, cc, s.z);
        Mk[i] = M; Wk[i] = s.z;
        bw[i] = ((i & 1) ? fr.x : 1 - fr.x) * ((i >> 1) ? fr.y : 1 - fr.y);
    }
    float3 zp = Prev.Load(P).rgb, zc = Cur.Load(P).rgb;
    float2 st = St.Load(int3(min(int2(id.xy) / 2, int2(W0 - 1, H0 - 1)), 0));
    e[4] = abs(Y(zp) - Y(zc)) + (st.x >= StillN ? 0 : ZeroPen);
    if ((FOpts & 2) && Extrap == 0 && st.x < StillN && Glob[uint2(13, 0)] > 0)
    {
        // (1.3.3, EFGMAPA) en un paneo plano el 0 solo vale en lo quieto: paga de mas, salvo cerca de la interfaz quieta con borde (el texto
        // sobre un panel translucido: sus bordes cambian con el mapa de debajo)
        int2 q0 = int2(id.xy) / 2, mxq = int2(W0 - 1, H0 - 1); int rq = (int)Glob[uint2(15, 0)]; bool cerca = false;
        [unroll] for (int ci = 0; ci < 9; ci++)
        {
            float2 sq = St.Load(int3(clamp(q0 + int2(ci % 3 - 1, ci / 3 - 1) * rq, int2(0, 0), mxq), 0));
            cerca = cerca || (sq.x >= Glob[uint2(16, 0)] && sq.y > 0.5);
        }
        if (!cerca) e[4] += Glob[uint2(13, 0)];
    }
    o[4] = Extrap != 0 ? zc : lerp(zp, zc, T);
    bw[4] = 0.5;
    float em = min(min(min(e[0], e[1]), min(e[2], e[3])), e[4]);
    float wk[5];
    [unroll] for (int k0 = 0; k0 < 5; k0++) wk[k0] = (bw[k0] + 0.05) * exp(-(e[k0] - em) / Sigma);
    int kb = -1;
    if ((FOpts & 1) && Extrap == 0)
    {
        // (1.3.1, EFGCAL) el candidato que mas pesa, con Catmull-Rom en vez de bilineal (si pesa mas que el 0)
        float wb = wk[4]; float2 Mb = 0; float Wb = 0;
        [unroll] for (int k1 = 0; k1 < 4; k1++) if (wk[k1] > wb) { wb = wk[k1]; kb = k1; Mb = Mk[k1]; Wb = Wk[k1]; }
        if (kb >= 0)
        {
            float2 sz = float2(W, H);
            float3 ob = lerp(Cr(Prev, X - T * Mb, sz), Cr(Cur, X + (1 - T) * Mb, sz), Wb);
            [unroll] for (int k2 = 0; k2 < 4; k2++) if (k2 == kb) o[k2] = ob;
        }
    }
    float3 acc = 0; float ws = 0;
    [unroll] for (int k3 = 0; k3 < 5; k3++) { acc += o[k3] * wk[k3]; ws += wk[k3]; }
    Out[id.xy] = float4(saturate(acc / ws), 1);
}";
    }
}
