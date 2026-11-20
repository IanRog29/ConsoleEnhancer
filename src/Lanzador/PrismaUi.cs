// PrismaUi.cs - Prisma: el aspecto comun de la app (avisos en pastilla, telon, pantalla de carga) y su logotipo (el prisma).
// Lo compilan Switch2Magpie.exe (build.bat) y la herramienta del icono (tools\PrismaIcon.cs): el icono y el logotipo de la
// pantalla de carga salen del mismo dibujo. El aviso copia al del motor propio (src\Motor\Engine\Osd.cs) y al
// de mpv (scripts\switch2_power.lua): pastilla oscura translucida, borde fino, punto de color, titulo en blanco y el resto
// en gris ("Titulo · detalle · detalle"). C# 5
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace ConsoleEnhancer
{
    static class PrismaUi
    {
        public const string AppName = "ConsoleEnhancer";
        // 19-11 (Prisma): dentro de la app no se ve el nombre, solo el logotipo. ConsoleEnhancer si lo enseña (titulos de los mensajes, menu)
        public const string ShownName = AppName;

        // Cian y rojo de Prisma (los dos rayos del prisma; rojo = aviso), el panel y el texto
        public static readonly Color Accent = Color.FromArgb(0, 200, 235);       // #00C8EB
        public static readonly Color Warn = Color.FromArgb(255, 60, 85);         // #FF3C55
        public static readonly Color PanelTop = Color.FromArgb(30, 33, 41);      // #1E2129
        public static readonly Color PanelBottom = Color.FromArgb(17, 19, 25);   // #111319
        public static readonly Color TitleColor = Color.FromArgb(246, 248, 252);
        public static readonly Color DetailColor = Color.FromArgb(168, 176, 190); // #A8B0BE
        public const int PanelAlpha = 232;   // ~90 %
        public const int BorderAlpha = 46;   // blanco al ~18 %

        // ---------------- Letra ----------------
        // (Solo los nombres: las letras se crean en cada uso, en el hilo que pinta. El telon del mando se compone fuera del
        // hilo de la interfaz y los objetos de GDI+ no se comparten entre hilos)
        static readonly object fontSync = new object();
        static string titleFam, bodyFam;
        static FontStyle titleStyle;

        static string Find(string[] names)
        {
            foreach (string n in names)
            {
                try { using (FontFamily f = new FontFamily(n)) return f.Name; }
                catch (ArgumentException) { }
            }
            return FontFamily.GenericSansSerif.Name;
        }

        static void InitFonts()
        {
            lock (fontSync)
            {
                if (titleFam != null) return;
                // (GDI+ corta los nombres a 31 letras: "Segoe UI Variable Display Semib" es la Display Semibold)
                bodyFam = Find(new string[] { "Segoe UI Variable Text", "Segoe UI" });
                string t = Find(new string[] { "Segoe UI Variable Display Semib", "Segoe UI Semibold", "Segoe UI" });
                titleStyle = t.IndexOf("Semib", StringComparison.OrdinalIgnoreCase) >= 0 ? FontStyle.Regular : FontStyle.Bold;
                titleFam = t;
            }
        }

        // Titulos (Segoe UI Variable Display Semibold) y detalles (Segoe UI Variable Text), en pixeles
        public static Font TitleFont(float px) { InitFonts(); return new Font(titleFam, Math.Max(1f, px), titleStyle, GraphicsUnit.Pixel); }
        public static Font BodyFont(float px) { InitFonts(); return new Font(bodyFam, Math.Max(1f, px), FontStyle.Regular, GraphicsUnit.Pixel); }
        // Para menus y ventanas normales (en puntos: WinForms la escala con el DPI)
        public static Font BodyFontPt(float pt) { InitFonts(); return new Font(bodyFam, pt, FontStyle.Regular, GraphicsUnit.Point); }
        public static Font TitleFontPt(float pt) { InitFonts(); return new Font(titleFam, pt, titleStyle, GraphicsUnit.Point); }

        [ThreadStatic] static StringFormat typo;
        public static StringFormat Typo
        {
            get
            {
                if (typo == null)
                {
                    StringFormat sf = (StringFormat)StringFormat.GenericTypographic.Clone();
                    sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                    typo = sf;
                }
                return typo;
            }
        }

        // ---------------- Formas ----------------
        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.01f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Mancha de luz radial (centro con 'alpha', borde transparente)
        public static void Glow(Graphics g, RectangleF r, Color c, int alpha)
        {
            if (alpha <= 0 || r.Width < 1 || r.Height < 1) return;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterColor = Color.FromArgb(Math.Min(255, alpha), c);
                    b.SurroundColors = new Color[] { Color.FromArgb(0, c) };
                    g.FillPath(b, p);
                }
            }
        }

        static int A(double a) { return Math.Max(0, Math.Min(255, (int)Math.Round(a))); }
        static Color Mix(Color a, Color b, float k)
        {
            return Color.FromArgb(A(a.R + (b.R - a.R) * k), A(a.G + (b.G - a.G) * k), A(a.B + (b.B - a.B) * k));
        }

        // ---------------- El aviso: pastilla ----------------
        // "Titulo · detalle · detalle": el titulo en blanco y el resto en gris, con los puntos mas separados
        public static void Split(string text, out string title, out string rest)
        {
            title = text ?? "";
            rest = null;
            int cut = title.IndexOf(" · ", StringComparison.Ordinal);
            if (cut > 0)
            {
                rest = title.Substring(cut + 3).Replace(" · ", "   ·   ");
                title = title.Substring(0, cut);
            }
        }

        // Medidas de una pastilla. u = 1: las del aviso del motor a 1080p (letra de 25 y 23 px, 62 px de alto)
        public struct Pill
        {
            public string Title, Rest;
            public float U, W, H, TitleW, TitleH, RestW, RestH;
            public float Shadow { get { return 18 * U; } }
        }

        public static Pill MeasurePill(Graphics g, string text, float u)
        {
            Pill p = new Pill();
            Split(Idioma.T(text), out p.Title, out p.Rest);
            p.U = u;
            using (Font ft = TitleFont(25 * u))
            using (Font fb = BodyFont(23 * u))
            {
                SizeF st = g.MeasureString(p.Title, ft, 100000, Typo);
                p.TitleW = st.Width;
                p.TitleH = st.Height;
                if (p.Rest != null)
                {
                    SizeF sr = g.MeasureString(p.Rest, fb, 100000, Typo);
                    p.RestW = sr.Width;
                    p.RestH = sr.Height;
                }
            }
            p.H = 62 * u;
            p.W = 30 * u + 11 * u + 16 * u + p.TitleW + (p.Rest != null ? 18 * u + p.RestW : 0) + 30 * u;
            return p;
        }

        // La pastilla en r (sin contar la sombra, que sale alrededor y algo por debajo). alpha 0..1 para los fundidos
        public static void DrawPill(Graphics g, Pill p, RectangleF r, Color accent, float alpha)
        {
            if (alpha <= 0.003f) return;
            float u = p.U, sh = p.Shadow, h = r.Height;
            SmoothingMode sm = g.SmoothingMode;
            System.Drawing.Text.TextRenderingHint th = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            // Sombra suave hacia abajo: capas que se abren (como la del motor; por arriba no asoma)
            for (int i = 6; i >= 1; i--)
            {
                float e = i * sh / 6f;
                using (GraphicsPath sp = RoundRect(new RectangleF(r.X - e * 0.5f, r.Y + e * 0.4f, r.Width + e, h + e * 0.6f), h / 2 + e))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(A(11 * alpha), 0, 0, 0))) g.FillPath(sb, sp);
            }
            using (GraphicsPath path = RoundRect(r, h / 2))
            {
                using (LinearGradientBrush bg = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, h + 2),
                           Color.FromArgb(A(PanelAlpha * alpha), PanelTop), Color.FromArgb(A(PanelAlpha * alpha), PanelBottom), 90f))
                    g.FillPath(bg, path);
                // Brillo arriba (vidrio), dentro de la pastilla (sus esquinas, mas cerradas, asomaban por fuera de la curva)
                Region clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (GraphicsPath hp = RoundRect(new RectangleF(r.X + 2 * u, r.Y + 1.5f * u, r.Width - 4 * u, h * 0.48f), h / 2))
                using (LinearGradientBrush hb = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, h * 0.5f + 1),
                           Color.FromArgb(A(20 * alpha), 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillPath(hb, hp);
                g.Clip = clip;
                clip.Dispose();
                using (Pen border = new Pen(Color.FromArgb(A(BorderAlpha * alpha), 255, 255, 255), Math.Max(1f, 1.2f * u))) g.DrawPath(border, path);
            }
            float dot = 11 * u, cx = r.X + 30 * u + dot / 2, cy = r.Y + h / 2;
            Glow(g, new RectangleF(cx - dot * 1.6f, cy - dot * 1.6f, dot * 3.2f, dot * 3.2f), accent, A(120 * alpha));
            using (SolidBrush db = new SolidBrush(Color.FromArgb(A(255 * alpha), accent))) g.FillEllipse(db, cx - dot / 2, cy - dot / 2, dot, dot);
            float tx = r.X + 30 * u + dot + 16 * u;
            using (Font ft = TitleFont(25 * u))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(A(255 * alpha), TitleColor)))
                g.DrawString(p.Title, ft, tb, tx, cy - p.TitleH / 2 - 1 * u, Typo);
            if (p.Rest != null)
                using (Font fb = BodyFont(23 * u))
                using (SolidBrush rb = new SolidBrush(Color.FromArgb(A(255 * alpha), DetailColor)))
                    g.DrawString(p.Rest, fb, rb, tx + p.TitleW + 18 * u, cy - p.RestH / 2, Typo);
            g.SmoothingMode = sm;
            g.TextRenderingHint = th;
        }

        // Una pastilla centrada en (cx, top..): devuelve su rectangulo
        public static RectangleF DrawPillCentered(Graphics g, string text, float u, float cx, float cy, Color accent, float alpha)
        {
            Pill p = MeasurePill(g, text, u);
            RectangleF r = new RectangleF(cx - p.W / 2, cy - p.H / 2, p.W, p.H);
            DrawPill(g, p, r, accent, alpha);
            return r;
        }

        // Linea de progreso fina e indeterminada: un tramo de luz que recorre la pista (t en segundos)
        public static void DrawProgressLine(Graphics g, RectangleF track, double t, Color accent, float alpha)
        {
            if (alpha <= 0.003f || track.Width < 2) return;
            SmoothingMode sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = track.Height / 2;
            using (GraphicsPath tp = RoundRect(track, rad))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(A(34 * alpha), 255, 255, 255))) g.FillPath(tb, tp);
            // Un barrido cada 1,25 s, con aceleracion suave; el tramo mide el 34 % de la pista (ya a la vista desde el principio)
            double ph = (t / 1.25 + 0.4) % 1.0;
            double e = ph * ph * (3 - 2 * ph);
            float len = track.Width * 0.34f;
            float x0 = track.X - len + (float)e * (track.Width + len);
            RectangleF seg = RectangleF.Intersect(new RectangleF(x0, track.Y, len, track.Height), track);
            if (seg.Width > 0.5f)
            {
                Region old = g.Clip;
                using (GraphicsPath tp = RoundRect(track, rad))
                {
                    g.SetClip(tp, CombineMode.Intersect);
                    using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(x0 - 1, track.Y, len + 2, track.Height),
                               Color.Transparent, Color.Transparent, 0f))
                    {
                        ColorBlend cb = new ColorBlend(4);
                        cb.Colors = new Color[] { Color.FromArgb(0, accent), Color.FromArgb(A(255 * alpha), accent),
                                                  Color.FromArgb(A(255 * alpha), Mix(accent, Color.White, 0.35f)), Color.FromArgb(0, accent) };
                        cb.Positions = new float[] { 0f, 0.55f, 0.8f, 1f };
                        b.InterpolationColors = cb;
                        g.FillRectangle(b, seg);
                    }
                    g.Clip = old;
                }
                old.Dispose();
            }
            g.SmoothingMode = sm;
        }

        // ---------------- El logotipo: el prisma ----------------
        // Un haz blanco entra en un prisma de cristal y sale separado en rojo y cian (los colores de Prisma; el rojo se desvia menos y
        // sale arriba, como en la luz de verdad). Dibujado en una caja de 256 unidades. tile = sobre su cuadrado oscuro
        // redondeado (el icono); sin el, el haz y los rayos se desvanecen en los extremos (pantalla de carga).
        // rays 0..1: cuanto han salido los rayos (animacion); alpha 0..1
        public static void DrawMark(Graphics g, RectangleF box, bool tile, float rays, float alpha)
        {
            if (alpha <= 0.003f || box.Width < 4) return;
            float s = box.Width / 256f;     // pixeles por unidad
            float px = 1f / s;              // un pixel en unidades
            bool tiny = box.Width < 40, small = box.Width < 72;
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(box.X, box.Y);
            g.ScaleTransform(s, s);

            RectangleF tileR = new RectangleF(8, 8, 240, 240);
            float tileRad = 54;
            GraphicsPath tilePath = null;
            if (tile)
            {
                if (tiny) { tileR = new RectangleF(0.5f * px, 0.5f * px, 256 - px, 256 - px); tileRad = 58; }
                tilePath = RoundRect(tileR, tileRad);
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(0, tileR.Y - 1, 256, tileR.Height + 2),
                           Color.FromArgb(A(255 * alpha), 36, 40, 50), Color.FromArgb(A(255 * alpha), 10, 11, 15), 90f))
                    g.FillPath(b, tilePath);
                g.SetClip(tilePath, CombineMode.Intersect);
            }

            // Geometria: el prisma (A arriba, B y C la base), el haz entra por AB en E y sale por AC en X (dentro, horizontal)
            PointF pa = new PointF(104, 56), pb = new PointF(38, 172), pc = new PointF(170, 172);
            float te = 0.55f;
            PointF pe = new PointF(pa.X + (pb.X - pa.X) * te, pa.Y + (pb.Y - pa.Y) * te);
            PointF pxp = new PointF(pa.X + (pc.X - pa.X) * te, pe.Y);
            float rk = Math.Max(0f, Math.Min(1f, rays));
            GraphicsPath tri = new GraphicsPath();
            tri.AddPolygon(new PointF[] { pa, pc, pb });

            // Haz de entrada: desde el borde izquierdo, subiendo hacia E (fuera del prisma: el halo no se mete en el cristal)
            Region outside = g.Clip.Clone();
            g.SetClip(tri, CombineMode.Exclude);
            PointF p0 = new PointF(-8, pe.Y + (pe.X + 8) * 0.32f);
            PointF pin = new PointF(pe.X + 6, pe.Y - 6 * 0.32f); // (un poco dentro: el canto del prisma tapa la junta)
            float beamW = Math.Max(tiny ? 15f : 9f, (tiny ? 1.2f : 1.1f) * px);
            Color beamC = Color.FromArgb(A(250 * alpha), 255, 255, 255);
            if (!small) SoftLine(g, p0, pin, Color.White, beamW, 26, 0.16f * alpha, tile);
            using (Pen bp = new Pen(beamC, beamW))
            {
                if (!tile)
                {
                    LinearGradientBrush lb = new LinearGradientBrush(new PointF(p0.X - 1, 0), new PointF(pe.X + 1, 0),
                        Color.FromArgb(0, 255, 255, 255), beamC);
                    bp.Brush = lb;
                    lb.Dispose();
                }
                g.DrawLine(bp, p0, pin);
            }

            // Rayos: cuñas que se abren desde X (rojo arriba, cian abajo)
            float endX = 262;
            float half0 = tiny ? 7f : 4.5f, half1 = tiny ? 27f : 21f;
            float len = (endX - pxp.X) * rk;
            PointF[] red = null, cyan = null;
            if (len > 1)
            {
                float x1 = pxp.X + len, h1 = half0 + (half1 - half0) * rk;
                red = Wedge(pxp, x1, 0.10f, half0, h1, -half0 * 0.55f);
                cyan = Wedge(pxp, x1, 0.52f, half0, h1, half0 * 0.55f);
                Color redHot = Mix(Warn, Color.White, 0.25f), cyanHot = Mix(Accent, Color.White, 0.25f);
                if (!small)
                {
                    SoftPolygon(g, red, Warn, 22, 0.20f * alpha);
                    SoftPolygon(g, cyan, Accent, 22, 0.22f * alpha);
                }
                FillRay(g, cyan, pxp.X, x1, cyanHot, Accent, tile, alpha);
                FillRay(g, red, pxp.X, x1, redHot, Warn, tile, alpha);
            }
            g.Clip = outside;

            // El prisma: cristal oscuro (un velo blanco muy tenue) con el canto blanco
            using (LinearGradientBrush fb = new LinearGradientBrush(new PointF(0, pa.Y - 1), new PointF(0, pb.Y + 1),
                       Color.FromArgb(A((tiny ? 64 : 28) * alpha), 255, 255, 255), Color.FromArgb(A((tiny ? 24 : 4) * alpha), 255, 255, 255)))
                g.FillPath(fb, tri);
            if (!tiny)
            {
                // Dentro: el haz se abre de E hacia la cara de salida, ya tiñéndose (blanco -> rojo arriba / cian abajo)
                PointF[] fan = { pe, new PointF(pxp.X, pxp.Y - half0 * 1.2f), new PointF(pxp.X, pxp.Y + half0 * 1.2f) };
                using (GraphicsPath fp = new GraphicsPath())
                {
                    fp.AddPolygon(fan);
                    using (LinearGradientBrush ib = new LinearGradientBrush(new PointF(pe.X - 1, 0), new PointF(pxp.X + 1, 0),
                               Color.FromArgb(A(200 * alpha), 255, 255, 255), Color.FromArgb(A(70 * alpha), 255, 255, 255)))
                        g.FillPath(ib, fp);
                }
            }
            {
                float edge = Math.Max(tiny ? 17f : 8.5f, (tiny ? 1.3f : 1.15f) * px);
                using (Pen ep = new Pen(Color.FromArgb(A(250 * alpha), 255, 255, 255), edge))
                {
                    ep.LineJoin = LineJoin.Round;
                    g.DrawPath(ep, tri);
                }
            }
            tri.Dispose();
            outside.Dispose();

            if (tile)
            {
                g.ResetClip();
                // Canto del cuadrado: una luz fina arriba que se apaga hacia abajo
                if (!tiny)
                    using (LinearGradientBrush rb = new LinearGradientBrush(new RectangleF(0, tileR.Y - 1, 256, tileR.Height + 2),
                               Color.FromArgb(A(46 * alpha), 255, 255, 255), Color.FromArgb(A(6 * alpha), 255, 255, 255), 90f))
                    using (Pen ib = new Pen(rb, Math.Max(1.6f, 1f * px)))
                    using (GraphicsPath inner = RoundRect(RectangleF.Inflate(tileR, -1f, -1f), tileRad - 1f))
                        g.DrawPath(ib, inner);
                tilePath.Dispose();
            }
            g.Restore(state);
        }

        // Cuña desde p hasta x1, bajando con la pendiente 'slope'; mitad del ancho de h0 (al salir) a h1 (al final). off: corre el
        // arranque (los dos rayos salen casi del mismo punto)
        static PointF[] Wedge(PointF p, float x1, float slope, float h0, float h1, float off)
        {
            float dx = x1 - p.X, yc0 = p.Y + off, yc1 = p.Y + off + dx * slope;
            return new PointF[] { new PointF(p.X, yc0 - h0), new PointF(x1, yc1 - h1), new PointF(x1, yc1 + h1), new PointF(p.X, yc0 + h0) };
        }

        static void FillRay(Graphics g, PointF[] w, float x0, float x1, Color hot, Color c, bool tile, float alpha)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddPolygon(w);
                using (LinearGradientBrush b = new LinearGradientBrush(new PointF(x0 - 1, 0), new PointF(x1 + 1, 0), hot, c))
                {
                    ColorBlend cb = new ColorBlend(3);
                    cb.Colors = new Color[] { Color.FromArgb(A(255 * alpha), hot), Color.FromArgb(A(255 * alpha), c),
                                              Color.FromArgb(A((tile ? 255 : 0) * alpha), c) };
                    cb.Positions = new float[] { 0f, 0.3f, 1f };
                    b.InterpolationColors = cb;
                    g.FillPath(b, p);
                }
            }
        }

        // Halo suave alrededor de una forma: trazos cada vez mas anchos y tenues (GDI+ no tiene desenfoque)
        static void SoftPolygon(Graphics g, PointF[] pts, Color c, float width, float alpha)
        {
            const int n = 14;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddPolygon(pts);
                for (int i = n; i >= 1; i--)
                    using (Pen pen = new Pen(Color.FromArgb(A(255 * alpha / n), c), width * i / n))
                    {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, p);
                    }
            }
        }

        static void SoftLine(Graphics g, PointF a, PointF b, Color c, float core, float width, float alpha, bool solidStart)
        {
            const int n = 14;
            for (int i = n; i >= 1; i--)
                using (Pen pen = new Pen(Color.FromArgb(A(255 * alpha / n), c), core + width * i / n))
                {
                    if (!solidStart)
                    {
                        LinearGradientBrush lb = new LinearGradientBrush(new PointF(a.X - 1, 0), new PointF(b.X + 1, 0),
                            Color.FromArgb(0, c), Color.FromArgb(A(255 * alpha / n), c));
                        pen.Brush = lb;
                        lb.Dispose();
                    }
                    g.DrawLine(pen, a, b);
                }
        }

        // Logotipo + "Prisma" a la derecha, centrado en (cx, cy). h = alto del logotipo
        public static void DrawLockup(Graphics g, float cx, float cy, float h, float rays, float alpha, float textAlpha)
        {
            using (Font f = TitleFont(h * 0.5f))
            {
                SizeF sz = g.MeasureString(AppName, f, 100000, Typo);
                float gap = h * 0.1f, w = h + gap + sz.Width;
                float x = cx - w / 2;
                DrawMark(g, new RectangleF(x, cy - h / 2, h, h), false, rays, alpha);
                System.Drawing.Text.TextRenderingHint th = g.TextRenderingHint;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(A(255 * textAlpha), TitleColor)))
                    g.DrawString(AppName, f, b, x + h + gap, cy - sz.Height / 2 + h * 0.02f, Typo);
                g.TextRenderingHint = th;
            }
        }

        // ---------------- El icono ----------------
        // prisma.ico va dentro del exe dos veces: como icono de Windows (/win32icon) y como recurso (/resource), del que salen
        // los tamaños que hagan falta (bandeja, ventanas). null si no esta (la herramienta del icono)
        static byte[] icoBytes;
        static Icon formIcon;
        static bool formIconTried;

        // El de las ventanas (con todos los tamaños: Windows elige), uno para toda la app (no se libera)
        public static Icon FormIcon
        {
            get
            {
                if (!formIconTried) { formIconTried = true; formIcon = AppIcon(0); }
                return formIcon;
            }
        }

        public static Icon AppIcon(int size)
        {
            try
            {
                if (icoBytes == null)
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Prisma.ico"))
                    {
                        if (s == null) return null;
                        using (MemoryStream ms = new MemoryStream()) { s.CopyTo(ms); icoBytes = ms.ToArray(); }
                    }
                }
                using (MemoryStream ms = new MemoryStream(icoBytes))
                    return size > 0 ? new Icon(ms, size, size) : new Icon(ms);
            }
            catch { return null; }
        }

        // ---------------- Menu de la bandeja ----------------
        // Oscuro como los avisos: cabecera con el icono y el nombre, texto claro, la marca de activado en cian
        public static void StyleMenu(ContextMenuStrip menu)
        {
            menu.Renderer = new MenuRenderer();
            menu.Font = BodyFontPt(10f);
            menu.ShowImageMargin = true;
            ToolStripLabel head = new ToolStripLabel(ShownName);
            head.Font = TitleFontPt(11f);
            head.Margin = new Padding(0, 4, 0, 2);
            int ico = Math.Max(16, SystemInformation.SmallIconSize.Width);
            Icon i = AppIcon(ico);
            if (i != null) { head.Image = i.ToBitmap(); head.ImageScaling = ToolStripItemImageScaling.None; i.Dispose(); }
            menu.Items.Insert(0, head);
            menu.Items.Insert(1, new ToolStripSeparator());
            // Mas aire entre opciones (como un menu de Windows 11)
            foreach (ToolStripItem it in menu.Items)
                if (it is ToolStripMenuItem) it.Padding = new Padding(it.Padding.Left, 5, it.Padding.Right + 12, 5);
        }

        class MenuRenderer : ToolStripProfessionalRenderer
        {
            public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? TitleColor : DetailColor;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = DetailColor;
                base.OnRenderArrow(e);
            }

            // Activado: un punto cian (como el de los avisos) en vez de la marca
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Rectangle r = e.ImageRectangle;
                float d = Math.Max(6f, Math.Max(Math.Min(r.Width, r.Height) * 0.45f, e.Item.Height * 0.2f));
                SmoothingMode sm = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
                Glow(e.Graphics, new RectangleF(cx - d * 1.4f, cy - d * 1.4f, d * 2.8f, d * 2.8f), Accent, 110);
                using (SolidBrush b = new SolidBrush(Accent)) e.Graphics.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
                e.Graphics.SmoothingMode = sm;
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                Rectangle r = e.Item.ContentRectangle;
                int y = r.Y + r.Height / 2;
                using (Pen p = new Pen(Color.FromArgb(42, 255, 255, 255))) e.Graphics.DrawLine(p, r.Left + 6, y, r.Right - 6, y);
            }
        }

        class MenuColors : ProfessionalColorTable
        {
            static readonly Color Bg = Color.FromArgb(24, 26, 33), Hover = Color.FromArgb(42, 46, 57), Line = Color.FromArgb(60, 64, 76);
            public override Color ToolStripDropDownBackground { get { return Bg; } }
            public override Color ImageMarginGradientBegin { get { return Bg; } }
            public override Color ImageMarginGradientMiddle { get { return Bg; } }
            public override Color ImageMarginGradientEnd { get { return Bg; } }
            public override Color MenuBorder { get { return Line; } }
            public override Color MenuItemBorder { get { return Hover; } }
            public override Color MenuItemSelected { get { return Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return Hover; } }
            public override Color MenuItemPressedGradientMiddle { get { return Hover; } }
            public override Color MenuItemPressedGradientEnd { get { return Hover; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Bg; } }
            public override Color CheckBackground { get { return Bg; } }
            public override Color CheckSelectedBackground { get { return Hover; } }
            public override Color CheckPressedBackground { get { return Hover; } }
        }
    }
}
