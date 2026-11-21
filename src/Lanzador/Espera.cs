// Espera.cs - la "espera ligera" (21-11-2026, 1.0.14): al apagar la consola (apagar_pc=1, apagar_pc_modo=espera, de serie) el PC
// hace como una consola en reposo: pantalla apagada, nada abierto salvo una sonda que gasta muy poco, y en cuanto se enciende la
// consola, se enciende un mando o se pulsa una tecla, vuelve la pantalla y se abre ConsoleEnhancer (con el telon negro).
//
// Por que no la suspension de verdad (apagar_pc_modo=suspender, src\Lanzador\Suspension.cs): suspendido, la capturadora esta sin
// corriente (no puede ver que se enciende la consola) y muchos receptores de mandos (el del GameSir, por ejemplo) no saben despertar
// el PC. Aqui el PC sigue encendido pero en reposo:
//  - Pantalla apagada (SC_MONITORPOWER) y otra vez si alguien la enciende moviendo el raton sin hacer nada mas.
//  - La sonda: mpv sin ventana ni GPU (--vo=null, sin sonido) a 1280x720 (la GC573 solo acepta 1280x720 a 59,94 de lo pequeno; ~7 % de un nucleo) con scripts\ce_standby.lua: 1 mirada por segundo; sale
//    con 55 cuando ve imagen (ni "No Signal" ni negro) dos veces seguidas. Prioridad baja y EcoQoS (Windows la lleva a los nucleos
//    lentos y a poca frecuencia). Este proceso tambien con EcoQoS y prioridad baja. Si mpv se cierra por otra cosa, se relanza.
//  - Mandos: PadHub (XInput, HID por USB y Bluetooth) cada 0,5 s: un mando nuevo (el receptor del GameSir aparece como mando al
//    encenderlo) o un boton. Teclado: gancho de bajo nivel, solo teclas de verdad (lo inyectado por Joyxoff y compania no cuenta).
//  - Windows no se duerme por su cuenta (ES_SYSTEM_REQUIRED) mientras dura, salvo espera_suspender_min (0 de serie = nunca): pasados
//    esos minutos sin nada, la suspension de verdad (S3) como apagar_pc_modo=suspender.
//  - Al despertar: fuera la sonda (suelta la capturadora), pantalla encendida y ConsoleEnhancer.exe --inicio --tras-suspension.
// Pruebas: ConsoleEnhancer.exe --prueba-espera (lo mismo sin consola ni aviso). Log: logs\ (como la app).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ConsoleEnhancer
{
    static class CeEspera
    {
        public static volatile int SuspenderTrasMin = 0;
        // La app abierta al salir de la espera (--desde-espera): si en VolverSinConsolaS segundos no se ve la consola, vuelve a la
        // espera (el mismo aviso que se cancela). espera_volver_sin_consola_s (90 de serie; 0 = no)
        public static volatile bool DesdeEspera;
        public static volatile int VolverSinConsolaS = 90;

        // ------------------------------------------------------------------ lo que se cuenta en la app (asistente y bandeja)
        public static string Nombre(string modo)
        {
            switch (modo)
            {
                case "espera": return Deteccion.Tx.T("Modo espera, como una consola (recomendado)", "Standby, like a console (recommended)");
                case "suspender": return Deteccion.Tx.T("Suspender el PC", "Sleep the PC");
                case "apagar": return Deteccion.Tx.T("Apagar el PC", "Shut down the PC");
                default: return Deteccion.Tx.T("No hacer nada (el PC sigue igual)", "Do nothing (the PC stays as it is)");
            }
        }

        public static string Explicacion()
        {
            return Deteccion.Tx.T(
"Qué hace el PC cuando apagas la consola (con suspender y apagar, antes sale un aviso de unos segundos que se cancela con el mando, una tecla o el ratón):\n\n" +
"• MODO ESPERA, como una consola (recomendado)\n" +
"   Cómo funciona: al apagar la consola, sin cuenta atrás, la pantalla se apaga, se cierra todo y Windows pasa a su plan Economizador; solo queda un vigilante muy pequeño que mira la capturadora una vez por segundo, " +
"los mandos y el teclado. En cuanto enciendes la consola, enciendes un mando o pulsas una tecla, vuelve la pantalla y ConsoleEnhancer se abre " +
"solo en 2-3 segundos. Si lo despiertas y no enciendes la consola, a los 90 s vuelve a la espera.\n" +
"   Cuánto gasta: lo que el PC sin hacer nada con la pantalla apagada, unos 30-60 W según el PC (el vigilante, menos del 1 % de la CPU). " +
"Con 50 W las 24 horas: ~1,2 kWh al día, unos 5-6 € al mes a 0,15 €/kWh. Tu tele o monitor, en su reposo.\n\n" +
"• SUSPENDER\n" +
"   Cómo funciona: el PC se duerme de verdad (S3) y al despertar se abre ConsoleEnhancer. Pero dormido la capturadora no tiene corriente: " +
"encender la consola NO lo enciende, y muchos receptores de mandos tampoco pueden (casi siempre solo el teclado o el botón de encendido).\n" +
"   Cuánto gasta: 2-5 W (unos 0,30 € al mes).\n\n" +
"• APAGAR\n" +
"   Cómo funciona: el PC se apaga del todo; hay que encenderlo con su botón (y si «Iniciar con Windows» está puesto, se abre la app).\n" +
"   Cuánto gasta: 0,5-2 W.\n\n" +
"Cerrar la app a mano (Salir, Alt+F4) nunca apaga ni duerme el PC.",
"What the PC does when you turn the console off (for sleep and shut down, first a notice of a few seconds that the controller, a key or the mouse cancels):\n\n" +
"• STANDBY, like a console (recommended)\n" +
"   How it works: when the console turns off, with no countdown, the screen goes off, everything closes and Windows switches to its Power saver plan; only a tiny watcher stays, checking the capture card once a second, " +
"the controllers and the keyboard. As soon as you turn the console on, turn a controller on or press a key, the screen comes back and " +
"ConsoleEnhancer opens by itself in 2-3 seconds. If you wake it and don't turn the console on, it goes back to standby after 90 s.\n" +
"   Power: what the PC uses idling with the screen off, about 30-60 W depending on the PC (the watcher, under 1% CPU). " +
"At 50 W around the clock: ~1.2 kWh a day, about 5-6 EUR a month at 0.15 EUR/kWh. Your TV or monitor, in its own standby.\n\n" +
"• SLEEP\n" +
"   How it works: the PC really sleeps (S3) and ConsoleEnhancer opens when it wakes. But while asleep the capture card has no power: " +
"turning the console on does NOT wake it, and many controller receivers can't either (usually only the keyboard or the power button).\n" +
"   Power: 2-5 W (about 0.30 EUR a month).\n\n" +
"• SHUT DOWN\n" +
"   How it works: the PC turns off completely; you turn it on with its button (with \"Start with Windows\", the app opens).\n" +
"   Power: 0.5-2 W.\n\n" +
"Closing the app yourself (Exit, Alt+F4) never shuts down or sleeps the PC.");
        }
        static string device = "", pixfmt = "";

        static string Exe { get { return Path.GetFullPath(System.Reflection.Assembly.GetEntryAssembly().Location); } }

        // Al abrir la app: la capturadora de la sonda
        public static void Recordar(Config cfg)
        {
            Economizador(false, "al abrir la app"); // (si la espera acabo mal y el plan de energia se quedo en Economizador)
            device = cfg.Device ?? "";
            pixfmt = cfg.PixelFormat ?? "";
        }

        static void Leer()
        {
            if (device.Length == 0) device = Ce.IniLeer("device") ?? "";
            if (pixfmt.Length == 0) pixfmt = Ce.IniLeer("pixel_format") ?? "";
        }

        // true = el PC ha estado en espera y se ha vuelto a abrir la app (o no se apaga); false = no se puede: lo de antes
        public static bool Entrar()
        {
            Leer();
            string mpv = Path.Combine(Paths.AppDir, @"mpv\mpv.exe"), lua = Path.Combine(Paths.AppDir, @"scripts\ce_standby.lua");
            if (device.Length == 0 || !File.Exists(mpv) || !File.Exists(lua))
            {
                Log.Write("Espera: sin capturadora, mpv o scripts\\ce_standby.lua: se suspende el PC");
                return CeSuspension.SuspenderYVolver();
            }
            Log.Write("Consola apagada: el PC en espera (apagar_pc_modo=espera): pantalla apagada; la consola, un mando o una tecla lo encienden");
            try { using (Process me = Process.GetCurrentProcess()) me.PriorityClass = ProcessPriorityClass.Idle; } catch { }
            EcoQos(IntPtr.Zero);
            SetThreadExecutionState(EsContinuous | EsSystemRequired);
            Economizador(true, "en espera");
            string porque = null;
            using (Vigia v = new Vigia(mpv, lua))
            {
                Application.Run(v);
                porque = v.Porque;
            }
            SetThreadExecutionState(EsContinuous);
            Economizador(false, "fin de la espera");
            if (porque == "suspender")
            {
                Log.Write("Espera: " + SuspenderTrasMin + " min sin nada (espera_suspender_min): suspension de verdad");
                return CeSuspension.SuspenderYVolver();
            }
            if (porque == null || porque == "windows") { Log.Write("Espera: terminada (" + (porque ?? "sin motivo") + "), sin abrir la app"); return true; }
            Pantalla(true);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Exe, CeInicio.ArgumentosInicio() + " --tras-suspension --desde-espera");
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(Exe);
                Process.Start(psi);
                Log.Write("Espera: despierto por " + porque + ": abriendo la app");
            }
            catch (Exception ex) { Log.Error("Espera: no se pudo abrir la app", ex); }
            return true;
        }

        // La ventana invisible que lo vigila todo (hilo de la UI: el gancho del teclado y los mensajes de Windows)
        sealed class Vigia : Form
        {
            readonly string mpv, lua;
            readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            readonly Stopwatch reloj = Stopwatch.StartNew();
            Process sonda;
            DateTime sondaOtraVez = DateTime.MinValue;
            int sondaFallos;
            bool mandos;
            int mandosAntes;
            uint botonesAntes;
            uint entradaPantalla;           // GetLastInputInfo cuando se apago la pantalla
            DateTime pantallaApagada = DateTime.MinValue;
            HookProc kbdProc;
            IntPtr hKbd;
            public string Porque;

            public Vigia(string mpv, string lua)
            {
                this.mpv = mpv; this.lua = lua;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Bounds = new System.Drawing.Rectangle(-32000, -32000, 1, 1);
                Opacity = 0;
                Text = "ConsoleEnhancer - espera";
                timer.Interval = 1000; // (1 vez por segundo: menos despertares de la CPU)
                timer.Tick += delegate { Paso(); };
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                try { PadHub.Shared.Start(); PadSnapshot s; PadHub.Shared.Poll(out s); mandosAntes = s.Count; botonesAntes = s.Buttons; mandos = true; }
                catch (Exception ex) { Log.Error("Espera: mandos", ex); }
                kbdProc = TeclaLL;
                try { hKbd = SetWindowsHookEx(13, kbdProc, GetModuleHandle(null), 0); } catch { }
                Microsoft.Win32.SystemEvents.SessionEnding += AlApagarWindows;
                Lanzar();
                ApagarPantalla();
                timer.Start();
                Log.Write("Espera: vigilando (" + mandosAntes + " mandos conectados" + (hKbd != IntPtr.Zero ? ", teclado" : "") + ", sonda " + (sonda != null ? "pid " + sonda.Id : "sin abrir") + ")");
            }

            void AlApagarWindows(object s, Microsoft.Win32.SessionEndingEventArgs e) { Fin("windows"); }

            void Lanzar()
            {
                try
                {
                    // (otra capturadora que no acepte 1280x720: tras 2 fallos, lo que ella quiera)
                    string lavf = (sondaFallos % 4 < 2 ? "video_size=1280x720" + (pixfmt.Length > 0 ? ",pixel_format=" + pixfmt : "") + "," : "") + "rtbufsize=16000000";
                    string args = "--no-config \"av://dshow:video=" + device + "\" --demuxer-lavf-o=" + lavf +
                                  " --no-audio --vo=null --untimed --cache=no --hwdec=no --priority=idle --idle=no --input-default-bindings=no" +
                                  " --script=\"" + lua + "\" --msg-level=all=warn,ce_standby=info";
                    ProcessStartInfo psi = new ProcessStartInfo(mpv, args);
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    sonda = Process.Start(psi);
                    try { sonda.PriorityClass = ProcessPriorityClass.Idle; } catch { }
                    EcoQos(sonda.Handle);
                    EngineJob.Hold(sonda, true); // (si este proceso muere, Windows cierra la sonda: la capturadora no se queda cogida)
                }
                catch (Exception ex) { Log.Error("Espera: la sonda", ex); sonda = null; }
            }

            void Paso()
            {
                if (Porque != null) return;
                // La consola: la sonda sale con 55 al ver imagen
                if (sonda != null)
                {
                    bool fuera = false; int code = 0;
                    try { fuera = sonda.HasExited; if (fuera) code = sonda.ExitCode; } catch { fuera = true; code = -1; }
                    if (fuera)
                    {
                        try { sonda.Dispose(); } catch { }
                        sonda = null;
                        if (code == 55) { Fin("la consola"); return; }
                        sondaFallos++;
                        sondaOtraVez = DateTime.UtcNow.AddSeconds(Math.Min(60, 3 * sondaFallos));
                        Log.Write("Espera: la sonda se ha cerrado (codigo " + code + "); otra vez en " + Math.Min(60, 3 * sondaFallos) + " s");
                    }
                }
                else if (DateTime.UtcNow >= sondaOtraVez) Lanzar();
                // Mandos: uno nuevo (encendido) o un boton
                if (mandos)
                {
                    try
                    {
                        PadSnapshot s;
                        PadHub.Shared.Poll(out s);
                        uint nuevos = s.Buttons & ~botonesAntes;
                        botonesAntes = s.Buttons;
                        if (s.Count > mandosAntes) { Fin("un mando encendido"); return; }
                        mandosAntes = s.Count;
                        if (nuevos != 0 && reloj.Elapsed.TotalSeconds > 2) { Fin("un boton del mando"); return; }
                    }
                    catch { mandos = false; }
                }
                // La pantalla: si alguien la ha encendido (raton) y lleva 30 s sin tocar nada, otra vez apagada
                uint ult = UltimaEntrada();
                if (ult != entradaPantalla && (uint)Environment.TickCount - ult > 30000) ApagarPantalla();
                if (SuspenderTrasMin > 0 && reloj.Elapsed.TotalMinutes >= SuspenderTrasMin) Fin("suspender");
            }

            void ApagarPantalla()
            {
                entradaPantalla = UltimaEntrada();
                pantallaApagada = DateTime.UtcNow;
                try { SendMessage(Handle, 0x0112, (IntPtr)0xF170, (IntPtr)2); } catch { } // WM_SYSCOMMAND, SC_MONITORPOWER, apagada
            }

            IntPtr TeclaLL(int code, IntPtr wParam, IntPtr lParam)
            {
                if (code >= 0 && Porque == null)
                {
                    int msg = (int)wParam;
                    if ((msg == 0x100 || msg == 0x104) && (Marshal.ReadInt32(lParam, 8) & 0x10) == 0 && reloj.Elapsed.TotalSeconds > 2) // sin LLKHF_INJECTED
                    {
                        try { BeginInvoke((MethodInvoker)delegate { Fin("una tecla"); }); } catch { }
                    }
                }
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            }

            void Fin(string porque)
            {
                if (Porque != null) return;
                Porque = porque;
                timer.Stop();
                Log.Write("Espera: " + porque + " (" + (int)reloj.Elapsed.TotalMinutes + " min en espera)");
                QuitarSonda();
                Close();
            }

            void QuitarSonda()
            {
                if (sonda == null) return;
                try { if (!sonda.HasExited) { sonda.Kill(); sonda.WaitForExit(3000); } } catch { }
                try { sonda.Dispose(); } catch { }
                sonda = null;
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                timer.Stop();
                if (hKbd != IntPtr.Zero) { UnhookWindowsHookEx(hKbd); hKbd = IntPtr.Zero; }
                try { Microsoft.Win32.SystemEvents.SessionEnding -= AlApagarWindows; } catch { }
                QuitarSonda();
                base.OnFormClosed(e);
            }
        }

        // ------------------------------------------------------------------ el plan de energia "Economizador" mientras dura
        // Lo que hace Windows en su reposo: CPU al minimo, PCIe (la grafica) y USB en ahorro. Se pone el plan Economizador de Windows
        // (si existe) y al salir se vuelve al que habia; el que habia se apunta en data\standby_plan.txt por si la app muere en espera
        // (al abrirla otra vez se devuelve). Sin el plan, nada.
        static readonly Guid PlanEconomizador = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");
        static string PlanFile { get { return Path.Combine(Paths.Datos, "standby_plan.txt"); } }

        public static void PlanDeAntes(string cuando) { Economizador(false, cuando); }

        static void Economizador(bool poner, string cuando)
        {
            try
            {
                if (poner)
                {
                    IntPtr pa;
                    if (PowerGetActiveScheme(IntPtr.Zero, out pa) != 0) return;
                    Guid antes = (Guid)Marshal.PtrToStructure(pa, typeof(Guid));
                    LocalFree(pa);
                    if (antes == PlanEconomizador) return;
                    Guid g = PlanEconomizador;
                    uint r = PowerSetActiveScheme(IntPtr.Zero, ref g);
                    if (r != 0) { Log.Write("Espera: sin el plan Economizador (" + r + "): se queda el de ahora"); return; }
                    try { File.WriteAllText(PlanFile, antes.ToString()); } catch { }
                    Log.Write("Espera: plan de energia Economizador (" + cuando + "; el de antes, " + antes + ", vuelve al despertar)");
                }
                else
                {
                    if (!File.Exists(PlanFile)) return;
                    Guid antes = new Guid(File.ReadAllText(PlanFile).Trim());
                    uint r = PowerSetActiveScheme(IntPtr.Zero, ref antes);
                    if (r == 0) { File.Delete(PlanFile); Log.Write("Espera: plan de energia de antes otra vez (" + cuando + ")"); }
                    else Log.Write("Espera: no se pudo volver al plan de energia de antes (" + r + ")");
                }
            }
            catch (Exception ex) { Log.Error("Espera: plan de energia", ex); }
        }

        // Encender la pantalla: Windows la enciende con entrada; un movimiento del raton de 0 px (inyectado) y "pantalla necesaria"
        static void Pantalla(bool encender)
        {
            if (!encender) return;
            SetThreadExecutionState(EsDisplayRequired);
            try { mouse_event(0x0001, 1, 0, 0, UIntPtr.Zero); mouse_event(0x0001, -1, 0, 0, UIntPtr.Zero); } catch { }
        }

        // EcoQoS (Windows 11): el proceso a los nucleos de bajo consumo y a poca frecuencia. IntPtr.Zero = este proceso
        static void EcoQos(IntPtr h)
        {
            try
            {
                if (h == IntPtr.Zero) h = GetCurrentProcess();
                PowerThrottling t = new PowerThrottling();
                t.Version = 1; t.ControlMask = 1; t.StateMask = 1; // PROCESS_POWER_THROTTLING_EXECUTION_SPEED
                SetProcessInformation(h, 4, ref t, Marshal.SizeOf(typeof(PowerThrottling))); // ProcessPowerThrottling
            }
            catch { }
        }

        static uint UltimaEntrada()
        {
            LastInput i = new LastInput();
            i.cbSize = (uint)Marshal.SizeOf(typeof(LastInput));
            return GetLastInputInfo(ref i) ? i.dwTime : 0;
        }

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        const uint EsContinuous = 0x80000000, EsSystemRequired = 0x1, EsDisplayRequired = 0x2;
        [StructLayout(LayoutKind.Sequential)] struct PowerThrottling { public uint Version, ControlMask, StateMask; }
        [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint cbSize, dwTime; }
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint f);
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr h, int cls, ref PowerThrottling info, int size);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput i);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr wParam, IntPtr lParam);
    }
}
