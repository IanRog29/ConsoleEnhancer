using System;
using System.Globalization;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 22-11 (1.2.4 LATENCIA): medida de la CPU por imagen y la cola de la tele con DLSS FG y el refresco fijo.
    // - cpu_pasos=1 (solo medida): cada 20 s al log la media / p95 (ms de CPU) de cada tramo de RenderFrame: bandas (descompresion),
    //   vectores, DLSS 5, escalado (DLSS SR), lo demas (CAS, interfaz, aviso, copia a la pantalla) y el envio. Medido el 22-11 (4K, modo 30,
    //   DLSS 5 al 70 %, con y sin DLSS FG x2): bandas 1,0-1,4 ms, vectores 0,2-0,3, DLSS 5 0,6-0,7, lo demas 0,1 (2,2-2,4 en total).
    //   Probado y descartado: enviar la lista en dos trozos (tras los vectores o tras DLSS 5) para que la grafica empiece antes: 43,3 ->
    //   43,4 ms (lo que queda por grabar tras las bandas es ~1 ms de CPU y la grafica aun esta con las bandas) y tras DLSS 5 la generacion
    //   DLSS se descuadraba (pares de 150 ms): fuera.
    // - cola_tirar_fg=N (ms; de serie 0 = nunca; p. ej. 4): con DLSS FG y la sincronizacion vertical (el 60 + DLSS FG con el refresco fijo a 120 Hz,
    //   motor_60fg_refresco_fijo) cada imagen real son 2 refrescos y la captura llega a 59,94: si se juntan dos presentaciones (al arrancar,
    //   al rehacer DLSS 5 o el modelo, un tiron de la grafica) la segunda espera en la cola de la tele y esa cola solo se vacia ~1 ms por
    //   segundo (60 frente a 59,94). Se ve en el motor como Reflex durmiendo casi una imagen entera (~15 ms) mientras la imagen de la
    //   captura, ya llegada, espera a que acabe. Medido el 22-11 (1080p -> 4K, 60 + DLSS FG x2, fuente de prueba a 59,94, tele a
    //   3840x2160@120): tras arrancar la imagen esperaba 10-13 ms y bajaba 1 ms/s (20 s hasta 0,6 ms). Ahora, si 20 imagenes seguidas
    //   esperan N ms o mas con Reflex durmiendo N ms o mas y la grafica va holgada (< 85 % del hueco de una imagen real), esa imagen no se
    //   pinta (se suelta, como cola_tirar_ms con Smooth Motion): la cola baja una imagen de golpe (un tiron de una imagen a cambio de hasta
    //   16,7 ms menos durante los ~16 s siguientes). Como mucho una cada 3 s. Solo con la sincronizacion vertical (con VRR no hay esa cola).
    //   Medido el 22-11 (4 pasadas de 45 s arrancando con DLSS 5 al 50 % y M, que no caben -> 40 % y K): sin ella la espera duro 6-14 s
    //   (4-6 ms) en una de dos pasadas; con 4, 3 imagenes tiradas por pasada y la espera fuera en ~2 s; la media de la pasada entera no
    //   cambia de forma clara (16,1 / 15,3 frente a 13,9 / 16,5 ms de la llegada a verse la real). Pasaba en el arranque, con la grafica
    //   justa: de serie apagada hasta probarlo jugando (riesgo: un tiron de una imagen por cada una tirada)
    unsafe partial class Engine
    {
        const int CpuSecs = 6;   // 0 bandas, 1 vectores, 2 DLSS 5, 3 escalado, 4 lo demas, 5 cerrar y enviar
        static readonly string[] CpuNames = { "bandas", "vectores", "dlss5", "escalado", "resto", "envio" };
        readonly double[][] cpuLog = new double[CpuSecs][];
        int cpuN;
        long cpuT, cpuLogAt;

        void CpuBegin()
        {
            if (!s.CpuSections) return;
            cpuT = Clock.Now;
        }

        void CpuMark(int k)
        {
            if (!s.CpuSections || cpuT == 0) return;
            long now = Clock.Now;
            if (cpuLog[k] == null) for (int i = 0; i < CpuSecs; i++) cpuLog[i] = new double[1200];
            if (cpuN < 1200) cpuLog[k][cpuN] = Clock.Ms(cpuT, now);
            cpuT = now;
        }

        void CpuEnd()
        {
            if (!s.CpuSections || cpuT == 0) return;
            CpuMark(5);
            cpuT = 0;
            if (cpuN < 1200) cpuN++;
            if (cpuLogAt == 0) cpuLogAt = Clock.Now;
            if (Clock.SinceMs(cpuLogAt) < 20000 || cpuN == 0) return;
            System.Text.StringBuilder sb = new System.Text.StringBuilder("CPU por imagen (" + cpuN + " imagenes, ms media/p95):");
            double tot = 0;
            for (int k = 0; k < CpuSecs; k++)
            {
                double[] v = new double[cpuN];
                Array.Copy(cpuLog[k], v, cpuN);
                Array.Sort(v);
                double sum = 0; foreach (double x in v) sum += x;
                tot += sum / cpuN;
                sb.AppendFormat(CultureInfo.InvariantCulture, " {0} {1:0.00}/{2:0.00}", CpuNames[k], sum / cpuN, v[(int)(0.95 * (cpuN - 1))]);
            }
            sb.AppendFormat(CultureInfo.InvariantCulture, " | total {0:0.00}", tot);
            Log.Write(sb.ToString());
            cpuN = 0; cpuLogAt = Clock.Now;
        }

        // ---- cola_tirar_fg
        uint fqSync;          // la sincronizacion del ultimo Present (VrrPresentArgs)
        int fqRun, fqDrops;
        long fqLastDrop;
        double fqGpuEma;

        // LatPresented: la grafica de las imagenes reales (media movil)
        void FgQueueGpu(bool real, double gpuMs)
        {
            if (!real || gpuMs <= 0 || gpuMs > 200) return;
            fqGpuEma = fqGpuEma <= 0 ? gpuMs : fqGpuEma * 0.9 + gpuMs * 0.1;
        }

        // LatSkip: true = esta imagen no se pinta (la cola de la tele baja una)
        bool FgQueueSkip(int slot)
        {
            double n = s.FgQueueDropMs;
            if (n <= 0 || !fgApplied || fqSync == 0) { fqRun = 0; return false; }
            double pick = Clock.Ms(slotArrival[slot], latPickup);
            double budget = s.Mode == 40 ? ModoPeriodoMs : (s.Mode == 30 ? 2000.0 : 1000.0) / 59.94;   // (1.3.2, MODO40)
            if (pick >= n && latSleepMs >= n && fqGpuEma > 0 && fqGpuEma < budget * 0.85) fqRun++; else fqRun = 0;
            if (fqRun < 20) return false;
            if (fqLastDrop != 0 && Clock.SinceMs(fqLastDrop) < 3000) return false;
            fqRun = 0; fqLastDrop = Clock.Now; fqDrops++;
            if (fqDrops <= 20 || fqDrops % 50 == 0)
                Log.Write(string.Format(CultureInfo.InvariantCulture,
                    "Cola de la tele (DLSS FG con sincronizacion): la imagen esperaba {0:0.0} ms con Reflex durmiendo {1:0.0}: se tira una para vaciarla ({2} en total; cola_tirar_fg=0 = nunca)",
                    pick, latSleepMs, fqDrops));
            return true;
        }
    }

    partial class Settings
    {
        public bool CpuSections = false;   // cpu_pasos=1 (EngineEnvio.cs)
        public double FgQueueDropMs = 0;   // cola_tirar_fg=N ms (EngineEnvio.cs; 0 = nunca, de serie)

        bool ApplyEnvio(string key, string val)
        {
            switch (key)
            {
                case "cpu_pasos": CpuSections = B(val); return true;
                case "cola_tirar_fg": FgQueueDropMs = Math.Max(0, Math.Min(30, D(val))); return true;
            }
            return false;
        }
    }
}
