using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ConsoleEnhancer.Motor.Capture;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 18-11 (MOTOR-CE): el motor de ConsoleEnhancer (app universal: cualquier grafica y capturadora). Lo que no es de este PC:
    //  - la grafica: adapter_luid=<hex> o la de alto rendimiento (GpuChoice, con el dxgi.dll del sistema y ANTES de Streamline)
    //  - Streamline: streamline=auto|0|1 (auto = solo con NVIDIA 0x10DE y sl.interposer.dll junto al exe; 0 = nunca se carga)
    //  - escalador: upscaler=auto|dlss|fsr1|catrom (auto = DLSS si esta, si no FSR 1 EASU + RCAS; Shaders.Easu) y enfoque=auto|cas|rcas
    //  - sin Reflex: swapchain con espera y cola de 1 (cola_max=-1, auto)
    //  - capturadora: device_path= exacto, device= subcadena o la primera que no parezca virtual (CaptureChoice)
    //  - datos=<carpeta>: engine.log, logs de Streamline, cache de shaders y bancos relativos (Program.DataDir)
    //  - salida: out=0 = el modo actual de la pantalla de la ventana (Program.ResolveOutput)

    // La grafica elegida, vista con el dxgi.dll del sistema (sin cargar nada de Streamline)
    class GpuChoice
    {
        public bool Found;
        public string Name = "";
        public uint VendorId, DeviceId;
        public long Luid;
        public ulong VramMb;
        public bool Software;
        public bool IsNvidia { get { return VendorId == 0x10DE; } }
        public string VendorText
        {
            get
            {
                return VendorId == 0x10DE ? "NVIDIA" : VendorId == 0x1002 || VendorId == 0x1022 ? "AMD" : VendorId == 0x8086 ? "Intel" :
                       VendorId == 0x1414 ? "Microsoft" : VendorId == 0x5143 ? "Qualcomm" : "0x" + VendorId.ToString("X4");
            }
        }
        public override string ToString()
        {
            return Name + " (" + VendorText + " " + VendorId.ToString("X4") + ":" + DeviceId.ToString("X4") + ", " + VramMb + " MB, LUID 0x" + Luid.ToString("X") +
                   (Software ? ", por software" : "") + ")";
        }

        static GpuChoice FromAdapter(object ao)
        {
            GpuChoice g = new GpuChoice();
            IDXGIAdapter1 a = ao as IDXGIAdapter1;
            DXGI_ADAPTER_DESC1 d;
            if (a == null || a.GetDesc1(out d) < 0) return g;
            g.Found = true;
            g.Name = d.Description ?? "";
            g.VendorId = d.VendorId; g.DeviceId = d.DeviceId;
            g.Luid = d.AdapterLuid.ToInt64();
            g.VramMb = d.DedicatedVideoMemory.ToUInt64() >> 20;
            g.Software = (d.Flags & DXGI_ADAPTER_FLAG.SOFTWARE) != 0;
            return g;
        }

        // La grafica de una fabrica (la del sistema o la de Streamline): por LUID si se pide (y esta), si no la de alto rendimiento,
        // si no la primera. note: lo que haya pasado ("" si nada)
        public static object Choose(object factory, long luid, ref string note)
        {
            object adapter = null;
            Guid iid = typeof(IDXGIAdapter1).GUID;
            if (luid != 0)
            {
                IDXGIFactory4 f4 = factory as IDXGIFactory4;
                LUID l; l.LowPart = (uint)(luid & 0xFFFFFFFFL); l.HighPart = (int)(luid >> 32);
                if (f4 == null || f4.EnumAdapterByLuid(l, ref iid, out adapter) < 0 || adapter == null)
                {
                    adapter = null;
                    note = "adapter_luid=0x" + luid.ToString("X") + ": no hay ninguna grafica con ese LUID; se usa la de alto rendimiento";
                }
            }
            if (adapter == null)
            {
                IDXGIFactory6 f6 = factory as IDXGIFactory6;
                if (f6 != null && f6.EnumAdapterByGpuPreference(0, DXGI_GPU_PREFERENCE.HIGH_PERFORMANCE, ref iid, out adapter) < 0) adapter = null;
            }
            if (adapter == null)
            {
                IDXGIFactory2 f2 = factory as IDXGIFactory2;
                IDXGIAdapter1 a1;
                if (f2 != null && f2.EnumAdapters1(0, out a1) >= 0) adapter = a1;
            }
            return adapter;
        }

        static object SystemFactory(out string err)
        {
            err = "";
            object fo;
            Guid iid = typeof(IDXGIFactory2).GUID;
            int hr = DxgiApi.CreateDXGIFactory2(DXGI_CREATE_FACTORY.NONE, ref iid, out fo);
            if (hr < 0 || fo == null) { err = "DXGI: no se pudo crear la fabrica (" + Hr.Format(hr) + ")"; return null; }
            return fo;
        }

        // wantLuid = 0: la de alto rendimiento
        public static GpuChoice Pick(long wantLuid, out string note)
        {
            note = "";
            GpuChoice g = new GpuChoice();
            object fo = null, ao = null;
            try
            {
                fo = SystemFactory(out note);
                if (fo == null) return g;
                ao = Choose(fo, wantLuid, ref note);
                g = FromAdapter(ao);
            }
            catch (Exception ex) { note = "DXGI (eligiendo la grafica): " + ex.Message; }
            finally { Com.Release(ref ao); Com.Release(ref fo); }
            return g;
        }

        // ¿Alguna grafica NVIDIA? (para --nvidia). names: las que hay
        public static bool AnyNvidia(out string names)
        {
            bool any = false;
            List<string> l = new List<string>();
            object fo = null;
            try
            {
                string err;
                fo = SystemFactory(out err);
                IDXGIFactory2 f2 = fo as IDXGIFactory2;
                for (uint i = 0; f2 != null && i < 16; i++)
                {
                    IDXGIAdapter1 a;
                    if (f2.EnumAdapters1(i, out a) < 0 || a == null) break;
                    object ao = a;
                    GpuChoice g = FromAdapter(ao);
                    Com.Release(ref ao);
                    if (!g.Found) continue;
                    l.Add(g.Name + " " + g.VendorText);
                    if (g.IsNvidia) any = true;
                }
            }
            catch (Exception ex) { l.Add("DXGI: " + ex.Message); }
            finally { Com.Release(ref fo); }
            names = l.Count > 0 ? string.Join(", ", l.ToArray()) : "ninguna";
            return any;
        }
    }

    unsafe partial class Engine
    {
        GpuChoice gpu = new GpuChoice();    // la grafica elegida (antes de Streamline)
        string scalerNow = "";              // el escalador de la imagen de ahora: dlss | fsr1 | catrom | nativo
        ID3D12PipelineState psoEasu;
        int queueMax;                       // la cola de la swapchain con espera (0 = sin espera, la de DXGI)

        bool WantDlss { get { return s.Upscaler == "dlss" || s.Upscaler == "auto"; } }
        // RCAS (el enfoque de FSR 1) tras fsr1 de serie (enfoque=auto); enfoque=cas|rcas para elegir
        bool RcasNow { get { return s.Sharpen == "rcas" || (s.Sharpen != "cas" && scalerNow == "fsr1"); } }

        // Antes de InitDevice: la grafica y si se carga Streamline. Con streamline=0 (o auto sin NVIDIA) no se carga sl.interposer.dll
        // nunca: ni slInit ni los CreateDXGIFactory2 / D3D12CreateDevice de Streamline (slOn = false: Reflex, PCL, DLSS y la generacion
        // quedan apagados y sus llamadas no se hacen)
        void StartStreamline()
        {
            string note;
            gpu = GpuChoice.Pick(s.AdapterLuid, out note);
            if (note.Length > 0) Log.Write(note);
            Log.Write("Grafica: " + (gpu.Found ? gpu.ToString() : "no se sabe (DXGI)") + (s.AdapterLuid != 0 && note.Length == 0 ? " (adapter_luid)" : " (la de alto rendimiento)"));
            bool dll = File.Exists(Path.Combine(Program.Dir, "sl.interposer.dll"));
            string no = null;
            if (s.Streamline == 0) no = "streamline=0";
            else if (FgFfx) no = "generacion FSR 3 (FidelityFX lleva su propia cadena de presentacion)";   // 21-11 (EngineFgFfx.cs)
            else if (!dll) no = "no esta sl.interposer.dll junto al exe";
            else if (s.Streamline < 0 && !gpu.IsNvidia) no = "streamline=auto y la grafica no es NVIDIA" + (gpu.Found ? " (" + gpu.VendorText + ")" : "");
            else if (!(s.Standby || WantDlss || s.Fg || s.Reflex > 0)) no = "no hace falta: upscaler=" + s.Upscaler + ", sin generacion ni Reflex";
            if (no == null)
            {
                InitStreamline();
                if (slOn) return;
                no = "slInit no fue bien";
            }
            Log.Write("Sin Streamline (" + no + "): DXGI y D3D12 del sistema, sin DLSS, generacion ni Reflex; escalador " +
                      (s.Upscaler == "catrom" ? "catrom" : "fsr1") + (s.QueueMax < 0 ? ", swapchain con espera (cola de 1)" : ""));
        }

        // InitDevice: la grafica en la fabrica de verdad (la de Streamline si va), la misma que GpuChoice (por su LUID)
        object ChooseAdapter(object factory)
        {
            string note = "";
            object a = GpuChoice.Choose(factory, gpu.Found ? gpu.Luid : s.AdapterLuid, ref note);
            if (note.Length > 0) Log.Write(note);
            return a;
        }

        // InitDevice, antes de crear la swapchain: cola_max=N, o -1 (auto) = 1 sin Reflex ni generacion (sin Streamline o sin su Reflex: sin
        // nada que marque el ritmo, DXGI dejaria 3 imagenes en cola); con Reflex la de DXGI como siempre (el ritmo lo lleva Reflex; con la
        // generacion de NVIDIA no se puede pedir: docs\SL_NOTES.md)
        int QueueMaxNow() { return FgFfx ? 0 : s.QueueMax >= 0 ? s.QueueMax : (!reflexOk && !fgOk ? 1 : 0); }   // (21-11: con FSR 3 FG el ritmo lo lleva su cadena)

        // Cada imagen (Run, tras ConfigureFeatures): el escalador que toca. Al cambiar, sin historia (DLSS y los vectores)
        void UpdateScaler()
        {
            if (inW <= 0 || inH <= 0) return;
            string sc, why = "";
            if (inW == outW && inH == outH) sc = "nativo";
            else if (WantDlss && dlssOk && !dlssBroken) sc = "dlss";
            else if (s.Upscaler == "catrom") sc = "catrom";
            else if (inW > outW || inH > outH) { sc = "catrom"; why = "la entrada es mayor que la salida: FSR 1 solo amplia"; }
            // 21-11 (EngineSr.cs): XeSS SR y FSR 3.1 / 4 en el motor (antes solo por mpv + Magpie)
            else if (WantXess && XessEnsure()) sc = "xess";
            else if (WantFfx && FfxEnsure()) sc = "ffx";
            else
            {
                sc = "fsr1";
                if (WantDlss) why = !slOn ? "sin Streamline" : !dlssOk ? "sin DLSS en esta grafica" : "DLSS falla";
                else if (WantXess) why = "XeSS no va aqui (ver arriba)";
                else if (WantFfx) why = "FSR 3/4 no va aqui (ver arriba)";
            }
            if (sc == scalerNow) return;
            if (scalerNow.Length > 0) { resetHistory = true; motionValid = false; }
            scalerNow = sc;
            Log.Write("Escalador: " + (sc == "fsr1" ? "fsr1 (AMD FidelityFX FSR 1 EASU)" : sc == "xess" ? "XeSS (" + xessQualityName + ")" : sc == "ffx" ? ffxName : sc) + " " + inW + "x" + inH + " -> " + outW + "x" + outH +
                      ", enfoque " + (RcasNow ? "RCAS" : "CAS") + " " + s.Sharp.ToString("0.00", CultureInfo.InvariantCulture) + (why.Length > 0 ? " (" + why + ")" : ""));
        }

        // RenderFrame, paso 2 con fsr1: inColor -> outColor (TScale: t0 = inColor, u0 = outColor, ya en UNORDERED_ACCESS)
        void RecordEasu(uint* c)
        {
            float sx = (float)inW / outW, sy = (float)inH / outH;
            c[0] = (uint)outW; c[1] = (uint)outH; c[2] = (uint)inW; c[3] = (uint)inH;
            c[4] = F(sx); c[5] = F(sy); c[6] = F(0.5f * sx - 0.5f); c[7] = F(0.5f * sy - 0.5f);
            BindCompute(psoEasu, TScale, c, 8);
            list.Dispatch(Groups(outW, 16), Groups(outH, 8), 1);
        }

        // Para la linea del banco
        string UniversalBench()
        {
            return "\tescalador=" + scalerNow + "\tenfoque=" + (RcasNow ? "rcas" : "cas") + "\tstreamline=" + (slOn ? "si" : "no") +
                   "\tgrafica=" + (gpu.Found ? gpu.Name + " " + gpu.VendorText : "?");
        }
    }

    // ---- La carpeta de datos, la salida y el perfil de NVIDIA
    static partial class Program
    {
        public const string WindowTitle = "ConsoleEnhancer";      // (antes "Prisma")
        static string dataDir;
        public static string DataDir { get { return dataDir ?? Dir; } }
        // Rutas relativas (bench_out=...) dentro de la carpeta de datos
        public static string DataPath(string p) { return string.IsNullOrEmpty(p) || Path.IsPathRooted(p) ? p : Path.Combine(DataDir, p); }

        // datos=<carpeta> (o --datos=), antes de abrir el log
        static void DataInit(string[] args)
        {
            foreach (string a in args)
            {
                string t = a.TrimStart('-');
                if (!t.StartsWith("datos=", StringComparison.OrdinalIgnoreCase)) continue;
                string v = t.Substring(6).Trim().Trim('"');
                if (v.Length == 0) continue;
                try { v = Path.GetFullPath(v); Directory.CreateDirectory(v); dataDir = v; } catch { }
            }
        }

        public static string ExeName
        {
            get { try { return Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location); } catch { return "ConsoleEnhancerEngine.exe"; } }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryExW(string name, IntPtr file, uint flags);

        // --nvidia: el perfil de NVIDIA del exe (NvDrs), solo con una grafica NVIDIA y su nvapi64.dll
        static int NvidiaProfile()
        {
            string names;
            if (!GpuChoice.AnyNvidia(out names)) { Log.Write("--nvidia: no hay grafica NVIDIA (" + names + "): nada que hacer"); return 0; }
            IntPtr h = IntPtr.Zero;
            try { h = LoadLibraryExW("nvapi64.dll", IntPtr.Zero, 0x800 /*LOAD_LIBRARY_SEARCH_SYSTEM32*/); } catch { }
            if (h == IntPtr.Zero) { Log.Write("--nvidia: no esta nvapi64.dll (el driver de NVIDIA): sin perfil"); return 1; }
            return NvDrs.Ensure(ExeName) >= 0 ? 0 : 1;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE   // 220 bytes (DEVMODEW de pantalla)
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string device, int mode, ref DEVMODE dm);

        // out=0 (de serie): el modo actual de la pantalla de la ventana (monitor=N; -1 = la principal), antes 3840x2160 fijo
        static void ResolveOutput(Settings s)
        {
            if (s.OutW > 0 && s.OutH > 0) return;
            Screen sc = Screen.PrimaryScreen;
            if (s.Monitor >= 0 && s.Monitor < Screen.AllScreens.Length) sc = Screen.AllScreens[s.Monitor];
            int w = sc.Bounds.Width, h = sc.Bounds.Height;
            string how = "su tamaño en el escritorio";
            try
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettingsW(sc.DeviceName, -1 /*ENUM_CURRENT_SETTINGS*/, ref dm) && dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
                {
                    w = dm.dmPelsWidth; h = dm.dmPelsHeight;
                    how = "modo actual, " + dm.dmDisplayFrequency + " Hz";
                }
            }
            catch { }
            if (w <= 0 || h <= 0) { w = 1920; h = 1080; how = "no se sabe: 1920x1080"; }
            s.OutW = w; s.OutH = h;
            Log.Write("Salida " + w + "x" + h + " (" + how + " de la pantalla " + (s.Monitor >= 0 ? s.Monitor.ToString(CultureInfo.InvariantCulture) : "principal") + " " + sc.DeviceName +
                      (sc.Bounds.Width != w || sc.Bounds.Height != h ? "; en el escritorio " + sc.Bounds.Width + "x" + sc.Bounds.Height : "") + "; out=WxH para otra)");
        }

        // El titulo y la clase de la ventana (la clase la pone WinForms), para el lanzador
        static void LogWindow()
        {
            try { Log.Write("Ventana: titulo \"" + WindowTitle + "\", clase " + ClassOf(window.Handle) + ", proceso " + ExeName); } catch { }
        }
    }

    // ---- 18-11 (MOTOR-CE): que capturadora abrir. device_path = exactamente esa (el DevicePath de DirectShow, como lo da la deteccion);
    // device = subcadena del nombre (como antes); sin ninguno, la primera capturadora de video de DirectShow que no parezca virtual
    // (OBS, NVIDIA Broadcast, Camo, DroidCam...; las de software sin DevicePath tampoco; las \\?\root# y \\?\swd# solo si no hay otra)
    static class CaptureChoice
    {
        static readonly string[] Virtual = { "virtual", "obs", "broadcast", "camo", "droidcam", "snap camera", "xsplit", "streamlabs", "manycam",
                                             "e2esoft", "vcam", "splitcam", "iriun", "epoccam" };
        static string cacheKey, cacheLink, cacheName;

        static List<CaptureDeviceInfo> Devices()
        {
            try { return DsCapture.ListDevices(4000); }
            catch (Exception ex) { Log.Write("Capturadoras de DirectShow: no se pudieron listar (" + ex.Message + ")"); return new List<CaptureDeviceInfo>(); }
        }

        // 0 = vale, 1 = solo si no hay otra (dispositivo de software con DevicePath), 2 = fuera (virtual)
        static int Rank(CaptureDeviceInfo d, out string why)
        {
            why = "";
            string n = (d.FriendlyName ?? "").ToLowerInvariant();
            foreach (string w in Virtual) if (n.Contains(w)) { why = "virtual: «" + w + "»"; return 2; }
            string p = (d.SymbolicLink ?? "").ToLowerInvariant();
            if (p.Length == 0) { why = "sin DevicePath: de software"; return 2; }
            if (p.StartsWith(@"\\?\root#", StringComparison.Ordinal) || p.StartsWith(@"\\?\swd#", StringComparison.Ordinal)) { why = "dispositivo de software"; return 1; }
            return 0;
        }

        // Pone o.SymbolicLink / o.DeviceName. Devuelve el nombre para el log (null = no hay ninguna que valga)
        public static string Apply(Settings s, CaptureOptions o)
        {
            if (s.DevicePath.Length > 0)
            {
                o.SymbolicLink = s.DevicePath;
                string key = "p|" + s.DevicePath;
                if (cacheKey != key)
                {
                    cacheKey = key; cacheName = null; cacheLink = s.DevicePath;
                    List<CaptureDeviceInfo> l = Devices();
                    foreach (CaptureDeviceInfo d in l) if (string.Equals(d.SymbolicLink, s.DevicePath, StringComparison.OrdinalIgnoreCase)) cacheName = d.FriendlyName;
                    Log.Write("Capturadora por device_path: " + (cacheName ?? "NO esta entre las " + l.Count + " de DirectShow (se intenta igual)") + " [" + s.DevicePath + "]");
                }
                return cacheName ?? s.DevicePath;
            }
            if (s.Device.Length > 0) { o.DeviceName = s.Device; return s.Device; }
            if (cacheKey != "auto" || cacheName == null)
            {
                cacheKey = "auto"; cacheLink = null; cacheName = null;
                List<CaptureDeviceInfo> l = Devices();
                List<string> parts = new List<string>();
                CaptureDeviceInfo best = null; int bestRank = 9;
                foreach (CaptureDeviceInfo d in l)
                {
                    string why;
                    int r = Rank(d, out why);
                    parts.Add("\"" + d.FriendlyName + "\"" + (why.Length > 0 ? " (" + why + ")" : ""));
                    if (r < 2 && r < bestRank) { best = d; bestRank = r; }
                }
                if (best != null) { cacheName = best.FriendlyName; cacheLink = best.SymbolicLink; }
                Log.Write("Capturadoras de DirectShow: " + (l.Count == 0 ? "ninguna" : string.Join(", ", parts.ToArray())) +
                          (best == null ? " -> ninguna vale (device= o device_path= para elegir una)" : " -> \"" + best.FriendlyName + "\" [" + best.SymbolicLink + "]"));
            }
            if (cacheName == null) return null;
            if (!string.IsNullOrEmpty(cacheLink)) o.SymbolicLink = cacheLink; else o.DeviceName = cacheName;
            return cacheName;
        }
    }
}
