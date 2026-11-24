// EngineFgEnhancedMando.cs - (1.3.3, decima ronda, EFGLAT) Enhanced FG baja latencia: el STICK DERECHO del mando como prediccion del giro.
//
// La baja latencia adivina el movimiento de la camara hasta 25 ms por delante de la ultima real suponiendo que sigue igual: al empezar,
// frenar o cambiar un giro se equivoca (es casi todo el error que queda: notas\EFGLAT.md). El mando que ve el PC (XInput) se adelanta a
// la imagen: la consola tarda 1-3 imagenes en ensenar lo que el stick ya ha pedido. Si ESE mando es el que mueve la camara (no se sabe:
// puede ser otro), su stick derecho casa con el global medido de cada pareja con un retraso y una ganancia que se aprenden en marcha:
//   - un hilo lee XInput cada 4 ms (el primer mando conectado; cada segundo si no hay ninguno) y guarda el stick derecho con su hora;
//   - con cada pareja (el global de efg_giro leido de la grafica, una pareja tarde) se ajusta, para 10 retrasos de 0 a 150 ms, global =
//     ganancia x stick medio de la pareja (por eje, con olvido): el retraso que mejor casa y su r2;
//   - efg_lat_mando=2 y r2 >= efg_lat_mando_r2 (0,6) en el eje: cada inventada corrige el global de lo que va con la camara con
//     ganancia x (stick a la hora de la inventada - retraso  -  stick medio de la pareja - retraso), con tope. Si no casa, nada (como antes).
//   - efg_lat_mando=1: solo aprende y lo dice en el registro cada 10 s (para saber, con una partida, si el mando del PC es el de
//     la consola); 0 (de serie, INTEGRA10) = ni lee el mando.
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConsoleEnhancer.Motor
{
    partial class Settings
    {
        public int EfgLatMando = 0;           // efg_lat_mando (1.3.3: 0 de serie, la PS5 / Switch 2 se juegan con su mando): 0 = no, 1 = solo aprender y registro, 2 = usarlo en la baja latencia
        public float EfgLatMandoR2 = 0.6f;    // efg_lat_mando_r2: lo que tiene que casar (r2) para usarlo

        bool EfgMandoKey(string key, string val)
        {
            switch (key)
            {
                case "efg_lat_mando": int v; if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) EfgLatMando = Math.Max(0, Math.Min(2, v)); return true;
                case "efg_lat_mando_r2": double d; if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) EfgLatMandoR2 = (float)Math.Max(0, Math.Min(1, d)); return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        [StructLayout(LayoutKind.Sequential)]
        struct XGamepadState { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
        delegate uint XGetStateFn(uint user, out XGamepadState st);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryW(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr mod, string name);

        const int MandoRing = 4096, MandoLags = 10, MandoPairs = 8;
        const double MandoLagStep = 16.667;
        readonly long[] mandoT = new long[MandoRing];
        readonly float[] mandoX = new float[MandoRing], mandoY = new float[MandoRing];
        long mandoN;                          // muestras escritas (el hilo escribe, el motor lee: la posicion se publica al final)
        Thread mandoThread;
        volatile bool mandoStop;
        volatile int mandoSlot = -1;
        readonly long[] mandoFitT = new long[MandoPairs];
        long mandoFits;
        // por retraso y eje: sumas con olvido de s, m, s2, m2, sm y peso
        readonly double[,,] mandoAcc = new double[MandoLags, 2, 6];
        int mandoLag = -1;
        float mandoGx, mandoGy, mandoR2x, mandoR2y;
        long mandoLogAt, mandoUsed, mandoGens;
        double mandoMoved, mandoSeen;

        void EfgMandoStart()
        {
            if (s.EfgLatMando <= 0 || mandoThread != null) return;
            mandoThread = new Thread(EfgMandoLoop) { IsBackground = true, Name = "Enhanced FG mando", Priority = ThreadPriority.AboveNormal };
            mandoThread.Start();
        }

        void EfgMandoLoop()
        {
            try
            {
                IntPtr mod = LoadLibraryW("xinput1_4.dll");
                if (mod == IntPtr.Zero) mod = LoadLibraryW("xinput9_1_0.dll");
                if (mod == IntPtr.Zero) { Log.Write("Enhanced FG mando: sin XInput"); return; }
                IntPtr fp = GetProcAddress(mod, "XInputGetState");
                if (fp == IntPtr.Zero) return;
                XGetStateFn get = (XGetStateFn)Marshal.GetDelegateForFunctionPointer(fp, typeof(XGetStateFn));
                long lastScan = 0;
                while (!mandoStop)
                {
                    int slot = mandoSlot;
                    XGamepadState st;
                    if (slot < 0 || get((uint)slot, out st) != 0)
                    {
                        mandoSlot = -1;
                        if (lastScan == 0 || Clock.SinceMs(lastScan) > 1000)
                        {
                            lastScan = Clock.Now;
                            for (uint u = 0; u < 4; u++) if (get(u, out st) == 0) { mandoSlot = (int)u; break; }
                        }
                        if (mandoSlot < 0) { Thread.Sleep(250); continue; }
                        get((uint)mandoSlot, out st);
                    }
                    long i = mandoN % MandoRing;
                    mandoT[i] = Clock.Now; mandoX[i] = Axis(st.RX); mandoY[i] = Axis(st.RY);
                    Interlocked.Increment(ref mandoN);
                    Thread.Sleep(4);
                }
            }
            catch (Exception ex) { Log.Write("Enhanced FG mando: " + ex.Message); }
        }

        static float Axis(short v)
        {
            float a = v / 32767f; float m = Math.Abs(a);
            if (m < 0.12f) return 0;
            return Math.Sign(a) * Math.Min(1f, (m - 0.12f) / 0.88f);
        }

        // el stick medio entre dos horas (false si no hay muestras que lo cubran)
        bool MandoMean(long t0, long t1, out float sx, out float sy)
        {
            sx = sy = 0;
            long n = Interlocked.Read(ref mandoN);
            if (n < 2) return false;
            long first = Math.Max(0, n - MandoRing + 1);
            if (mandoT[first % MandoRing] > t0 || mandoT[(n - 1) % MandoRing] < t1 - (long)(20 / Clock.TickMs)) return false;
            int c = 0; double ax = 0, ay = 0;
            for (long j = n - 1; j >= first; j--)
            {
                long tj = mandoT[j % MandoRing];
                if (tj < t0) break;
                if (tj > t1) continue;
                ax += mandoX[j % MandoRing]; ay += mandoY[j % MandoRing]; c++;
            }
            if (c == 0)
            {
                // ventana mas corta que el paso del hilo: la muestra mas cercana
                for (long j = n - 1; j >= first; j--) if (mandoT[j % MandoRing] <= t1) { sx = mandoX[j % MandoRing]; sy = mandoY[j % MandoRing]; return true; }
                return false;
            }
            sx = (float)(ax / c); sy = (float)(ay / c);
            return true;
        }

        // con cada pareja (desde EfgGiroFit): la hora de esta y el global (C2P, puntos de L0) de la ANTERIOR, leido ahora de la grafica
        void EfgMandoPair(float gx, float gy, bool ok)
        {
            if (s.EfgLatMando <= 0) return;
            EfgMandoStart();
            long now = Clock.Now;
            mandoFitT[mandoFits % MandoPairs] = now; mandoFits++;
            if (mandoFits < 3 || !ok) { EfgMandoLog(); return; }
            long t0 = mandoFitT[(mandoFits - 3) % MandoPairs], t1 = mandoFitT[(mandoFits - 2) % MandoPairs];   // la pareja de ese global
            if (Clock.Ms(t0, t1) > 200 || Math.Abs(gx) > 500 || Math.Abs(gy) > 500) { EfgMandoLog(); return; }
            const double keep = 0.995;
            bool any = false;
            for (int l = 0; l < MandoLags; l++)
            {
                long lag = (long)(l * MandoLagStep / Clock.TickMs);
                float sx, sy;
                if (!MandoMean(t0 - lag, t1 - lag, out sx, out sy)) continue;
                any = true;
                for (int a = 0; a < 2; a++)
                {
                    double sv = a == 0 ? sx : sy, mv = a == 0 ? gx : gy;
                    mandoAcc[l, a, 0] = keep * mandoAcc[l, a, 0] + sv; mandoAcc[l, a, 1] = keep * mandoAcc[l, a, 1] + mv;
                    mandoAcc[l, a, 2] = keep * mandoAcc[l, a, 2] + sv * sv; mandoAcc[l, a, 3] = keep * mandoAcc[l, a, 3] + mv * mv;
                    mandoAcc[l, a, 4] = keep * mandoAcc[l, a, 4] + sv * mv; mandoAcc[l, a, 5] = keep * mandoAcc[l, a, 5] + 1;
                }
                if (l == 0) { mandoSeen++; if (Math.Abs(sx) + Math.Abs(sy) > 0.2f) mandoMoved++; }
            }
            if (any)
            {
                // el retraso que mejor casa (r2 de los dos ejes, pesados por lo que se mueve la camara)
                double best = -1; int bl = -1; float bgx = 0, bgy = 0, brx = 0, bry = 0;
                for (int l = 0; l < MandoLags; l++)
                {
                    float gxl, gyl, r2x, r2y; double vx, vy;
                    if (!MandoFitAxis(l, 0, out gxl, out r2x, out vx) || !MandoFitAxis(l, 1, out gyl, out r2y, out vy)) continue;
                    double sc = (r2x * vx + r2y * vy) / Math.Max(1e-6, vx + vy);
                    if (sc > best) { best = sc; bl = l; bgx = gxl; bgy = gyl; brx = r2x; bry = r2y; }
                }
                mandoLag = bl; mandoGx = bgx; mandoGy = bgy; mandoR2x = brx; mandoR2y = bry;
            }
            EfgMandoLog();
        }

        bool MandoFitAxis(int l, int a, out float gain, out float r2, out double varM)
        {
            gain = 0; r2 = 0; varM = 0;
            double w = mandoAcc[l, a, 5];
            if (w < 60) return false;
            double ms = mandoAcc[l, a, 0] / w, mm = mandoAcc[l, a, 1] / w;
            double vs = mandoAcc[l, a, 2] / w - ms * ms, vm = mandoAcc[l, a, 3] / w - mm * mm, cv = mandoAcc[l, a, 4] / w - ms * mm;
            varM = Math.Max(0, vm);
            if (vs < 1e-4 || vm < 1e-3) return true;   // sin movimiento en ese eje: no casa, pero no descarta el retraso
            gain = (float)(cv / vs);
            r2 = (float)(cv * cv / (vs * vm));
            return true;
        }

        // la correccion del global de una inventada (en m de la eleccion: -C2P, puntos de L0 por pareja); false = nada
        bool EfgMandoDelta(float t, out float dx, out float dy)
        {
            dx = dy = 0;
            if (s.EfgLatMando < 2 || mandoLag < 0 || mandoFits < 2) return false;
            bool ux = mandoR2x >= s.EfgLatMandoR2, uy = mandoR2y >= s.EfgLatMandoR2;
            mandoGens++;
            if (!ux && !uy) return false;
            long t1 = mandoFitT[(mandoFits - 1) % MandoPairs], t0 = mandoFitT[(mandoFits - 2) % MandoPairs];
            long lag = (long)(mandoLag * MandoLagStep / Clock.TickMs);
            long len = t1 - t0;
            float px, py, ix, iy;
            if (!MandoMean(t0 - lag, t1 - lag, out px, out py)) return false;
            long mid = t1 + (long)((t - 1f) * 0.5f * len);
            long win = (long)(8 / Clock.TickMs);
            if (!MandoMean(mid - lag - win, Math.Min(mid - lag + win, Clock.Now), out ix, out iy)) return false;
            float cx = ux ? mandoGx * (ix - px) : 0, cy = uy ? mandoGy * (iy - py) : 0;
            // tope: lo que el stick entero pediria
            float cap = 2f * Math.Max(Math.Abs(mandoGx), Math.Abs(mandoGy));
            float l2 = (float)Math.Sqrt(cx * cx + cy * cy);
            if (l2 > cap && l2 > 0) { cx *= cap / l2; cy *= cap / l2; }
            dx = -cx; dy = -cy;   // C2P -> m
            mandoUsed++;
            return true;
        }

        void EfgMandoLog()
        {
            if (mandoLogAt != 0 && Clock.SinceMs(mandoLogAt) < 10000) return;
            mandoLogAt = Clock.Now;
            Log.Write("Enhanced FG mando (efg_lat_mando=" + s.EfgLatMando + "): " + (mandoSlot >= 0 ? "XInput " + mandoSlot : "sin mando XInput") +
                      ", stick derecho movido en " + (mandoSeen > 0 ? (100 * mandoMoved / mandoSeen).ToString("0", CultureInfo.InvariantCulture) : "-") + " % de las parejas; casa con la camara: " +
                      (mandoLag < 0 ? "aun no (pocas parejas)" : "r2 " + mandoR2x.ToString("0.00", CultureInfo.InvariantCulture) + " / " + mandoR2y.ToString("0.00", CultureInfo.InvariantCulture) +
                      " (x / y), retraso " + (mandoLag * MandoLagStep).ToString("0", CultureInfo.InvariantCulture) + " ms, ganancia " + mandoGx.ToString("0.0", CultureInfo.InvariantCulture) + " / " +
                      mandoGy.ToString("0.0", CultureInfo.InvariantCulture) + " puntos por pareja") + (s.EfgLatMando >= 2 ? "; usado en " + mandoUsed + " de " + mandoGens + " inventadas" : ""));
        }

        void EfgMandoStop() { mandoStop = true; }

        static uint Half2(float a, float b) { return HalfBits(a) | ((uint)HalfBits(b) << 16); }
        static uint HalfBits(float f)
        {
            // float -> half (redondeo a lo mas cercano; sin subnormales: lo que cabe aqui va de 0,001 a cientos)
            uint x = *(uint*)&f;
            uint sign = (x >> 16) & 0x8000u;
            int e = (int)((x >> 23) & 0xFF) - 127 + 15;
            uint m = x & 0x7FFFFFu;
            if (e <= 0) return sign;
            if (e >= 31) return sign | 0x7BFFu;
            uint h = sign | ((uint)e << 10) | (m >> 13);
            if ((m & 0x1000u) != 0) h++;
            return h;
        }
    }
}
