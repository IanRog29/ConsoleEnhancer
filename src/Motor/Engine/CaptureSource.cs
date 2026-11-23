using System;
using ConsoleEnhancer.Motor.Capture;

namespace ConsoleEnhancer.Motor
{
    // Puente entre el motor y el modulo de captura: la GC573 con DirectShow (Media Foundation no la abre: 0xC00D36B4)
    class CaptureSource : IFrameSource
    {
        public event FrameHandler Frame;
        readonly DsCapture cap;
        readonly string name;
        public string Name { get { return name; } }
        public volatile bool NoSignal;

        CaptureSource(DsCapture cap, string name) { this.cap = cap; this.name = name; }

        static CaptureFormat[] Prefs(string f)
        {
            switch (f)
            {
                case "rgb24": return new[] { CaptureFormat.RGB24 };
                case "rgb32": return new[] { CaptureFormat.RGB32 };
                case "yuy2": return new[] { CaptureFormat.YUY2 };
                case "nv12": return new[] { CaptureFormat.NV12 };
                case "mjpg": return new[] { CaptureFormat.MJPG };   // 23-11 (1.2.4, SINMPV): con el MJPEG Decompressor de Windows (DsCapture.cs)
                // RGB24 = 4:4:4 sin perdidas (como el mpv de siempre); si no hay, el resto
                // (23-11, SINMPV: y MJPEG la ultima: solo si ninguna sin comprimir llega a esos fps a ese tamaño -las USB 2.0 a 1080p-)
                default: return new[] { CaptureFormat.RGB24, CaptureFormat.RGB32, CaptureFormat.YUY2, CaptureFormat.NV12, CaptureFormat.MJPG };
            }
        }

        public static IFrameSource Open(Settings s)
        {
            CaptureOptions o = new CaptureOptions();
            o.DeviceName = s.Device;
            // 18-11 (MOTOR-CE): device_path= exacto, device= subcadena o la primera capturadora que no parezca virtual (EngineUniversal.cs)
            string dev = CaptureChoice.Apply(s, o);
            if (dev == null) return null;
            o.Width = s.CapW; o.Height = s.CapH;
            o.FpsNum = s.FpsNum; o.FpsDen = s.FpsDen;
            o.Preference = Prefs(s.CapFormat);
            o.DeliverOnSourceThread = true;     // sin el salto de hilo (17 us en vez de 43)
            o.NoSignalMs = 1000;
            DsCapture c = new DsCapture(o);
            CaptureSource src = new CaptureSource(c, "capturadora " + dev + " " + s.CapW + "x" + s.CapH + " (DirectShow)");
            c.OnFrame = src.OnCapFrame;
            c.OnEvent = src.OnCapEvent;
            return src;
        }

        void OnCapFrame(IntPtr data, int pitch, int width, int height, CaptureFormat format, long qpcArrival, long sampleTime100ns, long sequence)
        {
            int fmt;
            switch (format)
            {
                case CaptureFormat.RGB24: fmt = Shaders.FmtBgr24; break;
                case CaptureFormat.RGB32: case CaptureFormat.ARGB32: fmt = Shaders.FmtBgrx32; break;
                case CaptureFormat.YUY2: fmt = Shaders.FmtYuy2; break;
                case CaptureFormat.NV12: fmt = Shaders.FmtNv12; break;
                default: return;
            }
            NoSignal = false;
            DelayStat();
            FrameHandler f = Frame;
            if (f != null) f(data, pitch, width, height, fmt, qpcArrival, sequence);
        }

        // 18-11 (LATENCIA): de la marca de tiempo de la muestra (reloj del grafo) a la entrega al motor, una vez por apertura a las
        // 600 imagenes (~10 s): si el driver de la capturadora guarda imagenes en su cola, se ve aqui
        readonly double[] delays = new double[600];
        int delayN;
        void DelayStat()
        {
            if (delayN > delays.Length) return;
            FrameExtra fx = cap.CurrentFrame;
            if (!fx.HasClockDelay) return;
            if (delayN < delays.Length) { delays[delayN++] = fx.ClockDelay100ns / 10000.0; return; }
            delayN++;
            double[] a = (double[])delays.Clone();
            Array.Sort(a);
            Log.Write(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Captura: de la marca de tiempo de la muestra a la entrega {0:0.0} ms (p05 {1:0.0}, p95 {2:0.0}, max {3:0.0}; {4} imagenes)",
                a[a.Length / 2], a[a.Length / 20], a[a.Length * 19 / 20], a[a.Length - 1], a.Length));
        }

        void OnCapEvent(CaptureEvent ev, string detail)
        {
            if (ev == CaptureEvent.NoSignal) NoSignal = true;
            if (ev != CaptureEvent.StreamTick) Log.Write("Captura: " + ev + (string.IsNullOrEmpty(detail) ? "" : " " + detail));
        }

        // 16-11 (espera): tras Dispose, true = el hilo de captura ha terminado (grafo de DirectShow parado y soltado: otro proceso
        // puede abrir la capturadora). false = sigue vivo pasado su tiempo de parada (la capturadora puede seguir abierta)
        public bool Released { get { return !cap.IsRunning; } }

        public void Start() { cap.Start(); }
        public void Dispose() { try { cap.Stop(); } finally { cap.Dispose(); } }
    }
}
