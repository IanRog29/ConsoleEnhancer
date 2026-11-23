namespace ConsoleEnhancer.Motor
{
    // EngineFgEnhancedLatShaders.cs - 23-11-2026 (1.3.1, octava ronda, agente EFGLAT). Los sombreadores del modo BAJA LATENCIA de Enhanced FG
    // (extrapolacion hacia delante, EngineFgEnhancedLat.cs). Codigo propio en HLSL de DirectCompute (cs_5_1, la firma de raiz comun del motor).
    // Coordenadas como en EngineFgEnhancedShaders.cs: L0 = media resolucion de la salida, bloques de 8x8 de L0; C2P = de la actual a la anterior.
    static class EfgLatShaders
    {
        // ---- El MODELO GLOBAL del movimiento (casi todo es la camara al girar): mx, my = polinomio de grado 2 en u = (x - W/2) / (W/2),
        // v = (y - H/2) / (W/2) (6 coeficientes cada uno; con Order = 1, afin) ajustado al campo C2P de L0 de la actual (m = -C2P) por minimos
        // cuadrados con pesos: textura del bloque, su coste y, desde la 2a vuelta, lo que se aparta del modelo de la vuelta anterior (Cauchy:
        // 1 / (1 + (r / (C0 + C1 |m|))^2)): lo que se mueve por su cuenta (el personaje, el coche, la interfaz quieta) no cuenta. Un solo grupo de
        // 128 hilos; el hilo 0 resuelve el 6x6 (Gauss con pivote). Gm (4x2): fila 0 = el de esta pareja (x: c0-c5 en [0].xyzw [1].xy, y: c0-c5
        // en [1].zw [2].xyzw; [3] = (valido, peso total, 0, 0)), fila 1 = el de la pareja anterior (la vuelta 0 lo copia antes de empezar).
        public const string Fit = @"
cbuffer C : register(b0) { uint BX, BY, W0, H0; uint Iter, Order, Valid, Pad0; float C0, C1, MinW, Pad1; uint4 P3; }
Texture2D<float4> F : register(t0);
RWTexture2D<float4> Gm : register(u0);
groupshared float sh[33 * 128];
static float G[12];
void Basis(float2 p, out float b[6])
{
    float hw = W0 * 0.5;
    float u = (p.x - hw) / hw, v = (p.y - H0 * 0.5) / hw;
    b[0] = 1; b[1] = u; b[2] = v; b[3] = u * u; b[4] = u * v; b[5] = v * v;
}
[numthreads(128, 1, 1)]
void main(uint3 tid : SV_GroupThreadID)
{
    uint t = tid.x;
    uint nb = Order == 1 ? 3 : 6;
    float4 g0 = Gm[uint2(0, 0)], g1 = Gm[uint2(1, 0)], g2 = Gm[uint2(2, 0)], g3 = Gm[uint2(3, 0)];
    G[0] = g0.x; G[1] = g0.y; G[2] = g0.z; G[3] = g0.w; G[4] = g1.x; G[5] = g1.y;
    G[6] = g1.z; G[7] = g1.w; G[8] = g2.x; G[9] = g2.y; G[10] = g2.z; G[11] = g2.w;
    GroupMemoryBarrierWithGroupSync();
    if (Iter == 0)
    {
        if (t == 0) { Gm[uint2(0, 1)] = g0; Gm[uint2(1, 1)] = g1; Gm[uint2(2, 1)] = g2; Gm[uint2(3, 1)] = g3; }
        if (Valid == 0)
        {
            if (t == 0) { Gm[uint2(0, 0)] = 0; Gm[uint2(1, 0)] = 0; Gm[uint2(2, 0)] = 0; Gm[uint2(3, 0)] = 0; }
            return;
        }
    }
    float acc[33];
    [unroll] for (int i = 0; i < 33; i++) acc[i] = 0;
    uint n = BX * BY;
    for (uint q = t; q < n; q += 128)
    {
        uint bx = q % BX, by = q / BX;
        float4 f = F.Load(int3(bx, by, 0));
        float2 p = float2(bx * 8 + 4, by * 8 + 4);
        if (p.x > W0 || p.y > H0) continue;
        float2 m = -f.xy;
        float b[6];
        Basis(p, b);
        float w = (f.w > 0.004 ? 1 : 0.05) * (f.z < 0.06 ? 1 : 0.1);
        if (Iter > 0)
        {
            float2 gm = 0;
            [unroll] for (int k = 0; k < 6; k++) { gm.x += G[k] * b[k]; gm.y += G[6 + k] * b[k]; }
            float r = length(gm - m) / (C0 + C1 * length(gm));
            w *= 1.0 / (1 + r * r);
        }
        int o = 0;
        [unroll] for (int a = 0; a < 6; a++)
            [unroll] for (int c = a; c < 6; c++) { acc[o] += w * b[a] * b[c]; o++; }
        [unroll] for (int a2 = 0; a2 < 6; a2++) { acc[21 + a2] += w * b[a2] * m.x; acc[27 + a2] += w * b[a2] * m.y; }
    }
    [unroll] for (int i2 = 0; i2 < 33; i2++) sh[i2 * 128 + t] = acc[i2];
    GroupMemoryBarrierWithGroupSync();
    for (uint s = 64; s > 0; s >>= 1)
    {
        if (t < s) { [unroll] for (int i3 = 0; i3 < 33; i3++) sh[i3 * 128 + t] += sh[i3 * 128 + t + s]; }
        GroupMemoryBarrierWithGroupSync();
    }
    if (t != 0) return;
    float A[6][8];
    int o2 = 0;
    for (int a3 = 0; a3 < 6; a3++)
        for (int c3 = a3; c3 < 6; c3++) { float v = sh[o2 * 128]; A[a3][c3] = v; A[c3][a3] = v; o2++; }
    for (int a4 = 0; a4 < 6; a4++) { A[a4][6] = sh[(21 + a4) * 128]; A[a4][7] = sh[(27 + a4) * 128]; A[a4][a4] += 1e-3; }
    float wsum = sh[0];
    // fuera de grado: las filas / columnas que no se usan, identidad con 0 a la derecha
    for (uint z = nb; z < 6; z++)
    {
        for (int c4 = 0; c4 < 8; c4++) { A[z][c4] = 0; }
        for (int r4 = 0; r4 < 6; r4++) A[r4][z] = r4 == (int)z ? 1 : A[r4][z] * 0;
        A[z][z] = 1;
    }
    for (int c5 = 0; c5 < 6; c5++)
    {
        int pv = c5;
        for (int r5 = c5 + 1; r5 < 6; r5++) if (abs(A[r5][c5]) > abs(A[pv][c5])) pv = r5;
        if (pv != c5) for (int k5 = 0; k5 < 8; k5++) { float tmp = A[c5][k5]; A[c5][k5] = A[pv][k5]; A[pv][k5] = tmp; }
        float d = A[c5][c5]; if (abs(d) < 1e-9) d = 1e-9;
        for (int r6 = c5 + 1; r6 < 6; r6++)
        {
            float f6 = A[r6][c5] / d;
            for (int k6 = c5; k6 < 8; k6++) A[r6][k6] -= f6 * A[c5][k6];
        }
    }
    float X[6], Y[6];
    for (int c7 = 5; c7 >= 0; c7--)
    {
        float sx = A[c7][6], sy = A[c7][7];
        for (int k7 = c7 + 1; k7 < 6; k7++) { sx -= A[c7][k7] * X[k7]; sy -= A[c7][k7] * Y[k7]; }
        float d7 = A[c7][c7]; if (abs(d7) < 1e-9) d7 = 1e-9;
        X[c7] = sx / d7; Y[c7] = sy / d7;
    }
    bool ok = wsum >= MinW;
    [unroll] for (int k8 = 0; k8 < 6; k8++) { if (!(abs(X[k8]) < 1e4) || !(abs(Y[k8]) < 1e4)) ok = false; }
    if (!ok) { Gm[uint2(0, 0)] = 0; Gm[uint2(1, 0)] = 0; Gm[uint2(2, 0)] = 0; Gm[uint2(3, 0)] = float4(0, wsum, 0, 0); return; }
    Gm[uint2(0, 0)] = float4(X[0], X[1], X[2], X[3]);
    Gm[uint2(1, 0)] = float4(X[4], X[5], Y[0], Y[1]);
    Gm[uint2(2, 0)] = float4(Y[2], Y[3], Y[4], Y[5]);
    Gm[uint2(3, 0)] = float4(1, wsum, 0, 0);
}";

        // ---- La ELECCION hacia delante (T = 1 + t > 1) a un cuarto de resolucion: la del modo calidad (EfgShaders.Sel) con Extrap fijo (la
        // muestra de la actual en x - t m; la de la anterior en x - T m solo dice si m casa con lo de antes) y:
        //  Flags 1  = el candidato GLOBAL (el modelo de Fit, llevado al punto de donde viene lo que llega a x: m = G(x - t G(x)));
        //  Flags 2  = BORDES: que la muestra se salga de la imagen no la castiga (lo que entra por el borde: gana el movimiento que sigue al de
        //             alrededor y Fin lo estira); antes ganaba el 0 (una franja quieta pegada al borde: bordes dobles);
        //  Flags 16 = DELANTE / DETRAS con el modelo global (lo que va con la camara es el fondo): entre los candidatos que casan, el que mas
        //             se aparta del global gana por ZLam (lo de delante tapa: el personaje, el arma, el coche no se los come el fondo); en los
        //             HUECOS (lo que se destapa: ningun candidato casa), se coge de donde hay fondo (ZHole x lo que la muestra se aparta del
        //             global): antes se estiraba el borde del personaje / del arma sobre lo destapado.
        //  ZP > 0   = LISO: donde lo que llega no tiene textura, hacia el global (o, con Flags 64, hacia el vector grueso de L3 de x);
        //  Flags 32 = LO QUE LLEGA (sustituye al coste de 16 con su misma idea): lo que se ve en x es lo que LLEGA a x desde la actual (la
        //             muestra x - t m se mueve con m: Ec < ZV0 + ZV1 |m|); entre lo que llega gana lo de delante (ZLam) y casar con la anterior
        //             (D x ZD) solo desempata: lo animado (las manos, el arma, el personaje que se mueve) no casa con la anterior y antes perdia
        //             contra la pared lisa de detras, que si casa, y se lo comia. Donde lo que llega no tiene textura no hay "delante" que valga
        //             y se tira hacia el modelo global (ZP): una pared lisa sale entera y en su sitio en vez de un remiendo de vectores
        //             sueltos (lo que se veia al girar la camara en x3 / x4). Los huecos (no llega nada), como en 16.
        // Sel = (m en L0, 1, coste)
        public const string Sel = @"
cbuffer C : register(b0) { uint QW, QH, BX, BY; float T, Kappa, Tau, StillN; uint W0, H0, Flags; float ZD; float ZLam, ZHole, ZSat, ZP; }
Texture2D<float> Lp : register(t0);
Texture2D<float> Lc : register(t1);
Texture2D<float4> Fp : register(t2);
Texture2D<float4> Fc : register(t3);
Texture2D<float2> St : register(t4);
Texture2D<float4> Fp1 : register(t5);
Texture2D<float4> Fc1 : register(t6);
Texture2D<float4> Fc3 : register(t7);
RWTexture2D<float4> Sel : register(u0);
RWTexture2D<float4> Gm : register(u1);
SamplerState Lin : register(s0);
static float2 inv0, invB, gX, pRef;
static float bestS;
static float2 bestM;
static float G[12], Gp[12];
static bool gOk, gpOk, useZ, useN;
static float sc;
float Lp1(float2 p) { return Lp.SampleLevel(Lin, p * inv0, 0); }
float Lc1(float2 p) { return Lc.SampleLevel(Lin, p * inv0, 0); }
bool StillAt(float2 p)
{
    float2 s = St.Load(int3(clamp(int2(p), int2(0, 0), int2(W0 - 1, H0 - 1)), 0));
    return s.x >= StillN && s.y > 0.5;
}
float2 EvalG(float2 p, bool prev)
{
    float hw = W0 * 0.5;
    float u = (p.x - hw) / hw, v = (p.y - H0 * 0.5) / hw;
    float b[6] = { 1, u, v, u * u, u * v, v * v };
    float2 m = 0;
    [unroll] for (int k = 0; k < 6; k++)
    {
        m.x += (prev ? Gp[k] : G[k]) * b[k];
        m.y += (prev ? Gp[6 + k] : G[6 + k]) * b[k];
    }
    return m;
}
void LoadG()
{
    float4 a = Gm[uint2(0, 0)], b = Gm[uint2(1, 0)], c = Gm[uint2(2, 0)], d = Gm[uint2(3, 0)];
    G[0] = a.x; G[1] = a.y; G[2] = a.z; G[3] = a.w; G[4] = b.x; G[5] = b.y; G[6] = b.z; G[7] = b.w; G[8] = c.x; G[9] = c.y; G[10] = c.z; G[11] = c.w;
    gOk = d.x > 0.5;
    a = Gm[uint2(0, 1)]; b = Gm[uint2(1, 1)]; c = Gm[uint2(2, 1)]; d = Gm[uint2(3, 1)];
    Gp[0] = a.x; Gp[1] = a.y; Gp[2] = a.z; Gp[3] = a.w; Gp[4] = b.x; Gp[5] = b.y; Gp[6] = b.z; Gp[7] = b.w; Gp[8] = c.x; Gp[9] = c.y; Gp[10] = c.z; Gp[11] = c.w;
    gpOk = d.x > 0.5;
}
float Cost(float2 x, float2 m)
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
    bool free = (Flags & 2) != 0;
    if ((moving && StillAt(qp)) || (!free && (any(qp < 0) || any(qp > lim)))) Ep += 100;
    if ((moving && StillAt(qc)) || (!free && (any(qc < 0) || any(qc > lim)))) Ec += 100;
    float r;
    if (useN)
    {
        bool outq = any(qc < 0) || any(qc > lim);
        bool stl = moving && StillAt(qc);
        float ec = length(mc - m);
        bool valid = !stl && (!outq || free) && ec < sc + 0.1 * length(m);
        float tw = saturate((Fc.SampleLevel(Lin, qc * invB, 0).w - 0.004) / 0.01);
        float dg = length(m - gX);
        float rv = ZD * D + Kappa * 0.25 * min(ec, 16) - ZLam * tw * min(dg, ZSat) / ZSat + ZP * (1 - tw) * min(dg, 16);
        float rh = Tau + Kappa * min(ec + (stl || (outq && !free) ? 100 : 0), 16) + ZHole * min(length(mc - EvalG(qc, false)), 16);
        r = valid ? rv : rh;
    }
    else
    {
        float S2 = D + Kappa * 0.25 * min(Ep + Ec, 16) + (Ep >= 100 || Ec >= 100 ? 1 : 0);
        float S1 = Tau + Kappa * min(Ec, 16);
        if (useZ)
        {
            S2 -= ZLam * min(length(m - gX), ZSat) / ZSat;
            S1 += ZHole * min(length(mc - EvalG(qc, false)), 16);
        }
        if (gOk && ZP > 0)
        {
            // LISO: donde lo que llega no tiene textura, hacia el modelo global o (Flags 64) hacia el vector grueso de L3 de ese sitio (sin
            // remiendos de vectores sueltos en una pared lisa)
            float tw0 = saturate((Fc.SampleLevel(Lin, qc * invB, 0).w - 0.004) / 0.01);
            float pr = ZP * (1 - tw0) * min(length(m - pRef), 16);
            S2 += pr; S1 += pr;
        }
        r = min(S1, S2);
    }
    return r;
}
void Try(float2 x, float2 m)
{
    float s = Cost(x, m);
    if (s < bestS) { bestS = s; bestM = m; }
}
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= QW || id.y >= QH) return;
    inv0 = 1.0 / float2(W0, H0);
    invB = 1.0 / (8.0 * float2(BX, BY));
    uint2 l1 = (uint2(W0, H0) + 1) / 2, l2 = (l1 + 1) / 2, l3 = (l2 + 1) / 2;
    uint2 b1 = (l1 + 7) / 8, b3 = (l3 + 7) / 8;
    LoadG();
    float2 x = float2(id.xy) * 2 + 1;
    float t = T - 1;
    gX = gOk ? EvalG(x, false) : 0;
    pRef = (Flags & 64) ? -Fc3.SampleLevel(Lin, x / (64.0 * float2(b3)), 0).xy * 8 : gX;
    useZ = gOk && (Flags & 16) != 0;
    useN = gOk && (Flags & 32) != 0;
    sc = W0 / 1920.0;
    int2 b0 = int2(floor((x - 4) / 8));
    bestS = 1e9; bestM = 0;
    [unroll] for (int i = 0; i < 4; i++)
    {
        int2 b = clamp(b0 + int2(i & 1, i >> 1), int2(0, 0), int2(BX - 1, BY - 1));
        Try(x, Fp.Load(int3(b, 0)).xy);
        Try(x, -Fc.Load(int3(b, 0)).xy);
    }
    Try(x, float2(0, 0));
    float2 u1 = x / (16.0 * float2(b1));
    Try(x, Fp1.SampleLevel(Lin, u1, 0).xy * 2);
    Try(x, -Fc1.SampleLevel(Lin, u1, 0).xy * 2);
    Try(x, -Fc3.SampleLevel(Lin, x / (64.0 * float2(b3)), 0).xy * 8);
    float2 m0 = bestM;
    Try(x, Fp.SampleLevel(Lin, (x - T * m0) * invB, 0).xy);
    Try(x, -Fc.SampleLevel(Lin, (x + (1 - T) * m0) * invB, 0).xy);
    if (gOk && (Flags & 1)) Try(x, EvalG(x - t * gX, false));
    Sel[id.xy] = float4(bestM, 1, bestS);
}";

        // ---- La inventada hacia delante a la resolucion de salida: la de EfgShaders.Fin con Extrap fijo (todo de la actual, en x - t M) y, con
        // Border (y Stretch > 1), lo que se sale por el borde se ESTIRA: la franja de Stretch veces lo que se sale coge lo que queda dentro
        // estirado (sin cortes) en vez de repetir la ultima fila de la imagen
        public const string Fin = @"
