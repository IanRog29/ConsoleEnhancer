// AudioDs.cs - 23-11-2026 (1.2.4, SINMPV: sin mpv). El sonido de la consola por DirectShow, como lo abria el mpv de audio de la app
// ("av://dshow:audio=<nombre>" + demuxer-lavf-o=audio_pin_name=<pin>,sample_rate=48000,sample_size=16,channels=8|2,audio_buffer_size=20):
//  - el dispositivo: primero en la categoria de entradas de audio de DirectShow y, si no esta, en la de video (la GC573 da el sonido por
//    pines del MISMO filtro de video: "Audio" y "Audio Mixer"; ffmpeg hace lo mismo: "Searching for audio device within video devices");
//    otra instancia del filtro que la del motor de la imagen (el pin de video no se toca: los dos procesos a la vez, como mpv);
//  - el pin: el que se llame asi o tenga ese Id (QueryId: "1", "2"...; ffmpeg compara los dos), con formatos de audio;
//  - el formato: el de la lista del pin con esa frecuencia, bits y canales (IAMStreamConfig.SetFormat, como ffmpeg);
//  - los trozos: IAMBufferNegotiation.SuggestAllocatorProperties con cbBuffer = bytes de audio_buffer_size ms (lo que hace ffmpeg; si el
//    pin no lo admite, se sigue con los suyos);
//  - pin -> Sample Grabber (modo de llamada, sin copia interna) -> Null Renderer sin reloj (nunca espera).
// Cada trozo que llega va a OnBlock (en el hilo del driver: solo copiar). C# 5.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor.Audio
{
    [ComImport, Guid("56a868a5-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAMBufferNegotiation
    {
        [PreserveSig] int SuggestAllocatorProperties(ref AllocatorProperties pprop);
        [PreserveSig] int GetAllocatorProperties(out AllocatorProperties pprop);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct AllocatorProperties { public int cBuffers, cbBuffer, cbAlign, cbPrefix; }

    delegate void AudioBlock(IntPtr data, int bytes, long qpc);

    [ComVisible(true)]
    public sealed class AudioGrabberCallback : ISampleGrabberCB
    {
        readonly AudioDs owner;
        internal AudioGrabberCallback(AudioDs owner) { this.owner = owner; }
        public int SampleCB(double SampleTime, IntPtr pSample)
        {
            long qpc = System.Diagnostics.Stopwatch.GetTimestamp();
            try { owner.OnSample(pSample, qpc); } catch { }
            return 0;
        }
        public int BufferCB(double SampleTime, IntPtr pBuffer, int BufferLen) { return 0; }
    }

    public sealed class AudioDs
    {
        public static readonly Guid CLSID_AudioInputDeviceCategory = new Guid("33D9A762-90C8-11d0-BD43-00A0C911CE86");
        public static readonly Guid MEDIATYPE_Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        public static readonly Guid FORMAT_WaveFormatEx = new Guid("05589f81-c356-11ce-bf01-00aa0055595a");

        internal AudioBlock OnBlock;
        public string Device = "", Pin = "";
        public int Rate = 48000, Bits = 16, Channels = 2, BlockMs = 20;
        public string Opened = "";      // lo que se ha abierto (para el log)
        public int GotBits, GotChannels, GotRate, GotBlockAlign;

        IGraphBuilder graph;
        IBaseFilter capFilter, grabFilter, nullFilter;
        ISampleGrabber grabber;
        IPin capOut, grabIn, grabOut, nullIn;
        AudioGrabberCallback cb;

        internal void OnSample(IntPtr pSample, long qpc)
        {
            AudioBlock f = OnBlock;
            if (f == null) return;
            IMediaSample s = (IMediaSample)Marshal.GetObjectForIUnknown(pSample);
            try
            {
                IntPtr buf;
                if (s.GetPointer(out buf) < 0 || buf == IntPtr.Zero) return;
                int len = s.GetActualDataLength();
                if (len > 0) f(buf, len, qpc);
            }
            finally { Marshal.ReleaseComObject(s); }
        }

        // Abre y arranca. Excepcion con el motivo si no se puede
        public void Start()
        {
            string why = "";
            foreach (Guid cat in new Guid[] { CLSID_AudioInputDeviceCategory, DsApi.CLSID_VideoInputDeviceCategory })
            {
                string w = TryCategory(cat);
                if (w == null) return;
                why += (why.Length > 0 ? "; " : "") + (cat == CLSID_AudioInputDeviceCategory ? "audio: " : "video: ") + w;
                Close();
            }
            throw new InvalidOperationException("sin sonido de \"" + Device + "\"" + (Pin.Length > 0 ? " pin " + Pin : "") + " (" + why + ")");
        }

        // null = abierto y en marcha; si no, por que no
        string TryCategory(Guid cat)
        {
            string devName;
            capFilter = Bind(cat, Device, out devName);
            if (capFilter == null) return "no esta";
            graph = (IGraphBuilder)DsApi.CoCreate(DsApi.CLSID_FilterGraph);
            MfHr.Check(graph.AddFilter(capFilter, "Audio"), "AddFilter(audio)");
            string pinInfo, pins;
            capOut = FindAudioPin(capFilter, Pin, out pinInfo, out pins);
            if (capOut == null) return "sin pin de audio" + (Pin.Length > 0 ? " \"" + Pin + "\"" : "") + " (pines: " + pins + ")";
            IAMStreamConfig sc = capOut as IAMStreamConfig;
            if (sc == null) return "el pin " + pinInfo + " sin IAMStreamConfig";
            IntPtr pmt = IntPtr.Zero, gmt = IntPtr.Zero;
            try
            {
                string fmts;
                pmt = ChooseFormat(sc, out fmts);
                if (pmt == IntPtr.Zero) return "el pin " + pinInfo + " no da " + Rate + " Hz " + Bits + " bits " + Channels + " canales (da: " + fmts + ")";
                int hr = sc.SetFormat(pmt);
                if (hr < 0) return "SetFormat 0x" + hr.ToString("X8");
                AM_MEDIA_TYPE mt = (AM_MEDIA_TYPE)Marshal.PtrToStructure(pmt, typeof(AM_MEDIA_TYPE));
                ReadWave(mt.pbFormat, out GotChannels, out GotRate, out GotBits, out GotBlockAlign);
                // los trozos de BlockMs (como audio_buffer_size de ffmpeg)
                string neg = "";
                IAMBufferNegotiation bn = capOut as IAMBufferNegotiation;
                if (bn != null && BlockMs > 0)
                {
                    AllocatorProperties ap = new AllocatorProperties();
                    ap.cBuffers = -1; ap.cbAlign = -1; ap.cbPrefix = -1;
                    ap.cbBuffer = Math.Max(GotBlockAlign, GotRate * GotBlockAlign * BlockMs / 1000);
                    int nh = bn.SuggestAllocatorProperties(ref ap);
                    neg = nh >= 0 ? ", trozos de " + BlockMs + " ms (" + ap.cbBuffer + " bytes)" : ", trozos: el pin no admite " + BlockMs + " ms (0x" + nh.ToString("X8") + ")";
                }
                else neg = ", trozos los del pin (sin IAMBufferNegotiation)";
                grabFilter = (IBaseFilter)DsApi.CoCreate(DsApi.CLSID_SampleGrabber);
                grabber = (ISampleGrabber)grabFilter;
                AM_MEDIA_TYPE g = new AM_MEDIA_TYPE();
                g.majortype = MEDIATYPE_Audio; g.subtype = mt.subtype; g.formattype = mt.formattype;
                g.bFixedSizeSamples = 1; g.lSampleSize = (uint)GotBlockAlign;
                gmt = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(AM_MEDIA_TYPE)));
                Marshal.StructureToPtr(g, gmt, false);
                MfHr.Check(grabber.SetMediaType(gmt), "ISampleGrabber.SetMediaType(audio)");
                MfHr.Check(grabber.SetBufferSamples(0), "SetBufferSamples");
                MfHr.Check(grabber.SetOneShot(0), "SetOneShot");
                cb = new AudioGrabberCallback(this);
                MfHr.Check(grabber.SetCallback(cb, 0), "ISampleGrabber.SetCallback");
                MfHr.Check(graph.AddFilter(grabFilter, "Grabber"), "AddFilter(grabber)");
                nullFilter = (IBaseFilter)DsApi.CoCreate(DsApi.CLSID_NullRenderer);
                MfHr.Check(graph.AddFilter(nullFilter, "Null"), "AddFilter(null)");
                grabIn = DsApi.FindPin(grabFilter, DsApi.PINDIR_INPUT);
                grabOut = DsApi.FindPin(grabFilter, DsApi.PINDIR_OUTPUT);
                nullIn = DsApi.FindPin(nullFilter, DsApi.PINDIR_INPUT);
                hr = graph.ConnectDirect(capOut, grabIn, pmt);
                if (hr < 0) return "ConnectDirect(audio -> grabber) 0x" + hr.ToString("X8");
                hr = graph.ConnectDirect(grabOut, nullIn, IntPtr.Zero);
                if (hr < 0) return "ConnectDirect(grabber -> null) 0x" + hr.ToString("X8");
                // sin reloj en el Null Renderer: nunca espera a las marcas de tiempo
                graph.SetDefaultSyncSource();
                nullFilter.SetSyncSource(IntPtr.Zero);
                IMediaControl mc = (IMediaControl)graph;
                hr = mc.Run();
                if (hr < 0) return "Run 0x" + hr.ToString("X8");
                Opened = "\"" + devName + "\" (" + (cat == CLSID_AudioInputDeviceCategory ? "entrada de audio" : "filtro de video") + "), pin " + pinInfo + ", " +
                         GotRate + " Hz " + GotBits + " bits " + GotChannels + " canales" + neg;
                return null;
            }
            finally
            {
                DsApi.DeleteMediaType(pmt);
                if (gmt != IntPtr.Zero) Marshal.FreeHGlobal(gmt);
            }
        }

        public void Close()
        {
            try { if (graph != null) ((IMediaControl)graph).Stop(); } catch { }
            if (grabber != null) { try { grabber.SetCallback(null, 0); } catch { } }
            foreach (object o in new object[] { capOut, grabIn, grabOut, nullIn, grabber, nullFilter, grabFilter, capFilter, graph })
                if (o != null) { try { Marshal.ReleaseComObject(o); } catch { } }
            capOut = grabIn = grabOut = nullIn = null; grabber = null; nullFilter = grabFilter = capFilter = null; graph = null; cb = null;
        }

        static IBaseFilter Bind(Guid cat, string name, out string devName)
        {
            devName = "";
            ICreateDevEnum de = (ICreateDevEnum)DsApi.CoCreate(DsApi.CLSID_SystemDeviceEnum);
            IDsEnumMoniker em = null;
            try
            {
                IntPtr pe;
                if (de.CreateClassEnumerator(ref cat, out pe, 0) != 0 || pe == IntPtr.Zero) return null;
                em = (IDsEnumMoniker)Marshal.GetObjectForIUnknown(pe);
                Marshal.Release(pe);
                // el nombre exacto; si no hay, el primero que lo contenga (como la imagen del motor)
                IBaseFilter sub = null; string subName = "";
                while (true)
                {
                    IntPtr pm; uint got;
                    if (em.Next(1, out pm, out got) != 0 || got == 0 || pm == IntPtr.Zero) break;
                    IDsMoniker m = (IDsMoniker)Marshal.GetObjectForIUnknown(pm);
                    Marshal.Release(pm);
                    try
                    {
                        Guid iid = DsApi.IID_IPropertyBag;
                        IntPtr pb;
                        string fn = "";
                        if (m.BindToStorage(IntPtr.Zero, IntPtr.Zero, ref iid, out pb) >= 0 && pb != IntPtr.Zero)
                        {
                            IPropertyBag bag = (IPropertyBag)Marshal.GetObjectForIUnknown(pb);
                            Marshal.Release(pb);
                            fn = DsApi.ReadBagString(bag, "FriendlyName") ?? "";
                            Marshal.ReleaseComObject(bag);
                        }
                        bool exact = string.Equals(fn, name, StringComparison.OrdinalIgnoreCase);
                        bool part = !exact && sub == null && name.Length > 0 && fn.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!exact && !part) continue;
                        Guid ib = DsApi.IID_IBaseFilter;
                        IntPtr pf;
                        if (m.BindToObject(IntPtr.Zero, IntPtr.Zero, ref ib, out pf) < 0 || pf == IntPtr.Zero) continue;
                        IBaseFilter f = (IBaseFilter)Marshal.GetObjectForIUnknown(pf);
                        Marshal.Release(pf);
                        if (exact) { if (sub != null) Marshal.ReleaseComObject(sub); devName = fn; return f; }
                        sub = f; subName = fn;
                    }
                    finally { Marshal.ReleaseComObject(m); }
                }
                devName = subName;
                return sub;
            }
            finally { if (em != null) Marshal.ReleaseComObject(em); Marshal.ReleaseComObject(de); }
        }

        // El pin de salida con formatos de audio: el que se llame o tenga el Id pedido (o el primero si no se pide). pins = todos (log)
        static IPin FindAudioPin(IBaseFilter f, string want, out string info, out string pins)
        {
            info = ""; pins = "";
            IntPtr pe;
            if (f.EnumPins(out pe) < 0 || pe == IntPtr.Zero) return null;
            IEnumPins en = (IEnumPins)Marshal.GetObjectForIUnknown(pe);
            Marshal.Release(pe);
            StringBuilder all = new StringBuilder();
            try
            {
                while (true)
                {
                    IntPtr pp; uint got;
                    if (en.Next(1, out pp, out got) != 0 || got == 0 || pp == IntPtr.Zero) break;
                    IPin pin = (IPin)Marshal.GetObjectForIUnknown(pp);
                    Marshal.Release(pp);
                    bool keep = false;
                    try
                    {
                        int dir;
                        if (pin.QueryDirection(out dir) < 0 || dir != DsApi.PINDIR_OUTPUT) continue;
                        string n = DsApi.PinName(pin), id = DsApi.PinId(pin);
                        bool audio = HasAudio(pin);
                        all.Append(all.Length > 0 ? ", " : "").Append("\"" + n + "\"/" + id + (audio ? " (audio)" : ""));
                        if (!audio) continue;
                        if (want.Length > 0 && !string.Equals(n, want, StringComparison.OrdinalIgnoreCase) && !string.Equals(id, want, StringComparison.OrdinalIgnoreCase)) continue;
                        info = "\"" + n + "\"/" + id;
                        keep = true;
                        pins = all.ToString();
                        return pin;
                    }
                    finally { if (!keep) Marshal.ReleaseComObject(pin); }
                }
            }
            finally { Marshal.ReleaseComObject(en); }
            pins = all.ToString();
            return null;
        }

        static bool HasAudio(IPin pin)
        {
            IAMStreamConfig sc = pin as IAMStreamConfig;
            if (sc == null) return false;
            int n, sz;
            if (sc.GetNumberOfCapabilities(out n, out sz) < 0 || n <= 0) return false;
            IntPtr scc = Marshal.AllocHGlobal(Math.Max(sz, 256));
            try
            {
                IntPtr pmt;
                if (sc.GetStreamCaps(0, out pmt, scc) < 0 || pmt == IntPtr.Zero) return false;
                AM_MEDIA_TYPE mt = (AM_MEDIA_TYPE)Marshal.PtrToStructure(pmt, typeof(AM_MEDIA_TYPE));
                DsApi.DeleteMediaType(pmt);
                return mt.majortype == MEDIATYPE_Audio;
            }
            finally { Marshal.FreeHGlobal(scc); }
        }

        static void ReadWave(IntPtr wf, out int ch, out int rate, out int bits, out int align)
        {
            ch = 0; rate = 0; bits = 0; align = 0;
            if (wf == IntPtr.Zero) return;
            ch = Marshal.ReadInt16(wf, 2); rate = Marshal.ReadInt32(wf, 4); align = Marshal.ReadInt16(wf, 12); bits = Marshal.ReadInt16(wf, 14);
        }

        // El formato de la lista con Rate / Bits / Channels (como ffmpeg). Devuelve el AM_MEDIA_TYPE (lo libera quien llama) o Zero
        IntPtr ChooseFormat(IAMStreamConfig sc, out string fmts)
        {
            fmts = "";
            int n, sz;
            if (sc.GetNumberOfCapabilities(out n, out sz) < 0 || n <= 0) return IntPtr.Zero;
            IntPtr scc = Marshal.AllocHGlobal(Math.Max(sz, 256));
            StringBuilder sb = new StringBuilder();
            try
            {
                for (int i = 0; i < n && i < 256; i++)
                {
                    IntPtr pmt;
                    if (sc.GetStreamCaps(i, out pmt, scc) < 0 || pmt == IntPtr.Zero) continue;
                    AM_MEDIA_TYPE mt = (AM_MEDIA_TYPE)Marshal.PtrToStructure(pmt, typeof(AM_MEDIA_TYPE));
                    bool ok = false;
                    if (mt.majortype == MEDIATYPE_Audio && mt.formattype == FORMAT_WaveFormatEx && mt.pbFormat != IntPtr.Zero && mt.cbFormat >= 16)
                    {
                        int ch, rate, bits, align;
                        ReadWave(mt.pbFormat, out ch, out rate, out bits, out align);
                        if (sb.Length < 300) sb.Append(sb.Length > 0 ? ", " : "").Append(rate + "/" + bits + "/" + ch);
                        ok = (Rate <= 0 || rate == Rate) && (Bits <= 0 || bits == Bits) && (Channels <= 0 || ch == Channels);
                    }
                    if (ok) { fmts = sb.ToString(); return pmt; }
                    DsApi.DeleteMediaType(pmt);
                }
                // Sin uno exacto (ffmpeg fallaria aqui): un formato de la lista cuyos margenes (AUDIO_STREAM_CONFIG_CAPS) admitan lo pedido, con
                // esos valores puestos (otras capturadoras dan un formato y sus margenes en vez de la lista entera)
                for (int i = 0; i < n && i < 256 && Rate > 0 && Bits > 0 && Channels > 0; i++)
                {
                    IntPtr pmt;
                    if (sc.GetStreamCaps(i, out pmt, scc) < 0 || pmt == IntPtr.Zero) continue;
                    AM_MEDIA_TYPE mt = (AM_MEDIA_TYPE)Marshal.PtrToStructure(pmt, typeof(AM_MEDIA_TYPE));
                    bool ok = mt.majortype == MEDIATYPE_Audio && mt.formattype == FORMAT_WaveFormatEx && mt.pbFormat != IntPtr.Zero && mt.cbFormat >= 16 && sz >= 52;
                    if (ok)
                    {
                        int chMin = Marshal.ReadInt32(scc, 16), chMax = Marshal.ReadInt32(scc, 20), bMin = Marshal.ReadInt32(scc, 28), bMax = Marshal.ReadInt32(scc, 32);
                        int rMin = Marshal.ReadInt32(scc, 40), rMax = Marshal.ReadInt32(scc, 44);
                        ok = Channels >= chMin && Channels <= chMax && Bits >= bMin && Bits <= bMax && Rate >= rMin && Rate <= rMax;
                        int tag = (ushort)Marshal.ReadInt16(mt.pbFormat, 0);
                        if (ok && (tag == 1 || (tag == 0xFFFE && mt.cbFormat >= 40)))
                        {
                            int align = Channels * Bits / 8;
                            Marshal.WriteInt16(mt.pbFormat, 2, (short)Channels); Marshal.WriteInt32(mt.pbFormat, 4, Rate);
                            Marshal.WriteInt32(mt.pbFormat, 8, Rate * align); Marshal.WriteInt16(mt.pbFormat, 12, (short)align);
                            Marshal.WriteInt16(mt.pbFormat, 14, (short)Bits);
                            if (tag == 0xFFFE) { Marshal.WriteInt16(mt.pbFormat, 18, (short)Bits); Marshal.WriteInt32(mt.pbFormat, 20, Channels >= 8 ? 0x63F : Channels == 2 ? 3 : 4); }
                            mt.lSampleSize = (uint)align;
                            Marshal.StructureToPtr(mt, pmt, false);
                            fmts = sb.ToString() + " (con margenes: " + rMin + "-" + rMax + " Hz, " + bMin + "-" + bMax + " bits, " + chMin + "-" + chMax + " canales)";
                            return pmt;
                        }
                    }
                    DsApi.DeleteMediaType(pmt);
                }
            }
            finally { Marshal.FreeHGlobal(scc); }
            fmts = sb.ToString();
            return IntPtr.Zero;
        }
    }
}
