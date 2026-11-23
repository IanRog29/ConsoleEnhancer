// PrismaPreview.cs - Vistas previas del aspecto de Prisma, sin mostrar ninguna ventana ni tocar la capturadora ni el motor:
//   Switch2Magpie.exe --ui-preview=<carpeta>[;<imagen de un juego>]
// Pinta fuera de pantalla (DrawToBitmap) la pantalla de carga, el telon (oscuro y con la ultima imagen atenuada) y los avisos,
// y los guarda en PNG en la carpeta.
// (15-11: --ui-preview=telon-vivo probaba el telon en vivo con un mpv de prueba; 23-11, 1.2.4 SINMPV: fuera.) C# 5
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ConsoleEnhancer
{
    static class PrismaPreview
    {
        public static int Run(string spec)
        {
            if (spec.StartsWith("telon-vivo", StringComparison.OrdinalIgnoreCase)) { Log.Write("--ui-preview=telon-vivo ya no existe (sin mpv)"); return 1; }
            string[] parts = spec.Split(';');
            string dir = parts[0];
            string imgPath = parts.Length > 1 ? parts[1] : Path.Combine(Paths.Datos, @"games\banco_oot.png");
            Directory.CreateDirectory(dir);
            Cursor.Show();
            using (Bitmap frame = LoadFrame(imgPath))
            {
                Splash(Path.Combine(dir, "carga_1.png"), 0.95, 0.22, null, null, 1920, 1080);
                Splash(Path.Combine(dir, "carga_2.png"), 2.6, 0.62, null, null, 1920, 1080);
                Splash(Path.Combine(dir, "carga_enciende.png"), 4.0, 0.35, Consolas.TextoEnciende, "Pulsa Esc para cancelar", 1920, 1080);
                Curtain(Path.Combine(dir, "telon_oscuro.png"), "DLSS 5 activado", "Nivel 3 · 2 pasadas", null, false, 1920, 1080);
                Curtain(Path.Combine(dir, "telon_gpu.png"), "La tarjeta gráfica se ha reiniciado", "Recuperando la imagen…", null, true, 1920, 1080);
                Curtain(Path.Combine(dir, "telon_imagen.png"), "Generación de fotogramas activada", "Smooth Motion · imágenes intercaladas entre las reales", frame, false, 1920, 1080);
                Curtain(Path.Combine(dir, "telon_imagen_4k.png"), "Captura 1080p", "Un momento…", frame, false, 3840, 2160);
                Notices(Path.Combine(dir, "avisos.png"), frame, 1920, 1080);
                Menu(Path.Combine(dir, "menu.png"));
            }
            return 0;
        }

        // La imagen de un juego (o, si no hay, una de prueba con color y detalle)
        static Bitmap LoadFrame(string path)
        {
            try
            {
                if (File.Exists(path))
                    using (Bitmap b = new Bitmap(path))
                    {
                        Bitmap c = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppRgb);
                        using (Graphics g = Graphics.FromImage(c)) g.DrawImage(b, 0, 0, b.Width, b.Height);
                        return c;
                    }
            }
            catch { }
            Bitmap t = new Bitmap(1920, 1080, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(t))
            {
                using (LinearGradientBrush sky = new LinearGradientBrush(new Rectangle(0, 0, 1920, 700), Color.FromArgb(90, 150, 220), Color.FromArgb(230, 210, 170), 90f))
                    g.FillRectangle(sky, 0, 0, 1920, 700);
                using (SolidBrush grass = new SolidBrush(Color.FromArgb(70, 120, 60))) g.FillRectangle(grass, 0, 700, 1920, 380);
                Random r = new Random(3);
                for (int i = 0; i < 60; i++)
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(r.Next(40, 200), r.Next(60, 200), r.Next(40, 160))))
                        g.FillEllipse(b, r.Next(0, 1920), r.Next(500, 1000), r.Next(40, 220), r.Next(30, 120));
            }
            return t;
        }

        static void Save(Control f, string path, int w, int h)
        {
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void Splash(string path, double t, double progress, string status, string hint, int w, int h)
        {
            using (SplashForm f = new SplashForm())
            {
                f.Bounds = new Rectangle(0, 0, w, h);
                if (status != null) { f.SetStatus(status); f.SetHint(hint); }
                f.PreviewAt(t, progress);
                Save(f, path, w, h);
            }
        }

        static void Curtain(string path, string title, string subtitle, Bitmap frame, bool warn, int w, int h)
        {
            Color accent = warn ? PrismaUi.Warn : PrismaUi.Accent;
            Bitmap bg = frame != null ? CoverForm.ComposeDimmed(frame, new Size(w, h), title, subtitle, accent) : null;
            using (CoverForm f = new CoverForm(title, subtitle, accent, false, bg))
            {
                f.Bounds = new Rectangle(0, 0, w, h);
                Save(f, path, w, h);
            }
        }

        // Los avisos de arriba (los de mpv y los del motor son esta misma pastilla) sobre la imagen del juego
        static void Notices(string path, Bitmap frame, int w, int h)
        {
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(frame, new Rectangle(0, 0, w, h));
                float u = h / 1080f;
                string[] texts = { "DLSS 5 · Nivel 3 de 4 · ya activo", "Captura 1440p", "Zelda · 1440p · 60 → 120 fps (Smooth Motion) · DLSS 5 nivel 3 · 2 pasadas" };
                float y = 0.035f * h + 18 * u * 0.7f;
                foreach (string s in texts)
                {
                    PrismaUi.Pill p = PrismaUi.MeasurePill(g, s, u);
                    PrismaUi.DrawPill(g, p, new RectangleF((w - p.W) / 2, y, p.W, p.H), PrismaUi.Accent, 1f);
                    y += p.H + 40 * u;
                }
                PrismaUi.Pill pw = PrismaUi.MeasurePill(g, "Magpie no está escalando · mira switch2.log", u);
                PrismaUi.DrawPill(g, pw, new RectangleF((w - pw.W) / 2, y, pw.W, pw.H), PrismaUi.Warn, 1f);
                y += pw.H + 40 * u;
                // Medio fundido (como a mitad de la entrada)
                PrismaUi.Pill pf = PrismaUi.MeasurePill(g, "Modo 60 fps · ya activo", u);
                PrismaUi.DrawPill(g, pf, new RectangleF((w - pf.W) / 2, y + 7 * u, pf.W, pf.H), PrismaUi.Accent, 0.5f);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        // (23-11, 1.2.4 SINMPV: fuera la prueba del telon en vivo -LiveCurtainTest, --ui-preview=telon-vivo-: necesitaba un mpv de prueba y
        // era el telon de cuando solo se reabria Magpie, que ya no esta)

        static void Menu(string path)
        {
            try
            {
                using (ContextMenuStrip menu = new ContextMenuStrip())
                {
                    menu.Items.Add("Escalar ahora (atajo de Magpie)");
                    ToolStripMenuItem d = new ToolStripMenuItem("Detección de apagado: activada");
                    d.Checked = true;
                    menu.Items.Add(d);
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Abrir config");
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Salir");
                    PrismaUi.StyleMenu(menu);
                    menu.CreateControl();
                    menu.PerformLayout();
                    Size s = menu.GetPreferredSize(Size.Empty);
                    menu.Size = s;
                    using (Bitmap bmp = new Bitmap(Math.Max(1, s.Width), Math.Max(1, s.Height)))
                    {
                        menu.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                        bmp.Save(path, ImageFormat.Png);
                    }
                }
            }
            catch (Exception ex) { File.WriteAllText(path + ".txt", ex.ToString()); }
        }
    }
}
