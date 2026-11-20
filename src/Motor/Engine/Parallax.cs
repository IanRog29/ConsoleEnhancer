using System;
using System.Threading.Tasks;

namespace ConsoleEnhancer.Motor
{
    // 17-11 (REJAS): escena de prueba con PARALAJE para medir el rastro y el temblor de una reja con verdad de referencia.
    // Una reja de barrotes oscuros irregulares (ondulados, de grosor variable, con dos travesaños inclinados, como la de la sala de
    // Ocarina) delante de un cielo en degradado con montañas lejanas y un moteado muy suave; cada capa se desplaza a su velocidad
    // (el cielo a 'ratio' veces la de la reja: en una camara que gira alrededor de Link lo lejano corre mas), con velocidades que
    // cambian y se invierten. Se compone a 4K (la imagen "verdadera" de cada instante) y se reduce por area a la captura.
    // Las posiciones son enteros PARES a 4K en las imagenes enteras: las intermedias de la generacion (n + 0,5) caen en enteros.
    // La usan la fuente de prueba (test=paralaje) y el medidor (tests\rejas_mide), con el mismo codigo.
    // Convencion: la capa en pantalla es capa(X + off): un 'off' que crece mueve el contenido a la izquierda; el vector del motor
    // (donde estaba en la anterior - donde esta) es off(n) - off(n-1).
    class Parallax
    {
        public const int W4 = 3840, H4 = 2160;
        public readonly double Ratio, Speed;
        readonly int bgX0, bgY0, bgW, bgH;    // capa del cielo: origen (en coordenadas de capa) y tamaño
        readonly int fgX0, fgY0, fgW, fgH;    // capa de la reja
        public readonly byte[] Bg;            // BGR
        public readonly byte[] FgShade, FgAlpha;

        public Parallax(double ratio, double speed)
        {
            Ratio = ratio; Speed = Math.Max(0, speed);
            double mf = 760 * Speed, mfy = 52 * Speed;
            int mxf = (int)Math.Ceiling(mf) + 64, myf = (int)Math.Ceiling(mfy) + 64;
            int mxb = (int)Math.Ceiling(mf * Math.Max(1, Ratio)) + 64, myb = (int)Math.Ceiling(mfy * Math.Max(1, Ratio)) + 64;
            fgX0 = -mxf; fgY0 = -myf; fgW = W4 + 2 * mxf; fgH = H4 + 2 * myf;
            bgX0 = -mxb; bgY0 = -myb; bgW = W4 + 2 * mxb; bgH = H4 + 2 * myb;
            Bg = new byte[(long)bgW * bgH * 3];
            FgShade = new byte[(long)fgW * fgH];
            FgAlpha = new byte[(long)fgW * fgH];
            BuildBg();
            BuildFg();
        }

        // ---------------------------------------------------------------- movimiento
        static double PathX(double n) { return 600 * Math.Sin(2 * Math.PI * n / 360) + 150 * Math.Sin(2 * Math.PI * n / 130); }
        static double PathY(double n) { return 40 * Math.Sin(2 * Math.PI * n / 200) + 10 * Math.Sin(2 * Math.PI * n / 70); }
        static int Even(double v) { return Fine ? (int)Math.Round(v) : 2 * (int)Math.Round(v / 2); }
        // test_fino=1: posiciones enteras a 4K (no pares): movimientos de 1/2 o 1/3 de punto a la entrada (las intermedias de la
        // generacion ya no caen en enteros)
        public static bool Fine;

        // Posiciones de la imagen n (entera o n + 0,5) en px a 4K: reja (fx, fy) y cielo (bx, by)
        public void Offsets(double n, out int fx, out int fy, out int bx, out int by)
        {
            double fl = Math.Floor(n);
            if (n - fl < 1e-9) { OffsetsI((long)fl, out fx, out fy, out bx, out by); return; }
            int ax, ay, cx, cy, dx, dy, ex, ey;
            OffsetsI((long)fl, out ax, out ay, out cx, out cy);
            OffsetsI((long)fl + 1, out dx, out dy, out ex, out ey);
            double t = n - fl;
            fx = (int)Math.Round(ax + (dx - ax) * t); fy = (int)Math.Round(ay + (dy - ay) * t);
            bx = (int)Math.Round(cx + (ex - cx) * t); by = (int)Math.Round(cy + (ey - cy) * t);
        }

