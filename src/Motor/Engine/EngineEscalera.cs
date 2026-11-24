using System;
using System.Globalization;

namespace ConsoleEnhancer.Motor
{
    // EngineEscalera.cs - 23-11-2026 (1.3.1, agente FPS). El usuario, tras jugar con la 1.3: "haz que detecte mas rapidamente cuando los fps
    // no son los fps objetivo" y "cuando no quepa el fg antes que intentar bajar la cantidad de multiplicacion fg baja la resolucion del dlss 5
    // o el rescalado dlss bajalo".
    //
    // Lo que pasaba (su log del 23-11 20:14-20:16): (1) EngineFps.cs avisaba al lanzador con 5 de 7 s malos y 10 s de calma tras cada cambio,
    // y el lanzador (FpsBajo.cs), con Smooth Motion x4 / Enhanced FG x3-x4, pasaba DIRECTO a x2 aunque el motor siguiera bajando DLSS 5 (el
    // vigilante de x4 + DLSS 5, EngineX4D5.cs, a -10 puntos cada ~9-10 s: 90 -> 80 -> 70 -> 60 -> 50 % en 37 s con 30 inventadas saltadas/s);
    // (2) ninguno de los dos miraba al reescalado DLSS (a 1440p + DLSS M a 4K es lo caro); (3) recien despertado el motor de la espera, la
    // calma no se reiniciaba (la clave era la misma): juzgaba con el hueco del despertar (-15 imagenes) y la otra imagen aun en marcha.
    //
    // Ahora, una sola escalera en el motor para todos los caminos (Smooth Motion x2 / x4, Enhanced FG x2-x4, XeSS FG del motor, sin
    // generacion en el 30 / 60 / 120):
    //   - Deteccion cada 250 ms (Stats.Cuarto): "perdidas" = imagenes distintas de la consola (sin repetidas, como mucho las del modo) que no
    //     se presentan, o las que el motor tira ("tarde"); con Enhanced FG, ademas, inventadas saltadas. Rapida: en los ultimos 1,5 s, 3 o mas
    //     cuartos con perdidas y al menos escalera_perdidas (2 a 30 fps; 3 a 60; 5 a 120) -> ~1,5 s. Lenta (lo poco pero seguido, 29,4 de 30):
    //     en los ultimos 4 s, 2 cuartos o mas y 2 perdidas o mas. Un tiron suelto (todo en uno o dos cuartos seguidos) no cuenta.
    //   - Sin juzgar: en espera, sin señal o sin imagen, sin el foco o con el lanzador cambiando de imagen (FocusHold), con el menu de ajustes
    //     abierto (switch2-vigila 0 del lanzador), y unos segundos tras despertar / coger el foco / cerrar el menu / cambiar algo (2,5-3 s), tras
    //     rehacer DLSS 5 (1,5 s desde que esta hecho) y tras cambiar el modelo de DLSS SR (3 s).
    //   - Cuando no cabe, un paso cada vez: (1) DLSS 5: el % de entrada a pasos grandes al principio (-30, -20, luego -10; escalera_pasos)
    //     hasta el 25 % y, ahi, una pasada menos; (2) el reescalado DLSS: el modelo mas ligero (L > M > K > E: es lo que libera grafica de
    //     verdad sin cambiar nada mas; el elegido se queda hasta que DLSS 5 ya esta en su suelo); (3) solo entonces "switch2-fps bajo ...
    //     agotado" al lanzador, que baja el multiplicador SOLO esta sesion (nunca en el perfil del juego) o, sin generacion, lo de siempre.
    //   - Lo bajado se guarda en lo aprendido de adapta (EngineAdapta.cs: pct / pasadas / preset con bajada=1) y la proxima vez se empieza
    //     ahi. Subir: lo hace AdaptaSubir (de uno en uno, tras sobrar grafica), y con generacion solo tras escalera_subir_s (30) sin una perdida;
    //     si lo subido no cabe en menos de 40 s, se vuelve a lo de antes y ese valor queda de techo de la clave (no se vuelve a probar: sin
    //     oscilar).
    //   - Aviso al lanzador cuando lo bajado ya cabe (6 s sin perdidas): "switch2-escalera" <pct|-> <modelo|-> <pct pedido> <modelo pedido>
    //     <pasadas>; el lanzador lo dice en pantalla (una vez por estado).
    // Claves: escalera=0 (lo de la 1.3: EngineFps 5 de 7 s y EngineX4D5), escalera_pasos=30,20,10, escalera_modelo=0 (sin bajar el modelo de
    // DLSS SR), escalera_perdidas=N (a 30 fps; se escala con el modo), escalera_subir_s=30, escalera_log=1 (cada cuarto al registro, pruebas).
    partial class Settings
    {
        public bool Escalera = true;
        public int[] EscPasos = { 30, 20, 10 };
        public bool EscModelo = true;
        public bool EscFsr1 = true;
        public int EscPerdidas = 2;
        public int EscSubirS = 30;
        public bool EscLog = false;
        // (1.3.2, FPS) solo con la grafica entera llena (NVML, EngineGpuUso.cs): escalera_grafica_uso=0 = sin mirarla (como la 1.3.1);
        // escalera_grafica_llena = el % a partir del cual va llena (lo mas alto de las ultimas 2,5 s)
        public bool EscGpuUso = true;
        public int EscGpuLlena = 90;
        // (1.3.2, FPS; el usuario: "el dlss 5 a 1440p evita que sea menor que 33%") el suelo de DLSS 5 en pixeles de ancho: nunca por debajo
        // de 845 de ancho (el 33 % de 2560) ni del 25 %: 4K 25 % (960), 1440p 33 %, 1080p 44 %, 720p 66 %. 0 = el 25 % siempre (como la 1.3.1)
        public int NrSueloPx = 845;
        // (1.3.2, FPS) escalera_efg_p95: con Enhanced FG y solo inventadas saltadas, si la grafica del MOTOR por real (p95) pasa de este % del
        // tiempo de una real, la real tarda en estar lista (L) y bajar DLSS 5 / el modelo SI ayuda aunque la grafica entera no vaya llena
        public int EscEfgP95 = 40;

