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
// ini: proteccion=1|0 (0 = como la 1.2.3), proteccion_umbral=95, proteccion_lejos=90, proteccion_dias=7
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

        // La cadena con el multiplicador m y el tipo, para la prevision y la clave (sin la regla de la grafica dentro: multGpuDentro)
        public CadenaEscala ProteCadena(bool cap30, int m, string tipo)
        {
            bool antes = multGpuDentro;
            multGpuDentro = true;
            try
            {
                CadenaEscala c = CadenaEscalaPara(1);
                c.Gen = tipo; c.Mult = m;
                c.Modo = cap30 ? 30 : 60; c.Objetivo = MultBase(cap30);
                return c;
            }
            finally { multGpuDentro = antes; }
        }

        // La clave de una combinacion: la grafica, el juego, la cadena (modo, generacion + multiplicador, captura, ventana, salida, escalador),
        // el modelo de DLSS SR ELEGIDO (no el que ponga la proteccion) y DLSS 5 (las pasadas pedidas o 0 si esta apagado)
        public string ProteClave(CadenaEscala c)
        {
            int d5 = !Dlss5Off && Integrado.Dlss5Available ? Math.Max(1, Math.Min(3, Passes30)) : 0;
            int mod = c.Escala && c.Esc == "dlss" ? DlssModelBaseSinD5 : 0;
            string g = Escalas.GpuClave;
            return (g.Length > 0 ? g : "-") + "|" + (string.IsNullOrEmpty(c.Juego) ? "*" : c.Juego.Replace("|", " ").Replace("\t", " ")) + "|" + c.Clave + "|m" + mod + "|d5" + d5 + ProteVia(c.Gen, c.Mult);
        }
        // (1.2.4 ronda 5, XEFGM) por donde va esa generacion: "|vpropia" (la generacion propia del motor), "|vmotor" (XeSS FG / DLSS FG x3 / x4
        // del motor) o nada (Magpie). En la prueba del usuario la generacion propia x4 (12,7 de 30 reales) quedo apuntada como "XeSS FG x4" y
        // despues avisaba "XeSS FG x4 a prueba" con lo de la propia; lo medido con una no vale para la otra
        string ProteVia(string tipo, int m)
        {
            if (m <= 2 || !MultTipo(tipo)) return "";
            string v = MotorFgViaAhora(tipo, m);
            return v == "propia" ? "|vpropia" : v.Length > 0 ? "|vmotor" : "";
        }
        // (el modelo elegido sin pasar por las pasadas de DLSS 5: lo que no cambia con la escala automatica)
        int DlssModelBaseSinD5 { get { string pk = DlssPick; if (pk.Length > 0) return DlssPickNum(pk); if (CapMode && DlssModel30 > 0) return DlssModel30; if (DlssModel > 0) return DlssModel; return 0; } }

        // Las reales previstas con esta cadena, sin DLSS 5 (la escala automatica le da lo que quede o lo quita). XeSS FG x3 / x4 de
        // compatibilidad: 1 / (m x W); lo demas: si el hilo pasa del tiempo de una real, 1 / W
        public static double ProteReales(CadenaEscala c, out double w, out double presupuesto)
        {
            double k = EscalaAuto.KDe(c.Juego, c.Clave);
            w = EscalaAuto.Prevision(c, 0, 0, k, 0);
            presupuesto = EscalaAuto.Presupuesto(c);
            if (EscalaAuto.XessCompat(c))
            {
                w = EscalaAuto.XessCompatW(c, k);
                return Math.Min(c.Objetivo, 1000.0 / Math.Max(1, c.Mult * w));
            }
            return Math.Min(c.Objetivo, 1000.0 / Math.Max(1, w));
        }

        // Explicitamente elegido en esta sesion (menu, bandeja, Ctrl+X): se prueba aunque la prevision o lo recordado digan que no
        static readonly HashSet<string> protePrueba = new HashSet<string>();
        static readonly object protePruebaSync = new object();
        public void ProtePrueba(bool cap30, int m, string tipo)
        {
            if (!ProteOn || m <= 2 || !MultTipo(tipo)) return;
            string k = ProteClave(ProteCadena(cap30, m, tipo));
            lock (protePruebaSync) { protePrueba.Add(k); pruebaUltima = (cap30 ? 30 : 60) + "|" + m + "|" + tipo; pruebaAt = DateTime.UtcNow; }
            ProteMemoria.Version++;
        }
        // (lo elegido con otra captura en el mismo menu: la captura cambia despues; 90 s vale para la cadena que salga)
        static string pruebaUltima = "";
        static DateTime pruebaAt = DateTime.MinValue;
        public static void ProtePruebaFuera(string clave) { lock (protePruebaSync) { protePrueba.Remove(clave); pruebaUltima = ""; } }
        static bool EnPrueba(string clave) { lock (protePruebaSync) return protePrueba.Contains(clave); }

        static string R0(double v) { return Math.Round(v).ToString(CultureInfo.InvariantCulture); }

        // Por que x m NO va de forma automatica con esto (null = va): lo recordado o la prevision lejos. es / en cortos (van en el menu)
        public string ProteNoVa(bool cap30, int m, string tipo, out string en)
        {
            en = null;
            if (!ProteOn || m <= 2 || !MultTipo(tipo) || multGpuDentro) return null;
            try
            {
                CadenaEscala c = ProteCadena(cap30, m, tipo);
                string k = ProteClave(c);
                ProteMemoria.Entrada e = ProteMemoria.Mira(k);
                lock (protePruebaSync)
                    if (pruebaUltima == (cap30 ? 30 : 60) + "|" + m + "|" + tipo && (DateTime.UtcNow - pruebaAt).TotalSeconds < 90) protePrueba.Add(k);
                if (EnPrueba(k) || (e != null && e.Res == "si")) return null;
                string o = R0(c.Objetivo);
                if (e != null && e.Res == "no")
                {
                    en = "didn't hold here (" + R0(e.Reales) + " of " + o + " real fps)";
                    return "no llegó aquí (" + R0(e.Reales) + " de " + o + " reales)";
                }
                if (!XessMultiCabe) return null;   // (xess_fg_multi_cabe=0: sin prevision)
                if (ProteMotorHace(m, tipo)) return null;   // (1.2.4, integracion: la prevision es la del hilo de Magpie; el motor vigila lo suyo)
                double w, b, r = ProteReales(c, out w, out b);
                if (r < c.Objetivo * ProteLejos)
                {
                    en = "GPU can't keep up (~" + R0(r) + " of " + o + " real fps)";
                    return "la gráfica no da (~" + R0(r) + " de " + o + " reales)";
                }
            }
            catch (Exception ex) { Log.Error("Proteccion: prevision x" + m, ex); }
            return null;
        }

        // (1.2.4, integracion) Esta generacion la haria el motor (el reparto de SoloMotor.cs / MotorFg.cs: XeSS FG x2, DLSS FG x3 / x4 en
        // RTX 50...) y DLSS 5 no la manda a Magpie: la prevision del hilo de Magpie no vale ahi (solo cuenta lo recordado; la vigilancia del
        // motor -switch2-fps bajo, ProteMotor- sigue). Sin propiedades que vuelvan a la cuenta del multiplicador (sin recursion)
        bool ProteMotorHace(int m, string tipo)
        {
            return Engine == "propio" && MotorSabeGeneracion(tipo, m) && (Dlss5Off || MotorDlss5Disponible) && !MotorGenAMagpie(tipo, m);   // (1.2.4 ronda 5, XEFGM: lo que va por Magpie, con su prevision; XefgMotor.cs)
        }

        // Aviso de "a prueba" (se elige y va, pero justo): null = sin aviso
        public string ProteAviso(bool cap30, int m, string tipo, out string en)
        {
            en = null;
            if (!ProteOn || !MultTipo(tipo) || multGpuDentro) return null;
            try
            {
                CadenaEscala c = ProteCadena(cap30, m, tipo);
                string k = ProteClave(c);
                ProteMemoria.Entrada e = ProteMemoria.Mira(k);
                if (e != null && e.Res == "si") return null;
                string o = R0(c.Objetivo);
                if (e != null && e.Res == "no")
                {
                    en = "on trial: it didn't hold here before (" + R0(e.Reales) + " of " + o + " real fps)";
                    return "a prueba: antes no llegó aquí (" + R0(e.Reales) + " de " + o + " reales)";
                }
                if (ProteMotorHace(m, tipo)) return null;   // (1.2.4, integracion: ver ProteMotorHace)
                double w, b, r = ProteReales(c, out w, out b);
                if (r < c.Objetivo * 0.97)
                {
                    en = "on trial (~" + R0(r) + " of " + o + " real fps expected)";
                    return "a prueba (previstas ~" + R0(r) + " de " + o + " reales)";
                }
            }
            catch (Exception ex) { Log.Error("Proteccion: aviso x" + m, ex); }
            return null;
        }

        // El modelo de DLSS SR que llego en esta combinacion (lo bajo la vigilancia); 0 = ninguno
        public int ProteModeloTope()
        {
            if (!ProteOn || multGpuDentro) return 0;
            try
            {
                CadenaEscala c = ProteCadena(CapMode, FgOff || Mode120 ? 2 : FgMultNow, FgOff || Mode120 ? "" : FgTypeNow);
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
        string proteBienClave = "";

        // Lo bajado por la proteccion: recordado, aviso y Magpie otra vez (lo elegido se queda)
        void ProteReabrir(string clave, string res, double real, double modo, string es, string en, string log)
        {
            ProteMemoria.Anota(clave, res, real, modo);
            Config.ProtePruebaFuera(clave);
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

        // (desde MultVigila) no llega: lo siguiente por orden (el multiplicador, el modelo de DLSS SR, la escalera de FpsBajo). true = hecho
        bool ProteBajar(int m, string tipo, double real, double modo, double salida, double objetivo)
        {
            string fs = Math.Round(salida).ToString(CultureInfo.InvariantCulture), fo = Math.Round(objetivo).ToString(CultureInfo.InvariantCulture);
            string rr = real.ToString("0.0", CultureInfo.InvariantCulture), ro = Math.Round(modo).ToString(CultureInfo.InvariantCulture);
            string nombre = cfg.FgName + cfg.MotorFgViaNota(tipo, m);   // (1.2.4 ronda 5, XEFGM: " (FG propia)" si es la del motor)
            CadenaEscala c = cfg.ProteCadena(cfg.CapMode, m, tipo);
            string clave = cfg.ProteClave(c);
            string medida = "salida " + salida.ToString("0.0", CultureInfo.InvariantCulture) + " de " + objetivo.ToString("0.0", CultureInfo.InvariantCulture) + ", reales " + rr + " de " + modo.ToString("0.00", CultureInfo.InvariantCulture);
            // 1) el multiplicador
            if (m > 2)
            {
                int nuevo = m - 1;
                while (nuevo > 2 && cfg.MultBloqueado(cfg.CapMode, nuevo, tipo)) nuevo--;
                ProteReabrir(clave, "no", real, modo,
                    "Va a tirones (" + rr + " de " + ro + " reales) · " + nombre + " x" + m + " → x" + nuevo,
                    "Stuttering (" + rr + " of " + ro + " real fps): " + nombre + " x" + m + " -> x" + nuevo,
                    "no se llega con " + nombre + " x" + m + " (" + medida + "): x" + nuevo);
                return true;
            }
            // 2) el modelo de DLSS SR, uno mas ligero (M / L -> K -> E)
            if (c.Escala && c.Esc == "dlss")
            {
                int ahora = cfg.DlssModelNow, nuevo = EscalaAuto.MasLigero(ahora);
                if (nuevo > 0)
                {
                    string a = EscalaAuto.Letra(ahora), b = EscalaAuto.Letra(nuevo);
                    ProteReabrir(clave, "modelo=" + b, real, modo,
                        "Va a tirones (" + rr + " de " + ro + " reales) · DLSS " + a + " → " + b,
                        "Stuttering (" + rr + " of " + ro + " real fps): DLSS " + a + " -> " + b,
                        "no se llega con DLSS " + a + " y " + nombre + " x" + m + " (" + medida + "): DLSS " + b);
                    return true;
                }
            }
            // 3) la escalera de siempre (DLSS 5 fuera, FSR 1, captura...)
            Log.Write("Proteccion: no se llega con " + nombre + " x" + m + " (" + medida + "): lo siguiente de la escalera");
            ProteMemoria.Entrada ya = ProteMemoria.Mira(clave);
            if (ya == null || !ya.Res.StartsWith("modelo=")) ProteMemoria.Anota(clave, "no", real, modo);   // (el modelo ya bajado se queda)
            FpsLanza(real, modo, 0, "Magpie");
            return true;
        }

        // (1.2.4) El motor propio no llega ("switch2-fps bajo", FpsBajo.cs; antes ya ha bajado lo suyo: % de DLSS 5, pasadas, modelo -EngineAdapta-):
        // con x3 / x4 en el motor (agente MOTORFG) lo siguiente es el multiplicador, recordado por combinacion como con Magpie. true = hecho
        DateTime proteMotorAt = DateTime.MinValue;
        bool ProteMotor(double real, double obj)
        {
            try
            {
                if (!Config.ProteOn || !Config.EngineActive || (DateTime.Now - proteMotorAt).TotalSeconds < 30) return false;
                PrepareCfg();
                string tipo = cfg.FgTypeNow;
                if (cfg.FgOff || cfg.Mode120 || !Config.MultTipo(tipo)) return false;
                int m = cfg.FgMultNow;
                if (m <= 2) return false;
                proteMotorAt = DateTime.Now;
                return ProteBajar(m, tipo, real, Config.MultBase(cfg.CapMode), real * m, Config.MultBase(cfg.CapMode) * m);
            }
            catch (Exception ex) { Log.Error("Proteccion (motor)", ex); return false; }
        }

        // (MultVigila, ventana buena) 60 s llegando con una combinacion "a prueba" (lo previsto o lo recordado decia que no): se recuerda que si
        void ProteBien(int m, string tipo, double real, double modo)
        {
            try
            {
                if (m <= 2 || !Config.MultTipo(tipo)) { proteBienClave = ""; return; }
                string clave = cfg.ProteClave(cfg.ProteCadena(cfg.CapMode, m, tipo));
                if (clave != proteBienClave) { proteBienClave = clave; proteBienDesde = DateTime.Now; return; }
                if ((DateTime.Now - proteBienDesde).TotalSeconds < 60) return;
                proteBienDesde = DateTime.MaxValue;   // (una vez)
                ProteMemoria.Entrada e = ProteMemoria.Mira(clave);
                string en;
                bool dudosa = (e != null && e.Res == "no") || cfg.ProteAviso(cfg.CapMode, m, tipo, out en) != null;
                if (!dudosa) return;
                ProteMemoria.Anota(clave, "si", real, modo);
                Log.Write("Proteccion: " + cfg.FgName + " x" + m + " llega (60 s, reales " + real.ToString("0.0", CultureInfo.InvariantCulture) + "): recordado para esta combinacion");
            }
            catch (Exception ex) { Log.Error("Proteccion: llega", ex); }
        }
    }
}
