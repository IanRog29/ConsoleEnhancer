// EngineRtShaders.cs - 24-11-2026 (1.3.3, decima ronda, agente RTX). Enhanced RT (experimental): los sombreadores. C# 5
//
// Codigo propio, escrito desde cero para ConsoleEnhancer (GPL-3.0, LICENSE): nada copiado de otras aplicaciones ni de sombreadores ajenos.
// La captura de la consola no trae profundidad ni normales: se ESTIMAN de la propia imagen (relieve por luma: lo que es mas claro que su
// entorno sobresale, lo mas oscuro se hunde; las normales salen del gradiente de ese relieve). Sobre ese campo de alturas se trazan rayos en
// espacio de pantalla (marcha por pasos en varias direcciones, horizonte maximo de cada una): lo que tapa el rayo da la OCLUSION (sombras de
// contacto) y el color de lo que encuentra da la LUZ REBOTADA (iluminacion global de un rebote: lo claro de al lado aclara y tiñe, lo
// oscuro oscurece). Pocas muestras por imagen con ruido que cambia cada imagen y acumulacion temporal reproyectada con vectores de
// movimiento PROPIOS (busqueda por bloques en dos niveles sobre la luma reducida), con rechazo de lo destapado y recorte por la vecindad
// (sin estelas), y un filtro espacial que respeta los bordes. El HUD / texto se protege: lo que no se mueve mientras alrededor si (y los
// bordes muy fuertes) no se toca.
// Todos: cs_5_1 con la raiz comun (Shaders.RootSig: b0 = 16 constantes, t0..t7, u0..u1, s0 lineal con borde fijo).
namespace ConsoleEnhancer.Motor
{
    static class RtShaders
    {
        // 1. Captura (inColor, resolucion de entrada) -> resolucion de trabajo: color en luz lineal (rgb) + luma perceptual (a), y la luma sola
        // c0: ancho y alto de trabajo, factor (1-4) | c1: 1/ancho y 1/alto de la entrada
        public const string Prep = @"
cbuffer C : register(b0) { uint WW, WH, S, P0; float IFW, IFH, P1, P2; uint4 P3; uint4 P4; }
Texture2D<float4> Src : register(t0);
SamplerState L : register(s0);
RWTexture2D<float4> OutC : register(u0);
RWTexture2D<float> OutL : register(u1);
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= WW || id.y >= WH) return;
    float2 c = (float2(id.xy) + 0.5) * S;          // centro del bloque SxS en pixeles de la entrada
    float3 a;
    if (S <= 2) a = Src.SampleLevel(L, c * float2(IFW, IFH), 0).rgb;
    else
    {
        float o = S * 0.25;
        a = 0.25 * (Src.SampleLevel(L, (c + float2(-o, -o)) * float2(IFW, IFH), 0).rgb + Src.SampleLevel(L, (c + float2(o, -o)) * float2(IFW, IFH), 0).rgb +
                    Src.SampleLevel(L, (c + float2(-o, o)) * float2(IFW, IFH), 0).rgb + Src.SampleLevel(L, (c + float2(o, o)) * float2(IFW, IFH), 0).rgb);
    }
    float y = dot(a, float3(0.2126, 0.7152, 0.0722));
    OutC[id.xy] = float4(pow(max(a, 0), 2.2), y);
    OutL[id.xy] = y;
}";

        // 2. Luma de trabajo -> luma gruesa (1/4 por lado: media de 4x4 con 4 tomas bilineales)
        // c0: ancho y alto gruesos | c1: 1/ancho y 1/alto de trabajo
        public const string Down = @"
cbuffer C : register(b0) { uint CW, CH, P0, P1; float IW, IH, P2, P3; uint4 P4; uint4 P5; }
Texture2D<float> Src : register(t0);
SamplerState L : register(s0);
RWTexture2D<float> Out : register(u0);
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= CW || id.y >= CH) return;
    float2 c = float2(id.xy) * 4.0 + 2.0;
    float2 i = float2(IW, IH);
    Out[id.xy] = 0.25 * (Src.SampleLevel(L, (c + float2(-1, -1)) * i, 0) + Src.SampleLevel(L, (c + float2(1, -1)) * i, 0) +
                         Src.SampleLevel(L, (c + float2(-1, 1)) * i, 0) + Src.SampleLevel(L, (c + float2(1, 1)) * i, 0));
}";

        // 3. El relieve estimado: altura = luma (algo suavizada) - su media ancha (de la gruesa), en -1..1; y la fuerza del borde (para el
        // texto / HUD de mucho contraste). c0: ancho y alto de trabajo | c1: 1/ancho, 1/alto gruesos, radio de la media (px gruesos), ganancia
        public const string Height = @"