cbuffer C : register(b0) { uint W, H, QW, QH; float T, ZeroPen, StillN, Sigma; uint Border, CutCpu, W0, H0; float CutThr, TauF, Stretch; uint Mode; }
Texture2D<float4> Prev : register(t0);
Texture2D<float4> Cur : register(t1);
Texture2D<float4> Sel : register(t2);
Texture2D<float2> St : register(t3);
Texture2D<float> CutT : register(t4);
RWTexture2D<unorm float4> Out : register(u0);
SamplerState Lin : register(s0);
float Y(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
float Str(float X, float s, float Wd)
{
    float over = s - X;
    float r = s;
    if (over > 0) { float band = Stretch * over, e = Wd - X; if (e < band) r = Wd - max(0, e) * (band - over) / band; }
    else if (over < 0) { float o2 = -over, band = Stretch * o2, e = X; if (e < band) r = max(0, e) * (band - o2) / band; }
    return r;
}
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int3 P = int3(id.xy, 0);
    if (CutCpu != 0 || CutT.Load(int3(0, 0, 0)) > CutThr)
    {
        Out[id.xy] = float4(Cur.Load(P).rgb, 1);
        return;
    }
    float2 X = float2(id.xy) + 0.5, inv = 1.0 / float2(W, H);
    float2 qf = X * 0.25 - 0.5;
    int2 q0 = int2(floor(qf));
    float2 fr = qf - float2(q0);
    bool str = Border != 0 && Stretch > 1;
    float3 o[5]; float e[5]; float bw[5];
    [unroll] for (int i = 0; i < 4; i++)
    {
        int2 q = clamp(q0 + int2(i & 1, i >> 1), int2(0, 0), int2(QW - 1, QH - 1));
        float4 s = Sel.Load(int3(q, 0));
        float2 M = s.xy * 2;
        float2 sc = X + (1 - T) * M;
        if (str) { sc.x = Str(X.x, sc.x, W); sc.y = Str(X.y, sc.y, H); }
        float3 cp = Prev.SampleLevel(Lin, (X - T * M) * inv, 0).rgb;
        float3 cc = Cur.SampleLevel(Lin, sc * inv, 0).rgb;
        bool two = s.w < TauF * 0.9;
        e[i] = Mode == 1 ? s.w : two ? abs(Y(cp) - Y(cc)) : s.w;
        o[i] = cc;
        bw[i] = ((i & 1) ? fr.x : 1 - fr.x) * ((i >> 1) ? fr.y : 1 - fr.y);
    }
    float3 zp = Prev.Load(P).rgb, zc = Cur.Load(P).rgb;
    float2 st = St.Load(int3(min(int2(id.xy) / 2, int2(W0 - 1, H0 - 1)), 0));
    e[4] = abs(Y(zp) - Y(zc)) + (st.x >= StillN ? 0 : ZeroPen);
    o[4] = zc;
    bw[4] = 0.5;
    float em = min(min(min(e[0], e[1]), min(e[2], e[3])), e[4]);
    float3 acc = 0; float ws = 0;
    [unroll] for (int k = 0; k < 5; k++)
    {
        float w = (bw[k] + 0.05) * exp(-(e[k] - em) / Sigma);
        acc += o[k] * w; ws += w;
    }
    Out[id.xy] = float4(saturate(acc / ws), 1);
}";
    }
}
