// Ritmo.cs - de donde vienen los tirones del camino mpv + Magpie (22-11-2026, 1.2.2, agente RITMO)
//
// El usuario: "hay microstutters (puede que lo causen vrr...)". El log decia "Tirones sueltos: Magpie estuvo 60-130 ms sin imagen nueva" pero
// no de donde venian. Ahora cada tiron se reparte entre:
//   juego        la imagen ya llego tarde a mpv (la marca de tiempo de la capturadora salta: la consola saco una imagen de 50 ms, o la
//                capturadora perdio una). Es del juego, no se puede arreglar en el PC
//   captura_app  mpv la saco tarde justo despues de una captura de la propia app (la que mira si se ha vuelto al menu de la consola:
//                screenshot-raw para el hilo de mpv; medido el 22-11 sin grafica: 16 ms a 1080p, 22 ms a 1440p, 44-58 ms a 4K, mas que el
//                hueco de 33 ms entre dos imagenes del modo 30)
//   mpv          la marca de la capturadora no salta y no hubo captura, pero mpv la saco tarde (CPU ocupada)
//   wgc          mpv la saco a tiempo y Magpie la recibio tarde o no la recibio (la captura de Windows entre mpv y Magpie: Magpie se queda
//                con la mas nueva y tira las demas mientras esta ocupado)
//   grafica      lo anterior con un pico de la grafica en Magpie (la espera de su ultima imagen > 2 veces la media y 15 ms mas)
//   salida       Magpie recibio bien las imagenes pero presento con un hueco (su ritmo, la generacion o la cola de la pantalla)
// Lo de mpv llega al momento (scripts\ce_diag.lua manda "switch2-diag-hueco" con cada imagen que sale 8 ms o mas tarde: hora, marca y lo
// normal; scripts\ce_power.lua "switch2-diag-captura" con lo que paro a mpv cada captura); lo de Magpie, de su log (las lineas "Output
// cadence" cada ~4 s, con su hora, y "DLSSFG frontend timing": lo que esperan sus imagenes en su cola). Cada 5 min, el resumen.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ConsoleEnhancer
{
    // ---- Como presenta Magpie cada modo (22-11, 1.2.2). Lo de serie es lo de antes:
    //   modo 30 sin generacion      DirectComposition (sin VRR)                       magpie_vrr_30=1: por VRR (DXGI + sincronizacion forzada)
    //   modo 30, DLSS FG x2         VRR (DXGI + sincronizacion forzada)               magpie_fijo_30_dlss=1: refresco fijo "cada 2 refrescos"
    //   modo 30, XeSS/FSR 3/VFG x2  refresco fijo "cada 2 refrescos" (dxgi_30_*)     magpie_vrr_30_otras=1: por VRR
    //   modo 30, cualquiera x3 / x4 VRR (con "cada 2 refrescos" la salida no pasaria de 60: 89,9 y 119,88 imagenes/s no caben); sin VRR,
    //                               refresco fijo con la sincronizacion forzada (cada imagen 1 refresco)
    //   modo 60 (sin / DLSS FG)     como antes (DXGI con la sincronizacion forzada / VRR)
    // Y el ritmo del contenido de Magpie con generacion: magpie_ritmo_margen_fg=N (%: contentRate un N % por encima del de la captura; el
    // DLSS FG de Magpie no deja salir mas despacio que contentRate x multiplicador ni meter imagenes reales mas deprisa que contentRate:
    // con 29,97 justos no puede vaciar su cola tras un tiron) y magpie_sigue_fuente_30_fg=1 (contentMode "seguir la fuente": sin ese suelo
    // ni la espera de cada imagen real). De serie 0 / 0 (lo de antes) hasta medirlo con la tele
    partial class Config
    {
        public bool RitmoVrr30 = false, RitmoVrr30Otras = false, RitmoFijo30Dlss = false, RitmoSigue30Fg = false;
        public double RitmoMargenFg = 0;
        // ---- Las capturas de la app jugando a 4K (las que miran si se ha vuelto al menu de la consola, TrayApp.GrabTick): cada una es un
        // screenshot-raw de mpv que para su hilo mientras pasa la imagen de 4K a RGB y la copia (medido el 22-11 sin grafica, mpv 0.42:
        // 44-80 ms a 4K, 22 ms a 1440p, 16 ms a 1080p). A 4K es mas que el hueco entre dos imagenes del modo 30 (33 ms): cada captura deja
        // la siguiente imagen real 10-45 ms tarde y con la generacion de Magpie (su ritmo sigue a las imagenes que llegan; con VRR la tele
        // va con el) se ve como un tiron; con el refresco fijo de XeSS FG casi no (cae en su hueco de 2 refrescos). Antes una cada 1,5 s;
        // ahora, jugando a 4K con generacion en el modo 30, una cada captura_juego_4k_fg_s (de serie 3: el menu de la consola se ve, como
        // mucho, 1,5 s mas tarde). captura_juego_4k_s = la de los demas casos a 4K (de serie 1,5, como antes)
        public double Grab4kSecs = 1.5, Grab4kFgSecs = 3.0;
        public int RitmoResumenSecs = 300;   // ritmo_resumen_s: cada cuanto va al log "Tirones por origen" (de serie 5 min; pruebas: menos)
        // (solo por mpv + Magpie: con el motor propio la captura no pasa por mpv)
        public double RitmoGrab4kSecs { get { return CapMode && !FgOff && !HomeNative && !EngineActive ? Math.Max(Grab4kSecs, Grab4kFgSecs) : Grab4kSecs; } }

        bool CeParseRitmo(string key, string val)
        {
            string v = val.Trim().ToLowerInvariant();
            bool si = v == "1" || v == "si" || v == "true" || v == "on" || v == "yes";
            switch (key)
            {
                case "magpie_vrr_30": RitmoVrr30 = si; return true;
                case "magpie_vrr_30_otras": RitmoVrr30Otras = si; return true;
                case "magpie_fijo_30_dlss": RitmoFijo30Dlss = si; return true;
                case "magpie_sigue_fuente_30_fg": RitmoSigue30Fg = si; return true;
                case "ritmo_resumen_s":
                    {
                        int n;
                        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) RitmoResumenSecs = Math.Max(20, Math.Min(3600, n));
                        return true;
                    }
                case "captura_juego_4k_s":
                case "captura_juego_4k_fg_s":
                    {
                        double d;
                        if (!double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return true;
                        d = Math.Max(0.3, Math.Min(10, d));
                        if (key == "captura_juego_4k_s") Grab4kSecs = d; else Grab4kFgSecs = d;
                        return true;
                    }
                case "magpie_ritmo_margen_fg":
                    {
                        double d;
                        if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) RitmoMargenFg = Math.Max(0, Math.Min(10, d));
                        return true;
                    }
            }
            return false;
        }

        // El modo 30 con generacion por VRR (con el VRR encendido, Config.MagpieVrr30Fg): DLSS FG x2 (salvo magpie_fijo_30_dlss), x3 / x4
        // con cualquiera de Magpie, y XeSS / FSR 3 / VFG x2 con magpie_vrr_30_otras. Smooth Motion nunca (va por el driver)
        bool RitmoVrr30FgTipo
        {
            get
            {
                string t = FgTypeNow;
                if (t == "smooth") return false;
                if (FgMultNow >= 3) return true;
                return t == "dlss" ? !RitmoFijo30Dlss : RitmoVrr30Otras;
            }
        }
        // El modo 30 sin generacion por VRR (magpie_vrr_30=1)
        bool RitmoVrr30SinFgNow { get { return RitmoVrr30 && MagpieVrr30Fg && CapMode && FgOff; } }
        // x3 / x4 sin VRR: DXGI con la sincronizacion vertical forzada (estado 3 de NvProfiles), cada imagen un refresco
        public bool RitmoFijo1Now { get { return CapMode && !FgOff && FgTypeNow != "smooth" && FgMultNow >= 3 && !MagpieVrrNow && !NvProfiles.VsyncBroken; } }
        public bool RitmoSigueNow { get { return RitmoSigue30Fg && CapMode && !FgOff && FgTypeNow != "smooth"; } }
        public double RitmoRate(double rate) { return !FgOff && RitmoMargenFg > 0 ? Math.Round(rate * (1 + RitmoMargenFg / 100.0), 2) : rate; }
    }

    static class RitmoDiag
    {
        struct HuecoMpv { public DateTime At; public double Hora, Marca, Normal, NormalMarca; }
        struct Ventana { public DateTime Desde, Hasta; public double CapMax, OutMax, CapP95; public bool Pico; }
        struct Captura { public DateTime At; public double Ms; }
        static readonly object sync = new object();
        static readonly List<HuecoMpv> mpv = new List<HuecoMpv>();
        static readonly List<Ventana> ventanas = new List<Ventana>();
        static readonly List<Captura> capturas = new List<Captura>();
        static DateTime ultimaVentana = DateTime.MinValue;
        public static readonly string[] Nombres = { "juego", "captura_app", "mpv", "wgc", "grafica", "salida" };
        // contadores del resumen (5 min) por origen, y aparte: capturas de la app (cuantas, ms parado mpv) y las imagenes de mpv que
        // salieron tarde justo despues de una (aunque Magpie no viera un tiron: con la generacion y VRR un retraso de 10-25 ms ya se nota)
        static readonly int[] cuenta = new int[6];
        static readonly double[] peor = new double[6];
        static int nCapt, nCaptTarde; static double sumCapt, maxCapt;
        static readonly List<double> fifo = new List<double>(), espera = new List<double>();
        static readonly List<DateTime> tardeSueltas = new List<DateTime>();

        static double Num(string s)
        {
            double v; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        // "switch2-diag-hueco","<hora ms> <marca ms> <normal ms> [<normal de la marca ms>]" (scripts\ce_diag.lua)
        public static void MpvHueco(string txt)
        {
            string[] p = (txt ?? "").Split(' ');
            if (p.Length < 3) return;
            HuecoMpv h = new HuecoMpv();
            h.At = DateTime.Now; h.Hora = Num(p[0]); h.Marca = Num(p[1]); h.Normal = Num(p[2]); h.NormalMarca = p.Length > 3 ? Num(p[3]) : h.Normal;
            if (double.IsNaN(h.Hora) || double.IsNaN(h.Marca) || double.IsNaN(h.Normal)) return;
            if (double.IsNaN(h.NormalMarca)) h.NormalMarca = h.Normal;
            lock (sync)
            {
                mpv.Add(h);
                while (mpv.Count > 0 && (h.At - mpv[0].At).TotalSeconds > 60) mpv.RemoveAt(0);
                // tarde por la hora (no por la marca) justo despues de una captura de la app
                if (h.Hora - h.Normal >= 8 && h.Marca - h.NormalMarca < 8)
                {
                    if (TrasCaptura(h.At)) nCaptTarde++;
                    else { tardeSueltas.Add(h.At); while (tardeSueltas.Count > 0 && (h.At - tardeSueltas[0]).TotalSeconds > 2) tardeSueltas.RemoveAt(0); }
                }
            }
        }

        // "switch2-diag-captura","<ms>" (scripts\ce_power.lua: lo que paro a mpv una captura de la app)
        public static void MpvCaptura(string txt)
        {
            double ms = Num((txt ?? "").Trim());
            if (double.IsNaN(ms)) return;
            Captura c = new Captura(); c.At = DateTime.Now.AddMilliseconds(-ms); c.Ms = ms;
            lock (sync)
            {
                capturas.Add(c);
                while (capturas.Count > 0 && (c.At - capturas[0].At).TotalSeconds > 60) capturas.RemoveAt(0);
                nCapt++; sumCapt += ms; if (ms > maxCapt) maxCapt = ms;
                // (el aviso de la imagen tarde puede llegar antes que el de la captura: van por scripts distintos)
                for (int i = tardeSueltas.Count - 1; i >= 0; i--)
                {
                    double d = (tardeSueltas[i] - c.At).TotalMilliseconds;
                    if (d >= -20 && d <= 200) { nCaptTarde++; tardeSueltas.RemoveAt(i); }
                }
            }
        }

        // (con sync cogido) ¿empezo una captura de la app en los 200 ms antes de esta imagen tarde?
        static bool TrasCaptura(DateTime at)
        {
            for (int i = capturas.Count - 1; i >= 0; i--)
            {
                double d = (at - capturas[i].At).TotalMilliseconds;
                if (d < -20) continue;
                if (d > 200) break;
                return true;
            }
            return false;
        }

        // Una linea "Output cadence" del log de Magpie (y si en esa ventana hubo pico de la grafica)
        public static void MagpieVentana(string line, bool pico)
        {
            try
            {
                Match t = Regex.Match(line, @"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\|");
                Match c = Regex.Match(line, @"captureIntervalP95Ms=([0-9.]+) captureIntervalP99Ms=[0-9.]+ captureIntervalMaxMs=([0-9.]+) outputIntervalMaxMs=([0-9.]+)");
                if (!t.Success || !c.Success) return;
                DateTime at = DateTime.ParseExact(t.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                Ventana v = new Ventana();
                v.Hasta = at;
                v.Desde = ultimaVentana != DateTime.MinValue && (at - ultimaVentana).TotalSeconds < 10 ? ultimaVentana : at.AddSeconds(-4);
                ultimaVentana = at;
                v.CapP95 = Num(c.Groups[1].Value); v.CapMax = Num(c.Groups[2].Value); v.OutMax = Num(c.Groups[3].Value); v.Pico = pico;
                lock (sync) { ventanas.Add(v); if (ventanas.Count > 64) ventanas.RemoveAt(0); }
            }
            catch { }
        }

        // Una linea "DLSSFG frontend timing" del log de Magpie: lo que esperan sus imagenes en su cola (fifoAge) y al reloj (paceWait)
        public static void MagpieEspera(string line)
        {
            Match f = Regex.Match(line, @"paceWait=([0-9.]+) ms.*?fifoAge=([0-9.]+) ms");
            if (!f.Success) return;
            double p = Num(f.Groups[1].Value), a = Num(f.Groups[2].Value);
            if (double.IsNaN(p) || double.IsNaN(a)) return;
            lock (sync) { espera.Add(p); fifo.Add(a); if (fifo.Count > 400) { fifo.RemoveAt(0); espera.RemoveAt(0); } }
        }

        // El origen del peor tiron de las ventanas nuevas (normal = ms entre dos imagenes reales; salida = ms entre dos presentaciones
        // de Magpie, la mitad con la generacion x2). "" si no hubo tiron. Se cuentan todos para el resumen
        public static string Repasa(double normal, double salida, out double peorMs)
        {
            peorMs = 0;
            List<Ventana> vs; List<HuecoMpv> hs;
            lock (sync) { vs = new List<Ventana>(ventanas); ventanas.Clear(); hs = new List<HuecoMpv>(mpv); }
            string texto = ""; double peorDe = 0;
            foreach (Ventana v in vs)
            {
                bool tiron = v.CapMax >= normal * 1.45 && v.CapMax - normal >= 20 && v.CapMax < 400;   // (como antes: lo de 400 ms o mas es un cambio, no un tiron)
                bool salidaMal = !tiron && v.OutMax >= salida * 1.9 && v.OutMax - salida >= 14 && v.OutMax < 400 && v.CapMax < normal * 1.45;
                if (!tiron && !salidaMal) continue;
                int o; string porque;
                if (salidaMal) { o = 5; porque = "Magpie presento con un hueco de " + R(v.OutMax) + " ms con las imagenes llegando bien (su ritmo, la generacion o la cola de la pantalla)"; }
                else
                {
                    double marca = 0, hora = 0; bool captura = false;
                    lock (sync)
                        foreach (HuecoMpv h in hs)
                        {
                            if (h.At < v.Desde.AddMilliseconds(-300) || h.At > v.Hasta.AddMilliseconds(150)) continue;
                            if (h.Marca > marca) marca = h.Marca;
                            if (h.Hora > hora) { hora = h.Hora; captura = TrasCaptura(h.At); }
                        }
                    double cerca = v.CapMax - 0.6 * normal;
                    if (marca >= cerca) { o = 0; porque = "la imagen ya llego tarde a mpv (marca de la capturadora " + R(marca) + " ms: la consola o la capturadora)"; }
                    else if (hora >= cerca && captura) { o = 1; porque = "mpv la saco tarde (" + R(hora) + " ms) justo despues de una captura de la app (la que mira el menu de la consola)"; }
                    else if (hora >= cerca) { o = 2; porque = "mpv la saco tarde (" + R(hora) + " ms; la marca de la capturadora " + (marca > 0 ? R(marca) + " ms" : "normal") + ")"; }
                    else if (v.Pico) { o = 4; porque = "mpv la saco a tiempo" + (hora > 0 ? " (peor " + R(hora) + " ms)" : "") + "; con un pico de la grafica en Magpie"; }
                    else { o = 3; porque = "mpv la saco a tiempo" + (hora > 0 ? " (peor " + R(hora) + " ms)" : "") + "; se perdio o llego tarde en la captura de Windows (WGC) hasta Magpie"; }
                }
                double ms = o == 5 ? v.OutMax : v.CapMax;
                lock (sync) { cuenta[o]++; if (ms > peor[o]) peor[o] = ms; }
                if (ms > peorDe) { peorDe = ms; texto = Nombres[o] + ": " + porque; }
            }
            peorMs = peorDe;
            return texto;
        }

        static double Mediana(List<double> l)
        {
            if (l.Count == 0) return double.NaN;
            List<double> c = new List<double>(l); c.Sort(); return c[c.Count / 2];
        }

        // El resumen (y a cero); null si no hubo nada
        public static string Resumen()
        {
            lock (sync)
            {
                List<string> p = new List<string>();
                for (int i = 0; i < cuenta.Length; i++)
                    if (cuenta[i] > 0) p.Add(Nombres[i] + " " + cuenta[i] + " (peor " + R(peor[i]) + " ms)");
                if (nCapt > 0) p.Add("capturas de la app " + nCapt + " (paran a mpv " + R(sumCapt / nCapt) + " ms de media, peor " + R(maxCapt) + "; " + nCaptTarde + " imagenes de mpv tarde justo despues)");
                if (fifo.Count > 0) p.Add("espera en la cola de Magpie " + Math.Round(Mediana(fifo), 1).ToString(CultureInfo.InvariantCulture) + " ms (al reloj " + Math.Round(Mediana(espera), 1).ToString(CultureInfo.InvariantCulture) + ")");
                Array.Clear(cuenta, 0, cuenta.Length); Array.Clear(peor, 0, peor.Length);
                nCapt = 0; nCaptTarde = 0; sumCapt = 0; maxCapt = 0; fifo.Clear(); espera.Clear();
                return p.Count == 0 ? null : string.Join(", ", p.ToArray());
            }
        }

        public static void Limpia() { lock (sync) { ventanas.Clear(); mpv.Clear(); capturas.Clear(); ultimaVentana = DateTime.MinValue; } }

        static string R(double v) { return Math.Round(v).ToString(CultureInfo.InvariantCulture); }
    }
}
