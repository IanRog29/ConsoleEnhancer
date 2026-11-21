// MenuPs5.cs - El menu de la PS5 a 4K nativo, como el HOME de la Switch 2 (19-11, el usuario: "he puesto ahora mi ps5 en la capturadora,
// en el menu haz que vaya nativamente sin smooth motion ni nada ... copia lo de switch 2 pero con ps5").
//
// Como se reconoce: la PS5 pone su hora arriba a la derecha (pantalla de inicio, centro de control encima de un juego, sus paneles de
// Descargas, Notificaciones...), en blanco sobre la parte de arriba oscurecida y sin nada a su derecha. Visto el 19-11 con la PS5 de
// verdad (copias de la tele 02:42-02:49: "2:42"... "2:49", a 3515-3665 x 85-150 de 3840x2160). Un juego no saca la hora del PC en ese
// sitio: la que lee el OCR tiene que ser la del PC (+-1 min, 12 o 24 h). Sin la hora pero con el menu a la vista antes, el texto de la
// pantalla (OCR) dice si sigue siendo la consola (Ajustes, la biblioteca, la tienda...).
//  - Menu visto en 2 capturas seguidas: a 4K nativo a 60 fps, sin Magpie, sin generacion y SIN Smooth Motion (Config.EngineSmoothMode da
//    0: si el motor lo llevaba se vuelve a abrir una sola vez, ya con lo del menu)
//  - 3 capturas seguidas sin el menu (un juego, negro de cargar): al juego, con los ajustes de toda la consola ("Consola" en
//    games\profiles.txt, lo cambiado con el mando se recuerda ahi); si el juego lleva Smooth Motion, el motor se abre una vez con el
//  - El menu de apagado de la PS5 (centro de control > apagado: "Entrar en modo de reposo", "Apagar la PS5"...), leido en el menu: si
//    sale "No Signal", reposo o apagado, se cierra la app enseguida (PowerMenuSeen). Si no se vio, "No Signal" como jugando: la espera de
//    la PS5 (EsperaSinSenalMinS, 8 s; al arrancar un juego con HDR o 120 Hz cambia de modo de video unos segundos) y se cierra
// Con consola=auto y otra consola puesta, la hora del menu de la PS5 es una pista a su favor (Consolas.VistoMenuPs5). C# 5
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
    // La hora del menu de la PS5, arriba a la derecha
    static class RelojPs5
    {
        // Donde va (fracciones de la imagen): la hora acaba en x 0,955 (alineada a la derecha) y ocupa y 0,040-0,070
        const double X0 = 0.865, X1 = 0.962, Y0 = 0.022, Y1 = 0.092;
        // A su derecha no hay nada (la Switch tiene ahi la bateria): x 0,965-0,995
        const double D0 = 0.966, D1 = 0.995;

        public static Rectangle Zona(int w, int h)
        {
            return Rectangle.FromLTRB((int)(w * X0), (int)(h * Y0), (int)Math.Ceiling(w * X1), (int)Math.Ceiling(h * Y1));
        }

        // Sin OCR: ¿puede haber ahi una hora clara sobre oscuro? (casi todas las capturas jugando se quedan aqui: 0,1 ms). Relativo al
        // contraste, no a un blanco fijo: sin tocar el mando la PS5 atenua la pantalla (19-11 03:00:23, antes de entrar en reposo por
        // inactividad: la captura del motor, en grises de la Y, fondo 17 y cifras 40; normal, fondo 5 y cifras 205)
        public static bool PuedeSer(Bitmap full, out string porque)
        {
            porque = null;
            int w = full.Width, h = full.Height;
            Rectangle z = Zona(w, h);
            Rectangle d = Rectangle.FromLTRB((int)(w * D0), z.Top, (int)(w * D1), z.Bottom);
            int[] hz = Histo(full, z), hd = Histo(full, d);
            int nz = Total(hz), nd = Total(hd);
            if (nz == 0 || nd == 0) { porque = "sin imagen"; return false; }
            int fondo = Pct(hz, 0.5), arriba = Pct(hz, 0.995), fondoD = Pct(hd, 0.5), arribaD = Pct(hd, 0.995);
            int salto = arriba - fondo;
            if (fondo > 80) { porque = "fondo claro (" + fondo + ")"; return false; }
            if (salto < 14) { porque = "sin texto (fondo " + fondo + ", lo mas claro " + arriba + ")"; return false; }
            // Lo que es texto: por encima de la mitad del salto. Una hora ocupa ~1-25 % de la zona
            int corte = fondo + salto / 2, texto = 0;
            for (int v = corte; v < 256; v++) texto += hz[v];
            double share = (double)texto / nz;
            if (share < 0.006 || share > 0.30) { porque = "texto " + PctTxt(share); return false; }
            // A la derecha, nada: fondo como el de la hora y sin nada que pase de un cuarto del salto
            if (Math.Abs(fondoD - fondo) > 10 || arribaD - fondoD > Math.Max(6, salto / 4))
            { porque = "algo a la derecha de la hora (" + fondoD + "-" + arribaD + ")"; return false; }
            return true;
        }

        static string PctTxt(double v) { return (v * 100).ToString("0.#", CultureInfo.InvariantCulture) + " %"; }

        static int Total(int[] hi) { int n = 0; foreach (int c in hi) n += c; return n; }

        static int Pct(int[] hi, double p)
        {
            int n = Total(hi), want = (int)Math.Ceiling(n * p), acc = 0;
            for (int v = 0; v < 256; v++) { acc += hi[v]; if (acc >= want) return v; }
            return 255;
        }

        static int[] Histo(Bitmap b, Rectangle r)
        {
            r = Rectangle.Intersect(r, new Rectangle(0, 0, b.Width, b.Height));
            int[] c = new int[256];
            if (r.Width <= 0 || r.Height <= 0) return c;
            BitmapData d = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                byte[] row = new byte[r.Width * 4];
                for (int y = 0; y < r.Height; y++)
                {
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, row.Length);
                    for (int x = 0; x < r.Width; x++)
                    {
                        int o = x * 4;
                        c[(row[o] * 29 + row[o + 1] * 150 + row[o + 2] * 77) >> 8]++;
                    }
                }
            }
            finally { b.UnlockBits(d); }
            return c;
        }

        // El OCR de la zona: primero tal cual, agrandada (cifras de ~40 px; en la captura ligera de 1280x720 tienen ~20); si eso no es una
        // hora, binarizada (LeerBinaria). Con la pantalla normal basta la primera; atenuada y con la captura del motor, la segunda
        public static string Leer(Bitmap full)
        {
            Rectangle z = Rectangle.Intersect(Zona(full.Width, full.Height), new Rectangle(0, 0, full.Width, full.Height));
            if (z.Width < 8 || z.Height < 8) return null;
            const int Alto = 110;
            int w = Math.Max(1, (int)Math.Round((double)z.Width * Alto / z.Height));
            string t;
            using (Bitmap s = new Bitmap(w, Alto, PixelFormat.Format32bppRgb))
            {
                using (Graphics g = Graphics.FromImage(s))
                using (ImageAttributes ia = new ImageAttributes())
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(full, new Rectangle(0, 0, w, Alto), z.X, z.Y, z.Width, z.Height, GraphicsUnit.Pixel, ia);
                }
                t = Ocr.Read(s);
            }
            int hh, mm;
            string hora;
            if (Hora(t, out hh, out mm, out hora, false)) return t;
            string t2 = LeerBinaria(full, z);
            if (Hora(t2, out hh, out mm, out hora, false)) return t2;
            // (1.0.12: solo si ninguna de las dos tiene una hora bien escrita, la que la tenga sin sus dos puntos; antes de eso una lectura
            // basura de la primera, "2541", desplazaba a la buena de la segunda: 3 fallos de consola=auto en la prueba)
            if (Hora(t, out hh, out mm, out hora, true)) return t;
            if (Hora(t2, out hh, out mm, out hora, true)) return t2;
            return string.IsNullOrEmpty(t) ? t2 : t;
        }

        static string LeerBinaria(Bitmap full, Rectangle z)
        {
            // 19-11 03:00: la captura ligera del motor (1 de cada 3 pixeles del 4K, en grises) deja la hora con trazos de 1 pixel y, con la
            // pantalla atenuada, fondo 17 y cifras 40 sobre una trama: el OCR no leia nada. Antes del OCR: texto = lo que pasa del 40 % del
            // salto entre el fondo y lo mas claro, engordado 1 pixel si la zona es pequeña, en negro sobre blanco y agrandado
            int zw = z.Width, zh = z.Height;
            int[] l = new int[zw * zh];
            int[] hi = new int[256];
            BitmapData d = full.LockBits(z, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                byte[] row = new byte[zw * 4];
                for (int y = 0; y < zh; y++)
                {
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, row.Length);
                    for (int x = 0; x < zw; x++)
                    {
                        int o = x * 4, v = (row[o] * 29 + row[o + 1] * 150 + row[o + 2] * 77) >> 8;
                        l[y * zw + x] = v;
                        hi[v]++;
                    }
                }
            }
            finally { full.UnlockBits(d); }
            int fondo = Pct(hi, 0.5), arriba = Pct(hi, 0.995);
            if (arriba - fondo < 8) return "";
            int corte = fondo + Math.Max(4, (arriba - fondo) * 2 / 5);
            bool[] m = new bool[zw * zh];
            for (int i = 0; i < m.Length; i++) m[i] = l[i] >= corte;
            if (zh < 60) // (la ligera de 1280x720: ~50 px de alto, cifras de ~20 con trazos de 1-2)
            {
                bool[] g2 = new bool[m.Length];
                for (int y = 0; y < zh; y++)
                    for (int x = 0; x < zw; x++)
                        g2[y * zw + x] = m[y * zw + x] || (x + 1 < zw && m[y * zw + x + 1]) || (y + 1 < zh && m[(y + 1) * zw + x]) ||
                                         (x + 1 < zw && y + 1 < zh && m[(y + 1) * zw + x + 1]);
                m = g2;
            }
            int k = Math.Max(1, (int)Math.Round(120.0 / zh));
            using (Bitmap s0 = new Bitmap(zw, zh, PixelFormat.Format32bppRgb))
            {
                BitmapData sd = s0.LockBits(new Rectangle(0, 0, zw, zh), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    byte[] row = new byte[zw * 4];
                    for (int y = 0; y < zh; y++)
                    {
                        for (int x = 0; x < zw; x++)
                        {
                            byte v = m[y * zw + x] ? (byte)0 : (byte)255;
                            row[x * 4] = row[x * 4 + 1] = row[x * 4 + 2] = v;
                            row[x * 4 + 3] = 255;
                        }
                        Marshal.Copy(row, 0, new IntPtr(sd.Scan0.ToInt64() + (long)y * sd.Stride), row.Length);
                    }
                }
                finally { s0.UnlockBits(sd); }
                using (Bitmap s = new Bitmap(zw * k, zh * k, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(s))
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        ia.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(s0, new Rectangle(0, 0, s.Width, s.Height), 0, 0, zw, zh, GraphicsUnit.Pixel, ia);
                    }
                    return Ocr.Read(s);
                }
            }
        }

        static readonly Regex HoraRx = new Regex(@"^(\d{1,2})[:.](\d{2})(am|pm)?$", RegexOptions.CultureInvariant);
        // (1.0.12) Una hora sin sus dos puntos: "1232" -> "12:32"; "12147" / "9147" -> "12:47" / "9:47" (los dos puntos leidos como 1 o 7).
        // Tambien para la Xbox y la Switch (DetectorMenu.HoraEnTexto)
        public static List<string> HoraVariantes(string s)
        {
            List<string> r = new List<string>();
            string suf = "";
            if (s.EndsWith("am") || s.EndsWith("pm")) { suf = s.Substring(s.Length - 2); s = s.Substring(0, s.Length - 2); }
            foreach (char ch in s) if (!char.IsDigit(ch)) return r;
            if (s.Length == 3 || s.Length == 4) r.Add(s.Substring(0, s.Length - 2) + ":" + s.Substring(s.Length - 2) + suf);
            if (s.Length == 4 || s.Length == 5) r.Add(s.Substring(0, s.Length - 3) + ":" + s.Substring(s.Length - 2) + suf);
            return r;
        }

        // ¿Lo leido es la hora del PC (ahora +-1 min; 24 h o 12 h, con o sin a. m. / p. m.)? hora = lo leido, limpio
        public static bool EsHora(string ocr, DateTime now, out string hora)
        {
            int hh, mm;
            if (!Hora(ocr, out hh, out mm, out hora)) return false;
            for (int d = -1; d <= 1; d++)
            {
                DateTime t = now.AddMinutes(d);
                if (t.Minute != mm) continue;
                int h12 = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
                if (hh == t.Hour || hh == h12 || (hh == 0 && t.Hour == 0)) return true;
            }
            return false;
        }

        // 19-11 (DETECCION): ¿la hora del PC justo de este minuto? (con ella basta una captura para entrar en el menu: DetectorMenu)
        public static bool EsHoraExacta(string ocr, DateTime now)
        {
            int hh, mm;
            string hora;
            if (!Hora(ocr, out hh, out mm, out hora) || mm != now.Minute) return false;
            int h12 = now.Hour % 12 == 0 ? 12 : now.Hour % 12;
            return hh == now.Hour || hh == h12;
        }

        // ¿Lo leido es una hora (cualquiera)? Con lo que el OCR confunde con cifras. 20-11 (CONSOLA): en la pantalla de inicio de la PS5 el
        // avatar (y los iconos de ajustes y buscar) van pegados a la izquierda de la hora, dentro de la zona: el OCR los lee como una letra
        // ("O 14:42", "3C) 2:42") y no casaba (se tomaba por un juego y, con consola=auto, la hora la encontraba el lector de la Xbox). Si
        // lo de delante es corto (un icono, no texto), vale la ultima palabra
        static bool Hora(string ocr, out int hh, out int mm, out string hora) { return Hora(ocr, out hh, out mm, out hora, true); }
        static bool Hora(string ocr, out int hh, out int mm, out string hora, bool sinPuntos)
        {
            if (HoraSola(ocr, out hh, out mm, out hora, sinPuntos)) return true;
            string s = (ocr ?? "").Trim();
            int sp = s.LastIndexOfAny(new char[] { ' ', '\t', '\r', '\n' });
            if (sp <= 0 || s.Substring(0, sp).Trim().Length > 4) return false;
            return HoraSola(s.Substring(sp + 1), out hh, out mm, out hora, sinPuntos);
        }

        static bool HoraSola(string ocr, out int hh, out int mm, out string hora, bool sinPuntos)
        {
            hora = null; hh = mm = -1;
            if (string.IsNullOrEmpty(ocr)) return false;
            string s = ocr.ToLowerInvariant().Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("\t", "");
            s = s.Replace("a.m.", "am").Replace("p.m.", "pm").Replace("a.m", "am").Replace("p.m", "pm");
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool sufijo = (c == 'a' || c == 'p') && i + 1 < s.Length && s[i + 1] == 'm' && i + 2 == s.Length;
                if (sufijo) { sb.Append(s, i, 2); break; }
                // (lo que el OCR confunde con cifras en una hora)
                switch (c)
                {
                    case 'o': c = '0'; break;
                    case 'i': case 'l': case '|': case '!': c = '1'; break;
                    case 'z': c = '2'; break;
                    case 's': c = '5'; break;
                    case 'b': c = '8'; break;
                    case ';': case ',': c = ':'; break;
                }
                sb.Append(c);
            }
            s = sb.ToString();
            Match m = HoraRx.Match(s);
            // 21-11 (1.0.12, el usuario: "desde el menu de consola tambien deja abrir el menu de ajustes"): el OCR se come a veces los dos
            // puntos ("1232" a las 12:32) o los lee como una cifra ("12147" a las 12:47): no era la hora -> se tomaba el menu por un juego.
            // Solo cifras: con los dos puntos puestos antes de los minutos o en vez de la cifra que hay antes de ellos (EsHora / EsHoraExacta
            // lo comparan con la hora del PC, +-1 minuto: un numero cualquiera no casa)
            if (!m.Success && sinPuntos)
                foreach (string v in HoraVariantes(s))
                {
                    Match mv = HoraRx.Match(v);
                    if (mv.Success && int.Parse(mv.Groups[1].Value, CultureInfo.InvariantCulture) <= 23) { m = mv; break; }
                }
            if (!m.Success) return false;
            hh = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            mm = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (hh > 23 || mm > 59) return false;
            hora = m.Groups[1].Value + ":" + m.Groups[2].Value + (m.Groups[3].Success ? " " + m.Groups[3].Value : "");
            return true;
        }

        // Pantallas de la PS5 sin la hora (OCR de la imagen entera, en minusculas): frases que solo salen en ellas
        static readonly string[] Sistema = {
            "pantalla y vídeo", "pantalla y video", "usuarios y cuentas", "familia y controles parentales", "ahorro de energía",
            "biblioteca de juegos", "tu colección", "playstation store", "playstation plus", "game base", "lista de amigos",
            "descargas/copias", "centro de control", "datos guardados y ajustes de juegos", "juegos/aplicaciones guardados",
            "almacenamiento de la consola", "software del sistema", "accesibilidad", "entrar en modo de reposo", "apagar la ps5",
            "reiniciar la ps5", "elige un usuario", "pulsa el botón ps", "pulsa el boton ps", "screen and video", "users and accounts",
            "game library", "your collection", "control center", "enter rest mode", "turn off ps5", "restart ps5" };

        public static bool EsPantallaSistema(string ocr, out string frase)
        {
            frase = null;
            if (string.IsNullOrEmpty(ocr)) return false;
            string n = Regex.Replace(ocr.ToLowerInvariant().Replace('\r', ' ').Replace('\n', ' '), " +", " ");
            foreach (string t in Sistema) if (n.Contains(t)) { frase = t; return true; }
            return false;
        }

        // El menu de apagado de la PS5 (centro de control > apagado), por su texto
        // 21-11 (1.2.1): sin tildes (el OCR las pone o no) y sin el aviso de "Tu PS5 entrara en modo de reposo en 7 minutos debido a los ajustes
        // de ahorro de energia" (sale en el inicio minutos antes: no es apagarla ahora; con el tomado por el menu de apagado, un "No Signal"
        // de cambiar de modo de video al arrancar un juego en los 20 s siguientes cerraba la app y no salia nunca al juego hasta 4 s despues)
        static readonly Regex AvisoReposoRx = new Regex(@"(entrara en modo de reposo|will enter rest mode|enter rest mode) (en|in) \d+", RegexOptions.CultureInvariant);
        public static bool EsMenuApagado(string ocr, ConsolaPerfil c)
        {
            if (string.IsNullOrEmpty(ocr) || c.TextosApagado == null) return false;
            string n = AvisoReposoRx.Replace(DetectorMenu.Normal(ocr), " ");
            foreach (string t in c.TextosApagado) if (n.Contains(DetectorMenu.Normal(t))) return true;
            return false;
        }

        // ---- Prueba sin consola: --test-ps5=<carpeta de imagenes>;<informe> ----
        // Cada imagen como la captura ligera de la app (1280x720, 1 de cada 3 pixeles a 4K) y entera; la hora "del PC" sale del nombre
        // (fNNNN_HHmmss.jpg, el de grabar la tele) o de la fecha del archivo
        public static int Prueba(string arg)
        {
            string[] p = arg.Split(';');
            string dir = p[0], rep = p.Length > 1 ? p[1] : Path.Combine(dir, "prueba_ps5.txt");
            StringBuilder sb = new StringBuilder();
            int menus = 0, total = 0;
            List<string> files = new List<string>();
            foreach (string ext in new[] { "*.jpg", "*.png" }) files.AddRange(Directory.GetFiles(dir, ext));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                DateTime now = File.GetLastWriteTime(f);
                string bn = Path.GetFileNameWithoutExtension(f);
                Match mt = Regex.Match(bn, @"_(\d{2})(\d{2})(\d{2})$");
                if (!mt.Success) mt = Regex.Match(bn, @"^\d{8}-(\d{2})(\d{2})(\d{2})"); // (games\diag_home: 20261116-110025-928_...)
                if (mt.Success)
                    now = now.Date.AddHours(int.Parse(mt.Groups[1].Value)).AddMinutes(int.Parse(mt.Groups[2].Value)).AddSeconds(int.Parse(mt.Groups[3].Value));
                using (Bitmap src = new Bitmap(f))
                using (Bitmap full = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(full)) g.DrawImage(src, 0, 0, src.Width, src.Height);
                    using (Bitmap light = Ligera(full))
                    {
                        string r1 = Una(light, now), r2 = Una(full, now), r3;
                        using (Bitmap gris = Gris(light)) r3 = Una(gris, now); // (la del motor: la Y, en grises)
                        bool menu = r1.StartsWith("MENU") && r3.StartsWith("MENU");
                        if (menu) menus++;
                        total++;
                        sb.Append(Path.GetFileName(f)).Append('\t').Append(now.ToString("HH:mm:ss")).Append("\tligera ").Append(r1).Append("\tgris ").Append(r3).Append("\tentera ").Append(r2).Append("\r\n");
                    }
                }
            }
            sb.Insert(0, "Menu de la PS5 (la hora arriba a la derecha): " + menus + " de " + total + " imagenes\r\n");
            File.WriteAllText(rep, sb.ToString(), new UTF8Encoding(false));
            return 0;
        }

        static string Una(Bitmap b, DateTime now)
        {
            string porque;
            if (!PuedeSer(b, out porque)) return "no (" + porque + ")";
            string txt = Leer(b), hora;
            bool ok = EsHora(txt, now, out hora);
            // (sin la hora de ahora: ¿seria una hora a otra? asi se ve el riesgo con imagenes de otro dia)
            bool otra = false;
            if (!ok) for (int m = 0; m < 24 * 60 && !otra; m++) { string h2; otra = EsHora(txt, now.Date.AddMinutes(m), out h2); }
            return (ok ? "MENU " : otra ? "no, pero es una hora " : "no ") + "(OCR \"" + (txt ?? "").Replace("\r", " ").Replace("\n", " ") + "\")";
        }

        // Como la del motor con la captura en yuy2: la Y en grises, en rango limitado (16-235)
        static Bitmap Gris(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            BitmapData s = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                byte[] row = new byte[w * 4];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(new IntPtr(s.Scan0.ToInt64() + (long)y * s.Stride), row, 0, row.Length);
                    for (int x = 0; x < w; x++)
                    {
                        int o = x * 4, v = 16 + ((row[o] * 29 + row[o + 1] * 150 + row[o + 2] * 77) >> 8) * 219 / 255;
                        row[o] = row[o + 1] = row[o + 2] = (byte)v;
                    }
                    Marshal.Copy(row, 0, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row.Length);
                }
            }
            finally { src.UnlockBits(s); b.UnlockBits(d); }
            return b;
        }

        // Como la captura ligera del script de mpv: 1 de cada N pixeles hasta ~1280 de ancho
        static Bitmap Ligera(Bitmap full)
        {
            int k = Math.Max(1, full.Width / 1280);
            int w = full.Width / k, h = full.Height / k;
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(full, new Rectangle(0, 0, w, h), 0, 0, full.Width, full.Height, GraphicsUnit.Pixel);
            }
            return b;
        }
    }

    partial class TrayApp
    {
        int ps5MenuHits;                         // capturas seguidas con el menu de la consola (jugando; GrabTick: confirmar enseguida)
        int ps5OutHits;                          // en el menu: capturas seguidas sin el (juego)
        DateTime ps5TextoAt = DateTime.MinValue; // la ultima lectura de la pantalla entera en el menu (apagado, pantallas sin hora)
        readonly PistasMenu pistasMenu = new PistasMenu(); // consola=auto: las pistas de cada captura (20-11, CONSOLA; antes ps5PistaAt)
        DateTime ps5FalloLog = DateTime.MinValue;
        string ps5Hora;                          // la ultima hora leida (log)
        // 19-11 (DETECCION, src\DetectorMenu.cs): la decision menu <-> juego, con histeresis (con homeSync)
        readonly MaquinaMenu menuMaq = new MaquinaMenu();
        VistaMenu ps5UltimaVista = VistaMenu.Juego;
        DateTime ps5DecididoAt = DateTime.MinValue; // cuando se decidio el cambio (log: cuanto tarda en aplicarse)

        string MenuTexto { get { return Consolas.Actual.TieneHome ? "HOME" : "Menú"; } }

        // 20-11 (PS5JUEGO): reconocer CADA JUEGO de la consola (PS5, Xbox) por su nombre en el overlay y recordarle su perfil (src\JuegoConsola.cs)
        readonly ReconocedorJuego recoJuego = new ReconocedorJuego();
        DateTime ps5NombreAt = DateTime.MinValue; // ultima lectura del nombre del juego (cada ~0,8 s en el menu)
        string ps5JuegoLog;                       // ultimo juego reconocido que se escribio en el log
        string ps5LecturaLog;                     // ultima lectura del menu escrita en el log
        // 21-11 (PORTADA, src\JuegoConsola.cs PortadasConsola): tras salir del menu, la portada quieta del juego al arrancar
        DateTime ps5PortadaHasta = DateTime.MinValue;   // hasta cuando se busca (15 s tras salir del menu)
        double[] ps5PortadaPrev;                        // la captura anterior (para ver que esta quieta)
        DateTime ps5PortadaQuietaDesde = DateTime.MinValue;
        bool ps5PortadaFresco;                          // el juego se supo en esa visita al menu (se puede memorizar su portada)
        string ps5PortadaJuego;                         // perfil reconocido por su portada, a poner en MenuPs5Tick
        // 21-11 (quinta vuelta, ICONO): el icono del juego en foco en el inicio de la PS5 (JuegoConsola.IconoFoco), en esta visita al menu
        double[] ps5Foco;                               // el ultimo icono en foco visto
        DateTime ps5FocoAt = DateTime.MinValue;
        bool ps5FocoQuieto;                             // visto igual en dos lecturas seguidas (no a mitad de moverse)
        double[] ps5FocoPrev;
        // (1.2.1: cada lectura de la etiqueta con su icono, su hora y si el foco estaba quieto: JuegoConsola.FocoAlSalir elige el juego que
        // se arranca, su nombre entero y el icono de una captura con la etiqueta entera; antes el nombre mas leido y, a igualdad, el ultimo)
        readonly List<JuegoConsola.LecturaFoco> ps5Lecturas = new List<JuegoConsola.LecturaFoco>();
        bool ps5SalidaPorIcono;                         // la ultima salida al juego se decidio por el icono (manda sobre la portada)
        string ps5PrudenteDicho;                        // 1.2.1: la salida prudente ya dicha en el log (su porque)
        // 21-11 (VUELTA; el usuario: "arregla lo del caso que todavia puede fallar"): con un juego en marcha, en el inicio se mueve el foco
        // a OTRO juego y se vuelve al de antes con el boton PS (sin arrancar el del foco). El icono diria el otro. Entonces se sigue con
        // el que se jugaba y el del icono queda de CANDIDATO: solo si en los segundos siguientes se ve el arranque de un juego (la
        // pantalla negra lisa de cuando la PS5 cierra uno y abre otro, o la portada del candidato) se pasa a el. Volver al juego en
        // marcha es inmediato, sin negro: se queda el de antes
        string ps5Candidato;                            // perfil del icono, pendiente de ver el arranque
        string ps5JuegoAntesMenu;                       // el juego en marcha al abrir el menu
        // 21-11 (PORTADA RAPIDA; el usuario: "sigue fallando a veces"): la portada sale AL INSTANTE al pulsar Jugar (aun en el menu: captura
        // 03:12:13 con la de Rocket League, 3,4 s antes de que se reconociera). Una portada YA CONOCIDA se busca en cada captura (en el menu
        // y hasta 15 s despues) y con 2 capturas seguidas a menos de 7 (la misma: 0-5,1; otras: 14 o mas) decide el juego al momento, antes de
        // que se toque nada: manda sobre el icono y el nombre
        string ps5RapidaPrev;                           // portada de la captura anterior (para pedir 2 seguidas)
        string ps5PortadaVista;                         // portada conocida confirmada (en esta visita al menu o tras salir)
        DateTime ps5PortadaVistaAt = DateTime.MinValue;
        DateTime ps5SalidaAt = DateTime.MinValue;       // cuando se salio del menu al juego
        public const double PortadaSegura = 7.0;   // (la misma portada en el menu, a 4K y con el fundido, y despues: 0-5,1; otras pantallas: 14 o mas)

        void PortadaRapida(Bitmap full, DateTime now, ConsolaPerfil c, bool inMenu)
        {
            if (!RecoJuegosActivo(c)) return;
            if (!inMenu && (now - ps5SalidaAt).TotalSeconds > 15) { ps5RapidaPrev = null; return; }
            double[] f = PortadasConsola.Firma(full);
            if (!PortadasConsola.ConDibujo(f)) { ps5RapidaPrev = null; return; }
            double d;
            string p = PortadasConsola.Busca(f, out d);
            if (p == null || d >= PortadaSegura) { ps5RapidaPrev = null; return; }
            if (ps5RapidaPrev != p) { ps5RapidaPrev = p; return; }
            if (ps5PortadaVista != p) Log.Write("Portada de \"" + p + "\" en pantalla (distancia " + d.ToString("0.0", CultureInfo.InvariantCulture) + (inMenu ? ", aun en el menu: se arranca ese juego" : "") + ")");
            ps5PortadaVista = p;
            ps5PortadaVistaAt = now;
            if (inMenu) return;   // (al salir del menu, MenuPs5Salir la usa)
            ps5PortadaHasta = DateTime.MinValue;   // (ya se sabe: la lenta no hace falta)
            string cur;
            lock (profileSync) cur = curGame;
            if (cur == p || (cur != null && GameProfiles.SameGame(cur, p))) { ps5Candidato = null; return; }
            Log.Write("Portada del juego: \"" + p + "\" al momento: se pone ese juego y sus ajustes (antes \"" + (cur ?? "ninguno") + "\")");
            ps5Candidato = null;
            ps5SalidaPorIcono = true;
            lock (homeSync) { recoJuego.Pon(JuegoConsola.SinTag(p)); ps5PortadaJuego = p; }
        }
        double[] ps5FirmaJuego;                         // la imagen del juego (16x9) de la ultima captura fuera del menu
        double[] ps5FirmaAntesMenu;                     // la de justo antes de abrir el menu (volver al juego = parecida)
        public const double MismaEscena = 18.0;         // por debajo: la misma escena que antes del menu
        DateTime ps5CandidatoHasta = DateTime.MinValue;

        DateTime ps5NegroDesde = DateTime.MinValue;    // negro liso seguido desde (con candidato)

        // 1.2.05 (el usuario: "me meto al gta despues del rocket y pone que me he metido al rocket al darle a los ajustes"): 17:38:07 se
        // cambio a 1440p/120 y el cambio de motor (sin telon, ~6 s con lsReady=false) dejo la app SIN capturas: justo entonces se abrio el
        // menu de la PS5 y se arranco GTA VI, y la app siguio con Rocket League. Ahora, con el relevo en curso, se sigue capturando SOLO para
        // el menu (AnalyzeGrab -> MenuPs5Captura) y una visita al menu que no se pudo atender (VISITA CIEGA) se resuelve al volver la imagen
        // del juego como la salida del menu: el icono en foco, de candidato hasta ver el arranque (negro, sin señal o su portada)
        DateTime ps5CiegoDesde = DateTime.MinValue;    // menu visto sin poder entrar en el (cambio de motor / captura en curso)
        DateTime ps5CiegoUltimo = DateTime.MinValue;   // la ultima captura del menu en esa visita
        double[] ps5FirmaAntesCiego;                   // la imagen del juego de antes de esa visita
        // 1.2.05: al abrir el menu desde un juego, la PS5 pone en foco el juego EN MARCHA (17:37:40 y 17:38:31: GTA VI, el primero leido).
        // Si no es el que cree la app (se arranco otro sin que lo viera), ese manda como "el juego de antes del menu"
        string ps5PrimerFoco;                          // el primer nombre leido (etiqueta) en esta visita al menu
        double[] ps5PrimerFocoIcono;
        DateTime ps5PrimerMenuAt = DateTime.MinValue;  // la primera captura del menu en esta visita (jugando)

        // (lo que deja a MenuPs5Tick sin entrar en el menu un buen rato; homeBusy no: es la propia entrada/salida del menu)
        bool Ps5AppOcupada { get { return relevo != null || passesBusy || relaunchSize != null || relaunchEngine; } }

        // El perfil del primer juego en foco de esta visita (icono conocido o, si no, su nombre); null si no se leyo enseguida
        string PerfilPrimerFoco(ConsolaPerfil c)
        {
            if (ps5PrimerFoco == null) return null;
            double d;
            string p = ps5PrimerFocoIcono != null ? IconosConsola.Busca(ps5PrimerFocoIcono, out d) : null;
            if (p != null) return p;
            string nom = JuegoConsola.NombrePerfil(c, ps5PrimerFoco);
            if (NombreProvisional(nom)) return null;
            GameProfile gp = GameProfiles.Find(nom);
            return gp != null ? gp.Name : nom;
        }

        // 1.2.1: lo que se sabe del foco al salir del menu, copiado con homeSync (la decision, que lee y escribe ficheros, va fuera de el)
        sealed class FotoFoco
        {
            public double[] Ultimo;                      // el ultimo icono en foco visto
            public DateTime At = DateTime.MinValue;
            public bool Quieto;
            public List<JuegoConsola.LecturaFoco> Lecturas;
        }

        // (con homeSync) la foto del foco y se olvida para la siguiente visita
        FotoFoco TomaFoco()
        {
            FotoFoco f = new FotoFoco();
            f.Ultimo = ps5Foco; f.At = ps5FocoAt; f.Quieto = ps5FocoQuieto;
            f.Lecturas = new List<JuegoConsola.LecturaFoco>(ps5Lecturas);
            ps5Foco = ps5FocoPrev = null; ps5FocoQuieto = false; ps5Lecturas.Clear();
            return f;
        }

        // VISITA CIEGA terminada (ya no se ve el menu y la app esta libre): como salir del menu con el icono en foco
        void SalidaCiega(DateTime now, ConsolaPerfil c)
        {
            double s = (ps5CiegoUltimo - ps5CiegoDesde).TotalSeconds;
            ps5CiegoDesde = DateTime.MinValue;
            if (!RecoJuegosActivo(c)) return;
            string info, pi, cur;
            FotoFoco foto;
            lock (homeSync)
            {
                foto = TomaFoco();
                ps5PrimerFoco = null; ps5PrimerFocoIcono = null; ps5PrimerMenuAt = DateTime.MinValue;
            }
            pi = PerfilPorIcono(c, now, DateTime.MinValue, foto, out info, 30);
            lock (profileSync) cur = curGame;
            string vis = "Menu de la consola visto " + s.ToString("0.0", CultureInfo.InvariantCulture) + " s mientras la app cambiaba de motor o de captura";
            if (pi == null) { Log.Write(vis + ": sin icono en foco fiable: sigue \"" + (cur ?? "ninguno") + "\""); return; }
            if (cur != null && (cur == pi || GameProfiles.SameGame(cur, pi))) { Log.Write(vis + ": en foco \"" + pi + "\", el mismo: sigue"); return; }
            if (JuegoConsola.EsAppSistema(pi)) { Log.Write(vis + ": en foco \"" + pi + "\" (app de la consola): sigue \"" + (cur ?? "ninguno") + "\""); return; }
            ps5JuegoAntesMenu = cur;
            ps5FirmaAntesMenu = ps5FirmaAntesCiego;
            ps5Candidato = pi;
            ps5CandidatoHasta = now.AddSeconds(25);   // (GTA VI tarda en enseñar su pantalla de carga)
            ps5NegroDesde = DateTime.MinValue;
            ps5SalidaAt = now;
            ps5SalidaPorIcono = false;
            lock (homeSync) { ps5PortadaHasta = ps5CandidatoHasta; ps5PortadaPrev = null; ps5PortadaFresco = false; }
            Log.Write(vis + ": en foco \"" + pi + "\" (" + info + "), se jugaba \"" + (cur ?? "ninguno") +
                      "\": se pasa a el si se ve arrancar (negro, sin señal o su portada) en 25 s");
        }

        void PasaACandidato(string cand, string porque)
        {
            ps5Candidato = null;
            Log.Write("Arranque de otro juego visto (" + porque + "): al juego del icono \"" + cand + "\"");
            lock (homeSync) { recoJuego.Pon(JuegoConsola.SinTag(cand)); ps5PortadaJuego = cand; }
            ps5SalidaPorIcono = true;
        }

        void CandidatoNegro(DateTime now, string que)
        {
            string cand = ps5Candidato;
            if (cand == null || now > ps5CandidatoHasta) return;
            if (ps5NegroDesde == DateTime.MinValue) { ps5NegroDesde = now; return; }
            double s = (now - ps5NegroDesde).TotalSeconds;
            if (s >= 1.2) PasaACandidato(cand, que + " " + s.ToString("0.0", CultureInfo.InvariantCulture) + " s seguidos");
        }

        void CandidatoObserva(Bitmap full, DateTime now)
        {
            string cand = ps5Candidato;
            if (cand == null) return;
            if (now > ps5CandidatoHasta)
            {
                ps5Candidato = null;
                string cur; lock (profileSync) cur = curGame;
                Log.Write("Vuelta al juego en marcha: sin pantalla de arranque en 8 s, sigue \"" + (cur ?? "?") + "\" (el icono en foco era \"" + cand + "\", no se arranco)");
                return;
            }
            double[] f = PortadasConsola.Firma(full);
            if (f == null) return;
            // Negro liso SEGUIDO 1,2 s o mas (el de la PS5 al cerrar un juego y abrir otro, fondo 16-30, dura varios segundos; el de
            // reabrir la capturadora al salir del menu es mas corto)
            // La misma imagen que antes de abrir el menu: se ha vuelto al juego en marcha (no se arranco el del foco)
            double dPre = ps5FirmaAntesMenu != null ? PortadasConsola.Dist(f, ps5FirmaAntesMenu) : 999;
            if (dPre < MismaEscena)
            {
                ps5Candidato = null;
                string cur; lock (profileSync) cur = curGame;
                Log.Write("Vuelta al juego en marcha: la misma imagen que antes del menu (distancia " + dPre.ToString("0.0", CultureInfo.InvariantCulture) +
                          "), sigue \"" + (cur ?? "?") + "\" (el icono en foco era \"" + cand + "\", no se arranco)");
                return;
            }
            if (PortadasConsola.NegroLiso(f)) CandidatoNegro(now, "pantalla negra lisa");
            else ps5NegroDesde = DateTime.MinValue;
        }

        // (con homeSync) ic = el icono en foco de esta captura (JuegoConsola.IconoFoco, calculado antes, fuera del lock)
        void FocoObserva(double[] ic, DateTime now, string nombreFuerte)
        {
            if (ic == null) return;
            bool quieto = ps5FocoPrev != null && JuegoConsola.DistHuella(ps5FocoPrev, ic) < JuegoConsola.IconoQuieto;
            ps5FocoPrev = ic;
            if (ps5Foco == null || JuegoConsola.DistHuella(ps5Foco, ic) >= JuegoConsola.MismoIcono) ps5FocoQuieto = false; // otro juego en foco
            ps5Foco = ic;
            ps5FocoAt = now;
            if (quieto || !string.IsNullOrEmpty(nombreFuerte)) ps5FocoQuieto = true;   // (con la etiqueta legible el foco no se esta moviendo)
            if (!string.IsNullOrEmpty(nombreFuerte))
            {
                JuegoConsola.LecturaFoco lf = new JuegoConsola.LecturaFoco();
                lf.Nombre = nombreFuerte; lf.Icono = ic; lf.At = now; lf.Quieto = quieto;
                ps5Lecturas.Add(lf);
                if (ps5Lecturas.Count > 300) ps5Lecturas.RemoveRange(0, ps5Lecturas.Count - 300);   // (horas en el menu: las ultimas)
            }
        }

        static bool NombreProvisional(string perfil) { return JuegoConsola.Provisional(perfil); }

        // 1.2.1: ¿ese nombre es el de un perfil de esta consola que ya existe? (MejorNombre: desempate)
        static Func<string, bool> PerfilExiste(ConsolaPerfil c)
        {
            return delegate (string n) { return GameProfiles.ExisteNombre(JuegoConsola.NombrePerfil(c, n)); };
        }

        // Al salir del menu al juego: el perfil del icono en foco (el que se arranca). Conocido: ese, aunque el nombre no se leyera. Nuevo
        // (y quieto): con el nombre leido con el (o un perfil que ya hubiera con ese nombre); sin nombre, "PS5: Juego N" (se renombra
        // cuando se lea). null: sin icono fiable (se decide como antes: nombre, histeresis, portada)
        // 1.2.1: el juego, su nombre y su icono los elige JuegoConsola.FocoAlSalir (no gana una lectura a medias ni el icono del fundido de
        // arrancarlo); con el nombre leido entero, un perfil con el nombre a medias o provisional se renombra (MejorNombrePerfil). La edad del
        // foco se mide desde la primera captura sin el menu (primeraFuera), no desde la decision: salir tarda 1,2-2,9 s mas. Sin homeSync
        string PerfilPorIcono(ConsolaPerfil c, DateTime now, DateTime primeraFuera, FotoFoco f, out string info, double maxS)
        {
            info = null;
            if (f == null || f.Ultimo == null) return null;
            DateTime refT = primeraFuera != DateTime.MinValue && primeraFuera >= f.At && primeraFuera <= now ? primeraFuera : now;
            if ((refT - f.At).TotalSeconds > maxS) return null;
            int apoyo, enFoco;
            JuegoConsola.LecturaFoco foco = JuegoConsola.FocoAlSalir(f.Lecturas, PerfilExiste(c), out apoyo, out enFoco);
            string nombre = foco != null ? foco.Nombre : null;
            double[] icono = foco != null && foco.Icono != null ? foco.Icono : f.Ultimo;
            double d;
            string p = IconosConsola.Busca(icono, out d);
            if (p == null && icono != f.Ultimo)
            {
                double d2;
                string p2 = IconosConsola.Busca(f.Ultimo, out d2);
                if (p2 != null) { p = p2; d = d2; }
            }
            string dd = d < 900 ? d.ToString("0.0", CultureInfo.InvariantCulture) : "-";
            string leido = nombre != null ? "; leido \"" + nombre + "\" (" + apoyo + " de " + enFoco + " lecturas del foco)" : "";
            if (p != null)
            {
                info = "por su icono, distancia " + dd + leido;
                string nuevo = MejorNombrePerfil(c, p, nombre, apoyo, f.Lecturas);
                if (nuevo != null) { info += "; antes se llamaba \"" + p + "\""; p = nuevo; }
                return p;
            }
            if (!f.Quieto) { info = "icono nuevo pero sin ver quieto (la mas parecida a " + dd + ")"; return null; }
            string perfil;
            if (nombre != null)
            {
                string np = JuegoConsola.NombrePerfil(c, nombre);
                GameProfile gp = GameProfiles.Find(np);
                perfil = gp != null ? gp.Name : np;
                // (1.2.1: el perfil de ese juego con el nombre a medias, "PS5: Rocket" con "Rocket League" leido: se renombra)
                string nuevo = gp != null ? MejorNombrePerfil(c, gp.Name, nombre, apoyo, f.Lecturas) : null;
                if (nuevo != null) { leido += "; antes se llamaba \"" + perfil + "\""; perfil = nuevo; }
            }
            else
            {
                // "PS5: Juego N" libre (ni perfil ni icono con ese nombre)
                perfil = null;
                for (int i = 1; perfil == null; i++)
                {
                    string q = JuegoConsola.NombrePerfil(c, "Juego " + i);
                    if (!IconosConsola.Tiene(q) && !GameProfiles.ExisteNombre(q)) perfil = q;
                }
            }
            IconosConsola.Guarda(perfil, icono);
            info = "icono nuevo memorizado (la mas parecida a " + dd + ")" + (nombre == null ? ", sin nombre leido aun" : leido);
            return perfil;
        }

        // 1.2.1: si el nombre leido es mejor que el del perfil (JuegoConsola.MejorNombreQue: provisional, a medias o con erratas), el perfil
        // pasa a llamarse asi, con sus iconos y portadas, el juego en marcha y last_game.txt. El nombre nuevo, o null si no se cambia
        string MejorNombrePerfil(ConsolaPerfil c, string viejo, string nombre, int apoyo, List<JuegoConsola.LecturaFoco> lecturas)
        {
            if (string.IsNullOrEmpty(nombre) || string.IsNullOrEmpty(viejo) || JuegoConsola.TagConsola(viejo) == null) return null;
            string kv = GameNames.Key(JuegoConsola.SinTag(viejo));
            bool viejoLeido = false;
            if (lecturas != null) foreach (JuegoConsola.LecturaFoco x in lecturas) if (GameNames.Key(x.Nombre) == kv) { viejoLeido = true; break; }
            if (!JuegoConsola.MejorNombreQue(viejo, nombre, apoyo, viejoLeido)) return null;
            string nuevo = JuegoConsola.NombrePerfil(c, nombre);
            string porque = JuegoConsola.Provisional(viejo) ? "era provisional" : JuegoConsola.Parcial(JuegoConsola.SinTag(viejo), nombre) ?
                            "era una lectura a medias" : "era una lectura con erratas (" + apoyo + " lecturas del nombre nuevo)";
            if (!GameProfiles.Renombra(viejo, nuevo))
            {
                Log.Write("Perfil \"" + viejo + "\": leido \"" + nombre + "\" (" + porque + "), pero ya hay un perfil \"" + nuevo + "\" o no se ha podido cambiar: se deja");
                return null;
            }
            IconosConsola.Renombra(viejo, nuevo);
            PortadasConsola.Renombra(viejo, nuevo);
            lock (profileSync) { if (curGame == viejo) curGame = nuevo; }
            try { if (LoadText(Paths.LastGameFile) == viejo) SaveText(Paths.LastGameFile, nuevo); } catch { }
            if (ps5JuegoAntesMenu == viejo) ps5JuegoAntesMenu = nuevo;
            Log.Write("Perfil \"" + viejo + "\" -> \"" + nuevo + "\" (el nombre de la etiqueta leido entero; el de antes " + porque + ")");
            return nuevo;
        }

        DateTime ps5PortadaNoLog = DateTime.MinValue;

        void PortadaObserva(Bitmap full, DateTime now, ConsolaPerfil c)
        {
            if (now > ps5PortadaHasta || !RecoJuegosActivo(c)) return;
            double[] f = PortadasConsola.Firma(full);
            if (!PortadasConsola.ConDibujo(f)) { ps5PortadaPrev = null; ps5PortadaQuietaDesde = DateTime.MinValue; return; }
            if (ps5PortadaPrev == null || PortadasConsola.Dist(ps5PortadaPrev, f) > PortadasConsola.Quieta)
            {
                ps5PortadaPrev = f;
                ps5PortadaQuietaDesde = now;
                return;
            }
            if ((now - ps5PortadaQuietaDesde).TotalSeconds < 1.2) return;   // quieta 1,2 s o mas: la portada
            // 1.0.12: una pantalla de la consola quieta (la Store, PS Plus, ajustes: el 21-11 13:06 una pagina de la Store quedo como la
            // "portada" de "PS5: STANDARD" y otra como la de Rocket League) no es una portada: se sigue mirando
            {
                string txt = HomeLabel.ScreenText(full), frase;
                if (DetectorMenu.EsPantallaConsola(txt, c, out frase))
                {
                    ps5PortadaPrev = null; ps5PortadaQuietaDesde = DateTime.MinValue;
                    if ((now - ps5PortadaNoLog).TotalSeconds > 10) { ps5PortadaNoLog = now; Log.Write("Portada del juego: lo quieto es una pantalla de la consola (\"" + frase + "\"): no es una portada"); }
                    return;
                }
            }
            ps5PortadaHasta = DateTime.MinValue;                            // (una por arranque)
            ps5PortadaPrev = null;
            double dist;
            string visto = PortadasConsola.Busca(f, out dist);
            string cur;
            lock (profileSync) cur = curGame;
            string dd = dist < 900 ? dist.ToString("0.0", CultureInfo.InvariantCulture) : "-";
            // (VUELTA) con candidato pendiente, una portada quieta es el arranque de un juego: la del candidato o una nueva -> el candidato
            string cand = ps5Candidato;
            double dPre = ps5FirmaAntesMenu != null ? PortadasConsola.Dist(f, ps5FirmaAntesMenu) : 999;
            if (cand != null && visto == null && dPre < 30)
            {
                // Quieta pero no muy distinta de lo de antes del menu (p. ej. el menu de pausa del mismo juego al volver): no es una portada
                ps5PortadaHasta = ps5CandidatoHasta;   // (se sigue mirando mientras dure el candidato)
                return;
            }
            if (cand != null && (visto == null || visto == cand || GameProfiles.SameGame(visto, cand)))
            {
                PasaACandidato(cand, visto != null ? "su portada, distancia " + dd : "una portada nueva");
                if (visto == null) { PortadasConsola.Guarda(cand, f); Log.Write("Portada del juego: nueva, memorizada para \"" + cand + "\""); }
                return;
            }
            if (visto != null)
            {
                if (cur != null && (cur == visto || GameProfiles.SameGame(cur, visto)))
                { Log.Write("Portada del juego: \"" + visto + "\" (distancia " + dd + "): ya era ese"); return; }
                if (ps5SalidaPorIcono && cur != null && JuegoConsola.TagConsola(cur) != null)
                {
                    // El icono manda: esa portada estaba mal asociada (se arranco otro juego con el nombre viejo): pasa a este
                    PortadasConsola.Guarda(cur, f);
                    Log.Write("Portada del juego: estaba con \"" + visto + "\" pero el icono dice \"" + cur + "\": manda el icono, la portada pasa a ese");
                    return;
                }
                Log.Write("Portada del juego: \"" + visto + "\" (distancia " + dd + "): se pone ese juego y sus ajustes (antes \"" + (cur ?? "ninguno") + "\")");
                lock (homeSync) { recoJuego.Pon(JuegoConsola.SinTag(visto)); ps5PortadaJuego = visto; }
                return;
            }
            if (cur != null && (ps5SalidaPorIcono || ps5PortadaFresco) && JuegoConsola.TagConsola(cur) != null)
            {
                PortadasConsola.Guarda(cur, f);
                string png = GuardarDiagPs5(full, null);
                Log.Write("Portada del juego: nueva, memorizada para \"" + cur + "\" (la mas parecida a " + dd + ")" + (png != null ? " -> " + png : ""));
                return;
            }
            Log.Write("Portada del juego: no es ninguna conocida (la mas parecida a " + dd + ") y el juego no se leyo en el menu: no se memoriza");
        }

        // ¿Reconocer los juegos uno a uno con esta consola? (clave del ini ps5_reconocer_juegos / xbox_reconocer_juegos)
        bool RecoJuegosActivo(ConsolaPerfil c) { return JuegoConsola.Activo(c, cfg); }

        // 1.2.1 (SALIDA PRUDENTE): hasta cuando, tras el menu de apagado de la consola (lo de despues es la consola apagandose)
        DateTime PrudenteTrasApagado()
        {
            DateTime pm = powerMenuAt;
            return pm == DateTime.MinValue ? DateTime.MinValue : pm.AddSeconds(PowerMenuSecs);
        }

        static string PctClaros(LecturaMenu l) { return (l.Claros * 100).ToString("0.#", CultureInfo.InvariantCulture) + " %"; }

        int ps5DiagsSesion;
        int ps5DiagsSalida;             // 1.2.0: capturas guardadas al salir del menu (como mucho 3 por sesion)
        static readonly object diagSync = new object();
        static readonly HashSet<string> diagPendientes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // La captura en la que no se leyo la hora (games\debug\ps5, las 8 mas nuevas), para ajustar el lector
        // 21-11 (1.2.1, revision 1.2.03: "captura PS5 en homeSync"): la de la salida del menu se guardaba con homeSync cogido (PNG de 1280x720,
        // ~100-200 ms) y las demas en el hilo de la captura: la entrada en el juego esperaba a que acabara (los "184-305 ms tras decidirlo" del
        // log). Ahora se copia la imagen (~2 ms) y el PNG se escribe en segundo plano; el nombre se sabe ya (para el log). Dos en el mismo
        // segundo ya no se pisan (la segunda lleva "-2")
        static string GuardarDiagPs5(Bitmap full, string ocr)
        {
            try
            {
                string dir = Path.Combine(Paths.Datos, @"games\debug\ps5");
                Directory.CreateDirectory(dir);
                string sello = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), tam = "_" + full.Width + "x" + full.Height + ".png";
                string f = null;
                lock (diagSync)
                {
                    for (int n = 1; f == null; n++)
                    {
                        string q = Path.Combine(dir, sello + (n > 1 ? "-" + n : "") + tam);
                        if (!diagPendientes.Contains(q) && (n > 9 || !File.Exists(q))) f = q;
                    }
                    diagPendientes.Add(f);
                }
                Bitmap copia = full.Clone(new Rectangle(0, 0, full.Width, full.Height), full.PixelFormat);
                string destino = f;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        using (copia) copia.Save(destino, ImageFormat.Png);
                        FileInfo[] all = new DirectoryInfo(dir).GetFiles("*.png");
                        Array.Sort(all, delegate (FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                        for (int i = 8; i < all.Length; i++) { try { all[i].Delete(); } catch { } }
                    }
                    catch (Exception ex) { Log.Error("Menu de la PS5: guardando la captura", ex); }
                    finally { lock (diagSync) diagPendientes.Remove(destino); }
                });
                return f;
            }
            catch (Exception ex) { Log.Error("Menu de la PS5: guardando la captura", ex); return null; }
        }

        // AnalyzeGrab, con una consola con menu propio (la PS5; la Xbox SIN PROBAR): ¿es su menu? Clasifica la captura (DetectorMenu) y
        // decide con histeresis (MaquinaMenu); MenuPs5Tick hace el cambio (AnalyzeGrab lo llama enseguida, sin esperar al HomeTick)
        void MenuPs5Captura(Bitmap full, Bitmap small, bool noSignal)
        {
            DateTime now = DateTime.Now;
            bool inMenu = InHome;
            ConsolaPerfil c = Consolas.Actual;
            bool ocupada = Ps5AppOcupada;
            if (inMenu) ps5CiegoDesde = DateTime.MinValue;   // (se entro en el menu: la salida normal decide)
            // 1.2.05 (VISITA CIEGA): el menu ya no se ve (desde hace 0,6 s) y la app esta libre: que juego se arranco
            if (!inMenu && ps5CiegoDesde != DateTime.MinValue && !ocupada && (now - ps5CiegoUltimo).TotalSeconds >= 0.6)
            {
                int dec; lock (homeSync) dec = menuMaq.Decision;
                if (dec <= 0) SalidaCiega(now, c);
            }
            double[] firmaPrevia = ps5FirmaJuego;
            if (!noSignal) PortadaRapida(full, now, c, inMenu);
            if (!noSignal && !inMenu)
            {
                CandidatoObserva(full, now);
                PortadaObserva(full, now, c);
                if (ps5Candidato == null) ps5FirmaJuego = PortadasConsola.Firma(full); // (jugando: la imagen de ahora)
            }
            // (VUELTA) sin señal con candidato pendiente: la PS5 cambia de modo de video al arrancar otro juego (volver al de antes no)
            if (noSignal && !inMenu && ps5Candidato != null) CandidatoNegro(now, "sin señal");
            if (noSignal)
            {
                LecturaMenu ns = new LecturaMenu();
                ns.Vista = VistaMenu.SinSenal;
                lock (homeSync) { menuMaq.PrudenteHasta = PrudenteTrasApagado(); menuMaq.Observa(ns, now, inMenu); ps5MenuHits = menuMaq.Hits; ps5OutHits = menuMaq.Outs; }
                return;
            }
            // En el menu: la pantalla entera cada segundo aunque se vea la hora (el menu de apagado); sin la hora, siempre
            bool leer = inMenu && (now - ps5TextoAt).TotalSeconds >= 1.0;
            LecturaMenu l = DetectorMenu.Clasifica(full, now, c, inMenu, leer);
            if (!inMenu)
            {
                if (l.Vista == VistaMenu.Menu)
                {
                    if (ps5PrimerMenuAt == DateTime.MinValue) ps5PrimerMenuAt = now;
                    if (ocupada && RecoJuegosActivo(c))
                    {
                        if (ps5CiegoDesde == DateTime.MinValue)
                        {
                            ps5CiegoDesde = now;
                            ps5FirmaAntesCiego = firmaPrevia;
                            Log.Write("Menu de la consola visto mientras la app cambia de motor o de captura: se mira que juego queda en foco");
                        }
                        ps5CiegoUltimo = now;
                    }
                }
                else if (l.Vista == VistaMenu.Juego && ps5CiegoDesde == DateTime.MinValue)
                {
                    ps5PrimerMenuAt = DateTime.MinValue; ps5PrimerFoco = null; ps5PrimerFocoIcono = null;
                }
            }
            if (l.Pantalla != null)
            {
                ps5TextoAt = now;
                Consolas.VistoTexto(l.Pantalla);
                if (RelojPs5.EsMenuApagado(l.Pantalla, c))
                    lock (homeSync) PowerMenuSeen(now, "el menu de apagado de la consola (\"" + ShortText(l.Pantalla) + "\")");
            }
            // Tras el menu de apagado (o el aviso de reposo), el negro es la consola apagandose: ni menu ni juego (no se abre Magpie para nada)
            if (l.Vista == VistaMenu.Plana && powerMenuAt != DateTime.MinValue && (now - powerMenuAt).TotalSeconds < PowerMenuSecs)
            {
                l.Vista = VistaMenu.Oscura;
                l.Porque += "; tras el menu de apagado";
            }
            // (en el menu sin la hora a la vista: las 3 primeras veces de la sesion, al log con la captura, para ajustar el lector)
            if (l.Vista != VistaMenu.Menu && inMenu && ps5DiagsSesion < 3 && l.Vista != VistaMenu.Plana)
            {
                ps5DiagsSesion++;
                string diag = GuardarDiagPs5(full, null);
                if (l.Vista == VistaMenu.Juego) l.Porque = (l.Porque != null ? l.Porque + "; " : "") + "media " + l.Media + ", " + PctClaros(l) + " clara";
                Log.Write("Menu de la consola: sin la hora arriba (" + (l.Porque ?? "?") + ", " + l.Vista + (l.Frase != null ? ": \"" + l.Frase + "\"" : "") +
                          ", captura de " + full.Width + "x" + full.Height + ", " + l.Ms.ToString("0", CultureInfo.InvariantCulture) + " ms)" + (diag != null ? " -> " + diag : ""));
            }
            else if (l.Vista != VistaMenu.Menu && l.ParecioHora && l.Ocr != null && (now - ps5FalloLog).TotalSeconds >= 60)
            {
                ps5FalloLog = now;
                string diag = GuardarDiagPs5(full, l.Ocr);
                Log.Write("Menu de la consola: arriba parece texto, pero no es la hora del PC (OCR \"" + ShortText(l.Ocr) + "\", " +
                          now.ToString("HH:mm", CultureInfo.InvariantCulture) + ", captura de " + full.Width + "x" + full.Height + ")" +
                          (diag != null ? " -> " + diag : ""));
            }
            // Jugando, el menu visto: su miniatura (la imagen de mpv, sin Magpie) para comprobar la copia de la pantalla del telon congelado
            // (PutFrozenCurtain: sin ella pedia la imagen a mpv, 230-380 ms; con ella, la copia de la pantalla, ~125 ms)
            if (l.Vista == VistaMenu.Menu && !inMenu)
            {
                double lit;
                lastHomeThumb = HomeLabel.Thumb(small, out lit);
                lastHomeThumbAt = now;
            }
            // 20-11 (PS5JUEGO): en el menu / centro de control / conmutador (todos llevan la hora: VistaMenu.Menu) se lee el nombre del juego
            // en foco y se reconoce el juego (histeresis: se mantiene el ultimo si ahora no se lee). Cada ~0,8 s, solo si la clave del ini lo
            // enciende; si no se reconoce nada, al salir se usa el perfil unico de la consola ("Consola"), como hasta ahora
            if (l.Vista == VistaMenu.Menu && RecoJuegosActivo(c) && (now - ps5NombreAt).TotalSeconds >= 0.35)
            {
                ps5NombreAt = now;
                try
                {
                    string crudo;
                    bool fuerte;
                    string nombre = JuegoConsola.LeeNombre(full, c, out crudo, out fuerte);
                    double[] huella = JuegoConsola.Huella(full);
                    string reco;
                    lock (homeSync) reco = recoJuego.Observa(nombre, fuerte, huella, now);
                    // 21-11 (quinta vuelta): en el inicio de la PS5, el icono del juego en foco (lo que manda al entrar)
                    // (el icono solo con la etiqueta del nombre legible en la misma captura: el menu quieto, no a mitad de una animacion)
                    // (1.2.1: el icono se calcula fuera de homeSync: con la Xbox busca la tarjeta en la imagen entera)
                    double[] icFoco = (c.Familia == "playstation" || c.Familia == "xbox") && fuerte ? JuegoConsola.IconoFoco(full, c) : null;
                    if (icFoco != null)
                        lock (homeSync) FocoObserva(icFoco, now, nombre);
                    // 1.2.05: el primer juego en foco al abrir el menu desde un juego (el que esta en marcha), si se lee en 1,5 s
                    if (!inMenu && fuerte && !string.IsNullOrEmpty(nombre) && ps5PrimerFoco == null && ps5CiegoDesde == DateTime.MinValue &&
                        ps5PrimerMenuAt != DateTime.MinValue && (now - ps5PrimerMenuAt).TotalSeconds <= 1.5)
                    {
                        ps5PrimerFoco = nombre;
                        ps5PrimerFocoIcono = icFoco ?? JuegoConsola.IconoFoco(full, c);
                    }
                    // 21-11: cada lectura nueva al log (para ver que lee en el menu)
                    string lect = (nombre ?? "") + (fuerte ? "/f" : "");
                    if (nombre != null && lect != ps5LecturaLog)
                    {
                        ps5LecturaLog = lect;
                        Log.Write("Menu de la consola: leido \"" + nombre + "\"" + (fuerte ? " (la etiqueta del juego en foco: vale a la primera)" : " (suelto: hacen falta 2 lecturas)") +
                                  " (OCR \"" + ShortText(crudo) + "\")");
                    }
                    if (!string.IsNullOrEmpty(reco) && reco != ps5JuegoLog)
                    {
                        ps5JuegoLog = reco;
                        Log.Write("Juego de la " + c.Corto + " reconocido: \"" + reco + "\"" +
                                  (nombre != null ? " (OCR \"" + ShortText(crudo) + "\")" : " (por su huella)"));
                    }
                }
                catch (Exception ex) { Log.Error("Reconociendo el juego de la consola", ex); }
            }
            // Con consola=auto (cada 5 s): su menu visto refuerza la puesta (la PS5; con la Xbox, se mira si es la hora de la PS5 en su sitio);
            // sin el, los menus de las otras consolas (20-11, CONSOLA: PistasMenu, src\DetectorMenu.cs)
            if (!Consolas.Fija) MenuPs5Pista(full, false, l);
            // 1.0.12: dentro de una app de la consola (Remote Play, la galeria...): se acaba al volver a ver su menu (la hora / las pestañas
            // del inicio, o el centro de control); si la imagen es la del juego que se jugaba antes de abrir el menu, se ha vuelto a el
            // 1.0.12 (el usuario: "al entrar a una captura de pantalla sigue confundiendose"): una pantalla de una app de la consola (la galeria,
            // Remote Play, la Store...) dice en que app se esta, aunque no se viera abrirla (13:21: la app se abrio con la galeria ya delante y
            // una captura a pantalla completa, sin texto, se tomo por un juego)
            if (inMenu && l.Vista == VistaMenu.MenuTexto && l.Frase != null)
            {
                string a = JuegoConsola.AppDeFrase(l.Frase);
                if (a != null && a != ps5AppSistema)
                {
                    ps5AppSistema = a;
                    ps5AppVuelta = ps5JuegoAntesMenu != null && !JuegoConsola.EsAppSistema(ps5JuegoAntesMenu) ? ps5JuegoAntesMenu : null;
                    Log.Write("Menu de la consola: dentro de \"" + a + "\" (\"" + l.Frase + "\"): lo que se vea dentro no es un juego hasta volver a su menu");
                }
            }
            if (ps5AppSistema != null)
            {
                if (l.Vista == VistaMenu.Menu || (l.Vista == VistaMenu.MenuTexto && l.Frase == "centro de control"))
                {
                    Log.Write("De vuelta en el menu de la consola desde \"" + ps5AppSistema + "\"");
                    ps5AppSistema = null; ps5AppVuelta = null;
                }
                else if (inMenu && ps5AppVuelta != null && ps5FirmaAntesMenu != null && l.Vista == VistaMenu.Juego &&
                         PortadasConsola.Dist(PortadasConsola.Firma(full), ps5FirmaAntesMenu) < MismaEscena)
                {
                    string vuelta = ps5AppVuelta;
                    Log.Write("Desde \"" + ps5AppSistema + "\": la imagen es la de \"" + vuelta + "\" de antes del menu: se ha vuelto a ese juego");
                    ps5AppSistema = null; ps5AppVuelta = null;
                    ps5VueltaForzada = vuelta;
                }
            }
            lock (homeSync)
            {
                VistaMenu antes = ps5UltimaVista;
                ps5UltimaVista = l.Vista;
                int d0 = menuMaq.Decision;
                menuMaq.PrudenteHasta = PrudenteTrasApagado();
                int d = menuMaq.Observa(l, now, inMenu);
                // 1.2.1 (SALIDA PRUDENTE, src\DetectorMenu.cs): sin el menu visto desde que se abrio la app o tras su menu de apagado, lo que
                // no es el menu solo saca con una imagen de juego clara y seguida (una vez por cada motivo, al log)
                if (!inMenu || !menuMaq.Prudente(now)) ps5PrudenteDicho = null;
                else if (d == 0 && l.Vista == VistaMenu.Juego)
                {
                    string pq = menuMaq.PorQuePrudente(now);
                    if (pq != ps5PrudenteDicho)
                    {
                        ps5PrudenteDicho = pq;
                        Log.Write("Menu de la consola: sin la hora y " + pq + ": salida prudente, solo saca del menu una imagen de juego clara y seguida " +
                                  menuMaq.PrudenteS.ToString("0.#", CultureInfo.InvariantCulture) + " s (esta: " + (l.Tenue ? "oscura" : "clara") + ", media " +
                                  l.Media + ", " + PctClaros(l) + " clara)");
                    }
                }
                if (l.Hora != null) ps5Hora = l.Hora;
                if (l.Vista == VistaMenu.Menu || l.Vista == VistaMenu.MenuTexto) homeSeen = now;
                if (!inMenu && l.Vista == VistaMenu.Menu && menuMaq.Hits == 1 && d == 0)
                {
                    Log.Write("Menu de la consola visto (la hora \"" + l.Hora + "\" arriba, la del PC, a un minuto): se confirma con otra captura");
                    AjustesQuitarYa("el menu de la consola a la vista", false);
                }
                if (inMenu && l.Vista == VistaMenu.MenuTexto && antes != VistaMenu.MenuTexto && l.Frase != null)
                    Log.Write("Menu de la consola: sin la hora, pero es una pantalla de la consola (\"" + l.Frase + "\"): se sigue a 4K nativo");
                if (inMenu && l.Vista == VistaMenu.Plana && antes != VistaMenu.Plana)
                    Log.Write("Menu de la consola: imagen en negro (" + l.Porque + "): ni menu ni juego; tras el menu, si sigue " +
                              menuMaq.NegroSalirS.ToString("0.#", CultureInfo.InvariantCulture) + " s, se arranca un juego");
                if (d != 0 && d0 == 0)
                {
                    ps5DecididoAt = now;
                    // 1.2.0: la captura que saca del menu, a games\debug (las 3 primeras de la sesion), para ver si era un juego de verdad
                    string diagSal = null;
                    if (d < 0 && l.Vista == VistaMenu.Juego && ps5DiagsSalida < 3) { ps5DiagsSalida++; diagSal = GuardarDiagPs5(full, null); }
                    Log.Write((d > 0 ? "Menu de la consola: decidido (" : "Fuera del menu de la consola: decidido (") + menuMaq.Porque + "; " +
                              (long)(now - menuMaq.PrimeraAt).TotalMilliseconds + " ms desde la primera captura, esta leida en " +
                              l.Ms.ToString("0", CultureInfo.InvariantCulture) + " ms)" + (diagSal != null ? " -> " + diagSal : ""));
                }
                ps5MenuHits = menuMaq.Hits;
                ps5OutHits = menuMaq.Outs;
            }
        }

        // AnalyzeGrab, con otra consola puesta y consola=auto: ¿es el menu de la PS5 o de la Xbox? (cada 5 s; no en el HOME de la Switch 2:
        // tambien tiene la hora arriba, con la bateria a la derecha). La PS5: su sitio exacto, prueba propia (Consolas.VistoMenuPs5); la
        // Xbox: la hora del PC en la franja de arriba, fuera del sitio de la de la PS5 y de la del HOME de Nintendo (DetectorMenu.PistaReloj),
        // solo de apoyo (Consolas.VistoMenuXbox). 20-11 (CONSOLA): todo en PistasMenu (src\DetectorMenu.cs), tambien con su menu visto
        void MenuPs5Pista(Bitmap full, bool switchHome) { MenuPs5Pista(full, switchHome, null); }

        void MenuPs5Pista(Bitmap full, bool switchHome, LecturaMenu l)
        {
            if (switchHome || Consolas.Fija) return;
            try { pistasMenu.Observa(full, DateTime.Now, Consolas.Actual, l, false); }
            catch (Exception ex) { Log.Error("Consola: pistas del menu", ex); }
        }

        // HomeTick con una consola con menu propio (cada 0,5 s y tras cada captura): hace lo que decidio MenuPs5Captura
        void MenuPs5Tick(DateTime now)
        {
            bool enter = false, leave = false;
            lock (homeSync)
            {
                int d = menuMaq.Decision;
                if (homeBusy || passesBusy || relaunchSize != null || relaunchEngine || relevo != null) { }
                else if (!InHome && cfg.HomeNativeEnabled && d > 0) enter = true;
                else if (InHome && d < 0) leave = true;
                else if (d != 0 && (d > 0) == InHome) menuMaq.Decision = 0; // (ya estaba: nada)
            }
            if (enter) MenuPs5Entrar();
            else if (leave) MenuPs5Salir();
            else
            {
                // 21-11 (PORTADA): el juego reconocido por su portada al arrancar: sus ajustes (como al entrar desde el menu)
                string pj = null;
                lock (homeSync)
                    if (ps5PortadaJuego != null && !InHome && !homeBusy && !passesBusy && relaunchSize == null && !relaunchEngine && relevo == null)
                    { pj = ps5PortadaJuego; ps5PortadaJuego = null; }
                if (pj != null)
                {
                    ps5JuegoLog = JuegoConsola.SinTag(pj);
                    enterSure = true;
                    enterConfirmed = true;
                    enterIcon = null;
                    EnterGame(pj);
                }
            }
            ApplyPending();
        }

        string Ps5Tarda()
        {
            DateTime a = ps5DecididoAt;
            return a == DateTime.MinValue ? "" : " (" + (long)(DateTime.Now - a).TotalMilliseconds + " ms tras decidirlo)";
        }

        // Jugando, el menu de la PS5: a 4K nativo, sin Magpie, sin generacion ni Smooth Motion
        void MenuPs5Entrar()
        {
            if (!cfg.HomeNativeEnabled || cfg.HomeNative || homeBusy || passesBusy || relaunchSize != null) return;
            AjustesQuitarYa("al menu de la consola", false);
            DateTime now = DateTime.Now;
            lock (homeSync)
            {
                homeSeen = homeArrivedAt = now;
                noLabel = 0; homeGame = null; labelNoName = false; homeInUse = false; homeDock = true;
                ps5OutHits = ps5MenuHits = 0;
                ps5Foco = ps5FocoPrev = null; ps5FocoQuieto = false; ps5Lecturas.Clear();
                ps5Candidato = null;
                ps5PortadaVista = null; ps5RapidaPrev = null;
                menuMaq.Hecho(now, true);
            }
            // (VUELTA) el juego que se estaba jugando al abrir el menu, y su imagen
            lock (profileSync) ps5JuegoAntesMenu = curGame;
            ps5FirmaAntesMenu = ps5FirmaJuego;
            ps5CiegoDesde = DateTime.MinValue;
            // 1.2.05: el primer juego en foco es el que esta en marcha: si no es el que creia la app, manda ese
            if (RecoJuegosActivo(Consolas.Actual))
            {
                string pf = PerfilPrimerFoco(Consolas.Actual);
                string antes = ps5JuegoAntesMenu;
                if (pf != null && !JuegoConsola.EsAppSistema(pf) && antes != null && JuegoConsola.TagConsola(antes) != null &&
                    antes != pf && !GameProfiles.SameGame(antes, pf))
                {
                    Log.Write("Menu de la consola: el juego en foco al abrirlo (el que esta en marcha) es \"" + pf + "\", no \"" + antes +
                              "\" (se arranco sin que la app lo viera): se toma ese como el juego de antes del menu");
                    ps5JuegoAntesMenu = pf;
                }
            }
            ps5PrimerFoco = null; ps5PrimerFocoIcono = null; ps5PrimerMenuAt = DateTime.MinValue;
            pendingPassesRestart = false;
            bool sm = Config.EngineActive && Config.EngineSmoothRunning > 0;
            Log.Write("Menu de la consola (la hora \"" + ps5Hora + "\" arriba): a 4K nativo a 60 fps, sin Magpie ni generacion" +
                      (sm ? "; el motor lleva Smooth Motion: se vuelve a abrir sin el (el driver solo lo mira al abrirlo)" : ", sin Smooth Motion") +
                      (curGame != null ? " (\"" + curGame + "\" sigue con sus ajustes guardados)" : "") + Ps5Tarda());
            if (sm && cfg.EngineNow)
            {
                // Una sola vez: el motor nuevo se abre ya con la captura de 4K y sin Smooth Motion (Config.EngineSmoothMode = 0 en el menu)
                lock (lsModeSync) cfg.HomeNative = true;
                ReabrirMotorSmooth(0, "Menú", "4K nativo");
                return;
            }
            StartHomeSwitch(true, MenuTexto, "4K nativo");
        }

        // Del menu de la PS5 al juego: los ajustes de toda la consola (su perfil, "Consola")
        // 1.0.12: la app de la consola abierta desde el menu (Remote Play, la galeria, la Store, Netflix...): se sigue como en el menu
        string ps5AppSistema, ps5AppVuelta, ps5VueltaForzada;

        void MenuPs5Salir()
        {
            if (!cfg.HomeNative || homeBusy || passesBusy || relaunchSize != null) return;
            // (dentro de una app de la consola: nada de juego hasta volver a su menu o al juego de antes; salvo el negro de arrancar un juego,
            // que sale como siempre: "Jugar" en la biblioteca o en la Store)
            bool salidaNegroApp = false;
            if (ps5AppSistema != null)
            {
                string pq;
                lock (homeSync) pq = menuMaq.Porque ?? "";
                if (!pq.StartsWith("negro")) { lock (homeSync) menuMaq.Reiniciar(); return; }
                Log.Write("Desde \"" + ps5AppSistema + "\": " + pq + ": se arranca un juego");
                salidaNegroApp = true;
                ps5AppSistema = null; ps5AppVuelta = null;
            }
            ConsolaPerfil c = Consolas.Actual;
            // 20-11 (PS5JUEGO): el perfil del juego reconocido ("PS5: <juego>" / "Xbox Series: <juego>"); si no se reconocio nada (o la clave
            // del ini esta apagada), el perfil unico de toda la consola ("Consola"), como hasta ahora
            string juego;
            bool porUltima = false, fuerteVisita = false;
            DateTime primeraFuera;
            lock (homeSync)
            {
                ps5CiegoDesde = DateTime.MinValue;
                primeraFuera = menuMaq.PrimeraAt;   // (1.2.1: la primera captura sin el menu: la edad del foco se mide desde ahi)
                menuMaq.Hecho(DateTime.Now, false); ps5OutHits = ps5MenuHits = 0; juego = RecoJuegosActivo(c) ? recoJuego.AlSalir(DateTime.Now, out porUltima) : null;
                fuerteVisita = recoJuego.FuerteEnVisita;
                // 21-11 (PORTADA): 15 s para ver la portada del juego si arranca
                ps5PortadaFresco = recoJuego.ConfirmadoEnVisita;
                ps5PortadaHasta = DateTime.Now.AddSeconds(15);
                ps5PortadaPrev = null;
                ps5PortadaJuego = null;
            }
            string perfil = string.IsNullOrEmpty(juego) ? c.PerfilConsola : JuegoConsola.NombrePerfil(c, juego);
            // 21-11 (quinta vuelta; el usuario: "tiene que ser infalible con cualquier juego"): manda el ICONO del juego en foco en la
            // ultima captura del inicio (el que se arranca), como el icono del HOME de la Switch 2; el nombre solo le pone titulo
            string infoIcono = null;
            ps5SalidaPorIcono = false;
            if (RecoJuegosActivo(c))
            {
                string pi;
                FotoFoco foto;
                lock (homeSync) foto = TomaFoco();
                pi = PerfilPorIcono(c, DateTime.Now, primeraFuera, foto, out infoIcono, 4);
                // 1.2.1: sin icono y sin la etiqueta del inicio leida en esta visita, el nombre reconocido son 2 lecturas iguales de otra
                // cosa (el centro de control: "ácil de usar" de su tarjeta "Fácil de usar"): sin un perfil con ese nombre no se crea uno
                // nuevo ni se cambia el juego en marcha
                if (pi == null && !fuerteVisita && !string.IsNullOrEmpty(juego) && GameProfiles.Find(JuegoConsola.NombrePerfil(c, juego)) == null)
                {
                    string enM = ps5JuegoAntesMenu != null && JuegoConsola.TagConsola(ps5JuegoAntesMenu) != null ? ps5JuegoAntesMenu : null;
                    Log.Write("Fuera del menu de la consola: \"" + juego + "\" no se leyo en la etiqueta del juego en foco (2 lecturas de otra cosa) y no hay " +
                              "perfil con ese nombre: no se crea; " + (enM != null ? "sigue \"" + enM + "\"" : "el perfil de la consola"));
                    juego = enM != null ? JuegoConsola.SinTag(enM) : null;
                    perfil = enM ?? c.PerfilConsola;
                    lock (homeSync) { if (enM != null) recoJuego.Pon(juego); else recoJuego.Reiniciar(); }
                }
                ps5Candidato = null;
                ps5SalidaAt = DateTime.Now;
                string enMarcha = ps5JuegoAntesMenu;
                string pv = ps5PortadaVista;
                if (pv != null && (DateTime.Now - ps5PortadaVistaAt).TotalSeconds < 4)
                {
                    // La portada de un juego conocido ya en pantalla al salir: ese se arranca (manda sobre todo)
                    perfil = pv;
                    juego = JuegoConsola.SinTag(pv);
                    lock (homeSync) recoJuego.Pon(juego);
                    infoIcono = "por su portada, ya en pantalla" + (pi != null && pi != pv ? "; el icono decia \"" + pi + "\"" : "");
                    ps5SalidaPorIcono = true;
                }
                else if (pi != null && enMarcha != null && JuegoConsola.TagConsola(enMarcha) != null && enMarcha != pi && !GameProfiles.SameGame(enMarcha, pi))
                {
                    // Otro juego en foco que el que se jugaba: ¿se arranca o se vuelve al de antes? Se decide al ver la imagen (CandidatoObserva)
                    ps5Candidato = pi;
                    ps5CandidatoHasta = DateTime.Now.AddSeconds(8);
                    perfil = enMarcha;
                    juego = JuegoConsola.SinTag(enMarcha);
                    lock (homeSync) recoJuego.Pon(juego);
                    infoIcono = "se jugaba \"" + enMarcha + "\" y el icono en foco es \"" + pi + "\" (" + infoIcono + "): sigue el de antes salvo que se vea arrancar el otro";
                    ps5SalidaPorIcono = false;
                    ps5NegroDesde = DateTime.MinValue;
                }
                else if (pi != null)
                {
                    perfil = pi;
                    juego = JuegoConsola.SinTag(pi);
                    lock (homeSync) recoJuego.Pon(juego);
                    ps5SalidaPorIcono = true;
                }
            }
            // 1.0.12: vuelta al juego de antes desde una app de la consola (su imagen otra vez): ese juego
            if (ps5VueltaForzada != null)
            {
                perfil = ps5VueltaForzada;
                juego = JuegoConsola.SinTag(perfil);
                lock (homeSync) recoJuego.Pon(juego);
                ps5Candidato = null;
                ps5SalidaPorIcono = true;
                infoIcono = "vuelta desde una app de la consola: su imagen de antes del menu";
                ps5VueltaForzada = null;
            }
            // 1.0.12 (el usuario: "piensa que he entrado a un juego al entrar a remoteplay, galeria, etc."): lo que se abre es una app de la
            // consola, no un juego: ni perfil, ni Magpie, ni generacion; a 4K nativo como en el menu hasta volver a el
            string app = JuegoConsola.EsAppSistema(perfil) ? perfil : JuegoConsola.EsAppSistema(juego) ? juego :
                         (ps5Candidato != null && JuegoConsola.EsAppSistema(ps5Candidato)) ? ps5Candidato : null;
            if (app != null && !salidaNegroApp)
            {
                bool cand = ps5Candidato != null && app == ps5Candidato;
                ps5AppSistema = JuegoConsola.SinTag(app);
                // (si se jugaba algo, su imagen de antes del menu dice si en realidad se ha vuelto a el con el foco en la app)
                ps5AppVuelta = cand ? perfil : (ps5JuegoAntesMenu != null && !JuegoConsola.EsAppSistema(ps5JuegoAntesMenu) ? ps5JuegoAntesMenu : null);
                ps5Candidato = null;
                lock (homeSync) { menuMaq.Reiniciar(); ps5PortadaHasta = DateTime.MinValue; ps5PortadaPrev = null; }
                Log.Write("Fuera del menu de la consola: \"" + ps5AppSistema + "\" es una app de la consola, no un juego: se sigue como en el menu " +
                          "(4K nativo, sin perfil ni generacion) hasta volver a su menu" + (ps5AppVuelta != null ? " o a \"" + ps5AppVuelta + "\"" : "") + Ps5Tarda());
                return;
            }
            if (!string.IsNullOrEmpty(juego)) ps5JuegoLog = juego;
            Log.Write("Fuera del menu de la consola: al juego \"" + perfil + "\"" +
                      (ps5SalidaPorIcono ? " (" + infoIcono + ")" : (infoIcono != null ? " [" + infoIcono + "]" : "") +
                      (string.IsNullOrEmpty(juego) ? " (perfil unico de la consola)" : porUltima ? " (por su nombre en el menu)" : " (reconocido)")) + Ps5Tarda());
            enterSure = true;
            enterConfirmed = true;
            enterIcon = null;
            EnterGame(perfil); // (cfg.HomeNative: LeaveHome, que mira MenuPs5SalirConMotor)
        }

        // LeaveHome, antes de StartHomeSwitch: con el menu de la PS5 a 4K y un juego que lleva Smooth Motion, el motor se abre una sola vez
        // con todo lo del juego (si no, cambiaria sin el y se volveria a abrir enseguida para ponerlo: dos cortes). true = hecho
        bool MenuPs5SalirConMotor(string title, string subtitle)
        {
            if (!Consolas.Actual.MenuPropio || !Config.EngineActive) return false;
            bool wasHome = cfg.HomeNative;
            PrepareCfg();
            lock (lsModeSync) cfg.HomeNative = false;
            bool engine = cfg.EngineNow;
            int want = cfg.EngineSmoothMode;
            if (!engine || want == Config.EngineSmoothRunning || Config.EngineSmoothRunning < 0)
            {
                lock (lsModeSync) cfg.HomeNative = wasHome; // (StartHomeSwitch lo cambia como siempre)
                return false;
            }
            Log.Write("Al juego: lleva Smooth Motion (" + (want == 2 ? "sincronizacion cada 2 refrescos" : "modo 60") + ") y el motor del menu no: se abre una vez con todo");
            ReabrirMotorSmooth(want, title, subtitle);
            return true;
        }
    }
}
