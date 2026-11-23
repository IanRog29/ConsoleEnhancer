// AudioOut.cs - 23-11-2026 (1.2.4, SINMPV: sin mpv). La salida del sonido de la consola por WASAPI (modo compartido, por eventos), como el
// mpv de audio (--ao=wasapi --audio-buffer=0.02), con lo que hacia su cadena:
//  - de 8 canales a estereo con la misma mezcla (pan=stereo|c0=c0+0.707*c2+0.707*c3+0.707*c4+0.707*c6|c1=c1+0.707*c2+0.707*c3+0.707*c5+
//    0.707*c7) y un limitador que no deja pasar de 1 (alimiter limit=1 level=0, release 50 ms; aqui sin mirar por delante: 0 ms de retraso
//    en vez de los 5 ms de attack de alimiter);
//  - la deriva entre el reloj de la capturadora (el HDMI de la consola) y el de la salida: mpv la corregia con speed=1.004 cuando el lanzador
//    veia mas de 70 ms en cola. Aqui la cola se mira en el propio proceso cada 0,5 s y se corrige poco a poco (+-0,2 %, interpolacion
//    cubica), con la cola objetivo pequeña (audio_cola_ms, de serie 30: lo justo para los trozos de 20 ms de la capturadora); sin
//    corregir, el sonido pasa tal cual (sin interpolar). Lo que mande el lanzador (set_property speed) se multiplica.
//  - si la salida se para (el dispositivo cambia, la tele se apaga) o la cola se dispara (mas de audio_cola_max_ms), se tira lo viejo
//    y se vuelve a la cola objetivo: nunca retraso acumulado.
// La salida la pide en 48 kHz estereo de 32 bits con la conversion de Windows (AUTOCONVERTPCM) si el dispositivo va a otra cosa. C# 5.
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConsoleEnhancer.Motor.Audio
{
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumeratorA
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDeviceA device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceA
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long hnsLatency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr h);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
        [PreserveSig] int ReleaseBuffer(uint frames, int flags);
    }

    sealed class AudioOut
    {
        // ---- ajustes ----
        public int InRate = 48000, InChannels = 2, InBits = 16;
        public int TargetMs = 30;        // audio_cola_ms: la cola que se busca (capturadora -> salida)
        public int MaxMs = 100;          // audio_cola_max_ms: con mas, se tira lo viejo hasta la objetivo
        public int BufferMs = 10;        // audio_salida_ms: lo que se pide a WASAPI (el minimo real lo pone Windows)
        public bool Drift = true;        // audio_deriva=0: sin corregir la deriva (solo lo que mande el lanzador)
        public int BlockMs = 20;         // los trozos de la capturadora (audio_bloque_ms)
        public int FillMs = 0;           // audio_relleno_ms: lo que se deja como mucho en el buffer de WASAPI (0 = lleno, como mpv)
        public volatile float ExternalSpeed = 1f;   // set_property speed (el lanzador)
        public double Gain = 1.0;        // audio_volumen (pruebas)

        // ---- la cola: estereo float, anillo ----
        float[] ring = new float[2 * 48000 * 2];   // 2 s
        long wr, rd;                     // posiciones en tramas (se leen sin candado: un escritor y un lector)
        readonly object ringLock = new object();
        public long FramesIn, FramesPlayed;   // tramas capturadas que han entrado / que han salido (time-pos)
        public long Underruns, Dropped, SilenceFrames;
        double frac;                     // posicion fraccionaria del interpolador
        double ratio = 1.0;              // velocidad de lectura de la cola (1 = tal cual)
        bool prebuffer = true;           // esperando a la cola objetivo
        float limGain = 1f;
        float relCoef;
        public string Info = "";
        public volatile bool Stop;
        public volatile string LastError = "";
        public int OutRate, OutChannels;
        public double StreamLatencyMs, BufferRealMs;
        // estadistica de la cola (cada 0,5 s)
        double qSum; int qN;
        public double QueueAvgMs, QueueNowMs;
        public double RatioNow { get { return ratio; } }

        public AudioOut() { relCoef = (float)(1.0 - Math.Exp(-1.0 / (0.050 * 48000))); }

        public double QueueMs { get { long n = Interlocked.Read(ref wr) - Interlocked.Read(ref rd); return n * 1000.0 / InRate; } }

        // ---- entrada: un trozo de la capturadora (PCM entero o float), en el hilo del driver ----
        public unsafe void Push(IntPtr data, int bytes)
        {
            int bps = InBits / 8, ch = InChannels, frame = bps * ch;
            if (frame <= 0) return;
            int frames = bytes / frame;
            if (frames <= 0) return;
            lock (ringLock)
            {
                int cap = ring.Length / 2;
                long w = wr;
                if (w - rd + frames > cap) { long drop = w - rd + frames - cap; rd += drop; Dropped += drop; }
                byte* p = (byte*)data;
                for (int i = 0; i < frames; i++, p += frame)
                {
                    float l, r;
                    if (ch >= 8)
                    {
                        float c2 = S(p, 2, bps), c3 = S(p, 3, bps);
                        l = S(p, 0, bps) + 0.707f * (c2 + c3 + S(p, 4, bps) + S(p, 6, bps));
                        r = S(p, 1, bps) + 0.707f * (c2 + c3 + S(p, 5, bps) + S(p, 7, bps));
                    }
                    else if (ch >= 2) { l = S(p, 0, bps); r = S(p, 1, bps); }
                    else { l = r = S(p, 0, bps); }
                    // limitador (alimiter limit=1 level=0, sin mirar por delante)
                    float pk = Math.Max(Math.Abs(l), Math.Abs(r)) * limGain;
                    if (pk > 1f) limGain = limGain / pk;
                    else if (limGain < 1f) { limGain += (1f - limGain) * relCoef; if (limGain > 1f) limGain = 1f; }
                    int o = (int)((w % cap) * 2);
                    ring[o] = l * limGain; ring[o + 1] = r * limGain;
                    w++;
                }
                wr = w;
                FramesIn += frames;
            }
        }

        static unsafe float S(byte* p, int c, int bps)
        {
            if (bps == 2) return *(short*)(p + c * 2) * (1f / 32768f);
            if (bps == 4) return *(int*)(p + c * 4) * (1f / 2147483648f);
            if (bps == 3) { int v = (p[c * 3] << 8) | (p[c * 3 + 1] << 16) | (p[c * 3 + 2] << 24); return v * (1f / 2147483648f); }
            return (p[c] - 128) * (1f / 128f);
        }

        // ---- salida: el hilo de WASAPI ----
        const int AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000, AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = unchecked((int)0x80000000),
                  AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000, AUDCLNT_BUFFERFLAGS_SILENT = 2;
        static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        static readonly Guid IID_IAudioRenderClient = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
        static readonly Guid ClsidEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        [DllImport("kernel32.dll")] static extern IntPtr CreateEvent(IntPtr a, bool manual, bool initial, IntPtr name);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("avrt.dll", CharSet = CharSet.Unicode)] static extern IntPtr AvSetMmThreadCharacteristicsW(string task, ref uint idx);
        [DllImport("avrt.dll")] static extern bool AvRevertMmThreadCharacteristics(IntPtr h);
        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

        public void Run()
        {
            uint idx = 0;
            IntPtr mm = IntPtr.Zero;
            try { mm = AvSetMmThreadCharacteristicsW("Pro Audio", ref idx); } catch { }
            int fails = 0;
            while (!Stop)
            {
                long t0 = Clock.Now;
                string err = RunDevice();
                if (Stop) break;
                if (Clock.SinceMs(t0) > 10000) fails = 0;   // (iba bien un rato: la cuenta de nuevo)
                fails++;
                LastError = err;
                // (sin salida durante mucho rato -la tele apagada-: al log las 5 primeras y luego 1 de cada 30)
                if (fails <= 5 || fails % 30 == 0)
                    Log.Write("Sonido: la salida se ha parado (" + err + "): otra vez en " + (fails < 5 ? 500 : 2000) + " ms" + (fails == 5 ? " (siguen sin apuntarse hasta la 30)" : ""));
                for (int i = 0; i < (fails < 5 ? 5 : 20) && !Stop; i++) Thread.Sleep(100);
            }
            if (mm != IntPtr.Zero) try { AvRevertMmThreadCharacteristics(mm); } catch { }
        }

        // Una vez con el dispositivo de salida de serie hasta que falle (devuelve por que)
        unsafe string RunDevice()
        {
            object eo = null; IMMDeviceEnumeratorA en = null; IMMDeviceA dev = null; IAudioClient ac = null; IAudioRenderClient rc = null;
            IntPtr ev = IntPtr.Zero, wfx = IntPtr.Zero, mix = IntPtr.Zero;
            try
            {
                eo = Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidEnumerator, true));
                en = (IMMDeviceEnumeratorA)eo;
                int hr = en.GetDefaultAudioEndpoint(0, 0, out dev);   // eRender, eConsole (como --ao=wasapi de mpv)
                if (hr < 0 || dev == null) return "sin salida de sonido (0x" + hr.ToString("X8") + ")";
                Guid iid = IID_IAudioClient;
                object o;
                hr = dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out o);
                if (hr < 0) return "IMMDevice.Activate 0x" + hr.ToString("X8");
                ac = (IAudioClient)o;
                if (ac.GetMixFormat(out mix) >= 0 && mix != IntPtr.Zero) { OutRate = Marshal.ReadInt32(mix, 4); OutChannels = Marshal.ReadInt16(mix, 2); }
                // 48 kHz estereo float (WAVEFORMATEXTENSIBLE, KSDATAFORMAT_SUBTYPE_IEEE_FLOAT)
                wfx = Marshal.AllocCoTaskMem(40);
                Marshal.WriteInt16(wfx, 0, unchecked((short)0xFFFE)); Marshal.WriteInt16(wfx, 2, 2); Marshal.WriteInt32(wfx, 4, InRate);
                Marshal.WriteInt32(wfx, 8, InRate * 8); Marshal.WriteInt16(wfx, 12, 8); Marshal.WriteInt16(wfx, 14, 32); Marshal.WriteInt16(wfx, 16, 22);
                Marshal.WriteInt16(wfx, 18, 32); Marshal.WriteInt32(wfx, 20, 3 /* SPEAKER_FRONT_LEFT | RIGHT */);
                byte[] sub = new Guid("00000003-0000-0010-8000-00aa00389b71").ToByteArray();
                Marshal.Copy(sub, 0, new IntPtr(wfx.ToInt64() + 24), 16);
                long req = Math.Max(1, BufferMs) * 10000L;
                hr = ac.Initialize(0, AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, req, 0, wfx, IntPtr.Zero);
                if (hr < 0) return "IAudioClient.Initialize 0x" + hr.ToString("X8");
                ev = CreateEvent(IntPtr.Zero, false, false, IntPtr.Zero);
                ac.SetEventHandle(ev);
                uint bufFrames;
                ac.GetBufferSize(out bufFrames);
                long lat; if (ac.GetStreamLatency(out lat) >= 0) StreamLatencyMs = lat / 10000.0;
                BufferRealMs = bufFrames * 1000.0 / InRate;
                Guid ir = IID_IAudioRenderClient;
                hr = ac.GetService(ref ir, out o);
                if (hr < 0) return "GetService(IAudioRenderClient) 0x" + hr.ToString("X8");
                rc = (IAudioRenderClient)o;
                Info = "salida de serie de Windows, " + OutRate + " Hz " + OutChannels + " canales (se le da 48 kHz estereo), buffer " +
                       BufferRealMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms, latencia del flujo " + StreamLatencyMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
                Log.Write("Sonido: " + Info);
                // primero silencio en todo el buffer (como pide WASAPI), luego en marcha
                IntPtr d0;
                if (rc.GetBuffer(bufFrames, out d0) >= 0) rc.ReleaseBuffer(bufFrames, AUDCLNT_BUFFERFLAGS_SILENT);
                lock (ringLock) prebuffer = true;
                hr = ac.Start();
                if (hr < 0) return "IAudioClient.Start 0x" + hr.ToString("X8");
                long lastStat = Clock.Now;
                while (!Stop)
                {
                    uint w = WaitForSingleObject(ev, 200);
                    uint pad;
                    hr = ac.GetCurrentPadding(out pad);
                    if (hr < 0) return "GetCurrentPadding 0x" + hr.ToString("X8") + (hr == unchecked((int)0x88890004) ? " (dispositivo cambiado o quitado)" : "");
                    uint free = bufFrames - pad;
                    if (FillMs > 0) { uint lim = (uint)(FillMs * InRate / 1000); free = pad >= lim ? 0 : Math.Min(free, lim - pad); }
                    if (free > 0)
                    {
                        IntPtr d;
                        hr = rc.GetBuffer(free, out d);
                        if (hr < 0) return "GetBuffer 0x" + hr.ToString("X8");
                        int silent = Fill((float*)d, (int)free);
                        rc.ReleaseBuffer(free, silent == (int)free ? AUDCLNT_BUFFERFLAGS_SILENT : 0);
                    }
                    if (Clock.SinceMs(lastStat) >= 500) { lastStat = Clock.Now; Control(); }
                }
                ac.Stop();
                return "";
            }
            catch (Exception ex) { return ex.GetType().Name + ": " + ex.Message; }
            finally
            {
                if (ev != IntPtr.Zero) CloseHandle(ev);
                if (wfx != IntPtr.Zero) Marshal.FreeCoTaskMem(wfx);
                if (mix != IntPtr.Zero) CoTaskMemFree(mix);
                foreach (object x in new object[] { rc, ac, dev, en })
                    if (x != null) try { Marshal.ReleaseComObject(x); } catch { }
            }
        }

        // Llena n tramas estereo de la cola. Devuelve cuantas son silencio
        unsafe int Fill(float* d, int n)
        {
            int silent = 0;
            lock (ringLock)
            {
                int cap = ring.Length / 2;
                long avail = wr - rd;
                int target = TargetMs * InRate / 1000;
                if (prebuffer)
                {
                    if (avail < target) { for (int i = 0; i < n * 2; i++) d[i] = 0f; SilenceFrames += n; return n; }
                    prebuffer = false;
                    // (lo que se junto mientras la salida arrancaba o volvia: fuera, se empieza con la cola objetivo y no con mas retraso)
                    if (avail > target) { rd += avail - target; Dropped += avail - target; frac = 0; if (avail - target > InRate / 50) NoteDrop("al empezar o volver", avail - target); }
                }
                double sp = ratio * ExternalSpeed;
                bool plain = Math.Abs(sp - 1.0) < 1e-9 && frac == 0;
                for (int i = 0; i < n; i++)
                {
                    float l = 0f, r = 0f;
                    if (plain)
                    {
                        if (wr - rd >= 1)
                        {
                            int o = (int)((rd % cap) * 2);
                            l = ring[o]; r = ring[o + 1];
                            rd++; FramesPlayed++;
                        }
                        else { silent++; }
                    }
                    else
                    {
                        // cubica (Catmull-Rom) entre las tramas rd-1, rd, rd+1, rd+2
                        if (wr - rd >= 3 && rd >= 1)
                        {
                            float t = (float)frac;
                            int o0 = (int)(((rd - 1) % cap) * 2), o1 = (int)((rd % cap) * 2), o2 = (int)(((rd + 1) % cap) * 2), o3 = (int)(((rd + 2) % cap) * 2);
                            l = Cub(ring[o0], ring[o1], ring[o2], ring[o3], t);
                            r = Cub(ring[o0 + 1], ring[o1 + 1], ring[o2 + 1], ring[o3 + 1], t);
                            frac += sp;
                            int adv = (int)Math.Floor(frac);
                            frac -= adv;
                            rd += adv; FramesPlayed += adv;
                        }
                        else if (wr - rd >= 1)
                        {
                            int o = (int)((rd % cap) * 2);
                            l = ring[o]; r = ring[o + 1];
                            rd++; FramesPlayed++; frac = 0;
                        }
                        else { silent++; }
                    }
                    if (Gain != 1.0) { l *= (float)Gain; r *= (float)Gain; }
                    d[i * 2] = l; d[i * 2 + 1] = r;
                }
                if (silent > 0)
                {
                    SilenceFrames += silent;
                    if (FramesPlayed > 0) { Underruns++; prebuffer = true; }   // (sin nada en cola: otra vez hasta la objetivo, sin entrecortar)
                }
                qSum += (wr - rd) * 1000.0 / InRate; qN++;
            }
            return silent;
        }

        // (al log, como mucho 1 cada 5 s, fuera del candado de la cola)
        long lastDropLog;
        void NoteDrop(string why, long frames)
        {
            if (lastDropLog != 0 && Clock.SinceMs(lastDropLog) < 5000) return;
            lastDropLog = Clock.Now;
            string t = "Sonido: " + why + ": se tiran " + (frames * 1000 / InRate) + " ms de lo mas viejo (la cola vuelve a " + TargetMs + " ms)";
            ThreadPool.QueueUserWorkItem(delegate { Log.Write(t); });
        }

        static float Cub(float y0, float y1, float y2, float y3, float t)
        {
            float a = -0.5f * y0 + 1.5f * y1 - 1.5f * y2 + 0.5f * y3, b = y0 - 2.5f * y1 + 2f * y2 - 0.5f * y3, c = -0.5f * y0 + 0.5f * y2;
            return ((a * t + b) * t + c) * t + y1;
        }

        // Cada 0,5 s: la cola media y la correccion de la deriva (o tirar lo viejo si se ha disparado)
        void Control()
        {
            lock (ringLock)
            {
                QueueAvgMs = qN > 0 ? qSum / qN : (wr - rd) * 1000.0 / InRate;
                QueueNowMs = (wr - rd) * 1000.0 / InRate;
                qSum = 0; qN = 0;
                if (QueueNowMs > MaxMs)
                {
                    long keep = TargetMs * InRate / 1000, drop = (wr - rd) - keep;
                    if (drop > 0) { rd += drop; Dropped += drop; frac = 0; NoteDrop("la cola paso de " + MaxMs + " ms", drop); }
                    ratio = 1.0;
                    return;
                }
                if (!Drift || prebuffer) { ratio = 1.0; return; }
                // con histeresis (como el lanzador con mpv: alta / baja), pero en pequeño y en este proceso: +-0,2 %
                // (la cola media normal es la objetivo menos medio trozo: llega a trozos de BlockMs y sale seguida)
                double center = Math.Max(2, TargetMs - BlockMs / 2.0), hi = center + 15, lo = Math.Max(1, center - 4);
                if (ratio > 1.0) { if (QueueAvgMs <= center) ratio = 1.0; }
                else if (ratio < 1.0) { if (QueueAvgMs >= center) ratio = 1.0; }
                else if (QueueAvgMs > hi) ratio = 1.002;
                else if (QueueAvgMs < lo) ratio = 0.998;
                if (ratio == 1.0 && frac != 0 && ExternalSpeed == 1f) frac = 0;   // (de vuelta tal cual: una trama de mas o de menos no se oye)
            }
        }
    }
}
