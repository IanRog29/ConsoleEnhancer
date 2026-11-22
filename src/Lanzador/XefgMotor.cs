// XefgMotor.cs - 22-11-2026 noche (1.2.4, ronda de arreglos, agente XEFGM). C# 5
//
// La prueba del usuario con la 1.2.4 (21:28-21:39, GTA VI a 4K, modo 30, VRR, DLSS 5 por engine\PrismaNr.dll):
//  - XeSS FG x2 por el motor SI generaba ("presentadas 2, generacion 0" en engine.log = la inventada y la real, resultado bien), pero la
//    grafica se cayo dos veces (21:31:28 tras 54 s y 21:32:50 tras 2 s: DXGI_ERROR_DEVICE_HUNG; en el registro de Windows nvlddmkm 153
//    "Error occurred on GPUID" 0,7 s ANTES de que el motor viera nada) -> 3 s en negro cada vez. Las dos veces con DLSS 5 a la vez: esa
//    combinacion no se habia medido (MOTORFG probo XeSS FG del motor sin DLSS 5) y aqui no sale el cuelgue con la fuente de prueba ni con
//    escenas reales (ver notas\XEFGM.md). En la 1.2.3 XeSS FG + DLSS 5 iba por Magpie (su propia grafica D3D12 para XeSS FG) y no se caia.
//  - x4 / x3 con la generacion propia del motor + DLSS 5 a 4K: 12,7 y 22 reales de 29,97 (ver MotorPropiaNoLlega).
// Lo que hace (el reparto, sin propiedades que vuelvan a la cuenta del multiplicador: se usa dentro de FgMagpieOnly y de la proteccion):
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
        public static volatile bool MotorPropiaDlss5 = false;   // motor_propia_dlss5=0|1
        static volatile string motorGenCaida = "";              // " xess" / " propia": la grafica se perdio con ella en el motor (esta sesion)

        public bool CeParseXefgm(string key, string val)
        {
            string v = (val ?? "").Trim();
            switch (key)
            {
                case "motor_xefg_dlss5": MotorXefgDlss5 = v == "1"; return true;
                case "motor_propia_dlss5": MotorPropiaDlss5 = v == "1"; return true;
            }
            return false;
        }

        // Por donde haria el motor esta generacion: "xess" (XeSS FG del motor), "propia" (la generacion propia x3 / x4) o "" (otra / ninguna)
        static string MotorGenVia(string tipo, int mult)
        {
            mult = Math.Max(2, Math.Min(4, mult));
            if (tipo == "xess" && MotorFgSabe("xess", mult)) return "xess";
            if (mult >= 3 && (tipo == "xess" || tipo == "dlss") && MotorFgVia(tipo, mult) == "propia") return "propia";
            return "";
        }

        // DLSS 5 en el motor con lo de ahora (sin la cuenta de si cabe -EscalaAuto- ni FgMagpieOnly: sin recursion)
        bool MotorD5Simple { get { return !Dlss5Off && Integrado.Dlss5Available && EngineDlss5 && !MotorDlss5Roto && MotorDlss5Disponible; } }

        // La generacion propia x3 / x4 no llega a las reales del modo con DLSS 5 en el motor (medido 22-11 noche, ver notas\XEFGM.md)
        bool MotorPropiaNoLlega(int mult) { return mult >= 3 && MotorD5Simple && !MotorPropiaDlss5; }

        // ¿Esta generacion, que el motor sabe hacer, va por Magpie (como la 1.2.3) porque en el motor no es fiable o no llega?
        public bool MotorGenAMagpie(string tipo, int mult)
        {
            return MotorGenAMagpiePorQue(tipo, mult).Length > 0;
        }
        public string MotorGenAMagpiePorQue(string tipo, int mult)
        {
            try
            {
                if (Engine != "propio") return "";
                string via = MotorGenVia(tipo ?? "", mult);
                if (via.Length == 0) return "";
                if (motorGenCaida.Contains(" " + via)) return via == "xess" ? "XeSS FG: la grafica se perdio con el en el motor en esta sesion" : "la generacion propia: la grafica se perdio con ella en el motor en esta sesion";
                if (via == "xess" && MotorD5Simple && !MotorXefgDlss5) return "XeSS FG con DLSS 5: por Magpie como en la 1.2.3 (motor_xefg_dlss5=1 = por el motor)";
                if (via == "propia" && MotorPropiaNoLlega(mult)) return "la generacion propia x" + Math.Max(3, Math.Min(4, mult)) + " con DLSS 5 no llega a las reales: por Magpie como en la 1.2.3 (motor_propia_dlss5=1 = por el motor)";
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
        // RunMpv, el motor se ha cerrado solo (codigo != 0): con la grafica perdida (3) o parado demasiado (5) y XeSS FG / la generacion propia
        // en el, esa generacion va por Magpie el resto de la sesion (el "se vuelve a abrir" de despues ya lo coge: FgMagpieOnly)
        void MotorGenCaida(Process p, int code)
        {
            try
            {
                if (code != 3 && code != 5) return;
                int ch = Config.EngineFfxOf(p);
                string via = Config.ChainXess(ch) ? "xess" : (cfg != null && cfg.MotorFgPropiaAhora) ? "propia" : "";
                if (via.Length == 0) return;
                Config.MotorGenCaidaAnota(via);
                Log.Write("Motor: la grafica se ha " + (code == 3 ? "perdido" : "parado") + " con " + (via == "xess" ? "XeSS FG x" + Math.Max(2, ch / 10) : "la generacion propia") +
                          " en el motor (codigo " + code.ToString(CultureInfo.InvariantCulture) + "): el resto de la sesion esa generacion va por Magpie, como en la 1.2.3");
            }
            catch (Exception ex) { Log.Error("Motor: generacion tras la caida", ex); }
        }
    }
}
