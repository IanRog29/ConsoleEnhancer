// TelonVivo.cs - 15-11-2026 (telon_vivo): el telon de los cambios que solo reabren Magpie, con el juego en directo de fondo.
// Al aplicar un ajuste con el mando (nivel, pasadas, generacion, modo, R3...) Magpie se cierra y se vuelve a abrir (7-10 s, casi
// todo NVIDIA creando sus modelos). mpv sigue mientras con la captura en marcha: en vez de la ultima imagen congelada y atenuada,
// el telon es una miniatura de DWM de la ventana de mpv a toda la pantalla (DwmRegisterThumbnail: DWM la compone en cada refresco,
// casi gratis), sin atenuar, con el aviso arriba como los de siempre (una pastilla en una ventana aparte, por encima) y una linea
// de progreso fina. Se ve y se juega (mas suave: sin DLSS 5 ni el escalado de Magpie) hasta la primera imagen de Magpie, y luego
// el fundido de salida de siempre.
// Al momento: el telon sale ya opaco (sin fundido de entrada ni copiar antes la ultima imagen) y Magpie se mata enseguida
// (TrayApp.KillMagpieNow): el boton de captura del mando (Win derecha + Impr Pant) tumbaba a Magpie (c0000005) si seguia vivo
// ~0,5 s despues (15-11, 19:18 y 19:19). Si el en vivo no se puede, negro con la misma pastilla arriba.
// Lo compila build.bat con el resto (partes de CoverForm y TrayApp de Switch2Magpie.cs). C# 5
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ConsoleEnhancer
{
    // ---- El telon en vivo (la parte de CoverForm: live != Zero en su constructor) ----
    // Medido el 15-11 (prototipo y --ui-preview=telon-vivo con un mpv de prueba): la miniatura sale tambien en una ventana "layered"
    // (WinForms Opacity) y con su opacidad; sigue en directo con la ventana de mpv tapada; si mpv se cierra desaparece (y
    // DwmUpdateThumbnailProperties da E_INVALIDARG). OJO: al crearse la ventana (OnHandleCreated) su ClientSize aun no es el de verdad
    // (salia ~3833x2087 en vez de 3840x2160: la imagen al 96 % con bandas negras): el destino se vuelve a poner al mostrarla
    // (RefreshLive) y cada vez que cambia el tamaño. Con LS (28-10) registrar una miniatura de mpv paraba su escalado; Magpie captura
    // con Graphics Capture: comprobarlo en la prueba completa. Magpie (disableTopmost=false) se pone ENCIMA del telon mientras vive
    // (no de la pastilla): el telon se ve en cuanto Magpie muere y deja de verse cuando el nuevo presenta
    partial class CoverForm
    {
        [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref ThumbProps props);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out WinRect r);
        [StructLayout(LayoutKind.Sequential)] struct WinRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct ThumbProps // DWM_THUMBNAIL_PROPERTIES
        {
            public int Flags;
            public WinRect Dest, Source;
            public byte Opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool Visible;
            [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
        }
        const int TNP_RECTDESTINATION = 0x1, TNP_RECTSOURCE = 0x2, TNP_OPACITY = 0x4, TNP_VISIBLE = 0x8, TNP_SOURCECLIENTAREAONLY = 0x10;
        // La imagen de la consola (y de la capturadora) es 16:9 siempre
        const int ContentW = 16, ContentH = 9;

        readonly IntPtr liveSrc;    // la ventana de mpv (Zero: el telon de siempre). La pone el constructor
        IntPtr thumb;               // la miniatura registrada (Zero: sin imagen en directo)
        Size liveSize;              // el area de cliente de mpv la ultima vez (se sigue: mpv puede cambiar de tamaño debajo)
        Size liveDst;               // el area de cliente del telon con la que se puso el destino
        bool liveDstShown;          // (y si entonces ya estaba a la vista)
        bool thumbHasSrc;           // la miniatura lleva rectangulo de origen (ventana de mpv estirada)
        int liveBad;                // pasos seguidos con la ventana de mpv escondida o minimizada
        LivePill pill;              // la pastilla de arriba
        bool pillTried;
        Stopwatch pillAge;          // desde que salio la pastilla (su fundido de entrada, como el de los avisos de mpv)
        public string TopPillText;  // el aviso de la pastilla de arriba (null: sin ella). En vivo, o sobre el negro del telon rapido
        public string LiveLost;     // por que no hay (o se perdio) la imagen en directo; null = bien
        public long LiveLostMs;     // a los cuantos ms del telon se perdio
        public Action<string> Note; // al log (TrayApp: Log.Write; la vista previa, a su fichero)
        public bool LiveOk { get { return thumb != IntPtr.Zero; } }

        // La miniatura se registra al crearse la ventana (antes de mostrarla: si falla, TrayApp la desecha sin que se vea y pone el
        // telon negro)
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (liveSrc != IntPtr.Zero && LiveLost == null) StartLive();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            DropLive();
            base.OnHandleDestroyed(e);
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            RefreshLive();
        }

        void StartLive()
        {
            IntPtr t;
            int hr = DwmRegisterThumbnail(Handle, liveSrc, out t);
            if (hr != 0 || t == IntPtr.Zero) { LiveLost = "no se pudo registrar la miniatura de mpv (0x" + hr.ToString("X8") + ")"; return; }
            thumb = t;
            thumbHasSrc = false;
            liveSize = Size.Empty;
            liveDst = Size.Empty;
            liveBad = 0;
            string why = UpdateThumb();
            if (why != null) { DropLive(); LiveLost = why; }
        }

        // El destino otra vez con el tamaño de ahora (tras mostrar la ventana: al crearla aun no es el de verdad)
        public void RefreshLive()
        {
            if (thumb == IntPtr.Zero) return;
            string why = UpdateThumb();
            if (why != null) LoseLive(why);
        }

        // Lo que se ve de mpv: la imagen 16:9 centrada en su area de cliente (con la ventana estirada mpv deja bandas negras: se
        // quedan fuera), a toda la ventana del telon sin deformarla. null = bien; si no, el motivo. Con la ventana de mpv a 16:9 (lo
        // normal) va su area de cliente entera, sin rectangulo de origen; para volver a ella tras uno, la miniatura se registra de
        // nuevo (sin la marca de rcSource DWM se quedaria con el de antes)
        string UpdateThumb()
        {
            WinRect cr;
            if (!GetClientRect(liveSrc, out cr) || cr.Right - cr.Left <= 0 || cr.Bottom - cr.Top <= 0) return "la ventana de mpv no tiene tamaño";
            Size s = new Size(cr.Right - cr.Left, cr.Bottom - cr.Top);
            Rectangle src = Fit(ContentW, ContentH, new Rectangle(0, 0, s.Width, s.Height));
            bool whole = Math.Abs(src.Width - s.Width) <= 2 && Math.Abs(src.Height - s.Height) <= 2; // (16:9: la ventana entera)
            if (whole && thumbHasSrc)
            {
                IntPtr t;
                int hn = DwmRegisterThumbnail(Handle, liveSrc, out t);
                if (hn != 0 || t == IntPtr.Zero) return "no se pudo registrar otra vez la miniatura de mpv (0x" + hn.ToString("X8") + ")";
                DwmUnregisterThumbnail(thumb);
                thumb = t;
                thumbHasSrc = false;
            }
            Size cs = ClientSize;
            if (cs.Width <= 0 || cs.Height <= 0) return null; // (aun sin tamaño: ya se pondra)
            Rectangle dst = Fit(src.Width, src.Height, new Rectangle(0, 0, cs.Width, cs.Height));
            ThumbProps p = new ThumbProps();
            p.Flags = TNP_RECTDESTINATION | TNP_OPACITY | TNP_VISIBLE | TNP_SOURCECLIENTAREAONLY | (whole ? 0 : TNP_RECTSOURCE);
            p.Dest = new WinRect { Left = dst.Left, Top = dst.Top, Right = dst.Right, Bottom = dst.Bottom };
            p.Source = new WinRect { Left = src.Left, Top = src.Top, Right = src.Right, Bottom = src.Bottom };
            p.Opacity = 255;
            p.Visible = true;
            p.ClientOnly = true;
            int hr = DwmUpdateThumbnailProperties(thumb, ref p);
            if (hr != 0) return "la miniatura de mpv no se deja colocar (0x" + hr.ToString("X8") + ")";
            thumbHasSrc = !whole;
            if (liveSize != Size.Empty && liveSize != s && Note != null)
                Note("Telon en vivo: mpv a " + s.Width + "x" + s.Height + " (antes " + liveSize.Width + "x" + liveSize.Height + "): se sigue (" +
                     (whole ? "la ventana entera" : src.Width + "x" + src.Height + " en " + src.X + "," + src.Y) + " -> " + dst.Width + "x" + dst.Height + ")");
            // (el cambio de tamaño del telon al mostrarlo, ~3834x2087 -> 3840x2160, es el de siempre: solo se apunta uno ya a la vista)
            if (liveDst != Size.Empty && liveDst != cs && liveDstShown && Note != null)
                Note("Telon en vivo: el telon a " + cs.Width + "x" + cs.Height + " (antes " + liveDst.Width + "x" + liveDst.Height + "): imagen a " + dst.Width + "x" + dst.Height);
            liveSize = s;
            liveDst = cs;
            liveDstShown = Visible;
            return null;
        }

        // El rectangulo mas grande con proporcion aw:ah centrado en box
        static Rectangle Fit(int aw, int ah, Rectangle box)
        {
            if (aw <= 0 || ah <= 0 || box.Width <= 0 || box.Height <= 0) return box;
            int w = box.Width, h = (int)Math.Round(box.Width * (double)ah / aw);
            if (h > box.Height) { h = box.Height; w = (int)Math.Round(box.Height * (double)aw / ah); }
            return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }

        // Cada paso del telon (Step, cada 33 ms): mpv sigue ahi y con el mismo tamaño; la pastilla (se crea en el primer paso: asi no
        // retrasa el cierre de Magpie), con su entrada de 140 ms y el fundido de salida del telon
        void LiveStep()
        {
            if (liveSrc == IntPtr.Zero && TopPillText == null) return;
            if (thumb != IntPtr.Zero) TrackLive();
            if (thumb != IntPtr.Zero && pacer == null && !pacerTried) StartPacer(); // (16-11: una composicion en cada refresco)
            if (pill == null && !pillTried && TopPillText != null && (liveSrc == IntPtr.Zero || thumb != IntPtr.Zero))
            {
                pillTried = true;
                try
                {
                    pill = new LivePill(Handle, Bounds, TopPillText, accent);
                    pillAge = Stopwatch.StartNew();
                }
                catch (Exception ex)
                {
                    pill = null;
                    if (Note != null) Note("Telon: sin la pastilla de arriba (" + ex.GetType().Name + ": " + ex.Message + ")");
                }
            }
            if (pill != null)
            {
                double k = top > 0 ? Math.Max(0, Math.Min(1, Opacity / top)) : 1;   // (el fundido de salida del telon)
                double e = pillAge != null ? Math.Min(1, pillAge.Elapsed.TotalSeconds / 0.14) : 1;
                pill.Update(Math.Min(k, e), fading ? 0 : (1 - e) * (1 - e), (DateTime.Now - start).TotalSeconds);
            }
        }

        void TrackLive()
        {
            if (!IsWindow(liveSrc)) { LoseLive("la ventana de mpv se ha cerrado"); return; }
            WinRect cr;
            if (!IsWindowVisible(liveSrc) || IsIconic(liveSrc) || !GetClientRect(liveSrc, out cr) || cr.Right - cr.Left <= 0 || cr.Bottom - cr.Top <= 0)
            {
                if (++liveBad >= 10) LoseLive("la ventana de mpv esta escondida o minimizada"); // (~0,3 s seguidos)
                return;
            }
            liveBad = 0;
            if (cr.Right - cr.Left != liveSize.Width || cr.Bottom - cr.Top != liveSize.Height || ClientSize != liveDst)
            {
                string why = UpdateThumb();
                if (why != null) LoseLive(why);
            }
        }

        // Sin la imagen de mpv (cerrado, relanzado, escondido): el telon de siempre sin cerrarse (oscuro con la pastilla en el centro y
        // la linea; sin texto, negro). No vuelve a vivo: un mpv nuevo tarda en dar la imagen buena
        void LoseLive(string why)
        {
            DropLive();
            TopPillText = null; // (el oscuro lleva la suya en el centro)
            LiveLost = why;
            LiveLostMs = (long)(DateTime.Now - start).TotalMilliseconds;
            if (bg != null) { bg.Dispose(); bg = null; } // (se rehace: el oscuro)
            Invalidate();
            if (Note != null) Note("Telon en vivo: " + why + " a los " + LiveLostMs + " ms: telon oscuro");
        }

        void DropLive()
        {
            StopPacer();
            if (thumb != IntPtr.Zero) { DwmUnregisterThumbnail(thumb); thumb = IntPtr.Zero; }
            thumbHasSrc = false;
            if (pill != null) { pill.Dispose(); pill = null; }
        }

        // ---- 16-11: el ritmo de la imagen en vivo ----
        // Medido sin consola (mpv con una fuente que se mueve + este telon, tiempos de composicion de DWM y presentaciones de mpv): mpv debajo
        // va en "independent flip" y DWM no compone con cada imagen suya sino cuando le toca por otra cosa (la pastilla, el telon): en el
        // modo 30 la imagen nueva salia a los 33 o a los 42 ms (5 refrescos) y otras antes (p95 41,7, max 50 ms: tirones que la hacen
        // parecer lenta). Con una composicion en cada refresco (DwmFlush y un cambio de 1/255 en una ventana de 1x1 encima del telon) cada
        // imagen sale en el refresco siguiente: 30/s exactas a 33,3 ms (p95 = max = 33,3 ms) y 60/s en el modo 60
        [DllImport("dwmapi.dll")] static extern int DwmFlush();
        PacerForm pacer;
        bool pacerTried;
        volatile bool pacing;

        sealed class PacerForm : Form
        {
            public PacerForm(Rectangle at)
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(at.X, at.Y, 1, 1);
                BackColor = Color.Black;
                TopMost = true;
                Opacity = 1 / 255.0;
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000 | 0x80 | 0x20; // NOACTIVATE | TOOLWINDOW | TRANSPARENT (al raton)
                    return cp;
                }
            }
        }

        void StartPacer()
        {
            pacerTried = true;
            try
            {
                PacerForm f = new PacerForm(Bounds);
                f.Show(this);
                pacer = f;
                pacing = true;
                Thread t = new Thread(delegate ()
                {
                    bool flip = false;
                    Stopwatch quick = new Stopwatch();
                    while (pacing)
                    {
                        quick.Restart();
                        if (DwmFlush() != 0 || quick.ElapsedMilliseconds < 2) Thread.Sleep(4); // (sin composicion que esperar: sin dar vueltas en vacio)
                        flip = !flip;
                        double a = flip ? 2 / 255.0 : 1 / 255.0;
                        try { f.BeginInvoke((MethodInvoker)delegate { if (!f.IsDisposed) f.Opacity = a; }); }
                        catch { break; } // (ya cerrada)
                    }
                });
                t.IsBackground = true;
                t.Name = "Telon en vivo (ritmo)";
                t.Start();
            }
            catch (Exception ex)
            {
                pacing = false;
                pacer = null;
                if (Note != null) Note("Telon en vivo: sin el ritmo de cada refresco (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
        }

        void StopPacer()
        {
            pacing = false;
            PacerForm f = pacer;
            pacer = null;
            if (f != null) try { f.Close(); f.Dispose(); } catch { }
        }

        // OnPaint: en vivo, negro (la miniatura va encima; se ve solo si la imagen de mpv no llenase la pantalla); el negro liso (sin
        // texto ni imagen), igual pero sin crear antes una imagen de toda la pantalla (el telon rapido se pinta al momento). true = pintado
        bool LivePaint(PaintEventArgs e)
        {
            bool live = liveSrc != IntPtr.Zero && LiveLost == null;
            bool black = liveSrc == IntPtr.Zero && title == null && frozen == null;
            if (!live && !black) return false;
            e.Graphics.FillRectangle(Brushes.Black, e.ClipRectangle);
            return true;
        }
    }

    // ---- La pastilla de arriba del telon en vivo (y del negro rapido) ----
    // La miniatura de DWM se pinta encima de todo lo que pinta la ventana del telon, asi que la pastilla va en una ventana pequeña
    // aparte: de la del telon (siempre encima de ella y se cierra con ella), sin activarse, transparente al raton y con transparencia
    // por pixel (UpdateLayeredWindow: bordes y letra nitidos a 4K). Como los avisos de mpv (switch2_power.lua, notice_show): arriba en
    // el centro, a su tamaño, entra en 140 ms subiendo un poco; sin atenuar el juego. Debajo, la linea de progreso fina (7-10 s)
    sealed class LivePill : NativeWindow, IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Pt pptDst, ref Sz psize, IntPtr hdcSrc, ref Pt pptSrc,
                                               int crKey, ref Blend pblend, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern bool GdiFlush();
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [StructLayout(LayoutKind.Sequential)] struct Pt { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct Sz { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { public byte Op, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)]
        struct BitmapHeader // BITMAPINFOHEADER (con BI_RGB a 32 bits no hace falta tabla de colores)
        {
            public int Size, Width, Height;
            public short Planes, BitCount;
            public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
        }

        readonly int w, h;          // la ventana (la pastilla con su sombra y la linea debajo)
        readonly Point at;          // su sitio en la pantalla (sin el deslizamiento de la entrada)
        readonly float u;           // escala (1 = 1080p)
        readonly RectangleF barR;   // la linea de progreso
        readonly Color accent;
        Bitmap pillImg;             // la pastilla ya pintada (no cambia)
        IntPtr memDc, dib, oldBmp;
        Bitmap surface;             // los bits de la DIB, para pintar con GDI+ (PArgb: lo que espera UpdateLayeredWindow)
        bool shown;

        // owner: la ventana del telon; area: la suya (la pantalla)
        public LivePill(IntPtr owner, Rectangle area, string text, Color accent)
        {
            this.accent = accent;
            PrismaUi.Pill p;
            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(probe))
            {
                float u0 = area.Height / 1080f;
                p = PrismaUi.MeasurePill(g, text, u0);
                if (p.W > area.Width * 0.92f) p = PrismaUi.MeasurePill(g, text, u0 * area.Width * 0.92f / p.W); // (texto muy largo)
            }
            u = p.U;
            int pad = (int)Math.Ceiling(22 * u); // (la sombra sale 9u por los lados y 18u por debajo)
            int pw = (int)Math.Ceiling(p.W), ph = (int)Math.Ceiling(p.H);
            float gap = 14 * u, bh = Math.Max(2f, (float)Math.Round(2.5f * u));
            float bw = Math.Max(120 * u, Math.Min(280 * u, p.W * 0.42f));
            w = pw + 2 * pad;
            h = pad + ph + (int)Math.Ceiling(gap + bh) + pad;
            barR = new RectangleF((float)Math.Round((w - bw) / 2), (float)Math.Round(pad + ph + gap), (float)Math.Round(bw), bh);
            // Donde va el aviso de mpv: su borde de arriba a 0,035 del alto + 12,6 (medidas de 1080p), centrado
            at = new Point(area.X + (area.Width - w) / 2, area.Y + (int)Math.Round((0.035 * 1080 + 18 * 0.7) * area.Height / 1080.0) - pad);
            pillImg = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(pillImg))
            {
                g.Clear(Color.Transparent);
                PrismaUi.DrawPill(g, p, new RectangleF(pad, pad, p.W, p.H), accent, 1f);
            }
            // La superficie: una DIB de 32 bits de arriba abajo (la lee UpdateLayeredWindow) y GDI+ pintando en sus bits
            BitmapHeader bi = new BitmapHeader();
            bi.Size = Marshal.SizeOf(typeof(BitmapHeader));
            bi.Width = w;
            bi.Height = -h;
            bi.Planes = 1;
            bi.BitCount = 32;
            IntPtr bits;
            IntPtr screen = GetDC(IntPtr.Zero);
            try
            {
                memDc = CreateCompatibleDC(screen);
                dib = CreateDIBSection(screen, ref bi, 0, out bits, IntPtr.Zero, 0);
            }
            finally { ReleaseDC(IntPtr.Zero, screen); }
            if (memDc == IntPtr.Zero || dib == IntPtr.Zero || bits == IntPtr.Zero) { Dispose(); throw new InvalidOperationException("sin superficie para la pastilla"); }
            oldBmp = SelectObject(memDc, dib);
            surface = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
            CreateParams cp = new CreateParams();
            cp.Caption = PrismaUi.AppName;
            cp.X = at.X;
            cp.Y = at.Y;
            cp.Width = w;
            cp.Height = h;
            cp.Style = unchecked((int)0x80000000);                  // WS_POPUP
            cp.ExStyle = 0x80000 | 0x20 | 0x08000000 | 0x80 | 0x8;   // LAYERED | TRANSPARENT | NOACTIVATE | TOOLWINDOW | TOPMOST
            cp.Parent = owner;                                      // (de una WS_POPUP: su duena)
            CreateHandle(cp);
        }

        // alpha 0..1 (entrada de la pastilla y fundido del telon), slide 0..1 (lo que le queda por subir al entrar), t (s) para la linea
        public void Update(double alpha, double slide, double t)
        {
            if (Handle == IntPtr.Zero || surface == null) return;
            using (Graphics g = Graphics.FromImage(surface))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(pillImg, 0, 0);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // (bajo la linea, una sombra tenue: se ve tambien sobre una imagen clara)
                using (GraphicsPath sp = PrismaUi.RoundRect(RectangleF.Inflate(barR, 1.5f * u, 1.5f * u), barR.Height / 2 + 1.5f * u))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                    g.FillPath(sb, sp);
                PrismaUi.DrawProgressLine(g, barR, t, accent, 1f);
            }
            GdiFlush();
            Pt dst = new Pt { X = at.X, Y = at.Y + (int)Math.Round(slide * 14 * u) };
            Sz size = new Sz { W = w, H = h };
            Pt src = new Pt();
            Blend b = new Blend { Op = 0, Flags = 0, Alpha = (byte)Math.Max(0, Math.Min(255, Math.Round(alpha * 255))), Format = 1 }; // AC_SRC_OVER, AC_SRC_ALPHA
            IntPtr screen = GetDC(IntPtr.Zero);
            try { UpdateLayeredWindow(Handle, screen, ref dst, ref size, memDc, ref src, 0, ref b, 2); } // ULW_ALPHA
            finally { ReleaseDC(IntPtr.Zero, screen); }
            if (!shown) { ShowWindow(Handle, 4); shown = true; } // SW_SHOWNOACTIVATE
            // Encima del telon (que se pone encima de la ventana de escalado de Magpie en cada paso)
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); // HWND_TOPMOST, NOSIZE | NOMOVE | NOACTIVATE
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // WM_MOUSEACTIVATE: MA_NOACTIVATE
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
            if (surface != null) { surface.Dispose(); surface = null; }
            if (memDc != IntPtr.Zero)
            {
                if (oldBmp != IntPtr.Zero) SelectObject(memDc, oldBmp);
                DeleteDC(memDc);
                memDc = IntPtr.Zero;
            }
            if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
            if (pillImg != null) { pillImg.Dispose(); pillImg = null; }
        }
    }

    // ---- Cuando se usa (la parte de TrayApp) ----
    partial class TrayApp
    {
        volatile bool curtainLive;                  // el telon puesto es el en vivo
        volatile string curtainKind = "en negro";   // lo que se ve en el telon puesto (para el log al quitarlo)

        // El telon de las recargas de Magpie con el mando (RestartLs y el paso de Magpie al motor propio), AL MOMENTO: ya opaco, sin
        // fundido de entrada ni copiar antes la ultima imagen, sin activarse (el primer plano, despues de matar Magpie: CurtainToFront).
        // live: el en vivo si se puede (solo se reabre Magpie y mpv sigue con la imagen de la consola); si no, negro. Con texto (de un
        // boton del mando), la pastilla arriba como los avisos. Lo que tarde, con la pastilla aparte en el primer paso (+33 ms)
        void ShowFastCurtain(string subtitle, string title, bool live)
        {
            if (live && ShowLiveCurtain(subtitle, title)) return;
            string text = title == null ? null : string.IsNullOrEmpty(subtitle) ? title : title + " · " + subtitle;
            try
            {
                ui.Invoke((MethodInvoker)delegate
                {
                    DropClosedCurtain("al poner el telon al momento"); // (uno ya cerrado no impide el nuevo)
                    if (splash != null || curtain != null) return;    // (ya hay uno: ese tapa)
                    CoverForm c = new CoverForm(null, null, NeonBlue, false, null, 0);
                    c.Note = Log.Write;
                    c.TopPillText = text;
                    c.Show();
                    c.ShowNow(); // (ya opaco)
                    curtain = c;
                    curtainSince = Stopwatch.StartNew();
                    curtainFadeMs = 0;
                    curtainFrozen = false;
                    curtainLive = false;
                    curtainKind = "en negro";
                });
            }
            catch (Exception ex) { Log.Error("Mostrando el telon al momento", ex); }
        }

        // Solo en los reinicios de Magpie solo (RestartLs), con mpv dando la imagen de la consola: ni al relanzar mpv, ni al cambiar de
        // motor, ni en el paso al HOME a 4K (imagen congelada exacta), ni con la pantalla No Signal. false = no se puede
        bool ShowLiveCurtain(string subtitle, string title)
        {
            // 16-11: telon_vivo 0 = no (de serie), 1 = siempre, 2 = solo en los modos de 60 (en el de 30 serian 30 imagenes sin generacion)
            if (cfg.CurtainLive == 0 || (cfg.CurtainLive == 2 && cfg.CapMode)) return false;
            string no = null;
            IntPtr h = IntPtr.Zero;
            try
            {
                Process p = mpv;
                if (p == null || p.HasExited) no = "sin mpv";
                else if (Config.EngineActive) no = "con el motor propio";
                else if (cfg.HomeNative) no = "HOME a 4K";
                else if (!signalOn || !consoleOn) no = "sin la imagen de la consola (No Signal)";
                else
                {
                    h = mpvWindow; // (la que apunta WatchLoop, si es de este mpv; si no, la del proceso)
                    uint wp = 0;
                    if (h != IntPtr.Zero) GetWindowThreadProcessId(h, out wp);
                    if (h == IntPtr.Zero || wp != (uint)p.Id) { p.Refresh(); h = p.MainWindowHandle; }
                    if (h == IntPtr.Zero) no = "sin la ventana de mpv";
                }
            }
            catch (Exception ex) { no = ex.GetType().Name + ": " + ex.Message; }
            if (no != null) { Log.Write("Telon en vivo: no (" + no + "): negro"); return false; }
            bool ok = false;
            string why = null;
            string text = title == null ? null : string.IsNullOrEmpty(subtitle) ? title : title + " · " + subtitle;
            try
            {
                ui.Invoke((MethodInvoker)delegate
                {
                    DropClosedCurtain("al poner el en vivo"); // (uno ya cerrado no impide el nuevo)
                    if (splash != null || curtain != null) { why = "ya habia un telon"; return; }
                    CoverForm c = new CoverForm(title, title != null ? subtitle : null, NeonBlue, false, null, 0, h);
                    c.Note = Log.Write;
                    c.TopPillText = text;
                    IntPtr ch = c.Handle; // (crea la ventana y registra la miniatura sin que se vea nada)
                    if (!c.LiveOk || ch == IntPtr.Zero) { why = c.LiveLost ?? "sin miniatura"; c.Dispose(); return; }
                    c.Show();
                    c.ShowNow();     // (ya opaco: Magpie se mata enseguida)
                    c.RefreshLive(); // (el destino con el tamaño ya de verdad)
                    curtain = c;
                    curtainSince = Stopwatch.StartNew();
                    curtainFadeMs = 0;
                    curtainFrozen = false;
                    curtainLive = true;
                    curtainKind = "en vivo";
                    ok = true;
                });
            }
            catch (Exception ex) { why = ex.GetType().Name + ": " + ex.Message; }
            if (!ok && why != "ya habia un telon") Log.Write("Telon en vivo: no se pudo (" + why + "): negro");
            return ok;
        }

        // (1.2.4 ronda 6, QUITAFG, fase 2: KillMagpieNow era de Magpie; fuera)

        // El primer plano al telon puesto, sin esperar. El rapido no lo toma al ponerse: se le da tras matar Magpie (el escalado
        // automatico del Magpie nuevo salta cuando WaitForScaling se lo devuelve a mpv)
        void CurtainToFront()
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                ui.Invoke((MethodInvoker)delegate
                {
                    if (curtain != null && !curtain.IsDisposed && curtain.IsHandleCreated) h = curtain.Handle;
                });
            }
            catch (Exception ex) { Log.Error("Primer plano al telon", ex); }
            if (h != IntPtr.Zero) ForceForeground(h);
        }

        // 21-11 (1.2.1, log del usuario: "Telon quitado: 8637 ms en negro" al entrar en un juego y "10521 ms en negro" al cambiar a 1440p
        // desde el menu de ajustes): sin puente (sin motor en espera listo), mientras Magpie prepara, el telon negro pasa a ser el en vivo en
        // cuanto mpv ya da la imagen del juego (tras el cambio sin cerrar mpv o tras reabrirlo): la consola a la vista, sin escalar, en vez de
        // negro. Con lo de telon_vivo (2: solo en los modos de 60 y 120) y lo que mira ShowLiveCurtain. El nuevo sale ya opaco encima y el
        // negro se cierra debajo (sin fundidos: no se ve el cambio). Magpie, al presentar, sale encima de el; luego se quita como siempre
        bool SwapToLiveCurtain(string why)
        {
            if (cfg.CurtainLive == 0 || (cfg.CurtainLive == 2 && cfg.CapMode)) return false;
            if (curtain == null || curtainLive || curtainFrozen || relevo != null) return false;
            IntPtr h = IntPtr.Zero;
            try
            {
                Process p = mpv;
                if (p == null || p.HasExited || Config.EngineActive || cfg.HomeNative || !signalOn || !consoleOn) return false;
                h = mpvWindow;
                uint wp = 0;
                if (h != IntPtr.Zero) GetWindowThreadProcessId(h, out wp);
                if (h == IntPtr.Zero || wp != (uint)p.Id) { p.Refresh(); h = p.MainWindowHandle; }
                if (h == IntPtr.Zero) return false;
            }
            catch { return false; }
            bool ok = false;
            string no = null;
            try
            {
                ui.Invoke((MethodInvoker)delegate
                {
                    if (DropClosedCurtain("al pasar al en vivo")) { no = "el telon ya estaba cerrado"; return; }
                    CoverForm old = curtain;
                    if (old == null || splash != null || curtainLive) { no = "ya no hay telon negro"; return; }
                    CoverForm c = new CoverForm(null, null, NeonBlue, false, null, 0, h);
                    c.Note = Log.Write;
                    IntPtr ch = c.Handle; // (crea la ventana y registra la miniatura sin que se vea nada)
                    if (!c.LiveOk || ch == IntPtr.Zero) { no = c.LiveLost ?? "sin miniatura"; c.Dispose(); return; }
                    c.Show();
                    c.ShowNow();
                    c.RefreshLive();
                    curtain = c;
                    curtainLive = true;
                    curtainKind = "en vivo";
                    // (curtainSince sigue: "Telon quitado" cuenta desde el negro). El negro, por su camino de siempre (en su siguiente paso se
                    // cierra, sin volver a ponerse encima)
                    try { old.FadeOut(0.01); } catch { }
                    ok = true;
                });
            }
            catch (Exception ex) { no = ex.GetType().Name + ": " + ex.Message; }
            Log.Write(ok ? "Telon en vivo (" + why + "): la imagen de la consola a la vista mientras Magpie prepara (" + (curtainSince != null ? curtainSince.ElapsedMilliseconds + " ms en negro" : "") + ")"
                         : "Telon en vivo (" + why + "): no (" + no + "): sigue el negro");
            return ok;
        }

        // Lo mismo en otro hilo: hasta 2 s a que el mpv nuevo diga que lo que ve es la consola (su primer aviso puede ser "No Signal" un
        // momento), si sigue el telon negro y Magpie aun no escala ni hay puente
        void SwapToLiveCurtainAsync(string why, Process p)
        {
            if (cfg.CurtainLive == 0 || (cfg.CurtainLive == 2 && cfg.CapMode)) return;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 2000 && !userExit && mpv == p && curtain != null && relevo == null &&
                           !(consoleOn && signalOn))
                        Thread.Sleep(50);
                    if (userExit || mpv != p || curtain == null || relevo != null) return;
                    SwapToLiveCurtain(why);
                }
                catch (Exception ex) { Log.Error("Telon en vivo (" + why + ")", ex); }
            });
            t.IsBackground = true;
            t.Name = "Telon en vivo";
            t.Start();
        }

        // Lo que ha sido el telon que se quita (la linea "Telon quitado" del log)
        string CurtainKindText(CoverForm c)
        {
            if (curtainFrozen) return "con la imagen congelada";
            if (curtainLive)
                return c != null && c.LiveLost != null
                    ? "en vivo, sin la imagen de mpv desde los " + c.LiveLostMs + " ms (" + c.LiveLost + "; oscuro desde entonces)"
                    : "en vivo";
            return curtainKind ?? "en negro";
        }
    }
}
