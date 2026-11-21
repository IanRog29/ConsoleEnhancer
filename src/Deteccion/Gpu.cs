// Gpu.cs - las graficas del PC y lo que cada una puede hacer (ConsoleEnhancer)
//
// 1. DXGI (dxgi.dll, siempre esta): cada adaptador con su nombre, fabricante (VendorId), modelo (DeviceId), VRAM, LUID y la version del
//    driver (CheckInterfaceSupport); la memoria que deja usar ahora (QueryVideoMemoryInfo).
// 2. D3D12 (si esta d3d12.dll): un dispositivo de un momento para saber el nivel de funciones y el shader model (FSR 3 FG pide 12_0 y
//    SM 6.2; XeSS en DP4a, SM 6.4). No pinta nada ni reserva memoria apreciable.
// 3. D3DKMT: planificacion de la GPU por hardware (HAGS: DLSS-G no va sin ella) y la version de WDDM.
// 4. NVIDIA (NvApi.cs): arquitectura, nombre completo, driver y rama, portatil o sobremesa, si el driver conoce el ajuste de Smooth Motion.
//    AMD: la version de AMD Software (registro, RadeonSoftwareVersion). Intel: la del driver (101.xxxx).
// 5. Funciones (Derivar): que hay en cada grafica, con el motivo, a partir de lo anterior. Las tablas de arquitecturas, umbrales de driver e
//    indice de potencia son estimaciones; lo que manda despues es lo que el motor comprueba al arrancar (slIsFeatureSupported...) y el banco.
//
// Lo medido en ESTE PC (RTX 4060 8 GB, driver 616.92) es la referencia del indice (1,0) y de los costes de DLSS (Plan.cs).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ConsoleEnhancer.Deteccion
{
    public enum Fabricante { Desconocido, Nvidia, Amd, Intel, Microsoft, Qualcomm, Otro }

    public enum FamiliaGpu
    {
        Desconocida, Software,
        NvidiaAntigua, NvidiaGtx, NvidiaRtx20, NvidiaRtx30, NvidiaRtx40, NvidiaRtx50,
        AmdAntigua, AmdRdna1, AmdRdna2, AmdRdna3, AmdRdna4,
        IntelAntigua, IntelXe, IntelArc, IntelArcB,
        Otra
    }

    public sealed class Funcion
    {
        public bool Si;
        public bool Dudoso;           // segun tablas/umbrales: se confirma al usarlo
        public bool NoOficial;        // extra de la comunidad que pone el usuario (no va en el paquete)
        public string Motivo = "";
        public Funcion Pon(bool si, string motivo) { Si = si; Motivo = motivo; return this; }
        public override string ToString() { return (Si ? (Dudoso ? Tx.T("quizá", "maybe") : Tx.T("sí", "yes")) : "no") + (NoOficial && Si ? Tx.T(" (no oficial)", " (unofficial)") : "") + (Motivo.Length > 0 ? " - " + Motivo : ""); }
    }

    public sealed class GpuFunciones
    {
        public Funcion DlssSr = new Funcion(), DlssTransformer = new Funcion(), DlssG = new Funcion(), DlssMfg = new Funcion(),
                       SmoothMotion = new Funcion(), Reflex = new Funcion(), Nvof = new Funcion(), DlssNr = new Funcion(),
                       DlssGComunidad = new Funcion(), RtxVideo = new Funcion(), RtxHdr = new Funcion(),
                       Fsr1 = new Funcion(), Fsr3Fg = new Funcion(), Fsr4 = new Funcion(), XessSr = new Funcion(), XessFg = new Funcion(),
                       Afmf = new Funcion(), AntiLag = new Funcion(), Motor = new Funcion();
        public int MfgMax = 1;        // DLSS-G: imagenes por cada real que admite la grafica (2 en RTX 40, 4 en RTX 50)
    }

    public sealed class GpuInfo
    {
        public string Nombre = "";
        public Fabricante Fab;
        public int VendorId, DeviceId, SubSysId, Revision;
        public long Luid;
        public ulong Vram, MemSistemaDedicada, MemCompartida, PresupuestoVram;
        public bool Software, Integrada, Portatil;
        public int Salidas;               // pantallas conectadas a esta grafica
        public string DriverUmd = "";     // 32.0.16.1692
        public string Driver = "";        // 616.92 / AMD Software 24.10.1 / 101.6130
        public int DriverNv;              // 61692 (NVIDIA)
        public string RamaDriver = "";
        public string FechaDriver = "";
        public int NvArq, NvImpl;         // NVAPI (0 = no se sabe)
        public string Arquitectura = "";
        public FamiliaGpu Familia;
        public int Serie;                 // RTX 20/30/40/50, GTX 10/16, RX 5000-9000, Arc A/B...
        public double Indice;             // potencia aproximada frente a la RTX 4060 (1,0)
        public bool IndiceEstimado;
        public int Hags = -1;             // -1 no se sabe, 0 sin soporte, 1 apagada, 2 encendida
        public bool HagsDeSerie;
        public int Wddm;                  // 3100 = WDDM 3.1
        public int NivelD3D12;            // 0xc100 = 12_1 (0 = sin D3D12 o sin mirar)
        public int ShaderModel;           // 0x66 = 6.6
        public bool Nvapi;
        public int SmoothAjuste = -1;     // el driver conoce el ajuste de Smooth Motion: -1 no se sabe, 0 no, 1 si
        public bool Simulada;
        public GpuFunciones F = new GpuFunciones();

        public string FabTexto { get { return Fab == Fabricante.Nvidia ? "NVIDIA" : Fab == Fabricante.Amd ? "AMD" : Fab == Fabricante.Intel ? "Intel" : Fab == Fabricante.Microsoft ? "Microsoft" : Fab == Fabricante.Qualcomm ? "Qualcomm" : Tx.T("otro", "other"); } }
        public bool EsRtx { get { return Familia == FamiliaGpu.NvidiaRtx20 || Familia == FamiliaGpu.NvidiaRtx30 || Familia == FamiliaGpu.NvidiaRtx40 || Familia == FamiliaGpu.NvidiaRtx50; } }
        public string Clave { get { return VendorId.ToString("X4") + ":" + DeviceId.ToString("X4") + ":" + (DriverUmd.Length > 0 ? DriverUmd : Driver); } }   // para la calibracion
        // GB "de caja": las de 8 GB dan 7,8-8,0 segun quien lo cuente (DXGI quita lo reservado); los umbrales comparan con esto
        public double VramGb { get { double gb = Vram / (1024.0 * 1024 * 1024); return gb >= 1 ? Math.Round(gb * 2, MidpointRounding.AwayFromZero) / 2 : gb; } }
    }

    public static class DetectorGpu
    {
        // ------------------------------------------------------------------ DXGI por la vtable
        [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumAdapters1Fn(IntPtr self, uint i, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDesc1Fn(IntPtr self, [Out] byte[] desc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CheckIfFn(IntPtr self, ref Guid iid, out long umd);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumOutputsFn(IntPtr self, uint i, out IntPtr output);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryVidMemFn(IntPtr self, uint node, int group, [Out] byte[] info);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int D3D12CreateDeviceFn(IntPtr adapter, int minLevel, ref Guid riid, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CheckFeatureFn(IntPtr self, int feature, IntPtr data, int size);

        static readonly Guid IidFactory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        static readonly Guid IidAdapter3 = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");
        static readonly Guid IidDxgiDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        static readonly Guid IidD3D12Device = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");
        const int SlotEnumAdapters1 = 12, SlotEnumOutputs = 7, SlotCheckIf = 9, SlotGetDesc1 = 10, SlotQueryVidMem = 14, SlotCheckFeature = 13;

        public static List<GpuInfo> Detectar(OpcionesDeteccion op, List<string> errores)
        {
            List<GpuInfo> l = new List<GpuInfo>();
            IntPtr fac = IntPtr.Zero;
            try
            {
                Guid iid = IidFactory1;
                int hr = CreateDXGIFactory1(ref iid, out fac);
                if (hr < 0) { errores.Add("CreateDXGIFactory1 0x" + hr.ToString("X8")); return l; }
                EnumAdapters1Fn en = Nat.Vt<EnumAdapters1Fn>(fac, SlotEnumAdapters1);
                for (uint i = 0; i < 16; i++)
                {
                    IntPtr ad;
                    if (en(fac, i, out ad) < 0 || ad == IntPtr.Zero) break;
                    try { GpuInfo g = Adaptador(ad, op); if (g != null) l.Add(g); }
                    catch (Exception ex) { errores.Add("GPU " + i + ": " + ex.Message); }
                    finally { Nat.Release(ad); }
                }
            }
            catch (Exception ex) { errores.Add("DXGI: " + ex.Message); }
            finally { Nat.Release(fac); }

            // Sin NVIDIA (simulado): fuera las de NVIDIA, como en un PC que no tiene ninguna
            if (!op.UsarNvidia) l.RemoveAll(delegate(GpuInfo g) { return g.Fab == Fabricante.Nvidia; });

            // NVIDIA: lo que sabe su driver
            if (op.UsarNvidia && l.Exists(delegate(GpuInfo g) { return g.Fab == Fabricante.Nvidia; }) && NvApi.Cargar(true))
            {
                string rama;
                int drv = NvApi.Driver(out rama);
                List<NvGpu> nv = NvApi.Gpus();
                int smooth = NvApi.ConoceAjuste(NvApi.SmoothMotionSetting);
                List<NvGpu> libres = new List<NvGpu>(nv);
                foreach (GpuInfo g in l)
                {
                    if (g.Fab != Fabricante.Nvidia) continue;
                    NvGpu m = libres.Find(delegate(NvGpu x) { return x.DeviceId == g.DeviceId && x.SubSysId == g.SubSysId; });
                    if (m == null) m = libres.Find(delegate(NvGpu x) { return x.DeviceId == g.DeviceId; });
                    if (drv > 0) { g.DriverNv = drv; g.Driver = Tx.N(drv / 100.0, "0.00"); g.RamaDriver = rama; }
                    g.SmoothAjuste = smooth;
                    if (m == null) continue;
                    libres.Remove(m);
                    g.Nvapi = true;
                    g.NvArq = m.Arq; g.NvImpl = m.Implementacion;
                    if (m.Nombre.Length > 0 && !Nat.Has(g.Nombre, m.Nombre.Replace("NVIDIA ", ""))) g.Nombre = g.Nombre + " (" + m.Nombre + ")";
                    if (m.TipoSistema == 1) g.Portatil = true;
                }
            }
            else if (op.UsarNvidia && l.Exists(delegate(GpuInfo g) { return g.Fab == Fabricante.Nvidia; }))
                errores.Add("NVAPI: " + NvApi.Error);

            foreach (GpuInfo g in l) Derivar(g, op);
            return l;
        }

        static GpuInfo Adaptador(IntPtr ad, OpcionesDeteccion op)
        {
            byte[] d = new byte[320];
            if (Nat.Vt<GetDesc1Fn>(ad, SlotGetDesc1)(ad, d) < 0) return null;
            GpuInfo g = new GpuInfo();
            g.Nombre = Nat.Uni(d, 0, 128);
            g.VendorId = BitConverter.ToInt32(d, 256); g.DeviceId = BitConverter.ToInt32(d, 260);
            g.SubSysId = BitConverter.ToInt32(d, 264); g.Revision = BitConverter.ToInt32(d, 268);
            g.Vram = BitConverter.ToUInt64(d, 272); g.MemSistemaDedicada = BitConverter.ToUInt64(d, 280); g.MemCompartida = BitConverter.ToUInt64(d, 288);
            g.Luid = ((long)BitConverter.ToInt32(d, 300) << 32) | BitConverter.ToUInt32(d, 296);
            uint flags = BitConverter.ToUInt32(d, 304);
            g.Software = (flags & 2) != 0 || (g.VendorId == 0x1414 && g.DeviceId == 0x8C);
            g.Fab = g.VendorId == 0x10DE ? Fabricante.Nvidia : g.VendorId == 0x1002 || g.VendorId == 0x1022 ? Fabricante.Amd :
                    g.VendorId == 0x8086 ? Fabricante.Intel : g.VendorId == 0x1414 ? Fabricante.Microsoft :
                    g.VendorId == 0x5143 || g.VendorId == 0x4D4F4351 ? Fabricante.Qualcomm : Fabricante.Otro;

            // Driver (UMD): 32.0.16.1692 -> NVIDIA 616.92
            long umd;
            Guid iid = IidDxgiDevice;
            try
            {
                if (Nat.Vt<CheckIfFn>(ad, SlotCheckIf)(ad, ref iid, out umd) >= 0)
                {
                    int a = (int)((umd >> 48) & 0xFFFF), b = (int)((umd >> 32) & 0xFFFF), c = (int)((umd >> 16) & 0xFFFF), e = (int)(umd & 0xFFFF);
                    g.DriverUmd = a + "." + b + "." + c + "." + e;
                    if (g.Fab == Fabricante.Nvidia) { g.DriverNv = (c % 10) * 10000 + e; g.Driver = Tx.N(g.DriverNv / 100.0, "0.00"); }
                    else if (g.Fab == Fabricante.Intel) g.Driver = c + "." + e;
                }
            }
            catch { }

            // Pantallas conectadas
            try
            {
                EnumOutputsFn eo = Nat.Vt<EnumOutputsFn>(ad, SlotEnumOutputs);
                for (uint i = 0; i < 16; i++) { IntPtr o; if (eo(ad, i, out o) < 0 || o == IntPtr.Zero) break; g.Salidas++; Nat.Release(o); }
            }
            catch { }

            // Memoria que deja usar ahora (presupuesto)
            IntPtr a3 = Nat.Qi(ad, IidAdapter3);
            if (a3 != IntPtr.Zero)
            {
                try { byte[] m = new byte[32]; if (Nat.Vt<QueryVidMemFn>(a3, SlotQueryVidMem)(a3, 0, 0, m) >= 0) g.PresupuestoVram = BitConverter.ToUInt64(m, 0); }
                catch { }
                Nat.Release(a3);
            }

            if (!g.Software && op.ProbarD3D12) ProbarD3D12(ad, g);
            Nat.KmtInfo(g.Luid, out g.Hags, out g.HagsDeSerie, out g.Wddm);
            Registro(g);
            return g;
        }

        // Nivel de funciones y shader model con un dispositivo D3D12 de un momento
        static IntPtr d3d12;
        static void ProbarD3D12(IntPtr adapter, GpuInfo g)
        {
            try
            {
                if (d3d12 == IntPtr.Zero) d3d12 = Nat.LoadLibraryW("d3d12.dll");
                D3D12CreateDeviceFn create = Nat.Fn<D3D12CreateDeviceFn>(d3d12, "D3D12CreateDevice");
                if (create == null) return;
                Guid iid = IidD3D12Device;
                IntPtr dev;
                if (create(adapter, 0xb000, ref iid, out dev) < 0 || dev == IntPtr.Zero) return;
                try
                {
                    CheckFeatureFn cf = Nat.Vt<CheckFeatureFn>(dev, SlotCheckFeature);
                    int[] lv = { 0xb000, 0xb100, 0xc000, 0xc100, 0xc200 };
                    IntPtr req = Marshal.AllocHGlobal(lv.Length * 4), fl = Marshal.AllocHGlobal(24), sm = Marshal.AllocHGlobal(4);
                    try
                    {
                        Marshal.Copy(lv, 0, req, lv.Length);
                        Marshal.WriteInt32(fl, 0, lv.Length); Marshal.WriteIntPtr(fl, 8, req); Marshal.WriteInt32(fl, 16, 0);
                        if (cf(dev, 2 /*FEATURE_LEVELS*/, fl, 24) >= 0) g.NivelD3D12 = Marshal.ReadInt32(fl, 16);
                        for (int s = 0x69; s >= 0x60; s--)
                        {
                            Marshal.WriteInt32(sm, s);
                            if (cf(dev, 7 /*SHADER_MODEL*/, sm, 4) >= 0) { g.ShaderModel = Marshal.ReadInt32(sm); break; }
                        }
                    }
                    finally { Marshal.FreeHGlobal(req); Marshal.FreeHGlobal(fl); Marshal.FreeHGlobal(sm); }
                }
                finally { Nat.Release(dev); }
            }
            catch { }
        }

        // La clave de la grafica en el registro (clase "Display"): AMD Software, fecha del driver, VRAM real
        static void Registro(GpuInfo g)
        {
            const string cls = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            string want = ("ven_" + g.VendorId.ToString("x4") + "&dev_" + g.DeviceId.ToString("x4")).ToLowerInvariant();
            foreach (string k in Nat.SubKeys(Registry.LocalMachine, cls))
            {
                if (k.Length != 4) continue;
                string p = cls + "\\" + k;
                string match = (Nat.RegStr(Registry.LocalMachine, p, "MatchingDeviceId") ?? "").ToLowerInvariant();
                if (match.IndexOf(want, StringComparison.Ordinal) < 0) continue;
                string ver = Nat.RegStr(Registry.LocalMachine, p, "DriverVersion") ?? "";
                if (g.DriverUmd.Length > 0 && ver.Length > 0 && ver != g.DriverUmd) continue;   // otra instalacion vieja del mismo modelo
                g.FechaDriver = Nat.RegStr(Registry.LocalMachine, p, "DriverDate") ?? "";
                if (g.Fab == Fabricante.Amd)
                {
                    string rs = Nat.RegStr(Registry.LocalMachine, p, "RadeonSoftwareVersion");
                    g.Driver = !string.IsNullOrEmpty(rs) ? "AMD Software " + rs : ver;
                }
                object q = Nat.Reg(Registry.LocalMachine, p, "HardwareInformation.qwMemorySize");
                if (q is long && (long)q > 0 && (ulong)(long)q > g.Vram) g.Vram = (ulong)(long)q;   // la de verdad (DXGI quita lo reservado)
                break;
            }
            if (g.Driver.Length == 0) g.Driver = g.DriverUmd;
        }

        // ================================================================== funciones de cada grafica
        public static void Derivar(GpuInfo g, OpcionesDeteccion op)
        {
            string n = g.Nombre.ToUpperInvariant();
            g.Portatil = g.Portatil || n.Contains("LAPTOP") || n.Contains("MAX-Q") || n.Contains("MOBILE") || n.Contains("NOTEBOOK");
            Familia(g, n);
            Indice(g, n);
            GpuFunciones f = g.F;
            bool d3d12 = g.NivelD3D12 >= 0xc000 || (g.NivelD3D12 == 0 && !g.Software && g.Familia != FamiliaGpu.NvidiaAntigua && g.Familia != FamiliaGpu.IntelAntigua);
            bool sm62 = g.ShaderModel >= 0x62 || (g.ShaderModel == 0 && d3d12);
            bool sm64 = g.ShaderModel >= 0x64 || (g.ShaderModel == 0 && d3d12 && g.Familia != FamiliaGpu.AmdRdna1 && g.Familia != FamiliaGpu.AmdAntigua);
            double gb = g.VramGb;
            bool win10_2004 = Nat.OsBuild() >= 19041;

            if (g.Software)
            {
                f.Motor.Pon(false, Tx.T("grafica por software (sin driver de grafica): solo imagen sin escalar", "software adapter: no scaling"));
                return;
            }
            f.Motor.Pon(d3d12, d3d12 ? Tx.T("Direct3D 12 ", "Direct3D 12 ") + NivelTexto(g.NivelD3D12) : Tx.T("sin Direct3D 12 nivel 12_0: el motor propio no arranca (mpv + Magpie)", "no Direct3D 12 FL 12_0"));
            f.Fsr1.Pon(true, Tx.T("shader propio (EASU + RCAS), cualquier grafica", "own shader, any GPU"));
            f.Fsr3Fg.Pon(d3d12 && sm62, d3d12 && sm62 ? Tx.T("Magpie (FSR 3 FG x2, flujo óptico de AMD)", "Magpie (FSR 3 FG x2)") : Tx.T("pide D3D12 12_0 y SM 6.2", "needs D3D12 FL 12_0 + SM 6.2"));
            f.Fsr3Fg.Dudoso = g.ShaderModel == 0;

            if (g.Fab == Fabricante.Nvidia)
            {
                bool rtx = g.EsRtx;
                int drv = g.DriverNv;
                int arq = g.NvArq > 0 ? g.NvArq : ArqPorFamilia(g.Familia);
                f.Reflex.Pon(arq >= 0x110, arq >= 0x110 ? "NVIDIA Reflex (Streamline)" : Tx.T("anterior a Maxwell", "pre-Maxwell"));
                f.Nvof.Pon(arq >= 0x160 && File.Exists(Path.Combine(Environment.SystemDirectory, "nvofapi64.dll")),
                           arq >= 0x160 ? Tx.T("flujo óptico por hardware (nvofapi64.dll)", "hardware optical flow") : Tx.T("pide Turing o posterior", "needs Turing+"));
                f.Nvof.Dudoso = g.Familia == FamiliaGpu.NvidiaGtx && arq >= 0x160;
                if (rtx)
                {
                    f.DlssSr.Pon(drv == 0 || drv >= 51215, drv == 0 || drv >= 51215 ? "DLSS Super Resolution (Streamline)" : Tx.T("driver anterior a 512.15: actualízalo", "driver older than 512.15"));
                    f.DlssTransformer.Pon(drv == 0 || drv >= 57216, Tx.T("modelos transformer J/K (DLSS 4); L/M (DLSS 4.5) con driver reciente", "transformer presets J/K; L/M with a recent driver") +
                                          (g.Familia == FamiliaGpu.NvidiaRtx20 || g.Familia == FamiliaGpu.NvidiaRtx30 ? Tx.T("; en RTX 20/30 cuestan bastante más (sin FP8): el banco decide", "; slower on RTX 20/30") : ""));
                    bool fgArq = g.Familia == FamiliaGpu.NvidiaRtx40 || g.Familia == FamiliaGpu.NvidiaRtx50;
                    bool hagsOk = g.Hags == 2 || (g.Hags < 0 && g.HagsDeSerie);
                    if (!fgArq) f.DlssG.Pon(false, Tx.T("DLSS Frame Generation solo en RTX 40 y 50", "DLSS FG needs RTX 40/50"));
                    else if (!hagsOk) f.DlssG.Pon(false, g.Hags == 1 ? Tx.T("falta la «Programación de GPU acelerada por hardware» (Configuración > Pantalla > Gráficos)", "turn on Hardware-accelerated GPU scheduling") : Tx.T("HAGS sin confirmar", "HAGS unknown"));
                    else if (!win10_2004) f.DlssG.Pon(false, Tx.T("pide Windows 10 2004 o posterior", "needs Windows 10 2004+"));
                    else f.DlssG.Pon(true, "DLSS-G (Streamline) + Reflex");
                    if (fgArq && g.Hags < 0) f.DlssG.Dudoso = true;
                    if (f.DlssG.Si && gb < 6) { f.DlssG.Dudoso = true; f.DlssG.Motivo += Tx.T("; con menos de 6 GB va muy justa (~1,1 GB a 4K)", "; under 6 GB VRAM is tight"); }
                    f.MfgMax = f.DlssG.Si ? (g.Familia == FamiliaGpu.NvidiaRtx50 && (drv == 0 || drv >= 57186) ? 4 : 2) : 1;
                    f.DlssMfg.Pon(f.MfgMax >= 3, g.Familia == FamiliaGpu.NvidiaRtx50 ? Tx.T("generación múltiple x3/x4 (RTX 50)", "multi frame generation x3/x4") : Tx.T("x3/x4 solo en RTX 50", "x3/x4 only on RTX 50"));
                    // Smooth Motion: la generacion del driver (perfil del exe). RTX 50 desde 571.86; RTX 40 con drivers recientes (umbral aproximado)
                    bool smArq = g.Familia == FamiliaGpu.NvidiaRtx50 || g.Familia == FamiliaGpu.NvidiaRtx40;
                    int smMin = g.Familia == FamiliaGpu.NvidiaRtx50 ? 57186 : 59000;
                    if (!smArq) f.SmoothMotion.Pon(false, Tx.T("solo RTX 40 y 50", "RTX 40/50 only"));
                    else if (drv > 0 && drv < smMin && g.SmoothAjuste != 1) f.SmoothMotion.Pon(false, Tx.T("driver anterior a ", "driver older than ") + Tx.N(smMin / 100.0, "0.00"));
                    else
                    {
                        f.SmoothMotion.Pon(true, Tx.T("generación del driver (perfil del exe, x2)", "driver frame generation (x2)") +
                                           (g.SmoothAjuste == 1 ? Tx.T("; el driver lista el ajuste", "; driver lists the setting") :
                                            Tx.T("; ajuste oculto del driver: se confirma al ponerlo en el perfil (si falla, DLSS-G)", "; hidden driver setting: confirmed when set (falls back to DLSS-G)")));
                        f.SmoothMotion.Dudoso = drv == 0 && g.SmoothAjuste != 1;
                    }
                    f.RtxVideo.Pon(drv == 0 || drv >= 53118, "RTX Video Super Resolution (Magpie)");
                    f.RtxHdr.Pon(drv == 0 || drv >= 55123, "RTX HDR (Magpie)");
                    // DLSS 5 (DLSSNR de Magpie): DLL no oficial, solo si el usuario la tiene
                    string dnr = Existe(op.RutasDlssNr);
                    if (dnr == null) f.DlssNr.Pon(false, Tx.T("no está nvngx_dlssnr.dll (extra no oficial: no va en la app)", "nvngx_dlssnr.dll not present (unofficial extra)"));
                    else if (gb < 6) f.DlssNr.Pon(false, Tx.T("menos de 6 GB de VRAM", "under 6 GB VRAM"));
                    else
                    {
                        f.DlssNr.Pon(true, Tx.T("DLL del usuario (sin firma, no oficial): ", "user DLL (unsigned, unofficial): ") + dnr +
                                     (g.Familia == FamiliaGpu.NvidiaRtx40 || g.Familia == FamiliaGpu.NvidiaRtx50 ? "" : Tx.T("; sin el modelo FP8 (pide RTX 40/50)", "; no FP8 model")));
                        f.DlssNr.NoOficial = true;
                    }
                    string sm86 = Existe(op.RutasDlssgSm86);
                    if (g.Familia == FamiliaGpu.NvidiaRtx30 && sm86 != null)
                    {
                        f.DlssGComunidad.Pon(true, Tx.T("runtime dlssg_sm86 de la comunidad (autofirmado): DLSS-G en RTX 30 por Magpie, apagado de serie", "community dlssg_sm86 runtime (self-signed), off by default"));
                        f.DlssGComunidad.NoOficial = true; f.DlssGComunidad.Dudoso = true;
                    }
                    else f.DlssGComunidad.Pon(false, g.Familia == FamiliaGpu.NvidiaRtx30 ? Tx.T("sin el runtime dlssg_sm86", "no dlssg_sm86 runtime") : "");
                }
                else
                {
                    f.DlssSr.Pon(false, g.Familia == FamiliaGpu.NvidiaGtx ? Tx.T("GTX: sin núcleos tensor (sin DLSS)", "GTX: no tensor cores") : Tx.T("grafica sin núcleos tensor", "no tensor cores"));
                    f.DlssTransformer.Pon(false, "");
                    f.DlssG.Pon(false, Tx.T("solo RTX 40 y 50", "RTX 40/50 only"));
                    f.SmoothMotion.Pon(false, Tx.T("solo RTX 40 y 50", "RTX 40/50 only"));
                    f.DlssNr.Pon(false, Tx.T("solo RTX", "RTX only"));
                    f.RtxVideo.Pon(false, Tx.T("solo RTX", "RTX only"));
                    f.RtxHdr.Pon(false, Tx.T("solo RTX", "RTX only"));
                }
                f.XessSr.Pon(sm64, sm64 ? Tx.T("XeSS (DP4a) por Magpie", "XeSS (DP4a) via Magpie") : Tx.T("pide SM 6.4 (DP4a)", "needs SM 6.4"));
                f.XessFg.Pon(sm64 && d3d12, Tx.T("XeSS FG por Magpie (fuera de Intel: modo de compatibilidad)", "XeSS FG via Magpie (compat mode)"));
                f.XessFg.Dudoso = true;
            }
            else
            {
                string no = Tx.T("solo NVIDIA RTX", "NVIDIA RTX only");
                f.DlssSr.Pon(false, no); f.DlssTransformer.Pon(false, no); f.DlssG.Pon(false, no); f.DlssMfg.Pon(false, no);
                f.SmoothMotion.Pon(false, no); f.DlssNr.Pon(false, no); f.RtxVideo.Pon(false, no); f.RtxHdr.Pon(false, no);
                f.Reflex.Pon(false, Tx.T("solo NVIDIA", "NVIDIA only")); f.Nvof.Pon(false, Tx.T("solo NVIDIA", "NVIDIA only"));
                f.DlssGComunidad.Pon(false, "");
            }

            if (g.Fab == Fabricante.Amd)
            {
                bool rdna2 = g.Familia == FamiliaGpu.AmdRdna2 || g.Familia == FamiliaGpu.AmdRdna3 || g.Familia == FamiliaGpu.AmdRdna4;
                f.Afmf.Pon(rdna2, rdna2 ? Tx.T("AFMF 2 del driver (AMD Software: actívalo para el exe del motor)", "driver AFMF 2 (enable it in AMD Software)") : Tx.T("AFMF pide RX 6000 o posterior", "AFMF needs RX 6000+"));
                f.Afmf.Dudoso = rdna2;
                f.AntiLag.Pon(g.Familia != FamiliaGpu.AmdAntigua || g.Serie >= 400, Tx.T("Radeon Anti-Lag del driver", "driver Radeon Anti-Lag"));
                // 21-11 (1.0.9; el usuario: "en las 9000 7000 y 6000 que use fsr 4 que lo soportan"): RX 9000 oficial; RX 7000 / 6000 con el
                // override de FSR 4.1.1 de este Magpie (THIRD-PARTY-NOTICES: "capability-check override", experimental; en RDNA 2 mas lento)
                bool fsr4 = g.Familia == FamiliaGpu.AmdRdna4 || g.Familia == FamiliaGpu.AmdRdna3 || g.Familia == FamiliaGpu.AmdRdna2;
                f.Fsr4.Pon(fsr4, g.Familia == FamiliaGpu.AmdRdna4 ? Tx.T("FSR 4 (Magpie) en RX 9000", "FSR 4 (Magpie) on RX 9000")
                              : fsr4 ? Tx.T("FSR 4 (Magpie, sin soporte oficial: override de FSR 4.1.1)", "FSR 4 (Magpie, unofficial: FSR 4.1.1 override)")
                              : Tx.T("FSR 4 pide RX 6000 o mas nueva", "FSR 4 needs RX 6000 or newer"));
                if (fsr4 && g.Familia != FamiliaGpu.AmdRdna4) f.Fsr4.Dudoso = true;
                f.XessSr.Pon(sm64, sm64 ? Tx.T("XeSS (DP4a) por Magpie", "XeSS (DP4a) via Magpie") : Tx.T("pide SM 6.4 (DP4a)", "needs SM 6.4"));
                f.XessFg.Pon(sm64 && d3d12, Tx.T("XeSS FG por Magpie (modo de compatibilidad)", "XeSS FG via Magpie (compat mode)"));
                f.XessFg.Dudoso = true;
            }
            else if (g.Fab == Fabricante.Intel)
            {
                bool arc = g.Familia == FamiliaGpu.IntelArc || g.Familia == FamiliaGpu.IntelArcB;
                f.XessSr.Pon(sm64 || arc, arc ? Tx.T("XeSS con XMX (Intel Arc)", "XeSS on XMX (Arc)") : sm64 ? Tx.T("XeSS (DP4a): lento en integradas", "XeSS (DP4a): slow on iGPU") : Tx.T("pide SM 6.4", "needs SM 6.4"));
                f.XessFg.Pon(arc, arc ? Tx.T("XeSS FG nativo de Intel Arc (Magpie)", "native XeSS FG on Arc (Magpie)") : Tx.T("XeSS FG pide Intel Arc", "XeSS FG needs Intel Arc"));
                f.Afmf.Pon(false, Tx.T("solo AMD", "AMD only")); f.AntiLag.Pon(false, Tx.T("solo AMD", "AMD only")); f.Fsr4.Pon(false, Tx.T("solo AMD RX 9000", "AMD RX 9000 only"));
            }
            else
            {
                f.Afmf.Pon(false, Tx.T("solo AMD", "AMD only")); f.AntiLag.Pon(false, Tx.T("solo AMD", "AMD only")); f.Fsr4.Pon(false, Tx.T("solo AMD RX 9000", "AMD RX 9000 only"));
            }
        }

        static string Existe(string[] rutas)
        {
            if (rutas == null) return null;
            foreach (string r in rutas) { try { if (!string.IsNullOrEmpty(r) && (File.Exists(r) || Directory.Exists(r))) return r; } catch { } }
            return null;
        }

        public static string NivelTexto(int lv)
        {
            if (lv <= 0) return "";
            return ((lv >> 12) & 0xF) + "_" + ((lv >> 8) & 0xF);
        }

        static int ArqPorFamilia(FamiliaGpu f)
        {
            switch (f)
            {
                case FamiliaGpu.NvidiaRtx50: return 0x1B0;
                case FamiliaGpu.NvidiaRtx40: return 0x190;
                case FamiliaGpu.NvidiaRtx30: return 0x170;
                case FamiliaGpu.NvidiaRtx20: return 0x160;
                case FamiliaGpu.NvidiaGtx: return 0x130;
                default: return 0;
            }
        }

        // ------------------------------------------------------------------ familia (por NVAPI, el modelo PCI o el nombre)
        static void Familia(GpuInfo g, string n)
        {
            int dev = g.DeviceId;
            if (g.Software) { g.Familia = FamiliaGpu.Software; g.Arquitectura = "WARP"; return; }
            if (g.Fab == Fabricante.Nvidia)
            {
                int a = g.NvArq;
                if (a == 0)   // sin NVAPI: por el modelo PCI
                    a = dev >= 0x2900 ? 0x1B0 : dev >= 0x2600 ? 0x190 : dev >= 0x2200 ? 0x170 : dev >= 0x1E00 ? 0x160 : dev >= 0x1D80 ? 0x140 : dev >= 0x1500 ? 0x130 : dev >= 0x1340 ? 0x110 : 0x100;
                g.Arquitectura = NvApi.ArqNombre(a) + (g.NvArq == 0 ? Tx.T(" (por el modelo)", " (by device id)") : "");
                bool sinTensor = n.Contains("GTX") || n.Contains(" MX") || n.Contains("QUADRO T") || System.Text.RegularExpressions.Regex.IsMatch(n, @"\bT\d{3,4}\b");
                // Turing sin RTX = GTX 16 (TU116/TU117, sin nucleos tensor)
                if (a >= 0x160 && a < 0x170 && !n.Contains("RTX")) sinTensor = true;
                if (a >= 0x1A0 && !sinTensor) { g.Familia = FamiliaGpu.NvidiaRtx50; g.Serie = 50; }
                else if (a >= 0x190 && !sinTensor) { g.Familia = FamiliaGpu.NvidiaRtx40; g.Serie = 40; }
                else if (a >= 0x170 && !sinTensor) { g.Familia = FamiliaGpu.NvidiaRtx30; g.Serie = 30; }
                else if (a >= 0x160 && !sinTensor) { g.Familia = FamiliaGpu.NvidiaRtx20; g.Serie = 20; }
                else if (a >= 0x110) { g.Familia = FamiliaGpu.NvidiaGtx; g.Serie = a >= 0x160 ? 16 : a >= 0x130 ? 10 : 9; }
                else g.Familia = FamiliaGpu.NvidiaAntigua;
                return;
            }
            if (g.Fab == Fabricante.Amd)
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(n, @"RX\s*(\d{3,4})");
                int num = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                g.Serie = num >= 1000 ? num / 1000 * 1000 : num;
                if (num >= 9000) { g.Familia = FamiliaGpu.AmdRdna4; g.Arquitectura = "RDNA 4"; }
                else if (num >= 7000) { g.Familia = FamiliaGpu.AmdRdna3; g.Arquitectura = "RDNA 3"; }
                else if (num >= 6000) { g.Familia = FamiliaGpu.AmdRdna2; g.Arquitectura = "RDNA 2"; }
                else if (num >= 5000) { g.Familia = FamiliaGpu.AmdRdna1; g.Arquitectura = "RDNA 1"; }
                else if (num > 0) { g.Familia = FamiliaGpu.AmdAntigua; g.Arquitectura = "GCN"; }
                else if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\b(8[0-9]0M|890M|880M)\b")) { g.Familia = FamiliaGpu.AmdRdna3; g.Arquitectura = "RDNA 3.5 (integrada)"; g.Integrada = true; }
                else if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\b7[0-9]0M\b")) { g.Familia = FamiliaGpu.AmdRdna3; g.Arquitectura = "RDNA 3 (integrada)"; g.Integrada = true; }
                else if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\b6[0-9]0M\b") || dev == 0x163F || dev == 0x1435) { g.Familia = FamiliaGpu.AmdRdna2; g.Arquitectura = "RDNA 2 (integrada)"; g.Integrada = true; }
                else if (dev >= 0x7550 && dev < 0x7600) { g.Familia = FamiliaGpu.AmdRdna4; g.Arquitectura = "RDNA 4"; }
                else if (dev >= 0x7440 && dev < 0x7500) { g.Familia = FamiliaGpu.AmdRdna3; g.Arquitectura = "RDNA 3"; }
                else if (dev >= 0x73A0 && dev < 0x7440) { g.Familia = FamiliaGpu.AmdRdna2; g.Arquitectura = "RDNA 2"; }
                else if (dev >= 0x7310 && dev < 0x73A0) { g.Familia = FamiliaGpu.AmdRdna1; g.Arquitectura = "RDNA 1"; }
                else { g.Familia = FamiliaGpu.AmdAntigua; g.Arquitectura = "GCN / Vega"; g.Integrada = g.Vram < 1UL << 30; }
                return;
            }
            if (g.Fab == Fabricante.Intel)
            {
                if (n.Contains("ARC") && System.Text.RegularExpressions.Regex.IsMatch(n, @"\bB\d{3}\b")) { g.Familia = FamiliaGpu.IntelArcB; g.Arquitectura = "Xe2 (Battlemage)"; }
                else if (n.Contains("ARC") && System.Text.RegularExpressions.Regex.IsMatch(n, @"\bA\d{3}")) { g.Familia = FamiliaGpu.IntelArc; g.Arquitectura = "Xe-HPG (Alchemist)"; }
                else if (n.Contains("ARC")) { g.Familia = FamiliaGpu.IntelArc; g.Arquitectura = Tx.T("Arc integrada (Xe-LPG / Xe2)", "integrated Arc"); g.Integrada = true; }
                else if (n.Contains("IRIS XE") || n.Contains("UHD GRAPHICS 7") || dev >= 0x4600) { g.Familia = FamiliaGpu.IntelXe; g.Arquitectura = "Xe-LP"; g.Integrada = true; }
                else { g.Familia = FamiliaGpu.IntelAntigua; g.Arquitectura = "Gen9-11"; g.Integrada = true; }
                return;
            }
            g.Familia = FamiliaGpu.Otra;
        }

        // ------------------------------------------------------------------ potencia aproximada (RTX 4060 = 1,0; el banco la sustituye)
        static readonly string[][] Indices =
        {
            new[] { "RTX 5090", "3.8" }, new[] { "RTX 5080", "2.6" }, new[] { "RTX 5070 TI", "2.3" }, new[] { "RTX 5070", "1.75" },
            new[] { "RTX 5060 TI", "1.4" }, new[] { "RTX 5060", "1.2" }, new[] { "RTX 5050", "0.95" },
            new[] { "RTX 4090", "3.1" }, new[] { "RTX 4080 SUPER", "2.4" }, new[] { "RTX 4080", "2.35" }, new[] { "RTX 4070 TI SUPER", "2.05" },
            new[] { "RTX 4070 TI", "1.9" }, new[] { "RTX 4070 SUPER", "1.75" }, new[] { "RTX 4070", "1.5" }, new[] { "RTX 4060 TI", "1.2" },
            new[] { "RTX 4060", "1.0" }, new[] { "RTX 4050", "0.8" },
            new[] { "RTX 3090 TI", "1.9" }, new[] { "RTX 3090", "1.75" }, new[] { "RTX 3080 TI", "1.7" }, new[] { "RTX 3080", "1.5" },
            new[] { "RTX 3070 TI", "1.22" }, new[] { "RTX 3070", "1.15" }, new[] { "RTX 3060 TI", "1.0" }, new[] { "RTX 3060", "0.78" },
            new[] { "RTX 3050", "0.55" },
            new[] { "TITAN RTX", "1.25" }, new[] { "RTX 2080 TI", "1.2" }, new[] { "RTX 2080 SUPER", "1.0" }, new[] { "RTX 2080", "0.95" },
            new[] { "RTX 2070 SUPER", "0.88" }, new[] { "RTX 2070", "0.78" }, new[] { "RTX 2060 SUPER", "0.72" }, new[] { "RTX 2060", "0.62" },
            new[] { "GTX 1080 TI", "0.95" }, new[] { "GTX 1080", "0.7" }, new[] { "GTX 1070", "0.55" }, new[] { "GTX 1660", "0.47" },
            new[] { "GTX 1650", "0.3" }, new[] { "GTX 1060", "0.4" }, new[] { "GTX 1050", "0.22" },
            new[] { "RX 9070 XT", "2.4" }, new[] { "RX 9070", "2.1" }, new[] { "RX 9060 XT", "1.3" },
            new[] { "RX 7900 XTX", "2.55" }, new[] { "RX 7900 XT", "2.2" }, new[] { "RX 7900 GRE", "1.85" }, new[] { "RX 7800 XT", "1.7" },
            new[] { "RX 7700 XT", "1.45" }, new[] { "RX 7600", "1.0" },
            new[] { "RX 6950 XT", "1.85" }, new[] { "RX 6900 XT", "1.75" }, new[] { "RX 6800 XT", "1.65" }, new[] { "RX 6800", "1.45" },
            new[] { "RX 6750 XT", "1.2" }, new[] { "RX 6700", "1.12" }, new[] { "RX 6650 XT", "1.0" }, new[] { "RX 6600", "0.88" },
            new[] { "RX 6500", "0.4" }, new[] { "RX 5700", "0.85" }, new[] { "RX 5600", "0.7" }, new[] { "RX 5500", "0.45" },
            new[] { "RX 590", "0.48" }, new[] { "RX 580", "0.45" }, new[] { "RX 570", "0.38" }, new[] { "890M", "0.4" }, new[] { "880M", "0.35" },
            new[] { "780M", "0.3" }, new[] { "760M", "0.25" }, new[] { "680M", "0.25" },
            new[] { "ARC B580", "0.95" }, new[] { "ARC B570", "0.85" }, new[] { "ARC A770", "0.9" }, new[] { "ARC A750", "0.8" },
            new[] { "ARC A580", "0.7" }, new[] { "ARC A380", "0.3" }, new[] { "ARC 140V", "0.35" }, new[] { "ARC(TM) 140V", "0.35" },
            new[] { "IRIS XE", "0.12" }, new[] { "UHD", "0.06" },
        };

        static void Indice(GpuInfo g, string n)
        {
            string nn = n.Replace("(TM)", "").Replace("  ", " ");
            foreach (string[] e in Indices)
                if (nn.Contains(e[0]))
                {
                    g.Indice = double.Parse(e[1], CultureInfo.InvariantCulture) * (g.Portatil ? 0.75 : 1.0);
                    return;
                }
            g.IndiceEstimado = true;
            double gb = g.Vram / (1024.0 * 1024 * 1024);
            switch (g.Familia)
            {
                case FamiliaGpu.NvidiaRtx50: g.Indice = 1.3; break;
                case FamiliaGpu.NvidiaRtx40: g.Indice = 1.0; break;
                case FamiliaGpu.NvidiaRtx30: g.Indice = 0.8; break;
                case FamiliaGpu.NvidiaRtx20: g.Indice = 0.65; break;
                case FamiliaGpu.NvidiaGtx: g.Indice = 0.4; break;
                case FamiliaGpu.AmdRdna4: g.Indice = 1.5; break;
                case FamiliaGpu.AmdRdna3: g.Indice = g.Integrada ? 0.3 : 1.1; break;
                case FamiliaGpu.AmdRdna2: g.Indice = g.Integrada ? 0.22 : 0.9; break;
                case FamiliaGpu.AmdRdna1: g.Indice = 0.6; break;
                case FamiliaGpu.IntelArcB: g.Indice = 0.85; break;
                case FamiliaGpu.IntelArc: g.Indice = g.Integrada ? 0.3 : 0.6; break;
                case FamiliaGpu.IntelXe: g.Indice = 0.12; break;
                case FamiliaGpu.Software: g.Indice = 0.01; break;
                default: g.Indice = gb >= 6 ? 0.4 : 0.1; break;
            }
            if (g.Portatil) g.Indice *= 0.75;
        }
    }
}
