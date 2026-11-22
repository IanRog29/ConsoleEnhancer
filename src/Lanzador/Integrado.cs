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
//  - mpv\mpv.exe y engine\Switch2Engine.exe dentro de la app si existen (usar_carpetas_app=1), con las rutas del ini de respaldo
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
        // ConsoleEnhancer (fase 2, UNIVERSAL): sin rutas de ningun PC. La app es la carpeta del exe; mpv y el motor van dentro
        public const string DefaultMpv = "";
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
                    foreach (string comp in new[] { "mpv", "magpie" })
                    {
                        string src = Path.Combine(appDe, comp), dst = Path.Combine(appDir, comp);
                        if (!File.Exists(Path.Combine(src, ".version")) || File.Exists(Path.Combine(dst, ".version"))) continue;
                        int n = CopiarCarpeta(src, dst);
                        sb.AppendLine(comp + "\\: " + n + " ficheros <- " + src);
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
            foreach (string a in Environment.GetCommandLineArgs())   // (pruebas con otra carpeta de datos o simulando otro PC: nada)
                if (a.StartsWith("--datos=", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--simula", StringComparison.OrdinalIgnoreCase) ||
                    a.StartsWith("--prueba", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--test", StringComparison.OrdinalIgnoreCase) ||
                    // (22-11, 1.2.2, ERRORES: ni el proceso de la espera ni la guarda de la barra: la copia la lleva la app; la espera la hacia cada
                    // 2 min toda la noche, contra lo que dicen Espera.cs y EsperaCubierta.cs)
                    a == "--espera" || a == "--ce-guarda-barra") return;
            if (string.Equals(Path.GetFullPath(d).TrimEnd('\\'), Path.GetFullPath(CopiaDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
            copiaTimer = new System.Threading.Timer(delegate { CopiaAhora(d); }, null, 5000, 120000);
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

        // ---- Rutas de mpv y del motor (al leer el ini: Config.Load) ----
        public static string AppMpv { get { return Path.Combine(Paths.AppDir, @"mpv\mpv.exe"); } }
        public static string AppEngine { get { return Path.Combine(Paths.AppDir, @"engine\ConsoleEnhancerEngine.exe"); } }

        // magpie_actualizar (Config.MagpieUpdate): 0 nunca mira el origen, 1 coge del origen solo lo que sea mas nuevo, 2 lo deja igual que el origen
        public static volatile int MagpieUpdate = 1;
        static int pathsLogged;

        public static void ResolvePaths(Config c)
        {
            MagpieUpdate = c.MagpieUpdate;
            string mpvIni = c.MpvPath, engIni = c.EnginePath;
            c.MpvPath = Pick(c.AppFolders, AppMpv, c.MpvPath, DefaultMpv);
            c.EnginePath = Pick(c.AppFolders, AppEngine, c.EnginePath, DefaultEngine);
            if (Interlocked.Exchange(ref pathsLogged, 1) == 0)
                Log.Write("Rutas: app " + Paths.AppDir + ", Magpie " + Paths.MagpieDir + ", mpv " + c.MpvPath +
                          (string.Equals(c.MpvPath, mpvIni, StringComparison.OrdinalIgnoreCase) ? "" : " (ini: " + mpvIni + ")") +
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
            return "app " + Paths.AppDir + ", Magpie " + Paths.MagpieDir + " (" + MagpieVersion(Paths.MagpieDir) + (Dlss5Available ? "" : ", sin " + Dlss5Dll + ": " + Dlss5MissingText) +
                   "), origen " + (string.IsNullOrEmpty(c.MagpieSource) ? "ninguno" : c.MagpieSource + (File.Exists(Path.Combine(c.MagpieSource, "Magpie.exe")) ? "" : " (no esta)")) +
                   ", actualizar " + (MagpieUpdate <= 0 ? "nunca" : MagpieUpdate == 1 ? "lo mas nuevo" : "igual que el origen") +
                   ", mpv " + c.MpvPath + ", motor " + c.EnginePath;
        }

        // ---- DLSS 5 (el filtro DLSSNR de Magpie) disponible: esta su DLL ----
        static volatile int dlss5State; // 0 sin mirar, 1 si, -1 no
        public static bool Dlss5Available
        {
            get
            {
                if (dlss5State == 0) dlss5State = File.Exists(Path.Combine(Paths.MagpieDir, Dlss5Dll)) ? 1 : -1;
                return dlss5State > 0;
            }
        }
        // (20-11, PUBLICAR) el usuario ha puesto o quitado la DLL con el boton (Extras.cs): mirarlo otra vez
        public static void Dlss5Rever() { dlss5State = 0; dlssgState = 0; }

        // (1.1.2) El DLSS FG de Magpie (el de la cadena con DLSS 5) va SIEMPRE por runtimes\dlssg_sm86 de la comunidad, tambien en RTX 40:
        // sin el, "DLSSFG preparation failed: community package missing ... No native fallback" y Magpie no escala nunca (21-11 15:05: modo
        // 30 con DLSS 5 y DLSS FG, 4 reaperturas y la imagen parada). Sin el, con DLSS 5 la generacion va por otra (Config.FgTypeNow)
        static volatile int dlssgState;
        public static bool MagpieDlssgOk
        {
            get
            {
                if (dlssgState == 0) dlssgState = File.Exists(Path.Combine(Paths.MagpieDir, @"runtimes\dlssg_sm86\version.dll")) ? 1 : -1;
                return dlssgState > 0;
            }
        }

        // ---- La copia de Magpie de la app ----
        // Lo que nunca viene del origen: la config, los logs y la cache de efectos son de la app; Updater.exe no hace falta; los logs de
        // NGX junto al exe; restos de una copia a medias
        static bool Skip(string rel)
        {
            string top = rel.Split('\\')[0].ToLowerInvariant();
            if (top == "config" || top == "logs" || top == "cache" || top == "update") return true;
            if (rel.IndexOf('\\') < 0 && (rel.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || rel.Equals("Updater.exe", StringComparison.OrdinalIgnoreCase) ||
                                         rel.Equals("update.zip", StringComparison.OrdinalIgnoreCase))) return true;
            return rel.EndsWith(TmpExt, StringComparison.OrdinalIgnoreCase);
        }

        // Los que un paquete para repartir no lleva (scripts\empaquetar.ps1): no cuentan como "falta" en el manifiesto. Sin firma de NVIDIA:
        // nvngx_dlssnr.dll (modificada, sin firma) y runtimes\dlssg_sm86 (de la comunidad: Magpie lo copia a %LOCALAPPDATA%\Magpie\DlssgSm86)
        static bool Optional(string rel)
        {
            return rel.Equals(Dlss5Dll, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(@"runtimes\dlssg_sm86", StringComparison.OrdinalIgnoreCase) ||
                   // (ConsoleEnhancer: las DLL de NVIDIA VFX, ~480 MB, van aparte: empaquetar.ps1 -IncluirVfx)
                   Regex.IsMatch(rel, @"^(nvVFX.*\.dll|NVVideoEffects\.dll|NVCVImage\.dll|npp.*64_12\.dll|cudart64_12\.dll|nvidia-vfx-runtime-manifest\.json)$", RegexOptions.IgnoreCase) ||
                   rel.StartsWith(@"NVIDIA-VFX-Licenses", StringComparison.OrdinalIgnoreCase);
        }

        const string TmpExt = ".s2nuevo";

        // (Pack = 4: los FILETIME de Windows van alineados a 4; con 8 se leia el tamaño como numero de enlaces)
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct BY_HANDLE_FILE_INFORMATION
        {
            public uint attrs;
            public long created, accessed, written;
            public uint volume, sizeHigh, sizeLow, links, indexHigh, indexLow;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle h, out BY_HANDLE_FILE_INFORMATION info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

        // Cuantos nombres (enlaces duros) tiene el fichero. Abierto solo para leer sus atributos (sin leer datos: el antivirus no lo
        // analiza al abrirlo; con lectura normal, la primera vez tras copiarlos eran ~23 s para los ~300)
        static int Links(string path)
        {
            try
            {
                using (Microsoft.Win32.SafeHandles.SafeFileHandle h = CreateFileW(path, 0x80 /* FILE_READ_ATTRIBUTES */, 7, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) return 1;
                    BY_HANDLE_FILE_INFORMATION i;
                    return GetFileInformationByHandle(h, out i) ? (int)i.links : 1;
                }
            }
            catch { return 1; }
        }

        // Pone 'from' en 'dst' como fichero propio (copia; con la misma fecha). Por un temporal al lado: si se corta, la vez siguiente
        // se termina (Prepare, al empezar)
        static void CopyInto(string from, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            string tmp = dst + TmpExt;
            DateTime when = File.GetLastWriteTimeUtc(from);
            File.Copy(from, tmp, true);
            File.SetLastWriteTimeUtc(tmp, when);
            if (File.Exists(dst)) File.Delete(dst); // (un enlace: solo se quita este nombre, el original sigue igual)
            File.Move(tmp, dst);
        }

        static void CloseForChange(string why)
        {
            if (MagpieLive.Running) MagpieLive.Close(why); // (con el abierto no se pueden cambiar sus ficheros)
        }

        public static string MagpieVersion(string dir)
        {
            try
            {
                string m = Path.Combine(dir, "build-manifest.json");
                if (File.Exists(m))
                {
                    Match v = Regex.Match(File.ReadAllText(m, Encoding.UTF8), "\"version\"\\s*:\\s*\"([^\"]+)\"");
                    if (v.Success) return "v" + v.Groups[1].Value;
                }
                string exe = Path.Combine(dir, "Magpie.exe");
                if (File.Exists(exe)) return "v" + FileVersionInfo.GetVersionInfo(exe).FileVersion;
            }
            catch { }
            return "version ?";
        }

        // Deja lista la copia de la app: ficheros propios, actualizada desde el origen si toca y comprobada con su build-manifest.json.
        // true si hay Magpie.exe (lo que no se haya podido arreglar queda en el log). Con Magpie abierto lo cierra solo si hay que cambiar algo
        public static bool PrepareMagpie(string source, out string detail)
        {
            string dir = Paths.MagpieDir;
            List<string> notes = new List<string>();
            Directory.CreateDirectory(dir);
            int failed = 0;

            // 0. Restos de una vez anterior cortada a medias
            try
            {
                foreach (string t in Directory.GetFiles(dir, "*" + TmpExt, SearchOption.AllDirectories))
                {
                    string f = t.Substring(0, t.Length - TmpExt.Length);
                    try { if (!File.Exists(f)) File.Move(t, f); else File.Delete(t); } catch { }
                }
            }
            catch { }

            // 1. Ficheros propios: los que aun son enlaces duros (al Magpie del usuario) se copian. Una vez (~1 GB)
            List<string> linked = new List<string>();
            long linkedBytes = 0;
            Stopwatch scan = Stopwatch.StartNew();
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string top = f.Substring(dir.Length + 1).Split('\\')[0].ToLowerInvariant();
                if (top == "config" || top == "logs") continue;
                if (Links(f) > 1) { linked.Add(f); linkedBytes += new FileInfo(f).Length; }
            }
            if (scan.ElapsedMilliseconds > 300) notes.Add("mirar los enlaces: " + scan.ElapsedMilliseconds + " ms");
            if (linked.Count > 0)
            {
                Stopwatch sw = Stopwatch.StartNew();
                CloseForChange("separar sus ficheros de los del Magpie instalado");
                int ok = 0;
                foreach (string f in linked)
                {
                    try { CopyInto(f, f); ok++; }
                    catch (Exception ex) { if (++failed <= 3) Log.Write("Aviso: Magpie de la app, separando " + f.Substring(dir.Length + 1) + ": " + ex.Message); }
                }
                notes.Add(ok + " de " + linked.Count + " ficheros que eran enlaces duros ya son propios (" + (linkedBytes >> 20) + " MB, " + sw.ElapsedMilliseconds + " ms)");
            }

            // 2. Updater.exe fuera: el fork no busca actualizaciones (quitado al compilar) y la app no lo quiere
            try
            {
                string up = Path.Combine(dir, "Updater.exe");
                if (File.Exists(up)) { CloseForChange("quitar su Updater.exe"); File.Delete(up); notes.Add("quitado Updater.exe"); }
            }
            catch (Exception ex) { notes.Add("Updater.exe sin quitar (" + ex.Message + ")"); }

            // 3. Actualizar desde el origen (opcional)
            string srcRoot = null;
            try
            {
                if (MagpieUpdate <= 0) notes.Add("origen: no se mira (magpie_actualizar=0)");
                else if (string.IsNullOrEmpty(source)) notes.Add("sin origen (no hace falta)");
                else if (!File.Exists(Path.Combine(source, "Magpie.exe"))) notes.Add("origen " + source + " no esta (no hace falta: va con la suya)");
                else if (string.Equals(Path.GetFullPath(source).TrimEnd('\\'), Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) notes.Add("origen = la propia copia");
                else
                {
                    srcRoot = Path.GetFullPath(source).TrimEnd('\\');
                    string upd = Update(srcRoot, dir, ref failed);
                    if (upd != null) notes.Add(upd);
                }
            }
            catch (Exception ex) { failed++; notes.Add("actualizacion fallida, sigue la copia de antes (" + ex.Message + ")"); }

            // 4. Comprobada con su manifiesto (lo que el paquete de Magpie dice que lleva): lo que falte se coge del origen si se puede
            try { string chk = CheckManifest(dir, srcRoot, ref failed); if (chk != null) notes.Add(chk); }
            catch (Exception ex) { notes.Add("manifiesto sin comprobar (" + ex.Message + ")"); }

            Directory.CreateDirectory(Path.Combine(dir, "logs"));
            Directory.CreateDirectory(Path.Combine(dir, @"config\v4e"));
            dlss5State = 0;
            if (!Dlss5Available) notes.Add("sin " + Dlss5Dll + ": " + Dlss5MissingText + " (la app va sin el)");
            // (el fork hace SIEMPRE el DLSS FG de Magpie por este runtime de la comunidad, tambien en RTX 40: sin el "No native fallback")
            if (!File.Exists(Path.Combine(dir, @"runtimes\dlssg_sm86\version.dll"))) notes.Add(@"sin runtimes\dlssg_sm86: la generacion DLSS FG de Magpie no va (Smooth Motion y la del motor propio si)");
            bool exists = File.Exists(Path.Combine(dir, "Magpie.exe"));
            detail = MagpieVersion(dir) + (exists ? "" : ", NO ESTA Magpie.exe") + (notes.Count > 0 ? "; " + string.Join("; ", notes.ToArray()) : "; al dia") +
                     (failed > 0 ? " (" + failed + " fallos)" : "");
            return exists;
        }

        // Los ficheros del origen que tocan (mas nuevos, o distintos con magpie_actualizar=2) se copian primero aparte (magpie.nuevo junto
        // a magpie) y solo si estan todos se ponen en su sitio: un fallo a medias no deja una copia mezclada
        static string Update(string srcRoot, string dir, ref int failed)
        {
            List<string> rels = new List<string>();
            long bytes = 0;
            foreach (string f in Directory.GetFiles(srcRoot, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(srcRoot.Length + 1);
                if (Skip(rel)) continue;
                FileInfo s = new FileInfo(f), d = new FileInfo(Path.Combine(dir, rel));
                bool take = !d.Exists || (MagpieUpdate >= 2 ? s.Length != d.Length || s.LastWriteTimeUtc != d.LastWriteTimeUtc : s.LastWriteTimeUtc > d.LastWriteTimeUtc);
                if (take) { rels.Add(rel); bytes += s.Length; }
            }
            if (rels.Count == 0) return "origen " + srcRoot + " (" + MagpieVersion(srcRoot) + "): nada mas nuevo";
            Stopwatch sw = Stopwatch.StartNew();
            string stage = dir.TrimEnd('\\') + ".nuevo";
            try
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                foreach (string rel in rels)
                {
                    string to = Path.Combine(stage, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(Path.Combine(srcRoot, rel), to, true);
                    File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(Path.Combine(srcRoot, rel)));
                }
            }
            catch (Exception ex)
            {
                try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
                failed++;
                return "actualizacion desde " + srcRoot + " fallida al copiar, sigue la copia de antes (" + ex.Message + ")";
            }
            string before = MagpieVersion(dir);
            CloseForChange("actualizar su copia desde " + srcRoot);
            int ok = 0;
            foreach (string rel in rels)
            {
                try
                {
                    string dst = Path.Combine(dir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(Path.Combine(stage, rel), dst);
                    ok++;
                }
                catch (Exception ex) { if (++failed <= 3) Log.Write("Aviso: Magpie de la app, actualizando " + rel + ": " + ex.Message); }
            }
            try { Directory.Delete(stage, true); } catch { }
            return "actualizado desde " + srcRoot + ": " + ok + " de " + rels.Count + " ficheros (" + (bytes >> 20) + " MB, " + sw.ElapsedMilliseconds + " ms), " + before + " -> " + MagpieVersion(dir);
        }

        static string CheckManifest(string dir, string srcRoot, ref int failed)
        {
            string mf = Path.Combine(dir, "build-manifest.json");
            if (!File.Exists(mf)) return "sin build-manifest.json (no se comprueba)";
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = js.DeserializeObject(File.ReadAllText(mf, Encoding.UTF8)) as Dictionary<string, object>;
            object fo;
            if (root == null || !root.TryGetValue("files", out fo) || !(fo is object[])) return "build-manifest.json sin lista de ficheros";
            List<string> missing = new List<string>(), changed = new List<string>(), optionalOut = new List<string>();
            int total = 0;
            foreach (object o in (object[])fo)
            {
                Dictionary<string, object> e = o as Dictionary<string, object>;
                if (e == null || !e.ContainsKey("path")) continue;
                string rel = ((string)e["path"]).Replace('/', '\\');
                if (Skip(rel)) continue; // (Updater.exe: fuera a proposito)
                long want = e.ContainsKey("bytes") && e["bytes"] != null ? Convert.ToInt64(e["bytes"]) : -1;
                total++;
                FileInfo d = new FileInfo(Path.Combine(dir, rel));
                if (!d.Exists)
                {
                    // Del origen si lo tiene igual que el manifiesto
                    FileInfo s = srcRoot != null ? new FileInfo(Path.Combine(srcRoot, rel)) : null;
                    if (s != null && s.Exists && (want < 0 || s.Length == want) && !Optional(rel))
                    {
                        try { CloseForChange("reponer ficheros que le faltan"); CopyInto(s.FullName, d.FullName); continue; }
                        catch (Exception ex) { failed++; Log.Write("Aviso: Magpie de la app, reponiendo " + rel + ": " + ex.Message); }
                    }
                    if (Optional(rel)) optionalOut.Add(rel); else missing.Add(rel);
                }
                else if (want >= 0 && d.Length != want) changed.Add(rel);
            }
            StringBuilder sb = new StringBuilder("manifiesto: " + total + " ficheros");
            if (missing.Count == 0 && changed.Count == 0) sb.Append(" bien");
            if (missing.Count > 0) sb.Append(", FALTAN ").Append(missing.Count).Append(" (").Append(string.Join(", ", missing.GetRange(0, Math.Min(5, missing.Count)).ToArray())).Append(missing.Count > 5 ? "..." : "").Append(")");
            if (changed.Count > 0) sb.Append(", ").Append(changed.Count).Append(" de otro tamaño (cambiados a mano: ").Append(string.Join(", ", changed.GetRange(0, Math.Min(5, changed.Count)).ToArray())).Append(")");
            if (optionalOut.Count > 0) sb.Append(", sin los opcionales (").Append(optionalOut.Count).Append(")");
            return sb.ToString();
        }
    }

    // ===================================================================
    //  El icono de Magpie en la bandeja, fuera. Magpie solo arranca sin su ventana principal (-t) si lleva el icono (showNotifyIcon: sin
    //  el ignora -t y abre su ventana), asi que se le quita desde fuera: Shell_NotifyIcon(NIM_DELETE) con su ventana Magpie_NotifyIcon y
    //  su id (0, sin GUID: NotifyIconService.cpp del fork). Magpie no se entera (cree que lo tiene) y no lo vuelve a poner salvo si el
    //  explorador se reinicia (TaskbarCreated): por eso un repaso. Tampoco quedan iconos fantasma al matarlo (la app lo mata siempre)
    // ===================================================================
    static class MagpieOculto
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATAW
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID, uFlags, uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState, dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct NOTIFYICONIDENTIFIER { public int cbSize; public IntPtr hWnd; public int uID; public Guid guidItem; }
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int l, t, r, b; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIconW(int msg, ref NOTIFYICONDATAW d);
        [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        public const string IconClass = "Magpie_NotifyIcon";
        const int NIM_DELETE = 2;
        static int watching, logged;
        static volatile int startedTick = Environment.TickCount - 600000;
        static readonly HashSet<IntPtr> deleted = new HashSet<IntPtr>();
        static int lastBlind = Environment.TickCount;

        // ¿Esta puesto el icono de esa ventana? (1 si, 0 no, -1 no se sabe)
        public static int Present(IntPtr h)
        {
            try
            {
                NOTIFYICONIDENTIFIER id = new NOTIFYICONIDENTIFIER();
                id.cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
                id.hWnd = h;
                RECT r;
                int hr = Shell_NotifyIconGetRect(ref id, out r);
                return hr == 0 ? 1 : 0;
            }
            catch { return -1; }
        }

        static bool Delete(IntPtr h)
        {
            NOTIFYICONDATAW d = new NOTIFYICONDATAW();
            d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATAW));
            d.hWnd = h;
            d.uID = 0;
            return Shell_NotifyIconW(NIM_DELETE, ref d);
        }

        // Las ventanas del icono de un Magpie.exe que hay ahora
        public static List<IntPtr> IconWindows()
        {
            List<IntPtr> l = new List<IntPtr>();
            foreach (IntPtr parent in new IntPtr[] { IntPtr.Zero, new IntPtr(-3) }) // (normales y "solo mensajes", HWND_MESSAGE)
                for (IntPtr h = FindWindowEx(parent, IntPtr.Zero, IconClass, null); h != IntPtr.Zero; h = FindWindowEx(parent, h, IconClass, null))
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (MagpieLive.IsMagpiePid(pid, true) && !l.Contains(h)) l.Add(h);
                }
            return l;
        }

        // Quita ya el icono de los Magpie abiertos (la primera vez que se ve cada ventana y siempre que se vea puesto). Devuelve cuantos
        public static int Hide(string how)
        {
            int n = 0;
            try
            {
                bool blind = unchecked(Environment.TickCount - lastBlind) > 10000; // (cada 10 s, por si no se pudiera mirar si esta)
                if (blind) lastBlind = Environment.TickCount;
                foreach (IntPtr h in IconWindows())
                {
                    bool fresh;
                    lock (deleted) { fresh = !deleted.Contains(h); if (deleted.Count > 64) deleted.Clear(); }
                    int present = Present(h);
                    if (!fresh && present == 0 && !blind) continue;
                    bool ok = Delete(h);
                    lock (deleted) deleted.Add(h);
                    if (ok)
                    {
                        n++;
                        if (Interlocked.Increment(ref logged) <= 6)
                            Log.Write("Icono de Magpie en la bandeja quitado (" + how + (present == 1 ? "" : present == 0 ? ", no se veia puesto" : "") + ")");
                    }
                }
            }
            catch (Exception ex) { if (Interlocked.Increment(ref logged) <= 6) Log.Error("Quitando el icono de Magpie de la bandeja", ex); }
            return n;
        }

        // Al abrir Magpie (MagpieLive.Start): repaso cada 50 ms los primeros 15 s (pone el icono justo despues de crear su ventana) y luego
        // cada 2 s (el explorador reiniciado se lo vuelve a pedir). Un hilo para toda la app
        public static void Watch()
        {
            startedTick = Environment.TickCount;
            if (Interlocked.Exchange(ref watching, 1) != 0) return;
            Thread t = new Thread(delegate ()
            {
                while (true)
                {
                    bool early = unchecked(Environment.TickCount - startedTick) < 15000;
                    try { Hide(early ? "al abrirse" : "repaso"); } catch { }
                    Thread.Sleep(early ? 50 : 2000);
                }
            });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }
    }
}
