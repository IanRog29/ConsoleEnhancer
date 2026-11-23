using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 20-11-2026 (CIERRES; el usuario: "Arregla que me ha cerrado alguna vez la app mia propia"). La grafica parada (o reiniciada) no es la
    // consola apagada:
    //  - 20-11 04:14: DLSS 5 con 3 pasadas al 75 % en el 30 + DLSS FG a 1440p (grafica p95 61 ms con tope 26), vuelta a 1 pasada y a los 9 s
    //    la GPU parada (Streamline: "waitCPUFence ... timed out after 500ms"). El pintado dejo de presentar y el lanzador, que vigila
    //    time-pos (la ultima imagen PRESENTADA), a los 1,5 s lo tomo por la consola apagada y cerro la app. 03:54: lo mismo con un reinicio
    //    de la grafica (nvlddmkm 153) a los 20 ms de pasar de 2 pasadas a 1 (EngineDlss5.cs: las DLSSNR se soltaban con la GPU usandolas).
    //  - Aqui: un vigilante en su propio hilo (cada 100 ms) mira si el pintado sigue dando vueltas (frameIndex), si con imagenes de la
    //    capturadora entrando se presentan (presentedFrames) y si la grafica sigue viva (GetDeviceRemovedReason). Al lanzador:
    //      switch2-gpu colgada <ms>  /  switch2-gpu ok <ms>  /  switch2-gpu perdida <motivo>
    //    y las propiedades switch2-captura-ms (ms desde la ultima imagen de la capturadora, -1 sin ninguna) y switch2-gpu (estado). La
    //    grafica perdida: el motor se cierra con el codigo 3 (el lanzador abre otro); parado mas de cuelgue_salir_s: codigo 5.
    //  - Pruebas sin colgar la grafica de verdad: prueba_cuelgue_ms=N prueba_cuelgue_s=T prueba_cuelgue_modo=pintado|todo|perdida (el hilo
    //    de pintado parado N ms a los T s de la primera imagen; todo = y sin imagenes de la fuente mientras; perdida = como si la grafica se
    //    hubiera perdido) y prueba_apagado_s=T prueba_apagado_modo=corta|nosignal (a los T s la fuente deja de mandar imagenes, como la
    //    consola apagada; nosignal = en su lugar la pantalla "No Signal" de la capturadora, para la vigilancia de apagado con power_test=1)
    partial class Settings
    {
        public int HangMs = 700;            // cuelgue_ms: el hilo de pintado sin dar una vuelta tanto (o 1200 ms sin presentar con imagenes llegando)
        public int HangExitS = 15;          // cuelgue_salir_s: parado tanto, el motor se cierra solo (codigo 5; 0 = nunca)
        public bool Dlss5Guard = true;      // dlss5_guardian=0: DLSS 5 no se aligera solo si la grafica no llega (EngineDlss5.cs)
        public int TestHangMs = 0, TestHangAt = 15;
        public string TestHangMode = "pintado";
        public int TestOffAt = 0;
        public string TestOffMode = "corta";

        bool ApplyCierres(string key, string val)
        {
            switch (key)
            {
                case "cuelgue_ms": HangMs = Math.Max(200, I(val)); return true;
                case "cuelgue_salir_s": HangExitS = Math.Max(0, I(val)); return true;
                case "dlss5_guardian": Dlss5Guard = B(val); return true;
                case "prueba_cuelgue_ms": TestHangMs = Math.Max(0, I(val)); return true;
                case "prueba_cuelgue_s": TestHangAt = Math.Max(1, I(val)); return true;
                case "prueba_cuelgue_modo": TestHangMode = val.Trim().ToLowerInvariant(); return true;
                case "prueba_apagado_s": TestOffAt = Math.Max(0, I(val)); return true;
                case "prueba_apagado_modo": TestOffMode = val.Trim().ToLowerInvariant(); return true;
            }
            return ApplyCaidas(key, val);   // (23-11, 6a ronda, CAIDAS: cola_tirar_seguidas, cola_sin_repintar; EngineCaidas.cs)
        }
    }

    unsafe partial class Engine
    {
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);

        const int GpuOk = 0, GpuStalled = 1, GpuGone = 2;
        volatile int gpuState;              // lo que ve el lanzador (switch2-gpu)
        long gpuStalledAt;                  // Clock.Now al verla parada
        volatile bool testLost;             // prueba_cuelgue_modo=perdida
        bool testHangDone, testOffDone;
        volatile bool testPaused;           // durante la prueba de cuelgue (el vigilante no la confunde con una de verdad: si la cuenta)
        long slTroubleAt;                   // ultimo aviso de Streamline de la GPU parada o perdida
        string slTroubleText = "";

        // Desde Program.Main, antes de Run (espera sola a que haya dispositivo)
        public void StartGpuWatch()
        {
            Thread t = new Thread(GpuWatchLoop);
            t.IsBackground = true;
            t.Name = "vigilante de la grafica";
            t.Start();
        }

        // Para OnMpvCommand (get_property): null = no es nuestra
        string CierresProperty(string prop)
        {
            if (prop == "switch2-captura-ms")
            {
                long lc = lastCapture;
                if (lc == 0 || inStandby) return "-1";
                return ((long)Clock.SinceMs(lc)).ToString(CultureInfo.InvariantCulture);
            }
            if (prop == "switch2-gpu") return MpvIpc.Quote(gpuState == GpuGone ? "perdida" : gpuState == GpuStalled ? "colgada" : "ok");
            return null;
        }

        void GpuWatchLoop()
        {
            try
            {
                while (!Program.Quit && (dev == null || fence == null)) Thread.Sleep(100);
                // Los avisos de Streamline de la GPU parada o perdida (los de siempre al log, como antes)
                Action<string> prev = Sl.LogSink;
                Sl.LogSink = delegate (string m)
                {
                    if (prev != null) prev(m);
                    if (m != null && (m.Contains("timed out") || m.Contains("0x887A0005") || m.Contains("0x887A0006") || m.Contains("DEVICE_REMOVED")))
                    { slTroubleAt = Clock.Now; slTroubleText = m.Length > 160 ? m.Substring(m.Length - 160) : m; }
                };
                uint lastFi = frameIndex;
                long fiAt = Clock.Now, prAt = Clock.Now;
                long lastPr = presentedFrames;
                long lastPc = passedCount, pcAt = Clock.Now;
                long lastDr = 0;
                while (!Program.Quit)
                {
                    Thread.Sleep(100);
                    long now = Clock.Now;
                    uint fi = frameIndex;
                    if (fi != lastFi) { lastFi = fi; fiAt = now; }
                    long pr = presentedFrames;
                    if (pr != lastPr) { lastPr = pr; prAt = now; }
                    long pc = passedCount;
                    if (pc != lastPc) { lastPc = pc; pcAt = now; }
                    // La grafica perdida (TDR, quitada): cada 0,5 s
                    if (Clock.Ms(lastDr, now) >= 500)
                    {
                        lastDr = now;
                        int dr = testLost ? Hr.DXGI_ERROR_DEVICE_HUNG : DeviceRemovedCode();
                        if (dr < 0) { GpuGoneExit(dr); return; }
                    }
                    TestOffStep();
                    // Sin pintar a proposito (en espera, entrando o saliendo de ella, sin ventana aun, sin imagen aun): nada que mirar
                    if (inStandby || standbyPending > 0 || windowPending || pr == 0)
                    {
                        fiAt = prAt = now;
                        if (gpuState == GpuStalled) SetGpuState(GpuOk, "sin pintar a proposito");
                        continue;
                    }
                    double loopMs = Clock.Ms(fiAt, now), presMs = Clock.Ms(prAt, now);
                    bool passing = Clock.Ms(pcAt, now) < 300;   // imagenes de la capturadora pasando al pintado
                    bool stalled = loopMs >= s.HangMs || (passing && presMs >= Math.Max(1200, s.HangMs));
                    if (stalled && gpuState == GpuOk)
                    {
                        gpuStalledAt = now - (long)(Math.Max(loopMs, passing ? presMs : 0) / Clock.TickMs);
                        double ms = Math.Max(loopMs, presMs);
                        ulong done = 0;
                        try { done = fence.GetCompletedValue(); } catch { }
                        long lc = lastCapture;
                        string cap = lc == 0 ? "sin imagenes" : Clock.SinceMs(lc) < 300 ? "siguen llegando" : "no llegan desde hace " + Clock.SinceMs(lc).ToString("0", IC) + " ms";
                        string sl = slTroubleAt != 0 && Clock.SinceMs(slTroubleAt) < 5000 ? "; Streamline: ..." + slTroubleText : "";
                        SetGpuState(GpuStalled, string.Format(IC, "el pintado lleva {0:0} ms parado (vuelta {1:0} ms, ultima imagen presentada hace {2:0} ms; imagenes de la " +
                            "capturadora: {3}; valla de la GPU {4} de {5}{6}{7}): no es la consola", ms, loopMs, presMs, cap, done, fenceValue, testPaused ? "; PRUEBA prueba_cuelgue" : "", sl));
                    }
                    else if (!stalled && gpuState == GpuStalled)
                        SetGpuState(GpuOk, "el pintado vuelve tras " + Clock.SinceMs(gpuStalledAt).ToString("0", IC) + " ms parado");
                    else if (stalled && s.HangExitS > 0 && Clock.SinceMs(gpuStalledAt) >= s.HangExitS * 1000.0)
                    {
                        Log.Write("Grafica: el pintado sigue parado tras " + s.HangExitS + " s: el motor se cierra (codigo 5; el lanzador abre otro)");
                        Ev("switch2-gpu", "salida", (Clock.SinceMs(gpuStalledAt)).ToString("0", IC));
                        Thread.Sleep(200);
                        Log.Write("Fin (5, el vigilante de la grafica)");
                        TerminateProcess(GetCurrentProcess(), 5);
                        return;
                    }
                }
            }
            catch (Exception ex) { Log.Error("Vigilante de la grafica", ex); }
        }

        void SetGpuState(int st, string detail)
        {
            gpuState = st;
            Log.Write("Grafica: " + detail);
            if (st == GpuStalled) Ev("switch2-gpu", "colgada", Clock.SinceMs(gpuStalledAt).ToString("0", IC));
            else if (st == GpuOk) Ev("switch2-gpu", "ok", Clock.SinceMs(gpuStalledAt).ToString("0", IC));
        }

        // La grafica perdida (TDR o quitada): se dice al lanzador y el motor se cierra con el codigo 3 (como en espera); si el hilo de pintado
        // no sale (bloqueado en el driver), a los 4 s se termina el proceso con ese codigo
        void GpuGoneExit(int dr)
        {
            gpuState = GpuGone;
            Log.Write("Grafica perdida (GetDeviceRemovedReason = " + Hr.Format(dr) + (testLost ? ", PRUEBA prueba_cuelgue_modo=perdida" : "") +
                      "): el motor se cierra con el codigo 3 para que el lanzador abra otro (no es la consola apagada)");
            Ev("switch2-gpu", "perdida", Hr.Format(dr));
            ExitCode = 3;
            Program.CloseWindow();
            frameEvent.Set();
            for (int i = 0; i < 40; i++) Thread.Sleep(100);
            Log.Write("Grafica perdida: el motor no ha salido en 4 s (bloqueado en el driver): se termina el proceso (codigo 3)");
            TerminateProcess(GetCurrentProcess(), 3);
        }

        // ---- Pruebas (solo con prueba_cuelgue_ms / prueba_apagado_s)

        // Desde el hilo de pintado (RecordDlss5, en cada imagen): el pintado parado prueba_cuelgue_ms a los prueba_cuelgue_s de la primera imagen
        void TestHangStep()
        {
            if (testHangDone || (s.TestHangMs <= 0 && s.TestHangMode != "perdida") || firstArrival == 0) return;
            if (Clock.SinceMs(firstArrival) < s.TestHangAt * 1000.0) return;
            testHangDone = true;
            if (s.TestHangMode == "perdida")
            {
                // (1.2.4 arreglos, RELEVO: con prueba_cuelgue_ms, antes el pintado parado ese tiempo -como el DEVICE_HUNG del usuario: ~2 s
                // de imagen quieta y luego la grafica perdida-, para medir el negro del relevo tras una caida)
                if (s.TestHangMs > 0)
                {
                    Log.Write("Prueba: el hilo de pintado se para " + s.TestHangMs + " ms y luego la grafica se da por perdida (prueba_cuelgue_modo=perdida)");
                    testPaused = true;
                    Thread.Sleep(s.TestHangMs);
                }
                Log.Write("Prueba: la grafica se da por perdida (prueba_cuelgue_modo=perdida)");
                testLost = true;
                return;
            }
            bool all = s.TestHangMode == "todo";
            Log.Write("Prueba: el hilo de pintado se para " + s.TestHangMs + " ms (prueba_cuelgue" + (all ? ", y sin imagenes de la fuente mientras" : ", la fuente sigue") + ")");
            IFrameSource src = null;
            if (all) lock (sourceLock) { src = source; if (src != null) src.Frame -= OnFrame; }
            testPaused = true;
            Thread.Sleep(s.TestHangMs);
            testPaused = false;
            if (src != null) lock (sourceLock) { if (source == src) src.Frame += OnFrame; }
            Log.Write("Prueba: el hilo de pintado sigue (prueba_cuelgue)");
        }

        // Desde el vigilante: a los prueba_apagado_s de la primera imagen la fuente deja de mandar (la consola apagada) o manda la pantalla
        // "No Signal" de la capturadora (nosignal)
        void TestOffStep()
        {
            if (testOffDone || s.TestOffAt <= 0 || firstArrival == 0 || inStandby) return;
            if (Clock.SinceMs(firstArrival) < s.TestOffAt * 1000.0) return;
            testOffDone = true;
            lock (sourceLock) { if (source != null) source.Frame -= OnFrame; }
            bool ns = s.TestOffMode == "nosignal";
            Log.Write("Prueba: la fuente deja de mandar imagenes (prueba_apagado, como la consola apagada)" + (ns ? "; en su lugar la pantalla No Signal de la capturadora" : ""));
            if (!ns) return;
            Thread t = new Thread(NoSignalFeeder);
            t.IsBackground = true;
            t.Name = "prueba No Signal";
            t.Start();
        }

        // La pantalla "No Signal" de la GC573 (franjas negras arriba y abajo, recuadro gris en medio; PowerWatch.IsNoSignal) a 59,94
        void NoSignalFeeder()
        {
            try
            {
                int w = s.CapW, h = s.CapH, pitch = w * 4;
                byte[] img = new byte[pitch * h];
                for (int y = 0; y < h; y++)
                {
                    double fy = (y + 0.5) / h;
                    for (int x = 0; x < w; x++)
                    {
                        double fx = (x + 0.5) / w;
                        byte v = (byte)(fy > 0.30 && fy < 0.66 && fx > 0.06 && fx < 0.94 ? 170 : 8);
                        int o = y * pitch + x * 4;
                        img[o] = v; img[o + 1] = v; img[o + 2] = v; img[o + 3] = 255;
                    }
                }
                long seq = 1000000;
                long next = Clock.Now;
                double period = 1000.0 * 1001 / 60000;
                fixed (byte* p = img)
                    while (!Program.Quit)
                    {
                        if (!inStandby) OnFrame((IntPtr)p, pitch, w, h, Shaders.FmtBgrx32, Clock.Now, seq++);
                        next += (long)(period / Clock.TickMs);
                        double wait = Clock.Ms(Clock.Now, next);
                        if (wait > 1) Thread.Sleep((int)wait);
                    }
            }
            catch (Exception ex) { Log.Error("Prueba No Signal", ex); }
        }
    }
}
