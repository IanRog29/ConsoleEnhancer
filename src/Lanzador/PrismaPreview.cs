// PrismaPreview.cs - Vistas previas del aspecto de Prisma, sin mostrar ninguna ventana ni tocar la capturadora, mpv o Magpie:
//   Switch2Magpie.exe --ui-preview=<carpeta>[;<imagen de un juego>]
// Pinta fuera de pantalla (DrawToBitmap) la pantalla de carga, el telon (oscuro y con la ultima imagen atenuada) y los avisos,
// y los guarda en PNG en la carpeta.
// 15-11: --ui-preview=telon-vivo[;<carpeta>] prueba el telon en vivo EN PANTALLA (sin capturadora ni Magpie): ver LiveCurtainTest. C# 5
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
            if (spec.StartsWith("telon-vivo", StringComparison.OrdinalIgnoreCase)) return LiveCurtainTest(spec);
            string[] parts = spec.Split(';');
            string dir = parts[0];
            string imgPath = parts.Length > 1 ? parts[1] : Path.Combine(Paths.Datos, @"juegos\banco_oot.png");
            Directory.CreateDirectory(dir);
            Cursor.Show();
            using (Bitmap frame = LoadFrame(imgPath))
            {
                Splash(Path.Combine(dir, "carga_1.png"), 0.95, 0.22, null, null, 1920, 1080);
                Splash(Path.Combine(dir, "carga_2.png"), 2.6, 0.62, null, null, 1920, 1080);
                Splash(Path.Combine(dir, "carga_enciende.png"), 4.0, 0.35, Consolas.TextoEnciende, "Pulsa Esc para cancelar", 1920, 1080);
                Curtain(Path.Combine(dir, "telon_oscuro.png"), "DLSS 5 activado", "Nivel 3 · 2 pasadas", null, false, 1920, 1080);
                Curtain(Path.Combine(dir, "telon_gpu.png"), "La tarjeta gráfica se ha reiniciado", "Recuperando la imagen…", null, true, 1920, 1080);
                Curtain(Path.Combine(dir, "telon_imagen.png"), "Generación de fotogramas activada", "DLSS FG x2 · imágenes intercaladas entre las reales", frame, false, 1920, 1080);
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
                string[] texts = { "DLSS 5 · Nivel 3 de 4 · ya activo", "Captura 1440p", "Zelda · 1440p · 60 → 120 fps (DLSS FG) · DLSS 5 nivel 3 · 2 pasadas" };
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

        // ---- 15-11: el telon en vivo, en pantalla, sin capturadora ni Magpie ----
        // Un mpv de prueba (--no-config, lavfi testsrc2 a 1536x864 en 0,0: como la ventana de mpv de verdad, pequeña en una esquina),
        // el telon en vivo de verdad (CoverForm con live, src\TelonVivo.cs) encima a toda la pantalla, sin activarse (como RestartLs),
        // con su pastilla; en cada fase una copia de la pantalla con scripts\captura.exe grab (PNG a pantalla completa y la linea de
        // <carpeta>\parpadeo.txt: diferencia entre copias seguidas y brillo). Lo que pasa, en <carpeta>\telon_vivo.txt.
        // Con la tele en espera DWM compone 1 vez por segundo: cada copia espera >1,5 s tras cada cambio, y los fundidos se prueban
        // largos (4 s) con la copia a mitad. No toca juegos\ ni el log de la app. Devuelve los fallos
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        static StreamWriter rep;

        static void R(string s)
        {
            if (rep == null) return;
            rep.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s);
            rep.Flush();
        }

        static void Pump(int ms)
        {
            Stopwatch s = Stopwatch.StartNew();
            while (s.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        // Si un banco (Magpie) arranca a mitad, la prueba se para: no tapar ni robarle la grafica a otro
        static void CheckFree()
        {
            if (Process.GetProcessesByName("Magpie").Length > 0 || Process.GetProcessesByName("Switch2Magpie_banco").Length > 0)
                throw new InvalidOperationException("ABORTADA: hay un banco (Magpie) en marcha");
        }

        // La imagen de la copia llena la pantalla (sin bandas negras): limites de lo que no es negro
        static bool Full(string dir, string name)
        {
            try
            {
                using (Bitmap b = new Bitmap(Path.Combine(dir, name + ".png")))
                {
                    int w = b.Width, h = b.Height;
                    BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                    byte[] px = new byte[d.Stride * h];
                    Marshal.Copy(d.Scan0, px, 0, px.Length);
                    b.UnlockBits(d);
                    int stride = d.Stride;
                    Func<int, int, bool> lit = (x, y) => { int i = y * stride + x * 4; return px[i] + px[i + 1] + px[i + 2] > 120; };
                    int L = -1, Rr = -1, T = -1, Bt = -1;
                    for (int x = 0; x < w && L < 0; x++) for (int y = 0; y < h; y += 3) if (lit(x, y)) { L = x; break; }
                    for (int x = w - 1; x >= 0 && Rr < 0; x--) for (int y = 0; y < h; y += 3) if (lit(x, y)) { Rr = x; break; }
                    for (int y = 0; y < h && T < 0; y++) for (int x = 0; x < w; x += 3) if (lit(x, y)) { T = y; break; }
                    for (int y = h - 1; y >= 0 && Bt < 0; y--) for (int x = 0; x < w; x += 3) if (lit(x, y)) { Bt = y; break; }
                    bool full = L <= 2 && T <= 2 && Rr >= w - 3 && Bt >= h - 3;
                    R("  " + name + ": imagen en x " + L + ".." + Rr + ", y " + T + ".." + Bt + (full ? " (pantalla entera)" : " FALLO: no llena la pantalla"));
                    return full;
                }
            }
            catch (Exception ex) { R("  " + name + ": no se pudo mirar (" + ex.Message + ")"); return false; }
        }

        static void Grab(string dir, string label, int n, int ms)
        {
            CheckFree();
            string exe = Path.Combine(Paths.AppDir, @"scripts\captura.exe");
            ProcessStartInfo psi = new ProcessStartInfo(exe, "grab \"" + dir + "\" " + label + " " + n + " " + ms + " 0 0 todas");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            using (Process g = Process.Start(psi))
            {
                System.Threading.Tasks.Task<string> outp = g.StandardOutput.ReadToEndAsync(); // (sin parar el bucle de mensajes: el telon se anima)
                while (!g.HasExited) { Application.DoEvents(); Thread.Sleep(10); }
                outp.Wait(3000);
                R("copia " + label + " (captura.exe " + g.ExitCode + "): " + (outp.IsCompleted ? outp.Result.Trim() : "?"));
            }
        }

        static int LiveCurtainTest(string spec)
        {
            string[] parts = spec.Split(';');
            string dir = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : Path.Combine(Path.GetTempPath(), "prisma_telon_vivo");
            string mpvExe = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : Path.Combine(Paths.AppDir, @"mpv\mpv.exe");
            Directory.CreateDirectory(dir);
            int fails = 0;
            Process mpv = null;
            using (rep = new StreamWriter(Path.Combine(dir, "telon_vivo.txt"), false, new UTF8Encoding(false)))
            {
                try
                {
                    Rectangle scr = Screen.PrimaryScreen.Bounds;
                    IntPtr fg0 = GetForegroundWindow();
                    string[] wh = (parts.Length > 3 && parts[3].Length > 0 ? parts[3] : "1536x864").Split('x');
                    int mw = int.Parse(wh[0]), mh = int.Parse(wh[1]);
                    R("Prueba del telon en vivo: pantalla " + scr.Width + "x" + scr.Height + "; mpv de prueba " + mw + "x" + mh + " en 0,0");
                    CheckFree();
                    ProcessStartInfo psi = new ProcessStartInfo(mpvExe,
                        "--no-config --focus-on=never --no-border --geometry=" + mw + "x" + mh + "+0+0 --vo=gpu-next --gpu-api=d3d11 --no-osc " +
                        "--no-input-default-bindings --loop-file=inf --title=prisma-prueba av://lavfi:testsrc2=size=" + mw + "x" + mh + ":rate=60");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    mpv = Process.Start(psi);
                    IntPtr src = IntPtr.Zero;
                    Stopwatch ws = Stopwatch.StartNew();
                    while (src == IntPtr.Zero && ws.ElapsedMilliseconds < 10000) { Pump(100); mpv.Refresh(); src = mpv.MainWindowHandle; }
                    if (src == IntPtr.Zero) { R("FALLO: mpv sin ventana"); return 1; }
                    R("mpv " + mpv.Id + " con ventana en " + ws.ElapsedMilliseconds + " ms");
                    Pump(1500);

                    // 1) Entrada (fundido largo: la copia a mitad) y en directo, con el mpv recien abierto (sin cambiarle el tamaño: como
                    // el de la app). La imagen tiene que llenar la pantalla entera (Full)
                    CoverForm c = new CoverForm("DLSS 5 · Nivel 3 de 4", "2 pasadas", PrismaUi.Accent, false, null, 4.0, src);
                    c.Note = R;
                    c.TopPillText = "DLSS 5 · Nivel 3 de 4 · 2 pasadas";
                    IntPtr ch = c.Handle;
                    R("Telon 1: miniatura " + (c.LiveOk ? "registrada" : "FALLO (" + c.LiveLost + ")") + "; telon " + c.ClientSize.Width + "x" + c.ClientSize.Height + " al crearlo");
                    if (!c.LiveOk) fails++;
                    c.Show();
                    c.RefreshLive(); // (como TrayApp.ShowLiveCurtain: el destino con el tamaño ya de verdad)
                    R("Telon 1 mostrado: " + c.ClientSize.Width + "x" + c.ClientSize.Height);
                    Pump(2000); Grab(dir, "1_entrada_mitad", 1, 0);
                    Pump(3500); Grab(dir, "2_vivo", 2, 1500);
                    if (!Full(dir, "2_vivo_0")) fails++;
                    // 2) mpv cambia de tamaño debajo: estirada (mpv pone bandas, como con el doble clic en su borde), mas pequeña y de vuelta
                    int sh = Math.Min(scr.Height, mh * 3 / 2), pw = mw * 5 / 6 / 2 * 2, ph = mh * 5 / 6 / 2 * 2;
                    SetWindowPos(src, IntPtr.Zero, 0, 0, mw, sh, 0x4 | 0x10);
                    Pump(2000); Grab(dir, "3_mpv_estirada", 1, 0);
                    if (!Full(dir, "3_mpv_estirada_0")) fails++;
                    SetWindowPos(src, IntPtr.Zero, 0, 0, pw, ph, 0x4 | 0x10);
                    Pump(2000); Grab(dir, "4_mpv_pequena", 1, 0);
                    if (!Full(dir, "4_mpv_pequena_0")) fails++;
                    SetWindowPos(src, IntPtr.Zero, 0, 0, mw, mh, 0x4 | 0x10);
                    Pump(1500);
                    if (c.LiveLost != null) { R("FALLO: se perdio la imagen en directo (" + c.LiveLost + ")"); fails++; }
                    // 3) Salida (fundido largo: la copia a mitad) y cerrado del todo
                    c.FadeOut(4.0);
                    Pump(2000); Grab(dir, "5_salida_mitad", 1, 0);
                    Pump(3500);
                    R("Telon 1 cerrado: " + c.IsDisposed);
                    if (!c.IsDisposed) fails++;
                    Grab(dir, "6_cerrado", 1, 0);

                    // 4) mpv se cierra con el telon puesto: el oscuro, con la pastilla en el centro y la linea
                    CoverForm d = new CoverForm("Generación de fotogramas activada", "DLSS FG x2 · imágenes intercaladas entre las reales",
                                                PrismaUi.Accent, false, null, 0.15, src);
                    d.Note = R;
                    d.TopPillText = "Generación de fotogramas activada · DLSS FG x2 · imágenes intercaladas entre las reales";
                    ch = d.Handle;
                    d.Show();
                    d.ShowNow();     // (como TrayApp: al momento, ya opaco)
                    d.RefreshLive();
                    Pump(2000); Grab(dir, "7_vivo_otro", 1, 0);
                    if (!Full(dir, "7_vivo_otro_0")) fails++;
                    mpv.Kill();
                    mpv.WaitForExit(5000);
                    Pump(2000);
                    R("Telon 2 tras cerrar mpv: en directo " + d.LiveOk + ", perdido: " + (d.LiveLost ?? "no") + " a los " + d.LiveLostMs + " ms");
                    if (d.LiveOk || d.LiveLost == null) fails++;
                    Grab(dir, "8_sin_mpv", 1, 0);
                    d.FadeOut(0.25);
                    Pump(800);

                    // 5) Sin la ventana de mpv al ponerlo: no se registra (TrayApp lo desecha sin mostrarlo y pone el de siempre)
                    using (CoverForm e = new CoverForm("x", "y", PrismaUi.Accent, false, null, 0.1, src))
                    {
                        ch = e.Handle;
                        R("Telon 3 con la ventana de mpv ya cerrada: en directo " + e.LiveOk + " (" + (e.LiveLost ?? "?") + ")");
                        if (e.LiveOk) fails++;
                    }
                    // 6) El telon al momento cuando el en vivo no se puede (TrayApp.ShowFastCurtain): negro, ya opaco, con la pastilla arriba
                    Stopwatch fs = Stopwatch.StartNew();
                    CoverForm k = new CoverForm(null, null, PrismaUi.Accent, false, null, 0);
                    k.Note = R;
                    k.TopPillText = "Generación de fotogramas desactivada · Solo las imágenes reales de la consola";
                    k.Show();
                    k.ShowNow();
                    R("Telon negro al momento: puesto en " + fs.ElapsedMilliseconds + " ms");
                    Pump(2000); Grab(dir, "9_negro_rapido", 1, 0);
                    k.FadeOut(0.25);
                    Pump(800);
                    R("Primer plano al empezar " + fg0 + ", al acabar " + GetForegroundWindow());
                }
                catch (Exception ex) { R("FALLO: " + ex); fails++; }
                finally
                {
                    if (mpv != null) try { if (!mpv.HasExited) mpv.Kill(); } catch { }
                    R("Fin: " + fails + " fallos");
                }
            }
            rep = null;
            return fails;
        }

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
