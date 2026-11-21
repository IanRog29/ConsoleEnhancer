using System;
using System.Globalization;
using System.IO;

namespace ConsoleEnhancer.Motor
{
    // La vigilancia de apagado de scripts\switch2_power.lua (de mpv), en el motor. Mismas reglas y tiempos:
    //  - pantalla "No Signal" de AVerMedia (franjas negras + recuadro gris) vista, confirmada con mas miradas en silencio
    //    (2 en 0,4 s quieta y en silencio, o 3 en 1 s) -> apagada ("no-signal-lua"); con sonido nunca
    //  - armada tras 10 s de imagen buena: sin imagenes 10 s (stall), negro 30 s en silencio, congelada 180 s en silencio
    //  - "No Signal" 4 s tras haber visto la consola sin estar armada ("no-signal-unarmed")
    //  - 16-11 (como el script): "No Signal" confirmada aun no es un apagado (un cambio de modo de video la saca unos segundos:
    //    arrancando RESIDENT EVIL requiem la app se cerraba con la consola encendida; duro menos de 21 s). Se espera HoldMs (6 s;
    //    arrancando un juego, switch2-launch: HoldLaunchMs, 25 s; tras el menu de apagado, switch2-powermenu: nada) con el aviso
    //    "Sin señal de la consola" encima, y si vuelve la imagen se sigue. switch2-launch ya no es una ventana sin mirar: "No Signal"
    //    se mira con la espera larga (y no cuentan ni sin imagenes, ni negro, ni congelada); switch2-switching si (cambio de captura)
    //  - 16-11 tarde (APAGADO, como el script): HoldMs 3 s (antes 6); el lanzador manda switch2-powermenu 3 cada 0,5 s en el HOME sin nada
    //    arrancando (sin espera) y switch2-powermenu 0 / switch2-launch 0 para quitar una u otra; la espera de cada "No Signal" se decide
    //    al verla y solo crece (HoldEpisode); sin espera (0) se confirma como el script (3 miradas en 0,4 s quieta y en silencio)
    // La diferencia: el motor tiene cada imagen en memoria, asi que mira la rejilla 5 veces por segundo sin coste.
    unsafe class PowerWatch
    {
        const int GW = 48, GH = 27;
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        public int ArmSecs = 10, StallSecs = 10, BlackSecs = 30, FrozenSecs = 180;
        // Confirmacion mas larga que la del script (0,4 / 1 s): aqui se mira 5 veces por segundo, no cada 1,2 s, y una pantalla de carga
        // quieta con un recuadro gris sobre negro no debe cerrar la app
        public double NosigSecs = 4, BlackLuma = 18, ConfirmSilentMs = 1000, ConfirmMs = 2000;
        // 16-11 tarde (APAGADO): sin espera (tras el menu de apagado, en el HOME sin nada arrancando) la consola se esta apagando: la
        // confirmacion del script (2 miradas en 0,4 s; aqui 3, a 5 por segundo) en vez del segundo de arriba (era para pantallas de carga)
        public double ConfirmQuickMs = 400;
        // 16-11: lo que tiene que durar "No Signal" confirmada (desde la primera mirada) para darla por apagada (los nosig_hold_ms /
        // nosig_hold_launch_ms del script: apagado_espera_s / apagado_espera_arranque_s del switch2.ini). 16-11 tarde: 3 s (antes 6)
        public double HoldMs = 3000, HoldLaunchMs = 25000;
        public bool Enabled = true;
        public string FlagFile = "";

        public Action<bool> OnConsole;            // consola a la vista / pantalla No Signal
        public Action<bool, bool> OnNoSignalScreen; // poner / quitar el aviso a pantalla completa (16-11: true = el de esperar,
                                                    // "Sin señal de la consola"; false = "Enciende la consola")
        public Action<string, string> OnTrigger;  // apagada (motivo, detalle)
        public Action<string> OnDiag;

        readonly object sync = new object();
        bool armed, triggered, consoleOn, consoleSeen, noSignalShown;
        int goodSecs, stallN, blackN, frozenN;
        byte[] lastFp;
        bool? lastBlack;
        string extAudio; long extAudioT, silentSince;
        long nosigSince, switchingUntil;
        // confirmacion rapida
        int nsN; long nsT0; byte[] nsFp; bool nsStill = true; string nsSrc; bool nsSoundSaid;
        // ultima muestra
        double sAvg, sMax; byte[] sFp; long sT;
        long lastFrames = -1;
        long lastSampleAt;
        volatile bool suspended;
        // 16-11: espera de "No Signal": contexto (hasta cuando) y estado (mirandose / confirmada esperando) y el aviso pintado
        long launchUntil, powermenuUntil;
        bool nsEval, nsHeld;
        bool? paintedWait;
        // 16-11 tarde: la espera de este "No Signal" (ms, -1 = ninguno; decidida al verla y solo crece: HoldEpisode)
        double nsHold = -1;
        string nsWhy;

