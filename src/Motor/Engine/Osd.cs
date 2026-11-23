using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConsoleEnhancer.Motor
{
    // Aviso moderno pintado a la resolucion de salida (despues de DLSS: nitido y sin pasar por la generacion de fotogramas).
    // Pastilla oscura translucida con borde fino, punto de color, titulo en blanco y el resto en gris ("Titulo · detalle · ...").
    // 15-11 (Prisma): es el aspecto de toda la app (el lanzador lo copia en PrismaUi.cs y el script de mpv en ASS)
    class Osd
    {
        // (1.2.4 ronda 7, TEXTOS: 3600 de ancho; con 2400 a 4K -escala 2- solo cabian ~1160 px de 1080p y los avisos de varios cambios a la
        // vez -"Captura 1440p · Smooth Motion x2 · 30 → 60 fps · x4 no: ... · DLSS 5 nivel 4 · 1 pasada · DLSS M"- salian cortados por la
        // derecha; a 1080p / 1440p ya cabia el ancho de la pantalla. El lanzador lo mide antes: Avisos.cs, AvisoCabe)
        public const int MaxW = 3600, MaxH = 200;
        public static readonly Color Cyan = Color.FromArgb(0, 200, 235), Warn = Color.FromArgb(255, 60, 85);
        readonly object sync = new object();
        string text;
        Color accent = Cyan;
        long shownAt, holdMs;
        int version;
        byte[] pixels; int pw, ph;

        // color null = cian (antes se quedaba el del aviso anterior: tras "Enciende la consola" salian rojos)
        public void Show(string t, int ms, Color? color)
        {
            lock (sync)
            {
                text = t;
                holdMs = Math.Max(300, ms);
                accent = color.HasValue ? color.Value : Cyan;
                shownAt = Clock.Now;
                version++;
                pixels = null;
            }
        }

        public void Hide() { lock (sync) { text = null; version++; } }

        // Opacidad (0..1) y desplazamiento vertical (px) ahora. 0 = no hay aviso
        public double Opacity(out double slide)
        {
            slide = 0;
            lock (sync)
            {
                if (text == null) return 0;
                double t = Clock.SinceMs(shownAt);
                const double fin = 140, fout = 260;
                if (t < fin) { double k = t / fin; slide = (1 - k) * (1 - k) * 14; return k; }
                if (t < fin + holdMs) return 1;
                double o = 1 - (t - fin - holdMs) / fout;
                if (o <= 0) { text = null; return 0; }
                return o;
            }
        }

        // Pixeles BGRA premultiplicados (filas de w*4 bytes). Se repintan solo si el texto cambio
        public bool GetPixels(int outH, ref int seenVersion, out byte[] px, out int w, out int h)
        {
            string t; Color c; int v;
            lock (sync) { t = text; c = accent; v = version; px = pixels; w = pw; h = ph; }
            if (t == null) { px = null; w = h = 0; return false; }
            if (px != null && v == seenVersion) return false;
            Render(t, c, outH, out px, out w, out h);
            lock (sync) { if (version == v) { pixels = px; pw = w; ph = h; } }
            seenVersion = v;
            return true;
        }

        static FontFamily Family(string[] names)
        {
            using (InstalledFontCollection fc = new InstalledFontCollection())
                foreach (string n in names)
                    foreach (FontFamily f in fc.Families)
                        if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return new FontFamily(f.Name);
            return FontFamily.GenericSansSerif;
        }

        static FontFamily titleFam, bodyFam;

        static GraphicsPath Pill(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static void Render(string t, Color accent, int outH, out byte[] px, out int w, out int h)
        {
            if (titleFam == null)
            {
                titleFam = Family(new[] { "Segoe UI Variable Display Semib", "Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI" });
                bodyFam = Family(new[] { "Segoe UI Variable Text", "Segoe UI Variable Display", "Segoe UI" });
            }
            float u = outH / 1080f;
            string title = t, rest = null;
            int cut = t.IndexOf(" · ", StringComparison.Ordinal);
            if (cut > 0) { title = t.Substring(0, cut); rest = t.Substring(cut + 3); }

            FontStyle titleStyle = titleFam.Name.IndexOf("Semib", StringComparison.OrdinalIgnoreCase) >= 0 ? FontStyle.Regular : FontStyle.Bold;
            using (Font ft = new Font(titleFam, 25 * u, titleStyle, GraphicsUnit.Pixel))
            using (Font fb = new Font(bodyFam, 23 * u, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Bitmap probe = new Bitmap(4, 4))
            using (Graphics pg = Graphics.FromImage(probe))
            {
                StringFormat sf = StringFormat.GenericTypographic;
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                SizeF st = pg.MeasureString(title, ft, 100000, sf);
                string restShown = rest == null ? null : rest.Replace(" · ", "   ·   ");
                SizeF sr = restShown == null ? SizeF.Empty : pg.MeasureString(restShown, fb, 100000, sf);
                float padX = 30 * u, dot = 11 * u, gapDot = 16 * u, gapText = 18 * u;
                float pillH = 62 * u, shadow = 18 * u;
                float pillW = padX + dot + gapDot + st.Width + (restShown != null ? gapText + sr.Width : 0) + padX;
                w = Math.Min(MaxW, (int)Math.Ceiling(pillW + shadow * 2));
                h = Math.Min(MaxH, (int)Math.Ceiling(pillH + shadow * 2));
                pillW = Math.Min(pillW, w - shadow * 2);
                using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.TextRenderingHint = TextRenderingHint.AntiAlias;
                        RectangleF pill = new RectangleF(shadow, shadow * 0.7f, pillW, pillH);
                        // Sombra suave hacia abajo: capas que se abren (15-11: por arriba ya no asoma; igual que PrismaUi del lanzador)
                        for (int i = 6; i >= 1; i--)
                        {
                            float e = i * shadow / 6f;
                            using (GraphicsPath sp = Pill(new RectangleF(pill.X - e * 0.5f, pill.Y + e * 0.4f, pill.Width + e, pill.Height + e * 0.6f), pillH / 2 + e))
                            using (SolidBrush sb = new SolidBrush(Color.FromArgb(11, 0, 0, 0))) g.FillPath(sb, sp);
                        }
                        using (GraphicsPath p = Pill(pill, pillH / 2))
                        {
                            using (LinearGradientBrush bg = new LinearGradientBrush(pill, Color.FromArgb(232, 30, 33, 41), Color.FromArgb(232, 17, 19, 25), 90f))
                                g.FillPath(bg, p);
                            // Brillo arriba (vidrio), dentro de la pastilla (15-11: sus esquinas, mas cerradas, asomaban por fuera de la curva)
                            g.SetClip(p);
                            using (GraphicsPath hp = Pill(new RectangleF(pill.X + 2 * u, pill.Y + 1.5f * u, pill.Width - 4 * u, pillH * 0.48f), pillH / 2))
                            using (LinearGradientBrush hb = new LinearGradientBrush(new RectangleF(pill.X, pill.Y, pill.Width, pillH * 0.5f + 1),
                                       Color.FromArgb(20, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                                g.FillPath(hb, hp);
                            g.ResetClip();
                            using (Pen border = new Pen(Color.FromArgb(46, 255, 255, 255), Math.Max(1f, 1.2f * u))) g.DrawPath(border, p);
                        }
                        float cx = pill.X + padX + dot / 2, cy = pill.Y + pillH / 2;
                        using (GraphicsPath gp = new GraphicsPath())
                        {
                            gp.AddEllipse(cx - dot * 1.6f, cy - dot * 1.6f, dot * 3.2f, dot * 3.2f);
                            using (PathGradientBrush pb = new PathGradientBrush(gp))
                            {
                                pb.CenterColor = Color.FromArgb(120, accent);
                                pb.SurroundColors = new[] { Color.FromArgb(0, accent) };
                                g.FillPath(pb, gp);
                            }
                        }
                        using (SolidBrush db = new SolidBrush(accent)) g.FillEllipse(db, cx - dot / 2, cy - dot / 2, dot, dot);
                        float tx = pill.X + padX + dot + gapDot;
                        using (SolidBrush tb = new SolidBrush(Color.FromArgb(246, 248, 252)))
                            g.DrawString(title, ft, tb, tx, cy - st.Height / 2 - 1 * u, sf);
                        if (restShown != null)
                            using (SolidBrush rb = new SolidBrush(Color.FromArgb(168, 176, 190)))
                                g.DrawString(restShown, fb, rb, tx + st.Width + gapText, cy - sr.Height / 2, sf);
                    }
                    BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                    try
                    {
                        px = new byte[w * h * 4];
                        for (int y = 0; y < h; y++) Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), px, y * w * 4, w * 4);
                    }
                    finally { b.UnlockBits(d); }
                }
            }
        }
    }
}
