// PlanMargen.cs - 22-11-2026 (1.2.2, agente UNIVERSAL): el margen de grafica, VRAM y CPU del plan, para cualquier PC (ConsoleEnhancer)
//
// El usuario: "Haz que vaya fluidisimo en cualquier setup de cualquier persona (...) y no exprimas al 100% las graficas que tengan, que
// siempre haya un leve margen para que no haya lagazos ni nada".
//
// Los margenes, en todos los caminos (lo que se elige al empezar es prudente; lo que se ajusta jugando no pasa del 85 %):
//  - PLAN (este fichero + Plan.cs, sin medir nada): lo de serie tiene que caber en el 75 % del tiempo de una imagen real (Planificador.Margen)
//    con costes estimados por la gama de la grafica. Las estimaciones fallan +-20-30 % (driver, reloj, la capturadora de verdad cuesta 7-15
//    puntos mas que la de prueba): con el 75 % lo peor de la estimacion cae aun por debajo del 100 %. Grafica fuera de la tabla de
//    indices (indice estimado por la familia): un 15 % menos de grafica (IndicePrudente). El motor sube solo si sobra (EngineAdapta).
//  - MOTOR (src\Motor\Engine\EngineMargen.cs): el tope del tiempo de grafica (p95 de cada segundo) es el 85 % del tiempo de una imagen
//    (motor_uso_grafica=85) menos lo que se lleva la generacion de NVIDIA (DLSS-G / Smooth Motion: no se ve en lo medido). Antes 93 % a
//    60 (15,5 de 16,7 ms), 96 % a 120 (8,0 de 8,3) y 93 % a 30 (31 de 33,4): picos de un 5-10 % = imagenes tarde.
//  - MAGPIE con DLSS 5 (src\Lanzador\EscalaAuto.cs): 75 % del tiempo de una imagen real para lo previsto y sube solo hasta que la grafica
//    va al 85 % (ya era asi).
//  - VRAM: lo previsto (Windows + motor + DLSS + generacion; DLSS 5 por Magpie aparte, que ya pide 6/8 GB) no pasa del 85 % de la VRAM.
//  - CPU: descomprimir MJPEG lo hace mpv en la CPU (--hwdec=no) con un solo hilo: ~8-14 ms una imagen de 1080p en un procesador de 6
//    nucleos de 2018; 4K es 4 veces eso (no llega ni a 30). Sin otro formato: MJPEG hasta 1080p con 6 hilos o mas, hasta 720p con menos.
//  - 120 fps (capturas_120): solo las capturas a las que la cadena mas ligera (DLSS E / FSR 1...) cabe en el 85 % de 8,3 ms.
// Costes (ms por imagen real con salida 4K en una RTX 4060 = indice 1,0; se escalan por los pixeles de salida y el indice):
//  - DLSS-G x2 del motor 4,5 (antes 2,0, "estimado"): el 16-11 en la RTX 4060 a 4K, 60 + DLSS-G: K 8,5 ms cabia, M 12,4 no y K con los
//    vectores de calidad a 10,1 ms (p95 10,8) ya perdia 6 imagenes/s: lo de NVIDIA se lleva ~5 ms. Smooth Motion 2,5 (EscalaAuto: la mitad
//    que DLSS FG). FSR 3 FG por Magpie 8 y XeSS FG 9 (EscalaAuto: 7 y 8 en la Ti, x1,2), XeSS FG en Intel Arc (XMX) 6. AFMF 3 (del driver
//    de AMD: tampoco se ve, pero gasta).
//  - Escalado (antes XeSS y FSR 4 costaban 0 en el plan, pendiente conocido): XeSS DP4a 5,4 (EscalaAuto 4,5 en la Ti), XeSS en Arc con XMX
//    3,0; FSR 4 en RX 9000 3,0, en RX 7000 por el override x2,2 (6,6) y en RX 6000 x3 (9,0) (sin FP8: van emulados). FSR 1 0,8. Sin medir
//    en esas graficas: estimaciones prudentes; si no cabe, XeSS o FSR 1 de serie y el otro en R3.
// C# 5
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConsoleEnhancer.Deteccion
{
    public sealed partial class PlanImagen
    {
        // (1.2.2, UNIVERSAL) margenes del plan: VRAM prevista, capturas que caben a 120, CPU para MJPEG
        public double VramPrevistaMb, VramTotalMb;
        public int Tope120Alto = 4320;          // la captura mas alta que cabe a 120 fps (Capturas120)
        public string Margenes = "";            // una linea para el informe

        string InformeMargenes()
        {
            if (Margenes.Length == 0) return "";
            return "  " + Tx.T("Márgenes: ", "Headroom: ") + Margenes + "\r\n";
        }
    }

    public static partial class Planificador
    {
        public const double UsoMotor = 0.85;            // el tope del motor jugando (motor_uso_grafica; EngineMargen.cs)
        const double RefFgDlss2 = 4.5, RefFgSmooth2 = 2.5, RefFgFsr3 = 8.0, RefFgXess = 9.0, RefFgXessArc = 6.0, RefFgAfmf = 3.0;
        const double RefXess = 5.4, RefXessArc = 3.0, RefFsr4 = 3.0;

        // El indice para estimar: el de la tabla o, si la grafica no esta en ella (estimado por la familia), un 15 % menos
        public static double IndicePrudente(GpuInfo g)
        {
            if (g == null) return 0.05;
            return Math.Max(0.05, g.Indice * (g.IndiceEstimado ? 0.85 : 1.0));
        }

        // ms por imagen real de la generacion (a la salida de ahora; esc = pixeles de salida / 4K / indice)
        public static double CosteGeneracion(string gen, GpuInfo g, double esc)
        {
            bool arc = g != null && (g.Familia == FamiliaGpu.IntelArc || g.Familia == FamiliaGpu.IntelArcB);
            switch (gen)
            {
                case "dlssg": return RefFgDlss2 * esc;
                case "smooth": return RefFgSmooth2 * esc;
                case "fsr3fg": return RefFgFsr3 * esc;
                case "xessfg": return (arc ? RefFgXessArc : RefFgXess) * esc;
                case "afmf": return RefFgAfmf * esc;
                default: return 0;
            }
        }

        // ms por imagen real del escalador que no es DLSS (los de DLSS van por modelo en ElegirModelos)
        public static double CosteEscalador(string escalador, GpuInfo g, double esc)
        {
            switch (escalador)
            {
                case "fsr1": return RefFsr1 * esc;
                case "xess": return (g != null && (g.Familia == FamiliaGpu.IntelArc || g.Familia == FamiliaGpu.IntelArcB) ? RefXessArc : RefXess) * esc;
                case "fsr4":
                    {
                        double k = g == null ? 3.0 : g.Familia == FamiliaGpu.AmdRdna4 ? 1.0 : g.Familia == FamiliaGpu.AmdRdna3 ? 2.2 : 3.0;
                        return RefFsr4 * k * esc;
                    }
                default: return 0;
            }
        }

        // Cuanto MJPEG se puede descomprimir en la CPU (mpv, un hilo): el alto maximo de una captura comprimida
        public static int TopeComprimido(Hardware hw)
        {
            int hilos = hw != null && hw.Sistema != null ? hw.Sistema.Hilos : 0;
            return hilos == 0 || hilos >= 6 ? 1080 : 720;
        }

        // Los fps que se piden a la capturadora con este formato: 59,94 si su rango lo admite; si solo lista 60,000 exactos (muchas UVC
        // con velocidades fijas), "60": ffmpeg (dshow) no abre una velocidad fuera del rango que lista la capturadora (mpv salia sin imagen)
        public static string FpsPedido(FormatoCaptura f, double objetivo)
        {
            if (f == null) return "60000/1001";
            double mx = f.FpsMax, mn = f.FpsMin > 0 ? f.FpsMin : f.FpsMax;
            if (objetivo >= 59)
            {
                if (f.Previsto || (mn <= 59.95 && mx >= 59.93)) return "60000/1001";
                if (Math.Abs(mx - 60) < 0.05) return "60";
                if (mx >= 59) return mx.ToString("0.###", CultureInfo.InvariantCulture);
            }
            if (mx >= 29.9 && mx <= 30.05 && mn >= 29.9) return Math.Abs(mx - 29.97) < 0.02 ? "30000/1001" : "30";
            return mx.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- tras la captura: ¿puede el motor propio con ese formato?
        // El motor abre RGB24 / RGB32 / YUY2 / NV12 (src\Motor\Engine\CaptureSource.cs). Una capturadora que a esa resolucion solo da MJPEG
        // (MS2109 y casi todas las de USB 2.0) o H.264: el motor elegia el YUY2 del mismo tamaño a 5-10 fps (lo mas parecido que habia).
        // Entonces la imagen va por mpv (descomprime) + Magpie (escalado y generacion: los mismos de la grafica)
        static void MotorSegunCaptura(PlanImagen p)
        {
            if (p.Motor != "propio" || p.Captura == null) return;
            string f = (p.CapFormato ?? "").ToUpperInvariant();
            if (f == "MJPEG") f = "MJPG";
            bool conocido = f.Length > 0 && f != "AUTO";
            if (!conocido || CapturadoraInfo.MotorLoAbre(f)) return;
            p.Motor = "mpv";
            p.Motivos.Add(Tx.T("La captura llega en " + f + " (el motor propio solo abre YUY2 / NV12 / RGB): la imagen va por mpv (lo descomprime) y Magpie (escalado y generación)",
                               "Capture arrives as " + f + " (the own engine only takes YUY2 / NV12 / RGB): picture goes through mpv (decodes it) and Magpie"));
        }

        // ---------------------------------------------------------------- tras los modelos: escalador que no cabe, generacion imposible, 120 y VRAM
        static void AjustarMargenes(Hardware hw, PlanImagen p, GpuInfo g, Calibracion cal)
        {
            if (g == null || p.Escalador == "ninguno" && p.Gen60 == "ninguna" && p.Gen30 == "ninguna") { Vram(p, g); return; }
            // 1) XeSS / FSR 4 que no caben en el modo 60 con margen: el siguiente (FSR 4 -> XeSS -> FSR 1); el que no cabe, en R3
            for (int guard = 0; guard < 2; guard++)
            {
                ModoPlan m60;
                if ((p.Escalador != "fsr4" && p.Escalador != "xess") || !p.Modos.TryGetValue("60", out m60) || !m60.Justo) break;
                string antes = p.Escalador;
                p.Escalador = antes == "fsr4" && g.F.XessSr.Si ? "xess" : "fsr1";
                p.EscaladorAlternativo = antes;
                p.Avisos.RemoveAll(delegate(string a) { return a.StartsWith(Tx.T("Modo ", "Mode ")); });
                p.Modos.Clear();
                ElegirModelos(p, g, cal);
                p.Motivos.Add(Tx.T("Escalado: " + Nombre(antes) + " no cabe con margen en el modo 60 a esta salida (" + Tx.N(m60.CosteMs, "0.0") + " de " + Tx.N(m60.TopeMs, "0.0") +
                                   " ms): " + Nombre(p.Escalador) + " de serie y " + Nombre(antes) + " en R3",
                                   "Scaling: " + Nombre(antes) + " does not fit the 60 mode with headroom: " + Nombre(p.Escalador) + " by default, " + Nombre(antes) + " on R3"));
            }
            // 2) generacion que ni con lo mas ligero cabe en el tiempo entero de una imagen real: no se ofrece en ese modo (seria ir a tirones)
            QuitarGeneracionImposible(p, g, "60");
            QuitarGeneracionImposible(p, g, "30");
            // 2b) ni el modo 60 sin nada cabe en el tiempo entero de una imagen (graficas integradas a 4K): que hacer, en claro
            ModoPlan b60;
            if (p.Modos.TryGetValue("60", out b60) && b60.CosteMs > 1000.0 / 59.94 && p.OutH > 1080)
                p.Avisos.Add(Tx.T("Esta gráfica no llega a 60 fps pintando a " + p.OutW + "x" + p.OutH + " (" + Tx.N(b60.CosteMs, "0.0") + " ms de 16,7): pon la pantalla a 1920x1080 (Configuración > Pantalla) o usa una gráfica dedicada; si no, el modo 30",
                                  "This GPU can't reach 60 fps rendering at " + p.OutW + "x" + p.OutH + ": set the display to 1920x1080 or use a dedicated GPU; otherwise use the 30 mode"));
            // 3) a 120 fps: hasta que captura cabe la cadena mas ligera en el 85 %
            Tope120(p, g);
            // 4) VRAM
            Vram(p, g);
        }

        static string Nombre(string e) { return e == "fsr4" ? "FSR 4" : e == "xess" ? "XeSS" : e == "fsr1" ? "FSR 1" : e == "dlss" ? "DLSS" : e; }

        // ms de lo fijo + el escalado mas ligero para esta captura y esta salida (lo de ElegirModelos sin la generacion)
        static double CosteLigero(PlanImagen p, GpuInfo g, int capW, int capH) { return CosteLigero(p, g, capW, capH, false); }

        static double CosteLigero(PlanImagen p, GpuInfo g, int capW, int capH, bool conGeneracion)
        {
            double idx = IndicePrudente(g);
            double esc = (p.OutW * (double)p.OutH) / (3840.0 * 2160.0) / idx;
            bool up = capH > 0 && capH < p.OutH;
            bool temporal = up && (p.Escalador == "dlss" || p.Escalador == "xess" || p.Escalador == "fsr4");
            double mv = conGeneracion || temporal ? RefMv1440 * Math.Max(1, capW * (double)capH) / (2560.0 * 1440.0) / idx : 0;   // (vectores: solo si alguien los usa)
            double bas = RefBase * esc;
            double sc = !up ? 0 : p.Escalador == "dlss" ? RefDlssE * esc : CosteEscalador(p.Escalador == "ninguno" ? "fsr1" : p.Escalador, g, esc);
            return bas + mv + sc;
        }

        static void QuitarGeneracionImposible(PlanImagen p, GpuInfo g, string modo)
        {
            string gen = modo == "60" ? p.Gen60 : p.Gen30;
            List<string> lista = modo == "60" ? p.Lista60 : p.Lista30;
            double periodo = (modo == "60" ? 1000.0 : 2000.0) / 59.94;
            double idx = IndicePrudente(g);
            double esc = (p.OutW * (double)p.OutH) / (3840.0 * 2160.0) / idx;
            double fijo = CosteLigero(p, g, p.CapW, p.CapH, true);
            // (AFMF es del driver: no esta en el selector, pero el plan lo apunta para el asistente)
            string[] tipos = { "smooth", "dlss", "fsr3", "xess", "afmf" };
            string[] gens = { "smooth", "dlssg", "fsr3fg", "xessfg", "afmf" };
            for (int i = 0; i < tipos.Length; i++)
            {
                if (!lista.Contains(tipos[i]) && !(tipos[i] == "afmf" && gen == "afmf")) continue;
                double c = fijo + CosteGeneracion(gens[i], g, esc);
                if (c <= periodo) continue;
                lista.Remove(tipos[i]);
                p.Motivos.Add(Tx.T("Modo " + modo + ": " + NombreGen(gens[i]) + " no cabe en esta gráfica a esta salida (" + Tx.N(c, "0.0") + " de " + Tx.N(periodo, "0.0") + " ms por imagen real): fuera del selector",
                                   "Mode " + modo + ": " + NombreGen(gens[i]) + " does not fit this GPU at this output: removed from the picker"));
                if (gen == gens[i])
                {
                    if (modo == "60") { p.Gen60 = "ninguna"; p.Mult60 = 1; } else { p.Gen30 = "ninguna"; p.Mult30 = 1; }
                    gen = "ninguna";
                    if (p.Gen60 == "ninguna" && p.Gen30 == "ninguna") p.GenDeSerie = false;
                    // (y su modo con generacion del plan, con su aviso de "ni lo mas ligero cabe")
                    p.Modos.Remove(modo + "_fg");
                    string pre = Tx.T("Modo ", "Mode ") + modo + "_fg";
                    p.Avisos.RemoveAll(delegate(string a) { return a.StartsWith(pre); });
                }
            }
        }

        static string NombreGen(string g) { return g == "dlssg" ? "DLSS FG" : g == "smooth" ? "Smooth Motion" : g == "fsr3fg" ? "FSR 3 FG" : g == "xessfg" ? "XeSS FG" : g; }

        // capturas_120: la mas alta que cabe a 120 (sin generacion) con lo mas ligero en el 85 % de 8,34 ms
        static void Tope120(PlanImagen p, GpuInfo g)
        {
            double tope = UsoMotor * 1000.0 / 119.88;
            int max = 0;
            foreach (int h in new[] { 720, 1080, 1440 })
            {
                int w = ModeloCaptura.AnchoDe(h);
                if (CosteLigero(p, g, w, h) <= tope) max = h;
            }
            p.Tope120Alto = max;
            if (max < 1440 && p.Captura != null)
            {
                string fr;
                p.Tope120Alto = 4320;
                bool habia = p.Capturas120(out fr).Length > 0;
                p.Tope120Alto = max;
                if (habia)
                    p.Motivos.Add(max == 0 ? Tx.T("Modo 120: esta gráfica no llega a 120 fps con margen a esta salida (no se ofrece)", "120 mode: this GPU can't do 120 fps with headroom at this output (not offered)")
                                           : Tx.T("Modo 120: con esta gráfica, captura a 120 hasta " + max + "p (más no cabe con margen)", "120 mode: with this GPU, 120 fps capture up to " + max + "p"));
            }
        }

        // VRAM prevista con lo de serie (MB): Windows a esta salida + el motor (capturas, imagenes de salida) + DLSS + la generacion de serie
        static void Vram(PlanImagen p, GpuInfo g)
        {
            double q = Math.Max(0.25, (p.OutW * (double)p.OutH) / (3840.0 * 2160.0));
            double capMp = Math.Max(1, p.CapW * (double)p.CapH) / 1e6, outMp = Math.Max(1, p.OutW * (double)p.OutH) / 1e6;
            double mb = 300 + 300 * q;                                   // Windows y el escritorio
            if (p.Motor == "propio" || p.Motor == "mpv") mb += 250 + capMp * 4 * 6 + outMp * 8 * 5;   // motor o mpv: capturas en cola e imagenes de salida
            if (p.Escalador == "dlss") mb += (g != null && g.F.DlssTransformer.Si ? 650 : 350) * q;
            else if (p.Escalador == "xess" || p.Escalador == "fsr4") mb += 400 * q;
            string gen = p.GenDeSerie ? (p.Gen60 != "ninguna" ? p.Gen60 : p.Gen30) : "ninguna";
            if (gen == "dlssg") mb += 1100 * q; else if (gen == "fsr3fg" || gen == "xessfg") mb += 700 * q; else if (gen == "smooth") mb += 300 * q;
            p.VramPrevistaMb = mb;
            p.VramTotalMb = g != null ? g.Vram / (1024.0 * 1024.0) : 0;
            double libre = p.VramTotalMb * UsoMotor;
            if (p.VramTotalMb > 256 && mb > libre && p.GenDeSerie && gen != "ninguna")
            {
                p.GenDeSerie = false;
                p.Avisos.Add(Tx.T("VRAM: con la generación de serie se irían " + Tx.N(mb / 1024, "0.0") + " de " + Tx.N(p.VramTotalMb / 1024, "0.0") + " GB (más del 85 %): apagada de serie (el botón de captura la enciende)",
                                  "VRAM: frame gen by default would use " + Tx.N(mb / 1024, "0.0") + " of " + Tx.N(p.VramTotalMb / 1024, "0.0") + " GB (over 85%): off by default"));
            }
        }

        // El resumen de los margenes para el informe (una linea)
        static void ResumenMargenes(Hardware hw, PlanImagen p, GpuInfo g)
        {
            List<string> l = new List<string>();
            ModoPlan m;
            if (p.Modos.TryGetValue("60", out m) && m.TopeMs > 0) l.Add(Tx.T("modo 60 al ", "60 mode at ") + Math.Round(100 * m.CosteMs / (1000.0 / 59.94)) + Tx.T(" % de la gráfica (previsto)", "% GPU (estimated)"));
            if (p.Modos.TryGetValue("60_fg", out m) && m.TopeMs > 0) l.Add(Tx.T("60 con generación al ", "60 + frame gen at ") + Math.Round(100 * m.CosteMs / (1000.0 / 59.94)) + " %");
            l.Add(Tx.T("de serie ≤ 75 %, jugando ≤ ", "default <= 75%, while playing <= ") + Math.Round(UsoMotor * 100) + " %");
            if (p.VramTotalMb > 0 && g != null && !g.Integrada) l.Add("VRAM " + Tx.N(p.VramPrevistaMb / 1024, "0.0") + "/" + Tx.N(p.VramTotalMb / 1024, "0.0") + " GB");
            if (p.Tope120Alto < 1440) l.Add(p.Tope120Alto == 0 ? Tx.T("sin 120", "no 120") : Tx.T("120 hasta ", "120 up to ") + p.Tope120Alto + "p");
            if (hw != null && hw.Sistema != null && hw.Sistema.Hilos > 0 && p.CapFormato == "MJPG") l.Add("MJPEG: CPU " + hw.Sistema.Hilos + Tx.T(" hilos (hasta ", " threads (up to ") + TopeComprimido(hw) + "p)");
            if (g != null && g.IndiceEstimado && !g.Software) l.Add(Tx.T("gráfica fuera de la tabla: índice " + Tx.N(g.Indice, "0.00") + " -15 %", "GPU not in the table: index " + Tx.N(g.Indice, "0.00") + " -15%"));
            p.Margenes = string.Join("; ", l.ToArray());
        }
    }
}
