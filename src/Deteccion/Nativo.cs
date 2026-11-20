// Nativo.cs - llamadas a Windows comunes a toda la deteccion (ConsoleEnhancer). Solo P/Invoke y COM a mano, C# 5.
//
// - Cargar DLL opcionales sin que falte ninguna sea un error (nvapi64.dll, xinput1_4.dll...): LoadLibrary + GetProcAddress.
// - COM por la tabla de metodos (Vt): sin declarar interfaces enteras; el numero de cada metodo sale de la cabecera (orden de la vtable).
// - Configuration Manager (cfgmgr32): de la ruta de un dispositivo a su nodo, sus padres, su nombre y su "contenedor" (el aparato fisico).
// - D3DKMT (gdi32): planificacion de la GPU por hardware (HAGS) y version de WDDM de un adaptador por su LUID.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ConsoleEnhancer.Deteccion
{
    static class Nat
    {
        // ---------------------------------------------------------------- DLL opcionales
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr LoadLibraryW(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] public static extern IntPtr GetProcAddress(IntPtr mod, string name);
        [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr mod);

        public static T Fn<T>(IntPtr mod, string name) where T : class
        {
            if (mod == IntPtr.Zero) return null;
            IntPtr p = GetProcAddress(mod, name);
            return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        // ---------------------------------------------------------------- COM por la vtable
        public static T Vt<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vtbl = Marshal.ReadIntPtr(obj);
            IntPtr fn = Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(fn, typeof(T)) as T;
        }

        public static void Release(IntPtr p) { if (p != IntPtr.Zero) Marshal.Release(p); }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int QiFn(IntPtr self, ref Guid iid, out IntPtr obj);

        // QueryInterface; IntPtr.Zero si no lo tiene
        public static IntPtr Qi(IntPtr obj, Guid iid)
        {
            if (obj == IntPtr.Zero) return IntPtr.Zero;
            IntPtr r;
            return Vt<QiFn>(obj, 0)(obj, ref iid, out r) >= 0 ? r : IntPtr.Zero;
        }

        [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr p);

        // ---------------------------------------------------------------- registro
        public static object Reg(RegistryKey root, string path, string name)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(path))
                    return k == null ? null : k.GetValue(name);
            }
            catch { return null; }
        }

        public static string RegStr(RegistryKey root, string path, string name)
        {
            object o = Reg(root, path, name);
            if (o == null) return null;
            string[] multi = o as string[];
            if (multi != null) return multi.Length > 0 ? multi[0] : "";
            return o.ToString();
        }

        public static int RegInt(RegistryKey root, string path, string name, int def)
        {
            object o = Reg(root, path, name);
            if (o is int) return (int)o;
            if (o is long) return (int)(long)o;
            if (o is byte[] && ((byte[])o).Length >= 4) return BitConverter.ToInt32((byte[])o, 0);
            int v;
            return o != null && int.TryParse(o.ToString(), out v) ? v : def;
        }

        public static string[] SubKeys(RegistryKey root, string path)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(path))
                    return k == null ? new string[0] : k.GetSubKeyNames();
            }
            catch { return new string[0]; }
        }

        // ---------------------------------------------------------------- Configuration Manager
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Locate_DevNodeW(out uint devInst, string id, uint flags);
        [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buf, int len, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_DevNode_Registry_PropertyW(uint devInst, uint prop, out uint type, byte[] buf, ref uint len, uint flags);
        [DllImport("cfgmgr32.dll")] static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DevPropKey key, out uint type, byte[] buf, ref uint len, uint flags);
        [DllImport("cfgmgr32.dll")] static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);

        [StructLayout(LayoutKind.Sequential)] struct DevPropKey { public Guid Fmtid; public uint Pid; }

        public const uint CM_DRP_DEVICEDESC = 0x01, CM_DRP_HARDWAREID = 0x02, CM_DRP_SERVICE = 0x05, CM_DRP_CLASS = 0x08, CM_DRP_MFG = 0x0C,
                          CM_DRP_FRIENDLYNAME = 0x0D, CM_DRP_LOCATION_INFORMATION = 0x0E, CM_DRP_ENUMERATOR_NAME = 0x17;
        static readonly Guid DevpkeyContainerFmt = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c");   // DEVPKEY_Device_ContainerId (pid 2)

        // Nodo de un id de instancia (0 si no esta)
        public static uint DevNode(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return 0;
            uint d;
            try { return CM_Locate_DevNodeW(out d, instanceId, 0) == 0 ? d : 0; } catch { return 0; }
        }

        public static string DevId(uint devInst)
        {
            StringBuilder sb = new StringBuilder(512);
            try { return CM_Get_Device_IDW(devInst, sb, sb.Capacity, 0) == 0 ? sb.ToString() : null; } catch { return null; }
        }

        public static uint Parent(uint devInst)
        {
            uint p;
            try { return devInst != 0 && CM_Get_Parent(out p, devInst, 0) == 0 ? p : 0; } catch { return 0; }
        }

        public static string DevProp(uint devInst, uint prop)
        {
            if (devInst == 0) return null;
            try
            {
                byte[] b = new byte[1024]; uint len = (uint)b.Length, type;
                if (CM_Get_DevNode_Registry_PropertyW(devInst, prop, out type, b, ref len, 0) != 0 || len < 2) return null;
                string s = Encoding.Unicode.GetString(b, 0, (int)len);
                int z = s.IndexOf('\0');
                return z >= 0 ? s.Substring(0, z) : s;
            }
            catch { return null; }
        }

        public static Guid ContainerId(uint devInst)
        {
            if (devInst == 0) return Guid.Empty;
            try
            {
                DevPropKey k = new DevPropKey(); k.Fmtid = DevpkeyContainerFmt; k.Pid = 2;
                byte[] b = new byte[16]; uint len = 16, type;
                if (CM_Get_DevNode_PropertyW(devInst, ref k, out type, b, ref len, 0) != 0 || len != 16) return Guid.Empty;
                return new Guid(b);
            }
            catch { return Guid.Empty; }
        }

        // El contenedor de "este PC" (los dispositivos internos comparten este: no sirve para emparejar)
        public static readonly Guid ContenedorPc = new Guid("00000000-0000-0000-ffff-ffffffffffff");

        public static bool DevPresent(uint devInst)
        {
            uint st, pr;
            try { return devInst != 0 && CM_Get_DevNode_Status(out st, out pr, devInst, 0) == 0; } catch { return false; }
        }

        // \\?\usb#vid_0fd9&pid_0066&mi_00#7&2a9e&0&0000#{65e8773d-...}\global -> USB\VID_0FD9&PID_0066&MI_00\7&2A9E&0&0000
        public static string InstanceFromInterfacePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string p = path;
            if (p.StartsWith(@"\\?\") || p.StartsWith(@"\\.\")) p = p.Substring(4);
            int g = p.IndexOf("#{", StringComparison.Ordinal);
            if (g < 0) return null;
            return p.Substring(0, g).Replace('#', '\\').ToUpperInvariant();
        }

        // Los ids de instancia del nodo y de sus padres (hasta 6 niveles; el primero es el propio)
        public static List<string> Chain(string instanceId)
        {
            List<string> l = new List<string>();
            uint d = DevNode(instanceId);
            for (int i = 0; d != 0 && i < 7; i++)
            {
                string id = DevId(d);
                if (id == null) break;
                l.Add(id.ToUpperInvariant());
                d = Parent(d);
            }
            return l;
        }

        // VID/PID (USB) o VEN/DEV (PCI) de un id: "USB\VID_0FD9&PID_0066..." -> 0x0FD9, 0x0066
        public static bool UsbIds(string id, out int vid, out int pid)
        {
            vid = pid = 0;
            if (id == null) return false;
            string u = id.ToUpperInvariant();
            return Hex(u, "VID_", out vid) & Hex(u, "PID_", out pid) || (Hex(u, "VEN_", out vid) & Hex(u, "DEV_", out pid));
        }

        static bool Hex(string s, string tag, out int v)
        {
            v = 0;
            int i = s.IndexOf(tag, StringComparison.Ordinal);
            if (i < 0 || i + tag.Length + 4 > s.Length) return false;
            return int.TryParse(s.Substring(i + tag.Length, 4), System.Globalization.NumberStyles.HexNumber, null, out v);
        }

        // ---------------------------------------------------------------- D3DKMT (planificacion por hardware, WDDM)
        [StructLayout(LayoutKind.Sequential)] struct KmtOpenLuid { public uint LowPart; public int HighPart; public uint hAdapter; }
        [StructLayout(LayoutKind.Sequential)] struct KmtQuery { public uint hAdapter; public int Type; public IntPtr Data; public uint Size; }
        [StructLayout(LayoutKind.Sequential)] struct KmtClose { public uint hAdapter; }
        [DllImport("gdi32.dll")] static extern int D3DKMTOpenAdapterFromLuid(ref KmtOpenLuid a);
        [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref KmtQuery q);
        [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref KmtClose c);
        const int KMTQAITYPE_DRIVERVERSION = 13, KMTQAITYPE_WDDM_2_7_CAPS = 70;

        // hags: -1 no se sabe, 0 no soportado, 1 soportado y apagado, 2 encendido. wddm: 3100 = WDDM 3.1 (0 = no se sabe)
        public static void KmtInfo(long luid, out int hags, out bool hagsDefault, out int wddm)
        {
            hags = -1; hagsDefault = false; wddm = 0;
            KmtOpenLuid o = new KmtOpenLuid();
            o.LowPart = (uint)(luid & 0xFFFFFFFF); o.HighPart = (int)(luid >> 32);
            try
            {
                if (D3DKMTOpenAdapterFromLuid(ref o) != 0) return;
            }
            catch { return; }
            IntPtr buf = Marshal.AllocHGlobal(16);
            try
            {
                KmtQuery q = new KmtQuery();
                q.hAdapter = o.hAdapter; q.Type = KMTQAITYPE_WDDM_2_7_CAPS; q.Data = buf; q.Size = 4;
                Marshal.WriteInt32(buf, 0);
                if (D3DKMTQueryAdapterInfo(ref q) == 0)
                {
                    uint f = (uint)Marshal.ReadInt32(buf);
                    bool sup = (f & 1) != 0, on = (f & 2) != 0;
                    hagsDefault = (f & 4) != 0;
                    hags = !sup ? 0 : on ? 2 : 1;
                }
                q.Type = KMTQAITYPE_DRIVERVERSION;
                Marshal.WriteInt32(buf, 0);
                if (D3DKMTQueryAdapterInfo(ref q) == 0) wddm = Marshal.ReadInt32(buf);
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(buf);
                KmtClose c = new KmtClose(); c.hAdapter = o.hAdapter;
                try { D3DKMTCloseAdapter(ref c); } catch { }
            }
        }

        // ---------------------------------------------------------------- version de Windows de verdad
        // (Environment.OSVersion da 6.2.9200 en un exe sin manifiesto de compatibilidad: RtlGetVersion no miente)
        [StructLayout(LayoutKind.Sequential)] struct OsVer { public int Size, Major, Minor, Build, Platform; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Sp; }
        [DllImport("ntdll.dll")] static extern int RtlGetVersion(ref OsVer v);
        static int osBuild = -1;
        public static int OsBuild()
        {
            if (osBuild < 0)
            {
                try { OsVer v = new OsVer(); v.Size = Marshal.SizeOf(typeof(OsVer)); osBuild = RtlGetVersion(ref v) == 0 ? v.Build : Environment.OSVersion.Version.Build; }
                catch { osBuild = Environment.OSVersion.Version.Build; }
            }
            return osBuild;
        }

        // ---------------------------------------------------------------- utilidades
        public static string Trim0(string s)
        {
            if (s == null) return "";
            int z = s.IndexOf('\0');
            return (z >= 0 ? s.Substring(0, z) : s).Trim();
        }

        public static string Ansi(byte[] b, int off, int max)
        {
            int n = 0;
            while (n < max && off + n < b.Length && b[off + n] != 0) n++;
            return Encoding.ASCII.GetString(b, off, n).Trim();
        }

        public static string Uni(byte[] b, int off, int maxChars)
        {
            int n = 0;
            while (n < maxChars && off + 2 * n + 1 < b.Length && (b[off + 2 * n] != 0 || b[off + 2 * n + 1] != 0)) n++;
            return Encoding.Unicode.GetString(b, off, 2 * n).Trim();
        }

        public static bool Has(string s, string sub)
        {
            return s != null && sub != null && s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
