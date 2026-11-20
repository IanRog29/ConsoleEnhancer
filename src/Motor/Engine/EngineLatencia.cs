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
        bool Pass30(bool isNew, long since)
        {
            bool first = lastPassed < 0;
            bool pass = first || (since >= 2 && (isNew || dup30PrevNew)) || since >= 3 || (isNew && dup30Forced && s.Dedupe30 >= 3);
            if (pass) dup30Forced = !isNew && !dup30PrevNew && !first;
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
        void LatQueueWait()
        {
            latQueueWaitMs = 0;
            if (latWaitable == IntPtr.Zero) return;
            long t = Clock.Now;
            WaitForSingleObjectEx(latWaitable, 100, true);
            latQueueWaitMs = Clock.SinceMs(t);
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
            else if (QueueModelSkip(Clock.Now))
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
            QueueModelPresented(submit);
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
            double x = QueueModelPeek(now + (long)(Math.Max(0, latPickSubEma) / Clock.TickMs));   // (a la hora a la que se presentaria)
            if (x < s.QueueDropMs) return false;
            if (latDrops < 20 || Clock.Ms(latDropAt, now) > 20000)
                Log.Write(string.Format(CultureInfo.InvariantCulture, "Cola de la tele: esta imagen esperaria {0:0.0} ms (Smooth Motion): se tira para vaciarla", x));
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
            bool vs = fgApplied ? s.VsyncFg : s.Vsync;
            flags = 0;
            if (s.Vrr > 0 && vrrTearing)
            {
                if (fgApplied)
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
                double rate = 2.0 * (s.FpsNum > 0 ? s.FpsNum / (double)Math.Max(1, s.FpsDen) : 59.94) * (s.Mode == 30 ? 0.5 : 1);
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
                OnFrameBench();
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
                    string.Format(CultureInfo.InvariantCulture, "\tllegada_fin_grafica={0:0.00}/{1:0.00}", LAvg(latBGpuEnd), LPct(latBGpuEnd, 0.95));
        }

        // 20-11 (LATENCIA7): de la llegada de cada imagen real a que la grafica acaba su lista (la marca de tiempo del final pasada a la hora
        // de la CPU con GetClockCalibration): lo que mide la tanda sin la tele (sin la generacion de NVIDIA, que va despues, ni la pantalla).
        // Solo en el banco. Run: LatGpuEnd con la marca de la vez anterior de la lista fi (ya acabada), latFiArrival al enviarla
        readonly long[] latFiArrival = new long[FramesInFlight];
        readonly List<double> latBGpuEnd = new List<double>();
        long latCalAt; ulong latCalGpu, latCalCpu;
        void LatGpuEnd(int fi, ulong tEnd)
        {
            long a = latFiArrival[fi];
            latFiArrival[fi] = 0;
            if (a == 0 || tEnd == 0 || !stats.Recording || tsFreq <= 0) return;
            if (latCalAt == 0 || Clock.SinceMs(latCalAt) > 1000)
            {
                ulong g, c;
                if (queue.GetClockCalibration(out g, out c) >= 0) { latCalGpu = g; latCalCpu = c; latCalAt = Clock.Now; }
            }
            if (latCalAt == 0) return;
            double end = latCalCpu + ((double)tEnd - latCalGpu) * System.Diagnostics.Stopwatch.Frequency / tsFreq;
            double ms = (end - a) * Clock.TickMs;
            if (ms > 0 && ms < 500) latBGpuEnd.Add(ms);
        }
    }
}
