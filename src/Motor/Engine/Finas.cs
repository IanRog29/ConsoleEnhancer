using System;
using System.Threading.Tasks;

namespace ConsoleEnhancer.Motor
{
    // 22-11 (1.2.2, CALIDAD; el usuario: "arregla fallos de barrotes y cosas muy finas con frame generation" y "XeSS FG tiene problemas
    // visuales en la cabeza de los personajes al girar la camara"). Escenas de prueba con verdad para medir las imagenes que inventa la
    // generacion contra la intermedia ideal (test=finas / test=cabeza en el motor; para mpv y Magpie, el mismo codigo escribe un video):
    //  finas:  cosas finas delante de un cielo con montañas: cables (1,6-2,6 px a 4K), una verja de barrotes de 2-4 px cada 10-22 px con
    //          barandillas, una malla en diagonal y lamas horizontales cada 13 px, y hierba abajo. La capa fina va a test_vel y el cielo a
    //          test_par veces esa velocidad (de serie 0,5: lo lejano corre menos); test_dir=h (paneo horizontal), v (vertical) o hv
    //  cabeza: un personaje QUIETO en el centro (pelo con mechones, orejas, cara, cuello y hombros) mientras la camara gira a su alrededor:
    //          el fondo (una ciudad con ventanas, farolas y cables: detalle fino que se repite) corre detras. test_hud=1: interfaz quieta.
    // Como el paralaje (Parallax.cs): compuesta a 4K y reducida por area a la captura; posiciones enteras PARES a 4K en las imagenes
    // enteras (las intermedias n + 0,5 de la generacion x2 caen en enteros); el codigo Gray de la imagen abajo a la izquierda.
    // Convencion: la capa en pantalla es capa(X + off).
    class Finas
    {
        public const int W4 = 3840, H4 = 2160;
        public readonly int Kind;          // 0 finas, 1 cabeza
        public readonly double Ratio, Speed;
        public readonly int Dir;           // 1 horizontal, 2 vertical, 3 los dos
        public static bool Hud;            // interfaz quieta encima (test_hud=1)
        readonly int bgX0, bgY0, bgW, bgH, fgX0, fgY0, fgW, fgH;
        readonly byte[] Bg;                // BGR del fondo
        readonly byte[] FgA, FgB, FgG, FgR; // la capa fina (alfa y color); vacia en la escena de la cabeza
        readonly byte[] StA, StB, StG, StR, StK;   // lo quieto a 4K en pantalla (personaje 1, interfaz 2): alfa, color, tipo

        public static int KindOf(string name) { return name == "cabeza" ? 1 : name == "finas" ? 0 : -1; }

        public Finas(int kind, double ratio, double speed, int dir)
        {
            Kind = kind; Speed = Math.Max(0, speed); Dir = dir < 1 || dir > 3 ? 1 : dir;
            Ratio = kind == 1 ? 1 : ratio;
            double ax, ay; Amplitudes(out ax, out ay);
            int mxf = (int)Math.Ceiling(ax) + 64, myf = (int)Math.Ceiling(ay) + 64;
            int mxb = (int)Math.Ceiling(ax * Math.Max(1, Ratio)) + 64, myb = (int)Math.Ceiling(ay * Math.Max(1, Ratio)) + 64;
            if (kind == 1) { mxf = 0; myf = 0; }
            fgX0 = -mxf; fgY0 = -myf; fgW = kind == 1 ? 1 : W4 + 2 * mxf; fgH = kind == 1 ? 1 : H4 + 2 * myf;
            bgX0 = -mxb; bgY0 = -myb; bgW = W4 + 2 * mxb; bgH = H4 + 2 * myb;
            Bg = new byte[(long)bgW * bgH * 3];
            FgA = new byte[(long)fgW * fgH]; FgB = new byte[FgA.Length]; FgG = new byte[FgA.Length]; FgR = new byte[FgA.Length];
            StA = new byte[W4 * H4]; StB = new byte[W4 * H4]; StG = new byte[W4 * H4]; StR = new byte[W4 * H4]; StK = new byte[W4 * H4];
            if (kind == 1) { BuildCity(); BuildPerson(); }
            else { BuildSky(); BuildThin(); }
            if (Hud) BuildHud();
        }

