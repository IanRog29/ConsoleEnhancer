// EngineCaidas.cs - 23-11-2026 (1.2.4, sexta ronda, agente CAIDAS; el usuario: "Ha acabado crasheando y han pasado pantallazos negros
// antes"). Imagen congelada 2,2-3,3 s en CADA cambio de captura con el mando (4K <-> 1440p) con Smooth Motion en su sesion (01:58:39,
// 02:08:22, 02:16:07, 02:17:25: "Grafica: el pintado lleva 1202 ms parado", "La imagen vuelve tras 2161 ms parada"), y 1,2-1,7 s tras
// despertar un motor de Smooth Motion (la prueba df2dcfc, 03:19:27, 03:23:36, 03:25:10: 11-20 reales tiradas seguidas). Causa: el
// modelo de la cola de la tele (EngineLatencia.cs, cola_tirar_ms). Al cambiar de captura pasan 150-300 ms sin imagen y el lanzador pone
// el aviso "Captura 1440p" (2,5 s); con aviso el bucle repinta la ultima imagen cada 20 ms y cada repintado suma 4 refrescos (33,3 ms)
// a la cola estimada: sube a ~76 ms, todas las reales se tiran ("esperaria 76.0 ms (Smooth Motion): se tira"), latLastReal no avanza,
// se sigue repintando y la cola no baja hasta que se acaba el aviso (2,9 s). Ahora: (a) con la cola estimada ya por encima de
// cola_tirar_ms no se repinta (un repintado que no se veria antes solo retrasa a la real) y (b) como mucho cola_tirar_seguidas (2)
// reales tiradas seguidas: la siguiente pasa siempre (el modelo es una estimacion; nunca mas de ~67 ms sin imagen nueva por su culpa).
// Simulado con el mismo bucle (sim_cola.py: llegadas cada 33,37 ms, 200 ms sin imagen, aviso de 2,9 s): antes 3036 ms sin ninguna real,
// ahora 300 ms (los 200 sin imagen + la primera). cola_sin_repintar=0 y cola_tirar_seguidas=0 = como antes. C# 5.
using System;
using System.Globalization;

namespace ConsoleEnhancer.Motor
{
    partial class Settings
    {
        public int QueueDropRun = 2;         // cola_tirar_seguidas=N: con cola_tirar_ms, como mucho N reales tiradas seguidas (0 = sin tope, como antes)
        public bool QueueSkipRepaint = true; // cola_sin_repintar=0|1: con Smooth Motion y la cola estimada llena, sin repintar la ultima (0 = como antes)

        bool ApplyCaidas(string key, string val)
        {
            switch (key)
            {
                case "cola_tirar_seguidas": QueueDropRun = Math.Max(0, I(val)); return true;
                case "cola_sin_repintar": QueueSkipRepaint = B(val); return true;
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        int latDropRun;          // reales tiradas seguidas por cola_tirar_ms (0 al presentar una real)
        int latRunCapped, latRepaintSkips;
        long latRunCappedSaidAt, latRepaintSkipSaidAt;

        // QueueModelSkip, con la cola estimada x >= cola_tirar_ms: true = esta real NO se tira (ya van cola_tirar_seguidas seguidas)
        bool QueueDropCapped(double x, long now)
        {
            if (s.QueueDropRun > 0 && latDropRun >= s.QueueDropRun)
            {
                latRunCapped++;
                if (latRunCappedSaidAt == 0 || Clock.Ms(latRunCappedSaidAt, now) > 20000)
                {
                    latRunCappedSaidAt = now;
                    Log.Write(string.Format(CultureInfo.InvariantCulture, "Cola de la tele: ya van {0} imagenes tiradas seguidas (la cola estimada sigue en {1:0.0} ms): esta pasa (cola_tirar_seguidas; {2} veces)",
                        latDropRun, x, latRunCapped));
                }
                return true;
            }
            latDropRun++;
            return false;
        }

        // Bucle de pintado, antes de repintar la ultima (slot < 0): true = no se repinta. Con Smooth Motion y la cola estimada ya por encima
        // de cola_tirar_ms el repintado esperaria en la cola de la tele y sumaria otros 4 refrescos: la real siguiente se tiraria por el
        bool LatSkipRepaint()
        {
            if (!s.QueueSkipRepaint || s.QueueDropMs <= 0 || s.SmoothMode <= 0) return false;
            long now = Clock.Now;
            double x = QueueModelPeek(now);
            if (x < s.QueueDropMs) return false;
            latRepaintSkips++;
            if (latRepaintSkipSaidAt == 0 || Clock.Ms(latRepaintSkipSaidAt, now) > 20000)
            {
                latRepaintSkipSaidAt = now;
                Log.Write(string.Format(CultureInfo.InvariantCulture, "Cola de la tele: sin repintar la ultima (la cola estimada va en {0:0.0} ms con Smooth Motion: la real siguiente pasa antes; {1} veces)", x, latRepaintSkips));
            }
            return true;
        }
    }
}
