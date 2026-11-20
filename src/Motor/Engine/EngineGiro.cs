using System;
using System.Globalization;

namespace ConsoleEnhancer.Motor
{
    // 20-11 (GIRO; el usuario: "al girar la camara rapido parece que va a 30 fps en vez de 30+FG con DLSS FG"; GTA VI en la PS5 a 4K,
    // modo 30 + DLSS FG + DLSS 5). Dos cosas, las dos con la rejilla de luma de la captura (CutGrid.cs, hilo de la captura):
    // 1. corte_giro=1: un giro rapido ya no es un "corte de escena". Antes, si la media de luma de las casillas de 32x18 cambiaba mas
    //    de 28 niveles, corte: DLSS SR, DLSS 5 y DLSS-G sin historia y los vectores a 0 (RecordMotion no busca con frameReset). DLSS-G
    //    con reset no inventa la intermedia (esa pareja se ve a 30). Su partida del 20-11 (17:55-20:02): 165 cortes, en rachas cada
    //    5-10 s (19:15-19:31) jugando. Ahora solo es corte si ningun desplazamiento de la rejilla (+-720 x +-480 px a 4K) lo explica.
    // 2. mvglobal=1: ese desplazamiento (el giro de la camara) es un candidato mas en el nivel grueso de la busqueda de vectores, con
    //    +-mvglobal_radio px a 1/8 alrededor, y mvlargo no castiga la distancia a el (Shaders.cs Search, Neighbors & 16 / & 32). Antes el
    //    nivel grueso solo buscaba +-12 px a 1/8 = +-96 px de la entrada (con mvlargo=0.008 y mvlargo_rel=1, menos aun al empezar el
    //    giro): un giro de 150-400 px por imagen real a 4K salia con vectores de otro sitio y DLSS-G (fg_vectores=2 le pasa los
    //    nuestros) inventaba la intermedia mal o doble.
    unsafe partial class Engine
    {
        readonly CutGrid giroGrid = new CutGrid();
        // hilo de captura -> ranura: el giro de la imagen respecto a la anterior que paso por el corte (px de la captura)
        float giroX, giroY; bool giroOk;
        int giroPassN;
        readonly float[] slotGiroX = new float[Slots], slotGiroY = new float[Slots];
        readonly bool[] slotGiroOk = new bool[Slots];
        readonly int[] slotPassN = new int[Slots];
        // hilo de pintado: el giro de la imagen que se pinta, en px de la entrada (nivel 0 de la piramide)
        int giroLastRendered = -10;
        float mvGx, mvGy; bool mvGOk;
        // registro: giros que con lo de antes habrian sido corte
        int giroNotCut; double giroWorstOld, giroWorstComp; long giroLogAt;
        int cutTestN;
        int giroBenchN, giroBenchOld, giroBenchCut;

