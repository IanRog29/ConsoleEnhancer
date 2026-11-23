// PruebaPerfiles6.cs - 23-11 (1.2.4 ronda 6, agente PERFILES): prueba sin consola de lo arreglado con los logs del usuario del 22-11 noche
// y del 23-11 (PS5; la 1.2.4 retirada en Downloads\ConsoleEnhancer-v1.2.4 01:30-02:54 y la prueba en ConsoleEnhancer-v1.2.4-prueba desde
// las 03:13). Va dentro de --test-juegos-consola (PruebaPerfiles.Run). C# 5
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ConsoleEnhancer
{
    static partial class PruebaPerfiles
    {
        static void Ronda6()
        {
            sb.Append("\r\n-- 1.2.4 ronda 6 (PERFILES): perfiles y fps en los logs del 22-11 noche y del 23-11 --\r\n");
            RefrescoFijo120();
        }

        // 1. El refresco fijo del motor (G-SYNC apagado en su perfil) en el modo 120: log 23-11 02:49:16 (1.2.4 retirada) y 03:14:23 (prueba)
        //    "Refresco fijo del motor en el 60 con DLSS FG (motor_60fg_refresco_fijo=auto): si (... frente a 2 x 59.94)" con Rocket League a
        //    120 y "Refresco del motor: fijo (modo 60 con DLSS FG ...): se vuelve a abrir el motor". En el 120 no hay generacion: nunca
        static void RefrescoFijo120()
        {
            Config c = new Config();
            c.CeRefrescoFijo = "1";   // (sin depender de la tele de este PC: "si" en cuanto toque)
            c.EngineFixed60Fg = true; c.CapMode = false; c.FgOff = false; c.Mode120 = false; c.HomeNative = false;
            bool en60 = c.EngineFixedWanted;
            c.Mode120 = true;
            bool en120 = c.EngineFixedWanted;
            // (la cuenta de antes, sin mirar el modo 120)
            bool antes120 = c.EngineFixed60Fg && c.CeRefrescoFijoAhora() && (!c.CapMode || c.MotorFgFactorRefresco() == 4) && !c.FgOff && !c.EngineSmoothFor(false) && !NvProfiles.EngineFixedBroken;
            Mira(en60, "refresco fijo en el 60 con generacion (motor_60fg_refresco_fijo=1): " + (en60 ? "si" : "no") + " (como antes)");
            Mira(!en120, "refresco fijo en el modo 120 (Rocket League, 23-11 02:49:16 / 03:14:23): " + (en120 ? "si" : "no") + " (antes " + (antes120 ? "si: el motor se volvia a abrir con G-SYNC apagado" : "no") + ")");
            c.HomeNative = true;
            bool menu120 = c.EngineFixedWanted;
            Mira(!menu120, "en el menu de la consola con el perfil en el modo 120: " + (menu120 ? "si" : "no"));
            c.Mode120 = false; c.HomeNative = true;
            bool menu60 = c.EngineFixedWanted;
            Mira(menu60, "en el menu de la consola con el perfil en el 60 con generacion: " + (menu60 ? "si" : "no") + " (como antes: no se reabre al entrar y salir)");
        }
    }
}
