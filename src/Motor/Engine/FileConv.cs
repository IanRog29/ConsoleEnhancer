using System;
using System.Threading.Tasks;

namespace ConsoleEnhancer.Motor
{
    // 23-11 (1.2.4 ronda 7, GAMEPLAY): de una imagen del fichero (Y + color en planos o entrelazado: y4m 4:2:0 / 4:2:2 / 4:4:4 / mono,
    // NV12, P010) a YUY2 al tamaño de la captura, como la GC573 con la PS5. Separable y en coma fija:
    //  - mismo tamaño: Y tal cual y el color 4:2:0 -> 4:2:2 interpolado en vertical (centros alineados: 3/4 + 1/4);
    //  - mas pequeño (4K -> 1440p / 1080p, la "verdad" del reescalado): AREA (cada punto de salida es la media exacta de lo que cubre),
    //    como una consola que saca a 1440p lo que pinta para 4K; filtro=bilineal para el de antes;
    //  - mas grande (1080p -> 4K): bilineal (lo de antes).
    // Lo usan el motor (FileSource) y las herramientas del banco de verdad (la verdad tiene que pasar por EXACTAMENTE lo mismo).
    public sealed unsafe class FileConv
    {
        public const int FiltroAuto = 0, FiltroBilineal = 1, FiltroArea = 2;
        readonly int inW, inH, cinW, cinH, outW, outH, coutW;
        readonly bool mono, fast;
        readonly Taps hY, vY, hC, vC;
        short[] tmpY, tmpU, tmpV;
        public readonly string Desc;

        sealed class Taps
        {
            public int[] Start, Count, Idx; public int[] W;   // W en 1/4096
            public bool Ident;
        }

        public FileConv(int inW, int inH, int csx, int csy, int outW, int outH, int filtro)
        {
            this.inW = inW; this.inH = inH; this.outW = outW; this.outH = outH;
            mono = csx == 0;
            cinW = mono ? 1 : (inW + csx - 1) / csx; cinH = mono ? 1 : (inH + csy - 1) / csy;
            coutW = outW / 2;
            hY = Make(inW, outW, filtro); vY = Make(inH, outH, filtro);
            hC = Make(cinW, coutW, filtro); vC = Make(cinH, outH, filtro);
            fast = !mono && inW == outW && inH == outH && csx == 2;
            string f = outW < inW || outH < inH ? (Kind(filtro, true) == FiltroArea ? "area" : "bilineal") : (outW == inW && outH == inH ? "" : "bilineal");
            Desc = inW + "x" + inH + " -> " + outW + "x" + outH + (f.Length > 0 ? " (" + f + ")" : "");
        }

        static int Kind(int filtro, bool down) { return filtro == FiltroAuto ? (down ? FiltroArea : FiltroBilineal) : filtro; }

        static Taps Make(int inN, int outN, int filtro)
        {
            Taps t = new Taps();
            t.Start = new int[outN]; t.Count = new int[outN];
            System.Collections.Generic.List<int> idx = new System.Collections.Generic.List<int>(), w = new System.Collections.Generic.List<int>();
            double k = inN / (double)outN;
            t.Ident = inN == outN;
            bool area = outN < inN && Kind(filtro, true) == FiltroArea;
            for (int i = 0; i < outN; i++)
            {
                t.Start[i] = idx.Count;
                if (t.Ident) { idx.Add(i); w.Add(4096); }
                else if (area)
                {
                    double a = i * k, b = (i + 1) * k;
                    int j0 = (int)Math.Floor(a), j1 = Math.Min(inN - 1, (int)Math.Ceiling(b) - 1);
                    int sum = 0, best = -1, bestW = -1;
                    for (int j = j0; j <= j1; j++)
                    {
                        double ov = Math.Min(b, j + 1) - Math.Max(a, j);
                        if (ov <= 1e-9) continue;
                        int ww = (int)Math.Round(ov / k * 4096);
                        idx.Add(j); w.Add(ww); sum += ww;
                        if (ww > bestW) { bestW = ww; best = w.Count - 1; }
                    }
                    if (best >= 0) w[best] += 4096 - sum;
                }
                else
                {
                    double x = (i + 0.5) * k - 0.5;
                    if (x < 0) x = 0;
                    int x0 = (int)x; if (x0 > inN - 1) x0 = inN - 1;
                    int x1 = Math.Min(inN - 1, x0 + 1);
                    int w1 = (int)Math.Round((x - x0) * 4096);
                    if (w1 <= 0 || x1 == x0) { idx.Add(x0); w.Add(4096); }
                    else { idx.Add(x0); w.Add(4096 - w1); idx.Add(x1); w.Add(w1); }
                }
                t.Count[i] = idx.Count - t.Start[i];
            }
            t.Idx = idx.ToArray(); t.W = w.ToArray();
            return t;
        }

