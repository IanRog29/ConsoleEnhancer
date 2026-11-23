// Plan.cs - con lo detectado, la cadena de imagen y los ajustes de serie ("el plan") (ConsoleEnhancer)
//
// Regla general (ver DISENO.md):
//   NVIDIA RTX 40 / 50: motor propio (Streamline) DLSS SR + Reflex + Smooth Motion x2 (XeSS FG x2 con la tele a 119,88 Hz o un driver sin
//                      Smooth Motion); XeSS FG / FSR 3 FG x2 para elegir. (1.2.4 ronda 6: sin DLSS-G ni x3 / x4)
//   NVIDIA RTX 20/30: DLSS SR + Reflex, sin generacion de serie (XeSS FG / FSR 3 FG para elegir)
//   GTX / AMD / Intel: FSR 1 (EASU + RCAS) + CAS en shader propio; generacion: AFMF del driver (AMD RX 6000+), si no FSR 3 FG o XeSS FG
//                      (apagadas de serie); Reflex solo NVIDIA (GTX tambien)
//   Sin D3D12 / grafica por software: mpv solo, sin escalado ni generacion
//   DLSS 5 (DLSSNR de Magpie): solo si el usuario tiene la DLL (no oficial) y la grafica es RTX; apagado de serie
// El modelo de DLSS de cada modo sale de una estimacion de coste (lo medido en la RTX 4060 de este PC escalado por el indice de la grafica)
// hasta que el banco corto del primer arranque lo mida de verdad (Calibracion).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleEnhancer.Deteccion
{
    public sealed class PreferenciasPlan
    {
        public string Pantalla;          // nombre GDI (\\.\DISPLAY1); null = la principal
        public string Capturadora;       // DevicePath o nombre; null = la de mas puntos
        public string Consola = "";      // claves de Consolas.cs: switch2, switch1, ps5, ps4, xboxseries, xboxone, otra ("" = no se sabe)
        public bool PreferirLatencia;    // menos latencia antes que mas imagenes (no usa la generacion por Magpie)
        public Calibracion Calibracion;  // banco del primer arranque (null = estimacion)
        // (20-11, HdrVrr.cs) lo que sabe el lanzador y no la deteccion: G-SYNC visto funcionando en esta pantalla otra vez (data\vrr_seen.txt) y
        // VRRREQUESTSTATE del perfil global de NVIDIA (-1 no se sabe)
        public bool VrrVisto;
        public int GsyncGlobal = -1;
    }

    // Resultados del banco corto: ms de GPU por imagen real de cada pieza, para ESTA grafica, driver, captura y salida
    public sealed class Calibracion
    {
        public string GpuClave = "", Fecha = "";
        public Dictionary<string, double> Ms = new Dictionary<string, double>();   // dlss_L, dlss_M, dlss_K, dlss_E, fsr1, mv, base, fg_smooth

        public static Calibracion Leer(string fichero)
        {
            if (!File.Exists(fichero)) return null;
            Calibracion c = new Calibracion();
            foreach (string raw in File.ReadAllLines(fichero))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                int eq = l.IndexOf('=');
                if (eq <= 0) continue;
                string k = l.Substring(0, eq).Trim(), v = l.Substring(eq + 1).Trim();
                double d;
                if (k == "gpu") c.GpuClave = v;
                else if (k == "fecha") c.Fecha = v;
                else if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) c.Ms[k] = d;
            }
            return c;
        }

        public void Escribir(string fichero)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Banco corto de ConsoleEnhancer: ms de GPU por imagen real (se rehace si cambia la grafica, el driver, la captura o la salida)");
            sb.AppendLine("gpu=" + GpuClave);
            sb.AppendLine("fecha=" + Fecha);
            foreach (KeyValuePair<string, double> kv in Ms) sb.AppendLine(kv.Key + "=" + kv.Value.ToString("0.###", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fichero)));
            File.WriteAllText(fichero, sb.ToString(), new UTF8Encoding(false));
        }
    }

    public sealed class ModoPlan
    {
        public string Clave = "";              // 60, 60_fg, 30, 30_fg
        public string Modelo = "";             // L, M, K, E (DLSS) o "" (FSR 1 / sin escalado)
        public double CosteMs;                 // estimado (o medido) por imagen real
        public double TopeMs;                  // lo que deja el modo (75 % del tiempo de una imagen)
        public bool Justo;                     // ni el mas ligero cabe con margen
    }

    public sealed partial class PlanImagen   // (20-11: HDR / RTX HDR / VRR en HdrVrr.cs)
    {
        public GpuInfo Gpu;
        public PantallaInfo Pantalla;
        public CapturadoraInfo Captura;
        public AudioEntrada Audio;
        public string Consola = "";
        public string Motor = "propio";        // propio (motor D3D12), mpv (solo mpv), ninguno
        public bool Streamline;                // DLSS / Reflex de NVIDIA (sin NVIDIA el motor arranca sin Streamline)
        public string Escalador = "";          // dlss, fsr1, ninguno
        public string EscaladorAlternativo = "";
        public double Enfoque = 0.3;           // CAS
        public string Gen60 = "ninguna", Gen30 = "ninguna";   // smooth, afmf, fsr3fg, xessfg, ninguna (1.2.4 ronda 6: sin dlssg)
        public bool GenDeSerie;                // la generacion encendida al empezar (boton de captura: si/no)
        public string GenAlternativa = "";     // la que se puede elegir ademas (p. ej. FSR 3 FG)
        // 19-11 (agente CE, el selector de L3 de Prisma): los tipos que se ofrecen en cada modo (ini generacion_lista_60 / _30), los que
        // tiene esta grafica: smooth (Smooth Motion), xess (XeSS FG), fsr3 (FSR 3 FG)
        public List<string> Lista60 = new List<string>(), Lista30 = new List<string>();
        public string Latencia = "";           // reflex, antilag, ninguna
        public int CapW, CapH;
        public string CapFps = "60000/1001", CapFormato = "auto", CapOrigen = "";
        public int OutW, OutH; public double OutHz;
        public double HzPlan;                  // los Hz con los que se planea la generacion: los de ahora o, si la pantalla admite 100 o mas y esta mas baja, esos (con aviso)
        public bool UsarVrr;
        public bool Dlss5Disponible; public int Dlss5PasadasMax; public string Dlss5Nota = "";
        public bool MotorEnEspera;
        public bool HomeNativo;
        public bool Calibrado;
        public Dictionary<string, ModoPlan> Modos = new Dictionary<string, ModoPlan>();
        public List<string> Avisos = new List<string>();
        public List<string> Motivos = new List<string>();

        static string Nom(string v) { return v == "propio" ? Tx.T("propio", "own engine") : v == "ninguna" ? Tx.T("ninguna", "none") : v == "ninguno" ? Tx.T("ninguno", "none") : v; }

        public string Informe()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("== Plan ==");
            sb.AppendLine("  " + Tx.T("Gráfica: ", "GPU: ") + (Gpu != null ? Gpu.Nombre + " (" + Gpu.Familia + ")" : Tx.T("ninguna útil", "none usable")) +
                          Tx.T(" | Pantalla: ", " | Display: ") + (Pantalla != null ? Pantalla.Nombre + " " + OutW + "x" + OutH + " @ " + Tx.N(OutHz, "0.###") + " Hz" : "?"));
            sb.AppendLine("  " + Tx.T("Capturadora: ", "Capture: ") + (Captura != null ? Captura.Nombre + " -> " + CapW + "x" + CapH + " " + CapFormato + " " + CapFps + " (" + CapOrigen + ")" : Tx.T("ninguna", "none")) +
                          Tx.T(" | Audio: ", " | Audio: ") + (Audio != null ? Audio.Nombre + " [" + Audio.Origen + "]" : Tx.T("a elegir", "to choose")) + (Consola.Length > 0 ? Tx.T(" | Consola: ", " | Console: ") + Consola : ""));
            sb.AppendLine("  " + Tx.T("Motor: ", "Engine: ") + Nom(Motor) + (Motor == "propio" ? (Streamline ? Tx.T(" con Streamline", " with Streamline") : Tx.T(" sin Streamline", " without Streamline")) : "") +
                          Tx.T(" | Escalado: ", " | Scaling: ") + Escalador + (EscaladorAlternativo.Length > 0 ? " (R3: " + EscaladorAlternativo + ")" : "") + " + CAS " + Tx.N(Enfoque, "0.0#") +
                          Tx.T(" | Latencia: ", " | Latency: ") + Latencia);
            sb.AppendLine("  " + Tx.T("Generación: modo 60 ", "Frame gen: 60 mode ") + Nom(Gen60) + Tx.T(", modo 30 ", ", 30 mode ") + Nom(Gen30) +
                          Tx.T(" | de serie ", " | default ") + (GenDeSerie ? Tx.T("encendida", "on") : Tx.T("apagada", "off")) + (GenAlternativa.Length > 0 ? Tx.T(" | también: ", " | also: ") + GenAlternativa : ""));
            sb.AppendLine("  " + Tx.T("Selector de generación (L3): modo 60 ", "Frame gen picker (L3): 60 mode ") + (Lista60.Count > 0 ? string.Join(", ", Lista60.ToArray()) : Tx.T("ninguna", "none")) +
                          Tx.T(", modo 30 ", ", 30 mode ") + (Lista30.Count > 0 ? string.Join(", ", Lista30.ToArray()) : Tx.T("ninguna", "none")));
            List<string> ms = new List<string>();
            foreach (string k in new[] { "60", "60_fg", "30", "30_fg" })
            {
                ModoPlan m;
                if (!Modos.TryGetValue(k, out m)) continue;
                ms.Add(k + " " + (m.Modelo.Length > 0 ? m.Modelo : Escalador) + " " + Tx.N(m.CosteMs, "0.0") + "/" + Tx.N(m.TopeMs, "0.0") + " ms" + (m.Justo ? "!" : ""));
            }
            if (ms.Count > 0) sb.AppendLine("  " + (Calibrado ? Tx.T("Modelos (medido): ", "Presets (measured): ") : Tx.T("Modelos (estimado; el motor los ajusta jugando): ", "Presets (estimated; the engine adjusts them while playing): ")) + string.Join(" | ", ms.ToArray()));
            sb.AppendLine("  DLSS 5: " + (Dlss5Disponible ? Tx.T("disponible (apagado de serie, hasta ", "available (off by default, up to ") + Dlss5PasadasMax + Tx.T(" pasadas)", " passes)") : "no") + (Dlss5Nota.Length > 0 ? " - " + Dlss5Nota : "") +
                          Tx.T(" | Motor en espera: ", " | Engine standby: ") + Tx.SiNo(MotorEnEspera) + Tx.T(" | HOME a 4K nativo: ", " | native 4K HOME: ") + Tx.SiNo(HomeNativo) + " | VRR: " + Tx.SiNo(UsarVrr));
            sb.Append(InformeHdrVrr());   // (20-11, HdrVrr.cs)
            sb.Append(InformeMargenes()); // (1.2.2, PlanMargen.cs)
            foreach (string m in Motivos) sb.AppendLine("  - " + m);
            foreach (string a in Avisos) sb.AppendLine("  ! " + a);
            return sb.ToString();
        }

        // (fusion I) Capturas a 120 de los formatos leidos de la capturadora: "1920x1080,2560x1440" o "" (no se sabe o no hay)
        public string Capturas120(out string framerate)
        {
            framerate = "";
            if (Captura == null || Captura.Formatos == null || Captura.Formatos.Count == 0) return "";
            List<string> r = new List<string>();
            double mejor = 0;
            // (1.2.2, UNIVERSAL) solo hasta la captura que la grafica aguanta a 120 con margen (Tope120Alto, PlanMargen.cs) y con los fps de
            // verdad de esa capturadora (el catalogo sabe los modos que lista y no da)
            foreach (string t in new[] { "1280x720", "1920x1080", "2560x1440" })
                foreach (FormatoCaptura f in Captura.Formatos)
                    if (!f.Comprimido && f.Subtipo != "P010" && Captura.FpsDe(f) >= 119 && f.Alto <= Tope120Alto && (f.Ancho + "x" + f.Alto) == t)
                    {
                        if (!r.Contains(t)) r.Add(t);
                        if (mejor == 0 || Math.Abs(f.FpsMax - 120) < Math.Abs(mejor - 120)) mejor = f.FpsMax;
                    }
            if (r.Count > 0) framerate = mejor >= 119.5 ? "120" : "120000/1001";
            return string.Join(",", r.ToArray());
        }

        // Los ajustes de serie para el ini de ConsoleEnhancer (mismos nombres que switch2.ini donde existen)
        public string ComoIni()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Plan de serie elegido por la deteccion (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
            if (Captura != null)
            {
                sb.AppendLine("device=" + Captura.Nombre);
                sb.AppendLine("device_path=" + Captura.DevicePath);
                sb.AppendLine("video_size=" + CapW + "x" + CapH);
                sb.AppendLine("framerate=" + CapFps);
                sb.AppendLine("pixel_format=" + CapFormato);
                // (fusion I, modo 120 de Prisma HZ120) las capturas que ESTA capturadora lista a 120 (sin comprimir, de las que usa la app:
                // 720p / 1080p / 1440p; a 4K el boton sigue 30 <-> 60). Sin formatos leidos (asistente sin abrirla): vacio = sin modo 120
                string fr120;
                sb.AppendLine("# Modo 120 fps (esports): solo con estas capturas, la tele a 120 Hz o mas y una consola que saque 120 (permitir_120=auto)");
                sb.AppendLine("capturas_120=" + Capturas120(out fr120));
                if (fr120.Length > 0) sb.AppendLine("framerate_120=" + fr120);
            }
            if (Audio != null) { sb.AppendLine("audio_dispositivo=" + Audio.Nombre); sb.AppendLine("audio_origen=" + Audio.Origen); }
            sb.AppendLine("consola=" + Consola);
            sb.AppendLine("motor=" + Motor);
            sb.AppendLine("motor_streamline=" + (Streamline ? 1 : 0));
            sb.AppendLine("escalador=" + Escalador);
            if (EscaladorAlternativo.Length > 0) sb.AppendLine("escalador_alternativo=" + EscaladorAlternativo);
            foreach (KeyValuePair<string, ModoPlan> kv in Modos) if (kv.Value.Modelo.Length > 0) sb.AppendLine("motor_modelo_" + kv.Key + "=" + kv.Value.Modelo);
            sb.AppendLine("generacion_plan_60=" + Gen60);
            sb.AppendLine("generacion_plan_30=" + Gen30);
            sb.AppendLine("generacion_de_serie=" + (GenDeSerie ? 1 : 0));
            sb.AppendLine("motor_reflex=" + (Latencia == "reflex" ? 1 : 0));
            sb.AppendLine("upscale_sharpen=" + Enfoque.ToString("0.0#", CultureInfo.InvariantCulture));
            sb.AppendLine("salida=" + OutW + "x" + OutH);
            sb.AppendLine("motor_en_espera=" + (MotorEnEspera ? 1 : 0));
            sb.AppendLine("home_nativo=" + (HomeNativo ? 1 : 0));
            sb.AppendLine("dlss5_disponible=" + (Dlss5Disponible ? 1 : 0));
            sb.AppendLine("vrr=" + (UsarVrr ? 1 : 0));
            sb.AppendLine("rtx_hdr_posible=" + (RtxHdrPosible ? 1 : 0));
            return sb.ToString();
        }
    }

    public static partial class Planificador   // (1.2.2, UNIVERSAL: partial; los margenes de grafica, VRAM y CPU en PlanMargen.cs)
    {
        // Lo medido en la RTX 4060 (indice 1,0) con salida 4K (bancos del motor, 15/17-11): ms por imagen real
        const double RefDlssE = 4.8, RefDlssK = 6.2, RefDlssM = 10.1, RefDlssL = 12.1, RefMv1440 = 1.3, RefBase = 1.0, RefFsr1 = 0.8;
        // (1.2.2: la generacion ya no sale de aqui, eran 2,0 / 1,0 / 2,5 "estimados": PlanMargen.CosteGeneracion, con lo medido)
        const double Margen = 0.75;                                           // la captura de verdad cuesta 7-15 puntos mas que la de prueba

        // Consolas: minimo provisional con las claves de src\Consolas.cs del agente CONSOLAS (consola=). En la fase 2 se lee su ConsolaPerfil
        // (SalidaMaxAlto, Da120, HomeNativo4K...) en lugar de esto. Alto maximo de salida, 120 Hz, tramado de 5 bits
        static readonly object[][] Consolas =
        {
            new object[] { "switch2", "Nintendo Switch 2", 2160, true, true },
            new object[] { "switch1", "Nintendo Switch", 1080, false, false },
            new object[] { "ps5", "PlayStation 5", 2160, true, false },
            new object[] { "ps4", "PlayStation 4", 1080, false, false },
            new object[] { "xboxseries", "Xbox Series X|S", 2160, true, false },
            new object[] { "xboxone", "Xbox One", 1080, false, false },
            new object[] { "otra", "Otra", 2160, false, false },
        };

        public static PlanImagen Elegir(Hardware hw, PreferenciasPlan pref)
        {
            if (pref == null) pref = new PreferenciasPlan();
            PlanImagen p = new PlanImagen();
            p.Consola = pref.Consola ?? "";
            object[] cons = null;
            foreach (object[] c in Consolas) if ((string)c[0] == p.Consola) cons = c;
            int consMaxH = cons != null ? (int)cons[2] : 2160;

            // ---- pantalla
            p.Pantalla = hw.Pantallas.Find(delegate(PantallaInfo x) { return pref.Pantalla != null && string.Equals(x.Gdi, pref.Pantalla, StringComparison.OrdinalIgnoreCase); });
            if (p.Pantalla == null) p.Pantalla = hw.Pantallas.Find(delegate(PantallaInfo x) { return x.Principal; });
            if (p.Pantalla == null && hw.Pantallas.Count > 0) p.Pantalla = hw.Pantallas[0];
            if (p.Pantalla != null)
            {
                p.OutW = p.Pantalla.Ancho; p.OutH = p.Pantalla.Alto; p.OutHz = p.Pantalla.HzExacto > 0 ? p.Pantalla.HzExacto : p.Pantalla.Hz;
                int hzPosible = p.Pantalla.AnchoMax == p.OutW && p.Pantalla.AltoMax == p.OutH ? p.Pantalla.HzEnMax : p.Pantalla.HzMax;
                p.HzPlan = hzPosible >= 100 && p.OutHz < 100 ? hzPosible : p.OutHz;
                if (hzPosible >= 100 && p.OutHz < 100)
                    p.Avisos.Add(Tx.T("La pantalla admite ", "The display supports ") + hzPosible + Tx.T(" Hz y está a ", " Hz but runs at ") + Tx.N(p.OutHz, "0.##") + Tx.T(" Hz: súbela (Configuración > Pantalla > Pantalla avanzada) para la generación de imágenes", " Hz: raise it for frame generation"));
                if (p.Pantalla.AnchoMax > p.OutW && p.Pantalla.AltoMax > p.OutH)
                    p.Avisos.Add(Tx.T("La pantalla está a ", "Display set to ") + p.OutW + "x" + p.OutH + Tx.T(" y admite ", ", supports ") + p.Pantalla.AnchoMax + "x" + p.Pantalla.AltoMax + Tx.T(": el motor pinta a la de ahora", ": the engine renders at the current one"));
            }
            else { p.OutW = 1920; p.OutH = 1080; p.OutHz = 60; p.Avisos.Add(Tx.T("No se ve ninguna pantalla: se supone 1080p 60", "No display found: assuming 1080p60")); }

            // ---- grafica
            p.Gpu = hw.GpuDe(p.Pantalla);
            GpuInfo g = p.Gpu;
            double gb = g != null ? g.VramGb : 0;
            if (g == null || g.Software || !g.F.Motor.Si)
            {
                p.Motor = "ninguno";   // (23-11, 1.2.4 SINMPV: antes "mpv"; sin mpv no hay camino sin el motor)
                p.Escalador = "ninguno";
                p.Latencia = "ninguna";
                p.Avisos.Add(g == null || g.Software ? Tx.T("Sin gráfica útil (solo la de software de Windows): instala el driver de tu gráfica (el motor de imagen necesita Direct3D 12)", "No usable GPU (only Windows' software one): install your GPU driver (the image engine needs Direct3D 12)")
                                                     : Tx.T("La gráfica no tiene Direct3D 12 nivel 12_0: el motor de imagen no puede arrancar (desde la 1.3 no hay otro camino)", "The GPU lacks Direct3D 12 feature level 12_0: the image engine cannot start (since 1.3 there is no other path)"));
            }
            else
            {
                p.Motor = "propio";
                p.Streamline = g.Fab == Fabricante.Nvidia && (g.F.DlssSr.Si || g.F.Reflex.Si);
                p.Latencia = g.F.Reflex.Si ? "reflex" : g.F.AntiLag.Si ? "antilag" : "ninguna";
                if (g.Fab != Fabricante.Nvidia) p.Motivos.Add(Tx.T("Sin NVIDIA: el motor arranca sin Streamline (ni DLSS ni Reflex)", "No NVIDIA: engine without Streamline"));
                if (p.Latencia == "antilag") p.Motivos.Add(Tx.T("Latencia: Radeon Anti-Lag del driver (actívalo en AMD Software para el exe del motor)", "Latency: enable Radeon Anti-Lag in AMD Software"));
                if (p.Latencia == "ninguna") p.Motivos.Add(Tx.T("Latencia: sin Reflex; el motor va con 1 imagen en cola (swapchain con espera)", "Latency: no Reflex; 1-frame queue"));
            }

            // ---- capturadora y formato
            ElegirCaptura(hw, pref, p, consMaxH);
            // (1.2.2, UNIVERSAL) una captura que el motor no abre (MJPEG, H.264...): mpv + Magpie, con el escalado y la generacion de la grafica
            bool graficaUtil = p.Motor == "propio";
            MotorSegunCaptura(p);

            // ---- escalado y modelos
            if (graficaUtil)
            {
                bool up = p.CapH > 0 && p.CapH < p.OutH;
                if (!up) { p.Escalador = "ninguno"; p.Motivos.Add(Tx.T("La captura ya es de la resolución de la pantalla: sin escalado (bordes, enfoque y generación sí)", "Capture matches display: no scaling")); }
                else if (g.F.DlssSr.Si) { p.Escalador = "dlss"; p.EscaladorAlternativo = "fsr1"; }
                // 21-11 (1.0.9; el usuario: "si la gpu es gtx o amd que sea xess el rescalado" / "y fsr 4?"): sin DLSS, en AMD RX 9000 FSR 4
                // (IA, solo RDNA 4) y en el resto con XeSS (DP4a: GTX, AMD, Intel Arc) XeSS; los dos por Magpie (el motor propio no los tiene).
                // En graficas integradas (Intel Xe) XeSS va lento: FSR 1 como antes
                else if (g.F.Fsr4.Si)
                {
                    p.Escalador = "fsr4"; p.EscaladorAlternativo = g.F.XessSr.Si ? "xess" : "fsr1";
                    p.Motivos.Add(Tx.T("Escalado: FSR 4 (AMD RX 9000, motor propio)", "Scaling: FSR 4 (AMD RX 9000, own engine)"));
                }
                else if (g.F.XessSr.Si && g.Familia != FamiliaGpu.IntelXe && g.Familia != FamiliaGpu.IntelAntigua && g.Familia != FamiliaGpu.Software)
                {
                    p.Escalador = "xess"; p.EscaladorAlternativo = "fsr1";
                    p.Motivos.Add(Tx.T("Escalado: XeSS (sin DLSS en esta gráfica, motor propio)", "Scaling: XeSS (no DLSS on this GPU, own engine)"));
                }
                else { p.Escalador = "fsr1"; p.EscaladorAlternativo = "catrom"; }
                p.Enfoque = p.CapH <= 720 ? 0.6 : p.CapH <= 1080 ? 0.5 : 0.3;
                ElegirGeneracion(p, g, pref);
                ElegirModelos(p, g, pref.Calibracion);
                // ni lo mas ligero cabe en el modo 60 con captura de mas de 1080p: a 1080p (y se vuelve a calcular)
                ModoPlan m60;
                if (p.Escalador != "ninguno" && p.Captura != null && p.CapH > 1080 && p.Modos.TryGetValue("60", out m60) && m60.Justo)
                {
                    int antes = p.CapH;
                    p.CapRebajadaDe = antes;   // (1.2.2, UNIVERSAL: si luego queda FSR 1, PlanMargen.AjustarMargenes la vuelve a subir)
                    p.CapW = 1920; p.CapH = 1080;
                    FormatoCaptura f = p.Captura.FormatosParaElegir() != null ? p.Captura.Mejor(1080, 59, TopeComprimido(hw)) : null;
                    if (f != null) { p.CapW = f.Ancho; p.CapH = f.Alto; p.CapFormato = f.Subtipo; p.CapFps = FpsPedido(f, 59.94, EsUvc(p.Captura)); }
                    p.Avisos.RemoveAll(delegate(string a) { return a.StartsWith(Tx.T("Modo ", "Mode ")); });
                    p.Modos.Clear();
                    p.Enfoque = p.CapH <= 720 ? 0.6 : p.CapH <= 1080 ? 0.5 : 0.3;
                    ElegirModelos(p, g, pref.Calibracion);
                    p.Motivos.Add(Tx.T("Con captura de ", "With ") + antes + Tx.T("p ni el modelo más ligero cabe en el modo 60: captura a ", "p capture even the lightest preset does not fit: capture at ") + p.CapH + "p");
                }
                // y si ni DLSS E cabe (grafica floja con salida grande): FSR 1, que cuesta una fraccion
                if (p.Escalador == "dlss" && p.Modos.TryGetValue("60", out m60) && m60.Justo)
                {
                    p.Escalador = "fsr1"; p.EscaladorAlternativo = "dlss";
                    p.Avisos.RemoveAll(delegate(string a) { return a.StartsWith(Tx.T("Modo ", "Mode ")); });
                    p.Modos.Clear();
                    ElegirModelos(p, g, pref.Calibracion);
                    p.Motivos.Add(Tx.T("Ni DLSS E cabe en el modo 60 con esta salida: FSR 1 de serie (DLSS en R3; el banco lo confirma)", "Not even DLSS E fits the 60 mode: FSR 1 by default (DLSS on R3)"));
                }
                // (1.2.2, UNIVERSAL) XeSS / FSR 4 que no caben, generacion imposible, capturas a 120 y VRAM (PlanMargen.cs)
                AjustarMargenes(hw, p, g, pref.Calibracion);
                p.MotorEnEspera = gb >= 8 && p.Motor == "propio";
                if (!p.MotorEnEspera && gb < 8) p.Motivos.Add(Tx.T("Menos de 8 GB de VRAM: el motor no se queda en espera", "Under 8 GB VRAM: no engine standby"));
            }

            // ---- DLSS 5 (extra no oficial)
            if (g != null && g.F.DlssNr.Si)
            {
                p.Dlss5Disponible = true;
                p.Dlss5PasadasMax = gb >= 8 ? 3 : 1;
                p.Dlss5Nota = Tx.T("DLL del usuario, no oficial: la carga el motor (engine\\PrismaNr.dll)", "user DLL, unofficial: loaded by the engine (engine\\PrismaNr.dll)");
            }
            else if (g != null && g.EsRtx) p.Dlss5Nota = g.F.DlssNr.Motivo;

            // ---- HDR, RTX HDR y VRR (20-11, HdrVrr.cs): lo que deja la pantalla y la grafica y lo de serie
            PlanHdrVrr.Elegir(hw, p, pref);

            // ---- Menu / HOME a 4K nativo: la Switch 2 (su HOME) y las consolas con menu propio reconocido (PS5, Xbox Series), con pantalla
            // 4K y capturadora que coja 4K60. Con consola=auto tambien (detras puede estar cualquiera; en marcha solo se usa si la consola
            // puesta lo admite: src\Lanzador\ConsolasApp.cs). 20-11 (CE-PERFILES): antes solo la Switch 2, asi que a la PS5 / Xbox se les
            // escribia home_4k_nativo=0 y su menu propio NO salia a 4K nativo; sin ese paso no habia ciclo entrar/salir del menu y el perfil
            // por juego (nombre en el centro de control / conmutador, src\Lanzador\MenuPs5.cs) no se guardaba ni se re-aplicaba
            bool menuNativo = p.Consola == "switch2" || p.Consola == "ps5" || p.Consola == "xboxseries" || p.Consola == "" || p.Consola == "auto";
            p.HomeNativo = menuNativo && p.OutH >= 2160 && CapturaAdmite(p.Captura, 2160, 59);
            if (cons != null && (bool)cons[4]) p.Motivos.Add(Tx.T("Consola con tramado de 5 bits: filtro de bordes y tramado (s2_aa) encendido", "5-bit dithering console: dedither filter on"));
            if (cons != null && (bool)cons[3] && p.Pantalla != null && p.Pantalla.HzMax >= 120 && CapturaAdmite(p.Captura, Math.Min(p.CapH, 1080), 119))
                p.Motivos.Add(Tx.T("La consola y la capturadora llegan a 120 Hz: en los juegos de 120 no hace falta generación (captura a 120)", "Console and capture reach 120 Hz: capture at 120 for 120 Hz games"));
            ResumenMargenes(hw, p, g);   // (1.2.2, UNIVERSAL)
            return p;
        }

        static bool CapturaAdmite(CapturadoraInfo c, int alto, double fps)
        {
            if (c == null) return false;
            // (1.2.2, UNIVERSAL: tambien los formatos previstos del catalogo y con sus fps de verdad: los "malos")
            List<FormatoCaptura> fl = c.FormatosParaElegir();
            if (fl != null && fl.Count > 0) return fl.Exists(delegate(FormatoCaptura f) { return f.Alto >= alto && c.FpsDe(f) >= fps && !f.Comprimido; });
            return c.Modelo != null && c.Modelo.MaxH >= alto && (c.Modelo.MaxFps >= fps || alto < c.Modelo.MaxH && fps <= 60);
        }

        // (1.2.2) el catalogo dice que da 1080p60 sin comprimir y lo leido no lo tiene
        static bool UsbLenta(CapturadoraInfo c)
        {
            if (c.Formatos == null || c.Formatos.Count == 0 || c.Modelo == null) return false;
            Predicate<FormatoCaptura> bueno = delegate(FormatoCaptura f) { return !f.Comprimido && f.Subtipo != "P010" && f.Alto >= 1080 && f.FpsMax >= 59; };
            return c.Modelo.Previstos().Exists(bueno) && !c.Formatos.Exists(bueno);
        }

        static void ElegirCaptura(Hardware hw, PreferenciasPlan pref, PlanImagen p, int consMaxH)
        {
            CapturadoraInfo c = null;
            if (pref.Capturadora != null)
                c = hw.Capturadoras.Find(delegate(CapturadoraInfo x) { return string.Equals(x.DevicePath, pref.Capturadora, StringComparison.OrdinalIgnoreCase) || x.Nombre == pref.Capturadora; });
            if (c == null) c = hw.Capturadoras.Find(delegate(CapturadoraInfo x) { return x.Tipo == TipoCaptura.Capturadora; });
            if (c == null) c = hw.Capturadoras.Find(delegate(CapturadoraInfo x) { return x.Tipo == TipoCaptura.Desconocido; });
            p.Captura = c;
            if (c == null)
            {
                p.Avisos.Add(hw.Capturadoras.Count == 0 ? Tx.T("No hay ninguna capturadora: conéctala (o instala su driver) y vuelve a abrir el asistente", "No capture device: plug one in and rerun the wizard")
                                                        : Tx.T("Solo hay webcams o cámaras virtuales: elige la capturadora en el asistente", "Only webcams/virtual cameras found"));
                p.CapW = 1920; p.CapH = 1080; p.CapOrigen = Tx.T("de serie", "default");
                return;
            }
            p.Audio = c.Audio;
            if (p.Audio == null) p.Avisos.Add(Tx.T("No se sabe el audio de la capturadora: el asistente lo prueba (se oye o no)", "Capture audio unknown: the wizard will test it"));

            // la entrada que se quiere: 1440p para 4K (lo mejor que encontro el banco con DLSS), 1080p para 1080p/1440p si no hay 1440p
            int want = p.OutH >= 2160 ? 1440 : p.OutH >= 1440 ? 1440 : 1080;
            if (consMaxH < want) want = consMaxH;
            GpuInfo g = p.Gpu;
            if (g != null && (g.Indice < 0.35 || g.VramGb < 4) && want > 1080) { want = 1080; p.Motivos.Add(Tx.T("Gráfica modesta: captura a 1080p", "Modest GPU: 1080p capture")); }

            // (1.2.2, UNIVERSAL) los formatos leidos o, sin abrirla, los previstos del catalogo (CatalogoCapturas.cs); comprimidos solo hasta lo
            // que la CPU descomprime (TopeComprimido) y lo que el catalogo sabe que no da (FpsDe)
            List<FormatoCaptura> fl = c.FormatosParaElegir();
            if (fl != null && fl.Count > 0)
            {
                bool leidos = c.Formatos != null && c.Formatos.Count > 0;
                int topeComp = TopeComprimido(hw);
                // (orden: lo pedido con lo que la CPU descomprime; mas lento; mas grande; comprimido de mas pero del tamaño pedido; lo que haya)
                FormatoCaptura f = c.Mejor(want, 59, topeComp);
                if (f == null) f = c.Mejor(want, 0, topeComp);
                if (f == null) f = c.Mejor(4320, 0, topeComp);
                if (f == null) f = c.Mejor(want, 59, 4320);
                if (f == null) f = c.Mejor(want, 0, 4320);
                if (f == null) f = c.Mejor(4320, 0, 4320);
                if (f != null)
                {
                    double fps = c.FpsDe(f);
                    p.CapW = f.Ancho; p.CapH = f.Alto; p.CapFormato = f.Subtipo;
                    p.CapOrigen = leidos ? Tx.T("formatos de la capturadora", "device formats") : Tx.T("catálogo: ", "catalog: ") + c.Modelo.Marca + " " + c.Modelo.Modelo + Tx.T(" (se confirma al abrirla)", " (confirmed when opened)");
                    p.CapFps = FpsPedido(f, fps >= 59 ? 59.94 : fps, EsUvc(c));
                    if (f.Comprimido) p.Avisos.Add(Tx.T("La capturadora solo da ", "The capture only offers ") + f.Subtipo + Tx.T(" a esa resolución: hay que descomprimir (más latencia y CPU)", " there: decoding adds latency"));
                    if (fps < 59) p.Avisos.Add(Tx.T("La capturadora no llega a 60 fps a ", "Capture can't do 60 fps at ") + f.Ancho + "x" + f.Alto + " (" + Tx.N(fps, "0.#") + ")");
                    if (f.Comprimido && f.Alto < 1080 && topeComp < 1080 && fl.Exists(delegate(FormatoCaptura x) { return x.Comprimido && x.Alto == 1080 && c.FpsDe(x) >= 59; }))
                        p.Motivos.Add(Tx.T("MJPEG a 1080p pide más procesador (" + hw.Sistema.Hilos + " hilos): captura a 720p", "MJPEG at 1080p needs a stronger CPU: 720p capture"));
                    if (c.Modelo != null && c.Modelo.Rarezas.Length > 0) p.Motivos.Add(c.Modelo.Marca + " " + c.Modelo.Modelo + ": " + c.Modelo.Rarezas);
                    // (1.2.2) una USB 3 que lista mucho menos de lo que da (sin 1080p60 sin comprimir): suele ser un puerto USB 2.0, un hub o un
                    // cable lento (la lista de formatos encoge: MS2130, j5create, AVerMedia, Elgato...)
                    if (leidos && c.Modelo != null && c.Modelo.Bus.StartsWith("USB3") && UsbLenta(c))
                        p.Avisos.Add(Tx.T("La " + c.Modelo.Marca + " " + c.Modelo.Modelo + " da mucho menos de lo que debería (sin 1080p60 sin comprimir): conéctala a un puerto USB 3 de la placa, sin hub, con su cable",
                                          "The " + c.Modelo.Marca + " " + c.Modelo.Modelo + " offers much less than it should (no uncompressed 1080p60): plug it into a USB 3 port on the motherboard, no hub, with its own cable"));
                    return;
                }
            }
            ModeloCaptura m = c.Modelo;
            p.CapOrigen = m != null ? Tx.T("catálogo (se confirma al abrirla)", "catalog (confirmed when opened)") : Tx.T("de serie (se confirma al abrirla)", "default (confirmed when opened)");
            int h = want;
            if (m != null && m.MaxH > 0 && m.MaxH < h) h = m.MaxH;
            // (1.2.02) capturadora fuera del catalogo y sin formatos leidos: 1080p (casi todas la dan; 1440p con una USB de 1080p no abria)
            if (m == null && h > 1080) h = 1080;
            if (m != null && m.SoloComprimido) { p.CapFormato = "MJPG"; p.Avisos.Add(Tx.T("Capturadora USB 2.0: a 1080p solo MJPEG a 30 fps; mejor 720p60 sin comprimir o una USB 3.0", "USB 2.0 capture: 1080p MJPEG 30 only")); }
            p.CapH = h; p.CapW = h * 16 / 9;
        }

        // (1.2.4 ronda 6, QUITAFG; el usuario: "como el dlss fg va fatal, directamente quitalo", "que haya pa elegir frame gens y ya (da a
        // elegir entre xefg, smooth motion (los que pueden usarlo) y fsr frame generation)" y "En nvidia centrate en el smooth motion que ahora
        // mismo es el frame gen que mejor funciona"). Sin DLSS-G ni x3 / x4: en RTX 40 / 50 Smooth Motion de serie (con la tele a 119,88 Hz
        // Smooth Motion no va: XeSS FG); en RTX 20 / 30 nada de serie (XeSS FG / FSR 3 FG para elegir); AMD AFMF del driver; Intel Arc XeSS FG;
        // lo demas FSR 3 FG. XeSS FG y FSR 3 FG los hace el motor propio (con DLSS 5, XeSS FG va por Magpie)
        static void ElegirGeneracion(PlanImagen p, GpuInfo g, PreferenciasPlan pref)
        {
            GpuFunciones f = g.F;
            int mult = (int)Math.Floor(Math.Max(p.OutHz, p.HzPlan) / 59.94 + 0.02);
            bool es119 = p.Pantalla != null && p.Pantalla.Es119;
            if ((g.Familia == FamiliaGpu.NvidiaRtx40 || g.Familia == FamiliaGpu.NvidiaRtx50) && (f.SmoothMotion.Si || f.XessFg.Si || f.Fsr3Fg.Si))
            {
                // 17-11 (app original) y 23-11 (QUITAFG): el usuario prefiere Smooth Motion (60 reales sin cortes). Con la tele a 119,88 la cola de
                // la sincronizacion no se vacia con Smooth Motion: XeSS FG (el motor propio)
                string t = f.SmoothMotion.Si && !es119 ? "smooth" : f.XessFg.Si ? "xessfg" : f.Fsr3Fg.Si ? "fsr3fg" : "ninguna";
                p.Gen60 = mult >= 2 ? t : "ninguna";
                p.Gen30 = t;
                if (t != "smooth" && f.SmoothMotion.Si) p.GenAlternativa = "Smooth Motion x2";
                // (1.3.1, AJUSTES: XeSS FG ya no se elige -la app pone la mas parecida, Enhanced FG; TiposGeneracion.cs-: el aviso sin nombrarla)
                if (es119 && f.SmoothMotion.Si) p.Avisos.Add(Tx.T("Tele a 119,88 Hz: Smooth Motion no va; para Smooth Motion ponla a 120,000 Hz (Pantalla avanzada: «120», no «119»)", "TV at 119.88 Hz: no Smooth Motion; set 120.000 Hz for Smooth Motion"));
                p.GenDeSerie = t != "ninguna";
            }
            else if (g.Familia == FamiliaGpu.NvidiaRtx20 || g.Familia == FamiliaGpu.NvidiaRtx30)
            {
                p.Gen60 = p.Gen30 = "ninguna";
                p.Motivos.Add(Tx.T("RTX 20/30: DLSS SR y Reflex; sin Smooth Motion (pide RTX 40)", "RTX 20/30: DLSS SR + Reflex, no Smooth Motion"));
                if (!pref.PreferirLatencia && (f.XessFg.Si || f.Fsr3Fg.Si)) p.GenAlternativa = Tx.T("Enhanced FG (experimental, apagada de serie)", "Enhanced FG (experimental, off by default)");   // (1.3.1: XeSS FG y FSR 3 FG ya no se ofrecen)
            }
            else if (f.Afmf.Si)
            {
                p.Gen60 = mult >= 2 ? "afmf" : "ninguna"; p.Gen30 = "afmf";
                p.GenDeSerie = false;
                p.Motivos.Add(Tx.T("AMD: la generación es AFMF del driver: la app no puede encenderla sola; el asistente explica cómo (AMD Software > Juegos > el motor)", "AMD: AFMF is a driver toggle the app can't flip"));
                if (f.Fsr3Fg.Si) p.GenAlternativa = Tx.T("FSR 3 FG x2", "FSR 3 FG x2");
            }
            else
            {
                string t = f.XessFg.Si && (g.Familia == FamiliaGpu.IntelArc || g.Familia == FamiliaGpu.IntelArcB) ? "xessfg" : f.Fsr3Fg.Si ? "fsr3fg" : "ninguna";
                if (t != "ninguna")
                {
                    p.Gen60 = mult >= 2 ? t : "ninguna"; p.Gen30 = t;
                    p.GenDeSerie = false;
                    // (1.3.1: XeSS FG y FSR 3 FG ya no se ofrecen; la app pone la mas parecida, Enhanced FG)
                    p.Motivos.Add(Tx.T("Generación (Enhanced FG, experimental)", "Frame gen (Enhanced FG, experimental)") + Tx.T(": apagada de serie (más latencia); el botón de captura la enciende", ": off by default"));
                }
            }
            if (mult < 2 && p.Gen60 == "ninguna" && (f.SmoothMotion.Si || f.Afmf.Si || f.Fsr3Fg.Si || f.XessFg.Si))
                p.Motivos.Add(Tx.T("Pantalla de ", "Display at ") + Tx.N(p.OutHz, "0.##") + Tx.T(" Hz: en el modo 60 la generación no aporta (solo en el modo 30, a 60)", " Hz: no frame gen in 60 mode"));
            ListasGeneracion(p, g, mult);
        }

        // 19-11 (agente CE): las listas del selector de L3 (generacion_lista_60 / _30) segun lo que tiene la grafica, no fijas.
        // Primero la de serie del plan. (1.2.4 ronda 6, QUITAFG: las tres que quedan donde se puedan -Smooth Motion solo NVIDIA RTX 40 / 50
        // con el driver que la da-, en los dos modos: el usuario quiere poder elegir entre XeSS FG, Smooth Motion y FSR 3 FG. Antes FSR 3 FG
        // se quitaba con DLSS FG o Smooth Motion -medido el 19-11 por Magpie: 29,35-29,59 reales en el 30-; ahora la hace el motor propio. El
        // modo 60 pide 100 Hz o mas.) Orden: NVIDIA Smooth Motion, XeSS FG, FSR 3 FG; Intel Arc XeSS FG, FSR 3 FG; lo demas FSR 3 FG, XeSS FG.
        // XeSS FG con captura 1080p sin xess_fg_1080: eso lo quita la app al usarla (Generacion.cs, Config.FgTypeFor)
        static void ListasGeneracion(PlanImagen p, GpuInfo g, int mult)
        {
            GpuFunciones f = g.F;
            bool nv = g.Fab == Fabricante.Nvidia;
            bool sm = nv && f.SmoothMotion.Si, xe = f.XessFg.Si, fs = f.Fsr3Fg.Si;
            bool arc = g.Familia == FamiliaGpu.IntelArc || g.Familia == FamiliaGpu.IntelArcB;
            string[] orden = nv ? new[] { "smooth", "xess", "fsr3" } : arc ? new[] { "xess", "fsr3" } : new[] { "fsr3", "xess" };
            foreach (string modo in new[] { "30", "60" })
            {
                if (modo == "60" && mult < 2) continue;
                List<string> l = modo == "30" ? p.Lista30 : p.Lista60;
                string serie = Tipo(modo == "30" ? p.Gen30 : p.Gen60);
                if (serie.Length > 0 && !l.Contains(serie)) l.Add(serie);
                foreach (string t in orden)
                    if ((t == "smooth" ? sm : t == "xess" ? xe : fs) && !l.Contains(t)) l.Add(t);
            }
        }

        // El tipo del selector para la generacion del plan ("" = ninguna o AFMF, que es del driver y la app no enciende)
        static string Tipo(string gen)
        {
            return gen == "smooth" ? "smooth" : gen == "xessfg" ? "xess" : gen == "fsr3fg" ? "fsr3" : "";
        }

        static void ElegirModelos(PlanImagen p, GpuInfo g, Calibracion cal)
        {
            double idx = IndicePrudente(g);   // (1.2.2, UNIVERSAL: grafica fuera de la tabla, un 15 % menos; PlanMargen.cs)
            double esc = (p.OutW * (double)p.OutH) / (3840.0 * 2160.0) / idx;
            double capPx = Math.Max(1, p.CapW * (double)p.CapH);
            double factorT = g.Familia == FamiliaGpu.NvidiaRtx20 ? 1.8 : g.Familia == FamiliaGpu.NvidiaRtx30 ? 1.35 : g.Familia == FamiliaGpu.NvidiaRtx50 ? 0.85 : 1.0;
            p.Calibrado = cal != null && cal.GpuClave == g.Clave && cal.Ms.Count > 0;
            double mv = Medido(cal, p.Calibrado, "mv", RefMv1440 * capPx / (2560.0 * 1440.0) / idx);
            double bas = Medido(cal, p.Calibrado, "base", RefBase * esc);
            foreach (string k in new[] { "60", "60_fg", "30", "30_fg" })
            {
                ModoPlan m = new ModoPlan(); m.Clave = k;
                bool fg = k.EndsWith("_fg");
                string gen = k.StartsWith("60") ? p.Gen60 : p.Gen30;
                if (fg && gen == "ninguna") continue;
                m.TopeMs = (k.StartsWith("60") ? 1000.0 / 59.94 : 2000.0 / 59.94) * Margen;
                // (1.2.2, UNIVERSAL) los vectores de movimiento solo si alguien los usa (EngineMotion.RecordMotion: un escalador temporal -DLSS,
                // XeSS, FSR 4- o la generacion); con FSR 1 sin generacion no se calculan
                double mvAqui = fg || (p.CapH > 0 && p.CapH < p.OutH && (p.Escalador == "dlss" || p.Escalador == "xess" || p.Escalador == "fsr4")) ? mv : 0;
                // (1.2.2, UNIVERSAL: costes de la generacion con lo medido, PlanMargen.CosteGeneracion; AFMF tambien gasta)
                double fgc = !fg ? 0 : gen == "smooth" ? Medido(cal, p.Calibrado, "fg_smooth", CosteGeneracion(gen, g, esc)) : CosteGeneracion(gen, g, esc);
                double fijo = bas + mvAqui + fgc;
                if (p.Escalador == "dlss")
                {
                    List<string> orden = new List<string>();
                    if (g.F.DlssTransformer.Si) { orden.Add("L"); orden.Add("M"); orden.Add("K"); }
                    orden.Add("E");
                    m.Modelo = "E";
                    m.CosteMs = fijo + Medido(cal, p.Calibrado, "dlss_E", RefDlssE * esc);
                    m.Justo = m.CosteMs > m.TopeMs;
                    foreach (string pr in orden)
                    {
                        double c = fijo + Medido(cal, p.Calibrado, "dlss_" + pr, (pr == "L" ? RefDlssL : pr == "M" ? RefDlssM : pr == "K" ? RefDlssK : RefDlssE) * esc * (pr == "E" ? 1.0 : factorT));
                        if (c <= m.TopeMs) { m.Modelo = pr; m.CosteMs = c; m.Justo = false; break; }
                    }
                }
                else
                {
                    m.Modelo = "";
                    // (1.2.2, UNIVERSAL: XeSS y FSR 4 tambien cuestan; antes 0, pendiente conocido. Sin escalar, nada)
                    bool sube = p.CapH > 0 && p.CapH < p.OutH;
                    m.CosteMs = fijo + (!sube ? 0 : p.Escalador == "fsr1" ? Medido(cal, p.Calibrado, "fsr1", RefFsr1 * esc) : CosteEscalador(p.Escalador, g, esc));
                    m.Justo = m.CosteMs > m.TopeMs;
                }
                p.Modos[k] = m;
                if (m.Justo) p.Avisos.Add(Tx.T("Modo ", "Mode ") + k + Tx.T(": ni lo más ligero cabe con margen (", ": even the lightest is tight (") + Tx.N(m.CosteMs, "0.0") + " / " + Tx.N(m.TopeMs, "0.0") + Tx.T(" ms): el banco lo confirma; si no cabe, captura a menos resolución", " ms)"));
            }
        }

        static double Medido(Calibracion c, bool ok, string k, double estimado)
        {
            double v;
            return ok && c.Ms.TryGetValue(k, out v) ? v : estimado;
        }
    }
}
