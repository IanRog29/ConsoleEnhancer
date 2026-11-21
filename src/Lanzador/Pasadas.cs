// Pasadas.cs - 18-11-2026 (agente PASADAS): DLSS 5 con 2-3 pasadas. El usuario: "con 2 o 3 pasadas a veces se buguea viendo la imagen y se
// pone a parpadear (una locura); se arregla quitando y poniendo DLSS 5 (boton A)". Quitar y poner = Magpie (y mpv) desde cero.
//  - Vigilancia (ini pasadas_vigilancia=1; 0 = nada): cada 5 s se mira lo nuevo del log de Magpie. Con 2-3 pasadas, si la cadena de
//    DLSSNR se para ("DLSSNR chain failed": con mas de una pasada Magpie la deja parada hasta reabrirlo, DLSSNRFilter.cpp impl.disabled),
//    si DLSSNR da errores seguidos o si el historial de DLSS 5 se reinicia sin parar (cortes de la captura / "history reset"), se hace
//    sola la cura del usuario: Magpie se reabre con el puente (el motor encima, sin telon). Como mucho cada 90 s y 3 veces por sesion.
//    Todo lo raro se apunta en switch2.log ("Vigilancia de DLSS 5: ...") aunque no llegue a reabrir. Los cortes de la captura por mover el
//    raton sobre la imagen solo se apuntan (reabrir no los quita); pasadas_vigilancia=2 los cuenta tambien (prueba sin consola).
//  - Diagnostico: al quitar DLSS 5 con el boton A (o cambiar de pasadas) con 2-3 pasadas, o antes de la cura automatica, el log de Magpie
//    de esa imagen se guarda en games\debug\magpie (los 12 ultimos): Magpie solo deja ~1 MB de log (magpie.log + magpie.1.log, rota por
//    tamaño) y unas pocas reaperturas lo pisan (el del fallo del 18-11 01:31 ya no existia). Y si el usuario quita y pone DLSS 5 en menos de
//    20 s, se dice en el log (el "gesto de cura").
// Lo compila build.bat con el resto (partes de TrayApp). C# 5
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ConsoleEnhancer
{
    partial class TrayApp
    {
        // ---- Lo que se lleva visto del log de Magpie (de la imagen de ahora) ----
        readonly object d5wSync = new object();
        int d5wPid = -1;                    // el Magpie cuyo log se mira
        long d5wPos;                        // hasta donde se ha leido
        DateTime d5wPidSince = DateTime.MinValue;
        DateTime d5wStartTs = DateTime.MinValue;   // la primera hora del log de este Magpie (su arranque)
        int d5wPendingMouse;                       // cortes de la captura por el raton cuyo reinicio del historial aun no se ha visto
        readonly List<KeyValuePair<DateTime, int>> d5wEvents = new List<KeyValuePair<DateTime, int>>(); // (hora, tipo) de los ultimos 30 s
        const int D5wChain = 0, D5wError = 1, D5wReset = 2, D5wCut = 3, D5wFg = 4, D5wMouse = 5;
        int d5wTotalReset, d5wTotalCut, d5wTotalErr, d5wTotalFg;   // del Magpie de ahora (para el diagnostico)
        DateTime d5wLastCure = DateTime.MinValue, d5wLastNote = DateTime.MinValue;
        int d5wCures;
        DateTime d5wOffAt = DateTime.MinValue;  // ultima vez que se quito DLSS 5 con el boton A con 2-3 pasadas
        string d5wOffLog;                       // y el log de Magpie que se guardo entonces

        // Desde CheckLsPacing (cada 5 s con Magpie): lo nuevo de su log
        void PasadasWatch(DateTime now)
        {
            try
            {
                PasadasImageStart(); // (la vigilancia por la imagen, su hilo: una vez)
                if (cfg.D5Watch <= 0 || userExit) return;
                int pid = MagpieLive.Pid();
                if (pid <= 0) return;
                string text = PasadasReadNew(pid, now);
                if (text == null) return;
                string lastErr = null;
                lock (d5wSync)
                {
                    foreach (string raw in text.Split('\n'))
                    {
                        string line = raw.TrimEnd('\r');
                        if (line.Length == 0) continue;
                        // La hora de la linea (las de los primeros 20 s de este Magpie no cuentan: al arrancar la captura y el historial se
                        // reinician a proposito; tampoco las de lo leido de golpe si ya son viejas)
                        // (el log de Magpie va seguido de un arranque a otro y rota por tamaño: lo de antes de abrirse este no cuenta)
                        DateTime ts;
                        if (line.Length < 23 || !DateTime.TryParseExact(line.Substring(0, 23), "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out ts))
                            continue;
                        if (ts < d5wStartTs) continue;
                        int kind = -1;
                        if (line.Contains("DLSSNR chain failed")) { kind = D5wChain; lastErr = line; }
                        else if (line.Contains("|error|") && (line.Contains("DLSSNR") || line.Contains("Native effect Draw failed") || line.Contains("FrameGuidance")))
                        { kind = D5wError; lastErr = line; }
                        // Cada reinicio del historial de Magpie escribe "Frame Guidance history reset" (tambien los de quitarle el primer plano
                        // a mpv, sin linea de corte de la WGC). Los del raton ("WGC capture interrupted ... reason=cursor visible": Magpie rehace
                        // la captura al enseñar el cursor; reabrir no los quita) van aparte, salvo con pasadas_vigilancia=2 (prueba sin consola)
                        else if (line.Contains("Frame Guidance history reset"))
                        {
                            if (d5wPendingMouse > 0 && cfg.D5Watch < 2) { d5wPendingMouse--; kind = D5wMouse; }
                            else kind = D5wReset;
                        }
                        else if (line.Contains("WGC capture interrupted") && !line.Contains("reason=initial start"))
                        {
                            if (line.Contains("reason=cursor visible")) d5wPendingMouse = Math.Min(d5wPendingMouse + 1, 3);
                            kind = D5wCut;
                        }
                        else if (line.Contains("Source foreground changed")) kind = D5wFg;
                        if (kind < 0) continue;
                        switch (kind)
                        {
                            case D5wChain: case D5wError: d5wTotalErr++; break;
                            case D5wReset: case D5wMouse: d5wTotalReset++; break;
                            case D5wCut: d5wTotalCut++; break;
                            default: d5wTotalFg++; break;
                        }
                        if ((ts - d5wStartTs).TotalSeconds < 20) continue;
                        d5wEvents.Add(new KeyValuePair<DateTime, int>(ts, kind));
                    }
                    d5wEvents.RemoveAll(delegate (KeyValuePair<DateTime, int> e) { return (now - e.Key).TotalSeconds > 20.5; });
                }
                PasadasDecide(now, lastErr);
            }
            catch (Exception ex) { Log.Error("Vigilancia de DLSS 5", ex); }
        }

        // Lo nuevo del log de Magpie desde la ultima vez (un Magpie nuevo: su log entero, Magpie lo empieza al abrirse)
        string PasadasReadNew(int pid, DateTime now)
        {
            // (pasadas_vigilancia_prueba=<fichero>, solo con la fuente de prueba: se mira ese fichero en vez del log de Magpie)
            bool test = cfg.TestSource.Length > 0 && cfg.D5WatchTestLog.Length > 0;
            string path = test ? cfg.D5WatchTestLog : Paths.MagpieLog;
            if (!File.Exists(path)) return null;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                lock (d5wSync)
                {
                    if (pid != d5wPid)
                    {
                        d5wPid = pid;
                        // (desde como mucho 1 MB antes del final: lo de este Magpie; lo de antes de abrirse se salta por la hora)
                        if (!test) d5wPos = Math.Max(0, fs.Length - 1024 * 1024);
                        d5wPidSince = now;
                        double up = MagpieLive.RunningFor();
                        d5wStartTs = test ? now.AddSeconds(-20) : now.AddSeconds(-(up > 0 ? up : 0) - 1);
                        d5wEvents.Clear();
                        d5wTotalReset = d5wTotalCut = d5wTotalErr = d5wTotalFg = 0;
                        d5wPendingMouse = 0;
                    }
                    if (d5wPos > fs.Length) d5wPos = 0; // (rotado)
                }
                long pos = d5wPos;
                long n = Math.Min(fs.Length - pos, 4L * 1024 * 1024);
                if (n <= 0) return "";
                fs.Seek(pos, SeekOrigin.Begin);
                byte[] buf = new byte[n];
                int got = 0, r;
                while (got < buf.Length && (r = fs.Read(buf, got, buf.Length - got)) > 0) got += r;
                int end = Array.LastIndexOf(buf, (byte)'\n', Math.Max(0, got - 1));
                if (end < 0) return "";
                lock (d5wSync) d5wPos = pos + end + 1;
                return Encoding.UTF8.GetString(buf, 0, end + 1);
            }
        }

        void PasadasDecide(DateTime now, string lastErr)
        {
            int chain = 0, err = 0, reset = 0, cut = 0, fg = 0, mouse = 0;
            lock (d5wSync)
                foreach (KeyValuePair<DateTime, int> e in d5wEvents)
                    switch (e.Value)
                    {
                        case D5wChain: chain++; break;
                        case D5wError: err++; break;
                        case D5wReset: reset++; break;
                        case D5wCut: cut++; break;
                        case D5wMouse: mouse++; break;
                        default: fg++; break;
                    }
            // (los primeros 12 s de cada Magpie no cuentan: al arrancar la captura y el historial se reinician a proposito)
            bool settled = (now - d5wPidSince).TotalSeconds >= 12 && MagpieLive.RunningFor() >= 12;
            int passes = cfg.Dlss5Off || !cfg.Dlss5FitsNow ? 0 : cfg.Dlss5PassesNow;
            if (!settled || passes < 2) return;
            string why = null;
            if (chain > 0) why = "la cadena de DLSSNR de " + passes + " pasadas se ha parado (Magpie no la reanuda sin reabrirlo)";
            else if (err >= 3) why = err + " errores de DLSS 5 en 20 s";
            else if (reset >= cfg.D5WatchResets) why = "el historial de DLSS 5 se ha reiniciado " + reset + " veces en 20 s (" + cut + " cortes de la captura, primer plano cambiado " + fg + " veces)";
            // Lo raro que aun no llega: solo al log (una vez cada 60 s)
            if (why == null)
            {
                if ((reset >= 4 || mouse >= 4 || err > 0 || fg >= 8) && (now - d5wLastNote).TotalSeconds >= 60)
                {
                    d5wLastNote = now;
                    Log.Write("Vigilancia de DLSS 5 (" + passes + " pasadas): en 20 s " + (reset + mouse) + " reinicios del historial (" + cut + " cortes de la captura" +
                              (mouse > 0 ? ", " + mouse + " por el raton movido sobre la imagen: eso no se arregla reabriendo" : "") + "), " +
                              err + " errores, primer plano cambiado " + fg + " veces (delante: " + FgOwner() + ")" +
                              (lastErr != null ? "; el ultimo error: " + Short(lastErr) : ""));
                }
                return;
            }
            PasadasCure("Vigilancia de DLSS 5", why + (lastErr != null ? " (el ultimo: " + Short(lastErr) + ")" : ""), "vigilancia");
        }

        // La cura del usuario (quitar y poner DLSS 5 con A): Magpie se reabre con el puente. Como mucho cada 90 s y 3 veces por sesion,
        // contando las de la vigilancia del log y las de la imagen. false = no se ha podido o no tocaba
        readonly object d5wCureSync = new object();
        bool PasadasCure(string who, string why, string tag)
        {
            DateTime now = DateTime.Now;
            if (!lsReady || passesBusy || relevo != null || homeBusy || relaunchSize != null || relaunchEngine || Config.EngineActive || !MagpieLive.IsScaling()) return false;
            lock (d5wCureSync)
            {
                if ((now - d5wLastCure).TotalSeconds < 90 || d5wCures >= 3)
                {
                    if ((now - d5wLastNote).TotalSeconds >= 60)
                    {
                        d5wLastNote = now;
                        Log.Write(who + ": " + why + "; NO se reabre (" + (d5wCures >= 3 ? "ya 3 veces en esta sesion" : "hace menos de 90 s de la anterior") + ")");
                    }
                    return false;
                }
                d5wLastCure = now;
                d5wCures++;
            }
            string saved = PasadasArchive(tag);
            Log.Write(who + ": " + why + ": se reabre Magpie como al quitar y poner DLSS 5 (" + d5wCures + " de 3 en esta sesion)" + (saved != null ? "; su log en " + saved : ""));
            lock (d5wSync) d5wEvents.Clear();
            RestartLs("DLSS 5 · recuperando la imagen", Dlss5PassText(), true);
            return true;
        }

        static string Short(string line)
        {
            string[] f = line.Split('|');
            string s = f.Length >= 5 ? f[0].Length >= 23 ? f[0].Substring(11, 12) + " " + f[4] : f[4] : line;
            return s.Length > 200 ? s.Substring(0, 200) : s;
        }

        // ToggleDlss5 (boton A), antes de cambiar nada (cfg aun con la imagen de ahora). nowOn: lo que se va a poner
        void PasadasOnToggle(bool nowOn)
        {
            try
            {
                if (!nowOn)
                {
                    int passes = cfg.Dlss5Off || !cfg.Dlss5FitsNow ? 0 : cfg.Dlss5PassesNow;
                    if (passes < 2 || !MagpieLive.Running) { d5wOffAt = DateTime.MinValue; return; }
                    d5wOffAt = DateTime.Now;
                    d5wOffLog = null;
                    string summary = PasadasSummary();
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        string saved = PasadasArchive("quitado");
                        d5wOffLog = saved;
                        if (saved != null) Log.Write("DLSS 5 quitado con " + Pasadas(passes) + ": el log de Magpie de esa imagen guardado en " + saved + summary);
                    });
                }
                else if (d5wOffAt != DateTime.MinValue)
                {
                    double s = (DateTime.Now - d5wOffAt).TotalSeconds;
                    d5wOffAt = DateTime.MinValue;
                    if (s <= 20)
                        Log.Write("DLSS 5 quitado y puesto en " + Math.Round(s, 1).ToString(CultureInfo.InvariantCulture) + " s con " + Pasadas(dlss5Passes) +
                                  " (lo que hace el usuario cuando la imagen se estropea): el log de Magpie de la imagen de antes en " + (d5wOffLog ?? "(no se pudo guardar)"));
                }
            }
            catch (Exception ex) { Log.Error("Vigilancia de DLSS 5 (boton A)", ex); }
        }

        // SetPasses: se deja de usar 2-3 pasadas (el usuario puede estar huyendo del fallo): su log se guarda
        void PasadasOnPasses(int from, int to)
        {
            try
            {
                if (from < 2 || from == to || cfg.Dlss5Off || !MagpieLive.Running) return;
                string summary = PasadasSummary();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string saved = PasadasArchive("pasadas" + from + "a" + to);
                    if (saved != null) Log.Write("DLSS 5 de " + Pasadas(from) + " a " + Pasadas(to) + ": el log de Magpie de esa imagen guardado en " + saved + summary);
                });
            }
            catch (Exception ex) { Log.Error("Vigilancia de DLSS 5 (pasadas)", ex); }
        }

        // Lo visto del Magpie de ahora (para el log)
        string PasadasSummary()
        {
            lock (d5wSync)
            {
                if (d5wPid <= 0 || d5wPid != MagpieLive.Pid()) return "";
                int secs = (int)(DateTime.Now - d5wPidSince).TotalSeconds;
                return " (en " + secs + " s: " + d5wTotalReset + " reinicios del historial, " + d5wTotalCut + " cortes de la captura, " +
                       d5wTotalErr + " errores, primer plano cambiado " + d5wTotalFg + " veces)";
            }
        }

        // Copia del log de Magpie (el de la imagen de ahora) en games\debug\magpie; se quedan los 12 ultimos. null = no se pudo
        static string PasadasArchive(string why)
        {
            try
            {
                if (!File.Exists(Paths.MagpieLog)) return null;
                string dir = Path.Combine(Paths.Datos, @"games\debug\magpie");
                Directory.CreateDirectory(dir);
                string dst = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "_" + why + ".log");
                // (Magpie escribe seguido de un arranque a otro y rota por tamaño, ~500 KB, a magpie.1.log: los dos, el viejo antes)
                string old = Path.Combine(Path.GetDirectoryName(Paths.MagpieLog), "magpie.1.log");
                using (FileStream o = new FileStream(dst, FileMode.Create, FileAccess.Write))
                    foreach (string f in new[] { old, Paths.MagpieLog })
                    {
                        if (!File.Exists(f)) continue;
                        using (FileStream src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            src.CopyTo(o);
                    }
                string[] all = Directory.GetFiles(dir, "*.log");
                Array.Sort(all, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i + 12 < all.Length; i++) try { File.Delete(all[i]); } catch { }
                return dst;
            }
            catch (Exception ex) { Log.Error("Guardando el log de Magpie", ex); return null; }
        }

        // ================================================================================== 18-11 (PASADAS): el parpadeo, por la imagen
        // En la sesion del usuario (Arceus, 01:27-01:34) el log de Magpie no tenia ni un error: la vigilancia del log no habria saltado. Esta
        // mira la imagen: con 2-3 pasadas, Magpie delante y pasados 20 s de su arranque, cada 10 s 7 copias pequeñas seguidas del centro de la
        // pantalla (GDI BitBlt + CAPTUREBLT de 960x540, como el telon congelado; cada copia espera a la siguiente imagen de DWM: salen cada
        // 15-30 ms) reducidas a 240x135 (media de 4x4 puntos). Por punto, en cada terna (i, i+p, i+2p; p = 1 y 2): osc = max(0, min(d01, d12)
        // - d02) (A -> B -> A da osc alto; el movimiento normal, d02 >= d01) y "va y vuelve" si min(d01, d12) >= 4 y d02 <= 0,3 * min. De la
        // peor terna: osc medio y % de puntos que van y vuelven. MEDIDO 18-11 (modo 30, Smooth Motion, 3 pasadas, imagen de prueba de Ocarina):
        //   quieta con grano 0,014-0,016 / 0 %; en movimiento (desplazandose, con grano y HUD) 0,74-1,58 / 5,8-7,9 %; parpadeo de la entrada
        //   del 6 % 0,35-3,9 / 1,6-42 %; del 15 % (lo de "una locura") 18-23 / 76-94 % (5 de 6 muestras; la otra cayo en imagenes inventadas
        //   por Smooth Motion: 0,6 / 5 %). Umbral de serie osc >= 4 y >= 25 % (ini pasadas_vigilancia_imagen_umbral / _pct).
        // Con una muestra por encima: 2 mas a 1,5 s; con 2 de 3, la entrada (4 imagenes seguidas de mpv por su script, switch2-burst, el
        // mismo centro): si tambien parpadea (la mitad o mas), es el juego y no se toca nada; si no, la cura (PasadasCure). Copias medidas
        // con PresentMon: sin efecto en lo que llega a la tele (cada 2 s: p99 16,83 frente a 16,9 ms sin copias, Independent Flip igual).
        // ini pasadas_vigilancia_imagen=1 (0 = no; 2 = sin mirar la entrada, solo pruebas)
        static class D5Gdi
        {
            [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
            [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
            [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
            [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
            [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
            [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
            [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
            [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader bmi, uint usage, out IntPtr bits, IntPtr sec, uint off);
            [StructLayout(LayoutKind.Sequential)]
            public struct BitmapHeader { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        }

        const int D5iW = 960, D5iH = 540, D5iBlock = 4;   // el centro de la pantalla de 4K (una novena parte) -> 240x135
        volatile bool d5iStarted;
        int d5iLastPid = -1;
        DateTime d5iGameNote = DateTime.MinValue;   // la ultima vez que el parpadeo era del juego (no se vuelve a mirar en 60 s)
        readonly AutoResetEvent d5iBurstEvt = new AutoResetEvent(false);
        string d5iBurstPrefix; int d5iBurstN, d5iBurstW, d5iBurstH, d5iBurstStride; string d5iBurstFmt;

        // Desde PasadasWatch: el hilo de las muestras, una vez
        void PasadasImageStart()
        {
            if (d5iStarted || cfg.D5WatchImage <= 0) return;
            d5iStarted = true;
            Thread t = new Thread(PasadasImageLoop);
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Name = "Vigilancia de DLSS 5 (imagen)";
            t.Start();
        }

        // ¿Se mira ahora? (2-3 pasadas, Magpie escalando delante, nada cambiando, 20 s desde que se abrio)
        bool PasadasImageNow()
        {
            if (userExit || cfg.D5WatchImage <= 0) return false;
            int passes = cfg.Dlss5Off || !cfg.Dlss5FitsNow ? 0 : cfg.Dlss5PassesNow;
            if (passes < 2 || cfg.HomeNative || InHome) return false;
            if (!lsReady || passesBusy || relevo != null || homeBusy || relaunchSize != null || relaunchEngine || Config.EngineActive) return false;
            if (splash != null || (curtain != null && !curtain.IsDisposed)) return false;
            IntPtr img = mpvWindow;
            if (img == IntPtr.Zero || GetForegroundWindow() != img) return false;
            return MagpieLive.IsScaling() && MagpieLive.RunningFor() >= 20;
        }

        void PasadasImageLoop()
        {
            while (!userExit)
            {
                try
                {
                    Thread.Sleep(10000);
                    if (!PasadasImageNow() || (DateTime.Now - d5iGameNote).TotalSeconds < 60) continue;
                    int pid = MagpieLive.Pid();
                    double osc, pct, d01;
                    double ms;
                    if (!PasadasImageSample(out osc, out pct, out d01, out ms)) continue;
                    if (pid != d5iLastPid) { d5iLastPid = pid; Log.Write("Vigilancia de DLSS 5 (imagen): primera muestra de este Magpie: osc " + F2(osc) + " / " + F1(pct) + " % (copias " + F1(ms) + " ms en total)"); }
                    if (!PasadasImageBad(osc, pct)) continue;
                    // Una por encima: 2 mas, seguidas
                    int bad = 1;
                    double worstOsc = osc, worstPct = pct;
                    StringBuilder sb = new StringBuilder(F2(osc) + "/" + F1(pct) + "%");
                    for (int k = 0; k < 2; k++)
                    {
                        Thread.Sleep(1500);
                        if (!PasadasImageNow()) break;
                        double o2, p2, d2, m2;
                        if (!PasadasImageSample(out o2, out p2, out d2, out m2)) break;
                        sb.Append(", ").Append(F2(o2)).Append('/').Append(F1(p2)).Append('%');
                        if (PasadasImageBad(o2, p2)) { bad++; worstOsc = Math.Max(worstOsc, o2); worstPct = Math.Max(worstPct, p2); }
                    }
                    if (bad < 2)
                    {
                        Log.Write("Vigilancia de DLSS 5 (imagen): 1 muestra de 3 con parpadeo (osc/puntos que van y vuelven: " + sb + "): nada");
                        continue;
                    }
                    // La entrada: ¿parpadea el juego?
                    string input = "sin mirar la entrada (pasadas_vigilancia_imagen=2)";
                    if (cfg.D5WatchImage < 2)
                    {
                        double io, ip;
                        string why;
                        if (!PasadasInputOsc(out io, out ip, out why)) input = "la entrada no se pudo mirar (" + why + ")";
                        else if (io >= 0.5 * worstOsc || ip >= 0.5 * worstPct)
                        {
                            d5iGameNote = DateTime.Now;
                            Log.Write("Vigilancia de DLSS 5 (imagen): parpadeo en la salida (" + sb + ") pero la entrada tambien parpadea (osc " + F2(io) + " / " + F1(ip) +
                                      " %): es el juego, no se toca nada (otra vez dentro de 60 s)");
                            continue;
                        }
                        else input = "la entrada no (osc " + F2(io) + " / " + F1(ip) + " %)";
                    }
                    PasadasCure("Vigilancia de DLSS 5 (imagen)", "la imagen de Magpie parpadea (" + bad + " de 3 muestras, osc/puntos que van y vuelven: " + sb +
                                "; umbral " + F2(cfg.D5ImageOsc) + "/" + F1(cfg.D5ImagePct) + " %), " + input, "imagen");
                }
                catch (ThreadInterruptedException) { return; }
                catch (Exception ex) { Log.Error("Vigilancia de DLSS 5 (imagen)", ex); }
            }
        }

        bool PasadasImageBad(double osc, double pct) { return osc >= cfg.D5ImageOsc && pct >= cfg.D5ImagePct; }
        static string F2(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }
        static string F1(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

        // 7 copias seguidas del centro de la pantalla y la peor terna. false = no se pudo
        static bool PasadasImageSample(out double osc, out double pct, out double d01, out double totalMs)
        {
            osc = pct = d01 = totalMs = 0;
            System.Drawing.Rectangle b = Screen.PrimaryScreen.Bounds;
            int w = Math.Min(D5iW, b.Width), h = Math.Min(D5iH, b.Height);
            int x0 = b.X + (b.Width - w) / 2, y0 = b.Y + (b.Height - h) / 2;
            List<float[]> shots = new List<float[]>();
            IntPtr scr = D5Gdi.GetDC(IntPtr.Zero);
            if (scr == IntPtr.Zero) return false;
            IntPtr mem = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                mem = D5Gdi.CreateCompatibleDC(scr);
                D5Gdi.BitmapHeader bi = new D5Gdi.BitmapHeader();
                bi.biSize = 40; bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32;
                IntPtr bits;
                dib = D5Gdi.CreateDIBSection(mem, ref bi, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero || bits == IntPtr.Zero) return false;
                old = D5Gdi.SelectObject(mem, dib);
                byte[] raw = new byte[w * h * 4];
                for (int k = 0; k < 7; k++)
                {
                    if (!D5Gdi.BitBlt(mem, 0, 0, w, h, scr, x0, y0, 0x00CC0020 | 0x40000000)) return false; // SRCCOPY | CAPTUREBLT
                    Marshal.Copy(bits, raw, 0, raw.Length);
                    shots.Add(D5Reduce(raw, w, h, w * 4, D5iBlock, false, 0, 0));
                }
            }
            finally
            {
                if (old != IntPtr.Zero) D5Gdi.SelectObject(mem, old);
                if (dib != IntPtr.Zero) D5Gdi.DeleteObject(dib);
                if (mem != IntPtr.Zero) D5Gdi.DeleteDC(mem);
                D5Gdi.ReleaseDC(IntPtr.Zero, scr);
            }
            totalMs = sw.Elapsed.TotalMilliseconds;
            D5Worst(shots, new[] { 1, 2 }, out osc, out pct, out d01);
            return true;
        }

        // La peor terna (osc medio mayor) con esos pasos
        static void D5Worst(List<float[]> shots, int[] steps, out double osc, out double pct, out double d01)
        {
            osc = pct = d01 = 0;
            foreach (int step in steps)
                for (int i = 0; i + 2 * step < shots.Count; i++)
                {
                    float[] a = shots[i], bb = shots[i + step], c = shots[i + 2 * step];
                    double s = 0, sd = 0; int big = 0;
                    for (int p = 0; p < a.Length; p++)
                    {
                        float x01 = Math.Abs(bb[p] - a[p]), x12 = Math.Abs(c[p] - bb[p]), x02 = Math.Abs(c[p] - a[p]);
                        float mn = Math.Min(x01, x12), o = mn - x02;
                        if (o > 0) s += o;
                        sd += x01;
                        if (mn >= 4 && x02 <= 0.3f * mn) big++;
                    }
                    double m = s / a.Length;
                    if (m > osc) { osc = m; pct = 100.0 * big / a.Length; d01 = sd / a.Length; }
                }
        }

        // Luma media por bloques de un trozo (x0, y0, w, h) de una imagen de 4 bytes por punto (BGRx; rgb = RGBx)
        static float[] D5Reduce(byte[] raw, int w, int h, int stride, int block, bool rgb, int x0, int y0)
        {
            int bw = w / block, bh = h / block;
            float[] r = new float[bw * bh];
            float inv = 1f / (block * block * 256f);
            int cb = rgb ? 2 : 0, cr = rgb ? 0 : 2;
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int s = 0;
                    for (int y = by * block; y < by * block + block; y++)
                    {
                        int o = (y0 + y) * stride + (x0 + bx * block) * 4;
                        for (int x = 0; x < block; x++, o += 4) s += raw[o + cb] * 29 + raw[o + 1] * 150 + raw[o + cr] * 77;
                    }
                    r[by * bw + bx] = s * inv;
                }
            return r;
        }

        // La entrada: 4 imagenes seguidas de mpv (su script, como el diagnostico del tramado), el mismo centro (la cuarta parte de ancho y
        // alto), la peor terna con paso 1 (cada imagen ya es nueva: el filtro de repetidas de mpv quita las iguales)
        bool PasadasInputOsc(out double osc, out double pct, out string why)
        {
            osc = pct = 0;
            why = null;
            if (!ipcUp || burstBusy) { why = !ipcUp ? "sin enlace con mpv" : "otra tanda en marcha"; return false; }
            string prefix = Path.Combine(Path.GetTempPath(), "switch2_pasadas_" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture));
            burstBusy = true;
            d5iBurstEvt.Reset();
            d5iBurstPrefix = null;
            lock (cmdQueue) cmdQueue.Add(JsonCmd("script-message", "switch2-burst", prefix, "4"));
            bool got = d5iBurstEvt.WaitOne(4000);
            burstBusy = false;
            List<string> files = new List<string>();
            for (int i = 0; i < 4; i++) files.Add(prefix + "_" + i + ".raw");
            try
            {
                if (!got || d5iBurstPrefix != prefix) { why = "mpv no las dio en 4 s"; return false; }
                int w = d5iBurstW, h = d5iBurstH, stride = d5iBurstStride;
                bool rgb = d5iBurstFmt != null && d5iBurstFmt.StartsWith("rgb", StringComparison.Ordinal);
                if (d5iBurstN < 3 || w < 64 || h < 64 || stride < w * 4) { why = d5iBurstN + " imagenes de " + w + "x" + h; return false; }
                int rw = w / 4, rh = h / 4, block = Math.Max(1, rw / (D5iW / D5iBlock));
                List<float[]> shots = new List<float[]>();
                for (int i = 0; i < d5iBurstN; i++)
                {
                    byte[] data = File.ReadAllBytes(files[i]);
                    if (data.Length < (long)stride * h) { why = "imagen corta"; return false; }
                    shots.Add(D5Reduce(data, rw, rh, stride, block, rgb, (w - rw) / 2, (h - rh) / 2));
                }
                double d;
                D5Worst(shots, new[] { 1 }, out osc, out pct, out d);
                return true;
            }
            catch (Exception ex) { why = ex.Message; return false; }
            finally { foreach (string f in files) try { if (File.Exists(f)) File.Delete(f); } catch { } }
        }

        // Desde el enlace de mpv ("switch2-burst-done"): true = era de la vigilancia (la tanda con el prefijo de arriba)
        bool PasadasBurstDone(string prefix, int n, int w, int h, int stride, string fmt)
        {
            if (prefix == null || prefix.IndexOf("switch2_pasadas_", StringComparison.Ordinal) < 0) return false;
            d5iBurstN = n; d5iBurstW = w; d5iBurstH = h; d5iBurstStride = stride; d5iBurstFmt = fmt;
            d5iBurstPrefix = prefix;
            d5iBurstEvt.Set();
            return true;
        }
    }
}
