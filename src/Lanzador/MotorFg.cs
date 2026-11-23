// MotorFg.cs - 22-11-2026 (1.2.4, agente MOTORFG). La generacion que el motor propio sabe hacer ademas de Smooth Motion y lo que se le pasa.
// (1.2.4 ronda 6, QUITAFG: fuera DLSS FG -DLSS-G de Streamline-, los multiplicadores x3 / x4, la generacion propia del motor y la pieza
// PrismaFg.dll. Queda:)
//  - XeSS FG x2: libxess_fg.dll + libxell.dll de magpie\ (de Intel, redistribuibles), con los vectores del motor (src\Motor\Engine\EngineFgXe.cs).
//    Con XeSS FG el motor lleva la cadena de presentacion de XeSS FG; con DLSS de escalador, Streamline solo para DLSS SR (sin sus ganchos de
//    DXGI, fg_dlss_sr=1 en el motor).
//  - FSR 3 FG x2: la cadena de FidelityFX (EngineFgFfx.cs; con DLSS de escalador va por Magpie, Config.FgMagpieOnly).
//  El reparto (SOLOMOTOR, src\Lanzador\SoloMotor.cs: Config.MotorSabeGeneracion) llama a MotorFgSabe; el motor se abre con MotorFgArgs
//  (fg_tipo=xess) y se reabre si cambia su "cadena" (EngineFgChain), como con FSR 3 FG.
//  Si XeSS FG no arranca en el motor (switch2-fgfallo xess), en esta sesion va por Magpie.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // ini motor_fg_multi=1|0 (de serie 1): XeSS FG en el motor; 0 = como la 1.2.3 (por Magpie). (El nombre es de la 1.2.4, cuando tambien
        // eran los x3 / x4)
        public static volatile bool MotorFgMulti = true;
        // gpu_fabricante del ini (lo copia EngineFgChain; "" = sin detectar: como NVIDIA)
        static volatile string motorFgFab = "";
        public static void MotorFgFabricante(string v) { motorFgFab = (v ?? "").Trim().ToLowerInvariant(); }   // (al leer gpu_fabricante del ini)
        // Lo que el motor dijo que no puede en esta sesion ("xess")
        static readonly HashSet<string> motorFgNo = new HashSet<string>();

        // ¿Sabe el motor hacer esta generacion en este PC? (con XeSS FG y DLSS de escalador el motor va con DLSS SR sin los ganchos de
        // Streamline: tambien vale)
        public static bool MotorFgSabe(string tipo)
        {
            lock (motorFgNo) if (motorFgNo.Contains(tipo ?? "")) return false;
            switch (tipo ?? "")
            {
                case "xess":
                    if (!MotorFgMulti) return false;
                    return File.Exists(Path.Combine(Paths.MagpieDir, "libxess_fg.dll")) && File.Exists(Path.Combine(Paths.MagpieDir, "libxell.dll"));
            }
            return false;
        }

        // La "cadena" con la que se abre el motor (no se cambia en marcha): 0 la de siempre (sin generacion o Smooth Motion, que es del
        // driver), 1 FSR 3 FG, 20 XeSS FG (21 con DLSS de escalador: Streamline solo se carga si el motor se abre con upscaler=dlss; si cambia,
        // el motor se vuelve a abrir -CheckEngineFfx-). (1.2.4: 3 = DLSS FG x3 / x4 con PrismaFg.dll y 30 / 31 / 40 / 41 = XeSS FG x3 / x4, ya no)
        public int EngineFgChain
        {
            get
            {
                motorFgFab = (GpuVendor ?? "").Trim().ToLowerInvariant();
                if (FgOff || HomeNative || Mode120) return 0;
                if (EngineFfxWanted) return 1;
                if (FgTypeNow == "xess" && MotorFgSabe("xess") && !MotorGenAMagpie("xess")) return 20 + (Ce.EngineUpscaler(this) == "dlss" ? 1 : 0);   // (1.2.4 ronda 5, XEFGM: salvo si va por Magpie, XefgMotor.cs)
                return 0;
            }
        }
        public static bool ChainXess(int chain) { return chain >= 20; }

        // Lo que se añade a los argumentos del motor al abrirlo (EngineArgs)
        public string MotorFgArgs(int chain)
        {
            return ChainXess(chain) ? " fg_tipo=xess" : "";
        }

        // XeSS FG no arranco en el motor (switch2-fgfallo xess): en esta sesion no se le vuelve a pedir (va por Magpie)
        public static void MotorFgNoPuede(string tipo)
        {
            lock (motorFgNo) motorFgNo.Add(tipo ?? "");
        }
    }

    partial class TrayApp
    {
        // MpvReader: {"event":"client-message","args":["switch2-fgfallo","xess"]} del motor (EngineFgXe.cs: XeSS FG pedido y no arrancado)
        bool MotorFgEvento(string line)
        {
            if (!line.Contains("\"args\":[\"switch2-fgfallo\"")) return false;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line, "\"switch2-fgfallo\",\"([^\"]*)\"");
            if (!m.Success) return true;
            string tipo = m.Groups[1].Value;
            if (tipo != "xess") return true;
            Config.MotorFgNoPuede(tipo);
            Log.Write("Motor: su XeSS FG no ha arrancado en esta grafica: en esta sesion va por Magpie");
            PrepareCfg();
            if (!cfg.FgOff && cfg.FgTypeNow == "xess")
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try { System.Threading.Thread.Sleep(300); RestartLs("XeSS FG", Tx.T("el motor no ha podido arrancarlo: va por otro camino", "the engine couldn't start it: using another path")); }
                    catch (Exception ex) { Log.Error("generacion del motor: otro camino", ex); }
                });
            return true;
        }
    }
}
