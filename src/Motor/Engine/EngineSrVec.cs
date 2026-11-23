using System;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineSrVec.cs - 23-11-2026 (1.2.4 ronda 6, agente DLSSSR; el usuario: "Los reescalados dlss a partir del 4 dejan mucha borrosidad al
    // mover la camara" y "todos los dlss siguen teniendo borrosidades en sitios al girar la camara y en el suelo sin ni siquiera girar").
    // Vectores propios del escalador temporal (DLSS SR por la etiqueta local de EvaluateDlss; XeSS / FSR 3.1-4 por SrMvec), afinados a
    // fracciones de punto, en mvecSr. Los de siempre (mvec) NO cambian: DLSS 5 y su antiparpadeo, la generacion y su vector de la camara
    // siguen con ellos exactamente igual.
    // CAUSA (medido con la escena zoom: textura de juego 4K, giro, acercamiento y "suelo"; 1440p -> 4K; volcado de cada imagen contra la
    // verdad): la busqueda por bloques de 8x8 (Shaders.cs Search) da 0,5-1 px de error medio donde hay textura (71 % a mas de 0,25 px con la
    // camara lenta): prefiere el 0 si casi casa (mvsesgo / mvruido: 2 niveles por punto de margen; solo un giro de >= 64 px se lo salta), y
    // mvapertura / mvconsenso alisan el campo (en un suelo en perspectiva el movimiento cambia de un sitio a otro). Con esos vectores DLSS SR
    // trae la historia desalineada: borroso y doble. Con los vectores de VERDAD (mvverdad=8) el error de la imagen baja a menos de la mitad
    // en todos los modelos (K 1,32 -> 0,53 niveles, M 1,00 -> 0,61, L 1,01 -> 0,64, E 1,10 -> 0,79) y deja de depender de la velocidad.
    // ARREGLO: por casilla de 8x8 (sr_vec_casilla; con 4x4 sale igual y cuesta 4 veces mas), Lucas-Kanade desde el vector de la busqueda sobre la luma algo desenfocada ([1 2 1] x [1 2 1], FP16: sin
    // el, la interpolacion bilineal de la anterior "tira" de la fraccion hacia el punto entero y el afinado se va 0,3 px), con las muestras
    // de una ventana de 16x16 que llevan ese mismo movimiento (la interfaz quieta y el borde del personaje no cuentan), sr_vec_vueltas
    // vueltas; se queda con el que mejor casa si casa mejor que el de la busqueda (sr_vec_ganancia) y la correccion se suma a los puntos de
    // la casilla con ese movimiento (los otros, tal cual). Simulado en la CPU (lksim, solo traslacion): desde 0 con 0,8 px de verdad ->
    // 0,22 px; desde +-0,4 px -> 0,09; quieto con ruido de +-2 niveles: 0,009 px (con sr_vec_ganancia=0,8; con 0,95, 0,1: hervido).
    // Claves (motor_extra o la linea del motor): sr_vectores=0|1, sr_vec_radio, sr_vec_paso, sr_vec_vueltas, sr_vec_max (px),
    // sr_vec_ganancia, sr_vec_lambda, sr_vec_suelo (niveles: lo que ya casa por debajo no se toca), sr_vec_esm.
    partial class Settings
    {
        public int SrVec = 1;   // (de serie: 1; 0 = DLSS SR / XeSS / FSR con los vectores de la busqueda, como antes)
        public int SrVecRadius = 8, SrVecStep = 2, SrVecIter = 6, SrVecTile = 8;
        public bool SrVec120 = false;   // (sr_vec_120: tambien en el modo 120 -esports, 8 ms por imagen-; de serie no)
        public double SrVecMax = 1.5, SrVecGain = 0.8, SrVecLambda = 5e-6, SrVecFloor = 0;
        // 23-11 (1.2.4 ronda 7, SRBLUR) sr_vec_incert (px, de serie 0,1): la correccion solo si la incertidumbre de Lucas-Kanade (desviacion
        // del vector = raiz(residuo por muestra / (muestras x menor autovalor del tensor de estructura))) no pasa de eso y la correccion es al
        // menos el doble. CAUSA (medido con video real: Dead by Daylight 4K de noche, 1440p + DLSS M): en lo oscuro / de poco detalle con
        // ruido la interpolacion de la anterior a medio punto ya "casa mejor" (promedia el ruido) y la casilla se corria hasta 1,5 px CADA
        // imagen con la camara quieta: DLSS SR remuestreaba su historia sin parar y el suelo se deshacia en una mancha (nitidez 0,83 -> 0,15 en
        // 2 s; con los vectores de la busqueda, 0,80). Con 0,1: 0,80 (arreglado) y lo que los afinados ganaban se queda (BF1 quieto nitidez
        // 0,621 -> 0,641, Battlefront quieto "fantasma" 0,34 -> 0,16; error igual que con los de la ronda 6 en todo). 0 = como la ronda 6.
        // sr_vec_textura (niveles, de serie 0 = no): sin afinar la casilla si su menor autovalor por muestra no llega (arregla lo oscuro con 2,
        // pero quita lo que se gana en lo claro)
        public double SrVecTex = 0, SrVecIncert = 0.1;
        public bool SrVecEsm = true;
        // Los filtros de la entrada (Shaders.cs Aa: bordes -FXAA-, tramado y liso; con aa=0 la pasada es una copia y tampoco hay tramado ni
        // liso) con el escalador temporal que reescala y SIN DLSS 5 (con DLSS 5 son su entrada: no cambian). -1 = los de siempre (aa=,
        // dedither=, liso=). De serie sr_aa=0: medido en el suelo de la escena zoom (textura fina de poco contraste) con K QUIETO, error 0,76
        // -> 0,58 niveles y nitidez 0,880 -> 0,914 (liso=0 solo: 0,65 sin mas nitidez; dedither=0 solo: igual): el FXAA de la entrada toma la
        // textura del suelo por bordes y la emborrona antes de DLSS SR, que ya suaviza los bordes el mismo. En movimiento con los vectores
        // afinados, M 0,62 -> 0,41 y K 0,96 -> 0,82. sr_aa=-1 (o 1) = como antes
        public double SrFlat = -1; public int SrAa = 0, SrDedither = -1;
        // XeSS SR / FSR 3.1-4 de escalador con los vectores COMPLETOS de la busqueda, como DLSS SR (antes los ligeros a 1440p: mv_ligero).
        // Medido (escena zoom, 1440p, XeSS): vectores 1,42 px de error con los ligeros (0,72 los completos). sr_completos_xess=0 = como antes
        public bool SrXessFfxFull = true;
        // 23-11 (1.2.4 ronda 7, SRBLUR) sr_aa_dlss5=1 (de serie): lo de sr_aa / sr_tramado / sr_liso tambien con DLSS 5 cuando el escalador
        // reescala (1440p / 1080p + DLSS 5 -> DLSS SR / XeSS / FSR); a 4K nativo DLSS 5 sigue con su entrada de siempre. El usuario juega asi
        // (1440p + DLSS M + DLSS 5 nivel 4) y el arreglo del suelo de la ronda 6 (sin FXAA / liso antes del escalador) no le llegaba: con DLSS 5
        // el FXAA y el liso=6 seguian. Medido con video real (BF1 y Battlefront 4K -> 1440p), contra DLSS 5 a 4K nativo (lo que el usuario ve
        // perfecto): quieto err 2,82 -> 2,65, nitidez 0,80 -> 0,85; en movimiento 1,87 -> 1,84 y 0,76 -> 0,82; el hervido igual (0,16 / 0,31-0,33).
        // sr_aa_dlss5=0 = como antes
        public bool SrAaNr = true;
        // 23-11 (1.2.4 ronda 7, SRBLUR) sr_vec_suave=1: la correccion de cada casilla no se suma igual a sus puntos sino interpolada entre
        // los centros de las 4 casillas vecinas (las del mismo movimiento), como los vectores por punto: sin escalones de casilla a casilla
        public bool SrVecSmooth = false;
        // 23-11 (ronda 7, SRBLUR) sr_vec_brillo=1: Lucas-Kanade con un cambio de brillo de la casilla (no lo toma por movimiento)
        public bool SrVecBright = false;

        bool ApplySrVec(string key, string val)
        {
            switch (key)
            {
                case "sr_vectores": SrVec = I(val); return true;
                case "sr_vec_radio": SrVecRadius = Math.Max(2, Math.Min(16, I(val))); return true;
                case "sr_vec_paso": SrVecStep = Math.Max(1, Math.Min(4, I(val))); return true;
                case "sr_vec_vueltas": SrVecIter = Math.Max(1, Math.Min(10, I(val))); return true;
                case "sr_vec_max": SrVecMax = Math.Max(0.1, Math.Min(4, D(val))); return true;
                case "sr_vec_ganancia": SrVecGain = Math.Max(0.1, Math.Min(1, D(val))); return true;
                case "sr_vec_lambda": SrVecLambda = Math.Max(0, D(val)); return true;
                case "sr_vec_suelo": SrVecFloor = Math.Max(0, D(val)); return true;
                case "sr_vec_textura": SrVecTex = Math.Max(0, D(val)); return true;
                case "sr_vec_incert": SrVecIncert = Math.Max(0, D(val)); return true;
                case "sr_vec_esm": SrVecEsm = B(val); return true;
                case "sr_vec_casilla": SrVecTile = I(val) >= 8 ? 8 : 4; return true;
                case "sr_vec_120": SrVec120 = B(val); return true;
                case "sr_liso": SrFlat = D(val); return true;
                case "sr_aa": SrAa = I(val); return true;
                case "sr_tramado": SrDedither = I(val); return true;
                case "sr_completos_xess": SrXessFfxFull = B(val); return true;
                case "sr_aa_dlss5": SrAaNr = B(val); return true;
                case "sr_vec_suave": SrVecSmooth = B(val); return true;
                case "sr_vec_brillo": SrVecBright = B(val); return true;
            }
            return ApplySrBanco(key, val);   // (23-11 ronda 7, SRBLUR: EngineSrBanco.cs)
        }
    }

    unsafe partial class Engine
    {
        // (Engine.cs: TablesCount) TSrVec + juego (2): t0 = luma desenfocada de ahora, t1 = la de la anterior, t2 = mvec, u0 = mvecSr;
        // TSrBlur + juego (2): t0 = luma del nivel 0 de ese juego, u0 = su luma desenfocada
        const int TSrVec = TNr + 9, TSrBlur = TNr + 11;
        ID3D12PipelineState psoSrVec, psoSrBlur;

        // (Engine.cs RenderFrame y EngineCopia.cs: la pasada de bordes / tramado / liso) con el escalador temporal que reescala y sin DLSS 5
        bool SrXessFfxFull { get { return s.SrXessFfxFull && (scalerNow == "xess" ? !xessBroken : scalerNow == "ffx" ? !ffxBroken : false); } }
        bool SrFiltroPropio { get { return (!s.Nr || s.SrAaNr) && (inW != outW || inH != outH) && (scalerNow == "dlss" || scalerNow == "xess" || scalerNow == "ffx"); } }
        bool AaNow { get { return SrFiltroPropio && s.SrAa >= 0 ? s.SrAa > 0 : s.Aa; } }
        bool DeditherNow { get { return SrFiltroPropio && s.SrDedither >= 0 ? s.SrDedither > 0 : s.Dedither; } }
        double FlatNow { get { return SrFiltroPropio && s.SrFlat >= 0 ? s.SrFlat : s.FlatDenoise; } }
        readonly ID3D12Resource[] srBlur = new ID3D12Resource[2];

        const string SrBlurShader = @"
cbuffer C : register(b0) { uint W, H, P0, P1; uint4 Pad1; uint4 Pad2; uint4 Pad3; }
Texture2D<float> In : register(t0);
RWTexture2D<float> Out : register(u0);
float L(int2 p) { return In.Load(int3(clamp(p, int2(0, 0), int2(W - 1, H - 1)), 0)); }
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int2 p = int2(id.xy);
    float s = 4 * L(p) + 2 * (L(p + int2(1, 0)) + L(p - int2(1, 0)) + L(p + int2(0, 1)) + L(p - int2(0, 1))) +
              L(p + int2(1, 1)) + L(p + int2(-1, 1)) + L(p + int2(1, -1)) + L(p + int2(-1, -1));
    Out[id.xy] = s / 16;
}";

        const string SrVecShader = @"
cbuffer C : register(b0) { uint W, H, TW, TH; float MaxD, Gain, Lambda, Floor; uint Iter, WinR, Step, Esm; uint Tile, P1, P2, P3; }
Texture2D<float> Cur : register(t0);
Texture2D<float> Prev : register(t1);
Texture2D<float2> Mv : register(t2);
RWTexture2D<float2> Out : register(u0);
RWTexture2D<float4> TileOut : register(u1);
SamplerState Lin : register(s0);

float LdC(int2 p) { return Cur.Load(int3(clamp(p, int2(0, 0), int2(W - 1, H - 1)), 0)); }
float2 MvAt(int2 p) { return Mv.Load(int3(clamp(p, int2(0, 0), int2(W - 1, H - 1)), 0)); }

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= TW || id.y >= TH) return;
    int ts = (int)Tile, th2 = ts / 2;
    int2 t0 = int2(id.xy) * ts;
    float2 v0 = MvAt(t0 + int2(th2, th2));
    float2 best = v0;
    {
        float2 v = v0, inv = 1.0 / float2(W, H);
        float ssd0 = 0, bestSsd = 1e9, lmin0 = 0, nS = 1;
        int r = (int)WinR, st = (int)Step;
        int2 c = t0 + int2(th2, th2);
        uint2 skip = uint2(0, 0);   // (las muestras de otro movimiento, vistas en la primera vuelta: 64 como mucho)
        [loop] for (uint it = 0; it <= Iter; it++)
        {
            float bx = 0, by = 0, e = 0, hxx = 0, hxy = 0, hyy = 0, n = 0, sgx = 0, sgy = 0, sr = 0;
            uint ti = 0;
            [loop] for (int y = -r; y < r; y += st)
                [loop] for (int x = -r; x < r; x += st)
                {
                    uint bit = ti & 31, word = ti >> 5; ti++;
                    int2 q = c + int2(x, y);
                    if (q.x < 1 || q.y < 1 || q.x >= (int)W - 1 || q.y >= (int)H - 1) continue;
                    if (it == 0 || ti > 64)
                    {
                        float2 dq = MvAt(q) - v0;
                        if (abs(dq.x) + abs(dq.y) > 0.5) { if (ti <= 64) { if (word == 0) skip.x |= 1u << bit; else skip.y |= 1u << bit; } continue; }   // (otra cosa: otro movimiento)
                    }
                    else if (((word == 0 ? skip.x : skip.y) >> bit) & 1) continue;
                    float cc = LdC(q);
                    float gx = 0.5 * (LdC(q + int2(1, 0)) - LdC(q - int2(1, 0)));
                    float gy = 0.5 * (LdC(q + int2(0, 1)) - LdC(q - int2(0, 1)));
                    float2 uv = (float2(q) + 0.5 + v) * inv;
                    float pp = Prev.SampleLevel(Lin, uv, 0);
                    if (Esm)
                    {
                        // gradiente de las dos (ESM): converge antes cuando la anterior aun esta corrida
                        gx = 0.5 * gx + 0.25 * (Prev.SampleLevel(Lin, uv + float2(inv.x, 0), 0) - Prev.SampleLevel(Lin, uv - float2(inv.x, 0), 0));
                        gy = 0.5 * gy + 0.25 * (Prev.SampleLevel(Lin, uv + float2(0, inv.y), 0) - Prev.SampleLevel(Lin, uv - float2(0, inv.y), 0));
                    }
                    float rr = cc - pp;
                    e += rr * rr; bx += gx * rr; by += gy * rr;
                    hxx += gx * gx; hxy += gx * gy; hyy += gy * gy; n += 1;
                    sgx += gx; sgy += gy; sr += rr;
                }
            if (n < 8) break;
            if (P1 & 2)
            {
                // (23-11 ronda 7: sr_vec_brillo) con un cambio de brillo de la casilla (rr = gx dx + gy dy + b): sin el, en lo de poco detalle un
                // cambio de luz se toma por movimiento (video real oscuro: la casilla se corria 1,5 px cada imagen y DLSS emborronaba)
                float k = 1.0 / n;
                e -= sr * sr * k; bx -= sgx * sr * k; by -= sgy * sr * k;
                hxx -= sgx * sgx * k; hxy -= sgx * sgy * k; hyy -= sgy * sgy * k;
            }
            if (it == 0)
            {
                ssd0 = e;
                if (e <= Floor * n) break;   // (ya casa: lo que queda es ruido o nada)
                // (23-11 ronda 7: sr_vec_textura) sin detalle en las dos direcciones, Lucas-Kanade solo se inventa el movimiento
                float lmin = 0.5 * (hxx + hyy) - sqrt(0.25 * (hxx - hyy) * (hxx - hyy) + hxy * hxy);
                lmin0 = lmin; nS = n;
                if (lmin < asfloat(P2) * n) break;
            }
            if (e < bestSsd) { bestSsd = e; best = v; }
            if (it == Iter) break;
            float l = Lambda * n, a = hxx + l, dd = hyy + l, det = a * dd - hxy * hxy;
            if (det <= 1e-14) break;
            float2 d = float2((dd * bx - hxy * by) / det, (a * by - hxy * bx) / det);
            if (abs(d.x) + abs(d.y) < 0.01) break;   // (ya no se mueve: lo de esta vuelta es lo mejor)
            v += clamp(d, -1.0, 1.0);
            float2 dv = v - v0; float ld = length(dv);
            if (ld > MaxD) v = v0 + dv * (MaxD / ld);
        }
        if (!(bestSsd < Gain * ssd0)) best = v0;
        // (23-11 ronda 7: sr_vec_incert) en lo de poco detalle con ruido (video real oscuro, suelo de poco contraste) la interpolacion de la
        // anterior en un medio punto ya casa mejor (promedia el ruido): la casilla se corria hasta 1,5 px cada imagen y la historia de DLSS se
        // emborronaba sin parar. Solo si el vector es fiable (incertidumbre pequeña) y la correccion la supera con holgura
        if (asfloat(P3) > 0)
        {
            float sd = sqrt((bestSsd / max(nS, 1.0)) / max(lmin0, 1e-20));
            if (sd > asfloat(P3) || length(best - v0) < 2 * sd) best = v0;
        }
    }
    // la correccion a los puntos de la casilla que llevan ese movimiento (los vectores por punto interpolados siguen igual de suaves); los
    // de otro movimiento (la interfaz quieta, el borde del personaje) tal cual
    float2 dv = best - v0;
    if (P1 & 1) { TileOut[id.xy] = float4(dv, v0); return; }   // (sr_vec_suave: la correccion de la casilla, se reparte en SrVecApplyShader)
    [loop] for (int j2 = 0; j2 < ts; j2++)
        [loop] for (int i2 = 0; i2 < ts; i2++)
        {
            int2 p = t0 + int2(i2, j2);
            if (p.x >= (int)W || p.y >= (int)H) continue;
            float2 m = MvAt(p), dm = m - v0;
            Out[p] = abs(dm.x) + abs(dm.y) <= 0.5 ? m + dv : m;
        }
}";

        // (sr_vec_suave) la correccion de cada punto: la de las 4 casillas vecinas con el mismo movimiento, con el peso bilineal de su centro
        const string SrVecApplyShader = @"
