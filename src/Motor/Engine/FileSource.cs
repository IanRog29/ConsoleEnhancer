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
    //  - .y4m (YUV4MPEG2 de 8 bits: C420jpeg / C420 / C420mpeg2 / C420paldv / C422 / C444 / mono) o crudo YUY2 (cualquier otra extension:
    //    imagenes YUY2 seguidas; su tamaño, test_archivo_tam=WxH, el _WxH del nombre o el de la captura).
    //  - Se entrega en YUY2 al tamaño de la captura pedida (capture=; reescalado bilineal si el fichero es de otro), como la GC573 con la
    //    PS5 (format=yuy2), a fps= (59,94 de serie) y con el reloj del sistema: la imagen n se entrega a los n/59,94 s del QPC. Todos los
    //    procesos que leen el mismo fichero dan la MISMA imagen a la vez (como la capturadora compartida entre el motor y mpv): el relevo
    //    de un motor a otro se ve sin saltos en el contenido. test_archivo_reloj=0: desde la primera imagen del fichero al abrirse.
    //  - Cada imagen del fichero es una imagen del juego: con test_archivo_fps (de serie la F del .y4m; en crudo 29,97 en el modo 30 y
    //    59,94 en el 60) se repite las veces que toque en la señal de 59,94 (un juego a 30 = cada una 2 veces, identicas, como el HDMI).
    //  - Al acabar el fichero vuelve a empezar (un corte de escena); test_archivo_vuelta=1: ida y vuelta (sin corte).
    //  - Un hilo prepara las imagenes por delante (lectura + conversion) en un anillo; otro las entrega a su hora (como TestSource). Si la
    //    preparacion no llega, se repite la anterior y se cuenta ("tarde" en el log cada 10 s, si las hay).
    partial class Settings
    {
        public string FileFps = "";          // test_archivo_fps=30000/1001 | 29.97 | 60...: el ritmo del juego en el fichero ("" = el suyo)
        public string FileSize = "";         // test_archivo_tam=WxH (solo crudo YUY2)
        public bool FilePingPong = false;    // test_archivo_vuelta=1
        public bool FileClock = true;        // test_archivo_reloj=0: sin el reloj comun
        public double FileJitterMs = 0;      // test_archivo_temblor_ms=N: cada entrega +-N ms al azar (el temblor de DirectShow; 0 = puntual)

        bool ApplyArchivo(string key, string val)
        {
            switch (key)
            {
                case "test_archivo_fps": FileFps = val.Trim(); return true;
                case "test_archivo_tam": FileSize = val.Trim().ToLowerInvariant(); return true;
                case "test_archivo_vuelta": FilePingPong = B(val); return true;
                case "test_archivo_reloj": FileClock = B(val); return true;
                case "test_archivo_temblor_ms": FileJitterMs = Math.Max(0, D(val)); return true;
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
        bool raw;                           // crudo YUY2
        long dataStart, frameBytes, frameStride;
        int frames;
        int fileNum, fileDen;               // el ritmo del juego en el fichero
        string desc = "";
        FileStream fs;
        byte[] readBuf;                     // una imagen del fichero tal cual
        // El anillo de imagenes ya en YUY2 al tamaño de la captura
        const int Ring = 4;
        byte[][] ring = new byte[Ring][];
        GCHandle[] ringPin = new GCHandle[Ring];
        readonly long[] ringFrame = { -1, -1, -1, -1 };
        readonly object ringLock = new object();
        volatile bool stop;
        Thread deliver, prep;
        long nNext;                         // la siguiente imagen de la señal a entregar
        readonly AutoResetEvent prepWake = new AutoResetEvent(false);
        // Escala (si el fichero es de otro tamaño): por columna / fila, origen y peso (8 bits)
        int[] mx0, mx1, mwx, my0, my1, mwy;          // luma
        int[] cx0, cx1, cwx, cy0, cy1, cwy;          // color (a la mitad de ancho en la salida)
        long late, lateLogged, delivered;
        long lastLogAt;
        readonly Random jitter = new Random(20261122);

        public FileSource(Settings s)
        {
            this.s = s;
            path = s.FileTestPath;
            w = s.CapW; h = s.CapH; sigNum = s.FpsNum; sigDen = s.FpsDen;
        }

        public string Name { get { return "archivo " + desc; } }

        // ---- Lo del fichero
        void Open()
        {
            if (!File.Exists(path)) throw new FileNotFoundException("No esta el fichero de la fuente de prueba", path);
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.RandomAccess);
            raw = !path.EndsWith(".y4m", StringComparison.OrdinalIgnoreCase);
            int hn = 0, hd = 0;
            if (!raw)
            {
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
                frames = (int)((fs.Length - dataStart) / frameStride);
                desc = Path.GetFileName(path) + " " + fw + "x" + fh + " C" + chroma;
            }
            else
            {
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
                frames = (int)(fs.Length / frameStride);
                desc = Path.GetFileName(path) + " " + fw + "x" + fh + " YUY2 crudo";
            }
            if (frames <= 0) throw new InvalidDataException("el fichero no tiene ninguna imagen entera (" + fs.Length + " bytes, " + frameStride + " por imagen)");
            // El ritmo del juego en el fichero
            if (!ParseRate(s.FileFps, out fileNum, out fileDen))
            {
                if (hn > 0 && hd > 0) { fileNum = hn; fileDen = hd; }
                else if (s.Mode == 30) { fileNum = 30000; fileDen = 1001; }
                else { fileNum = 60000; fileDen = 1001; }
            }
            // (nunca mas rapido que la señal)
            if ((long)fileNum * sigDen > (long)sigNum * fileDen) { fileNum = sigNum; fileDen = sigDen; }
            readBuf = new byte[frameBytes];
            for (int i = 0; i < Ring; i++) { ring[i] = new byte[(long)w * h * 2]; ringPin[i] = GCHandle.Alloc(ring[i], GCHandleType.Pinned); }
            if (fw != w || fh != h) BuildScale();
            desc += string.Format(CultureInfo.InvariantCulture, ", {0} imagenes a {1:0.###}/s -> YUY2 {2}x{3} a {4:0.###}/s{5}{6}{7}", frames, fileNum / (double)fileDen, w, h,
                sigNum / (double)sigDen, fw != w || fh != h ? " (reescalado)" : "", s.FileClock ? ", reloj comun" : "", s.FilePingPong ? ", ida y vuelta" : "");
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

        void BuildScale()
        {
            Weights(w, fw, out mx0, out mx1, out mwx);
            Weights(h, fh, out my0, out my1, out mwy);
            int cwOut = w / 2, cwIn = csx == 0 ? 1 : (fw + csx - 1) / csx, chIn = csy == 0 ? 1 : (fh + csy - 1) / csy;
            Weights(cwOut, cwIn, out cx0, out cx1, out cwx);
            Weights(h, chIn, out cy0, out cy1, out cwy);
        }

        // Bilineal: para cada punto de salida, los dos de entrada y el peso del segundo (0-256), con los centros alineados
        static void Weights(int outN, int inN, out int[] a, out int[] b, out int[] wt)
        {
            a = new int[outN]; b = new int[outN]; wt = new int[outN];
            double k = inN / (double)outN;
            for (int i = 0; i < outN; i++)
            {
                double x = (i + 0.5) * k - 0.5;
                if (x < 0) x = 0;
                int x0 = (int)x;
                if (x0 > inN - 1) x0 = inN - 1;
                int x1 = Math.Min(inN - 1, x0 + 1);
                a[i] = x0; b[i] = x1; wt[i] = (int)Math.Round((x - x0) * 256);
            }
        }

        // ---- La imagen f del fichero, en YUY2 al tamaño de la captura, en dst
        void Decode(long f, byte[] dst)
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
            fixed (byte* src = readBuf) fixed (byte* d = dst)
            {
                byte* sp = src; byte* dp = d;
                if (raw && fw == w && fh == h) { Buffer.MemoryCopy(sp, dp, dst.Length, frameBytes); return; }
                if (raw) { FromYuy2Scaled(sp, dp); return; }
                byte* Y = sp, U = sp + (long)fw * fh, V = U + (csx == 0 ? 0 : (long)((fw + csx - 1) / csx) * ((fh + csy - 1) / csy));
                if (fw == w && fh == h) PlanarSame(Y, U, V, dp);
                else PlanarScaled(Y, U, V, dp);
            }
        }

        void PlanarSame(byte* Y, byte* U, byte* V, byte* d)
        {
            int cw = csx == 0 ? 0 : (fw + csx - 1) / csx;
            int ww = w, sx = csx, sy = csy, fwl = fw;
            IntPtr yp = (IntPtr)Y, up = (IntPtr)U, vp = (IntPtr)V, dp = (IntPtr)d;
            System.Threading.Tasks.Parallel.For(0, h, delegate (int y)
            {
                byte* yr = (byte*)yp + (long)y * fwl;
                byte* o = (byte*)dp + (long)y * ww * 2;
                if (sx == 0) { for (int x = 0; x < ww; x++) { o[x * 2] = yr[x]; o[x * 2 + 1] = 128; } return; }
                int cy = sy == 2 ? y >> 1 : y;
                byte* ur = (byte*)up + (long)cy * cw, vr = (byte*)vp + (long)cy * cw;
                for (int x = 0; x + 1 < ww; x += 2)
                {
                    int c = sx == 2 ? x >> 1 : x;
                    byte uu = sx == 2 ? ur[c] : (byte)((ur[c] + ur[c + 1] + 1) >> 1), vv = sx == 2 ? vr[c] : (byte)((vr[c] + vr[c + 1] + 1) >> 1);
                    o[0] = yr[x]; o[1] = uu; o[2] = yr[x + 1]; o[3] = vv;
                    o += 4;
                }
            });
        }

        void PlanarScaled(byte* Y, byte* U, byte* V, byte* d)
        {
            int cw = csx == 0 ? 1 : (fw + csx - 1) / csx;
            int ww = w, fwl = fw;
            bool mono = csx == 0;
            IntPtr yp = (IntPtr)Y, up = (IntPtr)U, vp = (IntPtr)V, dp = (IntPtr)d;
            int[] ax0 = mx0, ax1 = mx1, awx = mwx, ay0 = my0, ay1 = my1, awy = mwy, bx0 = cx0, bx1 = cx1, bwx = cwx, by0 = cy0, by1 = cy1, bwy = cwy;
            System.Threading.Tasks.Parallel.For(0, h, delegate (int y)
            {
                byte* r0 = (byte*)yp + (long)ay0[y] * fwl, r1 = (byte*)yp + (long)ay1[y] * fwl;
                int wy = awy[y];
                byte* o = (byte*)dp + (long)y * ww * 2;
                byte* u0 = (byte*)up + (long)by0[y] * cw, u1 = (byte*)up + (long)by1[y] * cw;
                byte* v0 = (byte*)vp + (long)by0[y] * cw, v1 = (byte*)vp + (long)by1[y] * cw;
                int wyc = bwy[y];
                for (int x = 0; x < ww; x++)
                {
                    int a = ax0[x], b = ax1[x], wx = awx[x];
                    int t = (r0[a] * (256 - wx) + r0[b] * wx), u = (r1[a] * (256 - wx) + r1[b] * wx);
                    o[x * 2] = (byte)((t * (256 - wy) + u * wy + 32768) >> 16);
                }
                for (int c = 0; c < ww / 2; c++)
                {
                    byte uu = 128, vv = 128;
                    if (!mono)
                    {
                        int a = bx0[c], b = bx1[c], wx = bwx[c];
                        int t = u0[a] * (256 - wx) + u0[b] * wx, q = u1[a] * (256 - wx) + u1[b] * wx;
                        uu = (byte)((t * (256 - wyc) + q * wyc + 32768) >> 16);
                        t = v0[a] * (256 - wx) + v0[b] * wx; q = v1[a] * (256 - wx) + v1[b] * wx;
                        vv = (byte)((t * (256 - wyc) + q * wyc + 32768) >> 16);
                    }
                    o[c * 4 + 1] = uu; o[c * 4 + 3] = vv;
                }
            });
        }

        // Crudo YUY2 de otro tamaño: a planar 4:2:2 y por el camino de arriba
        byte[] planar;
        void FromYuy2Scaled(byte* sp, byte* dp)
        {
            int cw = fw / 2;
            long n = (long)fw * fh + 2L * cw * fh;
            if (planar == null || planar.Length != n) planar = new byte[n];
            fixed (byte* pl = planar)
            {
                IntPtr s0 = (IntPtr)sp, p0 = (IntPtr)pl;
                int fwl = fw, fhl = fh;
                System.Threading.Tasks.Parallel.For(0, fh, delegate (int y)
                {
                    byte* r = (byte*)s0 + (long)y * fwl * 2;
                    byte* Y = (byte*)p0 + (long)y * fwl, U = (byte*)p0 + (long)fwl * fhl + (long)y * cw, V = (byte*)p0 + (long)fwl * fhl + (long)cw * fhl + (long)y * cw;
                    for (int x = 0; x < cw; x++) { Y[2 * x] = r[4 * x]; U[x] = r[4 * x + 1]; Y[2 * x + 1] = r[4 * x + 2]; V[x] = r[4 * x + 3]; }
                });
                PlanarScaled(pl, pl + (long)fw * fh, pl + (long)fw * fh + (long)cw * fh, dp);
            }
        }

        // ---- El reloj: la imagen n de la señal y la del fichero que le toca
        double SigFps { get { return sigNum / (double)sigDen; } }
        long t0Qpc;          // sin reloj comun: la hora de la imagen 0
        long nBase;          // sin reloj comun: la primera imagen

        long SignalAt(long qpc) { return (long)Math.Floor((qpc - t0Qpc) / (double)System.Diagnostics.Stopwatch.Frequency * SigFps); }
        long QpcOf(long n) { return t0Qpc + (long)Math.Round(n / SigFps * System.Diagnostics.Stopwatch.Frequency); }

        long FileFrameOf(long n)
        {
            long g = n - nBase;
            if (g < 0) g = 0;
            // imagen del juego: g * (ritmo del fichero / ritmo de la señal)
            long gf = (long)Math.Floor(g * ((double)fileNum * sigDen) / ((double)fileDen * sigNum) + 1e-9);
            if (!s.FilePingPong || frames < 2) return gf % frames;
            long period = 2L * frames - 2;
            long k = gf % period;
            return k < frames ? k : period - k;
        }

        public void Start()
        {
            Open();
            if (s.FileClock) { t0Qpc = 0; nBase = 0; }
            else { t0Qpc = Clock.Now; nBase = 0; }
            nNext = SignalAt(Clock.Now) + 1;
            if (!s.FileClock) nBase = nNext;
            // La primera ya preparada (que salga enseguida)
            long f0 = FileFrameOf(nNext);
            Decode(f0, ring[(int)(f0 % Ring)]);
            ringFrame[(int)(f0 % Ring)] = f0;
            Log.Write("Fuente de prueba: " + desc + " (empieza en la imagen " + f0 + " del fichero)");
            prep = new Thread(PrepLoop); prep.IsBackground = true; prep.Name = "fuente archivo (preparar)"; prep.Priority = ThreadPriority.AboveNormal; prep.Start();
            deliver = new Thread(DeliverLoop); deliver.IsBackground = true; deliver.Name = "fuente archivo"; deliver.Priority = ThreadPriority.Highest; deliver.Start();
        }

        // Prepara por delante las imagenes del fichero que tocan a las proximas de la señal (las distintas, hasta llenar el anillo menos la que
        // se esta entregando)
        void PrepLoop()
        {
            try
            {
                while (!stop)
                {
                    long n = nNext;
                    bool did = false;
                    long seen = -1; int distinct = 0;
                    for (long k = n; distinct < Ring - 1 && k < n + 64; k++)
                    {
                        long f = FileFrameOf(k);
                        if (f == seen) continue;
                        seen = f; distinct++;
                        int slot = (int)(f % Ring);
                        bool have;
                        lock (ringLock) have = ringFrame[slot] == f;
                        if (have) continue;
                        // (no pisar la que se entrega ahora)
                        if (f != FileFrameOf(n) && ringFrame[slot] == FileFrameOf(n)) break;
                        lock (ringLock) ringFrame[slot] = -1;
                        Decode(f, ring[slot]);
                        lock (ringLock) ringFrame[slot] = f;
                        did = true;
                        break;
                    }
                    if (!did) prepWake.WaitOne(4);
                }
            }
            catch (Exception ex) { if (!stop) Log.Error("Fuente archivo (preparar)", ex); }
        }

        void DeliverLoop()
        {
            timeBeginPeriod(1);
            try
            {
                long lastSlotFrame = -1; int lastSlot = -1;
                while (!stop)
                {
                    long n = nNext;
                    long due = QpcOf(n);
                    if (s.FileJitterMs > 0) due += (long)((jitter.NextDouble() * 2 - 1) * s.FileJitterMs / Clock.TickMs);
                    while (!stop)
                    {
                        double left = Clock.Ms(Clock.Now, due);
                        if (left <= 0) break;
                        if (left > 2) Thread.Sleep(1); else Thread.SpinWait(200);
                    }
                    if (stop) break;
                    // (muy atrasada -el PC parado, un paron-: se salta a la de ahora, como una capturadora que pierde imagenes)
                    long nowN = SignalAt(Clock.Now);
                    if (nowN > n + 3) { n = nowN; }
                    long f = FileFrameOf(n);
                    int slot = (int)(f % Ring);
                    bool ready;
                    lock (ringLock) ready = ringFrame[slot] == f;
                    if (!ready)
                    {
                        // un poco mas (hasta un cuarto de imagen) y si no, la anterior otra vez
                        long until = Clock.Now + (long)(250.0 / SigFps / Clock.TickMs);
                        while (!stop && Clock.Now < until) { lock (ringLock) ready = ringFrame[slot] == f; if (ready) break; Thread.SpinWait(100); }
                        if (!ready) { late++; if (lastSlot >= 0) { slot = lastSlot; f = lastSlotFrame; ready = true; } }
                    }
                    if (ready)
                    {
                        FrameHandler fh2 = Frame;
                        long at = Clock.Now;
                        if (fh2 != null) fh2(ringPin[slot].AddrOfPinnedObject(), w * 2, w, h, Shaders.FmtYuy2, at, n);
                        lastSlot = slot; lastSlotFrame = f;
                        delivered++;
                    }
                    nNext = n + 1;
                    prepWake.Set();
                    if (late != lateLogged && Clock.SinceMs(lastLogAt) > 10000)
                    {
                        lastLogAt = Clock.Now;
                        Log.Write("Fuente de prueba (archivo): " + (late - lateLogged) + " imagenes sin preparar a tiempo (se repitio la anterior); " + delivered + " entregadas");
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
            if (prep != null) prep.Join(1000);
            try { if (fs != null) fs.Dispose(); } catch { }
            // (si el hilo de entrega sigue dentro de la llamada al motor, las imagenes se quedan fijadas: se van con el proceso)
            if (deliver == null || !deliver.IsAlive)
                for (int i = 0; i < Ring; i++) if (ringPin[i].IsAllocated) ringPin[i].Free();
        }
    }
}
