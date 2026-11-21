// Suspension.cs - suspender el PC en vez de apagarlo al apagar la consola, y que un mando o un teclado lo despierten y vuelva a abrirse
// la app, como una consola (21-11-2026, 1.0.14).
//
//  - apagar_pc_modo=suspender (de serie) | apagar. Con apagar_pc=1 y la consola apagada: el mismo aviso que se puede cancelar y luego
//    suspension (S3, SetSuspendState) en vez de shutdown. Si el PC no puede suspenderse (sin S3 ni S0) o falla, se apaga como antes.
//  - Al despertar (PBT_APMRESUMEAUTOMATIC, llegue por lo que llegue: mando, teclado, boton de encendido) se abre la app con el telon
//    negro (--inicio --tras-suspension: espera a que este proceso suelte el mutex). El escritorio sigue negro mientras duerme.
//    Con la consola apagada la app espera a verla (no se cierra sola sin haberla visto), asi que no hay bucle de suspensiones.
//  - Despertar con el mando o el teclado: Windows solo deja despertar a los dispositivos "armados" (Administrador de dispositivos >
//    Administracion de energia > "Permitir que este dispositivo reactive el equipo"). La app arma (con un aviso de UAC, una vez por
//    dispositivo nuevo) los teclados, los mandos (Xbox/XInput, mandos HID y sus receptores USB: el dispositivo USB padre) y la radio
//    Bluetooth (mandos y teclados por Bluetooth). Tambien quita "pedir la contrasena al despertar" (CONSOLELOCK=0) para que no salga la
//    pantalla de bloqueo. Lo armado se apunta en data\wake_armed.txt; al abrir la app, si hay mandos o teclados nuevos, se vuelve
//    a pedir. Linea de comandos: --ce-despertar (lo pide con UAC), --ce-despertar-admin (el exe elevado), --prueba-suspender (suspende
//    de verdad, sin consola ni aviso, y abre la app al despertar).
//  - Ojo: un mando por Bluetooth o un receptor que se apaga del todo al dormir puede no despertar el PC (depende del hardware y de que la
//    BIOS deje la corriente en los USB en S3). Que despierte al ENCENDER el mando depende de que su receptor mande la senal de despertar.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // apagar_pc_modo: "espera" (de serie: src\Lanzador\Espera.cs), "suspender" (S3) o "apagar"
        public string ShutdownMode = "espera";

        bool CeParseSuspension(string key, string val)
        {
            if (key == "espera_volver_sin_consola_s")
            {
                int s;
                if (int.TryParse(val.Trim(), out s)) CeEspera.VolverSinConsolaS = Math.Max(0, s);
                return true;
            }
            if (key == "espera_suspender_min")
            {
                int m;
                if (int.TryParse(val.Trim(), out m)) CeEspera.SuspenderTrasMin = Math.Max(0, m);
                return true;
            }
            // 21-11 (1.2.1): la barra de tareas oculta desde la consola apagada hasta que vuelve su imagen (src\Lanzador\EsperaCubierta.cs),
            // el procesador en espera (src\Lanzador\Espera.cs) y, solo pruebas, lo que mira la sonda en vez de la capturadora
            if (key == "espera_ocultar_barra") { CeBarra.Permitido = val.Trim() != "0"; return true; }
            if (key == "espera_cpu_max")
            {
                int c;
                if (int.TryParse(val.Trim(), out c)) CeEspera.CpuMax = Math.Max(0, Math.Min(100, c));
                return true;
            }
            if (key == "espera_sonda_fuente") { CeEspera.SondaFuente = val.Trim(); return true; }
            if (key != "apagar_pc_modo") return false;
            ShutdownMode = CeSuspension.Normaliza(val);
            CeSuspension.Modo = ShutdownMode;
            return true;
        }
    }

    static partial class CeSuspension
    {
        public static volatile string Modo = "espera";
        public static bool NoApaga { get { return Modo != "apagar"; } }
        public static string Normaliza(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            if (v == "apagar" || v == "0" || v == "shutdown") return "apagar";
            if (v == "suspender" || v == "s3" || v == "sleep") return "suspender";
            return "espera";
        }
        // Lo que hace ApagarPc tras el aviso: true = hecho (no apagar)
        public static bool Dormir()
        {
            if (Modo == "espera") return CeEspera.EntrarAparte(); // (1.2.1: en su propio proceso; si no arranca, en este como antes)
            if (Modo == "suspender") return SuspenderYVolver();
            return false;
        }
        static string Exe { get { return Path.GetFullPath(System.Reflection.Assembly.GetEntryAssembly().Location); } }
        static string Marca { get { return Path.Combine(Paths.Datos, "wake_armed.txt"); } }

        // Lo que mira CeInicio.Argumento: int.MinValue = no es mio
        public static int Argumento(string arg)
        {
            if (arg == "--desde-espera") { CeEspera.DesdeEspera = true; return int.MinValue; } // (sigue: es la app normal)
            if (arg == "--ce-despertar-admin") return ArmarAdmin();
            if (arg.StartsWith("--ce-desinstalar-prueba=")) return TrayApp.PrepararBorrado(Paths.AppDir, Paths.Datos, arg.EndsWith("=1"), Process.GetCurrentProcess().Id) ? 0 : 1;
            if (arg == "--ce-despertar") return PedirArmar(true) ? 0 : 1;
            if (arg == "--prueba-espera")
            {
                Log.Write("Prueba de la espera ligera: pantalla apagada y la sonda mirando; la consola, un mando o una tecla la despiertan");
                CeEspera.Prueba = true; // (1.2.1: sin capturadora no se suspende el PC)
                return CeEspera.Entrar() ? 0 : 1;
            }
            // 1.2.1: la espera en su proceso (lo abre la app al apagarse la consola), la guarda de la barra de tareas y las pruebas sin
            // consola de todo el apagado (src\Lanzador\Espera.cs, src\Lanzador\EsperaCubierta.cs)
            if (arg == "--espera") return CeEspera.Proceso(Environment.GetCommandLineArgs());
            if (arg == "--ce-guarda-barra") return CeBarra.Guardian();
            if (arg == "--prueba-cadena" || arg.StartsWith("--prueba-cadena=")) return CeEspera.PruebaCadena(Segundos(arg, 20));
            if (arg.StartsWith("--prueba-cadena-aviso=")) return CeEspera.PruebaCadena(Segundos(arg, 8), true);
            if (arg == "--prueba-espera-simulada" || arg.StartsWith("--prueba-espera-simulada=")) return CeEspera.PruebaEsperaSimulada(Segundos(arg, 20));
            if (arg.StartsWith("--prueba-app=")) return CeEspera.PruebaApp(Segundos(arg, 6));
            if (arg == "--prueba-suspender")
            {
                Log.Write("Prueba de suspension: se suspende el PC ya y al despertar se abre la app");
                return SuspenderYVolver() ? 0 : 1;
            }
            return int.MinValue;
        }

        static int Segundos(string arg, int def)
        {
            int i = arg.IndexOf('='), s;
            return i > 0 && int.TryParse(arg.Substring(i + 1), out s) ? Math.Max(1, Math.Min(600, s)) : def;
        }

        // ------------------------------------------------------------------ suspender y volver
        // true = suspendido (y la app relanzada al despertar) o no hace falta apagar; false = no se pudo: apagar como antes
        public static bool SuspenderYVolver()
        {
            if (!IsPwrSuspendAllowed()) { Log.Write("Suspension: este PC no puede suspenderse (sin S3 ni S0): se apaga"); return false; }
            ManualResetEvent dormido = new ManualResetEvent(false), despierto = new ManualResetEvent(false);
            Stopwatch reloj = Stopwatch.StartNew();
            long dormidoMs = -1;
            Callback cb = delegate (IntPtr ctx, uint tipo, IntPtr setting)
            {
                if (tipo == 4) { dormidoMs = reloj.ElapsedMilliseconds; dormido.Set(); }          // PBT_APMSUSPEND
                else if (tipo == 0x12 || tipo == 7) despierto.Set();                               // PBT_APMRESUMEAUTOMATIC / RESUMESUSPEND
                return 0;
            };
            GCHandle keep = GCHandle.Alloc(cb);
            IntPtr hNot = IntPtr.Zero;
            try
            {
                SubscribeParams sp = new SubscribeParams();
                sp.Callback = Marshal.GetFunctionPointerForDelegate(cb);
                sp.Context = IntPtr.Zero;
                uint r = PowerRegisterSuspendResumeNotification(2, ref sp, out hNot); // DEVICE_NOTIFY_CALLBACK
                if (r != 0) { Log.Write("Suspension: sin avisos de suspension (" + r + "); se mira cuando vuelve SetSuspendState"); hNot = IntPtr.Zero; }

                Log.Write("Consola apagada: suspendiendo el PC (apagar_pc=1, apagar_pc_modo=suspender); al despertar se abre la app");
                bool ok = false;
                int err = 0;
                Thread t = new Thread(delegate ()
                {
                    ok = SetSuspendState(false, false, false);
                    if (!ok) err = Marshal.GetLastWin32Error();
                    if (hNot == IntPtr.Zero && ok) { dormido.Set(); despierto.Set(); } // (sin avisos: vuelve al despertar)
                });
                t.IsBackground = true;
                t.Start();
                // Hasta 60 s para dormirse (lo normal: 1-3 s)
                if (!dormido.WaitOne(60000))
                {
                    t.Join(1000);
                    if (!ok && !t.IsAlive) { Log.Write("Suspension: SetSuspendState fallo (error " + err + "): se apaga"); return false; }
                    Log.Write("Suspension: el PC no se ha dormido en 60 s (algun programa lo impide): sigue encendido");
                    Escritorio.Cancelado();
                    CeEspera.AlCerrarApp(false); // (1.2.1: fuera la cubierta negra y la barra visible)
                    return true;
                }
                despierto.WaitOne();
                Log.Write("Suspension: PC despierto (dormido a los " + dormidoMs + " ms, " + (reloj.ElapsedMilliseconds / 1000) + " s en total): abriendo la app");
            }
            finally
            {
                if (hNot != IntPtr.Zero) try { PowerUnregisterSuspendResumeNotification(hNot); } catch { }
                keep.Free();
            }
            // 1.2.1: la cubierta negra (puesta al apagarse la consola) sigue hasta ver el telon de --inicio (lo pone antes de esperar a que
            // este proceso suelte el mutex)
            EventWaitHandle telon = null;
            try { telon = new EventWaitHandle(false, EventResetMode.ManualReset, CeEspera.EventoTelon); telon.Reset(); } catch { telon = null; }
            CeCubierta.Pausa(true); // (el telon y la app, delante de la cubierta)
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Exe, CeInicio.ArgumentosInicio() + " --tras-suspension --desde-espera");
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(Exe);
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Error("Suspension: no se pudo abrir la app al despertar", ex); }
            if (telon != null) { if (CeCubierta.Puesta) telon.WaitOne(5000); telon.Dispose(); }
            return true;
        }

        // ------------------------------------------------------------------ que mandos y teclados despierten el PC
        // Lo que habria que armar: id de instancia -> por que (para el log)
        // Fabricantes de mandos y receptores (VID USB): con el mando apagado muchos receptores cambian de identidad (el de GameSir pasa de
        // PID_1040 XInput a PID_2106, un HID del fabricante) y es ESE el que tiene que despertar el PC al encender el mando. Se arman todos
        // los dispositivos USB/HID de estos fabricantes y de los de cualquier mando visto alguna vez (vid= en data\wake_armed.txt)
        static readonly string[] VidsMandos = { "045E", "054C", "057E", "2DC8", "3537", "0079", "20D6", "0E6F", "24C6", "146B", "0F0D", "1532", "28DE", "2563", "11C0", "1949", "2F24", "3285", "33DF", "413D" };
        static readonly System.Text.RegularExpressions.Regex VidRx = new System.Text.RegularExpressions.Regex(@"VID_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        static Dictionary<string, string> Objetivos()
        {
            HashSet<string> vidsVistos;
            return Objetivos(out vidsVistos);
        }

        static Dictionary<string, string> Objetivos(out HashSet<string> vidsVistos)
        {
            Dictionary<string, string> r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            vidsVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> vids = new HashSet<string>(VidsMandos, StringComparer.OrdinalIgnoreCase);
            foreach (string l in Armados()) if (l.StartsWith("vid=", StringComparison.OrdinalIgnoreCase)) vids.Add(l.Substring(4).Trim());
            List<string[]> todos = new List<string[]>();
            using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT DeviceID, PNPClass, Name, CompatibleID, HardwareID FROM Win32_PnPEntity"))
                foreach (ManagementObject o in s.Get())
                    using (o)
                    {
                        string id = o["DeviceID"] as string, cls = (o["PNPClass"] as string) ?? "", name = (o["Name"] as string) ?? "";
                        if (string.IsNullOrEmpty(id)) continue;
                        todos.Add(new string[] { id, name });
                        string ids = Unir(o["CompatibleID"] as string[]) + "|" + Unir(o["HardwareID"] as string[]);
                        bool mando = cls == "XnaComposite" || cls == "XboxComposite" || id.IndexOf("&IG_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     ids.IndexOf("HID_DEVICE_UP:0001_U:0005", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     ids.IndexOf("HID_DEVICE_UP:0001_U:0004", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     ids.IndexOf("HID_DEVICE_SYSTEM_GAME", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool teclado = cls == "Keyboard";
                        bool radioBt = cls == "Bluetooth" && id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase);
                        if (!mando && !teclado && !radioBt) continue;
                        string por = mando ? "mando" : teclado ? "teclado" : "radio Bluetooth";
                        r[id] = por + ": " + name;
                        if (mando)
                        {
                            System.Text.RegularExpressions.Match mv = VidRx.Match(id);
                            if (mv.Success) { vids.Add(mv.Groups[1].Value.ToUpperInvariant()); vidsVistos.Add(mv.Groups[1].Value.ToUpperInvariant()); }
                        }
                        // El receptor o el cable: los padres USB (ahi suele estar el despertar), hasta el concentrador
                        foreach (string p in Padres(id))
                        {
                            if (!p.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase)) break;
                            if (p.StartsWith("USB\\ROOT_HUB", StringComparison.OrdinalIgnoreCase)) break;
                            if (!r.ContainsKey(p)) r[p] = por + " (receptor USB de " + name + ")";
                        }
                    }
            // Los receptores de mandos (por fabricante), tambien con el mando apagado
            foreach (string[] d in todos)
            {
                if (r.ContainsKey(d[0])) continue;
                if (!d[0].StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase) && !d[0].StartsWith("HID\\VID_", StringComparison.OrdinalIgnoreCase)) continue;
                System.Text.RegularExpressions.Match m = VidRx.Match(d[0]);
                if (m.Success && vids.Contains(m.Groups[1].Value)) r[d[0]] = "receptor de mando (VID " + m.Groups[1].Value.ToUpperInvariant() + "): " + d[1];
            }
            return r;
        }

        static string Unir(string[] a) { return a == null ? "" : string.Join("|", a); }

        static List<string> Padres(string id)
        {
            List<string> r = new List<string>();
            uint dn;
            if (CM_Locate_DevNodeW(out dn, id, 0) != 0) return r;
            for (int i = 0; i < 8; i++)
            {
                uint p;
                if (CM_Get_Parent(out p, dn, 0) != 0) break;
                StringBuilder sb = new StringBuilder(512);
                if (CM_Get_Device_IDW(p, sb, sb.Capacity, 0) != 0) break;
                string pid = sb.ToString();
                if (pid.Length == 0 || pid.StartsWith("HTREE", StringComparison.OrdinalIgnoreCase)) break;
                // un mando de un hub USB: el hub no se arma (seguir no sirve de nada)
                if (pid.IndexOf("ROOT_HUB", StringComparison.OrdinalIgnoreCase) >= 0) break;
                r.Add(pid);
                dn = p;
            }
            return r;
        }

        static HashSet<string> Armados()
        {
            HashSet<string> r = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(Marca)) foreach (string l in File.ReadAllLines(Marca)) { string t = l.Trim(); if (t.Length > 0 && t[0] != '#') r.Add(t); } }
            catch { }
            return r;
        }

        // Al abrir la app (en otro hilo) y al activar la suspension: si hay mandos o teclados sin armar, se pide el UAC (siempre = aunque
        // esten todos apuntados). true = hecho o nada que hacer
        public static bool PedirArmar(bool siempre)
        {
            try
            {
                Dictionary<string, string> obj = Objetivos();
                HashSet<string> hechos = Armados();
                List<string> nuevos = new List<string>();
                foreach (string k in obj.Keys) if (!hechos.Contains(k)) nuevos.Add(obj[k]);
                if (!siempre && nuevos.Count == 0) return true;
                Log.Write("Despertar con el mando o el teclado: " + (nuevos.Count > 0 ? nuevos.Count + " sin armar (" + string.Join("; ", nuevos.ToArray()) + ")" : "comprobando") + ": se pide administrador");
                ProcessStartInfo psi = new ProcessStartInfo(Exe, "--ce-despertar-admin" + DatosArg());
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(60000)) return false;
                    Log.Write("Despertar con el mando o el teclado: " + (p.ExitCode == 0 ? "hecho" : "fallo (" + p.ExitCode + ")"));
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex) { Log.Write("Despertar con el mando o el teclado: sin permiso de administrador (" + ex.Message + ")"); return false; }
        }

        public static void AlAbrir(Config cfg)
        {
            CeEspera.Recordar(cfg); // (la capturadora para la sonda de la espera ligera, src\Lanzador\Espera.cs)
            // 1.2.1: la barra de tareas que se quedo oculta (la app murio) otra vez visible; abierta desde la espera, vuelve al verse la
            // consola. Con apagar_pc=1, la cubierta negra preparada (oculta) para el apagado (src\Lanzador\EsperaCubierta.cs)
            CeBarra.AlAbrir(CeEspera.DesdeEspera);
            if (cfg.ShutdownPc) ThreadPool.QueueUserWorkItem(delegate { try { CeCubierta.Preparar(); } catch (Exception ex) { Log.Error("Cubierta negra", ex); } });
            if (!cfg.ShutdownPc || cfg.ShutdownMode != "suspender") return;
            ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(8000); PedirArmar(false); });
        }

        static string DatosArg()
        {
            foreach (string s in Environment.GetCommandLineArgs())
                if (s.StartsWith("--datos=", StringComparison.OrdinalIgnoreCase)) return " \"" + s + "\"";
            return "";
        }

        // El exe elevado: arma todo lo que pueda despertar y quita la contrasena al despertar
        static int ArmarAdmin()
        {
            int armados = 0, ya = 0, fallos = 0;
            HashSet<string> vidsVistos;
            Dictionary<string, string> obj = Objetivos(out vidsVistos);
            // (lo de antes se queda: el mismo receptor con el mando encendido y apagado son dos dispositivos)
            HashSet<string> apuntar = Armados();
            foreach (string k in obj.Keys) apuntar.Add(k);
            foreach (string v in vidsVistos) apuntar.Add("vid=" + v);
            try
            {
                using (ManagementClass mc = new ManagementClass(@"root\wmi", "MSPower_DeviceWakeEnable", null))
                    foreach (ManagementObject o in mc.GetInstances())
                        using (o)
                        {
                            string inst = (o["InstanceName"] as string) ?? "";
                            int us = inst.LastIndexOf('_');
                            string id = us > 0 ? inst.Substring(0, us) : inst;
                            string por;
                            if (!obj.TryGetValue(id, out por)) continue;
                            try
                            {
                                if (o["Enable"] is bool && (bool)o["Enable"]) { ya++; continue; }
                                o["Enable"] = true;
                                o.Put();
                                armados++;
                                Log.Write("Despertar: armado " + id + " (" + por + ")");
                            }
                            catch (Exception ex) { fallos++; Log.Write("Despertar: no se pudo armar " + id + " (" + ex.Message + ")"); }
                        }
            }
            catch (Exception ex) { Log.Error("Despertar: MSPower_DeviceWakeEnable", ex); return 2; }
            // Sin pedir la contrasena al despertar (si no, sale la pantalla de bloqueo en vez de la app)
            string pc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powercfg.exe");
            foreach (string a in new string[] { "/SETACVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0", "/SETDCVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0", "/SETACTIVE SCHEME_CURRENT" })
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(pc, a);
                    psi.UseShellExecute = false; psi.CreateNoWindow = true;
                    using (Process p = Process.Start(psi)) p.WaitForExit(10000);
                }
                catch { }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Marca));
                List<string> ls = new List<string>();
                ls.Add("# Mandos, teclados y receptores que ConsoleEnhancer ya ha intentado armar para despertar el PC (" + DateTime.Now.ToString("dd-MM-yyyy HH:mm") + ")");
                ls.AddRange(apuntar);
                File.WriteAllLines(Marca, ls.ToArray(), new UTF8Encoding(false));
            }
            catch { }
            Log.Write("Despertar (administrador): " + armados + " armados, " + ya + " ya lo estaban, " + fallos + " fallos, de " + obj.Count + " mandos/teclados/receptores; sin contrasena al despertar");
            return fallos > 0 && armados + ya == 0 ? 1 : 0;
        }

        // ------------------------------------------------------------------ Windows
        delegate uint Callback(IntPtr context, uint type, IntPtr setting);
        [StructLayout(LayoutKind.Sequential)] struct SubscribeParams { public IntPtr Callback; public IntPtr Context; }
        [DllImport("powrprof.dll")] static extern uint PowerRegisterSuspendResumeNotification(uint flags, ref SubscribeParams recipient, out IntPtr handle);
        [DllImport("powrprof.dll")] static extern uint PowerUnregisterSuspendResumeNotification(IntPtr handle);
        [DllImport("powrprof.dll", SetLastError = true)] static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);
        [DllImport("powrprof.dll")] static extern bool IsPwrSuspendAllowed();
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Locate_DevNodeW(out uint devInst, string id, int flags);
        [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out uint parent, uint devInst, int flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buf, int len, int flags);
    }
}
