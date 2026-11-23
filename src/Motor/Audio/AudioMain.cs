// AudioMain.cs - 23-11-2026 (1.2.4, SINMPV; el usuario: "Toda la app sera en el motor sin magpie ni nada del estilo"): el sonido de la consola
// sin mpv. ConsoleEnhancerEngine.exe --audio ... es un proceso aparte SOLO de sonido (ni ventana, ni grafica, ni Streamline), como lo era el
// mpv de audio de la app: asi la imagen (los motores que se relevan, se despiertan y se cierran) nunca corta el sonido, y un driver de la
// capturadora colgado no se lleva la imagen. Habla por su tuberia como mpv (src\Motor\Engine\MpvIpc.cs) lo que el lanzador le pedia al mpv
// de audio (AudioLink, src\Lanzador\Switch2Magpie.cs): get_property time-pos (lo que ha sonado, s: sin datos de la capturadora no avanza
// y el lanzador lo reabre), get_property demuxer-cache-duration (la cola, s), set_property speed (el lanzador: 1.004 con mucha cola), quit.
// Argumentos (clave=valor; los de mpv entre parentesis):
//   ipc=<tuberia> (--input-ipc-server)  datos=<carpeta> (audio.log)  audio_disp=<nombre> (dshow:audio=)  audio_pin=<nombre o Id>
//   (audio_pin_name)  audio_hz=48000 (sample_rate)  audio_bits=16 (sample_size)  audio_canales=8|2 (channels; 8 = mezcla a estereo)
//   audio_bloque_ms=20 (audio_buffer_size)  audio_cola_ms=30  audio_cola_max_ms=200  audio_salida_ms=10  audio_deriva=1
//   audio_duracion_s=N (--length: el asistente, prueba de 8 s)
// Solo pruebas, sin capturadora: audio_prueba=tono|clic[,ppm=N][,db=N] (un tono de 440 Hz o un clic por segundo de un generador con el
// ritmo de la capturadora -trozos de audio_bloque_ms- y su reloj desviado N partes por millon) y audio_medir=1 (mide en la propia salida
// de Windows, por loopback, cuanto tarda cada clic desde que "llega" de la capturadora hasta que suena: audio.log). C# 5.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConsoleEnhancer.Motor.Audio
{
    static class AudioMain
    {
        static AudioOut outp;
        static AudioDs cap;
        static volatile bool quit;
        static int exitCode;

        public static int Run(string[] args)
        {
            Dictionary<string, string> a = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in args)
            {
                int i = s.IndexOf('=');
                if (i > 0) a[s.Substring(0, i).Trim()] = s.Substring(i + 1).Trim();
            }
            string dir = Get(a, "datos", AppDomain.CurrentDomain.BaseDirectory);
            try { Directory.CreateDirectory(dir); } catch { }
            Log.Open(Path.Combine(dir, "audio.log"));
            Log.Write("---- ConsoleEnhancerEngine (sonido) " + string.Join(" ", args));
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }   // (mpv: --priority=abovenormal)
            outp = new AudioOut();
            outp.InRate = Int(a, "audio_hz", 48000);
            outp.InBits = Int(a, "audio_bits", 16);
            outp.InChannels = Int(a, "audio_canales", 2);
            outp.TargetMs = Math.Max(5, Math.Min(500, Int(a, "audio_cola_ms", 30)));
            outp.MaxMs = Math.Max(outp.TargetMs + 40, Int(a, "audio_cola_max_ms", 100));
            outp.BufferMs = Math.Max(3, Math.Min(200, Int(a, "audio_salida_ms", 10)));
            outp.Drift = Int(a, "audio_deriva", 1) != 0;
            outp.FillMs = Math.Max(0, Int(a, "audio_relleno_ms", 0));
            double vol;
            if (a.ContainsKey("audio_volumen") && double.TryParse(a["audio_volumen"], NumberStyles.Float, CultureInfo.InvariantCulture, out vol)) outp.Gain = vol;
            int blockMs = Math.Max(5, Math.Min(500, Int(a, "audio_bloque_ms", 20)));
            outp.BlockMs = blockMs;
            int secs = Int(a, "audio_duracion_s", 0);
            string ipc = Get(a, "ipc", "");
            MpvIpc pipe = null;
            if (ipc.Length > 0)
            {
                pipe = new MpvIpc(ipc, OnCommand);
                pipe.Start();
                Log.Write("Sonido: tuberia \\\\.\\pipe\\" + ipc);
            }
            Thread ot = new Thread(outp.Run);
            ot.IsBackground = true; ot.Name = "salida de sonido"; ot.Priority = ThreadPriority.Highest;
            ot.Start();
            string test = Get(a, "audio_prueba", "");
            Thread gen = null;
            Medidor med = null;
            if (test.Length > 0)
            {
                Generador g = new Generador(test, outp.InRate, outp.InChannels, outp.InBits, blockMs);
                g.OnBlock = delegate (IntPtr d, int n, long q) { outp.Push(d, n); };
                gen = new Thread(g.Run); gen.IsBackground = true; gen.Name = "generador"; gen.Priority = ThreadPriority.Highest;
                gen.Start();
                Log.Write("Sonido: fuente de prueba " + g.Desc);
                if (Int(a, "audio_medir", 0) != 0) { med = new Medidor(g); med.Start(); }
            }
            else
            {
                cap = new AudioDs();
                cap.Device = Get(a, "audio_disp", "");
                cap.Pin = Get(a, "audio_pin", "");
                cap.Rate = outp.InRate; cap.Bits = outp.InBits; cap.Channels = outp.InChannels; cap.BlockMs = blockMs;
                cap.OnBlock = delegate (IntPtr d, int n, long q) { outp.Push(d, n); };
                try
                {
                    cap.Start();
                    // lo que de verdad da (si fuese otra cosa que lo pedido, la cola lo lee con eso)
                    if (cap.GotBits > 0) outp.InBits = cap.GotBits;
                    if (cap.GotChannels > 0) outp.InChannels = cap.GotChannels;
                    if (cap.GotRate > 0 && cap.GotRate != outp.InRate) { Log.Write("Sonido: la capturadora da " + cap.GotRate + " Hz (pedido " + outp.InRate + ")"); outp.InRate = cap.GotRate; }
                    Log.Write("Sonido: capturadora " + cap.Opened);
                }
                catch (Exception ex)
                {
                    // (como mpv sin poder abrirlo: sale; el lanzador prueba otro pin o el formato de 2 canales)
                    Log.Write("Sonido: no se abre: " + ex.Message);
                    outp.Stop = true;
                    if (pipe != null) pipe.Stop();
                    return 2;
                }
            }
            Stopwatch sw = Stopwatch.StartNew();
            long lastLog = 0, lastIn = 0;
            while (!quit)
            {
                Thread.Sleep(100);
                if (secs > 0 && sw.ElapsedMilliseconds >= secs * 1000L) { Log.Write("Sonido: " + secs + " s (prueba): fin"); break; }
                long t = sw.ElapsedMilliseconds;
                if (t - lastLog >= (lastLog == 0 ? 5000 : 60000))
                {
                    lastLog = t;
                    long fin = outp.FramesIn;
                    Log.Write("Sonido: cola media " + outp.QueueAvgMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms (ahora " + outp.QueueNowMs.ToString("0.0", CultureInfo.InvariantCulture) +
                              "), velocidad " + outp.RatioNow.ToString("0.0000", CultureInfo.InvariantCulture) + (outp.ExternalSpeed != 1f ? " x " + outp.ExternalSpeed.ToString("0.000", CultureInfo.InvariantCulture) : "") +
                              ", entradas " + (fin - lastIn) + " tramas, sin datos " + outp.Underruns + " veces, tiradas " + outp.Dropped + " tramas" +
                              (med != null ? "; " + med.Resumen() : ""));
                    lastIn = fin;
                }
            }
            Log.Write("Sonido: al cerrar: " + outp.FramesIn + " tramas entradas, " + outp.FramesPlayed + " salidas, sin datos " + outp.Underruns + " veces, tiradas " + outp.Dropped + " tramas");
            if (med != null) { med.Stop = true; Log.Write("Sonido (medida): " + med.Resumen()); }
            outp.Stop = true;
            if (cap != null) { try { cap.Close(); } catch { } }
            if (pipe != null) pipe.Stop();
            Thread.Sleep(50);
            return exitCode;
        }

        static string Get(Dictionary<string, string> a, string k, string def) { string v; return a.TryGetValue(k, out v) ? v : def; }
        static int Int(Dictionary<string, string> a, string k, int def) { string v; int n; return a.TryGetValue(k, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : def; }

        // Lo de la tuberia (como el mpv de audio)
        static string OnCommand(List<string> c)
        {
            string name = c[0], p1 = c.Count > 1 ? c[1] : "", p2 = c.Count > 2 ? c[2] : "";
            if (name == "get_property" || name == "get_property_string")
            {
                if (p1 == "time-pos") return outp.FramesPlayed > 0 ? (outp.FramesPlayed / (double)outp.InRate).ToString("0.000###", CultureInfo.InvariantCulture) : null;
                if (p1 == "demuxer-cache-duration") return (outp.QueueMs / 1000.0).ToString("0.000###", CultureInfo.InvariantCulture);
                if (p1 == "speed") return outp.ExternalSpeed.ToString("0.000", CultureInfo.InvariantCulture);
                return null;
            }
            if (name == "set_property" || name == "set")
            {
                double v;
                if (p1 == "speed" && double.TryParse(p2, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0.9 && v < 1.1)
                {
                    outp.ExternalSpeed = (float)v;
                    Log.Write("Sonido: velocidad del lanzador " + v.ToString("0.000", CultureInfo.InvariantCulture) + " (cola " + outp.QueueMs.ToString("0", CultureInfo.InvariantCulture) + " ms)");
                }
                return null;
            }
            if (name == "quit") { int code; if (int.TryParse(p1, out code)) exitCode = code; quit = true; return null; }
            return null;
        }
    }

    // ---- solo pruebas: un generador con el ritmo de la capturadora (trozos de bloqueMs que "llegan" al acabar, reloj desviado ppm) ----
    sealed class Generador
    {
        public AudioBlock OnBlock;
        public string Desc;
        readonly bool clic;
        readonly int rate, ch, bits, blockMs;
        readonly double ppm, amp, jit;
        readonly Random rnd = new Random(7);
        public volatile bool Stop;
        // clics: la hora (QPC) a la que "sono" cada uno en la consola (el final de su trozo menos lo que le falta)
        public readonly List<long> Clics = new List<long>();
        public long Periodo;   // tramas entre clics

        public Generador(string spec, int rate, int ch, int bits, int blockMs)
        {
            this.rate = rate; this.ch = ch; this.bits = bits; this.blockMs = blockMs;
            string[] p = spec.Split(',');
            clic = p[0].Trim() == "clic";
            double ppmV = 0, db = clic ? -45 : -60, jv = 0;
            for (int i = 1; i < p.Length; i++)
            {
                string[] kv = p[i].Split('=');
                if (kv.Length != 2) continue;
                double v;
                if (!double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                if (kv[0].Trim() == "ppm") ppmV = v; else if (kv[0].Trim() == "db") db = v; else if (kv[0].Trim() == "jit") jv = v;
            }
            ppm = ppmV; amp = Math.Pow(10, db / 20); jit = jv;
            Periodo = rate;
            Desc = (clic ? "clic cada 1 s" : "tono de 440 Hz") + " a " + db.ToString("0", CultureInfo.InvariantCulture) + " dBFS, " + rate + " Hz " + bits + " bits " + ch +
                   " canales, trozos de " + blockMs + " ms, reloj " + (ppm >= 0 ? "+" : "") + ppm.ToString("0", CultureInfo.InvariantCulture) + " ppm" + (jit > 0 ? ", temblor hasta " + jit.ToString("0", CultureInfo.InvariantCulture) + " ms" : "");
        }

        public unsafe void Run()
        {
            int frames = rate * blockMs / 1000, bps = bits / 8;
            byte[] buf = new byte[frames * ch * bps];
            long t0 = Stopwatch.GetTimestamp();
            double tick = Stopwatch.Frequency * (blockMs / 1000.0) * (1 + ppm / 1e6);
            long n = 0, pos = 0;
            while (!Stop)
            {
                long due = t0 + (long)((n + 1) * tick);
                long late = jit > 0 ? (long)(rnd.NextDouble() * jit * Stopwatch.Frequency / 1000) : 0;   // (el trozo llega tarde: el driver)
                long now;
                while ((now = Stopwatch.GetTimestamp()) < due + late)
                {
                    double ms = (due + late - now) * 1000.0 / Stopwatch.Frequency;
                    if (ms > 2) Thread.Sleep(1); else Thread.SpinWait(200);
                }
                int clickAt = -1;
                fixed (byte* b = buf)
                {
                    for (int i = 0; i < frames; i++, pos++)
                    {
                        double v;
                        if (clic) { long k = pos % Periodo; v = k < 96 ? amp * Math.Sin(2 * Math.PI * 2000 * k / rate) : 0; if (k == 0) clickAt = i; }   // (2 ms a 2 kHz)
                        else v = amp * Math.Sin(2 * Math.PI * 440 * pos / rate);
                        for (int c = 0; c < ch; c++)
                        {
                            double cv = ch >= 8 && c >= 2 ? 0 : v;   // (8 canales: solo delante, como la mayoria del sonido)
                            if (bps == 2) *(short*)(b + (i * ch + c) * 2) = (short)Math.Max(-32768, Math.Min(32767, Math.Round(cv * 32767)));
                            else if (bps == 4) *(int*)(b + (i * ch + c) * 4) = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(cv * 2147483647.0)));
                        }
                    }
                    if (clickAt >= 0)
                        lock (Clics) Clics.Add(due - (long)((frames - clickAt) * (double)Stopwatch.Frequency / rate));
                    AudioBlock f = OnBlock;
                    if (f != null) f(new IntPtr(b), buf.Length, due);
                }
                n++;
            }
        }
    }

    // ---- solo pruebas: el loopback de la salida de serie: cuando suena cada clic (QPC de WASAPI) frente a cuando "llego" ----
    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out int flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    sealed class Medidor
    {
        readonly Generador g;
        public volatile bool Stop;
        readonly List<double> lat = new List<double>();
        string err = "";
        public Medidor(Generador g) { this.g = g; }
        public void Start() { Thread t = new Thread(Run); t.IsBackground = true; t.Name = "medidor"; t.Start(); }

        public string Resumen()
        {
            lock (lat)
            {
                if (lat.Count == 0) return "medidor: sin clics" + (err.Length > 0 ? " (" + err + ")" : "");
                List<double> s = new List<double>(lat); s.Sort();
                double sum = 0; foreach (double v in s) sum += v;
                return "clic -> salida de Windows (loopback): n=" + s.Count + ", media " + (sum / s.Count).ToString("0.0", CultureInfo.InvariantCulture) + " ms, p50 " +
                       s[s.Count / 2].ToString("0.0", CultureInfo.InvariantCulture) + ", p95 " + s[Math.Min(s.Count - 1, s.Count * 95 / 100)].ToString("0.0", CultureInfo.InvariantCulture) +
                       ", min " + s[0].ToString("0.0", CultureInfo.InvariantCulture) + ", max " + s[s.Count - 1].ToString("0.0", CultureInfo.InvariantCulture);
            }
        }

        unsafe void Run()
        {
            try
            {
                object eo = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true));
                IMMDeviceEnumeratorA en = (IMMDeviceEnumeratorA)eo;
                IMMDeviceA dev;
                if (en.GetDefaultAudioEndpoint(0, 0, out dev) < 0) { err = "sin salida"; return; }
                Guid iid = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
                object o;
                dev.Activate(ref iid, 23, IntPtr.Zero, out o);
                IAudioClient ac = (IAudioClient)o;
                IntPtr mix;
                ac.GetMixFormat(out mix);
                int rate = Marshal.ReadInt32(mix, 4), ch = Marshal.ReadInt16(mix, 2), bits = Marshal.ReadInt16(mix, 14);
                int hr = ac.Initialize(0, 0x00020000 /* LOOPBACK */, 100000, 0, mix, IntPtr.Zero);
                if (hr < 0) { err = "loopback Initialize 0x" + hr.ToString("X8"); return; }
                Guid ic = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
                ac.GetService(ref ic, out o);
                IAudioCaptureClient cc = (IAudioCaptureClient)o;
                ac.Start();
                Log.Write("Sonido (medida): loopback " + rate + " Hz " + ch + " canales " + bits + " bits");
                int next = 0;
                long lastHit = 0;
                double qpcPer100ns = Stopwatch.Frequency / 1e7;
                while (!Stop)
                {
                    Thread.Sleep(3);
                    uint pk;
                    while (cc.GetNextPacketSize(out pk) >= 0 && pk > 0)
                    {
                        IntPtr d; uint n; int fl; ulong dp, qp;
                        if (cc.GetBuffer(out d, out n, out fl, out dp, out qp) < 0) break;
                        if ((fl & 2) == 0 && bits == 32)
                        {
                            float* f = (float*)d;
                            for (uint i = 0; i < n; i++)
                            {
                                float v = Math.Abs(f[i * ch]);
                                if (v < 0.0003f) continue;   // (-70 dB: el loopback es digital; sin nada mas sonando, 0)
                                long t = (long)(qp * qpcPer100ns) + (long)(i * (double)Stopwatch.Frequency / rate);
                                if (lastHit != 0 && t - lastHit < Stopwatch.Frequency / 4) continue;
                                lastHit = t;
                                // el clic generado mas cercano anterior
                                long best = 0;
                                lock (g.Clics) { while (next < g.Clics.Count && g.Clics[next] <= t) { best = g.Clics[next]; next++; } }
                                if (best != 0) lock (lat) lat.Add((t - best) * 1000.0 / Stopwatch.Frequency);
                            }
                        }
                        cc.ReleaseBuffer(n);
                    }
                }
                ac.Stop();
            }
            catch (Exception ex) { err = ex.Message; }
        }
    }
}
