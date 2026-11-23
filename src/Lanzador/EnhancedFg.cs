// EnhancedFg.cs - 23-11-2026 (1.2.4 ronda 6, agente ENHANCED). C# 5
//
// El usuario: "podrias intentar crear un fg que sea para cualquier gpu y tenga modos 2 3 y 4x y pille lo mejor de smooth motion dlss fg y xess fg?
// y añadirlo como opcion extra para que la pruebe cuando acabes. de manera legal, creando el codigo tu y llamandolo enhanced fg y que vaya tan o mas
// fluido como smooth motion" (y "latencia que sea mejor todavia que smooth motion").
// Enhanced FG es la generacion propia del motor (src\Motor\Engine\EngineFgEnhanced*.cs: codigo nuestro, cualquier grafica con D3D12). En el lanzador:
//  - un tipo de generacion mas, "efg", que NO es el de serie (el plan no lo pone en generacion_lista_*): sale al final de la lista del selector
//    (L3) y de la fila "Tipo de generacion" del menu de ajustes como opcion extra, en cualquier grafica;
//  - sus filas en el menu de ajustes (solo con "Enhanced FG" elegido): "Multiplicador" x2 / x3 / x4 (en el modo 60 solo x2: 3 x 59,94 no cabe en
//    120 Hz) y "Enhanced FG: baja latencia" (No = calidad, la interpolacion; Si = hacia delante, sin esperar a la siguiente real);
//  - el motor se abre con su "cadena" propia (EfgChainId: la cola de presentacion de Enhanced FG se crea al abrirlo) y con efg=N efg_modo=...;
//    el multiplicador y el modo cambian en marcha (EngineLive);
//  - si el motor se cae con Enhanced FG (grafica perdida / parado), el resto de la sesion va otro tipo (como XeSS FG).
// Lo elegido se guarda en data\games\efg.txt ("mult;latencia").
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public const int EfgChainId = 50;
        public static bool EfgChain(int chain) { return chain == EfgChainId; }
        static int efgMult = -1;
        static bool efgLow;
        static readonly object efgSync = new object();
        static string EfgFile { get { return Path.Combine(Paths.Datos, @"games\efg.txt"); } }

        static void EfgLoad()
        {
            if (efgMult > 0) return;
            efgMult = 2; efgLow = false;
            try
            {
                if (!File.Exists(EfgFile)) return;
                string[] f = File.ReadAllText(EfgFile).Trim().Split(';');
                int m; if (f.Length > 0 && int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out m)) efgMult = Math.Max(2, Math.Min(4, m));
                if (f.Length > 1) efgLow = f[1].Trim() == "1";
            }
            catch { }
        }
        public static int EfgMult { get { lock (efgSync) { EfgLoad(); return efgMult; } } }
        public static bool EfgLow { get { lock (efgSync) { EfgLoad(); return efgLow; } } }
        public static void EfgSet(int mult, bool low)
        {
            lock (efgSync)
            {
                EfgLoad();
                efgMult = Math.Max(2, Math.Min(4, mult)); efgLow = low;
                try { Directory.CreateDirectory(Path.GetDirectoryName(EfgFile)); File.WriteAllText(EfgFile, efgMult.ToString(CultureInfo.InvariantCulture) + ";" + (efgLow ? "1" : "0")); }
                catch (Exception ex) { Log.Error("Enhanced FG: guardando lo elegido", ex); }
            }
        }

        // El multiplicador que va: en el modo 30 el elegido; en el 60 x2 (3 x 59,94 no cabe en 120 Hz)
        public int EfgMultNow { get { return CapMode && !Mode120 ? EfgMult : 2; } }

        public static bool EfgCaido { get { return motorGenCaida.Contains(" efg"); } }
        public static bool EfgPuede() { return !EfgCaido; }

        // Lo que va en EngineLive con la cadena de Enhanced FG (fg=0 al motor: no es DLSS-G / XeSS FG / FSR 3 FG)
        public string EfgLiveArgs(bool on)
        {
            return " efg=" + (on ? EfgMultNow : 0).ToString(CultureInfo.InvariantCulture) + " efg_modo=" + (EfgLow ? "latencia" : "calidad");
        }

        public static string EfgLabel(bool conMult)
        {
            string s = "Enhanced FG";
            if (!conMult) return s;
            int m = EfgMult;
            return s + (m > 2 ? " x" + m.ToString(CultureInfo.InvariantCulture) : "") + (EfgLow ? Tx.T(" · baja latencia", " · low latency") : "");
        }
    }
    partial class TrayApp
    {
        // El menu de ajustes: las filas de Enhanced FG, detras de "Tipo de generacion" (solo se ven con Enhanced FG elegido: AjustesDepende)
        FilaAjuste[] EfgFilas()
        {
            FilaAjuste mul = new FilaAjuste { Id = "efgmul", Nombre = Tx.T("Multiplicador", "Multiplier") };
            string m = Config.EfgMult.ToString(CultureInfo.InvariantCulture);
            mul.Poner(new List<string> { "2", "3", "4" }, new List<string> { "x2", "x3", "x4" }, m, m);
            FilaAjuste lat = new FilaAjuste { Id = "efglat", Nombre = Tx.T("Enhanced FG: baja latencia", "Enhanced FG: low latency") };
            string l = Config.EfgLow ? "1" : "0";
            lat.Poner(SiNo(), SiNoTxt(), l, l);
            mul.Visible = lat.Visible = false;
            return new[] { mul, lat };
        }

        // (AjustesDepende, tras el tipo) visibles con Enhanced FG elegido; el multiplicador solo en el modo 30 (en el 60, x2)
        void EfgDepende(string modoSel)
        {
            FilaAjuste tipo = AjFila("tipo"), fg = AjFila("fg"), mul = AjFila("efgmul"), lat = AjFila("efglat");
            if (mul == null || lat == null) return;
            bool on = tipo != null && tipo.Visible && tipo.Val == "efg" && modoSel != "120";
            bool fgSi = fg == null || fg.Val == "1";
            mul.Visible = lat.Visible = on;
            mul.Activa = on && fgSi && modoSel == "30";
            mul.Nota = !on ? "" : modoSel != "30" ? Tx.T("x2 en el modo 60", "x2 in 60 fps mode") : !fgSi ? Tx.T("Con la generación encendida", "With frame generation on") : "";
            lat.Activa = on && fgSi;
            lat.Nota = on && !fgSi ? Tx.T("Con la generación encendida", "With frame generation on") : "";
        }

        // (AjustesPonerTodo) lo elegido en sus filas; el motor lo recibe en marcha (EngineLive -> SyncEngine)
        void EfgAplicar(Dictionary<string, string> e)
        {
            string a = AjVal(e, "efgmul"), b = AjVal(e, "efglat");
            if (a == null && b == null) return;
            int m = Config.EfgMult; bool low = Config.EfgLow;
            int n; if (a != null && int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) m = n;
            if (b != null) low = b == "1";
            if (m == Config.EfgMult && low == Config.EfgLow) return;
            Config.EfgSet(m, low);
            Log.Write("Enhanced FG: x" + Config.EfgMult.ToString(CultureInfo.InvariantCulture) + (Config.EfgLow ? ", baja latencia (hacia delante)" : ", calidad (interpolacion)"));
        }

        // RunMpv, el motor se ha cerrado solo con la grafica perdida (3) o parado (5) con Enhanced FG en el: el resto de la sesion otro tipo
        bool EfgCaida(Process p, int code)
        {
            if ((code != 3 && code != 5) || !Config.EfgChain(Config.EngineFfxOf(p))) return false;
            Config.MotorGenCaidaAnota("efg");
            Log.Write("Motor: la grafica se ha " + (code == 3 ? "perdido" : "parado") + " con Enhanced FG (codigo " + code.ToString(CultureInfo.InvariantCulture) +
                      "): el resto de la sesion va otro tipo de generacion (el mas parecido)");
            return true;
        }
    }
}
