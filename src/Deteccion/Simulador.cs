// Simulador.cs - PCs que no son este, para probar la deteccion y el plan sin tenerlos (ConsoleEnhancer)
//
// Dos clases de casos:
//  - "sin-nvidia" y "sin-capturadora": la deteccion DE VERDAD con NVAPI y las graficas de NVIDIA fuera, o sin buscar capturadoras
//    (Opciones). Es el camino real del codigo en un PC asi.
//  - el resto: lo detectado aqui con la grafica, la pantalla o la capturadora cambiadas por una inventada. Las funciones de la grafica
//    inventada salen del mismo DetectorGpu.Derivar que las de verdad (familia, driver, VRAM, HAGS...).
using System;
using System.Collections.Generic;

namespace ConsoleEnhancer.Deteccion
{
    public static class Simulador
    {
        public static readonly string[] Casos =
        {
            "sin-nvidia", "sin-capturadora", "rtx2060", "rtx3060", "rtx3050-portatil-4gb", "rtx4090", "rtx5080", "gtx1660",
            "amd-rx7800xt", "amd-rx9070xt", "amd-rx580", "intel-arc-b580", "intel-iris-xe", "uvc-ms2109", "elgato-hd60x",
            "pantalla-1080p60", "monitor-1440p240", "tele-119", "hags-apagada",
            // 20-11 (HDR / RTX HDR / VRR, HdrVrr.cs)
            "tele-hdr-gsync", "monitor-hdr400", "amd-hdr-freesync", "rtx2060-driver-viejo-hdr",
            // 22-11 (1.2.2, UNIVERSAL): mas graficas, pantallas, CPU y capturadoras del catalogo ("cap:<clave>", CatalogoCapturas.cs)
            "rtx4060", "rtx5070", "amd-rx6600", "intel-arc-a750", "amd-780m", "cpu-4hilos",
            "tele-4k60", "monitor-1080p144", "monitor-1440p144-vrr", "monitor-1080p60",
            "uvc-ms2130", "cap:elgato-4kx", "cap:elgato-hd60s+", "cap:elgato-camlink4k", "cap:avermedia-gc553", "cap:avermedia-gc311", "cap:j5create-jva04",
        };

        // (1.2.2) La matriz (DeteccionTest --simula=matriz): graficas x pantallas x capturadoras, con hardware inventado entero (sin detectar
        // nada: rapido) y la revision automatica de cada plan (Revisar)
        public static readonly string[] MatrizGpus = { "gtx1660", "rtx2060", "rtx3060", "rtx4060", "rtx4060ti", "rtx5070", "amd-rx6600", "amd-rx7800xt", "amd-rx9070xt", "intel-arc-a750", "intel-arc-b580", "intel-iris-xe", "amd-780m" };
        public static readonly string[] MatrizPantallas = { "tele-4k120-vrr", "tele-4k60", "monitor-1440p144-vrr", "monitor-1080p144", "monitor-1080p60" };

        public static OpcionesDeteccion Opciones(string caso, OpcionesDeteccion baseOp)
        {
            OpcionesDeteccion o = new OpcionesDeteccion();
            o.ProbarD3D12 = baseOp.ProbarD3D12; o.TopeMs = baseOp.TopeMs; o.RutasDlssNr = baseOp.RutasDlssNr; o.RutasDlssgSm86 = baseOp.RutasDlssgSm86;
            o.UsarMediaFoundation = baseOp.UsarMediaFoundation;
            if (caso == "sin-nvidia") o.UsarNvidia = false;
            if (caso == "sin-capturadora") o.BuscarCapturadoras = false;
            return o;
        }

