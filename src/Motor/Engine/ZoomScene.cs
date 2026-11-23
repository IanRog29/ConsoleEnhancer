using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ConsoleEnhancer.Motor
{
    // 17-11 12:50 (REJAS, borrosidad): escena de prueba con movimiento NO uniforme sobre textura real (test=zoom). Una imagen 4K de la sala
    // de Ocarina (tests\zoom_oot.png) como "mundo"; cada imagen la ve con un acercamiento que va y viene, un giro pequeño y un paseo:
    // cada punto se mueve distinto (como el suelo y el techo al girar la camara), con fracciones de punto. La verdad a 4K: el mundo
    // muestreado con filtro bilineal; la captura: 2x2 muestras por punto + ruido. La usan la fuente de prueba y tests\rejas_mide
    class ZoomScene
    {
        public const int W4 = 3840, H4 = 2160;
        readonly float[] tex;       // BGR en coma flotante, w x h
        readonly int tw, th;
        public readonly double Speed;

        public ZoomScene(string png, double speed, int kind)
        {
            Speed = speed <= 0 ? 1 : speed; Kind = kind;
            using (Bitmap b0 = new Bitmap(png))
            using (Bitmap b = new Bitmap(b0.Width, b0.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(b)) g.DrawImage(b0, 0, 0, b0.Width, b0.Height);
                tw = b.Width; th = b.Height;
                tex = new float[tw * th * 3];
                BitmapData d = b.LockBits(new Rectangle(0, 0, tw, th), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                byte[] row = new byte[tw * 3];
                for (int y = 0; y < th; y++)
                {
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, tw * 3);
                    for (int i = 0; i < tw * 3; i++) tex[(long)y * tw * 3 + i] = row[i];
                }
                b.UnlockBits(d);
            }
        }

        // Transformacion de la imagen n: punto de pantalla a 4K (X, Y) -> punto del mundo: c + s R(a) d + (tx (1 + Pk dy / H), ty), con
        // d = P - centro. El termino Pk (como un techo o un suelo al girar la camara: lo de abajo corre mas que lo de arriba) y el
        // acercamiento hacen que la velocidad cambie ~1-1,5 px (a la entrada) a lo largo de 128 px, con unos 5 px por imagen como mucho:
        // lo que un promedio de vectores de 16 bloques no puede respetar. Sigue siendo lineal en d: se invierte con un sistema de 2x2.
        // Kind = 1: solo traslacion (para comprobar la verdad de los vectores: ahi el motor tiene que acertar)
        public int Kind;
        double Pk { get { return Kind == 1 ? 0 : 1.2; } }
        public void Params(double n, out double s, out double a, out double tx, out double ty)
        {
            double k = Speed;
            s = Kind == 1 ? 0.80 : 0.80 + 0.04 * Math.Sin(2 * Math.PI * n * k / 120);
            a = Kind == 1 ? 0 : 0.02 * Math.Sin(2 * Math.PI * n * k / 200 + 0.7);
            tx = 160 * Math.Sin(2 * Math.PI * n * k / 160) + 30 * Math.Sin(2 * Math.PI * n * k / 53);
            ty = 50 * Math.Sin(2 * Math.PI * n * k / 210 + 1.1);
        }

        // 23-11 (1.2.4 ronda 6, DLSSSR, prueba): con Kind = 1 (solo traslacion) el punto (X, Y) de la imagen n estaba en (X + sx, Y + sy)
        // (px a 4K) en la imagen nPrev: los vectores de verdad (de la imagen de ahora a la anterior) para mvverdad=8 (EngineRejas.cs ZoomTruth)
        public static void Shift4K(double speed, double n, double nPrev, out double sx, out double sy)
        {
            double k = speed <= 0 ? 1 : speed;
            sx = (Tx(n, k) - Tx(nPrev, k)) / 0.80; sy = (Ty(n, k) - Ty(nPrev, k)) / 0.80;
        }
        static double Tx(double m, double k) { return 160 * Math.Sin(2 * Math.PI * m * k / 160) + 30 * Math.Sin(2 * Math.PI * m * k / 53); }
        static double Ty(double m, double k) { return 50 * Math.Sin(2 * Math.PI * m * k / 210 + 1.1); }

        public void ToWorld(double n, double X, double Y, out double u, out double v)
        {
            double s, a, tx, ty; Params(n, out s, out a, out tx, out ty);
            double dx = X - W4 / 2.0, dy = Y - H4 / 2.0, ca = Math.Cos(a), sa = Math.Sin(a);
            u = tw / 2.0 + s * (ca * dx - sa * dy) + tx * (1 + Pk * dy / H4);
            v = th / 2.0 + s * (sa * dx + ca * dy) + ty;
        }

        public void FromWorld(double n, double u, double v, out double X, out double Y)
        {
            double s, a, tx, ty; Params(n, out s, out a, out tx, out ty);
            double ca = Math.Cos(a), sa = Math.Sin(a);
            // [s ca, -s sa + tx Pk / H] [dx]   [u - cu - tx]
            // [s sa,  s ca            ] [dy] = [v - cv - ty]
            double m00 = s * ca, m01 = -s * sa + tx * Pk / H4, m10 = s * sa, m11 = s * ca;
            double r0 = u - tw / 2.0 - tx, r1 = v - th / 2.0 - ty, det = m00 * m11 - m01 * m10;
            double dx = (r0 * m11 - m01 * r1) / det, dy = (m00 * r1 - m10 * r0) / det;
            X = W4 / 2.0 + dx; Y = H4 / 2.0 + dy;
        }

        // El mundo en (u, v) (centro del punto = +0,5), bilineal: B, G, R
        public void Sample(double u, double v, out float b, out float g, out float r)
        {
            double x = u - 0.5, y = v - 0.5;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = (float)(x - x0), fy = (float)(y - y0);
            if (x0 < 0) { x0 = 0; fx = 0; } if (y0 < 0) { y0 = 0; fy = 0; }
            if (x0 > tw - 2) { x0 = tw - 2; fx = 1; } if (y0 > th - 2) { y0 = th - 2; fy = 1; }
            long p = ((long)y0 * tw + x0) * 3, q = p + (long)tw * 3;
            float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
            b = tex[p] * w00 + tex[p + 3] * w10 + tex[q] * w01 + tex[q + 3] * w11;
            g = tex[p + 1] * w00 + tex[p + 4] * w10 + tex[q + 1] * w01 + tex[q + 4] * w11;
            r = tex[p + 2] * w00 + tex[p + 5] * w10 + tex[q + 2] * w01 + tex[q + 5] * w11;
        }

        // La verdad a 4K en el punto (X, Y) (entero: el centro del punto es X + 0,5)
        public void Pixel4K(double n, int X, int Y, out float b, out float g, out float r)
        {
            double u, v; ToWorld(n, X + 0.5, Y + 0.5, out u, out v);
            Sample(u, v, out b, out g, out r);
        }

        // La imagen n capturada a w x h (BGR24): cada punto, la media de 2x2 muestras de la verdad dentro de su huella + ruido
        public unsafe void Compose(long n, byte* dst, int w, int h, int pitch, int noise)
        {
            double fx = (double)W4 / w, fy = (double)H4 / h;
            long dp = (long)dst;
            // (23-11 DLSSSR: los parametros de la imagen una vez, no en cada muestra -ToWorld- : la misma cuenta, ~10 veces mas rapido)
            double ps, pa, ptx, pty; Params(n, out ps, out pa, out ptx, out pty);
            double pca = Math.Cos(pa), psa = Math.Sin(pa), pk = Pk;
            Parallel.For(0, h, delegate (int y)
            {
                byte* row = (byte*)dp + (long)y * pitch;
                uint rng = (uint)(n * 7919 + y * 104729 + 777) | 1u;
                int span = 2 * noise + 1;
                for (int x = 0; x < w; x++)
                {
                    float sb = 0, sg = 0, sr = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        double X = (x + 0.25 + 0.5 * (k & 1)) * fx, Y = (y + 0.25 + 0.5 * (k >> 1)) * fy;
                        double ddx = X - W4 / 2.0, ddy = Y - H4 / 2.0;
                        double u = tw / 2.0 + ps * (pca * ddx - psa * ddy) + ptx * (1 + pk * ddy / H4);
                        double v = th / 2.0 + ps * (psa * ddx + pca * ddy) + pty;
                        float b, g, r; Sample(u, v, out b, out g, out r);
                        sb += b; sg += g; sr += r;
                    }
                    int vb = (int)Math.Round(sb / 4), vg = (int)Math.Round(sg / 4), vr = (int)Math.Round(sr / 4);
                    if (noise > 0)
                    {
                        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; vb += (int)(rng % (uint)span) - noise;
                        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; vg += (int)(rng % (uint)span) - noise;
                        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; vr += (int)(rng % (uint)span) - noise;
                    }
                    row[x * 3] = (byte)(vb < 0 ? 0 : vb > 255 ? 255 : vb);
                    row[x * 3 + 1] = (byte)(vg < 0 ? 0 : vg > 255 ? 255 : vg);
                    row[x * 3 + 2] = (byte)(vr < 0 ? 0 : vr > 255 ? 255 : vr);
                }
            });
        }
    }
}
