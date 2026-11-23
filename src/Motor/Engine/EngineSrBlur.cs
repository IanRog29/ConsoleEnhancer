using System;

namespace ConsoleEnhancer.Motor
{
    // EngineSrBlur.cs - 23-11-2026 (1.2.4 ronda 7, agente SRBLUR; el usuario, tras probar con su PS5: "Mejora la borrosidad de los
    // reescaladores"; antes: "todos los dlss siguen teniendo borrosidades en sitios al girar la camara y en el suelo sin ni siquiera girar").
    unsafe partial class Engine
    {
        // (Engine.cs AutoPresetTick) El modelo elegido no se cambia por el pico de rehacer DLSS 5. Log del usuario 23-11 12:10:40-41 y
        // 12:11:43-44 (1440p, DLSS M, DLSS 5 nivel 4): Adapta bajo DLSS 5 del 90 al 80 % (lo que tocaba: con DLSS 5 primero se baja su %) y
        // al segundo siguiente "DLSS M no cabe aqui (grafica p95 46,1 / 91,2 ms, tope 23,4 / 30)": el p95 era el de la imagen que rehacia
        // DLSS 5. AdaptaBajar ponia overSecs = 0, pero AutoPresetTick va detras en el mismo segundo y lo volvia a contar: al siguiente, M -> K, y
        // se aprendio (scales_engine.txt "preset=K pct=80 pedido=M": las veces siguientes, K de entrada; 12:14:21 "DLSS M -> K").
        // Ahora: con adapta=1 y DLSS 5 que aun puede bajar (mas del 25 % o mas de una pasada), lo que no cabe lo baja Adapta (como dice
        // EngineAdapta.cs) y el modelo no; y nunca en los 5 s tras rehacer DLSS 5 ni en los 8 s tras un cambio de Adapta (los suyos)
        bool AutoPresetEspera()
        {
            if (nrCreatedAt != 0 && Clock.SinceMs(nrCreatedAt) < 5000) return true;
            if (!s.Adapta) return false;
            if (adCambioAt != 0 && Clock.SinceMs(adCambioAt) < 8000) return true;
            return AdNrActivo && (s.NrPct > 25 || s.NrPasses > 1);
        }
    }
}