        // Cambia lo detectado segun el caso (los dos primeros ya vienen de Opciones)
        public static void Aplicar(Hardware h, string caso, OpcionesDeteccion op)
        {
            h.Simulacion = caso;
            switch (caso)
            {
                case "rtx2060": Gpu(h, op, 0x10DE, 0x1F08, "NVIDIA GeForce RTX 2060", 6, 0x160, 59000, 2); break;
                case "rtx3060": Gpu(h, op, 0x10DE, 0x2504, "NVIDIA GeForce RTX 3060", 12, 0x170, 61692, 2); break;
                case "rtx3050-portatil-4gb": Gpu(h, op, 0x10DE, 0x25A2, "NVIDIA GeForce RTX 3050 Laptop GPU", 4, 0x170, 56614, 2); break;
                case "rtx4090": Gpu(h, op, 0x10DE, 0x2684, "NVIDIA GeForce RTX 4090", 24, 0x190, 61692, 2); break;
                case "rtx5080": Gpu(h, op, 0x10DE, 0x2C02, "NVIDIA GeForce RTX 5080", 16, 0x1B0, 61692, 2); break;
                case "gtx1660": Gpu(h, op, 0x10DE, 0x2184, "NVIDIA GeForce GTX 1660", 6, 0x160, 58000, 2); break;
                case "amd-rx7800xt": Gpu(h, op, 0x1002, 0x747E, "AMD Radeon RX 7800 XT", 16, 0, 0, 2); break;
                case "amd-rx9070xt": Gpu(h, op, 0x1002, 0x7550, "AMD Radeon RX 9070 XT", 16, 0, 0, 2); break;
                case "amd-rx6700xt": Gpu(h, op, 0x1002, 0x73DF, "AMD Radeon RX 6700 XT", 12, 0, 0, 2); break;
                case "amd-rx580": Gpu(h, op, 0x1002, 0x67DF, "Radeon RX 580 Series", 8, 0, 0, 2); break;
                case "intel-arc-b580": Gpu(h, op, 0x8086, 0xE20B, "Intel(R) Arc(TM) B580 Graphics", 12, 0, 0, 2); break;
                case "intel-iris-xe": Gpu(h, op, 0x8086, 0x9A49, "Intel(R) Iris(R) Xe Graphics", 0.125, 0, 0, 2); break;
                case "hags-apagada": foreach (GpuInfo g in h.Gpus) { g.Hags = 1; g.Simulada = true; g.F = new GpuFunciones(); DetectorGpu.Derivar(g, op); } break;
                case "uvc-ms2109": Captura(h, "USB Video", 0x534D, 0x2109, "USB", new[] { F(1920, 1080, 30, "MJPG"), F(1280, 720, 60, "MJPG"), F(1280, 720, 10, "YUY2"), F(640, 480, 30, "YUY2") }, "Digital Audio Interface (USB Digital Audio)"); break;
                case "elgato-hd60x": Captura(h, "Game Capture HD60 X", 0x0FD9, 0x0082, "USB", new[] { F(3840, 2160, 30, "NV12"), F(2560, 1440, 60, "NV12"), F(1920, 1080, 60, "NV12"), F(1920, 1080, 60, "YUY2") }, "Digital Audio Interface (HD60 X)"); break;
                case "pantalla-1080p60": Pantalla(h, "LG TV (simulada)", 1920, 1080, 60, 60, false, false, 0); break;
                case "monitor-1440p240": Pantalla(h, "Monitor 1440p (simulado)", 2560, 1440, 240, 240, true, true, 48); break;
                case "tele-119": foreach (PantallaInfo p in h.Pantallas) { if (p.Principal) { p.HzExacto = 119.88; p.Hz = 119; p.Simulada = true; } } break;
                // 20-11: tele 4K HDR 1000 nits con G-SYNC encendido en el driver / monitor HDR400 con G-SYNC apagado / AMD con FreeSync y HDR /
                // RTX 2060 con un driver sin RTX HDR
                case "tele-hdr-gsync": Hdr(Pantalla(h, "Tele 4K HDR (simulada)", 3840, 2160, 120, 120, true, true, 40), 1000, 3); break;
                case "monitor-hdr400": Hdr(Pantalla(h, "Monitor 1440p HDR400 (simulado)", 2560, 1440, 165, 165, true, true, 48), 400, 2); break;
                case "amd-hdr-freesync":
                    Gpu(h, op, 0x1002, 0x747E, "AMD Radeon RX 7800 XT", 16, 0, 0, 2);
                    { PantallaInfo p = Hdr(Pantalla(h, "Tele 4K HDR FreeSync (simulada)", 3840, 2160, 120, 120, true, false, 0), 800, -1); p.FreeSyncMin = 48; p.FreeSyncMax = 120; }
                    break;
                case "rtx2060-driver-viejo-hdr":
                    Gpu(h, op, 0x10DE, 0x1F08, "NVIDIA GeForce RTX 2060", 6, 0x160, 53141, 2);
                    Hdr(Pantalla(h, "Tele 4K HDR (simulada)", 3840, 2160, 60, 60, true, false, 0), 600, 0);
                    break;
                // 22-11 (1.2.2, UNIVERSAL)
                case "rtx4060": Gpu(h, op, 0x10DE, 0x2882, "NVIDIA GeForce RTX 4060", 8, 0x190, 61692, 2); break;
                case "rtx4060ti": Gpu(h, op, 0x10DE, 0x2803, "NVIDIA GeForce RTX 4060 Ti", 8, 0x190, 61088, 2); break;
                case "rtx5070": Gpu(h, op, 0x10DE, 0x2F04, "NVIDIA GeForce RTX 5070", 12, 0x1B0, 61692, 2); break;
                case "amd-rx6600": Gpu(h, op, 0x1002, 0x73FF, "AMD Radeon RX 6600", 8, 0, 0, 2); break;
                case "intel-arc-a750": Gpu(h, op, 0x8086, 0x56A1, "Intel(R) Arc(TM) A750 Graphics", 8, 0, 0, 2); break;
                case "amd-780m": Gpu(h, op, 0x1002, 0x15BF, "AMD Radeon 780M Graphics", 0.5, 0, 0, 2); break;
                case "cpu-4hilos": h.Sistema.Hilos = 4; h.Sistema.Nucleos = 4; break;
                case "tele-4k60": Pantalla(h, "Tele 4K 60 Hz (simulada)", 3840, 2160, 60, 60, false, false, 0); break;
                case "tele-4k120-vrr": Pantalla(h, "Tele 4K 120 Hz VRR (simulada)", 3840, 2160, 120, 120, true, true, 40); break;
                case "monitor-1080p144": Pantalla(h, "Monitor 1080p 144 Hz (simulado)", 1920, 1080, 144, 144, false, false, 0); break;
                case "monitor-1440p144-vrr": Pantalla(h, "Monitor 1440p 144 Hz VRR (simulado)", 2560, 1440, 144, 144, true, true, 48); break;
                case "monitor-1080p60": Pantalla(h, "Monitor 1080p 60 Hz (simulado)", 1920, 1080, 60, 60, false, false, 0); break;
                // MS2130 / MS2131 (USB 3.0, "USB3 Video"): YUY2 hasta 1080p60, MJPEG hasta 4K30 (lo que suelen listar)
                case "uvc-ms2130":
                    Captura(h, "USB3 Video", 0x345F, 0x2130, "USB", new[] { F(3840, 2160, 30, "MJPG"), F(2560, 1440, 30, "MJPG"), F(1920, 1080, 60, "MJPG"), F(1280, 720, 60, "MJPG"),
                        F(1920, 1080, 60, "YUY2"), F(1280, 720, 60, "YUY2"), F(1920, 1080, 60, "NV12") }, "Digital Audio Interface (USB3 Video)", true);
                    break;
                default:
                    if (caso.StartsWith("cap:")) CapturaCatalogo(h, caso.Substring(4));
                    break;
            }
        }