        // Y: primer punto, pitch en bytes y paso entre puntos (1; 2 en P010 con Y apuntando al byte alto); U / V: igual con su paso
        // (planos: 1; NV12: 2 con V = U + 1; P010: 4 con U = UV + 1, V = UV + 3). dst: YUY2 de outW x outH con dstPitch.
        public void Run(byte* Y, int pitchY, int ystep, byte* U, byte* V, int pitchC, int cstep, byte* dst, int dstPitch)
        {
            IntPtr yp = (IntPtr)Y, up = (IntPtr)U, vp = (IntPtr)V, dp = (IntPtr)dst;
            if (fast)
            {
                // mismo tamaño: Y tal cual; color: mismo ancho (4:2:x -> 4:2:2), en vertical 4:2:0 -> 4:2:2 con 3/4 + 1/4 (o tal cual si 4:2:2)
                int w = outW, hh = outH, ch = cinH, cw = coutW;
                bool v420 = cinH * 2 >= inH - 1 && cinH < inH;
                Parallel.For(0, hh, delegate (int y)
                {
                    byte* yr = (byte*)yp + (long)y * pitchY;
                    byte* o = (byte*)dp + (long)y * dstPitch;
                    if (ystep == 1) { for (int x = 0; x < w; x++) o[x * 2] = yr[x]; }
                    else { for (int x = 0; x < w; x++) o[x * 2] = yr[x * ystep]; }
                    if (!v420)
                    {
                        byte* ur = (byte*)up + (long)y * pitchC, vr = (byte*)vp + (long)y * pitchC;
                        for (int c = 0; c < cw; c++) { o[c * 4 + 1] = ur[c * cstep]; o[c * 4 + 3] = vr[c * cstep]; }
                    }
                    else
                    {
                        int c0 = y >> 1, c1 = (y & 1) == 0 ? c0 - 1 : c0 + 1;
                        if (c1 < 0) c1 = 0; if (c1 > ch - 1) c1 = ch - 1;
                        byte* u0 = (byte*)up + (long)c0 * pitchC, u1 = (byte*)up + (long)c1 * pitchC;
                        byte* v0 = (byte*)vp + (long)c0 * pitchC, v1 = (byte*)vp + (long)c1 * pitchC;
                        for (int c = 0; c < cw; c++)
                        {
                            int i = c * cstep;
                            o[c * 4 + 1] = (byte)((u0[i] * 3 + u1[i] + 2) >> 2);
                            o[c * 4 + 3] = (byte)((v0[i] * 3 + v1[i] + 2) >> 2);
                        }
                    }
                });
                return;
            }
            // general: horizontal a un intermedio (x16) y luego vertical; con 2 puntos por salida (bilineal, area de 1,5x y 2x: lo de siempre)
            // por punteros y sin buscar en tablas en el bucle de dentro (1080p -> 4K y 4K -> 1440p costaban 3-5 nucleos)
            if (tmpY == null) { tmpY = new short[(long)inH * outW]; if (!mono) { tmpU = new short[(long)cinH * coutW]; tmpV = new short[(long)cinH * coutW]; } }
            fixed (short* ty0 = tmpY) fixed (short* tu0 = tmpU) fixed (short* tv0 = tmpV)
            {
                IntPtr ty = (IntPtr)ty0, tu = (IntPtr)tu0, tv = (IntPtr)tv0;
                Pass1(yp, pitchY, ystep, inH, outW, hY, ty);
                if (!mono) { Pass1(up, pitchC, cstep, cinH, coutW, hC, tu); Pass1(vp, pitchC, cstep, cinH, coutW, hC, tv); }
                int ow = outW, cow = coutW;
                Taps vy = vY, vc = vC;
                bool mn = mono;
                Parallel.For(0, outH, delegate (int y)
                {
                    byte* o = (byte*)dp + (long)y * dstPitch;
                    VRow((short*)ty, ow, vy, y, o, 2, 0);
                    if (mn) { for (int c = 0; c < cow; c++) { o[c * 4 + 1] = 128; o[c * 4 + 3] = 128; } return; }
                    VRow((short*)tu, cow, vc, y, o, 4, 1);
                    VRow((short*)tv, cow, vc, y, o, 4, 3);
                });
            }
        }

        // una fila de salida en vertical: de las filas del intermedio a dst[x * paso + desplazamiento]
        static void VRow(short* t, int ow, Taps v, int y, byte* o, int step, int off)
        {
            int s0 = v.Start[y], n = v.Count[y];
            byte* d = o + off;
            if (n == 1)
            {
                short* a = t + (long)v.Idx[s0] * ow;
                for (int x = 0; x < ow; x++) { int q = (a[x] + 8) >> 4; d[x * step] = (byte)(q > 255 ? 255 : q); }
                return;
            }
            if (n == 2)
            {
                short* a = t + (long)v.Idx[s0] * ow, b = t + (long)v.Idx[s0 + 1] * ow;
                int wa = v.W[s0], wb = v.W[s0 + 1];
                for (int x = 0; x < ow; x++) { int q = (wa * a[x] + wb * b[x] + 32768) >> 16; d[x * step] = (byte)(q > 255 ? 255 : q); }
                return;
            }
            for (int x = 0; x < ow; x++)
            {
                int acc = 0;
                for (int k = 0; k < n; k++) acc += v.W[s0 + k] * t[(long)v.Idx[s0 + k] * ow + x];
                int q = (acc + 32768) >> 16; d[x * step] = (byte)(q > 255 ? 255 : q);
            }
        }

