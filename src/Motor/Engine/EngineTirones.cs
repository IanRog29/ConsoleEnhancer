using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 16-11 (tirones): estadisticas ligeras del ritmo, siempre puestas (sin stats=1). Cada ~20 s, UNA linea al log y solo si ha
    // pasado algo ("Tirones (20 s, ...)"): imagenes de la captura perdidas antes de pintarlas, presentaciones que se retrasan
    // respecto a su captura, picos de latencia (captura -> Present), huecos de la capturadora, presentaciones de la generacion sin
    // su imagen intermedia y la grafica por encima de su tope; y de paso lo del juego (repetidas quitadas, saltos de cadencia: un
    // juego a 30 con imagenes de 1 y 3 refrescos). Ademas, una vez por sesion, el "latido" de los relojes: la captura a 59,94 y la
    // tele a 120,000 Hz hacen que cada ~8,3 s una imagen se vea un refresco de mas (con 119,88 Hz no hay latido).
    // Coste: unos contadores por imagen y ordenar <= 1200 numeros cada 20 s.
    unsafe partial class Engine
    {
        const double TirWindowMs = 20000;
        long tirStart;                          // inicio de la ventana (0 = sin empezar)
        int tirFrames;                          // imagenes reales presentadas en la ventana
        long tirPrevArrival, tirPrevPresent;
        int tirSlow; double tirSlowWorst;       // presentaciones que salen > 1/2 imagen mas tarde que su captura respecto a la anterior
        readonly float[] tirLat = new float[4096];
        int tirLatN;
        int tirGpuOver; double tirGpuWorst;
        int tirFgShort, tirFgCalls, tirFgSkip;  // generacion: presentaciones con menos de 2 imagenes (tras las 30 primeras)
        int tirLate, tirDups;                   // (de Stats, cada segundo)
        long tirSessionFrames = -1;
        bool tirBeatSaid;
        // hilo de captura
        long tirCapPrev;
        int tirCapGaps; double tirCapWorst;
        int tirCadJumps, tirLastPass;
        readonly object tirCapLock = new object();
        // la ultima pregunta a la generacion (FgState la reutiliza: una llamada por imagen, no dos)
        int fgLastPresented = -1;
        long fgPolledAt;
        SlResult fgLastResult;
        SlDLSSGStatus fgLastStatus;
        int tirGc2Start;                        // recolecciones completas de memoria al empezar la ventana (contexto)
        Timer tirGrabTimer;                     // test_grab_ms: capturas como las del lanzador, sin lanzador
        // Tras un cambio (sesion nueva, tamaño, generacion, modelo, modo, escalador) 2 s sin contar: esos tirones ya los dice su linea
        volatile bool tirMuted;
        long tirMuteUntil;
        string tirState = "";

        double TirFrameMs { get { return s.FpsNum > 0 ? 1000.0 * s.FpsDen / s.FpsNum : 16.683; } }
        double TirGpuLimit { get { return s.Mode == 30 ? (fgApplied ? 26.0 : 31.0) : s.Is120 ? 8.0 : (fgApplied ? 11.0 : 15.5); } }   // (los de AutoPresetTick; 20-11 HZ120: ~8,3 ms a 120)

        void TirReset(long now)
        {
            tirStart = now;
            tirFrames = 0; tirSlow = 0; tirSlowWorst = 0; tirLatN = 0; tirGpuOver = 0; tirGpuWorst = 0;
            tirFgShort = 0; tirFgCalls = 0; tirLate = 0; tirDups = 0;
            tirGc2Start = GC.CollectionCount(2);
            lock (tirCapLock) { tirCapGaps = 0; tirCapWorst = 0; tirCadJumps = 0; }
            if (s.TestGrabMs > 0 && tirGrabTimer == null)
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s2e_prueba_captura.raw");
                tirGrabTimer = new Timer(delegate { if (!grabWanted) { grabPath = path; grabMode = "light"; grabWanted = true; } }, null, s.TestGrabMs, s.TestGrabMs);
                Log.Write("Prueba: una captura del lanzador (light) cada " + s.TestGrabMs + " ms");
            }
        }

        // Hilo de pintado: cada imagen real presentada (tras Present)
        void TirFrame(long arrival, long now, double gpuMs)
        {
            if (tirStart == 0 || presentedFrames < tirSessionFrames)
            {
                // sesion nueva (arranque o despertar: presentedFrames vuelve a empezar)
                TirReset(now);
                tirPrevPresent = 0;
                tirBeatSaid = false;
                tirFgSkip = 30;
            }
            tirSessionFrames = presentedFrames;
            string st0 = inW + "x" + inH + "|" + fgApplied + "|" + s.Preset + "|" + s.Mode + "|" + s.Upscaler;
            if (st0 != tirState) { tirState = st0; tirMuteUntil = now + (long)(2000 / Clock.TickMs); }
            tirMuted = now < tirMuteUntil;
            if (tirMuted) { tirPrevPresent = now; tirPrevArrival = arrival; tirFgSkip = 30; return; }
            tirFrames++;
            double frame = TirFrameMs;
            if (tirLatN < tirLat.Length) tirLat[tirLatN++] = (float)Clock.Ms(arrival, now);
            if (tirPrevPresent != 0)
            {
                double dp = Clock.Ms(tirPrevPresent, now), da = Clock.Ms(tirPrevArrival, arrival);
                double late = dp - da;
                if (late > frame * 0.5 && dp > frame * 1.5) { tirSlow++; if (late > tirSlowWorst) tirSlowWorst = late; }
            }
            tirPrevPresent = now; tirPrevArrival = arrival;
            if (gpuMs > TirGpuLimit) { tirGpuOver++; if (gpuMs > tirGpuWorst) tirGpuWorst = gpuMs; }
            if (fgApplied && fgOk)
            {
                SlDLSSGState st;
                SlResult r = Sl.DLSSGGetState(vp, out st);
                fgLastResult = r; fgLastStatus = st.status; fgLastPresented = (int)st.numFramesActuallyPresented; fgPolledAt = now;
                if (tirFgSkip > 0) tirFgSkip--;
                else if (r == SlResult.eOk && st.status == SlDLSSGStatus.eOk)
                {
                    tirFgCalls++;
                    if (st.numFramesActuallyPresented < 2) tirFgShort++;
                }
            }
            else tirFgSkip = 30;
        }

        // Hilo de captura: cada imagen que llega (antes de quitar repetidas)
        void TirCapture(long arrival)
        {
            long prev = tirCapPrev;
            tirCapPrev = arrival;
            if (prev == 0 || tirMuted) return;
            double d = Clock.Ms(prev, arrival);
            if (d > TirFrameMs * 1.5 && d < 5000) lock (tirCapLock) { tirCapGaps++; if (d > tirCapWorst) tirCapWorst = d; }
        }

        // Hilo de captura, con repetidas quitadas: since = imagenes desde la ultima que paso (1 = 60, 2 = 30...)
        void TirPass(long since)
        {
            if (since <= 0 || since > 8) { tirLastPass = 0; return; }
            int p = (int)since;
            if (tirLastPass != 0 && p != tirLastPass) lock (tirCapLock) tirCadJumps++;
            tirLastPass = p;
        }

        // Hilo de pintado, cada segundo (SecondTick)
        void TirSecond()
        {
            if (!tirMuted) { tirLate += stats.LastLate; tirDups += stats.LastDups; }
            DumpWrite();
            KeepDisplayAwake();
            if (tirStart == 0) return;
            if (!tirBeatSaid && tirFrames >= 30) TirBeat();
            if (Clock.SinceMs(tirStart) < TirWindowMs) return;
            double secs = Clock.SinceMs(tirStart) / 1000.0;
            int gaps, jumps; double capWorst;
            lock (tirCapLock) { gaps = tirCapGaps; capWorst = tirCapWorst; jumps = tirCadJumps; }
            int spikes = 0; double med = 0, worstLat = 0;
            if (tirLatN > 10)
            {
                float[] a = new float[tirLatN];
                Array.Copy(tirLat, a, tirLatN);
                Array.Sort(a);
                med = a[a.Length / 2]; worstLat = a[a.Length - 1];
                double lim = med + TirFrameMs * 0.5;
                for (int i = a.Length - 1; i >= 0 && a[i] > lim; i--) spikes++;
            }
            // (los saltos de cadencia del juego cuentan si son muchos: un juego a 30 con imagenes de 1 y 3 refrescos; con repetidas
            // quitadas y la generacion, la tele las enseña irregulares: medido, 136 imagenes de 25 ms en 40 s)
            bool any = tirLate > 0 || tirSlow > 0 || spikes > 0 || gaps > 0 || tirFgShort > 0 || tirGpuOver > 0 || jumps >= 10;
            if (any && tirFrames > 0)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendFormat(IC, "Tirones ({0:0} s, {1} imagenes, {2}):", secs, tirFrames, fgApplied ? "con generacion" : "sin generacion");
                if (tirLate > 0) sb.AppendFormat(IC, " {0} perdidas antes de pintarlas |", tirLate);
                if (tirSlow > 0) sb.AppendFormat(IC, " {0} presentadas con retraso (peor +{1:0.0} ms) |", tirSlow, tirSlowWorst);
                if (spikes > 0) sb.AppendFormat(IC, " latencia: {0} picos (peor {1:0.0} ms, mediana {2:0.0}) |", spikes, worstLat, med);
                if (gaps > 0) sb.AppendFormat(IC, " captura: {0} huecos (peor {1:0.0} ms) |", gaps, capWorst);
                if (tirFgShort > 0) sb.AppendFormat(IC, " generacion: {0} de {1} sin la intermedia |", tirFgShort, tirFgCalls);
                if (tirGpuOver > 0) sb.AppendFormat(IC, " grafica: {0} por encima de {1:0.0} ms (peor {2:0.0}) |", tirGpuOver, TirGpuLimit, tirGpuWorst);
                if (tirDups > 0 || jumps > 0) sb.AppendFormat(IC, " juego: {0} repetidas quitadas, {1} saltos de cadencia |", tirDups, jumps);
                int gc2 = GC.CollectionCount(2) - tirGc2Start;
                if (gc2 > 0) sb.AppendFormat(IC, " memoria: {0} recolecciones completas |", gc2);
                string l = sb.ToString();
                Log.Write(l.EndsWith("|", StringComparison.Ordinal) ? l.Substring(0, l.Length - 2) : l);
            }
            TirReset(Clock.Now);
        }

        // Una vez por sesion: el latido de los relojes (la tele no va exactamente al doble de la captura)
        void TirBeat()
        {
            tirBeatSaid = true;
            double hz = DisplayHz();
            if (hz <= 0) return;
            double cap = s.FpsNum / (double)Math.Max(1, s.FpsDen);
            double rate = (fgApplied || s.SmoothMode > 0 ? 2 : 1) * cap * (s.Mode == 30 ? 0.5 : 1);   // (18-11: sm= Smooth Motion del driver tambien x2)
            double k = Math.Max(1, Math.Round(hz / rate));      // refrescos por imagen
            double spare = hz - k * rate;                        // refrescos por segundo que sobran (o faltan)
            // 20-11 (LATENCIA4): con la tele en VRR (NVAPI, EngineLatencia.cs) no hay latido: cada imagen sale al llegar (antes se decia
            // "latido" tambien con VRR: era solo la cuenta)
            string vrr = s.RefreshFixed ? "" : VrrDisplayState();   // (20-11 LATENCIA5: refresco_fijo=1, G-SYNC apagado para el motor)
            if (s.RefreshFixed) Log.Write("Ritmo: refresco fijo para el motor (G-SYNC apagado en su perfil, refresco_fijo=1: sin el tope de 116 de Reflex)");
            if (vrr.StartsWith("VRR activo", StringComparison.Ordinal))
                Log.Write(string.Format(IC, "Ritmo: tele a {0:0.000} Hz con {1} y {2:0.000} imagenes/s: cada una se ve al llegar, sin latido{3}", hz, vrr, rate,
                    s.Vrr > 0 ? " (vrr=1)" : " (vrr=0: el VRR lo pone Streamline)"));
            else if (Math.Abs(spare) < 0.005)
                Log.Write(string.Format(IC, "Ritmo: tele a {0:0.000} Hz y {1:0.000} imagenes/s ({2} refresco{3} cada una): sin latido", hz, rate, k, k > 1 ? "s" : ""));
            else
                Log.Write(string.Format(IC, "Ritmo: tele a {0:0.000} Hz y {1:0.000} imagenes/s ({2} refresco{3} cada una): latido, una imagen {4} cada {5:0.0} s",
                    hz, rate, k, k > 1 ? "s" : "", spare > 0 ? "un refresco mas" : "un refresco menos", 1 / Math.Abs(spare)));
        }

        // ---- 16-11 (1440p con generacion): tiempos de GPU por pasada, solo con stats=1 o en el banco (en juego normal nada): la
        // descompresion de la captura, bordes/tramado (s2_aa), vectores (con lo de calidad), DLSS, CAS (+ interfaz), aviso + copia a la
        // tele. Media y peor en la linea del banco ("pasos=")
        // ---- 16-11 (E): pantalla encendida mientras llega imagen, como hace un reproductor (mpv lo hacia con su ventana). Sin esto Windows
        // apaga la pantalla a los 15 min sin raton ni teclado (el mando de la consola no cuenta) y la tele, al volver, se queda a 60 Hz: paso
        // en las pruebas de hoy (15:25, a mitad de una). Solo con imagen de la fuente (no en espera, ni sin señal: ahi Windows decide).
        // Sin ES_CONTINUOUS: cada llamada solo reinicia el contador, y al dejar de llamar (espera, cierre) todo vuelve a ser como siempre.
        // pantalla_despierta=0: no se toca
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
        bool awakeSaid;

        void KeepDisplayAwake()
        {
            if (!s.KeepAwake || inStandby || windowPending || lastCapture == 0 || Clock.SinceMs(lastCapture) > 3000) return;
            SetThreadExecutionState(0x2 | 0x1);    // ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED
            if (!awakeSaid) { awakeSaid = true; Log.Write("Pantalla: encendida mientras llegue imagen (sin el apagado de Windows por inactividad)"); }
        }

        // ---- 16-11 (prueba de la copia asincrona) test_volcado=<fichero>: la imagen descomprimida (RGBA a la resolucion de la captura)
        // de la imagen real numero 120, a un fichero (filas de ancho*4 bytes): igual byte a byte con copia_asincrona=0 y =1
        ID3D12Resource dumpRead;
        IntPtr dumpReadPtr;
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT dumpFoot;
        ulong dumpFence;
        int dumpState;              // 0 = por hacer, 1 = pedida (esperando la valla), 2 = hecha
        int dumpCount;

        // RenderFrame, tras la descompresion (rawColor en NON_PIXEL_SHADER_RESOURCE)
        void DumpRaw()
        {
            if (s.TestDump.Length == 0 || dumpState != 0 || ++dumpCount < 120) return;
            D3D12_RESOURCE_DESC d;
            rawColor.GetDesc(out d);
            D3D12_PLACED_SUBRESOURCE_FOOTPRINT fp; uint rows; ulong rowSize, total;
            dev.GetCopyableFootprints(ref d, 0, 1, 0, &fp, &rows, &rowSize, &total);
            dumpRead = MakeBuffer(D3D12_HEAP_TYPE.READBACK, (long)total, D3D12_RESOURCE_STATES.COPY_DEST, "volcado de prueba");
            Hr.Check(dumpRead.Map(0, null, out dumpReadPtr), "Map volcado");
            dumpFoot = fp;
            Barrier(rawColor, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.COPY_SOURCE);
            D3D12_TEXTURE_COPY_LOCATION dst = D3D12_TEXTURE_COPY_LOCATION.FromFootprint(dumpRead, fp);
            D3D12_TEXTURE_COPY_LOCATION src = D3D12_TEXTURE_COPY_LOCATION.FromSubresource(rawColor, 0);
            list.CopyTextureRegion(ref dst, 0, 0, 0, ref src, null);
            Barrier(rawColor, D3D12_RESOURCE_STATES.COPY_SOURCE, D3D12_RESOURCE_STATES.NON_PIXEL_SHADER_RESOURCE);
            dumpFence = fenceValue + 1;
            dumpState = 1;
        }

        // Cada segundo (TirSecond): si ya termino, al fichero
        void DumpWrite()
        {
            if (dumpState != 1 || fence.GetCompletedValue() < dumpFence) return;
            dumpState = 2;
            try
            {
                int w = (int)dumpFoot.Footprint.Width, h = (int)dumpFoot.Footprint.Height, pitch = (int)dumpFoot.Footprint.RowPitch;
                byte[] o = new byte[(long)w * h * 4];
                for (int y = 0; y < h; y++) Marshal.Copy(new IntPtr(dumpReadPtr.ToInt64() + (long)dumpFoot.Offset + (long)y * pitch), o, y * w * 4, w * 4);
                System.IO.File.WriteAllBytes(s.TestDump, o);
                Log.Write("Prueba: imagen descomprimida " + w + "x" + h + " a " + s.TestDump);
            }
            catch (Exception ex) { Log.Error("Volcado de prueba", ex); }
        }

        ID3D12QueryHeap pHeap;
        ID3D12Resource pRead;
        IntPtr pReadPtr;
        const int PRing = 4, PMarks = 7;
        readonly ulong[] pFence = new ulong[PRing];
        int pCur;
        bool pOn, pRecording;
        readonly double[] pSum = new double[PMarks - 1], pMax = new double[PMarks - 1];
        int pN;

        bool PassBegin(bool newFrame)
        {
            pOn = false;
            if (!newFrame || (s.BenchSecs <= 0 && !s.ShowStats)) return false;
            if (pHeap == null)
            {
                D3D12_QUERY_HEAP_DESC qhd = new D3D12_QUERY_HEAP_DESC();
                qhd.Type = D3D12_QUERY_HEAP_TYPE.TIMESTAMP;
                qhd.Count = PRing * PMarks;
                object o;
                Guid iid = typeof(ID3D12QueryHeap).GUID;
                Hr.Check(dev.CreateQueryHeap(ref qhd, ref iid, out o), "CreateQueryHeap pasos");
                pHeap = (ID3D12QueryHeap)o;
                pRead = MakeBuffer(D3D12_HEAP_TYPE.READBACK, 8 * PRing * PMarks, D3D12_RESOURCE_STATES.COPY_DEST, "tiempos por pasada");
                Hr.Check(pRead.Map(0, null, out pReadPtr), "Map tiempos por pasada");
            }
            if (stats.Recording && !pRecording) { pRecording = true; pN = 0; for (int k = 0; k < pSum.Length; k++) pSum[k] = pMax[k] = 0; }
            ulong done = fence.GetCompletedValue();
            for (int i = 0; i < PRing; i++)
            {
                if (pFence[i] == 0 || done < pFence[i]) continue;
                ulong* t = (ulong*)pReadPtr + i * PMarks;
                bool ok = true;
                for (int k = 1; k < PMarks; k++) if (t[k] < t[k - 1]) ok = false;
                if (ok)
                {
                    for (int k = 1; k < PMarks; k++)
                    {
                        double ms = (t[k] - t[k - 1]) * 1000.0 / tsFreq;
                        pSum[k - 1] += ms; if (ms > pMax[k - 1]) pMax[k - 1] = ms;
                    }
                    pN++;
                }
                pFence[i] = 0;
            }
            pCur = (pCur + 1) % PRing;
            if (pFence[pCur] != 0) return false;   // (la de hace 4 imagenes aun no ha terminado: esta sin medir)
            pOn = true;
            PassMark(0);
            return true;
        }

        void PassMark(int k) { if (pOn) list.EndQuery(pHeap, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(pCur * PMarks + k)); }

        void PassEnd()
        {
            if (!pOn) return;
            PassMark(PMarks - 1);
            list.ResolveQueryData(pHeap, D3D12_QUERY_TYPE.TIMESTAMP, (uint)(pCur * PMarks), PMarks, pRead, (ulong)(pCur * PMarks * 8));
            pFence[pCur] = fenceValue + 1;   // la valla de esta imagen (se senala al enviarla)
            pOn = false;
        }

        // Para la linea del banco (BenchDone)
        string PassesBench()
        {
            if (pN == 0) return "";
            string[] names = { "desc", "aa", "mov", "dlss", "cas", "copia" };
            System.Text.StringBuilder sb = new System.Text.StringBuilder("\tpasos=");
            for (int k = 0; k < names.Length; k++)
                sb.AppendFormat(IC, "{0}{1} {2:0.00}/{3:0.00}", k > 0 ? " " : "", names[k], pSum[k] / pN, pMax[k]);
            return sb.ToString();
        }

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, byte[] paths, ref uint numModes, byte[] modes, IntPtr topologyId);

        // El refresco exacto de la pantalla activa (QueryDisplayConfig: el racional, 120/1 o 120000/1001); 0 si no se sabe
        static double DisplayHz()
        {
            try
            {
                uint np, nm;
                if (GetDisplayConfigBufferSizes(2 /*QDC_ONLY_ACTIVE_PATHS*/, out np, out nm) != 0 || np == 0) return 0;
                byte[] paths = new byte[np * 72], modes = new byte[nm * 64];
                if (QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return 0;
                uint num = BitConverter.ToUInt32(paths, 48), den = BitConverter.ToUInt32(paths, 52);   // targetInfo.refreshRate del primero
                return den != 0 ? num / (double)den : 0;
            }
            catch { return 0; }
        }
    }
}
