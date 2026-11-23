// Switch2Datos.cs - 23-11 (1.2.4 ronda 7, agente SWITCH2). El usuario: "he pasado de la ps5 a poner la switch 2 y no detecta que es
// la switch 2, en mi anterior app iba como la seda lo que tenia de switch 2, mete eso rapidamente".
// Lo que paso (su consoleenhancer.log del 23-11 desde las 18:10:08):
//  - Abrio como PS5 (games\console.txt) y la Switch 2 no se reconocio hasta los 16 s: solo contaban su HOME igual a games\home_ref.txt
//    (aun no lo habia en esta version) y "Nintendo Switch 2" leido cada 5 s, con 5 s entre dos pruebas. Mientras, la logica de la PS5
//    caso la portada de "PS5: Grand Theft Auto VI" con su HOME. Arreglado en Consolas.cs (pruebas concluyentes, ApuntaAOtra),
//    ConsolasApp.cs (ObservaHomeSwitch, el juego de la consola de antes) y MenuPs5.cs (RecoJuegosActivo).
//  - En el HOME leyo bien "The Legend of Zelda: Ocarina of Time" (seguro a las 18:10:37), pero las lecturas eran de cada seleccion:
//    al volver el cursor a el y entrar llevaba 1 lectura, "no esta confirmado" y se guardo como "Juego 1". Arreglado en
//    Switch2Magpie.cs (lecturasHome: las lecturas de cada icono para toda la sesion).
//  - La version de antes de la app (la de la carpeta Switch2Magpie, con juegos\perfiles.txt e iconos_juegos.txt) conocia todos sus
//    juegos de la Switch 2 por su icono, con sus ajustes: por eso "iba como la seda". ConsoleEnhancer empezaba sin ninguno.
// Aqui, al abrir la app (ConsolaAlAbrir, antes de nada del HOME), una vez:
//  1. ImportarViejo: los juegos de la Switch 2 de esa version de antes (sin los de la PS5 / Xbox, "Consola", "Juego N" ni casillas de
//     la consola), con sus iconos y sus ajustes, si aqui no hay ya uno con ese nombre; y su games\home_ref.txt / home_texts.txt si
//     aqui faltan. Apuntado en games\imported_switch2.txt (no se repite). Solo se LEE la carpeta de la version de antes.
//  2. ArreglaSinNombre: los "Juego N" guardados por este fallo (el log dice el nombre leido en el HOME al entrar: "Al entrar: "X" no
//     es un juego conocido y su nombre no esta confirmado ... Juego nuevo: "Juego N"") pasan a su nombre si ese nombre se leyo dos
//     veces o seguro en el HOME de esa misma sesion. Si ya hay un perfil con ese nombre (el importado), se le dan los iconos del
//     "Juego N" y este se quita (se quedan los ajustes del de nombre bueno). Mira el log de esta version y los de las carpetas
//     ConsoleEnhancer* de al lado (cada version portable tiene el suyo; solo se leen). Apuntado en games\name_fixes.txt.
// Prueba sin consola ni grafica: --test-switch2=<informe>[;<carpeta de datos de la app (copia)>[;<carpeta de la version de antes>]]

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ConsoleEnhancer
{
    static partial class GameProfiles
    {
        // (pruebas) la carpeta de los perfiles; devuelve la de antes. null = games\ de verdad
        internal static string CarpetaDePrueba(string dir)
        {
            lock (sync)
            {
                string old = Dir;
                Dir = dir ?? Path.Combine(Paths.Datos, "games");
                list = null;
                backedUp = false;
                return old;
            }
        }

        internal static string Carpeta { get { lock (sync) return Dir; } }

        // Una linea de profiles.txt / perfiles.txt (como Load), o null
        static GameProfile ParseLinea(string raw)
        {
            if (raw == null || raw.StartsWith("#")) return null;
            string[] f = raw.Split('|');
            if (f.Length < 6 || f[0].Trim().Length == 0) return null;
            GameProfile p = new GameProfile();
            p.Name = f[0].Trim();
            p.Capture = f[1].Trim();
            int.TryParse(f[2], out p.Level);
            int.TryParse(f[3], out p.Passes);
            p.Level = Math.Max(1, Math.Min(4, p.Level));
            p.Passes = Math.Max(1, Math.Min(3, p.Passes));
            p.Dlss5 = f[4].Trim() != "0";
            p.Scaler = f[5].Trim() != "0";
            if (f[5].Trim() == "fsr" || f[5].Trim() == "ninguno" || f[5].Trim() == "xess") p.ScalerPick = f[5].Trim();
            p.Cap = f.Length > 6 && f[6].Trim() == "30";
            p.Mode120 = f.Length > 6 && f[6].Trim() == "120";
            p.Fg = !(f.Length > 7 && f[7].Trim() == "0");
            p.NameSure = f.Length > 8 && f[8].Trim() == "1" && !GameNames.IsGeneric(p.Name);
            if (f.Length > 9) p.ParsePick(f[9].Trim());
            return p;
        }

        // ¿Un juego de la Switch 2? (sin etiqueta de consola, ni "Consola", ni "Juego N", ni una casilla de la consola)
        internal static bool EsDeSwitch2(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name == ConsolaPerfil.NombrePerfilConsola || JuegoConsola.TagConsola(name) != null) return false;
            if (GameNames.IsGeneric(name) || HomeHints.IsSystemName(name)) return false;
            return GameNames.Plausible(name);
        }

        // Los juegos de la Switch 2 de la version de antes (perfiles + iconos de oldDir) que aqui no estan. Devuelve los importados
        internal static List<string> ImportarViejo(string oldDir)
        {
            List<string> hechos = new List<string>();
            string pf = File.Exists(Path.Combine(oldDir, "perfiles.txt")) ? Path.Combine(oldDir, "perfiles.txt") : Path.Combine(oldDir, "profiles.txt");
            string icf = File.Exists(Path.Combine(oldDir, "iconos_juegos.txt")) ? Path.Combine(oldDir, "iconos_juegos.txt") : Path.Combine(oldDir, "game_icons.txt");
            if (!File.Exists(pf)) return hechos;
            List<GameProfile> viejos = new List<GameProfile>();
            foreach (string raw in File.ReadAllLines(pf, Encoding.UTF8))
            {
                GameProfile p = ParseLinea(raw);
                if (p != null && EsDeSwitch2(p.Name)) viejos.Add(p);
            }
            if (File.Exists(icf))
                foreach (string raw in File.ReadAllLines(icf, Encoding.UTF8))
                {
                    if (raw.StartsWith("#")) continue;
                    int bar = raw.LastIndexOf('|');
                    if (bar <= 0) continue;
                    string n = raw.Substring(0, bar).Trim();
                    foreach (GameProfile p in viejos)
                        if (p.Name == n) { p.AddIcon(HomeLabel.ParseNums(raw.Substring(bar + 1), 180)); break; }
                }
            lock (sync)
            {
                Load();
                foreach (GameProfile v in viejos)
                {
                    bool ya = false;
                    string k = GameNames.Key(v.Name);
                    foreach (GameProfile q in list)
                        if (q.Name == v.Name || GameNames.Key(q.Name) == k || (!GameNames.IsGeneric(q.Name) && SameGame(q.Name, v.Name))) { ya = true; break; }
                    if (ya) continue;
                    list.Add(v);
                    hechos.Add(v.Name);
                }
                if (hechos.Count > 0) { Backup(); SaveAll(); }
            }
            return hechos;
        }

        // Un "Juego N" (sin nombre seguro) que es el juego "nombre": a ese nombre, o juntado con el perfil que ya lo tiene (se quedan los
        // ajustes de ese y los iconos de los dos). Devuelve lo hecho (log) o null si no se toco nada
        internal static string NombraSinNombre(string generico, string nombre)
        {
            if (!GameNames.IsGeneric(generico) || string.IsNullOrEmpty(nombre) || GameNames.IsGeneric(nombre)) return null;
            lock (sync)
            {
                Load();
                GameProfile g = null, dest = null;
                string k = GameNames.Key(nombre);
                foreach (GameProfile q in list) if (q.Name == generico) g = q;
                if (g == null || g.NameSure) return null;
                foreach (GameProfile q in list)
                    if (q != g && (q.Name == nombre || GameNames.Key(q.Name) == k)) { dest = q; break; }
                Backup();
                if (dest != null)
                {
                    foreach (double[] ic in g.Icons) dest.AddIcon(ic);
                    list.Remove(g);
                    SaveAll();
                    return "\"" + generico + "\" juntado con \"" + dest.Name + "\" (sus iconos; se quedan los ajustes de \"" + dest.Name + "\": " + dest.Summary() + ")";
                }
                g.Name = nombre;
                g.NameSure = true;
                SaveAll();
                return "\"" + generico + "\" -> \"" + nombre + "\" (" + g.Summary() + ")";
            }
        }
    }

    static partial class GameProfiles
    {
        // (23-11, SWITCH2) Quita un "Juego N" sin nombre seguro (y sus iconos). false si no esta o ya tiene nombre
        internal static bool QuitaSinNombre(string generico)
        {
            if (!GameNames.IsGeneric(generico)) return false;
            lock (sync)
            {
                Load();
                GameProfile g = null;
                foreach (GameProfile q in list) if (q.Name == generico) g = q;
                if (g == null || g.NameSure) return false;
                Backup();
                list.Remove(g);
                SaveAll();
                return true;
            }
        }
    }

    static class Switch2Datos
    {
        public const string MarcaImportado = "imported_switch2.txt";
        public const string MarcaNombres = "name_fixes.txt";

        // La carpeta de datos de la version de antes de la app (la que nacio para la Switch 2: Switch2Magpie\juegos), o null
        public static string CarpetaVieja(string appDir)
        {
            List<string> cand = new List<string>();
            try
            {
                string padre = Path.GetDirectoryName((appDir ?? "").TrimEnd('\\'));
                if (!string.IsNullOrEmpty(padre)) cand.Add(Path.Combine(padre, "Switch2Magpie"));
                string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (string s in new[] { "", "Downloads", "Desktop", "Documents" })
                    cand.Add(Path.Combine(s.Length > 0 ? Path.Combine(up, s) : up, "Switch2Magpie"));
                foreach (DriveInfo d in DriveInfo.GetDrives())
                    if (d.DriveType == DriveType.Fixed && d.IsReady) cand.Add(Path.Combine(d.RootDirectory.FullName, "Switch2Magpie"));
            }
            catch { }
            foreach (string c in cand)
            {
                try
                {
                    foreach (string sub in new[] { "juegos", @"data\games", "games" })
                    {
                        string j = Path.Combine(c, sub);
                        if (File.Exists(Path.Combine(j, "perfiles.txt")) || File.Exists(Path.Combine(j, "profiles.txt"))) return j;
                    }
                }
                catch { }
            }
            return null;
        }

        // Al abrir la app (TrayApp.ConsolaAlAbrir): importar de la version de antes y arreglar los "Juego N" de este fallo. Devuelve el
        // ultimo juego cambiado de nombre (para last_game.txt / curGame) como "viejo|nuevo" o null
        public static List<KeyValuePair<string, string>> AlAbrir(string gamesDir, string appDir, string oldDir, IEnumerable<string> logs, DateTime ahora)
        {
            List<KeyValuePair<string, string>> renombres = new List<KeyValuePair<string, string>>();
            try { Importar(gamesDir, appDir, oldDir, ahora); }
            catch (Exception ex) { Log.Error("Switch 2: importando los juegos de la version de antes", ex); }
            try { renombres = ArreglaSinNombre(gamesDir, logs); }
            catch (Exception ex) { Log.Error("Switch 2: nombres de los \"Juego N\"", ex); }
            try { QuitaPortadasMalas(gamesDir, logs); }
            catch (Exception ex) { Log.Error("Switch 2: portadas de la PS5 sacadas del HOME de la Switch 2", ex); }
            try { foreach (string q in QuitaJuegosDeOtraConsola(gamesDir, logs)) renombres.Add(new KeyValuePair<string, string>(q, null)); }
            catch (Exception ex) { Log.Error("Switch 2: \"Juego N\" creados con la imagen de otra consola", ex); }
            return renombres;
        }

        // 23-11 18:56:30 (su log): la app abrio con la Switch 2 de console.txt y la PS5 delante; la logica del HOME de la Switch 2 tomo la
        // imagen de la PS5 por su HOME y creo "Juego 1" con un "icono" de la PS5; a las 18:57:21 cambio a la PS5. Se quitan (perfil e iconos)
        // los "Juego N" creados asi: en una sesion que empezo con la ultima consola vista, antes de ver de verdad su HOME y antes del primer
        // cambio de consola, con ese cambio a otra consola en los 3 minutos siguientes sin ver el HOME entre medias, y que siguen sin nombre.
        // Una vez (games\name_fixes.txt)
        static readonly Regex ReSesionUltima = new Regex("^Consola: (.+?) \\(consola=auto, la ultima vista");
        static readonly Regex ReAhora = new Regex("^Consola: ahora (.+?) \\(antes ");
        static List<string> QuitaJuegosDeOtraConsola(string gamesDir, IEnumerable<string> logs)
        {
            List<string> quitados = new List<string>();
            string marca = Path.Combine(gamesDir, MarcaNombres);
            HashSet<string> ya = new HashSet<string>(StringComparer.Ordinal);
            if (File.Exists(marca)) foreach (string l in File.ReadAllLines(marca, Encoding.UTF8)) ya.Add(l.Trim());
            List<string[]> malos = new List<string[]>();   // hora, "Juego N", consola de la sesion, consola a la que cambio
            foreach (string lf in logs)
            {
                if (lf == null || !File.Exists(lf) || new FileInfo(lf).Length > 64L << 20) continue;
                string consolaSesion = null, creadoHora = null, creado = null;
                bool desdeUltima = false, cambiada = false, homeVisto = false;
                DateTime creadoAt = DateTime.MinValue;
                using (FileStream fs = new FileStream(lf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string l;
                    while ((l = sr.ReadLine()) != null)
                    {
                        Match m = ReLinea.Match(l);
                        if (!m.Success) continue;
                        string t = m.Groups[2].Value;
                        DateTime at;
                        DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out at);
                        Match q;
                        if (t.StartsWith("===== Inicio", StringComparison.Ordinal))
                        { consolaSesion = null; desdeUltima = cambiada = homeVisto = false; creado = null; }
                        else if ((q = ReSesionUltima.Match(t)).Success) { consolaSesion = q.Groups[1].Value; desdeUltima = true; }
                        else if (t.StartsWith("HOME visto en capturas seguidas", StringComparison.Ordinal) || t.StartsWith("Consola: el HOME de la Switch 2", StringComparison.Ordinal))
                        { homeVisto = true; creado = null; }
                        else if ((q = ReNuevo.Match(t)).Success)
                        {
                            if (desdeUltima && !cambiada && !homeVisto) { creado = q.Groups[1].Value; creadoHora = m.Groups[1].Value; creadoAt = at; }
                        }
                        else if ((q = ReRenombrado.Match(t)).Success)
                        {
                            if (creado == q.Groups[1].Value) creado = null;
                            for (int i = malos.Count - 1; i >= 0; i--) if (malos[i][1] == q.Groups[1].Value) malos.RemoveAt(i);
                        }
                        else if ((q = ReAhora.Match(t)).Success)
                        {
                            if (!cambiada && creado != null && consolaSesion != null && q.Groups[1].Value != consolaSesion && (at - creadoAt).TotalSeconds <= 180)
                                malos.Add(new[] { creadoHora, creado, consolaSesion, q.Groups[1].Value });
                            cambiada = true;
                            creado = null;
                        }
                    }
                }
            }
            StringBuilder sb = new StringBuilder();
            foreach (string[] b in malos)
            {
                string id = "otra_consola|" + b[0] + "|" + b[1];
                if (ya.Contains(id)) continue;
                ya.Add(id);
                sb.Append(id).Append("\r\n");
                if (!GameProfiles.QuitaSinNombre(b[1])) continue;
                quitados.Add(b[1]);
                Log.Write("Switch 2: \"" + b[1] + "\" (creado el " + b[0] + " con " + b[2] + " puesta por ser la ultima vista, y era la imagen de " + b[3] +
                          "): quitado con sus iconos");
            }
            if (sb.Length > 0) File.AppendAllText(marca, sb.ToString(), new UTF8Encoding(false));
            return quitados;
        }

        // 23-11 18:08:18 (su log): con la PS5 puesta y la Switch 2 ya delante, el HOME de la Switch 2 se memorizo como la portada de
        // "PS5: Grand Theft Auto VI" (games\console_covers.txt); a las 18:10:20 caso con el y se iba a arrancar GTA VI. Las portadas
        // memorizadas segun el log de una captura que es el HOME de la Switch 2 (su captura en games\debug\ps5: el nombre cian y/o la
        // hora con la bateria) se quitan, una vez (games\name_fixes.txt)
        static readonly Regex RePortada = new Regex("^Portada del juego: nueva, memorizada para \"(.+?)\" .* -> (.+\\.png)$");
        static void QuitaPortadasMalas(string gamesDir, IEnumerable<string> logs)
        {
            string marca = Path.Combine(gamesDir, MarcaNombres);
            HashSet<string> ya = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(marca)) foreach (string l in File.ReadAllLines(marca, Encoding.UTF8)) ya.Add(l.Trim());
            StringBuilder sb = new StringBuilder();
            foreach (string lf in logs)
            {
                if (lf == null || !File.Exists(lf) || new FileInfo(lf).Length > 64L << 20) continue;
                List<string[]> vistas = new List<string[]>();
                using (FileStream fs = new FileStream(lf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string l;
                    while ((l = sr.ReadLine()) != null)
                    {
                        Match m = ReLinea.Match(l);
                        if (!m.Success) continue;
                        Match p = RePortada.Match(m.Groups[2].Value);
                        if (p.Success) vistas.Add(new[] { p.Groups[1].Value, p.Groups[2].Value.Trim() });
                    }
                }
                foreach (string[] v in vistas)
                {
                    string id = "portada|" + v[1];
                    if (ya.Contains(id) || !File.Exists(v[1])) continue;
                    ya.Add(id);
                    sb.Append(id).Append("\r\n");
                    DateTime cuando;
                    if (!HoraDeNombre(Path.GetFileName(v[1]), out cuando)) continue;
                    bool cian;
                    using (Bitmap full = Cargar32(v[1]))
                    {
                        if (!EsHomeSwitch(full, cuando, out cian)) continue;
                        List<string> de = PortadasConsola.QuitaParecidas(PortadasConsola.Firma(full));
                        Log.Write("Switch 2: la portada memorizada para \"" + v[0] + "\" era el HOME de la Switch 2 (" + Path.GetFileName(v[1]) + "): quitada" +
                                  (de.Count > 0 ? " (" + de.Count + ": " + string.Join(", ", de.ToArray()) + ")" : " (ya no estaba)"));
                    }
                }
            }
            if (sb.Length > 0) File.AppendAllText(marca, sb.ToString(), new UTF8Encoding(false));
        }

        // ¿Esta captura es el HOME de Nintendo? (la hora del PC de ese momento arriba a la derecha con la bateria; cian = con el nombre del
        // juego en cian)
        public static bool EsHomeSwitch(Bitmap full, DateTime cuando, out bool cian)
        {
            using (Bitmap small = HomeLabel.Small(full))
            {
                Rectangle box; bool hint; HomeLabel.LabelText label; int hy;
                cian = HomeLabel.Selected(small, out box, out hint, out label, out hy) != null;
                string rh, ro, rp;
                DetectorMenu.OlvidaZonas();
                return DetectorMenu.RelojSwitch(full, cuando, out rh, out ro, out rp);
            }
        }

        public static Bitmap Cargar32(string f)
        {
            using (Bitmap src = new Bitmap(f))
            {
                Bitmap b = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                using (Graphics g = Graphics.FromImage(b)) g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
                return b;
            }
        }

        // "20261123-181019_1280x720.png" / "20261123-181040-357_sin_nombre.png" -> su hora
        public static bool HoraDeNombre(string n, out DateTime t)
        {
            t = DateTime.MinValue;
            if (n == null || n.Length < 15) return false;
            return DateTime.TryParseExact(n.Substring(0, 15), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
        }

        static void Importar(string gamesDir, string appDir, string oldDir, DateTime ahora)
        {
            string marca = Path.Combine(gamesDir, MarcaImportado);
            if (File.Exists(marca)) return;
            string viejo = oldDir ?? CarpetaVieja(appDir);
            if (viejo == null) return;
            List<string> hechos = GameProfiles.ImportarViejo(viejo);
            StringBuilder extra = new StringBuilder();
            foreach (string[] par in new[] { new[] { "home_ref.txt", "home_ref.txt" }, new[] { "textos_home.txt", "home_texts.txt" }, new[] { "home_texts.txt", "home_texts.txt" } })
            {
                string src = Path.Combine(viejo, par[0]), dst = Path.Combine(gamesDir, par[1]);
                if (File.Exists(src) && !File.Exists(dst)) { File.Copy(src, dst, false); extra.Append(", ").Append(par[1]); }
            }
            File.WriteAllText(marca, ahora.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " desde " + viejo + "\r\n" +
                              string.Join("\r\n", hechos.ToArray()) + "\r\n", new UTF8Encoding(false));
            Log.Write("Switch 2: " + hechos.Count + " juegos de la version de antes de la app (" + viejo + ") con sus iconos y sus ajustes" +
                      (hechos.Count > 0 ? ": " + string.Join(", ", hechos.ToArray()) : "") + extra.ToString() + " (una vez: " + MarcaImportado + ")");
        }

        // Una entrada en un "Juego N" de este fallo, sacada del log
        sealed class SinNombre { public string Hora, Generico, Leido; }

        static readonly Regex ReLinea = new Regex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})  (.*)$");
        static readonly Regex ReAlEntrar = new Regex("^Al entrar: \"(.+)\" no es un juego conocido y su nombre no esta confirmado \\(se guarda como juego sin nombre");
        static readonly Regex ReNuevo = new Regex("^Juego nuevo: \"(Juego \\d+)\"");
        static readonly Regex ReLeido = new Regex("^HOME: nombre leido \"(.+)\" \\((\\d+) lecturas(.*)\\)$");
        static readonly Regex ReRenombrado = new Regex("^Perfil \"(Juego \\d+)\" -> ");

        // Las entradas de los logs (en orden de hora) con el nombre leido en el HOME de esa misma sesion: el ultimo "Juego N" de cada uno,
        // si no se le cambio el nombre despues
        static List<SinNombre> DeLosLogs(IEnumerable<string> logs)
        {
            List<string[]> lineas = new List<string[]>();   // hora, texto, sesion
            int sesion = 0;
            foreach (string lf in logs)
            {
                if (lf == null || !File.Exists(lf)) continue;
                FileInfo fi = new FileInfo(lf);
                if (fi.Length > 64L << 20) continue;
                sesion++;
                using (FileStream fs = new FileStream(lf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string l;
                    while ((l = sr.ReadLine()) != null)
                    {
                        Match m = ReLinea.Match(l);
                        if (!m.Success) continue;
                        string t = m.Groups[2].Value;
                        if (t.StartsWith("===== Inicio", StringComparison.Ordinal)) sesion++;
                        if (t.StartsWith("Al entrar: ", StringComparison.Ordinal) || t.StartsWith("Juego nuevo: ", StringComparison.Ordinal) ||
                            t.StartsWith("HOME: nombre leido ", StringComparison.Ordinal) || t.StartsWith("Perfil \"Juego ", StringComparison.Ordinal) ||
                            t.StartsWith("===== Inicio", StringComparison.Ordinal))
                            lineas.Add(new[] { m.Groups[1].Value, t, sesion.ToString(CultureInfo.InvariantCulture) });
                    }
                }
            }
            // (en orden de hora; a igual hora, el orden del fichero)
            List<int> orden = new List<int>();
            for (int i = 0; i < lineas.Count; i++) orden.Add(i);
            orden.Sort(delegate (int a, int b) { int c = string.CompareOrdinal(lineas[a][0], lineas[b][0]); return c != 0 ? c : a.CompareTo(b); });
            Dictionary<string, SinNombre> ultimo = new Dictionary<string, SinNombre>();
            Dictionary<string, int> leidas = new Dictionary<string, int>();     // clave del nombre -> lecturas en esta sesion
            Dictionary<string, bool> seguras = new Dictionary<string, bool>();
            string pendiente = null, pendienteHora = null, sesionAct = null;
            foreach (int i in orden)
            {
                string[] e = lineas[i];
                if (e[2] != sesionAct || e[1].StartsWith("===== Inicio", StringComparison.Ordinal)) { sesionAct = e[2]; leidas.Clear(); seguras.Clear(); pendiente = null; }
                Match m;
                if ((m = ReLeido.Match(e[1])).Success)
                {
                    string k = GameNames.Key(m.Groups[1].Value);
                    int n;
                    leidas.TryGetValue(k, out n);
                    leidas[k] = n + 1;
                    if (m.Groups[3].Value.IndexOf("seguro", StringComparison.Ordinal) >= 0) seguras[k] = true;
                }
                else if ((m = ReAlEntrar.Match(e[1])).Success) { pendiente = m.Groups[1].Value; pendienteHora = e[0]; }
                else if ((m = ReNuevo.Match(e[1])).Success)
                {
                    if (pendiente != null)
                    {
                        string k = GameNames.Key(pendiente);
                        int n;
                        leidas.TryGetValue(k, out n);
                        bool bien = seguras.ContainsKey(k) || n >= 2;
                        SinNombre s = new SinNombre();
                        s.Hora = pendienteHora; s.Generico = m.Groups[1].Value; s.Leido = bien ? pendiente : null;
                        ultimo[s.Generico] = s;
                    }
                    else ultimo.Remove(m.Groups[1].Value);   // (otro "Juego N" nuevo, de otra cosa: el de antes ya no es este)
                    pendiente = null;
                }
                else if ((m = ReRenombrado.Match(e[1])).Success) ultimo.Remove(m.Groups[1].Value);
            }
            List<SinNombre> r = new List<SinNombre>();
            foreach (SinNombre s in ultimo.Values) if (s.Leido != null) r.Add(s);
            return r;
        }

        static List<KeyValuePair<string, string>> ArreglaSinNombre(string gamesDir, IEnumerable<string> logs)
        {
            List<KeyValuePair<string, string>> hechos = new List<KeyValuePair<string, string>>();
            string marca = Path.Combine(gamesDir, MarcaNombres);
            HashSet<string> ya = new HashSet<string>(StringComparer.Ordinal);
            if (File.Exists(marca)) foreach (string l in File.ReadAllLines(marca, Encoding.UTF8)) ya.Add(l.Trim());
            StringBuilder sb = new StringBuilder();
            foreach (SinNombre s in DeLosLogs(logs))
            {
                string id = s.Hora + "|" + s.Generico + "|" + s.Leido;
                if (ya.Contains(id)) continue;
                string info = GameProfiles.NombraSinNombre(s.Generico, s.Leido);
                sb.Append(id).Append("\r\n");   // (hecho o no, no se vuelve a mirar)
                if (info == null) continue;
                Log.Write("Switch 2: " + info + ": el nombre se leyo bien en el HOME el " + s.Hora + " y se guardo sin el (arreglado)");
                hechos.Add(new KeyValuePair<string, string>(s.Generico, GameProfiles.Find(s.Leido) != null ? GameProfiles.Find(s.Leido).Name : s.Leido));
            }
            if (sb.Length > 0) File.AppendAllText(marca, sb.ToString(), new UTF8Encoding(false));
            return hechos;
        }

        // Los logs a mirar: el de esta version y los de las carpetas ConsoleEnhancer* de al lado (las versiones portables de antes; solo
        // los de los ultimos 30 dias)
        public static List<string> Logs(string appDir, string datos)
        {
            List<string> r = new List<string>();
            string mio = Path.Combine(datos, @"logs\consoleenhancer.log");
            r.Add(mio);
            try
            {
                string padre = Path.GetDirectoryName((appDir ?? "").TrimEnd('\\'));
                if (!string.IsNullOrEmpty(padre) && Directory.Exists(padre))
                    foreach (string dir in Directory.GetDirectories(padre, "ConsoleEnhancer*"))
                        foreach (string sd in new[] { "data", "datos" })
                        {
                            string lf = Path.Combine(dir, sd + @"\logs\consoleenhancer.log");
                            if (File.Exists(lf) && !string.Equals(Path.GetFullPath(lf), Path.GetFullPath(mio), StringComparison.OrdinalIgnoreCase) &&
                                (DateTime.Now - File.GetLastWriteTime(lf)).TotalDays <= 30) r.Add(lf);
                        }
            }
            catch { }
            return r;
        }
    }

    partial class TrayApp
    {
        // ConsolaAlAbrir: lo de Switch2Datos y, si el ultimo juego era un "Juego N" que ya tiene nombre, ese nombre
        void Switch2AlAbrir()
        {
            if (testMode) return;
            List<KeyValuePair<string, string>> ren = Switch2Datos.AlAbrir(Path.Combine(Paths.Datos, "games"), Paths.AppDir, null,
                                                                          Switch2Datos.Logs(Paths.AppDir, Paths.Datos), DateTime.Now);
            string ultimo = LoadText(Paths.LastGameFile);
            foreach (KeyValuePair<string, string> kv in ren)
            {
                // (kv.Value null: quitado, 23-11 SWITCH2)
                if (ultimo == kv.Key)
                {
                    if (kv.Value != null) SaveText(Paths.LastGameFile, kv.Value);
                    else try { File.Delete(Paths.LastGameFile); } catch { }
                    ultimo = kv.Value;
                }
                lock (profileSync) { if (curGame == kv.Key) curGame = kv.Value; }
            }
        }

        // ---- --test-switch2=<informe>[;<datos de la app (se copian)>[;<datos de la version de antes>]] ----
        // Sin consola ni grafica: la deteccion de la Switch 2 con la PS5 puesta (reloj falso), sus capturas reales (HOME con el nombre cian
        // y la hora con la bateria; menus de la PS5 que no lo son), las lecturas del HOME que siguen entre selecciones, y la importacion y
        // el arreglo de "Juego 1" sobre COPIAS de los datos del usuario
        const string MarcaImportadoPrueba = Switch2Datos.MarcaImportado;
        internal static int Switch2Prueba(string arg)
        {
            string[] pa = (arg ?? "").Split(';');
            string informe = pa.Length > 0 && pa[0].Length > 0 ? pa[0] : Path.Combine(Paths.Datos, @"games\test_switch2.txt");
            string datosUsuario = pa.Length > 1 && pa[1].Length > 0 ? pa[1] : null;
            string viejo = pa.Length > 2 && pa[2].Length > 0 ? pa[2] : null;
            StringBuilder sb = new StringBuilder();
            int fallos = 0;
            Action<string, bool> check = delegate (string what, bool ok)
            {
                sb.Append(ok ? "OK    " : "FALLO ").Append(what).Append("\r\n");
                if (!ok) fallos++;
            };
            string tmp = Path.Combine(Path.GetTempPath(), "ce_test_switch2_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            string carpetaAntes = Consolas.CarpetaDePrueba(tmp);
            Func<DateTime> relojAntes = Consolas.Reloj;
            try
            {
                // ---- 1. Deteccion con un reloj falso: la PS5 puesta (console.txt = ps5), la Switch 2 delante ----
                DateTime t0 = new DateTime(2026, 11, 23, 18, 10, 19);
                DateTime ahora = t0;
                Consolas.Reloj = delegate { return ahora; };
                File.WriteAllText(Path.Combine(tmp, "console.txt"), "ps5");
                Consolas.Reiniciar("auto");
                check("1. Al abrir: la PS5 por console.txt (como el usuario a las 18:10:09)", Consolas.Actual == Consolas.Ps5);
                Consolas.VistoMenuPropio(Consolas.Ps5, "18:09");   // (su menu visto hace nada: la puesta "reciente")
                ahora = t0.AddSeconds(1);
                ConsolaPerfil r1 = Consolas.VistoHomeSwitch2("18:10");
                check("1. El HOME de la Switch 2 (nombre cian + hora con bateria) una vez: aun no cambia, pero la imagen apunta a la Switch 2 (sin portadas de la PS5)",
                      r1 == null && Consolas.Actual == Consolas.Ps5 && Consolas.ApuntaAOtra() == Consolas.Switch2);
                ahora = t0.AddSeconds(1.5);
                ConsolaPerfil r2 = Consolas.VistoHomeSwitch2("18:10");
                check("1. ...y en la captura siguiente (0,5 s): Switch 2 al momento (antes 16 s), console.txt = " + LeerTxt(Path.Combine(tmp, "console.txt")),
                      r2 == Consolas.Switch2 && Consolas.Actual == Consolas.Switch2 && LeerTxt(Path.Combine(tmp, "console.txt")) == "switch2");
                check("1. Ya con la Switch 2: nada apunta a otra", Consolas.ApuntaAOtra() == null);
                // La misma captura dos veces (mismo instante) no basta
                File.WriteAllText(Path.Combine(tmp, "console.txt"), "ps5");
                Consolas.Reiniciar("auto");
                ahora = t0;
                Consolas.VistoHomeSwitch2("18:10");
                Consolas.VistoHomeSwitch2("18:10");
                check("1. Dos pruebas del HOME en el mismo instante (la misma captura): sigue la PS5", Consolas.Actual == Consolas.Ps5);
                // "Nintendo Switch 2" leido: apunta a la Switch 2 (portadas de la PS5 fuera) aunque aun no cambie
                Consolas.Reiniciar("auto");
                ahora = t0;
                Consolas.VistoTexto("The Legend of Zelda Tears of the Kingdom Nintendo Switch 2 Edition");
                check("1. \"Nintendo Switch 2\" en pantalla una vez: sigue la PS5 pero la imagen apunta a la Switch 2", Consolas.Actual == Consolas.Ps5 && Consolas.ApuntaAOtra() == Consolas.Switch2);
                ahora = t0.AddSeconds(25);
                check("1. ...25 s despues sin nada mas: ya no apunta a otra", Consolas.ApuntaAOtra() == null);
                // La PS5 de verdad: su menu cada 5 s; ni cambia ni apunta a otra
                Consolas.Reiniciar("auto");
                bool ps5ok = true;
                for (int i = 0; i < 24; i++) { ahora = t0.AddSeconds(5 * i); if (Consolas.VistoMenuPropio(Consolas.Ps5, "18:10") != null || Consolas.ApuntaAOtra() != null) ps5ok = false; }
                check("1. La PS5 con su menu cada 5 s durante 2 min: sigue la PS5 y nada apunta a otra", ps5ok && Consolas.Actual == Consolas.Ps5);
                // consola=ps5 fija: el HOME de la Switch 2 no cambia nada
                Consolas.Reiniciar("ps5");
                ahora = t0; Consolas.VistoHomeSwitch2("18:10"); ahora = t0.AddSeconds(1); Consolas.VistoHomeSwitch2("18:10");
                check("1. Con consola=ps5 en el ini: el HOME de la Switch 2 no cambia nada", Consolas.Actual == Consolas.Ps5 && Consolas.ApuntaAOtra() == null);

                // ---- 1b. La Xbox (SIN PROBAR con la consola: sin capturas reales suyas) ----
                // PS5 puesta y la Xbox delante: su marca leida (OCR cada segundo mientras apunta a otra) -> apunta a la Xbox desde la
                // primera (nada de la PS5 sobre su imagen) y cambia con 3 pruebas en 20 s (la regla de las SIN PROBAR, 20-11)
                File.WriteAllText(Path.Combine(tmp, "console.txt"), "ps5");
                Consolas.Reiniciar("auto");
                ahora = t0;
                Consolas.VistoTexto("Xbox Series X  Mis juegos y aplicaciones");
                check("1b. PS5 puesta, \"Xbox Series\" leido una vez: sigue la PS5 pero la imagen apunta a la Xbox (sin portadas ni iconos de la PS5)",
                      Consolas.Actual == Consolas.Ps5 && Consolas.ApuntaAOtra() == Consolas.XboxSeries);
                double xCuando = -1;
                for (int i = 1; i <= 40 && xCuando < 0; i++)
                {
                    ahora = t0.AddSeconds(i);
                    if (Consolas.VistoTexto(i % 2 == 0 ? "Xbox Series X" : "Mis juegos y aplicaciones") == Consolas.XboxSeries) xCuando = i;
                }
                check("1b. ...leyendo sus pantallas cada segundo: Xbox Series a los " + xCuando + " s (3 pruebas en 20 s: SIN PROBAR)", xCuando >= Consolas.SeparacionSinProbarS && xCuando <= 21 && Consolas.Actual == Consolas.XboxSeries);
                // Xbox puesta (console.txt = xboxseries) y la Switch 2 delante: su HOME 2 veces -> Switch 2 en 0,5 s
                File.WriteAllText(Path.Combine(tmp, "console.txt"), "xboxseries");
                Consolas.Reiniciar("auto");
                ahora = t0; Consolas.VistoHomeSwitch2("18:10");
                ahora = t0.AddSeconds(0.5);
                check("1b. Xbox puesta y el HOME de la Switch 2 en 2 capturas: Switch 2 al momento", Consolas.VistoHomeSwitch2("18:10") == Consolas.Switch2);
                // Xbox puesta y la PS5 delante: su menu (la hora en su sitio exacto) cada 5 s -> PS5 (probada: 2 en 5 s)
                File.WriteAllText(Path.Combine(tmp, "console.txt"), "xboxseries");
                Consolas.Reiniciar("auto");
                double pCuando = -1;
                for (int i = 0; i < 12 && pCuando < 0; i++) { ahora = t0.AddSeconds(5 * i); if (Consolas.VistoMenuPs5("18:10") == Consolas.Ps5) pCuando = 5 * i; }
                check("1b. Xbox puesta y el menu de la PS5 cada 5 s: PS5 a los " + pCuando + " s", pCuando >= 0 && pCuando <= 10 && Consolas.Actual == Consolas.Ps5);
                // Xbox puesta y su propia hora (apoyo) 2 min: sigue la Xbox; nada apunta a otra
                File.WriteAllText(Path.Combine(tmp, "console.txt"), "xboxseries");
                Consolas.Reiniciar("auto");
                bool xok = true;
                for (int i = 0; i < 24; i++) { ahora = t0.AddSeconds(5 * i); Consolas.VistoMenuXbox("18:10"); if (Consolas.ApuntaAOtra() != null) xok = false; }
                check("1b. Xbox puesta y su hora cada 5 s durante 2 min: sigue la Xbox y nada apunta a otra", xok && Consolas.Actual == Consolas.XboxSeries);
                // (23-11, "y lo mismo con xbox y viceversa") los seis cambios con su prueba fuerte: a la segunda captura (0,5 s)
                string[][] pares = {
                    new[] { "ps5", "xboxseries" }, new[] { "switch2", "xboxseries" }, new[] { "xboxseries", "ps5" },
                    new[] { "xboxseries", "switch2" }, new[] { "ps5", "switch2" }, new[] { "switch2", "ps5" } };
                foreach (string[] par in pares)
                {
                    File.WriteAllText(Path.Combine(tmp, "console.txt"), par[0]);
                    Consolas.Reiniciar("auto");
                    ahora = t0;
                    string nueva = par[1];
                    Func<ConsolaPerfil> fuerte = delegate
                    {
                        if (nueva == "xboxseries") return Consolas.VistoTexto("Mis juegos y aplicaciones  Perfil y sistema  Fiesta y chats");
                        if (nueva == "ps5") return Consolas.VistoMenuPs5Concluyente("pestañas");
                        return Consolas.VistoHomeSwitch2("18:10");
                    };
                    string pq0;
                    ConsolaPerfil a1 = fuerte();
                    bool apunta = a1 == null && Consolas.ApuntaAOtra() == Consolas.Buscar(nueva) && !Consolas.PuedeCrearJuegos(out pq0);
                    ahora = t0.AddSeconds(0.5);
                    ConsolaPerfil a2 = fuerte();
                    check("1c. " + Consolas.Buscar(par[0]).Corto + " -> " + Consolas.Buscar(nueva).Corto + " con su prueba fuerte: apunta a ella en la 1a captura (nada se crea) y cambia en la 2a",
                          apunta && a2 == Consolas.Buscar(nueva) && Consolas.Actual == Consolas.Buscar(nueva) && Consolas.PuedeCrearJuegos(out pq0));
                }
                // La Xbox de console.txt: sin confirmar no crea; una pantalla suya leida la confirma
                File.WriteAllText(Path.Combine(tmp, "console.txt"), "xboxseries");
                Consolas.Reiniciar("auto");
                string pqx;
                bool xAntes = Consolas.PuedeCrearJuegos(out pqx);
                Consolas.VistoTexto("Mis juegos y aplicaciones");
                check("1c. Xbox de console.txt: sin confirmar no crea; una pantalla suya leida la confirma", !xAntes && Consolas.PuedeCrearJuegos(out pqx));
                Consolas.Reloj = relojAntes;

                // ---- 2. Capturas reales: el HOME de la Switch 2 (nombre cian + hora con la bateria) y pantallas que no lo son ----
                List<string> imgs = new List<string>();
                foreach (string dir in new[] { datosUsuario != null ? Path.Combine(datosUsuario, @"games\debug\ps5") : null,
                                               datosUsuario != null ? Path.Combine(datosUsuario, @"games\diag_home") : null,
                                               viejo != null ? Path.Combine(viejo, "diag_home") : null,
                                               viejo != null ? Path.Combine(viejo, @"debug\ps5") : null,
                                               viejo != null ? Path.Combine(viejo, "test_deteccion") : null })
                {
                    if (dir == null || !Directory.Exists(dir)) continue;
                    foreach (string f in Directory.GetFiles(dir, "*.png"))
                        if (!f.EndsWith("_nombre.png", StringComparison.OrdinalIgnoreCase)) imgs.Add(f);
                }
                imgs.Sort(StringComparer.OrdinalIgnoreCase);
                int sw = 0, swHome = 0, otras = 0, otrasMal = 0, sinHora = 0, xboxN = 0, xboxMal = 0;
                foreach (string f in imgs)
                {
                    DateTime cuando;
                    // (las de la Xbox, dibujadas: xbox*_HHmmss.png, la hora al final)
                    bool esXbox = Path.GetFileName(f).StartsWith("xbox", StringComparison.OrdinalIgnoreCase);
                    if (esXbox)
                    {
                        string bn = Path.GetFileNameWithoutExtension(f);
                        if (!DateTime.TryParseExact("20261119-" + bn.Substring(bn.Length - 6), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out cuando)) continue;
                    }
                    else if (!Switch2Datos.HoraDeNombre(Path.GetFileName(f), out cuando)) continue;
                    using (Bitmap full = Switch2Datos.Cargar32(f))
                    using (Bitmap small = HomeLabel.Small(full))
                    {
                        Rectangle box; bool hint; HomeLabel.LabelText label; int hy;
                        DetectorMenu.OlvidaZonas();
                        bool cian = HomeLabel.Selected(small, out box, out hint, out label, out hy) != null;
                        string rh, ro, rp;
                        bool reloj = DetectorMenu.RelojSwitch(full, cuando, out rh, out ro, out rp);
                        bool home = cian && reloj;
                        // (lo que es de verdad: las de diag_home son del HOME de la Switch 2; las de debug\ps5, de la PS5 salvo la del
                        // 23-11 18:10:19, que es el HOME de la Switch 2 encima de Ocarina of Time)
                        // (vistas a mano: las del 23-11 18:08:16 y 18:08:18 y 18:10:19 de games\debug\ps5 son el HOME de la Switch 2 -la de
                        // 18:10:19 apareciendo sobre Ocarina of Time-; las demas de debug\ps5 son de la PS5; las de diag_home, de la biblioteca
                        // o del HOME de la Switch 2 sin la hora a la vista: solo se listan)
                        string fn = Path.GetFileName(f);
                        // (23-11 19:05: games\debug\ps5 guarda solo las 8 mas nuevas: las de 18:08 y 18:10 ya no estan; la de 18:55:16 es el HOME de la
                        // Switch 2 con la PS5 puesta, con el que la app de las 18:55 cambio a la Switch 2 en 0,5 s)
                        bool esSwitch = fn.StartsWith("20261123-180816", StringComparison.Ordinal) || fn.StartsWith("20261123-180818", StringComparison.Ordinal) ||
                                        fn.StartsWith("20261123-181019", StringComparison.Ordinal) || fn.StartsWith("20261123-185516", StringComparison.Ordinal);
                        bool esPs5 = f.IndexOf(@"debug\ps5", StringComparison.OrdinalIgnoreCase) >= 0 && !esSwitch;
                        if (esSwitch) { sw++; if (reloj) swHome++; else sinHora++; }
                        else if (esPs5) { otras++; if (reloj) otrasMal++; }
                        else if (esXbox) { xboxN++; if (reloj || home) xboxMal++; }
                        sb.Append("      ").Append(home ? "HOME S2 " : "-       ").Append(esSwitch ? "[switch] " : esPs5 ? "[ps5]    " : esXbox ? "[xbox]   " : "[?]      ")
                          .Append("cian ").Append(cian ? "si" : "no").Append(", hora ").Append(reloj ? "\"" + rh + "\"" : "no (" + rp + ")").Append("  ").Append(f).Append("\r\n");
                    }
                }
                check("2. Menus de la Xbox (" + xboxN + ", dibujados: no hay capturas reales suyas): ninguno da la hora del HOME de Nintendo", xboxN > 0 && xboxMal == 0);
                check("2. Capturas de la PS5 (" + otras + ", suyas y de la version de antes): ninguna da la hora del HOME de Nintendo (ni el HOME de la Switch 2)", otras > 0 && otrasMal == 0);
                check("2. Las del usuario del 23-11 con la Switch 2 delante y la PS5 puesta (las que quedan de 18:08:16, 18:08:18, 18:10:19, 18:55:16): su hora con la bateria en todas (" +
                      swHome + " de " + sw + (sw == 0 ? ": ya no estan, games\\debug\\ps5 guarda solo las 8 mas nuevas; 4 de 4 el 23-11 a las 19:06" : "") + ")", swHome == sw);
                int enteros = 0, conCian = 0;
                foreach (string f in imgs)
                {
                    string fn = Path.GetFileName(f);
                    if (!fn.StartsWith("20261123-180816", StringComparison.Ordinal) && !fn.StartsWith("20261123-180818", StringComparison.Ordinal) && !fn.StartsWith("20261123-185516", StringComparison.Ordinal)) continue;
                    conCian++;
                    if (ReconoceHome(f)) enteros++;
                }
                check("2. ...y el HOME entero (nombre cian + hora: concluyente) en las del HOME quieto (" + enteros + " de " + conCian + "; la de 18:10:19, el HOME apareciendo: la hora sola)",
                      enteros == conCian);

                // ---- 3. Las lecturas del HOME siguen entre selecciones (el caso de las 18:10:34-46) ----
                TrayApp t = new TrayApp(new Config(), true);
                double[] icOot = new double[180], icArc = new double[180];
                for (int i = 0; i < 180; i++) { icOot[i] = 40 + (i * 7) % 150; icArc[i] = 200 - (i * 11) % 160; }
                const string oot = "The Legend of Zelda: Ocarina of Time", arc = "Leyendas Pokémon: Arceus";
                DateTime tt = new DateTime(2026, 11, 23, 18, 10, 34);
                NameReads sel = t.SeleccionPrueba(icOot); sel.Add(oot, false, tt);                       // 18:10:34 (1 lectura)
                sel = t.SeleccionPrueba(icArc); sel.Add(arc, false, tt.AddSeconds(1));                  // 18:10:35 Arceus
                sel.Add(arc, false, tt.AddSeconds(1.5));
                sel = t.SeleccionPrueba(icOot);
                check("3. Vuelta a Ocarina: sigue con su lectura de antes (" + sel.Count + ")", sel.Count == 1 && sel.Best == oot && !sel.Sure);
                sel.Add(oot, false, tt.AddSeconds(2.5));                                                // 18:10:36 (antes: 1 lectura otra vez)
                check("3. ...y con otra lectura igual queda seguro (antes se empezaba de cero: \"1 lecturas\")", sel.Sure && sel.Best == oot);
                sel = t.SeleccionPrueba(icArc);
                check("3. Arceus, seguro de antes: sigue seguro sin leerlo otra vez", sel.Sure && sel.Best == arc && sel.Done);
                sel = t.SeleccionPrueba(icOot);
                check("3. Al volver a Ocarina para entrar (18:10:45): seguro -> al entrar, su nombre (ni \"Juego 1\" ni \"no esta confirmado\")", sel.Sure && sel.Best == oot);
                // Un nombre que se desplaza (marquesina) empieza de nuevo, como antes
                double[] icLargo = new double[180];
                for (int i = 0; i < 180; i++) icLargo[i] = (i * 13) % 255;
                sel = t.SeleccionPrueba(icLargo); sel.Add("The Legend of Zelda: Tears of the Kingdc", true, tt);
                NameReads otra = t.SeleccionPrueba(icArc);
                sel = t.SeleccionPrueba(icLargo);
                check("3. Un nombre largo que se desplaza (sin seguro): lecturas nuevas al volver (la marquesina vuelve a empezar)", sel.Count == 0 && otra.Sure);

                // ---- 4. Importar de la version de antes y arreglar "Juego 1" (COPIAS) ----
                if (datosUsuario != null && Directory.Exists(Path.Combine(datosUsuario, "games")))
                {
                    string g = Path.Combine(tmp, "games");
                    Directory.CreateDirectory(g);
                    // (sus datos de antes de la primera apertura con esto, el 23-11 a las 18:46:54: los .bak de esa apertura -File.Copy guarda la
                    // fecha- y el log hasta entonces)
                    foreach (string n in new[] { "profiles.txt", "game_icons.txt", "last_game.txt", "home_ref.txt" })
                    {
                        string s = Path.Combine(datosUsuario, @"games\" + n);
                        if (File.Exists(s + ".bak") && File.GetLastWriteTime(s + ".bak") < new DateTime(2026, 11, 23, 18, 46, 54)) s = s + ".bak";
                        if (File.Exists(s)) File.Copy(s, Path.Combine(g, n), true);
                    }
                    if (File.Exists(Path.Combine(g, "last_game.txt")) && File.GetLastWriteTime(Path.Combine(datosUsuario, @"games\last_game.txt")) > new DateTime(2026, 11, 23, 18, 46, 54))
                        File.WriteAllText(Path.Combine(g, "last_game.txt"), "Juego 1");
                    string logCopia = Path.Combine(tmp, "consoleenhancer.log");
                    string logU = Path.Combine(datosUsuario, @"logs\consoleenhancer.log");
                    List<string> hasta = new List<string>();
                    using (FileStream fs = new FileStream(logU, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string l;
                        while ((l = sr.ReadLine()) != null) { if (l.Length >= 23 && string.CompareOrdinal(l.Substring(0, 23), "2026-11-23 18:46:54.000") >= 0) break; hasta.Add(l); }
                    }
                    File.WriteAllLines(logCopia, hasta.ToArray(), new UTF8Encoding(false));
                    // (las portadas de la PS5 van en games\ de --datos: una copia de las del usuario, nunca las suyas)
                    bool fueraCovers = false;
                    string coverAntes = null;
                    double coverAntesD = -1;
                    string misCovers = Path.Combine(Paths.Datos, @"games\console_covers.txt"), suyos = Path.Combine(datosUsuario, @"games\console_covers.txt");
                    if (!string.Equals(Path.GetFullPath(Paths.Datos).TrimEnd('\\'), Path.GetFullPath(datosUsuario).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && File.Exists(suyos))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(misCovers));
                        File.Copy(suyos, misCovers, true);
                        try { File.Delete(Path.Combine(g, Switch2Datos.MarcaNombres)); } catch { }
                        string png = Path.Combine(datosUsuario, @"games\debug\ps5\20261123-180818_1280x720.png");
                        if (File.Exists(png))
                            using (Bitmap full = Switch2Datos.Cargar32(png)) { PortadasConsola.Olvida(); coverAntes = PortadasConsola.Busca(PortadasConsola.Firma(full), out coverAntesD); }
                        fueraCovers = true;
                    }
                    string antesP = GameProfiles.CarpetaDePrueba(g);
                    try
                    {
                        GameProfile j1 = GameProfiles.Find("Juego 1");
                        double[] icJ1 = j1 != null ? j1.Icon : null;
                        check("4. En la copia de los datos de hoy esta \"Juego 1\" (Ocarina of Time guardado sin nombre a las 18:10:46)", j1 != null && icJ1 != null && !j1.NameSure);
                        List<KeyValuePair<string, string>> ren = Switch2Datos.AlAbrir(g, null, viejo, new[] { logCopia }, new DateTime(2026, 11, 23, 19, 0, 0));
                        string imp = LeerTxt(Path.Combine(g, Switch2Datos.MarcaImportado));
                        sb.Append("      importado: ").Append((imp ?? "").Replace("\r\n", " | ")).Append("\r\n");
                        GameProfile oo = GameProfiles.Find(oot);
                        check("4. \"Juego 1\" ya no esta y su icono reconoce \"" + oot + "\" (" + (oo != null ? oo.Summary() : "-") + ")",
                              GameProfiles.Find("Juego 1") == null && oo != null && oo.Name == oot && GameProfiles.FindByIcon(icJ1) == oo);
                        check("4. last_game.txt / el juego en marcha pasan a \"" + oot + "\"", ren.Exists(delegate (KeyValuePair<string, string> kv) { return kv.Key == "Juego 1" && kv.Value == oot; }));
                        if (viejo != null)
                        {
                            check("4. Importados de la version de antes los juegos de la Switch 2 (Arceus, Rocket League...) con sus ajustes, sin los de la PS5 ni \"Consola\" ni \"Juego N\"",
                                  GameProfiles.Find(arc) != null && GameProfiles.Find("Rocket League") != null && GameProfiles.Find("Rocket League").Name == "Rocket League" &&
                                  GameProfiles.Find("PS5: Grand Theft Auto VI") != null && GameProfiles.Find("Juego 2") == null);
                            GameProfile cons = GameProfiles.Find("Consola");
                            check("4. \"Consola\" (la PS5) sigue con sus ajustes de ConsoleEnhancer (" + (cons != null ? cons.Summary() : "-") + ")", cons != null && cons.Capture == "3840x2160" && cons.Cap);
                            check("4. Ocarina of Time con los ajustes que tenia en la version de antes (1080p, 60 fps)", oo != null && oo.Capture == "1920x1080" && !oo.Cap && !oo.Mode120);
                        }
                        // La portada de "PS5: Grand Theft Auto VI" memorizada a las 18:08:18 del HOME de la Switch 2: quitada
                        if (fueraCovers)
                        {
                            string png = Path.Combine(datosUsuario, @"games\debug\ps5\20261123-180818_1280x720.png");
                            double dAntes = -1, dDespues = -1;
                            string pAntes = null, pDespues = null;
                            if (File.Exists(png))
                                using (Bitmap full = Switch2Datos.Cargar32(png))
                                {
                                    PortadasConsola.Olvida();
                                    pDespues = PortadasConsola.Busca(PortadasConsola.Firma(full), out dDespues);
                                }
                            pAntes = coverAntes; dAntes = coverAntesD;
                            if (pAntes == null) sb.Append("      5. (la portada mala ya no esta en sus datos: la quito la apertura del 23-11 a las 18:46:55; ahora casa con " + (pDespues ?? "nada") + ")\r\n");
                            check("5. La portada mala (el HOME de la Switch 2 como \"PS5: Grand Theft Auto VI\"): " + (pAntes != null ? "antes casaba (" + pAntes + " a " +
                                  dAntes.ToString("0.0", CultureInfo.InvariantCulture) + "), " : "") + "ahora no (" + (pDespues ?? "nada") + ")", pDespues == null);
                            List<string> quedan = PortadasConsola.Nombres();
                            check("5. Las demas portadas de la PS5 siguen (" + string.Join(", ", quedan.ToArray()) + ")", quedan.Count > 0);
                        }
                        // Otra vez (la siguiente apertura): no hace nada
                        int antesN = File.ReadAllLines(Path.Combine(g, "profiles.txt")).Length;
                        List<KeyValuePair<string, string>> ren2 = Switch2Datos.AlAbrir(g, null, viejo, new[] { logCopia }, new DateTime(2026, 11, 23, 19, 5, 0));
                        check("4. Al abrir otra vez: nada que hacer (una vez)", ren2.Count == 0 && File.ReadAllLines(Path.Combine(g, "profiles.txt")).Length == antesN);
                    }
                    finally { GameProfiles.CarpetaDePrueba(antesP); }
                }
                else sb.Append("      (sin datos del usuario: la parte 4 no se ha probado)\r\n");

                // ---- 6. Al reves (23-11 18:56): la Switch 2 de console.txt y la PS5 delante: "Juego 1" con la imagen de la PS5, quitado ----
                if (datosUsuario != null && Directory.Exists(Path.Combine(datosUsuario, "games")))
                {
                    string g2 = Path.Combine(tmp, "games6");
                    Directory.CreateDirectory(g2);
                    foreach (string n in new[] { "profiles.txt", "game_icons.txt", "last_game.txt", "home_ref.txt", MarcaImportadoPrueba, Switch2Datos.MarcaNombres })
                    {
                        string s = Path.Combine(datosUsuario, @"games\" + n);
                        if (File.Exists(s)) File.Copy(s, Path.Combine(g2, n), true);
                    }
                    string logTodo = Path.Combine(tmp, "consoleenhancer6.log");
                    using (FileStream fs = new FileStream(Path.Combine(datosUsuario, @"logs\consoleenhancer.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (FileStream fo = new FileStream(logTodo, FileMode.Create)) fs.CopyTo(fo);
                    string antesP6 = GameProfiles.CarpetaDePrueba(g2);
                    try
                    {
                        GameProfile j1 = GameProfiles.Find("Juego 1");
                        double[] icJ1 = j1 != null ? j1.Icon : null;
                        int antesN = GameProfiles.Nombres().Count;
                        bool hayJ1 = j1 != null && !j1.NameSure;
                        sb.Append("      6. en sus datos de ahora: " + (hayJ1 ? "\"Juego 1\" (creado a las 18:56:30 con la imagen de la PS5)" : "sin \"Juego 1\"") + ", " + antesN + " perfiles\r\n");
                        List<KeyValuePair<string, string>> r6 = Switch2Datos.AlAbrir(g2, null, viejo, new[] { logTodo }, new DateTime(2026, 11, 23, 19, 30, 0));
                        bool quitado = r6.Exists(delegate (KeyValuePair<string, string> kv) { return kv.Key == "Juego 1" && kv.Value == null; });
                        check("6. \"Juego 1\" creado con la imagen de la PS5 (Switch 2 de console.txt, cambio a la PS5 51 s despues): quitado con sus iconos" +
                              (hayJ1 ? "" : " (no estaba)"),
                              !hayJ1 || (quitado && GameProfiles.Find("Juego 1") == null && (icJ1 == null || GameProfiles.FindByIcon(icJ1) == null)));
                        check("6. Los demas perfiles siguen (" + GameProfiles.Nombres().Count + " de " + antesN + "): Ocarina, Arceus, \"Consola\", los de la PS5",
                              GameProfiles.Nombres().Count == antesN - (hayJ1 ? 1 : 0) && GameProfiles.Find("The Legend of Zelda: Ocarina of Time") != null &&
                              GameProfiles.Find("Leyendas Pokémon: Arceus") != null && GameProfiles.Find("Consola") != null && GameProfiles.Find("PS5: Grand Theft Auto VI") != null);
                        List<KeyValuePair<string, string>> r6b = Switch2Datos.AlAbrir(g2, null, viejo, new[] { logTodo }, new DateTime(2026, 11, 23, 19, 31, 0));
                        check("6. Al abrir otra vez: nada mas", r6b.Count == 0 && GameProfiles.Nombres().Count == antesN - (hayJ1 ? 1 : 0));
                    }
                    finally { GameProfiles.CarpetaDePrueba(antesP6); }
                }

                // ---- 7. Al reves en vivo (reloj falso): la Switch 2 de console.txt, sin confirmar, y la PS5 delante ----
                {
                    DateTime t7 = new DateTime(2026, 11, 23, 18, 56, 20);
                    DateTime ahora7 = t7;
                    Consolas.Reloj = delegate { return ahora7; };
                    string pq;
                    File.WriteAllText(Path.Combine(tmp, "console.txt"), "switch2");
                    Consolas.Reiniciar("auto");
                    check("7. Al abrir con la Switch 2 de console.txt: aun no crea juegos (" + (Consolas.PuedeCrearJuegos(out pq) ? "si" : pq) + ")",
                          Consolas.Actual == Consolas.Switch2 && !Consolas.PuedeCrearJuegos(out pq));
                    Consolas.VistoMenuPs5Concluyente("pestañas");
                    check("7. El menu de la PS5 (sus pestañas) en una captura: apunta a la PS5, nada se crea", Consolas.Actual == Consolas.Switch2 &&
                          Consolas.ApuntaAOtra() == Consolas.Ps5 && !Consolas.PuedeCrearJuegos(out pq));
                    ahora7 = t7.AddSeconds(0.5);
                    ConsolaPerfil r7 = Consolas.VistoMenuPs5Concluyente("18:56");
                    check("7. ...y en la siguiente (0,5 s): PS5 (antes 76 s), confirmada: ya puede crear sus juegos", r7 == Consolas.Ps5 && Consolas.PuedeCrearJuegos(out pq));
                    // La Switch 2 de verdad al abrir: su HOME la confirma
                    File.WriteAllText(Path.Combine(tmp, "console.txt"), "switch2");
                    Consolas.Reiniciar("auto");
                    ahora7 = t7;
                    Consolas.VistoHomeNintendo(true, 0);
                    check("7. Switch 2 de console.txt y su HOME (con su referencia) a la vista: confirmada, crea juegos", Consolas.Actual == Consolas.Switch2 && Consolas.PuedeCrearJuegos(out pq));
                    // La PS5 de console.txt: su menu la confirma
                    File.WriteAllText(Path.Combine(tmp, "console.txt"), "ps5");
                    Consolas.Reiniciar("auto");
                    bool antes7 = Consolas.PuedeCrearJuegos(out pq);
                    Consolas.VistoMenuPropio(Consolas.Ps5, "18:56");
                    check("7. PS5 de console.txt: sin confirmar no crea; su menu visto la confirma", !antes7 && Consolas.PuedeCrearJuegos(out pq));
                    // Con consola=switch2 fija en el ini: siempre
                    Consolas.Reiniciar("switch2");
                    check("7. consola=switch2 fija en el ini: crea juegos sin esperar a la imagen", Consolas.PuedeCrearJuegos(out pq));
                    Consolas.Reloj = relojAntes;
                }

                // ---- 8. Capturas reales: el menu de la PS5 se ve (concluyente) y el HOME de la Switch 2 no lo da ----
                {
                    int ps5Si = 0, ps5N = 0, swMal = 0, swN = 0, xbN = 0, xbSi = 0, xbMal = 0;
                    List<string> bancoPs5 = new List<string>();
                    foreach (string dir in new[] { viejo != null ? Path.Combine(viejo, "test_deteccion") : null, Environment.GetEnvironmentVariable("CE_BANCO_DETECCION") })
                        if (dir != null && Directory.Exists(dir)) bancoPs5.AddRange(Directory.GetFiles(dir, "*.png"));
                    if (datosUsuario != null)
                        foreach (string n in new[] { "20261123-180816_1280x720.png", "20261123-180818_1280x720.png", "20261123-181019_1280x720.png", "20261123-185516_1280x720.png" })
                            if (File.Exists(Path.Combine(datosUsuario, @"games\debug\ps5\" + n))) bancoPs5.Add(Path.Combine(datosUsuario, @"games\debug\ps5\" + n));
                    foreach (string f in bancoPs5)
                    {
                        string fn = Path.GetFileNameWithoutExtension(f);
                        bool menuPs5 = fn.StartsWith("menu_ce-gta-pestanas", StringComparison.Ordinal) || fn.StartsWith("ps5inicio_", StringComparison.Ordinal);
                        bool otraC = fn.StartsWith("sw2home", StringComparison.Ordinal) || fn.StartsWith("20261123-18", StringComparison.Ordinal) || fn.StartsWith("juego_sw2", StringComparison.Ordinal) ||
                                  fn.StartsWith("xbox", StringComparison.Ordinal);
                        if (!menuPs5 && !otraC) continue;
                        DateTime cuando;
                        if (fn.StartsWith("2026", StringComparison.Ordinal)) { if (!Switch2Datos.HoraDeNombre(fn + ".png", out cuando)) continue; }
                        else if (!DateTime.TryParseExact("20261122-" + fn.Substring(fn.Length - 6), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out cuando)) continue;
                        string h, fx;
                        bool visto;
                        using (Bitmap full = Switch2Datos.Cargar32(f))
                        {
                            DetectorMenu.OlvidaZonas();
                            visto = MenuPs5Visto(full, cuando, out h);
                            fx = Consolas.TextoXboxFuerteOcr(HomeLabel.ScreenText(full));
                        }
                        bool esXb = fn.StartsWith("xbox", StringComparison.Ordinal);
                        if (menuPs5) { ps5N++; if (visto) ps5Si++; }
                        else { swN++; if (visto) swMal++; }
                        if (esXb) { xbN++; if (fx != null) xbSi++; } else if (fx != null) xbMal++;
                        sb.Append("      ").Append(visto ? "MENU PS5 (" + h + ") " : "-              ").Append(fx != null ? "XBOX (" + fx + ") " : "")
                          .Append(menuPs5 ? "[ps5]  " : esXb ? "[xbox] " : "[otra] ").Append(f).Append("\r\n");
                    }
                    check("8. Menus de la PS5 de verdad (" + ps5N + "): se ven como su menu (hora o pestañas) en " + ps5Si, ps5N > 0 && ps5Si == ps5N);
                    check("8. HOME y juegos de la Switch 2 y menus de la Xbox (" + swN + "): ninguno da el menu de la PS5", swN > 0 && swMal == 0);
                    check("8. Ninguna captura de la PS5 ni de la Switch 2 da el texto fuerte de la Xbox (" + xbMal + ")", xbMal == 0);
                    sb.Append("      8. menus de la Xbox dibujados con su texto fuerte: " + xbSi + " de " + xbN + " (sin capturas reales de la Xbox)\r\n");
                }
            }
            catch (Exception ex) { check("Excepcion: " + ex, false); }
            finally
            {
                Consolas.Reloj = relojAntes;
                Consolas.CarpetaDePrueba(carpetaAntes);
                try { Directory.Delete(tmp, true); } catch { }
            }
            sb.Insert(0, "--test-switch2 (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "): " + fallos + " fallos\r\n");
            try { File.WriteAllText(informe, sb.ToString(), new UTF8Encoding(false)); } catch { }
            Console.WriteLine(sb.ToString());
            return fallos;
        }

        // (prueba) Una seleccion nueva en el HOME con este icono, como en AnalyzeGrab
        NameReads SeleccionPrueba(double[] icon)
        {
            lock (homeSync)
            {
                NameReads previas = LecturasDe(icon);
                NameReads nr = previas ?? new NameReads(icon, null);
                if (previas == null) GuardaLecturas(nr);
                selReads = nr;
                return nr;
            }
        }

        static bool ReconoceHome(string f)
        {
            DateTime cuando;
            if (f == null || !Switch2Datos.HoraDeNombre(Path.GetFileName(f), out cuando)) return false;
            using (Bitmap full = Switch2Datos.Cargar32(f))
            using (Bitmap small = HomeLabel.Small(full))
            {
                Rectangle box; bool hint; HomeLabel.LabelText label; int hy;
                string rh, ro, rp;
                DetectorMenu.OlvidaZonas();
                return HomeLabel.Selected(small, out box, out hint, out label, out hy) != null && DetectorMenu.RelojSwitch(full, cuando, out rh, out ro, out rp);
            }
        }

        static string LeerTxt(string f)
        {
            try { return File.Exists(f) ? File.ReadAllText(f).Trim() : null; } catch { return null; }
        }
    }
}