cbuffer C : register(b0) { uint WW, WH, P0, P1; float ICW, ICH, R, G; uint4 P3; uint4 P4; }
Texture2D<float> Lw : register(t0);
Texture2D<float> Lc : register(t1);
SamplerState L : register(s0);
RWTexture2D<float2> Out : register(u0);
float Ld(int2 p) { return Lw.Load(int3(clamp(p, int2(0, 0), int2(WW - 1, WH - 1)), 0)); }
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= WW || id.y >= WH) return;
    int2 p = int2(id.xy);
    float c = Ld(p), l = Ld(p + int2(-1, 0)), r = Ld(p + int2(1, 0)), u = Ld(p + int2(0, -1)), d = Ld(p + int2(0, 1));
    float sm = 0.5 * c + 0.125 * (l + r + u + d);
    float2 uv = (float2(p) + 0.5) * 0.25 * float2(ICW, ICH);
    float m = 0, wsum = 0;
    [unroll] for (int k = 0; k < 8; k++)
    {
        float a = k * 0.785398;
        float2 o = float2(cos(a), sin(a)) * R * float2(ICW, ICH);
        m += Lc.SampleLevel(L, uv + o, 0) + Lc.SampleLevel(L, uv + o * 0.5, 0);
        wsum += 2;
    }
    m = (m + 2 * Lc.SampleLevel(L, uv, 0)) / (wsum + 2);
    float h = clamp((sm - m) * G, -1, 1);
    float e = max(max(abs(c - l), abs(c - r)), max(abs(c - u), abs(c - d)));
    Out[id.xy] = float2(h, e);
}";

        // 4. Vectores gruesos: un hilo = un bloque de 4x4 px gruesos (16x16 de trabajo). Busqueda completa +-5 alrededor de 0 y +-1 alrededor
        // del vector de la imagen anterior en este bloque y sus 4 vecinos (sigue los paneos rapidos que aceleran). Sale (dx, dy) en px gruesos
        // de ahora -> anterior, y el error. c0: ancho y alto gruesos, bloques x / y | c1: historia valida
        public const string MvCoarse = @"
cbuffer C : register(b0) { uint CW, CH, BW, BH; uint Valid, P0, P1, P2; uint4 P3; uint4 P4; }
Texture2D<float> Cur : register(t0);
Texture2D<float> Prev : register(t1);
Texture2D<float4> MvPrev : register(t2);
RWTexture2D<float4> Out : register(u0);
float Lp(Texture2D<float> t, int2 p) { return t.Load(int3(clamp(p, int2(0, 0), int2(CW - 1, CH - 1)), 0)); }
float Sad(int2 o, int2 v)
{
    float s = 0;
    [unroll] for (int y = 0; y < 4; y++) [unroll] for (int x = 0; x < 4; x++) s += abs(Lp(Cur, o + int2(x, y)) - Lp(Prev, o + int2(x, y) + v));
    return s;
}
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= BW || id.y >= BH) return;
    if (Valid == 0) { Out[id.xy] = float4(0, 0, 0, 0); return; }
    int2 o = int2(id.xy) * 4;
    float best = Sad(o, int2(0, 0)) - 0.02; int2 bv = int2(0, 0);   // (lo quieto, un poco preferido: HUD y fondos lisos)
    for (int y = -5; y <= 5; y++)
        for (int x = -5; x <= 5; x++)
        {
            float s = Sad(o, int2(x, y)) + 0.004 * (abs(x) + abs(y));
            if (s < best) { best = s; bv = int2(x, y); }
        }
    int2 nb[5] = { int2(0, 0), int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1) };
    [unroll] for (int k = 0; k < 5; k++)
    {
        int2 b = clamp(int2(id.xy) + nb[k], int2(0, 0), int2(BW - 1, BH - 1));
        int2 c = int2(round(MvPrev.Load(int3(b, 0)).xy));
        if (abs(c.x) <= 4 && abs(c.y) <= 4) continue;   // (ya en la busqueda completa)
        for (int y2 = -1; y2 <= 1; y2++)
            for (int x2 = -1; x2 <= 1; x2++)
            {
                int2 v = c + int2(x2, y2);
                float s = Sad(o, v) + 0.004 * (abs(x2) + abs(y2)) - 0.01;
                if (s < best) { best = s; bv = v; }
            }
    }
    Out[id.xy] = float4(bv, best / 16.0, 0);
}";

        // 5. Vectores finos: un hilo = un bloque de 8x8 px de trabajo. Candidatos: 0 y los gruesos de su bloque y los 4 vecinos (x4); cada uno
        // +-1 con el error de 16 tomas (una de cada 2x2; los repetidos una vez). Sale (dx, dy) en px de trabajo ahora -> anterior.
        // c0: ancho y alto de trabajo, bloques finos x / y | c1: bloques gruesos x / y, historia valida
        public const string MvFine = @"
