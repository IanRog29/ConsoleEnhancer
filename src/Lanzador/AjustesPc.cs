// AjustesPc.cs - 23-11-2026 (1.3.1, octava ronda, agente AJUSTES). C# 5
//
// El usuario: "cuando alguien se descargue una nueva version que los ajustes que tenia se guarden (a no ser que tuviera alguna opcion que ya
// no haya en la nueva version, entonces se cambiara por otra cosa y ya). haz que se guarde en alguna carpeta del ordenador o algo las settings"
//
// Hasta la 1.3 el zip traia portable.txt: los datos (ini, games\, escalas, logs) iban en data\ de la carpeta de cada version, y una version
// nueva los heredaba de las carpetas ConsoleEnhancer* de al lado o de la copia de seguridad (Integrado.HeredarDatos / CopiaSeguridad).
// Descomprimida en otro sitio (Escritorio, otra unidad) y sin la copia, empezaba de cero. Desde la 1.3.1:
//  - LA CARPETA DEL PC: %LOCALAPPDATA%\ConsoleEnhancer\settings (PcBase\settings). Ahi viven el ini, games\ (perfiles por juego, consola,
//    lo elegido con L3 / el menu), las escalas aprendidas, el idioma, la huella del hardware... y tambien logs\ (y la cache de shaders del
//    motor, logs\cache: compartida, una version nueva no las vuelve a compilar). Es la misma para TODAS las versiones, se descompriman donde
//    se descompriman, y se escribe en cada cambio (es la carpeta de datos de la app: Paths.Datos). Por que ahi y no en la raiz de
//    %LOCALAPPDATA%\ConsoleEnhancer: la raiz ya tiene datos de versiones viejas (las de antes del 1.0.7 sin portable.txt) y en un PC de
//    desarrollo la usan las pruebas (--test-*); settings\ empieza limpia y la llena una migracion con fuente elegida (abajo). Y no en
//    Documentos / AppData\Roaming: %LOCALAPPDATA% es lo de Windows para datos de una app de este PC (la grafica, la capturadora y la tele
//    son de este PC; no se sincroniza con otros) y ya es donde estan backup\ y extras\.
//  - PORTABLE solo si se pide: un portable.txt junto al exe escrito por el usuario (vacio vale). El que traian los zip hasta la 1.3 (su
//    texto) no cuenta: es de una version de antes descomprimida encima. O si la carpeta del PC no se puede escribir: data\ junto al exe.
//  - VERSIONES: settings\version.txt = la version mas nueva que ha usado la carpeta. Una version MAS ANTIGUA (abierta despues) no la toca:
//    trabaja en una copia, settings-older\<su version>\ (al abrirse copia de settings\ lo que alli sea mas nuevo; lo que cambie en la copia
//    no vuelve). Asi un ini o un perfil con cosas que ella no conoce no se reescribe sin ellas.
//  - MIGRACION (una vez, si settings\ no tiene ini): de la carpeta de datos usada mas recientemente entre data\ de esta carpeta (una version
//    descomprimida encima de otra), data\ (o datos\) de las carpetas ConsoleEnhancer* de al lado, la copia de seguridad (backup\, que
//    escriben todas desde la 1.2.07 cada 2 min) y, si no hay nada de eso, la raiz de %LOCALAPPDATA%\ConsoleEnhancer. Se copia TAL CUAL esa
//    (no se mezclan ficheros de varias versiones), sin logs, copias (.antes-*), ni informes de pruebas; la cache de shaders tambien. Se apunta
//    en settings\inherited.txt y en el log, y se dice una vez en pantalla.
//  - LO QUE YA NO EXISTE (PonerAlDia, al abrir): un perfil o lo elegido con L3 con XeSS FG (quitada de momento) o FSR 3 FG (quitada en la
//    1.3) pasa al mas parecido de este PC (en NVIDIA RTX 40 / 50, Smooth Motion; los dos eran x2: el mismo multiplicador); DLSS FG / VFG de
//    antes, igual. El resto de cada perfil no cambia. Al log, y una vez en pantalla (corto).
// Pruebas sin tocar los datos de verdad: CE_PC_DIR=<carpeta> en lugar de %LOCALAPPDATA%\ConsoleEnhancer (tambien backup\ y extras\), o
// --datos=<carpeta> (todo en esa, sin migracion). Las pruebas (--test-*, --prueba*, --simula*, --ui-preview*...) sin --datos= siguen en la
// raiz de %LOCALAPPDATA%\ConsoleEnhancer como hasta ahora: nunca en settings\. --test-ajustes[=<informe>][;<carpeta>]: los casos sobre copias.
// --ajustes-donde[=<informe>]: la carpeta elegida (y la migracion, si toca) y salir, con la logica de verdad (para probar con CE_PC_DIR).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace ConsoleEnhancer
{
    static class AjustesPc
    {
        // ---------------------------------------------------------------- donde
        // %LOCALAPPDATA%\ConsoleEnhancer (o CE_PC_DIR en pruebas); null si Windows no da la carpeta
        public static string PcBase
        {
            get
            {
                string e = Environment.GetEnvironmentVariable("CE_PC_DIR");
                if (!string.IsNullOrEmpty(e)) { try { return Path.GetFullPath(e).TrimEnd('\\'); } catch { } }
                string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return string.IsNullOrEmpty(la) ? null : Path.Combine(la, "ConsoleEnhancer");
            }
        }
        public static bool PcDePrueba { get { return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CE_PC_DIR")); } }
        public const string Sub = "settings", SubViejas = "settings-older", VersionTxt = "version.txt";
        public static string Carpeta(string pcBase) { return Path.Combine(pcBase, Sub); }

        // Lo que se apunta mientras se decide la carpeta (Paths aun no esta: Log.Write no puede): AlAbrir lo pasa al log
        static readonly List<string> pendientes = new List<string>();
        static void Apunta(string s) { lock (pendientes) pendientes.Add(s); }
        // Lo que se dice una vez en pantalla (null = nada): Mostrar lo da y lo quita
        static volatile string avisoEs, avisoEn;
        static void Avisa(string es, string en)
        {
            avisoEs = avisoEs == null ? es : avisoEs + " · " + es;
            avisoEn = avisoEn == null ? en : avisoEn + " · " + en;
        }
        public static string Modo = "";   // "pc", "pc-copia", "portable", "datos", "prueba", "app": como se eligio (para el log y el panel)

        // ¿Es una prueba (sin --datos=)? Entonces la raiz de %LOCALAPPDATA%\ConsoleEnhancer, como hasta la 1.3, nunca settings\
        public static bool EsPrueba(string[] args)
        {
            foreach (string a in args ?? new string[0])
            {
                string s = (a ?? "").ToLowerInvariant();
                if (s.StartsWith("--test") || s.StartsWith("--prueba") || s.StartsWith("--simula") || s.StartsWith("--ui-preview") || s.StartsWith("--splash-preview") ||
                    s.StartsWith("--ajustes-preview") || s.StartsWith("--dry") || s == "--asistente-auto" || s.StartsWith("--ce-desinstalar-prueba") ||
                    s.StartsWith("--ce-sonda-prueba") || s.StartsWith("--inicio-prueba")) return true;
            }
            return false;
        }
        // Los procesos de ayuda de la app (la espera, la sonda, la guarda de la barra, el modo PC del mando): la misma carpeta, sin copiar nada
        static bool EsAyudante(string[] args)
        {
            foreach (string a in args ?? new string[0])
                if (a == "--espera" || a.StartsWith("--ce-", StringComparison.OrdinalIgnoreCase) || a == "--mando-pc" || a == "--nvidia-profiles") return true;
            return false;
        }

        // portable.txt pedido por el usuario (no el que traian los zip hasta la 1.3)
        public const string PortableViejo = "with this file the data (settings, log, games, calibration) is kept in the data";
        public static bool PortablePedido(string appDir)
        {
            try
            {
                string p = Path.Combine(appDir, "portable.txt");
                if (!File.Exists(p)) return false;
                string t = File.ReadAllText(p);
                return t.IndexOf(PortableViejo, StringComparison.OrdinalIgnoreCase) < 0;
            }
            catch { return false; }
        }

        static bool SePuedeEscribir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".escribir" + System.Diagnostics.Process.GetCurrentProcess().Id);
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        // La carpeta de datos de esta ejecucion (la llama Integrado.FindDataDir; lo de --datos= ya lo ha mirado el). null = la de siempre
        // de la app (Integrado sigue con portable / la raiz)
        public static string Elegir(string appDir, string[] args)
        {
            string pc = PcBase;
            if (EsPrueba(args))
            {
                Modo = "prueba";
                return pc;   // (como hasta la 1.3: la raiz; Integrado hace el resto)
            }
            if (PortablePedido(appDir))
            {
                string p = Path.Combine(appDir, "data");
                if (SePuedeEscribir(p)) { Modo = "portable"; Apunta("Ajustes: portable (portable.txt junto al exe): " + p); return p; }
                Apunta("Ajustes: portable.txt pedido pero " + p + " no se puede escribir: la carpeta del PC");
            }
            if (pc == null) { Modo = "app"; return null; }
            string r = ElegirPc(appDir, pc, CeVersion.Texto, EsAyudante(args));
            if (r == null)
            {
                string p = Path.Combine(appDir, "data");
                Apunta("Ajustes: " + Carpeta(pc) + " no se puede escribir: van en " + p);
                Modo = "portable";
                return p;
            }
            return r;
        }

        // La carpeta del PC para esta version (settings\ o, si es mas antigua que la que la uso, su copia), con la migracion si toca
        public static string ElegirPc(string appDir, string pcBase, string version, bool ayudante)
        {
            string dir = Carpeta(pcBase);
            if (!SePuedeEscribir(dir)) return null;
            string sello = LeerSello(dir);
            if (sello.Length > 0 && CompararVersion(version, sello) < 0)
            {
                string copia = Path.Combine(Path.Combine(pcBase, SubViejas), Limpia(version));
                if (!SePuedeEscribir(copia)) return null;
                Modo = "pc-copia";
                if (!ayudante)
                {
                    int n = RefrescarCopia(dir, copia);
                    Apunta("Ajustes: esta version (" + version + ") es mas antigua que la que uso la carpeta del PC (" + sello + "): trabaja en una copia, " + copia +
                           " (" + n + " ficheros puestos al dia desde " + dir + "); los de la " + sello + " no se tocan");
                    Avisa("Versión más antigua que la de tus ajustes (" + sello + "): usa una copia, los de la " + sello + " no se tocan",
                          "Older version than your settings (" + sello + "): it uses a copy, the " + sello + " ones are not touched");
                }
                return copia;
            }
            Modo = "pc";
            Mutex mx = null;
            bool tengo = false;
            try
            {
                try { mx = new Mutex(false, "Local\\ConsoleEnhancer_AjustesPc"); tengo = mx.WaitOne(15000); } catch (AbandonedMutexException) { tengo = true; } catch { }
                if (!File.Exists(Path.Combine(dir, "consoleenhancer.ini"))) Migrar(dir, appDir, pcBase);
                if (!ayudante && (sello.Length == 0 || CompararVersion(version, sello) > 0)) EscribirSello(dir, version, sello);
            }
            finally { if (mx != null) { if (tengo) try { mx.ReleaseMutex(); } catch { } mx.Dispose(); } }
            return dir;
        }

        static string Limpia(string v)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in v ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_');
            return sb.Length > 0 ? sb.ToString() : "0";
        }

        public static string LeerSello(string dir)
        {
            try
            {
                string f = Path.Combine(dir, VersionTxt);
                if (!File.Exists(f)) return "";
                foreach (string l in File.ReadAllLines(f)) { string s = l.Trim(); if (s.Length > 0 && !s.StartsWith("#")) return s; }
            }
            catch { }
            return "";
        }
        static void EscribirSello(string dir, string version, string antes)
        {
            try
            {
                File.WriteAllText(Path.Combine(dir, VersionTxt), version + "\r\n" +
                    "# The newest ConsoleEnhancer version that has used these settings. An older version opened later works on a copy\r\n" +
                    "# (..\\" + SubViejas + "\\<version>) so it never rewrites them. La version mas nueva que ha usado estos ajustes; una mas antigua\r\n" +
                    "# usa una copia (..\\" + SubViejas + "\\<version>) y no los toca.\r\n", new UTF8Encoding(false));
                if (antes.Length > 0) Apunta("Ajustes: carpeta del PC de la " + antes + " a la " + version + " (" + dir + ")");
            }
            catch { }
        }

        // a < b: -1; igual: 0; a > b: 1. "1.3" = "1.3.0"; lo que no son numeros detras de cada parte no cuenta ("1.2.4-r7" = 1.2.4)
        public static int CompararVersion(string a, string b)
        {
            int[] x = Partes(a), y = Partes(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p < q ? -1 : 1;
            }
            return 0;
        }
        static int[] Partes(string v)
        {
            List<int> l = new List<int>();
            foreach (string s in (v ?? "").Trim().TrimStart('v', 'V').Split('.'))
            {
                int n = 0, k = 0;
                while (k < s.Length && char.IsDigit(s[k])) { n = n * 10 + (s[k] - '0'); k++; if (n > 100000) break; }
                l.Add(n);
                if (k < s.Length) break;   // ("4-r7": hasta aqui)
            }
            return l.ToArray();
        }

        // ---------------------------------------------------------------- que se copia
        // Los ficheros de ajustes (de la carpeta de datos y de games\): sin logs, copias, temporales, informes de pruebas ni el estado de un
        // momento que no vale para otra version (el plan de energia de antes de la espera)
        public static bool EsAjuste(string n)
        {
            if (string.IsNullOrEmpty(n) || n.StartsWith(".")) return false;
            string l = n.ToLowerInvariant();
            if (l.EndsWith(".log") || l.EndsWith(".tmp") || l.EndsWith(".old") || l.EndsWith(".png") || l.IndexOf(".antes-", StringComparison.Ordinal) >= 0 || l.EndsWith(".anterior")) return false;
            if (l.StartsWith("test_") || l == "sonda_prueba.txt" || l == Integrado.HeredadoTxt || l == "heredado.txt" || l == VersionTxt) return false;
            if (l == "standby_plan.txt" || l == "espera_plan.txt") return false;
            return true;
        }

        // Lo que ha tenido actividad mas reciente en una carpeta de datos (ini y ficheros de ajustes de ella y de games\)
        static DateTime Actividad(string dir)
        {
            DateTime m = DateTime.MinValue;
            try
            {
                foreach (string sub in new[] { "", "games", "juegos" })
                {
                    string d = sub.Length == 0 ? dir : Path.Combine(dir, sub);
                    if (!Directory.Exists(d)) continue;
                    foreach (string f in Directory.GetFiles(d))
                        if (EsAjuste(Path.GetFileName(f))) { DateTime t = File.GetLastWriteTimeUtc(f); if (t > m) m = t; }
                }
            }
            catch { }
            return m;
        }

        sealed class Fuente { public string Dir, Que; public DateTime Act; public int Orden; }

        // Las carpetas de datos de las que se puede traer (con consoleenhancer.ini), sin la de destino
        static List<Fuente> Fuentes(string dst, string appDir, string pcBase)
        {
            List<Fuente> l = new List<Fuente>();
            string yo = Norm(dst);
            Action<string, string, int> add = delegate(string d, string que, int orden)
            {
                try
                {
                    if (string.IsNullOrEmpty(d) || Norm(d) == yo || !File.Exists(Path.Combine(d, "consoleenhancer.ini"))) return;
                    foreach (Fuente f in l) if (Norm(f.Dir) == Norm(d)) return;
                    l.Add(new Fuente { Dir = d, Que = que, Act = Actividad(d), Orden = orden });
                }
                catch { }
            };
            foreach (string sd in new[] { "data", "datos" }) add(Path.Combine(appDir, sd), "esta carpeta (" + sd + "\\)", 0);
            if (pcBase != null) add(Carpeta(pcBase), "la carpeta del PC", 0);
            try
            {
                string padre = Path.GetDirectoryName(appDir.TrimEnd('\\'));
                if (!string.IsNullOrEmpty(padre) && Directory.Exists(padre))
                    foreach (string dir in Directory.GetDirectories(padre, "ConsoleEnhancer*"))
                        if (Norm(dir) != Norm(appDir))
                            foreach (string sd in new[] { "data", "datos" })
                                if (File.Exists(Path.Combine(dir, sd + @"\consoleenhancer.ini"))) { add(Path.Combine(dir, sd), Path.GetFileName(dir), 0); break; }
            }
            catch { }
            if (pcBase != null) add(Path.Combine(pcBase, "backup"), "la copia de seguridad", 1);
            if (l.Count == 0 && pcBase != null) add(pcBase, "la carpeta de datos de antes", 2);
            return l;
        }
        static string Norm(string d) { try { return Path.GetFullPath(d).TrimEnd('\\').ToLowerInvariant(); } catch { return (d ?? "").ToLowerInvariant(); } }

        // La que se copia: la usada mas recientemente; la copia de seguridad (que va hasta 2 min por detras de la carpeta de la que sale) solo si
        // es mas nueva por mas de 3 min que todas las carpetas de datos (se uso una version que no esta al lado)
        static Fuente Mejor(List<Fuente> l)
        {
            Fuente datos = null, copia = null, raiz = null;
            foreach (Fuente f in l)
            {
                if (f.Orden == 0) { if (datos == null || f.Act > datos.Act) datos = f; }
                else if (f.Orden == 1) copia = f;
                else raiz = f;
            }
            if (datos == null) return copia ?? raiz;
            if (copia != null && copia.Act > datos.Act.AddMinutes(3)) return copia;
            return datos;
        }

        // Una vez: dst sin ini -> lo de la mejor fuente, tal cual. Devuelve cuantos ficheros
        public static int Migrar(string dst, string appDir, string pcBase)
        {
            int n = 0;
            try
            {
                List<Fuente> l = Fuentes(dst, appDir, pcBase);
                Fuente f = Mejor(l);
                bool enPc = pcBase != null && Norm(dst) == Norm(Carpeta(pcBase));
                if (f == null) { Apunta("Ajustes: carpeta nueva " + dst + " (no hay ajustes de antes en este PC)"); return 0; }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Ajustes traidos a esta carpeta (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ") de " + f.Que + ": " + f.Dir);
                sb.AppendLine("(otras que habia: " + Otras(l, f) + ")");
                foreach (string sub in new[] { "", "games", "juegos" })
                {
                    string src = sub.Length == 0 ? f.Dir : Path.Combine(f.Dir, sub);
                    if (!Directory.Exists(src)) continue;
                    string to = sub.Length == 0 ? dst : Path.Combine(dst, "games");
                    Directory.CreateDirectory(to);
                    foreach (string a in Directory.GetFiles(src))
                    {
                        string nm = Path.GetFileName(a);
                        if (!EsAjuste(nm)) continue;
                        string m = Integrado.NombreIngles(nm);
                        string b = Path.Combine(to, m);
                        if (File.Exists(b)) continue;
                        try { File.Copy(a, b, false); File.SetLastWriteTimeUtc(b, File.GetLastWriteTimeUtc(a)); n++; sb.AppendLine((sub.Length == 0 ? "" : "games\\") + m); }
                        catch (Exception ex) { sb.AppendLine("NO " + nm + ": " + ex.Message); }
                    }
                }
                // la cache de shaders del motor (la clave es el texto del shader: el mismo nombre, lo mismo); de la fuente y de la copia
                int cso = 0;
                foreach (string c in new[] { Path.Combine(f.Dir, @"logs\cache"), pcBase != null ? Path.Combine(pcBase, @"backup\logs\cache") : null })
                    cso += CopiarCache(c, Path.Combine(dst, @"logs\cache"));
                if (cso > 0) sb.AppendLine("logs\\cache: " + cso + " shaders compilados");
                // de la carpeta de una version de antes: las DLL de Intel / AMD del motor si en esta no estan (no hace falta bajarlas)
                string appDe = Path.GetDirectoryName(f.Dir.TrimEnd('\\'));
                string deNombre = Path.GetFileName(f.Dir.TrimEnd('\\'));
                if ((deNombre.Equals("data", StringComparison.OrdinalIgnoreCase) || deNombre.Equals("datos", StringComparison.OrdinalIgnoreCase)) && Norm(appDe) != Norm(appDir))
                    foreach (string comp in new[] { "xess", "fsr" })
                        try
                        {
                            string s = Path.Combine(appDe, comp), t = Path.Combine(appDir, comp);
                            if (!File.Exists(Path.Combine(s, ".version")) || File.Exists(Path.Combine(t, ".version"))) continue;
                            int k = CopiarCarpeta(s, t);
                            sb.AppendLine(comp + "\\: " + k + " ficheros <- " + s);
                        }
                        catch { }
                File.WriteAllText(Path.Combine(dst, Integrado.HeredadoTxt), sb.ToString(), new UTF8Encoding(false));
                Apunta("Ajustes: " + (enPc ? "carpeta del PC" : "carpeta de datos") + " nueva " + dst + ": " + n + " ficheros de " + f.Que + " (" + f.Dir + ", " + f.Act.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                       ")" + (cso > 0 ? " y " + cso + " shaders" : "") + "; ver " + Integrado.HeredadoTxt);
                string de = VersionDe(f);
                Avisa("Ajustes y perfiles " + (de.Length > 0 ? "de la " + de + " " : "") + "recuperados" + (enPc ? " · ahora se guardan en este PC" : ""),
                      "Settings and profiles " + (de.Length > 0 ? "from " + de + " " : "") + "brought over" + (enPc ? " · now kept on this PC" : ""));
            }
            catch (Exception ex) { Apunta("Ajustes: ERROR trayendo los de antes a " + dst + ": " + ex.Message); }
            return n;
        }
        static string Otras(List<Fuente> l, Fuente f)
        {
            List<string> s = new List<string>();
            foreach (Fuente o in l) if (o != f) s.Add(o.Dir + " " + o.Act.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture));
            return s.Count == 0 ? "ninguna" : string.Join(", ", s.ToArray());
        }
        // "1.3" de "...\ConsoleEnhancer-v1.3\data"; "" si no se sabe
        static string VersionDe(Fuente f)
        {
            try
            {
                string app = Path.GetFileName(Path.GetDirectoryName(f.Dir.TrimEnd('\\')) ?? "");
                int i = app.IndexOf("-v", StringComparison.OrdinalIgnoreCase);
                if (i >= 0 && app.Length > i + 2 && char.IsDigit(app[i + 2])) return app.Substring(i + 2);
            }
            catch { }
            return "";
        }

        static int CopiarCache(string src, string dst)
        {
            int n = 0;
            try
            {
                if (string.IsNullOrEmpty(src) || !Directory.Exists(src)) return 0;
                Directory.CreateDirectory(dst);
                foreach (string a in Directory.GetFiles(src, "*.cso"))
                {
                    string b = Path.Combine(dst, Path.GetFileName(a));
                    if (!File.Exists(b)) try { File.Copy(a, b, false); n++; } catch { }
                }
            }
            catch { }
            return n;
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

        // Version mas antigua: su copia con lo que en la carpeta del PC sea mas nuevo (lo cambiado en la copia, si es mas nuevo, se queda)
        static int RefrescarCopia(string main, string copia)
        {
            int n = 0;
            try
            {
                foreach (string sub in new[] { "", "games" })
                {
                    string s = sub.Length == 0 ? main : Path.Combine(main, sub), t = sub.Length == 0 ? copia : Path.Combine(copia, sub);
                    if (!Directory.Exists(s)) continue;
                    Directory.CreateDirectory(t);
                    foreach (string a in Directory.GetFiles(s))
                    {
                        string nm = Path.GetFileName(a);
                        if (!EsAjuste(nm)) continue;
                        string b = Path.Combine(t, nm);
                        try
                        {
                            if (File.Exists(b) && File.GetLastWriteTimeUtc(b) >= File.GetLastWriteTimeUtc(a)) continue;
                            File.Copy(a, b, true);
                            File.SetLastWriteTimeUtc(b, File.GetLastWriteTimeUtc(a));
                            n++;
                        }
                        catch { }
                    }
                }
                CopiarCache(Path.Combine(main, @"logs\cache"), Path.Combine(copia, @"logs\cache"));
            }
            catch { }
            return n;
        }

        // ---------------------------------------------------------------- al abrir la app (Main, tras "Inicio")
        // Lo apuntado al elegir la carpeta, al log; y lo guardado que ya no existe, al mas parecido
        public static void AlAbrir(Config cfg)
        {
            List<string> l;
            lock (pendientes) { l = new List<string>(pendientes); pendientes.Clear(); }
            foreach (string s in l) Log.Write(s);
            Log.Write("Ajustes en " + Paths.Datos + " (" + (Modo.Length > 0 ? Modo : "?") + ")");
            try { PonerAlDia(Path.Combine(Paths.Datos, "games"), true); } catch (Exception ex) { Log.Error("Ajustes: poniendo al dia lo guardado", ex); }
        }

        // ¿Este tipo guardado ya no existe (o esta quitado) y por cual se cambia? null = se queda
        static string EnVezDe(string t, out string nombre)
        {
            nombre = null;
            string v = (t ?? "").Trim().ToLowerInvariant();
            if ((v == "xess" || v == "xefg") && !Config.XessFgOfrecer) { nombre = "XeSS FG"; return Config.GenEnVezDeDlss; }
            if ((v == "fsr3" || v == "fsr") && !Config.FsrFgOfrecer) { nombre = "FSR 3 FG"; return Config.GenEnVezDeDlss; }
            if (v == "dlss" || v == "vfg") { nombre = v == "dlss" ? "DLSS FG" : "NVIDIA VFG"; return Config.GenEnVezDeDlss; }
            return null;
        }

        // games\profiles.txt (columna 10: el tipo del 60 y del 30) y games\framegen_type.txt (lo elegido con L3 / el menu): lo que ya no existe
        // al mas parecido de este PC; lo demas igual (las lineas sin eso, tal cual). Devuelve cuantos cambios
        public static int PonerAlDia(string games, bool avisar)
        {
            int cambios = 0;
            HashSet<string> quitados = new HashSet<string>();
            string porNombre = Config.GenEnVezDeDlss;
            // perfiles
            string pf = Path.Combine(games, "profiles.txt");
            if (File.Exists(pf))
            {
                string[] ls = File.ReadAllLines(pf, Encoding.UTF8);
                bool cambia = false;
                for (int i = 0; i < ls.Length; i++)
                {
                    string raw = ls[i];
                    if (raw.Length == 0 || raw.StartsWith("#")) continue;
                    string[] f = raw.Split('|');
                    if (f.Length < 10) continue;
                    string[] p = f[9].Split(',');
                    string p60 = p.Length > 0 ? p[0].Trim() : "", p30 = p.Length > 1 ? p[1].Trim() : "";
                    string n60, n30, q60, q30;
                    string e60 = EnVezDe(p60, out q60), e30 = EnVezDe(p30, out q30);
                    if (e60 == null && e30 == null) continue;
                    n60 = e60 ?? p60; n30 = e30 ?? p30;
                    string col = n60.Length == 0 && n30.Length == 0 ? null : n60 + "," + n30;
                    // (las columnas de detras, si una version nueva las añade, se quedan; sin tipo y sin nada detras, sin la columna 10)
                    f[9] = col ?? "";
                    ls[i] = col == null && f.Length == 10 ? string.Join("|", f, 0, 9) : string.Join("|", f);
                    cambia = true;
                    if (q60 != null) { quitados.Add(q60); cambios++; Log.Write("Ajustes al dia: perfil \"" + f[0].Trim() + "\", generacion del modo 60 " + q60 + " (ya no esta) -> " + Nombre(n60)); }
                    if (q30 != null) { quitados.Add(q30); cambios++; Log.Write("Ajustes al dia: perfil \"" + f[0].Trim() + "\", generacion del modo 30 " + q30 + " (ya no esta) -> " + Nombre(n30)); }
                }
                if (cambia)
                {
                    try { File.Copy(pf, pf + ".antes-" + Limpia(CeVersion.Texto), true); } catch { }
                    GameProfiles.WriteAtomic(pf, string.Join("\r\n", ls) + "\r\n");
                }
            }
            // lo elegido ahora (L3 / el menu)
            string ft = Path.Combine(games, "framegen_type.txt");
            if (File.Exists(ft))
            {
                string[] ls = File.ReadAllLines(ft);
                bool cambia = false;
                for (int i = 0; i < ls.Length; i++)
                {
                    string l = ls[i].Trim();
                    if (!(l.StartsWith("60=") || l.StartsWith("30="))) continue;
                    string q;
                    string e = EnVezDe(l.Substring(3), out q);
                    if (e == null) continue;
                    ls[i] = l.Substring(0, 3) + e;
                    cambia = true; quitados.Add(q); cambios++;
                    Log.Write("Ajustes al dia: generacion elegida del modo " + l.Substring(0, 2) + " " + q + " (ya no esta) -> " + Nombre(e) + " (" + ft + ")");
                }
                if (cambia) File.WriteAllText(ft, string.Join("\r\n", ls) + "\r\n", new UTF8Encoding(false));
            }
            if (cambios > 0 && avisar)
            {
                List<string> q = new List<string>(quitados);
                q.Sort(StringComparer.Ordinal);
                string que = string.Join(" / ", q.ToArray());
                string va = porNombre.Length > 0 ? Config.GenNombre(porNombre) : null;
                Avisa(que + " ya no está: va " + (va ?? "la de serie de este PC"), que + " is gone: using " + (va ?? "this PC's default"));
            }
            return cambios;
        }
        static string Nombre(string t) { return string.IsNullOrEmpty(t) ? "la de serie de este PC" : Config.GenNombre(t); }

        // El aviso de una vez (con la imagen ya delante); null = nada
        public static string AvisoUnaVez()
        {
            string es = avisoEs, en = avisoEn;
            avisoEs = avisoEn = null;
            if (es == null) return null;
            return Deteccion.Tx.T(es, en);
        }
    }

    partial class TrayApp
    {
        // (RunMpv, con la imagen ya delante: una vez) lo de AjustesPc -ajustes traidos de la version de antes, lo que ya no existe-
        void AjustesAviso()
        {
            try
            {
                if (!Config.EngineActive) return;   // (lo pinta el motor: sin el -el HOME a 4K nativo-, a la siguiente)
                string a = AjustesPc.AvisoUnaVez();
                if (a == null) return;
                lock (cmdQueue) cmdQueue.Add(OsdCmd(a, "7000", false));
            }
            catch (Exception ex) { Log.Error("Ajustes: aviso", ex); }
        }
    }
}
