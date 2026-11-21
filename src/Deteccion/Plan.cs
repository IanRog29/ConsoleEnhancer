// Plan.cs - con lo detectado, la cadena de imagen y los ajustes de serie ("el plan") (ConsoleEnhancer)
//
// Regla general (ver DISENO.md):
//   NVIDIA RTX 50 : motor propio (Streamline) DLSS SR + Reflex + DLSS-G (x2, x3/x4 si la pantalla tiene los Hz); Smooth Motion x2 de alternativa
//   NVIDIA RTX 40 : DLSS SR + Reflex + Smooth Motion x2 (o DLSS-G x2: tele a 119,88 Hz, driver sin Smooth Motion)
//   NVIDIA RTX 20/30: DLSS SR + Reflex, sin DLSS-G (extra no oficial dlssg_sm86 en RTX 30 si el usuario lo tiene, apagado)
//   GTX / AMD / Intel: FSR 1 (EASU + RCAS) + CAS en shader propio; generacion: AFMF del driver (AMD RX 6000+), si no FSR 3 FG o XeSS FG
//                      por Magpie (apagadas de serie: mas latencia); Reflex solo NVIDIA (GTX tambien)
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
        public Dictionary<string, double> Ms = new Dictionary<string, double>();   // dlss_L, dlss_M, dlss_K, dlss_E, fsr1, mv, base, fg_dlssg, fg_smooth

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
        public string Gen60 = "ninguna", Gen30 = "ninguna";   // dlssg, smooth, afmf, fsr3fg, xessfg, ninguna
        public int Mult60 = 1, Mult30 = 1;
        public bool GenDeSerie;                // la generacion encendida al empezar (boton de captura: si/no)
        public bool DlssgSoloMagpie;           // (1.0.10) RTX 30 con dlssg_sm86: DLSS FG solo por Magpie (el motor no la tiene: Streamline pide RTX 40)
        public string GenAlternativa = "";     // la que se puede elegir ademas (p. ej. FSR 3 FG por Magpie)
        // 19-11 (agente CE, el selector de L3 de Prisma): los tipos que se ofrecen en cada modo (ini generacion_lista_60 / _30), los que
        // tiene esta grafica: smooth (Smooth Motion), dlss (DLSS FG de Magpie / DLSS-G del motor), xess, fsr3 (estos dos por Magpie)
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
            sb.AppendLine("  " + Tx.T("Generación: modo 60 ", "Frame gen: 60 mode ") + Nom(Gen60) + (Mult60 > 1 ? " x" + Mult60 : "") + Tx.T(", modo 30 ", ", 30 mode ") + Nom(Gen30) + (Mult30 > 1 ? " x" + Mult30 : "") +
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
            foreach (string t in new[] { "1280x720", "1920x1080", "2560x1440" })
                foreach (FormatoCaptura f in Captura.Formatos)
                    if (!f.Comprimido && f.FpsMax >= 119 && (f.Ancho + "x" + f.Alto) == t)
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
            sb.AppendLine("motor_generacion_60=" + Gen60);
            sb.AppendLine("motor_generacion_30=" + Gen30);
            sb.AppendLine("generacion_mult_60=" + Mult60);
            sb.AppendLine("generacion_mult_30=" + Mult30);
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

    public static class Planificador
    {
        // Lo medido en la RTX 4060 (indice 1,0) con salida 4K (bancos del motor, 15/17-11): ms por imagen real
        const double RefDlssE = 4.8, RefDlssK = 6.2, RefDlssM = 10.1, RefDlssL = 12.1, RefMv1440 = 1.3, RefBase = 1.0, RefFsr1 = 0.8;
        const double RefFgDlss = 2.0, RefFgSmooth = 1.0, RefFgMagpie = 2.5;   // estimados (la generacion se midio junto al resto)
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
                p.Motor = g == null || g.Software ? "mpv" : "mpv";
                p.Escalador = "ninguno";
                p.Latencia = "ninguna";
                p.Avisos.Add(g == null || g.Software ? Tx.T("Sin gráfica útil (solo la de software de Windows): se enseña la captura tal cual con mpv; instala el driver de tu gráfica", "No usable GPU: plain mpv; install your GPU driver")
                                                     : Tx.T("La gráfica no tiene Direct3D 12 nivel 12_0: mpv + Magpie, sin el motor propio", "No D3D12 FL 12_0: mpv + Magpie"));
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

            // ---- escalado y modelos
            if (p.Motor == "propio")
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
                    p.Motivos.Add(Tx.T("Escalado: FSR 4 (AMD RX 9000, por Magpie)", "Scaling: FSR 4 (AMD RX 9000, via Magpie)"));
                }
                else if (g.F.XessSr.Si && g.Familia != FamiliaGpu.IntelXe && g.Familia != FamiliaGpu.IntelAntigua && g.Familia != FamiliaGpu.Software)
                {
                    p.Escalador = "xess"; p.EscaladorAlternativo = "fsr1";
                    p.Motivos.Add(Tx.T("Escalado: XeSS (sin DLSS en esta gráfica, por Magpie)", "Scaling: XeSS (no DLSS on this GPU, via Magpie)"));
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
                    p.CapW = 1920; p.CapH = 1080;
                    FormatoCaptura f = p.Captura.Formatos != null ? p.Captura.Mejor(1080, 59) : null;
                    if (f != null) { p.CapW = f.Ancho; p.CapH = f.Alto; p.CapFormato = f.Subtipo; }
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
                p.MotorEnEspera = gb >= 8;
                if (!p.MotorEnEspera) p.Motivos.Add(Tx.T("Menos de 8 GB de VRAM: el motor no se queda en espera (libera la memoria para Magpie)", "Under 8 GB VRAM: no engine standby"));
            }

            // ---- DLSS 5 (extra no oficial)
            if (g != null && g.F.DlssNr.Si)
            {
                p.Dlss5Disponible = true;
                p.Dlss5PasadasMax = gb >= 8 ? 3 : 1;
                p.Dlss5Nota = Tx.T("DLL del usuario, no oficial: va por mpv + Magpie; escala de entrada medida por el banco", "user DLL, unofficial: via mpv + Magpie");
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
            return p;
        }

        static bool CapturaAdmite(CapturadoraInfo c, int alto, double fps)
        {
            if (c == null) return false;
            if (c.Formatos != null && c.Formatos.Count > 0) return c.Formatos.Exists(delegate(FormatoCaptura f) { return f.Alto >= alto && f.FpsMax >= fps && !f.Comprimido; });
            return c.Modelo != null && c.Modelo.MaxH >= alto && (c.Modelo.MaxFps >= fps || alto < c.Modelo.MaxH && fps <= 60);
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

            if (c.Formatos != null && c.Formatos.Count > 0)
            {
                FormatoCaptura f = c.Mejor(want, 59);
                if (f == null) f = c.Mejor(4320, 0);
                if (f != null)
                {
                    p.CapW = f.Ancho; p.CapH = f.Alto; p.CapFormato = f.Subtipo; p.CapOrigen = Tx.T("formatos de la capturadora", "device formats");
                    p.CapFps = f.FpsMax >= 59.9 && f.FpsMax < 60.5 ? "60000/1001" : f.FpsMax >= 59 ? "60000/1001" : f.FpsMax.ToString("0.###", CultureInfo.InvariantCulture);
                    if (f.Comprimido) p.Avisos.Add(Tx.T("La capturadora solo da ", "The capture only offers ") + f.Subtipo + Tx.T(" a esa resolución: hay que descomprimir (más latencia y CPU)", " there: decoding adds latency"));
                    if (f.FpsMax < 59) p.Avisos.Add(Tx.T("La capturadora no llega a 60 fps a ", "Capture can't do 60 fps at ") + f.Ancho + "x" + f.Alto + " (" + Tx.N(f.FpsMax, "0.#") + ")");
                    return;
                }
            }
            ModeloCaptura m = c.Modelo;
            p.CapOrigen = m != null ? Tx.T("catálogo (se confirma al abrirla)", "catalog (confirmed when opened)") : Tx.T("de serie (se confirma al abrirla)", "default (confirmed when opened)");
            int h = want;
            if (m != null && m.MaxH > 0 && m.MaxH < h) h = m.MaxH;
            // (1.2.02) capturadora fuera del catalogo y sin formatos leidos: 1080p (casi todas la dan; 1440p con una USB de 1080p no abria)
            if (m == null && h > 1080) h = 1080;
            if (m != null && m.SoloComprimido) { p.CapFormato = "mjpeg"; p.Avisos.Add(Tx.T("Capturadora USB 2.0: a 1080p solo MJPEG a 30 fps; mejor 720p60 sin comprimir o una USB 3.0", "USB 2.0 capture: 1080p MJPEG 30 only")); }
            p.CapH = h; p.CapW = h * 16 / 9;
        }

        static void ElegirGeneracion(PlanImagen p, GpuInfo g, PreferenciasPlan pref)
        {
            GpuFunciones f = g.F;
            int mult = (int)Math.Floor(Math.Max(p.OutHz, p.HzPlan) / 59.94 + 0.02);
            bool es119 = p.Pantalla != null && p.Pantalla.Es119;
            if (f.DlssG.Si && g.Familia == FamiliaGpu.NvidiaRtx50)
            {
                p.Gen60 = mult >= 2 ? "dlssg" : "ninguna"; p.Mult60 = Math.Max(1, Math.Min(f.MfgMax, mult));
                p.Gen30 = "dlssg"; p.Mult30 = 2;
                if (f.SmoothMotion.Si) p.GenAlternativa = "Smooth Motion x2";
                p.GenDeSerie = true;
            }
            // (1.2.02: tambien RTX 50 sin DLSS-G visto, p. ej. con HAGS apagado: Smooth Motion como en las RTX 40)
            else if ((g.Familia == FamiliaGpu.NvidiaRtx40 || g.Familia == FamiliaGpu.NvidiaRtx50) && (f.SmoothMotion.Si || f.DlssG.Si))
            {
                // 17-11 (app original): el usuario prefiere Smooth Motion (60 reales sin cortes; DLSS-G con Reflex y vsync perdia 2 por segundo).
                // Con la tele a 119,88 la cola de la sincronizacion no se vacia con Smooth Motion: DLSS-G
                // 21-11 (1.0.7): DLSS FG de serie, como Prisma desde el 20-11 03:58 (motor_generacion_60/30=dlss); Smooth Motion queda de
                // alternativa en el selector de L3. Antes Smooth Motion de serie: "no hay DLSS FG" (el usuario, 21-11)
                string t = f.DlssG.Si ? "dlssg" : f.SmoothMotion.Si && !es119 ? "smooth" : "ninguna";
                p.Gen60 = mult >= 2 ? t : "ninguna"; p.Mult60 = mult >= 2 && t != "ninguna" ? 2 : 1;
                p.Gen30 = t; p.Mult30 = t != "ninguna" ? 2 : 1;
                if (t == "smooth" && f.DlssG.Si) p.GenAlternativa = "DLSS-G x2";
                if (t == "dlssg" && f.SmoothMotion.Si) p.GenAlternativa = "Smooth Motion x2";
                if (es119 && f.SmoothMotion.Si) p.Avisos.Add(Tx.T("Tele a 119,88 Hz: con DLSS-G; para Smooth Motion ponla a 120,000 Hz (Pantalla avanzada: «120», no «119»)", "TV at 119.88 Hz: DLSS-G; set 120.000 Hz for Smooth Motion"));
                p.GenDeSerie = t != "ninguna";
            }
            else if (g.Familia == FamiliaGpu.NvidiaRtx20 || g.Familia == FamiliaGpu.NvidiaRtx30)
            {
                p.Gen60 = p.Gen30 = "ninguna";
                p.Motivos.Add(Tx.T("RTX 20/30: DLSS SR y Reflex, sin DLSS-G (pide RTX 40)", "RTX 20/30: DLSS SR + Reflex, no DLSS-G"));
                if (!pref.PreferirLatencia && f.Fsr3Fg.Si) p.GenAlternativa = Tx.T("FSR 3 FG x2 por Magpie (más latencia, apagada de serie)", "FSR 3 FG x2 via Magpie (off by default)");
                if (f.DlssGComunidad.Si) p.GenAlternativa += (p.GenAlternativa.Length > 0 ? "; " : "") + Tx.T("DLSS-G no oficial (dlssg_sm86) por Magpie", "unofficial DLSS-G (dlssg_sm86)");
                // 21-11 (1.0.10, Reddit: "no se como encender DLSS FG aunque elegi mi carpeta dlssg_sm86 en el asistente", RTX 30): con el
                // runtime de la comunidad, DLSS FG por Magpie es elegible (L3 / Ctrl+G / bandeja) y la generacion queda disponible; apagada de serie
                if (f.DlssGComunidad.Si) p.DlssgSoloMagpie = true;
            }
            else if (f.Afmf.Si)
            {
                p.Gen60 = mult >= 2 ? "afmf" : "ninguna"; p.Mult60 = mult >= 2 ? 2 : 1; p.Gen30 = "afmf"; p.Mult30 = 2;
                p.GenDeSerie = false;
                p.Motivos.Add(Tx.T("AMD: la generación es AFMF del driver: la app no puede encenderla sola; el asistente explica cómo (AMD Software > Juegos > el motor)", "AMD: AFMF is a driver toggle the app can't flip"));
                if (f.Fsr3Fg.Si) p.GenAlternativa = Tx.T("FSR 3 FG x2 por Magpie", "FSR 3 FG x2 via Magpie");
            }
            else
            {
                string t = f.XessFg.Si && (g.Familia == FamiliaGpu.IntelArc || g.Familia == FamiliaGpu.IntelArcB) ? "xessfg" : f.Fsr3Fg.Si ? "fsr3fg" : "ninguna";
                if (t != "ninguna")
                {
                    p.Gen60 = mult >= 2 ? t : "ninguna"; p.Mult60 = mult >= 2 ? 2 : 1; p.Gen30 = t; p.Mult30 = 2;
                    p.GenDeSerie = false;
                    p.Motivos.Add(Tx.T("Generación por Magpie (", "Frame gen via Magpie (") + (t == "xessfg" ? "XeSS FG" : "FSR 3 FG") + Tx.T("): apagada de serie (más latencia); el botón de captura la enciende (mpv + Magpie)", "): off by default"));
                }
            }
            if (mult < 2 && p.Gen60 == "ninguna" && (f.DlssG.Si || f.SmoothMotion.Si || f.Afmf.Si || f.Fsr3Fg.Si))
                p.Motivos.Add(Tx.T("Pantalla de ", "Display at ") + Tx.N(p.OutHz, "0.##") + Tx.T(" Hz: en el modo 60 la generación no aporta (solo en el modo 30, a 60)", " Hz: no frame gen in 60 mode"));
            ListasGeneracion(p, g, mult);
        }

        // 19-11 (agente CE): las listas del selector de L3 (generacion_lista_60 / _30) segun lo que tiene la grafica, no fijas.
        // Primero la de serie del plan. Modo 30 (a 60 Hz de salida basta): todo lo que haya. Modo 60 (pide 100 Hz o mas): solo lo del driver o
        // de NVIDIA (Smooth Motion, DLSS FG); XeSS / FSR 3 por Magpie no llegan a 60 reales (Prisma, 19-11: 27-48) salvo que sean lo unico.
        // Medido en Prisma (RTX 4060 Ti, 19-11 19:10-19:30, tele a 120 Hz): en el 30 FSR 3 FG se queda en 29,35-29,59 reales -> fuera si hay
        // Smooth Motion o DLSS FG (RTX 40/50); en las demas es lo unico y se deja (sin medir en ellas; en la Ti no llego a 29,9). En el 60
        // DLSS FG no llega a ritmo en RTX 40 (Magpie 59,7-59,8, motor DLSS-G 58,7-59,0: Reflex con la sincronizacion) -> fuera en RTX 40,
        // salvo que sea la de serie del plan (tele a 119,88 Hz: ahi Smooth Motion no va). En RTX 50 se queda (es su generacion de serie,
        // DLSS-G multiple; sin medir). XeSS FG con captura 1080p no arranca: eso lo quita la app al usarla (Generacion.cs, Config.FgTypeNow)
        static void ListasGeneracion(PlanImagen p, GpuInfo g, int mult)
        {
            GpuFunciones f = g.F;
            bool nv = g.Fab == Fabricante.Nvidia;
            bool sm = nv && f.SmoothMotion.Si, dg = nv && (f.DlssG.Si || p.DlssgSoloMagpie), xe = f.XessFg.Si, fs = f.Fsr3Fg.Si;
            bool rtx40 = g.Familia == FamiliaGpu.NvidiaRtx40;
            string serie30 = Tipo(p.Gen30), serie60 = Tipo(p.Gen60);
            List<string> l30 = new List<string>();
            if (serie30.Length > 0) l30.Add(serie30);
            if (sm) l30.Add("smooth");
            if (dg) l30.Add("dlss");
            // (XeSS FG primero solo en Intel Arc; en lo demas FSR 3 FG, el de AMD, que es el nativo fuera de Intel)
            bool arc = g.Familia == FamiliaGpu.IntelArc || g.Familia == FamiliaGpu.IntelArcB;
            string[] magpie = arc ? new[] { "xess", "fsr3" } : new[] { "fsr3", "xess" };
            foreach (string t in magpie)
            {
                if (t == "fsr3" && (sm || dg) && serie30 != "fsr3") continue;   // (no llega a ritmo: hay algo mejor)
                if (t == "xess" ? xe : fs) l30.Add(t);
            }
            List<string> l60 = new List<string>();
            if (mult >= 2)
            {
                if (serie60.Length > 0) l60.Add(serie60);
                if (sm) l60.Add("smooth");
                if (dg) l60.Add("dlss");   // (21-11, 1.0.7: tambien en RTX 40, como Prisma)
                if (!sm && !dg) foreach (string t in magpie) if (t == "xess" ? xe : fs) l60.Add(t);
                // 21-11 (1.0.9; el usuario: "arregla que ahora de repente no aparece xefg"): XeSS FG tambien en el modo 60 aunque haya algo
                // de NVIDIA (en Prisma, 19-11, no llegaba a 60 reales: 27-48; el usuario lo quiere poder elegir)
                if (xe && !l60.Contains("xess")) l60.Add("xess");
            }
            foreach (string t in l30) if (!p.Lista30.Contains(t)) p.Lista30.Add(t);
            foreach (string t in l60) if (!p.Lista60.Contains(t)) p.Lista60.Add(t);
        }

        // El tipo del selector para la generacion del plan ("" = ninguna o AFMF, que es del driver y la app no enciende)
        static string Tipo(string gen)
        {
            return gen == "smooth" ? "smooth" : gen == "dlssg" ? "dlss" : gen == "xessfg" ? "xess" : gen == "fsr3fg" ? "fsr3" : "";
        }

        static void ElegirModelos(PlanImagen p, GpuInfo g, Calibracion cal)
        {
            double idx = Math.Max(0.05, g.Indice);
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
                double fgc = !fg ? 0 : gen == "dlssg" ? Medido(cal, p.Calibrado, "fg_dlssg", RefFgDlss * esc) : gen == "smooth" ? Medido(cal, p.Calibrado, "fg_smooth", RefFgSmooth * esc) : gen == "afmf" ? 0 : RefFgMagpie * esc;
                double fijo = bas + mv + fgc;
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
                    m.CosteMs = fijo + (p.Escalador == "fsr1" ? Medido(cal, p.Calibrado, "fsr1", RefFsr1 * esc) : 0);
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
