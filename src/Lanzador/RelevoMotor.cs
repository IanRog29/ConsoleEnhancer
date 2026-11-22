// RelevoMotor.cs - 22-11-2026 noche (1.2.4, ronda de arreglos, agente RELEVO). La prueba del usuario con la 1.2.4 (21:28-21:39):
// "le doy a activar xefg y se pone dlss fg", "lagazos" y "pantallazos negros". Lo que pasaba (su consoleenhancer.log y engine.log):
//  1. El motor se abre con una "identidad" que no se cambia en marcha (la cadena de generacion: la de siempre con Streamline / XeSS FG /
//     FSR 3 FG / PrismaFg; Smooth Motion del driver; el refresco fijo). Al elegir XeSS FG x2 con el motor de la FG propia (cadena de
//     siempre) delante, SyncEngine le mandaba en marcha fg=1 (sin fg_tipo): ese motor encendia DLSS-G ("Generacion si: eOk", 21:30:33) y
//     se veia DLSS FG ~2,4 s hasta que el motor nuevo con XeSS FG se ponia delante. Igual al reves (XeSS FG seguia con DLSS FG pedido).
//     Ahora: mientras el motor de delante tenga otra identidad de la que toca, la generacion en marcha va apagada (fg=0 fg_propia=0):
//     nunca la generacion equivocada; y el relevo empieza al momento (antes 0,5 s seguidos distintos y la vuelta de WatchLoop, ~0,85 s).
//  2. Cada cambio de identidad arrancaba un motor nuevo escondido: ~1 s sin Streamline (XeSS FG) y ~5 s con Streamline (slInit 2,1 s +
//     slSetD3DDevice 2,4 s), y el motor que se iba se cerraba. Ahora el que se va pasa a la espera (suelta la capturadora, la generacion,
//     DLSS 5 y la memoria de la sesion: ~560 MB) y es el motor en espera: volver a lo de antes (XeSS FG x2 <-> x4, DLSS FG <-> XeSS FG...)
//     es despertarlo (~0,5 s). El motor en espera solo se cierra si no vale para lo que se pide (antes, siempre: "Smooth Motion cambia").
//     El de reserva (tras 10 s jugando sin motor en espera) se abre con la identidad de siempre (cadena 0, sin Smooth Motion) si la de
//     ahora es otra; con la de siempre delante, como antes (Smooth Motion, la otra del modo).
//  3. Tras una caida del motor (DEVICE_HUNG, codigo 3): 3,1 s en negro + 3,3 s del proceso saliendo + 1,5 s de espera. Ahora: el motor
//     sale al momento (Program.cs), el telon es la imagen congelada (la copia de la pantalla al verla parada, como el telon congelado
//     de la 1.2.3) y, con un motor en espera que vale, sin la espera de 1,5 s. (Que generacion va por Magpie tras una caida: XEFGM.)
// C# 5. Partes de TrayApp (Switch2Magpie.cs, SinTelon.cs).
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace ConsoleEnhancer
{
    partial class TrayApp
    {
        // ---- 1. La identidad del motor (lo que solo se elige al abrirlo) ----

        // Lo que se quiere ahora: cadena de generacion, Smooth Motion y refresco fijo (como CheckEngineFfx / Smooth / Fixed)
        int WantChain { get { return cfg.EngineFgChain; } }
        int WantFixed { get { return cfg.EngineSmoothMode == 0 && cfg.EngineFixedWanted ? 1 : 0; } }

        // El motor que hace la imagen (p) se abrio con otra identidad de la que toca: por que (null = vale). Con cfg ya preparada
        string EngineIdentityDiff(Process p)
        {
            if (p == null) return null;
            int have = Config.EngineFfxOf(p), want = WantChain;
            if (have >= 0 && !Config.ChainOk(have, want)) return "cadena de generacion " + have + " -> " + want;
            int smRun = Config.EngineSmoothRunning;
            if (smRun >= 0 && (cfg.EngineSmoothAny || smRun > 0) && cfg.EngineSmoothMode != smRun) return "Smooth Motion " + smRun + " -> " + cfg.EngineSmoothMode;
            int fx = Config.EngineFixedOf(p);
            if (fx >= 0 && (cfg.EngineFixedWanted || fx != 0) && fx != WantFixed) return "refresco " + (fx > 0 ? "fijo" : "VRR") + " -> " + (WantFixed > 0 ? "fijo" : "VRR");
            return null;
        }

        // El motor en espera no vale para lo que se pide: por que (null = vale o no hay). Con cfg ya preparada
        string StandbyIdentityDiff() { return IdentityDiffOf(standbyEngine, standbySmooth); }

        // Un motor abierto (en espera) con su Smooth Motion 'sm': por que no vale para lo de ahora (null = vale o no hay)
        string IdentityDiffOf(Process e, int sm)
        {
            if (e == null) return null;
            int have = Config.EngineFfxOf(e), want = WantChain;
            if (have >= 0 && !Config.ChainOk(have, want)) return "cadena de generacion " + have + ", ahora " + want;
            if ((cfg.EngineSmoothAny || sm > 0) && sm != cfg.EngineSmoothMode)
                return "Smooth Motion " + (sm > 0 ? sm.ToString(CultureInfo.InvariantCulture) : "no") + ", ahora " + cfg.EngineSmoothMode;
            int fx = Config.EngineFixedOf(e);
            if (fx >= 0 && fx != WantFixed) return "refresco " + (fx > 0 ? "fijo" : "VRR") + ", ahora " + (WantFixed > 0 ? "fijo" : "VRR");
            return null;
        }

        // Lo que se manda en marcha al motor de delante (SyncEngine / EngineSetCmd): lo de siempre, pero sin generacion si ese motor se abrio
        // con otra identidad (la buena llega con el relevo)
        string idDiffLogged;
        string EngineLiveForRunning()
        {
            string live = EngineLive(cfg);
            string d = EngineIdentityDiff(mpv);
            if (d == null) { idDiffLogged = null; return live; }
            string sin = LiveSinGeneracion(live);
            if (sin != live && d != idDiffLogged)
            {
                idDiffLogged = d;
                Log.Write("Motor de ahora con otra identidad (" + d + "): en marcha sin generacion hasta que se ponga delante el que la lleva (nunca la equivocada)");
            }
            return sin;
        }

        static string LiveSinGeneracion(string live)
        {
            string s = Regex.Replace(live, "(^| )fg=1( |$)", "$1fg=0$2");
            s = Regex.Replace(s, " fg_propia=[0-9]+", " fg_propia=0");
            s = Regex.Replace(s, " fg_mult=[0-9]+", "");
            return s;
        }

        // RestartLs (menu de ajustes, L3, perfiles) con el motor delante: si lo nuevo pide otra identidad, el relevo ya (sin esperar a la
        // vuelta de WatchLoop ni al medio segundo de CheckEngineFfx / Smooth / Fixed). true = empezado
        bool RelevoSiOtraIdentidad(string title, string subtitle)
        {
            if (!Config.EngineActive || !ipcUp || !lsReady || homeBusy || passesBusy || relaunchEngine || relaunchSize != null || relevo != null) return false;
            string d = EngineIdentityDiff(mpv);
            if (d == null) return false;
            ffxDiffSince = smoothDiffSince = fixedDiffSince = DateTime.MinValue;
            Log.Write("Motor: lo elegido pide otra identidad (" + d + "): relevo al momento");
            ReabrirMotorSmooth(cfg.EngineSmoothMode, title, subtitle);
            return true;
        }

        // ---- 2. El motor que se va pasa a la espera (relevo de motor a motor) ----

        volatile bool relevoQuitStandby;    // QuitOldMpv: el que se va es un motor: switch2-standby en vez de quit (MpvLinkLoop)
        volatile Process relevoSentStandby; // el ultimo motor que se va al que se le pidio la espera (QuitOldMpv no lo cierra a los 3 s)
        volatile int relevoAdoptSmooth = -1; // TakeRelevoEngine: el Smooth Motion del motor del relevo (SetStandby del que se va pisa standbySmooth)

        // MpvLinkLoop, con relevoQuit == p: true = se le ha pedido la espera (si no, quit como siempre)
        bool RelevoOldToStandby(Process p, System.IO.StreamWriter w)
        {
            if (!relevoQuitStandby) return false;
            relevoQuitStandby = false;
            if (!cfg.RelevoKeepOld || userExit || !StandbyOn()) return false;
            standbyGot = null;
            standbyGotMs = -1;
            standbyAskedSw = Stopwatch.StartNew();
            standbyAsked = p;
            switchUntil = DateTime.Now.AddSeconds(3);
            relevoSentStandby = p;
            w.WriteLine("{\"command\":[\"script-message\",\"switch2-standby\"]}");
            Log.Write("Relevo: el motor que se va (pid " + p.Id + ", cadena " + Config.EngineFfxOf(p) + ", Smooth Motion " + Config.EngineSmoothRunning +
                      ") pasa a la espera en vez de cerrarse: volver a lo de antes sera despertarlo");
            return true;
        }

        // ---- 2b. El corte y el telon, con la primera imagen PINTADA del motor (switch2-pintada) ----
        volatile int enginePaintedPid = -1;

        // ¿El ConsoleEnhancerEngine.exe dice switch2-pintada? (uno de antes no: entonces como antes, con switch2-signal y su ritmo)
        static readonly object paintProbeSync = new object();
        static string paintProbeExe;
        static DateTime paintProbeAt;
        static bool paintProbeOk;
        bool EngineKnowsPainted()
        {
            try
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(cfg.EnginePath);
                if (!fi.Exists) return false;
                lock (paintProbeSync)
                {
                    if (paintProbeExe == fi.FullName && paintProbeAt == fi.LastWriteTimeUtc) return paintProbeOk;
                    paintProbeOk = HasText(System.IO.File.ReadAllBytes(fi.FullName), "switch2-pintada");
                    paintProbeExe = fi.FullName;
                    paintProbeAt = fi.LastWriteTimeUtc;
                    return paintProbeOk;
                }
            }
            catch { return false; }
        }

        // WaitForSignal, con el aviso de imagen ya: con el motor, hasta su primera imagen pintada (como mucho 2 s)
        void PaintWait(Process p)
        {
            if (!Config.EngineActive || p == null || !EngineKnowsPainted()) return;
            Stopwatch sw = Stopwatch.StartNew();
            while (enginePaintedPid != p.Id && sw.ElapsedMilliseconds < 2000 && !userExit)
            {
                try { if (p.HasExited) return; } catch { return; }
                Thread.Sleep(5);
            }
            if (sw.ElapsedMilliseconds >= 15) Log.Write("Motor: su primera imagen pintada " + (enginePaintedPid == p.Id ? sw.ElapsedMilliseconds + " ms despues del aviso de imagen" : "sin llegar en 2 s: se sigue"));
        }

        // ---- 3. Caidas del motor: el telon con la imagen congelada y sin esperas de mas ----

        Bitmap crashFrozen;                 // la copia de la pantalla al ver el motor parado (switch2-gpu colgada / perdida)
        DateTime crashFrozenAt = DateTime.MinValue;
        int crashFrozenPid = -1;
        int crashFrozenBusy;

        // CierresEvent (switch2-gpu colgada / perdida del motor de ahora): la pantalla tal cual (la ultima imagen, quieta) para el telon si se cae
        void CrashFrozenGrab(Process owner)
        {
            if (owner == null || !cfg.CurtainFrozen) return;
            if (crashFrozenPid == owner.Id && (DateTime.Now - crashFrozenAt).TotalSeconds < 5) return;
            if (Interlocked.CompareExchange(ref crashFrozenBusy, 1, 0) != 0) return;
            int pid = owner.Id;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    Bitmap b = CaptureScreen();
                    Bitmap old;
                    lock (crashSync) { old = crashFrozen; crashFrozen = b; crashFrozenAt = DateTime.Now; crashFrozenPid = pid; }
                    if (old != null) old.Dispose();
                    Log.Write("Motor parado: copia de la pantalla para el telon si se cae (" + (b != null ? "con imagen" : "sale negra: sin ella") + ", " + sw.ElapsedMilliseconds + " ms)");
                }
                catch (Exception ex) { Log.Error("Copia de la pantalla (motor parado)", ex); }
                finally { Interlocked.Exchange(ref crashFrozenBusy, 0); }
            });
        }
        readonly object crashSync = new object();

        // RunMpv, el motor se ha cerrado solo: el telon (la imagen congelada si la hay, reciente y de ese motor; si no, negro)
        void CrashCurtain(Process p)
        {
            Bitmap b = null;
            lock (crashSync)
            {
                if (crashFrozen != null && crashFrozenPid == p.Id && (DateTime.Now - crashFrozenAt).TotalSeconds < 20) { b = crashFrozen; crashFrozen = null; }
            }
            if (b != null && ShowFrozenCurtain(b, true)) { Log.Write("Telon de la caida con la imagen congelada (la ultima que se vio)"); return; }
            ShowCurtain();
        }

        // RunMpv, el motor se ha cerrado solo: cuanto esperar antes de abrir otro. Con un motor en espera vivo y listo (su dispositivo sigue:
        // la grafica esta bien), nada: se despierta ya; si no vale para lo de ahora, de puente sin generacion (crashBridgeNext) y luego el
        // relevo al bueno (CheckEngineFfx / Smooth / Fixed). Sin el, el 1,5 s de siempre (tras un reinicio de la grafica, que vuelva)
        volatile bool crashBridgeNext;
        int CrashWaitMs()
        {
            crashBridgeNext = false;
            try
            {
                PrepareCfg();
                Process e = standbyEngine;
                if (e != null && !e.HasExited && standbyReady && cfg.EngineNow)
                {
                    crashBridgeNext = StandbyIdentityDiff() != null;
                    return 0;
                }
            }
            catch { }
            return 1500;
        }
    }

    partial class Config
    {
        // ini relevo_motor_espera=1|0 (de serie 1): el motor que se va en un relevo de motor a motor pasa a la espera (0 = se cierra, como la 1.2.4)
        public bool RelevoKeepOld = true;

        // Un motor abierto con la cadena 'have' vale para 'want': la misma, o XeSS FG con DLSS SR (Streamline solo para DLSS SR: 21, 31, 41)
        // para XeSS FG sin el (20, 30, 40): el escalador se cambia en marcha (FSR 1 / sin reescalado); al reves no (hace falta Streamline).
        // Antes, pasar de DLSS a FSR 1 con XeSS FG abria otro motor (~1 s) y volver a DLSS otro (~5 s)
        public static bool ChainOk(int have, int want)
        {
            return have == want || (ChainXess(have) && have % 10 == 1 && have - 1 == want);
        }

        public bool ParseRelevo(string key, string val)
        {
            switch (key)
            {
                case "relevo_motor_espera": RelevoKeepOld = ParseBool(val); return true;
            }
            return false;
        }
    }
}
