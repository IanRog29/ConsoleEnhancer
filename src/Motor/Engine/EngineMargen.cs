using System;
using System.Globalization;

namespace ConsoleEnhancer.Motor
{
    // EngineMargen.cs - 22-11-2026 (1.2.2, agente UNIVERSAL, ConsoleEnhancer). El usuario: "no exprimas al 100% las graficas que tengan, que
    // siempre haya un leve margen para que no haya lagazos ni nada".
    //
    // El tope del tiempo de grafica del motor (el p95 de cada segundo, lo que miden AutoPresetTick, EngineAdapta y el guardian de DLSS 5):
    // el 85 % del tiempo de una imagen real (uso_grafica=0.85; el lanzador lo pasa de motor_uso_grafica=85) menos lo que se lleva la
    // generacion de NVIDIA, que no se ve en lo medido (va en la presentacion de Streamline / el driver):
    //   - DLSS-G x2: ~4,5 ms con salida 4K en una RTX 4060 (16-11: a 60 + DLSS-G, K 8,5 ms cabia, M 12,4 no y K a 10,1 ms -p95 10,8- ya perdia
    //     6 imagenes/s), escalado por los pixeles de salida y la grafica (gpu_indice=, el de Gpu.cs: RTX 4060 = 1,0; sin el, 1,0).
    //   - Smooth Motion: ~2,5 ms con salida 4K en la RTX 4060 (la mitad que DLSS-G, como en EscalaAuto.cs).
    // Antes (fijos, medidos en la RTX 4060 / 4060 Ti): 60 15,5 ms (93 % de 16,7), 120 8,0 (96 % de 8,3), 30 31 (93 % de 33,4), 60 + DLSS-G
    // 11,0, 30 + DLSS-G 26, Smooth Motion 12,5 / 27. Con el 93-96 % un pico de un 5-10 % (otro proceso, la capturadora, el reloj de la
    // grafica bajando) era una imagen tarde. Ahora, en la RTX 4060 Ti (gpu_indice 1,2) a 4K: 60 14,2, 120 7,1, 30 28,4, 60 + DLSS-G 10,4,
    // 30 + DLSS-G 24,6, Smooth Motion 12,1 / 26,3. EngineAdapta sube un paso solo si lo previsto queda por debajo del 85 % de esto (~72 % del
    // tiempo de una imagen): asi se queda entre el 72 y el 85 % y no oscila. uso_grafica=0.93 ~ como antes.
    partial class Settings
    {
        public double UsoGrafica = 0.85;     // uso_grafica= (0,5-0,98; tambien en %: 85)
        public double GpuIndice = 0;         // gpu_indice= (0 = no se sabe: como una RTX 4060)

        bool ApplyMargen(string key, string val)
        {
            double d;
            switch (key.Trim().ToLowerInvariant())
            {
                case "uso_grafica":
                    if (double.TryParse(val.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0)
                        UsoGrafica = Math.Max(0.5, Math.Min(0.98, d > 1 ? d / 100.0 : d));
                    return true;
                case "gpu_indice":
                    if (double.TryParse(val.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) GpuIndice = Math.Max(0.05, Math.Min(10, d));
                    return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        // (1.2.4, XEFG) imagenes inventadas por cada real (1 = x2, 3 = x4) y tipo ("dlss" | "xess") de la generacion del motor, para la reserva
        internal int margenInventadas = 1;
        internal string margenFgTipo = "dlss";

        // El tope (ms de grafica, p95) del modo de ahora; ver arriba
        double TopeGpu()
        {
            double periodo = s.Mode == 30 ? 2000.0 / 59.94 : s.Is120 ? 1000.0 / 119.88 : 1000.0 / 59.94;
            double t = periodo * Math.Max(0.5, Math.Min(0.98, s.UsoGrafica));
            if (s.Is120) return t;   // (a 120 sin generacion)
            double q = Math.Max(0.1, outW * (double)outH / (3840.0 * 2160.0));
            double idx = s.GpuIndice > 0.05 ? s.GpuIndice : 1.0;
            if (s.SmoothMode > 0) t -= Math.Max(0.8, 2.5 * q / idx);
            // (DLSS-G x2: numFramesToGenerate = 1 en Engine.cs)
            // (1.2.4, XEFG) x3 / x4 y XeSS FG en el motor (agente MOTORFG): la reserva por cada imagen inventada. Medido el 22-11 por Magpie en la
            // RTX 4060 Ti con salida 4K: DLSS-G la primera ~5 ms y cada una de mas ~1,5 (reutiliza el flujo del par); XeSS FG ~8 ms de grafica por
            // inventada (genera en su cola: no alarga el hilo, pero ocupa la grafica). margenInventadas / margenFgTipo los pone quien configura la
            // generacion (1 y "dlss" = lo de siempre)
            else if (fgApplied || XeFgMultNow > 1)   // (1.2.4 MOTORFG: tambien XeSS FG del motor, EngineFgXe.cs)
            {
                int n = Math.Max(1, Math.Min(3, margenInventadas));
                double r = margenFgTipo == "xess" ? 8.0 * n : 4.5 + 1.5 * (n - 1);
                t -= Math.Max(1.0, r * q / idx);
            }
            return Math.Max(periodo * 0.4, t);
        }

        // Una vez por cambio de modo: el tope al log (para ver en engine.log con que margen va)
        string topeVisto = "";
        void TopeGpuLog()
        {
            string k = s.Mode + "|" + (s.Is120 ? 1 : 0) + "|" + (fgApplied ? 1 : 0) + "|" + s.SmoothMode + "|" + outW + "x" + outH + "|" + margenInventadas + margenFgTipo;
            if (k == topeVisto) return;
            topeVisto = k;
            double periodo = s.Mode == 30 ? 2000.0 / 59.94 : s.Is120 ? 1000.0 / 119.88 : 1000.0 / 59.94;
            double t = TopeGpu();
            Log.Write(string.Format(CultureInfo.InvariantCulture, "Margen: tope de grafica {0:0.0} ms de {1:0.0} ({2:0} %; uso_grafica {3:0.00}, gpu_indice {4:0.00}{5})",
                t, periodo, 100 * t / periodo, s.UsoGrafica, s.GpuIndice, s.SmoothMode > 0 ? ", Smooth Motion" : fgApplied ? ", " + margenFgTipo + " x" + (margenInventadas + 1) : ""));
        }
    }
}
