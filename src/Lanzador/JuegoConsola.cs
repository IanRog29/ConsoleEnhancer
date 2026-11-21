// JuegoConsola.cs - 20-11 (agente PS5JUEGO): reconocer CADA JUEGO de una consola con menu propio (PS5, Xbox Series) y recordarle su
// perfil, como ya se hace con la Switch 2, en vez de usar un solo perfil para toda la consola. El usuario: "con la PS5 que reconozca cada
// juego y le guarde su perfil como con la Switch 2".
//
// Como se reconoce el juego (sin fiarse de leer sobre el gameplay, que no pone el nombre en sitio fijo):
//  - PS5: al pulsar el boton PS sale el CENTRO DE CONTROL (barra abajo con tarjetas) y, con doble pulsacion, el CONMUTADOR de juegos con
//    tarjetas grandes y el nombre del juego/app en foco. Las dos pantallas llevan la hora de la PS5 arriba (RelojPs5): la app ya las toma
//    por "menu" (VistaMenu.Menu, src\DetectorMenu.cs) y pasa a 4K nativo. Ahi se lee el nombre con el OCR de Windows, igual que el nombre
//    cian del HOME de la Switch (HomeLabel): se escanea la zona del overlay y se escoge el TEXTO GRANDE y estable, descartando la hora,
//    "en linea", el nombre de usuario, los rotulos de la interfaz, etc.
//  - Xbox Series (SIN PROBAR con consola real): el boton Xbox abre la GUIA (panel a la izquierda) y la pantalla de Inicio tiene las fichas
//    de juego y los "Game Hubs" con el nombre arriba. Mismo enfoque: leer el texto grande del overlay. Implementado defensivo: si no
//    reconoce nada, cae al perfil unico de la consola ("Consola"), sin romper nada.
//
// Al leer un nombre fiable por primera vez se guarda su perfil (juegos\perfiles.txt, con la MISMA clave que la Switch pero marcada por
// consola: "PS5: <juego>" / "Xbox Series: <juego>", para no mezclar juegos de consolas distintas: GameProfiles.SameGame respeta la
// etiqueta) y se asocia una HUELLA visual estable (la tarjeta/caratula del conmutador: juegos\huellas_consola.txt) para re-reconocer ese
// juego mas tarde sin leer el nombre, como los iconos del HOME de la Switch. Con histeresis: si en un momento no hay nombre legible se
// mantiene el ultimo juego reconocido; si nunca se reconoce, el perfil unico de antes.
//
// Clave del ini (switch2.ini; en ConsoleEnhancer consoleenhancer.ini y recursos\base.ini), de serie ENCENDIDO con caida segura: ps5_reconocer_juegos=1 y xbox_reconocer_juegos=1 (0 = el perfil unico).
// Prueba sin consola: --test-juegos-consola=<carpeta de imagenes>[;<informe>] (por juego del nombre: xbox_* usa la Xbox, el resto la PS5).
// C# 5 (sin C# 6+).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ConsoleEnhancer
{
    static class JuegoConsola
    {
        // ---------------- El nombre del perfil: consola + juego ----------------
        // Se guarda como "PS5: <juego>" / "Xbox Series: <juego>" (el Corto de la consola). Asi no choca con los juegos de la Switch 2 ni de
        // otra consola: GameProfiles.SameGame / SameTitle miran la etiqueta y nunca juntan nombres de consolas distintas
        public static string NombrePerfil(ConsolaPerfil c, string juego)
        {
            if (c == null || string.IsNullOrEmpty(juego)) return null;
            return c.Corto + ": " + juego.Trim();
        }

        // La etiqueta de consola de un nombre de perfil ("PS5", "Xbox Series"...), o null si no la lleva (la Switch 2 y el perfil unico
        // "Consola" no la llevan). Se reconoce "<Corto>: " al principio, con el Corto de una consola de verdad
        public static string TagConsola(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int i = name.IndexOf(": ", StringComparison.Ordinal);
            if (i <= 0) return null;
            string pre = name.Substring(0, i);
            foreach (ConsolaPerfil c in Consolas.Todas)
                if (c != Consolas.Otra && c.Corto == pre) return pre;
            return null;
        }

        // El juego que hay dentro de un nombre de perfil con etiqueta ("PS5: Bloodborne" -> "Bloodborne"); el nombre tal cual si no la lleva
        public static string SinTag(string name)
        {
            string t = TagConsola(name);
            if (t == null) return name;
            return name.Substring(t.Length + 2);
        }

        // ¿Esta consola reconoce sus juegos uno a uno? (solo las de menu propio y con su clave del ini encendida)
        public static bool Activo(ConsolaPerfil c, Config cfg)
        {
            if (c == null || cfg == null || !c.MenuPropio) return false;
            if (c.Familia == "playstation") return cfg.Ps5RecognizeGames;
            if (c.Familia == "xbox") return cfg.XboxRecognizeGames;
            return false;
        }

        // ---------------- Leer el nombre del juego en el overlay ----------------
        // Escanea la zona del overlay (en la PS5 la barra/tarjetas y el conmutador; en la Xbox la Guia y las fichas) con el OCR de Windows,
        // se queda con el TEXTO GRANDE y lo limpia; descarta horas, porcentajes, "en linea", rotulos de interfaz y pantallas de ajustes.
        // Devuelve el nombre limpio (o null) y, en crudo, lo que leyo (para el log). NOTA: las fracciones de zona son generosas a proposito;
        // se afinan con capturas reales del centro de control / conmutador (PS5) y de la Guia / fichas (Xbox)
        public static string LeeNombre(Bitmap full, ConsolaPerfil c, out string crudo)
        {
            crudo = null;
            if (full == null || c == null) return null;
            try
            {
                // Se salta la franja de arriba (la hora) y el borde de abajo; ancho entero. El nombre grande gana por su altura de letra
                List<Ocr.Linea> lineas = LineasZona(full, 0.03, 0.06, 0.985, 0.93);
                if (lineas.Count == 0) return null;
                StringBuilder sb = new StringBuilder();
                Ocr.Linea mejor = null;
                foreach (Ocr.Linea l in lineas)
                {
                    if (sb.Length < 400) sb.Append(sb.Length > 0 ? " | " : "").Append((l.Texto ?? "").Replace("\r", " ").Replace("\n", " "));
                    if (!EsNombreJuego(l.Texto, c)) continue;
                    // El texto mas grande (la letra mas alta); a igualdad, el mas largo
                    if (mejor == null || l.Alto > mejor.Alto + 0.5 || (Math.Abs(l.Alto - mejor.Alto) <= 0.5 && Limpia(l.Texto).Length > Limpia(mejor.Texto).Length))
                        mejor = l;
                }
                crudo = sb.ToString();
                return mejor != null ? Limpia(mejor.Texto) : null;
            }
            catch (Exception ex) { Log.Error("Leyendo el nombre del juego de la consola", ex); return null; }
        }

        // Las lineas del OCR de una zona (fracciones de la imagen), reducida a <=1280 de ancho para el OCR (como HomeLabel.ScreenText)
        static List<Ocr.Linea> LineasZona(Bitmap full, double x0f, double y0f, double x1f, double y1f)
        {
            Rectangle z = Rectangle.FromLTRB((int)(full.Width * x0f), (int)(full.Height * y0f), (int)(full.Width * x1f), (int)(full.Height * y1f));
            z = Rectangle.Intersect(z, new Rectangle(0, 0, full.Width, full.Height));
            if (z.Width < 8 || z.Height < 8) return new List<Ocr.Linea>();
            int w = Math.Min(z.Width, 1280);
            int h = Math.Max(1, (int)Math.Round((double)z.Height * w / z.Width));
            using (Bitmap s = new Bitmap(w, h, PixelFormat.Format32bppRgb))
            {
                using (Graphics g = Graphics.FromImage(s))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(full, new Rectangle(0, 0, w, h), z.X, z.Y, z.Width, z.Height, GraphicsUnit.Pixel);
                }
                return Ocr.ReadLines(s);
            }
        }

        static readonly Regex HoraRx = new Regex(@"^\s*\d{1,2}\s*[:.;]\s*\d{2}\s*(a\.?\s?m\.?|p\.?\s?m\.?)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // Rotulos de la interfaz (PS5 / Xbox) y estados que NO son un juego (minusculas, sin tildes: DetectorMenu.Normal)
        static readonly string[] PantallasSistema = {
            "bienvenida", "welcome", "quien esta usando", "who's using", "whos using", "selecciona un usuario", "elige un usuario",
            "choose a user", "select a user", "pulsa el boton", "press the ps button", "press the button", "iniciar sesion", "sign in",
            "actualizacion del sistema", "system update", "modo de reposo", "rest mode", "apagando", "turning off" };
        static readonly string[] Interfaz = {
            "en linea", "sin conexion", "fuera de linea", "desconectado", "conectado", "online", "offline", "buscar", "search", "inicio",
            "home", "biblioteca", "library", "ajustes", "settings", "tienda", "store", "notificaciones", "notifications", "descargas",
            "downloads", "amigos", "friends", "fiesta", "party", "perfil", "profile", "aceptar", "cancelar", "opciones", "options",
            "atras", "back", "cerrar", "salir", "quit", "usuario", "user", "jugar", "play", "continuar", "reanudar", "resume", "cambiar",
            "mando", "controller", "bateria", "battery", "mensajes", "messages", "capturas", "capturar", "capture", "compartir", "share",
            "silenciar", "microfono", "mic", "sonido", "audio", "pantalla de inicio", "centro de control", "control center", "conmutador",
            "game base", "ps plus", "ps store", "game pass", "xbox", "playstation", "recientes", "recent", "ver todo", "see all", "mas" };

        // ¿Esa linea del OCR puede ser el nombre de un juego? Se descarta lo que seguro no lo es
        static bool EsNombreJuego(string s, ConsolaPerfil c)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string t = Limpia(s);
            if (t.Length < 3) return false;
            if (HoraRx.IsMatch(s)) return false;                       // la hora
            if (Regex.IsMatch(t, @"^\d+\s*%$")) return false;          // bateria, progreso
            // Letras de verdad: al menos la mitad de lo alfanumerico y 3 letras como poco (descarta "19:42", "100%", "12/50")
            int letras = 0, alnum = 0;
            foreach (char ch in t) { if (char.IsLetter(ch)) letras++; if (char.IsLetterOrDigit(ch)) alnum++; }
            if (letras < 3 || alnum == 0 || letras * 2 < alnum) return false;
            string n = DetectorMenu.Normal(t);
            foreach (string w in Interfaz) if (n == w) return false;    // un rotulo exacto de la interfaz
            if (n.Contains("en linea") || n.Contains("sin conexion") || n.Contains("fuera de linea")) return false;
            // 21-11 (el usuario: "siguen sin guardarse los perfiles"): la pantalla de bienvenida / eleccion de usuario de la PS5 al
            // encenderla ("Te damos la bienvenida de nuevo a PlayStation | ¿Quien esta usando este mando?") se leia como un juego y todo lo
            // jugado despues se guardaba en ese perfil. Frases de pantallas del sistema (no juegos), en español e ingles
            foreach (string f in PantallasSistema) if (n.Contains(f)) return false;
            // Una pantalla de ajustes / biblioteca / tienda de la consola (no un juego): DetectorMenu ya tiene esas frases
            string frase;
            if (DetectorMenu.EsPantallaConsola(t, c, out frase)) return false;
            return true;
        }

        // Limpia un nombre leido: espacios simples, sin la marca ® / ™ / © final, sin comillas raras
        public static string Limpia(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("®", "").Replace("™", "").Replace("©", "");
            s = Regex.Replace(s, "\\s+", " ").Trim();
            s = s.Trim('"', '\'', '|', '-', '_', ':', '.', ' ');
            return s;
        }

        // ---------------- Huella visual de respaldo (la tarjeta/caratula en foco) ----------------
        // Una firma de luma de la zona central (donde va la tarjeta del conmutador): si luego no se puede leer el nombre pero se reconoce
        // esta imagen, se vuelve a saber el juego sin OCR. Generosa: se afina con capturas reales
        const int HW = 12, HH = 9;
        public static double[] Huella(Bitmap full)
        {
            if (full == null) return null;
            try
            {
                Rectangle z = Rectangle.FromLTRB((int)(full.Width * 0.30), (int)(full.Height * 0.18), (int)(full.Width * 0.70), (int)(full.Height * 0.72));
                z = Rectangle.Intersect(z, new Rectangle(0, 0, full.Width, full.Height));
                if (z.Width < HW * 2 || z.Height < HH * 2) return null;
                double[] s = new double[HW * HH];
                int[] cnt = new int[HW * HH];
                BitmapData d = full.LockBits(z, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    byte[] row = new byte[z.Width * 4];
                    for (int y = 0; y < z.Height; y += 2)
                    {
                        Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, row.Length);
                        int cy = y * HH / z.Height;
                        for (int x = 0; x < z.Width; x += 2)
                        {
                            int o = x * 4, cx = x * HW / z.Width, i = cy * HW + cx;
                            s[i] += (row[o] * 29 + row[o + 1] * 150 + row[o + 2] * 77) >> 8;
                            cnt[i]++;
                        }
                    }
                }
                finally { full.UnlockBits(d); }
                for (int i = 0; i < s.Length; i++) if (cnt[i] > 0) s[i] /= cnt[i];
                return s;
            }
            catch { return null; }
        }

        // Distancia media por celda entre dos huellas (999 si no casan de tamaño)
        public static double DistHuella(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 999;
            double t = 0;
            for (int i = 0; i < a.Length; i++) t += Math.Abs(a[i] - b[i]);
            return t / a.Length;
        }
        public const double MismaHuella = 10.0; // por debajo, la misma tarjeta (margen amplio: la huella solo decide cuando no se lee el nombre)

        // ---------------- Prueba sin consola: --test-juegos-consola=<carpeta>[;<informe>] ----------------
        public static int Prueba(string arg)
        {
            string[] p = arg.Split(';');
            string dir = p[0], rep = p.Length > 1 ? p[1] : Path.Combine(dir, "prueba_juegos_consola.txt");
            StringBuilder sb = new StringBuilder();
            sb.Append("Lectura del nombre del juego en el overlay de la consola (centro de control / conmutador de la PS5, Guia / fichas de la Xbox)\r\n\r\n");
            List<string> files = new List<string>();
            foreach (string ext in new[] { "*.png", "*.jpg" }) if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, ext));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            int leidos = 0;
            foreach (string f in files)
            {
                string bn = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                ConsolaPerfil c = bn.Contains("xbox") ? Consolas.XboxSeries : Consolas.Ps5;
                string nombre, crudo;
                using (Bitmap src = new Bitmap(f))
                using (Bitmap full = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(full)) g.DrawImage(src, 0, 0, src.Width, src.Height);
                    nombre = LeeNombre(full, c, out crudo);
                }
                if (nombre != null) leidos++;
                sb.Append(Path.GetFileName(f)).Append("\t").Append(c.Corto).Append("\tnombre: ")
                  .Append(nombre != null ? "\"" + nombre + "\"" : "(ninguno)")
                  .Append("\tOCR: ").Append(crudo ?? "").Append("\r\n");
            }
            sb.Append("\r\nNombre leido en ").Append(leidos).Append(" de ").Append(files.Count).Append(" imagenes\r\n");
            // Mini comprobacion de los filtros (sin consola: que no se cuele la hora ni un rotulo)
            sb.Append("\r\nFiltros (EsNombreJuego):\r\n");
            string[] no = { "19:42", "100%", "En linea", "Buscar", "Ajustes", "12/50", "ab" };
            string[] si = { "Bloodborne", "God of War Ragnarok", "EA SPORTS FC 26", "Marvel's Spider-Man 2" };
            foreach (string s in no) sb.Append("  descarta \"").Append(s).Append("\": ").Append(EsNombreJuego(s, Consolas.Ps5) ? "FALLO (lo acepto)" : "OK").Append("\r\n");
            foreach (string s in si) sb.Append("  acepta   \"").Append(s).Append("\": ").Append(EsNombreJuego(s, Consolas.Ps5) ? "OK" : "FALLO (lo descarto)").Append("\r\n");
            File.WriteAllText(rep, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine(sb.ToString());
            return 0;
        }
    }

    // ===================================================================
    //  El reconocedor con histeresis (una instancia por TrayApp, con homeSync). Puro, sin la app: se le dan las lecturas del nombre (o
    //  null) y la huella de cada captura del menu/centro de control/conmutador y decide el juego actual, confirmandolo con votos (como
    //  NameReads del HOME de la Switch) y recordando su huella para re-reconocerlo sin OCR. Mantiene el ultimo juego si deja de leerse
    // ===================================================================
    sealed class ReconocedorJuego
    {
        sealed class Voto { public string Nombre; public DateTime At; }
        readonly List<Voto> votos = new List<Voto>();
        readonly Dictionary<string, double[]> huellas = new Dictionary<string, double[]>(); // juego -> su huella (en memoria; la sesion)
        string actual;              // el juego reconocido ahora (null: ninguno aun -> perfil unico)
        DateTime actualAt = DateTime.MinValue;

        public const double VentanaVotosS = 10;   // los votos de hace mas de esto no cuentan
        public const int VotosParaFijar = 2;      // 2 lecturas iguales (seguidas en el tiempo) confirman el juego

        public string Juego { get { return actual; } }

        public void Reiniciar() { votos.Clear(); actual = null; actualAt = DateTime.MinValue; }

        // Una captura del menu: nombre leido (o null) y la huella (o null). Devuelve el juego actual tras observarla
        public string Observa(string nombre, double[] huella, DateTime now)
        {
            votos.RemoveAll(delegate (Voto v) { return (now - v.At).TotalSeconds > VentanaVotosS || v.At > now; });
            if (!string.IsNullOrEmpty(nombre))
            {
                votos.Add(new Voto { Nombre = nombre, At = now });
                // ¿Hay ya VotosParaFijar lecturas del mismo juego en la ventana? (por su clave, tolerando erratas del OCR via SameGame)
                string clave = GameNames.Key(nombre);
                int n = 0;
                string repr = nombre;
                foreach (Voto v in votos)
                    if (GameNames.Key(v.Nombre) == clave || GameProfiles.SameGame(v.Nombre, nombre))
                    {
                        n++;
                        if (JuegoConsola.Limpia(v.Nombre).Length > JuegoConsola.Limpia(repr).Length) repr = v.Nombre; // el mas largo/completo
                    }
                if (n >= VotosParaFijar)
                {
                    Fijar(repr, huella, now);
                    return actual;
                }
                // Una sola lectura: no cambia aun el juego (histeresis), salvo que no haya ninguno y la huella ya lo asocie
            }
            else if (huella != null && actual == null)
            {
                // Sin nombre y sin juego aun: ¿la huella de una tarjeta ya vista? (re-reconocer sin OCR)
                string j = PorHuella(huella);
                if (j != null) { actual = j; actualAt = now; }
            }
            return actual;
        }

        void Fijar(string nombre, double[] huella, DateTime now)
        {
            actual = nombre;
            actualAt = now;
            if (huella != null) huellas[nombre] = huella; // se asocia la huella al nombre ya leido (respaldo)
        }

        string PorHuella(double[] huella)
        {
            string best = null;
            double bd = JuegoConsola.MismaHuella;
            foreach (KeyValuePair<string, double[]> kv in huellas)
            {
                double d = JuegoConsola.DistHuella(kv.Value, huella);
                if (d < bd) { bd = d; best = kv.Key; }
            }
            return best;
        }
    }
}
