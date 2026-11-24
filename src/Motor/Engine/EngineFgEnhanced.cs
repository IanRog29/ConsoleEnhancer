using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFgEnhanced.cs - 23-11-2026 (1.2.4, sexta ronda, agente ENHANCED): "ENHANCED FG", generacion de imagenes PROPIA x2 / x3 / x4 para
    // CUALQUIER grafica con D3D12. Todo es codigo nuestro (C# + HLSL de DirectCompute en EngineFgEnhancedShaders.cs): nada de los SDK de NVIDIA,
    // AMD ni Intel, ni sus DLL. No depende de la generacion propia vieja (EngineFgPropia*.cs, que sale del menu): lo que servia se ha copiado y
    // rehecho aqui.
    // Lo que coge de cada una:
    //  - ritmo como Smooth Motion: un HILO DE PRESENTACION con su propia COLA de la grafica (la cadena de presentacion se crea en ella, prioridad
    //    alta): las inventadas y la real salen a una hora fija contada desde la LLEGADA de la captura (que el motor sabe exacta), no desde cuando
    //    acaba la grafica, asi que salen igual de separadas aunque la imagen pesada (DLSS 5) tarde distinto cada vez; y lo de esa cola no espera
    //    detras de la siguiente imagen pesada (lo que le pasaba a la propia vieja con DLSS 5). Con VRR, cada una a su hora sin sincronizacion; si
    //    la salida no cabe con margen bajo el refresco (x4 de 30 en 120 Hz, x2 del modo 60), con ella.
    //  - calidad como DLSS FG / XeSS FG: FLUJO OPTICO PROPIO jerarquico (piramide de luma de 5 niveles a media resolucion, bloques de 8x8 de lo
    //    grueso a lo fino con candidatos del padre, del vecino, temporal y 0, subpunto) en los DOS sentidos (de la actual a la anterior y de la
    //    anterior a la actual) y una pasada de apoyo entre vecinos en cada nivel.
    //    (1.2.4 ronda 7, EFG2, medido con VIDEO REAL de juego contra la verdad: con 4 niveles la busqueda gruesa llegaba a +-64 puntos de L0
    //    -128 a 4K- y los giros rapidos de camara -Fallout 4, CS2, GTA V conduciendo- se salian: vectores sueltos y la inventada casi una
    //    mezcla. Con 5 niveles (+-128 / +-256 a 4K) y el apoyo en todos los niveles: GTA V 5,37 -> 4,61, CS2 3,63 -> 3,14, Fallout 4 8,34 ->
    //    6,59, Witcher 3 4,10 -> 4,02; el umbral de oclusion efg_tau 0,04 -> 0,08: menos inventadas de un solo lado.) La inventada de cada instante elige el movimiento por punto con las dos
    //    imagenes (lo que esta en x en el instante t viene de x - t m en la anterior y de x + (1 - t) m en la actual): si casan, se mezclan; si no,
    //    es una OCLUSION y la comprobacion de ida y vuelta (el campo del otro sentido dice lo mismo que m o no) dice de que lado sale: lo que se
    //    destapa, de la actual; lo que se tapa, de la anterior ("relleno desde el lado bueno").
    //  - la interfaz (HUD) quieta: un punto que no cambia en varias imagenes y tiene borde es "quieto"; sus puntos se quedan con el vector 0 y,
    //    para lo que se mueve alrededor, cuentan como algo que tapa (no se coge color de ahi). No se rellena nada ni se toca el fondo (lo que hacia
    //    fgui=4 con DLSS-G y traia inventadas malas sueltas).
    //  - el personaje quieto con la camara girando: el 0 y los vectores de sus bloques compiten por punto con los del fondo; no hereda el de la camara.
    //  - corte de escena: el del motor (frameReset) y uno propio en la grafica (de los bloques con textura, cuantos no casan): sin inventar.
    //  - x3 / x4: el flujo se calcula UNA vez por cada par de reales y sirve para todos los instantes.
    // Dos modos (efg_modo):
    //  - calidad (interpolacion, de serie): las inventadas entre la anterior y la actual; la real se ve (k-1)/k de intervalo despues de llegar
    //    (x2: media imagen real) + lo que tarde la grafica.
    //  - latencia (extrapolacion, "Enhanced FG: baja latencia"): la real se presenta en cuanto esta y las inventadas van HACIA DELANTE desde ella con
    //    los mismos vectores (sin esperar a la siguiente): sin retraso añadido; falla mas en los bordes y en los cambios de direccion.
    //  efg_retraso=F (0..(k-1)/k, en intervalos reales) pone cualquier punto intermedio (p. ej. 0,5 con x4: dos interpoladas y una hacia delante).
    // Claves (motor): efg=0|2|3|4 (de serie 0; solo sin fg=1 ni fg_propia), efg_modo=calidad|latencia, efg_retraso=-1, efg_cola=1 (cola propia),
    // efg_vsync=-1, efg_margen=1.0 (ms), efg_csv=<fichero> (cada Present con la llegada: para medir con PresentMon), efg_prioridad=1 (con la cola
    // propia, la del motor en prioridad normal) y las de ajuste efg_tau, efg_kappa, efg_sigma, efg_cero, efg_corte, efg_lambda, efg_beta,
    // efg_quieto, efg_quieto_dif, efg_quieto_borde, efg_radio.
    partial class Settings
    {
        public int Efg = 0;
        public int EfgMode = 0;                 // 0 = calidad (interpolacion), 1 = latencia (extrapolacion)
        public double EfgPhase = -1;            // efg_retraso (-1 = el del modo)
        // (1.3.1, INTEGRA8; EFGLAT con video real) x4 / x3 en baja latencia: la real una imagen de salida despues (la 1a inventada se interpola
        // entre las dos reales y las demas se extrapolan 1/k menos): -22 % de error en x4 y -27 % en x3 al girar (simulador). En la tele a 120 Hz
        // (INTEGRA8, motor con el video real de GTA V a 4K): x4 +5 ms (23,1 -> 28,2 ms) con el mismo ritmo (100 % a un refresco, p95 0,11 ms
        // fuera de su hora): de serie 0,25. x3 (90/s sin sincronizacion) 8,3 -> 26,6 ms y el ritmo peor (p95 0,35 -> 4,35 ms fuera de su hora,
        // 31 % de imagenes a dos refrescos): de serie -1 (todo hacia delante, como la 1.3). -1 (o 0) = como la 1.3; efg_retraso= manda sobre estas.
        // Con DLSS 5 no (efg_retraso_dlss5=0): x4 a 4K + DLSS 5 con la real una imagen despues el resto del grupo choca con la real siguiente
        // (89 imagenes/s y 22 inventadas saltadas/s al 25 % frente a 109/s y 0 saltadas como la 1.3) y la escalera acabaria en x2
        public double EfgPhaseLat4 = 0.25;      // efg_retraso_lat4
        public double EfgPhaseLat3 = -1;        // efg_retraso_lat3
        public bool EfgPhaseLatD5 = false;      // efg_retraso_dlss5=1: tambien con DLSS 5
        public bool EfgOwnQueue = true;
        // (1.2.4 ronda 7, EFGFIX) efg_prioridad=1 = la cola del motor en prioridad NORMAL con la propia de Enhanced FG en ALTA (lo de la ronda 6).
        // Con DLSS 5 eso colgaba la grafica (DXGI_ERROR_DEVICE_HUNG, nvlddmkm 153): la sesion del usuario del 23-11 a los 37 s de x2 a 4K,
        // INTEGRA7 a los 22 s de x4 y aqui, con el video real de GTA V y DLSS 5 nivel 4 a 4K, a los 45 s de x2. Con las DOS colas en alta
        // (de serie ahora): x2 130 s y x4 180 s sin cuelgue, x2 99,7 % a 2 refrescos y x4 100 % a 1 (notas\EFGFIX.md)
        public bool EfgMainLowPrio = false;
        public int EfgVsync = -1;
        public double EfgMarginMs = 1.0;
        public string EfgCsv = "";
        public float EfgTau = 0.08f, EfgKappa = 0.01f, EfgSigma = 0.012f, EfgZeroPen = 0.02f, EfgCutThr = 0.45f, EfgLambda = 0.002f, EfgBeta = 0.01f;
        public float EfgStillDiff = 3f / 255, EfgStillGrad = 0.05f;
        public int EfgStillN = 3, EfgRadius = 8;
        // (EFG2) niveles de la piramide del flujo (4-6; antes 4). (1.3.2, EFGGIRO) de serie 6: la busqueda por bloques del nivel mas grueso
        // llega a +-256 puntos de L0 (+-512 de una salida de 4K, 13 % del ancho) en vez de +-128: con base 30 un giro rapido de mando se salia
        // (con video real, Fallout 4 girando con el arma al doble de velocidad: error 22,4 -> 18,1, las peores 43-46 -> 27-28); donde el giro
        // cabe, igual o algo mejor (-1 / -2 %). Junto con efg_giro (EngineFgEnhancedGiro.cs)
        public int EfgLevels = 6;
        public bool EfgPropAll = true;          // (EFG2) la pasada de apoyo entre vecinos tambien en los niveles gruesos (antes solo en L0)
        public int EfgSearch = 1;               // (EFG2) busqueda alrededor del mejor en los niveles gruesos (+-N). De serie 1: con 2 el simulador
                                                // daba -1 %, pero en la tele (otra sesion) salio algo peor y no pude repetirlo: queda para probar
        public bool EfgStamps = true;           // efg_tiempos=0: sin las marcas de tiempo de la grafica (prueba)
        public bool EfgPrecompile = true;       // (1.2.4 ronda 7, EFGFIX) efg_precompilar=0: sin compilar sus sombreadores de fondo (EfgPrecompilar)
        // (1.3.1, EFGCAL) la eleccion del movimiento de las inventadas al interpolar (modo calidad; la baja latencia no cambia):
        // efg_sel_vecinos=1: candidatos de los 3x3 bloques de alrededor (antes los 2x2 mas cercanos); efg_sel_local=N: busqueda +-N puntos de
        // L0 alrededor del elegido (lo fino que se mueve distinto de su bloque). De serie 0 / 0 (= la 1.3): medido en el simulador con video
        // real, -0,3 % / 0 % (Fallout 4 13-15 s 7,43 -> 7,41 / 7,42; CS2 3,99 -> 3,98 / 3,99) por +70 % / +60 % de grafica en la eleccion
        public bool EfgSelVecinos = false;
        public int EfgSelLocal = 0;
        // (1.3.1, EFGCAL) efg_nitido: al interpolar, el candidato que mas pesa de cada punto se lee con Catmull-Rom (5 muestras) en vez de
        // bilineal: la textura fina (grietas del asfalto, hierba) se lava menos al llevarla a medio punto. -1 = solo en x2 (en x3 / x4 son 2-3
        // inventadas por real y la grafica ya va justa con DLSS 5), 1 = siempre, 0 = nunca (lo de la 1.3). De serie 0: medido con video real
        // en el simulador (efgsim, GTA V 2,5-11 s / Fallout 4 9-17 s) la nitidez sube 80,0 -> 83,3 % / 88,6 -> 91,5 % de la de la verdad, pero
        // el error no baja (5,40 -> 5,40 / 7,80 -> 7,85) y los puntos muy errados suben un 2 %: no es claramente mejor; queda para probar a ojo
        public int EfgNitido = 0;
        public bool EfgNitidoOn { get { return EfgNitido > 0 || (EfgNitido < 0 && Efg <= 2); } }
        public bool EfgSaltoFino = false;       // (1.2.4 ronda 7, EFGFIX) efg_salto_fino=1: con otra real en la cola solo se saltan las inventadas que llegarian a
                                                // la siguiente real. (1.3, RELEASE13) de serie 0 = como antes: medido con video real, x4 baja latencia + DLSS 5 al 65 %
                                                // a 4K, sin saltos pero la grafica se colgo a los 37 s (DXGI_ERROR_DEVICE_HUNG, nvlddmkm 153); con 0, sin cuelgue y
                                                // el vigilante de x4 + DLSS 5 baja DLSS 5 al 35 % (EngineX4D5.cs)

        bool ApplyEfg(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "efg": { int k = I(val); Efg = k <= 1 ? 0 : Math.Min(4, k); return true; }
                case "efg_modo": { string v = val.Trim().ToLowerInvariant(); EfgMode = v == "latencia" || v == "1" || v == "extrapolacion" || v == "baja" ? 1 : 0; return true; }
                case "efg_retraso": EfgPhase = D(val); return true;
                case "efg_retraso_lat4": EfgPhaseLat4 = D(val); return true;   // (1.3.1, INTEGRA8)
                case "efg_retraso_lat3": EfgPhaseLat3 = D(val); return true;   // (1.3.1, INTEGRA8)
                case "efg_retraso_dlss5": EfgPhaseLatD5 = B(val); return true;   // (1.3.1, INTEGRA8)
                case "efg_cola": EfgOwnQueue = B(val); return true;
                case "efg_prioridad": EfgMainLowPrio = B(val); return true;
                case "efg_vsync": EfgVsync = I(val); return true;
                case "efg_margen": EfgMarginMs = Math.Max(0, Math.Min(20, D(val))); return true;
                case "efg_csv": EfgCsv = val.Trim(); return true;
                case "efg_tau": EfgTau = (float)D(val); return true;
                case "efg_kappa": EfgKappa = (float)D(val); return true;
                case "efg_sigma": EfgSigma = (float)Math.Max(0.001, D(val)); return true;
                case "efg_cero": EfgZeroPen = (float)D(val); return true;
                case "efg_corte": EfgCutThr = (float)D(val); return true;
                case "efg_lambda": EfgLambda = (float)D(val); return true;
                case "efg_beta": EfgBeta = (float)D(val); return true;
                case "efg_quieto": EfgStillN = Math.Max(1, I(val)); return true;
                case "efg_quieto_dif": EfgStillDiff = (float)D(val); return true;
                case "efg_quieto_borde": EfgStillGrad = (float)D(val); return true;
                case "efg_radio": EfgRadius = Math.Max(2, Math.Min(16, I(val))); return true;
                case "efg_niveles": EfgLevels = Math.Max(4, Math.Min(6, I(val))); return true;
                case "efg_apoyo_niveles": EfgPropAll = B(val); return true;
                case "efg_busqueda": EfgSearch = Math.Max(1, Math.Min(4, I(val))); return true;
                case "efg_tiempos": EfgStamps = B(val); return true;
                case "efg_precompilar": EfgPrecompile = B(val); return true;   // (EFGFIX)
                case "efg_salto_fino": EfgSaltoFino = B(val); return true;   // (EFGFIX)
                case "efg_sel_vecinos": EfgSelVecinos = B(val); return true;   // (1.3.1, EFGCAL)
                case "efg_sel_local": EfgSelLocal = Math.Max(0, Math.Min(3, I(val))); return true;   // (1.3.1, EFGCAL)
                case "efg_nitido": EfgNitido = Math.Max(-1, Math.Min(1, I(val))); return true;   // (1.3.1, EFGCAL)
            }
            return ApplyEfgRitmo(key, val) || ApplyEfgLat(key, val) || ApplyEfgGiro(key, val);   // (1.3.1: EFGCAL efg_ritmo* / efg_colchon_ms, EngineFgEnhancedRitmo.cs; EFGLAT efg_lat*, EngineFgEnhancedLat.cs; 1.3.2: EFGGIRO efg_giro*, EngineFgEnhancedGiro.cs)
        }
    }

    unsafe partial class Engine
    {
        // (1.2.4 ronda 7, EFG2) hasta 6 niveles de piramide (de serie 5: con 4 la busqueda gruesa llegaba a +-64 puntos de L0 y los giros
        // rapidos -Fallout 4, CS2- se salian); tablas por nivel con EfgTabDown / EfgTabFlow / ...
        const int EfgSets = 3, EfgMaxLv = 6, EfgTabs = 5 * EfgMaxLv + 6;
        int efgLv = 5;
        static int EfgTabDown(int l) { return l; }
        static int EfgTabFlow(int d, int l) { return EfgMaxLv + d * EfgMaxLv + l; }
        static int EfgTabProp(int d) { return 3 * EfgMaxLv + d; }
        const int EfgTabStill = 3 * EfgMaxLv + 2, EfgTabCut = 3 * EfgMaxLv + 3, EfgTabSel = 3 * EfgMaxLv + 4, EfgTabFin = 3 * EfgMaxLv + 5;
        static int EfgTabPropL(int d, int l) { return 3 * EfgMaxLv + 6 + d * EfgMaxLv + l; }   // (apoyo en los niveles gruesos)
        ID3D12CommandQueue efgQueue;                 // la cola propia de presentacion (la cadena se crea en ella) o null (la del motor)
        ID3D12Fence efgFence;                        // la valla de lo que manda el hilo de presentacion
        readonly long[] efgSlotDone = new long[EfgSets];   // (valla efgFence) la ultima lista que lee cada ranura de efgRing
        readonly ID3D12Resource[] efgRing = new ID3D12Resource[EfgSets];   // copias de las reales (lo que se enseña)
        ID3D12Resource efgGen, efgSel;
        readonly ID3D12Resource[,] efgL = new ID3D12Resource[EfgSets, EfgMaxLv];
        readonly ID3D12Resource[,,] efgF = new ID3D12Resource[EfgSets, 2, EfgMaxLv];   // [juego, sentido 0 = C2P / 1 = P2C, nivel]
        readonly ID3D12Resource[,] efgFRaw = new ID3D12Resource[EfgSets, 2];
        readonly ID3D12Resource[,] efgFRawL = new ID3D12Resource[2, EfgMaxLv];   // (EFG2) el flujo de cada nivel grueso antes de su apoyo (comun)
        bool efgPropAll;
        readonly ID3D12Resource[] efgSt = new ID3D12Resource[EfgSets], efgCut = new ID3D12Resource[EfgSets];
        readonly int[] efgLw = new int[EfgMaxLv], efgLh = new int[EfgMaxLv], efgBx = new int[EfgMaxLv], efgBy = new int[EfgMaxLv];
        int efgQw, efgQh;
        ID3D12PipelineState psoEfgLuma, psoEfgDown, psoEfgFlow, psoEfgProp, psoEfgStill, psoEfgCut, psoEfgSel, psoEfgFin;
        ID3D12DescriptorHeap efgHeap;
        IntPtr efgHeapPtr;
        D3D12_CPU_DESCRIPTOR_HANDLE efgCpu0;
        D3D12_GPU_DESCRIPTOR_HANDLE efgGpu0;
        bool efgReady, efgBroken, efgHold, efgJobReset;
        // (1.2.4 ronda 6, INTEGRA6) con la cadena en la cola propia NADA puede escribirla desde la cola del motor (DXGI_ERROR_ACCESS_DENIED: las
        // pasadas q01 de ENHANCED y la cadena propia de FGMULTI): los repintados (aviso, imagen quieta 250 ms), lo que se enseña con Enhanced FG
        // apagada en marcha y la imagen negra de la espera tambien van por el hilo: el motor deja la imagen en una ranura y el hilo la copia
        // a la cadena y la presenta tal cual (sin inventar ni tocar el flujo). efgShow = este Present es eso
        bool efgShow;
        int efgShowW;
        long efgShows;
        volatile int efgShowPend;
        int efgW = EfgSets - 1;
        long efgJobArrival, efgSeq;

        bool EfgWanted() { return s.Efg >= 2 && !FgXe && !FgFfx && !efgBroken; }

        // InitDevice, antes de crear la cadena: con efg pedido al arrancar (y sin la cadena de FSR 3 FG / XeSS FG), la cola propia de presentacion
        ID3D12CommandQueue EfgSwapQueue()
        {
            if (s.Efg < 2 || !s.EfgOwnQueue || FgFfx || FgXe) return queue;
            try
            {
                D3D12_COMMAND_QUEUE_DESC qd = new D3D12_COMMAND_QUEUE_DESC();
                qd.Type = D3D12_COMMAND_LIST_TYPE.DIRECT;
                qd.Priority = 100;   // HIGH
                object o;
                Guid iid = typeof(ID3D12CommandQueue).GUID;
                Hr.Check(dev.CreateCommandQueue(ref qd, ref iid, out o), "CreateCommandQueue (Enhanced FG)");
                efgQueue = (ID3D12CommandQueue)o;
                Log.Write("Enhanced FG: cola propia de presentacion (prioridad alta; la cadena se crea en ella)");
                return efgQueue;
            }
            catch (Exception ex) { Log.Write("Enhanced FG: sin cola propia (" + ex.Message + "): con la del motor"); efgQueue = null; return queue; }
        }

        // Prioridad de la cola del motor (InitDevice): con la cola propia de Enhanced FG, normal, para que la de presentacion pase delante
        int EfgMainQueuePriority(int def) { return s.Efg >= 2 && s.EfgOwnQueue && s.EfgMainLowPrio && !FgFfx && !FgXe ? 0 : def; }

        // La cola de las listas del hilo de presentacion
        ID3D12CommandQueue EfgPq { get { return efgQueue ?? queue; } }

        // Antes de que el motor presente por su cuenta (repintado, espera, sin Enhanced FG) con la cadena en la cola propia: que el hilo acabe y
        // que la cola de presentacion espere a lo grabado por el motor (ScPresent, EngineFgXe.cs)
        void EfgEngineSync()
        {
            if (efgThread != null) EfgDrain();
            if (efgQueue == null || queue == null) return;
            fenceValue++;
            queue.Signal(fence, fenceValue);
            efgQueue.Wait(fence, fenceValue);
        }

        void EfgEnsure()
        {
            if (efgReady) return;
            long t0 = Clock.Now;
            EfgPsos();
            int W = outW, H = outH;
            efgLv = Math.Max(4, Math.Min(EfgMaxLv, s.EfgLevels));
            efgLw[0] = (W + 1) / 2; efgLh[0] = (H + 1) / 2;
            for (int l = 1; l < efgLv; l++) { efgLw[l] = (efgLw[l - 1] + 1) / 2; efgLh[l] = (efgLh[l - 1] + 1) / 2; }
            for (int l = 0; l < efgLv; l++) { efgBx[l] = (efgLw[l] + 7) / 8; efgBy[l] = (efgLh[l] + 7) / 8; }
            efgQw = efgLw[1]; efgQh = efgLh[1];
            const D3D12_RESOURCE_STATES NP = D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE;
            for (int c = 0; c < EfgSets; c++)
            {
                efgRing[c] = Tex(ColorFmt, W, H, false, D3D12_RESOURCE_STATES.COMMON, "Enhanced FG: real " + c);
                for (int l = 0; l < efgLv; l++)
                {
                    efgL[c, l] = Tex(DXGI_FORMAT.R16_FLOAT, efgLw[l], efgLh[l], true, NP, "Enhanced FG: luma " + c + "/" + l);
                    for (int d = 0; d < 2; d++) efgF[c, d, l] = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, efgBx[l], efgBy[l], true, NP, "Enhanced FG: flujo " + c + "/" + d + "/" + l);
                }
                for (int d = 0; d < 2; d++) efgFRaw[c, d] = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, efgBx[0], efgBy[0], true, NP, "Enhanced FG: flujo sin apoyo " + c + "/" + d);
                efgSt[c] = Tex(DXGI_FORMAT.R16G16_FLOAT, efgLw[0], efgLh[0], true, NP, "Enhanced FG: quieto " + c);
                efgCut[c] = Tex(DXGI_FORMAT.R32_FLOAT, 1, 1, true, NP, "Enhanced FG: corte " + c);
            }
            efgPropAll = s.EfgPropAll;
            if (efgPropAll)
                for (int d = 0; d < 2; d++)
                    for (int l = 1; l < efgLv; l++) efgFRawL[d, l] = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, efgBx[l], efgBy[l], true, NP, "Enhanced FG: flujo sin apoyo " + d + "/" + l);
            efgSel = Tex(DXGI_FORMAT.R16G16B16A16_FLOAT, efgQw, efgQh, true, NP, "Enhanced FG: eleccion");
            efgGen = Tex(ColorFmt, W, H, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "Enhanced FG: inventada");
            // tablas de descriptores propias (EfgTabs por juego)
            D3D12_DESCRIPTOR_HEAP_DESC hd = new D3D12_DESCRIPTOR_HEAP_DESC();
            hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE.CBV_SRV_UAV;
            hd.NumDescriptors = (uint)(EfgSets * EfgTabs * Shaders.TableSize);
            hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.SHADER_VISIBLE;
            object o;
            Guid iid = typeof(ID3D12DescriptorHeap).GUID;
            Hr.Check(dev.CreateDescriptorHeap(ref hd, ref iid, out o), "CreateDescriptorHeap (Enhanced FG)");
            efgHeap = (ID3D12DescriptorHeap)o;
            efgHeapPtr = Com.BorrowPtr(efgHeap, typeof(ID3D12DescriptorHeap));
            efgHeap.GetCPUDescriptorHandleForHeapStart(out efgCpu0);
            efgHeap.GetGPUDescriptorHandleForHeapStart(out efgGpu0);
            D3D12_SHADER_RESOURCE_VIEW_DESC nsd = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(ColorFmt);
            D3D12_UNORDERED_ACCESS_VIEW_DESC nud = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(ColorFmt);
            for (int t = 0; t < EfgSets * EfgTabs; t++)
            {
                for (int i = 0; i < U0; i++) dev.CreateShaderResourceView(null, &nsd, efgCpu0.Offset(t * Shaders.TableSize + i, inc));
                for (int i = U0; i < Shaders.TableSize; i++) dev.CreateUnorderedAccessView(null, null, &nud, efgCpu0.Offset(t * Shaders.TableSize + i, inc));
            }
            for (int c = 0; c < EfgSets; c++)
            {
                int p = (c + EfgSets - 1) % EfgSets;
                EfgSrv(c, EfgTabDown(0), 0, efgRing[c], ColorFmt); EfgUav(c, EfgTabDown(0), 0, efgL[c, 0], DXGI_FORMAT.R16_FLOAT);
                for (int l = 1; l < efgLv; l++) { EfgSrv(c, EfgTabDown(l), 0, efgL[c, l - 1], DXGI_FORMAT.R16_FLOAT); EfgUav(c, EfgTabDown(l), 0, efgL[c, l], DXGI_FORMAT.R16_FLOAT); }
                for (int d = 0; d < 2; d++)
                    for (int l = 0; l < efgLv; l++)
                    {
                        int t = EfgTabFlow(d, l);
                        EfgSrv(c, t, 0, d == 0 ? efgL[c, l] : efgL[p, l], DXGI_FORMAT.R16_FLOAT);
                        EfgSrv(c, t, 1, d == 0 ? efgL[p, l] : efgL[c, l], DXGI_FORMAT.R16_FLOAT);
                        if (l < efgLv - 1) EfgSrv(c, t, 2, efgF[c, d, l + 1], DXGI_FORMAT.R16G16B16A16_FLOAT);
                        EfgSrv(c, t, 3, efgF[p, 0, l], DXGI_FORMAT.R16G16B16A16_FLOAT);
                        EfgUav(c, t, 0, l == 0 ? efgFRaw[c, d] : efgPropAll ? efgFRawL[d, l] : efgF[c, d, l], DXGI_FORMAT.R16G16B16A16_FLOAT);
                        if (efgPropAll && l > 0)
                        {
                            int tp = EfgTabPropL(d, l);
                            EfgSrv(c, tp, 0, d == 0 ? efgL[c, l] : efgL[p, l], DXGI_FORMAT.R16_FLOAT);
                            EfgSrv(c, tp, 1, d == 0 ? efgL[p, l] : efgL[c, l], DXGI_FORMAT.R16_FLOAT);
                            EfgSrv(c, tp, 2, efgFRawL[d, l], DXGI_FORMAT.R16G16B16A16_FLOAT);
                            EfgUav(c, tp, 0, efgF[c, d, l], DXGI_FORMAT.R16G16B16A16_FLOAT);
                        }
                    }
                for (int d = 0; d < 2; d++)
                {
                    int t = EfgTabProp(d);
                    EfgSrv(c, t, 0, d == 0 ? efgL[c, 0] : efgL[p, 0], DXGI_FORMAT.R16_FLOAT);
                    EfgSrv(c, t, 1, d == 0 ? efgL[p, 0] : efgL[c, 0], DXGI_FORMAT.R16_FLOAT);
                    EfgSrv(c, t, 2, efgFRaw[c, d], DXGI_FORMAT.R16G16B16A16_FLOAT);
                    EfgUav(c, t, 0, efgF[c, d, 0], DXGI_FORMAT.R16G16B16A16_FLOAT);
                }
                EfgSrv(c, EfgTabStill, 0, efgL[c, 0], DXGI_FORMAT.R16_FLOAT); EfgSrv(c, EfgTabStill, 1, efgL[p, 0], DXGI_FORMAT.R16_FLOAT);
                EfgSrv(c, EfgTabStill, 2, efgSt[p], DXGI_FORMAT.R16G16_FLOAT); EfgUav(c, EfgTabStill, 0, efgSt[c], DXGI_FORMAT.R16G16_FLOAT);
                EfgSrv(c, EfgTabCut, 0, efgF[c, 0, 0], DXGI_FORMAT.R16G16B16A16_FLOAT); EfgUav(c, EfgTabCut, 0, efgCut[c], DXGI_FORMAT.R32_FLOAT);
                EfgSrv(c, EfgTabSel, 0, efgL[p, 0], DXGI_FORMAT.R16_FLOAT); EfgSrv(c, EfgTabSel, 1, efgL[c, 0], DXGI_FORMAT.R16_FLOAT);
                EfgSrv(c, EfgTabSel, 2, efgF[c, 1, 0], DXGI_FORMAT.R16G16B16A16_FLOAT); EfgSrv(c, EfgTabSel, 3, efgF[c, 0, 0], DXGI_FORMAT.R16G16B16A16_FLOAT);
                EfgSrv(c, EfgTabSel, 4, efgSt[c], DXGI_FORMAT.R16G16_FLOAT); EfgUav(c, EfgTabSel, 0, efgSel, DXGI_FORMAT.R16G16B16A16_FLOAT);
                EfgSrv(c, EfgTabSel, 5, efgF[c, 1, 1], DXGI_FORMAT.R16G16B16A16_FLOAT); EfgSrv(c, EfgTabSel, 6, efgF[c, 0, 1], DXGI_FORMAT.R16G16B16A16_FLOAT);
                EfgSrv(c, EfgTabSel, 7, efgF[c, 0, 3], DXGI_FORMAT.R16G16B16A16_FLOAT);
                EfgSrv(c, EfgTabFin, 0, efgRing[p], ColorFmt); EfgSrv(c, EfgTabFin, 1, efgRing[c], ColorFmt);
                EfgSrv(c, EfgTabFin, 2, efgSel, DXGI_FORMAT.R16G16B16A16_FLOAT); EfgSrv(c, EfgTabFin, 3, efgSt[c], DXGI_FORMAT.R16G16_FLOAT);
                EfgSrv(c, EfgTabFin, 4, efgCut[c], DXGI_FORMAT.R32_FLOAT); EfgUav(c, EfgTabFin, 0, efgGen, ColorFmt);
            }
            EfgLatEnsure();   // (1.3.1, EFGLAT: el camino propio de la baja latencia, EngineFgEnhancedLat.cs)
            EfgGiroEnsure();   // (1.3.2, EFGGIRO: el movimiento global de la camara, EngineFgEnhancedGiro.cs)
            iid = typeof(ID3D12Fence).GUID;
            Hr.Check(dev.CreateFence(0, D3D12_FENCE_FLAGS.NONE, ref iid, out o), "CreateFence (Enhanced FG)");
            efgFence = (ID3D12Fence)o;
            EfgThreadStart();
            efgReady = true;
            long mb = ((long)W * H * 4 * (EfgSets + 1) + (long)efgLw[0] * efgLh[0] * (2 + 4) * EfgSets * 4 / 3) >> 20;
            Log.Write("Enhanced FG si (x" + s.Efg + ", " + EfgModeName() + "; flujo optico propio en los dos sentidos a " + efgLw[0] + "x" + efgLh[0] +
                      ", eleccion a " + efgQw + "x" + efgQh + ", " + (efgQueue != null ? "cola propia de presentacion" : "con la cola del motor") +
                      "; ~" + mb + " MB; listo en " + Clock.SinceMs(t0).ToString("0", IC) + " ms)");
        }

        // Los sombreadores (InitPipelines con efg pedido al arrancar: el flujo tarda en compilarse la 1a vez; luego van de la cache)
        void EfgPsos()
        {
            if (psoEfgFin != null) return;
            // (1.2.4 ronda 6, INTEGRA6: los 8 a la vez. Sin la cache -la primera vez, o tras una version nueva- uno tras otro tardaban ~21 s en
            // compilarse y el motor escondido del relevo no llegaba a tiempo: 28 s de telon en negro al elegir Enhanced FG, sesion a2 del 23-11)
            long t0 = Clock.Now;
            string[] nm = { "efg_luma", "efg_down", "efg_flujo", "efg_apoyo", "efg_quieto", "efg_corte", "efg_eleccion", "efg_inventada" };
            string[] src = { EfgShaders.Luma, EfgShaders.Down, EfgShaders.Flow, EfgShaders.Prop, EfgShaders.Still, EfgShaders.Cut, EfgShaders.Sel, EfgShaders.Fin };
            byte[][] cs = new byte[nm.Length][];
            System.Threading.Tasks.Parallel.For(0, nm.Length, delegate (int i) { cs[i] = CompileCs(nm[i], src[i]); });
            psoEfgLuma = Pso(cs[0]); psoEfgDown = Pso(cs[1]); psoEfgFlow = Pso(cs[2]); psoEfgProp = Pso(cs[3]);
            psoEfgStill = Pso(cs[4]); psoEfgCut = Pso(cs[5]); psoEfgSel = Pso(cs[6]); psoEfgFin = Pso(cs[7]);
            EfgLatPsos();   // (1.3.1, EFGLAT)
            EfgGiroPsos();   // (1.3.2, EFGGIRO)
            Log.Write("Enhanced FG: shaders (" + Clock.SinceMs(t0).ToString("0", IC) + " ms)");
        }

        // (1.2.4 ronda 7, EFGFIX) Los 8 sombreadores de Enhanced FG a la cache de fondo, en cualquier motor que se abra sin Enhanced FG (el de delante,
        // el de reserva, los escondidos): la sesion del usuario del 23-11 (17:59:44) eligio Enhanced FG por primera vez con la version nueva y su
        // motor escondido tardo 24,7 s en estar listo (shaders 21 156 ms; INTEGRA7: 24-30 s), con el aviso "preparando…" encima todo ese rato.
        // Con la cache llena, 30 ms. Un hilo de prioridad baja, uno tras otro, 4 s despues de arrancar; solo compila los que no esten
        // (efg_precompilar=0 = no)
        static int efgPrecompiling;
        void EfgPrecompilar()
        {
            if (s.Efg >= 2 || !s.EfgPrecompile || Interlocked.Exchange(ref efgPrecompiling, 1) != 0) return;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    Thread.Sleep(4000);
                    long t0 = Clock.Now;
                    string[] nm = { "efg_flujo", "efg_inventada", "efg_eleccion", "efg_apoyo", "efg_luma", "efg_down", "efg_quieto", "efg_corte" };
                    string[] src = { EfgShaders.Flow, EfgShaders.Fin, EfgShaders.Sel, EfgShaders.Prop, EfgShaders.Luma, EfgShaders.Down, EfgShaders.Still, EfgShaders.Cut };
                    int hechos = 0;
                    for (int i = 0; i < nm.Length && !Program.Quit; i++)
                        if (!CsCached(nm[i], src[i])) { CompileCs(nm[i], src[i]); hechos++; }
                    if (!Program.Quit) hechos += EfgLatPrecompile();   // (1.3.1, EFGLAT)
                    if (!Program.Quit) hechos += EfgGiroPrecompile();   // (1.3.2, EFGGIRO)
                    if (hechos > 0) Log.Write("Enhanced FG: " + hechos + " sombreadores a la cache de fondo (" + Clock.SinceMs(t0).ToString("0", IC) + " ms): elegirla luego no espera a compilarlos");
                }
                catch (Exception ex) { Log.Write("Enhanced FG: sombreadores de fondo: " + ex.Message); }
            });
            t.IsBackground = true;
            t.Priority = ThreadPriority.Lowest;
            t.Name = "Enhanced FG (sombreadores de fondo)";
            t.Start();
        }

        string EfgModeName()
        {
            if (s.EfgPhase >= 0) return "retraso " + s.EfgPhase.ToString("0.00", IC) + " de imagen real";
            { int k = Math.Max(2, Math.Min(4, s.Efg)); if (EfgLat34(k)) return "baja latencia (hacia delante; la real con retraso " + EfgPhaseLat34(k).ToString("0.00", IC) + ")"; }
            return s.EfgMode == 1 ? "baja latencia (hacia delante)" : "calidad (interpolacion)";
        }

        D3D12_CPU_DESCRIPTOR_HANDLE EfgCpu(int set, int tab, int slot) { return efgCpu0.Offset((set * EfgTabs + tab) * Shaders.TableSize + slot, inc); }
        D3D12_GPU_DESCRIPTOR_HANDLE EfgGpu(int set, int tab) { return efgGpu0.Offset((set * EfgTabs + tab) * Shaders.TableSize, inc); }
        void EfgSrv(int set, int tab, int slot, ID3D12Resource r, DXGI_FORMAT f)
        {
            D3D12_SHADER_RESOURCE_VIEW_DESC d = D3D12_SHADER_RESOURCE_VIEW_DESC.Tex2D(f);
            dev.CreateShaderResourceView(r, &d, EfgCpu(set, tab, slot));
        }
        void EfgUav(int set, int tab, int slot, ID3D12Resource r, DXGI_FORMAT f)
        {
            D3D12_UNORDERED_ACCESS_VIEW_DESC d = D3D12_UNORDERED_ACCESS_VIEW_DESC.Tex2D(f);
            dev.CreateUnorderedAccessView(r, null, &d, EfgCpu(set, tab, U0 + slot));
        }

        // RenderFrame, paso 5 (con 'show' = lo que se enseñaria, con su aviso si lo hay): la real se copia a su ranura y la presenta el hilo
        bool EfgTakeReal(ID3D12Resource show, int slot)
        {
            efgHold = false; efgShow = false;
            if (s.NoPresent) return false;
            bool real = EfgWanted() && slot >= 0;
            if (!real && efgQueue == null) return false;   // (sin la cola propia lo demas va por la cadena como siempre)
            try { EfgEnsure(); }
            catch (Exception ex) { efgBroken = true; Log.Write("Enhanced FG no disponible: " + ex.Message + " (se sigue sin ella)"); return false; }
            int w = (efgW + 1) % EfgSets;
            // (INTEGRA6: si el hilo aun no ha copiado la de enseñar de antes -la misma ranura-, que acabe: son raras y cortas)
            for (int i = 0; i < 100 && efgShowPend > 0; i++) Thread.Sleep(1);
            long done = Interlocked.Read(ref efgSlotDone[w]);
            if (done > 0 && efgFence.GetCompletedValue() < (ulong)done) queue.Wait(efgFence, (ulong)done);   // (la grafica: el hilo ya no la lee)
            Barrier(efgRing[w], D3D12_RESOURCE_STATES.COMMON, D3D12_RESOURCE_STATES.COPY_DEST);
            list.CopyResource(efgRing[w], show);
            Barrier(efgRing[w], D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.COMMON);
            if (!real)
            {
                // (INTEGRA6) solo enseñarla: la ranura siguiente sin pasar a ser la "anterior" (la proxima real la vuelve a usar)
                efgShowW = w; efgShow = true; efgHold = true;
                efgJobArrival = slot >= 0 ? slotArrival[slot] : Clock.Now;
                return true;
            }
            efgW = w;
            efgJobArrival = slotArrival[slot];
            efgJobContent = EfgContentOf(slot);   // (1.3.1, EFGCAL: la hora del contenido, EngineFgEnhancedRitmo.cs)
            efgJobReset = frameReset;
            efgHold = true;
            return true;
        }

        // Run, en vez de ScPresent: el trabajo al hilo de presentacion (que presenta las inventadas y la real a su hora)
        int EfgPresent(uint sync, uint flags)
        {
            efgHold = false;
            if (efgHr < 0) { int e = efgHr; efgHr = 0; return e; }
            EfgJob j = new EfgJob();
            bool show = efgShow;
            efgShow = false;
            j.Show = show;
            if (show) Interlocked.Increment(ref efgShowPend);
            j.W = show ? efgShowW : efgW; j.Arrival = efgJobArrival; j.Content = show ? 0 : efgJobContent; j.Fence = fenceValue; j.Reset = efgJobReset; j.Seq = show ? efgSeq : ++efgSeq;
            j.Sync = sync; j.Flags = flags; j.K = Math.Max(2, Math.Min(4, s.Efg));
            j.Phase = EfgPhaseFor(j.K);
            j.RealMs = 1000.0 / Math.Max(1.0, (s.FpsNum > 0 ? s.FpsNum / (double)Math.Max(1, s.FpsDen) : 59.94) * (s.Mode == 30 ? 0.5 : 1));
            j.Ready = new ManualResetEvent(false);
            lock (efgWatchQ) efgWatchQ.Enqueue(j);   // (el vigilante apunta cuando la grafica la acaba: EngineFgEnhancedHilo.cs)
            efgWatchEv.Set();
            lock (efgJobs) { efgJobs.Enqueue(j); efgIdle.Reset(); }
            efgJobEv.Set();
            return 0;
        }

        // El instante de la real dentro de las k presentaciones (en intervalos reales de retraso): calidad (k-1)/k, latencia 0
        double EfgPhaseFor(int k)
        {
            double ph = s.EfgPhase >= 0 ? s.EfgPhase : EfgLat34(k) ? EfgPhaseLat34(k) : s.EfgMode == 1 ? 0 : (k - 1) / (double)k;
            return Math.Max(0, Math.Min((k - 1) / (double)k, ph));
        }

        // (1.3.1, INTEGRA8) x4 / x3 baja latencia con la real mas tarde (efg_retraso_lat4 de serie 0,25; efg_retraso_lat3 de serie -1)
        double EfgPhaseLat34(int k) { return k >= 4 ? s.EfgPhaseLat4 : k == 3 ? s.EfgPhaseLat3 : -1; }
        bool EfgLat34(int k) { return s.EfgPhase < 0 && s.EfgMode == 1 && EfgPhaseLat34(k) > 0 && (s.EfgPhaseLatD5 || !s.Nr || nrBroken); }

        // ¿el instante de la real es el del modo (calidad (k-1)/k, latencia 0)? El ritmo por el contenido (EFGCAL) solo con esos
        bool EfgPhaseOfMode(int k) { return s.EfgPhase < 0 && !EfgLat34(k); }

        string EfgBench()
        {
            if (s.Efg < 2) return "";
            return "\tefg=x" + s.Efg + " modo=" + (s.EfgMode == 1 ? "latencia" : "calidad") + " reales=" + efgReals + " inventadas=" + efgGens + " repetidas=" + efgDups +
                   " tarde=" + efgLate + " saltadas=" + efgSkipped + " L=" + efgLcur.ToString("0.0", IC) + "ms flujo=" + efgFlowGpu.ToString("0.00", IC) +
                   "ms inventada=" + efgGenGpu.ToString("0.00", IC) + "ms copia=" + efgCopyMs.ToString("0.00", IC) + "ms" + (efgQueue != null ? " cola=propia" : " cola=motor") +
                   " ensenadas=" + efgShows + (efgBroken ? " rota" : "");
        }
    }
}
