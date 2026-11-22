// Ritmo.cs - de donde vienen los tirones del camino mpv + Magpie (22-11-2026, 1.2.2, agente RITMO)
//
// El usuario: "hay microstutters (puede que lo causen vrr...)". El log decia "Tirones sueltos: Magpie estuvo 60-130 ms sin imagen nueva" pero
// no de donde venian. Ahora cada tiron se reparte entre:
//   juego      la imagen ya llego tarde a mpv (la marca de tiempo de la capturadora salta: la consola saco una imagen de 50 ms, o la
//              capturadora perdio una). Es del juego, no se puede arreglar en el PC
//   mpv        la marca de la capturadora no salta pero mpv la saco tarde (mpv ocupado: capturas de la app, CPU)
//   captura    mpv la saco a tiempo y Magpie la recibio tarde o no la recibio (la captura de Windows, WGC, entre mpv y Magpie)
//   grafica    lo anterior con un pico de la grafica en Magpie (la espera de su ultima imagen > 2 veces la media y 15 ms mas)
//   salida     Magpie recibio bien las imagenes pero presento con un hueco (su ritmo, la generacion o la cola de la pantalla)
// Lo de mpv llega al momento (scripts\ce_diag.lua manda "switch2-diag-hueco" con cada imagen que sale tarde: hora, marca y lo normal);
// lo de Magpie, de su log (las lineas "Output cadence" cada ~4 s, con su hora). Cada 5 min, el resumen por origen.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ConsoleEnhancer
{
    static class RitmoDiag
    {
        struct HuecoMpv { public DateTime At; public double Hora, Marca, Normal; }
        struct Ventana { public DateTime Desde, Hasta; public double CapMax, OutMax, CapP95; public bool Pico; }
        static readonly object sync = new object();
        static readonly List<HuecoMpv> mpv = new List<HuecoMpv>();
        static readonly List<Ventana> ventanas = new List<Ventana>();
        static DateTime ultimaVentana = DateTime.MinValue;
        // contadores del resumen (5 min): juego, mpv, captura, grafica, salida
        static readonly int[] cuenta = new int[5];
        static readonly double[] peor = new double[5];
        public static readonly string[] Nombres = { "juego", "mpv", "captura", "grafica", "salida" };

        static double Num(string s)
        {
            double v; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        // "switch2-diag-hueco","<hora ms> <marca ms> <normal ms>" (scripts\ce_diag.lua)
        public static void MpvHueco(string txt)
        {
            string[] p = (txt ?? "").Split(' ');
            if (p.Length < 3) return;
            HuecoMpv h = new HuecoMpv();
            h.At = DateTime.Now; h.Hora = Num(p[0]); h.Marca = Num(p[1]); h.Normal = Num(p[2]);
            if (double.IsNaN(h.Hora) || double.IsNaN(h.Marca)) return;
            lock (sync)
            {
                mpv.Add(h);
                while (mpv.Count > 0 && (h.At - mpv[0].At).TotalSeconds > 60) mpv.RemoveAt(0);
            }
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
                bool tiron = v.CapMax >= normal * 1.45 && v.CapMax - normal >= 20 && v.CapMax < 1000;
                bool salidaMal = !tiron && v.OutMax >= salida * 1.9 && v.OutMax - salida >= 14 && v.OutMax < 1000;
                if (!tiron && !salidaMal) continue;
                int o; string porque;
                if (salidaMal) { o = 4; porque = "Magpie presento con un hueco de " + R(v.OutMax) + " ms con las imagenes llegando bien (su ritmo, la generacion o la cola de la pantalla)"; }
                else
                {
                    double marca = 0, hora = 0;
                    foreach (HuecoMpv h in hs)
                    {
                        if (h.At < v.Desde.AddMilliseconds(-300) || h.At > v.Hasta.AddMilliseconds(150)) continue;
                        if (h.Marca > marca) marca = h.Marca;
                        if (h.Hora > hora) hora = h.Hora;
                    }
                    double cerca = v.CapMax - 0.6 * normal;
                    if (marca >= cerca) { o = 0; porque = "la imagen ya llego tarde a mpv (marca de la capturadora " + R(marca) + " ms: la consola o la capturadora)"; }
                    else if (hora >= cerca) { o = 1; porque = "mpv la saco tarde (" + R(hora) + " ms; la marca de la capturadora " + (marca > 0 ? R(marca) + " ms" : "normal") + ")"; }
                    else if (v.Pico) { o = 3; porque = "mpv la saco a tiempo" + (hora > 0 ? " (peor " + R(hora) + " ms)" : "") + "; con un pico de la grafica en Magpie"; }
                    else { o = 2; porque = "mpv la saco a tiempo" + (hora > 0 ? " (peor " + R(hora) + " ms)" : "") + "; se perdio o llego tarde en la captura de Windows (WGC) hasta Magpie"; }
                }
                double ms = o == 4 ? v.OutMax : v.CapMax;
                lock (sync) { cuenta[o]++; if (ms > peor[o]) peor[o] = ms; }
                if (ms > peorDe) { peorDe = ms; texto = Nombres[o] + ": " + porque; }
            }
            peorMs = peorDe;
            return texto;
        }

        // El resumen (y a cero); null si no hubo nada
        public static string Resumen()
        {
            lock (sync)
            {
                int total = 0; foreach (int c in cuenta) total += c;
                if (total == 0) return null;
                List<string> p = new List<string>();
                for (int i = 0; i < cuenta.Length; i++)
                    if (cuenta[i] > 0) p.Add(Nombres[i] + " " + cuenta[i] + " (peor " + R(peor[i]) + " ms)");
                Array.Clear(cuenta, 0, cuenta.Length); Array.Clear(peor, 0, peor.Length);
                return string.Join(", ", p.ToArray());
            }
        }

        public static void Limpia() { lock (sync) { ventanas.Clear(); mpv.Clear(); ultimaVentana = DateTime.MinValue; } }

        static string R(double v) { return Math.Round(v).ToString(CultureInfo.InvariantCulture); }
    }
}