        // Una capturadora del catalogo "abierta": sus formatos previstos como si se hubieran leido (con 60,000 fijos, como muchas UVC)
        static void CapturaCatalogo(Hardware h, string clave)
        {
            ModeloCaptura m = DetectorCapturadoras.Catalogo.Find(delegate(ModeloCaptura x) { return x.Clave == clave; });
            if (m == null) return;
            List<FormatoCaptura> fl = new List<FormatoCaptura>();
            foreach (FormatoCaptura f in m.Previstos()) { f.Previsto = false; f.FpsMin = f.FpsMax; fl.Add(f); }
            string nombre = m.Patrones.Length > 0 ? m.Patrones[0] : m.Modelo;
            Captura(h, nombre, m.Vid != 0 ? m.Vid : 0x1234, m.Pid != 0 ? m.Pid : 0x0001, m.Bus.StartsWith("PCI") ? "PCI" : "USB", fl.ToArray(), "Digital Audio Interface (" + nombre + ")", false);
        }

        // ---------------------------------------------------------------- la matriz (1.2.2)
        public static Hardware Sintetico(OpcionesDeteccion op, string gpu, string pantalla, string captura, string extra)
        {
            Hardware h = new Hardware();
            h.Sistema.Windows = "Windows 11 (simulado)"; h.Sistema.Build = 26200; h.Sistema.Windows11 = true; h.Sistema.Hilos = 12; h.Sistema.Nucleos = 6;
            h.Simulacion = gpu + " + " + pantalla + " + " + captura + (extra.Length > 0 ? " + " + extra : "");
            Aplicar(h, pantalla, op);
            foreach (PantallaInfo p in h.Pantallas) p.GpuLuid = 0x1234;
            Aplicar(h, gpu, op);
            Aplicar(h, captura, op);
            if (extra.Length > 0) Aplicar(h, extra, op);
            h.Simulacion = gpu + " + " + pantalla + " + " + captura + (extra.Length > 0 ? " + " + extra : "");
            return h;
        }

