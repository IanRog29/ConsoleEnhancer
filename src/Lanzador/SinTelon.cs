// SinTelon.cs - 16-11-2026 (dlss5_sin_telon): DLSS 5 si/no con el boton A del mando SIN telon. La imagen de ahora sigue a la vista
// hasta que la otra da su primera imagen de verdad, y entonces se pasa a ella de golpe (sin negro ni imagen congelada):
//  - DLSS 5 APAGADO (mpv + Magpie -> motor propio): Magpie sigue escalando con DLSS 5 y mpv con el primer plano (Magpie solo escala la
//    ventana de delante). El motor en espera despierta DETRAS ("switch2-wake detras": ventana al fondo, sin activarse ni coger el foco)
//    y abre la capturadora a la vez que mpv (la GC573 la comparten dos procesos con el mismo formato). Con su aviso de imagen y su
//    ritmo ("switch2-signal" + "switch2-rate" > 0) el corte: el motor delante y con el foco (la generacion de NVIDIA se pone), Magpie
//    matado ya y mpv se cierra por su tuberia, debajo. RunMpv toma ese motor tal cual (TakeRelevoEngine) en vez de despertarlo.
//    Sin imagen del motor en dlss5_sin_telon_espera_ms (4 s), el telon de siempre (negro con el aviso) y lo de antes.
//  - DLSS 5 ENCENDIDO (motor propio -> mpv + Magpie): el motor sigue jugando ENCIMA de todo ("switch2-encima": siempre encima sin
//    activarse; 2 = reafirmado) mientras RunMpv abre mpv (capturadora compartida) y Magpie prepara DLSS 5 (8-15 s, sobre todo NVIDIA
//    creando sus modelos). Con mpv delante (lo necesita Magpie) el motor no tiene el foco: sin la generacion de NVIDIA mientras tanto
//    (60 imagenes en el modo 60, 30 en el de 30), salvo dlss5_sin_telon_foco=1 (el foco vuelve al motor mientras Magpie prepara).
//    Cuando Magpie presenta (FinishLsStart, la deteccion de siempre), el motor se va al fondo ("switch2-encima 0") y a la espera.
// El aviso del mando sale en la imagen que se ve (mpv al apagar, el motor al encender) y luego en la nueva, como siempre.
// Solo el boton A (ToggleDlss5): el boton de captura (generacion) sigue con el telon y Magpie fuera en ~0,1 s (se caia si seguia vivo).
// Lo compila build.bat con el resto (partes de TrayApp de Switch2Magpie.cs). C# 5
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ConsoleEnhancer
{
    partial class TrayApp
    {
        // ---- El relevo: el motor mientras se pasa de una imagen a la otra (su tuberia al estilo de mpv, aparte del enlace de siempre) ----
        sealed class Relevo
        {
            public readonly bool ToEngine;          // true: de mpv + Magpie al motor (DLSS 5 apagado); false: del motor a mpv + Magpie
            public readonly Stopwatch Since = Stopwatch.StartNew();    // desde el boton
            public Process Engine;
            public string Ipc;
            public Process OldMpv;                  // (al motor) el mpv que se va
            public volatile bool WakeSent, Signal, StandbyOk, Cut, Curtain;
            public bool Bridge;                     // (16-11, velocidad) el puente: el motor en espera juega encima mientras Magpie se (re)abre
            public bool OldEngine;                  // (1.2.4 arreglos, RELEVO) al motor: el que se va tambien es un motor (otra identidad): pasa a la espera
            public string Why = "";                 // (y para que: el log)
            public volatile int Rate = -1;
            public long WakeMs = -1, SignalMs = -1, RateMs = -1, CutMs = -1;
            public volatile bool Painted;           // (1.2.4 arreglos, RELEVO) switch2-pintada: el motor ya ha presentado una imagen de verdad
            public long PaintedMs = -1;
            // (1.2.4 ronda 5, INTEGRA5) el corte con relevo_pintadas imagenes presentadas (de serie 4), no con la primera: al despertar con
            // DLSS 5, tras la 1a imagen el motor se paraba ~1,2 s (sus primeras evaluaciones; medido en la app entera, Magpie -> motor con
            // DLSS FG + DLSS 5 a 4K) y ese paron se veia; asi lo de antes sigue a la vista mientras tanto. El puente (Magpie se reabre: la imagen
            // de antes se va), con la primera como antes: esperando 4 se veian huecos de 1,4-2,7 s
            public long SteadyMs = -1;
            // 18-11 (PASADAS): el Smooth Motion con el que se abrio ESTE motor (standbySmooth al volver a la espera). Antes SetStandby(ready) tomaba
            // el del ultimo motor que hizo la imagen: el motor del puente (abierto ya en espera, con Smooth Motion) volvia a la espera como "sin
            // Smooth Motion" y al apagar DLSS 5 se le mandaba fg=1 (DLSS FG + Smooth Motion a la vez; luego en espera +600 MB de la generacion)
            public int Smooth = -1;
            NamedPipeClientStream pipe;
            StreamWriter w;
            readonly object wSync = new object();

            public Relevo(bool toEngine) { ToEngine = toEngine; }

            public bool Connect(int ms)
            {
                NamedPipeClientStream p = null;
                try
                {
                    p = new NamedPipeClientStream(".", Ipc, PipeDirection.InOut, PipeOptions.Asynchronous);
                    p.Connect(ms);
                    StreamWriter sw = new StreamWriter(p, new UTF8Encoding(false));
                    sw.AutoFlush = true;
                    sw.NewLine = "\n";
                    StreamReader r = new StreamReader(p, new UTF8Encoding(false));
                    lock (wSync) { pipe = p; w = sw; }
                    Thread t = new Thread(delegate () { Read(r); });
                    t.IsBackground = true;
                    t.Name = "Sin telon (tuberia del motor)";
                    t.Start();
                    return true;
                }
                catch (Exception)
                {
                    if (p != null) try { p.Dispose(); } catch { }
                    return false;
                }
            }

            public bool Send(string json)
            {
                lock (wSync)
                {
                    if (w == null) return false;
                    try { w.WriteLine(json); return true; }
                    catch (Exception) { return false; }
                }
            }

            // Lo que avisa el motor (a todos sus clientes): imagen, ritmo y la espera. Su aviso de imagen no se contesta aqui: lo contesta el
            // enlace de siempre cuando RunMpv toma el motor (el motor lo repite cada 0,5 s hasta entonces)
            void Read(StreamReader r)
            {
                try
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        if (!line.Contains("\"client-message\"")) continue;
                        if (line.Contains("\"args\":[\"switch2-signal\""))
                        {
                            if (!Signal) { SignalMs = Since.ElapsedMilliseconds; Signal = true; }
                        }
                        else if (line.Contains("\"args\":[\"switch2-rate\""))
                        {
                            Match m = Regex.Match(line, "\"switch2-rate\",\"([0-9]+)\"");
                            int v;
                            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                            {
                                if (v > 0 && RateMs < 0) RateMs = Since.ElapsedMilliseconds;
                                Rate = v;
                            }
                        }
                        else if (line.Contains("\"args\":[\"switch2-standby-ok\"")) StandbyOk = true;
                        else if (line.Contains("\"args\":[\"switch2-pintada\""))
                        {
                            Match mp = Regex.Match(line, "\"switch2-pintada\",\"([0-9]+)\"");
                            int pn;
                            if (!mp.Success || !int.TryParse(mp.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out pn)) pn = int.MaxValue;
                            if (PaintedMs < 0) PaintedMs = Since.ElapsedMilliseconds;
                            if (!Painted && pn >= (Bridge ? 1 : Math.Max(1, Config.RelevoPintadas))) { SteadyMs = Since.ElapsedMilliseconds; Painted = true; }   // (el puente, con la primera: ahi la imagen de antes ya no esta)
                        }
                    }
                }
                catch (Exception) { } // (tuberia cerrada: el motor se ha cerrado, o Close)
            }

            public void Close()
            {
                NamedPipeClientStream p;
                lock (wSync) { p = pipe; pipe = null; w = null; }
                if (p != null) try { p.Dispose(); } catch { }
            }
        }

        readonly object relevoSync = new object();
        volatile Relevo relevo;             // el relevo en curso (null = ninguno)
        volatile Process relevoAside;       // (a Magpie) el motor que RunMpv deja aparte, jugando, en vez de esperar a que salga
        volatile Process relevoQuit;        // (al motor) el mpv que se cierra ya con el motor delante (solo el enlace de ese mpv: MpvLinkLoop)
        Process relevoAdopt;                // (al motor) el motor ya despierto que RunMpv toma tal cual (con relevoSync)
        string relevoAdoptIpc;
        bool relevoAdoptNoImage;            // (y si aun no daba imagen: por el telon de siempre)

        // ---- Desde RestartLs (ToggleDlss5: DLSS 5 si/no, al cambiar de motor). false = no se puede: lo de siempre (telon) ----
        bool TrySinTelon(string title, string subtitle)
        {
            if (!cfg.Dlss5Seamless) return false;
            string no = null;
            Process p = mpv;
            bool toEngine = !Config.EngineActive && cfg.EngineNow;
            bool toMagpie = Config.EngineActive && !cfg.EngineNow;
            try
            {
                if (p == null || p.HasExited) no = "sin imagen en marcha";
                else if (toMagpie) no = "a mpv sin el motor";   // (1.2.4 ronda 6, QUITAFG: sin Magpie no hay relevo del motor a el)
                else if (!toEngine && !toMagpie) no = "no cambia de motor";
                else if (relevo != null) no = "ya hay un relevo en marcha";
                else if (InHome || homeBusy) no = "HOME";
                else if (!StandbyOn()) no = "sin motor en espera (motor_en_espera=0 o un ConsoleEnhancerEngine.exe que no lo sabe hacer)";
                else if (!EngineKnowsSinTelon()) no = "el ConsoleEnhancerEngine.exe no sabe ponerse detras ni encima (switch2-delante, switch2-encima)";

                else if (toEngine && !signalOn) no = "mpv sin imagen";
                else if (toMagpie && (!ipcUp || !videoSignal)) no = "el motor sin imagen";
                else if (toMagpie && !(cfg.UpscaleOn || !cfg.Dlss5Off || !cfg.FgOff)) no = "nada que hacer para Magpie";
            }
            catch (Exception ex) { no = ex.GetType().Name + ": " + ex.Message; }
            if (no != null) { Log.Write(title + ": sin telon no se puede (" + no + "): con el telon de siempre"); return false; }
            return StartRelevoToEngine(title, subtitle, p);
        }

        // ¿El ConsoleEnhancerEngine.exe sabe ponerse detras / delante / encima? (uno de antes no: se pondria delante al despertar). Una vez por version
        static readonly object sinTelonProbeSync = new object();
        static string sinTelonProbeExe;
        static DateTime sinTelonProbeAt;
        static bool sinTelonProbeOk;
        bool EngineKnowsSinTelon()
        {
            try
            {
                FileInfo fi = new FileInfo(cfg.EnginePath);
                if (!fi.Exists) return false;
                lock (sinTelonProbeSync)
                {
                    if (sinTelonProbeExe == fi.FullName && sinTelonProbeAt == fi.LastWriteTimeUtc) return sinTelonProbeOk;
                    byte[] exe = File.ReadAllBytes(fi.FullName);
                    sinTelonProbeOk = HasText(exe, "switch2-delante") && HasText(exe, "switch2-encima");
                    sinTelonProbeExe = fi.FullName;
                    sinTelonProbeAt = fi.LastWriteTimeUtc;
                    return sinTelonProbeOk;
                }
            }
            catch (Exception ex) { Log.Error("Sin telon: mirando el motor", ex); return false; }
        }

        static string PillText(string title, string subtitle)
        {
            return title == null ? null : string.IsNullOrEmpty(subtitle) ? title : title + " · " + subtitle;
        }

        // ================================================================================== DLSS 5 apagado: de mpv + Magpie al motor
        bool StartRelevoToEngine(string title, string subtitle, Process old)
        {
            bool pad = PadRecent;
            if (pad) { padRestart = true; padRestartAt = DateTime.Now; }
            string pill = PillText(title, subtitle);
            Relevo r = new Relevo(true);
            r.OldMpv = old;
            r.OldEngine = Config.EngineActive;   // (1.2.4 arreglos, RELEVO)
            lock (relevoSync) relevo = r;
            lsReady = false;          // (nada del mando ni del HOME sobre la imagen que se va)
            relaunchEngine = true;    // (RunMpv: al cerrarse mpv, el motor; mientras, ningun otro cambio de motor)
            // El filtro de bordes / tramado de DLSS 5 apagado (SetDlss5State) no va a mpv, que se va: se veria el cambio y un tiron
            lock (cmdQueue) cmdQueue.RemoveAll(delegate (string c) { return c.StartsWith("{\"command\":[\"set\",\"glsl-shaders\"", StringComparison.Ordinal); });
            if (pad && pill != null) Osd(pill); // (en la imagen de ahora: Magpie la escala)
            // (16-11, velocidad: tambien desde el HOME a 4K, sin Magpie: la imagen de mpv tal cual)
            bool withMagpie = false;   // (1.2.4 ronda 6, QUITAFG: sin Magpie)
            Log.Write(title + ": sin telon (" + (r.OldEngine ? "del motor propio a otro motor propio" : "de mpv" + (withMagpie ? " + Magpie" : " (HOME a 4K)") + " al motor propio") + "): " +
                      (withMagpie ? "Magpie sigue a la vista con DLSS 5" : r.OldEngine ? "el motor de ahora sigue a la vista" : "la imagen de la consola sigue a la vista") + "; el motor despierta detras y se pone delante en cuanto da imagen");
            Thread t = new Thread(delegate () { RelevoToEngine(r, title, subtitle, pill, pad); });
            t.IsBackground = true;
            t.Name = "Sin telon (al motor)";
            t.Start();
            return true;
        }

        void RelevoToEngine(Relevo r, string title, string subtitle, string pill, bool pad)
        {
            try
            {
                // (1.2.4, integracion: XeSS FG x2 ya va por el motor; p. ej. XeSS FG x3 por Magpie bajado a x2 -proteccion, menu, Ctrl+X-:
                // el motor en espera se abrio con la cadena de antes -DLSS-G- y no se cambia en marcha: fuera, y uno nuevo con la buena
                // mientras Magpie sigue a la vista. Lo mismo que mira RunMpv al despertar uno sin relevo)
                PrepareCfg();
                // (1.2.4 arreglos, RELEVO: tambien Smooth Motion y el refresco, no solo la cadena: RelevoMotor.cs)
                string sbNo = StandbyIdentityDiff();
                if (sbNo != null) KillStandby("relevo: el motor en espera no vale (" + sbNo + ")");
                else if (standbyEngine != null) Log.Write("Relevo: el motor en espera (pid " + standbyEngine.Id + ") vale para lo de ahora: se despierta");
                Process e = StandbyForWake(); // (recien arrancado: hasta que diga que esta listo, como mucho 8 s desde su arranque)
                if (e == null && !userExit)
                {
                    e = StartStandbyNow();
                    if (e != null) e = StandbyForWake();
                }
                if (userExit) return;
                if (e == null) { RelevoToEngineCurtain(r, "no hay motor en espera ni se pudo arrancar uno", title, subtitle, pad); return; }
                string ipc = standbyIpc;
                int sm = standbySmooth;   // (18-11, PASADAS: el suyo, no el del ultimo motor que hizo la imagen)
                // Fuera de la espera, como RunMpv al despertarlo (su tuberia de espera se cierra: va la del relevo); en el job (se cierra si la
                // app muere) hasta que RunMpv lo tome
                standbyEngine = null;
                standbyReady = false;
                CloseStandbyPipe();
                standbyAtExit = e;
                r.Engine = e;
                r.Ipc = ipc;
                r.Smooth = sm;
                if (!r.Connect(1500))
                {
                    SetStandby(e, ipc, true); // (sigue en espera: RunMpv lo despierta como siempre)
                    standbySmooth = sm;
                    RelevoToEngineCurtain(r, "sin tuberia con el motor en espera", title, subtitle, pad);
                    return;
                }
                PrepareCfg();
                string set = EngineSessionSet(cfg, standbySmooth > 0);
                r.Send(JsonCmd("engine", "set " + set));
                // La espera de "No Signal" arrancando un juego / tras el menu de apagado, como hace MpvLinkLoop al conectarse
                DateTime nowL = DateTime.Now;
                double lr = (launchUntil - nowL).TotalSeconds;
                if (lr > 0.5) r.Send(JsonCmd("script-message", "switch2-launch", Ms(Math.Ceiling(lr))));
                double pr = powerMenuAt == DateTime.MinValue ? 0 : PowerMenuSecs - (nowL - powerMenuAt).TotalSeconds;
                if (pr > 0.5) r.Send(JsonCmd("script-message", "switch2-powermenu", Ms(Math.Ceiling(pr))));
                if (pad && pill != null) r.Send(OsdCmd(pill, OsdMs, false)); // (el motor lo enseña al despertar: sigue en el corte)
                r.WakeMs = r.Since.ElapsedMilliseconds;
                r.WakeSent = r.Send(JsonCmd("script-message", "switch2-wake", "detras"));
                Log.Write("Sin telon: motor (pid " + e.Id + ") despertado detras de Magpie a los " + r.WakeMs + " ms del boton: " + set);
                Stopwatch sw = Stopwatch.StartNew();
                bool dead = false;
                // (1.2.4 arreglos, RELEVO: y con su primera imagen PINTADA -switch2-pintada-, no solo con imagenes llegando: si no, el corte
                // ponia delante un motor que aun no habia presentado nada -DLSS 5 creandose- y se veia negro ~0,5 s. Un motor de antes no lo dice)
                bool needPaint = EngineKnowsPainted();
                while (!userExit && !(r.Signal && r.Rate > 0 && (r.Painted || !needPaint)) && sw.ElapsedMilliseconds < cfg.Dlss5SeamlessWaitMs + (r.Signal && needPaint ? 2000 : 0))
                {
                    try { dead = e.HasExited; } catch { dead = true; }
                    if (dead) break;
                    Thread.Sleep(4);
                }
                if (userExit) return;
                if (dead) { RelevoToEngineCurtain(r, "el motor se ha cerrado al despertarlo", title, subtitle, pad); return; }
                if (!(r.Signal && r.Rate > 0))
                {
                    RelevoToEngineCurtain(r, "el motor no da imagen en " + sw.ElapsedMilliseconds + " ms (aviso de imagen " + (r.Signal ? "si" : "no") + ", ritmo " + r.Rate + ")",
                                          title, subtitle, pad);
                    return;
                }
                // El corte: el motor delante y con el foco (pantalla completa en primer plano: la barra de tareas no asoma; la generacion de
                // NVIDIA se pone), Magpie fuera ya (su ventana encima desaparece: se ve el motor) y mpv se cierra debajo por su tuberia
                IntPtr h = WindowOf(e);
                r.CutMs = r.Since.ElapsedMilliseconds;
                bool fg = false;
                if (h != IntPtr.Zero)
                {
                    // (por encima de mpv aunque no llegara a coger el foco: al quitar Magpie se ve el motor, no la ventana pequeña de mpv)
                    SetWindowPosST(h, IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | 0x10); // HWND_TOP, NOSIZE | NOMOVE | NOACTIVATE
                    ForceForeground(h);
                    fg = GetForegroundWindow() == h;
                }

                r.Send(JsonCmd("script-message", "switch2-delante"));
                lock (relevoSync)
                {
                    relevoAdopt = e;
                    relevoAdoptIpc = ipc;
                    relevoAdoptNoImage = false;
                    r.Cut = true;
                }
                QuitOldMpv(r);
                Log.Write("Sin telon (al motor): imagen del motor a los " + r.SignalMs + " ms del boton (ritmo " + r.Rate + "/s a los " + r.RateMs + " ms; despertado a los " +
                          r.WakeMs + (needPaint ? "; pintada a los " + r.PaintedMs + (r.SteadyMs >= 0 ? ", " + Config.RelevoPintadas + " a los " + r.SteadyMs : "") : "") + "); corte a los " + r.CutMs + " ms: motor delante" + (fg ? " y con el foco" : " (el foco aun no: se lo da el motor)") +
                          ", Magpie fuera y mpv se cierra debajo" + (h == IntPtr.Zero ? " (SIN su ventana a mano: el foco, luego)" : ""));
            }
            catch (Exception ex)
            {
                Log.Error("Sin telon (al motor)", ex);
                if (!r.Cut) RelevoToEngineCurtain(r, "fallo: " + ex.Message, title, subtitle, pad);
            }
        }

        // Sin imagen del motor a tiempo (o sin motor): el telon de siempre (negro con el aviso), Magpie fuera y mpv se cierra. El motor, si
        // llego a despertarse, lo toma RunMpv (sin imagen aun: se le pide reabrir la capturadora cuando mpv ya se ha ido)
        void RelevoToEngineCurtain(Relevo r, string why, string title, string subtitle, bool pad)
        {
            Log.Write("Sin telon: " + why + " (" + r.Since.ElapsedMilliseconds + " ms del boton): telon como antes");
            ShowFastCurtain(pad ? subtitle : null, pad ? title : null, false);

            Process e = r.Engine;
            bool alive = false;
            try { alive = e != null && !e.HasExited && r.WakeSent; } catch { }
            lock (relevoSync)
            {
                if (alive)
                {
                    relevoAdopt = e;
                    relevoAdoptIpc = r.Ipc;
                    relevoAdoptNoImage = !(r.Signal && r.Rate > 0);
                }
                r.Curtain = true;
                r.Cut = true;
            }
            if (alive) r.Send(JsonCmd("script-message", "switch2-delante")); // (delante y con el foco, tapado por el telon hasta su imagen)
            else if (e != null && standbyAtExit == e && standbyEngine != e) standbyAtExit = null;
            QuitOldMpv(r);
        }

        // (1.2.4 ronda 6, QUITAFG, fase 2: RelevoCaptureButton era de Magpie; fuera)

        // El mpv que se va: por su tuberia (MpvLinkLoop, solo ese mpv); si no se ha ido en 3 s, a la fuerza
        void QuitOldMpv(Relevo r)
        {
            Process old = r.OldMpv;
            if (old == null) return;
            try { if (old.HasExited) return; } catch { return; }
            relevoQuitStandby = r.OldEngine;   // (1.2.4 arreglos, RELEVO: un motor que se va pasa a la espera, RelevoMotor.cs)
            relevoQuit = old;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (old.WaitForExit(3000)) return;
                    // (1.2.4 arreglos, RELEVO: el que paso a la espera no se cierra -aunque ya lo haya despertado otro relevo: en la prueba del
                    // 22-11 23:04:41 este guardian mato al motor en espera recien despertado y salio el telon 5,2 s-; si no contesta a la espera,
                    // ya lo cierra RunMpv a los 1,5 s)
                    if (RelevoFueAEspera(old) || old == mpv || old == standbyEngine || old == standbyGot || old == standbyAsked) return;
                    if (relevoQuit == old) relevoQuit = null;
                    Log.Write("Sin telon: el mpv de antes (pid " + old.Id + ") no se ha cerrado en 3 s: se cierra a la fuerza");
                    old.Kill();
                }
                catch { }
            });
        }

        // RunMpv, al lanzar el motor propio: el motor que el relevo ya ha despertado y puesto delante (o, con el telon, despertado y aun sin
        // imagen). Si el relevo aun no ha decidido (mpv se fue solo antes del corte) se le espera. null = ninguno: lo de siempre
        Process TakeRelevoEngine(out string ipc, out bool noImage)
        {
            ipc = null;
            noImage = false;
            Relevo r = relevo;
            if (r == null || !r.ToEngine) return null;
            if (!r.Cut)
            {
                Stopwatch sw = Stopwatch.StartNew();
                while (!r.Cut && !userExit && sw.ElapsedMilliseconds < 40000) Thread.Sleep(20);
                Log.Write("Sin telon: mpv se fue antes del corte: " + sw.ElapsedMilliseconds + " ms esperando al relevo" + (r.Cut ? "" : " (sin acabar: se sigue sin el)"));
            }
            Process e;
            lock (relevoSync)
            {
                e = relevoAdopt;
                ipc = relevoAdoptIpc;
                noImage = relevoAdoptNoImage;
                relevoAdoptSmooth = e != null ? r.Smooth : -1;   // (1.2.4 arreglos, RELEVO: su Smooth Motion para RunMpv)
                relevoAdopt = null;
                relevoAdoptIpc = null;
                if (relevo == r) relevo = null;
            }
            r.Close(); // (la tuberia del relevo: el enlace de siempre se conecta ahora)
            if (relevoQuit != null) { try { if (relevoQuit.HasExited) relevoQuit = null; } catch { relevoQuit = null; } }
            if (e == null) return null;
            try
            {
                if (e.HasExited)
                {
                    Log.Write("Sin telon: el motor del relevo se ha cerrado (codigo " + e.ExitCode + "): otro como siempre");
                    if (standbyAtExit == e) standbyAtExit = null;
                    return null;
                }
            }
            catch { return null; }
            return e;
        }

        // RunMpv (cambio de motor): la otra imagen ya esta a la vista por el relevo, sin telon: el motor delante tras el corte, o el motor
        // jugando encima mientras se abren mpv y Magpie
        bool RelevoShowing()
        {
            Relevo r = relevo;
            if (r == null) return false;
            return r.ToEngine ? r.Cut && !r.Curtain : !r.Cut;
        }

        // Sin motor en espera (la sesion empezo con DLSS 5 y aun no se arranco ninguno, o se cerro): uno nuevo en espera ya, con Magpie aun a
        // la vista (~3,7 s). null = no esta listo a tiempo (el telon de siempre)
        Process StartStandbyNow()
        {
            // 21-11 (1.2.1): si otro hilo ya esta arrancando uno (HOME quieto, puente), ese y no otro (dos a la vez se cerraban entre ellos)
            WaitStandbyLaunch(3000);
            if (standbyEngine != null) return standbyEngine;
            if (!StandbyLaunchBegin("sin telon")) { WaitStandbyLaunch(3000); return standbyEngine; }
            try
            {
                string ipc = MpvConst.StandbyIpcName();
                PrepareCfg();
                ProcessStartInfo psi = new ProcessStartInfo(cfg.EnginePath, EngineArgs(cfg, ipc) + " standby=1");
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(cfg.EnginePath);
                Log.Write("Sin telon: no hay motor en espera: se arranca uno escondido: " + psi.FileName + " " + psi.Arguments);
                Process e = Process.Start(psi);
                standbyPrestarted = true;
                SetStandby(e, ipc, false);
                StandbyLaunchEnd(); // (ya es el motor en espera: los demas lo ven)
                Stopwatch sw = Stopwatch.StartNew();
                while (!userExit && standbyEngine == e && !e.HasExited && !standbyReady && sw.ElapsedMilliseconds < 20000) Thread.Sleep(30);
                if (e.HasExited || standbyEngine != e || !standbyReady)
                {
                    Log.Write("Sin telon: el motor en espera no esta listo a tiempo (" + sw.ElapsedMilliseconds + " ms" + (e.HasExited ? ", cerrado" : "") + ")");
                    if (standbyEngine == e) KillStandby("no estuvo listo a tiempo");
                    return null;
                }
                Log.Write("Sin telon: motor en espera listo en " + sw.ElapsedMilliseconds + " ms");
                return e;
            }
            catch (Exception ex) { Log.Error("Sin telon: arrancando un motor en espera", ex); return null; }
            finally { StandbyLaunchEnd(); }
        }

        // (1.2.4 ronda 6, QUITAFG, fase 2: StartRelevoToMagpie era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: WatchRelevoToMagpie era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: RelevoToMagpieActive era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: RelevoFocusToEngine era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: FinishRelevoToMagpie era de Magpie; fuera)

        // ================================================================================== 16-11 (velocidad): el puente
        // dlss5_sin_telon_puente: cada vez que Magpie se (re)abre con mpv en marcha (nivel, pasadas, modo, generacion, R3... con el mando, los
        // ajustes de un juego, la entrada desde el HOME), el motor en espera juega encima con la misma captura (compartida con mpv) hasta que
        // el Magpie nuevo presenta, en vez de 10-15 s de telon. Es el relevo "a Magpie" (FinishRelevoToMagpie lo termina igual) con el motor
        // despertado de la espera en vez del motor en marcha. Sin motor en espera listo, o sin su imagen a tiempo, el telon de siempre

        // El boton de captura del mando acaba de pulsarse (Win derecha + Impr Pant: con Magpie vivo ~0,5 s despues, Magpie se caia)
        bool CaptureButtonRecent { get { return (DateTime.Now - lastShotKey).TotalMilliseconds < 1500; } }
        volatile bool bridgePrestarting;    // arrancando un motor en espera para el proximo puente

        // (1.2.4 ronda 6, QUITAFG, fase 2: BridgeWake era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: BridgeUp era de Magpie; fuera)

        // (dlss5_sin_telon_entrada) ¿Hay un motor en espera listo para entrar en un juego sin telon? Solo lo que se mira sin tocar nada
        bool BridgeEntryReady()
        {
            if (!cfg.Dlss5Seamless || !cfg.MagpieBridge || !cfg.BridgeEntryFirst || cfg.BenchSecs > 0 || relevo != null) return false;
            Process e = standbyEngine;
            try { return e != null && standbyReady && !e.HasExited && StandbyOn() && EngineKnowsSinTelon(); }
            catch { return false; }
        }

        // (1.2.4 ronda 6, QUITAFG, fase 2: BridgeUpAsync era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: BridgeLightSet era de Magpie; fuera)

        // El cambio (entrada en un juego, reapertura de Magpie o de mpv + Magpie) ya ha terminado: el puente tardio no se pone
        sealed class Bandera { public volatile bool Hecho; }

        bool StandbyListoYa() { Process e = standbyEngine; try { return e != null && standbyReady && !e.HasExited; } catch { return false; } }
        bool StandbyEnCamino() { return standbyLaunchBusy || bridgePrestarting || (standbyEngine != null && !standbyReady); }

        // (1.2.4 ronda 6, QUITAFG, fase 2: PuenteTardio era de Magpie; fuera)

        // (1.2.4 ronda 6, QUITAFG, fase 2: PuenteAlReabrir era de Magpie; fuera)

        // Al salir de la app (o un fallo): el motor del relevo, si lo hay, fuera
        void KillRelevo(string why)
        {
            Relevo r;
            lock (relevoSync)
            {
                r = relevo;
                relevo = null;
                relevoAdopt = null;
                relevoAdoptIpc = null;
            }
            relevoAside = null;
            relevoQuit = null;
            if (r == null) return;
            r.Close();
            Process e = r.Engine;
            if (e == null) return;
            if (standbyAtExit == e) standbyAtExit = null;
            try
            {
                if (!e.HasExited && e != mpv) { e.Kill(); Log.Write("Sin telon: motor del relevo cerrado (" + why + ")"); }
            }
            catch { }
        }

        // ---- La ventana del motor (visible: Process.MainWindowHandle; si no, la primera ventana visible del proceso) ----
        delegate bool EnumWindowsProcST(IntPtr h, IntPtr l);
        [DllImport("user32.dll", EntryPoint = "EnumWindows")] static extern bool EnumWindowsST(EnumWindowsProcST cb, IntPtr l);
        [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] static extern bool IsWindowVisibleST(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "GetWindow")] static extern IntPtr GetWindowST(IntPtr h, uint cmd);
        [DllImport("user32.dll", EntryPoint = "SetWindowPos")] static extern bool SetWindowPosST(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        static IntPtr WindowOf(Process e)
        {
            try
            {
                if (e == null || e.HasExited) return IntPtr.Zero;
                e.Refresh();
                IntPtr h = e.MainWindowHandle;
                if (h != IntPtr.Zero) return h;
                uint want = (uint)e.Id;
                IntPtr found = IntPtr.Zero;
                EnumWindowsST(delegate (IntPtr w, IntPtr l)
                {
                    uint pid;
                    GetWindowThreadProcessId(w, out pid);
                    if (pid == want && IsWindowVisibleST(w) && GetWindowST(w, 4) == IntPtr.Zero) { found = w; return false; } // (GW_OWNER: sin dueño)
                    return true;
                }, IntPtr.Zero);
                return found;
            }
            catch { return IntPtr.Zero; }
        }
    }

    partial class Config
    {
        // 21-11 (1.2.1): puente_ligero=2: el motor del puente / relevo con FSR 1 siempre; 1 (de serie): E y FSR 1 en el modo 120 (BridgeLightSet)
        public int BridgeLightLevel = 1;
    }
}
