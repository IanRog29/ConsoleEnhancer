// InicioWindows.cs - iniciar con Windows y apagar el PC al apagar la consola (19-11-2026, agente CE). Lo mismo que hace Prisma con
// inicio\PrismaInicio.exe + la tarea "Prisma - inicio" y apagar_pc=1, pero dentro del propio exe y con un aviso que se puede cancelar.
//
//  - iniciar_con_windows=1: tarea programada "ConsoleEnhancer - inicio" (al iniciar sesion ESTE usuario; sin admin: su propio token y
//    privilegios minimos) -> ConsoleEnhancer.exe --inicio. Si el Programador de tareas no la deja crear, la clave Run de HKCU (arranca
//    algo mas tarde). La app la pone o la quita al arrancar segun el ini, solo si la clave esta en el ini; con 0 solo quita la suya (si
//    la tarea es de otra copia de la app no se toca). Tambien el menu de la bandeja, el ultimo paso del asistente y la linea de
//    comandos: --iniciar-con-windows=1|0 (pone o quita y sale; codigo 0 = hecho)
//  - --inicio: telon negro al instante (encima de todo, sin cursor), abre la app (otro proceso con los mismos argumentos, p. ej.
//    --datos=) con prioridad por encima de lo normal mientras arranca, y quita el negro al ver su pantalla de carga, cualquier ventana
//    suya (el asistente, un aviso), si se cierra o a los 30 s. Log: logs\inicio.log. Si la app ya esta abierta no hace nada
//  - apagar_pc=1 (la clave de Prisma): solo con el cierre por consola apagada (Alt+F4 o Salir no apagan). apagar_pc_aviso_s (15 de
//    serie): antes, una pantalla con la cuenta atras; cualquier boton del mando, tecla, clic o movimiento del raton lo cancela.
//    0 = sin aviso, como Prisma. shutdown /s /hybrid /f /t 0 (con /hybrid solo si el inicio rapido de Windows esta activo)
//  - Pruebas sin apagar nada: --prueba-apagado[=<s>[;fuera]] (la cuenta atras de verdad y al acabar solo el log "SIMULADO"; ;fuera =
//    la ventana fuera de la pantalla), --ui-preview=apagado;<carpeta> (imagenes del aviso sin mostrarlo), --inicio --inicio-prueba
//    (el telon fuera de la pantalla)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // iniciar_con_windows: -1 = la clave no esta en el ini (no se toca la tarea), 0 / 1
        public int StartWithWindows = -1;
        // apagar_pc_aviso_s: segundos de la cuenta atras antes de apagar el PC (0 = sin aviso, como Prisma)
        public int ShutdownWarnSecs = 15;

        bool CeParseInicio(string key, string val)
        {
            switch (key)
            {
                case "iniciar_con_windows": StartWithWindows = val.Trim() == "1" ? 1 : 0; return true;
                case "apagar_pc_aviso_s":
                    {
                        int s;
                        if (int.TryParse(val.Trim(), out s)) ShutdownWarnSecs = Math.Max(0, Math.Min(120, s));
                        CeInicio.AvisoApagadoS = ShutdownWarnSecs;
                        return true;
                    }
            }
            return false;
        }
    }

    static partial class Program
    {
        // 19-11 (Prisma): consola apagada -> PC apagado. En ConsoleEnhancer, antes, el aviso con la cuenta atras (apagar_pc_aviso_s)
        static void ApagarPc()
        {
            // 1.0.14: en modo espera sin cuenta atras (como una consola: se apaga y ya); la consola, un mando o una tecla lo devuelven
            int s = CeSuspension.Modo == "espera" ? 0 : CeInicio.AvisoApagadoS;
            if (s > 0)
            {
                Log.Write("Consola apagada: aviso de " + s + " s antes de " + (CeSuspension.Modo == "espera" ? "dejar en espera" : CeSuspension.Modo == "suspender" ? "suspender" : "apagar") + " el PC (apagar_pc=1)");
                string porque;
                if (!AvisoApagado.Mostrar(s, false, false, out porque))
                {
                    Log.Write("Apagado del PC cancelado (" + porque + ")");
                    Escritorio.Cancelado(); // (el PC sigue encendido: el escritorio normal, si estaba en negro)
                    return;
                }
            }
            // 1.0.14 (src\Suspension.cs): suspender y, al despertar con el mando o el teclado, volver a abrir la app; si no puede, apagar
            if (CeSuspension.NoApaga && CeSuspension.Dormir()) return;
            CeInicio.ApagarPcYa(false);
        }
    }

    static partial class CeInicio
    {
        public const string TaskName = "ConsoleEnhancer - inicio";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "ConsoleEnhancer";
        public static volatile int AvisoApagadoS = 15;

        static string Exe { get { return Path.GetFullPath(System.Reflection.Assembly.GetEntryAssembly().Location); } }

        // Lo que mira Program.Main al principio de cada argumento: int.MinValue = no es mio, seguir
        public static int Argumento(string arg, string[] args)
        {
            { int su = CeSuspension.Argumento(arg); if (su != int.MinValue) return su; } // (1.0.14: src\Suspension.cs)
            if (arg == "--ajustes") return PanelAjustes.Run();
            if (arg.StartsWith("--ajustes-preview=")) return PanelAjustes.Preview(arg.Substring(18));   // (1.2.0: "ConsoleEnhancer Settings.exe", src\Lanzador\Panel.cs)
            if (arg == "--inicio") return Lanzar(args);
            if (arg.StartsWith("--iniciar-con-windows="))
            {
                bool on = arg.Substring(22).Trim() == "1";
                string det;
                bool ok = Poner(on, out det);
                Log.Write("--iniciar-con-windows=" + (on ? "1" : "0") + ": " + det);
                return ok ? 0 : 1;
            }
            if (arg == "--prueba-apagado" || arg.StartsWith("--prueba-apagado="))
            {
                int s = 5;
                bool fuera = false;
                if (arg.Length > 17)
                {
                    string[] p = arg.Substring(17).Split(';');
                    int v;
                    if (int.TryParse(p[0], out v)) s = Math.Max(1, Math.Min(120, v));
                    fuera = p.Length > 1 && p[1] == "fuera";
                }
                Log.Write("Prueba del aviso de apagado (" + s + " s" + (fuera ? ", fuera de la pantalla" : "") + "): no se apaga nada");
                string porque;
                bool apagar = AvisoApagado.Mostrar(s, true, fuera, out porque);
                if (apagar) ApagarPcYa(true); else Log.Write("Prueba del aviso de apagado: cancelado (" + porque + ")");
                return apagar ? 0 : 2;
            }
            if (arg.StartsWith("--ui-preview=apagado"))
            {
                string[] pv = arg.Substring(13).Split(new char[] { ';' }, 2);
                AvisoApagado.Preview(pv.Length > 1 ? pv[1] : Path.Combine(Paths.Datos, "apagado"));
                return 0;
            }
            return int.MinValue;
        }

        // ------------------------------------------------------------------ apagar el PC
        public static void ApagarPcYa(bool simulado)
        {
            bool hybrid = false;
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0);
                hybrid = v is int && (int)v == 1;
            }
            catch { }
            string sd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
            string a = "/s " + (hybrid ? "/hybrid " : "") + "/f /t 0";
            if (simulado) { Log.Write("SIMULADO: apagaria el PC con " + sd + " " + a + " (no se ejecuta)"); return; }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(sd, a);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Log.Write("Consola apagada: apagando el PC (apagar_pc=1" + (hybrid ? ", con el inicio rapido" : "") + ")");
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Error("No se pudo apagar el PC", ex); }
        }

        // ------------------------------------------------------------------ la tarea programada
        // Lo que arranca al iniciar sesion: "tarea", "run" o null; exe = a que exe apunta
        public static string Estado(out string exe)
        {
            exe = null;
            string xml;
            if (Schtasks("/Query /TN \"" + TaskName + "\" /XML", out xml) == 0)
            {
                Match m = Regex.Match(xml, @"<Command>\s*""?([^""<]+?)""?\s*</Command>");
                if (m.Success) exe = m.Groups[1].Value.Trim();
                return "tarea";
            }
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    string v = k != null ? k.GetValue(RunValue) as string : null;
                    if (!string.IsNullOrEmpty(v))
                    {
                        Match m = Regex.Match(v, @"^\s*""([^""]+)""");
                        exe = m.Success ? m.Groups[1].Value : v.Split(' ')[0];
                        return "run";
                    }
                }
            }
            catch { }
            return null;
        }

        public static bool EsEsteExe(string exe)
        {
            try { return !string.IsNullOrEmpty(exe) && string.Equals(Path.GetFullPath(exe), Exe, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        // Pone o quita el arranque con Windows (esta copia de la app). det = lo que ha pasado (para el log y los avisos)
        public static bool Poner(bool on, out string det)
        {
            string salida;
            if (!on)
            {
                bool ok = true;
                List<string> hecho = new List<string>();
                string exe;
                string modo = Estado(out exe);
                if (modo == "tarea")
                {
                    if (Schtasks("/Delete /TN \"" + TaskName + "\" /F", out salida) == 0) hecho.Add("tarea quitada");
                    else { ok = false; hecho.Add("no se pudo quitar la tarea: " + Una(salida)); }
                }
                if (QuitarRun()) hecho.Add("quitada la entrada de Run");
                det = hecho.Count > 0 ? string.Join(", ", hecho.ToArray()) : "no estaba";
                return ok;
            }
            string tmp = Path.Combine(Path.GetTempPath(), "consoleenhancer_tarea_" + Process.GetCurrentProcess().Id + ".xml");
            try
            {
                File.WriteAllText(tmp, TareaXml(), Encoding.Unicode);
                int rc = Schtasks("/Create /TN \"" + TaskName + "\" /XML \"" + tmp + "\" /F", out salida);
                if (rc == 0)
                {
                    QuitarRun();
                    det = "tarea \"" + TaskName + "\" -> " + Exe + " " + Argumentos();
                    return true;
                }
                string err = Una(salida);
                // Sin permiso para el Programador de tareas: la clave Run del usuario (no pide nada)
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
                    k.SetValue(RunValue, "\"" + Exe + "\" " + Argumentos());
                det = "sin tarea programada (" + err + "): clave Run de HKCU -> " + Exe + " " + Argumentos();
                return true;
            }
            catch (Exception ex)
            {
                det = "fallo: " + ex.Message;
                return false;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        static bool QuitarRun()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null || k.GetValue(RunValue) == null) return false;
                    k.DeleteValue(RunValue, false);
                    return true;
                }
            }
            catch { return false; }
        }

        public static string ArgumentosInicio() { return Argumentos(); }

        // --inicio y, si la app se abrio con --datos=, el mismo (asi la tarea abre los mismos datos)
        static string Argumentos()
        {
            string a = "--inicio";
            foreach (string s in Environment.GetCommandLineArgs())
                if (s.StartsWith("--datos=", StringComparison.OrdinalIgnoreCase)) a += " \"" + s + "\"";
            return a;
        }

        static string TareaXml()
        {
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            string user = Environment.UserDomainName + "\\" + Environment.UserName;
            string exe = Exe;
            StringBuilder x = new StringBuilder();
            x.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            x.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            x.AppendLine("  <RegistrationInfo>");
            x.AppendLine("    <Description>" + SecurityElement.Escape("Abre ConsoleEnhancer al iniciar sesion, con la pantalla en negro hasta que se ve (" + exe + " --inicio). Se quita desde el menu de la bandeja o con --iniciar-con-windows=0.") + "</Description>");
            x.AppendLine("  </RegistrationInfo>");
            x.AppendLine("  <Principals>");
            x.AppendLine("    <Principal id=\"Author\">");
            x.AppendLine("      <UserId>" + SecurityElement.Escape(sid) + "</UserId>");
            x.AppendLine("      <LogonType>InteractiveToken</LogonType>");
            x.AppendLine("      <RunLevel>LeastPrivilege</RunLevel>");
            x.AppendLine("    </Principal>");
            x.AppendLine("  </Principals>");
            x.AppendLine("  <Settings>");
            x.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            x.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            x.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            x.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
            x.AppendLine("    <StartWhenAvailable>false</StartWhenAvailable>");
            x.AppendLine("    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>");
            x.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
            x.AppendLine("    <Enabled>true</Enabled>");
            x.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
            x.AppendLine("    <Priority>4</Priority>");
            x.AppendLine("  </Settings>");
            x.AppendLine("  <Triggers>");
            x.AppendLine("    <LogonTrigger><Enabled>true</Enabled><UserId>" + SecurityElement.Escape(user) + "</UserId></LogonTrigger>");
            x.AppendLine("  </Triggers>");
            x.AppendLine("  <Actions Context=\"Author\">");
            x.AppendLine("    <Exec>");
            x.AppendLine("      <Command>\"" + SecurityElement.Escape(exe) + "\"</Command>");
            x.AppendLine("      <Arguments>" + SecurityElement.Escape(Argumentos()) + "</Arguments>");
            x.AppendLine("      <WorkingDirectory>" + SecurityElement.Escape(Path.GetDirectoryName(exe)) + "</WorkingDirectory>");
            x.AppendLine("    </Exec>");
            x.AppendLine("  </Actions>");
            x.AppendLine("</Task>");
            return x.ToString();
        }

        static int Schtasks(string args, out string salida)
        {
            salida = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"), args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    // (los dos a la vez: con mucha salida, leer uno detras de otro puede atascarse)
                    System.Threading.Tasks.Task<string> err = p.StandardError.ReadToEndAsync();
                    string o = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } salida = "schtasks no termina"; return -1; }
                    salida = o + err.Result;
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { salida = ex.Message; return -1; }
        }

        static string Una(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 160 ? s.Substring(0, 160) : s;
        }

        // Al arrancar la app: la tarea como diga el ini (solo si la clave esta). En segundo plano: schtasks tarda unos 100-300 ms
        public static void Sincronizar(Config cfg)
        {
            int want = cfg.StartWithWindows;
            if (want < 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string exe;
                    string modo = Estado(out exe);
                    bool mio = modo != null && EsEsteExe(exe);
                    string det;
                    if (want == 1 && !mio)
                    {
                        if (modo != null) Log.Write("Iniciar con Windows: apuntaba a " + exe + "; ahora a esta copia");
                        Poner(true, out det);
                        Log.Write("Iniciar con Windows (iniciar_con_windows=1): " + det);
                    }
                    else if (want == 0 && mio)
                    {
                        Poner(false, out det);
                        Log.Write("Iniciar con Windows (iniciar_con_windows=0): " + det);
                    }
                    else if (want == 0 && modo != null)
                        Log.Write("Iniciar con Windows: hay " + (modo == "tarea" ? "una tarea" : "una entrada de Run") + " de otra copia (" + exe + "); no se toca");
                    else if (want == 1) Log.Write("Iniciar con Windows: " + (modo == "tarea" ? "tarea \"" + TaskName + "\"" : "clave Run") + " -> " + exe);
                }
                catch (Exception ex) { Log.Error("Iniciar con Windows", ex); }
            });
        }

        // ------------------------------------------------------------------ --inicio: el telon y la app
        static string logInicio;
        static readonly Stopwatch reloj = new Stopwatch();

        static void LogI(string t)
        {
            try { File.AppendAllText(logInicio, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + t + "\r\n", Encoding.UTF8); }
            catch { }
        }

        static int Lanzar(string[] args)
        {
            reloj.Start();
            bool prueba = Array.IndexOf(args, "--inicio-prueba") >= 0;
            // 1.0.14: al despertar de la suspension, la app que suspendio el PC aun tiene el mutex unos instantes: esperar a que lo suelte
            if (Array.IndexOf(args, "--tras-suspension") >= 0)
                for (int i = 0; i < 100; i++)
                {
                    Mutex w;
                    if (!Mutex.TryOpenExisting("Local\\ConsoleEnhancer_SingleInstance", out w)) break;
                    w.Dispose();
                    Thread.Sleep(100);
                }
            // Ya abierta (o arrancando): nada
            Mutex o;
            if (Mutex.TryOpenExisting("Local\\ConsoleEnhancer_SingleInstance", out o)) { o.Dispose(); return 0; }

            // El negro lo primero (antes de leer nada del disco)
            Application.EnableVisualStyles();
            Telon telon = new Telon(prueba);
            telon.Show();
            telon.Refresh();
            long negro = reloj.ElapsedMilliseconds;

            logInicio = Path.Combine(Paths.Datos, @"logs\inicio.log");
            try { FileInfo f = new FileInfo(logInicio); if (f.Exists && f.Length > 256 * 1024) f.Delete(); } catch { }
            LogI("===== Abriendo ConsoleEnhancer al iniciar sesion =====" + (prueba ? " (prueba: el telon fuera de la pantalla)" : ""));
            LogI("Negro en pantalla (" + negro + " ms)");

            StringBuilder resto = new StringBuilder();
            foreach (string a in args)
            {
                if (a == "--inicio" || a == "--inicio-prueba" || a == "--tras-suspension") continue;
                if (resto.Length > 0) resto.Append(' ');
                resto.Append(a.IndexOf(' ') >= 0 ? "\"" + a + "\"" : a);
            }
            Process p;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Exe, resto.ToString());
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(Exe);
                p = Process.Start(psi);
                try { AllowSetForegroundWindow(p.Id); } catch { }
                try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
                LogI("Lanzada la app (pid " + p.Id + (resto.Length > 0 ? ", " + resto : "") + ", " + reloj.ElapsedMilliseconds + " ms)");
            }
            catch (Exception ex)
            {
                LogI("Error al abrir la app: " + ex.Message);
                telon.Close();
                return 2;
            }

            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 50;
            DateTime fin = DateTime.UtcNow.AddSeconds(30);
            DateTime grande = DateTime.MinValue, otra = DateTime.MinValue;
            t.Tick += delegate
            {
                string porque = null;
                int v = 0;
                try { p.Refresh(); if (p.HasExited) porque = "la app se ha cerrado (codigo " + p.ExitCode + ")"; else v = Ventanas(p.Id); }
                catch (Exception ex) { porque = "error mirando la app: " + ex.Message; }
                if (porque == null)
                {
                    if (v == 2) { if (grande == DateTime.MinValue) grande = DateTime.UtcNow; else if ((DateTime.UtcNow - grande).TotalMilliseconds >= 250) porque = "pantalla de carga vista"; }
                    else grande = DateTime.MinValue;
                    if (v == 1) { if (otra == DateTime.MinValue) otra = DateTime.UtcNow; else if ((DateTime.UtcNow - otra).TotalMilliseconds >= 400) porque = "ventana de la app (asistente o aviso)"; }
                    else otra = DateTime.MinValue;
                    if (porque == null && DateTime.UtcNow > fin) porque = "30 s sin ver la app";
                }
                if (porque == null) return;
                t.Stop();
                LogI("Quitado el negro: " + porque + " (" + reloj.ElapsedMilliseconds + " ms)");
                telon.Close();
            };
            t.Start();
            Application.Run(telon);

            // Prioridad de vuelta a normal cuando ya ha arrancado del todo
            Thread.Sleep(5000);
            try
            {
                p.Refresh();
                if (!p.HasExited && p.PriorityClass == ProcessPriorityClass.AboveNormal) p.PriorityClass = ProcessPriorityClass.Normal;
            }
            catch { }
            return 0;
        }

        sealed class Telon : Form
        {
            readonly bool prueba;
            public Telon(bool prueba)
            {
                this.prueba = prueba;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Rectangle s = Screen.PrimaryScreen.Bounds;
                Bounds = prueba ? new Rectangle(-12000, -12000, s.Width / 4, s.Height / 4) : s;
                BackColor = Color.Black;
                TopMost = !prueba;
                ShowInTaskbar = false;
                Text = "ConsoleEnhancer";
            }
            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                if (!prueba) Cursor.Hide();
            }
            // Sin quitarle el foco a nadie: la app se pone delante sola
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                    return cp;
                }
            }
        }

        // 0 = nada que ver, 1 = una ventana normal de la app en la pantalla (asistente, aviso), 2 = una que la ocupa (la pantalla de carga)
        static int Ventanas(int pid)
        {
            Rectangle pant = Screen.PrimaryScreen.Bounds;
            long area = (long)pant.Width * pant.Height;
            int r = 0;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint wp;
                GetWindowThreadProcessId(h, out wp);
                if (wp != (uint)pid || !IsWindowVisible(h)) return true;
                RECT rc;
                if (!GetWindowRect(h, out rc)) return true;
                Rectangle w = Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom);
                Rectangle en = Rectangle.Intersect(w, pant);
                if ((long)en.Width * en.Height >= area * 8 / 10) { r = 2; return false; }
                if (en.Width >= 200 && en.Height >= 120) r = 1;
                return true;
            }, IntPtr.Zero);
            return r;
        }

        delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    }

    // ------------------------------------------------------------------ el aviso antes de apagar el PC
    // Pantalla entera, oscura como los avisos de la app: "La consola se ha apagado" y la cuenta atras. Cancela cualquier cosa que haga
    // el usuario: un boton del mando (PadHub: XInput, HID, winmm), una tecla o el raton (GetLastInputInfo: vale aunque la ventana no
    // tenga el foco). Los primeros 600 ms no cuentan (lo que aun estuviera pulsado al apagar la consola)
    sealed class AvisoApagado : Form
    {
        readonly int total;
        readonly bool simulado;
        readonly Stopwatch reloj = new Stopwatch();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        bool apagar, cancelado;
        string porque = "";
        uint ultimaEntrada;
        uint botonesAntes;
        bool mandos;
        double vistaS = -1;   // (solo la vista previa: los segundos que se dibujan)

        public static bool Mostrar(int segundos, bool simulado, bool fuera, out string porque)
        {
            porque = "";
            try
            {
                Application.EnableVisualStyles();
                using (AvisoApagado a = new AvisoApagado(segundos, simulado))
                {
                    if (fuera) { a.Bounds = new Rectangle(-12000, -12000, 1280, 720); a.TopMost = false; }
                    Application.Run(a);
                    porque = a.porque;
                    return a.apagar;
                }
            }
            catch (Exception ex)
            {
                // Sin aviso que enseñar no se apaga a ciegas
                Log.Error("Aviso de apagado", ex);
                porque = "error del aviso";
                return false;
            }
        }

        AvisoApagado(int segundos, bool simulado)
        {
            total = Math.Max(1, segundos);
            this.simulado = simulado;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            BackColor = PrismaUi.PanelBottom;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            Text = PrismaUi.AppName;
            DoubleBuffered = true;
            try { Icon = PrismaUi.FormIcon; } catch { }
            // 1.0.13: sin KeyDown/MouseDown del formulario (tambien saltan con lo inyectado): lo miran los ganchos de abajo
            timer.Interval = 50;
            timer.Tick += delegate { Paso(); };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ultimaEntrada = UltimaEntrada();
            PonerGanchos();
            try { PadHub.Shared.Start(); PadSnapshot s; PadHub.Shared.Poll(out s); botonesAntes = s.Buttons; mandos = true; }
            catch (Exception ex) { Log.Error("Aviso de apagado: mandos", ex); }
            reloj.Start();
            timer.Start();
            try { Activate(); } catch { }
        }

        void Paso()
        {
            if (cancelado)
            {
                if (reloj.ElapsedMilliseconds >= 1500) Close();
                return;
            }
            double t = reloj.Elapsed.TotalSeconds;
            if (!ganchos)
            {
                // (sin ganchos: como antes, GetLastInputInfo, que tambien cuenta lo inyectado)
                uint e = UltimaEntrada();
                if (t < 0.6) ultimaEntrada = e;
                else if (e != ultimaEntrada) { Cancelar("teclado o raton"); return; }
            }
            if (mandos)
            {
                try
                {
                    PadSnapshot s;
                    PadHub.Shared.Poll(out s);
                    uint nuevos = s.Buttons & ~botonesAntes;
                    botonesAntes = s.Buttons;
                    if (t >= 0.6 && nuevos != 0) { Cancelar("mando"); return; }
                }
                catch { mandos = false; }
            }
            if (t >= total)
            {
                apagar = true;
                porque = "cuenta atras terminada" + (simulado ? " (prueba)" : "");
                timer.Stop();
                Close();
                return;
            }
            Invalidate();
        }

        // 21-11 (1.0.13): con GetLastInputInfo el aviso se cancelaba solo a los 2 s sin tocar nada: cuenta tambien la entrada inyectada
        // (Joyxoff, mapeadores del mando, Steam Input, la propia app). Ahora ganchos de bajo nivel: solo una tecla o un clic de verdad
        // (sin LLKHF_INJECTED / LLMHF_INJECTED) o mover el raton de verdad mas de MoverPx desde donde estaba
        const int MoverPx = 40;
        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        HookProc kbdProc, mouseProc;
        IntPtr hKbd, hMouse;
        bool ganchos;
        bool hayRaton;
        Point ratonInicio;
        int inyectadas;

        void PonerGanchos()
        {
            try
            {
                kbdProc = TeclaLL;
                mouseProc = RatonLL;
                IntPtr mod = GetModuleHandle(null);
                hKbd = SetWindowsHookEx(13, kbdProc, mod, 0);   // WH_KEYBOARD_LL
                hMouse = SetWindowsHookEx(14, mouseProc, mod, 0); // WH_MOUSE_LL
                ganchos = hKbd != IntPtr.Zero && hMouse != IntPtr.Zero;
                if (!ganchos) { QuitarGanchos(); Log.Write("Aviso de apagado: sin ganchos de teclado/raton (se mira GetLastInputInfo)"); }
            }
            catch (Exception ex) { ganchos = false; Log.Error("Aviso de apagado: ganchos", ex); }
        }

        void QuitarGanchos()
        {
            if (hKbd != IntPtr.Zero) { UnhookWindowsHookEx(hKbd); hKbd = IntPtr.Zero; }
            if (hMouse != IntPtr.Zero) { UnhookWindowsHookEx(hMouse); hMouse = IntPtr.Zero; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            QuitarGanchos();
            if (inyectadas > 0) Log.Write("Aviso de apagado: " + inyectadas + " entradas inyectadas (de otro programa) no cuentan para cancelar");
            base.OnFormClosed(e);
        }

        bool Cuenta { get { return !cancelado && !apagar && reloj.Elapsed.TotalSeconds >= 0.6; } }

        IntPtr TeclaLL(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int msg = (int)wParam;
                if (msg == 0x100 || msg == 0x104) // WM_KEYDOWN, WM_SYSKEYDOWN
                {
                    int flags = Marshal.ReadInt32(lParam, 8);
                    if ((flags & 0x10) != 0) inyectadas++;             // LLKHF_INJECTED
                    else if (Cuenta) Cancelar("tecla");
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        IntPtr RatonLL(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int msg = (int)wParam;
                int x = Marshal.ReadInt32(lParam, 0), y = Marshal.ReadInt32(lParam, 4);
                int flags = Marshal.ReadInt32(lParam, 12);
                bool iny = (flags & 0x01) != 0;                          // LLMHF_INJECTED
                if (iny) { if (msg != 0x200) inyectadas++; }
                else if (msg == 0x200)                                   // WM_MOUSEMOVE
                {
                    if (!hayRaton || !Cuenta) { ratonInicio = new Point(x, y); hayRaton = true; }
                    else if (Math.Abs(x - ratonInicio.X) + Math.Abs(y - ratonInicio.Y) > MoverPx) Cancelar("raton movido");
                }
                else if (msg == 0x201 || msg == 0x204 || msg == 0x207 || msg == 0x20B || msg == 0x20A) { if (Cuenta) Cancelar("clic"); }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);

        void Cancelar(string como)
        {
            if (cancelado || apagar || vistaS >= 0) return;
            cancelado = true;
            porque = como;
            reloj.Restart();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Dibujar(e.Graphics, ClientRectangle, vistaS >= 0 ? vistaS : reloj.Elapsed.TotalSeconds);
        }

        void Dibujar(Graphics g, Rectangle r, double t)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (LinearGradientBrush bg = new LinearGradientBrush(r, PrismaUi.PanelTop, PrismaUi.PanelBottom, 90f)) g.FillRectangle(bg, r);
            float u = r.Height / 1080f;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            int quedan = Math.Max(0, (int)Math.Ceiling(total - t));
            string modo = CeSuspension.Modo;
            string titulo = cancelado ? Idioma.T(modo == "apagar" ? "Apagado del PC cancelado" : "Suspensión del PC cancelada") : Idioma.T("La consola se ha apagado");
            string grande = cancelado ? "" : quedan.ToString();
            string linea = cancelado ? Idioma.T("El PC sigue encendido. Puedes volver a abrir ConsoleEnhancer cuando quieras.")
                                     : Idioma.T(modo == "espera" ? "El PC se quedará en espera en unos segundos (la consola, un mando o una tecla lo vuelven a encender)"
                                                        : modo == "suspender" ? "El PC se suspenderá en unos segundos (un mando o una tecla lo vuelven a encender)" : "El PC se apagará en unos segundos") + (simulado ? " (" + Idioma.T("prueba: no se apaga") + ")" : "");
            string ayuda = cancelado ? "" : Idioma.T("Pulsa cualquier botón del mando o una tecla, o mueve el ratón, para cancelar");
            using (StringFormat c = new StringFormat())
            using (Font ft = PrismaUi.TitleFont(54 * u))
            using (Font fn = PrismaUi.TitleFont(150 * u))
            using (Font fb = PrismaUi.BodyFont(30 * u))
            using (SolidBrush bt = new SolidBrush(PrismaUi.TitleColor))
            using (SolidBrush bd = new SolidBrush(PrismaUi.DetailColor))
            using (SolidBrush ba = new SolidBrush(cancelado ? PrismaUi.Accent : PrismaUi.Warn))
            {
                c.Alignment = StringAlignment.Center;
                c.LineAlignment = StringAlignment.Center;
                g.DrawString(titulo, ft, bt, new RectangleF(r.X, cy - 300 * u, r.Width, 90 * u), c);
                if (grande.Length > 0) g.DrawString(grande, fn, ba, new RectangleF(r.X, cy - 190 * u, r.Width, 220 * u), c);
                g.DrawString(linea, fb, bt, new RectangleF(r.X + 60 * u, cy + 50 * u, r.Width - 120 * u, 60 * u), c);
                if (ayuda.Length > 0) g.DrawString(ayuda, fb, bd, new RectangleF(r.X + 60 * u, cy + 120 * u, r.Width - 120 * u, 60 * u), c);
            }
            if (!cancelado)
            {
                // lo que queda: una barra que se vacia hacia el centro
                RectangleF pista = new RectangleF(cx - 420 * u, cy + 220 * u, 840 * u, 6 * u);
                float queda = (float)Math.Max(0, Math.Min(1, 1 - t / total));
                using (GraphicsPath tp = PrismaUi.RoundRect(pista, pista.Height / 2))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(34, 255, 255, 255))) g.FillPath(tb, tp);
                RectangleF lleno = new RectangleF(cx - pista.Width * queda / 2, pista.Y, pista.Width * queda, pista.Height);
                if (lleno.Width >= pista.Height)
                    using (GraphicsPath lp = PrismaUi.RoundRect(lleno, pista.Height / 2))
                    using (SolidBrush lb = new SolidBrush(PrismaUi.Warn)) g.FillPath(lb, lp);
            }
        }

        // --ui-preview=apagado;<carpeta>: la cuenta atras al empezar, a 3 s del final y cancelada, en es y en
        public static void Preview(string carpeta)
        {
            Directory.CreateDirectory(carpeta);
            foreach (string lang in new[] { "es", "en" })
            {
                Idioma.Poner(lang);
                foreach (int caso in new[] { 0, 1, 2 })
                    using (AvisoApagado a = new AvisoApagado(15, caso == 1))
                    using (Bitmap b = new Bitmap(1920, 1080))
                    {
                        a.vistaS = caso == 0 ? 0.2 : 12.4;
                        a.cancelado = caso == 2;
                        using (Graphics g = Graphics.FromImage(b)) a.Dibujar(g, new Rectangle(0, 0, 1920, 1080), a.vistaS);
                        b.Save(Path.Combine(carpeta, "apagado_" + lang + "_" + (caso == 0 ? "inicio" : caso == 1 ? "final_prueba" : "cancelado") + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        static uint UltimaEntrada()
        {
            LASTINPUTINFO i = new LASTINPUTINFO();
            i.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            return GetLastInputInfo(ref i) ? i.dwTime : 0;
        }
    }

    // ------------------------------------------------------------------ menu de la bandeja
    partial class TrayApp
    {
        ToolStripMenuItem ceInicioItem, ceApagarItem, ceEscritorioItem, ceSuspenderItem;

        // (una linea en el constructor del menu, tras "Asistente de configuracion")
        void MenuCe(ContextMenuStrip menu)
        {
            ceInicioItem = new ToolStripMenuItem(Idioma.T("Iniciar con Windows"), null, delegate { CambiarInicio(); });
            ceEscritorioItem = new ToolStripMenuItem(Idioma.T("Escritorio negro con la app abierta"), null, delegate { CambiarEscritorio(); });
            ceApagarItem = new ToolStripMenuItem(Idioma.T("Apagar el PC al apagar la consola"), null, delegate { CambiarApagar(); });
            menu.Items.Add(ceInicioItem);
            menu.Items.Add(ceEscritorioItem);
            menu.Items.Add(ceApagarItem);
            // 1.0.14: suspender en vez de apagar; un mando o un teclado lo encienden y vuelve la app (src\Suspension.cs)
            ceSuspenderItem = new ToolStripMenuItem(Idioma.T("Al apagar la consola, el PC"));
            ToolStripMenuItem mEsp = new ToolStripMenuItem(Idioma.T("En espera (la consola, un mando o una tecla lo encienden)"), null, delegate { CambiarModo("espera"); });
            ToolStripMenuItem mSus = new ToolStripMenuItem(Idioma.T("Suspendido (solo un teclado lo enciende; gasta casi nada)"), null, delegate { CambiarModo("suspender"); });
            ToolStripMenuItem mApa = new ToolStripMenuItem(Idioma.T("Apagado"), null, delegate { CambiarModo("apagar"); });
            ToolStripMenuItem mQue = new ToolStripMenuItem(Deteccion.Tx.T("¿Cómo funciona cada uno y cuánto gasta?", "How does each one work and how much power?"), null, delegate
            {
                MessageBox.Show(CeEspera.Explicacion(), PrismaUi.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            ceSuspenderItem.DropDownItems.Add(mEsp); ceSuspenderItem.DropDownItems.Add(mSus); ceSuspenderItem.DropDownItems.Add(mApa);
            ceSuspenderItem.DropDownItems.Add(new ToolStripSeparator()); ceSuspenderItem.DropDownItems.Add(mQue);
            menu.Items.Add(ceSuspenderItem);
            MethodInvoker marcar = delegate
            {
                mEsp.Checked = cfg.ShutdownMode == "espera"; mSus.Checked = cfg.ShutdownMode == "suspender"; mApa.Checked = cfg.ShutdownMode == "apagar";
                ceSuspenderItem.Enabled = cfg.ShutdownPc;
            };
            marcar();
            menu.Opening += delegate { marcar(); };
            ceApagarItem.Checked = cfg.ShutdownPc;
            ceEscritorioItem.Checked = cfg.BlackDesktop;
            menu.Opening += delegate { ceApagarItem.Checked = cfg.ShutdownPc; ceEscritorioItem.Checked = cfg.BlackDesktop; MirarInicio(); };
            MirarInicio();
            MenuUsoYDesinstalar(menu); // (1.1.1: como una consola / como un PC normal, y desinstalar; src\Lanzador\Desinstalar.cs)
            MenuCeImagen(menu); // (20-11: Imagen > RTX HDR, Auto HDR, VRR, botones, escalas; src\ImagenCe.cs)
        }

        // 19-11 (ESCRITORIO de Prisma): el escritorio de Windows negro mientras la app esta abierta (con el telon del arranque no se ve Windows
        // al encender); el normal al cerrarla a mano. Se aplica ya
        void CambiarEscritorio()
        {
            bool on = !cfg.BlackDesktop;
            cfg.BlackDesktop = on;
            ceEscritorioItem.Checked = on;
            Ce.IniPoner("escritorio_negro", on ? "1" : "0");
            Log.Write("Escritorio negro con la app abierta " + (on ? "activado" : "desactivado") + " desde la bandeja");
            ThreadPool.QueueUserWorkItem(delegate { try { Escritorio.Cambiar(on); } catch (Exception ex) { Log.Error("Escritorio", ex); } });
        }

        void MirarInicio()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string exe;
                string modo = CeInicio.Estado(out exe);
                bool mio = modo != null && CeInicio.EsEsteExe(exe);
                try { ui.BeginInvoke((MethodInvoker)delegate { ceInicioItem.Checked = mio; }); } catch { }
            });
        }

        void CambiarInicio()
        {
            bool on = !ceInicioItem.Checked;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string det;
                bool ok = CeInicio.Poner(on, out det);
                Log.Write("Iniciar con Windows " + (on ? "activado" : "desactivado") + " desde la bandeja: " + det);
                if (ok) Ce.IniPoner("iniciar_con_windows", on ? "1" : "0");
                cfg.StartWithWindows = on ? 1 : 0;
                MirarInicio();
                if (!ok)
                    try
                    {
                        ui.BeginInvoke((MethodInvoker)delegate
                        {
                            MessageBox.Show(Idioma.T("No se pudo cambiar el inicio con Windows:") + "\n" + det, PrismaUi.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        });
                    }
                    catch { }
            });
        }

        void CambiarApagar()
        {
            bool on = !cfg.ShutdownPc;
            if (on)
            {
                int s = cfg.ShutdownWarnSecs;
                string txt = Idioma.T(cfg.ShutdownMode == "espera" ? "Al apagar la consola, ConsoleEnhancer se cerrará y dejará el PC en espera; al encender la consola, un mando o una tecla vuelve a abrirse." :
                                       cfg.ShutdownMode == "suspender" ? "Al apagar la consola, ConsoleEnhancer se cerrará y suspenderá el PC; un mando o una tecla lo encienden y vuelve a abrirse." : "Al apagar la consola, ConsoleEnhancer se cerrará y apagará el PC.") + "\n\n" +
                             (s > 0 ? string.Format(Idioma.T("Antes sale un aviso de {0} s: cualquier botón del mando, tecla o el ratón lo cancela."), s)
                                    : Idioma.T("Sin aviso (apagar_pc_aviso_s=0): se apaga en cuanto se cierra.")) + "\n" +
                             Idioma.T("Cerrar la app a mano (Salir, Alt+F4) no apaga el PC. Guarda lo que tengas abierto en el PC: el apagado cierra los programas.") + "\n\n" +
                             Idioma.T("¿Activarlo?");
                if (MessageBox.Show(txt, PrismaUi.AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            }
            cfg.ShutdownPc = on;
            ceApagarItem.Checked = on;
            Ce.IniPoner("apagar_pc", on ? "1" : "0");
            Log.Write("Apagar el PC al apagar la consola " + (on ? "activado" : "desactivado") + " desde la bandeja");
            if (on && cfg.ShutdownMode == "suspender") ThreadPool.QueueUserWorkItem(delegate { CeSuspension.PedirArmar(false); });
        }

        // 1.0.14: abierta al salir de la espera (--desde-espera): si la consola no se ve en espera_volver_sin_consola_s, se cierra como
        // con la consola apagada (el aviso que se cancela y otra vez la espera)
        System.Threading.Timer esperaReloj;
        void VolverAEsperaSinConsola()
        {
            if (!CeEspera.DesdeEspera || !cfg.ShutdownPc || cfg.ShutdownMode == "apagar" || CeEspera.VolverSinConsolaS <= 0) return;
            int s = CeEspera.VolverSinConsolaS;
            Log.Write("Abierta desde la espera: si en " + s + " s no se ve la consola, vuelve a la espera");
            esperaReloj = new System.Threading.Timer(delegate
            {
                try
                {
                    if (consoleSeen || userExit || PowerOffClock != null) { esperaReloj.Dispose(); return; }
                    if (mpv == null) return; // (aun abriendo la imagen: en 10 s otra vez)
                    esperaReloj.Dispose();
                    Log.Write("Abierta desde la espera y la consola sigue apagada tras " + s + " s: otra vez en espera");
                    PowerOffNow("sin-consola-tras-espera", null);
                }
                catch (Exception ex) { Log.Error("Volver a la espera", ex); }
            }, null, s * 1000, 10000);
        }

        // 1.0.14: apagar_pc_modo=espera|suspender|apagar. Con suspender se arman los mandos y teclados (UAC). Antes, lo que hace y gasta
        void CambiarModo(string modo)
        {
            if (modo != cfg.ShutdownMode &&
                MessageBox.Show(CeEspera.Explicacion() + "\n\n" + Deteccion.Tx.T("¿Usar «", "Use \"") + CeEspera.Nombre(modo) + Deteccion.Tx.T("»?", "\"?"),
                                PrismaUi.AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            cfg.ShutdownMode = modo;
            CeSuspension.Modo = modo;
            Ce.IniPoner("apagar_pc_modo", modo);
            Log.Write("Al apagar la consola: apagar_pc_modo=" + modo + " (desde la bandeja)");
            if (modo == "suspender") ThreadPool.QueueUserWorkItem(delegate { CeSuspension.PedirArmar(true); });
        }
    }

    // ------------------------------------------------------------------ leer y cambiar una clave del ini
    static partial class Ce
    {
        // El valor que vale de una clave (la ultima linea); null si no esta
        public static string IniLeer(string key)
        {
            string r = null;
            try
            {
                if (!File.Exists(Paths.IniFile)) return null;
                foreach (string raw in File.ReadAllLines(Paths.IniFile))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l[0] == '#' || l[0] == ';') continue;
                    int i = l.IndexOf('=');
                    if (i > 0 && l.Substring(0, i).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) r = l.Substring(i + 1).Trim();
                }
            }
            catch { }
            return r;
        }

        // Cambia la ultima linea de la clave (la que vale) o la pone al final; el resto del ini igual
        public static void IniPoner(string key, string val)
        {
            try
            {
                lock (typeof(Ce))
                {
                    List<string> ls = File.Exists(Paths.IniFile) ? new List<string>(File.ReadAllLines(Paths.IniFile)) : new List<string>();
                    int last = -1;
                    for (int n = 0; n < ls.Count; n++)
                    {
                        string l = ls[n].Trim();
                        if (l.Length == 0 || l[0] == '#' || l[0] == ';') continue;
                        int i = l.IndexOf('=');
                        if (i > 0 && l.Substring(0, i).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) last = n;
                    }
                    if (last >= 0) ls[last] = key + "=" + val; else ls.Add(key + "=" + val);
                    File.WriteAllLines(Paths.IniFile, ls.ToArray(), new UTF8Encoding(false));
                }
            }
            catch (Exception ex) { Log.Error("Guardando " + key + " en " + Paths.IniFile, ex); }
        }
    }
}