        static long Now { get { return Clock.Now; } }

        // 16-11 (motor en espera, docs\STANDBY.md): en pausa no se mira nada ni se avisa de nada (ni consola, ni No Signal, ni
        // apagado). Al volver, todo de cero como al arrancar: se arma tras ArmSecs de imagen buena ("Apagado: deteccion armada").
        // Se conservan el sonido de la consola y un switch2-switching/launch reciente
        public void Suspend() { lock (sync) { suspended = true; NsReset(); } }

        public void Resume()
        {
            lock (sync)
            {
                armed = triggered = consoleOn = consoleSeen = noSignalShown = false;
                goodSecs = 0;
                Reset();
                NsReset();
                nsSoundSaid = false;
                lastBlack = null;
                nosigSince = 0;
                sAvg = sMax = 0; sFp = null; sT = 0;
                lastFrames = -1;
                lastSampleAt = 0;
                paintedWait = null;
                suspended = false;
            }
        }
        static double Secs(long since) { return Clock.SinceMs(since) / 1000.0; }
        static long Ticks(double secs) { return (long)(secs * 1000 / Clock.TickMs); }

        // 16-11: switch2-launch <s> (el lanzador: se arranca un juego): "No Signal" espera HoldLaunchMs mientras dure
        // (16-11, revision: y anula el menu de apagado visto antes, abierto y cancelado: si no, 20 s sin espera al arrancar un juego)
        // (16-11 tarde: switch2-launch 0 = ya no se arranca nada: el lanzador la cancela, vuelto al HOME sin entrar en el juego)
        public void Launch(double secs) { lock (sync) { launchUntil = Now + Ticks(secs); powermenuUntil = 0; } }

        // 16-11: switch2-powermenu <s> (el lanzador vio el menu de apagado o el boton de apagado): "No Signal" = apagada, sin esperar
        // (16-11 tarde: tambien cada 0,5 s, "switch2-powermenu 3", en el HOME sin nada arrancando; "switch2-powermenu 0" al dejar de estarlo)
        public void PowerMenu(double secs) { lock (sync) { powermenuUntil = Now + Ticks(secs); launchUntil = 0; } }

        bool LaunchingNow { get { return launchUntil != 0 && Now < launchUntil; } }

        // Lo que tiene que durar "No Signal" confirmada para dar la consola por apagada (ms) y por que (como hold_now del script)
        double HoldNow(out string why)
        {
            if (powermenuUntil != 0 && Now < powermenuUntil) { why = "tras el menu de apagado"; return 0; }
            if (LaunchingNow) { why = "arrancando un juego"; return HoldLaunchMs; }
            why = "jugando o entrando o saliendo del HOME";
            return HoldMs;
        }

        // 16-11 tarde (APAGADO, como hold_episode del script): la de este "No Signal": la de cuando se vio y, si luego toca mas (se marca un
        // arranque), esa; nunca menos (al pasar a "en el HOME sin nada arrancando" a mitad, recien vuelto de un juego, se cerraba sin esperar)
        double HoldEpisode(out string why)
        {
            double h = HoldNow(out why);
            if (nsHold < 0 || h > nsHold) { nsHold = h; nsWhy = why; }
            why = nsWhy;
            return nsHold;
        }

        public void Audio(string state)
        {
            lock (sync)
            {
                long now = Now;
                if (state != "silent") silentSince = 0;
                else if (extAudio != "silent" || Secs(extAudioT) >= 3 || silentSince == 0) silentSince = now;
                extAudio = state; extAudioT = now;
            }
        }

        public void Switching(double secs)
        {
            lock (sync)
            {
                switchingUntil = Now + (long)(secs * 1000 / Clock.TickMs);
                Reset();
                lastFrames = -1;
            }
        }
        bool SwitchingNow { get { return switchingUntil != 0 && Now < switchingUntil; } }