        // Lo que estaria mal en un plan (vacio = bien): lo que se pidio al agente UNIVERSAL (fluido en cualquier PC, con margen)
        public static List<string> Revisar(Hardware h, PlanImagen p)
        {
            List<string> r = new List<string>();
            CapturadoraInfo c = p.Captura;
            GpuInfo g = p.Gpu;
            string f = (p.CapFormato ?? "").ToUpperInvariant();
            if (c != null && p.Motor == "propio" && f.Length > 0 && f != "AUTO" && !CapturadoraInfo.MotorLoAbre(f)) r.Add("motor propio con captura " + f + " (no la abre)");
            List<FormatoCaptura> fl = c != null ? c.FormatosParaElegir() : null;
            if (fl != null && fl.Count > 0)
            {
                FormatoCaptura elegido = fl.Find(delegate(FormatoCaptura x) { return x.Ancho == p.CapW && x.Alto == p.CapH && x.Subtipo.ToUpperInvariant() == f; });
                if (elegido == null) r.Add("captura " + p.CapW + "x" + p.CapH + " " + f + " que la capturadora no lista");
                else
                {
                    double fps = c.FpsDe(elegido);
                    bool hay60 = fl.Exists(delegate(FormatoCaptura x) { return !x.Comprimido && x.Alto >= 720 && c.FpsDe(x) >= 59; });
                    if (fps < 59 && hay60) r.Add("captura a " + Tx.N(fps, "0.#") + " fps habiendo 60 sin comprimir");
                    if (elegido.Comprimido && elegido.Alto > Planificador.TopeComprimido(h)) r.Add("MJPEG a " + elegido.Alto + "p con " + h.Sistema.Hilos + " hilos");
                    if (!elegido.Previsto && p.CapFps == "60000/1001" && elegido.FpsMin > 59.95 && Math.Abs(elegido.FpsMax - 60) < 0.05) r.Add("pide 59,94 a una capturadora que solo lista 60 (mpv no abre)");
                    if (elegido.Comprimido && fl.Exists(delegate(FormatoCaptura x) { return !x.Comprimido && x.Alto >= 720 && c.FpsDe(x) >= Math.Min(59, fps); })) r.Add("comprimida habiendo sin comprimir");
                }
                if (p.HomeNativo && !fl.Exists(delegate(FormatoCaptura x) { return !x.Comprimido && x.Alto >= 2160 && c.FpsDe(x) >= 59; })) r.Add("HOME a 4K nativo sin 4K60 sin comprimir");
            }
            if (p.CapH > p.OutH && p.OutH > 0) r.Add("captura (" + p.CapH + "p) mayor que la pantalla (" + p.OutH + "p)");
            // lo de serie, como mucho al 85 %; lo que solo se ofrece (generacion apagada de serie, en el selector), como mucho al 100 %
            double limite = Planificador.UsoMotor;
            foreach (KeyValuePair<string, ModoPlan> kv in p.Modos)
            {
                double periodo = (kv.Key.StartsWith("60") ? 1000.0 : 2000.0) / 59.94;
                bool fg = kv.Key.EndsWith("_fg");
                string gen = kv.Key.StartsWith("60") ? p.Gen60 : p.Gen30;
                if (fg && gen == "ninguna") continue;
                bool deSerie = !fg || p.GenDeSerie;
                double tope = deSerie ? limite : 1.0;
                if (kv.Value.CosteMs > tope * periodo)
                    r.Add("modo " + kv.Key + (deSerie ? "" : " (ofrecido)") + " al " + Math.Round(100 * kv.Value.CosteMs / periodo) + " % (" + (kv.Value.Modelo.Length > 0 ? kv.Value.Modelo : p.Escalador) + ")" +
                          (!fg && p.Avisos.Exists(delegate(string a) { return a.StartsWith(Tx.T("Esta gráfica no llega", "This GPU can't")); }) ? " [avisado: la grafica no da para esta salida]" : ""));
            }
            if (p.Captura != null && p.Captura.Formatos != null)
            {
                string fr;
                string c120 = p.Capturas120(out fr);
                if (c120.Length > 0 && p.Tope120Alto < 1440 && c120.Contains("2560x1440")) r.Add("120 a 1440p sin caber");
            }
            if (g != null && !g.Integrada && p.VramTotalMb > 256 && p.VramPrevistaMb > p.VramTotalMb * limite) r.Add("VRAM " + Math.Round(p.VramPrevistaMb) + " de " + Math.Round(p.VramTotalMb) + " MB");
            return r;
        }

