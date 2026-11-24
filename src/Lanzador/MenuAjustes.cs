// MenuAjustes.cs - menu de ajustes en pantalla con el boton central del mando (21-11-2026, 1.0.11)
//
// El usuario: "añade un menu que se vean todas las configuraciones que se pueden activar en la app al presionar el boton home de los
// mandos (el del centro), que se puedan aplicar ahi y darle a aplicar y se aplique todo a la vez, y que sea una ui moderna".
//
// Boton central (Xbox / PS / HOME; PadBtn.Guide) con la imagen de la consola delante = abre / cierra el panel. Teclado: Ctrl+O (y la
// bandeja). Dentro:
//   cruceta (o stick izquierdo) arriba / abajo = fila; izquierda / derecha = valor; A (el de abajo) = siguiente valor, o el boton del pie;
//   B (el de la derecha) o el central = cerrar sin cambiar nada. Teclado: flechas, Intro / Espacio, Esc / Retroceso.
// Nada cambia hasta "Aplicar": entonces se pone todo a la vez como un perfil de juego (EnterGame: applying = true, los ajustes solo se
// guardan) y la imagen se reabre UNA vez (cambio de captura > de modo > RestartLs). Lo cambiado se marca con un punto cian.
// La ventana es propia (capa con alfa por pixel, siempre encima, sin activarse ni coger el foco: la generacion de NVIDIA se apaga sin el)
// y con los pixeles reales de la pantalla de la imagen (contexto PER_MONITOR_AWARE_V2 en el hilo al crearla: sin el, Windows la estira).
// Mientras esta abierta, el mando no hace sus acciones de siempre (todo va al panel). OJO: el boton Xbox tambien abre la Game Bar de
// Windows si esta encendida "Abrir la Game Bar con este boton del mando" (Configuracion > Juegos > Game Bar).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    // Una fila del panel: un ajuste con sus valores
    sealed class FilaAjuste
    {
        public string Id, Nombre, Nota = "";
        public string Ayuda;   // (1.1.2) una segunda linea debajo, a todo lo ancho (la fila ocupa dos)
        public List<string> Vals = new List<string>(), Txts = new List<string>();
        public int Sel, Orig;
        public bool Visible = true, Activa = true, Carrusel;
        // (1.2.2, MULTI) valores que se ven pero no se eligen (gris, con candado): el motivo de cada uno ("" = se puede). Visible puede
        // cambiar en marcha: una fila escondida no cuenta ni se aplica. (1.2.4 ronda 6, QUITAFG: la fila del multiplicador ya no esta)
        public List<string> Bloq = new List<string>();
        public bool Bloqueado(int i) { return i >= 0 && i < Bloq.Count && !string.IsNullOrEmpty(Bloq[i]); }
        public bool Cambiada { get { return Visible && Activa && Sel != Orig; } }
        public string Val { get { return Sel >= 0 && Sel < Vals.Count ? Vals[Sel] : ""; } }
        public void Poner(List<string> vals, List<string> txts, string elegido, string original)
        {
            Vals = vals; Txts = txts;
            Sel = Math.Max(0, vals.IndexOf(elegido));
            Orig = vals.IndexOf(original);   // (-1: lo de ahora no esta en la lista: cualquiera cuenta como cambio)
        }
    }

    // ---------------------------------------------------------------- el modelo de DLSS elegido (1.0.12)
    // El usuario: "añade a las opciones de escalador diferentes dlss". "" = el de siempre (el de la tabla del modo / motor_modelo_*); si no,
    // la letra (E, J, K, L, M) en el motor propio (preset=) y en Magpie (renderPreset: 5, 10, 11, 12, 13). Para todos los juegos, en
    // data\games\dlss_model_choice.txt
    partial class Config
    {
        static string dlssPick;
        static readonly object dlssPickSync = new object();
        static string DlssPickFile { get { return Path.Combine(Paths.Datos, @"games\dlss_model_choice.txt"); } }
        public static readonly string[] DlssPicks = { "M", "L", "K", "J", "E" };

        public static string DlssPick
        {
            get
            {
                string p = dlssPick;
                if (p != null) return p;
                lock (dlssPickSync)
                {
                    if (dlssPick == null)
                    {
                        string v = "";
                        try { if (File.Exists(DlssPickFile)) v = File.ReadAllText(DlssPickFile).Trim().ToUpperInvariant(); } catch { }
                        dlssPick = Array.IndexOf(DlssPicks, v) >= 0 ? v : "";
                        if (dlssPick.Length > 0) Log.Write("DLSS: modelo elegido en el menu de ajustes: " + EngineModelName(dlssPick));
                    }
                    return dlssPick;
                }
            }
            set
            {
                string v = (value ?? "").Trim().ToUpperInvariant();
                if (Array.IndexOf(DlssPicks, v) < 0) v = "";
                lock (dlssPickSync) dlssPick = v;
                try { File.WriteAllText(DlssPickFile, v); } catch (Exception ex) { Log.Error("Guardando el modelo de DLSS elegido", ex); }
            }
        }

        public static int DlssPickNum(string l)
        {
            switch (l) { case "M": return 13; case "L": return 12; case "K": return 11; case "J": return 10; case "E": return 5; default: return 0; }
        }
    }

    // ---------------------------------------------------------------- la ventana
    sealed class VentanaAjustes : NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct Pt { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct Sz { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { public byte Op, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)] struct BmpInfo { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPpm, YPpm, ClrUsed, ClrImportant; }
        [StructLayout(LayoutKind.Sequential)] struct Rc { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MonInfo { public int Size; public Rc Monitor, Work; public int Flags; }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BmpInfo bi, uint usage, out IntPtr bits, IntPtr sec, uint off);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool GdiFlush();
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref Pt pos, ref Sz size, IntPtr src, ref Pt srcPos, int key, ref Blend b, int flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Pt p, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MonInfo mi);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);

        public readonly float U;            // escala: alto de la pantalla / 1080
        readonly int w, h;
        readonly Pt at;
        IntPtr memDc, dib, oldBmp;
        Bitmap surface;
        bool shown;
        byte alpha;

        // h0: la ventana de la imagen (su pantalla); filas: cuantas como mucho (para el alto)
        public VentanaAjustes(IntPtr imagen, int filas)
        {
            IntPtr prevCtx = IntPtr.Zero;
            try { prevCtx = SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { }   // PER_MONITOR_AWARE_V2: pixeles reales
            try
            {
                MonInfo mi = new MonInfo(); mi.Size = Marshal.SizeOf(typeof(MonInfo));
                IntPtr mon = imagen != IntPtr.Zero ? MonitorFromWindow(imagen, 2) : MonitorFromPoint(new Pt(), 1);
                Rectangle scr = GetMonitorInfo(mon, ref mi) ? Rectangle.FromLTRB(mi.Monitor.L, mi.Monitor.T, mi.Monitor.R, mi.Monitor.B) : Screen.PrimaryScreen.Bounds;
                U = Math.Max(0.6f, scr.Height / 1080f);
                Size panel = Panel(U, filas);
                int m = (int)(Margen * U);
                w = panel.Width + 2 * m; h = Math.Min(scr.Height, panel.Height + 2 * m);
                at = new Pt { X = scr.X + (scr.Width - w) / 2, Y = scr.Y + (scr.Height - h) / 2 };
                BmpInfo bi = new BmpInfo { Size = Marshal.SizeOf(typeof(BmpInfo)), Width = w, Height = -h, Planes = 1, BitCount = 32 };
                IntPtr bits, screen = GetDC(IntPtr.Zero);
                try { memDc = CreateCompatibleDC(screen); dib = CreateDIBSection(screen, ref bi, 0, out bits, IntPtr.Zero, 0); }
                finally { ReleaseDC(IntPtr.Zero, screen); }
                if (memDc == IntPtr.Zero || dib == IntPtr.Zero || bits == IntPtr.Zero) { Dispose(); throw new InvalidOperationException("sin superficie para el menu de ajustes"); }
                oldBmp = SelectObject(memDc, dib);
                surface = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
                CreateParams cp = new CreateParams();
                cp.Caption = "ConsoleEnhancer · Ajustes";
                cp.X = at.X; cp.Y = at.Y; cp.Width = w; cp.Height = h;
                cp.Style = unchecked((int)0x80000000);                  // WS_POPUP
                cp.ExStyle = 0x80000 | 0x20 | 0x08000000 | 0x80 | 0x8;   // LAYERED | TRANSPARENT | NOACTIVATE | TOOLWINDOW | TOPMOST
                CreateHandle(cp);
            }
            finally { try { if (prevCtx != IntPtr.Zero) SetThreadDpiAwarenessContext(prevCtx); } catch { } }
        }

        // ---- medidas (en "u": pixeles a 1080p) ----
        public const float Margen = 40, Ancho = 1040, Cab = 132, Fila = 62, Pie = 168, ColValor = 600;
        static Size Panel(float u, int filas) { return new Size((int)(Ancho * u), (int)((Cab + filas * Fila + 22 + Pie) * u)); }

        public Graphics Lienzo()
        {
            Graphics g = Graphics.FromImage(surface);
            g.CompositingMode = CompositingMode.SourceCopy;
            using (SolidBrush clear = new SolidBrush(Color.FromArgb(0, 0, 0, 0))) g.FillRectangle(clear, 0, 0, w, h);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return g;
        }
        public int W { get { return w; } }
        public int H { get { return h; } }

        public void Mostrar(byte a)
        {
            if (Handle == IntPtr.Zero || surface == null) return;
            alpha = a;
            GdiFlush();
            Pt dst = at, src = new Pt();
            Sz size = new Sz { W = w, H = h };
            Blend b = new Blend { Op = 0, Flags = 0, Alpha = a, Format = 1 };
            IntPtr screen = GetDC(IntPtr.Zero);
            try { UpdateLayeredWindow(Handle, screen, ref dst, ref size, memDc, ref src, 0, ref b, 2); }
            finally { ReleaseDC(IntPtr.Zero, screen); }
            if (!shown) { ShowWindow(Handle, 4); shown = true; }   // SW_SHOWNOACTIVATE
            Encima();
        }
        public void Repintar() { Mostrar(alpha); }
        // (1.2.2, pruebas: lo pintado a un PNG, sin depender de una captura de pantalla; el "ajpng" de las pruebas por la tuberia)
        public void GuardarPng(string path) { if (surface == null) return; GdiFlush(); surface.Save(path, ImageFormat.Png); }
        public void Encima() { if (Handle != IntPtr.Zero) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE: MA_NOACTIVATE
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
            if (surface != null) { surface.Dispose(); surface = null; }
            if (memDc != IntPtr.Zero) { if (oldBmp != IntPtr.Zero) SelectObject(memDc, oldBmp); DeleteDC(memDc); memDc = IntPtr.Zero; }
            if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
        }
    }

    // ---------------------------------------------------------------- la parte de TrayApp
    partial class TrayApp
    {
        enum AjOrden { Arriba, Abajo, Izq, Der, Ok, Atras }

        readonly List<FilaAjuste> ajFilas = new List<FilaAjuste>();
        VentanaAjustes ajVentana;
        volatile bool ajustesAbierto;
        int ajFoco;                           // indice en la navegacion: filas visibles y activas, luego Aplicar (-1) y Cancelar (-2)
        int ajPie = -1;                       // -1 = la navegacion esta en las filas; 0 = Aplicar; 1 = Cancelar
        System.Windows.Forms.Timer ajTimer;   // fundido de entrada y "siempre encima" cada 0,5 s
        int ajFundido;
        DateTime ajCerradoEn = DateTime.MinValue;
        // (1.2.4 r7, coordinador; el usuario: "no me detecta el boton home del mando de pc, no reacciona"): el menu se abria con el boton
        // central y se cerraba solo 2-5 ms despues ("cerrar sin aplicar"): la misma pulsacion llegaba tambien como Atras (el receptor del
        // mando tiene ademas un teclado HID que manda su tecla, o el mando la da como B). Atras en los primeros ms tras abrir no cierra
        DateTime ajAbiertoEn = DateTime.MinValue;
        const int AjAtrasTrasAbrirMs = 450;

        // ---- del mando (GamepadLoop, su hilo) ----
        bool ajGuiaAntes;
        uint ajAntes;
        DateTime ajRepDesde = DateTime.MinValue, ajRepUlt = DateTime.MinValue;
        uint ajRepDir;

        // true = el mando es del menu este paso (GamepadLoop no hace nada mas)
        bool AjustesMando(PadSnapshot s)
        {
            uint bt = s.Buttons;
            bool guia = (bt & PadBtn.Guide) != 0;
            // 22-11 (1.2.2, MENU): cada pulsacion cuenta aunque ya se haya soltado (PadHub.GuidePresses) y, si no se puede abrir aun, se
            // abre en cuanto se pueda o se dice por que no (src\Lanzador\AjustesGuia.cs). Antes: solo si seguia pulsada en esta lectura y
            // con PuedeAbrirAjustes en ese momento; si no, nada (ni en pantalla)
            bool guiaNueva = AjGuiaNueva(s);
            ajGuiaAntes = guia;
            if (guiaNueva)
            {
                if (ajustesAbierto) { OnUi(delegate { AjustesCerrar("boton central"); }); return true; }
                AjGuiaPulsada("boton central");
                ajAntes = bt;
                return true;
            }
            if (!ajustesAbierto)
            {
                ajAntes = bt;
                // (un momento tras cerrar: lo que siga pulsado no hace lo de siempre)
                return (DateTime.Now - ajCerradoEn).TotalMilliseconds < 250;
            }
            // El stick izquierdo como la cruceta
            uint dir = bt & (PadBtn.Up | PadBtn.Down | PadBtn.Left | PadBtn.Right);
            if (s.LY > 16000) dir |= PadBtn.Up; else if (s.LY < -16000) dir |= PadBtn.Down;
            if (s.LX < -16000) dir |= PadBtn.Left; else if (s.LX > 16000) dir |= PadBtn.Right;
            uint pulsado = bt & ~ajAntes;
            ajAntes = bt;
            if ((pulsado & PadBtn.A) != 0) Ajuste(AjOrden.Ok);
            else if ((pulsado & PadBtn.B) != 0) Ajuste(AjOrden.Atras);
            // Direcciones con repeticion al mantener (0,38 s y luego cada 0,11 s)
            uint una = (dir & PadBtn.Up) != 0 ? PadBtn.Up : (dir & PadBtn.Down) != 0 ? PadBtn.Down : (dir & PadBtn.Left) != 0 ? PadBtn.Left : (dir & PadBtn.Right) != 0 ? PadBtn.Right : 0;
            DateTime now = DateTime.Now;
            if (una == 0) ajRepDir = 0;
            else if (una != ajRepDir) { ajRepDir = una; ajRepDesde = now; ajRepUlt = now; AjusteDir(una); }
            else if ((now - ajRepDesde).TotalMilliseconds > 380 && (now - ajRepUlt).TotalMilliseconds > 110) { ajRepUlt = now; AjusteDir(una); }
            return true;
        }

        void AjusteDir(uint d)
        {
            Ajuste(d == PadBtn.Up ? AjOrden.Arriba : d == PadBtn.Down ? AjOrden.Abajo : d == PadBtn.Left ? AjOrden.Izq : AjOrden.Der);
        }

        void Ajuste(AjOrden o) { OnUi(delegate { AjustesOrden(o); }); }

        // ---- teclado (KeyHook, hilo de la interfaz): true = la tecla es del menu ----
        bool AjustesTecla(uint vk)
        {
            if (!ajustesAbierto) return false;
            switch (vk)
            {
                case 0x26: AjustesOrden(AjOrden.Arriba); return true;
                case 0x28: AjustesOrden(AjOrden.Abajo); return true;
                case 0x25: AjustesOrden(AjOrden.Izq); return true;
                case 0x27: AjustesOrden(AjOrden.Der); return true;
                case 0x0D: case 0x20: AjustesOrden(AjOrden.Ok); return true;
                case 0x1B: case 0x08: AjustesOrden(AjOrden.Atras); return true;
                default: return false;
            }
        }

        // ¿Se puede abrir? Con la imagen de la consola delante y fuera de cambios. aviso: en el menu de la consola, lo dice
        // (pruebas: CE_AJUSTES_PRUEBA=1 lo abre sin consola, solo para verlo; aplicar sigue pidiendo la imagen)
        static readonly bool AjPrueba = Environment.GetEnvironmentVariable("CE_AJUSTES_PRUEBA") == "1";

        // 22-11 (1.2.2, MENU): lo decide AjustesPuede (src\Lanzador\AjustesGuia.cs): tambien sobre el puente / el telon mientras Magpie prepara;
        // lo pasajero (6 s de imagen nueva, 4 s tras salir del menu de la consola, otra captura...) en cola. aviso: lo dice (log y pantalla)
        bool PuedeAbrirAjustes(bool aviso)
        {
            string no, txt;
            AjPuede r = AjustesPuede(out no, out txt);
            if (r == AjPuede.Si) return true;
            if (aviso && (DateTime.Now - ajNoLog).TotalSeconds > 2) { ajNoLog = DateTime.Now; Log.Write("Menu de ajustes: no se abre: " + no); AjAvisoNo(txt); }
            return false;
        }
        DateTime ajNoLog = DateTime.MinValue;
        DateTime salioMenuEn = DateTime.MinValue;   // (1.0.12) cuando se salio del menu de la consola (LeaveHome / LeaveHomeGrace)

        // ---------------- abrir / cerrar (hilo de la interfaz) ----------------
        void AjustesAbrir(string desde)
        {
            if (ajustesAbierto) return;
            if (!PuedeAbrirAjustes(false)) return;
            try
            {
                AjustesModelo();
                ajFirma = AjFirma(ajFilas, curGame);
                IntPtr img = mpvWindow;
                int altas = AjAltas(ajFilas);   // (1.2.2: sin las escondidas)
                ajVentana = new VentanaAjustes(img, altas);
                ajustesAbierto = true;
                ajAbiertoEn = DateTime.Now;
                ajPie = -1; ajFoco = 0;
                AjustesFocoValido(+1);
                ajFundido = 0;
                AjustesPintar(0);
                if (ajTimer == null)
                {
                    ajTimer = new System.Windows.Forms.Timer();
                    ajTimer.Interval = 16;
                    ajTimer.Tick += delegate { AjustesTick(); };
                }
                ajTimer.Interval = 16;
                ajTimer.Start();
                if (AjCambiando()) AjSobreElPuente(); // (22-11, 1.2.2: abierto mientras Magpie prepara, src\Lanzador\AjustesGuia.cs)
                Log.Write("Menu de ajustes abierto (" + desde + ")");
                lock (cmdQueue) cmdQueue.Add(JsonCmd("script-message", "switch2-vigila", "0"));   // (1.3.1, FPS: el motor no juzga los fps con el menu encima; EngineEscalera.cs)
                RitmoDiag.Ventanal("el menu de ajustes encima de la imagen");   // (1.2.3 RITMO2: sus tirones no son del camino, Ritmo.cs)
            }
            catch (Exception ex) { Log.Error("Menu de ajustes: abrir", ex); AjustesCerrar(null); }
        }

        void AjustesTick()
        {
            if (!ajustesAbierto || ajVentana == null) { if (ajTimer != null) ajTimer.Stop(); return; }
            if (ajFundido < 8)
            {
                ajFundido++;
                double t = ajFundido / 8.0;
                ajVentana.Mostrar((byte)Math.Round(255 * (1 - Math.Pow(1 - t, 3))));
                if (ajFundido == 8) ajTimer.Interval = AjCambiando() ? 100 : 500;
                return;
            }
            ajVentana.Encima();
            // 22-11 (1.2.2): con la imagen cambiando (el puente, el telon, la ventana de Magpie al presentar salen encima) cada 0,1 s
            bool cambiando = AjCambiando();
            ajTimer.Interval = cambiando ? 100 : 500;
            // La imagen se ha ido (consola apagada...) o se ha vuelto al menu de la consola: fuera. 22-11 (1.2.2): mientras Magpie prepara
            // (puente, telon) sigue abierto (AjustesSigue, src\Lanzador\AjustesGuia.cs)
            string porque;
            bool eraCambiando = ajEraCambiando;
            ajEraCambiando = cambiando;
            if (!AjustesSigue(out porque)) AjustesCerrar(porque);
            else if (cambiando) { if ((DateTime.Now - ajPintadoCambiando).TotalMilliseconds > 450) { ajPintadoCambiando = DateTime.Now; AjustesRefrescar(); AjustesPintar(-1); } }
            else { AjustesRefrescar(); if (eraCambiando && ajVentana != null) AjustesPintar(-1); }   // (sin el "preparando la imagen")
        }
        DateTime ajPintadoCambiando = DateTime.MinValue;
        bool ajEraCambiando;

        // (1.1.2, el usuario: "si se cambian los ajustes que se vean reflejados en la interfaz del boton home"): con el menu abierto, lo
        // de ahora se vuelve a mirar cada 0,5 s (botones, atajos, bandeja, otro juego, un "Aplicar" que acaba, cambios por fps bajos...):
        // si algo ha cambiado, las filas muestran lo nuevo; lo que el usuario ya habia cambiado en el menu y no ha aplicado se queda
        static string AjFirma(List<FilaAjuste> filas, string juego)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(juego ?? "");
            foreach (FilaAjuste f in filas)
                sb.Append('|').Append(f.Id).Append('=').Append(f.Orig >= 0 && f.Orig < f.Vals.Count ? f.Vals[f.Orig] : "?")
                  .Append(f.Activa ? "" : "-").Append(':').Append(string.Join(",", f.Vals.ToArray())).Append(':').Append(string.Join(",", f.Bloq.ToArray()));
            return sb.ToString();
        }
        string ajFirma;
        bool ajRefrescando;

        void AjustesRefrescar()
        {
            if (ajVentana == null || passesBusy || relaunchSize != null || relaunchEngine) return;   // (a medio cambiar: al acabar)
            List<FilaAjuste> viejas = new List<FilaAjuste>(ajFilas);
            FilaAjuste foco = AjFilaFoco();
            string idFoco = foco != null ? foco.Id : null;
            // (1.2.4 ronda 7, INTEGRA7: ajRefrescando = lo elegido sin aplicar en las filas con candado -smx4Quiere, efgQuiere- no vuelve a lo guardado)
            ajRefrescando = true;
            try { AjustesModelo(); }
            catch (Exception ex) { Log.Error("Menu de ajustes: refrescar", ex); ajFilas.Clear(); ajFilas.AddRange(viejas); return; }
            finally { ajRefrescando = false; }
            string firma = AjFirma(ajFilas, curGame);
            if (firma == ajFirma) { ajFilas.Clear(); ajFilas.AddRange(viejas); return; }   // (nada nuevo: sigue todo como estaba)
            // Lo cambiado en el menu sin aplicar se mantiene (si sigue siendo posible)
            // 21-11 (1.2.1, pendiente de la revision: "tipo del menu se pierde en un refresco con el modo cambiado"): la lista de tipos de
            // generacion depende del modo elegido; el modelo nuevo la trae para el modo de AHORA y el tipo elegido para el otro modo no estaba
            // en ella: se perdia. Ahora se pone lo cambiado, se recalcula lo que depende (AjustesDepende) y se vuelve a poner lo que antes no
            // cabia. Y con el modo cambiado, el tipo que se veia aunque no se tocara (es el que se aplicara con ese modo)
            FilaAjuste viejoModo = null;
            foreach (FilaAjuste v in viejas) if (v.Id == "modo") viejoModo = v;
            bool modoCambiado = viejoModo != null && viejoModo.Cambiada;
            List<FilaAjuste> sinSitio = new List<FilaAjuste>();
            foreach (FilaAjuste v in viejas)
            {
                if (!v.Cambiada && !(modoCambiado && v.Id == "tipo")) continue;
                FilaAjuste n = AjFila(v.Id);
                if (n == null) continue;
                int i = n.Vals.IndexOf(v.Val);
                if (i >= 0) n.Sel = i; else sinSitio.Add(v);
            }
            AjustesDepende(false);
            if (sinSitio.Count > 0)
            {
                foreach (FilaAjuste v in sinSitio)
                {
                    FilaAjuste n = AjFila(v.Id);
                    if (n == null) continue;
                    int i = n.Vals.IndexOf(v.Val);
                    if (i >= 0) n.Sel = i;
                }
                AjustesDepende(false);
            }
            ajFirma = firma;   // (la del modelo recien hecho: si no, se volveria a refrescar cada vez)
            Log.Write("Menu de ajustes: actualizado con lo de ahora");
            int altas = AjAltas(ajFilas), altasAntes = AjAltas(viejas);   // (1.2.2: sin las escondidas)
            if (altas != altasAntes)
            {
                // Otro numero de filas: otra ventana del tamaño nuevo, ya opaca
                // (1.2.02: la nueva antes de quitar la vieja: si fallaba, el menu quedaba invisible quedandose el mando)
                VentanaAjustes nueva = new VentanaAjustes(mpvWindow, altas);
                try { ajVentana.Dispose(); } catch { }
                ajVentana = nueva;
            }
            List<FilaAjuste> nav = AjNavegables();
            if (ajPie < 0) { int k = idFoco == null ? -1 : nav.FindIndex(delegate(FilaAjuste f) { return f.Id == idFoco; }); ajFoco = k >= 0 ? k : 0; }
            AjustesFocoValido(+1);
            AjustesPintar(255);
            ajVentana.Encima();
        }

        // (1.1.2, el usuario: "he salido al menu home y ha tardado mucho en quitar el menu de configuracion"): el telon congelado del
        // HOME es una copia de la pantalla, con el menu de ajustes encima si seguia abierto, y se quedaba ~5 s hasta la imagen nueva.
        // Desde cualquier hilo: lo quita en el de la interfaz; espera = hasta 300 ms a que se haya ido (antes de copiar la pantalla)
        void AjustesQuitarYa(string porque, bool espera)
        {
            if (!ajustesAbierto) return;
            OnUi(delegate { AjustesCerrar(porque); });
            if (!espera) return;
            Stopwatch sw = Stopwatch.StartNew();
            while (ajustesAbierto && sw.ElapsedMilliseconds < 300) System.Threading.Thread.Sleep(5);
            System.Threading.Thread.Sleep(20); // (DWM: un refresco para que la ventana deje de verse)
        }

        void AjustesCerrar(string porque)
        {
            bool era = ajustesAbierto;
            ajustesAbierto = false;
            ajCerradoEn = DateTime.Now;
            if (ajTimer != null) ajTimer.Stop();
            if (ajVentana != null) { try { ajVentana.Dispose(); } catch { } ajVentana = null; }
            if (era && porque != null) Log.Write("Menu de ajustes cerrado (" + porque + ")");
            if (era) RitmoDiag.Ventanal("el menu de ajustes (al quitarse)");   // (1.2.3 RITMO2, src\Lanzador\Ritmo.cs)
            if (era) lock (cmdQueue) cmdQueue.Add(JsonCmd("script-message", "switch2-vigila", "1"));   // (1.3.1, FPS: EngineEscalera.cs, 2 s de calma)
        }

        // ---------------- el modelo: lo de ahora ----------------
        static readonly string[] AjCapturas = { "1280x720", "1920x1080", "2560x1440", "3840x2160" };

        FilaAjuste AjFila(string id) { foreach (FilaAjuste f in ajFilas) if (f.Id == id) return f; return null; }

        void AjustesModelo()
        {
            PrepareCfg();
            ajFilas.Clear();
            int modo = cfg.ModoFps;
            bool sino;

            FilaAjuste cap = new FilaAjuste { Id = "cap", Nombre = Tx.T("Captura", "Capture") };
            List<string> cv = new List<string>(), ct = new List<string>();
            foreach (string c in AjCapturas)
            {
                bool util = true;
                try { util = Consolas.Actual.CapturaUtil(c); } catch { }
                if (!util && c != cfg.VideoSize) continue;
                cv.Add(c); ct.Add(c == "3840x2160" ? "4K" : SizeName(c));
            }
            cap.Poner(cv, ct, cfg.VideoSize, cfg.VideoSize);

            FilaAjuste mo = new FilaAjuste { Id = "modo", Nombre = Tx.T("Modo", "Mode") };
            mo.Poner(new List<string> { "30", "40", "60", "120" }, new List<string> { "30 fps", "40 fps", "60 fps", "120 fps" }, modo.ToString(), modo.ToString());   // (1.3.2, MODO40)

            FilaAjuste fg = new FilaAjuste { Id = "fg", Nombre = Tx.T("Generación de fotogramas", "Frame generation") };
            sino = fgWanted && cfg.FgAvailable;
            fg.Poner(SiNo(), SiNoTxt(), sino ? "1" : "0", sino ? "1" : "0");
            fg.Visible = cfg.FgAvailable;

            FilaAjuste tipo = new FilaAjuste { Id = "tipo", Nombre = Tx.T("Tipo de generación", "Frame generation type"), Carrusel = true };
            tipo.Visible = cfg.FgAvailable;
            // (1.2.2-1.2.4: aqui iba la fila "Multiplicador" x2 / x3 / x4. 1.2.4 ronda 6, QUITAFG: fuera, todas x2; solo se elige el tipo)

            FilaAjuste d5 = new FilaAjuste { Id = "d5", Nombre = "DLSS 5" };
            d5.Poner(SiNo(), SiNoTxt(), dlss5Shown ? "1" : "0", dlss5Shown ? "1" : "0");
            // (1.1.2) puesta a mano en extras\ con la app abierta: a magpie\ ya (Magpie la carga la proxima vez que se abre)
            // (1.2.02: el modelo se rehace cada 0,5 s con el menu abierto: como mucho cada 10 s, por si la copia falla)
            if (!Integrado.Dlss5Available && Extras.HayDlss5 && ExtrasToca()) { Ce.PrepararExtras(); Integrado.Dlss5Rever(); }
            d5.Visible = Integrado.Dlss5Available;
            // (1.1.2, el usuario: "si no he instalado el dlss 5 ponga que no esta instalado y que para instalarlo que meta el archivo en
            // donde haya que ponerlo"): con NVIDIA y sin la DLL, la fila dice "No instalado" y donde va. La app no dice de donde sacarla
            FilaAjuste d5no = new FilaAjuste { Id = "d5info", Nombre = "DLSS 5", Activa = false, Nota = Tx.T("No instalado", "Not installed") };
            d5no.Visible = !Integrado.Dlss5Available && !Ce.NoNvidia;
            d5no.Ayuda = Tx.T("Para instalarlo, pon tu archivo " + Integrado.Dlss5Dll + " en la carpeta " + Extras.Dir + " y vuelve a abrir este menú " +
                              "(o en la bandeja: «Añadir DLSS 5 (tu propio archivo)…»)",
                              "To install it, put your " + Integrado.Dlss5Dll + " file in the folder " + Extras.Dir + " and open this menu again " +
                              "(or from the tray: “Add DLSS 5 (your own file)…”)");

            // (1.2.02-1.2.4: aqui iba la fila "DLSS FG · No disponible ahora" -faltaba dlssg_sm86-. 1.2.4 ronda 6, QUITAFG: sin DLSS FG, fuera)

            FilaAjuste niv = new FilaAjuste { Id = "nivel", Nombre = Tx.T("Nivel de DLSS 5", "DLSS 5 level") };
            string nv = Math.Max(1, Math.Min(4, dlss5Level)).ToString();
            niv.Poner(new List<string> { "1", "2", "3", "4" }, new List<string> { "1", "2", "3", "4" }, nv, nv);
            niv.Visible = Integrado.Dlss5Available;

            FilaAjuste pas = new FilaAjuste { Id = "pasadas", Nombre = Tx.T("Pasadas de DLSS 5", "DLSS 5 passes") };
            string pv = Math.Max(1, Math.Min(3, dlss5Passes)).ToString();
            pas.Poner(new List<string> { "1", "2", "3" }, new List<string> { "1", "2", "3" }, pv, pv);
            pas.Visible = Integrado.Dlss5Available;

            // (1.0.12, el usuario: "añade a las opciones de escalador diferentes dlss"): con DLSS de escalador, cada modelo (M, L, K, J, E) y
            // "DLSS auto" (el de siempre segun el modo), y el alternativo detras. (1.0.12: sin la fila del filtro de tramado, a peticion)
            FilaAjuste esc = new FilaAjuste { Id = "esc", Nombre = Tx.T("Escalador", "Upscaler") };
            List<string> ev = new List<string>(), et = new List<string>();
            if (cfg.Scaler == "dlss" && !Ce.NoNvidia)
            {
                ev.Add("dlss:auto"); et.Add(Tx.T("DLSS automático", "DLSS auto"));
                foreach (string l in Config.DlssPicks) { ev.Add("dlss:" + l); et.Add("DLSS " + Config.EngineModelName(l)); }
            }
            else { ev.Add("normal"); et.Add(cfg.ScalerName(cfg.Scaler)); }
            if (cfg.AltScaler != cfg.Scaler && cfg.AltScaler != "fsr" && cfg.AltScaler != "ninguno") { ev.Add("alt"); et.Add(cfg.ScalerName(cfg.AltScaler)); }
            // (1.2.01, el usuario: "añade opcion de fsr 1 o de directamente no usar reescalado por si no quiere el usuario"): para este juego
            if (cfg.Scaler != "fsr") { ev.Add("pick:fsr"); et.Add("FSR 1"); }
            // (23-11, 1.2.4 ronda 6 DLSSSR; el usuario: "añade reescalado xess si puedes tambien al motor"): XeSS SR, por el motor (EngineSr.cs,
            // libxess.dll de dlls_sr= o junto al motor), tambien en NVIDIA; para este juego como FSR 1
            bool xessSr = false;
            // (1.2.4 ronda 6, INTEGRA6: sin Magpie la DLL de Intel esta en xess\, Paths.HayLib; antes se buscaba en magpie\ y sin el no salia)
            try { xessSr = Paths.HayLib("libxess.dll") || (cfg.EnginePath.Length > 0 && File.Exists(Path.Combine(Path.GetDirectoryName(cfg.EnginePath), "libxess.dll"))); } catch { }
            if (cfg.Scaler != "xess" && xessSr) { ev.Add("pick:xess"); et.Add("XeSS"); }
            if (cfg.Scaler != "ninguno") { ev.Add("pick:ninguno"); et.Add(Tx.T("Sin reescalado", "No upscaling")); }
            // (1.2.4 ronda 6, INTEGRA6: el alternativo de R3 / un perfil "escalador alternativo" de antes -RTX Video de Magpie- es ahora FSR 1 o sin
            // reescalado, que no salen como "alt": la fila dice lo que hace de verdad el motor, no DLSS)
            string altPick = !dlssScalerOn && scalerPick.Length == 0 && !ev.Contains("alt") && ev.Contains("pick:" + cfg.AltScaler) ? "pick:" + cfg.AltScaler : "";
            string ea = scalerPick.Length > 0 && cfg.Scaler != scalerPick ? "pick:" + scalerPick : altPick.Length > 0 ? altPick :
                        !dlssScalerOn && ev.Contains("alt") ? "alt" : ev[0] == "normal" ? "normal" : "dlss:" + (Config.DlssPick.Length > 0 ? Config.DlssPick : "auto");
            esc.Poner(ev, et, ea, ea);
            esc.Carrusel = ev.Count > 3;
            esc.Visible = ev.Count > 1;

            // (1.2.3 MOTOR5 tenia aqui la fila "DLSS 5 por: Motor / Magpie"; 1.2.4 SOLOMOTOR: fuera, no se elige: el motor siempre que sepa, SoloMotor.cs)
            FilaAjuste[] efgF = EfgFilas();   // (1.2.4 ronda 6, ENHANCED: multiplicador y baja latencia de Enhanced FG, EnhancedFg.cs)
            ajFilas.AddRange(new[] { cap, mo, fg, tipo, efgF[0], efgF[1], FsrMulFila(), d5, d5no, niv, pas, esc, RtFila() });   // (1.3.3 RTX: Enhanced RT, EnhancedRt.cs)   // (1.3.2, FPS: sin la fila x2 / x4 de Smooth Motion -SmoothX4Fila-: x4 quitado)   // (FGMULTI: el multiplicador de FSR 3 FG, MultiFsr.cs; INTEGRA6: el de Smooth Motion, SmoothX4.cs)
            // (1.2.4 ronda 6, coordinador: las filas que enseña AjustesDepende segun el tipo elegido -multiplicadores de FSR 3 FG, Enhanced FG y
            // Smooth Motion, baja latencia de Enhanced FG- nacen escondidas: si se quitaban aqui no salian nunca; el dibujo ya salta las escondidas)
            ajFilas.RemoveAll(delegate(FilaAjuste f) { return !f.Visible && f.Id != "efgmul" && f.Id != "efglat" && f.Id != "fsrmul" && f.Id != "smx4"; });
            AjustesDepende(true);
        }

        static DateTime extrasVisto = DateTime.MinValue;
        static bool ExtrasToca()
        {
            if ((DateTime.Now - extrasVisto).TotalSeconds < 10) return false;
            extrasVisto = DateTime.Now;
            return true;
        }

        // (1.2.2, MULTI; desde la 1.2.4 ronda 6 aqui: estaban en Multiplicador.cs) altura del menu en filas (las de dos lineas cuentan dos;
        // las escondidas, nada)
        static int AjAltas(List<FilaAjuste> filas)
        {
            int n = 0;
            foreach (FilaAjuste f in filas) if (f.Visible) n += f.Ayuda != null ? 2 : 1;
            return n;
        }

        // Tras un cambio en el menu: si cambia la altura (una fila con su explicacion debajo aparece o se va), la ventana nueva, ya opaca
        void AjRedimensionar(int altasAntes)
        {
            int altas = AjAltas(ajFilas);
            if (altas == altasAntes || ajVentana == null) return;
            VentanaAjustes nueva = new VentanaAjustes(mpvWindow, altas);
            try { ajVentana.Dispose(); } catch { }
            ajVentana = nueva;
            AjustesPintar(255);
            ajVentana.Encima();
        }

        // Un candado pequeño a la izquierda de un valor bloqueado (con lineas: la fuente del menu puede no tener el simbolo)
        static void AjCandado(System.Drawing.Graphics g, System.Drawing.RectangleF cr, float u, System.Drawing.Color c)
        {
            float w = 10 * u, h = 8 * u, x = cr.X + 14 * u, yb = cr.Y + cr.Height / 2 - 1 * u;
            using (System.Drawing.SolidBrush b = new System.Drawing.SolidBrush(c))
            using (System.Drawing.Pen p = new System.Drawing.Pen(c, Math.Max(1f, 1.6f * u)))
            {
                g.FillRectangle(b, x, yb, w, h);
                g.DrawArc(p, x + 2 * u, yb - 6 * u, w - 4 * u, 10 * u, 180, 180);
            }
        }

        // El siguiente valor de una fila sin pasar por los bloqueados (d = +1 / -1; vuelta = carrusel)
        static int AjPaso(FilaAjuste f, int d, bool vuelta)
        {
            int n = f.Vals.Count, s = f.Sel;
            for (int i = 0; i < n; i++)
            {
                int k = s + d;
                if (vuelta) k = (k + n) % n;
                else if (k < 0 || k >= n) return f.Sel;
                s = k;
                if (!f.Bloqueado(s)) return s;
            }
            return f.Sel;
        }

        static List<string> SiNo() { return new List<string> { "1", "0" }; }
        static List<string> SiNoTxt() { return new List<string> { Tx.T("Sí", "On"), Tx.T("No", "Off") }; }

        // Lo que depende de otras filas: el 120 segun la captura, los tipos de generacion del modo elegido, filas apagadas
        void AjustesDepende(bool inicio)
        {
            FilaAjuste cap = AjFila("cap"), mo = AjFila("modo"), fg = AjFila("fg"), tipo = AjFila("tipo"), d5 = AjFila("d5");
            string capSel = cap != null ? cap.Val : cfg.VideoSize;
            int modoAhora = cfg.ModoFps;
            if (mo != null)
            {
                string m = mo.Val;
                List<string> v = new List<string> { "30", "40", "60" }, t = new List<string> { "30 fps", "40 fps", "60 fps" };   // (1.3.2, MODO40)
                // (1.2.01: a 4K el 120 no se ofrece aunque se este en el 120: la capturadora no da 4K a 120 y la app se quedaba cargando)
                if (Puede120(capSel) || (modoAhora == 120 && cfg.Captura120Ok(capSel))) { v.Add("120"); t.Add(Tx.T("120 fps", "120 fps")); }
                if (!v.Contains(m)) m = "60";
                mo.Poner(v, t, m, modoAhora.ToString());
                // (1.3.2, MODO40: con el 40, a que va la captura y lo de la consola)
                mo.Nota = mo.Val != "40" ? "" : cfg.Modo40A120(capSel) ? Tx.T("Captura a 120 Hz · la consola a 120 Hz y el modo de 40 del juego", "Capture at 120 Hz · console at 120 Hz and the game's 40 fps mode")
                                                                        : Tx.T("Captura a 60 Hz: los 40 se reparten parejos (mejor 1440p / 1080p)", "Capture at 60 Hz: 40 fps evened out (better 1440p / 1080p)");
            }
            string modoSel = mo != null ? mo.Val : modoAhora.ToString();
            bool a120 = modoSel == "120";
            if (fg != null) { fg.Activa = !a120; fg.Nota = a120 ? Tx.T("No a 120 fps", "Not at 120 fps") : ""; }
            if (tipo != null)
            {
                bool cap30 = modoSel == "30" || modoSel == "40";   // (1.3.2, MODO40: el 40 con los tipos del 30)
                List<string> opts = a120 ? new List<string>() : GenOptionsFor(cap30, capSel);
                string ahora = AjTipoDe(cap30);
                string sel = inicio ? ahora : tipo.Val;
                string pedido = sel;   // (1.2.2: si no esta con esta captura / modo, lo que va en su lugar no es un cambio, AjTipoForzado)
                if (!opts.Contains(sel)) sel = opts.Contains(ahora) ? ahora : opts.Count > 0 ? opts[0] : "";
                List<string> txt = new List<string>();
                foreach (string o in opts) txt.Add(GenLabel(o, false));
                // (el original: lo de ese modo ahora, solo si el modo no cambia: con otro modo lo elegido siempre se pone)
                tipo.Poner(opts, txt, sel, modoSel == modoAhora.ToString() ? ahora : "\u0000");
                if (modoSel != modoAhora.ToString()) tipo.Orig = tipo.Sel;   // (no cuenta como cambio por si solo)
                bool fgSi = fg == null || fg.Val == "1";
                tipo.Activa = !a120 && fgSi && opts.Count > 0;
                tipo.Nota = a120 ? Tx.T("No a 120 fps", "Not at 120 fps") : opts.Count == 0 ? Tx.T("Ninguna en este PC", "None on this PC") :
                            !fgSi ? Tx.T("Con la generación encendida", "With frame generation on") : "";
                AjTipoForzado(tipo, pedido, cap30, capSel);   // (1.2.2: el log del usuario, 22-11 04:01: captura 1080p en el menu = XeSS FG cambiado por DLSS FG y guardado)
            }
            FsrMulDepende(modoSel);   // (1.2.4 ronda 6, FGMULTI: la fila del multiplicador de FSR 3 FG, MultiFsr.cs)
            EfgDepende(modoSel);   // (1.2.4 ronda 6, ENHANCED: las filas de Enhanced FG, EnhancedFg.cs)
            SmoothX4Depende(modoSel);   // (1.2.4 ronda 6, INTEGRA6: x2 / x4 de Smooth Motion, SmoothX4.cs)
            bool d5si = d5 == null || d5.Val == "1";
            foreach (string id in new[] { "nivel", "pasadas" })
            {
                FilaAjuste f = AjFila(id);
                if (f == null) continue;
                f.Activa = d5si;
                f.Nota = d5si ? "" : Tx.T("Con DLSS 5 encendido", "With DLSS 5 on");
            }
            FilaAjuste esc = AjFila("esc");
            if (esc != null)
            {
                bool k4 = Config.WindowFor(capSel) == "3840x2160";
                esc.Activa = !k4;
                esc.Nota = k4 ? Tx.T("A 4K no se escala", "No upscaling at 4K") : "";
            }
        }

        // El tipo de generacion que va (o iria) en el modo de 30 o 60
        string AjTipoDe(bool cap30)
        {
            if (cap30 == cfg.CapMode && !cfg.Mode120 && fgWanted) { try { return GenCurrent(); } catch { } }
            string p = cap30 ? cfg.FgPick30 : cfg.FgPick60;
            if (!string.IsNullOrEmpty(p)) return p;
            string t = cap30 ? cfg.FgType30 : cfg.FgType60;
            return string.IsNullOrEmpty(t) ? cfg.FgType : t;
        }

        int AjCambios() { int n = 0; foreach (FilaAjuste f in ajFilas) if (f.Cambiada) n++; return n; }

        // ---------------- navegacion (hilo de la interfaz) ----------------
        List<FilaAjuste> AjNavegables() { List<FilaAjuste> l = new List<FilaAjuste>(); foreach (FilaAjuste f in ajFilas) if (f.Visible && f.Activa && f.Vals.Count > 1) l.Add(f); return l; }

        void AjustesFocoValido(int dir)
        {
            List<FilaAjuste> nav = AjNavegables();
            if (nav.Count == 0) { ajPie = 0; return; }
            if (ajFoco >= nav.Count) ajFoco = nav.Count - 1;
            if (ajFoco < 0) ajFoco = 0;
        }

        FilaAjuste AjFilaFoco() { List<FilaAjuste> nav = AjNavegables(); return ajPie < 0 && ajFoco >= 0 && ajFoco < nav.Count ? nav[ajFoco] : null; }

        void AjustesOrden(AjOrden o)
        {
            if (!ajustesAbierto || ajVentana == null) return;
            try
            {
                FilaAjuste foco = AjFilaFoco();
                int n = AjNavegables().Count;
                switch (o)
                {
                    case AjOrden.Arriba:
                        if (ajPie >= 0) { ajPie = -1; ajFoco = n - 1; }
                        else if (ajFoco > 0) ajFoco--;
                        else { ajPie = 0; }   // (de la primera al pie: da la vuelta)
                        break;
                    case AjOrden.Abajo:
                        if (ajPie >= 0) { ajPie = -1; ajFoco = 0; }
                        else if (ajFoco < n - 1) ajFoco++;
                        else ajPie = 0;
                        break;
                    case AjOrden.Izq:
                    case AjOrden.Der:
                        if (ajPie >= 0) { ajPie = ajPie == 0 ? 1 : 0; break; }
                        if (foco == null) break;
                        int d = o == AjOrden.Der ? 1 : -1;
                        foco.Sel = AjPaso(foco, d, foco.Carrusel);   // (1.2.2: sin pasar por los bloqueados, Multiplicador.cs)
                        AjustesTrasCambio(foco);
                        break;
                    case AjOrden.Ok:
                        if (ajPie == 0) { AjustesAplicar(); return; }
                        if (ajPie == 1) { AjustesCerrar("cancelar"); return; }
                        if (foco == null) break;
                        foco.Sel = AjPaso(foco, 1, true);
                        AjustesTrasCambio(foco);
                        break;
                    case AjOrden.Atras:
                        {
                            double trasAbrir = (DateTime.Now - ajAbiertoEn).TotalMilliseconds;
                            if (trasAbrir >= 0 && trasAbrir < AjAtrasTrasAbrirMs)
                            {
                                Log.Write("Menu de ajustes: Atras ignorado (" + ((int)trasAbrir).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                          " ms tras abrirlo: la misma pulsacion del boton central)");
                                return;
                            }
                        }
                        AjustesCerrar("cerrar sin aplicar");
                        return;
                }
                AjustesPintar(-1);
            }
            catch (Exception ex) { Log.Error("Menu de ajustes", ex); }
        }

        void AjustesTrasCambio(FilaAjuste f)
        {
            FilaAjuste antes = f;
            int altasAntes = AjAltas(ajFilas);
            SmoothX4Tocada(f);   // (1.2.4 ronda 7, FGREAL: lo elegido en la fila x2 / x4 de Smooth Motion, SmoothX4.cs)
            EfgTocada(f);   // (1.2.4 ronda 7, INTEGRA7: y en la de Enhanced FG, EnhancedFg.cs)
            AjustesDepende(false);
            // (si una fila deja de ser navegable, el foco sigue en la misma)
            List<FilaAjuste> nav = AjNavegables();
            int i = nav.IndexOf(antes);
            if (i >= 0) ajFoco = i; else AjustesFocoValido(0);
            AjRedimensionar(altasAntes);   // (1.2.2: una fila con su explicacion debajo -el tipo que no puede ir- cambia la altura)
        }

        // ---------------- aplicar (todo a la vez) ----------------
        void AjustesAplicar()
        {
            if (AjCambios() == 0) { AjustesCerrar("aplicar sin cambios"); return; }
            Dictionary<string, string> e;
            HashSet<string> cambiadas;
            List<string> que;
            AjRecoge(out e, out cambiadas, out que);
            AjustesCerrar(null);
            Log.Write("Menu de ajustes: aplicar " + string.Join(", ", que.ToArray()));
            if (RtSoloEsto(e, cambiadas)) return;   // (1.3.3 RTX: solo Enhanced RT: en marcha, sin reabrir la imagen; EnhancedRt.cs)
            ThreadPool.QueueUserWorkItem(delegate { try { AjustesPonerTodo(e, cambiadas); } catch (Exception ex) { Log.Error("Menu de ajustes: aplicando", ex); } });
        }

        // Lo que se aplica (e: las filas que cuentan), las filas tocadas (para el aviso: lo que se dice aunque no cambie) y el texto del log
        void AjRecoge(out Dictionary<string, string> e, out HashSet<string> cambiadas, out List<string> que)
        {
            e = new Dictionary<string, string>();
            cambiadas = new HashSet<string>();
            que = new List<string>();
            FilaAjuste fm = AjFila("modo");
            bool modoCambia = fm != null && fm.Cambiada;
            foreach (FilaAjuste f in ajFilas)
            {
                if (!f.Activa || !f.Visible) continue;
                if (f.Id == "tipo" && !f.Cambiada && !modoCambia) continue;   // (sin tocarlo: lo elegido sigue como estaba, tambien "lo del ini")
                // (1.2.02) el escalador solo si se ha tocado: con R3 en un alternativo que el menu no lista (FSR 1 de AMD / Intel / GTX),
                // aplicar otra cosa lo devolvia al normal sin decir nada
                if (f.Id == "esc" && !f.Cambiada) continue;
                e[f.Id] = f.Val;
            }
            foreach (FilaAjuste f in ajFilas)
                if (f.Cambiada) { cambiadas.Add(f.Id); que.Add(f.Nombre + " " + (f.Sel < f.Txts.Count ? f.Txts[f.Sel] : f.Val)); }
        }

        static string AjVal(Dictionary<string, string> e, string k) { string v; return e.TryGetValue(k, out v) ? v : null; }

        // El modo que va a quedar (120 con una captura que no lo da: 60) y la captura; modoPedido = el del menu
        int AjModoDe(Dictionary<string, string> e, out string capV, out int modoPedido)
        {
            int modoAhora = cfg.ModoFps;
            capV = AjVal(e, "cap") ?? cfg.VideoSize;
            string modoV = AjVal(e, "modo") ?? modoAhora.ToString();
            int modo = int.Parse(modoV);
            modoPedido = modo;
            if (modo == 120 && !cfg.Captura120Ok(capV)) modo = 60;   // (1.2.01: 4K a 120 no lo da la capturadora)
            return modo;
        }

        // Lo elegido, a la config (con applying puesto: solo se guarda; la captura y el modo, despues)
        void AjPonerEstado(Dictionary<string, string> e, int modo)
        {
            string v;
            if ((v = AjVal(e, "nivel")) != null) { int n = int.Parse(v); if (n != dlss5Level) SetLevel(n); }
            if ((v = AjVal(e, "pasadas")) != null)
            {
                int n = int.Parse(v);
                if (n != dlss5Passes) { dlss5Passes = n; SaveText(Paths.Dlss5PassesFile, v); }
            }
            if ((v = AjVal(e, "d5")) != null && (v == "1") != dlss5Shown) SetDlss5State(v == "1");
            if ((v = AjVal(e, "esc")) != null)
            {
                if (v.StartsWith("pick:")) SetScalerChoice(true, v.Substring(5));   // (1.2.01: FSR 1 / sin reescalado)
                else SetScalerChoice(v != "alt");
                if (v.StartsWith("dlss:")) { string l = v.Substring(5); Config.DlssPick = l == "auto" ? "" : l; }
            }
            if ((v = AjVal(e, "fg")) != null)
            {
                bool on = v == "1" && cfg.FgAvailable;
                if (on != fgWanted) { fgWanted = on; SaveText(Paths.FgFile, on ? "1" : "0"); }
            }
            // (1.2.4 ronda 7, TEXTOS: el tipo antes que los multiplicadores: el log de SmoothX4Aplicar -"ahora no: <por que>"- miraba el tipo
            // de antes: "x4 ... ahora no: la generacion del modo 30 no es Smooth Motion" eligiendo Smooth Motion x4 a la vez, 23-11 12:05:54)
            if ((v = AjVal(e, "tipo")) != null && v.Length > 0 && v != "\u0000" && modo != 120)
            {
                if (v == "efg") { FilaAjuste ft = AjFila("tipo"); if (ft == null || ft.Cambiada) Config.EfgReintenta("menu de ajustes"); }   // (1.2.4 ronda 7, EFGFIX)
                if (modo == 30 || modo == 40) SetPicks(cfg.FgPick60, v); else SetPicks(v, cfg.FgPick30);   // (1.3.2, MODO40)
            }
            FsrMulAplicar(e);   // (1.2.4 ronda 6, FGMULTI: MultiFsr.cs)
            EfgAplicar(e);   // (1.2.4 ronda 6, ENHANCED: EnhancedFg.cs)
            SmoothX4Aplicar(e);   // (1.2.4 ronda 6, INTEGRA6: SmoothX4.cs)
            RtAplicar(e);   // (1.3.3 RTX: Enhanced RT, EnhancedRt.cs)
            if ((v = AjVal(e, "tram")) != null && (v == "1") != cfg.StrongDitherOn)
            {
                cfg.StrongDitherOn = v == "1";
                SaveText(Paths.StrongDitherFile, v);
            }
        }

        void AjustesPonerTodo(Dictionary<string, string> e, HashSet<string> cambiadas)
        {
            // 22-11 (1.2.2, MENU): con Magpie preparando (el puente o el telon a la vista; sin otra captura ni el 120) se pone ya y Magpie se
            // reabre con lo nuevo sin esperar a que acabe (src\Lanzador\AjustesGuia.cs). Si no, con algo cambiando aun se espera a que acabe
            // (antes 10 s como mucho y luego no se aplicaba: ahora hasta 45 s, con aviso)
            bool enCaliente = AjEnCaliente(e);
            if (enCaliente) Log.Write("Menu de ajustes: la imagen se esta preparando: se aplica ya (en caliente)");
            else if (!AjEsperarParaAplicar()) return;
            // (1.2.4 ronda 7, TEXTOS: el aviso, de lo que va a quedar de verdad -no de lo pedido- frente a lo de antes; src\Lanzador\Avisos.cs)
            AvisoFoto antes = AvisoAhora();
            int modoAhora = cfg.ModoFps;
            string capV;
            int modoPedido;
            int modo = AjModoDe(e, out capV, out modoPedido);
            bool capCambia = capV != cfg.VideoSize, modoCambia = modo != modoAhora;
            lastPadAction = DateTime.Now;   // (el telon / el aviso de lo que reabre lleva el texto, como con un boton)
            lock (profileSync) applying = true;
            try
            {
                AjPonerEstado(e, modo);
                // (con otra captura, el modo solo se guarda: la reapertura de la captura lo coge todo, como los perfiles. 1.2.2: en caliente
                // tambien: lo coge el Magpie que se reabre)
                if ((capCambia || enCaliente) && modoCambia) SetModeN(modo);
            }
            finally { applying = false; }
            ApplyEdgeShader();
            SaveCurProfile();
            string tit, det;
            AjAvisoCalcula(antes, cambiadas, capV, modo, modoPedido, out tit, out det);
            if (enCaliente)
            {
                if (AjReiniciarEnCaliente()) return;
                // (no se pudo en caliente: al acabar lo que estaba cambiando, una reapertura con todo; el modo ya esta puesto)
                if (!AjEsperarParaAplicar()) return;
                RestartLs(tit, det);
                return;
            }
            if (capCambia) RequestCapture(capV, tit, det);
            else if (modoCambia) SetModeN(modo, tit, det, null);
            else RestartLs(tit, det);
        }

        // ---------------- pintar ----------------
        void AjustesPintar(int fundido)
        {
            VentanaAjustes v = ajVentana;
            if (v == null) return;
            float u = v.U;
            Color acc = PrismaUi.Accent, txt = PrismaUi.TitleColor, det = PrismaUi.DetailColor, oscuro = Color.FromArgb(255, 8, 12, 18);
            using (Graphics g = v.Lienzo())
            using (Font fTit = PrismaUi.TitleFont(34 * u), fSub = PrismaUi.BodyFont(18 * u), fFila = PrismaUi.BodyFont(22 * u),
                        fChip = PrismaUi.BodyFont(19 * u), fChipSel = PrismaUi.TitleFont(19 * u), fNota = PrismaUi.BodyFont(17 * u),
                        fBoton = PrismaUi.TitleFont(21 * u), fPista = PrismaUi.BodyFont(16 * u), fTecla = PrismaUi.TitleFont(13 * u))
            {
                float m = VentanaAjustes.Margen * u;
                RectangleF panel = new RectangleF(m, m, v.W - 2 * m, v.H - 2 * m);
                float rad = 26 * u;
                // Sombra suave
                for (int i = 10; i >= 1; i--)
                    using (GraphicsPath sp = PrismaUi.RoundRect(RectangleF.Inflate(panel, i * 3 * u, i * 3 * u), rad + i * 3 * u))
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(9, 0, 0, 0)))
                        g.FillPath(sb, sp);
                using (GraphicsPath pp = PrismaUi.RoundRect(panel, rad))
                {
                    using (LinearGradientBrush lb = new LinearGradientBrush(panel, Color.FromArgb(255, 32, 36, 46), Color.FromArgb(255, 14, 16, 22), 90f))
                        g.FillPath(lb, pp);
                    // brillo arriba (cristal)
                    RectangleF top = new RectangleF(panel.X, panel.Y, panel.Width, 90 * u);
                    using (LinearGradientBrush gl = new LinearGradientBrush(top, Color.FromArgb(22, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    {
                        Region old = g.Clip; g.SetClip(pp); g.FillRectangle(gl, top); g.Clip = old;
                    }
                    using (Pen bp = new Pen(Color.FromArgb(44, 255, 255, 255), Math.Max(1f, 1.2f * u))) g.DrawPath(bp, pp);
                }
                // Cabecera: icono, titulo, juego y cambios
                float x0 = panel.X + 36 * u, y = panel.Y + 30 * u;
                Icon ic = PrismaUi.AppIcon((int)(44 * u));
                float tx = x0;
                if (ic != null) { using (Bitmap ib = ic.ToBitmap()) g.DrawImage(ib, x0, y + 2 * u, 44 * u, 44 * u); ic.Dispose(); tx += 60 * u; }
                using (SolidBrush b = new SolidBrush(txt)) g.DrawString(Tx.T("Ajustes", "Settings"), fTit, b, tx, y - 4 * u);
                int nc = AjCambios();
                string sub = (curGame ?? Tx.T("Consola", "Console")) + "  ·  " + (nc == 0 ? Tx.T("todo como está", "nothing changed") :
                             nc == 1 ? Tx.T("1 cambio sin aplicar", "1 pending change") : nc + Tx.T(" cambios sin aplicar", " pending changes"));
                if (AjCambiando()) sub += "  ·  " + Tx.T("preparando la imagen…", "preparing the picture…"); // (22-11, 1.2.2: abierto mientras Magpie prepara)
                using (SolidBrush b = new SolidBrush(nc > 0 ? acc : det)) g.DrawString(sub, fSub, b, tx + 2 * u, y + 44 * u);
                y = panel.Y + VentanaAjustes.Cab * u;
                using (Pen sep = new Pen(Color.FromArgb(26, 255, 255, 255), Math.Max(1f, u))) g.DrawLine(sep, panel.X + 28 * u, y - 10 * u, panel.Right - 28 * u, y - 10 * u);

                // Filas
                FilaAjuste foco = AjFilaFoco();
                float rh = VentanaAjustes.Fila * u, colV = VentanaAjustes.ColValor * u, xr = panel.Right - 32 * u;
                StringFormat cen = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                StringFormat izq = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                StringFormat der = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                foreach (FilaAjuste f in ajFilas)
                {
                    if (!f.Visible) continue;   // (1.2.2: la fila del multiplicador, escondida con otros tipos)
                    if (f.Ayuda != null && !f.Activa)
                    {
                        // (1.1.2) fila de aviso: nombre y nota arriba, la explicacion debajo en dos lineas como mucho
                        RectangleF ar = new RectangleF(panel.X + 16 * u, y, panel.Width - 32 * u, rh - 6 * u);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(230, txt))) g.DrawString(f.Nombre, fFila, b, new RectangleF(ar.X + 38 * u, ar.Y, 360 * u, ar.Height), izq);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 236, 170, 70)))
                            g.DrawString(f.Nota, fChipSel, b, new RectangleF(xr - colV, ar.Y, colV - 8 * u, ar.Height), der);
                        RectangleF hr = new RectangleF(ar.X + 38 * u, ar.Bottom - 8 * u, xr - 8 * u - (ar.X + 38 * u), rh);
                        using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisPath })
                        using (SolidBrush b = new SolidBrush(det)) g.DrawString(f.Ayuda, fNota, b, hr, sf);
                        y += 2 * rh;
                        continue;
                    }
                    RectangleF fr = new RectangleF(panel.X + 16 * u, y, panel.Width - 32 * u, rh - 6 * u);
                    bool sel = f == foco;
                    if (sel)
                    {
                        using (GraphicsPath sp = PrismaUi.RoundRect(fr, 14 * u))
                        using (SolidBrush sb = new SolidBrush(Color.FromArgb(34, acc.R, acc.G, acc.B))) g.FillPath(sb, sp);
                        using (GraphicsPath bar = PrismaUi.RoundRect(new RectangleF(fr.X + 6 * u, fr.Y + 14 * u, 4 * u, fr.Height - 28 * u), 2 * u))
                        using (SolidBrush sb = new SolidBrush(acc)) g.FillPath(sb, bar);
                    }
                    if (f.Cambiada)
                        using (SolidBrush sb = new SolidBrush(acc)) g.FillEllipse(sb, fr.X + 20 * u, fr.Y + fr.Height / 2 - 4 * u, 8 * u, 8 * u);
                    Color lc = !f.Activa ? Color.FromArgb(120, det) : sel ? txt : Color.FromArgb(230, txt);
                    using (SolidBrush b = new SolidBrush(lc)) g.DrawString(f.Nombre, fFila, b, new RectangleF(fr.X + 38 * u, fr.Y, 360 * u, fr.Height), izq);
                    RectangleF vr = new RectangleF(xr - colV, fr.Y, colV - 8 * u, fr.Height);
                    if (!f.Activa)
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(150, det))) g.DrawString(f.Nota, fNota, b, vr, der);
                    }
                    else if (f.Carrusel)
                    {
                        float cw = 330 * u, ch = 40 * u;
                        RectangleF cr = new RectangleF(vr.Right - cw, vr.Y + (vr.Height - ch) / 2, cw, ch);
                        using (GraphicsPath cp = PrismaUi.RoundRect(cr, ch / 2))
                        using (SolidBrush cb = new SolidBrush(Color.FromArgb(sel ? 30 : 18, 255, 255, 255))) g.FillPath(cb, cp);
                        string val = f.Sel < f.Txts.Count ? f.Txts[f.Sel] : "";
                        using (SolidBrush b = new SolidBrush(f.Cambiada ? acc : txt)) g.DrawString(val, sel ? fChipSel : fChip, b, cr, cen);
                        using (SolidBrush b = new SolidBrush(sel ? txt : Color.FromArgb(110, det)))
                        {
                            g.DrawString("‹", fBoton, b, new RectangleF(cr.X + 6 * u, cr.Y, 30 * u, cr.Height), cen);
                            g.DrawString("›", fBoton, b, new RectangleF(cr.Right - 36 * u, cr.Y, 30 * u, cr.Height), cen);
                        }
                        if (f.Vals.Count > 1)
                        {
                            // puntitos: cual de cuantos
                            float dw = 6 * u, gap = 6 * u, tot = f.Vals.Count * dw + (f.Vals.Count - 1) * gap, dx = cr.X + (cr.Width - tot) / 2;
                            for (int i = 0; i < f.Vals.Count; i++)
                                using (SolidBrush db = new SolidBrush(i == f.Sel ? acc : Color.FromArgb(60, 255, 255, 255)))
                                    g.FillEllipse(db, dx + i * (dw + gap), cr.Bottom + 3 * u, dw, dw);
                        }
                    }
                    else
                    {
                        int n = f.Vals.Count;
                        float gap = 8 * u, ch = 40 * u;
                        float cw = Math.Min(132 * u, (vr.Width - (n - 1) * gap) / n);
                        float cx = vr.Right - (n * cw + (n - 1) * gap), cy = vr.Y + (vr.Height - ch) / 2;
                        for (int i = 0; i < n; i++)
                        {
                            RectangleF cr = new RectangleF(cx + i * (cw + gap), cy, cw, ch);
                            bool es = i == f.Sel, orig = i == f.Orig && f.Orig != f.Sel;
                            bool bq = f.Bloqueado(i) && !es;   // (1.2.2: bloqueado, gris y con candado; el motivo, en la linea de debajo)
                            using (GraphicsPath cp = PrismaUi.RoundRect(cr, ch / 2))
                            {
                                if (es) using (SolidBrush cb = new SolidBrush(sel ? acc : Color.FromArgb(215, acc.R, acc.G, acc.B))) g.FillPath(cb, cp);
                                else using (SolidBrush cb = new SolidBrush(Color.FromArgb(bq ? 7 : sel ? 26 : 16, 255, 255, 255))) g.FillPath(cb, cp);
                                if (orig) using (Pen op = new Pen(Color.FromArgb(120, acc.R, acc.G, acc.B), Math.Max(1f, 1.5f * u)) { DashStyle = DashStyle.Dash }) g.DrawPath(op, cp);
                                if (bq) using (Pen bp = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1f, 1.2f * u)) { DashStyle = DashStyle.Dot }) g.DrawPath(bp, cp);
                            }
                            if (bq) AjCandado(g, cr, u, Color.FromArgb(110, det));
                            using (SolidBrush b = new SolidBrush(es ? oscuro : bq ? Color.FromArgb(95, det) : sel ? Color.FromArgb(225, txt) : det))
                                g.DrawString(f.Txts[i], es ? fChipSel : fChip, b, bq ? new RectangleF(cr.X + 10 * u, cr.Y, cr.Width - 10 * u, cr.Height) : cr, cen);
                        }
                    }
                    y += rh;
                    // (1.2.2) fila activa con su explicacion debajo (el multiplicador: lo que no cabe y por que)
                    if (f.Ayuda != null && f.Activa)
                    {
                        bool hayBloq = f.Id == "tipo";   // (la del tipo solo sale si lo elegido no puede ir: aviso)
                        for (int i = 0; i < f.Vals.Count; i++) if (f.Bloqueado(i)) hayBloq = true;
                        RectangleF hr = new RectangleF(fr.X + 38 * u, y - 10 * u, xr - 8 * u - (fr.X + 38 * u), rh);
                        using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter })
                        using (SolidBrush b = new SolidBrush(hayBloq ? Color.FromArgb(220, 236, 170, 70) : det)) g.DrawString(f.Ayuda, fNota, b, hr, sf);
                        y += rh;
                    }
                }

                // Pie: Aplicar / Cancelar
                y = panel.Bottom - (VentanaAjustes.Pie - 20) * u;
                float bw = 230 * u, bh = 56 * u, bgap = 18 * u;
                float bx = panel.X + (panel.Width - (2 * bw + bgap)) / 2;
                RectangleF ra = new RectangleF(bx, y, bw, bh), rc = new RectangleF(bx + bw + bgap, y, bw, bh);
                bool hay = nc > 0;
                using (GraphicsPath ap = PrismaUi.RoundRect(ra, bh / 2))
                {
                    using (SolidBrush ab = new SolidBrush(hay ? acc : Color.FromArgb(60, acc.R, acc.G, acc.B))) g.FillPath(ab, ap);
                    using (SolidBrush b = new SolidBrush(hay ? oscuro : Color.FromArgb(200, txt)))
                        g.DrawString(hay ? Tx.T("Aplicar", "Apply") + (nc > 1 ? " (" + nc + ")" : "") : Tx.T("Aplicar", "Apply"), fBoton, b, ra, cen);
                }
                using (GraphicsPath cp = PrismaUi.RoundRect(rc, bh / 2))
                {
                    using (SolidBrush cb = new SolidBrush(Color.FromArgb(22, 255, 255, 255))) g.FillPath(cb, cp);
                    using (Pen cpn = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1f, 1.2f * u))) g.DrawPath(cpn, cp);
                    using (SolidBrush b = new SolidBrush(txt)) g.DrawString(Tx.T("Cancelar", "Cancel"), fBoton, b, rc, cen);
                }
                if (ajPie >= 0)
                {
                    RectangleF fr = RectangleF.Inflate(ajPie == 0 ? ra : rc, 6 * u, 6 * u);
                    using (GraphicsPath fp = PrismaUi.RoundRect(fr, fr.Height / 2))
                    using (Pen fpn = new Pen(Color.FromArgb(235, 255, 255, 255), 2.5f * u)) g.DrawPath(fpn, fp);
                }

                // Pistas: los botones de este mando
                PadKind k = PadKind.XInput;
                try { k = PadHub.Shared.MainKind; } catch { }
                List<string[]> pistas = new List<string[]>
                {
                    new[] { "↕", Tx.T("moverse", "move") },
                    new[] { "↔", Tx.T("cambiar", "change") },
                    new[] { BotonesCe.NombreBoton(k, PadBtn.A), Tx.T("aceptar", "select") },
                    new[] { BotonesCe.NombreBoton(k, PadBtn.B), Tx.T("cerrar", "close") },
                };
                float py = panel.Bottom - 42 * u, kh = 24 * u;
                float total = 0;
                List<float> anchos = new List<float>();
                foreach (string[] p in pistas)
                {
                    float kw = Math.Max(kh, g.MeasureString(p[0], fTecla).Width + 10 * u);
                    float tw = g.MeasureString(p[1], fPista).Width;
                    anchos.Add(kw); total += kw + 6 * u + tw + 22 * u;
                }
                float px = panel.X + (panel.Width - total) / 2;
                for (int i = 0; i < pistas.Count; i++)
                {
                    RectangleF kr = new RectangleF(px, py, anchos[i], kh);
                    using (GraphicsPath kp = PrismaUi.RoundRect(kr, kh / 2))
                    using (SolidBrush kb = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillPath(kb, kp);
                    using (SolidBrush b = new SolidBrush(txt)) g.DrawString(pistas[i][0], fTecla, b, kr, cen);
                    px += anchos[i] + 6 * u;
                    float tw = g.MeasureString(pistas[i][1], fPista).Width;
                    using (SolidBrush b = new SolidBrush(det)) g.DrawString(pistas[i][1], fPista, b, new RectangleF(px, py - 2 * u, tw + 4 * u, kh + 4 * u), izq);
                    px += tw + 22 * u;
                }
                cen.Dispose(); izq.Dispose(); der.Dispose();
            }
            if (fundido >= 0) v.Mostrar((byte)fundido); else v.Repintar();
        }
    }
}
