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
// Al leer un nombre fiable por primera vez se guarda su perfil (games\profiles.txt, con la MISMA clave que la Switch pero marcada por
// consola: "PS5: <juego>" / "Xbox Series: <juego>", para no mezclar juegos de consolas distintas: GameProfiles.SameGame respeta la
// etiqueta) y se asocia una HUELLA visual estable (la tarjeta/caratula del conmutador: games\console_fingerprints.txt) para re-reconocer ese
// juego mas tarde sin leer el nombre, como los iconos del HOME de la Switch. Con histeresis: si en un momento no hay nombre legible se
// mantiene el ultimo juego reconocido; si nunca se reconoce, el perfil unico de antes.
//
// Clave del ini (switch2.ini; en ConsoleEnhancer consoleenhancer.ini y resources\base.ini), de serie ENCENDIDO con caida segura: ps5_reconocer_juegos=1 y xbox_reconocer_juegos=1 (0 = el perfil unico).
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
            bool fuerte;
            return LeeNombre(full, c, out crudo, out fuerte);
        }

        // La etiqueta del juego en foco del menu de la PS5: la franja bajo la fila de iconos (y 19-30 %, x 5-75 %); la linea que empieza
        // por la marca PS5 / PS4 (el OCR la lee "PS5", "PSS", "P55"...) o, sin ella, la que queda a la derecha del icono grande en foco
        static readonly Regex MarcaPs = new Regex(@"^\s*[PpF][SsBb5$][45SsA](?![\p{L}\p{N}])\s*[|:I]?\s*", RegexOptions.CultureInvariant);
        static string Etiqueta(Bitmap full, ConsolaPerfil c, out string crudo)
        {
            crudo = null;
            // Solo en la pantalla de inicio: arriba a la izquierda las pestañas "Juegos" / "Contenido multimedia" (no el centro de control
            // ni texto del juego que quede en esa franja)
            if (!EsInicio(full)) return null;
            List<Ocr.Linea> lineas = LineasZona(full, 0.05, 0.19, 0.75, 0.30);
            StringBuilder sb = new StringBuilder("etiqueta: ");
            string sinMarca = null;
            foreach (Ocr.Linea l in lineas)
            {
                string t = (l.Texto ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                sb.Append(t).Append(" | ");
                Match m = MarcaPs.Match(t);
                if (m.Success && t.Length > m.Length)
                {
                    string n = Limpia(t.Substring(m.Length));
                    n = LimpiaEtiqueta(n);
                    if (n.IndexOf('_') < 0 && EsNombreJuego(n, c)) { crudo = sb.ToString(); return n; }
                }
                else
                {
                    // (21-11: la marca PS5 leida como "P_rm" se quita con LimpiaEtiqueta; de las que quedan, la de mas letras: "p. AyrnoM" de
                    // un fundido pierde frente a "Rocket League")
                    string n = LimpiaEtiqueta(t);
                    if (n.IndexOf('_') < 0 && EsNombreJuego(n, c) && (sinMarca == null || Letras(n) > Letras(sinMarca))) sinMarca = n;
                }
            }
            crudo = sb.ToString();
            return sinMarca;
        }

        static int Letras(string s) { int n = 0; foreach (char ch in s ?? "") if (char.IsLetter(ch)) n++; return n; }

        // 1.0.12 (el usuario: "sigue dejando abrir el menu de ajustes estando en el menu"): las DOS pestañas del inicio de la PS5 ("Juegos" y
        // "Contenido multimedia" / "Games" y "Media"), para decir "es el menu" cuando la hora no se lee (fondo claro junto a la hora: "algo a
        // la derecha de la hora", 12:49:14 con GTA VI; OCR "12147"). Mas estricto que EsInicio: con una sola palabra un juego podria colar
        public static bool PestanasInicio(Bitmap full, out string leido)
        {
            bool juegos = false, media = false;
            StringBuilder sb = new StringBuilder();
            foreach (Ocr.Linea l in LineasZona(full, 0.0, 0.015, 0.55, 0.11))
            {
                string n = DetectorMenu.Normal(Limpia(l.Texto));
                sb.Append(n).Append(" | ");
                if (n == "juegos" || n == "games" || n.StartsWith("juegos ") || n.StartsWith("games ")) juegos = true;
                // (cortada por una notificacion de la PS5 encima: "Contenido multime", o pegada a ella con erratas: "contenldo tu ps5 entrara...")
                if (n.Contains("contenido multi") || n.StartsWith("conten") || n.Contains(" conten") || n == "media" || n.EndsWith(" media")) media = true;
            }
            leido = sb.ToString();
            return juegos && media;
        }

        // ¿La pantalla de inicio de la PS5? (arriba a la izquierda las pestañas "Juegos" / "Contenido multimedia")
        public static bool EsInicio(Bitmap full)
        {
            foreach (Ocr.Linea l in LineasZona(full, 0.0, 0.015, 0.55, 0.11))
            {
                string n = DetectorMenu.Normal(Limpia(l.Texto));
                if (n == "juegos" || n == "games" || n.StartsWith("contenido multimedia") || n == "media" || n.StartsWith("juegos contenido") || n.StartsWith("games media")) return true;
            }
            return false;
        }

        // 21-11 (quinta vuelta; el usuario: "tiene que ser infalible con cualquier juego"): el ICONO del juego en foco en la fila del
        // inicio de la PS5. Siempre en el mismo sitio (el grande, el segundo de la fila: x 8,8-18 %, y 11,4-27,5 % de la imagen, medido en
        // capturas reales con GTA VI y Bloodborne), igual en cada visita: identifica el juego como el icono del HOME de la Switch 2, sin
        // OCR. El fondo NO vale (cambia con retraso: Bloodborne en foco con el fondo aun de GTA VI). Luma de 12x12 celdas del interior
        public const int IcoN = 12;
        public static double[] IconoFoco(Bitmap full)
        {
            if (full == null) return null;
            return IconoZona(full, Rectangle.FromLTRB((int)(full.Width * 0.0955), (int)(full.Height * 0.128), (int)(full.Width * 0.1725), (int)(full.Height * 0.262)));
        }

        // 21-11 (Xbox): el icono del juego en foco segun la consola: PS5, su sitio fijo; Xbox Series / One, el dibujo de la tarjeta en foco
        // del Inicio (XboxFoco: la fila se mueve, el foco no esta en un sitio fijo), sin su franja del nombre
        public static double[] IconoFoco(Bitmap full, ConsolaPerfil c)
        {
            if (full == null || c == null) return null;
            if (c.Familia == "xbox")
            {
                Rectangle t = XboxFoco(full);
                if (t.IsEmpty) return null;
                return IconoZona(full, Rectangle.FromLTRB(t.Left + t.Width * 8 / 100, t.Top + t.Height * 8 / 100, t.Right - t.Width * 8 / 100, t.Top + t.Height * 74 / 100));
            }
            return IconoFoco(full);
        }

        static double[] IconoZona(Bitmap full, Rectangle z)
        {
            try
            {
                z = Rectangle.Intersect(z, new Rectangle(0, 0, full.Width, full.Height));
                if (z.Width < IcoN * 2 || z.Height < IcoN * 2) return null;
                double[] s = new double[IcoN * IcoN];
                int[] cnt = new int[IcoN * IcoN];
                BitmapData d = full.LockBits(z, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    byte[] row = new byte[z.Width * 4];
                    for (int y = 0; y < z.Height; y++)
                    {
                        Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, row.Length);
                        int cy = y * IcoN / z.Height;
                        for (int x = 0; x < z.Width; x++)
                        {
                            int o = x * 4, i = cy * IcoN + x * IcoN / z.Width;
                            s[i] += (row[o] * 29 + row[o + 1] * 150 + row[o + 2] * 77) >> 8;
                            cnt[i]++;
                        }
                    }
                }
                finally { full.UnlockBits(d); }
                // Normalizada (media 0, desviacion 50): el menu se oscurece y aclara al abrirse y cerrarse; el dibujo del icono no cambia
                double m = 0;
                for (int i = 0; i < s.Length; i++) { s[i] = cnt[i] > 0 ? s[i] / cnt[i] : 0; m += s[i]; }
                m /= s.Length;
                double v = 0;
                foreach (double x in s) v += (x - m) * (x - m);
                double sd = Math.Sqrt(v / s.Length);
                if (sd < 4) return null;   // liso: no es un icono
                for (int i = 0; i < s.Length; i++) s[i] = (s[i] - m) * 50.0 / sd;
                return s;
            }
            catch { return null; }
        }
        public const double MismoIcono = 22.0;   // distancia media por celda (normalizada) por debajo: el mismo icono
        public const double IconoQuieto = 8.0;   // entre dos capturas seguidas: el foco quieto (no a mitad de moverse)

        // ---------------- Xbox Series / One: la tarjeta en foco del Inicio ----------------
        // 21-11 (el usuario: "haz lo mismo con la Xbox series s/x, que no falle"): en el Inicio de la Xbox (el de 2023, el de ahora) la fila
        // de juegos recientes esta abajo (y 0,59-0,73) y la tarjeta en foco es mas grande (y 0,494-0,736, cuadrada), con un borde del color
        // de acento del usuario y SU NOMBRE escrito abajo sobre una franja oscura (Xbox Wire, captura oficial de 1920x1080: "Starfield" en
        // x 0,06-0,18, y 0,69-0,72). El foco se mueve por la fila (no esta en un sitio fijo, como en la PS5): se busca el borde. Con el
        // borde (una linea de color liso de 0,10-0,16 del ancho, con otra igual debajo a la distancia de un cuadrado) se tiene la tarjeta.
        // Vacio = no se ve el Inicio con una tarjeta en foco (la guia, un juego, otra pantalla)
        public static Rectangle XboxFoco(Bitmap full)
        {
            if (full == null) return Rectangle.Empty;
            try
            {
                const int W = 960;
                int H = Math.Max(1, full.Height * W / full.Width);
                int[] px = new int[W * H];
                using (Bitmap sm = new Bitmap(W, H, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(sm))
                    {
                        g.InterpolationMode = InterpolationMode.Bilinear;
                        g.DrawImage(full, new Rectangle(0, 0, W, H), 0, 0, full.Width, full.Height, GraphicsUnit.Pixel);
                    }
                    BitmapData d = sm.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                    try { for (int y = 0; y < H; y++) Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), px, y * W, W); }
                    finally { sm.UnlockBits(d); }
                }
                int minL = W * 10 / 100, maxL = W * 16 / 100;
                for (int y = H * 46 / 100; y <= H * 53 / 100; y++)
                {
                    int x0, x1, col;
                    int from = W * 2 / 100;
                    while (from < W * 97 / 100 && LineaLisa(px, W, H, y, from, minL, maxL, out x0, out x1, out col))
                    {
                        from = x1 + 1;
                        int len = x1 - x0 + 1;
                        // La de abajo: a la distancia de un cuadrado (en pixeles, alto = ancho), con el mismo color y casi el mismo tramo
                        int yb0 = y + len * 88 / 100, yb1 = Math.Min(H - 1, y + len * 112 / 100);
                        for (int yb = yb0; yb <= yb1; yb++)
                        {
                            int b0, b1, cb;
                            if (!LineaLisa(px, W, H, yb, Math.Max(0, x0 - 6), minL, maxL, out b0, out b1, out cb)) continue;
                            if (Math.Abs(b0 - x0) > 6 || Math.Abs(b1 - x1) > 6 || ColorDist(col, cb) > 60) continue;
                            double fx = (double)full.Width / W, fy = (double)full.Height / H;
                            return Rectangle.FromLTRB((int)(x0 * fx), (int)(y * fy), (int)((x1 + 1) * fx), (int)((yb + 1) * fy));
                        }
                    }
                }
            }
            catch { }
            return Rectangle.Empty;
        }

        // En la fila y, desde x: el primer tramo de color liso (cada punto como el de al lado), con color (no negro ni gris apagado), de
        // largo entre minL y maxL, distinto de lo de 3 filas mas arriba (un borde, no una zona lisa del fondo)
        static bool LineaLisa(int[] px, int W, int H, int y, int from, int minL, int maxL, out int x0, out int x1, out int col)
        {
            x0 = x1 = col = 0;
            if (y < 3 || y >= H) return false;
            int x = from;
            while (x < W - 1)
            {
                int s = x;
                int c0 = px[y * W + x];
                while (x + 1 < W && ColorDist(px[y * W + x + 1], px[y * W + x]) < 24 && ColorDist(px[y * W + x + 1], c0) < 70) x++;
                int len = x - s + 1;
                if (len >= minL && len <= maxL)
                {
                    int m = px[y * W + (s + x) / 2];
                    int r = (m >> 16) & 255, g = (m >> 8) & 255, b = m & 255;
                    int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                    bool vivo = mx >= 90 && (mx - mn >= 40 || mx >= 170);
                    int dif = 0, n = 0;
                    for (int k = s; k <= x; k += 4) { dif += ColorDist(px[y * W + k], px[(y - 3) * W + k]); n++; }
                    if (vivo && n > 0 && dif / n >= 45) { x0 = s; x1 = x; col = m; return true; }
                }
                x++;
            }
            return false;
        }

        static int ColorDist(int a, int b)
        {
            return Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255)) + Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) + Math.Abs((a & 255) - (b & 255));
        }

        // El nombre de la tarjeta en foco del Inicio de la Xbox: su franja de abajo (el ultimo 20 % de la tarjeta)
        static string EtiquetaXbox(Bitmap full, ConsolaPerfil c, out string crudo)
        {
            crudo = null;
            Rectangle t = XboxFoco(full);
            if (t.IsEmpty) return null;
            double x0 = (t.Left + t.Width * 0.03) / full.Width, x1 = (t.Right - t.Width * 0.03) / full.Width;
            double y0 = (t.Top + t.Height * 0.78) / full.Height, y1 = (t.Bottom - t.Height * 0.02) / full.Height;
            StringBuilder sb = new StringBuilder("etiqueta xbox (" + t.X + "," + t.Y + " " + t.Width + "x" + t.Height + "): ");
            string mejor = null;
            foreach (Ocr.Linea l in LineasZona(full, x0, y0, x1, y1))
            {
                string tx = (l.Texto ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                sb.Append(tx).Append(" | ");
                string n = LimpiaEtiqueta(tx);
                if (mejor == null && n.IndexOf('_') < 0 && EsNombreJuego(n, c)) mejor = n;
            }
            crudo = sb.ToString();
            return mejor;
        }

        // Un nombre leido sin la basura de alrededor: la marca PS5/PS4 leida como "p_ra" / "P_j-m" / "p_rzl" delante y el ™ leido como
        // "TM" / "TY" / "Tt" pegado detras, y signos sueltos ("Bloor]borno'?")
        public static string LimpiaEtiqueta(string s)
        {
            s = Limpia(s);
            s = Regex.Replace(s, @"^(?:\S*[_\]\[]\S*\s+)+", "");
            s = Regex.Replace(s, @"(?<=\p{Ll})['’]?(?:TM|TY|Tt|T)(?:\W.*)?$", "");
            s = Regex.Replace(s, @"[\[\]|•]", "");
            s = Regex.Replace(s, @"[^\p{L}\p{N}]+$", "");
            // (1.2.04) el ® de "Rocket League®" leido como "n": "Rocket Leaguen" (src\Lanzador\Ortografia.cs)
            s = Ortografia.QuitaMarcaPegada(s.Trim());
            return s.Trim();
        }

        // 21-11 (segunda vuelta; el usuario: "sigue igual lo de guardar perfiles de juegos en ps5"): fuerte = la PAGINA DEL JUEGO del
        // menu de la PS5 (su logo / titulo grande con el boton "Jugar" debajo), lo unico tan fiable como el nombre del HOME de la Switch:
        // con una sola lectura fuerte vale. Lo demas (tarjetas, anuncios, pestañas, el centro de control) necesita 2 lecturas iguales y
        // nunca cambia el juego al entrar con una sola. La pantalla de Bienvenida (anuncios: "Vice City, USA" de GTA VI) no da nombre
        public static string LeeNombre(Bitmap full, ConsolaPerfil c, out string crudo, out bool fuerte)
        {
            crudo = null;
            fuerte = false;
            if (full == null || c == null) return null;
            try
            {
                // 21-11 (tercera vuelta; el usuario: "sigue sin ir"): el MENU de la PS5 pone el nombre del juego en foco SIEMPRE en el
                // mismo sitio, debajo de la fila de iconos, tras la etiqueta "PS5" / "PS4" ("PS5 | Grand Theft Auto VI"): como el nombre
                // cian del HOME de la Switch 2. Esa etiqueta vale a la primera (fuerte). Lo grande de la pagina ("Continua donde lo dejaste")
                // no es el nombre: antes se buscaba el texto mas grande y con GTA VI no salia nunca
                if (c.Familia == "playstation" || c.Familia == "xbox")
                {
                    string et = c.Familia == "xbox" ? EtiquetaXbox(full, c, out crudo) : Etiqueta(full, c, out crudo);
                    if (et != null) { fuerte = true; return et; }
                    // 21-11 (Xbox): sin la tarjeta en foco del Inicio, nada (la guia y sus paneles estan llenos de rotulos: "Pins", "Favorites",
                    // "My subscriptions"... con 2 lecturas iguales se colarian). El juego lo dan la tarjeta del Inicio y su portada al arrancar
                    if (c.Familia == "xbox") return null;
                }
                // Se salta la franja de arriba (la hora) y el borde de abajo; ancho entero. El nombre grande gana por su altura de letra
                List<Ocr.Linea> lineas = LineasZona(full, 0.03, 0.06, 0.985, 0.93);
                if (lineas.Count == 0) return null;
                StringBuilder sb = new StringBuilder();
                foreach (Ocr.Linea l in lineas)
                    if (sb.Length < 400) sb.Append(sb.Length > 0 ? " | " : "").Append((l.Texto ?? "").Replace("\r", " ").Replace("\n", " "));
                crudo = sb.ToString();
                bool boton = false;
                foreach (Ocr.Linea l in lineas)
                {
                    string n = DetectorMenu.Normal(Limpia(l.Texto));
                    if (n == "bienvenida" || n == "welcome" || n.StartsWith("te damos la bienvenida") || n.StartsWith("welcome back")) return null;
                    if ((n.StartsWith("jugar") || n.StartsWith("play")) && n.Length <= 9 && !n.StartsWith("playstation")) boton = true;
                }
                string nombre = EligeNombre(JuntaLogo(lineas), c);
                // 21-11 (cuarta vuelta): la pantalla entera NUNCA vale a la primera (en mitad de una animacion del menu salio "ajeasdoy Lu"
                // con el boton Jugar y GTA VI guardo sus ajustes ahi): solo la etiqueta del juego en foco; esto necesita 2 lecturas iguales
                fuerte = false;
                return nombre;
            }
            catch (Exception ex) { Log.Error("Leyendo el nombre del juego de la consola", ex); return null; }
        }

        // 21-11 (el usuario: "siguen sin guardarse los perfiles de los juegos"): de las lineas que pueden ser un juego, el texto mas grande
        // (a igualdad, el mas largo), pero si el OCR leyo el mismo nombre varias veces con erratas distintas ("Bloodbornc | BloodborneTY |
        // Bfoodborne", el logo, la tarjeta y el titulo) se juntan letra a letra por mayoria: "Bloodborne"
        static string EligeNombre(List<Ocr.Linea> lineas, ConsolaPerfil c)
        {
            List<Ocr.Linea> cand = new List<Ocr.Linea>();
            foreach (Ocr.Linea l in lineas) if (EsNombreJuego(l.Texto, c)) cand.Add(l);
            Ocr.Linea mejor = null;
            foreach (Ocr.Linea l in cand)
                if (mejor == null || l.Alto > mejor.Alto + 0.5 || (Math.Abs(l.Alto - mejor.Alto) <= 0.5 && Limpia(l.Texto).Length > Limpia(mejor.Texto).Length))
                    mejor = l;
            if (mejor == null) return null;
            string m = Limpia(mejor.Texto);
            List<string> grupo = new List<string> { m };
            foreach (Ocr.Linea l in cand)
            {
                if (l == mejor) continue;
                string t = Limpia(l.Texto);
                if (t.Length >= m.Length && Parecido(t.Substring(0, m.Length), m)) grupo.Add(t);
            }
            return grupo.Count >= 3 ? Mayoria(grupo, m.Length) : m;
        }

        // Letra a letra, la que mas se repite (sin mayusculas ni tildes al comparar; a igualdad, la del texto mas grande, el primero)
        static string Mayoria(List<string> grupo, int len)
        {
            StringBuilder r = new StringBuilder();
            for (int i = 0; i < len; i++)
            {
                char best = grupo[0][i];
                int bestN = 0;
                foreach (string a in grupo)
                {
                    int n = 0;
                    foreach (string b in grupo) if (GameNames.Key(a[i].ToString()) == GameNames.Key(b[i].ToString())) n++;
                    if (n > bestN) { bestN = n; best = a[i]; }
                }
                r.Append(best);
            }
            return r.ToString();
        }

        // Los logos en dos lineas ("ROCKET" encima de "LEAGUE.") son un solo nombre: dos lineas en MAYUSCULAS de 1-2 palabras, de letra
        // parecida, una justo debajo de la otra y empezando cerca
        static List<Ocr.Linea> JuntaLogo(List<Ocr.Linea> lineas)
        {
            List<Ocr.Linea> r = new List<Ocr.Linea>(lineas);
            for (int i = 0; i < lineas.Count; i++)
                for (int j = 0; j < lineas.Count; j++)
                {
                    Ocr.Linea a = lineas[i], b = lineas[j];
                    if (i == j || !Logo(a.Texto) || !Logo(b.Texto) || a.Alto < 4) continue;
                    if (Math.Abs(a.Alto - b.Alto) > a.Alto * 0.35) continue;
                    double dy = b.Y - a.Y;
                    if (dy <= a.Alto * 0.5 || dy > a.Alto * 1.9 || Math.Abs(b.X - a.X) > a.Alto * 2.5) continue;
                    r.Add(new Ocr.Linea { Texto = Limpia(a.Texto) + " " + Limpia(b.Texto), Alto = Math.Max(a.Alto, b.Alto) + 0.6, X = a.X, Y = a.Y });
                }
            return r;
        }

        static bool Logo(string s)
        {
            string t = Limpia(s);
            if (t.Length < 2 || t.Split(' ').Length > 2) return false;
            int may = 0;
            foreach (char ch in t) { if (char.IsLower(ch)) return false; if (char.IsUpper(ch)) may++; }
            return may >= 2;
        }

        static string Cifras(string s)
        {
            StringBuilder r = new StringBuilder();
            foreach (string w in Regex.Split(s, @"[^\p{L}\p{N}]+"))
            {
                int d = 0, l = 0;
                foreach (char ch in w) { if (char.IsDigit(ch)) d++; else if (char.IsLetter(ch)) l++; }
                if (d > 0 && (d >= l || w.Length <= 3)) r.Append(Regex.Replace(w, "[^0-9]", "")).Append(' ');
            }
            return r.ToString();
        }

        static string SinCifrasOcr(string s)
        {
            StringBuilder r = new StringBuilder();
            foreach (string w in Regex.Split(s, @"[^\p{L}\p{N}]+"))
            {
                int d = 0, l = 0;
                foreach (char ch in w) { if (char.IsDigit(ch)) d++; else if (char.IsLetter(ch)) l++; }
                string k = GameNames.Key(w);
                if (d > 0 && !(d >= l || w.Length <= 3))
                {
                    StringBuilder m = new StringBuilder();
                    foreach (char ch in k) m.Append(ch == '0' ? 'o' : ch == '1' ? 'l' : ch == '5' ? 's' : ch == '6' || ch == '8' ? 'b' : ch == '4' ? 'a' : ch == '3' ? 'e' : ch);
                    k = m.ToString();
                }
                r.Append(k);
            }
            return r.ToString();
        }

        static string Romanos(string s)
        {
            StringBuilder r = new StringBuilder();
            foreach (Match m in Regex.Matches(s, @"\b[IVX]{1,4}\b")) r.Append(m.Value).Append(' ');
            return r.ToString();
        }

        // ¿El mismo juego con erratas del OCR? (nombres de 8 letras o mas, las mismas cifras, como mucho 1 cambio cada 5 letras:
        // "Bfoodborne" = "Bloodborne"; las cifras distintas son otro juego: "FC 25" / "FC 26")
        public static bool Parecido(string a, string b)
        {
            string sa = SinTag(a ?? ""), sb2 = SinTag(b ?? "");
            // Las cifras de verdad (un numero suelto o casi: "FC 26", "2K25", "Spider-Man 2") tienen que ser las mismas; las de dentro de
            // una palabra son erratas del OCR ("Bfood60rne" = "Bloodborne"): se comparan como la letra que parecen
            if (Cifras(sa) != Cifras(sb2)) return false;
            string x = SinCifrasOcr(sa), y = SinCifrasOcr(sb2);
            int min = Math.Min(x.Length, y.Length);
            if (min < 8) return false;
            if (Romanos(SinTag(a ?? "")) != Romanos(SinTag(b ?? ""))) return false;   // "Grand Theft Auto V" no es "... VI"
            // (1.2.04) el nombre cortado por delante (la etiqueta a medio entrar: "Theft Auto VI" = "Grand Theft Auto VI"): el final entero,
            // desde el principio de una palabra, con 2 palabras o mas
            {
                string na = Regex.Replace(sa.Trim().ToLowerInvariant(), @"\s+", " "), nb = Regex.Replace(sb2.Trim().ToLowerInvariant(), @"\s+", " ");
                string corto = na.Length <= nb.Length ? na : nb, largo = na.Length <= nb.Length ? nb : na;
                if (corto.Length < largo.Length && corto.IndexOf(' ') > 0 && largo.EndsWith(" " + corto, StringComparison.Ordinal)) return true;
            }
            int max = Math.Max(1, min / 5);
            if (Math.Abs(x.Length - y.Length) > max) return false;
            int[] prev = new int[y.Length + 1], cur = new int[y.Length + 1];
            for (int j = 0; j <= y.Length; j++) prev[j] = j;
            for (int i = 1; i <= x.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= y.Length; j++)
                    cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1));
                int[] t = prev; prev = cur; cur = t;
            }
            return prev[y.Length] <= max;
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
            "actualizacion del sistema", "system update", "modo de reposo", "rest mode", "apagando", "turning off",
            // (1.0.12: rotulos de la galeria y de la app de PlayStation: "Escanea el codigo QR para descargar PS App" se tomo por un juego)
            "codigo qr", "qr code", "escanea el", "scan the", "ps app", "playstation app", "descargar", "download the", "tus capturas",
            "your captures", "inicio para tus", "home for your", "galeria", "gallery" };
        static readonly string[] FrasesTarjeta = {
            "reanudar", "resume game", "continua donde", "continue where", "pick up where", "videoclip", "video clip", "captura de pantalla",
            "screenshot", "de reciente creacion", "recently created", "historias de tus juegos", "noticias destacadas", "featured news",
            "un unico jugador", "un solo jugador", "single player", "single-player", "multijugador", "multiplayer", "ver detalles",
            "view details", "actividad", "activit", "juega ahora", "play now", "descubre", "discover", "trofeo", "trophies", "trophy",
            "desafio", "challenge", "partida", "transmitir", "broadcast", "amigos jugando", "friends who play", "complementos", "add-ons",
            "anadir a la biblioteca", "comprar", "buy now", "mas informacion", "more info",
            // 21-11 (Xbox: el Inicio y la guia, en ingles y en español)
            "quick resume", "reanudacion rapida", "browse your games", "explora tus juegos", "customize your home", "personaliza tu inicio",
            "play like a pro", "jump back in", "see all", "full library", "biblioteca completa", "play history", "historial de juegos",
            "share last capture", "compartir la ultima captura", "record what happened", "capture screenshot", "more options",
            "mas opciones", "my games", "mis juegos", "capture options", "xbox support", "appear offline", "add to home" };
        static readonly string[] Interfaz = {
            "en linea", "sin conexion", "fuera de linea", "desconectado", "conectado", "online", "offline", "buscar", "search", "inicio",
            "home", "biblioteca", "library", "ajustes", "settings", "tienda", "store", "notificaciones", "notifications", "descargas",
            "downloads", "amigos", "friends", "fiesta", "party", "perfil", "profile", "aceptar", "cancelar", "opciones", "options",
            "atras", "back", "cerrar", "salir", "quit", "usuario", "user", "jugar", "play", "continuar", "reanudar", "resume", "cambiar",
            "mando", "controller", "bateria", "battery", "mensajes", "messages", "capturas", "capturar", "capture", "compartir", "share",
            "silenciar", "microfono", "mic", "sonido", "audio", "pantalla de inicio", "centro de control", "control center", "conmutador",
            "game base", "ps plus", "ps store", "game pass", "xbox", "playstation", "recientes", "recent", "ver todo", "see all", "mas", "mapa", "map", "ayuda", "help", "estudio fotografico", "modo foto",
            "photo mode", "galeria", "gallery", "pausa", "pause", "menu", "salir del juego", "exit game", "juegos", "games", "multimedia",
            "media", "explorar", "explore", "bienvenida", "welcome", "juega ahora", "todos los juegos", "all games", "tu biblioteca",
            "your library", "instalados", "installed", "noticias", "news", "oficial", "official" };

        static string MasParecidaNorm(List<double[]> fs, int i, List<string> files)
        {
            double best = 999; int bj = -1;
            for (int j = 0; j < fs.Count; j++) if (j != i) { double d = PortadasConsola.DistNorm(fs[i], fs[j]); if (d < best) { best = d; bj = j; } }
            return (bj >= 0 ? Path.GetFileName(files[bj]) : "-") + " a " + best.ToString("0.0", CultureInfo.InvariantCulture);
        }

        // 1.0.12 (el usuario: "tambien piensa que he entrado a un juego al entrar a remoteplay, galeria, etc. arregla esas cosas, en todas las
        // consolas"): las apps de la consola (y de video) que estan en la fila de juegos del inicio: abrirlas no es entrar en un juego. Dentro
        // (una captura de la galeria a pantalla completa, Remote Play, un video) la imagen no se distingue de un juego: lo que manda es la app
        // en foco al salir del menu (MenuPs5Salir). Minusculas sin tildes; las de una palabra, exactas; las de varias, contenidas
        static readonly string[] AppsSistema = {
            // PS5
            "remote play", "ps remote play", "galeria de contenido multimedia", "galeria multimedia", "galeria", "media gallery", "gallery",
            "playstation store", "ps store", "playstation plus", "ps plus", "explorar", "explore", "biblioteca de juegos", "game library",
            "biblioteca", "library", "ajustes", "settings", "tv y video", "tv & video", "tv and video", "share factory", "sharefactory",
            "sharefactory studio", "share factory studio", "consejos de ps5", "ps5 tips", "ayuda de juego", "game help", "game base",
            "playstation vr2", "ps vr2", "playstation app", "notificaciones", "notifications", "musica", "music",
            // Xbox
            "microsoft store", "tienda", "store", "game pass", "xbox game pass", "configuracion", "capturas", "captures", "mis juegos y aplicaciones",
            "my games & apps", "my games and apps", "biblioteca completa", "full library", "microsoft edge", "edge", "reproductor multimedia",
            "media player", "accesorios de xbox", "xbox accessories", "consejos de xbox", "xbox tips",
            // Video y musica (en cualquier consola)
            "netflix", "youtube", "spotify", "prime video", "amazon prime video", "disney+", "disney plus", "twitch", "apple tv", "apple tv+",
            "hbo max", "max", "movistar plus+", "movistar+", "dazn", "crunchyroll", "plex", "rtve play", "atresplayer", "mitele", "skyshowtime",
            "filmin", "pluto tv", "tidal", "deezer", "apple music" };

        // 1.0.12: la frase de una pantalla de la consola (DetectorMenu.EsPantallaConsola) -> la app en la que se esta (lo que se vea dentro
        // de ella, p. ej. una captura a pantalla completa en la galeria, no es un juego). null = una pantalla del menu, no de una app
        public static string AppDeFrase(string frase)
        {
            string f = frase ?? "";
            if (f.Contains("galeria") || f.Contains("media gallery") || f.Contains("capturas de pantalla y videoclips") || f.Contains("screenshots and video")) return "Galería de contenido multimedia";
            if (f.Contains("remote play")) return "Remote Play";
            if (f == "playstation store" || f == "lista de deseos" || f == "anadir al carrito" || f == "comprar ahora" || f == "cambiar pestanas" ||
                f.StartsWith("colecciones") || f.StartsWith("descuentos") || f == "wishlist" || f == "add to cart" || f == "buy now" || f == "change tabs") return "PlayStation Store";
            if (f == "playstation plus" || f == "juegos mensuales" || f == "catalogo de juegos") return "PlayStation Plus";
            if (f == "biblioteca de juegos" || f == "tu coleccion" || f == "game library" || f == "your collection") return "Biblioteca de juegos";
            if (f == "microsoft store") return "Microsoft Store";
            if (f == "game pass") return "Game Pass";
            if (f.StartsWith("capturas y clips") || f.StartsWith("captures & clips")) return "Capturas";
            if (f == "reproductor multimedia" || f == "media player") return "Reproductor multimedia";
            return null;
        }

        // ¿Es una app de la consola (no un juego)? nombre = el del perfil sin "PS5: " (o con: se quita)
        public static bool EsAppSistema(string nombre)
        {
            if (string.IsNullOrEmpty(nombre)) return false;
            string n = DetectorMenu.Normal(SinTag(nombre)).Trim().TrimEnd('.', ':', '|', '>', '»');
            foreach (string a in AppsSistema)
            {
                if (n == a) return true;
                if (a.IndexOf(' ') > 0 && n.Contains(a)) return true;
            }
            return false;
        }

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
            // 21-11: las tarjetas del juego en marcha en el centro de control (GTA VI, Rocket League: "@Reanudar el juego", "Continúa donde
            // lo dejaste" y "Contint" cortado, "Nuevo videoclip", "Un único jugador"...) se guardaban como juegos y el de verdad nunca
            foreach (string f in FrasesTarjeta) if (n.Contains(f)) return false;
            if (n.StartsWith("contin")) return false;
            // Una frase (un aviso, una descripcion), no un titulo: muchas palabras en minuscula ("Si no terminas una sesion con Salir del
            // juego, puedes perder tu"); los titulos llevan pocas ("The Legend of Zelda: Tears of the Kingdom" = 3)
            string[] pal = t.Split(' ');
            int minus = 0;
            foreach (string w in pal) if (w.Length > 0 && char.IsLower(w[0])) minus++;
            if (pal.Length > 9 || (minus >= 4 && minus * 2 > pal.Length)) return false;
            if (pal.Length == 1 && char.IsLower(t[0])) return false;   // una palabra suelta en minuscula ("progreso."): el final de una frase
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
            // 21-11: sin simbolos delante ("@Reanudar el juego": el icono del boton leido como @, "*are")
            int i0 = 0;
            while (i0 < s.Length && !char.IsLetterOrDigit(s[i0])) i0++;
            s = s.Substring(i0).Trim();
            // un icono leido como "9)" / "O" delante de un rotulo ("9) Ayuda", "O Tienda")
            s = Regex.Replace(s, @"^(?:[IO0-9]{1,2}['’)]?\s+)+(?=\p{L})", "");
            return s.Trim();
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
                string nombre, crudo; bool fuerteP;
                using (Bitmap src = new Bitmap(f))
                using (Bitmap full = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(full)) g.DrawImage(src, 0, 0, src.Width, src.Height);
                    nombre = LeeNombre(full, c, out crudo, out fuerteP);
                }
                if (nombre != null) leidos++;
                sb.Append(Path.GetFileName(f)).Append("\t").Append(c.Corto).Append("\tnombre: ")
                  .Append(nombre != null ? "\"" + nombre + "\"" + (fuerteP ? " FUERTE" : "") : "(ninguno)")
                  .Append("\tOCR: ").Append(crudo ?? "").Append("\r\n");
            }
            // 21-11 (PORTADA): firmas de las imagenes y la distancia a la mas parecida de las demas (distintas: por encima de Misma)
            List<double[]> fs = new List<double[]>();
            foreach (string f in files)
                using (Bitmap src = new Bitmap(f))
                using (Bitmap full = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(full)) g.DrawImage(src, 0, 0, src.Width, src.Height);
                    fs.Add(PortadasConsola.Firma(full));
                }
            for (int i = 0; i < fs.Count; i++)
            {
                double best = 999; int bj = -1;
                for (int j = 0; j < fs.Count; j++) if (j != i) { double d = PortadasConsola.Dist(fs[i], fs[j]); if (d < best) { best = d; bj = j; } }
                sb.Append("portada ").Append(Path.GetFileName(files[i])).Append(": con dibujo ").Append(PortadasConsola.ConDibujo(fs[i]) ? "si" : "no")
                  .Append(", negro liso ").Append(PortadasConsola.NegroLiso(fs[i]) ? "SI" : "no").Append(", norm mas parecida ").Append(MasParecidaNorm(fs, i, files)).Append(", la mas parecida ").Append(bj >= 0 ? Path.GetFileName(files[bj]) : "-").Append(" a ").Append(best.ToString("0.0", CultureInfo.InvariantCulture)).Append("\r\n");
            }
            // 21-11 (quinta vuelta): el icono en foco del inicio, solo en las capturas del inicio; distancias entre todas
            List<double[]> ics = new List<double[]>();
            List<string> icn = new List<string>();
            foreach (string f in files)
                using (Bitmap src = new Bitmap(f))
                using (Bitmap full = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(full)) g.DrawImage(src, 0, 0, src.Width, src.Height);
                    if (!EsInicio(full)) continue;
                    string cr; bool fu;
                    string nm = LeeNombre(full, Consolas.Ps5, out cr, out fu);
                    ics.Add(IconoFoco(full));
                    icn.Add(Path.GetFileName(f) + " (" + (nm ?? "?") + ")");
                }
            for (int i = 0; i < ics.Count; i++)
                for (int j = i + 1; j < ics.Count; j++)
                    sb.Append("icono ").Append(icn[i]).Append(" / ").Append(icn[j]).Append(": ")
                      .Append(DistHuella(ics[i], ics[j]).ToString("0.0", CultureInfo.InvariantCulture)).Append("\r\n");
            foreach (string e in new[] { "BloodborneTY", "Bloor]borno'?", "Blaodborne'Tt•", "p_ra Rocket League", "P_j-m Rocket aguen", "BloodborneTM", "Grand Theft Auto VI" })
                sb.Append("  limpia \"").Append(e).Append("\" -> \"").Append(LimpiaEtiqueta(e)).Append("\"\r\n");
            sb.Append("\r\nNombre leido en ").Append(leidos).Append(" de ").Append(files.Count).Append(" imagenes\r\n");
            // Mini comprobacion de los filtros (sin consola: que no se cuele la hora ni un rotulo)
            sb.Append("\r\nFiltros (EsNombreJuego):\r\n");
            string[] no = { "19:42", "100%", "En linea", "Buscar", "Ajustes", "12/50", "ab", "@Reanudar el juego", "Continúa donde lo dejaste", "Contint",
                             "Nuevo videoclip", "Un único jugador", "Juegos", "Multimedia", "Bienvenida", "Actividad act", "7 historias de tus juegos", "Te damos la bienvenida de nuevo a PlayStation", "Mapa",
                             "9) Ayuda", "O Tienda", "II' Mapa", "progreso.", "Estudio fotográfico", "Si no terminas una sesión con Salir del juego, puedes perder tu" };
            string[] si = { "Bloodborne", "God of War Ragnarok", "EA SPORTS FC 26", "Marvel's Spider-Man 2", "Grand Theft Auto VI", "ROCKET LEAGUE" };
            foreach (string s in no) sb.Append("  descarta \"").Append(s).Append("\": ").Append(EsNombreJuego(s, Consolas.Ps5) ? "FALLO (lo acepto)" : "OK").Append("\r\n");
            foreach (string s in si) sb.Append("  acepta   \"").Append(s).Append("\": ").Append(EsNombreJuego(s, Consolas.Ps5) ? "OK" : "FALLO (lo descarto)").Append("\r\n");
            // 21-11: erratas del OCR = el mismo perfil; cifras o nombres distintos = otro
            string[][] pares = { new[] { "PS5: Bfoodborne", "PS5: Bloodborne", "1" }, new[] { "PS5: Bloodbornc", "PS5: Bfoodborne", "1" },
                                 new[] { "PS5: EA SPORTS FC 26", "PS5: EA SPORTS FC 25", "0" }, new[] { "PS5: Hitman 3", "PS5: Batman 3", "0" },
                                 new[] { "PS5: Grand Theft Auto VI", "PS5: Grand Theft Auto V", "0" }, new[] { "PS5: ROCKET", "PS5: ROCKET LEAGUE", "1" },
                                 new[] { "PS5: Grand Theft Auto VI", "PS5: Grand Thef Auto VI", "1" },
                                 new[] { "PS5: Bfood60rne", "PS5: Bloodborne", "1" }, new[] { "PS5: NBA 2K25", "PS5: NBA 2K26", "0" },
                                 new[] { "PS5: Theft Auto VI", "PS5: Grand Theft Auto VI", "1" }, new[] { "PS5: Theft Auto V", "PS5: Grand Theft Auto VI", "0" },
                                 new[] { "PS5: Rocket aguen", "PS5: Rocket Leaguen", "1" } };
            foreach (string[] q in pares)
            {
                bool same = GameProfiles.SameGame(q[0], q[1]);
                sb.Append("  ").Append(same ? "mismo  " : "otro   ").Append(q[0]).Append(" / ").Append(q[1]).Append(": ").Append(same == (q[2] == "1") ? "OK" : "FALLO").Append("\r\n");
            }
            // (1.2.04) el ® leido como "n" pegada
            foreach (string[] q in new[] { new[] { "P_r-m Rocket Leaguen", "Rocket League" }, new[] { "Batman", "Batman" }, new[] { "Grand Theft Auto VI", "Grand Theft Auto VI" }, new[] { "Bloodborne", "Bloodborne" } })
            {
                string got = LimpiaEtiqueta(q[0]);
                sb.Append("  limpia \"").Append(q[0]).Append("\" -> \"").Append(got).Append("\": ").Append(got == q[1] ? "OK" : "FALLO").Append("\r\n");
            }
            List<Ocr.Linea> ls = new List<Ocr.Linea> {
                new Ocr.Linea { Texto = "Bloodbornc", Alto = 30, X = 100, Y = 100 }, new Ocr.Linea { Texto = "BloodborneTY", Alto = 22, X = 100, Y = 300 },
                new Ocr.Linea { Texto = "Bfoodborne", Alto = 31, X = 500, Y = 400 }, new Ocr.Linea { Texto = "Jugar", Alto = 14, X = 500, Y = 500 } };
            sb.Append("  elige \"").Append(EligeNombre(JuntaLogo(ls), Consolas.Ps5)).Append("\" (Bloodbornc | BloodborneTY | Bfoodborne | Jugar)\r\n");
            ls = new List<Ocr.Linea> {
                new Ocr.Linea { Texto = "'ROCKET", Alto = 40, X = 100, Y = 100 }, new Ocr.Linea { Texto = "LEAGUE.", Alto = 38, X = 104, Y = 150 },
                new Ocr.Linea { Texto = "7 historias de tus juegos", Alto = 20, X = 100, Y = 400 } };
            sb.Append("  elige \"").Append(EligeNombre(JuntaLogo(ls), Consolas.Ps5)).Append("\" ('ROCKET | LEAGUE. | 7 historias de tus juegos)\r\n");
            ls = new List<Ocr.Linea> {
                new Ocr.Linea { Texto = "Mapa", Alto = 18, X = 100, Y = 60 }, new Ocr.Linea { Texto = "Grand Theft Auto VI", Alto = 26, X = 100, Y = 200 },
                new Ocr.Linea { Texto = "@Reanudar el juego", Alto = 28, X = 100, Y = 400 }, new Ocr.Linea { Texto = "Nuevo videoclip", Alto = 30, X = 600, Y = 400 } };
            sb.Append("  elige \"").Append(EligeNombre(JuntaLogo(ls), Consolas.Ps5)).Append("\" (Mapa | Grand Theft Auto VI | @Reanudar el juego | Nuevo videoclip)\r\n");
            // 21-11: al entrar en un juego vale el que estaba en foco aunque se leyera una sola vez (como el HOME de la Switch 2)
            ReconocedorJuego rj = new ReconocedorJuego();
            DateTime t0 = DateTime.Now.AddMinutes(-1);
            bool ult;
            rj.Observa("Bloodborne", null, t0); rj.Observa("Bloodborne", null, t0.AddSeconds(0.8));
            rj.Observa("Vice City, USA", false, null, t0.AddSeconds(5));
            string sale = rj.AlSalir(t0.AddSeconds(5.5), out ult);
            sb.Append("  al salir tras 1 lectura suelta de otra cosa: \"").Append(sale).Append("\" ").Append(sale == "Bloodborne" ? "OK" : "FALLO").Append("\r\n");
            rj.Observa("Grand Theft Auto VI", true, null, t0.AddSeconds(8));
            sale = rj.AlSalir(t0.AddSeconds(8.5), out ult);
            sb.Append("  al salir tras su pagina con Jugar (1 lectura): \"").Append(sale).Append("\" ").Append(sale == "Grand Theft Auto VI" ? "OK" : "FALLO").Append("\r\n");
            rj.Observa(null, null, t0.AddSeconds(30));
            sale = rj.AlSalir(t0.AddSeconds(31), out ult);
            sb.Append("  al salir sin leer nada: \"").Append(sale).Append("\" (el de antes) ").Append(sale == "Grand Theft Auto VI" ? "OK" : "FALLO").Append("\r\n");
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
        public string Observa(string nombre, double[] huella, DateTime now) { return Observa(nombre, false, huella, now); }

        public string Observa(string nombre, bool fuerte, double[] huella, DateTime now)
        {
            votos.RemoveAll(delegate (Voto v) { return (now - v.At).TotalSeconds > VentanaVotosS || v.At > now; });
            if (!string.IsNullOrEmpty(nombre) && fuerte)
            {
                // La pagina del juego con "Jugar": vale a la primera, como el nombre del HOME de la Switch
                votos.Add(new Voto { Nombre = nombre, At = now });
                if (actual == null || !Mismo(nombre, actual)) { Fijar(nombre, huella, now); porPagina = true; }
                confirmado = true;
                return actual;
            }
            if (!string.IsNullOrEmpty(nombre))
            {
                votos.Add(new Voto { Nombre = nombre, At = now });
                // ¿Hay ya VotosParaFijar lecturas del mismo juego en la ventana? (por su clave, tolerando erratas del OCR via SameGame)
                string clave = GameNames.Key(nombre);
                int n = 0;
                string repr = nombre;
                foreach (Voto v in votos)
                    if (GameNames.Key(v.Nombre) == clave || GameProfiles.SameGame(v.Nombre, nombre) || JuegoConsola.Parecido(v.Nombre, nombre))
                    {
                        n++;
                        if (JuegoConsola.Limpia(v.Nombre).Length > JuegoConsola.Limpia(repr).Length) repr = v.Nombre; // el mas largo/completo
                    }
                if (n >= VotosParaFijar)
                {
                    if (actual == null || !Mismo(repr, actual)) { Fijar(repr, huella, now); porPagina = false; }
                    confirmado = true;
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

        // 21-11 (el usuario: "tiene que ser como con la switch 2, que detecta al entrar a un juego por primera vez como es y ya se
        // guarda"): al salir del menu al juego vale el juego EN FOCO en ese momento (la ultima lectura, de hace <= 4 s), como el nombre
        // del HOME de la Switch al pulsar A, aunque solo se leyera una vez: antes hacian falta 2 lecturas iguales (~1,6 s quieto en la
        // tarjeta) y, si se entraba antes, se quedaba el juego anterior (o "Consola") y lo cambiado se guardaba en el perfil de otro.
        // Sin lectura reciente, el de antes (histeresis, como hasta ahora). Los votos se vacian: el menu siguiente empieza de cero
        // 21-11 (segunda vuelta): la primera version de AlSalir tomaba la ULTIMA lectura aunque fuera de una sola vez y metia basura
        // ("Vice City, USA" del anuncio, la pestaña "Juegos", "Bfood60rne"): cada una un perfil nuevo. Ahora al salir vale el juego ya
        // reconocido (por su pagina con "Jugar" a la primera, o por 2 lecturas iguales); sin nada nuevo fiable, el que habia (al volver
        // al mismo juego, lo normal, se queda el suyo). porPagina: se reconocio en su pagina (para el log)
        bool porPagina;
        bool confirmado;            // en esta visita al menu se leyo (fiable) el juego: la pagina con "Jugar" o 2 lecturas iguales
        public bool ConfirmadoEnVisita;  // (lo de la ultima visita, para memorizar su portada al arrancar)
        public string AlSalir(DateTime now, out bool porUltima)
        {
            porUltima = porPagina;
            ConfirmadoEnVisita = confirmado;
            confirmado = false;
            votos.Clear();
            return actual;
        }

        // 21-11 (PORTADA): el juego sabido por su portada al arrancar (sin el menu)
        public void Pon(string nombre) { if (!string.IsNullOrEmpty(nombre)) { actual = nombre; actualAt = DateTime.Now; } }

        static bool Mismo(string a, string b)
        {
            return GameNames.Key(a) == GameNames.Key(b) || GameProfiles.SameGame(a, b) || JuegoConsola.Parecido(a, b);
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

    // ===================================================================
    //  21-11 (PORTADA; el usuario: "en ps5 es facilisimo, al entrar a un juego sale en gigante unos segundos la portada, haz que la
    //  memorice"): al arrancar un juego la PS5 pone su portada a pantalla completa, quieta, unos segundos. Su firma (luma en 16x9
    //  celdas de la imagen entera) se guarda con el perfil del juego (games\console_covers.txt) la primera vez, cuando el juego se
    //  supo en el menu; despues, al ver otra vez esa portada, se pone ese juego y sus ajustes aunque el menu no se leyera (como el icono
    //  del HOME de la Switch 2). Hasta 4 firmas por juego (otras capturas / resoluciones)
    // ===================================================================
    static class PortadasConsola
    {
        public const int W = 16, H = 9;
        public const double Misma = 7.0;      // distancia media por celda (0-255) por debajo: la misma portada
        public const double Quieta = 2.5;     // entre dos capturas seguidas por debajo: imagen quieta

        public static double[] Firma(Bitmap full)
        {
            if (full == null || full.Width < W * 4 || full.Height < H * 4) return null;
            try
            {
                double[] s = new double[W * H];
                int[] cnt = new int[W * H];
                Rectangle z = new Rectangle(0, 0, full.Width, full.Height);
                BitmapData d = full.LockBits(z, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    byte[] row = new byte[z.Width * 4];
                    int step = Math.Max(1, z.Width / 320);
                    for (int y = 0; y < z.Height; y += step)
                    {
                        Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, row.Length);
                        int cy = y * H / z.Height;
                        for (int x = 0; x < z.Width; x += step)
                        {
                            int o = x * 4, i = cy * W + x * W / z.Width;
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

        // ¿Una imagen con dibujo? (no negra ni lisa: la portada tiene detalle)
        public static bool ConDibujo(double[] f)
        {
            if (f == null) return false;
            double m = 0;
            foreach (double v in f) m += v;
            m /= f.Length;
            double var = 0;
            foreach (double v in f) var += (v - m) * (v - m);
            return m >= 22 && Math.Sqrt(var / f.Length) >= 14;
        }

        public static double Dist(double[] a, double[] b) { return JuegoConsola.DistHuella(a, b); }

        // (PORTADA RAPIDA) distancia sin el brillo ni el contraste (media 0, desviacion 50): la misma portada en el menu (a 4K, con el
        // fundido) y despues (a la captura del juego) da mas parecida asi que en crudo
        public static double DistNorm(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 999;
            double[] x = Norm(a), y = Norm(b);
            if (x == null || y == null) return 999;
            double t = 0;
            for (int i = 0; i < x.Length; i++) t += Math.Abs(x[i] - y[i]);
            return t / x.Length;
        }

        static double[] Norm(double[] a)
        {
            double m = 0;
            foreach (double v in a) m += v;
            m /= a.Length;
            double s = 0;
            foreach (double v in a) s += (v - m) * (v - m);
            s = Math.Sqrt(s / a.Length);
            if (s < 3) return null;
            double[] r = new double[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = (a[i] - m) * 50.0 / s;
            return r;
        }

        // (VUELTA) el negro liso de la PS5 al cerrar un juego y arrancar otro (fondo 16-30, sin nada)
        public static bool NegroLiso(double[] f)
        {
            if (f == null) return false;
            double mn = 999, mx = -1, m = 0;
            foreach (double v in f) { if (v < mn) mn = v; if (v > mx) mx = v; m += v; }
            m /= f.Length;
            return mx - mn < 12 && m < 45;
        }

        static readonly AlmacenFirmas almacen = new AlmacenFirmas("console_covers.txt", W * H, "Portadas de los juegos de la consola al arrancar (las guarda la app): perfil|luma de 16x9 celdas");

        // El perfil de la portada mas parecida (por debajo de Misma) o null
        public static string Busca(double[] f, out double dist) { return almacen.Busca(f, Misma, out dist); }
        public static void Guarda(string perfil, double[] f) { almacen.Guarda(perfil, f, Misma); }
        public static void Renombra(string viejo, string nuevo) { almacen.Renombra(viejo, nuevo); }
    }

    // 21-11 (quinta vuelta): el icono del juego en foco del inicio de la PS5 (JuegoConsola.IconoFoco) de cada perfil (games\console_icons.txt)
    static class IconosConsola
    {
        static readonly AlmacenFirmas almacen = new AlmacenFirmas("console_icons.txt", JuegoConsola.IcoN * JuegoConsola.IcoN, "Iconos de los juegos en el inicio de la consola (los guarda la app): perfil|luma normalizada de 12x12 celdas");
        public static string Busca(double[] f, out double dist) { return almacen.Busca(f, JuegoConsola.MismoIcono, out dist); }
        public static void Guarda(string perfil, double[] f) { almacen.Guarda(perfil, f, JuegoConsola.MismoIcono); }
        public static void Renombra(string viejo, string nuevo) { almacen.Renombra(viejo, nuevo); }
        public static bool Tiene(string perfil) { return almacen.Tiene(perfil); }
    }

    // Firmas (portadas, iconos) por perfil en un fichero de games\: perfil|v1,v2,... (hasta 4 por perfil)
    sealed class AlmacenFirmas
    {
        readonly object sync = new object();
        readonly string nombre, cabecera;
        readonly int n;
        List<KeyValuePair<string, double[]>> lista;

        public AlmacenFirmas(string nombre, int n, string cabecera) { this.nombre = nombre; this.n = n; this.cabecera = cabecera; }

        string Fichero { get { return Path.Combine(Path.GetDirectoryName(Paths.FgFile), nombre); } }

        void Carga()
        {
            if (lista != null) return;
            lista = new List<KeyValuePair<string, double[]>>();
            try
            {
                if (!File.Exists(Fichero)) return;
                foreach (string raw in File.ReadAllLines(Fichero, Encoding.UTF8))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith("#")) continue;
                    int i = l.LastIndexOf('|');
                    if (i <= 0) continue;
                    string[] p = l.Substring(i + 1).Split(',');
                    if (p.Length != n) continue;
                    double[] f = new double[n];
                    for (int k = 0; k < f.Length; k++) f[k] = double.Parse(p[k], CultureInfo.InvariantCulture);
                    lista.Add(new KeyValuePair<string, double[]>(l.Substring(0, i), f));
                }
            }
            catch (Exception ex) { Log.Error("Leyendo " + Fichero, ex); }
        }

        void Escribe()
        {
            try
            {
                StringBuilder sb = new StringBuilder("# " + cabecera + "\r\n");
                foreach (KeyValuePair<string, double[]> kv in lista)
                {
                    sb.Append(kv.Key).Append('|');
                    for (int k = 0; k < kv.Value.Length; k++) sb.Append(k > 0 ? "," : "").Append(kv.Value[k].ToString("0.#", CultureInfo.InvariantCulture));
                    sb.Append("\r\n");
                }
                File.WriteAllText(Fichero, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Error("Guardando " + Fichero, ex); }
        }

        public string Busca(double[] f, double umbral, out double dist)
        {
            dist = 999;
            if (f == null || f.Length != n) return null;
            lock (sync)
            {
                Carga();
                string best = null;
                foreach (KeyValuePair<string, double[]> kv in lista)
                {
                    double d = JuegoConsola.DistHuella(kv.Value, f);
                    if (d < dist) { dist = d; best = kv.Key; }
                }
                return dist < umbral ? best : null;
            }
        }

        // La mas nueva manda: las firmas iguales a esta (de otro perfil, mal asociadas antes) se quitan
        public void Guarda(string perfil, double[] f, double umbral)
        {
            if (string.IsNullOrEmpty(perfil) || f == null || f.Length != n) return;
            lock (sync)
            {
                Carga();
                for (int i = lista.Count - 1; i >= 0; i--)
                    if (lista[i].Key != perfil && JuegoConsola.DistHuella(lista[i].Value, f) < umbral)
                    {
                        Log.Write(nombre + ": la firma de \"" + lista[i].Key + "\" pasa a \"" + perfil + "\"");
                        lista.RemoveAt(i);
                    }
                int c = 0;
                for (int i = lista.Count - 1; i >= 0; i--)
                    if (lista[i].Key == perfil && ++c >= 4) lista.RemoveAt(i);
                lista.Add(new KeyValuePair<string, double[]>(perfil, f));
                Escribe();
            }
        }

        public bool Tiene(string perfil)
        {
            lock (sync) { Carga(); foreach (KeyValuePair<string, double[]> kv in lista) if (kv.Key == perfil) return true; return false; }
        }

        public void Renombra(string viejo, string nuevo)
        {
            lock (sync)
            {
                Carga();
                bool cambio = false;
                for (int i = 0; i < lista.Count; i++)
                    if (lista[i].Key == viejo) { lista[i] = new KeyValuePair<string, double[]>(nuevo, lista[i].Value); cambio = true; }
                if (cambio) Escribe();
            }
        }
    }
}