        // ---------------------------------------------------------------- movimiento
        void Amplitudes(out double ax, out double ay)
        {
            if (Kind == 1) { ax = (1000 + 220) * Speed; ay = 60 * Speed; return; }
            ax = (Dir & 1) != 0 ? (700 + 160) * Speed : 0;
            ay = (Dir & 2) != 0 ? (150 + 40) * Speed : 0;
        }
        static int Even(double v) { return 2 * (int)Math.Round(v / 2); }

        // Posiciones de la imagen n (entera o con fraccion) en px a 4K: capa fina (fx, fy) y fondo (bx, by)
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

        void OffsetsI(long n, out int fx, out int fy, out int bx, out int by)
        {
            if (Kind == 1)
            {
                // la camara gira alrededor del personaje: el fondo corre (hasta ~33 px por imagen a 4K con test_vel=1) y sube y baja poco
                double px = Speed * (1000 * Math.Sin(2 * Math.PI * n / 300) + 220 * Math.Sin(2 * Math.PI * n / 110));
                double py = Speed * 60 * Math.Sin(2 * Math.PI * n / 240);
                fx = 0; fy = 0; bx = Even(px); by = Even(py);
                return;
            }
            // hasta ~29 px por imagen a 4K en horizontal y ~7 en vertical con test_vel=1
            double qx = (Dir & 1) != 0 ? Speed * (700 * Math.Sin(2 * Math.PI * n / 240) + 160 * Math.Sin(2 * Math.PI * n / 90)) : 0;
            double qy = (Dir & 2) != 0 ? Speed * (150 * Math.Sin(2 * Math.PI * n / 200 + 0.6) + 40 * Math.Sin(2 * Math.PI * n / 64)) : 0;
            fx = Even(qx); fy = Even(qy); bx = Even(qx * Ratio); by = Even(qy * Ratio);
        }