        void Reset() { stallN = blackN = frozenN = 0; lastFp = null; }
        void NsReset() { nsN = 0; nsT0 = 0; nsFp = null; nsStill = true; nsSrc = null; nsEval = nsHeld = false; nsHold = -1; nsWhy = null; }

        string AudioState() { return extAudio == null || Secs(extAudioT) >= 3 ? "desconocido" : extAudio; }

        // Rejilla de 48x27 (luma media, maxima y huella RGB). Desde el hilo de captura, como mucho 5 veces por segundo
        public void Sample(byte* data, int pitch, int w, int h, int fmt, bool force)
        {
            if (!Enabled || suspended) return;
            long now = Now;
            if (!force && lastSampleAt != 0 && Clock.Ms(lastSampleAt, now) < 200) return;
            lastSampleAt = now;
            byte[] fp = new byte[GW * GH * 3];
            double sum = 0, mx = 0;
            int n = 0;
            for (int j = 0; j < GH; j++)
            {
                int y = (int)((j + 0.5) * h / GH);
                byte* row = data + (long)y * pitch;
                for (int i = 0; i < GW; i++)
                {
                    int x = (int)((i + 0.5) * w / GW);
                    int R, G, B;
                    if (fmt == Shaders.FmtBgr24) { byte* p = row + x * 3; B = p[0]; G = p[1]; R = p[2]; }
                    else if (fmt == Shaders.FmtBgrx32) { byte* p = row + x * 4; B = p[0]; G = p[1]; R = p[2]; }
                    else if (fmt == Shaders.FmtYuy2) { byte* p = row + (x & ~1) * 2 + (x & 1) * 2; R = G = B = p[0]; }
                    else { R = G = B = row[x]; }
                    double l = (54 * R + 183 * G + 19 * B) / 256.0;
                    sum += l; if (l > mx) mx = l;
                    fp[n * 3] = (byte)R; fp[n * 3 + 1] = (byte)G; fp[n * 3 + 2] = (byte)B;
                    n++;
                }
            }
            lock (sync)
            {
                if (suspended) return;
                sAvg = sum / n; sMax = mx; sFp = fp; sT = now;
                if (!consoleSeen && !triggered)
                {
                    // Arranque: con imagen de la consola (ni "No Signal" ni negra) se da por encendida
                    UpdateNoSignal(fp);
                    if (!noSignalShown && !(sAvg < BlackLuma && sMax < BlackLuma * 3)) SetConsole(true);
                }
                FastSeen(fp, "imagen");
                if (noSignalShown || nsN >= 2) UpdateNoSignal(fp);
            }
        }

        // 21-11 (el usuario: "no pongas los textos de que se va la señal cuando pasa eso"): ¿esta imagen es la pantalla No Signal de la
        // capturadora? (cada imagen, para no pintarla: el motor deja la ultima del juego en los cortes cortos)
        public static bool FrameIsNoSignal(byte* data, int pitch, int w, int h, int fmt)
        {
            byte[] fp = new byte[GW * GH * 3];
            int n = 0;
            for (int j = 0; j < GH; j++)
            {
                byte* row = data + (long)(int)((j + 0.5) * h / GH) * pitch;
                for (int i = 0; i < GW; i++)
                {
                    int x = (int)((i + 0.5) * w / GW);
                    int R, G, B;
                    if (fmt == Shaders.FmtBgr24) { byte* p = row + x * 3; B = p[0]; G = p[1]; R = p[2]; }
                    else if (fmt == Shaders.FmtBgrx32) { byte* p = row + x * 4; B = p[0]; G = p[1]; R = p[2]; }
                    else if (fmt == Shaders.FmtYuy2) { byte* p = row + (x & ~1) * 2 + (x & 1) * 2; R = G = B = p[0]; }
                    else { R = G = B = row[x]; }
                    fp[n * 3] = (byte)R; fp[n * 3 + 1] = (byte)G; fp[n * 3 + 2] = (byte)B;
                    n++;
                }
            }
            return IsNoSignal(fp);
        }

        public bool ConsoleSeen { get { lock (sync) return consoleSeen; } }

