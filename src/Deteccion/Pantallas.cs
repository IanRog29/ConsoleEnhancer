// Pantallas.cs - las pantallas (tele o monitor): modo de ahora, el maximo, Hz exactos, HDR y VRR (ConsoleEnhancer)
//
// - QueryDisplayConfig (user32): cada camino activo con su refresco exacto (racional: 120,000 frente a 119,88 importa con Smooth
//   Motion, ver LEEME.txt), el nombre del monitor ("Panasonic-TV"), la conexion (HDMI/DP) y el HDR (advanced color).
// - EnumDisplaySettings: todos los modos de cada pantalla (resolucion maxima y Hz maximos a esa resolucion y en total).
// - EDID (registro, Enum\DISPLAY\...\Device Parameters\EDID): nombre, resolucion nativa, VRR del bloque HDMI Forum (VRRmin-VRRmax),
//   FreeSync, ALLM, FRL (HDMI 2.1), HDR10/HLG del bloque de metadatos HDR. Como tests\VrrProbe.cs del motor.
// - DXGI: el ultimo HDR de la salida (DXGI_OUTPUT_DESC1: espacio de color PQ, luminancias) y si se permite el "tearing" (lo que
//   necesita VRR en ventana). NVIDIA: G-SYNC/VRR posible y activo por NVAPI.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ConsoleEnhancer.Deteccion
{
    public sealed class PantallaInfo
    {
        public string Gdi = "";                // \\.\DISPLAY1
        public string Nombre = "";             // Panasonic-TV
        public string Pnp = "";                // MEI96A2
        public string Conexion = "";           // HDMI, DisplayPort, interna...
        public bool Principal;
        public int X, Y;
        public int Ancho, Alto, Hz;            // modo de ahora
        public double HzExacto;                // 119,880 / 120,000
        public int AnchoMax, AltoMax, HzEnMax, HzMax;
        public List<int> HzDeLaMax = new List<int>();
        public int AnchoNativo, AltoNativo;    // EDID
        public bool HdrPosible, HdrActivo;
        public int BitsColor;
        public double NitsMax, NitsMin;
        public int VrrMin, VrrMax;             // HDMI Forum (0 = no hay)
        public int FreeSyncMin, FreeSyncMax;
        public int RangoMin, RangoMax;         // limites de frecuencia del EDID
        public bool Allm, Hdr10Edid, HlgEdid;
        public int Frl;                        // HDMI 2.1 FRL maxima (0 = HDMI 2.0 o menos)
        public int VrrNvidia = -1;             // bits de NVAPI (1 activo, 2 posible); -1 no se sabe
        public bool Tearing;                   // DXGI PRESENT_ALLOW_TEARING
        public long GpuLuid;
        public bool Simulada;

        public bool VrrPosible { get { return VrrMax > 0 || FreeSyncMax > 0 || (VrrNvidia >= 0 && (VrrNvidia & 2) != 0); } }
        public bool VrrActivo { get { return VrrNvidia >= 0 && (VrrNvidia & 1) != 0; } }
        public bool Es119 { get { return HzExacto > 119.5 && HzExacto < 119.95; } }
    }

    public static class DetectorPantallas
    {
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] byte[] paths, ref uint numModes, [Out] byte[] modes, IntPtr topology);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo([In, Out] byte[] packet);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string device, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevicesW(string device, uint i, ref DISPLAY_DEVICE dd, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public uint dmFields;
            public int dmPositionX, dmPositionY; public uint dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels; public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        // DXGI (salidas: HDR de DXGI_OUTPUT_DESC1)
        [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumAdapters1Fn(IntPtr self, uint i, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumOutputsFn(IntPtr self, uint i, out IntPtr output);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDescBytesFn(IntPtr self, [Out] byte[] desc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CheckFeatureFn(IntPtr self, int feature, ref int data, int size);
        static readonly Guid IidFactory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        static readonly Guid IidFactory5 = new Guid("7632e1f5-ee65-4dca-87fd-84cd75f8838d");
        static readonly Guid IidOutput6 = new Guid("068346e8-aaec-4b84-add7-137f513f77a1");

        public static List<PantallaInfo> Detectar(OpcionesDeteccion op, List<string> errores)
        {
            List<PantallaInfo> l = new List<PantallaInfo>();
            try { DisplayConfig(l); } catch (Exception ex) { errores.Add("QueryDisplayConfig: " + ex.Message); }
            // Sin DisplayConfig (o vacio): al menos las de GDI
            if (l.Count == 0)
            {
                try
                {
                    DISPLAY_DEVICE dd = new DISPLAY_DEVICE(); dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    for (uint i = 0; EnumDisplayDevicesW(null, i, ref dd, 0); i++)
                    {
                        if ((dd.StateFlags & 1) != 0) { PantallaInfo p = new PantallaInfo(); p.Gdi = dd.DeviceName; p.Nombre = dd.DeviceString; l.Add(p); }
                        dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    }
                }
                catch (Exception ex) { errores.Add("EnumDisplayDevices: " + ex.Message); }
            }
            bool tearing = false;
            try { Dxgi(l, out tearing); } catch (Exception ex) { errores.Add("DXGI (pantallas): " + ex.Message); }
            foreach (PantallaInfo p in l)
            {
                p.Tearing = tearing;
                try { Modos(p); } catch (Exception ex) { errores.Add("Modos " + p.Gdi + ": " + ex.Message); }
                if (op.UsarNvidia && NvApi.Cargar(true)) p.VrrNvidia = NvApi.Vrr(p.Gdi);
            }
            l.Sort(delegate(PantallaInfo a, PantallaInfo b) { return a.Principal == b.Principal ? string.CompareOrdinal(a.Gdi, b.Gdi) : a.Principal ? -1 : 1; });
            return l;
        }

        static void DisplayConfig(List<PantallaInfo> l)
        {
            uint np, nm;
            if (GetDisplayConfigBufferSizes(2 /*QDC_ONLY_ACTIVE_PATHS*/, out np, out nm) != 0 || np == 0) return;
            byte[] paths = new byte[np * 72], modes = new byte[nm * 64];
            if (QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return;
            for (int i = 0; i < np; i++)
            {
                int o = i * 72;
                PantallaInfo p = new PantallaInfo();
                long srcLuid = ((long)BitConverter.ToInt32(paths, o + 4) << 32) | BitConverter.ToUInt32(paths, o);
                uint srcId = BitConverter.ToUInt32(paths, o + 8);
                uint srcMode = BitConverter.ToUInt32(paths, o + 12);
                long tgtLuid = ((long)BitConverter.ToInt32(paths, o + 24) << 32) | BitConverter.ToUInt32(paths, o + 20);
                uint tgtId = BitConverter.ToUInt32(paths, o + 28);
                int tech = BitConverter.ToInt32(paths, o + 36);
                uint rn = BitConverter.ToUInt32(paths, o + 48), rd = BitConverter.ToUInt32(paths, o + 52);
                p.HzExacto = rd != 0 ? Math.Round(rn / (double)rd, 3) : 0;
                p.Conexion = Tecnologia(tech);
                p.GpuLuid = srcLuid;
                if (srcMode != 0xFFFFFFFF && srcMode < nm)
                {
                    int m = (int)srcMode * 64;
                    if (BitConverter.ToInt32(modes, m) == 1 /*SOURCE*/)
                    {
                        p.Ancho = BitConverter.ToInt32(modes, m + 16); p.Alto = BitConverter.ToInt32(modes, m + 20);
                        p.X = BitConverter.ToInt32(modes, m + 28); p.Y = BitConverter.ToInt32(modes, m + 32);
                        p.Principal = p.X == 0 && p.Y == 0;
                    }
                }
                // nombre de GDI
                byte[] sn = Cabecera(1, 84, srcLuid, srcId);
                if (DisplayConfigGetDeviceInfo(sn) == 0) p.Gdi = Nat.Uni(sn, 20, 32);
                // monitor
                byte[] tn = Cabecera(2, 420, tgtLuid, tgtId);
                string devPath = "";
                if (DisplayConfigGetDeviceInfo(tn) == 0)
                {
                    p.Nombre = Nat.Uni(tn, 36, 64);
                    devPath = Nat.Uni(tn, 164, 128);
                }
                // HDR
                byte[] ac = Cabecera(9, 32, tgtLuid, tgtId);
                if (DisplayConfigGetDeviceInfo(ac) == 0)
                {
                    uint v = BitConverter.ToUInt32(ac, 20);
                    p.HdrPosible = (v & 1) != 0 && (v & 8) == 0;
                    p.HdrActivo = (v & 2) != 0;
                    p.BitsColor = BitConverter.ToInt32(ac, 28);
                }
                // (20-11) Windows 11 24H2+: con la gestion automatica del color, "advanced color" esta activo tambien en SDR: el HDR de verdad
                // es GET_ADVANCED_COLOR_INFO_2 (15): bit 4 HDR posible, activeColorMode 2 = HDR (como HDRTray)
                if (Nat.OsBuild() >= 26100)
                {
                    byte[] a2 = Cabecera(15, 36, tgtLuid, tgtId);
                    if (DisplayConfigGetDeviceInfo(a2) == 0)
                    {
                        uint v2 = BitConverter.ToUInt32(a2, 20);
                        p.HdrPosible = (v2 & 16) != 0;
                        p.HdrActivo = BitConverter.ToInt32(a2, 32) == 2;
                    }
                }
                Edid(p, devPath);
                if (p.Nombre.Length == 0) p.Nombre = p.Pnp.Length > 0 ? p.Pnp : p.Gdi;
                l.Add(p);
            }
        }

        static byte[] Cabecera(int type, int size, long luid, uint id)
        {
            byte[] b = new byte[size];
            BitConverter.GetBytes(type).CopyTo(b, 0);
            BitConverter.GetBytes(size).CopyTo(b, 4);
            BitConverter.GetBytes((uint)(luid & 0xFFFFFFFF)).CopyTo(b, 8);
            BitConverter.GetBytes((int)(luid >> 32)).CopyTo(b, 12);
            BitConverter.GetBytes(id).CopyTo(b, 16);
            return b;
        }

        static string Tecnologia(int t)
        {
            switch (t)
            {
                case 0: return "VGA";
                case 4: return "DVI";
                case 5: return "HDMI";
                case 6: return "LVDS";
                case 10: return "DisplayPort";
                case 11: return Tx.T("DisplayPort interna", "embedded DisplayPort");
                case 15: return "Miracast";
                case 16: return Tx.T("indirecta", "indirect");
                case unchecked((int)0x80000000): return Tx.T("interna", "internal");
                default: return "(" + t + ")";
            }
        }

        static void Modos(PantallaInfo p)
        {
            if (string.IsNullOrEmpty(p.Gdi)) return;
            DEVMODE dm = new DEVMODE(); dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            if (EnumDisplaySettingsW(p.Gdi, -1, ref dm))
            {
                if (p.Ancho == 0) { p.Ancho = (int)dm.dmPelsWidth; p.Alto = (int)dm.dmPelsHeight; }
                p.Hz = (int)dm.dmDisplayFrequency;
                if (p.HzExacto == 0) p.HzExacto = p.Hz;
                if (dm.dmPositionX == 0 && dm.dmPositionY == 0) p.Principal = true;
            }
            long best = 0;
            List<uint[]> all = new List<uint[]>();
            for (int k = 0; k < 4000; k++)
            {
                dm = new DEVMODE(); dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
                if (!EnumDisplaySettingsW(p.Gdi, k, ref dm)) break;
                if (dm.dmBitsPerPel != 0 && dm.dmBitsPerPel < 24) continue;
                all.Add(new uint[] { dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency });
                long px = (long)dm.dmPelsWidth * dm.dmPelsHeight;
                if (px > best) best = px;
                if (dm.dmDisplayFrequency > p.HzMax) p.HzMax = (int)dm.dmDisplayFrequency;
            }
            foreach (uint[] m in all)
            {
                if ((long)m[0] * m[1] != best) continue;
                p.AnchoMax = (int)m[0]; p.AltoMax = (int)m[1];
                if (m[2] > p.HzEnMax) p.HzEnMax = (int)m[2];
                if (!p.HzDeLaMax.Contains((int)m[2])) p.HzDeLaMax.Add((int)m[2]);
            }
            p.HzDeLaMax.Sort();
        }

        static void Dxgi(List<PantallaInfo> l, out bool tearing)
        {
            tearing = false;
            IntPtr fac = IntPtr.Zero;
            Guid iid = IidFactory1;
            if (CreateDXGIFactory1(ref iid, out fac) < 0) return;
            try
            {
                IntPtr f5 = Nat.Qi(fac, IidFactory5);
                if (f5 != IntPtr.Zero)
                {
                    int allow = 0;
                    try { if (Nat.Vt<CheckFeatureFn>(f5, 28)(f5, 0 /*PRESENT_ALLOW_TEARING*/, ref allow, 4) >= 0) tearing = allow != 0; } catch { }
                    Nat.Release(f5);
                }
                EnumAdapters1Fn en = Nat.Vt<EnumAdapters1Fn>(fac, 12);
                for (uint a = 0; a < 16; a++)
                {
                    IntPtr ad;
                    if (en(fac, a, out ad) < 0 || ad == IntPtr.Zero) break;
                    try
                    {
                        EnumOutputsFn eo = Nat.Vt<EnumOutputsFn>(ad, 7);
                        for (uint i = 0; i < 16; i++)
                        {
                            IntPtr o;
                            if (eo(ad, i, out o) < 0 || o == IntPtr.Zero) break;
                            try
                            {
                                IntPtr o6 = Nat.Qi(o, IidOutput6);
                                if (o6 == IntPtr.Zero) continue;
                                try
                                {
                                    byte[] d = new byte[160];
                                    if (Nat.Vt<GetDescBytesFn>(o6, 27)(o6, d) < 0) continue;
                                    string name = Nat.Uni(d, 0, 32);
                                    PantallaInfo p = l.Find(delegate(PantallaInfo x) { return string.Equals(x.Gdi, name, StringComparison.OrdinalIgnoreCase); });
                                    if (p == null) continue;
                                    int cs = BitConverter.ToInt32(d, 100);
                                    if (cs == 12 /*G2084 P2020*/) p.HdrActivo = true;
                                    if (p.BitsColor == 0) p.BitsColor = BitConverter.ToInt32(d, 96);
                                    p.NitsMin = BitConverter.ToSingle(d, 136);
                                    p.NitsMax = BitConverter.ToSingle(d, 140);
                                }
                                finally { Nat.Release(o6); }
                            }
                            finally { Nat.Release(o); }
                        }
                    }
                    finally { Nat.Release(ad); }
                }
            }
            finally { Nat.Release(fac); }
        }

        // EDID del monitor por la ruta del dispositivo: \\?\DISPLAY#MEI96A2#5&...#{e6f07b5f-...}
        static void Edid(PantallaInfo p, string devPath)
        {
            string inst = Nat.InstanceFromInterfacePath(devPath);
            if (inst == null) return;
            string[] parts = inst.Split('\\');
            if (parts.Length >= 2) p.Pnp = parts[1];
            byte[] e = Nat.Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Enum\" + inst + @"\Device Parameters", "EDID") as byte[];
            if (e == null || e.Length < 128) return;
            ParseEdid(p, e);
        }

        public static void ParseEdid(PantallaInfo p, byte[] e)
        {
            // descriptores de 18 bytes: nombre (FC), limites (FD); el primero es el modo nativo
            int pc = e[54] | (e[55] << 8);
            if (pc != 0) { p.AnchoNativo = e[56] | ((e[58] & 0xF0) << 4); p.AltoNativo = e[59] | ((e[61] & 0xF0) << 4); }
            for (int d = 54; d <= 108; d += 18)
            {
                if (e[d] != 0 || e[d + 1] != 0) continue;
                if (e[d + 3] == 0xFC && p.Nombre.Length == 0) p.Nombre = Encoding.ASCII.GetString(e, d + 5, 13).Split('\n')[0].Trim();
                if (e[d + 3] == 0xFD)
                {
                    int fl = e[d + 4];
                    p.RangoMin = e[d + 5] + ((fl & 1) != 0 ? 255 : 0);
                    p.RangoMax = e[d + 6] + ((fl & 2) != 0 ? 255 : 0);
                }
            }
            for (int ext = 1; ext <= e[126] && (ext + 1) * 128 <= e.Length; ext++)
            {
                int o = ext * 128;
                if (e[o] != 0x02) continue;   // CTA-861
                int dtd = e[o + 2];
                for (int q = o + 4; q < o + dtd && q < o + 127; )
                {
                    int tag = e[q] >> 5, len = e[q] & 31;
                    if (len == 0) { q++; continue; }
                    if (tag == 3 && len >= 3)
                    {
                        int oui = e[q + 1] | (e[q + 2] << 8) | (e[q + 3] << 16);
                        if (oui == 0xC45DD8)   // HDMI Forum
                        {
                            if (len >= 7) p.Frl = e[q + 7] >> 4;
                            if (len >= 8) p.Allm = (e[q + 8] & 2) != 0;
                            if (len >= 10)
                            {
                                int vmin = e[q + 9] & 0x3F, vmax = ((e[q + 9] >> 6) << 8) | e[q + 10];
                                if (vmax > 0) { p.VrrMin = vmin; p.VrrMax = vmax; }
                            }
                        }
                        else if (oui == 0x00001A && len >= 7) { p.FreeSyncMin = e[q + 6]; p.FreeSyncMax = e[q + 7]; }   // AMD FreeSync
                    }
                    else if (tag == 7 && len >= 2 && e[q + 1] == 6)   // metadatos HDR estaticos
                    {
                        int eotf = e[q + 2];
                        p.Hdr10Edid = (eotf & 4) != 0; p.HlgEdid = (eotf & 8) != 0;
                    }
                    q += len + 1;
                }
            }
        }
    }
}
