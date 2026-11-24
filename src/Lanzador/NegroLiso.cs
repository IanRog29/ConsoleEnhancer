// NegroLiso.cs - 24-11-2026 (1.3.3, VARIOS): ¿la captura es negro LISO en todo el cuadro? Para el aviso de HDCP (ConsolasApp.ObservaConsola).
//
// El usuario (1.3.2): "Cuando estoy en gta 6 por ejemplo en una pantalla negra con algun hud quieto unos segundos se piensa que esta lo del
// hdcp eso de los ajustes desactivado". En su log: "negro de verdad y en silencio 21 s seguidos (lit 0.001 ...)": el 0,1 % de la imagen
// con luz era el HUD quieto del juego sobre negro, y el umbral de "negro de verdad" (lit < 0,004 de la miniatura de 640x360 a saltos de 2)
// lo dejaba pasar. Con el HDCP la capturadora da un negro uniforme en TODO el cuadro (sin un solo pixel distinto); un HUD, un logo, un
// texto o una pantalla de carga oscura no lo son. Ahora, ademas del umbral de antes (barato, de la miniatura), la captura entera se mira
// pixel a pixel (a saltos para no pasar de ~1 millon de muestras: todos a 1280x720, 1 de cada 3 en filas y columnas a 4K): el valor de
// fondo (el mas repetido) tiene que ser negro (luma <= 24, tambien el 16 del rango limitado) y nada puede salirse de el mas de 10 niveles
// salvo como mucho 2 muestras sueltas. Un simbolo de 8x8 pixeles a 4K ya no es negro liso. C# 5
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleEnhancer
{
    static class NegroLiso
    {
        public const int FondoMax = 24;        // luma del fondo: negro (0) o el negro del rango limitado (16) con algo de margen
        public const int Tolerancia = 10;      // niveles que una muestra puede apartarse del fondo (ruido de la conversion de color)
        public const int SueltasMax = 2;       // muestras que se pueden salir (pixeles sueltos de la capturadora)
        public const int MuestrasMax = 1000000;

        public struct Medida
        {
            public bool Liso;
            public int Fondo, Max, Fuera, Muestras, Paso;
            public override string ToString()
            {
                return (Liso ? "negro liso" : "no liso") + " (fondo " + Fondo + ", max " + Max + ", fuera " + Fuera + " de " + Muestras + " muestras, paso " + Paso + ")";
            }
        }

        // La captura (Format32bppRgb, como HomeLabel.FromRaw). Con cualquier fallo, no liso (mejor no avisar)
        public static Medida Mide(Bitmap full)
        {
            Medida m = new Medida();
            if (full == null || full.Width <= 0 || full.Height <= 0) return m;
            try
            {
                BitmapData d = full.LockBits(new Rectangle(0, 0, full.Width, full.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    byte[] px = new byte[d.Stride * full.Height];
                    Marshal.Copy(d.Scan0, px, 0, px.Length);
                    return Mide(px, full.Width, full.Height, d.Stride);
                }
                finally { full.UnlockBits(d); }
            }
            catch { return m; }
        }

        // bgr0 / bgra, stride en bytes
        public static Medida Mide(byte[] px, int w, int h, int stride)
        {
            Medida m = new Medida();
            if (px == null || w <= 0 || h <= 0 || (long)stride * h > px.Length) return m;
            int paso = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)w * h / MuestrasMax)));
            int[] hist = new int[256];
            int n = 0;
            for (int y = 0; y < h; y += paso)
            {
                int o = y * stride;
                for (int x = 0; x < w; x += paso)
                {
                    int i = o + x * 4;
                    hist[(19 * px[i] + 183 * px[i + 1] + 54 * px[i + 2]) >> 8]++;
                    n++;
                }
            }
            int fondo = 0, max = 0;
            for (int v = 0; v < 256; v++) { if (hist[v] > hist[fondo]) fondo = v; if (hist[v] > 0) max = v; }
            int fuera = 0;
            for (int v = 0; v < 256; v++) if (Math.Abs(v - fondo) > Tolerancia) fuera += hist[v];
            m.Fondo = fondo; m.Max = max; m.Fuera = fuera; m.Muestras = n; m.Paso = paso;
            m.Liso = n > 0 && fondo <= FondoMax && fuera <= SueltasMax;
            return m;
        }

        // ------------------------------------------------------------------ prueba: --test-negro-liso[=<informe>][;<carpeta con capturas png/jpg>]
        // Capturas sinteticas a 4K, 1440p y 1280x720 (la ligera): negro liso (0 y 16), negro con ruido +-3, negro con un HUD pequeno (minimapa,
        // un icono de 12x12, una linea de texto), un logo en el centro, una pantalla de carga oscura (degradado + rueda), un pixel suelto; y
        // las capturas reales de la carpeta (sus nombres: "liso*" tienen que dar liso; las demas, no)
        public static int Prueba(string arg)
        {
            string informe = null, carpeta = null;
            if (!string.IsNullOrEmpty(arg))
            {
                string[] p = arg.Split(';');
                if (p[0].Length > 0) informe = p[0];
                if (p.Length > 1 && p[1].Length > 0) carpeta = p[1];
            }
            StringBuilder sb = new StringBuilder();
            int fallos = 0, total = 0;
            int[][] tam = { new[] { 3840, 2160 }, new[] { 2560, 1440 }, new[] { 1280, 720 } };
            foreach (int[] t in tam)
            {
                int w = t[0], h = t[1];
                double k = w / 3840.0;
                Caso(sb, ref fallos, ref total, w, h, "negro 0", true, delegate (Graphics g) { });
                Caso(sb, ref fallos, ref total, w, h, "negro 16 (rango limitado)", true, delegate (Graphics g) { g.Clear(Color.FromArgb(16, 16, 16)); });
                Caso(sb, ref fallos, ref total, w, h, "negro con ruido +-3", true, null, 3);
                Caso(sb, ref fallos, ref total, w, h, "un pixel suelto blanco", true, delegate (Graphics g) { g.FillRectangle(Brushes.White, w / 2, h / 2, 1, 1); });
                Caso(sb, ref fallos, ref total, w, h, "HUD: minimapa abajo a la izquierda", false, delegate (Graphics g)
                {
                    g.FillEllipse(new SolidBrush(Color.FromArgb(60, 70, 60)), (int)(90 * k), h - (int)(420 * k), (int)(330 * k), (int)(330 * k));
                });
                Caso(sb, ref fallos, ref total, w, h, "HUD: icono de 12x12 (a 4K)", false, delegate (Graphics g)
                {
                    g.FillRectangle(new SolidBrush(Color.FromArgb(200, 200, 200)), w - (int)(200 * k), (int)(120 * k), Math.Max(4, (int)(12 * k)), Math.Max(4, (int)(12 * k)));
                });
                Caso(sb, ref fallos, ref total, w, h, "HUD: una linea de texto gris", false, delegate (Graphics g)
                {
                    using (Font f = new Font("Arial", (float)Math.Max(9, 26 * k), FontStyle.Regular, GraphicsUnit.Pixel))
                        g.DrawString("Pulsa X para continuar", f, new SolidBrush(Color.FromArgb(120, 120, 120)), (float)(w * 0.42), (float)(h * 0.9));
                });
                Caso(sb, ref fallos, ref total, w, h, "logo en el centro", false, delegate (Graphics g)
                {
                    g.FillEllipse(new SolidBrush(Color.FromArgb(180, 30, 30)), w / 2 - (int)(150 * k), h / 2 - (int)(150 * k), (int)(300 * k), (int)(300 * k));
                });
                Caso(sb, ref fallos, ref total, w, h, "carga oscura (degradado + rueda)", false, delegate (Graphics g)
                {
                    using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(2, 2, 4), Color.FromArgb(30, 28, 40), 90f))
                        g.FillRectangle(b, 0, 0, w, h);
                    g.DrawEllipse(new Pen(Color.FromArgb(150, 150, 150), (float)Math.Max(2, 6 * k)), w - (int)(160 * k), h - (int)(160 * k), (int)(80 * k), (int)(80 * k));
                });
                Caso(sb, ref fallos, ref total, w, h, "escena muy oscura sin HUD (gris 30)", false, delegate (Graphics g) { g.Clear(Color.FromArgb(30, 30, 30)); g.FillRectangle(new SolidBrush(Color.FromArgb(50, 50, 50)), 0, 0, w / 3, h); });
            }
            if (carpeta != null && System.IO.Directory.Exists(carpeta))
            {
                int reales = 0, litViejo = 0, hudViejo = 0;
                foreach (string f in System.IO.Directory.GetFiles(carpeta, "*.*", System.IO.SearchOption.AllDirectories))
                {
                    string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".png" && ext != ".jpg" && ext != ".bmp") continue;
                    string nom = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (nom.EndsWith("_nombre", StringComparison.OrdinalIgnoreCase) || nom.EndsWith("_mascara", StringComparison.OrdinalIgnoreCase)) continue;
                    bool quiere = nom.StartsWith("liso", StringComparison.OrdinalIgnoreCase) || nom.StartsWith("negro_", StringComparison.OrdinalIgnoreCase);
                    try
                    {
                        using (Bitmap b0 = new Bitmap(f))
                        using (Bitmap b = new Bitmap(b0.Width, b0.Height, PixelFormat.Format32bppRgb))
                        {
                            if (b0.Width < 320 || b0.Height < 180) continue;
                            using (Graphics g = Graphics.FromImage(b)) g.DrawImage(b0, 0, 0, b0.Width, b0.Height);
                            reales++;
                            Medida m = Mide(b);
                            double lit;
                            using (Bitmap s = HomeLabel.Small(b)) HomeLabel.Thumb(s, out lit);
                            if (lit < 0.004) litViejo++;
                            // (una captura real negra del todo -max 0, como las de la capturadora sin imagen- es negro liso aunque su nombre no lo diga)
                            bool ok = m.Liso == quiere || (m.Liso && m.Max == 0);
                            total++; if (!ok) fallos++;
                            if (!ok || lit < 0.004)
                                sb.AppendLine((ok ? "bien   " : "FALLO  ") + "real " + f.Substring(carpeta.Length).TrimStart('\\') + " " + b.Width + "x" + b.Height + ": " + m +
                                              "; lit " + lit.ToString("0.####", CultureInfo.InvariantCulture) + (lit < 0.004 ? " (la 1.3.2 lo contaba como negro)" : ""));
                            // un trozo de la captura (la esquina con mas luz, 1/8 x 1/10 del cuadro: un HUD de verdad) sobre negro
                            int cw = b.Width / 8, ch = b.Height / 10;
                            Rectangle[] esq = { new Rectangle(0, 0, cw, ch), new Rectangle(b.Width - cw, 0, cw, ch), new Rectangle(0, b.Height - ch, cw, ch), new Rectangle(b.Width - cw, b.Height - ch, cw, ch) };
                            Rectangle mejor = esq[0]; double luz = -1;
                            foreach (Rectangle r in esq)
                                using (Bitmap c = b.Clone(r, PixelFormat.Format32bppRgb)) { Medida mc = Mide(c); double v = mc.Fuera; if (v > luz) { luz = v; mejor = r; } }
                            if (luz <= SueltasMax) continue;   // (las cuatro esquinas negras: no hay HUD que pegar)
                            using (Bitmap h2 = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppRgb))
                            {
                                using (Graphics g = Graphics.FromImage(h2)) { g.Clear(Color.Black); g.DrawImage(b, mejor, mejor, GraphicsUnit.Pixel); }
                                Medida mh = Mide(h2);
                                double lh;
                                using (Bitmap s = HomeLabel.Small(h2)) HomeLabel.Thumb(s, out lh);
                                if (lh < 0.004) hudViejo++;
                                bool okh = !mh.Liso;
                                total++; if (!okh) fallos++;
                                if (!okh) sb.AppendLine("FALLO  HUD real sobre negro de " + f.Substring(carpeta.Length).TrimStart('\\') + ": " + mh + "; lit " + lh.ToString("0.####", CultureInfo.InvariantCulture));
                            }
                        }
                    }
                    catch (Exception ex) { total++; fallos++; sb.AppendLine("FALLO  real " + f + ": " + ex.Message); }
                }
                sb.AppendLine("       capturas reales: " + reales + " (" + litViejo + " negras para la 1.3.2); trozos de HUD real sobre negro que la 1.3.2 daba por negro: " + hudViejo);
            }
            sb.Insert(0, "Negro liso (aviso de HDCP): " + (total - fallos) + " de " + total + " bien\r\n");
            string txt = sb.ToString();
            if (informe != null) System.IO.File.WriteAllText(informe, txt, new UTF8Encoding(false));
            Console.Write(txt);
            return fallos;
        }

        static void Caso(StringBuilder sb, ref int fallos, ref int total, int w, int h, string que, bool quiere, Action<Graphics> pinta, int ruido = 0)
        {
            using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppRgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Black);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    if (pinta != null) pinta(g);
                }
                if (ruido > 0)
                {
                    BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
                    try
                    {
                        byte[] px = new byte[d.Stride * h];
                        Marshal.Copy(d.Scan0, px, 0, px.Length);
                        Random r = new Random(7);
                        for (int i = 0; i < px.Length; i += 4) { int v = 8 + r.Next(-ruido, ruido + 1); px[i] = px[i + 1] = px[i + 2] = (byte)v; }
                        Marshal.Copy(px, 0, d.Scan0, px.Length);
                    }
                    finally { b.UnlockBits(d); }
                }
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                Medida m = Mide(b);
                double ms = sw.Elapsed.TotalMilliseconds;
                double lit;
                using (Bitmap s = HomeLabel.Small(b)) HomeLabel.Thumb(s, out lit);
                bool ok = m.Liso == quiere;
                total++; if (!ok) fallos++;
                sb.AppendLine((ok ? "bien   " : "FALLO  ") + w + "x" + h + " " + que + ": " + m + ", " + ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms; lit " +
                              lit.ToString("0.####", CultureInfo.InvariantCulture) + (lit < 0.004 && !quiere ? " (la 1.3.2 avisaba en falso)" : ""));
            }
        }
    }
}
