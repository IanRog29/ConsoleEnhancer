using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 16-11: el motor "en espera" (docs\STANDBY.md). El lanzador alterna este motor (DLSS 5 apagado) con mpv + Magpie (DLSS 5
    // puesto) y la capturadora solo la puede abrir uno: antes cada vuelta al motor era un arranque en frio (~4,5 s en negro:
    // 1,4 s de slInit + 1,7 s de slSetD3DDevice + el resto). En espera el motor sigue vivo con Streamline, el dispositivo, la
    // swapchain y DLSS / la generacion (y sus recursos), sin la capturadora, sin ventana y sin pintar, y vuelve en ~0,5 s:
    //   standby=1 (arranque)             todo iniciado sin abrir la captura ni enseñar la ventana -> switch2-standby-ready <ms>
    //   script-message switch2-standby   suelta la capturadora (el grafo de DirectShow entero) -> switch2-standby-ok <ms>;
    //                                    luego apaga la generacion, pone una imagen negra, esconde la ventana y deja de pintar
    //   script-message switch2-wake      abre la captura con los ajustes de ahora, enseña la ventana como al arrancar y vuelve
    //                                    a pintar (los avisos de imagen y el armado del apagado, como en un arranque)
    // Las ordenes se atienden en el hilo de pintado y en el orden en que llegan: un switch2-wake que llega mientras se entra en
    // espera se atiende justo despues (la capturadora se suelta, sale switch2-standby-ok y se despierta).
    unsafe partial class Engine
    {
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        struct StandbyCmd { public bool Wake; public bool Behind; public long At; }
        readonly object standbyLock = new object();
        readonly Queue<StandbyCmd> standbyCmds = new Queue<StandbyCmd>();
        volatile int standbyPending;        // ordenes en la cola (se mira en cada imagen sin el candado)
        volatile bool inStandby;            // sin captura, ventana oculta, sin pintar y sin avisos al lanzador
        long standbySince;                  // Clock.Now al quedar en espera
        long wakeAt;                        // Clock.Now del switch2-wake, hasta la primera imagen (0 = nada que medir)
        bool vramLogPending;                // en espera: la memoria al log cuando la fosa se haya vaciado
        object dxgiAdapter;                 // el adaptador de InitDevice (memoria de la grafica del proceso)
        // Al despertar, hasta que la ventana se ha enseñado de verdad: sin generacion (con la generacion puesta, tocar la ventana puede
        // bloquear a Streamline). Medido el 16-11 a las 02:25: el hilo de la ventana parado mas de 2 s (la tele paso a 60 Hz a la vez)
        volatile bool windowPending;
        long showAskedAt;

        // ---- Desde las tuberias (cualquier hilo): a la cola del hilo de pintado
        // behind (16-11, sin telon): switch2-wake detras (ver Wake)
        void AskStandby(bool wake, bool behind = false)
        {
            StandbyCmd c;
            c.Wake = wake;
            c.Behind = behind && wake;
            c.At = Clock.Now;
            lock (standbyLock) { standbyCmds.Enqueue(c); standbyPending = standbyCmds.Count; }
            frameEvent.Set();
        }

        bool NextStandbyCmd(out StandbyCmd c)
        {
            lock (standbyLock)
            {
                if (standbyCmds.Count == 0) { c = new StandbyCmd(); return false; }
                c = standbyCmds.Dequeue();
                standbyPending = standbyCmds.Count;
                return true;
            }
        }

        // ---- Hilo de pintado (desde Run, al principio de cada vuelta): ordenes pendientes y, en espera, el bucle de espera
        void StandbyStep(ref long lastShown, ref int fi)
        {
            StandbyCmd c;
            while (!inStandby && !Program.Quit && NextStandbyCmd(out c))
            {
                if (c.Wake) Log.Write("switch2-wake sin estar en espera: no se hace nada");
                else EnterStandby(c.At, ref fi);
            }
            if (inStandby && !Program.Quit) StandbyLoop(ref lastShown);
        }

        // Arranque standby=1: todo iniciado (Streamline, dispositivo, swapchain, shaders) sin captura, con la ventana sin enseñar y
        // sin pintar; la vigilancia de apagado en pausa. Aviso switch2-standby-ready <ms desde que arranco el proceso> (si aun no
        // hay nadie conectado, al primero que se conecte)
        void StandbyReady()
        {
            inStandby = true;
            if (power != null) power.Suspend();
            ReleaseSessionResources();      // (las de la salida, de InitFixedResources: hasta despertar)
            ResetSession(true);
            standbySince = Clock.Now;
            vramLogPending = true;
            string ms = Program.SinceStartMs().ToString("0", IC);
            Log.Write("En espera: listo en " + ms + " ms desde el arranque (sin captura, ventana sin enseñar, sin pintar; se espera switch2-wake)");
            if (mpvIpc != null) mpvIpc.EventOrKeep("switch2-standby-ready", ms);
        }

        // switch2-standby con el motor en marcha
        void EnterStandby(long askedAt, ref int fi)
        {
            Log.Write("En espera: switch2-standby recibido");
            // 16-11 (sin telon): encima de todo (switch2-encima, mientras Magpie preparaba DLSS 5): al fondo lo primero, para que la imagen
            // negra de mas abajo no se vea por encima de la de Magpie
            if (Program.OnTopMode > 0)
            {
                bool down = Program.SetOnTop(0);
                Log.Write("En espera: estaba encima de todo: " + (down ? "al fondo" : "NO se pudo mandar al fondo") + " antes de nada (" + Clock.SinceMs(askedAt).ToString("0", IC) + " ms)");
            }
            // Desde aqui nada de avisos de imagen, consola, ritmo ni apagado
            inStandby = true;
            if (power != null) power.Suspend();
            // 1. La capturadora fuera del todo: grafo de DirectShow parado y soltado (mpv la abre enseguida)
            bool released = StopSource();
            string rel = Clock.SinceMs(askedAt).ToString("0", IC);
            if (!released)
            {
                // El hilo de captura no termina (controlador colgado): solo cerrando el proceso se suelta seguro la capturadora
                Log.Write("En espera: la capturadora no se suelta (el hilo de captura sigue tras " + rel + " ms): se cierra el motor para soltarla (codigo 4, sin switch2-standby-ok)");
                ExitCode = 4;
                Program.CloseWindow();
                frameEvent.Set();
                return;
            }
            Log.Write("En espera: captura liberada en " + rel + " ms");
            Ev("switch2-standby-ok", rel);
            LogVram("En espera, antes de soltar la memoria de la sesion");
            // 2. Una imagen casi negra a la tele (al volver a enseñar la ventana no sale la ultima imagen de esta sesion); luego se esconde
            //    (1.2.4 ronda 6, QUITAFG: sin DLSS-G ya no hay que apagarla ni soltar sus recursos antes de esconder la ventana)
            long t = Clock.Now;
            try
            {
                ConfigureFeatures();            // (en espera)
                PresentBlank(ref fi);
                FreeDlssResources();            // (espera_soltar_dlss=1)
                WaitIdle();
                // 20-11 (CIERRES): las DLSSNR de DLSS 5 (1-3, con sus texturas) tampoco se quedan en espera: Magpie hace DLSS 5 en la
                // misma grafica de 8 GB (al despertar ya se creaban otra vez: inColor es otro)
                ReleaseDlss5("en espera");
                ReleaseDvc("en espera");   // 22-11 (1.2.4, DLSS5LEGAL: EngineDvc.cs)
                FmRelease("en espera");    // 23-11 (1.2.4 ronda 6, FGMULTI: FSR FG x2 / x3 / x4, ~0,4 GB a 4K; EngineFsrMulti.cs)
            }
            catch (Exception ex) { Log.Error("En espera: imagen negra", ex); }
            bool hidden = Program.HideWindow();
            Program.FocusHold = false;      // (16-11: el siguiente despertar dice si coge el foco)
            // 3. Lo que se rehace en unos ms al despertar
            ReleaseSessionResources();
            ResetSession(true);
            standbySince = Clock.Now;
            vramLogPending = true;
            Log.Write("En espera: ventana " + (hidden ? "oculta" : "SIN ocultar") + ", sin pintar y memoria de la sesion soltada a los " +
                      Clock.SinceMs(askedAt).ToString("0", IC) + " ms (" + Clock.SinceMs(t).ToString("0", IC) + " ms tras soltar la captura)");
        }

        // En espera hasta switch2-wake o quit: sin pintar ni captura. Los ajustes que llegan se guardan (sin abrir la captura)
        void StandbyLoop(ref long lastShown)
        {
            long lastSec = Clock.Now;
            while (inStandby && !Program.Quit)
            {
                frameEvent.WaitOne(100);
                try { ApplyPending(); } catch (Exception ex) { Log.Error("En espera: ajustes", ex); }
                if (Clock.SinceMs(lastSec) >= 1000)
                {
                    lastSec = Clock.Now;
                    Unbury(false);
                    if (vramLogPending && graveyard.Count == 0) { vramLogPending = false; LogVram("En espera"); }
                    int dr = DeviceRemovedCode();
                    if (dr < 0) { GpuLostInStandby(dr); return; }
                }
                StandbyCmd c;
                while (inStandby && !Program.Quit && NextStandbyCmd(out c))
                {
                    if (c.Wake) Wake(c.At, ref lastShown, c.Behind);
                    else
                    {
                        Log.Write("En espera: switch2-standby otra vez (ya lo esta): switch2-standby-ok 0");
                        Ev("switch2-standby-ok", "0");
                    }
                }
            }
        }

        // switch2-wake en espera: la sesion nueva con los ajustes de ahora, como un arranque (sin Streamline ni dispositivo)
        // behind (16-11, sin telon: "switch2-wake detras"): igual, pero la ventana se enseña sin activarse y al fondo de todo, y el motor no
        // coge el foco solo: el lanzador mantiene Magpie a la vista (y a mpv delante) hasta que el motor da imagen, y entonces quita Magpie
        // y pide switch2-delante. La generacion se pone como siempre al enseñarse, pero NVIDIA no genera hasta tener el foco
        void Wake(long askedAt, ref long lastShown, bool behind = false)
        {
            Log.Write("Despierto: switch2-wake" + (behind ? " detras" : "") + " recibido (en espera " + (Clock.SinceMs(standbySince) / 1000.0).ToString("0.0", IC) + " s)");
            if (mpvIpc != null) mpvIpc.DropKept();      // (un switch2-standby-ready sin entregar ya no vale)
            int dr = DeviceRemovedCode();
            if (dr < 0) { GpuLostInStandby(dr); return; }
            // Los ajustes que acaban de llegar (aun en espera: sin abrir la captura)
            try { ApplyPending(); } catch (Exception ex) { Log.Error("Despierto: ajustes", ex); }
            ResetSession(false);
            vramLogPending = false;
            long t = Clock.Now;
            try
            {
                // Lo de la sesion, como al arrancar: texturas de la salida y las del tamaño de la captura, memoria de subida
                CreateOutputTextures();
                EnsureInputResources(s.CapW, s.CapH, ((RowBytes(Shaders.FmtBgrx32, s.CapW) + 3) & ~3) * s.CapH);
            }
            catch (Exception ex)
            {
                Log.Error("Despierto: recursos de la sesion", ex);
                ExitCode = 1;
                Program.CloseWindow();
                frameEvent.Set();
                return;
            }
            double resMs = Clock.SinceMs(t);
            // El apagado, de cero como al arrancar (se arma tras 10 s de imagen buena: "Apagado: deteccion armada")
            if (power != null) power.Resume();
            wakeAt = askedAt;
            inStandby = false;
            StartSource();                              // (abre en su hilo: ~0,2 s hasta la primera imagen)
            windowPending = true;
            showAskedAt = Clock.Now;
            // pantalla completa, con el foco, topmost segun el arranque; la generacion, solo cuando ya esta a la vista (detras: al fondo y sin
            // el foco)
            bool shown = Program.ShowWindow(s, delegate
            {
                double ms = Clock.SinceMs(showAskedAt);
                if (windowPending && ms > 2000) Log.Write("Ventana: a la vista por fin a los " + ms.ToString("0", IC) + " ms (la generacion ya puede ponerse)");
                windowPending = false;
                frameEvent.Set();
            }, behind);
            lastShown = 0;                              // como al arrancar: negro hasta la primera imagen
            Log.Write("Despierto: captura pedida y ventana " + (shown ? (behind ? "enseñada detras de todo, sin el foco" : "a la vista") : "SIN enseñar") +
                      " a los " + Clock.SinceMs(askedAt).ToString("0", IC) + " ms (recursos de la sesion " + resMs.ToString("0", IC) + " ms): " + s.Describe());
        }

        // 22-11 (1.2.4 arreglos, RELEVO; lo vio DLSS5Q en el log del usuario 21:29:50): el lanzador hacia el corte del relevo (el motor nuevo
        // delante, el de antes fuera) con switch2-signal + switch2-rate, que cuentan imagenes que LLEGAN de la capturadora, no pintadas: el
        // motor nuevo aun no habia presentado nada (DLSS 5 creandose: "primera imagen en 1029 ms desde switch2-wake", 0,5 s despues del corte)
        // y se veia negro. Ahora, con la primera imagen de verdad presentada (en un arranque o al despertar): "switch2-pintada <n>", al
        // momento y luego cada 0,5 s durante 5 s (EventTick: por si aun no habia nadie conectado)
        long firstPaintAt;
        void PaintedFirst(long now)
        {
            firstPaintAt = now;
            Ev("switch2-pintada", "1");
        }
        // (1.2.4 ronda 5, INTEGRA5) y con la 4a: el lanzador corta con N presentadas (relevo_pintadas, de serie 4), por el paron de ~1,2 s
        // que venia tras la primera al despertar con DLSS 5
        void PaintedSteady(int n)
        {
            if (firstPaintAt != 0) Ev("switch2-pintada", n.ToString(CultureInfo.InvariantCulture));
        }

        // Desde Run, con la primera imagen nueva presentada tras despertar
        void WakeFirstImage(long arrival, long now)
        {
            Log.Write("Despierto: primera imagen en " + Clock.Ms(wakeAt, now).ToString("0", IC) + " ms desde switch2-wake (llego de la capturadora a los " +
                      Clock.Ms(wakeAt, arrival).ToString("0", IC) + " ms)");
            wakeAt = 0;
        }

        // ---- Piezas

        // La fuente fuera del todo. false = el hilo de captura no ha terminado (la capturadora puede seguir abierta)
        bool StopSource()
        {
            IFrameSource src;
            lock (sourceLock) { src = source; source = null; }
            if (src == null) return true;
            try { src.Frame -= OnFrame; } catch { }     // (ninguna imagen tardia a las ranuras que se van a soltar)
            try { src.Dispose(); } catch (Exception ex) { Log.Error("Cerrando la fuente", ex); }
            CaptureSource cs = src as CaptureSource;
            bool ok = cs == null || cs.Released;
            if (ok && cs != null)
            {
                // Red de seguridad (como DsCapture.Close, que ya hace GC.Collect): los finalizadores de lo que quede de COM, ya.
                // En otro hilo y como mucho 0,5 s (WaitForPendingFinalizers puede no volver nunca con un STA que no atiende)
                Thread f = new Thread(delegate () { try { GC.Collect(); GC.WaitForPendingFinalizers(); } catch { } });
                f.IsBackground = true;
                f.SetApartmentState(ApartmentState.MTA);
                f.Start();
                f.Join(500);
            }
            return ok;
        }

        // Una imagen casi negra a la tele (el fondo de "Enciende la consola": CAS con S.z = 1), con su ficha de Streamline,
        // Reflex y marcadores como cualquier imagen (es la que aplica el apagado de la generacion). Se usa antes de esconder la
        // ventana: al volver a enseñarla se ve esta y no la ultima imagen de la sesion anterior
        void PresentBlank(ref int fi)
        {
            frameIndex++;
            IntPtr token = IntPtr.Zero;
            if (slOn) Sl.GetNewFrameToken(frameIndex, out token);
            if (reflexOk && token != IntPtr.Zero) Sl.ReflexSleep(token);
            WaitFence(allocFence[fi]);
            if (pclOk && token != IntPtr.Zero) Sl.PCLSetMarker(SlPCLMarker.eSimulationStart, token);
            if (slOn && token != IntPtr.Zero)
            {
                SlConstants k = SlConstants.CreateVideo((uint)Math.Max(1, inW), (uint)Math.Max(1, inH), (uint)outW, (uint)outH, true);
                Sl.SetConstants(ref k, token, vp);
            }
            if (pclOk && token != IntPtr.Zero) { Sl.PCLSetMarker(SlPCLMarker.eSimulationEnd, token); Sl.PCLSetMarker(SlPCLMarker.eRenderSubmitStart, token); }

            uint* c = stackalloc uint[16];
            for (int i = 0; i < 16; i++) c[i] = 0;
            Hr.Check(alloc[fi].Reset(), "Allocator.Reset");
            Hr.Check(list.Reset(alloc[fi], null), "List.Reset");
            list.EndQuery(qheap, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * 2));
            // CAS en modo "negro" sobre 'final' (TCasIn: lee inColor, que esta en NON_PIXEL_SHADER_RESOURCE; con S.z = 1 ni lo lee)
            Barrier(final, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.UNORDERED_ACCESS);
            c[0] = (uint)outW; c[1] = (uint)outH; c[4] = F(0f); c[5] = F(1f); c[6] = F(1f);
            BindCompute(psoCas, TCasIn, c, 8);
            list.Dispatch(Groups(outW, 16), Groups(outH, 8), 1);
            Barrier(final, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, D3D12_RESOURCE_STATES.COPY_SOURCE);
            bool viaFm = scQueue != null;   // (1.2.4 ronda 6, FGMULTI: con la cola de presentacion de FSR FG la copia a la cadena va en ella, FmScBlank)
            if (!viaFm)
            {
                uint bi = ScIndex();
                ID3D12Resource bb = backbuf[bi];
                Barrier(bb, D3D12_RESOURCE_STATES.PRESENT, D3D12_RESOURCE_STATES.COPY_DEST);
                list.CopyResource(bb, final);
                Barrier(bb, D3D12_RESOURCE_STATES.COPY_DEST, D3D12_RESOURCE_STATES.PRESENT);
            }
            list.EndQuery(qheap, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * 2 + 1));
            list.ResolveQueryData(qheap, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(fi * 2), 2, tsReadback, (ulong)(fi * 16));
            Hr.Check(list.Close(), "Close");
            IntPtr lp = Com.BorrowPtr(list, typeof(ID3D12CommandList));
            queue.ExecuteCommandLists(1, ref lp);
            fenceValue++;
            queue.Signal(fence, fenceValue);
            allocFence[fi] = fenceValue;

            if (pclOk && token != IntPtr.Zero) { Sl.PCLSetMarker(SlPCLMarker.eRenderSubmitEnd, token); Sl.PCLSetMarker(SlPCLMarker.ePresentStart, token); }
            XeFgBlank();   // (1.2.4 MOTORFG, EngineFgXe.cs)
            int hr;
            if (!(viaFm && FmScBlank(final, s.Vsync ? 1u : 0u, out hr))) hr = ScPresent(s.Vsync ? 1u : 0u, 0);
            if (pclOk && token != IntPtr.Zero) Sl.PCLSetMarker(SlPCLMarker.ePresentEnd, token);
            if (hr < 0) Log.Write("En espera: Present de la imagen negra: " + Hr.Format(hr));
            fi = (fi + 1) % FramesInFlight;
        }

        // En espera: fuera lo que se rehace en unos ms al despertar. La memoria de subida de la captura (3 ranuras, en la RAM), las
        // texturas del tamaño de la captura (RGBA, bordes, profundidad, movimiento, piramides de luma, bloques) y las de la salida
        // (salida DLSS, final, con aviso). Lo que Streamline puede tener etiquetado va a la fosa (se suelta a los 3 s, como al cambiar
        // de tamaño). Se quedan Streamline con DLSS y la generacion (y sus recursos), el dispositivo, la swapchain, los shaders y el aviso
        void ReleaseSessionResources()
        {
            WaitIdle();
            lock (slotLock)
            {
                for (int i = 0; i < Slots; i++)
                {
                    if (upload[i] != null) { upload[i].Unmap(0, null); Com.Release(ref upload[i]); }
                    uploadPtr[i] = IntPtr.Zero;
                    slotState[i] = Free;
                    slotFence[i] = 0;
                    slotCut[i] = false;
                }
                slotBytes = 0;
                reallocating = false;
            }
            ReleaseCopySlots();   // (16-11: las copias de las ranuras en la memoria de la grafica, EngineCopia.cs)
            pendingSize = 0;
            Bury(rawColor); Bury(inColor); Bury(depth); Bury(mvec); Bury(rawSmall);
            rawColor = inColor = depth = mvec = rawSmall = null;
            for (int k = 0; k < Levels; k++)
            {
                for (int st = 0; st < 2; st++) { Bury(luma[st, k]); luma[st, k] = null; }
                Bury(mvLevel[k]); mvLevel[k] = null;
            }
            Bury(outColor); Bury(final); Bury(composed);
            outColor = final = composed = null;
            if (mvRead != null) { mvRead.Unmap(0, null); Com.Release(ref mvRead); mvReadSize = 0; mvReadFence = 0; }
            inW = inH = capW = capH = 0;          // (al despertar EnsureInputResources las vuelve a crear; DLSS sigue igual si no cambia el tamaño)
            motionValid = false;
            mvecDirty = false;
        }

        // Las texturas de la salida y sus descriptores, como en InitFixedResources (al despertar)
        void CreateOutputTextures()
        {
            outColor = Tex(ColorFmt, outW, outH, true, D3D12_RESOURCE_STATES.UNORDERED_ACCESS, "salida DLSS");
            final = Tex(ColorFmt, outW, outH, true, D3D12_RESOURCE_STATES.COPY_SOURCE, "final");
            composed = Tex(ColorFmt, outW, outH, true, D3D12_RESOURCE_STATES.COPY_SOURCE, "con aviso");
            SrvTex(TCasOut, 0, outColor, ColorFmt); UavTex(TCasOut, U0, final, ColorFmt);
            UavTex(TCasIn, U0, final, ColorFmt);
            UavTex(TOsd, U0, composed, ColorFmt);
            UavTex(TScale, U0, outColor, ColorFmt);
        }

        // Lo que en un arranque empieza de cero: avisos de imagen (y switch2-signal-ack: solo cuenta tras un switch2-signal de esta
        // sesion), contadores y tiempos (time-pos vuelve a empezar), repetidas, cortes de escena, el modelo rebajado y el fallo de DLSS.
        // entering = al entrar en espera: ademas fuera el aviso en pantalla de la sesion que se va.
        // No se tocan: una captura para el lanzador o una rafaga a medias (contestan solas: switch2-grabbed vacio a los 1,5 s, o
        // con las imagenes de la sesion nueva) ni un cambio de captura pedido (switch2-switched llega con la primera imagen de ese
        // tamaño, ya despierto)
        void ResetSession(bool entering)
        {
            signalSeen = false; signalAcked = false; signalSent = false; earlyAckLogged = false;
            firstArrival = 0; lastArrivalT = 0; framesIn = 0; capN = 0; lastPassed = -100; passedCount = 0; ratePassed = 0; lastPassedNew = true;
            lastPresentedArrival = 0; presentedFrames = 0; lastCapture = 0; noSignalLogged = false;
            firstPaintAt = 0;   // (1.2.4 arreglos, RELEVO: switch2-pintada de esta sesion)
            noSignalScreen = false;
            dupSig = null; dupSigLen = 0; cutValid = false;
            resetHistory = true; motionValid = false; mvecDirty = false;
            presetCap.Clear(); overSecs = 0; dlssBroken = false; evalFailsRow = 0;
            tickN = 0;
            stats.Restart();
            if (entering) osd.Hide();
        }

        int DeviceRemovedCode()
        {
            try { return dev != null ? dev.GetDeviceRemovedReason() : 0; } catch { return 0; }
        }

        void GpuLostInStandby(int dr)
        {
            Log.Write("En espera: la grafica se ha reiniciado (" + Hr.Format(dr) + "): se cierra el motor (codigo 3; el lanzador arrancara otro)");
            ExitCode = 3;
            Program.CloseWindow();
            frameEvent.Set();
        }

        static string Mb(ulong b) { return (b / (1024.0 * 1024.0)).ToString("0", IC); }

        // (18-11, pedido de PASADAS: en espera se soltaban los recursos de DLSS-G, ~600-675 MB, fg_retener=1 = se quedaban. 1.2.4 ronda 6,
        // QUITAFG: sin DLSS-G; fg_retener se sigue leyendo y no hace nada)

        // 18-11: espera_soltar_dlss=1: tambien DLSS SR (NGX: 450-620 MB a 1080p / 1440p) en espera; al despertar se vuelve a crear con la
        // primera imagen. De serie no (ver lo medido en el informe de LATENCIA: lo que tarda en despertar)
        void FreeDlssResources()
        {
            if (!s.StandbyFreeDlss || !dlssOk || dlssApplied.Length == 0) return;
            long t = Clock.Now;
            try
            {
                SlResult r = Sl.FreeResources(SlFeature.DLSS, vp);
                dlssApplied = "";       // (al despertar, DLSSSetOptions otra vez)
                Log.Write("En espera: recursos de DLSS soltados (" + r + ", " + Clock.SinceMs(t).ToString("0", IC) + " ms; espera_soltar_dlss=1)");
            }
            catch (Exception ex) { Log.Error("En espera: soltar DLSS", ex); }
        }

        // La memoria de la grafica de este proceso (DXGI) y la que dice DLSS (NGX), al log
        void LogVram(string when)
        {
            try
            {
                string t = "";
                IDXGIAdapter3 a3 = dxgiAdapter as IDXGIAdapter3;
                if (a3 != null)
                {
                    DXGI_QUERY_VIDEO_MEMORY_INFO loc, non;
                    if (a3.QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP.LOCAL, out loc) >= 0)
                        t += "grafica " + Mb(loc.CurrentUsage) + " MB de este proceso (presupuesto " + Mb(loc.Budget) + " MB)";
                    if (a3.QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP.NON_LOCAL, out non) >= 0)
                        t += (t.Length > 0 ? ", " : "") + "compartida (RAM) " + Mb(non.CurrentUsage) + " MB";
                }
                if (dlssOk)
                {
                    SlDLSSState ds;
                    if (Sl.DLSSGetState(vp, out ds) == SlResult.eOk) t += (t.Length > 0 ? " | " : "") + "DLSS (NGX) " + Mb(ds.estimatedVRAMUsageInBytes) + " MB";
                }
                Log.Write(when + ": memoria " + t);
            }
            catch (Exception ex) { Log.Write(when + ": memoria de la grafica: " + ex.Message); }
        }
    }
}
