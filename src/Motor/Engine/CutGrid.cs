using System;

namespace ConsoleEnhancer.Motor
{
    // 20-11 (GIRO; el usuario: "al girar la camara rapido parece que va a 30 fps en vez de 30+FG con DLSS FG"). Rejilla de luma de la
    // captura para el corte de escena (Engine.cs SceneCut) y el movimiento de la camara (el giro) para la busqueda de vectores.
    // Las mismas muestras que corte_medias=1 (8x8 puntos por casilla de 32x18), guardadas en 64x36 casillas de 4x4: la media de 2x2 de
    // ellas es exactamente la casilla de siempre ("viejo", el corte de antes: > 28 niveles de media).
    // Causa (medida con ag_GIRO\tests\corte_giro.cs, 46 imagenes de las consolas ampliadas a 4K y desplazadas como un giro, con y sin
    // desenfoque, con perspectiva y paralaje): en un giro rapido las medias de 120x120 px cambian como en un corte (2-13 % de los paneos
    // de 250-600 px pasaban de 28): DLSS SR, DLSS 5 y DLSS-G empezaban sin historia y los vectores iban a 0 (Engine.cs: resetHistory;
    // DLSS-G con reset NO inventa la intermedia: medido con la tele, 0 de 24 copias generadas con un corte en cada imagen, 9 de 24 sin).
    // Ahora (corte_giro=1), si pasa de 28 se busca el desplazamiento de la rejilla que mejor la explica (+-6 x +-3 casillas de 32x18 =
    // +-720 x +-360 px a 4K, y +-2 x +-1 de 64x36 alrededor, un poco peor cuanto mas lejos) y solo es corte si ni asi pasa de
    // corte_giro_umbral (de serie 20) en la rejilla fina: 0 de 4554 paneos son corte (con el de siempre, 134) y los 2910 cortes de verdad
    // que veia el de siempre (otra escena, menu, negro, pantalla de carga) se siguen viendo todos (el mas bajo, 21,9).
    // De paso sale el giro de la camara (Gx, Gy en px de la captura: donde estaba en la anterior - donde esta, como los vectores) para
    // la busqueda de vectores (mvglobal, EngineMotion.cs): el nivel grueso solo buscaba +-12 px a 1/8 = +-96 px de la captura.
    unsafe class CutGrid
    {
        public const int GW = 64, GH = 36, CW = 32, CH = 18;
        readonly float[] fine = new float[GW * GH], finePrev = new float[GW * GH];
        readonly float[] coarse = new float[CW * CH], coarsePrev = new float[CW * CH];
        bool valid;
        readonly double[] cd = new double[13 * 7];   // la busqueda gruesa (+-6 x +-3)
        double lastFx, lastFy; bool lastSet;         // el giro de la imagen anterior (casillas finas)
        public double Threshold = 28;       // el de siempre (rejilla de 32x18)
        public double CompThreshold = 20;   // corte_giro_umbral: con el mejor desplazamiento, en la rejilla de 64x36
        public bool Compensate = true;      // corte_giro=1
        public bool Global = true;          // calcular el giro aunque no haya corte (mvglobal)

        public struct Result
        {
            public bool Cut, CutOld;        // corte (con lo de ahora) y lo que habria dicho el de antes
            public double Old, Comp;        // diferencia media de siempre (32x18) y la mejor desplazada (64x36), niveles
            public double Gx, Gy;           // giro en px de la captura (vector: anterior - actual)
            public bool GOk;                // giro fiable (explica la diferencia mucho mejor que quieto)
        }

        public void Invalidate() { valid = false; }

        // src: la imagen (filas de arriba abajo); bpp 1|2|3|4; lumaFirst: la luma es el primer byte (yuy2 / gris), si no BGR(X)
        public Result Next(byte* src, int pitch, int w, int h, int bpp, bool lumaFirst)
        {
            Result r = new Result();
            for (int J = 0; J < GH; J++)
            {
                for (int I = 0; I < GW; I++)
                {
                    float l = 0;
                    for (int t = 0; t < 4; t++)
                    {
                        byte* row = src + (long)((8 * J + 2 * t + 1) * h / 288) * pitch;
                        for (int s = 0; s < 4; s++)
                        {
                            byte* p = row + ((8 * I + 2 * s + 1) * w / 512) * bpp;
                            l += lumaFirst ? p[0] : (p[0] * 0.114f + p[1] * 0.587f + p[2] * 0.299f);
                        }
                    }
                    fine[J * GW + I] = l / 16;
                }
            }
            for (int j = 0; j < CH; j++)
                for (int i = 0; i < CW; i++)
                {
                    int f = 2 * j * GW + 2 * i;
                    coarse[j * CW + i] = 0.25f * (fine[f] + fine[f + 1] + fine[f + GW] + fine[f + GW + 1]);
                }
            if (valid)
            {
                r.Old = Diff(coarse, coarsePrev, CW, CH, 0, 0);
                r.CutOld = r.Old > Threshold;
                bool search = (Compensate && r.CutOld) || (Global && Diff(fine, finePrev, GW, GH, 0, 0) >= 2.0);   // (quieta: nada que buscar)
                r.Comp = r.Old;
                if (search) Search(ref r, w, h); else lastSet = false;
                r.Cut = r.CutOld && (!Compensate || r.Comp > CompThreshold);
            }
            valid = true;
            Array.Copy(fine, finePrev, fine.Length);
            Array.Copy(coarse, coarsePrev, coarse.Length);
            return r;
        }