cbuffer C : register(b0) { uint WW, WH, FBW, FBH; uint CBW, CBH, Valid, P0; uint4 P3; uint4 P4; }
Texture2D<float> Cur : register(t0);
Texture2D<float> Prev : register(t1);
Texture2D<float4> MvC : register(t2);
RWTexture2D<float4> Out : register(u0);
float Lp(Texture2D<float> t, int2 p) { return t.Load(int3(clamp(p, int2(0, 0), int2(WW - 1, WH - 1)), 0)); }
float Sad2(int2 o, int2 v)
{
    float s = 0;
    [unroll] for (int y = 0; y < 8; y += 2) [unroll] for (int x = 0; x < 8; x += 2) s += abs(Lp(Cur, o + int2(x, y)) - Lp(Prev, o + int2(x, y) + v));
    return s;
}
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= FBW || id.y >= FBH) return;
    if (Valid == 0) { Out[id.xy] = float4(0, 0, 0, 0); return; }
    int2 o = int2(id.xy) * 8;
    int2 cb = int2(id.xy) / 2;
    int2 cand[6];
    cand[0] = int2(0, 0);
    int2 nb[5] = { int2(0, 0), int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1) };
    [unroll] for (int k = 0; k < 5; k++) cand[k + 1] = int2(round(MvC.Load(int3(clamp(cb + nb[k], int2(0, 0), int2(CBW - 1, CBH - 1)), 0)).xy * 4.0));
    float best = 1e9; int2 bv = int2(0, 0);
    [loop] for (int c = 0; c < 6; c++)
    {
        bool rep = false;
        for (int j = 0; j < c; j++) rep = rep || all(cand[j] == cand[c]);
        if (rep) continue;
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                int2 v = cand[c] + int2(x, y);
                float s = Sad2(o, v) + 0.003 * (abs(x) + abs(y)) - (c == 0 && x == 0 && y == 0 ? 0.01 : 0.0);
                if (s < best) { best = s; bv = v; }
            }
    }
    Out[id.xy] = float4(bv, best / 16.0, 0);
}";

        // 6. El trazado: por cada punto, Dirs rayos en el plano de la imagen (direccion que gira con un ruido propio por punto y por imagen)
        // que marchan Steps pasos sobre el campo de alturas; en cada rayo, el horizonte mas alto encontrado tapa (oclusion) y el color de
        // donde sube el horizonte es la luz que rebota hacia el punto. Sale rgb = luz rebotada (lineal), a = oclusion (0 = nada tapa).
        // c0: ancho y alto de trabajo, imagen, Dirs | c1: Steps, radio (px de trabajo), altura (px de trabajo por unidad de relieve), sesgo
        public const string Trace = @"