        bool ApplyEscalera(string key, string val)
        {
            int n;
            switch (key.Trim().ToLowerInvariant())
            {
                case "escalera": Escalera = val.Trim() != "0"; return true;
                case "escalera_modelo": EscModelo = val.Trim() != "0"; return true;
                case "escalera_fsr1": EscFsr1 = val.Trim() != "0"; return true;
                case "escalera_log": EscLog = val.Trim() == "1"; return true;
                case "escalera_grafica_uso": EscGpuUso = val.Trim() != "0"; return true;   // (1.3.2, FPS)
                case "escalera_grafica_llena": if (int.TryParse(val.Trim().TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) EscGpuLlena = Math.Max(0, Math.Min(100, n)); return true;
                case "escalera_efg_p95": if (int.TryParse(val.Trim().TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) EscEfgP95 = Math.Max(0, Math.Min(100, n)); return true;   // (1.3.2, FPS)
                case "dlss5_suelo_px": if (int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) NrSueloPx = Math.Max(0, Math.Min(3840, n)); return true;   // (1.3.2, FPS)
                case "escalera_perdidas": if (int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) EscPerdidas = Math.Max(1, Math.Min(10, n)); return true;
                case "escalera_subir_s": if (int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) EscSubirS = Math.Max(5, Math.Min(600, n)); return true;
                case "escalera_pasos":
                    {
                        System.Collections.Generic.List<int> l = new System.Collections.Generic.List<int>();
                        foreach (string p in val.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                            if (int.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) l.Add(Math.Max(5, Math.Min(75, n)));
                        if (l.Count > 0) EscPasos = l.ToArray();
                        return true;
                    }
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        const int EscQ = 16;                                   // 16 cuartos = 4 s
        readonly int[] escPres = new int[EscQ], escDist = new int[EscQ], escTarde = new int[EscQ], escSalt = new int[EscQ], escInv = new int[EscQ];
        readonly double[] escMs = new double[EscQ];
        int escPos, escN;                                      // ultimo cuarto escrito y cuantos hay validos desde la ultima calma
        long escCuartoAt, escCalmaHasta, escSk0 = -1, escGn0 = -1;
        string escClave = "";
        bool escEraEspera = true, escEraFoco, escPausa;
        long escPausaAt;
        int escPasosHechos;                                    // pasos de DLSS 5 bajados con esta clave (los primeros mas grandes)
        long escBajadaAt, escLimpioDesde, escAgotadoAt, escNrPedidoAt;
        int escNrEspera;                                       // 1 = esperando a que DLSS 5 se rehaga con lo bajado
        string escDicho = "";
        bool escFsr1, escFsr1NoVolver;                         // (2b) FSR 1 en vez de DLSS SR esta sesion del motor
        string escFsr1De = "";
        long escFsr1At, escFsr1VueltaAt;
        bool escBajo;                                          // hay algo bajado de lo pedido por la escalera (para el aviso)

        bool EscActiva { get { return s.Escalera; } }

        // switch2-vigila <0|1> (el lanzador: el menu de ajustes abierto / cerrado). 0 = no se juzga
        void EscVigila(bool on)
        {
            escPausa = !on;
            escPausaAt = Clock.Now;
            Log.Write("Escalera: " + (on ? "se vuelve a vigilar (menu de ajustes cerrado)" : "sin vigilar (menu de ajustes abierto)"));
        }

        void EscCalma(int ms)
        {
            escN = 0;
            long hasta = Clock.Now + (long)(ms / Clock.TickMs);
            if (hasta > escCalmaHasta) escCalmaHasta = hasta;
        }

        // (1.3.2, FPS; EFGRITMO: con Enhanced FG el ritmo -L- tarda en asentarse 2-5 s tras arrancar / despertar / cambiar: 60-240 saltadas que
        // no son de la grafica -23:59:24 del usuario: 9 de 192 en 2,1 s y x4 -> x2-) 4,5 s sin juzgar con Enhanced FG; 3 s sin el
        int EscCalmaInicioMs { get { return s.Efg >= 2 && EfgWanted() ? 4500 : 3000; } }

        double EscObjetivo { get { return s.Mode == 40 ? 39.96 : s.Mode == 30 ? 29.97 : s.Mode >= 100 ? 119.88 : 59.94; } }   // (1.3.2, MODO40: 39,96)

        // Desde SecondTick (cada vuelta del bucle): un cuarto cada 250 ms
        void EscCuarto()
        {
            if (!EscActiva) return;
            long now = Clock.Now;
            if (escCuartoAt == 0) { escCuartoAt = now; GpuUsoArranca(); return; }   // (1.3.2, FPS: la grafica entera, EngineGpuUso.cs)
            double dt = Clock.Ms(escCuartoAt, now);
            if (dt < 250) return;
            escCuartoAt = now;
            int cap, dup, pres, late; double gpuMax;
            stats.Cuarto(out cap, out dup, out pres, out late, out gpuMax);
            long sk = efgSkipped, gn = efgGens;
            int dSk = escSk0 < 0 || sk < escSk0 ? 0 : (int)(sk - escSk0), dGn = escGn0 < 0 || gn < escGn0 ? 0 : (int)(gn - escGn0);
            escSk0 = sk; escGn0 = gn;
            escRxMs = escRxMs * 0.85 + latSleepMs * 0.15;   // (1.3.2, FPS: lo que duerme Reflex, para decir por que)
            try { EscJuzga(now, dt, cap - dup, pres, late, dSk, dGn); }
            catch (Exception ex) { Log.Error("Escalera", ex); s.Escalera = false; }
        }

        void EscJuzga(long now, double dt, int distintas, int pres, int late, int dSk, int dGn)
        {
            // ---- cuando no se juzga (y la calma de despues) ----
            bool espera = inStandby || noSignalScreen || nosigSilencio || lastCapture == 0 || Clock.SinceMs(lastCapture) > 1500;
            if (espera) { escEraEspera = true; escN = 0; if (inStandby) escPausa = false; return; }   // (el menu era del motor de antes del relevo)
            if (escEraEspera) { escEraEspera = false; EscCalma(EscCalmaInicioMs); escSk0 = escGn0 = -1; }   // (recien despierto / vuelve la imagen)
            bool foco = !Program.FocusHold && Program.HasFocus();
            if (!foco) { escEraFoco = false; escN = 0; return; }
            if (!escEraFoco) { escEraFoco = true; EscCalma(2500); }                               // (relevo: delante y con el foco)
            if (escPausa && Clock.SinceMs(escPausaAt) > 180000) { escPausa = false; Log.Write("Escalera: el menu de ajustes lleva 3 min abierto: se vuelve a vigilar"); }
            if (escPausa) { escN = 0; return; }
            if (escPausaAt != 0 && Clock.SinceMs(escPausaAt) < 2000) { escN = 0; return; }
            string clave = s.Mode + "|" + (s.Fg ? 1 : 0) + "|" + s.FgTipo + "|sm" + s.SmoothMode + "|efg" + (EfgWanted() ? s.Efg : 0) + "|" + inW + "x" + inH + "|" + outW + "x" + outH + "|nr" + (AdNrActivo ? 1 : 0);
            if (clave != escClave)
            {
                // (escBajo: si se empieza por debajo de lo pedido -lo aprendido, x4d5_inicio-, tambien se dice cuando ya cabe)
                escClave = clave; escPasosHechos = 0; escDicho = ""; escBajo = true; escBajadaAt = now; escLimpioDesde = now; escNrEspera = 0;
                escD5Inutil = false; escUltPaso = ""; escUltMal = escUltP95 = 0;
                EscCalma(EscCalmaInicioMs);
                return;
            }
            if (escFsr1 && s.Upscaler != "fsr1") { escFsr1 = false; escFsr1VueltaAt = 0; }   // (el lanzador ha mandado otro escalador)
            // tras rehacer DLSS 5 con lo bajado: hasta que este hecho y 1,5 s mas (como mucho 4 s)
            if (escNrEspera == 1)
            {
                if (nrCreatedAt > escNrPedidoAt || Clock.SinceMs(escNrPedidoAt) > 4000 || !AdNrActivo) { escNrEspera = 0; EscCalma(1500); }
                escN = 0; return;
            }
            if (now < escCalmaHasta) { escN = 0; return; }
            if (nrCreatedAt != 0 && Clock.SinceMs(nrCreatedAt) < 1500) { escN = 0; return; }       // (DLSS 5 rehecho por otro: el guardian, adapta)
            if (presetSetAt != 0 && Clock.SinceMs(presetSetAt) < 3000) { escN = 0; return; }       // (modelo de DLSS SR recien puesto)
            if (adCambioAt != 0 && Clock.SinceMs(adCambioAt) < 2000) { escN = 0; return; }

            // ---- el cuarto ----
            double obj = EscObjetivo;
            bool efg = s.Efg >= 2 && EfgWanted();
            escPos = (escPos + 1) % EscQ;
            escPres[escPos] = pres; escDist[escPos] = distintas; escTarde[escPos] = late; escMs[escPos] = dt;
            escSalt[escPos] = efg ? dSk : 0; escInv[escPos] = efg ? dGn : 0;
            if (escN < EscQ) escN++;
            if (s.EscLog) Log.Write(string.Format(CultureInfo.InvariantCulture, "Escalera: cuarto {0:0} ms: distintas {1}, presentadas {2}, tarde {3}{4}", dt, distintas, pres, late, efg ? ", saltadas " + dSk + " de " + (dSk + dGn) : ""));

            // ---- ventanas: 1,5 s (rapida) y 4 s (lenta) ----
            // perdidas = lo que falta de presentar de lo que dio la consola (como mucho las del modo) Y que el motor tiro (tarde): la consola lenta
            // (sin tiradas) o un juego a 60 en el modo 30 (lo de mas no falta) no cuentan. Repartidas: 3 cuartos o mas con tiradas y separados
            // (del primero al ultimo 0,75 s en la rapida, 1,5 s en la lenta): un tiron suelto, aunque sea largo, no cuenta
            EscVentana v6 = EscSuma(6, obj), v16 = EscSuma(16, obj);
            double f = s.EscPerdidas / 2.0;
            bool rapida = escN >= 6 && v6.Falta >= Math.Max(2, Math.Ceiling(obj * 1.5 * 0.07 * f)) && v6.Tarde >= 2 && v6.CuartosTarde >= 3 && v6.Tramo >= 3;
            bool lenta = escN >= 8 && v16.Falta >= Math.Max(3, Math.Ceiling(obj * v16.Segs * 0.02 * f)) && v16.Tarde >= 2 && v16.CuartosTarde >= 3 && v16.Tramo >= 6;
            bool saltaMal6 = efg && escN >= 6 && v6.Salt > Math.Max(3, (v6.Salt + v6.Inv) * 0.04) && v6.CuartosSalt >= 3 && v6.TramoSalt >= 3;
            bool saltaMal16 = efg && escN >= 8 && v16.Salt > Math.Max(4, (v16.Salt + v16.Inv) * 0.03) && v16.CuartosSalt >= 3 && v16.TramoSalt >= 6;
            bool mal = rapida || lenta || saltaMal6 || saltaMal16;
            bool limpio = late == 0 && (!efg || dSk <= 1);
            if (!limpio) escLimpioDesde = now;

            if (!mal)
            {
                // (2b) con FSR 1 puesto por la escalera: 60 s sin perdidas y la grafica del motor holgada -> otra vez DLSS (una vez por sesion)
                if (escFsr1 && !escFsr1NoVolver && Clock.SinceMs(escFsr1At) > 60000 && Clock.SinceMs(escLimpioDesde) > 60000 && stats.LastGpuP95 > 0 && stats.LastGpuP95 < TopeGpu() * 0.55)
                {
                    Log.Write(string.Format(CultureInfo.InvariantCulture, "Escalera: sobra (60 s sin perdidas, grafica del motor p95 {0:0.0} ms): otra vez DLSS {1} en vez de FSR 1", stats.LastGpuP95, s.Preset));
                    s.Upscaler = escFsr1De.Length > 0 ? escFsr1De : "dlss"; escFsr1 = false; escFsr1VueltaAt = now; escDicho = "";
                    EscCalma(3000);
                    return;
                }
                // lo bajado ya cabe: el aviso (6 s sin perdidas desde la ultima bajada)
                if (escBajo && escBajadaAt != 0 && Clock.SinceMs(escBajadaAt) > 6000 && Clock.SinceMs(escLimpioDesde) > 6000) EscAvisoCabe();
                return;
            }
            EscVentana v = rapida || saltaMal6 ? v6 : v16;
            double reales = v.Segs > 0 ? v.Pres / v.Segs : 0;
            string por = string.Format(CultureInfo.InvariantCulture, "en {0:0.0} s: faltan {1:0} de {2:0} reales ({3:0.0}/s de {4:0.00}), {5} tiradas en {6} cuartos{7}; grafica del motor p95 {8:0.0} ms",
                v.Segs, v.Falta, v.Esperadas, reales, obj, v.Tarde, v.CuartosTarde, efg ? ", inventadas saltadas " + v.Salt + " de " + (v.Salt + v.Inv) : "", stats.LastGpuP95);
            // (lo que no ayuda: si el paso de DLSS 5 de antes bajo la grafica del motor un 25 % o mas y lo que se pierde por segundo no bajo ni
            // un 10 %, no es DLSS 5 lo que no cabe -la tele, la cola de la generacion...-: no se baja mas DLSS 5 y se pasa a lo siguiente)
            double malS = v.Segs > 0 ? (v.Falta + (efg ? v.Salt : 0)) / v.Segs : 0, p95 = stats.LastGpuP95;
            if (escUltPaso == "d5" && escUltMal > 0 && escUltP95 > 0 && p95 > 0 && p95 <= escUltP95 * 0.75 && malS >= escUltMal * 0.9 && !escD5Inutil)
            {
                escD5Inutil = true;
                Log.Write(string.Format(CultureInfo.InvariantCulture, "Escalera: bajar DLSS 5 no ha ayudado (grafica del motor {0:0.0} -> {1:0.0} ms, perdidas {2:0.0} -> {3:0.0}/s): no se baja mas DLSS 5 con esta clave",
                    escUltP95, p95, escUltMal, malS));
            }
            escVistas = efg && v.Segs > 0 ? (v.Pres + v.Inv) / v.Segs : 0;   // (las que se ven: con Enhanced FG, reales + inventadas no saltadas)
            // (1.3.2, FPS) solo si es la grafica: con la grafica entera sin llenarse (NVML) es el ritmo (Enhanced FG, la cola de la tele, el tope
            // de Reflex) o la fuente, y bajar DLSS 5, el reescalado o el multiplicador no lo arregla: no se toca nada y se dice (EscNoEsGrafica)
            if (EscNoEsGrafica(now, por, reales, v, efg)) return;
            escUltMal = malS; escUltP95 = p95;
            EscBajar(por, reales);
        }

        // (1.3.2, FPS) La sesion del usuario del 24-11 con la 1.3.1: "no llega" con la grafica sobrada -Enhanced FG x4 a 1080p con la grafica del
        // motor a 3,3 ms de 33 ("inventadas saltadas 10 de 135"), Smooth Motion x2 en el modo 60 con 58,0 de 59,94 reales a 4,3 ms de 16,7 (el
        // tope de Reflex con VRR)- y la escalera bajo DLSS 5, el modelo, FSR 1, el x4 y hasta DLSS 5 entero (guardado). Ahora, antes de bajar
        // nada: si la grafica ENTERA (EngineGpuUso.cs) no ha pasado de escalera_grafica_llena (90 %) en las ultimas 2,5 s y el motor cabe en su
        // tope, no es la grafica: nada se baja, se apunta (una vez cada 30 s por clave) y el lanzador lo dice en pantalla una vez ("switch2-fps"
        // "ritmo"). Sin NVML (otra grafica), como la 1.3.1
        long escRitmoAt;
        string escRitmoClave = "";
        int escRitmoN;
        double escRxMs;                                        // (Reflex: lo que duerme por imagen, media de los cuartos)
        bool EscNoEsGrafica(long now, string por, double reales, EscVentana v, bool efg)
        {
            if (!s.EscGpuUso || s.EscGpuLlena <= 0) return false;
            int max; double media;
            GpuUsoVentana(2500, out max, out media);
            double p95 = stats.LastGpuP95, tope = TopeGpu();
            if (max < 0)
            {
                // (sin NVML -otra grafica-; EFGRITMO) con Enhanced FG, si lo unico son inventadas saltadas -ni una real tirada- y la grafica del
                // motor mas la de Enhanced FG (flujo + las inventadas de una real) no llegan al 60 % del tiempo de una real, es su ritmo
                double periodo = 1000.0 / EscObjetivo, propio = efgFlowGpu + efgGenGpu * Math.Max(1, s.Efg - 1);
                if (!(efg && v.Falta <= 0 && v.Tarde == 0 && p95 > 0 && p95 + propio < periodo * 0.6)) return false;
                max = -1;
            }
            else if (max >= s.EscGpuLlena) return false;                 // (llena: es la grafica)
            if (p95 > 0 && p95 > tope) return false;                     // (el motor solo ya no cabe en su tope)
            // (Enhanced FG, sobre todo baja latencia: las inventadas se saltan cuando la real tarda en estar lista -L > ~17 ms-; con la grafica del
            // motor por real alta -DLSS 5 a 4K: 16-18 ms de 33- bajar DLSS 5 acorta L aunque la grafica entera vaya al 70 %: medido 24-11 03:28,
            // 4K + DLSS 5 al 65 % + x4 baja latencia, 68-73 % y 1 de cada 4 inventadas saltadas (90/s); en la 1.3.1 al 25 % cabia: 113,9/s)
            if (efg && v.Salt > v.Falta && s.EscEfgP95 > 0 && p95 >= 1000.0 / EscObjetivo * s.EscEfgP95 / 100.0) return false;
            escN = 0;
            EscCalma(2000);
            escRitmoN++;
            if (escRitmoClave == escClave && escRitmoAt != 0 && Clock.SinceMs(escRitmoAt) < 30000) return true;
            // por que, para el aviso: las inventadas de Enhanced FG saltadas (su ritmo), Reflex durmiendo (su tope con VRR) o el motor tirando reales
            // (Smooth Motion en el modo 60 con VRR: la tele no pasa de 116 -120 - 120^2/3600-, con o sin el dormir de Reflex: medido 24-11 03:30,
            // 1440p + DLSS 5, 58,0 reales y 116,1/s en la tele las dos veces; sin dormir, la latencia mediana 26 ms en vez de 12)
            string causa = efg && v.Salt > v.Falta ? "efg" : s.SmoothMode > 0 && (s.Mode == 60 || escRxMs >= 4) ? "reflex" : "tarde";
            double vistas = efg && v.Segs > 0 ? (v.Pres + v.Inv) / v.Segs : reales * (s.SmoothMode > 0 ? 2 : 1);
            Log.Write(string.Format(CultureInfo.InvariantCulture, "Escalera: no se llega ({0}) pero la grafica entera no va llena ({1} en 2,5 s; llena = {2} %{3}): no se baja nada ({4}){5}",
                por, GpuUsoTxt(2500), s.EscGpuLlena, s.SmoothMode > 0 ? ", Reflex duerme " + escRxMs.ToString("0.0", IC) + " ms por imagen" : "",
                causa == "efg" ? "el ritmo de Enhanced FG" : causa == "reflex" ? "el tope de 116 imagenes por segundo con VRR y Smooth Motion (Reflex / el driver)" : "el ritmo de la presentacion o la fuente",
                escRitmoN > 1 && escRitmoClave == escClave ? " [" + escRitmoN + " veces con esta clave]" : ""));
            escRitmoClave = escClave; escRitmoAt = now;
            Ev("switch2-fps", "ritmo", reales.ToString("0.0", IC), EscObjetivo.ToString("0.00", IC), p95.ToString("0.0", IC), Math.Max(0, max).ToString(IC), vistas.ToString("0.0", IC), causa);
            return true;
        }

        // (1.3.2, FPS) el suelo del % de DLSS 5 con esta entrada (NrSueloPx: 4K 25 %, 1440p 33 %, 1080p 44 %, 720p 66 %)
        int NrSuelo { get { return NrSueloPara(inW > 0 ? inW : s.CapW); } }
        int NrSueloPara(int w)
        {
            if (s.NrSueloPx <= 0 || w <= 0) return 25;
            return Math.Max(25, Math.Min(100, (int)Math.Round(100.0 * s.NrSueloPx / w)));
        }
        bool escD5Inutil;
        double escVistas;
        string escUltPaso = "";
        double escUltMal, escUltP95;

        struct EscVentana { public double Segs, Esperadas, Falta; public int Pres, Tarde, CuartosTarde, Tramo, Salt, Inv, CuartosSalt, TramoSalt; }

        EscVentana EscSuma(int n, double obj)
        {
            EscVentana v = new EscVentana();
            int m = Math.Min(n, escN), dist = 0, priT = -1, ultT = -1, priS = -1, ultS = -1;
            double ms = 0;
            for (int i = 0; i < m; i++)
            {
                int k = (escPos - i + EscQ) % EscQ;
                v.Pres += escPres[k]; dist += escDist[k]; ms += escMs[k];
                // (un cuarto "con tiradas" tiene que haber presentado algo: un paron entero -todo tirado- es un tiron, no falta de grafica)
                v.Tarde += escTarde[k];
                if (escTarde[k] > 0 && escPres[k] > 0) { v.CuartosTarde++; if (ultT < 0) ultT = i; priT = i; }
                v.Salt += escSalt[k]; v.Inv += escInv[k];
                if (escSalt[k] > 1) { v.CuartosSalt++; if (ultS < 0) ultS = i; priS = i; }
            }
            v.Segs = ms / 1000.0;
            v.Esperadas = Math.Min(dist, obj * v.Segs);
            v.Falta = Math.Max(0, Math.Floor(v.Esperadas - v.Pres - 0.5));   // (el borde de la ventana: una de menos)
            v.Tramo = priT >= 0 ? priT - ultT : 0;
            v.TramoSalt = priS >= 0 ? priS - ultS : 0;
            return v;
        }

        static string EscPrevio(string p) { return p == "L" ? "M" : p == "M" ? "K" : p == "K" || p == "J" ? "E" : null; }

        bool EscSrActivo { get { return dlssOk && scalerNow == "dlss" && (inW != outW || inH != outH) && !dlssBroken; } }

        // Un paso de la escalera: DLSS 5 (% y pasadas) -> el modelo de DLSS SR -> el lanzador (multiplicador esta sesion)
        void EscBajar(string por, double reales)
        {
            long now = Clock.Now;
            escN = 0;
            // (lo subido hace nada por adapta -AdaptaSubir- no cabe: de vuelta a lo de antes y ese valor de techo; no se baja nada mas ahora)
            if (s.Adapta && adTabla != null && adClave.Length > 0 && adSubida.Length > 0 && Clock.SinceMs(adSubidaAt) < 40000)
            {
                if (adSubida == "pct" && AdNrActivo && adSubidaDe >= NrSuelo && adSubidaDe < s.NrPct)
                {
                    AdaptaTecho("pct", s.NrPct);
                    Log.Write("Escalera: lo subido no cabe (" + por + "): DLSS 5 al " + s.NrPct + " % -> " + adSubidaDe + " % (lo de antes)");
                    s.NrPct = adSubidaDe;
                    adCambioAt = now; AdaptaGuardarFila();
                    escNrEspera = 1; escNrPedidoAt = now; escBajadaAt = now; escBajo = true; escDicho = ""; escUltPaso = "";
                    return;
                }
                if (adSubida == "preset" && adSubidaDePreset.Length > 0 && AdRango(adSubidaDePreset) < AdRango(s.Preset))
                {
                    AdaptaTecho("preset", s.Preset);
                    Log.Write("Escalera: lo subido no cabe (" + por + "): DLSS " + s.Preset + " -> " + adSubidaDePreset + " (lo de antes)");
                    s.Preset = adSubidaDePreset; presetCap[CapKey] = adSubidaDePreset; adVistoPreset = adSubidaDePreset;
                    adCambioAt = now; AdaptaGuardarFila();
                    EscCalma(3000); escBajadaAt = now; escBajo = true; escDicho = ""; escUltPaso = "";
                    return;
                }
            }
            if (escFsr1VueltaAt != 0 && Clock.SinceMs(escFsr1VueltaAt) < 40000 && EscSrActivo)
            {
                Log.Write("Escalera: la vuelta a DLSS no cabe (" + por + "): FSR 1 el resto de esta sesion del motor");
                s.Upscaler = "fsr1"; escFsr1 = true; escFsr1NoVolver = true; escFsr1At = now; escFsr1VueltaAt = 0;
                EscCalma(3000); escBajadaAt = now; escBajo = true; escDicho = "";
                return;
            }
            // (1) DLSS 5 (1.3.2, FPS: hasta su suelo con esta entrada -NrSuelo: 25 % a 4K, 33 % a 1440p, 44 % a 1080p-, no siempre el 25 %)
            int suelo = NrSuelo;
            if (AdNrActivo && (s.NrPct > suelo || s.NrPasses > 1) && !escD5Inutil)
            {
                escUltPaso = "d5";
                if (s.NrPct > suelo)
                {
                    int antes = Math.Min(100, s.NrPct);
                    int paso = s.EscPasos[Math.Min(escPasosHechos, s.EscPasos.Length - 1)];
                    int sig = antes - paso;
                    sig = (int)(Math.Floor(sig / 5.0) * 5);
                    if (sig < suelo + 10) sig = suelo;                      // (a 10 puntos del suelo o menos, al suelo de una vez)
                    sig = Math.Max(suelo, Math.Min(antes - 5, sig));
                    escPasosHechos++;
                    s.NrPct = sig;
                    Log.Write("Escalera: no se llega (" + por + "): DLSS 5 al " + antes + " % -> " + sig + " %" + (sig == suelo && suelo > 25 ? " (su suelo con esta entrada)" : ""));
                }
                else
                {
                    s.NrPasses--;
                    Log.Write("Escalera: no se llega ni con DLSS 5 al " + suelo + " % (" + por + "): DLSS 5 con " + s.NrPasses + " pasada(s)");
                }
                if (s.Adapta && adTabla != null && adClave.Length > 0) { adCambioAt = now; adSubida = ""; AdaptaGuardarFila(); }
                overSecs = 0;
                escNrEspera = 1; escNrPedidoAt = now; escBajadaAt = now; escBajo = true; escDicho = "";
                return;
            }
            // (2) el reescalado DLSS: el modelo mas ligero
            string sigP = EscPrevio(s.Preset);
            if (s.EscModelo && EscSrActivo && sigP != null)
            {
                escUltPaso = "sr";
                string antes = s.Preset;
                Log.Write("Escalera: no se llega (" + por + ")" + (AdNrActivo ? " con DLSS 5 en su suelo (" + suelo + " %, " + s.NrPasses + " pasada)" : "") + ": DLSS " + antes + " -> " + sigP + " (mas ligero)");
                s.Preset = sigP; presetCap[CapKey] = sigP; adVistoPreset = sigP;
                if (s.Adapta && adTabla != null && adClave.Length > 0) { adCambioAt = now; adSubida = ""; AdaptaGuardarFila(); }
                overSecs = 0;
                EscCalma(3000); escBajadaAt = now; escBajo = true; escDicho = "";
                return;
            }
            // (2b) sin DLSS SR esta sesion: FSR 1 (lo que mas libera del reescalado: medido 1440p -> 4K, DLSS M 11,4 ms de grafica del motor
            // frente a 6,8 con FSR 1; de M a E solo 0,4-2,2). Solo en el motor y esta sesion: lo elegido sigue siendo DLSS
            if (s.EscFsr1 && EscSrActivo && !escFsr1)
            {
                escUltPaso = "fsr1";
                Log.Write("Escalera: no se llega (" + por + ") ni con DLSS " + s.Preset + (AdNrActivo ? " y DLSS 5 al " + suelo + " %" : "") + ": FSR 1 en vez de DLSS esta sesion del motor (antes de bajar el multiplicador)");
                escFsr1De = s.Upscaler; s.Upscaler = "fsr1"; escFsr1 = true; escFsr1At = now;
                overSecs = 0;
                EscCalma(3000); escBajadaAt = now; escBajo = true; escDicho = "";
                return;
            }
            // (3) ya no queda nada en el motor: al lanzador (30 s como poco entre avisos, como EngineFps: casi siempre cambia de motor antes)
            if (escAgotadoAt != 0 && Clock.SinceMs(escAgotadoAt) < 30000) { EscCalma(2000); return; }
            escAgotadoAt = now; escUltPaso = "";
            string r = reales.ToString("0.0", IC), o = EscObjetivo.ToString("0.00", IC), g = stats.LastGpuP95.ToString("0.0", IC);
            string d5 = AdNrActivo ? s.NrPct.ToString(IC) : "-", m = escFsr1 ? "F" : EscSrActivo ? s.Preset : "-";
            Log.Write("Escalera: no se llega (" + por + ") y en el motor ya no queda nada que bajar (DLSS 5 " + (AdNrActivo ? "al " + d5 + " %" : "no") +
                      ", " + (m == "F" ? "FSR 1" : "DLSS SR " + (m == "-" ? "no" : m)) + "): aviso al lanzador");
            Ev("switch2-fps", "bajo", r, o, g, "agotado", d5, m, escVistas.ToString("0.0", IC));
            EscCalma(4000);
        }

        // Lo bajado ya cabe: al lanzador, una vez por estado (y solo si hay algo por debajo de lo pedido)
        void EscAvisoCabe()
        {
            string pedP = adPedidoPreset.Length > 0 ? adPedidoPreset : "";
            int pedPct = adPedidoPct > 0 ? adPedidoPct : -1;
            bool d5Bajo = AdNrActivo && pedPct > 0 && s.NrPct < pedPct;
            bool srBajo = escFsr1 || (EscSrActivo && pedP.Length > 0 && AdRango(s.Preset) < AdRango(pedP));
            if (!d5Bajo && !srBajo) { escBajo = false; return; }
            string sr = escFsr1 ? "F" : s.Preset;
            string estado = (d5Bajo ? s.NrPct.ToString(IC) : "-") + "|" + (srBajo ? sr : "-") + "|" + s.NrPasses;
            if (estado == escDicho) return;
            escDicho = estado;
            Log.Write("Escalera: cabe con " + (d5Bajo ? "DLSS 5 al " + s.NrPct + " % (pedido " + pedPct + " %)" : "") + (d5Bajo && srBajo ? " y " : "") +
                      (srBajo ? (escFsr1 ? "FSR 1 (pedido DLSS " + pedP + ")" : "DLSS " + s.Preset + " (pedido " + pedP + ")") : "") + ": aviso al lanzador");
            Ev("switch2-escalera", d5Bajo ? s.NrPct.ToString(IC) : "-", srBajo ? sr : "-", pedPct > 0 ? pedPct.ToString(IC) : "-", pedP.Length > 0 ? pedP : "-", s.NrPasses.ToString(IC));
        }

        // EngineAdapta (AdaptaTick, "sobra"): con generacion la grafica del motor no ve la de la generacion; solo se sube tras escalera_subir_s
        // sin una perdida (y nunca justo despues de bajar)
        bool EscDejaSubir()
        {
            if (!EscActiva) return true;
            bool gen = s.SmoothMode > 0 || xeFgOn || (s.Efg >= 2 && EfgWanted()) || ffxFgOn;
            if (!gen) return escBajadaAt == 0 || Clock.SinceMs(escBajadaAt) > 20000;
            return Clock.SinceMs(escLimpioDesde) > s.EscSubirS * 1000.0 && (escBajadaAt == 0 || Clock.SinceMs(escBajadaAt) > s.EscSubirS * 1000.0);
        }
    }
}
