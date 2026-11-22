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

        // ---- (1.2.4, integracion; decision del coordinador: "todo por el motor y legal") la GENERACION PROPIA del motor (EngineFgPropia.cs,
        // DLSS5LEGAL: codigo propio con los vectores del motor, cualquier grafica) entra en el reparto para x3 / x4. Orden para x3 / x4:
        //   1. el motor por la via oficial (DLSS FG x3 / x4 en RTX 50 por Streamline; XeSS FG x3 / x4 en Intel) -> "oficial"
        //   2. el motor con la pieza privada PrismaFg.dll (DLSS FG x3 / x4 en RTX 40), si esta                    -> "privada"
        //   3. el motor con la generacion propia (x3 / x4 en RTX 20/30/40 sin la pieza, AMD / Intel sin otra)    -> "propia"
        //   4. Magpie solo si nada de eso vale (motor_fg_propia=0, motor_fg_multi=0, o el motor dijo que no)        -> ""
        // DLSS FG x2 / XeSS FG x2 siguen como estaban. ini motor_fg_propia=1|0 (de serie 1; 0 = x3 / x4 sin la propia: Magpie como antes)
        public static volatile bool MotorFgPropia = true;
        public static bool MotorFgPropiaSabe(int mult)
        {
            if (mult < 3 || mult > 4 || !MotorFgMulti || !MotorFgPropia) return false;
            lock (motorFgNo) return !motorFgNo.Contains("propia" + mult);
        }
        // Por donde haria el motor esta generacion x mult (x3 / x4; con x2: "" = la de siempre)
        public static string MotorFgVia(string tipo, int mult)
        {
            mult = Math.Max(2, Math.Min(4, mult));
            if (mult <= 2 || (tipo != "dlss" && tipo != "xess")) return "";
            if (MotorFgSabe(tipo, mult)) return tipo == "dlss" && MotorFgFamilia() != "rtx50" ? "privada" : "oficial";
            return MotorFgPropiaSabe(mult) ? "propia" : "";
        }
        // ¿La haria el motor de verdad con lo de ahora? (DLSS 5 sin engine\PrismaNr.dll manda la imagen a Magpie: ahi no). Sin propiedades que
        // vuelvan a la cuenta del multiplicador (se usa dentro de MultBloqueado / la prevision)
        public string MotorFgViaAhora(string tipo, int mult)
        {
            if (Engine != "propio" || !(Dlss5Off || MotorDlss5Disponible)) return "";
            return MotorFgVia(tipo, mult);
        }
        // La generacion propia con lo que va ahora (EngineLive: fg=0 fg_propia=N)
        public bool MotorFgPropiaAhora
        {
            get
            {
                if (FgOff || HomeNative || Mode120 || !MultOn) return false;
                string t = FgTypeNow;
                return (t == "dlss" || t == "xess") && MotorFgVia(t, MotorFgMult) == "propia";
            }
        }
        // " (FG propia)" / " (own FG)" para los textos del multiplicador (menu, bandeja, avisos, log)
        public string MotorFgViaNota(string tipo, int mult)
        {
            return MotorFgViaAhora(tipo, mult) == "propia" ? Tx.T(" (FG propia)", " (own FG)") : "";
        }

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
        // (1.2.4, integracion: y la generacion propia, fg_propia=N con fg=0 -EngineLive-; fg_propia=0 en lo demas, para quitarla en marcha)
        public string MotorFgLive()
        {
            if (MotorFgPropiaAhora) return " fg_propia=" + MotorFgMult.ToString(CultureInfo.InvariantCulture);
            if (FgOff || FgTypeNow != "dlss") return " fg_propia=0";
            int m = Math.Max(2, Math.Min(4, FgMultNow));
            if (m > 2 && !MotorFgSabe("dlss", m)) m = 2;
            return " fg_mult=" + m.ToString(CultureInfo.InvariantCulture) + " fg_propia=0";
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
        // (1.2.4, integracion) y ["switch2-fgpropia","rota","<N>"] (EngineFgPropia.cs: la generacion propia fallo): sin ella en esta sesion
        bool MotorFgEvento(string line)
        {
            if (line.Contains("\"args\":[\"switch2-fgpropia\""))
            {
                System.Text.RegularExpressions.Match mp = System.Text.RegularExpressions.Regex.Match(line, "\"switch2-fgpropia\",\"([^\"]*)\",\"(\\d)\"");
                if (!mp.Success || mp.Groups[1].Value != "rota") return true;
                int k = int.Parse(mp.Groups[2].Value, CultureInfo.InvariantCulture);
                Config.MotorFgNoPuede("propia", 3); Config.MotorFgNoPuede("propia", 4);
                Log.Write("Motor: su generacion propia (x" + k + ") ha fallado: en esta sesion x3 / x4 van por otro camino");
                PrepareCfg();
                if (!cfg.FgOff)
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        try { System.Threading.Thread.Sleep(300); RestartLs(cfg.FgName + " x" + k, Tx.T("la generación propia del motor ha fallado", "the engine's own frame generation failed")); }
                        catch (Exception ex) { Log.Error("generacion propia: otro camino", ex); }
                    });
                return true;
            }
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
