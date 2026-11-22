// MotorFg.cs - 22-11-2026 (1.2.4, agente MOTORFG). El usuario: "y no se podria pasar todo al motor para que funcione mejor?".
// La generacion que el motor propio sabe hacer ahora (src\Motor\Engine\EngineFgMulti.cs y EngineFgXe.cs) y lo que se le pasa:
//  - DLSS FG x3 / x4: Streamline, via oficial (numFramesToGenerate 2 / 3), solo donde el runtime de NVIDIA lo admite (RTX 50). En
//    RTX 40 / 30 / 20 el motor no hace x3 / x4 de DLSS FG: la regla del usuario (22-11) es que en el motor no haya nada que se salte
//    comprobaciones de NVIDIA, y el paquete comunitario dlssg_sm86 solo funciona redirigiendo la carga de NGX.
//  - XeSS FG x2 (y x3 / x4 en graficas Intel, por su camino nativo): libxess_fg.dll + libxell.dll de magpie\ (de Intel,
//    redistribuibles), con los vectores del motor. Fuera de Intel, x3 / x4 de XeSS FG solo salen parcheando la DLL de Intel en memoria
//    (lo que hace Magpie), y su licencia no lo permite: el motor no lo hace.
//    Con XeSS FG el motor lleva la cadena de presentacion de XeSS FG; con DLSS de escalador, Streamline solo para DLSS SR (sin sus
//    ganchos de DXGI, fg_dlss_sr=1 en el motor).
//  El reparto (SOLOMOTOR, src\Lanzador\SoloMotor.cs: Config.MotorSabeGeneracion) llama a MotorFgSabe; el motor se abre con
//  MotorFgArgs (fg_tipo / fg_mult) y se reabre si cambia su "cadena" (EngineFgChain), como ya pasaba con FSR 3 FG.
//  Si el motor no puede (switch2-fgmult no <pedido> <da>), en esta sesion esa combinacion no se le vuelve a pedir.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // ini motor_fg_multi=1|0 (de serie 1): XeSS FG y DLSS FG x3 / x4 (RTX 50) en el motor; 0 = como la 1.2.3 (por Magpie)
        public static volatile bool MotorFgMulti = true;
        // gpu_fabricante del ini (lo copia EngineFgChain; "" = sin detectar: como NVIDIA)
        static volatile string motorFgFab = "";
        public static void MotorFgFabricante(string v) { motorFgFab = (v ?? "").Trim().ToLowerInvariant(); }   // (al leer gpu_fabricante del ini)
        // Lo que el motor dijo que no puede en esta sesion ("dlss4", "xess3"...)
        static readonly HashSet<string> motorFgNo = new HashSet<string>();

        // La familia de la grafica (la de la escala automatica: escala_gpu_familia del ini o data\hardware.txt): rtx20 / 30 / 40 / 50 / otra
        static string MotorFgFamilia()
        {
            try { CadenaEscala c = new CadenaEscala(); EscalaAuto.GpuPara(c); return (c.Familia ?? "").ToLowerInvariant(); }
            catch { return ""; }
        }

        // ¿Sabe el motor hacer esta generacion con este multiplicador en este PC? (con XeSS FG y DLSS de escalador el motor va con DLSS SR
        // sin los ganchos de Streamline: tambien vale)
        public static bool MotorFgSabe(string tipo, int mult)
        {
            mult = Math.Max(2, Math.Min(4, mult));
            lock (motorFgNo) if (motorFgNo.Contains((tipo ?? "") + mult)) return false;
            switch (tipo ?? "")
            {
                case "dlss":
                    if (mult <= 2) return true;
                    if (!MotorFgMulti) return false;
                    if (motorFgFab != "" && motorFgFab != "nvidia") return false;
                    return MotorFgFamilia() == "rtx50";   // (x3 / x4 de NVIDIA por la via oficial: solo RTX 50)
                case "xess":
                    if (!MotorFgMulti) return false;
                    if (!File.Exists(Path.Combine(Paths.MagpieDir, "libxess_fg.dll")) || !File.Exists(Path.Combine(Paths.MagpieDir, "libxell.dll"))) return false;
                    return mult <= 2 || motorFgFab == "intel";
            }
            return false;
        }

        // La "cadena" con la que se abre el motor (no se cambia en marcha): 0 la de siempre, 1 FSR 3 FG, 10 x N (+1 con DLSS de escalador)
        // XeSS FG xN: 20, 21, 30, 31, 40, 41 (todas > 1). (1.2.4, integracion: con el multiplicador y el escalador dentro, porque XeSS FG fija
        // sus interpoladas al crear la cadena y Streamline -solo DLSS SR- solo se carga si el motor se abre con upscaler=dlss: si cambian, el
        // motor se vuelve a abrir -CheckEngineFfx- en vez de quedarse con lo de antes o con FSR 1 sin decirlo)
        public int EngineFgChain
        {
            get
            {
                motorFgFab = (GpuVendor ?? "").Trim().ToLowerInvariant();
                if (FgOff || HomeNative || Mode120) return 0;
                if (EngineFfxWanted) return 1;
                int m = MotorFgMult;
                if (MotorFgMulti && FgTypeNow == "xess" && MotorFgSabe("xess", m)) return 10 * m + (Ce.EngineUpscaler(this) == "dlss" ? 1 : 0);
                return 0;
            }
        }
        // El multiplicador que cuenta para el reparto (el mismo que mira FgMagpieOnly)
        int MotorFgMult { get { return Math.Max(2, Math.Min(4, MultOn && !Mode120 ? FgMultNow : 2)); } }
        public static bool ChainXess(int chain) { return chain >= 2; }

        // Lo que se añade a los argumentos del motor al abrirlo (EngineArgs)
        public string MotorFgArgs(int chain)
        {
            if (ChainXess(chain)) return " fg_tipo=xess fg_mult=" + Math.Max(2, Math.Min(4, chain / 10)).ToString(CultureInfo.InvariantCulture);
            return "";
        }

        // En marcha (EngineLive): el multiplicador de DLSS FG (x3 / x4 solo si el motor lo sabe hacer; si no, 2)
        public string MotorFgLive()
        {
            if (FgOff || FgTypeNow != "dlss") return "";
            int m = Math.Max(2, Math.Min(4, FgMultNow));
            if (m > 2 && !MotorFgSabe("dlss", m)) m = 2;
            return " fg_mult=" + m.ToString(CultureInfo.InvariantCulture);
        }

        // El motor dijo "switch2-fgmult no <pedido> <da>": esa combinacion no se le pide mas en esta sesion
        public static void MotorFgNoPuede(string tipo, int pedido)
        {
            lock (motorFgNo) motorFgNo.Add((tipo ?? "") + pedido);
        }
    }

    partial class TrayApp
    {
        // MpvReader: {"event":"client-message","args":["switch2-fgmult","no","<pedido>","<da>"]} del motor (EngineFgMulti.cs / EngineFgXe.cs)
        bool MotorFgEvento(string line)
        {
            if (!line.Contains("\"args\":[\"switch2-fgmult\"")) return false;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line, "\"switch2-fgmult\",\"([^\"]*)\",\"(\\d)\",\"(\\d)\"");
            if (!m.Success || m.Groups[1].Value != "no") return true;
            int pedido = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), da = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            PrepareCfg();
            string tipo = cfg.FgTypeNow;
            Config.MotorFgNoPuede(tipo, pedido);
            Log.Write("Motor: su " + tipo + " x" + pedido + " no va en esta grafica (da x" + da + "): en esta sesion no se le vuelve a pedir");
            if (!cfg.FgOff)
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try { System.Threading.Thread.Sleep(300); RestartLs(tipo.ToUpperInvariant() + " FG x" + pedido, Tx.T("el motor no puede con ese multiplicador en esta gráfica", "the engine can't do that multiplier on this GPU")); }
                    catch (Exception ex) { Log.Error("generacion del motor: otro camino", ex); }
                });
            return true;
        }
    }
}