        void OffsetsI(long n, out int fx, out int fy, out int bx, out int by) { OffsetsOf(Ratio, Speed, n, out fx, out fy, out bx, out by); }

        // Sin construir las capas (el motor, para los vectores de la verdad: mvverdad)
        public static void OffsetsOf(double ratio, double speed, long n, out int fx, out int fy, out int bx, out int by)
        {
            double px = PathX(n) * speed, py = PathY(n) * speed;
            fx = Even(px); fy = Even(py);
            bx = Even(px * ratio); by = Even(py * ratio);
        }

        // ---------------------------------------------------------------- capas
        static double Hash(int k)
        {
            uint h = (uint)k * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            return h / 4294967295.0 * 2 - 1;
        }
        static double Hash2(int x, int y) { return Hash(x * 73856093 ^ y * 19349663); }
        static double ValueNoise(double x, double y)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            double fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            double a = Hash2(ix, iy), b = Hash2(ix + 1, iy), c = Hash2(ix, iy + 1), d = Hash2(ix + 1, iy + 1);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        void BuildBg()
        {
            Parallel.For(0, bgH, delegate (int j)
            {
                double v = bgY0 + j;
                double t = Math.Max(0, Math.Min(1, (v + 100) / 2400.0));
                t = Math.Pow(t, 0.9);
                double r0 = 22 + (96 - 22) * t, g0 = 50 + (150 - 50) * t, b0 = 72 + (174 - 72) * t;
                long row = (long)j * bgW * 3;
                for (int i = 0; i < bgW; i++)
                {
                    double u = bgX0 + i;
                    double hm = 1450 + 110 * Math.Sin(u / 610) + 55 * Math.Sin(u / 233 + 1.3) + 22 * Math.Sin(u / 81 + 2.1) + 8 * Math.Sin(u / 29 + 0.4);
                    double m = Math.Max(0, Math.Min(1, v - hm + 0.5));
                    double cl = 1.5 * ValueNoise(u / 90.0, v / 90.0);
                    double tex = m > 0 ? 3 * ValueNoise(u / 40.0 + 100, v / 40.0) : 0;
                    double r = r0 + cl - m * (16 - tex), g = g0 + cl - m * (12 - tex), b = b0 + cl - m * (7 - tex);
                    Bg[row + i * 3 + 0] = Clamp8(b); Bg[row + i * 3 + 1] = Clamp8(g); Bg[row + i * 3 + 2] = Clamp8(r);
                }
            });
        }

        const double Spacing = 110;   // (la de Ocarina a 4K: barrotes de ~28 px cada ~105 px)
        void BuildFg()
        {
            Parallel.For(0, fgH, delegate (int j)
            {
                double v = fgY0 + j;
                long row = (long)j * fgW;
                for (int i = 0; i < fgW; i++)
                {
                    double u = fgX0 + i;
                    double a = 0, shade = 16;
                    // barrotes verticales (los 3 mas cercanos)
                    int k0 = (int)Math.Floor(u / Spacing);
                    for (int k = k0 - 1; k <= k0 + 1; k++)
                    {
                        double uk = k * Spacing + 14 * Hash(k * 3 + 1);
                        double wk = 29 + 4 * Math.Sin(v / 170 + 3.1 * k) + 2 * Hash(k * 3 + 2);
                        double xc = uk + 5 * Math.Sin(v / 240 + 1.7 * k) + 2.5 * Math.Sin(v / 63 + 0.9 * k) + 1.2 * Math.Sin(v / 19 + 2.3 * k);
                        double ak = Math.Max(0, Math.Min(1, wk / 2 - Math.Abs(u - xc) + 0.5));
                        if (ak > a) { a = ak; shade = 15 + 4 * Hash(k * 3 + 3) + 3 * Math.Sin(v / 300 + k); }
                    }
                    // travesaños inclinados y vigas de arriba y abajo
                    double c1 = 620 + 0.035 * u, c2 = 1420 + 0.02 * u;
                    double a1 = Math.Max(0, Math.Min(1, 13 - Math.Abs(v - c1) + 0.5)), a2 = Math.Max(0, Math.Min(1, 11 - Math.Abs(v - c2) + 0.5));
                    double a3 = Math.Max(0, Math.Min(1, (120 - v) + 0.5)), a4 = Math.Max(0, Math.Min(1, (v - 2060) + 0.5));
                    double ab = Math.Max(Math.Max(a1, a2), Math.Max(a3, a4));
                    if (ab > a) { a = ab; shade = 13 + 2 * Math.Sin(u / 200); }
                    FgAlpha[row + i] = Clamp8(a * 255);
                    FgShade[row + i] = Clamp8(shade);
                }
            });
        }

