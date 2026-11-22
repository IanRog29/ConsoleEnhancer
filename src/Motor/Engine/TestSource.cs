using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConsoleEnhancer.Motor
{
    // Lo que entrega cada imagen al motor: datos validos solo durante la llamada; filas de arriba abajo
    delegate void FrameHandler(IntPtr data, int pitch, int width, int height, int fmt, long qpcArrival, long seq);

    interface IFrameSource : IDisposable
    {
        event FrameHandler Frame;
        string Name { get; }
        void Start();
    }

    // Fuente de prueba sin consola: una imagen (o barras) que se desplaza como un paneo, al ritmo de la captura
    // (59,94/s). gameFps=30: cada imagen se repite 2 veces (un juego a 30 en una señal de 60), como la consola
    class TestSource : IFrameSource
    {
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

        public event FrameHandler Frame;
        readonly int w, h, num, den, gameFps;
        readonly string image;
        public bool Scroll = true;
        public static int GiroScrollX, GiroScrollY;   // 20-11 (GIRO) test_giro=px,py: paneo de px por imagen (prueba de giros rapidos; 0 = el de siempre)
        public bool Hud = false;
        public string Overlay = "";   // test_capa=<png con alfa>: encima de la imagen, quieta (un HUD sobre el fondo que se mueve)
        public int Noise = 0;          // 16-11 noche: test_ruido=N, +-N niveles nuevos en cada imagen (el ruido de la capturadora)
        uint rng = 2463534242u;
        public string ImageB = "";    // 16-11 (calidad, E2) test_b=<png>: una imagen si y otra no es esta (dos copias seguidas de la capturadora: su ruido real)
        // 16-11 (tirones) test_cadencia=: cuantos refrescos de la señal dura cada imagen del juego (las cadencias de cadencia.exe de G1):
        // 30 (2), caida (2 y a veces 3), par (2 y a veces 3+1 o 1+3), mezcla (1/2/3), alterno (1,3), 20 (3), 45 (1 o 2), 60 (1), o una
        // lista que se repite ("2,2,3,1"). Cada imagen del juego avanza el paneo lo mismo, como un juego que pierde el ritmo
        public string Holds = "";
        int[] holdList; int holdPos; readonly Random holdRnd = new Random(20261116); int holdPending;
        int NextHold()
        {
            if (holdPending > 0) { int p = holdPending; holdPending = 0; return p; }
            double x = holdRnd.NextDouble();
            switch (Holds)
            {
                case "30": return 2;
                case "caida": return x < 0.06 ? 3 : 2;
                case "par": if (x < 0.06) { holdPending = 1; return 3; } if (x < 0.12) { holdPending = 3; return 1; } return 2;
                case "mezcla": return x < 0.15 ? 1 : x < 0.85 ? 2 : 3;
                case "alterno": holdPos ^= 1; return holdPos == 1 ? 1 : 3;
                case "20": return 3;
                case "45": return x < 0.6 ? 1 : 2;
                case "60": return 1;
            }
            if (holdList == null)
            {
                System.Collections.Generic.List<int> l = new System.Collections.Generic.List<int>();
                foreach (string t in Holds.Split(',')) { int v; if (int.TryParse(t.Trim(), out v) && v > 0) l.Add(v); }
                holdList = l.Count > 0 ? l.ToArray() : new[] { 1 };
            }
            int r = holdList[holdPos % holdList.Length];
            holdPos++;
            return r;
        }
        byte[] srcB;                  // imagen B BGR24 (como src)
        byte[] ovl;                   // BGRA premultiplicado no: alfa normal, w*h*4      // test_hud=1: las zonas del HUD (arriba a la izquierda, abajo a la derecha) quietas sobre el fondo que se mueve
        byte[] src;           // imagen base BGR24 (w x h), de arriba abajo
        IntPtr frame;         // imagen desplazada que se entrega
        int pitch;
        volatile bool stop;
        Thread thread;

        // 17-11 (REJAS) test=paralaje: reja oscura delante de un cielo con montañas, cada capa a su velocidad (Parallax.cs), compuesta
        // a 4K y reducida por area a la captura. test_par = velocidad del cielo / la de la reja (de serie 2,2), test_vel = escala
        public double ParRatio = 2.2, ParSpeed = 1.0;
        Parallax par;
        bool IsParallax { get { return image == "paralaje"; } }
        // 17-11 12:50 (REJAS, borrosidad) test=zoom: ZoomScene.cs con test_textura (de serie tests/zoom_oot.png) y test_vel
        public string ZoomTexture = ""; public int ZoomKind;
        ZoomScene zoom;
        bool IsZoom { get { return image == "zoom"; } }
        // 22-11 (1.2.2, CALIDAD) test=finas / test=cabeza: cosas finas (barrotes, cables, malla, lamas, hierba) o un personaje quieto con el
        // fondo corriendo detras (Finas.cs), con test_par, test_vel y test_dir=h|v|hv
        public string Dir = "h";
        Finas fin;
        bool IsFinas { get { return Finas.KindOf(image) >= 0; } }

        public string Name { get { return "prueba (" + (image.Length > 0 ? image : "barras") + (IsParallax ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " cielo x{0:0.00} velocidad x{1:0.00} ruido {2}", ParRatio, ParSpeed, Noise) : "") + (Holds.Length > 0 ? ", cadencia " + Holds : ", juego a " + gameFps) + ")"; } }

        public TestSource(string image, int w, int h, int num, int den, int gameFps)
        {
            this.image = image == "barras" ? "" : image;
            this.w = w; this.h = h; this.num = num; this.den = den; this.gameFps = gameFps;
        }

        void Build()
        {
            pitch = (w * 3 + 3) & ~3;
            if (IsZoom)
            {
                long t0 = Clock.Now;
                zoom = new ZoomScene(ZoomTexture, ParSpeed, ZoomKind);
                frame = Marshal.AllocHGlobal(pitch * h);
                Log.Write("Fuente de prueba: zoom preparada (" + Clock.SinceMs(t0).ToString("0") + " ms, " + ZoomTexture + ")");
                return;
            }
            if (IsFinas)
            {
                long t0 = Clock.Now;
                fin = new Finas(Finas.KindOf(image), ParRatio, ParSpeed, Dir == "v" ? 2 : Dir == "hv" ? 3 : 1);
                frame = Marshal.AllocHGlobal(pitch * h);
                Log.Write("Fuente de prueba: " + image + " preparada (" + Clock.SinceMs(t0).ToString("0") + " ms)");
                return;
            }
            if (IsParallax)
            {
                long t0 = Clock.Now;
                par = new Parallax(ParRatio, ParSpeed);
                frame = Marshal.AllocHGlobal(pitch * h);
                Log.Write("Fuente de prueba: paralaje preparada (" + Clock.SinceMs(t0).ToString("0") + " ms)");
                return;
            }
            src = new byte[pitch * h];
            using (Bitmap b = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    if (image.Length > 0 && System.IO.File.Exists(image))
                        using (Image im = Image.FromFile(image)) g.DrawImage(im, 0, 0, w, h);
                    else
                    {
                        // Barras de color con rejilla fina (para ver el detalle)
                        Color[] bars = { Color.White, Color.Yellow, Color.Cyan, Color.Lime, Color.Magenta, Color.Red, Color.Blue, Color.Black };
                        for (int i = 0; i < bars.Length; i++)
                            using (SolidBrush br = new SolidBrush(bars[i])) g.FillRectangle(br, i * w / bars.Length, 0, w / bars.Length + 1, h);
                        using (Pen p = new Pen(Color.FromArgb(128, 128, 128)))
                            for (int x = 0; x < w; x += 16) g.DrawLine(p, x, 0, x, h);
                    }
                }
                BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try { for (int y = 0; y < h; y++) Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), src, y * pitch, w * 3); }
                finally { b.UnlockBits(d); }
            }
            frame = Marshal.AllocHGlobal(pitch * h);
            if (Yuy2) BuildYuy2();   // 20-11 (LATENCIA7): test_yuy2
            LoadOverlay();
            if (ImageB.Length > 0 && System.IO.File.Exists(ImageB))
            {
                // 16-11 (calidad): la segunda imagen del par, igual que la primera
                using (Bitmap b = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(b)) using (Image im = Image.FromFile(ImageB)) g.DrawImage(im, 0, 0, w, h);
                    srcB = new byte[pitch * h];
                    BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    try { for (int y = 0; y < h; y++) Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), srcB, y * pitch, w * 3); }
                    finally { b.UnlockBits(d); }
                }
            }
        }

        void LoadOverlay()
        {
            if (Overlay.Length == 0 || !System.IO.File.Exists(Overlay)) return;
            using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b)) using (Image im = Image.FromFile(Overlay)) g.DrawImage(im, 0, 0, w, h);
                BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                ovl = new byte[w * h * 4];
                try { for (int y = 0; y < h; y++) Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), ovl, y * w * 4, w * 4); }
                finally { b.UnlockBits(d); }
            }
        }

        public void Start()
        {
            Build();
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "fuente de prueba";
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Start();
        }

        // 20-11 (LATENCIA7) test_yuy2=1 (solo pruebas): la imagen de prueba en YUY2 (BT.709 de rango limitado, lo que lee la descompresion),
        // como la PS5 a 4K por la GC573 (16,6 MB por imagen en vez de los 24,9 de BGR24): para medir la copia y la subida de verdad
        public bool Yuy2;
        byte[] srcY;
        void BuildYuy2()
        {
            int pY = w * 2;
            srcY = new byte[(long)pY * h];
            byte[] s0 = src, d0 = srcY;
            int sp = pitch;
            System.Threading.Tasks.Parallel.For(0, h, delegate (int y)
            {
                for (int x = 0; x + 1 < w; x += 2)
                {
                    int o = y * sp + x * 3, d = y * pY + x * 2;
                    float b0 = s0[o] / 255f, g0 = s0[o + 1] / 255f, r0 = s0[o + 2] / 255f, b1 = s0[o + 3] / 255f, g1 = s0[o + 4] / 255f, r1 = s0[o + 5] / 255f;
                    float y0 = 0.2126f * r0 + 0.7152f * g0 + 0.0722f * b0, y1 = 0.2126f * r1 + 0.7152f * g1 + 0.0722f * b1;
                    float ym = (y0 + y1) * 0.5f, u = ((b0 + b1) * 0.5f - ym) / 1.8556f, v = ((r0 + r1) * 0.5f - ym) / 1.5748f;
                    d0[d] = (byte)(16 + y0 * 219 + 0.5f); d0[d + 1] = (byte)Math.Max(16, Math.Min(240, 128 + u * 224 + 0.5f));
                    d0[d + 2] = (byte)(16 + y1 * 219 + 0.5f); d0[d + 3] = (byte)Math.Max(16, Math.Min(240, 128 + v * 224 + 0.5f));
                }
            });
        }
        unsafe void ComposeYuy2(int dx, int dy)
        {
            int pY = w * 2;
            fixed (byte* s = srcY)
            {
                byte* dst = (byte*)frame;
                for (int y = 0; y < h; y++)
                {
                    byte* srow = s + (long)((y + dy) % h) * pY, drow = dst + (long)y * pY;
                    int first = (w - dx) * 2;
                    Buffer.MemoryCopy(srow + dx * 2, drow, first, first);
                    Buffer.MemoryCopy(srow, drow + first, dx * 2, dx * 2);
                }
            }
        }

        public double ComposeMsMax;   // 17-11 (REJAS): lo que mas ha tardado en componer una imagen del paralaje
        // 17-11 (REJAS) test_paso=1: cada imagen se entrega cuando el motor ha cogido la anterior (sin perder ninguna aunque el motor vaya
        // lento: con la tele apagada Present tarda 80 ms); test_desde=N: la primera imagen es la N (el paralaje, sin esperar a llegar)
        public bool Lockstep; public long SeqStart;
        public static readonly AutoResetEvent Consumed = new AutoResetEvent(false);

        unsafe void Compose(long n)
        {
            if (zoom != null) { zoom.Compose(n, (byte*)frame, w, h, pitch, Noise); return; }
            if (fin != null) { fin.Compose(n, (byte*)frame, w, h, pitch, Noise); return; }   // 22-11 (1.2.2, CALIDAD)
            if (par != null)
            {
                long t0 = Clock.Now;
                par.Compose(n, (byte*)frame, w, h, pitch, Noise);
                double ms = Clock.SinceMs(t0);
                if (ms > ComposeMsMax) { ComposeMsMax = ms; if (ms > 12) Log.Write("Fuente de prueba: paralaje, imagen en " + ms.ToString("0.0") + " ms"); }
                return;
            }
            // Desplazamiento como el banco de mpv (scroll h=0.003, v=0.0015 por imagen), con vuelta al borde
            long stx = GiroScrollX > 0 ? GiroScrollX : (long)(w * 0.003), sty = GiroScrollX > 0 ? GiroScrollY : (long)Math.Max(1, h * 0.0015);   // 20-11 (GIRO): test_giro=px,py
            int dx = Scroll ? (int)((n * stx) % w) : 0, dy = Scroll ? (int)((n * sty) % h) : 0;
            if (srcY != null) { ComposeYuy2(dx & ~1, dy); return; }   // 20-11 (LATENCIA7): test_yuy2
            byte[] img = srcB != null && (n & 1) != 0 ? srcB : src;   // 16-11: con test_b, A y B alternas
            fixed (byte* s = img)
            {
                byte* dst = (byte*)frame;
                for (int y = 0; y < h; y++)
                {
                    byte* srow = s + ((y + dy) % h) * pitch;
                    byte* drow = dst + y * pitch;
                    int first = (w - dx) * 3;
                    Buffer.MemoryCopy(srow + dx * 3, drow, first, first);
                    Buffer.MemoryCopy(srow, drow + first, dx * 3, dx * 3);
                    if (ovl != null)
                        fixed (byte* o = ovl)
                        {
                            byte* orow2 = o + (long)y * w * 4;
                            for (int x = 0; x < w; x++)
                            {
                                int al = orow2[x * 4 + 3];
                                if (al == 0) continue;
                                byte* q = drow + x * 3;
                                for (int c = 0; c < 3; c++) q[c] = (byte)((orow2[x * 4 + c] * al + q[c] * (255 - al)) / 255);
                            }
                        }
                    if (Noise > 0)
                    {
                        int span = 2 * Noise + 1;
                        for (int x = 0; x < w * 3; x++)
                        {
                            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                            int v = drow[x] + (int)(rng % (uint)span) - Noise;
                            drow[x] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                        }
                    }
                    if (Hud)
                    {
                        // HUD quieto: el trozo de la imagen original sin desplazar
                        byte* orow = s + y * pitch;
                        if (y < h * 22 / 100) { int hw = w * 25 / 100 * 3; Buffer.MemoryCopy(orow, drow, hw, hw); }
                        if (y > h * 72 / 100) { int x0 = w * 78 / 100 * 3, hw = w * 3 - x0; Buffer.MemoryCopy(orow + x0, drow + x0, hw, hw); }
                    }
                }
            }
        }

        void Loop()
        {
            timeBeginPeriod(1);
            try
            {
                double periodMs = 1000.0 * den / num;
                long t0 = Clock.Now, seq = SeqStart, gameFrame = -1;
                int repeat = gameFps >= 55 ? 1 : (int)Math.Round(num / (double)den / Math.Max(1, gameFps));
                int holdLeft = 0;
                while (!stop)
                {
                    long due = t0 + (long)((seq - SeqStart) * periodMs / Clock.TickMs);
                    // 16-11 (E): la imagen se compone ANTES de esperar a su hora y se entrega puntual, como la capturadora. Antes se
                    // componia al llegar la hora (11 MB a 1440p, 2-6 ms segun la memoria) y la entrega salia irregular: a 1440p con la
                    // generacion, 158 presentaciones de mas de 10,5 ms en 40 s y dobles en la tele que la capturadora no da (0 y 0)
                    if (Holds.Length > 0)
                    {
                        // (16-11: cadencia del juego de prueba)
                        if (holdLeft <= 0) { gameFrame++; Compose(gameFrame); ProbeBandDraw(gameFrame); holdLeft = NextHold(); }
                        holdLeft--;
                    }
                    else
                    {
                        long gf = seq / repeat;
                        if (gf != gameFrame) { Compose(gf); gameFrame = gf; ProbeBandDraw(gf); }
                    }
                    while (!Lockstep)
                    {
                        double left = Clock.Ms(Clock.Now, due);
                        if (left <= 0) break;
                        if (left > 2) Thread.Sleep(1); else Thread.SpinWait(200);
                    }
                    FrameHandler f = Frame;
                    long at = Clock.Now;
                    if (f != null) f(frame, srcY != null ? w * 2 : pitch, w, h, srcY != null ? Shaders.FmtYuy2 : Shaders.FmtBgr24, at, seq);   // (20-11 LATENCIA7: test_yuy2)
                    ProbeLog(seq, gameFrame, at);
                    if (Lockstep) Consumed.WaitOne(2000);   // 17-11 (REJAS): test_paso=1, hasta que el motor la coge
                    seq++;
                }
            }
            catch (Exception ex) { Log.Error("Fuente de prueba", ex); }
            finally { timeEndPeriod(1); }
        }

        // 18-11 (LATENCIA) sonda=<fichero>: la banda de scripts\latencia.exe (el numero de la imagen del juego en codigo Gray, 16 bloques,
        // abajo del todo: una fila con los bits y debajo otra con el complemento) y la hora (QPC) a la que se entrega cada imagen, en
        // "n;g;qpc" (la fuente externa de "latencia pantalla ... externa=<fichero> destino=motor": de la entrega a verse en la tele)
        public string Probe = "";
        public int ProbeBand = 16;
        System.Text.StringBuilder probeBuf;
        bool probeStarted;
        int probeLines;

        unsafe void ProbeBandDraw(long g)
        {
            if (Probe.Length == 0 || frame == IntPtr.Zero) return;
            int code = (int)((g & 0xFFFF) ^ ((g & 0xFFFF) >> 1));
            int band = Math.Max(4, Math.Min(ProbeBand, h / 8));
            int yA = h - 2 * band, yB = h - band;
            byte* dst = (byte*)frame;
            for (int i = 0; i < 16; i++)
            {
                int x0 = i * w / 16, x1 = (i + 1) * w / 16;
                byte va = ((code >> i) & 1) != 0 ? (byte)255 : (byte)0, vb = (byte)(255 - va);
                for (int y = yA; y < h; y++)
                {
                    byte v = y < yB ? va : vb;
                    byte* p = dst + (long)y * pitch + x0 * 3;
                    for (int x = x0; x < x1; x++) { p[0] = v; p[1] = v; p[2] = v; p += 3; }
                }
            }
        }

        void ProbeLog(long seq, long g, long qpc)
        {
            if (Probe.Length == 0) return;
            if (probeBuf == null) probeBuf = new System.Text.StringBuilder(4096);
            probeBuf.Append(seq.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';').Append(g.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(';').Append(qpc.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
            if (++probeLines % 15 == 0) ProbeFlush();
        }

        // (el fichero abierto solo un momento: latencia.exe lo lee al acabar mientras el motor sigue)
        void ProbeFlush()
        {
            if (probeBuf == null || probeBuf.Length == 0 || Probe.Length == 0) return;
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (!probeStarted) { System.IO.File.WriteAllText(Probe, "n;g;qpc\r\n" + probeBuf.ToString()); probeStarted = true; }
                    else System.IO.File.AppendAllText(Probe, probeBuf.ToString());
                    probeBuf.Length = 0;
                    return;
                }
                catch (System.IO.IOException) { Thread.Sleep(2); }
                catch (Exception ex) { Log.Error("Sonda", ex); Probe = ""; return; }
            }
        }

        public void Dispose()
        {
            stop = true;
            if (thread != null) thread.Join(500);
            if (frame != IntPtr.Zero) { Marshal.FreeHGlobal(frame); frame = IntPtr.Zero; }
            try { ProbeFlush(); } catch { }
        }
    }
}