cbuffer C : register(b0) { uint W, H, TW, TH; float MaxD, Gain, Lambda, Floor; uint Iter, WinR, Step, Esm; uint Tile, P1, P2, P3; }
Texture2D<float2> Mv : register(t2);
Texture2D<float4> Tiles : register(t3);
RWTexture2D<float2> Out : register(u0);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    float2 m = Mv.Load(int3(id.xy, 0));
    float2 f = (float2(id.xy) + 0.5) / (float)Tile - 0.5;
    int2 t0 = int2(floor(f)); float2 w = f - t0;
    float2 acc = 0; float ws = 0;
    [unroll] for (int j = 0; j < 2; j++)
        [unroll] for (int i = 0; i < 2; i++)
        {
            int2 t = clamp(t0 + int2(i, j), int2(0, 0), int2(TW - 1, TH - 1));
            float4 r = Tiles.Load(int3(t, 0));
            float2 d = m - r.zw;
            if (abs(d.x) + abs(d.y) > 0.5) continue;   // (otro movimiento: la interfaz quieta, el borde del personaje)
            float k = (i ? w.x : 1 - w.x) * (j ? w.y : 1 - w.y) + 1e-4;
            acc += r.xy * k; ws += k;
        }
    Out[id.xy] = ws > 0 ? m + acc / ws : m;
}";
        ID3D12PipelineState psoSrApply;
        ID3D12Resource srTiles;

        // CreateMotionResources (EngineMotion.cs), con la piramide, mvec y mvecSr ya hechos (lw[0] x lh[0] = la entrada)
        void SrVecViews()
        {
            for (int st = 0; st < 2; st++)
            {
                if (srBlur[st] != null) Com.Release(ref srBlur[st]);
                srBlur[st] = Tex(DXGI_FORMAT.R16_FLOAT, lw[0], lh[0], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "luma desenfocada " + st + " (vectores de DLSS SR)");
            }
            if (srTiles != null) Com.Release(ref srTiles);
            srTiles = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, (lw[0] + 3) / 4, (lh[0] + 3) / 4, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "correccion por casilla (sr_vec_suave)");   // (casillas de 4 o mas)
            for (int st = 0; st < 2; st++)
            {
                int t = TSrVec + st;
                NullViews(t);
                SrvTex(t, 0, srBlur[st], DXGI_FORMAT.R16_FLOAT);
                SrvTex(t, 1, srBlur[1 - st], DXGI_FORMAT.R16_FLOAT);
                SrvTex(t, 2, mvec, DXGI_FORMAT.R16G16_FLOAT);
                UavTex(t, U0, mvecSr, DXGI_FORMAT.R16G16_FLOAT);
                SrvTex(t, 3, srTiles, DXGI_FORMAT.R16G16B16A16_FLOAT);   // (sr_vec_suave: t3 en el reparto, u1 en la casilla)
                UavTex(t, U0 + 1, srTiles, DXGI_FORMAT.R16G16B16A16_FLOAT);
                int b = TSrBlur + st;
                NullViews(b);
                SrvTex(b, 0, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                UavTex(b, U0, srBlur[st], DXGI_FORMAT.R16_FLOAT);
            }
        }

        // RecordMotion (EngineMotion.cs), con mvec ya hecho de esta imagen (en NON_PIXEL_SHADER_RESOURCE) y cur = el juego de luma de ahora.
        // Solo para un escalador temporal que reescala (DLSS SR; XeSS / FSR 3.1-4 leen SrMvec); con mvmask_modo=1 no (usa mvecSr)
        int srFiltroSaid = -1;
        void RecordSrVec(int cur, bool on, uint* c)
        {
            int fs = SrFiltroPropio && (AaNow != s.Aa || DeditherNow != s.Dedither || FlatNow != s.FlatDenoise) ? 1 : 0;
            if (fs != srFiltroSaid)
            {
                srFiltroSaid = fs;
                if (fs == 1) Log.Write("Entrada del escalador (" + scalerNow + (s.Nr ? ", con DLSS 5" : ", sin DLSS 5") + "): " + (AaNow ? "bordes" + (DeditherNow ? ", tramado" : "") + (FlatNow > 0 ? ", liso " + FlatNow.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "") : "sin FXAA, tramado ni liso") + " (sr_aa / sr_tramado / sr_liso; con DLSS 5, sr_aa_dlss5; a 4K nativo, los de siempre)");
            }
            if (s.SrVec <= 0 || !on || srVecValid || s.MvMaskMode == 1 || s.MvFixed || mvecSr == null || srBlur[0] == null || (s.Is120 && !s.SrVec120)) return;
            bool sr = (inW != outW || inH != outH) && (scalerNow == "dlss" || scalerNow == "xess" || scalerNow == "ffx");
            if (!sr) return;
            if (psoSrVec == null)
            {
                long t = Clock.Now;
                psoSrVec = Pso(CompileCs("srvec", SrVecShader));
                psoSrBlur = Pso(CompileCs("srblur", SrBlurShader));
                Log.Write("Vectores de DLSS SR afinados (sr_vectores=" + s.SrVec + (s.SrVecIncert > 0 ? ", solo los fiables: sr_vec_incert=" + s.SrVecIncert.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " px" : "") + "): shaders en " + Clock.SinceMs(t).ToString("0") + " ms");
            }
            int w = lw[0], h = lh[0];
            // la luma desenfocada de las dos imagenes (la de ahora y la anterior: barato, y asi no depende de lo que se hizo antes)
            for (int k = 0; k < 2; k++)
            {
                int st = k == 0 ? cur : 1 - cur;
                Barrier(srBlur[st], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                for (int i = 0; i < 16; i++) c[i] = 0;
                c[0] = (uint)w; c[1] = (uint)h;
                BindCompute(psoSrBlur, TSrBlur + st, c, 4);
                list.Dispatch(Groups(w, 16), Groups(h, 8), 1);
                Barrier(srBlur[st], D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            }
            int tile = s.SrVecTile, tw = (w + tile - 1) / tile, th = (h + tile - 1) / tile;
            bool suave = s.SrVecSmooth && srTiles != null;
            if (suave && psoSrApply == null) psoSrApply = Pso(CompileCs("srvecreparto", SrVecApplyShader));
            Barrier(mvecSr, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            if (suave) Barrier(srTiles, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)w; c[1] = (uint)h; c[2] = (uint)tw; c[3] = (uint)th;
            c[4] = F((float)s.SrVecMax); c[5] = F((float)s.SrVecGain); c[6] = F((float)s.SrVecLambda); c[7] = F((float)(s.SrVecFloor / 255.0 * s.SrVecFloor / 255.0));
            c[8] = (uint)s.SrVecIter; c[9] = (uint)s.SrVecRadius; c[10] = (uint)s.SrVecStep; c[11] = s.SrVecEsm ? 1u : 0u; c[12] = (uint)tile; c[13] = (suave ? 1u : 0u) | (s.SrVecBright ? 2u : 0u);
            c[14] = F((float)(s.SrVecTex / 255.0 * s.SrVecTex / 255.0)); c[15] = F((float)s.SrVecIncert);   // (23-11 ronda 7: sr_vec_textura, sr_vec_incert)
            BindCompute(psoSrVec, TSrVec + cur, c, 16);
            list.Dispatch(Groups(tw, 8), Groups(th, 8), 1);
            if (suave)
            {
                Barrier(srTiles, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                BindCompute(psoSrApply, TSrVec + cur, c, 16);
                list.Dispatch(Groups(w, 16), Groups(h, 8), 1);
            }
            Barrier(mvecSr, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            srVecValid = true;
        }
    }
}
