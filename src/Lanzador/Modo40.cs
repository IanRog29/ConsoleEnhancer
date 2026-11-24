// Modo40.cs - 24-11-2026 (1.3.2, novena ronda, agente MODO40). C# 5
//
// El usuario: "y añade opcion de 40 fps". El MODO 40 junto al 30 y al 60: para los modos de 40 imagenes por segundo de los juegos de consola
// (los "equilibrados" de la PS5 / Xbox Series, que solo existen con la consola a 120 Hz).
//  - Va DENTRO de la familia del 30 (cfg.CapMode = true y cfg.Mode40 = true): pocas reales, repetidas siempre fuera, el tipo de generacion
//    del 30 (lo elegido con L3 / el menu en el modo 30 vale para el 40), DLSS 5 con las tablas del 30. Lo que cambia: las reales son 39,96/s
//    (presupuesto 25 ms por real), el motor recibe mode=40 (src\Motor\Engine\EngineModo40.cs) y la salida:
//      sin generacion 40 clavados (cada imagen 3 refrescos a 120 Hz) · Enhanced FG x3 -> 120 (de serie en el 40, su multiplicador va aparte:
//      games\efg40.txt), x2 -> 80 con VRR · Smooth Motion x2 -> 80 (sincronizacion forzada, la tele sigue con VRR) · x4 no (160 > 120).
//  - La captura: con 1440p / 1080p a 120 (capturas_120) si la consola saca 120 Hz (modo40_captura_120=auto: Consolas.Actual.Da120; 1 = siempre,
//    0 = nunca, a 60); a 4K (la GC573 no da 4K a 120) a 60: la cadencia 2-1 la reparte pareja el motor. En el menu de la consola / HOME a 4K
//    nativo a 60 como siempre (FramerateNow, EngineLive con HomeNative).
//  - En la consola: los modos de 40 solo salen con su salida a 120 Hz (en la PS5: Ajustes > Pantalla y vídeo > Salida de vídeo > Salida a
//    120 Hz: Automático) y una resolucion de 1440p o 1080p. El aviso del modo lo dice (Modo40Pista).
//  - games\mode.txt y la columna "modo" del perfil del juego: "40" (una version de antes lo lee como el 60).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public bool Mode40 = false;     // modo 40 fps (games\mode.txt "40"), siempre con CapMode = true
        public string Modo40Cap120 = "auto";   // ini modo40_captura_120: auto | 1 | 0

        // El modo de ahora en fps de la consola: 30 | 40 | 60 | 120
        public int ModoFps { get { return Mode120 ? 120 : CapMode && Mode40 ? 40 : CapMode ? 30 : 60; } }
        // Las reales por segundo del modo (las que deberian llegar)
        public double ModoObjetivo { get { int m = ModoFps; return m == 40 ? 39.96 : m == 30 ? 29.97 : m == 120 ? 119.88 : 59.94; } }
        public bool Mode40Now { get { return Mode40 && CapMode && !Mode120; } }

        // ¿El modo 40 captura a 120 con esta captura? (capturas_120 de la capturadora y la consola saca 120 Hz)
        public bool Modo40A120(string size)
        {
            string p = (Modo40Cap120 ?? "auto").Trim().ToLowerInvariant();
            if (p == "0" || p == "no" || p == "off" || p == "false") return false;
            if (!Captura120Ok(size)) return false;
            if (p == "1" || p == "si" || p == "on" || p == "true" || p == "yes") return true;
            try { return Consolas.Actual.Da120; } catch { return true; }
        }

        bool CeParseModo40(string key, string val)
        {
            if (key == "modo40_captura_120") { Modo40Cap120 = val.Trim(); return true; }
            return false;
        }

        // El multiplicador de Enhanced FG del modo 40 (aparte del de los demas: de serie x3 = 120; x4 no cabe). games\efg40.txt
        static int efgMult40 = -1;
        static string Efg40File { get { return Path.Combine(Paths.Datos, @"games\efg40.txt"); } }
        public static int EfgMult40
        {
            get
            {
                lock (efgSync)
                {
                    if (efgMult40 > 0) return efgMult40;
                    efgMult40 = 3;
                    try
                    {
                        int m;
                        if (File.Exists(Efg40File) && int.TryParse(File.ReadAllText(Efg40File).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out m)) efgMult40 = Math.Max(2, Math.Min(3, m));
                    }
                    catch { }
                    return efgMult40;
                }
            }
        }
        public static void EfgSet40(int m)
        {
            lock (efgSync)
            {
                efgMult40 = Math.Max(2, Math.Min(3, m));
                try { Directory.CreateDirectory(Path.GetDirectoryName(Efg40File)); File.WriteAllText(Efg40File, efgMult40.ToString(CultureInfo.InvariantCulture)); }
                catch (Exception ex) { Log.Error("Enhanced FG: guardando el multiplicador del modo 40", ex); }
            }
        }
        // (pruebas) olvidar lo leido
        public static void EfgMult40Olvida() { lock (efgSync) efgMult40 = -1; }

        // El % de entrada de DLSS 5 del motor en el modo 40, del del 30: el coste va con los puntos (% al cuadrado) y en el 40 hay 25 ms por
        // real en vez de 33,4: x raiz(25,03 / 33,37) = 0,866, a pasos de 5 y como poco el 25 % (el motor lo ajusta despues: adapta / escalera)
        public static int Modo40Pct(int v30)
        {
            if (v30 <= 0) return v30;
            return Math.Max(25, Math.Min(100, (int)(Math.Round(v30 * 0.866 / 5.0) * 5)));
        }

        // "Modo 40 fps" / "40 fps mode" (y el 120 con su "(esports)")
        public static string ModoTitulo(int fps)
        {
            if (fps == 120) return Tx.T("Modo 120 fps (esports)", "120 fps mode (esports)");
            string n = fps.ToString(CultureInfo.InvariantCulture);
            return Tx.T("Modo " + n + " fps", n + " fps mode");
        }

        // Lo que hay que saber en la consola al pasar al modo 40 (corto: va en la pastilla del aviso del modo). "" = nada
        public string Modo40Pista(string size)
        {
            string c = "";
            try { c = Consolas.Actual.Corto; } catch { }
            bool consola = c.Length > 0 && c != Consolas.Visible;
            if (Modo40A120(size))
                return consola ? Tx.T(c + ": salida a 120 Hz y el modo de 40 del juego", c + ": 120 Hz output and the game's 40 fps mode")
                               : Tx.T("consola a 120 Hz y el modo de 40 del juego", "console at 120 Hz and the game's 40 fps mode");
            if (Captura120Ok("2560x1440") || Captura120Ok("1920x1080"))
                return Tx.T("captura a 60: mejor 1440p / 1080p a 120 Hz", "capture at 60: better 1440p / 1080p at 120 Hz");
            return Tx.T("captura a 60: los 40 se reparten parejos", "capture at 60: 40 fps evened out");
        }
    }

    partial class GameProfile
    {
        public bool Mode40 = false;   // (1.3.2, MODO40) columna "modo" = 40 (con Cap = true)
        public int ModoFps { get { return Mode120 ? 120 : Cap && Mode40 ? 40 : Cap ? 30 : 60; } }
        // La columna "modo" de profiles.txt -> Cap / Mode40 / Mode120
        public void PonModo(string m)
        {
            m = (m ?? "").Trim();
            Cap = m == "30" || m == "40";
            Mode40 = m == "40";
            Mode120 = m == "120";
        }
    }

    partial class TrayApp
    {
        // El modo de un perfil que va de verdad (el 120 solo si cabe)
        int PerfilFps(GameProfile p)
        {
            if (p.Mode120 && Puede120(p.Capture)) return 120;
            return p.Cap && p.Mode40 ? 40 : p.Cap ? 30 : 60;
        }

        // Pone el modo en la config (sin reabrir nada) y lo guarda en games\mode.txt
        void PonModoCfg(int fps)
        {
            cfg.CapMode = fps == 30 || fps == 40;
            cfg.Mode40 = fps == 40;
            cfg.Mode120 = fps == 120;
            SaveText(Paths.ModeFile, fps.ToString(CultureInfo.InvariantCulture));
        }

        // ---- --test-modo40[=<informe>]: el modo 40 sin abrir nada (con --datos=<copia>: escribe games\efg40.txt alli) ----
        public static int Modo40Prueba(string informe)
        {
            StringBuilder sb = new StringBuilder();
            int fallos = 0, n = 0;
            Action<string, bool> check = delegate (string que, bool ok) { n++; if (!ok) fallos++; sb.AppendLine((ok ? "bien   " : "FALLO  ") + que); };
            FieldInfo f5 = typeof(Integrado).GetField("dlss5State", BindingFlags.NonPublic | BindingFlags.Static);
            object f5Antes = f5 != null ? f5.GetValue(null) : null;
            bool ingles = Tx.Ingles;
            try
            {
                Idioma.Poner("es");
                Pantalla.HzPrueba = 120;
                if (f5 != null) f5.SetValue(null, 1);
                Config.MotorDlss5Prueba1(1);
                Config c = new Config();
                try { c = Config.Load(); } catch { }
                c.Engine = "propio"; c.Capturas120 = new[] { "1920x1080", "2560x1440" }; c.Permit120 = "1"; c.Modo40Cap120 = "1";
                c.FgType = "smooth"; c.FgType30 = ""; c.FgType60 = ""; c.EngineVrr = 1;
                Config.MotorGenCaidaOlvida(); Config.X4NoLlegaOlvida();
                TrayApp t = new TrayApp(c, true);
                Config.EngineActive = true;
                t.lsReady = true; t.fgWanted = true; t.dlss5Shown = false;
                c.FgOff = false; c.Dlss5Off = true; c.HomeNative = false;
                // 1. el modo y la captura
                t.cfg.VideoSize = "2560x1440"; t.cfg.CapMode = true; t.cfg.Mode40 = true; t.cfg.Mode120 = false;
                t.PrepareCfg();
                check("ModoFps = 40", t.cfg.ModoFps == 40 && t.cfg.Mode40Now && Math.Abs(t.cfg.ModoObjetivo - 39.96) < 0.001);
                check("1440p: captura a 120 (" + t.cfg.FramerateNow + ")", t.cfg.FramerateNow == t.cfg.Framerate120);
                string live = EngineLive(t.cfg);
                check("al motor mode=40 (" + live.Substring(0, Math.Min(60, live.Length)) + ")", live.Contains(" mode=40 "));
                t.cfg.VideoSize = "3840x2160";
                check("4K: captura a 60 (" + t.cfg.FramerateNow + ") y mode=40", t.cfg.FramerateNow == t.cfg.Framerate && EngineLive(t.cfg).Contains(" mode=40 "));
                t.cfg.VideoSize = "2560x1440"; t.cfg.HomeNative = true;
                check("menu de la consola: mode=60 y captura a 60", EngineLive(t.cfg).Contains(" mode=60 ") && t.cfg.FramerateNow == t.cfg.Framerate);
                t.cfg.HomeNative = false; t.cfg.Modo40Cap120 = "0";
                check("modo40_captura_120=0: a 60", t.cfg.FramerateNow == t.cfg.Framerate);
                t.cfg.Modo40Cap120 = "1";
                t.cfg.CapMode = true; t.cfg.Mode40 = false;
                check("el 30 sigue: mode=30 y captura a 60", EngineLive(t.cfg).Contains(" mode=30 ") && t.cfg.FramerateNow == t.cfg.Framerate && t.cfg.ModoFps == 30);
                t.cfg.Mode40 = true;
                // 2. la generacion en el 40
                t.cfg.FgPick30 = "efg"; t.cfg.FgPick60 = "smooth";
                Config.EfgSet(4, false); Config.EfgSet40(3);
                t.PrepareCfg();
                check("Enhanced FG en el 40: x3 (" + t.cfg.EfgMultNow + ")", t.cfg.FgTypeNow == "efg" && t.cfg.EfgMultNow == 3 && t.cfg.FgMultNow == 3);
                Config.EfgSet40(4);
                check("x4 en el 40 no: se guarda x3 (" + Config.EfgMult40 + ")", Config.EfgMult40 == 3);
                Config.EfgSet40(2);
                check("Enhanced FG x2 en el 40 (80 fps)", t.cfg.EfgMultNow == 2);
                Config.EfgSet40(3);
                t.cfg.Mode40 = false;
                check("el 30 sigue con el suyo: x4 (" + t.cfg.EfgMultNow + ")", t.cfg.EfgMultNow == 4);
                t.cfg.Mode40 = true;
                t.cfg.FgPick30 = "smooth"; t.cfg.SmoothX4 = true;
                t.PrepareCfg();
                check("Smooth Motion en el 40: x2, sincronizacion forzada (sm " + t.cfg.EngineSmoothMode + ")", t.cfg.EngineSmoothMode == 1 && !t.cfg.SmoothX4Now && t.cfg.FgMultNow == 2);
                Config.FsrMultSet(4);
                check("FSR 3 FG en el 40: x2", t.cfg.FsrMultNow == 2);
                Config.FsrMultSet(2);
                // 3. el perfil del juego y el mando
                GameProfile p = new GameProfile();
                p.PonModo("40"); bool a = p.Cap && p.Mode40 && !p.Mode120 && p.ModoFps == 40;
                p.PonModo("30"); bool b = p.Cap && !p.Mode40 && p.ModoFps == 30;
                p.PonModo("60"); bool d = !p.Cap && !p.Mode40 && p.ModoFps == 60;
                p.PonModo("120"); bool e = p.Mode120 && p.ModoFps == 120;
                check("perfil: columna modo 30 / 40 / 60 / 120", a && b && d && e);
                p.PonModo("40"); p.Capture = "3840x2160";
                check("perfil a 4K con el 40: va el 40", t.PerfilFps(p) == 40);
                check("mando: 30 -> 40 -> 60", t.Modo40Siguiente(30) == 40 && t.Modo40Siguiente(40) == 60 && (t.Modo40Siguiente(60) == 120 || t.Modo40Siguiente(60) == 30) && t.Modo40Siguiente(120) == 30);
                // 4. DLSS 5 del motor en el 40
                check("DLSS 5 del 40: 100 -> 85, 75 -> 65, 45 -> 40, 25 -> 25", Config.Modo40Pct(100) == 85 && Config.Modo40Pct(75) == 65 && Config.Modo40Pct(45) == 40 && Config.Modo40Pct(25) == 25);
                // 5. el aviso del modo (con Enhanced FG x3: 40 -> 120)
                t.cfg.FgPick30 = "efg"; t.cfg.VideoSize = "2560x1440";
                t.PrepareCfg();
                foreach (string idi in new[] { "es", "en" })
                {
                    Idioma.Poner(idi);
                    string tit, det;
                    t.AvisoModo(null, 40, null, out tit, out det);
                    float w = AvisoAncho(PillText(tit, det), 2f);
                    check("aviso (" + idi + "): " + tit + " · " + det, (tit == "Modo 40 fps" || tit == "40 fps mode") && det.Contains("40 → 120 fps") && det.Contains("Enhanced FG x3") && w <= AvisoMaxW &&
                                                                     (det.Contains("120 Hz")));
                }
                Idioma.Poner("es");
            }
            catch (Exception ex) { fallos++; sb.AppendLine("FALLO excepcion: " + ex); }
            finally
            {
                Pantalla.HzPrueba = -1;
                if (f5 != null) f5.SetValue(null, f5Antes);
                Config.MotorDlss5Prueba1(-1);
                Config.EngineActive = false;
                Idioma.Poner(ingles ? "en" : "es");
            }
            string cab = "# MODO40: el modo 40 (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")\r\n" + n + " pruebas, " + fallos + " fallo(s)\r\n";
            Console.Write(cab);
            try { if (!string.IsNullOrEmpty(informe)) File.WriteAllText(informe, cab + sb.ToString(), new UTF8Encoding(false)); } catch { }
            if (fallos > 0) Console.Write(sb.ToString());
            return fallos;
        }

        // El boton Menu / Options / + (CE): 30 -> 40 -> 60 -> 120 (si cabe) -> 30
        int Modo40Siguiente(int cur)
        {
            if (cur == 30) return 40;
            if (cur == 40) return 60;
            if (cur == 60) return Puede120() ? 120 : 30;
            return 30;
        }
    }
}
