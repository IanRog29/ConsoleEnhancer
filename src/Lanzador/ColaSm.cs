using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace ConsoleEnhancer
{
    // 18-11 (LATENCIA): la cola de Smooth Motion con Magpie (DLSS 5 encendido), sobre todo en el modo 30 (el 30x2 que mas se usa).
    // Medido (banco con la fuente de latencia.exe, 1080p, DLSS 5 nivel 2, tele a 120 Hz, PresentMon de Magpie.exe): Magpie presenta cada
    // 33,37 ms (29,97) y la tele saca cada imagen suya en 4 refrescos (inventada + real, 33,33 ms). ~1-1,5 s despues de empezar a
    // presentar, Magpie mete 3 imagenes en ~32 ms (su ritmo se pone al dia): la cola del driver crece 2-3 imagenes y solo baja 1 ms por
    // segundo. De presentar a verse: 1 pasada 105-123 ms al empezar (bajando a 85 en 40 s), 3 pasadas 57 -> 28; de punta a punta
    // (latencia.exe) 115-162 ms. Con la imagen quieta Smooth Motion no inventa y la tele saca cada imagen en 2 refrescos: la cola baja
    // el doble de rapido. Una pausa de mpv (la imagen que Magpie recoge, quieta) de 159 ms a mitad: de 105 a 49 ms de golpe.
    // Lo que no sirve (medido): el limitador de NVIDIA de Magpie.exe (0x10835002) a 30 deja a Magpie en 15 imagenes reales (cuenta las
    // inventadas) y a 60 lo desordena (43 presentaciones/s y paradas).
    // Con magpie_sm_vaciar_ms=250 (2,5 s tras quitar el telon; tele a 60 Hz, 1080p, nivel 2): 1 pasada de presentar a verse 127 -> 43 ms,
    // de punta a punta 191 -> 74 ms; 3 pasadas 78 -> 30 ms (por 5 s: 138 / 135 / 108 -> 27 / 23 / 35). A cambio, la imagen se para 0,25 s
    // una vez, 2,5 s despues de verse Magpie. 0 = como antes
    partial class TrayApp
    {
        int smDrainSeq;

        // FinishLsStart (Magpie ya presenta y el telon fuera): con Smooth Motion, una pausa de mpv de magpie_sm_vaciar_ms pasados
        // magpie_sm_vaciar_tras_ms (despues de las 3 imagenes juntas del arranque de Magpie)
        void SmDrainAfterStart()
        {
            // (solo el modo 30: en el 60 con Smooth Motion G1 midio 20-24 ms de presentar a verse, sin esta cola; sin medir con esto)
            if (cfg.SmDrainMs <= 0 && !cfg.SmDrainAuto && !cfg.SmDrainReal || !cfg.SmoothNow || !cfg.CapMode) return;
            int seq = Interlocked.Increment(ref smDrainSeq);
            int ms = cfg.SmDrainMs, delay = cfg.SmDrainDelayMs;
            Thread t = new Thread(delegate ()
            {
                Thread.Sleep(delay);
                if (seq != smDrainSeq || userExit || Config.EngineActive || !ipcUp || !MagpieLive.IsScaling()) return;
                if (ms <= 0) { }
                else if (cfg.SmDrainSplit)
                {
                    // (magpie_sm_vaciar_partido=1: la misma pausa en trozos de 1 imagen separados 1 s)
                    int n = Math.Max(1, (int)Math.Round(ms / 33.4));
                    for (int i = 0; i < n && seq == smDrainSeq && !userExit && ipcUp; i++) { if (i > 0) Thread.Sleep(1000); SmDrainPause(34, "magpie_sm_vaciar_ms partido, " + (i + 1) + "/" + n); }
                }
                else SmDrainPause(ms, "magpie_sm_vaciar_ms");
                // (magpie_sm_vaciar_auto=1: despues, vigilar si la cola vuelve a crecer)
                if (cfg.SmDrainReal) SmRealWatch(seq);   // (src\ColaReal.cs: con la cola real, PresentMon)
                else if (cfg.SmDrainAuto) SmDrainWatch(seq);
            });
            t.IsBackground = true;
            t.Name = "vaciar cola SM";
            t.Start();
        }

        // 19-11 (LATENCIA3): con la tele a 120 Hz la cola SI se queda: de presentar a verse 92-114 ms al empezar, bajando ~1 ms/s (51-59 ms
        // al minuto); de punta a punta 158-169 ms (1440p, 1 pasada, DLSS 5 nivel 2). Smooth Motion con la sincronizacion cada 2 refrescos saca
        // justo Hz/4 reales/s (30,00) y la fuente da 29,97: la cola solo baja 0,03 imagenes/s; cuando Magpie se para mas de lo que hay en cola
        // (un tiron) y luego se pone al dia, crece. Una pausa de mpv de 150 ms la vacia entera (102-118 ms de punta a punta) pero con la cola
        // a 0 los parones de Magpie se ven (tirones 0-3,6 -> 6-7/min).
        // magpie_sm_vaciar_auto=1: despues de la pausa del arranque cuenta la cola con el log de Magpie ("Output cadence ... captureId=N"
        // cada 4 s: imagenes presentadas) frente a los refrescos de la tele (Hz exactos de DWM): cola = max(0, cola + presentadas - Hz/4 *
        // tiempo), y cuando pasa de magpie_sm_vaciar_objetivo + magpie_sm_vaciar_umbral la deja en el objetivo con una pausa de mpv.
        // OJO (medido 22:28): la cola del ARRANQUE no se ve asi (Magpie presenta 120 en 4,0 s justos desde la primera: la cola sale de que la
        // tele empieza a sacar tarde), por eso la pausa del arranque es fija (magpie_sm_vaciar_ms); esto solo ve la que crece por parones.
        // magpie_sm_vaciar_partido=1: las pausas en trozos de 1 imagen separados 1 s
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]   // (DWM_TIMING_INFO, pshpack1: 292 bytes)
        struct DwmTiming
        {
            public int cbSize; public uint rateRefreshNum, rateRefreshDen; public ulong qpcRefreshPeriod;
            public uint rateCompose_n, rateCompose_d; public ulong qpcVBlank, cRefresh;   // (44 bytes; el resto, 248)
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray, SizeConst = 248)] public byte[] rest;
        }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DwmTiming t);

        // Hz exactos de la pantalla (DWM; 0 = no se sabe)
        static double ExactHz()
        {
            try
            {
                DwmTiming t = new DwmTiming();
                t.rest = new byte[248];
                t.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(DwmTiming));
                if (DwmGetCompositionTimingInfo(IntPtr.Zero, ref t) != 0 || t.rateRefreshDen == 0) return 0;
                return (double)t.rateRefreshNum / t.rateRefreshDen;
            }
            catch { return 0; }
        }

        void SmDrainPause(int ms, string why)
        {
            Stopwatch sw = Stopwatch.StartNew();
            lock (cmdQueue) cmdQueue.Add(JsonCmd("set", "pause", "yes"));
            Thread.Sleep(ms);
            lock (cmdQueue) cmdQueue.Add(JsonCmd("set", "pause", "no"));
            Log.Write("Smooth Motion de Magpie: pausa de mpv de " + sw.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) +
                      " ms para vaciar la cola de la tele (" + why + ")");
        }

        static readonly System.Text.RegularExpressions.Regex reSmCad = new System.Text.RegularExpressions.Regex(
            @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\|.*Output cadence .*?captureId=(\d+)");

        static bool SmTime(string s, out DateTime t)
        {
            return DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
        }

        void SmDrainWatch(int seq)
        {
            double hz = ExactHz();
            if (hz <= 0) hz = Pantalla.Hz() == 119 ? 119.88 : Pantalla.Hz();   // (sin DWM: los Hz enteros)
            if (hz < 100) { Log.Write("Smooth Motion de Magpie: vaciado automatico no (pantalla a " + hz.ToString("0.00", CultureInfo.InvariantCulture) + " Hz)"); return; }
            double rate = hz / 4;   // reales por segundo que saca la tele (cada 2 refrescos, inventada + real)
            double target = cfg.SmDrainTarget, over = cfg.SmDrainThreshold;
            long pos = LogLength(Paths.MagpieLog);   // (desde ahora: la cola recien vaciada hasta el objetivo)
            double q = target; DateTime last = DateTime.MinValue; long lastId = 0; int drains = 0, wins = 0;
            while (seq == smDrainSeq && !userExit && !Config.EngineActive && ipcUp && MagpieLive.IsScaling())
            {
                long len = LogLength(Paths.MagpieLog);
                if (len < pos) { pos = 0; last = DateTime.MinValue; }
                if (len > pos)
                {
                    string text = LogSince(Paths.MagpieLog, pos);
                    int nl = text.LastIndexOf('\n');
                    if (nl >= 0)
                    {
                        pos += System.Text.Encoding.UTF8.GetByteCount(text.Substring(0, nl + 1));
                        foreach (string raw in text.Substring(0, nl).Split('\n'))
                        {
                            string line = raw.TrimEnd('\r');
                            DateTime t;
                            System.Text.RegularExpressions.Match m = reSmCad.Match(line);
                            if (!m.Success || !SmTime(m.Groups[1].Value, out t)) continue;
                            long id = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                            if (last != DateTime.MinValue && id >= lastId)
                            {
                                double dt = (t - last).TotalSeconds;
                                if (dt > 0 && dt < 30) q = Math.Max(0, q + (id - lastId) - rate * dt);
                            }
                            last = t; lastId = id;
                            wins++;
                            if (cfg.SmDrainLog || wins % 8 == 0)
                                Log.Write("Smooth Motion de Magpie: cola estimada " + q.ToString("0.0", CultureInfo.InvariantCulture) + " imagenes (captura " + id +
                                          ", " + rate.ToString("0.000", CultureInfo.InvariantCulture) + " reales/s en la tele)");
                        }
                    }
                }
                if (lastId > 0 && q >= target + over && seq == smDrainSeq)
                {
                    int n = (int)Math.Round(q - target);
                    drains++;
                    string why = "automatico: ~" + q.ToString("0.0", CultureInfo.InvariantCulture) + " imagenes en cola, se deja en " +
                                 target.ToString("0", CultureInfo.InvariantCulture) + ", vez " + drains;
                    if (cfg.SmDrainSplit)
                        for (int i = 0; i < n && seq == smDrainSeq && !userExit; i++) { if (i > 0) Thread.Sleep(1000); SmDrainPause((int)Math.Round(1000 / rate), why + " (" + (i + 1) + "/" + n + ")"); }
                    else SmDrainPause((int)Math.Round(n * 1000 / rate), why);
                    // (la ventana de Magpie con la pausa ya esta descontada: se vuelve a contar desde la siguiente)
                    last = DateTime.MinValue;
                }
                Thread.Sleep(500);
            }
        }
    }
}
