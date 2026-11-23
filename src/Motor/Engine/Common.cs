using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace ConsoleEnhancer.Motor
{
    static class Log
    {
        static readonly object sync = new object();
        static StreamWriter w;
        public static string PathName;

        public static void Open(string path)
        {
            PathName = path;
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 4 * 1024 * 1024) File.Delete(path);
                w = new StreamWriter(path, true, new UTF8Encoding(false));
                w.AutoFlush = true;
            }
            catch
            {
                // 20-11 (CIERRES): engine.log abierto por otro motor (uno colgado que aun no ha salido): este escribe en engine_<pid>.log
                // (el de las 03:54:27 del 20-11 no dejo nada)
                w = null;
                try
                {
                    string alt = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_" + Process.GetCurrentProcess().Id + ".log");
                    w = new StreamWriter(alt, true, new UTF8Encoding(false));
                    w.AutoFlush = true;
                    PathName = alt;
                }
                catch { w = null; }
            }
        }

        public static void Write(string s)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + s;
            lock (sync)
            {
                try { if (w != null) w.WriteLine(line); } catch { }
                Console.WriteLine(line);
            }
        }

        public static void Error(string what, Exception ex) { Write("ERROR " + what + ": " + ex.GetType().Name + ": " + ex.Message + "\r\n" + ex.StackTrace); }
    }

    static class Clock
    {
        public static readonly double TickMs = 1000.0 / Stopwatch.Frequency;
        public static long Now { get { return Stopwatch.GetTimestamp(); } }
        public static double Ms(long from, long to) { return (to - from) * TickMs; }
        public static double SinceMs(long from) { return (Stopwatch.GetTimestamp() - from) * TickMs; }
    }

    // Ajustes: clave=valor de la linea de ordenes y de 'set' por la tuberia (20-11, CIERRES: partial, mas en EngineCuelgue.cs)
    partial class Settings   // (ConsoleEnhancer: claves propias en EngineAdapta.cs, ApplyCe)
    {
        public string Device = "";                  // 18-11 (MOTOR-CE): subcadena del nombre; "" (y sin device_path) = la primera capturadora que no parezca virtual (antes "GC573")
        // 18-11 (MOTOR-CE, ConsoleEnhancer: cualquier grafica y capturadora)
        public string DevicePath = "";              // device_path=<DevicePath de DirectShow>: exactamente ese dispositivo (manda sobre device=)
        public int Streamline = -1;                 // streamline=auto|0|1: auto (-1) = solo con grafica NVIDIA (0x10DE) y sl.interposer.dll junto al exe; 0 = nunca
        public long AdapterLuid = 0;                // adapter_luid=<hex> (como "0x" + Luid.ToString("X") de la deteccion): la grafica; 0 = la de alto rendimiento
        public string DataDir = "";                 // datos=<carpeta>: engine.log, logs de Streamline, cache de shaders y bancos relativos ("" = la del exe)
        public string Sharpen = "auto";             // enfoque=auto|cas|rcas: auto = rcas (el de FSR 1) tras fsr1, cas tras lo demas
        public int CapW = 2560, CapH = 1440;
        public int FpsNum = 60000, FpsDen = 1001;
        public string CapFormat = "auto";           // auto | rgb24 | rgb32 | yuy2 | nv12
        public string Test = "";                    // "" = capturadora; ruta de imagen o "barras" = fuente de prueba
        public int OutW = 0, OutH = 0;              // 18-11 (MOTOR-CE): 0 = el modo actual de la pantalla de la ventana (monitor=; antes 3840x2160 fijo)
        public string Upscaler = "auto";            // auto | dlss | fsr1 | catrom (18-11, MOTOR-CE: auto = dlss si esta, si no fsr1; antes "dlss" y sin DLSS catrom)
        public string Preset = "K";                 // E | K | L | M | default
        public double Sharp = 0.3;
        public bool Fg = false;
        // 20-11 (DLSS 5 EN EL MOTOR): DLSSNR (native\PrismaNr\PrismaNr.dll + la nvngx_dlssnr.dll de Magpie) sobre la captura, antes de
        // DLSS SR. dlss5=1; dlss5_nivel=intensidad,tono,estructura,piel; dlss5_estilo, dlss5_modelo (preset), dlss5_mascara, dlss5_interfaz,
        // dlss5_dir (carpeta de nvngx_dlssnr.dll)
        public bool Nr = false;
        public bool DlssLinear = false;             // dlss_lineal: DLSS SR en luz lineal FP16 (HDR + auto exposicion) (EngineLinear.cs)
        public float NrIntensity = 1.2f, NrTone = 1.2f, NrStructure = 1.0f, NrSkin = 0.6f;
        public int NrPasses = 1;                    // dlss5_pasadas: 1-3 (cada una otra DLSSNR sobre la salida de la anterior)
        public float[] NrPassStrength = { 1f, 0.6f, 0.5f };   // dlss5_fuerza_pasadas: el nivel de cada pasada x esto (con 2-3)
        public int NrAntiFlicker = 0;               // dlss5_antiparpadeo: 0 no, 1 sin vectores, 2 con vectores, 3 con vectores y soporte
        public float NrAntiFlickerMs = 80f;         // dlss5_antiparpadeo_ms: tiempo de la historia (exp(-t / esto))
        public int NrPct = 100;                     // dlss5_entrada: % de la entrada del motor con que trabaja DLSS 5 (25-100; el resto se suma ampliado)
        public float NrStrength = 1f;               // dlss5_fuerza: la "fuerza total" del nivel (residualMultiplier de Magpie): entrada + (DLSS 5 - entrada) x fuerza
        public bool NrBlendDirect = true;           // 20-11 (LATENCIA7) dlss5_mezcla_directa=0: la mezcla a una textura aparte y su copia a inColor (como antes)
        public int NrStyle = 2, NrPreset = 2;
        public bool NrMask = true, NrUi = true;
        public string NrDir = "";   // ConsoleEnhancer: la carpeta magpie de la app (el lanzador la pasa con dlss5_dir)
        public int Reflex = 1;                      // 0 off, 1 baja latencia, 2 + boost
        public int Mode = 60;                       // 60 | 30 | 120 (30: solo imagenes nuevas; 120: esports, captura a 120, sin generacion, minimo proceso)
        public bool Is120 { get { return Mode >= 120; } }   // 20-11 (HZ120): modo 120 fps de baja latencia
        public int Dedupe = -1;                     // -1 auto (modo 30 o con generacion), 0 no, 1 si
        public bool Motion = true;
        public bool Aa = true, Dedither = true;     // bordes (FXAA) y tramado de 5 bits, como scripts\s2_aa.glsl de mpv
        public bool MvLog = false;                  // diagnostico de los vectores al log                  // vectores de movimiento propios (si no, 0)
        public string Ipc = "";                     // tuberia al estilo de mpv para el lanzador ("" = no)
        public string Flag = "";                    // fichero del aviso de apagado (como el script de mpv)
        public bool Power = true;
        public bool PowerTest = false;
        public bool AutoPreset = true;              // si el modelo de DLSS no cabe en el tiempo del modo, uno mas ligero
        public bool TestScroll = true;
        public bool TestHud = false;
        public string TestOverlay = "";             // capa fija con alfa sobre la fuente de prueba (test_capa=<png>)                // la fuente de prueba con el HUD quieto sobre el fondo que se mueve              // la fuente de prueba se desplaza (test_quieta=1: imagen quieta)              // la vigilancia de apagado tambien con la fuente de prueba (para probarla)                   // vigilancia de apagado (con Ipc)
        public string TestHolds = "";               // 16-11 (tirones): test_cadencia=30|caida|par|mezcla|alterno|20|45|60|<lista "2,2,3,1">: cadencia del juego de prueba
        public int TestGrabMs = 0;                  // 16-11 (tirones): test_grab_ms=1200: una captura "light" cada tanto, como el lanzador jugando (sin lanzador)
        public bool AsyncCopy = true;               // 16-11 (1440p): copia_asincrona=0: la descompresion lee la memoria de subida por PCIe (como antes)
        public int CopyBands = 4;                   // 20-11 (LATENCIA7) copia_bandas=N: copia y subida de la captura por N bandas desde su hilo (EngineCopia.cs); 0 = como antes
        public bool TestYuy2 = false;               // 20-11 (LATENCIA7) test_yuy2=1 (solo pruebas): la fuente de prueba en YUY2, como la PS5 a 4K
        public bool DupThreads = true;              // 20-11 (LATENCIA7) repetidas_hilos=0: la comparacion de repetidas con un hilo (como antes; EngineCopia.cs)
        public bool BandsGpu = true;                // 20-11 (LATENCIA7) bandas_grafica=1 (con copia_bandas): descompresion y bordes banda a banda segun suben (EngineCopia.cs)
        public string TestDump = "";                // 16-11 (prueba): test_volcado=<fichero>: la imagen descomprimida numero 120 a un fichero
        public bool KeepAwake = true;               // 16-11 (E): pantalla_despierta=0: Windows puede apagar la pantalla por inactividad jugando
        public int PowerExit = 42;                  // codigo de salida al apagarse la consola (el de mpv + script)
        public bool Vsync = true;
        public bool VsyncFg = true;
        public bool ReflexFgForce = true;           // 17-11 16:45 reflex_fg=0: con la generacion, Reflex el del ini (antes siempre encendido)                 // 17-11 15:45 vsync_fg=0: sin sincronizacion solo con la generacion (Reflex la limitaba a 58 reales)
        public bool ShowStats = false;
        public string Pipe = "consoleenhancer-engine";   // 18-11 (MOTOR-CE): antes "switch2engine"
        public int Monitor = -1;                    // -1 = la pantalla principal
        public bool Topmost = true;
        public int BenchSecs = 0;                   // >0: medir N s, escribir bench y salir
        public int BenchWarm = 6;
        public string BenchOut = "";
        public string BenchLabel = "";
        // 16-11: standby=1 = arrancar en espera (todo iniciado, sin abrir la captura ni enseñar la ventana; se empieza con
        // switch2-wake). Ver docs\STANDBY.md
        public bool Standby = false;
        // 16-11: "No Signal" confirmada se da por apagada si dura esto (como nosig_hold_ms / nosig_hold_launch_ms del script de mpv;
        // apagado_espera_s / apagado_espera_arranque_s del switch2.ini). Arrancando un juego (switch2-launch), la larga. 16-11 tarde
        // (APAGADO): 3 s (antes 6), como el lanzador
        public double NosigHoldMs = 3000, NosigHoldLaunchMs = 25000;
        // 18-11 (CONSOLAS): negro y en silencio seguidos estos segundos = consola apagada (como black_secs del script de mpv). El lanzador
        // manda 86400 con una PlayStation (ese negro es el HDCP: lo avisa el lanzador y no se cierra)
        public int BlackSecs = 30;
        // 16-11 (calidad, agente E2; ver EngineMotion.cs, con lo medido): todo apagado por defecto, se enciende con motor_extra=
        // (recomendado: mvpx=1 mvexacto=0.1 mvsesgo=0.25 mvruido=1; mvmask, mvflat y fgui no mejoran nada medible)
        public bool MvFlat = false;                 // mvflat=1: bloques lisos con el mejor candidato en 16x16 (antes vector 0)
        public bool MvPx = false;                   // mvpx=1: vectores por punto en los bordes entre movimientos distintos
        public bool MvPxAuto = true;                // 16-11 (E, 1440p): mvpx_auto=0: mvpx tambien con generacion a 1440p o mas (de serie se quita ahi, no cabe)
        public int MvMask = 0;                      // mvmask: DLSS sin historia, suma de 1 (el vector no explica el bloque), 2 (borde quieto/movimiento), 4 (quieto N imagenes)
        public int MvMaskSides = 2;                 // mvmask_lados=1: solo el lado quieto del borde (2: los dos)
        public int MvMaskStill = 6;                 // mvmask_quieto: imagenes quieto para la regla 4
        public int MvMaskMode = 0;                  // mvmask_modo: 0 = pista BiasCurrentColorHint (la guia de DLSS: solo modelo F), 1 = vectores de DLSS SR fuera de la imagen
        public double MvMaskSad = 0.02, MvMaskK = 1.0, MvMaskPx = 0.04;   // mvmask_sad, mvmask_k, mvmask_px (con mvpx)
        public double MvZeroBias = 0.03, MvNoise = 0;   // mvsesgo (relativa) y mvruido (niveles por punto): preferencia por el vector 0 en el nivel fino
        public double MvPxMargin = 0;   // 17-11 (rejas): mvpx_margen=N niveles: mvpx solo cambia el vector de un punto si el nuevo explica su 3x3 N niveles mejor; 0 = como antes
        public double MvSmooth = 0;   // 17-11 (rejas): mvsuave=R media 3x3 de los vectores de bloque si todos estan a <= R px; 0 = como antes (17-11 REJAS: con mvconsenso/mvapertura, peor: 0)
        public double MvLong = 0;   // 17-11 (rejas): mvlargo=0.002 castigo por pixel de desplazamiento en la busqueda (luma por punto); 0 = como antes
        public bool MvFixed = false; public double MvFixedX = 0, MvFixedY = 0;   // 17-11 (rejas, prueba): mvfijo=x,y vectores fijos
        public double MvExact = 0;                  // mvexacto=0.1: sin medio pixel cuando el entero casa (SAD < 0,1 x el de sus vecinos); 0 = como antes
        // 17-11 (REJAS, EngineRejas.cs). Pruebas: test=paralaje con test_par (cielo / reja) y test_vel; volcado=<carpeta> con volcado_n,
        // volcado_desde y volcado_rect (la salida 4K y los vectores, imagen a imagen, para medir contra la verdad); mvverdad=1 (vectores
        // de la verdad del paralaje), 2 (y profundidad), 3 (solo profundidad)
        public double TestPar = 2.2, TestVel = 1.0;
        public string DumpDir = "";
        public int DumpN = 60, DumpFrom = 240;
        public int DumpX = 1280, DumpY = 720, DumpW = 1280, DumpH = 720;
        public int MvTruth = 0;
        public bool MvTemporal = false;            // mvtemporal=1: candidato temporal en la busqueda (el vector del bloque en la imagen anterior)
        public double MvCons = 0; public double MvConsTol = 1.0;   // 17-11 (REJAS): mvconsenso=N (niveles; vecinos de 5x5), mvconsenso_tol (px)
        public bool MvCapas = false; public double MvCapasTie = 1.5;   // mvcapas=1, mvcapas_empate=N (niveles)
        public bool CutMeans = false;              // corte_medias=1: el corte de escena con medias por casilla (no puntos sueltos)
        // 20-11 (GIRO, CutGrid.cs / EngineGiro.cs): corte_giro=1 un giro rapido de la camara no es corte (se busca el desplazamiento que
        // explica el cambio; con medias como corte_medias=1); corte_giro_umbral (niveles, con el desplazamiento). mvglobal=1 ese giro como
        // candidato en el nivel grueso de los vectores (+-mvglobal_radio px a 1/8) y sin el castigo de mvlargo
        public bool CutTurn = true; public double CutTurnThr = 20;
        public bool MvGlobal = true; public int MvGlobalRadius = 4; public double MvGlobalMin = 64;   // mvglobal_min: solo con giros de N px de la entrada o mas
        public int CutTest = 0;                    // corte_prueba=N (prueba): un corte de escena cada N imagenes (0 = no)
        public bool MvLongCoarse = true;           // mvlargo_grueso=1: mvlargo no castiga lo que sigue al vector del nivel grueso (EngineGiro.cs)
        public int MvApert = 0;                    // mvapertura=R: cada componente, media pesada por su fiabilidad en los bloques de su capa a <= R
        public bool MvApertOutl = false;           // mvapertura_fuera=1: los bloques con 5 px de error en la componente que no se ve, a la media de su capa
        public bool MvConsStill = false;           // mvconsenso_quieto=1: el consenso no toca los bloques con vector 0
        public bool MvConsAxis = false;            // mvconsenso_eje=1: candidato con la vertical de la capa y la horizontal propia +-0,5
        public double MvApertAmb = 0;              // 17-11 12:50 (borrosidad) mvapertura_ambigua=A: mvapertura solo en la componente A veces menos fiable que la otra
        public double MvApertGate = 2.5;           // mvapertura_puerta=px: misma capa si el vector esta a <= esto (de serie 2,5)
        public int MvEns = 0; public double MvEnsEdge = 12, MvEnsFlat = 5;   // 17-11 13:20 mvensancha=R (px, 0 = no), mvensancha_borde / mvensancha_liso (niveles)
        public double MvTempBlend = 0;             // mvtemporal_mezcla=a (con mvtemporal y mvapertura): el vector, a de la imagen anterior si casi igual
        public bool TestLockstep = false; public long TestFrom = 0; public bool NoPresent = false;   // test_paso=1, test_desde=N, presentar=0 (pruebas con la tele apagada)
        public bool TestFine = false;              // test_fino=1 (Parallax.cs)
        public string TestTexture = "zoom_oot.png";   // 17-11 (borrosidad): test_textura=<png 4K> para test=zoom (18-11, MOTOR-CE: relativa a la carpeta del exe)
        public int TestZoomKind = 0;               // test_zoom=1: test=zoom solo con traslacion (comprobar la verdad)
        public string TestDir = "h";              // 22-11 (1.2.2, CALIDAD) test=finas: paneo h, v o hv (Finas.cs)
        public double RejasNear = 0.5, RejasFar = 0.9;   // prof_cerca / prof_lejos: profundidad de la reja y del cielo (mvverdad=2/3)
        public int TestNoise = 0;                   // test_ruido=N
        public int InW = 0, InH = 0;                // entrada=WxH: la captura reducida a esto antes de DLSS (0 = la captura tal cual)
        public double FlatDenoise = 0;              // liso=N (niveles, p. ej. 2.5): media 3x3 donde todo el 3x3 varia <= N niveles (ruido de la captura en lo plano)
        public string TestB = "";                   // test_b=<png>: la fuente de prueba alterna con esta imagen (pares reales de la capturadora)
        public int FgVec = 1;                       // 17-11: fg_vectores=0: DLSS-G con los vectores a cero (su propio flujo optico, como el DLSS FG de Magpie); DLSS SR sigue con los del motor. 13:40 (REJAS): 2 = suavizados (fg_vectores_radio), 3 = uno global (EngineRejas.cs RecordFgVec)
        public long MvLightPx = 0;                  // 17-11 17:35 mv_ligero=N: con N puntos de entrada o mas, vectores sin mvpx/mvflat/mvconsenso/mvapertura
        // 20-11 16:05 (DLSS5VEC): con DLSS 5 en el modo 30 los vectores completos aunque la entrada pase de mv_ligero (4K, 1440p): DLSS 5 y su
        // antiparpadeo siguen la historia con ellos y, ligeros, lo liso y con luz se quedaba a 0 al mover la camara (parpadeo; Magpie le
        // daba el flujo optico de NVIDIA). mv_ligero_dlss5=1 = como antes (ligeros tambien con DLSS 5)
        public bool MvLightDlss5 = false;
        // 20-11 noche (coordinador, visto por LATENCIA7): a 4K nativo SIN generacion no se calculaban vectores (nadie mas los usa) y DLSS 5 y
        // su antiparpadeo iban con vectores a 0 (mov_ms=0): el parpadeo de DLSS5VEC en el modo 30 sin FG. mv_nativo_dlss5=1 (de serie): con
        // DLSS 5 en el modo 30 se calculan tambien a entrada = salida (~+4 ms a 4K, de 31 de tope); 0 = como antes
        public bool MvNativeNr = true;
        // 20-11 (SR45): mv_completo_sr=lista de modos (30 30fg 60 60fg 120, "todos"; 0 = ninguno, como antes) en los que DLSS SR al reescalar
        // con la entrada de mv_ligero o mas (1440p) recibe los vectores completos (EngineMotion.cs MvFullForSr; la generacion sigue con los
        // ligeros). mv_completo_sr_guarda=0: sin volver solo a los ligeros cuando la grafica no cabe en el tope del modo.
        // mv_completo_sr_px=modelos (L de serie; "todos"; 0 = ninguno) que llevan mvpx en esos completos (con K/M mvpx da mas rastro)
        public int MvFullSr = 1 | 2 | 4 | 8;
        public bool MvSrGuard = true;
        public string MvSrPx = "L";
        static int MvModes(string v)
        {
            int m = 0;
            foreach (string t in v.ToLowerInvariant().Split(new char[] { ',', ' ', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                m |= t == "30" ? 1 : t == "30fg" ? 2 : t == "60" ? 4 : t == "60fg" ? 8 : t == "120" ? 16 : t == "todos" ? 31 : 0;
            return m;
        }
        public bool MvSubEq = false;                // 17-11 15:50 mvsub=1: fraccion de pixel con dos rectas (SAD en V) en vez de parabola
        public bool MvLongRel = false;              // 17-11 15:40 mvlargo_rel=1: mvlargo castiga la distancia al vector de la imagen anterior (no a 0)
        public int ReflexLimitUs = 0;               // 17-11 15:15 reflex_limite=us: limite de Reflex (0 = el suyo)
        public int FgDepth = 0;                     // 17-11 15:20 fg_profundidad=R: profundidad aproximada solo para DLSS-G (a <= R px de un borde fuerte, cerca; lo liso, lejos)
        public double FgDepthEdge = 24, FgDepthNear = 0.5, FgDepthFar = 0.99;   // fg_profundidad_borde (niveles de luma), _cerca, _lejos
        public bool FgVecSmooth = false;            // 17-11 15:10 fg_vectores_suave=1: fg_vectores=2 con interpolacion bilineal entre bloques (sin escalones de 8 px)
        public double FgVecGate = 0;                // 17-11 15:10 fg_vectores_puerta=px: fg_vectores=2, las muestras pesan menos cuanto mas se separan del vector del punto (0 = sin puerta)
        public int FgVecRad = 4;                    // 17-11 13:40 (REJAS) fg_vectores_radio=R: fg_vectores=2, (2R+1)^2 muestras cada 8 px
        public int FgVecFull = 0;                   // 20-11 (FGFALLOS) fg_vectores_completos=1: DLSS-G con los vectores completos por punto del motor donde DLSS 5 ya los hace (modo 30, entrada >= mv_ligero); 2: tambien donde haya mvpx (EngineMotion.cs FgVecFullNow)
        public bool FgFill = true;                  // 21-11 (FG21) fg_relleno=1: con fg_vectores_completos, lo liso con vector 0 sigue a lo de alrededor (solo DLSS-G; EngineFgRelleno.cs)
        public double FgFillFlat = 3, FgFillTie = 2;   // fg_relleno_liso (desviacion media de la luma del bloque, niveles), fg_relleno_empate (niveles)
        public int Chroma = 2;                      // 21-11 croma=0 vecino (antes: escalones en los bordes de color con YUY2/NV12), 1 lineal, 2 guiado por la luma (Shaders.cs Unpack)
        public bool FgGuard = false;                // 21-11 (BORDE) fg_borde=1 (de serie 0: no quitaba todos los halos en GTA; se usa fg_vectores=3): la generacion con 0 en los bordes entre movimientos distintos (los halos; EngineFgBorde.cs)
        public int FgGuardRad = 6; public double FgGuardDif = 6, FgGuardRel = 0.25;   // fg_borde_radio (bloques de 8), fg_borde_dif (px), fg_borde_rel
        public int FgFillRad = 48;                  // fg_relleno_radio (bloques de 8 px)
        // fgui=1: la generacion no deforma lo quieto con borde (UIAlpha); 2 UIColorAndAlpha; 3 NoWarpMask
        // (1.2.4 DLSSFG: 4 = la imagen sin la interfaz quieta como HUDLess, EngineFgHud.cs; 5 = y UIColorAndAlpha; 0 = como antes.)
        // 1.2.4 ronda 5 (FGCAL; el usuario: "el x2 tambien tiene sus fallitos"): DE SERIE 0 en todos los modos (como la 1.2.3). Medido con
        // imagen real (test=real*, RealPrueba.cs: capturas de la PS5 con un personaje que sigue a la camara, su interfaz real, avance de
        // camara, DLSS 5) en la tele, DLSS-G x2 a 4K en el modo 30: con fgui=4 el texto mejora poco (0,42 -> 0,29 niveles) pero el error de
        // toda la imagen sube en todas las escenas (+5 a +55 %: el fondo 0,12 -> 0,44) y salen INVENTADAS MALAS sueltas (error 3-4,6 frente
        // a <= 1,3 sin ella, ~1 cada 2-3 s: la busqueda por bloques que fgui enciende -con fgui=0 fg_solo_camara=0 tambien salen-). En la
        // escena del minimapa de DLSSFG gana en la interfaz (1,65 -> 0,74) y pierde en todo lo demas (todo 2,58 -> 3,09). fgui=4 en
        // motor_extra la vuelve a poner (con fgui_pegada=1 de serie: algo mejor, no quita las inventadas malas).
        int fgUiVal = -1;
        public int FgUi { get { return fgUiVal >= 0 ? fgUiVal : 0; } set { fgUiVal = value; } }
        public bool FgMenus = false;                // fg_menus=1: DLSS-G with eEnableFullscreenMenuDetection (no generated frames over a fullscreen menu)
        public bool FgUiRecomp = true;              // fgui_recomp=1: recomposicion de la interfaz de DLSS-G (1.2.4: de serie 1, con fgui=4)
        public int FgUiFrames = 4;                  // fgui_quieto: imagenes quieto
        public double FgUiMov = 0.3, FgUiEdge = 0.08;   // fgui_mov: fraccion de la escena que se mueve; fgui_borde: contraste del borde
        // 18-11 (LATENCIA, EngineLatencia.cs): medida y ritmo de los modos 30x2
        public string LatCsv = "";                  // lat_csv=<fichero>: una linea por imagen real (llegada, cogida, envio, Present, estadisticas de DXGI)
        public string Probe = "";                   // sonda=<fichero>: la fuente de prueba lleva el numero de imagen (banda de scripts\latencia.exe) y apunta n;g;qpc
        public int ProbeBand = 16;                  // sonda_banda=px (alto de cada fila de la banda, en px de la fuente)
        public bool ReflexSleep = true;             // reflex_dormir=0: sin slReflexSleep cuando no va DLSS-G (con DLSS-G siempre)
        public int QueueMax = -1;                   // cola_max=N (al arrancar): swapchain con espera (waitable) y como mucho N imagenes en cola; 0 = la de DXGI (3); 18-11 (MOTOR-CE): -1 = auto (1 sin Reflex, si no la de DXGI)
        public int Dedupe30 = 2;                    // repetidas_30=2: filtro del modo 30 que no pierde imagenes reales (el P2 de G1 en mpv, sin su regla 2); 3 = el P2 entero; 1 = el de antes
        public int OsdRepaintMs = 100;              // aviso_repinta_ms: con aviso en pantalla, repintar la ultima solo si no llega imagen real en N ms (0 = como antes: a los 20 ms)
        public int TestStallMs = 0, TestStallAt = 10; // test_paron=ms, test_paron_s=s: un paron del hilo de pintado (prueba de la cola)
        public int SmoothMode = 0;                  // sm=0|1|2: Smooth Motion del driver puesto para este motor (lo dice el lanzador: 1 sincronizacion forzada, 2 cada 2 refrescos)
        public bool StandbyFreeDlss = false;        // espera_soltar_dlss=1: en espera tambien DLSS SR suelta lo suyo (NGX)
        public bool FgRetainStandby = false;        // fg_retener=1: en espera la generacion guarda sus recursos (~0,6 GB de la grafica) como antes del 18-11
        // 20-11 (LATENCIA4, EngineLatencia.cs): vrr=1 = tele con frecuencia variable (G-SYNC / FreeSync / HDMI VRR): cadena con
        // ALLOW_TEARING y Present(0, ALLOW_TEARING) sin generacion: cada imagen sale al acabarla, sin esperar al refresco ni "latido".
        // vrr_fg_vsync: con vrr=1 y la generacion, -1 = auto (sin sincronizacion si la salida cabe en la tele: modo 30; con ella en el 60),
        // 0 = nunca, 1 = siempre (lo de vsync_fg). vrr=0: como antes
        public int Vrr = 0;
        public int VrrFgVsync = -1;
        public bool RefreshFixed = false;           // 20-11 (LATENCIA5) refresco_fijo=1: el lanzador abrio este motor con G-SYNC apagado en su perfil (60 + DLSS FG); solo para el registro
        public double QueueDropMs = 10;             // cola_tirar_ms=N: con Smooth Motion (sm=1|2), si la imagen esperaria N ms o mas en la cola de la tele (modelo de EngineLatencia.cs), no se presenta (0 = nunca)

        static bool B(string v) { v = v.Trim().ToLowerInvariant(); return v == "1" || v == "true" || v == "si" || v == "on" || v == "yes"; }
        static int I(string v) { return int.Parse(v.Trim(), CultureInfo.InvariantCulture); }
        static double D(string v) { return double.Parse(v.Trim().Replace(',', '.'), CultureInfo.InvariantCulture); }

        // Devuelve false si la clave no existe
        public bool Apply(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "device": Device = val; break;
                // 18-11 (MOTOR-CE)
                case "device_path": DevicePath = val.Trim(); break;
                case "streamline": { string v = val.Trim().ToLowerInvariant(); Streamline = v == "auto" || v == "-1" || v == "" ? -1 : (B(v) ? 1 : 0); break; }
                case "adapter_luid":
                    {
                        string v = val.Trim();
                        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) v = v.Substring(2);
                        AdapterLuid = v.Length == 0 ? 0 : (long)ulong.Parse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture); break;
                    }
                case "datos": DataDir = val.Trim(); break;
                case "enfoque": Sharpen = val.Trim().ToLowerInvariant(); break;
                case "capture":
                case "captura":
                    {
                        string[] p = val.ToLowerInvariant().Split('x');
                        CapW = I(p[0]); CapH = I(p[1]); break;
                    }
                case "fps":
                    {
                        string[] p = val.Split('/');
                        FpsNum = I(p[0]); FpsDen = p.Length > 1 ? I(p[1]) : 1; break;
                    }
                case "format": CapFormat = val.Trim().ToLowerInvariant(); break;
                case "test": Test = val.Trim(); break;
                case "out":
                    {
                        string[] p = val.ToLowerInvariant().Split('x');
                        OutW = I(p[0]); OutH = I(p[1]); break;
                    }
                case "upscaler": Upscaler = val.Trim().ToLowerInvariant(); if (Upscaler == "fsr") Upscaler = "fsr1"; break;
                case "preset": Preset = val.Trim().ToUpperInvariant(); break;
                case "sharp": Sharp = D(val); break;
                case "fg": Fg = B(val); break;
                case "dlss5": Nr = B(val); break;
                case "dlss_lineal": DlssLinear = B(val); break;   // 20-11 (EngineLinear.cs)
                case "dlss5_nivel":
                    {
                        string[] pn = val.Split(',');
                        if (pn.Length >= 4) { NrIntensity = (float)D(pn[0]); NrTone = (float)D(pn[1]); NrStructure = (float)D(pn[2]); NrSkin = (float)D(pn[3]); }
                        break;
                    }
                case "dlss5_estilo": NrStyle = I(val); break;
                case "dlss5_fuerza": NrStrength = (float)Math.Max(0, Math.Min(2, D(val))); break;
                case "dlss5_entrada": NrPct = Math.Max(25, Math.Min(100, I(val))); break;
                case "dlss5_pasadas": NrPasses = Math.Max(1, Math.Min(3, I(val))); break;
                case "dlss5_fuerza_pasadas":
                    {
                        string[] pf = val.Split(',');
                        for (int i = 0; i < 3 && i < pf.Length; i++) NrPassStrength[i] = (float)Math.Max(0, Math.Min(2, D(pf[i])));
                        break;
                    }
                case "dlss5_antiparpadeo": NrAntiFlicker = Math.Max(0, Math.Min(3, I(val))); break;
                case "dlss5_antiparpadeo_ms": NrAntiFlickerMs = (float)Math.Max(10, Math.Min(1000, D(val))); break;
                case "dlss5_modelo": NrPreset = I(val); break;
                case "dlss5_mascara": NrMask = B(val); break;
                case "dlss5_interfaz": NrUi = B(val); break;
                case "dlss5_dir": NrDir = val.Trim(); break;
                case "dlss5_mezcla_directa": NrBlendDirect = B(val); break;   // 20-11 (LATENCIA7, EngineDlss5.cs)
                case "reflex": Reflex = I(val); break;
                case "mode": Mode = I(val); break;
                case "dedupe": Dedupe = val.Trim().ToLowerInvariant() == "auto" ? -1 : (B(val) ? 1 : 0); break;
                case "ipc": Ipc = val.Trim(); break;
                case "motion": Motion = B(val); break;
                case "aa": Aa = B(val); break;
                case "dedither": Dedither = B(val); break;
                case "mvlog": MvLog = B(val); break;
                case "flag": Flag = val.Trim(); break;
                case "power": Power = B(val); break;
                case "power_test": PowerTest = B(val); break;
                case "auto_preset": AutoPreset = B(val); break;
                case "test_quieta": TestScroll = !B(val); break;
                case "test_hud": TestHud = B(val); break;
                case "test_capa": TestOverlay = val.Trim(); break;
                case "test_cadencia": TestHolds = val.Trim().ToLowerInvariant(); break;
                case "test_grab_ms": TestGrabMs = I(val); break;
                case "copia_asincrona": AsyncCopy = B(val); break;
                case "copia_bandas": CopyBands = Math.Max(0, Math.Min(16, I(val))); break;   // 20-11 (LATENCIA7, EngineCopia.cs)
                case "test_yuy2": TestYuy2 = B(val); break;   // 20-11 (LATENCIA7, TestSource.cs)
                case "repetidas_hilos": DupThreads = B(val); break;   // 20-11 (LATENCIA7, EngineCopia.cs)
                case "bandas_grafica": BandsGpu = B(val); break;   // 20-11 (LATENCIA7, EngineCopia.cs)
                case "test_volcado": TestDump = val.Trim(); break;
                case "pantalla_despierta": KeepAwake = B(val); break;
                case "vsync": Vsync = B(val); break;
                case "vsync_fg": VsyncFg = B(val); break;
                case "reflex_fg": ReflexFgForce = B(val); break;
                case "stats": ShowStats = B(val); break;
                case "pipe": Pipe = val.Trim(); break;
                case "monitor": Monitor = I(val); break;
                case "topmost": Topmost = B(val); break;
                case "bench": BenchSecs = I(val); break;
                case "bench_warm": BenchWarm = I(val); break;
                case "bench_out": BenchOut = val.Trim(); break;
                case "bench_label": BenchLabel = val.Trim(); break;
                case "standby": Standby = B(val); break;
                case "nosig_hold_ms": NosigHoldMs = Math.Max(0, D(val)); break;
                case "nosig_hold_launch_ms": NosigHoldLaunchMs = Math.Max(0, D(val)); break;
                case "black_secs": BlackSecs = (int)Math.Max(1, Math.Min(int.MaxValue / 2, D(val))); break; // (18-11, CONSOLAS)
                case "mvflat": MvFlat = B(val); break;
                case "mvpx": MvPx = B(val); break;
                case "mvpx_auto": MvPxAuto = B(val); break;
                case "mvmask": MvMask = I(val); break;
                case "mvmask_lados": MvMaskSides = I(val); break;
                case "mvmask_sad": MvMaskSad = D(val); break;
                case "mvmask_quieto": MvMaskStill = I(val); break;
                case "mvmask_modo": MvMaskMode = I(val); break;
                case "mvsesgo": MvZeroBias = D(val); break;
                case "mvruido": MvNoise = D(val); break;
                case "mvexacto": MvExact = D(val); break;
                case "mvpx_margen": MvPxMargin = Math.Max(0, D(val)); break;
                case "mvsuave": MvSmooth = Math.Max(0, D(val)); break;
                case "mvlargo": MvLong = Math.Max(0, D(val)); break;
                case "mvfijo": { string[] pf = val.Split(','); if (pf.Length == 2) { MvFixed = true; MvFixedX = D(pf[0]); MvFixedY = D(pf[1]); } break; }   // 17-11 (rejas, PRUEBA)
                case "liso": FlatDenoise = Math.Max(0, D(val)); break;
                case "test_par": TestPar = D(val); break;            // 17-11 (REJAS)
                case "test_vel": TestVel = D(val); break;
                case "volcado": DumpDir = val.Trim(); break;
                case "volcado_n": DumpN = Math.Max(1, I(val)); break;
                case "volcado_desde": DumpFrom = Math.Max(1, I(val)); break;
                case "volcado_rect": { string[] pr = val.Split(','); DumpX = I(pr[0]); DumpY = I(pr[1]); DumpW = I(pr[2]); DumpH = I(pr[3]); break; }
                case "mvverdad": MvTruth = I(val); break;
                case "mvtemporal": MvTemporal = B(val); break;
                case "mvconsenso": MvCons = Math.Max(0, D(val)); break;
                case "mvconsenso_tol": MvConsTol = D(val); break;
                case "mvcapas": MvCapas = B(val); break;
                case "mvcapas_empate": MvCapasTie = D(val); break;
                case "corte_medias": CutMeans = B(val); break;
                case "corte_giro": CutTurn = B(val); break;                     // 20-11 (GIRO)
                case "corte_giro_umbral": CutTurnThr = Math.Max(0, D(val)); break;
                case "mvglobal": MvGlobal = B(val); break;
                case "mvglobal_radio": MvGlobalRadius = Math.Max(0, Math.Min(12, I(val))); break;
                case "mvglobal_min": MvGlobalMin = Math.Max(0, D(val)); break;
                case "corte_prueba": CutTest = Math.Max(0, I(val)); break;
                case "mvlargo_grueso": MvLongCoarse = B(val); break;
                case "test_giro": { string[] pg = val.Split(','); TestSource.GiroScrollX = I(pg[0]); TestSource.GiroScrollY = pg.Length > 1 ? I(pg[1]) : 0; break; }
                case "mvapertura": MvApert = Math.Max(0, I(val)); break;
                case "mvapertura_fuera": MvApertOutl = B(val); break;
                case "mvconsenso_quieto": MvConsStill = B(val); break;
                case "mvconsenso_eje": MvConsAxis = B(val); break;
                case "mvapertura_ambigua": MvApertAmb = Math.Max(0, Math.Min(1, D(val))); break;
                case "mvapertura_puerta": MvApertGate = Math.Max(0.1, D(val)); break;
                case "mvensancha": MvEns = Math.Max(0, I(val)); break;
                case "mvensancha_borde": MvEnsEdge = D(val); break;
                case "mvensancha_liso": MvEnsFlat = D(val); break;
                case "mvtemporal_mezcla": MvTempBlend = Math.Max(0, Math.Min(0.9, D(val))); break;
                case "test_paso": TestLockstep = B(val); break;
                case "test_fino": TestFine = B(val); break;
                case "test_textura": TestTexture = val.Trim(); break;
                case "test_zoom": TestZoomKind = I(val); break;
                case "test_dir": TestDir = val.Trim().ToLowerInvariant(); break;   // 22-11 (1.2.2, CALIDAD)
                case "test_desde": TestFrom = Math.Max(0, long.Parse(val.Trim(), CultureInfo.InvariantCulture)); break;
                case "presentar": NoPresent = !B(val); break;
                case "prof_cerca": RejasNear = D(val); break;
                case "prof_lejos": RejasFar = D(val); break;
                case "entrada":
                    {
                        string v = val.Trim().ToLowerInvariant();
                        if (v == "0" || v == "" || v == "no") { InW = InH = 0; break; }
                        string[] p = v.Split('x'); InW = I(p[0]); InH = I(p[1]); break;
                    }
                case "test_ruido": TestNoise = Math.Max(0, I(val)); break;
                case "test_b": TestB = val.Trim(); break;
                case "mvmask_k": MvMaskK = D(val); break;
                case "mvmask_px": MvMaskPx = D(val); break;
                case "fg_vectores": FgVec = I(val); break;
                case "fg_vectores_radio": FgVecRad = I(val); break;
                case "fg_vectores_suave": FgVecSmooth = B(val); break;
                case "fg_profundidad": FgDepth = Math.Max(0, I(val)); break;
                case "reflex_limite": ReflexLimitUs = Math.Max(0, I(val)); break;
                case "mvlargo_rel": MvLongRel = B(val); break;
                case "mvsub": MvSubEq = B(val); break;
                case "mv_ligero": MvLightPx = Math.Max(0, (long)D(val)); break;
                case "mv_completo_sr": MvFullSr = MvModes(val); break;   // 20-11 (SR45)
                case "mv_completo_sr_guarda": MvSrGuard = B(val); break;
                case "mv_completo_sr_px": MvSrPx = val.Trim().ToUpperInvariant(); break;
                case "mv_ligero_dlss5": MvLightDlss5 = B(val); break;
                case "mv_nativo_dlss5": MvNativeNr = B(val); break;   // 20-11 noche (coordinador)
                case "fg_profundidad_borde": FgDepthEdge = D(val); break;
                case "fg_profundidad_cerca": FgDepthNear = D(val); break;
                case "fg_profundidad_lejos": FgDepthFar = D(val); break;
                case "fg_vectores_puerta": FgVecGate = Math.Max(0, D(val)); break;
                case "fgui": FgUi = I(val); break;
                case "fgui_recomp": FgUiRecomp = B(val); break;
                case "fg_menus": FgMenus = B(val); break;
                case "fg_vectores_completos": FgVecFull = Math.Max(0, Math.Min(2, I(val))); break;   // 20-11 (FGFALLOS)
                case "fg_relleno": FgFill = B(val); break;   // 21-11 (FG21)
                case "croma": Chroma = Math.Max(0, Math.Min(2, I(val))); break;   // 21-11 (Shaders.cs Unpack)
                case "fg_borde": FgGuard = B(val); break;   // 21-11 (BORDE)
                case "fg_borde_radio": FgGuardRad = Math.Max(1, Math.Min(16, I(val))); break;
                case "fg_borde_dif": FgGuardDif = Math.Max(0, D(val)); break;
                case "fg_borde_rel": FgGuardRel = Math.Max(0, D(val)); break;
                case "fg_relleno_liso": FgFillFlat = Math.Max(0, D(val)); break;
                case "fg_relleno_empate": FgFillTie = Math.Max(0, D(val)); break;
                case "fg_relleno_radio": FgFillRad = Math.Max(1, Math.Min(128, I(val))); break;
                case "fgui_quieto": FgUiFrames = I(val); break;
                case "fgui_mov": FgUiMov = D(val); break;
                case "fgui_borde": FgUiEdge = D(val); break;
                case "lat_csv": LatCsv = val.Trim(); break;           // 18-11 (LATENCIA)
                case "sonda": Probe = val.Trim(); break;
                case "sonda_banda": ProbeBand = Math.Max(4, I(val)); break;
                case "reflex_dormir": ReflexSleep = B(val); break;
                case "cola_max": QueueMax = val.Trim().ToLowerInvariant() == "auto" ? -1 : Math.Max(-1, Math.Min(8, I(val))); break;
                case "repetidas_30": Dedupe30 = I(val); break;
                case "aviso_repinta_ms": OsdRepaintMs = Math.Max(0, I(val)); break;
                case "test_paron": TestStallMs = Math.Max(0, I(val)); break;
                case "test_paron_s": TestStallAt = Math.Max(1, I(val)); break;
                case "sm": SmoothMode = I(val); break;
                case "cola_tirar_ms": QueueDropMs = Math.Max(0, D(val)); break;
                case "fg_retener": FgRetainStandby = B(val); break;
                case "espera_soltar_dlss": StandbyFreeDlss = B(val); break;
                case "vrr": Vrr = B(val) ? 1 : 0; break;                       // 20-11 (LATENCIA4, EngineLatencia.cs)
                case "vrr_fg_vsync": VrrFgVsync = I(val); break;
                case "copia_hilos": Engine.CopyThreadsSet = Math.Max(0, Math.Min(16, I(val))); break;   // 20-11 (LATENCIA4)
                case "refresco_fijo": RefreshFixed = B(val); break;   // 20-11 (LATENCIA5)
                default: return ApplyCierres(key.Trim().ToLowerInvariant(), val) || ApplyCe(key, val) || ApplyLatencia(key.Trim().ToLowerInvariant(), val) || ApplyEnvio(key.Trim().ToLowerInvariant(), val) || ApplyFgGlobal(key, val) || ApplyFgMulti(key, val) || ApplyDvc(key, val) || ApplyFsrMulti(key, val) || ApplyArchivo(key.Trim().ToLowerInvariant(), val) || ApplyD5q(key, val);   // (1.2.4 arreglos, RELEVO: test_archivo_*, FileSource.cs; DLSS5Q: dlss5_*, EngineDlss5Q.cs)   // (1.2.4 MOTORFG: fg_mult, xefg_*: EngineFgMulti.cs; LATENCIA: cola_tirar_fg, cpu_pasos: EngineEnvio.cs; DLSS5LEGAL: vibrancia*: EngineDvc.cs) 20-11 (CIERRES: EngineCuelgue.cs) (ConsoleEnhancer: adapta=...; false = desconocida) (21-11, 1.2.1: EngineLatencia.cs; fg_global, fg_quieto: EngineFgGlobal.cs)
            }
            return true;
        }

        public void ApplyArgs(string[] args)
        {
            foreach (string a in args)
            {
                int i = a.IndexOf('=');
                if (i <= 0) continue;
                string k = a.Substring(0, i).TrimStart('-'), v = a.Substring(i + 1);
                try { if (!Apply(k, v)) Log.Write("Ajuste desconocido: " + a); }
                catch (Exception ex) { Log.Write("Ajuste no valido " + a + ": " + ex.Message); }
            }
        }

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "captura {0}x{1}@{2}/{3} {4}{5} -> {6}x{7} {8}{9} nitidez {10:0.00}, modo {11}{12}, FG {13}, Reflex {14}",
                CapW, CapH, FpsNum, FpsDen, CapFormat, Test.Length > 0 ? " (prueba: " + Test + ")" : "", OutW, OutH,
                Upscaler, Upscaler == "dlss" || Upscaler == "auto" ? " " + Preset : "", Sharp, Mode, Dedupe == 1 ? " sin repetidas" : Dedupe == 0 ? " con repetidas" : "",
                Fg ? "si" + (FgMult > 2 ? " x" + FgMult : "") + (FgTipo.Length > 0 ? " " + FgTipo : "") : "no", Reflex) + (SmoothMode > 0 ? ", Smooth Motion " + (SmoothMode == 2 ? "cada 2 refrescos" : "") + (QueueDropMs > 0 ? " (cola de la tele <= " + QueueDropMs.ToString("0", CultureInfo.InvariantCulture) + " ms)" : "") : "") + (Vrr > 0 ? ", VRR" : "") + QualityText();
        }

        // 16-11 (calidad): lo encendido de EngineMotion.cs ("" si nada)
        public string QualityText()
        {
            string r = "";
            if (MvFlat) r += " mvflat";
            if (MvPx) r += " mvpx";
            if (MvMask > 0) r += " mvmask=" + MvMask + ((MvMask & 2) != 0 ? " lados=" + MvMaskSides : "") + ((MvMask & 4) != 0 ? " quieto=" + MvMaskStill : "") + (MvMaskMode == 1 ? " modo=vectores" : "");
            if (MvZeroBias != 0.03 || MvNoise > 0) r += string.Format(CultureInfo.InvariantCulture, " mvsesgo={0:0.00} mvruido={1:0.0}", MvZeroBias, MvNoise);
            if (MvExact > 0) r += string.Format(CultureInfo.InvariantCulture, " mvexacto={0:0.00}", MvExact);
            if (InW > 0) r += " entrada=" + InW + "x" + InH;
            if (FlatDenoise > 0) r += string.Format(CultureInfo.InvariantCulture, " liso={0:0.0}", FlatDenoise);
            if (FgVec != 1) r += " fg_vectores=" + FgVec;
            if (FgDepth > 0) r += " fg_profundidad=" + FgDepth;
            if (FgUi > 0) r += " fgui=" + FgUi + (FgUiRecomp ? "+recomp" : "");
            if (FgMenus) r += " fg_menus";
            if (FgVecFull > 0) r += " fg_vectores_completos=" + FgVecFull + (FgFill ? " fg_relleno" : "");
            if (FgGuard) r += string.Format(CultureInfo.InvariantCulture, " fg_borde={0}/{1:0.#}/{2:0.##}", FgGuardRad, FgGuardDif, FgGuardRel);   // 21-11 (BORDE)   // 20-11 (FGFALLOS) (21-11 FG21: fg_relleno)
            if (MvTemporal) r += " mvtemporal";   // 17-11 (REJAS)
            if (MvCons > 0) r += string.Format(CultureInfo.InvariantCulture, " mvconsenso={0:0.0}", MvCons) + (MvConsAxis ? " mvconsenso_eje" : "");
            if (MvCapas) r += " mvcapas";
            if (MvEns > 0) r += " mvensancha=" + MvEns;
            if (MvApert > 0) r += " mvapertura=" + MvApert + (MvApertAmb > 0 ? string.Format(CultureInfo.InvariantCulture, " mvapertura_ambigua={0:0.00}", MvApertAmb) : "");
            if (CutMeans) r += " corte_medias";
            if (CutTurn) r += string.Format(CultureInfo.InvariantCulture, " corte_giro={0:0}", CutTurnThr);   // 20-11 (GIRO)
            if (MvGlobal) r += " mvglobal=" + MvGlobalRadius;
            if (CutTest > 0) r += " corte_prueba=" + CutTest;
            if (MvLongCoarse && MvLong > 0) r += " mvlargo_grueso";
            if (MvLong > 0) r += string.Format(CultureInfo.InvariantCulture, " mvlargo={0:0.000}", MvLong);
            if (MvSmooth > 0) r += string.Format(CultureInfo.InvariantCulture, " mvsuave={0:0.0}", MvSmooth);
            if (MvTruth > 0) r += " mvverdad=" + MvTruth;
            if (TestB.Length > 0) r += " test_b";
            return r.Length > 0 ? ", calidad:" + r : "";
        }
    }

    // Estadisticas por ventanas de 1 s (y acumuladas para el banco)
    class Stats
    {
        readonly object sync = new object();
        readonly List<double> frameMs = new List<double>();     // entre presentaciones de imagenes reales
        readonly List<double> latMs = new List<double>();       // llegada de la captura -> vuelta de Present
        readonly List<double> gpuMs = new List<double>();
        int captured, dups, presented, late;
        long windowStart = Clock.Now, lastPresent;
        public string Last = "";
        public double LastGpuP95, LastRate;         // del ultimo segundo (para bajar de modelo si no cabe)
        public int LastLate, LastDups;              // 16-11 (tirones): del ultimo segundo, para EngineTirones

        public bool Recording;
        readonly List<double> allFrame = new List<double>(), allLat = new List<double>(), allGpu = new List<double>();
        int allCaptured, allDups, allPresented;
        long recStart;

        public void Captured(bool dup) { lock (sync) { captured++; if (dup) dups++; if (Recording) { allCaptured++; if (dup) allDups++; } } }

        public void Presented(long arrival, long now, double gpu)
        {
            lock (sync)
            {
                presented++;
                double lat = Clock.Ms(arrival, now);
                latMs.Add(lat);
                if (gpu > 0) gpuMs.Add(gpu);
                if (lastPresent != 0)
                {
                    double f = Clock.Ms(lastPresent, now);
                    frameMs.Add(f);
                    if (Recording) allFrame.Add(f);
                }
                lastPresent = now;
                if (Recording) { allPresented++; allLat.Add(lat); if (gpu > 0) allGpu.Add(gpu); }
            }
        }

        public void Late() { lock (sync) late++; }

        static double Pct(List<double> v, double p)
        {
            if (v.Count == 0) return 0;
            double[] a = v.ToArray();
            Array.Sort(a);
            return a[Math.Min(a.Length - 1, (int)(p * a.Length))];
        }
        static double Avg(List<double> v) { double s = 0; foreach (double x in v) s += x; return v.Count > 0 ? s / v.Count : 0; }

        // Cada segundo: linea para el log / la tuberia
        public string Tick()
        {
            lock (sync)
            {
                double secs = Clock.SinceMs(windowStart) / 1000.0;
                if (secs < 1.0) return null;
                Last = string.Format(CultureInfo.InvariantCulture,
                    "entran {0:0.0}/s (repetidas {1}) presentadas {2:0.0}/s | intervalo p50 {3:0.0} p99 {4:0.0} ms | latencia media {5:0.0} p99 {6:0.0} ms | GPU {7:0.00} ms{8}",
                    captured / secs, dups, presented / secs, Pct(frameMs, 0.5), Pct(frameMs, 0.99), Avg(latMs), Pct(latMs, 0.99),
                    Avg(gpuMs), late > 0 ? " | tarde " + late : "");
                LastGpuP95 = Pct(gpuMs, 0.95);
                LastRate = presented / secs;
                LastLate = late; LastDups = dups;
                captured = dups = presented = late = 0;
                frameMs.Clear(); latMs.Clear(); gpuMs.Clear();
                windowStart = Clock.Now;
                return Last;
            }
        }

        // 16-11 (motor en espera): al despertar, como recien creado (sin el hueco de la espera en el primer segundo ni en el
        // primer intervalo entre imagenes)
        public void Restart()
        {
            lock (sync)
            {
                captured = dups = presented = late = 0;
                frameMs.Clear(); latMs.Clear(); gpuMs.Clear();
                windowStart = Clock.Now;
                lastPresent = 0;
                LastGpuP95 = 0; LastRate = 0;
            }
        }

        public void StartRecording() { lock (sync) { Recording = true; recStart = Clock.Now; allFrame.Clear(); allLat.Clear(); allGpu.Clear(); allCaptured = allDups = allPresented = 0; } }

        public string Summary(string label, Settings s)
        {
            lock (sync)
            {
                double secs = Clock.SinceMs(recStart) / 1000.0;
                return string.Format(CultureInfo.InvariantCulture,
                    "{0}\tetiqueta={1}\tentran={2:0.00}\trepetidas={3}\treales={4:0.00}\tp50={5:0.000}\tp95={6:0.000}\tp99={7:0.000}\tpeor={8:0.000}\tlatencia={9:0.00}\tlat_p99={10:0.00}\tgpu_ms={11:0.00}\tgpu_p99={12:0.00}\tperfil={13}",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), label,
                    allCaptured / secs, allDups, allPresented / secs, Pct(allFrame, 0.5), Pct(allFrame, 0.95), Pct(allFrame, 0.99),
                    Pct(allFrame, 1.0), Avg(allLat), Pct(allLat, 0.99), Avg(allGpu), Pct(allGpu, 0.99), s.Describe());
            }
        }
    }
}
