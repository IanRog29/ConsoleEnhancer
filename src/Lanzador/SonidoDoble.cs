// SonidoDoble.cs - que el sonido de la consola no se oiga dos veces (ConsoleEnhancer, 21-11-2026)
//
// Un usuario con una AVerMedia GC573 lo oia todo "dos veces" (como un eco): el sonido de la capturadora salia por el mpv de audio de la
// app y, a la vez, por otro camino con otro retraso. Los caminos que la app puede ver y evitar:
//  1. Windows "Escuchar este dispositivo" (Sonido > Grabar > propiedades de la entrada de la capturadora > Escuchar) encendido en la
//     entrada de la capturadora: Windows la reproduce por su cuenta. Mientras suena por la app se apaga y al salir se vuelve a poner
//     como estaba (se apunta en data\games\poweroff_listen.txt por si la app se cerrase de golpe; la proxima vez lo devuelve).
//     Se cambia con el almacen de propiedades del dispositivo (IMMDevice::OpenPropertyStore), como el panel de Sonido: sin UAC.
//  2. Un mpv de audio de una sesion anterior que siguio sonando (la app se cerro de golpe, desde el administrador de tareas o un fallo):
//     al empezar se cierran los que no son de esta sesion, y los nuevos van en un "job" de Windows que los cierra con la app.
//  3. Otro programa abierto con la capturadora (RECentral, OBS con "monitorizar", Streamlabs, XSplit...): no se cierra (puede estar
//     grabando), se avisa en pantalla si se le oye sonar.
// sonido_doble=0 lo deja todo como antes. El paso de la HDMI de la capturadora a una tele con altavoces tambien suena doble, pero eso
// no lo ve la app: lo dice el LEEME.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public bool SonidoDoble = true;     // sonido_doble: 0 = no tocar "Escuchar este dispositivo" ni cerrar mpv de audio de otras sesiones

        public bool ParseSonidoDoble(string key, string val)
        {
            if (key != "sonido_doble") return false;
            SonidoDoble = ParseBool(val);
            return true;
        }
    }

    partial class TrayApp
    {
        static readonly string EscucharFile = Paths.Datos + @"\games\poweroff_listen.txt";
        readonly object escucharSync = new object();
        readonly List<string> escucharApagado = new List<string>();   // entradas a las que la app quito "Escuchar"
        volatile bool vigilaOtros;

        // StartAudio, con el mpv de audio de la app (console_audio=1)
        void SonidoDobleAlEmpezar()
        {
            if (!cfg.SonidoDoble) return;
            string myPid = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            // Lo lento (WMI, el almacen de propiedades) en otro hilo: no retrasa la imagen
            Thread t = new Thread(delegate ()
            {
                try { CerrarAudiosHuerfanos(myPid); } catch (Exception ex) { Log.Error("Sonido doble: mpv de audio de otras sesiones", ex); }
                try { ApagarEscucharWindows(); } catch (Exception ex) { Log.Error("Sonido doble: Escuchar este dispositivo", ex); }
                try { VigilarOtrosProgramas(); } catch (Exception ex) { Log.Error("Sonido doble: otros programas con la capturadora", ex); }
            });
            t.IsBackground = true;
            t.Start();
        }

        // CloseAudio y ExitApp (las dos veces no hace nada)
        void SonidoDobleAlAcabar()
        {
            vigilaOtros = false;
            try { DevolverEscucharWindows(); } catch (Exception ex) { Log.Error("Sonido doble: devolviendo Escuchar este dispositivo", ex); }
        }

        // ---- 2. mpv de audio de otra sesion ----
        void CerrarAudiosHuerfanos(string myPid)
        {
            string mine = "ce-audio-" + myPid + "-";
            int n = 0;
            using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'mpv.exe'"))
            using (ManagementObjectCollection l = s.Get())
            {
                foreach (ManagementBaseObject o in l)
                {
                    string cmd = o["CommandLine"] as string;
                    if (cmd == null || cmd.IndexOf("--title=ConsoleEnhancerAudio", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (cmd.IndexOf(mine, StringComparison.OrdinalIgnoreCase) >= 0) continue; // de esta sesion
                    try
                    {
                        Process p = Process.GetProcessById(Convert.ToInt32(o["ProcessId"], CultureInfo.InvariantCulture));
                        p.Kill();
                        n++;
                    }
                    catch (Exception ex) { Log.Error("Cerrando un mpv de audio de otra sesion", ex); }
                }
            }
            if (n > 0) Log.Write("Sonido doble: cerrado" + (n > 1 ? "s " + n : " un") + " mpv de audio de una sesion anterior que seguia sonando");
        }

        // ---- 1. "Escuchar este dispositivo" de Windows ----
        void ApagarEscucharWindows()
        {
            lock (escucharSync)
            {
                // Lo que quedo apagado de una sesion que no termino bien: se devolvera al salir de esta
                try
                {
                    if (File.Exists(EscucharFile))
                        foreach (string l in File.ReadAllLines(EscucharFile))
                            if (l.Trim().Length > 0 && !escucharApagado.Contains(l.Trim())) escucharApagado.Add(l.Trim());
                }
                catch (Exception ex) { Log.Error("Leyendo " + EscucharFile, ex); }

                List<string> nombres = new List<string>();
                if (cfg.AudioDevice.Length > 0) nombres.Add(cfg.AudioDevice);
                if (cfg.Device.Length > 0) nombres.Add(cfg.Device);
                int vistas = 0;
                foreach (Escuchar.Entrada e in Escuchar.Entradas())
                {
                    if (!Escuchar.EsLaCapturadora(e, nombres)) continue;
                    vistas++;
                    if (!e.Escuchando) continue;
                    string err = Escuchar.Poner(e.Id, false);
                    if (err.Length > 0)
                    {
                        Log.Write("Sonido doble: \"Escuchar este dispositivo\" esta puesto en \"" + e.Nombre + "\" y no se ha podido quitar (" + err + ")");
                        OsdWarn(Deteccion.Tx.T("Sonido doble: quita «Escuchar este dispositivo» en Windows (Sonido > Grabar > " + e.Nombre + ")",
                                               "Double sound: turn off Windows \"Listen to this device\" (Sound > Recording > " + e.Nombre + ")"), "8000");
                        continue;
                    }
                    if (!escucharApagado.Contains(e.Id)) escucharApagado.Add(e.Id);
                    Log.Write("Sonido doble: Windows tambien reproducia \"" + e.Nombre + "\" (Escuchar este dispositivo): quitado mientras suena por la app, se devuelve al salir");
                }
                GuardarEscuchar();
                if (vistas == 0) Log.Write("Sonido doble: la capturadora no tiene entrada de sonido de Windows (o no se reconoce): nada que mirar en Escuchar este dispositivo");
            }
        }

        void DevolverEscucharWindows()
        {
            lock (escucharSync)
            {
                if (escucharApagado.Count == 0) return;
                List<string> quedan = new List<string>();
                foreach (string id in escucharApagado)
                {
                    string err = Escuchar.Poner(id, true);
                    if (err.Length == 0) Log.Write("Sonido doble: devuelto \"Escuchar este dispositivo\" a la entrada " + id);
                    else if (err != "no esta") { quedan.Add(id); Log.Write("Sonido doble: no se pudo devolver Escuchar a " + id + " (" + err + "): se intenta la proxima vez"); }
                }
                escucharApagado.Clear();
                escucharApagado.AddRange(quedan);
                GuardarEscuchar();
            }
        }

        void GuardarEscuchar()
        {
            try
            {
                if (escucharApagado.Count == 0) { if (File.Exists(EscucharFile)) File.Delete(EscucharFile); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(EscucharFile));
                File.WriteAllLines(EscucharFile, escucharApagado.ToArray());
            }
            catch (Exception ex) { Log.Error("Guardando " + EscucharFile, ex); }
        }

        // ---- 3. otros programas con la capturadora ----
        static readonly string[] OtrosConCaptura = { "RECentral", "RECentral4", "obs64", "obs32", "obs", "Streamlabs OBS", "Streamlabs Desktop", "XSplit.Core", "XSplit.Broadcaster", "4KCapture", "GamingHub" };

        // (1.2.02) los de la lista abiertos ahora (para decir quien puede tener la capturadora si no se abre)
        static string ProgramasConCaptura()
        {
            List<string> l = new List<string>();
            foreach (string nombre in OtrosConCaptura)
            {
                try { Process[] ps = Process.GetProcessesByName(nombre); if (ps.Length > 0 && !l.Contains(nombre)) l.Add(nombre); } catch { }
            }
            return string.Join(", ", l.ToArray());
        }

        void VigilarOtrosProgramas()
        {
            vigilaOtros = true;
            HashSet<string> avisados = new HashSet<string>();
            // Los 3 primeros minutos (cuando se nota el eco), cada 2 s: si alguno esta abierto y suena con la consola sonando
            DateTime fin = DateTime.Now.AddMinutes(3);
            while (vigilaOtros && !userExit && !audioClosing && DateTime.Now < fin)
            {
                foreach (string nombre in OtrosConCaptura)
                {
                    if (avisados.Contains(nombre)) continue;
                    Process[] ps = Process.GetProcessesByName(nombre);
                    if (ps.Length == 0) continue;
                    HashSet<uint> pids = new HashSet<uint>();
                    foreach (Process p in ps) pids.Add((uint)p.Id);
                    float pico;
                    try { pico = AudioMeter.PeakOfProcesses(pids); } catch { pico = -1f; }
                    if (pico < 0.01f) continue; // abierto pero callado: no molesta
                    avisados.Add(nombre);
                    Log.Write("Sonido doble: " + nombre + " esta abierto y reproduce sonido (pico " + pico.ToString("0.00", CultureInfo.InvariantCulture) +
                              "): si tiene la capturadora, se oye dos veces");
                    OsdWarn(Deteccion.Tx.T("Sonido doble: " + nombre + " también reproduce la capturadora · ciérralo o quita su monitorización",
                                           "Double sound: " + nombre + " is also playing the capture card · close it or turn off its monitoring"), "8000");
                }
                Thread.Sleep(2000);
            }
        }
    }

    // El mpv de audio va en un job de Windows que lo cierra si la app se va sin cerrarlo (un fallo, el administrador de tareas)
    static class AudioJob
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObjectW(IntPtr attrs, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtLimits info, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [StructLayout(LayoutKind.Sequential)]
        struct ExtLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        static readonly object sync = new object();
        static IntPtr job;
        static bool warned;

        public static void Hold(Process p)
        {
            try
            {
                lock (sync)
                {
                    if (job == IntPtr.Zero)
                    {
                        job = CreateJobObjectW(IntPtr.Zero, null);
                        if (job == IntPtr.Zero) { Warn("sin job " + Marshal.GetLastWin32Error()); return; }
                        ExtLimits l = new ExtLimits();
                        l.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                        if (!SetInformationJobObject(job, 9, ref l, (uint)Marshal.SizeOf(typeof(ExtLimits)))) Warn("SetInformationJobObject " + Marshal.GetLastWin32Error());
                    }
                    if (p != null && !AssignProcessToJobObject(job, p.Handle)) Warn("AssignProcessToJobObject " + Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex) { Warn(ex.GetType().Name + ": " + ex.Message); }
        }

        static void Warn(string why)
        {
            if (warned) return;
            warned = true;
            Log.Write("Audio de la consola: sin el job de Windows (" + why + "): si la app muriera sin cerrarlo, el mpv de audio seguiria sonando");
        }
    }

    // "Escuchar este dispositivo" de las entradas de sonido de Windows (Core Audio, almacen de propiedades del dispositivo)
    static class Escuchar
    {
        public sealed class Entrada { public string Id = "", Nombre = "", Adaptador = ""; public bool Escuchando; }

        // (21-11, 1.2.1: sin clase [ComImport] propia del enumerador: la misma que el medidor de audio, AudioMeter.NuevoEnumerador; con dos
        // clases para el mismo objeto COM la segunda fallaba con InvalidCastException)

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PropKey key);
            [PreserveSig] int GetValue(ref PropKey key, out PropVar value);
            [PreserveSig] int SetValue(ref PropKey key, ref PropVar value);
            [PreserveSig] int Commit();
        }

        [StructLayout(LayoutKind.Sequential)] struct PropKey { public Guid fmtid; public int pid; public PropKey(string g, int p) { fmtid = new Guid(g); pid = p; } }
        // PROPVARIANT (24 bytes en 64 bits): vt y el valor (VT_BOOL: short; VT_LPWSTR: puntero)
        [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVar { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public short b; [FieldOffset(8)] public IntPtr p; }
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVar v);

        const int eCapture = 1, DEVICE_STATE_ACTIVE = 1, STGM_READ = 0, STGM_READWRITE = 2;
        const ushort VT_BOOL = 11, VT_LPWSTR = 31;
        static readonly PropKey PkListen = new PropKey("24dbb0fc-9311-4b3d-9cf0-18ff155639d4", 1);       // Escuchar este dispositivo
        static readonly PropKey PkNombre = new PropKey("a45c254e-df1c-4efd-8020-67d146a850e0", 14);      // PKEY_Device_FriendlyName
        static readonly PropKey PkAdaptador = new PropKey("026e516e-b814-414b-83cd-856d6fef4822", 2);    // PKEY_DeviceInterface_FriendlyName

        public static List<Entrada> Entradas()
        {
            List<Entrada> l = new List<Entrada>();
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)AudioMeter.NuevoEnumerador();
            IMMDeviceCollection devs;
            if (en.EnumAudioEndpoints(eCapture, DEVICE_STATE_ACTIVE, out devs) != 0) return l;
            int n; devs.GetCount(out n);
            for (int i = 0; i < n; i++)
            {
                IMMDevice d;
                if (devs.Item(i, out d) != 0) continue;
                Entrada e = new Entrada();
                string id; if (d.GetId(out id) == 0 && id != null) e.Id = id;
                IPropertyStore ps;
                if (d.OpenPropertyStore(STGM_READ, out ps) != 0) continue;
                e.Nombre = Texto(ps, PkNombre);
                e.Adaptador = Texto(ps, PkAdaptador);
                PropKey k = PkListen; PropVar v;
                if (ps.GetValue(ref k, out v) == 0) { e.Escuchando = v.vt == VT_BOOL && v.b != 0; PropVariantClear(ref v); }
                if (e.Id.Length > 0) l.Add(e);
            }
            return l;
        }

        static string Texto(IPropertyStore ps, PropKey key)
        {
            PropVar v;
            if (ps.GetValue(ref key, out v) != 0) return "";
            string s = v.vt == VT_LPWSTR && v.p != IntPtr.Zero ? Marshal.PtrToStringUni(v.p) : "";
            PropVariantClear(ref v);
            return s ?? "";
        }

        // "" bien; si no, el motivo ("no esta": la entrada ya no existe)
        public static string Poner(string id, bool on)
        {
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)AudioMeter.NuevoEnumerador();
            IMMDevice d;
            if (en.GetDevice(id, out d) != 0 || d == null) return "no esta";
            IPropertyStore ps;
            int hr = d.OpenPropertyStore(STGM_READWRITE, out ps);
            if (hr != 0) return "0x" + hr.ToString("X8");
            PropKey k = PkListen;
            PropVar v = new PropVar(); v.vt = VT_BOOL; v.b = (short)(on ? -1 : 0);
            hr = ps.SetValue(ref k, ref v);
            if (hr != 0) return "SetValue 0x" + hr.ToString("X8");
            hr = ps.Commit();
            return hr != 0 ? "Commit 0x" + hr.ToString("X8") : "";
        }

        // La entrada es la de la capturadora: el nombre de DirectShow dentro del de Windows (o al reves), o el mismo modelo ("GC573",
        // "HD60", "4K60": letras y numeros juntos)
        public static bool EsLaCapturadora(Entrada e, List<string> nombres)
        {
            string w = (e.Nombre + " | " + e.Adaptador).ToLowerInvariant();
            foreach (string n0 in nombres)
            {
                string n = n0.Trim().ToLowerInvariant();
                if (n.Length >= 6 && w.Contains(n)) return true;
                if (e.Adaptador.Length >= 6 && n.Contains(e.Adaptador.ToLowerInvariant())) return true;
                foreach (string t in n.Split(new char[] { ' ', '(', ')', '-', '_', ',', '.', '/' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (t.Length < 4) continue;
                    bool dig = false, let = false;
                    foreach (char c in t) { if (char.IsDigit(c)) dig = true; else if (char.IsLetter(c)) let = true; }
                    if (dig && let && w.Contains(t)) return true;
                }
            }
            return false;
        }
    }
}
