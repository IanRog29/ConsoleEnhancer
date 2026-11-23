using System;
using System.Runtime.InteropServices;
using ConsoleEnhancer.Motor.Native;

// 23-11 (1.2.4 ronda 7, GAMEPLAY): video comprimido (mp4 / mkv / webm: H.264, HEVC, VP9, AV1...) para test=archivo, por Media Foundation y
// decodificado por la grafica (DXVA con un dispositivo D3D11 propio, como un reproductor), para probar con VIDEO REAL DE JUEGO en
// movimiento sin la consola. Se lee en orden (una imagen tras otra) y se salta hacia atras al volver al principio (bucle). La imagen sale
// en NV12 (o P010 si es de 10 bits) en memoria del procesador; FileSource la pasa a YUY2 al tamaño de la captura (FileConv.cs).
// No depende de nada del motor salvo de la interop de MfCapture.cs: las herramientas de medida (banco de verdad) lo compilan tal cual
// para decodificar EXACTAMENTE lo mismo (la decodificacion de H.264 / HEVC / AV1 es exacta bit a bit, sea por la grafica o no).
namespace ConsoleEnhancer.Motor.Native
{
    [ComImport, Guid("eb533d5d-2db6-40f8-97a9-494692014f07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFDXGIDeviceManager
    {
        [PreserveSig] int CloseDeviceHandle(IntPtr hDevice);
        [PreserveSig] int GetVideoService(IntPtr hDevice, [In] ref Guid riid, out IntPtr ppService);
        [PreserveSig] int LockDevice(IntPtr hDevice, [In] ref Guid riid, out IntPtr ppUnkDevice, int fBlock);
        [PreserveSig] int OpenDeviceHandle(out IntPtr phDevice);
        [PreserveSig] int ResetDevice(IntPtr pUnkDevice, uint resetToken);
        [PreserveSig] int TestDevice(IntPtr hDevice);
        [PreserveSig] int UnlockDevice(IntPtr hDevice, int fSaveState);
    }

    [ComImport, Guid("33ae5ea6-4316-436f-8ddd-d73d22f829ec"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMF2DBuffer2
    {
        [PreserveSig] int Lock2D(out IntPtr ppbScanline0, out int plPitch);
        [PreserveSig] int Unlock2D();
        [PreserveSig] int GetScanline0AndPitch(out IntPtr pbScanline0, out int plPitch);
        [PreserveSig] int IsContiguousFormat(out int pfIsContiguous);
        [PreserveSig] int GetContiguousLength(out uint pcbLength);
        [PreserveSig] int ContiguousCopyTo(IntPtr pbDestBuffer, uint cbDestBuffer);
        [PreserveSig] int ContiguousCopyFrom(IntPtr pbSrcBuffer, uint cbSrcBuffer);
        [PreserveSig] int Lock2DSize(int lockFlags, out IntPtr ppbScanline0, out int plPitch, out IntPtr ppbBufferStart, out uint pcbBufferLength);
        [PreserveSig] int Copy2DTo(IntPtr pDestBuffer);
    }

    [ComImport, Guid("9B7E4E00-342C-4106-A19F-4F2704F689F0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ID3D10MultithreadV
    {
        [PreserveSig] void Enter();
        [PreserveSig] void Leave();
        [PreserveSig] int SetMultithreadProtected(int bMTProtect);
        [PreserveSig] int GetMultithreadProtected();
    }

    public static class MfFileApi
    {
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
        public static extern int MFCreateSourceReaderFromURL(string pwszURL, [MarshalAs(UnmanagedType.Interface)] IMFAttributes pAttributes, out IMFSourceReader ppSourceReader);
        [DllImport("mfplat.dll")] public static extern int MFCreateDXGIDeviceManager(out uint resetToken, out IntPtr ppDeviceManager);
        [DllImport("d3d11.dll")]
        public static extern int D3D11CreateDevice(IntPtr pAdapter, int driverType, IntPtr software, uint flags, IntPtr pFeatureLevels, uint featureLevels,
            uint sdkVersion, out IntPtr ppDevice, out int pFeatureLevel, out IntPtr ppImmediateContext);

        public const uint MEDIASOURCE = 0xFFFFFFFF;          // MF_SOURCE_READER_MEDIASOURCE
        public static readonly Guid MF_SOURCE_READER_D3D_MANAGER = new Guid("ec822da2-e1e9-4b29-a0d8-563c719f5269");
        public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");
        public static readonly Guid MF_SOURCE_READER_DISABLE_DXVA = new Guid("aa456cfd-3943-4a1e-a77d-1838c0ea2e35");
        public static readonly Guid MF_PD_DURATION = new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
        public static readonly Guid MF_MT_MINIMUM_DISPLAY_APERTURE = new Guid("d7388766-18fe-48c6-a177-ee894867c8c4");
        public static readonly Guid MF_MT_GEOMETRIC_APERTURE = new Guid("66758743-7e5f-400d-980a-aa8596c85696");
        public static readonly Guid IID_IMFDXGIBuffer = new Guid("e7174cfa-1c9e-48b1-8866-626226bfc258");
    }
}

namespace ConsoleEnhancer.Motor
{
    public sealed unsafe class MfVideoFile : IDisposable
    {
        public static Action<string> Logger;         // (el motor: Log.Write)
        public readonly string Path;
        public int Width, Height;                    // lo visible
        public int FpsNum = 60, FpsDen = 1;
        public long DurationHns;
        public long Frames;                          // estimadas por la duracion (se corrigen al llegar al final)
        public string Codec = "?";
        public bool WantHw;                          // se pidio decodificar por la grafica
        public bool Hardware;                        // las imagenes llegan en superficies de la grafica (DXVA de verdad)
        public bool TenBit;                          // P010
        public long Index = -1;                      // la imagen leida ahora (-1 = ninguna)
        public long Decoded;                         // imagenes decodificadas en total

        IMFSourceReader rd;
        IntPtr dev, ctx, mgrPtr;
        IMFDXGIDeviceManager mgr;
        bool mfUp;
        IMFSample cur;
        long ts0 = long.MinValue, lastTs;
        int bufH, offX, offY;                        // alto de la superficie (filas por plano) y origen de lo visible
        bool firstLogged;

        public delegate void FrameAction(long index, byte* y, int pitchY, byte* uv, int pitchUV, bool p010);

        static void L(string s) { Action<string> l = Logger; if (l != null) l(s); }

        public MfVideoFile(string path, bool hw)
        {
            Path = path; WantHw = hw;
            MfHr.Check(MfApi.MFStartup(MfApi.MF_VERSION, 0), "MFStartup");
            mfUp = true;
            try { Open(); }
            catch { Dispose(); throw; }
        }

        void Open()
        {
            IMFAttributes at;
            MfHr.Check(MfApi.MFCreateAttributes(out at, 4), "MFCreateAttributes");
            try
            {
                Guid k;
                if (WantHw)
                {
                    // dispositivo D3D11 con video (el adaptador de serie: la grafica de la pantalla principal)
                    int fl;
                    int hr = MfFileApi.D3D11CreateDevice(IntPtr.Zero, 1 /*HARDWARE*/, IntPtr.Zero, 0x800 /*VIDEO_SUPPORT*/ | 0x20 /*BGRA*/, IntPtr.Zero, 0, 7, out dev, out fl, out ctx);
                    if (hr < 0) { L("Video: sin dispositivo D3D11 con video (" + MfHr.Describe(hr) + "): se decodifica sin la grafica"); WantHw = false; }
                    else
                    {
                        object o = Marshal.GetObjectForIUnknown(dev);
                        try { ID3D10MultithreadV mt = (ID3D10MultithreadV)o; mt.SetMultithreadProtected(1); }
                        finally { MfApi.Release(o); }
                        uint token;
                        MfHr.Check(MfFileApi.MFCreateDXGIDeviceManager(out token, out mgrPtr), "MFCreateDXGIDeviceManager");
                        mgr = (IMFDXGIDeviceManager)Marshal.GetObjectForIUnknown(mgrPtr);
                        MfHr.Check(mgr.ResetDevice(dev, token), "IMFDXGIDeviceManager.ResetDevice");
                        k = MfFileApi.MF_SOURCE_READER_D3D_MANAGER; MfHr.Check(at.SetUnknown(ref k, mgr), "MF_SOURCE_READER_D3D_MANAGER");
                    }
                }
                k = MfFileApi.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS; at.SetUINT32(ref k, WantHw ? 1u : 0u);
                if (!WantHw) { k = MfFileApi.MF_SOURCE_READER_DISABLE_DXVA; at.SetUINT32(ref k, 1); }
                MfHr.Check(MfFileApi.MFCreateSourceReaderFromURL(Path, at, out rd), "MFCreateSourceReaderFromURL " + Path);
            }
            finally { MfApi.Release(at); }
            rd.SetStreamSelection(MfApi.ALL_STREAMS, 0);
            MfHr.Check(rd.SetStreamSelection(MfApi.FIRST_VIDEO_STREAM, 1), "sin pista de video");
            // lo que trae (codec, ritmo)
            IMFMediaType nt;
            MfHr.Check(rd.GetNativeMediaType(MfApi.FIRST_VIDEO_STREAM, 0, out nt), "GetNativeMediaType");
            try
            {
                Guid sub; uint fcc; ulong v;
                if (MfApi.GetGuid((IMFAttributes)nt, MfGuids.MF_MT_SUBTYPE, out sub))
                    Codec = MfGuids.IsFourCCGuid(sub, out fcc) ? FourCCText(fcc) : sub.ToString();
                if (MfApi.GetU64((IMFAttributes)nt, MfGuids.MF_MT_FRAME_RATE, out v) && (v >> 32) > 0 && (v & 0xFFFFFFFF) > 0)
                { FpsNum = (int)(v >> 32); FpsDen = (int)(v & 0xFFFFFFFF); }
            }
            finally { MfApi.Release(nt); }
            // salida NV12 (8 bits) o P010 (10 bits)
            if (!SetOut("NV12") && !SetOut("P010")) throw new InvalidOperationException("el decodificador no da NV12 ni P010 (" + Codec + ")");
            ReadGeometry();
            // duracion
            IntPtr pv = Marshal.AllocHGlobal(24);
            try
            {
                for (int i = 0; i < 24; i++) Marshal.WriteByte(pv, i, 0);
                Guid k = MfFileApi.MF_PD_DURATION;
                if (rd.GetPresentationAttribute(MfFileApi.MEDIASOURCE, ref k, pv) >= 0) DurationHns = Marshal.ReadInt64(pv, 8);
            }
            finally { Marshal.FreeHGlobal(pv); }
            Frames = DurationHns > 0 ? (long)Math.Floor(DurationHns / 1e7 * FpsNum / FpsDen + 0.5) : 0;
            // la hora de la imagen 0 (la primera del fichero, antes de cualquier salto): queda leida como la actual (Index 0)
            if (!Read()) throw new System.IO.InvalidDataException("el video no tiene ninguna imagen");
            // El ritmo de verdad, de las horas de las primeras imagenes (algunos webm no lo dicen y MF pone 60: uno de 120 salia a 60 con
            // las imagenes contadas dos veces). Si no casa con el del tipo (> 2 %), manda el medido y se vuelve a la 0
            CheckRate();
        }

        void CheckRate()
        {
            long[] ts = new long[17]; int n = 0;
            ts[n++] = lastTs;
            while (n < ts.Length && Read()) ts[n++] = lastTs;
            if (n >= 4)
            {
                // (media de los intervalos, no la mediana: en webm las horas van en ms y a 120/s salen 8 y 9 ms)
                long[] d = new long[n - 1];
                for (int i = 1; i < n; i++) d[i - 1] = ts[i] - ts[i - 1];
                Array.Sort(d);
                double med = (ts[n - 1] - ts[0]) / (double)(n - 1);
                if (d[d.Length - 1] > 3 * d[0] + 20000) med = d[d.Length / 2];   // (un hueco: la mediana)
                if (med > 0)
                {
                    double fps = 1e7 / med, cur = FpsNum / (double)FpsDen;
                    if (Math.Abs(fps - cur) / cur > 0.02)
                    {
                        // el ritmo de siempre mas cercano (las horas de un webm van en ms: 119,88 y 120 no se distinguen -> 120)
                        int[][] std = { new[] { 24000, 1001 }, new[] { 24, 1 }, new[] { 25, 1 }, new[] { 30000, 1001 }, new[] { 30, 1 }, new[] { 50, 1 },
                            new[] { 60000, 1001 }, new[] { 60, 1 }, new[] { 90, 1 }, new[] { 100, 1 }, new[] { 120, 1 }, new[] { 144, 1 }, new[] { 165, 1 }, new[] { 240, 1 } };
                        int bi = -1; double be = 1;
                        for (int i = 0; i < std.Length; i++) { double e = Math.Abs(fps - std[i][0] / (double)std[i][1]) / fps; if (e < be - 1e-4) { be = e; bi = i; } }
                        if (bi >= 0 && be < 0.015) { FpsNum = std[bi][0]; FpsDen = std[bi][1]; }
                        else { FpsNum = (int)Math.Round(fps * 1000); FpsDen = 1000; }
                        L("Video: el tipo dice " + cur.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "/s y las horas de las imagenes " +
                            fps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "/s: va a " + (FpsNum / (double)FpsDen).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                        if (DurationHns > 0) Frames = (long)Math.Floor(DurationHns / 1e7 * FpsNum / FpsDen + 0.5);
                    }
                }
            }
            Decoded = 0;
            Seek(0);
            if (!Read()) throw new System.IO.InvalidDataException("el video no vuelve a la primera imagen");
            Decoded = 0;
        }

        static string FourCCText(uint f)
        {
            char[] c = new char[4];
            for (int i = 0; i < 4; i++) { char ch = (char)((f >> (8 * i)) & 0xFF); c[i] = ch >= ' ' && ch < 127 ? ch : '?'; }
            return new string(c).Trim();
        }

        bool SetOut(string fourcc)
        {
            IMFMediaType t;
            MfHr.Check(MfApi.MFCreateMediaType(out t), "MFCreateMediaType");
            try
            {
                MfApi.SetGuid((IMFAttributes)t, MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Video, "major");
                MfApi.SetGuid((IMFAttributes)t, MfGuids.MF_MT_SUBTYPE, MfGuids.FromFourCC(MfGuids.FourCC(fourcc)), "subtype");
                int hr = rd.SetCurrentMediaType(MfApi.FIRST_VIDEO_STREAM, IntPtr.Zero, t);
                if (hr < 0) return false;
                TenBit = fourcc == "P010";
                return true;
            }
            finally { MfApi.Release(t); }
        }

        void ReadGeometry()
        {
            IMFMediaType ct;
            MfHr.Check(rd.GetCurrentMediaType(MfApi.FIRST_VIDEO_STREAM, out ct), "GetCurrentMediaType");
            try
            {
                ulong v;
                if (!MfApi.GetU64((IMFAttributes)ct, MfGuids.MF_MT_FRAME_SIZE, out v)) throw new InvalidOperationException("sin tamaño de imagen");
                int fw = (int)(v >> 32), fh = (int)(v & 0xFFFFFFFF);
                bufH = fh; Width = fw; Height = fh; offX = 0; offY = 0;
                // lo visible (H.264 a 1080 va en 1088 con recorte)
                IntPtr area = Marshal.AllocHGlobal(16);
                try
                {
                    Guid[] keys = { MfFileApi.MF_MT_MINIMUM_DISPLAY_APERTURE, MfFileApi.MF_MT_GEOMETRIC_APERTURE };
                    foreach (Guid g in keys)
                    {
                        Guid k = g; uint got;
                        if (((IMFAttributes)ct).GetBlob(ref k, area, 16, out got) >= 0 && got >= 16)
                        {
                            int ox = Marshal.ReadInt16(area, 2), oy = Marshal.ReadInt16(area, 6), cx = Marshal.ReadInt32(area, 8), cy = Marshal.ReadInt32(area, 12);
                            if (cx > 0 && cy > 0 && ox >= 0 && oy >= 0 && ox + cx <= fw && oy + cy <= fh) { Width = cx; Height = cy; offX = ox & ~1; offY = oy & ~1; break; }
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(area); }
            }
            finally { MfApi.Release(ct); }
        }

        void DropCur() { if (cur != null) { MfApi.Release(cur); cur = null; } }

        // La siguiente imagen (en orden de presentacion). false = fin del fichero
        public bool Read()
        {
            DropCur();
            while (true)
            {
                uint idx = 0, flags = 0; long ts = 0; IntPtr ps = IntPtr.Zero;
                int hr = rd.ReadSample(MfApi.FIRST_VIDEO_STREAM, 0, (IntPtr)(&idx), (IntPtr)(&flags), (IntPtr)(&ts), (IntPtr)(&ps));
                if (hr < 0) { if (ps != IntPtr.Zero) Marshal.Release(ps); MfHr.Check(hr, "ReadSample"); }
                if ((flags & MfApi.READERF_CURRENTMEDIATYPECHANGED) != 0) ReadGeometry();
                if (ps == IntPtr.Zero)
                {
                    if ((flags & MfApi.READERF_ENDOFSTREAM) != 0) { if (Index >= 0 && Index + 1 != Frames) Frames = Index + 1; Index = -1; return false; }
                    if ((flags & MfApi.READERF_ERROR) != 0) throw new InvalidOperationException("error al leer el video");
                    continue;   // (STREAMTICK u otra cosa sin imagen)
                }
                cur = (IMFSample)Marshal.GetObjectForIUnknown(ps);
                Marshal.Release(ps);
                lastTs = ts;
                if (ts0 == long.MinValue) ts0 = ts;
                Index = (long)Math.Floor((ts - ts0) / 1e7 * FpsNum / FpsDen + 0.5);
                Decoded++;
                if ((flags & MfApi.READERF_ENDOFSTREAM) != 0) { }
                return true;
            }
        }

        // La imagen leida, bloqueada: act(indice, Y, pitch, UV, pitch, p010)
        public void WithFrame(FrameAction act)
        {
            if (cur == null) throw new InvalidOperationException("no hay imagen leida");
            IMFMediaBuffer mb;
            MfHr.Check(cur.GetBufferByIndex(0, out mb), "GetBufferByIndex");
            try
            {
                if (!firstLogged)
                {
                    firstLogged = true;
                    IntPtr pu = Marshal.GetIUnknownForObject(mb), pd;
                    Guid g = MfFileApi.IID_IMFDXGIBuffer;
                    Hardware = Marshal.QueryInterface(pu, ref g, out pd) >= 0;
                    if (pd != IntPtr.Zero) Marshal.Release(pd);
                    Marshal.Release(pu);
                }
                IMF2DBuffer2 b2 = mb as IMF2DBuffer2;
                IntPtr scan, start; int pitch; uint len;
                if (b2 != null && b2.Lock2DSize(1 /*READ*/, out scan, out pitch, out start, out len) >= 0)
                {
                    try
                    {
                        // filas por plano: lo que mide la superficie de verdad (la de la grafica puede ir alineada a mas que el tipo)
                        int rows = bufH;
                        if (pitch > 0 && len > 0) { long r = (long)len * 2 / (3L * pitch); if (r >= Height && r < bufH + 256) rows = (int)r; }
                        Call(act, (byte*)scan, pitch, rows);
                    }
                    finally { b2.Unlock2D(); }
                    return;
                }
                IMF2DBuffer b1 = mb as IMF2DBuffer;
                if (b1 != null && b1.Lock2D(out scan, out pitch) >= 0)
                {
                    try { Call(act, (byte*)scan, pitch, bufH); }
                    finally { b1.Unlock2D(); }
                    return;
                }
                IntPtr p; uint max, curLen;
                MfHr.Check(mb.Lock(out p, out max, out curLen), "IMFMediaBuffer.Lock");
                try
                {
                    int bpp = TenBit ? 2 : 1;
                    int pt = (int)((long)curLen * 2 / (3L * bufH));
                    if (pt < Width * bpp) pt = Width * bpp;
                    Call(act, (byte*)p, pt, bufH);
                }
                finally { mb.Unlock(); }
            }
            finally { MfApi.Release(mb); }
        }

        void Call(FrameAction act, byte* scan, int pitch, int rows)
        {
            int bpp = TenBit ? 2 : 1;
            byte* y = scan + (long)offY * pitch + offX * bpp;
            byte* uv = scan + (long)rows * pitch + (long)(offY / 2) * pitch + offX * bpp;
            act(Index, y, pitch, uv, pitch, TenBit);
        }

        // Ir a la imagen f: la siguiente Read() da la imagen clave de antes (o la misma) y luego en orden
        public void Seek(long f)
        {
            DropCur();
            long hns = (ts0 == long.MinValue ? 0 : ts0) + (long)Math.Round(f * 1e7 * FpsDen / FpsNum);
            if (hns < 0) hns = 0;
            IntPtr pv = Marshal.AllocHGlobal(24);
            try
            {
                for (int i = 0; i < 24; i++) Marshal.WriteByte(pv, i, 0);
                Marshal.WriteInt16(pv, 0, 20);   // VT_I8
                Marshal.WriteInt64(pv, 8, hns);
                Guid nul = Guid.Empty;
                MfHr.Check(rd.SetCurrentPosition(ref nul, pv), "SetCurrentPosition");
            }
            finally { Marshal.FreeHGlobal(pv); }
            Index = -1;
        }

        public string Describe()
        {
            return System.IO.Path.GetFileName(Path) + " " + Codec + (TenBit ? " 10 bits" : "") + " " + Width + "x" + Height + " a " +
                (FpsNum / (double)FpsDen).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "/s, " + Frames + " imagenes" +
                (WantHw ? (firstLogged ? (Hardware ? ", por la grafica" : ", SIN la grafica (el decodificador no la usa)") : ", por la grafica (pedido)") : ", sin la grafica");
        }

        public void Dispose()
        {
            DropCur();
            if (rd != null) { MfApi.Release(rd); rd = null; }
            if (mgr != null) { MfApi.Release(mgr); mgr = null; }
            if (mgrPtr != IntPtr.Zero) { Marshal.Release(mgrPtr); mgrPtr = IntPtr.Zero; }
            if (ctx != IntPtr.Zero) { Marshal.Release(ctx); ctx = IntPtr.Zero; }
            if (dev != IntPtr.Zero) { Marshal.Release(dev); dev = IntPtr.Zero; }
            if (mfUp) { mfUp = false; MfApi.MFShutdown(); }
        }
    }
}
