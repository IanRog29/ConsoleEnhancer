using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 18-11 (LATENCIA): ritmo y latencia de los modos 30x2 (modo 30 con Smooth Motion del driver: el motor presenta las 29,97 reales y
    // el driver mete una inventada entre cada dos, con la sincronizacion "cada 2 refrescos" del perfil de NVIDIA de Switch2Engine.exe).
    // - Pass30: el filtro de repetidas del modo 30 que no pierde imagenes reales con ritmo irregular (el "P2" de G1 en mpv, 16-11).
    // - Aviso en pantalla: sin repintar la ultima imagen entre dos reales (Engine.Run, aviso_repinta_ms).
    // - Medida: lat_csv= (una linea por presentacion: llegada, cogida, envio, vuelta de Present, cola de DXGI), sonda= (TestSource.cs: la
    //   banda de scripts\latencia.exe) y lo que va a la linea del banco (LatBench).
    // - Pruebas: reflex_dormir=0, cola_max=N (swapchain con espera), test_paron=ms, cola_tirar_ms=N.
    // Medido el 18-11 (motor solo, fuente de prueba 1080p a 59,94 con juego a 30, L, tele 3840x2160 a 120,000 Hz, Smooth Motion cada 2
    // refrescos en el perfil; PresentMon --qpc_time + latencia.exe con Desktop Duplication; ~25 s desde el arranque), de la llegada de
    // la imagen a verse la real (la inventada, que ya la lleva a medias, sale 16,7 ms antes):
    //   antes: 41-52 ms de media (p95 55-58; al arrancar 52-68 y baja 1 ms/s), tras un aviso 82-99 ms (y ~1 min en bajar), tras un
    //          paron de 150 ms 45 ms; con un juego de ritmo irregular (test_cadencia=par) 103 de 1074 imagenes perdidas
    //   ahora: 35 ms (p95 39), igual tras un aviso o un paron (1-2 imagenes tiradas al arrancar / tras el paron); par: 29 perdidas
    // Sin efecto (descartado): reflex_dormir=0 (Reflex no duerme nada aqui: 0,00 ms), cola_max=1 (la espera de DXGI no ve la cola de
    // Smooth Motion: 0 ms de espera, misma latencia); las estadisticas de DXGI no estan (E_NOTIMPL en la swapchain de Streamline)
    unsafe partial class Engine
    {
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObjectEx(IntPtr h, uint ms, bool alertable);

        long latPickup, latLastReal, latFirstReal;
        double latSleepMs, latQueueWaitMs;
        IntPtr latWaitable;
        bool latStallDone, latFsSaid;
        int latDrops;
        // filtro del modo 30
        bool dup30PrevNew = true, dup30Forced;
        // registro por imagen
        StringBuilder latBuf;
        bool latCsvStarted;
        // presentaciones (contador de DXGI -> hora de envio) para ver cuando se ven
        readonly long[] latSubmitByPc = new long[128];
        readonly uint[] latPcRing = new uint[128];
        uint latLastFsPc;
        // banco
        readonly List<double> latBPick = new List<double>(), latBSleep = new List<double>(), latBCall = new List<double>(), latBDisp = new List<double>(),
                              latBQueue = new List<double>(), latBWait = new List<double>(), latBArrDisp = new List<double>();
        readonly long[] latArrivalByPc = new long[128];
        // cola_tirar_ms: lo que tarda en verse cada presentacion (de DXGI): minimo del ultimo segundo frente al minimo de 10 s
        readonly Queue<KeyValuePair<long, double>> latDispWin = new Queue<KeyValuePair<long, double>>();
        long latDropAt;
        bool latDropWanted;

        // ---- filtro de repetidas del modo 30 (repetidas_30=2). Imagen nueva (isNew) y capturas desde la ultima que paso (since).
        // Como el de antes (una nueva si han pasado 2, o cualquiera a las 3) mas: (1) con 2 pasa tambien la copia de una nueva que llego
        // con 1 (sale 1 refresco tarde y no se pierde), (2) tras una copia forzada (imagen quieta) la nueva pasa en cuanto llega.
        // Banco de G1 (16-11, cadencia.exe por mpv): pares 3,1 / 1,3 31 -> 11 perdidas de 300 (= el optimo), ritmo 1,3,1,3 50 -> 0 de 200,
        // 20 fps con tirones 67 imagenes 33 ms tarde -> 0; con 30 / 20 regulares y 60 igual que antes
        // En el motor, sin (2) (repetidas_30=3 la pone): la nueva justo tras una copia forzada saldria 16,7 ms despues de ella y con Smooth
        // Motion esperaria en la cola de la tele (ver QueueModel) ~16 s; asi pasa su copia (con 2, por (1)) y entre dos presentaciones hay
        // siempre 2 capturas (33,4 ms). Sin Smooth Motion solo cambia el arranque del movimiento tras una imagen quieta (1 refresco)
        // 22-11 (1.2.3 RITMO2): tenia un fallo de latencia (el mismo que el filtro de mpv, Switch2Magpie.cs FrameFilter): en cuanto pasa una
        // copia tarde, cada imagen nueva llega 1 captura despues de la ultima que paso y tampoco puede pasar: pasa su copia, y asi SIEMPRE
        // (todas 16,7 ms tarde) hasta una imagen de 3 refrescos. Pasa con un juego a 30 regular si el motor empieza en la segunda copia (la
        // mitad de las veces) y tras cada par 3,1. Ahora, tras pasar una copia tarde, la nueva siguiente pasa en cuanto llega (since 1: una
        // pareja a 16,7 ms y se vuelve a tiempo), como mucho una vez cada 40 capturas (para no pasar de 30 con un juego a 45). Con Smooth
        // Motion no (salvo repetidas_30=3): esa pareja esperaria en la cola de la tele, como con la regla (2). Medido sin grafica con esta misma
        // logica y la llamada de Engine.cs (motor_filtro.exe, 1200 imagenes por cadencia): 30 regular empezando en la copia 100 % de las
        // imagenes 1 refresco tarde -> 0 %; pares 3,1 cada 20 imagenes 93-95 % -> 48 %; una de 3 refrescos cada 60 empezando en la copia
        // 5 % -> 0 %; 1,3,1,3 igual (50 %, a 2 capturas: se sigue igualando); a 45 fps 30,0 -> 30,6 pasadas/s (perdidas 278 -> 258); imagen
        // quieta igual; con Smooth Motion igual que antes
        bool dup30LateCopy;            // la ultima que paso era la copia de una nueva que llego con 1 (paso 1 refresco tarde)
        long dup30Recovery = -1000;    // capN de la ultima vuelta a tiempo
        bool Pass30(bool isNew, long since)
        {
            bool first = lastPassed < 0;
            if (dup30Recovery > capN) dup30Recovery = -1000;   // (capN vuelve a 0 al despertar de la espera)
            bool back = isNew && dup30LateCopy && since == 1 && capN - dup30Recovery > 40 && (s.SmoothMode == 0 || s.Dedupe30 >= 3);
            bool pass = first || (since >= 2 && (isNew || dup30PrevNew)) || since >= 3 || (isNew && dup30Forced && s.Dedupe30 >= 3) || back;
            if (pass)
            {
                dup30Forced = !isNew && !dup30PrevNew && !first;
                dup30LateCopy = !isNew && dup30PrevNew;
                if (back) dup30Recovery = capN;
            }
            dup30PrevNew = isNew;
            return pass;
        }

        // ---- cola_max=N: la swapchain con espera (InitDevice, tras crearla)
        void LatQueueInit()
        {
            if (queueMax <= 0) return;   // (18-11, MOTOR-CE: queueMax = cola_max, o 1 sin Reflex con cola_max=-1 de serie)
            try
            {
                int hr = swap.SetMaximumFrameLatency((uint)queueMax);
                latWaitable = swap.GetFrameLatencyWaitableObject();
                Log.Write("Swapchain con espera: como mucho " + queueMax + " en cola" + (s.QueueMax < 0 ? " (sin Reflex)" : "") + " (" + (hr >= 0 ? "bien" : Hr.Format(hr)) + (latWaitable == IntPtr.Zero ? ", sin objeto de espera" : "") + ")");
            }
            catch (Exception ex) { Log.Error("Swapchain con espera", ex); latWaitable = IntPtr.Zero; }
        }

        // Bucle de pintado, antes de coger la imagen: hasta que la cola tiene sitio (como mucho 100 ms)
        // 21-11 (1.2.1, cola_cuenta=1): una espera por cada Present. El objeto de espera de DXGI es un semaforo: cada presentacion que sale
        // de la cola devuelve un sitio. Antes (1) una vuelta que esperaba y no presentaba (la captura cambia de tamaño, cola_tirar_ms,
        // presentar=0) dejaba a la siguiente sin sitio: 100 ms parada; y (2) las presentaciones hechas fuera de este bucle (la imagen negra
        // al entrar en espera, EngineStandby.cs) devolvian sitios que nadie habia cogido: al despertar la espera ya no frenaba y la cola de
        // la tele crecia hasta 3. Medido el 21-11 (streamline=0, fuente de prueba 1080p a 180/s, pantalla de 120 Hz): en cola 1,3 de media
        // antes de la espera y 3,0 para siempre tras switch2-standby + switch2-wake (+2 refrescos de retraso, y la grafica ~5 ms mas por
        // imagen esperando a la cadena). Ahora el sitio cogido se guarda hasta su Present (latHeld) y los sitios de mas se recogen sin
        // esperar en cuanto vuelven (latOwed). Solo con la cadena con espera (sin Reflex: AMD, Intel, streamline=0). 0 = como antes
        bool latHeld;          // un sitio de la cola cogido y aun sin su Present
        int latOwed;           // presentaciones hechas sin sitio cogido (fuera del bucle): sitios de mas que la cola devolvera
        uint latPcMine;        // GetLastPresentCount tras el ultimo Present del bucle (0 = aun ninguno)
        int latTimeouts, latAbsorbed;
        void LatQueueWait()
        {
            latQueueWaitMs = 0;
            if (latWaitable == IntPtr.Zero) return;
            if (!s.QueueCount)
            {
                long t0 = Clock.Now;
                WaitForSingleObjectEx(latWaitable, 100, true);
                latQueueWaitMs = Clock.SinceMs(t0);
                return;
            }
            uint pc;
            if (latPcMine != 0 && swap.GetLastPresentCount(out pc) >= 0 && pc != latPcMine)
            {
                // (con una generacion de imagenes, las que presenta ella no son de fuera: no se cuentan)
                int extra = (int)(pc - latPcMine);
                if (extra > 0 && extra < 64 && !fgApplied) latOwed = Math.Min(8, latOwed + extra);
                latPcMine = pc;
            }
            if (!latHeld)
            {
                long t = Clock.Now;
                uint r = WaitForSingleObjectEx(latWaitable, 100, true);
                latQueueWaitMs = Clock.SinceMs(t);
                latHeld = r == 0;   // WAIT_OBJECT_0
                if (!latHeld && ++latTimeouts <= 5)
                    Log.Write("Swapchain con espera: sin sitio en la cola en " + latQueueWaitMs.ToString("0", CultureInfo.InvariantCulture) + " ms (se presenta igual)" + (latTimeouts == 5 ? " (no se apunta mas)" : ""));
            }
            // Los sitios de mas que ya han vuelto, fuera sin esperar (con un sitio ya cogido solo queda otro libre si sobra)
            int absorbed = 0;
            while (latOwed > 0 && WaitForSingleObjectEx(latWaitable, 0, false) == 0) { latOwed--; absorbed++; }
            if (absorbed > 0)
            {
                latAbsorbed += absorbed;
                if (latAbsorbed <= 20) Log.Write("Swapchain con espera: " + absorbed + " sitio(s) de mas en la cola recogido(s) (presentaciones sin espera: la imagen negra de la espera)");
            }
        }

        // Tras cada Present del bucle de pintado: el sitio se gasta si la presentacion entro en la cola (si no, p. ej. un Present que falla,
        // se guarda para la siguiente; 3 seguidas sin que cambie la cuenta de DXGI = no se fia de ella y se gasta igual, como antes)
        int latPcSame;
        void LatQueuePresent()
        {
            if (latWaitable == IntPtr.Zero || !s.QueueCount) return;
            uint pc;
            if (swap.GetLastPresentCount(out pc) >= 0)
            {
                if (pc != latPcMine || ++latPcSame >= 3) { latHeld = false; latPcSame = 0; }
                latPcMine = pc;
            }
            else latHeld = false;
        }

        // ---- 21-11 (1.2.1) mmcss=1 (de serie 0, prueba): los dos hilos por los que pasa cada imagen, en MMCSS (el planificador multimedia de
        // Windows: por encima de los hilos normales de cualquier proceso, con el 20 % de cada nucleo reservado para lo demas). El que entrega
        // la captura (OnFrame: el hilo del controlador de DirectShow, o el de la fuente de prueba) como "Capture" y el de pintado (Run) como
        // "Games". Medido el 21-11 (motor en prioridad alta como lo deja el lanzador, fuente de prueba 4K YUY2 a 59,94, Ryzen 5 2600) con 12
        // hilos de carga en prioridad normal, en alta y en alta a rafagas (2 ms si, 1 ms no): sin diferencia (de la llegada al fin de la
        // grafica 6,5 ms, p99 7,0, copia p99 2,6-2,9 con y sin MMCSS): al despertar, Windows ya les sube la prioridad a los dos hilos. Se
        // queda apagado; con o sin el, el log dice una vez la prioridad del hilo que entrega la captura (el de DirectShow es del controlador)
        [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr AvSetMmThreadCharacteristicsW(string task, ref uint index);
        [DllImport("avrt.dll")] static extern bool AvRevertMmThreadCharacteristics(IntPtr h);
        [DllImport("kernel32.dll")] static extern int GetThreadPriority(IntPtr thread);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
        [ThreadStatic] static bool mmcssCaptureSeen;
        static int mmcssCaptureThreads;

        IntPtr MmcssOn(string task, string who, bool log)
        {
            if (s.Mmcss <= 0) return IntPtr.Zero;
            try
            {
                int before = GetThreadPriority(GetCurrentThread());
                uint idx = 0;
                IntPtr h = AvSetMmThreadCharacteristicsW(task, ref idx);
                int err = h == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
                if (log) Log.Write("MMCSS: hilo " + who + " (prioridad relativa antes " + before + ") en \"" + task + "\"" + (h != IntPtr.Zero ? "" : ": NO (error " + err + ")"));
                return h;
            }
            catch (Exception ex) { if (log) Log.Write("MMCSS (" + who + "): " + ex.Message); return IntPtr.Zero; }
        }

        static void MmcssOff(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            try { AvRevertMmThreadCharacteristics(h); } catch { }
        }

        // OnFrame, la primera vez en cada hilo que entrega imagenes (el de DirectShow es otro al volver a abrir la captura). No se revierte:
        // el hilo es del controlador y MMCSS lo suelta cuando termina
        void MmcssCaptureThread()
        {
            if (mmcssCaptureSeen) return;
            mmcssCaptureSeen = true;
            bool log = Interlocked.Increment(ref mmcssCaptureThreads) <= 10;
            if (s.Mmcss > 0) { MmcssOn("Capture", "de la captura", log); return; }
            if (!log) return;
            int p = 0;
            try { p = GetThreadPriority(GetCurrentThread()); } catch { }
            Log.Write("Captura: el hilo que entrega las imagenes va a prioridad relativa " + p + " (sin MMCSS: mmcss=0)");
        }

        // Antes de pintar una imagen real: true = no se pinta (se suelta la ranura). test_paron (una vez) y cola_tirar_ms
        bool LatSkip(int slot)
        {
            bool skip = false;
            if (s.TestStallMs > 0 && !latStallDone && latFirstReal != 0 && Clock.SinceMs(latFirstReal) > s.TestStallAt * 1000.0)
            {
                latStallDone = true;
                Log.Write("Prueba: paron de " + s.TestStallMs + " ms del hilo de pintado (test_paron)");
                Thread.Sleep(s.TestStallMs);
                skip = true;
            }
            else if (QueueModelSkip(Clock.Now) || FgQueueSkip(slot))   // (22-11, 1.2.4 LATENCIA: cola_tirar_fg, EngineEnvio.cs)
            {
                latDrops++;
                skip = true;
            }
            if (!skip) return false;
            lock (slotLock) { slotState[slot] = Free; slotFence[slot] = 0; }
            return true;
        }

        // Tras cada Present (slot < 0 = repintado de la ultima)
        void LatPresented(int slot, long arrival, long submit, long ret, double gpuMs)
        {
            bool real = slot >= 0;
            if (real) { latLastReal = ret; if (latFirstReal == 0) latFirstReal = ret; }
            if (real && latPickup != 0) { double d = Clock.Ms(latPickup, submit); if (d > 0 && d < 50) latPickSubEma = latPickSubEma <= 0 ? d : latPickSubEma * 0.9 + d * 0.1; }
            if (real && latPickup != 0) LatReflexWatch(Clock.Ms(arrival, latPickup));   // 21-11 (1.2.1)
            QueueModelPresented(submit);
            FgQueueGpu(real, gpuMs);   // 22-11 (1.2.4 LATENCIA, EngineEnvio.cs)
            if (gpuMs > 0 && gpuMs < 100) qgDurMs = qgDurMs < 0 ? gpuMs : qgDurMs * 0.9 + gpuMs * 0.1;   // 23-11 (ronda 6, SMOOTH: cola_grafica)
            if (stats.Recording && real && s.SmoothMode > 0) latBQX.Add(latQX);
            bool csv = s.LatCsv.Length > 0;
            bool want = csv || stats.Recording;     // (jugando, nada mas: DXGI no da estadisticas con Streamline)
            if (!want) return;
            uint pc = 0; DXGI_FRAME_STATISTICS fs = new DXGI_FRAME_STATISTICS();
            int hrFs = -1;
            try
            {
                if (swap.GetLastPresentCount(out pc) >= 0)
                {
                    latSubmitByPc[pc & 127] = submit; latPcRing[pc & 127] = pc;
                    latArrivalByPc[pc & 127] = real ? arrival : 0;
                }
                hrFs = swap.GetFrameStatistics(out fs);
                if (hrFs < 0 && !latFsSaid) { latFsSaid = true; Log.Write("DXGI: sin estadisticas de presentacion (" + Hr.Format(hrFs) + ")"); }
            }
            catch { }
            double dispMs = -1, arrDispMs = -1;
            int queued = -1;
            if (hrFs >= 0 && fs.PresentCount != 0)
            {
                queued = (int)(pc - fs.PresentCount);
                if (fs.PresentCount != latLastFsPc && latPcRing[fs.PresentCount & 127] == fs.PresentCount)
                {
                    latLastFsPc = fs.PresentCount;
                    dispMs = Clock.Ms(latSubmitByPc[fs.PresentCount & 127], fs.SyncQPCTime);
                    long a = latArrivalByPc[fs.PresentCount & 127];
                    if (a != 0) arrDispMs = Clock.Ms(a, fs.SyncQPCTime);
                    if (dispMs > 0 && dispMs < 1000) LatDropCheck(ret, dispMs);
                }
            }
            if (stats.Recording && real)
            {
                latBPick.Add(Clock.Ms(arrival, latPickup)); latBSleep.Add(latSleepMs); latBCall.Add(Clock.Ms(submit, ret));
                latBWait.Add(latQueueWaitMs);
                if (dispMs > 0 && dispMs < 1000) latBDisp.Add(dispMs);
                if (arrDispMs > 0 && arrDispMs < 1000) latBArrDisp.Add(arrDispMs);
                if (queued >= 0 && queued < 64) latBQueue.Add(queued);
            }
            if (!csv) return;
            if (latBuf == null) latBuf = new StringBuilder(1 << 16);
            latBuf.Append(real ? 'r' : 'p').Append(';').Append(real ? slotSeq[slot] : -1).Append(';').Append(arrival).Append(';').Append(real ? latPickup : 0).Append(';')
                  .Append(latSleepMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(';').Append(latQueueWaitMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(';')
                  .Append(submit).Append(';').Append(ret).Append(';').Append(gpuMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(';')
                  .Append(pc).Append(';').Append(hrFs >= 0 ? fs.PresentCount : 0).Append(';').Append(hrFs >= 0 ? fs.SyncQPCTime : 0).Append(';')
                  .Append(hrFs >= 0 ? fs.PresentRefreshCount : 0).Append(';').Append(hrFs >= 0 ? fs.SyncRefreshCount : 0).Append(';').Append(queued)
                  .Append(';').Append(latQX.ToString("0.00", CultureInfo.InvariantCulture)).Append("\r\n");
        }

        void LatDropCheck(long now, double dispMs) { }

        // 21-11 (1.2.1): jugando (no solo en el banco), si Reflex duerme mucho y la imagen de la captura, ya llegada, espera a que acabe: es su
        // tope de imagenes con G-SYNC por debajo del ritmo de la captura (p. ej. el 60 + DLSS FG con VRR en una pantalla de 119,88 Hz, donde
        // no va el refresco fijo de motor_60fg_refresco_fijo=auto). Medido el 21-11 con la fuente de prueba a 59,94 (1440p, DLSS K, VRR a
        // 120 Hz, sin el refresco fijo): Reflex 14,0 ms por imagen, la imagen esperando 7,5 ms de media (p95 16,2) y 58,3 reales de 59,9.
        // Una linea cada minuto como mucho, para verlo en los registros de los usuarios
        double latRxSleepSum, latRxPickSum, latRxPickMax;
        int latRxN, latRxSaid;
        long latRxSaidAt;
        void LatReflexWatch(double pickMs)
        {
            if (pickMs < 0 || pickMs > 1000) return;
            latRxSleepSum += latSleepMs; latRxPickSum += pickMs; latRxN++;
            if (pickMs > latRxPickMax) latRxPickMax = pickMs;
            if (latRxN < 600) return;
            double sl = latRxSleepSum / latRxN, pk = latRxPickSum / latRxN, mx = latRxPickMax;
            latRxSleepSum = latRxPickSum = latRxPickMax = 0; latRxN = 0;
            if (sl < 3 || pk < 3 || latRxSaid >= 30) return;
            if (latRxSaidAt != 0 && Clock.SinceMs(latRxSaidAt) < 60000) return;
            latRxSaidAt = Clock.Now; latRxSaid++;
            Log.Write(string.Format(CultureInfo.InvariantCulture,
                "Reflex: duerme {0:0.0} ms de media por imagen y la imagen de la captura, ya llegada, espera {1:0.0} ms (max {2:0.0}): el tope de imagenes de Reflex no deja pasar las de la captura (+{1:0.0} ms de retraso{3})",
                sl, pk, mx, fgApplied ? "; con la generacion y VRR lo quitan motor_60fg_refresco_fijo=1 o motor_vrr_fg_vsync=0 en el ini" : ""));
        }

        // ---- La cola de la tele con Smooth Motion (sm=1|2). Medido (18-11, fuente de prueba a 59,94, modo 30, tele a 120,000 Hz,
        // PresentMon): cada imagen del motor sale como inventada + real, 2 refrescos cada una (4 refrescos = 33,333 ms por imagen), y
        // las imagenes llegan cada 33,367 ms: si dos presentaciones se juntan (una que sale tarde y la siguiente a su hora: al arrancar,
        // al despertar, un tiron de la grafica o de la CPU), la segunda espera en la cola del driver y la cola solo se vacia 0,033 ms por
        // imagen = 1 ms por segundo. Al arrancar: la real 68 ms de la llegada a verse que bajan a 35 en 35 s (la inventada 52 -> 18).
        // Modelo: X = lo que la imagen de ahora esperaria en esa cola = max(0, X_anterior + P - (ahora - presentacion anterior)), con P
        // = 4 refrescos (sm=2) o 2 (sm=1). Con cola_tirar_ms=N, si X >= N la imagen no se presenta (se suelta): la siguiente llega con la
        // cola vacia. Se pierde esa imagen del juego (un tiron de una imagen) a cambio de quitar de golpe hasta 33 ms de latencia
        double latQX, latQXMax, latQPeriodMs, latPickSubEma;
        long latQLast;
        readonly List<double> latBQX = new List<double>();

        // P: lo que la tele tarda en sacar una imagen del motor (inventada + real). El refresco se vuelve a mirar cada 5 s (la tele se pone
        // sola a 60 Hz). Si P no cuadra con el ritmo de las imagenes (p. ej. cada 2 refrescos con la tele a 60 Hz: 66,7 ms para imagenes de
        // 33,4; Smooth Motion ahi no genera y la tele saca 30), 0 = sin modelo (medido el 18-11: con P mal se tiraba 1 de cada 2)
        long latQPeriodAt;
        double QueuePeriodMs()
        {
            if (latQPeriodMs < 0 || latQPeriodAt == 0 || Clock.SinceMs(latQPeriodAt) > 5000)
            {
                latQPeriodAt = Clock.Now;
                double hz = DisplayHz();
                double p = hz >= 20 ? (s.SmoothMode == 2 ? 4 : 2) * 1000.0 / hz : -1;
                double frame = (s.FpsNum > 0 ? 1000.0 * s.FpsDen / s.FpsNum : 16.683) * (s.Mode == 30 ? 2 : 1);
                if (p > frame * 1.05 || p < frame * 0.9) p = -1;
                if ((p < 0) != (latQPeriodMs < 0) && latQPeriodMs != 0)
                    Log.Write(string.Format(CultureInfo.InvariantCulture, "Cola de la tele: {0} (tele a {1:0.000} Hz, imagenes cada {2:0.0} ms)",
                        p < 0 ? "sin vigilar, el ritmo no cuadra con Smooth Motion" : "vigilada otra vez", hz, frame));
                latQPeriodMs = p;
            }
            return latQPeriodMs;
        }

        // La cola estimada si se presenta ahora (no cambia nada)
        double QueueModelPeek(long now)
        {
            if (s.SmoothMode <= 0 || latQLast == 0) return 0;
            if (QueuePeriodMs() <= 0) return 0;
            double gap = Clock.Ms(latQLast, now);
            if (gap > 1000) return 0;
            return Math.Max(0, latQX + QueuePeriodMs() - gap);
        }

        // Antes de pintar una imagen real: true = se tira (cola_tirar_ms)
        bool QueueModelSkip(long now)
        {
            if (s.QueueDropMs <= 0 || s.SmoothMode <= 0) return false;
            long at = now + (long)(Math.Max(0, latPickSubEma) / Clock.TickMs);   // (a la hora a la que se presentaria)
            double x = QueueModelPeek(at);
            double xg = QueueGpuWait(at);   // 23-11 (ronda 6, SMOOTH): con lo que acaba de verdad la grafica (cola_grafica)
            if (x < s.QueueDropMs && xg < s.QueueDropMs) return false;
            if (xg >= s.QueueDropMs && x < s.QueueDropMs) qgDrops++;
            if (latDrops < 20 || Clock.Ms(latDropAt, now) > 20000)
                Log.Write(string.Format(CultureInfo.InvariantCulture, "Cola de la tele: esta imagen esperaria {0:0.0} ms (Smooth Motion{1}): se tira para vaciarla", Math.Max(x, xg),
                    xg > x ? "; contada con la grafica, que va " + qgLagMs.ToString("0", CultureInfo.InvariantCulture) + " ms detras del envio" : ""));
            latDropAt = now;
            return true;
        }

        // Tras cada presentacion (real o repintada): la cola sigue
        void QueueModelPresented(long now)
        {
            if (s.SmoothMode <= 0) return;
            latQX = QueueModelPeek(now);
            latQLast = now;
            if (latQX > latQXMax) latQXMax = latQX;
        }

        // Cada segundo
        void LatSecond()
        {
            if (latBuf == null || latBuf.Length == 0 || s.LatCsv.Length == 0) return;
            try
            {
                string head = latCsvStarted ? "" : "tipo;seq;llegada;cogida;reflex_ms;espera_cola_ms;envio;vuelta;gpu_ms;pc;fs_pc;fs_qpc;fs_refresco;fs_sync;en_cola;cola_modelo_ms\r\n";
                if (!latCsvStarted) { latCsvStarted = true; File.WriteAllText(s.LatCsv, head + latBuf.ToString()); }
                else File.AppendAllText(s.LatCsv, latBuf.ToString());
            }
            catch (Exception ex) { Log.Error("lat_csv", ex); }
            latBuf.Length = 0;
        }

        void LatFinish()
        {
            LatSecond();
            if (latDrops > 0) Log.Write("Cola de la tele: " + latDrops + " imagenes tiradas para vaciarla (cola_tirar_ms)");
        }

        // ---- 20-11 (LATENCIA4) vrr=1: tele con frecuencia variable. Medido (PresentMon, tele de 120 Hz con VRR y G-SYNC "pantalla completa
        // y ventana" en el perfil global): con Streamline cargado el motor YA salia por VRR (Streamline vuelve a presentar con intervalo 0 y
        // ALLOW_TEARING, y la tele sigue a la imagen: intervalos de 16,68 ms sin escalones de 8,33, fase repartida), asi que el "latido" del
        // registro era solo una cuenta. vrr=1 lo pide sin depender de Streamline (cadena con ALLOW_TEARING, Present(0, ALLOW_TEARING) sin
        // generacion: sirve tambien sin NVIDIA) y, con la generacion, la sincronizacion de vrr_fg_vsync
        bool vrrTearing, vrrActiveSeen;
        const uint PresentAllowTearing = 0x200;

        // InitDevice, antes de crear la cadena: el flag de la cadena
        DXGI_SWAP_CHAIN_FLAG VrrSwapFlags()
        {
            vrrTearing = false;
            if (s.Vrr <= 0) return DXGI_SWAP_CHAIN_FLAG.NONE;
            int allow = 0;
            try
            {
                IDXGIFactory5 f5 = factory as IDXGIFactory5;
                if (f5 != null && f5.CheckFeatureSupport(DXGI_FEATURE.PRESENT_ALLOW_TEARING, &allow, 4) >= 0 && allow != 0) vrrTearing = true;
            }
            catch (Exception ex) { Log.Error("VRR: CheckFeatureSupport", ex); }
            string tele = VrrDisplayState();
            // (NVIDIA: si el driver dice que la tele no admite VRR, sin cortes: la sincronizacion de siempre. Sin NVIDIA no se sabe: se
            // hace caso a vrr=1, que el lanzador solo manda con VRR)
            if (tele.StartsWith("sin VRR", StringComparison.Ordinal)) vrrTearing = false;
            Log.Write("VRR (vrr=1): " + (vrrTearing ? "cadena con ALLOW_TEARING, sin generacion Present(0, ALLOW_TEARING)" : "sin ALLOW_TEARING (DXGI no lo admite o la tele no tiene VRR): sincronizacion de siempre") +
                      (tele.Length > 0 ? "; tele: " + tele : ""));
            return vrrTearing ? DXGI_SWAP_CHAIN_FLAG.ALLOW_TEARING : DXGI_SWAP_CHAIN_FLAG.NONE;
        }

        // Intervalo y flags de cada Present (Run; vrr=0: lo de siempre, vsync / vsync_fg)
        void VrrPresentArgs(out uint sync, out uint flags)
        {
            bool gen = fgApplied || (xeFgOn && xeFgEnabledNow);   // (1.2.4 MOTORFG: XeSS FG como DLSS-G)
            bool vs = gen ? s.VsyncFg : s.Vsync;
            flags = 0;
            if (s.Vrr > 0 && vrrTearing)
            {
                if (gen)
                {
                    // auto: sin sincronizacion si la salida (2 x las reales) queda por debajo del techo de la tele con margen (modo 30: 59,94
                    // en 120 Hz); en el 60 (119,88 en 120 Hz) con ella (sin ella las que llegan antes de 8,33 ms se cortarian)
                    if (s.VrrFgVsync == 0) vs = false;
                    else if (s.VrrFgVsync == 1) vs = true;
                    else vs = !VrrFgRoom();
                }
                else if (s.Vsync) vs = false;
                if (!vs) flags = PresentAllowTearing;
            }
            sync = vs ? 1u : 0u;
            fqSync = sync;   // 22-11 (1.2.4 LATENCIA: cola_tirar_fg, EngineEnvio.cs)
        }

        // ¿La salida con la generacion (2 x las imagenes reales) cabe por debajo del refresco de la tele con un 10 % de margen? (el refresco
        // se mira cada 5 s; si no se sabe, solo el modo 30)
        long vrrRoomAt; bool vrrRoom;
        bool VrrFgRoom()
        {
            if (vrrRoomAt == 0 || Clock.SinceMs(vrrRoomAt) > 5000)
            {
                vrrRoomAt = Clock.Now;
                double hz = DisplayHz();
                double rate = Math.Max(2, FgFactor) * (s.FpsNum > 0 ? s.FpsNum / (double)Math.Max(1, s.FpsDen) : 59.94) * (s.Mode == 30 ? 0.5 : 1);
                vrrRoom = hz > 20 ? rate <= hz * 0.9 : s.Mode == 30;
            }
            return vrrRoom;
        }

        // Estado de VRR de la tele por NVAPI (NvAPI_Disp_GetVRRInfo, desde el driver 525): "" si no se sabe (sin NVIDIA)
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr NvQI(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvFnVoid();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvFnIdByName([MarshalAs(UnmanagedType.LPStr)] string name, out uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvFnVrrInfo(uint id, IntPtr info);
        string VrrDisplayState()
        {
            if (!gpu.IsNvidia) return "";   // (ConsoleEnhancer: sin NVIDIA no se carga NVAPI; el VRR lo decide el lanzador con vrr=, ImagenCe.cs)
            IntPtr info = IntPtr.Zero;
            try
            {
                IntPtr pi = NvQI(0x0150E828), pn = NvQI(0xAE457190), pv = NvQI(0xDF8FDA57);
                if (pi == IntPtr.Zero || pn == IntPtr.Zero || pv == IntPtr.Zero) return "";
                ((NvFnVoid)Marshal.GetDelegateForFunctionPointer(pi, typeof(NvFnVoid)))();
                string dev = System.Windows.Forms.Screen.FromHandle(hwnd).DeviceName;
                uint id;
                if (((NvFnIdByName)Marshal.GetDelegateForFunctionPointer(pn, typeof(NvFnIdByName)))(dev, out id) != 0) return "";
                info = Marshal.AllocHGlobal(24);
                for (int i = 0; i < 24; i += 4) Marshal.WriteInt32(info, i, 0);
                Marshal.WriteInt32(info, 0, 24 | (1 << 16));   // NV_GET_VRR_INFO_VER1
                if (((NvFnVrrInfo)Marshal.GetDelegateForFunctionPointer(pv, typeof(NvFnVrrInfo)))(id, info) != 0) return "";
                int b = Marshal.ReadInt32(info, 4);
                bool on = (b & 1) != 0 || (b & 16) != 0;
                if (on) vrrActiveSeen = true;
                return (on ? "VRR activo" : (b & 2) != 0 ? "VRR posible pero no activo" : "sin VRR") + ((b & 4) != 0 ? " (pedido en el panel de NVIDIA)" : "");
            }
            catch { return ""; }
            finally { if (info != IntPtr.Zero) Marshal.FreeHGlobal(info); }
        }

        static double LPct(List<double> v, double p)
        {
            if (v.Count == 0) return 0;
            double[] a = v.ToArray(); Array.Sort(a);
            return a[Math.Min(a.Length - 1, (int)(p * a.Length))];
        }
        static double LAvg(List<double> v) { double t = 0; foreach (double x in v) t += x; return v.Count > 0 ? t / v.Count : 0; }

        // Para la linea del banco: llegada -> cogida, dormir de Reflex, la llamada a Present, envio -> verse (DXGI), llegada -> verse
        // (DXGI) y la cola de DXGI (presentaciones hechas que aun no se ven)
        string LatBench()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "\tcogida={0:0.00}/{1:0.00}\treflex_dormir={2:0.00}/{3:0.00}\tpresent_ms={4:0.00}/{5:0.00}\tespera_cola={6:0.00}\tdxgi_verse={7:0.0}/{8:0.0}/{9:0.0}\tdxgi_llegada_verse={10:0.0}/{11:0.0}/{12:0.0}\tdxgi_cola={13:0.00}/{14:0}\ttiradas={15}\tcola_modelo={16:0.0}/{17:0.0}/{18:0.0}",
                LAvg(latBPick), LPct(latBPick, 0.95), LAvg(latBSleep), LPct(latBSleep, 0.95), LAvg(latBCall), LPct(latBCall, 0.95), LAvg(latBWait),
                LAvg(latBDisp), LPct(latBDisp, 0.5), LPct(latBDisp, 0.95), LAvg(latBArrDisp), LPct(latBArrDisp, 0.5), LPct(latBArrDisp, 0.95),
                LAvg(latBQueue), LPct(latBQueue, 1.0), latDrops, LAvg(latBQX), LPct(latBQX, 0.95), latQXMax) +
                QueueGpuBench() + OnFrameBench();
        }

        // 20-11 (LATENCIA4): en el hilo de la captura, de la llegada a empezar la copia (repetidas, corte de escena, vigilancia) y la copia
        // a la memoria de subida (copia_hilos)
        readonly List<double> latBPre = new List<double>(), latBCopy = new List<double>();
        readonly object latOnFrameLock = new object();
        void LatOnFrame(double preMs, double copyMs) { lock (latOnFrameLock) { latBPre.Add(preMs); latBCopy.Add(copyMs); } }
        string OnFrameBench()
        {
            lock (latOnFrameLock)
                return string.Format(CultureInfo.InvariantCulture, "\tantes_copia={0:0.00}/{1:0.00}\tcopia={2:0.00}/{3:0.00}\tcopia_hilos={4}",
                    LAvg(latBPre), LPct(latBPre, 0.95), LAvg(latBCopy), LPct(latBCopy, 0.95), CopyThreadsSet > 0 ? CopyThreadsSet : CopyThreads) +
                    "\tcopia_bandas=" + capBandsUsed +   // 20-11 (LATENCIA7, EngineCopia.cs)
                    string.Format(CultureInfo.InvariantCulture, "\tllegada_fin_grafica={0:0.00}/{1:0.00}", LAvg(latBGpuEnd), LPct(latBGpuEnd, 0.95)) +
                    // 21-11 (1.2.1): las colas largas (p99, peor) y lo de mmcss= / cola_cuenta=
                    string.Format(CultureInfo.InvariantCulture, "\tllegada_fin_grafica_p99={0:0.00}/{1:0.00}\tcogida_p99={2:0.00}/{3:0.00}\tcopia_p99={4:0.00}\tmmcss={5}\tcola_sobras={6}\tcola_sin_sitio={7}",
                        LPct(latBGpuEnd, 0.99), LPct(latBGpuEnd, 1.0), LPct(latBPick, 0.99), LPct(latBPick, 1.0), LPct(latBCopy, 0.99), s.Mmcss, latAbsorbed, latTimeouts);
        }

        // 20-11 (LATENCIA7): de la llegada de cada imagen real a que la grafica acaba su lista (la marca de tiempo del final pasada a la hora
        // de la CPU con GetClockCalibration): lo que mide la tanda sin la tele (sin la generacion de NVIDIA, que va despues, ni la pantalla).
        // Solo en el banco. Run: LatGpuEnd con la marca de la vez anterior de la lista fi (ya acabada), latFiArrival al enviarla
        readonly long[] latFiArrival = new long[FramesInFlight];
        readonly List<double> latBGpuEnd = new List<double>();
        long latCalAt; ulong latCalGpu, latCalCpu;
        void LatGpuEnd(int fi, ulong tEnd)
        {
            QueueGpuEnd(fi, tEnd);   // 23-11 (ronda 6, SMOOTH): la cola de la tele con lo que acaba de verdad la grafica
            long a = latFiArrival[fi];
            latFiArrival[fi] = 0;
            if (a == 0 || tEnd == 0 || !stats.Recording || tsFreq <= 0) return;
            double end;
            if (!GpuToQpc(tEnd, out end)) return;
            double ms = (end - a) * Clock.TickMs;
            if (ms > 0 && ms < 500) latBGpuEnd.Add(ms);
        }

        // La marca de tiempo de la grafica pasada a la hora de la CPU (QPC), con la calibracion de la cola (cada segundo)
        bool GpuToQpc(ulong t, out double qpc)
        {
            qpc = 0;
            if (t == 0 || tsFreq <= 0) return false;
            if (latCalAt == 0 || Clock.SinceMs(latCalAt) > 1000)
            {
                ulong g, c;
                if (queue.GetClockCalibration(out g, out c) >= 0) { latCalGpu = g; latCalCpu = c; latCalAt = Clock.Now; }
            }
            if (latCalAt == 0) return false;
            qpc = latCalCpu + ((double)t - latCalGpu) * System.Diagnostics.Stopwatch.Frequency / tsFreq;
            return true;
        }

        // ---- 23-11 (ronda 6, SMOOTH): la cola de la tele con Smooth Motion, contada con la hora a la que ACABA la grafica (cola_grafica=1).
        // El modelo de arriba (QueueModelPeek) cuenta con la hora del Present: no ve cuando la grafica va por detras de lo enviado. Medido
        // (motor solo, test=archivo con las capturas de la PS5, 4K modo 30, DLSS 5 1 pasada, Smooth Motion "cada 2 refrescos" + VRR,
        // PresentMon + lat_csv): la primera imagen tras arrancar tarda 808 ms en la grafica (DLSS 5 la primera vez), las 2-3 de detras ya estan
        // enviadas y se ven todas seguidas cuando acaba; el modelo cree que la tele las saco al enviarlas (cola 10 -> 2 ms) y la de verdad se
        // queda llena: de la llegada a verse la real 78 ms a los 2 s, bajando 1 ms por segundo (60 a los 19 s) frente a 47-53 sin cola. En 8
        // de 12 pasadas con DLSS 5 (10-30 ms de mas durante 15-25 s tras arrancar; igual al despertar y, mas corto, al rehacer DLSS 5).
        // Aqui: cada lista de pintado del bucle se apunta al enviarla (QueueGpuMark) y, cuando se sabe su fin en la grafica (LatGpuEnd, una
        // vuelta despues), la tele queda libre en max(fin, libre) + P. Para la imagen de ahora se proyectan las que aun estan en la grafica con
        // su retraso tipico (envio -> fin). Se tira la real si esperaria cola_tirar_ms o mas con cualquiera de los dos modelos.
        // Medido con esto (6 pasadas, 4K y 1440p, modo 30, DLSS 5): a los 2 s del arranque 43-51 ms (sin cola) en las 6; lo estable igual que
        // antes (real 49,3-50,0 frente a 49,3-50,2 ms, inventada 32,6-33,4 / 32,6-33,6) y como mucho una real tirada de mas al arrancar.
        // Lo que NO arregla: la cola del arranque del modo 60 (~9 ms de mas durante ~10 s, sin DLSS 5): ningun modelo la ve (ni bajando el
        // umbral a 5 ms: medido igual, 28,1 frente a 28,6 ms a los 2 s)
        public bool QueueGpuOn { get { return s.QueueGpu && s.SmoothMode > 0 && s.QueueDropMs > 0; } }
        readonly long[] qgMark = new long[FramesInFlight];   // Clock.Now al enviar la lista de la ranura fi del bucle (0 = ninguna)
        double qgFree, qgLastReady;                           // QPC: la tele libre tras lo ya listo / la ultima lista acabada
        double qgLagMs = -1, qgDurMs = -1;                    // envio -> fin en la grafica, y lo que dura una lista en la grafica, tipicos (medias moviles)
        double qgWaitLast, qgWaitMax;                         // lo que espero en la tele la ultima ya acabada (ms)
        int qgDrops;
        readonly List<double> latBQG = new List<double>();

        // Engine.Run, tras grabar la lista de la imagen (real o repintada) en la ranura fi
        void QueueGpuMark(int fi)
        {
            if (!QueueGpuOn) { qgMark[fi] = 0; return; }
            qgMark[fi] = Clock.Now;
        }

        void QueueGpuEnd(int fi, ulong tEnd)
        {
            long mark = qgMark[fi];
            qgMark[fi] = 0;
            if (!QueueGpuOn) return;
            double end, p = QueuePeriodMs();
            if (p <= 0 || !GpuToQpc(tEnd, out end)) return;
            double pT = p / Clock.TickMs;
            // (lo que acaba en la grafica es una imagen que va a la tele, sea del bucle o la negra de la espera: cuenta; el retraso tipico
            // solo con las del bucle recientes)
            double lag = mark != 0 ? (end - mark) * Clock.TickMs : -1;
            if (lag > 0 && lag < 200) qgLagMs = qgLagMs < 0 ? lag : qgLagMs * 0.9 + lag * 0.1;
            if (qgFree == 0 || end - qgFree > 2000 / Clock.TickMs) qgFree = end;   // (tras mas de 2 s sin nada: la tele vacia)
            qgWaitLast = Math.Max(0, (qgFree - end) * Clock.TickMs);
            if (qgWaitLast > qgWaitMax) qgWaitMax = qgWaitLast;
            if (stats.Recording) latBQG.Add(qgWaitLast);
            qgFree = Math.Max(end, qgFree) + pT;
            qgLastReady = Math.Max(qgLastReady, end);
        }

        // Lo que esperaria en la tele una real que se presentase a la hora 'at' (ms), con las listas que siguen en la grafica proyectadas
        double QueueGpuWait(long at)
        {
            if (!QueueGpuOn || qgFree == 0) return 0;
            double p = QueuePeriodMs();
            if (p <= 0) return 0;
            // (cada una lista como pronto a su retraso tipico desde el envio y tras la de delante mas lo que dura una; sin retraso medido aun,
            // lo que dura)
            double durT = Math.Max(0, qgDurMs) / Clock.TickMs, lagT = (qgLagMs >= 0 ? qgLagMs : Math.Max(0, qgDurMs)) / Clock.TickMs, pT = p / Clock.TickMs;
            if (at - qgFree > 2000 / Clock.TickMs) return 0;
            double free = qgFree, last = qgLastReady;
            // las que aun estan en la grafica, por orden de envio (como mucho FramesInFlight)
            long m0 = long.MaxValue, m1 = long.MaxValue;
            for (int i = 0; i < FramesInFlight; i++)
            {
                long m = qgMark[i];
                if (m == 0 || Clock.Ms(m, at) > 2000) continue;
                if (m < m0) { m1 = m0; m0 = m; } else if (m < m1) m1 = m;
            }
            if (m0 != long.MaxValue) { double r = Math.Max(m0 + lagT, last + durT); free = Math.Max(r, free) + pT; last = r; }
            if (m1 != long.MaxValue) { double r = Math.Max(m1 + lagT, last + durT); free = Math.Max(r, free) + pT; last = r; }
            double rn = Math.Max(at + lagT, last + durT);
            return Math.Max(0, (free - rn) * Clock.TickMs);
        }

        // Para la linea del banco: lo que espero cada imagen en la tele segun la grafica (media / p95 / peor) y las tiradas por esto
        string QueueGpuBench()
        {
            return string.Format(CultureInfo.InvariantCulture, "\tcola_grafica={0:0.0}/{1:0.0}/{2:0.0}\ttiradas_grafica={3}\tretraso_grafica={4:0.0}",
                LAvg(latBQG), LPct(latBQG, 0.95), qgWaitMax, qgDrops, qgLagMs);
        }
    }

    // 21-11 (1.2.1): claves de la latencia (Settings.Apply de Common.cs las pasa aqui)
    partial class Settings
    {
        public bool QueueCount = true;      // cola_cuenta=0: la espera de la cadena como antes (Engine.LatQueueWait)
        public int Mmcss = 0;               // mmcss=1 (prueba): los hilos de la captura y de pintado en MMCSS (de serie no: sin diferencia medida)
        public bool QueueGpu = true;        // 23-11 (ronda 6, SMOOTH) cola_grafica=0: la cola de la tele con Smooth Motion solo con la hora del Present (como antes)

        bool ApplyLatencia(string key, string val)
        {
            switch (key)
            {
                case "cola_cuenta": QueueCount = B(val); return true;
                case "mmcss": Mmcss = B(val) ? 1 : 0; return true;
                case "cola_grafica": QueueGpu = B(val); return true;
            }
            return false;
        }
    }
}
