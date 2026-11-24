using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 20-11-2026 (DLSS 5 EN EL MOTOR): DLSSNR ("DLSS 5") sobre la captura, despues de los vectores de movimiento (paso 1b) y antes de
    // DLSS SR. native\PrismaNr\PrismaNr.dll (C++) carga la nvngx_dlssnr.dll comunitaria de Magpie y la llama directamente (como Magpie);
    // el resultado vuelve a inColor, asi DLSS SR, CAS y la generacion siguen igual. Ajustes: dlss5=1, dlss5_nivel, dlss5_estilo,
    // dlss5_modelo, dlss5_mascara, dlss5_interfaz, dlss5_fuerza, dlss5_entrada, dlss5_pasadas, dlss5_fuerza_pasadas, dlss5_antiparpadeo,
    // dlss5_antiparpadeo_ms, dlss5_dir. Si algo falla, el motor sigue sin DLSS 5 (y lo dice en el log).
    static class PrismaNr
    {
        [DllImport("PrismaNr.dll", CallingConvention = CallingConvention.StdCall)] public static extern IntPtr PNR_LastError();
        [DllImport("PrismaNr.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)] public static extern int PNR_Init(IntPtr device, string runtimeDir);
        [DllImport("PrismaNr.dll", CallingConvention = CallingConvention.StdCall)] public static extern int PNR_CreateN(int idx, IntPtr list, uint w, uint h, int preset);
        [DllImport("PrismaNr.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int PNR_EvaluateN(int idx, IntPtr list, IntPtr color, IntPtr output, IntPtr mvec, IntPtr depth, uint mvW, uint mvH, float mvScaleX, float mvScaleY,
            int style, float intensity, float tone, float structure, float skin, int autoMask, int uiCorrection, int reset);
        [DllImport("PrismaNr.dll", CallingConvention = CallingConvention.StdCall)] public static extern void PNR_ReleaseN(int idx);
        [DllImport("PrismaNr.dll", CallingConvention = CallingConvention.StdCall)] public static extern void PNR_Shutdown();
        public static string LastError() { try { return Marshal.PtrToStringAnsi(PNR_LastError()) ?? ""; } catch { return ""; } }
    }

    unsafe partial class Engine
    {
        // ---- 20-11: "fuerza total" (dlss5_fuerza, el residualMultiplier de Magpie) y % de entrada (dlss5_entrada, 25-100, como el
        // inputResolutionPercent de Magpie). Con entrada < 100, DLSS 5 trabaja sobre la captura reducida (Catmull-Rom) y lo que cambia
        // (salida - entrada reducida) se amplia (bilineal) y se suma a la captura entera: el detalle fino de la captura se queda.
        // inColor = entrada + (DLSS 5 - entrada reducida) x fuerza.
        // ---- 20-11: 2-3 pasadas (dlss5_pasadas): cada una es otra DLSSNR (su propia historia) sobre la salida de la anterior, con su
        // fuerza (dlss5_fuerza_pasadas, la de Prisma: 1,0.6,0.5; con la misma fuerza el efecto se acumulaba, ver Magpie). Antiparpadeo
        // (dlss5_antiparpadeo 1-3, dlss5_antiparpadeo_ms): el filtro temporal de Magpie (DLSSNRTemporalShader.h) sobre lo que cambia DLSS 5,
        // en SDR y sin el modo 4 (a media resolucion) ni la memoria de color: 1 sin vectores, 2 con vectores, 3 con vectores y soporte
        // (60 ms de subida / 180 de bajada). El peso de la historia es exp(-tiempo / dlss5_antiparpadeo_ms), 0 a partir de 250 ms.
        const int TNr = TQBase + TQCount + 1 + TRCount;   // mezcla: t0 = inColor, t1 = salida (o antiparpadeo), t2 = entrada de DLSS 5, u0 = nrBlend
        const int TNrDown = TNr + 1;                       // reduccion de la captura (el Catmull-Rom del motor): t0 = inColor, u0 = nrIn
        const int TNrPrep = TNr + 2;                       // vectores y profundidad reducidos: t0 = mvec, t1 = depth, u0 = nrMv, u1 = nrDepth
        const int TNrAf0 = TNr + 3, TNrAf1 = TNr + 4;      // antiparpadeo (dos juegos: la historia va y viene): t0 entrada, t1 salida, t2 historia,
                                                           // t3 guia (la entrada anterior), t4 vectores; u0 = afOut, u1 = historia nueva
        // (Engine.cs: TablesCount = ... + 5)
        const string NrBlendShader = @"
cbuffer C : register(b0) { uint W, H, P0, P1; float K, Q0, Q1, Q2; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> A : register(t0);
Texture2D<float4> B : register(t1);
Texture2D<float4> Cin : register(t2);
RWTexture2D<unorm float4> Out : register(u0);
SamplerState Lin : register(s0);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    float2 uv = (id.xy + 0.5) / float2(W, H);
    float3 a = A[id.xy].rgb;
    float3 d = B.SampleLevel(Lin, uv, 0).rgb - Cin.SampleLevel(Lin, uv, 0).rgb;
    if (Q0 > 0 && Q0 != 1) { float dy = dot(d, float3(0.2126, 0.7152, 0.0722)); d = dy + (d - dy) * Q0; }   // (dlss5_color)
    Out[id.xy] = float4(saturate(a + d * K), 1);
}";
        // 20-11 (LATENCIA7, dlss5_mezcla_directa=1): la misma mezcla escrita en inColor (UAV, cada hilo lee y escribe su punto): sin la
        // textura nrBlend ni su copia a inColor (a 4K, 33 MB leidos y escritos de mas por imagen). P0 = 1 con la entrada reducida (Cin =
        // nrIn); 0 al 100 % (Cin era inColor en el mismo punto: = a)
        const string NrBlendDirectShader = @"
cbuffer C : register(b0) { uint W, H, P0, P1; float K, Q0, Q1, Q2; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> B : register(t1);
Texture2D<float4> Cin : register(t2);
RWTexture2D<unorm float4> Io : register(u0);
SamplerState Lin : register(s0);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    float2 uv = (id.xy + 0.5) / float2(W, H);
    float3 a = Io[id.xy].rgb;
    float3 c = a;
    if (P0 != 0) c = Cin.SampleLevel(Lin, uv, 0).rgb;
    float3 d = B.SampleLevel(Lin, uv, 0).rgb - c;
    if (Q0 > 0 && Q0 != 1) { float dy = dot(d, float3(0.2126, 0.7152, 0.0722)); d = dy + (d - dy) * Q0; }   // (dlss5_color)
    Io[id.xy] = float4(saturate(a + d * K), 1);
}";
        ID3D12PipelineState psoNrBlendD;   // 20-11 (LATENCIA7)
        bool nrDirect;                     // 20-11 (LATENCIA7): como se creo (mezcla directa en inColor)
        bool nrDirectBroken;               // 20-11 (LATENCIA7): su shader no se pudo crear (se sigue con la de antes)
        bool DirectBlendNow() { return s.NrBlendDirect && !nrDirectBroken; }
        const string NrPrepShader = @"
cbuffer C : register(b0) { uint W, H, SW, SH; float S, Q0, Q1, Q2; uint4 Pad1; uint4 Pad2; }
Texture2D<float2> Mv : register(t0);
Texture2D<float> Dp : register(t1);
RWTexture2D<float2> OMv : register(u0);
RWTexture2D<float> ODp : register(u1);
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    uint2 p = min(uint2((id.xy + 0.5) * float2(SW, SH) / float2(W, H)), uint2(SW - 1, SH - 1));
    OMv[id.xy] = Mv[p] * S;   // (los vectores van en pixeles: a la escala de la imagen reducida)
    ODp[id.xy] = Dp[p];
}";
        // El de Magpie (DLSSNRTemporalShader.h) con Base = Input, SDR, region entera, sin la ruta 4 ni el color Lab
        // Derivado de src/Magpie.Core/DLSSNRTemporalShader.h de Magpie Experimental v0.6.12 (https://github.com/SAOG0721/Magpie, fork de
        // https://github.com/Blinue/Magpie), (c) los autores de Magpie, GNU GPL version 3. Esta parte va solo con la GPL-3.0 (sin el
        // permiso adicional de LICENSE-EXCEPTION.txt, que solo dan los autores de ConsoleEnhancer). Ver THIRD-PARTY.md
        const string NrAntiFlickerShader = @"
cbuffer C : register(b0) { uint W, H, UseMotion, Route; float HistoryWeight, Q0, Q1, Q2; uint4 Pad1; uint4 Pad2; }
Texture2D<float4> Input : register(t0);
Texture2D<float4> Raw : register(t1);
Texture2D<float4> History : register(t2);
Texture2D<float4> PreviousGuide : register(t3);
Texture2D<float2> Motion : register(t4);
RWTexture2D<unorm float4> Output : register(u0);
RWTexture2D<float4> NextHistory : register(u1);
SamplerState LinearClamp : register(s0);
bool Inside(int2 p) { return all(p >= 0) && all(p < int2(W, H)); }
bool PreviousPosition(int2 p, out float2 previous) {
    previous = p;
    if (!Inside(p)) return false;
    if (UseMotion) {
        float2 mv = Motion.Load(int3(p, 0));
        if (!all(isfinite(mv))) return false;
        previous += mv;
    }
    return all(previous >= 0) && all(previous <= float2(W, H) - 1);
}
[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID) {
    int2 p = id.xy;
    if (any(id.xy >= uint2(W, H))) return;
    float4 original = Input.Load(int3(p, 0));
    float4 raw = Raw.Load(int3(p, 0));
    float3 current = raw.rgb - original.rgb;
    float3 delta = abs(raw.rgb - original.rgb);
    float observed = smoothstep(.005, .02, max(delta.r, max(delta.g, delta.b)));
    float support = observed;
    float3 result = current;
    float historyWeight = HistoryWeight;
    float2 previous;
    if (historyWeight > 0 && PreviousPosition(p, previous)) {
        float error = 0, maximumError = 0;
        bool valid = true;
        float3 lo = current, hi = current;
        [unroll] for (int y = -1; y <= 1; ++y) {
            [unroll] for (int x = -1; x <= 1; ++x) {
                int2 n = p + int2(x, y);
                float2 previousN;
                if (!PreviousPosition(n, previousN)) { valid = false; continue; }
                if (UseMotion && any(abs((previousN - n) - (previous - p)) > 2)) { valid = false; continue; }
                float4 inputN = Input.Load(int3(n, 0));
                float4 old = PreviousGuide.SampleLevel(LinearClamp, (previousN + .5) / float2(W, H), 0);
                float3 residualN = Raw.Load(int3(n, 0)).rgb - inputN.rgb;
                float3 d = abs(inputN.rgb - old.rgb);
                float e = max(d.r, max(d.g, d.b));
                error += e / 9;
                maximumError = max(maximumError, e);
                lo = min(lo, residualN); hi = max(hi, residualN);
            }
        }
        float q = (1 - smoothstep(.008, .04, error)) * (1 - smoothstep(.025, .10, maximumError));
        if (valid && q > 0) {
            float4 old = History.SampleLevel(LinearClamp, (previous + .5) / float2(W, H), 0);
            if (all(isfinite(old)) && old.a >= .999) {
                float3 margin = .02 + q * abs(old.rgb);
                float3 safe = clamp(old.rgb, lo - margin, hi + margin);
                if (Route == 3) {
                    float oldSupport = saturate(old.a - 1);
                    float memory = pow(abs(historyWeight), .08 / (observed > oldSupport ? .06 : .18)) * q;
                    support = lerp(observed, oldSupport, memory);
                    if (oldSupport <= .001) result = current;
                    else if (observed > 0 && dot(current, old.rgb) < 0) {
                        support = observed * (1 - pow(abs(historyWeight), .08 / .06) * q);
                        result = current;
                    } else {
                        float update = (1 - historyWeight * q) * observed;
                        result = lerp(safe, current, update);
                    }
                } else result = lerp(current, safe, historyWeight * q);
            }
        }
    }
    NextHistory[p] = float4(clamp(result, -65504, 65504), Route == 3 ? 1 + support : 1);
    float3 color = original.rgb + (Route == 3 ? support * result : result);
    Output[p] = float4(saturate(color), 1);
}";
        ID3D12PipelineState psoNrBlend, psoNrPrep, psoNrAf;
        ID3D12Resource nrInColor;          // el inColor de las tablas (si el motor lo recrea, se rehace todo)
        ID3D12Resource nrBlend;            // inColor con lo de DLSS 5 segun la fuerza (inW x inH, RGBA8, UAV)
        ID3D12Resource[] nrOuts = new ID3D12Resource[3];   // salida de cada pasada (nrW x nrH, RGBA8, UAV)
        ID3D12Resource nrIn, nrMv, nrDepth; // con entrada < 100: la captura, los vectores y la profundidad reducidos (nrW x nrH)
        ID3D12Resource afOut, afGuide;     // antiparpadeo: su salida (UAV) y la entrada de la imagen anterior (la guia)
        ID3D12Resource[] afHist = new ID3D12Resource[2];   // su historia (RGBA16F con signo), va y viene
        int afPing;                        // la historia que se lee (0/1)
        long afLastTick;                   // cuando se proceso la ultima imagen (para el peso de la historia)
        bool nrInited, nrBroken;           // PNR_Init hecho / DLSS 5 roto en esta sesion (se sigue sin el)
        int nrW, nrH, nrSrcW, nrSrcH, nrPasses, nrAf;   // como se creo (tamaños, pasadas, antiparpadeo; nrW 0 = sin crear)
        bool nrReset = true;               // historia nueva (tras crear o cambiar de tamaño)
        int nrFails;
        long nrFrames;
        static readonly Stopwatch nrClock = Stopwatch.StartNew();

        // 20-11 (CIERRES): el guardian (dlss5_guardian, de serie 1): si la grafica pasa mucho de su tope con DLSS 5 puesto, se baja solo
        // (como el modelo de DLSS SR, L > M > K > E): primero las pasadas (3 > 2 > 1) y luego el % de entrada (75, 50). 04:14 del 20-11: 3
        // pasadas al 75 % en el 30 + DLSS FG a 1440p (lo de la tabla motor_dlss5_entrada_30, medido con la imagen de prueba: 22 ms) dieron
        // p95 61 ms con la consola (tope 26), 20 s de tirones y la GPU parada al poco. Lo bajado vale para esta sesion del motor y este modo
        // (modo | generacion | tamaño, como el modelo); si el lanzador vuelve a pedir mas, se queda en lo que cabe (y lo dice)
        readonly System.Collections.Generic.Dictionary<string, int> nrPassCap = new System.Collections.Generic.Dictionary<string, int>();
        readonly System.Collections.Generic.Dictionary<string, int> nrPctCap = new System.Collections.Generic.Dictionary<string, int>();
        long nrCreatedAt, nrGuardAt;
        int nrOver, nrOverHard;
        string nrClampSaid = "";
        bool nrFloorSaid;

        void Dlss5Caps(ref int passes, ref int pct)
        {
            string key = CapKey, said = "";
            int c;
            if (nrPassCap.TryGetValue(key, out c) && passes > c) { said = passes + " pasadas -> " + c; passes = c; }
            if (nrPctCap.TryGetValue(key, out c) && pct > c) { said += (said.Length > 0 ? ", " : "") + "entrada " + pct + " % -> " + c + " %"; pct = c; }
            if (said == nrClampSaid) return;
            nrClampSaid = said;
            if (said.Length > 0) Log.Write("DLSS 5: lo pedido no cabe aqui (lo bajo el guardian en esta sesion): " + said);
        }

        void Dlss5GuardTick()
        {
            if (!s.Dlss5Guard || nrW == 0) return;
            long now = Clock.Now;
            if (Clock.Ms(nrGuardAt, now) < 1000) return;
            nrGuardAt = now;
            double p = stats.LastGpuP95;
            // (como AutoPresetTick: no mientras el lanzador cambia de imagen -la grafica la comparte con Magpie- ni recien creado)
            if (Program.FocusHold || Clock.Ms(nrCreatedAt, now) < 5000 || p <= 0) { nrOver = nrOverHard = 0; return; }
            double limit = AdTope;   // (ConsoleEnhancer: el mismo tope que EngineAdapta.cs; en Prisma el de AutoPresetTick, sin lo de Smooth Motion; a 120 los 8 ms de Prisma HZ120)
            nrOverHard = p > limit * 1.6 ? nrOverHard + 1 : 0;   // muy por encima: 2 s
            nrOver = p > limit * 1.15 ? nrOver + 1 : 0;           // por encima: 5 s
            if (nrOverHard < 2 && nrOver < 5) return;
            nrOver = nrOverHard = 0;
            string key = CapKey, what;
            int curPct = nrW >= inW ? 100 : (int)Math.Round(100.0 * nrW / Math.Max(1, inW));
            // 22-11 (1.2.4 DLSS5Q): con 2-3 pasadas, primero el % si con las mismas pasadas cabe al 50 % o mas, y si no, las pasadas: quitar
            // una pasada cambia mucho mas el aspecto (medido a 4K: 1 -> 2 pasadas, el croma del efecto de -2 a -7 niveles) que bajar el %
            // (el usuario eligio 2-3 pasadas por su aspecto; Magpie en la 1.2.3 tambien bajaba el %), pero rehacer 2-3 DLSSNR a otro
            // tamaño para la imagen 0,6-1,3 s (medido: 589 ms 2 pasadas, 1298 ms 3) y quitar pasadas no rehace nada (se quedan las que
            // estaban); por debajo del 50 % DLSS 5 pierde mucho detalle
            double fijoG = p * 0.3, variaG = p - fijoG, objG = limit * 0.92;
            int suelo = NrSuelo;   // (1.3.2, FPS: el suelo de DLSS 5 con esta entrada, EngineEscalera.cs)
            int needPct = suelo;
            for (int t = curPct - 5; t >= suelo; t -= 5) { needPct = t; if (fijoG + variaG * (t * (double)t) / (curPct * (double)curPct) <= objG) break; }
            bool pctFirst = s.NrSuave && nrPasses > 1 && curPct > 50 && needPct >= 50;
            if (nrPasses > 1 && !pctFirst)
            {
                // 22-11 (1.2.4 DLSS5Q): de una vez a las que caben (el 30 % de la grafica no depende de las pasadas): 3 -> 2 -> 1 eran dos
                // cambios de aspecto en 28 s (log del usuario 21:35:04 y 21:35:32, 1440p con 3 pasadas: p95 54 ms de 30)
                int to = nrPasses - 1;
                double porPasada = p * 0.7 / nrPasses;
                while (s.NrSuave && to > 1 && p * 0.3 + porPasada * to > limit) to--;
                nrPassCap[key] = to;
                what = to + (to > 1 ? " pasadas" : " pasada");
            }
            else
            {
                int cur = curPct;
                if (cur <= 50)
                {
                    if (!nrFloorSaid) { nrFloorSaid = true; Log.Write(string.Format(IC, "DLSS 5 ya esta al minimo del guardian (1 pasada al {0} %) y la grafica sigue sin llegar (p95 {1:0.0} ms, tope {2:0.0})", cur, p, limit)); }
                    return;
                }
                int next = cur > 75 ? 75 : 50;
                if (s.NrSuave)
                {
                    // 22-11 (1.2.4 DLSS5Q): de una vez al % que cabe (el 30 % de la grafica no depende del %; lo demas, con su cuadrado), no a
                    // 75 y luego a 50: cada paso rehace DLSS 5 (paron y otro aspecto). Medido con poca grafica (tope 14,6 ms, 4K al 90 %):
                    // antes 90 -> 75 -> 65 -> 55 -> 45 en 33 s (4 paradas), ahora 90 -> 45
                    double fijo = p * 0.3, varia = p - fijo, obj = limit * 0.92;
                    next = Math.Max(suelo, cur - 45);
                    for (int t = cur - 10; t >= Math.Max(suelo, cur - 45); t -= 5) { next = t; if (fijo + varia * (t * (double)t) / (cur * (double)cur) <= obj) break; }
                    next = Math.Min(next, cur > 75 ? 75 : Math.Max(suelo, cur - 10));
                    if (pctFirst) next = Math.Max(50, next);
                }
                nrPctCap[key] = next;
                what = (nrPasses > 1 ? nrPasses + " pasadas" : "1 pasada") + " al " + next + " %";
            }
            AdaptaFreno(key, p, limit);   // (ConsoleEnhancer: con adapta=1 lo bajado pasa al estado de EngineAdapta.cs, uno solo y recordado)
            Log.Write(string.Format(IC, "DLSS 5 no cabe aqui (grafica p95 {0:0.0} ms, tope {1:0.0}; {2} pasada(s) a {3}x{4}): se baja a {5} (el guardian, para esta sesion y este modo)",
                p, limit, nrPasses, nrW, nrH, what));
            osd.Show("DLSS 5 no llega aquí · se baja a " + what + " para no colgar la gráfica", 4000, null);
        }

        // 20-11 (CIERRES): las DLSSNR y sus texturas fuera, esperando antes a que la grafica acabe lo que las usa (en espera: Magpie hace
        // DLSS 5 en la misma grafica de 8 GB y las del motor seguian ocupando; con DLSS 5 apagado en el motor, igual). Se vuelven a crear al
        // hacer falta (al despertar ya se creaban otra vez: inColor es otro)
        void ReleaseDlss5(string why)
        {
            if (!nrInited || (nrW == 0 && nrOuts[0] == null)) return;
            WaitIdle();
            int n = nrPasses;
            foreach (ID3D12Resource r in new[] { nrOuts[0], nrOuts[1], nrOuts[2], nrBlend, nrIn, nrMv, nrDepth, afOut, afGuide, afHist[0], afHist[1] }) if (r != null) Bury(r);
            nrOuts[0] = nrOuts[1] = nrOuts[2] = nrBlend = nrIn = nrMv = nrDepth = afOut = afGuide = afHist[0] = afHist[1] = null;
            D5aCancel();   // (22-11 DLSS5Q: la creacion aparte, si estaba en marcha)
            for (int i = 0; i < 3; i++) PrismaNr.PNR_ReleaseN(nrBank + i);
            nrW = nrH = nrSrcW = nrSrcH = nrPasses = 0;
            nrInColor = null;
            nrReset = true;
            Log.Write("DLSS 5 (DLSSNR) soltado (" + why + "; " + n + " instancia(s) y sus texturas, con la grafica ya parada)");
        }

        // En el paso 1b de RenderFrame, con inColor/mvec/depth en NON_PIXEL_SHADER_RESOURCE
        void RecordDlss5(bool newFrame)
        {
            TestHangStep();   // 20-11 (CIERRES): prueba_cuelgue_ms (EngineCuelgue.cs)
            if (!s.Nr && nrW != 0 && newFrame) ReleaseDlss5("DLSS 5 apagado");
            if (!s.Nr || nrBroken || !newFrame || inColor == null || mvec == null || depth == null) return;
            if (frameReset) AdaptaCorte();   // 22-11 (1.2.4 DLSS5Q): la subida del % que esperaba un corte de escena (EngineAdapta.cs)
            IntPtr cl = Com.BorrowPtr(list, typeof(ID3D12GraphicsCommandList));
            if (!nrInited)
            {
                try
                {
                    if (!File.Exists(Path.Combine(s.NrDir, "nvngx_dlssnr.dll"))) { NrFail("no esta nvngx_dlssnr.dll en " + s.NrDir); return; }
                    if (PrismaNr.PNR_Init(Com.BorrowPtr(dev, typeof(ID3D12Device)), s.NrDir) == 0) { NrFail("PNR_Init: " + PrismaNr.LastError()); return; }
                }
                catch (Exception ex) { NrFail("PrismaNr.dll: " + ex.Message); return; }
                nrInited = true;
                Log.Write("DLSS 5 (DLSSNR) iniciado desde " + s.NrDir);
            }
            int pct = Math.Max(NrSuelo, Math.Min(100, s.NrPct));   // (1.3.2, FPS: antes el 25 % con cualquier entrada)
            int passes = Math.Max(1, Math.Min(3, s.NrPasses)), af = Math.Max(0, Math.Min(3, s.NrAntiFlicker));
            Dlss5Caps(ref passes, ref pct);   // 20-11 (CIERRES): lo que el guardian ha bajado aqui
            int w = pct >= 100 ? inW : Math.Max(64, (inW * pct / 100) & ~1), h = pct >= 100 ? inH : Math.Max(64, (inH * pct / 100) & ~1);
            bool rehacer = nrW != w || nrH != h || nrSrcW != inW || nrSrcH != inH || nrPasses != passes || nrAf != af || nrOuts[0] == null || !ReferenceEquals(nrInColor, inColor) || nrDirect != DirectBlendNow();   // (20-11 LATENCIA7: + dlss5_mezcla_directa)
            // 22-11 (1.2.4 DLSS5Q, dlss5_crear_aparte=1): las DLSSNR nuevas se crean en otro hilo; mientras, se sigue con las de antes
            if (rehacer && D5aPending(w, h, passes)) rehacer = false;
            else if (!rehacer && d5aState != 0) D5aCancel();   // (lo pedido volvio a lo de ahora: las nuevas sobran)
            if (rehacer)
            {
                // Crear (o recrear) todo a este tamaño: esta imagen va sin DLSS 5 (la creacion va en esta lista, la evaluacion en la siguiente)
                // 20-11 (CIERRES): antes de soltar nada, la grafica tiene que haber acabado las imagenes anteriores (FramesInFlight), que usan
                // las DLSSNR de antes: soltarlas con la GPU aun con ellas reinicio la grafica (nvlddmkm 153 a los 20 ms de pasar de 2 pasadas a 1,
                // 03:54:20; las texturas ya iban a la fosa, las DLSSNR no). Y las que siguen igual (mismo tamaño, menos o mas pasadas) se quedan
                long tw = Clock.Now;
                bool hadAny = nrW != 0;
                if (hadAny) WaitIdle();
                double waited = Clock.SinceMs(tw);
                bool sameSize = hadAny && nrW == w && nrH == h && nrSrcW == inW && nrSrcH == inH && ReferenceEquals(nrInColor, inColor);
                // 22-11 (1.2.4 DLSS5Q, dlss5_sosten=1: EngineDlss5Q.cs): esta imagen, con la correccion de DLSS 5 de la anterior en vez de sin
                // DLSS 5 (un destello con el color de la captura cada vez que la escala, el guardian o el menu cambian el % o las pasadas)
                bool swap = d5aState == 4 && d5aW == w && d5aH == h && d5aPasses == passes;   // (DLSS5Q: las creadas aparte, listas)
                bool held = !swap && hadAny && !(s.NrNow && s.NrSuave && !d5qFlushBroken) && D5qHoldRecord();   // (con la creacion inmediata no hace falta)
                d5qCreated = true;
                int keep = sameSize && !swap ? Math.Min(nrPasses, passes) : 0;
                foreach (ID3D12Resource r in new[] { nrOuts[0], nrOuts[1], nrOuts[2], nrBlend, nrIn, nrMv, nrDepth, afOut, afGuide, afHist[0], afHist[1] }) if (r != null) Bury(r);
                nrOuts[0] = nrOuts[1] = nrOuts[2] = nrBlend = nrIn = nrMv = nrDepth = afOut = afGuide = afHist[0] = afHist[1] = null;
                for (int i = keep; i < 3; i++) PrismaNr.PNR_ReleaseN(nrBank + i);
                if (swap) { nrBank = d5aBank; d5aState = 0; }
                else if (d5aState != 0) D5aCancel();
                bool small = w != inW || h != inH;
                for (int i = 0; i < passes; i++)
                    nrOuts[i] = Tex(ColorFmt, w, h, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "DLSS 5 (salida de la pasada " + (i + 1) + ")");
                bool direct = DirectBlendNow();   // 20-11 (LATENCIA7): sin nrBlend (la mezcla va a inColor)
                if (direct && psoNrBlendD == null)
                {
                    try { psoNrBlendD = Pso(CompileCs("nrblenddirect", NrBlendDirectShader)); }
                    catch (Exception ex) { nrDirectBroken = true; direct = false; Log.Write("DLSS 5: mezcla directa no disponible (" + ex.Message + "): con la copia de antes"); }
                }
                if (!direct) nrBlend = Tex(ColorFmt, inW, inH, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "DLSS 5 (con la fuerza)");
                if (small)
                {
                    nrIn = Tex(ColorFmt, w, h, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "DLSS 5 (entrada reducida)");
                    nrMv = Tex(DXGI_FORMAT.R16G16_FLOAT, w, h, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "DLSS 5 (vectores reducidos)");
                    nrDepth = Tex(DXGI_FORMAT.R32_FLOAT, w, h, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "DLSS 5 (profundidad reducida)");
                    SrvTex(TNrDown, 0, inColor, ColorFmt); UavTex(TNrDown, U0, nrIn, ColorFmt);
                    SrvTex(TNrPrep, 0, mvec, DXGI_FORMAT.R16G16_FLOAT); SrvTex(TNrPrep, 1, depth, DXGI_FORMAT.R32_FLOAT);
                    UavTex(TNrPrep, U0, nrMv, DXGI_FORMAT.R16G16_FLOAT); UavTex(TNrPrep, U0 + 1, nrDepth, DXGI_FORMAT.R32_FLOAT);
                    if (psoNrPrep == null) psoNrPrep = Pso(CompileCs("nrprep", NrPrepShader));
                }
                ID3D12Resource src = small ? nrIn : inColor, rawC = nrOuts[passes - 1], mv = small ? nrMv : mvec;
                if (af > 0)
                {
                    afOut = Tex(ColorFmt, w, h, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "DLSS 5 (antiparpadeo)");
                    afGuide = Tex(ColorFmt, w, h, false, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "DLSS 5 (antiparpadeo: entrada anterior)");
                    for (int i = 0; i < 2; i++)
                        afHist[i] = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, w, h, true, i == 0 ? D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE : D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "DLSS 5 (antiparpadeo: historia " + i + ")");
                    for (int t = 0; t < 2; t++)
                    {
                        int tab = t == 0 ? TNrAf0 : TNrAf1;
                        SrvTex(tab, 0, src, ColorFmt); SrvTex(tab, 1, rawC, ColorFmt);
                        SrvTex(tab, 2, afHist[t], DXGI_FORMAT.R16G16B16A16_FLOAT); SrvTex(tab, 3, afGuide, ColorFmt);
                        SrvTex(tab, 4, mv, DXGI_FORMAT.R16G16_FLOAT);
                        UavTex(tab, U0, afOut, ColorFmt); UavTex(tab, U0 + 1, afHist[1 - t], DXGI_FORMAT.R16G16B16A16_FLOAT);
                    }
                    if (psoNrAf == null) psoNrAf = Pso(CompileCs("nrantiparpadeo", NrAntiFlickerShader));
                    afPing = 0; afLastTick = 0;
                }
                if (direct) { SrvTex(TNr, 0, rawC, ColorFmt); SrvTex(TNr, 1, af > 0 ? afOut : rawC, ColorFmt); SrvTex(TNr, 2, small ? src : rawC, ColorFmt); UavTex(TNr, U0, inColor, ColorFmt); }   // (20-11 LATENCIA7: inColor solo como UAV)
                else { SrvTex(TNr, 0, inColor, ColorFmt); SrvTex(TNr, 1, af > 0 ? afOut : rawC, ColorFmt); SrvTex(TNr, 2, src, ColorFmt); UavTex(TNr, U0, nrBlend, ColorFmt); }
                if (psoNrBlend == null) psoNrBlend = Pso(CompileCs("nrblend2", NrBlendShader));
                nrDirect = direct;
                long tc = Clock.Now;   // (22-11 DLSS5Q: lo que tarda crear, en el log: es el paron de cada cambio de % o de pasadas)
                for (int i = keep; i < passes && !swap; i++)
                    if (PrismaNr.PNR_CreateN(nrBank + i, cl, (uint)w, (uint)h, s.NrPreset) == 0) { nrW = 0; NrFail("PNR_Create " + w + "x" + h + " (pasada " + (i + 1) + "): " + PrismaNr.LastError()); return; }
                double createMs = swap ? d5aCreateMs : Clock.SinceMs(tc);
                nrW = w; nrH = h; nrSrcW = inW; nrSrcH = inH; nrPasses = passes; nrAf = af; nrReset = true; nrInColor = inColor;
                nrCreatedAt = Clock.Now; nrOver = nrOverHard = 0;
                Log.Write("DLSS 5 (DLSSNR) creado a " + w + "x" + h + (small ? " (" + pct + " % de " + inW + "x" + inH + ")" : "") + ", " + passes + " pasada(s)" +
                          (passes > 1 ? " (fuerzas " + s.NrPassStrength[0] + "," + s.NrPassStrength[1] + "," + s.NrPassStrength[2] + ")" : "") +
                          ", antiparpadeo " + af + (af > 0 ? " (" + s.NrAntiFlickerMs + " ms)" : "") + " (modelo " + s.NrPreset +
                          ", estilo " + s.NrStyle + ", nivel " + s.NrIntensity + "," + s.NrTone + "," + s.NrStructure + "," + s.NrSkin + ", fuerza " + s.NrStrength + ")" +
                          (hadAny ? string.Format(IC, "; antes, la grafica acabando lo de antes: {0:0} ms{1}", waited, keep > 0 ? ", " + keep + " reutilizada(s)" : "") : "") +
                          (held ? "; esta imagen con la correccion de la anterior (sosten)" : "") +
                          string.Format(IC, swap ? "; creadas aparte en {0:0} ms ({1} instancia(s)) sin parar la imagen; el cambio, en esta: {2:0} ms" : "; crear {0:0} ms ({1} instancia(s))",
                                        createMs, passes - keep, waited));
                // 22-11 (1.2.4 DLSS5Q, dlss5_creacion_inmediata): la creacion a la grafica ya y esta misma imagen con DLSS 5 (EngineDlss5Q.cs)
                if (swap) { }   // (creadas aparte: su lista ya se hizo en la grafica, esta imagen ya se evalua con ellas)
                else if (held || !D5qFlush()) return;
                else
                Log.Write(string.Format(IC, "DLSS 5: creacion enviada y hecha en {0:0} ms; esta imagen ya lleva DLSS 5", d5qFlushMs));
                cl = Com.BorrowPtr(list, typeof(ID3D12GraphicsCommandList));
            }
            Dlss5GuardTick();   // 20-11 (CIERRES)
            uint* c = stackalloc uint[8];
            bool reduced = nrIn != null;
            if (reduced)
            {
                // La captura, los vectores y la profundidad, reducidos al tamaño de DLSS 5
                Barrier(nrIn, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                c[0] = (uint)nrW; c[1] = (uint)nrH; c[2] = 0; c[3] = 0;
                c[4] = F(1f / inW); c[5] = F(1f / inH); c[6] = F(inW); c[7] = F(inH);
                BindCompute(psoScale, TNrDown, c, 8);
                list.Dispatch(Groups(nrW, 16), Groups(nrH, 8), 1);
                Barrier(nrIn, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                Barrier(nrMv, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                Barrier(nrDepth, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                c[0] = (uint)nrW; c[1] = (uint)nrH; c[2] = (uint)inW; c[3] = (uint)inH; c[4] = F((float)nrW / inW); c[5] = 0; c[6] = 0; c[7] = 0;
                BindCompute(psoNrPrep, TNrPrep, c, 8);
                list.Dispatch(Groups(nrW, 16), Groups(nrH, 8), 1);
                Barrier(nrMv, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                Barrier(nrDepth, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            }
            ID3D12Resource srcIn = reduced ? nrIn : inColor, mvIn = reduced ? nrMv : mvec, dpIn = reduced ? nrDepth : depth;
            bool reset = nrReset || frameReset;   // (1.0.4) resetHistory is already false here (Run copies it to frameReset before RenderFrame): DLSS 5 never saw scene cuts
            // Las pasadas: cada una sobre la salida de la anterior, con su fuerza
            for (int i = 0; i < nrPasses; i++)
            {
                ID3D12Resource pin = i == 0 ? srcIn : nrOuts[i - 1];
                if (i > 0)
                {
                    UavBarrier(nrOuts[i - 1]);
                    Barrier(nrOuts[i - 1], D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                }
                float k = nrPasses > 1 ? s.NrPassStrength[i] : 1f;
                int ok = PrismaNr.PNR_EvaluateN(nrBank + i, cl, Com.BorrowPtr(pin, typeof(ID3D12Resource)), Com.BorrowPtr(nrOuts[i], typeof(ID3D12Resource)),
                    Com.BorrowPtr(mvIn, typeof(ID3D12Resource)), Com.BorrowPtr(dpIn, typeof(ID3D12Resource)), (uint)nrW, (uint)nrH, 1f, 1f,
                    s.NrStyle, Math.Min(2f, s.NrIntensity * k), Math.Min(2f, s.NrTone * k), Math.Min(2f, s.NrStructure * k), Math.Min(2f, s.NrSkin * k),
                    s.NrMask ? 1 : 0, s.NrUi ? 1 : 0, reset ? 1 : 0);
                if (i > 0) Barrier(nrOuts[i - 1], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                if (ok == 0)
                {
                    if (++nrFails >= 8) NrFail("PNR_Evaluate (pasada " + (i + 1) + "): " + PrismaNr.LastError());
                    return;
                }
            }
            nrFails = 0; nrReset = false; nrFrames++;
            d5qApplied = true; d5qReset = reset;   // (22-11 DLSS5Q, dlss5_medida=)
            ID3D12Resource raw = nrOuts[nrPasses - 1];
            UavBarrier(raw);
            Barrier(raw, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            // Antiparpadeo sobre lo que cambia DLSS 5 (a su tamaño)
            if (nrAf > 0)
            {
                long now = nrClock.ElapsedTicks;
                double dt = afLastTick == 0 ? 1 : (now - afLastTick) / (double)Stopwatch.Frequency;
                afLastTick = now;
                float weight = reset || dt > 0.25 ? 0f : (float)Math.Exp(-dt / (Math.Max(10, s.NrAntiFlickerMs) * 0.001));
                d5qWeight = weight;   // (DLSS5Q)
                ID3D12Resource readH = afHist[afPing], writeH = afHist[1 - afPing];
                c[0] = (uint)nrW; c[1] = (uint)nrH; c[2] = nrAf >= 2 ? 1u : 0u; c[3] = (uint)nrAf; c[4] = F(weight); c[5] = 0; c[6] = 0; c[7] = 0;
                BindCompute(psoNrAf, afPing == 0 ? TNrAf0 : TNrAf1, c, 8);
                list.Dispatch(Groups(nrW, 16), Groups(nrH, 8), 1);
                UavBarrier(afOut); UavBarrier(writeH);
                // La historia nueva pasa a leerse la proxima vez; la vieja, a escribirse
                Barrier(writeH, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                Barrier(readH, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                afPing = 1 - afPing;
                // La entrada de esta imagen es la guia de la siguiente
                Barrier(afGuide, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_DEST);
                Barrier(srcIn, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_SOURCE);
                list.CopyResource(afGuide, srcIn);
                Barrier(srcIn, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                Barrier(afGuide, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                Barrier(afOut, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            }
            // El resultado vuelve a inColor (lo que leen DLSS SR / el escalado / CAS / la generacion), con la fuerza y ampliado si hace falta
            if (nrDirect)
            {
                // 20-11 (LATENCIA7, dlss5_mezcla_directa=1): directamente en inColor, sin nrBlend ni la copia
                c[0] = (uint)inW; c[1] = (uint)inH; c[2] = reduced ? 1u : 0u; c[3] = 0; c[4] = F(s.NrStrength); c[5] = F(NrChromaNow); c[6] = 0; c[7] = 0;
                Barrier(inColor, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                BindCompute(psoNrBlendD, TNr, c, 8);
                list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
                Barrier(inColor, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                if (nrAf > 0) Barrier(afOut, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                Barrier(raw, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                return;
            }
            c[0] = (uint)inW; c[1] = (uint)inH; c[2] = 0; c[3] = 0; c[4] = F(s.NrStrength); c[5] = F(NrChromaNow); c[6] = 0; c[7] = 0;
            BindCompute(psoNrBlend, TNr, c, 8);
            list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            UavBarrier(nrBlend);
            if (nrAf > 0) Barrier(afOut, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            Barrier(raw, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            Barrier(nrBlend, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.COPY_SOURCE);
            Barrier(inColor, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_DEST);
            list.CopyResource(inColor, nrBlend);
            Barrier(inColor, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(nrBlend, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
        }

        void NrFail(string why)
        {
            nrBroken = true;
            Log.Write("DLSS 5 (DLSSNR) no disponible: " + why + " (se sigue sin DLSS 5)");
            // 22-11 (1.2.3, MOTOR5): al lanzador, que lo pasa a Magpie (src\Lanzador\MotorDlss5.cs); uno de antes no lo escucha
            try { Ev("switch2-dlss5", "roto", why.Replace("\"", "'").Replace("\\", "/")); } catch { }
        }

        void DisposeDlss5()
        {
            D5qClose();   // (22-11 DLSS5Q, dlss5_medida=)
            D5aCancel();
            try { if (nrInited) PrismaNr.PNR_Shutdown(); } catch { }
            nrInited = false;
        }
    }
}
