// XefgMotor.cs - 22-11-2026 noche (1.2.4, ronda de arreglos, agente XEFGM). C# 5
//
// La prueba del usuario con la 1.2.4 (21:28-21:39, GTA VI a 4K, modo 30, VRR, DLSS 5 por engine\PrismaNr.dll):
//  - XeSS FG x2 por el motor SI generaba ("presentadas 2, generacion 0" en engine.log = la inventada y la real, resultado bien), pero la
//    grafica se cayo dos veces (21:31:28 tras 54 s y 21:32:50 tras 2 s: DXGI_ERROR_DEVICE_HUNG; en el registro de Windows nvlddmkm 153
//    "Error occurred on GPUID" 0,7 s ANTES de que el motor viera nada) -> 3 s en negro cada vez. Las dos veces con DLSS 5 a la vez: esa
//    combinacion no se habia medido (MOTORFG probo XeSS FG del motor sin DLSS 5) y aqui no sale el cuelgue con la fuente de prueba ni con
//    escenas reales (ver notas\XEFGM.md). En la 1.2.3 XeSS FG + DLSS 5 iba por Magpie (su propia grafica D3D12 para XeSS FG) y no se caia.
//  - x4 / x3 con la generacion propia del motor + DLSS 5 a 4K: 12,7 y 22 reales de 29,97 (ver MotorPropiaNoLlega).
//    Medido 22-11 noche con escenas reales de la PS5 desde un fichero (test=archivo, 4K, juego a 30, x4): el motor de la 1.2.4 sin DLSS 5
//    29,4 reales (10-11 perdidas cada 20 s) y con DLSS 5 25,2 (64-97 perdidas) -> con el hilo de presentacion nuevo (EngineFgPropiaHilo.cs)
//    29,97 / 0 sin DLSS 5 y 29,4-29,9 / 0-21 con DLSS 5 al 45 % (sin margen, y con la imagen real del usuario la grafica le costaba el
//    doble o mas): con DLSS 5, por Magpie. XeSS FG + DLSS 5 con las mismas escenas, 180 s: 30,0 reales, bien, sin cuelgue (aqui no sale).
// (1.2.4 ronda 6, QUITAFG: sin la generacion propia x3 / x4 -fuera, como los multiplicadores-: queda lo de XeSS FG)
// Lo que hace (el reparto, sin propiedades que vuelvan a la cuenta de la generacion: se usa dentro de FgMagpieOnly y de la proteccion):
//  1. XeSS FG (x2; x3 / x4 en Intel) en el motor CON DLSS 5 en el motor -> por Magpie, como la 1.2.3 (ini motor_xefg_dlss5=1 = por el motor).
//     Sin DLSS 5 sigue por el motor (medido por MOTORFG: 30,0 reales, todas a 2 refrescos).
//  2. La generacion propia x3 / x4 donde no llega (MotorPropiaNoLlega: con DLSS 5 en el motor) -> por Magpie como la 1.2.3 (ini
//     motor_propia_dlss5=1 = por el motor igual).
//  3. Si la grafica se pierde (el motor sale con el codigo 3 o 5) con XeSS FG o la generacion propia en el motor, el resto de la sesion esa
//     generacion va por Magpie (una caida como mucho, no una tras otra).
using System;
using System.Diagnostics;
using System.Globalization;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public static volatile bool MotorXefgDlss5 = false;     // motor_xefg_dlss5=0|1
        static volatile string motorGenCaida = "";              // " xess": la grafica se perdio con el en el motor (esta sesion)

        public bool CeParseXefgm(string key, string val)
        {
            string v = (val ?? "").Trim();
            switch (key)
            {
                case "motor_xefg_dlss5": MotorXefgDlss5 = v == "1"; return true;
            }
            return false;
        }

        // DLSS 5 en el motor con lo de ahora (sin la cuenta de si cabe -EscalaAuto- ni FgMagpieOnly: sin recursion)
        bool MotorD5Simple { get { return !Dlss5Off && Integrado.Dlss5Available && EngineDlss5 && !MotorDlss5Roto && MotorDlss5Disponible; } }

        // ¿Esta generacion, que el motor sabe hacer, va por Magpie (como la 1.2.3) porque en el motor no es fiable?
        public bool MotorGenAMagpie(string tipo)
        {
            return MotorGenAMagpiePorQue(tipo).Length > 0;
        }
        public string MotorGenAMagpiePorQue(string tipo)
        {
            try
            {
                if (Engine != "propio" || tipo != "xess" || !MotorFgSabe("xess")) return "";
                if (motorGenCaida.Contains(" xess")) return "XeSS FG: la grafica se perdio con el en el motor en esta sesion";
                if (MotorD5Simple && !MotorXefgDlss5) return "XeSS FG con DLSS 5: por Magpie como en la 1.2.3 (motor_xefg_dlss5=1 = por el motor)";
            }
            catch { }
            return "";
        }

        // El motor salio por la grafica perdida (3) o parado (5) con esta generacion: el resto de la sesion por Magpie
        public static void MotorGenCaidaAnota(string via)
        {
            if (string.IsNullOrEmpty(via) || motorGenCaida.Contains(" " + via)) return;
            motorGenCaida = motorGenCaida + " " + via;
        }
        // (pruebas)
        public static void MotorGenCaidaOlvida() { motorGenCaida = ""; }
    }

    partial class TrayApp
    {
        // RunMpv, el motor se ha cerrado solo (codigo != 0): con la grafica perdida (3) o parado demasiado (5) y XeSS FG en el, XeSS FG va
        // por Magpie el resto de la sesion (el "se vuelve a abrir" de despues ya lo coge: FgMagpieOnly)
        void MotorGenCaida(Process p, int code)
        {
            try
            {
                if (code != 3 && code != 5) return;
                if (!Config.ChainXess(Config.EngineFfxOf(p))) return;
                Config.MotorGenCaidaAnota("xess");
                Log.Write("Motor: la grafica se ha " + (code == 3 ? "perdido" : "parado") + " con XeSS FG en el motor (codigo " + code.ToString(CultureInfo.InvariantCulture) +
                          "): el resto de la sesion XeSS FG va por Magpie, como en la 1.2.3");
            }
            catch (Exception ex) { Log.Error("Motor: generacion tras la caida", ex); }
        }
    }
}
