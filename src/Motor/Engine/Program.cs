using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ConsoleEnhancer.Motor
{
    // Ventana a pantalla completa sin bordes en la tele (la swapchain de flip la ocupa entera: "independent flip")
    class EngineWindow : Form
    {
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        readonly bool topmost;
        // 16-11 (sin telon): enseñarla sin activarla (al despertar detras de Magpie: el primer plano sigue en mpv). Se mira al enseñarla
        public volatile bool NoActivate;
        public EngineWindow(Rectangle bounds, bool topmost, bool activate)
        {
            this.topmost = topmost;
            NoActivate = !activate;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            BackColor = Color.Black;
            // Ventana normal, sin dueño (como la de mpv): asi es la "ventana principal" del proceso para el lanzador (con
            // ShowInTaskbar = false WinForms le pone un dueño oculto y Process.MainWindowHandle da 0)
            ShowInTaskbar = true;
            TopMost = topmost;
            // 15-11: el nombre y el icono de Prisma (prisma.ico va dentro del exe como recurso: build.bat). Nada busca esta ventana por
            // su titulo (el lanzador va por el proceso). 18-11 (MOTOR-CE): "ConsoleEnhancer" (antes "Prisma"); la clase la pone WinForms
            Text = Program.WindowTitle;
            try
            {
                using (System.IO.Stream ico = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Prisma.ico"))
                    if (ico != null) Icon = new Icon(ico);
            }
            catch { }
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            Cursor.Hide();
        }
        // Con el foco: la generacion de NVIDIA se apaga sola si la ventana no lo tiene ("window not focused")
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        // Con el lanzador: sin quitarle el foco a su pantalla de carga (el lanzador le da el foco al quitarla)
        protected override bool ShowWithoutActivation { get { return NoActivate; } }
        protected override CreateParams CreateParams
        {
            get { return base.CreateParams; }
        }
        public bool TakeFocus()
        {
            if (GetForegroundWindow() == Handle) return true;
            Activate();
            if (!SetForegroundWindow(Handle))
            {
                // Windows solo deja pasar el foco tras una pulsacion: ALT suelto (no hace nada en ningun programa). 17-11 (REJAS, de
                // VELOCIDAD): Alt abajo -> SetForegroundWindow -> Alt arriba, como ForceForeground de la app (antes Alt abajo y arriba y
                // despues SetForegroundWindow: keybd_event no espera y el log alternaba "bien / no se pudo")
                keybd_event(0x12, 0, 0, UIntPtr.Zero);
                SetForegroundWindow(Handle);
                keybd_event(0x12, 0, 2, UIntPtr.Zero);
            }
            return GetForegroundWindow() == Handle;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
    }

    static partial class Program   // (18-11, MOTOR-CE: el resto en EngineUniversal.cs)
    {
        public static string Dir = AppDomain.CurrentDomain.BaseDirectory;
        static EngineWindow window;
        static readonly ManualResetEvent windowReady = new ManualResetEvent(false);
        public static volatile bool Quit;
        static readonly long mainAt = Clock.Now;
        // 16-11 (sin telon, docs\STANDBY.md 8): mientras el lanzador pasa de mpv + Magpie al motor o al reves, el primer plano es de mpv
        // (Magpie solo escala la ventana de delante): el motor no lo coge solo (ni al enseñarse ni para recuperar la generacion)
        public static volatile bool FocusHold;
        // switch2-encima: 0 no, 1 encima de todo una vez, 2 encima y reafirmado (ver SetOnTop)
        public static volatile int OnTopMode;
        static bool startTopmost;           // topmost= del arranque (lo de siempre al volver a ponerse delante)
        public static volatile bool ClosedOutside;   // (1.0.11) la ventana la cerro otro (el usuario): se sale con 77

        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

        // La pantalla de la ventana: la principal o monitor=N
        static Rectangle ScreenBounds(Settings s)
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            if (s.Monitor >= 0 && s.Monitor < Screen.AllScreens.Length) bounds = Screen.AllScreens[s.Monitor].Bounds;
            return bounds;
        }

        [MTAThread]
        static int Main(string[] args)
        {
            // Pixeles reales (la tele va al 300 %: sin esto Windows dice 1280x720)
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Settings s = new Settings();
            DataInit(args);   // 18-11 (MOTOR-CE): datos=<carpeta> (el log, Streamline, la cache y los bancos; si no, la del exe)
            Log.Open(Path.Combine(DataDir, "engine.log"));
            if (Array.IndexOf(args, "--nvidia") >= 0) return NvidiaProfile();   // (18-11: solo con NVIDIA y nvapi64.dll; el perfil del exe que sea)
            Log.Write("---- ConsoleEnhancerEngine " + string.Join(" ", args));
            s.ApplyArgs(args);
            ResolveOutput(s);   // 18-11 (MOTOR-CE): out=0 = el modo actual de la pantalla (antes 3840x2160)
            Log.Write("Ajustes: " + s.Describe());

            Rectangle bounds = ScreenBounds(s);
            startTopmost = s.Topmost;
            Thread ui = new Thread(delegate ()
            {
                bool standalone = s.Ipc.Length == 0;
                // Con el foco al aparecer (como mpv). Sin "siempre encima" con el lanzador: su pantalla de carga y su telon quedan delante
                // (16-11, sin telon: la primera vez que se enseña un motor arrancado en espera puede ser detras de Magpie: sin el foco)
                window = new EngineWindow(bounds, s.Topmost, true);
                window.Shown += delegate { if (!FocusHold) window.TakeFocus(); windowReady.Set(); };
                // 20-11 (CIERRES): cerrada desde fuera (Alt+F4 que no vio el lanzador, WM_CLOSE de otro programa): que conste (el motor
                // sale con el codigo 0, como siempre; el lanzador ya no lo toma por "cerrado por el usuario" si no se lo pidio el)
                // 21-11 (1.0.11): y sale con 77 (ClosedOutside): el lanzador cierra la app entera (la barra de tareas cerraba solo la ventana
                // y el lanzador la volvia a abrir). El lanzador nunca cierra la ventana para reabrirla (eso va por la tuberia)
                window.FormClosing += delegate (object o, FormClosingEventArgs a) { if (!Quit) { ClosedOutside = true; Log.Write("Ventana: cerrada desde fuera (" + a.CloseReason + "): el motor se cierra (codigo 77: la app entera)"); } };
                window.FormClosed += delegate { Quit = true; };
                if (s.Standby)
                {
                    // 16-11 (espera, docs\STANDBY.md): la ventana se crea sin enseñarla (ni parpadeo, ni barra de tareas, ni foco):
                    // la swapchain se hace sobre ella y se enseña al despertar (ShowWindow). Bucle de mensajes sin ventana principal
                    // (Application.Run(window) la enseñaria): al cerrarla se acaba a mano
                    window.FormClosed += delegate { Application.ExitThread(); };
                    if (window.Handle == IntPtr.Zero) Log.Write("La ventana (oculta) no se crea");
                    windowReady.Set();
                    Application.Run();
                }
                else Application.Run(window);
            });
            ui.SetApartmentState(ApartmentState.STA);
            ui.IsBackground = true;
            ui.Name = "ventana";
            ui.Start();
            if (!windowReady.WaitOne(5000)) { Log.Write("La ventana no aparece"); return 2; }
            LogWindow();   // 18-11 (MOTOR-CE): titulo y clase

            // 20-11 (CIERRES): un fallo en un hilo secundario, al log (antes el proceso moria sin dejar nada)
            AppDomain.CurrentDomain.UnhandledException += delegate (object o, UnhandledExceptionEventArgs a)
            {
                try { Log.Write("ERROR no controlado en el hilo \"" + Thread.CurrentThread.Name + "\"" + (a.IsTerminating ? " (el motor se cierra)" : "") + ": " + a.ExceptionObject); } catch { }
            };
            Engine e = null;
            int code = 0;
            try
            {
                e = new Engine(s, window.Handle, bounds.Width, bounds.Height);
                e.StartGpuWatch();   // 20-11 (CIERRES): la grafica parada o perdida no es la consola apagada (EngineCuelgue.cs)
                e.Run();   // hasta 'quit', cerrar la ventana o el fin del banco
                code = e.ExitCode;
            }
            catch (Exception ex) { Log.Error("Motor", ex); code = 1; }
            finally
            {
                if (e != null) e.Dispose();
                try { window.BeginInvoke((MethodInvoker)delegate { window.Close(); }); } catch { }
            }
            if (ClosedOutside && code == 0) code = 77;
            Log.Write("Fin (" + code + ")");
            return code;
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        // 17-11 (REJAS): quien tiene el primer plano (proceso, clase, titulo), para el log cuando el motor pierde el foco
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int n);
        public static string ForegroundInfo()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return "nadie";
                uint pid; GetWindowThreadProcessId(h, out pid);
                System.Text.StringBuilder c = new System.Text.StringBuilder(128), t = new System.Text.StringBuilder(128);
                GetClassName(h, c, 128); GetWindowText(h, t, 128);
                string pn = "?"; try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
                return pn + " " + pid + ", " + c + ", \"" + t + "\"";
            }
            catch (Exception ex) { return "? (" + ex.GetType().Name + ")"; }
        }

        public static bool HasFocus() { try { return window != null && GetForegroundWindow() == window.Handle; } catch { return false; } }

        // 21-11 (1.0.11, el usuario: "dentro de la app no me reacciona la tecla de Windows"): el primer plano lo tiene Windows (menu Inicio,
        // buscador, barra de tareas, centro de notificaciones, Alt+Tab, vista de tareas) o nadie (al abrirse Inicio hay un instante sin nadie):
        // es el usuario usando Windows, el foco NO se recupera (antes la generacion lo cogia otra vez a los <= 3 s y el menu Inicio se cerraba)
        public static bool ShellInFront()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return true;
                uint pid; GetWindowThreadProcessId(h, out pid);
                string pn = "";
                try { using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById((int)pid)) pn = p.ProcessName; } catch { }
                return EsShell(pn);
            }
            catch { return false; }
        }

        public static bool EsShell(string proceso)
        {
            switch ((proceso ?? "").ToLowerInvariant())
            {
                case "explorer": case "startmenuexperiencehost": case "searchhost": case "searchapp": case "shellexperiencehost":
                case "shellhost": case "textinputhost": case "lockapp": case "taskmgr":
                    return true;
                default: return false;
            }
        }

        public static bool Focus()
        {
            bool ok = false;
            try { window.Invoke((MethodInvoker)delegate { ok = window.TakeFocus(); }); } catch { }
            return ok;
        }

        public static void CloseWindow()
        {
            Quit = true;
            try { if (window != null) window.BeginInvoke((MethodInvoker)delegate { window.Close(); }); } catch { }
        }

        // 16-11 (espera): esconder la ventana (el lanzador pasa a mpv + Magpie: sin barra de tareas ni foco) y enseñarla al
        // despertar como al arrancar (pantalla completa en la tele, con el foco; encima o no segun topmost). Se llaman desde el
        // hilo de pintado y se hacen en el de la ventana (como mucho 2 s de espera)
        public static bool HideWindow() { return OnWindow(delegate { window.Hide(); }, "esconder"); }

        // shown: en el hilo de la ventana, ya enseñada (aunque sea pasados los 2 s: el motor no pone la generacion hasta entonces)
        // behind (16-11, sin telon: switch2-wake detras): sin activarse, al fondo de todo y sin coger el foco despues (FocusHold): Magpie
        // sigue a la vista y mpv con el primer plano hasta que el lanzador pide switch2-delante
        public static bool ShowWindow(Settings s, MethodInvoker shown, bool behind = false)
        {
            Rectangle b = ScreenBounds(s);
            return OnWindow(delegate
            {
                if (window.Bounds != b)
                {
                    Log.Write("Ventana: la pantalla es ahora " + b.Width + "x" + b.Height + " en " + b.X + "," + b.Y + " (la ventana estaba en " +
                              window.Bounds.Width + "x" + window.Bounds.Height + " en " + window.Bounds.X + "," + window.Bounds.Y + ")");
                    window.Bounds = b;
                }
                StopReassert();
                OnTopMode = 0;
                if (behind)
                {
                    FocusHold = true;
                    window.NoActivate = true;
                    try { window.Show(); }
                    finally { window.NoActivate = false; }
                    // (HWND_BOTTOM: debajo de todo, tambien de la ventana de mpv; quita "siempre encima" si lo tuviera)
                    SetWindowPos(window.Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                else
                {
                    FocusHold = false;
                    window.Show();
                    window.TakeFocus();
                }
                if (shown != null) shown();
            }, behind ? "enseñar detras" : "enseñar");
        }

        // ---- 16-11: sin telon (switch2-delante / switch2-encima, docs\STANDBY.md 8) ----
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint evMin, uint evMax, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        static string ClassOf(IntPtr h) { System.Text.StringBuilder sb = new System.Text.StringBuilder(96); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        static readonly IntPtr HWND_TOP = IntPtr.Zero, HWND_BOTTOM = new IntPtr(1), HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        const uint GW_HWNDPREV = 3;
        const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_OBJECT_SHOW = 0x8002, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
        static System.Windows.Forms.Timer reassertTimer;
        static IntPtr hookShow, hookFg;
        static WinEventProc hookProc;       // (referenciado: si lo recoge el GC, el gancho se cae)
        static int reasserts;

        // switch2-delante: el lanzador ya ha quitado Magpie (o lo va a quitar): delante de todo lo que no es "siempre encima", con el foco
        // (la generacion de NVIDIA se apaga sola sin el) y otra vez recuperandolo solo
        public static bool Front()
        {
            return OnWindow(delegate
            {
                StopReassert();
                OnTopMode = 0;
                FocusHold = false;
                if (!startTopmost) SetWindowPos(window.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                SetWindowPos(window.Handle, startTopmost ? HWND_TOPMOST : HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                window.TakeFocus();
            }, "ponerla delante");
        }

        // switch2-encima: 1 = por encima de todo, tambien de las ventanas "siempre encima" que salgan despues (la de escalado de Magpie),
        // sin activarse ni coger el foco (mpv lo necesita para que Magpie escale); 2 = igual y reafirmado (cada 15 ms y en cuanto otro
        // proceso enseña una ventana o cambia el primer plano) hasta el 0; 0 = deja de estarlo y se va al fondo de todo (lo que haya
        // encima ya se ve; switch2-standby despues no enseña su imagen negra)
        public static bool SetOnTop(int mode)
        {
            return OnWindow(delegate
            {
                OnTopMode = mode;
                if (mode > 0)
                {
                    FocusHold = true;
                    SetWindowPos(window.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    if (mode >= 2) StartReassert(); else StopReassert();
                }
                else
                {
                    StopReassert();
                    // (HWND_BOTTOM le quita "siempre encima" y la pone debajo de todas)
                    SetWindowPos(window.Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }, "encima " + mode);
        }

        // (en el hilo de la ventana)
        static void StartReassert()
        {
            reasserts = 0;
            if (reassertTimer == null)
            {
                reassertTimer = new System.Windows.Forms.Timer();
                reassertTimer.Interval = 15;
                reassertTimer.Tick += delegate { Reassert(false); };
            }
            reassertTimer.Start();
            if (hookProc == null) hookProc = delegate (IntPtr hk, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
            {
                if (idObject == 0 && OnTopMode >= 2) Reassert(true);
            };
            if (hookShow == IntPtr.Zero)
                hookShow = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, hookProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            if (hookFg == IntPtr.Zero)
                hookFg = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, hookProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }

        static void StopReassert()
        {
            if (reassertTimer != null) reassertTimer.Stop();
            if (hookShow != IntPtr.Zero) { UnhookWinEvent(hookShow); hookShow = IntPtr.Zero; }
            if (hookFg != IntPtr.Zero) { UnhookWinEvent(hookFg); hookFg = IntPtr.Zero; }
            if (reasserts > 0) Log.Write("Ventana: encima de todo reafirmada " + reasserts + " veces");
            reasserts = 0;
        }

        // Otra vez encima si hay alguna ventana visible por encima (always: sin mirar, tras un aviso de que otra se ha enseñado)
        static void Reassert(bool always)
        {
            try
            {
                if (window == null || window.IsDisposed || OnTopMode < 2) return;
                IntPtr h = window.Handle;
                bool above = always;
                IntPtr w = h;
                for (int i = 0; i < 64 && !above; i++)
                {
                    w = GetWindow(w, GW_HWNDPREV);
                    if (w == IntPtr.Zero) break;
                    // (16-11: solo las que pueden tapar algo: visibles, no ocultas por DWM y de mas de 64x64; con las pequeñas o
                    // invisibles de encima de todo -avisos, ventanas del sistema- se reafirmaba cada 15 ms sin que tapasen nada)
                    if (!IsWindowVisible(w)) continue;
                    int cloaked;
                    if (DwmGetWindowAttribute(w, 14, out cloaked, 4) == 0 && cloaked != 0) continue;   // DWMWA_CLOAKED
                    RECT r;
                    if (!GetWindowRect(w, out r) || r.R - r.L < 64 || r.B - r.T < 64) continue;
                    above = true;
                    if (reasserts == 0) Log.Write("Ventana: encima de todo otra vez (por encima estaba " + ClassOf(w) + " " + r.L + "," + r.T + "-" + r.R + "," + r.B + ")");
                }
                if (!above) return;
                SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                reasserts++;
            }
            catch { }
        }

        static bool OnWindow(MethodInvoker m, string what)
        {
            try
            {
                if (window == null || window.IsDisposed) return false;
                IAsyncResult ar = window.BeginInvoke(m);
                if (!ar.AsyncWaitHandle.WaitOne(2000)) { Log.Write("Ventana: no se pudo " + what + " a tiempo (2 s)"); return false; }
                window.EndInvoke(ar);
                return true;
            }
            catch (Exception ex) { Log.Write("Ventana (" + what + "): " + ex.Message); return false; }
        }

        // Desde que arranco el proceso (para switch2-standby-ready)
        public static double SinceStartMs()
        {
            try { return (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds; }
            catch { return Clock.SinceMs(mainAt); }
        }
    }
}
