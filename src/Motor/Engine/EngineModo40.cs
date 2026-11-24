using System;
using System.Globalization;
using System.Threading;

namespace ConsoleEnhancer.Motor
{
    // EngineModo40.cs - 24-11-2026 (1.3.2, novena ronda, agente MODO40). El usuario: "y añade opcion de 40 fps".
    // MODO 40 (mode=40): los modos de 40 imagenes por segundo de los juegos de consola (los "equilibrados" de la PS5 / Xbox Series, que solo
    // existen con la consola a 120 Hz). Las reales son 39,96/s:
    //  - captura a 120 Hz (1440p / 1080p: la GC573 no da 4K a 120): cada imagen del juego dura 3 capturas; pasa una nueva de cada 3
    //    (Pass40, con el par 4-2 de una imagen que llega tarde y la copia de una nueva que llego antes de tiempo, como el filtro del 30);
    //  - captura a 60 Hz (4K, o la capturadora bajando a 60 la señal de 120 de la consola): el juego llega con la cadencia 2-1 (33,4 / 16,7
    //    ms). Pasan todas las nuevas (como mucho 2 de cada 3 capturas) y la que llega a 1 captura de la anterior se da con su llegada + media
    //    captura (M40Hora): las reales quedan parejas cada 25 ms, como las saca la consola (m40_reparto=1 de serie).
    //  La captura que llega de verdad se mide (m40CapMs): si se pidio 120 y llega a 60, va la regla del 60.
    // Presentacion sin generacion (y con Smooth Motion x2, que inventa entre dos presentaciones nuestras) (M40Espera, m40_ritmo=1 de serie):
    // cada real a su hora = su hora (la de M40Hora) + L, con L el p90 de lo que tarda la grafica en acabar las ultimas 32 (sube enseguida, baja
    // despacio): asi cada imagen dura 25 ms en la tele (3 refrescos a 120 Hz) aunque DLSS / DLSS 5 tarden distinto cada vez, y nunca menos de
    // m40_hueco (0,96) x 25 ms de la anterior. Con VRR la tele sigue a cada Present. Con Enhanced FG lo hace su hilo (x3 = 120, x2 = 80) con
    // esas mismas horas; x4 no hay en el modo 40 (160 > 120).
    // Claves: m40_reparto=0 (las de la cadencia 2-1 tal cual llegan), m40_ritmo=0 (sin generacion, cada real en cuanto esta, como el 30 / 60),
    // m40_hueco=0.96 (separacion minima entre dos reales, en intervalos del modo), m40_log=1 (cada 10 s al registro).
    partial class Settings
    {
        public bool M40Reparto = true;
        public bool M40Ritmo = true;
        public double M40Hueco = 0.96;
        public bool M40Log = false;
        public bool Is40 { get { return Mode == 40; } }

