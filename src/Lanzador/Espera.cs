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
//
// 21-11 (1.2.1, el usuario: "que el modo de consola sea mas cercano a que el pc este casi apagado pero siga funcionando como quiero"):
//  - Nada de Windows a la vista: la cubierta negra y la barra oculta (src\Lanzador\EsperaCubierta.cs) desde PowerOffNow hasta que vuelve
//    la imagen; la pantalla se apaga lo primero (antes, tras arrancar los mandos, el gancho y la sonda: ~0,5 s mas encendida).
//  - La espera en un proceso suyo (--espera), pequeno: antes seguia en el de la app tras "Fin ConsoleEnhancer" y con el seguian sus
//    hilos (el bucle del mando cada 50 ms, el modo PC del mando, que con el stick movia el raton y encendia la pantalla, la copia de
//    seguridad cada 2 min...). El de la app espera a que la nueva tenga su cubierta puesta y se cierra. Si no arranca, como antes.
//    Medido (prueba sin consola, pantalla encendida por otros programas): 0,3-0,8 % de un nucleo, ~38 MB, 16 hilos; con la pantalla
//    apagada la cubierta no despierta nada.
//  - La sonda sin los scripts de serie de mpv ni terminal y sin subir la resolucion del temporizador de Windows: mpv la pone a 1 ms
//    (timeBeginPeriod) para todo el PC mientras esta abierto (medido: 15,6 ms -> 1 ms); con PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION
//    (Windows 11) se queda en 15,6 ms. Medido con una fuente falsa a 59,94: 158 -> 109 Mciclos/s y 425 -> 189 cambios de contexto/s.
//    La consola se ve antes: la segunda mirada a los 0,4 s (ce_standby.lua) y el cierre de la sonda avisa al momento (Process.Exited).
//  - Plan de energia propio mientras dura: copia del Economizador con el procesador a espera_cpu_max % (50 de serie: el P-state mas bajo
//    en un Ryzen 5 2600; medido "% rendimiento del procesador" 110 % -> 45 %), sin turbo, el disco duro parado a los 10 min y la pantalla a
//    1 min (por si algo la enciende). Se borra al despertar (y al abrir la app si se quedo). Sin poder crearlo, el Economizador como antes.
//    Volver al plan de antes tarda 0,8-1,5 s (el Economizador tal cual, 0,9-1,1 s): se hace antes de abrir la app, que apunta el plan
//    que hay para devolverlo al salir, y el plan propio se borra (0,2-0,3 s) ya abierta.
//  - La pantalla encendida sin que nadie toque nada (otro programa pide pantalla, ES_DISPLAY_REQUIRED): otra vez apagada a los 2 s, y si
//    vuelve una y otra vez, cada vez mas tarde (10, 30 s, 2 y 5 min).
//  - Las teclas Win y de menu en espera no llegan a Windows (despiertan, pero no abren Inicio); el resto va a la cubierta, que no hace
//    nada con ellas (ni Alt+F4 la cierra).
//  - Al despertar: la pantalla lo primero (la tele tarda en encenderse; delante, negro), el plan de antes antes de abrir la app y el
//    --inicio con su telon antes de que se cierre esta.
//
// 22-11 (1.2.2, el usuario: "Haz que el pc este mas dormido en el modo consola, que gaste menos"). Medido en espera (RTX 4060 Ti, Ryzen 5
// 2600, prueba con la GC573 en "No Signal"): la grafica ya estaba en lo mas bajo (P8, 210/405 MHz, ~8,2 W con la pantalla apagada y
// ~12 W encendida); lo que se movia era la sonda de mpv (13,5 % de un nucleo y ~405 cambios de contexto por segundo con el procesador al
// 50 %: la capturadora mandando 60 imagenes por segundo sin parar) y la pantalla que se quedaba encendida:
//  - La sonda sin mpv (src\Lanzador\EsperaSonda.cs, ConsoleEnhancer.exe --ce-sonda): el grafo de DirectShow montado una vez y en marcha
//    solo un momento cada espera_sonda_cada_ms (2000 de serie); entre medias la capturadora parada. Si no se puede (codigo 3, tres
//    veces), la de mpv como antes. La de mpv abriendo y cerrando a ratos no valia: cada apertura cuesta ~0,4 s de CPU.
//  - La pantalla: "alguien la ha tocado" con el raton o el teclado por Raw Input (PadHub), no con GetLastInputInfo, que cuenta tambien un
//    mando XInput encendido: con el mando del PC encima de la mesa la pantalla (la tele) no se volvia a apagar.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
        // (1.2.1) espera_cpu_max: estado maximo del procesador en espera (50 de serie; 0 o 100 = el del Economizador, sin plan propio)
        public static volatile int CpuMax = 50;
        // (1.2.1) espera_sonda_fuente: solo pruebas, lo que mira la sonda en vez de la capturadora (p. ej. av://lavfi:...)
        public static string SondaFuente = "";
        // (1.2.2) espera_sonda_cada_ms: la sonda sin mpv (src\Lanzador\EsperaSonda.cs) mira la capturadora un momento cada tantos ms
        // (2000 de serie); 0 = la de mpv con la capturadora abierta todo el rato, como hasta la 1.2.1
        public static volatile int SondaCadaMs = 2000;
        // (1.2.1) pruebas sin consola (--prueba-cadena, --prueba-espera-simulada): segundos hasta que la consola de mentira se enciende
        public static volatile int Simulada;
        public static volatile bool Prueba;   // (--prueba-espera: sin capturadora no se suspende el PC)
        // 22-11 (1.2.2) SOLO PRUEBAS: con la variable de entorno CE_PRUEBA_PANTALLA_ENCENDIDA=1 la espera no apaga la pantalla (ni
        // SC_MONITORPOWER ni la pantalla a 1 min del plan propio) ni pone la cubierta negra ni oculta la barra: para medir lo demas (la
        // sonda, el proceso de la espera, el plan) sin que la tele se apague (apagada no se vuelve a encender por programa y Windows la deja
        // a 60 Hz). La heredan los procesos que abre (--espera, --ce-sonda, --inicio)
        public static readonly bool PantallaEncendida = Environment.GetEnvironmentVariable("CE_PRUEBA_PANTALLA_ENCENDIDA") == "1";

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
"   Cómo funciona: al apagar la consola, sin cuenta atrás, la pantalla se queda en negro y se apaga, se cierra todo y Windows pasa a un plan de bajo consumo (el procesador al mínimo y sin turbo); solo queda un vigilante muy pequeño que mira la capturadora un momento cada 2 segundos (entre medias, parada), " +
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
"   How it works: when the console turns off, with no countdown, the screen stays black and goes off, everything closes and Windows switches to a low-power plan (lowest processor speed, no boost); only a tiny watcher stays, checking the capture card for a moment every 2 seconds (stopped in between), " +
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
        static string device = "", devicePath = "", pixfmt = "", testSource = "";

        static string Exe { get { return Path.GetFullPath(System.Reflection.Assembly.GetEntryAssembly().Location); } }

        // Al abrir la app: la capturadora de la sonda
        public static void Recordar(Config cfg)
        {
            Economizador(false, "al abrir la app"); // (si la espera acabo mal y el plan de energia se quedo en Economizador)
            device = cfg.Device ?? "";
            pixfmt = cfg.PixelFormat ?? "";
            testSource = cfg.TestSource ?? "";
            devicePath = Ce.IniLeer("device_path") ?? "";
        }

        // (1.2.1) Lo que la espera lee del ini (en su proceso no hay Config)
        static void Leer()
        {
            if (device.Length == 0) device = Ce.IniLeer("device") ?? "";
            if (devicePath.Length == 0) devicePath = Ce.IniLeer("device_path") ?? "";
            if (pixfmt.Length == 0) pixfmt = Ce.IniLeer("pixel_format") ?? "";
            if (testSource.Length == 0) testSource = Ce.IniLeer("test_source") ?? "";
            string v = Ce.IniLeer("espera_sonda_fuente");
            if (!string.IsNullOrEmpty(v)) SondaFuente = v.Trim();
            int n;
            if (int.TryParse(Ce.IniLeer("espera_suspender_min") ?? "", out n)) SuspenderTrasMin = Math.Max(0, n);
            if (int.TryParse(Ce.IniLeer("espera_cpu_max") ?? "", out n)) CpuMax = Math.Max(0, Math.Min(100, n));
            if (int.TryParse(Ce.IniLeer("espera_sonda_cada_ms") ?? "", out n)) SondaCadaMs = n <= 0 ? 0 : Math.Max(250, Math.Min(30000, n));
            v = Ce.IniLeer("espera_ocultar_barra");
            if (v != null) CeBarra.Permitido = v.Trim() != "0";
        }

        // ------------------------------------------------------------------ 1.2.1: la consola apagada, la app cerrandose
        // PowerOffNow (Switch2Magpie.cs), ANTES de cerrar mpv / Magpie / el motor: con apagar_pc=1 el PC va a quedarse en espera (o a
        // dormirse / apagarse): la cubierta negra delante y la barra de tareas oculta. Con apagar_pc=0 (el PC normal) nada
        public static void AlApagarConsola(bool apagarPc)
        {
            if (!apagarPc || PantallaEncendida) return;
            try
            {
                CeCubierta.Poner("consola apagada", true);
                CeBarra.Ocultar("consola apagada (apagar_pc=1)");
            }
            catch (Exception ex) { Log.Error("Cubierta al apagar la consola", ex); }
        }

        // Main, al terminar la app: si el PC no va a la espera (cerrada a mano, apagar_pc=0, o apagar_pc_confirmar dice que no), fuera la
        // cubierta y la barra otra vez visible (si la habia ocultado la app)
        public static void AlCerrarApp(bool apagaPc)
        {
            if (apagaPc) return;
            try
            {
                CeCubierta.Quitar("el PC sigue encendido");
                CeBarra.Mostrar("la app se cierra y el PC sigue encendido");
            }
            catch (Exception ex) { Log.Error("Cubierta al cerrar la app", ex); }
        }

        // Apagando el PC de verdad (apagar_pc_modo=apagar): la cubierta hasta que Windows cierre la sesion; si en 20 s no empieza a
        // apagarse (algo lo ha parado), fuera y la barra visible
        public static void TrasApagar()
        {
            if (!CeCubierta.Puesta) return;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 60)
            {
                Thread.Sleep(500);
                if (GetSystemMetrics(0x2000) == 0 && sw.Elapsed.TotalSeconds >= 20) break; // SM_SHUTTINGDOWN
            }
            Log.Write("Apagado del PC: Windows no se ha apagado en " + (int)sw.Elapsed.TotalSeconds + " s: fuera la cubierta");
            AlCerrarApp(false);
        }

        // ------------------------------------------------------------------ la espera en su propio proceso (--espera)
        const string EventoLista = "Local\\ConsoleEnhancer_EsperaLista";
        public const string EventoTelon = "Local\\ConsoleEnhancer_TelonArriba";
        const string MutexApp = "Local\\ConsoleEnhancer_SingleInstance";
        static Mutex mutexApp;

        // Lo que pasa la app al proceso nuevo (los mismos datos e ini de pruebas)
        static string ArgsDatos()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string s in Environment.GetCommandLineArgs())
                if (s.StartsWith("--datos=", StringComparison.OrdinalIgnoreCase) || s.StartsWith("--ini=", StringComparison.OrdinalIgnoreCase))
                    sb.Append(" \"").Append(s).Append("\"");
            return sb.ToString();
        }

        // ApagarPc (modo espera): la espera en un proceso nuevo; este (el de la app) se cierra en cuanto la nueva tiene la cubierta puesta
        public static bool EntrarAparte()
        {
            Process p = null;
            try
            {
                using (EventWaitHandle lista = new EventWaitHandle(false, EventResetMode.ManualReset, EventoLista))
                {
                    lista.Reset();
                    Stopwatch sw = Stopwatch.StartNew();
                    p = CeCubierta.Lanzar(Exe, "--espera" + (Simulada > 0 ? " --prueba-simulada=" + Simulada : "") + ArgsDatos(), false);
                    if (p == null) throw new InvalidOperationException("el proceso de la espera no ha arrancado");
                    try { AllowSetForegroundWindow(p.Id); } catch { }
                    if (lista.WaitOne(8000))
                    {
                        Log.Write("Espera: en su propio proceso (pid " + p.Id + ", lista en " + sw.ElapsedMilliseconds + " ms); este se cierra");
                        // (la suya ya esta encima: la de este fuera, por si este proceso tardara en terminar y le tapara luego la app)
                        CeCubierta.Pausa(true);
                        CeCubierta.Quitar("la espera sigue en su proceso");
                        return true;
                    }
                    Log.Write("Espera: el proceso nuevo (pid " + p.Id + ") no ha contestado en 8 s: se queda en este, como antes");
                    try { if (!p.HasExited) p.Kill(); } catch { }
                }
            }
            catch (Exception ex) { Log.Error("Espera: no se pudo abrir su proceso (se queda en este)", ex); }
            return Entrar();
        }

        // --espera [--prueba-simulada=<s>]: el proceso de la espera
        public static int Proceso(string[] args)
        {
            foreach (string a in args)
                if (a.StartsWith("--prueba-simulada=")) { int s; if (int.TryParse(a.Substring(18), out s)) Simulada = Math.Max(1, s); }
            Log.Write("Espera: proceso propio (pid " + Process.GetCurrentProcess().Id + ")" + (Simulada > 0 ? " (PRUEBA: consola de mentira a los " + Simulada + " s)" : ""));
            if (!PantallaEncendida) CeCubierta.Poner("espera", true);   // (encima de la del proceso de la app, que aun esta)
            CeBarra.Adoptar("la espera (pid " + Process.GetCurrentProcess().Id + ")");
            EventWaitHandle e;
            if (EventWaitHandle.TryOpenExisting(EventoLista, out e)) { e.Set(); e.Dispose(); }
            // El mutex de la app, como cuando la espera seguia en su proceso (el modo PC residente del mando, el Panel y --inicio lo miran):
            // la app se esta cerrando; se suelta antes de abrirla otra vez
            if (Simulada == 0)
                try
                {
                    mutexApp = new Mutex(false, MutexApp);
                    bool mio;
                    try { mio = mutexApp.WaitOne(10000); }
                    catch (AbandonedMutexException) { mio = true; }
                    if (!mio) { mutexApp.Dispose(); mutexApp = null; Log.Write("Espera: la app sigue con su mutex tras 10 s (sigue sin el)"); }
                }
                catch (Exception ex) { Log.Error("Espera: mutex de la app", ex); mutexApp = null; }
            try { return Entrar() ? 0 : 1; }
            finally { SoltarMutex(); }
        }

        static void SoltarMutex()
        {
            Mutex m = mutexApp;
            mutexApp = null;
            if (m == null) return;
            try { m.ReleaseMutex(); } catch { }
            try { m.Dispose(); } catch { }
        }

        // ------------------------------------------------------------------ la espera
        // true = el PC ha estado en espera y se ha vuelto a abrir la app (o no se apaga); false = no se puede: lo de antes
        public static bool Entrar()
        {
            Leer();
            string mpv = Path.Combine(Paths.AppDir, @"mpv\mpv.exe"), lua = Path.Combine(Paths.AppDir, @"scripts\ce_standby.lua");
            bool prueba = Simulada > 0 || Prueba;
            string fuente = Fuente();
            if (fuente == null && testSource.Length > 0)
                Log.Write("Espera: test_source en el ini (pruebas): sin sonda, la capturadora no se abre (la despiertan los mandos o una tecla)");
            else if (fuente == null || !File.Exists(mpv) || !File.Exists(lua))
            {
                // (1.2.1: en las pruebas nunca se suspende el PC)
                if (prueba) { Log.Write("Espera (prueba): sin capturadora, mpv o scripts\\ce_standby.lua: nada que hacer, el PC no se suspende"); AlCerrarApp(false); return true; }
                Log.Write("Espera: sin capturadora, mpv o scripts\\ce_standby.lua: se suspende el PC");
                return CeSuspension.SuspenderYVolver();
            }
            Log.Write("Consola apagada: el PC en espera (apagar_pc_modo=espera): pantalla apagada; la consola, un mando o una tecla lo encienden");
            if (PantallaEncendida)
                Log.Write("Espera: PRUEBA con CE_PRUEBA_PANTALLA_ENCENDIDA=1: la pantalla no se apaga, sin cubierta negra ni la barra oculta");
            else
            {
                CeCubierta.Poner("espera", true);
                CeBarra.Ocultar("espera");
            }
            // (1.2.1: por debajo de lo normal, no "inactiva": con algo de Windows ocupando la CPU (un analisis de Defender, una
            // actualizacion), una prioridad "inactiva" solo corre cuando nadie mas quiere la CPU: el gancho del teclado puede tardar mas de
            // lo que Windows le deja (y lo quita) y la sonda ve tarde la consola. Gastan lo mismo: casi nada)
            try { using (Process me = Process.GetCurrentProcess()) me.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            EcoQos(IntPtr.Zero);
            SetThreadExecutionState(EsContinuous | EsSystemRequired);
            string porque = null;
            Process sonda = null;
            using (Vigia v = new Vigia(mpv, lua, fuente))
            {
                Application.Run(v);   // (el plan de la espera, en OnShown con la pantalla ya apagada)
                porque = v.Porque;
                sonda = v.SondaFinal;
            }
            SetThreadExecutionState(EsContinuous);
            if (porque == "suspender")
            {
                Economizador(false, "fin de la espera");
                EsperarSonda(sonda);
                Log.Write("Espera: " + SuspenderTrasMin + " min sin nada (espera_suspender_min): suspension de verdad");
                return CeSuspension.SuspenderYVolver();
            }
            if (porque == null || porque == "windows")
            {
                Economizador(false, "fin de la espera");
                EsperarSonda(sonda);
                Log.Write("Espera: terminada (" + (porque ?? "sin motivo") + "), sin abrir la app");
                return true;
            }
            // Despierto: la pantalla lo primero (delante sigue la cubierta negra), el plan de antes ANTES de abrir la app y el --inicio, que
            // pone su telon antes de que esta se cierre
            Stopwatch reloj = Stopwatch.StartNew();
            Pantalla(true);
            Economizador(false, "fin de la espera", false);
            long plan = reloj.ElapsedMilliseconds;
            SoltarMutex();
            EventWaitHandle telon = null;
            try { telon = new EventWaitHandle(false, EventResetMode.ManualReset, EventoTelon); telon.Reset(); } catch { telon = null; }
            CeCubierta.Pausa(true); // (ya no se vuelve a poner encima: el telon de --inicio y la pantalla de carga de la app, delante)
            try
            {
                using (Process p = CeCubierta.Lanzar(Exe, CeInicio.ArgumentosInicio() + " --tras-suspension --desde-espera" + (prueba ? " --prueba-app=6" : ""), false))
                    if (p != null) try { AllowSetForegroundWindow(p.Id); } catch { }
                Log.Write("Espera: despierto por " + porque + ": abriendo la app" + (prueba ? " (PRUEBA: la app de mentira)" : "") + " (plan de antes en " + plan + " ms)");
            }
            catch (Exception ex) { Log.Error("Espera: no se pudo abrir la app", ex); }
            BorrarPlanPropio();   // (ya no esta activo: mientras arranca la app)
            EsperarSonda(sonda);
            bool visto = telon != null && telon.WaitOne(5000);
            if (telon != null) telon.Dispose();
            Log.Write("Espera: " + (visto ? "telon de --inicio a la vista" : "sin ver el telon de --inicio en 5 s") + " a los " + reloj.ElapsedMilliseconds + " ms de despertar; se cierra la espera");
            return true;
        }

        // Lo que mira la sonda: la capturadora, la fuente de pruebas (espera_sonda_fuente) o, en las pruebas sin nada, una pantalla
        // "No Signal" de mentira que a los Simulada s pasa a imagen. null = nada (test_source sin fuente de la espera)
        static string Fuente()
        {
            if (SondaFuente.Length > 0) return SondaFuente;
            if (Simulada > 0)
                return "av://lavfi:color=c=black:s=1280x720:r=60000/1001,drawbox=x=128:y=232:w=1024:h=226:color=0x909090:t=fill," +
                       "drawbox=x=0:y=0:w=1280:h=720:color=0x2050A0:t=fill:enable='gte(t," + Simulada + ")',realtime";
            if (testSource.Length > 0 || device.Length == 0) return null;
            return "av://dshow:video=" + device;
        }

        static void EsperarSonda(Process s)
        {
            if (s == null) return;
            try { if (!s.HasExited && !s.WaitForExit(3000)) Log.Write("Espera: la sonda (pid " + s.Id + ") sigue sin cerrarse tras 3 s"); } catch { }
            try { s.Dispose(); } catch { }
        }

        // La ventana invisible que lo vigila todo (hilo de la UI: el gancho del teclado y los mensajes de Windows)
        sealed class Vigia : Form
        {
            readonly string mpv, lua, fuente;
            readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            readonly System.Windows.Forms.Timer reapagar = new System.Windows.Forms.Timer();
            readonly Stopwatch reloj = Stopwatch.StartNew();
            Process sonda;
            DateTime sondaOtraVez = DateTime.MinValue;
            int sondaFallos;
            bool mandos;
            int mandosAntes;
            uint botonesAntes;
            uint entradaPantalla;           // GetLastInputInfo cuando se apago la pantalla
            DateTime pantallaApagada = DateTime.MinValue;
            int sola;                       // (1.2.1) veces que la pantalla se ha encendido sin que nadie toque nada
            HookProc kbdProc;
            IntPtr hKbd;
            public string Porque;
            public Process SondaFinal;      // (la sonda matada al despertar: se espera a que suelte la capturadora mientras abre la app)

            public Vigia(string mpv, string lua, string fuente)
            {
                this.mpv = mpv; this.lua = lua; this.fuente = fuente;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Bounds = new System.Drawing.Rectangle(-32000, -32000, 1, 1);
                Opacity = 0;
                Text = "ConsoleEnhancer - espera";
                timer.Interval = 1000; // (1 vez por segundo: menos despertares de la CPU)
                timer.Tick += delegate { Paso(); };
                reapagar.Interval = 2000;
                reapagar.Tick += delegate { reapagar.Stop(); if (Porque == null && !Tocado()) { solaAntes = DateTime.UtcNow; ApagarPantalla(); } };
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                ApagarPantalla(); // (1.2.1: lo primero; delante, la cubierta negra)
                Economizador(true, "en espera");
                try { PadHub.Shared.Start(); PadSnapshot s; PadHub.Shared.Poll(out s); mandosAntes = s.Count; botonesAntes = s.Buttons; mandos = true; }
                catch (Exception ex) { Log.Error("Espera: mandos", ex); }
                kbdProc = TeclaLL;
                try { hKbd = SetWindowsHookEx(13, kbdProc, GetModuleHandle(null), 0); } catch { }
                Microsoft.Win32.SystemEvents.SessionEnding += AlApagarWindows;
                CeCubierta.AlCambiarPantalla = delegate (int v) { try { BeginInvoke((MethodInvoker)delegate { PantallaCambia(v); }); } catch { } };
                if (fuente != null) Lanzar();
                timer.Start();
#pragma warning disable 618
                int hilo = AppDomain.GetCurrentThreadId();
#pragma warning restore 618
                Log.Write("Espera: vigilando (" + mandosAntes + " mandos conectados" + (hKbd != IntPtr.Zero ? ", teclado" : "") + ", sonda " + (sonda != null ? "pid " + sonda.Id : "sin abrir") +
                          "; hilos: vigia " + hilo + ", cubierta " + CeCubierta.Hilo + ")");
            }

            void AlApagarWindows(object s, Microsoft.Win32.SessionEndingEventArgs e) { Fin("windows"); }

            // (1.2.1) La pantalla encendida sin entrada del usuario desde que se apago (otro programa pide pantalla): otra vez apagada a los
            // 2 s. Con el raton movido, lo de siempre (a los 30 s sin tocar nada, Paso). Si vuelve a encenderse enseguida una y otra vez (un
            // programa que pide pantalla sin parar, p. ej. un video o el motor de la app en otra prueba: SetThreadExecutionState con
            // ES_DISPLAY_REQUIRED la enciende ~1 s despues de cada apagado), se espera cada vez mas (2, 10, 30 s, 2 y 5 min): mejor la
            // cubierta negra encendida que la tele encendiendose y apagandose cada 3 s
            DateTime solaAntes = DateTime.MinValue;
            int seguidasSola;
            void PantallaCambia(int v)
            {
                if (Porque != null || v != 1) return;
                if (Tocado()) return;
                seguidasSola = (DateTime.UtcNow - solaAntes).TotalSeconds < 15 ? seguidasSola + 1 : 0;
                int[] pasos = { 2, 10, 30, 120, 300 };
                int s = pasos[Math.Min(seguidasSola, pasos.Length - 1)];
                sola++;
                if (sola <= 5 || sola % 20 == 0)
                    Log.Write("Espera: la pantalla se ha encendido sin que nadie toque nada (otro programa pide pantalla o Windows; " + sola + "a vez): otra vez apagada en " + s + " s");
                reapagar.Stop();
                reapagar.Interval = s * 1000;
                reapagar.Start();
            }

            // (1.2.2) la sonda sin mpv (src\Lanzador\EsperaSonda.cs) con la capturadora de verdad: el grafo montado una vez y en marcha solo
            // un momento cada SondaCadaMs. Si no puede montarlo o arrancarlo (codigo 3) 3 veces, la de mpv como antes
            bool nativa;
            int nativaFallos;
            EventWaitHandle latido;          // (lo pone la sonda sin mpv en cada vez: sin el en 30 s, colgada)
            DateTime ultimoLatido;

            void Lanzar()
            {
                bool captura = fuente.StartsWith("av://dshow:", StringComparison.OrdinalIgnoreCase);
                nativa = captura && SondaCadaMs > 0 && nativaFallos < 3;
                if (nativa)
                {
                    try
                    {
                        string nombre = fuente.StartsWith("av://dshow:video=", StringComparison.OrdinalIgnoreCase) ? fuente.Substring(17) : device;
                        bool laDelIni = nombre.Equals(device, StringComparison.OrdinalIgnoreCase);   // (con espera_sonda_fuente, otra: sin su ruta)
                        string args = "--ce-sonda \"dispositivo=" + nombre + "\"" + (laDelIni && devicePath.Length > 0 ? " \"ruta=" + devicePath + "\"" : "") +
                                      " cada_ms=" + SondaCadaMs + (pixfmt.Length > 0 ? " formato=" + pixfmt : "") + ArgsDatos();
                        if (latido == null) try { latido = new EventWaitHandle(false, EventResetMode.AutoReset, CeSonda.Latido); } catch { latido = null; }
                        ultimoLatido = DateTime.UtcNow;
                        sonda = CeCubierta.Lanzar(Exe, args, true);
                        if (sonda == null) throw new InvalidOperationException("no ha arrancado");
                        Preparar(sonda);
                        return;
                    }
                    catch (Exception ex) { Log.Error("Espera: la sonda sin mpv (sigue la de mpv)", ex); sonda = null; nativa = false; nativaFallos = 3; }
                }
                try
                {
                    // (otra capturadora que no acepte 1280x720: tras 2 fallos, lo que ella quiera)
                    string lavf = (sondaFallos % 4 < 2 ? "video_size=1280x720" + (pixfmt.Length > 0 ? ",pixel_format=" + pixfmt : "") + "," : "") + "rtbufsize=16000000";
                    // 1.2.1: sin los scripts de serie de mpv (osc, ytdl, stats, consola, perfiles...: 7 hilos menos) ni terminal; si mpv no
                    // arrancara asi 4 veces seguidas, otras 4 como antes
                    bool ligera = (sondaFallos / 4) % 2 == 0;
                    string args = "--no-config \"" + fuente + "\"" + (captura ? " --demuxer-lavf-o=" + lavf : "") +
                                  " --no-audio --vo=null --untimed --cache=no --hwdec=no --priority=belownormal --idle=no --input-default-bindings=no" +
                                  (ligera ? " --input-terminal=no --terminal=no --osc=no --ytdl=no --load-stats-overlay=no --load-console=no --load-auto-profiles=no" +
                                            " --load-select=no --load-commands=no --load-positioning=no" : "") +
                                  " --script=\"" + lua + "\" --msg-level=all=warn,ce_standby=info";
                    ProcessStartInfo psi = new ProcessStartInfo(mpv, args);
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    sonda = Process.Start(psi);
                    Preparar(sonda);
                }
                catch (Exception ex) { Log.Error("Espera: la sonda", ex); sonda = null; }
            }

            // La sonda (de mpv o la de --ce-sonda) con poca prioridad, EcoQoS y en el job
            void Preparar(Process p)
            {
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                EcoQos(p.Handle);
                EngineJob.Hold(p, true); // (si este proceso muere, Windows cierra la sonda: la capturadora no se queda cogida)
                // 1.2.1: al cerrarse la sonda (la consola vista), enseguida y no en el siguiente segundo de Paso (~0,5 s de media antes);
                // Windows avisa (una espera en su hilo de esperas, sin sondear)
                Process s = p;
                s.EnableRaisingEvents = true;
                s.Exited += delegate { try { BeginInvoke((MethodInvoker)delegate { if (sonda == s && Porque == null) Paso(); }); } catch { } };
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
                        if (nativa)
                        {
                            // (1.2.2) la sonda sin mpv: 3 = sin grafo (otra vez en 3, 6 s; a la tercera, la de mpv)
                            nativaFallos++;
                            sondaOtraVez = DateTime.UtcNow.AddSeconds(nativaFallos >= 3 ? 0 : 3 * nativaFallos);
                            Log.Write("Espera: la sonda sin mpv se ha cerrado (codigo " + code + "); " + (nativaFallos >= 3 ? "sigue la de mpv" : "otra vez en " + 3 * nativaFallos + " s"));
                        }
                        else
                        {
                            sondaFallos++;
                            sondaOtraVez = DateTime.UtcNow.AddSeconds(Math.Min(60, 3 * sondaFallos));
                            Log.Write("Espera: la sonda se ha cerrado (codigo " + code + "); otra vez en " + Math.Min(60, 3 * sondaFallos) + " s");
                        }
                    }
                    else if (nativa && latido != null)
                    {
                        // (1.2.2) la sonda sin mpv viva pero sin mirar (un Run o un Stop de la capturadora colgados): fuera y otra
                        bool late = false;
                        try { late = latido.WaitOne(0); } catch { }
                        if (late) ultimoLatido = DateTime.UtcNow;
                        else if ((DateTime.UtcNow - ultimoLatido).TotalSeconds > Math.Max(30, SondaCadaMs / 100))
                        {
                            Log.Write("Espera: la sonda sin mpv (pid " + sonda.Id + ") lleva " + (int)(DateTime.UtcNow - ultimoLatido).TotalSeconds + " s sin mirar: se cierra y se abre otra");
                            ultimoLatido = DateTime.UtcNow;
                            try { sonda.Kill(); } catch { }
                        }
                    }
                }
                else if (fuente != null && DateTime.UtcNow >= sondaOtraVez) Lanzar();
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
                if (Tocado() && MsSinTocar() > 30000) ApagarPantalla();
                if (SuspenderTrasMin > 0 && reloj.Elapsed.TotalMinutes >= SuspenderTrasMin) Fin("suspender");
            }

            void ApagarPantalla()
            {
                entradaPantalla = UltimaEntrada();
                rawRaton = PadHub.Shared.LastMouseTick; rawTeclado = PadHub.Shared.LastKbdTick;
                pantallaApagada = DateTime.UtcNow;
                if (PantallaEncendida) return;   // (SOLO PRUEBAS: CE_PRUEBA_PANTALLA_ENCENDIDA=1)
                try { SendMessage(Handle, 0x0112, (IntPtr)0xF170, (IntPtr)2); } catch { } // WM_SYSCOMMAND, SC_MONITORPOWER, apagada
            }

            // 22-11 (1.2.2): ¿alguien ha tocado el raton o el teclado desde que se apago la pantalla? Con Raw Input (PadHub, en este proceso),
            // solo el raton y los teclados (de verdad o inyectados); GetLastInputInfo cuenta tambien los mandos XInput (lo vio MenuJuego.cs:
            // jugando lo cambiaban cada pocos ms) y con un mando encendido encima de la mesa la pantalla no se volvia a apagar: en el log del
            // usuario (22-11 04:06:26), "pantalla encendida" 0,17 s despues de apagarla, con "1 mandos conectados", y asi el minuto entero
            int rawRaton, rawTeclado;
            bool Tocado()
            {
                PadHub h = PadHub.Shared;
                if (h.RawMouseOk) return h.LastMouseTick != rawRaton || h.LastKbdTick != rawTeclado;
                return UltimaEntrada() != entradaPantalla;
            }

            uint MsSinTocar()
            {
                PadHub h = PadHub.Shared;
                uint ahora = (uint)Environment.TickCount;
                if (!h.RawMouseOk) return ahora - UltimaEntrada();
                uint r = uint.MaxValue;
                if (h.LastMouseTick != rawRaton) r = Math.Min(r, ahora - (uint)h.LastMouseTick);
                if (h.LastKbdTick != rawTeclado) r = Math.Min(r, ahora - (uint)h.LastKbdTick);
                return r;
            }

            // 1.2.1: las teclas de Windows (Win y la de menu) no llegan a Windows mientras dura: la que despierta no abre Inicio encima de la
            // cubierta. El resto llega a la ventana de delante, la cubierta (no hace nada con ellas). Lo inyectado pasa y no despierta
            IntPtr TeclaLL(int code, IntPtr wParam, IntPtr lParam)
            {
                if (code >= 0 && (Marshal.ReadInt32(lParam, 8) & 0x10) == 0) // sin LLKHF_INJECTED
                {
                    int msg = (int)wParam;
                    if (Porque == null && (msg == 0x100 || msg == 0x104) && reloj.Elapsed.TotalSeconds > 2)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { Fin("una tecla"); }); } catch { }
                    }
                    int vk = Marshal.ReadInt32(lParam, 0);
                    if (vk == 0x5B || vk == 0x5C || vk == 0x5D) return (IntPtr)1; // VK_LWIN, VK_RWIN, VK_APPS
                }
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            }

            void Fin(string porque)
            {
                if (Porque != null) return;
                Porque = porque;
                timer.Stop();
                reapagar.Stop();
                Log.Write("Espera: " + porque + " (" + (int)reloj.Elapsed.TotalMinutes + " min en espera)");
                QuitarSonda();
                Close();
            }

            // (1.2.1) matada sin esperar: se espera a que suelte la capturadora mientras se abre la app (Entrar, EsperarSonda)
            void QuitarSonda()
            {
                if (sonda == null) return;
                try { if (!sonda.HasExited) sonda.Kill(); } catch { }
                SondaFinal = sonda;
                sonda = null;
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                timer.Stop();
                reapagar.Stop();
                CeCubierta.AlCambiarPantalla = null;
                if (hKbd != IntPtr.Zero) { UnhookWindowsHookEx(hKbd); hKbd = IntPtr.Zero; }
                try { Microsoft.Win32.SystemEvents.SessionEnding -= AlApagarWindows; } catch { }
                QuitarSonda();
                if (latido != null) { try { latido.Dispose(); } catch { } latido = null; }
                base.OnFormClosed(e);
            }
        }

        // ------------------------------------------------------------------ el plan de energia "Economizador" mientras dura
        // Lo que hace Windows en su reposo: CPU al minimo, PCIe (la grafica) y USB en ahorro. Se pone el plan Economizador de Windows
        // (si existe) y al salir se vuelve al que habia; el que habia se apunta en data\standby_plan.txt por si la app muere en espera
        // (al abrirla otra vez se devuelve). Sin el plan, nada.
        // 1.2.1: un plan propio (PlanEspera, "ConsoleEnhancer (espera)"), copia del Economizador (o del de ahora si no esta) con el
        // procesador a espera_cpu_max % y sin turbo, el disco duro parado a los 10 min y la pantalla a 1 min; se borra al volver (y al abrir la
        // app, si se quedo). La marca se apunta antes de cambiar de plan
        static readonly Guid PlanEconomizador = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");
        static readonly Guid PlanEspera = new Guid("c0e5e5a0-7e57-4c0e-9e5a-0000000c0121");
        static readonly Guid SubProcesador = new Guid("54533251-82be-4824-96c1-47b60b740d00"), ProcesadorMax = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec"),
                             Turbo = new Guid("be337238-0d82-4146-a960-4f3749d470c7");
        static readonly Guid SubDisco = new Guid("0012ee47-9041-4b5d-9b77-535fba8b1442"), DiscoApagar = new Guid("6738e2c4-e8a5-4a42-b16a-e040e769756e");
        static readonly Guid SubVideo = new Guid("7516b95f-f776-4464-8c53-06167f40cc99"), PantallaApagar = new Guid("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
        static string PlanFile { get { return Path.Combine(Paths.Datos, "standby_plan.txt"); } }

        public static void PlanDeAntes(string cuando) { Economizador(false, cuando); }

        static void Economizador(bool poner, string cuando) { Economizador(poner, cuando, true); }

        // borrar = false: el plan de la espera se borra despues (al despertar, ya abierta la app: borrarlo tarda ~0,2-0,3 s)
        static void Economizador(bool poner, string cuando, bool borrar)
        {
            try
            {
                if (poner)
                {
                    IntPtr pa;
                    if (PowerGetActiveScheme(IntPtr.Zero, out pa) != 0) return;
                    Guid antes = (Guid)Marshal.PtrToStructure(pa, typeof(Guid));
                    LocalFree(pa);
                    if (antes == PlanEconomizador || antes == PlanEspera) return;
                    Stopwatch sw = Stopwatch.StartNew();
                    try { File.WriteAllText(PlanFile, antes.ToString()); } catch { }
                    string propio = CpuMax > 0 && CpuMax < 100 ? PlanPropio(antes) : null;
                    Guid g = propio != null ? PlanEspera : PlanEconomizador;
                    uint r = PowerSetActiveScheme(IntPtr.Zero, ref g);
                    if (r != 0 && propio != null) { BorrarPlanPropio(); propio = null; g = PlanEconomizador; r = PowerSetActiveScheme(IntPtr.Zero, ref g); }
                    if (r != 0) { try { File.Delete(PlanFile); } catch { } Log.Write("Espera: sin el plan Economizador (" + r + "): se queda el de ahora"); return; }
                    Log.Write("Espera: plan de energia " + (propio != null ? "\"ConsoleEnhancer (espera)\" (" + propio + ")" : "Economizador") + " (" + cuando + "; el de antes, " + antes +
                              ", vuelve al despertar; " + sw.ElapsedMilliseconds + " ms)");
                }
                else
                {
                    if (File.Exists(PlanFile))
                    {
                        Guid antes = new Guid(File.ReadAllText(PlanFile).Trim());
                        Stopwatch sw = Stopwatch.StartNew();
                        uint r = PowerSetActiveScheme(IntPtr.Zero, ref antes);
                        if (r == 0) { File.Delete(PlanFile); Log.Write("Espera: plan de energia de antes otra vez (" + cuando + "; " + sw.ElapsedMilliseconds + " ms)"); }
                        else Log.Write("Espera: no se pudo volver al plan de energia de antes (" + r + ")");
                    }
                    if (borrar) BorrarPlanPropio();
                }
            }
            catch (Exception ex) { Log.Error("Espera: plan de energia", ex); }
        }

        // La copia del Economizador (o del plan de ahora) con lo de la espera. null = no se ha podido
        static string PlanPropio(Guid antes)
        {
            BorrarPlanPropio();
            Guid fuente = PlanEconomizador;
            IntPtr dst = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.StructureToPtr(PlanEspera, dst, false);
                uint r = PowerDuplicateScheme(IntPtr.Zero, ref fuente, ref dst);
                if (r != 0) { fuente = antes; Marshal.StructureToPtr(PlanEspera, dst, false); r = PowerDuplicateScheme(IntPtr.Zero, ref fuente, ref dst); }
                if (r != 0) { Log.Write("Espera: no se pudo crear el plan de la espera (" + r + ")"); return null; }
            }
            finally { Marshal.FreeHGlobal(dst); }
            Guid p = PlanEspera;
            Valor(p, SubProcesador, ProcesadorMax, (uint)CpuMax);
            Valor(p, SubProcesador, Turbo, 0);           // (0 = sin turbo)
            Valor(p, SubDisco, DiscoApagar, 600);   // (10 min: un disco duro que se para y arranca cada pocos minutos hace mas ruido)
            Valor(p, SubVideo, PantallaApagar, PantallaEncendida ? 0u : 60u);   // (SOLO PRUEBAS con CE_PRUEBA_PANTALLA_ENCENDIDA=1: nunca)
            try
            {
                byte[] n = Encoding.Unicode.GetBytes("ConsoleEnhancer (espera)\0");
                PowerWriteFriendlyName(IntPtr.Zero, ref p, IntPtr.Zero, IntPtr.Zero, n, (uint)n.Length);
            }
            catch { }
            return "copia del " + (fuente == PlanEconomizador ? "Economizador" : "plan de antes") + ", procesador al " + CpuMax + " % sin turbo, disco a los 10 min, pantalla " +
                   (PantallaEncendida ? "nunca (PRUEBA)" : "a 1 min");
        }

        static void Valor(Guid plan, Guid sub, Guid ajuste, uint v)
        {
            PowerWriteACValueIndex(IntPtr.Zero, ref plan, ref sub, ref ajuste, v);
            PowerWriteDCValueIndex(IntPtr.Zero, ref plan, ref sub, ref ajuste, v);
        }

        static void BorrarPlanPropio()
        {
            try
            {
                IntPtr pa;
                if (PowerGetActiveScheme(IntPtr.Zero, out pa) == 0)
                {
                    Guid act = (Guid)Marshal.PtrToStructure(pa, typeof(Guid));
                    LocalFree(pa);
                    if (act == PlanEspera)
                    {
                        // (activo sin la marca: la espera acabo mal antes de apuntarla) el Equilibrado de Windows
                        Guid eq = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
                        PowerSetActiveScheme(IntPtr.Zero, ref eq);
                    }
                }
                Guid g = PlanEspera;
                if (PowerDeleteScheme(IntPtr.Zero, ref g) == 0) Log.Write("Espera: plan \"ConsoleEnhancer (espera)\" borrado");
            }
            catch { }
        }

        // Encender la pantalla: Windows la enciende con entrada; un movimiento del raton de 0 px (inyectado) y "pantalla necesaria"
        static void Pantalla(bool encender)
        {
            if (!encender) return;
            SetThreadExecutionState(EsDisplayRequired);
            try { mouse_event(0x0001, 1, 0, 0, UIntPtr.Zero); mouse_event(0x0001, -1, 0, 0, UIntPtr.Zero); } catch { }
        }

        // EcoQoS (Windows 11): el proceso a los nucleos de bajo consumo y a poca frecuencia. IntPtr.Zero = este proceso
        // 1.2.1: y sin subir la resolucion del temporizador de Windows (PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION; mpv la pone a
        // 1 ms para todo el PC). Sin eso (Windows 10), solo EcoQoS
        static void EcoQos(IntPtr h)
        {
            try
            {
                if (h == IntPtr.Zero) h = GetCurrentProcess();
                PowerThrottling t = new PowerThrottling();
                t.Version = 1; t.ControlMask = 1 | 4; t.StateMask = 1 | 4; // EXECUTION_SPEED | IGNORE_TIMER_RESOLUTION
                if (!SetProcessInformation(h, 4, ref t, Marshal.SizeOf(typeof(PowerThrottling))))
                {
                    t.ControlMask = 1; t.StateMask = 1;
                    SetProcessInformation(h, 4, ref t, Marshal.SizeOf(typeof(PowerThrottling))); // ProcessPowerThrottling
                }
            }
            catch { }
        }

        static uint UltimaEntrada()
        {
            LastInput i = new LastInput();
            i.cbSize = (uint)Marshal.SizeOf(typeof(LastInput));
            return GetLastInputInfo(ref i) ? i.dwTime : 0;
        }

        // ------------------------------------------------------------------ 1.2.1: pruebas sin consola ni capturadora
        // --prueba-cadena[=<s>]: todo lo del apagado con una imagen de mentira: la "imagen de la consola" 3 s delante, la consola apagada
        // (cubierta y barra ANTES de quitarla, como PowerOffNow), el cierre de la app (0,7 s), la espera en su proceso con una sonda que ve
        // "No Signal" s segundos (de serie 20) y luego imagen, y al despertar --inicio con su telon y una app de mentira (--prueba-app):
        // la pantalla de carga de verdad y otra "imagen" que devuelve la barra, como la app al ver la consola. No abre la capturadora
        public static int PruebaCadena(int s) { return PruebaCadena(s, false); }

        // aviso = (--prueba-cadena-aviso=<s>) en vez de la espera, el aviso de apagado de los modos suspender / apagar encima de la
        // cubierta (sin apagar nada: "prueba: no se apaga") y, al acabar o cancelarlo, fuera la cubierta y la barra visible
        public static int PruebaCadena(int s, bool aviso)
        {
            Simulada = Math.Max(3, s);
            Log.Write("PRUEBA de la cadena del apagado: imagen de mentira, consola apagada, espera en su proceso (" + Simulada + " s) y al despertar la app de mentira");
            Application.EnableVisualStyles();
            using (Form img = Imagen("PRUEBA · la imagen de la consola", System.Drawing.Color.FromArgb(150, 40, 40)))
            {
                img.Show();
                img.Activate();
                Bombear(3000);
                AlApagarConsola(true);   // (lo de PowerOffNow con apagar_pc=1)
                img.Close();             // (como mpv / Magpie matados)
                Bombear(700);            // (lo que tarda la app en cerrarse: "Cierre por consola apagada: 761 ms")
            }
            Log.Write("===== Fin ConsoleEnhancer (PRUEBA) =====");
            if (aviso)
            {
                string porque;
                CeCubierta.Pausa(true);   // (lo mismo que Program.ApagarPc)
                bool seguir = AvisoApagado.Mostrar(Simulada, true, false, out porque);
                CeCubierta.Pausa(false);
                Log.Write("PRUEBA: aviso de apagado " + (seguir ? "terminado (aqui se suspenderia / apagaria el PC)" : "cancelado (" + porque + ")"));
                AlCerrarApp(false);
                return 0;
            }
            return EntrarAparte() ? 0 : 1;
        }

        // --prueba-espera-simulada[=<s>]: la espera en este mismo proceso (lo de antes, por si el proceso nuevo no arranca) con la sonda de
        // mentira; al despertar, la app de mentira
        public static int PruebaEsperaSimulada(int s)
        {
            Simulada = Math.Max(3, s);
            Log.Write("PRUEBA de la espera en este proceso (" + Simulada + " s hasta la consola de mentira)");
            return Entrar() ? 0 : 1;
        }

        // --prueba-app=<s> (lo abre --inicio al despertar en las pruebas): la pantalla de carga de la app 2,5 s, luego una "imagen de la
        // consola" s segundos; con ella delante, la barra otra vez visible (como TrayApp.BarraAlVolverLaImagen); al cerrar, AlCerrarApp
        public static int PruebaApp(int s)
        {
            Log.Write("PRUEBA: la app de mentira (pid " + Process.GetCurrentProcess().Id + (DesdeEspera ? ", desde la espera" : "") + ")");
            CeBarra.AlAbrir(DesdeEspera);
            Application.EnableVisualStyles();
            // (con su bucle de mensajes, como la app: con DoEvents la pantalla de carga a 4K y la CPU ocupada no lo soltaban)
            SplashForm carga = new SplashForm();
            Form img = Imagen("PRUEBA · la consola otra vez", System.Drawing.Color.FromArgb(30, 110, 60));
            ApplicationContext ctx = new ApplicationContext();
            Stopwatch sw = Stopwatch.StartNew();
            int paso = 0;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 100;
            t.Tick += delegate
            {
                long ms = sw.ElapsedMilliseconds;
                if (paso == 0 && ms >= 2500) { paso = 1; img.Show(); img.Activate(); }
                else if (paso == 1 && ms >= 2800) { paso = 2; carga.Close(); }
                else if (paso == 2 && ms >= 3800) { paso = 3; CeBarra.Mostrar("PRUEBA: la imagen de la consola ya esta delante"); }
                else if (paso == 3 && ms >= 3800 + Math.Max(1, s - 1) * 1000) { paso = 4; t.Stop(); img.Close(); ctx.ExitThread(); }
            };
            carga.Show();
            carga.Activate();
            t.Start();
            Application.Run(ctx);
            AlCerrarApp(false);
            Log.Write("PRUEBA: la app de mentira se cierra");
            return 0;
        }

        static Form Imagen(string texto, System.Drawing.Color fondo)
        {
            Form f = new Form();
            f.FormBorderStyle = FormBorderStyle.None;
            f.StartPosition = FormStartPosition.Manual;
            f.Bounds = Screen.PrimaryScreen.Bounds;
            f.BackColor = fondo;
            f.TopMost = true;
            f.ShowInTaskbar = false;
            f.Text = texto;
            f.Paint += delegate (object o, PaintEventArgs e)
            {
                using (System.Drawing.Font ft = new System.Drawing.Font("Segoe UI", 48))
                    e.Graphics.DrawString(texto, ft, System.Drawing.Brushes.White, 120, 120);
            };
            return f;
        }

        static void Bombear(int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(15); }
        }

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        const uint EsContinuous = 0x80000000, EsSystemRequired = 0x1, EsDisplayRequired = 0x2;
        [StructLayout(LayoutKind.Sequential)] struct PowerThrottling { public uint Version, ControlMask, StateMask; }
        [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint cbSize, dwTime; }
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint f);
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")] static extern uint PowerDuplicateScheme(IntPtr root, ref Guid src, ref IntPtr dst);
        [DllImport("powrprof.dll")] static extern uint PowerDeleteScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint v);
        [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint v);
        [DllImport("powrprof.dll")] static extern uint PowerWriteFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, byte[] name, uint size);
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
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    }
}
