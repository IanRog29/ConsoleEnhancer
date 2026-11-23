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
        public double FgReservaMs = -1;      // (1.2.4 ronda 7, FGREAL) fg_reserva_ms= (ms de grafica de la generacion del motor por real; -1 = la cuenta de EngineMargen)

        bool ApplyMargen(string key, string val)
        {
            double d;
            switch (key.Trim().ToLowerInvariant())
            {
                case "uso_grafica":
                    if (double.TryParse(val.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0)
                        UsoGrafica = Math.Max(0.5, Math.Min(0.98, d > 1 ? d / 100.0 : d));
                    return true;
                case "fg_reserva_ms":
                    if (double.TryParse(val.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) FgReservaMs = d < 0 ? -1 : Math.Min(30, d);
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
        // (1.2.4, XEFG) imagenes inventadas por cada real y tipo de la generacion del motor, para la reserva. (1.2.4 ronda 6, QUITAFG: sin
        // DLSS-G ni x3 / x4: XeSS FG x2, la pone EngineFgXe.cs)
        internal int margenInventadas = 1;
        internal string margenFgTipo = "xess";

        // El tope (ms de grafica, p95) del modo de ahora; ver arriba
        double TopeGpu()
        {
            double periodo = s.Mode == 30 ? 2000.0 / 59.94 : s.Is120 ? 1000.0 / 119.88 : 1000.0 / 59.94;
            double t = periodo * Math.Max(0.5, Math.Min(0.98, s.UsoGrafica));
            if (s.Is120) return t;   // (a 120 sin generacion)
            double q = Math.Max(0.1, outW * (double)outH / (3840.0 * 2160.0));
            double idx = s.GpuIndice > 0.05 ? s.GpuIndice : 1.0;
            // (1.2.4 ronda 6, SMX4: sm=3 = Smooth Motion x4 encima de XeSS FG x2: dos inventadas del driver por real. Ronda 7, FGREAL: con sm=3
            // 3,0 ms cada una -medido en la sesion del usuario, x4 a 2,7 GHz: ~2,9 ms por inventada del driver a 4K-; x2 sigue con 2,5, como siempre)
            if (s.SmoothMode > 0) t -= Math.Max(0.8, (s.SmoothMode == 3 ? 3.0 : 2.5) * q / idx) * (s.SmoothMode == 3 ? 2 : 1);
            // (DLSS-G x2: numFramesToGenerate = 1 en Engine.cs)
            // (1.2.4, XEFG) x3 / x4 y XeSS FG en el motor (agente MOTORFG): la reserva por cada imagen inventada. Medido el 22-11 por Magpie en la
            // RTX 4060 Ti con salida 4K: DLSS-G la primera ~5 ms y cada una de mas ~1,5 (reutiliza el flujo del par); XeSS FG ~8 ms de grafica por
            // inventada (genera en su cola: no alarga el hilo, pero ocupa la grafica). margenInventadas / margenFgTipo los pone quien configura la
            // generacion (1 y "dlss" = lo de siempre)
            // (1.2.4 ronda 7, FGREAL: ya no va con "else": con sm=3 -Smooth Motion x4- va XeSS FG debajo y no se reservaba; y las otras generaciones
            // del motor -FSR 3 FG de AMD, FSR FG x3 / x4 nuestra, Enhanced FG- tampoco: con DLSS 5 la escala no bajaba y la grafica no daba. FgReservaMs)
            t -= FgReservaMs(q, idx);
            return Math.Max(periodo * 0.4, t);
        }

        // (1.2.4 ronda 7, FGREAL) La grafica de la generacion del motor que no se ve en el p95 del motor (va en otras colas o en la cadena), ms por
        // real con salida 4K en una RTX 4060 (gpu_indice 1,0), escalada por los pixeles de salida y la grafica. fg_reserva_ms= la pone a mano
        // (ms tal cual, sin escalar; -1 = la cuenta de aqui)
        double FgReservaMs(double q, double idx)
        {
            if (s.FgReservaMs >= 0) return s.FgReservaMs;
            double r = 0;
            if (XeFgMultNow > 1)   // (1.2.4 MOTORFG: XeSS FG del motor, EngineFgXe.cs; 1.2.4 ronda 6: DLSS-G ya no esta)
            {
                int n = Math.Max(1, Math.Min(3, margenInventadas));
                r = margenFgTipo == "xess" ? FgCosteXess * n : 4.5 + 1.5 * (n - 1);
            }
            else if (ffxFgOn) r = FgCosteFsr3;
            else if (FmWanted()) r = FgCosteFsrFlujo + FgCosteFsrMulti * Math.Max(1, FmK - 1);
            // (Enhanced FG: sin reserva. Medido 23-11, 4K + DLSS 5 nivel 4, x4: con ella la escala bajo DLSS 5 del 65 al 50 % -con un paron de
            // 0,1 s al rehacerlo- y Enhanced FG siguio igual -saltadas 241 / tarde 417 en 60 s frente a 212 / 589 sin ella-: su hilo, no la grafica)
            return r <= 0 ? 0 : Math.Max(1.0, r * q / idx);
        }

        // (FGREAL) costes por real (ms, salida 4K, RTX 4060 = gpu_indice 1,0; ver FgReservaMs y notas\FGREAL.md). Sacados de la sesion del
        // usuario del 23-11 12:27-12:36 (4K, modo 30, sin DLSS 5, nvidia-smi a 2,3-2,7 GHz: grafica de toda la app por real, menos la de sin
        // generacion, x gpu_indice 1,2): XeSS FG x2 ~6,3 ms en la 4060 Ti, FSR 3 FG x2 de AMD ~7,1, FSR FG x4 nuestra ~7,9 (Enhanced FG x4 ~2-5: sin reserva, ver arriba)
        const double FgCosteXess = 8.0, FgCosteFsr3 = 8.5, FgCosteFsrFlujo = 3.5, FgCosteFsrMulti = 2.0;

        string FgReservaTxt()
        {
            double q = Math.Max(0.1, outW * (double)outH / (3840.0 * 2160.0)), idx = s.GpuIndice > 0.05 ? s.GpuIndice : 1.0;
            double r = FgReservaMs(q, idx);
            return r > 0 ? string.Format(CultureInfo.InvariantCulture, ", generacion del motor {0:0.0} ms", r) : "";
        }

        // Una vez por cambio de modo: el tope al log (para ver en engine.log con que margen va)
        string topeVisto = "";
        void TopeGpuLog()
        {
            string k = s.Mode + "|" + (s.Is120 ? 1 : 0) + "|" + XeFgMultNow + "|" + s.SmoothMode + "|" + outW + "x" + outH + "|" + margenInventadas + margenFgTipo + "|" + FgReservaTxt();
            if (k == topeVisto) return;
            topeVisto = k;
            double periodo = s.Mode == 30 ? 2000.0 / 59.94 : s.Is120 ? 1000.0 / 119.88 : 1000.0 / 59.94;
            double t = TopeGpu();
            Log.Write(string.Format(CultureInfo.InvariantCulture, "Margen: tope de grafica {0:0.0} ms de {1:0.0} ({2:0} %; uso_grafica {3:0.00}, gpu_indice {4:0.00}{5})",
                t, periodo, 100 * t / periodo, s.UsoGrafica, s.GpuIndice, (s.SmoothMode > 0 ? ", Smooth Motion" + (s.SmoothMode == 3 ? " x4" : "") : "") + FgReservaTxt()));   // (FGREAL: y lo reservado para la generacion del motor)
        }
    }
}
