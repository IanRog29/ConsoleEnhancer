using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ConsoleEnhancer.Motor
{
    // 22-11 (1.2.4, 5a ronda, DLSS5Q) test=escenas:<carpeta | lista.txt>: capturas REALES de la consola (PNG de cualquier tamaño) una tras
    // otra, como un juego, para probar DLSS 5 (cambios de color, reinicios, relevos) sin la consola: cada escena dura test_escena_s (5 s)
    // con la camara en movimiento (paneo que acelera y frena, acercamiento lento), corte seco a la siguiente, un fundido a negro (y vuelta)
    // cada 3 escenas y un destello corto (una explosion) a mitad de cada escena. Lo que se ve son imagenes de verdad (Rocket League, GTA VI,
    // Bloodborne... de los informes anteriores); el movimiento es sintetico. Con test_ruido (el ruido de la capturadora).
    // La lista: una ruta por linea (# comentario) o todos los PNG de la carpeta por orden de nombre.
    class EscenasReales
    {
        public static double SceneSecs = 5;   // test_escena_s
        public static bool Still;             // test_escena_quieta=1: la imagen entera, quieta (solo el ruido): para comparar con Magpie
        readonly List<byte[]> imgs = new List<byte[]>();   // BGR24 a sw x sh (la captura + margen para el paneo)
        readonly List<string> names = new List<string>();
        readonly int w, h, sw, sh, gameFps;
        const double Margin = 1.18;

        public int Count { get { return imgs.Count; } }
        public string Names { get { return string.Join(", ", names.ToArray()); } }

        public EscenasReales(string spec, int w, int h, int gameFps)
        {
            this.w = w; this.h = h; this.gameFps = Math.Max(1, gameFps);
            sw = ((int)Math.Ceiling(w * Margin) + 1) & ~1; sh = ((int)Math.Ceiling(h * Margin) + 1) & ~1;
            List<string> files = new List<string>();
            if (Directory.Exists(spec))
            {
                string[] f = Directory.GetFiles(spec, "*.png");
                Array.Sort(f, StringComparer.OrdinalIgnoreCase);
                files.AddRange(f);
            }
            else if (File.Exists(spec))
            {
                string baseDir = Path.GetDirectoryName(Path.GetFullPath(spec));
                foreach (string raw in File.ReadAllLines(spec))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l[0] == '#') continue;
                    files.Add(Path.IsPathRooted(l) ? l : Path.Combine(baseDir, l));
                }
            }
            foreach (string f in files)
            {
                if (!File.Exists(f)) { Log.Write("Escenas reales: no esta " + f); continue; }
                try { imgs.Add(Load(f)); names.Add(Path.GetFileNameWithoutExtension(f)); }
                catch (Exception ex) { Log.Write("Escenas reales: " + f + ": " + ex.Message); }
                if (imgs.Count >= 14) break;   // (memoria: ~36 MB por escena a 4K)
            }
        }

        byte[] Load(string f)
        {
            byte[] o = new byte[(long)sw * sh * 3];
            using (Image im = Image.FromFile(f))
            using (Bitmap b = new Bitmap(sw, sh, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        ia.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(im, new Rectangle(0, 0, sw, sh), 0, 0, im.Width, im.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                BitmapData d = b.LockBits(new Rectangle(0, 0, sw, sh), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try { for (int y = 0; y < sh; y++) Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), o, y * sw * 3, sw * 3); }
                finally { b.UnlockBits(d); }
            }
            return o;
        }

        // Lo que pasa en la imagen n del juego: escena, posicion de la camara y brillo (fundido / destello)
        public void State(long n, out int scene, out double cx, out double cy, out double zoom, out double gain)
        {
            int per = Math.Max(30, (int)Math.Round(SceneSecs * gameFps));
            long k = n / per; int f = (int)(n % per);
            scene = (int)(k % Math.Max(1, imgs.Count));
            double t = f / (double)gameFps, ph = k * 1.7;
            if (Still) { zoom = sw / (double)w; cx = sw / 2.0; cy = sh / 2.0; gain = 1; return; }
            // acercamiento lento: se ve del 86 al 100 % del ancho guardado / margen
            zoom = 0.93 + 0.07 * Math.Sin(2 * Math.PI * t / 7.0 + ph);
            double vw = w * zoom, vh = h * zoom;
            double ax = (sw - vw) / 2 - 2, ay = (sh - vh) / 2 - 2;
            // paneo que acelera y frena (como girar la camara con el stick) y un poco en vertical
            double px = 0.75 * Math.Sin(2 * Math.PI * t / 4.3 + ph) + 0.25 * Math.Sin(2 * Math.PI * t / 1.9 + 2 * ph);
            double py = 0.6 * Math.Sin(2 * Math.PI * t / 5.1 + 0.5 * ph);
            cx = sw / 2.0 + ax * px; cy = sh / 2.0 + ay * py;
            gain = 1;
            // destello a mitad de escena (4 imagenes: x1,55 x1,35 x1,18 x1,06)
            int mid = per / 2;
            if (f >= mid && f < mid + 4) gain = new[] { 1.55, 1.35, 1.18, 1.06 }[f - mid];
            // fundido a negro al final de cada 3a escena y vuelta al empezar la siguiente (15 imagenes cada uno)
            int fade = Math.Max(4, gameFps / 2);
            if (k % 3 == 2 && f >= per - fade) gain *= (per - 1 - f) / (double)fade;
            if (k % 3 == 0 && k > 0 && f < fade) gain *= (f + 1) / (double)fade;
        }

        public unsafe void Compose(long n, byte* dst, int dw, int dh, int pitch, int noise)
        {
            if (imgs.Count == 0) return;
            int scene; double cx, cy, zoom, gain;
            State(n, out scene, out cx, out cy, out zoom, out gain);
            byte[] img = imgs[scene];
            double sx = zoom, x0 = cx - dw * zoom / 2.0, y0 = cy - dh * zoom / 2.0;
            int[] xi = new int[dw]; int[] xf = new int[dw];
            for (int x = 0; x < dw; x++)
            {
                double u = x0 + (x + 0.5) * sx - 0.5;
                int i = (int)Math.Floor(u); double fr = u - i;
                if (i < 0) { i = 0; fr = 0; } if (i > sw - 2) { i = sw - 2; fr = 1; }
                xi[x] = i * 3; xf[x] = (int)(fr * 256);
            }
            int g256 = (int)Math.Round(gain * 256);
            uint seed = (uint)(n * 2654435761u + 12345u);
            IntPtr dp = (IntPtr)dst;
            Parallel.For(0, dh, delegate (int y)
            {
                double v = y0 + (y + 0.5) * sx - 0.5;
                int j = (int)Math.Floor(v); double fv = v - j;
                if (j < 0) { j = 0; fv = 0; } if (j > sh - 2) { j = sh - 2; fv = 1; }
                int wy = (int)(fv * 256);
                uint rng = seed ^ (uint)(y * 0x9E3779B9u) | 1u;
                fixed (byte* s = img)
                {
                    byte* r0 = s + (long)j * sw * 3, r1 = r0 + sw * 3;
                    byte* drow = (byte*)dp + (long)y * pitch;
                    for (int x = 0; x < dw; x++)
                    {
                        int o = xi[x], wx = xf[x];
                        for (int c = 0; c < 3; c++)
                        {
                            int a = r0[o + c] * (256 - wx) + r0[o + 3 + c] * wx;
                            int b = r1[o + c] * (256 - wx) + r1[o + 3 + c] * wx;
                            int val = (a * (256 - wy) + b * wy) >> 16;
                            val = (val * g256) >> 8;
                            if (noise > 0) { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; val += (int)(rng % (uint)(2 * noise + 1)) - noise; }
                            drow[x * 3 + c] = (byte)(val < 0 ? 0 : val > 255 ? 255 : val);
                        }
                    }
                }
            });
        }
    }
}
