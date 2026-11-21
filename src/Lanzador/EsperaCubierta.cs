// EsperaCubierta.cs - 21-11-2026 (1.2.1, el usuario: "Al apagar la consola con el modo de consola en la app, se ve la barra de tareas,
// evita eso"). Lo que tapa Windows desde que se ve la consola apagada (apagar_pc=1) hasta que vuelve su imagen.
//
// Por que se veia la barra (consoleenhancer.log del 21-11 19:08:58): PowerOffNow mataba mpv / Magpie / el motor (la imagen a pantalla
// completa) sin nada delante, y la pantalla no se apagaba hasta ~1,2 s despues (fin de la app, "Fin ConsoleEnhancer", la espera arrancando
// la sonda y los mandos). En ese hueco se veia el escritorio negro... y la barra de tareas: en ocultacion automatica (escritorio_negro) se
// despliega en cuanto se va la ventana a pantalla completa de delante (medido aqui con el cursor en el centro: de [0,2157] a [0,2016] al
// pasar el primer plano de una ventana a pantalla completa a otra) y se queda desplegada si el cursor esta en el borde de abajo (con el
// cursor en y=2159, 0,3 s despues de cerrarse la ultima). Y el cursor, que mpv solo escondia sobre su ventana. Al despertar, el telon de
// --inicio esperaba a que la app vieja soltara el mutex (otro hueco con la pantalla ya encendida) y se quitaba con la pantalla de carga aun
// transparente (empieza con Opacity 0): el escritorio y el cursor ~0,3 s.
//
//  - La cubierta (CeCubierta): ventanas negras encima de todo, una por pantalla (EnumDisplayMonitors), sin cursor (WM_SETCURSOR sin
//    cursor y ShowCursor) y que no se cierran con Alt+F4, en su propio hilo: sigue puesta aunque termine el Application.Run de la app
//    ("Fin ConsoleEnhancer"). Con la pantalla encendida se vuelve a poner encima cada 500 ms (y la barra otra vez oculta si el Explorador
//    la ha vuelto a mostrar); con la pantalla apagada no hace nada (ni un temporizador: lo dice GUID_CONSOLE_DISPLAY_STATE). Se intenta
//    que sea la ventana de delante: a pantalla completa, Windows la toma por una app a pantalla completa (la barra detras y los avisos en
//    "no molestar": SHQueryUserNotificationState = QUNS_BUSY).
//  - La barra de tareas (CeBarra): Shell_TrayWnd y las Shell_SecondaryTrayWnd ocultas (ShowWindow) mientras tanto, solo con apagar_pc=1
//    (nunca en el modo de PC normal) y espera_ocultar_barra=1 (de serie). Vuelve siempre: al volver la imagen de la consola, al cerrar la
//    app, al pasar a "como un PC normal", al desinstalar y, si la app muere, la "guarda": un proceso pequeno (--ce-guarda-barra, sin
//    ventanas ni temporizadores) que espera a que termine el proceso dueno de la marca data\.taskbar_hidden.txt y, si nadie la ha cogido
//    (la espera, el --inicio y la app se la pasan: pid= de la marca), la vuelve a mostrar (y el plan de energia de antes). Ademas, al abrir
//    la app con la marca de un proceso que ya no esta, se muestra (si no viene de la espera). Ocultar no se guarda en Windows: reiniciado el
//    Explorador, vuelve. Y el Explorador la vuelve a mostrar el solo cuando se va la ventana a pantalla completa de delante (medido: 0,11 s
//    despues): la cubierta (con la pantalla encendida), el telon de --inicio y la app (hasta ver la consola) la vuelven a ocultar.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace ConsoleEnhancer
{
    static class CeCubierta
    {
        static readonly object sync = new object();
        static Thread hilo;
        static Cubre raiz;                                  // la de la pantalla principal: recibe los mensajes (pantalla, cambios de pantalla)
        static readonly List<Cubre> otras = new List<Cubre>();
        static readonly ManualResetEvent creada = new ManualResetEvent(false);
        static System.Windows.Forms.Timer arriba;
        static volatile bool puesta, pausada, cursorOculto;
        static volatile int pantalla = -1;                  // GUID_CONSOLE_DISPLAY_STATE: 0 apagada, 1 encendida, 2 atenuada (-1 sin saber)
        static DateTime pantallaDesde = DateTime.UtcNow;
        static string porque = "";
        public static volatile Action<int> AlCambiarPantalla;  // (la espera: la pantalla encendida sin que nadie toque nada -> otra vez apagada)

        public static bool Puesta { get { return puesta; } }
        public static int Pantalla { get { return pantalla; } }
        public static volatile int Hilo;   // (el id de su hilo de Windows, para medir lo que despierta en espera)
        public static IntPtr Handle { get { Cubre r = raiz; return r != null && r.IsHandleCreated ? r.Handle : IntPtr.Zero; } }

        // El hilo y la ventana (oculta) de antemano: en el apagado Poner es inmediato (nada que crear ni compilar)
        public static void Preparar()
        {
            lock (sync)
            {
                if (hilo == null)
                {
                    hilo = new Thread(Bucle);
                    hilo.IsBackground = true;
                    hilo.SetApartmentState(ApartmentState.STA);
                    hilo.Name = "Cubierta negra";
                    hilo.Start();
                }
            }
            creada.WaitOne(5000);
        }

        static void Bucle()
        {
            try
            {
#pragma warning disable 618
                Hilo = AppDomain.GetCurrentThreadId();
#pragma warning restore 618
                raiz = new Cubre(true);
                IntPtr h = raiz.Handle;   // (crea la ventana, sin mostrarla)
                Guid g = GuidPantalla;
                RegisterPowerSettingNotification(h, ref g, 0); // (llega enseguida el estado de ahora)
                arriba = new System.Windows.Forms.Timer();
                arriba.Interval = 500;
                arriba.Tick += delegate { Subir(false); };
            }
            catch (Exception ex) { Log.Error("Cubierta negra: creando la ventana", ex); raiz = null; }
            creada.Set();
            if (raiz == null) return;
            try { Application.Run(); }   // (hilo de fondo: hasta que termina el proceso)
            catch (Exception ex) { Log.Error("Cubierta negra", ex); }
        }

        // Ponerla (si no lo esta) y esperar a que se vea (como mucho 1 s). activar = que sea la ventana de delante
        public static bool Poner(string por, bool activar)
        {
            Preparar();
            Cubre r = raiz;
            if (r == null) return false;
            bool ya = puesta;
            Stopwatch sw = Stopwatch.StartNew();
            ManualResetEvent hecho = new ManualResetEvent(false);
            string delante = "";
            try
            {
                r.BeginInvoke((MethodInvoker)delegate
                {
                    try { Mostrar(activar); delante = Delante(); }
                    catch (Exception ex) { Log.Error("Cubierta negra: poniendola", ex); }
                    hecho.Set();
                });
            }
            catch (Exception ex) { Log.Error("Cubierta negra: poniendola", ex); return false; }
            bool ok = hecho.WaitOne(1000);
            porque = por;
            if (!ya) Log.Write("Cubierta negra puesta (" + por + "): " + (otras.Count + 1) + " pantalla" + (otras.Count > 0 ? "s" : "") + ", sin cursor, en " +
                               sw.ElapsedMilliseconds + " ms" + (ok ? "" : " (sin confirmar)") + (activar ? "; delante: " + delante : ""));
            return ok;
        }

        public static void Quitar(string por)
        {
            Cubre r = raiz;
            if (r == null || !puesta) return;
            ManualResetEvent hecho = new ManualResetEvent(false);
            try
            {
                r.BeginInvoke((MethodInvoker)delegate
                {
                    try { Esconder(); } catch (Exception ex) { Log.Error("Cubierta negra: quitandola", ex); }
                    hecho.Set();
                });
                hecho.WaitOne(1000);
            }
            catch (Exception ex) { Log.Error("Cubierta negra: quitandola", ex); }
            Log.Write("Cubierta negra quitada (" + por + ")");
        }

        // Con el aviso de apagado delante (suspender / apagar): no se vuelve a poner encima de el
        public static void Pausa(bool si)
        {
            pausada = si;
            Cubre r = raiz;
            if (r == null || !puesta) return;
            try { r.BeginInvoke((MethodInvoker)delegate { Reloj(); if (!si) Subir(false); }); } catch { }
        }

        // ---------------- en el hilo de la cubierta ----------------

        static void Mostrar(bool activar)
        {
            Recolocar();
            puesta = true;
            foreach (Cubre c in Todas()) if (!c.Visible) c.Show();
            if (!cursorOculto) { ShowCursor(false); cursorOculto = true; }
            SetCursor(IntPtr.Zero);
            Subir(activar);
            foreach (Cubre c in Todas()) c.Update();
            Reloj();
        }

        static void Esconder()
        {
            puesta = false;
            Reloj();
            foreach (Cubre c in Todas()) if (c.Visible) c.Hide();
            if (cursorOculto) { ShowCursor(true); cursorOculto = false; }
        }

        static List<Cubre> Todas()
        {
            List<Cubre> l = new List<Cubre>();
            if (raiz != null) l.Add(raiz);
            l.AddRange(otras);
            return l;
        }

        // Encima de todo otra vez (y la barra, si ha vuelto, otra vez oculta). Solo si algo se le ha puesto encima (lo que hay en su
        // centro no es ella): sin mover nada, nada que componer
        static void Subir(bool activar)
        {
            if (!puesta) return;
            if (!pausada)
                foreach (Cubre c in Todas())
                {
                    Rectangle b = c.Bounds;
                    POINT p = new POINT(); p.X = b.X + b.Width / 2; p.Y = b.Y + b.Height / 2;
                    if (activar || GetAncestor(WindowFromPoint(p), 2) != c.Handle)
                        SetWindowPos(c.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // HWND_TOPMOST, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
                }
            CeBarra.Reocultar();
            // (sin nada delante, o el escritorio o la barra: como al matar la imagen de la consola) la cubierta delante: a pantalla
            // completa, el Explorador deja la barra detras y Windows pone "no molestar"
            if (!activar && !pausada && raiz != null)
            {
                IntPtr f = GetForegroundWindow();
                if (f == IntPtr.Zero) activar = true;
                else
                {
                    StringBuilder sb = new StringBuilder(32);
                    GetClassName(f, sb, 32);
                    string c = sb.ToString();
                    activar = c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd";
                }
            }
            if (activar && raiz != null && GetForegroundWindow() != raiz.Handle)
            {
                try { raiz.Activate(); } catch { }
                SetForegroundWindow(raiz.Handle);
            }
        }

        // El temporizador solo con la cubierta puesta, sin pausa y la pantalla encendida (o sin saber)
        static void Reloj()
        {
            if (arriba == null) return;
            bool on = puesta && !pausada && pantalla != 0;
            if (on && !arriba.Enabled) arriba.Start();
            else if (!on && arriba.Enabled) arriba.Stop();
        }

        // Una ventana por pantalla (las de ahora: tambien al conectar o quitar una, o al cambiar la resolucion)
        static void Recolocar()
        {
            List<Rectangle> pant = Pantallas();
            Rectangle prin = Screen.PrimaryScreen.Bounds;
            foreach (Rectangle p in pant) if (p.Contains(0, 0)) prin = p;
            if (raiz.Bounds != prin) raiz.Bounds = prin;
            int k = 0;
            foreach (Rectangle p in pant)
            {
                if (p == prin) continue;
                if (k >= otras.Count) otras.Add(new Cubre(false));
                if (otras[k].Bounds != p) otras[k].Bounds = p;
                if (puesta && !otras[k].Visible) otras[k].Show();
                k++;
            }
            while (otras.Count > k) { Cubre c = otras[otras.Count - 1]; c.Cerrar = true; c.Close(); otras.RemoveAt(otras.Count - 1); }
        }

        static List<Rectangle> Pantallas()
        {
            List<Rectangle> r = new List<Rectangle>();
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr m, IntPtr dc, ref RECT rc, IntPtr d)
                {
                    r.Add(Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom));
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            if (r.Count == 0) foreach (Screen s in Screen.AllScreens) r.Add(s.Bounds);
            return r;
        }

        static void AlMensaje(ref Message m)
        {
            if (m.Msg == 0x007E) { if (puesta) { Recolocar(); Subir(false); } }   // WM_DISPLAYCHANGE
            else if (m.Msg == 0x0218 && (long)m.WParam == 0x8013)                // WM_POWERBROADCAST, PBT_POWERSETTINGCHANGE
            {
                try
                {
                    Guid g = (Guid)Marshal.PtrToStructure(m.LParam, typeof(Guid));
                    if (g != GuidPantalla) return;
                    int v = Marshal.ReadInt32(m.LParam, 20);
                    if (v == pantalla) return;
                    bool primera = pantalla < 0;
                    pantalla = v;
                    pantallaDesde = DateTime.UtcNow;
                    if (!primera && puesta) Log.Write("Cubierta negra: pantalla " + (v == 0 ? "apagada" : v == 1 ? "encendida" : "atenuada"));
                    Reloj();
                    if (v != 0) Subir(false);   // (encendida: lo primero, encima otra vez y la barra oculta)
                    Action<int> f = AlCambiarPantalla;
                    if (f != null && !primera) f(v);
                }
                catch { }
            }
        }

        static string Delante()
        {
            IntPtr f = GetForegroundWindow();
            if (raiz != null && f == raiz.Handle) return "la cubierta";
            if (f == IntPtr.Zero) return "nada";
            StringBuilder sb = new StringBuilder(64);
            GetClassName(f, sb, 64);
            uint pid;
            GetWindowThreadProcessId(f, out pid);
            return sb + " (pid " + pid + ")";
        }

        sealed class Cubre : Form
        {
            readonly bool esRaiz;
            public bool Cerrar;   // (Close() tambien llega como UserClosing: solo se deja con esto)
            public Cubre(bool esRaiz)
            {
                this.esRaiz = esRaiz;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                BackColor = Color.Black;
                TopMost = true;
                Text = "ConsoleEnhancer";
                Bounds = Screen.PrimaryScreen.Bounds;
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x00000080 | 0x00000008; // WS_EX_TOOLWINDOW (ni barra ni Alt+Tab) | WS_EX_TOPMOST
                    return cp;
                }
            }
            protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Color.Black); }
            protected override void OnPaint(PaintEventArgs e) { }
            // Alt+F4 (o el mando haciendo de teclado) no la quita: solo Quitar
            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing && !Cerrar) e.Cancel = true;
                base.OnFormClosing(e);
            }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0020) { SetCursor(IntPtr.Zero); m.Result = (IntPtr)1; return; } // WM_SETCURSOR: sin cursor
                if (esRaiz) AlMensaje(ref m);
                base.WndProc(ref m);
            }
        }

        // (1.2.1) Abrir otro proceso de la app sin el cursor de "iniciando" (la flecha con el reloj que Windows pone hasta 5 s al lanzar un
        // programa: visto un instante encima del telon al despertar). CreateProcess con STARTF_FORCEOFFFEEDBACK; si falla, Process.Start
        public static Process Lanzar(string exe, string args, bool sinVentana)
        {
            try
            {
                STARTUPINFO si = new STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                si.dwFlags = 0x80; // STARTF_FORCEOFFFEEDBACK
                PROCESS_INFORMATION pi;
                StringBuilder cmd = new StringBuilder("\"" + exe + "\" " + args);
                if (CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, sinVentana ? 0x08000000u : 0u, IntPtr.Zero, Path.GetDirectoryName(exe), ref si, out pi)) // CREATE_NO_WINDOW
                {
                    CloseHandle(pi.hThread);
                    try { return Process.GetProcessById(pi.dwProcessId); }
                    catch { return null; }
                    finally { CloseHandle(pi.hProcess); }
                }
            }
            catch { }
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = sinVentana;
            psi.WorkingDirectory = Path.GetDirectoryName(exe);
            return Process.Start(psi);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        static readonly Guid GuidPantalla = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47"); // GUID_CONSOLE_DISPLAY_STATE
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        delegate bool MonitorEnumProc(IntPtr m, IntPtr dc, ref RECT rc, IntPtr d);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc f, IntPtr d);
        [DllImport("user32.dll")] static extern IntPtr RegisterPowerSettingNotification(IntPtr h, ref Guid g, int flags);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr c);
        [DllImport("user32.dll")] static extern int ShowCursor(bool show);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    }

    static class CeBarra
    {
        // espera_ocultar_barra (1 de serie; 0 = la barra no se toca, solo la cubierta negra)
        public static volatile bool Permitido = true;
        static readonly object sync = new object();
        // (con punto delante: ni la copia de seguridad de los datos ni una version nueva que herede los datos la cogen)
        static string Marca { get { return Path.Combine(Paths.Datos, ".taskbar_hidden.txt"); } }
        const string EventoVuelta = "Local\\ConsoleEnhancer_BarraVuelta";
        const string MutexGuarda = "Local\\ConsoleEnhancer_GuardaBarra";
        static int reocultadas;

        public static bool Oculta { get { try { return File.Exists(Marca); } catch { return false; } } }

        static List<IntPtr> Ventanas()
        {
            List<IntPtr> r = new List<IntPtr>();
            IntPtr t = FindWindow("Shell_TrayWnd", null);
            if (t != IntPtr.Zero) r.Add(t);
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                StringBuilder sb = new StringBuilder(64);
                GetClassName(h, sb, 64);
                if (sb.ToString() == "Shell_SecondaryTrayWnd") r.Add(h);
                return true;
            }, IntPtr.Zero);
            return r;
        }

        // Oculta (y apunta la marca con este proceso de dueno; arranca la guarda)
        public static void Ocultar(string porque)
        {
            if (!Permitido) return;
            lock (sync)
            {
                bool ya = Oculta;
                // (si alguien ya tenia la principal oculta antes, con otro programa, al volver se deja como estaba)
                if (!ya) { IntPtr t = FindWindow("Shell_TrayWnd", null); antesOculta = t != IntPtr.Zero && !IsWindowVisible(t); }
                Apuntar(porque);
                try { using (EventWaitHandle e = new EventWaitHandle(false, EventResetMode.ManualReset, EventoVuelta)) e.Reset(); } catch { }
                int n = 0, total = 0;
                foreach (IntPtr h in Ventanas()) { total++; if (IsWindowVisible(h)) { ShowWindow(h, 0); n++; } } // SW_HIDE
                if (!ya || n > 0) Log.Write("Barra de tareas: oculta (" + porque + "; " + n + " de " + total + " ventanas)");
                Guarda();
            }
        }

        // La cubierta, con la pantalla encendida: si el Explorador la ha vuelto a mostrar (reiniciado, una pantalla nueva), otra vez oculta
        public static void Reocultar()
        {
            if (!Permitido || !Oculta) return;
            int n = 0;
            foreach (IntPtr h in Ventanas()) if (IsWindowVisible(h)) { ShowWindow(h, 0); n++; }
            if (n > 0 && reocultadas++ < 20) Log.Write("Barra de tareas: habia vuelto a salir (" + n + " ventanas): otra vez oculta");
        }

        // Otra vez visible (si la habia ocultado la app) y fuera la marca
        public static void Mostrar(string porque)
        {
            lock (sync)
            {
                if (!Oculta) return;
                if (GetSystemMetrics(0x2000) != 0) { Log.Write("Barra de tareas: Windows se esta apagando; se queda como esta (" + porque + ")"); return; } // SM_SHUTTINGDOWN
                int n = 0;
                bool dejar = LeerAntesOculta();
                IntPtr prin = FindWindow("Shell_TrayWnd", null);
                foreach (IntPtr h in Ventanas()) if (!IsWindowVisible(h) && !(dejar && h == prin)) { ShowWindow(h, 8); n++; } // SW_SHOWNA (sin quitar el primer plano)
                try { File.Delete(Marca); } catch { }
                try { using (EventWaitHandle e = new EventWaitHandle(false, EventResetMode.ManualReset, EventoVuelta)) e.Set(); } catch { }
                Log.Write("Barra de tareas: otra vez visible (" + porque + (n > 0 ? "" : "; ya lo estaba") + ")");
            }
        }

        // Este proceso pasa a ser el dueno de la marca (la espera, el --inicio al despertar, la app abierta desde la espera)
        public static void Adoptar(string porque)
        {
            lock (sync)
            {
                if (!Oculta) return;
                Apuntar(porque);
                Guarda();
            }
        }

        // Al abrir la app: la marca de otro proceso que ya no esta (la app murio con la barra oculta) -> visible; abierta desde la espera, suya
        public static void AlAbrir(bool desdeEspera)
        {
            try
            {
                if (!Oculta) return;
                int pid = Dueno();
                if (desdeEspera) { Adoptar("abierta desde la espera: vuelve al verse la imagen de la consola"); return; }
                if (pid != Process.GetCurrentProcess().Id && EsDeLaApp(pid)) return;
                Mostrar("al abrir la app: se quedo oculta de una sesion que no termino bien (pid " + pid + ")");
            }
            catch (Exception ex) { Log.Error("Barra de tareas al abrir", ex); }
        }

        static void Apuntar(string porque)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Marca));
                bool antes = Oculta ? LeerAntesOculta() : antesOculta;
                File.WriteAllText(Marca, "# La barra de tareas la ha ocultado ConsoleEnhancer (consola apagada / espera); vuelve sola. A mano: ConsoleEnhancer.exe --restaurar-escritorio\r\n" +
                                  "pid=" + Process.GetCurrentProcess().Id + "\r\nhora=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\nporque=" + porque + "\r\n" +
                                  (antes ? "antes_oculta=1\r\n" : ""), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Error("Barra de tareas: apuntando la marca", ex); }
        }

        static bool antesOculta;

        static bool LeerAntesOculta()
        {
            try { foreach (string l in File.ReadAllLines(Marca)) if (l.Trim() == "antes_oculta=1") return true; }
            catch { }
            return false;
        }

        static int Dueno()
        {
            try
            {
                foreach (string l in File.ReadAllLines(Marca))
                    if (l.StartsWith("pid=")) { int p; if (int.TryParse(l.Substring(4).Trim(), out p)) return p; }
            }
            catch { }
            return 0;
        }

        static bool EsDeLaApp(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return !p.HasExited && p.ProcessName.Equals(Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static string Exe { get { return Path.GetFullPath(System.Reflection.Assembly.GetEntryAssembly().Location); } }

        // La guarda (si no esta ya): --datos= de esta carpeta de datos (asi ademas no hace la copia de seguridad cada 2 min)
        static void Guarda()
        {
            try
            {
                Mutex m;
                if (Mutex.TryOpenExisting(MutexGuarda, out m)) { m.Dispose(); return; }
                using (CeCubierta.Lanzar(Exe, "--ce-guarda-barra \"--datos=" + Paths.Datos + "\"", true)) { }
            }
            catch (Exception ex) { Log.Error("Barra de tareas: la guarda", ex); }
        }

        // --ce-guarda-barra: espera al dueno de la marca; si termina y en 4 s nadie la coge, la barra otra vez visible
        public static int Guardian()
        {
            bool creado;
            using (Mutex m = new Mutex(true, MutexGuarda, out creado))
            {
                if (!creado) return 0;
                try { using (Process me = Process.GetCurrentProcess()) me.PriorityClass = ProcessPriorityClass.Idle; } catch { }
                using (EventWaitHandle vuelta = new EventWaitHandle(false, EventResetMode.ManualReset, EventoVuelta))
                {
                    int vigilado = -1;
                    while (true)
                    {
                        if (!Oculta) return 0;
                        int pid = Dueno();
                        Process p = null;
                        try { if (EsDeLaApp(pid)) p = Process.GetProcessById(pid); } catch { p = null; }
                        if (p == null)
                        {
                            if (vuelta.WaitOne(4000) || !Oculta) return 0;
                            if (Dueno() != pid) continue; // (otro proceso de la app la ha cogido)
                            if (GetSystemMetrics(0x2000) != 0) return 0;
                            Mostrar("la guarda: el proceso " + pid + " de la app ha terminado sin volver a mostrarla");
                            // (si era la espera, tampoco ha vuelto al plan de energia de antes: el procesador seguiria al 50 %)
                            CeEspera.PlanDeAntes("la guarda: la espera termino sin volver al plan de antes");
                            return 0;
                        }
                        if (pid != vigilado) { vigilado = pid; Log.Write("Barra de tareas: la guarda (pid " + Process.GetCurrentProcess().Id + ") vigila el proceso " + pid); }
                        try
                        {
                            using (p)
                            using (ManualResetEvent fin = new ManualResetEvent(false))
                            {
                                fin.SafeWaitHandle = new SafeWaitHandle(p.Handle, false);
                                if (WaitHandle.WaitAny(new WaitHandle[] { fin, vuelta }) == 1) return 0;
                            }
                        }
                        catch { if (vuelta.WaitOne(2000)) return 0; } // (sin poder esperar a ese proceso: se mira cada 2 s)
                    }
                }
            }
        }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    }
}
