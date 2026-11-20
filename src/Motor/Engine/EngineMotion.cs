using System;
using System.Globalization;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // Vectores de movimiento propios (sin el flujo optico de NVIDIA, que no tiene cabeceras de D3D12 a mano): piramide de luma de
    // 4 niveles de la imagen actual y la anterior, busqueda por bloques de 8x8 de lo grueso a lo fino (nivel 3: +-12 px = +-96 px a
    // resolucion de captura; luego +-2 / +-2 / +-1 y medio pixel por parabola) y vectores por pixel interpolados. DLSS SR y la
    // generacion los usan para seguir lo que se mueve (con vectores a 0, los modelos transformer emborronan las texturas al moverse)
    //
    // 16-11 (calidad, agente E2): todo apagado por defecto, cada cosa con su ajuste (motor_extra=):
    //   mvflat=1   bloques lisos del nivel fino: el candidato que mejor explica una ventana de 16x16 alrededor (antes siempre 0:
    //              en un paneo el cesped, el cielo o la niebla poco texturados se quedaban con la historia sin mover)
    //   mvpx=1     en los bloques junto a otro movimiento, cada punto elige su vector (el suyo, el de los vecinos o 0) por su 3x3:
    //              las letras quietas de un panel por el que se ve el fondo moverse no se arrastran con el fondo
    //   mvmask=N   mascara "sin historia" para DLSS SR (Streamline BiasCurrentColorHint, R8 a la resolucion de entrada, 1 = sin
    //              historia), suma de: 1 donde el vector no explica el bloque, 2 en el borde entre lo quieto y lo que se mueve
    //              (texto/HUD), 4 en lo que lleva quieto mvmask_quieto imagenes
    //   mvsesgo / mvruido   preferencia por el vector 0 frente al ruido de la captura (relativa, y en niveles por punto): sin esto, en
    //              lo oscuro casi quieto (cueva de Ocarina) un 29 % de bloques cogia vectores de ruido ("hervido" con los transformer)
    //   fgui=1|2|3 la generacion (DLSS-G) no deforma lo quieto con borde (texto, HUD) mientras la escena se mueve: 1 = UIAlpha
    //              (alfa de interfaz, R8 a 4K), 2 = UIColorAndAlpha (color premultiplicado + alfa), 3 = NoWarpMask (sin documentar)
    //   mvexacto=0.1  sin medio pixel cuando el entero ya casa (la parabola se inventaba 0,1-0,3 px en desplazamientos enteros)
    // Medido el 16-11 con K ("borde" = cuanto cambia el anillo de 4 px del texto/HUD quieto entre copias de la tele; "resto" = lo que
    // cambia el fondo que se desplaza tras alinear las copias; GPU de la seccion de vectores, RTX 4060):
    //   mvpx: menu de FC 26 (30, 1080p) borde 0,46 -> 0,30 (gt8 0,77 -> 0,17 %), resto 1,32 -> 1,00; HUD de Ocarina (60, 1440p)
    //         0,385 -> 0,301; con L/M el borde es del modelo (no cambia) pero el resto si (L 0,96 -> 0,65, M 0,92 -> 0,74).
    //         +0,25 ms a 1080p, +0,45 ms a 1440p. Es lo que arregla las letras arrastradas con el fondo.
    //   mvexacto=0.1 (con mvpx): resto FC 1,00 -> 0,61, Ocarina 2,74 -> 2,32, lista que se desplaza 0,49 -> 0,36; coste 0.
    //   mvsesgo=0.25 mvruido=1: par real de Ocarina oscuro, bloques con vector (ruido) 21 % -> 1 %; la salida con la misma entrada
    //         0,019 -> 0,007 y su reaccion al ruido de la captura 0,74 -> 0,38 (la mitad de "hervido"); en paneos ayuda un poco
    //         (resto FC 1,00 -> 0,89). Coste 0.
    //   mvmask (pista): K no la usa (con la mascara entera a 1 la imagen sale identica bit a bit; la guia de DLSS 310.4+, seccion
    //         3.15: solo el modelo F). mvmask_modo=1 (vectores fuera de la imagen) si quita la historia, pero el texto empeora mucho
    //         (K 0,47 -> 2,36; L 2,35 -> 7,1; menu quieto L 0,90 -> 6,6, K 0,007 -> 1,37): no usar.
    //   mvflat: quita el 0 a un 10 % de bloques del paneo pero sin mejora medible en la imagen (FC 1,28; Ocarina 2,72 -> 2,52); +0,1-0,2 ms.
    //   fgui=1/2 (con o sin recomposicion): sin cambio medible en ninguna prueba (con K la generacion no deforma el texto: borde con
    //         y sin generacion igual); +0,2-0,25 ms. fgui=3 (NoWarpMask) sin probar.
    //
    // 17-11 (REJAS): la reja de Ocarina (barrotes oscuros ondulados delante del cielo) dejaba rastro y temblaba con DLSS 4.5 (L/M) al mover
    // la camara. Prueba nueva con verdad de referencia: test=paralaje (Parallax.cs: reja y cielo, cada uno a su velocidad, compuestos a
    // 4K), volcado= (la salida y los vectores a ficheros) y tests\rejas_mide (mide contra la verdad; temblor = cuanto cambia el error del
    // borde del barrote de una imagen a otra siguiendo al barrote). Causa (medido): los vectores de los barrotes estaban mal en el 34-72 %
    // de sus puntos: (1) apertura: un barrote vertical no dice nada de su movimiento vertical y cada bloque cogia otra altura (1-5 px):
    // DLSS traia la historia de otro trozo del barrote ondulado; (2) el borde del barrote y el cielo que destapa llevaban 0 o el vector del
    // cielo (donde en la anterior estaba el barrote): rastro oscuro y borde que baila. Con los vectores de verdad tal cual, peor (temblor
    // 2,25 frente a 1,33); con los de verdad y la reja "ensanchada" (lo que hace un juego con su profundidad) 0,30: hace falta el vector del
    // barrote en su borde y en lo que destapa. La profundidad por capas no cambia nada en DLSS SR (0,30 / 0,33). Arreglo (motor_extra):
    //   mvconsenso=2   por bloque, el vector suyo o de un vecino de 5x5 que menos cuesta (lo que queda - 2 niveles x los vecinos que lo llevan)
    //   mvconsenso_eje=1  y otro candidato: la vertical de su capa (media recortada de los vecinos con su misma horizontal) con la
    //                  horizontal propia y +-0,5 px: con media punto en horizontal, el barrote ondulado casaba mejor con 3-5 px de vertical
    //                  falsa (el ondulado desplazado imita la fraccion) que con la buena a un punto entero (L 1,26 -> 1,01, M 1,24 -> 1,02)
    //   mvapertura=16  cada componente, media de la de su capa a <= 16 bloques (uno de cada 2 pasado 8) pesada por su fiabilidad (curvatura): la vertical del barrote
    //                  la ponen los travesaños y la mayoria, no el ruido de cada bloque (sin el: L a 1440p 1,20 -> 1,49; 8 -> 16: L 1,01 -> 0,96)
    //   mvtemporal=1   candidato temporal en la busqueda (+-2 px como mucho): el vector no salta entre el barrote y el de al lado
    //   mvcapas=1      por punto (en vez de mvpx, y mas barato): ademas de lo de mvpx, el cielo liso junto a un bloque con textura coge su
    //                  vector si lo explica igual: el barrote ensanchado
    //   mvsuave=0      (antes 1,5) la media de 3x3 bloques mezclaba fracciones distintas: con lo de arriba, peor (L 1,29 sin / 1,46 con)
    //   mvlargo=0.004  (antes 0.002) con mvlargo=0 el temblor sube a 2,49
    //   corte_medias=1 el corte de escena con medias por casilla: un paneo rapido de la reja (16 px por imagen a 1080p) era un "corte" en
    //                  cada imagen (DLSS y la generacion sin historia, vectores a 0). Un corte de verdad (dos escenas) igual: 31 (puntos 33)
    // Medido (paralaje, movimiento con fracciones de punto, 60 imagenes, temblor antes -> despues; verdad ensanchada 0,46):
    //   1080p: L 2,23 -> 0,96   M 2,21 -> 0,97   K 3,30 -> 0,85   E 2,65 -> 1,32;  1440p reducida a 1080p, M 2,58 -> 1,16;
    //   1440p, L 2,32 -> 0,99; cielo a 0,4 de la reja 2,29 -> 0,96; el doble de rapido 2,88 -> 1,11 (movimiento entero: L 1,45 -> 0,33)
    //   Texto quieto del menu de FC 26 sobre el partido (cambio entre imagenes): L 0,208 -> 0,204, K 0,269 -> 0,269, E 0,080 -> 0,080,
    //   M 0,232 -> 0,252 (mvconsenso_quieto=1 lo deja mejor pero la reja peor); fondo que se desplaza: L, K y M igual
    //   Coste (seccion de vectores): 1080p 1,60 -> 1,97 ms, 1440p reducida a 1080p 1,88 -> 2,20, 1440p 2,54 -> 2,9-3,1 ms
    //   Probado y peor: media por componente con su propia puerta, moda, mvapertura_fuera, mvtemporal_mezcla; sin ganancia: candidatos
    //   del nivel grueso con la vertical a 0 (quitado); profundidad por capas (mvverdad): DLSS SR igual
    unsafe partial class Engine
    {
        const int Levels = 4;
        readonly ID3D12Resource[,] luma = new ID3D12Resource[2, Levels];
        readonly ID3D12Resource[] mvLevel = new ID3D12Resource[Levels];
        readonly ID3D12Resource[] mvPrev = new ID3D12Resource[Levels];   // 17-11 (REJAS, mvtemporal): el campo de cada nivel de la imagen anterior
        bool mvPrevValid;
        readonly int[] lw = new int[Levels], lh = new int[Levels], bx = new int[Levels], by = new int[Levels];
        int motionSet;              // juego de luma de la imagen actual (el otro es la anterior)
        bool motionValid;           // hay imagen anterior
        bool mvecDirty;             // mvec tiene vectores (hay que ponerlo a 0 si se apaga)
        public double MotionGpuHint;

        // ---- 16-11 (calidad): tablas propias detras de las del motor (Engine.cs: TablesCount = TQBase + TQCount)
        const int TQBase = 25, TQBlock = TQBase, TQExpand = TQBase + 1 /* + juego de luma */, TQUi = TQBase + 3 /* + juego */, TQUiOut = TQBase + 5, TQMvSr = TQBase + 6, TQCount = 7;
        ID3D12PipelineState psoMvBlock, psoMvExpandQ, psoUiMask, psoUiOut, psoMvSr;
        // mvmask_modo=1: la mascara va en los vectores que ve DLSS SR (fuera de la imagen = sin historia, en cualquier modelo; la
        // pista BiasCurrentColorHint solo la usa el modelo F segun la guia de DLSS 310.4+), DLSS-G sigue con los de verdad
        ID3D12Resource mvecSr;
        bool srVecValid;
        readonly SlTagSet localTags = new SlTagSet(2);
        ID3D12Resource blockInfo;   // por bloque del nivel fino: x = lo que queda tras mover (media por punto), y = textura, z = sin mover, w = liso
        ID3D12Resource blockMask;   // por bloque: r = vector dudoso, g = borde quieto/movimiento, b = quieto N imagenes
        ID3D12Resource blockStill;  // R32 por bloque: imagenes seguidas sin cambiar (para b)
        ID3D12Resource biasMask;    // R8 a la resolucion de entrada: 1 = DLSS sin historia
        ID3D12Resource uiMaskIn;    // R8 a la resolucion de entrada: 1 = interfaz quieta (para la generacion)
        ID3D12Resource stillCount;  // R32 por punto: imagenes seguidas sin cambiar
        ID3D12Resource uiAlpha, uiColor;   // a la resolucion de salida (lo que se etiqueta para DLSS-G)
        bool biasValid;             // la mascara de DLSS es de esta imagen
        bool uiValid;               // la de interfaz es de la imagen que se pinta
        int fgUiApplied;            // fgui/fgui_recomp con los que se configuro la generacion

        bool QualityMotion { get { return s.MvMask > 0 || MvPxNow || s.FgUi > 0 || s.MvCapas; } }   // (17-11, REJAS: mvcapas usa blockInfo)

        // 16-11 (E, tirones a 1440p): mvpx solo por debajo de 1440p cuando hay generacion en el modo 60 (mvpx_auto=1, de serie). Medido a
        // 1440p con K: los vectores 2,37 ms con mvpx y 1,26 sin (+1,1 ms, no los 0,45 de arriba), y con la generacion de NVIDIA no cabia
        // (54 imagenes reales, 13 % dobles en la tele). mvpx_auto=0: mvpx como diga mvpx= siempre
        bool MvPxNow { get { return s.MvPx && !MvLightNow && !(s.MvPxAuto && fgApplied && s.Mode == 60 && (long)inW * inH >= 2560L * 1440 * 9 / 10) && !MvSrNoPx; } }   // (20-11 SR45: MvSrNoPx)
        // 17-11 17:35 (mv_ligero=N puntos): con la entrada de N puntos o mas (1440p entera) sin mvpx, mvflat, mvconsenso ni mvapertura (~0,8 ms):
        // asi cabe DLSS 4.5 con la captura de 1440p sin reducir (el usuario: "si pongo captura 1440p quiero que capture a 1440p")
        // 20-11 16:05 (DLSS5VEC): con DLSS 5 en el modo 30, completos (mv_ligero_dlss5=0, de serie): hay ~17 ms libres de los 33
        bool MvLightNow { get { return MvLightSize && (mvForceLight || !MvFullWanted); } }
        bool MvLightSize { get { return s.MvLightPx > 0 && (long)inW * inH >= s.MvLightPx; } }
        bool MvFullForDlss5 { get { return s.Nr && s.Mode == 30 && !s.MvLightDlss5; } }
        // 20-11 (SR45): DLSS SR al reescalar (DLSS 4.5 L/M de 1440p a 4K) tambien con los completos en los modos de mv_completo_sr: con los
        // ligeros el borde de lo que se mueve temblaba al mover la camara (paralaje 1440p -> 4K, temblor ligeros / completos: L 2,52 / 0,97,
        // M 2,46 / 1,03, K 3,27 / 0,88; 1080p, que siempre los tuvo, L 0,92). La generacion sigue con los ligeros de siempre (la doble pasada
        // de DLSS5VEC2/3 con fg_vectores=2; con fg_vectores=1 lee mvec y se quedan ligeros, como antes). Si no caben (AutoPresetTick ->
        // MvSrFallback), vuelven a ser ligeros para ese modo y modelo
        bool MvFullWanted { get { return MvFullForDlss5 || MvFullForSr; } }
        bool MvFullForSr { get { return (s.MvFullSr & MvSrModeBit) != 0 && s.Upscaler == "dlss" && dlssOk && !dlssBroken && (inW != outW || inH != outH) && !(fgApplied && s.FgVec == 1) && !mvSrLight.Contains(MvSrKey); } }
        int MvSrModeBit { get { return s.Is120 ? 16 : (s.Mode == 30 ? 1 : 4) << (fgApplied ? 1 : 0); } }   // (Common.cs MvModes: 30 30fg 60 60fg 120)
        string MvSrKey { get { return CapKey + "|" + s.Preset; } }
        readonly System.Collections.Generic.HashSet<string> mvSrLight = new System.Collections.Generic.HashSet<string>();
        int mvSrSaid = -1;          // lo ultimo dicho al registro (0 nada, 1 ligeros, 2 completos)
        double qMovLast;            // vectores y mascaras, ms de GPU (media del ultimo segundo)

        // 20-11 (SR45): AutoPresetTick, antes de bajar el modelo. Con los completos solo por DLSS SR (no por DLSS 5): si quitarlos basta (lo que
        // pasa del tope <= lo que ahorran, ~45 % de los vectores: 1440p 2,00 -> 1,16 ms) o con la generacion se pierden imagenes, vuelven los
        // ligeros para este modo y modelo; si no basta baja el modelo como siempre (y el siguiente prueba con los completos)
        bool MvSrFallback(double p95, double limit, bool lostFrames)
        {
            if (!s.MvSrGuard || !MvLightSize || !MvFullForSr || MvFullForDlss5) return false;
            if (!lostFrames && p95 - limit > 0.45 * qMovLast + 0.1) return false;
            mvSrLight.Add(MvSrKey);
            Log.Write(string.Format(CultureInfo.InvariantCulture, "Vectores de DLSS SR: los completos no caben aqui (grafica p95 {0:0.0} ms, tope {1:0.0}, vectores {2:0.00} ms{3}): ligeros con {4} en este modo",
                p95, limit, qMovLast, lostFrames ? "; con la generacion se pierden imagenes" : "", s.Preset));
            return true;
        }

        // 20-11 (SR45): al registro, cuando cambian, los vectores que recibe DLSS SR con la entrada de mv_ligero o mas
        void MvSrSay()
        {
            int st = s.Upscaler != "dlss" || inW <= 0 ? 0 : (inW == outW && inH == outH) ? 3 : !MvLightSize ? 0 : MvFullWanted ? 2 : 1;
            if (st == mvSrSaid) return;
            mvSrSaid = st;
            // (con la entrada igual a la salida, 4K a 4K, DLSS SR no se usa: el "dlss L" de "Ajustes:" es solo lo pedido)
            if (st == 3) Log.Write("DLSS SR: entrada = salida (" + inW + "x" + inH + "): no se usa (el modelo " + s.Preset + " de Ajustes no se aplica; solo DLSS 5 y la generacion)");
            else if (st > 0) Log.Write("Vectores de DLSS SR (" + inW + "x" + inH + ", modo " + s.Mode + (fgApplied ? " + generacion" : "") + ", " + s.Preset + "): " +
                (st == 2 ? "completos" + (MvFullForDlss5 ? " (DLSS 5)" : " (mv_completo_sr)") + (MvSrNoPx ? " sin mvpx" : "") + (fgApplied && s.FgVec >= 2 ? "; la generacion, los ligeros" : "") : "ligeros"));
        }

        // 20-11 (SR45): mv_completo_sr_px=L (de serie): en los completos solo por DLSS SR, mvpx solo con esos modelos. Paralaje 1440p, con /
        // sin mvpx (temblor, total, detras = rastro): L 0,97 0,70 0,94 / 1,15 0,72 0,91 (mvpx mejor); K 0,88 0,99 1,61 / 0,98 0,92 1,26 y
        // M 1,03 0,72 1,27 / 1,20 0,68 1,01 (con mvpx el cielo que destapa la reja se oscurece: mas rastro). Sin mvpx ~0,3 ms menos
        bool MvSrNoPx { get { return MvLightSize && !MvFullForDlss5 && MvFullForSr && s.MvSrPx != "TODOS" && s.MvSrPx.IndexOf(s.Preset, StringComparison.Ordinal) < 0; } }
        // 20-11 16:15 (DLSS5VEC2): con DLSS 5 + DLSS-G a la vez la generacion con los completos daba errores (por separado bien): la generacion
        // sigue con los ligeros de siempre (primera pasada del nivel fino sin mvflat, sin consenso, expandidos sin mvpx -> mvecFg) y luego se
        // rehace el nivel fino completo solo para DLSS 5 (mvec). mvForceLight = en la primera pasada
        bool mvForceLight;
        bool fgVecDone;             // RecordFgVec ya hecho en esta imagen (con los ligeros)
        // 20-11 (FGFALLOS) fg_vectores_completos=1: en ese mismo caso (DLSS 5, modo 30, entrada >= mv_ligero) DLSS-G recibe los vectores por
        // punto COMPLETOS del motor (mvec, los de DLSS 5) en vez de los ligeros suavizados, y sin la doble pasada; =2: tambien donde el motor
        // ya los hace completos (mvpx: 1080p, 1440p reducida). Medido (ag_FGFALLOS\tools\fgmide: paralaje con interfaz quieta, la inventada
        // contra la interpolacion ideal de las dos reales vistas, en los mismos instantes): DLSS-G casi no usa unos vectores suaves (ligeros
        // suavizados = a cero = uno global) y si los del punto: 4K + DLSS 5 -35 % (interfaz -73 %, borde -53 %, dentro de la reja peor
        // 0,42 -> 0,90), 1440p + DLSS 5 -28 %, 1080p -24 %, con ruido -22 %; con los LIGEROS por punto, peor (+25 %): por eso solo con los
        // completos. Necesita fg_profundidad (sin ella solo -8 %). GPU -1,2 ms a 4K (sin la doble pasada)
        bool FgVecFullNow { get { return fgApplied && ((s.FgVecFull >= 1 && MvLightSize && MvFullForDlss5) || (s.FgVecFull >= 2 && MvPxNow)); } }

        void CreateMotionResources(int w, int h)
        {
            for (int k = 0; k < Levels; k++)
            {
                lw[k] = k == 0 ? w : (lw[k - 1] + 1) / 2;
                lh[k] = k == 0 ? h : (lh[k - 1] + 1) / 2;
                bx[k] = (lw[k] + 7) / 8; by[k] = (lh[k] + 7) / 8;
                for (int st = 0; st < 2; st++)
                {
                    if (luma[st, k] != null) { ID3D12Resource r = luma[st, k]; Com.Release(ref r); }
                    luma[st, k] = Tex(DXGI_FORMAT.R8_UNORM, lw[k], lh[k], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "luma " + st + "." + k);
                }
                if (mvLevel[k] != null) Com.Release(ref mvLevel[k]);
                mvLevel[k] = Tex(DXGI_FORMAT.R16G16_FLOAT, bx[k], by[k], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "bloques " + k);
                if (mvPrev[k] != null) Com.Release(ref mvPrev[k]);
                mvPrev[k] = Tex(DXGI_FORMAT.R16G16_FLOAT, bx[k], by[k], false, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "bloques anteriores " + k);
            }
            if (mvLight0 != null) Com.Release(ref mvLight0);
            mvLight0 = Tex(DXGI_FORMAT.R16G16_FLOAT, bx[0], by[0], false, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "bloques ligeros (generacion)");
            // 16-11 (calidad): lo de las mascaras (pequeño: se crea siempre, para poder encenderlo en marcha)
            if (blockInfo != null) Com.Release(ref blockInfo);
            if (blockMask != null) Com.Release(ref blockMask);
            if (biasMask != null) Com.Release(ref biasMask);
            if (uiMaskIn != null) Com.Release(ref uiMaskIn);
            if (stillCount != null) Com.Release(ref stillCount);
            if (blockStill != null) Com.Release(ref blockStill);
            if (mvecSr != null) Com.Release(ref mvecSr);
            mvecSr = Tex(DXGI_FORMAT.R16G16_FLOAT, w, h, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "movimiento para DLSS SR");
            srVecValid = false;
            blockInfo = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, bx[0], by[0], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "bloques info");
            blockMask = Tex(DXGI_FORMAT.R8G8B8A8_UNORM, bx[0], by[0], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "bloques mascara");
            blockStill = Tex(DXGI_FORMAT.R32_UINT, bx[0], by[0], true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "bloques quietos");
            biasMask = Tex(DXGI_FORMAT.R8_UNORM, w, h, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "mascara sin historia");
            uiMaskIn = Tex(DXGI_FORMAT.R8_UNORM, w, h, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "interfaz quieta");
            stillCount = Tex(DXGI_FORMAT.R32_UINT, w, h, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "imagenes quieto");
            biasValid = uiValid = false;

            for (int st = 0; st < 2; st++)
                for (int k = 0; k < Levels; k++)
                {
                    int tl = TLuma + st * Levels + k, ts = TSearch + st * Levels + k;
                    NullViews(tl); NullViews(ts);
                    if (k == 0) SrvTex(tl, 0, inColor, ColorFmt);
                    else SrvTex(tl, 1, luma[st, k - 1], DXGI_FORMAT.R8_UNORM);
                    UavTex(tl, U0, luma[st, k], DXGI_FORMAT.R8_UNORM);
                    SrvTex(ts, 0, luma[st, k], DXGI_FORMAT.R8_UNORM);
                    SrvTex(ts, 1, luma[1 - st, k], DXGI_FORMAT.R8_UNORM);
                    if (k < Levels - 1) SrvTex(ts, 2, mvLevel[k + 1], DXGI_FORMAT.R16G16_FLOAT);
                    SrvTex(ts, 3, mvPrev[k], DXGI_FORMAT.R16G16_FLOAT);   // 17-11 (REJAS)
                    UavTex(ts, U0, mvLevel[k], DXGI_FORMAT.R16G16_FLOAT);
                    if (k == 0) UavTex(ts, U0 + 1, blockInfo, DXGI_FORMAT.R16G16B16A16_FLOAT);
                }
            NullViews(TExpand);
            SrvTex(TExpand, 0, mvLevel[0], DXGI_FORMAT.R16G16_FLOAT);
            UavTex(TExpand, U0, mvec, DXGI_FORMAT.R16G16_FLOAT);
            // 16-11 (calidad)
            NullViews(TQBlock);
            SrvTex(TQBlock, 0, mvLevel[0], DXGI_FORMAT.R16G16_FLOAT);
            SrvTex(TQBlock, 1, blockInfo, DXGI_FORMAT.R16G16B16A16_FLOAT);
            UavTex(TQBlock, U0, blockMask, DXGI_FORMAT.R8G8B8A8_UNORM);
            UavTex(TQBlock, U0 + 1, blockStill, DXGI_FORMAT.R32_UINT);
            for (int st = 0; st < 2; st++)
            {
                int te = TQExpand + st, tu = TQUi + st;
                NullViews(te); NullViews(tu);
                SrvTex(te, 0, mvLevel[0], DXGI_FORMAT.R16G16_FLOAT);
                SrvTex(te, 1, blockMask, DXGI_FORMAT.R8G8B8A8_UNORM);
                SrvTex(te, 2, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(te, 3, luma[1 - st, 0], DXGI_FORMAT.R8_UNORM);
                UavTex(te, U0, mvec, DXGI_FORMAT.R16G16_FLOAT);
                UavTex(te, U0 + 1, biasMask, DXGI_FORMAT.R8_UNORM);
                SrvTex(tu, 0, luma[st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(tu, 1, luma[1 - st, 0], DXGI_FORMAT.R8_UNORM);
                SrvTex(tu, 2, mvLevel[0], DXGI_FORMAT.R16G16_FLOAT);
                SrvTex(tu, 3, blockInfo, DXGI_FORMAT.R16G16B16A16_FLOAT);
                UavTex(tu, U0, uiMaskIn, DXGI_FORMAT.R8_UNORM);
                UavTex(tu, U0 + 1, stillCount, DXGI_FORMAT.R32_UINT);
            }
            UiOutViews();
            NullViews(TQMvSr);
            SrvTex(TQMvSr, 0, mvec, DXGI_FORMAT.R16G16_FLOAT);
            SrvTex(TQMvSr, 1, biasMask, DXGI_FORMAT.R8_UNORM);
            UavTex(TQMvSr, U0, mvecSr, DXGI_FORMAT.R16G16_FLOAT);
            RejasViews();   // 17-11 (REJAS)
            mvPrevValid = false;
            motionValid = false;
            mvecDirty = false;
        }

        void EnsureQualityPsos()
        {
            if (psoMvBlock != null) return;
            long t = Clock.Now;
            psoMvBlock = Pso(CompileCs("mvblock", QShaders.MvBlock));
            psoMvExpandQ = Pso(CompileCs("mvexpandq", QShaders.MvExpandQ));
            psoUiMask = Pso(CompileCs("uimask", QShaders.UiMask));
            psoUiOut = Pso(CompileCs("uiout", QShaders.UiOut));
            psoMvSr = Pso(CompileCs("mvsr", QShaders.MvSr));
            Log.Write("Calidad: shaders de mascaras y vectores por punto (" + Clock.SinceMs(t).ToString("0") + " ms)");
        }

        ID3D12Resource mvLight0;    // 20-11 (DLSS5VEC3): el nivel fino de la pasada ligera (pista temporal de la generacion)

        // Copia entre texturas que estan en NON_PIXEL_SHADER_RESOURCE (y ahi se quedan)
        void CopyTex(ID3D12Resource dst, ID3D12Resource src)
        {
            Barrier(src, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_SOURCE);
            Barrier(dst, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_DEST);
            list.CopyResource(dst, src);
            Barrier(dst, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(src, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
        }

        // Busqueda por bloques de un nivel (de lo grueso a lo fino: el nivel k lee el k + 1). inf = el nivel fino escribe blockInfo
        void SearchLevel(int cur, int k, bool inf, uint* c)
        {
            Barrier(mvLevel[k], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            if (inf) Barrier(blockInfo, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            c[0] = (uint)lw[k]; c[1] = (uint)lh[k]; c[2] = (uint)bx[k]; c[3] = (uint)by[k];
            c[4] = k == Levels - 1 ? 12u : k == Levels - 2 ? 2u : 1u;   // +-12 / +-2 / +-1 / +-1
            c[5] = k < Levels - 1 ? 1u : 0u;
            c[6] = k < Levels - 1 ? (uint)bx[k + 1] : 1u; c[7] = k < Levels - 1 ? (uint)by[k + 1] : 1u;
            c[8] = k == 0 ? 1u : 0u; c[9] = (k == 0 ? 0u : 1u) | (s.MvTemporal && mvPrevValid ? 2u : 0u) | (s.MvLongRel && mvPrevValid ? 4u : 0u) | (s.MvSubEq ? 8u : 0u);   // (17-11 15:40: mvlargo_rel; 15:50: mvsub)   // nivel 0: solo el del nivel grueso y el 0 (17-11, REJAS: + el de la imagen anterior con mvtemporal)
            c[10] = (k == 0 && s.MvFlat && !MvLightNow ? 1u : 0u) | ((uint)Math.Round(s.MvLong * 1e6) << 1);   // 17-11 (rejas): mvlargo en los bits altos              // 16-11: bloques lisos del nivel fino con ventana de 16x16
            c[11] = inf ? 1u : 0u;                              // 16-11: informacion por bloque (u1)
            // 16-11 (d): en el nivel fino, preferencia por 0 frente al ruido (mvsesgo relativa, mvruido en niveles por punto)
            c[12] = F(k == 0 ? (float)s.MvZeroBias : 0.03f); c[13] = F(0.004f); c[14] = F(k == 0 ? (float)(s.MvNoise / 255.0) : 0f);
            c[15] = F(k == 0 ? (float)s.MvExact : 0f);   // 16-11 (mvexacto): sin medio pixel si el entero ya casa
            GiroSearch(k, c);   // 20-11 (GIRO, mvglobal=1): el giro de la camara como candidato del nivel grueso (EngineGiro.cs)
            BindCompute(psoSearch, TSearch + cur * Levels + k, c, 16);
            list.Dispatch(Groups(bx[k], 8), Groups(by[k], 8), 1);
            Barrier(mvLevel[k], D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            if (inf) Barrier(blockInfo, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
        }

        // Despues de pasar la captura a RGBA (inColor en NON_PIXEL_SHADER_RESOURCE). newFrame = false: imagen repetida, vectores a 0
        void RecordMotion(bool newFrame)
        {
            uint* c = stackalloc uint[16];
            QFrameBegin();
            // Solo si alguien los usa: DLSS (captura mas pequeña que la salida) o la generacion. En el HOME a 4K nativo, nada
            // (18-11, MOTOR-CE: con fsr1 / catrom sin generacion no los usa nadie: tampoco)
            bool on = s.Motion && newFrame && ((inW != outW || inH != outH) && scalerNow == "dlss" || fgApplied || (s.MvNativeNr && s.Nr && s.Mode == 30));   // (1.0.5: mv_nativo_dlss5, DLSS 5 a 4K nativo sin FG)
            bool quality = on && QualityMotion;
            if (quality) EnsureQualityPsos();
            bool info = quality;    // el nivel fino escribe blockInfo (para las mascaras y la interfaz)
            int cur = motionSet;
            if (newFrame) MvSrSay();   // 20-11 (SR45)
            if (on)
            {
                QStamp(0);
                for (int k = 0; k < Levels; k++)
                {
                    ID3D12Resource L = luma[cur, k];
                    Barrier(L, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                    for (int i = 0; i < 16; i++) c[i] = 0;
                    c[0] = k == 0 ? 0u : 1u; c[1] = (uint)lw[k]; c[2] = (uint)lh[k];
                    BindCompute(psoLuma, TLuma + cur * Levels + k, c, 4);
                    list.Dispatch(Groups(lw[k], 16), Groups(lh[k], 8), 1);
                    Barrier(L, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                }
                if (motionValid && !frameReset)
                {
                    // 20-11 16:15 (DLSS5VEC2): DLSS 5 + DLSS-G con la entrada de mv_ligero o mas: primero los ligeros para la generacion
                    bool dual = MvLightSize && MvFullWanted && fgApplied && s.FgVec >= 2 && mvecFg != null && !s.MvFixed && !FgVecFullNow;   // (20-11 SR45: o DLSS SR; FGFALLOS: con fg_vectores_completos sin la doble pasada)
                    mvForceLight = dual;
                    for (int k = Levels - 1; k >= 0; k--) SearchLevel(cur, k, info && k == 0, c);
                    if (dual)
                    {
                        // Los ligeros de siempre, expandidos sin mvpx, y de ellos los vectores de la generacion (mvecFg)
                        Barrier(mvec, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                        for (int i = 0; i < 16; i++) c[i] = 0;
                        c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)bx[0]; c[3] = (uint)by[0]; c[4] = 0u;
                        c[8] = F((float)s.MvSmooth);
                        BindCompute(psoExpand, TExpand, c, 12);
                        list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
                        Barrier(mvec, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                        RecordFgVec(true);
                        fgVecDone = true;
                        // 16:25 (DLSS5VEC3): el nivel fino ligero se guarda para la pista temporal (mvtemporal) de la imagen siguiente:
                        // asi la generacion recibe exactamente lo de siempre (antes la pista salia del completo de DLSS 5)
                        if (s.MvTemporal && mvLight0 != null) CopyTex(mvLight0, mvLevel[0]);
                        // Y el nivel fino otra vez, completo, para DLSS 5
                        mvForceLight = false;
                        SearchLevel(cur, 0, info, c);
                    }
                    mvForceLight = false;
                    RecordConsensus(cur, c);   // 17-11 (REJAS): mvconsenso
                    // 17-11 (REJAS, mvtemporal): el campo de cada nivel para la imagen siguiente (20-11 DLSS5VEC3: con la doble pasada, el
                    // nivel fino ligero)
                    if (s.MvTemporal)
                        for (int k = 0; k < Levels; k++)
                            CopyTex(mvPrev[k], k == 0 && dual && mvLight0 != null ? mvLight0 : mvLevel[k]);
                    mvPrevValid = s.MvTemporal;
                }
                else { on = false; mvPrevValid = false; }
                motionSet = 1 - motionSet;
                motionValid = true;
            }
            else if (!newFrame) { } // la misma imagen: la piramide no cambia
            quality = quality && on;
            biasValid = false;
            if (newFrame) uiValid = false;   // imagen nueva: la interfaz vale si se rehace abajo (no en un corte ni sin movimiento)
            if (!on && !mvecDirty) { QStampEnd(on); return; }

            // 16-11 (calidad): mascara por bloque. mvmask = suma de 1 (vector dudoso), 2 (borde quieto/movimiento), 4 (quieto
            // mvmask_quieto imagenes seguidas: sin jitter la historia no aporta nada a lo quieto y L/M "respiran" con ella);
            // mvmask_lados=1: del borde solo el lado quieto (2: los dos)
            bool mask = quality && s.MvMask > 0;
            bool maskEdge = (s.MvMask & 2) != 0;
            if (mask)
            {
                Barrier(blockMask, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                Barrier(blockStill, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                for (int i = 0; i < 16; i++) c[i] = 0;
                c[0] = (uint)bx[0]; c[1] = (uint)by[0];
                c[2] = ((s.MvMask & 1) != 0 ? 1u : 0u) | (maskEdge ? 2u : 0u) | (maskEdge && s.MvMaskSides >= 2 ? 4u : 0u) | ((s.MvMask & 4) != 0 ? 8u : 0u);
                c[3] = (uint)Math.Max(1, s.MvMaskStill);
                c[4] = F((float)s.MvMaskSad); c[5] = F((float)s.MvMaskK); c[6] = F(1.0f); c[7] = F(0.6f / 255f);
                BindCompute(psoMvBlock, TQBlock, c, 8);
                list.Dispatch(Groups(bx[0], 8), Groups(by[0], 8), 1);
                Barrier(blockMask, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                Barrier(blockStill, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            }

            Barrier(mvec, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            if (on && s.MvCapas && !s.MvFixed && !mask) RecordCapas(cur, c);   // 17-11 (REJAS): mvcapas, vectores por punto por capas
            else if (quality && (mask || MvPxNow) && !(on && s.MvFixed))
            {
                // Vectores por punto (mvpx) y mascara sin historia (mvmask) a la vez
                if (mask) Barrier(biasMask, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)bx[0]; c[3] = (uint)by[0];
                c[4] = MvPxNow ? 1u : 0u; c[5] = mask ? 1u : 0u; c[6] = maskEdge ? 1u : 0u;
                c[8] = F((float)s.MvMaskPx); c[9] = F(0.8f); c[10] = F((float)s.MvSmooth); c[11] = F((float)(s.MvPxMargin / 255.0));   // 17-11 (rejas): mvsuave, mvpx_margen
                BindCompute(psoMvExpandQ, TQExpand + cur, c, 12);
                list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
                if (mask) Barrier(biasMask, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                biasValid = mask;
            }
            else
            {
                c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)bx[0]; c[3] = (uint)by[0]; c[4] = on ? 0u : 1u;
                if (on && s.MvFixed) { c[5] = 1u; c[6] = F((float)s.MvFixedX); c[7] = F((float)s.MvFixedY); }   // 17-11 (rejas, prueba)
                c[8] = F(on ? (float)s.MvSmooth : 0f);   // 17-11 (rejas): mvsuave
                BindCompute(psoExpand, TExpand, c, 12);
                list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
            }
            if (on && !s.MvFixed) RecordEnsancha(cur, c);   // 17-11 13:20 (REJAS): mvensancha
            if (on) RecordTruth(cur, c);   // 17-11 (REJAS): mvverdad (prueba con test=paralaje)
            Barrier(mvec, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            if (!fgVecDone) RecordFgVec(on);   // 17-11 13:40 (REJAS): fg_vectores=2|3 (20-11 DLSS5VEC2: si no se hizo ya con los ligeros)
            fgVecDone = false;
            if (on) RecordFgDepth(cur);   // 17-11 15:20: fg_profundidad
            mvecDirty = on;
            srVecValid = false;
            if (biasValid && s.MvMaskMode == 1)
            {
                // mvmask_modo=1: los vectores de DLSS SR con lo marcado fuera de la imagen (2 anchos/altos: la anterior "no existe")
                Barrier(mvecSr, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                for (int i = 0; i < 16; i++) c[i] = 0;
                c[0] = (uint)inW; c[1] = (uint)inH; c[4] = F(2f * inW); c[5] = F(2f * inH);
                BindCompute(psoMvSr, TQMvSr, c, 8);
                list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
                Barrier(mvecSr, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                srVecValid = true;
            }

            // 16-11 (calidad): interfaz quieta para la generacion
            if (quality && s.FgUi > 0 && fgApplied)
            {
                Barrier(uiMaskIn, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                Barrier(stillCount, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
                for (int i = 0; i < 16; i++) c[i] = 0;
                c[0] = (uint)inW; c[1] = (uint)inH; c[2] = (uint)bx[0]; c[3] = (uint)by[0];
                c[4] = (uint)Math.Max(1, s.FgUiFrames);
                c[8] = F(2.5f / 255f); c[9] = F((float)s.FgUiEdge); c[10] = F((float)s.FgUiMov);
                BindCompute(psoUiMask, TQUi + cur, c, 12);
                list.Dispatch(Groups(inW, 16), Groups(inH, 8), 1);
                Barrier(uiMaskIn, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                Barrier(stillCount, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
                uiValid = true;
            }
            QStampEnd(on);
            if (on && s.MvLog) MotionReadback();
        }

        // =============================================================================================== 16-11 (calidad): etiquetas
        // Desde el pintado (Engine.cs), con las demas etiquetas de la imagen: la mascara sin historia para DLSS SR y la interfaz para DLSS-G
        void AddQualityTags(SlExtent inExt, SlExtent outExt, bool dlss)
        {
            if (dlss && biasValid && biasMask != null && s.MvMaskMode == 0)
                tags.Add(SlBufferType.BiasCurrentColorHint, Com.BorrowPtr(biasMask, typeof(ID3D12Resource)), (uint)D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, SlResourceLifecycle.eValidUntilEvaluate, inExt);
            ID3D12Resource ui = UiTarget;
            if (fgApplied && s.FgUi > 0 && uiValid && ui != null)
            {
                SlBufferType t = s.FgUi == 2 ? SlBufferType.UIColorAndAlpha : s.FgUi == 3 ? SlBufferType.NoWarpMask : SlBufferType.UIAlpha;
                tags.Add(t, Com.BorrowPtr(ui, typeof(ID3D12Resource)), (uint)D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, SlResourceLifecycle.eValidUntilPresent, outExt);
            }
        }

        // La evaluacion de DLSS SR (Engine.cs): con mvmask_modo=1, sus vectores (los de la mascara) como etiqueta local
        SlResult EvaluateDlss(IntPtr token, IntPtr cl)
        {
            // 17-11 (fg_vectores=0): la etiqueta de la imagen lleva los vectores a cero (los lee DLSS-G); DLSS SR, los del motor como etiqueta local
            ID3D12Resource srv = srVecValid && mvecSr != null ? mvecSr : FgMvec != mvec ? mvec : null;
            bool dOver = FgDepthTex != depth;   // 17-11 15:20 (fg_profundidad): la de la imagen es para DLSS-G; DLSS SR, la de siempre
            if (srv == null && !dOver) return Sl.EvaluateFeature(SlFeature.DLSS, token, vp, cl);
            SlExtent inExt = new SlExtent(); inExt.width = (uint)inW; inExt.height = (uint)inH;
            localTags.Clear();
            if (srv != null)
                localTags.Add(SlBufferType.MotionVectors, Com.BorrowPtr(srv, typeof(ID3D12Resource)), (uint)D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, SlResourceLifecycle.eValidUntilPresent, inExt);
            if (dOver)
                localTags.Add(SlBufferType.Depth, Com.BorrowPtr(depth, typeof(ID3D12Resource)), (uint)D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, SlResourceLifecycle.eValidUntilPresent, inExt);
            return Sl.EvaluateFeature(SlFeature.DLSS, token, vp, cl, localTags);
        }

        ID3D12Resource UiTarget { get { return s.FgUi == 2 ? uiColor : s.FgUi > 0 ? uiAlpha : null; } }

        // Formato de la interfaz para DLSSGOptions.uiBufferFormat (0 = sin interfaz)
        uint UiBufferFormat() { return s.FgUi == 2 ? (uint)ColorFmt : s.FgUi > 0 ? (uint)DXGI_FORMAT.R8_UNORM : 0u; }
        int FgUiKey() { return s.FgUi * 10 + (s.FgUiRecomp ? 1 : 0) + (s.FgMenus ? 100 : 0); }

        void UiOutViews()
        {
            NullViews(TQUiOut);
            if (uiMaskIn != null) SrvTex(TQUiOut, 0, uiMaskIn, DXGI_FORMAT.R8_UNORM);
            if (final != null) SrvTex(TQUiOut, 1, final, ColorFmt);
            if (uiColor != null) UavTex(TQUiOut, U0, uiColor, ColorFmt);
            if (uiAlpha != null) UavTex(TQUiOut, U0 + 1, uiAlpha, DXGI_FORMAT.R8_UNORM);
        }

        // Despues del CAS ('final' en COPY_SOURCE): la interfaz a la resolucion de salida, para la generacion de esta imagen
        void RecordUiOut()
        {
            if (!(fgApplied && s.FgUi > 0 && uiValid) || psoUiOut == null) return;
            bool color = s.FgUi == 2;
            if ((color && uiColor == null) || (!color && uiAlpha == null))
            {
                // Se crean una vez (la salida no cambia de tamaño) y no se sueltan: Streamline las retiene hasta su Present
                if (color) uiColor = Tex(ColorFmt, outW, outH, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "interfaz (color y alfa)");
                else uiAlpha = Tex(DXGI_FORMAT.R8_UNORM, outW, outH, true, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, "interfaz (alfa)");
                UiOutViews();
            }
            ID3D12Resource t = color ? uiColor : uiAlpha;
            uint* c = stackalloc uint[16];
            QStamp(2);
            if (color) Barrier(final, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            Barrier(t, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            for (int i = 0; i < 16; i++) c[i] = 0;
            c[0] = (uint)outW; c[1] = (uint)outH; c[2] = color ? 1u : 0u;
            c[4] = F(1f / outW); c[5] = F(1f / outH);
            BindCompute(psoUiOut, TQUiOut, c, 8);
            list.Dispatch(Groups(outW, 16), Groups(outH, 8), 1);
            Barrier(t, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            if (color) Barrier(final, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_SOURCE);
            QStamp(3);
            QResolve(2);
            qMaskQ[qCur] |= 2;
        }

        // =============================================================================================== 16-11 (calidad): tiempos de GPU
        // Marcas de tiempo propias (4 por imagen, anillo de 4 imagenes): vectores + mascaras (de la luma al final de la interfaz) y la
        // interfaz a 4K. Una linea por segundo al log con stats=1 o en el banco, y la media del banco en su linea (QualityBench)
        ID3D12QueryHeap qHeapQ;
        ID3D12Resource qReadQ;
        IntPtr qReadQPtr;
        const int QRing = 4;
        readonly ulong[] qFenceQ = new ulong[QRing];
        readonly int[] qMaskQ = new int[QRing];
        int qCur;
        double qMovSec, qUiSec, qMovAll, qUiAll;
        int qMovSecN, qUiSecN, qMovAllN, qUiAllN;
        bool qRecording;
        long qLogAt;

        void QFrameBegin()
        {
            if (qHeapQ == null)
            {
                D3D12_QUERY_HEAP_DESC qhd = new D3D12_QUERY_HEAP_DESC();
                qhd.Type = D3D12_QUERY_HEAP_TYPE.TIMESTAMP;
                qhd.Count = QRing * 4;
                object o;
                Guid iid = typeof(ID3D12QueryHeap).GUID;
                Hr.Check(dev.CreateQueryHeap(ref qhd, ref iid, out o), "CreateQueryHeap calidad");
                qHeapQ = (ID3D12QueryHeap)o;
                qReadQ = MakeBuffer(D3D12_HEAP_TYPE.READBACK, 8 * QRing * 4, D3D12_RESOURCE_STATES.COPY_DEST, "tiempos calidad");
                Hr.Check(qReadQ.Map(0, null, out qReadQPtr), "Map tiempos calidad");
                qLogAt = Clock.Now;
            }
            if (stats.Recording && !qRecording) { qRecording = true; qMovAll = qUiAll = 0; qMovAllN = qUiAllN = 0; }
            ulong done = fence.GetCompletedValue();
            for (int i = 0; i < QRing; i++)
            {
                if (qFenceQ[i] == 0 || done < qFenceQ[i]) continue;
                ulong* t = (ulong*)qReadQPtr + i * 4;
                if ((qMaskQ[i] & 1) != 0 && t[1] > t[0]) { double ms = (t[1] - t[0]) * 1000.0 / tsFreq; qMovSec += ms; qMovSecN++; if (qRecording) { qMovAll += ms; qMovAllN++; } }
                if ((qMaskQ[i] & 2) != 0 && t[3] > t[2]) { double ms = (t[3] - t[2]) * 1000.0 / tsFreq; qUiSec += ms; qUiSecN++; if (qRecording) { qUiAll += ms; qUiAllN++; } }
                qFenceQ[i] = 0;
            }
            if (Clock.SinceMs(qLogAt) >= 1000)
            {
                if ((s.ShowStats || s.BenchSecs > 0) && (qMovSecN > 0 || qUiSecN > 0))
                    Log.Write(string.Format(CultureInfo.InvariantCulture, "Calidad GPU: vectores y mascaras {0:0.000} ms ({1}), interfaz 4K {2:0.000} ms ({3}) | {4}",
                        qMovSecN > 0 ? qMovSec / qMovSecN : 0, qMovSecN, qUiSecN > 0 ? qUiSec / qUiSecN : 0, qUiSecN, QualityDescribe()));
                if (qMovSecN > 0) qMovLast = qMovSec / qMovSecN;   // 20-11 (SR45): MvSrFallback
                qMovSec = qUiSec = 0; qMovSecN = qUiSecN = 0;
                qLogAt = Clock.Now;
            }
            qCur = (qCur + 1) % QRing;
            qMaskQ[qCur] = 0;
            qFenceQ[qCur] = fenceValue + 1;   // la valla de esta imagen (se senala al enviarla)
        }

        void QStamp(int i) { list.EndQuery(qHeapQ, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(qCur * 4 + i)); }
        void QResolve(int i0) { list.ResolveQueryData(qHeapQ, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(qCur * 4 + i0), 2, qReadQ, (ulong)((qCur * 4 + i0) * 8)); }
        void QStampEnd(bool on)
        {
            if (!on) return;
            QStamp(1);
            QResolve(0);
            qMaskQ[qCur] |= 1;
        }

        // Para la linea del banco (Engine.cs BenchDone)
        string QualityBench()
        {
            return string.Format(CultureInfo.InvariantCulture, "\tmov_ms={0:0.000}\tui_ms={1:0.000}\tcalidad={2}",
                qMovAllN > 0 ? qMovAll / qMovAllN : 0, qUiAllN > 0 ? qUiAll / qUiAllN : 0, QualityDescribe());
        }

        string QualityDescribe()
        {
            string r = "";
            if (s.MvFlat) r += "mvflat ";
            if (s.MvPx) r += MvPxNow ? "mvpx " : "mvpx(quitado:1440p+generacion) ";   // 16-11 (E): mvpx_auto
            if (s.MvMask > 0) r += "mvmask" + s.MvMask + ((s.MvMask & 2) != 0 ? "/lados" + s.MvMaskSides : "") + ((s.MvMask & 4) != 0 ? "/quieto" + s.MvMaskStill : "") + (s.MvMaskMode == 1 ? "/vectores" : "") + " ";
            if (s.MvZeroBias != 0.03 || s.MvNoise > 0) r += "sesgo" + s.MvZeroBias.ToString("0.00", CultureInfo.InvariantCulture) + "/ruido" + s.MvNoise.ToString("0.0", CultureInfo.InvariantCulture) + " ";
            if (s.MvExact > 0) r += "exacto" + s.MvExact.ToString("0.00", CultureInfo.InvariantCulture) + " ";
            if (s.FgUi > 0) r += "fgui" + s.FgUi + (s.FgUiRecomp ? "+recomp" : "") + " ";
            if (s.MvTemporal) r += "temporal ";   // 17-11 (REJAS)
            if (s.MvCons > 0) r += "consenso" + s.MvCons.ToString("0.0", CultureInfo.InvariantCulture) + (s.MvConsAxis ? "+eje" : "") + " ";
            if (s.MvCapas) r += "capas ";
            if (s.MvEns > 0) r += "ensancha" + s.MvEns + " ";
            if (s.MvApert > 0) r += "apertura" + s.MvApert + (s.MvApertAmb > 0 ? "/ambigua" + s.MvApertAmb.ToString("0.00", CultureInfo.InvariantCulture) : "") + " ";
            if (s.MvLong > 0) r += "largo" + s.MvLong.ToString("0.000", CultureInfo.InvariantCulture) + " ";
            if (s.MvSmooth > 0) r += "suave" + s.MvSmooth.ToString("0.0", CultureInfo.InvariantCulture) + " ";
            return r.Length > 0 ? r.Trim() : "nada";
        }

        // ---- Diagnostico (mvlog=1): una vez por segundo, el campo de bloques del nivel 0 a la CPU y el vector mas comun al log
        ID3D12Resource mvRead;
        IntPtr mvReadPtr;
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT mvFoot;
        long mvReadAt;
        ulong mvReadFence;

        void MotionReadback()
        {
            if (mvReadFence != 0 && fence.GetCompletedValue() >= mvReadFence) { MotionLog(); mvReadFence = 0; }
            if (mvReadFence != 0 || (mvReadAt != 0 && Clock.SinceMs(mvReadAt) < 1000)) return;
            D3D12_RESOURCE_DESC d;
            mvLevel[0].GetDesc(out d);
            D3D12_PLACED_SUBRESOURCE_FOOTPRINT fp; uint rows; ulong rowSize, total;
            dev.GetCopyableFootprints(ref d, 0, 1, 0, &fp, &rows, &rowSize, &total);
            if (mvRead == null || (long)total > mvReadSize)
            {
                if (mvRead != null) { mvRead.Unmap(0, null); Com.Release(ref mvRead); }
                mvRead = MakeBuffer(D3D12_HEAP_TYPE.READBACK, (long)total, D3D12_RESOURCE_STATES.COPY_DEST, "vectores (diagnostico)");
                Hr.Check(mvRead.Map(0, null, out mvReadPtr), "Map vectores");
                mvReadSize = (long)total;
            }
            mvFoot = fp;
            Barrier(mvLevel[0], D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_SOURCE);
            D3D12_TEXTURE_COPY_LOCATION dst = D3D12_TEXTURE_COPY_LOCATION.FromFootprint(mvRead, fp);
            D3D12_TEXTURE_COPY_LOCATION src = D3D12_TEXTURE_COPY_LOCATION.FromSubresource(mvLevel[0], 0);
            list.CopyTextureRegion(ref dst, 0, 0, 0, ref src, null);
            Barrier(mvLevel[0], D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            mvReadFence = fenceValue + 1;   // la valla de esta imagen (se senala al enviarla)
            mvReadAt = Clock.Now;
        }
        long mvReadSize;

        static float Half(ushort h)
        {
            int e = (h >> 10) & 31, m = h & 1023;
            float v = e == 0 ? m * 5.9604645e-8f : BitConverter.ToSingle(BitConverter.GetBytes(((e + 112) << 23) | (m << 13)), 0);
            return (h & 0x8000) != 0 ? -v : v;
        }

        void MotionLog()
        {
            System.Collections.Generic.Dictionary<int, int> hist = new System.Collections.Generic.Dictionary<int, int>();
            int zero = 0, n = 0;
            byte* b = (byte*)mvReadPtr;
            for (int y = 0; y < by[0]; y++)
                for (int x = 0; x < bx[0]; x++)
                {
                    ushort* p = (ushort*)(b + mvFoot.Offset + (long)y * mvFoot.Footprint.RowPitch + x * 4);
                    n++;
                    if ((p[0] & 0x7FFF) == 0 && (p[1] & 0x7FFF) == 0) { zero++; continue; }
                    int k = (int)Math.Round(Half(p[0])) * 10000 + (int)Math.Round(Half(p[1]));
                    int c; hist.TryGetValue(k, out c); hist[k] = c + 1;
                }
            string top = ""; int topN = 0;
            foreach (System.Collections.Generic.KeyValuePair<int, int> kv in hist) if (kv.Value > topN) { topN = kv.Value; top = (int)Math.Round(kv.Key / 10000.0) + "," + (kv.Key - (int)Math.Round(kv.Key / 10000.0) * 10000); }
            Log.Write(string.Format("Movimiento: {0} bloques, {1:0}% quietos, el mas comun ({2}) {3:0}%", n, 100.0 * zero / Math.Max(1, n), top, 100.0 * topN / Math.Max(1, n)));
        }
    }

    // 16-11 (calidad, agente E2): HLSL de las mascaras y los vectores por punto (cs_5_1, la root signature comun de Shaders.cs)
    static class QShaders
    {
        // Por bloque de 8x8 del nivel fino, para la mascara sin historia de DLSS SR:
        //  r = el vector no explica el bloque: lo que queda tras moverlo (media por punto) > Tsad y > Ksad x su textura (transparencias,
        //      destellos, lo que aparece por detras)
        //  g = borde entre lo quieto y lo que se mueve: bloque con textura quieto junto a uno con textura que se mueve (Rules 2) o al
        //      reves (Rules 4). Los lisos no cuentan (que un liso no se mueva no dice nada)
        //  b = quieto (sin cambio medio por punto > Tstill) NStill imagenes seguidas (Rules 8), con rampa de 4 imagenes
        // c0: bloques x/y, reglas, NStill | c1 (float): Tsad, Ksad, movimiento minimo (px), Tstill
        public const string MvBlock = @"
cbuffer C : register(b0) { uint BX, BY, Rules, NStill; float Tsad, Ksad, Vmov, Tstill; uint4 Pad2; uint4 Pad3; }
Texture2D<float2> Mv : register(t0);
Texture2D<float4> Inf : register(t1);
RWTexture2D<unorm float4> Out : register(u0);
RWTexture2D<uint> BStill : register(u1);

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= BX || id.y >= BY) return;
    int2 b = int2(id.xy);
    float2 v = Mv.Load(int3(b, 0));
    float4 inf = Inf.Load(int3(b, 0));
    float bad = ((Rules & 1) && inf.x > Tsad && inf.x > Ksad * inf.y) ? 1.0 : 0.0;
    float still = 0;
    if (Rules & 8)
    {
        uint n = BStill[b];
        n = inf.z < Tstill ? min(n + 1, 250u) : 0u;
        BStill[b] = n;
        still = saturate(((float)n - (float)NStill + 1.0) / 4.0);
    }
    float edge = 0;
    if ((Rules & 2) && inf.w < 0.5)
    {
        float vv = dot(v, v), m2 = Vmov * Vmov;
        bool still0 = vv < 1e-4, mov = vv >= m2;
        [unroll] for (int j = -1; j <= 1; j++)
            [unroll] for (int i = -1; i <= 1; i++)
            {
                if (i == 0 && j == 0) continue;
                int2 q = clamp(b + int2(i, j), int2(0, 0), int2(BX - 1, BY - 1));
                if (Inf.Load(int3(q, 0)).w > 0.5) continue;
                float2 w = Mv.Load(int3(q, 0));
                float ww = dot(w, w);
                if ((still0 && ww >= m2) || ((Rules & 4) && mov && ww < 1e-4)) edge = 1;
            }
    }
    Out[b] = float4(bad, edge, still, 0);
}";

        // Vectores por punto desde los bloques del nivel fino y mascara sin historia para DLSS SR
        //  Refine (mvpx=1): solo en los bloques junto a otro con un movimiento distinto (> 1 px): cada punto elige entre su vector, los
        //  de los 8 vecinos y 0 el que mejor explica su 3x3 (la anterior con filtro bilineal: hay medios pixeles); el suyo con ventaja
        //  (OwnBias). Fuera de esos bordes, el vector del bloque como siempre (sin mezclar: el movimiento no se corre sobre lo quieto)
        //  Mask (mvmask): r y b del bloque con media rampa hacia los vecinos, y g (borde); con Refine, en vez de g, los puntos del borde
        //  que ni el mejor vector explica (|dif| media en 3x3 > Tpx)
        // c0: ancho, alto, bloques x/y | c1: refine, mask | c2 (float): Tpx, OwnBias
        public const string MvExpandQ = @"
cbuffer C : register(b0) { uint W, H, BX, BY; uint Refine, Mask, EdgeOn, P1; float Tpx, OwnBias, Smooth, Margin; uint4 Pad3; }
Texture2D<float2> Blocks : register(t0);
Texture2D<float4> BMask : register(t1);
Texture2D<float> Cur : register(t2);
Texture2D<float> Prev : register(t3);
RWTexture2D<float2> Out : register(u0);
RWTexture2D<unorm float> MaskOut : register(u1);
SamplerState Lin : register(s0);

float2 B(int2 p) { return Blocks.Load(int3(clamp(p, int2(0, 0), int2(BX - 1, BY - 1)), 0)); }
// 17-11 (rejas, mvsuave=R): si el bloque y sus 8 vecinos llevan casi el mismo movimiento (todos a <= R px del suyo), la media
// de los 9 (antes mediana: 2,9 s de compilar): un barrote que cruza varios bloques no sale ondulado por fracciones de pixel distintas en cada uno.
// Donde hay un borde de verdad (HUD quieto sobre el fondo, objeto contra fondo) algun vecino pasa de R y se queda el suyo
float2 Sm(int2 bk, float R)
{
    float2 v = B(bk);
    if (R <= 0) return v;
    float2 sum = 0;
    [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
        {
            float2 w = B(bk + int2(i, j));
            if (any(abs(w - v) > R)) return v;
            sum += w;
        }
    return sum * (1.0 / 9.0);
}

static float cc[9];   // el 3x3 de la actual alrededor del punto (se lee una vez)
void LoadCur(int2 p)
{
    [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
            cc[(j + 1) * 3 + i + 1] = Cur.Load(int3(clamp(p + int2(i, j), int2(0, 0), int2(W - 1, H - 1)), 0));
}

float Err(int2 p, float2 v)
{
    float s = 0;
    float2 inv = 1.0 / float2(W, H);
    [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
        {
            int2 q = clamp(p + int2(i, j), int2(0, 0), int2(W - 1, H - 1));
            s += abs(cc[(j + 1) * 3 + i + 1] - Prev.SampleLevel(Lin, (float2(q) + 0.5 + v) * inv, 0));
        }
    return s * (1.0 / 9.0);
}

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    int2 p = int2(id.xy), bk = p / 8;
    float2 v = Sm(bk, Smooth);
    bool border = false;
    float eb = 0, ebRaw = 0;
    if (Refine)
    {
        float2 best = v;
        [unroll] for (int j = -1; j <= 1; j++)
            [unroll] for (int i = -1; i <= 1; i++)
            {
                if (i == 0 && j == 0) continue;
                float2 w = B(bk + int2(i, j));
                if (any(abs(w - v) > 1.0))
                {
                    if (!border) { border = true; LoadCur(p); ebRaw = Err(p, v); eb = ebRaw * OwnBias - Margin; }   // 17-11 (rejas): Margin = mvpx_margen (luma media por punto en el 3x3): en lo liso (cielo) todos casan igual y no se cambia
                    float e = Err(p, w);
                    if (e < eb) { eb = e; ebRaw = e; best = w; }
                }
            }
        if (border && any(v != 0))
        {
            float e0 = Err(p, float2(0, 0));
            if (e0 < eb) { eb = e0; ebRaw = e0; best = float2(0, 0); }
        }
        v = best;
    }
    Out[id.xy] = v;
    if (Mask)
    {
        float2 uv = (float2(p) + 0.5) / (8.0 * float2(BX, BY));
        float4 mb = BMask.SampleLevel(Lin, uv, 0);
        float4 mn = BMask.Load(int3(bk, 0));
        float m = max(max(mb.x, mn.x), max(mb.z, mn.z));
        float g = !EdgeOn ? 0.0 : Refine ? ((border && ebRaw > Tpx) ? 1.0 : 0.0) : max(mb.y, mn.y);
        MaskOut[id.xy] = max(m, g);
    }
}";

        // Lo que la generacion (DLSS-G) no debe deformar: puntos quietos NStill imagenes seguidas (|dif| < Tdiff en el punto y sus 4
        // vecinos) con borde (rango de luma en 13 puntos alrededor > Tedge: texto, iconos, marcos) mientras la escena se mueve (de
        // una rejilla de 16x8 bloques de toda la imagen, los que tienen textura: fraccion que se mueve >= Fmov; cada grupo la cuenta)
        // c0: ancho, alto, bloques x/y | c1: NStill | c2 (float): Tdiff, Tedge, Fmov
        public const string UiMask = @"
cbuffer C : register(b0) { uint W, H, BX, BY; uint NStill, P0, P1, P2; float Tdiff, Tedge, Fmov, P3; uint4 Pad3; }
Texture2D<float> Cur : register(t0);
Texture2D<float> Prev : register(t1);
Texture2D<float2> Blocks : register(t2);
Texture2D<float4> Inf : register(t3);
RWTexture2D<unorm float> Ui : register(u0);
RWTexture2D<uint> Still : register(u1);

groupshared uint gMov, gTex;

float Lc(int2 q) { return Cur.Load(int3(clamp(q, int2(0, 0), int2(W - 1, H - 1)), 0)); }
float Dq(int2 q) { q = clamp(q, int2(0, 0), int2(W - 1, H - 1)); return abs(Cur.Load(int3(q, 0)) - Prev.Load(int3(q, 0))); }

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID, uint3 gt : SV_GroupThreadID, uint gi : SV_GroupIndex)
{
    if (gi == 0) { gMov = 0; gTex = 0; }
    GroupMemoryBarrierWithGroupSync();
    int2 sb = min(int2((float2(gt.xy) + 0.5) * float2(BX / 16.0, BY / 8.0)), int2(BX - 1, BY - 1));
    if (Inf.Load(int3(sb, 0)).w < 0.5)
    {
        InterlockedAdd(gTex, 1);
        float2 v = Blocks.Load(int3(sb, 0));
        if (dot(v, v) >= 1.0) InterlockedAdd(gMov, 1);
    }
    GroupMemoryBarrierWithGroupSync();
    if (id.x >= W || id.y >= H) return;
    int2 p = int2(id.xy);
    float d = max(max(Dq(p), max(Dq(p + int2(1, 0)), Dq(p - int2(1, 0)))), max(Dq(p + int2(0, 1)), Dq(p - int2(0, 1))));
    uint n = Still[p];
    n = d < Tdiff ? min(n + 1, 250u) : 0u;
    Still[p] = n;
    float lo = 1, hi = 0;
    [unroll] for (int j = -2; j <= 2; j++)
        [unroll] for (int i = -2; i <= 2; i++)
        {
            if (abs(i) + abs(j) > 2) continue;
            float l = Lc(p + int2(i, j));
            lo = min(lo, l); hi = max(hi, l);
        }
    bool moving = gTex >= 8 && (float)gMov >= Fmov * (float)gTex;
    Ui[p] = (moving && n >= NStill && hi - lo > Tedge) ? 1.0 : 0.0;
}";

        // mvmask_modo=1: vectores para DLSS SR = los de verdad, salvo lo marcado (mascara > 0,5): fuera de la imagen (Off)
        // c0: ancho, alto | c1 (float): Off.xy
        public const string MvSr = @"
cbuffer C : register(b0) { uint W, H, P0, P1; float2 Off; float2 P2; uint4 Pad2; uint4 Pad3; }
Texture2D<float2> Mv : register(t0);
Texture2D<float> Mask : register(t1);
RWTexture2D<float2> Out : register(u0);

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    Out[id.xy] = Mask.Load(int3(id.xy, 0)) > 0.5 ? Off : Mv.Load(int3(id.xy, 0));
}";

        // La interfaz a la resolucion de salida (bilineal desde la de entrada): alfa (UIAlpha / NoWarp) o color premultiplicado + alfa
        // (UIColorAndAlpha, con 'final': la imagen sin el aviso, la misma que la etiquetada como HUDLess)
        // c0: ancho, alto, color | c1 (float): 1/ancho, 1/alto
        public const string UiOut = @"
cbuffer C : register(b0) { uint W, H, Color, P0; float4 Inv; uint4 Pad2; uint4 Pad3; }
Texture2D<float> Ui : register(t0);
Texture2D<float4> Final : register(t1);
RWTexture2D<unorm float4> OutC : register(u0);
RWTexture2D<unorm float> OutA : register(u1);
SamplerState Lin : register(s0);

[numthreads(16, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    float a = Ui.SampleLevel(Lin, (float2(id.xy) + 0.5) * Inv.xy, 0);
    if (Color) OutC[id.xy] = float4(Final.Load(int3(id.xy, 0)).rgb * a, a);
    else OutA[id.xy] = a;
}";
    }
}