cbuffer C : register(b0) { uint WW, WH, Frame, Dirs; uint Steps; float Rad, Hpx, Bias; uint4 P3; uint4 P4; }
Texture2D<float2> He : register(t0);
Texture2D<float4> Col : register(t1);
SamplerState L : register(s0);
RWTexture2D<float4> Out : register(u0);
float Hash(uint2 p, uint f)
{
    uint h = p.x * 1664525u + p.y * 22695477u + f * 2891336453u;
    h ^= h >> 16; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
    return (h & 0xFFFFFF) / 16777216.0;
}
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= WW || id.y >= WH) return;
    float2 isz = 1.0 / float2(WW, WH);
    float2 p = float2(id.xy) + 0.5;
    float h0 = He.Load(int3(id.xy, 0)).x * Hpx;
    // ruido de baja discrepancia: rejilla 4x4 fija (cuadrado latino) + paso aureo por imagen
    uint2 q = id.xy & 3;
    float n1 = frac(((q.x * 4 + ((q.y * 3 + q.x) & 3)) + 0.5) / 16.0 + Frame * 0.618034);
    float n2 = frac(Hash(id.xy, Frame) + Frame * 0.754878);
    float occ = 0; float3 gi = 0;
    for (uint k = 0; k < Dirs; k++)
    {
        float a = (k + n1) * 6.283185 / Dirs;
        float2 d = float2(cos(a), sin(a));
        float maxS = Bias, o = 0; float3 g = 0;
        for (uint s = 0; s < Steps; s++)
        {
            float t = (s + n2) / Steps;
            float dist = 1.0 + (Rad - 1.0) * t * t;
            float2 uv = (p + d * dist) * isz;
            if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) break;
            float dz = He.SampleLevel(L, uv, 0).x * Hpx - h0;
            float sn = dz * rsqrt(dist * dist + dz * dz);
            if (sn > maxS)
            {
                float w = (sn - maxS) * (1.0 - t * t);
                o += w;
                g += w * Col.SampleLevel(L, uv, 0).rgb;
                maxS = sn;
            }
        }
        occ += o; gi += g;
    }
    Out[id.xy] = float4(gi / Dirs, saturate(occ / Dirs));
}";

        // 7. Acumulacion temporal: la historia de la imagen anterior traida con los vectores finos (bilineal), recortada a la media +-1,25
        // desviaciones de la vecindad 3x3 de lo de ahora y tirada donde la luma reproyectada no casa (destapado, corte de escena). El HUD:
        // lo que no cambia en su sitio con movimiento alrededor (y con detalle) sube la mascara; lo que cambia la baja; quieto todo, se queda.
        // c0: ancho y alto de trabajo, bloques finos x / y | c1: historia valida, largo maximo, -, - | c2: 1/ancho, 1/alto de trabajo
        public const string Accum = @"
cbuffer C : register(b0) { uint WW, WH, FBW, FBH; uint Valid; float MaxLen, P0, P1; float IW, IH, P2, P3; uint4 P4; }
Texture2D<float4> Noisy : register(t0);
Texture2D<float4> HistP : register(t1);
Texture2D<float4> Mv : register(t2);
Texture2D<float> Lc : register(t3);
Texture2D<float> Lp : register(t4);
Texture2D<float2> HudP : register(t5);
SamplerState L : register(s0);
RWTexture2D<float4> Hist : register(u0);
RWTexture2D<float2> Hud : register(u1);
float LcAt(int2 p) { return Lc.Load(int3(clamp(p, int2(0, 0), int2(WW - 1, WH - 1)), 0)); }
float2 MvAt(float2 px) { return Mv.SampleLevel(L, px / 8.0 / float2(FBW, FBH), 0).xy; }
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= WW || id.y >= WH) return;
    int2 p = int2(id.xy);
    float2 pc = float2(p) + 0.5;
    float4 n = Noisy.Load(int3(p, 0));
    float4 m1 = 0, m2 = 0; float lmin = 9, lmax = -9;
    [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
    {
        int2 q = clamp(p + int2(x, y), int2(0, 0), int2(WW - 1, WH - 1));
        float4 v = Noisy.Load(int3(q, 0)); m1 += v; m2 += v * v;
        float l = Lc.Load(int3(q, 0)); lmin = min(lmin, l); lmax = max(lmax, l);
    }
    m1 /= 9; m2 /= 9;
    float4 sd = sqrt(max(m2 - m1 * m1, 0));
    if (Valid == 0) { Hist[p] = n; Hud[p] = float2(0, 1); return; }
    float2 mv = MvAt(pc);
    float mvMax = length(mv);
    [unroll] for (int k = 0; k < 4; k++)
    {
        float2 o = float2(k & 1 ? 12 : -12, k & 2 ? 12 : -12);
        mvMax = max(mvMax, length(MvAt(pc + o)));
    }
    float lc = LcAt(p), l0 = Lp.Load(int3(p, 0));
    float gr = max(max(abs(lc - LcAt(p + int2(1, 0))), abs(lc - LcAt(p + int2(-1, 0)))), max(abs(lc - LcAt(p + int2(0, 1))), abs(lc - LcAt(p + int2(0, -1)))));
    float2 hp = HudP.Load(int3(p, 0));
    float hud = hp.x;
    if (mvMax > 1.5)
    {
        float d0 = abs(lc - l0);
        if (d0 < 0.012 && gr > 0.04) hud = min(1, hud + 0.3);
        else if (d0 > 0.03) hud = max(0, hud - 0.15);
    }
    float2 mu = hud > 0.5 ? float2(0, 0) : mv;
    float2 pp = pc + mu;
    bool ok = pp.x >= 0 && pp.y >= 0 && pp.x <= WW && pp.y <= WH;
    float2 uvp = pp / float2(WW, WH);
    float lr = Lp.SampleLevel(L, uvp, 0);
    if (lr < lmin - 0.04 || lr > lmax + 0.04) ok = false;
    float len = ok ? min(HudP.SampleLevel(L, uvp, 0).y + 1, MaxLen) : 1;
    float4 h = HistP.SampleLevel(L, uvp, 0);
    h = clamp(h, m1 - 1.25 * sd - 0.01, m1 + 1.25 * sd + 0.01);
    float4 r = ok ? lerp(h, n, 1.0 / len) : n;
    Hist[p] = r;
    Hud[p] = float2(hud, len);
}";

        // 8. Filtro espacial que respeta los bordes (3x3 con paso 2-3 segun lo que lleva acumulado): peso por distancia, por parecido del
        // relieve y de la luma. c0: ancho y alto de trabajo
        public const string Filter = @"