        static byte Clamp8(double v) { return (byte)(v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5)); }

        // ---------------------------------------------------------------- composicion
        // Un punto a 4K (X, Y en pantalla) con las posiciones dadas: B, G, R (0-255, sin redondear) y el alfa de la reja (0-1)
        public void Pixel4K(int X, int Y, int fx, int fy, int bx, int by, out double b, out double g, out double r, out double a)
        {
            int bi = Math.Max(0, Math.Min(bgW - 1, X + bx - bgX0)), bj = Math.Max(0, Math.Min(bgH - 1, Y + by - bgY0));
            int fi = Math.Max(0, Math.Min(fgW - 1, X + fx - fgX0)), fj = Math.Max(0, Math.Min(fgH - 1, Y + fy - fgY0));
            long pb = ((long)bj * bgW + bi) * 3, pf = (long)fj * fgW + fi;
            a = FgAlpha[pf] / 255.0;
            double s = FgShade[pf];
            b = Bg[pb] * (1 - a) + (s + 7) * a;
            g = Bg[pb + 1] * (1 - a) + (s + 3) * a;
            r = Bg[pb + 2] * (1 - a) + s * a;
        }

        // Alfa de la reja en un punto de su capa (coordenadas de capa: pantalla + off)
        public double AlphaLayer(int u, int v)
        {
            int fi = Math.Max(0, Math.Min(fgW - 1, u - fgX0)), fj = Math.Max(0, Math.Min(fgH - 1, v - fgY0));
            return FgAlpha[(long)fj * fgW + fi] / 255.0;
        }

        // Pesos de la reduccion por area de 4K a 'n' puntos (por eje): para cada punto de salida, hasta 3 de entrada
        static void AreaWeights(int n, int n4, out int[] first, out double[] w)
        {
            first = new int[n]; w = new double[n * 3];
            double f = (double)n4 / n;
            for (int i = 0; i < n; i++)
            {
                double a = i * f, b = (i + 1) * f;
                int j0 = (int)Math.Floor(a);
                first[i] = j0;
                for (int k = 0; k < 3; k++)
                {
                    double lo = Math.Max(a, j0 + k), hi = Math.Min(b, j0 + k + 1);
                    w[i * 3 + k] = hi > lo ? (hi - lo) / f : 0;
                }
            }
        }
        int[] axFirst, ayFirst; double[] axW, ayW; int aW, aH;

        // Una fila a 4K compuesta (B, G, R por punto, x255 para no dividir) en 'o' (3 x W4 enteros)
        unsafe void Row4K(int Y, int fx, int fy, int bx, int by, int* o)
        {
            int bj = Math.Max(0, Math.Min(bgH - 1, Y + by - bgY0)), fj = Math.Max(0, Math.Min(fgH - 1, Y + fy - fgY0));
            int bi0 = bx - bgX0, fi0 = fx - fgX0;   // con los margenes de las capas siempre dentro
            fixed (byte* bgp = Bg, fsp = FgShade, fap = FgAlpha)
            {
                byte* pb = bgp + ((long)bj * bgW + bi0) * 3;
                byte* ps = fsp + (long)fj * fgW + fi0, pa = fap + (long)fj * fgW + fi0;
                for (int X = 0; X < W4; X++)
                {
                    int a = pa[X], ia = 255 - a, s = ps[X];
                    o[X * 3] = pb[X * 3] * ia + (s + 7) * a;
                    o[X * 3 + 1] = pb[X * 3 + 1] * ia + (s + 3) * a;
                    o[X * 3 + 2] = pb[X * 3 + 2] * ia + s * a;
                }
            }
        }

        // Numero de la imagen en codigo Gray (16 cuadros abajo a la izquierda, quietos en pantalla): en las copias de la tele se sabe
        // que imagen es y, en las de la generacion, que es la intermedia (un cuadro a medias). Coordenadas a 4K
        public const int CodeX = 128, CodeY = 2080, CodeSize = 64, CodeBits = 16;
        public static uint Gray(long n) { uint v = (uint)n; return v ^ (v >> 1); }
        public static long FromGray(uint g) { uint v = g; for (uint s = g >> 1; s != 0; s >>= 1) v ^= s; return v; }

