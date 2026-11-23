// Integrado.cs - Magpie (y mpv y el motor) como piezas de la propia app, sin depender de nada instalado aparte (18-11-2026, agente
// INTEGRADO; el usuario: "que no haya que descargarse magpie, que sea como propia").
//  - Paths.AppDir: la carpeta del exe si es una carpeta completa de la app (la que monta scripts\empaquetar.ps1); antes, en la app original, una carpeta fija
//    (asi los exe de prueba compilados en otra carpeta siguen usando la app de siempre)
//  - magpie\ con ficheros propios de verdad. Antes eran enlaces duros al Magpie instalado aparte: el mismo fichero en los dos sitios
//    (una actualizacion del Magpie del usuario escrita encima cambiaba tambien el de la app, y con uno abierto el otro no se podia
//    cambiar). Se separan una vez (se copian). magpie_origen ya no hace falta: si esta y tiene ficheros mas nuevos, como mucho se
//    actualiza desde el (magpie_actualizar); si no esta, la app sigue con lo suyo y nunca pide instalar ni descargar Magpie
//  - Sin Updater.exe en la copia (el fork tiene la busqueda de actualizaciones quitada al compilar: no lo usa nunca)
//  - Sin el icono de Magpie en la bandeja (MagpieOculto): Magpie tiene que llevarlo para arrancar sin su ventana (-t), asi que se le
//    quita desde fuera en cuanto lo pone (y si el explorador se reinicia y lo vuelve a poner)
//  - engine\ConsoleEnhancerEngine.exe dentro de la app si existe (usar_carpetas_app=1), con la ruta del ini de respaldo (23-11, 1.2.4 SINMPV: sin mpv)
//  - Sin nvngx_dlssnr.dll (un paquete para repartir no la lleva: es una DLL de NVIDIA modificada, sin firma): DLSS 5 no disponible y la
//    app va sin el (con motor=propio, el motor propio), en vez de pedirle a Magpie un filtro que no puede cargar
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace ConsoleEnhancer
{
    static class Integrado
    {
        // ConsoleEnhancer (fase 2, UNIVERSAL): sin rutas de ningun PC. La app es la carpeta del exe; el motor va dentro
        public const string DefaultEngine = "";
        public const string Dlss5Dll = "nvngx_dlssnr.dll";
        public const string Dlss5MissingText = "DLSS 5 no disponible";

        // La carpeta de la app: SIEMPRE la del exe (portable: donde se descomprima). NO usa Paths (lo inicializa)
        public static string FindAppDir()
        {
            try
            {
                Assembly a = Assembly.GetEntryAssembly();
                if (a != null && !string.IsNullOrEmpty(a.Location)) return Path.GetDirectoryName(Path.GetFullPath(a.Location)).TrimEnd('\\');
            }
            catch { }
            return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        }

        // Los datos (ini, log, games\, calibracion): <app>\datos si esta portable.txt y se puede escribir ahi; si no,
        // %LOCALAPPDATA%\ConsoleEnhancer. Pruebas: --datos=<carpeta> (lo mira Program.Main antes de tocar Paths)
        public static string FindDataDir(string appDir)
        {
            string d = null;
            try
            {
                foreach (string a in Environment.GetCommandLineArgs())
                    if (a.StartsWith("--datos=", StringComparison.OrdinalIgnoreCase)) d = Path.GetFullPath(a.Substring(8));
                if (d == null && File.Exists(Path.Combine(appDir, "portable.txt")))
                {
                    MoverViejo(appDir, "datos", "data");   // (1.2.06: carpetas en ingles; ver NombresIngles)
                    string p = Path.Combine(appDir, "data");
                    Directory.CreateDirectory(p);
                    string probe = Path.Combine(p, ".escribir");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                    d = p;
                }
            }
            catch { d = null; }
            if (d == null) d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConsoleEnhancer");
            MoverViejo(appDir, "descargas", "downloads");
            NombresIngles(d);
            try { Directory.CreateDirectory(Path.Combine(d, "games")); Directory.CreateDirectory(Path.Combine(d, "logs")); } catch { }
            HeredarDatos(d, appDir);
            CopiaSeguridad(d.TrimEnd('\\'));
            return d.TrimEnd('\\');
        }

        // 21-11 (1.2.06, el usuario: "los archivos haz que esten en ingles"): las carpetas y ficheros de datos tenian nombres en espanol
        // (datos\, juegos\, captura.txt...). Los de una version anterior se renombran al abrir la app (si el nuevo aun no existe) y al
        // heredar datos de otra version se copian ya con el nombre nuevo
        static readonly string[] Viejos = {
            "consola.txt", "console.txt", "perfiles.txt", "profiles.txt", "generacion_tipo.txt", "framegen_type.txt", "iconos_juegos.txt", "game_icons.txt",
            "ultimo_juego.txt", "last_game.txt", "imagen_restaurar.txt", "picture_restore.txt", "generacion.txt", "framegen.txt",
            "escalas_motor.txt", "scales_engine.txt", "escalas_magpie.txt", "scales_magpie.txt", "vrr_visto.txt", "vrr_seen.txt", "modo.txt", "mode.txt",
            "escritorio_normal.txt", "desktop_normal.txt", "dlss5_activo.txt", "dlss5_on.txt", "despertar_armado.txt", "wake_armed.txt", "banco.txt", "bench.txt",
            "tramado_fuerte.txt", "strong_dither.txt", "textos_home.txt", "home_texts.txt", "portadas_consola.txt", "console_covers.txt",
            "iconos_consola.txt", "console_icons.txt", "heredado.txt", "inherited.txt", "espera_plan.txt", "standby_plan.txt",
            "escuchar_apagado.txt", "poweroff_listen.txt", "escritorio_estado.txt", "desktop_state.txt", "dlss_modelo_elegido.txt", "dlss_model_choice.txt",
            "dlss5_pasadas.txt", "dlss5_passes.txt", "dlss5_nivel.txt", "dlss5_level.txt", "juego_pendiente.txt", "pending_game.txt",
            "huellas_consola.txt", "console_fingerprints.txt", "captura.txt", "capture.txt" };

        public static string NombreIngles(string n)
        {
            for (int i = 0; i < Viejos.Length; i += 2) if (n.Equals(Viejos[i], StringComparison.OrdinalIgnoreCase)) return Viejos[i + 1];
            return n;
        }

        static void MoverViejo(string dir, string viejo, string nuevo)
        {
            try
            {
                string a = Path.Combine(dir, viejo), b = Path.Combine(dir, nuevo);
                if (Directory.Exists(a) && !Directory.Exists(b)) Directory.Move(a, b);
            }
            catch { }
        }

        static void NombresIngles(string d)
        {
            try
            {
                MoverViejo(d, "juegos", "games");
                foreach (string dir in new[] { d, Path.Combine(d, "games") })
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        string n = Path.GetFileName(f), m = NombreIngles(n);
                        if (m == n) continue;
                        string dst = Path.Combine(dir, m);
                        try { if (!File.Exists(dst)) File.Move(f, dst); } catch { }
                    }
                }
            }
            catch { }
        }

        // 21-11 (1.0.7, el usuario: "en ConsoleEnhancer no se guardan perfiles por juego"): con portable.txt cada version se descomprime
        // en su carpeta (Downloads\ConsoleEnhancer-v1.0.6...) y empezaba sin nada: ni el ini (asistente otra vez), ni games\profiles.txt,
        // ni games\console.txt (sin el, consola generica: un solo perfil "Consola" hasta ver el menu de la PS5). Si los datos de ahora no
        // tienen ini, se copian los de la version anterior: de cada fichero, el mas nuevo de las carpetas ConsoleEnhancer* de al lado y de
        // %LOCALAPPDATA%\ConsoleEnhancer (los de data\ y data\games\; no logs ni cache). Se apunta en data\inherited.txt
        public const string HeredadoTxt = "inherited.txt";
        // 22-11 (1.2.1): estado de un momento que no vale para otra version ni se guarda en la copia: el plan de energia de antes de la espera
        // (standby_plan.txt, espera_plan.txt hasta la 1.2.05). Copiado mientras el PC estaba en espera, una version nueva lo heredaba y al
        // "volver" de una espera que no habia ponia un plan viejo
        static bool EstadoPasajero(string n)
        {
            return n.Equals("standby_plan.txt", StringComparison.OrdinalIgnoreCase) || n.Equals("espera_plan.txt", StringComparison.OrdinalIgnoreCase);
        }
        static void HeredarDatos(string d, string appDir)
        {
            try
            {
                if (File.Exists(Path.Combine(d, "consoleenhancer.ini"))) return;
                // (1.0.8) primero las versiones de al lado (portables: la de antes de verdad); %LOCALAPPDATA% solo si no hay ninguna (en la
                // 1.0.7 se mezclaban y ganaba un %LOCALAPPDATA% viejo de otra instalacion con un perfil malo)
                List<string> fuentes = new List<string>();
                string padre = Path.GetDirectoryName(appDir.TrimEnd('\\'));
                if (!string.IsNullOrEmpty(padre) && Directory.Exists(padre))
                    foreach (string dir in Directory.GetDirectories(padre, "ConsoleEnhancer*"))
                        if (!string.Equals(dir.TrimEnd('\\'), appDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                            foreach (string sd in new[] { "data", "datos" })   // (1.2.06: hasta la 1.2.05, datos\)
                                if (File.Exists(Path.Combine(dir, sd + @"\consoleenhancer.ini"))) { fuentes.Add(Path.Combine(dir, sd)); break; }
                // (1.2.07, el usuario: "otra vez me meto a un juego y no pone su nombre"): borrada la carpeta de la version anterior, la
                // nueva empezaba sin games\console.txt (consola generica: sin nombres de juegos) ni perfiles. La copia de seguridad que
                // guarda cada version en %LOCALAPPDATA%\ConsoleEnhancer\backup (CopiaSeguridad) va antes que el %LOCALAPPDATA% viejo
                if (fuentes.Count == 0 && File.Exists(Path.Combine(CopiaDir, "consoleenhancer.ini"))) fuentes.Add(CopiaDir);
                if (fuentes.Count == 0) fuentes.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConsoleEnhancer"));
                string yo = Path.GetFullPath(d).TrimEnd('\\');
                Dictionary<string, string> mejor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in fuentes)
                {
                    if (string.Equals(Path.GetFullPath(f).TrimEnd('\\'), yo, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(Path.Combine(f, "consoleenhancer.ini"))) continue;
                    foreach (string sub in new[] { "", "games", "juegos" })
                    {
                        string dir = sub.Length == 0 ? f : Path.Combine(f, sub);
                        if (!Directory.Exists(dir)) continue;
                        foreach (string a in Directory.GetFiles(dir))
                        {
                            string n = Path.GetFileName(a);
                            if (n.StartsWith(".") || n.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || n.Equals(HeredadoTxt, StringComparison.OrdinalIgnoreCase)) continue;
                            // (lo elegido con L3 en versiones de antes era Smooth Motion de serie en RTX 40: desde la 1.0.7, DLSS FG del plan)
                            if (n.IndexOf(".antes-", StringComparison.OrdinalIgnoreCase) >= 0 || n.EndsWith(".anterior", StringComparison.OrdinalIgnoreCase)) continue;   // (copias)
                            n = NombreIngles(n);   // (1.2.06: los nombres de antes, en espanol, con el de ahora)
                            if (n.Equals("framegen_type.txt", StringComparison.OrdinalIgnoreCase) || n.Equals(HeredadoTxt, StringComparison.OrdinalIgnoreCase) || EstadoPasajero(n)) continue;
                            string k = sub.Length == 0 ? n : "games\\" + n;
                            string ya;
                            if (!mejor.TryGetValue(k, out ya) || File.GetLastWriteTimeUtc(a) > File.GetLastWriteTimeUtc(ya)) mejor[k] = a;
                        }
                    }
                }
                if (!mejor.ContainsKey("consoleenhancer.ini")) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Datos heredados de la version anterior (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "):");
                foreach (KeyValuePair<string, string> kv in mejor)
                {
                    string dst = Path.Combine(d, kv.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (!File.Exists(dst)) { File.Copy(kv.Value, dst, false); sb.AppendLine(kv.Key + " <- " + kv.Value); }
                }
                // (1.0.8, el usuario: "tarda mas en abrirse la app que Prisma"): el primer arranque de cada version bajaba otra vez mpv y
                // Magpie (37 s) y compilaba todos los shaders del motor (~18 s: la cache estaba vacia). De la carpeta de la que sale el ini:
                // la cache de shaders (solo cambian los que se han tocado: la clave es el texto) y mpv\ y magpie\ si aqui no estan
                // (Componentes mira su .version: si es otra version, los baja igual)
                string de = Path.GetDirectoryName(mejor["consoleenhancer.ini"]);
                int cso = 0;
                string cache = Path.Combine(de, @"logs\cache");
                if (Directory.Exists(cache))
                {
                    string dc = Path.Combine(d, @"logs\cache");
                    Directory.CreateDirectory(dc);
                    foreach (string a in Directory.GetFiles(cache, "*.cso"))
                    {
                        string dst = Path.Combine(dc, Path.GetFileName(a));
                        if (!File.Exists(dst)) { File.Copy(a, dst, false); cso++; }
                    }
                }
                if (cso > 0) sb.AppendLine("logs\\cache: " + cso + " shaders compilados <- " + cache);
                string appDe = Path.GetDirectoryName(de.TrimEnd('\\'));
                string deNombre = Path.GetFileName(de.TrimEnd('\\'));
                if (deNombre.Equals("data", StringComparison.OrdinalIgnoreCase) || deNombre.Equals("datos", StringComparison.OrdinalIgnoreCase))
                {
                    // (1.2.4 ronda 6, QUITAFG: sin Magpie; de su carpeta de antes solo las DLL de Intel / AMD que usa el motor, a xess\ y fsr\)
                    foreach (string comp in new[] { "xess", "fsr" })   // (23-11, 1.2.4 SINMPV: sin mpv\)
                    {
                        string src = Path.Combine(appDe, comp), dst = Path.Combine(appDir, comp);
                        if (!File.Exists(Path.Combine(src, ".version")) || File.Exists(Path.Combine(dst, ".version"))) continue;
                        int n = CopiarCarpeta(src, dst);
                        sb.AppendLine(comp + "\\: " + n + " ficheros <- " + src);
                    }
                    string viejoMag = Path.Combine(appDe, "magpie");
                    if (Directory.Exists(viejoMag)) { Componentes.RecogerDe(viejoMag); sb.AppendLine("xess\\ / fsr\\: las DLL de Intel / AMD <- " + viejoMag + " (si faltaban)"); }
                }
                File.WriteAllText(Path.Combine(d, HeredadoTxt), sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        // (1.2.07) Copia de seguridad de los datos fuera de la carpeta de la app (cada version portable vive en la suya y el usuario borra
        // las viejas): el ini y data\ + data\games\ (sin logs ni cache) en %LOCALAPPDATA%\ConsoleEnhancer\backup, al abrir y cada 2 min
        // (solo lo que ha cambiado). HeredarDatos la usa si no hay otra version al lado; Consolas lee de ahi console.txt si falta
        public static string CopiaDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ConsoleEnhancer\backup"); } }
        static System.Threading.Timer copiaTimer;
        public static void CopiaSeguridad(string d)
        {
            if (copiaTimer != null || string.IsNullOrEmpty(d)) return;
            // (pruebas con otra carpeta de datos o simulando otro PC: nada. 1.2.2: tampoco en la espera ni en sus procesos de ayuda (--espera,
            // --ce-sonda, --ce-guarda-barra...): con la app cerrada no cambia nada y la copia despertaba el disco y la CPU cada 2 min)
            foreach (string a in Environment.GetCommandLineArgs())
                if (a.StartsWith("--datos=", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--simula", StringComparison.OrdinalIgnoreCase) ||
                    a.StartsWith("--prueba", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--test", StringComparison.OrdinalIgnoreCase) ||
                    // (22-11, 1.2.2, ERRORES y ESPERA: ni el proceso de la espera ni sus ayudantes -la guarda de la barra, la sonda...-: la copia
                    // la lleva la app; la espera la hacia cada 2 min toda la noche, contra lo que dicen Espera.cs y EsperaCubierta.cs)
                    a == "--espera" || a.StartsWith("--ce-", StringComparison.OrdinalIgnoreCase) ||
                    // (22-11, 1.2.2 REVISION: ni el residente del modo PC del mando, abierto toda la noche si se usa en vez de Joyxoff)
                    a == "--mando-pc") return;
            if (string.Equals(Path.GetFullPath(d).TrimEnd('\\'), Path.GetFullPath(CopiaDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
            copiaTimer = new System.Threading.Timer(delegate { CopiaAhora(d); }, null, 5000, 120000);
        }
        // (1.2.3, DEFENDER) Al desinstalar, antes de borrar (CeBorrado, src\Lanzador\Desinstalar.cs): ni otra copia a los 2 min ni una a medias
        public static void PararCopia()
        {
            System.Threading.Timer t = copiaTimer;
            if (t != null) t.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            for (int i = 0; i < 50 && System.Threading.Volatile.Read(ref copiando) == 1; i++) System.Threading.Thread.Sleep(100);
        }
        static int copiando;
        public static void CopiaAhora(string d)
        {
            if (System.Threading.Interlocked.Exchange(ref copiando, 1) == 1) return;
            try
            {
                if (!File.Exists(Path.Combine(d, "consoleenhancer.ini"))) return;
                foreach (string sub in new[] { "", "games" })
                {
                    string dir = sub.Length == 0 ? d : Path.Combine(d, sub);
                    if (!Directory.Exists(dir)) continue;
                    string dst = sub.Length == 0 ? CopiaDir : Path.Combine(CopiaDir, sub);
                    Directory.CreateDirectory(dst);
                    foreach (string a in Directory.GetFiles(dir))
                    {
                        string n = Path.GetFileName(a);
                        if (n.StartsWith(".") || n.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || n.Equals(HeredadoTxt, StringComparison.OrdinalIgnoreCase)) continue;
                        if (n.IndexOf(".antes-", StringComparison.OrdinalIgnoreCase) >= 0 || EstadoPasajero(n)) continue;
                        try
                        {
                            FileInfo fa = new FileInfo(a), fb = new FileInfo(Path.Combine(dst, n));
                            if (fb.Exists && fb.Length == fa.Length && fb.LastWriteTimeUtc >= fa.LastWriteTimeUtc) continue;
                            File.Copy(a, fb.FullName, true);
                        }
                        catch { }   // (un fichero abierto para escribir: la proxima vez)
                    }
                }
                // 22-11 (1.2.2, ERRORES): tambien los shaders ya compilados del motor (logs\cache\*.cso, ~0,5 MB). Sin ellos, una version nueva
                // con la anterior ya borrada (hereda de esta copia) compilaba todos al abrir el motor la primera vez (22-11 01:50: 25-35 s sin
                // imagen, y la captura segura lo tomo por "la capturadora no da 4K"). HeredarDatos ya los coge de <origen>\logs\cache
                string cache = Path.Combine(d, @"logs\cache");
                if (Directory.Exists(cache))
                {
                    string dc = Path.Combine(CopiaDir, @"logs\cache");
                    Directory.CreateDirectory(dc);
                    foreach (string a in Directory.GetFiles(cache, "*.cso"))
                        try
                        {
                            string b = Path.Combine(dc, Path.GetFileName(a));
                            if (!File.Exists(b)) File.Copy(a, b, false);   // (la clave es el texto del shader: el mismo nombre, lo mismo)
                        }
                        catch { }
                }
            }
            catch { }
            finally { System.Threading.Interlocked.Exchange(ref copiando, 0); }
        }

        // 22-11 (1.2.2, ERRORES): un fichero de estado que la app borra al acabar con el (data\picture_restore.txt: lo cambiado en el HDR /
        // los perfiles de NVIDIA hasta devolverlo) se queda en la copia si se copio mientras existia: la copia no borra nada. Una version
        // nueva lo heredaba al abrirse por primera vez y "devolvia" una sesion que ya se habia cerrado bien (22-11 01:50:04, con la copia
        // del 21-11 19:10). Al borrarlo de data\, fuera tambien de la copia (si otra carpeta de la app tuviera el suyo, lo sigue teniendo
        // en su data\ y lo devuelve ella al abrirse)
        public static void QuitarDeLaCopia(string nombre)
        {
            if (copiaTimer == null) return;   // (solo quien lleva la copia: no en pruebas con --datos=, --test..., ni sin la copia en marcha)
            try
            {
                string b = Path.Combine(CopiaDir, nombre);
                if (File.Exists(b)) File.Delete(b);
            }
            catch { }
        }

        static int CopiarCarpeta(string src, string dst)
        {
            int n = 0;
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
            {
                string t = Path.Combine(dst, Path.GetFileName(f));
                if (!File.Exists(t)) { File.Copy(f, t, false); n++; }
            }
            foreach (string s in Directory.GetDirectories(src)) n += CopiarCarpeta(s, Path.Combine(dst, Path.GetFileName(s)));
            return n;
        }

        // ---- Ruta del motor (al leer el ini: Config.Load) ----
        // (23-11, 1.2.4 SINMPV: sin mpv; mpv_path del ini ya no se usa para nada)
        public static string AppEngine { get { return Path.Combine(Paths.AppDir, @"engine\ConsoleEnhancerEngine.exe"); } }

        static int pathsLogged;

        public static void ResolvePaths(Config c)
        {
            string engIni = c.EnginePath;
            c.EnginePath = Pick(c.AppFolders, AppEngine, c.EnginePath, DefaultEngine);
            if (Interlocked.Exchange(ref pathsLogged, 1) == 0)
                Log.Write("Rutas: app " + Paths.AppDir +
                          ", motor " + c.EnginePath + (string.Equals(c.EnginePath, engIni, StringComparison.OrdinalIgnoreCase) ? "" : " (ini: " + engIni + ")"));
        }

        // El de dentro de la app si existe (y se quiere); si no, el del ini; si ese no existe, el de dentro o el de siempre
        static string Pick(bool inside, string own, string ini, string def)
        {
            try
            {
                if (inside && File.Exists(own)) return own;
                if (!string.IsNullOrEmpty(ini) && File.Exists(ini)) return ini;
                if (File.Exists(own)) return own;
                if (File.Exists(def)) return def;
            }
            catch { }
            return string.IsNullOrEmpty(ini) ? def : ini;
        }

        public static string Describe(Config c)
        {
            return "app " + Paths.AppDir + (Dlss5Available ? ", DLSS 5 en " + Dlss5Dir : ", sin " + Dlss5Dll + ": " + Dlss5MissingText) +
                   ", XeSS " + (Paths.HayLib("libxess_fg.dll") ? Paths.XessDir : "NO") + ", FSR " + (Paths.HayLib("amd_fidelityfx_loader_dx12.dll") ? Paths.FsrDir : "NO") +
                   ", motor " + c.EnginePath;
        }

        // ---- DLSS 5 (el filtro DLSSNR de Magpie) disponible: esta su DLL ----
        static volatile int dlss5State; // 0 sin mirar, 1 si, -1 no
        public static bool Dlss5Available
        {
            get
            {
                if (dlss5State == 0) dlss5State = File.Exists(Path.Combine(Dlss5Dir, Dlss5Dll)) ? 1 : -1;
                return dlss5State > 0;
            }
        }
        // (1.2.4 ronda 6, QUITAFG, fase 2) Donde esta la DLL del usuario para el motor (dlss5_dir=): extras\ (la pone "Añadir DLSS 5"); de
        // una instalacion de antes, a mano en magpie\
        public static string Dlss5Dir
        {
            get
            {
                string ex = Path.Combine(Paths.AppDir, "extras");
                if (!File.Exists(Path.Combine(ex, Dlss5Dll)) && File.Exists(Path.Combine(Paths.MagpieDir, Dlss5Dll))) return Paths.MagpieDir;
                return ex;
            }
        }
        // (20-11, PUBLICAR) el usuario ha puesto o quitado la DLL con el boton (Extras.cs): mirarlo otra vez
        public static void Dlss5Rever() { dlss5State = 0; }
        // (1.1.2-1.2.4: MagpieDlssgOk, el runtime dlssg_sm86 que pedia el DLSS FG de Magpie. 1.2.4 ronda 6, QUITAFG: sin DLSS FG, fuera)

        // (18-11 a 1.2.4: la copia de Magpie de la app -PrepareMagpie, Update, CheckManifest, MagpieVersion...-; 1.2.4 ronda 6, QUITAFG, fase 2:
        // sin Magpie, fuera)
    }

    // (1.2.4 ronda 6, QUITAFG, fase 2: aqui iba MagpieOculto -el icono de Magpie en la bandeja, fuera-; sin Magpie, fuera)
}
