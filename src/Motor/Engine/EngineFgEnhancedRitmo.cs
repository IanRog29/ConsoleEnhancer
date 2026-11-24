using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // EngineFgEnhancedRitmo.cs - 23-11-2026 (1.3.1, octava ronda, agente EFGCAL). El RITMO de Enhanced FG cuando las reales NO llegan cada
    // 33,4 ms exactos (efg_ritmo=1, de serie).
    // Lo que pasaba (1.3): cada real ponia sus k imagenes a una hora fija desde su llegada (B = llegada + L, y luego cada intervalo / k), con
    // las inventadas siempre en t = 1/k, 2/k... Con un juego de consola a 30 en la señal de 59,94 las reales no llegan siempre a 2 capturas:
    // una imagen del juego de 3 refrescos (una bajada) llega 50 ms despues de la anterior y la siguiente a 1 (el filtro de repetidas la deja
    // pasar en su 2a copia, 16,7 ms tarde). En la sesion del usuario (GTA VI, PS5): 12-44 "saltos de cadencia" cada 20 s. Con la hora fija:
    //  - tras un hueco de 50 ms la inventada (t = 0,5) salia 33 ms despues de la real anterior y la real 16,7 despues: lo que se ve en
    //    pantalla no casa con el tiempo (lo de en medio se enseña tarde y se "para" y luego salta);
    //  - una real que llega 16,7 ms despues de la anterior se retrasaba 8,3 ms (B >= la ultima + medio paso) y arrastraba a las siguientes;
    //  - la 2a copia de una imagen (la que deja pasar el filtro) se tomaba con la hora de esa copia, 16,7 ms tarde.
    // Ahora:
    //  - la hora de cada real es la de su CONTENIDO: la de la primera captura en que aparecio esa imagen (no la de la copia que pasa el filtro);
    //  - modo calidad: la real va a contenido + D (D = L + (k-1)/k del intervalo, lo mismo que antes con el ritmo exacto) y las inventadas
    //    REPARTEN el hueco de verdad hasta la real anterior: tantas como quepan a un paso (x2: 0 tras 16,7 ms, 1 tras 33,4, 2 tras 50; x3/x4
    //    como mucho k-1) y cada una con su t = (su hora - la real anterior) / (la real - la real anterior): lo que se ve casa con su hora aunque
    //    el hueco sea irregular. Las que ya no llegan a tiempo (el hueco largo empieza antes de tener la real) no se hacen;
    //  - modo baja latencia: la real enseguida como siempre; sus inventadas hacia delante con t = 1 + (lo que va por delante) / (el intervalo
    //    del contenido, como poco el nominal: nunca mas lejos que antes); si mientras espera a enseñar una inventada la siguiente real ya
    //    esta lista y le tocaria antes de medio paso, la inventada se deja y va la real (antes la real se retrasaba hasta 8 ms).
    // Y el filtro de repetidas del modo 30 con Enhanced FG (EngineLatencia.cs Pass30, repetidas_30_efg=1): tras una imagen de 3 refrescos la
    // nueva que llega a 1 pasa en cuanto llega (antes pasaba su copia, 16,7 ms tarde).
    // efg_ritmo=1 (de serie) = esto con x2; 2 = tambien con x3 / x4; 0 = lo de antes (todo igual que la 1.3). efg_ritmo_x2=2: con x2 hasta 2 inventadas en un hueco de 50 ms (1 = como mucho 1).
    // efg_colchon_ms=N (modo calidad, de serie 0): N ms mas de retraso para que tambien quepan las inventadas del principio de un hueco largo
    // (con 16,7, un hueco de 50 ms se reparte entero en x2; cuesta esos ms de latencia).
    partial class Settings
    {
        public int EfgRitmo = 1;
        public int EfgRitmoX2 = 2;
        public double EfgColchonMs = 0;
        public bool Pass30Efg = true;           // repetidas_30_efg (EngineLatencia.cs Pass30)
        // (1.3.2, ronda 9, EFGRITMO) efg_copia_pronto=1 (de serie): la copia de cada imagen a la cadena se manda en cuanto se puede (tras su
        // inventada) y no justo antes de su hora (0 = lo de la 1.3.1: a su hora menos lo que tarda la copia)
        public bool EfgCopiaPronto = true;
        public bool EfgLNuevo = true;           // efg_l_nuevo (1.3.2, EFGRITMO): L y la ventana de "lista" de nuevo tras un hueco / otro xN

        bool ApplyEfgRitmo(string key, string val)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "efg_ritmo": EfgRitmo = Math.Max(0, Math.Min(2, I(val))); return true;
                case "efg_ritmo_x2": EfgRitmoX2 = Math.Max(1, Math.Min(3, I(val))); return true;
                case "efg_colchon_ms": EfgColchonMs = Math.Max(0, Math.Min(40, D(val))); return true;
                case "repetidas_30_efg": Pass30Efg = B(val); return true;
                case "efg_copia_pronto": EfgCopiaPronto = B(val); return true;   // (1.3.2, EFGRITMO)
                case "efg_l_nuevo": EfgLNuevo = B(val); return true;   // (1.3.2, EFGRITMO)
            }
            return false;
        }
    }

    unsafe partial class Engine
    {
        // ---- la hora del contenido de cada captura que pasa (hilo de la captura, OnFrame)
        long efgLastNewAt;
        readonly long[] efgCSeq = new long[16], efgCAt = new long[16];
        int efgCI;
        long efgJobContent;

        // OnFrame, con el filtro de repetidas: cada captura (isNew = distinta de la anterior) y si pasa
        void EfgNoteCapture(long seq, long arrival, bool isNew, bool pass)
        {
            if (isNew) efgLastNewAt = arrival;
            if (!pass || s.Efg < 2) return;
            long c = isNew || efgLastNewAt == 0 ? arrival : efgLastNewAt;
            lock (efgCSeq) { efgCSeq[efgCI] = seq; efgCAt[efgCI] = c; efgCI = (efgCI + 1) % efgCSeq.Length; }
        }

        long EfgContentOf(int slot)
        {
            if (slot < 0) return Clock.Now;
            long seq = slotSeq[slot], a = slotArrival[slot];
            lock (efgCSeq)
                for (int i = 0; i < efgCSeq.Length; i++)
                    if (efgCSeq[i] == seq && efgCAt[i] != 0) return efgCAt[i] <= a ? efgCAt[i] : a;
            return a;
        }

        // ---- el estado entre reales (hilo de presentacion)
        long efgPrevRealAt;      // cuando se presento la real anterior (la llamada a Present)
        long efgLastCall;        // la ultima llamada a Present del hilo
        long efgPrevContent;     // la hora del contenido de la real anterior
        long efgRitmoN, efgRitmoGap, efgRitmoMas, efgRitmoMenos, efgRitmoSalta;   // (para el registro: cuantas, huecos irregulares, con mas / menos inventadas)

        // La hora del contenido de la real de este trabajo, con sus guardas (una copia forzada -la misma imagen que la anterior- o una hora
        // rara: la de llegada)
        long EfgJobContent(EfgJob j, bool hasPrev)
        {
            long tc = j.Content != 0 ? j.Content : j.Arrival;
            if (tc > j.Arrival || Clock.Ms(tc, j.Arrival) > j.RealMs * 1.2) tc = j.Arrival;
            if (hasPrev && efgPrevContent != 0 && Clock.Ms(efgPrevContent, tc) < j.RealMs * 0.3) tc = j.Arrival;
            return tc;
        }

        // Una imagen del plan: la hora a la que se enseña, su t (1 = la real) y si es la real
        struct EfgPlan { public long At; public float T; public bool Real; }

        // Modo calidad (la real la ultima, nReal = k - 1) con el ritmo nuevo. Devuelve false si no aplica (lo de antes)
        bool EfgDoJobRitmoCalidad(EfgJob j, int c, bool flow, bool hasPrev, int k, double step, double readyMs, bool vs, uint sy, uint fl)
        {
            long tc = EfgJobContent(j, hasPrev);
            long rp = efgPrevRealAt;
            if (!flow || !hasPrev || rp == 0 || Clock.SinceMs(rp) > j.RealMs * 4) { efgPrevContent = tc; return false; }
            // (lo minimo entre dos imagenes: un refresco de la tele, pero nunca mas que el paso: x2 del modo 60 en una tele de 60 Hz)
            double minGap = Math.Min(step * 0.9, Math.Max(step * 0.5, 1000.0 / (efgHz > 20 ? efgHz : 60) * 0.9));
            double need0 = efgFlowGpu + efgGenGpu + 0.4 + efgCopyMs;
            long now = Clock.Now;
            // la real: contenido + D (D = L + (k-1)/k del intervalo: lo de antes con el ritmo exacto) [+ el colchon]
            double D = efgLcur + (k - 1) * step + s.EfgColchonMs;
            long rc = EfgAt(tc, D);
            long early = EfgAt(now, efgCopyMs + 0.3);
            if (rc < early) { rc = early; efgLate++; }
            // (con la hora de la LLAMADA a Present: la vuelta tarda ~1 ms y con un paso de 8,3 ms se perdian inventadas)
            long lastCall = efgLastCall != 0 ? efgLastCall : efgLastPresent;
            if (rc < EfgAt(lastCall, minGap)) rc = EfgAt(lastCall, minGap);
            // (nunca mas lejos que una imagen real + 20 ms: una hora mala no deja el hilo esperando)
            if (Clock.Ms(now, rc) > j.RealMs + 20 + s.EfgColchonMs) rc = EfgAt(now, need0 + s.EfgMarginMs + (k - 1) * step);
            double I = Clock.Ms(rp, rc);
            long first = EfgAt(now, need0);
            long lastP = lastCall > rp ? lastCall : rp;
            List<EfgPlan> plan = new List<EfgPlan>(k + 2);
            if (k == 2)
            {
                // x2 (con VRR, sin sincronizacion): las que quepan a un paso, repartidas a partes iguales en el hueco. Se colocan de la
                // ultima hacia atras: la que no llega a su hora por poco (menos de medio paso: la real algo tarde) sale en cuanto se puede,
                // con el t de esa hora; las que no llegan por mas, no se hacen
                int n = (int)Math.Floor(I / step + 0.5) - 1;
                n = Math.Max(0, Math.Min(Math.Max(1, s.EfgRitmoX2), n));
                long next = rc;
                for (int q = n; q >= 1; q--)
                {
                    long at = EfgAt(rp, I * q / (n + 1));
                    if (at < first) { if (Clock.Ms(at, first) > step * 0.5) break; at = first; }
                    if (Clock.Ms(at, next) < minGap || Clock.Ms(lastP, at) < minGap) break;
                    EfgPlan e = new EfgPlan(); e.At = at; e.T = (float)Math.Max(0.02, Math.Min(0.98, Clock.Ms(rp, at) / I)); e.Real = false;
                    plan.Insert(0, e); next = at;
                }
            }
            else
            {
                // x3 / x4: a un paso hacia atras desde la real (con sincronizacion cada una a su refresco), como mucho k-1
                for (int q = 1; q <= k - 1; q++)
                {
                    long at = EfgAt(rc, -q * step);
                    if (at < first || Clock.Ms(lastP, at) < minGap) break;
                    EfgPlan e = new EfgPlan(); e.At = at; e.T = (float)Math.Max(0.02, Math.Min(0.98, Clock.Ms(rp, at) / I)); e.Real = false;
                    plan.Insert(0, e);
                }
            }
            if (Math.Abs(I - j.RealMs) > step * 0.5) efgRitmoGap++;
            if (plan.Count > k - 1) efgRitmoMas++; else if (plan.Count < k - 1) efgRitmoMenos++;
            EfgPlan er = new EfgPlan(); er.At = rc; er.T = 1; er.Real = true;
            plan.Add(er);
            efgRitmoN++;
            // el flujo y la primera inventada ya; cada siguiente, tras enseñar la anterior
            EfgFlowList(c, true);
            bool genReady = false;
            if (!plan[0].Real) { EfgGenList(c, plan[0].T, false, hasPrev, j.Reset); genReady = true; }
            for (int i = 0; i < plan.Count; i++)
            {
                EfgPlan e = plan[i];
                bool skip = !e.Real && (!genReady || Clock.SinceMs(e.At) > step * 0.5);
                if (skip) { efgSkipped++; genReady = false; }
                else
                {
                    if (!EfgShowAt(j, c, e.Real, e.At, sy, fl, i == 0 && !e.Real, e.T, readyMs, i)) return true;
                    genReady = false;
                }
                if (i + 1 < plan.Count && !plan[i + 1].Real) { EfgGenList(c, plan[i + 1].T, false, hasPrev, j.Reset); genReady = true; }
            }
            efgPrevContent = tc;
            return true;
        }

        // Modo baja latencia (la real la primera, nReal = 0) con el ritmo nuevo. Devuelve false si no aplica
        bool EfgDoJobRitmoLatencia(EfgJob j, int c, bool flow, bool hasPrev, int k, double step, double readyMs, bool vs, uint sy, uint fl, long B)
        {
            long tc = EfgJobContent(j, hasPrev);
            double ic = hasPrev && efgPrevContent != 0 ? Clock.Ms(efgPrevContent, tc) : j.RealMs;
            efgPrevContent = tc;
            if (!flow) return false;
            double icUse = Math.Max(j.RealMs, Math.Min(j.RealMs * 3, ic));
            if (icUse > j.RealMs * 1.15) efgRitmoGap++;
            efgRitmoN++;
            // la real ya (B, lo de antes)
            if (!EfgShowAt(j, c, true, B, sy, fl, false, 1, readyMs, 0)) return true;
            EfgFlowList(c, true);
            for (int i = 1; i < k; i++)
            {
                float t = (float)(1.0 + i * step / icUse);
                long ti = EfgAt(B, i * step);
                bool skip = Clock.SinceMs(ti) > step * 0.5, cede = false;
                // (otra real en la cola: lo de antes con efg_salto_fino; ademas, si ya esta lista y le tocaria antes de medio paso)
                if (!skip)
                    lock (efgJobs)
                    {
                        if (efgJobs.Count > 0)
                        {
                            EfgJob nx = efgJobs.Peek();
                            if (nx.Show || !s.EfgSaltoFino) skip = true;
                            else if (ti + (long)(step * 0.5 / Clock.TickMs) > EfgAt(nx.Arrival, efgLcur)) skip = cede = true;
                        }
                    }
                // (1.3.2, EFGRITMO) la que cede a la real siguiente no es falta de grafica: no cuenta como saltada (la escalera no la ve)
                if (skip) { if (cede) efgRitmoSalta++; else efgSkipped++; continue; }
                EfgGenList(c, t, false, hasPrev, j.Reset);
                if (!EfgShowAt(j, c, false, ti, sy, fl, i == 1, t, readyMs, i, step)) return true;
            }
            return true;
        }

        // ¿La siguiente real ya esta lista y le tocaria antes de medio paso despues de 'ti'? (entonces la inventada sobra)
        bool EfgNextRealFirst(long ti, double step)
        {
            EfgJob nx = null;
            lock (efgJobs) { if (efgJobs.Count > 0) nx = efgJobs.Peek(); }
            if (nx == null || nx.Show) return false;
            bool ready;
            try { ready = nx.Ready.WaitOne(0); } catch { return false; }
            if (!ready) return false;
            return EfgAt(nx.Arrival, efgLcur) < ti + (long)(step * 0.5 / Clock.TickMs);
        }

        // Espera la hora, copia a la cadena y presenta (como el bucle de siempre). false = la grafica se perdio (salir del trabajo).
        // watchStep > 0 (baja latencia): mientras espera, si la siguiente real ya esta lista y le tocaria antes, se deja esta inventada
        bool EfgShowAt(EfgJob j, int c, bool real, long ti, uint sy, uint fl, bool firstGen, float t, double readyMs, int idx, double watchStep = 0)
        {
            if (!s.EfgCopiaPronto)
            {
                EfgWaitUntilWatch(EfgAt(ti, -(efgCopyMs + 0.5)), watchStep > 0 ? ti : 0, watchStep);
                if (watchStep > 0 && !real && EfgNextRealFirst(ti, watchStep)) { efgRitmoSalta++; return true; }   // (no cuenta como saltada: no es la grafica, la real va antes)
            }
            EfgCopyTimed(c, !real, ti);   // (1.3.2, EFGRITMO: la copia en cuanto se puede)
            if (!real) EfgStampsRead(firstGen, true);
            if (s.EfgCopiaPronto && watchStep > 0 && !real)
            {
                // (la copia ya esta en la cadena; si mientras se espera la hora la real siguiente ya puede ir antes, esta inventada no se presenta:
                // la copia de la real escribe encima)
                EfgWaitUntilWatch(ti, ti, watchStep);
                if (EfgNextRealFirst(ti, watchStep)) { efgRitmoSalta++; return true; }
            }
            EfgWaitUntil(ti);
            long call = Clock.Now;
            int hr = swap.Present(sy, (DXGI_PRESENT)fl);
            efgLastPresent = Clock.Now; efgLastCall = call;
            if (hr < 0) { Log.Write("Enhanced FG: Present: " + Hr.Format(hr)); if (Hr.IsDeviceLost(hr)) { efgHr = hr; return false; } }
            if (real) { efgReals++; efgPrevRealAt = call; } else efgGens++;
            EfgCsvRow(j, idx, real ? 'r' : 'g', t, call, readyMs);
            return true;
        }

        // (1.3.2, ronda 9, EFGRITMO) La copia de la imagen (la real o efgGen) a la cadena para enseñarla a la hora ti, y la espera (CPU) a que la
        // grafica la acabe. Antes se mandaba a ti - (copia + 0,5 ms): si la grafica iba con la cola del motor llena (DLSS SR / DLSS 5 de la
        // real siguiente) o con el flujo y la inventada aun delante en la cola propia, la copia acababa tarde y la imagen salia tarde; y lo que
        // se tardaba se apuntaba como "copia" (1,9-5,2 ms en los logs del usuario, 0,4-0,6 ms de verdad): need0 contaba dos veces el flujo y la
        // inventada, L subia y las reales salian "tarde" (x4 calidad a 1080p: 223-300 de 300 por 10 s). Ahora (efg_copia_pronto=1) se manda en
        // cuanto se puede (la inventada ya esta pedida; efgGen solo se lee aqui y la anterior ya se presento) y "copia" solo se aprende de las
        // copias sin nada delante en la cola propia
        bool EfgCopyTimed(int c, bool gen, long ti)
        {
            if (!s.EfgCopiaPronto) EfgWaitUntil(EfgAt(ti, -(efgCopyMs + 0.5)));
            bool libre = efgFence.GetCompletedValue() >= efgFenceValue;
            long tSub = Clock.Now;
            ulong v = EfgCopyList(c, gen);
            bool ok = EfgWaitOwn(v, Math.Max(4, Clock.Ms(Clock.Now, ti) + 30));
            if (ok && (libre || !s.EfgCopiaPronto)) efgCopyMs = efgCopyMs * 0.9 + Math.Min(Clock.SinceMs(tSub), 10) * 0.1;
            return ok;
        }

        // (1.3.2, ronda 9, EFGRITMO) L (lo que tarda la real en estar + lo de la primera) y la ventana de "lista" (p90 de las ultimas 32) no se
        // reiniciaban nunca: el motor despertado de la espera (el relevo) o tras cambiar de xN / de modo / de captura empezaba con los de su
        // sesion de antes (otra resolucion, DLSS 5 si / no...) durante 32 reales (~1 s), con L demasiado alta (latencia) o demasiado baja
        // (las primeras salian tarde y se saltaban justo cuando la escalera empieza a mirar). Ahora, tras un hueco de mas de 400 ms sin reales o
        // con otro k / otra fase, se empieza de cero (efg_l_nuevo=0 = como antes)
        long efgUltJob, efgLNuevoLog; int efgUltK = -1, efgUltFase = -1;
        void EfgRitmoNuevo(EfgJob j)
        {
            if (!s.EfgLNuevo) return;
            int fase = (int)Math.Round(j.Phase * j.K);
            bool hueco = efgUltJob != 0 && Clock.Ms(efgUltJob, j.Arrival) > 400;
            if ((hueco || j.K != efgUltK || fase != efgUltFase) && efgUltK >= 0)
            {
                efgReadyN = 0; efgLcur = 0;
                if (efgLNuevoLog == 0 || Clock.SinceMs(efgLNuevoLog) > 5000) { efgLNuevoLog = Clock.Now; Log.Write("Enhanced FG: L de nuevo (" + (hueco ? "hueco de " + Clock.Ms(efgUltJob, j.Arrival).ToString("0", IC) + " ms" : "x" + efgUltK + " -> x" + j.K + ", la real en la " + (fase + 1) + "a") + ")"); }
            }
            efgUltJob = j.Arrival; efgUltK = j.K; efgUltFase = fase;
        }

        // Como EfgWaitUntil; con watchTi != 0 sale antes si la siguiente real ya puede ir (baja latencia)
        void EfgWaitUntilWatch(long at, long watchTi, double step)
        {
            if (watchTi == 0) { EfgWaitUntil(at); return; }
            while (true)
            {
                double left = -Clock.SinceMs(at);
                if (left <= 0) return;
                if (EfgNextRealFirst(watchTi, step)) return;
                if (left > 1.6) Thread.Sleep(1); else Thread.SpinWait(200);
            }
        }

        string EfgRitmoTxt()
        {
            if (s.EfgRitmo == 0 || efgRitmoN == 0) return efgRitmoSalta > 0 ? "; dejadas por la real siguiente " + efgRitmoSalta : "";   // (1.3.2, EFGRITMO: x3 / x4)
            return "; ritmo por el contenido: " + efgRitmoN + " reales, " + efgRitmoGap + " con hueco irregular (" + efgRitmoMas + " con mas inventadas, " + efgRitmoMenos +
                   " con menos" + (efgRitmoSalta > 0 ? ", " + efgRitmoSalta + " dejadas por la real siguiente" : "") + ")";
        }
    }
}