        unsafe void DrawCode(long n, byte* dst, int w, int h, int pitch)
        {
            uint g = Gray(n);
            double k = (double)w / W4;
            int y0 = (int)Math.Round(CodeY * k), y1 = Math.Min(h, (int)Math.Round((CodeY + CodeSize) * k));
            for (int b = 0; b < CodeBits; b++)
            {
                int x0 = (int)Math.Round((CodeX + b * CodeSize) * k), x1 = (int)Math.Round((CodeX + (b + 1) * CodeSize) * k);
                byte v = ((g >> b) & 1) != 0 ? (byte)230 : (byte)20;
                for (int y = y0; y < y1; y++)
                {
                    byte* row = dst + (long)y * pitch;
                    for (int x = x0 + 1; x < x1 - 1; x++) { row[x * 3] = v; row[x * 3 + 1] = v; row[x * 3 + 2] = v; }
                }
            }
        }

        // 20-11 (FGFALLOS) test_hud=1 con test=paralaje: interfaz QUIETA encima (texto blanco con borde oscuro, un panel oscuro
        // semitransparente con marco blanco y una cruz), para medir si la generacion (DLSS-G) la arrastra con el fondo que se mueve.
        // Geometria a 4K; a otra captura se muestrea en el centro de cada punto. La usa tambien la herramienta de medida (fgmide)
        public static bool Hud;
        public static void HudAt(int X, int Y, out double a, out double v)
        {
            a = 0; v = 0;
            // tres lineas de "letras" (cajas de 18x34 cada 26 px, con huecos de palabra) con borde oscuro de 3 px
            if (X >= 1317 && X < 1843 && Y >= 797 && Y < 947)
            {
                int line = (Y - 797) / 50, yl = (Y - 797) % 50;
                if (yl < 40)
                {
                    int g = (X - 1317) / 26, xl = (X - 1317) % 26;
                    bool gap = (g * 7 + line * 3) % 9 == 4 || g >= 20 - line * 3;
                    if (!gap && xl < 24)
                    {
                        bool core = xl >= 3 && xl < 21 && yl >= 3 && yl < 37;
                        a = 1; v = core ? 235 : 18;
                        return;
                    }
                }
            }
            // panel (minimapa): marco blanco de 4 px y dentro negro al 55 %, con una flecha blanca quieta
            if (X >= 2150 && X < 2500 && Y >= 1050 && Y < 1350)
            {
                if (X < 2154 || X >= 2496 || Y < 1054 || Y >= 1346) { a = 1; v = 240; return; }
                int dx = X - 2325, dy = Y - 1200;
                if (dy >= -24 && dy <= 24 && Math.Abs(dx) <= (dy + 24) / 2) { a = 1; v = 230; return; }
                a = 0.55; v = 8; return;
            }
            // cruz del centro (2 px)
            if ((Math.Abs(X - 1920) <= 1 && Math.Abs(Y - 1080) <= 22 && Math.Abs(Y - 1080) >= 6) || (Math.Abs(Y - 1080) <= 1 && Math.Abs(X - 1920) <= 22 && Math.Abs(X - 1920) >= 6)) { a = 1; v = 225; return; }
        }

        unsafe void DrawHud(byte* dst, int w, int h, int pitch)
        {
            double k = (double)W4 / w, kh = (double)H4 / h;
            int x0 = (int)(1300 / k), x1 = Math.Min(w, (int)(2520 / k) + 2), y0 = (int)(780 / kh), y1 = Math.Min(h, (int)(1360 / kh) + 2);
            for (int y = y0; y < y1; y++)
            {
                byte* row = dst + (long)y * pitch;
                int Y = (int)((y + 0.5) * kh);
                for (int x = x0; x < x1; x++)
                {
                    double a, v; HudAt((int)((x + 0.5) * k), Y, out a, out v);
                    if (a <= 0) continue;
                    for (int c = 0; c < 3; c++) row[x * 3 + c] = Clamp8(row[x * 3 + c] * (1 - a) + v * a);
                }
            }
        }

        // La imagen n (capturada) en BGR24 de w x h con 'pitch' bytes por fila: 4K reducida por area + ruido de +-noise niveles
        public unsafe void Compose(long n, byte* dst, int w, int h, int pitch, int noise)
        {
            ComposeLayers(n, dst, w, h, pitch, noise);
            if (Hud) DrawHud(dst, w, h, pitch);   // 20-11 (FGFALLOS)
            DrawCode(n, dst, w, h, pitch);
        }

