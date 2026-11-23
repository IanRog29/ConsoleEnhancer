using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace ConsoleEnhancer.Motor
{
    // El motor haciendo de mpv + scripts\switch2_power.lua para el lanzador (ver MpvIpc)
    unsafe partial class Engine
    {
        MpvIpc mpvIpc;
        PowerWatch power;
        Timer eventTimer;
        long framesIn, capN, lastPassed = -100, passedCount, ratePassed;
        bool lastPassedNew = true;
        volatile bool noSignalScreen;
        // 21-11 (sin aviso en los cortes cortos): la pantalla No Signal no se pinta ni se avisa durante NosigSilencioMs
        const int NosigSilencioMs = 5000;
        volatile bool nosigSilencio;
        int nosigEpisodio;
        long nosigFrameDesde;   // (ms de Environment.TickCount) primera imagen No Signal seguida

        // Desde OnFrame: true = no usar esta imagen (es la pantalla No Signal de la capturadora en un corte corto con la consola vista):
        // se queda la ultima del juego en pantalla
        bool NoSignalCongela(byte* src, int pitch, int w, int h, int fmt)
        {
            if (power == null || !power.Enabled || inStandby || !power.ConsoleSeen) { nosigFrameDesde = 0; return false; }
            if (!PowerWatch.FrameIsNoSignal(src, pitch, w, h, fmt)) { nosigFrameDesde = 0; return false; }
            long now = Environment.TickCount & 0x7fffffffL;
            if (nosigFrameDesde == 0 || now < nosigFrameDesde) nosigFrameDesde = now;
            return now - nosigFrameDesde < NosigSilencioMs;
        }
        long firstArrival, lastArrivalT;
        volatile bool signalSeen, signalAcked;
        // 15-11: el aviso "switch2-signal" ya ha llegado a algun cliente. Hasta entonces su respuesta (switch2-signal-ack) no cuenta:
        // el lanzador la mandaba en cuanto oia la consola (antes de la primera imagen), el aviso no salia nunca y el lanzador
        // cerraba el motor a los 30 s "sin imagen" (sesion del 15-11, 19:24:53 y 19:25:27)
        volatile bool signalSent;
        bool earlyAckLogged;
        string pendingLavfSize;                 // video_size de "set demuxer-lavf-o" (se aplica con loadfile)
        // 21-11 (1.2.1): y su framerate y pixel_format (antes solo el tamaño: del menu de la consola a 4K a 60 a un juego a 1440p a 120 sin
        // cerrar el motor, el motor seguia pidiendo 59,94 a la capturadora; ni el formato de pixel_format_4k). null = no venia
        string pendingLavfFps, pendingLavfFmt;
        volatile string awaitSwitch;            // tamaño esperado tras un cambio de captura
        volatile bool mpvGrab;                  // la captura en curso es una peticion por la tuberia de mpv
        // Rafaga de imagenes (diagnostico del tramado)
        string burstPrefix; int burstN, burstI, burstW, burstH;
        volatile bool exiting;
        int tickN, grabSeqN;
        volatile int mpvGrabSeq;

        void StartMpvCompat()
        {
            if (s.Ipc.Length == 0) return;
            power = new PowerWatch();
            power.Enabled = s.Power && (s.Test.Length == 0 || s.PowerTest);
            power.FlagFile = s.Flag;
            // (16-11: en espera ningun aviso de consola, No Signal ni apagado; la vigilancia ademas esta en pausa: power.Suspend)
            power.OnConsole = delegate (bool on) { if (inStandby) return; Ev("switch2-console", on ? "on" : "off"); };
            power.OnNoSignalScreen = delegate (bool on, bool wait)
            {
                if (inStandby) return;
                // 21-11 (el usuario: "no pongas los textos de que se va la señal cuando pasa eso"): con la consola vista antes (un cambio de
                // modo de video de la PS5 al entrar o salir de un juego a 120 Hz, ~2 s) nada en pantalla: se queda la ultima imagen del juego
                // (NoSignalCongela). Solo si dura mas de NosigSilencioMs (apagada de verdad) sale el aviso de siempre
                int ep = Interlocked.Increment(ref nosigEpisodio);
                if (on && wait && power.ConsoleSeen)
                {
                    if (!nosigSilencio) Log.Write("Sin señal con la consola vista: sin aviso ni la pantalla No Signal (la ultima imagen) " + NosigSilencioMs / 1000 + " s");
                    nosigSilencio = true;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Thread.Sleep(NosigSilencioMs);
                        if (ep != nosigEpisodio || !nosigSilencio) return;
                        nosigSilencio = false;
                        Log.Write("Sin señal mas de " + NosigSilencioMs / 1000 + " s: el aviso de siempre");
                        noSignalScreen = true;
                        osd.Show("Sin señal de la consola · Si vuelve en unos segundos, se sigue jugando", 24 * 3600 * 1000, Osd.Warn);
                        frameEvent.Set();
                    });
                    return;
                }
                nosigSilencio = false;
                noSignalScreen = on;
                // (16-11: con la consola vista antes, mientras se mira o se espera "No Signal", el aviso de esperar, como el del script)
                if (on) osd.Show(wait ? "Sin señal de la consola · Si vuelve en unos segundos, se sigue jugando"
                                      : "Enciende la consola · La imagen aparecerá en cuanto se encienda", 24 * 3600 * 1000, Osd.Warn);
                else osd.Hide();
                frameEvent.Set();
            };
            power.HoldMs = s.NosigHoldMs;
            power.HoldLaunchMs = s.NosigHoldLaunchMs;
            power.BlackSecs = s.BlackSecs; // (18-11, CONSOLAS: black_secs del lanzador)
            power.OnDiag = delegate (string t) { if (inStandby) return; Ev("switch2-nosig", t); };
            power.OnTrigger = delegate (string reason, string detail)
            {
                if (inStandby) return;
                if (reason.StartsWith("no-signal", StringComparison.Ordinal)) Ev("switch2-poweroff", reason, detail);
                // Como el script: el lanzador cierra; si no, se sale solo con el codigo de apagado
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Thread.Sleep(300);
                    ExitCode = s.PowerExit;
                    exiting = true;
                    Program.CloseWindow();
                    frameEvent.Set();
                });
            };
            mpvIpc = new MpvIpc(s.Ipc, OnMpvCommand);
            mpvIpc.Start();
            eventTimer = new Timer(delegate { EventTick(); }, null, 500, 500);
            Log.Write("Tuberia al estilo de mpv: \\\\.\\pipe\\" + s.Ipc + (power.Enabled ? ", con vigilancia de apagado" : ""));
        }

        void Ev(params string[] a) { if (mpvIpc != null) mpvIpc.Event(a); }

        // Cada 0,5 s: imagen a la vista (hasta que el lanzador conteste) y ritmo tras quitar repetidas
        void EventTick()
        {
            try
            {
                if (mpvIpc == null) return;
                // 16-11: en espera, ni aviso de imagen, ni consola, ni ritmo, y el apagado en pausa (el lanzador esta con mpv)
                if (inStandby) return;
                XeFgFailTick();   // (1.2.4, integracion: XeSS FG pedido y no arrancado: que el lanzador lo sepa, EngineFgXe.cs)
                FmFailTick();     // (23-11, FGMULTI: FSR FG x2 / x3 / x4 rota, EngineFsrMulti.cs)
                if (signalSeen && !signalAcked && mpvIpc.Event("switch2-signal", "on") > 0) signalSent = true;
                // La vigilancia de apagado, con su propio reloj (no el del pintado). 20-11 (CIERRES): con las imagenes que llegan de la
                // capturadora (framesIn), no con las presentadas: la grafica parada no es la consola apagada (EngineCuelgue.cs)
                if (power != null && (++tickN % 2) == 0) power.Tick(framesIn);
                if (power != null) Ev("switch2-console", power.ConsoleOn ? "on" : "off");
                long p = passedCount, d = p - ratePassed;
                ratePassed = p;
                Ev("switch2-rate", (d * 2).ToString(CultureInfo.InvariantCulture), lastPassedNew ? "new" : "copy");
                if (firstPaintAt != 0 && Clock.SinceMs(firstPaintAt) < 5000) Ev("switch2-pintada", presentedFrames.ToString(CultureInfo.InvariantCulture));   // (1.2.4 arreglos, RELEVO)
            }
            catch (Exception ex) { Log.Error("Avisos", ex); }
        }

        // Cada imagen que llega (antes de quitar repetidas)
        void FrameHook(byte* src, int pitch, int w, int h, int fmt, long arrival)
        {
            framesIn++;
            if (firstArrival == 0) firstArrival = arrival;
            lastArrivalT = arrival;
            if (!signalSeen) { signalSeen = true; Log.Write("Primera imagen de la fuente: " + w + "x" + h); }
            if (power != null) power.Sample(src, pitch, w, h, fmt, false);
            string aw = awaitSwitch;
            // (21-11, 1.2.1: o el tamaño que eligio la captura porque la capturadora no da el pedido: DsCapture.LastSizeSwap "pedido>elegido")
            if (aw != null && (aw == w + "x" + h || Capture.DsCapture.LastSizeSwap == aw + ">" + w + "x" + h))
            {
                awaitSwitch = null;
                Ev("switch2-switched", aw);
                Log.Write("Captura cambiada a " + aw);
            }
            if (burstPrefix != null) BurstStep(src, pitch, w, h, fmt);
        }

        void GrabDone(string path, int w, int h)
        {
            if (!mpvGrab) return;
            mpvGrab = false;
            Ev("switch2-grabbed", path, w.ToString(CultureInfo.InvariantCulture), h.ToString(CultureInfo.InvariantCulture),
               (w * 4).ToString(CultureInfo.InvariantCulture), "bgr0");
        }

        void BurstStep(byte* src, int pitch, int w, int h, int fmt)
        {
            try
            {
                // (23-11, SINMPV: en color, como las de mpv: EngineGrabColor.cs. Antes con YUY2 salia la luma de puntos alternos -bpp 4 en vez de 2-)
                int bpp = fmt == Shaders.FmtBgr24 ? 3 : fmt == Shaders.FmtYuy2 ? 2 : fmt == Shaders.FmtNv12 ? 1 : 4;
                byte[] o = new byte[w * h * 4];
                fixed (byte* d = o)
                    for (int y = 0; y < h; y++)
                        GrabColor.Row(src + (long)y * pitch, fmt == Shaders.FmtNv12 ? src + (long)h * pitch + (long)(y / 2) * pitch : null, fmt, bpp, w, 1, d + (long)y * w * 4);
                File.WriteAllBytes(burstPrefix + "_" + burstI + ".raw", o);
                burstW = w; burstH = h;
            }
            catch (Exception ex) { Log.Error("Rafaga", ex); burstN = burstI; }
            burstI++;
            if (burstI >= burstN)
            {
                string p = burstPrefix;
                burstPrefix = null;
                Ev("switch2-burst-done", p, burstI.ToString(CultureInfo.InvariantCulture), burstW.ToString(CultureInfo.InvariantCulture),
                   burstH.ToString(CultureInfo.InvariantCulture), (burstW * 4).ToString(CultureInfo.InvariantCulture), "bgr0");
            }
        }

        static string Arg(List<string> c, int i) { return i < c.Count ? c[i] : ""; }

        // Una orden de la tuberia de mpv. Devuelve el JSON de "data" para get_property
        string OnMpvCommand(List<string> c)
        {
            string name = c[0];
            switch (name)
            {
                case "get_property":
                case "get_property_string":
                    {
                        string prop = Arg(c, 1);
                        if (prop == "time-pos")
                        {
                            // De la ultima imagen PRESENTADA (no de la que llega): si el pintado se cuelga, el lanzador lo ve parado
                            if (firstArrival == 0 || presentedFrames == 0) return null;
                            return (Clock.Ms(firstArrival, lastPresentedArrival) / 1000.0).ToString("0.000###", CultureInfo.InvariantCulture);
                        }
                        if (prop == "pause") return "false";
                        string cierres = CierresProperty(prop);   // 20-11 (CIERRES): switch2-captura-ms, switch2-gpu (EngineCuelgue.cs)
                        if (cierres != null) return cierres;
                        if (prop == "video-params/w") return inW.ToString(CultureInfo.InvariantCulture);
                        if (prop == "video-params/h") return inH.ToString(CultureInfo.InvariantCulture);
                        return null;
                    }
                case "show-text":
                    {
                        int ms = 2500;
                        int.TryParse(Arg(c, 2), out ms);
                        if (!noSignalScreen) { osd.Show(Arg(c, 1), ms <= 0 ? 2500 : ms, null); frameEvent.Set(); }
                        return null;
                    }
                case "script-message":
                    {
                        string m = Arg(c, 1);
                        if (m == "switch2-osd")
                        {
                            // 15-11 (Prisma): los avisos de la app (antes show-text), como los pinta el script de mpv:
                            // "switch2-osd <ms> <texto> [warn]" (warn = punto rojo)
                            int ms = 2500;
                            int.TryParse(Arg(c, 2), out ms);
                            if (!noSignalScreen && Arg(c, 3).Length > 0)
                            {
                                osd.Show(Arg(c, 3), ms <= 0 ? 2500 : ms, Arg(c, 4) == "warn" ? Osd.Warn : Osd.Cyan);
                                frameEvent.Set();
                            }
                        }
                        else if (m == "switch2-audio") { if (power != null) power.Audio(Arg(c, 2)); }
                        else if (m == "switch2-signal-ack")
                        {
                            // (solo tras haber mandado el aviso: uno de antes no cuenta y se sigue avisando cada 0,5 s)
                            if (signalSent) signalAcked = true;
                            else if (!earlyAckLogged) { earlyAckLogged = true; Log.Write("switch2-signal-ack antes del aviso de imagen: no cuenta, se sigue avisando"); }
                        }
                        else if (m == "switch2-switching" || m == "switch2-launch" || m == "switch2-powermenu")
                        {
                            // switch2-switching: cambio de captura (una ventana sin mirar). 16-11, como el script: switch2-launch = se
                            // arranca un juego (la consola puede cambiar de modo de video: "No Signal" espera HoldLaunchMs);
                            // switch2-powermenu = el menu de apagado (sin espera: "No Signal" es la consola apagada)
                            double def = m == "switch2-switching" ? 8 : 20, secs;
                            if (!double.TryParse(Arg(c, 2), NumberStyles.Float, CultureInfo.InvariantCulture, out secs)) secs = def;
                            if (power != null)
                            {
                                if (m == "switch2-switching") power.Switching(secs);
                                else if (m == "switch2-launch") power.Launch(secs);
                                else power.PowerMenu(secs);
                            }
                        }
                        else if (m == "switch2-grab")
                        {
                            grabPath = Arg(c, 2); grabMode = Arg(c, 3).Length > 0 ? Arg(c, 3) : "full";
                            int seq = Interlocked.Increment(ref grabSeqN);
                            mpvGrabSeq = seq;
                            mpvGrab = true;
                            grabWanted = true;
                            // Sin imagen en 1,5 s: respuesta vacia (como el script). Solo si sigue siendo esta peticion
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                Thread.Sleep(1500);
                                if (mpvGrab && mpvGrabSeq == seq) { mpvGrab = false; grabWanted = false; Ev("switch2-grabbed", ""); }
                            });
                        }
                        else if (m == "switch2-burst")
                        {
                            int n = 2;
                            int.TryParse(Arg(c, 3), out n);
                            burstN = Math.Max(1, Math.Min(4, n)); burstI = 0;
                            burstPrefix = Arg(c, 2);
                        }
                        // 16-11: motor en espera (docs\STANDBY.md). Se atienden en el hilo de pintado, en orden (EngineStandby.cs)
                        // ("switch2-wake detras": sin telon, la ventana al fondo y sin el foco hasta switch2-delante)
                        else if (m == "switch2-standby") AskStandby(false);
                        else if (m == "switch2-wake") AskStandby(true, Arg(c, 2) == "detras");
                        // 16-11 (sin telon, docs\STANDBY.md 8): delante y con el foco / encima de todo sin el foco (1, 2 reafirmado) o al fondo (0)
                        // / la capturadora otra vez ya (si no pudo abrirla mientras otro la tenia)
                        else if (m == "switch2-delante") { bool ok = Program.Front(); Log.Write("switch2-delante: ventana delante y con el foco" + (ok ? "" : " (NO se pudo a tiempo)") + "; el foco se recupera solo otra vez"); }
                        else if (m == "switch2-encima")
                        {
                            int mode = 1;
                            int.TryParse(Arg(c, 2), out mode);
                            mode = Math.Max(0, Math.Min(2, mode));
                            bool ok = Program.SetOnTop(mode);
                            Log.Write("switch2-encima " + mode + ": " + (mode == 0 ? "al fondo de todo" : mode == 1 ? "encima de todo, sin el foco" : "encima de todo y reafirmado, sin el foco") +
                                      (ok ? "" : " (NO se pudo a tiempo)"));
                        }
                        else if (m == "switch2-reabrir") { reopenAsked = true; frameEvent.Set(); }
                        else if (m == "switch2-vigila") EscVigila(Arg(c, 2) != "0");   // (1.3.1, FPS: el menu de ajustes abierto = 0; EngineEscalera.cs)
                        // 22-11 (1.2.3, MOTOR5): las ordenes de prueba (switch2-test-*: el lanzador solo las hace con test_source) a todos los
                        // clientes, como mpv con cualquier script-message: las pruebas sin mando tambien con el motor haciendo la imagen
                        else if (m.StartsWith("switch2-test-", StringComparison.Ordinal)) { List<string> r = new List<string>(); for (int i = 1; i < c.Count; i++) r.Add(Arg(c, i)); Ev(r.ToArray()); }
                        else Log.Write("script-message sin uso: " + m);
                        return null;
                    }
                case "vf":
                    {
                        // El filtro de repetidas del lanzador: set = quitarlas, clr = no
                        string op = Arg(c, 1);
                        if (op == "set" || op == "add") s.Dedupe = 1;
                        else if (op == "clr" || op == "remove") s.Dedupe = 0;
                        Log.Write("Repetidas: " + (s.Dedupe == 1 ? "se quitan" : "se dejan"));
                        return null;
                    }
                case "set":
                case "set_property":
                    {
                        string prop = Arg(c, 1), val = Arg(c, 2);
                        if (prop == "demuxer-lavf-o")
                        {
                            Match mm = Regex.Match(val, @"video_size=(\d+x\d+)");
                            if (mm.Success) pendingLavfSize = mm.Groups[1].Value;
                            // (21-11, 1.2.1: la velocidad y el formato, con el mismo nombre que en los argumentos del motor: fps= y format=)
                            Match mf = Regex.Match(val, @"framerate=(\d+(?:/\d+)?)");
                            pendingLavfFps = mf.Success ? mf.Groups[1].Value : null;
                            Match mp = Regex.Match(val, @"pixel_format=([a-z0-9]+)");
                            // (como TrayApp.EngineFormat del lanzador: bgr24 -> rgb24, yuyv422 -> yuy2, nv12; el resto o sin el, auto)
                            pendingLavfFmt = !mp.Success ? (mm.Success ? "auto" : null) :
                                             mp.Groups[1].Value == "yuyv422" ? "yuy2" : mp.Groups[1].Value == "bgr24" ? "rgb24" :
                                             mp.Groups[1].Value == "nv12" ? "nv12" : "auto";
                        }
                        else if (prop == "pause" || prop == "geometry" || prop == "glsl-shaders" || prop == "glsl-shader-opts") { }
                        else Log.Write("set sin uso: " + prop);
                        return null;
                    }
                case "loadfile":
                    {
                        // Cambio de captura sin cerrar: tamaño de demuxer-lavf-o (o el size= de una fuente de prueba)
                        string url = Arg(c, 1);
                        string size = pendingLavfSize;
                        Match tm = Regex.Match(url, @"size=(\d+x\d+)");
                        if (url.StartsWith("av://lavfi", StringComparison.Ordinal) && tm.Success) size = tm.Groups[1].Value;
                        if (size != null)
                        {
                            awaitSwitch = size;
                            // (21-11, 1.2.1: con la capturadora -no una fuente de prueba- tambien la velocidad y el formato de demuxer-lavf-o)
                            bool cap = !url.StartsWith("av://lavfi", StringComparison.Ordinal);
                            string fps = cap ? pendingLavfFps : null, fmt = cap ? pendingLavfFmt : null;
                            pendingLavfFps = null; pendingLavfFmt = null;
                            lock (pendingLock)
                            {
                                pending.Add(new KeyValuePair<string, string>("capture", size));
                                if (fps != null) pending.Add(new KeyValuePair<string, string>("fps", fps));
                                if (fmt != null) pending.Add(new KeyValuePair<string, string>("format", fmt));
                            }
                            frameEvent.Set();
                            Log.Write("loadfile: captura a " + size + (fps != null ? " a " + fps : "") + (fmt != null ? " " + fmt : ""));
                        }
                        return null;
                    }
                case "quit":
                    {
                        int code = 0;
                        int.TryParse(Arg(c, 1), out code);
                        ExitCode = code;
                        Program.CloseWindow();
                        frameEvent.Set();
                        return null;
                    }
                case "observe_property":
                case "disable_event":
                case "enable_event":
                case "client_name":
                    return null;
                default:
                    // Ordenes propias (las de la otra tuberia) tambien por esta: "engine-set fg=1"
                    if (name == "engine")
                    {
                        string rest = string.Join(" ", c.GetRange(1, c.Count - 1).ToArray());
                        return MpvIpc.Quote(OnCommand(rest));
                    }
                    Log.Write("Orden de mpv sin uso: " + name);
                    return null;
            }
        }
    }
}
