using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ConsoleEnhancer.Motor
{
    // 22-11 (1.2.4, ronda de arreglos, RELEVO): test=archivo:<ruta> - un fichero de video como si fuera la capturadora, para probar el
    // camino entero (lanzador -> motor) sin la consola ni la GC573:
    //  - .y4m (YUV4MPEG2 de 8 bits: C420jpeg / C420 / C420mpeg2 / C420paldv / C422 / C444 / mono) o crudo YUY2 (imagenes YUY2 seguidas;
    //    su tamaño, test_archivo_tam=WxH, el _WxH del nombre o el de la captura).
    //  - 23-11 (ronda 7, GAMEPLAY): VIDEO COMPRIMIDO (.mp4 .mkv .webm .mov .m4v .ts: H.264 / HEVC / VP9 / AV1, lo que traiga) por Media
    //    Foundation, decodificado por la grafica (FileSourceMf.cs): video REAL de juego en movimiento sin pasar minutos a y4m.
    //  - Se entrega en YUY2 al tamaño de la captura pedida (capture=; FileConv.cs: AREA al reducir -4K a 1440p / 1080p-, bilineal al
    //    agrandar), como la GC573 con la PS5 (format=yuy2), a fps= (59,94 de serie) y con el reloj del sistema: la imagen n se entrega a los
    //    n/59,94 s del QPC. Todos los procesos que leen el mismo fichero dan la MISMA imagen a la vez (como la capturadora compartida): el
    //    relevo de un motor a otro se ve sin saltos en el contenido. test_archivo_reloj=0: desde la primera imagen del fichero al abrirse.
    //  - "Imagen del juego" k = la imagen desde + k * paso del fichero. test_archivo_paso=2 con un video de 60: el juego va a 30 (cada
    //    imagen 2 veces en la señal, identicas, como el HDMI) y las impares que no pasan son la VERDAD de las inventadas x2; paso=4: juego
    //    a 15 y las 3 de en medio, la verdad de x4. test_archivo_fps cambia el ritmo del fichero (de serie el suyo: la F del .y4m, el del
    //    video; en crudo 29,97 en el modo 30 y 59,94 en el 60). Nunca mas rapido que la señal.
    //  - test_archivo_desde / _hasta (segundos del fichero): solo ese trozo, en bucle. test_archivo_quieto=<s>: siempre esa imagen (el
    //    reescalado "quieto"). Al acabar vuelve a empezar (un corte de escena); test_archivo_vuelta=1: ida y vuelta (solo y4m / crudo).
    //  - test_archivo_codigo=1 | arriba: el numero k de la imagen del juego en codigo Gray (16 cuadros, Parallax / FileConv): las
    //    herramientas de medida saben que imagen del fichero es cada real y cual es la verdad de cada inventada. test_archivo_registro=
    //    <fichero.csv>: senal n, hora QPC, k e imagen del fichero de cada entrega.
    //  - test_paso=1 (como TestSource): la siguiente solo cuando el motor ha cogido la anterior (sin reloj; para volcar sin perder ninguna).
    //  - Un hilo prepara las imagenes por delante (lectura / decodificacion + conversion) en un anillo; otro las entrega a su hora. Si la
    //    preparacion no llega, se repite la anterior y se cuenta ("tarde" en el log cada 10 s, si las hay).
    partial class Settings
    {
        public string FileFps = "";          // test_archivo_fps=30000/1001 | 29.97 | 60...: el ritmo del juego en el fichero ("" = el suyo)
        public string FileSize = "";         // test_archivo_tam=WxH (solo crudo YUY2)
        public bool FilePingPong = false;    // test_archivo_vuelta=1
        public bool FileClock = true;        // test_archivo_reloj=0: sin el reloj comun
        public double FileJitterMs = 0;      // test_archivo_temblor_ms=N: cada entrega +-N ms al azar (el temblor de DirectShow; 0 = puntual)
        // 23-11 (ronda 7, GAMEPLAY)
        public int FileStep = 1;             // test_archivo_paso=N: una de cada N imagenes del fichero es del juego (las demas: la verdad)
        public double FileFrom = 0;          // test_archivo_desde=<s>
        public double FileTo = 0;            // test_archivo_hasta=<s> (0 = hasta el final)
        public double FileStill = -1;        // test_archivo_quieto=<s> (-1 = no)
        public int FileCode = 0;             // test_archivo_codigo=1 (abajo a la izquierda) | 2 = arriba
        public int FileFilter = 0;           // test_archivo_filtro=area | bilineal (0 = area al reducir, bilineal al agrandar)
        public bool FileHw = true;           // test_archivo_hw=0: el video sin la grafica
        public string FileLog = "";          // test_archivo_registro=<csv>

        bool ApplyArchivo(string key, string val)
        {
            switch (key)
            {
                case "test_archivo_fps": FileFps = val.Trim(); return true;
                case "test_archivo_tam": FileSize = val.Trim().ToLowerInvariant(); return true;
                case "test_archivo_vuelta": FilePingPong = B(val); return true;
                case "test_archivo_reloj": FileClock = B(val); return true;
                case "test_archivo_temblor_ms": FileJitterMs = Math.Max(0, D(val)); return true;
                case "test_archivo_paso": FileStep = Math.Max(1, Math.Min(16, I(val))); return true;
                case "test_archivo_desde": FileFrom = Math.Max(0, D(val)); return true;
                case "test_archivo_hasta": FileTo = Math.Max(0, D(val)); return true;
                case "test_archivo_quieto": FileStill = val.Trim().Length == 0 ? -1 : D(val); return true;
                case "test_archivo_codigo": { string v = val.Trim().ToLowerInvariant(); FileCode = v == "arriba" || v == "2" ? 2 : B(v) || v == "abajo" ? 1 : 0; return true; }
                case "test_archivo_filtro": { string v = val.Trim().ToLowerInvariant(); FileFilter = v == "area" ? FileConv.FiltroArea : v == "bilineal" ? FileConv.FiltroBilineal : FileConv.FiltroAuto; return true; }
                case "test_archivo_hw": FileHw = B(val); return true;
                case "test_archivo_registro": FileLog = val.Trim().Trim('"'); return true;
            }
            return false;
        }

        public bool IsFileTest { get { return Test.StartsWith("archivo:", StringComparison.OrdinalIgnoreCase); } }
        public string FileTestPath { get { return IsFileTest ? Test.Substring(8).Trim().Trim('"') : ""; } }
    }

    unsafe class FileSource : IFrameSource
    {
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

        public event FrameHandler Frame;
        readonly Settings s;
        readonly string path;
        readonly int w, h;                  // lo que se entrega (la captura pedida)
        readonly int sigNum, sigDen;        // el ritmo de la señal (fps=)
        int fw, fh, csx = 2, csy = 2;       // el fichero: tamaño y submuestreo del color (1,1 = 444; 2,1 = 422; 2,2 = 420; 0 = sin color)
        bool raw, video;                    // crudo YUY2 / video comprimido (Media Foundation)
        long dataStart, frameBytes, frameStride;
        long frames;                        // imagenes del fichero
        int fileNum, fileDen;               // el ritmo del fichero (ajustado para que el del juego sea la señal / m)
        double fileFpsNative;               // el suyo
        // las imagenes del juego: k = 0..K-1 -> imagen del fichero F0 + k * P (o siempre stillF)
        int P = 1; long F0, K = 1, stillF = -1;
        string desc = "";
        FileStream fs;
        byte[] readBuf;                     // una imagen del fichero tal cual (y4m / crudo)
        MfVideoFile mf;                     // video comprimido
        FileConv conv;
        // El anillo de imagenes ya en YUY2 al tamaño de la captura, por imagen del juego k (ranura k % Ring)
        int Ring = 4;
        byte[][] ring;
        GCHandle[] ringPin;
        long[] ringFrame;                   // la k que tiene cada ranura (-1 = nada)
        readonly object ringLock = new object();
        volatile bool stop;
        Thread deliver, prep;
        long nNext;                         // la siguiente imagen de la señal a entregar
        readonly AutoResetEvent prepWake = new AutoResetEvent(false);
        long late, lateLogged, delivered;
        long lastLogAt;
        readonly Random jitter = new Random(20261122);
        StreamWriter reg; long regFlushAt;

        public FileSource(Settings s)
        {
            this.s = s;
            path = s.FileTestPath;
            w = s.CapW; h = s.CapH; sigNum = s.FpsNum; sigDen = s.FpsDen;
        }

        public string Name { get { return "archivo " + desc; } }

        static bool IsVideoExt(string p)
        {
            string e = Path.GetExtension(p).ToLowerInvariant();
            return e == ".mp4" || e == ".mkv" || e == ".webm" || e == ".mov" || e == ".m4v" || e == ".ts" || e == ".mts" || e == ".m2ts" || e == ".avi" || e == ".wmv";
        }

        // ---- Lo del fichero
        void Open()
        {
            if (!File.Exists(path)) throw new FileNotFoundException("No esta el fichero de la fuente de prueba", path);
            video = IsVideoExt(path);
            raw = !video && !path.EndsWith(".y4m", StringComparison.OrdinalIgnoreCase);
            int hn = 0, hd = 0;
            if (video)
            {
                MfVideoFile.Logger = Log.Write;
                mf = new MfVideoFile(path, s.FileHw);
                fw = mf.Width; fh = mf.Height; csx = 2; csy = 2;
                frames = mf.Frames;
                hn = mf.FpsNum; hd = mf.FpsDen;
                desc = mf.Describe();
                Ring = 8;   // (la decodificacion va a tirones: las imagenes clave tardan mas)
                if (s.FilePingPong) Log.Write("Fuente de prueba (archivo): ida y vuelta no vale con video comprimido; va en bucle");
            }
            else if (!raw)
            {
                fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.RandomAccess);
                // YUV4MPEG2 W3840 H2160 F30000:1001 Ip A1:1 C420jpeg XYSCSS=...\n y cada imagen "FRAME[ params]\n" + Y + U + V
                StringBuilder sb = new StringBuilder();
                int c;
                while ((c = fs.ReadByte()) >= 0 && c != '\n') { sb.Append((char)c); if (sb.Length > 4096) throw new InvalidDataException("cabecera y4m demasiado larga"); }
                string hdr = sb.ToString();
                if (!hdr.StartsWith("YUV4MPEG2", StringComparison.Ordinal)) throw new InvalidDataException("no es YUV4MPEG2: " + hdr);
                string chroma = "420jpeg";
                foreach (string t in hdr.Split(' '))
                {
                    if (t.Length < 2) continue;
                    string v = t.Substring(1);
                    switch (t[0])
                    {
                        case 'W': fw = int.Parse(v, CultureInfo.InvariantCulture); break;
                        case 'H': fh = int.Parse(v, CultureInfo.InvariantCulture); break;
                        case 'F': { string[] p = v.Split(':'); hn = int.Parse(p[0], CultureInfo.InvariantCulture); hd = p.Length > 1 ? int.Parse(p[1], CultureInfo.InvariantCulture) : 1; break; }
                        case 'C': chroma = v; break;
                    }
                }
                if (chroma.StartsWith("420", StringComparison.Ordinal)) { csx = 2; csy = 2; }
                else if (chroma == "422") { csx = 2; csy = 1; }
                else if (chroma == "444") { csx = 1; csy = 1; }
                else if (chroma == "mono") { csx = 0; csy = 0; }
                else throw new InvalidDataException("color del y4m no admitido: C" + chroma + " (8 bits: 420, 422, 444, mono)");
                long cw = csx == 0 ? 0 : (fw + csx - 1) / csx, ch = csy == 0 ? 0 : (fh + csy - 1) / csy;
                frameBytes = (long)fw * fh + 2 * cw * ch;
                dataStart = fs.Position;
                // La primera marca FRAME (puede llevar parametros): su largo vale para todas (en la practica, "FRAME\n")
                int mk = 0;
                while ((c = fs.ReadByte()) >= 0) { mk++; if (c == '\n') break; if (mk > 256) throw new InvalidDataException("marca FRAME rara"); }
                frameStride = mk + frameBytes;
                frames = (fs.Length - dataStart) / frameStride;
                desc = Path.GetFileName(path) + " " + fw + "x" + fh + " C" + chroma;
            }
            else
            {
                fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.RandomAccess);
                string tam = s.FileSize;
                if (tam.Length == 0)
                {
                    System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(path), "(\\d{3,5})x(\\d{3,5})");
                    tam = m.Success ? m.Groups[1].Value + "x" + m.Groups[2].Value : w + "x" + h;
                }
                string[] p = tam.Split('x');
                fw = int.Parse(p[0], CultureInfo.InvariantCulture); fh = int.Parse(p[1], CultureInfo.InvariantCulture);
                csx = 2; csy = 1;
                frameBytes = (long)fw * fh * 2;
                frameStride = frameBytes;
                dataStart = 0;
                frames = fs.Length / frameStride;
                desc = Path.GetFileName(path) + " " + fw + "x" + fh + " YUY2 crudo";
            }
            if (frames <= 0 && !video) throw new InvalidDataException("el fichero no tiene ninguna imagen entera (" + fs.Length + " bytes, " + frameStride + " por imagen)");
            if (frames <= 0) frames = 1 << 30;   // (video sin duracion: se sabra al llegar al final)
            // El ritmo del fichero
            if (!ParseRate(s.FileFps, out fileNum, out fileDen))
            {
                if (hn > 0 && hd > 0) { fileNum = hn; fileDen = hd; }
                else if (s.Mode == 30) { fileNum = 30000; fileDen = 1001; }
                else { fileNum = 60000; fileDen = 1001; }
            }
            // El ritmo del JUEGO (el del fichero / paso): nunca mas rapido que la señal, y si es la señal / m (30 en 59,94, 60 en 59,94, 120 /
            // 4 en 59,94) se clava a la señal / m, como una consola (sin un salto de cadencia cada ~30 s). Antes se recortaba el del FICHERO a
            // la señal: un video de 120 con paso 4 iba a camara lenta (15 imagenes del juego en vez de 30)
            P = Math.Max(1, s.FileStep);
            double nfps = fileNum / (double)fileDen;   // (el suyo, sin ajustar: para pasar segundos a imagenes igual que las herramientas)
            {
                double gr = fileNum / (double)fileDen / P, sr = sigNum / (double)sigDen;
                if (gr >= sr) { fileNum = sigNum * P; fileDen = sigDen; }
                else
                {
                    int m = (int)Math.Round(sr / gr);
                    if (m >= 1 && Math.Abs(sr / m - gr) / gr < 0.003) { fileNum = sigNum * P; fileDen = sigDen * m; }
                }
            }
            double ffps = fileNum / (double)fileDen;   // (el del fichero, ya ajustado)
            // Las imagenes del juego
            F0 = Math.Min(frames - 1, (long)Math.Round(s.FileFrom * nfps));
            long end = s.FileTo > 0 ? Math.Min(frames, (long)Math.Round(s.FileTo * nfps)) : frames;
            if (end <= F0) end = Math.Min(frames, F0 + 1);
            K = Math.Max(1, (end - F0 + P - 1) / P);
            stillF = s.FileStill >= 0 ? Math.Min(frames - 1, (long)Math.Round(s.FileStill * nfps)) : -1;
            fileFpsNative = nfps;
            if (!video) readBuf = new byte[frameBytes];
            ring = new byte[Ring][]; ringPin = new GCHandle[Ring]; ringFrame = new long[Ring];
            for (int i = 0; i < Ring; i++) { ring[i] = new byte[(long)w * h * 2]; ringPin[i] = GCHandle.Alloc(ring[i], GCHandleType.Pinned); ringFrame[i] = -1; }
            if (!(raw && fw == w && fh == h)) conv = new FileConv(fw, fh, csx, csy, w, h, s.FileFilter);
            desc += string.Format(CultureInfo.InvariantCulture, " -> YUY2 {0}x{1} a {2:0.###}/s{3}; juego a {4:0.###}/s (paso {5}), {6}{7}{8}{9}{10}",
                w, h, sigNum / (double)sigDen, conv != null ? " (" + conv.Desc + ")" : "", ffps / P, P,
                stillF >= 0 ? "QUIETA en la imagen " + stillF : "imagenes " + F0 + "-" + (F0 + (K - 1) * P) + " del fichero (" + K + " del juego)",
                s.FileClock ? ", reloj comun" : "", s.FilePingPong && !video ? ", ida y vuelta" : "",
                s.FileCode > 0 ? (s.FileCode == 2 ? ", codigo arriba" : ", codigo abajo") : "", video ? "" : "");
            if (s.FileLog.Length > 0)
            {
                // (en la app hay varios motores con la misma clave -el de delante, el de espera, el de reserva-: el que no puede abrirlo
                // escribe el suyo con su numero de proceso)
                string rp = s.FileLog;
                for (int t = 0; t < 2 && reg == null; t++)
                {
                    try { reg = new StreamWriter(new FileStream(rp, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)); reg.WriteLine("n,qpc,k,f,repetida"); }
                    catch (Exception ex)
                    {
                        if (t == 0) rp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(s.FileLog)), Path.GetFileNameWithoutExtension(s.FileLog) + "_" + System.Diagnostics.Process.GetCurrentProcess().Id + Path.GetExtension(s.FileLog));
                        else Log.Write("Fuente de prueba: no se puede escribir el registro " + s.FileLog + ": " + ex.Message);
                    }
                }
            }
        }

        static bool ParseRate(string v, out int num, out int den)
        {
            num = 0; den = 1;
            if (string.IsNullOrEmpty(v)) return false;
            string[] p = v.Split('/', ':');
            if (p.Length == 2) { num = int.Parse(p[0], CultureInfo.InvariantCulture); den = int.Parse(p[1], CultureInfo.InvariantCulture); return num > 0 && den > 0; }
            double d = double.Parse(v, CultureInfo.InvariantCulture);
            if (d <= 0) return false;
            // 29.97 / 59.94 / 23.976 -> x1000/1001
            double r = d * 1001 / 1000;
            if (Math.Abs(r - Math.Round(r)) < 0.01) { num = (int)Math.Round(r) * 1000; den = 1001; }
            else { num = (int)Math.Round(d * 1000); den = 1000; }
            return true;
        }

        // ---- La imagen del fichero f -> YUY2 al tamaño de la captura en dst, con el codigo de k
        void ConvertPlanar(byte* Y, byte* U, byte* V, byte[] dst, long k)
        {
            fixed (byte* d = dst)
            {
                int cw = csx == 0 ? 1 : (fw + csx - 1) / csx;
                conv.Run(Y, fw, 1, U, V, cw, 1, d, w * 2);
                Stamp(d, k);
            }
        }

        void Stamp(byte* d, long k) { if (s.FileCode > 0) FileConv.StampYuy2(d, w * 2, w, h, k, s.FileCode == 2); }

        void DecodeFileFrame(long f, long k, byte[] dst)
        {
            long off = dataStart + f * frameStride + (frameStride - frameBytes);
            fs.Seek(off, SeekOrigin.Begin);
            int got = 0;
            while (got < frameBytes)
            {
                int r = fs.Read(readBuf, got, (int)Math.Min(1 << 24, frameBytes - got));
                if (r <= 0) break;
                got += r;
            }
            if (got < frameBytes) throw new EndOfStreamException("imagen " + f + " cortada");
            fixed (byte* src = readBuf)
            {
                if (raw && conv == null) { fixed (byte* d = dst) { Buffer.MemoryCopy(src, d, dst.Length, frameBytes); Stamp(d, k); } return; }
                if (raw) { fixed (byte* d = dst) { conv.Run(src, fw * 2, 2, src + 1, src + 3, fw * 2, 4, d, w * 2); Stamp(d, k); } return; }
                byte* Y = src, U = src + (long)fw * fh, V = U + (csx == 0 ? 0 : (long)((fw + csx - 1) / csx) * ((fh + csy - 1) / csy));
                ConvertPlanar(Y, U, V, dst, k);
            }
        }

        // la imagen del video que ya esta leida (mf.Index) -> dst
        void ConvertVideo(long k, byte[] dst)
        {
            MfVideoFile.FrameAction act = delegate (long idx, byte* y, int py, byte* uv, int puv, bool p010)
            {
                fixed (byte* d = dst)
                {
                    if (!p010) conv.Run(y, py, 1, uv, uv + 1, puv, 2, d, w * 2);
                    else conv.Run(y + 1, py, 2, uv + 1, uv + 3, puv, 4, d, w * 2);
                    Stamp(d, k);
                }
            };
            mf.WithFrame(act);
        }

        // ---- El reloj: la imagen n de la señal, la k del juego que le toca y su imagen del fichero
        double SigFps { get { return sigNum / (double)sigDen; } }
        long t0Qpc;          // sin reloj comun: la hora de la imagen 0
        long nBase;          // sin reloj comun: la primera imagen

        long SignalAt(long qpc) { return (long)Math.Floor((qpc - t0Qpc) / (double)System.Diagnostics.Stopwatch.Frequency * SigFps); }
        long QpcOf(long n) { return t0Qpc + (long)Math.Round(n / SigFps * System.Diagnostics.Stopwatch.Frequency); }

        long GameOf(long n)
        {
            if (stillF >= 0) return 0;
            long g = n - nBase;
            if (g < 0) g = 0;
            // imagen del juego: g * (ritmo del fichero / paso / ritmo de la señal)
            long j = (long)Math.Floor(g * ((double)fileNum * sigDen) / ((double)fileDen * sigNum * P) + 1e-9);
            if (!s.FilePingPong || video || K < 2) return j % K;
            long period = 2L * K - 2;
            long q = j % period;
            return q < K ? q : period - q;
        }

        long FileOf(long k) { return stillF >= 0 ? stillF : F0 + k * P; }
        int Slot(long k) { return (int)(k % Ring); }

        public void Start()
        {
            Open();
            bool own = !s.FileClock || s.TestLockstep;   // (test_paso=1: una a una, desde la primera del trozo)
            if (!own) { t0Qpc = 0; nBase = 0; }
            else { t0Qpc = Clock.Now; nBase = 0; }
            nNext = SignalAt(Clock.Now) + 1;
            if (own) nBase = nNext;
            // La primera ya preparada (que salga enseguida)
            long k0 = GameOf(nNext);
            long t = Clock.Now;
            if (video) VideoPrepare(k0, true);
            else { DecodeFileFrame(FileOf(k0), k0, ring[Slot(k0)]); ringFrame[Slot(k0)] = k0; }
            Log.Write("Fuente de prueba: " + desc + " (empieza en la imagen " + FileOf(k0) + " del fichero, k " + k0 + "; la primera en " +
                Clock.SinceMs(t).ToString("0", CultureInfo.InvariantCulture) + " ms" + (video ? "; " + mf.Describe() : "") + ")");
            prep = new Thread(PrepLoop); prep.IsBackground = true; prep.Name = "fuente archivo (preparar)"; prep.Priority = ThreadPriority.AboveNormal; prep.Start();
            deliver = new Thread(DeliverLoop); deliver.IsBackground = true; deliver.Name = "fuente archivo"; deliver.Priority = ThreadPriority.Highest; deliver.Start();
        }

        // Prepara por delante las imagenes del juego que tocan a las proximas de la señal (las distintas, hasta llenar el anillo menos la
        // que se esta entregando)
        void PrepLoop()
        {
            try
            {
                long[] need = new long[Ring];
                while (!stop)
                {
                    long n = nNext;
                    long cur = GameOf(n);
                    int cnt = 0; long seen = -1;
                    for (long q = n; cnt < Ring - 1 && q < n + 256; q++) { long k = GameOf(q); if (k == seen) continue; seen = k; need[cnt++] = k; }
                    long want = -1;
                    for (int i = 0; i < cnt; i++)
                    {
                        bool have; int sl = Slot(need[i]);
                        lock (ringLock) have = ringFrame[sl] == need[i];
                        if (have) continue;
                        // (no pisar la que se entrega ahora)
                        if (need[i] != cur && ringFrame[sl] == cur) break;
                        want = need[i]; break;
                    }
                    if (want < 0) { prepWake.WaitOne(4); continue; }
                    if (video) VideoPrepare(want, false);
                    else
                    {
                        int sl = Slot(want);
                        lock (ringLock) ringFrame[sl] = -1;
                        DecodeFileFrame(FileOf(want), want, ring[sl]);
                        lock (ringLock) ringFrame[sl] = want;
                    }
                }
            }
            catch (Exception ex) { if (!stop) Log.Error("Fuente archivo (preparar)", ex); }
        }

        // Video: decodifica en orden hasta la imagen del fichero de 'want' (convirtiendo por el camino las del juego que hagan falta); si
        // hay que volver atras (bucle) o esta muy lejos, salta (a la imagen clave de antes)
        long seeks;
        void VideoPrepare(long want, bool first)
        {
            long wf = FileOf(want);
            bool useCur = mf.Index == wf;   // (la que ya esta leida es la que hace falta: al abrir, la 0)
            if (!useCur && (mf.Index < 0 || wf < mf.Index || wf - mf.Index > 300)) { mf.Seek(wf); seeks++; }
            while (!stop || first)
            {
                bool got = useCur || mf.Read();
                useCur = false;
                if (!got)
                {
                    // fin del fichero antes de lo esperado (la duracion no casaba): se recorta y vuelta a empezar
                    frames = Math.Max(1, mf.Frames);
                    long end = Math.Min(frames, s.FileTo > 0 ? (long)Math.Round(s.FileTo * fileFpsNative) : frames);
                    if (F0 >= end) F0 = 0;
                    K = Math.Max(1, (end - F0 + P - 1) / P);
                    if (stillF >= frames) stillF = frames - 1;
                    Log.Write("Fuente de prueba (archivo): el video acaba en la imagen " + (frames - 1) + "; " + K + " imagenes del juego");
                    return;   // (la siguiente vuelta del bucle vuelve a calcular lo que falta)
                }
                long i = mf.Index;
                if (i > wf)
                {
                    // (se paso: esa imagen no esta -video de ritmo variable o recortado-: va la siguiente que hay, para no saltar sin fin)
                    int sw = Slot(want);
                    lock (ringLock) ringFrame[sw] = -1;
                    ConvertVideo(want, ring[sw]);
                    lock (ringLock) ringFrame[sw] = want;
                    return;
                }
                long k = stillF >= 0 ? (i == stillF ? 0 : -1) : ((i - F0) % P == 0 && i >= F0 ? (i - F0) / P : -1);
                if (k >= 0 && k < K && (k == want || IsSoon(k)))
                {
                    int sl = Slot(k);
                    long cur = GameOf(nNext);
                    // (no pisar la que se entrega ahora: esperar a que pase)
                    while (!stop && k != cur && ringFrame[sl] == cur) { Thread.Sleep(1); cur = GameOf(nNext); }
                    lock (ringLock) ringFrame[sl] = -1;
                    ConvertVideo(k, ring[sl]);
                    lock (ringLock) ringFrame[sl] = k;
                }
                if (i == wf) return;
            }
        }

        bool IsSoon(long k)
        {
            long n = nNext; long seen = -1; int cnt = 0;
            for (long q = n; cnt < Ring - 1 && q < n + 256; q++) { long kk = GameOf(q); if (kk == seen) continue; seen = kk; cnt++; if (kk == k) return true; }
            return false;
        }

        void DeliverLoop()
        {
            timeBeginPeriod(1);
            try
            {
                long lastK = -1; int lastSlot = -1;
                bool lockstep = s.TestLockstep;
                while (!stop)
                {
                    long n = nNext;
                    long due = QpcOf(n);
                    if (s.FileJitterMs > 0) due += (long)((jitter.NextDouble() * 2 - 1) * s.FileJitterMs / Clock.TickMs);
                    while (!stop && !lockstep)
                    {
                        double left = Clock.Ms(Clock.Now, due);
                        if (left <= 0) break;
                        if (left > 2) Thread.Sleep(1); else Thread.SpinWait(200);
                    }
                    if (stop) break;
                    // (muy atrasada -el PC parado, un paron-: se salta a la de ahora, como una capturadora que pierde imagenes)
                    long nowN = SignalAt(Clock.Now);
                    if (nowN > n + 3 && !lockstep) { n = nowN; }
                    long k = GameOf(n);
                    int slot = Slot(k);
                    bool ready, repeated = false;
                    lock (ringLock) ready = ringFrame[slot] == k;
                    if (!ready)
                    {
                        // un poco mas (hasta un cuarto de imagen) y si no, la anterior otra vez
                        long until = Clock.Now + (long)((lockstep ? 3000.0 : 250.0 / SigFps) / Clock.TickMs);   // (test_paso=1: sin perder ninguna)
                        while (!stop && Clock.Now < until) { lock (ringLock) ready = ringFrame[slot] == k; if (ready) break; Thread.SpinWait(100); }
                        if (!ready) { late++; if (lastSlot >= 0) { slot = lastSlot; k = lastK; ready = true; repeated = true; } }
                    }
                    if (ready)
                    {
                        FrameHandler fh2 = Frame;
                        long at = Clock.Now;
                        if (fh2 != null) fh2(ringPin[slot].AddrOfPinnedObject(), w * 2, w, h, Shaders.FmtYuy2, at, n);
                        lastSlot = slot; lastK = k;
                        delivered++;
                        if (reg != null)
                        {
                            try
                            {
                                reg.WriteLine(n.ToString(CultureInfo.InvariantCulture) + "," + at.ToString(CultureInfo.InvariantCulture) + "," + k.ToString(CultureInfo.InvariantCulture) + "," +
                                    FileOf(k).ToString(CultureInfo.InvariantCulture) + "," + (repeated ? "1" : "0"));
                                if (Clock.SinceMs(regFlushAt) > 1000) { regFlushAt = Clock.Now; reg.Flush(); }
                            }
                            catch { }
                        }
                    }
                    nNext = n + 1;
                    // (test_paso=1: la siguiente imagen DISTINTA del juego; las repetidas -un juego a 30 en la señal de 59,94- el motor las
                    // quita como iguales y no las coge: se esperaban los 2 s enteros cada vez, SRBLUR 13:05)
                    if (lockstep && stillF < 0) { long kk = GameOf(n), q = n + 1; while (q < n + 64 && GameOf(q) == kk) q++; nNext = q; }
                    prepWake.Set();
                    if (lockstep) TestSource.Consumed.WaitOne(2000);   // (test_paso=1, como TestSource: la siguiente cuando el motor coge esta)
                    if (late != lateLogged && Clock.SinceMs(lastLogAt) > 10000)
                    {
                        lastLogAt = Clock.Now;
                        Log.Write("Fuente de prueba (archivo): " + (late - lateLogged) + " imagenes sin preparar a tiempo (se repitio la anterior); " + delivered + " entregadas" +
                            (video ? "; video: " + mf.Decoded + " decodificadas, " + seeks + " saltos" : ""));
                        lateLogged = late;
                    }
                }
            }
            catch (Exception ex) { if (!stop) Log.Error("Fuente archivo", ex); }
            finally { timeEndPeriod(1); }
        }

        public void Dispose()
        {
            stop = true;
            prepWake.Set();
            if (deliver != null) deliver.Join(500);
            if (prep != null) prep.Join(2000);
            try { if (fs != null) fs.Dispose(); } catch { }
            if (prep == null || !prep.IsAlive) { try { if (mf != null) mf.Dispose(); } catch { } mf = null; }
            try { if (reg != null) { reg.Flush(); reg.Dispose(); } } catch { }
            reg = null;
            // (si el hilo de entrega sigue dentro de la llamada al motor, las imagenes se quedan fijadas: se van con el proceso)
            if ((deliver == null || !deliver.IsAlive) && ringPin != null)
                for (int i = 0; i < Ring; i++) if (ringPin[i].IsAllocated) ringPin[i].Free();
        }
    }
}