        static void Pass1(IntPtr src, int pitch, int step, int rows, int ow, Taps h, IntPtr tmp)
        {
            int[] st = h.Start, cn = h.Count, ix = h.Idx, wt = h.W;
            bool two = true; for (int x = 0; x < ow && two; x++) if (cn[x] > 2) two = false;
            // (2 puntos: tablas planas i0 / i1 / w0 / w1)
            int[] i0 = new int[ow], i1 = new int[ow], w0 = new int[ow], w1 = new int[ow];
            if (two) for (int x = 0; x < ow; x++) { i0[x] = ix[st[x]] * step; w0[x] = wt[st[x]]; if (cn[x] > 1) { i1[x] = ix[st[x] + 1] * step; w1[x] = wt[st[x] + 1]; } else { i1[x] = i0[x]; w1[x] = 0; } }
            fixed (int* pi0 = i0) fixed (int* pi1 = i1) fixed (int* pw0 = w0) fixed (int* pw1 = w1)
            {
                IntPtr a0 = (IntPtr)pi0, a1 = (IntPtr)pi1, b0 = (IntPtr)pw0, b1 = (IntPtr)pw1;
                Parallel.For(0, rows, delegate (int r)
                {
                    byte* s = (byte*)src + (long)r * pitch;
                    short* o = (short*)tmp + (long)r * ow;
                    if (h.Ident) { for (int x = 0; x < ow; x++) o[x] = (short)(s[x * step] << 4); return; }
                    if (two)
                    {
                        int* j0 = (int*)a0; int* j1 = (int*)a1; int* v0 = (int*)b0; int* v1 = (int*)b1;
                        for (int x = 0; x < ow; x++) o[x] = (short)((v0[x] * s[j0[x]] + v1[x] * s[j1[x]] + 128) >> 8);
                        return;
                    }
                    for (int x = 0; x < ow; x++)
                    {
                        int s0 = st[x], n = cn[x], acc = 0;
                        for (int t = 0; t < n; t++) acc += wt[s0 + t] * s[ix[s0 + t] * step];
                        o[x] = (short)((acc + 128) >> 8);
                    }
                });
            }
        }

        // ---- El numero de la imagen en codigo Gray (16 cuadros de 64 px a 4K, como Parallax.DrawCode: las herramientas de medida
        // FGCAL / FGMULTI y las del banco de verdad lo leen ahi). Sobre YUY2 (Y 230 / 20, color 128). arriba = en la fila de arriba
        // (y = 16 a 4K) en vez de abajo (y = 2080; abajo a la izquierda suele estar el minimapa del juego)
        public const int CodeX = 128, CodeYAbajo = 2080, CodeYArriba = 16, CodeSize = 64, CodeBits = 16;
        public static uint Gray(long n) { uint v = (uint)n; return v ^ (v >> 1); }
        public static long FromGray(uint g) { uint v = g; for (uint s = g >> 1; s != 0; s >>= 1) v ^= s; return v; }

        public static void StampYuy2(byte* dst, int pitch, int w, int h, long n, bool arriba)
        {
            uint g = Gray(n) & 0xFFFF;
            double k = w / 3840.0;
            int cy = arriba ? CodeYArriba : CodeYAbajo;
            int y0 = (int)Math.Round(cy * k), y1 = Math.Min(h, (int)Math.Round((cy + CodeSize) * k));
            for (int b = 0; b < CodeBits; b++)
            {
                int x0 = (int)Math.Round((CodeX + b * CodeSize) * k), x1 = (int)Math.Round((CodeX + (b + 1) * CodeSize) * k);
                byte v = ((g >> b) & 1) != 0 ? (byte)230 : (byte)20;
                for (int y = y0; y < y1; y++)
                {
                    byte* row = dst + (long)y * pitch;
                    for (int x = x0 + 1; x < x1 - 1 && x < w; x++) { row[x * 2] = v; row[(x & ~1) * 2 + 1] = 128; row[(x & ~1) * 2 + 3] = 128; }
                }
            }
        }

        // lectura del codigo en una imagen de luma (w x h, pitch): 16 valores 0-255 (centro de cada cuadro)
        public static void ReadCode(byte* luma, int pitch, int w, int h, bool arriba, byte[] code16)
        {
            double k = w / 3840.0;
            int cy = arriba ? CodeYArriba : CodeYAbajo;
            int y = (int)Math.Round((cy + CodeSize / 2) * k);
            if (y >= h) y = h - 1;
            for (int b = 0; b < CodeBits; b++)
            {
                int x = (int)Math.Round((CodeX + b * CodeSize + CodeSize / 2) * k);
                code16[b] = luma[(long)y * pitch + x];
            }
        }
    }
}
