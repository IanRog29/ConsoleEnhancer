// Escalas.cs - escalas que se adaptan a cada jugador (20-11-2026, agente CE-FUNCIONES, ConsoleEnhancer)
//
// El usuario: "el console enhancer lo que tiene es que se adapta a los jugadores asique no hagas las escalas como haces conmigo en mi app
// propia". En Prisma las tablas (tabla_modelo_*, tabla_dlss5_*, motor_dlss5_entrada_*, motor_modelo_60_dlssg) estan medidas a mano en la
// grafica del usuario (RTX 4060 Ti, tele 4K). Aqui:
//  1) ESTIMACION por la gama de la grafica: el asistente escribe esas claves en el bloque del plan a partir de las medidas de referencia
//     (Referencia, abajo: las de la 4060 Ti, indice 1,2 a 4K) escaladas por el indice de la grafica (Gpu.cs, RTX 4060 = 1,0) y la salida,
//     con margen (prudente). Modelo de coste: a la referencia el 40 % del tiempo es fijo (captura, DLSS SR, generacion: escala con la
//     salida) y el 60 % es DLSS 5 (escala con el cuadrado del % de entrada). Lo que en la referencia no cabia (-1) cuenta como ~20 %.
//  2) EN MARCHA, motor propio: el motor ajusta % de DLSS 5, pasadas y modelo de DLSS SR con el tiempo de grafica medido, bajando y
//     subiendo con histeresis, y lo recuerda (src\Motor\Engine\EngineAdapta.cs; aqui solo los argumentos adapta=1 adapta_clave=...).
//  3) EN MARCHA, mpv + Magpie (no cambia en caliente): con DLSS 5 por Magpie, si Magpie recibe menos imagenes reales de las que da mpv
//     15 s seguidos, la escala de ese modo baja 10 puntos para la proxima apertura (y al 25 %, una pasada menos); tras 10 min sin
//     perder nada sube 5 (si no se sabe ya que mas no cabe). Se guarda en data\scales_magpie.txt por grafica + driver y se aplica
//     al arrancar (Config.Dlss5PctTable).
//     21-11 (1.2.1): con dlss5_escala_auto=1 (de serie) el % de DLSS 5 por Magpie ya no sale de estas tablas sino de la cadena entera
//     (src\Lanzador\EscalaAuto.cs: captura, ventana, salida, modo 30/60/120, generacion, modelo de DLSS SR, pasadas y grafica) y se baja
//     en caliente si no se llega a los fps; 3) queda solo con dlss5_escala_auto=0. El plan escribe ademas escala_gpu_indice /
//     escala_gpu_familia / escala_gpu_vram (la grafica para ese modelo).
// Claves: escalas_adaptables=1 (0 = lo del ini fijo, como Prisma), escalas_modelo_max=auto|pedido|L|M|K|E (hasta donde puede subir el
// motor el modelo de DLSS SR; auto lo escribe el plan: L con modelos transformer, E sin ellos).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public bool CeEscalas = true;            // escalas_adaptables
        public string CeModeloMax = "auto";      // escalas_modelo_max
        // (1.2.2, UNIVERSAL) motor_uso_grafica: % del tiempo de una imagen que el motor deja usar a la grafica jugando (el tope de su p95,
        // menos la generacion de NVIDIA: src\Motor\Engine\EngineMargen.cs). 85 de serie; 93 ~ como antes de la 1.2.2
        public double CeUsoGrafica = 90;   // (1.2.4, XEFG: antes 85)

        bool CeParseEscalas(string key, string val)
        {
            switch (key)
            {
                case "escalas_adaptables": { string v = val.Trim().ToLowerInvariant(); CeEscalas = !(v == "0" || v == "no" || v == "false" || v == "off"); return true; }
                case "escalas_modelo_max": CeModeloMax = val.Trim().ToUpperInvariant(); return true;
                case "motor_uso_grafica":
                    {
                        double d;
                        if (double.TryParse(val.Trim().TrimEnd('%', ' ').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0)
                            CeUsoGrafica = Math.Max(50, Math.Min(98, d <= 1 ? d * 100 : d));
                        return true;
                    }
            }
            return CeParseEscalaAuto(key, val);   // 21-11 (1.2.1): dlss5_escala_auto, dlss5_escala_uso, escala_gpu_* (src\Lanzador\EscalaAuto.cs)
        }

        // La clave de la tabla de DLSS 5 de ahora (PctKey es privada de Config: aqui se puede por ser la misma clase)
        public string CePctKey { get { return PctKey; } }
    }

    static class Escalas
    {
        const double IndiceRef = 1.2;      // RTX 4060 Ti (Gpu.cs: "RTX 4060 TI" 1.2)
        const double Fijo = 0.4;           // parte del tiempo que no es DLSS 5 en la referencia
        const double Prudencia = 0.9;      // las estimaciones fallan: un 10 % menos de grafica de la que dice el indice

        // Medido en Prisma (RTX 4060 Ti con OC, tele 4K 120 Hz, imagen de Ocarina en movimiento; 14-19/11): % de entrada de DLSS 5 por
        // pasadas (1,2,3); -1 = no cabe. Ver el LEEME.txt de Prisma (Switch2Magpie) y los comentarios de Config.Dlss5PctTable
        static readonly string[][] RefDlss5 =
        {
            new[] { "60_fg", "40,25,-1" }, new[] { "60_fg_1080", "55,35,-1" }, new[] { "60_fg_4k", "-1,-1,-1" },
            new[] { "60", "60,45,35" }, new[] { "60_1080", "90,70,50" }, new[] { "60_720", "65,45,35" }, new[] { "60_4k", "25,-1,-1" },
            new[] { "60_sm", "40,30,-1" }, new[] { "60_sm_1080", "65,55,35" }, new[] { "60_sm_720", "45,40,-1" },
            new[] { "30_fg", "45,65,45" }, new[] { "30_fg_1080", "90,60,50" }, new[] { "30_fg_720", "45,65,45" }, new[] { "30_fg_4k", "60,35,25" },
            new[] { "30", "85,50,40" }, new[] { "30_1080", "100,75,60" }, new[] { "30_4k", "75,45,35" },
            new[] { "30_sm", "100,70,55" },
            new[] { "30_xess", "70,45,35" }, new[] { "30_xess_4k", "35,25,-1" },
            new[] { "30_fsr3", "75,50,40" }, new[] { "30_fsr3_1080", "95,70,55" }, new[] { "30_fsr3_4k", "40,25,-1" },
        };
        // Modelo de DLSS SR de Magpie { sin DLSS 5, con 1 / 2 / 3 pasadas } (5 E, 11 K, 12 L, 13 M). Los de 30_fg con captura son de calidad
        // (GTA: K y no L sin flujo optico), no de coste: se quedan salvo que la grafica no llegue
        static readonly string[][] RefModelo =
        {
            new[] { "60_fg", "5,5,5,5" }, new[] { "60", "11,5,5,5" }, new[] { "30_fg", "12,12,12,12" }, new[] { "30", "12,12,12,12" },
            new[] { "30_fg_1440", "12,11,11,11" }, new[] { "30_fg_1080", "12,11,11,11" }, new[] { "30_fg_720", "12,11,11,11" },
            new[] { "30_xess", "12,5,5,5" }, new[] { "30_fsr3", "12,5,5,5" },
        };
        // DLSS 5 en el motor propio, modo 30 (1, 2, 3 pasadas): captura 1080p/1440p y 4K (motor_dlss5_entrada_30 / _30_4k)
        static readonly int[] RefMotor30 = { 100, 100, 75 }, RefMotor30_4k = { 75, 60, 45 };

        static int Numero(char l) { return l == 'L' ? 12 : l == 'M' ? 13 : l == 'K' ? 11 : 5; }
        static int Rango(int n) { return n == 12 ? 3 : n == 13 ? 2 : n == 11 || n == 10 ? 1 : 0; }

        // La velocidad frente a la referencia, con margen
        static double Velocidad(GpuInfo g) { return g == null ? 0 : Math.Max(0.05, g.Indice) / IndiceRef * Prudencia; }

        // Un % de la referencia para esta grafica y esta salida (q = pixeles de salida / 4K)
        public static int Pct(int pRef, double r, double q)
        {
            double p = pRef < 0 ? 20 : pRef;
            double libre = (r - Fijo * q) / (1 - Fijo);
            if (libre <= 0) return -1;
            double v = p * Math.Sqrt(libre);
            if (v > 100) v = 100;
            int n = (int)(Math.Floor(v / 5) * 5);
            // (entre 20 y 25: se prueba al 25, el minimo; si no llega, la app lo quita la siguiente vez: Muestra)
            return n >= 25 ? n : v >= 20 ? 25 : -1;
        }

        // El bloque del plan con las escalas estimadas (PlanComoIni). Solo con DLSS (NVIDIA RTX): sin el, nada de esto se usa
        public static string BloqueIni(PlanImagen p)
        {
            GpuInfo g = p.Gpu;
            StringBuilder sb = new StringBuilder();
            if (g == null || !g.F.DlssSr.Si)
            {
                sb.AppendLine("# Escalas de DLSS / DLSS 5: no se usan con esta grafica");
                return sb.ToString();
            }
            double r = Velocidad(g);
            double q = p.OutW > 0 && p.OutH > 0 ? Math.Min(1.0, (p.OutW * (double)p.OutH) / (3840.0 * 2160.0)) : 1.0;
            sb.AppendLine("# ===== Escalas estimadas para " + g.Nombre + " (indice " + g.Indice.ToString("0.##", CultureInfo.InvariantCulture) +
                          (g.IndiceEstimado ? ", estimado por la familia" : "") + "; salida " + p.OutW + "x" + p.OutH + ") =====");
            sb.AppendLine("# Punto de partida: lo medido en una RTX 4060 Ti (indice 1,2) a 4K escalado por la grafica, con margen. Con escalas_adaptables=1");
            sb.AppendLine("# el motor las ajusta solo en marcha (data\\scales_engine.txt) y con Magpie se corrigen de una vez a la siguiente");
            sb.AppendLine("# (data\\scales_magpie.txt). Puedes fijarlas aqui; borrar esos ficheros = volver a empezar por estas.");
            foreach (string[] e in RefDlss5)
            {
                string[] f = e[1].Split(',');
                int[] v = new int[3];
                for (int i = 0; i < 3; i++) v[i] = Pct(int.Parse(f[i], CultureInfo.InvariantCulture), r, q);
                // con menos de 8 GB, una pasada (Plan: Dlss5PasadasMax)
                if (p.Dlss5PasadasMax > 0 && p.Dlss5PasadasMax < 2) { v[1] = -1; v[2] = -1; }
                sb.AppendLine("tabla_dlss5_" + e[0] + "=" + v[0] + "," + v[1] + "," + v[2]);
            }
            bool transformer = g.F.DlssTransformer.Si;
            foreach (string[] e in RefModelo)
            {
                string[] f = e[1].Split(',');
                int[] v = new int[f.Length];
                for (int i = 0; i < f.Length; i++) v[i] = int.Parse(f[i], CultureInfo.InvariantCulture);
                // sin DLSS 5: como mucho el modelo que el plan ve caber en ese modo
                ModoPlan m;
                string modo = e[0].StartsWith("60_fg") ? "60_fg" : e[0].StartsWith("60") ? "60" : e[0] == "30" ? "30" : "30_fg";
                if (p.Modos.TryGetValue(modo, out m) && m.Modelo.Length > 0 && Rango(Numero(m.Modelo[0])) < Rango(v[0])) v[0] = Numero(m.Modelo[0]);
                if (!transformer) for (int i = 0; i < v.Length; i++) v[i] = 5;
                // con DLSS 5 y una grafica mas lenta que la referencia: E (lo que menos cuesta)
                if (r < 0.75) for (int i = 1; i < v.Length; i++) v[i] = 5;
                sb.Append("tabla_modelo_").Append(e[0]).Append('=');
                for (int i = 0; i < v.Length; i++) sb.Append(i > 0 ? "," : "").Append(v[i].ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
            }
            // 21-11 (1.2.1): la grafica para la escala de DLSS 5 automatica (EscalaAuto.cs; sin estas lineas la lee de data\hardware.txt)
            string fam = g.Familia == FamiliaGpu.NvidiaRtx20 ? "rtx20" : g.Familia == FamiliaGpu.NvidiaRtx30 ? "rtx30" : g.Familia == FamiliaGpu.NvidiaRtx40 ? "rtx40" :
                         g.Familia == FamiliaGpu.NvidiaRtx50 ? "rtx50" : "otra";
            sb.AppendLine("escala_gpu_indice=" + g.Indice.ToString("0.##", CultureInfo.InvariantCulture));
            sb.AppendLine("escala_gpu_familia=" + fam);
            sb.AppendLine("escala_gpu_vram=" + g.VramGb.ToString("0.#", CultureInfo.InvariantCulture));
            sb.AppendLine("motor_dlss5_entrada_30=" + Lista(RefMotor30, r, 1.0, p.Dlss5PasadasMax));
            sb.AppendLine("motor_dlss5_entrada_30_4k=" + Lista(RefMotor30_4k, r, 1.0, p.Dlss5PasadasMax));
            // motor con DLSS-G en el modo 60: el modelo del plan para el 60 con generacion (en la Ti la grafica sobraba con M)
            ModoPlan m60;
            if (p.Modos.TryGetValue("60_fg", out m60) && m60.Modelo.Length > 0) sb.AppendLine("motor_modelo_60_dlssg=" + m60.Modelo);
            sb.AppendLine("escalas_modelo_max=" + (transformer ? "L" : "E"));
            // 22-11 (1.2.2, UNIVERSAL): con una grafica bastante mas lenta que la de referencia (RTX 3060, 20, 3050, 4050...), el modo "ligero"
            // de Magpie (antiparpadeo estatico, flujo optico en rendimiento, DLSS FG preset C) tambien en el modo 30: en la Ti solo hacia falta
            // en el 60 (Config.LightTable); EscalaAuto ya cuenta lo que ahorra. Mas fps de margen antes que un poco de nitidez
            if (r < 0.7)
            {
                sb.AppendLine("# Grafica lenta para la referencia: modo ligero de Magpie tambien en el modo 30 (tabla_ligero_*; 0 = como en la Ti)");
                sb.AppendLine("tabla_ligero_30_fg=1");
                sb.AppendLine("tabla_ligero_30=1");
                sb.AppendLine("tabla_ligero_30_fg_4k=1");
                sb.AppendLine("tabla_ligero_30_4k=1");
            }
            return sb.ToString();
        }

        static string Lista(int[] refs, double r, double q, int pasadasMax)
        {
            int[] v = new int[3];
            for (int i = 0; i < 3; i++) { v[i] = Pct(refs[i], r, q); if (v[i] < 25) v[i] = 25; }   // (el motor no baja de 25: ahi ya quita pasadas el solo)
            return v[0] + "," + v[1] + "," + v[2];
        }

        // ------------------------------------------------------------------ al motor
        public static void ArgsMotor(Config cfg, List<string> a)
        {
            // (1.2.2, UNIVERSAL) el margen de grafica del motor y la grafica (para lo que se lleva la generacion de NVIDIA): EngineMargen.cs
            a.Add("uso_grafica=" + (cfg.CeUsoGrafica / 100.0).ToString("0.00", CultureInfo.InvariantCulture));
            try
            {
                CadenaEscala gc = new CadenaEscala();
                EscalaAuto.GpuPara(gc);
                if (gc.Indice > 0.05) a.Add("gpu_indice=" + gc.Indice.ToString("0.00", CultureInfo.InvariantCulture));
            }
            catch { }
            if (!cfg.CeEscalas) return;
            a.Add("adapta=1");
            string g = GpuClave;
            if (g.Length > 0) a.Add("adapta_clave=" + g);
            a.Add("adapta_fichero=" + Path.Combine(Paths.Datos, "scales_engine.txt"));
            string t = cfg.CeModeloMax;
            if (t == "L" || t == "M" || t == "K" || t == "E") a.Add("adapta_techo=" + t);
        }

        // Grafica + driver (la primera parte de la huella de data\hardware.txt: VID:DID:driver)
        static string gpuClave;
        public static string GpuClave
        {
            get
            {
                if (gpuClave != null) return gpuClave;
                string r = "";
                try
                {
                    if (File.Exists(Ce.HardwareFile))
                        foreach (string l in File.ReadAllLines(Ce.HardwareFile))
                            if (l.StartsWith("huella=")) { string h = l.Substring(7); int i = h.IndexOf('|'); r = (i > 0 ? h.Substring(0, i) : h).Replace("\t", " ").Trim(); break; }
                }
                catch { }
                if (r == "-") r = "";
                gpuClave = r;
                return r;
            }
        }

        // ------------------------------------------------------------------ camino de Magpie: lo aprendido
        sealed class FilaMagpie { public int Pct = -2, NoPct; public double MinBien; public string Fecha = ""; }
        static Dictionary<string, FilaMagpie> tabla;
        static string Fichero { get { return Path.Combine(Paths.Datos, "scales_magpie.txt"); } }

        static void Cargar()
        {
            if (tabla != null) return;
            tabla = new Dictionary<string, FilaMagpie>();
            try
            {
                if (!File.Exists(Fichero)) return;
                foreach (string raw in File.ReadAllLines(Fichero))
                {
                    if (raw.Length == 0 || raw[0] == '#') continue;
                    string[] c = raw.Split('\t');
                    if (c.Length < 2) continue;
                    FilaMagpie f = new FilaMagpie();
                    for (int i = 1; i < c.Length; i++)
                    {
                        int eq = c[i].IndexOf('=');
                        if (eq <= 0) continue;
                        string k = c[i].Substring(0, eq), v = c[i].Substring(eq + 1);
                        int n; double d;
                        if (k == "pct" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) f.Pct = n;
                        else if (k == "no_pct" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) f.NoPct = n;
                        else if (k == "min_bien" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) f.MinBien = d;
                        else if (k == "fecha") f.Fecha = v;
                    }
                    tabla[c[0]] = f;
                }
            }
            catch (Exception ex) { Log.Error("Escalas: leyendo " + Fichero, ex); }
        }

        static void Guardar()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ConsoleEnhancer: % de entrada de DLSS 5 por Magpie que cabe en este PC (lo corrige la app sola; borrar = volver a la estimacion)");
                sb.AppendLine("# grafica+driver|clave de la tabla|pasadas <TAB> pct (-1 = esas pasadas no caben) <TAB> no_pct (no cabe) <TAB> minutos bien");
                foreach (KeyValuePair<string, FilaMagpie> kv in tabla)
                    sb.Append(kv.Key).Append("\tpct=").Append(kv.Value.Pct.ToString(CultureInfo.InvariantCulture))
                      .Append(kv.Value.NoPct > 0 ? "\tno_pct=" + kv.Value.NoPct.ToString(CultureInfo.InvariantCulture) : "")
                      .Append("\tmin_bien=").Append(kv.Value.MinBien.ToString("0.#", CultureInfo.InvariantCulture))
                      .Append("\tfecha=").Append(kv.Value.Fecha).AppendLine();
                Directory.CreateDirectory(Paths.Datos);
                File.WriteAllText(Fichero, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Error("Escalas: guardando " + Fichero, ex); }
        }

        // Al arrancar (tras leer el ini): lo aprendido de esta grafica a Config.Dlss5PctTable
        public static void Aplicar(Config cfg)
        {
            // (1.2.1) la escala de DLSS 5 por Magpie: automatica por la cadena entera (EscalaAuto.cs) o las tablas de aqui
            // (1.2.3 REVISION3: con lo elegido, no con Dlss5AutoAhora, que es falso mientras DLSS 5 lo hace el motor -y la linea decia "las tablas
            // (dlss5_escala_auto=0)"-; y los dos presupuestos de MULTI2: el 95 % es el del hilo de Magpie, no de la grafica)
            Log.Write("Escala de DLSS 5 por Magpie: " + (cfg.Dlss5AutoOn && cfg.Dlss5InputPct == 0 ? "automatica por la cadena (dlss5_escala_auto=1, " + EscalaAuto.Fichero + "; hilo de Magpie " +
                      Math.Round(EscalaAuto.Uso * 100) + " %, grafica " + Math.Round(EscalaAuto.UsoGrafica * 100) + " %)" : cfg.Dlss5InputPct != 0 ? "fija (dlss5_entrada)" : "las tablas tabla_dlss5_* (dlss5_escala_auto=0)") +
                      (cfg.MotorDlss5Pedido ? "; ahora DLSS 5 lo hace el motor propio (engine\\PrismaNr.dll)" : ""));
            if (!cfg.CeEscalas) return;
            Cargar();
            string g = GpuClave + "|";
            int n = 0;
            foreach (KeyValuePair<string, FilaMagpie> kv in tabla)
            {
                if (!kv.Key.StartsWith(g, StringComparison.Ordinal) || kv.Value.Pct == -2) continue;
                string[] c = kv.Key.Substring(g.Length).Split('|');
                int pas;
                if (c.Length != 2 || !c[1].StartsWith("p") || !int.TryParse(c[1].Substring(1), out pas) || pas < 1 || pas > 3) continue;
                int[] t;
                if (!cfg.Dlss5PctTable.TryGetValue(c[0], out t)) { t = new[] { -1, -1, -1 }; }
                else t = (int[])t.Clone();
                t[pas - 1] = kv.Value.Pct;
                cfg.Dlss5PctTable[c[0]] = t;
                n++;
            }
            if (n > 0) Log.Write("Escalas: " + n + " escala(s) de DLSS 5 por Magpie aprendidas en este PC (" + Fichero + ")");
        }

        // Una muestra de cada 5 s del log de Magpie (TrayApp.CeEscalasMagpie). Devuelve el aviso para la pantalla ("" = nada)
        static int mal, bien;
        static string claveVista = "";
        public static string Muestra(Config cfg, bool juzgar, bool cabe, bool justo, out bool cambiado)
        {
            cambiado = false;
            if (!cfg.CeEscalas || !juzgar) { mal = bien = 0; return ""; }
            Cargar();
            int pas = Math.Max(1, Math.Min(3, cfg.Dlss5PassesNow));
            string pk = cfg.CePctKey;
            string k = GpuClave + "|" + pk + "|p" + pas;
            if (k != claveVista) { claveVista = k; mal = bien = 0; }
            int[] t;
            if (!cfg.Dlss5PctTable.TryGetValue(pk, out t) || t[pas - 1] < 25) return "";
            int pct = t[pas - 1];
            FilaMagpie f;
            if (!tabla.TryGetValue(k, out f)) { f = new FilaMagpie(); f.Pct = pct; tabla[k] = f; }
            mal = justo ? mal + 1 : 0;
            if (cabe) { bien++; f.MinBien += 5 / 60.0; } else bien = 0;
            if (mal >= 3)
            {
                mal = 0;
                f.NoPct = pct;
                int nuevo = pct > 25 ? Math.Max(25, pct - 10) : -1;
                f.Pct = nuevo; f.MinBien = 0; f.Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                int[] c = (int[])t.Clone(); c[pas - 1] = nuevo; cfg.Dlss5PctTable[pk] = c;
                Guardar();
                cambiado = true;
                Log.Write("Escalas: con Magpie DLSS 5 al " + pct + " % (" + pk + ", " + pas + " pasada(s)) no llega a ritmo en este PC: la proxima vez " +
                          (nuevo > 0 ? "al " + nuevo + " %" : "con menos pasadas"));
                return nuevo > 0 ? "DLSS 5 al " + pct + " % no llega aquí · la próxima vez al " + nuevo + " %" : "DLSS 5 con " + pas + " pasadas no llega aquí · la próxima vez con menos";
            }
            // 10 min seguidos sin perder nada: +5 para la proxima (si no se sabe ya que eso no cabe)
            if (f.MinBien >= 10 && pct < 100 && (f.NoPct == 0 || pct + 5 < f.NoPct))
            {
                int nuevo = Math.Min(100, pct + 5);
                f.Pct = nuevo; f.MinBien = 0; f.Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                int[] c = (int[])t.Clone(); c[pas - 1] = nuevo; cfg.Dlss5PctTable[pk] = c;
                Guardar();
                cambiado = true;
                Log.Write("Escalas: con Magpie DLSS 5 al " + pct + " % (" + pk + ", " + pas + " pasada(s)) va sobrado 10 min: la proxima vez al " + nuevo + " %");
            }
            else if (bien > 0 && bien % 24 == 0) Guardar();   // (cada 2 min, los minutos bien)
            return "";
        }
    }

    partial class TrayApp
    {
        // CheckLsPacing (cada 5 s con Magpie): con DLSS 5 por Magpie, si llega a ritmo
        void CeEscalasMagpie(LsStats st)
        {
            if (cfg.Dlss5AutoAhora) return;   // (1.2.1: con la escala automatica lo aprende y lo cambia EscalaAuto.cs, tambien en caliente)
            try
            {
                bool juzgar = st.Captured > 0 && cfg.D5Tuned && !Config.EngineActive && !cfg.HomeNative && MagpieLive.RunningFor() > 20 &&
                              (cfg.CapMode ? vfFps >= 29 : vfFps >= 59) && !passesBusy && relaunchSize == null;
                double esperado = cfg.CapMode ? Math.Min(29.97, vfFps) : vfFps;
                bool justo = st.Captured < esperado - (cfg.CapMode ? 1.0 : 2.0);
                bool cabe = !justo && st.Captured >= esperado - 0.2 && st.Gaps == 0;
                bool cambiado;
                string aviso = Escalas.Muestra(cfg, juzgar, cabe, justo, out cambiado);
                if (aviso.Length > 0) Osd(aviso);
            }
            catch (Exception ex) { Log.Error("Escalas (Magpie)", ex); }
        }
    }
}
