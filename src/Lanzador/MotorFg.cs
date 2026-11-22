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
//  22-11 noche (pedido del usuario, "Si, pieza privada"): con engine\PrismaFg.dll (pieza privada de uso propio, NO se publica ni esta en
//  git, como PrismaNr.dll) y el paquete dlssg_sm86 del usuario, DLSS FG x3 / x4 tambien en el motor en RTX 40 (cadena 3: el motor se abre
//  con fg_mult=3|4 y la pieza prepara la generacion antes de Streamline). Sin la DLL, todo como arriba.
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

        // engine\PrismaFg.dll junto al motor y un paquete dlssg_sm86 (mirado como mucho cada 5 s)
        static DateTime pfgAt; static bool pfgOk; static string pfgExe;
        static readonly object pfgSync = new object();
        public static bool MotorFgPrivada(string enginePath)
        {
            try
            {
                string exe = string.IsNullOrEmpty(enginePath) ? Paths.AppDir + @"\engine\ConsoleEnhancerEngine.exe" : enginePath;
                lock (pfgSync)
                {
                    if (exe == pfgExe && (DateTime.Now - pfgAt).TotalSeconds < 5) return pfgOk;
                    pfgExe = exe; pfgAt = DateTime.Now;
                    string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    pfgOk = File.Exists(Path.Combine(Path.GetDirectoryName(exe), "PrismaFg.dll")) &&
                            (File.Exists(Path.Combine(Extras.Sm86, "version.dll")) || File.Exists(Path.Combine(Paths.MagpieDir, @"runtimes\dlssg_sm86\version.dll")) ||
                             File.Exists(Path.Combine(la, @"ConsoleEnhancer\extras\runtimes\dlssg_sm86\version.dll")));
                    return pfgOk;
                }
            }
            catch { return false; }
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
                    if (MotorFgFamilia() == "rtx50") return true;   // (x3 / x4 de NVIDIA por la via oficial: solo RTX 50)
                    return MotorFgFamilia() == "rtx40" && MotorFgPrivada(null);   // (RTX 40 con la pieza privada PrismaFg.dll)
                case "xess":
                    if (!MotorFgMulti) return false;
                    if (!File.Exists(Path.Combine(Paths.MagpieDir, "libxess_fg.dll")) || !File.Exists(Path.Combine(Paths.MagpieDir, "libxell.dll"))) return false;
                    return mult <= 2 || motorFgFab == "intel";
            }
            return false;
        }

        // La "cadena" con la que se abre el motor (no se cambia en marcha): 0 la de siempre, 1 FSR 3 FG, 3 DLSS FG x3 / x4 con PrismaFg.dll
        // (MOTORFG: la pieza tiene que preparar la generacion antes de Streamline: de x2 a x3 / x4 o al reves se vuelve a abrir; entre x3 y
        // x4 no) y XeSS FG xN = 10 x N (+1 con DLSS de escalador): 20, 21, 30, 31, 40, 41. (1.2.4, integracion: con el multiplicador y el
        // escalador dentro, porque XeSS FG fija sus interpoladas al crear la cadena y Streamline -solo DLSS SR- solo se carga si el motor se
        // abre con upscaler=dlss: si cambian, el motor se vuelve a abrir -CheckEngineFfx- en vez de quedarse con lo de antes o con FSR 1)
        public int EngineFgChain
        {
            get
            {
                motorFgFab = (GpuVendor ?? "").Trim().ToLowerInvariant();
                if (FgOff || HomeNative || Mode120) return 0;
                if (EngineFfxWanted) return 1;
                int m = MotorFgMult;
                if (MotorFgMulti && FgTypeNow == "xess" && MotorFgSabe("xess", m)) return 10 * m + (Ce.EngineUpscaler(this) == "dlss" ? 1 : 0);
                if (MotorFgMulti && FgTypeNow == "dlss" && m >= 3 && MotorFgFamilia() != "rtx50" && MotorFgSabe("dlss", m)) return 3;
                return 0;
            }
        }
        // El multiplicador que cuenta para el reparto (el mismo que mira FgMagpieOnly)
        int MotorFgMult { get { return Math.Max(2, Math.Min(4, MultOn && !Mode120 ? FgMultNow : 2)); } }
        public static bool ChainXess(int chain) { return chain >= 20; }   // (3 = DLSS FG x3 / x4 con PrismaFg.dll)

        // Lo que se añade a los argumentos del motor al abrirlo (EngineArgs)
        public string MotorFgArgs(int chain)
        {
            if (ChainXess(chain)) return " fg_tipo=xess fg_mult=" + Math.Max(2, Math.Min(4, chain / 10)).ToString(CultureInfo.InvariantCulture);
            if (chain == 3) return " fg_mult=" + MotorFgMult.ToString(CultureInfo.InvariantCulture);
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

        // Imagenes en la tele por cada una del juego con lo que hace el motor ahora (para el refresco fijo, ImagenCe.CeRefrescoFijoAhora):
        // DLSS FG x3 / x4 si el motor lo hace (RTX 50 o PrismaFg.dll); lo demas x2
        public int MotorFgFactorRefresco()
        {
            if (FgOff || FgTypeNow != "dlss") return 2;
            int m = Math.Max(2, Math.Min(4, FgMultNow));
            return m > 2 && MotorFgSabe("dlss", m) ? m : 2;
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