        static void Gpu(Hardware h, OpcionesDeteccion op, int ven, int dev, string nombre, double vramGb, int nvArq, int drvNv, int hags)
        {
            long luid = 0;
            foreach (PantallaInfo p in h.Pantallas) if (p.Principal) luid = p.GpuLuid;
            GpuInfo g = new GpuInfo();
            g.Simulada = true;
            g.Nombre = nombre; g.VendorId = ven; g.DeviceId = dev; g.Luid = luid;
            g.Fab = ven == 0x10DE ? Fabricante.Nvidia : ven == 0x1002 ? Fabricante.Amd : ven == 0x8086 ? Fabricante.Intel : Fabricante.Otro;
            g.Vram = (ulong)(vramGb * 1024 * 1024 * 1024);
            g.NvArq = nvArq; g.Nvapi = nvArq > 0; g.DriverNv = drvNv;
            g.Driver = drvNv > 0 ? Tx.N(drvNv / 100.0, "0.00") : ven == 0x1002 ? "AMD Software 25.10.1" : "32.0.101.6130";
            g.Hags = hags; g.Wddm = 3200; g.NivelD3D12 = 0xc100; g.ShaderModel = ven == 0x1002 && dev == 0x67DF ? 0x60 : 0x66;
            g.Salidas = 1; g.Integrada = vramGb < 1;
            g.SmoothAjuste = nvArq >= 0x190 && drvNv >= 59000 ? 1 : -1;
            DetectorGpu.Derivar(g, op);
            h.Gpus.Clear();
            h.Gpus.Add(g);
        }

