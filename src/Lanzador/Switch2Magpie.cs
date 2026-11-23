// Switch2Magpie.cs - Lanzador de bandeja (Prisma) para jugar a la consola por la capturadora, con Magpie (en lugar de Lossless Scaling).
// Nacio para la Switch 2; desde el 18-11 vale para cualquier consola (src\Consolas.cs, docs\CONSOLAS.md).
//
// Copia de la primera version de la app (que sigue igual, con Lossless Scaling). Lo mismo que aquella:
//   1. Lee/crea el ini (consoleenhancer.ini en la carpeta de datos)
//   2. Prepara su propio Magpie (magpie\ dentro de la app: ficheros propios desde el 18-11, ver Integrado.cs, con su
//      configuracion portatil) y escribe en ella un grupo de efectos y un perfil para la ventana de mpv
//   3. Lanza mpv con la capturadora (video + audio) y el script detector de apagado; Magpie escala su ventana
//   4. Al cerrarse mpv: si la consola se apago (o esta en reposo), la app se cierra y devuelve lo de segundo plano
// Lo de Magpie: DLSS 5 = DLSSNR AI Filter (con antiparpadeo por flujo optico y resolucion adaptativa), escalado DLSS SR con
// movimiento por flujo optico de NVIDIA (o RTX Video), DLSS Frame Generation x2-x4.
//
// Compilar con build.bat (csc de .NET Framework 4, sintaxis C# 5).
// Uso: Switch2Magpie.exe            -> modo normal (icono en la bandeja)
//      Switch2Magpie.exe --dry-run  -> prepara la copia de Magpie y registra la config que escribiria, sin lanzar nada
//        opcionales en dry-run: --dry-passes=N --dry-cap

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Xml;

namespace ConsoleEnhancer
{
    // ------------------------------------------------------------------
    // Rutas fijas
    // ------------------------------------------------------------------
    static class Paths
    {
        // 18-11 (INTEGRADO): la carpeta del exe si es una carpeta completa de la app (paquete de scripts\empaquetar.ps1); antes, en la app original, una carpeta fija
        // como siempre (los exe de prueba compilados en otra carpeta). Ver Integrado.FindAppDir. Ya no son const: static readonly
        public static readonly string AppDir = Integrado.FindAppDir();
        // ConsoleEnhancer: los datos (ini, log, games\) en Integrado.FindDataDir: <app>\datos (portable.txt) o %LOCALAPPDATA%\ConsoleEnhancer
        public static readonly string Datos = Integrado.FindDataDir(AppDir);
        // (16-11: solo pruebas, --ini=<ruta>: otro ini sin tocar el de verdad, que editan otros)
        public static string IniFile = Datos + @"\consoleenhancer.ini";
        public static readonly string LogFile = Datos + @"\logs\consoleenhancer.log";
        // (23-11, 1.2.4 SINMPV: sin los scripts de mpv -ce_power.lua, ce_diag.lua, ce_standby.lua-: el motor hace lo de ce_power.lua, PowerWatch.cs)

        public static string FlagFile
        {
            get { return Path.Combine(Path.GetTempPath(), "consoleenhancer_poweroff.flag"); }
        }

        // Suavizado de bordes de la imagen de la capturadora (shader de mpv)
        public static readonly string EdgeAaShader = AppDir + @"\scripts\s2_aa.glsl";
        // 10-11: el mismo con el tramado quitado mucho mas a fondo en lo plano (niebla, oscuros); va con DLSS 5 apagado (Config.EdgeShaderNow)
        public static readonly string EdgeAaShaderStrong = AppDir + @"\scripts\s2_aa_strong.glsl";
        public static readonly string StrongDitherFile = Datos + @"\games\strong_dither.txt"; // L3 del mando: 1 el filtro nuevo, 0 el de antes

        // Pin de audio de la capturadora con el que ya se ha oido la consola
        public static readonly string AudioPinFile = Datos + @"\games\audio_pin.txt";
        // Lo elegido con el mando: tamaño de captura y nivel de DLSS 5
        public static readonly string CaptureFile = Datos + @"\games\capture.txt";
        public static readonly string Dlss5LevelFile = Datos + @"\games\dlss5_level.txt";
        public static readonly string Dlss5OnFile = Datos + @"\games\dlss5_on.txt";  // DLSS 5 (DLSSNR de Magpie): 1 si, 0 no
        public static readonly string Dlss5PassesFile = Datos + @"\games\dlss5_passes.txt";
        public static readonly string ModeFile = Datos + @"\games\mode.txt";
        public static readonly string LastGameFile = Datos + @"\games\last_game.txt"; // el juego de la ultima vez (la app abierta jugando)
        public static readonly string FgFile = Datos + @"\games\framegen.txt";        // generacion de fotogramas (1 si, 0 no; boton de captura)
        // 19-11 (GENERACION): el tipo de generacion elegido con L3 (selector), por modo: "60=smooth" / "30=xess" (sin linea = el del ini)
        public static readonly string FgPickFile = Datos + @"\games\framegen_type.txt";
        // Las franjas fijas del HOME (arriba y la barra de iconos) de la ultima vez que se vio: sin ellas, abierta la app jugando,
        // cualquier texto azul del juego contaba como el HOME (02-11, TOTK)
        public static readonly string HomeRefFile = Datos + @"\games\home_ref.txt";

        // ---- (1.2.4 ronda 6, QUITAFG) las DLL de Intel XeSS y AMD FidelityFX que usa el motor (XeSS SR, XeSS FG + XeLL, FSR 3.1 / 4, FSR 3 FG) ----
        // Las baja la app de las releases oficiales de Intel (XeSS SDK) y AMD (FSR SDK), scripts\components.json; antes venian con Magpie
        public static readonly string XessDir = AppDir + @"\xess";
        public static readonly string FsrDir = AppDir + @"\fsr";
        // Lo que se le pasa al motor (dlls_sr=): las dos carpetas
        public static string SrDlls { get { return XessDir + ";" + FsrDir; } }
        // ¿Esta esa DLL de Intel / AMD para el motor?
        public static bool HayLib(string name)
        {
            return File.Exists(Path.Combine(name.StartsWith("amd_", StringComparison.OrdinalIgnoreCase) ? FsrDir : XessDir, name));
        }

        // ---- Magpie (1.2.4 ronda 6, QUITAFG: ya no se usa; solo para recoger de una instalacion de antes las DLL de Intel / AMD, Componentes.cs) ----
        // El Magpie de la app, con ficheros propios (18-11, INTEGRADO: antes enlaces duros al Magpie instalado aparte; ver Integrado.cs)
        // y su propia configuracion portatil (config\v4e\config.json junto al exe). Asi el Magpie del usuario, si lo hay, con sus perfiles
        // y efectos, no se toca nunca
        public static readonly string MagpieDir = AppDir + @"\magpie";
        public static readonly string MagpieExe = MagpieDir + @"\Magpie.exe";
        public static readonly string MagpieConfig = MagpieDir + @"\config\v4e\config.json";
        public static readonly string MagpieLog = MagpieDir + @"\logs\magpie.log";
        public static readonly string MagpieIssue = MagpieDir + @"\config\v4e\last-issue.json";

        // La configuracion normal del Magpie del usuario: de ella sale la primera config de la copia (sus atajos, refresco...)
        public static string UserMagpieConfig
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    @"Magpie\config\v4e\config.json");
            }
        }
    }

    // ------------------------------------------------------------------
    // Filtro de audio para el detector de apagado (lo usa el script Lua).
    // Una sola constante para poder cambiarla facilmente.
    // ------------------------------------------------------------------
    static class MpvConst
    {
        public const string AudioFilter =
            "--af=@s2lvl:lavfi=[astats=metadata=1:reset=1:measure_perchannel=none:measure_overall=RMS_level]";

        // Tuberia IPC de mpv (para enviarle el estado del audio de Streaming Center y el filtro de
        // los juegos a 30 fps). Una por cada mpv que se lanza: un mpv colgado en el driver de la
        // capturadora conserva la suya, y con el mismo nombre el mpv nuevo se quedaba sin IPC
        // (medido: FC 26 sin "una imagen de cada dos" -> tirones de la generacion). Pasa entre
        // sesiones y tambien dentro de una, al relanzar mpv tras un reinicio de la grafica
        static readonly string IpcBase = "ce-mpv-" + Process.GetCurrentProcess().Id;
        static int ipcLaunch;
        public static string IpcName { get { return IpcBase + "-" + ipcLaunch; } }
        public static string IpcPipe { get { return @"\\.\pipe\" + IpcName; } }
        // Nombre nuevo para el siguiente mpv
        public static void NextIpc() { Interlocked.Increment(ref ipcLaunch); }
        // 16-11 (motor_en_espera): la del motor arrancado ya en espera, con su propio contador (desde otro hilo: NextIpc + IpcName de
        // RunMpv no pueden cambiar entre medias)
        static int standbyLaunch;
        public static string StandbyIpcName() { return IpcBase + "-espera-" + Interlocked.Increment(ref standbyLaunch); }

        // Codigo de salida que usa el script Lua cuando detecta que la consola se ha apagado
        public const int PowerOffExitCode = 42;
        public const int ClosedByUserExitCode = 77;   // (1.0.11) la ventana de la imagen cerrada por el usuario: el motor y scripts\ce_power.lua
    }

    // ------------------------------------------------------------------
    // Reinicios del driver de la grafica (TDR: la GPU no responde en 2 s y Windows la reinicia).
    // Medido el 28/10 con FC 26 (16:31 y 20:51): nvlddmkm 153 en el registro System y volcado
    // WATCHDOG (LiveKernelEvent 141) a la vez que dejan de llegar fotogramas; mpv se queda
    // colgado y la app lo tomaba por la consola apagada (se cerraba y dejaba la ventana de mpv
    // pequeña en una esquina). El aviso llega a los 0-3 s del corte
    // ------------------------------------------------------------------
    static class GpuReset
    {
        // Hora del ultimo aviso de reinicio del driver desde 'since' (MinValue si no hay)
        public static DateTime Since(DateTime since)
        {
            try
            {
                long ms = Math.Max(1000, (long)(DateTime.Now - since).TotalMilliseconds);
                string q = "*[System[(Provider[@Name='nvlddmkm'] or (Provider[@Name='Display'] and EventID=4101))" +
                           " and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]";
                DateTime last = DateTime.MinValue;
                using (System.Diagnostics.Eventing.Reader.EventLogReader r = new System.Diagnostics.Eventing.Reader.EventLogReader(
                           new System.Diagnostics.Eventing.Reader.EventLogQuery("System",
                               System.Diagnostics.Eventing.Reader.PathType.LogName, q)))
                {
                    for (System.Diagnostics.Eventing.Reader.EventRecord e = r.ReadEvent(); e != null; e = r.ReadEvent())
                        using (e)
                            if (e.TimeCreated.HasValue && e.TimeCreated.Value > last) last = e.TimeCreated.Value;
                }
                return last;
            }
            catch (Exception ex)
            {
                Log.Error("Leyendo el registro de eventos (reinicios de la grafica)", ex);
                return DateTime.MinValue;
            }
        }
    }

    // ------------------------------------------------------------------
    // Registro (ConsoleEnhancer: logs\consoleenhancer.log en la carpeta de datos)
    // ------------------------------------------------------------------
    static class Log
    {
        static readonly object sync = new object();
        // 15-11: las pruebas sin consola (--test-entrada) se quedan con las lineas: nada en switch2.log
        public static volatile Action<string> Hook;

        public static void Write(string msg)
        {
            Action<string> hook = Hook;
            if (hook != null) { hook(msg); return; }
            try
            {
                lock (sync)
                {
                    Directory.CreateDirectory(Paths.AppDir);
                    File.AppendAllText(Paths.LogFile,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + "\r\n",
                        Encoding.UTF8);
                }
            }
            catch { /* el log nunca debe tumbar la aplicacion */ }
        }

        public static void Error(string msg, Exception ex)
        {
            Write("ERROR: " + msg + (ex != null ? " -> " + ex.GetType().Name + ": " + ex.Message : ""));
        }
    }

    // ------------------------------------------------------------------
    // Configuracion (switch2.ini, formato clave=valor, '#' o ';' = comentario)
    // (20-11, CIERRES: partial; las claves de los cierres en src\Cierres.cs)
    // ------------------------------------------------------------------
    partial class Config
    {
        public string Device = "";    // (ConsoleEnhancer: la elige el asistente, device= / device_path=)
        // La ventana de mpv va siempre a este tamaño y Magpie la sube a 4K
        public string VideoSize = "2560x1440";
        public string Framerate = "60000/1001";
        public string PixelFormat = "bgr24";
        // 18-11 19:10: a 4K la GC573 en bgr24 solo da 53,5 fps (sin ancho de banda; nv12 60,1, yuyv422 60,4): en el modo 30 mpv iba
        // a 37,5 ms (26,7 fps) a cualquier escala de DLSS 5. pixel_format_4k = el formato con captura 4K ("" = el de siempre)
        public string PixelFormat4k = "";   // (ConsoleEnhancer: de serie el de la capturadora; lo de la GC573 va en el ini si hace falta)
        public string PixelFormatNow { get { return CaptureNow == "3840x2160" && PixelFormat4k.Length > 0 ? PixelFormat4k : PixelFormat; } }
        public bool CaptureAudio = false;
        // El sonido sale de un mpv solo de audio (sin GPU). Streaming Center pinta la imagen de la capturadora
        // en una ventana oculta: medido, 430 MB de la grafica, picos de 65-71 ms y un 25 % de imagenes repetidas.
        // Se abre solo si por este camino no se oye nada, o con start_streaming_center=1 y console_audio=0
        public bool ConsoleAudio = true;
        // Canales que se piden a la capturadora: con 8 se oye el canal central (las voces) si la consola manda sonido envolvente
        public int AudioChannels = 8;
        public bool StartStreamingCenter = false;
        public string StreamingCenterPath = "";   // (ConsoleEnhancer: sin programa de un fabricante de serie)
        public string TestSource = ""; // solo pruebas: fuente de mpv en lugar de la capturadora
        public string TestSourceGame = ""; // solo pruebas: la fuente cuando se esta en un juego (test_source = el HOME)
        // 22-11 (1.2.4 arreglos, RELEVO) solo pruebas, con test_source: lo que se le pasa al motor como test= (de serie "barras"; p. ej.
        // archivo:C:\ruta\video.y4m, la fuente del motor que hace de capturadora: src\Motor\Engine\FileSource.cs). Al abrirlo y al
        // despertarlo (antes el despertar mandaba siempre test=barras y pisaba un test= de motor_extra)
        public string TestMotor = "";
        public string TestMotorArg { get { return "test=" + (TestMotor.Length > 0 ? TestMotor : "barras"); } }
        // HOME de la consola a 4K nativo y 60 fps, tal cual llega: captura a 4K, ventana de 4K y sin Magpie (ni escalado, ni
        // DLSS 5, ni generacion de imagenes, ni el filtro de repetidas ni el suavizado de bordes de mpv). Al entrar en un juego
        // vuelven sus ajustes (games\profiles.txt) y Magpie escala
        public bool HomeNativeEnabled = true;
        // 20-11 (PS5JUEGO): en las consolas con menu propio (PS5, Xbox Series), reconocer CADA JUEGO por su nombre (centro de control /
        // conmutador de la PS5, Guia / fichas de la Xbox) y recordarle su perfil, como el HOME de la Switch 2. 0 = un solo perfil para toda
        // la consola ("Consola", como hasta ahora). La Xbox esta SIN PROBAR con consola real: cae al perfil unico si no reconoce nada
        public bool Ps5RecognizeGames = true;
        public bool XboxRecognizeGames = true;
        public bool HomeNative = false;  // ahora se esta en el HOME a 4K nativo
        // Lo que se captura ahora: en el HOME, 4K; en un juego, la captura elegida para el (VideoSize)
        public string CaptureNow { get { return HomeNative ? "3840x2160" : VideoSize; } }
        // solo pruebas (16-11, G1): el filtro de imagenes de mpv tal cual en vez del de TrayApp.FrameFilter, tambien en los "vf set" en caliente
        // (p. ej. "1 de cada 2" para que la pantalla No Signal quieta llegue a 29,97 en el modo 30: test_mpv_args=--vf= no aguanta). ini test_filtro
        public string TestFrameFilter = "";
        public bool TestAudioSearch = false; // solo pruebas: con test_source, buscar el pin de audio como si no se conociera
        public bool BenchNoGrabs = false;    // solo bancos: sin capturas de la app jugando
        // Modo banco (ini banco_s=N): con Magpie ya presentando se espera banco_calentar_s, se mide N s (log de Magpie), se apunta una
        // linea en games\bench.txt (con banco_etiqueta) y la app se cierra sola, como al salir desde la bandeja
        public int BenchSecs = 0, BenchWarmSecs = 10;
        public int BenchReopens = 0;      // solo bancos (15-11): antes de medir, N reaperturas de Magpie como las del mando (tiempos de carga)
        public bool BenchEngine = false;  // solo bancos: las reaperturas son DLSS 5 si/no (con motor=propio: de Magpie al motor y vuelta)
        public string BenchLabel = "";
        // 10-11: diagnostico del tramado (ini diag_tramado = cuantas tandas por sesion, 0 = nada)
        public int DiagDither = 0;
        // 15-11: capturas del HOME con el nombre del juego sin leer o dudoso, a games\diag_home (ini diag_home = cuantas por sesion, 0 = nada)
        public int DiagHome = 12;
        public bool CloseOnPowerOff = true;
        // 19-11: al cerrarse la app por la consola apagada, apagar tambien el PC (ini apagar_pc=1; cerrarla a mano no lo apaga)
        public bool ShutdownPc = false;
        // 19-11 (ESCRITORIO): escritorio de Windows negro mientras la app esta abierta y al apagar el PC por la consola; el normal al
        // cerrarla a mano (src\Escritorio.cs). ini escritorio_negro=1; 0 = no tocar el escritorio nunca
        public bool BlackDesktop = false;
        // Con la grafica muy cargada mpv llego a estar 876 ms sin imagen (medido): con 800 la app creia que la consola se
        // habia apagado y se cerraba
        public int PowerOffStallMs = 1500;
        // 09-11: consola en espera, mas rapido (ver el script de mpv)
        public double NoSignalFastSecs = 0;
        public int NoSignalConfirmMs = 400;
        public int NoSignalConfirmSlowMs = 1000;
        // 16-11: "No Signal" tambien es un cambio de modo de video de la consola (arrancando RESIDENT EVIL requiem: la app se cerraba con
        // ella encendida). Se da por apagada si dura esto (ver TrayApp.NoSignalHold): arrancando un juego (cambio de modo esperado; el de
        // RESIDENT EVIL duro menos de 21 s) y si no. Tras el menu de apagado, sin esperar
        // 16-11 tarde (APAGADO): jugando 3 s (antes 6): en el log del 08-11 al 16-11, de ~30 "No Signal" de verdad tras ver la consola
        // todas fueron apagados (la mitad jugando, con el boton de encendido) menos los dos de RESIDENT EVIL arrancando (dentro de la
        // espera de arrancar) y ninguna volvio sola; cada segundo de mas retrasa todos esos cierres
        public double NoSignalHoldSecs = 3;
        public double NoSignalHoldLaunchSecs = 25;
        // 16-11 tarde (APAGADO): en el HOME sin nada arrancando, "No Signal" es la consola apagada o en espera: sin esperar (ini
        // apagado_espera_home_s). Recien vuelto de un juego, aun la espera de jugando durante apagado_home_tras_juego_s (un cambio de
        // modo de video al volver al HOME iria antes de verse el HOME o justo al verlo; los apagados del log llegan 2,2-4,9 s despues)
        public double NoSignalHoldHomeSecs = 0;
        public double NoSignalHomeAfterGameSecs = 1.5;
        // 16-11 tarde (APAGADO): tras elegir usuario o el aviso de cerrar el suspendido sin entrar en nada (cancelado): de vuelta en el
        // HOME con el cursor en otra casilla, o el HOME a la vista seguido esto (mas que el HOME de paso tras elegir usuario, 0,45-0,83 s
        // medido), ya no se esta arrancando nada (ini apagado_fin_arranque_s)
        public double LaunchCancelSecs = 2;
        // 16-11 tarde (APAGADO): el HOME atenuado se lee (OCR, ~60-150 ms) cada vez que sale y, si sigue, otra vez cada esto (el menu de
        // apagado aun entrando no se leia: una lectura por salida). Ini apagado_ocr_atenuado_s
        public double DimOcrEverySecs = 1;
        public bool PerformanceBoost = true;
        // 09-11: MSI Afterburner cerrado mientras la app esta abierta (afterburner_pausa). Ver la clase Afterburner
        public bool AfterburnerPause = false;   // (ConsoleEnhancer: de serie no)
        // Programas a los que se les baja la prioridad (CPU, GPU y EcoQoS) mientras la app esta abierta (segundo_plano=)
        public string[] Background = new string[0];   // (ConsoleEnhancer: vacio de serie)

        // =================================================================
        //  Magpie (en lugar de Lossless Scaling)
        // =================================================================
        // 18-11 (INTEGRADO): la app lleva su Magpie en magpie\ (ficheros propios). magpie_origen = opcional, una instalacion de Magpie de
        // la que coger lo mas nuevo (magpie_actualizar: 0 nunca, 1 lo mas nuevo, 2 igual que el origen). Ver Integrado.PrepareMagpie
        public string MagpieSource = "";
        public int MagpieUpdate = 1;
        // usar_carpetas_app: mpv\mpv.exe y engine\ConsoleEnhancerEngine.exe dentro de la app si estan (mpv_path y motor_ruta de respaldo)
        public bool AppFolders = true;

        // ---- Escalado de la ventana de mpv (2560x1440, o 4K) a la pantalla ----
        // dlss = DLSS Super Resolution de Magpie (modelo dlss_modelo); vsr = RTX Video Super Resolution (el escalador de video
        // de NVIDIA, hecho para imagen de video comprimida como la de una capturadora); xess = XeSS SR; fsr = FSR 1 (EASU + RCAS)
        public string Scaler = "dlss";
        // El de R3 del mando (antes: escalador DLSS / el de LS). Nunca NIS (el usuario no lo quiere)
        public string AltScaler = "fsr";   // (1.2.4 ronda 6, QUITAFG: antes "vsr", RTX Video de Magpie; el motor no lo hace: FSR 1)
        // Modelo de DLSS SR: 5 = E (DLSS 3, CNN), 10 = J, 11 = K (DLSS 4), 12 = L, 13 = M (DLSS 4.5). 11-11 (aceptado por el
        // usuario con LS): E en todo; K sin el jitter de un juego alisaba y arrastraba la textura suave. Con Magpie DLSS recibe
        // ademas movimiento medido por flujo optico (dlss_flujo): merece la pena volver a probar K o L
        public int DlssModel = 0;          // 0 = el de la tabla del modo (ModelTable); otro numero = ese en todos los modos
        // ---- Tablas por modo (14-11, MEDIDAS en la RTX 4060 con el banco: ver games\bench.txt) ----
        // Clave: "60" / "30" (modo) + "_fg" (con generacion) [+ "_4k" en las de DLSS 5: captura 4K, ventana de 4K, sin escalado]
        // Modelo de DLSS SR { sin DLSS 5, con DLSS 5 }: el de mas calidad que deja 60 (o 30) reales clavadas. 13 M y 12 L (DLSS 4.5),
        // 11 K (DLSS 4), 5 E (DLSS 3, CNN). ini tabla_modelo_<clave>=a,b
        public Dictionary<string, int[]> ModelTable = new Dictionary<string, int[]>
        {
            // 60 con FG: K sin DLSS 5 (60/120 clavados; L 44, M 46 reales), E con el (K con DLSS 5 al 30 %: 52). 60 sin FG: M (59,8);
            // con DLSS 5, E (K al 50 %: p99 27 ms). 30 con FG: M sin DLSS 5 (30/60, grafica 62 %); K con el (L: 25 reales). 30 sin FG: M
            // 14-11 noche: K en vez de M (agente de menus: M rompe el texto pequeno de los menus, digitos de 18 px partidos; K mucho menos y cuesta menos)
            // 15-11, JUGANDO (Ocarina): 60 con FG y DLSS 5 al 25 % Magpie solo llegaba a ~51 reales (19,5 ms por imagen; 43 con 3 pasadas): el juego real
            // cuesta 15-20 % mas que la imagen de prueba. 60 con FG va con E y sin DLSS 5 (la tabla de arriba lo quita y lo dice el aviso)
            // 15-11 tarde (exploracion, imagen de Ocarina): en los de 30 con DLSS 5, E y no M/K/L: con el flujo optico estos rompen los bordes
            // del HUD quieto sobre fondo en movimiento y parpadean mas en quieto (M 0,46, E 0,30); E cuesta la mitad (DLSS 5 al 85-100 %)
            { "60_fg", new[] { 5, 5 } }, { "60", new[] { 11, 5 } }, { "30_fg", new[] { 13, 5 } }, { "30", new[] { 13, 5 } }, // (15-11: M = transformer 2 en el modo 30 sin DLSS 5, donde sobra grafica: 62 % con FG; en los de 60 L/M no caben, L 53,8 reales)
            // 19-11 (GENERACION): XeSS FG / FSR 3 FG en el modo 30: L sin DLSS 5, E con el (cuestan 9-14 puntos mas que DLSS FG: ver Dlss5PctTable)
            { "30_xess", new[] { 12, 5, 5, 5 } }, { "30_fsr3", new[] { 12, 5, 5, 5 } },
            // 19-11 (agente GTA; el usuario: "he estado jugando GTA 6 en PS5 desde 1440 y 1080 con DLSS L y se veia mal, tenia fallos
            // visuales"): modo 30 con generacion y DLSS 5 a 1440p / 1080p / 720p (DLSS SR sin flujo optico: flujo_30_fg_dlss5=ninguno): K y no L.
            // L sin vectores trata cada imagen como quieta: la piel y la madera salen como de cera (detalle 6,0 frente a 7,6 de K y 6,7 de
            // lanczos con la imagen quieta) y en movimiento borra la textura y el grano (detalle movido/quieto 0,60; K 0,90). L con el flujo
            // de NVIDIA rompe el HUD (letras partidas, ya visto el 15-11); E sin flujo deja escalones y bloques en rejas y hojas. K ademas
            // cuesta menos (mas margen para DLSS 5 en la Ti con OC). Medidas: LEEME.txt (GTA-1440-1080). ini tabla_modelo_30_fg_1440= etc.
            // 23-11 (1.2.4 ronda 7, SRBLUR): con DLSS 5, M y no K. Lo de arriba era con DLSS SR SIN vectores (Magpie); en el motor van con los
            // suyos y, medido con video real 4K -> 1440p -> 4K (BF1 y Battlefront) contra DLSS 5 a 4K nativo, M queda mas cerca que K en todo:
            // quieto error 2,82 / 2,99, en marcha 1,87 / 2,04, casi quieto 2,31 / 2,75; nitidez 0,80 / 0,79, 0,76 / 0,68, 0,73 / 0,65. Sin DLSS 5,
            // K es el mas borroso en movimiento (0,67; M 0,75, L 0,79). Si M no cabe, el motor baja antes el % de DLSS 5 y luego el modelo
            { "30_fg_1440", new[] { 12, 13, 13, 13 } }, { "30_fg_1080", new[] { 12, 13, 13, 13 } }, { "30_fg_720", new[] { 12, 13, 13, 13 } },
        };
        // Resolucion de entrada de DLSS 5 (DLSSNR) en % { 1, 2, 3 pasadas }: la mayor que cabe. 0 = adaptativa. ini tabla_dlss5_<clave>=a,b,c
        public Dictionary<string, int[]> Dlss5PctTable = new Dictionary<string, int[]>
        {
            // -1 = no cabe con esas pasadas (se usan menos). Medido (14 s, imagen de prueba a 60/s): 60 FG 25 % = 60,00/119,9 (30 %: p99 29 ms,
            // 2 pasadas al 25 %: 55,8); 60 sin FG 50 % (peor imagen 19,9 ms; al 60-70 % algun tiron de 30-41 ms con el nivel 4), 2 pasadas 35 % = 59,9; 30 FG 100 % / 60 % / 50 % = 30/60;
            // 30 sin FG 100 / 65 / 50 = 30 (75 %: p99 47 ms) (2 pasadas al 100 %: 24). 4K: 60 FG no cabe ni al 25 % (47,7); 60 sin FG 25 %; 30 FG 50 % (70 %: grafica 98 %)
            // 15-11 tarde (exploracion con la imagen de Ocarina en movimiento, grafica <= 85 %): 60 FG 40 % (2 pasadas ni al 25 %: 83-88 %);
            // 60 sin FG 65 / 45 / 35 (E en 1 pasada, FSR en 2 y 3: D5ScalerTable); 30 FG 85 / 60 / 50 y 30 sin FG 100 / 80 / 65 (con E)
            { "60_fg", new[] { 40, -1, -1 } }, { "60", new[] { 45, 35, 25 } }, { "30_fg", new[] { 85, 60, 50 } }, { "30", new[] { 100, 80, 65 } }, // (16-11 G1: 60 sin FG con E y la capturadora de verdad, por DXGI (Config.VsyncNoFgNow): 45 / 35 / no cabe (3 al 25 %: 87 %); por DirectComposition era 40 / 30; con la imagen de prueba 65 / 40 / 30)
            // (16-11 16:15, G1: 60 sin FG con 3 pasadas al 25 % SI, con lo ligero de "60_p3" (DLSS SR y DLSS 5 sin flujo, sin AA de mpv, sin mascara,
            // sin antiparpadeo; ver D5AntiFlickerTable): capturadora de verdad, 2 pasadas en la misma tanda: 1440p 59,94 / 59,93 (min 59,92 / 59,75),
            // 79-83 % de grafica, 0 ventanas lentas; 1080p (con antiparpadeo 1) 59,94 / 59,94 (min 59,92 / 59,93), 82-84 %, 0 lentas)
            // 15-11 noche: 60 con Smooth Motion (generacion_tipo_60=smooth; misma cadena que 60 con FG: FSR, antiparpadeo 1...): 50 / 35 / 25 %
            // = grafica 74-78 / 77-81 / 76-82 % (DLSS FG 1 pasada al 40 %: 82 %; sin generacion 52-61 %); 60 % en 1, 40 % en 2 y 30 % en 3 no caben
            // 16-11 (G1, regla del usuario: DLSS E como minimo): 60 con Smooth Motion y E solo cabe 1 pasada al 40 % (79 % de grafica; 2 pasadas al 25 %,
            // el minimo, 83/87 % y lentas); modo 30 con Smooth Motion cada 2 refrescos y E: 100 / 75 / 60 % = 72 / 81 / 79 % (DLSS FG: 85 / 60 / 50)
            { "60_sm", new[] { 35, -1, -1 } }, { "30_sm", new[] { 100, 70, 55 } }, // (16-11 11:31: 2 pasadas al 30 % caben con captura 1080p pero NO con 1440p: 59,4-59,6 reales, 95 %) // (03:50: 60 SM 35 y no 40: con la captura de verdad el 40 % llega al 86 %; 30 SM con margen: 75/60 daban 81/79 % con la imagen de prueba)
            { "60_fg_4k", new[] { -1, -1, -1 } }, { "60_4k", new[] { 25, -1, -1 } }, { "30_fg_4k", new[] { 50, 35, 25 } }, { "30_4k", new[] { 50, 35, 25 } },
            // 19-11 (GENERACION): por tipo de generacion (Config.ModeKeyGen) en el modo 30, XeSS FG y FSR 3 FG de Magpie, con DLSS SR E (tabla_modelo_30_xess
            // / _fsr3). MEDIDO 19-11 19:10-19:30 (RTX 4060 Ti, Afterburner abierto, banco con la imagen de Ocarina en movimiento, 12 s, tele a 120 Hz):
            // XeSS 1440p 70 % 29,90 (min 29,81) 62/67 % de grafica, 2 al 45 % 29,90 (29,80) 65/72 %, 3 al 35 % 29,78 (29,49) 66/71 % (3 al 45 %
            // igual: 29,82 / 29,58, 74/77 %); mas no: 1 al 85 % 29,82 (29,54), 2 al 55 % 29,60 (28,91). XeSS con captura 1080p NO arranca (0
            // imagenes; Config.FgTypeNow). FSR 3 cabe de grafica (75/50/40: 63-68 %) pero NO llega a ritmo: 29,35-29,59 reales en todas (tambien
            // por DirectComposition): fuera del selector (generacion_lista_30). 4K: SIN MEDIR (la fuente de prueba a 4K solo da 20 imagenes)
            { "30_xess", new[] { 70, 45, 35 } }, { "30_xess_4k", new[] { 35, 25, -1 } },
            { "30_fsr3", new[] { 75, 50, 40 } }, { "30_fsr3_1080", new[] { 95, 70, 55 } }, { "30_fsr3_4k", new[] { 40, 25, -1 } },
        };
        // Modo "ligero" donde la grafica va justa: antiparpadeo estatico (sin flujo), flujo optico de NVIDIA en rendimiento y DLSS FG en
        // su preset C (rendimiento). Medido: con el, 60 con generacion y DLSS 5 al 50 % paso de 50,6 a 55 reales. ini tabla_ligero_<clave>=0/1
        public Dictionary<string, bool> LightTable = new Dictionary<string, bool>
        {
            { "60_fg", true }, { "60", true }, { "30_fg", false }, { "30", false },
            { "60_fg_4k", true }, { "60_4k", true }, { "30_fg_4k", false }, { "30_4k", false },
        };
        public string ModeKey { get { return (CapMode ? "30" : "60") + (FgOff ? "" : "_fg"); } }
        // 19-11 (GENERACION): ajustes POR TIPO de generacion. La clave del modo con el tipo en vez de "_fg": "60_sm" (Smooth Motion),
        // "60_dlssg" (DLSS FG: la de Magpie con DLSS 5, DLSS-G del motor sin el), "60_fsr3", "60_xess", "60_vfg" (igual en el 30), y detras
        // lo de siempre (captura _1080 / _1440 / _720 / _4k, pasadas _p2 / _p3). P. ej. tabla_dlss5_30_xess_1080=, antiparpadeo_60_fsr3_p2_dlss5=,
        // tabla_modelo_30_dlssg=, motor_modelo_60_dlssg_1440=. Se busca primero con el tipo y, si no hay, la de "_fg" (comun a todos), como antes
        public static string GenTag(string fgType)
        {
            switch (fgType) { case "smooth": return "sm"; case "fsr3": return "fsr3"; case "xess": return "xess"; case "vfg": return "vfg"; case "efg": return "efg"; default: return "dlssg"; }
        }
        public static readonly string[] GenTags = { "sm", "dlssg", "fsr3", "xess", "vfg", "efg" };
        public string ModeKeyGen { get { return FgOff ? "" : (CapMode ? "30_" : "60_") + GenTag(FgTypeNow); } }
        // La de la tabla para el modo de ahora + tail (p. ej. "_1080_p2"): primero la del tipo de generacion, luego la del modo
        public bool ModeGet<T>(Dictionary<string, T> t, string tail, out T v)
        {
            string g = ModeKeyGen;
            if (g.Length > 0 && t.TryGetValue(g + tail, out v)) return true;
            return t.TryGetValue(ModeKey + tail, out v);
        }
        public bool Win4k { get { return WindowFor(VideoSize) == "3840x2160"; } }
        public bool LightNow { get { bool v; return ModeGet(LightTable, Win4k ? "_4k" : "", out v) && v; } }
        // La clave de la tabla de DLSS 5 (Dlss5PctTable): con Smooth Motion la suya ("60_sm": la generacion del driver cuesta ~la mitad que
        // DLSS FG), si la hay; si no, la del modo con generacion
        string PctKey
        {
            get
            {
                string k = ModeKey + (Win4k ? "_4k" : "");
                // 16-11 noche (el usuario: "capturando a 1080 igual se puede subir la escala, y 720p"): tablas por captura, ini
                // tabla_dlss5_<modo>_1080= / _720= (p. ej. tabla_dlss5_60_sm_1080=); sin ellas, la del modo
                string hgt = VideoSize.EndsWith("x1080") ? "_1080" : VideoSize.EndsWith("x720") ? "_720" : "";
                // (19-11, GENERACION: la del tipo de generacion, como la de Smooth Motion: "60_sm", "30_xess_1080"...)
                if (ModeKeyGen.Length > 0)
                {
                    string s = ModeKeyGen + (Win4k ? "_4k" : "");
                    if (hgt.Length > 0 && Dlss5PctTable.ContainsKey(s + hgt)) return s + hgt;
                    if (Dlss5PctTable.ContainsKey(s)) return s;
                }
                if (hgt.Length > 0 && Dlss5PctTable.ContainsKey(k + hgt)) return k + hgt;
                return k;
            }
        }
        // Pasadas de DLSS 5 que caben ahora: las pedidas o, si con ellas no cabe (-1 en la tabla), menos. 0 = no cabe ni con una
        public int Dlss5PassesNow
        {
            get
            {
                int want = Math.Max(1, Math.Min(3, Passes30));
                if (Dlss5AutoAhora) return AutoPasadas(want);   // 21-11 (1.2.1): la escala automatica, por la cadena entera (src\Lanzador\EscalaAuto.cs)
                int[] p;
                if (Dlss5InputPct != 0 || !Dlss5PctTable.TryGetValue(PctKey, out p)) return want;
                for (int n = want; n >= 1; n--) if (p[n - 1] >= 0) return n;
                return 0;
            }
        }
        // (18-11, INTEGRADO: y sin nvngx_dlssnr.dll en magpie\ -un paquete sin ella- DLSS 5 no disponible: como si no cupiera)
        public bool Dlss5FitsNow { get { return Dlss5PassesNow > 0 && Integrado.Dlss5Available; } }
        // % de entrada de DLSS 5 ahora (0 = adaptativa)
        public int Dlss5PctNow
        {
            get
            {
                if (Dlss5InputPct != 0) return Math.Max(0, Dlss5InputPct);
                if (Dlss5AutoAhora) return AutoPctAhora();   // 21-11 (1.2.1): EscalaAuto.cs
                int[] p;
                if (!Dlss5PctTable.TryGetValue(PctKey, out p)) return 0;
                return Math.Max(0, p[Math.Max(1, Dlss5PassesNow) - 1]);
            }
        }
        // ---- 15-11: ajustes CON DLSS 5 por modo, para dejarle la grafica (captura 1080p/1440p; la de 4K no). Clave del modo: "60", "60_fg",
        // "30", "30_fg". ini <ajuste>_<modo>_dlss5=:
        //   escalador     uno, o uno por pasadas (a,b,c): dlss, fsr, sgsr, nvvfx, lanczos, cas... (vacio = el de siempre, escalador=)
        //   antiparpadeo  0-4 de DLSSNR (-1 = el de siempre: dlss5_antiparpadeo, 1 como mucho en los modos ligeros)
        //   aa            suavizado de bordes (y tramado) de mpv 0/1
        //   mascara       mascara automatica y arreglo de interfaz de DLSS 5 0/1
        //   mpv_escala    escalado de mpv de 1080p/720p a la ventana de 1440p (bilinear, spline36, ewa_lanczossharp...)
        // Lo que no esta en la tabla va como siempre. 60 con generacion (banco 15-11): FSR en vez de DLSS SR, sin bordes de mpv ni mascara,
        // mpv bilineal (E + antiparpadeo 1: 93 % de grafica; FSR + 0: 74/81 %; 2 pasadas al 40 % de 57,5 a 59,9 reales). 15-11 tarde
        // (exploracion): ahi antiparpadeo 1 (no cuesta y parpadea menos: 0,205 -> 0,149); 60 sin FG: mpv bilineal (87 -> 77 % de grafica) y
        // FSR en 2 y 3 pasadas (mas escala y menos parpadeo que E: 0,219 frente a 0,316)
        // 16-11 (el usuario: "DLSS E es el MINIMO"): nunca FSR 1 ni nada mas ligero como escalador final -> DLSS (E o mejor) en todas
        public Dictionary<string, string[]> D5ScalerTable = new Dictionary<string, string[]> { { "60_fg", new[] { "dlss" } }, { "60", new[] { "dlss" } } };
        // 16-11 (G2, noche): flujo optico de DLSS SR con DLSS 5 por modo (ini flujo_<modo>_dlss5=ninguno/amd/nvidia; vacio = dlss_flujo).
        // 60 con generacion / Smooth Motion SIN flujo: con el de NVOF, E deja perfiles en el HUD quieto (los vectores de bloque del fondo
        // se le pegan: borde 3,24-3,85, gt8 10-14 %); sin el, 1,44 (como FSR 1: 1,50), el fondo en movimiento igual (resto 1,20 / 1,16,
        // sin arrastres en los montajes noche_16-11_magpie60sm_E_sinflujo_*.png) y ~4 puntos de grafica menos. Los de 30 (el doble de
        // movimiento por imagen) y el 60 sin generacion, sin probar: con el flujo de siempre
        // 16-11 15:30 (G1, el usuario: "haz posibles 2 y 3 pasadas aunque sea cambiando cosas en esos modos"): estos ajustes tambien POR
        // PASADAS, con la clave <modo>_p2 / <modo>_p3 (ini <ajuste>_<modo>_p2_dlss5=..., p. ej. antiparpadeo_60_fg_p2_dlss5=0): con 2 o 3
        // pasadas va el de su clave y si no, el del modo (D5Get). Capturadora de verdad (tandas V1/V2): la grafica de los modos de 60 es casi
        // toda fija; lo que mas quita es el antiparpadeo (0), luego el enfoque CAS a 4K y el flujo optico de DLSS 5 (reutilizar el residuo)
        // en el 60 SM, y en el 60 sin generacion lo "ligero" del 60 SM (DLSS SR sin flujo, sin AA de mpv, sin mascara)
        public Dictionary<string, int> D5FlowTable = new Dictionary<string, int> { { "60_fg", 0 }, { "60_p3", 0 } };
        public int DlssFlowNow { get { int f; return D5Get(D5FlowTable, out f) && f >= 0 ? f : DlssFlow; } }
        public Dictionary<string, int> D5AntiFlickerTable = new Dictionary<string, int> { { "60_fg", 1 }, { "60_fg_p2", 0 }, { "60_p3", 0 } };
        public Dictionary<string, bool> D5EdgeAaTable = new Dictionary<string, bool> { { "60_fg", false }, { "60_p3", false } };
        public Dictionary<string, bool> D5MaskTable = new Dictionary<string, bool> { { "60_fg", false }, { "60_p3", false } };
        public Dictionary<string, string> D5MpvScaleTable = new Dictionary<string, string> { { "60_fg", "bilinear" }, { "60", "bilinear" } };
        // 16-11 (G1): por modo / pasadas, el flujo optico de DLSS 5 (DLSSNR: reutilizar el residuo y el antiparpadeo con flujo; ini
        // flujo5_<modo>[_pN]_dlss5=ninguno/amd/nvidia; sin el, el antiparpadeo 2-4 pasa a 1) y el enfoque CAS tras el escalado
        // (enfoque_<modo>[_pN]_dlss5=0-1; sin el, el de la captura: upscale_sharpen*)
        public Dictionary<string, double> D5DetailTable = new Dictionary<string, double>(), D5CompressTable = new Dictionary<string, double>(),
                                          D5DarkTable = new Dictionary<string, double>();
        public bool D5Num(Dictionary<string, double> t, out double v) { return D5Get(t, out v) && v >= 0; }
        public Dictionary<string, int> D5Flow5Table = new Dictionary<string, int> { { "60_fg_p2", 0 }, { "60_p3", 0 } };
        public Dictionary<string, double> D5SharpenTable = new Dictionary<string, double> { { "60_fg_p2", 0 } };
        public int Dlss5FlowNow { get { int f; return D5Get(D5Flow5Table, out f) && f >= 0 ? f : Dlss5Flow; } }
        public double SharpenNow { get { double s; return D5Get(D5SharpenTable, out s) && s >= 0 ? s : SharpenFor(VideoSize) * (D5Multi ? Dlss5MultiSharpen : 1); } }
        // ---- 19-11 (agente DLSS5; el usuario: "al hacer dos pasadas de DLSS 5 hay algunos fallos visuales leves"). Con 2-3 pasadas Magpie
        // pasa el modelo otra vez sobre la salida de la pasada anterior (misma resolucion de inferencia; al final un solo residuo total =
        // ultima salida - entrada, magpie\DLSSNR-MULTIPASS.md) y la app le daba a la 2a y 3a la MISMA fuerza que a la 1a: el efecto se
        // acumula. MEDIDO (imagen de Ocarina quieta, captura 1080p, modo 30, entrada 100 %, contra la misma cadena con el residuo a 0):
        // ver DLSS5-PASADAS en LEEME.txt. Lo que se veia: contorno oscuro alrededor del texto y de lo claro (halo oscuro x4,6), un rayado
        // fino en las telas, color lavado (saturacion 0,86 -> 0,76) y mas parpadeo con la imagen quieta, todo subido por el enfoque CAS.
        // Arreglo: (1) la fuerza de cada pasada (intensidad, tono local, estructura, piel del nivel) por pasada, ini dlss5_fuerza_pasadas=
        // 1,f2,f3; (2) con 2-3 pasadas, si el modo no trae contener_<modo>_dlss5, Overcorrection Suppression dlss5_pasadas_contener;
        // (3) Chroma Strength del residuo dlss5_pasadas_color (1 = de serie); (4) el enfoque CAS por dlss5_pasadas_enfoque (si el modo no
        // trae enfoque_<modo>_dlss5). Todo a 1 / 0 = como antes (dlss5_fuerza_pasadas=1,1,1 dlss5_pasadas_contener=0 dlss5_pasadas_color=1
        // dlss5_pasadas_enfoque=1). Medido por separado (2 pasadas, halo oscuro % / saturacion / parpadeo quieta): de antes 1,13 / 0,76 /
        // 0,85; 2a a la mitad 0,42 / 0,83 / 0,43; contener 0,5 0,81 (y el claro 3,8 -> 0,8) / 0,76 / 0,57; color 0,6 sat 0,86; enfoque a la
        // mitad parpadeo 0,34-0,41 (1 pasada: 0,25 / 0,86 / 0,49-0,58). Con 1 pasada nada cambia. Lo elegido (2a 0,6, contener 0,7, color
        // 0,7, enfoque 0,5), media de 6 copias: halo oscuro 1,44 -> 0,65 % (1 pasada 0,2-0,28), saturacion 0,76 -> 0,87, parpadeo 0,41-0,61 ->
        // 0,40, y sigue un 20 % mas de efecto que 1 pasada (2 pasadas de antes: +70 %)
        public double[] Dlss5PassStrength = { 1, 0.6, 0.5 };
        public double Dlss5MultiContain = 0.7, Dlss5MultiChroma = 0.7, Dlss5MultiSharpen = 0.5;
        public bool D5Multi { get { return D5Tuned && Dlss5PassesNow >= 2; } }
        // DLSS 5 encendido (y cabe): van los ajustes de arriba. 18-11 (RTX 4060 Ti): tambien con la captura de 4K, pero SOLO con claves
        // <modo>_4k (p. ej. aa_60_fg_4k_dlss5=0); sin ellas, a 4K como antes (nada de lo de 1080p/1440p). Medido: 60 con Smooth Motion a 4K
        // sin suavizado de mpv, sin mascara y DLSS 5 sin flujo cabe al 30 % (grafica 72 %); con todo puesto no cabia ni al 25 % (83 %)
        public bool D5Tuned { get { return !Dlss5Off && Dlss5FitsNow; } }
        bool D5Get<T>(Dictionary<string, T> t, out T v)
        {
            v = default(T);
            if (!D5Tuned) return false;
            // 19-11 (GENERACION): primero con el tipo de generacion (<modo>_<tipo>..., p. ej. 60_xess_p2), luego como antes
            string g = ModeKeyGen;
            if (g.Length > 0 && D5GetFor(t, g, out v)) return true;
            return D5GetFor(t, ModeKey, out v);
        }
        bool D5GetFor<T>(Dictionary<string, T> t, string mk, out T v)
        {
            v = default(T);
            int n = Dlss5PassesNow;
            if (Win4k) return (n >= 2 && t.TryGetValue(mk + "_4k_p" + n, out v)) || t.TryGetValue(mk + "_4k", out v);
            // 16-11 noche: por captura delante (<modo>_1440_p2, <modo>_1440), luego por modo
            string cs = CapSuffix;
            if (cs.Length > 0 && ((n >= 2 && t.TryGetValue(mk + cs + "_p" + n, out v)) || t.TryGetValue(mk + cs, out v))) return true;
            return (n >= 2 && t.TryGetValue(mk + "_p" + n, out v)) || t.TryGetValue(mk, out v);
        }
        public bool Dlss5MaskNow { get { bool m; return Dlss5AutoMask && !(D5Get(D5MaskTable, out m) && !m); } }
        public bool Dlss5UiFixNow { get { bool m; return Dlss5UiFix && !(D5Get(D5MaskTable, out m) && !m); } }
        public string MpvScaleNow { get { string s; return D5Get(D5MpvScaleTable, out s) && !string.IsNullOrEmpty(s) ? s : "ewa_lanczossharp"; } }
        public int AntiFlickerNow
        {
            get
            {
                int a;
                // 16-11 noche (el usuario: "1 pasada sin antiparpadeo, asi usas DLSS 4.5; con 3 pasadas si"): en el modo 30, 1 pasada sin
                // antiparpadeo (en todas las capturas, tambien 4K). ini dlss5_antiparpadeo_30_1pasada=-1 = como antes
                if (CapMode && Dlss5PassesNow == 1 && Af30OnePass >= 0) return Math.Min(4, Af30OnePass);
                int r = D5Get(D5AntiFlickerTable, out a) && a >= 0 ? Math.Min(4, a) : LightNow ? Math.Min(1, Dlss5AntiFlicker) : Dlss5AntiFlicker;
                return Dlss5FlowNow == 0 && r >= 2 ? 1 : r; // (los de flujo, 2-4, sin flujo de DLSS 5: el estatico)
            }
        }
        // 16-11 (G1): ¿caben n pasadas en este modo sin generacion? (para el aviso cuando con ella no caben)
        public bool PassesFitWithoutFg(int n)
        {
            int[] p;
            if (Dlss5AutoAhora) return AutoCabeSinFg(n);   // (1.2.1: EscalaAuto.cs)
            return n >= 1 && n <= 3 && Dlss5PctTable.TryGetValue((CapMode ? "30" : "60") + (Win4k ? "_4k" : ""), out p) && p[n - 1] >= 0;
        }
        // 16-11 (G1): lo que se quita con estas pasadas (para el aviso: Dlss5PassText), "" si nada
        public string D5PassNote
        {
            get
            {
                int n = Dlss5PassesNow;
                if (n < 2 || !D5Tuned) return "";
                string tail = (Win4k ? "_4k" : "") + "_p" + n;
                List<string> l = new List<string>();
                int a; if (ModeGet(D5AntiFlickerTable, tail, out a)) l.Add(a == 0 ? "sin antiparpadeo" : "antiparpadeo suave");
                double s; if (ModeGet(D5SharpenTable, tail, out s) && s <= 0.001) l.Add("sin enfoque");
                bool b; if (ModeGet(D5MaskTable, tail, out b) && !b) l.Add("sin máscara");
                if (ModeGet(D5EdgeAaTable, tail, out b) && !b) l.Add("sin suavizado");
                return l.Count == 0 ? "" : "ligero: " + string.Join(", ", l.ToArray());
            }
        }
        // El escalador de la tabla para las pasadas de ahora (null = el de siempre)
        public string D5ScalerNow
        {
            get
            {
                string[] s;
                if (!D5Get(D5ScalerTable, out s) || s == null || s.Length == 0) return null;
                string v = s[Math.Min(s.Length, Math.Max(1, Dlss5PassesNow)) - 1];
                return string.IsNullOrEmpty(v) ? null : v;
            }
        }
        // Las claves de modo (las mas largas antes: se busca por el final de la clave). 16-11 noche: tambien por captura
        static readonly string[] D5Modes = BuildD5Modes();
        static string[] BuildD5Modes()
        {
            List<string> l = new List<string>();
            // (19-11, GENERACION: tambien con el tipo de generacion: 60_sm, 30_xess_1080_p2...; ver Config.ModeKeyGen)
            List<string> modes = new List<string> { "60_fg", "30_fg" };
            foreach (string tg in GenTags) { modes.Add("60_" + tg); modes.Add("30_" + tg); }
            modes.Add("60"); modes.Add("30");
            foreach (string cap in new[] { "_1440", "_1080", "_720", "_4k" })   // (18-11: _4k, ver D5Get)
                foreach (string m in modes)
                { l.Add(m + cap + "_p2"); l.Add(m + cap + "_p3"); l.Add(m + cap); }
            foreach (string m in modes) { l.Add(m + "_p2"); l.Add(m + "_p3"); }
            l.AddRange(modes);
            return l.ToArray();
        }
        // ini escalador_<modo>_dlss5 / antiparpadeo_ / aa_ / mascara_ / mpv_escala_<modo>_dlss5
        bool ParseD5Opt(string key, string val)
        {
            if (!key.EndsWith("_dlss5")) return false;
            string k = key.Substring(0, key.Length - 6);
            // (16-11: tambien por pasadas: <modo>_p2 / <modo>_p3, ver D5Get)
            foreach (string mode in D5Modes)
            {
                if (!k.EndsWith("_" + mode)) continue;
                string opt = k.Substring(0, k.Length - mode.Length - 1);
                switch (opt)
                {
                    case "escalador":
                        {
                            if (val.Trim().Length == 0) { D5ScalerTable.Remove(mode); return true; }
                            string[] f = val.Split(',');
                            for (int i = 0; i < f.Length; i++) f[i] = f[i].Trim().Length == 0 ? "" : ParseScaler(f[i]);
                            D5ScalerTable[mode] = f;
                            return true;
                        }
                    case "antiparpadeo": D5AntiFlickerTable[mode] = ParseInt(val); return true;
                    case "aa": D5EdgeAaTable[mode] = ParseBool(val); return true;
                    case "mascara": D5MaskTable[mode] = ParseBool(val); return true;
                    case "mpv_escala": if (val.Trim().Length == 0) D5MpvScaleTable.Remove(mode); else D5MpvScaleTable[mode] = val.Trim(); return true;
                    case "flujo": if (val.Trim().Length == 0) D5FlowTable.Remove(mode); else D5FlowTable[mode] = ParseFlow(val); return true;
                    case "flujo5": if (val.Trim().Length == 0) D5Flow5Table.Remove(mode); else D5Flow5Table[mode] = ParseFlow(val); return true;
                    case "enfoque": if (val.Trim().Length == 0) D5SharpenTable.Remove(mode); else D5SharpenTable[mode] = ParseNum(val); return true;
                    // 16-11 noche: controles del residuo de DLSS 5 (gratis: sobre su resultado). detalle = High-frequency Range Strength
                    // (0-2), contener = Overcorrection Suppression (0-1), sombras = Shadow Protection (0-1). Vacio = el de Magpie
                    case "detalle": if (val.Trim().Length == 0) D5DetailTable.Remove(mode); else D5DetailTable[mode] = ParseNum(val); return true;
                    case "contener": if (val.Trim().Length == 0) D5CompressTable.Remove(mode); else D5CompressTable[mode] = ParseNum(val); return true;
                    case "sombras": if (val.Trim().Length == 0) D5DarkTable.Remove(mode); else D5DarkTable[mode] = ParseNum(val); return true;
                }
                return false;
            }
            return false;
        }
        public int FlowQualityNvNow { get { return LightNow ? 1 : FlowQualityNv; } }
        public int DlssModel30 = 0;        // en el modo 30 (sobra grafica); 0 = el de arriba
        // Movimiento para DLSS SR: 0 = ninguno, 1 = AMD (compute), 2 = NVIDIA (el acelerador de flujo optico de la RTX, apenas
        // cuesta). Sin el, DLSS trata cada imagen casi como quieta (lo que pasaba con LS)
        public int DlssFlow = 2;
        public int FlowQualityNv = 2;      // NVOF: 1 rendimiento, 2 equilibrado, 3 calidad, 4 alta, 5 maxima
        public int FlowQualityAmd = 1;     // AMDOF: 0 rendimiento, 1 calidad
        // (1.2.4 ronda 6, QUITAFG: fuera fg_flujo_30 / _60 / fg_flujo_calidad / _60, los vectores NVOF del DLSS FG de Magpie)
        // RTX Video VSR: categoria (0 estandar, 1 alta tasa, 2 streaming, 3 quitar ruido, 4 quitar desenfoque) y calidad 0-3
        public int VsrCategory = 2, VsrQuality = 3;
        // Enfoque tras el escalado (CAS de Magpie; con FSR 3 FG, RCAS). Por captura: con 1080p/720p, que mpv ya sube a la
        // ventana de 1440p, la imagen sale mas blanda. 0 = sin enfoque
        public double UpscaleSharpen = 0.3, UpscaleSharpen1080 = 0.5, UpscaleSharpen720 = 0.6;
        public double SharpenFor(string size)
        {
            return size == "1280x720" ? UpscaleSharpen720 : size == "1920x1080" ? UpscaleSharpen1080 : UpscaleSharpen;
        }

        // ---- DLSS 5 = DLSSNR AI Filter de Magpie (el filtro neuronal de NVIDIA a la misma resolucion) ----
        // Va ANTES del escalado, sobre la imagen de la captura (menos pixeles que a 4K)
        // Niveles 1-4 (mando: L1 / L2 / R2 / R1): "intensidad,tono local,estructura local,piel,fuerza total" (0-2 cada uno).
        // El nivel 4 es lo que el usuario se puso a mano en Magpie (grupo "dlss 5": todo a 2)
        public string[] Dlss5Levels =
        {
            "0.8,0.8,0.6,0.3,0.7",
            "1.2,1.2,1.0,0.6,0.85",
            // 20-11: R2 y R1 mas cerca de L2. Antes "1.6,1.6,1.5,0.9,1.0" y "2,2,2,1.2,1.1"
            "1.35,1.35,1.15,0.7,0.9",
            "1.5,1.5,1.3,0.8,0.95",
        };
        public int Dlss5DefaultLevel = 2;   // el de partida si no hay ninguno guardado
        public int Dlss5Style = 2;          // NR Style 0-2 (el usuario usaba 2 en Magpie)
        public int Dlss5Model = 0;          // 0 automatico (el del DLL), 1 HT, 2 CC Small FP8
        public bool Dlss5AutoMask = true;   // mascara automatica (no tocar el HUD...)
        public bool Dlss5UiFix = true;      // correccion de la interfaz del juego
        // Contra el parpadeo (lo que con LS no tenia arreglo: el addon no llevaba el cambio a las imagenes generadas). 0 nada,
        // 1 acumulacion estatica, 2 con flujo optico, 3 con flujo optico+ (suaviza que aparezca y desaparezca), 4 reconstruccion
        // temporal de baja frecuencia. 2-4 necesitan flujo optico (dlss5_flujo)
        public int Dlss5AntiFlicker = 3;
        public int Dlss5AntiFlickerMs = 80;
        public int Dlss5Flow = 2;           // flujo optico de DLSSNR: 0 ninguno, 1 AMD, 2 NVIDIA
        // Resolucion de entrada del modelo: 0 = adaptativa (Magpie la ajusta en pasos de 5 % para llegar al ritmo: sustituye a
        // las tablas de escala por modo, captura y pasadas que hacian falta con LS); 25-100 = fija en %
        public int Dlss5InputPct = 0;      // 0 = la tabla del modo (Dlss5PctTable); -1 = adaptativa; 25-100 = ese en todos
        // El modelo en cada imagen nueva (1) o en 1 de cada 2-4 (las de en medio reutilizan el resultado movido con el flujo):
        // por modo. Solo SDR
        public int Dlss5Every60 = 0, Dlss5Every30 = 0;   // 0 = la tabla del modo (Dlss5EveryTable); 1-4 = ese en todos
        // Modelo de DLSS 5 en cada imagen (1) o en 1 de cada 2 ('procesado alterno': la de en medio reutiliza el resultado movido con el flujo
        // optico), por modo y pasadas { 1, 2, 3 }. MEDIDO 14-11 (nivel 4): 60 con FG alterno 1 pasada al 50 % = 60,0/120 (en cada imagen solo
        // cabia el 25 %, y en los menus se veia raro), 2 pasadas al 25 % = 60/120; 60 sin FG 3 pasadas al 30 % = 60 (grafica 68 %)
        public Dictionary<string, int[]> Dlss5EveryTable = new Dictionary<string, int[]>
        {
            // (con alterno: 60 FG 1/2/3 pasadas = 50 / 25 / 25 % con 2 / 2 / 3; 60 sin FG 70 / 40 / 30 %; 30 sin FG 3 pasadas 75 % con 2;
            // en el modo 30 con FG el alterno no ayudaba: 2 pasadas al 100 % p95 46 ms)
            // 15-11: TODO en cada imagen. Con el alterno DLSS 5 parpadeaba de forma injugable (el usuario: "parpadeo fatal"): el efecto se
            // recalcula cada 2-3 imagenes y late. No volver a ponerlo sin verlo en la tele
            { "60_fg", new[] { 1, 1, 1 } }, { "60", new[] { 1, 1, 1 } }, { "30_fg", new[] { 1, 1, 1 } }, { "30", new[] { 1, 1, 1 } },
            { "60_fg_4k", new[] { 1, 1, 1 } }, { "60_4k", new[] { 1, 1, 1 } }, { "30_fg_4k", new[] { 1, 1, 1 } }, { "30_4k", new[] { 1, 1, 1 } },
        };
        // Tras un reinicio de la grafica: el modelo en menos imagenes hasta cerrar la app
        public int Dlss5EveryExtra = 0;
        public int Dlss5EveryNow { get { int e = CapMode ? Dlss5Every30 : Dlss5Every60; int[] t; if (e <= 0) e = ModeGet(Dlss5EveryTable, Win4k ? "_4k" : "", out t) ? t[Math.Max(1, Dlss5PassesNow) - 1] : 1; return Math.Max(1, Math.Min(4, e + Dlss5EveryExtra)); } }

        // ---- Generacion de fotogramas ----
        // (23-11, 1.2.4 ronda 6, QUITAFG; el usuario: "como el dlss fg va fatal, directamente quitalo" y "que haya pa elegir frame gens y ya
        // (da a elegir entre xefg, smooth motion (los que pueden usarlo) y fsr frame generation)"). Los tipos: smooth = NVIDIA Smooth Motion
        // (la del driver: perfil de NVIDIA del motor o de Magpie.exe; RTX 40 / 50 con el driver que la da), xess = XeSS FG, fsr3 = FSR 3 FG.
        // Siempre x2. DLSS FG (dlss) y NVIDIA VFG (vfg) ya no existen: lo guardado con ellos pasa a la mas parecida (src\Lanzador\TiposGeneracion.cs)
        public string FgType = "";
        // 15-11 (Smooth Motion): la generacion por modo, ini generacion_tipo_60= / generacion_tipo_30= (vacio = generacion_tipo). Smooth Motion
        // va con la sincronizacion vertical forzada para Magpie.exe (sin ella corta: la global esta forzada a desactivada) y con la presentacion
        // DXGI de Magpie (no DirectComposition)
        public string FgType60 = "", FgType30 = "";
        // 19-11 (GENERACION, el usuario: "apretando el joystick izquierdo da opcion a cambiar el tipo de frame generation"): lo elegido con
        // L3 (selector en pantalla) por modo, por juego (perfil) y en games\framegen_type.txt: "" = lo del ini (generacion_tipo_60/30)
        public string FgPick60 = "", FgPick30 = "";
        public string FgPickNow { get { return CapMode ? FgPick30 : FgPick60; } }
        // Los tipos que ofrece el selector en cada modo (ini generacion_lista_60 / _30, del plan de la grafica; "Sin generacion" siempre)
        public string[] GenList60 = { "smooth", "xess", "fsr3" }, GenList30 = { "smooth", "xess", "fsr3" };
        // El tipo que va en el modo de ahora (lo elegido con L3 / el menu, o el del ini; si no puede ir aqui, el mas parecido que si:
        // TiposGeneracion.cs)
        public string FgTypeNow { get { return FgTypeFor(CapMode, VideoSize); } }
        public bool SmoothNow { get { return !FgOff && FgTypeNow == "smooth"; } }
        // (1.2.4 ronda 6, QUITAFG: sin multiplicadores -x3 / x4 fuera-: cada real, una inventada. Se queda el nombre para las cuentas de fps)
        public int FgMultNow { get { if (FgOff) return 2; string t = FgTypeNow; return t == "fsr3" ? FsrMultNow : t == "efg" ? EfgMultNow : t == "smooth" && SmoothX4Now ? 4 : 2; } }   // (1.2.4 ronda 6, FGMULTI: FSR 3 FG x3 / x4, MultiFsr.cs; INTEGRA6: y Enhanced FG xN, Smooth Motion x4)

        // ---- Lo demas de Magpie ----
        public bool MagpieVrr = false;              // VRR experimental de Magpie (magpie_vrr)
        // 17-11 00:10 (el usuario: "añadiria vrr a la imagen"): VRR solo en el 60 con DLSS FG (ini magpie_vrr_60_fg). Sonda de punta a punta,
        // 1080p: 1 pasada 42,3 -> 40,9 ms, 2 pasadas 38,3 -> 37,5 (dentro del ruido). En el modo 30 y con Smooth Motion empeoraba (88 / 56 ms)
        public bool MagpieVrr60Fg = false;
        // 19-11 23:30 (el usuario: "reduce la latencia y los tirones con DLSS 5 y FG a 30 fps"): VRR tambien en el modo 30 con DLSS FG (ini
        // magpie_vrr_30_fg), por DXGI con la sincronizacion vertical forzada (estado 3) en vez de "cada 2 refrescos" (estado 4): Magpie saca
        // 59,94 imagenes/s y la tele a 120,000 Hz solo tiene 60,000 huecos de 2 refrescos (saltos de 3-4 refrescos = tirones, y la latencia
        // sube y baja). El 17-11 "empeoraba" en el 30 porque iba por DirectComposition (sin VRR de verdad)
        public bool MagpieVrr30Fg = false;
        // (22-11, 1.2.2 RITMO: que generaciones del modo 30 van por VRR -x3 / x4 con cualquiera de Magpie- y el modo 30 sin generacion por
        // VRR, src\Lanzador\Ritmo.cs; MULTI: en el 60, x3 / x4 -pantallas de 180-240 Hz- tambien por VRR, como DLSS FG)
        bool Vrr30FgNow { get { return MagpieVrr30Fg && CapMode && !FgOff && RitmoVrr30FgTipo; } }
        // (1.2.4 ronda 6, QUITAFG: magpie_vrr_60_fg era solo con DLSS FG / x3 / x4 en el 60: ya no hay ninguno de los dos)
        public bool MagpieVrrNow { get { return MagpieVrr || Vrr30FgNow || RitmoVrr30SinFgNow; } }
        // 17-11 00:15: con DirectComposition (el 60 con DLSS FG) Magpie no puede poner VRR ("eligible=false reason=non-DXGI-path"): con el VRR
        // del 60 con DLSS FG, presentacion por DXGI con la sincronizacion vertical forzada en su perfil (estado 3 de NvProfiles, como el 60 sin
        // generacion; G-SYNC con VSync = sin cortes arriba del todo). ini magpie_vrr_dxgi=0 = DirectComposition como antes (sin VRR de verdad)
        public bool VrrDxgi = true;
        public bool VrrDxgiNow { get { return VrrDxgi && (Vrr30FgNow || RitmoVrr30SinFgNow) && !NvProfiles.VsyncBroken; } }
        // RTX HDR (Nvidia VFX TrueHDR de Magpie): convierte la imagen SDR de la consola a HDR. Necesita el HDR de Windows encendido en la tele.
        // ini rtx_hdr=0/1, rtx_hdr_nits (400-2000), rtx_hdr_gris (10-100), rtx_hdr_contraste / rtx_hdr_saturacion (0-200, 100 = igual)
        public bool RtxHdr = false;        // 15-11, MEDIDO: a 4K cuesta ~15 ms por imagen (y pasa DLSSNR a 16 bits): 60 con FG 36-39 reales (sin DLSS 5 o antes del escalado igual); solo cabe en el modo 30 sin DLSS 5
        public bool RtxHdrBefore = false;  // ini rtx_hdr_antes: antes del escalado (a la resolucion de la captura, mas barato) en vez de a 4K
        public int RtxHdrNits = 1000, RtxHdrGray = 50, RtxHdrContrast = 100, RtxHdrSaturation = 100;
        // Menos latencia: NVIDIA Reflex en el ritmo de Magpie (con el refresco 'simple' Magpie lo ignoraba y usaba Front Edge). ini reflex=0/1
        public bool Reflex = false;        // 15-11: con Reflex (sin generacion) Magpie presenta sin sincronizacion vertical: desgarro en la tele (el usuario). Apagado
        // 15-11, MEDIDO: sin generacion Reflex SI se activa ("Reflex warm status: actualOn=true", 59,7 reales); con DLSS FG falla siempre ("D3D12 async
        // generation marker status=-1") y Magpie cae a Async con tirones (57,6 frente a 59,5 con Front Edge): solo sin generacion
        public bool ReflexWithFg = false; // ini reflex_con_generacion (DLSS FG: falla; otras generaciones: ver tabla)
        // 16-11 04:20 (G1, capturadora de verdad): el 60 sin generacion por DXGI con la sincronizacion vertical forzada (como Smooth Motion
        // pero sin generar) en vez de por DirectComposition. Con DirectComposition Magpie recogia las imagenes con saltos (p95 24-27 ms,
        // 11-15 de 15 ventanas lentas) y perdia imagenes (DLSS 5 al 55 %: 58,2 reales, min 51); asi p95 19,7-20 ms, 0 lentas, 59,9 reales,
        // en la tele el 94-98 % de las imagenes a 2 refrescos, sin cortes (AllowsTearing 0 %), 5-9 ms de presentar a verse; +2-3 puntos de
        // grafica. ini dxgi_60_sin_fg=0 = como antes (DirectComposition); y si el driver no deja poner la sincronizacion (NvProfiles.VsyncBroken)
        public bool VsyncNoFg = true;
        public bool VsyncNoFgNow { get { return VsyncNoFg && FgOff && !CapMode && !ReflexNow && !NvProfiles.VsyncBroken; } }
        // 16-11 12:45 (G1 + LAT): en ese 60 sin generacion por DXGI, Magpie "siguiendo a la fuente" (contentMode 1) en vez de su reloj fijo
        // de 60,000 (Front Edge): con la captura a 59,94 el reloj fijo hace que la cola de la tele vaya dando saltos. Medido por LAT de punta
        // a punta: 24,6 ms de media (p95 28) frente a 31,5-35,7 (p95 43-48), 100 % a 2 refrescos frente a 94-99 %. Con Smooth Motion NO (el
        // doble de repeticiones: SM quiere 60,000 fijas) ni en el modo 30 (la cola se llena: 120 ms). ini magpie_sigue_fuente_60
        // PERO con la capturadora de verdad (G1, tanda O, 1440p, 120 s) va peor de fluidez: el temblor de la captura llega a la tele (313
        // imagenes a 1 o 3 refrescos repartidas todo el rato y 247 huecos reales >= 25 ms, frente a 218 y 15 con Front Edge, que ademas solo
        // los tiene mientras su reloj cae cerca del refresco de la tele). Por eso NO va por defecto
        public bool Follow60NoFg = false;
        public bool Follow60NoFgNow { get { return Follow60NoFg && VsyncNoFgNow; } }
        // 17-11 00:00 (LAT, sonda de punta a punta, captura 1080p, DLSS 5 nivel 2): en el 60 con DLSS FG (Magpie, no Smooth Motion)
        // siguiendo a la fuente: 1 pasada 42,3 -> 38,2 ms de media (p95 51,5 -> 46,0), 2 pasadas 38,3 -> 32,1 (p95 46,7 -> 41,1); a
        // cambio la captura llega algo mas irregular a Magpie (cP95 17,3 -> 17,9-18,9 ms, ventanas lentas 0-1 -> 2-3). Sin probar con la
        // capturadora de verdad (sin generacion iba peor de fluidez: ver arriba). ini magpie_sigue_fuente_60_fg=1 (por defecto no)
        public bool Follow60Fg = false;
        public bool FollowNow { get { return Follow60NoFgNow; } }   // (1.2.4 ronda 6, QUITAFG: magpie_sigue_fuente_60_fg era solo con DLSS FG)
        // 16-11 13:40 (G1, idea del coordinador): el reloj de Magpie (Front Edge) al ritmo de la capturadora, 59,94 / 29,97 (la captura es
        // siempre 60000/1001), en vez de 60,000 / 30,000 = justo la mitad de la tele: con Smooth Motion la cola de la tele no se vaciaba
        // nunca (la latencia iba a saltos y se quedaba arriba). Magpie lo acepta ("effective base target: 59.940 FPS"). Capturadora de verdad,
        // 1440p, 120 s (tanda S): 60 SM de presentar a verse 23,9 -> 20,1 ms (diente de sierra regular 16-24 en vez de mesetas de 25-26),
        // huecos de imagenes reales >= 25 ms 16 -> 3, repeticiones en la tele 20 -> 22 (igual), 59,93 -> 59,92 reales; 30 SM la cola se vacia
        // sola a 1 ms por segundo (de 96 a ~24-36 ms en ~65 s; con 30,000 se quedaba en mesetas de 82 / 65 / 56 / 42 ms), huecos >= 50 ms 3 -> 0,
        // ventanas lentas 6 -> 1, repeticiones 6 -> 6, 29,98 (min 29,78) -> 29,97 (min 29,95). Sin medir: 60 sin generacion y DLSS FG.
        // ini magpie_ritmo_ntsc=0 = 60 / 30 como antes
        public bool NtscRate = true;
        // 18-11 (LATENCIA, src\ColaSm.cs): con Smooth Motion en Magpie, la cola de la tele se vacia con una pausa de mpv de N ms poco despues de
        // que Magpie empiece a presentar (ini magpie_sm_vaciar_ms, 0 = no; magpie_sm_vaciar_tras_ms = cuando, tras el telon)
        public int SmDrainMs = 0, SmDrainDelayMs = 2500;
        public bool SmDrainAuto = false, SmDrainSplit = false, SmDrainLog = false; public double SmDrainThreshold = 1, SmDrainTarget = 1;   // 19-11 (LATENCIA3, src\ColaSm.cs)
        public bool SmDrainReal = false; public double SmRealHigh = 1.5, SmRealTarget = 1, SmRealGain = 1; public string PresentMonExe = "";   // 19-11 (LATENCIA3, src\ColaReal.cs)
        // 16-11 10:10 (G1): el modo 30 con DLSS FG por DXGI con la sincronizacion del driver "cada 2 refrescos" (sin Smooth Motion: estado 4
        // de NvProfiles) en vez de DirectComposition, donde la tele recibia el 85 % de las imagenes a 2 refrescos y el 7 % a 1 y a 3 (medido
        // con PresentMon). Solo con la tele a 100 Hz o mas y no a 119,88 (ahi la cola no se vaciaria). ini dxgi_30_fg=0 = como antes
        public bool VsyncHalfFg30 = true;
        // 19-11 (GENERACION): lo mismo con XeSS FG / FSR 3 FG / NVIDIA VFG de Magpie en el modo 30 (ini dxgi_30_xess, dxgi_30_fsr3, dxgi_30_vfg;
        // 16-11 G1 con el estado 4 para todas: XeSS 99,6 % de las imagenes a 2 refrescos, FSR 3 96,3 %)
        public Dictionary<string, bool> VsyncHalfByType = new Dictionary<string, bool> { { "xess", true }, { "fsr3", true }, { "vfg", true } };
        bool VsyncHalfType { get { string t = FgTypeNow; bool b; return VsyncHalfByType.TryGetValue(t, out b) && b; } }
        // 19-11 noche (VRR30): ini dxgi_30_fg_119=1 = tambien con la tele a 119,88 Hz (ahi los huecos de 2 refrescos son 59,94 por segundo,
        // los mismos que saca Magpie con la captura a 59,94: sin el desfase de 120,000 que da saltos de 3-4 refrescos)
        public bool VsyncHalf119 = false;
        // (22-11, 1.2.2 RITMO y MULTI: solo con x2; con x3 / x4 la sincronizacion "cada 2 refrescos" dejaria la salida en 60: Config.RitmoFijo1Now)
        public bool VsyncHalfFgNow { get { return VsyncHalfType && CapMode && !FgOff && FgTypeNow != "smooth" && !ReflexNow && !NvProfiles.VsyncBroken && Pantalla.Hz() >= 100 && (VsyncHalf119 || !Pantalla.Is119); } }
        public bool ReflexNow { get { return Reflex && (FgOff || ReflexWithFg); } }        // 15-11, MEDIDO: Reflex no se activa en este montaje (Magpie cae a Async) y Async da 58,3 reales con tirones frente a 60,0 con Front Edge
        public int MagpieDuplicates = 0;            // deteccion de repetidas de Magpie: 0 nunca (las quita mpv), 1 dinamica, 2 siempre
        public int MagpieCaptureMethod = 0;         // 0 Graphics Capture (WGC)
        // magpie.global.<clave>, magpie.perfil.<clave>, magpie.efecto.<efecto>.<parametro>: tal cual en la config de Magpie
        public List<string[]> MagpieOverrides = new List<string[]>();

        // ---- 15-11: motor propio (src\Motor): la capturadora por DirectShow + DLSS SR, DLSS-G y Reflex de NVIDIA (Streamline)
        // en vez de mpv + Magpie. motor=propio: con DLSS 5 apagado la imagen la hace el motor (sin Magpie: los cambios del mando son al
        // momento, sin telon; generacion con Reflex y sincronizacion vertical). Con DLSS 5 encendido, mpv + Magpie como siempre (DLSSNR
        // solo esta en Magpie). motor=magpie: siempre como antes
        public string Engine = "propio";   // (ConsoleEnhancer: el motor propio de serie; el plan lo dice)
        public string EnginePath = "";   // (ConsoleEnhancer: <app>\engine\ConsoleEnhancerEngine.exe)
        // Modelo de DLSS SR del motor por modo (MEDIDO 15-11 en el motor: E 4,8 ms, K 6,2, M 10,1, L 12,1 por imagen a 4K). 16-11 (noche,
        // G2): L y M (transformer 2) dejan los bordes del texto/HUD sucios y temblando sobre fondo en movimiento y el menu quieto "respira"
        // (el "DLSS con bugs" del modo 30 sin DLSS 5); K limpio, quieto y mas barato (Ocarina 60: borde HUD K 0,36 / M 1,28, 8,1 / 12,4 ms)
        // -> K en todos los modos (nunca menos que E)
        public string EnginePreset60 = "K", EnginePreset60Fg = "K", EnginePreset30 = "K", EnginePreset30Fg = "K";
        // 16-11 (motor_nitidez_transformer, 0-1): el enfoque (CAS) del motor con J/K/L/M es el de la captura (upscale_sharpen_1080 0,5,
        // el mismo que el CAS de Magpie) por esto: con K a 0,5 sale un halo oscuro alrededor de las letras (noche_16-11_motor30_nitidez.png);
        // a 0,3 limpio y nitido. Con E, igual que antes
        public double EngineSharpenTransformer = 0.6;
        public int EngineReflex = 1;                  // 1 baja latencia, 2 con boost (la generacion lo exige: minimo 1)
        // 20-11 (LATENCIA4, motor_vrr=0|1): 1 = vrr=1 al motor (tele con VRR: cadena con ALLOW_TEARING, cada imagen al acabarla; con la
        // generacion, sin sincronizacion en el modo 30 y con ella en el 60). 0 = como antes (en este PC Streamline ya sacaba la imagen por VRR)
        public int EngineVrr = 0;
        // motor_vrr_fg_vsync (con motor_vrr=1 y la generacion): -1 = auto (sin sincronizacion en el modo 30, con ella en el 60), 0 = nunca, 1 = siempre
        public int EngineVrrFgVsync = -1;
        // 20-11 (LATENCIA4, motor_dlss5_home): DLSS 5 del motor tambien en el HOME / menu de la consola a 4K nativo (1, como del 20-11
        // 03:15 a las 06:00) o no (0, de serie: el menu tal cual, como con Magpie, que no lo escalaba). Medido (menu 4K, fuente de prueba,
        // PresentMon): de la llegada a verse 20,5 -> 10,0 ms y grafica 13,3 -> 3,4 ms; DLSS 5 en los menus cambiaba el tono de lo plano
        public bool EngineDlss5Home = false;
        public string EngineExtra = "";               // motor_extra: ajustes tal cual (clave=valor separados por espacios)
        // 16-11 (motor_en_espera, ini 0/1; de serie 1): el motor no se cierra al pasar a mpv + Magpie (DLSS 5 encendido, otra captura,
        // un juego con DLSS 5): suelta la capturadora y se queda escondido y listo ("switch2-standby"); al volver a el (DLSS 5 apagado)
        // se despierta en ~0,5 s en vez de arrancar otro (~4,5 s de negro, ~3,1 s solo de Streamline). Si la sesion empieza con
        // mpv + Magpie, uno arrancado ya en espera (standby=1) una vez, en el HOME a 4K nativo. Solo con un ConsoleEnhancerEngine.exe que lo
        // sepa hacer (lleva "switch2-standby" y "switch2-wake"); si no contesta a tiempo, como antes. 0 = exactamente lo de antes
        public bool EngineStandby = true;
        // 16-11 (dlss5_sin_telon, src\SinTelon.cs): DLSS 5 si/no con el boton A sin telon. Al apagarlo el motor despierta DETRAS de Magpie
        // (que sigue con DLSS 5 a la vista y mpv delante) y, en cuanto da imagen, se pone delante y Magpie y mpv se cierran; al encenderlo
        // el motor sigue jugando ENCIMA de todo mientras mpv y Magpie se abren y preparan DLSS 5 (8-15 s) y se va cuando Magpie presenta.
        // Sin imagen del motor a tiempo, el telon de siempre. 0 = exactamente lo de antes (telon). Medido el 16-11 (capturadora con la
        // pantalla No Signal; 28 cambios en los modos 60 y 30, 1080p y 1440p): 0 ms de negro (antes 1-1,3 s al apagarlo y 9,6-9,9 s de
        // telon al encenderlo); queda un tiron de ~0,2 s justo despues del corte (la capturadora para ~180 ms al soltarla el otro proceso)
        public bool Dlss5Seamless = true;
        // dlss5_sin_telon_encima: al encenderlo, 1 = el motor encima una vez (la ventana de Magpie sale encima en cuanto aparece), 2 = encima
        // y reafirmado hasta que la app ve presentar a Magpie. 16-11: la primera imagen que enseña Magpie ya es buena (sin negro: copias de la
        // tele cada 25 ms) -> 1
        public int Dlss5SeamlessOnTop = 1;
        // dlss5_sin_telon_foco: al encenderlo, el foco vuelve al motor mientras Magpie prepara (su generacion sigue); 0 = mpv delante todo el
        // rato (el motor sin generacion: 60 imagenes en el modo 60, 30 en el de 30). 16-11: Magpie termina de preparar y presenta igual (9 de
        // 9 encendidos); la generacion del motor solo falta ~1 s (lo que mpv tiene el primer plano para que empiece Magpie)
        public bool Dlss5SeamlessFocus = true;
        // dlss5_sin_telon_espera_ms: al apagarlo, lo que se espera la imagen del motor despertado antes de poner el telon de siempre
        public int Dlss5SeamlessWaitMs = 4000;
        // 16-11 (velocidad, dlss5_sin_telon_puente): el puente en TODAS las reaperturas de Magpie con mpv en marcha (nivel, pasadas, modo,
        // generacion, R3, los ajustes de un juego) y en la entrada desde el HOME a un juego con Magpie: el motor en espera juega encima
        // (con la captura compartida y su generacion) hasta que el Magpie nuevo presenta, en vez de 10-15 s de telon; y los cambios de
        // motor por el modo o la generacion, sin telon como DLSS 5 si/no. 0 = como antes (solo DLSS 5 si/no sin telon)
        public bool MagpieBridge = true;
        // 16-11 (velocidad, dlss5_sin_telon_entrada): al entrar en un juego desde el HOME a 4K con un motor en espera listo, SIN telon: la imagen
        // de la consola sigue hasta que el motor da la suya encima (a un juego del motor: el relevo de DLSS 5 apagado; a uno de Magpie: el
        // puente, y mpv cambia y Magpie se abre debajo del motor). Necesita la capturadora a la vez a 4K (mpv en el HOME) y al tamaño del
        // juego (el motor). 0 = telon negro y el puente cuando mpv ya ha cambiado de captura
        public bool BridgeEntryFirst = false;
        // 18-11 (PASADAS, src\Pasadas.cs): vigilancia de DLSS 5 con 2-3 pasadas (pasadas_vigilancia=0 la quita) y cuantos reinicios del
        // historial en 20 s cuentan como fallo (pasadas_vigilancia_reinicios)
        public int D5Watch = 1, D5WatchResets = 10;
        public string D5WatchTestLog = "";   // (solo pruebas con test_source: pasadas_vigilancia_prueba=<fichero con lineas como las del log de Magpie>)
        // 18-11 (PASADAS): el parpadeo por la imagen (pasadas_vigilancia_imagen=1; 0 = no, 2 = sin mirar la entrada) y su umbral
        public int D5WatchImage = 1;
        public double D5ImageOsc = 4, D5ImagePct = 25;
        public static volatile bool EngineActive;     // la imagen la esta haciendo el motor (no mpv)
        // 17-11 16:55: Smooth Motion en el motor propio = la generacion del driver (perfil de NVIDIA de ConsoleEnhancerEngine.exe). El driver
        // solo lo mira al abrir el proceso: el motor se vuelve a abrir cuando cambia (generacion si/no, modo 30/60). Estado del perfil: 0 sin,
        // 1 con la sincronizacion forzada (modo 60), 2 con la de cada 2 refrescos (modo 30 a 100 Hz o mas: cada imagen 2 refrescos)
        // (1.2.4 ronda 6, QUITAFG: sin DLSS-G en el motor, Smooth Motion va en el motor cuando el tipo del modo es smooth; motor_generacion_60 /
        // _30, que elegian entre Smooth Motion y DLSS-G, ya no se leen)
        public bool BridgeLight = true;   // 17-11 17:00 (puente_ligero): el motor con E mientras juega encima de Magpie arrancando (menos tirones)
        public bool EngineSmoothAny { get { return EngineSmoothFor(true) || EngineSmoothFor(false); } }
        // Smooth Motion en el motor para este modo: el tipo de ese modo (lo elegido con L3 / el menu, o el del ini) es smooth
        public bool EngineSmoothFor(bool cap) { return FgTypeFor(cap, VideoSize) == "smooth"; }
        // (19-11: en el menu de la PS5 a 4K nativo, nunca: el usuario lo quiere "nativamente sin smooth motion ni nada", src\MenuPs5.cs)
        public int EngineSmoothMode { get { if (FgOff || Mode120 || NvProfiles.EngineSmoothBroken || (HomeNative && Consolas.Actual.MenuPropio)) return 0; if (CapMode) return EngineSmoothFor(true) ? (SmoothX4Now ? 3 : Pantalla.Hz() >= 100 ? 2 : 1) : 0; return EngineSmoothFor(false) && Pantalla.Hz() >= 100 ? 1 : 0; } }   // (ConsoleEnhancer: en el modo 60 la generacion solo con la pantalla a 100 Hz o mas; 20-11 HZ120: a 120 sin Smooth Motion)
        // 19-11 (GENERACION): la generacion de ahora solo la hace Magpie (FSR 3 FG, XeSS FG, NVIDIA VFG): con DLSS 5 apagado, mpv + Magpie sin el
        // 21-11 (1.0.9): FSR 3 FG ya la hace el motor propio (src\Motor\Engine\EngineFgFfx.cs) salvo con DLSS de escalador (con ella el motor
        // va sin Streamline, sin DLSS): entonces sigue Magpie. XeSS FG y la de NVIDIA VFX, Magpie
        // (22-11, 1.2.4 SOLOMOTOR: los tipos / multiplicadores que el motor sabe, en MotorSabeGeneracion de SoloMotor.cs -MOTORFG: XeSS FG y x3 / x4-)
        // (1.2.4 ronda 6, QUITAFG: sin DLSS FG ni x3 / x4, lo que va por Magpie es FSR 3 FG con DLSS de escalador y lo que el reparto manda alli:
        // XefgMotor.cs, MotorGenAMagpie)
        // (1.2.4 ronda 6, QUITAFG, fase 2: sin Magpie, nada va por el; se queda el nombre para lo que lo pregunta)
        public bool FgMagpieOnly { get { return false; } }
        // El motor con FSR 3 FG (fg_tipo=fsr3 al abrirlo: su cadena de presentacion es la de FidelityFX; no se cambia en marcha)
        // (1.2.4 ronda 6, QUITAFG: tambien con DLSS de escalador -antes por Magpie-: el motor lleva DLSS SR por Streamline sin sus ganchos, como con XeSS FG)
        public bool EngineFfxWanted { get { return !FgOff && !HomeNative && !Mode120 && FgTypeNow == "fsr3"; } }
        public static volatile int EngineFfxLaunched = -1;
        static readonly Dictionary<int, int> engineFfxByPid = new Dictionary<int, int>();
        public static void NoteEngineFfx(Process p) { if (p == null) return; try { lock (engineFfxByPid) engineFfxByPid[p.Id] = EngineFfxLaunched; } catch { } }
        public static int EngineFfxOf(Process p) { if (p == null) return -1; try { int v; lock (engineFfxByPid) return engineFfxByPid.TryGetValue(p.Id, out v) ? v : -1; } catch { return -1; } }
        // La clave del modo del motor con su generacion ("60_sm" con Smooth Motion, "60_dlssg" con DLSS-G; "" sin generacion)
        public string EngineModeKeyGen { get { return FgOff ? "" : (CapMode ? "30_" : "60_") + (EngineSmoothMode > 0 ? "sm" : "dlssg"); } }
        string EngineGet(Dictionary<string, string> t)
        {
            string v, g = EngineModeKeyGen, cs = CapSuffix;
            if (g.Length > 0 && ((cs.Length > 0 && t.TryGetValue(g + cs, out v)) || t.TryGetValue(g, out v))) return v;
            return cs.Length > 0 && t.TryGetValue(ModeKey + cs, out v) ? v : null;
        }
        public bool EngineSmoothWanted { get { return EngineSmoothMode > 0; } }
        public static volatile int EngineSmoothLaunched = -1;   // Smooth Motion del ultimo motor lanzado (EngineArgs): -1 ninguno, 0 no, 1 si
        public static volatile int EngineSmoothRunning = -1;    // el del motor que hace la imagen
        // 20-11 (LATENCIA5, motor_60fg_refresco_fijo): el motor con el refresco fijo (G-SYNC apagado en su perfil) en el modo 60 con DLSS FG.
        // Tambien en el menu de la consola mientras el modo elegido sea el 60 con DLSS FG (asi no se reabre el motor al entrar y salir del
        // menu); sin generacion (boton de captura), en el 30 y con Smooth Motion, VRR como siempre (el 60 sin generacion a refresco fijo
        // pierde el ritmo: 59,94 en 120 Hz, medido 22,3 ms frente a 17,7 y saltos de 1 y 3 refrescos)
        public bool EngineFixed60Fg = true;
        public bool EngineReserve = true;   // 21-11 (1.0.9) motor_reserva=0|1: motor de reserva con la otra generacion del modo (MaybeReserveEngine)
        // (23-11, 1.2.4 ronda 6 PERFILES: nunca en el modo 120. Alli no hay generacion -EngineLive fg=0, EngineSmoothMode 0- y la cuenta de
        // CeRefrescoFijoAhora salia con los numeros del 60 -Framerate 59,94 x 2-: log del usuario 23-11 02:49:16 y 03:14:23, Rocket League a 120
        // "Refresco fijo ... si (... frente a 2 x 59.94)" -> "Refresco del motor: fijo (modo 60 con DLSS FG...)": el motor se volvia a abrir
        // -7-10 s con el de antes- y jugaba con G-SYNC apagado y Present(0, ALLOW_TEARING) -sin generacion- en vez de VRR)
        public bool EngineFixedWanted { get { return EngineFixed60Fg && !Mode120 && !CapMode && CeRefrescoFijoAhora() && !FgOff && !EngineSmoothFor(false) && !NvProfiles.EngineFixedBroken; } } // (ConsoleEnhancer: CeRefrescoFijoAhora, motor_60fg_refresco_fijo=auto, ImagenCe.cs; 1.2.4 ronda 6, QUITAFG: sin el x4 del motor en el 30)
        public static volatile int EngineFixedLaunched = -1;    // el refresco fijo del ultimo motor lanzado (EngineArgs): -1 ninguno, 0 no, 1 si
        static readonly Dictionary<int, int> engineFixedByPid = new Dictionary<int, int>();
        // (justo tras Process.Start de un motor: con que refresco se abrio ese proceso)
        public static void NoteEngineFixed(Process p) { if (p == null) return; try { lock (engineFixedByPid) engineFixedByPid[p.Id] = EngineFixedLaunched; } catch { } }
        public static int EngineFixedOf(Process p) { if (p == null) return -1; try { int v; lock (engineFixedByPid) return engineFixedByPid.TryGetValue(p.Id, out v) ? v : -1; } catch { return -1; } }
        // DLSS 5 apagado, o que no cabe en este modo (60 con generacion: Magpie iria sin el, peor que el motor)
        // (19-11, GENERACION: y no con una generacion que solo hace Magpie, FgMagpieOnly)
        // (22-11, 1.2.4 SOLOMOTOR: el reparto entero en MotorSabeAhora, src\Lanzador\SoloMotor.cs: el motor en todo lo que sabe hacer)
        public bool EngineNow { get { return Engine == "propio" && MotorSabeAhora && File.Exists(EnginePath); } }
        // 21-11 (1.0.9): XeSS y FSR 4 de escalador solo los tiene Magpie (el motor ponia FSR 1 sin decirlo): con ellos, mpv + Magpie
        public bool ScalerMagpieOnly { get { return false; } }   // (1.0.9: ya no: el motor propio hace XeSS y FSR 3/4, EngineSr.cs)
        // 20-11 (DLSS 5 EN EL MOTOR): ini motor_dlss5=1 = DLSS 5 con 1 pasada lo hace el motor propio (PrismaNr.dll, pieza propia del motor -en el zip desde la 1.2.4-, +
        // EngineDlss5.cs: DLSSNR sobre la captura antes de DLSS SR) en vez de mpv + Magpie: sin mpv, DWM ni la captura de Windows (el motor
        // va a ~35-48 ms con DLSS-G frente a 72-94 de Magpie con DLSS 5 y FG). 2-3 pasadas siguen en Magpie. Con la "fuerza total" (dlss5_fuerza, 20-11). Sin el 5o
        // numero del nivel) ni % de entrada: DLSS 5 a la resolucion de entrada del motor
        // (22-11, 1.2.4 SOLOMOTOR: sin eleccion ni clave -motor_dlss5 ya no se lee-: con engine\PrismaNr.dll, DLSS 5 siempre en el motor
        // si sabe hacer la combinacion, MotorDlss5Pedido. Solo las pruebas lo ponen a false)
        public bool EngineDlss5 = true;
        // 20-11: % de entrada de DLSS 5 en el motor (dlss5_entrada del motor: DLSS 5 sobre la captura reducida y lo que cambia sumado
        // ampliado). ini motor_dlss5_entrada=N (todos los modos; 0 = lo de abajo), motor_dlss5_entrada_30 (100: 1440p cabe entera, 14 ms de
        // grafica) y motor_dlss5_entrada_30_4k (75: 4K 100 % = 24,3 ms, 75 % = 15,8, 50 % = 9,4; 30 reales en los tres). En el 60, la tabla de
        // % de DLSS 5 de Magpie de ese modo (tabla_dlss5_*), o 100 si no hay
        // 20-11 03:40 (2-3 pasadas en el motor): un % por pasadas (1,2,3). Medido modo 30 + FG con antiparpadeo 3: 1440p 2 pasadas al 100 %
        // 23,7 ms, 3 al 75 % 22,1 (al 100 % 31 ms y 26,8 reales: no); 4K 2 al 60 % 19,6, 3 al 45 % 17,2 (30 reales todos)
        public int EngineDlss5PctAll = 0;
        public int[] EngineDlss5Pct30 = { 100, 100, 75 }, EngineDlss5Pct30_4k = { 75, 60, 45 };
        public int EngineDlss5PctNow
        {
            get
            {
                if (EngineDlss5PctAll > 0) return EngineDlss5PctAll;
                int i = Math.Max(1, Math.Min(3, Dlss5PassesNow)) - 1;
                if (CapMode) return (CaptureNow == "3840x2160" ? EngineDlss5Pct30_4k : EngineDlss5Pct30)[i];
                int p = Dlss5PctNow; return p > 0 ? p : 100;
            }
        }
        // (20-11 03:40: ya con 2-3 pasadas y el antiparpadeo; antes solo 1 pasada)
        // (22-11, 1.2.3 MOTOR5: y solo lo que el motor hace -con PrismaNr.dll, hasta motor_dlss5_pasadas, sin x3 / x4 ni XeSS FG-; lo demas por
        // Magpie como siempre. Antes con motor_dlss5=1 y x4 o 2 pasadas Magpie iba sin su escala automatica y los textos decian "motor propio")
        public bool EngineDlss5Now { get { return EngineDlss5 && Engine == "propio" && !Dlss5Off && Dlss5FitsNow && MotorDlss5Pedido; } }
        // El % de entrada de DLSS 5 que se aplica de verdad (para los textos): el del motor si DLSS 5 va en el, si no el de Magpie
        public int Dlss5PctShownNow { get { return EngineDlss5Now ? EngineDlss5PctNow : Dlss5PctNow; } }
        // 16-11 noche: motor_modelo_<modo>_1080= / _1440= / _720= (por captura; sin ella, la del modo)
        // 19-11 (GENERACION): el motor con DLSS-G en el modo 60. MEDIDO 19-11 (motor solo, imagen de prueba, 1440p): K 58,69 reales (5,95 ms de
        // grafica), M 59,02 (9,55 ms); 1080p M 59,60 (6,99 ms): la grafica sobra, lo que no llega es el ritmo (Reflex con la sincronizacion,
        // ya visto el 17-11): DLSS FG fuera del selector del 60 (generacion_lista_60). Si se vuelve a poner: M. Ini: motor_modelo_60_dlssg[_1080]=
        public Dictionary<string, string> EnginePresetByCap = new Dictionary<string, string> { { "60_dlssg", "M" } };
        public Dictionary<string, string> EngineInByCap = new Dictionary<string, string>();
        // (19-11, GENERACION: primero con el tipo de generacion del motor, motor_modelo_60_sm_1440= / motor_modelo_30_dlssg=...: EngineGet)
        public string EngineInNow { get { string v = HomeNative ? null : EngineGet(EngineInByCap); return v ?? "0"; } }
        public string EnginePresetNow { get { string pk = DlssPick; if (pk.Length > 0) return pk; /* (1.0.12: el modelo elegido en el menu de ajustes) */ string p = EngineGet(EnginePresetByCap); if (p != null) return p; return CapMode ? (FgOff ? EnginePreset30 : EnginePreset30Fg) : (FgOff ? EnginePreset60 : EnginePreset60Fg); } }

        // Quitar las imagenes repetidas: un juego a 30 fps llega a 60 con cada imagen 2 veces. Sin esto la
        // generacion inventa imagenes entre dos iguales y el movimiento va a saltos (se ve como 40 fps)
        public bool Dedupe = true;
        // 16-11 (G1): filtro del modo 30 que no pierde imagenes reales con ritmo irregular (TrayApp.FrameFilter). ini repetidas_30_sin_perder
        public bool Dedupe30Keep = true;
        // Suavizado de bordes en mpv (FXAA a 1440p, medido: 0,34 ms por imagen real)
        public bool EdgeAa = true;
        // 09-11: el mismo shader quita el tramado de las zonas casi planas (niebla, sombras): ini quitar_tramado
        public bool Dedither = true;
        // 10-11: con DLSS 5 apagado, el shader s2_aa_strong.glsl (ini tramado_fuerte: 0 nunca, 1 con DLSS 5 apagado, 2 siempre)
        public int StrongDither = 1;
        public bool StrongDitherOn = true; // lo del mando (L3)

        // El shader de bordes y tramado de ahora ("" = ninguno)
        public string EdgeShaderNow
        {
            get
            {
                if (!EdgeAa) return "";
                bool aa;
                if (D5Get(D5EdgeAaTable, out aa) && !aa) return ""; // 15-11: con DLSS 5 en ese modo, la grafica para DLSS 5 (aa_<modo>_dlss5)
                bool strong = StrongDitherOn && StrongDither > 0 && (Dlss5Off || StrongDither >= 2) && File.Exists(Paths.EdgeAaShaderStrong);
                string f = strong ? Paths.EdgeAaShaderStrong : Paths.EdgeAaShader;
                return File.Exists(f) ? f : "";
            }
        }

        // Jugando, cada cuanto se mira si se ha vuelto al HOME (s): captura_juego_s
        public double GameGrabEvery = 1.2;
        // 09-11: entrar en un juego desde el HOME a 4K solo cuando se ha entrado de verdad (ver HomeTick)
        public bool EnterWaitUser = true;
        public int EnterGrabs = 3;
        public int EnterInUseGrabs = 2;
        public double EnterBlackSecs = 1.0;
        public double EnterBlackLongSecs = 4.0;
        public double EnterMaxSecs = 30;
        public double EnterGrabEvery = 0.3;
        // 09-11: recien abierta la app sin ver el HOME: ya se estaba jugando (ver HomeTick)
        public int StartGrabs = 5;
        public int StartGrabsWake = 10;
        public double StartStillSecs = 15;
        public double StartGrabEvery = 0.35;
        // 10-11: HOME visto jugando: Magpie sigue escalando home_espera_nativo_s segundos antes de pasar a 4K nativo. Volver al
        // mismo juego en ese tiempo no reabre nada
        public double HomeGraceSecs = 6;               // ini home_espera_nativo_s (14-11: 6, no 20: el HOME con DLSS 5 y DLSS SR se ve raro -tono y texto-; a 4K nativo, perfecto)
        public double HomeGraceStillSecs = 1.5;        // ini home_espera_quieto_s
        // 10-11: telon del paso al HOME a 4K nativo: la pantalla congelada y un fundido con el HOME nuevo
        public bool CurtainFrozen = true;              // ini telon_imagen_congelada
        public int CurtainFrozenFadeMs = 400;          // ini telon_fundido_ms
        // 15-11: al reabrir solo Magpie (mpv sigue), el telon con el juego en directo de fondo (src\TelonVivo.cs). 16-11 (el usuario: "se sigue
        // jugando pero muy ralentizado, queda fatal"): MEDIDO sin consola, con mpv debajo en "independent flip" DWM no componia con cada imagen
        // de mpv: en el modo 30 un 19 % de las imagenes salia a los 42-51 ms en vez de 33 (tirones; ya con el arreglo de TelonVivo, cada imagen
        // al refresco siguiente: 33,3 ms exactos). Aun asi en el modo 30 son 30 imagenes sin generacion frente a las 60 de siempre: de serie,
        // NO (negro con el aviso arriba, al momento). ini telon_vivo: 0 no (de serie), 1 siempre, 2 solo en los modos de 60 (60 imagenes)
        public int CurtainLive = 0;                    // ini telon_vivo
        // Del HOME a 4K a un juego: Magpie se abre a la vez que mpv cambia de captura y escala en cuanto los dos estan listos
        public bool EnterLsDirect = true;              // ini entrada_directa
        public int CurtainFadeInMs = 120;              // ini telon_entrada_ms
        // El telon sigue hasta que Magpie presenta imagenes con todo puesto (su log), con tope
        public int CurtainSettleMaxMs = 2500;          // ini telon_esperar_magpie_max_ms (0 = no esperar)
        // Lo que se le espera a Magpie mientras crea los modelos de NVIDIA (medido el 14-11: 8-12 s con DLSSNR + DLSS SR + DLSS FG)
        public int MagpieStartMaxSecs = 30;            // ini magpie_arranque_max_s

        // ---- Lo que elige el mando (lo pone la app antes de escribir la config de Magpie) ----
        public bool CapMode = false;      // modo 30 fps (games\mode.txt)
        // 20-11 (HZ120): modo 120 fps para esports (fallo 3). El boton de 30/60 cicla 30 -> 60 -> 120 cuando la captura admite 120. A 120:
        // captura a 120 real, sin generacion de fotogramas, Reflex en baja latencia, presentacion inmediata y el minimo proceso. La GC573
        // da 120 EXACTO a 1080p y 1440p (4K y 720p solo 60): por eso solo se ofrece en esas capturas. mode.txt guarda "120"
        public bool Mode120 = false;
        public string Framerate120 = "120";        // ini framerate_120 (la GC573 anuncia 120 exacto a 1080p/1440p)
        public string Permit120 = "auto";          // ini permitir_120: auto (solo si la tele va a >=120 Hz y la captura admite 120) | 1 | 0
        public string[] Capturas120 = new string[0]; // ini capturas_120 (ConsoleEnhancer: las escribe el plan con los formatos que lista TU capturadora a 120; vacio = sin modo 120. En Prisma, las de la GC573: 1080p y 1440p)
        // 20-11 (HZ120): la velocidad de captura de ahora. 21-11 (1.2.1, el usuario: "se me ha quedado congelada la app al salir al menu desde
        // rocket league"): en el HOME / menu de la consola (4K nativo, CaptureNow) se pedia framerate=120 con el modo 120 del juego: mpv no
        // pudo cambiar en caliente, se reabrio a 3840x2160 a 120 (la GC573 no lo da) y se quedo en "sin senal" para siempre. A 120 solo en el
        // juego y con una captura que lo admite (Capturas120); si no, la de siempre (60)
        public string FramerateNow { get { return Mode120 && !HomeNative && Captura120Ok(CaptureNow) ? Framerate120 : Framerate; } }
        public bool Captura120Ok(string size) { if (Capturas120 == null) return false; foreach (string cc in Capturas120) if (cc == size) return true; return false; }
        // 20-11 (HDCP120): aviso de HDCP (PS5/PS4) solo cuando de verdad no hay imagen un rato largo (ver ConsolasApp.ObservaConsola)
        public bool HdcpWarn = true;               // ini hdcp_aviso (0 lo apaga)
        public double HdcpWarnSecs = 20;           // ini hdcp_aviso_s: negro de verdad + silencio seguidos antes de avisar
        public int Passes30 = 1;          // pasadas de DLSSNR (Multi Pass 1-3)
        public int Level30 = 2;           // nivel de DLSS 5 (1-4)
        public bool Dlss5Off = false;     // DLSS 5 apagado (sin DLSSNR en la cadena)
        public bool FgOff = false;        // generacion de fotogramas apagada (boton de captura del mando)
        public bool AltScalerOn = false;  // R3: el escalador alternativo en vez del normal

        // 21-11 (1.2.01, el usuario: "añade opcion de fsr 1 o de directamente no usar reescalado"): el escalador elegido en el menu de
        // ajustes para este juego ("" = el de siempre; fsr = FSR 1; ninguno = sin reescalado: estirado simple, Catmull-Rom en el motor)
        public string ScalerPick = "";
        public string ScalerNow { get { return ScalerPick.Length > 0 ? ScalerPick : AltScalerOn ? AltScaler : (D5ScalerNow ?? Scaler); } }
        // (15-11: tabla_modelo_<modo> con 4 numeros = sin DLSS 5, y con DLSS 5 en 1, 2 y 3 pasadas)
        // dlss5_antiparpadeo_30_1pasada: el antiparpadeo de DLSS 5 con 1 pasada en el modo 30 (-1 = el de siempre, dlss5_antiparpadeo). 16-11
        // noche: 0 (sin el, para dejar sitio a DLSS 4.5). 22-11 (1.2.2, REESCALADO): 3, como con 2-3 pasadas: con la entrada reducida (4K al
        // 45-55 %) la correccion de DLSS 5 parpadeaba al mover la camara, y el flujo optico que necesita ya se calculaba igual (Magpie lo
        // pide con dlss5_flujo aunque el antiparpadeo este a 0): cuesta solo su pasada (src\Lanzador\Reescalado.cs). =0 como antes
        public int Af30OnePass = 3;
        // 16-11 noche: tabla_modelo_<modo>_1440= / _1080= / _720= (por captura; sin ella, la del modo)
        string CapSuffix { get { return VideoSize.EndsWith("x1080") ? "_1080" : VideoSize.EndsWith("x720") ? "_720" : VideoSize.EndsWith("x1440") ? "_1440" : ""; } }
        // (19-11, GENERACION: con el tipo de generacion delante: tabla_modelo_30_xess_1440=, tabla_modelo_30_xess=, luego como antes)
        bool ModelGet(out int[] m)
        {
            string g = ModeKeyGen, cs = CapSuffix;
            if (g.Length > 0 && ((cs.Length > 0 && ModelTable.TryGetValue(g + cs, out m)) || ModelTable.TryGetValue(g, out m))) return true;
            return ModelTable.TryGetValue(ModeKey + cs, out m) || ModelTable.TryGetValue(ModeKey, out m);
        }
        public int DlssModelNow { get { return AutoModelo(DlssModelBase); } }   // 21-11 (1.2.1): con el tope que pone la vigilancia de la escala (EscalaAuto.cs)
        int DlssModelBase { get { string pk = DlssPick; if (pk.Length > 0) return DlssPickNum(pk); /* (1.0.12: menu de ajustes) */ if (CapMode && DlssModel30 > 0) return DlssModel30; if (DlssModel > 0) return DlssModel; int[] m; return ModelGet(out m) ? (Dlss5Off || !Dlss5FitsNow ? m[0] : m.Length >= 4 ? m[Math.Max(1, Math.Min(3, Dlss5PassesNow))] : m[1]) : 5; } }
        // Con la captura de 4K (ventana de 4K) no hay nada que escalar
        // (1.2.02) "ninguno" (sin reescalado, del menu) tambien estira la imagen a la pantalla: antes por Magpie quedaba la ventana de mpv
        // en una esquina; Magpie estira con bicubico y el motor con Catmull-Rom (Ce.EngineUpscaler)
        // (escalador=ninguno del ini = la captura ya es la pantalla: sin Magpie, como antes)
        public bool UpscaleOn { get { return WindowFor(VideoSize) != "3840x2160" && (ScalerNow != "ninguno" || ScalerPick == "ninguno"); } }

        public double[] LevelNums(int level)
        {
            string[] v = Dlss5Levels[Math.Max(1, Math.Min(4, level)) - 1].Split(',');
            double[] n = { 1, 1, 1, 0, 1 };
            for (int k = 0; k < v.Length && k < 5; k++) n[k] = double.Parse(v[k].Trim(), CultureInfo.InvariantCulture);
            return n;
        }

        // Tamaño de la ventana de mpv (lo que escala Magpie) para cada captura. 1080p y 720p van en una ventana de 1440p (mpv
        // la sube con EWA lanczos): asi el cambio 1080p <-> 1440p no toca Magpie (se reabre la capturadora sin cerrar mpv)
        // 16-11 noche (ventana_1080=1): con captura 1080p la ventana de mpv a 1080p (sin estirarla a 1440p: con DLSS 5 en los de 60 mpv
        // la estiraba en bilineal, borrosa, y DLSS SR y DLSS 5 trabajaban con mas puntos de los que hay)
        public static volatile bool Window1080;
        public static string WindowFor(string capture) { return capture == "3840x2160" ? "3840x2160" : Window1080 && capture == "1920x1080" ? "1920x1080" : "2560x1440"; }

        public static string ModelName(int preset)
        {
            switch (preset)
            {
                case 13: return "M (DLSS 4.5)";
                case 12: return "L (DLSS 4.5)";
                case 11: return "K (DLSS 4)";
                case 10: return "J (DLSS 4)";
                case 5: return "E (DLSS 3)";
                case 0: return "automatico";
                default: return "preset " + preset;
            }
        }

        // 16-11: el modelo del motor propio por su letra (motor_modelo_*), como ModelName
        public static string EngineModelName(string p)
        {
            switch (p)
            {
                case "M": return "M (DLSS 4.5)";
                case "L": return "L (DLSS 4.5)";
                case "K": return "K (DLSS 4)";
                case "J": return "J (DLSS 4)";
                case "E": return "E (DLSS 3)";
                default: return p;
            }
        }

        // 16-11 (el usuario: "si hay algun texto de que escalado se usa, corrigelo"): el escalado que de verdad se ve. Con DLSS 5 apagado (o
        // si no cabe en el modo) la imagen la hace el motor propio con SU modelo (motor_modelo_*, K), no el de la tabla de Magpie: los avisos
        // decian "DLSS M (DLSS 4.5)" en el modo 30 con el motor en K. Y el alternativo (R3) del motor es Catmull-Rom, no RTX Video
        public string ScalerShownName
        {
            get
            {
                if (CaptureNow == "3840x2160") return "4K nativo";
                if (EngineNow)
                {
                    // 21-11 (1.0.9): lo que hace de verdad el motor (Ce.EngineUpscaler): DLSS, XeSS, FSR 3/4 o FSR 1
                    string eu = Ce.EngineUpscaler(this);
                    return eu == "dlss" ? "DLSS " + EngineModelName(EnginePresetNow) + " · motor propio" : eu == "xess" ? "XeSS · motor propio" :
                           eu == "fsr4" ? "FSR 4 · motor propio" : eu == "fsr3" ? "FSR 3 · motor propio" :
                           eu == "catrom" ? "sin reescalado · motor propio" : "FSR 1 · motor propio";
                }
                return UpscaleOn ? ScalerName(ScalerNow) : "sin reescalado";
            }
        }

        public string ScalerName(string s)
        {
            switch (s)
            {
                case "dlss": return "DLSS " + ModelName(DlssModelNow);
                case "vsr": return "RTX Video";
                case "xess": return "XeSS";
                case "fsr": return "FSR 1";
                // 15-11 (exploracion): los demas escaladores de Magpie
                case "sgsr": return "SGSR";
                case "nvvfx": return "NVIDIA VFX Upscale";
                case "fsr2": return "FSR 2";
                case "fsr3": return "FSR 3";
                case "fsr4": return "FSR 4";
                case "lanczos": return "Lanczos";
                case "bicubic": return "Bicubico";
                case "cas": return "CAS (escalado)";
                default: return "sin reescalado";
            }
        }

        public string FgName
        {
            get { string t = FgTypeNow; return t == "smooth" ? "Smooth Motion" : t == "fsr3" ? "FSR 3 FG" : "XeSS FG"; }
        }

        // Lo que se aplica, para el log
        public string ProfileText
        {
            get
            {
                return VideoSize + (WindowFor(VideoSize) != VideoSize ? " (mpv a " + WindowFor(VideoSize) + ")" : "") +
                       (UpscaleOn ? " -> 4K con " + ScalerName(ScalerNow) : " sin escalar") +
                       (Mode120 ? ", modo 120 fps (esports)" : CapMode ? ", modo 30 fps" : ", modo 60 fps") +
                       (Dlss5Off ? ", DLSS 5 apagado" : !Integrado.Dlss5Available ? ", DLSS 5 NO DISPONIBLE (falta " + Integrado.Dlss5Dll + ")" : !Dlss5FitsNow ? ", DLSS 5 NO CABE en este modo (quitado)" : ", DLSS 5 (DLSSNR) nivel " + Level30 + ", " + Dlss5PassesNow + " pasada" + (Dlss5PassesNow > 1 ? "s" : "") + (Dlss5PassesNow < Passes30 ? " (pedidas " + Passes30 + ")" : "") +
                        ", entrada " + (Dlss5PctShownNow > 0 ? Dlss5PctShownNow + " %" : "adaptativa") + (EngineDlss5Now ? ", en el motor propio" : LightNow ? ", ligero" : "") +
                        (!EngineDlss5Now && Dlss5EveryNow > 1 ? ", modelo 1 de cada " + Dlss5EveryNow : "") +
                        (AntiFlickerNow > 0 ? ", antiparpadeo " + AntiFlickerNow : "")) +
                       (FgOff ? ", sin generacion" : ", " + FgName) + (RtxHdr ? ", RTX HDR " + RtxHdrNits + " nits" : "") + (ReflexNow ? ", Reflex" : "");
            }
        }

        const string DefaultIni =
            "# ConsoleEnhancer (sin asistente: lo normal es que este ini lo escriba el asistente con lo detectado)\r\n" +
            "consola=auto\r\n" +
            "motor=propio\r\n" +
            "console_audio=1\r\n" +
            "close_on_poweroff=1\r\n" +
            "poweroff_stall_ms=1500\r\n" +
            "performance_boost=1\r\n";

        public static Config Load()
        {
            Config c = new Config();
            if (!File.Exists(Paths.IniFile))
            {
                Directory.CreateDirectory(Paths.Datos);
                File.WriteAllText(Paths.IniFile, DefaultIni, new UTF8Encoding(false));
                Log.Write("Creado " + Paths.IniFile + " con valores por defecto");
                Integrado.ResolvePaths(c); // (18-11, INTEGRADO: mpv y motor dentro de la app si estan)
                return c;
            }

            foreach (string raw in File.ReadAllLines(Paths.IniFile))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("["))
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim().Trim('"');
                // magpie.global.<clave> / magpie.perfil.<clave> / magpie.efecto.<efecto>.<parametro>: tal cual (respeta mayusculas)
                if (key.StartsWith("magpie."))
                {
                    string[] mk = line.Substring(0, eq).Trim().Split(new char[] { '.' }, 3);
                    if (mk.Length == 3 && mk[2].Length > 0) c.MagpieOverrides.Add(new string[] { mk[1].ToLowerInvariant(), mk[2], val });
                    continue;
                }
                if (c.CeParse(key, val)) continue; // (ConsoleEnhancer: src\ConsoleEnhancerApp.cs)
                try
                {
                    switch (key)
                    {
                        case "mpv_path": break;   // (23-11, 1.2.4 SINMPV: sin mpv; la clave de un ini de antes no cuenta)
                        case "device": c.Device = val; break;
                        case "video_size": c.VideoSize = val; break;
                        case "framerate": c.Framerate = val; break;
                        case "framerate_120": c.Framerate120 = val; break;                                    // 20-11 (HZ120)
                        case "permitir_120": c.Permit120 = val.Trim().ToLowerInvariant(); break;              // 20-11 (HZ120): auto | 1 | 0
                        case "capturas_120":                                                                  // 20-11 (HZ120): capturas que la capturadora admite a 120
                            {
                                string[] cs = val.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                                for (int ci = 0; ci < cs.Length; ci++) cs[ci] = cs[ci].Trim();
                                c.Capturas120 = cs;
                                break;
                            }
                        case "hdcp_aviso": c.HdcpWarn = ParseBool(val); break;                                // 20-11 (HDCP120)
                        case "hdcp_aviso_s": c.HdcpWarnSecs = Math.Max(3, Math.Min(120, ParseNum(val))); break; // 20-11 (HDCP120)
                        case "pixel_format": c.PixelFormat = val; break;
                        case "pixel_format_4k": c.PixelFormat4k = val; break;
                        case "capture_audio": c.CaptureAudio = ParseBool(val); break;
                        case "console_audio": c.ConsoleAudio = ParseBool(val); break;
                        case "audio_canales": { int ch; if (int.TryParse(val, out ch) && (ch == 2 || ch == 8)) c.AudioChannels = ch; break; }
                        case "start_streaming_center": c.StartStreamingCenter = ParseBool(val); break;
                        case "streaming_center_path": c.StreamingCenterPath = val; break;
                        case "close_on_poweroff": c.CloseOnPowerOff = ParseBool(val); break;
                        case "apagar_pc": c.ShutdownPc = ParseBool(val); break;
                        case "escritorio_negro": c.BlackDesktop = ParseBool(val); break;
                        case "poweroff_stall_ms": c.PowerOffStallMs = ParseInt(val); break;
                        case "apagado_rapido_s": c.NoSignalFastSecs = Math.Max(0, Math.Min(10, ParseNum(val))); break;
                        case "apagado_confirmar_ms": c.NoSignalConfirmMs = Math.Max(150, ParseInt(val)); break;
                        case "apagado_confirmar_sin_silencio_ms": c.NoSignalConfirmSlowMs = Math.Max(300, ParseInt(val)); break;
                        case "apagado_espera_s": c.NoSignalHoldSecs = Math.Max(0, Math.Min(60, ParseNum(val))); break;
                        case "apagado_espera_arranque_s": c.NoSignalHoldLaunchSecs = Math.Max(0, Math.Min(120, ParseNum(val))); break;
                        // 16-11 tarde (APAGADO): en el HOME sin nada arrancando, recien vuelto de un juego, arranque cancelado, HOME atenuado
                        case "apagado_espera_home_s": c.NoSignalHoldHomeSecs = Math.Max(0, Math.Min(60, ParseNum(val))); break;
                        case "apagado_home_tras_juego_s": c.NoSignalHomeAfterGameSecs = Math.Max(0, Math.Min(30, ParseNum(val))); break;
                        case "apagado_fin_arranque_s": c.LaunchCancelSecs = Math.Max(0.5, Math.Min(60, ParseNum(val))); break;
                        case "apagado_ocr_atenuado_s": c.DimOcrEverySecs = Math.Max(0.3, Math.Min(10, ParseNum(val))); break;
                        case "performance_boost": c.PerformanceBoost = ParseBool(val); break;
                        case "afterburner_pausa": c.AfterburnerPause = ParseBool(val); break;
                        case "segundo_plano": c.Background = val.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries); break;
                        case "test_source": c.TestSource = val; break;
                        case "test_mpv_args": break;   // (23-11, 1.2.4 SINMPV: sin mpv)
                        case "test_filtro": c.TestFrameFilter = val; break;
                        case "test_source_juego": c.TestSourceGame = val; break;
                        case "test_motor": c.TestMotor = val.Trim(); break;   // (1.2.4 arreglos, RELEVO: la fuente del motor en pruebas)
                        case "test_audio_search": c.TestAudioSearch = ParseBool(val); break;
                        case "banco_sin_capturas": c.BenchNoGrabs = ParseBool(val); break;
                        case "banco_s": c.BenchSecs = Math.Max(0, ParseInt(val)); break;
                        case "banco_calentar_s": c.BenchWarmSecs = Math.Max(0, ParseInt(val)); break;
                        case "banco_reaperturas": c.BenchReopens = Math.Max(0, ParseInt(val)); break;
                        case "banco_motor": c.BenchEngine = ParseBool(val); break;
                        case "banco_etiqueta": c.BenchLabel = val; break;
                        case "diag_tramado": c.DiagDither = Math.Max(0, ParseInt(val)); break;
                        case "diag_home": c.DiagHome = Math.Max(0, ParseInt(val)); break;
                        case "home_4k_nativo": c.HomeNativeEnabled = ParseBool(val); break;
                        case "consola": Consolas.Configurar(val); break; // (18-11, CONSOLAS: auto | switch2 | ps5... docs\CONSOLAS.md)
                        // ---- Magpie ----
                        case "magpie_origen": c.MagpieSource = val; break;
                        case "magpie_actualizar": c.MagpieUpdate = val.Trim().ToLowerInvariant().StartsWith("igual") ? 2 : Math.Max(0, Math.Min(2, ParseInt(val))); break; // 18-11 (INTEGRADO)
                        case "usar_carpetas_app": c.AppFolders = ParseBool(val); break;                                                                                    // 18-11 (INTEGRADO)
                        case "escalador": c.Scaler = ParseScaler(val); break;
                        case "escalador_alternativo": c.AltScaler = ParseScaler(val); break;
                        case "dlss_modelo": c.DlssModel = ParseInt(val); break;
                        case "dlss_modelo_30": c.DlssModel30 = ParseInt(val); break;
                        case "dlss_flujo": c.DlssFlow = ParseFlow(val); break;
                        case "flujo_calidad_nvidia": c.FlowQualityNv = Math.Max(1, Math.Min(5, ParseInt(val))); break;
                        case "flujo_calidad_amd": c.FlowQualityAmd = Math.Max(0, Math.Min(1, ParseInt(val))); break;
                        case "ventana_1080": Config.Window1080 = ParseBool(val); break;
                        case "dlss5_antiparpadeo_30_1pasada": c.Af30OnePass = ParseInt(val); break;
                        case "vsr_categoria": c.VsrCategory = Math.Max(0, Math.Min(4, ParseInt(val))); break;
                        case "vsr_calidad": c.VsrQuality = Math.Max(0, Math.Min(3, ParseInt(val))); break;
                        case "upscale_sharpen": c.UpscaleSharpen = ParseNum(val); break;
                        case "upscale_sharpen_1080": c.UpscaleSharpen1080 = ParseNum(val); break;
                        case "upscale_sharpen_720": c.UpscaleSharpen720 = ParseNum(val); break;
                        case "dlss5_nivel1": c.Dlss5Levels[0] = val; break;
                        case "dlss5_nivel2": c.Dlss5Levels[1] = val; break;
                        case "dlss5_nivel3": c.Dlss5Levels[2] = val; break;
                        case "dlss5_nivel4": c.Dlss5Levels[3] = val; break;
                        case "dlss5_nivel": c.Dlss5DefaultLevel = Math.Max(1, Math.Min(4, ParseInt(val))); break;
                        case "dlss5_estilo": c.Dlss5Style = Math.Max(0, Math.Min(2, ParseInt(val))); break;
                        case "dlss5_modelo": c.Dlss5Model = Math.Max(0, Math.Min(2, ParseInt(val))); break;
                        case "dlss5_mascara": c.Dlss5AutoMask = ParseBool(val); break;
                        case "dlss5_interfaz": c.Dlss5UiFix = ParseBool(val); break;
                        case "dlss5_antiparpadeo": c.Dlss5AntiFlicker = Math.Max(0, Math.Min(4, ParseInt(val))); break;
                        case "dlss5_antiparpadeo_ms": c.Dlss5AntiFlickerMs = Math.Max(5, Math.Min(200, ParseInt(val))); break;
                        // 19-11 (DLSS5): 2-3 pasadas sin acumular fallos (ver Config.Dlss5PassStrength)
                        case "dlss5_fuerza_pasadas":
                            {
                                string[] f = val.Split(',');
                                for (int k = 0; k < 3 && k < f.Length; k++) if (f[k].Trim().Length > 0) c.Dlss5PassStrength[k] = Math.Max(0, Math.Min(2, ParseNum(f[k].Trim())));
                                break;
                            }
                        case "dlss5_pasadas_contener": c.Dlss5MultiContain = Math.Max(0, Math.Min(1, ParseNum(val))); break;
                        case "dlss5_pasadas_color": c.Dlss5MultiChroma = Math.Max(0, Math.Min(2, ParseNum(val))); break;
                        case "dlss5_pasadas_enfoque": c.Dlss5MultiSharpen = Math.Max(0, Math.Min(2, ParseNum(val))); break;
                        case "dlss5_flujo": c.Dlss5Flow = ParseFlow(val); break;
                        case "dlss5_entrada": c.Dlss5InputPct = val.ToLowerInvariant().StartsWith("a") ? -1 : val.ToLowerInvariant().StartsWith("t") ? 0 : Math.Max(25, Math.Min(100, ParseInt(val.TrimEnd('%', ' ')))); break;
                        case "dlss5_cada_60": c.Dlss5Every60 = Math.Max(1, Math.Min(4, ParseInt(val))); break;
                        case "dlss5_cada_30": c.Dlss5Every30 = Math.Max(1, Math.Min(4, ParseInt(val))); break;
                        case "generacion_tipo": c.FgType = ParseFg(val); break;
                        case "generacion_tipo_60": c.FgType60 = val.Trim().Length == 0 ? "" : ParseFg(val); break;
                        case "generacion_tipo_30": c.FgType30 = val.Trim().Length == 0 ? "" : ParseFg(val); break;
                        case "generacion_lista_60": c.GenList60 = ParseGenList(val); break;   // 19-11 (GENERACION: el selector de L3)
                        case "generacion_lista_30": c.GenList30 = ParseGenList(val); break;
                        case "magpie_vrr": c.MagpieVrr = ParseBool(val); break;
                        case "magpie_vrr_30_fg": c.MagpieVrr30Fg = ParseBool(val); break;
                        case "magpie_vrr_dxgi": c.VrrDxgi = ParseBool(val); break;
                        case "rtx_hdr": c.RtxHdr = ParseBool(val); break;
                        case "rtx_hdr_antes": c.RtxHdrBefore = ParseBool(val); break;
                        case "rtx_hdr_nits": c.RtxHdrNits = Math.Max(400, Math.Min(2000, ParseInt(val))); break;
                        case "rtx_hdr_gris": c.RtxHdrGray = Math.Max(10, Math.Min(100, ParseInt(val))); break;
                        case "rtx_hdr_contraste": c.RtxHdrContrast = Math.Max(0, Math.Min(200, ParseInt(val))); break;
                        case "rtx_hdr_saturacion": c.RtxHdrSaturation = Math.Max(0, Math.Min(200, ParseInt(val))); break;
                        case "reflex": c.Reflex = ParseBool(val); break;
                        // (1.2.4 ronda 6, QUITAFG: sin Magpie la imagen la hace siempre el motor propio; motor=magpie de un ini de antes no cuenta)
                        case "motor": c.Engine = "propio"; if (val.Trim().ToLowerInvariant() != "propio") Log.Write("motor=" + val.Trim() + " del ini no cuenta: sin Magpie, siempre el motor propio"); break;
                        case "motor_dlss5": break;   // (1.2.3 MOTOR5: 0 / 1 / auto. 1.2.4 SOLOMOTOR: ya no se elige; la puesta al dia la quita)
                        case "motor_dlss5_pasadas": break;   // (1.2.3 MOTOR5: hasta cuantas pasadas hacia el motor -las demas por Magpie-; 1.2.4 ronda 6: sin Magpie, las 1-3 siempre en el motor)
                        case "motor_dlss5_entrada": c.EngineDlss5PctAll = Math.Max(0, Math.Min(100, ParseInt(val))); break;
                        case "motor_dlss5_entrada_30": ParsePctList(val, c.EngineDlss5Pct30); break;
                        case "motor_dlss5_entrada_30_4k": ParsePctList(val, c.EngineDlss5Pct30_4k); break;
                        case "motor_ruta": c.EnginePath = val.Trim(); break;
                        case "motor_modelo_60": c.EnginePreset60 = val.Trim().ToUpperInvariant(); break;
                        case "motor_modelo_60_fg": c.EnginePreset60Fg = val.Trim().ToUpperInvariant(); break;
                        case "motor_modelo_30": c.EnginePreset30 = val.Trim().ToUpperInvariant(); break;
                        case "motor_modelo_30_fg": c.EnginePreset30Fg = val.Trim().ToUpperInvariant(); break;
                        case "motor_modelo_60_1080": case "motor_modelo_60_1440": case "motor_modelo_60_720":
                        case "motor_modelo_60_fg_1080": case "motor_modelo_60_fg_1440": case "motor_modelo_60_fg_720":
                        case "motor_modelo_30_1080": case "motor_modelo_30_1440": case "motor_modelo_30_720":
                        case "motor_modelo_30_fg_1080": case "motor_modelo_30_fg_1440": case "motor_modelo_30_fg_720":
                            c.EnginePresetByCap[key.Substring("motor_modelo_".Length)] = val.Trim().ToUpperInvariant(); break;
                        case "motor_entrada_60_1440": case "motor_entrada_60_fg_1440": case "motor_entrada_30_1440": case "motor_entrada_30_fg_1440":
                        case "motor_entrada_60_1080": case "motor_entrada_60_fg_1080": case "motor_entrada_30_1080": case "motor_entrada_30_fg_1080":
                            c.EngineInByCap[key.Substring("motor_entrada_".Length)] = val.Trim().ToLowerInvariant(); break;
                        case "motor_nitidez_transformer": c.EngineSharpenTransformer = Math.Max(0, Math.Min(1, ParseNum(val))); break;
                        case "motor_reflex": c.EngineReflex = Math.Max(0, Math.Min(2, ParseInt(val))); break;
                        case "motor_vrr": c.EngineVrr = Math.Max(0, Math.Min(1, ParseInt(val))); break;   // 20-11 (LATENCIA4)
                        case "motor_vrr_fg_vsync": c.EngineVrrFgVsync = Math.Max(-1, Math.Min(1, ParseInt(val))); break;
                        case "motor_dlss5_home": c.EngineDlss5Home = ParseInt(val) != 0; break;
                        case "motor_60fg_refresco_fijo": c.EngineFixed60Fg = ParseInt(val) != 0; break;   // 20-11 (LATENCIA5)
                        case "motor_extra": c.EngineExtra = val.Trim(); break;
                        case "puente_ligero": c.BridgeLight = ParseBool(val) || val.Trim() == "2"; c.BridgeLightLevel = val.Trim() == "2" ? 2 : 1; break;   // 17-11 17:00 (21-11, 1.2.1: 2 = FSR 1 siempre, src\SinTelon.cs)
                        case "pasadas_vigilancia": c.D5Watch = ParseInt(val); break;   // 18-11 (PASADAS)
                        case "pasadas_vigilancia_reinicios": c.D5WatchResets = Math.Max(3, ParseInt(val)); break;
                        case "pasadas_vigilancia_prueba": c.D5WatchTestLog = val.Trim(); break;
                        case "pasadas_vigilancia_imagen": c.D5WatchImage = ParseInt(val); break;
                        case "pasadas_vigilancia_imagen_umbral": c.D5ImageOsc = Math.Max(0.5, ParseNum(val)); break;
                        case "pasadas_vigilancia_imagen_pct": c.D5ImagePct = Math.Max(1, Math.Min(100, ParseNum(val))); break;
                        case "motor_en_espera": c.EngineStandby = ParseBool(val); break;
                        case "motor_reserva": c.EngineReserve = ParseBool(val); break;   // 21-11 (1.0.9): motor de reserva con la otra generacion (1 de serie)
                        case "dlss5_sin_telon": c.Dlss5Seamless = ParseBool(val); break;
                        case "dlss5_sin_telon_encima": c.Dlss5SeamlessOnTop = Math.Max(1, Math.Min(2, ParseInt(val))); break;
                        case "dlss5_sin_telon_foco": c.Dlss5SeamlessFocus = ParseBool(val); break;
                        case "dlss5_sin_telon_espera_ms": c.Dlss5SeamlessWaitMs = Math.Max(1000, Math.Min(15000, ParseInt(val))); break;
                        case "dlss5_sin_telon_puente": c.MagpieBridge = ParseBool(val); break;
                        case "dlss5_sin_telon_entrada": c.BridgeEntryFirst = ParseBool(val); break;
                        case "reflex_con_generacion": c.ReflexWithFg = ParseBool(val); break;
                        case "dxgi_60_sin_fg": c.VsyncNoFg = ParseBool(val); break;
                        case "magpie_sigue_fuente_60": c.Follow60NoFg = ParseBool(val); break;
                        case "magpie_ritmo_ntsc": c.NtscRate = ParseBool(val); break;
                        case "magpie_cola_cpu": NvProfiles.MagpiePreRender = Math.Max(-1, Math.Min(8, ParseInt(val))); break;
                        case "nvidia_latencia_ultra": NvProfiles.MagpieUll = ParseBool(val) ? 1 : 0; break;   // 19-11 (LATENCIA3)
                        case "mpv_gpu_prioridad": Perf.MpvGpuClass = val.Trim().ToLowerInvariant().StartsWith("t") || val.Trim() == "5" ? 5 : 4; break;
                        case "magpie_sm_vaciar_ms": c.SmDrainMs = Math.Max(0, Math.Min(1000, ParseInt(val))); break;          // 18-11 (LATENCIA)
                        case "magpie_sm_vaciar_tras_ms": c.SmDrainDelayMs = Math.Max(0, Math.Min(30000, ParseInt(val))); break;
                        case "magpie_sm_vaciar_auto": c.SmDrainAuto = ParseBool(val); break;          // 19-11 (LATENCIA3)
                        case "magpie_sm_vaciar_umbral": c.SmDrainThreshold = Math.Max(0.5, Math.Min(10, ParseNum(val))); break;
                        case "magpie_sm_vaciar_objetivo": c.SmDrainTarget = Math.Max(0, Math.Min(5, ParseInt(val))); break;
                        case "magpie_sm_vaciar_partido": c.SmDrainSplit = ParseBool(val); break;
                        case "magpie_sm_vaciar_log": c.SmDrainLog = ParseBool(val); break;
                        case "magpie_sm_vaciar_real": c.SmDrainReal = ParseBool(val); break;          // 19-11 (LATENCIA3, src\ColaReal.cs)
                        case "magpie_sm_vaciar_real_alto": c.SmRealHigh = Math.Max(1.1, Math.Min(6, ParseNum(val))); break;
                        case "magpie_sm_vaciar_real_objetivo": c.SmRealTarget = Math.Max(0.3, Math.Min(5, ParseNum(val))); break;
                        case "magpie_sm_vaciar_real_ganancia": c.SmRealGain = Math.Max(0.5, Math.Min(3, ParseNum(val))); break;
                        case "presentmon_exe": c.PresentMonExe = val.Trim().Trim('"'); break;
                        case "dxgi_30_fg_119": c.VsyncHalf119 = ParseBool(val); break;
                        case "dxgi_30_xess": c.VsyncHalfByType["xess"] = ParseBool(val); break;   // 19-11 (GENERACION)
                        case "dxgi_30_fsr3": c.VsyncHalfByType["fsr3"] = ParseBool(val); break;
                        case "magpie_repetidas": c.MagpieDuplicates = Math.Max(0, Math.Min(2, ParseInt(val))); break;
                        case "magpie_captura": c.MagpieCaptureMethod = Math.Max(0, ParseInt(val)); break;
                        // ---- mpv, HOME y telon ----
                        case "captura_juego_s": c.GameGrabEvery = Math.Max(0.3, ParseNum(val)); break;
                        case "ps5_reconocer_juegos": c.Ps5RecognizeGames = ParseBool(val); break;   // 20-11 (PS5JUEGO)
                        case "xbox_reconocer_juegos": c.XboxRecognizeGames = ParseBool(val); break; // 20-11 (PS5JUEGO; Xbox SIN PROBAR)
                        case "entrada_esperar_usuario": c.EnterWaitUser = ParseBool(val); break;
                        case "entrada_capturas": c.EnterGrabs = Math.Max(1, ParseInt(val)); break;
                        case "entrada_en_uso_capturas": c.EnterInUseGrabs = Math.Max(1, ParseInt(val)); break;
                        case "entrada_negro_tras_usuario_s": c.EnterBlackSecs = Math.Max(0.2, ParseNum(val)); break;
                        case "entrada_negro_s": c.EnterBlackLongSecs = Math.Max(0.5, ParseNum(val)); break;
                        case "entrada_max_s": c.EnterMaxSecs = Math.Max(3, ParseNum(val)); break;
                        case "entrada_captura_s": c.EnterGrabEvery = Math.Max(0.15, ParseNum(val)); break;
                        case "inicio_capturas": c.StartGrabs = Math.Max(2, ParseInt(val)); break;
                        case "inicio_capturas_al_encender": c.StartGrabsWake = Math.Max(2, ParseInt(val)); break;
                        case "inicio_quieto_s": c.StartStillSecs = Math.Max(1, ParseNum(val)); break;
                        case "inicio_captura_s": c.StartGrabEvery = Math.Max(0.15, ParseNum(val)); break;
                        case "home_espera_nativo_s": c.HomeGraceSecs = Math.Max(0, Math.Min(600, ParseNum(val))); break;
                        case "home_espera_quieto_s": c.HomeGraceStillSecs = Math.Max(0, Math.Min(30, ParseNum(val))); break;
                        case "telon_imagen_congelada": c.CurtainFrozen = ParseBool(val); break;
                        case "telon_fundido_ms": c.CurtainFrozenFadeMs = Math.Max(0, Math.Min(2000, ParseInt(val))); break;
                        case "telon_vivo": c.CurtainLive = val.Trim() == "2" ? 2 : ParseBool(val) ? 1 : 0; break;
                        case "entrada_directa": c.EnterLsDirect = ParseBool(val); break;
                        case "telon_entrada_ms": c.CurtainFadeInMs = Math.Max(0, Math.Min(1000, ParseInt(val))); break;
                        case "magpie_arranque_max_s": c.MagpieStartMaxSecs = Math.Max(5, Math.Min(90, ParseInt(val))); break;
                        case "telon_esperar_magpie_max_ms": c.CurtainSettleMaxMs = Math.Max(0, Math.Min(8000, ParseInt(val))); break;
                        case "dedupe": c.Dedupe = ParseBool(val); break;
                        case "repetidas_30_sin_perder": c.Dedupe30Keep = ParseBool(val); break;
                        case "edge_aa": c.EdgeAa = ParseBool(val); break;
                        case "quitar_tramado": c.Dedither = ParseBool(val); break;
                        case "tramado_fuerte": c.StrongDither = Math.Max(0, Math.Min(2, ParseInt(val))); break;
                        default:
                            if (key.StartsWith("tabla_") && c.ParseTable(key.Substring(6), val)) break;
                            // 19-11 (GENERACION): motor_modelo_ / motor_entrada_ con el tipo de generacion del motor (60_sm, 30_dlssg_1440...)
                            if (key.StartsWith("motor_modelo_") && (key.Substring(13).StartsWith("60_") || key.Substring(13).StartsWith("30_")))
                            { c.EnginePresetByCap[key.Substring(13)] = val.Trim().ToUpperInvariant(); break; }
                            if (key.StartsWith("motor_entrada_") && (key.Substring(14).StartsWith("60_") || key.Substring(14).StartsWith("30_")))
                            { c.EngineInByCap[key.Substring(14)] = val.Trim().ToLowerInvariant(); break; }
                            if (c.ParseD5Opt(key, val)) break; // (escalador_<modo>_dlss5 y demas: ver Config.D5ScalerTable)
                            if (c.ParseCierres(key, val)) break; // (20-11, CIERRES: apagado_confirmar y demas, src\Cierres.cs)
                            if (c.ParseRelevo(key, val)) break; // (1.2.4 arreglos, RELEVO: relevo_motor_espera, relevo_xess_magpie, src\Lanzador\RelevoMotor.cs)
                            if (c.ParseSonidoDoble(key, val)) break; // (21-11: sonido_doble, src\SonidoDoble.cs)
                            if (c.ParseCapturaSegura(key, val)) break; // (21-11, 1.2.1: captura_segura, src\Lanzador\CapturaSegura.cs)
                            if (key == "mpv_sin_marco") break; // (22-11, 1.2.1: la ventana de mpv sin marco para Magpie; 23-11, 1.2.4 SINMPV: sin mpv, no cuenta)
                            if (c.ParseTiempos(key, val)) break; // (22-11, 1.2.2: motor_en_espera_al_abrir_ms, magpie_dlss_sr_solo_cadena; src\Lanzador\TiemposCarga.cs)
                            if (key.StartsWith("mando_") && PadOptions.Parse(key, val)) break; // (18-11, MANDOS: modo PC, src\Mandos.cs)
                            Log.Write("Aviso: clave desconocida en ini: " + key); break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Valor no valido en ini para '" + key + "' (se usa el valor por defecto)", ex);
                }
            }
            if (c.PowerOffStallMs < 300) c.PowerOffStallMs = 300;
            // Un nivel mal escrito en el ini: se usa el de serie
            Config d = new Config();
            for (int i = 0; i < 4; i++)
            {
                string[] f = c.Dlss5Levels[i].Split(',');
                double x;
                bool ok = f.Length >= 4 && f.Length <= 5;
                for (int k = 0; ok && k < f.Length; k++) ok = double.TryParse(f[k].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) && x >= 0 && x <= 2;
                if (!ok)
                {
                    Log.Write("Valor no valido en ini para 'dlss5_nivel" + (i + 1) + "' (4 o 5 numeros de 0 a 2 con punto decimal): se usa el de serie");
                    c.Dlss5Levels[i] = d.Dlss5Levels[i];
                }
            }
            if (c.Dlss5Flow == 0 && c.Dlss5AntiFlicker >= 2)
            {
                Log.Write("Aviso: el antiparpadeo " + c.Dlss5AntiFlicker + " necesita flujo optico (dlss5_flujo): va con acumulacion estatica");
                c.Dlss5AntiFlicker = 1;
            }
            Integrado.ResolvePaths(c); // (18-11, INTEGRADO: mpv y motor dentro de la app si estan, magpie_actualizar)
            c.CeTiposTrasCargar(); // (1.2.4 ronda 6, QUITAFG: lo que va en lugar de DLSS FG en lo guardado, src\Lanzador\TiposGeneracion.cs)
            return c;
        }

        // tabla_modelo_<clave>=a,b / tabla_dlss5_<clave>=a,b,c / tabla_ligero_<clave>=0|1
        bool ParseTable(string k, string v)
        {
            int u = k.IndexOf('_');
            if (u <= 0) return false;
            string kind = k.Substring(0, u), mode = k.Substring(u + 1);
            string[] f = v.Split(',');
            if (kind == "modelo" && f.Length == 2) { ModelTable[mode] = new[] { ParseInt(f[0].Trim()), ParseInt(f[1].Trim()) }; return true; }
            if (kind == "modelo" && f.Length == 4) { ModelTable[mode] = new[] { ParseInt(f[0].Trim()), ParseInt(f[1].Trim()), ParseInt(f[2].Trim()), ParseInt(f[3].Trim()) }; return true; }
            if (kind == "dlss5" && f.Length == 3) { Dlss5PctTable[mode] = new[] { ParseInt(f[0].Trim()), ParseInt(f[1].Trim()), ParseInt(f[2].Trim()) }; return true; }
            if (kind == "ligero") { LightTable[mode] = ParseBool(v); return true; }
            if (kind == "cada" && f.Length == 3) { Dlss5EveryTable[mode] = new[] { ParseInt(f[0].Trim()), ParseInt(f[1].Trim()), ParseInt(f[2].Trim()) }; return true; }
            return false;
        }

        static string ParseScaler(string v)
        {
            v = v.Trim().ToLowerInvariant();
            if (v == "dlss" || v == "xess" || v == "fsr" || v == "fsr3" || v == "fsr4" || v == "ninguno") return v;
            if (v == "fsr1") return "fsr";
            if (v == "fsr2") return "fsr3";
            // (1.2.4 ronda 6, QUITAFG: los escaladores que solo tenia Magpie -RTX Video, NVIDIA VFX, SGSR, CAS, Lanczos, Bicubico- ya no estan:
            // el motor hace FSR 1 -el de siempre sin DLSS- o, los que solo estiran, sin reescalado)
            if (v == "vsr" || v == "rtx" || v == "rtxvideo" || v == "nvvfx" || v == "vfx" || v == "sgsr" || v == "cas") return "fsr";
            if (v == "lanczos" || v == "bicubic") return "ninguno";
            throw new FormatException("escalador: dlss, xess, fsr, fsr3, fsr4 o ninguno");
        }

        // (ParseFg, ParsePick y ParseGenList: src\Lanzador\TiposGeneracion.cs)

        static int ParseFlow(string v)
        {
            v = v.Trim().ToLowerInvariant();
            if (v == "0" || v == "no" || v == "ninguno") return 0;
            if (v == "1" || v == "amd" || v == "amdof") return 1;
            if (v == "2" || v == "nvidia" || v == "nvof") return 2;
            throw new FormatException("flujo: ninguno, amd o nvidia");
        }

        static int ParseInt(string v)
        {
            return int.Parse(v, CultureInfo.InvariantCulture);
        }

        // 20-11: "a,b,c" (un % por 1, 2 y 3 pasadas; con un solo numero, para las tres) -> dst (25-100)
        static void ParsePctList(string v, int[] dst)
        {
            string[] p = v.Split(',');
            for (int i = 0; i < dst.Length; i++)
            {
                string s = (i < p.Length ? p[i] : p[p.Length - 1]).Trim();
                if (s.Length > 0) dst[i] = Math.Max(25, Math.Min(100, ParseInt(s)));
            }
        }

        static double ParseNum(string v)
        {
            return double.Parse(v.Replace(',', '.'), CultureInfo.InvariantCulture);
        }

        static bool ParseBool(string v)
        {
            v = v.ToLowerInvariant();
            return v == "1" || v == "true" || v == "si" || v == "yes" || v == "on";
        }
    }

    // (1.2.4 ronda 6, QUITAFG, fase 2: aqui iban MagpieConfig -la config de Magpie: su cadena de efectos, el perfil, el atajo- y JsonWriter;
    // sin Magpie, fuera)

    // ===================================================================
    //  Ventanas ajenas (ocultar las de Streaming Center)
    // ===================================================================

    static class Win
    {
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int cmd);

        const int SW_HIDE = 0;

        // Oculta todas las ventanas visibles de nivel superior de un proceso. Devuelve cuantas.
        public static int HideProcessWindows(string processName)
        {
            HashSet<uint> pids = new HashSet<uint>();
            foreach (Process p in Process.GetProcessesByName(processName)) pids.Add((uint)p.Id);
            if (pids.Count == 0) return 0;
            int n = 0;
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pids.Contains(pid) && IsWindowVisible(h))
                {
                    ShowWindow(h, SW_HIDE);
                    n++;
                }
                return true;
            }, IntPtr.Zero);
            return n;
        }
    }

    // (1.2.4 ronda 6, QUITAFG, fase 2: aqui iba MagpieLive -abrir, cerrar y vigilar el proceso de Magpie-; sin Magpie, fuera)

    // ===================================================================
    //  Rendimiento durante la sesion (menos latencia, misma imagen):
    //  plan "Alto rendimiento" y prioridad alta (CPU y GPU) sin limitacion
    //  de energia (EcoQoS) para mpv y Lossless Scaling
    // ===================================================================

    // MSI Afterburner: cuando lee los sensores (memoria compartida MAHM, hora del ultimo sondeo en la cabecera +20). 08-11:
    // 44 de 56 tirones del escalado DLSS empezaron en la misma ventana de 35 ms que su sondeo de cada segundo
    // 09-11: tras varios reinicios de la grafica (nvlddmkm 153) Windows dejo la tele a 3840x2160 a 60 Hz en vez de 120 y no dejo
    // volver por programa (ChangeDisplaySettingsEx -> fallo): con 60 Hz LS no puede presentar 120 y la generacion se queda en
    // 30 imagenes reales. Se avisa al encenderla
    static class Pantalla
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency,
                dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);

        // 16-11 (G1): la tele a 119,88 Hz (Windows la da como "119"). Con Smooth Motion la captura de 59,94 sale a 119,88 (o a 59,94 en el modo
        // 30, que va cada 2 refrescos): el ritmo justo de la tele, y la cola de la sincronizacion vertical no se vacia nunca (banco: 52-58 ms
        // en el modo 60 y 100-112 ms en el 30, frente a 16-24 ms con la tele a 120,000). Ahi va DLSS FG (Config.FgTypeNow). Mirado cada 2 s
        static int hz119 = -1; static DateTime hz119At;
        public static bool Is119
        {
            get
            {
                DateTime now = DateTime.UtcNow;
                if (hz119 < 0 || (now - hz119At).TotalSeconds > 2) { hz119 = Hz() == 119 ? 1 : 0; hz119At = now; }
                return hz119 == 1;
            }
        }

        // Hz de la pantalla principal (0 = no se sabe)
        public static int Hz()
        {
            try
            {
                DEVMODE d = new DEVMODE();
                d.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                return EnumDisplaySettings(null, -1, ref d) ? d.dmDisplayFrequency : 0;
            }
            catch { return 0; }
        }
    }

    static class Afterburner
    {
        public static volatile int PhaseMs = -1;
        public static int PeriodMs;
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);
        // 09-11: en la sesion del 08/09-11, 11 de 18 tirones de 50-69 ms empezaron a -26..+10 ms de su lectura de sensores
        // (al azar saldrian 1-2) y, con la generacion, cada uno trae ~10 s de imagenes repetidas (el escalado deja de esperar
        // a la grafica). Con afterburner_pausa=1 se cierra al abrir la app con su propia tarea de inicio de sesion
        // (schtasks /end \MSIAfterburner: no pide permisos de administrador, probado) y se vuelve a abrir al salir (/run).
        // Solo si el ventilador no depende de el (SwAutoFanControl=0: lo lleva la tarjeta; su perfil de arranque va de serie)
        public static volatile bool Paused;
        const string AbTask = @"\MSIAfterburner";

        public static void Start(bool pause)
        {
            if (Process.GetProcessesByName("MSIAfterburner").Length == 0) { Log.Write("Afterburner: no esta abierto"); return; }
            string text = "";
            try { text = File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"MSI Afterburner\Profiles\MSIAfterburner.cfg")); }
            catch { }
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, @"(?m)^\s*HwPollPeriod\s*=\s*(\d+)");
            if (m.Success) PeriodMs = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            System.Text.RegularExpressions.Match fan = System.Text.RegularExpressions.Regex.Match(text, @"(?m)^\s*SwAutoFanControl\s*=\s*(\d+)");
            bool fanByCard = fan.Success && fan.Groups[1].Value == "0";
            Thread t = new Thread(delegate ()
            {
                if (pause && fanByCard && TryPause()) return;
                if (pause && !fanByCard) Log.Write("Afterburner: lleva el ventilador por software (o no se pudo leer su config): se deja abierto");
                Probe();
            });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        static bool TryPause()
        {
            try
            {
                Schtasks("/end /tn " + AbTask, true);
                Stopwatch sw = Stopwatch.StartNew();
                while (Process.GetProcessesByName("MSIAfterburner").Length > 0 && sw.ElapsedMilliseconds < 4000) Thread.Sleep(100);
                if (Process.GetProcessesByName("MSIAfterburner").Length > 0)
                {
                    Log.Write("Afterburner: no se ha podido cerrar (no lo abrio su tarea de inicio de sesion); se deja abierto");
                    return false;
                }
                Paused = true;
                Log.Write("Afterburner cerrado mientras la app este abierta (sus lecturas de sensores coincidian con los tirones); se vuelve a abrir al salir");
                return true;
            }
            catch (Exception ex) { Log.Error("Afterburner: cerrandolo", ex); return false; }
        }

        // Al salir de la app: se vuelve a abrir como al iniciar sesion (no se espera a que arranque)
        public static void Restore()
        {
            if (!Paused) return;
            Paused = false;
            try
            {
                if (Process.GetProcessesByName("MSIAfterburner").Length == 0) Schtasks("/run /tn " + AbTask, false);
                Log.Write("Afterburner: se vuelve a abrir");
            }
            catch (Exception ex) { Log.Error("Afterburner: abriendolo otra vez", ex); }
        }

        static void Schtasks(string args, bool wait)
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi)) { if (wait) p.WaitForExit(5000); }
        }
        static void Probe()
        {
            bool logged = false;
            while (true)
            {
                try
                {
                    using (MemoryMappedFile m = MemoryMappedFile.OpenExisting("MAHMSharedMemory", MemoryMappedFileRights.Read))
                    using (MemoryMappedViewAccessor v = m.CreateViewAccessor(0, 32, MemoryMappedFileAccess.Read))
                    {
                        if (v.ReadUInt32(0) != 0x4D41484D) throw new InvalidDataException("firma MAHM");
                        timeBeginPeriod(1);
                        try
                        {
                            int last = v.ReadInt32(20), ph = -1;
                            Stopwatch sw = Stopwatch.StartNew();
                            while (sw.ElapsedMilliseconds < 3500) { Thread.Sleep(2); int x = v.ReadInt32(20); if (x != last) { last = x; ph = DateTime.Now.Millisecond; } }
                            PhaseMs = ph;
                        }
                        finally { timeEndPeriod(1); }
                    }
                    if (!logged && PhaseMs >= 0)
                    {
                        logged = true;
                        Log.Write("Afterburner abierto: lee los sensores cada " + PeriodMs + " ms, a los ." + PhaseMs.ToString("000") +
                                  " s de cada segundo (si salen tirones \"(coincide)\", subir su periodo de sondeo o cerrarlo)");
                    }
                }
                catch (FileNotFoundException) { PhaseMs = -1; }
                catch (Exception ex) { Log.Error("Afterburner: memoria compartida", ex); PhaseMs = -1; }
                Thread.Sleep(300000);
            }
        }
    }

    // Al salir: lo que la app cambio al abrirse (programas de segundo plano, plan de energia, Afterburner), devuelto a la vez y
    // una sola vez. 09-11: antes iba uno tras otro despues de cerrar LS y la ventana; el plan de energia solo tarda ~0,45 s
    // (en el log, 0,4-0,5 s hasta "Plan de energia restaurado"). Con la consola apagada empieza en cuanto se cierran mpv y LS
    static class ExitRestore
    {
        static readonly object sync = new object();
        static Thread worker;

        public static void Start()
        {
            lock (sync)
            {
                if (worker != null) return;
                worker = new Thread(Run);
                worker.IsBackground = true;
                worker.Start();
            }
        }

        static void Run()
        {
            // 16-11 (motor_en_espera): el motor escondido en espera, fuera ya (sin esperarlo)
            try { TrayApp.KillStandbyAtExit("al salir"); } catch { }
            Stopwatch sw = Stopwatch.StartNew();
            long bg = -1, plan = -1, ab = -1;
            Thread tp = new Thread(delegate () { Stopwatch s = Stopwatch.StartNew(); Perf.EndSession(); plan = s.ElapsedMilliseconds; });
            Thread ta = new Thread(delegate () { Stopwatch s = Stopwatch.StartNew(); Afterburner.Restore(); ab = s.ElapsedMilliseconds; });
            tp.IsBackground = true;
            ta.IsBackground = true;
            tp.Start();
            ta.Start();
            try { Perf.RestoreBackground(); } catch (Exception ex) { Log.Error("Devolviendo los programas de segundo plano", ex); }
            // 15-11: Smooth Motion fuera del perfil de Magpie.exe si lo puso la app (el Magpie del usuario no lo lleva)
            try { NvProfiles.SmoothOffAtExit(); } catch (Exception ex) { Log.Error("Quitando Smooth Motion de Magpie.exe", ex); }
            bg = sw.ElapsedMilliseconds;
            tp.Join(4000);
            ta.Join(4000);
            Log.Write("Restaurado al salir en " + sw.ElapsedMilliseconds + " ms (a la vez: segundo plano " + bg + " ms, plan de energia " +
                      plan + " ms, Afterburner " + ab + " ms)");
        }

        // Al final de Main: que haya acabado (el plan de energia, sobre todo) antes de que termine el proceso
        public static void Finish()
        {
            Start();
            Thread w;
            lock (sync) w = worker;
            if (!w.Join(5000)) Log.Write("Aviso: restaurar lo de segundo plano / el plan de energia / Afterburner sigue sin acabar tras 5 s; se sale igualmente");
            // 16-11 (revision): otra vez al final: si tras empezar esto (PowerOffNow) algo relanzo Magpie (la recuperacion de un reinicio de
            // la grafica) y volvio a poner Smooth Motion o la sincronizacion vertical en su perfil. Sin nada puesto no hace nada
            try { NvProfiles.SmoothOffAtExit(); } catch (Exception ex) { Log.Error("Quitando Smooth Motion de Magpie.exe", ex); }
        }
    }

    // 16-11 (motor_en_espera): el motor en espera va en un "job" de Windows que lo cierra si la app se va sin poder hacerlo ella (un
    // fallo nativo, cerrarla desde el administrador de tareas): escondido y sin nadie que lo despierte no se quedaria para siempre.
    // Solo mientras esta en espera: despierto es la imagen, como un motor normal (sin el job a la app le da igual que muera)
    static class EngineJob
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObjectW(IntPtr attrs, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtLimits info, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION (la basica, IO_COUNTERS y los limites de memoria, en linea: mismo orden y relleno)
        [StructLayout(LayoutKind.Sequential)]
        struct ExtLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        const int JobObjectExtendedLimitInformation = 9;
        const uint KillOnJobClose = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        static readonly object sync = new object();
        static IntPtr job;
        static readonly Dictionary<int, DateTime> held = new Dictionary<int, DateTime>();
        static bool warned;

        // El motor al job (si no estaba) y si Windows lo cierra con la app (kill: en espera) o no (despierto)
        // 22-11 (1.2.2, ERRORES; pendiente de la revision de la 1.2.03 "huerfanos de mpv/motor sin job"): el "cerrar con la app" es de TODO el
        // job, no de cada proceso (y un proceso no sale de un job): despertar uno lo quitaba a todos -un motor de reserva escondido seguia
        // sin el- y poner otro en espera se lo devolvia al despierto. Ahora el job siempre cierra lo suyo con la app, y RunMpv mete tambien
        // la imagen (mpv y el motor recien abierto): si la app muere (administrador de tareas, un antivirus, un fallo nativo) no se quedan
        // con la capturadora y la pantalla sin nadie que los mande. kill solo dice para que se mete
        public static void Hold(Process p, bool kill)
        {
            try
            {
                lock (sync)
                {
                    if (job == IntPtr.Zero)
                    {
                        job = CreateJobObjectW(IntPtr.Zero, null);
                        if (job == IntPtr.Zero) { Warn("sin job (" + Marshal.GetLastWin32Error() + ")"); return; }
                        ExtLimits l = new ExtLimits();
                        l.LimitFlags = KillOnJobClose;
                        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref l, (uint)Marshal.SizeOf(typeof(ExtLimits))))
                            Warn("SetInformationJobObject " + Marshal.GetLastWin32Error());
                    }
                    if (p != null)
                    {
                        // (por pid y su hora de arranque: con cada mpv tambien, un pid de uno ya cerrado puede volver a salir)
                        DateTime st = DateTime.MinValue;
                        try { st = p.StartTime; } catch { }
                        DateTime ya;
                        if (held.TryGetValue(p.Id, out ya) && ya == st) return;
                        if (AssignProcessToJobObject(job, p.Handle)) held[p.Id] = st;
                        else Warn("AssignProcessToJobObject " + Marshal.GetLastWin32Error());
                    }
                }
            }
            catch (Exception ex) { Warn(ex.GetType().Name + ": " + ex.Message); }
        }

        static void Warn(string why)
        {
            if (warned) return;
            warned = true;
            Log.Write("Imagen (mpv / motor): sin el job de Windows (" + why + "): si la app muriera sin cerrarlos, se quedarian abiertos");
        }
    }

    static class Perf
    {
        [StructLayout(LayoutKind.Sequential)]
        struct PowerThrottlingState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref PowerThrottlingState info, uint size);
        [DllImport("gdi32.dll")]
        static extern int D3DKMTSetProcessSchedulingPriorityClass(IntPtr hProcess, int priority);
        [DllImport("powrprof.dll")]
        static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")]
        static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr p);

        const int ProcessPowerThrottling = 4;
        const uint ThrottleExecutionSpeed = 0x1;
        const uint ThrottleIgnoreTimerResolution = 0x4;
        const int GpuPriorityHigh = 4; // D3DKMT_SCHEDULINGPRIORITYCLASS_HIGH
        const int GpuPriorityIdle = 0, GpuPriorityNormal = 2;

        static readonly Guid HighPerformance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

        // Plan anterior; se guarda en disco por si el lanzador muere sin restaurarlo
        static string PlanFile
        {
            get { return Path.Combine(Path.GetTempPath(), "consoleenhancer_powerplan.txt"); }
        }

        static Guid? previousPlan;

        public static void BeginSession()
        {
            try
            {
                Guid current = GetActivePlan();
                Guid prev = current;
                // Sesion anterior sin terminar: el plan bueno es el que se guardo entonces
                if (File.Exists(PlanFile)) prev = new Guid(File.ReadAllText(PlanFile).Trim());
                if (current == HighPerformance && prev == HighPerformance) return;
                File.WriteAllText(PlanFile, prev.ToString());
                Guid hp = HighPerformance;
                uint rc = PowerSetActiveScheme(IntPtr.Zero, ref hp);
                if (rc != 0)
                {
                    Log.Write("No se pudo activar el plan Alto rendimiento (codigo " + rc + ")");
                    File.Delete(PlanFile);
                    return;
                }
                previousPlan = prev;
                Log.Write("Plan de energia: Alto rendimiento (se restaura " + prev + " al salir)");
            }
            catch (Exception ex) { Log.Error("Cambiando el plan de energia", ex); }
        }

        public static void EndSession()
        {
            if (previousPlan == null) return;
            try
            {
                Guid prev = previousPlan.Value;
                uint rc = PowerSetActiveScheme(IntPtr.Zero, ref prev);
                if (rc == 0) File.Delete(PlanFile);
                Log.Write("Plan de energia restaurado: " + prev + (rc == 0 ? "" : " (codigo " + rc + ")"));
            }
            catch (Exception ex) { Log.Error("Restaurando el plan de energia", ex); }
            previousPlan = null;
        }

        static Guid GetActivePlan()
        {
            IntPtr p;
            uint rc = PowerGetActiveScheme(IntPtr.Zero, out p);
            if (rc != 0) throw new InvalidOperationException("PowerGetActiveScheme codigo " + rc);
            try { return (Guid)Marshal.PtrToStructure(p, typeof(Guid)); }
            finally { LocalFree(p); }
        }

        public static void BoostByName(string processName)
        {
            foreach (Process p in Process.GetProcessesByName(processName)) Boost(p, processName);
        }

        // Prioridad alta de GPU de nuevo: a mpv recien lanzado le fallaba siempre (0xC000000D, aun sin dispositivo grafico);
        // con su imagen ya en pantalla, si
        // 19-11 (FLUIDEZ): clase de GPU de mpv (ini mpv_gpu_prioridad: 4 alta, 5 tiempo real = la de Magpie)
        public static int MpvGpuClass = GpuPriorityHigh;
        public static void GpuHigh(Process p, string name)
        {
            try
            {
                int cls = name == "mpv" ? MpvGpuClass : GpuPriorityHigh;
                int st = D3DKMTSetProcessSchedulingPriorityClass(p.Handle, cls);
                if (st != 0 && cls > GpuPriorityHigh) { int st2 = D3DKMTSetProcessSchedulingPriorityClass(p.Handle, GpuPriorityHigh); Log.Write("Prioridad de GPU de " + name + ": tiempo real no aplicada (0x" + st.ToString("X8") + ")"); st = st2; cls = GpuPriorityHigh; }
                Log.Write("Prioridad de GPU de " + name + (st == 0 ? (cls > GpuPriorityHigh ? ": tiempo real" : ": alta") : " no aplicada (0x" + st.ToString("X8") + ")"));
            }
            catch (Exception ex) { Log.Error("Prioridad de GPU de " + name, ex); }
        }

        // Programas que siguen dibujando de fondo mientras se juega (medido el 03-11: Playnite en pantalla completa, 15 % de la
        // grafica y 38 % de un nucleo sin usarse): prioridad minima de CPU y de GPU y con EcoQoS hasta cerrar la app
        static readonly List<KeyValuePair<Process, ProcessPriorityClass>> throttled = new List<KeyValuePair<Process, ProcessPriorityClass>>();

        public static void ThrottleBackground(string[] names)
        {
            foreach (string n in names)
                foreach (Process p in Process.GetProcessesByName(n.Trim()))
                {
                    try
                    {
                        ProcessPriorityClass old = p.PriorityClass;
                        // 16-11: ya en Idle = lo dejo asi una sesion anterior que no pudo devolverlo (cerrada de golpe): se apuntaba Idle como
                        // "lo de antes" y Playnite se quedaba en Idle para siempre. Al acabar, Normal
                        if (old == ProcessPriorityClass.Idle) old = ProcessPriorityClass.Normal;
                        p.PriorityClass = ProcessPriorityClass.Idle;
                        PowerThrottlingState s = new PowerThrottlingState();
                        s.Version = 1;
                        s.ControlMask = ThrottleExecutionSpeed;
                        s.StateMask = ThrottleExecutionSpeed; // EcoQoS puesto
                        SetProcessInformation(p.Handle, ProcessPowerThrottling, ref s, (uint)Marshal.SizeOf(s));
                        int st = D3DKMTSetProcessSchedulingPriorityClass(p.Handle, GpuPriorityIdle);
                        lock (throttled) throttled.Add(new KeyValuePair<Process, ProcessPriorityClass>(p, old));
                        // Y minimizado: con la prioridad baja seguia dibujando (40 % de un nucleo); minimizado deja de dibujar
                        // (medido el 03-11: 2 % de CPU y 0 % de GPU). Al cerrar la app vuelve a estar delante
                        IntPtr h = p.MainWindowHandle;
                        bool mini = h != IntPtr.Zero && IsWindowVisible(h) && !IsIconic(h) && ShowWindow(h, 6); // SW_MINIMIZE
                        if (mini) lock (minimized) minimized.Add(h);
                        Log.Write("De fondo mientras se juega: " + p.ProcessName + " (pid " + p.Id + ")" + (mini ? " minimizado," : "") +
                                  " con la prioridad minima de CPU" + (st == 0 ? " y de GPU" : "") + " (se le devuelve todo al cerrar la app)");
                    }
                    catch (Exception ex) { Log.Error("Bajando la prioridad de " + n, ex); }
                }
        }

        public static void RestoreBackground()
        {
            lock (throttled)
            {
                foreach (KeyValuePair<Process, ProcessPriorityClass> kv in throttled)
                {
                    try
                    {
                        if (kv.Key.HasExited) continue;
                        kv.Key.PriorityClass = kv.Value;
                        PowerThrottlingState s = new PowerThrottlingState();
                        s.Version = 1; // sin control: lo decide Windows, como antes
                        SetProcessInformation(kv.Key.Handle, ProcessPowerThrottling, ref s, (uint)Marshal.SizeOf(s));
                        D3DKMTSetProcessSchedulingPriorityClass(kv.Key.Handle, GpuPriorityNormal);
                        Log.Write("Prioridad devuelta a " + kv.Key.ProcessName);
                    }
                    catch (Exception ex) { Log.Error("Devolviendo la prioridad", ex); }
                }
                throttled.Clear();
            }
            lock (minimized)
            {
                foreach (IntPtr h in minimized) ShowWindow(h, 9); // SW_RESTORE
                // 16-11 (agente PLAYNITE): restaurada pero sin activar, Playnite en pantalla completa no hace caso al mando hasta que
                // se la activa a mano: se le da el primer plano (la primera, la de pantalla completa si la hay: el orden de Background)
                if (minimized.Count > 0) Activate(minimized[0]);
                minimized.Clear();
            }
        }

        // Primer plano a una ventana de otro proceso: como RestoreWindow de Playnite, con la entrada unida a la del hilo de delante;
        // si aun asi Windows no lo deja, con una pulsacion de Alt (la regla de Windows: tras una pulsacion se puede)
        static void Activate(IntPtr h)
        {
            try
            {
                if (h == IntPtr.Zero || !IsWindowVisible(h)) return;
                IntPtr fg = GetForegroundWindow();
                if (fg == h) return;
                uint fgPid;
                uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out fgPid) : 0;
                uint me = GetCurrentThreadId();
                bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
                BringWindowToTop(h);
                SetForegroundWindow(h);
                if (attached) AttachThreadInput(me, fgThread, false);
                if (GetForegroundWindow() != h)
                {
                    keybd_event(0x12, 0, 0, (UIntPtr)0x50434D44u);
                    SetForegroundWindow(h);
                    keybd_event(0x12, 0, 2, (UIntPtr)0x50434D44u); // KEYEVENTF_KEYUP (1.0.12: con la marca de la app)
                }
                Log.Write("Playnite delante y activo al cerrar la app" + (GetForegroundWindow() == h ? "" : " (Windows no lo dejo)"));
            }
            catch (Exception ex) { Log.Error("Activando Playnite", ex); }
        }

        static readonly List<IntPtr> minimized = new List<IntPtr>();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        // Prioridad alta en CPU y GPU, y sin EcoQoS: Windows 11 frena los procesos
        // cuya ventana queda tapada, y la de mpv la tapa Lossless Scaling
        public static void Boost(Process p, string name)
        {
            List<string> done = new List<string>();
            try
            {
                if (p.PriorityClass != ProcessPriorityClass.High && p.PriorityClass != ProcessPriorityClass.RealTime)
                    p.PriorityClass = ProcessPriorityClass.High;
                done.Add("CPU alta");
            }
            catch (Exception ex) { Log.Error("Prioridad de CPU de " + name, ex); }
            try
            {
                PowerThrottlingState s = new PowerThrottlingState();
                s.Version = 1;
                s.ControlMask = ThrottleExecutionSpeed | ThrottleIgnoreTimerResolution;
                s.StateMask = 0; // controlado y desactivado
                if (SetProcessInformation(p.Handle, ProcessPowerThrottling, ref s, (uint)Marshal.SizeOf(s)))
                    done.Add("sin EcoQoS");
                else
                    Log.Write("SetProcessInformation fallo para " + name + " (error " + Marshal.GetLastWin32Error() + ")");
            }
            catch (Exception ex) { Log.Error("Limitacion de energia de " + name, ex); }
            try
            {
                int st = D3DKMTSetProcessSchedulingPriorityClass(p.Handle, GpuPriorityHigh);
                if (st == 0) done.Add("GPU alta");
                else if (name != "mpv") Log.Write("Prioridad de GPU de " + name + " no aplicada (0x" + st.ToString("X8") + ")"); // (mpv: se pone despues, Perf.GpuHigh)
            }
            catch (Exception ex) { Log.Error("Prioridad de GPU de " + name, ex); }
            Log.Write("Rendimiento " + name + " (pid " + p.Id + "): " + string.Join(", ", done.ToArray()));
        }
    }

    // ===================================================================
    //  Perfiles de NVIDIA (NVAPI DRS, lo mismo que NVIDIA App): "Maximo
    //  rendimiento preferido" para mpv y Lossless Scaling, para que la GPU
    //  no baje relojes entre fotogramas (cada fotograma se procesa antes)
    // ===================================================================

    static class NvProfiles
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QueryInterface64(uint id);
        [DllImport("nvapi.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QueryInterface32(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnVoid();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnOutHandle(out IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnHandle(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnFindApp(IntPtr session, IntPtr appName, out IntPtr profile, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnFindProfile(IntPtr session, IntPtr name, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnCreateProfile(IntPtr session, IntPtr info, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnCreateApp(IntPtr session, IntPtr profile, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetSetting(IntPtr session, IntPtr profile, uint id, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnSetSetting(IntPtr session, IntPtr profile, IntPtr setting);

        // Ids de funciones de NVAPI
        const uint IdInitialize = 0x0150E828;
        const uint IdCreateSession = 0x0694D52E;
        const uint IdDestroySession = 0xDAD9CFF8;
        const uint IdLoadSettings = 0x375DBD6B;
        const uint IdSaveSettings = 0xFCBC7E14;
        const uint IdFindApplicationByName = 0xEEE566B2;
        const uint IdFindProfileByName = 0x7E4A9A0B;
        const uint IdCreateProfile = 0xCC176068;
        const uint IdCreateApplication = 0x4347A9DE;
        const uint IdGetSetting = 0x73BF8338;
        const uint IdSetSetting = 0x577DD202;

        const uint PowerManagementMode = 0x1057EB71; // "Power management mode"
        const uint PreferMaxPerformance = 1;
        // Latencia: el modo "latencia baja" del panel de NVIDIA en Ultra (el driver entrega cada imagen justo a tiempo, sin cola;
        // lo mas parecido a Reflex que se puede poner desde fuera) y como mucho 1 imagen en cola (IDs de NVAPI / NVIDIA Profile Inspector)
        const uint PreRenderLimit = 0x007BA09E;       // Max pre-rendered frames
        const uint UllCplState = 0x0005F543;          // Ultra Low Latency - CPL State: 0 off, 1 on, 2 ultra
        const uint UllEnabled = 0x10835000;           // Ultra Low Latency - Enabled
        public static int MagpiePreRender = -1;   // -1 = el de Wanted (1)
        // 19-11 (LATENCIA3): ini nvidia_latencia_ultra. 1 = Ultra (lo que pedia Wanted y nunca se puso: -160). Medido 1440p 1 pasada: 30 sin
        // generacion 45,3 -> 41,7 ms (ruido), 30 + Smooth Motion 158 -> 215 ms y DLSS FG 86 -> 104 ms (peor): de serie 0 = quitado
        public static int MagpieUll = 0;
        static readonly uint[][] Wanted = { new[] { PowerManagementMode, PreferMaxPerformance }, new[] { PreRenderLimit, 1u }, new[] { UllCplState, 2u }, new[] { UllEnabled, 1u } };

        // Estructuras de NVAPI (v1) y desplazamientos de sus campos
        const int StringBytes = 4096;            // NvAPI_UnicodeString = 2048 wchar
        const int SettingSize = 12320;           // NVDRS_SETTING_V1
        const int SettingIdOffset = 4100;
        const int SettingLocationOffset = 4108;
        const int SettingCurrentOffset = 8220;
        const int ProfileSize = 4116;            // NVDRS_PROFILE_V1
        const int ProfileGpuSupportOffset = 4100;
        const int AppSize = 12296;               // NVDRS_APPLICATION_V1
        const int AppNameOffset = 8;

        const int NvOk = 0;
        const int NvProfileNotFound = -163;
        const int NvExecutableNotFound = -166;

        public static readonly string[] Exes = { };   // (1.2.4 ronda 6, QUITAFG: sin Magpie.exe; el motor lleva su perfil aparte: SetEngineSmooth / SetEngineFixed) (23-11, SINMPV: y sin mpv.exe: ya no se abre y no se toca su perfil)

        public const int Unchanged = 0, Changed = 1, Failed = -1;

        static T Fn<T>(uint id) where T : class
        {
            IntPtr p = IntPtr.Size == 8 ? QueryInterface64(id) : QueryInterface32(id);
            if (p == IntPtr.Zero) throw new InvalidOperationException("NVAPI sin la funcion 0x" + id.ToString("X8"));
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        static void Check(int rc, string what)
        {
            if (rc != NvOk) throw new InvalidOperationException(what + " -> NVAPI " + rc);
        }

        // Pone "Maximo rendimiento preferido" en el perfil de cada exe (lo crea si no
        // tiene). Solo guarda si hay cambios. saveDenied = el driver no dejo guardar.
        public static int Apply(out bool saveDenied)
        {
            saveDenied = false;
            if (Ce.NoNvidia) return Unchanged; // (ConsoleEnhancer: sin NVIDIA no hay perfiles)
            List<IntPtr> mem = new List<IntPtr>();
            IntPtr session = IntPtr.Zero;
            try
            {
                Check(Fn<FnVoid>(IdInitialize)(), "Initialize");
                Check(Fn<FnOutHandle>(IdCreateSession)(out session), "CreateSession");
                Check(Fn<FnHandle>(IdLoadSettings)(session), "LoadSettings");

                List<string> changes = new List<string>();
                foreach (string exe in Exes)
                {
                    IntPtr profile;
                    int rc = Fn<FnFindApp>(IdFindApplicationByName)(session, Str(mem, exe), out profile, Alloc(mem, AppSize));
                    if (rc == NvExecutableNotFound)
                    {
                        string name = "ConsoleEnhancer - " + exe;
                        rc = Fn<FnFindProfile>(IdFindProfileByName)(session, Str(mem, name), out profile);
                        if (rc == NvProfileNotFound)
                        {
                            IntPtr info = Alloc(mem, ProfileSize);
                            WriteString(info, 4, name);
                            Marshal.WriteInt32(info, ProfileGpuSupportOffset, 1); // GeForce
                            Check(Fn<FnCreateProfile>(IdCreateProfile)(session, info, out profile), "CreateProfile " + name);
                        }
                        else Check(rc, "FindProfileByName " + name);
                        IntPtr app = Alloc(mem, AppSize);
                        WriteString(app, AppNameOffset, exe);
                        Check(Fn<FnCreateApp>(IdCreateApplication)(session, profile, app), "CreateApplication " + exe);
                        changes.Add(exe + ": perfil creado");
                    }
                    else Check(rc, "FindApplicationByName " + exe);

                    // Solo cuenta lo puesto en el propio perfil (no lo heredado del global)
                    foreach (uint[] want0 in Wanted)
                    {
                        uint[] want = want0;
                        // 19-11 (FLUIDEZ): imagenes en cola de Magpie.exe (ini magpie_cola_cpu; 0 = la que pida la aplicacion)
                        if (want0[0] == PreRenderLimit && exe == "Magpie.exe" && MagpiePreRender >= 0) want = new[] { PreRenderLimit, (uint)MagpiePreRender };
                        bool ull = want0[0] == UllCplState || want0[0] == UllEnabled;
                        IntPtr s = Alloc(mem, SettingSize);
                        rc = Fn<FnGetSetting>(IdGetSetting)(session, profile, want[0], s);
                        // 19-11 (LATENCIA3): los de latencia ultra baja el NvAPI publico los rechaza (-160) en este driver (610.88): van con
                        // el privado GetSettingEx / SetSettingEx (como Smooth Motion). Hasta hoy NUNCA se habian puesto
                        if (rc == NvSettingNotFound && ull) rc = Fn<FnGetSettingEx>(IdGetSettingEx)(session, profile, want[0], s, Alloc(mem, 64));
                        bool own = rc == NvOk && Marshal.ReadInt32(s, SettingLocationOffset) == 0;
                        if (ull && MagpieUll == 0)
                        {
                            // (ini nvidia_latencia_ultra=0: quitado del perfil, como antes de hoy)
                            if (own)
                            {
                                int d = Fn<FnDelSetting>(IdDeleteProfileSettingEx)(session, profile, want[0]);
                                if (d == NvOk) changes.Add(exe + ": 0x" + want[0].ToString("X8") + " quitado");
                            }
                            continue;
                        }
                        if (own && (uint)Marshal.ReadInt32(s, SettingCurrentOffset) == want[1]) continue;
                        IntPtr w = Alloc(mem, SettingSize);
                        Marshal.WriteInt32(w, SettingIdOffset, (int)want[0]);
                        Marshal.WriteInt32(w, SettingCurrentOffset, (int)want[1]);
                        int src = Fn<FnSetSetting>(IdSetSetting)(session, profile, w);
                        if (src == NvSettingNotFound && ull) src = Fn<FnSetSettingEx>(IdSetSettingEx)(session, profile, w, 0, 0);
                        if (src != NvOk) { Log.Write("NVIDIA: " + exe + " ajuste 0x" + want[0].ToString("X8") + " no aceptado (NVAPI " + src + ")"); continue; }
                        changes.Add(exe + ": 0x" + want[0].ToString("X8") + " " + (own ? Marshal.ReadInt32(s, SettingCurrentOffset).ToString() : "(heredado)") + " -> " + want[1]);
                    }
                }
                if (changes.Count == 0) return Unchanged;

                int save = Fn<FnHandle>(IdSaveSettings)(session);
                if (save != NvOk)
                {
                    saveDenied = true;
                    Log.Write("NVIDIA: no se pudieron guardar los perfiles (NVAPI " + save + ")");
                    return Failed;
                }
                Log.Write("NVIDIA: " + string.Join(", ", changes.ToArray()));
                return Changed;
            }
            catch (Exception ex)
            {
                Log.Error("NVIDIA: perfiles", ex);
                return Failed;
            }
            finally
            {
                if (session != IntPtr.Zero)
                {
                    try { Fn<FnHandle>(IdDestroySession)(session); } catch { }
                }
                foreach (IntPtr p in mem) Marshal.FreeHGlobal(p);
            }
        }

        // ---- 15-11: NVIDIA Smooth Motion para Magpie.exe (ini generacion_tipo_60=smooth, Config.SmoothNow) ----
        // Ajuste oculto del driver 616.92: 0xB0D384C0 = Smooth Motion 0/1 (lo mira nvldumdx.dll al cargar el driver en el proceso;
        // 0xB0CC0875 = APIs permitidas, sin poner = todas). El NvAPI_DRS_SetSetting publico lo rechaza (-160, como si no existiera, igual
        // que los de latencia ultra baja): va con el privado NvAPI_DRS_SetSettingEx, el de la NVIDIA App (ids de NVAPI sacados de sus
        // llamadas en NvBackend64.dll, en orden alfabetico entre las publicas) y se quita con NvAPI_DRS_DeleteProfileSettingEx. Con el, en el
        // mismo perfil, la sincronizacion vertical forzada: la global esta forzada a desactivada y Smooth Motion presentaba con cortes
        // (ALLOW_TEARING, medido con PresentMon). Sin administrador. Vale para los procesos que se abren despues: MagpieConfig.Patch lo
        // pone con Magpie cerrado, como su config
        const uint IdSetSettingEx = 0x8A2CF5F5, IdGetSettingEx = 0xEA99498D, IdDeleteProfileSettingEx = 0xD20D29DF, IdDeleteProfileSetting = 0xE4A26362;
        const uint SmoothMotionEnable = 0xB0D384C0, VerticalSync = 0x00A879CF, VerticalSyncForceOn = 0x47814940;
        // 16-11 (G1, banco con PresentMon): en el modo 30 Smooth Motion presenta la inventada y la real seguidas (0,4 ms); con la
        // sincronizacion forzada a 120 Hz salen en dos refrescos seguidos y luego nada en dos (8,3 / 25 ms: el 50 % de las imagenes
        // dura 1 refresco y el 41 % 3, va a saltos). Con la del driver "cada 2 refrescos" (VSYNCMODE_FLIPINTERVAL2) cada una dura 2
        // refrescos (16,7 ms: el 99,9 % de 1798). Solo en el modo 30 y con la tele a 100 Hz o mas (a 60 Hz serian 30 en pantalla)
        const uint VerticalSyncHalf = 0x32610244;
        const int NvSettingNotFound = -160;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnSetSettingEx(IntPtr session, IntPtr profile, IntPtr setting, uint a, uint b);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetSettingEx(IntPtr session, IntPtr profile, uint id, IntPtr setting, IntPtr notUsed);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnDelSetting(IntPtr session, IntPtr profile, uint id);
        static readonly object smoothSync = new object();
        static int smoothState = -1;               // en el perfil de Magpie.exe: -1 sin mirar, 0 no, 1 si (VSync forzada), 2 si (VSync cada 2 refrescos)
        public static volatile bool SmoothBroken;  // el driver no deja ponerlo: la generacion vuelve a DLSS FG (Config.FgTypeNow)

        // Smooth Motion puesto ahora en el perfil de Magpie.exe (se mira una vez; luego lo que ha puesto la app): 0 no, 1 con la
        // sincronizacion vertical forzada (modo 60), 2 con la de cada 2 refrescos (modo 30)
        public static int SmoothState { get { lock (smoothSync) { if (smoothState < 0) smoothState = SmoothRead(); return smoothState; } } }
        public static bool SmoothOn { get { int s = SmoothState; return s == 1 || s == 2; } }
        // 16-11 04:20 (G1): estado 3 = sin Smooth Motion, solo la sincronizacion vertical forzada (el 60 sin generacion por DXGI). Si el driver
        // no la deja poner, el 60 sin generacion vuelve a DirectComposition (Config.VsyncNoFgNow): nunca DXGI sin sincronizacion (cortes)
        public static volatile bool VsyncBroken;
        // (16-11 10:10: estado 4 = sin Smooth Motion, solo la sincronizacion cada 2 refrescos: el modo 30 con DLSS FG por DXGI)
        static int SmoothRead() { if (SmoothRw(-1)) return SmoothRw(-2) ? 2 : 1; if (SmoothRw(-3)) return 3; return SmoothRw(-2) ? 4 : 0; }

        // Lo pone (Smooth Motion + sincronizacion vertical: 1 forzada, 2 cada 2 refrescos) o lo quita (0: los dos vuelven a lo
        // heredado). false = no se pudo
        public static bool SetSmooth(int want)
        {
            lock (smoothSync)
            {
                if (smoothState < 0) smoothState = SmoothRead();
                if (smoothState == want) return true;
                Stopwatch sw = Stopwatch.StartNew();
                bool ok = SmoothRw(want);
                if (ok) smoothState = want;
                else if (want == 1 || want == 2) SmoothBroken = true;
                else if (want == 3 || want == 4) VsyncBroken = true;
                Log.Write("NVIDIA: Smooth Motion para Magpie.exe " + (want == 2 ? "encendido (sincronizacion vertical cada 2 refrescos: modo 30)" :
                                                                      want == 1 ? "encendido (con sincronizacion vertical forzada)" :
                                                                      want == 3 ? "apagado, solo la sincronizacion vertical forzada (60 sin generacion por DXGI)" :
                                                                      want == 4 ? "apagado, solo la sincronizacion cada 2 refrescos (el modo 30 por DXGI)" : "apagado") +
                          (ok ? "" : " NO SE PUDO" + (want == 1 || want == 2 ? " (la generacion va con DLSS FG)" : want == 3 ? " (el 60 sin generacion sigue por DirectComposition)" :
                                                              want == 4 ? " (el 30 con DLSS FG sigue por DirectComposition)" : "")) +
                          " en " + sw.ElapsedMilliseconds + " ms");
                return ok;
            }
        }

        // Al salir de la app: quitado si lo puso ella (asi el Magpie del usuario no lo coge)
        public static void SmoothOffAtExit()
        {
            bool on;
            lock (smoothSync) on = smoothState > 0;
            if (on) SetSmooth(0);
            lock (smoothSync) on = engineSmoothState > 0;   // (17-11 16:55: y el del motor)
            if (on) SetEngineSmooth(0);
            lock (smoothSync) on = engineFixedState > 0;    // (20-11 LATENCIA5: y el refresco fijo del motor)
            if (on) SetEngineFixed(false);
        }

        // ---- 17-11 16:55: Smooth Motion para ConsoleEnhancerEngine.exe (ini motor_generacion_60=smooth, Config.EngineSmoothWanted) ----
        // Con la sincronizacion vertical forzada (sin cortes). Lo mira el driver al abrir el proceso: se pone justo antes de lanzar un
        // motor (EngineArgs)
        static int engineSmoothState = -1;            // en el perfil de ConsoleEnhancerEngine.exe: -1 sin mirar, 0 no, 1 si
        public static volatile bool EngineSmoothBroken; // el driver no deja ponerlo: el motor sigue con DLSS-G
        static int EngineSmoothRead() { return SmoothRw(-1, "ConsoleEnhancerEngine.exe") ? (SmoothRw(-2, "ConsoleEnhancerEngine.exe") ? 2 : SmoothRw(-3, "ConsoleEnhancerEngine.exe") ? 1 : 3) : 0; }   // (1.2.4 ronda 6, SMX4: 3 = sin la sincronizacion del perfil)
        public static bool EngineSmoothOn { get { lock (smoothSync) { if (engineSmoothState < 0) engineSmoothState = EngineSmoothRead(); return engineSmoothState > 0; } } }
        // want: 0 quitar, 1 con la sincronizacion forzada (modo 60), 2 con la de cada 2 refrescos (modo 30), 3 sin tocar la sincronizacion (la de
        // la aplicacion: Smooth Motion x4 encima de XeSS FG x2 con VRR, src\Lanzador\SmoothX4.cs)
        public static bool SetEngineSmooth(int want)
        {
            bool on = want > 0;
            lock (smoothSync)
            {
                if (engineSmoothState < 0) engineSmoothState = EngineSmoothRead();
                if (engineSmoothState == want) return true;
                Stopwatch sw = Stopwatch.StartNew();
                bool ok = SmoothRw(want == 3 ? 5 : want == 2 ? 2 : want == 1 ? 1 : 0, "ConsoleEnhancerEngine.exe");   // (1.2.4 ronda 6, SMX4: 3 = x4, src\Lanzador\SmoothX4.cs)
                if (ok) engineSmoothState = want;
                else if (on) EngineSmoothBroken = true;
                Log.Write("NVIDIA: Smooth Motion para ConsoleEnhancerEngine.exe " + (want == 3 ? "encendido sin tocar la sincronizacion (x4: encima de XeSS FG x2, VRR)" : want == 2 ? "encendido (sincronizacion cada 2 refrescos: modo 30)" : on ? "encendido (con sincronizacion vertical forzada)" : "apagado") +
                          (ok ? "" : " NO SE PUDO" + (on ? " (el motor sigue con DLSS FG)" : "")) + " en " + sw.ElapsedMilliseconds + " ms");
                return ok;
            }
        }

        // ---- 20-11 (LATENCIA5): refresco fijo (G-SYNC / VRR apagado) solo para ConsoleEnhancerEngine.exe, en el modo 60 con DLSS FG
        // (motor_60fg_refresco_fijo). Con VRR, Reflex limita la salida a 116/s (2 imagenes reales perdidas por segundo); con el refresco
        // fijo no hay tope: Streamline presenta con la sincronizacion de siempre (sin cortes) y salen las 59,94. Lo mira el driver al abrir el
        // proceso: se pone justo antes de lanzar un motor (EngineArgs) y si cambia se vuelve a abrir (CheckEngineFixed)
        const uint VrrAppOverride = 0x10A879CF, VrrAppOverrideRequest = 0x10A879AC, VrrFixedRefresh = 4;
        static int engineFixedState = -1;             // en el perfil de ConsoleEnhancerEngine.exe: -1 sin mirar, 0 no, 1 si
        public static volatile bool EngineFixedBroken;
        public static bool EngineFixedOn { get { lock (smoothSync) { if (engineFixedState < 0) engineFixedState = FixedRw(-1) ? 1 : 0; return engineFixedState > 0; } } }
        public static bool SetEngineFixed(bool on)
        {
            lock (smoothSync)
            {
                if (engineFixedState < 0) engineFixedState = FixedRw(-1) ? 1 : 0;
                if (engineFixedState == (on ? 1 : 0)) return true;
                Stopwatch sw = Stopwatch.StartNew();
                bool ok = FixedRw(on ? 1 : 0);
                if (ok) engineFixedState = on ? 1 : 0;
                else if (on) EngineFixedBroken = true;
                if (ok) CeImagen.MarcaRefrescoFijo(on); // (ConsoleEnhancer: en data\picture_restore.txt: si la app se cae, se quita al volver a abrirla)
                Log.Write("NVIDIA: refresco fijo (G-SYNC apagado) para ConsoleEnhancerEngine.exe " + (on ? "puesto (60 con DLSS FG)" : "quitado (VRR)") +
                          (ok ? "" : " NO SE PUDO") + " en " + sw.ElapsedMilliseconds + " ms");
                return ok;
            }
        }
        // op -1 = mirar, 1 = poner, 0 = quitar
        static bool FixedRw(int op)
        {
            if (Ce.NoNvidia) return false; // (ConsoleEnhancer: sin NVIDIA no hay NVAPI)
            List<IntPtr> mem = new List<IntPtr>();
            IntPtr session = IntPtr.Zero;
            try
            {
                Check(Fn<FnVoid>(IdInitialize)(), "Initialize");
                Check(Fn<FnOutHandle>(IdCreateSession)(out session), "CreateSession");
                Check(Fn<FnHandle>(IdLoadSettings)(session), "LoadSettings");
                IntPtr profile;
                int rc = Fn<FnFindApp>(IdFindApplicationByName)(session, Str(mem, "ConsoleEnhancerEngine.exe"), out profile, Alloc(mem, AppSize));
                if (rc == NvExecutableNotFound)
                {
                    if (op < 1) return false;
                    string name = "ConsoleEnhancer - ConsoleEnhancerEngine.exe"; // (ConsoleEnhancer: el nombre de los perfiles de la app)
                    rc = Fn<FnFindProfile>(IdFindProfileByName)(session, Str(mem, name), out profile);
                    if (rc == NvProfileNotFound)
                    {
                        IntPtr info = Alloc(mem, ProfileSize);
                        WriteString(info, 4, name);
                        Marshal.WriteInt32(info, ProfileGpuSupportOffset, 1);
                        Check(Fn<FnCreateProfile>(IdCreateProfile)(session, info, out profile), "CreateProfile " + name);
                    }
                    else Check(rc, "FindProfileByName " + name);
                    IntPtr app = Alloc(mem, AppSize);
                    WriteString(app, AppNameOffset, "ConsoleEnhancerEngine.exe");
                    Check(Fn<FnCreateApp>(IdCreateApplication)(session, profile, app), "CreateApplication ConsoleEnhancerEngine.exe");
                }
                else Check(rc, "FindApplicationByName ConsoleEnhancerEngine.exe");
                if (op < 0)
                {
                    IntPtr s = Alloc(mem, SettingSize);
                    return Fn<FnGetSetting>(IdGetSetting)(session, profile, VrrAppOverride, s) == NvOk && Marshal.ReadInt32(s, SettingLocationOffset) == 0 &&
                           (uint)Marshal.ReadInt32(s, SettingCurrentOffset) == VrrFixedRefresh;
                }
                foreach (uint id in new[] { VrrAppOverride, VrrAppOverrideRequest })
                {
                    if (op >= 1)
                    {
                        IntPtr w = Alloc(mem, SettingSize);
                        Marshal.WriteInt32(w, SettingIdOffset, unchecked((int)id));
                        Marshal.WriteInt32(w, SettingCurrentOffset, (int)VrrFixedRefresh);
                        Check(Fn<FnSetSetting>(IdSetSetting)(session, profile, w), "SetSetting refresco fijo");
                    }
                    else
                    {
                        rc = Fn<FnDelSetting>(IdDeleteProfileSetting)(session, profile, id);
                        if (rc != NvSettingNotFound && rc != NvOk) Check(rc, "DeleteProfileSetting refresco fijo");
                    }
                }
                Check(Fn<FnHandle>(IdSaveSettings)(session), "SaveSettings");
                return true;
            }
            catch (Exception ex) { Log.Error("NVIDIA: refresco fijo del motor", ex); return false; }
            finally
            {
                if (session != IntPtr.Zero) { try { Fn<FnHandle>(IdDestroySession)(session); } catch { } }
                foreach (IntPtr p in mem) Marshal.FreeHGlobal(p);
            }
        }

        // op -1 = mirar (true si esta puesto), -2 = mirar si la sincronizacion es la de cada 2 refrescos, 1 = poner (VSync forzada),
        // 2 = poner (VSync cada 2 refrescos), 3 = solo VSync forzada sin Smooth Motion, -3 = mirar si esta asi, 4 = solo VSync cada 2
        // refrescos sin Smooth Motion (se mira con -2), 0 = quitar (true = hecho)
        static bool SmoothRw(int op, string exe = "Magpie.exe")   // (17-11 16:55: tambien ConsoleEnhancerEngine.exe)
        {
            if (Ce.NoNvidia) return false; // (ConsoleEnhancer: sin NVIDIA no hay NVAPI)
            List<IntPtr> mem = new List<IntPtr>();
            IntPtr session = IntPtr.Zero;
            try
            {
                Check(Fn<FnVoid>(IdInitialize)(), "Initialize");
                Check(Fn<FnOutHandle>(IdCreateSession)(out session), "CreateSession");
                Check(Fn<FnHandle>(IdLoadSettings)(session), "LoadSettings");
                IntPtr profile;
                int rc = Fn<FnFindApp>(IdFindApplicationByName)(session, Str(mem, exe), out profile, Alloc(mem, AppSize));
                if (rc == NvExecutableNotFound)
                {
                    if (op < 1) return false; // (sin perfil: ni puesto ni nada que quitar)
                    string name = "ConsoleEnhancer - " + exe;
                    rc = Fn<FnFindProfile>(IdFindProfileByName)(session, Str(mem, name), out profile);
                    if (rc == NvProfileNotFound)
                    {
                        IntPtr info = Alloc(mem, ProfileSize);
                        WriteString(info, 4, name);
                        Marshal.WriteInt32(info, ProfileGpuSupportOffset, 1); // GeForce
                        Check(Fn<FnCreateProfile>(IdCreateProfile)(session, info, out profile), "CreateProfile " + name);
                    }
                    else Check(rc, "FindProfileByName " + name);
                    IntPtr app = Alloc(mem, AppSize);
                    WriteString(app, AppNameOffset, exe);
                    Check(Fn<FnCreateApp>(IdCreateApplication)(session, profile, app), "CreateApplication " + exe);
                }
                else Check(rc, "FindApplicationByName " + exe);
                if (op == -3)
                {
                    IntPtr s = Alloc(mem, SettingSize);
                    return Fn<FnGetSetting>(IdGetSetting)(session, profile, VerticalSync, s) == NvOk && Marshal.ReadInt32(s, SettingLocationOffset) == 0 &&
                           (uint)Marshal.ReadInt32(s, SettingCurrentOffset) == VerticalSyncForceOn;
                }
                if (op == -2)
                {
                    IntPtr s = Alloc(mem, SettingSize);
                    return Fn<FnGetSetting>(IdGetSetting)(session, profile, VerticalSync, s) == NvOk && Marshal.ReadInt32(s, SettingLocationOffset) == 0 &&
                           (uint)Marshal.ReadInt32(s, SettingCurrentOffset) == VerticalSyncHalf;
                }
                if (op < 0)
                {
                    IntPtr s = Alloc(mem, SettingSize);
                    rc = Fn<FnGetSettingEx>(IdGetSettingEx)(session, profile, SmoothMotionEnable, s, Alloc(mem, 64));
                    return rc == NvOk && Marshal.ReadInt32(s, SettingLocationOffset) == 0 && Marshal.ReadInt32(s, SettingCurrentOffset) != 0;
                }
                if (op >= 1)
                {
                    if (op == 3 || op == 4)
                    {
                        // (solo la sincronizacion: sin Smooth Motion)
                        rc = Fn<FnDelSetting>(IdDeleteProfileSettingEx)(session, profile, SmoothMotionEnable);
                        if (rc != NvSettingNotFound) Check(rc, "DeleteProfileSettingEx Smooth Motion");
                    }
                    else
                    {
                        IntPtr w = Alloc(mem, SettingSize);
                        Marshal.WriteInt32(w, SettingIdOffset, unchecked((int)SmoothMotionEnable));
                        Marshal.WriteInt32(w, SettingCurrentOffset, 1);
                        Check(Fn<FnSetSettingEx>(IdSetSettingEx)(session, profile, w, 0, 0), "SetSettingEx Smooth Motion");
                    }
                    if (op == 5)
                    {
                        // (1.2.4 ronda 6, SMX4: Smooth Motion sin sincronizacion del perfil -la de la aplicacion, con VRR-; si la que hay en el
                        // perfil es una de las nuestras, fuera. Medido: con la forzada, XeSS FG + Smooth Motion deja la mitad sin verse)
                        IntPtr s5 = Alloc(mem, SettingSize);
                        if (Fn<FnGetSetting>(IdGetSetting)(session, profile, VerticalSync, s5) == NvOk && Marshal.ReadInt32(s5, SettingLocationOffset) == 0 &&
                            ((uint)Marshal.ReadInt32(s5, SettingCurrentOffset) == VerticalSyncForceOn || (uint)Marshal.ReadInt32(s5, SettingCurrentOffset) == VerticalSyncHalf))
                        {
                            rc = Fn<FnDelSetting>(IdDeleteProfileSetting)(session, profile, VerticalSync);
                            if (rc != NvSettingNotFound) Check(rc, "DeleteProfileSetting sincronizacion vertical");
                        }
                    }
                    else
                    {
                    IntPtr v = Alloc(mem, SettingSize);
                    Marshal.WriteInt32(v, SettingIdOffset, (int)VerticalSync);
                    Marshal.WriteInt32(v, SettingCurrentOffset, unchecked((int)(op == 2 || op == 4 ? VerticalSyncHalf : VerticalSyncForceOn)));
                    Check(Fn<FnSetSetting>(IdSetSetting)(session, profile, v), "SetSetting sincronizacion vertical");
                    }
                }
                else
                {
                    rc = Fn<FnDelSetting>(IdDeleteProfileSettingEx)(session, profile, SmoothMotionEnable);
                    if (rc != NvSettingNotFound) Check(rc, "DeleteProfileSettingEx Smooth Motion");
                    // La sincronizacion vertical solo si es la que puso esto (forzada o cada 2 refrescos, en el propio perfil)
                    IntPtr s = Alloc(mem, SettingSize);
                    if (Fn<FnGetSetting>(IdGetSetting)(session, profile, VerticalSync, s) == NvOk && Marshal.ReadInt32(s, SettingLocationOffset) == 0 &&
                        ((uint)Marshal.ReadInt32(s, SettingCurrentOffset) == VerticalSyncForceOn || (uint)Marshal.ReadInt32(s, SettingCurrentOffset) == VerticalSyncHalf))
                    {
                        rc = Fn<FnDelSetting>(IdDeleteProfileSetting)(session, profile, VerticalSync);
                        if (rc != NvSettingNotFound) Check(rc, "DeleteProfileSetting sincronizacion vertical");
                    }
                }
                Check(Fn<FnHandle>(IdSaveSettings)(session), "SaveSettings");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("NVIDIA: Smooth Motion de " + exe, ex);
                return false;
            }
            finally
            {
                if (session != IntPtr.Zero) { try { Fn<FnHandle>(IdDestroySession)(session); } catch { } }
                foreach (IntPtr p in mem) Marshal.FreeHGlobal(p);
            }
        }

        // Bloque a cero con la version de NVAPI (tamaño | 1 << 16) al principio
        static IntPtr Alloc(List<IntPtr> mem, int size)
        {
            IntPtr p = Marshal.AllocHGlobal(size);
            mem.Add(p);
            Marshal.Copy(new byte[size], 0, p, size);
            Marshal.WriteInt32(p, 0, size | (1 << 16));
            return p;
        }

        static IntPtr Str(List<IntPtr> mem, string s)
        {
            IntPtr p = Marshal.AllocHGlobal(StringBytes);
            mem.Add(p);
            Marshal.Copy(new byte[StringBytes], 0, p, StringBytes);
            WriteString(p, 0, s);
            return p;
        }

        static void WriteString(IntPtr block, int offset, string s)
        {
            byte[] b = Encoding.Unicode.GetBytes(s);
            Marshal.Copy(b, 0, new IntPtr(block.ToInt64() + offset), b.Length);
        }

        // Aplica los perfiles; si el driver pide administrador, relanza este exe elevado
        // solo para eso (un aviso de UAC, una vez: despues ya estan bien).
        // Devuelve true si algo cambio (los procesos abiertos deben reiniciarse).
        public static bool Ensure()
        {
            bool saveDenied;
            int r = Apply(out saveDenied);
            if (r == Changed) return true;
            if (!saveDenied) return false;
            try
            {
                Log.Write("NVIDIA: se pide permiso de administrador para guardar los perfiles");
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--nvidia-profiles");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(30000)) return false;
                    return p.ExitCode == Changed;
                }
            }
            catch (Exception ex)
            {
                Log.Error("NVIDIA: no se pudo elevar (UAC cancelado?)", ex);
                return false;
            }
        }
    }

    // ===================================================================
    //  Medidor de audio de Windows (Core Audio): pico de las sesiones de
    //  audio de un proceso (Streaming Center reproduce el sonido de la consola)
    // ===================================================================

    static class AudioMeter
    {
        // 21-11 (1.2.1): "ERROR: Medidor de audio -> InvalidCastException: No se puede convertir un objeto de tipo 'MMDeviceEnumeratorCom' al
        // tipo 'MMDeviceEnumeratorCom'" al abrir la app (19:09:22.486): habia DOS clases [ComImport] con el CLSID del enumerador (esta y la de
        // Escuchar, SonidoDoble.cs). El enumerador de Windows devuelve el mismo objeto COM y .NET reutiliza su envoltorio (RCW), que es de la
        // OTRA clase: el "new" de la segunda fallaba al convertirlo (y el medidor de audio no iba en toda la sesion). Ahora las dos lo crean
        // sin clase propia (un __ComObject) y solo se convierte a la interfaz (QueryInterface), que vale con cualquier envoltorio
        static readonly Guid ClsidEnumerador = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        internal static object NuevoEnumerador() { return Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidEnumerador, true)); }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            // (resto de metodos no usados)
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                                       [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr a, int b, out IntPtr c);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr a, int b, out IntPtr c);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            // IAudioSessionControl
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr a, IntPtr b);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr a, IntPtr b);
            [PreserveSig] int GetGroupingParam(out Guid g);
            [PreserveSig] int SetGroupingParam(IntPtr a, IntPtr b);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr a);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr a);
            // IAudioSessionControl2
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint pid);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioMeterInformation
        {
            [PreserveSig] int GetPeakValue(out float peak);
        }

        const int eRender = 0;
        const int DEVICE_STATE_ACTIVE = 1;
        const int CLSCTX_ALL = 23;

        // Pico maximo (0..1) de todas las sesiones de audio de los procesos indicados,
        // en todas las salidas activas. -1 si no hay ninguna sesion de esos procesos.
        // 17-11 (VELOCIDAD): lo llama MpvLinkLoop cada 0,1 s y buscar las salidas y las sesiones son 5,5 ms (4,9 de CPU; medido con
        // scratchpad\velocidad\bench\ameter.exe): ~5 % de un nucleo todo el rato. Ahora la busqueda entera (como antes) cada 0,5 s o si
        // cambian los procesos o falla un medidor; entre medias solo el pico de los medidores ya encontrados (microsegundos)
        static readonly object meterSync = new object();
        static readonly List<IAudioMeterInformation> meters = new List<IAudioMeterInformation>();
        static string metersKey;
        static int metersTick;
        static bool metersOk;

        public static float PeakOfProcesses(HashSet<uint> pids)
        {
            lock (meterSync)
            {
                List<uint> ids = new List<uint>(pids);
                ids.Sort();
                StringBuilder kb = new StringBuilder();
                foreach (uint id in ids) kb.Append(id).Append(',');
                string key = kb.ToString();
                int age = unchecked(Environment.TickCount - metersTick);
                if (metersOk && key == metersKey && age >= 0 && age < 500)
                {
                    float got = -1f;
                    bool fine = true;
                    foreach (IAudioMeterInformation m in meters)
                    {
                        float peak;
                        if (m.GetPeakValue(out peak) != 0) { fine = false; break; }
                        if (peak > got) got = peak;
                    }
                    if (fine) return got;
                }
                metersOk = false;
                meters.Clear();
                float best = ScanPeak(pids, meters);
                metersKey = key;
                metersTick = Environment.TickCount;
                metersOk = true;
                return best;
            }
        }

        // La busqueda de siempre (todas las salidas activas y sus sesiones); found: los medidores de esos procesos
        static float ScanPeak(HashSet<uint> pids, List<IAudioMeterInformation> found)
        {
            float best = -1f;
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)NuevoEnumerador();
            IMMDeviceCollection devs;
            if (en.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out devs) != 0) return best;
            int nd;
            devs.GetCount(out nd);
            Guid iidMgr = typeof(IAudioSessionManager2).GUID;
            for (int d = 0; d < nd; d++)
            {
                IMMDevice dev;
                if (devs.Item(d, out dev) != 0) continue;
                object o;
                if (dev.Activate(ref iidMgr, CLSCTX_ALL, IntPtr.Zero, out o) != 0) continue;
                IAudioSessionEnumerator se;
                if (((IAudioSessionManager2)o).GetSessionEnumerator(out se) != 0) continue;
                int ns;
                se.GetCount(out ns);
                for (int i = 0; i < ns; i++)
                {
                    IAudioSessionControl2 s;
                    if (se.GetSession(i, out s) != 0) continue;
                    uint pid;
                    if (s.GetProcessId(out pid) != 0 || !pids.Contains(pid)) continue;
                    IAudioMeterInformation m = s as IAudioMeterInformation;
                    float peak;
                    if (m != null && m.GetPeakValue(out peak) == 0)
                    {
                        found.Add(m);
                        if (peak > best) best = peak;
                    }
                }
            }
            return best;
        }
    }

    // ===================================================================
    //  Que juego esta seleccionado en el HOME, para recordar los ajustes de cada juego. El nombre del
    //  juego seleccionado va en azul encima de su icono (en el HOME y en la biblioteca). De la version
    //  anterior (11-01), que lo usaba para otra cosa; las capturas ya no son PrintWindow (daba tirones):
    //  salen de la imagen en memoria de mpv (script Lua) y solo con la imagen a 60 fps
    // ===================================================================
    static class HomeLabel
    {
        static int Px(byte[] px, int stride, int x, int y, int ch) { return px[y * stride + x * 4 + ch]; }

        static byte[] Pixels(Bitmap bmp, out int stride)
        {
            System.Drawing.Imaging.BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                byte[] px = new byte[d.Stride * bmp.Height];
                Marshal.Copy(d.Scan0, px, 0, px.Length);
                stride = d.Stride;
                return px;
            }
            finally { bmp.UnlockBits(d); }
        }

        const int MW = 48, MH = 6; // forma del nombre: 48x6 celdas con o sin letra

        // Cian del nombre. Reducida a 640x360 la letra se mezcla con el fondo oscuro (medido en capturas del HOME:
        // 30,180,220 y 30,160,200 mas que 0,200,240): poco rojo, azul alto y por encima del verde
        static bool LabelColor(byte[] px, int stride, int x, int y)
        {
            int b = Px(px, stride, x, y, 0), g = Px(px, stride, x, y, 1), r = Px(px, stride, x, y, 2);
            return r < 90 && g >= 120 && g <= 245 && b >= 150 && b >= g && b - r >= 100;
        }

        // Forma del nombre del juego seleccionado (null si no hay) y donde esta (box, para el OCR). hint: se ha
        // saltado un texto azul que no es un nombre ("Cambiar de usuario", bajo el juego en uso). Pensado para
        // una imagen de 640x360
        public static byte[] Selected(Bitmap bmp, out Rectangle box, out bool hint)
        {
            LabelText lt;
            return Selected(bmp, out box, out hint, out lt);
        }

        // 15-11: el texto del nombre (en la imagen de 640x360): sus limites sin margen, donde acaba el icono de la izquierda (la
        // tarjeta de juego: el OCR la leia como una letra, "B EA SPORTS FC 26") y si llega al ancho maximo de la etiqueta (los
        // nombres largos se cortan ahi en seco, sin "...", y se desplazan: medido en una captura del HOME, "The Legend of Zelda:
        // Tears of the Kingdc" ocupa 229 de 640 pixeles con el icono; "EA SPORTS FC 26", 107)
        public class LabelText
        {
            public int W, H;              // la imagen en la que se midio (640x360)
            public int X0, X1, Y0, Y1;    // el texto, icono incluido
            public int TextX0;            // donde empiezan las letras (tras el icono; X0 si no se ha visto)
            public int IconEnd = -1;      // la ultima columna del icono (-1: no se ha visto)
            public bool Clipped;          // llega al ancho maximo: cortado por la etiqueta (marquesina)
            public bool Icon { get { return IconEnd >= 0; } }
            public int Height { get { return Y1 - Y0 + 1; } }
            public int Width { get { return X1 - X0 + 1; } }
        }
        public const double ClipWidth = 0.335; // ancho a partir del cual la etiqueta esta cortada (229 / 640 = 0,358, con margen)

        static bool ColHas(bool[] text, int W, int x, int y0, int y1)
        {
            for (int y = y0; y <= y1; y++) if (text[y * W + x]) return true;
            return false;
        }

        static LabelText Measure(bool[] text, int W, int H, int x0, int x1, int y0, int y1)
        {
            LabelText t = new LabelText();
            // (los limites con paso de 1 pixel: Shape los busca de 2 en 2)
            while (x0 > 0 && ColHas(text, W, x0 - 1, y0, y1)) x0--;
            while (x1 < W - 1 && ColHas(text, W, x1 + 1, y0, y1)) x1++;
            t.W = W; t.H = H; t.X0 = x0; t.X1 = x1; t.Y0 = y0; t.Y1 = y1; t.TextX0 = x0;
            // El icono: un bloque de 0,5-1,5 veces la altura desde x0 (con huecos de 1 columna: el de linea discontinua), luego un
            // hueco de 2 columnas o mas y las letras
            int h = y1 - y0 + 1, minGap = Math.Max(2, h / 5), lastInk = x0, run = 0, textStart = -1;
            for (int x = x0; x <= Math.Min(x1, x0 + 2 * h); x++)
            {
                if (ColHas(text, W, x, y0, y1))
                {
                    if (run >= minGap) { textStart = x; break; }
                    lastInk = x;
                    run = 0;
                }
                else run++;
            }
            if (textStart > 0)
            {
                int iconW = lastInk - x0 + 1, gap = textStart - lastInk - 1;
                if (iconW * 2 >= h && iconW * 2 <= h * 3 && gap <= h) { t.IconEnd = lastInk; t.TextX0 = textStart; }
            }
            t.Clipped = t.Width >= ClipWidth * W;
            return t;
        }

        // El recuadro del nombre para el OCR en la imagen original (w x h): sin el icono si se ha visto, con un margen pequeño
        // (debajo esta el borde iluminado de la casilla: "EA SPORTS FC 26 a")
        public static Rectangle OcrRect(LabelText t, int w, int h)
        {
            double th = t.Height;
            double left = t.Icon ? t.TextX0 - Math.Max(1.0, (t.TextX0 - t.IconEnd - 1) / 2.0) : t.X0 - th * 0.6;
            double right = t.X1 + 1 + th * 0.5, top = t.Y0 - th * 0.5, bottom = t.Y1 + 1 + th * 0.4;
            double sx = (double)w / t.W, sy = (double)h / t.H;
            Rectangle r = Rectangle.FromLTRB((int)Math.Floor(left * sx), (int)Math.Floor(top * sy), (int)Math.Ceiling(right * sx), (int)Math.Ceiling(bottom * sy));
            return Rectangle.Intersect(r, new Rectangle(0, 0, w, h));
        }

        // 15-11: texto de la imagen entera (elegir usuario, avisos de la consola) a 1280x720, como la captura ligera con la que se
        // ajustaron las palabras: con una captura entera de 4K (pedida para leer el nombre) el OCR tardaria mas y leeria otra cosa
        public static string ScreenText(Bitmap full)
        {
            if (full.Width <= 1400) return Ocr.Read(full);
            using (Bitmap s = new Bitmap(1280, 720, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                using (Graphics g = Graphics.FromImage(s))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(full, 0, 0, 1280, 720);
                }
                return Ocr.Read(s);
            }
        }

        public static byte[] Selected(Bitmap bmp, out Rectangle box, out bool hint, out LabelText label)
        {
            int hintY;
            return Selected(bmp, out box, out hint, out label, out hintY);
        }

        // 16-11: hintY = la altura (a 640x360) del primer texto conocido saltado (-1 ninguno): arriba (< 150) es la etiqueta de una casilla
        // de la fila de juegos que no es un juego ("Ver más", la biblioteca); "Cambiar de usuario" va debajo de las casillas (y ~238)
        public static byte[] Selected(Bitmap bmp, out Rectangle box, out bool hint, out LabelText label, out int hintY)
        {
            box = Rectangle.Empty;
            hint = false;
            hintY = -1;
            label = null;
            int W = bmp.Width, H = bmp.Height, stride;
            byte[] px = Pixels(bmp, out stride);
            // Pixeles de letra: del color del nombre y en tramos cortos (las lineas largas de ese color, como el
            // borde animado del icono, no son texto)
            bool[] text = new bool[W * H];
            int maxRun = Math.Max(8, W / 50);
            for (int y = 0; y < H; y++)
            {
                int run = 0;
                for (int x = 0; x <= W; x++)
                {
                    if (x < W && LabelColor(px, stride, x, y)) { run++; continue; }
                    if (run > 0 && run <= maxRun)
                        for (int k = x - run; k < x; k++) text[y * W + k] = true;
                    run = 0;
                }
            }
            int[] rows = new int[H];
            for (int y = 0; y < H; y += 2)
                for (int x = 0; x < W; x += 2)
                    if (text[y * W + x]) rows[y]++;
            // La franja de la altura de una linea con mas pixeles de ese color; con el juego en uso seleccionado
            // hay dos (su nombre y "Cambiar de usuario" debajo): la de un texto conocido se salta
            int band = Math.Max(4, (int)(0.035 * H));
            List<int> tried = new List<int>();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int bestY = -1, bestN = 0;
                for (int y = 0; y + band < H; y += 2)
                {
                    bool near = false;
                    foreach (int t in tried) if (Math.Abs(y - t) < band) { near = true; break; }
                    if (near) continue;
                    int n = 0;
                    for (int k = y; k < y + band; k += 2) n += rows[k];
                    if (n > bestN) { bestN = n; bestY = y; }
                }
                if (bestN < 60) return null;
                tried.Add(bestY);
                Rectangle b;
                LabelText lt;
                byte[] m = Shape(text, W, H, bestY, band, out b, out lt);
                if (m == null) continue;
                if (HomeHints.IsHint(m)) { if (!hint) hintY = b.Y + b.Height / 2; hint = true; continue; }
                box = b;
                label = lt;
                return m;
            }
            return null;
        }

        static byte[] Shape(bool[] text, int W, int H, int bestY, int band, out Rectangle box, out LabelText label)
        {
            box = Rectangle.Empty;
            label = null;
            int x0 = W, x1 = -1, y0 = H, y1 = -1;
            for (int y = bestY; y < bestY + band; y++)
                for (int x = 0; x < W; x += 2)
                    if (text[y * W + x])
                    {
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            // Mas de 24 veces mas ancho que alto no es una linea de texto (al pulsar A el HOME se amplia y sale una raya)
            if (w < W * 0.03 || h < 4 || w > W * 0.6 || w > h * 24) return null;
            // Con margen (para el OCR, OcrRect con label: sin el icono azul de la izquierda, que lee como una letra)
            box = Rectangle.FromLTRB(Math.Max(0, x0 - h), Math.Max(0, y0 - h / 2), Math.Min(W, x1 + h), Math.Min(H, y1 + h / 2));
            label = Measure(text, W, H, x0, x1, y0, y1);
            byte[] m = new byte[1 + MW * MH];
            m[0] = (byte)Math.Min(255, w * 10 / h);
            for (int cy = 0; cy < MH; cy++)
                for (int cx = 0; cx < MW; cx++)
                {
                    int ax = x0 + w * cx / MW, bx = Math.Max(ax + 1, x0 + w * (cx + 1) / MW);
                    int ay = y0 + h * cy / MH, by = Math.Max(ay + 1, y0 + h * (cy + 1) / MH);
                    int on = 0, all = 0;
                    for (int y = ay; y < by; y++)
                        for (int x = ax; x < bx; x++) { all++; if (text[y * W + x]) on++; }
                    m[1 + cy * MW + cx] = (byte)(on * 4 >= all ? 1 : 0);
                }
            return m;
        }

        public static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length || a.Length < 2) return false;
            if (Math.Abs(a[0] - b[0]) > Math.Max(3, a[0] / 8)) return false;
            int same = 0;
            for (int i = 1; i < a.Length; i++) if (a[i] == b[i]) same++;
            return same >= (a.Length - 1) * 85 / 100;
        }

        // Nombre leido por OCR, sin el icono azul (que lee como una letra suelta) ni simbolos (15-11: GameNames.Clean)
        public static string CleanName(string ocr) { return GameNames.Clean(ocr, false); }

        // Para comparar nombres: minusculas, solo letras y cifras
        public static string Norm(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        // Imagen cruda de mpv (bgr0/bgra o rgb0/rgba) -> 640x360 para buscar el nombre, y la original para el OCR
        public static Bitmap FromRaw(byte[] data, int w, int h, int stride, string format)
        {
            Bitmap full = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            System.Drawing.Imaging.BitmapData d = full.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                bool rgb = format != null && format.StartsWith("rgb", StringComparison.Ordinal);
                byte[] row = new byte[w * 4];
                for (int y = 0; y < h; y++)
                {
                    Buffer.BlockCopy(data, y * stride, row, 0, w * 4);
                    if (rgb) for (int i = 0; i < row.Length; i += 4) { byte t = row[i]; row[i] = row[i + 2]; row[i + 2] = t; }
                    Marshal.Copy(row, 0, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row.Length);
                }
            }
            finally { full.UnlockBits(d); }
            return full;
        }

        public static Bitmap Small(Bitmap full)
        {
            Bitmap s = new Bitmap(640, 360, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(s))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(full, 0, 0, 640, 360);
            }
            return s;
        }

        // Franjas fijas del HOME (en la imagen de 640x360): la de arriba (usuarios, hora, bateria; y 0-54) y la barra de
        // iconos de abajo (y 255-305), 32 columnas de bloques en grises. No cambian al mover el cursor por los juegos
        public static double[] Strips(Bitmap small)
        {
            int stride;
            byte[] px = Pixels(small, out stride);
            double[] s = new double[32 * 6 + 32 * 5];
            int k = 0;
            foreach (int[] band in new int[][] { new int[] { 0, 54, 6 }, new int[] { 255, 305, 5 } })
                for (int ry = 0; ry < band[2]; ry++)
                    for (int cx = 0; cx < 32; cx++)
                    {
                        int ay = band[0] + (band[1] - band[0]) * ry / band[2], ey = band[0] + (band[1] - band[0]) * (ry + 1) / band[2];
                        double v = 0; int n = 0;
                        for (int y = ay; y < ey; y += 2)
                            for (int x = cx * 20; x < cx * 20 + 20; x += 2) { int o = y * stride + x * 4; v += (px[o] + px[o + 1] + px[o + 2]) / 3.0; n++; }
                        s[k++] = v / n;
                    }
            return s;
        }

        // ¿Es el HOME? Diferencia con las franjas del HOME solo donde estas tienen dibujo (lejos de su gris mediano): con el
        // tema oscuro una pantalla negra se parecia a la franja de arriba (media 12 con todos los puntos) y se tomaba por el
        // HOME (03-11: entrar en un juego no se veia). Medido con capturas: HOME 0,3, pantalla negra 58, gris oscuro 35,
        // juegos 20-42. <= 12 = el HOME
        public static double HomeDiff(double[] cur, double[] rf)
        {
            if (cur == null || rf == null || cur.Length != rf.Length) return 999;
            double[] sorted = (double[])rf.Clone();
            Array.Sort(sorted);
            double med = sorted[sorted.Length / 2], s = 0, all = 0;
            int n = 0;
            for (int i = 0; i < rf.Length; i++)
            {
                double d = Math.Abs(cur[i] - rf[i]);
                all += d;
                if (Math.Abs(rf[i] - med) > 20) { s += d; n++; }
            }
            return n >= 10 ? s / n : all / rf.Length;
        }

        // Icono del juego seleccionado: el nombre azul va centrado sobre el y los iconos estan siempre a la misma altura
        // (y 97-228). Solo su mitad de arriba (y 104-168): abajo sale "En uso" y el usuario en el que esta abierto. 10x6
        // bloques en color. Medido con capturas del HOME: el mismo juego 0,3-12,8 (seleccionado o no, "En uso"), juegos
        // distintos 66-95
        public static double[] Icon(Bitmap small, int cx)
        {
            int stride;
            byte[] px = Pixels(small, out stride);
            int x0 = cx - 54, x1 = cx + 54, y0 = 104, y1 = 168;
            double[] s = new double[10 * 6 * 3];
            for (int by = 0; by < 6; by++)
                for (int bx = 0; bx < 10; bx++)
                {
                    int ax = x0 + (x1 - x0) * bx / 10, ex = x0 + (x1 - x0) * (bx + 1) / 10;
                    int ay = y0 + (y1 - y0) * by / 6, ey = y0 + (y1 - y0) * (by + 1) / 6;
                    double r = 0, g = 0, b = 0; int n = 0;
                    for (int y = ay; y < ey; y++)
                        for (int x = ax; x < ex; x++)
                        {
                            int o = y * stride + Math.Max(0, Math.Min(639, x)) * 4;
                            b += px[o]; g += px[o + 1]; r += px[o + 2]; n++;
                        }
                    int k = (by * 10 + bx) * 3;
                    s[k] = r / n; s[k + 1] = g / n; s[k + 2] = b / n;
                }
            return s;
        }

        // 09-11: ¿el juego seleccionado esta en uso (abierto o suspendido)? Debajo de su casilla sale "(Y) Cambiar de
        // usuario" en el mismo azul que el nombre (y ~236-250 en 640x360, centrado con la casilla); con otro juego
        // seleccionado ahi no hay nada. Devuelve los pixeles de letra azul en esa franja (box = el nombre, de Selected)
        public static int InUsePixels(Bitmap small, Rectangle box)
        {
            int stride;
            byte[] px = Pixels(small, out stride);
            int W = small.Width, H = small.Height;
            int cx = box.X + box.Width / 2;
            int x0 = Math.Max(0, cx - 80), x1 = Math.Min(W, cx + 80);
            int maxRun = Math.Max(8, W / 50), n = 0;
            for (int y = 232; y < 258 && y < H; y++)
            {
                int run = 0;
                for (int x = x0; x <= x1; x++)
                {
                    if (x < x1 && LabelColor(px, stride, x, y)) { run++; continue; }
                    if (run > 0 && run <= maxRun) n += run;
                    run = 0;
                }
            }
            return n;
        }
        public const int InUseMin = 40;      // pixeles de "Cambiar de usuario" por encima de los cuales esta en uso

        // 16-11: la biblioteca ("Programas", la casilla "Ver más" del HOME) no es el HOME: las casillas van en rejilla, el nombre del juego
        // seleccionado sale en una pastilla gris encima o debajo de su casilla y ni su icono (y 104-168) ni "Cambiar de usuario" (y 232-258)
        // estan donde los busca el HOME (16-11 00:40 y 00:45: el arte de Fall Guys y Hollow Knight se tomaba por el nombre, "LLó", y por
        // "en uso"; dos entradas falsas a los ~300 ms). Se reconoce por la raya cian bajo la pestaña elegida (Programas / Grupos): una fila
        // de y 24-36 (a 640x360) con un tramo seguido de ese color de 60-260 px (medido en las 20 capturas del 16-11, ligeras y enteras:
        // 111 px en y 30; en las del HOME, 2 como mucho)
        public static bool IsLibrary(Bitmap small)
        {
            int stride;
            byte[] px = Pixels(small, out stride);
            int W = small.Width;
            bool tab = false;
            for (int y = 24; y <= 36 && y < small.Height && !tab; y++)
            {
                int run = 0;
                for (int x = 0; x <= W && !tab; x++)
                {
                    if (x < W && LabelColor(px, stride, x, y)) { run++; continue; }
                    if (run >= 60 && run <= 260) tab = true;
                    run = 0;
                }
            }
            if (!tab) return false;
            // Y debajo de las pestañas la raya gris de lado a lado (y 37: 232-592 de 640 pixeles grises en las 20 capturas del 16-11, las
            // casillas la tapan en parte al bajar; en las del HOME, 60 como mucho en y 33-41): que una imagen de un juego con una raya cian
            // arriba no lo parezca
            for (int y = 33; y <= 41 && y < small.Height; y++)
            {
                int gray = 0;
                for (int x = 0; x < W; x++)
                {
                    int o = y * stride + x * 4, b = px[o], g = px[o + 1], r = px[o + 2];
                    double l = (54 * r + 183 * g + 19 * b) / 256.0;
                    if (l >= 45 && l <= 120 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 20) gray++;
                }
                if (gray >= W * 25 / 100) return true;
            }
            return false;
        }

        // Gris de la pastilla del nombre en la biblioteca: 56,56,56 en las capturas del 16-11 (ligeras y enteras); el fondo es 28,28,28
        static bool PillGray(byte[] px, int stride, int x, int y)
        {
            int o = y * stride + x * 4;
            return Math.Abs(px[o] - 56) <= 8 && Math.Abs(px[o + 1] - 56) <= 8 && Math.Abs(px[o + 2] - 56) <= 8;
        }

        // 16-11: en la biblioteca, el nombre del juego seleccionado: la pastilla gris lisa (26-30 filas de alto a 640x360, filas grises de 40 px
        // o mas encima y debajo del texto) con el nombre en cian en medio (y a su izquierda el icono de la tarjeta de juego, como en el HOME).
        // Su forma y el texto para el OCR como Selected, o null si no se ve (el cursor en las pestañas, o moviendose: la pastilla sale despues)
        public static byte[] LibraryLabel(Bitmap bmp, out Rectangle box, out LabelText label)
        {
            box = Rectangle.Empty;
            label = null;
            int W = bmp.Width, H = bmp.Height, stride;
            byte[] px = Pixels(bmp, out stride);
            // El tramo gris mas largo de cada fila (40 px o mas)
            int[] rx0 = new int[H], rx1 = new int[H];
            for (int y = 0; y < H; y++)
            {
                rx0[y] = rx1[y] = -1;
                int run = 0, best = 0;
                for (int x = 0; x <= W; x++)
                {
                    if (x < W && PillGray(px, stride, x, y)) { run++; continue; }
                    if (run >= 40 && run > best) { best = run; rx0[y] = x - run; rx1[y] = x - 1; }
                    run = 0;
                }
            }
            int maxRun = Math.Max(8, W / 50);
            for (int top = 40; top < H - 18; top++)
            {
                // El borde de arriba de una pastilla: fila gris larga sin otra encima que se solape
                if (rx0[top] < 0 || (rx0[top - 1] >= 0 && rx0[top - 1] <= rx1[top] && rx1[top - 1] >= rx0[top])) continue;
                int x0 = rx0[top], x1 = rx1[top], bottom = top, rowsOk = 1;
                for (int y = top + 1; y < Math.Min(H, top + 36); y++)
                {
                    if (rx0[y] < 0) continue;
                    int ov = Math.Min(x1, rx1[y]) - Math.Max(x0, rx0[y]) + 1;
                    if (ov < 0.6 * (x1 - x0 + 1)) continue;
                    bottom = y;
                    rowsOk++;
                    x0 = Math.Min(x0, rx0[y]);
                    x1 = Math.Max(x1, rx1[y]);
                }
                int ph = bottom - top + 1;
                if (ph < 18 || ph > 36 || rowsOk < 8) continue;
                // El texto: pixeles cian en tramos cortos dentro de la pastilla
                bool[] text = new bool[W * H];
                int n = 0, ty0 = H, ty1 = -1;
                for (int y = top + 1; y < bottom; y++)
                {
                    int run = 0;
                    for (int x = x0 + 1; x <= x1; x++)
                    {
                        if (x < x1 && LabelColor(px, stride, x, y)) { run++; continue; }
                        if (run > 0 && run <= maxRun)
                        {
                            for (int k = x - run; k < x; k++) text[y * W + k] = true;
                            n += run;
                            ty0 = Math.Min(ty0, y);
                            ty1 = Math.Max(ty1, y);
                        }
                        run = 0;
                    }
                }
                if (n < 30 || ty1 < ty0) { top = bottom; continue; }
                Rectangle b;
                LabelText lt;
                byte[] m = Shape(text, W, H, ty0, ty1 - ty0 + 1, out b, out lt);
                if (m == null) { top = bottom; continue; }
                box = b;
                label = lt;
                return m;
            }
            return null;
        }

        // 09-11: fuera del HOME, que hay en pantalla: la parte con algo de luz (luma > 40; negro = casi nada) y una
        // miniatura de 32x18 en grises para ver si se mueve (la pantalla de bloqueo o la de elegir usuario no se mueven)
        public static double[] Thumb(Bitmap small, out double lit)
        {
            int stride;
            byte[] px = Pixels(small, out stride);
            double[] t = new double[32 * 18];
            int on = 0, all = 0;
            for (int by = 0; by < 18; by++)
                for (int bx = 0; bx < 32; bx++)
                {
                    double v = 0; int n = 0;
                    for (int y = by * 20; y < by * 20 + 20 && y < small.Height; y += 2)
                        for (int x = bx * 20; x < bx * 20 + 20 && x < small.Width; x += 2)
                        {
                            int o = y * stride + x * 4;
                            double l = (19 * px[o] + 183 * px[o + 1] + 54 * px[o + 2]) / 256.0;
                            v += l; n++; all++;
                            if (l > 40) on++;
                        }
                    t[by * 32 + bx] = n > 0 ? v / n : 0;
                }
            lit = all > 0 ? (double)on / all : 0;
            return t;
        }
        public const double DarkLit = 0.03;  // menos de un 3 % de la imagen con luz = pantalla negra (cargando)
        public const double MoveMin = 1.0;   // diferencia media de las miniaturas (Diff) por encima de la cual se mueve (HOME quieto 0,3)

        // 09-11: ¿el HOME atenuado (un aviso encima: "cerrar el programa", el menu de apagado...)? Las franjas del HOME
        // multiplicadas por un mismo factor k (0,12-0,85): las dos van a la par (correlacion corr >= 0,9) y el contraste
        // baja lo mismo que el brillo. Una pantalla oscura lisa (o con otro dibujo) no lo es. Medido con capturas del HOME
        // oscurecidas con un aviso encima: corr 0,99; negro, gris liso, pantallas tipo bloqueo o elegir usuario: no
        public static bool DimHome(double[] cur, double[] rf, out double k, out double corr)
        {
            k = 0; corr = 0;
            if (cur == null || rf == null || cur.Length != rf.Length) return false;
            int n = rf.Length;
            double mc = 0, mr = 0;
            for (int i = 0; i < n; i++) { mc += cur[i]; mr += rf[i]; }
            mc /= n; mr /= n;
            double cc = 0, rr = 0, cr = 0;
            for (int i = 0; i < n; i++) { double a = cur[i] - mc, b = rf[i] - mr; cc += a * a; rr += b * b; cr += a * b; }
            if (mr < 5 || rr <= 0 || cc <= 0) return false;
            corr = cr / Math.Sqrt(cc * rr);
            k = mc / mr;
            double ks = Math.Sqrt(cc / rr); // lo que baja el contraste
            return corr >= 0.9 && k >= 0.12 && k <= 0.85 && ks >= k * 0.6 && ks <= k * 1.5;
        }

        // Pantalla "No Signal" de la capturadora AVerMedia (la consola apagada): franjas negras arriba y abajo y un recuadro
        // gris en el centro. El mismo criterio que el script Lua (is_no_signal), sobre una rejilla de 48x27 puntos
        public static bool IsNoSignal(Bitmap small)
        {
            int stride;
            byte[] px = Pixels(small, out stride);
            int blkN = 0, blkOk = 0, boxN = 0, boxOk = 0;
            for (int j = 0; j < 27; j++)
            {
                double fy = (j + 0.5) / 27;
                int y = (int)(fy * small.Height);
                for (int i = 0; i < 48; i++)
                {
                    double fx = (i + 0.5) / 48;
                    int o = y * stride + (int)(fx * small.Width) * 4;
                    int b = px[o], g = px[o + 1], r = px[o + 2];
                    double l = (54 * r + 183 * g + 19 * b) / 256.0;
                    if (fy < 0.27 || fy > 0.67) { blkN++; if (l < 20) blkOk++; }
                    else if (fy > 0.33 && fy < 0.63 && fx > 0.10 && fx < 0.90)
                    {
                        boxN++;
                        if (l > 110 && l < 235 && Math.Abs(r - g) < 25 && Math.Abs(g - b) < 25) boxOk++;
                    }
                }
            }
            return blkN > 0 && boxN > 0 && blkOk >= blkN * 0.95 && boxOk >= boxN * 0.75;
        }

        public static double Diff(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 999;
            double s = 0;
            for (int i = 0; i < a.Length; i++) s += Math.Abs(a[i] - b[i]);
            return s / a.Length;
        }

        public const double SameIcon = 30;   // diferencia de iconos por debajo de la cual es el mismo juego
        public const double IsHome = 12;     // HomeDiff por debajo de la cual es el HOME

        public static string NumsText(double[] v)
        {
            StringBuilder sb = new StringBuilder();
            foreach (double x in v) { if (sb.Length > 0) sb.Append(','); sb.Append(((int)Math.Round(x)).ToString(CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }

        public static double[] ParseNums(string s, int count)
        {
            if (s == null) return null;
            string[] f = s.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length != count) return null;
            double[] v = new double[count];
            for (int i = 0; i < count; i++) if (!double.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
            return v;
        }
    }

    // Textos azules del HOME que no son el nombre de un juego ("Cambiar de usuario", bajo el juego en uso). Se
    // aprende su forma la primera vez que el OCR los lee (games\home_texts.txt) y HomeLabel los salta
    static class HomeHints
    {
        static string FilePath { get { return Path.Combine(Paths.Datos, @"games\home_texts.txt"); } }
        // (03-11: "Ver más", la casilla de la biblioteca, se tomaba por un juego)
        static readonly string[] Texts = { "cambiar de", "change user", "ver más", "ver mas", "see more" };
        // Casillas de la consola (no son juegos): al abrirlas se sigue en el HOME a 4K, sin LS ni perfil (08-11: la
        // "Lista de amigos" arranco LS dos veces; 05-11 "Tarjetas de juego virtuales")
        static readonly string[] SystemTexts = { "lista de amigos", "página de", "pagina de", "configuración de la consola",
            "configuracion de la consola", "tarjetas de juego virtuales", "nintendo eshop", "nintendo switch online",
            "álbum", "album", "noticias", "mandos", "gamechat", "game chat", "apagar", "modo de suspens",
            "modo de espera", // (15-11: el boton de apagado de la barra, visto en una captura del HOME del 31-10)
            // 15-11 (sesion de las 19:17): mas casillas y botones de la consola, y sus nombres con la consola en ingles
            // (solo frases que no pueden ser el nombre de un juego: una casilla tomada por la consola no guarda perfil)
            "eshop", "ajustes de la consola", "ajustes de consola", "ajustes del sistema", "opciones de energía", "notificaciones",
            "control parental", "controles parentales", "todos los programas", "system settings", "sleep mode", "power options",
            "virtual game cards", "friend list", "parental controls", "all software" };
        static string[] systemKeys;

        // Una casilla de la consola, no un juego. 15-11: por su clave (sin mayusculas, acentos ni signos) y aunque el OCR pierda las
        // primeras letras (lo que queda junto al icono): "gina de X_gorbea X" (Página de X_gorbea_X) y "onfiguración de la consola"
        // pasaban por juegos (se seleccionaban como uno y podian dar o cambiar el nombre de un perfil). Hasta 3 letras de menos al
        // principio si quedan 6 o mas; una frase larga tambien cortada al final (de 8 letras leidas como poco)
        public static bool IsSystemName(string name)
        {
            if (name == null) return false;
            string k = GameNames.Key(name);
            if (k.Length < 4) return false;
            string[] keys = systemKeys;
            if (keys == null)
            {
                List<string> l = new List<string>();
                foreach (string t in SystemTexts) { string tk = GameNames.Key(t); if (tk.Length > 0 && !l.Contains(tk)) l.Add(tk); }
                systemKeys = keys = l.ToArray();
            }
            foreach (string t in keys)
                for (int d = 0; d <= 3 && (d == 0 || t.Length - d >= 6); d++)
                {
                    string s = d == 0 ? t : t.Substring(d);
                    if (k.StartsWith(s, StringComparison.Ordinal)) return true;
                    if (s.Length >= 10 && k.Length >= 8 && s.StartsWith(k, StringComparison.Ordinal)) return true;
                }
            return false;
        }

        // 09-11: pantallas de la consola fuera del HOME que no son el juego, por el texto de la imagen entera (OCR): la de
        // elegir usuario (picker), "cerrar el programa", las opciones de un programa, la pantalla de bloqueo, avisos de
        // actualizacion. Solo retrasan la entrada: un juego con esas palabras en pantalla entra al dejar de verlas
        static readonly string[] PickerTexts = { "usuari", "user", "quién", "quien", "who " };
        // (tambien con las primeras letras perdidas por el OCR: "gina de X_gorbea X")
        static readonly string[] UserPageTexts = { "página de ", "pagina de ", "agina de ", "gina de ", "lista de amigos", "invitaciones",
            "añadir un amigo", "anadir un amigo", "friend list" };
        static readonly string[] ConsoleTexts = { "programa", "software", "datos guardados", "save data", "no guardados",
            "unsaved", "mismo botón", "mismo boton", "same button", "tres veces", "3 veces", "three times", "desbloque",
            "unlock", "cuenta nintendo", "nintendo account", "actualizaci", "update" };
        public static bool IsConsoleScreen(string ocr, out bool picker)
        {
            picker = false;
            if (string.IsNullOrEmpty(ocr)) return false;
            StringBuilder sb = new StringBuilder(" ");
            foreach (char c in ocr.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            sb.Append(' ');
            string n = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), " +", " ");
            // ("Cambiar de usuario" bajo el juego en uso, si se ve el HOME detras de un aviso, no es la de elegir usuario)
            n = n.Replace(" cambiar de usuario", " ").Replace(" change user", " ");
            // 15-11: la pagina del usuario ("Página de X_gorbea_X Perfil Invitaciones Lista de amigos...", 19:18:07) y la etiqueta de su
            // casilla en el HOME son de la consola, pero no la de elegir usuario (sus palabras, "usuario"..., la hacian pasar por ella)
            foreach (string t in UserPageTexts) if (n.Contains(" " + t)) return true;
            foreach (string t in PickerTexts) if (n.Contains(" " + t)) { picker = true; return true; }
            foreach (string t in ConsoleTexts) if (n.Contains(" " + t)) return true;
            return false;
        }

        // 16-11: el menu de apagado de la consola (boton de encendido mantenido, o el de la barra del HOME), por su texto (OCR): tras el, la
        // pantalla "No Signal" es la consola apagada o en espera y se cierra enseguida (sin la espera por un cambio de modo de video). No el
        // aviso de cerrar un programa, que va antes de arrancar otro juego (y de su posible "No Signal")
        static readonly string[] PowerTexts = { "apagar", "reiniciar", "modo de espera", "modo de suspens", "opciones de energ",
            "opciones de encendido", "power off", "restart", "sleep mode", "power options", "turn off" };
        public static bool IsPowerMenu(string ocr)
        {
            if (string.IsNullOrEmpty(ocr)) return false;
            string n = " " + System.Text.RegularExpressions.Regex.Replace(ocr.ToLowerInvariant().Replace('\r', ' ').Replace('\n', ' '), " +", " ") + " ";
            if (n.Contains(" cerrar el programa") || n.Contains(" close the software") || n.Contains(" programa suspendido")) return false;
            foreach (string t in PowerTexts) if (n.Contains(" " + t)) return true;
            return false;
        }
        static readonly object sync = new object();
        static List<byte[]> shapes;

        public static bool IsHintText(string ocr)
        {
            if (ocr == null) return false;
            string n = " " + ocr.ToLowerInvariant().Trim() + " ";
            foreach (string t in Texts) if (n.Contains(" " + t)) return true;
            return false;
        }

        static void Load()
        {
            if (shapes != null) return;
            shapes = new List<byte[]>();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    int eq = line.LastIndexOf('=');
                    if (line.StartsWith("#") || eq <= 0) continue;
                    string[] v = line.Substring(eq + 1).Split(',');
                    byte[] t = new byte[v.Length];
                    for (int i = 0; i < v.Length; i++) t[i] = byte.Parse(v[i], CultureInfo.InvariantCulture);
                    shapes.Add(t);
                }
            }
            catch (Exception ex) { Log.Error("Leyendo " + FilePath, ex); }
        }

        public static bool IsHint(byte[] shape)
        {
            lock (sync)
            {
                Load();
                foreach (byte[] s in shapes) if (HomeLabel.Same(s, shape)) return true;
                return false;
            }
        }

        public static void Learn(byte[] shape, string text)
        {
            if (shape == null || IsHint(shape)) return;
            lock (sync)
            {
                shapes.Add(shape);
                try
                {
                    if (!File.Exists(FilePath))
                        File.WriteAllText(FilePath, "# Textos azules del HOME que no son nombres de juego (se aprenden solos)\r\n",
                                          new UTF8Encoding(false));
                    StringBuilder sb = new StringBuilder(text.Replace("=", " ") + "=");
                    for (int i = 0; i < shape.Length; i++) sb.Append(i > 0 ? "," : "").Append(shape[i]);
                    File.AppendAllText(FilePath, sb.ToString() + "\r\n", new UTF8Encoding(false));
                }
                catch (Exception ex) { Log.Error("Guardando " + FilePath, ex); }
            }
            Log.Write("Aprendido el texto \"" + text + "\" del HOME: no es el nombre de un juego, se salta al leerlo");
        }
    }

    // OCR de Windows (Windows.Media.Ocr, por reflexion: el compilador de .NET 4 no ve WinRT)
    static class Ocr
    {
        static object engine;
        static MethodInfo asTask;
        static bool failed;
        static int readFails;           // lecturas seguidas que han fallado (15-11)
        static readonly object sync = new object();

        static object Await(object op, Type result)
        {
            Task t = (Task)asTask.MakeGenericMethod(result).Invoke(null, new object[] { op });
            t.Wait(5000);
            return t.GetType().GetProperty("Result").GetValue(t, null);
        }

        // Gris con el contraste estirado (el texto es cian: su canal mas alto)
        static void Stretch(Bitmap b)
        {
            Rectangle r = new Rectangle(0, 0, b.Width, b.Height);
            System.Drawing.Imaging.BitmapData d = b.LockBits(r, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                                                             System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                byte[] px = new byte[d.Stride * b.Height];
                Marshal.Copy(d.Scan0, px, 0, px.Length);
                int lo = 255, hi = 0;
                for (int i = 0; i + 3 < px.Length; i += 4)
                {
                    int l = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                    if (l < lo) lo = l;
                    if (l > hi) hi = l;
                }
                int span = Math.Max(1, hi - lo);
                for (int i = 0; i + 3 < px.Length; i += 4)
                {
                    int l = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                    byte v = (byte)((l - lo) * 255 / span);
                    px[i] = px[i + 1] = px[i + 2] = v;
                }
                Marshal.Copy(px, 0, d.Scan0, px.Length);
            }
            finally { b.UnlockBits(d); }
        }

        static Type WinRt(string name)
        {
            return Type.GetType(name + ", Windows.Foundation, ContentType=WindowsRuntime", true);
        }

        // 15-11: una linea de texto (el nombre del juego) a un alto fijo antes del OCR, filtrada: de la captura entera de 4K (letras de
        // ~60 px) o de la ligera de 1280x720 (~20 px, sacada de 1 de cada 3 pixeles) llega lo mismo: letras de ~30 px, el tamaño que mejor
        // lee el OCR de Windows (a 4K tal cual leyo "Rocket Leagues" por "Rocket League®")
        public const int LineHeight = 64;
        public static string ReadLine(Bitmap crop)
        {
            // (solo se reduce: ampliada x1,4, la de 1280x720 leia "Rocket Leagues"; tal cual, "Rocket League@" -> bien)
            if (crop.Height <= LineHeight) return Read(crop);
            int w = Math.Max(1, (int)Math.Round((double)crop.Width * LineHeight / crop.Height));
            using (Bitmap s = new Bitmap(w, LineHeight, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                using (Graphics g = Graphics.FromImage(s))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        ia.SetWrapMode(WrapMode.TileFlipXY); // (sin borde oscuro en los lados al reescalar)
                        g.DrawImage(crop, new Rectangle(0, 0, w, LineHeight), 0, 0, crop.Width, crop.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                return Read(s);
            }
        }

        // Con sync: el motor del OCR listo (una vez por sesion). false si este Windows no tiene OCR
        static bool EnsureEngine()
        {
            if (failed) return false;
            if (engine != null) return true;
            try
            {
                Assembly rt = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                foreach (MethodInfo m in rt.GetType("System.WindowsRuntimeSystemExtensions").GetMethods())
                    if (m.Name == "AsTask" && m.IsGenericMethod && m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1") { asTask = m; break; }
                engine = WinRt("Windows.Media.Ocr.OcrEngine").GetMethod("TryCreateFromUserProfileLanguages").Invoke(null, null);
                if (engine == null || asTask == null) throw new InvalidOperationException("sin OCR en este Windows");
                return true;
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("OCR de Windows no disponible (no se pueden recordar los ajustes por juego)", ex);
                return false;
            }
        }

        // Con sync: el OcrResult de Windows de la imagen (null si falla). k = cuanto se amplio (las coordenadas van en esa escala)
        static object RecognizeToResult(Bitmap bmp, out int k)
        {
            k = 1;
            if (!EnsureEngine()) return null;
            // (15-11: un fichero por proceso: --test-home con la app abierta escribia en el mismo)
            string tmp = Path.Combine(Path.GetTempPath(), "switch2_ocr_" + Process.GetCurrentProcess().Id + ".png");
            try
            {
                // Letras de mas de ~30 pixeles: el OCR lee mejor. Y con el contraste estirado
                k = bmp.Height < 40 ? Math.Max(2, 60 / Math.Max(1, bmp.Height)) : 1;
                using (Bitmap big = new Bitmap(bmp.Width * k, bmp.Height * k, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(big))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(bmp, 0, 0, big.Width, big.Height);
                    }
                    Stretch(big);
                    big.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                }
                Type fileT = WinRt("Windows.Storage.StorageFile"), streamT = WinRt("Windows.Storage.Streams.IRandomAccessStream");
                Type decT = WinRt("Windows.Graphics.Imaging.BitmapDecoder"), sbT = WinRt("Windows.Graphics.Imaging.SoftwareBitmap");
                Type resT = WinRt("Windows.Media.Ocr.OcrResult"), modeT = WinRt("Windows.Storage.FileAccessMode");
                object file = Await(fileT.GetMethod("GetFileFromPathAsync").Invoke(null, new object[] { tmp }), fileT);
                object stream = Await(fileT.GetMethod("OpenAsync", new Type[] { modeT }).Invoke(file, new object[] { Enum.ToObject(modeT, 0) }), streamT);
                try
                {
                    object dec = Await(decT.GetMethod("CreateAsync", new Type[] { streamT }).Invoke(null, new object[] { stream }), decT);
                    object sb = Await(decT.GetMethod("GetSoftwareBitmapAsync", Type.EmptyTypes).Invoke(dec, null), sbT);
                    object res = Await(engine.GetType().GetMethod("RecognizeAsync").Invoke(engine, new object[] { sb }), resT);
                    readFails = 0;
                    return res;
                }
                finally
                {
                    ((IDisposable)stream).Dispose();
                    try { File.Delete(tmp); } catch { } // (uno por proceso: que no se queden en %TEMP%)
                }
            }
            catch (Exception ex)
            {
                // 15-11: un fallo con una imagen (el .png ocupado, un tiempo de espera) ya no apaga el OCR para toda la sesion
                // (antes: todos los juegos nuevos de esa sesion como "Juego N"). Solo si falla muchas veces seguidas
                if (++readFails >= 20) { failed = true; Log.Error("OCR de Windows: 20 fallos seguidos, se deja de usar en esta sesion", ex); }
                else if (readFails == 1) Log.Error("OCR de Windows (una imagen)", ex);
                return null;
            }
        }

        // Texto de la imagen (null si el OCR no esta disponible)
        public static string Read(Bitmap bmp)
        {
            lock (sync)
            {
                int k;
                object res = RecognizeToResult(bmp, out k);
                if (res == null) return null;
                try { return (string)WinRt("Windows.Media.Ocr.OcrResult").GetProperty("Text").GetValue(res, null); }
                catch { return null; }
            }
        }

        // 20-11 (PS5JUEGO): cada linea del OCR con su alto en pixeles de bmp (para quedarse con el texto grande del overlay de la consola:
        // el nombre del juego en el centro de control / conmutador de la PS5 o en la Guia / fichas de la Xbox). Lista vacia si el OCR falla
        public sealed class Linea { public string Texto; public double Alto, X, Y; }
        public static List<Linea> ReadLines(Bitmap bmp)
        {
            List<Linea> r = new List<Linea>();
            lock (sync)
            {
                int k;
                object res = RecognizeToResult(bmp, out k);
                if (res == null) return r;
                try
                {
                    object lines = WinRt("Windows.Media.Ocr.OcrResult").GetProperty("Lines").GetValue(res, null);
                    if (lines == null) return r;
                    Type lineT = WinRt("Windows.Media.Ocr.OcrLine");
                    double inv = 1.0 / Math.Max(1, k);
                    foreach (object line in (System.Collections.IEnumerable)lines)
                    {
                        string t = (string)lineT.GetProperty("Text").GetValue(line, null);
                        object words = lineT.GetProperty("Words").GetValue(line, null);
                        double h = 0, minx = 1e9, miny = 1e9;
                        if (words != null)
                            foreach (object w in (System.Collections.IEnumerable)words)
                            {
                                object rect = w.GetType().GetProperty("BoundingRect").GetValue(w, null);
                                double hh = Convert.ToDouble(rect.GetType().GetProperty("Height").GetValue(rect, null));
                                double xx = Convert.ToDouble(rect.GetType().GetProperty("X").GetValue(rect, null));
                                double yy = Convert.ToDouble(rect.GetType().GetProperty("Y").GetValue(rect, null));
                                if (hh > h) h = hh;
                                if (xx < minx) minx = xx;
                                if (yy < miny) miny = yy;
                            }
                        if (!string.IsNullOrEmpty(t)) r.Add(new Linea { Texto = t, Alto = h * inv, X = (minx >= 1e9 ? 0 : minx) * inv, Y = (miny >= 1e9 ? 0 : miny) * inv });
                    }
                }
                catch (Exception ex) { Log.Error("OCR de Windows (lineas)", ex); }
                return r;
            }
        }
    }

    // ===================================================================
    //  15-11: el nombre del juego seleccionado en el HOME a partir de varias lecturas del OCR. El nombre azul tiene un ancho
    //  maximo: los largos se cortan en seco (sin "...", la ultima letra a medias: "Kingdc", "Switcl") y se desplazan en bucle
    //  (marquesina), asi que cada captura ve una ventana: "The Legend of Zelda: Tears of the Kingdc", "ars of the Kingdom -
    //  Nintendo Switch 2 E", "Switch 2 Edition The Legend of Zeld" (log de la app original, 12-11). Antes valia la primera
    //  lectura: perfiles con el nombre cortado ("Royal Editi", "INTERGRAD", "+ A NEW pc") y, si el OCR no leia nada antes de
    //  entrar, "Juego N" para siempre (reconocido luego por su icono, el nombre no se volvia a leer). Ahora las ventanas que se
    //  solapan se juntan, las lecturas se votan y el nombre es "seguro" con la vuelta entera vista (o, si cabe entero en la
    //  etiqueta, con dos lecturas iguales). Uno no seguro se sigue leyendo en las siguientes visitas al HOME
    // ===================================================================
    static class GameNames
    {
        // Para comparar: minusculas sin acentos, solo letras y cifras. idx[i] = posicion en s de la letra i de la clave
        public static string Key(string s, out int[] idx)
        {
            StringBuilder sb = new StringBuilder();
            List<int> ix = new List<int>();
            if (s != null)
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (!char.IsLetterOrDigit(c)) continue;
                    sb.Append(Fold(char.ToLowerInvariant(c)));
                    ix.Add(i);
                }
            idx = ix.ToArray();
            return sb.ToString();
        }

        public static string Key(string s) { int[] idx; return Key(s, out idx); }

        static char Fold(char c)
        {
            if (c < 128) return c;
            string d = c.ToString().Normalize(NormalizationForm.FormD);
            return d.Length > 0 && d[0] < 128 && char.IsLetter(d[0]) ? d[0] : c;
        }

        // Letras que el OCR confunde entre dos ventanas de la misma etiqueta (0/o, 1/l/i)
        static bool Eq(char a, char b) { return a == b || Conf(a) == Conf(b); }
        static char Conf(char c) { return c == '0' ? 'o' : (c == '1' || c == 'i') ? 'l' : c; }

        // "Juego 7": el nombre que se pone a un juego cuyo nombre no se ha podido leer
        public static bool IsGeneric(string name)
        {
            if (name == null) return true;
            string n = name.Trim();
            int k;
            return n.StartsWith("Juego ", StringComparison.Ordinal) &&
                   int.TryParse(n.Substring(6), NumberStyles.None, CultureInfo.InvariantCulture, out k);
        }

        // Lo que lee el OCR en la etiqueta, limpio. iconCut: la imagen ya va sin el icono de la izquierda (HomeLabel lo recorta);
        // si no, una letra o un simbolo suelto al principio es el icono ("B EA SPORTS FC 26")
        public static string Clean(string ocr, bool iconCut)
        {
            if (string.IsNullOrEmpty(ocr)) return null;
            string s = ocr.Replace("®", " ").Replace("™", " ").Replace("©", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            s = System.Text.RegularExpressions.Regex.Replace(s, " {2,}", " ").Trim();
            if (!iconCut && s.Length > 2 && s[1] == ' ' && (char.IsLetter(s[0]) || !char.IsLetterOrDigit(s[0]))) s = s.Substring(2);
            // Simbolos sueltos al principio y al final (el OCR lee "®" como "»" y el borde de la casilla como "|")
            int a = 0, b = s.Length;
            while (a < b && !char.IsLetterOrDigit(s[a]) && s[a] != '¡' && s[a] != '¿') a++;
            while (b > a && !char.IsLetterOrDigit(s[b - 1]) && s[b - 1] != ')' && s[b - 1] != '!' && s[b - 1] != '?') b--;
            s = s.Substring(a, b - a).Trim();
            // Una minuscula suelta al final tras un numero o una palabra en mayusculas: "EA SPORTS FC 26 a" (31-10: el borde
            // iluminado de la casilla, debajo del nombre)
            string[] t = s.Split(' ');
            if (t.Length >= 2)
            {
                string last = t[t.Length - 1], prev = t[t.Length - 2];
                if (last.Length == 1 && char.IsLower(last[0]) && prev.Length > 0 && (char.IsDigit(prev[prev.Length - 1]) || UpperWord(prev)))
                    s = string.Join(" ", t, 0, t.Length - 1).TrimEnd(' ', '-', ':', ',');
            }
            // 16-11: el "®" pegado al final leido como una mayuscula ("Rocket LeagueG" a las 00:39:58: el perfil "Rocket League" cambio de
            // nombre). Una sola mayuscula de las que se le parecen tras dos minusculas o mas, al final del nombre
            if (s.Length >= 4 && "GOQCBDE".IndexOf(s[s.Length - 1]) >= 0 && char.IsLower(s[s.Length - 2]) && char.IsLower(s[s.Length - 3]))
                s = s.Substring(0, s.Length - 1);
            return s.Length < 2 ? null : s;
        }

        static bool UpperWord(string w)
        {
            int up = 0;
            foreach (char c in w) { if (char.IsLower(c)) return false; if (char.IsUpper(c)) up++; }
            return up >= 2;
        }

        // ¿Parece un nombre? (no basura del OCR: "VIO,Hr" el 12-11, "|", "a")
        public static bool Plausible(string s)
        {
            if (s == null) return false;
            int letters = 0, alnum = 0, odd = 0;
            foreach (char c in s)
            {
                if (char.IsLetter(c)) letters++;
                if (char.IsLetterOrDigit(c)) alnum++;
                else if (" -:.,'!?&+/()¡¿".IndexOf(c) < 0) odd++;
            }
            if (alnum < 3 || letters < 2) return false;
            if (odd * 6 > alnum) return false;
            if (alnum < 8 && (odd > 0 || s.IndexOf(',') >= 0)) return false; // corto y con simbolos raros
            return true;
        }

        const int EdgeL = 2, EdgeR = 2, MinOverlap = 8; // letras de los bordes cortados que no cuentan; solape fiable minimo

        // b sigue a a hacia la derecha (otra ventana de la marquesina, mas avanzada): a + lo que anade b. null si no se solapan
        // lo bastante. Las 2 primeras letras de b y las 2 ultimas de a pueden ser letras cortadas ("TI Come:", "Kingdc"): no
        // cuentan, y el corte se hace en mitad del solape (asi lo cortado de a lo pone b)
        public static string Merge(string a, string b) { return Merge(a, b, 0); }

        // minShift: cuantas letras tiene que ir b por delante de a como poco (0: tambien la misma ventana, mas larga)
        public static string Merge(string a, string b, int minShift)
        {
            if (a == null || b == null) return null;
            int[] ia, ib;
            string x = Key(a, out ia), y = Key(b, out ib);
            int bestS = -1, bestScore = int.MinValue;
            for (int s = minShift; s < x.Length; s++)
            {
                int n = x.Length - s;               // letras de x que quedan bajo y
                if (n >= y.Length) continue;        // y no pasa del final de x: no anade nada
                int lo = Math.Min(EdgeL, n), hi = n - EdgeR, m = hi - lo;
                if (m < MinOverlap) continue;
                int bad = 0;
                for (int i = lo; i < hi && bad <= m / 10; i++) if (!Eq(x[s + i], y[i])) bad++;
                if (bad > m / 10) continue;
                int score = m - 4 * bad;
                if (score > bestScore) { bestScore = score; bestS = s; }
            }
            if (bestS < 0) return null;
            int nn = x.Length - bestS, cut = (Math.Min(EdgeL, nn) + nn - EdgeR) / 2;
            return (a.Substring(0, ia[bestS + cut]) + b.Substring(ib[cut])).Trim();
        }

        // La misma ventana leida dos veces (la etiqueta quieta): igual salvo las ultimas letras, las del borde cortado
        public static bool SameWindow(string a, string b)
        {
            string x = Key(a), y = Key(b);
            if (Math.Abs(x.Length - y.Length) > EdgeR || Math.Min(x.Length, y.Length) < MinOverlap) return x == y;
            int n = Math.Min(x.Length, y.Length) - EdgeR;
            for (int i = 0; i < n; i++) if (!Eq(x[i], y[i])) return false;
            return true;
        }

        // ¿La ventana b esta dentro de a? (sin contar sus letras de los bordes)
        public static bool Within(string a, string b)
        {
            string x = Key(a), y = Key(b);
            if (y.Length == 0) return false;
            if (x.Contains(y)) return true;
            int lo = Math.Min(EdgeL, y.Length / 4), hi = y.Length - Math.Min(EdgeR, y.Length / 4), m = hi - lo;
            if (m < 4) return false;
            for (int s = -lo; s + hi <= x.Length; s++)
            {
                int bad = 0;
                for (int i = lo; i < hi && bad <= m / 10; i++) if (s + i < 0 || !Eq(x[s + i], y[i])) bad++;
                if (bad <= m / 10) return true;
            }
            return false;
        }

        // La marquesina ha dado la vuelta: el final de s repite su principio ("...Switch 2 Edition The Legend of Zeld"). El nombre
        // (hasta donde empieza la repeticion) o null
        public static string CutRepeat(string s)
        {
            int[] idx;
            string x = Key(s, out idx);
            for (int p = 8; p <= x.Length - 8; p++)
            {
                int n = x.Length - p - 1, bad = 0;  // (la ultima letra puede estar a medias)
                for (int i = 0; i < n && bad <= n / 12; i++) if (!Eq(x[p + i], x[i])) bad++;
                if (bad <= n / 12) return TrimEnds(s.Substring(0, idx[p]));
            }
            return null;
        }

        static string TrimEnds(string s)
        {
            return s == null ? null : s.Trim().TrimEnd('-', ':', ',', '+', '/', ' ').Trim();
        }

        // Nombre guardado de un perfil (stored, seguro o no) frente al leido ahora (read, seguro o no): el nombre que debe quedar
        // si es mejor que el guardado, o null. Un "Juego N" toma cualquier nombre leido; uno no seguro, el leido si es la misma
        // ventana mas larga, o el seguro (votado o con la vuelta entera) aunque sea mas corto ("EA SPORTS FC 26 a"); uno seguro
        // solo se alarga con otro seguro. Un nombre leido distinto de uno seguro no lo cambia (iconos parecidos de dos juegos)
        public static string Better(string stored, bool storedSure, string read, bool readSure)
        {
            return Better(stored, storedSure, read, readSure, true);
        }

        // 16-11: readFirm = el leido es firme (NameReads.Firm: votado, o el principio de un nombre que se desplaza leido dos veces o el
        // guardado). Uno que no lo es (una lectura suelta) no cambia ningun nombre, tampoco un "Juego N" (00:39:58 "Rocket LeagueG")
        public static string Better(string stored, bool storedSure, string read, bool readSure, bool readFirm)
        {
            if (string.IsNullOrEmpty(read) || !Plausible(read)) return null;
            if (!readSure && !readFirm) return null;
            // (una casilla de la consola o un texto del HOME no es el nombre de un juego: "Juego 7" no pasa a "Modo de espera")
            if (HomeHints.IsSystemName(read) || HomeHints.IsHintText(read)) return null;
            if (string.IsNullOrEmpty(stored) || IsGeneric(stored)) return read;
            string ks = Key(stored), kr = Key(read);
            if (ks == kr) return !storedSure && readSure && stored != read ? read : null;
            bool related = Within(read, stored) || Within(stored, read) || Merge(stored, read) != null;
            if (storedSure)
                return readSure && related && kr.Length > ks.Length ? read : null;
            if (readSure) return related || !Plausible(stored) ? read : null;
            // Los dos sin confirmar: el leido si contiene al guardado y es mas largo (otra ventana mas avanzada)
            return related && kr.Length > ks.Length && Within(read, stored) ? read : null;
        }

        // ¿El mismo titulo, para juntar dos perfiles? Mas estricto que GameProfiles.SameGame: iguales, o uno es el principio del
        // otro cortado por la etiqueta (no seguro) y largo
        public static bool SameTitle(string a, bool aSure, string b, bool bSure)
        {
            if (JuegoConsola.TagConsola(a) != JuegoConsola.TagConsola(b)) return false; // 20-11 (PS5JUEGO): no juntar juegos de consolas distintas
            string x = Key(a), y = Key(b);
            if (x.Length == 0 || y.Length == 0) return false;
            if (x == y) return true;
            string shortK = x.Length < y.Length ? x : y, longK = x.Length < y.Length ? y : x;
            bool shortSure = x.Length < y.Length ? aSure : bSure;
            if (shortSure || shortK.Length < 15) return false;
            int n = shortK.Length - 1; // (su ultima letra puede estar a medias)
            for (int i = 0; i < n; i++) if (!Eq(shortK[i], longK[i])) return false;
            return true;
        }

        // ---- Prueba sin consola (--test-nombres): limpieza, votacion, union de ventanas y que nombre queda ----
        public static string SelfTest(out int failures)
        {
            StringBuilder sb = new StringBuilder();
            int fails = 0;
            Action<string, bool> check = delegate (string what, bool ok)
            {
                sb.Append(ok ? "OK    " : "FALLO ").Append(what).Append("\r\n");
                if (!ok) fails++;
            };
            Func<string, string, bool> eq = delegate (string got, string want) { return got == want; };

            check("Clean \"B EA SPORTS FC 26\" (icono leido como letra)", eq(Clean("B EA SPORTS FC 26", false), "EA SPORTS FC 26"));
            check("Clean \"A Short Hike\" con el icono ya recortado", eq(Clean("A Short Hike", true), "A Short Hike"));
            check("Clean \"Rocket League»\"", eq(Clean("Rocket League»", true), "Rocket League"));
            check("Clean \"EA SPORTS FC 26 a\" (borde de la casilla)", eq(Clean("EA SPORTS FC 26 a", true), "EA SPORTS FC 26"));
            check("Clean \"Mario Kart World\" sin tocar", eq(Clean("Mario Kart World", true), "Mario Kart World"));
            check("Clean de dos lineas", eq(Clean("The Legend of\r\nZelda", true), "The Legend of Zelda"));
            check("Plausible \"VIO,Hr\" = no", !Plausible("VIO,Hr"));
            check("Plausible \"|\" = no", !Plausible("|"));
            check("Plausible \"DRAGON BALL Z: KAKAROT + A NEW pc\"", Plausible("DRAGON BALL Z: KAKAROT + A NEW pc"));
            check("Plausible \"Ys X\" (corto)", Plausible("Ys X"));
            check("IsGeneric \"Juego 7\"", IsGeneric("Juego 7"));
            check("IsGeneric \"Juego 7 Deluxe\" = no", !IsGeneric("Juego 7 Deluxe"));

            // Ventanas reales de la marquesina (log de la app original, 12-11)
            string m = Merge("The Legend of Zelda: Tears of the Kingdc", "ars of the Kingdom - Nintendo Switch 2 E");
            check("Merge TOTK 1+2 = \"" + m + "\"", eq(m, "The Legend of Zelda: Tears of the Kingdom - Nintendo Switch 2 E"));
            m = Merge("Kingdom Come: Deliverance - Royal Editi", "TI Come: Deliverance - Royal Edition");
            check("Merge Kingdom Come = \"" + m + "\"", eq(m, "Kingdom Come: Deliverance - Royal Edition"));
            check("Merge Ocarina / TOTK = null (otro juego)", Merge("The Legend of Zelda: Ocarina of Time", "Tears of the Kingdom - Nintendo") == null);
            string c = CutRepeat("The Legend of Zelda: Tears of the Kingdom - Nintendo Switch 2 Edition The Legend of Zeld");
            check("CutRepeat TOTK = \"" + c + "\"", eq(c, "The Legend of Zelda: Tears of the Kingdom - Nintendo Switch 2 Edition"));
            check("CutRepeat sin vuelta = null", CutRepeat("The Legend of Zelda: Tears of the Kingdom - Nintendo Switch 2 E") == null);

            // Marquesina completa: el principio (parado un momento), ventanas que avanzan y la vuelta
            NameReads r = new NameReads(null, null);
            foreach (string w in new string[] { "The Legend of Zelda: Tears of the Kingdc", "The Legend of Zelda: Tears of the Kingdc",
                                                 "The Legend of Zelda: Tears of the Kingdc", "The Legend of Zelda: Tears of the Kingdc",
                                                 "egend of Zelda: Tears of the Kingdom - N", "of Zelda: Tears of the Kingdom - Ninten",
                                                 "ars of the Kingdom - Nintendo Switch 2 E", "e Kingdom - Nintendo Switch 2 Edition",
                                                 "om - Nintendo Switch 2 Edition The Le", "Switch 2 Edition The Legend of Zeld" })
                r.Add(Clean(w, true), true);
            check("Marquesina TOTK = \"" + r.Best + "\" seguro " + r.Sure,
                  eq(r.Best, "The Legend of Zelda: Tears of the Kingdom - Nintendo Switch 2 Edition") && r.Sure);

            // La primera lectura a medias (la etiqueta apareciendo): manda el principio leido varias veces
            r = new NameReads(null, null);
            foreach (string w in new string[] { "The Legend of Zel", "The Legend of Zelda: Tears of the Kingdc", "The Legend of Zelda: Tears of the Kingdo",
                                                 "of Zelda: Tears of the Kingdom - Ninten", "ars of the Kingdom - Nintendo Switch 2 E",
                                                 "om - Nintendo Switch 2 Edition The Le", "Switch 2 Edition The Legend of Zeld" })
                r.Add(Clean(w, true), true);
            check("Primera lectura a medias = \"" + r.Best + "\" seguro " + r.Sure,
                  eq(r.Best, "The Legend of Zelda: Tears of the Kingdom - Nintendo Switch 2 Edition") && r.Sure);
            // Abierta la app con la etiqueta ya desplazandose (sin principio repetido): el nombre, pero no seguro
            r = new NameReads(null, null);
            foreach (string w in new string[] { "ars of the Kingdom - Nintendo Switch 2 E", "om - Nintendo Switch 2 Edition The Le",
                                                 "Switch 2 Edition The Legend of Zeld", "The Legend of Zelda: Tears of the Kingdc" })
                r.Add(Clean(w, true), true);
            check("Sin principio fiable: no seguro (\"" + r.Best + "\")", !r.Sure);
            // La misma ventana con una letra de mas al final no es desplazarse
            r = new NameReads(null, null);
            r.Add("Mortal Kombat 11 REMASTER", false);
            r.Add("Mortal Kombat 11 REMASTERS", false);
            r.Add("Mortal Kombat 11 REMASTER", false);
            check("Etiqueta quieta con una lectura de mas: no se desplaza, seguro", !r.Clipped && r.Sure && r.Best == "Mortal Kombat 11 REMASTER");

            // Sin la vuelta: el nombre mas largo hasta ahora, no seguro
            r = new NameReads(null, null);
            r.Add("DRAGON QUEST XI S: Ecos de un pasad", true);
            r.Add("DRAGON QUEST XI S: Ecos de un pasad", true);
            r.Add("QUEST XI S: Ecos de un pasado perdid", true);
            check("Marquesina a medias = \"" + r.Best + "\" no seguro", eq(r.Best, "DRAGON QUEST XI S: Ecos de un pasado perdid") && !r.Sure);

            // Perfil guardado con el nombre cortado (reconocido por su icono) y una ventana nueva: se alarga
            r = new NameReads(null, "Kingdom Come: Deliverance - Royal Editi");
            r.Add(Clean("TI Come: Deliverance - Royal Edition", true), true);
            check("Guardado + ventana = \"" + r.Best + "\"", eq(r.Best, "Kingdom Come: Deliverance - Royal Edition") && !r.Sure);
            check("Better guardado cortado -> \"" + Better("Kingdom Come: Deliverance - Royal Editi", false, r.Best, r.Sure) + "\"",
                  eq(Better("Kingdom Come: Deliverance - Royal Editi", false, r.Best, r.Sure), "Kingdom Come: Deliverance - Royal Edition"));

            // Etiqueta que cabe entera: votacion
            r = new NameReads(null, null);
            r.Add(Clean("EA SPORTS FC 26", true), false);
            check("Una lectura: no seguro", r.Best == "EA SPORTS FC 26" && !r.Sure);
            r.Add(Clean("EA SPORTS FC 2G", true), false);
            r.Add(Clean("EA SPORTS FC 26", true), false);
            check("Votacion FC 26 = \"" + r.Best + "\" seguro " + r.Sure, eq(r.Best, "EA SPORTS FC 26") && r.Sure);
            r = new NameReads(null, null);
            r.Add(Clean("VIO,Hr", true), false);
            r.Add(null, false);
            check("Basura y nada: sin nombre (2 fallos)", r.Best == null && r.Fails == 2);
            r = new NameReads(null, null);
            r.Add("Hollow Knight - Nintendo Switch 2 Editio", true);
            r.Add("Hollow Knight- Nintendo Switch 2 Editio", true);
            check("Hollow Knight cortado, dos lecturas iguales: no seguro", r.Best != null && !r.Sure && Key(r.Best) == Key("Hollow Knight - Nintendo Switch 2 Editio"));
            // Un nombre largo exacto al ancho maximo que no se desplaza: seguro tras muchas lecturas iguales
            r = new NameReads(null, null);
            for (int i = 0; i < 7; i++) r.Add("Divinity Original Sin 2 - Nintendo Switch", true, DateTime.Now.AddSeconds(i));
            check("Ancho maximo sin desplazarse 7 lecturas en 6 s: seguro", r.Sure);

            // Que nombre queda en el perfil
            check("Better Juego 7 -> leido", eq(Better("Juego 7", false, "Hollow Knight - Nintendo Switch 2 Editio", false), "Hollow Knight - Nintendo Switch 2 Editio"));
            check("Better basura del borde -> seguro mas corto", eq(Better("EA SPORTS FC 26 a", false, "EA SPORTS FC 26", true), "EA SPORTS FC 26"));
            check("Better seguro no cambia por otro nombre", Better("RESIDENT EVIL requiem", true, "Pokémon Champions", true) == null);
            check("Better no seguro no cambia por una lectura suelta distinta", Better("RESIDENT EVIL requiem", false, "Pokémon Champions", false) == null);
            check("Better \"VIO,Hr\" -> seguro distinto", eq(Better("VIO,Hr", false, "Mortal Kombat 1", true), "Mortal Kombat 1"));
            check("Better misma ventana con basura no", Better("Red Dead Redemption - Nintendo Switcl", false, "Red Dead Redemption - Nintendo Switct", false) == null);
            check("Better \"Juego 7\" no pasa a una casilla de la consola", Better("Juego 7", false, "Modo de espera", true) == null);
            check("SameTitle cortado / entero", SameTitle("Kingdom Come: Deliverance - Royal Editi", false, "Kingdom Come: Deliverance - Royal Edition", true));
            check("SameTitle Zelda / Ocarina = no", !SameTitle("The Legend of Zelda", true, "The Legend of Zelda: Ocarina of Time", true));
            check("SameGame \"Zelda\" / \"Zelda 2\" = no", !GameProfiles.SameGame("Zelda", "Zelda 2"));
            check("SameGame Ocarina / TOTK = no", !GameProfiles.SameGame("The Legend of Zelda: Ocarina of Time", "The Legend of Zelda: Tears of the Kingdc"));
            check("SameGame Switcl / Switct", GameProfiles.SameGame("Red Dead Redemption - Nintendo Switcl", "Red Dead Redemption - Nintendo Switct"));

            // 15-11 (sesion de las 19:17): casillas de la consola, tambien con las primeras letras perdidas por el OCR
            foreach (string s in new string[] { "gina de X_gorbea X", "onfiguración de la consola", "Página de X_gorbea_X", "Pagina de ozcoidiii",
                                                 "agina de Gorben", "Configuración de la consola", "Configuración de la cons", "Noticias", "oticias",
                                                 "Nintendo eShop", "eShop", "Álbum", "Album", "Mandos", "Modo de espera", "odo de espera", "GameChat",
                                                 "Game Chat", "Ajustes de la consola", "Lista de amigos", "ista de amigos", "Tarjetas de juego virtuales",
                                                 "Nintendo Switch Online", "Apagar", "Notificaciones", "Control parental", "System Settings", "Sleep Mode" })
                check("IsSystemName \"" + s + "\"", HomeHints.IsSystemName(s));
            foreach (string s in new string[] { "Leyendas Pokémon: Arceus", "The Legend of Zelda: Ocarina of Time", "EA SPORTS FC 26",
                                                 "Red Dead Redemption - Nintendo Switcl", "Mario Kart World", "Nintendo 64 - Nintendo Switch Online",
                                                 "Nintendo Switch Sports", "Paper Mario", "Albion Online", "Control Ultimate Edition", "Mandalorian",
                                                 "Juego 7", "Kingdom Come: Deliverance - Royal Edition", "Animal Crossing: New Horizons", "Ys X" })
                check("IsSystemName \"" + s + "\" = no", !HomeHints.IsSystemName(s));
            check("Better \"Juego 7\" no pasa a \"gina de X_gorbea X\"", Better("Juego 7", false, "gina de X_gorbea X", false) == null);
            check("Better un juego no pasa a \"onfiguración de la consola\"", Better("Leyendas Pokémon: Arceus", false, "onfiguración de la consola", true) == null);
            // La pagina del usuario (y la etiqueta de su casilla) es de la consola pero no la de elegir usuario; la de elegir usuario si
            bool pk;
            bool cs = HomeHints.IsConsoleScreen("Página de X_gorbea_X Perfil Invitaciones Lista de amigos Añadir un amigo Configuración de usuario", out pk);
            check("IsConsoleScreen pagina del usuario: de la consola, no elegir usuario", cs && !pk);
            cs = HomeHints.IsConsoleScreen("gina de X_gorbea X usuario", out pk);
            check("IsConsoleScreen \"gina de X_gorbea X\" (etiqueta cortada): no elegir usuario", cs && !pk);
            cs = HomeHints.IsConsoleScreen("¿Quién va a usar el programa? X_gorbea_X ozcoidiii 17 Gorben O Atrás O", out pk);
            check("IsConsoleScreen elegir usuario: si", cs && pk);
            cs = HomeHints.IsConsoleScreen("19:17 Ya disponible Warzone 1-7. Ya disp el of Time ora red", out pk);
            check("IsConsoleScreen imagen de juego: no", !cs && !pk);

            // 16-11 (sesion de las 00:39): nombres de una sola lectura, basura del OCR y la biblioteca
            check("Clean \"Rocket LeagueG\" (el ® pegado, leido como G) = \"Rocket League\"", eq(Clean("Rocket LeagueG", true), "Rocket League"));
            check("Clean \"Rocket League@\"", eq(Clean("Rocket League@", true), "Rocket League"));
            check("Clean \"NieR Replicant\" sin tocar", eq(Clean("NieR Replicant", true), "NieR Replicant"));
            check("Clean \"; Metro 2033 Redux\" (un simbolo delante)", eq(Clean("; Metro 2033 Redux", true), "Metro 2033 Redux"));
            r = new NameReads(null, null);
            r.Add(Clean("LLó", true), false);
            check("\"LLó\" (00:45:55, una lectura del arte de Fall Guys): ni seguro ni firme", r.Best == "LLó" && !r.Sure && !r.Firm);
            check("Better \"Rocket League\" -> \"Rocket LeagueG\" de una lectura: no (00:39:58)", Better("Rocket League", false, "Rocket LeagueG", false, false) == null);
            check("Better \"Juego 7\" -> \"LLó\" de una lectura: no", Better("Juego 7", false, "LLó", false, false) == null);
            check("Better \"Juego 7\" -> un nombre firme: si", eq(Better("Juego 7", false, "Hollow Knight - Nintendo Switch 2 Editio", false, true), "Hollow Knight - Nintendo Switch 2 Editio"));
            r = new NameReads(null, null);
            r.Add("RESIDENT EVIL requiem", false);
            r.Add("RESIDENT EVIL requiem", false);
            check("Dos lecturas iguales: seguro y firme", r.Sure && r.Firm);
            r = new NameReads(null, "Kingdom Come: Deliverance - Royal Editi");
            r.Add(Clean("TI Come: Deliverance - Royal Edition", true), true);
            check("Nombre guardado + otra ventana: firme, alarga el nombre", r.Firm &&
                  eq(Better("Kingdom Come: Deliverance - Royal Editi", false, r.Best, r.Sure, r.Firm), "Kingdom Come: Deliverance - Royal Edition"));
            r = new NameReads(null, null);
            r.Add("DRAGON QUEST XI S: Ecos de un pasad", true);
            check("Nombre que se desplaza, una lectura: no firme", !r.Firm);
            r.Add("DRAGON QUEST XI S: Ecos de un pasadc", true);
            check("Nombre que se desplaza, su principio leido dos veces: firme", r.Firm);
            check("SameGame \"LLó\" / \"RESIDENT EVIL requiem\" = no", !GameProfiles.SameGame("LLó", "RESIDENT EVIL requiem"));
            NameReads lib = new NameReads(new Rectangle(383, 157, 162, 24));
            check("Biblioteca: la misma pastilla (desplazandose el nombre) es la misma casilla", lib.SameBox(new Rectangle(380, 158, 170, 24)));
            check("Biblioteca: otra pastilla es otra casilla", !lib.SameBox(new Rectangle(76, 143, 250, 23)));
            check("IsPowerMenu menu de apagado", HomeHints.IsPowerMenu("Opciones de energía Modo de espera Apagar Reiniciar"));
            check("IsPowerMenu el boton de la barra (\"Modo de espera\")", HomeHints.IsPowerMenu("Modo de espera"));
            check("IsPowerMenu el aviso de cerrar el programa = no", !HomeHints.IsPowerMenu("¿Quieres cerrar el programa suspendido? Se perderán los datos no guardados. Cerrar"));
            check("IsPowerMenu el HOME con un juego = no", !HomeHints.IsPowerMenu("EA SPORTS FC 26 Cambiar de usuario Opciones Confirmar"));
            failures = fails;
            sb.Append(fails == 0 ? "Todo bien" : fails + " fallos").Append("\r\n");
            return sb.ToString();
        }
    }

    // Las lecturas del nombre del juego seleccionado en el HOME (una por captura) hasta tenerlo seguro. Otra seleccion (otro
    // icono) = otras lecturas
    class NameReads
    {
        public readonly double[] Icon;              // icono del juego seleccionado (HomeLabel.Icon)
        public readonly string Prior;               // el nombre guardado, si se le reconocio por el icono (cuenta como el principio)
        public int Attempts, Fails;                 // lecturas intentadas / sin texto util
        public bool Clipped;                        // la etiqueta llega al ancho maximo o se ha visto desplazarse
        public string Best;                         // el mejor nombre hasta ahora (null: ninguno)
        public bool Sure;                           // confirmado (dos lecturas iguales o la vuelta entera)
        public string LastRaw;                      // la ultima lectura (para el log)
        public int Saved;                           // imagenes de diagnostico guardadas de esta seleccion
        public int FullGrabs;                       // lecturas hechas con la captura entera (no la ligera)
        public int LoggedLen;                       // letras del ultimo nombre que se puso en el log
        // 16-11: firme = seguro, o (nombre largo que se desplaza) su principio leido dos veces o el nombre guardado. Solo un nombre firme da
        // nombre a un juego nuevo o cambia el de un perfil (00:39:58 "Rocket League" -> "Rocket LeagueG" y 00:45:56 "LLó", de una lectura)
        public bool Firm;
        public readonly bool Library;               // 16-11: leido en la biblioteca (la pastilla del nombre; sin icono)
        public readonly Rectangle Box;              // (en la biblioteca: donde estaba el nombre; otra casilla = otro sitio)
        public const int MaxAttempts = 30;          // por seleccion (cada 0,5 s: 15 s)
        readonly List<string> reads = new List<string>();
        readonly List<DateTime> times = new List<DateTime>();
        string first;                               // la primera lectura, hecha al seleccionarlo (la etiqueta empieza por el principio)

        public NameReads(double[] icon, string prior)
        {
            Icon = icon;
            Prior = prior != null && !GameNames.IsGeneric(prior) && GameNames.Plausible(prior) ? prior : null;
        }

        public NameReads(Rectangle libraryBox) : this(null, null)
        {
            Library = true;
            Box = libraryBox;
        }

        // ¿La misma casilla de la biblioteca? (el nombre en el mismo sitio, a 640x360; desplazandose cambia algo su ancho)
        public bool SameBox(Rectangle b)
        {
            return Library && Math.Abs(b.X + b.Width / 2 - (Box.X + Box.Width / 2)) <= 12 && Math.Abs(b.Y + b.Height / 2 - (Box.Y + Box.Height / 2)) <= 6;
        }

        public bool Done { get { return Sure || Attempts >= MaxAttempts; } }
        public int Count { get { return reads.Count; } }

        public bool Add(string clean, bool clipped) { return Add(clean, clipped, DateTime.Now); }

        // Una lectura ya limpia (null: el OCR no leyo nada util). true si cambia el nombre o si queda seguro
        public bool Add(string clean, bool clipped, DateTime t)
        {
            Attempts++;
            LastRaw = clean;
            if (clipped) Clipped = true;
            if (clean == null || !GameNames.Plausible(clean)) { Fails++; return false; }
            if (reads.Count == 0 && Attempts == 1) first = clean;
            reads.Add(clean);
            times.Add(t);
            string ob = Best;
            bool os = Sure;
            Recompute();
            return ob != Best || os != Sure;
        }

        void Recompute()
        {
            // Lecturas agrupadas por su clave (sin mayusculas, acentos ni signos)
            List<string> keys = new List<string>(), reps = new List<string>();
            List<int> counts = new List<int>();
            List<DateTime> firstAt = new List<DateTime>();
            for (int i = 0; i < reads.Count; i++)
            {
                string k = GameNames.Key(reads[i]);
                int j = keys.IndexOf(k);
                if (j < 0) { keys.Add(k); reps.Add(reads[i]); counts.Add(1); firstAt.Add(times[i]); }
                else counts[j]++;
            }
            int top = 0;
            for (int j = 1; j < keys.Count; j++) if (counts[j] > counts[top]) top = j;
            int priorJ = Prior != null ? keys.IndexOf(GameNames.Key(Prior)) : -1;
            // ¿Se desplaza? Dos lecturas distintas que son ventanas seguidas del mismo nombre (una va por delante de la otra)
            if (!Clipped && keys.Count > 1)
                for (int j = 0; j < keys.Count && !Clipped; j++)
                    for (int q = 0; q < keys.Count && !Clipped; q++)
                        if (q != j && GameNames.Merge(reps[j], reps[q], 2) != null) Clipped = true;
            if (!Clipped)
            {
                // Cabe entera: gana la mas leida; segura con 2 iguales (contando el nombre guardado) y mas que cualquier otra
                int c = counts[top] + (priorJ == top ? 1 : 0), second = 0;
                for (int j = 0; j < keys.Count; j++) if (j != top) second = Math.Max(second, counts[j] + (priorJ == j ? 1 : 0));
                Best = reps[top];
                Sure = c >= 2 && c > second;
                Firm = Sure;
                return;
            }
            // Marquesina: se para un momento en el principio (lecturas iguales) y luego avanza (cada lectura otra ventana). Desde el
            // principio se van juntando las ventanas que lo siguen; la vuelta entera (el final repite el principio) da el nombre
            // seguro. Principio fiable: la ventana leida 2 veces o mas, o el nombre guardado; si no, la primera lectura (no seguro)
            int[] win = new int[keys.Count];
            int topW = 0;
            for (int j = 0; j < keys.Count; j++)
            {
                for (int q = 0; q < keys.Count; q++) if (GameNames.SameWindow(reps[j], reps[q])) win[j] += counts[q];
                if (win[j] > win[topW] || (win[j] == win[topW] && firstAt[j] < firstAt[topW])) topW = j;
            }
            string anchor;
            bool strong = true;
            if (win[topW] >= 2) anchor = reps[topW];
            else if (Prior != null) anchor = Prior;
            else { anchor = first ?? reps[topW]; strong = false; }
            string best = Assemble(anchor, reps);
            if (Prior != null && anchor != Prior)
            {
                // El nombre guardado es el principio de una visita anterior: si lo leido lo continua, se alarga desde el
                string withPrior = GameNames.Merge(Prior, best, 0);
                if (withPrior != null) { best = Assemble(withPrior, reps); strong = true; }
            }
            int explained = Explained(best, reps);
            string cut = GameNames.CutRepeat(best);
            Best = cut ?? best;
            if (cut != null && strong && explained * 10 >= reps.Count * 7) { Sure = Firm = true; return; }
            // Al ancho maximo pero sin desplazarse: muchas lecturas iguales durante un rato largo (no es la pausa del principio)
            Sure = keys.Count == 1 && counts[0] >= 6 && (times[times.Count - 1] - times[0]).TotalSeconds >= 5.5;
            Firm = Sure || strong;
        }

        // seed y las ventanas que la siguen, juntadas mientras alarguen el nombre
        static string Assemble(string seed, List<string> reps)
        {
            string m = seed;
            for (int round = 0; round < 40; round++)
            {
                string bestNext = null;
                int bestLen = GameNames.Key(m).Length;
                foreach (string r in reps)
                {
                    string mm = GameNames.Merge(m, r);
                    if (mm == null) continue;
                    int len = GameNames.Key(mm).Length;
                    // (la que menos anade: la ventana siguiente; asi no se salta letras)
                    if (len > GameNames.Key(m).Length && (bestNext == null || len < bestLen)) { bestNext = mm; bestLen = len; }
                }
                if (bestNext == null) break;
                m = bestNext;
                if (GameNames.CutRepeat(m) != null) break;
            }
            return m;
        }

        static int Explained(string name, List<string> reps)
        {
            // (con la vuelta: el nombre dos veces seguidas contiene las ventanas que cruzan el final)
            string twice = name + " " + name;
            int n = 0;
            foreach (string r in reps) if (GameNames.Within(twice, r)) n++;
            return n;
        }
    }

    // Ajustes recordados por juego (games\profiles.txt): nombre|captura|nivel|pasadas|dlss5|escalador|modo|generacion|nombre_seguro
    // (1.2.2: partial, el multiplicador de la generacion en src\Lanzador\Multiplicador.cs)
    partial class GameProfile
    {
        public string Name;          // el nombre leido en el HOME (GameNames: los largos juntando sus ventanas) o "Juego N"
        public bool NameSure;        // 15-11: nombre confirmado (votado, o la vuelta entera de la marquesina); si no, se sigue leyendo en el HOME
        public string Capture = "2560x1440";
        public int Level = 2, Passes = 1;
        public bool Dlss5 = true, Scaler = true, Cap = false, Fg = true;
        public string ScalerPick = "";   // 21-11 (1.2.01): escalador elegido en el menu (fsr / ninguno; "" = el de siempre). Columna 6
        public bool Mode120 = false;  // 20-11 (HZ120): modo 120 fps guardado por juego (columna "modo" = 120; manda sobre Cap)
        // 19-11 (GENERACION): el tipo de generacion elegido con L3 en este juego, en el modo 60 y en el 30 ("" = el del ini). Columna 10
        public string Pick60 = "", Pick30 = "";
        // Sus iconos en el HOME (HomeLabel.Icon): asi se le reconoce, sin depender del OCR. 15-11: puede tener varios (dos perfiles
        // del mismo juego juntados, o reconocido por el nombre con otro icono), como mucho MaxIcons; el primero es el de cuando se creo
        public readonly List<double[]> Icons = new List<double[]>();
        public const int MaxIcons = 4;
        public double[] Icon { get { return Icons.Count > 0 ? Icons[0] : null; } }

        public void AddIcon(double[] icon)
        {
            if (icon == null) return;
            foreach (double[] i in Icons) if (HomeLabel.Diff(i, icon) < HomeLabel.SameIcon / 3) return; // (ya lo tiene)
            Icons.Add(icon);
            if (Icons.Count > MaxIcons) Icons.RemoveAt(1);
        }

        // Diferencia con el mas parecido de sus iconos (999 sin ninguno)
        public double IconDiff(double[] icon)
        {
            double best = 999;
            foreach (double[] i in Icons) best = Math.Min(best, HomeLabel.Diff(icon, i));
            return best;
        }

        public string Summary()
        {
            return (Capture == "1280x720" ? "720p" : Capture == "1920x1080" ? "1080p" : Capture == "3840x2160" ? "4K" : "1440p") +
                   (Mode120 ? ", modo 120 fps" : Cap ? ", modo 30 fps" : "") + ", nivel " + Level + ", " + Passes + " pasada" + (Passes > 1 ? "s" : "") +
                   (Dlss5 ? "" : ", DLSS 5 apagado") + (ScalerPick == "fsr" ? ", FSR 1" : ScalerPick == "ninguno" ? ", sin reescalado" : Scaler ? "" : ", escalador alternativo") + (Fg ? "" : ", sin generacion de fotogramas") +
                   (Pick60.Length > 0 ? ", generacion del 60: " + Pick60 : "") + (Pick30.Length > 0 ? ", generacion del 30: " + Pick30 : "");
        }

        // 19-11 (GENERACION): la columna 10 ("smooth,xess"; vacio = los del ini). (1.2.2-1.2.4 llevaba detras el multiplicador de cada modo,
        // "dlss,xess,0,4": 1.2.4 ronda 6, QUITAFG, se lee y se tira -sin x3 / x4- y dlss / vfg pasan al mas parecido: Config.ParsePick)
        public string PickText { get { return Pick60.Length == 0 && Pick30.Length == 0 ? "" : Pick60 + "," + Pick30; } }
        public void ParsePick(string s)
        {
            string[] f = (s ?? "").Split(',');
            Pick60 = Config.ParsePick(f.Length > 0 ? f[0] : "");
            Pick30 = Config.ParsePick(f.Length > 1 ? f[1] : "");
        }
    }

    static partial class GameProfiles   // (1.2.2: parte en src\VueltaJuego.cs: nombres con basura del OCR)
    {
        // (15-11: la carpeta se cambia solo en la prueba --test-perfiles, sobre una copia)
        static string Dir = Path.Combine(Paths.Datos, "games");
        static string FilePath { get { return Path.Combine(Dir, "profiles.txt"); } }
        static readonly object sync = new object();
        static List<GameProfile> list;

        static void Load()
        {
            if (list != null) return;
            list = new List<GameProfile>();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    if (raw.StartsWith("#")) continue;
                    string[] f = raw.Split('|');
                    if (f.Length < 6 || f[0].Trim().Length == 0) continue;
                    GameProfile p = new GameProfile();
                    p.Name = f[0].Trim();
                    p.Capture = f[1].Trim();
                    int.TryParse(f[2], out p.Level);
                    int.TryParse(f[3], out p.Passes);
                    p.Level = Math.Max(1, Math.Min(4, p.Level));
                    p.Passes = Math.Max(1, Math.Min(3, p.Passes)); // (sin 4 pasadas desde el 03-11: las guardadas con 4 pasan a 3)
                    p.Dlss5 = f[4].Trim() != "0";
                    p.Scaler = f[5].Trim() != "0";
                    if (f[5].Trim() == "fsr" || f[5].Trim() == "ninguno" || f[5].Trim() == "xess") p.ScalerPick = f[5].Trim();   // (1.2.01: FSR 1 / sin reescalado; 23-11 DLSSSR: XeSS)
                    p.Cap = f.Length > 6 && f[6].Trim() == "30";
                    p.Mode120 = f.Length > 6 && f[6].Trim() == "120"; // 20-11 (HZ120)
                    p.Fg = !(f.Length > 7 && f[7].Trim() == "0");
                    // 15-11: nombre confirmado (columna 9). Los de antes, sin ella, no lo estan: se vuelven a leer en el HOME y se
                    // confirman o se completan ("Juego N", "Royal Editi"...)
                    p.NameSure = f.Length > 8 && f[8].Trim() == "1" && !GameNames.IsGeneric(p.Name);
                    if (f.Length > 9) p.ParsePick(f[9].Trim()); // (19-11, GENERACION: el tipo elegido con L3)
                    list.Add(p);
                }
                // Los iconos van aparte (games\game_icons.txt: nombre|180 numeros; 15-11: una linea por icono, puede haber varias)
                if (File.Exists(IconPath))
                    foreach (string raw in File.ReadAllLines(IconPath, Encoding.UTF8))
                    {
                        if (raw.StartsWith("#")) continue;
                        int bar = raw.LastIndexOf('|');
                        if (bar <= 0) continue;
                        string n = raw.Substring(0, bar).Trim();
                        foreach (GameProfile p in list)
                            if (p.Name == n) { p.AddIcon(HomeLabel.ParseNums(raw.Substring(bar + 1), 180)); break; }
                    }
            }
            catch (Exception ex) { Log.Error("Leyendo " + FilePath, ex); }
        }

        static string IconPath { get { return Path.Combine(Dir, "game_icons.txt"); } }

        // El juego cuyo icono se parece a este (el mas parecido, por debajo de HomeLabel.SameIcon), o null
        public static GameProfile FindByIcon(double[] icon)
        {
            if (icon == null) return null;
            lock (sync)
            {
                Load();
                GameProfile best = null;
                double bd = HomeLabel.SameIcon;
                foreach (GameProfile p in list)
                {
                    double d = p.IconDiff(icon);
                    if (d < bd) { bd = d; best = p; }
                }
                return best;
            }
        }

        // Nombre para un juego cuyo nombre no se ha podido leer: "Juego 1", "Juego 2"...
        public static string NewName()
        {
            lock (sync)
            {
                Load();
                for (int i = 1; ; i++)
                {
                    string n = "Juego " + i;
                    bool used = false;
                    foreach (GameProfile p in list) if (p.Name == n) { used = true; break; }
                    if (!used) return n;
                }
            }
        }

        static void SaveAll()
        {
            try
            {
                StringBuilder sb = new StringBuilder("# Ajustes recordados por juego (los pone el mando): nombre|captura|nivel|pasadas|dlss5|escalador|modo|generacion|nombre_seguro|tipo_generacion_60,30\r\n");
                foreach (GameProfile p in list)
                {
                    sb.Append(p.Name.Replace("|", " ")).Append('|').Append(p.Capture).Append('|').Append(p.Level).Append('|')
                      .Append(p.Passes).Append('|').Append(p.Dlss5 ? 1 : 0).Append('|').Append(p.ScalerPick.Length > 0 ? p.ScalerPick : p.Scaler ? "1" : "0").Append('|')
                      .Append(p.Mode120 ? 120 : p.Cap ? 30 : 60).Append('|').Append(p.Fg ? 1 : 0).Append('|').Append(p.NameSure ? 1 : 0);
                    if (p.PickText.Length > 0) sb.Append('|').Append(p.PickText); // (19-11, GENERACION: solo si se ha elegido con L3)
                    sb.Append("\r\n");
                }
                WriteAtomic(FilePath, sb.ToString());
                StringBuilder ib = new StringBuilder("# Iconos de cada juego en el HOME (los guarda la app al entrar en el; puede haber varios por juego): nombre|firma\r\n");
                foreach (GameProfile p in list)
                    foreach (double[] ic in p.Icons) ib.Append(p.Name.Replace("|", " ")).Append('|').Append(HomeLabel.NumsText(ic)).Append("\r\n");
                WriteAtomic(IconPath, ib.ToString());
            }
            catch (Exception ex) { Log.Error("Guardando " + FilePath, ex); }
        }

        // 15-11: el fichero entero o nada. File.WriteAllText trunca y luego escribe: un apagon o un cuelgue a mitad dejaba
        // profiles.txt vacio o cortado (todos los perfiles perdidos). Ahora a un .tmp en la misma carpeta, a disco de verdad
        // (Flush(true)) y luego se cambia por el bueno de una vez (File.Replace)
        public static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            byte[] data = new UTF8Encoding(false).GetBytes(text);
            using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, null); }
            catch (Exception)
            {
                // (otro programa con el fichero abierto: copia normal, que por lo menos no deja el .tmp a medias)
                File.Copy(tmp, path, true);
                try { File.Delete(tmp); } catch { }
            }
        }

        // Copia de profiles.txt e game_icons.txt (.bak) antes del primer cambio de nombre o union de perfiles de cada sesion
        static bool backedUp;
        static void Backup()
        {
            if (backedUp) return;
            backedUp = true;
            try
            {
                if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
                if (File.Exists(IconPath)) File.Copy(IconPath, IconPath + ".bak", true);
            }
            catch (Exception ex) { Log.Error("Copiando " + FilePath, ex); }
        }

        // Mismo juego: un nombre contiene al otro, o se solapan letra a letra (los nombres largos se desplazan y el OCR
        // lee ventanas distintas del mismo nombre)
        public static bool SameGame(string a, string b)
        {
            // 20-11 (PS5JUEGO): los juegos de las consolas con menu propio se guardan como "PS5: <juego>" / "Xbox Series: <juego>". Dos
            // nombres con distinta etiqueta de consola (o uno con y otro sin) NUNCA son el mismo juego, aunque el resto se solape ("PS5:
            // Rocket League" no es el "Rocket League" de la Switch 2). Asi no se mezclan juegos de consolas distintas
            if (JuegoConsola.TagConsola(a) != JuegoConsola.TagConsola(b)) return false;
            // 21-11 (el usuario: "siguen sin guardarse los perfiles"): en las consolas con menu propio el OCR lee el mismo juego con
            // erratas distintas cada vez ("Bfoodborne" / "Bloodborne"): cada una era un perfil nuevo. Ahora es el mismo (solo con etiqueta:
            // la Switch 2 sigue igual)
            if (JuegoConsola.TagConsola(a) != null && JuegoConsola.Parecido(a, b)) return true;
            string x = HomeLabel.Norm(a), y = HomeLabel.Norm(b);
            if (x.Length == 0 || y.Length == 0) return false;
            if (x == y) return true;
            if (Math.Min(x.Length, y.Length) >= 5 && (x.Contains(y) || y.Contains(x)))
            {
                // "Zelda" y "Zelda 2" son juegos distintos: lo que sobra tras el nombre corto es solo su numero
                string shortN = x.Length < y.Length ? x : y, longN = x.Length < y.Length ? y : x;
                return !(longN.StartsWith(shortN, StringComparison.Ordinal) && IsSequelNumber(longN.Substring(shortN.Length)));
            }
            // Dos ventanas del mismo nombre coinciden en todo lo que se solapan (salvo erratas del OCR). Compartir solo un
            // trozo no basta: "The Legend of Zelda: Ocarina of Time" y "...: Tears of the Kingdom" empiezan igual (05-11)
            for (int shift = 10 - y.Length; shift <= x.Length - 10; shift++)
            {
                int ox = Math.Max(shift, 0), oy = Math.Max(-shift, 0), n = Math.Min(x.Length - ox, y.Length - oy);
                int allowed = n / 16, wrong = 0;
                for (int i = 0; i < n && wrong <= allowed; i++)
                {
                    char cx = x[ox + i], cy = y[oy + i];
                    if (cx == cy) continue;
                    wrong++;
                    if (char.IsDigit(cx) || char.IsDigit(cy)) wrong = allowed + 1; // otro numero: otro juego
                }
                if (wrong <= allowed) return true;
            }
            return false;
        }

        static bool IsSequelNumber(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s) if (!char.IsDigit(c) && c != 'i' && c != 'v' && c != 'x') return false;
            return true;
        }

        // Perfil por nombre: el mismo nombre primero (tal cual, luego sin mayusculas, acentos ni signos); si no, el mismo juego
        // (otra ventana del nombre, SameGame). 15-11: ya no le cambia el nombre por uno mas largo de una sola lectura (asi podia
        // entrar basura del OCR: "HITMAN World of Assassination kAàiuy" el 12-11): eso lo decide Improve, con lecturas votadas
        // 21-11 (quinta vuelta, PS5 por icono): otro nombre para un perfil (el "Juego N" o el leido con basura, ya con un nombre bien
        // leido). Si ya hay otro perfil con ese nombre exacto no se toca (devuelve false)
        public static bool ExisteNombre(string nombre)
        {
            lock (sync) { Load(); foreach (GameProfile q in list) if (q.Name == nombre) return true; return false; }
        }

        public static bool Renombra(string viejo, string nuevo)
        {
            if (string.IsNullOrEmpty(viejo) || string.IsNullOrEmpty(nuevo) || viejo == nuevo) return false;
            lock (sync)
            {
                Load();
                GameProfile p = null;
                foreach (GameProfile q in list) { if (q.Name == nuevo) return false; if (q.Name == viejo) p = q; }
                if (p == null) return false;
                Backup();
                p.Name = nuevo;
                SaveAll();
                return true;
            }
        }

        public static GameProfile Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            lock (sync)
            {
                Load();
                foreach (GameProfile p in list) if (p.Name == name) return p;
                string k = GameNames.Key(name);
                foreach (GameProfile p in list) if (GameNames.Key(p.Name) == k) return p;
                foreach (GameProfile p in list) if (SameGame(p.Name, name)) return p;
                return null;
            }
        }

        // 15-11: el nombre leido ahora en el HOME (name, seguro o no) para el perfil p (reconocido por su icono, o en el que se
        // entra). Si es mejor que el guardado (GameNames.Better: un "Juego N", un nombre cortado...) se le pone; si con el nombre
        // nuevo resulta ser el mismo juego que otro perfil guardado aparte (GameNames.SameTitle: un "Juego N" de un juego que
        // tambien tenia perfil con su nombre), se juntan en p, el del icono de ahora: se queda con sus ajustes y con los iconos
        // de los dos (los del otro van al log). Devuelve los nombres que ya no existen (el de antes de p y los juntados) o null
        public static List<string> Improve(GameProfile p, string name, bool sure, out string mergedInfo)
        {
            return Improve(p, name, sure, true, out mergedInfo);
        }

        // 16-11: firm = el nombre leido es firme (NameReads.Firm); si no, no cambia el nombre de nadie (GameNames.Better)
        public static List<string> Improve(GameProfile p, string name, bool sure, bool firm, out string mergedInfo)
        {
            mergedInfo = null;
            if (p == null || string.IsNullOrEmpty(name)) return null;
            lock (sync)
            {
                Load();
                if (!list.Contains(p)) return null;
                string old = p.Name;
                string kOld = GameNames.Key(old), kNew = GameNames.Key(name);
                if (sure && !p.NameSure && kOld.Length > 0 && kOld == kNew)
                {
                    // El mismo nombre, ahora confirmado
                    p.NameSure = true;
                    if (old != name) { Backup(); p.Name = name; }
                    SaveAll();
                    return old != name ? new List<string> { old } : null;
                }
                string better = GameNames.Better(old, p.NameSure, name, sure, firm);
                if (better == null || better == old) return null;
                Backup();
                List<string> gone = new List<string> { old };
                p.Name = better;
                p.NameSure = sure;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    GameProfile q = list[i];
                    if (q == p || !GameNames.SameTitle(q.Name, q.NameSure, p.Name, p.NameSure)) continue;
                    foreach (double[] ic in q.Icons) p.AddIcon(ic);
                    mergedInfo = (mergedInfo == null ? "" : mergedInfo + "; ") + "\"" + q.Name + "\" (" + q.Summary() + ")";
                    gone.Add(q.Name);
                    list.RemoveAt(i);
                }
                SaveAll();
                return gone;
            }
        }

        // ¿Sigue existiendo este perfil? (otro lo ha podido absorber en Improve)
        public static bool Exists(GameProfile p)
        {
            lock (sync) { Load(); return list.Contains(p); }
        }

        // ---- 15-11: prueba sin consola (--test-perfiles=<carpeta con una COPIA de profiles.txt e game_icons.txt>) ----
        // Carga del formato de antes (8 columnas), iconos, un "Juego N" que pasa a su nombre, un perfil cortado que se junta con un
        // "Juego N" del mismo juego, y la escritura (entera, 9 columnas, sin .tmp, con .bak) vuelta a cargar del disco
        public static string SelfTest(string dir, out int failures)
        {
            StringBuilder sb = new StringBuilder();
            int fails = 0;
            Action<string, bool> check = delegate (string what, bool ok)
            {
                sb.Append(ok ? "OK    " : "FALLO ").Append(what).Append("\r\n");
                if (!ok) fails++;
            };
            List<GameProfile> all;
            lock (sync) { Dir = dir; list = null; backedUp = false; Load(); all = new List<GameProfile>(list); }
            try
            {
                int withIcon = 0, sureN = 0, generic = 0;
                foreach (GameProfile p in all) { if (p.Icon != null) withIcon++; if (p.NameSure) sureN++; if (GameNames.IsGeneric(p.Name)) generic++; }
                check("Cargados " + all.Count + " perfiles (" + withIcon + " con icono, " + generic + " \"Juego N\", " + sureN + " con el nombre seguro)", all.Count > 0);
                bool iconsOk = true;
                foreach (GameProfile p in all)
                {
                    GameProfile f = p.Icon != null ? FindByIcon(p.Icon) : p;
                    if (f != p) { iconsOk = false; sb.Append("      el icono de \"").Append(p.Name).Append("\" da \"").Append(f != null ? f.Name : "nada").Append("\"\r\n"); }
                }
                check("Cada icono reconoce su perfil", iconsOk);
                GameProfile g1 = null, g2 = null, cut = null;
                foreach (GameProfile p in all)
                {
                    if (GameNames.IsGeneric(p.Name) && p.Icon != null) { if (g1 == null) g1 = p; else if (g2 == null) g2 = p; }
                    if (p.Name.StartsWith("Kingdom Come", StringComparison.Ordinal)) cut = p;
                }
                string merged;
                if (g1 != null)
                {
                    string was = g1.Name;
                    List<string> gone = Improve(g1, "Hollow Knight - Nintendo Switch 2 Edition", true, out merged);
                    check("\"" + was + "\" -> \"" + g1.Name + "\", seguro, sin juntar nada", gone != null && g1.NameSure && merged == null &&
                          g1.Name == "Hollow Knight - Nintendo Switch 2 Edition");
                    check("Un nombre seguro no cambia por otro distinto", Improve(g1, "Pokémon Champions", true, out merged) == null && g1.Name == "Hollow Knight - Nintendo Switch 2 Edition");
                }
                if (g2 != null && cut != null)
                {
                    string was = g2.Name, cutName = cut.Name;
                    double[] cutIcon = cut.Icon;
                    List<string> gone = Improve(g2, "Kingdom Come: Deliverance - Royal Edition", true, out merged);
                    check("\"" + was + "\" -> \"" + g2.Name + "\" y juntado con \"" + cutName + "\" (" + merged + ")",
                          gone != null && gone.Contains(cutName) && !Exists(cut) && Exists(g2));
                    check("El icono del juntado reconoce el perfil que queda", cutIcon == null || FindByIcon(cutIcon) == g2);
                    check("Find por el nombre cortado de antes da el que queda", Find(cutName) == g2);
                }
                lock (sync) { list = null; Load(); all = new List<GameProfile>(list); }
                GameProfile again = Find("Hollow Knight - Nintendo Switch 2 Edition");
                check("Vuelto a cargar del disco: el nombre nuevo, seguro y con su icono", g1 == null || (again != null && again.NameSure && again.Icon != null));
                check("Sin .tmp a medias", !File.Exists(FilePath + ".tmp") && !File.Exists(IconPath + ".tmp"));
                string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                // (22-11, 1.2.2, ERRORES: desde el 19-11 (GENERACION) una 10ª columna, el tipo de generacion elegido con L3, si se eligio;
                // y la .bak solo se hace antes de cambiar un nombre o juntar perfiles: con perfiles sin "Juego N" no hay cambio que copiar.
                // Con los del usuario daba 2 fallos sin que nada fuera mal)
                int cols = lines.Length > 1 ? lines[1].Split('|').Length : 0;
                check("9 columnas, o 10 con el tipo de generacion elegido (" + (lines.Length > 1 ? lines[1] : "") + ")", cols == 9 || cols == 10);
                bool cambiado = g1 != null || (g2 != null && cut != null);
                check("Copia .bak hecha antes del primer cambio" + (cambiado ? "" : " (ningun nombre cambiado en esta copia: no hace falta)"),
                      !cambiado || File.Exists(FilePath + ".bak"));
            }
            finally { lock (sync) { Dir = Path.Combine(Paths.Datos, "games"); list = null; backedUp = false; } }
            failures = fails;
            sb.Append(fails == 0 ? "Todo bien" : fails + " fallos").Append("\r\n");
            return sb.ToString();
        }

        public static void Save(GameProfile p)
        {
            lock (sync)
            {
                Load();
                if (!list.Contains(p)) list.Add(p);
                SaveAll();
            }
        }

        // 15-11: otro icono para p (reconocido por su nombre con un icono que no tenia), y guardado
        public static void AddIcon(GameProfile p, double[] icon)
        {
            lock (sync)
            {
                Load();
                p.AddIcon(icon);
                if (!list.Contains(p)) list.Add(p);
                SaveAll();
            }
        }

        // 15-11: los ajustes de from en p, y guardado, todo con el cerrojo de la lista (Improve puede estar escribiendo a la vez)
        public static void SaveSettings(GameProfile p, GameProfile from)
        {
            lock (sync)
            {
                Load();
                p.Capture = from.Capture; p.Level = from.Level; p.Passes = from.Passes; p.Dlss5 = from.Dlss5;
                p.Scaler = from.Scaler; p.ScalerPick = from.ScalerPick; p.Cap = from.Cap; p.Mode120 = from.Mode120; p.Fg = from.Fg; p.Pick60 = from.Pick60; p.Pick30 = from.Pick30;
                if (!list.Contains(p)) list.Add(p);
                SaveAll();
            }
        }

    }

    // ===================================================================
    //  Telon con aspecto de pantalla de carga para los cambios en plena sesion
    //  (reinicio de LS, relanzar mpv). Sin activar (por
    //  defecto) no le quita el primer plano a mpv, asi LS sigue escalando debajo.
    //  El fondo se pinta una vez; despues solo se repinta la barra (a 4K, GDI+
    //  pintando la pantalla entera 30 veces por segundo ocupa un nucleo).
    //  15-11 (Prisma): con texto, una pastilla centrada como los avisos (PrismaUi) y una linea de progreso fina debajo; en los
    //  cambios con el mando, sobre la ultima imagen atenuada (ComposeDimmed) en vez del fondo oscuro
    //  15-11 (telon_vivo): en vivo, una miniatura de DWM de mpv en vez de imagen propia (la otra parte: src\TelonVivo.cs)
    // ===================================================================

    partial class CoverForm : Form
    {
        static readonly Color BgTop = Color.FromArgb(12, 14, 20);
        static readonly Color BgBottom = Color.FromArgb(3, 4, 7);
        const string DimTag = "prisma-atenuada"; // (Image.Tag del fondo ya compuesto por ComposeDimmed)

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

        readonly bool activating;
        readonly string title, subtitle;
        readonly Color accent;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
        readonly DateTime start = DateTime.Now;
        bool fading;
        DateTime fadeStart;
        Bitmap bg;
        RectangleF bar;
        // 10-11: con una imagen congelada (la pantalla tal cual) en vez de negro; al quitarse se funde con lo que haya debajo
        Bitmap frozen;
        readonly double fadeInSecs;
        double fadeOutSecs = 0.25;
        // Con imagen, como mucho 254/255: la ventana sigue siendo "layered". Al llegar a 1 WinForms le quita ese estilo y Windows
        // la vuelve a pintar (con negro liso no se nota; con una imagen podria salir un instante en negro)
        readonly double top;

        // Sin imagen de mpv en directo debajo: una miniatura de DWM de su ventana hace que LS deje de
        // escalarla (medido el 28/10: "Scaling status 1" sobre la ventana de mpv al registrarla)
        // 15-11 (telon_vivo): live = la ventana de mpv para el telon en vivo (src\TelonVivo.cs); Zero = el de siempre
        public CoverForm(string title, string subtitle, Color accent, bool activating, Bitmap frozen = null, double fadeInSecs = 0.12,
                         IntPtr live = default(IntPtr))
        {
            this.title = title;
            this.subtitle = subtitle;
            this.accent = accent;
            this.activating = activating;
            this.frozen = frozen;
            this.fadeInSecs = fadeInSecs;
            liveSrc = live;
            top = frozen != null || live != IntPtr.Zero ? 254.0 / 255.0 : 1.0; // (en vivo tambien: la miniatura va en la "layered")
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            BackColor = title == null || live != IntPtr.Zero ? Color.Black : BgBottom;
            TopMost = true;
            ShowInTaskbar = false;
            Text = PrismaUi.AppName;
            if (PrismaUi.FormIcon != null) Icon = PrismaUi.FormIcon;
            Opacity = 0;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            anim.Interval = 33;
            anim.Tick += delegate { Step(); };
            Cursor = BlankCursor; // sin flecha del raton encima del telon
            anim.Start();
        }

        // Cursor transparente (1x1): sobre el telon no se ve la flecha del raton
        static Cursor blank;
        static Cursor BlankCursor
        {
            get
            {
                if (blank == null)
                    using (Bitmap b = new Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        b.SetPixel(0, 0, Color.Transparent);
                        blank = new Cursor(b.GetHicon());
                    }
                return blank;
            }
        }
        protected override bool ShowWithoutActivation { get { return !activating; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (!activating) cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        // Fundido y cierre (se libera solo). secs: lo que dura el fundido (0,25 s el de siempre)
        public void FadeOut(double secs = 0.25)
        {
            if (fading) return;
            fadeOutSecs = Math.Max(0.01, secs);
            fading = true;
            fadeStart = DateTime.Now;
        }

        // 10-11: con la imagen congelada que es justo lo que se ve: pintada ya y a la vista de golpe (sin fundido de entrada).
        // Se llama tras Show(): se pinta con la ventana aun transparente y luego se le da la opacidad
        public void ShowNow()
        {
            Refresh();
            Opacity = top;
        }

        void Step()
        {
            if (fading)
            {
                double k = (DateTime.Now - fadeStart).TotalSeconds / fadeOutSecs;
                if (k >= 1) { anim.Stop(); Close(); return; } // Close libera el formulario
                Opacity = Math.Min(Opacity, top * (1 - k));
            }
            else if (Opacity < top) Opacity = fadeInSecs <= 0 ? top : Math.Min(top, top * (DateTime.Now - start).TotalSeconds / fadeInSecs);
            // Por encima de la ventana de escalado de LS (tambien siempre encima), sin activarse
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            LiveStep(); // (15-11, en vivo: sigue a mpv; la pastilla aparte, encima, con el fundido)
            if (bar.Width > 0) Invalidate(Rectangle.Inflate(Rectangle.Round(bar), 2, 2));
        }

        // ---- Prisma: la pastilla del telon (1,4 veces la de los avisos, centrada) y la linea de progreso debajo ----
        static float PillScale(int H) { return H / 1080f * 1.4f; }

        static RectangleF BarRect(int W, int H)
        {
            float u = H / 1080f, pillH = 62 * PillScale(H);
            float bw = 300 * u, bh = Math.Max(2f, (float)Math.Round(3 * u));
            return new RectangleF((float)Math.Round((W - bw) / 2), (float)Math.Round(H / 2f + pillH / 2 + 40 * u), (float)Math.Round(bw), bh);
        }

        static void DrawCenterPill(Graphics g, int W, int H, string title, string subtitle, Color accent)
        {
            string text = string.IsNullOrEmpty(subtitle) ? title : title + " · " + subtitle;
            float u = PillScale(H);
            PrismaUi.Pill p = PrismaUi.MeasurePill(g, text, u);
            if (p.W > W * 0.92f) p = PrismaUi.MeasurePill(g, text, u * W * 0.92f / p.W); // (texto muy largo: mas pequeña)
            PrismaUi.DrawPill(g, p, new RectangleF((W - p.W) / 2, (H - p.H) / 2, p.W, p.H), accent, 1f);
        }

        // Fondo del telon de los cambios con el mando: la ultima imagen a toda la pantalla, atenuada (queda al 45 %), con la
        // pastilla encima. Se compone en el hilo que pide el telon (a 4K ~0,1 s: fuera del de la interfaz, que lleva el gancho
        // del teclado); el telon lo libera
        public static Bitmap ComposeDimmed(Bitmap frame, Size screen, string title, string subtitle, Color accent)
        {
            int W = screen.Width, H = screen.Height;
            Bitmap b = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.Bilinear; // (atenuada y debajo de la pastilla: no hace falta mas)
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(frame, new Rectangle(0, 0, W, H), 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(140, 6, 7, 10))) g.FillRectangle(dim, 0, 0, W, H);
                DrawCenterPill(g, W, H, title, subtitle, accent);
            }
            b.Tag = DimTag;
            return b;
        }

        void BuildBackground(int W, int H)
        {
            bar = RectangleF.Empty;
            if (frozen != null && title != null && DimTag.Equals(frozen.Tag) && frozen.Width == W && frozen.Height == H)
            {
                // Ya compuesto (ComposeDimmed): la ultima imagen atenuada con la pastilla es el fondo tal cual (sin otra copia a 4K)
                bg = frozen;
                frozen = null;
                bar = BarRect(W, H);
                return;
            }
            bg = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            if (frozen != null)
            {
                // La imagen congelada a toda la pantalla (la copia de la pantalla ya es de su tamaño)
                using (Graphics g = Graphics.FromImage(bg))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    if (frozen.Width == W && frozen.Height == H) g.DrawImageUnscaled(frozen, 0, 0);
                    else
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(frozen, new Rectangle(0, 0, W, H), 0, 0, frozen.Width, frozen.Height, GraphicsUnit.Pixel);
                    }
                    if (title != null)
                    {
                        // (con texto y sin componer antes: atenuada y con la pastilla aqui)
                        g.CompositingMode = CompositingMode.SourceOver;
                        using (SolidBrush dim = new SolidBrush(Color.FromArgb(140, 6, 7, 10))) g.FillRectangle(dim, 0, 0, W, H);
                        DrawCenterPill(g, W, H, title, subtitle, accent);
                        bar = BarRect(W, H);
                    }
                }
                return;
            }
            if (title == null)
            {
                // Negro liso (como la carga de un juego): no se nota que hay un telon
                using (Graphics g = Graphics.FromImage(bg)) g.Clear(Color.Black);
                return;
            }
            using (Graphics g = Graphics.FromImage(bg))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, W, H), BgTop, BgBottom,
                                                                       LinearGradientMode.Vertical))
                    g.FillRectangle(b, 0, 0, W, H);
                // Luz de ambiente del color del modo
                using (GraphicsPath p = new GraphicsPath())
                {
                    RectangleF r = new RectangleF(W * 0.2f, H * 0.18f, W * 0.6f, H * 0.64f);
                    p.AddEllipse(r);
                    using (PathGradientBrush b = new PathGradientBrush(p))
                    {
                        b.CenterColor = Color.FromArgb(40, accent);
                        b.SurroundColors = new Color[] { Color.FromArgb(0, accent) };
                        g.FillPath(b, p);
                    }
                }
                DrawCenterPill(g, W, H, title, subtitle, accent);
                bar = BarRect(W, H);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (LivePaint(e)) return; // (15-11, en vivo: negro debajo de la miniatura de mpv)
            int W = ClientSize.Width, H = ClientSize.Height;
            if (W <= 0 || H <= 0) return;
            if (bg == null || bg.Width != W || bg.Height != H)
            {
                if (bg != null) bg.Dispose();
                BuildBackground(W, H);
            }
            Graphics g = e.Graphics;
            g.DrawImage(bg, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
            if (bar.Width <= 0) return;
            // Linea indeterminada: un tramo de luz que recorre la pista
            PrismaUi.DrawProgressLine(g, bar, (DateTime.Now - start).TotalSeconds, accent, 1f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                anim.Dispose();
                DropLive(); // (15-11: la miniatura de mpv y la pastilla del telon en vivo)
                if (bg != null) bg.Dispose();
                if (frozen != null) { frozen.Dispose(); frozen = null; }
            }
            base.Dispose(disposing);
        }
    }

    // ===================================================================
    //  Pantalla de carga: tapa el arranque de mpv / Lossless Scaling
    // ===================================================================

    class SplashForm : Form
    {
        // Colores de Prisma (cian y rojo, los de sus rayos) y del fondo
        static readonly Color Neon = Color.FromArgb(0, 200, 235);
        static readonly Color Red = Color.FromArgb(255, 60, 85);
        static readonly Color BgTop = Color.FromArgb(12, 14, 20);
        static readonly Color BgBottom = Color.FromArgb(3, 4, 7);

        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
        DateTime start = DateTime.Now;
        // Aviso (solo "Enciende la consola"); null = sin texto
        string status, shownStatus;
        DateTime statusChanged = DateTime.Now;
        string hint; // texto pequeño bajo el aviso (null = nada)
        Bitmap background; // fondo con las luces de ambiente (se pinta una vez: a 4K es caro)

        // Progreso: 'target' lo marca cada paso del arranque; la barra avanza sola un
        // poco dentro de cada paso (para no parecer parada) y se mueve con suavidad
        double target = 0.04, shown;
        DateTime targetSet = DateTime.Now;

        // Final: el prisma destella y luego todo se funde
        bool finishing, closing;
        DateTime finishStart, closeStart;

        // Partículas de luz que suben despacio (posición relativa 0..1)
        struct Mote { public float X, Y, R, Speed, Phase; public bool Cyan; }
        readonly Mote[] motes = new Mote[46];

        // Esc en la pantalla de carga
        public event EventHandler CancelRequested;

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            BackColor = BgBottom;
            TopMost = true;
            ShowInTaskbar = false;
            Text = PrismaUi.AppName;
            if (PrismaUi.FormIcon != null) Icon = PrismaUi.FormIcon;
            Opacity = 0;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Random rnd = new Random();
            for (int i = 0; i < motes.Length; i++)
            {
                motes[i].X = (float)rnd.NextDouble();
                motes[i].Y = (float)rnd.NextDouble();
                motes[i].R = 1.2f + (float)rnd.NextDouble() * 2.8f;
                motes[i].Speed = 0.008f + (float)rnd.NextDouble() * 0.022f;
                motes[i].Phase = (float)(rnd.NextDouble() * Math.PI * 2);
                motes[i].Cyan = motes[i].X < 0.5f ? rnd.NextDouble() < 0.8 : rnd.NextDouble() < 0.2;
            }
            anim.Interval = 33; // 30 fps: a 4K con GDI+ mas no compensa (ocupa un nucleo)
            anim.Tick += delegate { Step(); };
            anim.Start();
            Cursor.Hide();
        }

        // Aviso bajo el prisma (cambia con un pequeño fundido); null lo quita
        public void SetStatus(string s)
        {
            if (s == status) return;
            shownStatus = status;
            status = s;
            statusChanged = DateTime.Now;
        }

        public void SetHint(string s) { hint = s; }

        // Paso del arranque: progreso (0..1) al que llega la barra
        public void SetProgress(double progress)
        {
            if (progress > target) { target = progress; targetSet = DateTime.Now; }
        }

        // Solo para las vistas previas (--ui-preview): la pantalla tal como se ve a los 'secs' segundos, ya asentada
        public void PreviewAt(double secs, double progress)
        {
            start = DateTime.Now.AddSeconds(-secs);
            statusChanged = start;
            target = shown = progress;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            // Se encola: el manejador cierra este mismo formulario
            if (e.KeyCode == Keys.Escape && !closing && CancelRequested != null)
                BeginInvoke((MethodInvoker)delegate { CancelRequested(this, EventArgs.Empty); });
        }

        // Cierre: barra al 100 %, la pantalla se ilumina y después fundido
        public void FadeOutAndClose()
        {
            if (finishing) return;
            finishing = true;
            finishStart = DateTime.Now;
            target = 1;
            SetStatus(null);
            hint = null;
        }

        void Step()
        {
            double t = (DateTime.Now - start).TotalSeconds;
            if (finishing && !closing && (DateTime.Now - finishStart).TotalSeconds >= 0.55)
            {
                closing = true;
                closeStart = DateTime.Now;
            }
            if (closing)
            {
                double k = (DateTime.Now - closeStart).TotalSeconds / 0.4;
                if (k >= 1) { anim.Stop(); Close(); return; }
                Opacity = 1 - k;
            }
            else if (Opacity < 1) Opacity = Math.Min(1, t / 0.35);

            double creep = finishing ? 0 : 0.10 * (1 - Math.Exp(-(DateTime.Now - targetSet).TotalSeconds / 5));
            double goal = Math.Min(finishing ? 1 : 0.97, target + creep);
            shown += (goal - shown) * (finishing ? 0.4 : 0.14);
            Invalidate();
        }

        static double Ease(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Mancha de luz radial (centro con 'alpha', borde transparente)
        static void Glow(Graphics g, RectangleF r, Color c, int alpha)
        {
            if (alpha <= 0 || r.Width < 1 || r.Height < 1) return;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterColor = Color.FromArgb(Math.Min(255, alpha), c);
                    b.SurroundColors = new Color[] { Color.FromArgb(0, c) };
                    g.FillPath(b, p);
                }
            }
        }

        // (18-11: fuera DrawJoyCon, los Joy-Con de la pantalla de carga de antes; la escena nueva esta en src\CargaConsola.cs)

        // Fondo fijo: degradado + luces de ambiente cian (izquierda) y roja (derecha) + viñeta
        void BuildBackground(int W, int H)
        {
            if (background != null) background.Dispose();
            background = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(background))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, W, H), BgTop, BgBottom, 90f))
                    g.FillRectangle(b, 0, 0, W, H);
                Glow(g, new RectangleF(-W * 0.35f, -H * 0.2f, W * 0.9f, H * 1.3f), Neon, 34);
                Glow(g, new RectangleF(W * 0.45f, -H * 0.1f, W * 0.9f, H * 1.3f), Red, 30);
                // Viñeta: bordes más oscuros
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddEllipse(-W * 0.25f, -H * 0.3f, W * 1.5f, H * 1.6f);
                    using (PathGradientBrush pb = new PathGradientBrush(p))
                    {
                        pb.CenterColor = Color.FromArgb(0, 0, 0, 0);
                        pb.SurroundColors = new Color[] { Color.FromArgb(200, 0, 0, 0) };
                        pb.FocusScales = new PointF(0.55f, 0.55f);
                        g.FillRectangle(pb, 0, 0, W, H);
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int W = ClientSize.Width, H = ClientSize.Height;
            if (W <= 0 || H <= 0) return;
            double t = (DateTime.Now - start).TotalSeconds;
            float u = H / 1080f; // escala respecto a 1080p
            float cy = H * 0.44f;

            if (background == null || background.Width != W || background.Height != H) BuildBackground(W, H);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(background, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float pulse = (float)(0.5 + 0.5 * Math.Sin(t * 2.0));
            // Destello al terminar: el prisma se enciende
            float lit = finishing ? (float)Ease((DateTime.Now - finishStart).TotalSeconds / 0.3) : 0f;

            // Partículas: suben despacio, parpadean y aparecen poco a poco
            float motesIn = (float)Ease(t / 1.2);
            foreach (Mote m in motes)
            {
                float y = (float)((m.Y - t * m.Speed) % 1.0);
                if (y < 0) y += 1;
                float x = m.X + 0.012f * (float)Math.Sin(t * 0.6 + m.Phase);
                float tw = 0.5f + 0.5f * (float)Math.Sin(t * 1.7 + m.Phase * 3);
                // Más tenues arriba y abajo
                float edge = (float)Math.Sin(y * Math.PI);
                int a = (int)(motesIn * edge * (25 + 70 * tw));
                if (a <= 2) continue;
                // Halo + núcleo con círculos lisos (un degradado radial por partícula
                // cuesta demasiado a 4K)
                float r = m.R * u;
                Color c = m.Cyan ? Neon : Red;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(a / 6, c)))
                    g.FillEllipse(b, x * W - r * 2, y * H - r * 2, r * 4, r * 4);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(a, Color.FromArgb(
                           (c.R + 255) / 2, (c.G + 255) / 2, (c.B + 255) / 2))))
                    g.FillEllipse(b, x * W - r / 2, y * H - r / 2, r, r);
            }

            // 18-11 (CONSOLAS): la escena sin ninguna consola (antes la Switch 2 con los Joy-Con): el prisma en grande, el haz que entra
            // y sus dos rayos (src\CargaConsola.cs)
            CargaConsola.Escena(g, W, H, W / 2f, cy, u, t, pulse, lit);

            // Barra de progreso fina y luminosa (cian -> rojo), sin texto
            float bw = 360 * u, bh = 3 * u, bx = W / 2f - bw / 2, by = CargaConsola.BarraY(cy, u);
            int barAlpha = (int)(255 * Ease((t - 0.6) / 0.5));
            using (GraphicsPath track = RoundRect(new RectangleF(bx, by, bw, bh), bh / 2))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(barAlpha / 10, 255, 255, 255)))
                g.FillPath(tb, track);
            float fillW = (float)(Math.Max(0, Math.Min(1, shown)) * bw);
            if (fillW > bh && barAlpha > 0)
            {
                RectangleF fill = new RectangleF(bx, by, fillW, bh);
                Glow(g, new RectangleF(bx - 10 * u, by - 9 * u, fillW + 20 * u, bh + 18 * u),
                     Color.FromArgb(120, 140, 220), barAlpha / 5);
                using (GraphicsPath fp = RoundRect(fill, bh / 2))
                using (LinearGradientBrush fb = new LinearGradientBrush(
                           new RectangleF(bx - 1, by, bw + 2, bh),
                           Color.FromArgb(barAlpha, Neon), Color.FromArgb(barAlpha, Red), 0f))
                    g.FillPath(fb, fp);
                // Punto luminoso en el extremo
                float dx = bx + fillW;
                Glow(g, new RectangleF(dx - 14 * u, by + bh / 2 - 14 * u, 28 * u, 28 * u),
                     Color.White, (int)(barAlpha * 0.6));
            }

            // Aviso ("Enciende la consola · Pulsa Esc para cancelar"): una pastilla como los avisos (punto rojo: espera algo del
            // usuario), con fundido al aparecer / cambiar y un poco de desplazamiento al entrar
            double sk = (DateTime.Now - statusChanged).TotalSeconds / 0.3;
            string txt = sk < 0.5 ? shownStatus : status;
            if (txt != null)
            {
                float alpha = sk >= 1 ? 1f : (float)Math.Abs(1 - sk * 2);
                string line = hint != null && txt == status ? txt + " · " + hint : txt;
                float pillSlide = txt == status && sk < 1 ? (float)((1 - Ease((sk - 0.5) * 2)) * 12 * u) : 0f;
                PrismaUi.DrawPillCentered(g, line, u * 1.1f, W / 2f, by + 72 * u + pillSlide, Red, alpha);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            anim.Stop();
            if (background != null) { background.Dispose(); background = null; }
            CargaConsola.Liberar(); // (18-11: el haz y los rayos guardados)
            Cursor.Show();
            base.OnFormClosed(e);
        }
    }

    // ------------------------------------------------------------------
    // Aplicacion de bandeja (15-11: el telon en vivo, en src\TelonVivo.cs)
    // ------------------------------------------------------------------
    partial class TrayApp : ApplicationContext
    {
        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        const byte VK_CONTROL = 0x11;
        const byte VK_S = 0x53;
        const byte VK_MENU = 0x12; // Alt
        const uint KEYEVENTF_KEYUP = 0x0002;

        readonly Config cfg;
        readonly NotifyIcon tray;
        readonly Control ui; // control oculto para volver al hilo de la interfaz
        readonly ToolStripMenuItem detectItem;
        readonly object lsModeSync = new object(); // un cambio en Lossless Scaling a la vez
        volatile bool lsReady;        // LS ya escala esta ventana de mpv (fin del arranque)
        volatile Process mpv;
        volatile bool userExit;
        bool powerOffDetect;
        SplashForm splash;
        IntPtr focusProxy; // ventana invisible para devolver el primer plano a mpv (Rescale)
        int autoReopens;   // mpv reabierto tras cerrarse solo (corte de señal)
        volatile bool scClosing;
        volatile bool signalOn; // ya llega imagen (o sonido) de la consola

        public TrayApp(Config cfg)
        {
            this.cfg = cfg;
            powerOffDetect = cfg.CloseOnPowerOff;

            ui = new Control();
            ui.CreateControl();

            ContextMenuStrip menu = new ContextMenuStrip();
            // (1.2.4 ronda 6, QUITAFG: sin "Escalar ahora (atajo de Magpie)": sin Magpie)
            detectItem = new ToolStripMenuItem("", null, delegate { ToggleDetection(); });
            menu.Items.Add(detectItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Idioma.T("Abrir config"), null, delegate { OpenConfig(); });
            menu.Items.Add(Idioma.T("Asistente de configuración"), null, delegate { AbrirAsistente(); }); // (ConsoleEnhancer: Asistente.cs)
            // (1.2.06) Language / Idioma: se guarda y la app se vuelve a abrir en ese idioma
            ToolStripMenuItem idiomaMenu = new ToolStripMenuItem("Language / Idioma");
            foreach (string[] li in new[] { new[] { "en", "English" }, new[] { "es", "Español" } })
            {
                string lang = li[0];
                ToolStripMenuItem it = new ToolStripMenuItem(li[1], null, delegate
                {
                    if (Idioma.Ingles == (lang == "en")) return;
                    Idioma.Elegir(lang);
                    Log.Write("Bandeja: idioma=" + lang + " (se reabre la app)");
                    CeReabrir();
                });
                it.Checked = Idioma.Ingles == (lang == "en");
                idiomaMenu.DropDownItems.Add(it);
            }
            menu.Items.Add(idiomaMenu);
            MenuCe(menu); // (ConsoleEnhancer: iniciar con Windows y apagar el PC, src\InicioWindows.cs)
            VolverAEsperaSinConsola(); // (1.0.14: abierta desde la espera y sin consola -> otra vez en espera, src\Lanzador\Espera.cs)
            MenuAcercaDe(menu); // (ConsoleEnhancer, GITHUB: licencias y avisos legales, src\Lanzador\Componentes.cs)
            MenuExtras(menu); // (ConsoleEnhancer, PUBLICAR: componentes, anadir / quitar DLSS 5 y apoyar el proyecto, src\Lanzador\Extras.cs)
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem salir = new ToolStripMenuItem(Idioma.T("Salir"), null, delegate { QuitRequested(); });
            salir.ShortcutKeyDisplayString = "Alt+F4";   // (1.0.10: Alt+F4 sobre la imagen cierra la app, KeyHook)
            menu.Items.Add(salir);
            MenuJuego(menu); // (ConsoleEnhancer 1.0.10: lo del mando en la bandeja, arriba; src\Lanzador\MenuJuego.cs)
            UpdateDetectItem();
            PrismaUi.StyleMenu(menu); // 15-11 (Prisma): oscuro como los avisos, con el icono y el nombre arriba

            tray = new NotifyIcon();
            tray.Icon = LoadIcon(cfg.EnginePath);
            tray.Text = PrismaUi.AppName;
            tray.ContextMenuStrip = menu;
            tray.Visible = true;

            // Ventana invisible a la que pasar el primer plano un momento (ver Rescale)
            Form proxy = new Form();
            proxy.FormBorderStyle = FormBorderStyle.FixedToolWindow; // herramienta: ni barra de tareas ni Alt+Tab
            proxy.ShowInTaskbar = false;
            proxy.StartPosition = FormStartPosition.Manual;
            proxy.Bounds = new Rectangle(-32000, -32000, 1, 1);
            proxy.Opacity = 0.01;
            proxy.Show();
            focusProxy = proxy.Handle;
            // 17-11 (VELOCIDAD): WS_EX_NOACTIVATE: Windows ya no le da el primer plano al cerrarse otra ventana (el telon) en vez de a la
            // imagen. Rescale se lo sigue dando a proposito (SetForegroundWindow si vale con este estilo)
            try { ProxySetLong(focusProxy, -20, ProxyGetLong(focusProxy, -20) | 0x08000000); } catch { }

            splash = new SplashForm();
            splash.CancelRequested += delegate { QuitRequested(); };
            splash.Show();
            splash.Activate(); // asi este proceso puede pasar el primer plano a mpv luego

            Thread t = new Thread(Worker);
            t.IsBackground = true;
            t.Name = "CeWorker";
            t.Start();
        }

        // 15-11: el de Prisma (prisma.ico, dentro del exe) al tamaño de la bandeja; si no estuviera, el de mpv como antes
        // (23-11, 1.2.4 SINMPV: el de la app; si no, el del exe del motor -antes el de mpv-)
        static Icon LoadIcon(string exePath)
        {
            Icon own = PrismaUi.AppIcon(SystemInformation.SmallIconSize.Width);
            if (own != null) return own;
            try
            {
                if (File.Exists(exePath)) return Icon.ExtractAssociatedIcon(exePath);
            }
            catch (Exception ex) { Log.Error("No se pudo extraer el icono", ex); }
            return SystemIcons.Application;
        }

        // ---------------- Menu ----------------

        void UpdateDetectItem()
        {
            detectItem.Text = Idioma.T("Detección de apagado: " + (powerOffDetect ? "activada" : "desactivada"));
            detectItem.Checked = powerOffDetect;
        }

        void ToggleDetection()
        {
            powerOffDetect = !powerOffDetect;
            UpdateDetectItem();
            Log.Write("Deteccion de apagado " + (powerOffDetect ? "activada" : "desactivada") + " (solo esta sesion)");
        }

        // Pasa el primer plano a una ventana aunque este proceso no lo tenga: Windows solo
        // deja hacerlo tras una pulsacion de teclado, asi que se simula Alt
        static void ForceForeground(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            // (1.0.12: con la marca de la app, como el 0xE8 del GameSir: el bloqueo de Joyxoff no se las traga)
            keybd_event(VK_MENU, 0, 0, FillerMark);
            SetForegroundWindow(h);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, FillerMark);
        }

        // Telon (pantalla de carga) mientras se reinicia LS o se relanza mpv en plena
        // partida: sin el se veia la ventana de mpv pequeña en una esquina. Ademas, al
        // quedarse el primer plano y devolverlo luego a mpv, el AutoScale de LS salta solo.
        CoverForm curtain;
        static readonly Color NeonBlue = Color.FromArgb(0, 200, 235);

        // Con title, un aviso con texto (reinicio de la grafica); sin el, negro liso. 15-11 (Prisma): con texto, la pastilla
        // centrada; warn = punto rojo (algo ha ido mal); dimmed = el fondo ya compuesto por CoverForm.ComposeDimmed (la ultima
        // imagen atenuada con la pastilla, PadCurtainImage): pasa a ser del telon (si no se usa, se libera aqui)
        void ShowCurtain(string subtitle = "Cargando", string title = null, bool warn = false, Bitmap dimmed = null)
        {
            bool used = false;
            try
            {
                ui.Invoke((MethodInvoker)delegate
                {
                    if (splash != null) return; // en el arranque ya tapa la pantalla de carga
                    DropClosedCurtain("al ponerlo"); // (15-11: uno ya cerrado, otro nuevo)
                    if (curtain == null)
                    {
                        // negro liso: pasa por la carga del juego (con la imagen atenuada, un fundido algo mas largo: se oscurece)
                        Bitmap img = title != null ? dimmed : null;
                        int fade = img != null ? Math.Max(160, cfg.CurtainFadeInMs) : cfg.CurtainFadeInMs;
                        curtain = new CoverForm(title, title != null ? subtitle : null, warn ? PrismaUi.Warn : NeonBlue, true, img, fade / 1000.0);
                        used = img != null;
                        curtain.Show();
                        curtainSince = Stopwatch.StartNew();
                        curtainFadeMs = fade;
                        curtainFrozen = false;
                        curtainLive = false;
                        curtainKind = img != null ? "con la ultima imagen atenuada" : "en negro"; // (para el log al quitarlo)
                    }
                    ForceForeground(curtain.Handle);
                });
            }
            catch (Exception ex) { Log.Error("Mostrando el telon", ex); }
            if (!used && dimmed != null) dimmed.Dispose();
        }

        // 15-11 (Prisma): el fondo del telon de los cambios con el mando: la ultima imagen de mpv (o del motor) congelada y atenuada,
        // con la pastilla (CoverForm.ComposeDimmed). null si no se puede o no llega a tiempo (normalmente 0,2-0,3 s; tope 0,7):
        // entonces el telon oscuro con la pastilla, como antes. Nunca desde el hilo que lee la tuberia de mpv (la respuesta llega
        // por el) ni desde el de la interfaz (lleva el gancho del teclado). telon_imagen_congelada=0 lo quita
        Bitmap PadCurtainImage(string title, string subtitle)
        {
            if (title == null || !cfg.CurtainFrozen || !ipcUp || readerMpv != null || !ui.InvokeRequired || (curtain != null && !curtain.IsDisposed) || splash != null) return null;
            Process p = mpv;
            if (p == null || p.HasExited) return null;
            Stopwatch sw = Stopwatch.StartNew();
            Bitmap frame = GrabFrozenFromMpv(700, 300);
            if (frame == null)
            {
                Log.Write("Telon del mando: sin la ultima imagen (" + sw.ElapsedMilliseconds + " ms): el oscuro");
                return null;
            }
            try
            {
                long got = sw.ElapsedMilliseconds;
                Bitmap b = CoverForm.ComposeDimmed(frame, Screen.PrimaryScreen.Bounds.Size, title, subtitle, NeonBlue);
                Log.Write("Telon del mando con la ultima imagen atenuada (" + frame.Width + "x" + frame.Height + "): " + got +
                          " ms en tenerla, compuesta a los " + sw.ElapsedMilliseconds + " ms");
                return b;
            }
            catch (Exception ex) { Log.Error("Telon del mando con la ultima imagen", ex); return null; }
            finally { frame.Dispose(); }
        }

        // 10-11 (telon_imagen_congelada): el telon del paso al HOME a 4K es una imagen congelada de la pantalla. exact = es justo lo
        // que se ve (la copia de la pantalla): sale de golpe, sin fundido; si no (la imagen de mpv agrandada, sin el escalado ni
        // DLSS 5), con fundido de entrada; img null = negro con fundido. Revision 10-11: NO toma el primer plano (mpv lo conserva y
        // LS sigue escalando hasta que se le mata, con el telon ya opaco: ni un instante de la ventana de mpv pequeña en una
        // esquina) y en el HOME a 4K mpv sigue delante. La imagen pasa a ser del telon (la libera el). false = no se pudo
        volatile bool curtainFrozen;
        volatile int curtainFadeMs;     // fundido de entrada del telon puesto (WaitCurtainOpaque)
        bool ShowFrozenCurtain(Bitmap img, bool exact)
        {
            bool ok = false;
            int fade = exact && img != null ? 0 : Math.Max(150, cfg.CurtainFadeInMs);
            try
            {
                ui.Invoke((MethodInvoker)delegate
                {
                    DropClosedCurtain("al poner el congelado"); // (15-11: uno ya cerrado no impide el nuevo)
                    if (splash != null || curtain != null) return;
                    curtain = new CoverForm(null, null, NeonBlue, false, img, fade / 1000.0); // (false: sin activarse)
                    curtain.Show();
                    if (fade == 0) curtain.ShowNow();
                    curtainSince = Stopwatch.StartNew();
                    curtainFadeMs = fade;
                    curtainFrozen = img != null;
                    curtainLive = false;
                    curtainKind = "en negro";
                    ok = true;
                });
            }
            catch (Exception ex) { Log.Error("Mostrando el telon congelado", ex); }
            if (!ok && img != null) img.Dispose();
            return ok;
        }

        // 10-11: hasta que el telon tapa del todo (su fundido de entrada real), para que no se vea nada de lo que cambia debajo
        void WaitCurtainOpaque()
        {
            Stopwatch s = curtainSince;
            if (s == null) return;
            int need = curtainFadeMs + 45; // (+ un paso de su temporizador de 33 ms)
            while (s.ElapsedMilliseconds < need && curtainSince == s) Thread.Sleep(10);
        }

        // 15-11: el telon puede estar cerrado sin que HideCurtain lo sepa (19:26:34, recarga del mando: ObjectDisposedException
        // 'CoverForm' al darle el primer plano; liberado <0,8 s despues de ponerlo, sin saber por que). En el hilo de la interfaz: si
        // esta liberado se olvida (no se toca, el siguiente se crea nuevo y HideCurtain no lo da por quitado). true = estaba cerrado
        bool DropClosedCurtain(string where)
        {
            CoverForm c = curtain;
            if (c == null || !c.IsDisposed) return false;
            curtain = null;
            Log.Write("Aviso: el telon ya estaba cerrado (" + where + (curtainSince != null ? ", puesto hace " + curtainSince.ElapsedMilliseconds + " ms" : "") + "): se olvida");
            curtainSince = null;
            curtainFrozen = false;
            curtainLive = false;    // (lo mismo que deja HideCurtain)
            curtainKind = "en negro";
            return true;
        }

        // El primer plano un momento al telon (o a la pantalla de carga), para que despues vuelva a mpv
        void BounceForeground()
        {
            try
            {
                IntPtr h = IntPtr.Zero;
                ui.Invoke((MethodInvoker)delegate
                {
                    DropClosedCurtain("al darle el primer plano");
                    if (curtain != null) h = curtain.Handle;
                    else if (splash != null && !splash.IsDisposed) h = splash.Handle;
                });
                // (16-11, dlss5_sin_telon: sin telon, el motor que juega encima mientras se abre Magpie)

                if (h == IntPtr.Zero) return;
                ForceForeground(h);
                Thread.Sleep(50);
            }
            catch (Exception ex) { Log.Error("Cambiando el primer plano", ex); }
        }

        Stopwatch curtainSince; // desde que se puso el telon (para el log: lo que dura la pantalla en negro)

        // fadeMs: el fundido de salida (con la imagen congelada, telon_fundido_ms: se ve como un fundido entre las dos imagenes)
        void HideCurtain(int fadeMs = 250)
        {
            OnUi(delegate
            {
                if (DropClosedCurtain("al quitarlo")) return; // (15-11: ya cerrado: nada que fundir)
                if (curtain == null) return;
                CoverForm c = curtain;
                c.FadeOut(fadeMs / 1000.0);
                curtain = null;
                ImageFocusAfterCurtain(); // (17-11: con el motor, el primer plano ya a el; el telon sigue encima mientras se funde)
                // 15-11: una linea por telon con lo que se ha visto (en vivo, la imagen congelada, la ultima atenuada o negro) y lo que dura
                if (curtainSince != null)
                    Log.Write("Telon quitado: " + curtainSince.ElapsedMilliseconds + " ms " + CurtainKindText(c) +
                              (curtainFrozen || curtainLive ? " (y " + fadeMs + " ms de fundido)" : ""));
                curtainSince = null;
                curtainFrozen = false;
                curtainLive = false;
                curtainKind = "en negro";
            });
        }

        // ---- 10-11: imagen congelada para el telon (telon_imagen_congelada) ----
        [DllImport("user32.dll", EntryPoint = "GetDC")] static extern IntPtr GetScreenDc(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "ReleaseDC")] static extern int ReleaseScreenDc(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

        // La pantalla tal cual (lo que escala LS, con DLSS 5 y todo) por GDI (BitBlt con CAPTUREBLT: tambien las ventanas "layered";
        // Graphics.CopyFromScreen no admite esa combinacion). null si no se pudo o sale casi negra
        static Bitmap CaptureScreen()
        {
            Rectangle b = Screen.PrimaryScreen.Bounds;
            Bitmap bmp = null;
            try
            {
                bmp = new Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr dst = g.GetHdc(), scr = GetScreenDc(IntPtr.Zero);
                    try { BitBlt(dst, 0, 0, b.Width, b.Height, scr, b.X, b.Y, 0x00CC0020 | 0x40000000); } // SRCCOPY | CAPTUREBLT
                    finally { ReleaseScreenDc(IntPtr.Zero, scr); g.ReleaseHdc(dst); }
                }
                if (MostlyBlack(bmp)) { Log.Write("Telon congelado: la copia de la pantalla sale negra"); bmp.Dispose(); return null; }
                return bmp;
            }
            catch (Exception ex)
            {
                Log.Error("Copiando la pantalla para el telon", ex);
                if (bmp != null) bmp.Dispose();
                return null;
            }
        }

        // Casi todo negro (32x18 puntos de muestra): la copia no ha visto la imagen
        static bool MostlyBlack(Bitmap b)
        {
            System.Drawing.Imaging.BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                int lit = 0, n = 0;
                byte[] px = new byte[4];
                for (int y = 0; y < 18; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        int xx = (int)((x + 0.5) * b.Width / 32), yy = (int)((y + 0.5) * b.Height / 18);
                        Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)yy * d.Stride + xx * 4), px, 0, 4);
                        if (px[0] + px[1] + px[2] > 36) lit++;
                        n++;
                    }
                return lit * 50 < n; // menos de un 2 % con luz
            }
            finally { b.UnlockBits(d); }
        }

        static Bitmap ScaledToScreen(Bitmap src)
        {
            Rectangle b = Screen.PrimaryScreen.Bounds;
            if (src.Width == b.Width && src.Height == b.Height) return src;
            Bitmap dst = new Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, b.Width, b.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
            }
            src.Dispose();
            return dst;
        }

        // La imagen entera de mpv (la captura, sin el escalado de LS) por el script Lua, como las del HOME pero a otro fichero
        // (MpvReader no la analiza). null si no llega en maxMs. Cada peticion con su numero en el nombre del fichero: una respuesta
        // tardia de una peticion anterior no se toma por la de esta (revision 10-11)
        static readonly string FrozenPrefix = Path.Combine(Path.GetTempPath(), "switch2_frozen_");
        readonly ManualResetEvent frozenEvt = new ManualResetEvent(false);
        volatile string frozenReply;    // "w h stride formato" de la imagen congelada pedida (null: fallo)
        volatile string frozenPathNow;  // el fichero de la peticion en curso (null: ninguna)
        int frozenSeq;

        static bool IsFrozenPath(string p)
        {
            return p != null && p.StartsWith(FrozenPrefix, StringComparison.OrdinalIgnoreCase);
        }

        Bitmap GrabFrozenFromMpv(int maxMs, int busyWaitMs = 1500)
        {
            if (!ipcUp) return null;
            Stopwatch sw = Stopwatch.StartNew();
            while (grabBusy && sw.ElapsedMilliseconds < busyWaitMs) Thread.Sleep(20); // (el script atiende una peticion a la vez)
            grabBusy = true;
            lastGrab = DateTime.Now;
            string path = FrozenPrefix + Interlocked.Increment(ref frozenSeq).ToString(CultureInfo.InvariantCulture) + ".raw";
            frozenReply = null;
            frozenEvt.Reset();
            frozenPathNow = path;
            try
            {
                lock (cmdQueue) cmdQueue.Add(JsonCmd("script-message", "switch2-grab", path, "full"));
                if (!frozenEvt.WaitOne(maxMs) || frozenReply == null) return null;
                string[] f = frozenReply.Split(' ');
                int w = int.Parse(f[0], CultureInfo.InvariantCulture), h = int.Parse(f[1], CultureInfo.InvariantCulture);
                int st = int.Parse(f[2], CultureInfo.InvariantCulture);
                byte[] data = File.ReadAllBytes(path);
                if (data.Length < (long)st * h) return null;
                return HomeLabel.FromRaw(data, w, h, st, f.Length > 3 ? f[3] : "");
            }
            catch (Exception ex) { Log.Error("Imagen congelada de mpv", ex); return null; }
            finally
            {
                frozenPathNow = null;
                grabBusy = false;
                try { File.Delete(path); } catch { } // (15 MB: no se dejan en %TEMP%)
            }
        }

        // El telon congelado: la copia de la pantalla o, si sale negra, la imagen de mpv agrandada. Si no hay ninguna, negro.
        // true = puesto con imagen
        bool PutFrozenCurtain()
        {
            AjustesQuitarYa("al menu de la consola", true); // (1.1.2: que la copia no lleve el menu de ajustes)
            Stopwatch sw = Stopwatch.StartNew();
            bool exact = true;
            Bitmap img = CaptureScreen();
            long copyMs = sw.ElapsedMilliseconds;
            // La copia tiene que parecerse a la ultima imagen del HOME que vio la app (la de mpv, sin LS): si LS presentase en un plano
            // que GDI no ve, saldria la ventana de mpv pequeña en una esquina (o el escritorio) y eso no puede ser el telon
            double[] refThumb = lastHomeThumb;
            string check = "";
            if (img != null && refThumb != null && (DateTime.Now - lastHomeThumbAt).TotalSeconds < 6)
            {
                double d, l;
                // (reduccion rapida: la miniatura son medias de bloques de 20x20, no hace falta la de calidad de HomeLabel.Small)
                using (Bitmap s = new Bitmap(640, 360, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(s))
                    {
                        g.InterpolationMode = InterpolationMode.Bilinear;
                        g.DrawImage(img, 0, 0, 640, 360);
                    }
                    d = HomeLabel.Diff(HomeLabel.Thumb(s, out l), refThumb);
                }
                check = ", diferencia con la imagen de mpv " + d.ToString("0.0", CultureInfo.InvariantCulture) + "; copia " + copyMs + " ms";
                if (d > 18)
                {
                    Log.Write("Telon congelado: la copia de la pantalla no se parece a la imagen de mpv (diferencia " +
                              d.ToString("0.0", CultureInfo.InvariantCulture) + "): se usa la de mpv");
                    img.Dispose();
                    img = null;
                }
            }
            else if (img != null)
            {
                // Sin una imagen reciente del HOME con la que compararla no se usa a ciegas
                Log.Write("Telon congelado: sin imagen reciente de mpv para comprobar la copia de la pantalla: se usa la de mpv");
                img.Dispose();
                img = null;
            }
            if (img == null)
            {
                exact = false;
                img = GrabFrozenFromMpv(1500);
                if (img != null) img = ScaledToScreen(img);
            }
            long got = sw.ElapsedMilliseconds;
            if (img == null)
            {
                Log.Write("Telon congelado: sin imagen (" + got + " ms): negro con fundido (sin quitarle el primer plano a mpv)");
                ShowFrozenCurtain(null, false);
                return false;
            }
            if (cfg.TestSource.Length > 0) SaveCurtainThumb(img);
            if (!ShowFrozenCurtain(img, exact)) return false; // (ya habia un telon puesto: ese tapa)
            Log.Write("Telon con la imagen congelada (" + (exact ? "copia de la pantalla" + check : "imagen de mpv agrandada, con fundido de entrada") + "): " +
                      got + " ms en tenerla, puesta a los " + sw.ElapsedMilliseconds + " ms");
            return true;
        }

        // Solo con fuentes de prueba: la imagen del telon en pequeño (%TEMP%\switch2_telon.png), para comprobarla
        static void SaveCurtainThumb(Bitmap img)
        {
            try
            {
                using (Bitmap t = new Bitmap(480, 270))
                {
                    using (Graphics g = Graphics.FromImage(t)) g.DrawImage(img, 0, 0, 480, 270);
                    t.Save(Path.Combine(Path.GetTempPath(), "switch2_telon.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { }
        }

        // Con el telon aun puesto se comprueba que la ventana de escalado de Magpie sigue ahi y, si no, se vuelve a escalar
        // (1.2.4 ronda 6, QUITAFG, fase 2: EnsureStillScaling era de Magpie; fuera)

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] static extern int GetClassNameFg(IntPtr h, StringBuilder s, int n);
        // Quien tiene el primer plano (para el log)
        static string FgOwner()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return "nadie";
            uint pid; GetWindowThreadProcessId(fg, out pid);
            StringBuilder sb = new StringBuilder(128); GetClassNameFg(fg, sb, sb.Capacity);
            string n = "?"; try { n = Process.GetProcessById((int)pid).ProcessName; } catch { }
            return n + " / " + sb + (pid == (uint)Process.GetCurrentProcess().Id ? " (esta app)" : "");
        }

        // ---- 17-11 (VELOCIDAD): quien le quita el primer plano a la imagen (el motor propio o mpv) ----
        // engine.log 17-11 02:11-02:13, 02:31, 02:39-02:40, 02:46, 02:48-02:49: el motor con la generacion puesta escribe "Sin el foco con la
        // generacion puesta: se recupera (bien / no se pudo)" cada 3 s durante 10-70 s y la generacion pierde cientos de intermedias por
        // tanda ("generacion: 890 de 1176 sin la intermedia"); sin el foco tampoco valen el mando ni el boton de captura (MpvInFront). En
        // switch2.log no salia nada en esos ratos (la app no estaba cambiando nada) y ni la app ni el motor decian quien tenia el primer
        // plano. Ahora: un gancho de Windows (EVENT_SYSTEM_FOREGROUND, sin sondeos, en el hilo de la interfaz) apunta quien lo coge mientras
        // se juega (las 3 primeras veces una linea cada una, luego un resumen cada 30 s como mucho) y, con el motor, se lo devuelve al
        // momento si lo coge algo que no lo quiere: la ventana auxiliar de esta app (focusProxy), el escritorio o nadie. Lo de otro
        // programa (la terminal, el explorador...) solo se apunta: el motor lo recupera como siempre (cada 3 s)
        delegate void FgEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll", EntryPoint = "SetWinEventHook")] static extern IntPtr FgSetWinEventHook(uint min, uint max, IntPtr mod, FgEventProc fn, uint pid, uint tid, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] static extern int FgGetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int ProxyGetLong(IntPtr h, int idx);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int ProxySetLong(IntPtr h, int idx, int v);
        FgEventProc fgProc;                 // (referencia viva: si la recoge el GC, el gancho se cae)
        IntPtr fgHook;                      // (19-11: se quita al salir, QuitarGanchos)
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
        readonly object fgSync = new object();
        int fgLossN;                        // perdidas desde la ultima linea del log
        int fgLossLogged;                   // lineas sueltas ya escritas (las 3 primeras)
        DateTime fgLossLogAt = DateTime.MinValue;
        string fgLossLast;                  // la ultima que se lo quito (para el resumen)
        static readonly int ownPid = Process.GetCurrentProcess().Id;

        void InstallFgWatch()
        {
            fgProc = OnForegroundEvent;
            // EVENT_SYSTEM_FOREGROUND (3), WINEVENT_OUTOFCONTEXT: llega por el bucle de mensajes de este hilo (el de la interfaz)
            fgHook = FgSetWinEventHook(3, 3, IntPtr.Zero, fgProc, 0, 0, 0);
            if (fgHook == IntPtr.Zero)
                Log.Write("Aviso: sin vigilancia del primer plano (" + Marshal.GetLastWin32Error() + ")");
        }

        // En el hilo de la interfaz (comparte con el gancho del teclado): lo minimo y el resto en otro hilo
        void OnForegroundEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (idObject != 0 || userExit) return;
                IntPtr img = mpvWindow;
                if (img == IntPtr.Zero || hwnd == img) return;
                // Solo jugando con la imagen ya puesta: sin telon, ni pantalla de carga, ni cambios en marcha (ahi el primer plano lo
                // mueve la app a proposito)
                if (!lsReady || homeBusy || passesBusy || relaunchSize != null || relaunchEngine || splash != null || relevo != null ||
                    (curtain != null && !curtain.IsDisposed)) return; // (relevo: DLSS 5 si/no sin telon, el primer plano pasa de uno a otro)
                Process p = mpv;
                if (p == null || PowerOffClock != null || powerOffPid == p.Id || stallKilledPid == p.Id) return; // (cerrandose: consola apagada)
                IntPtr proxy = focusProxy;
                ThreadPool.QueueUserWorkItem(delegate { ForegroundLost(hwnd, img, p, proxy); });
            }
            catch { }
        }

        void ForegroundLost(IntPtr h, IntPtr img, Process p, IntPtr proxy)
        {
            try
            {
                if (p.HasExited || mpv != p || mpvWindow != img) return;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                StringBuilder cb = new StringBuilder(128);
                if (h != IntPtr.Zero) GetClassNameFg(h, cb, cb.Capacity);
                string cls = cb.ToString();
                // 19-11 noche (de Prisma): Joyxoff abre su teclado en pantalla con L3 (aqui L3 = selector de generacion): coge el primer
                // plano y encoge la ventana de mpv, y se reabria Magpie. Con la imagen delante se esconde y se devuelve todo al momento
                if (cls == "JoyXboxVkWindow") { HideJoyxoffKeyboard(h, img, p); return; }
                // Con el motor (la generacion lo necesita), de vuelta al momento si lo coge algo que no lo quiere. No en los relevos de
                // DLSS 5 sin telon (ahi el primer plano es de mpv a proposito: Magpie escala la ventana de delante)
                bool unwanted = h == IntPtr.Zero || h == proxy || cls == "Progman" || cls == "WorkerW";
                // 21-11 (1.0.11, el usuario: "dentro de la app no me reacciona la tecla de Windows"): al abrirse el menu Inicio el primer plano
                // pasa un instante por "nadie" y se devolvia al motor al momento: Inicio se cerraba nada mas abrirse. Ahora con "nadie" se
                // espera 0,4 s y solo si sigue sin nadie (o en el escritorio) se devuelve; si lo ha cogido Windows (Inicio, buscador, barra), no
                if (unwanted && h == IntPtr.Zero)
                {
                    Thread.Sleep(400);
                    h = GetForegroundWindow();
                    if (h != IntPtr.Zero && h != proxy)
                    {
                        cb.Length = 0; GetClassNameFg(h, cb, cb.Capacity); cls = cb.ToString();
                        GetWindowThreadProcessId(h, out pid);
                        unwanted = cls == "Progman" || cls == "WorkerW";
                        if (h == img) return;
                    }
                }
                bool back = false;
                if (Config.EngineActive && unwanted && GetForegroundWindow() == h)
                {
                    ForceForeground(img);
                    back = GetForegroundWindow() == img;
                }
                string who = "nadie";
                if (h != IntPtr.Zero)
                {
                    StringBuilder tb = new StringBuilder(96);
                    FgGetWindowText(h, tb, tb.Capacity);
                    string n = "?";
                    try { using (Process q = Process.GetProcessById((int)pid)) n = q.ProcessName; } catch { }
                    string title = tb.ToString();
                    if (title.Length > 60) title = title.Substring(0, 60) + "...";
                    who = n + " (pid " + pid + (pid == (uint)ownPid ? ", esta app" + (h == proxy ? ": la ventana auxiliar" : "") : "") +
                          ", " + cls + (title.Length > 0 ? ", \"" + title + "\"" : "") + ")";
                }
                string what = (Config.EngineActive ? "el motor propio" : "mpv") + " pierde el primer plano: lo coge " + who +
                              (back ? " -> devuelto al momento" : Config.EngineActive && unwanted ? " -> no se pudo devolver" : "");
                RitmoDiag.Ventanal("otra ventana con el primer plano (" + who + ")");   // (1.2.3 RITMO2, src\Lanzador\Ritmo.cs)
                lock (fgSync)
                {
                    fgLossN++;
                    fgLossLast = what;
                    DateTime now = DateTime.Now;
                    if (fgLossLogged < 3) { fgLossLogged++; fgLossN = 0; fgLossLogAt = now; Log.Write("Primer plano: " + what); }
                    else if ((now - fgLossLogAt).TotalSeconds >= 30)
                    {
                        Log.Write("Primer plano: " + fgLossN + " veces perdido en " + (int)(now - fgLossLogAt).TotalSeconds + " s; la ultima: " + what);
                        fgLossN = 0;
                        fgLossLogAt = now;
                    }
                }
            }
            catch (Exception ex) { Log.Error("Vigilando el primer plano", ex); }
        }

        [DllImport("user32.dll", EntryPoint = "ShowWindow")] static extern bool JoyShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] static extern bool JoyIsVisible(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "PostMessage")] static extern bool JoyPostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        int joyKbN;

        void HideJoyxoffKeyboard(IntPtr kb, IntPtr img, Process p)
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                JoyShowWindow(kb, 0); // SW_HIDE
                for (int i = 0; i < 10 && JoyIsVisible(kb); i++) Thread.Sleep(10);
                bool closed = false;
                if (JoyIsVisible(kb)) { JoyPostMessage(kb, 0x0010, IntPtr.Zero, IntPtr.Zero); closed = true; } // WM_CLOSE si no se escondio
                ForceForeground(img);
                // La ventana de mpv (Joyxoff la encoge al abrir su teclado): a su tamaño ya, sin esperar a la vigilancia (que reabriria Magpie)
                for (int i = 0; i < 6; i++)
                {
                    Thread.Sleep(i == 0 ? 0 : 50);
                    if (!p.HasExited && !Config.EngineActive && p.ProcessName.Equals("mpv", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] ww = Config.WindowFor(cfg.CaptureNow).Split('x');
                        RECT r;
                        if (GetWindowRect(img, out r) && (r.Left != 0 || r.Top != 0 ||
                            (r.Right - r.Left).ToString(CultureInfo.InvariantCulture) != ww[0] || (r.Bottom - r.Top).ToString(CultureInfo.InvariantCulture) != ww[1]))
                            SetWindowPos(img, IntPtr.Zero, 0, 0, int.Parse(ww[0], CultureInfo.InvariantCulture), int.Parse(ww[1], CultureInfo.InvariantCulture), 0x0004 | 0x0010);
                    }
                }
                if (++joyKbN <= 5 || joyKbN % 20 == 0)
                    Log.Write("Teclado en pantalla de Joyxoff (L3) con la imagen delante: " + (closed ? "cerrado" : "escondido") + " y el primer plano devuelto en " +
                              sw.ElapsedMilliseconds + " ms (vez " + joyKbN + "; la app usa L3 para el selector de generacion)");
            }
            catch (Exception ex) { Log.Error("Escondiendo el teclado de Joyxoff", ex); }
        }

        [DllImport("user32.dll", EntryPoint = "IsWindow")] static extern bool WatchIsWindow(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] static extern bool WatchIsVisible(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "GetWindow")] static extern IntPtr WatchGetWindow(IntPtr h, uint cmd);
        // 17-11 (VELOCIDAD): la ventana sigue siendo la principal de ese proceso (la que daria MainWindowHandle: suya, visible, sin duena)
        static bool WatchWindowOk(IntPtr h, int pid)
        {
            if (!WatchIsWindow(h) || !WatchIsVisible(h) || WatchGetWindow(h, 4) != IntPtr.Zero) return false; // (4 = GW_OWNER)
            uint wp;
            GetWindowThreadProcessId(h, out wp);
            return wp == (uint)pid;
        }

        // 17-11 (VELOCIDAD): al quitar el telon con el motor como imagen, el primer plano al motor ya (el telon se lo habia quitado,
        // ShowCurtain). Antes nadie se lo devolvia: Windows elegia otra ventana al cerrarse el telon y el motor lo recuperaba solo con la
        // generacion puesta y como mucho cada 3 s (engine.log 02:31:18.939 "Sin el foco ... (bien)" justo al quitarse el telon de la
        // entrada; sin generacion, ni eso: el mando y el boton de captura no hacian nada hasta tocarlo). Solo si lo tiene esta app (el
        // telon) o nadie: nunca se le quita a otro programa
        void ImageFocusAfterCurtain()
        {
            try
            {
                if (!Config.EngineActive || userExit) return;
                Process p = mpv;
                if (p == null || p.HasExited) return;
                IntPtr img = mpvWindow;
                uint ip = 0;
                if (img != IntPtr.Zero) GetWindowThreadProcessId(img, out ip);
                if (ip != (uint)p.Id) { p.Refresh(); img = p.MainWindowHandle; } // (recien relanzado: WatchLoop aun no la ha mirado)
                if (img == IntPtr.Zero) return;
                IntPtr fg = GetForegroundWindow();
                if (fg == img) return;
                uint pid = 0;
                if (fg != IntPtr.Zero) GetWindowThreadProcessId(fg, out pid);
                if (fg != IntPtr.Zero && pid != (uint)ownPid) return;
                ForceForeground(img);
            }
            catch { }
        }

        // (1.2.4 ronda 6, QUITAFG, fase 2: Rescale era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: WaitWhileStarting era de Magpie; fuera)

        // Un solo cambio grande a la vez (reabrir Magpie, pasar del HOME al juego): antes passesBusy se miraba y se ponia en dos pasos
        int busyGate;
        bool TryGate() { return Interlocked.CompareExchange(ref busyGate, 1, 0) == 0; }
        void ReleaseGate() { Interlocked.Exchange(ref busyGate, 0); }

        // (1.2.4 ronda 6, QUITAFG, fase 2: KeepMpvInFront era de Magpie; fuera)

        // Red de seguridad: el motor escondido en espera no se puede quedar el primer plano (1.2.4 ronda 6, QUITAFG: antes tambien las
        // ventanas de Magpie, que ya no esta)
        void LsFocusGuard(Process p)
        {
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                while (!p.HasExited && !userExit && mpv == p)
                {
                    Thread.Sleep(25);
                    // 16-11 (motor_en_espera): el motor escondido en espera tampoco se puede quedar el primer plano
                    Process se = standbyEngine;
                    if (se != null && se != p) StandbyFgBack(se);
                }
            }
            catch (InvalidOperationException) { } // ese mpv ya se ha cerrado (se esta relanzando)
            catch (Exception ex) { Log.Error("Vigilando el primer plano", ex); }
        }

        // ---------------- Ritmo real del juego ----------------
        // El filtro de mpv (AddCommonMpvArgs) quita las imagenes repetidas: Magpie solo recibe las distintas. Con Magpie no hace
        // falta cambiar de "look" de DLSS 5 segun el ritmo (lo hacia LS con su atajo): DLSSNR ajusta solo su resolucion de entrada
        // (adaptativa) para llegar al ritmo del juego
        int dlss5Passes = 1;        // pasadas de DLSSNR (1-3), se guarda en games\dlss5_passes.txt
        bool want30;                // el juego va a 30 fps
        volatile bool vfDedupe;     // mpv lleva puesto el filtro de repetidas (TrayApp.DedupeOn)
        int dlss5Level;             // nivel de DLSS 5 (1-4), se guarda en games\dlss5_level.txt
        int fast60, slow30;         // lecturas seguidas (cada 0,5 s) a 45 fps o mas / a ~30 con imagenes nuevas
        double vfFps;               // imagenes por segundo a la salida del filtro de repetidas (mpv)
        bool vfNew;                 // la ultima que salio era nueva (no una copia de una imagen quieta)

        // Cada 0,5 s que Magpie siga escalando; cada 5 s el ritmo
        void WatchLoop(Process p)
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; // no compite con mpv
            Thread gl = new Thread(delegate () { GrabLoop(p); });
            gl.IsBackground = true;
            gl.Priority = ThreadPriority.BelowNormal;
            gl.Start();
            try
            {
                int wrongWindow = 0, winTicks = 0;
                DateTime lastRescale = DateTime.MinValue, lastPacing = DateTime.MinValue, lastWindowFix = DateTime.MinValue;
                while (!p.HasExited && !userExit && mpv == p)
                {
                    Thread.Sleep(500);
                    IntPtr hwnd;
                    try
                    {
                        // 17-11 (VELOCIDAD): la ventana de la vez anterior mientras siga siendo suya y visible; la de Windows (Refresh +
                        // MainWindowHandle: 3,4 ms medidos, todas las ventanas del sistema) cada 5 s o si deja de valer
                        hwnd = mpvWindow;
                        if (hwnd == IntPtr.Zero || ++winTicks >= 10 || !WatchWindowOk(hwnd, p.Id))
                        {
                            winTicks = 0;
                            p.Refresh();
                            hwnd = p.MainWindowHandle;
                        }
                        mpvWindow = hwnd;
                    }
                    catch (InvalidOperationException) { break; } // mpv acaba de cerrarse
                    DateTime now = DateTime.Now;
                    if (!signalOn) continue;
                    // (1.2.4 ronda 6, INTEGRA6: un fallo en una vuelta ya no para la vigilancia del motor: en la sesion b1 del 23-11 una
                    // NullReferenceException al pasar al modo 120 dejo sin SyncEngine / CheckEngineSmooth el resto de la vida de ese motor -el 120
                    // con Smooth Motion puesto y el vuelta al 30 sin relevo-. Ahora se apunta con su pila y se sigue)
                    try
                    {

                    // 16-11 (motor_en_espera): en el HOME a 4K nativo con mpv, 3 s seguidos sin cambios en marcha: el motor en espera, si
                    // hace falta (una vez por sesion; ahi el tiron de su arranque no se ve)
                    // (21-11, 1.2.1: 1 s y no 3: en el log del usuario, a los 3 s de abrir la app ya se entraba en el juego, sin puente; el motor
                    // tarda 4,7-7,4 s en estar listo: cuanto antes, mas veces llega para el puente de la entrada, src\SinTelon.cs PuenteTardio)
                    if (cfg.HomeNative && lsReady && !homeBusy && !passesBusy && relaunchSize == null && !relaunchEngine && !Config.EngineActive)
                    { if (++homeQuietTicks >= 2) MaybePrestartStandby(); }
                    else homeQuietTicks = 0;

                    // Ventana de mpv con otro tamaño (ver RestoreWindowSize). Magpie mide la ventana al empezar a escalar: con el
                    // escalando, se reabre
                    if (Config.EngineActive) SyncEngine();
                    if (Config.EngineActive) CheckEngineSmooth(now);   // 17-11 16:55
                    if (Config.EngineActive) CheckEngineFixed(now);    // 20-11 (LATENCIA5)
                    if (Config.EngineActive) CheckEngineFfx(now);      // 21-11 (1.0.9): FSR 3 FG en el motor
                    if (Config.EngineActive) MaybeReserveEngine(now);  // 21-11 (1.0.9): motor de reserva con la otra generacion
                    if (!Config.EngineActive && !relaunchEngine && !homeBusy && !passesBusy && relaunchSize == null && (now - lastWindowFix).TotalSeconds >= 10)
                    {
                        RECT wr;
                        string[] ww = Config.WindowFor(cfg.CaptureNow).Split('x');
                        bool wrong = GetWindowRect(hwnd, out wr) && wr.Left > -30000 &&
                                     (wr.Left != 0 || wr.Top != 0 ||
                                      (wr.Right - wr.Left).ToString(CultureInfo.InvariantCulture) != ww[0] ||
                                      (wr.Bottom - wr.Top).ToString(CultureInfo.InvariantCulture) != ww[1]);
                        if (!wrong) wrongWindow = 0;
                        else if (++wrongWindow >= 2)
                        {
                            wrongWindow = 0;
                            lastWindowFix = now;
                            RestoreWindowSize(p);   // (1.2.4 ronda 6: sin Magpie que reabrir)
                            continue;
                        }
                    }

                    // (1.2.4 ronda 6, QUITAFG: sin Magpie, ni "Magpie ha dejado de escalar" ni su ritmo cada 5 s -CheckLsPacing-)
                    if (lsReady && !cfg.HomeNative && !homeGrace) ChooseLook(hwnd, now);
                    if (lsReady) RunHomeTick(now);
                    }
                    catch (Exception ex)
                    {
                        if (++watchErrs <= 5) Log.Write("ERROR: Vigilando la imagen del motor (se sigue vigilando): " + ex.ToString().Replace("\r\n", " | "));
                    }
                }
            }
            catch (Exception ex) { Log.Error("Vigilando la imagen de mpv", ex); }
        }
        int watchErrs;

        // (1.2.4 ronda 6, QUITAFG, fase 2: LsStats era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: ReadLsStats era de Magpie; fuera)

        // Cada 0,5 s: si el juego va a 30 o a 60 (para los avisos y el log). Con Magpie no hay que cambiar nada en caliente
        void ChooseLook(IntPtr hwnd, DateTime now)
        {
            if (!cfg.Dedupe && !cfg.CapMode) return;
            double f = vfFps;
            bool was30 = want30;
            if (f >= 45) { fast60++; slow30 = 0; }
            else
            {
                fast60 = 0;
                if (f >= 26 && f <= 34 && vfNew) slow30++;
            }
            MiraCadencia(f, now); // (18-11, CONSOLAS: un juego a 40 = la consola a 120 Hz: al log y pista para consola=auto)
            if (cfg.CapMode) want30 = true;
            else if (fast60 >= 2) want30 = false;
            else if (slow30 >= 30) want30 = true;
            if (want30 != was30)
                Log.Write(want30 ? "Juego a 30 fps (" + Ms(Math.Round(f, 1)) + " imagenes nuevas por segundo)"
                                 : "Juego o menu a 60 fps (" + Ms(Math.Round(f, 1)) + " por segundo)");
        }

        // ---------------- Mando (XInput) ----------------
        // Con la imagen de la consola delante: cruceta izquierda / arriba / derecha / abajo = captura a 720p, 1080p, 1440p o 4K;
        // X / Y / B / A = DLSS 5 con 1 / 2 / 3 / 4 pasadas; L1 / L2 / R2 / R1 = nivel 1 / 2 / 3 / 4; Share (-) / Options (+) =
        // modo 30 / 60 fps; L3 = filtro de tramado nuevo / el de antes (10-11; antes repetia A); R3 = escalador DLSS / el de LS.
        // 18-11 (MANDOS): lo lee src\Mandos.cs (XInput, HID, winmm) y sin Joyxoff hace el modo PC fuera de la imagen (mando_pc)

        const ushort PadUp = 0x0001, PadDown = 0x0002, PadLeft = 0x0004, PadRight = 0x0008, PadStart = 0x0010,
                     PadView = 0x0020, PadLB = 0x0100, PadRB = 0x0200, PadA = 0x1000, PadB = 0x2000, PadX = 0x4000, PadY = 0x8000,
                     PadL3 = 0x0040, PadR3 = 0x0080,
                     // Los gatillos (L2 / R2) son analogicos: se ponen en estos bits (que XInput no usa) al pasar de un umbral
                     PadLT = 0x0400, PadRT = 0x0800;
        bool dlssScalerOn = true;       // R3: el escalador normal (escalador=); false = el alternativo (escalador_alternativo=)
        DateTime lastPadAction = DateTime.MinValue;
        volatile string relaunchSize;   // captura pedida con el mando: mpv se reabre con ella
        Stopwatch captureSw;            // desde que se pidio la captura (para el log: tiempo con el telon)
        bool dlss5Shown = true;         // DLSS 5 encendido (false: sin DLSSNR en la cadena de Magpie; games\dlss5_on.txt)
        readonly LinkQueue osdQueue = new LinkQueue();

        // 17-11 (VELOCIDAD): las ordenes para mpv / el motor (cmdQueue) y los avisos (osdQueue) despiertan a MpvLinkLoop al encolarse y
        // salen al momento. Antes esperaban a su paso de 0,1 s: de media 50 ms (hasta 100) de mas en cada cambio (captura del HOME al
        // juego y vuelta, ajustes del motor, avisos, cada captura para mirar el HOME). Mismo orden y mismo hilo que escribe (el del enlace)
        static readonly AutoResetEvent linkWake = new AutoResetEvent(false);
        sealed class LinkQueue : List<string>
        {
            public new void Add(string s) { base.Add(s); linkWake.Set(); }
        }

        // Avisos en pantalla: solo como respuesta a los botones del mando (como siempre). Al entrar en un juego, volver al
        // HOME o ponerse los ajustes de un juego no sale ningun texto (03-11: el usuario lo quiere lo mas discreto posible)
        const string OsdMs = "2500";

        // 15-11 (Prisma): los avisos los pinta como una pastilla el script de mpv (scripts\switch2_power.lua, sobre la imagen: Magpie
        // los escala con ella) o el motor propio (Osd.cs), igual que el telon y la pantalla de carga:
        // "script-message switch2-osd <ms> <texto> [warn]" ("Titulo · detalle": el titulo en blanco, el resto en gris; warn = punto
        // rojo en vez de cian). La cola guarda la orden ya hecha
        static string OsdCmd(string text, string ms, bool warn)
        {
            text = Idioma.T(text); // (ConsoleEnhancer: en ingles, languages\en.txt)
            return warn ? JsonCmd("script-message", "switch2-osd", ms, text, "warn")
                        : JsonCmd("script-message", "switch2-osd", ms, text);
        }

        void Osd(string text)
        {
            lock (osdQueue) osdQueue.Add(OsdCmd(text, OsdMs, false));
        }

        // Un aviso de algo que va mal (punto rojo), ms en pantalla
        void OsdWarn(string text, string ms)
        {
            lock (osdQueue) osdQueue.Add(OsdCmd(text, ms, true));
        }

        // Se acaba de pulsar un boton del mando (o el de captura, la generacion): el telon de lo que reabre lleva su texto
        // y, al quitarse, sale lo que ha quedado puesto. Sin boton reciente (entrar en un juego, sus ajustes, arreglos
        // solos), telon negro liso y nada mas
        bool PadRecent { get { return !applying && (DateTime.Now - lastPadAction).TotalSeconds < 10; } }
        volatile bool padRestart;
        DateTime padRestartAt = DateTime.MinValue;

        // live (15-11, telon_vivo): se reabre Magpie solo (RestartLs) y mpv sigue con la captura: el telon AL MOMENTO (ya opaco y
        // sin copiar antes la ultima imagen, para matar Magpie enseguida: src\TelonVivo.cs), en vivo si se puede y si no negro, con la
        // pastilla arriba solo si viene del mando
        void ShowPadCurtain(string subtitle, string title, bool live = false)
        {
            subtitle = Idioma.T(subtitle); title = Idioma.T(title); // (ConsoleEnhancer)
            bool pad = PadRecent;
            if (pad)
            {
                padRestart = true;
                padRestartAt = DateTime.Now;
            }
            if (live) { ShowFastCurtain(pad ? subtitle : null, pad ? title : null, true); return; }
            if (!pad) { ShowCurtain(); return; }
            // 15-11 (Prisma): sobre la ultima imagen atenuada (si llega enseguida), con la pastilla y la linea de progreso
            ShowCurtain(subtitle, title, false, PadCurtainImage(title, subtitle));
        }

        void GamepadLoop()
        {
            // 18-11 (MANDOS): los mandos los lee la capa propia de src\Mandos.cs (XInput + HID por Raw Input + winmm; varios a la vez y
            // en caliente; "Mando conectado: ..." en el log con los nombres de sus botones) y fuera de la imagen hace el modo PC
            // (mando_pc). Aqui todo sigue igual: mismas acciones, cada 50 ms, gatillos con histeresis, la diagonal de la cruceta
            PadHub hub = PadHub.Shared;
            hub.Start();
            StartPcMode(hub);
            bool any = false;
            ushort prev = 0;
            ushort dirSeen = 0, dirDone = 0; // direccion sola de la cruceta vista ahora / ya usada hasta soltarla
            int dirCount = 0;
            uint prevShot = 0;
            DateTime l3DownAt = DateTime.MinValue; // (19-11: L3 pulsado desde; ver abajo)
            bool l3Held = false;
            DateTime shotAt = DateTime.MinValue;
            try
            {
                while (!userExit)
                {
                    Thread.Sleep(50);
                    PadSnapshot s;
                    hub.Poll(out s);
                    if (s.Count == 0)
                    {
                        if (any) { any = false; prev = 0; dirSeen = dirDone = 0; dirCount = 0; prevShot = 0; l3DownAt = DateTime.MinValue; }
                        continue;
                    }
                    any = true;
                    // 21-11 (1.0.11): el boton central abre / cierra el menu de ajustes; abierto, el mando es suyo (src\Lanzador\MenuAjustes.cs)
                    if (AjustesMando(s))
                    {
                        prev = (ushort)(s.Buttons & 0xFFFF);
                        prevShot = s.Buttons & (PadBtn.Capture | PadBtn.Touchpad);
                        dirSeen = dirDone = 0; dirCount = 0; l3DownAt = DateTime.MinValue; shotAt = DateTime.MinValue;
                        continue;
                    }
                    // Captura (Switch, 8BitDo...) / panel tactil (PlayStation) = la generacion, como el boton de captura del GameSir. El del
                    // GameSir (su teclado) lo hace el gancho (KeyHook, que ademas se lo quita a Windows); si en 0,2 s no lo ha hecho, aqui
                    uint shot = s.Buttons & (PadBtn.Capture | PadBtn.Touchpad);
                    if ((shot & ~prevShot) != 0) { if (s.KbdCapture) shotAt = DateTime.Now; else PadCaptureButton(false); }
                    prevShot = shot;
                    if (shotAt != DateTime.MinValue && (DateTime.Now - shotAt).TotalMilliseconds >= 200) { shotAt = DateTime.MinValue; PadCaptureButton(true); }
                    ushort b = (ushort)(s.Buttons & 0xFFFF & ~(PadLT | PadRT));
                    // Gatillos con histeresis: pulsado por encima de 140 (de 255), suelto por debajo de 60
                    bool lt = (prev & PadLT) != 0 ? s.LT > 60 : s.LT > 140;
                    bool rt = (prev & PadRT) != 0 ? s.RT > 60 : s.RT > 140;
                    if (lt) b |= PadLT;
                    if (rt) b |= PadRT;
                    // Cruceta: cuenta la direccion en la que se queda sola 2 lecturas seguidas (~100 ms), y una vez hasta
                    // soltarla. Al pulsar se cuela un instante una diagonal (medido con el GameSir: arriba llega como
                    // izquierda + arriba y luego arriba): antes se descartaba la diagonal y "arriba" ya no contaba como
                    // pulsacion nueva, asi que la captura a 1440p no respondia nunca
                    ushort dpad = (ushort)(b & (PadUp | PadDown | PadLeft | PadRight));
                    ushort single = (dpad == PadUp || dpad == PadDown || dpad == PadLeft || dpad == PadRight) ? dpad : (ushort)0;
                    if (dpad == 0) { dirDone = 0; dirSeen = 0; dirCount = 0; }
                    else if (single != 0) { if (single == dirSeen) dirCount++; else { dirSeen = single; dirCount = 1; } }
                    ushort pressed = (ushort)(b & ~prev & (PadLB | PadRB | PadLT | PadRT | PadX | PadY | PadA | PadB | PadView | PadStart |
                                                           PadR3));
                    // 19-11 (GENERACION): L3 = selector del tipo de generacion (al soltarlo, src\Generacion.cs); mantenido ~0,8 s = el filtro de
                    // tramado nuevo / el de antes (lo que hacia L3 desde el 10-11). Sin la espera de 0,4 s de los demas: se pasa de tipo deprisa
                    bool l3 = (b & PadL3) != 0;
                    if (l3 && l3DownAt == DateTime.MinValue) { l3DownAt = DateTime.Now; l3Held = false; }
                    else if (l3 && !l3Held && (DateTime.Now - l3DownAt).TotalMilliseconds >= 800)
                    {
                        l3Held = true;
                        if (MpvInFront()) { lastPadAction = DateTime.Now; ToggleStrongDither(); }
                    }
                    else if (!l3 && l3DownAt != DateTime.MinValue)
                    {
                        l3DownAt = DateTime.MinValue;
                        if (!l3Held && MpvInFront()) GenSelectorPress();
                    }
                    prev = b;
                    if (single != 0 && dirCount >= 2 && single != dirDone)
                    {
                        dirDone = single;
                        pressed |= single;
                    }
                    // Sin boton modificador (lo pidio el usuario): solo con la ventana de mpv delante, que es cuando se juega.
                    // Con Joyxoff manejando el PC (otra ventana delante) no hace nada. Con View pulsado sigue valiendo
                    if (pressed == 0) continue;
                    // Una orden cada 0,4 s (captura y pasadas, cada 2 s: reabren cosas)
                    bool heavy = (pressed & (PadUp | PadDown | PadLeft | PadRight | PadX | PadY | PadB | PadView | PadStart)) != 0;
                    double since = (DateTime.Now - lastPadAction).TotalMilliseconds;
                    if (since < 400 || (heavy && since < 2000)) continue;
                    if (!MpvInFront()) continue;
                    lastPadAction = DateTime.Now;
                    // En el HOME (4K tal cual) no hay nada que ajustar: los ajustes son de cada juego, dentro de el
                    // (10-11: tampoco en el HOME con LS aun escalando, homeGrace)
                    if (InHome || homeBusy)
                    {
                        if (!homeBusy) Osd(MenuTexto + (cfg.HomeNative ? " en 4K nativo" : "") + " · los ajustes se cambian dentro de cada juego");
                        continue;
                    }
                    if ((pressed & PadLeft) != 0) RequestCapture("1280x720");
                    else if ((pressed & PadUp) != 0) RequestCapture("1920x1080");
                    else if ((pressed & PadRight) != 0) RequestCapture("2560x1440");
                    else if ((pressed & PadDown) != 0) RequestCapture("3840x2160");
                    else if ((pressed & PadX) != 0) SetPasses(1);
                    else if ((pressed & PadY) != 0) SetPasses(2);
                    else if ((pressed & PadB) != 0) SetPasses(3);
                    else if ((pressed & PadA) != 0) ToggleDlss5(); // 03-11: sin modo de 4 pasadas, A = DLSS 5 si / no
                    else if ((pressed & PadLB) != 0) SetLevel(1);
                    else if ((pressed & PadLT) != 0) SetLevel(2);
                    else if ((pressed & PadRT) != 0) SetLevel(3);
                    else if ((pressed & PadRB) != 0) SetLevel(4);
                    else if ((pressed & PadView) != 0) CeBotonVista();  // (ConsoleEnhancer: Share / Crear / Vista / - = generacion si/no; src\BotonesCe.cs)
                    else if ((pressed & PadStart) != 0) CeBotonMenu();  // (ConsoleEnhancer: Options / Menu / + = modo 30 -> 60 -> 120 -> 30, el 120 solo donde cabe; src\BotonesCe.cs)
                    // (L3: arriba, el selector de generacion; mantenido, el filtro de tramado)
                    else if ((pressed & PadR3) != 0) ToggleScaler();
                }
            }
            catch (DllNotFoundException) { Log.Write("Sin XInput: el mando no puede cambiar la imagen"); }
            catch (Exception ex) { Log.Error("Mando", ex); }
        }

        // La ventana de mpv es la de delante (se esta jugando; no esta el telon ni otra ventana)
        bool MpvInFront()
        {
            Process p = mpv;
            if (p == null) return false;
            try
            {
                if (p.HasExited) return false;
                p.Refresh();
                return p.MainWindowHandle != IntPtr.Zero && GetForegroundWindow() == p.MainWindowHandle;
            }
            catch { return false; }
        }

        static string SizeName(string size)
        {
            return size == "1280x720" ? "720p" : size == "1920x1080" ? "1080p" : size == "3840x2160" ? "4K nativo" : "1440p";
        }

        void RequestCapture(string size)
        {
            if (size == cfg.VideoSize) { Osd("Captura " + SizeName(size) + " · ya activa"); return; }
            if (CapturaSinSentido(size)) return; // (18-11, CONSOLAS: mas de lo que saca la consola: la capturadora solo agrandaria)
            if (relaunchSize != null) return; // ya se esta cambiando
            // 15-11: no con Magpie reabriendose (como SetPasses / SetMode): a las 19:26:37 se acepto la de 1440p con Magpie aun
            // preparando la de 1080p, que siguio con su enfoque de 1080p. Los ajustes de un juego (applying) si: los pone el
            if (!applying && (passesBusy || !lsReady)) return;
            // 21-11 (1.2.01, el usuario: "he cambiado de 1440p a 4k y se ha quedado pillada la app ahi cargando"): en el modo 120 una captura
            // que la capturadora no da a 120 (4K en la GC573) se abria a 3840x2160 a 120 fps -> No Signal para siempre. Se baja al 60 antes
            if (cfg.Mode120 && !cfg.Captura120Ok(size))
            {
                cfg.Mode120 = false; cfg.CapMode = false;
                want30 = false;
                SaveText(Paths.ModeFile, "60");
                lock (cmdQueue)
                {
                    vfDedupe = DedupeOn(cfg, fgWanted);
                    cmdQueue.Add(vfDedupe ? JsonCmd("vf", "set", FrameFilter(cfg)) : JsonCmd("vf", "clr", ""));
                }
                Log.Write("Captura a " + SizeName(size) + ": la capturadora no la da a 120 fps -> modo 60 fps");
            }
            captureSw = Stopwatch.StartNew();
            SaveText(Paths.CaptureFile, size);
            if ((Config.EngineActive || Config.WindowFor(size) == Config.WindowFor(cfg.VideoSize)) && ipcUp)
            {
                // La misma ventana (1080p <-> 1440p): solo se vuelve a abrir la capturadora con el otro tamaño, sin
                // cerrar mpv ni tocar LS (su escalado y DLSS siguen igual). Se ve ~1 s la ultima imagen
                // 15-11: en el motor, otra captura puede cambiar el motor que toca (a 4K DLSS 5 cabe distinto): entonces se reabre
                if (Config.EngineActive)
                {
                    string was = cfg.VideoSize;
                    string fmtWas = EngineFormat(cfg);
                    cfg.VideoSize = size;
                    PrepareCfg();
                    bool still = cfg.EngineNow;
                    // 18-11 19:24: con pixel_format_4k el formato cambia entre 4K (yuy2) y el resto (bgr24); el motor se lanzo con el
                    // otro formato y sin cerrarlo no llegaban fotogramas: se tomo por consola apagada y se cerro la app. Entonces, reabrir
                    if (EngineFormat(cfg) != fmtWas) still = false;
                    cfg.VideoSize = was;
                    if (!still)
                    {
                        Log.Write((applying ? "Perfil" : "Mando") + ": captura a " + SizeName(size) + " (se reabre la imagen)");
                        relaunchSize = size;
                        SaveCurProfile();
                        ShowPadCurtain("Un momento…", "Captura " + SizeName(size));
                        QuitMpvForRelaunch(true); // (16-11: el motor, en espera)
                        return;
                    }
                }
                Log.Write((applying ? "Perfil" : "Mando") + ": captura a " + SizeName(size) + " (se reabre la capturadora sin cerrar el motor)");
                cfg.VideoSize = size;
                relaunchSize = size; // si mpv se cerrase a medias, se vuelve a abrir ya con la nueva
                SaveCurProfile();
                switchUntil = DateTime.Now.AddSeconds(SwitchSecs);
                string engSet = EngineSetCmd(); // (17-11, VELOCIDAD: con el motor, sus ajustes de esta captura en la misma tanda)
                lock (cmdQueue)
                {
                    if (engSet != null) cmdQueue.Add(JsonCmd("engine", "set " + engSet));
                    cmdQueue.Add(JsonCmd("script-message", "switch2-switching", SwitchSecs.ToString(CultureInfo.InvariantCulture)));
                    // (1.2.4 arreglos, RELEVO: en pruebas con la fuente del motor -test_motor-, el motor tambien: saca el tamaño de aqui)
                    if (cfg.TestSource.Length == 0 || (Config.EngineActive && cfg.TestMotor.Length > 0)) cmdQueue.Add(JsonCmd("set", "demuxer-lavf-o", LavfOptions(cfg)));
                    cmdQueue.Add(JsonCmd("loadfile", SourceUrl(cfg), "replace"));
                    // (10-11: al poner los ajustes de un juego, sin aviso: lo mas discreto posible)
                    if (!applying) cmdQueue.Add(OsdCmd("Captura " + SizeName(size), OsdMs, false));
                }
                if (engSet != null) Log.Write("Motor propio: " + engSet + " (con el cambio de captura)");
                // Si no llega imagen nueva a tiempo, a la manera de siempre
                Thread guard = new Thread(delegate ()
                {
                    Thread.Sleep(SwitchWaitMs); // (21-11, 1.2.1: con captura_segura, 5 s)
                    if (relaunchSize == size && switchUntil != DateTime.MinValue)
                    {
                        Log.Write("La captura a " + SizeName(size) + " no ha llegado en " + SwitchWaitMs / 1000 + " s: se reabre mpv");
                        SwitchFallback();
                    }
                });
                guard.IsBackground = true;
                guard.Start();
                return;
            }
            Log.Write((applying ? "Perfil" : "Mando") + ": captura a " + SizeName(size) + " (otra ventana: se reabren mpv y Magpie)");
            relaunchSize = size;
            SaveCurProfile();
            // El telon (con el aviso si es del mando) ya, y mpv se cierra sin esperar (antes 0,4 s para ver el aviso en mpv). 16-11: al
            // momento, negro con el aviso arriba (antes la ultima imagen atenuada: 0,8-0,9 s de componerla a 4K antes de cerrar nada) y,
            // con otra ventana, Magpie fuera ya, a la vez que mpv (RunMpv lo reabre para la ventana nueva; antes se cerraba despues)
            bool pad = PadRecent;
            if (pad) { padRestart = true; padRestartAt = DateTime.Now; }
            ShowFastCurtain(pad ? "Un momento…" : null, pad ? "Captura " + SizeName(size) : null, false);
            QuitMpvForRelaunch();
            CurtainToFront();
        }

        const int SwitchSecs = 8;
        // 21-11 (1.2.1): lo que se espera la primera imagen de un cambio de captura sin cerrar mpv antes de reabrirlo (CapturaSegura.cs)
        int SwitchWaitMs { get { return cfg.CapturaSegura ? Math.Min(SwitchSecs * 1000, 5000) : SwitchSecs * 1000; } }
        DateTime switchUntil = DateTime.MinValue;  // cambio de captura sin cerrar mpv en marcha: sin vigilancia de apagado
        volatile bool ipcUp;                        // hay enlace IPC con mpv (para mandarle ordenes)
        readonly LinkQueue cmdQueue = new LinkQueue(); // (17-11: LinkQueue, ver osdQueue)

        static string JsonCmd(params string[] args)
        {
            StringBuilder sb = new StringBuilder("{\"command\":[");
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(args[i].Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            }
            return sb.Append("]}").ToString();
        }

        // El script Lua dice que ya hay imagen de la captura nueva
        void SwitchDone(string what)
        {
            CapCambioHecho(); // 21-11 (1.2.1): el formato nuevo da imagen (CapturaSegura.cs)
            if (homeSwitching) { homeSwitchOk = true; homeSwitchEvt.Set(); return; } // HOME <-> juego
            if (relaunchSize == null || switchUntil == DateTime.MinValue) return;
            relaunchSize = null;
            switchUntil = DateTime.Now.AddSeconds(1); // margen para que vuelva el ritmo
            Stopwatch cs = captureSw;
            captureSw = null;
            Log.Write("Captura a " + SizeName(cfg.VideoSize) + " lista sin cerrar el motor (" + what + "): " +
                      (cs != null ? cs.ElapsedMilliseconds + " ms desde que se pidio" : "") +
                      " (el enfoque de esta captura va en la proxima reapertura)");
        }

        // No se pudo sin cerrar mpv: se reabre (relaunchSize ya es la captura nueva; la ventana es la misma)
        void SwitchFallback()
        {
            capEnSitioFallo = true; // 21-11 (1.2.1): la reapertura se vigila con menos tiempo (CapturaSegura.cs)
            if (homeSwitching) { homeSwitchOk = false; homeSwitchEvt.Set(); return; } // lo reabre StartHomeSwitch
            if (relaunchSize == null) return;
            switchUntil = DateTime.MinValue;
            ShowPadCurtain("Un momento…", "Captura " + SizeName(cfg.VideoSize));
            QuitMpvForRelaunch();
        }

        void ToggleDlss5()
        {
            Process p = mpv;
            if (p == null || p.HasExited || GetForegroundWindow() != p.MainWindowHandle) return;
            if (passesBusy || relaunchSize != null || !lsReady) return;
            SetDlss5State(!dlss5Shown);
            Log.Write("Mando: DLSS 5 " + (dlss5Shown ? "encendido" : "apagado") + ReopenNote);
            SaveCurProfile();
            RestartLs(dlss5Shown ? "DLSS 5 activado" : "DLSS 5 desactivado",
                      dlss5Shown ? "Nivel " + dlss5Level + " · " + Dlss5PassText() : "Escalado " + ScalerText(), false, true); // (16-11: sin telon si se puede)
        }

        // 16-11 (el usuario: "si hay algun texto de que escalado se usa, corrigelo"): los avisos con lo que de verdad se usa: las pasadas y
        // la entrada de DLSS 5 que caben en este modo (antes las pedidas: "2 pasadas · Resolucion adaptativa" en el 60 con Smooth Motion,
        // que va con 1 pasada al 35 %) y el escalado del motor propio con DLSS 5 apagado (Config.ScalerShownName)
        string Dlss5PassText()
        {
            PrepareCfg();
            if (!cfg.Dlss5FitsNow) return Integrado.Dlss5Available ? "DLSS 5 no cabe en este modo" : Integrado.Dlss5MissingText;
            int now = cfg.Dlss5PassesNow, pct = cfg.Dlss5PctShownNow;   // (20-11: con DLSS 5 en el motor, su % de entrada)
            string t = Pasadas(now) + (pct > 0 ? " al " + pct + " %" : " · resolución adaptativa");
            // 16-11 (coordinador): en el 60 con generacion las 3 pasadas no caben ni con lo ligero (medido: 93-94 % de grafica a 720p); la
            // generacion no se quita sola: se dice que sin ella si caben
            if (now < dlss5Passes && !cfg.CapMode && !cfg.FgOff && cfg.PassesFitWithoutFg(dlss5Passes))
                t = Pasadas(dlss5Passes) + " solo caben sin generación (botón de captura): " + (now == 1 ? "va " : "van ") + t;
            else if (now < dlss5Passes) t += " (" + Pasadas(dlss5Passes) + " no caben en este modo)";
            string note = cfg.EngineDlss5Now ? "motor propio" : cfg.D5PassNote; // (20-11: con el motor, nada de lo de Magpie) (16-11: lo que se quita para que quepan estas pasadas, p. ej. "ligero: sin antiparpadeo, sin enfoque")
            if (note.Length > 0) t += " · " + note;
            return t;
        }
        string ScalerText() { PrepareCfg(); return cfg.ScalerShownName; }

        // DLSS 5 si/no: DLSSNR en la cadena de Magpie o no
        void SetDlss5State(bool on)
        {
            dlss5Shown = on;
            cfg.Dlss5Off = !on;
            SaveText(Paths.Dlss5OnFile, on ? "1" : "0");
            ApplyEdgeShader(); // (10-11: con DLSS 5 apagado va el filtro de tramado fuerte)
        }

        // 10-11: el shader de bordes y tramado que toca (Config.EdgeShaderNow) a mpv por la tuberia, si esta en un juego (en el HOME a
        // 4K no lleva ninguno; al entrar en un juego lo pone SwitchMpvInPlace). Solo si cambia: mpv rehace sus shaders (una imagen)
        string edgeShaderSet;
        void ApplyEdgeShader()
        {
            if (!ipcUp || cfg.HomeNative) return;
            string sh = cfg.EdgeShaderNow;
            if (sh == edgeShaderSet) return;
            edgeShaderSet = sh;
            lock (cmdQueue) cmdQueue.Add(JsonCmd("set", "glsl-shaders", sh));
        }

        // 10-11: L3 del mando = filtro de tramado nuevo / el de antes, en caliente, para compararlos en el juego de verdad (antes L3
        // repetia lo de A: DLSS 5 si/no). Se guarda para todos los juegos (games\strong_dither.txt)
        void ToggleStrongDither()
        {
            Process p = mpv;
            if (p == null || p.HasExited || GetForegroundWindow() != p.MainWindowHandle) return;
            if (InHome || homeBusy) { if (!applying) Osd(MenuTexto + " · el filtro de tramado se cambia dentro de cada juego"); return; }
            if (cfg.StrongDither <= 0 || !File.Exists(Paths.EdgeAaShaderStrong) || !cfg.EdgeAa || !cfg.Dedither)
            {
                // (23-11, 1.2.4 SINMPV: el filtro fuerte era un shader de mpv -scripts\\s2_aa_strong.glsl-; el motor lleva el normal)
                Osd(File.Exists(Paths.EdgeAaShaderStrong) ? "Filtro de tramado nuevo desactivado en el ini" : "Filtro de tramado fuerte: era de mpv; el motor lleva el normal");
                return;
            }
            cfg.StrongDitherOn = !cfg.StrongDitherOn;
            SaveText(Paths.StrongDitherFile, cfg.StrongDitherOn ? "1" : "0");
            ApplyEdgeShader();
            bool active = cfg.EdgeShaderNow == Paths.EdgeAaShaderStrong;
            Log.Write("Mando: filtro de tramado " + (cfg.StrongDitherOn ? "nuevo" : "de antes") +
                      (cfg.StrongDitherOn && !active ? " (con DLSS 5 encendido sigue el de antes)" : ""));
            Osd(!cfg.StrongDitherOn ? "Filtro de tramado · el de antes" :
                active ? "Filtro de tramado · nuevo (niebla y zonas oscuras)" : "Filtro de tramado · nuevo, solo con DLSS 5 apagado");
        }

        // R3: el escalador normal (escalador=, DLSS) o el alternativo (escalador_alternativo=, RTX Video), para comparar en el juego
        // de verdad. Se reabre Magpie
        void ToggleScaler()
        {
            Process p = mpv;
            if (p == null || p.HasExited || GetForegroundWindow() != p.MainWindowHandle) return;
            if (passesBusy || relaunchSize != null || !lsReady) return;
            if (!cfg.UpscaleOn && !cfg.AltScalerOn)
            {
                if (!applying) Osd("Captura 4K · no hay escalado que cambiar");
                return;
            }
            // (1.2.02) con FSR 1 / sin reescalado elegidos en el menu (o por el aviso de fps bajos) R3 no cambiaba nada: R3 los quita y va
            // al escalador normal; despues alterna como siempre
            if (scalerPick.Length > 0) SetScalerChoice(true);
            else SetScalerChoice(!dlssScalerOn);
            Log.Write((applying ? "Perfil" : "Mando") + ": escalador " + ScalerText() + ReopenNote);
            SaveCurProfile();
            if (!applying) RestartLs("Escalador · " + ScalerText(), "Un momento…");
        }

        // ---- Generacion de fotogramas con el boton de captura del mando ----
        // El GameSir Nova Lite manda su boton de captura por el teclado de su receptor: Win DERECHA + Impr Pant (medido el
        // 02-11 con Raw Input: VID_3537 MI_01, E0 5C + Impr Pant). Los teclados normales usan la Win izquierda: asi se sabe que
        // es el mando. Con la imagen de la consola delante se lo queda la app (Windows no hace la captura de pantalla) y
        // enciende o apaga la generacion de fotogramas de LS. El boton M no manda nada al PC (ni XInput, ni teclado, ni HID):
        // es del propio mando (asignar botones, turbo). 18-11 (MANDOS): el mismo boton se ve tambien por Raw Input con su dispositivo
        // (VID_3537, src\Mandos.cs): si este gancho no lo hace en 0,2 s (Windows quita los ganchos lentos), lo hace GamepadLoop
        // (PadCaptureButton) y el gancho se vuelve a poner. El gancho sigue siendo el que se lo quita a Windows (Raw Input no puede)
        delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [StructLayout(LayoutKind.Sequential)] struct KbdLL { public uint Vk, Scan, Flags, Time; public IntPtr Extra; }
        LowLevelKeyboardProc keyProc;   // (referenciado: si lo recoge el GC, el gancho se cae)
        IntPtr keyHook;
        bool rwinDown;
        DateTime rwinUp = DateTime.MinValue, lastShotKey = DateTime.MinValue;
        volatile IntPtr mpvWindow;      // ventana de mpv (WatchLoop), para no tocar nada mas desde el gancho
        volatile bool fgWanted = true;  // generacion de fotogramas pedida (games\framegen.txt y el perfil del juego)
        int fgBusy;

        void InstallKeyHook()
        {
            keyProc = KeyHook;
            keyHook = SetWindowsHookEx(13, keyProc, GetModuleHandle(null), 0); // WH_KEYBOARD_LL, en el hilo de la interfaz
            if (keyHook == IntPtr.Zero) Log.Write("Aviso: no se pudo vigilar el boton de captura del mando (" + Marshal.GetLastWin32Error() + ")");
            ganchoTeclaUlt = Environment.TickCount;   // (21-11, 1.2.1: recien puesto; MenuJuego.cs GanchosVigila)
        }

        IntPtr KeyHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            ganchoUlt = Environment.TickCount;   // (1.0.12: vigilante de los ganchos, MenuJuego.cs GanchosVigila)
            ganchoTeclaUlt = ganchoUlt;          // (21-11, 1.2.1: el de cada gancho, frente a lo que ve Raw Input)
            try
            {
                if (nCode >= 0)
                {
                    KbdLL k = (KbdLL)Marshal.PtrToStructure(lParam, typeof(KbdLL));
                    // (inyectadas con la marca 0x5332 en dwExtraInfo cuentan como del mando: para probarlo sin el)
                    bool up = (k.Flags & 0x80) != 0, injected = (k.Flags & 0x10) != 0 && k.Extra != (IntPtr)0x5332;
                    if (TeclasLog && !up) { uint vk0 = k.Vk, fl0 = k.Flags; long ex0 = k.Extra.ToInt64(); bool c0 = (GetAsyncKeyState(0x11) & 0x8000) != 0; ThreadPool.QueueUserWorkItem(delegate { Log.Write("Tecla 0x" + vk0.ToString("X2") + " flags 0x" + fl0.ToString("X") + " extra 0x" + ex0.ToString("X") + " ctrl " + c0 + " delante " + FgOwner()); }); }
                    // 18-11 (MANDOS, mando_bloquear_joyxoff): con la imagen de la consola delante, las teclas INYECTADAS que no sean de la app
                    // (las de Joyxoff) se tragan aqui. Las fisicas (y el boton del GameSir, que es un teclado de verdad) no llevan 0x10: pasan
                    // (1.0.12: nunca un "soltar": tragarse el Alt arriba del truco del primer plano del motor -keybd_event sin marca, otro proceso-
                    // dejaba Alt PULSADO para Windows: sin Ctrl+O / G / F..., la tecla Windows rara. Soltar una tecla no hace nada que bloquear)
                    if (!up && ShouldSwallowInjected((k.Flags & 0x10) != 0, k.Extra)) return (IntPtr)1;
                    // 19-11 (el usuario: "permite que si hago alt f4 con el teclado se cierre la app"): Alt+F4 sobre la imagen de la consola (el
                    // motor, mpv, Magpie escalando) o sobre la pantalla de carga / el telon cierra la app entera, como "Salir" de la bandeja. Antes
                    // cerraba solo la ventana del motor y la app lo volvia a abrir ("mpv se ha cerrado solo", 19-11 02:37:24). LLKHF_ALTDOWN = 0x20
                    if (!up && k.Vk == 0x73 && (k.Flags & 0x20) != 0 && !userExit && EsVentanaDeLaApp(GetForegroundWindow()))
                    {
                        if (!altF4Pedido)
                        {
                            altF4Pedido = true;
                            Log.Write("Alt+F4 con el teclado sobre la imagen de la consola: se cierra la app");
                            OnUi(delegate { QuitRequested(); });
                        }
                        return (IntPtr)1; // (la ventana no lo recibe: el motor no se cierra solo para volver a abrirse)
                    }
                    // 21-11 (1.0.10, Reddit: sin mando): Ctrl+F / G / M / X / 1-4 / R / D / I sobre la imagen = lo de los botones (src\Lanzador\MenuJuego.cs;
                    // 1.2.2: Ctrl+G, el selector de generacion como L3, va alli con los demas: antes iba aqui aparte, sin la espera ni la imagen delante)
                    if (!up && !injected && AjustesTecla((uint)k.Vk)) return (IntPtr)1;   // (1.0.11: con el menu de ajustes abierto, flechas / Intro / Esc)
                    if (!up && !injected && TeclaCe((uint)k.Vk)) return (IntPtr)1;
                    if (!injected && k.Vk == 0x5C) // Win derecha
                    {
                        rwinDown = !up;
                        if (up) rwinUp = DateTime.Now;
                    }
                    // Impr Pant con la Win derecha pulsada o recien soltada (el mando suelta la Win antes que Impr Pant)
                    else if (!injected && k.Vk == 0x2C && (rwinDown || (DateTime.Now - rwinUp).TotalMilliseconds < 400) &&
                             mpvWindow != IntPtr.Zero && GetForegroundWindow() == mpvWindow)
                    {
                        if (!up && rwinDown)
                        {
                            // Una tecla de por medio (sin uso, 0xE8) para que soltar la Win no abra el menu Inicio (marcada como de la
                            // app para que el bloqueo de Joyxoff no se la trague)
                            keybd_event(0xE8, 0, 0, FillerMark);
                            keybd_event(0xE8, 0, KEYEVENTF_KEYUP, FillerMark);
                        }
                        if ((DateTime.Now - lastShotKey).TotalMilliseconds > 600)
                        {
                            lastShotKey = DateTime.Now;
                            ThreadPool.QueueUserWorkItem(delegate { ToggleFrameGeneration(); });
                        }
                        return (IntPtr)1; // ni captura de Windows ni nada
                    }
                }
            }
            catch (Exception ex)
            {
                // (1.0.12: antes catch vacio: un fallo aqui dejaba los atajos sin hacer nada y sin rastro)
                if ((DateTime.Now - keyHookErrAt).TotalSeconds > 5) { keyHookErrAt = DateTime.Now; Exception e2 = ex; ThreadPool.QueueUserWorkItem(delegate { Log.Error("Gancho del teclado", e2); }); }
            }
            return CallNextHookEx(keyHook, nCode, wParam, lParam);
        }

        bool altF4Pedido;
        DateTime keyHookErrAt = DateTime.MinValue;

        // 19-11: ¿la ventana es de la app? (la suya: pantalla de carga, telon, avisos; el motor o mpv que dan la imagen, el motor en espera o del
        // relevo; Magpie escalando). Lo que no sea de la app (Playnite, el navegador...) sigue cerrandose con Alt+F4 como siempre
        bool EsVentanaDeLaApp(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            if (h == mpvWindow) return true;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == 0) return false;
            if (pid == (uint)Process.GetCurrentProcess().Id) return true;
            try
            {
                Process m = mpv;
                if (m != null && !m.HasExited && pid == (uint)m.Id) return true;
                using (Process p = Process.GetProcessById((int)pid))
                {
                    string n = p.ProcessName;
                    return string.Equals(n, "ConsoleEnhancerEngine", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(n, "mpv", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        // "Zelda · 1440p · 60 → 120 fps · DLSS 5 nivel 4 · 1 pasada": lo puesto, para el aviso en pantalla
        string StatusText()
        {
            if (cfg.Mode120) // 20-11 (HZ120): esports, imagenes reales a 120, sin generacion, baja latencia
            {
                string up120 = cfg.UpscaleOn ? " · " + (Config.EngineActive ? (cfg.ScalerNow == "dlss" ? "DLSS " + Config.EngineModelName(cfg.EnginePresetNow) : "Catmull-Rom") : cfg.ScalerName(cfg.ScalerNow)) : "";
                return (curGame ?? "Juego") + " · " + SizeName(cfg.VideoSize) + " · 120 fps (esports) · sin generación · Reflex" + (Config.EngineActive ? " · motor propio" : "") + up120;
            }
            int fps = cfg.CapMode ? 30 * (fgWanted ? 2 : 1) : 60 * (fgWanted ? 2 : 1);
            if (Config.EngineActive)
                return (curGame ?? "Juego") + " · " + SizeName(cfg.VideoSize) + " · " +
                       (fgWanted ? (cfg.CapMode ? "30" : "60") + " → " + fps + " fps (" + (cfg.EngineSmoothMode > 0 ? "Smooth Motion" : "DLSS-G + Reflex") + ")" : fps + " fps sin generación · Reflex") +
                       " · motor propio · " + (cfg.ScalerNow == "dlss" ? "DLSS " + Config.EngineModelName(cfg.EnginePresetNow) : "Catmull-Rom, sin DLSS") +
                       (!dlss5Shown ? " · DLSS 5 desactivado" : " · DLSS 5 no cabe en este modo");
            return (curGame ?? "Juego") + " · " + SizeName(cfg.VideoSize) + " · " +
                   (fgWanted ? (cfg.CapMode ? "30" : "60") + " → " + fps + " fps (" + cfg.FgName + ")" : fps + " fps sin generación") + " · " +
                   (!dlss5Shown ? "DLSS 5 desactivado" : !Integrado.Dlss5Available ? Integrado.Dlss5MissingText : !cfg.Dlss5FitsNow ? "DLSS 5 no cabe en este modo (se quita)" : "DLSS 5 nivel " + dlss5Level + " · " + Pasadas(cfg.Dlss5PassesNow) + (cfg.Dlss5PassesNow < dlss5Passes ? " (" + dlss5Passes + " no caben)" : "") + " · " + (cfg.Dlss5PctShownNow > 0 ? cfg.Dlss5PctShownNow + " %" : "adaptativo") + (cfg.EngineDlss5Now ? " · motor propio" : "")) +
                   (cfg.UpscaleOn ? " · " + cfg.ScalerName(cfg.ScalerNow) : "");
        }

        void ToggleFrameGeneration()
        {
            lastPadAction = DateTime.Now;

            SetFrameGen(!fgWanted);
        }

        // Generacion de fotogramas si / no (boton de captura del mando). Se reabre Magpie
        void SetFrameGen(bool on)
        {
            if (InHome || homeBusy)
            {
                if (!applying) Osd(MenuTexto + (cfg.HomeNative ? " en 4K nativo" : "") + " · la generación se activa dentro de cada juego");
                return;
            }
            Process p = mpv;
            if (p == null || p.HasExited || passesBusy || relaunchSize != null || !lsReady) return;
            if (Interlocked.CompareExchange(ref fgBusy, 1, 0) != 0) return;
            try
            {
                if (on && !cfg.FgAvailable) { Osd(Deteccion.Tx.T("Generación de imágenes no disponible con esta gráfica", "Frame generation not available on this GPU")); return; } // (ConsoleEnhancer)
                fgWanted = on;
                SaveText(Paths.FgFile, on ? "1" : "0");
                Log.Write((applying ? "Perfil" : "Mando") + ": generacion de fotogramas " + (on ? "encendida" : "apagada") + ReopenNote);
                SaveCurProfile();
                RestartLs(on ? "Generación de fotogramas activada" : "Generación de fotogramas desactivada",
                          on ? cfg.FgName + " · imágenes intercaladas entre las reales" : "Solo las imágenes reales de la consola");
            }
            finally { Interlocked.Exchange(ref fgBusy, 0); }
        }

        void ChangeLevel(int delta) { SetLevel(Math.Max(1, Math.Min(4, dlss5Level + delta))); }

        // L1 / L2 / R2 / R1 = nivel 1 / 2 / 3 / 4. Con Magpie el nivel va en su config: se reabre (con LS era en caliente)
        void SetLevel(int n)
        {
            n = Math.Max(1, Math.Min(4, n));
            if (n == dlss5Level) { Osd("DLSS 5 · Nivel " + n + " de 4 · ya activo" + (cfg.Dlss5Off ? " (desactivado)" : "")); return; }
            if (!applying && (passesBusy || relaunchSize != null || !lsReady)) return;
            dlss5Level = n;
            SaveText(Paths.Dlss5LevelFile, n.ToString(CultureInfo.InvariantCulture));
            Log.Write((applying ? "Perfil" : "Mando") + ": DLSS 5 nivel " + n + (cfg.Dlss5Off ? " (DLSS 5 apagado: se aplicara al encenderlo)" : ReopenNote));
            SaveCurProfile();
            if (applying) return;
            if (cfg.Dlss5Off) { Osd("DLSS 5 · Nivel " + n + " de 4 · se aplicará al activarlo"); return; }
            RestartLs("DLSS 5 · Nivel " + n + " de 4", Dlss5PassText());
        }

        // ---------------- Ajustes por juego ----------------
        // El juego se reconoce por su nombre en el HOME (texto azul encima del icono seleccionado, leido con el OCR de
        // Windows). Al dejar de verse el HOME (o pasar la imagen a un juego a 30 fps) se ha entrado en el ultimo juego
        // seleccionado: vuelven sus ajustes. Lo que se cambia con el mando se guarda en el juego en el que se esta
        string curGame;                 // juego en el que se esta (null: no se sabe, p. ej. la app se abrio ya jugando)
        string homeGame;                // nombre del juego seleccionado en el HOME (null: no se esta en el HOME)
        DateTime homeSeen = DateTime.MinValue, lastGrab = DateTime.MinValue, slowSince = DateTime.MinValue;
        int noLabel;                    // capturas seguidas sin nombre y sin el HOME
        volatile bool grabBusy;
        // poniendo los ajustes de un juego (no se guardan como eleccion). 21-11 (1.2.1): un contador y no un bool: EnterGame / LeaveHomeGrace y
        // el "Aplicar" del menu de ajustes (otro hilo) a la vez se quitaban el "applying" uno a otro y lo que quedaba del segundo se hacia
        // como si fuera del mando (RequestCapture / SetModeN podian no hacer nada con el perfil ya guardado)
        int applyingN;
        bool applying
        {
            get { return Thread.VolatileRead(ref applyingN) > 0; }
            set { if (value) Interlocked.Increment(ref applyingN); else if (Interlocked.Decrement(ref applyingN) < 0) Interlocked.Exchange(ref applyingN, 0); }
        }
        volatile bool pendingPassesRestart;
        readonly object homeSync = new object();
        static readonly string GrabPath = Path.Combine(Path.GetTempPath(), "switch2_grab.raw");

        // ---- 15-11: que los perfiles se creen y se guarden bien ----
        // El nombre del juego seleccionado se lee en varias capturas (NameReads), con la captura entera mientras haga falta (la
        // ligera de 1280x720 sale de 1 de cada 3 pixeles de la de 4K, sin filtrar: letras peores para el OCR)
        NameReads selReads;             // lecturas del nombre del juego seleccionado en el HOME (otro icono: otras)
        volatile bool nameFullGrab;     // la siguiente captura entera (GrabTick)
        bool enterSure;                 // el nombre del juego en el que se entra esta confirmado (DecideHomeNative -> EntryProfile)
        bool enterConfirmed;            // 16-11 (revision): y lo esta del todo (NameReads.Sure, no solo Firm): solo esto marca NameSure
        Rectangle sysBox;               // donde estaba el nombre de la ultima casilla de la consola (con sysShape)
        volatile bool curGameGuess;     // curGame puesto al abrir la app sin ver el HOME (el ultimo juego o el pendiente)
        string staleWarned;             // ya se aviso de que el juego en uso no era este curGame
        int diagHomeSaved;              // imagenes guardadas en games\diag_home en esta sesion
        // curGame y los ajustes que se guardan con el van juntos: SaveCurProfile (hilo del mando) no puede guardar los ajustes del
        // juego de antes con el nombre del nuevo mientras se entra en otro (LeaveHomeGrace cambiaba curGame y luego ponia applying)
        readonly object profileSync = new object();
        // El juego en el que se iba a entrar (elegido en el HOME, esperando a que cargue). Si la app se cierra antes de confirmar la
        // entrada (14-11 23:57: una pantalla "No Signal" al arrancar RESIDENT EVIL requiem la cerro) y se reabre sin ver el HOME,
        // se pone este y no el ultimo juego (last_game.txt: Red Dead Redemption, con el que se guardaron 6 cambios del mando)
        static readonly string PendingGameFile = Path.Combine(Paths.Datos, @"games\pending_game.txt");
        bool pendingSaved;

        // HOME a 4K nativo (Config.HomeNative): con un juego en marcha, el HOME visto en 2 capturas seguidas = se ha vuelto
        // al HOME (se cierra LS y la imagen pasa a 4K tal cual); en el HOME, la consola deja de mostrarlo con un juego
        // seleccionado = se entra en ese juego (sus ajustes y LS). Recien abierta la app se esta en el HOME a 4K: si no se
        // ve el HOME y si un juego en marcha, ya se estaba jugando (al ultimo juego, games\last_game.txt)
        // 09-11: los ajustes van solo cuando se ha entrado DE VERDAD (antes se ponian al dejar de verse el HOME: encima del
        // aviso de "cerrar el programa" o de la pantalla de elegir usuario, tapadas por el telon). Cada captura fuera del
        // HOME es una de estas (OutGrab): el HOME atenuado (un aviso encima), una pantalla de la consola (OCR: elegir
        // usuario, cerrar el programa, bloqueo...), negro (cargando) o imagen de juego. El juego en uso (sale "Cambiar de
        // usuario" bajo su casilla) se reanuda sin elegir usuario: entra en cuanto no se ve el HOME. Uno que no esta en uso:
        // tras imagen de juego en capturas seguidas, o negro tras la pantalla de elegir usuario. Ver HomeTick
        const int KHome = 0, KBlack = 1, KDim = 2, KConsole = 3, KGame = 4;
        int homeHits;                   // capturas seguidas con el HOME mientras se juega
        bool labelNoName;               // la ultima captura del HOME tenia un juego seleccionado sin nombre leido
        volatile bool homeBusy;         // pasando del HOME al juego o al reves
        bool homeInUse;                 // en el HOME: el juego seleccionado esta en uso ("Cambiar de usuario" debajo)
        DateTime inUseAt = DateTime.MinValue; // la ultima captura del HOME en la que se vio en uso
        DateTime outSince = DateTime.MinValue;   // fuera del HOME desde (primera captura sin el HOME de esta salida)
        DateTime blackSince = DateTime.MinValue; // fuera del HOME: desde cuando se ve negro seguido
        int gameHits;                   // fuera del HOME: capturas seguidas con imagen de juego (el negro corta)
        int outHits;                    // fuera del HOME: capturas seguidas que no son de la consola (juego o negro)
        bool pickerSeen;                // fuera del HOME: se ha visto la pantalla de elegir usuario
        bool consoleSeenOut;            // fuera del HOME: se ha visto otra pantalla de la consola o el HOME atenuado
        bool otherConsoleOut;           // 17-11: fuera del HOME: una pantalla de la consola que NO es el aviso de cerrar el suspendido (ni el HOME atenuado)
        bool blackSeenOut;              // 15-11: fuera del HOME: se ha visto negro (aunque luego haya imagen)
        // 15-11: se acaba de elegir usuario y el HOME que se ve despues es de paso (al elegir usuario la consola enseña el HOME ~0,5 s
        // y luego carga en negro): sigue valiendo la regla de 1 s de negro tras elegir usuario. Antes ResetOut lo borraba en ese HOME
        // y siempre tocaba la de 4 s ("tras elegir usuario" no salia nunca en el log: 13:04:16, 19:18:09, 19:23:55)
        bool pickerBefore;
        DateTime homeBackAt = DateTime.MinValue; // la primera captura del HOME tras una salida (ResetOut)
        const double PickerFlashSecs = 1.5;      // lo que puede durar ese HOME de paso (medido: 0,45-0,6 s hasta la siguiente salida)
        // 15-11: la casilla vista en uso en esta visita al HOME = el juego en marcha (suspendido): ver DecideHomeNative
        string inUseGame;
        double[] inUseIcon;
        int lastKind = -1;              // lo ultimo visto fuera del HOME (KHome...)
        bool outLogged;                 // ya se dijo en el log que se espera a entrar de verdad
        double[] lastThumb;             // miniatura de la captura anterior (para ver si se mueve)
        int ocrState = -1;              // la captura anterior por OCR: 1 pantalla de la consola, 0 no, -1 sin OCR (una quieta lo repite)
        bool ocrPicker;                 // (y si era la de elegir usuario)
        int startHits, startMoves;      // recien abierta la app, sin ver el HOME: capturas de juego seguidas y cuantas se movian
        DateTime startFirst = DateTime.MinValue; // la primera de esas capturas
        bool startWoke;                 // recien abierta la app no se veia la consola (apagada, en reposo, arrancando o negro)
        int startKind = -1;             // lo ultimo visto recien abierta la app (para el log)

        // 10-11 (home_espera_nativo_s): en el HOME pero aun con LS escalando y mpv con lo del juego (el HOME se ve con el escalado
        // del juego). Todo lo del HOME (que juego esta seleccionado, si se entra de verdad...) funciona igual que a 4K nativo
        // (InHome); lo que es de la imagen (LS, ventana, captura), como jugando (cfg.HomeNative = false)
        volatile bool homeGrace;
        DateTime graceSince = DateTime.MinValue;    // desde cuando (el HOME visto jugando)
        DateTime graceMoved = DateTime.MinValue;    // la ultima captura del HOME distinta de la anterior (se movia el cursor)
        DateTime graceLastHome = DateTime.MinValue; // la ultima captura en la que se vio el HOME
        double[] graceThumb;                        // miniatura del HOME de la captura anterior
        volatile double[] lastHomeThumb;            // la del ultimo HOME visto con LS escalando (la imagen de mpv, sin LS)
        DateTime lastHomeThumbAt = DateTime.MinValue;
        bool InHome { get { return cfg.HomeNative || homeGrace; } }

        // ¿Toca pasar ya al HOME a 4K nativo? (con homeSync) Tras home_espera_nativo_s: en el HOME, viendolo ahora y con la imagen
        // quieta home_espera_quieto_s (la imagen se congela un momento al cambiar); o fuera del HOME sin entrada posible (Ajustes,
        // eShop, Album... desde la barra: no se vuelve al juego por ahi). A las 3 veces el tiempo, sin condiciones (revision 10-11:
        // si no, en una pantalla de la consola LS seguia para siempre, con el mando bloqueado)
        string graceWhy;
        bool GraceDue(DateTime now)
        {
            if (!homeGrace || cfg.HomeGraceSecs <= 0) return false;
            double inHome = (now - graceSince).TotalSeconds;
            if (inHome < cfg.HomeGraceSecs) return false;
            // (15-11: tope a los home_espera_nativo_s + 1,5 s aunque se mueva el cursor; antes hasta 3 veces: 18 s de HOME escalado)
            if (inHome >= cfg.HomeGraceSecs + 1.5) { graceWhy = "tope de " + Ms(cfg.HomeGraceSecs + 1.5) + " s"; return true; }
            if (noLabel > 0 || outSince != DateTime.MinValue)
            {
                bool entryPossible = homeGame != null || labelNoName || (!homeDock && homeSeen != DateTime.MinValue);
                if (entryPossible) return false; // (se espera a ver si se entra de verdad)
                graceWhy = "fuera del HOME en una pantalla de la consola";
                return true;
            }
            if ((now - graceLastHome).TotalSeconds > 1.5) return false; // (sin capturas recientes del HOME no se sabe que se ve)
            if ((now - graceMoved).TotalSeconds < cfg.HomeGraceStillSecs) return false;
            graceWhy = "HOME quieto";
            return true;
        }

        // Revision 10-11: mpv se va a relanzar (corte de señal, reinicio de la grafica) estando en el HOME con LS: se relanza ya en el
        // HOME a 4K nativo (EnsureLossless cierra LS) en vez de volver a abrir LS, DLSS 5 y la generacion para el HOME
        void GraceOffForRelaunch(string why)
        {
            if (!homeGrace) return;
            lock (homeSync)
            {
                homeGrace = false;
                cfg.HomeNative = true;
            }
            pendingPassesRestart = false;
            Log.Write("HOME (homeGrace) y se relanza (" + why + "): se vuelve a abrir ya en el HOME a 4K nativo");
        }

        // Del HOME visto con LS al HOME a 4K nativo (la imagen congelada y un fundido). Lo del HOME (juego seleccionado...) sigue
        void GraceToNative()
        {
            if (!homeGrace || homeBusy || passesBusy || relaunchSize != null) return;
            Log.Write("HOME: " + (int)(DateTime.Now - graceSince).TotalSeconds + " s en el HOME sin volver al juego (" + graceWhy + "): a 4K nativo, sin Magpie" +
                      (curGame != null ? " (\"" + curGame + "\" sigue con sus ajustes guardados)" : ""));
            pendingPassesRestart = false;
            StartHomeSwitch(true, "HOME", "4K nativo", true);
        }

        readonly object homeTickSync = new object();
        void RunHomeTick(DateTime now)
        {
            if (!Monitor.TryEnter(homeTickSync)) return;
            try { HomeTick(now); } finally { Monitor.Exit(homeTickSync); }
        }

        // Cada 0,5 s (WatchLoop, con LS listo)
        void HomeTick(DateTime now)
        {
            // 19-11: la PS5 (menu propio, src\MenuPs5.cs): nada del HOME de la Switch 2
            if (Consolas.Actual.MenuPropio) { MenuPs5Tick(now); return; }
            string enter = null;
            bool goHome = false, goNative = false;
            lock (homeSync)
            {
                if (homeBusy || passesBusy || relaunchSize != null) { }
                else if (InHome)
                {
                    enter = DecideHomeNative(now);
                    if (enter == null && GraceDue(now)) goNative = true; // (10-11: del HOME con LS al de 4K nativo)
                }
                else if (cfg.HomeNativeEnabled && homeHits >= 2)
                {
                    goHome = true;
                    homeHits = 0;
                }
                else if (!cfg.HomeNativeEnabled && homeGame != null)
                {
                    // (en el modo 30 fps todo va a 30: solo cuenta que deje de verse el HOME)
                    if (vfFps < 45 && !cfg.CapMode) { if (slowSince == DateTime.MinValue) slowSince = now; }
                    else slowSince = DateTime.MinValue;
                    if (noLabel >= 3 || (slowSince != DateTime.MinValue && (now - slowSince).TotalSeconds >= 5))
                    {
                        enter = homeGame;
                        homeGame = null;
                        noLabel = 0;
                        slowSince = DateTime.MinValue;
                    }
                }
            }
            if (goHome) EnterHomeNative();
            else if (goNative) GraceToNative();
            else if (enter != null) EnterGame(enter);
            ApplyPending();
        }

        // En el HOME a 4K: ¿se ha entrado de verdad en un juego? (con homeSync). El juego ("" sin nombre) o null
        string DecideHomeNative(DateTime now)
        {
            string enter = null;
            // En el HOME con un juego seleccionado (sel) o sin haber visto cual (unread: se entro antes de la primera
            // lectura; a un juego sin identificar, con los ajustes de ahora, y si fue una falsa alarma de HOME en plena
            // partida, asi vuelve solo). No con el cursor en la barra de abajo (eShop, ajustes)
            bool sel = homeGame != null || labelNoName;
            bool unread = !sel && !homeDock && homeSeen != DateTime.MinValue;
            // 15-11: la casilla que se ve en uso en esta visita al HOME es el juego en marcha (ver running). 16-11: la de la fila del HOME
            // ("Cambiar de usuario" debajo); en la biblioteca el "en uso" sale de esto mismo, no lo confirma
            if (noLabel == 0 && homeInUse && homeGame != null && !homeDock && !homeLib) { inUseGame = homeGame; inUseIcon = homeIcon; }
            if (noLabel >= 1 && outSince != DateTime.MinValue && (sel || unread))
            {
                // HOME y vuelta al juego en uso sin leer ningun otro nombre (08-11: 4 veces; con "(sin nombre)" no se
                // guardaba nada de lo cambiado con el mando)
                bool back = unread && curGame != null && (outSince - homeSeen).TotalSeconds < 10;
                bool resume = (sel && homeInUse) || back;
                // 15-11 (19:26: Ocarina con los ajustes de Arceus, y lo cambiado con el mando guardado en Arceus): con un juego en marcha
                // (suspendido en el HOME: la casilla vista en uso en esta visita, si no curGame), entrar en OTRO pasa siempre por la
                // consola: el aviso de cerrar el programa suspendido, la pantalla de elegir usuario o negro (cargando). Solo imagen de
                // juego enseguida con el cursor en otra casilla (se ha vuelto con el boton HOME) = de vuelta al juego en marcha. Antes
                // valian 3 capturas de imagen de juego y se entraba en la casilla del cursor
                string running = inUseGame ?? curGame;
                bool other = sel && !homeInUse && !string.IsNullOrEmpty(running) && (homeGame == null || !GameProfiles.SameGame(homeGame, running));
                // 17-11 (12:25: Ocarina en marcha, cursor en Red Dead, el aviso de cerrar Ocarina y vuelta a Ocarina sin pasar por el HOME: se
                // pusieron los ajustes de Red Dead): el aviso de cerrar el suspendido (y el HOME atenuado que lo acompaña) ya no quita la vuelta
                // al juego en marcha. Si se confirma, el juego nuevo arranca con negro y/o elegir usuario; sin nada de eso, solo imagen de
                // juego = se ha vuelto al que estaba en marcha
                bool backRunning = other && cfg.EnterWaitUser && !pickerSeen && !otherConsoleOut && !blackSeenOut;
                bool afterPicker = pickerSeen || pickerBefore; // (elegido usuario en esta salida o justo antes de un HOME de paso)
                string what = sel ? (homeGame != null ? "\"" + homeGame + "\"" : "un juego nuevo (sin nombre leido)")
                                  : back ? "\"" + curGame + "\"" : "un juego sin identificar";
                double outS = (now - outSince).TotalSeconds;
                double blackS = blackSince != DateTime.MinValue ? (now - blackSince).TotalSeconds : 0;
                string why = null;
                bool byRunning = false;
                if (resume || !cfg.EnterWaitUser)
                {
                    // En uso: se reanuda tal cual, sin elegir usuario. Basta con que no se vea el HOME (ni atenuado). 16-11: elegido en la
                    // biblioteca (en uso por ser el juego en marcha), una captura mas y con OCR (wantText): la animacion de vuelta al HOME no
                    // es entrar
                    int need = cfg.EnterWaitUser ? cfg.EnterInUseGrabs + (sel && homeLib ? 1 : 0) : 3;
                    // 16-11 tarde (APAGADO): sin ninguna imagen de juego en esta salida (solo negro), 3 capturas mas: el HOME atenuado y negro
                    // de ponerla en espera con el juego en uso seleccionado se tomaba por la vuelta a el (15-11 11:39:28, 12:17:13: "de vuelta"
                    // ~1 s antes de "No Signal", se cambiaba la imagen al juego y se esperaba como jugando en vez de cerrar enseguida). Al
                    // reanudarlo se ve su imagen (el HOME se amplia con ella): igual que antes
                    if (!gameSeenOut) need += 3;
                    if (outHits >= need)
                        why = back ? "de vuelta al juego en uso sin leer otro" :
                              homeInUse ? "estaba en uso, se reanuda sin elegir usuario" : "fuera del HOME";
                }
                else if (gameHits >= cfg.EnterGrabs)
                {
                    byRunning = backRunning;
                    why = byRunning ? "de vuelta al juego en marcha: solo imagen de juego (" + gameHits + " capturas), sin el aviso de cerrar el " +
                                      "programa suspendido, sin elegir usuario y sin negro; el cursor estaba en " + what
                                    : "imagen del juego en " + gameHits + " capturas seguidas" + (afterPicker ? " tras elegir usuario" : "");
                }
                else if (afterPicker && blackS >= cfg.EnterBlackSecs)
                    why = "negro " + blackS.ToString("0.0", CultureInfo.InvariantCulture) + " s tras elegir usuario (cargando)";
                else if (!consoleSeenOut && blackS >= cfg.EnterBlackLongSecs)
                    why = "negro " + blackS.ToString("0.0", CultureInfo.InvariantCulture) + " s sin pantalla de usuario (cargando)";
                else if (outS >= cfg.EnterMaxSecs && lastKind != KDim && (lastKind != KConsole || outS >= 3 * cfg.EnterMaxSecs))
                    why = "tiempo maximo de espera (" + (int)outS + " s)";
                // 16-11: tras el menu de apagado (o el boton de apagado) en esta salida, el negro es la consola apagandose o en espera, no un
                // juego (15-11 22:25:05: "de vuelta" en Ocarina a los 515 ms, con Magpie y su telon, y la consola se puso en espera)
                bool powerOut = powerMenuAt != DateTime.MinValue && powerMenuAt >= outSince;
                if (why != null && powerOut)
                {
                    if (!outLogged) Log.Write("Fuera del HOME tras el menu de apagado: no se entra en " + what + " (" + why + ")");
                    outLogged = true;
                    why = null;
                }
                // 15-11: se va a entrar en el juego elegido: apuntado por si la app se cierra antes de confirmarlo (PendingGameFile)
                // (no mientras todo apunta a la vuelta al juego en marcha)
                if (why == null && sel && !resume && !backRunning && !powerOut) SavePending(homeGame ?? "");
                if (why != null)
                {
                    if (byRunning) what = "\"" + running + "\"";
                    Log.Write("Entrada en " + what + " confirmada: " + why + " (" + (long)(outS * 1000) + " ms fuera del HOME)");
                    if (byRunning)
                    {
                        // El juego en marcha, con el icono de su casilla si se vio en uso (el de la casilla del cursor es de otro juego)
                        enter = running;
                        enterIcon = inUseGame != null ? inUseIcon : null;
                        enterSure = false;
                    }
                    else if (sel)
                    {
                        enter = homeGame ?? "";
                        enterIcon = homeIcon;
                        // (15-11: su nombre esta confirmado si las lecturas de esta seleccion lo dan por seguro)
                        // (16-11: o firme, NameReads.Firm: sin eso no da nombre a un juego nuevo ni cambia el de un perfil)
                        NameReads nr = selReads;
                        enterSure = homeGame != null && nr != null && (nr.Sure || nr.Firm) && nr.Best != null && GameNames.Key(nr.Best) == GameNames.Key(homeGame);
                        // (16-11, revision: firme basta para crear o renombrar, pero el perfil solo queda "confirmado" -NameSure, ya no se
                        // vuelve a leer- con nr.Sure: un nombre largo leido firme pero cortado se seguira leyendo hasta tenerlo entero)
                        enterConfirmed = enterSure && nr.Sure;
                    }
                    else
                    {
                        enter = back ? curGame : "";
                        enterIcon = null;
                        Log.Write(back ? "Fuera del HOME sin leer otro juego: de vuelta en \"" + curGame + "\""
                                       : "Fuera del HOME sin haber visto que juego se elegia: juego sin identificar, con los ajustes de ahora");
                    }
                    homeGame = null;
                    homeIcon = null;
                    labelNoName = false;
                    homeInUse = false;
                    homeLib = false;
                    noLabel = 0;
                    ResetOut(null, now);
                    // 16-11: un juego que arranca (no uno que se reanuda) puede cambiar el modo de video despues (ver NoSignalHold)
                    if (!resume && !byRunning) MarkLaunch(now, "entrada en " + what, true);
                }
                else if (!resume && !outLogged && noLabel >= 2)
                {
                    outLogged = true;
                    Log.Write("Fuera del HOME con " + what + " (no esta en uso" +
                              (other ? "; en marcha \"" + running + "\": si solo se ve imagen de juego, es la vuelta a el" : "") +
                              "): sus ajustes cuando se entre de verdad (tras elegir usuario y empezar a cargar); de momento sigue a 4K nativo");
                }
            }
            // Abierta la app jugando: nunca se ha visto el HOME. Antes 12 capturas sin el HOME (08-11: con 5 = 2,5 s
            // hubo dos falsos al despertar la consola; 09-11 11:05 con 12 otro: la consola encendiendose). Ahora
            // capturas seguidas con imagen de juego que se mueve (no negro, ni pantallas de la consola por OCR: bloqueo,
            // elegir usuario..., ni quietas): inicio_capturas (5, ~2 s); con la consola recien encendida o en negro al
            // abrir, inicio_capturas_al_encender (10). Quieta pero de juego: a los inicio_quieto_s (15 s)
            else if (homeSeen == DateTime.MinValue && consoleOn && startHits > 0)
            {
                int need = startWoke ? cfg.StartGrabsWake : cfg.StartGrabs;
                double stillS = startFirst != DateTime.MinValue ? (now - startFirst).TotalSeconds : 0;
                bool moving = startMoves >= Math.Max(2, need / 2);
                if (startHits >= need && (moving || stillS >= cfg.StartStillSecs))
                {
                    // 15-11: si la app se cerro esperando a entrar en un juego elegido en el HOME (hace menos de 3 min), ese, y no el
                    // ultimo confirmado (14-11 23:58: RESIDENT EVIL requiem cargando y se puso Red Dead Redemption). "" = uno sin nombre
                    // leido: sin identificar, mejor que los ajustes de otro
                    string last = LoadText(Paths.LastGameFile) ?? "", pend = LoadPending(TimeSpan.FromMinutes(3));
                    enter = pend ?? last;
                    enterGuess = true;
                    Log.Write("Sin el HOME desde que se abrio la app: ya se estaba jugando (" + startHits + " capturas de juego seguidas, " +
                              startMoves + " con movimiento, " + stillS.ToString("0.0", CultureInfo.InvariantCulture) + " s" +
                              (startWoke ? "; la consola se acababa de encender o estaba en negro" : "") + ")" +
                              (pend != null ? " (el juego elegido en el HOME justo antes de cerrarse la app: " + (pend.Length > 0 ? "\"" + pend + "\"" : "uno sin nombre leido") +
                                              (last.Length > 0 && last != pend ? "; el ultimo confirmado era \"" + last + "\"" : "") + ")"
                                            : enter.Length > 0 ? " (el ultimo juego: \"" + enter + "\")" : ""));
                    startHits = startMoves = 0;
                    startFirst = DateTime.MinValue;
                }
            }
            if (enter != null) lastEnterAt = now; // (16-11 tarde: hasta volver al HOME, ya no es "en el HOME sin nada arrancando")
            return enter;
        }

        static string KindName(int k)
        {
            return k == KBlack ? "negro" : k == KDim ? "el HOME atenuado (un aviso de la consola encima)" :
                   k == KConsole ? "una pantalla de la consola" : k == KGame ? "imagen de juego" : "el HOME";
        }

        static string ShortText(string s)
        {
            if (s == null) return "";
            s = System.Text.RegularExpressions.Regex.Replace(s.Replace("\r", " ").Replace("\n", " "), " +", " ").Trim();
            return s.Length > 70 ? s.Substring(0, 70) + "..." : s;
        }

        // De vuelta en el HOME (o ya en el juego): nada pendiente de la salida anterior. (con homeSync)
        // 15-11: backTo null = se ha entrado en un juego o se vuelve al HOME desde uno: todo a cero (tambien la casilla en uso). Con el
        // HOME visto otra vez (backTo), lo de haber elegido usuario sigue mientras ese HOME sea de paso (PickerFlashSecs)
        void ResetOut(string backTo) { ResetOut(backTo, DateTime.Now); }

        void ResetOut(string backTo, DateTime now)
        {
            if (outSince != DateTime.MinValue) homeBackAt = now;
            bool keepPicker = backTo != null && (pickerSeen || pickerBefore) && homeBackAt != DateTime.MinValue &&
                              (now - homeBackAt).TotalSeconds < PickerFlashSecs;
            if (outLogged && backTo != null)
                Log.Write("De vuelta en el HOME sin haber entrado en " + backTo + " (" + (long)(now - outSince).TotalMilliseconds +
                          " ms fuera): no se ha puesto nada" +
                          (keepPicker && pickerSeen ? " (se acaba de elegir usuario: si ahora carga, se entra con " + Ms(cfg.EnterBlackSecs) + " s de negro)" : ""));
            outSince = blackSince = DateTime.MinValue;
            gameHits = outHits = outTextLogs = 0;
            pickerSeen = consoleSeenOut = otherConsoleOut = blackSeenOut = outLogged = false;
            gameSeenOut = false; // (16-11 tarde)
            dimLast = false;     // (16-11 tarde: con el HOME a la vista, el siguiente HOME atenuado sale de nuevo: se lee, DimOcrDue)
            pickerBefore = keepPicker;
            if (backTo == null) { inUseGame = null; inUseIcon = null; homeBackAt = DateTime.MinValue; }
            lastKind = KHome;
            // (15-11: ya no se va a entrar en el juego apuntado; la primera vez tambien borra uno de otra sesion)
            if (pendingSaved || !pendingChecked) { pendingChecked = true; ClearPending(); }
        }
        bool pendingChecked;

        // Una captura fuera del HOME (con homeSync): ver HomeTick
        void OutGrab(int kind, string screen, bool picker, DateTime now)
        {
            if (outSince == DateTime.MinValue)
            {
                outSince = now;
                // 15-11: se sale del HOME (se lanza un juego): la consola puede cambiar de modo de video y la capturadora enseña "No Signal"
                // unos segundos (14-11 23:57, RESIDENT EVIL: la app se cerro creyendo la consola apagada). 16-11: ya no aqui (switch2-launch 20
                // tambien tras el menu de apagado), sino tras elegir usuario, el aviso de cerrar el suspendido o al entrar (MarkLaunch); saliendo
                // del HOME por su boton de apagado, lo contrario (PowerMenuSeen, ver NoSignalHold)
                if (powerTile) PowerMenuSeen(now, "saliendo del HOME por el boton de apagado");
            }
            if (kind == KBlack)
            {
                if (blackSince == DateTime.MinValue) blackSince = now;
                blackSeenOut = true;
                gameHits = 0;
                outHits++;
            }
            else
            {
                blackSince = DateTime.MinValue;
                if (kind == KGame) { gameHits++; outHits++; gameSeenOut = true; }
                else
                {
                    gameHits = outHits = 0;
                    if (picker) { pickerSeen = true; MarkLaunch(now, "elegir usuario"); }
                    else
                    {
                        consoleSeenOut = true;
                        string n = (screen ?? "").ToLowerInvariant();
                        if (kind == KConsole && (n.Contains("cerrar") || n.Contains("suspendido") || n.Contains("close")))
                            MarkLaunch(now, "el aviso de cerrar el programa suspendido");
                        else if (kind == KConsole) otherConsoleOut = true; // (17-11: ver backRunning en DecideHomeNative)
                    }
                }
            }
            if (kind != lastKind && (kind == KDim || kind == KConsole || lastKind == KDim || lastKind == KConsole))
                Log.Write("Fuera del HOME: " + KindName(kind) + (picker ? " (elegir usuario)" : "") +
                          (kind == KConsole && screen != null ? ": \"" + ShortText(screen) + "\"" : "") +
                          (kind == KDim || kind == KConsole ? "; aun no se entra" : ""));
            // (para ajustar las palabras de HomeHints.IsConsoleScreen: el texto de las primeras imagenes "de juego")
            else if (kind == KGame && screen != null && ShortText(screen).Length >= 3 && outTextLogs < 3)
            {
                outTextLogs++;
                Log.Write("Fuera del HOME: imagen de juego con texto \"" + ShortText(screen) + "\" (no se toma por una pantalla de la consola)");
            }
            lastKind = kind;
        }
        int outTextLogs, startTextLogs;

        // ---- 15-11: prueba sin consola de la entrada en los juegos desde el HOME (--test-entrada -> games\test_entrada.txt) ----
        // Pantallas ya clasificadas por la logica de verdad (OutGrab, ResetOut, DecideHomeNative), con un reloj falso y al ritmo de la
        // app: en el HOME una captura cada 0,5 s, fuera cada entrada_captura_s, y la decision tras cada captura y cada 0,1 s fuera del
        // HOME (GrabLoop). Del HOME la prueba repite lo de AnalyzeGrab que cuenta para entrar (SimGrab): la casilla del cursor, si
        // esta en uso (con su ~1 s de margen) y las casillas de la consola. Sin mpv, Magpie ni capturas y sin escribir nada (ni el
        // log). Pantallas: "H=Juego" el HOME con el cursor en ese juego ("H=Juego*" en uso, "H=?" uno sin nombre leido), "S=Album"
        // una casilla de la consola, "J" imagen de juego, "N" negro, "U" elegir usuario, "A" el aviso de cerrar el programa
        // suspendido, "D" el HOME atenuado; cada una con los segundos que se ve: "H=Arceus 2; J 3". Valores entrada_* por defecto
        TrayApp(Config cfg, bool test) { this.cfg = cfg; testMode = test; }
        readonly bool testMode;

        public static string EntrySelfTest(out int failures)
        {
            // 22-11 (1.2.2, ERRORES): los casos son del HOME de la Switch 2 (Ocarina, Arceus...). Con la consola de este PC (games\console.txt,
            // o la de la copia de seguridad: en el del usuario la PS5) "No Signal" esperaba lo de la PS5 (8 s, sin HOME reconocido) y daba
            // 10 FALLOS desde la 1.2.09. La prueba con la Switch 2 puesta y, al acabar, la de antes
            string consolaAntes = Consolas.Elegida;
            Consolas.Reiniciar("switch2");
            try { return EntrySelfTestSwitch2(out failures); }
            finally { Consolas.Reiniciar(consolaAntes); }
        }

        static string EntrySelfTestSwitch2(out int failures)
        {
            StringBuilder sb = new StringBuilder();
            int fails = 0;
            Config d = new Config();
            sb.Append("Entrada en los juegos desde el HOME (entrada_capturas=").Append(d.EnterGrabs).Append(", entrada_en_uso_capturas=")
              .Append(d.EnterInUseGrabs).Append(", entrada_negro_tras_usuario_s=").Append(Ms(d.EnterBlackSecs)).Append(", entrada_negro_s=")
              .Append(Ms(d.EnterBlackLongSecs)).Append(", entrada_captura_s=").Append(Ms(d.EnterGrabEvery)).Append(")\r\n\r\n");
            // (titulo, juego en marcha al volver al HOME (null: ninguno), pantallas, juego en el que se debe entrar (null: ninguno, "":
            //  uno sin identificar), parte del motivo, tope y minimo en ms fuera del HOME (0: sin tope / sin minimo))
            Action<string, string, string, string, string, int, int> run =
                delegate (string title, string cur, string seq, string want, string whyHas, int maxMs, int minMs)
                {
                    List<string> log = new List<string>();
                    string why = null, got = null;
                    long ms = -1;
                    try { got = new TrayApp(new Config(), true).SimEntry(cur, seq, log, out why, out ms); }
                    catch (Exception ex) { why = "ERROR " + ex.GetType().Name + ": " + ex.Message; got = "(error)"; }
                    finally { Log.Hook = null; }
                    bool ok = got == want && (got == null || (why != null && (whyHas == null || why.Contains(whyHas)) && (maxMs <= 0 || ms <= maxMs) && ms >= minMs));
                    if (!ok) fails++;
                    sb.Append(ok ? "OK    " : "FALLO ").Append(title).Append("\r\n")
                      .Append("      en marcha: ").Append(cur == null ? "ninguno" : "\"" + cur + "\"").Append("; pantallas: ").Append(seq).Append("\r\n")
                      .Append("      -> ").Append(got == null ? "no entra" : got.Length == 0 ? "juego sin identificar" : "\"" + got + "\"");
                    if (got != null) sb.Append(": ").Append(why).Append(" (").Append(ms).Append(" ms fuera del HOME)");
                    if (!ok)
                        sb.Append("\r\n      se esperaba ").Append(want == null ? "no entrar" : (want.Length == 0 ? "juego sin identificar" : "\"" + want + "\"") +
                                  " (\"" + whyHas + "\"" + (maxMs > 0 ? ", <= " + maxMs + " ms" : "") + (minMs > 0 ? ", >= " + minMs + " ms" : "") + ")");
                    sb.Append("\r\n");
                    foreach (string l in log) sb.Append("        ").Append(l).Append("\r\n");
                    sb.Append("\r\n");
                };

            // Fallo 1 (19:26): HOME pulsado otra vez con el cursor en otra casilla = de vuelta al juego en marcha
            run("Vuelta al juego en marcha con el cursor en otra casilla (19:26: Ocarina tomado por Arceus)",
                "Ocarina", "H=Arceus 3; J 3", "Ocarina", "juego en marcha", 1500, 0);
            run("Lo mismo con la casilla en uso vista en esta visita y curGame equivocado (puesto al abrir la app)",
                "RDR", "H=Ocarina* 1; H=Arceus 2; J 3", "Ocarina", "juego en marcha", 1500, 0);
            run("Lo mismo con el cursor en una casilla sin nombre leido",
                "Ocarina", "H=? 2; J 3", "Ocarina", "juego en marcha", 1500, 0);
            run("Aviso de cerrar el suspendido cancelado y vuelta con el boton HOME",
                "Arceus", "H=Arceus* 1; H=Ocarina 1; A 1.5; H=Ocarina 1.5; J 3", "Arceus", "juego en marcha", 1500, 0);
            run("17-11 12:25: aviso de cerrar el suspendido y vuelta al juego sin pasar por el HOME (Ocarina tomado por Red Dead)",
                "Ocarina", "H=Ocarina* 1; H=RDR 1; A 0.4; J 3", "Ocarina", "juego en marcha", 1500, 0);
            run("Lo mismo con el HOME atenuado antes del aviso",
                "Ocarina", "H=Ocarina* 1; H=RDR 1; D 0.3; A 0.6; J 3", "Ocarina", "juego en marcha", 1500, 0);
            run("Elegir usuario cancelado y HOME enseguida: sigue el juego en marcha",
                "RDR", "H=RE 1; U 1; H=RE 0.5; J 3", "RDR", "juego en marcha", 1500, 0);
            // Juego nuevo con otro en marcha: por el aviso de cerrar el suspendido
            run("Juego nuevo por el aviso de cerrar el suspendido y elegir usuario (19:23:54: Arceus cerrado, Ocarina)",
                "Arceus", "H=Arceus* 1; H=Ocarina 1; A 0.6; D 0.7; U 0.8; D 0.3; H=Ocarina 0.5; N 5", "Ocarina", "tras elegir usuario", 1500, 0);
            run("Juego nuevo por el aviso, sin elegir usuario: pantalla legal, negro e imagen (23:52, Red Dead)",
                "EA", "H=EA* 1; H=RDR 1; D 4; A 3; N 3; J 3", "RDR", "imagen del juego en 3 capturas", 0, 0);
            // Fallo 2: elegir usuario, el HOME de paso y negro = la regla de 1 s
            run("Juego nuevo por elegir usuario, con el HOME de paso (19:18:09, Arceus)",
                null, "H=Arceus 2; U 0.6; D 0.3; H=Arceus 0.5; N 5", "Arceus", "tras elegir usuario", 1500, 0);
            run("Elegir usuario, el aviso atenuado 3 s, el HOME de paso y negro (23:57, RESIDENT EVIL)",
                "RDR", "H=RE 1; U 0.4; J 0.3; D 3; H=RE 0.5; N 5", "RE", "tras elegir usuario", 1500, 0);
            run("Elegir usuario y luego imagen del juego sin negro",
                null, "H=Arceus 2; U 0.6; H=Arceus 0.5; J 3", "Arceus", "capturas seguidas tras elegir usuario", 1500, 0);
            run("Elegir usuario cancelado, rato en el HOME y luego negro: ya no cuenta (regla de 4 s)",
                null, "H=Arceus 1; U 1; H=Arceus 3; N 6", "Arceus", "sin pantalla de usuario", 0, 3900);
            // Juego nuevo sin nada suspendido: negro / pantalla de inicio
            run("Juego nuevo sin nada suspendido: negro",
                null, "H=Mario 2; N 6", "Mario", "sin pantalla de usuario", 0, 3900);
            run("Juego nuevo con curGame ya cerrado: el negro no es la vuelta a curGame",
                "Zelda", "H=Mario 2; N 6", "Mario", "sin pantalla de usuario", 0, 3900);
            run("Juego nuevo: negro y luego su imagen",
                "Zelda", "H=Mario 2; N 1.5; J 3", "Mario", "imagen del juego en 3 capturas", 0, 0);
            run("Juego nuevo sin nombre leido: negro",
                "Ocarina", "H=? 2; N 6", "", "sin pantalla de usuario", 0, 3900);
            // Vuelta rapida al mismo juego
            run("Vuelta rapida al mismo juego (en uso)",
                "Ocarina", "H=Ocarina* 2; J 2", "Ocarina", "estaba en uso", 700, 0);
            run("Vuelta sin haber leido ninguna casilla",
                "Ocarina", "J 2", "Ocarina", "sin leer otro", 700, 0);
            run("Cursor a otra casilla y de vuelta a la del juego en uso",
                "Ocarina", "H=Ocarina* 1; H=Arceus 1; H=Ocarina* 1; J 2", "Ocarina", "estaba en uso", 700, 0);
            // Casillas de la consola
            run("Casilla de la consola (Album): no se entra en nada",
                "Ocarina", "H=Ocarina* 1; S=Album 1; J 5", null, null, 0, 0);
            run("Casilla de la consola y luego vuelta al juego en uso",
                "Ocarina", "S=Ajustes 1; J 3; H=Ocarina* 1; J 2", "Ocarina", "estaba en uso", 700, 0);

            // 16-11: la biblioteca (Programas, "Ver más"). L=Juego: la pastilla con su nombre; L: sin pastilla (cursor en las pestañas o
            // moviendose); V: el cursor en "Ver más"
            sb.Append("-- 16-11: la biblioteca --\r\n\r\n");
            run("00:45 (\"LLó\"): Kingdom Come en marcha, la biblioteca y el cursor moviendose (sin pastilla): no se entra en nada",
                "KCD", "H=KCD* 1.5; V 0.5; J 0.3; L 1; L=FF7 0.5; L 0.6; L=RE 1; L 1", null, null, 0, 0);
            run("00:45 y 00:46: lo mismo y RESIDENT EVIL arrancado desde la biblioteca: no al quitarse la pastilla, sino tras elegir usuario",
                "KCD", "H=KCD* 1.5; V 0.5; L 1; L=RE 1.5; L 0.3; A 1; U 1; N 3", "RE", "tras elegir usuario", 0, 0);
            run("00:40 (\"Juego 7\"): EA en marcha, biblioteca (MGS, DQ XI, KCD) y KCD arrancado: aviso de cerrar EA, elegir usuario y negro",
                "EA", "H=EA* 1; V 0.5; J 0.3; L=MGS 1; L=DQ 1; L=KCD 1; L 0.4; A 1; U 1; N 3", "KCD", "tras elegir usuario", 0, 0);
            run("Biblioteca: la casilla del juego en marcha y se reanuda (en uso por su nombre: una captura mas)",
                "KCD", "H=KCD* 1; V 0.5; L 0.5; L=KCD 1.5; J 2", "KCD", "estaba en uso", 1000, 600);
            run("Biblioteca: la casilla del juego en marcha y vuelta al HOME con B (la animacion no es entrar)",
                "KCD", "H=KCD* 1; V 0.5; L=KCD 1.5; J 0.4; H=KCD* 1", null, null, 0, 0);
            run("HOME: del juego en uso a \"Ver más\" y la animacion hacia la biblioteca: no se entra en el juego en uso",
                "KCD", "H=KCD* 1; V 0.3; J 0.6; L 2", null, null, 0, 0);
            run("Biblioteca: otro juego (no en marcha) y A: imagen del juego sin elegir usuario, como en el HOME",
                null, "V 0.5; L=Mario 1.5; N 1.5; J 3", "Mario", "imagen del juego en 3 capturas", 0, 0);

            // 16-11: "No Signal" que no es un apagado (cambio de modo de video) y la que si (ver NoSignalHold)
            sb.Append("-- 16-11: \"No Signal\" (apagado_espera_s=").Append(Ms(d.NoSignalHoldSecs)).Append(", apagado_espera_arranque_s=")
              .Append(Ms(d.NoSignalHoldLaunchSecs)).Append(", apagado_espera_home_s=").Append(Ms(d.NoSignalHoldHomeSecs))
              .Append(", apagado_home_tras_juego_s=").Append(Ms(d.NoSignalHomeAfterGameSecs)).Append(", apagado_fin_arranque_s=")
              .Append(Ms(d.LaunchCancelSecs)).Append(") --\r\n\r\n");
            Action<string, string, string, bool, int, int> runOff =
                delegate (string title, string cur, string seq, bool wantOff, int maxMs, int minMs)
                {
                    List<string> log = new List<string>();
                    string why = null, got = null, entered = null;
                    long ms = -1;
                    try { got = new TrayApp(new Config(), true).SimPower(cur, seq, log, out why, out ms, out entered); }
                    catch (Exception ex) { why = "ERROR " + ex.GetType().Name + ": " + ex.Message; got = "(error)"; }
                    finally { Log.Hook = null; }
                    bool off = got == "(apagado)";
                    // (un titulo con "no se entra": tampoco se puede haber entrado en un juego)
                    bool ok = got != "(error)" && off == wantOff && (!off || ((maxMs <= 0 || ms <= maxMs) && ms >= minMs)) &&
                              (entered == null || !title.Contains("no se entra"));
                    if (!ok) fails++;
                    sb.Append(ok ? "OK    " : "FALLO ").Append(title).Append("\r\n")
                      .Append("      en marcha: ").Append(cur == null ? "ninguno" : "\"" + cur + "\"").Append("; pantallas: ").Append(seq).Append("\r\n")
                      .Append("      -> ").Append(off ? "se cierra (" + why + "; " + ms + " ms con \"No Signal\")" : got == "(error)" ? why : "no se cierra")
                      .Append(entered != null ? "; entrada en " + (entered.Length == 0 ? "un juego sin identificar" : "\"" + entered + "\"") : "");
                    if (!ok) sb.Append("\r\n      se esperaba ").Append(wantOff ? "cerrar" + (maxMs > 0 ? " en <= " + maxMs + " ms" : "") + (minMs > 0 ? " y >= " + minMs + " ms" : "") : "no cerrar");
                    sb.Append("\r\n");
                    foreach (string l in log) sb.Append("        ").Append(l).Append("\r\n");
                    sb.Append("\r\n");
                };
            runOff("14-11 23:57: RESIDENT EVIL arrancando (elegir usuario, negro) y 3 s de \"No Signal\": no se cierra",
                "RDR", "H=RE 1; U 1; N 2; NS 3; J 3", false, 0, 0);
            runOff("16-11 00:46: RESIDENT EVIL, 20 s de \"No Signal\" a los 10 s de entrar: no se cierra",
                "KCD", "H=RE 1; U 1; N 2; J 8; NS 20; J 3", false, 0, 0);
            runOff("Arrancando un juego, \"No Signal\" que no vuelve: se cierra a los ~26 s",
                "KCD", "H=RE 1; U 1; N 2; NS 32", true, 27500, 25000);
            runOff("Menu de apagado (HOME atenuado: Apagar, Reiniciar...), negro y \"No Signal\": se cierra enseguida, como antes",
                "Ocarina", "H=Ocarina 1; P 0.6; N 0.6; NS 4", true, 1600, 0);
            runOff("Saliendo del HOME por su boton de apagado (Modo de espera): se cierra enseguida",
                "Ocarina", "S=Modo de espera 1; N 0.5; NS 4", true, 1600, 0);
            runOff("15-11 22:25 (en espera desde el HOME): menu de apagado y negro: no se entra en el juego de antes (\"de vuelta\") y se cierra enseguida",
                "Ocarina", "P 0.5; N 2; NS 4", true, 1600, 0);
            // 16-11 tarde (APAGADO): las esperas segun lo que se hace (el HOME sin nada arrancando, recien vuelto, saliendo, jugando)
            runOff("Recien vuelto al HOME (1 s), \"No Signal\" 4 s y vuelve: no se cierra (la espera de jugando: un cambio de modo al volver)",
                "Ocarina", "H=Ocarina* 1; NS 4; H=Ocarina* 2", false, 0, 0);
            runOff("Recien vuelto al HOME (1 s), \"No Signal\" que no vuelve: la espera de jugando (~4 s), aunque a mitad ya no sea \"recien\"",
                "Ocarina", "H=Ocarina* 1; NS 12", true, 4600, 3500);
            runOff("En el HOME sin nada arrancando (2 s), \"No Signal\" que no vuelve (consola en espera): se cierra enseguida",
                "Ocarina", "H=Ocarina* 2; NS 12", true, 1600, 0);
            runOff("Jugando (reanudado), \"No Signal\" que no vuelve: se cierra a los ~4 s (apagado_espera_s + 1, capturas cada 1,2 s)",
                "Ocarina", "H=Ocarina* 1; J 3; NS 12", true, 5000, 4000);
            runOff("16-11 14:02 (de verdad): aviso de cerrar el suspendido (FC 26, Ocarina suspendido) cancelado con el cursor en una casilla de " +
                   "la consola, HOME atenuado y en espera: se cierra enseguida (antes 26 s: seguia la espera de arrancar)",
                "Ocarina", "H=Ocarina* 2; H=EA 1; A 1.5; S=Tarjetas de juego virtuales 0.8; D 1.7; NS 30", true, 2000, 0);
            runOff("16-11 09:58 (de verdad): de FC 26 al HOME (el cambio a 4K nativo, sin capturas), negro y en espera 2,5 s despues: " +
                   "no se entra en FC 26 (\"de vuelta\" solo con negro) y se cierra enseguida (antes 6 s)",
                "EA", "H=EA* 1; J 3; H=EA* 1.2; X 1.1; N 1.1; NS 10", true, 2000, 0);
            runOff("15-11 11:39 / 12:17: en espera desde el HOME con el juego en uso (atenuado y negro): no se entra en el (\"de vuelta\") " +
                   "y se cierra enseguida",
                "Juego 9", "H=Juego 9* 2; D 0.4; N 1.2; NS 6", true, 1600, 0);
            runOff("Saliendo del HOME con un juego que no esta en uso (atenuado y negro, como FC 26 a las 09:55:17 al arrancar sin elegir " +
                   "usuario) y \"No Signal\": la espera de jugando (~4 s), puede ser un juego que arranca",
                "Ocarina", "H=Ocarina* 2; H=RDR 1; D 0.3; N 1; NS 10", true, 4600, 3500);
            runOff("Aviso de cerrar el suspendido cancelado sin mover el cursor: a los 2 s de HOME se cancela la espera de arrancar; en espera " +
                   "con ese juego seleccionado, la espera de jugando (~4 s), no la de arrancar (26 s)",
                "Ocarina", "H=Ocarina* 2; H=EA 1; A 1.5; H=EA 2.5; D 0.4; N 0.6; NS 10", true, 4600, 3500);
            runOff("Aviso de cerrar el suspendido cancelado y el cursor de vuelta en el juego en uso: atenuado, negro y en espera: se cierra enseguida",
                "Ocarina", "H=Ocarina* 2; H=EA 1; A 1.5; H=Ocarina* 1; D 0.4; N 0.6; NS 6", true, 1600, 0);
            runOff("14-11 23:57: elegir usuario, el HOME de paso 0,8 s, negro y 10 s de \"No Signal\" (RESIDENT EVIL): el HOME de paso no " +
                   "cancela la espera de arrancar, no se cierra",
                "RDR", "H=RE 1; U 0.4; J 0.3; D 3; H=RE 0.8; N 2.8; NS 10; J 3", false, 0, 0);
            runOff("RESIDENT EVIL entrado, de vuelta al HOME y en espera a los 3 s (dentro de sus 60 s de arrancar): se cierra enseguida",
                "KCD", "H=RE 1; U 1; N 2; J 5; H=RE* 1.2; H=RE* 3; NS 6", true, 1600, 0);
            runOff("RESIDENT EVIL entrado, HOME y reanudado, y 15 s de \"No Signal\" (dentro de sus 60 s de arrancar): vuelve su espera, no se cierra",
                "KCD", "H=RE 1; U 1; N 2; J 5; H=RE* 1.2; H=RE* 2; J 2; NS 15; J 3", false, 0, 0);
            runOff("HOME atenuado y luego el menu de apagado en la misma salida (antes se leia una vez por salida): se cierra enseguida",
                "Ocarina", "H=Ocarina* 2; H=RDR 1; D 0.5; N 0.3; P 1.5; N 0.5; NS 6", true, 1600, 0);
            runOff("HOME atenuado que pasa a ser el menu de apagado sin dejar de estar atenuado (se vuelve a leer al ~1 s): se cierra enseguida",
                "Ocarina", "H=Ocarina* 2; H=RDR 1; D 0.5; P 1.5; N 0.5; NS 6", true, 1600, 0);

            LibraryImageTest(sb, ref fails);
            sb.Append(fails == 0 ? "Todo bien" : fails + " FALLOS").Append("\r\n");
            failures = fails;
            return sb.ToString();
        }

        // Una secuencia de la prueba: el juego en el que se entra ("" uno sin identificar, null ninguno), por que (lo del log) y a los
        // cuantos ms fuera del HOME. Empieza como al volver al HOME desde un juego (EnterHomeNative) con curGame = cur
        string SimEntry(string cur, string seq, List<string> log, out string why, out long ms)
        {
            why = null;
            ms = -1;
            DateTime t0 = new DateTime(2026, 11, 15, 12, 0, 0);
            DateTime now = t0;
            List<string> screens = new List<string>();
            List<double> ends = new List<double>();
            double total = 0;
            foreach (string part in seq.Split(';'))
            {
                string p = part.Trim();
                int sp = p.LastIndexOf(' ');
                if (sp <= 0) continue;
                total += double.Parse(p.Substring(sp + 1), CultureInfo.InvariantCulture);
                screens.Add(p.Substring(0, sp).Trim());
                ends.Add(total);
            }
            Log.Hook = delegate (string m) { log.Add("+" + (now - t0).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s  " + m); };
            lock (homeSync)
            {
                curGame = cur;
                pendingChecked = true;
                noLabel = 0; labelNoName = false; homeSeen = now; homeGame = null; homeIcon = null; homeInUse = false; homeDock = false;
                ResetOut(null, now);
                string shape = null; // (la casilla de la captura anterior del HOME: homeShape en AnalyzeGrab)
                DateTime lastG = DateTime.MinValue;
                int steps = (int)Math.Round(total * 10);
                for (int step = 0; step <= steps; step++)
                {
                    now = t0.AddMilliseconds(step * 100);
                    double ts = step / 10.0;
                    int k = 0;
                    while (k < ends.Count - 1 && ts >= ends[k] - 1e-6) k++;
                    double every = noLabel > 0 ? cfg.EnterGrabEvery : 0.5;
                    if (lastG == DateTime.MinValue || (now - lastG).TotalSeconds >= every - 1e-6) { lastG = now; SimGrab(screens[k], ref shape, now); }
                    else if (noLabel < 1) continue; // (entre capturas solo se decide fuera del HOME: GrabLoop)
                    string e = DecideHomeNative(now);
                    if (e == null) continue;
                    for (int i = log.Count - 1; i >= 0 && why == null; i--)
                    {
                        System.Text.RegularExpressions.Match mt =
                            System.Text.RegularExpressions.Regex.Match(log[i], "confirmada: (.*) \\((\\d+) ms fuera del HOME\\)$");
                        if (mt.Success) { why = mt.Groups[1].Value; ms = long.Parse(mt.Groups[2].Value, CultureInfo.InvariantCulture); }
                    }
                    return e;
                }
            }
            return null;
        }

        // Una captura de la prueba, ya clasificada: lo que hace AnalyzeGrab con ella (con homeSync)
        // 16-11: tambien "L=Juego" la biblioteca con la pastilla del nombre de ese juego (en uso solo si es el juego en marcha, por el nombre),
        // "L" la biblioteca sin pastilla (cursor en las pestañas o moviendose), "V" el HOME con el cursor en "Ver más", "P" el HOME atenuado
        // con el menu de apagado y "NS" la pantalla "No Signal" (solo cuenta en SimPower)
        void SimGrab(string s, ref string shape, DateTime now)
        {
            if (s == "NS") return; // (AnalyzeGrab no mira el HOME con "No Signal")
            if (s.StartsWith("L="))
            {
                // La biblioteca con la pastilla del nombre (lab != null, library): sin icono ni "Cambiar de usuario"
                string tile = s.Substring(2);
                homeSeen = now;
                noLabel = 0;
                homeNoSel = 0;
                homeDock = false;
                ResetOut(homeGame != null ? "\"" + homeGame + "\"" : "el juego", now);
                lastThumb = null;
                string running = inUseGame ?? curGame;
                bool inUseNow = tile != "?" && running != null && GameProfiles.SameGame(tile, running);
                if (inUseNow) inUseAt = now;
                homeInUse = inUseNow;
                powerTile = false;
                if (tile == "?") { homeGame = null; labelNoName = true; }
                else { homeGame = tile; labelNoName = false; }
                homeIcon = null;
                homeLib = true;
                shape = "L:" + tile;
                homeShape = SimShape(shape); // (16-11 tarde: la forma de la pastilla, para LaunchCancelCheck)
                LaunchCancelCheck(now);
                return;
            }
            if (s == "L" || s == "V")
            {
                // El HOME o la biblioteca sin el nombre de un juego: no se ha salido (V: "Ver más", lo de antes ya no vale)
                noLabel = 0;
                ResetOut(homeGame != null ? "\"" + homeGame + "\"" : "el juego", now);
                lastThumb = null;
                if (s == "V" && (homeGame != null || labelNoName || homeInUse))
                {
                    Log.Write("HOME: seleccionado \"Ver más\" (la biblioteca, no es un juego)");
                    homeGame = null; homeIcon = null; homeShape = null; labelNoName = false; homeInUse = false; homeDock = true;
                    shape = null;
                }
                if (++homeNoSel >= 3)
                {
                    if (homeGame != null || labelNoName) Log.Write("HOME: ningun juego seleccionado");
                    homeGame = null; homeIcon = null; homeShape = null; labelNoName = false; homeDock = true; noLabel = 0;
                    shape = null;
                }
                LaunchCancelCheck(now);
                return;
            }
            if (s.StartsWith("H=") || s.StartsWith("S="))
            {
                // El HOME con el nombre azul de una casilla (lab != null)
                bool sys = s[0] == 'S';
                string tile = s.Substring(2);
                bool inUseNow = tile.EndsWith("*");
                if (inUseNow) tile = tile.Substring(0, tile.Length - 1);
                homeSeen = now;
                noLabel = 0;
                homeNoSel = 0;
                homeDock = false;
                ResetOut(homeGame != null ? "\"" + homeGame + "\"" : "el juego", now);
                lastThumb = null;
                bool sameSel = shape == tile && !homeLib;
                if (inUseNow) inUseAt = now;
                homeInUse = inUseNow || (sameSel && homeInUse && (now - inUseAt).TotalSeconds < 1.2);
                powerTile = false;
                if (sys) { homeGame = null; labelNoName = false; homeDock = true; homeInUse = false; powerTile = HomeHints.IsPowerMenu(tile); }
                else if (tile == "?") { homeGame = null; labelNoName = true; }
                else { homeGame = tile; labelNoName = false; }
                homeLib = false;
                shape = tile;
                homeShape = SimShape(tile); // (16-11 tarde: la forma del nombre azul, para LaunchCancelCheck)
                LaunchCancelCheck(now);
                return;
            }
            // Fuera del HOME (ni el nombre ni el HOME): OutGrab con lo que se ve
            int kind = s == "N" ? KBlack : s == "D" || s == "P" ? KDim : s == "J" ? KGame : s == "U" || s == "A" ? KConsole : -1;
            if (kind < 0) throw new ArgumentException("pantalla desconocida en la prueba: \"" + s + "\"");
            string screen = s == "U" ? "¿Quién va a usar el programa?" :
                            s == "A" ? "¿Quieres cerrar el programa suspendido? Se perderán los datos no guardados" : null;
            noLabel++;
            homeNoSel = 0;
            // (16-11 tarde: el HOME atenuado se lee cada vez que sale y, si sigue, cada apagado_ocr_atenuado_s, como en AnalyzeGrab; "D" se
            // lee como el HOME de detras, sin el menu de apagado ni el aviso)
            bool dimText = DimOcrDue(kind == KDim, now);
            OutGrab(kind, screen, s == "U", now);
            if (dimText) DimRead(s == "P" ? "Opciones de energía Modo de espera Apagar Reiniciar" : "Ya disponible Ver más Opciones Confirmar", s == "P", now);
        }

        // (prueba, 16-11 tarde) La forma del nombre de una casilla (lo que compara HomeLabel.Same): la misma casilla, la misma forma
        static byte[] SimShape(string tile)
        {
            byte[] b = new byte[33];
            b[0] = 100;
            uint h = 2166136261;
            foreach (char c in tile ?? "") { h ^= c; h *= 16777619; }
            for (int i = 1; i < b.Length; i++) { h ^= (uint)i; h *= 16777619; b[i] = (byte)(h >> 24); }
            return b;
        }

        // 16-11: una secuencia con "No Signal" (NS): ¿se da la consola por apagada? Como SimEntry, pero sigue tras entrar en un juego (ya
        // jugando: una captura cada captura_juego_s, solo "J", "N" o "NS") y cada captura pasa por NoSignalTick, como en AnalyzeGrab. Devuelve
        // "(apagado)" (ms = desde la primera captura con "No Signal", why = el detalle) o null (no se cierra), y en entered el juego
        // 16-11 tarde (APAGADO): con InHome de verdad (en el HOME / jugando); jugando, una captura "H=..." (o "S=", "L", "V") es la vuelta al
        // HOME (EnterHomeNative: esa captura no se mira como del HOME, como jugando; entered vuelve a null) y "X" ninguna captura ni decision
        // (el cambio al HOME a 4K en curso, homeBusy). Empieza como recien vuelto de un juego (homeArrivedAt)
        string SimPower(string cur, string seq, List<string> log, out string why, out long ms, out string entered)
        {
            why = null;
            ms = -1;
            entered = null;
            DateTime t0 = new DateTime(2026, 11, 16, 12, 0, 0);
            DateTime now = t0;
            List<string> screens = new List<string>();
            List<double> ends = new List<double>();
            double total = 0;
            foreach (string part in seq.Split(';'))
            {
                string p = part.Trim();
                int sp = p.LastIndexOf(' ');
                if (sp <= 0) continue;
                total += double.Parse(p.Substring(sp + 1), CultureInfo.InvariantCulture);
                screens.Add(p.Substring(0, sp).Trim());
                ends.Add(total);
            }
            Log.Hook = delegate (string m) { log.Add("+" + (now - t0).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s  " + m); };
            consoleSeen = true;
            lock (homeSync)
            {
                curGame = cur;
                pendingChecked = true;
                noLabel = 0; labelNoName = false; homeSeen = now; homeGame = null; homeIcon = null; homeInUse = false; homeDock = false;
                ResetOut(null, now);
                cfg.HomeNative = true;  // (16-11 tarde: en el HOME a 4K nativo, InHome)
                homeArrivedAt = now;    // (recien vuelto de un juego, como tras EnterHomeNative)
                string shape = null;
                bool playing = false;
                DateTime lastG = DateTime.MinValue;
                int steps = (int)Math.Round(total * 10);
                for (int step = 0; step <= steps; step++)
                {
                    now = t0.AddMilliseconds(step * 100);
                    double ts = step / 10.0;
                    int k = 0;
                    while (k < ends.Count - 1 && ts >= ends[k] - 1e-6) k++;
                    if (screens[k] == "X") continue; // (cambio al HOME a 4K en curso: ni capturas ni decision)
                    double every = playing ? cfg.GameGrabEvery : noLabel > 0 ? cfg.EnterGrabEvery : 0.5;
                    bool grab = lastG == DateTime.MinValue || (now - lastG).TotalSeconds >= every - 1e-6;
                    if (grab)
                    {
                        lastG = now;
                        string detail;
                        if (NoSignalTick(screens[k] == "NS", now, out detail))
                        {
                            why = detail;
                            ms = (long)(now - noSignalFirst).TotalMilliseconds;
                            log.Add("+" + ts.ToString("0.0", CultureInfo.InvariantCulture) + " s  (se cierra: consola apagada)");
                            return "(apagado)";
                        }
                        string sc = screens[k];
                        if (playing && (sc.StartsWith("H=") || sc.StartsWith("S=") || sc.StartsWith("L") || sc == "V"))
                        {
                            // 16-11 tarde: jugando, el HOME: de vuelta en el (EnterHomeNative)
                            playing = false;
                            cfg.HomeNative = true;
                            noLabel = 0; labelNoName = false; homeSeen = now; homeShape = null; homeGame = null; homeInUse = false;
                            ResetOut(null, now);
                            homeLib = powerTile = false;
                            homeArrivedAt = now;
                            shape = null;
                            entered = null; // (entered = en lo que se ha entrado desde esta vuelta al HOME)
                            log.Add("+" + ts.ToString("0.0", CultureInfo.InvariantCulture) + " s  (de vuelta en el HOME desde el juego)");
                            continue;
                        }
                        if (!playing) SimGrab(sc, ref shape, now);
                    }
                    if (playing || (!grab && noLabel < 1)) continue;
                    string e = DecideHomeNative(now);
                    if (e == null) continue;
                    entered = e;
                    playing = true;
                    cfg.HomeNative = false; // (16-11 tarde: jugando, InHome no)
                }
            }
            return null;
        }

        // 16-11: la biblioteca y el HOME con capturas de verdad (games\test_biblioteca\casos.txt, si esta): ¿biblioteca?, el nombre que
        // se lee (la pastilla en la biblioteca, la etiqueta en el HOME; con el OCR de Windows) y "en uso" por "Cambiar de usuario" (solo HOME)
        static void LibraryImageTest(StringBuilder sb, ref int fails)
        {
            string dir = Path.Combine(Paths.Datos, @"games\test_biblioteca"), list = Path.Combine(dir, "casos.txt");
            if (!File.Exists(list)) { sb.Append("(sin ").Append(list).Append(": prueba con imagenes no hecha)\r\n\r\n"); return; }
            sb.Append("-- 16-11: capturas de la biblioteca y del HOME (").Append(list).Append(") --\r\n\r\n");
            foreach (string raw in File.ReadAllLines(list, Encoding.UTF8))
            {
                if (raw.Trim().Length == 0 || raw.StartsWith("#")) continue;
                string[] f = raw.Split('|');
                if (f.Length < 4) continue;
                string file = f[0].Trim(), wantName = f[2].Trim(), wantUse = f[3].Trim();
                bool wantLib = f[1].Trim() == "si";
                string got = "", why = "";
                bool ok;
                try
                {
                    using (Bitmap img = new Bitmap(Path.Combine(dir, file)))
                    using (Bitmap full = new Bitmap(img.Width, img.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                    {
                        using (Graphics g = Graphics.FromImage(full)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                        using (Bitmap small = HomeLabel.Small(full))
                        {
                            Rectangle box;
                            bool hint;
                            HomeLabel.LabelText label;
                            bool lib = HomeLabel.IsLibrary(small);
                            byte[] lab = lib ? HomeLabel.LibraryLabel(small, out box, out label) : HomeLabel.Selected(small, out box, out hint, out label);
                            string name = null;
                            if (lab != null && label != null)
                            {
                                Rectangle fb = HomeLabel.OcrRect(label, full.Width, full.Height);
                                if (fb.Width > 4 && fb.Height > 4)
                                    using (Bitmap crop = full.Clone(fb, full.PixelFormat)) name = GameNames.Clean(Ocr.ReadLine(crop), label.Icon);
                            }
                            string use = lab == null || lib ? "-" : HomeLabel.InUsePixels(small, box) >= HomeLabel.InUseMin ? "si" : "no";
                            got = (lib ? "biblioteca" : "HOME") + ", " + (name != null ? "\"" + name + "\"" : lab != null ? "(nombre sin leer)" : "sin nombre") +
                                  ", en uso " + use;
                            string kw = GameNames.Key(wantName == "-" ? "" : wantName), kg = GameNames.Key(name);
                            bool nameOk = wantName == "-" ? lab == null : kw.Length > 0 && kg.Length >= kw.Length - 1 &&
                                                                         kg.StartsWith(kw.Substring(0, kw.Length - 1), StringComparison.Ordinal);
                            ok = lib == wantLib && nameOk && (wantUse == "-" || use == wantUse);
                            if (!ok) why = "se esperaba " + (wantLib ? "biblioteca" : "HOME") + ", " + (wantName == "-" ? "sin nombre" : "\"" + wantName + "\"") + ", en uso " + wantUse;
                        }
                    }
                }
                catch (Exception ex) { ok = false; why = "ERROR " + ex.Message; }
                if (!ok) fails++;
                sb.Append(ok ? "OK    " : "FALLO ").Append(file).Append(": ").Append(got).Append(ok ? "" : " (" + why + ")").Append("\r\n");
            }
            sb.Append("\r\n");
        }

        // Recien abierta la app, sin haber visto el HOME (con homeSync): ver HomeTick
        void StartGrab(int kind, bool moved, string screen, DateTime now)
        {
            if (!consoleOn || kind == KDim || kind == KConsole)
            {
                // (sin la consola segun el script de mpv: negro = arrancando; con imagen, el aviso del script aun no ha llegado)
                if (!consoleOn) { if (kind == KBlack) startWoke = true; }
                else if (kind != startKind)
                    Log.Write("Recien abierta la app: " + KindName(kind) + (screen != null ? " (\"" + ShortText(screen) + "\")" : "") +
                              ": todavia no se sabe si se esta jugando");
                startHits = startMoves = 0;
                startFirst = DateTime.MinValue;
            }
            else if (kind == KBlack)
            {
                // (negro al abrir: la consola arrancando o despertando; el juego se espera mas)
                if (startHits == 0) startWoke = true;
            }
            else if (kind == KGame)
            {
                if (startHits == 0) startFirst = now;
                startHits++;
                if (moved) startMoves++;
                if (screen != null && ShortText(screen).Length >= 3 && startTextLogs < 3)
                {
                    startTextLogs++;
                    Log.Write("Recien abierta la app: imagen de juego con texto \"" + ShortText(screen) + "\" (no se toma por una pantalla de la consola)");
                }
            }
            startKind = consoleOn ? kind : -1;
        }

        // Cada 0,1 s (GrabLoop, con LS listo): pide la siguiente captura cuando toca. Aparte de HomeTick (cada 0,5 s), que
        // redondeaba el ritmo de las capturas a 0,5 s
        void GrabLoop(Process p)
        {
            try
            {
                while (!p.HasExited && !userExit && mpv == p)
                {
                    Thread.Sleep(100);
                    if (signalOn && (lsReady || MenuCiegoCaptura || CapturaPreparando || CapturaEntrandoMenu)) GrabTick(DateTime.Now); // (1.2.2: CapturaPreparando, src\VueltaJuego.cs; 1.2.3: CapturaEntrandoMenu, src\JuegoEnMarcha.cs)
                    // Fuera del HOME: se decide sin esperar al siguiente WatchLoop (hasta 0,5 s; el negro cuenta por tiempo)
                    if (signalOn && lsReady && InHome && noLabel >= 1) RunHomeTick(DateTime.Now);
                }
            }
            catch (InvalidOperationException) { }
            catch (Exception ex) { Log.Error("Pidiendo capturas", ex); }
        }

        // 1.2.05: con un relevo de motor en curso (lsReady=false, ~5-7 s) y una consola con menu propio (PS5/Xbox), se sigue capturando
        // SOLO para su menu (AnalyzeGrab -> MenuPs5Captura): el 21-11 17:38 se arranco GTA VI en ese hueco y la app siguio con Rocket League
        bool MenuCiegoCaptura
        {
            get { return relevo != null && !InHome && Consolas.Actual.MenuPropio && !Consolas.Actual.TieneHome && JuegoConsola.Activo(Consolas.Actual, cfg); }
        }

        void GrabTick(DateTime now)
        {
            // Una captura que no contesta (no deberia pasar) no deja sin mirar el HOME para siempre
            if (grabBusy && (now - lastGrab).TotalSeconds > 5) grabBusy = false;
            // Jugando con el HOME a 4K: una captura ligera (1280x720, sin OCR) cada segundo, a 30 o a 60 fps, y otra a los
            // 0,3 s si sale el HOME (AnalyzeGrab). Medido el 02-11 con las opciones de mpv de la app: cada captura son 11 ms
            // del hilo de mpv y una por segundo no deja ni una imagen tarde mas (a 30 fps 2 de 601 con y sin capturas; a 60,
            // 8-10 de 1201); con la captura de 4K, cada 2 s (una por segundo dejaba ~0,6 imagenes tarde por captura). Antes:
            // cada 4 s en el modo 30 y solo con la imagen a 60 fps en el de 60 (el HOME se veia a los 2-6 s)
            // Sin el HOME a 4K (home_4k_nativo=0) como antes: solo con la imagen a 60 fps, cada 1,5 s cerca del HOME, si no 4
            // En el HOME a 4K tambien ligera, cada segundo (antes entera cada 2 s: se movia el cursor a otro juego y se entraba
            // en el antes de verlo, 02-11 Mario Kart World tomado por Rocket League), y cada 0,5 s mientras se confirma que se
            // ha salido del HOME. Cada captura de 4K para a mpv un instante (~0,6 imagenes tarde): en el HOME apenas se nota
            // (revision 10-11: en el HOME con LS y captura de 1080p la ligera seria de 960x540, peor para leer el nombre del juego y la
            // pantalla de elegir usuario por OCR que la de 1280x720 del HOME a 4K: entera, 1920x1080)
            if (cfg.BenchNoGrabs && !InHome) return; // solo bancos (10-11: con --no-untimed cada captura paraba mpv 30-110 ms)
            bool light = cfg.HomeNativeEnabled && !(homeGrace && cfg.VideoSize == "1920x1080");
            // 18-11 (CONSOLAS): una consola sin HOME reconocido: una captura ligera cada captura_juego_s, como jugando con la Switch 2 (para
            // "No Signal", el aviso de HDCP y, con consola=auto, la deteccion: ObservaConsola)
            if (!Consolas.Actual.TieneHome)
            {
                // (1.2.2: mientras Magpie prepara, tambien, para el menu y la comprobacion de la salida: CapturaPreparando)
                // (1.2.3, JUEGO: y pasando al menu sin saber aun el juego en foco, CapturaEntrandoMenu: sin el margen de switchUntil, mpv no recarga)
                bool entrandoMenu = CapturaEntrandoMenu;
                bool focoMenu = entrandoMenu || (InHome && !homeBusy && !homeSwitching && FocoMenuPorLeer);   // (y ya en el menu: sin el margen)
                bool preparando = passesBusy && (CapturaPreparando || entrandoMenu);
                if (grabBusy || relaunchSize != null || (passesBusy && !preparando) || (now < switchUntil && !focoMenu) || !ipcUp) return;
                // 19-11 (src\MenuPs5.cs): con la PS5, en su menu y confirmando que se entra o se sale de el, cada 0,4-0,5 s
                // (19-11, DETECCION: confirmando, a los 0,25 s: src\DetectorMenu.cs decide con 2 capturas seguidas, o con 1 si la hora es la del
                // PC al minuto; antes 0,4 s jugando y 0,5 s en el menu, y 3 capturas para salir)
                double ev = cfg.GameGrabEvery;
                if (Consolas.Actual.MenuPropio && (!homeBusy || preparando))
                {
                    if (menuMaq.Confirmando) ev = 0.25; // (sin homeSync: dos enteros, una lectura de mas o de menos no importa)
                    else if ((InHome || entrandoMenu) && FocoMenuPorLeer) ev = 0.25;   // (1.2.3, JUEGO: el primer juego en foco de la visita, como mucho 5 s: src\JuegoEnMarcha.cs)
                    else if (InHome) ev = 0.5;
                    // (1.2.2: comprobando la salida del menu: el negro de arrancar otro juego, 1,2 s. Con Magpie ya escalando a 4K cada
                    // captura para a mpv 44-81 ms (RITMO): 0,8 s; mientras Magpie prepara, mpv no se ve (el puente esta encima): 0,4 s)
                    else if (VueltaPendiente) ev = preparando ? 0.4 : cfg.CaptureNow == "3840x2160" ? 0.8 : 0.6;
                    else if (preparando) ev = Math.Max(ev, 1.0);
                    else if (cfg.CaptureNow == "3840x2160") ev = Math.Max(ev, cfg.RitmoGrab4kSecs);   // (22-11, 1.2.2 RITMO: src\Lanzador\Ritmo.cs)
                }
                // (1.2.3 RITMO2) las consolas sin menu propio (otra, Xbox...) jugando a 4K: como la PS5, una cada captura_juego_4k_s (1,5 s) y no
                // cada captura_juego_s (0,6): cada una a 4K tiene a mpv ~18 ms y su hilo ~110 ms con el PNG (2,5 veces menos)
                else if (!Consolas.Actual.MenuPropio && cfg.CaptureNow == "3840x2160" && !preparando && !InHome) ev = Math.Max(ev, cfg.RitmoGrab4kSecs);
                if ((now - lastGrab).TotalSeconds >= ev - 1e-6) RequestGrab(true);
                return;
            }
            if (!cfg.HomeNativeEnabled && !cfg.CapMode && (vfFps < 45 || want30)) return;
            if (grabBusy || relaunchSize != null || passesBusy || homeBusy || now < switchUntil || !ipcUp) return;
            // 03-11: en el HOME cada 0,5 s (se elegia y lanzaba un juego entre dos lecturas: se tomaba por otro); jugando cada 0,6 s
            // (medido: 0,1 imagenes tarde por captura a 60 fps; con la captura de 4K, cada 1,5 s)
            // (08-11: confirmando la salida del HOME, cada 0,3 s; 09-11: recien abierta la app sin ver el HOME, cada 0,35 s)
            // (10-11: en el HOME con LS aun escalando, homeGrace, como en el HOME a 4K)
            double every = InHome ? (noLabel > 0 ? cfg.EnterGrabEvery : homeSeen == DateTime.MinValue ? cfg.StartGrabEvery : 0.5) :
                           light ? (cfg.CaptureNow == "3840x2160" ? cfg.RitmoGrab4kSecs : cfg.GameGrabEvery) :   // (22-11, 1.2.2 RITMO)
                           homeGame != null || (now - homeSeen).TotalSeconds < 30 ? 1.5 : 4;
            if ((now - lastGrab).TotalSeconds < every) return;
            // 15-11: en el HOME, leyendo el nombre del juego seleccionado (AnalyzeGrab): entera, a toda resolucion (hasta 6 por juego)
            RequestGrab(light && !(nameFullGrab && InHome));
        }

        // Pide una captura al script Lua. Ligera: 1 de cada 2-3 pixeles y filas (~1280x720, la captura de 720p entera):
        // basta para ver el nombre azul del HOME (se mira a 640x360) y escribe 3,7 MB en vez de 15-33
        void RequestGrab(bool light)
        {
            lastGrab = DateTime.Now;
            grabBusy = true;
            // (22-11, 1.2.2 RITMO: jugando con Magpie iba por PNG a una tuberia para que mpv no se parase. 23-11, 1.2.4 SINMPV: sin mpv, siempre
            // la del motor -a fichero, en color, sin parar la imagen: la copia en su hilo de captura y la conversion aparte, EngineGrabColor.cs-)
            lock (cmdQueue) cmdQueue.Add(JsonCmd("script-message", "switch2-grab", GrabPath, light ? "light" : "full"));
        }

        // La imagen que ha volcado el script Lua: ¿es el HOME? ¿que juego esta seleccionado?
        DateTime homeFirstHit;          // jugando: la primera captura con el HOME (para el log: cuanto tardo en confirmarse)
        DateTime relojSwLog = DateTime.MinValue; // 19-11 (DETECCION): la ultima vez que se apunto en el log el HOME por su hora
        double[] homeIcon;              // en el HOME: el icono del juego seleccionado (el que se guarda al entrar en el)
        int homeNoSel;                  // en el HOME: capturas seguidas del HOME sin juego seleccionado (cursor en la barra de abajo)

        // ---- Diagnostico del tramado (10-11, ini diag_tramado) ----
        int ditherBursts;                       // tandas guardadas en esta sesion
        DateTime lastBurst = DateTime.Now;      // la ultima (o el arranque: la primera, como pronto a los 150 s)
        volatile bool burstBusy;

        // Parte de la imagen casi negra (luma < 45 de 255), mirando 1 de cada 8x8 puntos de una captura (ligera o entera)
        static double DarkShare(byte[] d, int w, int h, int stride, string format)
        {
            bool rgb = format != null && format.StartsWith("rgb", StringComparison.Ordinal);
            int n = 0, dark = 0;
            for (int y = 4; y < h; y += 8)
                for (int x = 4; x < w; x += 8)
                {
                    int o = y * stride + x * 4;
                    if (o + 2 >= d.Length) continue;
                    int r = rgb ? d[o] : d[o + 2], b = rgb ? d[o + 2] : d[o];
                    n++;
                    if (((r * 77 + d[o + 1] * 150 + b * 29) >> 8) < 45) dark++;
                }
            return n > 0 ? (double)dark / n : 0;
        }

        // Jugando, en una escena oscura: 2 imagenes seguidas de la captura (sin shader) para estudiar el tramado del juego de verdad
        void MaybeDitherBurst(double dark)
        {
            if (dark < 0.3 || burstBusy || ditherBursts >= cfg.DiagDither) return;
            if (!lsReady || homeBusy || passesBusy || InHome || userExit || relaunchSize != null) return;
            if ((DateTime.Now - lastBurst).TotalSeconds < 150) return;
            burstBusy = true;
            lastBurst = DateTime.Now;
            ditherBursts++;
            string prefix = Path.Combine(Path.GetTempPath(), "switch2_tramado_" + ditherBursts);
            Log.Write("Diagnostico del tramado: escena oscura (" + Math.Round(dark * 100) + " % casi negro), se guardan 2 imagenes seguidas de la " +
                      "captura (" + ditherBursts + " de " + cfg.DiagDither + " esta sesion; diag_tramado=0 en el ini lo apaga)");
            lock (cmdQueue) cmdQueue.Add(JsonCmd("script-message", "switch2-burst", prefix, "2"));
        }

        // La respuesta del script de mpv: los .raw a PNG en games\debug\tramado (como mucho los 12 mas nuevos), en segundo plano
        static void SaveDitherBurst(string prefix, int n, int w, int h, int stride, string format, string game)
        {
            try
            {
                string dir = Path.Combine(Paths.Datos, @"games\debug\tramado");
                Directory.CreateDirectory(dir);
                string g = game ?? "juego";
                foreach (char c in Path.GetInvalidFileNameChars()) g = g.Replace(c, '_');
                if (g.Length > 40) g = g.Substring(0, 40);
                string tag = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "_" + g;
                int saved = 0;
                for (int i = 0; i < n; i++)
                {
                    string raw = prefix + "_" + i + ".raw";
                    if (!File.Exists(raw)) continue;
                    byte[] data = File.ReadAllBytes(raw);
                    try { File.Delete(raw); } catch { }
                    if (w <= 0 || h <= 0 || data.Length < (long)stride * h) continue;
                    using (Bitmap b = HomeLabel.FromRaw(data, w, h, stride, format))
                        b.Save(Path.Combine(dir, tag + "_" + i + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    saved++;
                }
                FileInfo[] all = new DirectoryInfo(dir).GetFiles("*.png");
                Array.Sort(all, delegate (FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                for (int i = 12; i < all.Length; i++) { try { all[i].Delete(); } catch { } } // (~6 MB cada una)
                Log.Write("Diagnostico del tramado: " + saved + " imagenes de " + w + "x" + h + " guardadas en " + dir + " (" + tag + ")");
            }
            catch (Exception ex) { Log.Error("Guardando el diagnostico del tramado", ex); }
        }

        void AnalyzeGrab(string path, int w, int h, int stride, string format)
        {
            bool goHome = false, confirm = false;
            bool wantFull = false; // 15-11: la siguiente captura entera (para leer el nombre del juego seleccionado)
            int confirmMs = 250;
            double diagDark = -1; // 10-11: parte casi negra de la imagen jugando (diagnostico del tramado, MaybeDitherBurst)
            try
            {
                byte[] data = grabEnMemoria ?? File.ReadAllBytes(path);   // (22-11, 1.2.2 RITMO: la del PNG ya viene en memoria, Ritmo.cs)
                grabEnMemoria = null;
                if (data.Length < (long)stride * h) return;
                if (cfg.DiagDither > 0 && ditherBursts < cfg.DiagDither && !InHome && cfg.HomeNativeEnabled)
                    diagDark = DarkShare(data, w, h, stride, format);
                string name = null;
                byte[] lab;
                double[] strips, icon = null;
                GameProfile known = null;
                bool noSignal;
                bool inUseNow = false, isConsole = false, picker = false;
                bool library = false, freshSel = false, tileHint = false; // 16-11: la biblioteca; otra casilla; "Ver más" (ver HomeLabel.Selected)
                bool powerMenu = false;          // 16-11: el HOME atenuado es el menu de apagado (dimScreen, su texto)
                bool relojHome = false;          // 19-11 (DETECCION): la hora del HOME de la Switch 2 vista sin el nombre ni las franjas
                string dimScreen = null;
                int hintY = -1;
                int kind = KHome;
                string screen = null;
                double[] thumb = null;
                Rectangle labBox = Rectangle.Empty;
                // Jugando (con el HOME a 4K) solo importa si es el HOME: sin OCR (100-300 ms), el nombre se lee ya en el HOME
                // (10-11: en el HOME con LS aun escalando, homeGrace, todo como en el HOME a 4K)
                bool inHomeNow = InHome;
                bool inGame = !inHomeNow && cfg.HomeNativeEnabled;
                double[] homeThumb = null;
                using (Bitmap full = HomeLabel.FromRaw(data, w, h, stride, format))
                using (Bitmap small = HomeLabel.Small(full))
                {
                    Rectangle box;
                    bool hint;
                    HomeLabel.LabelText label;
                    // 18-11 (CONSOLAS): el HOME (nombre cian, franjas, biblioteca) solo con la consola cuyo HOME se reconoce (la Switch 2). Con
                    // las demas nada del HOME: solo "No Signal" y, con consola=auto, lo de ObservaConsola (src\ConsolasApp.cs)
                    bool conHome = Consolas.Actual.TieneHome;
                    if (conHome) lab = HomeLabel.Selected(small, out box, out hint, out label, out hintY);
                    else { lab = null; box = Rectangle.Empty; hint = false; label = null; hintY = -1; }
                    labBox = box;
                    strips = HomeLabel.Strips(small);
                    noSignal = HomeLabel.IsNoSignal(small);
                    if (noSignal) lab = null;
                    // 1.2.05: captura del relevo de motor (MenuCiegoCaptura): solo para el menu de la consola; lo demas, como antes: nada
                    // (1.2.2: y mientras Magpie prepara, CapturaPreparando, src\VueltaJuego.cs)
                    if (!lsReady && (relevo != null || (passesBusy && Consolas.Actual.MenuPropio)))
                    {
                        if (Consolas.Actual.MenuPropio) MenuPs5Captura(full, small, noSignal);
                        return;
                    }
                    if (!conHome) ObservaConsola(full, small, strips, noSignal, homeRef);
                    // 19-11: el menu de la PS5 (src\MenuPs5.cs): con ella puesta, su menu a 4K nativo; con otra y consola=auto, pista a su favor
                    if (Consolas.Actual.MenuPropio) MenuPs5Captura(full, small, noSignal);
                    else if (!noSignal) MenuPs5Pista(full, lab != null || (homeRef != null && HomeLabel.HomeDiff(strips, homeRef) <= HomeLabel.IsHome));
                    // 16-11: la biblioteca (Programas): el nombre es el de su pastilla gris (o ninguno si no se ve), sin icono ni "en uso" por
                    // pixeles (HomeLabel.IsLibrary); cuenta como el HOME (no es salir de el)
                    library = conHome && !noSignal && inHomeNow && !inGame && HomeLabel.IsLibrary(small);
                    if (library)
                    {
                        lab = HomeLabel.LibraryLabel(small, out box, out label);
                        labBox = box;
                        hint = false;
                        hintY = -1;
                    }
                    // 09-11: un texto azul sin las franjas del HOME puede ser la pantalla de elegir usuario (el borde o el
                    // nombre del usuario seleccionado; sale justo despues del HOME): si su texto lo dice, no es el HOME (ni el
                    // nombre de un juego). Si no (la biblioteca...), sigue siendo el HOME. Probado con una imagen sintetica:
                    // sin esto, el nombre del usuario pasaba a ser el juego seleccionado
                    if (lab != null && !library && !inGame && inHomeNow && cfg.EnterWaitUser && homeRef != null &&
                        HomeLabel.HomeDiff(strips, homeRef) > HomeLabel.IsHome)
                    {
                        screen = HomeLabel.ScreenText(full);
                        Consolas.VistoTexto(screen); // (18-11: con consola=auto, palabras como "PS5" o "Xbox")
                        bool pk;
                        if (HomeHints.IsConsoleScreen(screen, out pk) && pk) { lab = null; isConsole = true; picker = true; }
                    }
                    if (lab != null && !inGame)
                    {
                        // El juego se reconoce por su icono (guardado la primera vez que se entro en el). 15-11: el nombre (OCR) se lee
                        // en cada captura de la seleccion hasta tenerlo seguro (NameReads: votacion y union de las ventanas de los
                        // nombres largos, que se desplazan), para un juego nuevo y para uno guardado con el nombre sin confirmar
                        // ("Juego N", cortado: "Royal Editi"); antes, solo para uno nuevo y una vez (la primera lectura, cortada o nada)
                        // (16-11: en la biblioteca no hay icono: donde lo busca el HOME hay otras casillas de la rejilla; se le reconoce
                        // por el nombre de su pastilla, y otra casilla es otro sitio de la pastilla)
                        icon = library ? null : HomeLabel.Icon(small, box.X + box.Width / 2);
                        known = library ? null : GameProfiles.FindByIcon(icon);
                        NameReads nr;
                        bool sysLike;
                        lock (homeSync)
                        {
                            freshSel = selReads == null || (library ? !selReads.SameBox(box)
                                                                    : selReads.Library || HomeLabel.Diff(icon, selReads.Icon) > HomeLabel.SameIcon);
                            if (freshSel) selReads = library ? new NameReads(box) : new NameReads(icon, known != null ? known.Name : null);
                            nr = selReads;
                            // (la ultima casilla de la consola, en el mismo sitio: ya leida)
                            sysLike = !library && sysShape != null && HomeLabel.Same(lab, sysShape) && Math.Abs(box.Y - sysBox.Y) <= 3 &&
                                      Math.Abs(box.X + box.Width / 2 - (sysBox.X + sysBox.Width / 2)) <= 6;
                        }
                        bool readName = !sysLike && label != null && (known == null || !known.NameSure) && !nr.Done;
                        if (readName)
                        {
                            Rectangle fb = HomeLabel.OcrRect(label, w, h);
                            string raw = null;
                            if (fb.Width > 4 && fb.Height > 4)
                                using (Bitmap crop = full.Clone(fb, full.PixelFormat)) raw = Ocr.ReadLine(crop);
                            string clean = GameNames.Clean(raw, label.Icon);
                            if (HomeHints.IsHintText(clean))
                            {
                                if (!library) { HomeHints.Learn(lab, clean); hint = true; hintY = box.Y + box.Height / 2; }
                                lab = null; icon = null; known = null;
                            }
                            else
                            {
                                if (w > 1400) nr.FullGrabs++;
                                string before = nr.Best;
                                bool fail = clean == null || !GameNames.Plausible(clean);
                                bool changed = nr.Add(clean, label.Clipped);
                                // Lo que se lee, al log: el primer fallo de la seleccion, y el nombre cuando cambia de verdad o queda seguro
                                string why = null;
                                if (fail) { if (nr.Fails == 1 || nr.Fails == 4) why = "sin_nombre"; }
                                else if (before != null && !changed && !GameNames.Within(before + " " + before, clean)) why = "dudosa";
                                string diag = why != null ? SaveHomeDiag(full, fb, why, raw, nr) : null;
                                if (fail && why != null)
                                    Log.Write("HOME: el OCR no lee el nombre del juego seleccionado (lectura " + nr.Attempts + ": \"" + ShortText(raw) + "\", " +
                                              label.Width + " px de ancho" + (label.Clipped ? ", cortado" : "") + ", captura de " + w + "x" + h + ")" +
                                              (diag != null ? " -> " + diag : ""));
                                else if (why != null && diag != null)
                                    Log.Write("HOME: lectura que no encaja con \"" + nr.Best + "\": \"" + clean + "\" -> " + diag);
                                int kl = nr.Best != null ? GameNames.Key(nr.Best).Length : 0;
                                if (changed && nr.Best != null && (nr.Sure || kl >= nr.LoggedLen + 5 || nr.LoggedLen == 0))
                                {
                                    nr.LoggedLen = kl;
                                    Log.Write("HOME: nombre leido \"" + nr.Best + "\" (" + nr.Count + " lecturas" + (nr.Clipped ? ", se desplaza" : "") +
                                              (nr.Sure ? ", seguro" : "") + (w > 1400 ? ", captura entera" : "") + ")");
                                }
                                // Perfil reconocido por su icono con el nombre sin confirmar: el leido si es mejor (o lo confirma)
                                if (changed && known != null && nr.Best != null) ImproveProfile(known, nr.Best, nr.Sure, "leido en el HOME", nr.Firm);
                            }
                            // (la siguiente captura entera, no la ligera de 1 de cada 3 pixeles, mientras haga falta leer: hasta 6 por seleccion)
                            wantFull = !nr.Done && nr.FullGrabs < 6;
                        }
                        if (known != null && !GameProfiles.Exists(known)) known = GameProfiles.FindByIcon(icon); // (juntado con otro)
                        name = nr.Best;
                        // 09-11: ¿en uso? ("Cambiar de usuario" bajo su casilla; medido: 341 pixeles en uso, 0 si no). 16-11: solo en el HOME de
                        // verdad (sus franjas): en la biblioteca y en otras pantallas con texto azul ahi hay otra cosa (00:45:53: Fall Guys y
                        // Hollow Knight, 272 pixeles); en la biblioteca, en uso si es el juego en marcha (por su nombre, mas abajo)
                        if (lab != null && inHomeNow && !library && homeRef != null && HomeLabel.HomeDiff(strips, homeRef) <= HomeLabel.IsHome)
                            inUseNow = HomeLabel.InUsePixels(small, box) >= HomeLabel.InUseMin;
                    }
                    // 16-11: el cursor en "Ver más" (la etiqueta de una casilla de la fila de juegos que no es un juego): la seleccion de antes
                    // ya no vale (si no, la del juego en uso seguia valiendo y la animacion hacia la biblioteca se tomaba por entrar en el)
                    tileHint = !library && lab == null && hint && hintY >= 0 && hintY < 150;
                    // 09-11: en el HOME a 4K, una captura sin el HOME: que se ve (ver OutGrab). El OCR de la imagen entera
                    // (60-150 ms a 1280x720) solo si hace falta: esperando a entrar en un juego que no esta en uso, o recien
                    // abierta la app sin haber visto el HOME. El juego en uso se reanuda sin pantallas de la consola: sin OCR
                    // 10-11: en el HOME con LS aun escalando, una miniatura de cada captura (para pasar a 4K nativo con el HOME quieto);
                    // jugando, la del HOME visto (para comprobar la copia de la pantalla del telon congelado, PutFrozenCurtain)
                    // 19-11 (DETECCION, src\DetectorMenu.cs): sin el nombre cian ni las franjas del HOME (la captura en grises del motor con
                    // el 4K en yuy2, games\home_ref.txt de otra captura...): la hora del HOME arriba a la derecha, la del PC con la bateria a
                    // su derecha ("17:49 100%"), cuenta como el HOME (OCR de la zona, ~40 ms, solo si hay algo como texto y ha cambiado)
                    // (no con el HOME atenuado: eso es el menu de apagado o el aviso de cerrar el suspendido, que lee DimRead)
                    double dkR, dcR;
                    if (conHome && !noSignal && lab == null && !library && (homeRef == null || HomeLabel.HomeDiff(strips, homeRef) > HomeLabel.IsHome) &&
                        !HomeLabel.DimHome(strips, homeRef, out dkR, out dcR))
                    {
                        string rh, ro, rp;
                        relojHome = DetectorMenu.RelojSwitch(full, DateTime.Now, out rh, out ro, out rp);
                        if (relojHome && (DateTime.Now - relojSwLog).TotalSeconds >= 30)
                        {
                            relojSwLog = DateTime.Now;
                            Log.Write("HOME: sin el nombre cian ni sus franjas, pero con su hora arriba a la derecha (\"" + rh + "\", la del PC, con la bateria): cuenta como el HOME");
                        }
                    }
                    if ((homeGrace || (inGame && (lab != null || relojHome))) && !noSignal) { double litG; homeThumb = HomeLabel.Thumb(small, out litG); }
                    if (conHome && inHomeNow && lab == null && !noSignal && !library) // (19-11: no en el menu de la PS5: MenuPs5Captura)
                    {
                        double[] rf = homeRef;
                        if ((rf == null || HomeLabel.HomeDiff(strips, rf) > HomeLabel.IsHome) && !relojHome) // (19-11: con su hora, es el HOME)
                        {
                            double lit, dk, dc;
                            thumb = HomeLabel.Thumb(small, out lit);
                            bool dim = HomeLabel.DimHome(strips, rf, out dk, out dc);
                            bool wantText, still, dimText;
                            int prevOcr;
                            bool prevPicker;
                            lock (homeSync)
                            {
                                bool sel = homeGame != null || labelNoName;
                                bool unread = !sel && !homeDock && homeSeen != DateTime.MinValue;
                                bool back = unread && curGame != null && (DateTime.Now - homeSeen).TotalSeconds < 10;
                                // (16-11: el juego en uso elegido en la biblioteca, sin "Cambiar de usuario" que lo confirme: con OCR, como los
                                // demas; asi lo de la consola que salga antes que el juego no cuenta)
                                wantText = homeSeen == DateTime.MinValue ? consoleOn :
                                           cfg.EnterWaitUser && (sel || unread) && !(sel && homeInUse && !homeLib) && !back;
                                still = lastThumb != null && HomeLabel.Diff(thumb, lastThumb) < HomeLabel.MoveMin;
                                prevOcr = ocrState;
                                prevPicker = ocrPicker;
                                // 16-11: el HOME atenuado: ¿el menu de apagado? (ver PowerMenuNow). 16-11 tarde: cada vez que sale y, si sigue,
                                // cada apagado_ocr_atenuado_s (DimOcrDue; antes una vez por salida)
                                dimText = homeSeen != DateTime.MinValue && DimOcrDue(dim, DateTime.Now);
                            }
                            if (dimText)
                            {
                                dimScreen = HomeLabel.ScreenText(full);
                                Consolas.VistoTexto(dimScreen);
                                powerMenu = HomeHints.IsPowerMenu(dimScreen);
                            }
                            int ocrNow = -1;
                            if (!dim && wantText && lit >= 0.001 && screen == null)
                            {
                                // Quieta como la anterior (la pantalla de elegir usuario mientras se elige, la de bloqueo...): lo
                                // mismo que esa, sin otro OCR
                                if (still && prevOcr >= 0) { isConsole = prevOcr == 1; picker = prevPicker; }
                                else
                                {
                                    screen = HomeLabel.ScreenText(full); // (15-11: a 1280x720 aunque la captura sea entera)
                                    Consolas.VistoTexto(screen);
                                    isConsole = HomeHints.IsConsoleScreen(screen, out picker);
                                }
                                ocrNow = isConsole ? 1 : 0;
                            }
                            else if (screen != null) ocrNow = isConsole ? 1 : 0;
                            lock (homeSync) { ocrState = ocrNow; ocrPicker = picker; }
                            kind = dim ? KDim : isConsole ? KConsole : lit < HomeLabel.DarkLit ? KBlack : KGame;
                        }
                    }
                }
                // Consola apagada: la pantalla "No Signal" de la capturadora en 3 capturas seguidas (~1,5 s), habiendo visto la
                // consola antes (al abrir la app con ella apagada no). Antes se esperaba al script de mpv (que solo mira la imagen
                // tras 10 s de silencio, o 4 s de "No Signal") y a un posible aviso de reinicio de la grafica: 17-20 s (03-11)
                // 16-11: y que dure lo que diga NoSignalHold (un cambio de modo de video al arrancar un juego tambien la enseña)
                string nsDetail;
                if (NoSignalTick(noSignal, DateTime.Now, out nsDetail) && consoleSeen && !userExit)
                {
                    PowerOffNow("no-signal-app", nsDetail);
                    return;
                }
                lock (homeSync)
                {
                    double hd = HomeLabel.HomeDiff(strips, homeRef);
                    bool looksHome = (homeRef != null && hd <= HomeLabel.IsHome) || relojHome; // (19-11: o su hora, DETECCION)
                    // 09-11: movimiento respecto a la captura anterior fuera del HOME; recien abierta la app, "No Signal" = la
                    // consola aun no se ve (al encenderla se espera mas imagen de juego antes de decidir que ya se jugaba)
                    bool moved = false;
                    if (thumb != null) { moved = lastThumb != null && HomeLabel.Diff(thumb, lastThumb) >= HomeLabel.MoveMin; lastThumb = thumb; }
                    if (noSignal && inHomeNow && homeSeen == DateTime.MinValue) { startWoke = true; startHits = startMoves = 0; startFirst = DateTime.MinValue; }
                    // 10-11: en el HOME con LS aun escalando: cuando se vio el HOME por ultima vez y si se movia (GraceDue)
                    if (homeGrace && !homeBusy && !noSignal && (lab != null || looksHome))
                    {
                        graceLastHome = DateTime.Now;
                        if (homeThumb != null && (graceThumb == null || HomeLabel.Diff(homeThumb, graceThumb) >= HomeLabel.MoveMin)) graceMoved = DateTime.Now;
                        graceThumb = homeThumb;
                    }
                    else if (homeGrace) graceThumb = null;
                    if (homeThumb != null && !noSignal && (lab != null || looksHome) && (inGame ? looksHome : homeGrace))
                    {
                        lastHomeThumb = homeThumb;
                        lastHomeThumbAt = DateTime.Now;
                    }
                    if (homeBusy || noSignal || !Consolas.Actual.TieneHome) { } // (19-11: sin el HOME de la Switch 2, nada de lo suyo: la PS5 va por MenuPs5Captura)
                    else if (lab != null && inGame)
                    {
                        // Jugando: el nombre azul y las franjas del HOME (la de arriba y la barra de iconos, guardadas en
                        // games\home_ref.txt). Muy claro (franjas casi iguales): al HOME ya; si no, con otra captura a los 0,15 s
                        if (looksHome)
                        {
                            homeHits++;
                            if (homeHits == 1) homeSeen = homeFirstHit = DateTime.Now;
                            if (homeHits >= 2 || hd <= 6)
                            {
                                homeHits = 0;
                                goHome = true;
                            }
                            else confirm = true;
                        }
                        else if (homeRef == null)
                        {
                            // Nunca visto el HOME en este PC: 4 capturas seguidas (~1,2 s) con el mismo texto azul y las mismas
                            // franjas (un texto azul de un juego en movimiento no lo hace: 02-11, TOTK)
                            if (strictShape != null && HomeLabel.Same(lab, strictShape) && HomeLabel.Diff(strips, strictTop) <= 4) strictHits++;
                            else { strictHits = 1; homeFirstHit = DateTime.Now; }
                            strictShape = lab;
                            strictTop = strips;
                            if (strictHits >= 4) { strictHits = 0; goHome = true; }
                            else { confirm = true; confirmMs = 300; }
                        }
                        else homeHits = 0;
                    }
                    // 19-11 (DETECCION): jugando, sin el nombre cian pero con la hora del HOME (la del PC con la bateria): como el HOME con sus
                    // franjas, con otra captura a los 0,15 s
                    else if (relojHome && inGame)
                    {
                        homeHits++;
                        if (homeHits == 1) homeSeen = homeFirstHit = DateTime.Now;
                        if (homeHits >= 2) { homeHits = 0; goHome = true; }
                        else confirm = true;
                    }
                    else if (lab != null)
                    {
                        homeSeen = DateTime.Now;
                        // La referencia del HOME solo con el HOME (no con la biblioteca u otras pantallas con texto azul)
                        if (inHomeNow && !library && (homeRef == null || looksHome)) { homeRef = strips; SaveRef(strips); }
                        noLabel = 0;
                        homeNoSel = 0;
                        bool wasDock = homeDock;
                        homeDock = false;
                        // 09-11: de vuelta en el HOME: la salida anterior no cuenta (si se esperaba a entrar, no se puso nada)
                        if (inHomeNow) ResetOut(homeGame != null ? "\"" + homeGame + "\"" : "el juego");
                        lastThumb = null;
                        // 09-11: ¿el juego seleccionado esta en uso? Con el mismo juego se mantiene ~1 s (al pulsar A, el HOME
                        // se amplia y "Cambiar de usuario" puede no verse ya)
                        bool sameSel = homeShape != null && homeLib == library && HomeLabel.Same(lab, homeShape);
                        bool wasInUse = homeInUse;
                        bool named = name != null && name.Length >= 3;
                        // 16-11: en la biblioteca no se ve si esta en uso: lo esta si es el juego en marcha (la casilla vista en uso en esta visita
                        // al HOME o, si no, el de antes), por el nombre de su pastilla; sin heredar nada de la casilla de antes (00:45:53-56: Kingdom
                        // Come en marcha, "en uso" visto en el arte de otras casillas y "LLó" entrado a los 389 ms sin estar en uso)
                        if (library)
                        {
                            string running = inUseGame ?? curGame;
                            inUseNow = named && running != null && !HomeHints.IsSystemName(name) && GameProfiles.SameGame(name, running);
                        }
                        if (inUseNow) inUseAt = DateTime.Now;
                        homeInUse = library ? inUseNow : inUseNow || (sameSel && homeInUse && (DateTime.Now - inUseAt).TotalSeconds < 1.2);
                        // (15-11: si lo leido es una casilla de la consola, eso, aunque el icono que hay debajo sea el de un juego)
                        string selName = HomeHints.IsSystemName(name) ? name : known != null ? known.Name : named ? name : null;
                        powerTile = false;
                        if (HomeHints.IsSystemName(selName) || (selName == null && !library && HomeLabel.Same(lab, sysShape)))
                        {
                            if (homeGame != null || !wasDock) Log.Write("HOME: seleccionado \"" + (selName ?? "?") + "\" (de la consola, no es un juego)");
                            homeGame = null; labelNoName = false; homeDock = true; sysShape = lab; sysBox = labBox; icon = null; homeIcon = null;
                            homeInUse = false;
                            // 16-11: el boton de apagado / modo de espera de la barra: si se sale del HOME por el, "No Signal" es un apagado
                            powerTile = HomeHints.IsPowerMenu(selName);
                        }
                        else if (known != null)
                        {
                            if (homeGame == null || homeGame != known.Name) Log.Write("HOME: seleccionado \"" + known.Name + "\" (reconocido por su icono)");
                            homeGame = known.Name;
                            labelNoName = false;
                        }
                        else if (named)
                        {
                            if (homeGame == null || !GameProfiles.SameGame(homeGame, name))
                                Log.Write("HOME: seleccionado \"" + name + "\"" + (library ? " (en la biblioteca)" : ""));
                            homeGame = name; // (15-11: el mejor de sus lecturas, NameReads; ya no "el mas largo": con basura al final lo era)
                            labelNoName = false;
                        }
                        else if (library ? freshSel : HomeLabel.Diff(icon, homeIcon) > HomeLabel.SameIcon)
                        {
                            // Otro juego seleccionado, sin icono conocido y sin nombre leido: el de antes ya no vale (02-11: se
                            // entro en Mario Kart World con los ajustes de Rocket League). Al entrar se guarda con su icono
                            // (16-11: en la biblioteca, otra casilla = otro sitio de la pastilla; sin icono)
                            if (homeGame != null) Log.Write("HOME: otro juego seleccionado" + (library ? " en la biblioteca" : "") + " (nuevo, sin leer su nombre todavia)");
                            homeGame = null;
                            labelNoName = true;
                        }
                        // (16-11: de la biblioteca ningun icono: el de una casilla del HOME vista antes seria el de otro juego)
                        if (library) homeIcon = null;
                        else if (icon != null) homeIcon = icon;
                        homeShape = lab;
                        if (library != homeLib)
                            Log.Write(library ? "HOME: en la biblioteca (Programas): el juego por el nombre de su pastilla, sin icono; en uso solo si es el que esta en marcha"
                                              : "HOME: fuera de la biblioteca, en el HOME");
                        homeLib = library;
                        if (inHomeNow && !homeDock && homeInUse != wasInUse && (homeInUse || sameSel))
                            Log.Write("HOME: " + (homeGame != null ? "\"" + homeGame + "\"" : "el juego seleccionado") +
                                      (homeInUse ? (library ? " esta en uso (en la biblioteca: es el juego en marcha; al entrar se reanuda sin elegir usuario)"
                                                            : " esta en uso (al entrar se reanuda sin elegir usuario: sus ajustes en cuanto no se vea el HOME)")
                                                 : " ya no esta en uso"));
                        // 15-11: el juego en uso no es en el que la app creia estar: lo cambiado con el mando se guardo en otro (14-11 23:58:
                        // la app se reabrio con RESIDENT EVIL requiem cargando y puso "Red Dead Redemption", el ultimo; 6 cambios del mando
                        // fueron a Red Dead). Solo aviso, una vez por juego: el "en uso" a veces se ve en dos juegos (12-11)
                        string cg = curGame;
                        if (inHomeNow && !library && homeInUse && !wasInUse && homeGame != null && cg != null && cg != staleWarned &&
                            !GameProfiles.SameGame(homeGame, cg))
                        {
                            staleWarned = cg;
                            Log.Write("Aviso: el juego en uso es \"" + homeGame + "\", pero lo cambiado con el mando se guardaba en \"" + cg +
                                      "\" (" + (curGameGuess ? "puesto al abrir la app sin ver el HOME" : "un cambio de juego que la app no vio") + ")");
                        }
                        // 16-11 tarde (APAGADO): elegir usuario o el aviso de cerrar el suspendido cancelados (otra casilla, o el HOME seguido)
                        if (inHomeNow) LaunchCancelCheck(DateTime.Now);
                    }
                    else
                    {
                        homeHits = 0;
                        strictHits = 0;
                        // (solo con la imagen de la consola: con "No Signal" no cuenta)
                        if (inHomeNow && homeSeen == DateTime.MinValue)
                        {
                            if (looksHome || library) homeSeen = DateTime.Now; // el HOME con el cursor abajo (16-11: o la biblioteca)
                            else StartGrab(kind, moved, screen, DateTime.Now);
                        }
                        // (16-11: la biblioteca sin la pastilla del nombre, con el cursor en las pestañas o moviendose, es como el HOME: no se
                        // ha salido de el. 00:40:14 y 00:45:56: contaba como imagen de juego y se "entraba" a los ~300 ms)
                        else if (inHomeNow && (looksHome || library))
                        {
                            noLabel = 0; // (noLabel = capturas SEGUIDAS fuera del HOME)
                            ResetOut(homeGame != null ? "\"" + homeGame + "\"" : "el juego");
                            lastThumb = null;
                            // 16-11: el cursor en "Ver más": lo de la casilla de antes (el juego, si estaba en uso) ya no vale, ya
                            if (tileHint && (homeGame != null || labelNoName || homeInUse))
                            {
                                Log.Write("HOME: seleccionado \"Ver más\" (la biblioteca, no es un juego)");
                                homeGame = null; homeIcon = null; homeShape = null; labelNoName = false; homeInUse = false; homeDock = true;
                            }
                            // Sigue el HOME sin juego seleccionado (cursor en la barra de abajo: eShop, ajustes...). Solo tras 3
                            // capturas seguidas (al pulsar A en un juego, el HOME se ve un instante sin el nombre y se olvidaba)
                            if (++homeNoSel >= 3)
                            {
                                if (homeGame != null || labelNoName) Log.Write("HOME: ningun juego seleccionado");
                                homeGame = null;
                                homeIcon = null;
                                homeShape = null;
                                labelNoName = false;
                                homeDock = true;
                                noLabel = 0;
                            }
                            LaunchCancelCheck(DateTime.Now); // (16-11 tarde)
                        }
                        // Ni el nombre ni el HOME (otra imagen: pantalla negra, el juego...): fuera del HOME. Cuenta aunque no se
                        // haya visto que juego estaba seleccionado (03-11: se entro en uno antes de la primera lectura y la app se
                        // quedo 48 s creyendo que seguia en el HOME); no si el cursor estaba en la barra de abajo (eShop, ajustes)
                        else if (inHomeNow)
                        {
                            noLabel++;
                            homeNoSel = 0;
                            OutGrab(kind, screen, picker, DateTime.Now);
                            if (dimScreen != null) DimRead(dimScreen, powerMenu, DateTime.Now);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Error("Leyendo el HOME", ex); }
            finally { nameFullGrab = wantFull; grabBusy = false; }
            if (diagDark >= 0 && !goHome && !confirm) MaybeDitherBurst(diagDark);
            // 09-11: en el HOME a 4K, la decision (entrar en el juego) sin esperar al siguiente tick de GrabLoop / WatchLoop
            // (19-11, DETECCION: con menu propio, tambien jugando: la entrada al menu esperaba al HomeTick, hasta 0,5 s)
            if ((InHome || Consolas.Actual.MenuPropio) && !goHome && signalOn && lsReady && !userExit) RunHomeTick(DateTime.Now);
            if (goHome)
            {
                Log.Write("HOME visto en capturas seguidas (" + (long)(DateTime.Now - homeFirstHit).TotalMilliseconds + " ms entre la primera y la decision" +
                          (homeRef == null ? "; primera vez en este PC" : "") + ")");
                Consolas.VistoHomeNintendo(homeRef != null, 0); // (18-11: con consola=auto, el HOME de la Switch 2 confirma la consola)
                EnterHomeNative();
            }
            else if (confirm)
            {
                // La segunda captura enseguida (la animacion del HOME ya ha acabado)
                Thread.Sleep(confirmMs == 250 ? 150 : confirmMs);
                if (!InHome && !grabBusy && !homeBusy && !passesBusy && relaunchSize == null && ipcUp && DateTime.Now >= switchUntil)
                    RequestGrab(true);
            }
        }

        bool homeDock;                  // en el HOME: el cursor esta en la barra de abajo (lo que se abra no es un juego)
        int noSignalHits;               // capturas seguidas con la pantalla "No Signal" de la capturadora
        volatile bool consoleSeen;      // se ha visto la imagen de la consola en esta sesion
        bool homeLib;                   // 16-11: en el HOME: lo seleccionado es de la biblioteca (Programas), no de la fila del HOME
        bool powerTile;                 // 16-11: en el HOME: el cursor en el boton de apagado / modo de espera de la barra

        // ---- 16-11: "No Signal" que no es un apagado ----
        // La capturadora enseña "No Signal" con la consola apagada o en espera, pero tambien unos segundos cuando la consola cambia de modo
        // de video: arrancando RESIDENT EVIL requiem (14-11 23:57:32, 2,8 s tras elegir usuario; 16-11 00:46:33): las dos veces la app se
        // cerro con la consola encendida (vuelta a abrir, la imagen del juego ya estaba: menos de 21 s sin señal). Ahora se cierra si dura:
        //  - nada tras el menu de apagado (el HOME atenuado con "Apagar", "Reiniciar"...) o saliendo del HOME por su boton de apagado: como
        //    antes (3 capturas, ~1-1,5 s; los apagados del log van asi: HOME atenuado, negro y "No Signal" en 1,5-2 s)
        //  - 16-11 tarde (APAGADO): apagado_espera_home_s (0, nada) en el HOME sin nada arrancando (HomeIdle). 14:02: el aviso de cerrar el
        //    suspendido cancelado dejaba la espera de arrancar (25 s) y la app tardo 26 s en cerrarse; 09:58, en el HOME, 6 s
        //  - apagado_espera_arranque_s (25) arrancando un juego: tras elegir usuario o el aviso de cerrar el suspendido (16-11 tarde: hasta
        //    que se cancela, LaunchCancelCheck), y hasta 60 s despues de entrar en un juego (de vuelta en el HOME no: HomeIdle)
        //  - apagado_espera_s (3; antes 6) si no (jugando, recien vuelto al HOME, saliendo del HOME hacia un juego)
        // 16-11 tarde: lo que toca esperar se decide al empezar cada "No Signal" y solo puede crecer mientras dure (si no, al pasar a "en el HOME
        // sin nada arrancando" a mitad, recien vuelto de un juego, se cerraba sin esperar); el script de mpv y el motor hacen lo mismo
        // Mientras, el script de mpv tapa la pantalla "No Signal" con su aviso (pastilla "Sin señal de la consola") y espera lo mismo
        // (switch2-launch / switch2-powermenu le dicen cual toca; 16-11 tarde: en el HOME sin nada arrancando, switch2-powermenu 3 cada 0,5 s
        // desde MpvLinkLoop, HomeIdleCmds): suele cerrar el antes; lo de aqui es por si no
        DateTime noSignalFirst = DateTime.MinValue;
        DateTime powerMenuAt = DateTime.MinValue;   // el menu de apagado visto (o salir del HOME desde su boton)
        DateTime launchUntil = DateTime.MinValue;   // arrancando un juego: hasta cuando
        // 16-11 tarde (APAGADO): de que arranque: cuando se marco, si fue al entrar en el juego (si no: elegir usuario o el aviso de cerrar
        // el suspendido, que se pueden cancelar) y la casilla del HOME que se arrancaba (su forma, homeShape)
        DateTime launchMarkAt = DateTime.MinValue;
        bool launchEntered;
        byte[] launchShape;
        DateTime homeArrivedAt = DateTime.MinValue; // 16-11 tarde: la vuelta al HOME desde un juego (EnterHomeNative)
        DateTime lastEnterAt = DateTime.MinValue;   // 16-11 tarde: la ultima entrada en un juego confirmada (DecideHomeNative)
        bool gameSeenOut;                           // 16-11 tarde: fuera del HOME: se ha visto imagen de juego (se reanuda o arranca)
        // 16-11 tarde: el HOME atenuado se lee (OCR) cada vez que sale y, si sigue, cada DimOcrEverySecs, como mucho DimMaxReads veces
        // (antes una vez por salida: 14:02:49 el menu de apagado o lo que fuera, leido entrando, no se reconocio)
        DateTime lastDimOcrAt = DateTime.MinValue;
        bool dimLast;                               // la captura anterior del HOME era el HOME atenuado
        int dimReads;                               // lecturas de este HOME atenuado
        const int DimMaxReads = 4;
        double nsHoldNow = -1;                      // 16-11 tarde: la espera de este "No Signal" (decidida al empezar; solo crece)
        string nsWhyNow;
        bool homeIdleLogged;                        // 16-11 tarde: lo ultimo visto de "en el HOME sin nada arrancando" (para el log)
        DateTime homeIdleSaidAt = DateTime.MinValue; // (y cuando se dijo en el log)
        const double PowerMenuSecs = 20, LaunchSecs = 60;

        double NoSignalHold(DateTime now, out string why)
        {
            if (powerMenuAt != DateTime.MinValue && (now - powerMenuAt).TotalSeconds < PowerMenuSecs) { why = "tras el menu de apagado"; return 0; }
            string busy;
            if (HomeIdle(now, out busy)) { why = "en el HOME sin nada arrancando"; return cfg.NoSignalHoldHomeSecs; }
            if (now < launchUntil) { why = "arrancando un juego"; return cfg.NoSignalHoldLaunchSecs; }
            why = busy ?? "jugando";
            return EsperaSinSenalJugando(cfg); // (18-11, CONSOLAS: la del ini y, con una consola sin HOME reconocido, como poco la suya)
        }

        // 16-11 tarde (APAGADO): ¿en el HOME sin nada arrancando? Entonces "No Signal" es la consola apagada o en espera (boton de encendido,
        // menu de apagado...): sin esperar. No lo esta: recien abierta la app sin ver aun el HOME; recien vuelto de un juego
        // (apagado_home_tras_juego_s); arrancando un juego (elegir usuario o el aviso de cerrar el suspendido, aun sin cancelar; uno ya entrado
        // no cuenta de vuelta en el HOME: suspendido no cambia de modo, y al reanudarlo vuelve su espera); con una entrada ya confirmada; o
        // fuera del HOME con imagen de juego (se reanuda o arranca) o con un juego que no esta en uso seleccionado (puede ser uno que arranca
        // sin elegir usuario: 09:55:17 FC 26, HOME atenuado y negro como al ponerla en espera). Con el juego en uso, una casilla de la
        // consola o sin leer ninguna, el HOME atenuado y negro es ponerla en espera (15-11 11:39, 12:17; 16-11 14:02). busy = por que no
        bool HomeIdle(DateTime now, out string busy)
        {
            busy = null;
            // 19-11: en el menu de la PS5 no se sabe si se esta arrancando un juego (con HDR o 120 Hz cambia de modo de video: "No Signal" unos
            // segundos): su espera, salvo tras su menu de apagado (PowerMenuSeen, src\MenuPs5.cs)
            if (!Consolas.Actual.TieneHome) { if (InHome) busy = "en el menu de la consola"; return false; }
            lock (homeSync)
            {
                if (!InHome) return false;
                if (homeSeen == DateTime.MinValue) { busy = "recien abierta la app, sin ver aun el HOME"; return false; }
                if (homeArrivedAt != DateTime.MinValue && (now - homeArrivedAt).TotalSeconds < cfg.NoSignalHomeAfterGameSecs)
                {
                    busy = "recien vuelto al HOME desde un juego";
                    return false;
                }
                if (lastEnterAt != DateTime.MinValue && lastEnterAt >= homeArrivedAt) { busy = "entrando en un juego"; return false; }
                if (now < launchUntil && !(launchEntered && homeArrivedAt > launchMarkAt)) return false; // (arrancando un juego)
                if (outSince != DateTime.MinValue)
                {
                    if (gameSeenOut) { busy = "saliendo del HOME hacia un juego"; return false; }
                    if ((homeGame != null || labelNoName) && !homeInUse && !homeDock)
                    {
                        busy = "saliendo del HOME con un juego que no esta en uso seleccionado (puede estar arrancando)";
                        return false;
                    }
                }
                return true;
            }
        }

        // 16-11 tarde (APAGADO): para el script de mpv y el motor de un enlace (MpvLinkLoop, cada 0,5 s): en el HOME sin nada arrancando,
        // switch2-powermenu 3 ("No Signal" = apagada, sin esperar; tambien les quita la espera de arrancar, que en el HOME no cuenta). Al
        // dejar de estarlo (se reanuda o arranca un juego...), switch2-powermenu 0 (fuera) y, si sigue la espera de arrancar (un juego entrado
        // hace menos de 60 s que se reanuda, o uno que empieza a arrancar), switch2-launch con lo que le queda. Mientras dure el menu de
        // apagado de verdad (switch2-powermenu 20) nada. idle = lo ultimo mandado por ese enlace. null si no toca mandar nada
        List<string> HomeIdleCmds(DateTime now, bool current, ref bool idle)
        {
            string busy;
            bool nowIdle = HomeIdle(now, out busy);
            if (current && nowIdle != homeIdleLogged)
            {
                homeIdleLogged = nowIdle;
                // (al log una vez por visita al HOME, no en cada salida y vuelta con un juego que no esta en uso seleccionado)
                if (nowIdle && (homeIdleSaidAt == DateTime.MinValue || homeIdleSaidAt < homeArrivedAt || (now - homeIdleSaidAt).TotalSeconds > 60))
                {
                    homeIdleSaidAt = now;
                    Log.Write("En el HOME sin nada arrancando: si sale \"No Signal\", la consola se apaga (se cierra enseguida)");
                }
            }
            double pr = powerMenuAt == DateTime.MinValue ? 0 : PowerMenuSecs - (now - powerMenuAt).TotalSeconds;
            if (pr > 0.5) return null;
            if (nowIdle)
            {
                idle = true;
                return new List<string> { JsonCmd("script-message", "switch2-powermenu", "3") };
            }
            if (!idle) return null;
            idle = false;
            List<string> cmds = new List<string> { JsonCmd("script-message", "switch2-powermenu", "0") };
            double lr = (launchUntil - now).TotalSeconds;
            if (lr > 0.5) cmds.Add(JsonCmd("script-message", "switch2-launch", Ms(Math.Ceiling(lr))));
            return cmds;
        }

        // Una captura de la app: ¿toca cerrar por "No Signal"? (3 capturas seguidas y el tiempo de NoSignalHold, mas 1 s: antes el script)
        // 16-11 tarde: la espera de este "No Signal" se decide en su primera captura y solo crece (nsHoldNow); en el log por que
        bool NoSignalTick(bool noSignal, DateTime now, out string detail)
        {
            detail = null;
            if (!noSignal)
            {
                if (noSignalHits >= 2 && consoleSeen)
                    Log.Write("Pantalla No Signal " + ((now - noSignalFirst).TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture) + " s (" + noSignalHits +
                              " capturas) y ya se ve la consola: no era un apagado (un cambio de modo de video), se sigue");
                noSignalHits = 0;
                nsHoldNow = -1;
                return false;
            }
            if (noSignalHits == 0) noSignalFirst = now;
            noSignalHits++;
            string why;
            double hold = NoSignalHold(now, out why), secs = (now - noSignalFirst).TotalSeconds;
            if (hold > nsHoldNow) { nsHoldNow = hold; nsWhyNow = why; }
            hold = nsHoldNow;
            why = nsWhyNow;
            if (hold > 0) hold += 1;
            if (noSignalHits == 1 && consoleSeen)
                Log.Write("Pantalla No Signal en una captura de la app (" + why + "): " + (hold > 0 ? "se cierra si dura " + Ms(hold) + " s" : "se cierra enseguida"));
            else if (noSignalHits == 3 && hold > 0 && consoleSeen)
                Log.Write("Pantalla No Signal en 3 capturas seguidas (" + why + "): se cierra si dura " + Ms(hold) + " s (puede ser un cambio de modo de video)");
            if (noSignalHits < 3 || secs < hold) return false;
            detail = noSignalHits + " capturas, " + secs.ToString("0.0", CultureInfo.InvariantCulture) + " s seguidos, " + why;
            return true;
        }

        // Arrancando un juego (con homeSync o no): "No Signal" puede ser el cambio de modo de video, hasta LaunchSecs desde ahora
        // 16-11 tarde (APAGADO): entered = al entrar en el juego; si no (elegir usuario, el aviso de cerrar el suspendido) se puede cancelar
        // (LaunchCancelCheck): se apunta la casilla que se arranca
        void MarkLaunch(DateTime now, string why, bool entered = false)
        {
            // 16-11 (revision): arrancar un juego anula el menu de apagado visto antes (abierto y cancelado: si no, 20 s sin espera)
            powerMenuAt = DateTime.MinValue;
            launchMarkAt = now;
            launchEntered = entered;
            if (!entered) launchShape = homeShape;
            DateTime until = now.AddSeconds(LaunchSecs);
            if (until <= launchUntil) return;
            bool was = now < launchUntil;
            launchUntil = until;
            lock (cmdQueue) cmdQueue.Add(JsonCmd("script-message", "switch2-launch", Ms(LaunchSecs)));
            if (!was) Log.Write("Arrancando un juego (" + why + "): si sale \"No Signal\", se espera hasta " + Ms(cfg.NoSignalHoldLaunchSecs) +
                                " s antes de dar la consola por apagada (cambio de modo de video)");
        }

        // 16-11 tarde (APAGADO): tras cada captura del HOME (con homeSync): de vuelta en el HOME tras elegir usuario o el aviso de cerrar el
        // suspendido sin haber entrado en nada, con el cursor en otra casilla (14:02:48 "Tarjetas de juego virtuales": 2,6 s despues la
        // consola en espera y 26 s hasta cerrarse) o con el HOME a la vista seguido apagado_fin_arranque_s (mas que el HOME de paso tras
        // elegir usuario), ya no se arranca nada: fuera la espera de arrancar (tambien en el script de mpv y el motor: switch2-launch 0)
        void LaunchCancelCheck(DateTime now)
        {
            if (launchEntered || now >= launchUntil || outSince != DateTime.MinValue) return;
            bool moved = homeDock || (launchShape != null && homeShape != null && !HomeLabel.Same(homeShape, launchShape));
            bool steady = homeBackAt != DateTime.MinValue && homeBackAt > launchMarkAt && !pickerBefore &&
                          (now - homeBackAt).TotalSeconds >= cfg.LaunchCancelSecs;
            if (!moved && !steady) return;
            launchUntil = DateTime.MinValue;
            if (powerMenuAt == DateTime.MinValue || (now - powerMenuAt).TotalSeconds >= PowerMenuSecs)
                lock (cmdQueue) cmdQueue.Add(JsonCmd("script-message", "switch2-launch", "0"));
            Log.Write("De vuelta en el HOME sin arrancar nada (" + (moved ? "el cursor en otra casilla" : Ms(cfg.LaunchCancelSecs) + " s con el HOME a la vista") +
                      "): fuera la espera de arrancar un juego");
        }

        // 16-11 tarde (APAGADO): ¿leer (OCR, 60-150 ms) este HOME atenuado? Cada vez que sale (la captura anterior no lo era; como poco 0,4 s
        // desde la ultima lectura) y, si sigue, otra cada apagado_ocr_atenuado_s hasta DimMaxReads (el menu de apagado aun entrando no se lee
        // bien). Con homeSync; dim = false para las demas capturas fuera del HOME (ResetOut lo deja en false con el HOME)
        bool DimOcrDue(bool dim, DateTime now)
        {
            bool fresh = dim && !dimLast;
            dimLast = dim;
            if (!dim) { dimReads = 0; return false; }
            if (fresh) dimReads = 0;
            if (dimReads >= DimMaxReads) return false;
            if ((now - lastDimOcrAt).TotalSeconds < (fresh ? 0.4 : cfg.DimOcrEverySecs)) return false;
            dimReads++;
            lastDimOcrAt = now;
            return true;
        }

        // El menu de apagado (o salir del HOME por su boton): "No Signal" en los PowerMenuSecs siguientes = apagado, sin esperar
        void PowerMenuSeen(DateTime now, string what)
        {
            bool was = powerMenuAt != DateTime.MinValue && (now - powerMenuAt).TotalSeconds < PowerMenuSecs;
            powerMenuAt = now;
            launchUntil = DateTime.MinValue;
            lock (cmdQueue) cmdQueue.Add(JsonCmd("script-message", "switch2-powermenu", Ms(PowerMenuSecs)));
            if (!was) Log.Write((Consolas.Actual.TieneHome ? "Fuera del HOME: " : "Consola: ") + what + ": si sale \"No Signal\", la consola se apaga (se cierra enseguida)");
        }

        // El texto del HOME atenuado (OCR; 16-11 tarde: cada vez que sale, DimOcrDue): el menu de apagado, o el aviso de cerrar el suspendido
        // (se va a arrancar otro). 16-11 tarde: el aviso por sus frases ("programa" suelto tambien es la pestaña "Programas" de la biblioteca,
        // y ahora se lee mas veces: atenuada al ponerla en espera habria puesto la espera de arrancar)
        void DimRead(string text, bool power, DateTime now)
        {
            if (power) PowerMenuSeen(now, "el menu de apagado de la consola (\"" + ShortText(text) + "\")");
            else
            {
                string n = (text ?? "").ToLowerInvariant();
                if (n.Contains("cerrar el") || n.Contains("suspendid") || n.Contains("close the") || n.Contains("suspended"))
                    MarkLaunch(now, "el aviso de cerrar el programa suspendido");
            }
        }

        // (16-11 tarde, APAGADO: fuera EngineNoSignalHold, el respaldo para motores sin espera propia. Todos los de hoy esperan como el
        // script de mpv (PowerWatch, desde el de las 04:30) y ese switch2-launch de "hold + 3 s" les ponia la espera de arrancar (25 s)
        // mientras durase: jugando el motor no cerraba hasta ~9 s y decidia la app con sus capturas cada 1,2 s; ademas les quitaba la de
        // "en el HOME sin nada arrancando")

        // La consola se ha apagado o esta en espera (la pantalla "No Signal": la vio la app en 3 capturas seguidas o el script de
        // mpv, switch2-poweroff): mpv, el audio y LS se cierran ya, a la vez y sin esperar a ninguno, y lo de segundo plano, el
        // plan de energia y Afterburner se devuelven mientras (ExitRestore). RunMpv no espera a mpv mas de ~1 s y la app sale
        // (flag de apagado). 09-11: antes, mpv matado y luego uno tras otro: esperar a que terminase (~0,2 s), cerrar LS
        // esperandolo, la ventana, segundo plano y el plan de energia (~0,45 s): 0,5-1,1 s de "No Signal" a "Fin Switch2"
        public static volatile Stopwatch PowerOffClock; // desde que se vio la consola apagada (para el log)
        volatile int powerOffPid = -1;  // el mpv cerrado por PowerOffNow
        readonly object powerOffSync = new object();
        bool powerOffOffSaid;

        void PowerOffNow(string reason) { PowerOffNow(reason, null); }

        void PowerOffNow(string reason, string detail)
        {
            Process p = mpv;
            if (p == null) return;
            if (!powerOffDetect)
            {
                if (!powerOffOffSaid) Log.Write("Pantalla No Signal (" + reason + "), pero la deteccion de apagado esta desactivada: no se cierra");
                powerOffOffSaid = true;
                return;
            }
            lock (powerOffSync)
            {
                if (powerOffPid == p.Id) return; // (ya lo vieron la app o el script)
                powerOffPid = p.Id;
                PowerOffClock = Stopwatch.StartNew();
            }
            Log.Write("Pantalla No Signal (" + (reason == "no-signal-app" ? "capturas seguidas de la app" : "script de mpv, " + reason) +
                      (string.IsNullOrEmpty(detail) ? "" : ": " + detail) + "): la consola se ha apagado, se cierra todo");
            CeEspera.AlApagarConsola(cfg.ShutdownPc); // (21-11, 1.2.1: con apagar_pc=1, la cubierta negra y la barra oculta ANTES de quitar la imagen; src\Lanzador\EsperaCubierta.cs)
            try { File.WriteAllText(Paths.FlagFile, reason + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); }
            catch (Exception ex) { Log.Error("Escribiendo flag", ex); }
            try { if (!p.HasExited) p.Kill(); } catch (Exception ex) { Log.Error("Cerrando mpv", ex); }
            audioClosing = true;
            Process a = audioProc;
            try { if (a != null && !a.HasExited) a.Kill(); } catch { }
            Log.Write("Apagado: mpv y el audio cerrados en " + PowerOffClock.ElapsedMilliseconds +
                      " ms (sin esperarlos); se devuelven a la vez lo de segundo plano, el plan de energia y Afterburner");
            ExitRestore.Start();
        }

        byte[] sysShape;                // en el HOME: la forma del ultimo nombre de una casilla de la consola (no juego)
        byte[] homeShape;               // en el HOME: la forma del nombre azul del juego de homeGame (cambia = otro juego)
        byte[] strictShape;             // jugando sin el HOME conocido: lo visto en la captura anterior
        double[] strictTop;
        int strictHits;
        double[] homeRef, savedRef;     // las franjas del HOME (games\home_ref.txt)

        // Las franjas del HOME, guardadas para la proxima vez (solo si han cambiado: la hora, la bateria...)
        void SaveRef(double[] r)
        {
            if (savedRef != null && HomeLabel.Diff(r, savedRef) <= 3) return;
            savedRef = r;
            SaveText(Paths.HomeRefFile, HomeLabel.NumsText(r));
        }

        double[] LoadRef()
        {
            savedRef = HomeLabel.ParseNums(LoadText(Paths.HomeRefFile), 32 * 6 + 32 * 5);
            return savedRef;
        }

        GameProfile CurrentProfile(string name)
        {
            GameProfile p = new GameProfile();
            p.Name = name;
            p.Capture = relaunchSize ?? cfg.VideoSize;
            p.Level = dlss5Level;
            p.Passes = dlss5Passes;
            p.Dlss5 = dlss5Shown;
            p.Scaler = dlssScalerOn;
            p.ScalerPick = scalerPick;   // (1.2.01)
            p.Cap = cfg.CapMode;
            p.Mode120 = cfg.Mode120; // 20-11 (HZ120)
            p.Fg = fgWanted;
            p.Pick60 = cfg.FgPick60; p.Pick30 = cfg.FgPick30; // (19-11, GENERACION)
            return p;
        }

        // Lo cambiado con el mando, al juego en el que se esta. 15-11: con profileSync, el juego y los ajustes de la misma vez (al
        // entrar en otro, LeaveHome / LeaveHomeGrace los cambian dentro del mismo cerrojo)
        void SaveCurProfile()
        {
            GameProfile p;
            lock (profileSync)
            {
                string g = curGame;
                if (applying || g == null) return;
                GameProfile now = CurrentProfile(g);
                p = GameProfiles.Find(g) ?? now;
                GameProfiles.SaveSettings(p, now);
            }
            Log.Write("Ajustes de \"" + p.Name + "\" guardados: " + p.Summary());
        }

        // 15-11: el nombre leido en el HOME es mejor para el perfil p (GameProfiles.Improve: un "Juego N", un nombre cortado): se le
        // pone y se cambia en lo que usa el nombre (curGame, el juego del HOME, last_game.txt). Fuera de homeSync
        // 16-11: firm = NameReads.Firm (una lectura suelta no cambia el nombre de un perfil)
        void ImproveProfile(GameProfile p, string name, bool sure, string why, bool firm)
        {
            bool wasSure = p.NameSure;
            string merged;
            List<string> gone = GameProfiles.Improve(p, name, sure, sure || firm, out merged);
            if (gone == null)
            {
                if (!wasSure && p.NameSure) Log.Write("Perfil \"" + p.Name + "\": nombre confirmado (" + why + ")");
                return;
            }
            string now = p.Name;
            foreach (string old in gone)
            {
                lock (profileSync) { if (curGame == old) curGame = now; }
                lock (homeSync) { if (homeGame == old) homeGame = now; }
                if (LoadText(Paths.LastGameFile) == old) SaveText(Paths.LastGameFile, now);
            }
            Log.Write("Perfil \"" + gone[0] + "\" -> \"" + now + "\" (" + why + (p.NameSure ? ", nombre seguro" : ", aun sin confirmar: se sigue leyendo") + ")" +
                      (merged != null ? "; juntado con " + merged + ", el mismo juego: se quedan los ajustes de este y los iconos de los dos" : ""));
        }

        static Bitmap CopyOf(Bitmap src, Rectangle r, int w, int h)
        {
            Bitmap b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                bool same = w == r.Width && h == r.Height;
                g.InterpolationMode = same ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(src, new Rectangle(0, 0, w, h), r, GraphicsUnit.Pixel);
            }
            return b;
        }

        // 15-11: una lectura del nombre fallida o que no encaja con las demas: la captura (a 1920x1080 como mucho) y el recorte del
        // nombre tal cual (lo que mira el OCR) a games\diag_home, para verlas y repetirlas con --test-home (frase y nombre: lecturas.txt).
        // Como mucho diag_home= por sesion (12; 0 = nada), 2 por seleccion y las 40 imagenes mas nuevas. Se escriben en segundo plano
        string SaveHomeDiag(Bitmap full, Rectangle fb, string why, string raw, NameReads nr)
        {
            if (cfg.DiagHome <= 0 || diagHomeSaved >= cfg.DiagHome || nr.Saved >= 2) return null;
            nr.Saved++;
            diagHomeSaved++;
            try
            {
                string dir = Path.Combine(Paths.Datos, @"games\diag_home");
                string tag = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "_" + why;
                int fw = Math.Min(full.Width, 1920), fh = full.Width > 1920 ? full.Height * 1920 / full.Width : full.Height;
                Bitmap frame = CopyOf(full, new Rectangle(0, 0, full.Width, full.Height), fw, fh);
                Bitmap crop = fb.Width > 4 && fb.Height > 4 ? CopyOf(full, fb, fb.Width, fb.Height) : null;
                string note = "lectura " + nr.Attempts + " de la seleccion, OCR \"" + ShortText(raw) + "\", mejor hasta ahora \"" + (nr.Best ?? "") +
                              "\", captura " + full.Width + "x" + full.Height + ", recorte " + fb;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                        frame.Save(Path.Combine(dir, tag + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        if (crop != null) crop.Save(Path.Combine(dir, tag + "_nombre.png"), System.Drawing.Imaging.ImageFormat.Png);
                        File.AppendAllText(Path.Combine(dir, "lecturas.txt"), tag + ": " + note + "\r\n", new UTF8Encoding(false));
                        FileInfo[] all = new DirectoryInfo(dir).GetFiles("*.png");
                        Array.Sort(all, delegate (FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                        for (int i = 40; i < all.Length; i++) { try { all[i].Delete(); } catch { } }
                    }
                    catch (Exception ex) { Log.Error("Guardando el diagnostico del HOME", ex); }
                    finally { frame.Dispose(); if (crop != null) crop.Dispose(); }
                });
                return @"games\diag_home\" + tag + ".png";
            }
            catch (Exception ex) { Log.Error("Guardando el diagnostico del HOME", ex); return null; }
        }

        // 15-11: el juego elegido en el HOME en el que se va a entrar (o "" si no se ha leido su nombre), mientras se espera a que cargue
        void SavePending(string name)
        {
            if (pendingSaved) return;
            pendingSaved = true;
            if (testMode) return; // (--test-entrada: sin escribir nada)
            SaveText(PendingGameFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "|" + (name ?? ""));
        }

        void ClearPending()
        {
            pendingSaved = false;
            if (testMode) return;
            try { if (File.Exists(PendingGameFile)) File.Delete(PendingGameFile); } catch { }
        }

        // El juego pendiente si es de hace menos de maxAge (null si no hay)
        static string LoadPending(TimeSpan maxAge)
        {
            string s = LoadText(PendingGameFile);
            if (string.IsNullOrEmpty(s)) return null;
            int bar = s.IndexOf('|');
            DateTime t;
            if (bar < 0 || !DateTime.TryParseExact(s.Substring(0, bar), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return null;
            if (DateTime.Now - t > maxAge || t > DateTime.Now.AddMinutes(1)) return null;
            return s.Substring(bar + 1).Trim();
        }

        // ---- HOME a 4K nativo <-> juego ----

        // Jugando se ha vuelto al HOME: se cierra LS y mpv pasa a la captura de 4K tal cual (sin cerrarlo)
        // 10-11 (home_espera_nativo_s): antes, unos segundos con LS escalando el HOME (homeGrace): si se vuelve al juego no se ha
        // cerrado nada. Pasado ese tiempo con el HOME a la vista, GraceToNative (imagen congelada y fundido)
        void EnterHomeNative()
        {
            if (!cfg.HomeNativeEnabled || cfg.HomeNative || homeGrace || homeBusy || passesBusy || relaunchSize != null) return;
            AjustesQuitarYa("al menu de la consola", false);
            // (revision 10-11: no con la captura de 4K, ventana de 4K: las capturas del HOME cada 0,5 s a 4K atascan mpv con LS encima)
            // 15-11: con DLSS 5 encendido, a 4K nativo al momento (DLSS 5 sobre el HOME: tono, texto e interfaz con errores; en 60 con
            // generacion ademas sin su mascara de interfaz). La espera solo sin DLSS 5
            bool grace = false;   // (1.2.4 ronda 6, QUITAFG: seguir viendo el HOME con Magpie unos segundos; sin Magpie, nunca)
            if (grace)
                Log.Write("HOME de la consola: se sigue viendo con Magpie " + Ms(cfg.HomeGraceSecs) + " s por si se vuelve al juego " +
                          "(sin cerrar nada); luego a 4K nativo" + (curGame != null ? " (\"" + curGame + "\" sigue con sus ajustes guardados)" : ""));
            else
            {
                Log.Write("HOME de la consola: a 4K nativo a 60 fps" +
                          (curGame != null ? " (\"" + curGame + "\" sigue con sus ajustes guardados)" : ""));
                pendingPassesRestart = false;
            }
            lock (homeSync)
            {
                noLabel = 0; labelNoName = false; homeSeen = DateTime.Now; homeShape = null; homeGame = null; strictHits = 0;
                homeInUse = false; lastThumb = null; ResetOut(null);
                homeLib = powerTile = false; // (16-11)
                homeArrivedAt = homeSeen;    // (16-11 tarde: recien vuelto de un juego, ver HomeIdle)
                if (grace)
                {
                    graceSince = graceMoved = graceLastHome = DateTime.Now;
                    graceThumb = null;
                    homeGrace = true;
                }
            }
            if (!grace) StartHomeSwitch(true, "HOME", "4K nativo");
        }

        // Del HOME a un juego. La app recuerda cada juego en el que se entra: su icono del HOME y sus ajustes (los que se
        // cambian con el mando dentro de el). Ya conocido (por su icono, o por su nombre si se guardo antes sin icono): sus
        // ajustes. Nuevo: se guarda con su icono, su nombre (o "Juego N" si no se pudo leer) y los ajustes que haya ahora,
        // sin nada puesto de antemano por juego
        // El perfil del juego en el que se entra de verdad: por su icono, por su nombre (y se le guarda el icono) o uno nuevo con los
        // ajustes de ahora. Sin nombre ni icono, lo ultimo elegido (sin nombre)
        // 15-11: el nombre (name) es el mejor de las lecturas del HOME (NameReads) y enterSure dice si esta confirmado. Uno reconocido
        // por su icono con un nombre peor guardado ("Juego N", cortado) se queda con el leido (ImproveProfile). Uno nuevo se guarda
        // ya (entero: GameProfiles.WriteAtomic) con los ajustes de ahora, y si su nombre no esta confirmado se sigue leyendo en las
        // siguientes visitas al HOME
        GameProfile EntryProfile(string name, double[] icon)
        {
            // 15-11: una casilla de la consola ("Página de <usuario>", "Configuración de la consola"...) no es un juego: ni perfil
            // nuevo ni nombre para otro (lo ultimo elegido, sin nombre)
            if (HomeHints.IsSystemName(name))
            {
                enterSure = false;
                Log.Write("Al entrar: \"" + name + "\" es una casilla de la consola, no un juego: sin perfil");
                return CurrentProfile(null);
            }
            bool sure = enterSure && name.Length > 0;
            bool confirmed = sure && enterConfirmed;
            enterSure = false;
            enterConfirmed = false;
            // (16-11: el icono gana: reconocido por el, el nombre leido solo lo mejora si es firme, ImproveProfile)
            GameProfile p = GameProfiles.FindByIcon(icon);
            if (p != null && name.Length > 0 && name != p.Name) ImproveProfile(p, name, confirmed, "al entrar", sure);
            if (p == null && name.Length > 0)
            {
                p = GameProfiles.Find(name);
                if (p != null)
                {
                    // (16-11: su icono solo con el nombre firme: con una lectura suelta podria ser el de otro juego)
                    if (icon != null && sure)
                    {
                        GameProfiles.AddIcon(p, icon);
                        Log.Write("Icono de \"" + p.Name + "\" guardado: la proxima vez se le reconoce por el");
                    }
                    if (name != p.Name) ImproveProfile(p, name, confirmed, "al entrar", sure);
                }
            }
            // 16-11: un juego nuevo solo con su nombre firme (NameReads.Firm); si no ("LLó" a las 00:45:56, una lectura del arte de Fall Guys),
            // como sin nombre: "Juego N" con su icono del HOME (se le reconocera por el y su nombre se leera en el HOME), y sin icono (elegido
            // en la biblioteca) sin perfil: los ajustes de ahora, sin recordar nada
            if (p == null && name.Length > 0 && !sure)
            {
                Log.Write("Al entrar: \"" + name + "\" no es un juego conocido y su nombre no esta confirmado (" +
                          (icon != null ? "se guarda como juego sin nombre, con su icono del HOME" : "sin icono: juego sin identificar, con los ajustes de ahora") + ")");
                name = "";
            }
            if (p == null && (name.Length > 0 || icon != null))
            {
                p = CurrentProfile(name.Length > 0 ? name : GameProfiles.NewName());
                p.NameSure = confirmed;
                p.AddIcon(icon);
                GameProfiles.Save(p);
                Log.Write("Juego nuevo: \"" + p.Name + "\"" + (icon != null ? " (guardado con su icono del HOME)" : "") +
                          (name.Length == 0 ? " (sin nombre leido: se leera la proxima vez que se seleccione en el HOME)" :
                           sure ? "" : " (nombre sin confirmar: se seguira leyendo en el HOME)") +
                          ". Se recordaran sus ajustes (de partida, los de ahora: " + p.Summary() + ")");
            }
            if (p == null) p = CurrentProfile(null); // sin nombre ni icono: lo ultimo elegido
            return p;
        }

        // R3 del perfil de un juego: escalador normal (true) o el alternativo
        void SetScalerChoice(bool normal) { SetScalerChoice(normal, ""); }
        void SetScalerChoice(bool normal, string pick)
        {
            dlssScalerOn = normal;
            cfg.AltScalerOn = !normal;
            scalerPick = pick == "fsr" || pick == "ninguno" || pick == "xess" ? pick : "";   // (1.2.01; 23-11, 1.2.4 ronda 6 DLSSSR: XeSS SR del motor)
            cfg.ScalerPick = scalerPick;
        }
        string scalerPick = "";

        void LeaveHome(string name, double[] icon)
        {
            salioMenuEn = DateTime.Now;   // (1.0.12: el menu de ajustes espera unos segundos tras salir del menu de la consola)
            GameProfile p = EntryProfile(name, icon);
            bool guess = enterGuess;
            enterGuess = false;
            // (15-11: el juego y sus ajustes de la misma vez: SaveCurProfile no puede guardar entre medias los de antes con este nombre)
            lock (profileSync)
            {
                curGame = p.Name;
                curGameGuess = guess && p.Name != null;
                if (curGame != null) SaveText(Paths.LastGameFile, curGame);
                Log.Write("Juego: " + (curGame != null ? "\"" + curGame + "\"" : "(sin nombre)") + " -> sus ajustes: " + p.Summary());
                dlss5Level = p.Level;
                SaveText(Paths.Dlss5LevelFile, p.Level.ToString(CultureInfo.InvariantCulture));
                dlss5Passes = p.Passes;
                SaveText(Paths.Dlss5PassesFile, p.Passes.ToString(CultureInfo.InvariantCulture));
                bool pm120 = p.Mode120 && Puede120(p.Capture); // 20-11 (HZ120): 120 solo si la captura/consola/tele lo admiten; si no, 60
                cfg.CapMode = p.Cap && !pm120;
                cfg.Mode120 = pm120;
                SaveText(Paths.ModeFile, pm120 ? "120" : cfg.CapMode ? "30" : "60");
                want30 = cfg.CapMode;
                slow30 = fast60 = 0;
                cfg.VideoSize = p.Capture;
                SaveText(Paths.CaptureFile, p.Capture);
                SetDlss5State(p.Dlss5); // (Magpie se abre ya con el o sin el)
                SetScalerChoice(p.Scaler, p.ScalerPick);
                fgWanted = p.Fg;
                SaveText(Paths.FgFile, p.Fg ? "1" : "0");
                SetPicks(p.Pick60, p.Pick30); // (19-11, GENERACION: el tipo de generacion elegido con L3 en este juego)
            }
            ClearPending();
            lock (homeSync) { homeHits = 0; homeGame = null; homeShape = null; selReads = null; }
            if (MenuPs5SalirConMotor(curGame ?? "Juego", p.Summary())) return; // (19-11: PS5, el juego con Smooth Motion: una sola reapertura)
            StartHomeSwitch(false, curGame ?? "Juego", p.Summary());
        }

        // 10-11: del HOME con Magpie aun escalando (homeGrace) a un juego, ya entrado de verdad (DecideHomeNative). El mismo juego, o
        // otro con la misma config de Magpie: nada que reabrir, la imagen sigue tal cual (0 ms de negro). Si no, solo lo que cambia:
        // la captura sin cerrar mpv (otra ventana: mpv y Magpie se reabren con todo) y una sola reapertura de Magpie
        void LeaveHomeGrace(string name, double[] icon)
        {
            salioMenuEn = DateTime.Now;
            GameProfile p = EntryProfile(name, icon);
            bool guess = enterGuess;
            enterGuess = false;
            double inHome = (DateTime.Now - graceSince).TotalSeconds;
            // 15-11: applying ANTES de cambiar curGame, y los dos con profileSync. Antes curGame cambiaba primero (y homeGrace = false ya
            // dejaba usar el mando): un cambio del mando justo entre medias guardaba los ajustes del juego de antes con este nombre
            lock (profileSync)
            {
                applying = true;
                curGame = p.Name;
                curGameGuess = guess && p.Name != null;
            }
            try
            {
                homeGrace = false;
                lock (homeSync) { homeHits = 0; homeGame = null; homeShape = null; selReads = null; }
                if (curGame != null) SaveText(Paths.LastGameFile, curGame);
                ClearPending();
                string where = " (" + inHome.ToString("0.0", CultureInfo.InvariantCulture) + " s en el HOME, con Magpie escalando)";
                bool newWindow = Config.WindowFor(p.Capture) != Config.WindowFor(cfg.VideoSize);
                bool capture = p.Capture != cfg.VideoSize;
                dlss5Level = p.Level;
                SaveText(Paths.Dlss5LevelFile, p.Level.ToString(CultureInfo.InvariantCulture));
                dlss5Passes = p.Passes;
                SaveText(Paths.Dlss5PassesFile, p.Passes.ToString(CultureInfo.InvariantCulture));
                // 20-11 (HZ120): el modo del perfil (30/60/120, el 120 solo si cabe); con applying SetModeN no reabre Magpie (lo hace lo de abajo)
                { int pfps = (p.Mode120 && Puede120(p.Capture)) ? 120 : p.Cap ? 30 : 60; if (pfps != (cfg.Mode120 ? 120 : cfg.CapMode ? 30 : 60)) SetModeN(pfps); }
                if (p.Dlss5 != dlss5Shown) SetDlss5State(p.Dlss5);
                fgWanted = p.Fg;
                SaveText(Paths.FgFile, p.Fg ? "1" : "0");
                SetPicks(p.Pick60, p.Pick30); // (19-11, GENERACION: el tipo de generacion elegido con L3 en este juego)
                SetScalerChoice(p.Scaler, p.ScalerPick);
                // Otra ventana (la captura de 4K): mpv y Magpie se reabren y Magpie coge ya todo lo de arriba
                if (newWindow)
                {
                    Log.Write("Juego: " + (curGame != null ? "\"" + curGame + "\"" : "(sin nombre)") + " -> sus ajustes: " + p.Summary() + ": otra ventana" + where);
                    RequestCapture(p.Capture);
                    return;
                }
                if (capture) RequestCapture(p.Capture); // (sin cerrar mpv)
                // (1.2.4 ronda 6, QUITAFG: sin Magpie no hay config que reabrir: el motor coge lo nuevo en marcha -SyncEngine- y, si pide otra
                // identidad -cadena, Smooth Motion, refresco-, el relevo -CheckEngine*-; o otro motor -abajo-)
                pendingPassesRestart = false;
                Log.Write("Juego: " + (curGame != null ? "\"" + curGame + "\"" : "(sin nombre)") + " -> sus ajustes: " + p.Summary() + ": en marcha, sin reabrir nada" + where);
            }
            finally { applying = false; }
        }

        // Lo que elige la app, en la config (para escribirla o compararla)
        void PrepareCfg()
        {
            cfg.Passes30 = dlss5Passes;
            cfg.Level30 = dlss5Level;
            cfg.FgOff = !fgWanted || !cfg.FgAvailable;
            cfg.Dlss5Off = !dlss5Shown;
            cfg.AltScalerOn = !dlssScalerOn;
            cfg.ScalerPick = scalerPick;
            EscalaAuto.Juego = curGame; // (1.2.1: la escala de DLSS 5 aprendida va por juego, EscalaAuto.cs)
        }

        // (1.2.4 ronda 6, QUITAFG, fase 2: LsHardChanges era de Magpie; fuera)

        // Cambia mpv (ventana, filtros y captura) sin cerrarlo y, al ir a un juego, abre Magpie. Con el telon puesto
        // fromGrace (10-11): del HOME visto con Magpie (homeGrace) al de 4K nativo; lo del HOME sigue como estaba
        void StartHomeSwitch(bool toHome, string title, string subtitle, bool fromGrace = false)
        {
            Process p = mpv;
            if (p == null || p.HasExited) return;
            if (toHome) AjustesQuitarYa("al menu de la consola", false);
            homeSwitchToHome = toHome; // (22-11, 1.2.2: entrando en un juego el menu de ajustes se puede abrir, AjustesGuia.cs)
            homeBusy = true;
            passesBusy = true;
            padRestart = false;
            // 10-11 (telon_imagen_congelada): al HOME a 4K con Magpie escalando, el telon era la pantalla congelada (1.2.4 ronda 6, QUITAFG:
            // sin Magpie, no)
            bool frozen = false;
            // 16-11 (velocidad, dlss5_sin_telon_entrada): al juego con un motor en espera listo, sin telon: la imagen de la consola sigue
            // hasta que el motor da la suya encima (el telon lo pone, si hace falta, lo que falle)
            bool early = !toHome && !fromGrace && BridgeEntryReady();
            if (!frozen && !early) ShowCurtain(); // negro liso, sin texto: al entrar en un juego o volver al HOME no se ve nada mas
            Thread t = new Thread(delegate ()
            {
                Stopwatch sw = Stopwatch.StartNew();
                bool relaunched = false, curtainOff = false;
                Bandera finEntrada = new Bandera(); // 21-11 (1.2.1): para el puente tardio (src\SinTelon.cs)
                // (si se esta reabriendo Magpie por un boton, se espera a que acabe)
                bool gate = false;
                for (int gi = 0; gi < 400 && !(gate = TryGate()); gi++) Thread.Sleep(50);
                passesBusy = true;
                try
                {
                    if (frozen)
                    {
                        frozen = PutFrozenCurtain();
                        // Magpie se cierra con el telon ya opaco. Ese telon no se activa: mpv sigue delante y Magpie escalando hasta aqui
                        WaitCurtainOpaque();
                    }
                    string oldCapture = cfg.CaptureNow;
                    lock (lsModeSync)
                    {
                        lsReady = false;
                        cfg.HomeNative = toHome;
                        if (fromGrace) homeGrace = false; // (InHome sigue: ya es el HOME a 4K)
                    }
                    // 15-11: con motor=propio, del HOME al juego (o al reves) puede tocar el otro motor (DLSS 5 del perfil del juego, o que
                    // no quepa en el modo): entonces se reabre la imagen con el que toca en vez de cambiar sin cerrar
                    PrepareCfg();
                    if (cfg.EngineNow != Config.EngineActive)
                    {
                        Log.Write((toHome ? "Al HOME" : "Al juego") + ": toca " + (cfg.EngineNow ? "el motor propio" : "mpv sin el motor") + " (se reabre la imagen)");
                        relaunched = true;
                        switchUntil = DateTime.MinValue;
                        // 16-11 (velocidad, dlss5_sin_telon_entrada): al motor sin telon, como DLSS 5 apagado con el boton A: el motor despierta
                        // detras de mpv (la consola sigue a la vista) y se pone delante en cuanto da imagen; si no, el telon de siempre
                        if (early && cfg.EngineNow && StartRelevoToEngine(title, subtitle, p)) return;
                        if (early) ShowCurtain();
                        relaunchEngine = true;
                        QuitMpvForRelaunch();
                        return;
                    }
                    // (1.2.4 ronda 6, QUITAFG: sin Magpie, mpv solo cambia de captura y de ventana; antes Magpie se abria a la vez y el motor en
                    // espera hacia de puente mientras preparaba)
                    bool switched = SwitchMpvInPlace(p, oldCapture);
                    if (toHome)
                    {
                        if (switched)
                        {
                            if (frozen) Thread.Sleep(80);
                            HideCurtain(frozen ? cfg.CurtainFrozenFadeMs : 250);
                            curtainOff = true;
                            Log.Write("HOME a la vista: " + sw.ElapsedMilliseconds + " ms" + (frozen ? " (imagen congelada hasta aqui, luego " + cfg.CurtainFrozenFadeMs + " ms de fundido)" : ""));
                        }
                    }
                    if (!switched)
                    {
                        capEnSitioFallo = true; // 21-11 (1.2.1): la reapertura se vigila con menos tiempo (CapturaSegura.cs)
                        Log.Write("mpv no ha podido cambiar sin cerrarse: se reabre");
                        relaunched = true;
                        switchUntil = DateTime.MinValue;
                        relaunchSize = cfg.CaptureNow;
                        QuitMpvForRelaunch();
                        return;
                    }
                }
                catch (Exception ex) { Log.Error("Cambiando entre el HOME y el juego", ex); }
                finally
                {
                    // 21-11 (1.2.1): el cambio ha terminado: el puente tardio ya no se pone, y si se puso y Magpie no llego a presentar, fuera
                    finEntrada.Hecho = true;
                    if (!relaunched)
                    {
                        lsReady = true;
                        if (!curtainOff) HideCurtain();
                    }
                    passesBusy = false;
                    homeBusy = false;
                    if (gate) ReleaseGate();
                    Log.Write((toHome ? "HOME a 4K nativo" : title) + " listo: " + sw.ElapsedMilliseconds + " ms");
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // Lo que tiene que cambiar en mpv al pasar del HOME al juego o al reves, por la tuberia: la ventana (LS esta
        // cerrado: cambiarla con LS escalando rompe su escalador DLSS, ver Config.WindowFor), el filtro de repetidas, el
        // suavizado de bordes y la captura. false = no se pudo (entonces se reabre mpv)
        readonly ManualResetEvent homeSwitchEvt = new ManualResetEvent(false);
        volatile bool homeSwitching, homeSwitchOk;

        bool SwitchMpvInPlace(Process p, string oldCapture)
        {
            if (!ipcUp) return false;
            string win = Config.WindowFor(cfg.CaptureNow);
            bool winChange = !Config.EngineActive && win != Config.WindowFor(oldCapture);
            // (con fuentes de prueba la del HOME y la del juego son distintas: se vuelve a abrir siempre)
            bool reload = cfg.CaptureNow != oldCapture || cfg.TestSourceGame.Length > 0;
            bool filters = !cfg.HomeNative;
            homeSwitchEvt.Reset();
            homeSwitchOk = false;
            homeSwitching = reload;
            switchUntil = DateTime.Now.AddSeconds(SwitchSecs);
            string engSet = EngineSetCmd(); // (17-11: con el motor, sus ajustes con el cambio, ver EngineSetCmd)
            lock (cmdQueue)
            {
                if (engSet != null) cmdQueue.Add(JsonCmd("engine", "set " + engSet));
                if (winChange) cmdQueue.Add(JsonCmd("set", "geometry", win + "+0+0"));
                vfDedupe = DedupeOn(cfg, fgWanted);
                if (vfDedupe) cmdQueue.Add(JsonCmd("vf", "set", FrameFilter(cfg)));
                else cmdQueue.Add(JsonCmd("vf", "clr", ""));
                ReAddThrottleLocked(); // (LS se abre a la vez: "vf set/clr" quitaria el filtro de arranque lento)
                edgeShaderSet = filters ? cfg.EdgeShaderNow : "";
                cmdQueue.Add(JsonCmd("set", "glsl-shaders", edgeShaderSet));
                if (reload)
                {
                    cmdQueue.Add(JsonCmd("script-message", "switch2-switching", SwitchSecs.ToString(CultureInfo.InvariantCulture)));
                    if (cfg.TestSource.Length == 0) cmdQueue.Add(JsonCmd("set", "demuxer-lavf-o", LavfOptions(cfg)));
                    cmdQueue.Add(JsonCmd("loadfile", SourceUrl(cfg), "replace"));
                }
            }
            if (engSet != null) Log.Write("Motor propio: " + engSet + " (con el cambio de captura)");
            bool ok = true;
            try
            {
                // (21-11, 1.2.1: con captura_segura, como mucho 5 s: un cambio sin cerrar mpv da su primera imagen en 0,3-1,2 s -log del usuario-
                // y si la capturadora no da el formato nuevo, mpv se queda sin imagen: cuanto antes se reabre, antes llega el repuesto)
                if (reload) ok = homeSwitchEvt.WaitOne(SwitchWaitMs) && homeSwitchOk;
                if (ok && winChange) ok = EnsureWindowSize(p, win);
            }
            finally
            {
                homeSwitching = false;
                switchUntil = DateTime.Now.AddSeconds(1); // margen para que vuelva el ritmo
            }
            return ok;
        }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        // La ventana de mpv a WxH en 0,0 (mpv la cambia con "geometry"; si no, se le cambia desde aqui)
        bool EnsureWindowSize(Process p, string size)
        {
            string[] wh = size.Split('x');
            int w = int.Parse(wh[0], CultureInfo.InvariantCulture), h = int.Parse(wh[1], CultureInfo.InvariantCulture);
            for (int i = 0; i < 30; i++)
            {
                p.Refresh();
                IntPtr hw = p.MainWindowHandle;
                RECT r;
                if (hw != IntPtr.Zero && GetWindowRect(hw, out r))
                {
                    if (r.Right - r.Left == w && r.Bottom - r.Top == h && r.Left == 0 && r.Top == 0) return true;
                    if (i == 10)
                    {
                        Log.Write("La ventana de mpv sigue a " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) + ": se le cambia el tamaño a " + size);
                        SetWindowPos(hw, IntPtr.Zero, 0, 0, w, h, 0x0004 | 0x0010); // SWP_NOZORDER | SWP_NOACTIVATE
                    }
                }
                Thread.Sleep(50);
            }
            return false;
        }

        // Doble clic en el borde de arriba o de abajo de mpv = Windows la estira a todo el alto (medido: 2560x1440 ->
        // 2560x2160). LS mide entonces 2560x2160 -> 2560x2160, escala 1.00, y la imagen se queda a tamaño de captura.
        // Se devuelve aqui a su tamaño. true si estaba mal
        bool RestoreWindowSize(Process p)
        {
            try
            {
                p.Refresh();
                IntPtr hw = p.MainWindowHandle;
                RECT r;
                if (hw == IntPtr.Zero || !GetWindowRect(hw, out r) || r.Left <= -30000) return false; // (minimizada)
                // 16-11 noche (el usuario: "al activar DLSS 5 primero se hace pequeña la ventana y se ve el escritorio"): al pasar del motor
                // a mpv + Magpie la vigilancia veia aun la ventana DEL MOTOR (a 4K) y la "devolvia" a 2560x1440. Solo la de mpv
                if (Config.EngineActive || relaunchEngine || !p.ProcessName.Equals("mpv", StringComparison.OrdinalIgnoreCase)) return false;
                string[] wh = Config.WindowFor(cfg.CaptureNow).Split('x');
                int w = int.Parse(wh[0], CultureInfo.InvariantCulture), h = int.Parse(wh[1], CultureInfo.InvariantCulture);
                if (r.Right - r.Left == w && r.Bottom - r.Top == h && r.Left == 0 && r.Top == 0) return false;
                Log.Write("La ventana de mpv estaba a " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) + " en " + r.Left + "," + r.Top +
                          ": se devuelve a " + w + "x" + h);
                SetWindowPos(hw, IntPtr.Zero, 0, 0, w, h, 0x0004 | 0x0010); // SWP_NOZORDER | SWP_NOACTIVATE
                return true;
            }
            catch (Exception ex) { Log.Error("Comprobando la ventana de mpv", ex); return false; }
        }

        double[] enterIcon;             // el icono del juego en el que se acaba de entrar (HomeTick -> LeaveHome)
        bool enterGuess;                // 15-11: se entra sin haber visto el HOME (al abrir la app): el ultimo juego o el pendiente

        void EnterGame(string name)
        {
            if (cfg.HomeNative) { double[] ic = enterIcon; enterIcon = null; LeaveHome(name, ic); return; }
            if (homeGrace) { double[] ic = enterIcon; enterIcon = null; LeaveHomeGrace(name, ic); return; } // (10-11)
            bool sure = enterSure;
            bool confirmed = sure && enterConfirmed;
            enterSure = false;
            enterConfirmed = false;
            enterGuess = false;
            if (curGame != null && GameProfiles.SameGame(curGame, name)) { Log.Write("De vuelta en \"" + curGame + "\""); return; }
            if (string.IsNullOrEmpty(name)) return; // (sin nombre leido: nada que recordar)
            if (HomeHints.IsSystemName(name)) { Log.Write("Al entrar: \"" + name + "\" es una casilla de la consola, no un juego: sin perfil"); return; } // (15-11)
            GameProfile p = GameProfiles.Find(name);
            if (p != null && name != p.Name) ImproveProfile(p, name, confirmed, "al entrar", sure);
            // (16-11: un juego nuevo solo con su nombre firme: ver EntryProfile)
            if (p == null && !sure) { Log.Write("Al entrar: \"" + name + "\" no es un juego conocido y su nombre no esta confirmado: sin perfil"); return; }
            if (p == null)
            {
                p = CurrentProfile(name);
                p.NameSure = confirmed;
                GameProfiles.Save(p);
                lock (profileSync) { curGame = p.Name; curGameGuess = false; }
                ClearPending();
                Log.Write("Juego nuevo: \"" + name + "\". Se recordaran sus ajustes (ahora: " + p.Summary() + ")");
                return;
            }
            // (15-11: applying antes de cambiar curGame, con profileSync: ver LeaveHomeGrace)
            lock (profileSync) { applying = true; curGame = p.Name; curGameGuess = false; }
            ClearPending();
            Log.Write("Juego: \"" + p.Name + "\" -> sus ajustes: " + p.Summary());
            try
            {
                if (p.Level != dlss5Level) SetLevel(p.Level);
                if (p.Passes != dlss5Passes)
                {
                    dlss5Passes = p.Passes;
                    SaveText(Paths.Dlss5PassesFile, p.Passes.ToString(CultureInfo.InvariantCulture));
                }
                // 20-11 (HZ120): el modo del perfil (30/60/120, el 120 solo si cabe); con applying SetModeN no reabre Magpie (lo hace ApplyPending)
                { int pfps = (p.Mode120 && Puede120(p.Capture)) ? 120 : p.Cap ? 30 : 60; if (pfps != (cfg.Mode120 ? 120 : cfg.CapMode ? 30 : 60)) SetModeN(pfps); }
                bool newWindow = Config.WindowFor(p.Capture) != Config.WindowFor(cfg.VideoSize);
                // Otra ventana (4K): mpv y Magpie se reabren y Magpie ya coge todo; si no, Magpie se reabre al acabar el cambio
                if (p.Capture != cfg.VideoSize) RequestCapture(p.Capture);
                if (p.Dlss5 != dlss5Shown) SetDlss5State(p.Dlss5);
                SetScalerChoice(p.Scaler, p.ScalerPick);
                if (p.Fg != fgWanted)
                {
                    fgWanted = p.Fg;
                    SaveText(Paths.FgFile, p.Fg ? "1" : "0");
                }
                SetPicks(p.Pick60, p.Pick30); // (19-11, GENERACION: el tipo de generacion elegido con L3 en este juego)
                PrepareCfg();
                if (!newWindow && cfg.EngineNow != Config.EngineActive) pendingPassesRestart = true;
            }
            finally { applying = false; }
        }


        // Lo que queda del perfil: reabrir Magpie con sus ajustes (con mpv delante y Magpie listo)
        void ApplyPending()
        {
            if (InHome || !lsReady || passesBusy || homeBusy || relaunchSize != null || DateTime.Now < switchUntil) return;
            if (pendingPassesRestart)
            {
                pendingPassesRestart = false;
                RestartLs(curGame ?? "Ajustes del juego", "Aplicando sus ajustes…");
            }
        }

        // Pasadas de DLSSNR (Multi Pass 1-3: X / Y / B). Van en la config de Magpie: se reabre con el telon
        volatile bool passesBusy;

        void SetPasses(int n)
        {
            n = Math.Max(1, Math.Min(3, n));
            if (n == dlss5Passes) { Osd("DLSS 5 · " + Pasadas(n) + " · ya activo"); return; }
            if (passesBusy || relaunchSize != null || !lsReady) return;
            Process p = mpv;
            if (p == null || p.HasExited) return;
            dlss5Passes = n;
            SaveText(Paths.Dlss5PassesFile, n.ToString(CultureInfo.InvariantCulture));
            Log.Write("Mando: DLSS 5 con " + Pasadas(n) + ReopenNote);
            SaveCurProfile();
            if (cfg.Dlss5Off) { Osd("DLSS 5 · " + Pasadas(n) + " · se aplicará al activarlo"); return; }
            // (16-11: lo que de verdad va: en el 60 con Smooth Motion "2 pasadas" es 1 pasada al 35 %, y lo dice)
            RestartLs("DLSS 5 · " + Pasadas(n), Dlss5PassText());
        }

        // Modo 30 fps (Share / -) o 60 fps (Options / +). Compatibilidad: SetMode(true/false) = 30/60 (lo llaman los perfiles y las ordenes
        // m30/m60 de la tuberia). El boton del mando ahora cicla con CycleMode (20-11, HZ120)
        void SetMode(bool cap) { SetModeN(cap ? 30 : 60); }

        // 20-11 (HZ120): el modo de imagenes reales: 30 | 60 | 120. El filtro de imagenes de mpv cambia en caliente; la generacion y el modelo
        // del escalado van en la config de Magpie (se reabre con el telon). A/desde 120 cambia la velocidad REAL de captura (60000/1001 ->
        // 120): hay que reabrir la captura (el motor reinicia su fuente; mpv recarga la capturadora). El 30 y el 60 comparten captura (60) y
        // solo se diferencian por el filtro de repetidas
        void SetModeN(int fps)
        {
            bool cap = fps == 30, m120 = fps == 120;
            if (cap == cfg.CapMode && m120 == cfg.Mode120) { Osd(ModoTexto(fps) + " · ya activo"); return; }
            if (passesBusy || relaunchSize != null || !lsReady) { if (!applying) return; }
            bool fpsChange = m120 != cfg.Mode120;   // la velocidad de captura cambia (a/desde 120)
            cfg.CapMode = cap;
            cfg.Mode120 = m120;
            SaveText(Paths.ModeFile, fps.ToString(CultureInfo.InvariantCulture));
            Log.Write((applying ? "Perfil" : "Mando") + ": modo " + fps + " fps" + (fpsChange ? " (captura a " + cfg.FramerateNow + ")" : "") + (applying ? "" : ReopenNote));
            lock (cmdQueue)
            {
                vfDedupe = DedupeOn(cfg, fgWanted);
                cmdQueue.Add(vfDedupe ? JsonCmd("vf", "set", FrameFilter(cfg)) : JsonCmd("vf", "clr", ""));
            }
            if (cap) want30 = true;
            else { want30 = false; slow30 = 0; fast60 = 0; }
            SaveCurProfile();
            if (applying) return; // los perfiles reabren la imagen una sola vez (coge FramerateNow en EngineArgs / LavfOptions)
            // Cambio de velocidad de captura: el motor propio reinicia su fuente con el nuevo fps por la tuberia; con mpv + Magpie se recarga
            // la capturadora a la nueva velocidad y luego Magpie (cambia la generacion: a 120 sin ella)
            if (fpsChange && Config.EngineActive && ipcUp)
            {
                engineSent = null; // que no se salte por igual al EngineLive de siempre
                // a 120 nunca Smooth Motion (EngineSmoothMode=0); OJO: el ajuste del driver (NvProfiles, sm/refresco fijo) se fija al lanzar
                // el motor, no por la tuberia: entrar a 120 desde un 60 con Smooth Motion puede dejar el Smooth Motion del driver puesto hasta
                // que se reabra el motor (al cargar un perfil se relanza con sm=0). SIN PROBAR con consola
                string set = EngineSessionSet(cfg, cfg.Mode120 ? false : Config.EngineSmoothRunning > 0);
                lock (cmdQueue) cmdQueue.Add(JsonCmd("engine", "set " + set));
                Log.Write("Motor propio: " + set + " (cambio de velocidad de captura a " + cfg.FramerateNow + ")");
                Osd(ModoTexto(fps) + " · " + ScalerText());
                return;
            }
            if (fpsChange && !Config.EngineActive && ipcUp && cfg.TestSource.Length == 0)
            {
                // 21-11 (1.2.1): como un cambio de captura sin cerrar mpv (RequestCapture): con switch2-switching (sin el, si la capturadora no
                // abria la velocidad nueva, el script de mpv tomaba el fin de la captura por la consola apagada -con apagar_pc=1, el PC
                // apagado-) y relaunchSize, para que si no llega imagen nueva mpv se vuelva a abrir (SwitchFallback; luego CapturaSegura.cs)
                string size = cfg.VideoSize;
                relaunchSize = size;
                switchUntil = DateTime.Now.AddSeconds(SwitchSecs);
                lock (cmdQueue)
                {
                    cmdQueue.Add(JsonCmd("script-message", "switch2-switching", SwitchSecs.ToString(CultureInfo.InvariantCulture)));
                    cmdQueue.Add(JsonCmd("set", "demuxer-lavf-o", LavfOptions(cfg)));
                    cmdQueue.Add(JsonCmd("loadfile", SourceUrl(cfg), "replace"));
                }
                Thread guard = new Thread(delegate ()
                {
                    Thread.Sleep(SwitchWaitMs);
                    if (relaunchSize == size && switchUntil != DateTime.MinValue)
                    {
                        Log.Write("La captura a " + cfg.FramerateNow + " fps no ha llegado en " + SwitchWaitMs / 1000 + " s: se reabre mpv");
                        SwitchFallback();
                    }
                });
                guard.IsBackground = true;
                guard.Start();
            }
            RestartLs(ModoTexto(fps),
                      (m120 ? "captura a " + cfg.FramerateNow + " · baja latencia" :
                       fgWanted ? (cap ? "30 → 60" : "60 → 120") + " fps · " + ScalerText() :
                       ScalerText()),
                      fpsChange);
        }

        string ModoTexto(int fps) { return "Modo " + fps + " fps" + (fps == 120 ? " (esports)" : ""); }

        // 20-11 (HZ120): el boton de modo cicla la escalera 30 <-> 60 <-> 120. dir>0 (Options / +) sube; dir<0 (Share / -) baja. El 120 solo
        // entra cuando la captura lo admite (Puede120); si no, se queda en 30 <-> 60 y al pedir subir desde el 60 avisa por que no cabe
        void CycleMode(int dir)
        {
            int cur = cfg.Mode120 ? 120 : cfg.CapMode ? 30 : 60;
            int next;
            if (dir > 0) next = cur == 30 ? 60 : cur == 60 ? (Puede120() ? 120 : 60) : 120;
            else next = cur == 120 ? 60 : cur == 60 ? 30 : 30;
            if (next == cur)
            {
                if (dir > 0 && cur == 60 && !Puede120()) Osd(Aviso120NoCabe());
                else Osd(ModoTexto(cur) + " · ya activo");
                return;
            }
            SetModeN(next);
        }

        // ¿Se puede ofrecer el modo 120 con esta captura? permitir_120 (auto: solo si la tele va a >=120 Hz y la captura admite 120 en la
        // capturadora, segun capturas_120: la GC573 solo a 1080p y 1440p), la consola saca 120 y la captura es una de 120
        bool Puede120() { return Puede120(cfg.VideoSize); }
        bool Puede120(string size)
        {
            string p = cfg.Permit120;
            if (p == "0" || p == "no" || p == "false" || p == "off") return false;
            if (!cfg.Captura120Ok(size)) return false;
            if (p == "1" || p == "si" || p == "yes" || p == "true" || p == "on") return true;
            return Consolas.Actual.Da120 && Pantalla.Hz() >= 120; // auto
        }

        string Aviso120NoCabe()
        {
            string p = cfg.Permit120;
            if (p == "0" || p == "no" || p == "false" || p == "off") return "120 fps desactivado (permitir_120=0)";
            if (!cfg.Captura120Ok(cfg.VideoSize))
                return "120 fps no disponible a " + SizeName(cfg.VideoSize) + " con esta capturadora: pon la captura a 1080p o 1440p";
            if (!Consolas.Actual.Da120) return "120 fps: la " + Consolas.Actual.Corto + " no saca 120 Hz";
            return "120 fps: pon la tele a 120 Hz o mas";
        }

        // Reabre Magpie (con lo elegido ahora en su config) con el telon puesto
        // sinTelon (16-11, dlss5_sin_telon): DLSS 5 si/no con el mando: al cambiar de motor, sin telon si se puede (src\SinTelon.cs)
        void RestartLs(string title, string subtitle, bool force = false, bool sinTelon = false)
        {
            Process p = mpv;
            if (p == null || p.HasExited || passesBusy) return;
            // 15-11: motor propio. Los cambios van al momento por su tuberia (sin cerrar nada ni telon); si cambia de motor (DLSS 5
            // encendido o apagado con motor=propio) se reabre la imagen con el otro
            PrepareCfg();
            if (Config.EngineActive || cfg.EngineNow)
            {
                if (Config.EngineActive && cfg.EngineNow)
                {
                    SyncEngine();
                    // (1.2.4 arreglos, RELEVO: si lo nuevo pide otro motor -otra cadena de generacion, Smooth Motion, refresco-, el relevo ya; el
                    // aviso lo da el motor nuevo. RelevoMotor.cs)
                    if (RelevoSiOtraIdentidad(title, subtitle)) return;
                    if (!applying && title != null) Osd(string.IsNullOrEmpty(subtitle) ? title : title + " · " + subtitle);
                    return;
                }
                if (relaunchEngine || relaunchSize != null) return;
                // 16-11 (dlss5_sin_telon): sin telon, la imagen de ahora sigue hasta que la otra da la suya (si no se puede, como siempre).
                // 16-11 (velocidad, dlss5_sin_telon_puente): tambien al cambiar de motor por el modo o la generacion; con el boton de captura
                // del mando, Magpie fuera ya (se caia si seguia vivo ~0,5 s despues) y el telon hasta la imagen del motor
                if ((sinTelon || cfg.MagpieBridge) && TrySinTelon(title, subtitle))
                {

                    return;
                }
                Log.Write(title + ": " + (cfg.EngineNow ? "de mpv al motor propio" : "del motor propio a mpv sin el motor") + " (se reabre la imagen)");
                lsReady = false; // (nada del mando ni del HOME sobre la imagen que se cierra)
                relaunchEngine = true;
                // 16-11: al momento, negro con el aviso arriba (como el de las reaperturas de Magpie). Antes la ultima imagen atenuada:
                // 0,8-0,9 s de componerla a 4K ANTES de cerrar (o pasar a espera) el motor
                bool padNow = PadRecent;
                if (padNow) { padRestart = true; padRestartAt = DateTime.Now; }
                ShowFastCurtain(padNow ? subtitle : null, padNow ? title : null, false);
                QuitMpvForRelaunch();
                CurtainToFront();
                return;
            }
            // (1.2.4 ronda 6, QUITAFG: sin Magpie, con mpv sin el motor no hay nada que reabrir -la captura la cambia RequestCapture-)
            if (!applying && title != null) Osd(string.IsNullOrEmpty(subtitle) ? title : title + " · " + subtitle);
        }

        // (1.2.4 ronda 6, QUITAFG, fase 2: WriteMagpieConfigNow era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: WaitMagpieGone era de Magpie; fuera)

        static void SaveText(string path, string text)
        {
            try { File.WriteAllText(path, text); }
            catch (Exception ex) { Log.Error("Guardando " + path, ex); }
        }

        static string LoadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
            catch { return null; }
        }

        // mpv se cierra con 0 y RunMpv lo vuelve a abrir con la captura nueva (relaunchSize)
        // 16-11 (motor_en_espera): con el motor propio y lo siguiente mpv + Magpie (lo elegido ya en la config: DLSS 5 encendido, un juego con
        // DLSS 5...; toMpv cuando no lo dice la config, como otra captura), el motor no se cierra: se queda en espera
        volatile bool quitForRelaunch;
        void QuitMpvForRelaunch(bool toMpv = false)
        {
            bool toEngine = false;
            if (Config.EngineActive && !toMpv) { PrepareCfg(); toEngine = cfg.EngineNow; }
            quitToStandby = Config.EngineActive && (toMpv || !toEngine) && StandbyOn();
            quitForRelaunch = true;
        }

        // ---- 16-11: motor en espera (ver los campos de standbyEngine) ----
        // ¿Se usa? motor_en_espera=1, motor=propio y un ConsoleEnhancerEngine.exe que lo sepa hacer: lleva "switch2-standby" y "switch2-wake" (uno de
        // antes no conoce standby=1: abriria la capturadora con mpv usandola y no se esconderia). Mirado una vez por version del exe
        static readonly object standbyProbeSync = new object();
        static string standbyProbeExe;
        static DateTime standbyProbeAt;
        static bool standbyProbeOk;
        bool StandbyOn()
        {
            if (!cfg.EngineStandby || cfg.Engine != "propio") return false;
            try
            {
                FileInfo fi = new FileInfo(cfg.EnginePath);
                if (!fi.Exists) return false;
                lock (standbyProbeSync)
                {
                    if (standbyProbeExe == fi.FullName && standbyProbeAt == fi.LastWriteTimeUtc) return standbyProbeOk;
                    byte[] exe = File.ReadAllBytes(fi.FullName);
                    standbyProbeOk = HasText(exe, "switch2-standby") && HasText(exe, "switch2-wake");
                    standbyProbeExe = fi.FullName;
                    standbyProbeAt = fi.LastWriteTimeUtc;
                    if (!standbyProbeOk)
                        Log.Write("Motor en espera: " + fi.Name + " (" + fi.LastWriteTime.ToString("dd-MM HH:mm") + ") aun no lo sabe hacer (no lleva switch2-standby / " +
                                  "switch2-wake): el motor se cierra y se abre como siempre");
                    return standbyProbeOk;
                }
            }
            catch (Exception ex) { Log.Error("Motor en espera: mirando el motor", ex); return false; }
        }

        // El texto en las cadenas de un exe de .NET (UTF-16)
        static bool HasText(byte[] data, string text)
        {
            byte[] t = Encoding.Unicode.GetBytes(text);
            for (int i = 0; i + t.Length <= data.Length; i++)
            {
                if (data[i] != t[0]) continue;
                int k = 1;
                while (k < t.Length && data[i + k] == t[k]) k++;
                if (k == t.Length) return true;
            }
            return false;
        }

        // Pasa a ser el motor en espera (recien arrancado con standby=1, o el de ahora tras su switch2-standby-ok): aparte de 'mpv', en el
        // job que lo cierra si la app muere, y escuchando su tuberia
        int standbySmooth = -1;   // 17-11 16:55: Smooth Motion con el que se abrio el motor en espera (Config.EngineSmoothLaunched/Running)
        void SetStandby(Process e, string ipc, bool ready)
        {
            // 21-11 (1.0.9, motor de reserva): nunca dos en espera: el que hubiera (la reserva) se cierra antes
            if (standbyEngine != null && standbyEngine != e) KillStandby("otro motor pasa a espera");
            standbySmooth = ready ? Config.EngineSmoothRunning : Config.EngineSmoothLaunched;   // (ready: el que hacia la imagen y pasa a espera)
            if (!ready) { Config.NoteEngineFixed(e); Config.NoteEngineFfx(e); }   // 20-11 (LATENCIA5): recien lanzado con EngineArgs: su refresco (fijo / VRR)
            standbyReady = ready;
            standbyIpc = ipc;
            standbySince = DateTime.Now;
            standbyEngine = e;
            standbyAtExit = e;
            EngineJob.Hold(e, true);
            Thread t = new Thread(delegate () { StandbyLinkLoop(e, ipc); });
            t.IsBackground = true;
            t.Name = "Motor en espera";
            t.Start();
        }

        void KillStandby(string why)
        {
            Process e = standbyEngine;
            standbyEngine = null;
            standbyReady = false;
            CloseStandbyPipe();
            if (e == null) return;
            if (standbyAtExit == e) standbyAtExit = null;
            try { if (!e.HasExited) { e.Kill(); Log.Write("Motor en espera: cerrado (" + why + ")"); } }
            catch (Exception ex) { Log.Error("Cerrando el motor en espera", ex); }
        }

        // 21-11 (1.2.1, log del usuario 19:09:27.393 y .394): dos motores en espera arrancados a la vez (el del HOME quieto, WatchLoop, y el
        // del puente que no lo encontro, BridgeWake): SetStandby del segundo cerro el primero ("otro motor pasa a espera") y el hilo del
        // primero, al verlo cerrado, hacia KillStandby... del SEGUNDO ("se ha cerrado al arrancar (codigo -1)" dos veces): la entrada en
        // Rocket League a las 19:09:49 sin puente, 8,6 s en negro. Ahora: un solo arranque a la vez (StandbyLaunchBegin / End: los demas no
        // arrancan otro, o esperan a ese) y cada hilo solo cierra SU motor (KillStandbyIf)
        readonly object standbyLaunchSync = new object();
        volatile bool standbyLaunchBusy;
        DateTime standbyLaunchAt = DateTime.MinValue;
        int standbyLaunchFails;             // arranques seguidos que se cerraron solos antes de estar listos (para no insistir)

        bool StandbyLaunchBegin(string who)
        {
            lock (standbyLaunchSync)
            {
                // (uno colgado mas de 30 s no bloquea los siguientes)
                if (standbyLaunchBusy && (DateTime.Now - standbyLaunchAt).TotalSeconds < 30)
                {
                    Log.Write("Motor en espera (" + who + "): ya se esta arrancando otro: no se arranca un segundo");
                    return false;
                }
                if (standbyEngine != null) return false; // (ya hay uno: nada que arrancar)
                standbyLaunchBusy = true;
                standbyLaunchAt = DateTime.Now;
                return true;
            }
        }

        void StandbyLaunchEnd() { lock (standbyLaunchSync) standbyLaunchBusy = false; }

        // El de espera fuera solo si sigue siendo ESTE (el hilo que lo arranco); si ya es otro, no se toca
        void KillStandbyIf(Process e, string why)
        {
            if (e == null || standbyEngine != e) return;
            KillStandby(why);
        }

        // Si otro hilo esta arrancando uno, se le espera (hasta que se apunta como motor en espera: ~0,1-1 s) en vez de arrancar otro
        void WaitStandbyLaunch(int maxMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (standbyLaunchBusy && standbyEngine == null && !userExit && sw.ElapsedMilliseconds < maxMs) Thread.Sleep(20);
        }

        void CloseStandbyPipe()
        {
            NamedPipeClientStream sp = standbyPipe;
            standbyPipe = null;
            if (sp != null) try { sp.Dispose(); } catch { }
        }

        // Mientras esta en espera: su tuberia, solo por lo del modo espera (switch2-standby-ready al acabar de arrancar con standby=1). Lo
        // demas que manda un motor escondido (switch2-console, switch2-rate...) no cuenta para nada
        void StandbyLinkLoop(Process e, string ipc)
        {
            try
            {
                while (!userExit && standbyEngine == e && !e.HasExited)
                {
                    NamedPipeClientStream pipe = new NamedPipeClientStream(".", ipc, PipeDirection.InOut, PipeOptions.Asynchronous);
                    try { pipe.Connect(500); }
                    catch (Exception) { pipe.Dispose(); Thread.Sleep(150); continue; }
                    standbyPipe = pipe;
                    if (standbyEngine != e) { CloseStandbyPipe(); break; } // (despertado mientras se conectaba)
                    try
                    {
                        StreamReader r = new StreamReader(pipe, new UTF8Encoding(false));
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            if (!line.Contains("\"client-message\"")) continue;
                            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line, "\"args\":\\[\"switch2-standby-(ready|ok)\"(?:,\"(\\d+)\")?");
                            if (m.Success) StandbyIsReady(e, m.Groups[1].Value == "ready", m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : -1);
                        }
                    }
                    catch (Exception) { } // (tuberia cerrada: despertado, cerrado, o se vuelve a conectar)
                    finally { if (standbyPipe == pipe) standbyPipe = null; try { pipe.Dispose(); } catch { } }
                    Thread.Sleep(100);
                }
            }
            catch (InvalidOperationException) { }
            catch (Exception ex) { Log.Error("Tuberia del motor en espera", ex); }
        }

        void StandbyIsReady(Process e, bool started, int ms)
        {
            if (standbyEngine != e || standbyReady) return;
            standbyReady = true;
            Log.Write("Motor en espera: listo (" + (ms >= 0 ? ms : (long)(DateTime.Now - standbySince).TotalMilliseconds) + " ms): " +
                      (started ? "arrancado escondido, sin la capturadora" : "capturadora soltada, sigue abierto y escondido") + " (pid " + e.Id + ")");
        }

        // El de espera para despertarlo (RunMpv, al pasar al motor propio): vivo y listo. Recien arrancado y aun sin decir que esta listo, se le
        // espera hasta sus 8 s (menos que abrir otro, ~4,5 s hasta la imagen); con mas tiempo sin aviso (se pudo perder: la tuberia llega
        // tarde), se despierta igual. null = no hay o no vale (cerrado): se abre otro como siempre
        Process StandbyForWake()
        {
            Process e = standbyEngine;
            if (e == null) return null;
            try
            {
                while (!standbyReady && !e.HasExited && !userExit && (DateTime.Now - standbySince).TotalSeconds < 8) Thread.Sleep(30);
                if (e.HasExited)
                {
                    Log.Write("Motor en espera: se habia cerrado (codigo " + e.ExitCode + "): se abre otro como siempre");
                    KillStandby("ya cerrado");
                    return null;
                }
                if (userExit) return null;
                if (!standbyReady) Log.Write("Motor en espera: sin su aviso de listo (" + (int)(DateTime.Now - standbySince).TotalSeconds + " s abierto): se despierta igual");
                return e;
            }
            catch (Exception ex) { Log.Error("Motor en espera", ex); KillStandby("no se pudo mirar"); return null; }
        }

        // Los ajustes de esta sesion, como los argumentos de un arranque (captura, ritmo, formato, generacion, modo, modelo, escalador, enfoque,
        // Reflex, bordes, tramado, repetidas), y luego "switch2-wake". Por la cola de ordenes: los escribe el enlace de siempre al conectarse
        void SendWake(Process e)
        {
            string set = EngineSessionSet(cfg, standbySmooth > 0);
            // (1.2.4 arreglos, RELEVO: uno que no vale para la generacion de ahora -el puente tras una caida- se despierta sin ella)
            if (IdentityDiffOf(e, standbySmooth) != null) { set = LiveSinGeneracion(set); engineSent = LiveSinGeneracion(engineSent); }   // (y lo apuntado: SyncEngine sigue sin ella)
            lock (cmdQueue)
            {
                cmdQueue.Add(JsonCmd("engine", "set " + set));
                cmdQueue.Add(JsonCmd("script-message", "switch2-wake"));
            }
            Log.Write("Motor en espera: se despierta (pid " + e.Id + "): " + set);
        }

        // (sin despertarlo a tiempo: lo de SendWake fuera de la cola, que no le llegue a otro motor)
        void DropWake()
        {
            lock (cmdQueue) cmdQueue.RemoveAll(delegate (string c) { return c.Contains("\"switch2-wake\"") || c.StartsWith("{\"command\":[\"engine\",\"set capture=", StringComparison.Ordinal); });
        }

        // (17-11 16:55: smooth = el motor que se despierta se abrio con Smooth Motion: la generacion del 60 la hace el driver, fg=0)
        static string EngineSessionSet(Config cfg, bool smooth)
        {
            CapFmt ef = CapturaSegura.Efectivo(cfg);   // 21-11 (1.2.1): lo que se abre de verdad (src\Lanzador\CapturaSegura.cs)
            return "capture=" + ef.Size + " fps=" + ef.Fps + " format=" + EngineFormat(cfg) + (cfg.TestSource.Length > 0 ? " " + cfg.TestMotorArg : "") + " " + EngineLive(cfg, smooth);
        }

        static string EngineFormat(Config cfg)
        {
            string pf = CapturaSegura.Efectivo(cfg).Pix;   // (21-11, 1.2.1: el de lo que se abre de verdad; sin repuesto, PixelFormatNow)
            return pf == "bgr24" ? "rgb24" : pf == "yuyv422" ? "yuy2" : pf == "nv12" ? "nv12" : pf == "mjpeg" ? "mjpg" : "auto";   // (23-11, SINMPV: mjpeg)
        }

        // Si la sesion va con mpv + Magpie y no hay motor: uno arrancado ya en espera (standby=1), una vez por sesion, en el HOME a 4K nativo
        // (WatchLoop, con 3 s de HOME quieto: ahi el tiron de su arranque, ~3 s de Streamline, no se ve)
        // 21-11 (1.0.9; el usuario: "intenta reducir los tiempos de carga de los perfiles"): cambiar de Smooth Motion a DLSS FG (o al reves)
        // con L3, o un perfil que lo cambia, vuelve a abrir el motor: 4-5 s (3 de ellos de Streamline, slInit + slSetD3DDevice). Con el motor
        // jugando 10 s sin cambios, uno de reserva escondido (en espera: sin capturadora ni pintar) con la OTRA generacion de este modo y su
        // refresco; al cambiar, se despierta ese (~0,5 s). Uno por estado (no se repite si ya se probo con lo mismo)
        DateTime reserveQuietSince = DateTime.MinValue;
        string reserveKeyDone;
        void MaybeReserveEngine(DateTime now)
        {
            // (22-11, 1.2.2, ERRORES: !Config.EngineActive, "con el motor jugando 10 s": sin el, los 10 s contaban tambien con mpv + Magpie
            // preparando y el motor del puente encima -sin motor en espera-; 02:49:26 el de reserva se arranco 1 s despues de pasar al
            // motor, el usuario volvio a DLSS 5, arranco a la vez que Magpie -Magpie 20 s, el de reserva 13,6 s- y se cerro sin usarse)
            if (!cfg.EngineReserve || !Config.EngineActive || standbyEngine != null || !ipcUp || !lsReady || homeBusy || passesBusy || relaunchEngine || relaunchSize != null ||
                relevo != null || cfg.HomeNative || cfg.FgOff || cfg.Mode120 || userExit || !StandbyOn())
            { reserveQuietSince = DateTime.MinValue; return; }
            if (reserveQuietSince == DateTime.MinValue) { reserveQuietSince = now; return; }
            if ((now - reserveQuietSince).TotalSeconds < 10) return;
            int smRun = Config.EngineSmoothRunning;
            if (smRun < 0 || NvProfiles.EngineSmoothBroken) return;
            int altSm, altFixed, altCh = 0;
            // (1.2.4 arreglos, RELEVO: con otra cadena delante -XeSS FG, FSR 3 FG-, el de reserva es el de siempre -cadena 0-; antes Smooth Motion)
            // (1.2.4 ronda 6, QUITAFG: sin DLSS-G, "el de siempre sin Smooth Motion" es un motor sin generacion -log del usuario 23-11 01:46:33,
            // "se arranca uno escondido con DLSS FG" con Smooth Motion delante, y apagaba Smooth Motion en el perfil del driver mientras-. Ahora
            // el otro tipo del selector que quede: con Smooth Motion delante, XeSS FG -su cadena 20, sin Smooth Motion-; con XeSS FG / FSR 3 FG
            // delante, Smooth Motion si esta en el selector; si no hay otro, ninguno)
            int chRun = Config.EngineFfxOf(mpv);
            List<string> opts = GenOptions();
            bool xeVa = opts.Contains("xess") && Config.MotorFgSabe("xess") && !cfg.MotorGenAMagpie("xess");
            if (smRun > 0)
            {
                if (!xeVa) return;
                altSm = 0; altCh = 20 + (Ce.EngineUpscaler(cfg) == "dlss" ? 1 : 0); altFixed = (!cfg.CapMode && !cfg.Mode120 && cfg.EngineFixed60Fg && cfg.CeRefrescoFijoAhora() && !NvProfiles.EngineFixedBroken) ? 1 : 0;
            }
            else
            {
                if (!opts.Contains("smooth")) return;
                altSm = cfg.CapMode ? (Pantalla.Hz() >= 100 ? 2 : 1) : (Pantalla.Hz() >= 100 ? 1 : 0); altFixed = 0;
            }
            if (altSm == smRun && altCh == chRun) return;
            string key = smRun + ">" + altSm + "|" + altFixed + "|c" + chRun + ">" + altCh + "|" + cfg.CaptureNow + "|" + (cfg.CapMode ? 30 : 60);
            if (key == reserveKeyDone) return;
            reserveKeyDone = key;
            int sm = altSm, fx = altFixed, ch = altCh;
            Thread t = new Thread(delegate () { PrestartReserve(sm, fx, ch); });
            t.IsBackground = true;
            t.Name = "Motor de reserva";
            t.Start();
        }

        void PrestartReserve(int sm, int fixedRefresh, int chain)
        {
            if (!StandbyLaunchBegin("de reserva")) return; // (21-11, 1.2.1: uno solo a la vez)
            try
            {
                string ipc = MpvConst.StandbyIpcName();
                PrepareCfg();
                ProcessStartInfo psi = new ProcessStartInfo(cfg.EnginePath, EngineArgs(cfg, ipc, sm, fixedRefresh, chain) + " standby=1");
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(cfg.EnginePath);
                // 21-11 (1.2.1, log 1.2.07 18:31:55: el de reserva arranco justo cuando el modo 30 pasaba del motor a mpv + Magpie; arrancando a
                // la vez que Magpie y con el motor jugando encima, Magpie tardo 14,7 s y luego el de reserva se cerro al pasar el otro a la espera).
                // EngineArgs tarda (perfil de NVIDIA): si mientras tanto ha empezado un cambio, no se arranca
                if (!Config.EngineActive || relevo != null || relaunchEngine || relaunchSize != null || homeBusy || passesBusy || !lsReady || cfg.HomeNative || userExit)
                {
                    Log.Write("Motor de reserva: no se arranca (ha empezado un cambio de imagen mientras se preparaba)");
                    return;
                }
                Log.Write("Motor de reserva: se arranca uno escondido con " + (sm > 0 ? "Smooth Motion" : Config.ChainXess(chain) ? "XeSS FG" + (fixedRefresh > 0 ? " y refresco fijo" : "") : "la cadena de siempre") +
                          " (el otro tipo de generacion de este modo: cambiar a el sera despertarlo, ~0,5 s)");
                Process e = Process.Start(psi);
                if (userExit) { try { e.Kill(); } catch { } return; }
                SetStandby(e, ipc, false);
                StandbyLaunchEnd(); // (ya es el motor en espera: los demas lo ven)
                Stopwatch sw = Stopwatch.StartNew();
                bool fgSaid = false;
                while (!userExit && standbyEngine == e && !e.HasExited && sw.ElapsedMilliseconds < 25000 && (!standbyReady || sw.ElapsedMilliseconds < 4000))
                {
                    Thread.Sleep(100);
                    if (!fgSaid) fgSaid = StandbyFgBack(e);
                }
                // (21-11, 1.2.1: solo si se ha cerrado solo siendo aun el de espera; cerrado por la app -otro paso a espera, se desperto- no)
                if (e.HasExited && standbyEngine == e) { Log.Write("Motor de reserva: se ha cerrado al arrancar (codigo " + e.ExitCode + ")"); KillStandbyIf(e, "cerrado al arrancar"); }
            }
            catch (Exception ex) { Log.Error("Motor de reserva: arrancandolo", ex); }
            finally { StandbyLaunchEnd(); }
        }

        // 21-11 (1.2.1): no solo una vez por sesion: cada vez que falte en el HOME (cerrado por otro Smooth Motion, la grafica reiniciada, uno
        // que se cerro al arrancar...), como mucho uno cada 60 s y no despues de 3 que se cierran solos al arrancar. Para que el puente (el motor
        // en espera jugando encima mientras Magpie prepara) este casi siempre
        DateTime standbyPrestartAt = DateTime.MinValue;
        void MaybePrestartStandby()
        {
            if (standbyEngine != null || standbyLaunchBusy || bridgePrestarting || Config.EngineActive || !StandbyOn()) return;
            if (standbyPrestarted && (DateTime.Now - standbyPrestartAt).TotalSeconds < 60) return;
            if (standbyLaunchFails >= 3) return;
            standbyPrestarted = true;
            standbyPrestartAt = DateTime.Now;
            Thread t = new Thread(PrestartStandby);
            t.IsBackground = true;
            t.Name = "Motor en espera (arranque)";
            t.Start();
        }

        void PrestartStandby()
        {
            if (!StandbyLaunchBegin("en el HOME")) return; // (21-11, 1.2.1: uno solo a la vez)
            try
            {
                string ipc = MpvConst.StandbyIpcName();
                PrepareCfg();
                ProcessStartInfo psi = new ProcessStartInfo(cfg.EnginePath, EngineArgs(cfg, ipc) + " standby=1");
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(cfg.EnginePath);
                Log.Write("Motor en espera: se arranca uno escondido, sin la capturadora (en el HOME): " + psi.FileName + " " + psi.Arguments);
                Process e = Process.Start(psi);
                if (userExit) { try { e.Kill(); } catch { } return; }
                SetStandby(e, ipc, false);
                StandbyLaunchEnd(); // (ya es el motor en espera: los demas lo ven)
                // Escondido no deberia quedarse el primer plano; si lo coge, vuelve a mpv (el mando solo va con mpv delante)
                Stopwatch sw = Stopwatch.StartNew();
                bool fgSaid = false;
                while (!userExit && standbyEngine == e && !e.HasExited && sw.ElapsedMilliseconds < 25000 && (!standbyReady || sw.ElapsedMilliseconds < 4000))
                {
                    Thread.Sleep(100);
                    if (!fgSaid) fgSaid = StandbyFgBack(e);
                }
                // (21-11, 1.2.1: solo si se ha cerrado solo siendo aun el de espera; y solo se cierra ESE)
                if (e.HasExited && standbyEngine == e)
                {
                    standbyLaunchFails++;
                    Log.Write("Motor en espera: se ha cerrado al arrancar (codigo " + e.ExitCode + "): se sigue sin el" + (standbyLaunchFails >= 3 ? " (3 seguidos: no se arrancan mas en el HOME)" : ""));
                    KillStandbyIf(e, "cerrado al arrancar");
                }
                else if (standbyEngine == e && standbyReady) standbyLaunchFails = 0;
                else if (standbyEngine == e && !standbyReady) Log.Write("Motor en espera: sin aviso de listo en 25 s (sigue abierto: se despertara igual)");
            }
            catch (Exception ex) { Log.Error("Motor en espera: arrancandolo", ex); }
            finally { StandbyLaunchEnd(); }
        }

        // El primer plano lo tiene el motor escondido: a mpv otra vez. true = lo tenia
        DateTime standbyFgLog = DateTime.MinValue;
        bool StandbyFgBack(Process e)
        {
            try
            {
                uint pid;
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                GetWindowThreadProcessId(fg, out pid);
                if (pid != (uint)e.Id) return false;
                Process m = mpv;
                if (m == null || m.HasExited || m == e) return false;
                m.Refresh();
                if (m.MainWindowHandle != IntPtr.Zero) ForceForeground(m.MainWindowHandle);
                if ((DateTime.Now - standbyFgLog).TotalSeconds >= 5)
                {
                    standbyFgLog = DateTime.Now;
                    Log.Write("Aviso: el motor en espera se habia quedado el primer plano: devuelto a mpv");
                }
                return true;
            }
            catch { return false; }
        }

        // ---- 15-11: motor propio (src\Motor) ----
        string mpvScaleSent = "ewa_lanczossharp";
        volatile bool relaunchEngine;   // al cerrarse la imagen se abre la del otro motor (RunMpv)
        static string ReopenNote { get { return Config.EngineActive ? " (motor propio: al momento)" : " (se reabre Magpie)"; } }
        int engineFails;                // veces seguidas que el motor no llego a dar imagen
        int mpvFails;                   // (1.2.02) veces seguidas que mpv se cerro sin imagen
        int engineCrashes;              // veces que el motor se cerro solo jugando
        volatile bool videoSignal;      // la imagen (mpv o el motor) ya ha dado su aviso de imagen (o el motor, imagenes por segundo)
        volatile bool signalHeard;      // 15-11: ha llegado su "switch2-signal" (solo entonces se le contesta: ver MpvLinkLoop)
        string engineSent = "";         // lo ultimo mandado al motor

        // ---- 16-11: motor en espera (ini motor_en_espera, ver Config.EngineStandby) ----
        // Pasar de mpv + Magpie al motor propio eran ~4,5 s de negro: ~3,1 s solo de Streamline (slInit y slSetD3DDevice) antes de abrir
        // la capturadora. Ahora el motor que deja paso a mpv + Magpie no se cierra: se le pide "switch2-standby" (suelta la capturadora,
        // esconde su ventana, deja de presentar y de vigilar el apagado, y sigue con Streamline, el dispositivo y la swapchain) y queda
        // aqui (standbyEngine, aparte de 'mpv'). Al volver al motor, con mpv ya cerrado (la capturadora libre), los ajustes de la sesion
        // y "switch2-wake": abre la capturadora y da la imagen como al arrancar. Si la sesion empieza con mpv + Magpie, uno arrancado ya en
        // espera (standby=1) una vez, en el HOME a 4K nativo (ahi un tiron no se ve). Sin respuesta a tiempo, como antes
        volatile Process standbyEngine;     // el motor en espera (escondido, sin la capturadora); null = ninguno
        volatile string standbyIpc;         // su tuberia al estilo de mpv
        volatile bool standbyReady;         // ya ha dicho switch2-standby-ready (arrancado en espera) o switch2-standby-ok
        DateTime standbySince;              // desde cuando esta (arrancado o pasado a espera)
        volatile Process standbyAsked;      // al que se le ha pedido switch2-standby (el motor de ahora, aun en 'mpv')
        Stopwatch standbyAskedSw;
        volatile Process standbyGot;        // el que ya ha contestado switch2-standby-ok
        volatile int standbyGotMs = -1;     // lo que tardo segun el
        volatile bool quitToStandby;        // con quitForRelaunch: al motor "switch2-standby" en vez de "quit"
        bool standbyPrestarted;             // ya se arranco uno en espera en esta sesion (solo una vez)
        volatile NamedPipeClientStream standbyPipe; // la tuberia con el de espera (se cierra al despertarlo: la usa el enlace de siempre)
        volatile string curIpc;             // la tuberia del mpv / motor de ahora (RunMpv)
        DateTime imageStart = DateTime.Now; // cuando se lanzo (o se desperto) la imagen de ahora: los limites sin imagen van desde aqui
        int homeQuietTicks;                 // pasos de WatchLoop seguidos en el HOME a 4K nativo, quieto (para arrancar el de espera)

        // 16-11 (motor_en_espera): el motor escondido en espera, para cerrarlo tambien desde fuera de la app (ExitRestore, un fallo)
        static volatile Process standbyAtExit;

        public static void KillStandbyAtExit(string why)
        {
            Process e = standbyAtExit;
            standbyAtExit = null;
            if (e == null) return;
            try { if (!e.HasExited) { e.Kill(); Log.Write("Motor en espera: cerrado (" + why + ")"); } }
            catch { }
        }

        // 17-11 16:55 (motor_generacion_60=smooth): el motor que hace la imagen se abrio con otro Smooth Motion del que toca ahora (la
        // generacion encendida o apagada con el boton de captura, modo 30/60, otro juego): el driver no lo cambia en marcha, se vuelve a
        // abrir el motor (con el telon). Medio segundo seguido distinto (no a mitad de un cambio)
        // 20-11 (LATENCIA5, motor_60fg_refresco_fijo): el motor que hace la imagen se abrio con otro refresco del que toca (fijo en el 60 con
        // DLSS FG, VRR en lo demas): el driver solo lo mira al abrirlo, se vuelve a abrir (sin telon, como con Smooth Motion). Medio segundo
        DateTime fixedDiffSince = DateTime.MinValue;
        void CheckEngineFixed(DateTime now)
        {
            int have = Config.EngineFixedOf(mpv);
            if (have < 0 || (!cfg.EngineFixed60Fg && have == 0)) { fixedDiffSince = DateTime.MinValue; return; }
            if (!ipcUp || !lsReady || homeBusy || passesBusy || relaunchEngine || relaunchSize != null || relevo != null)
            { fixedDiffSince = DateTime.MinValue; return; }
            PrepareCfg();
            int want = cfg.EngineSmoothMode == 0 && cfg.EngineFixedWanted ? 1 : 0;
            if (want == have || cfg.EngineSmoothMode != Config.EngineSmoothRunning) { fixedDiffSince = DateTime.MinValue; return; }   // (lo de Smooth Motion primero)
            if (fixedDiffSince == DateTime.MinValue) { fixedDiffSince = now; return; }
            if ((now - fixedDiffSince).TotalSeconds < 0.5) return;
            fixedDiffSince = DateTime.MinValue;
            Log.Write("Refresco del motor: " + (want > 0 ? "fijo (" + (cfg.CapMode ? "modo 30 con DLSS FG x4 del motor" : "modo 60 con DLSS FG") + ": sin el tope de 116 de Reflex con VRR)" : "VRR (" + (cfg.CapMode ? "modo 30" : cfg.FgOff ? "sin generacion" : "otra generacion") + ")") +
                      ": el driver solo lo mira al abrirlo, se vuelve a abrir el motor");
            ReabrirMotorSmooth(cfg.EngineSmoothMode, null, null);
        }

        // 21-11 (1.0.9): el motor que hace la imagen se abrio con FSR 3 FG y ya no toca (u otra al reves): se vuelve a abrir (su cadena de
        // presentacion es la de FidelityFX y no se cambia en marcha). Medio segundo seguido distinto, como Smooth Motion
        DateTime ffxDiffSince = DateTime.MinValue;
        void CheckEngineFfx(DateTime now)
        {
            int have = Config.EngineFfxOf(mpv);
            if (have < 0 || !ipcUp || !lsReady || homeBusy || passesBusy || relaunchEngine || relaunchSize != null || relevo != null)
            { ffxDiffSince = DateTime.MinValue; return; }
            PrepareCfg();
            int want = cfg.EngineFgChain;   // (1.2.4 MOTORFG: tambien XeSS FG y DLSS FG x3 / x4, MotorFg.cs)
            if (Config.ChainOk(have, want) || cfg.EngineSmoothMode != Config.EngineSmoothRunning) { ffxDiffSince = DateTime.MinValue; return; }   // (1.2.4 arreglos, RELEVO: ChainOk)
            if (ffxDiffSince == DateTime.MinValue) { ffxDiffSince = now; return; }
            if ((now - ffxDiffSince).TotalSeconds < 0.5) return;
            ffxDiffSince = DateTime.MinValue;
            Log.Write("Generacion del motor: " + (want == 1 ? "FSR 3 FG (FidelityFX)" : Config.ChainXess(want) ? "XeSS FG x" + (want / 10) + (want % 10 == 1 ? " con DLSS SR" : "") : want == 3 ? "DLSS FG x3 / x4 (PrismaFg.dll)" : "la de siempre") + " (antes " + have + "): su cadena de presentacion se elige al abrirlo, se vuelve a abrir el motor");
            ReabrirMotorSmooth(cfg.EngineSmoothMode, null, null);
        }

        DateTime smoothDiffSince = DateTime.MinValue;
        void CheckEngineSmooth(DateTime now)
        {
            if (!cfg.EngineSmoothAny && Config.EngineSmoothRunning <= 0) return;
            if (!ipcUp || !lsReady || homeBusy || passesBusy || relaunchEngine || relaunchSize != null || relevo != null || Config.EngineSmoothRunning < 0)
            { smoothDiffSince = DateTime.MinValue; return; }
            PrepareCfg();
            int want = cfg.EngineSmoothMode;
            if (want == Config.EngineSmoothRunning) { smoothDiffSince = DateTime.MinValue; return; }
            if (smoothDiffSince == DateTime.MinValue) { smoothDiffSince = now; return; }
            if ((now - smoothDiffSince).TotalSeconds < 0.5) return;
            smoothDiffSince = DateTime.MinValue;
            Log.Write("Smooth Motion del motor: " + (want > 0 ? "hace falta (generacion en el modo " + (cfg.CapMode ? "30" : "60") + (want == 2 ? ", sincronizacion cada 2 refrescos" : "") + ")" : "sobra (" + (cfg.HomeNative ? "en el menu de la consola, nativo" : cfg.FgOff ? "sin generacion" : cfg.Mode120 ? "modo 120, sin generacion" : "otro tipo de generacion en este modo (ini o L3)") + ")") +
                      ": el driver solo lo mira al abrirlo, se vuelve a abrir el motor");
            ReabrirMotorSmooth(want, null, null);
        }

        // El motor se vuelve a abrir con el Smooth Motion que toca ahora (CheckEngineSmooth; 19-11: tambien al entrar y salir del menu de la
        // PS5, src\MenuPs5.cs, con su titulo). Sin titulo, los avisos de la generacion
        void ReabrirMotorSmooth(int want, string title, string subtitle)
        {
            // (1.2.4 arreglos, RELEVO: el motor en espera solo fuera si no vale para lo de ahora; antes siempre -log del usuario 21:29:28,
            // 21:34:51-, y luego "no hay motor en espera: se arranca uno escondido ... 5068 ms". RelevoMotor.cs)
            PrepareCfg();
            string sbNo = StandbyIdentityDiff();
            if (sbNo != null) KillStandby("no vale para lo de ahora: " + sbNo);
            // 17-11 17:00 (el usuario: "al activar o desactivar el smooth motion la imagen se pone negra"): sin telon, como DLSS 5 apagado: uno
            // nuevo en espera con el Smooth Motion que toca arranca escondido (~3-4 s) mientras el de ahora sigue jugando, se despierta detras y
            // se pone delante en cuanto da imagen; el de antes se cierra debajo
            // 21-11 (el usuario: "al cambiar de fg sale que se desactiva y activa sin tocarlo"): el aviso decia "desactivada" con want=0, que
            // solo es SIN SMOOTH MOTION (p. ej. de Smooth Motion a DLSS FG, o el cambio de refresco del 60 con DLSS FG), y "activada" al
            // reves. Ahora dice lo que hay de verdad: el tipo de generacion de ahora, o desactivada solo si esta apagada
            string genNow = GenCurrent();
            bool genOff = genNow == "no" || cfg.FgOff;
            string stTitle = title ?? (genOff ? "Generación de fotogramas desactivada" : "Generación: " + GenLabel(genNow, true));
            string stSub = subtitle ?? (genOff ? "Solo las imágenes reales de la consola" : GenDetail(genNow, true) +
                                        (want == 0 && cfg.EngineFixedWanted ? " · ritmo fijo de la tele" : ""));
            if (cfg.Dlss5Seamless && StandbyOn() && EngineKnowsSinTelon() && signalOn && mpv != null)
            {
                Log.Write("Smooth Motion del motor: sin telon (otro motor detras con el ajuste nuevo; el de ahora sigue jugando)");
                StartRelevoToEngine(stTitle, stSub, mpv);
                return;
            }
            lsReady = false;
            relaunchEngine = true;
            bool pad = PadRecent;
            if (pad) { padRestart = true; padRestartAt = DateTime.Now; }
            ShowFastCurtain(pad ? "Un momento…" : null, pad ? stTitle : null, false);
            QuitMpvForRelaunch();
            CurtainToFront();
        }

        // Lo elegido ahora (generacion, modo, modelo, escalador, enfoque) al motor, si cambia: lo aplica al momento
        void SyncEngine()
        {
            if (!Config.EngineActive || !ipcUp) return;
            PrepareCfg();
            string want = EngineLiveForRunning();   // (1.2.4 arreglos, RELEVO: sin generacion si este motor se abrio con otra identidad, RelevoMotor.cs)
            if (want == engineSent) return;
            engineSent = want;
            lock (cmdQueue) cmdQueue.Add(JsonCmd("engine", "set " + want));
            Log.Write("Motor propio: " + want);
        }

        // 17-11 (VELOCIDAD): los ajustes del motor (generacion, modelo, bordes, repetidas, modo) en la MISMA tanda que el cambio de captura
        // (HOME <-> juego, captura con el mando). Antes los mandaba WatchLoop cuando le tocaba (cada 0,5 s): llegaban antes, durante o
        // despues del cambio (switch2.log 17-11: 02:48:03.459 y 02:44:27.495 "Motor propio: ..." DESPUES de "listo"/"HOME a la vista": el
        // juego salia sin la generacion y el HOME a 4K con la del juego, y el motor se reconfiguraba otra vez ya a la vista). El motor junta
        // lo pendiente (ApplyPending) y abre la captura una vez. null = nada que cambiar (se pide dentro del lock de cmdQueue)
        string EngineSetCmd()
        {
            if (!Config.EngineActive || !ipcUp) return null;
            PrepareCfg();
            string want = EngineLiveForRunning();   // (1.2.4 arreglos, RELEVO: RelevoMotor.cs)
            if (want == engineSent) return null;
            engineSent = want;
            return want;
        }

        static double EngineSharpen(Config cfg)
        {
            string c = cfg.CaptureNow;
            double s = c == "1920x1080" ? cfg.UpscaleSharpen1080 : c == "1280x720" ? cfg.UpscaleSharpen720 : c == "3840x2160" ? 0 : cfg.UpscaleSharpen;
            // 16-11: con los modelos transformer, menos (halo oscuro con K a 0,5)
            string p = cfg.EnginePresetNow;
            // (23-11, 1.2.4 ronda 6 DLSSSR: solo con DLSS de escalador; antes tambien con XeSS / FSR si el modelo de DLSS elegido era J/K/L/M,
            // y con motor_nitidez_transformer=0 de serie se quedaban sin enfoque)
            if ((p == "J" || p == "K" || p == "L" || p == "M") && Ce.EngineUpscaler(cfg) == "dlss") s *= cfg.EngineSharpenTransformer;
            // (22-11, 1.2.2 REESCALADO: "sin reescalado" -catrom- sin enfoque, como en Magpie; antes 0,5 a 1080p si el modelo de DLSS elegido
            // era E: x enfoque_sin_reescalado, src\Lanzador\Reescalado.cs)
            if (Ce.EngineUpscaler(cfg) == "catrom") s *= cfg.SharpenNoScale;
            return s;
        }

        // En el HOME a 4K nativo, sin generacion (como sin Magpie)
        static string EngineLive(Config cfg) { return EngineLive(cfg, Config.EngineSmoothRunning > 0); }
        // smooth = el motor lleva Smooth Motion del driver: en el modo 60 la generacion la hace el (fg=0 al motor)
        static string EngineLive(Config cfg, bool smooth) { return EngineLive(cfg, smooth, -1); }
        // chain = la cadena con la que se abre ese motor (-1 = la de ahora, Config.EngineFgChain): fg=1 solo con la de XeSS FG o FSR 3 FG
        static string EngineLive(Config cfg, bool smooth, int chain)
        {
            bool fg = !cfg.FgOff && !cfg.HomeNative && (!smooth || cfg.SmoothX4Now) && !cfg.Mode120 && (cfg.CapMode || Pantalla.Hz() >= 100);   // (1.2.4 ronda 6, SMX4: con Smooth Motion x4 el motor hace XeSS FG x2 debajo)   // (ConsoleEnhancer: modo 60 con la pantalla a 60 Hz: sin generacion)   // (17:50: tambien en el modo 30; 20-11 HZ120: a 120 sin generacion de fotogramas)
            // (1.2.4 ronda 6, QUITAFG: sin DLSS-G, fg=1 solo con la cadena de XeSS FG o FSR 3 FG; con la de siempre el motor iria sin generacion
            // -Smooth Motion va con fg=0 y sm=N-. Asi el motor de delante no enciende nada que no sea lo suyo)
            if (fg && (chain >= 0 ? chain : cfg.EngineFgChain) <= 0) fg = false;
            // (1.2.4 ronda 6, FGMULTI: con la cadena de FSR 3 FG x3 / x4, fg=0 al motor y fsrfg_multi=N; MultiFsr.cs)
            bool fsrMulCadena = Config.FsrMulChain(chain >= 0 ? chain : cfg.EngineFgChain), fsrMulOn = fg && fsrMulCadena;
            if (fsrMulCadena) fg = false;
            // (1.2.4 ronda 6, ENHANCED: con la cadena de Enhanced FG, fg=0 al motor y efg=N efg_modo=...; EnhancedFg.cs)
            bool efgCadena = Config.EfgChain(chain >= 0 ? chain : cfg.EngineFgChain), efgOn = fg && efgCadena;
            if (efgCadena) fg = false;
            return "fg=" + (fg ? "1" : "0") + " mode=" + (cfg.Mode120 && !cfg.HomeNative ? "120" : cfg.CapMode && !cfg.HomeNative ? "30" : "60") +
                   " preset=" + cfg.EnginePresetNow + " upscaler=" + Ce.EngineUpscaler(cfg) +
                   " sharp=" + EngineSharpen(cfg).ToString("0.00", CultureInfo.InvariantCulture) +
                   " reflex=" + cfg.EngineReflex.ToString(CultureInfo.InvariantCulture) +
                   // Bordes y tramado (el s2_aa.glsl de mpv, en el motor); en el HOME a 4K nada, como mpv
                   " aa=" + (cfg.EdgeAa && !cfg.HomeNative ? "1" : "0") + " dedither=" + (cfg.Dedither ? "1" : "0") +
                   // Repetidas fuera con generacion (o en el modo 30), como el filtro de mpv: tambien al encenderla o apagarla en marcha
                   " dedupe=" + (DedupeOn(cfg, !cfg.FgOff) ? "1" : "0") +
                   // 16-11 noche: la captura reducida antes de DLSS (motor_entrada_<modo>_<captura>=WxH; 0 = tal cual)
                   " entrada=" + cfg.EngineInNow + EngineDlss5Args(cfg) + (fsrMulCadena ? cfg.FsrMulLiveArgs(fsrMulOn) : "") + (efgCadena ? cfg.EfgLiveArgs(efgOn) : "");
        }

        // 20-11 (DLSS 5 EN EL MOTOR): nivel, estilo, modelo, mascara e interfaz de DLSS 5 para el motor (EngineDlss5.cs); dlss5=0 si no toca
        static string EngineDlss5Args(Config cfg)
        {
            // (20-11 LATENCIA4: motor_dlss5_home=0, el HOME / menu a 4K nativo sin DLSS 5)
            // (22-11, 1.2.3 REVISION3: dlss5=0 tambien con motor_dlss5=0 si esta engine\PrismaNr.dll. Desde la 1.2.3 "DLSS 5 por: Magpie" se
            // elige en marcha y el motor -en espera o de puente- seguia con el dlss5=1 de antes: al apagar DLSS 5 con el A el motor lo volvia
            // a crear -prueba de las 13:38:04, "DLSSNR creado a 2688x1512" con DLSS 5 apagado- y de puente lo tenia a la vez que Magpie)
            if (!cfg.EngineDlss5Now || (cfg.HomeNative && !cfg.EngineDlss5Home)) return cfg.EngineDlss5 || cfg.MotorDlss5Disponible ? " dlss5=0" : "";
            double[] n = cfg.LevelNums(cfg.Level30);
            Func<double, string> f = x => Math.Min(2, x).ToString("0.###", CultureInfo.InvariantCulture);
            int passes = Math.Max(1, Math.Min(3, cfg.Dlss5PassesNow));   // (1.2.4 ronda 6: las 1-3, siempre en el motor)
            return " dlss5=1 dlss5_nivel=" + f(n[0]) + "," + f(n[1]) + "," + f(n[2]) + "," + f(n[3]) +
                   // 20-11 03:40: las pasadas, su fuerza (dlss5_fuerza_pasadas) y el antiparpadeo del modo (lo mismo que a Magpie)
                   " dlss5_pasadas=" + passes.ToString(CultureInfo.InvariantCulture) +
                   " dlss5_fuerza_pasadas=" + f(cfg.Dlss5PassStrength[0]) + "," + f(cfg.Dlss5PassStrength[1]) + "," + f(cfg.Dlss5PassStrength[2]) +
                   " dlss5_antiparpadeo=" + Math.Max(0, Math.Min(3, cfg.AntiFlickerNow)).ToString(CultureInfo.InvariantCulture) +
                   " dlss5_antiparpadeo_ms=" + cfg.Dlss5AntiFlickerMs.ToString(CultureInfo.InvariantCulture) +
                   " dlss5_fuerza=" + f(n[4]) +   // 20-11: la "fuerza total" (5o numero del nivel; residualMultiplier en Magpie)
                   // 22-11 (1.2.4 DLSS5Q): con 2-3 pasadas, el croma de la correccion como a Magpie (dlss5_pasadas_color / _4k: sin el, color lavado)
                   " dlss5_color=" + f(cfg.Dlss5MultiChromaNow) +
                   " dlss5_entrada=" + cfg.EngineDlss5PctNow.ToString(CultureInfo.InvariantCulture) +   // 20-11: % de entrada (Config.EngineDlss5PctNow)
                   " dlss5_estilo=" + cfg.Dlss5Style.ToString(CultureInfo.InvariantCulture) +
                   " dlss5_modelo=" + cfg.Dlss5Model.ToString(CultureInfo.InvariantCulture) +
                   " dlss5_mascara=" + (cfg.Dlss5AutoMask ? "1" : "0") + " dlss5_interfaz=" + (cfg.Dlss5UiFix ? "1" : "0") +
                   " dlss5_dir=" + Integrado.Dlss5Dir;   // (1.2.4 ronda 6, QUITAFG: la DLL del usuario en extras\, sin copiarla a magpie\)
        }

        static string EngineArgs(Config cfg, string ipc) { return EngineArgs(cfg, ipc, -1, -1, -1); }
        static string EngineArgs(Config cfg, string ipc, int smOverride, int fixedOverride) { return EngineArgs(cfg, ipc, smOverride, fixedOverride, -1); }

        // smOverride / fixedOverride >= 0 (21-11, 1.0.9, motor de reserva): el Smooth Motion y el refresco con los que se abre, en vez de los de
        // la config de ahora (la "otra" generacion del modo, para que cambiarla con L3 sea despertar un motor ya abierto: ~0,5 s, no 4-5)
        // (1.2.4 ronda 6, QUITAFG: chainOverride >= 0 = la cadena del de reserva -20 XeSS FG con Smooth Motion delante-)
        static string EngineArgs(Config cfg, string ipc, int smOverride, int fixedOverride, int chainOverride)
        {
            // 17-11 16:55 (motor_generacion_60=smooth): Smooth Motion en el perfil de ConsoleEnhancerEngine.exe justo antes de abrirlo (el driver lo
            // mira al abrir el proceso); quitado si no toca (o si lo dejo puesto otra sesion)
            cfg.SmoothX4Log();   // (1.2.4 ronda 6, SMX4)
            int sm = smOverride >= 0 ? smOverride : cfg.EngineSmoothMode;
            if (cfg.EngineSmoothAny || NvProfiles.EngineSmoothOn || smOverride > 0) { if (!NvProfiles.SetEngineSmooth(sm)) sm = 0; }
            else sm = 0;
            Config.EngineSmoothLaunched = sm;
            // 20-11 (LATENCIA5): refresco fijo en el perfil del motor en el 60 con DLSS FG (motor_60fg_refresco_fijo); quitado si no toca
            bool fixedRefresh = fixedOverride >= 0 ? (fixedOverride > 0 && sm == 0) : (sm == 0 && cfg.EngineFixedWanted);
            if (fixedRefresh || NvProfiles.EngineFixedOn) { if (!NvProfiles.SetEngineFixed(fixedRefresh)) fixedRefresh = false; }
            Config.EngineFixedLaunched = fixedRefresh ? 1 : 0;
            // 21-11 (1.0.9): FSR 3 FG en el motor (1.2.4 MOTORFG: XeSS FG; src\Lanzador\MotorFg.cs). (1.2.4, integracion: el de reserva -Smooth
            // Motion / DLSS-G, la "otra" del modo- con la cadena de siempre: con la de XeSS FG de ahora no valdria nunca y gastaria memoria)
            Config.EngineFfxLaunched = chainOverride >= 0 ? chainOverride : smOverride >= 0 ? 0 : cfg.EngineFgChain;
            List<string> a = new List<string>();
            a.Add("ipc=" + ipc);
            a.Add("flag=" + Paths.FlagFile);
            a.Add("topmost=0"); // como la ventana de mpv: la pantalla de carga y el telon del lanzador quedan por encima
            a.Add("device=" + cfg.Device);
            a.AddRange(Ce.EngineArgsExtra(cfg)); // (ConsoleEnhancer: device_path, streamline, adapter_luid, datos)
            CapFmt capEf = CapturaSegura.Efectivo(cfg);   // 21-11 (1.2.1): lo que se abre de verdad (src\Lanzador\CapturaSegura.cs)
            a.Add("capture=" + capEf.Size);
            a.Add("fps=" + capEf.Fps);
            a.Add("format=" + EngineFormat(cfg));
            if (cfg.TestSource.Length > 0) a.Add(cfg.TestMotorArg);
            a.Add("dedupe=" + (DedupeOn(cfg, !cfg.FgOff) ? "1" : "0"));
            a.AddRange(EngineLive(cfg, sm > 0, Config.EngineFfxLaunched).Split(' '));
            // 18-11 (LATENCIA): el motor sabe que va con Smooth Motion (1 sincronizacion forzada, 2 cada 2 refrescos): vigila la cola de la
            // tele y no deja que se llene (motor nuevo, EngineLatencia.cs; uno de antes solo dice "Ajuste desconocido: sm=")
            if (sm > 0) a.Add("sm=" + sm.ToString(CultureInfo.InvariantCulture));
            // 16-11 (revision): la espera de "No Signal" del ini (apagado_espera_s / apagado_espera_arranque_s) tambien para el motor
            // 18-11 (CONSOLAS): la de la consola si es mayor (PS5 8 s...) y el negro con silencio que se da por apagada (PlayStation: nunca)
            a.Add("nosig_hold_ms=" + ((int)(EsperaSinSenalJugando(cfg) * 1000)).ToString(CultureInfo.InvariantCulture));
            a.Add("black_secs=" + BlackSecsLua());
            a.Add("nosig_hold_launch_ms=" + ((int)(cfg.NoSignalHoldLaunchSecs * 1000)).ToString(CultureInfo.InvariantCulture));
            // 20-11 (LATENCIA4): tele con VRR (motor_vrr=1; un motor de antes solo dice "Ajuste desconocido: vrr=1")
            if (cfg.EngineVrr > 0) { a.Add("vrr=1"); if (cfg.EngineVrrFgVsync >= 0) a.Add("vrr_fg_vsync=" + cfg.EngineVrrFgVsync.ToString(CultureInfo.InvariantCulture)); }
            if (fixedRefresh) a.Add("refresco_fijo=1");
            if (Config.EngineFfxLaunched == 1) a.Add("fg_tipo=fsr3");
            else if (Config.EngineFfxLaunched > 1) a.AddRange(cfg.MotorFgArgs(Config.EngineFfxLaunched).Trim().Split(' '));   // (1.2.4 MOTORFG)   // 21-11 (1.0.9, EngineFgFfx.cs del motor)   // 20-11 (LATENCIA5: para su registro; uno de antes solo dice "Ajuste desconocido")
            if (cfg.EngineExtra.Length > 0) a.AddRange(cfg.EngineExtra.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            return JoinArgs(a);
        }

        // (1.2.4 ronda 6, QUITAFG, fase 2: CheckLsPacing era de Magpie; fuera)
        DateTime ritmoOrigenSince = DateTime.MinValue;     // (22-11, 1.2.2 RITMO: el resumen por origen, src\Lanzador\Ritmo.cs)
        DateTime ritmoSince = DateTime.MinValue;
        DateTime lastLockLog = DateTime.MinValue, lastErrLog = DateTime.MinValue;

        static string Ms(double v) { return v.ToString(CultureInfo.InvariantCulture); }
        static string Pct(double v) { return Math.Round(v * 100).ToString(CultureInfo.InvariantCulture) + " %"; } // 0.75 -> "75 %"
        static string Pasadas(int n) { return n + (n > 1 ? " pasadas" : " pasada"); }

        void Tip(string text)
        {
            OnUi(delegate { tray.ShowBalloonTip(6000, PrismaUi.ShownName, text, ToolTipIcon.Info); });
        }

        void OpenConfig()
        {
            try { Process.Start("notepad.exe", "\"" + Paths.IniFile + "\""); }
            catch (Exception ex) { Log.Error("No se pudo abrir la config", ex); }
        }

        // (1.2.4 ronda 6, QUITAFG, fase 2: MagpieQuieto era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: SendScaleHotkey era de Magpie; fuera)

        void QuitRequested()
        {
            Log.Write("Salir pedido por el usuario");
            userExit = true;
            Process p = mpv;
            if (p != null)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        // Sin imagen mpv puede no tener ventana: se cierra directamente
                        if (!signalOn) p.Kill();
                        else
                        {
                            p.CloseMainWindow();
                            if (!p.WaitForExit(3000)) p.Kill();
                        }
                    }
                }
                catch (Exception ex) { Log.Error("No se pudo cerrar mpv", ex); }
            }
            CloseAudio();
            KillStandby("salir"); // (16-11: el motor en espera)
            KillRelevo("salir");  // (16-11, dlss5_sin_telon: el motor del relevo, si lo hay)
            ExitApp();
        }

        void CloseSplash()
        {
            if (splash == null) return;
            splash.Close();
            splash.Dispose();
            splash = null;
        }

        // Quita la pantalla de carga con un fundido (se cierra sola al terminar)
        void FadeSplash()
        {
            if (splash == null) return;
            splash.FadeOutAndClose();
            splash = null;
        }
        // 19-11: los ganchos de teclado y raton (WH_*_LL) y el del primer plano se quitan antes de salir de Application.Run. Si no, al cerrarse
        // el CLR aun le llegaban eventos del raton a un delegado ya muerto: 0xC0020001 / 0xC000041D despues de "Fin Switch2" (volcados de
        // 18-11 19:24, dos)
        void QuitarGanchos()
        {
            try
            {
                if (keyHook != IntPtr.Zero) { UnhookWindowsHookEx(keyHook); keyHook = IntPtr.Zero; }
                if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
                if (fgHook != IntPtr.Zero) { UnhookWinEvent(fgHook); fgHook = IntPtr.Zero; }
            }
            catch (Exception ex) { Log.Error("Quitando los ganchos al salir", ex); }
        }

        void ExitApp()
        {
            SonidoDobleAlAcabar(); // (21-11: src\SonidoDoble.cs)
            QuitarGanchos();
            CloseSplash();
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }

        // Ejecuta una accion en el hilo de la interfaz
        void OnUi(MethodInvoker a)
        {
            try { ui.BeginInvoke(a); } catch (Exception ex) { Log.Error("BeginInvoke", ex); }
        }

        void Fatal(string msg)
        {
            Log.Write("FATAL: " + msg);
            KillStandby("error de la app"); // (16-11: el motor en espera)
            KillRelevo("error de la app");
            // (1.2.02) el aviso salia DEBAJO del telon y de Magpie (los dos siempre encima): pantalla negra sin salida con el mando. Antes de
            // avisar: fuera la imagen (mpv / motor, audio, Magpie) y el telon; el aviso encima de todo
            try { Process v = mpv; if (v != null && !v.HasExited) v.Kill(); } catch { }
            try { CloseAudio(); } catch { }
            OnUi(delegate
            {
                try { HideCurtain(0); } catch { }
                CloseSplash();
                MessageBox.Show(msg, PrismaUi.ShownName, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
                ExitApp();
            });
        }

        // ---------------- Flujo principal (hilo de trabajo) ----------------

        void Worker()
        {
            try
            {
                // (23-11, 1.2.4 SINMPV: sin mpv, la imagen y el sonido los hace el motor: lo que tiene que estar es su exe)
                if (!File.Exists(cfg.EnginePath))
                {
                    Fatal(Deteccion.Tx.T("No se encuentra el motor de imagen en:\n" + cfg.EnginePath + "\n\nVuelve a descomprimir la app (falta engine\\ConsoleEnhancerEngine.exe).",
                                         "The image engine is missing:\n" + cfg.EnginePath + "\n\nUnzip the app again (engine\\ConsoleEnhancerEngine.exe is missing)."));
                    return;
                }

                Progress(0.12);
                // El perfil de NVIDIA de mpv / el motor (se lee al arrancar el proceso). (1.2.4 ronda 6, QUITAFG: sin Magpie que reabrir ni su perfil)
                if (cfg.PerformanceBoost) NvProfiles.Ensure();
                try { Log.Write("Imagen: " + cfg.SoloMotorTexto); } catch { }   // (1.2.4 SOLOMOTOR)
                // Lo ultimo elegido con el mando
                string size = LoadText(Paths.CaptureFile);
                if ((cfg.TestSource.Length == 0 || cfg.TestSource.Contains("{w}")) &&
                    (size == "1280x720" || size == "1920x1080" || size == "2560x1440" || size == "3840x2160"))
                    cfg.VideoSize = size;
                int lvl;
                dlss5Level = int.TryParse(LoadText(Paths.Dlss5LevelFile), out lvl) && lvl >= 1 && lvl <= 4 ? lvl : cfg.Dlss5DefaultLevel;
                int pas;
                dlss5Passes = int.TryParse(LoadText(Paths.Dlss5PassesFile), out pas) && pas >= 1 ? Math.Min(3, pas) : 1; // (como mucho 3)
                cfg.Passes30 = dlss5Passes;
                fgWanted = LoadText(Paths.FgFile) != "0";
                LoadPicks(); // (19-11, GENERACION: el tipo de generacion elegido con L3 la ultima vez)
                cfg.StrongDitherOn = LoadText(Paths.StrongDitherFile) != "0"; // (10-11: L3, sin fichero = el filtro nuevo)
                dlss5Shown = LoadText(Paths.Dlss5OnFile) != "0";
                cfg.Dlss5Off = !dlss5Shown;
                if (fgWanted)
                {
                    int hz = Pantalla.Hz();
                    if (hz > 0 && hz < 100)
                    {
                        Log.Write("Aviso: la pantalla esta a " + hz + " Hz (no a 120): la generacion de fotogramas no puede presentar 120. " +
                                  "Pasa a menudo tras un reinicio de la grafica; se arregla en Configuracion > Pantalla > Frecuencia de actualizacion o reiniciando el PC");
                        OnUi(delegate { OsdWarn("La tele está a " + hz + " Hz · la generación necesita 120 Hz (Configuración > Pantalla)", "4000"); });
                    }
                }
                homeRef = LoadRef();
                ConsolaAlAbrir(); // (18-11, CONSOLAS: la consola; sin su HOME reconocido, los ajustes guardados de toda la consola)
                string modeTxt = LoadText(Paths.ModeFile);
                cfg.CapMode = modeTxt == "30";
                cfg.Mode120 = modeTxt == "120"; // 20-11 (HZ120)
                // 20-11 (HZ120): si se guardo 120 pero esta captura no lo admite (4K/720p, consola sin 120 o tele < 120 Hz), al 60
                if (cfg.Mode120 && !Puede120(cfg.VideoSize)) { cfg.Mode120 = false; Log.Write("Modo 120 guardado pero no cabe con esta captura/consola/tele: se usa el 60"); }
                want30 = cfg.CapMode;
                if (cfg.CapMode) Log.Write("Modo 30 fps (elegido con el mando): imagenes reales a 30");
                else if (cfg.Mode120) Log.Write("Modo 120 fps (elegido con el mando, esports): captura a 120, sin generacion, baja latencia");
                Log.Write("Captura " + SizeName(cfg.VideoSize) + ", DLSS 5 nivel " + dlss5Level + " con " + dlss5Passes +
                          " pasada" + (dlss5Passes > 1 ? "s" : ""));
                // Se abre en el HOME a 4K nativo (lo normal al encender la consola); si ya se estaba jugando, HomeTick
                // lo ve en unos segundos y pone los ajustes del ultimo juego
                cfg.HomeNative = cfg.HomeNativeEnabled;
                if (cfg.HomeNative) Log.Write("Se empieza en el " + (Consolas.Actual.TieneHome ? "HOME" : "menu de la consola") + " a 4K nativo (60 fps" + (Consolas.Actual.MenuPropio ? ", sin Smooth Motion" : "") + ")");
                Thread padThread = new Thread(GamepadLoop);
                padThread.IsBackground = true;
                padThread.Start();
                if (!fgWanted) Log.Write("Generacion de fotogramas apagada (elegido con el boton de captura del mando)");
                OnUi(InstallKeyHook);
                OnUi(InstallMouseHook); // (18-11, MANDOS: bloquear a Joyxoff con la imagen delante; mando_bloquear_joyxoff)
                OnUi(InstallFgWatch); // (17-11: quien le quita el primer plano a la imagen)

                Progress(0.35);
                StartAudio();

                if (userExit) return;
                int exitCode = RunMpv();
                if (exitCode == int.MinValue) return; // error ya notificado

                CloseAudio();

                bool flag = File.Exists(Paths.FlagFile);
                Log.Write("La imagen (el motor) terminada, codigo " + exitCode + ", flag de apagado: " + (flag ? "si" : "no") +
                          (PowerOffClock != null ? " (" + PowerOffClock.ElapsedMilliseconds + " ms desde que se vio la consola apagada)" : ""));

                if (userExit) return;
                bool off = exitCode == MpvConst.PowerOffExitCode || flag;
                Log.Write(off ? "Consola apagada: se cierra la aplicacion" +
                                (PowerOffClock != null ? " (" + PowerOffClock.ElapsedMilliseconds + " ms desde que se vio)" : "")
                              : "Cierre normal de mpv; saliendo");
                // Sin la app, LS no debe quedarse abierto. Con la consola apagada no se vuelve a abrir nada: se mata sin esperar a
                // que termine y lo de segundo plano se devuelve a la vez (ExitRestore, si no empezo ya en PowerOffNow)
                KillStandby(off ? "consola apagada" : "fin de la sesion"); // (16-11: el motor en espera)
                KillRelevo(off ? "consola apagada" : "fin de la sesion");
                if (off) ExitRestore.Start();
                OnUi(ExitApp);
            }
            catch (Exception ex)
            {
                Log.Error("Error inesperado", ex);
                Fatal("Error inesperado:\n" + ex.Message);
            }
        }

        // Avance de la barra de la pantalla de carga segun el paso del arranque
        void Progress(double progress)
        {
            OnUi(delegate { if (splash != null) splash.SetProgress(progress); });
        }

        // La copia propia de Magpie (enlaces duros), una vez por sesion de la app (asi coge una version nueva de Magpie)
        // (1.2.4 ronda 6, QUITAFG, fase 2: EnsureMagpieCopy era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: PrestartMagpie era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: EnsureLossless era de Magpie; fuera)

        // ---------------- Audio de la consola ----------------
        // Un proceso solo de audio con el pin de audio de la capturadora: no toca la GPU ni la imagen. Su sonido
        // lo mide el mismo medidor que al de Streaming Center (se oye = la consola esta encendida)
        // (23-11, 1.2.4 SINMPV: ya no es un mpv sino el motor en su modo de sonido, ConsoleEnhancerEngine.exe --audio, con la misma
        // tuberia: src\Motor\Audio\AudioMain.cs. LaunchAudioMpv sigue con su nombre para no chocar con otras ramas)

        // Pines de audio de la capturadora: "Audio" y "Audio Mixer" (hay que pedirlos: ffmpeg solo no sabe)
        static readonly int[] AudioPinOrder = { 2, 1 };
        const int AudioTrySecs = 40;   // con imagen de la consola y sin oir nada: se prueba el siguiente pin

        volatile Process audioProc;
        volatile bool audioClosing;
        volatile bool scFallback;      // no se oye nada por los pines: el sonido lo da Streaming Center
        bool scStarted;
        volatile bool audioHeard;      // se ha oido algo por el pin actual
        double audioQueue = -1, audioPos = double.NaN;
        readonly object audioSync = new object();

        void StartAudio()
        {
            if (!cfg.ConsoleAudio)
            {
                if (cfg.StartStreamingCenter) StartStreamingCenter();
                return;
            }
            // Un Streaming Center que se haya quedado abierto (oculto, de una sesion que no termino bien) doblaria
            // el sonido y seguiria gastando grafica
            foreach (Process sc in Process.GetProcessesByName("StreamingCenter"))
            {
                try { sc.Kill(); Log.Write("Cerrado un Streaming Center que seguia abierto"); }
                catch (Exception ex) { Log.Error("Cerrando Streaming Center", ex); }
            }
            // (21-11) ni Windows ("Escuchar este dispositivo") ni un mpv de audio de otra sesion lo reproducen a la vez: src\SonidoDoble.cs
            SonidoDobleAlEmpezar();
            Thread t = new Thread(ConsoleAudioLoop);
            t.IsBackground = true;
            t.Start();
        }

        void CloseAudio()
        {
            audioClosing = true;
            Process a = audioProc;
            if (a != null)
            {
                // Sin esperar: tras un reinicio de la grafica el driver de la capturadora puede tenerlo colgado
                try { if (!a.HasExited) a.Kill(); } catch { }
            }
            if (scStarted || (!cfg.ConsoleAudio && cfg.StartStreamingCenter)) CloseStreamingCenter();
            SonidoDobleAlAcabar(); // (21-11: devuelve "Escuchar este dispositivo" si la app lo quito)
        }

        // Los procesos cuyo sonido es el de la consola
        void AddAudioPids(HashSet<uint> pids)
        {
            if (!cfg.ConsoleAudio || scFallback)
            {
                foreach (Process sc in Process.GetProcessesByName("StreamingCenter")) pids.Add((uint)sc.Id);
                return;
            }
            Process a = audioProc;
            try { if (a != null && !a.HasExited) pids.Add((uint)a.Id); }
            catch (InvalidOperationException) { }
        }

        void ConsoleAudioLoop()
        {
            int pin = -1;
            // Ya se oyo por este pin en otra sesion, o es una prueba sin consola: no hay nada que buscar
            bool known = cfg.TestSource.Length > 0 && !cfg.TestAudioSearch;
            try
            {
                int saved;
                if (!cfg.TestAudioSearch && File.Exists(Paths.AudioPinFile) &&
                    int.TryParse(File.ReadAllText(Paths.AudioPinFile).Trim(), out saved) &&
                    Array.IndexOf(AudioPinOrder, saved) >= 0)
                {
                    pin = saved;
                    known = true;
                }
            }
            catch (Exception ex) { Log.Error("Leyendo " + Paths.AudioPinFile, ex); }
            int tryIndex = 0;
            if (pin < 0) pin = AudioPinOrder[0];
            int launch = 0;
            string myPid = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            DateTime lastFail = DateTime.MinValue;
            bool surround = cfg.AudioChannels == 8;
            try
            {
                while (!userExit && !audioClosing && !scFallback)
                {
                    string ipc = "ce-audio-" + myPid + "-" + (++launch);
                    audioHeard = false;
                    Process a = LaunchAudioMpv(pin, ipc, surround);
                    audioProc = a;
                    bool nextPin = AudioLink(a, ipc, pin, ref known);
                    try { if (!a.HasExited) a.Kill(); } catch { }
                    if (userExit || audioClosing) return;
                    // No llego a abrirse: no todos los pines admiten 8 canales ("Audio Mixer" no). Se prueba con el otro formato
                    bool neverPlayed;
                    lock (audioSync) neverPlayed = double.IsNaN(audioPos);
                    if (!nextPin && neverPlayed && cfg.AudioChannels == 8)
                    {
                        surround = !surround;
                        Log.Write("El audio no se abre: se prueba con " + (surround ? "8 canales" : "2 canales"));
                    }
                    if (nextPin)
                    {
                        tryIndex++;
                        if (tryIndex >= AudioPinOrder.Length)
                        {
                            Log.Write("No se oye nada por ningun pin de la capturadora: el sonido lo da Streaming Center");
                            scFallback = true;
                            scStarted = true;
                            StartStreamingCenter();
                            return;
                        }
                        pin = AudioPinOrder[tryIndex];
                        Log.Write("Sin sonido en " + AudioTrySecs + " s con la consola encendida: se prueba el pin de audio " + pin);
                    }
                    else
                    {
                        // Se cerro solo (corte de la señal, capturadora ocupada): se vuelve a abrir
                        if ((DateTime.Now - lastFail).TotalSeconds >= 30) Log.Write("El audio se ha cerrado: se vuelve a abrir");
                        lastFail = DateTime.Now;
                    }
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex) { Log.Error("Audio de la consola", ex); }
        }

        void SaveAudioPin(int pin)
        {
            try
            {
                File.WriteAllText(Paths.AudioPinFile, pin.ToString(CultureInfo.InvariantCulture));
                Log.Write("Sonido de la consola por el pin de audio " + pin + ": guardado");
            }
            catch (Exception ex) { Log.Error("Guardando " + Paths.AudioPinFile, ex); }
        }

        // 8 canales -> estereo. Pedir 2 a la capturadora se queda con izquierda y derecha y tira el canal central, donde van
        // las voces cuando la consola manda sonido envolvente. Los canales 3 y 4 entran los dos porque HDMI y Windows ordenan
        // central y graves al reves. El limitador evita que la suma sature; con sonido estereo no cambia nada
        // (23-11, SINMPV: la misma mezcla y el limitador los hace el motor con audio_canales=8: src\Motor\Audio\AudioOut.cs)

        // 23-11 (1.2.4, SINMPV; el usuario: "Toda la app sera en el motor sin magpie ni nada del estilo"): el sonido de la consola por el motor
        // (ConsoleEnhancerEngine.exe --audio: DirectShow como el dshow de mpv -el mismo dispositivo, pin, formato y trozos- y WASAPI como
        // --ao=wasapi), en un proceso aparte como el mpv de audio. Misma tuberia: AudioLink no cambia. Medido sin capturadora (fuente de prueba
        // con el ritmo de la capturadora, trozos de 20 ms con temblor de 4 ms, loopback de la salida): clic -> salida 78,5 ms (p95 78,8), sin
        // cortes; con el reloj de la consola desviado +-500 ppm la cola se corrige sola (src\Motor\Audio\AudioOut.cs). audio_motor_extra=
        // pasa claves al motor (audio_cola_ms=30, audio_salida_ms=10, audio_deriva=1...); test_audio=clic|tono (solo pruebas: sin capturadora)
        Process LaunchAudioMpv(int pin, string ipc, bool surround)
        {
            List<string> a = new List<string>();
            a.Add("--audio");
            a.Add("datos=" + Ce.EngineLogDir);
            a.Add("ipc=" + ipc);
            a.Add("audio_disp=" + (cfg.AudioDevice.Length > 0 ? cfg.AudioDevice : cfg.Device)); // (ConsoleEnhancer: audio_dispositivo)
            // (ConsoleEnhancer: audio_pines: el pin de audio del filtro de video, como audio_pin_name de mpv)
            if (cfg.AudioPins) a.Add("audio_pin=" + pin.ToString(CultureInfo.InvariantCulture));
            a.Add("audio_hz=48000");
            a.Add("audio_bits=16");
            a.Add("audio_canales=" + (surround ? "8" : "2"));
            // Trozos de 20 ms de la capturadora (por defecto ~500 ms) (21-11, 1.2.1: audio_bloque_ms, de serie 20; src\Lanzador\Latencia.cs)
            a.Add("audio_bloque_ms=" + cfg.AudioBlockMs.ToString(CultureInfo.InvariantCulture));
            if (cfg.TestAudio.Length > 0) a.Add("audio_prueba=" + cfg.TestAudio);   // (solo pruebas: sin abrir la capturadora)
            if (cfg.AudioEngineExtra.Length > 0) a.AddRange(cfg.AudioEngineExtra.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            AudioQueueLog.Block = cfg.AudioBlockMs; AudioQueueLog.High = cfg.AudioQueueHighS; AudioQueueLog.Low = cfg.AudioQueueLowS;
            ProcessStartInfo psi = new ProcessStartInfo(cfg.EnginePath, JoinArgs(a));
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WorkingDirectory = Path.GetDirectoryName(cfg.EnginePath);
            Log.Write("Audio de la consola (motor): pin " + pin + ": " + psi.Arguments);
            Process p = Process.Start(psi);
            AudioJob.Hold(p); // (21-11: se cierra con la app aunque esta muera de golpe; src\SonidoDoble.cs)
            return p;
        }

        // Vigila el mpv de audio hasta que se cierre. true = este pin no suena: hay que probar otro
        bool AudioLink(Process a, string ipc, int pin, ref bool known)
        {
            NamedPipeClientStream pipe = null;
            StreamWriter w = null;
            int silentSecs = 0, stalled = 0, noPos = 0;
            double lastSeen = double.NaN;
            bool fast = false;
            lock (audioSync) { audioQueue = -1; audioPos = double.NaN; }
            try
            {
                while (!a.HasExited && !userExit && !audioClosing)
                {
                    Thread.Sleep(1000);
                    if (pipe == null)
                    {
                        pipe = new NamedPipeClientStream(".", ipc, PipeDirection.InOut, PipeOptions.Asynchronous);
                        try { pipe.Connect(500); }
                        catch (Exception) { pipe.Dispose(); pipe = null; continue; }
                        w = new StreamWriter(pipe, new UTF8Encoding(false));
                        w.AutoFlush = true;
                        w.NewLine = "\n";
                        StreamReader r = new StreamReader(pipe);
                        Thread rt = new Thread(delegate () { AudioReader(r); });
                        rt.IsBackground = true;
                        rt.Start();
                    }
                    w.WriteLine("{\"command\":[\"get_property\",\"demuxer-cache-duration\"],\"request_id\":1}");
                    w.WriteLine("{\"command\":[\"get_property\",\"time-pos\"],\"request_id\":2}");

                    double q, pos;
                    lock (audioSync) { q = audioQueue; pos = audioPos; }

                    // Sin datos nuevos (la capturadora dejo de mandar, o no llego a abrirse): se reabre
                    if (!double.IsNaN(pos))
                    {
                        if (pos == lastSeen) stalled++; else stalled = 0;
                        lastSeen = pos;
                        if (stalled >= 5) { Log.Write("El audio no avanza: se reabre"); return false; }
                    }
                    else if (++noPos >= 20) { Log.Write("El audio no arranca: se reabre"); return false; }

                    // El reloj de la capturadora y el de la salida de sonido no son iguales: lo que sobra se
                    // acumularia como retraso sin fin, y lo que falta seria un corte. Se corrige con un 0,4 %,
                    // que no se nota. Con trozos de 20 ms lo normal es 0-40 ms en cola
                    if (q >= 0)
                    {
                        // (21-11, 1.2.1: los umbrales con audio_cola_alta_ms / audio_cola_baja_ms, de serie 70 / 30; y la cola al log cada 5 min)
                        bool want = fast ? q >= cfg.AudioQueueLowS : q > cfg.AudioQueueHighS;
                        if (want != fast)
                        {
                            fast = want;
                            w.WriteLine("{\"command\":[\"set_property\",\"speed\"," + (fast ? "1.004" : "1") + "]}");
                        }
                        if (audioHeard) AudioQueueLog.Add(q, fast);
                    }

                    // Pin sin confirmar: en cuanto se oye se guarda. Si con la consola a la vista no se oye
                    // nada (el HOME suena al moverse por el), se prueba otro
                    if (!known)
                    {
                        if (audioHeard)
                        {
                            known = true;
                            SaveAudioPin(pin);
                        }
                        else if (consoleOn && ++silentSecs >= AudioTrySecs) return true;
                    }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { Log.Error("Enlace con el mpv de audio", ex); }
            finally { if (pipe != null) pipe.Dispose(); }
            return false;
        }

        void AudioReader(StreamReader r)
        {
            try
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    bool queue = line.Contains("\"request_id\":1"), pos = line.Contains("\"request_id\":2");
                    if (!queue && !pos) continue;
                    System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                        line, "\"data\":(-?[0-9.eE+-]+)");
                    if (!m.Success) continue;
                    double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    lock (audioSync)
                    {
                        if (queue) audioQueue = v; else audioPos = v;
                    }
                }
            }
            catch (Exception) { }
        }

        void StartStreamingCenter()
        {
            // (ConsoleEnhancer: sin streaming_center_path no hay programa de reserva: solo se avisa)
            if (string.IsNullOrEmpty(cfg.StreamingCenterPath)) { Log.Write("Sin sonido por la capturadora y sin programa de reserva (streaming_center_path vacio): repite el asistente para elegir otra entrada de audio"); OsdWarn(Deteccion.Tx.T("No se oye la consola: elige otra entrada de audio en el asistente", "No console audio: pick another audio input in the wizard"), "6000"); return; }
            try
            {
                scClosing = false;
                Log.Write("Abriendo Streaming Center oculto");
                ProcessStartInfo psi = new ProcessStartInfo(cfg.StreamingCenterPath);
                psi.UseShellExecute = true;
                psi.WindowStyle = ProcessWindowStyle.Minimized;
                Process.Start(psi);
                // Ocultar sus ventanas mientras dure la sesion (puede abrir mas de una, y tarde)
                Thread h = new Thread(HideStreamingCenterLoop);
                h.IsBackground = true;
                h.Start();
                Thread.Sleep(2000);
            }
            catch (Exception ex) { Log.Error("No se pudo abrir Streaming Center", ex); }
        }

        // Lo abrimos nosotros (oculto) solo para el audio: se cierra junto con mpv
        void CloseStreamingCenter()
        {
            scClosing = true;
            foreach (Process p in Process.GetProcessesByName("StreamingCenter"))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                    Log.Write("Streaming Center cerrado");
                }
                catch (Exception ex) { Log.Error("Cerrando Streaming Center", ex); }
            }
        }

        void HideStreamingCenterLoop()
        {
            DateTime start = DateTime.Now;
            while (!userExit && !scClosing)
            {
                int n = Win.HideProcessWindows("StreamingCenter");
                if (n > 0) Log.Write("Ocultadas " + n + " ventana(s) de Streaming Center");
                // Los primeros 30 s se vigila a menudo; despues basta con cada 2 s
                Thread.Sleep((DateTime.Now - start).TotalSeconds < 30 ? 200 : 2000);
            }
        }

        // Enlace con mpv por la tuberia IPC, cada 0,1 s:
        //  - envia el estado del audio de la consola ("sound"/"silent") al detector Lua
        //    (cada 0,5 s)
        //  - vigila que mpv siga recibiendo fotogramas. Al apagar la consola la capturadora
        //    deja de mandar fotogramas y el nucleo de mpv se queda bloqueado (el script Lua
        //    ni se ejecuta), asi que esta vigilancia tiene que ser externa: si time-pos no
        //    avanza (o mpv no contesta) durante cfg.PowerOffStallMs -> consola apagada.
        const int ArmSecs = 3;
        readonly object linkSync = new object();
        double lastPos = double.NaN;
        DateTime lastAdvance, firstAdvance = DateTime.MinValue;
        bool paused;
        // El script Lua dice si lo que se ve es la consola (no la pantalla "No Signal"):
        // solo entonces se vigila, para no cerrar al pasar de "No Signal" a la imagen
        volatile bool consoleOn;

        // connectNow (16-11, motor_en_espera): el motor despertado ya tiene su tuberia abierta: se conecta en el primer paso (no a los 0,5 s)
        void MpvLinkLoop(Process p, string ipcName, bool connectNow = false)
        {
            const float SilencePeak = 0.0005f; // ~ -66 dBFS
            NamedPipeClientStream pipe = null;
            StreamWriter w = null;
            string lastLogged = null;
            bool signalAcked = false;
            HashSet<uint> pids = new HashSet<uint>();
            float peak = -1f;
            int step = 0;
            DateTime linkStart = DateTime.Now;
            bool linkWarned = false, quitSent = false;
            bool linkIdle = false; // 16-11 tarde (APAGADO): este enlace dijo "en el HOME sin nada arrancando" (HomeIdleCmds)
            lastAdvance = DateTime.Now;
            try
            {
                // Hasta que se cierre o se lance otro (uno colgado tras un reinicio de la grafica
                // no termina nunca)
                Stopwatch stepWait = new Stopwatch();
                while (!p.HasExited && mpv == p)
                {
                    // 17-11 (VELOCIDAD): los 0,1 s de siempre entre pasos, pero lo que se encole entretanto (cmdQueue, osdQueue) se escribe
                    // al momento (linkWake). Antes Thread.Sleep(100): cada orden esperaba al paso siguiente
                    stepWait.Restart();
                    int left;
                    while ((left = 100 - (int)stepWait.ElapsedMilliseconds) > 0)
                    {
                        if (!linkWake.WaitOne(left)) break;
                        if (mpv != p) { linkWake.Set(); break; } // (es para el enlace del mpv / motor de ahora)
                        if (w == null) { Thread.Sleep(Math.Max(1, 100 - (int)stepWait.ElapsedMilliseconds)); break; } // (sin tuberia aun: al conectar)
                        try
                        {
                            lock (osdQueue)
                            {
                                foreach (string t in osdQueue) w.WriteLine(t);
                                osdQueue.Clear();
                            }
                            lock (cmdQueue)
                            {
                                foreach (string c in cmdQueue) w.WriteLine(c);
                                cmdQueue.Clear();
                            }
                        }
                        catch (IOException) { w = null; pipe.Dispose(); pipe = null; ipcUp = false; linkStart = DateTime.Now; }
                    }
                    step++;
                    // --- Audio de la consola ---
                    // Pico maximo de cada 0,5 s (el medidor da el pico de cada ~10 ms)
                    string state = null;
                    bool meter = cfg.ConsoleAudio || cfg.StartStreamingCenter;
                    if (meter)
                    {
                        if (step % 5 == 1)
                        {
                            pids.Clear();
                            AddAudioPids(pids);
                        }
                        try { peak = Math.Max(peak, AudioMeter.PeakOfProcesses(pids)); }
                        catch (Exception ex) { if (lastLogged != "error") Log.Error("Medidor de audio", ex); lastLogged = "error"; }
                    }
                    if (meter && step % 5 == 0)
                    {
                        if (peak < 0)
                        {
                            if (lastLogged != "none") Log.Write("Sin sesion de audio de la consola (audio desconocido)");
                            lastLogged = "none";
                        }
                        else
                        {
                            state = peak > SilencePeak ? "sound" : "silent";
                            if (state != lastLogged) Log.Write("Audio de la consola: " + state);
                            ConsolaAudio(state); // (18-11: para el aviso de HDCP, ConsolasApp.cs)
                            CierresAudio(state); // (20-11, CIERRES: la consola sonando con la imagen parada no esta apagada)
                            CapNotaAudio(state); // (21-11, 1.2.1: la consola sonando sin imagen tras cambiar de formato, CapturaSegura.cs)
                            lastLogged = state;
                            // Con la consola apagada no hay sonido: si lo hay, esta encendida
                            if (state == "sound")
                            {
                                signalOn = true;
                                audioHeard = true;
                            }
                        }
                        peak = -1f;
                    }

                    // --- Conexion con mpv (se reintenta cada 0,5 s) ---
                    if (w == null && (step % 5 == 0 || (connectNow && step == 1)))
                    {
                        pipe = new NamedPipeClientStream(".", ipcName,
                                                         PipeDirection.InOut, PipeOptions.Asynchronous);
                        try { pipe.Connect(1000); }
                        catch (Exception) { pipe.Dispose(); pipe = null; }
                        // Sin IPC no se puede vigilar el apagado: que conste. 16-11: solo si ese mpv (o motor) sigue vivo, es el de ahora y no
                        // se le ha pedido que se cierre: salia el ultimo intento del que se acababa de cerrar, ya con otro lanzado ("Sin enlace
                        // IPC con mpv (...-2) tras 10 s" a las 22:07:35, con el motor -3 ya en marcha; Connect espera hasta 1 s)
                        if (pipe == null && !linkWarned && !quitSent && (DateTime.Now - linkStart).TotalSeconds >= 10 && !p.HasExited && mpv == p)
                        {
                            linkWarned = true;
                            // (22-11, 1.2.2: con el motor recien abierto es que aun arranca: Streamline y, la primera vez, sus shaders, 20-30 s)
                            if (CapMotorArrancando(p)) Log.Write("Sin enlace IPC con el motor (\\\\.\\pipe\\" + ipcName + ") tras 10 s: aun arrancando (Streamline; la primera vez compila sus shaders)");
                            else Log.Write("Sin enlace IPC con mpv (\\\\.\\pipe\\" + ipcName + ") tras 10 s: sin vigilancia de la imagen");
                        }
                        if (pipe != null)
                        {
                            if (linkWarned) Log.Write("Enlace IPC con mpv establecido");
                            linkWarned = false;
                            CapEnlace(p); // (22-11, 1.2.2: el motor recien abierto ya ha arrancado; la captura segura cuenta desde aqui, CapturaSegura.cs)
                            w = new StreamWriter(pipe, new UTF8Encoding(false));
                            w.AutoFlush = true;
                            w.NewLine = "\n";
                            // 16-11 (revision): la espera de "No Signal" arrancando un juego (o la de nada tras el menu de apagado) tambien
                            // para el mpv / motor que se conecta dentro de la ventana: switch2-launch se lo llevaba el que estaba conectado
                            // al marcarla y uno lanzado despues (DLSS 5, otra captura, mpv reabierto) empezaba con la de 6 s
                            // 16-11 tarde (APAGADO): en el HOME sin nada arrancando, en vez de la espera de arrancar, switch2-powermenu (HomeIdleCmds)
                            try
                            {
                                DateTime nowL = DateTime.Now;
                                string busyL;
                                linkIdle = false;
                                if (HomeIdle(nowL, out busyL))
                                {
                                    w.WriteLine(JsonCmd("script-message", "switch2-powermenu", "3"));
                                    linkIdle = true;
                                }
                                else
                                {
                                    double lr = (launchUntil - nowL).TotalSeconds;
                                    if (lr > 0.5) w.WriteLine(JsonCmd("script-message", "switch2-launch", Ms(Math.Ceiling(lr))));
                                }
                                double pr = powerMenuAt == DateTime.MinValue ? 0 : PowerMenuSecs - (nowL - powerMenuAt).TotalSeconds;
                                if (pr > 0.5) w.WriteLine(JsonCmd("script-message", "switch2-powermenu", Ms(Math.Ceiling(pr))));
                            }
                            catch (IOException) { }
                            StreamReader r = new StreamReader(pipe);
                            Thread rt = new Thread(delegate () { readerMpv = p; MpvReader(r); });
                            rt.IsBackground = true;
                            rt.Start();
                        }
                    }
                    if (w != null)
                    {
                        try
                        {
                            if (state != null)
                                w.WriteLine("{\"command\":[\"script-message\",\"switch2-audio\",\"" + state + "\"]}");
                            // El script Lua (o el motor) repite su aviso de imagen hasta recibir esto. 15-11: solo cuando ha llegado su
                            // aviso, no por el sonido de la consola: contestado antes de su primera imagen, no volvia a avisar (el motor
                            // a las 19:24:53 y 19:25:27 se cerro a los 30 s "sin imagen"; el script de mpv paraba igual)
                            if (signalHeard && !signalAcked)
                            {
                                w.WriteLine("{\"command\":[\"script-message\",\"switch2-signal-ack\"]}");
                                signalAcked = true;
                            }
                            w.WriteLine("{\"command\":[\"get_property\",\"time-pos\"],\"request_id\":77}");
                            w.WriteLine("{\"command\":[\"get_property\",\"pause\"],\"request_id\":78}");
                            if (Config.EngineActive) w.WriteLine(CierresQuery); // (20-11, CIERRES: si la capturadora sigue mandando al motor)
                            // Avisos del mando en pantalla (los dibuja el script de mpv, una pastilla: Magpie los escala con la imagen;
                            // o el motor propio). Ya son la orden entera (OsdCmd: script-message switch2-osd)
                            lock (osdQueue)
                            {
                                foreach (string t in osdQueue) w.WriteLine(t);
                                osdQueue.Clear();
                            }
                            // Ordenes para mpv (cambio de captura sin cerrarlo)
                            lock (cmdQueue)
                            {
                                foreach (string c in cmdQueue) w.WriteLine(c);
                                cmdQueue.Clear();
                            }
                            // 16-11 tarde (APAGADO): en el HOME sin nada arrancando, "No Signal" sin esperar tambien en el script de mpv y el motor
                            // (cada 0,5 s; despues de las ordenes: un switch2-launch 0 de LaunchCancelCheck no le quita este)
                            if (step % 5 == 0)
                            {
                                List<string> ic = HomeIdleCmds(DateTime.Now, p == mpv, ref linkIdle);
                                if (ic != null) foreach (string c in ic) w.WriteLine(c);
                            }
                            ipcUp = true;
                            if (quitForRelaunch)
                            {
                                // El aviso ya lo da el telon (puesto al pedirlo): se cierra enseguida
                                quitForRelaunch = false;
                                // 16-11 (motor_en_espera): el motor que deja paso a mpv + Magpie no se cierra: suelta la capturadora y se esconde
                                // (RunMpv lo aparta al contestar switch2-standby-ok; si no contesta en 1,5 s, se cierra como siempre). Mientras,
                                // sin vigilancia de apagado (deja de presentar a proposito: switchUntil, como en un cambio de captura)
                                if (quitToStandby && Config.EngineActive)
                                {
                                    quitToStandby = false;
                                    standbyGot = null;
                                    standbyGotMs = -1;
                                    standbyAskedSw = Stopwatch.StartNew();
                                    standbyAsked = p;
                                    switchUntil = DateTime.Now.AddSeconds(3);
                                    w.WriteLine("{\"command\":[\"script-message\",\"switch2-standby\"]}");
                                    Log.Write("Motor en espera: se le pide al motor (pid " + p.Id + ") que suelte la capturadora y se esconda (switch2-standby)");
                                }
                                else w.WriteLine("{\"command\":[\"quit\",\"0\"]}");
                                quitSent = true;
                            }
                            // 16-11 (dlss5_sin_telon): el relevo ya ha puesto el motor delante (o el telon): este mpv se cierra, debajo. Solo este
                            // mpv (quitForRelaunch lo cogeria el enlace siguiente si este ya no estuviera: el del motor recien tomado)
                            if (relevoQuit == p)
                            {
                                relevoQuit = null;
                                // (1.2.4 arreglos, RELEVO: si el que se va es un motor, a la espera en vez de cerrarse; RelevoMotor.cs)
                                if (!RelevoOldToStandby(p, w)) w.WriteLine("{\"command\":[\"quit\",\"0\"]}");
                                quitSent = true;
                            }
                        }
                        catch (IOException) { w = null; pipe.Dispose(); pipe = null; ipcUp = false; linkStart = DateTime.Now; }
                    }

                    // --- Vigilancia de fotogramas ---
                    if (CheckStall(p)) return;
                    if (mpv == p) CapNotaConsola(DateTime.Now); // (21-11, 1.2.1: cuando se vio la consola por ultima vez, CapturaSegura.cs)
                }
            }
            catch (InvalidOperationException) { } // ese mpv ya se cerro y se relanzo (Process liberado)
            catch (Exception ex) { Log.Error("Enlace con mpv", ex); }
            finally
            {
                if (pipe != null) pipe.Dispose();
                if (mpv == p) ipcUp = false;
            }
        }

        [ThreadStatic] static Process readerMpv; // el mpv del que lee este hilo de MpvReader (los avisos de apagado solo del actual)

        // Lee las respuestas de mpv (una linea JSON por mensaje)
        void MpvReader(StreamReader r)
        {
            try
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Contains("\"request_id\":77"))
                    {
                        CierresReply77(); // (20-11, CIERRES: el motor contesta)
                        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                            line, "\"data\":(-?[0-9.eE+-]+)");
                        if (!m.Success) continue;
                        double pos = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                        lock (linkSync)
                        {
                            if (double.IsNaN(lastPos) || pos != lastPos)
                            {
                                lastAdvance = DateTime.Now;
                                if (firstAdvance == DateTime.MinValue) firstAdvance = lastAdvance;
                            }
                            lastPos = pos;
                        }
                    }
                    else if (line.Contains("\"request_id\":78"))
                    {
                        lock (linkSync) paused = line.Contains("\"data\":true");
                    }
                    else if (line.Contains("\"request_id\":79")) CierresCaptura(line); // (20-11, CIERRES: switch2-captura-ms del motor)
                    else if (line.Contains("\"client-message\"") && CierresEvent(line)) { } // (20-11, CIERRES: switch2-gpu del motor)
                    else if (line.Contains("\"client-message\"") && FpsEvent(line)) { } // (21-11: switch2-fps del motor, src\Lanzador\FpsBajo.cs)
                    else if (line.Contains("\"client-message\"") && MotorDlss5Evento(line)) { }
                    else if (line.Contains("\"client-message\"") && FsrMulEvento(line)) { } // (1.2.4 ronda 6, FGMULTI: switch2-fsrmulti rota, MultiFsr.cs)
                    else if (line.Contains("\"client-message\"") && MotorFgEvento(line)) { } // (1.2.4 MOTORFG: switch2-fgmult no, MotorFg.cs) // (22-11, 1.2.3 MOTOR5: switch2-dlss5 roto del motor, MotorDlss5.cs)
                    else if (line.Contains("\"client-message\"") && AjPruebaEvento(line)) { } // (22-11, 1.2.2: pruebas del menu de ajustes, AjustesGuia.cs)
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-pad\""))
                    {
                        // Lo mismo que el mando, por la tuberia de mpv (para probar sin mando)
                        lastPadAction = DateTime.Now;
                        if (InHome || homeBusy) Osd(MenuTexto + (cfg.HomeNative ? " en 4K nativo" : "") + " · los ajustes se cambian dentro de cada juego");
                        else if (line.Contains("\"720\"")) RequestCapture("1280x720");
                        else if (line.Contains("\"1080\"")) RequestCapture("1920x1080");
                        else if (line.Contains("\"1440\"")) RequestCapture("2560x1440");
                        else if (line.Contains("\"4k\"")) RequestCapture("3840x2160");
                        else if (line.Contains("\"dlss5\"")) ToggleDlss5();
                        else if (line.Contains("\"mas\"")) ChangeLevel(+1);
                        else if (line.Contains("\"menos\"")) ChangeLevel(-1);
                        else if (line.Contains("\"m30\"")) SetMode(true);
                        else if (line.Contains("\"m60\"")) SetMode(false);
                        else if (line.Contains("\"m120\"")) { if (Puede120()) SetModeN(120); else Osd(Aviso120NoCabe()); } // 20-11 (HZ120)
                        else if (line.Contains("\"n1\"")) SetLevel(1);
                        else if (line.Contains("\"n2\"")) SetLevel(2);
                        else if (line.Contains("\"n3\"")) SetLevel(3);
                        else if (line.Contains("\"n4\"")) SetLevel(4);
                        else if (line.Contains("\"p1\"")) SetPasses(1);
                        else if (line.Contains("\"p2\"")) SetPasses(2);
                        else if (line.Contains("\"p3\"")) SetPasses(3);
                        else if (line.Contains("\"p4\"")) ToggleDlss5(); // (como el boton A)
                        else if (line.Contains("\"fg\"")) ToggleFrameGeneration();
                        else if (line.Contains("\"tramado\"")) ToggleStrongDither(); // (como L3 mantenido)
                        // 19-11 (GENERACION): "gen" = una pulsacion de L3 (el selector); "gen_<tipo>" = ese tipo ya (no, smooth, dlss, xess, fsr3, vfg)
                        else if (line.Contains("\"gen\"")) GenSelectorPress();
                        else if (line.Contains("\"gen_"))
                        {
                            System.Text.RegularExpressions.Match gm = System.Text.RegularExpressions.Regex.Match(line, "\"gen_([a-z0-9]+)\"");
                            if (gm.Success) { string gt = gm.Groups[1].Value; ThreadPool.QueueUserWorkItem(delegate { try { GenApply(gt == "no" ? "no" : Config.ParsePick(gt), 0); } catch (Exception ex) { Log.Error("Prueba de generacion", ex); } }); }
                        }
                    }
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-test-entrada\""))
                    {
                        // 16-11 (velocidad): solo pruebas sin consola (como switch2-pad): desde el HOME a 4K, la entrada en ese juego como si se
                        // hubiera confirmado (con las imagenes de prueba el HOME no se reconoce)
                        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line, "switch2-test-entrada\",\"([^\"]*)\"");
                        string g = m.Success ? m.Groups[1].Value : "";
                        if (g.Length > 0 && cfg.HomeNative && !homeBusy)
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                try { Log.Write("Prueba: entrada en \"" + g + "\" (switch2-test-entrada)"); EnterGame(g); }
                                catch (Exception ex) { Log.Error("Prueba de entrada", ex); }
                            });
                        else Log.Write("Prueba: switch2-test-entrada sin hacer (" + (g.Length == 0 ? "sin juego" : !cfg.HomeNative ? "no se esta en el HOME a 4K" : "cambiando") + ")");
                    }
                    // 18-11 (PASADAS): solo pruebas sin consola: salir como con "Salir" de la bandeja (deja los perfiles de NVIDIA como estaban)
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-test-salir\"") && cfg.TestSource.Length > 0)
                        OnUi(delegate { QuitRequested(); });
                    // 21-11 (1.2.1): solo pruebas sin consola (como switch2-test-entrada): de vuelta al HOME / menu de la consola a 4K nativo
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-test-home\"") && cfg.TestSource.Length > 0)
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            try { Log.Write("Prueba: vuelta al menu de la consola (switch2-test-home)"); EnterHomeNative(); }
                            catch (Exception ex) { Log.Error("Prueba de vuelta al menu", ex); }
                        });
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-switched\""))
                    {
                        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line, "switch2-switched\",\"([^\"]*)\"");
                        SwitchDone(m.Success ? m.Groups[1].Value : "");
                    }
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-standby-ok\""))
                    {
                        // 16-11 (motor_en_espera): el motor de ahora ya ha soltado la capturadora y se ha escondido: RunMpv sigue sin cerrarlo
                        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line, "switch2-standby-ok\",\"(\\d+)\"");
                        Process owner = readerMpv;
                        if (owner != null && owner == standbyAsked)
                        {
                            standbyGotMs = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
                            standbyGot = owner;
                        }
                    }
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-grabbed\""))
                    {
                        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line,
                            "switch2-grabbed\",\"((?:[^\"\\\\]|\\\\.)+)\",\"(\\d+)\",\"(\\d+)\",\"(\\d+)\",\"([^\"]*)\"");
                        if (!m.Success)
                        {
                            if (frozenPathNow != null) { frozenReply = null; frozenEvt.Set(); } // (la imagen congelada no ha salido)
                            grabBusy = false;
                            continue;
                        }
                        string gp = m.Groups[1].Value.Replace("\\\\", "\\");
                        int gw = int.Parse(m.Groups[2].Value), gh = int.Parse(m.Groups[3].Value), gs = int.Parse(m.Groups[4].Value);
                        string gf = m.Groups[5].Value;
                        // 10-11: la imagen para el telon congelado (GrabFrozenFromMpv), no una del HOME. Solo la de la peticion en
                        // curso; una tardia de otra anterior se borra
                        if (IsFrozenPath(gp))
                        {
                            if (string.Equals(gp, frozenPathNow, StringComparison.OrdinalIgnoreCase))
                            {
                                frozenReply = gw + " " + gh + " " + gs + " " + gf;
                                frozenEvt.Set();
                            }
                            else { try { File.Delete(gp); } catch { } }
                            continue;
                        }
                        Thread at = new Thread(delegate () { AnalyzeGrab(gp, gw, gh, gs, gf); });
                        at.IsBackground = true;
                        at.Priority = ThreadPriority.BelowNormal;
                        at.Start();
                    }
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-burst-done\""))
                    {
                        // 10-11: las imagenes del diagnostico del tramado (MaybeDitherBurst), a PNG en segundo plano
                        burstBusy = false;
                        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line,
                            "switch2-burst-done\",\"((?:[^\"\\\\]|\\\\.)+)\",\"(\\d+)\",\"(\\d+)\",\"(\\d+)\",\"(\\d+)\",\"([^\"]*)\"");
                        if (m.Success)
                        {
                            string bp = m.Groups[1].Value.Replace("\\\\", "\\");
                            int bn = int.Parse(m.Groups[2].Value), bw = int.Parse(m.Groups[3].Value), bh = int.Parse(m.Groups[4].Value),
                                bs = int.Parse(m.Groups[5].Value);
                            string bf = m.Groups[6].Value, bg = curGame;
                            Thread st = new Thread(delegate () { SaveDitherBurst(bp, bn, bw, bh, bs, bf, bg); });
                            st.IsBackground = true;
                            st.Priority = ThreadPriority.Lowest;
                            st.Start();
                        }
                    }
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-rate\""))
                    {
                        // Imagenes por segundo a la salida del filtro de repetidas (contadas en el script Lua cada
                        // 0,5 s) y si la ultima era nueva. estimated-vf-fps de mpv no sirve: con fuentes de prueba da 25
                        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                            line, "\"switch2-rate\",\"([0-9]+)\",\"(\\w+)\"");
                        if (m.Success)
                        {
                            vfFps = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                            vfNew = m.Groups[2].Value == "new";
                            RitmoDiag.MpvRitmo(m.Groups[2].Value);   // (1.2.3 RITMO2: la imagen quieta no es un tiron, src\Lanzador\Ritmo.cs)
                            // 15-11: el motor solo cuenta imagenes que le llegan: con ritmo, ya muestra imagen aunque su aviso no llegue
                            // (19:24:53: ni uno, y el motor se cerro a los 30 s "sin imagen"). Solo el de ahora, no uno que se cierra
                            if (vfFps > 0 && Config.EngineActive && readerMpv == mpv) { signalOn = true; videoSignal = true; }
                        }
                    }
                    // (23-11, 1.2.4 SINMPV: fuera lo que solo mandaban los scripts de mpv -switch2-diag, switch2-diag-hueco, switch2-diag-captura,
                    // switch2-grabbed-png, switch2-switch-failed-: el motor no los manda)
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-pintada\""))
                    {
                        // (1.2.4 arreglos, RELEVO: el motor de ahora ya ha presentado una imagen de verdad; WaitForSignal la espera)
                        Process rp = readerMpv;
                        if (rp != null && rp == mpv) enginePaintedPid = rp.Id;
                    }
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-signal\""))
                    {
                        // (15-11: solo el aviso del mpv / motor de ahora: uno tardio del que se cierra no vale para el nuevo)
                        if (readerMpv == mpv)
                        {
                            signalOn = true;
                            videoSignal = true;
                            signalHeard = true;
                        }
                    }
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-console\""))
                    {
                        bool on = line.Contains("\"on\"");
                        if (on != consoleOn)
                        {
                            Log.Write(on ? "Imagen de la consola: vigilancia de apagado activa"
                                         : "Pantalla No Signal: vigilancia de apagado en pausa");
                            // Se rearma desde cero cada vez que vuelve la consola
                            lock (linkSync) firstAdvance = DateTime.MinValue;
                        }
                        consoleOn = on;
                        if (on) consoleSeen = true;
                    }
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-poweroff\""))
                    {
                        // 09-11: el script de mpv ha confirmado la pantalla "No Signal" (switch2_power.lua, fast_seen): se cierra
                        // ya desde aqui (el script sale solo a los 0,3 s si nadie lo cierra)
                        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                            line, "\"switch2-poweroff\",\"([^\"]*)\"(?:,\"([^\"]*)\")?");
                        string why = m.Success ? m.Groups[1].Value : "";
                        if (!why.StartsWith("no-signal", StringComparison.Ordinal)) why = "no-signal-lua"; // (RecoverFromGpuReset)
                        Process owner = readerMpv;
                        if (userExit) { }
                        else if (owner != null && owner != mpv) Log.Write("mpv anterior: pantalla No Signal confirmada, no cuenta (ya hay otro mpv)");
                        else PowerOffNow(why, m.Success ? m.Groups[2].Value : "");
                    }
                    else if (line.Contains("\"client-message\"") && line.Contains("\"args\":[\"switch2-nosig\""))
                    {
                        // Lo que ve la deteccion rapida de "No Signal" del script (vista, confirmandose, falsa alarma)
                        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line, "\"switch2-nosig\",\"([^\"]*)\"");
                        if (m.Success) Log.Write("mpv: " + m.Groups[1].Value);
                        // (16-11 tarde, APAGADO: el motor propio ya espera solo, como el script de mpv: sin EngineNoSignalHold)
                    }
                }
            }
            catch (Exception) { }
        }

        // true si se ha detectado la consola apagada (y se ha cerrado mpv)
        // Proceso de mpv que mato CheckStall (por si no termina: ver RunMpv) y desde cuando no
        // llegaban fotogramas (para buscar un reinicio de la grafica en el registro de Windows)
        volatile int stallKilledPid = -1;
        DateTime stallAt = DateTime.MinValue;

        bool CheckStall(Process p)
        {
            if (!powerOffDetect) return false;
            // Ya cerrado (se esta relanzando): sin imagen, pero no es la consola. Medido: al cambiar
            // la captura para Zelda dejo el aviso de apagado y un corte de señal despues habria
            // cerrado la aplicacion en vez de volver a abrir mpv
            if (p.HasExited) return false;
            DateTime now = DateTime.Now;
            double stalled;
            // 16-11 (revision): el motor al que se le ha pedido que se quede en espera (o ya en espera) deja de dar imagenes a proposito:
            // si el paso a mpv + Magpie tarda mas de switchUntil, no es la consola apagada (se escribia "stall-launcher" y se mataba)
            if (p == standbyAsked || p == standbyEngine || p == standbyGot) { lock (linkSync) lastAdvance = now; return false; }
            lock (linkSync)
            {
                if (paused || !consoleOn || now < switchUntil) { lastAdvance = now; return false; }
                // Armado: imagen de la consola durante ArmSecs (con la consola apagada al
                // abrir la app no se arma y no se cierra nada)
                if (firstAdvance == DateTime.MinValue || (lastAdvance - firstAdvance).TotalSeconds < ArmSecs)
                    return false;
                stalled = (now - lastAdvance).TotalMilliseconds;
                if (stalled < cfg.PowerOffStallMs) { StallResumed(p, now); return false; }
            }
            // 20-11 (CIERRES, src\Cierres.cs): antes de tomarlo por la consola apagada, si el motor dice que la grafica esta parada, la
            // capturadora le sigue mandando imagenes o la consola sigue sonando, no lo es: se espera (y si dura, se reabre la imagen)
            int veto = StallVeto(p, now, stalled);
            if (veto == 1) return false;
            if (veto == 2) return true;
            Log.Write("No llegan fotogramas desde hace " + (int)stalled + " ms: la consola se ha apagado / esta en reposo");
            try { File.WriteAllText(Paths.FlagFile, "stall-launcher " + now.ToString("yyyy-MM-dd HH:mm:ss")); }
            catch (Exception ex) { Log.Error("Escribiendo flag", ex); }
            try { p.Kill(); } catch (Exception ex) { Log.Error("Cerrando mpv", ex); }
            stallAt = now.AddMilliseconds(-stalled);
            stallKilledPid = p.Id;
            return true;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        // Espera a que llegue imagen de la consola mostrando "Enciende la consola" en la
        // pantalla de carga. Devuelve false si mpv se cierra antes (o se pide salir).
        // Se conservan entre relanzamientos de mpv
        DateTime waitStart = DateTime.MinValue;
        bool waitAsked;

        // engineMaxSecs / fast (16-11, motor_en_espera): el motor despertado tiene que dar imagen en mucho menos (si no, se abre otro) y se
        // le mira mas a menudo; el tiempo va desde que se lanzo o se desperto (imageStart: el StartTime de uno en espera es de hace rato)
        bool WaitForSignal(Process p, int engineMaxSecs = 30, bool fast = false)
        {
            // 15-11: "ya muestra imagen" es su aviso de imagen (switch2-signal del script de mpv o del motor; el motor, tambien su ritmo
            // de imagenes), no el sonido de la consola: con el sonido el telon se quitaba antes de la primera imagen (el motor tarda
            // ~3 s en arrancar; mpv ~1 s: 13:01:09 y 19:25:57, tras volver del motor). Sin script Lua, el sonido, como antes; sin
            // script ni medidor de audio nadie puede avisar: no se espera
            bool byVideo = true;   // (23-11, 1.2.4 SINMPV: siempre el motor, que avisa de la imagen; antes mpv solo si tenia scripts\ce_power.lua)
            if (!byVideo && !cfg.ConsoleAudio && !cfg.StartStreamingCenter) return true;
            if (waitStart == DateTime.MinValue) waitStart = DateTime.Now;
            while (!(byVideo ? videoSignal : signalOn))
            {
                if (p.HasExited || userExit) return false;
                // 15-11: el motor propio sin imagen en 30 s (colgado al arrancar: NVIDIA, la capturadora): se cierra y cuenta como fallo
                // (la capturadora sin consola tambien da imagen: su pantalla No Signal)
                if (Config.EngineActive && (DateTime.Now - imageStart).TotalSeconds > engineMaxSecs)
                {
                    Log.Write("El motor propio no da imagen en " + engineMaxSecs + " s: se cierra");
                    try { p.Kill(); } catch { }
                    return false;
                }
                // 21-11 (1.2.1, src\Lanzador\CapturaSegura.cs): sin imagen tras cambiar el formato de la capturadora con la consola encendida:
                // ese formato no lo da; se cierra y RunMpv lo vuelve a abrir al momento con otro (antes: "esperando a que se encienda" para siempre)
                if (CapSinImagen(p))
                {
                    try { p.Kill(); } catch { }
                    return false;
                }
                // Margen de 3 s para no mostrar el aviso si la consola ya esta encendida
                // (el motor propio tarda ~4 s en dar su primera imagen: NVIDIA prepara DLSS y la generacion)
                // (21-11, 1.2.1: mientras se vigila un formato nuevo con la consola encendida, no: no es que este apagada)
                // (22-11, 1.2.2: ni con el motor aun arrancando, sin abrir la capturadora: la primera vez compila sus shaders 20-30 s y salia
                // "Enciende la consola" con la consola encendida)
                if (!waitAsked && capVigilado == null && !CapMotorArrancando(p) && (DateTime.Now - waitStart).TotalSeconds >= (Config.EngineActive ? 6 : 3))
                {
                    waitAsked = true;
                    Log.Write("Sin senal de la consola: esperando a que se encienda");
                    OnUi(delegate
                    {
                        if (splash == null) return;
                        splash.SetStatus(Consolas.TextoEnciende);
                        splash.SetHint("Pulsa Esc para cancelar");
                    });
                }
                // 18-11 (CONSOLAS): con una PlayStation, a los 12 s el texto pequeño recuerda el HDCP (con el, la capturadora no da imagen)
                if (waitAsked)
                {
                    string ph = PistaEsperando((DateTime.Now - waitStart).TotalSeconds);
                    if (ph != waitHint) { waitHint = ph; OnUi(delegate { if (splash != null) splash.SetHint(ph); }); }
                }
                // Si la ventana de mpv se queda el foco se intenta devolver a la pantalla de
                // carga (solo si es de mpv: no se quita el foco al menu de la bandeja, etc.)
                if (waitAsked)
                {
                    uint fgPid;
                    GetWindowThreadProcessId(GetForegroundWindow(), out fgPid);
                    if (fgPid == (uint)p.Id)
                        OnUi(delegate { if (splash != null) splash.Activate(); });
                }
                // (16-11, velocidad: cada 50 ms y no 200 hasta pedir que se encienda la consola: el telon o la pantalla de carga se quitaban
                // hasta 0,2 s tarde)
                Thread.Sleep(fast ? 40 : waitAsked ? 1000 : 50);
            }
            Log.Write("El motor ya muestra imagen (consola o pantalla No Signal)");
            PaintWait(p);   // (1.2.4 arreglos, RELEVO: el motor, hasta su primera imagen pintada: el telon no se quita sobre negro)
            if (cfg.PerformanceBoost) Perf.GpuHigh(p, "mpv");
            // 16-11 (revision): waitAsked no se quitaba nunca: tras una espera larga (la app abierta con la consola apagada), cada espera
            // despues miraba cada 1 s (hasta 1 s mas de negro en cada relanzamiento) y no cada 0,2 s (o 0,04 s al despertar el motor)
            bool asked = waitAsked;
            waitAsked = false;
            if (asked)
                OnUi(delegate
                {
                    if (splash == null) return;
                    splash.SetStatus(null);
                    splash.SetHint(null);
                });
            return true;
        }

        // 16-11: mpv al primer plano en cuanto tiene ventana, con Magpie ya abierto (RunMpv): su escalado automatico salta ya y crea los
        // modelos de NVIDIA (~8 s) mientras mpv abre la capturadora (~1,2 s hasta su primera imagen) en vez de despues. Probado sin consola
        // (16-11, 4 vueltas alternas con una fuente de prueba): Magpie presenta igual de bien y antes. Debajo del telon: no se ve nada.
        // (1.2.4 ronda 6, QUITAFG, fase 2: EarlyScale era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: WaitForScalingCore era de Magpie; fuera)

        // (con lock (cmdQueue)) tras un "vf set" / "vf clr": nada mas que volver a poner (con LS habia un filtro de arranque lento)
        void ReAddThrottleLocked() { }

        string pendingWarnOsd;          // aviso para cuando se quite el telon (Magpie no escala)

        // El aviso, ya sin el telon (mas tiempo que los del mando: no lo ha pedido nadie)
        bool ShowPendingWarn()
        {
            string w = pendingWarnOsd;
            if (w == null) return false;
            pendingWarnOsd = null;
            lock (cmdQueue) cmdQueue.Add(OsdCmd(w, "6000", true)); // (punto rojo: algo va mal)
            return true;
        }

        // (1.2.4 ronda 6, QUITAFG, fase 2: WaitLsSettled era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: FinishLsStart era de Magpie; fuera)

        static string LogSince(string path, long start)
        {
            try
            {
                if (!File.Exists(path)) return "";
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                      FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < start) start = 0; // el log se ha rotado
                    fs.Seek(start, SeekOrigin.Begin);
                    using (StreamReader r = new StreamReader(fs)) return r.ReadToEnd();
                }
            }
            catch { return ""; }
        }

        // (1.2.4 ronda 6, QUITAFG, fase 2: EnsureScaled era de Magpie; fuera)

        static long LogLength(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch { return 0; }
        }

        static bool LogHasSince(string path, long start, string text)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                      FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < start) start = 0; // el log se ha rotado
                    fs.Seek(start, SeekOrigin.Begin);
                    using (StreamReader r = new StreamReader(fs))
                        return r.ReadToEnd().Contains(text);
                }
            }
            catch { return false; }
        }

        // Reinicios de la grafica en esta sesion: con tantos seguidos se deja de insistir
        int gpuResets;
        const int MaxGpuResets = 3;

        // Tras un corte de imagen: si Windows ha reiniciado el driver de la grafica (TDR) no es la
        // consola apagada. Se tapa la pantalla (si no, al cerrar LS quedaba la ventana de mpv
        // colgada, pequeña en una esquina), se aligera la GPU hasta cerrar la app (el reinicio
        // llega con la tarjeta al limite) y se vuelven a abrir LS y mpv. true = relanzar mpv
        bool RecoverFromGpuReset(Process p)
        {
            // La pantalla "No Signal" de la capturadora es la consola apagada (un reinicio de la grafica congela la imagen,
            // no la cambia por esa pantalla): sin esperar al aviso del driver (eran hasta 6 s mas en cada apagado)
            string reason = LoadText(Paths.FlagFile) ?? "";
            if (reason.StartsWith("no-signal", StringComparison.Ordinal)) { stallAt = DateTime.MinValue; return false; }
            DateTime cut = stallAt != DateTime.MinValue ? stallAt : DateTime.Now;
            stallAt = DateTime.MinValue;
            // El aviso del driver llega hasta ~3 s despues del corte: se espera hasta 6 s (20-11, CIERRES: como poco 2 s desde ahora; con el
            // motor diciendo que la grafica iba bien, 1,5 s desde el corte: GpuResetDeadline, src\Cierres.cs)
            DateTime tdr = GpuReset.Since(cut.AddSeconds(-10));
            DateTime tdrUntil = GpuResetDeadline(cut);
            while (tdr == DateTime.MinValue && DateTime.Now < tdrUntil && !userExit)
            {
                Thread.Sleep(500);
                tdr = GpuReset.Since(cut.AddSeconds(-10));
            }
            if (tdr == DateTime.MinValue || userExit) return false;
            gpuResets++;
            if (gpuResets > MaxGpuResets)
            {
                Log.Write("La grafica se ha reiniciado otra vez (" + tdr.ToString("HH:mm:ss") + ", " + gpuResets +
                          " en esta sesion): se cierra la aplicacion");
                return false;
            }
            Log.Write("La grafica se ha reiniciado (driver de NVIDIA a las " + tdr.ToString("HH:mm:ss") +
                      "): no es la consola apagada; se recupera la imagen");
            ShowCurtain("Recuperando la imagen…", "La tarjeta gráfica se ha reiniciado", true);
            Tip("Windows ha reiniciado el driver de la tarjeta gráfica. Se recupera la imagen con menos carga " +
                "en la GPU; si no vuelve en un minuto, reinicia el PC.");
            // Menos carga hasta cerrar la app: el modelo de DLSS 5 corre en menos imagenes
            cfg.Dlss5EveryExtra++;
            Log.Write("Hasta cerrar la app: el modelo de DLSS 5 corre en 1 de cada " + cfg.Dlss5EveryNow + " imagenes");
            stallKilledPid = -1;
            // El mpv colgado tiene la capturadora: se le da tiempo a soltarla
            if (!p.HasExited && !p.WaitForExit(30000))
                Log.Write("El mpv colgado (pid " + p.Id + ") sigue sin terminar: se abre otro igualmente");
            return !userExit;
        }

        // ---- Modo banco (Config.BenchSecs) ----
        bool benchStarted;

        static double Num(System.Text.RegularExpressions.Match m, int g)
        {
            return double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
        }

        void BenchRun(Process p)
        {
            try
            {
                // Reaperturas como las de un boton del mando (forzadas: la config no cambia), para medir el telon de cada una
                for (int i = 0; i < cfg.BenchReopens && !p.HasExited && !userExit; i++)
                {
                    Thread.Sleep(3000);
                    Stopwatch rs = Stopwatch.StartNew();
                    while ((passesBusy || !lsReady) && rs.ElapsedMilliseconds < 30000) Thread.Sleep(50);
                    if (cfg.BenchEngine) { SetDlss5State(!dlss5Shown); RestartLs(dlss5Shown ? "Banco: DLSS 5 activado" : "Banco: DLSS 5 desactivado", null); }
                    else RestartLs("Banco: reapertura " + (i + 1), null, true);
                    Thread.Sleep(300);
                    rs = Stopwatch.StartNew();
                    while ((passesBusy || !lsReady) && rs.ElapsedMilliseconds < 40000) Thread.Sleep(50);
                }
                Thread.Sleep(cfg.BenchWarmSecs * 1000);
                long start = LogLength(Paths.MagpieLog);
                double vfSum = 0; int vfN = 0;
                Stopwatch sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < cfg.BenchSecs * 1000L && !p.HasExited && !userExit)
                {
                    Thread.Sleep(500);
                    vfSum += vfFps; vfN++;
                }
                string text = LogSince(Paths.MagpieLog, start);
                List<double> cap = new List<double>(), shown = new List<double>();
                double p50 = 0, p95 = 0, p99 = 0, worst = 0, outWorst = 0;
                int windows = 0, errors = 0, slow95 = 0;
                string pct = "";
                double target = cfg.CapMode ? 33.37 : 16.68;
                foreach (string raw in text.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    System.Text.RegularExpressions.Match m;
                    if ((m = System.Text.RegularExpressions.Regex.Match(line, @"Output cadence .*?captureFps=([0-9.]+).*?captureIntervalP50Ms=([0-9.]+) captureIntervalP95Ms=([0-9.]+) captureIntervalP99Ms=([0-9.]+) captureIntervalMaxMs=([0-9.]+) outputIntervalMaxMs=([0-9.]+)")).Success)
                    {
                        windows++;
                        cap.Add(Num(m, 1));
                        p50 = Math.Max(p50, Num(m, 2)); p95 = Math.Max(p95, Num(m, 3)); p99 = Math.Max(p99, Num(m, 4));
                        worst = Math.Max(worst, Num(m, 5)); outWorst = Math.Max(outWorst, Num(m, 6));
                        if (Num(m, 3) > target * 1.25) slow95++;
                    }
                    else if ((m = System.Text.RegularExpressions.Regex.Match(line, @"presentation ring: captured=([0-9.]+) FPS, queued=([0-9.]+) FPS")).Success)
                        shown.Add(Num(m, 2));
                    else if ((m = System.Text.RegularExpressions.Regex.Match(line, @"inputResolutionPercent=(\d+)")).Success)
                        pct += (pct.Length > 0 ? ">" : "") + m.Groups[1].Value;
                    else if ((m = System.Text.RegularExpressions.Regex.Match(line, @"resolution prepare: full=(\d+)")).Success)
                        pct += (pct.Length > 0 ? ">" : "") + m.Groups[1].Value;
                    if (line.Contains("|error|")) errors++;
                }
                Func<List<double>, double> avg = l => { if (l.Count == 0) return 0; double s = 0; foreach (double x in l) s += x; return Math.Round(s / l.Count, 2); };
                Func<List<double>, double> min = l => { if (l.Count == 0) return 0; double s = double.MaxValue; foreach (double x in l) s = Math.Min(s, x); return s; };
                string res = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                    "\tetiqueta=" + cfg.BenchLabel +
                    "\treales=" + Ms(avg(cap)) + "\treales_min=" + Ms(min(cap)) +
                    "\tmostradas=" + Ms(avg(shown)) +
                    "\tp50=" + Ms(p50) + "\tp95=" + Ms(p95) + "\tp99=" + Ms(p99) + "\tpeor=" + Ms(worst) + "\tsalida_peor=" + Ms(outWorst) +
                    "\tventanas=" + windows + "\tventanas_lentas=" + slow95 +
                    "\tmpv=" + Ms(vfN > 0 ? Math.Round(vfSum / vfN, 1) : 0) +
                    "\tdlssnr_pct=" + (pct.Length > 0 ? pct : "-") + "\terrores=" + errors +
                    "\tperfil=" + cfg.ProfileText;
                File.AppendAllText(Path.Combine(Paths.Datos, @"games\bench.txt"), res + "\r\n", new UTF8Encoding(false));
                Log.Write("Banco: " + res);
            }
            catch (Exception ex) { Log.Error("Banco", ex); }
            OnUi(QuitRequested);
        }

        // Lanza mpv y espera a que termine. Devuelve el codigo de salida (int.MinValue si fallo).
        int RunMpv()
        {
            signalOn = false;
            bool first = true;
            try
            {
                long logStart = LogLength(Paths.MagpieLog);
              relaunch: // otra vez con otro tamaño de captura
                // El aviso de apagado es del mpv anterior: si se relanza, la consola sigue encendida
                try { if (File.Exists(Paths.FlagFile)) File.Delete(Paths.FlagFile); }
                catch (Exception ex) { Log.Error("No se pudo borrar " + Paths.FlagFile, ex); }
                lsReady = false;
                logStart = LogLength(Paths.MagpieLog);
                // Motor propio o mpv + Magpie: lo que toca ahora (DLSS 5 apagado o no). Lo pendiente de la imagen anterior no cuenta
                PrepareCfg();
                Config.EngineActive = cfg.EngineNow;
                relaunchEngine = false;
                quitForRelaunch = false;
                quitToStandby = false;
                if (!first) pendingPassesRestart = false; // (la imagen se abre ya con todo: nada pendiente)
                Process p;
                while (true)
                {
                    Progress(0.55);
                    // Tuberia IPC propia de este mpv (el anterior puede seguir colgado con la suya)
                    MpvConst.NextIpc();
                    string ipc = MpvConst.IpcName;
                    vfDedupe = DedupeOn(cfg, !cfg.FgOff); // (lo que lleva este mpv al abrirse)
                    bool eng = Config.EngineActive;
                    // 23-11 (1.2.4, SINMPV): sin mpv la imagen la hace siempre el motor (lo que antes iba por mpv + Magpie va por el, con lo que
                    // sepa hacer; sin su exe, Worker ya se ha parado)
                    if (!eng)
                    {
                        Log.Write("Sin mpv: la imagen por el motor propio (antes: mpv + Magpie" + (cfg.PorQueReserva.Length > 0 ? ", " + cfg.PorQueReserva : "") + ")");
                        Config.EngineActive = true;
                        eng = true;
                    }
                    // 16-11 (motor_en_espera): con un motor en espera se despierta (~0,5 s) en vez de abrir otro (~4,5 s); si no hay o no vale,
                    // como siempre (y nunca dos motores a la vez: el de espera fuera antes de abrir otro)
                    // 16-11 (dlss5_sin_telon): el motor ya despierto por el relevo (DLSS 5 apagado sin telon) se toma tal cual
                    string adoptIpc = null;
                    bool adoptNoImage = false;
                    Process adopted = eng ? TakeRelevoEngine(out adoptIpc, out adoptNoImage) : null;
                    // (1.2.4 arreglos, RELEVO: tras una caida del motor, el motor en espera aunque no valga -sin generacion-, de puente: imagen en
                    // ~0,5 s en vez del negro de un arranque; luego el relevo al bueno. RelevoMotor.cs CrashWaitMs)
                    bool puenteCaida = eng && adopted == null && crashBridgeNext && standbyEngine != null;
                    crashBridgeNext = false;
                    if (puenteCaida) Log.Write("Tras la caida: el motor en espera (pid " + standbyEngine.Id + ") de puente, sin generacion, mientras se abre el que toca (" + StandbyIdentityDiff() + ")");
                    // 17-11 16:55: un motor en espera abierto con otro Smooth Motion no vale (el driver no lo cambia en marcha): uno nuevo
                    if (eng && !puenteCaida && adopted == null && standbyEngine != null && (cfg.EngineSmoothAny || standbySmooth > 0) &&
                        standbySmooth != cfg.EngineSmoothMode)
                        KillStandby("Smooth Motion: el motor en espera se abrio " + (standbySmooth > 0 ? "con el (" + standbySmooth + ")" : "sin el"));
                    // 20-11 (LATENCIA5): y uno abierto con otro refresco (fijo en el 60 con DLSS FG / VRR) tampoco
                    if (eng && !puenteCaida && adopted == null && standbyEngine != null && Config.EngineFixedOf(standbyEngine) >= 0 &&
                        Config.EngineFixedOf(standbyEngine) != (cfg.EngineSmoothMode == 0 && cfg.EngineFixedWanted ? 1 : 0))
                        KillStandby("refresco: el motor en espera se abrio " + (Config.EngineFixedOf(standbyEngine) > 0 ? "con el refresco fijo" : "con VRR"));
                    // 21-11 (1.0.9): y uno abierto con otra generacion de motor (FSR 3 FG / la de siempre): la cadena no se cambia en marcha
                    if (eng && !puenteCaida && adopted == null && standbyEngine != null && Config.EngineFfxOf(standbyEngine) >= 0 &&
                        !Config.ChainOk(Config.EngineFfxOf(standbyEngine), cfg.EngineFgChain))   // (1.2.4 arreglos, RELEVO: ChainOk)
                        KillStandby("generacion: el motor en espera se abrio con otra cadena (" + Config.EngineFfxOf(standbyEngine) + ", ahora " + cfg.EngineFgChain + ": 1 FSR 3 FG, 3 DLSS FG x3 / x4 con PrismaFg.dll, 2x / 3x / 4x XeSS FG x2 / x3 / x4, +1 con DLSS SR)");
                    Process woke = adopted ?? (eng ? StandbyForWake() : null);
                    if (eng && woke == null) KillStandby("se abre otro motor");
                    if (woke != null) ipc = adopted != null ? adoptIpc : standbyIpc;
                    CapLanzamiento(eng, woke != null); // 21-11 (1.2.1): ¿formato nuevo con la consola encendida? se vigila (CapturaSegura.cs)
                    ProcessStartInfo psi = null;
                    if (woke == null)
                    {
                        string margs = EngineArgs(cfg, ipc);   // (23-11, SINMPV: siempre el motor)
                        psi = new ProcessStartInfo(cfg.EnginePath, margs);
                        psi.UseShellExecute = false;
                        psi.WorkingDirectory = Path.GetDirectoryName(cfg.EnginePath);
                        Log.Write("Lanzando el motor propio: " + psi.FileName + " " + psi.Arguments);
                    }
                    engineSent = eng ? EngineLive(cfg) : "";
                    mpvScaleSent = cfg.MpvScaleNow; // (el que lleva en sus argumentos)
                    lock (linkSync) { lastPos = double.NaN; firstAdvance = DateTime.MinValue; }
                    // 15-11: el aviso de imagen es de cada lanzamiento (tambien al reintentar aqui dentro: tras el motor, signalOn seguia
                    // puesto por el sonido de la consola y mpv "ya mostraba imagen" al lanzarse, 19:25:57.900 -> .921). 16-11: tambien al
                    // despertar uno en espera (sus avisos de imagen vuelven a empezar)
                    videoSignal = false;
                    signalHeard = false;
                    signalOn = false;
                    enginePaintedPid = -1;   // (1.2.4 arreglos, RELEVO: switch2-pintada de esta imagen)
                    Stopwatch wakeSw = null;
                    if (woke != null)
                    {
                        // Ya no esta en espera: es la imagen, como un motor normal (su tuberia la usa el enlace de siempre; sin el job)
                        // (1.2.4 arreglos, RELEVO: el del relevo no era el de espera: el de espera ahora es el motor que se fue -RelevoOldToStandby-,
                        // y no se suelta: antes se perdia aqui y a los 3 s QuitOldMpv lo cerraba a la fuerza)
                        if (adopted == null || standbyEngine == woke)
                        {
                            standbyEngine = null;
                            standbyReady = false;
                            CloseStandbyPipe();
                        }
                        if (standbyAtExit == woke) standbyAtExit = null;
                        standbyAsked = null;
                        standbyGot = null;
                        EngineJob.Hold(woke, false);
                        p = woke;
                        mpv = p;
                        // (1.2.4 arreglos, RELEVO: el del relevo con el suyo -el que se fue ya puede estar en espera con el de antes-)
                        Config.EngineSmoothRunning = adopted != null && relevoAdoptSmooth >= 0 ? relevoAdoptSmooth : standbySmooth;   // 17-11 16:55
                        wakeSw = Stopwatch.StartNew();
                        if (adopted == null) SendWake(p);
                        else
                        {
                            // (16-11, sin telon: ya despierto y delante; sin imagen aun -por el telon de siempre-, la capturadora otra vez ya:
                            // mientras mpv la tenia pudo no abrirla y su siguiente intento podia tardar hasta 5 s)
                            Log.Write("Sin telon: el motor despertado (pid " + p.Id + ") pasa a ser la imagen" + (adoptNoImage ? " (sin imagen aun: se le pide reabrir la capturadora)" : ""));
                            if (adoptNoImage) lock (cmdQueue) cmdQueue.Add(JsonCmd("script-message", "switch2-reabrir"));
                        }
                    }
                    else
                    {
                        p = Process.Start(psi);
                        mpv = p;
                        EngineJob.Hold(p, true);   // (22-11, 1.2.2: si la app muere, Windows lo cierra; ver EngineJob)
                        if (first && !eng) StandbyAlAbrir(); // (22-11, 1.2.2: el motor en espera ya, a la vez que mpv; src\Lanzador\TiemposCarga.cs)
                        if (eng) { Config.NoteEngineFixed(p); Config.NoteEngineFfx(p); }   // 20-11 (LATENCIA5)
                        if (eng) Config.EngineSmoothRunning = Config.EngineSmoothLaunched;   // 17-11 16:55 (17:15: con mpv se queda el del ultimo motor: el que pasa a espera tras el puente lo necesita; antes -1 y al motor de Smooth Motion se le mandaba fg=1)
                    }
                    imageStart = DateTime.Now;
                    curIpc = ipc;
                    edgeShaderSet = cfg.HomeNative ? "" : cfg.EdgeShaderNow; // (lo que lleva en sus argumentos, AddCommonMpvArgs)
                    if (cfg.PerformanceBoost) Perf.Boost(p, "mpv");
                    if (userExit) // salir pedido justo mientras se lanzaba
                    {
                        try { p.Kill(); } catch { }
                        return int.MinValue;
                    }
                    Process started = p; // copia para el hilo (p cambia al relanzar)
                    bool linkNow = woke != null; // (16-11: el despertado ya tiene su tuberia: el enlace al momento)
                    // Audio de Streaming Center + vigilancia de fotogramas
                    Thread link = new Thread(delegate () { MpvLinkLoop(started, ipc, linkNow); });
                    link.IsBackground = true;
                    link.Start();
                    Thread guard = new Thread(delegate () { LsFocusGuard(started); });
                    guard.IsBackground = true;
                    guard.Start();
                    if (WaitForSignal(p, woke != null ? 4 : eng ? 60 : 30, woke != null))   // (1.2.4 ronda 6, INTEGRA6: el motor, 60 s: la primera vez, sin la cache de shaders y con Enhanced FG, pasa de 30 -sesion c1 del 23-11-)
                    {
                        if (eng) engineFails = 0;
                        if (wakeSw != null) Log.Write("Motor en espera: despierto en " + wakeSw.ElapsedMilliseconds + " ms (pid " + p.Id + ")");
                        CapImagen(); // 21-11 (1.2.1): este formato da imagen (si era un repuesto, se queda; CapturaSegura.cs)
                        break;
                    }
                    // (1.2.02) sin tope: un mpv / motor colgado en el driver de la capturadora o de la grafica dejaba el hilo parado para siempre
                    if (!p.WaitForExit(8000))
                    {
                        try { p.Kill(); } catch { }
                        if (!p.WaitForExit(5000)) Log.Write("La imagen (pid " + p.Id + ") no se cierra (colgada en el driver): se sigue sin esperarla");
                    }
                    int salida = -1;
                    try { if (p.HasExited) salida = p.ExitCode; } catch { }
                    if (userExit) return salida;
                    // 16-11 (dlss5_sin_telon): el mpv nuevo se ha cerrado sin imagen con el motor del relevo aun encima: deja paso, con el telon
                    // de siempre mientras se sigue

                    // 21-11 (1.2.1, src\Lanzador\CapturaSegura.cs): sin imagen por el formato de la capturadora (cerrado aqui o solo, con 2): otra
                    // vez ya con el repuesto, sin contarlo como fallo ni esperar
                    if (CapFallo(eng, woke != null, salida)) continue;
                    // 16-11 (motor_en_espera): el despertado no da imagen a tiempo: otro como siempre (sin contarlo como fallo ni esperar 2 s)
                    if (woke != null)
                    {
                        Log.Write("Motor en espera: no da imagen en 4 s tras despertarlo: se abre otro como siempre");
                        DropWake();
                        continue;
                    }
                    // 15-11: el motor propio no llega a dar imagen 2 veces: el resto de la sesion, mpv sin el motor (1.2.4 ronda 6: sin Magpie)
                    // (23-11, 1.2.4 SINMPV: sin mpv no hay otro camino: se apunta y se sigue con los reintentos de abajo -2 s, a las 5 cada 10 s y a
                    // las 20 el aviso, que entonces dice que es el motor-)
                    if (eng && salida != 0 && ++engineFails == 2)
                        Log.Write("El motor propio no arranca (codigo " + salida + ", " + engineFails + " veces; ver data\\logs\\engine.log): se vuelve a intentar (sin mpv no hay otro camino)");
                    // Cerrado por el usuario (codigo 0): se sale como siempre. 20-11 (CIERRES): el motor con 0 sin que el usuario saliera
                    // (03:54:38, el relanzado tras un reinicio de la grafica) se vuelve a abrir (src\Cierres.cs, EngineZeroExitRetry)
                    if (salida == 0) { if (EngineZeroExitRetry(eng, p)) continue; return 0; }
                    // Sin imagen mpv puede fallar al abrir la capturadora: se reintenta. (1.2.02) sin tope era un bucle cada 2 s para siempre
                    // con la pantalla de carga (capturadora ocupada por OBS / RECentral, desenchufada...): a las 5 seguidas cada 10 s y a las 20
                    // se avisa y se sale
                    mpvFails++;
                    string otros = mpvFails == 5 || mpvFails >= 20 ? ProgramasConCaptura() : "";
                    if (mpvFails >= 20)
                    {
                        if (engineFails >= 2)
                        {
                            Fatal(Deteccion.Tx.T("El motor de imagen no arranca (" + mpvFails + " intentos; codigo " + salida + "). ¿Tu gráfica tiene Direct3D 12 y su driver al día? Mira data\\logs\\engine.log.",
                                                 "The image engine does not start (" + mpvFails + " tries; code " + salida + "). Does your GPU have Direct3D 12 and an up-to-date driver? See data\\logs\\engine.log."));
                            return int.MinValue;
                        }
                        Fatal(Deteccion.Tx.T("No se puede abrir la capturadora (" + mpvFails + " intentos). " +
                                             (otros.Length > 0 ? "Ciérralo y vuelve a abrir la app: " + otros + " puede estar usándola." : "¿Está conectada? ¿La usa otro programa?"),
                                             "The capture card can't be opened (" + mpvFails + " tries). " +
                                             (otros.Length > 0 ? "Close it and open the app again: " + otros + " may be using it." : "Is it connected? Is another program using it?")));
                        return int.MinValue;
                    }
                    int pausa = mpvFails >= 5 ? 10000 : 2000;
                    Log.Write("El motor se ha cerrado sin mostrar imagen (codigo " + salida + ", " + mpvFails + " seguidas); se relanza en " + pausa / 1000 + " s" +
                              (otros.Length > 0 ? " (abiertos que pueden tener la capturadora: " + otros + ")" : ""));
                    Thread.Sleep(pausa);
                    if (userExit) return int.MinValue;
                }
                mpvFails = 0;
                // La prioridad de GPU solo se puede poner cuando mpv ya ha abierto la GPU
                if (cfg.PerformanceBoost) Perf.Boost(p, "mpv");
                bool relanzado = !first; // 21-11 (1.2.1): para el puente al reabrir mpv y Magpie (src\SinTelon.cs)
                first = false;
                Process watched = p;
                Thread watch = new Thread(delegate () { WatchLoop(watched); });
                watch.IsBackground = true;
                watch.Start();
                Progress(0.8);
                if (Config.EngineActive)
                {
                    Log.Write("Motor propio en marcha (" + EngineLive(cfg) + ")");
                    // El primer plano al motor al irse la pantalla de carga (la generacion de NVIDIA lo necesita, y el mando
                    // solo cambia cosas con la imagen delante)
                    Process ep = p;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Thread.Sleep(1100);
                        try { ep.Refresh(); if (!ep.HasExited && GetForegroundWindow() != ep.MainWindowHandle) ForceForeground(ep.MainWindowHandle); }
                        catch { }
                    });
                }
                else Log.Write(cfg.HomeNative ? "HOME a 4K nativo con mpv" : "mpv sin el motor: la imagen de la capturadora tal cual");   // (1.2.4 ronda 6, QUITAFG: sin Magpie)
                lsReady = true;
                HideCurtain(); // si mpv se relanzo en plena partida para cambiar la captura
                ShowPendingWarn();
                CapMostrarAviso(); // 21-11 (1.2.1): "La capturadora no da ... · se usa ..." si hizo falta un repuesto (CapturaSegura.cs)
                Stopwatch cs = captureSw;
                if (cs != null)
                {
                    captureSw = null;
                    Log.Write("Captura a " + SizeName(cfg.VideoSize) + " lista: " + cs.ElapsedMilliseconds + " ms desde que se pidio");
                }
                OnUi(FadeSplash);
                if (cfg.BenchSecs > 0 && !benchStarted) { benchStarted = true; Process bp = p; Thread bt = new Thread(delegate () { BenchRun(bp); }); bt.IsBackground = true; bt.Start(); }
                // Matado por falta de imagen, mpv puede quedarse colgado en el driver de la
                // capturadora (medido: la app esperaba horas y ese mpv seguia con la tuberia IPC):
                // a los 5 s se sigue como consola apagada sin esperarlo
                Stopwatch killWait = null;
                bool hung = false, toStandby = false, standbyGaveUp = false, aside = false;
                while (!p.WaitForExit(standbyAsked == p || relevoAside == p ? 30 : 250))
                {
                    // 16-11 (motor_en_espera): el motor ha soltado la capturadora y se ha escondido: no se espera a que salga (sigue en espera).
                    // Sin respuesta en 1,5 s, cerrado como siempre
                    if (standbyGot == p && !userExit && powerOffPid != p.Id) { toStandby = true; break; }
                    // 16-11 (dlss5_sin_telon): DLSS 5 encendido sin telon: el motor sigue jugando aparte (encima de todo) mientras se abren mpv y
                    // Magpie; pasa a la espera cuando Magpie presenta (FinishRelevoToMagpie)
                    if (relevoAside == p && !userExit && powerOffPid != p.Id) { aside = true; break; }
                    if (standbyAsked == p && !standbyGaveUp && standbyAskedSw != null && standbyAskedSw.ElapsedMilliseconds > 1500)
                    {
                        standbyGaveUp = true;
                        Log.Write("Motor en espera: el motor no contesta a switch2-standby en 1,5 s: se cierra como siempre");
                        try { p.Kill(); } catch { }
                    }
                    // 09-11: cerrado por la consola apagada (PowerOffNow, pantalla "No Signal"): como mucho ~1 s
                    bool off = powerOffPid == p.Id;
                    if (stallKilledPid != p.Id && hangKilledPid != p.Id && !off) continue; // (20-11, CIERRES: hangKilledPid, src\Cierres.cs)
                    if (killWait == null) killWait = Stopwatch.StartNew();
                    if (killWait.ElapsedMilliseconds < (off || stallOffPid == p.Id ? 1000 : 5000)) continue; // (20-11, CIERRES: stallOffPid, src\Cierres.cs)
                    Log.Write("mpv no termina tras cerrarlo (capturadora colgada): se sigue sin esperarlo");
                    hung = true;
                    break;
                }
                if (standbyAsked == p) standbyAsked = null;
                if (aside)
                {
                    // 16-11 (dlss5_sin_telon): aparte de 'mpv' (vivo, jugando y encima de todo); 'mpv' pasa a ser el mpv que se lanza ahora
                    relevoAside = null;
                    Log.Write("Sin telon: el motor (pid " + p.Id + ") sigue jugando encima de todo mientras se abren mpv y Magpie");
                    if (relaunchSize == null && !relaunchEngine) relaunchEngine = true;
                    if (userExit) { KillRelevo("salir"); return 0; }
                }
                if (toStandby)
                {
                    // 16-11 (motor_en_espera): aparte de 'mpv' (vivo y escondido); 'mpv' pasa a ser lo que se lanza ahora
                    int ms = standbyGotMs >= 0 ? standbyGotMs : (int)(standbyAskedSw != null ? standbyAskedSw.ElapsedMilliseconds : 0);
                    standbyGot = null;
                    SetStandby(p, curIpc, true);
                    Log.Write("Motor en espera: listo (" + ms + " ms): capturadora soltada, sigue abierto y escondido (pid " + p.Id + ")");
                    if (relaunchSize == null && !relaunchEngine) relaunchEngine = true; // (con el motor en espera solo se pasa a mpv + Magpie)
                    if (userExit) { KillStandby("salir"); return 0; }
                }
                if (powerOffPid == p.Id && PowerOffClock != null)
                    Log.Write("Apagado: mpv " + (hung ? "sin terminar" : "terminado") + " a los " + PowerOffClock.ElapsedMilliseconds + " ms");
                // 21-11 (1.0.11, el usuario: "si cierro la app desde la barra de tareas no se cierra"): la ventana de la imagen cerrada por el
                // usuario (barra de tareas, su X, el administrador de tareas: el motor sale con 77, mpv tambien por scripts\ce_power.lua) = Salir.
                // Antes se tomaba por un corte de la señal y se volvia a abrir. Las reaperturas de la app van por la tuberia, no por cerrar la ventana
                // (1.0.12: SOLO si ha salido de verdad. En el relevo sin telon y al pasar a espera el motor sigue vivo a proposito: leer su
                // ExitCode lanzaba InvalidOperationException -> "FATAL: No se pudo lanzar mpv", Magpie sin mpv y el boton central sin imagen)
                if (!userExit && !hung && p.HasExited && p.ExitCode == MpvConst.ClosedByUserExitCode)
                {
                    Log.Write("Ventana de la imagen cerrada por el usuario (barra de tareas o su X): se cierra la app");
                    userExit = true;   // (ya: que nada de abajo lo tome por la consola apagada ni apague el PC)
                    OnUi(delegate { QuitRequested(); });
                    return 0;
                }
                // Captura cambiada con el mando: otra vez con el tamaño nuevo (no si se cerro por la consola apagada: entonces se sale)
                if (!userExit && !hung && relaunchSize != null && powerOffPid != p.Id)
                {
                    bool newWindow = !Config.EngineActive && Config.WindowFor(relaunchSize) != Config.WindowFor(cfg.CaptureNow);
                    // (en el HOME la captura de 4K no es la del juego: VideoSize no se toca)
                    if (relaunchSize != cfg.CaptureNow) cfg.VideoSize = relaunchSize;
                    switchUntil = DateTime.MinValue;
                    ShowCurtain();
                    Log.Write("Captura a " + SizeName(cfg.CaptureNow) + " (" + cfg.FramerateNow + " fps): se vuelve a abrir " + (Config.EngineActive ? "el motor" : "mpv") +
                              (newWindow ? " (ventana de " + Config.WindowFor(cfg.VideoSize) + ")" : ""));
                    relaunchSize = null;
                    signalOn = false;
                    waitStart = DateTime.Now;
                    GraceOffForRelaunch("cambio de captura");
                    goto relaunch;
                }
                // 15-11: DLSS 5 encendido / apagado con motor=propio: del motor a mpv + Magpie o al reves
                if (!userExit && !hung && relaunchEngine && powerOffPid != p.Id)
                {
                    relaunchEngine = false;
                    PrepareCfg();
                    Log.Write("Cambio de motor: " + (cfg.EngineNow ? "motor propio" : "mpv sin el motor"));
                    // (16-11, dlss5_sin_telon: con el relevo la otra imagen ya esta a la vista: sin telon)
                    if (!RelevoShowing()) ShowCurtain();
                    signalOn = false;
                    waitStart = DateTime.Now;
                    GraceOffForRelaunch("cambio de motor");
                    goto relaunch;
                }
                // 20-11 (CIERRES, src\Cierres.cs): cerrado por la imagen parada sin ser la consola (la grafica o el motor parados): se vuelve
                // a abrir; nunca es la consola apagada (ni se apaga el PC)
                if (!userExit && hangKilledPid == p.Id)
                {
                    if (!RecoverFromHang(p, hung)) return 0;
                    signalOn = false;
                    waitStart = DateTime.Now;
                    GraceOffForRelaunch("imagen parada");
                    KillStandby("la imagen se ha parado: el motor en espera puede estar igual"); // (como tras un reinicio de la grafica)
                    goto relaunch;
                }
                // Sin imagen (lo vio la app o el script Lua): la consola apagada o la grafica
                // reiniciada por Windows. Con lo segundo se recupera todo en vez de salir
                if (!userExit && (hung || stallKilledPid == p.Id || p.ExitCode == MpvConst.PowerOffExitCode ||
                                  File.Exists(Paths.FlagFile)) && RecoverFromGpuReset(p))
                {
                    signalOn = false;
                    waitStart = DateTime.Now;
                    GraceOffForRelaunch("reinicio de la grafica");
                    KillStandby("la grafica se ha reiniciado: su dispositivo ya no vale"); // (16-11: el motor en espera)
                    goto relaunch;
                }
                if (hung) return MpvConst.PowerOffExitCode;
                // 15-11: el motor propio se cierra solo jugando (fallo, la grafica reiniciada): se vuelve a abrir en vez de salir de la
                // app; a la 3ª vez en la sesion, mpv + Magpie
                if (!userExit && Config.EngineActive && p.ExitCode != 0 && p.ExitCode != MpvConst.PowerOffExitCode && !File.Exists(Paths.FlagFile))
                {
                    engineCrashes++;
                    FsrMulCaida(p, p.ExitCode);     // (1.2.4 ronda 6, FGMULTI: con FSR 3 FG x3 / x4, x2 el resto de la sesion; MultiFsr.cs)
                    MotorGenCaida(p, p.ExitCode);   // (1.2.4 ronda 5, XEFGM: con XeSS FG / la generacion propia en el, esa generacion por Magpie el resto de la sesion; XefgMotor.cs)
                    // (1.2.4 ronda 6, INTEGRA6: sin mpv ni Magpie no hay "el resto de la sesion sin el motor": se vuelve a abrir siempre -antes, a
                    // la 3a, cfg.Engine = "magpie" y EngineNow falso el resto de la sesion aunque la imagen siguiera por el motor-)
                    Log.Write("El motor propio se ha cerrado solo (codigo " + p.ExitCode + ", vez " + engineCrashes + "; ver data\\logs\\engine.log): se vuelve a abrir");
                    // (1.2.4 arreglos, RELEVO: la imagen congelada en vez de negro; sin la espera si hay un motor en espera que vale. RelevoMotor.cs.
                    // Lo que va por Magpie tras una caida con XeSS FG / la FG propia del motor lo decide XEFGM, XefgMotor.cs)
                    CrashCurtain(p);
                    signalOn = false;
                    waitStart = DateTime.Now;
                    GraceOffForRelaunch("motor cerrado");
                    int crashWait = CrashWaitMs();
                    if (crashWait > 0) Thread.Sleep(crashWait); // (tras un reinicio de la grafica, un momento para que vuelva)
                    else Log.Write("Motor en espera listo: se despierta ya, sin la espera de 1,5 s" + (crashBridgeNext ? " (no vale para lo de ahora: de puente, sin generacion)" : ""));
                    goto relaunch;
                }
                // Cerrado solo (codigo 0) sin pedirlo y sin apagar la consola: la capturadora corto la señal (medido: al abrir
                // GameChat, al volver al HOME). Se vuelve a abrir en vez de salir
                // (16-11: desde que se lanzo o se desperto, imageStart: el StartTime de un motor despertado es de cuando se arranco en espera)
                if ((DateTime.Now - imageStart).TotalMinutes >= 2) autoReopens = 0; // cortes sueltos
                if (p.ExitCode == 0 && !userExit && !File.Exists(Paths.FlagFile) &&
                    (DateTime.Now - imageStart).TotalSeconds >= 3 && autoReopens < 6)
                {
                    autoReopens++;
                    Log.Write("mpv se ha cerrado solo (corte de la señal de la capturadora): se vuelve a abrir");
                    ShowCurtain();
                    signalOn = false;
                    waitStart = DateTime.Now;
                    GraceOffForRelaunch("corte de la señal");
                    goto relaunch;
                }
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo lanzar mpv", ex);
                Fatal("No se pudo lanzar mpv:\n" + ex.Message);
                return int.MinValue;
            }
        }

        // ---------------- Lo que se le dice al motor al estilo de mpv ----------------
        // (23-11, 1.2.4 SINMPV: mpv ya no se abre -BuildMpvArgs y AddCommonMpvArgs, su linea de ordenes, fuera-. DedupeOn, FrameFilter, SourceUrl
        // y LavfOptions se quedan: son lo que el motor lee por su tuberia al estilo de mpv -"vf set", "set demuxer-lavf-o", "loadfile"-, EngineMpv.cs)

        // Filtro de imagenes (se cambia en caliente con "vf set" al cambiar de modo). Normal: quita las repetidas.
        // Modo 30 fps: como mucho 1 de cada 2 (a 60 fps de la consola, 30 regulares): pasa una imagen nueva si han
        // pasado 2 desde la ultima (asi un juego a 30 sigue con sus imagenes nuevas, sin retraso) o cualquiera a las 3
        // 09-11: si se quitan las repetidas. En el modo 60 SIN generacion no: alli no hay nada que inventar entre dos iguales
        // y quitarlas solo dejaba huecos de 33 ms (sesion del 09-11, Ocarina: hasta 270 en 20 s, 1272 en 28 min) en los que
        // LS no presentaba nada y la tele, con VRR, bajaba a 30 Hz. Asi LS recibe 60 regulares (las copias tambien se
        // escalan: sin generacion sobra grafica). Con generacion (y en el modo 30) sigue igual
        public static bool DedupeOn(Config cfg, bool fgOn)
        {
            // 20-11 (HZ120): a 120 las imagenes son reales a 120, sin generacion: no hay repetidas que quitar (menos proceso = menos latencia)
            return !cfg.HomeNative && !cfg.Mode120 && (cfg.CapMode || (cfg.Dedupe && fgOn));
        }

        // 16-11 (G1, repetidas_30_sin_perder=1): el del modo 30 de antes perdia imagenes reales si el juego no va a ritmo fijo (cada imagen
        // 1, 2 o 3 refrescos): 10-12 % con pares 3,1 / 1,3, el 25 % con 1,3,1,3, y a 20 fps con tirones (4 refrescos) se quedaba 2 refrescos
        // tarde (33 ms) hasta otro tiron. Medido sin grafica (cadencia.exe: fuente y4m a 59,94 por mpv --vo=null --untimed con este --vf; mpv
        // da justo lo del modelo). Ahora: (1) si la imagen nueva llega 1 refresco despues de la ultima que paso (aun no puede pasar: Magpie va
        // a 30), pasa su copia del refresco siguiente: 1 refresco tarde y no se pierde; (2) tras una copia forzada (imagen quieta) la nueva
        // pasa en cuanto llega. Solo pierde las que no caben ni con 1 refresco de retraso: las mismas que el optimo sabiendo el futuro
        // (30 regular, 20 fps y 60 fps igual que antes). Registros de la expresion: 0 = la anterior era nueva, 1 = esta es nueva,
        // 2 = esta pasa, 3 = la ultima que paso era una copia forzada
        // 22-11 (1.2.3 RITMO2): (1) tenia un fallo de latencia: una vez que pasa una copia tarde, cada imagen nueva llega 1 refresco despues
        // de la ultima que paso y tampoco puede pasar: pasa su copia, y asi SIEMPRE, todas 16,7 ms tarde. Le pasa a un juego a 30 regular si mpv
        // empieza en la segunda copia (la mitad de las veces) y tras cada par 3,1 (un tiron que se recupera); solo lo arregla una imagen de 3
        // refrescos. Medido sin grafica (mpv --vo=null, 1280x720, fuente con la cadencia): 30 regular 100 % de las imagenes 1 refresco tarde,
        // pares 3,1 cada 20 el 95 %. Ahora (registros 4, 6 y 7): tras pasar una copia tarde, la nueva siguiente pasa en cuanto llega aunque
        // sea a 1 refresco (una pareja junta y se vuelve a tiempo), como mucho una vez cada 40 refrescos (para no pasar de 30: a 45 fps sale
        // a 30,6/s). Medido: 30 regular 6 % tarde (solo hasta la primera vuelta), pares 3,1 46 %, caida 12 -> 0 %; 1,3,1,3 igual que antes (50 %
        // tarde, a 2 refrescos: se sigue igualando), a 45 fps 30,6 pasadas/s y las mismas perdidas (101 frente a 107), quieta igual
        // (repetidas_30_sin_perder=1 de siempre; test_filtro= para probar otro)
        public static string FrameFilter(Config cfg)
        {
            if (cfg.TestFrameFilter.Length > 0) return cfg.TestFrameFilter; // (solo pruebas: ini test_filtro)
            if (cfg.CapMode && cfg.Dedupe30Keep)
                return "@s2dup:lavfi=[select='st(1,gt(scene,0));st(7,ld(1)*ld(4)*eq(n-prev_selected_n,1)*gt(n-ld(6),40));" +
                       "st(2,isnan(prev_selected_n)+gte(n-prev_selected_n,2)*(ld(1)+ld(0))+gte(n-prev_selected_n,3)+ld(1)*ld(3)+ld(7));" +
                       "st(3,if(ld(2),eq(ld(1)+ld(0),0)*not(isnan(prev_selected_n)),ld(3)));st(4,if(ld(2),eq(ld(1),0)*ld(0),ld(4)));" +
                       "st(6,if(ld(7),n,ld(6)));st(0,ld(1));ld(2)']";
            return cfg.CapMode
                ? "@s2dup:lavfi=[select='isnan(prev_selected_n)+gt(scene,0)*gte(n-prev_selected_n,2)+gte(n-prev_selected_n,3)']"
                : "@s2dup:lavfi=[select='gt(scene,0)+isnan(prev_selected_n)+gte(n-prev_selected_n,2)']";
        }

        // Lo que abre mpv: la capturadora o, en pruebas, la fuente de test_source ({w} y {h}: el tamaño de captura,
        // para probar el cambio de captura sin consola)
        public static string SourceUrl(Config cfg)
        {
            if (cfg.TestSource.Length > 0)
            {
                // 21-11 (1.2.1): el tamaño que se abre de verdad (CapturaSegura) y, solo pruebas, los formatos que "no da" (test_captura_mala)
                CapFmt ef = CapturaSegura.Efectivo(cfg);
                if (CapturaSegura.TestMala(cfg, ef)) return cfg.TestCapturaMalaUrl;
                string[] tw = ef.Size.Split('x');
                string ts = !cfg.HomeNative && cfg.TestSourceGame.Length > 0 ? cfg.TestSourceGame : cfg.TestSource;
                return ts.Replace("{w}", tw[0]).Replace("{h}", tw[1]);
            }
            string src = "av://dshow:video=" + cfg.Device;
            if (cfg.CaptureAudio) src += ":audio=" + cfg.Device;
            return src;
        }

        // Opciones de la capturadora (tamaño, ritmo, formato y bufer)
        public static string LavfOptions(Config cfg)
        {
            // Búfer de captura de ~3 fotogramas: si mpv se retrasa (p. ej. al abrir el
            // audio) se descartan fotogramas en vez de acumular retraso permanente.
            // 21-11 (1.2.1): lo que se abre de verdad (CapturaSegura.Efectivo: lo pedido o, si la capturadora no lo da, su repuesto)
            CapFmt ef = CapturaSegura.Efectivo(cfg);
            string[] wh = ef.Size.Split('x');
            long rtbuf = long.Parse(wh[0], CultureInfo.InvariantCulture) *
                         long.Parse(wh[1], CultureInfo.InvariantCulture) * 3 * 3;
            string lavf = "video_size=" + ef.Size + ",framerate=" + ef.Fps +
                          (ef.Pix.Length > 0 ? ",pixel_format=" + ef.Pix : "") + // (ConsoleEnhancer: sin formato, el de la capturadora)
                          ",rtbufsize=" + rtbuf + ",fflags=+nobuffer";
            if (cfg.CaptureAudio) lavf += ",audio_buffer_size=30"; // ms; el valor por defecto (~500 ms) retrasa el audio
            return lavf;
        }

        static string JoinArgs(List<string> a)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string s in a)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Quote(s));
            }
            return sb.ToString();
        }

        static string Quote(string s)
        {
            if (s.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return s;
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }
    }

    // ------------------------------------------------------------------
    // Punto de entrada
    // ------------------------------------------------------------------
    static partial class Program
    {
        // Sin esto Windows dibuja la pantalla de carga a baja resolucion y la estira
        // (p. ej. 1280x720 en un 4K al 300 %)
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch (Exception) { }
            TrayApp.InstalarManejadoresDeFallos(); // 21-11 (1.2.1): antes de cualquier ventana (el asistente las crea); ver src\Cierres.cs
            bool dryRun = false, dryCap = false;
            int dryPasses = 1;
            foreach (string arg in args)
            {
                // ConsoleEnhancer: --inicio (el telon al iniciar sesion), --iniciar-con-windows=, --prueba-apagado, --ui-preview=apagado (src\InicioWindows.cs)
                { int ce = CeInicio.Argumento(arg, args); if (ce != int.MinValue) return ce; }
                if (arg.StartsWith("--splash-preview="))
                {
                    // Guarda una captura de la pantalla de carga (para revisar el diseño)
                    using (SplashForm f = new SplashForm())
                    {
                        Cursor.Show();

                        Thread.Sleep(1600);
                        f.Bounds = new Rectangle(0, 0, 1920, 1080);
                        using (Bitmap bmp = new Bitmap(1920, 1080))
                        {
                            f.DrawToBitmap(bmp, new Rectangle(0, 0, 1920, 1080));
                            bmp.Save(arg.Substring(17), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    return 0;
                }
                // 15-11 (Prisma): vistas previas del aspecto (pantalla de carga, telones, avisos, menu) a PNG, sin mostrar ninguna
                // ventana: --ui-preview=<carpeta>[;<imagen de un juego para el telon>] (ver src\PrismaPreview.cs)
                // 18-11 (CONSOLAS): prueba sin consola de src\Consolas.cs (perfiles, deteccion con un reloj falso, la resolucion de verdad)
                if (arg.StartsWith("--test-consolas="))
                {
                    int cf;
                    string rep = Consolas.SelfTest(out cf);
                    File.WriteAllText(arg.Substring(16), rep, new UTF8Encoding(false));
                    return cf;
                }
                // ConsoleEnhancer: --ui-preview=asistente;<carpeta> (una imagen de cada paso del asistente, sin abrir la capturadora)
                if (arg.StartsWith("--ui-preview=asistente")) { string[] pv = arg.Substring(13).Split(new char[] { ';' }, 2); Asistente.Preview(pv.Length > 1 ? pv[1] : Path.Combine(Paths.Datos, "asistente")); return 0; }
                // (20-11, CE-UI) ConsoleEnhancer: --ui-preview=componentes;<carpeta> (la ventana del primer arranque que baja mpv/Magpie, en sus estados, sin red)
                if (arg.StartsWith("--ui-preview=componentes")) { string[] pv = arg.Substring(13).Split(new char[] { ';' }, 2); return Componentes.Preview(pv.Length > 1 ? pv[1] : Path.Combine(Paths.Datos, "componentes")); }
                if (arg == "--esperar-salida") Ce.EsperarSalida();
                // 19-11: el lector del menu de la PS5 con imagenes (src\MenuPs5.cs): --test-ps5=<carpeta>[;<informe>]
                if (arg.StartsWith("--test-ps5=")) return RelojPs5.Prueba(arg.Substring(11));

                // 20-11 (PS5JUEGO): leer el nombre del juego en el overlay (centro de control / conmutador de la PS5, Guia / fichas de la
                // Xbox): --test-juegos-consola=<carpeta>[;<informe>] (xbox_* usa la Xbox, el resto la PS5)
                if (arg.StartsWith("--test-juegos-consola=")) return JuegoConsola.Prueba(arg.Substring(22));
                // 1.2.3 (JUEGO): el juego en marcha (primer foco de la visita, modo guardado, huella del HUD): --test-juego-en-marcha=<informe>[;<capturas>]
                if (arg.StartsWith("--test-juego-en-marcha=")) return PruebaJuegoEnMarcha.Run(arg.Substring(23));
                // 19-11 (DETECCION): el menu de la consola con imagenes y escenas, antes y ahora (src\PruebaDeteccion.cs):
                // --test-deteccion=<carpeta>[;<informe>] (games\test_deteccion) o --test-deteccion=listar;<carpeta>;<informe.tsv>
                if (arg.StartsWith("--test-deteccion=")) return PruebaDeteccion.Run(arg.Substring(17));
                // (1.2.1-1.2.4: --test-escala, la escala de DLSS 5 por Magpie, y --test-reescalado, su config de reescalado; 1.2.4 ronda 6,
                // QUITAFG: sin Magpie, fuera: dicen que ya no estan y salen bien, para los guiones de antes)
                if (arg == "--test-escala" || arg.StartsWith("--test-escala=") || arg == "--test-reescalado" || arg.StartsWith("--test-reescalado="))
                { Console.WriteLine(arg.Split('=')[0] + ": era de Magpie, que ya no esta (1.2.4 ronda 6): nada que probar"); return 0; }
                // (1.2.4 ronda 6, QUITAFG) los tipos de generacion y la puesta al dia de lo guardado con DLSS FG / x3 / x4, sin abrir nada:
                // --test-tipos-generacion[=<informe>][;<carpeta con copias de datos de usuario>] (src\Lanzador\TiposGeneracion.cs). El
                // --test-multiplicador de la 1.2.2-1.2.4 (las reglas de x3 / x4, que ya no estan) hace lo mismo, para los guiones de antes
                if (arg == "--test-tipos-generacion" || arg.StartsWith("--test-tipos-generacion=") || arg == "--test-multiplicador" || arg.StartsWith("--test-multiplicador="))
                {
                    int ig = arg.IndexOf('=');
                    string[] pa = (ig > 0 ? arg.Substring(ig + 1) : "").Split(';');
                    return TrayApp.TiposPrueba(pa[0].Length > 0 ? pa[0] : null, pa.Length > 1 ? pa[1] : null);
                }
                // 22-11 (1.2.3, MOTOR5): por donde va DLSS 5 (motor con PrismaNr.dll / Magpie) con cada combinacion: --test-motor-dlss5[=<informe>]
                if (arg == "--test-motor-dlss5" || arg.StartsWith("--test-motor-dlss5=")) return Config.MotorDlss5Prueba(arg.Length > 19 ? arg.Substring(19) : null);
                // 22-11 (1.2.4, SOLOMOTOR): el reparto motor / Magpie (reserva) con cada combinacion: --test-solo-motor[=<informe>] (SoloMotor.cs)
                if (arg == "--test-solo-motor" || arg.StartsWith("--test-solo-motor=")) return Config.SoloMotorPrueba(arg.Length > 18 ? arg.Substring(18) : null);
                // 23-11 (1.2.4 ronda 6, SMX4): cuando hay Smooth Motion x4 y que va al perfil y al motor: --test-smooth-x4[=<informe>] (SmoothX4.cs)
                if (arg == "--test-smooth-x4" || arg.StartsWith("--test-smooth-x4=")) return Config.SmoothX4Prueba(arg.Length > 17 ? arg.Substring(17) : null);
                // 22-11 (1.2.3, MOTOR5): PrismaNr.dll junto a la DLL elegida en "Añadir DLSS 5": --test-extras-guardados[=<informe>]
                if (arg == "--test-extras-guardados" || arg.StartsWith("--test-extras-guardados=")) return ExtrasGuardados.Prueba(arg.Length > 24 ? arg.Substring(24) : null);
                // 22-11 (1.2.4): la busqueda de los archivos de DLSS 5 en el PC con carpetas falsas: --test-buscar-extras[=<informe>] (BuscaExtras.cs)
                if (arg == "--test-buscar-extras" || arg.StartsWith("--test-buscar-extras=")) return BuscaExtras.Prueba(arg.Length > 21 ? arg.Substring(21) : null);
                if (arg.StartsWith("--ui-preview=")) return PrismaPreview.Run(arg.Substring(13));
                // 18-11 (MANDOS): --test-mandos=<segundos>;<archivo>: mandos detectados, botones pulsados y el modo PC (sin mandar nada)
                if (arg.StartsWith("--test-mandos=")) return PadTest.Run(arg.Substring(14), args);
                // 18-11 (MANDOS): prueba del bloqueo de Joyxoff con la imagen delante (ganchos; sin la capturadora)
                if (arg.StartsWith("--test-bloqueo=")) return BlockTest.Run(arg.Substring(15), args);
                // 18-11 (MANDOS): el modo PC del mando sin la app abierta (residente, sin ventanas; en lugar de Joyxoff)
                if (arg == "--mando-pc") return PadResident.Run();
                // 19-11 (ESCRITORIO): a mano, sin abrir la app: el escritorio normal (games\desktop_normal.txt) o el negro
                if (arg == "--restaurar-escritorio") return Escritorio.AMano(false);
                if (arg == "--escritorio-negro") return Escritorio.AMano(true);
                if (arg.StartsWith("--test-escritorio=")) return Escritorio.Prueba(arg.Substring(18));
                // Paso elevado (lo lanza NvProfiles.Ensure): solo guarda los perfiles de NVIDIA
                if (arg == "--nvidia-profiles")
                {
                    bool saveDenied;
                    return NvProfiles.Apply(out saveDenied);
                }
                // 15-11: prueba sin consola de la limpieza, votacion y union de los nombres leidos (GameNames) -> games\test_nombres.txt
                if (arg == "--test-nombres")
                {
                    int fails;
                    string rep = GameNames.SelfTest(out fails);
                    File.WriteAllText(Path.Combine(Paths.Datos, @"games\test_nombres.txt"), rep, new UTF8Encoding(false));
                    return fails;
                }
                // 15-11: prueba sin consola de la entrada en los juegos desde el HOME (pantallas ya clasificadas por OutGrab / ResetOut /
                // DecideHomeNative con un reloj falso; ver TrayApp.EntrySelfTest) -> games\test_entrada.txt
                if (arg == "--test-entrada")
                {
                    int fails;
                    string rep = TrayApp.EntrySelfTest(out fails);
                    File.WriteAllText(Path.Combine(Paths.Datos, @"games\test_entrada.txt"), rep, new UTF8Encoding(false));
                    return fails;
                }
                // 22-11 (1.2.2, MENU): lo que decide la Guia en cada momento de la imagen (AjustesGuia.cs) -> games\test_menu_ajustes.txt
                if (arg == "--test-menu-ajustes")
                {
                    int fails;
                    string rep = TrayApp.AjustesSelfTest(out fails);
                    Directory.CreateDirectory(Path.Combine(Paths.Datos, "games"));
                    File.WriteAllText(Path.Combine(Paths.Datos, @"games\test_menu_ajustes.txt"), rep, new UTF8Encoding(false));
                    return fails;
                }
                // 15-11: prueba de los perfiles guardados (cargar el formato de antes, cambiar nombres, juntar, escribir) sobre una COPIA:
                // --test-perfiles=<carpeta con profiles.txt e game_icons.txt> -> <carpeta>\test_perfiles.txt
                if (arg.StartsWith("--test-perfiles="))
                {
                    string dir = arg.Substring(16);
                    if (string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.Combine(Paths.Datos, "games"), StringComparison.OrdinalIgnoreCase))
                        return -1; // (nunca sobre los perfiles de verdad)
                    int fails;
                    string rep = GameProfiles.SelfTest(dir, out fails);
                    File.WriteAllText(Path.Combine(dir, "test_perfiles.txt"), rep, new UTF8Encoding(false));
                    return fails;
                }
                // Prueba del lector del HOME con imagenes: --test-home=<png>[;<png>...] o una carpeta (games\diag_home) -> games\test_home.txt.
                // 15-11: con lo que lee el OCR tal cual, el nombre limpio, si la etiqueta esta cortada y el icono, y al final todas las
                // lecturas juntas como si fueran de una misma seleccion (NameReads). Los recortes del nombre (_nombre.png) se leen tal cual
                if (arg.StartsWith("--test-home="))
                {
                    StringBuilder sb = new StringBuilder();
                    List<string> files = new List<string>();
                    foreach (string f in arg.Substring(12).Split(';'))
                    {
                        if (Directory.Exists(f))
                        {
                            string[] pngs = Directory.GetFiles(f, "*.png");
                            Array.Sort(pngs, StringComparer.OrdinalIgnoreCase);
                            files.AddRange(pngs);
                        }
                        else if (f.Length > 0) files.Add(f);
                    }
                    NameReads all = new NameReads(null, null);
                    foreach (string f in files)
                    {
                        try
                        {
                            using (Bitmap img = new Bitmap(f))
                            using (Bitmap full = new Bitmap(img.Width, img.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                            {
                                using (Graphics g = Graphics.FromImage(full)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                                if (full.Width > full.Height * 3)
                                {
                                    // (un recorte del nombre: tal cual al OCR)
                                    string rawC = Ocr.ReadLine(full), cleanC = GameNames.Clean(rawC, true);
                                    all.Add(cleanC, false);
                                    sb.Append(Path.GetFileName(f)).Append(": recorte ").Append(full.Width).Append("x").Append(full.Height)
                                      .Append(", OCR \"").Append((rawC ?? "").Replace("\r", " ").Replace("\n", " ")).Append("\" -> \"").Append(cleanC).Append("\"\r\n");
                                    continue;
                                }
                                using (Bitmap small = HomeLabel.Small(full))
                                {
                                    Rectangle box;
                                    bool hint;
                                    HomeLabel.LabelText label;
                                    byte[] lab = HomeLabel.Selected(small, out box, out hint, out label);
                                    // 16-11: la biblioteca: el nombre de su pastilla, como AnalyzeGrab
                                    bool lib = HomeLabel.IsLibrary(small);
                                    if (lib) { lab = HomeLabel.LibraryLabel(small, out box, out label); hint = false; }
                                    string name = null, raw = null;
                                    Rectangle fb = Rectangle.Empty;
                                    if (lab != null && label != null)
                                    {
                                        fb = HomeLabel.OcrRect(label, full.Width, full.Height);
                                        if (fb.Width > 4 && fb.Height > 4)
                                            using (Bitmap crop = full.Clone(fb, full.PixelFormat)) raw = Ocr.ReadLine(crop);
                                        name = GameNames.Clean(raw, label.Icon);
                                        all.Add(name, label.Clipped);
                                    }
                                    sb.Append(Path.GetFileName(f)).Append(": ").Append(lib ? "biblioteca, " : "")
                                      .Append(lab == null ? "sin nombre" : "nombre \"" + name + "\" en " + box)
                                      .Append(hint ? " (saltado un texto conocido)" : "");
                                    if (label != null)
                                        sb.Append(" [OCR \"").Append((raw ?? "").Replace("\r", " ").Replace("\n", " ")).Append("\"; texto ").Append(label.Width)
                                          .Append(" px de ").Append(label.W).Append(label.Clipped ? ", cortado (marquesina)" : "")
                                          .Append(label.Icon ? ", icono hasta x=" + label.IconEnd : ", sin icono").Append("; recorte ").Append(fb)
                                          .Append(GameNames.Plausible(name) ? "" : "; no parece un nombre").Append("]");
                                    // 09-11: lo que miran la entrada en los juegos (en uso) y las capturas fuera del HOME
                                    if (lab != null && lib) sb.Append("; en uso: solo si es el juego en marcha (por el nombre)");
                                    else if (lab != null)
                                    {
                                        int iu = HomeLabel.InUsePixels(small, box);
                                        sb.Append("; en uso: ").Append(iu >= HomeLabel.InUseMin ? "si" : "no").Append(" (").Append(iu).Append(")");
                                    }
                                    else if (lib) sb.Append("; sin la pastilla del nombre: sigue siendo el HOME (no se sale)");
                                    else
                                    {
                                        double lit, dk, dc;
                                        HomeLabel.Thumb(small, out lit);
                                        double[] hr = HomeLabel.ParseNums(File.Exists(Paths.HomeRefFile) ? File.ReadAllText(Paths.HomeRefFile).Trim() : null, 32 * 6 + 32 * 5);
                                        double[] st = HomeLabel.Strips(small);
                                        bool dim = HomeLabel.DimHome(st, hr, out dk, out dc);
                                        bool pk;
                                        string txt;
                                        using (Bitmap lt = new Bitmap(full, 1280, 720)) txt = Ocr.Read(lt);
                                        bool cs = HomeHints.IsConsoleScreen(txt, out pk);
                                        sb.Append("; HOME ").Append(HomeLabel.HomeDiff(st, hr) <= HomeLabel.IsHome ? "si" : "no")
                                          .Append(", atenuado ").Append(dim ? "si" : "no").Append(" (k ").Append(dk.ToString("0.00", CultureInfo.InvariantCulture))
                                          .Append(", corr ").Append(dc.ToString("0.00", CultureInfo.InvariantCulture)).Append(")")
                                          .Append(", luz ").Append(lit.ToString("0.000", CultureInfo.InvariantCulture))
                                          .Append(lit < HomeLabel.DarkLit ? " (negro)" : "")
                                          .Append(", consola ").Append(cs ? (pk ? "si (elegir usuario)" : "si") : "no")
                                          .Append(", texto \"").Append((txt ?? "").Replace("\r", " ").Replace("\n", " ")).Append("\"");
                                    }
                                    sb.Append("\r\n");
                                }
                            }
                        }
                        catch (Exception ex) { sb.Append(f).Append(": ERROR ").Append(ex.Message).Append("\r\n"); }
                    }
                    if (all.Attempts > 1)
                        sb.Append("Todas juntas (como lecturas seguidas de un mismo juego): \"").Append(all.Best).Append("\"")
                          .Append(all.Sure ? ", seguro" : ", sin confirmar").Append(all.Clipped ? ", se desplaza" : "")
                          .Append(" (").Append(all.Count).Append(" lecturas utiles de ").Append(all.Attempts).Append(")\r\n");
                    File.WriteAllText(Path.Combine(Paths.Datos, @"games\test_home.txt"), sb.ToString(), new UTF8Encoding(false));
                    return 0;
                }
                if (arg == "--dry-run") dryRun = true;
                // (solo con --dry-run: las pasadas y el modo 30 que elegiria el mando)
                else if (arg.StartsWith("--dry-passes=")) dryPasses = int.Parse(arg.Substring(13), CultureInfo.InvariantCulture);
                else if (arg == "--dry-cap") dryCap = true;
                // 16-11: solo pruebas: otro ini (una copia con las lineas de la prueba; el de verdad lo editan otros mientras)
                else if (arg.StartsWith("--ini=") && File.Exists(arg.Substring(6))) Paths.IniFile = Path.GetFullPath(arg.Substring(6));
            }

            // ConsoleEnhancer (GITHUB): mpv y Magpie no van en el zip (sus licencias): la primera vez se bajan de sus releases oficiales (src\Lanzador\Componentes.cs)
            // (1.2.06) el idioma antes de la primera ventana: el del ini o el elegido; si no, ingles (Idioma.Poner)
            try { Idioma.Poner(Ce.IniLeer("idioma")); } catch { }
            { int cr = Componentes.Asegurar(args, dryRun); if (cr >= 0) return cr; }
            // ConsoleEnhancer: primer arranque / --asistente / hardware cambiado -> deteccion + asistente (src\ConsoleEnhancerApp.cs)
            try { int ar = Ce.Arranque(args, dryRun); if (ar >= 0) return ar; }
            catch (Exception ex) { Log.Error("Asistente", ex); }
            Config cfg;
            try { cfg = Config.Load(); }
            catch (Exception ex)
            {
                Log.Error("No se pudo leer la configuracion", ex);
                MessageBox.Show("No se pudo leer " + Paths.IniFile + ":\n" + ex.Message, PrismaUi.ShownName,
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            if (dryRun)
            {
                cfg.Passes30 = Math.Max(1, Math.Min(3, dryPasses));
                cfg.CapMode = dryCap;
                CeImagen.AlAbrir(cfg, false); // (ConsoleEnhancer: VRR y escalas, sin tocar el sistema)
                return DryRun(cfg);
            }

            bool created;
            using (Mutex m = new Mutex(true, "Local\\ConsoleEnhancer_SingleInstance", out created))
            {
                if (!created)
                {
                    Log.Write("Ya hay otra instancia en marcha; saliendo");
                    MessageBox.Show(Deteccion.Tx.T("ConsoleEnhancer ya está en marcha (el icono en la bandeja).", "ConsoleEnhancer is already running (tray icon)."), PrismaUi.ShownName,
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                // La app original (con Lossless Scaling) usa la misma capturadora: las dos a la vez no
                bool otherApp;
                // (ConsoleEnhancer: Prisma / Switch2Magpie usa la misma capturadora: las dos a la vez no)
                using (Mutex o = new Mutex(false, "Local\\Switch2Magpie_SingleInstance", out otherApp)) { }
                if (!otherApp)
                {
                    Log.Write("Prisma (Switch2Magpie) esta abierto; saliendo");
                    MessageBox.Show(Deteccion.Tx.T("Está abierto Prisma (Switch2Magpie).\nCiérralo antes: los dos usan la capturadora.", "Prisma (Switch2Magpie) is running.\nClose it first: both use the capture device."),
                                    PrismaUi.ShownName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 0;
                }
                // El log crece sin limite: por encima de 4 MB, el anterior pasa a switch2.log.old
                try
                {
                    FileInfo lf = new FileInfo(Paths.LogFile);
                    if (lf.Exists && lf.Length > 4 * 1024 * 1024)
                    {
                        string old = Paths.LogFile + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(Paths.LogFile, old);
                    }
                }
                catch { }
                Log.Write("===== Inicio ConsoleEnhancer =====");
                try { CeDefender.AlAbrir(); } catch (Exception ex) { Log.Error("Microsoft Defender", ex); } // (1.2.08: si Defender cerro la app, explicarlo; src\Lanzador\DefenderAviso.cs)
                CeInicio.Sincronizar(cfg); // (ConsoleEnhancer: iniciar_con_windows, src\InicioWindows.cs; 1.2.3: despues del aviso de Defender, por si pregunta)
                CeSuspension.AlAbrir(cfg); // (1.0.14: que los mandos y teclados nuevos despierten el PC, src\Suspension.cs)
                CeImagen.AlAbrir(cfg); // (ConsoleEnhancer: escalas aprendidas, VRR, HDR / RTX HDR / Auto HDR; src\ImagenCe.cs)
                if (!string.Equals(Paths.IniFile, Paths.Datos + @"\consoleenhancer.ini", StringComparison.OrdinalIgnoreCase)) Log.Write("Ini de prueba: " + Paths.IniFile);
                Escritorio.AlAbrir(cfg.BlackDesktop); // 19-11 (ESCRITORIO): el negro mientras esta abierta (en otro hilo)
                TrayApp.InstalarManejadoresDeFallos(); // 20-11 (CIERRES): los fallos no controlados al log; la app sigue (src\Cierres.cs)
                Application.EnableVisualStyles();
                try { Application.SetCompatibleTextRenderingDefault(false); } catch (InvalidOperationException) { } // (ConsoleEnhancer: el asistente ya ha creado ventanas)
                if (cfg.PerformanceBoost) Perf.BeginSession();
                Afterburner.Start(cfg.AfterburnerPause);
                if (cfg.PerformanceBoost && cfg.Background.Length > 0) Perf.ThrottleBackground(cfg.Background);
                // 16-11 (motor_en_espera): si la app falla, el motor escondido en espera no se queda abierto (y si muere sin poder
                // hacerlo, lo cierra Windows: EngineJob). 20-11 (CIERRES): solo si de verdad se cierra (un hilo que falla ya no la cierra)
                AppDomain.CurrentDomain.UnhandledException += delegate (object uo, UnhandledExceptionEventArgs ua) { if (ua.IsTerminating) TrayApp.KillStandbyAtExit("la app ha fallado"); };
                bool apagaPc = false;
                try { Application.Run(new TrayApp(cfg)); }
                finally
                {
                    TrayApp.KillStandbyAtExit("fin de la app"); ExitRestore.Finish(); // (a la vez; con la consola apagada ya empezado en PowerOffNow)
                    // 20-11 (CIERRES): el PC se apaga solo si nada dice que fuera la grafica (apagar_pc_confirmar, src\Cierres.cs)
                    apagaPc = TrayApp.PowerOffClock != null && TrayApp.ApagarPcAhora(cfg);
                    // 19-11 (ESCRITORIO): cerrada a mano -> el escritorio normal; si se apaga el PC (consola apagada o Windows) sigue negro
                    Escritorio.AlCerrar(apagaPc);
                    CeImagen.AlCerrar(); // (ConsoleEnhancer: el HDR de Windows, RTX HDR y Auto HDR como estaban)
                    CeEspera.AlCerrarApp(apagaPc); // (21-11, 1.2.1: si el PC sigue encendido, fuera la cubierta negra y la barra visible; src\Lanzador\Espera.cs)
                }
                if (TrayApp.PowerOffClock != null)
                    Log.Write("Cierre por consola apagada: " + TrayApp.PowerOffClock.ElapsedMilliseconds + " ms desde que se vio");
                Log.Write("===== Fin ConsoleEnhancer =====");
                // 19-11: consola apagada -> PC apagado (con el inicio rapido de Windows si esta activo: /hybrid arranca antes)
                // (ConsoleEnhancer: antes, el aviso con la cuenta atras que se puede cancelar: src\InicioWindows.cs)
                if (apagaPc) ApagarPc();
                CeBorrado.AlSalir(); // (1.2.3: "Desinstalar" de la bandeja: la app borra sus archivos ahora, ya cerrado todo; src\Lanzador\Desinstalar.cs)
            }
            return 0;
        }

        // (ConsoleEnhancer: ApagarPc, con el aviso que se puede cancelar, esta en src\InicioWindows.cs)

        // Prueba: lo que haria la app con este ini (rutas, perfil, camino, los argumentos de mpv). No lanza nada. (1.2.4 ronda 6, QUITAFG:
        // sin Magpie: ni su copia ni su config)
        static int DryRun(Config cfg)
        {
            Log.Write("===== DRY RUN =====");
            int rc = 0;
            try
            {
                Log.Write("DRY RUN XeSS / FSR para el motor: " + (Paths.HayLib("libxess_fg.dll") ? "xess\\ si" : "xess\\ NO") + ", " +
                          (Paths.HayLib("amd_fidelityfx_framegeneration_dx12.dll") ? "fsr\\ si" : "fsr\\ NO") + "; DLSS 5 " + (Integrado.Dlss5Available ? "en " + Integrado.Dlss5Dir : "no"));
                Log.Write("DRY RUN rutas: " + Integrado.Describe(cfg)); // (18-11, INTEGRADO)
                Log.Write("DRY RUN perfil: " + cfg.ProfileText);
                Log.Write("DRY RUN camino: " + cfg.SoloMotorTexto);   // (1.2.4 SOLOMOTOR)
                Log.Write("DRY RUN generacion: " + (cfg.FgOff ? "no" : cfg.FgTypeNow) + (Config.EngineActive || cfg.EngineNow ? " (motor, cadena " + cfg.EngineFgChain + ", Smooth Motion " + cfg.EngineSmoothMode + ")" : ""));
                Log.Write("DRY RUN sonido: el motor con --audio (sin mpv; ConsoleEnhancerEngine.exe --audio)");   // (23-11, 1.2.4 SINMPV)
            }
            catch (Exception ex)
            {
                Log.Error("DRY RUN fallido", ex);
                rc = 1;
            }
            Log.Write("===== FIN DRY RUN =====");
            return rc;
        }
    }
}
