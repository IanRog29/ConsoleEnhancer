// Proteccion.cs - 22-11-2026 (1.2.4, agente XEFG). C# 5
//
// El usuario: "He puesto por ejemplo dlss 3 con xess fg x3 y iba lagueado, evita que pueda pasar algo asi" y "No entiendo porque me
// aparece en 4k el xefg x4 bloqueado cuando podria yo creo".
//
// Lo que pasaba (su log de hoy, 14:55-14:59, 1.2.3):
//  - 14:55:48 "Escalador DLSS E (DLSS 3)" con XeSS FG x4 pedido a 1440p: la app escribio x4 en Magpie y luego penso que iba x3 ("AVISO, se
//    pidio XeSS FG x3 y Magpie ha puesto x4"). La cuenta de si x4 cabe (Config.MultXessCabe) guardaba su resultado 2 s con una clave sin el
//    escalador: lo de "sin reescalado" (cabe) valio para DLSS E (no cabe). Iba x4 con DLSS E (a tirones) creyendo que era x3; la vigilancia
//    (que compara con x3) no lo vio hasta cambiar a FSR 1.
//  - 14:56-14:59 XeSS FG x4 a 1440p con FSR 1: el modelo decia que cabia y no (26,7 y 25,8 de 29,97 reales); la vigilancia bajo a x3 dos
//    veces (a los 30-130 s) y guardo x3 en el perfil, borrando lo elegido.
//  - 15:00 x4 a 4K: "bloqueado" sin mas (iba x3 en silencio, el menu decia el motivo en gris).
//
// Ahora, una proteccion para todas las combinaciones (escalador + DLSS 5 + generacion + multiplicador):
//  1) PREVISION antes de aplicarla (Config.ProteNoVa, desde MultAhora): el modelo del hilo de Magpie de EscalaAuto (ya con el coste del
//     escalador: DLSS E / K / M / L, FSR 1...; y el ritmo de compatibilidad de XeSS FG x3 / x4) estima las reales. Si quedan lejos (menos
//     del 90 %, proteccion_lejos) o esta combinacion ya no llego antes (lo recordado), se usa el multiplicador de abajo con el motivo en el
//     aviso y en el menu ("x4: no llego aqui (27 de 30 reales) · va x3"). Elegirlo a mano (menu, bandeja, Ctrl+X) es "probar": va lo elegido
//     con un aviso de lo que pasa y la vigilancia de abajo. Nada queda "bloqueado" por la grafica: solo lo que no puede ir (Hz de la pantalla,
//     VRR, el componente de DLSS FG).
//  2) VIGILANCIA despues (TrayApp.MultVigila, cada 5 s con la generacion de Magpie, tambien x2): reales por debajo del 95 % (proteccion_umbral)
//     o la salida por debajo del 92 % dos ventanas seguidas (~10 s) -> se baja por orden lo que menos se nota: el % de DLSS 5 y sus pasadas
//     (la escala automatica, EscalaAuto: va primero), el multiplicador, el modelo de DLSS SR (M / L -> K -> E) y luego la escalera de siempre
//     (FpsBajo: DLSS 5 fuera, FSR 1, captura...). Aviso corto en pantalla y en el log; lo bajado se recuerda por combinacion (juego + cadena)
//     en data\games\proteccion.txt (7 dias, proteccion_dias) y lo elegido NO se borra: al cambiar algo de la cadena, vuelve.
//     60 s llegando con algo "a prueba" -> se recuerda que si llega (la prevision ya no lo baja).
// (1.2.4 ronda 6, QUITAFG: sin multiplicadores -x3 / x4 fuera- la prevision de "x4 no va" y "a prueba" ya no hace falta; queda la
// VIGILANCIA de la generacion de Magpie -ProteVigila, antes TrayApp.MultVigila- y lo que baja: el modelo de DLSS SR y la escalera de FpsBajo,
// recordado por combinacion)
// ini: proteccion=1|0 (0 = como la 1.2.3), proteccion_umbral=95, proteccion_dias=7 (proteccion_lejos: se lee, ya no se usa)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    // Lo recordado por combinacion: "no" = no llego (con las reales medidas), "si" = llego 60 s, "modelo=E" = el modelo de DLSS SR que llego
    static class ProteMemoria
    {
        public sealed class Entrada { public string Res = ""; public double Reales, Obj; public DateTime Fecha; }
        static readonly object sync = new object();
        static Dictionary<string, Entrada> d;
        static string fichero;
        public static volatile int Version;   // (sube con cada cambio)
        public static string Fichero { get { return fichero ?? Path.Combine(Paths.Datos, @"games\proteccion.txt"); } set { lock (sync) { fichero = value; d = null; } } }

        static void Cargar()
        {
            if (d != null) return;
            d = new Dictionary<string, Entrada>();
            try
            {
                if (!File.Exists(Fichero)) return;
                foreach (string raw in File.ReadAllLines(Fichero, Encoding.UTF8))
                {
                    if (raw.Length == 0 || raw[0] == '#') continue;
                    string[] c = raw.Split('\t');
                    if (c.Length < 3) continue;
                    Entrada e = new Entrada();
                    for (int i = 1; i < c.Length; i++)
                    {
                        int eq = c[i].IndexOf('=');
                        if (eq <= 0) continue;
                        string k = c[i].Substring(0, eq), v = c[i].Substring(eq + 1);
                        double dv; DateTime t;
                        if (k == "res") e.Res = v;
                        else if (k == "reales" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) e.Reales = dv;
                        else if (k == "de" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) e.Obj = dv;
                        else if (k == "fecha" && DateTime.TryParseExact(v, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) e.Fecha = t;
                    }
                    if (e.Res.Length > 0) d[c[0]] = e;
                }
            }
            catch (Exception ex) { Log.Error("Proteccion: leyendo " + Fichero, ex); }
        }

        static void Guardar()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ConsoleEnhancer (1.2.4): combinaciones que no llegaron a las imagenes reales del modo (res=no), que si (res=si) o el modelo de DLSS SR que llego (res=modelo=E)");
                sb.AppendLine("# grafica|juego|modo|generacion+multiplicador|captura|ventana|salida|escalador|m<modelo DLSS SR elegido>|d5<pasadas de DLSS 5, 0 sin el> <TAB> res= <TAB> reales= <TAB> de= <TAB> fecha=");
                sb.AppendLine("# (la app lo usa 7 dias; borrar = olvidar)");
                foreach (KeyValuePair<string, Entrada> kv in d)
                    sb.Append(kv.Key).Append("\tres=").Append(kv.Value.Res).Append("\treales=").Append(kv.Value.Reales.ToString("0.0", CultureInfo.InvariantCulture))
                      .Append("\tde=").Append(kv.Value.Obj.ToString("0.00", CultureInfo.InvariantCulture)).Append("\tfecha=").Append(kv.Value.Fecha.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).AppendLine();
                Directory.CreateDirectory(Path.GetDirectoryName(Fichero));
                string tmp = Fichero + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(Fichero)) File.Replace(tmp, Fichero, null); else File.Move(tmp, Fichero);
            }
            catch (Exception ex) { Log.Error("Proteccion: guardando " + Fichero, ex); }
        }

        public static Entrada Mira(string clave)
        {
            lock (sync)
            {
                Cargar();
                Entrada e;
                if (!d.TryGetValue(clave, out e)) return null;
                if ((DateTime.Now - e.Fecha).TotalDays > Config.ProteDias) return null;
                return e;
            }
        }

        public static void Anota(string clave, string res, double reales, double obj)
        {
            lock (sync)
            {
                Cargar();
                Entrada e = new Entrada { Res = res, Reales = reales, Obj = obj, Fecha = DateTime.Now };
                d[clave] = e;
                // (lo de mas de proteccion_dias se va al guardar)
                List<string> viejas = new List<string>();
                foreach (KeyValuePair<string, Entrada> kv in d) if ((DateTime.Now - kv.Value.Fecha).TotalDays > Config.ProteDias) viejas.Add(kv.Key);
                foreach (string k in viejas) d.Remove(k);
                Guardar();
                Version++;
            }
        }

        public static void Olvida(string clave) { lock (sync) { Cargar(); if (d.Remove(clave)) { Guardar(); Version++; } } }
    }

    partial class Config
    {
        public static volatile bool ProteOn = true;      // proteccion
        public static double ProteUmbral = 0.95, ProteLejos = 0.90;   // proteccion_umbral / proteccion_lejos (de las reales del modo)
        public static int ProteDias = 7;                 // proteccion_dias

        public bool CeParseProte(string key, string val)
        {
            string v = val.Trim().TrimEnd('%', ' ');
            double d; int i;
            switch (key)
            {
                case "proteccion": ProteOn = !(v == "0" || v.ToLowerInvariant() == "no"); return true;
                case "proteccion_umbral": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) ProteUmbral = Math.Max(0.5, Math.Min(1.0, d > 1 ? d / 100 : d)); return true;
                case "proteccion_lejos": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) ProteLejos = Math.Max(0.3, Math.Min(1.0, d > 1 ? d / 100 : d)); return true;
                case "proteccion_dias": if (int.TryParse(v, out i)) ProteDias = Math.Max(0, Math.Min(365, i)); return true;
            }
            return CeParseXefgm(key, val);   // (1.2.4 ronda 5, XEFGM: motor_xefg_dlss5, motor_propia_dlss5; XefgMotor.cs)
        }

        // Las imagenes reales por segundo del modo (la captura va a 60000/1001)
        public static double ModoBase(bool cap30) { return cap30 ? 29.97 : 59.94; }

        // La cadena con el tipo de generacion, para la clave (1.2.4 ronda 6: siempre x2)
        public CadenaEscala ProteCadena(bool cap30, string tipo)
        {
            CadenaEscala c = CadenaEscalaPara(1);
            c.Gen = tipo; c.Mult = 2;
            c.Modo = cap30 ? 30 : 60; c.Objetivo = ModoBase(cap30);
            return c;
        }

        // La clave de una combinacion: la grafica, el juego, la cadena (modo, generacion + multiplicador, captura, ventana, salida, escalador),
        // el modelo de DLSS SR ELEGIDO (no el que ponga la proteccion) y DLSS 5 (las pasadas pedidas o 0 si esta apagado)
        public string ProteClave(CadenaEscala c)
        {
            int d5 = !Dlss5Off && Integrado.Dlss5Available ? Math.Max(1, Math.Min(3, Passes30)) : 0;
            int mod = c.Escala && c.Esc == "dlss" ? DlssModelBaseSinD5 : 0;
            string g = Escalas.GpuClave;
            return (g.Length > 0 ? g : "-") + "|" + (string.IsNullOrEmpty(c.Juego) ? "*" : c.Juego.Replace("|", " ").Replace("\t", " ")) + "|" + c.Clave + "|m" + mod + "|d5" + d5;
        }
        // (el modelo elegido sin pasar por las pasadas de DLSS 5: lo que no cambia con la escala automatica)
        int DlssModelBaseSinD5 { get { string pk = DlssPick; if (pk.Length > 0) return DlssPickNum(pk); if (CapMode && DlssModel30 > 0) return DlssModel30; if (DlssModel > 0) return DlssModel; return 0; } }

        // El modelo de DLSS SR que llego en esta combinacion (lo bajo la vigilancia); 0 = ninguno
        public int ProteModeloTope()
        {
            if (!ProteOn) return 0;
            try
            {
                CadenaEscala c = ProteCadena(CapMode, FgOff || Mode120 ? "" : FgTypeNow);
                if (!c.Escala || c.Esc != "dlss") return 0;
                ProteMemoria.Entrada e = ProteMemoria.Mira(ProteClave(c));
                if (e == null || !e.Res.StartsWith("modelo=")) return 0;
                return EscalaAuto.Numero(e.Res.Substring(7));
            }
            catch { return 0; }
        }
    }

    partial class TrayApp
    {
        DateTime proteBienDesde = DateTime.MinValue;

        // Lo bajado por la proteccion: recordado, aviso y Magpie otra vez (lo elegido se queda)
        void ProteReabrir(string clave, string res, double real, double modo, string es, string en, string log)
        {
            ProteMemoria.Anota(clave, res, real, modo);
            Log.Write("Proteccion: " + log + " (recordado para esta combinacion en " + ProteMemoria.Fichero + "; lo elegido se guarda)");
            OnUi(delegate { OsdWarn(Tx.T(es, en), "7000"); });
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    for (int i = 0; i < 40 && Interlocked.CompareExchange(ref fgBusy, 1, 0) != 0; i++) Thread.Sleep(250);
                    try
                    {
                        PrepareCfg();
                        lastPadAction = DateTime.Now;
                        RestartLs(Tx.T("Protección", "Protection"), Tx.T(es, en), false, true);
                    }
                    finally { Interlocked.Exchange(ref fgBusy, 0); }
                }
                catch (Exception ex) { Log.Error("Proteccion: reabriendo", ex); }
            });
        }

        // (desde ProteVigila) no llega: lo siguiente por orden (el modelo de DLSS SR, la escalera de FpsBajo). true = hecho
        bool ProteBajar(string tipo, double real, double modo, double salida, double objetivo)
        {
            string rr = real.ToString("0.0", CultureInfo.InvariantCulture), ro = Math.Round(modo).ToString(CultureInfo.InvariantCulture);
            string nombre = cfg.FgName;
            CadenaEscala c = cfg.ProteCadena(cfg.CapMode, tipo);
            string clave = cfg.ProteClave(c);
            string medida = "salida " + salida.ToString("0.0", CultureInfo.InvariantCulture) + " de " + objetivo.ToString("0.0", CultureInfo.InvariantCulture) + ", reales " + rr + " de " + modo.ToString("0.00", CultureInfo.InvariantCulture);
            // 1) el modelo de DLSS SR, uno mas ligero (M / L -> K -> E)
            if (c.Escala && c.Esc == "dlss")
            {
                int ahora = cfg.DlssModelNow, nuevo = EscalaAuto.MasLigero(ahora);
                if (nuevo > 0)
                {
                    string a = EscalaAuto.Letra(ahora), b = EscalaAuto.Letra(nuevo);
                    ProteReabrir(clave, "modelo=" + b, real, modo,
                        "Va a tirones (" + rr + " de " + ro + " reales) · DLSS " + a + " → " + b,
                        "Stuttering (" + rr + " of " + ro + " real fps): DLSS " + a + " -> " + b,
                        "no se llega con DLSS " + a + " y " + nombre + " (" + medida + "): DLSS " + b);
                    return true;
                }
            }
            // 2) la escalera de siempre (DLSS 5 fuera, FSR 1, captura...)
            Log.Write("Proteccion: no se llega con " + nombre + " (" + medida + "): lo siguiente de la escalera");
            ProteMemoria.Entrada ya = ProteMemoria.Mira(clave);
            if (ya == null || !ya.Res.StartsWith("modelo=")) ProteMemoria.Anota(clave, "no", real, modo);   // (el modelo ya bajado se queda)
            FpsLanza(real, modo, 0, "Magpie");
            return true;
        }

        // ---------------- vigilancia (1.2.4 ronda 6: antes TrayApp.MultVigila, Multiplicador.cs) ----------------
        int proteMal;
        DateTime proteBajadoAt = DateTime.MinValue;

        // XeSS FG: las imagenes que saca no estan en el "Output cadence" de Magpie (generated=0: las presenta la cadena de Intel; medido el
        // 22-11, x2 a 1440p: nuevas 721, inventadas 0, y PresentMon 57,9 imagenes/s con la mitad "Intel XeSS-FG"). Si, cada ~4 s, "XeSSFG SDK
        // output: requested=2x frames=1439 submissions=720" (acumuladas): de dos seguidas, la salida (frames/s) y las reales (submissions/s)
        // Una linea "2026-11-22 05:48:24.014|info|...|XeSSFG SDK output: requested=2x frames=477 submissions=120 ..." -> hora (s), frames, submissions
        public static bool ProteXessLinea(string line, out double ts, out long frames, out long subs)
        {
            ts = 0; frames = subs = 0;
            if (line == null || line.IndexOf("XeSSFG SDK output", StringComparison.Ordinal) < 0) return false;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line, @"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+)\|.*XeSSFG SDK output: requested=\dx frames=(\d+) submissions=(\d+)");
            DateTime t;
            if (!m.Success || !DateTime.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return false;
            ts = t.Ticks / 1e7;
            frames = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            subs = long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            return true;
        }

        long proteLogPos = -1;
        double proteXeT = -1, proteXeOut = -1, proteXeReal = -1;
        long proteXeFrames = -1, proteXeSubs = -1;
        DateTime proteXeAt = DateTime.MinValue;

        void ProteLeerXess()
        {
            string path = Paths.MagpieLog;
            if (!File.Exists(path)) return;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (proteLogPos < 0 || proteLogPos > fs.Length) { proteLogPos = Math.Max(0, fs.Length - 65536); proteXeT = -1; }   // (primera vez o log rotado)
                fs.Seek(proteLogPos, SeekOrigin.Begin);
                byte[] buf = new byte[Math.Min(fs.Length - proteLogPos, 4 * 1024 * 1024)];
                int n = 0, r;
                while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0) n += r;
                if (n == 0) return;
                int end = Array.LastIndexOf(buf, (byte)'\n', n - 1);
                if (end < 0) return;
                proteLogPos += end + 1;
                foreach (string line in Encoding.UTF8.GetString(buf, 0, end + 1).Split('\n'))
                {
                    double ts; long f, s;
                    if (!ProteXessLinea(line, out ts, out f, out s)) continue;
                    if (proteXeT > 0 && f >= proteXeFrames && s >= proteXeSubs && ts > proteXeT + 1 && ts < proteXeT + 30)
                    {
                        proteXeOut = (f - proteXeFrames) / (ts - proteXeT);
                        proteXeReal = (s - proteXeSubs) / (ts - proteXeT);
                        proteXeAt = DateTime.Now;
                    }
                    proteXeT = ts; proteXeFrames = f; proteXeSubs = s;   // (si baja, es otra sesion de Magpie: desde aqui)
                }
            }
        }

        // CheckLsPacing cada 5 s con Magpie. true = ha bajado algo (lo demas no juzga esta vez). (1.2.4, XEFG: con cualquier generacion de
        // Magpie y por orden: con DLSS 5 por Magpie y su escala automatica, primero su % y sus pasadas -EscalaVigila, que va detras: aqui no se
        // juzga mientras se puedan bajar-; luego el modelo de DLSS SR y la escalera de FpsBajo. Reales por debajo del 95 % -proteccion_umbral- o
        // la salida del 92 %, dos ventanas seguidas. proteccion=0 = nada)
        bool ProteVigila(LsStats st)
        {
            try
            {
                PrepareCfg();
                string tipo = cfg.FgTypeNow;
                bool xess = tipo == "xess";
                bool fgMagpie = !cfg.FgOff && !cfg.Mode120 && fgWanted && tipo != "smooth" && tipo.Length > 0;
                if (!Config.ProteOn) { proteMal = 0; return false; }
                if (xess && fgMagpie) ProteLeerXess();
                if (!fgMagpie || Config.EngineActive || cfg.HomeNative || InHome || homeBusy || passesBusy || relaunchSize != null || relaunchEngine ||
                    userExit || !lsReady || applying || MagpieLive.RunningFor() < 20 || (DateTime.Now - proteBajadoAt).TotalSeconds < 30) { proteMal = 0; return false; }
                // (el % de DLSS 5 y sus pasadas, antes: los baja EscalaVigila)
                if (cfg.Dlss5AutoAhora && cfg.D5Tuned && !cfg.EngineDlss5Now)
                {
                    int n5 = cfg.Dlss5PassesNow, p5 = cfg.Dlss5PctNow;
                    if (n5 > 1 || p5 > EscalaAuto.Suelo(cfg.CadenaEscalaPara(Math.Max(1, n5)))) { proteMal = 0; return false; }
                }
                double fuente = FuenteFps(), modo = Config.ModoBase(cfg.CapMode);
                if (fuente < modo * 0.92) { proteMal = 0; return false; }   // (el juego no da el modo entero: no es cosa del PC)
                int hz = Pantalla.Hz();
                double salida, real;
                if (xess)
                {
                    if ((DateTime.Now - proteXeAt).TotalSeconds > 6 || proteXeOut <= 0) return false;   // (sin dato nuevo: se sigue la cuenta)
                    salida = proteXeOut; real = proteXeReal;
                    proteXeAt = DateTime.MinValue;   // (cada dato una vez)
                }
                else
                {
                    if (st.CapN == 0 || st.CapTime <= 0) return false;
                    salida = (st.NewSum + st.GenSum) / st.CapTime; real = st.NewSum / st.CapTime;
                    if (st.GenSum == 0) salida = real * 2;   // (FSR 3 FG: sin inventadas en el resumen, solo las reales)
                }
                double objetivo = Math.Min(modo * 2, hz >= 60 ? hz - 0.2 : modo * 2);
                bool mal = salida < objetivo * 0.92 || real < modo * Config.ProteUmbral;
                proteMal = mal ? proteMal + 1 : 0;
                if (!mal || proteMal < 2) return false;
                proteMal = 0;
                proteBajadoAt = DateTime.Now;
                return ProteBajar(tipo, real, modo, salida, objetivo);
            }
            catch (Exception ex) { Log.Error("Proteccion (vigilancia)", ex); return false; }
        }
    }
}
