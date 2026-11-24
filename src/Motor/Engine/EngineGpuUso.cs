using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineGpuUso.cs - 24-11-2026 (1.3.2, agente FPS). El usuario, tras jugar con la 1.3.1: "no va el modo 4x de ninguna manera", "el
    // enhanced fg no me llega a 120 fps ni con dlss 5 off en ninguna resolucion ni modo" y "Bug no llega a 120 fps 60x2 a 1440p con dlss 5 y
    // smooth". En su sesion (24-11 23:55-01:48) la escalera (EngineEscalera.cs) bajo DLSS 5, el modelo de DLSS SR, puso FSR 1, quito el x4 y
    // hasta apago DLSS 5 con la grafica SOBRADA: Enhanced FG x4 a 1080p sin DLSS 5 con la grafica del motor a 3,3 ms de 33 ("inventadas
    // saltadas 10 de 135": el ritmo de Enhanced FG, no la grafica) y Smooth Motion x2 en el modo 60 con 58,0 de 59,94 reales a 4,3 ms de 16,7
    // (Reflex duerme ~14 ms por imagen: su tope con VRR, 116 = 120 - 120^2/3600, deja 58 reales; "4 tiradas en 4 cuartos" siempre igual, con
    // DLSS 5 al 25 % y con FSR 1, y tambien en el HOME). La grafica del motor (su p95) no ve la de la generacion (Smooth Motion va en el driver,
    // Enhanced FG en su cola), asi que no sirve para decir "la grafica no da".
    // Aqui: lo ocupada que va la grafica ENTERA (todos los procesos y colas), por NVML (nvml.dll, la del driver de NVIDIA: el % del tiempo en
    // que la grafica tiene algo en marcha, el mismo numero de nvidia-smi), leida cada 200 ms en un hilo aparte. La escalera solo baja algo si la
    // grafica va llena (escalera_grafica_llena, 90 %) en lo que mira; si no, es el ritmo o la fuente y no se toca nada (y se dice). Sin NVML
    // (otra grafica, sin el driver) se sabe -1 y la escalera hace lo de la 1.3.1.
    unsafe partial class Engine
    {
        [StructLayout(LayoutKind.Sequential)]
        struct NvmlUtil { public uint Gpu, Memoria; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct NvmlPci
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string BusIdViejo;
            public uint Dominio, Bus, Disp, PciDeviceId, PciSubSystemId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string BusId;
        }

        static class Nvml
        {
            [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] public static extern int Init();
            [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2")] public static extern int Cuantas(out uint n);
            [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] public static extern int Una(uint i, out IntPtr dev);
            [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")] public static extern int Uso(IntPtr dev, out NvmlUtil u);
            [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPciInfo_v3")] public static extern int Pci(IntPtr dev, out NvmlPci p);
        }

        const int GpuUsoN = 64;                                // 64 lecturas de 200 ms = 12,8 s
        readonly int[] gpuUsoV = new int[GpuUsoN];
        readonly long[] gpuUsoT = new long[GpuUsoN];
        int gpuUsoPos = -1;
        readonly object gpuUsoSync = new object();
        int gpuUsoEstado;                                      // 0 = sin empezar, 1 = en marcha, -1 = no hay (sin NVML / sin la grafica)
        IntPtr gpuUsoDev;

        // Desde EscCuarto (la primera vez): el hilo que lee el uso de la grafica
        void GpuUsoArranca()
        {
            if (gpuUsoEstado != 0) return;
            gpuUsoEstado = -1;
            if (!s.EscGpuUso) { Log.Write("Grafica entera: sin mirar lo ocupada que va (escalera_grafica_uso=0)"); return; }
            try
            {
                int r = Nvml.Init();
                if (r != 0) { Log.Write("Grafica entera: NVML no arranca (" + r + "): la escalera baja como en la 1.3.1 (sin saber si la grafica va llena)"); return; }
                uint n;
                if (Nvml.Cuantas(out n) != 0 || n == 0) { Log.Write("Grafica entera: NVML sin graficas: la escalera como en la 1.3.1"); return; }
                // la del motor: por su id PCI (fabricante y modelo de DXGI); si no casa ninguna, la primera
                uint ven = 0, dis = 0, sub = 0;
                try
                {
                    DXGI_ADAPTER_DESC1 ad;
                    IDXGIAdapter1 a = dxgiAdapter as IDXGIAdapter1;
                    if (a != null && a.GetDesc1(out ad) >= 0) { ven = ad.VendorId; dis = ad.DeviceId; sub = ad.SubSysId; }
                }
                catch { }
                IntPtr elegida = IntPtr.Zero, primera = IntPtr.Zero;
                string como = "la primera";
                for (uint i = 0; i < n && elegida == IntPtr.Zero; i++)
                {
                    IntPtr d;
                    if (Nvml.Una(i, out d) != 0) continue;
                    if (primera == IntPtr.Zero) primera = d;
                    if (ven == 0) continue;
                    try
                    {
                        NvmlPci p;
                        if (Nvml.Pci(d, out p) == 0 && p.PciDeviceId == ((dis << 16) | ven) && (sub == 0 || p.PciSubSystemId == sub)) { elegida = d; como = "por su id PCI " + ven.ToString("X4") + ":" + dis.ToString("X4"); }
                    }
                    catch (EntryPointNotFoundException) { }
                }
                if (elegida == IntPtr.Zero) elegida = primera;
                if (elegida == IntPtr.Zero) { Log.Write("Grafica entera: NVML no da la grafica: la escalera como en la 1.3.1"); return; }
                NvmlUtil u;
                if (Nvml.Uso(elegida, out u) != 0) { Log.Write("Grafica entera: NVML no da el uso: la escalera como en la 1.3.1"); return; }
                gpuUsoDev = elegida;
                gpuUsoEstado = 1;
                Thread t = new Thread(GpuUsoHilo) { IsBackground = true, Name = "uso de la grafica", Priority = ThreadPriority.BelowNormal };
                t.Start();
                Log.Write("Grafica entera: lo ocupada que va, por NVML cada 200 ms (" + n + " grafica(s), " + como + "; ahora al " + u.Gpu + " %): la escalera solo baja algo con la grafica llena (escalera_grafica_llena " + s.EscGpuLlena + " %)");
            }
            catch (DllNotFoundException) { Log.Write("Grafica entera: sin nvml.dll (no es NVIDIA o falta el driver): la escalera como en la 1.3.1"); }
            catch (Exception ex) { Log.Write("Grafica entera: NVML no va (" + ex.Message + "): la escalera como en la 1.3.1"); }
        }

        void GpuUsoHilo()
        {
            int fallos = 0;
            while (!Program.Quit)
            {
                Thread.Sleep(200);
                try
                {
                    NvmlUtil u;
                    if (Nvml.Uso(gpuUsoDev, out u) != 0) { if (++fallos > 50) { gpuUsoEstado = -1; Log.Write("Grafica entera: NVML deja de dar el uso: la escalera como en la 1.3.1"); return; } continue; }
                    fallos = 0;
                    lock (gpuUsoSync)
                    {
                        gpuUsoPos = (gpuUsoPos + 1) % GpuUsoN;
                        gpuUsoV[gpuUsoPos] = (int)Math.Min(100, u.Gpu);
                        gpuUsoT[gpuUsoPos] = Clock.Now;
                    }
                }
                catch { if (++fallos > 50) { gpuUsoEstado = -1; return; } }
            }
        }

        // Lo mas ocupada que ha ido la grafica entera en los ultimos ms (%); -1 = no se sabe
        int GpuUsoMax(double ms) { int m; double media; GpuUsoVentana(ms, out m, out media); return m; }

        void GpuUsoVentana(double ms, out int max, out double media)
        {
            max = -1; media = -1;
            if (gpuUsoEstado != 1) return;
            int n = 0; double suma = 0;
            lock (gpuUsoSync)
            {
                if (gpuUsoPos < 0) return;
                for (int i = 0; i < GpuUsoN; i++)
                {
                    int k = (gpuUsoPos - i + GpuUsoN) % GpuUsoN;
                    if (gpuUsoT[k] == 0 || Clock.SinceMs(gpuUsoT[k]) > ms) break;
                    if (gpuUsoV[k] > max) max = gpuUsoV[k];
                    suma += gpuUsoV[k]; n++;
                }
            }
            if (n < 3) { max = -1; return; }   // (pocas lecturas: no se sabe)
            media = suma / n;
        }

        string GpuUsoTxt(double ms)
        {
            int m; double media;
            GpuUsoVentana(ms, out m, out media);
            return m < 0 ? "?" : string.Format(CultureInfo.InvariantCulture, "{0} % como mucho, {1:0} % de media", m, media);
        }
    }
}