        unsafe void ComposeLayers(long n, byte* dst, int w, int h, int pitch, int noise)
        {
            if (aW != w || aH != h) { AreaWeights(w, W4, out axFirst, out axW); AreaWeights(h, H4, out ayFirst, out ayW); aW = w; aH = h; }
            int fx, fy, bx, by;
            Offsets(n, out fx, out fy, out bx, out by);
            long dp = (long)dst;
            int nb = Math.Max(1, Environment.ProcessorCount - 2), rowsPer = (h + nb - 1) / nb;
            if (w * 2 == W4 && h * 2 == H4)
            {
                // 1080p: cada punto, la media de 2x2 a 4K (todo en enteros)
                Parallel.For(0, nb, delegate (int part)
                {
                    fixed (byte* bgp = Bg, fsp = FgShade, fap = FgAlpha)
                        for (int y = part * rowsPer; y < Math.Min(h, (part + 1) * rowsPer); y++)
                        {
                            byte* row = (byte*)dp + (long)y * pitch;
                            uint rng = (uint)(n * 7919 + y * 104729 + 12345) | 1u;
                            int span = 2 * noise + 1;
                            byte* pb0 = bgp + ((long)Math.Max(0, Math.Min(bgH - 1, 2 * y + by - bgY0)) * bgW + bx - bgX0) * 3;
                            byte* pb1 = bgp + ((long)Math.Max(0, Math.Min(bgH - 1, 2 * y + 1 + by - bgY0)) * bgW + bx - bgX0) * 3;
                            long f0 = (long)Math.Max(0, Math.Min(fgH - 1, 2 * y + fy - fgY0)) * fgW + fx - fgX0;
                            long f1 = (long)Math.Max(0, Math.Min(fgH - 1, 2 * y + 1 + fy - fgY0)) * fgW + fx - fgX0;
                            byte* pa0 = fap + f0, pa1 = fap + f1, ps0 = fsp + f0, ps1 = fsp + f1;
                            for (int x = 0; x < w; x++)
                            {
                                int X = 2 * x;
                                int a00 = pa0[X], a01 = pa0[X + 1], a10 = pa1[X], a11 = pa1[X + 1];
                                int sb, sg, sr;
                                if ((a00 | a01 | a10 | a11) == 0)
                                {
                                    int X3 = X * 3;
                                    sb = (pb0[X3] + pb0[X3 + 3] + pb1[X3] + pb1[X3 + 3]) * 255;
                                    sg = (pb0[X3 + 1] + pb0[X3 + 4] + pb1[X3 + 1] + pb1[X3 + 4]) * 255;
                                    sr = (pb0[X3 + 2] + pb0[X3 + 5] + pb1[X3 + 2] + pb1[X3 + 5]) * 255;
                                }
                                else
                                {
                                    sb = sg = sr = 0;
                                    for (int q = 0; q < 4; q++)
                                    {
                                        int Xq = X + (q & 1);
                                        byte* pb = (q < 2 ? pb0 : pb1) + Xq * 3;
                                        int a = q == 0 ? a00 : q == 1 ? a01 : q == 2 ? a10 : a11, ia = 255 - a;
                                        int s = (q < 2 ? ps0 : ps1)[Xq];
                                        sb += pb[0] * ia + (s + 7) * a; sg += pb[1] * ia + (s + 3) * a; sr += pb[2] * ia + s * a;
                                    }
                                }
                                int vb = (sb + 510) / 1020, vg = (sg + 510) / 1020, vr = (sr + 510) / 1020;
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
                        }
                });
                return;
            }
            if (w == W4 && h == H4)
            {
                // 20-11 (FGFALLOS): a 4K, cada punto es el suyo (sin reducir): 43 -> pocos ms por imagen (el modo 30 a 4K no llegaba a 30)
                Parallel.For(0, nb * 2, delegate (int part)
                {
                    int rp2 = (h + nb * 2 - 1) / (nb * 2);
                    fixed (byte* bgp = Bg, fsp = FgShade, fap = FgAlpha)
                        for (int y = part * rp2; y < Math.Min(h, (part + 1) * rp2); y++)
                        {
                            byte* row = (byte*)dp + (long)y * pitch;
                            int bj = Math.Max(0, Math.Min(bgH - 1, y + by - bgY0)), fj = Math.Max(0, Math.Min(fgH - 1, y + fy - fgY0));
                            byte* pb = bgp + ((long)bj * bgW + bx - bgX0) * 3;
                            byte* ps = fsp + (long)fj * fgW + fx - fgX0, pa = fap + (long)fj * fgW + fx - fgX0;
                            if (noise == 0)
                            {
                                for (int X = 0; X < W4; X++)
                                {
                                    int a = pa[X], X3 = X * 3;
                                    if (a == 0) { row[X3] = pb[X3]; row[X3 + 1] = pb[X3 + 1]; row[X3 + 2] = pb[X3 + 2]; continue; }
                                    int ia = 255 - a, s = ps[X];
                                    row[X3] = (byte)((pb[X3] * ia + (s + 7) * a + 127) / 255);
                                    row[X3 + 1] = (byte)((pb[X3 + 1] * ia + (s + 3) * a + 127) / 255);
                                    row[X3 + 2] = (byte)((pb[X3 + 2] * ia + s * a + 127) / 255);
                                }
                                continue;
                            }
                            uint rng = (uint)(n * 7919 + y * 104729 + 12345) | 1u;
                            int span = 2 * noise + 1;
                            for (int X = 0; X < W4; X++)
                            {
                                int a = pa[X], ia = 255 - a, s = ps[X], X3 = X * 3;
                                int vb = (pb[X3] * ia + (s + 7) * a + 127) / 255, vg = (pb[X3 + 1] * ia + (s + 3) * a + 127) / 255, vr = (pb[X3 + 2] * ia + s * a + 127) / 255;
                                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; vb += (int)(rng % (uint)span) - noise;
                                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; vg += (int)(rng % (uint)span) - noise;
                                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; vr += (int)(rng % (uint)span) - noise;
                                row[X3] = (byte)(vb < 0 ? 0 : vb > 255 ? 255 : vb); row[X3 + 1] = (byte)(vg < 0 ? 0 : vg > 255 ? 255 : vg); row[X3 + 2] = (byte)(vr < 0 ? 0 : vr > 255 ? 255 : vr);
                            }
                        }
                });
                return;
            }
            Parallel.For(0, nb, delegate (int part)
            {
                int* r4 = stackalloc int[W4 * 3];
                double* acc = stackalloc double[w * 3];
                for (int y = part * rowsPer; y < Math.Min(h, (part + 1) * rowsPer); y++)
                {
                    for (int i = 0; i < w * 3; i++) acc[i] = 0;
                    for (int ky = 0; ky < 3; ky++)
                    {
                        double wy = ayW[y * 3 + ky];
                        if (wy <= 0) continue;
                        int Y = Math.Min(H4 - 1, ayFirst[y] + ky);
                        Row4K(Y, fx, fy, bx, by, r4);
                        for (int x = 0; x < w; x++)
                        {
                            double sb = 0, sg = 0, sr = 0;
                            int X0 = axFirst[x];
                            for (int kx = 0; kx < 3; kx++)
                            {
                                double wx = axW[x * 3 + kx];
                                if (wx <= 0) continue;
                                int X = Math.Min(W4 - 1, X0 + kx) * 3;
                                sb += r4[X] * wx; sg += r4[X + 1] * wx; sr += r4[X + 2] * wx;
                            }
                            acc[x * 3] += sb * wy; acc[x * 3 + 1] += sg * wy; acc[x * 3 + 2] += sr * wy;
                        }
                    }
                    byte* row = (byte*)dp + (long)y * pitch;
                    uint rng = (uint)(n * 7919 + y * 104729 + 12345) | 1u;
                    int span = 2 * noise + 1;
                    for (int x = 0; x < w; x++)
                    {
                        double sb = acc[x * 3] / 255.0, sg = acc[x * 3 + 1] / 255.0, sr = acc[x * 3 + 2] / 255.0;
                        if (noise > 0)
                        {
                            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; sb += (int)(rng % (uint)span) - noise;
                            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; sg += (int)(rng % (uint)span) - noise;
                            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; sr += (int)(rng % (uint)span) - noise;
                        }
                        row[x * 3] = Clamp8(sb); row[x * 3 + 1] = Clamp8(sg); row[x * 3 + 2] = Clamp8(sr);
                    }
                }
            });
        }
    }
}