        // ---------------------------------------------------------------- utilidades
        static double Hash(int k)
        {
            uint h = (uint)k * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            return h / 4294967295.0 * 2 - 1;
        }
        static double Hash2(int x, int y) { return Hash(x * 73856093 ^ y * 19349663); }
        static double Noise(double x, double y)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            double fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            double a = Hash2(ix, iy), b = Hash2(ix + 1, iy), c = Hash2(ix, iy + 1), d = Hash2(ix + 1, iy + 1);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }
        static byte C8(double v) { return (byte)(v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5)); }
        static double Cov(double d) { return Math.Max(0, Math.Min(1, d + 0.5)); }   // cobertura de un borde a distancia firmada d (px)

        // ---------------------------------------------------------------- escena finas
        void BuildSky()
        {
            Parallel.For(0, bgH, delegate (int j)
            {
                double v = bgY0 + j;
                double t = Math.Max(0, Math.Min(1, (v + 100) / 2400.0));
                double r0 = 70 + (170 - 70) * t, g0 = 110 + (196 - 110) * t, b0 = 150 + (226 - 150) * t;
                long row = (long)j * bgW * 3;
                for (int i = 0; i < bgW; i++)
                {
                    double u = bgX0 + i;
                    double hm = 1500 + 90 * Math.Sin(u / 540) + 45 * Math.Sin(u / 201 + 1.3) + 18 * Math.Sin(u / 71 + 2.1) + 6 * Math.Sin(u / 23 + 0.4);
                    double m = Math.Max(0, Math.Min(1, v - hm + 0.5));
                    double cl = 6 * Noise(u / 120.0, v / 120.0) + 3 * Noise(u / 37.0 + 50, v / 37.0);
                    double tex = m > 0 ? 10 * Noise(u / 25.0 + 100, v / 25.0) + 6 * Noise(u / 7.0, v / 7.0) : 0;
                    double r = r0 + cl - m * (80 - tex), g = g0 + cl - m * (60 - tex), b = b0 + cl - m * (70 - tex);
                    Bg[row + i * 3] = C8(b); Bg[row + i * 3 + 1] = C8(g); Bg[row + i * 3 + 2] = C8(r);
                }
            });
        }

        static readonly double[] Spacings = { 10, 13, 16, 19, 22, 12 };
        void BuildThin()
        {
            Parallel.For(0, fgH, delegate (int j)
            {
                double v = fgY0 + j;
                long row = (long)j * fgW;
                for (int i = 0; i < fgW; i++)
                {
                    double u = fgX0 + i;
                    double a = 0, sb = 30, sg = 30, sr = 34;
                    // cables (4), casi horizontales con una curva larga
                    for (int k = 0; k < 4; k++)
                    {
                        double yc = 600 + 46 * k + 22 * Math.Sin(u / (900 + 130 * k) + k) + 0.012 * (k - 1.5) * u;
                        double th = 1.6 + 0.33 * k;
                        double ak = Cov(th / 2 - Math.Abs(v - yc));
                        if (ak > a) { a = ak; sb = 26; sg = 26; sr = 28; }
                    }
                    // verja: barrotes verticales de 2,2-3,8 px; separacion por tramos de 480 px (10, 13, 16, 19, 22 o 12 px) y tres barandillas
                    if (v >= 790 && v < 1190)
                    {
                        int sg0 = (int)Math.Floor(u / 480.0);
                        double sp = Spacings[((sg0 % 6) + 6) % 6], u0 = sg0 * 480.0;
                        int k0 = (int)Math.Floor((u - u0) / sp);
                        for (int k = k0 - 1; k <= k0 + 1; k++)
                        {
                            if (k < 0 || (k + 1) * sp > 480) continue;
                            double xc = u0 + (k + 0.5) * sp + 1.0 * Hash(k * 5 + 1 + sg0 * 131) + 0.6 * Math.Sin(v / 90 + k);
                            double wk = 2.2 + 1.6 * (0.5 + 0.5 * Hash(k * 5 + 2));
                            double ak = Cov(wk / 2 - Math.Abs(u - xc));
                            if (ak > a) { a = ak; double s = 20 + 10 * Hash(k * 5 + 3); sb = s + 4; sg = s + 2; sr = s; }
                        }
                        double ar = Math.Max(Cov(3 - Math.Abs(v - 800)), Math.Max(Cov(3 - Math.Abs(v - 992)), Cov(3 - Math.Abs(v - 1180))));
                        if (ar > a) { a = ar; sb = 24; sg = 22; sr = 22; }
                    }
                    // malla en diagonal y lamas horizontales, en tramos de 600 px que se alternan
                    if (v >= 1210 && v < 1420)
                    {
                        int seg = (int)Math.Floor(u / 600.0);
                        if ((seg & 1) == 0)
                        {
                            double p = 18, d1 = (u + v) / 1.41421356, d2 = (u - v) / 1.41421356;
                            double e1 = Math.Abs(d1 - p * Math.Round(d1 / p)), e2 = Math.Abs(d2 - p * Math.Round(d2 / p));
                            double ak = Math.Max(Cov(0.9 - e1), Cov(0.9 - e2));
                            if (ak > a) { a = ak; sb = 40; sg = 40; sr = 44; }
                        }
                        else
                        {
                            double p = 13, e = Math.Abs(v - 1210 - p * Math.Round((v - 1210) / p));
                            double ak = Cov(1.25 - e);
                            if (ak > a) { a = ak; sb = 34; sg = 30; sr = 30; }
                        }
                        double fr = Math.Min(Cov(3 - Math.Abs(v - 1212)), 1);   // el marco de arriba
                        if (fr > a) { a = fr; sb = 24; sg = 22; sr = 22; }
                    }
                    // hierba: hojas de 1,5-3 px desde el suelo (1700) hasta 1440-1620, algo inclinadas
                    if (v >= 1430)
                    {
                        if (v >= 1700) { double ak = 1; if (ak > a) { a = ak; sb = 30 + 6 * Noise(u / 9, v / 9); sg = 52 + 8 * Noise(u / 13, v / 7); sr = 34; } }
                        else
                        {
                            int k0 = (int)Math.Floor(u / 7.0);
                            for (int k = k0 - 4; k <= k0 + 4; k++)
                            {
                                double baseX = k * 7.0 + 3 * Hash(k * 7 + 1), top = 1450 + 160 * (0.5 + 0.5 * Hash(k * 7 + 2));
                                if (v < top) continue;
                                double f = (1700 - v) / (1700 - top);                  // 0 abajo, 1 en la punta
                                double xc = baseX + 22 * Hash(k * 7 + 3) * f * f;
                                double wk = (1.5 + 1.5 * (0.5 + 0.5 * Hash(k * 7 + 4))) * (1 - 0.6 * f);
                                double ak = Cov(wk / 2 - Math.Abs(u - xc));
                                if (ak > a) { a = ak; double s = 0.5 + 0.5 * Hash(k * 7 + 5); sb = 24 + 10 * s; sg = 60 + 40 * s; sr = 30 + 14 * s; }
                            }
                        }
                    }
                    FgA[row + i] = C8(a * 255); FgB[row + i] = C8(sb); FgG[row + i] = C8(sg); FgR[row + i] = C8(sr);
                }
            });
        }

        // ---------------------------------------------------------------- escena cabeza
        void BuildCity()
        {
            // edificios en columnas de anchura al azar (periodo 4096 px), con ventanas (14x20 cada 26x36), farolas y cables
            int nb = 64; double[] bx0 = new double[nb + 1], bh = new double[nb], bc = new double[nb];
            double acc = 0;
            for (int k = 0; k < nb; k++) { bx0[k] = acc; acc += 120 + 280 * (0.5 + 0.5 * Hash(k * 11 + 1)); bh[k] = 380 + 900 * (0.5 + 0.5 * Hash(k * 11 + 2)); bc[k] = Hash(k * 11 + 3); }
            bx0[nb] = acc; double period = acc;
            Parallel.For(0, bgH, delegate (int j)
            {
                double v = bgY0 + j;
                long row = (long)j * bgW * 3;
                int kb = 0;
                for (int i = 0; i < bgW; i++)
                {
                    double u = bgX0 + i;
                    double uu = u - period * Math.Floor(u / period);
                    while (kb > 0 && uu < bx0[kb]) kb--;
                    while (kb < nb - 1 && uu >= bx0[kb + 1]) kb++;
                    if (uu < bx0[kb]) { kb = 0; while (kb < nb - 1 && uu >= bx0[kb + 1]) kb++; }
                    double t = Math.Max(0, Math.Min(1, v / 1400.0));
                    double r = 120 + 70 * t, g = 150 + 50 * t, b = 190 + 30 * t;   // cielo
                    double top = 1700 - bh[kb];
                    if (v >= top)
                    {
                        double c = bc[kb];
                        r = 92 + 30 * c; g = 86 + 24 * c; b = 80 + 18 * c;
                        double lx = uu - bx0[kb], ly = v - top;
                        double wxp = lx - 26 * Math.Floor(lx / 26), wyp = ly - 36 * Math.Floor(ly / 36);
                        if (lx > 14 && (bx0[kb + 1] - uu) > 14 && ly > 24 && wxp >= 6 && wxp < 20 && wyp >= 8 && wyp < 28)
                        {
                            double lit = Hash2((int)Math.Floor(lx / 26) + kb * 97, (int)Math.Floor(ly / 36));
                            if (lit > 0.35) { r = 230; g = 214; b = 150; } else { r = 38; g = 44; b = 56; }
                        }
                        double edge = Math.Min(uu - bx0[kb], bx0[kb + 1] - uu);
                        if (edge < 3) { r *= 0.6; g *= 0.6; b *= 0.6; }
                        double n2 = 5 * Noise(u / 6.0, v / 6.0); r += n2; g += n2; b += n2;
                    }
                    if (v >= 1700) { double n3 = 8 * Noise(u / 11.0, v / 5.0); r = 70 + n3; g = 70 + n3; b = 74 + n3; if (Math.Abs(v - 1900) < 5 && (uu % 180) < 90) { r = 220; g = 220; b = 200; } }
                    // farolas cada 700 px (4 px) y un cable entre ellas
                    double ul = uu - 700 * Math.Floor(uu / 700) - 350;
                    if (Math.Abs(ul) < 2.5 && v > 1100 && v < 1700) { r = 30; g = 32; b = 36; }
                    double cy = 1060 + 40 * Math.Cos(2 * Math.PI * (uu % 700) / 700);
                    if (Math.Abs(v - cy) < 1.1) { r = 25; g = 25; b = 28; }
                    Bg[row + i * 3] = C8(b); Bg[row + i * 3 + 1] = C8(g); Bg[row + i * 3 + 2] = C8(r);
                }
            });
        }

        // El personaje (quieto, centro de la pantalla): distancia firmada a su silueta (<0 dentro) y su color
        static double Ell(double x, double y, double cx, double cy, double rx, double ry)
        {
            double dx = (x - cx) / rx, dy = (y - cy) / ry, d = Math.Sqrt(dx * dx + dy * dy);
            return (d - 1) * Math.Min(rx, ry);
        }
        void BuildPerson()
        {
            Parallel.For(0, H4, delegate (int Y)
            {
                for (int X = 0; X < W4; X++)
                {
                    double x = X + 0.5, y = Y + 0.5;
                    // cabeza con pelo: elipse 150x190 en (1920, 760); arriba el pelo con mechones (radio que sube y baja)
                    double ang = Math.Atan2(y - 760, x - 1920);
                    double hair = y < 800 ? 26 * Math.Abs(Math.Sin(9 * ang)) + 10 * Math.Abs(Math.Sin(23 * ang + 1)) + 6 : 0;
                    double dHead = Ell(x, y, 1920, 760, 150 + hair, 190 + hair);
                    double dFace = Ell(x, y, 1920, 785, 138, 172);
                    double dEarL = Ell(x, y, 1772, 790, 26, 48), dEarR = Ell(x, y, 2068, 790, 26, 48);
                    double dNeck = Math.Max(Math.Abs(x - 1920) - 68, Math.Max(940 - y, y - 1200));
                    double dBody = y < 1130 ? 1e9 : Ell(x, y, 1920, 1560, 560, 440);
                    double d = Math.Min(Math.Min(dHead, Math.Min(dEarL, dEarR)), Math.Min(dNeck, dBody));
                    double a = Cov(-d);
                    if (a <= 0) continue;
                    double r, g, b;
                    if (dBody < 0 && y > 1150)
                    {
                        // chaqueta con cremallera y costuras
                        double n = 8 * Noise(x / 5.0, y / 5.0) + 6 * Noise(x / 40.0, y / 40.0);
                        r = 62 + n; g = 70 + n; b = 84 + n;
                        if (Math.Abs(x - 1920) < 3) { r = 170; g = 170; b = 176; }
                        if (Math.Abs(dBody + 40) < 2) { r *= 0.6; g *= 0.6; b *= 0.6; }
                    }
                    else if (dFace < 0 || dEarL < 0 || dEarR < 0 || dNeck < 0)
                    {
                        double n = 4 * Noise(x / 9.0, y / 9.0);
                        r = 206 + n; g = 156 + n; b = 126 + n;
                        double eL = Ell(x, y, 1866, 760, 22, 11), eR = Ell(x, y, 1974, 760, 22, 11);
                        if (eL < 0 || eR < 0) { r = 40; g = 36; b = 34; }
                        if (Math.Abs(y - 728) < 4 && (Math.Abs(x - 1866) < 30 || Math.Abs(x - 1974) < 30)) { r = 70; g = 50; b = 40; }   // cejas
                        if (Math.Abs(y - 880) < 2.5 && Math.Abs(x - 1920) < 42) { r = 120; g = 60; b = 60; }                             // boca
                        if (Math.Abs(x - 1920) < 2 && y > 770 && y < 840) { r *= 0.8; g *= 0.8; b *= 0.8; }                              // nariz
                        if (dNeck < 0 && dFace >= 0) { r *= 0.82; g *= 0.82; b *= 0.82; }
                    }
                    else
                    {
                        // pelo oscuro con mechones finos mas claros
                        double st = Math.Abs(Math.Sin(x / 3.1 + 0.6 * Math.Sin(y / 17.0))) > 0.93 ? 30 : 0;
                        r = 52 + st; g = 40 + st * 0.8; b = 34 + st * 0.6;
                    }
                    int i = Y * W4 + X;
                    StA[i] = C8(a * 255); StB[i] = C8(b); StG[i] = C8(g); StR[i] = C8(r); StK[i] = 1;
                }
            });
        }

        // ---------------------------------------------------------------- interfaz quieta (las dos escenas)
        // Un recuadro de texto (letras blancas con borde oscuro) arriba a la izquierda del recorte de medida, un minimapa (marco blanco,
        // dentro negro al 55 %, una flecha) a la derecha y una barra de vida abajo a la izquierda. Geometria a 4K
        public static void HudAt(int X, int Y, out double a, out double v)
        {
            a = 0; v = 0;
            if (X >= 1260 && X < 1786 && Y >= 450 && Y < 600)
            {
                int line = (Y - 450) / 50, yl = (Y - 450) % 50;
                if (yl < 40)
                {
                    int g = (X - 1260) / 26, xl = (X - 1260) % 26;
                    bool gap = (g * 7 + line * 3) % 9 == 4 || g >= 20 - line * 3;
                    if (!gap && xl < 24) { a = 1; v = xl >= 3 && xl < 21 && yl >= 3 && yl < 37 ? 235 : 18; return; }
                }
            }
            if (X >= 2260 && X < 2560 && Y >= 1160 && Y < 1420)
            {
                if (X < 2264 || X >= 2556 || Y < 1164 || Y >= 1416) { a = 1; v = 240; return; }
                int dx = X - 2410, dy = Y - 1290;
                if (dy >= -24 && dy <= 24 && Math.Abs(dx) <= (dy + 24) / 2) { a = 1; v = 230; return; }
                a = 0.55; v = 8; return;
            }
            if (X >= 1240 && X < 1700 && Y >= 1360 && Y < 1384) { a = 1; v = (X < 1600 && Y >= 1364 && Y < 1380) ? 200 : 20; return; }
        }

        void BuildHud()
        {
            Parallel.For(0, H4, delegate (int Y)
            {
                for (int X = 0; X < W4; X++)
                {
                    double a, v; HudAt(X, Y, out a, out v);
                    if (a <= 0) continue;
                    int i = Y * W4 + X;
                    double a0 = StA[i] / 255.0;
                    double b = StB[i] * a0 * (1 - a) + v * a, g = StG[i] * a0 * (1 - a) + v * a, r = StR[i] * a0 * (1 - a) + v * a, at = a + a0 * (1 - a);
                    StA[i] = C8(at * 255); StB[i] = C8(b / at); StG[i] = C8(g / at); StR[i] = C8(r / at); StK[i] = 2;
                }
            });
        }

        // ---------------------------------------------------------------- consultas (para la medida)
        // Alfa de la capa fina en un punto de su capa (pantalla + off)
        public double AlphaLayer(int u, int v)
        {
            if (Kind == 1) return 0;
            int fi = Math.Max(0, Math.Min(fgW - 1, u - fgX0)), fj = Math.Max(0, Math.Min(fgH - 1, v - fgY0));
            return FgA[(long)fj * fgW + fi] / 255.0;
        }
        // Lo quieto en un punto de la pantalla a 4K: alfa (0-1) y tipo (1 personaje, 2 interfaz)
        public double StillAt(int X, int Y, out int kind)
        {
            if (X < 0 || Y < 0 || X >= W4 || Y >= H4) { kind = 0; return 0; }
            int i = Y * W4 + X; kind = StK[i]; return StA[i] / 255.0;
        }

        // Un punto a 4K con las posiciones dadas: B, G, R (0-255)
        public void Pixel4K(int X, int Y, int fx, int fy, int bx, int by, out double b, out double g, out double r)
        {
            int bi = Math.Max(0, Math.Min(bgW - 1, X + bx - bgX0)), bj = Math.Max(0, Math.Min(bgH - 1, Y + by - bgY0));
            long pb = ((long)bj * bgW + bi) * 3;
            b = Bg[pb]; g = Bg[pb + 1]; r = Bg[pb + 2];
            if (Kind != 1)
            {
                int fi = Math.Max(0, Math.Min(fgW - 1, X + fx - fgX0)), fj = Math.Max(0, Math.Min(fgH - 1, Y + fy - fgY0));
                long pf = (long)fj * fgW + fi;
                double a = FgA[pf] / 255.0;
                if (a > 0) { b = b * (1 - a) + FgB[pf] * a; g = g * (1 - a) + FgG[pf] * a; r = r * (1 - a) + FgR[pf] * a; }
            }
            int k = Math.Max(0, Math.Min(H4 - 1, Y)) * W4 + Math.Max(0, Math.Min(W4 - 1, X));
            double sa = StA[k] / 255.0;
            if (sa > 0) { b = b * (1 - sa) + StB[k] * sa; g = g * (1 - sa) + StG[k] * sa; r = r * (1 - sa) + StR[k] * sa; }
        }

        // ---------------------------------------------------------------- composicion
        // Una fila a 4K (B, G, R x 255 por punto, enteros) en 'o'
        unsafe void Row4K(int Y, int fx, int fy, int bx, int by, int* o)
        {
            int bj = Math.Max(0, Math.Min(bgH - 1, Y + by - bgY0));
            fixed (byte* bgp = Bg, fa = FgA, fb = FgB, fg = FgG, fr = FgR, sa = StA, sb = StB, sg = StG, sr = StR)
            {
                byte* pb = bgp + ((long)bj * bgW + bx - bgX0) * 3;
                long frow = Kind == 1 ? 0 : (long)Math.Max(0, Math.Min(fgH - 1, Y + fy - fgY0)) * fgW + fx - fgX0;
                long srow = (long)Y * W4;
                for (int X = 0; X < W4; X++)
                {
                    int b = pb[X * 3] * 255, g = pb[X * 3 + 1] * 255, r = pb[X * 3 + 2] * 255;
                    if (Kind != 1)
                    {
                        int a = fa[frow + X];
                        if (a != 0) { int ia = 255 - a; b = (b * ia) / 255 + fb[frow + X] * a; g = (g * ia) / 255 + fg[frow + X] * a; r = (r * ia) / 255 + fr[frow + X] * a; }
                    }
                    int s = sa[srow + X];
                    if (s != 0) { int isv = 255 - s; b = (b * isv) / 255 + sb[srow + X] * s; g = (g * isv) / 255 + sg[srow + X] * s; r = (r * isv) / 255 + sr[srow + X] * s; }
                    o[X * 3] = b; o[X * 3 + 1] = g; o[X * 3 + 2] = r;
                }
            }
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

        // La imagen n (capturada) en BGR24 de w x h con 'pitch' bytes por fila: 4K reducida por area + ruido de +-noise niveles + codigo
        public unsafe void Compose(long n, byte* dst, int w, int h, int pitch, int noise)
        {
            if (aW != w || aH != h) { AreaWeights(w, W4, out axFirst, out axW); AreaWeights(h, H4, out ayFirst, out ayW); aW = w; aH = h; }
            int fx, fy, bx, by;
            Offsets(n, out fx, out fy, out bx, out by);
            long dp = (long)dst;
            int nb = Math.Max(1, Environment.ProcessorCount - 2), rowsPer = (h + nb - 1) / nb;
            bool full = w == W4 && h == H4;
            Parallel.For(0, nb, delegate (int part)
            {
                int[] r4a = new int[W4 * 3];
                double[] acc = new double[w * 3];
                fixed (int* r4 = r4a)
                    for (int y = part * rowsPer; y < Math.Min(h, (part + 1) * rowsPer); y++)
                    {
                        byte* row = (byte*)dp + (long)y * pitch;
                        uint rng = (uint)(n * 7919 + y * 104729 + 4242) | 1u;
                        int span = 2 * noise + 1;
                        if (full)
                        {
                            Row4K(y, fx, fy, bx, by, r4);
                            for (int x = 0; x < w * 3; x++)
                            {
                                int v = (r4[x] + 127) / 255;
                                if (noise > 0) { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; v += (int)(rng % (uint)span) - noise; }
                                row[x] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                            }
                            continue;
                        }
                        for (int i = 0; i < w * 3; i++) acc[i] = 0;
                        for (int ky = 0; ky < 3; ky++)
                        {
                            double wy = ayW[y * 3 + ky];
                            if (wy <= 0) continue;
                            Row4K(Math.Min(H4 - 1, ayFirst[y] + ky), fx, fy, bx, by, r4);
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
                        for (int x = 0; x < w * 3; x++)
                        {
                            double v = acc[x] / 255.0;
                            if (noise > 0) { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; v += (int)(rng % (uint)span) - noise; }
                            row[x] = C8(v);
                        }
                    }
            });
            DrawCode(n, dst, w, h, pitch);
        }

        // El numero de la imagen en codigo Gray (el mismo sitio y tamaño que el del paralaje: Parallax.CodeX...)
        static unsafe void DrawCode(long n, byte* dst, int w, int h, int pitch)
        {
            uint g = Parallax.Gray(n);
            double k = (double)w / W4;
            int y0 = (int)Math.Round(Parallax.CodeY * k), y1 = Math.Min(h, (int)Math.Round((Parallax.CodeY + Parallax.CodeSize) * k));
            for (int b = 0; b < Parallax.CodeBits; b++)
            {
                int x0 = (int)Math.Round((Parallax.CodeX + b * Parallax.CodeSize) * k), x1 = (int)Math.Round((Parallax.CodeX + (b + 1) * Parallax.CodeSize) * k);
                byte v = ((g >> b) & 1) != 0 ? (byte)230 : (byte)20;
                for (int y = y0; y < y1; y++)
                {
                    byte* row = dst + (long)y * pitch;
                    for (int x = x0 + 1; x < x1 - 1; x++) { row[x * 3] = v; row[x * 3 + 1] = v; row[x * 3 + 2] = v; }
                }
            }
        }
    }
}