        // SceneCut (hilo de captura), antes que nada. true = el corte lo decide esto (cut)
        bool GiroCut(byte* src, int pitch, int w, int h, int fmt, out bool cut)
        {
            cut = false;
            giroOk = false;
            giroPassN++;
            if (fmt == Shaders.FmtNv12) return false;
            int bpp = fmt == Shaders.FmtBgr24 ? 3 : fmt == Shaders.FmtYuy2 ? 2 : 4;
            CutGrid.Result r = new CutGrid.Result();
            if (s.CutTurn || s.MvGlobal || s.BenchSecs > 0)
            {
                giroGrid.Compensate = s.CutTurn; giroGrid.Global = s.MvGlobal; giroGrid.CompThreshold = s.CutTurnThr;
                r = giroGrid.Next(src, pitch, w, h, bpp, fmt == Shaders.FmtYuy2);
                giroOk = s.MvGlobal && r.GOk; giroX = (float)r.Gx; giroY = (float)r.Gy;
                if (s.BenchSecs > 0)
                {
                    // en el banco, la cuenta exacta: el corte de siempre (con medias, = corte_medias=1) y el de ahora
                    giroBenchN++;
                    if (r.CutOld) giroBenchOld++;
                    if (s.CutTurn ? r.Cut : r.CutOld) giroBenchCut++;
                    if (r.CutOld) Log.Write("Cortes (banco): de siempre " + giroBenchOld + ", ahora " + giroBenchCut + " de " + giroBenchN + " imagenes");
                }
            }
            bool test = s.CutTest > 0 && ++cutTestN % s.CutTest == 0;   // corte_prueba=N
            if (!s.CutTurn && !test) return false;   // el corte de siempre (Engine.cs)
            cut = test || r.Cut;
            if (cut)
            {
                cutCount++;
                if (Clock.SinceMs(cutLogAt) > 5000)
                {
                    Log.Write("Corte de escena (" + cutCount + " desde el anterior aviso; luma media cambia " + r.Old.ToString("0", CultureInfo.InvariantCulture) +
                        ", con el mejor desplazamiento " + r.Comp.ToString("0", CultureInfo.InvariantCulture) + (test ? "; corte_prueba" : "") + ")");
                    cutLogAt = Clock.Now; cutCount = 0;
                }
            }
            else if (r.CutOld)
            {
                giroNotCut++;
                if (r.Old > giroWorstOld) { giroWorstOld = r.Old; giroWorstComp = r.Comp; }
                if (Clock.SinceMs(giroLogAt) > 5000)
                {
                    Log.Write(string.Format(CultureInfo.InvariantCulture, "Giro de camara, no es corte ({0} desde el anterior aviso; luma media cambia hasta {1:0}, con el giro ({2:0}, {3:0}) px queda {4:0}; corte_giro_umbral {5:0})",
                        giroNotCut, giroWorstOld, r.Gx, r.Gy, giroWorstComp, s.CutTurnThr));
                    giroLogAt = Clock.Now; giroNotCut = 0; giroWorstOld = 0;
                }
            }
            return true;
        }

        // OnFrame (hilo de captura, bajo slotLock): el giro va con su ranura
        void GiroSlot(int slot)
        {
            slotGiroX[slot] = giroX; slotGiroY[slot] = giroY; slotGiroOk[slot] = giroOk; slotPassN[slot] = giroPassN;
        }

        // RenderFrame (hilo de pintado), con la imagen nueva: vale si es respecto a la anterior que se pinto (ninguna ranura perdida)
        void GiroTake(int slot)
        {
            if (slot < 0) return;
            int n = slotPassN[slot];
            mvGOk = s.MvGlobal && slotGiroOk[slot] && n == giroLastRendered + 1 && slotW[slot] > 0 && slotH[slot] > 0;
            giroLastRendered = n;
            if (mvGOk) { mvGx = slotGiroX[slot] * inW / slotW[slot]; mvGy = slotGiroY[slot] * inH / slotH[slot]; }
            // solo giros que la busqueda de siempre no alcanza bien (mvglobal_min px de la entrada): lo mas lento sigue como lo dejo REJAS
            if (mvGOk && mvGx * mvGx + mvGy * mvGy < s.MvGlobalMin * s.MvGlobalMin) mvGOk = false;
        }

        // SearchLevel (EngineMotion.cs): el giro en px del nivel k (c[2], c[3]: el shader calcula BX/BY de LW/LH); en el nivel grueso
        // tambien como candidato con +-mvglobal_radio (Radius >> 8)
        void GiroSearch(int k, uint* c)
        {
            // mvlargo_grueso=1: mvlargo cuenta tambien desde el vector del nivel grueso (Neighbors & 64). Con mvlargo=0.008 y mvlargo_rel=1
            // el castigo es por px del nivel y desde el vector de la imagen anterior: un giro que empieza de golpe (240 px a 4K = 120 de
            // castigo en el nivel fino) volvia a 0 en los niveles finos aunque el grueso lo hubiera encontrado. Medido (paneo de 240 px
            // a 4K, imagen de GTA): vectores a <4 px de la verdad 85 -> 92 %, error medio 3,9 -> 1,0 px; paralaje 1080p (REJAS) igual
            if (s.MvLongCoarse && k < Levels - 1) c[9] |= 64u;
            if (!s.MvGlobal || !mvGOk) return;
            float sc = 1f / (1 << k);
            c[2] = F(mvGx * sc); c[3] = F(mvGy * sc);
            c[9] |= 16u;
            if (k == Levels - 1 && s.MvGlobalRadius > 0) { c[9] |= 32u; c[4] |= (uint)s.MvGlobalRadius << 8; }
        }
    }
}
