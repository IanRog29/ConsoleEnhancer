// Sistema.cs - Windows, procesador, memoria, portatil o sobremesa, plan de energia, idioma (ConsoleEnhancer)
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ConsoleEnhancer.Deteccion
{
    public sealed class SistemaInfo
    {
        public string Windows = "";
        public int Build;
        public bool Windows11;
        public string Cpu = "";
        public int Nucleos, Hilos, NucleosP, NucleosE;    // P/E: procesadores hibridos (Intel 12.a o posterior)
        public ulong Ram, RamLibre;
        public bool Portatil, ConBateria;
        public string PlanEnergia = "";
        public bool VrrVentana, OptimizacionVentana;      // Configuracion > Graficos: VRR y "optimizaciones para juegos en ventana"
        public string Idioma = "";
        public bool Admin;
    }

    public static class DetectorSistema
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MemStatus { public uint Length, Load; public ulong Total, Avail, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExt; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemStatus m);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetLogicalProcessorInformationEx(int rel, IntPtr buf, ref int len);
        [StructLayout(LayoutKind.Sequential)] struct PowerStatus { public byte AcLine, BatteryFlag, BatteryPercent, SystemStatus; public int Life, FullLife; }
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus s);
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll", CharSet = CharSet.Unicode)] static extern uint PowerReadFriendlyName(IntPtr root, IntPtr scheme, IntPtr sub, IntPtr setting, byte[] buf, ref uint size);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
        [StructLayout(LayoutKind.Sequential)] struct OsVer { public int Size, Major, Minor, Build, Platform; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Sp; }
        [DllImport("ntdll.dll")] static extern int RtlGetVersion(ref OsVer v);

        public static SistemaInfo Detectar(System.Collections.Generic.List<string> errores)
        {
            SistemaInfo s = new SistemaInfo();
            try
            {
                OsVer v = new OsVer(); v.Size = Marshal.SizeOf(typeof(OsVer));
                if (RtlGetVersion(ref v) == 0) s.Build = v.Build;
                else s.Build = Environment.OSVersion.Version.Build;
                s.Windows11 = s.Build >= 22000;
                string disp = Nat.RegStr(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion") ?? "";
                string ed = Nat.RegStr(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID") ?? "";
                s.Windows = (s.Windows11 ? "Windows 11" : "Windows 10") + " " + ed + " " + disp + " (" + s.Build + ")";
            }
            catch (Exception ex) { errores.Add("Windows: " + ex.Message); }

            s.Cpu = (Nat.RegStr(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") ?? "").Trim();
            s.Hilos = Environment.ProcessorCount;
            try { Nucleos(s); } catch (Exception ex) { errores.Add("CPU: " + ex.Message); }

            MemStatus m = new MemStatus(); m.Length = (uint)Marshal.SizeOf(typeof(MemStatus));
            if (GlobalMemoryStatusEx(ref m)) { s.Ram = m.Total; s.RamLibre = m.Avail; }

            PowerStatus ps;
            if (GetSystemPowerStatus(out ps)) { s.ConBateria = ps.BatteryFlag != 128 && ps.BatteryFlag != 255; s.Portatil = s.ConBateria; }
            try
            {
                IntPtr g;
                if (PowerGetActiveScheme(IntPtr.Zero, out g) == 0)
                {
                    byte[] b = new byte[512]; uint sz = (uint)b.Length;
                    if (PowerReadFriendlyName(IntPtr.Zero, g, IntPtr.Zero, IntPtr.Zero, b, ref sz) == 0) s.PlanEnergia = Nat.Uni(b, 0, 255);
                    LocalFree(g);
                }
            }
            catch { }

            string dx = Nat.RegStr(Registry.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings") ?? "";
            s.VrrVentana = dx.Contains("VRROptimizeEnable=1");
            s.OptimizacionVentana = dx.Contains("SwapEffectUpgradeEnable=1");
            try { s.Idioma = CultureInfo.CurrentUICulture.Name; } catch { }
            try
            {
                System.Security.Principal.WindowsPrincipal wp = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
                s.Admin = wp.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { }
            return s;
        }

        // Nucleos fisicos y, en los hibridos, cuantos de cada clase (EfficiencyClass: el mayor = nucleos P)
        static void Nucleos(SistemaInfo s)
        {
            int len = 0;
            GetLogicalProcessorInformationEx(0 /*RelationProcessorCore*/, IntPtr.Zero, ref len);
            if (len <= 0) return;
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                if (!GetLogicalProcessorInformationEx(0, buf, ref len)) return;
                int[] clases = new int[8];
                int maxClase = 0;
                for (int off = 0; off < len; )
                {
                    int size = Marshal.ReadInt32(buf, off + 4);
                    if (size <= 0) break;
                    int eff = Marshal.ReadByte(buf, off + 9);
                    s.Nucleos++;
                    clases[Math.Min(7, eff)]++;
                    if (eff > maxClase) maxClase = eff;
                    off += size;
                }
                if (maxClase > 0) { s.NucleosP = clases[maxClase]; s.NucleosE = s.Nucleos - s.NucleosP; }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }
}
