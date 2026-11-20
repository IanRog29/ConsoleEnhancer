// Controles.cs - mandos del PC: XInput (los que Windows ya trata como de Xbox) y la lista basica de HID (ConsoleEnhancer)
//
// - XInput (xinput1_4.dll, o xinput9_1_0.dll): las 4 ranuras, tipo, cable o bateria.
// - HID (SetupAPI + hid.dll): cada coleccion de "joystick / gamepad / multieje" con su VID/PID, nombre y fabricante. Se abre el
//   dispositivo SIN permisos de lectura ni escritura (acceso 0: solo sus datos, no se le quita a nadie) y se cierra al momento.
// - Mandos conocidos por VID/PID (PlayStation, Nintendo, Xbox, GameSir, 8BitDo...): si van por XInput o necesitan que la app los lea por HID
//   (la fase 2 lo hace con el src\Mandos.cs del lanzador, sin Joyxoff).
// - Programas que cambian los mandos (Joyxoff, DS4Windows, DSX, Steam, reWASD, x360ce) y drivers virtuales (ViGEmBus, HidHide).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ConsoleEnhancer.Deteccion
{
    public sealed class MandoInfo
    {
        public string Origen = "";          // xinput / hid
        public int Ranura = -1;             // XInput 0-3
        public string Nombre = "", Marca = "", Fabricante = "";
        public int Vid, Pid;
        public string Tipo = "";            // mando, volante, arcade... / gamepad, joystick
        public string Conexion = "";        // cable, bateria, bluetooth?...
        public bool XInputNativo;           // va por XInput (sin traductor)
        public bool Virtual;                // mando virtual (ViGEm, Steam...)
        public string Ruta = "";
        public string Nota = "";
    }

    public static class DetectorMandos
    {
        // ------------------------------------------------------------------ XInput
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint XGetCaps(uint idx, uint flags, [Out] byte[] caps);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint XGetBattery(uint idx, byte devType, [Out] byte[] info);

        // ------------------------------------------------------------------ SetupAPI / HID
        [StructLayout(LayoutKind.Sequential)] struct SpIfData { public int cbSize; public Guid Class; public int Flags; public IntPtr Reserved; }
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevsW(ref Guid cls, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid cls, int idx, ref SpIfData data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SpIfData data, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(IntPtr h, [In, Out] byte[] attrs);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, [Out] byte[] caps);
        [DllImport("hid.dll")] static extern bool HidD_GetProductString(IntPtr h, [Out] byte[] buf, int len);
        [DllImport("hid.dll")] static extern bool HidD_GetManufacturerString(IntPtr h, [Out] byte[] buf, int len);

        // VID, PID (0 = cualquiera), marca, modelo, va por XInput sin ayuda
        static readonly object[][] Conocidos =
        {
            new object[] { 0x054C, 0x05C4, "Sony", "DualShock 4", false }, new object[] { 0x054C, 0x09CC, "Sony", "DualShock 4 (v2)", false },
            new object[] { 0x054C, 0x0BA0, "Sony", "Adaptador inalámbrico DS4", false }, new object[] { 0x054C, 0x0CE6, "Sony", "DualSense", false },
            new object[] { 0x054C, 0x0DF2, "Sony", "DualSense Edge", false }, new object[] { 0x054C, 0, "Sony", "PlayStation", false },
            new object[] { 0x057E, 0x2006, "Nintendo", "Joy-Con (L)", false }, new object[] { 0x057E, 0x2007, "Nintendo", "Joy-Con (R)", false },
            new object[] { 0x057E, 0x2009, "Nintendo", "Pro Controller", false }, new object[] { 0x057E, 0, "Nintendo", "Nintendo", false },
            new object[] { 0x045E, 0x028E, "Microsoft", "Xbox 360", true }, new object[] { 0x045E, 0x0B12, "Microsoft", "Xbox Series", true },
            new object[] { 0x045E, 0x0B13, "Microsoft", "Xbox Series (Bluetooth)", true }, new object[] { 0x045E, 0x02FD, "Microsoft", "Xbox One S (Bluetooth)", true },
            new object[] { 0x045E, 0x0B00, "Microsoft", "Xbox Elite 2", true }, new object[] { 0x045E, 0, "Microsoft", "Xbox", true },
            new object[] { 0x3537, 0, "GameSir", "GameSir", true }, new object[] { 0x2DC8, 0, "8BitDo", "8BitDo", true },
            new object[] { 0x0F0D, 0, "Hori", "Hori", true }, new object[] { 0x20D6, 0, "PowerA", "PowerA", true }, new object[] { 0x0E6F, 0, "PDP", "PDP", true },
            new object[] { 0x046D, 0, "Logitech", "Logitech", true }, new object[] { 0x1532, 0, "Razer", "Razer", true },
            new object[] { 0x044F, 0, "Thrustmaster", "Thrustmaster", false }, new object[] { 0x146B, 0, "Nacon", "Nacon", true },
            new object[] { 0x0738, 0, "Mad Catz", "Mad Catz", true }, new object[] { 0x28DE, 0, "Valve", "Steam", false },
            new object[] { 0x18D1, 0x9400, "Google", "Stadia", false }, new object[] { 0x2563, 0, "ShanWan", "genérico", false },
            new object[] { 0x0079, 0, "DragonRise", "genérico", false },
        };

        static readonly string[] Programas = { "Joyxoff", "DS4Windows", "DSX", "DualSenseX", "steam", "reWASDEngine", "reWASDTray", "x360ce", "BetterJoy", "JoyShockMapper", "AntiMicroX", "InputMapper" };

        public static List<MandoInfo> Detectar(OpcionesDeteccion op, List<string> programas, List<string> errores)
        {
            List<MandoInfo> l = new List<MandoInfo>();
            try { XInput(l); } catch (Exception ex) { errores.Add("XInput: " + ex.Message); }
            try { Hid(l); } catch (Exception ex) { errores.Add("HID: " + ex.Message); }
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        foreach (string n in Programas)
                            if (string.Equals(p.ProcessName, n, StringComparison.OrdinalIgnoreCase) && !programas.Contains(p.ProcessName)) programas.Add(p.ProcessName);
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex) { errores.Add("Procesos: " + ex.Message); }
            foreach (string svc in new[] { "ViGEmBus", "HidHide" })
                if (Nat.Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\" + svc, "ImagePath") != null) programas.Add(svc + Tx.T(" (driver)", " (driver)"));
            return l;
        }

        static void XInput(List<MandoInfo> l)
        {
            IntPtr mod = Nat.LoadLibraryW("xinput1_4.dll");
            if (mod == IntPtr.Zero) mod = Nat.LoadLibraryW("xinput9_1_0.dll");
            XGetCaps caps = Nat.Fn<XGetCaps>(mod, "XInputGetCapabilities");
            XGetBattery bat = Nat.Fn<XGetBattery>(mod, "XInputGetBatteryInformation");
            if (caps == null) return;
            for (uint i = 0; i < 4; i++)
            {
                byte[] c = new byte[20];
                if (caps(i, 0, c) != 0) continue;   // 1167 = no conectado
                MandoInfo m = new MandoInfo();
                m.Origen = "xinput"; m.Ranura = (int)i; m.XInputNativo = true;
                m.Nombre = Tx.T("Mando XInput ", "XInput pad ") + (i + 1);
                m.Tipo = SubTipo(c[1]);
                ushort fl = BitConverter.ToUInt16(c, 2);
                if ((fl & 0x0002) != 0) m.Nota = Tx.T("inalámbrico", "wireless");
                if (bat != null)
                {
                    byte[] b = new byte[2];
                    if (bat(i, 0, b) == 0) m.Conexion = b[0] == 1 ? Tx.T("cable", "wired") : b[0] == 0 ? "" : Tx.T("batería ", "battery ") + new[] { Tx.T("vacía", "empty"), Tx.T("baja", "low"), Tx.T("media", "medium"), Tx.T("llena", "full") }[Math.Min(3, (int)b[1])];
                }
                l.Add(m);
            }
        }

        static string SubTipo(byte s)
        {
            switch (s)
            {
                case 1: return Tx.T("mando", "gamepad");
                case 2: return Tx.T("volante", "wheel");
                case 3: return "arcade stick";
                case 4: return "flight stick";
                case 5: return "dance pad";
                case 6: case 7: return Tx.T("guitarra", "guitar");
                case 8: return Tx.T("batería", "drum kit");
                case 0x13: return "arcade pad";
                default: return Tx.T("mando (", "pad (") + s + ")";
            }
        }

        static void Hid(List<MandoInfo> l)
        {
            Guid hid;
            HidD_GetHidGuid(out hid);
            IntPtr set = SetupDiGetClassDevsW(ref hid, IntPtr.Zero, IntPtr.Zero, 0x02 | 0x10 /*PRESENT | DEVICEINTERFACE*/);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return;
            HashSet<string> vistos = new HashSet<string>();
            try
            {
                for (int i = 0; i < 512; i++)
                {
                    SpIfData d = new SpIfData(); d.cbSize = Marshal.SizeOf(typeof(SpIfData));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref d)) break;
                    int need;
                    SetupDiGetDeviceInterfaceDetailW(set, ref d, IntPtr.Zero, 0, out need, IntPtr.Zero);
                    if (need <= 0) continue;
                    IntPtr det = Marshal.AllocHGlobal(need);
                    string path;
                    try
                    {
                        Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetailW(set, ref d, det, need, out need, IntPtr.Zero)) continue;
                        path = Marshal.PtrToStringUni(new IntPtr(det.ToInt64() + 4));
                    }
                    finally { Marshal.FreeHGlobal(det); }
                    MandoInfo m = Uno(path);
                    if (m == null) continue;
                    // un mando = varias colecciones (&col01, &col02...): uno por aparato
                    string inst = Nat.InstanceFromInterfacePath(path) ?? path;
                    int col = inst.IndexOf("&COL", StringComparison.OrdinalIgnoreCase);
                    string key = m.Vid.ToString("X4") + m.Pid.ToString("X4") + (col > 0 ? inst.Substring(0, col) : inst);
                    if (!vistos.Add(key)) continue;
                    l.Add(m);
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }

        static MandoInfo Uno(string path)
        {
            IntPtr h = CreateFileW(path, 0, 3 /*FILE_SHARE_READ|WRITE*/, IntPtr.Zero, 3 /*OPEN_EXISTING*/, 0, IntPtr.Zero);
            if (h == new IntPtr(-1) || h == IntPtr.Zero) return null;
            try
            {
                byte[] at = new byte[12]; BitConverter.GetBytes(12).CopyTo(at, 0);
                if (!HidD_GetAttributes(h, at)) return null;
                int vid = BitConverter.ToUInt16(at, 4), pid = BitConverter.ToUInt16(at, 6);
                int usage = 0, page = 0;
                IntPtr pp;
                if (HidD_GetPreparsedData(h, out pp))
                {
                    byte[] caps = new byte[64];
                    if (HidP_GetCaps(pp, caps) == 0x00110000 /*HIDP_STATUS_SUCCESS*/) { usage = BitConverter.ToUInt16(caps, 0); page = BitConverter.ToUInt16(caps, 2); }
                    HidD_FreePreparsedData(pp);
                }
                object[] k = Conocido(vid, pid);
                bool pad = page == 1 && (usage == 4 || usage == 5 || usage == 8);
                if (!pad && k == null) return null;
                if (!pad && k != null && page == 1 && (usage == 2 || usage == 6)) return null;   // el raton o el teclado de un receptor (el boton de captura del GameSir va por aqui)
                if (!pad && k != null && page != 1) return null;
                MandoInfo m = new MandoInfo();
                m.Origen = "hid"; m.Vid = vid; m.Pid = pid; m.Ruta = path;
                m.Tipo = usage == 5 ? "gamepad" : usage == 4 ? "joystick" : usage == 8 ? Tx.T("multieje", "multi-axis") : "HID " + page.ToString("X") + "/" + usage.ToString("X");
                byte[] s = new byte[256];
                if (HidD_GetProductString(h, s, s.Length)) m.Nombre = Nat.Uni(s, 0, 126);
                s = new byte[256];
                if (HidD_GetManufacturerString(h, s, s.Length)) m.Fabricante = Nat.Uni(s, 0, 126);
                if (k != null) { m.Marca = (string)k[2]; if (m.Nombre.Length == 0) m.Nombre = (string)k[3]; m.XInputNativo = (bool)k[4]; }
                string up = path.ToUpperInvariant();
                if (up.Contains("IG_")) { m.XInputNativo = true; m.Nota = Tx.T("también por XInput", "also on XInput"); }
                if (up.Contains("{00001124-0000-1000-8000-00805F9B34FB}") || up.Contains("BTHENUM") || up.Contains("_VID&0002")) m.Conexion = "Bluetooth";
                if (vid == 0x045E && pid == 0x028E && !up.Contains("USB#")) { m.Virtual = true; m.Nota = Tx.T("virtual (ViGEm u otro)", "virtual (ViGEm or other)"); }
                if (!m.XInputNativo && m.Nota.Length == 0) m.Nota = Tx.T("no es XInput: la app lo leerá por HID (fase 2)", "not XInput: read via HID (phase 2)");
                if (m.Nombre.Length == 0) m.Nombre = "HID " + vid.ToString("X4") + ":" + pid.ToString("X4");
                return m;
            }
            catch { return null; }
            finally { CloseHandle(h); }
        }

        static object[] Conocido(int vid, int pid)
        {
            object[] any = null;
            foreach (object[] k in Conocidos)
            {
                if ((int)k[0] != vid) continue;
                if ((int)k[1] == pid) return k;
                if ((int)k[1] == 0) any = k;
            }
            return any;
        }
    }
}
