using System;
using System.Threading.Tasks;

namespace ConsoleEnhancer.Motor
{
    // 23-11-2026 (1.2.4, SINMPV: sin mpv). Las capturas para el lanzador (switch2-grab: el OCR y la deteccion del menu de la consola, el
    // juego, "No Signal", el HDCP, el telon con la imagen congelada; switch2-burst: el diagnostico del tramado) EN COLOR, como el
    // screenshot-raw de mpv: hasta ahora, con la captura en YUY2 o NV12 (la de siempre de la GC573 a 4K / 1440p), el motor las daba EN GRIS
    // (solo la luma: B = G = R = Y, y sin pasar del rango limitado 16-235 al completo). Con mpv fuera, todas las capturas son del motor:
    //  - el nombre azul del HOME de la Switch 2 (se busca por el color) no se veia;
    //  - el telon "con la ultima imagen congelada" (y el del mando, atenuada) salia en blanco y negro;
    //  - la luma de los iconos / portadas y del criterio de "No Signal" no era la de mpv (sin el paso de 16-235 a 0-255).
    // YUY2 / NV12 -> BGR0 como mpv (y la sonda de la espera, src\Lanzador\EsperaSonda.cs): BT.709, rango limitado. Coma fija 8.8 (los
    // mismos redondeos dan +-1 frente a la cuenta en double). Cada punto con el croma de su pareja (4:2:2) o de su bloque de 2x2 (4:2:0),
    // como el escalado sin filtro de mpv para las capturas pequeñas. En el hilo de la cola de trabajos del motor, nunca en el de captura.
    // grab_gris=1 (motor_extra) = como antes (para comparar).
    static unsafe class GrabColor
    {
        public static bool Gray;   // grab_gris=1

        // Tablas BT.709 rango limitado: R = 1,1644 (Y-16) + 1,7927 (V-128); G = ... - 0,2132 (U-128) - 0,5329 (V-128); B = ... + 2,1124 (U-128)
        static readonly int[] TY = new int[256], TRv = new int[256], TGu = new int[256], TGv = new int[256], TBu = new int[256];
        static GrabColor()
        {
            for (int i = 0; i < 256; i++)
            {
                TY[i] = (int)Math.Round(1.164383 * (i - 16) * 256);
                TRv[i] = (int)Math.Round(1.792741 * (i - 128) * 256);
                TGu[i] = (int)Math.Round(-0.213249 * (i - 128) * 256);
                TGv[i] = (int)Math.Round(-0.532909 * (i - 128) * 256);
                TBu[i] = (int)Math.Round(2.112402 * (i - 128) * 256);
            }
        }

        static byte C(int v) { v = (v + 128) >> 8; return (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }

        public static void Pixel(int y, int u, int v, byte* q)
        {
            int l = TY[y];
            q[0] = C(l + TBu[u]);
            q[1] = C(l + TGu[u] + TGv[v]);
            q[2] = C(l + TRv[v]);
            q[3] = 0;
        }

        // Una fila de la captura (ya copiada) a BGR0. row = la fila de la imagen (YUY2: Y0 U Y1 V; NV12: el plano Y); uv = la fila de croma
        // de NV12 (U V entrelazados) que le toca; step = 1 de cada step puntos (la captura ligera)
        public static void Row(byte* row, byte* uv, int fmt, int bpp, int ow, int step, byte* q)
        {
            if (fmt == Shaders.FmtYuy2 && !Gray)
            {
                for (int x = 0; x < ow; x++, q += 4)
                {
                    int X = x * step;
                    byte* pr = row + (X & ~1) * 2;
                    Pixel(pr[(X & 1) * 2], pr[1], pr[3], q);
                }
                return;
            }
            if (fmt == Shaders.FmtNv12 && !Gray && uv != null)
            {
                for (int x = 0; x < ow; x++, q += 4)
                {
                    int X = x * step;
                    byte* pc = uv + (X & ~1);
                    Pixel(row[X], pc[0], pc[1], q);
                }
                return;
            }
            for (int x = 0; x < ow; x++, q += 4)
            {
                byte* px = row + x * step * bpp;
                if (fmt == Shaders.FmtBgr24 || fmt == Shaders.FmtBgrx32) { q[0] = px[0]; q[1] = px[1]; q[2] = px[2]; }
                else { q[0] = q[1] = q[2] = px[0]; }
                q[3] = 0;
            }
        }

        // Toda la imagen copiada (oh filas de rowBytes; con NV12, detras sus filas de croma: una por cada fila copiada) a BGR0. A partir de
        // ~1 millon de puntos (la entera de 1440p / 4K) en varios nucleos
        public static void Image(byte* raw, int rowBytes, int fmt, int bpp, int ow, int oh, int step, byte* dst)
        {
            long r0 = (long)raw, d0 = (long)dst;
            bool nv12 = fmt == Shaders.FmtNv12;
            long uvBase = r0 + (long)rowBytes * oh;
            if ((long)ow * oh < 1000000)
            {
                for (int y = 0; y < oh; y++)
                    Row((byte*)(r0 + (long)y * rowBytes), nv12 ? (byte*)(uvBase + (long)y * rowBytes) : null, fmt, bpp, ow, step, (byte*)(d0 + (long)y * ow * 4));
                return;
            }
            int n = Math.Max(2, Math.Min(6, Environment.ProcessorCount / 2));
            Parallel.For(0, n, delegate (int k)
            {
                int y0 = oh * k / n, y1 = oh * (k + 1) / n;
                for (int y = y0; y < y1; y++)
                    Row((byte*)(r0 + (long)y * rowBytes), nv12 ? (byte*)(uvBase + (long)y * rowBytes) : null, fmt, bpp, ow, step, (byte*)(d0 + (long)y * ow * 4));
            });
        }
    }
}
