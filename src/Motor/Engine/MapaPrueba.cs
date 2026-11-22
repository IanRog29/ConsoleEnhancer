// MapaPrueba.cs (1.2.4, agente DLSSFG; el usuario: "Dlss fg x2 x3 y x4 tiene muchos fallos visuales y en los mapas"). test=mapa: escena de prueba "mapa" para medir la generacion con la interfaz de un juego encima de un fondo
// que corre: minimapa (marco quieto, dentro un mapa que se desplaza en OTRA direccion, flecha del jugador quieta), textos pequenos con
// borde oscuro, brujula con marcas finas, barra de vida, un icono; un personaje quieto en el centro (cabeza con pelo) y una verja de
// barrotes finos delante (mas rapida que el fondo). Fondo: una ciudad con ventanas que se repiten. Todo a 4K, reducido por area.
// Opcion cortes=N: cada N imagenes un corte de camara (el fondo, la verja y el mapa saltan).
// Tipos de lo quieto (Still): 1 persona, 2 interfaz, 3 dentro del minimapa (se mueve con su propio desplazamiento), 4 texto.
using System;
using System.Threading.Tasks;

namespace ConsoleEnhancer.Motor
{
class MapaPrueba
{
    public const int W4 = 3840, H4 = 2160;
    readonly double Speed; readonly int Cut;
    // fondo
    const int BgM = 1400; readonly int bgW = W4 + 2 * BgM + 6000, bgH = H4 + 2 * 400; readonly byte[] Bg;
    // verja (capa fina): alfa y color, en coordenadas de capa
    const int FgM = 2000; readonly int fgW = W4 + 2 * FgM + 4000; const int FgY0 = 1290, FgY1 = 1720; readonly byte[] FgA;
    // mapa del minimapa (textura) y lo quieto a 4K
    public const int MX0 = 1262, MY0 = 1222, MX1 = 1698, MY1 = 1658;   // interior del minimapa (pantalla)
    const int MapW = 3000, MapH = 3000; readonly byte[] Map;
    readonly byte[] StA, StV, StK;   // lo quieto: alfa, gris, tipo
    readonly byte[] PerB, PerG, PerR; // color de la persona

    public MapaPrueba(double speed, int cut)
    {
        Speed = speed; Cut = cut;
        Bg = new byte[(long)bgW * bgH * 3]; FgA = new byte[(long)fgW * (FgY1 - FgY0)]; Map = new byte[MapW * MapH];
        StA = new byte[W4 * H4]; StV = new byte[W4 * H4]; StK = new byte[W4 * H4]; PerB = new byte[W4 * H4]; PerG = new byte[W4 * H4]; PerR = new byte[W4 * H4];
        BuildCity(); BuildFence(); BuildMap(); BuildPerson(); BuildHud();
    }

    // ---------------------------------------------------------------- utilidades
    static double Hash(int k) { uint h = (uint)k * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16; return h / 4294967295.0 * 2 - 1; }
    static double Hash2(int x, int y) { return Hash(x * 73856093 ^ y * 19349663); }
    static double Noise(double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y); double fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        double a = Hash2(ix, iy), b = Hash2(ix + 1, iy), c = Hash2(ix, iy + 1), d = Hash2(ix + 1, iy + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }
    static byte C8(double v) { return (byte)(v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5)); }
    static int Even(double v) { return 2 * (int)Math.Round(v / 2); }

