// DetectorMenu.cs - El menu de la consola, mas fiable y mas rapido (19-11, agente DETECCION). El usuario: "mejora los detectores de las
// consolas y la rapidez de aplicar perfiles ... que detecte mejor cuando estoy en el menu y entro a un juego".
//
// Lo que se vio en el log del 19-11 con la PS5 (src\MenuPs5.cs, el lector de su hora):
//  - Al abrir la app con la PS5 despertando (negro puro, 11:00:37) se salia del menu a las 3 capturas: Magpie y 9 s de telon negro, y a
//    los 23 s otra vez al menu.
//  - En la pantalla de un juego de la PS5 (la de Bloodborne con "Jugado 36 h", sin la hora arriba) se tomaba por un juego: 15:04:38-15:05:46
//    tres idas y vueltas menu <-> juego en 70 s (cada una abre Magpie, ~8 s de GPU, con el motor encima).
//  - Del juego al menu: la primera captura con la hora, otra a los 0,4 s y la decision en el siguiente HomeTick (0,3-0,84 s tras la
//    primera, medido: 9 veces, media 0,66 s); del menu al juego: 3 capturas seguidas sin la hora (cada 0,5 s) y el siguiente HomeTick.
// Ahora (todo en este fichero, puro y con prueba sin consola: --test-deteccion=<carpeta>):
//  - Cada captura se clasifica (Clasifica): Menu (la hora del PC donde la pone la consola), MenuTexto (sin la hora pero con frases que
//    solo salen en las pantallas de la consola: Ajustes, la biblioteca, la tienda, la pantalla de un juego "Jugado N h", el aviso de
//    reposo...), Plana (negro o un color liso: la consola despertando o arrancando un juego), Oscura (la pantalla atenuada sin la hora
//    legible: el fundido), Juego (nada de eso) o SinSenal.
//  - La decision (MaquinaMenu), con histeresis: al menu con UNA captura si la hora es justo la del PC (minuto exacto) y limpia; si no, dos.
//    Al juego con dos capturas Juego seguidas (la segunda a los 0,25 s); Plana y Oscura no cuentan (ni para salir ni para quedarse); el
//    negro tras el menu (arrancando un juego) cuenta para salir pasados 2 s. Si se vuelve al menu antes de 15 s de haber salido (la
//    salida era dudosa), la siguiente salida pide 3 capturas durante 60 s.
//  - Xbox Series / One (SIN PROBAR con la consola: no hay ninguna): el Inicio (dashboard) y la guia (boton Xbox) ponen la hora arriba (la
//    del Inicio arriba a la derecha con iconos a su izquierda; la de la guia en su cabecera). Se lee la franja de arriba entera por OCR y
//    se busca la hora del PC entre lo leido. Mas sus frases (Mis juegos y aplicaciones, Perfil y sistema, Microsoft Store...).
//    20-11 (CONSOLA): esa franja incluye el sitio de la hora de la PS5: con consola=auto y la PS5 puesta, su propia hora (no leida por su
//    lector) daba pistas a la Xbox y se cambio a ella (04:33 y 04:35). Ahora PistaReloj no da la Xbox con una PlayStation puesta y las
//    pistas de cada captura van por PistasMenu (abajo).
//  - Switch 2: la hora del HOME (arriba a la derecha, con el wifi y la bateria a su derecha) cuenta como el HOME cuando sus franjas no
//    casan (la captura en grises del motor, juegos\home_ref.txt de otra resolucion); nada cambia cuando el nombre cian se lee.
// C# 5
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ConsoleEnhancer
{
    enum VistaMenu { Juego, Menu, MenuTexto, Plana, Oscura, SinSenal }

    // Lo que dio una captura
    sealed class LecturaMenu
    {
        public VistaMenu Vista = VistaMenu.Juego;
        public bool Fuerte;        // la hora es la del PC al minuto y limpia: basta una captura
        public string Hora;        // la hora leida (log)
        public string Ocr;         // lo que leyo el OCR donde va la hora
        public string Pantalla;    // el texto de la pantalla entera (si se leyo)
        public string Frase;       // la frase de la consola que se encontro en Pantalla
        public string Porque;      // por que no es el menu (log)
        public bool ParecioHora;   // la zona de la hora parecia texto (para el diagnostico)
        public int Brillo99;       // lo mas claro de la imagen (percentil 99,5 de la luma)
        public double Ms;          // lo que tardo
    }

    static class DetectorMenu
    {
        // ---------------- Clasificar una captura ----------------
        // full = la captura (la ligera de 1280x720 o la entera); c = la consola puesta; enMenu = la app esta en el menu (a 4K nativo);
        // leerPantalla = leer la pantalla entera aunque se vea la hora (el menu de apagado: MenuPs5Captura lo pide cada segundo)
        public static LecturaMenu Clasifica(Bitmap full, DateTime now, ConsolaPerfil c, bool enMenu, bool leerPantalla)
        {
            Stopwatch sw = Stopwatch.StartNew();
            LecturaMenu l = new LecturaMenu();
            int p005, p50, p995;
            Brillos(full, out p005, out p50, out p995);
            l.Brillo99 = p995;
            if (p995 - p005 < 12)
            {
                l.Vista = VistaMenu.Plana;
                l.Porque = "imagen lisa (" + p005 + "-" + p995 + ")";
                l.Ms = sw.Elapsed.TotalMilliseconds;
                return l;
            }
            string hora = null, ocr = null, porque = null;
            bool fuerte = false, parecio = false;
            bool reloj = BuscaReloj(full, now, c, out hora, out ocr, out fuerte, out porque, out parecio);
            l.Ocr = ocr;
            l.ParecioHora = parecio;
            if (reloj)
            {
                l.Vista = VistaMenu.Menu;
                l.Fuerte = fuerte;
                l.Hora = hora;
            }
            // Sin la hora, en el menu: ¿otra pantalla de la consola? (OCR de la pantalla entera, 60-150 ms)
            if (enMenu && (!reloj || leerPantalla))
            {
                l.Pantalla = HomeLabel.ScreenText(full);
                string frase;
                if (!reloj && EsPantallaConsola(l.Pantalla, c, out frase)) { l.Vista = VistaMenu.MenuTexto; l.Frase = frase; }
            }
            if (l.Vista == VistaMenu.Juego)
            {
                // La pantalla atenuada (sin tocar el mando) o un fundido: nada claro en toda la imagen. No es un juego seguro
                if (p995 < 70) { l.Vista = VistaMenu.Oscura; porque = (porque != null ? porque + "; " : "") + "imagen oscura (lo mas claro " + p995 + ")"; }
            }
            l.Porque = porque;
            l.Ms = sw.Elapsed.TotalMilliseconds;
            return l;
        }

        // La hora del PC donde la pone la consola. PS5: su sitio exacto (RelojPs5); Xbox: la franja de arriba entera; las demas, nada
        public static bool BuscaReloj(Bitmap full, DateTime now, ConsolaPerfil c, out string hora, out string ocr, out bool fuerte, out string porque, out bool parecio)
        {
            hora = null; ocr = null; fuerte = false; porque = null; parecio = false;
            if (c.Familia == "playstation")
            {
                if (!RelojPs5.PuedeSer(full, out porque)) return false;
                parecio = true;
                if (ZonaRepetida(full, RelojPs5.Zona(full.Width, full.Height), now)) { porque = "lo de arriba a la derecha no ha cambiado (ya leido: no era la hora)"; return false; }
                ocr = RelojPs5.Leer(full);
                if (!RelojPs5.EsHora(ocr, now, out hora)) { porque = "no es la hora del PC (OCR \"" + Corto(ocr) + "\")"; ZonaFallida(full, RelojPs5.Zona(full.Width, full.Height), now, ocr); return false; }
                fuerte = RelojPs5.EsHoraExacta(ocr, now);
                return true;
            }
            if (c.Familia == "xbox")
            {
                Rectangle z = ZonaFranja(full.Width, full.Height);
                if (!HayTexto(full, z, out porque)) return false;
                parecio = true;
                if (ZonaRepetida(full, z, now)) { porque = "la franja de arriba no ha cambiado (ya leida: sin la hora)"; return false; }
                ocr = LeerZona(full, z, 2);
                bool exacta;
                if (!HoraEnTexto(ocr, now, out hora, out exacta)) { porque = "sin la hora del PC arriba (OCR \"" + Corto(ocr) + "\")"; ZonaFallida(full, z, now, ocr); return false; }
                fuerte = exacta;
                return true;
            }
            porque = "esta consola no tiene lector de su menu";
            return false;
        }

        // La franja de arriba entera (Xbox: la hora del Inicio arriba a la derecha y la de la guia en su cabecera)
        public static Rectangle ZonaFranja(int w, int h) { return Rectangle.FromLTRB((int)(w * 0.02), (int)(h * 0.015), (int)(w * 0.99), (int)(h * 0.125)); }

        // El HOME de la Switch 2 (y de la Switch): la hora arriba a la derecha con el wifi y la bateria a su derecha (17:49 a x 0,765-0,825,
        // y 0,083-0,13 en juegos\diag_home; la bateria a x 0,93-0,96). Para cuando sus franjas no casan. La hora se busca solo en su sitio
        // (x 0,735-0,85: ahi no la ponen ni la PS5, x 0,915-0,955, ni el Inicio de la Xbox, pegada a la derecha) y a su derecha tiene que
        // haber algo (los iconos): el OCR no siempre lee "96%"
        public static Rectangle ZonaSwitch(int w, int h) { return Rectangle.FromLTRB((int)(w * 0.735), (int)(h * 0.045), (int)(w * 0.85), (int)(h * 0.165)); }
        static Rectangle ZonaSwitchIconos(int w, int h) { return Rectangle.FromLTRB((int)(w * 0.85), (int)(h * 0.06), (int)(w * 0.985), (int)(h * 0.15)); }

        public static bool RelojSwitch(Bitmap full, DateTime now, out string hora, out string ocr, out string porque)
        {
            hora = null; ocr = null;
            Rectangle z = ZonaSwitch(full.Width, full.Height);
            if (!HayTexto(full, z, out porque)) return false;
            string p2;
            if (!HayTexto(full, ZonaSwitchIconos(full.Width, full.Height), out p2)) { porque = "nada a la derecha de la hora (" + p2 + ")"; return false; }
            if (ZonaRepetida(full, z, now)) { porque = "lo de arriba a la derecha no ha cambiado"; return false; }
            ocr = LeerZona(full, z, 2);
            bool exacta;
            if (!HoraEnTexto(ocr, now, out hora, out exacta)) { porque = "sin la hora del PC (OCR \"" + Corto(ocr) + "\")"; ZonaFallida(full, z, now, ocr); return false; }
            return true;
        }

        // consola=auto, con otra consola puesta (MenuPs5Pista): de que consola es la hora del PC que se ve arriba. "ps5" (su sitio exacto),
        // "switch" (la del HOME de Nintendo, con sus iconos), "xbox" (en la franja de arriba, en otro sitio) o null.
        // 20-11 (CONSOLA): con una PlayStation puesta, la hora en la franja NO es de la Xbox: la franja incluye el sitio de la de la PS5 y
        // es su propia hora en una pantalla en la que su lector no la ha leido (el avatar junto a la hora en la pantalla de inicio: OCR
        // "3C) 2:42"; algo a su derecha; un fundido). Asi se cambio a la Xbox el 20-11 a las 04:33 y 04:35. Ante la duda, de la puesta: null
        public static string PistaReloj(Bitmap full, DateTime now, ConsolaPerfil actual, out string hora, out bool miro)
        {
            string ocr, porque;
            bool fuerte, parecio = false;
            miro = false;
            if (actual.Familia != "playstation" && BuscaReloj(full, now, Consolas.Ps5, out hora, out ocr, out fuerte, out porque, out parecio)) { miro = true; return "ps5"; }
            miro = parecio;
            if (RelojSwitch(full, now, out hora, out ocr, out porque)) { miro = true; return "switch"; }
            if (actual.Familia == "playstation" || actual.Familia == "xbox") return null;
            if (BuscaReloj(full, now, Consolas.XboxSeries, out hora, out ocr, out fuerte, out porque, out parecio)) { miro = true; return "xbox"; }
            miro |= parecio;
            return null;
        }

        // ---------------- Lo que no hace falta leer otra vez ----------------
        // Jugando, lo de arriba a la derecha suele ser un marcador fijo del juego: si no ha cambiado desde la ultima vez que se leyo sin
        // encontrar la hora (sin leer ni una cifra y hace menos de 6 s), no se vuelve a leer (dos OCR menos por captura)
        static readonly object repSync = new object();
        static Dictionary<string, double[]> repFirma = new Dictionary<string, double[]>();
        static Dictionary<string, DateTime> repAt = new Dictionary<string, DateTime>();

        static bool ZonaRepetida(Bitmap full, Rectangle z, DateTime now)
        {
            string k = z.Width + "x" + z.Height;
            double[] f = Firma(full, z);
            lock (repSync)
            {
                double[] prev;
                DateTime at;
                if (!repFirma.TryGetValue(k, out prev) || !repAt.TryGetValue(k, out at)) return false;
                if ((now - at).TotalSeconds > 6 || now < at) return false;
                return Dif(prev, f) < 2.0;
            }
        }

        static void ZonaFallida(Bitmap full, Rectangle z, DateTime now, string ocr)
        {
            // (solo si no leyo ni una cifra: una hora mal leida se vuelve a mirar en la siguiente captura)
            if (ocr != null && Regex.IsMatch(ocr, @"\d")) return;
            string k = z.Width + "x" + z.Height;
            double[] f = Firma(full, z);
            lock (repSync) { repFirma[k] = f; repAt[k] = now; }
        }

        public static void OlvidaZonas() { lock (repSync) { repFirma.Clear(); repAt.Clear(); } }

        // 24x6 medias de luma de la zona
        static double[] Firma(Bitmap b, Rectangle z)
        {
            z = Rectangle.Intersect(z, new Rectangle(0, 0, b.Width, b.Height));
            const int CW = 24, CH = 6;
            double[] s = new double[CW * CH];
            int[] n = new int[CW * CH];
            if (z.Width <= 0 || z.Height <= 0) return s;
            BitmapData d = b.LockBits(z, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                byte[] row = new byte[z.Width * 4];
                for (int y = 0; y < z.Height; y += 2)
                {
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, row.Length);
                    int cy = y * CH / z.Height;
                    for (int x = 0; x < z.Width; x += 2)
                    {
                        int o = x * 4, cx = x * CW / z.Width, i = cy * CW + cx;
                        s[i] += (row[o] * 29 + row[o + 1] * 150 + row[o + 2] * 77) >> 8;
                        n[i]++;
                    }
                }
            }
            finally { b.UnlockBits(d); }
            for (int i = 0; i < s.Length; i++) if (n[i] > 0) s[i] /= n[i];
            return s;
        }

        static double Dif(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 999;
            double t = 0;
            for (int i = 0; i < a.Length; i++) t += Math.Abs(a[i] - b[i]);
            return t / a.Length;
        }

        // ---------------- Pixeles ----------------
        // Percentiles 0,5 / 50 / 99,5 de la luma de toda la imagen (1 de cada 4x4 pixeles)
        public static void Brillos(Bitmap b, out int p005, out int p50, out int p995)
        {
            int[] hi = new int[256];
            BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                byte[] row = new byte[b.Width * 4];
                for (int y = 2; y < b.Height; y += 4)
                {
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, row.Length);
                    for (int x = 2; x < b.Width; x += 4)
                    {
                        int o = x * 4;
                        hi[(row[o] * 29 + row[o + 1] * 150 + row[o + 2] * 77) >> 8]++;
                    }
                }
            }
            finally { b.UnlockBits(d); }
            p005 = Pct(hi, 0.005); p50 = Pct(hi, 0.5); p995 = Pct(hi, 0.995);
        }

        static int Pct(int[] hi, double p)
        {
            int n = 0;
            foreach (int c in hi) n += c;
            int want = Math.Max(1, (int)Math.Ceiling(n * p)), acc = 0;
            for (int v = 0; v < 256; v++) { acc += hi[v]; if (acc >= want) return v; }
            return 255;
        }

        // ¿Hay algo como texto en la zona? (como RelojPs5.PuedeSer, sin lo de la derecha): fondo no muy claro y algo claro encima
        static bool HayTexto(Bitmap b, Rectangle z, out string porque)
        {
            porque = null;
            z = Rectangle.Intersect(z, new Rectangle(0, 0, b.Width, b.Height));
            if (z.Width < 8 || z.Height < 8) { porque = "sin imagen"; return false; }
            int[] hi = new int[256];
            BitmapData d = b.LockBits(z, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                byte[] row = new byte[z.Width * 4];
                for (int y = 0; y < z.Height; y++)
                {
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, row.Length);
                    for (int x = 0; x < z.Width; x++) { int o = x * 4; hi[(row[o] * 29 + row[o + 1] * 150 + row[o + 2] * 77) >> 8]++; }
                }
            }
            finally { b.UnlockBits(d); }
            int fondo = Pct(hi, 0.5), arriba = Pct(hi, 0.995);
            if (fondo > 110) { porque = "fondo claro arriba (" + fondo + ")"; return false; }
            if (arriba - fondo < 14) { porque = "sin texto arriba (fondo " + fondo + ", lo mas claro " + arriba + ")"; return false; }
            return true;
        }

        // OCR de una zona, agrandada k veces (las cifras de la captura ligera tienen ~20 px)
        static string LeerZona(Bitmap full, Rectangle z, int k)
        {
            z = Rectangle.Intersect(z, new Rectangle(0, 0, full.Width, full.Height));
            if (z.Width < 8 || z.Height < 8) return null;
            if (z.Height * k > 260) k = Math.Max(1, 260 / z.Height);
            using (Bitmap s = new Bitmap(z.Width * k, z.Height * k, PixelFormat.Format32bppRgb))
            {
                using (Graphics g = Graphics.FromImage(s))
                using (ImageAttributes ia = new ImageAttributes())
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(full, new Rectangle(0, 0, s.Width, s.Height), z.X, z.Y, z.Width, z.Height, GraphicsUnit.Pixel, ia);
                }
                return Ocr.Read(s);
            }
        }

        // ¿Esta la hora del PC (+-1 min; 12 o 24 h) entre lo leido? exacta = la del minuto de ahora
        static readonly Regex HoraSuelta = new Regex(@"(?<![\d])(\d{1,2})\s?[:.;]\s?(\d{2})(?![\d])", RegexOptions.CultureInvariant);
        public static bool HoraEnTexto(string ocr, DateTime now, out string hora, out bool exacta)
        {
            hora = null; exacta = false;
            if (string.IsNullOrEmpty(ocr)) return false;
            string s = ocr.Replace('O', '0').Replace('o', '0');
            foreach (Match m in HoraSuelta.Matches(s))
            {
                int hh = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), mm = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (hh > 23 || mm > 59) continue;
                for (int d = -1; d <= 1; d++)
                {
                    DateTime t = now.AddMinutes(d);
                    if (t.Minute != mm) continue;
                    int h12 = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
                    if (hh == t.Hour || hh == h12)
                    {
                        hora = hh + ":" + m.Groups[2].Value;
                        exacta = d == 0;
                        return true;
                    }
                }
            }
            return false;
        }

        static string Corto(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 40 ? s.Substring(0, 40) + "..." : s;
        }

        // ---------------- Las pantallas de la consola sin la hora (OCR de la pantalla entera) ----------------
        // Sin tildes ni mayusculas (Normal). Frases que solo salen en ellas, nunca palabras sueltas que un juego tambien tenga ("Jugar",
        // "Opciones", "Sonido")
        static readonly string[] FrasesPs = {
            // Ajustes
            "pantalla y video", "usuarios y cuentas", "familia y controles parentales", "ahorro de energia", "capturas y emisiones",
            "datos guardados y ajustes de juegos", "juegos/aplicaciones guardados", "almacenamiento de la consola", "software del sistema",
            "ajustes de sonido", "salida de video", "dispositivos de almacenamiento", "actualizacion del software",
            // Biblioteca, tienda, PS Plus
            "biblioteca de juegos", "tu coleccion", "playstation store", "playstation plus", "lista de deseos", "anadir al carrito",
            "anadir a la biblioteca", "juegos mensuales", "catalogo de juegos",
            // La pantalla de un juego (debajo de su icono en el inicio) y el centro de control
            "game base", "lista de amigos", "descargas/copias", "centro de control", "informacion del juego", "progreso del juego",
            "trofeos", "descargando", "instalando", "copiando", "en cola", "listo para jugar", "buscar actualizaciones",
            // Encendido, usuarios, avisos de la consola
            "entrar en modo de reposo", "apagar la ps5", "reiniciar la ps5", "entrara en modo de reposo", "opciones de alimentacion",
            "elige un usuario", "pulsa el boton ps", "quien va a usar", "anadir usuario", "ajustar area de visualizacion",
            // En ingles
            "screen and video", "users and accounts", "power saving", "game library", "your collection", "control center", "game help",
            "enter rest mode", "turn off ps5", "restart ps5", "downloading", "installing", "ready to play", "wishlist", "add to cart",
            "who's using", "choose a user", "press the ps button" };
        static readonly Regex JugadoRx = new Regex(@"\b(jugado|played)\s*\d+\s*(h|min|horas?|hours?)\b|\b(ps4|ps5)\s*[|]|\|\s*(ps4|ps5)\b", RegexOptions.CultureInvariant);

        static readonly string[] FrasesXbox = {
            "mis juegos y aplicaciones", "my games & apps", "my games and apps", "biblioteca completa", "full library",
            "perfil y sistema", "profile & system", "profile and system", "fiesta y chats", "parties & chats", "parties and chats",
            "microsoft store", "game pass", "opciones de tv y pantalla", "tv & display options", "dispositivos y conexiones",
            "devices & connections", "capturar y compartir", "capture & share", "anadir a inicio", "add to home",
            "apagar la consola", "reiniciar la consola", "turn off console", "restart console", "centro de energia", "power center",
            "jugado recientemente", "recently played", "logros", "achievements" };

        public static bool EsPantallaConsola(string ocr, ConsolaPerfil c, out string frase)
        {
            frase = null;
            if (string.IsNullOrEmpty(ocr)) return false;
            string n = Normal(ocr);
            string[] list = c.Familia == "playstation" ? FrasesPs : c.Familia == "xbox" ? FrasesXbox : null;
            if (list == null) return false;
            foreach (string t in list) if (n.Contains(t)) { frase = t; return true; }
            if (c.Familia == "playstation")
            {
                Match m = JugadoRx.Match(n);
                if (m.Success) { frase = m.Value.Trim(); return true; }
            }
            return false;
        }

        // Minusculas, sin tildes, espacios simples
        public static string Normal(string s)
        {
            if (s == null) return "";
            string d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(d.Length);
            foreach (char ch in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(ch == '\r' || ch == '\n' || ch == '\t' ? ' ' : ch);
            }
            return Regex.Replace(sb.ToString(), " +", " ");
        }
    }

    // ---------------- consola=auto: las pistas que da cada captura (20-11, agente CONSOLA) ----------------
    // Pura (sin la app): la usan MenuPs5Captura / MenuPs5Pista (src\MenuPs5.cs) y la prueba sin consola (src\PruebaDeteccion.cs, seccion 4).
    //  - La puesta ve su menu con su lector (l.Vista = Menu): si es de las probadas (la PS5), la refuerza (Consolas.VistoMenuPropio). Si
    //    es la Xbox (SIN PROBAR: su lector busca la hora en toda la franja y tambien casa con la de la PS5), se mira si es la hora de la
    //    PS5 en su sitio exacto: la PS5 detras (el 20-11 05:22-05:28 la app abria con la Xbox por consola.txt y la PS5 delante)
    //  - Sin su menu (ni una pantalla suya sin la hora): de que consola es la hora de arriba (DetectorMenu.PistaReloj)
    // Cada cosa, cada 5 s como mucho (cada una con su reloj)
    sealed class PistasMenu
    {
        public const double CadaS = 5;
        DateTime propioAt = DateTime.MinValue, otrasAt = DateTime.MinValue;

        public void Reiniciar() { propioAt = otrasAt = DateTime.MinValue; }

        // full = la captura; actual = la consola puesta; l = lo que dio su lector (null si no tiene: AnalyzeGrab con la Switch 2 u otra);
        // switchHome = el HOME de la Switch 2 a la vista (tambien tiene la hora arriba: nada). Devuelve lo que se apunto (prueba y log):
        // "propio", "ps5", "xbox", "switch" o null
        public string Observa(Bitmap full, DateTime now, ConsolaPerfil actual, LecturaMenu l, bool switchHome)
        {
            return Observa(full, now, now, actual, l, switchHome);
        }

        // (la prueba: now = su reloj, para el ritmo; horaPc = la hora del PC de la imagen, para leer la de la consola)
        public string Observa(Bitmap full, DateTime now, DateTime horaPc, ConsolaPerfil actual, LecturaMenu l, bool switchHome)
        {
            if (switchHome || Consolas.Fija || full == null) return null;
            if (now < propioAt) propioAt = DateTime.MinValue; // (el reloj ha ido hacia atras)
            if (now < otrasAt) otrasAt = DateTime.MinValue;
            if (l != null && l.Vista == VistaMenu.Menu)
            {
                if ((now - propioAt).TotalSeconds < CadaS) return null;
                propioAt = now;
                if (actual.Probada) { Consolas.VistoMenuPropio(actual, l.Hora); return "propio"; }
                if (actual.Familia == "playstation") return null;
                string h, o, p;
                bool f, par;
                if (DetectorMenu.BuscaReloj(full, horaPc, Consolas.Ps5, out h, out o, out f, out p, out par)) { Consolas.VistoMenuPs5(h); return "ps5"; }
                return null;
            }
            if (l != null && l.Vista == VistaMenu.MenuTexto) return null;
            if ((now - otrasAt).TotalSeconds < CadaS) return null;
            string hora;
            bool miro;
            string quien = DetectorMenu.PistaReloj(full, horaPc, actual, out hora, out miro);
            if (miro) otrasAt = now;
            if (quien == "ps5") Consolas.VistoMenuPs5(hora);
            else if (quien == "xbox") Consolas.VistoMenuXbox(hora);
            // ("switch": el HOME de Nintendo; la pista la dan sus franjas, ObservaConsola, y con la Switch 2 puesta su lector)
            return quien;
        }
    }

    // ---------------- La decision: menu <-> juego, con histeresis ----------------
    // Pura (sin la app): la usan MenuPs5Captura (src\MenuPs5.cs) y la prueba sin consola. Con homeSync en la app
    sealed class MaquinaMenu
    {
        public int EntrarCon = 2;          // capturas con la hora (no exacta) para entrar al menu
        public int SalirCon = 2;           // capturas Juego seguidas para salir
        public int SalirConDudosa = 3;     // las mismas, tras volver al menu enseguida de salir
        public double NegroSalirS = 2.0;   // negro tras el menu (arrancando un juego): se sale pasado esto
        public double NegroTrasMenuS = 30; // ...si el menu se vio hace menos de esto
        public double VueltaDudosaS = 15;  // volver al menu antes de esto tras salir = la salida era dudosa
        public double DudosaDuraS = 60;

        int hits, outs;
        DateTime planaDesde = DateTime.MinValue;
        DateTime ultimoMenu = DateTime.MinValue;
        DateTime salidaAt = DateTime.MinValue;
        DateTime dudosaHasta = DateTime.MinValue;
        public int Decision;               // +1 entrar en el menu, -1 salir al juego, 0 nada (hasta que se haga: Hecho)
        public string Porque;              // de la decision (log)
        public DateTime PrimeraAt = DateTime.MinValue; // la primera captura de la decision (log: cuanto tardo)

        // ¿Pidiendo una captura enseguida para confirmar?
        public bool Confirmando { get { return Decision == 0 && (hits > 0 || outs > 0); } }
        public int Hits { get { return hits; } }
        public int Outs { get { return outs; } }

        public void Reiniciar() { hits = outs = 0; planaDesde = DateTime.MinValue; Decision = 0; Porque = null; PrimeraAt = DateTime.MinValue; }

        // Una captura. enMenu = la app en el menu ahora. Devuelve Decision
        public int Observa(LecturaMenu l, DateTime t, bool enMenu)
        {
            VistaMenu v = l.Vista;
            if (v == VistaMenu.Menu || v == VistaMenu.MenuTexto) ultimoMenu = t;
            if (v != VistaMenu.Plana) planaDesde = DateTime.MinValue;
            else if (planaDesde == DateTime.MinValue) planaDesde = t;
            if (!enMenu)
            {
                outs = 0;
                if (Decision < 0) Decision = 0;
                if (v == VistaMenu.Menu)
                {
                    if (hits == 0) PrimeraAt = t;
                    hits++;
                    if (l.Fuerte || hits >= EntrarCon)
                    {
                        Decision = 1;
                        Porque = l.Fuerte && hits == 1 ? "la hora \"" + l.Hora + "\" arriba, la del PC: una captura basta" :
                                 hits + " capturas con la hora \"" + l.Hora + "\" arriba";
                    }
                }
                else if (v != VistaMenu.SinSenal) { hits = 0; if (Decision > 0) Decision = 0; }
                return Decision;
            }
            hits = 0;
            if (Decision > 0) Decision = 0;
            switch (v)
            {
                case VistaMenu.Menu:
                case VistaMenu.MenuTexto:
                    outs = 0;
                    if (Decision < 0) Decision = 0;
                    break;
                case VistaMenu.Juego:
                    if (outs == 0) PrimeraAt = t;
                    outs++;
                    int need = t < dudosaHasta ? SalirConDudosa : SalirCon;
                    if (outs >= need)
                    {
                        Decision = -1;
                        Porque = outs + " capturas seguidas sin la hora ni una pantalla de la consola" + (need > SalirCon ? " (vuelta reciente al menu: una mas)" : "");
                    }
                    break;
                case VistaMenu.Plana:
                    // (al abrir la app con la consola despertando no se ha visto el menu: el negro no saca de el)
                    if (ultimoMenu != DateTime.MinValue && (t - ultimoMenu).TotalSeconds <= NegroTrasMenuS + NegroSalirS &&
                        (t - planaDesde).TotalSeconds >= NegroSalirS)
                    {
                        if (outs == 0) PrimeraAt = planaDesde;
                        Decision = -1;
                        Porque = "negro " + (t - planaDesde).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s tras el menu (arranca un juego)";
                    }
                    break;
                // Oscura y SinSenal: ni salen ni se quedan
            }
            return Decision;
        }

        // La app ha hecho el cambio (entrado = al menu)
        public void Hecho(DateTime t, bool entrado)
        {
            if (entrado)
            {
                if (salidaAt != DateTime.MinValue && (t - salidaAt).TotalSeconds < VueltaDudosaS) dudosaHasta = t.AddSeconds(DudosaDuraS);
                ultimoMenu = t;
            }
            else salidaAt = t;
            hits = outs = 0;
            planaDesde = DateTime.MinValue;
            Decision = 0;
        }

        // La de antes (hasta el 19-11): 2 capturas con la hora para entrar, 3 sin ella (ni frase) para salir; el negro y lo oscuro, fuera
        public static int Antes(ref int hits, ref int outs, bool reloj, bool frase, bool enMenu)
        {
            if (reloj || (enMenu && frase)) { hits++; outs = 0; }
            else { hits = 0; if (enMenu) outs++; }
            if (!enMenu && hits >= 2) { hits = 0; return 1; }
            if (enMenu && outs >= 3) { outs = 0; return -1; }
            return 0;
        }
    }
}
