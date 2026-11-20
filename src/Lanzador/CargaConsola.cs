// CargaConsola.cs - La escena de la pantalla de carga sin ninguna consola (18-11, agente CONSOLAS). Antes: la Switch 2 con los
// Joy-Con entrando deslizandose (SplashForm). Ahora el prisma de Prisma en grande, con lineas finas: un haz de luz blanca entra
// desde la izquierda, el prisma de cristal se ilumina por dentro y salen sus dos rayos (rojo arriba, cian abajo, como en el
// logotipo: PrismaUi.DrawMark) abriendose hacia la derecha hasta el borde de la pantalla. Luego, cada 1,8 s, un pulso de luz
// recorre el haz, cruza el prisma y sale por los dos rayos (la imagen de la consola entrando y saliendo tratada). Debajo el
// nombre. Al terminar (lit) el prisma destella y los rayos se encienden del todo. La barra y el aviso van donde iban (BarraY).
// Lo llama SplashForm.OnPaint en lugar de la consola. C# 5
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ConsoleEnhancer
{
    static class CargaConsola
    {
        // Geometria del prisma: la de PrismaUi.DrawMark (caja de 256 unidades). El haz entra por AB en E y sale por AC en X
        static readonly PointF PA = new PointF(104, 56), PB = new PointF(38, 172), PC = new PointF(170, 172);
        const float TE = 0.55f;
        const float PendHaz = 0.32f;                    // el haz sube hacia el prisma (baja 0,32 por unidad hacia la izquierda)
        const float PendRojo = 0.10f, PendCian = 0.52f; // los rayos bajan hacia la derecha (el rojo se desvia menos)
        const float Medio0 = 4.5f;                      // medio ancho de cada rayo al salir (unidades)
        const float Abre = 0.075f;                      // lo que se abre cada rayo por unidad (el logotipo: 0,136; aqui son largos)
        const float Caja = 400f;                        // lado de la caja del prisma a 1080p (el logotipo de antes media 128)
        const double Periodo = 1.8, PrimerPulso = 1.45;

        // Donde va la barra de progreso (el aviso, 72 px mas abajo): lo mismo que con la consola de antes (cy + 270 / 2 + 110)
        public static float BarraY(float cy, float u) { return cy + 245 * u; }

        static double Ease(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }
        static int A(double a) { return Math.Max(0, Math.Min(255, (int)Math.Round(a))); }
        static Color Mix(Color a, Color b, float k)
        {
            return Color.FromArgb(A(a.R + (b.R - a.R) * k), A(a.G + (b.G - a.G) * k), A(a.B + (b.B - a.B) * k));
        }

        // La escena entera, centrada en el prisma (cx, cy). u = alto / 1080. t = segundos desde que salio la pantalla; pulse 0..1 (el
        // latido de SplashForm); lit 0..1 (el destello del final, FadeOutAndClose)
        public static void Escena(Graphics g, float W, float H, float cx, float cy, float u, double t, float pulse, float lit)
        {
            SmoothingMode sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Caja * u / 256f;
            float ox = cx - PA.X * s;
            PointF pe = new PointF(PA.X + (PB.X - PA.X) * TE, PA.Y + (PB.Y - PA.Y) * TE);
            float oy = cy - pe.Y * s; // (el haz entra en el prisma a la altura cy)
            PointF a = new PointF(ox + PA.X * s, oy + PA.Y * s), b = new PointF(ox + PB.X * s, oy + PB.Y * s), c = new PointF(ox + PC.X * s, oy + PC.Y * s);
            PointF e = new PointF(ox + pe.X * s, cy);
            PointF x = new PointF(ox + (PA.X + (PC.X - PA.X) * TE) * s, cy);
            PointF l = new PointF(-0.02f * W, cy + (e.X + 0.02f * W) * PendHaz);

            float prismK = (float)Ease((t - 0.05) / 0.45);
            float beamK = (float)Ease((t - 0.15) / 0.55);
            float fanK = (float)Ease((t - 0.62) / 0.22);
            float rayK = (float)Ease((t - 0.72) / 0.75);
            float wordK = (float)Ease((t - 1.0) / 0.5);

            // Halo del prisma (mas fuerte con el destello)
            Halo(g, new RectangleF(cx - 300 * u, cy - 190 * u, 600 * u, 360 * u), Color.FromArgb(150, 170, 230),
                          A(prismK * (18 + 14 * pulse) + 70 * lit));

            // ---- Los rayos largos (detras de todo): cuñas que se abren hasta el borde derecho y se apagan al final ----
            float full = (W * 1.02f - x.X) / s; // largo total (unidades)
            if (rayK > 0.001f)
            {
                if (rayK >= 0.999f)
                {
                    // Ya enteros y quietos: de una imagen guardada (a 4K, ~50 ms pintandolos cada vez)
                    Rectangle rr = Rectangle.FromLTRB((int)Math.Floor(x.X - 6 * u), (int)Math.Floor(cy - 280 * u), (int)Math.Ceiling(W), (int)Math.Ceiling(H));
                    Bitmap bm = Guardada(ref rayCache, ref rayKey, rr, u);
                    if (bm == null)
                    {
                        bm = Nueva(ref rayCache, ref rayKey, rr, u);
                        using (Graphics gc = Graphics.FromImage(bm))
                        {
                            gc.SmoothingMode = SmoothingMode.AntiAlias;
                            gc.TranslateTransform(-rr.X, -rr.Y);
                            Rays(gc, x, s, full, full, 0.55f, u, 0f);
                        }
                    }
                    g.DrawImageUnscaled(bm, rr.X, rr.Y);
                    if (lit > 0.001f) RaysCore(g, x, s, full, lit);
                }
                else Rays(g, x, s, full, full * rayK, 0.55f + 0.45f * lit, u, lit);
            }

            // ---- El haz: de la izquierda a E, con su halo; la cabeza brilla mientras llega ----
            if (beamK > 0.001f)
            {
                if (beamK >= 0.999f)
                {
                    Rectangle br = Rectangle.FromLTRB((int)Math.Floor(Math.Max(0, l.X) - 2), (int)Math.Floor(e.Y - 30 * u), (int)Math.Ceiling(e.X + 24 * u),
                                                      (int)Math.Ceiling(l.Y + 30 * u));
                    Bitmap bm = Guardada(ref beamCache, ref beamKey, br, u);
                    if (bm == null)
                    {
                        bm = Nueva(ref beamCache, ref beamKey, br, u);
                        using (Graphics gc = Graphics.FromImage(bm))
                        {
                            gc.SmoothingMode = SmoothingMode.AntiAlias;
                            gc.TranslateTransform(-br.X, -br.Y);
                            Beam(gc, l, e, e, 0.92f, u, 0f);
                        }
                    }
                    g.DrawImageUnscaled(bm, br.X, br.Y);
                    if (lit > 0.001f)
                        using (Pen p = new Pen(Color.FromArgb(A(160 * lit), 255, 255, 255), 9 * u * lit + 1))
                        using (LinearGradientBrush lb = new LinearGradientBrush(new PointF(l.X - 1, l.Y), new PointF(e.X + 1, e.Y),
                                   Color.FromArgb(0, 255, 255, 255), Color.FromArgb(A(160 * lit), 255, 255, 255)))
                        {
                            p.Brush = lb;
                            g.DrawLine(p, l, e);
                        }
                }
                else
                {
                    PointF head = new PointF(l.X + (e.X - l.X) * beamK, l.Y + (e.Y - l.Y) * beamK);
                    Beam(g, l, e, head, 0.92f * (1 + 0.6f * lit), u, lit);
                    if (beamK < 0.999f)
                    {
                        float ang = (float)(Math.Atan2(e.Y - l.Y, e.X - l.X) * 180 / Math.PI);
                        Blob(g, head, ang, 90 * u, 16 * u, Color.White, A(200 * (1 - beamK * 0.3f)));
                    }
                }
            }
            // ---- El prisma: cristal oscuro con un velo blanco, el abanico de luz dentro y el canto blanco fino ----
            if (prismK > 0.001f)
            {
                using (GraphicsPath tri = new GraphicsPath())
                {
                    tri.AddPolygon(new PointF[] { a, c, b });
                    using (LinearGradientBrush fb = new LinearGradientBrush(new PointF(0, a.Y - 1), new PointF(0, b.Y + 1),
                               Color.FromArgb(A((34 + 40 * lit) * prismK), 255, 255, 255), Color.FromArgb(A((6 + 30 * lit) * prismK), 255, 255, 255)))
                        g.FillPath(fb, tri);
                    if (fanK > 0.001f)
                    {
                        // El haz se abre dentro de E hacia la cara de salida, tiñendose (blanco -> rojo arriba / cian abajo)
                        float hw = Medio0 * 1.25f * s;
                        using (GraphicsPath fp = new GraphicsPath())
                        {
                            fp.AddPolygon(new PointF[] { e, new PointF(x.X, x.Y - hw), new PointF(x.X, x.Y + hw) });
                            using (LinearGradientBrush ib = new LinearGradientBrush(new PointF(e.X - 1, 0), new PointF(x.X + 1, 0),
                                       Color.FromArgb(A(215 * fanK), 255, 255, 255), Color.FromArgb(A(90 * fanK), 255, 255, 255)))
                                g.FillPath(ib, fp);
                        }
                        using (GraphicsPath up = new GraphicsPath())
                        {
                            up.AddPolygon(new PointF[] { new PointF(e.X + (x.X - e.X) * 0.45f, x.Y - hw * 0.45f), new PointF(x.X, x.Y - hw), new PointF(x.X, x.Y) });
                            using (SolidBrush rb = new SolidBrush(Color.FromArgb(A(70 * fanK), PrismaUi.Warn))) g.FillPath(rb, up);
                        }
                        using (GraphicsPath dn = new GraphicsPath())
                        {
                            dn.AddPolygon(new PointF[] { new PointF(e.X + (x.X - e.X) * 0.45f, x.Y + hw * 0.45f), new PointF(x.X, x.Y), new PointF(x.X, x.Y + hw) });
                            using (SolidBrush cb2 = new SolidBrush(Color.FromArgb(A(70 * fanK), PrismaUi.Accent))) g.FillPath(cb2, dn);
                        }
                    }
                    // Canto: un halo ancho y tenue y la linea fina
                    using (Pen hp = new Pen(Color.FromArgb(A((26 + 10 * pulse + 60 * lit) * prismK), 225, 235, 255), 11 * u))
                    {
                        hp.LineJoin = LineJoin.Round;
                        g.DrawPath(hp, tri);
                    }
                    using (Pen ep = new Pen(Color.FromArgb(A(245 * prismK), 255, 255, 255), 2.8f * u))
                    {
                        ep.LineJoin = LineJoin.Round;
                        g.DrawPath(ep, tri);
                    }
                }
            }

            // ---- Los pulsos: por el haz hasta el prisma, un destello dentro y dos por los rayos ----
            if (t >= PrimerPulso && rayK >= 0.999f && lit <= 0.001f)
            {
                double ph = ((t - PrimerPulso) % Periodo) / Periodo;
                float ang = (float)(Math.Atan2(e.Y - l.Y, e.X - l.X) * 180 / Math.PI);
                if (ph < 0.30)
                {
                    float k = (float)(ph / 0.30);
                    PointF p = new PointF(l.X + (e.X - l.X) * k, l.Y + (e.Y - l.Y) * k);
                    Blob(g, p, ang, 110 * u, 14 * u, Color.White, A(210 * Math.Min(1f, k * 2.5f)));
                }
                if (ph >= 0.27 && ph < 0.47)
                {
                    float k = (float)Math.Sin((ph - 0.27) / 0.20 * Math.PI);
                    Halo(g, new RectangleF(e.X - 40 * u, cy - 70 * u, (x.X - e.X) + 80 * u, 140 * u), Color.White, A(90 * k));
                }
                if (ph >= 0.36)
                {
                    float k = (float)((ph - 0.36) / 0.64);
                    float d = full * 0.80f * (float)(1 - Math.Pow(1 - k, 1.6)); // (sale rapido y frena un poco)
                    float fade = (float)Math.Min(1, (ph - 0.36) / 0.05) * (1 - k);
                    PointF pr = new PointF(x.X + d * s, x.Y - Medio0 * 0.55f * s + d * PendRojo * s);
                    PointF pc2 = new PointF(x.X + d * s, x.Y + Medio0 * 0.55f * s + d * PendCian * s);
                    float wr = (Medio0 + Abre * d) * s;
                    Blob(g, pr, (float)(Math.Atan(PendRojo) * 180 / Math.PI), 120 * u, Math.Max(12 * u, wr * 1.1f), Mix(PrismaUi.Warn, Color.White, 0.35f), A(170 * fade));
                    Blob(g, pc2, (float)(Math.Atan(PendCian) * 180 / Math.PI), 120 * u, Math.Max(12 * u, wr * 1.1f), Mix(PrismaUi.Accent, Color.White, 0.35f), A(170 * fade));
                }
            }

            // ---- Destello del final: el prisma se enciende ----
            if (lit > 0.001f)
            {
                // (un halo medio y el nucleo; a 4K uno grande mas costaba ~30 ms por imagen)
                PointF mid = new PointF((e.X + x.X) / 2, cy);
                Halo(g, new RectangleF(mid.X - 520 * u, mid.Y - 290 * u, 1040 * u, 580 * u), Color.FromArgb(225, 232, 255), A(70 * lit));
                PrismaUi.Glow(g, new RectangleF(mid.X - 150 * u, mid.Y - 100 * u, 300 * u, 200 * u), Color.White, A(210 * lit)); // (pequeña: la de degradado de verdad)
            }

            // ---- El nombre debajo ----
            if (wordK > 0.001f && PrismaUi.ShownName.Length > 0)
            {
                float rise = (1 - wordK) * 10 * u;
                using (Font f = PrismaUi.TitleFont(54 * u))
                {
                    SizeF sz = g.MeasureString(PrismaUi.ShownName, f, 100000, PrismaUi.Typo);
                    System.Drawing.Text.TextRenderingHint th = g.TextRenderingHint;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(A(255 * wordK), PrismaUi.TitleColor)))
                        g.DrawString(PrismaUi.ShownName, f, tb, cx - sz.Width / 2, cy + 128 * u + rise, PrismaUi.Typo);
                    g.TextRenderingHint = th;
                }
            }
            g.SmoothingMode = sm;
        }

        // Lo que ya no se mueve (los rayos enteros y el haz ya llegado), guardado: a 4K los dos costaban ~45 ms en cada imagen.
        // Uno por hilo (la pantalla de carga pinta en el de la interfaz); Liberar al cerrarla
        [ThreadStatic] static Bitmap rayCache, beamCache;
        [ThreadStatic] static string rayKey, beamKey;

        static string Clave(Rectangle r, float u) { return r.X + "," + r.Y + "," + r.Width + "," + r.Height + "," + u.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }

        static Bitmap Guardada(ref Bitmap cache, ref string key, Rectangle r, float u)
        {
            return cache != null && key == Clave(r, u) ? cache : null;
        }

        static Bitmap Nueva(ref Bitmap cache, ref string key, Rectangle r, float u)
        {
            if (cache != null) cache.Dispose();
            cache = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            key = Clave(r, u);
            return cache;
        }

        public static void Liberar()
        {
            if (rayCache != null) { rayCache.Dispose(); rayCache = null; }
            if (beamCache != null) { beamCache.Dispose(); beamCache = null; }
            rayKey = beamKey = null;
        }

        // El haz de l hacia e (pintado hasta head): un halo en capas y la linea fina, con el degradado fijo de l a e
        static void Beam(Graphics g, PointF l, PointF e, PointF head, float bk, float u, float lit)
        {
            const int n = 9;
            for (int i = n; i >= 1; i--)
                using (Pen p = new Pen(Color.White, (3 + 30 * i / (float)n) * u * (1 + 0.5f * lit)))
                using (LinearGradientBrush lb = new LinearGradientBrush(new PointF(l.X - 1, l.Y), new PointF(e.X + 1, e.Y),
                           Color.FromArgb(0, 255, 255, 255), Color.FromArgb(A(255 * 0.30f / n * bk), 235, 242, 255)))
                {
                    p.Brush = lb;
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawLine(p, l, head);
                }
            float c = Math.Min(1f, bk);
            using (Pen p = new Pen(Color.White, 2.6f * u))
            using (LinearGradientBrush lb = new LinearGradientBrush(new PointF(l.X - 1, l.Y), new PointF(e.X + 1, e.Y),
                       Color.FromArgb(0, 255, 255, 255), Color.FromArgb(A(250 * c), 255, 255, 255)))
            {
                ColorBlend cb = new ColorBlend(3);
                cb.Colors = new Color[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(A(170 * c), 255, 255, 255), Color.FromArgb(A(250 * c), 255, 255, 255) };
                cb.Positions = new float[] { 0f, 0.55f, 1f };
                lb.InterpolationColors = cb;
                p.Brush = lb;
                p.EndCap = LineCap.Round;
                g.DrawLine(p, l, head);
            }
        }
        // Los dos rayos (como en el logotipo: el cian sale un poco mas abajo y el rojo, encima, un poco mas arriba)
        static void Rays(Graphics g, PointF x, float s, float full, float len, float k, float u, float lit)
        {
            Color redHot = Mix(PrismaUi.Warn, Color.White, 0.3f), cyanHot = Mix(PrismaUi.Accent, Color.White, 0.3f);
            Ray(g, x, s, Medio0 * 0.55f, PendCian, full, len, PrismaUi.Accent, cyanHot, k, u, lit);
            Ray(g, x, s, -Medio0 * 0.55f, PendRojo, full, len, PrismaUi.Warn, redHot, k, u, lit);
        }

        // El destello del final sobre los rayos guardados: su nucleo otra vez, mas fuerte
        static void RaysCore(Graphics g, PointF x, float s, float full, float lit)
        {
            for (int i = 0; i < 2; i++)
            {
                float off = i == 0 ? Medio0 * 0.55f : -Medio0 * 0.55f, slope = i == 0 ? PendCian : PendRojo;
                Color col = i == 0 ? PrismaUi.Accent : PrismaUi.Warn;
                float x0 = x.X, y0 = x.Y + off * s, x1 = x0 + full * s, y1 = y0 + full * slope * s;
                float h0 = Medio0 * s * 0.7f, h1 = (Medio0 + Abre * full) * s * 0.7f;
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddPolygon(new PointF[] { new PointF(x0, y0 - h0), new PointF(x1, y1 - h1), new PointF(x1, y1 + h1), new PointF(x0, y0 + h0) });
                    using (LinearGradientBrush b = new LinearGradientBrush(new PointF(x0 - 1, 0), new PointF(x1 + 1, 0),
                               Color.FromArgb(A(200 * lit), Mix(col, Color.White, 0.5f)), Color.FromArgb(0, col)))
                        g.FillPath(b, p);
                }
            }
        }
        // Un rayo largo desde x (desplazado 'off' unidades en vertical), con pendiente 'slope': cuña de 'len' unidades de las 'full'
        // del todo, con el degradado fijo en el largo total (caliente al salir, su color, nada al final) y un halo mas ancho y tenue
        static void Ray(Graphics g, PointF x, float s, float off, float slope, float full, float len, Color col, Color hot, float k, float u, float lit)
        {
            if (len < 1) return;
            float x0 = x.X, y0 = x.Y + off * s;
            float x1 = x0 + len * s, y1 = y0 + len * slope * s;
            float h0 = Medio0 * s, h1 = (Medio0 + Abre * len) * s * (1 + 0.25f * lit);
            // El degradado a lo largo: el del largo total (caliente al salir, su color, nada al final) y, mientras crece, la punta que
            // se apaga en su ultimo 10 % (sin el corte recto). Posiciones sobre el largo de ahora
            float r = len / full;
            List<float> pos = new List<float>();
            List<float> amp = new List<float>();
            float[] fp = { 0f, 0.12f, 0.5f, 1f }, fa = { 1f, 0.86f, 0.35f, 0f };
            float tip = r < 0.995f ? 0.90f : 1f;
            for (int i = 0; i < fp.Length; i++)
            {
                float q = fp[i] / r;
                if (q >= tip) break;
                pos.Add(q); amp.Add(fa[i]);
            }
            if (tip < 1f)
            {
                pos.Add(tip); amp.Add(Prof(fp, fa, tip * r));
                pos.Add(1f); amp.Add(0f);
            }
            else if (pos[pos.Count - 1] < 1f) { pos.Add(1f); amp.Add(Prof(fp, fa, r)); }
            // Primero el halo, en capas cada vez mas anchas y tenues (bordes suaves), luego el rayo y su nucleo
            float[] mult = { 2.5f, 2.05f, 1.65f, 1.3f, 1.0f, 0.55f };
            float[] alph = { 0.030f, 0.036f, 0.045f, 0.060f, 0.42f, 0.22f };
            for (int pass = 0; pass < mult.Length; pass++)
            {
                float m = mult[pass], al = alph[pass] * k;
                PointF[] w = { new PointF(x0, y0 - h0 * Math.Min(1f, m)), new PointF(x1, y1 - h1 * m), new PointF(x1, y1 + h1 * m), new PointF(x0, y0 + h0 * Math.Min(1f, m)) };
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddPolygon(w);
                    using (LinearGradientBrush b = new LinearGradientBrush(new PointF(x0 - 1, 0), new PointF(x1 + 1, 0), hot, col))
                    {
                        ColorBlend cb = new ColorBlend(pos.Count);
                        Color[] cs = new Color[pos.Count];
                        for (int i = 0; i < pos.Count; i++) cs[i] = Color.FromArgb(A(255 * al * amp[i]), i == 0 ? hot : col);
                        cb.Colors = cs;
                        cb.Positions = pos.ToArray();
                        b.InterpolationColors = cb;
                        g.FillPath(b, p);
                    }
                }
            }
            // La cabeza del rayo mientras crece
            if (len < full * 0.995f)
                Blob(g, new PointF(x1, y1), (float)(Math.Atan(slope) * 180 / Math.PI), 70 * u, Math.Max(10 * u, h1 * 1.4f), hot, A(150 * k));
        }

        // El perfil a lo largo del rayo (tramos rectos entre los puntos fp / fa) en la fraccion f del largo total
        static float Prof(float[] fp, float[] fa, float f)
        {
            if (f <= fp[0]) return fa[0];
            for (int i = 1; i < fp.Length; i++)
                if (f <= fp[i]) return fa[i - 1] + (fa[i] - fa[i - 1]) * (f - fp[i - 1]) / (fp[i] - fp[i - 1]);
            return fa[fa.Length - 1];
        }

        // Mancha de luz radial grande, en elipses concentricas lisas (la de PrismaUi.Glow, con PathGradientBrush, costaba 25-30 ms a 4K
        // con 1200x720 pixeles; asi ~1-2). Con alfas tan bajos los escalones no se ven
        static void Halo(Graphics g, RectangleF r, Color c, int alpha)
        {
            if (alpha <= 0 || r.Width < 1 || r.Height < 1) return;
            int n = Math.Max(6, Math.Min(30, alpha / 3)); // (escalones de 3 de alfa como mucho)
            if (r.Width * r.Height > 2000000) n = Math.Min(n, 10); // (a 4K una elipse de 5 MP son ~12 ms)
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            // Cada anillo suma lo que sube el perfil (lineal: del borde 0 al centro alpha), compuesto con "encima de"
            double prev = 0;
            for (int i = 1; i <= n; i++)
            {
                double target = alpha * (double)i / n;                // lo que hay que llevar dentro de este anillo
                double a = (target - prev) / (255.0 - prev) * 255.0; // (para que la mezcla "encima" llegue justo a target)
                prev = target;
                float k = 1f - (i - 1) / (float)n;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(A(a), c)))
                    g.FillEllipse(b, cx - r.Width * k / 2, cy - r.Height * k / 2, r.Width * k, r.Height * k);
            }
        }
        // Una mancha de luz alargada en p, girada 'deg' grados (largo x ancho)
        static void Blob(Graphics g, PointF p, float deg, float len, float wid, Color c, int alpha)
        {
            if (alpha <= 2) return;
            GraphicsState st = g.Save();
            g.TranslateTransform(p.X, p.Y);
            g.RotateTransform(deg);
            PrismaUi.Glow(g, new RectangleF(-len / 2, -wid / 2, len, wid), c, alpha);
            PrismaUi.Glow(g, new RectangleF(-len / 6, -wid / 6, len / 3, wid / 3), Color.White, Math.Min(255, alpha + 40));
            g.Restore(st);
        }
    }
}