cbuffer C : register(b0) { uint WW, WH, P0, P1; uint4 P2; uint4 P3; uint4 P4; }
Texture2D<float4> Hist : register(t0);
Texture2D<float2> He : register(t1);
Texture2D<float4> Col : register(t2);
Texture2D<float2> Hud : register(t3);
RWTexture2D<float4> Out : register(u0);
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= WW || id.y >= WH) return;
    int2 p = int2(id.xy);
    float h0 = He.Load(int3(p, 0)).x, l0 = Col.Load(int3(p, 0)).a;
    float len = Hud.Load(int3(p, 0)).y;
    int st = len < 4 ? 3 : 2;
    float4 acc = 0; float ws = 0;
    [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
    {
        int2 q = clamp(p + int2(x, y) * st, int2(0, 0), int2(WW - 1, WH - 1));
        float w = exp(-0.5 * (x * x + y * y)) * exp(-abs(He.Load(int3(q, 0)).x - h0) * 6.0) * exp(-abs(Col.Load(int3(q, 0)).a - l0) * 10.0);
        acc += Hist.Load(int3(q, 0)) * w; ws += w;
    }
    Out[p] = acc / max(ws, 1e-4);
}";

        // 9. Mezcla en la imagen (inColor, resolucion de entrada, en su sitio): luz lineal x (1 - ao * oclusion + gi * luz rebotada), menos
        // en el HUD (mascara ensanchada) y en los bordes muy fuertes (texto). c0: ancho y alto de la entrada, depuracion | c1: 1/ancho,
        // 1/alto de trabajo, fuerza AO, fuerza GI | c2: fuerza total, proteger HUD
        public const string Comp = @"
cbuffer C : register(b0) { uint FW, FH, Dbg, P0; float IWW, IWH, Ao, Gi; float Str, HudOn, P1, P2; uint4 P3; }
Texture2D<float4> Filt : register(t0);
Texture2D<float2> Hud : register(t1);
Texture2D<float2> He : register(t2);
SamplerState L : register(s0);
RWTexture2D<unorm float4> Dst : register(u0);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= FW || id.y >= FH) return;
    float2 uv = (float2(id.xy) + 0.5) / float2(FW, FH);
    float4 c = Dst[id.xy];
    float4 f = Filt.SampleLevel(L, uv, 0);
    float hud = 0;
    [unroll] for (int k = 0; k < 5; k++)
    {
        float2 o = k == 0 ? float2(0, 0) : float2(k & 1 ? 2 : -2, k & 2 ? 2 : -2);
        hud = max(hud, Hud.SampleLevel(L, uv + o * float2(IWW, IWH), 0).x);
    }
    float e = He.SampleLevel(L, uv, 0).y;
    float prot = (1 - HudOn * smoothstep(0.25, 0.6, hud)) * (1 - 0.7 * smoothstep(0.22, 0.45, e));
    float3 lin = pow(max(c.rgb, 0), 2.2);
    float3 gi = f.rgb * 2.0;
    float3 k3 = 1 - Ao * f.a + Gi * gi;
    float3 o = lin * lerp(1, k3, saturate(Str * prot));
    float3 r = pow(saturate(o), 1.0 / 2.2);
    if (Dbg == 1) r = (1 - f.a).xxx;
    else if (Dbg == 2) r = pow(saturate(gi), 1.0 / 2.2);
    else if (Dbg == 3) r = lerp(c.rgb, float3(1, 0, 0), 0.6 * (1 - prot));
    Dst[id.xy] = float4(r, c.a);
}";
    }
}