        // Diferencia media de |a(i, j) - b(i + sx, j + sy)| donde las dos existen (el contenido de la casilla i estaba en la i + sx)
        static double Diff(float[] a, float[] b, int gw, int gh, int sx, int sy)
        {
            int i0 = Math.Max(0, -sx), i1 = Math.Min(gw, gw - sx), j0 = Math.Max(0, -sy), j1 = Math.Min(gh, gh - sy);
            if (i1 <= i0 || j1 <= j0) return 1e9;
            double d = 0;
            fixed (float* pa = a, pb = b)
            {
                for (int j = j0; j < j1; j++)
                {
                    float* ra = pa + j * gw, rb = pb + (j + sy) * gw + sx;
                    float s = 0;
                    for (int i = i0; i < i1; i++) s += Math.Abs(ra[i] - rb[i]);
                    d += s;
                }
            }
            return d / ((i1 - i0) * (j1 - j0));
        }

        void Search(ref Result r, int w, int h)
        {
            // Grueso: 32x18, +-6 x +-3 casillas (+-720 x +-360 px a 4K; lo que queda a la vista: 26 de 32 y 15 de 18 como poco)
            double best = r.Old; int bx = 0, by = 0;
            for (int sy = -3; sy <= 3; sy++)
                for (int sx = -6; sx <= 6; sx++)
                {
                    double d = sx == 0 && sy == 0 ? r.Old : Diff(coarse, coarsePrev, CW, CH, sx, sy) * (1 + 0.01 * (Math.Abs(sx) + Math.Abs(sy)));   // (ante la duda, el mas corto)
                    cd[(sy + 3) * 13 + sx + 6] = d;
                    if (d < best) { best = d; bx = sx; by = sy; }
                }
            // Lo mejor a 2 casillas o mas del mejor: si casa casi igual (algo que se repite: una reja, ventanas), el giro es dudoso
            double best2 = 1e9;
            for (int sy = -3; sy <= 3; sy++)
                for (int sx = -6; sx <= 6; sx++)
                    if (Math.Max(Math.Abs(sx - bx), Math.Abs(sy - by)) >= 2) best2 = Math.Min(best2, cd[(sy + 3) * 13 + sx + 6]);
            // Fino: 64x36, +-2 x +-1 alrededor del doble (y el 0)
            double d0 = Diff(fine, finePrev, GW, GH, 0, 0);
            double fb = d0; int fx = 0, fy = 0;
            for (int sy = 2 * by - 1; sy <= 2 * by + 1; sy++)
                for (int sx = 2 * bx - 2; sx <= 2 * bx + 2; sx++)
                {
                    if ((sx == 0 && sy == 0) || Math.Abs(sx) > 14 || Math.Abs(sy) > 7) continue;
                    double d = Diff(fine, finePrev, GW, GH, sx, sy);
                    if (d < fb) { fb = d; fx = sx; fy = sy; }
                }
            // (para el corte, un poco peor cuanto mas lejos: con poco a la vista y mucho desplazamiento casan cosas que no son el giro)
            r.Comp = fb * (1 + 0.025 * (Math.Abs(fx) + Math.Abs(fy)));
            // Fraccion de casilla por parabola en cada eje
            double ox = 0, oy = 0;
            if (fx != 0 || fy != 0)
            {
                double dl = Diff(fine, finePrev, GW, GH, fx - 1, fy), dr = Diff(fine, finePrev, GW, GH, fx + 1, fy);
                double du = Diff(fine, finePrev, GW, GH, fx, fy - 1), dd = Diff(fine, finePrev, GW, GH, fx, fy + 1);
                double ax = dl - 2 * fb + dr, ay = du - 2 * fb + dd;
                if (ax > 1e-6 && dl < 1e8 && dr < 1e8) ox = Math.Max(-0.5, Math.Min(0.5, 0.5 * (dl - dr) / ax));
                if (ay > 1e-6 && du < 1e8 && dd < 1e8) oy = Math.Max(-0.5, Math.Min(0.5, 0.5 * (du - dd) / ay));
            }
            r.Gx = (fx + ox) * w / (double)GW;
            r.Gy = (fy + oy) * h / (double)GH;
            // Fiable: algo ha cambiado, desplazado lo explica mucho mejor que quieto, sin otro desplazamiento que case casi igual y parecido
            // al de la imagen anterior (la camara no salta: en el paralaje con la reja, sin esto, a veces -240 px en vez de +200)
            bool steady = lastSet && Math.Abs(fx + ox - lastFx) <= 1.5 && Math.Abs(fy + oy - lastFy) <= 1.0;
            r.GOk = (fx != 0 || fy != 0) && d0 >= 2.0 && fb <= 0.75 * d0 && best2 >= 1.15 * best && steady;
            lastFx = fx + ox; lastFy = fy + oy; lastSet = true;
        }
    }
}
