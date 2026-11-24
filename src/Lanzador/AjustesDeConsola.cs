// AjustesDeConsola.cs - 1.3.1 (agente SWITCH2). El usuario: "arregla que de primeras no ha detectado el cambio a switch 2 despues de estar
// en ps5". Su consoleenhancer.log del 23-11: 20:22:38 la app abrio con la PS5 (games\console.txt) y sus ajustes de toda la consola
// ("Consola": 4K, modo 30, generacion del 30 efg); a las 20:22:45 vio la Switch 2, pero el primer juego (Ocarina of Time, tomado por uno
// nuevo: Switch2Magpie.cs, HomeLabel.Selected) se guardo como "Juego 1" con "los de ahora", que eran los de la PS5. Lo mismo el 23-11 a las
// 18:10:46 (r7). Un juego nuevo empezaba siempre con los ajustes que hubiera puestos, fueran de la consola que fueran.
// Ahora la app sabe de que consola son los ajustes de ahora (los de games\*.txt al abrir son de la ultima consola vista; despues, los del
// ultimo perfil puesto) y, si son de otra, un juego nuevo empieza con los de partida de la suya (BaseDeConsola): los de su ultimo juego
// (games\console_last_game.txt, consola|juego, lo escribe cada entrada en un juego con la consola confirmada por la imagen); con una consola
// sin HOME, su perfil "Consola"; y si no hay nada, los de serie. Con la misma consola, como siempre: los de ahora.
// C# 5
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleEnhancer
{
    partial class TrayApp
    {
        // La consola (su Clave) de la que son los ajustes de ahora (dlss5Level, la captura, el modo...). null = no se sabe (como antes: los de ahora)
        volatile string ajustesDe;

        // (en la carpeta de los perfiles: games\ de los datos, o la de una prueba)
        static string UltimosFile { get { return Path.Combine(GameProfiles.Carpeta, "console_last_game.txt"); } }

        // Los ajustes de ahora son de la consola c (al abrir, AplicarPerfilConsola, al entrar en un juego suyo)
        void AjustesSonDe(ConsolaPerfil c)
        {
            if (c != null) ajustesDe = c.Clave;
        }

        // Al entrar en un juego (LeaveHome, LeaveHomeGrace, EnterGame): sus ajustes son de la consola de ahora y, si la imagen la confirma, es
        // su ultimo juego (para BaseDeConsola)
        void JuegoPuestoDeConsola(GameProfile p)
        {
            ConsolaPerfil c = Consolas.Actual;
            if (c == null) return;
            AjustesSonDe(c);
            if (p == null || string.IsNullOrEmpty(p.Name)) return;
            string pq;
            if (!Consolas.PuedeCrearJuegos(out pq) && p.Name != c.PerfilConsola) return;
            try { GuardaUltimoDe(c.Clave, p.Name); }
            catch (Exception ex) { Log.Error("Guardando el ultimo juego de la consola", ex); }
        }

        // games\console_last_game.txt: "clave|juego" por consola
        internal static Dictionary<string, string> LeeUltimos()
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(UltimosFile)) return d;
                foreach (string raw in File.ReadAllLines(UltimosFile, Encoding.UTF8))
                {
                    int bar = raw.IndexOf('|');
                    if (raw.StartsWith("#") || bar <= 0) continue;
                    string k = raw.Substring(0, bar).Trim(), v = raw.Substring(bar + 1).Trim();
                    if (k.Length > 0 && v.Length > 0) d[k] = v;
                }
            }
            catch { }
            return d;
        }

        internal static string UltimoJuegoDe(string clave)
        {
            string v;
            return clave != null && LeeUltimos().TryGetValue(clave, out v) ? v : null;
        }

        internal static void GuardaUltimoDe(string clave, string juego)
        {
            if (string.IsNullOrEmpty(clave) || string.IsNullOrEmpty(juego)) return;
            Dictionary<string, string> d = LeeUltimos();
            string antes;
            if (d.TryGetValue(clave, out antes) && antes == juego) return;
            d[clave] = juego;
            StringBuilder sb = new StringBuilder("# El ultimo juego de cada consola (consola|juego): los ajustes de partida de un juego nuevo suyo si los de ahora son de otra consola\r\n");
            foreach (KeyValuePair<string, string> kv in d) sb.Append(kv.Key).Append('|').Append(kv.Value).Append("\r\n");
            Directory.CreateDirectory(Path.GetDirectoryName(UltimosFile));
            string tmp = UltimosFile + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(UltimosFile)) File.Replace(tmp, UltimosFile, null);
            else File.Move(tmp, UltimosFile);
        }

        // Un perfil nuevo con los ajustes de partida de la consola de ahora: los de ahora si son suyos (o no se sabe de cual son); si son de otra
        // consola, los de BaseDeConsola. de = por que (null: los de ahora, como siempre)
        GameProfile PerfilDePartida(string name, out string de)
        {
            ConsolaPerfil c = Consolas.Actual;
            string a = ajustesDe;
            de = null;
            if (a == null || c == null || a == c.Clave) return CurrentProfile(name);
            ConsolaPerfil otra = Consolas.Buscar(a);
            GameProfile p = BaseDeConsola(c, cfg, out de);
            p.Name = name;
            de += " (los de ahora eran de la " + (otra != null ? otra.Corto : a) + ")";
            return p;
        }

        // Los ajustes de partida de la consola c para un juego nuevo: los de su ultimo juego (games\console_last_game.txt); con una consola sin
        // HOME, su perfil "Consola"; si no, los de serie (los de un perfil nuevo, con el nivel de DLSS 5 del ini). La captura, una que saque c
        internal static GameProfile BaseDeConsola(ConsolaPerfil c, Config cfg, out string de)
        {
            GameProfile src = null;
            de = null;
            // (una consola sin HOME reconocido -PS5, Xbox...- tiene su perfil de toda la consola: ese primero; la Switch 2, su ultimo juego)
            if (c != null && !c.TieneHome)
            {
                src = GameProfiles.Find(c.PerfilConsola);
                if (src != null && src.Name != c.PerfilConsola) src = null;
                if (src != null) de = "los de partida de la " + c.Corto + ": su perfil \"" + src.Name + "\"";
            }
            string ult = src == null && c != null ? UltimoJuegoDe(c.Clave) : null;
            if (ult != null)
            {
                src = GameProfiles.Find(ult);
                if (src != null && src.Name != ult) src = null;
                if (src != null) de = "los de partida de la " + c.Corto + ": los de su ultimo juego, \"" + src.Name + "\"";
            }
            if (src == null) de = "los de serie de la " + (c != null ? c.Corto : "consola");
            GameProfile p = new GameProfile();
            if (src != null)
            {
                p.Capture = src.Capture; p.Level = src.Level; p.Passes = src.Passes; p.Dlss5 = src.Dlss5; p.Scaler = src.Scaler;
                p.ScalerPick = src.ScalerPick; p.Cap = src.Cap; p.Mode120 = src.Mode120; p.Mode40 = src.Mode40; p.Fg = src.Fg; p.Pick60 = src.Pick60; p.Pick30 = src.Pick30;
            }
            else if (cfg != null) p.Level = cfg.Dlss5DefaultLevel;
            if (c != null) p.Capture = c.CapturaCercana(p.Capture);
            return p;
        }

        // ¿Un nombre de juego leido por OCR que parece basura? No plausible (GameNames), o sin ninguna palabra de 4 letras o mas y sin mayusculas ni
        // cifras ("mn noo"; "Ico" o "nba 2k" si valen)
        internal static bool PareceBasuraOcr(string s)
        {
            if (string.IsNullOrEmpty(s) || !GameNames.Plausible(s)) return true;
            bool larga = false, marca = false;
            foreach (string w in s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int letras = 0;
                foreach (char ch in w)
                {
                    if (char.IsLetter(ch)) letras++;
                    if (char.IsUpper(ch) || char.IsDigit(ch)) marca = true;
                }
                if (letras >= 4) larga = true;
            }
            return !larga && !marca;
        }

        // (pruebas) Los ajustes de ahora como los del perfil p (lo que lee CurrentProfile), sin abrir ni guardar nada
        internal void PonAjustesPrueba(GameProfile p)
        {
            relaunchSize = null;
            cfg.VideoSize = p.Capture; dlss5Level = p.Level; dlss5Passes = p.Passes; dlss5Shown = p.Dlss5; dlssScalerOn = p.Scaler;
            scalerPick = p.ScalerPick; cfg.CapMode = p.Cap; cfg.Mode120 = p.Mode120; cfg.Mode40 = p.Mode40 && p.Cap; fgWanted = p.Fg; cfg.FgPick60 = p.Pick60; cfg.FgPick30 = p.Pick30;
        }

        // (pruebas: MismosAjustes esta en src\Lanzador\VueltaJuego.cs)

        // Al abrir (Switch2AlAbrir), una vez: sin games\console_last_game.txt, el ultimo juego (last_game.txt) es el de su consola (un "PS5: ..." de
        // la PS5, uno sin etiqueta de la Switch 2; "Consola" y los "Juego N" no)
        internal static string SiembraUltimos(string gamesDir)
        {
            string f = Path.Combine(gamesDir, "console_last_game.txt"), lg = Path.Combine(gamesDir, "last_game.txt");
            if (File.Exists(f) || !File.Exists(lg)) return null;
            string ultimo = null;
            try { ultimo = File.ReadAllText(lg, Encoding.UTF8).Trim(); } catch { }
            if (string.IsNullOrEmpty(ultimo) || GameProfiles.Find(ultimo) == null || GameProfiles.Find(ultimo).Name != ultimo) return null;
            // (la etiqueta es el nombre corto de la consola: "PS5: Bloodborne")
            string tag = JuegoConsola.TagConsola(ultimo);
            string clave = null;
            if (tag != null) { foreach (ConsolaPerfil c in Consolas.Todas) if (c.Corto == tag) { clave = c.Clave; break; } }
            else if (GameProfiles.EsDeSwitch2(ultimo)) clave = Consolas.Switch2.Clave;
            if (clave == null) return null;
            try
            {
                File.WriteAllText(f, "# El ultimo juego de cada consola (consola|juego): los ajustes de partida de un juego nuevo suyo si los de ahora son de otra consola\r\n" +
                                     clave + "|" + ultimo + "\r\n", new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Error("Sembrando el ultimo juego de cada consola", ex); return null; }
            return clave + "|" + ultimo;
        }
    }
}