    // ---------------------------------------------------------------- movimiento
    // q: la verja (hasta ~29 px por imagen a 4K); el fondo 0,8 q (~23 px) y un poco en vertical; el mapa del minimapa: en vertical 0,3 q y
    // en horizontal -0,12 q (otra direccion que el fondo)
    void OffsetsI(long n, out int fx, out int fy, out int bx, out int by)
    {
        double q = Speed * (700 * Math.Sin(2 * Math.PI * n / 240) + 160 * Math.Sin(2 * Math.PI * n / 90));
        double qy = Speed * 50 * Math.Sin(2 * Math.PI * n / 200);
        long c = Cut > 0 ? n / Cut : 0;
        fx = Even(q) + (int)(c % 3) * 1500; fy = 0;
        bx = Even(0.8 * q) + (int)(c % 3) * 2000; by = Even(qy) + (int)(c % 2) * 300;
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
    // desplazamiento del mapa del minimapa (de la posicion de la verja)
    public static void MapOff(int fx, out int mx, out int my) { mx = Even(-0.12 * fx); my = Even(0.3 * fx); }

    // ---------------------------------------------------------------- texturas
    void BuildCity()
    {
        Parallel.For(0, bgH, delegate (int j)
        {
            int v = j - 400; long row = (long)j * bgW * 3;
            for (int i = 0; i < bgW; i++)
            {
                int u = i - BgM;
                // cielo arriba, edificios con ventanas que se repiten, calle abajo
                double sky = 150 + 40 * (1 - Math.Max(0, Math.Min(1, (v + 200) / 900.0))) + 8 * Noise(u / 90.0, v / 90.0);
                int bi = (int)Math.Floor(u / 260.0); double top = 420 + 260 * (0.5 + 0.5 * Hash(bi * 7 + 3));
                double r, g, b;
                if (v < top) { r = sky * 0.75; g = sky * 0.85; b = sky; }
                else
                {
                    double base0 = 70 + 50 * (0.5 + 0.5 * Hash(bi * 13 + 1));
                    r = base0 * 1.05; g = base0; b = base0 * 0.9;
                    int wx = ((u % 22) + 22) % 22, wy = ((v % 30) + 30) % 30;
                    if (wx >= 5 && wx < 16 && wy >= 7 && wy < 22) { double lit = Hash2(u / 22, v / 30) > 0.3 ? 120 : 0; r = 40 + lit * 1.2; g = 45 + lit; b = 60 + lit * 0.5; }
                    if (((u % 260) + 260) % 260 < 4) { r = 25; g = 25; b = 28; }
                    double tex = 7 * Noise(u / 6.0, v / 6.0); r += tex; g += tex; b += tex;
                }
                if (v > 1850) { double st = 60 + 12 * Noise(u / 14.0, v / 14.0) + ((((u + 3 * v) % 180) + 180) % 180 < 8 ? 120 : 0); r = st; g = st; b = st * 1.05; }
                // farolas (postes finos)
                if (((u % 610) + 610) % 610 < 5 && v > 900) { r = 30; g = 32; b = 35; }
                // un marcador del mundo (rombo amarillo): se mueve con el fondo
                int du = ((u % 1300) + 1300) % 1300 - 650, dv = v - 760;
                if (Math.Abs(du) + Math.Abs(dv) < 34) { r = 250; g = 210; b = 40; }
                long o = row + (long)i * 3; Bg[o] = C8(b); Bg[o + 1] = C8(g); Bg[o + 2] = C8(r);
            }
        });
    }
    void BuildFence()
    {
        int h = FgY1 - FgY0;
        Parallel.For(0, h, delegate (int j)
        {
            int Y = FgY0 + j;
            for (int i = 0; i < fgW; i++)
            {
                int u = i - FgM;
                double a = 0;
                // barrotes de 3 px cada 18 px, barandillas arriba y abajo de 8 px
                double xb = ((u % 18) + 18) % 18; double d = Math.Min(Math.Abs(xb - 1.5), 18 - Math.Abs(xb - 1.5));
                a = Math.Max(0, Math.Min(1, 2.0 - d));
                if (Y < FgY0 + 10 || (Y >= FgY1 - 40 && Y < FgY1 - 30)) a = 1;
                FgA[(long)j * fgW + i] = C8(a * 255);
            }
        });
    }
    public double AlphaF(int u, int v)
    {
        if (v < FgY0 || v >= FgY1) return 0;
        int i = Math.Max(0, Math.Min(fgW - 1, u + FgM)); return FgA[(long)(v - FgY0) * fgW + i] / 255.0;
    }
    void BuildMap()
    {
        Parallel.For(0, MapH, delegate (int y)
        {
            for (int x = 0; x < MapW; x++)
            {
                double v = 60 + 20 * Noise(x / 60.0, y / 60.0);
                if (Noise(x / 210.0 + 9, y / 210.0) > 0.35) v = 40;            // agua
                if (((x % 150) + 150) % 150 < 6 || ((y % 190) + 190) % 190 < 6) v = 170;   // calles
                if ((((x + y) % 330) + 330) % 330 < 3) v = 140;                // calle en diagonal
                if (Hash2(x / 75, y / 75) > 0.85 && ((x % 75) > 20 && (x % 75) < 55 && (y % 75) > 20 && (y % 75) < 55)) v = 220; // iconos
                Map[y * MapW + x] = C8(v);
            }
        });
    }
    double MapAt(int X, int Y, int mx, int my)
    {
        int x = ((X - MX0 + mx + 1000) % MapW + MapW) % MapW, y = ((Y - MY0 + my + 1000) % MapH + MapH) % MapH;
        return Map[y * MapW + x];
    }
    void BuildPerson()
    {
        Parallel.For(0, H4, delegate (int Y)
        {
            for (int X = 1650; X < 2190; X++)
            {
                double dx = (X - 1920) / 105.0, dy = (Y - 1000) / 135.0;
                double head = Math.Sqrt(dx * dx + dy * dy);
                double sx = (X - 1920) / 260.0, sy = (Y - 1300) / 150.0;
                double sh = Y >= 1150 && Y < 1288 ? Math.Sqrt(sx * sx + sy * sy) : 9;
                double neck = Math.Abs(X - 1920) < 45 && Y >= 1110 && Y < 1200 ? 0 : 9;
                double a = 0, r = 0, g = 0, b = 0;
                if (head < 1.0 || sh < 1.0 || neck < 1)
                {
                    a = head < 1.0 ? Math.Max(0, Math.Min(1, (1 - head) * 105)) : 1;
                    if (head < 1.0 && dy < -0.15) { double st = Math.Abs(Math.Sin(X / 3.1 + 0.6 * Math.Sin(Y / 17.0))) > 0.93 ? 30 : 0; r = 52 + st; g = 40 + st * 0.8; b = 34 + st * 0.6; }
                    else if (head < 1.0 || neck < 1) { r = 205; g = 160; b = 130; }
                    else { r = 60; g = 70; b = 110; double st = 10 * Noise(X / 9.0, Y / 9.0); r += st; g += st; b += st; }
                }
                if (a <= 0) continue;
                int i = Y * W4 + X; StA[i] = C8(a * 255); StK[i] = 1; PerB[i] = C8(b); PerG[i] = C8(g); PerR[i] = C8(r);
                StV[i] = C8(0.2126 * r + 0.7152 * g + 0.0722 * b);
            }
        });
    }
    // letras de 5x7 de mentira (por hash), cada punto 'k' px, blancas con borde oscuro de 2 px
    static bool Glyph(int ch, int gx, int gy) { if (gx < 0 || gx >= 5 || gy < 0 || gy >= 7) return false; return Hash(ch * 131 + gy * 7 + gx) > -0.1; }
    static void TextAt(int X, int Y, int x0, int y0, int k, int chars, int seed, out double a, out double v)
    {
        a = 0; v = 0;
        int cw = 6 * k, lx = X - x0, ly = Y - y0;
        if (lx < -2 || ly < -2 || lx >= chars * cw + 2 || ly >= 7 * k + 2) return;
        bool ink = false, near = false;
        for (int dy = -2; dy <= 2 && !ink; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int px = lx + dx, py = ly + dy; if (px < 0 || py < 0) continue;
                int c = px / cw, gx = (px % cw) / k, gy = py / k;
                if (c >= chars || (seed + c) % 7 == 3) continue;
                if (Glyph(seed + c, gx, gy)) { if (dx == 0 && dy == 0) { ink = true; break; } near = true; }
            }
        if (ink) { a = 1; v = 236; } else if (near) { a = 1; v = 16; }
    }
    void BuildHud()
    {
        Parallel.For(0, H4, delegate (int Y)
        {
            for (int X = 1200; X < 2700; X++)
            {
                double a = 0, v = 0; int kind = 2;
                // textos arriba a la izquierda (3 lineas: grande, media, pequena)
                double ta, tv;
                TextAt(X, Y, 1270, 500, 5, 18, 11, out ta, out tv); if (ta > 0) { a = ta; v = tv; kind = 4; }
                TextAt(X, Y, 1270, 560, 4, 26, 37, out ta, out tv); if (ta > 0) { a = ta; v = tv; kind = 4; }
                TextAt(X, Y, 1270, 610, 3, 34, 71, out ta, out tv); if (ta > 0) { a = ta; v = tv; kind = 4; }
                // brujula: barra con marcas finas de 2 px cada 24 px y una letra
                if (X >= 2060 && X < 2580 && Y >= 500 && Y < 540)
                {
                    if (Y < 503 || Y >= 537) { a = 1; v = 200; }
                    else if ((X - 2060) % 24 < 2 && Y >= 515) { a = 1; v = 230; }
                }
                TextAt(X, Y, 2305, 550, 4, 1, 5, out ta, out tv); if (ta > 0) { a = ta; v = tv; kind = 4; }
                // minimapa: borde oscuro 4 px + marco blanco 6 px; dentro el mapa (tipo 3); flecha del jugador y "N" quietas
                if (X >= MX0 - 10 && X < MX1 + 10 && Y >= MY0 - 10 && Y < MY1 + 10)
                {
                    if (X < MX0 - 6 || X >= MX1 + 6 || Y < MY0 - 6 || Y >= MY1 + 6) { a = 1; v = 18; }
                    else if (X < MX0 || X >= MX1 || Y < MY0 || Y >= MY1) { a = 1; v = 235; }
                    else
                    {
                        int dx = X - (MX0 + MX1) / 2, dy = Y - (MY0 + MY1) / 2;
                        if (dy >= -26 && dy <= 22 && Math.Abs(dx) <= (dy + 26) / 2.2) { a = 1; v = dy > 16 ? 20 : 245; }
                        else { a = 1; v = 0; kind = 3; }
                        TextAt(X, Y, MX0 + 200, MY0 + 12, 4, 1, 23, out ta, out tv); if (ta > 0) { a = ta; v = tv; kind = 4; }
                    }
                }
                // barra de vida y un icono redondo con numero
                if (X >= 1760 && X < 2560 && Y >= 1628 && Y < 1652) { a = 1; v = (X < 2300 && Y >= 1632 && Y < 1648) ? 190 : 22; }
                {
                    double dx = X - 2480, dy = Y - 1330, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < 70) { a = 1; v = d > 62 ? 230 : 30; }
                    TextAt(X, Y, 2464, 1312, 5, 1, 41, out ta, out tv); if (ta > 0 && d < 60) { a = ta; v = tv; kind = 4; }
                }
                if (a <= 0) continue;
                int i = Y * W4 + X; StA[i] = C8(a * 255); StV[i] = C8(v); StK[i] = (byte)kind;
            }
        });
    }