        bool ApplyModo40(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "m40_reparto": M40Reparto = B(val); return true;
                case "m40_ritmo": M40Ritmo = B(val); return true;
                case "m40_hueco": M40Hueco = Math.Max(0.5, Math.Min(1.0, D(val))); return true;
                case "m40_log": M40Log = B(val); return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        // Las imagenes reales por segundo del modo (las de la consola que se presentan)
        double ModoFpsJuego { get { return s.Mode == 30 ? 29.97 : s.Mode == 40 ? 39.96 : s.Mode >= 100 ? 119.88 : 59.94; } }
        // Lo que dura una real del modo (ms)
        double ModoPeriodoMs { get { return 1000.0 / ModoFpsJuego; } }
        // La captura pedida (fps=)
        double CapFpsPedida { get { return s.FpsNum > 0 ? s.FpsNum / (double)Math.Max(1, s.FpsDen) : 59.94; } }
        // reales del modo / captura pedida: 0,5 en el 30; 1/3 en el 40 a 120, 2/3 a 60; 1 en el 60 / 120
        double ModoFactor { get { return s.Mode == 30 ? 0.5 : s.Mode == 40 ? Math.Min(1.0, 39.96 / Math.Max(1.0, CapFpsPedida)) : 1; } }
        // ¿modo de pocas reales, con repetidas siempre fuera y el reparto de DLSS 5 / vectores del modo 30? (30 y 40)
        bool ModoLento { get { return s.Mode == 30 || s.Mode == 40; } }

        // ---- el filtro de repetidas del modo 40 (hilo de la captura, OnFrame)
        double m40CapMs;            // lo que tarda de verdad una captura (media movil)
        long m40LastCap;
        long m40LastGap = 3;        // capturas entre las dos ultimas que pasaron
        bool m40Unpassed;           // llego una nueva que no paso (su copia puede pasar)
        bool m40Forced;             // la ultima que paso era una copia sin nada nuevo (imagen quieta)
        long m40PacedPrev;          // la hora (M40Hora) de la ultima que paso
        bool m40LateCopy;           // la ultima que paso era la copia tarde de una nueva que no paso
        long m40Back = -1000;       // capN de la ultima vuelta a tiempo
        long m40NewA = -1000, m40NewB = -1000;   // capN de las dos ultimas nuevas que pasaron (a 60 Hz)
        long m40Pass, m40Early, m40Spaced, m40Copies, m40Backs;   // (registro)
        readonly double[] m40SlotDelay = new double[Slots];   // lo que M40Hora le sumo a la llegada de cada ranura (ms)
        double m40LastDelay;

        bool M40Cap120 { get { return m40CapMs > 0 ? m40CapMs < 12.0 : CapFpsPedida > 100; } }

        bool Pass40(bool isNew, long since, long arrival)
        {
            if (m40LastCap != 0)
            {
                double d = Clock.Ms(m40LastCap, arrival);
                if (d > 2 && d < 40) m40CapMs = m40CapMs <= 0 ? d : m40CapMs * 0.97 + d * 0.03;
            }
            m40LastCap = arrival;
            bool c120 = M40Cap120;
            int n = c120 ? 3 : 2;          // capturas de una real (a 60: 2 la de la cadencia 2-1 que dura 2)
            int par = c120 ? 6 : 3;        // dos reales en estas capturas como mucho (40 de media)
            int force = c120 ? 6 : 3;      // una copia a los 50 ms sin nada nuevo (imagen quieta)
            bool first = lastPassed < 0;
            if (m40Back > capN) m40Back = -1000;   // (capN vuelve a 0 al despertar de la espera)
            // la vuelta a tiempo: si la ultima que paso era la copia tarde de una nueva que no paso, la nueva siguiente pasa en cuanto llega
            // (como el filtro del 30: si no, cada nueva llega a 1 captura de la ultima copia que paso, tampoco pasa, pasa su copia y asi
            // SIEMPRE, todas 2 capturas tarde -medido: 16,7 ms de mas-). Como mucho una vez por segundo (un juego a 60 en el 40 no pasa de 40)
            bool back = isNew && m40LateCopy && capN - m40Back > (c120 ? 120 : 60);
            if (m40NewA > capN || m40NewB > capN) m40NewA = m40NewB = -1000;
            // a 60 Hz: toda nueva pasa salvo que hayan pasado ya 2 nuevas en las 2 capturas de antes (40 de media como mucho); las copias no
            // cuentan (con la regla de los huecos, si la primera que pasaba era una copia, la 2a nueva del 2-1 no pasaba nunca: pasaba tarde su copia)
            int nuevas2 = (capN - m40NewA <= 2 ? 1 : 0) + (capN - m40NewB <= 2 ? 1 : 0);
            bool pass;
            if (first) pass = true;
            else if (isNew) pass = c120 ? since >= n || since + m40LastGap >= par || m40Forced || back : nuevas2 < 2 || m40Forced;
            else pass = (m40Unpassed && since >= n) || since >= force;
            if (pass)
            {
                if (!isNew && !first) m40Copies++;
                if (back && !(since >= n || since + m40LastGap >= par || m40Forced)) { m40Back = capN; m40Backs++; }
                m40LateCopy = !isNew && m40Unpassed && !first;
                m40Forced = !isNew && !m40Unpassed && !first;
                m40Unpassed = false;
                m40LastGap = first ? n : since;
                if (isNew) { m40NewB = m40NewA; m40NewA = capN; }
                m40Pass++;
            }
            else if (isNew) m40Unpassed = true;
            return pass;
        }

        // La hora que se da a una real que pasa (en vez de su llegada): la temprana de la cadencia 2-1, media captura despues; y nunca a menos
        // de m40_hueco x 25 ms de la anterior (como poco: un par 4-2 o un tiron se reparte en las siguientes). Como mucho 1,5 capturas mas tarde
        long M40Hora(long arrival, long since, bool isNew)
        {
            m40LastDelay = 0;
            if (!s.M40Reparto) { m40PacedPrev = arrival; return arrival; }
            double cap = m40CapMs > 0 ? m40CapMs : 1000.0 / Math.Max(1.0, CapFpsPedida);
            double add = 0;
            if (!M40Cap120 && isNew && since == 1) { add = cap * 0.5; m40Early++; }
            double per = ModoPeriodoMs * s.M40Hueco;
            if (m40PacedPrev != 0)
            {
                double gap = Clock.Ms(m40PacedPrev, arrival) + add;
                if (gap < per && gap > -200) { add += per - gap; m40Spaced++; }
            }
            add = Math.Max(0, Math.Min(cap * 1.5, add));
            long t = arrival + (long)(add / Clock.TickMs);
            m40PacedPrev = t;
            m40LastDelay = add;
            return t;
        }

        // ---- la presentacion sin generacion (hilo de pintado, Run: tras mandar la imagen y antes de Present)
        readonly AutoResetEvent m40FenceEv = new AutoResetEvent(false);
        readonly double[] m40Win = new double[32];
        int m40WinN, m40WinI;
        double m40L;
        long m40Waits, m40Late, m40LogAt, m40Presents;
        double m40WaitSum;

        bool M40Pinta { get { return s.Mode == 40 && s.M40Ritmo && !s.NoPresent && !efgHold && !fmHold && !fgApplied && !(xeFgOn && xeFgEnabledNow); } }

        void M40Espera(int slot, long arrival)
        {
            if (slot < 0 || !M40Pinta) return;
            try
            {
                // la grafica acaba esta imagen (como mucho 40 ms: si no, se presenta ya)
                ulong v = fenceValue;
                if (fence.GetCompletedValue() < v)
                {
                    m40FenceEv.Reset();   // (una señal vieja de una espera que ya vencio no vale)
                    fence.SetEventOnCompletion(v, m40FenceEv.SafeWaitHandle.DangerousGetHandle());
                    m40FenceEv.WaitOne(40);
                }
                double delay = m40SlotDelay[slot];
                long real = arrival - (long)(delay / Clock.TickMs);
                double ready = Clock.SinceMs(real);
                if (ready < 0 || ready > 200) return;
                m40Win[m40WinI] = ready; m40WinI = (m40WinI + 1) % m40Win.Length; if (m40WinN < m40Win.Length) m40WinN++;
                double[] a = new double[m40WinN];
                Array.Copy(m40Win, a, m40WinN);
                Array.Sort(a);
                double p90 = a[Math.Min(m40WinN - 1, (int)(m40WinN * 0.9))];
                // sube enseguida, baja despacio
                m40L = p90 >= m40L ? p90 : m40L * 0.97 + p90 * 0.03;
                double L = Math.Min(m40L + 0.3, ModoPeriodoMs * 0.9);
                long target = arrival + (long)(L / Clock.TickMs);
                double wait = Clock.Ms(Clock.Now, target);
                m40Presents++;
                if (wait <= 0.2) { if (wait < -2) m40Late++; return; }
                if (wait > ModoPeriodoMs) wait = ModoPeriodoMs;
                long until = Clock.Now + (long)(wait / Clock.TickMs);
                while (true)
                {
                    double r = Clock.Ms(Clock.Now, until);
                    if (r <= 0) break;
                    if (r > 1.6) Thread.Sleep(1);
                    else Thread.SpinWait(200);
                }
                m40Waits++; m40WaitSum += wait;
            }
            catch (Exception ex) { Log.Error("Modo 40: espera", ex); s.M40Ritmo = false; }
        }

        // SecondTick: cada 10 s al registro con m40_log=1, y una vez al minuto sin el
        void M40Tick()
        {
            if (s.Mode != 40) return;
            if (m40LogAt == 0) { m40LogAt = Clock.Now; return; }
            if (Clock.SinceMs(m40LogAt) < (s.M40Log ? 10000 : 60000)) return;
            double secs = Clock.SinceMs(m40LogAt) / 1000.0;
            m40LogAt = Clock.Now;
            Log.Write(string.Format(CultureInfo.InvariantCulture,
                "Modo 40: captura {0:0.00} ms ({1}), pasan {2:0.0}/s ({3} copias, {11} vueltas a tiempo), tempranas de la cadencia 2-1 {4}, separadas {5}; sin generacion: L {6:0.0} ms, esperas {7} ({8:0.0} ms de media), tarde {9} de {10}",
                m40CapMs, M40Cap120 ? "120 Hz" : "60 Hz", m40Pass / Math.Max(0.1, secs), m40Copies, m40Early, m40Spaced, m40L, m40Waits,
                m40Waits > 0 ? m40WaitSum / m40Waits : 0, m40Late, m40Presents, m40Backs));
            m40Pass = m40Copies = m40Early = m40Spaced = m40Waits = m40Late = m40Presents = m40Backs = 0; m40WaitSum = 0;
        }
    }
}