        static bool IsNoSignal(byte[] fp)
        {
            if (fp == null) return false;
            int blkN = 0, blkOk = 0, boxN = 0, boxOk = 0;
            for (int j = 0; j < GH; j++)
            {
                double fy = (j + 0.5) / GH;
                for (int i = 0; i < GW; i++)
                {
                    double fx = (i + 0.5) / GW;
                    int p = (j * GW + i) * 3;
                    int R = fp[p], G = fp[p + 1], B = fp[p + 2];
                    double l = (54 * R + 183 * G + 19 * B) / 256.0;
                    if (fy < 0.27 || fy > 0.67) { blkN++; if (l < 20) blkOk++; }
                    else if (fy > 0.33 && fy < 0.63 && fx > 0.10 && fx < 0.90)
                    {
                        boxN++;
                        if (l > 110 && l < 235 && Math.Abs(R - G) < 25 && Math.Abs(G - B) < 25) boxOk++;
                    }
                }
            }
            return blkN > 0 && boxN > 0 && blkOk >= blkN * 0.95 && boxOk >= boxN * 0.75;
        }

        static bool SameFp(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            long d = 0;
            for (int i = 0; i < a.Length; i++) d += Math.Abs(a[i] - b[i]);
            return d / (double)a.Length < 4;
        }

        static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        void SetConsole(bool on)
        {
            if (on == consoleOn) return;
            consoleOn = on;
            if (on) consoleSeen = true;
            if (OnConsole != null) OnConsole(on);
        }

        public bool ConsoleOn { get { lock (sync) return consoleOn; } }

        void UpdateNoSignal(byte[] fp)
        {
            bool ns = IsNoSignal(fp);
            if (ns == noSignalShown) return;
            noSignalShown = ns;
            nosigSince = ns ? Now : 0;
            if (ns)
            {
                Log.Write(WaitingNow ? "Pantalla No Signal de la capturadora: aviso de esperar por si vuelve la imagen"
                                     : "Pantalla No Signal de la capturadora: aviso para encender la consola");
                PaintNoSignal();
            }
            else
            {
                Log.Write("La pantalla No Signal ha desaparecido");
                paintedWait = null;
                if (OnNoSignalScreen != null) OnNoSignalScreen(false, false);
            }
            SetConsole(!ns);
        }

        // 16-11: el aviso a pantalla completa (como nosig_paint del script): "Sin señal de la consola" mientras se mira o se espera
        // "No Signal" tras haber visto la consola; si no, "Enciende la consola" (la app abierta con la consola apagada)
        bool WaitingNow { get { return (nsEval || nsHeld) && consoleSeen; } }
        void PaintNoSignal()
        {
            bool wait = WaitingNow;
            if (paintedWait == wait) return;
            paintedWait = wait;
            if (OnNoSignalScreen != null) OnNoSignalScreen(true, wait);
        }

        void Diag(string t) { Log.Write("Apagado: " + t); if (OnDiag != null) OnDiag(t); }

        void FastSeen(byte[] fp, string src)
        {
            if (triggered || !Enabled || !consoleSeen || SwitchingNow) { if (nsN > 0) NsReset(); return; }
            long now = Now;
            if (!IsNoSignal(fp))
            {
                if (nsN > 0)
                {
                    Diag(string.Format(IC, "pantalla No Signal vista {0} {1} en {2:0} ms y ya no se ve: no era un apagado{3}", nsN, nsN == 1 ? "vez" : "veces",
                        Clock.Ms(nsT0, now), nsHeld ? " (un cambio de modo de video): se sigue" : ""));
                    // (16-11: el aviso fuera ya)
                    if (noSignalShown) UpdateNoSignal(fp);
                }
                NsReset();
                return;
            }
            string audio = AudioState();
            if (audio == "sound")
            {
                if (!nsSoundSaid) { nsSoundSaid = true; Diag("pantalla No Signal vista con sonido de la consola (" + src + "): no cuenta"); }
                NsReset();
                return;
            }
            string why;
            if (nsN == 0)
            {
                nsT0 = now; nsStill = true; nsSrc = src;
                nsEval = true;
                HoldEpisode(out why); // (16-11 tarde: la espera de este "No Signal", la de ahora)
                Diag("pantalla No Signal vista (" + src + ", audio " + audio + "): se confirma");
            }
            else nsStill = nsStill && SameFp(fp, nsFp);
            nsN++;
            nsFp = fp;
            double span = Clock.Ms(nsT0, now);
            bool quick = nsStill && audio == "silent";
            double hold = HoldEpisode(out why);
            // 16-11 tarde: sin espera (la consola se esta apagando: tras el menu de apagado, en el HOME sin nada arrancando) quieta y en
            // silencio basta ConfirmQuickMs (como el script); con espera, como antes
            bool confirmed = (quick && hold <= 0 && nsN >= 3 && span >= ConfirmQuickMs) ||
                             (quick && nsN >= 4 && span >= ConfirmSilentMs) || (nsN >= 6 && span >= ConfirmMs);
            if (confirmed)
            {
                // 16-11: confirmada; apagada si ademas dura lo de HoldNow (un cambio de modo de video vuelve antes). 16-11 tarde: HoldEpisode
                if (span >= hold)
                {
                    Trigger("no-signal-lua", string.Format(IC, "{0} miradas en {1:0} ms, {2}, audio {3}, la primera en {4}{5}",
                        nsN, span, nsStill ? "quieta" : "cambiando", audio, nsSrc,
                        hold > 0 ? string.Format(IC, ", {0:0} s esperados: {1}", hold / 1000, why) : ""));
                    return;
                }
                if (!nsHeld)
                {
                    nsHeld = true;
                    Diag(string.Format(IC, "pantalla No Signal confirmada ({0}, audio {1}): se espera hasta {2:0} s por si vuelve la imagen ({3}: puede ser " +
                        "un cambio de modo de video)", nsStill ? "quieta" : "cambiando", audio, hold / 1000, why));
                    // El aviso a pantalla completa ya ("Sin señal de la consola")
                    if (noSignalShown) PaintNoSignal(); else UpdateNoSignal(fp);
                }
            }
        }