        static FormatoCaptura F(int w, int hh, double fps, string sub)
        {
            FormatoCaptura f = new FormatoCaptura(); f.Ancho = w; f.Alto = hh; f.FpsMax = fps; f.FpsMin = 5; f.Subtipo = sub; f.Comprimido = sub == "MJPG";
            return f;
        }

        static void Captura(Hardware h, string nombre, int vid, int pid, string bus, FormatoCaptura[] fmts, string audio) { Captura(h, nombre, vid, pid, bus, fmts, audio, false); }

        // fijos: los fps de cada formato sin rango (60,000 exactos, como muchas UVC: ffmpeg no abre 59,94 ahi)
        static void Captura(Hardware h, string nombre, int vid, int pid, string bus, FormatoCaptura[] fmts, string audio, bool fijos)
        {
            if (fijos) foreach (FormatoCaptura f in fmts) f.FpsMin = f.FpsMax;
            CapturadoraInfo c = new CapturadoraInfo();
            c.Simulada = true;
            c.Nombre = nombre; c.Vid = vid; c.Pid = pid; c.Bus = bus; c.EnMediaFoundation = true;
            c.DevicePath = @"\\?\usb#vid_" + vid.ToString("x4") + "&pid_" + pid.ToString("x4") + "&mi_00#simulada#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\\global";
            c.Instancia = "USB\\VID_" + vid.ToString("X4") + "&PID_" + pid.ToString("X4") + "&MI_00\\SIMULADA";
            c.Cadena = new List<string> { c.Instancia, "USB\\VID_" + vid.ToString("X4") + "&PID_" + pid.ToString("X4") + "\\SIMULADA" };
            c.Formatos = new List<FormatoCaptura>(fmts);
            DetectorCapturadoras.Clasificar(c);
            AudioEntrada a = new AudioEntrada();
            a.Nombre = audio; a.Origen = "wasapi"; a.Id = "{0.0.1.00000000}.{simulada}";
            a.Cadena = new List<string> { @"SWD\MMDEVAPI\SIMULADA", "USB\\VID_" + vid.ToString("X4") + "&PID_" + pid.ToString("X4") + "&MI_02\\SIMULADA", c.Cadena[1] };
            h.Audios.Add(a);
            DetectorCapturadoras.EmparejarAudio(c, h.Audios);
            h.Capturadoras.Clear();
            h.Capturadoras.Add(c);
        }

        static PantallaInfo Hdr(PantallaInfo p, double nits, int vrrNvidia)
        {
            p.NitsMax = nits; p.NitsMin = 0.05; p.Hdr10Edid = true; p.VrrNvidia = vrrNvidia; p.Tearing = true;
            return p;
        }

        static PantallaInfo Pantalla(Hardware h, string nombre, int w, int hh, int hz, int hzMax, bool hdr, bool vrr, int vrrMin)
        {
            PantallaInfo p = null;
            foreach (PantallaInfo x in h.Pantallas) if (x.Principal) p = x;
            if (p == null) { p = new PantallaInfo(); p.Principal = true; p.Gdi = @"\\.\DISPLAY1"; h.Pantallas.Add(p); }
            p.Simulada = true;
            p.Nombre = nombre; p.Ancho = p.AnchoMax = p.AnchoNativo = w; p.Alto = p.AltoMax = p.AltoNativo = hh;
            p.Hz = hz; p.HzExacto = hz; p.HzEnMax = p.HzMax = hzMax;
            p.HzDeLaMax = new List<int> { 60, hzMax };
            p.HdrPosible = hdr; p.HdrActivo = false;
            p.VrrMin = vrr ? vrrMin : 0; p.VrrMax = vrr ? hzMax : 0; p.FreeSyncMin = 0; p.FreeSyncMax = 0;
            p.VrrNvidia = vrr ? 2 : 0;
            p.Conexion = "HDMI";
            return p;
        }
    }
}
