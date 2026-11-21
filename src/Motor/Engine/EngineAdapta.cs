using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleEnhancer.Motor
{
    // EngineAdapta.cs - (20-11-2026, agente CE-FUNCIONES, ConsoleEnhancer) escalas que se adaptan a cada PC en marcha.
    //
    // En Prisma las escalas (modelo de DLSS SR, % de entrada y pasadas de DLSS 5) estan medidas a mano en la grafica del usuario. En
    // ConsoleEnhancer el lanzador da una estimacion por la gama de la grafica (src\Lanzador\Escalas.cs) y aqui se ajusta con el tiempo de
    // grafica medido (el p95 de cada segundo, como AutoPresetTick) y las imagenes perdidas:
    //   - NO CABE (p95 por encima del tope del modo, o se pierden imagenes en el 60) 2 s seguidos: con DLSS 5 en el motor, su % de entrada
    //     baja 10 puntos (minimo 25) y, ya en 25, una pasada menos; sin DLSS 5 (o ya en lo minimo) baja el modelo AutoPresetTick (L>M>K>E).
    //   - SOBRA (p95 por debajo del 70 % del tope y nada perdido) 20 s seguidos: sube un paso lo que se bajo, solo si lo previsto (el p95
    //     de ahora por lo que cuesta mas el paso siguiente) queda por debajo del 85 % del tope: % de DLSS 5 +5 hasta 100, luego el modelo
    //     de DLSS SR hasta adapta_techo (de serie el que pidio el lanzador). Las pasadas nunca por encima de las pedidas (son del mando).
    //   - Histéresis: tras cada cambio 8 s sin juzgar; si una subida acaba en "no cabe" en menos de 40 s, ese valor queda como techo
    //     ("no_pct" / "no_preset") de esa clave y no se vuelve a probar.
    //   - Se recuerda por clave = adapta_clave (grafica + driver, la pone el lanzador) | modo | generacion | Smooth Motion | captura |
    //     entrada | salida | DLSS 5 y pasadas, en adapta_fichero (relativo a datos=): la proxima vez se empieza por lo que cupo.
    // Ajustes: adapta=1 (de serie 0: el motor de Prisma igual que siempre), adapta_clave=, adapta_fichero=, adapta_techo=L|M|K|E.
    // Enganches (una linea cada uno): Common.cs (Settings partial + default -> ApplyCierres || ApplyCe), Engine.cs (SecondTick -> AdaptaTick
    // antes de AutoPresetTick; ApplyPending -> AdaptaTrasAjustes), EngineDlss5.cs (Dlss5GuardTick: el tope AdTope y AdaptaFreno; fusion 20-11,
    // el guardian de DLSS 5 de Prisma CIERRES como freno de emergencia sobre el mismo estado: ver AdGuardianDelante / AdaptaFreno).
    partial class Settings
    {
        public bool Adapta = false;
        public string AdaptaClave = "", AdaptaFichero = "scales_engine.txt", AdaptaTecho = "";
        public string SrDlls = "";

        // Claves de ConsoleEnhancer que el Apply de siempre no conoce (false = desconocida, como antes)
        bool ApplyCe(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "adapta": { string v = val.Trim().ToLowerInvariant(); Adapta = v == "1" || v == "si" || v == "true" || v == "on"; return true; }
                case "adapta_clave": AdaptaClave = val.Trim(); return true;
                case "adapta_fichero": AdaptaFichero = val.Trim(); return true;
                case "adapta_techo": AdaptaTecho = val.Trim().ToUpperInvariant(); return true;
                case "dlls_sr": SrDlls = val.Trim(); return true;
                case "fg_tipo": FgTipo = val.Trim().ToLowerInvariant(); return true;   // 21-11 (EngineFgFfx.cs): fsr3 = FSR 3 FG de FidelityFX   // 21-11 (EngineSr.cs): la carpeta de libxess.dll / amd_fidelityfx_*.dll (la de Magpie)
                // (vrr= y vrr_fg_vsync= ya los hace el motor: Settings.Apply de Common.cs y EngineLatencia.cs, de Prisma LATENCIA4, fusion 20-11)
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        sealed class AdaptaFila
        {
            public string Preset = "", NoPreset = "";
            public int Pct, Pasadas, NoPct;
            public string Fecha = "";
        }

        Dictionary<string, AdaptaFila> adTabla;
        string adClave = "";
        string adPedidoPreset = "";          // lo que pidio el lanzador (ini / set)
        int adPedidoPct = -1, adPedidoPasadas = -1;
        string adVistoPreset = "";            // el modelo de la vez anterior (para ver lo que bajo AutoPresetTick)
        int adMal, adBien;                    // segundos seguidos sin caber / sobrando
        long adCambioAt;                      // ultimo cambio (8 s sin juzgar)
        string adSubida = "";                 // lo ultimo subido: "pct" / "preset" ("" = nada)
        long adSubidaAt;
        int adSubidaDe; string adSubidaDePreset = "";

        static readonly string[] AdOrden = { "E", "K", "M", "L" };     // de mas ligero a mas caro
        static double AdCoste(string p) { return p == "L" ? 12.1 : p == "M" ? 10.1 : p == "K" || p == "J" ? 6.2 : 4.8; }   // ms a 4K en la RTX 4060 (solo la proporcion)
        static int AdRango(string p) { return p == "L" ? 3 : p == "M" ? 2 : p == "K" || p == "J" ? 1 : 0; }

        bool AdNrActivo { get { return s.Nr && !nrBroken && nrW > 0; } }

        string AdaptaClaveAhora()
        {
            return (s.AdaptaClave.Length > 0 ? s.AdaptaClave : "-") + "|" + s.Mode + "|fg" + (s.Fg ? 1 : 0) + "|sm" + s.SmoothMode + "|" + s.CapW + "x" + s.CapH +
                   "|in" + inW + "x" + inH + "|out" + outW + "x" + outH + "|nr" + (s.Nr ? 1 : 0) + (s.Nr ? "p" + Math.Max(1, Math.Min(3, adPedidoPasadas > 0 ? adPedidoPasadas : s.NrPasses)) : "");
        }

        double AdTope
        {
            get
            {
                // los de AutoPresetTick; con Smooth Motion (la generacion del driver no se ve aqui) algo menos
                if (s.Is120) return 8.0;   // (fusion I, Prisma HZ120: el modo 120, sin generacion; el de AutoPresetTick)
                if (s.SmoothMode > 0) return s.Mode == 30 ? 27.0 : 12.5;
                return s.Mode == 30 ? (fgApplied ? 26.0 : 31.0) : (fgApplied ? 11.0 : 15.5);
            }
        }

        // ApplyPending (tras los "set" del lanzador): lo pedido es el techo de lo que se sube; la clave nueva empieza por lo aprendido
        void AdaptaTrasAjustes(List<KeyValuePair<string, string>> p)
        {
            if (!s.Adapta) return;
            foreach (KeyValuePair<string, string> kv in p)
            {
                string k = kv.Key.Trim().ToLowerInvariant();
                int n;
                if (k == "preset") adPedidoPreset = kv.Value.Trim().ToUpperInvariant();
                else if (k == "dlss5_entrada" && int.TryParse(kv.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) adPedidoPct = Math.Max(25, Math.Min(100, n));
                else if (k == "dlss5_pasadas" && int.TryParse(kv.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) adPedidoPasadas = Math.Max(1, Math.Min(3, n));
            }
            AdaptaMirarClave(true);
        }

        // SecondTick, antes de AutoPresetTick
        void AdaptaTick()
        {
            if (!s.Adapta) return;
            try
            {
                if (adTabla == null) AdaptaCargar();
                if (adPedidoPreset.Length == 0) { adPedidoPreset = s.Preset; adPedidoPct = s.NrPct; adPedidoPasadas = s.NrPasses; adVistoPreset = s.Preset; }
                AdaptaMirarClave(false);
                // lo que haya bajado AutoPresetTick (y si fue justo tras una subida nuestra: techo)
                if (adVistoPreset.Length > 0 && AdRango(s.Preset) < AdRango(adVistoPreset))
                {
                    if (adSubida == "preset" && Clock.SinceMs(adSubidaAt) < 40000) AdaptaTecho("preset", adVistoPreset);
                    AdaptaGuardarFila();
                    adCambioAt = Clock.Now;
                }
                adVistoPreset = s.Preset;
                if (Program.FocusHold || inStandby || stats.LastGpuP95 <= 0 || lastCapture == 0 || Clock.SinceMs(lastCapture) > 1500) { adMal = adBien = 0; return; }
                if (Clock.SinceMs(adCambioAt) < 8000 || Clock.SinceMs(presetSetAt) < 5000) { adMal = adBien = 0; return; }
                double tope = AdTope, p95 = stats.LastGpuP95;
                int lost = s.Mode == 60 ? stats.LastLate : 0;
                bool mal = p95 > tope || lost >= (fgApplied && s.VsyncFg ? 5 : 3);
                bool sobra = !mal && p95 < tope * 0.70 && lost == 0;
                adMal = mal ? adMal + 1 : 0;
                adBien = sobra ? adBien + 1 : 0;
                if (adMal >= 2)
                {
                    adMal = 0;
                    if (AdGuardianDelante(p95, tope)) { }   // (muy por encima del tope: frena el guardian de DLSS 5, que baja mas de golpe; ver AdaptaFreno)
                    else if (AdaptaBajar(p95, tope)) overSecs = 0;   // (lo ha hecho esto: AutoPresetTick no baja ademas el modelo en este segundo)
                }
                else if (adBien >= 20)
                {
                    adBien = 0;
                    AdaptaSubir(p95, tope);
                }
            }
            catch (Exception ex) { Log.Error("Adapta", ex); s.Adapta = false; }
        }

        // Clave nueva (otro modo, generacion, captura...): lo aprendido de ella, sin pasar de lo pedido en pasadas
        void AdaptaMirarClave(bool forzar)
        {
            if (adTabla == null) AdaptaCargar();
            string k = AdaptaClaveAhora();
            if (k == adClave && !forzar) return;
            bool nueva = k != adClave;
            adClave = k; adMal = adBien = 0; adSubida = "";
            // (con la misma clave tambien: el lanzador vuelve a mandar lo pedido con cualquier cambio y se volveria a lo pedido)
            AdaptaFila f;
            if (!adTabla.TryGetValue(k, out f)) { if (nueva) Log.Write("Adapta: clave nueva " + k + " (se empieza por lo pedido: " + s.Preset + (s.Nr ? ", DLSS 5 al " + s.NrPct + " %" : "") + ")"); return; }
            List<string> hecho = new List<string>();
            if (f.Preset.Length > 0 && f.Preset != s.Preset && AdRango(f.Preset) <= AdRango(TechoPreset))
            {
                hecho.Add("DLSS " + s.Preset + " -> " + f.Preset);
                s.Preset = f.Preset;
                presetCap[CapKey] = f.Preset;
                adVistoPreset = f.Preset;
            }
            if (s.Nr && f.Pct >= 25 && f.Pct != s.NrPct) { hecho.Add("DLSS 5 " + s.NrPct + " -> " + f.Pct + " %"); s.NrPct = f.Pct; }
            if (s.Nr && f.Pasadas >= 1 && f.Pasadas < s.NrPasses) { hecho.Add("pasadas " + s.NrPasses + " -> " + f.Pasadas); s.NrPasses = f.Pasadas; }
            if (hecho.Count > 0) { adCambioAt = Clock.Now; Log.Write("Adapta: " + k + ": lo que cupo la ultima vez (" + f.Fecha + "): " + string.Join(", ", hecho.ToArray())); }
        }

        string TechoPreset { get { string t = s.AdaptaTecho; return t == "L" || t == "M" || t == "K" || t == "E" ? t : (adPedidoPreset.Length > 0 ? adPedidoPreset : s.Preset); } }

        bool AdaptaBajar(double p95, double tope)
        {
            if (AdNrActivo && (s.NrPct > 25 || s.NrPasses > 1))
            {
                if (adSubida == "pct" && Clock.SinceMs(adSubidaAt) < 40000 && adSubidaDe >= 25 && adSubidaDe < s.NrPct)
                {
                    // la subida de hace nada no cupo: techo y de vuelta a lo de antes (que si cabia)
                    AdaptaTecho("pct", s.NrPct);
                    Log.Write(string.Format(CultureInfo.InvariantCulture, "Adapta: DLSS 5 al {0} % -> {1} % (p95 {2:0.0} ms, tope {3:0.0})", s.NrPct, adSubidaDe, p95, tope));
                    s.NrPct = adSubidaDe;
                }
                else if (s.NrPct > 25)
                {
                    int antes = s.NrPct;
                    s.NrPct = Math.Max(25, s.NrPct - 10);
                    Log.Write(string.Format(CultureInfo.InvariantCulture, "Adapta: no cabe (grafica p95 {0:0.0} ms, tope {1:0.0}): DLSS 5 al {2} % -> {3} %", p95, tope, antes, s.NrPct));
                }
                else
                {
                    s.NrPasses--;
                    Log.Write(string.Format(CultureInfo.InvariantCulture, "Adapta: no cabe ni al 25 % (p95 {0:0.0} ms, tope {1:0.0}): DLSS 5 con {2} pasada(s)", p95, tope, s.NrPasses));
                }
                adSubida = ""; adCambioAt = Clock.Now;
                AdaptaGuardarFila();
                return true;
            }
            return false;   // (el modelo lo baja AutoPresetTick, como siempre)
        }

        void AdaptaSubir(double p95, double tope)
        {
            AdaptaFila f;
            adTabla.TryGetValue(adClave, out f);
            // 1) el % de DLSS 5 (hasta 100 o el techo aprendido)
            if (AdNrActivo && s.NrPct < 100)
            {
                int sig = Math.Min(100, s.NrPct + 5);
                double prev = p95 * (sig * sig) / (double)(s.NrPct * s.NrPct);   // (prudente: como si todo el tiempo fuera de DLSS 5)
                if ((f == null || f.NoPct == 0 || sig < f.NoPct) && prev <= tope * 0.85)
                {
                    Log.Write(string.Format(CultureInfo.InvariantCulture, "Adapta: sobra grafica (p95 {0:0.0} ms de {1:0.0}): DLSS 5 al {2} % -> {3} %", p95, tope, s.NrPct, sig));
                    adSubida = "pct"; adSubidaAt = Clock.Now; adSubidaDe = s.NrPct;
                    s.NrPct = sig; adCambioAt = Clock.Now;
                    AdaptaGuardarFila();
                    return;
                }
            }
            // 2) el modelo de DLSS SR, hacia el techo
            if (dlssOk && scalerNow == "dlss" && (inW != outW || inH != outH) && AdRango(s.Preset) < AdRango(TechoPreset))
            {
                string sig = AdOrden[Math.Min(AdOrden.Length - 1, AdRango(s.Preset) + 1)];
                double prev = p95 * AdCoste(sig) / AdCoste(s.Preset);
                if ((f == null || f.NoPreset.Length == 0 || AdRango(sig) < AdRango(f.NoPreset)) && prev <= tope * 0.85)
                {
                    Log.Write(string.Format(CultureInfo.InvariantCulture, "Adapta: sobra grafica (p95 {0:0.0} ms de {1:0.0}): DLSS {2} -> {3}", p95, tope, s.Preset, sig));
                    adSubida = "preset"; adSubidaAt = Clock.Now; adSubidaDePreset = s.Preset;
                    s.Preset = sig; presetCap[CapKey] = sig; adVistoPreset = sig; adCambioAt = Clock.Now;
                    AdaptaGuardarFila();
                }
            }
        }

        // Una subida que no cupo: ese valor no se vuelve a probar con esta clave (y se vuelve al de antes)
        void AdaptaTecho(string que, object valor)
        {
            AdaptaFila f = Fila();
            if (que == "pct") { f.NoPct = (int)valor; Log.Write("Adapta: DLSS 5 al " + f.NoPct + " % no cabe aqui: no se vuelve a probar (" + adClave + ")"); }
            else { f.NoPreset = (string)valor; Log.Write("Adapta: DLSS " + f.NoPreset + " no cabe aqui: no se vuelve a probar (" + adClave + ")"); }
            adSubida = "";
        }

        // ---- (fusion 20-11) El guardian de DLSS 5 de Prisma (CIERRES: EngineDlss5.cs, Dlss5GuardTick) y esto, sin pelearse ni oscilar:
        //  - Un solo tope (AdTope) para los dos.
        //  - El guardian es el FRENO DE EMERGENCIA: grafica p95 > 1,6 x tope 2 s o > 1,15 x tope 5 s -> una pasada menos o el % a 75 / 50
        //    de golpe. Esto (adapta) es el ajuste fino: > tope 2 s -> -10 puntos (hasta 25) y luego una pasada; sobra 20 s -> +5.
        //  - Muy por encima (> 1,6 x tope) y con el guardian aun pudiendo bajar, adapta no baja: lo hace el (AdGuardianDelante).
        //  - Lo que baja el guardian (con adapta=1) no se queda en sus topes de la sesion (nrPassCap / nrPctCap: adapta no los ve y sus subidas
        //    no harian nada por encima de ellos, creyendo que si) sino aqui (AdaptaFreno): s.NrPasses / s.NrPct (lo que se pinta), la fila
        //    aprendida (pasadas y %, y el % que no cupo como techo no_pct: no se vuelve a probar) y 8 s sin juzgar (AutoPresetTick tampoco
        //    baja el modelo ese segundo). Un solo estado, recordado para la proxima vez; adapta sube despacio desde ahi sin llegar al techo.
        //  - Cada cambio de cualquiera de los dos recrea DLSS 5 (otro tamaño u otras pasadas): el guardian no juzga en 5 s y adapta en 8 s.
        //  - Sin adapta (adapta=0, escalas_adaptables=0 en el ini): el guardian como en Prisma (sus topes, solo esa sesion del motor).
        bool AdGuardianDelante(double p95, double tope)
        {
            if (!s.Dlss5Guard || !AdNrActivo || p95 <= tope * 1.6) return false;
            int cur = nrW >= inW ? 100 : (int)Math.Round(100.0 * nrW / Math.Max(1, inW));
            return nrPasses > 1 || cur > 50;
        }

        // Desde Dlss5GuardTick, justo despues de poner su tope (capKey = CapKey)
        void AdaptaFreno(string capKey, double p95, double tope)
        {
            if (!s.Adapta) return;
            try
            {
                if (adTabla == null) AdaptaCargar();
                if (adPedidoPreset.Length == 0) { adPedidoPreset = s.Preset; adPedidoPct = s.NrPct; adPedidoPasadas = s.NrPasses; adVistoPreset = s.Preset; }
                AdaptaMirarClave(false);
                int c, pasAntes = s.NrPasses, pctAntes = Math.Max(25, Math.Min(100, s.NrPct));
                if (nrPassCap.TryGetValue(capKey, out c)) { nrPassCap.Remove(capKey); if (c < s.NrPasses) s.NrPasses = Math.Max(1, c); }
                if (nrPctCap.TryGetValue(capKey, out c)) { nrPctCap.Remove(capKey); if (c < pctAntes) s.NrPct = Math.Max(25, c); }
                AdaptaFila f = Fila();
                if (s.NrPct < pctAntes && (f.NoPct == 0 || pctAntes < f.NoPct)) f.NoPct = pctAntes;
                adMal = adBien = 0; adSubida = ""; adCambioAt = Clock.Now; overSecs = 0;
                AdaptaGuardarFila();
                Log.Write(string.Format(CultureInfo.InvariantCulture, "Adapta: freno del guardian de DLSS 5 (p95 {0:0.0} ms, tope {1:0.0}): {2} pasada(s) al {3} % -> {4} al {5} %; " +
                    "queda como lo aprendido de {6}{7}", p95, tope, pasAntes, pctAntes, s.NrPasses, s.NrPct, adClave, f.NoPct > 0 ? " (techo: menos del " + f.NoPct + " %)" : ""));
            }
            catch (Exception ex) { Log.Error("Adapta (freno del guardian)", ex); }
        }

        AdaptaFila Fila()
        {
            AdaptaFila f;
            if (!adTabla.TryGetValue(adClave, out f)) { f = new AdaptaFila(); adTabla[adClave] = f; }
            return f;
        }

        void AdaptaGuardarFila()
        {
            if (adClave.Length == 0) return;
            AdaptaFila f = Fila();
            f.Preset = s.Preset;
            f.Pct = s.Nr ? s.NrPct : f.Pct;
            f.Pasadas = s.Nr ? s.NrPasses : f.Pasadas;
            f.Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            AdaptaEscribir();
        }

        string AdRuta { get { return Program.DataPath(s.AdaptaFichero.Length > 0 ? s.AdaptaFichero : "scales_engine.txt"); } }

        void AdaptaCargar()
        {
            adTabla = new Dictionary<string, AdaptaFila>();
            try
            {
                string r = AdRuta;
                if (!File.Exists(r)) return;
                foreach (string raw in File.ReadAllLines(r))
                {
                    if (raw.Length == 0 || raw[0] == '#') continue;
                    string[] c = raw.Split('\t');
                    if (c.Length < 2) continue;
                    AdaptaFila f = new AdaptaFila();
                    for (int i = 1; i < c.Length; i++)
                    {
                        int eq = c[i].IndexOf('=');
                        if (eq <= 0) continue;
                        string k = c[i].Substring(0, eq), v = c[i].Substring(eq + 1);
                        int n; int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                        switch (k)
                        {
                            case "preset": f.Preset = v; break;
                            case "pct": f.Pct = n; break;
                            case "pasadas": f.Pasadas = n; break;
                            case "no_preset": f.NoPreset = v; break;
                            case "no_pct": f.NoPct = n; break;
                            case "fecha": f.Fecha = v; break;
                        }
                    }
                    adTabla[c[0]] = f;
                }
                Log.Write("Adapta: " + adTabla.Count + " clave(s) aprendidas en " + r);
            }
            catch (Exception ex) { Log.Error("Adapta: leyendo " + AdRuta, ex); }
        }

        void AdaptaEscribir()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ConsoleEnhancer: escalas del motor que caben en este PC (las ajusta el motor solo; borrar este fichero = empezar de cero)");
                sb.AppendLine("# clave (grafica+driver|modo|generacion|Smooth Motion|captura|entrada|salida|DLSS 5 y pasadas) <TAB> lo que cupo <TAB> lo que no");
                foreach (KeyValuePair<string, AdaptaFila> kv in adTabla)
                {
                    AdaptaFila f = kv.Value;
                    sb.Append(kv.Key).Append("\tpreset=").Append(f.Preset);
                    if (f.Pct > 0) sb.Append("\tpct=").Append(f.Pct.ToString(CultureInfo.InvariantCulture));
                    if (f.Pasadas > 0) sb.Append("\tpasadas=").Append(f.Pasadas.ToString(CultureInfo.InvariantCulture));
                    if (f.NoPreset.Length > 0) sb.Append("\tno_preset=").Append(f.NoPreset);
                    if (f.NoPct > 0) sb.Append("\tno_pct=").Append(f.NoPct.ToString(CultureInfo.InvariantCulture));
                    sb.Append("\tfecha=").Append(f.Fecha).AppendLine();
                }
                string r = AdRuta;
                string tmp = r + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(r)) File.Delete(r);
                File.Move(tmp, r);
            }
            catch (Exception ex) { Log.Error("Adapta: guardando " + AdRuta, ex); }
        }
    }
}
