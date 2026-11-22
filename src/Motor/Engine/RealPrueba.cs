// RealPrueba.cs (1.2.4, quinta ronda, agente FGCAL; el usuario: "dlss fg x3 y x4 son inusables de los fallos que tienen. el x2 tambien
// tiene sus fallitos"). test=real: escena de prueba con IMAGEN REAL de consola (sin la PS5 a mano): un mundo hecho con capturas reales
// de un juego puestas en fila (cada una escalada a 4K) que corre como un giro de camara, un personaje recortado de otra captura real que
// sigue a la camara (quieto en pantalla, como en un juego en tercera persona) y la interfaz real de otra captura (minimapa, cuadro de
// texto, avisos, contador, botones) quieta encima. Opcional: test=realf = ademas una verja fina delante (la de MapaPrueba, mas rapida que
// el fondo); test=reala = el personaje no esta clavado: se balancea 1-3 px (como un personaje de verdad, que respira y se mueve un
// poco: quieto unas imagenes, luego un punto); test=realz = ademas la camara avanza y retrocede (acercamiento del fondo hasta x1,13:
// vectores que salen del centro, como al correr o conducir; sin verja); test=realN = un corte de camara cada N imagenes (se pueden
// juntar: realfa15, realaz).
// Las imagenes salen de una carpeta (test_textura=<carpeta>) con tres PNG que hace la herramienta de medida (fuera de git: son capturas
// del juego): mundo.png (alto 2160, ancho >= 3840 + 2 x margen), quieto.png (3840x2160 con alfa: personaje + interfaz) y tipo.png
// (3840x2160, rojo = tipo de lo quieto: 1 persona, 2 interfaz, 4 texto). Mismo movimiento y misma interfaz publica que MapaPrueba (la
// medida usa la misma clase).
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ConsoleEnhancer.Motor
{
class RealPrueba
{
    public const int W4 = 3840, H4 = 2160;
    readonly double Speed; readonly int Cut; readonly bool Fence; readonly bool Anim; public readonly bool Zoom;
    // mundo (capturas reales en fila) y margen a la izquierda
    readonly int bgW, bgH, BgM; readonly byte[] Bg;
    // verja fina opcional (como MapaPrueba)
    const int FgM = 2000; readonly int fgW = W4 + 2 * FgM + 4000; const int FgY0 = 1290, FgY1 = 1720; readonly byte[] FgA;
    // lo quieto a 4K: alfa, tipo y color (la interfaz) y el personaje aparte (se puede balancear)
    readonly byte[] StA, StK, StB, StG, StR;
    readonly byte[] PeA, PeB, PeG, PeR;

    public RealPrueba(string dir, double speed, int cut, bool fence) : this(dir, speed, cut, fence, false, false) { }
    public RealPrueba(string dir, double speed, int cut, bool fence, bool anim, bool zoom)
    {
        Speed = speed; Cut = cut; Fence = fence; Anim = anim; Zoom = zoom && !fence;
        int w, h;
        Bg = LoadRgb(System.IO.Path.Combine(dir, "mundo.png"), out w, out h);
        bgW = w; bgH = h; BgM = Math.Max(0, (bgW - W4) / 2);
        StA = new byte[W4 * H4]; StK = new byte[W4 * H4]; StB = new byte[W4 * H4]; StG = new byte[W4 * H4]; StR = new byte[W4 * H4];
        PeA = new byte[W4 * H4]; PeB = new byte[W4 * H4]; PeG = new byte[W4 * H4]; PeR = new byte[W4 * H4];
        LoadStill(System.IO.Path.Combine(dir, "quieto.png"), System.IO.Path.Combine(dir, "tipo.png"));
        if (fence) { FgA = new byte[(long)fgW * (FgY1 - FgY0)]; BuildFence(); }
    }

    static unsafe byte[] LoadRgb(string file, out int w, out int h)
    {
        using (Bitmap src = new Bitmap(file))
        {
            w = src.Width; h = src.Height;
            byte[] r = new byte[(long)w * h * 3];
            BitmapData d = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try { for (int y = 0; y < h; y++) Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), r, (int)((long)y * w * 3), w * 3); }
            finally { src.UnlockBits(d); }
            return r;
        }
    }

    unsafe void LoadStill(string fq, string ft)
    {
        using (Bitmap q = new Bitmap(fq))
        using (Bitmap t = new Bitmap(ft))
        {
            if (q.Width != W4 || q.Height != H4 || t.Width != W4 || t.Height != H4) throw new Exception("quieto.png / tipo.png tienen que ser de 3840x2160");
            BitmapData dq = q.LockBits(new Rectangle(0, 0, W4, H4), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dt = t.LockBits(new Rectangle(0, 0, W4, H4), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                for (int y = 0; y < H4; y++)
                {
                    byte* rq = (byte*)dq.Scan0 + (long)y * dq.Stride, rt = (byte*)dt.Scan0 + (long)y * dt.Stride;
                    for (int x = 0; x < W4; x++)
                    {
                        int i = y * W4 + x; byte a = rq[x * 4 + 3];
                        if (a == 0) continue;
                        if (rt[x * 3 + 2] == 1) { PeA[i] = a; PeB[i] = rq[x * 4]; PeG[i] = rq[x * 4 + 1]; PeR[i] = rq[x * 4 + 2]; continue; }
                        StA[i] = a; StB[i] = rq[x * 4]; StG[i] = rq[x * 4 + 1]; StR[i] = rq[x * 4 + 2]; StK[i] = rt[x * 3 + 2];
                    }
                }
            }
            finally { q.UnlockBits(dq); t.UnlockBits(dt); }
        }
    }

    static byte C8(double v) { return (byte)(v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5)); }
    static int Even(double v) { return 2 * (int)Math.Round(v / 2); }

    void BuildFence()
    {
        int h = FgY1 - FgY0;
        Parallel.For(0, h, delegate (int j)
        {
            int Y = FgY0 + j;
            for (int i = 0; i < fgW; i++)
            {
                int u = i - FgM;
                double xb = ((u % 18) + 18) % 18; double d = Math.Min(Math.Abs(xb - 1.5), 18 - Math.Abs(xb - 1.5));
                double a = Math.Max(0, Math.Min(1, 2.0 - d));
                if (Y < FgY0 + 10 || (Y >= FgY1 - 40 && Y < FgY1 - 30)) a = 1;
                FgA[(long)j * fgW + i] = C8(a * 255);
            }
        });
    }

    // ---------------------------------------------------------------- movimiento (el de MapaPrueba: hasta ~23 px por imagen el fondo)
    // fy = el balanceo del personaje en horizontal (px a 4K; la verja no se mueve en vertical): 3 sin(2 pi n / 26), a puntos enteros
    void OffsetsI(long n, out int fx, out int fy, out int bx, out int by)
    {
        double q = Speed * (700 * Math.Sin(2 * Math.PI * n / 240) + 160 * Math.Sin(2 * Math.PI * n / 90));
        double qy = Speed * 50 * Math.Sin(2 * Math.PI * n / 200);
        long c = Cut > 0 ? n / Cut : 0;
        fx = Even(q) + (int)(c % 3) * 1500; fy = Anim ? (int)Math.Round(3 * Math.Sin(2 * Math.PI * n / 26)) : 0;
        // (realz) fx = el acercamiento del fondo x 1e6 (sin verja no hace falta su desplazamiento)
        if (Zoom) fx = (int)Math.Round(1e6 * Math.Exp(0.12 * Speed * Math.Sin(2 * Math.PI * n / 150)));
        bx = Even(0.8 * q) + (int)(c % 3) * 1700; by = Even(qy) + (int)(c % 2) * 120;
    }
    public bool CutBetween(long n0) { return Cut > 0 && (n0 + 1) / Cut != n0 / Cut; }
    public void Offsets(double n, out int fx, out int fy, out int bx, out int by)
    {
        double fl = Math.Floor(n);
        if (n - fl < 1e-9) { OffsetsI((long)fl, out fx, out fy, out bx, out by); return; }
        int ax, ay, cx, cy, dx, dy, ex, ey;
        OffsetsI((long)fl, out ax, out ay, out cx, out cy); OffsetsI((long)fl + 1, out dx, out dy, out ex, out ey);
        double t = n - fl;
        fx = (int)Math.Round(ax + (dx - ax) * t); fy = (int)Math.Round(ay + (dy - ay) * t);
        bx = (int)Math.Round(cx + (ex - cx) * t); by = (int)Math.Round(cy + (ey - cy) * t);
    }

    public double AlphaF(int u, int v)
    {
        if (!Fence || v < FgY0 || v >= FgY1) return 0;
        int i = Math.Max(0, Math.Min(fgW - 1, u + FgM)); return FgA[(long)(v - FgY0) * fgW + i] / 255.0;
    }

    // (para las zonas de la medida: el personaje en su sitio sin balanceo)
    public double Still(int X, int Y, out int kind)
    {
        if (X < 0 || Y < 0 || X >= W4 || Y >= H4) { kind = 0; return 0; }
        int i = Y * W4 + X;
        if (StA[i] > 0) { kind = StK[i]; return StA[i] / 255.0; }
        kind = PeA[i] > 0 ? 1 : 0; return PeA[i] / 255.0;
    }

    public void Rgb(int X, int Y, int fx, int fy, int bx, int by, out double b, out double g, out double r)
    {
        if (Zoom)
        {
            // acercamiento alrededor del centro de la pantalla (bilineal)
            double sc = fx / 1e6, u = W4 / 2 + (X - W4 / 2) / sc + bx + BgM, v = H4 / 2 + (Y - H4 / 2) / sc + by;
            int u0 = (int)Math.Floor(u), v0 = (int)Math.Floor(v); double au = u - u0, av = v - v0;
            b = g = r = 0;
            for (int q = 0; q < 4; q++)
            {
                int uu = u0 + (q & 1), vv = Math.Max(0, Math.Min(bgH - 1, v0 + (q >> 1)));
                uu = ((uu % bgW) + bgW) % bgW;
                double w = ((q & 1) != 0 ? au : 1 - au) * ((q >> 1) != 0 ? av : 1 - av);
                long pz = ((long)vv * bgW + uu) * 3; b += Bg[pz] * w; g += Bg[pz + 1] * w; r += Bg[pz + 2] * w;
            }
        }
        else
        {
            int bi = X + bx + BgM; bi = ((bi % bgW) + bgW) % bgW;
            int bj = Math.Max(0, Math.Min(bgH - 1, Y + by));
            long pb = ((long)bj * bgW + bi) * 3; b = Bg[pb]; g = Bg[pb + 1]; r = Bg[pb + 2];
        }
        if (Fence)
        {
            double fa = AlphaF(X + fx, Y);
            if (fa > 0) { b = b * (1 - fa) + 34 * fa; g = g * (1 - fa) + 30 * fa; r = r * (1 - fa) + 28 * fa; }
        }
        int yy = Math.Max(0, Math.Min(H4 - 1, Y)), xp = X - fy;
        if (xp >= 0 && xp < W4)
        {
            int kp = yy * W4 + xp; double pa = PeA[kp] / 255.0;
            if (pa > 0) { b = b * (1 - pa) + PeB[kp] * pa; g = g * (1 - pa) + PeG[kp] * pa; r = r * (1 - pa) + PeR[kp] * pa; }
        }
        int k = yy * W4 + Math.Max(0, Math.Min(W4 - 1, X));
        double sa = StA[k] / 255.0;
        if (sa > 0) { b = b * (1 - sa) + StB[k] * sa; g = g * (1 - sa) + StG[k] * sa; r = r * (1 - sa) + StR[k] * sa; }
    }

    // Como MapaPrueba: las CacheN primeras imagenes se hacen una vez y se repiten (el codigo Gray vuelve a 0: la medida salta ese par)
    public const int CacheN = 60;
    byte[][] cache; int cacheW, cacheH, cachePitch;
    public unsafe void Compose(long n, byte* dst, int w, int h, int pitch, int noise)
    {
        if (cache == null || cacheW != w || cacheH != h || cachePitch != pitch)
        {
            byte[][] c = new byte[CacheN][];
            for (int k = 0; k < CacheN; k++) { c[k] = new byte[(long)pitch * h]; fixed (byte* p = c[k]) ComposeNow(k, p, w, h, pitch); }
            cache = c; cacheW = w; cacheH = h; cachePitch = pitch;
        }
        byte[] f = cache[(int)(n % CacheN)];
        Marshal.Copy(f, 0, (IntPtr)dst, f.Length);
    }

    public unsafe void ComposeNow(long n, byte* dst, int w, int h, int pitch)
    {
        int fx, fy, bx, by; Offsets(n, out fx, out fy, out bx, out by);
        long dp = (long)dst; double kx = (double)W4 / w, ky = (double)H4 / h;
        Parallel.For(0, h, delegate (int y)
        {
            byte* row = (byte*)dp + (long)y * pitch;
            int Y0 = (int)Math.Floor(y * ky), Y1 = Math.Max(Y0 + 1, (int)Math.Floor((y + 1) * ky));
            for (int x = 0; x < w; x++)
            {
                int X0 = (int)Math.Floor(x * kx), X1 = Math.Max(X0 + 1, (int)Math.Floor((x + 1) * kx));
                double sb = 0, sg = 0, sr = 0; int c = 0;
                for (int Y = Y0; Y < Y1; Y++) for (int X = X0; X < X1; X++) { double b, g, r; Rgb(X, Y, fx, fy, bx, by, out b, out g, out r); sb += b; sg += g; sr += r; c++; }
                row[x * 3] = C8(sb / c); row[x * 3 + 1] = C8(sg / c); row[x * 3 + 2] = C8(sr / c);
            }
        });
        // codigo Gray (como Finas / Parallax / MapaPrueba)
        uint gcode = Parallax.Gray(n); double k = (double)w / W4;
        int y0 = (int)Math.Round(Parallax.CodeY * k), y1 = Math.Min(h, (int)Math.Round((Parallax.CodeY + Parallax.CodeSize) * k));
        for (int bit = 0; bit < Parallax.CodeBits; bit++)
        {
            int x0 = (int)Math.Round((Parallax.CodeX + bit * Parallax.CodeSize) * k), x1 = (int)Math.Round((Parallax.CodeX + (bit + 1) * Parallax.CodeSize) * k);
            byte v = ((gcode >> bit) & 1) != 0 ? (byte)230 : (byte)20;
            for (int yy = y0; yy < y1; yy++) { byte* rr = dst + (long)yy * pitch; for (int xx = x0 + 1; xx < x1 - 1; xx++) { rr[xx * 3] = v; rr[xx * 3 + 1] = v; rr[xx * 3 + 2] = v; } }
        }
    }
}
}
