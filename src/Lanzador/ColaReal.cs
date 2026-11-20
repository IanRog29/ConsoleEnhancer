using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace ConsoleEnhancer
{
    // 19-11 noche (LATENCIA3, 3er intento): vaciar la cola de Smooth Motion del modo 30 con la cola REAL, medida en vivo con PresentMon
    // (ETW sin administrador: el usuario esta en "Usuarios del registro de rendimiento"). Solo Magpie.exe, solo mientras hay Smooth
    // Motion en el modo 30. De cada imagen real de Magpie (MsBetweenPresents > 4: las inventadas del driver van pegadas) se lee
    // MsUntilDisplayed (de presentar a verse); cola = mediana de ~1,5 s / (1000 / (Hz / 4)). Si pasa de magpie_sm_vaciar_real_alto
    // (1,5 imagenes) se pausa mpv (cola - magpie_sm_vaciar_real_objetivo) imagenes (objetivo 1 = una de colchon para los parones de
    // Magpie), se esperan 3 s a que se asiente y se repite. ini magpie_sm_vaciar_real=1 (presentmon_exe = la ruta si no esta en las de
    // siempre)
    partial class TrayApp
    {
        static string FindPresentMon(string ini)
        {
            List<string> c = new List<string>();
            if (!string.IsNullOrEmpty(ini)) c.Add(ini);
            foreach (string dir in new[] { Path.Combine(Paths.AppDir, "tools"), Path.Combine(Paths.AppDir, "scripts"),
                                           @"C:\Program Files (x86)\RivaTuner Statistics Server\Plugins\Client\PresentMonDataProvider",
                                           @"C:\Program Files\Intel\PresentMon\PresentMonCli" })
            {
                try
                {
                    // (PresentMon 2.x: el 1.x tiene otras opciones; el mas nuevo primero)
                    if (!Directory.Exists(dir)) continue;
                    List<string> fs = new List<string>();
                    foreach (string f in Directory.GetFiles(dir, "PresentMon-*.exe"))
                        if (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^PresentMon-([2-9]|\d\d)\.")) fs.Add(f);
                    fs.Sort(); fs.Reverse(); c.AddRange(fs);
                }
                catch { }
            }
            foreach (string f in c) if (File.Exists(f)) return f;
            return null;
        }

        void SmRealWatch(int seq)
        {
            string exe = FindPresentMon(cfg.PresentMonExe);
            if (exe == null) { Log.Write("Smooth Motion de Magpie: vaciado con la cola real no (no hay PresentMon; ini presentmon_exe)"); return; }
            double hz = ExactHz();
            if (hz <= 0) hz = Pantalla.Hz() == 119 ? 119.88 : Pantalla.Hz();
            if (hz < 100) { Log.Write("Smooth Motion de Magpie: vaciado con la cola real no (pantalla a " + hz.ToString("0.00", CultureInfo.InvariantCulture) + " Hz)"); return; }
            double frameMs = 4000.0 / hz;   // una imagen real (inventada + real = 2 x 2 refrescos)
            const string session = "prisma_cola";
            Process pm = null;
            List<double[]> samples = new List<double[]>();   // (ms del reloj, ms de presentar a verse)
            Stopwatch clock = Stopwatch.StartNew();
            int drains = 0, rows = 0;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, "--process_name Magpie.exe --output_stdout --no_track_gpu --no_track_input " +
                    "--exclude_dropped --terminate_on_proc_exit --session_name " + session + " --stop_existing_session");   // (se cierra solo si se cierra Magpie)
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                pm = Process.Start(psi);
                Process pmr = pm;
                pm.ErrorDataReceived += delegate { };
                pm.BeginErrorReadLine();
                Thread reader = new Thread(delegate ()
                {
                    try
                    {
                        string header = pmr.StandardOutput.ReadLine();
                        if (header == null) return;
                        string[] h = header.Split(',');
                        int iApp = Array.IndexOf(h, "Application"), iBetween = Array.IndexOf(h, "MsBetweenPresents"), iUntil = Array.IndexOf(h, "MsUntilDisplayed");
                        if (iApp < 0 || iBetween < 0 || iUntil < 0) { Log.Write("Smooth Motion de Magpie: PresentMon sin columnas MsUntilDisplayed: " + header); return; }
                        string line;
                        while ((line = pmr.StandardOutput.ReadLine()) != null)
                        {
                            string[] f = line.Split(',');
                            if (f.Length <= Math.Max(iUntil, iBetween)) continue;
                            double b, u;
                            if (!double.TryParse(f[iBetween], NumberStyles.Float, CultureInfo.InvariantCulture, out b) || b <= 4) continue;
                            if (!double.TryParse(f[iUntil], NumberStyles.Float, CultureInfo.InvariantCulture, out u)) continue;
                            lock (samples) { samples.Add(new[] { clock.Elapsed.TotalMilliseconds, u }); rows++; }
                        }
                    }
                    catch { }
                });
                reader.IsBackground = true; reader.Name = "PresentMon cola"; reader.Start();
                Log.Write("Smooth Motion de Magpie: vaciado con la cola real (PresentMon " + Path.GetFileName(exe) + ", alto " +
                          cfg.SmRealHigh.ToString("0.0", CultureInfo.InvariantCulture) + ", objetivo " + cfg.SmRealTarget.ToString("0.0", CultureInfo.InvariantCulture) + " imagenes)");
                double next = 2000, lastLog = 0;
                while (seq == smDrainSeq && !userExit && !Config.EngineActive && ipcUp && cfg.SmoothNow && cfg.CapMode && MagpieLive.IsScaling() && !pm.HasExited)
                {
                    Thread.Sleep(250);
                    double now = clock.Elapsed.TotalMilliseconds;
                    List<double> win = new List<double>();
                    lock (samples)
                    {
                        samples.RemoveAll(s => s[0] < now - 1500);
                        foreach (double[] s in samples) win.Add(s[1]);
                    }
                    if (win.Count < 20) continue;
                    win.Sort();
                    double q = win[win.Count / 2] / frameMs;
                    if (cfg.SmDrainLog && now - lastLog >= 4000)
                    {
                        lastLog = now;
                        Log.Write("Smooth Motion de Magpie: cola real " + q.ToString("0.00", CultureInfo.InvariantCulture) + " imagenes (" +
                                  win[win.Count / 2].ToString("0.0", CultureInfo.InvariantCulture) + " ms de presentar a verse, " + win.Count + " imagenes)");
                    }
                    if (now < next || q <= cfg.SmRealHigh) continue;
                    // (medido: una pausa vacia ~la mitad de lo que dura, el resto se queda antes, en mpv: magpie_sm_vaciar_real_ganancia)
                    int ms = (int)Math.Round((q - cfg.SmRealTarget) * frameMs * cfg.SmRealGain);
                    if (ms < 20) continue;
                    ms = Math.Min(ms, 400);
                    drains++;
                    SmDrainPause(ms, "cola real " + q.ToString("0.00", CultureInfo.InvariantCulture) + " imagenes (" +
                                 win[win.Count / 2].ToString("0", CultureInfo.InvariantCulture) + " ms), vez " + drains);
                    lock (samples) samples.Clear();
                    next = clock.Elapsed.TotalMilliseconds + 3000;
                }
            }
            catch (Exception ex) { Log.Error("Smooth Motion de Magpie: PresentMon", ex); }
            finally
            {
                try { if (pm != null && pm.HasExited) Log.Write("Smooth Motion de Magpie: PresentMon se cerro solo (codigo " + pm.ExitCode + ")"); } catch { }
                try { if (pm != null && !pm.HasExited) pm.Kill(); } catch { }
                try
                {
                    ProcessStartInfo k = new ProcessStartInfo(exe, "--terminate_existing_session --session_name " + session);
                    k.UseShellExecute = false; k.CreateNoWindow = true;
                    using (Process kp = Process.Start(k)) kp.WaitForExit(5000);
                }
                catch { }
                Log.Write("Smooth Motion de Magpie: vaciado con la cola real terminado (" + drains + " pausas, " + rows + " imagenes medidas)");
            }
        }
    }
}