    // ---------------------------------------------------------------- consultas
    public double Still(int X, int Y, out int kind)
    {
        if (X < 0 || Y < 0 || X >= W4 || Y >= H4) { kind = 0; return 0; }
        int i = Y * W4 + X; kind = StK[i]; return StA[i] / 255.0;
    }
    public void Rgb(int X, int Y, int fx, int fy, int bx, int by, out double b, out double g, out double r)
    {
        int bi = Math.Max(0, Math.Min(bgW - 1, X + bx + BgM)), bj = Math.Max(0, Math.Min(bgH - 1, Y + by + 400));
        long pb = ((long)bj * bgW + bi) * 3; b = Bg[pb]; g = Bg[pb + 1]; r = Bg[pb + 2];
        double fa = AlphaF(X + fx, Y + fy);
        if (fa > 0) { b = b * (1 - fa) + 34 * fa; g = g * (1 - fa) + 30 * fa; r = r * (1 - fa) + 28 * fa; }
        int k = Math.Max(0, Math.Min(H4 - 1, Y)) * W4 + Math.Max(0, Math.Min(W4 - 1, X));
        double sa = StA[k] / 255.0;
        if (sa > 0)
        {
            double vb, vg, vr; int kind = StK[k];
            if (kind == 3) { int mx, my; MapOff(fx, out mx, out my); vb = vg = vr = MapAt(X, Y, mx, my); vr *= 0.9; }
            else if (kind == 1) { vb = PerB[k]; vg = PerG[k]; vr = PerR[k]; }
            else { vb = vg = vr = StV[k]; }
            b = b * (1 - sa) + vb * sa; g = g * (1 - sa) + vg * sa; r = r * (1 - sa) + vr * sa;
        }
    }
    public unsafe void Compose(long n, byte* dst, int w, int h, int pitch, int noise)
    {
        int fx, fy, bx, by; Offsets(n, out fx, out fy, out bx, out by);
        long dp = (long)dst; double kx = (double)W4 / w, ky = (double)H4 / h;
        // reduccion por area sencilla (cajas de kx x ky puntos a 4K; a 4K, 1:1)
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
        // codigo Gray (como Finas / Parallax)
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
