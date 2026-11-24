using System;
using System.Globalization;

namespace ConsoleEnhancer.Motor
{
    // EngineFps.cs - 21-11-2026 (el usuario: "haz que la app si detecta que no se llega a los fps seleccionados que te avise y te cambie la
    // configuracion"). Cada segundo (SecondTick, despues de AdaptaTick): si las imagenes reales presentadas se quedan por debajo del 90 % de
    // las del modo (30 / 60 / 120) Y la culpa es del PC (el p95 de la grafica pasa del 95 % del tiempo de una imagen, o imagenes tarde), 5 s
    // seguidos, se avisa al lanzador ("switch2-fps" "bajo" <presentadas> <objetivo> <gpu p95 ms>) y este baja la configuracion. Si el juego
    // de la consola va lento por si mismo, la grafica va sobrada y no se avisa. Tras abrir o cambiar algo (modo, generacion, entrada,
    // modelo: tambien lo que baja EngineAdapta) 10 s sin juzgar, para dejar a EngineAdapta probar primero; entre avisos, 30 s como poco.
    unsafe partial class Engine
    {
        int fpsMalN, fpsHist;
        long fpsAvisoAt, fpsCalmaDesde = Clock.Now;
        string fpsClave = "";

        void FpsVigila()
        {
            if (s.Escalera) return;   // (1.3.1, FPS: lo hace EngineEscalera.cs en 1-2 s y solo avisa con todo lo del motor ya bajado; escalera=0 = esto)
            string clave = s.Mode + "|" + (s.Fg ? 1 : 0) + "|" + inW + "x" + inH + "|" + s.Preset + "|" + s.FgTipo;   // (1.2.4 ronda 6, QUITAFG: el tipo de generacion -antes la propia x3 / x4, que ya no esta-)
            if (clave != fpsClave) { fpsClave = clave; fpsCalmaDesde = Clock.Now; fpsMalN = 0; fpsHist = 0; }
            if (inStandby || noSignalScreen || nosigSilencio || lastCapture == 0 || Clock.SinceMs(lastCapture) > 1500 || Clock.SinceMs(fpsCalmaDesde) < 10000)
            { fpsMalN = 0; fpsHist = 0; return; }
            double objetivo = s.Mode == 40 ? 39.96 : s.Mode == 30 ? 29.97 : s.Mode >= 100 ? 119.88 : 59.94;   // (1.3.2, MODO40)
            double tope = 1000.0 / objetivo;
            bool bajo = stats.LastRate < objetivo * 0.9;
            // (1.2.4 ronda 5, XEFGM) con XeSS FG (y la generacion propia, quitada en la ronda 6) las reales tambien se pierden por el pintado / la presentacion
            // con la grafica dentro del tope: cualquier imagen perdida cuenta como culpa del PC (la consola lenta no deja imagenes sin pintar).
            // Y 5 de los ultimos 7 s (antes 5 seguidos: con las perdidas a rachas -la prueba del usuario, x4 propia a 22 de 30 reales con 107
            // perdidas en 20 s- no avisaba nunca)
            bool pc = stats.LastGpuP95 > tope * 0.95 || stats.LastLate > 2 || (xeFgOn && xeFgEnabledNow && stats.LastLate > 0);
            fpsHist = ((fpsHist << 1) | (bajo && pc ? 1 : 0)) & 0x7F;
            int malos = 0;
            for (int b = 0; b < 7; b++) if ((fpsHist & (1 << b)) != 0) malos++;
            fpsMalN = malos;
            if (malos < 5 || (fpsAvisoAt != 0 && Clock.SinceMs(fpsAvisoAt) < 30000)) return;
            fpsMalN = 0; fpsHist = 0;
            fpsAvisoAt = Clock.Now;
            string r = stats.LastRate.ToString("0.0", CultureInfo.InvariantCulture), o = objetivo.ToString("0.00", CultureInfo.InvariantCulture),
                   g = stats.LastGpuP95.ToString("0.0", CultureInfo.InvariantCulture);
            Log.Write("Fps: no se llega al modo " + s.Mode + " (" + r + " de " + o + " imagenes reales/s, grafica p95 " + g + " ms de " +
                      tope.ToString("0.0", CultureInfo.InvariantCulture) + ", tarde " + stats.LastLate + ") 5 s seguidos: aviso al lanzador");
            Ev("switch2-fps", "bajo", r, o, g);
        }
    }
}