        // Cada segundo. frames = imagenes recibidas (para ver si avanza). 20-11 (CIERRES): las de la capturadora (framesIn del motor); hasta
        // ahora le llegaban las presentadas y la grafica parada contaba como "sin imagenes" (la consola apagada a los StallSecs)
        public void Tick(long frames)
        {
            lock (sync)
            {
                if (suspended) return;
                if (SwitchingNow) { Reset(); lastFrames = -1; return; }
                if (!Enabled || triggered) return;
                bool advancing = lastFrames >= 0 && frames != lastFrames;
                if (lastFrames >= 0 && frames == lastFrames) stallN++;
                else stallN = 0;
                lastFrames = frames;

                bool? silent = null;
                if (extAudio != null && Secs(extAudioT) < 3) silent = extAudio == "silent";

                if (sFp != null && Secs(sT) < 3)
                {
                    lastBlack = noSignalShown || nsN > 0 || (sAvg < BlackLuma && sMax < BlackLuma * 3);
                    bool frozen = lastFp != null && Equal(sFp, lastFp);
                    lastFp = sFp;
                    frozenN = (frozen && silent == true) ? frozenN + 1 : 0;
                }
                else { if (silent != true) lastBlack = null; frozenN = 0; }

                // Consola apagada al poco de encenderla (sin armar): "No Signal" que dura (16-11: como poco lo de HoldNow; 16-11 tarde: o lo
                // de este "No Signal" si se esta mirando, nsHold)
                if (noSignalShown && consoleSeen && nosigSince != 0)
                {
                    string why;
                    double hold = HoldNow(out why);
                    if (nsN > 0 && nsHold > hold) hold = nsHold;
                    if (Secs(nosigSince) >= Math.Max(NosigSecs, hold / 1000)) { Trigger("no-signal-unarmed", ""); return; }
                }

                bool black = lastBlack == true;
                blackN = (black && silent == true) ? blackN + 1 : 0;

                if (!armed && advancing && !noSignalShown && (silent != true || lastBlack == false))
                {
                    goodSecs++;
                    if (goodSecs >= ArmSecs) { armed = true; Reset(); Log.Write("Apagado: deteccion armada"); }
                }
                if (!armed) return;
                // 16-11: arrancando un juego (switch2-launch) ni sin imagenes, ni negro largo, ni congelada (como el script: antes
                // esa ventana era la de switching; ahora "No Signal" si se mira, con su espera larga)
                if (LaunchingNow) { stallN = blackN = frozenN = 0; return; }
                if (stallN >= StallSecs) Trigger("stall", "");
                else if (blackN >= BlackSecs) Trigger("black", "");
                else if (frozenN >= FrozenSecs) Trigger("frozen", "");
            }
        }

        void Trigger(string reason, string detail)
        {
            if (triggered) return;
            triggered = true;
            Log.Write("Consola apagada (" + reason + (detail.Length > 0 ? ", " + detail : "") + ")");
            if (FlagFile.Length > 0)
                try { File.WriteAllText(FlagFile, reason + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n"); }
                catch (Exception ex) { Log.Error("flag de apagado", ex); }
            if (OnTrigger != null) OnTrigger(reason, detail);
        }
    }
}
