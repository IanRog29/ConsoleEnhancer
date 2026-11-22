// PruebaPerfiles.cs - 22-11 (1.2.2, agente PERFILES): prueba sin consola de lo de src\VueltaJuego.cs con los casos del log del usuario del
// 22-11 (PS5): el nombre de consenso de las lecturas de la etiqueta (la ™ / ® leida como letras), la migracion de los nombres guardados,
// y "¿se ha vuelto al juego de antes o se ha arrancado el del foco?" con capturas de verdad (antes y ahora). La llama
// --test-juegos-consola=<carpeta>[;<informe>[;<console_icons.txt>[;<carpeta de capturas 1.2.2>]]] (JuegoConsola.Prueba). C# 5
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleEnhancer
{
    static class PruebaPerfiles
    {
        static int fallos;
        static StringBuilder sb;

        static void Mira(bool ok, string que)
        {
            sb.Append(ok ? "  OK    " : "  FALLO ").Append(que).Append("\r\n");
            if (!ok) fallos++;
        }

        public static int Run(StringBuilder salida, string carpetaCapturas)
        {
            sb = salida;
            fallos = 0;
            sb.Append("\r\n== 1.2.2 (PERFILES): nombres de consenso, migracion y vuelta o arranque al salir del menu ==\r\n");
            Nombres();
            Migracion();
            Vuelta(carpetaCapturas);
            return fallos;
        }

        // ---------------- Nombres ----------------
        static void Nombres()
        {
            sb.Append("\r\n-- El nombre de las lecturas del foco (antes = MejorNombre de la 1.2.1) --\r\n");
            // (lecturas tal cual las dejo LimpiaEtiqueta en el log del 22-11)
            string[][] casos = {
                new[] { "Bloodborne", "Bloodborne|Bloodbornevy|Bloodborne|BloodborneJY|BloodborneJbf|Bloodborne", "22-11 03:59:41-44" },
                new[] { "Bloodborne", "Bloodbornevy|BloodborneJY|BloodborneJbf", "solo lecturas con basura" },
                new[] { "Bloodborne", "Bloodborney|Bloodbornevy", "las dos de los ficheros del usuario" },
                new[] { "Rocket League", "Rocket League|Rocket agu|Rocket League|Rocket Leagueñ|Rocket LeagueF|Rocket Leagueñ", "22-11 04:06:08-17" },
                new[] { "Rocket League", "Rocket Leagueñ|Rocket LeagueF|Rocket Leagueõ", "solo con la ® leida" },
                new[] { "Grand Theft Auto VI", "Theft Auto VI|Grand Theft Auto VI|Theft Auto VI", "la etiqueta entrando" },
                new[] { "Bloodborne", "Bloodborne|Bloodbornc:v|Bloodborne", "una errata" },
                new[] { "Rocket League", "Rocket Leag|Rocket League", "cortada al final (1 a 1: el largo)" },
                new[] { "Rocket League", "Rocket Leag|Rocket League|Rocket League", "cortada al final" },
                new[] { "It Takes Two", "It Takes|It Takes Two|It Takes Two", "una palabra corta de verdad" },
                new[] { "Destiny 2", "Destiny 2|Destiny 2", "un numero no es basura" },
                new[] { "Grand Theft Auto VI", "Grand Theft Auto VI|Grand Theft Auto VI", "romanos" },
                new[] { "Rocket League", "Rocket League|Rocket agu|Rocket", "1.2.1: a medias" },
                new[] { "ASTRO's PLAYROOM", "PLAYÑOOM|ASTRO's PLAYROOM|PLAYÑOOM", "1.2.1" },
                new[] { "Bloodborne", "Bloodborne|Bloodborne|Bloodborne a", "1.2.1: letra suelta" } };
            foreach (string[] q in casos)
            {
                List<string> l = new List<string>(q[1].Split('|'));
                int ap, apA;
                string got = JuegoConsola.MejorNombre(l, null, out ap);
                string antes = JuegoConsola.MejorNombreAntes(l, null, out apA);
                Mira(got == q[0], "\"" + q[1] + "\" (" + q[2] + "): \"" + got + "\" (explica " + ap + ")" + (antes != got ? "; antes \"" + antes + "\"" : "; igual que antes"));
            }
            // Con un perfil que ya existe ("PS5: Bloodborne"): una lectura suelta con basura es el
            {
                Func<string, bool> conocido = delegate (string n) { return n == "Bloodborne"; };
                int ap;
                string got = JuegoConsola.MejorNombre(new List<string> { "Bloodborney" }, delegate (string n) { return n == "Bloodborne"; }, out ap);
                // (el consenso no inventa el conocido desde una sola lectura: lo junta Find / SameGame al buscar el perfil)
                Mira(got == "Bloodborney" && GameProfiles.SameGame("PS5: Bloodborney", "PS5: Bloodborne") && GameProfiles.SameGame("PS5: BloodborneJbf", "PS5: Bloodborne"),
                     "una lectura suelta \"Bloodborney\" con \"PS5: Bloodborne\" guardado: el perfil lo encuentra SameGame (tambien \"BloodborneJbf\")");
                got = JuegoConsola.MejorNombre(new List<string> { "Bloodborneyv", "Bloodborne" }, conocido, out ap);
                Mira(got == "Bloodborne", "\"Bloodborneyv|Bloodborne\" con \"Bloodborne\" conocido: \"" + got + "\"");
                got = JuegoConsola.MejorNombre(new List<string> { "Bloodborne", "Bloodborney", "Bloodborne", "Bloodborne" }, delegate (string n) { return n == "Bloodborney"; }, out ap);
                Mira(got == "Bloodborne", "tres \"Bloodborne\" y un \"Bloodborney\" con el perfil \"PS5: Bloodborney\" guardado: \"" + got + "\" (luego se renombra)");
                got = JuegoConsola.MejorNombre(new List<string> { "Rocket Leag" }, delegate (string n) { return n == "Rocket League"; }, out ap);
                Mira(got == "Rocket Leag", "una sola lectura cortada \"Rocket Leag\": \"" + got + "\" (el perfil \"PS5: Rocket League\" lo encuentra SameGame)");
            }
            sb.Append("\r\n-- Basura pegada, base comun y renombrar un perfil --\r\n");
            object[][] bp = {
                new object[] { "Bloodbornevy", "Bloodborne", true }, new object[] { "Rocket Leagueñ", "Rocket League", true }, new object[] { "Bloodborne a", "Bloodborne", true },
                new object[] { "Destiny 2", "Destiny", false }, new object[] { "Grand Theft Auto VI", "Grand Theft Auto V", false }, new object[] { "Rocky II", "Rocky", false },
                new object[] { "Spider-Man2", "Spider-Man", false }, new object[] { "Bloodbornc:v", "Bloodborne", false }, new object[] { "FC 26", "FC 2", false } };
            foreach (object[] q in bp)
            {
                bool got = NombresConsola.BasuraPegada((string)q[0], (string)q[1]);
                Mira(got == (bool)q[2], "\"" + q[0] + "\" = \"" + q[1] + "\" con basura pegada: " + (got ? "si" : "no"));
            }
            string[][] bc = { new[] { "Bloodborney", "Bloodbornevy", "Bloodborne" }, new[] { "BloodborneJY", "Bloodbornevy", "Bloodborne" },
                              new[] { "Rocket Leagueñ", "Rocket LeagueF", "Rocket League" }, new[] { "Bloodborne", "Bloodbornc:v", "-" },
                              new[] { "Bloodborne", "Bloodbornc", "-" }, new[] { "Sackboy", "Sackbot", "-" }, new[] { "Rayman Origins", "Rayman Legends", "-" } };
            foreach (string[] q in bc)
            {
                string got = NombresConsola.BaseComun(q[0], q[1]) ?? "-";
                Mira(got == q[2], "base comun de \"" + q[0] + "\" y \"" + q[1] + "\": \"" + got + "\"");
            }
            object[][] ren = {
                new object[] { "PS5: Bloodborney", "Bloodborne", 3, false, true }, new object[] { "PS5: Bloodborney", "Bloodborne", 2, false, false },
                new object[] { "PS5: Bloodborney", "Bloodborne", 3, true, false }, new object[] { "PS5: Rocket LeagueF", "Rocket League", 2, false, true },
                new object[] { "PS5: Destiny", "Destin", 2, false, false }, new object[] { "PS5: Rocket League", "Rocket Leag", 5, false, false },
                new object[] { "PS5: Rocket", "Rocket League", 1, false, true }, new object[] { "PS5: Bloodborne", "Bloodborney", 4, false, false } };
            foreach (object[] q in ren)
            {
                bool got = JuegoConsola.MejorNombreQue((string)q[0], (string)q[1], (int)q[2], (bool)q[3]);
                Mira(got == (bool)q[4], "renombrar \"" + q[0] + "\" con \"" + q[1] + "\" (" + q[2] + " lecturas" + ((bool)q[3] ? ", el viejo tambien leido" : "") + "): " + (got ? "si" : "no"));
            }
            string[][] lim = { new[] { "BloodborneJY", "Bloodborne" }, new[] { "BloodborneTY", "Bloodborne" }, new[] { "NieR Replicant", "NieR Replicant" },
                               new[] { "Rocket League", "Rocket League" } };
            foreach (string[] q in lim)
            {
                string got = JuegoConsola.LimpiaEtiqueta(q[0]);
                Mira(got == q[1], "limpia \"" + q[0] + "\" -> \"" + got + "\"");
            }
            // La Bienvenida no da nombre ni es el menu de verdad (tambien con la errata del OCR del 22-11 04:07:29)
            Mira(JuegoConsola.EsBienvenida("Te demos la bienvenida de nuevo a PlayStation | ¿Quién está usando est..."), "Bienvenida con la errata \"Te demos\"");
            Mira(JuegoConsola.EsBienvenida("Who's using this controller?"), "Bienvenida en ingles");
            Mira(!JuegoConsola.EsBienvenida("Juegos Contenido multimedia Bienvenida Grand Theft Auto VI"), "el inicio con su seccion \"Bienvenida\" no es la Bienvenida");
        }

        // ---------------- Migracion de lo guardado ----------------
        static void Migracion()
        {
            sb.Append("\r\n-- Migracion de los nombres guardados (al abrir la app) --\r\n");
            // Los nombres de los ficheros del usuario (22-11 04:08): perfiles, iconos, portadas y el ultimo juego
            List<string> usuario = new List<string> { "Consola", "PS5: Grand Theft Auto VI", "PS5: PLAYÑOOM", "PS5: Rocket", "PS5: Bloodborney",
                                                      "PS5: Grand Theft Auto VI", "PS5: PLAYÑOOM", "PS5: Rocket", "PS5: Bloodbornevy", "PS5: Rocket", "PS5: Bloodborney", "Consola" };
            Dictionary<string, string> m = NombresConsola.Migracion(usuario);
            StringBuilder t = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in m) t.Append(t.Length > 0 ? ", " : "").Append(kv.Key).Append(" -> ").Append(kv.Value);
            Mira(m.Count == 2 && m.ContainsKey("PS5: Bloodborney") && m["PS5: Bloodborney"] == "PS5: Bloodborne" && m.ContainsKey("PS5: Bloodbornevy") &&
                 m["PS5: Bloodbornevy"] == "PS5: Bloodborne", "los del usuario: " + t);
            object[][] otros = {
                new object[] { new[] { "PS5: Rocket LeagueF" }, "PS5: Rocket LeagueF", "PS5: Rocket League" },
                new object[] { new[] { "PS5: Bloodborne", "PS5: BloodborneJY" }, "PS5: BloodborneJY", "PS5: Bloodborne" },
                new object[] { new[] { "PS5: Destiny", "PS5: Destiny 2" }, "PS5: Destiny 2", null },
                new object[] { new[] { "PS5: Destiny", "PS5: Destin" }, "PS5: Destiny", null },
                new object[] { new[] { "PS5: Bloodborney" }, "PS5: Bloodborney", null },
                new object[] { new[] { "Rocket LeagueF" }, "Rocket LeagueF", null },        // (la Switch 2 no: sin etiqueta de consola)
                new object[] { new[] { "Xbox Series: HaloJY" }, "Xbox Series: HaloJY", "Xbox Series: Halo" } };
            foreach (object[] q in otros)
            {
                Dictionary<string, string> mm = NombresConsola.Migracion(new List<string>((string[])q[0]));
                string got;
                mm.TryGetValue((string)q[1], out got);
                Mira(got == (string)q[2], "\"" + string.Join("\", \"", (string[])q[0]) + "\": \"" + q[1] + "\" -> " + (got != null ? "\"" + got + "\"" : "se queda"));
            }
            // Sobre una copia de un profiles.txt como el del usuario (mismos ajustes), y con el bueno ya guardado: queda uno
            string tmp = Path.Combine(Path.GetTempPath(), "ce_prueba_perfiles_" + System.Diagnostics.Process.GetCurrentProcess().Id);
            try
            {
                Directory.CreateDirectory(tmp);
                string cab = "# Ajustes recordados por juego (los pone el mando): nombre|captura|nivel|pasadas|dlss5|escalador|modo|generacion|nombre_seguro|tipo_generacion_60,30\r\n";
                File.WriteAllText(Path.Combine(tmp, "profiles.txt"), cab + "Consola|3840x2160|2|1|1|1|30|1|1|,dlss\r\nPS5: Grand Theft Auto VI|3840x2160|3|1|1|1|30|1|1|,xess\r\n" +
                                  "PS5: PLAYÑOOM|3840x2160|2|1|1|1|30|1|1|,dlss\r\nPS5: Rocket|2560x1440|2|1|1|1|120|1|1|,dlss\r\nPS5: Bloodborney|1920x1080|4|2|1|1|30|1|1|,dlss\r\n", new UTF8Encoding(false));
                List<string> info = new List<string>();
                List<string> fin = GameProfiles.PruebaFusiona(tmp, m, "Consola", info);
                bool ok = fin.Contains("PS5: Bloodborne|1920x1080|4|2|1|1|30|1|1|,dlss") && !string.Join("\n", fin.ToArray()).Contains("Bloodborney") && fin.Count == 6;
                Mira(ok, "profiles.txt del usuario (copia): " + string.Join("; ", info.ToArray()));
                // Con "PS5: Bloodborne" ya guardado (del 1.2.2 en otra sesion) y "PS5: Bloodborney" mas nuevo y el ultimo jugado: queda uno, con sus ajustes
                File.WriteAllText(Path.Combine(tmp, "profiles.txt"), cab + "PS5: Bloodborne|3840x2160|2|1|1|1|30|1|1|,dlss\r\nPS5: Bloodborney|1920x1080|4|2|1|1|30|1|1|,dlss\r\n", new UTF8Encoding(false));
                info.Clear();
                Dictionary<string, string> m2 = new Dictionary<string, string>(); m2["PS5: Bloodborney"] = "PS5: Bloodborne";
                fin = GameProfiles.PruebaFusiona(tmp, m2, "PS5: Bloodborney", info);
                ok = fin.Count == 2 && fin.Contains("PS5: Bloodborne|1920x1080|4|2|1|1|30|1|1|,dlss");
                Mira(ok, "con el bueno ya guardado y el de basura el ultimo jugado: " + string.Join("; ", info.ToArray()));
                File.WriteAllText(Path.Combine(tmp, "profiles.txt"), cab + "PS5: Bloodborney|1920x1080|4|2|1|1|30|1|1|,dlss\r\nPS5: Bloodborne|3840x2160|2|1|1|1|30|1|1|,dlss\r\n", new UTF8Encoding(false));
                info.Clear();
                fin = GameProfiles.PruebaFusiona(tmp, m2, "Consola", info);
                ok = fin.Count == 2 && fin.Contains("PS5: Bloodborne|3840x2160|2|1|1|1|30|1|1|,dlss");
                Mira(ok, "con el bueno ya guardado y mas nuevo: " + string.Join("; ", info.ToArray()));
            }
            catch (Exception ex) { Mira(false, "migracion sobre una copia: " + ex.Message); }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // ---------------- Vuelta o arranque ----------------
        sealed class Paso
        {
            public double T; public string Img; public bool SinSenal; public string Portada;
            public Paso(double t, string img) { T = t; Img = img; }
        }

        static Dictionary<string, double[]> firmas = new Dictionary<string, double[]>();
        static Dictionary<string, bool> tenues = new Dictionary<string, bool>();

        static double[] F(string dir, string img)
        {
            if (img == null) return null;
            double[] f;
            if (firmas.TryGetValue(img, out f)) return f;
            string p = File.Exists(img) ? img : Path.Combine(dir, img);
            if (!File.Exists(p)) return null;
            using (Bitmap src = new Bitmap(p))
            using (Bitmap full = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppRgb))
            {
                using (Graphics g = Graphics.FromImage(full)) g.DrawImage(src, 0, 0, src.Width, src.Height);
                f = PortadasConsola.Firma(full);
                int p005, p50, p995, media; double claros;
                DetectorMenu.Brillos(full, out p005, out p50, out p995, out media, out claros);
                tenues[img] = media < LecturaMenu.MediaClara || claros < LecturaMenu.ClarosClara;
            }
            firmas[img] = f;
            return f;
        }

        // Una escena: antes del menu (imagenes del juego en marcha), las capturas que sacaron del menu (t <= 0) y las de despues (t > 0,
        // con el hueco ciego de la 1.2.1 mientras Magpie preparaba). Ahora: VueltaOArranque con todas; antes (1.2.1): sin las de la salida,
        // sin capturas entre 0 y ciegoS, plazo de 8 s de reloj, la misma imagen = la ultima captura (unica) a menos de 18 en crudo, negro 1,2 s
        static void Escena(string dir, string titulo, string[] antes, List<Paso> pasos, double ciegoS, string quiere, string quiereAntes)
        {
            List<double[]> fa = new List<double[]>();
            foreach (string a in antes) { double[] f = F(dir, a); if (f != null) fa.Add(f); }
            if (fa.Count == 0) { sb.Append("  (sin imagenes) ").Append(titulo).Append("\r\n"); return; }
            DateTime t0 = new DateTime(2026, 11, 22, 3, 59, 46);
            VueltaOArranque v = new VueltaOArranque("PS5: X", "PS5: Y", fa, t0.AddSeconds(pasos.Count > 0 ? Math.Min(0, pasos[0].T) : 0));
            string dec = null; double decT = 0;
            foreach (Paso p in pasos)
            {
                double[] f = p.SinSenal ? null : F(dir, p.Img);
                bool ten = (p.Img != null && tenues.ContainsKey(p.Img) && tenues[p.Img]);
                dec = v.Observa(t0.AddSeconds(p.T), f, p.SinSenal, ten, p.Portada, false);
                if (dec != null) { decT = p.T; break; }
            }
            if (dec == null) { DateTime fin = t0.AddSeconds(pasos.Count > 0 ? pasos[pasos.Count - 1].T + 120 : 120); dec = v.Vence(fin); decT = 999; }
            // Antes (1.2.1, CandidatoObserva): la ultima imagen de antes, 8 s de reloj desde la salida, ciego mientras Magpie preparaba
            string ant = null;
            double[] ultima = fa[fa.Count - 1];
            DateTime negro = DateTime.MinValue;
            foreach (Paso p in pasos)
            {
                if (p.T <= 0 || p.T < ciegoS) continue;
                if (p.T > 8) break;
                if (p.SinSenal) { if (negro == DateTime.MinValue) negro = t0.AddSeconds(p.T); else if ((t0.AddSeconds(p.T) - negro).TotalSeconds >= 1.2) { ant = "Y"; break; } continue; }
                double[] f = F(dir, p.Img);
                if (f == null) continue;
                if (PortadasConsola.Dist(f, ultima) < 18) { ant = "X"; break; }
                if (PortadasConsola.NegroLiso(f)) { if (negro == DateTime.MinValue) negro = t0.AddSeconds(p.T); else if ((t0.AddSeconds(p.T) - negro).TotalSeconds >= 1.2) { ant = "Y"; break; } }
                else negro = DateTime.MinValue;
            }
            if (ant == null) ant = "X";
            string got = dec == "PS5: Y" ? "Y" : dec == "PS5: X" ? "X" : dec;
            Mira(got == quiere, titulo + ": ahora \"" + got + "\"" + (decT < 900 ? " a los " + decT.ToString("0.0", CultureInfo.InvariantCulture) + " s" : "") + " (" + v.Porque + ")" +
                 "; 1.2.1 \"" + ant + "\"" + (ant != quiereAntes ? " (cambia)" : ""));
        }

        static List<Paso> Serie(List<Paso> l, double desde, double hasta, double cada, string img)
        {
            for (double t = desde; t < hasta - 1e-6; t += cada) l.Add(new Paso(t, img));
            return l;
        }

        static List<Paso> SinSenal(List<Paso> l, double desde, double hasta, double cada)
        {
            for (double t = desde; t < hasta - 1e-6; t += cada) { Paso p = new Paso(t, null); p.SinSenal = true; l.Add(p); }
            return l;
        }

        static void Vuelta(string dir)
        {
            sb.Append("\r\n-- ¿Vuelta al juego de antes (X) o arranque del juego en foco (Y)? Capturas de verdad (" + (dir ?? "-") + ") --\r\n");
            if (dir == null || !Directory.Exists(dir)) { sb.Append("  (sin la carpeta de capturas: --test-juegos-consola=<carpeta>;<informe>;<console_icons.txt>;<capturas>)\r\n"); return; }
            string bb1 = "bb_hunters1.png", bb2 = "bb_hunters2.png", bb3 = "bb_bosque.png", bb4 = "bb_casa.png";
            string gta1 = "gta1.png", gta2 = "gta2.png", gta3 = "gta3.png", rlPortada = "rl_portada.png", rlCarga = "rl_cargando.png", rlPartido = "rl_partido.png";
            string negro = "negro_puro.png", particulas = "particulas.png", inicio = "inicio_cargando.png";
            // 1. El caso del log (03:59:36-58): GTA VI en marcha, foco en Bloodborne y se arranca. La 1.2.1 no miraba nada mientras Magpie
            //    preparaba (11 s): a los 8 s "no se arranco". La portada de Bloodborne no esta en el banco: la de Rocket League hace de portada
            //    nueva; luego su carga a oscuras y el negro de la PS5
            List<Paso> l = new List<Paso>();
            Serie(l, -1.2, 0.01, 0.25, rlPortada); Serie(l, 0.6, 4, 0.4, rlPortada); Serie(l, 4, 6, 0.4, negro); Serie(l, 6, 14, 0.4, rlCarga);
            Escena(dir, "22-11 03:59:46: GTA VI -> arranca el del foco (portada, negro, carga)", new[] { gta1, gta2, gta3 }, l, 11.0, "Y", "X");
            // 2. Lo mismo sin el negro liso: portada nueva y su carga a oscuras
            l = new List<Paso>();
            Serie(l, -1.2, 0.01, 0.25, rlPortada); Serie(l, 0.6, 5, 0.4, rlPortada); Serie(l, 5, 14, 0.4, rlCarga);
            Escena(dir, "arranca el del foco: portada nueva y su carga a oscuras (sin negro liso)", new[] { gta1, gta2, gta3 }, l, 11.0, "Y", "X");
            // 3. Con "sin señal" (cambio de modo de video)
            l = new List<Paso>();
            Serie(l, -1.2, 0.01, 0.25, rlPortada); SinSenal(l, 0.6, 3, 0.4); Serie(l, 3, 10, 0.4, rlPartido);
            Escena(dir, "arranca el del foco con \"sin señal\" 2,4 s", new[] { gta1, gta2, gta3 }, l, 11.0, "Y", "X");
            // 4. La salida fue por el negro tras el menu (2 s): enseguida
            l = new List<Paso>();
            Serie(l, -2.2, 0.01, 0.25, negro); Serie(l, 0.6, 8, 0.4, rlPortada);
            Escena(dir, "el negro saco del menu (arranca otro): con las capturas de la salida", new[] { gta1, gta2, gta3 }, l, 11.0, "Y", "X");
            // 5. El caso del log (04:03:57): Bloodborne en marcha, foco en GTA VI y se vuelve a Bloodborne (la misma imagen, en pausa)
            l = new List<Paso>();
            Serie(l, -1.2, 0.01, 0.25, bb1); Serie(l, 0.6, 14, 0.4, bb1);
            Escena(dir, "22-11 04:03:57: Bloodborne -> vuelve a el (la misma imagen): enseguida", new[] { bb2, bb1 }, l, 8.0, "X", "X");
            // 6. Vuelve al de antes con la escena moviendose (otra imagen del mismo juego): ni negro ni portada en 25 s mirados
            l = new List<Paso>();
            Serie(l, -1.2, 0.01, 0.25, bb3); Serie(l, 0.6, 40, 0.4, bb3);
            Escena(dir, "vuelve al de antes con otra escena suya (sin pruebas): el de antes a los 25 s mirados", new[] { bb1, bb2 }, l, 8.0, "X", "X");
            // 7. Vuelve al de antes y su menu de pausa (quieto, otra imagen: hace de el la portada de Rocket League) 30 s: nunca el del foco
            l = new List<Paso>();
            Serie(l, -1.2, 0.01, 0.25, rlPortada); Serie(l, 0.6, 30, 0.4, rlPortada);
            Escena(dir, "vuelve al de antes y se queda en su menu de pausa (quieto, distinto) 30 s: el de antes", new[] { gta1, gta2, gta3 }, l, 8.0, "X", "X");
            // 8. Lo de antes del menu era oscuro (una carga): el negro de despues no cuenta
            l = new List<Paso>();
            Serie(l, -1.2, 0.01, 0.25, negro); Serie(l, 0.6, 6, 0.4, negro); Serie(l, 6, 40, 0.4, bb4);
            Escena(dir, "lo de antes del menu era negro (cargando) y sigue negro: no es arrancar otro", new[] { negro, negro }, l, 8.0, "X", "X");
            // 9. La portada conocida del foco
            l = new List<Paso>();
            Paso pp = new Paso(-0.5, rlPortada); pp.Portada = "PS5: Y"; l.Add(pp);
            Escena(dir, "la portada conocida del foco ya al salir", new[] { gta1, gta2 }, l, 8.0, "Y", "X");
            // 10. El menu apareciendo como imagen de antes no estropea la vuelta: con la de antes de verdad (las 3 ultimas) se reconoce
            l = new List<Paso>();
            Serie(l, -1.2, 0.01, 0.25, gta2); Serie(l, 0.6, 5, 0.4, gta2);
            Escena(dir, "vuelve al de antes; entre las de antes, una del inicio apareciendo", new[] { gta1, gta2, inicio }, l, 8.0, "X", "X");
            // 11. Las particulas del arranque de la PS5 (oscuras) no son "la misma imagen" que un juego oscuro
            VueltaOArranque w = new VueltaOArranque("PS5: X", "PS5: Y", new List<double[]> { F(dir, bb4) }, DateTime.Now);
            Mira(!w.ComoAntes(F(dir, particulas)) && !w.ComoAntes(F(dir, negro)), "las particulas y el negro no se parecen a una escena oscura de Bloodborne");
            // Distancias (para ajustar): entre imagenes del mismo juego en otro momento y la misma
            double dn, dc;
            VueltaOArranque d = new VueltaOArranque("x", "y", new List<double[]> { F(dir, bb1) }, DateTime.Now);
            foreach (string q in new[] { bb1, bb2, bb3, bb4, gta1, rlPortada, particulas, inicio, negro })
            {
                double[] fq = F(dir, q);
                if (fq == null) continue;
                d.DistAntes(fq, out dn, out dc);
                sb.Append("      distancia a ").Append(bb1).Append(" de ").Append(q).Append(": ").Append(dn.ToString("0.0", CultureInfo.InvariantCulture)).Append(" sin el brillo, ")
                  .Append(dc.ToString("0.0", CultureInfo.InvariantCulture)).Append(" en crudo").Append(d.ComoAntes(fq) ? " (la misma)" : "").Append("\r\n");
            }
        }
    }
}
