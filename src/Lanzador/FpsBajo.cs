// FpsBajo.cs - 21-11-2026 (el usuario: "haz que la app si detecta que no se llega a los fps seleccionados que te avise y te cambie la
// configuracion"). El motor propio (src\Motor\Engine\EngineFps.cs) avisa con "switch2-fps" "bajo" <reales/s> <objetivo> <grafica p95 ms>
// cuando, 5 s seguidos, no llega a las imagenes del modo y la culpa es del PC (no del juego de la consola). Antes, el motor ya intenta lo
// suyo (EngineAdapta: modelo de DLSS y % de DLSS 5). Aqui se baja UN paso, se avisa en pantalla de que se ha cambiado y se guarda en el
// perfil del juego (lo hacen SetPasses / RequestCapture / SetModeN, como con el mando). Orden: DLSS 5 una pasada menos -> DLSS 5 fuera ->
// del 120 al 60 -> captura 1440p -> 1080p -> 720p -> del 60 con generacion al 30 con generacion. Si ya no queda nada, solo el aviso. El
// siguiente paso, si sigue sin llegar, con el siguiente aviso del motor (30 s como poco entre avisos y 10 s tras cada cambio).
// 21-11 (1.2.01, el usuario: "en rocket league estaba en el modo 120 hz y he activado el dlss 5 y iba fatal, te dije que si no llega a
// los fps objetivo que se cambien cosas"): tambien con mpv + Magpie (DLSS 5 por Magpie: 70 de 120 reales medidos en su log). Lo mide
// CheckLsPacing con las imagenes reales que recibe Magpie (captureFps de su log) frente a las que da mpv: 2 resumenes seguidos (~10 s)
// por debajo del 90 % con mpv dando el modo entero = culpa del PC. Y un paso mas: el escalador a FSR 1 antes de bajar el modo.
// C# 5.
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace ConsoleEnhancer
{
    partial class TrayApp
    {
        int fpsBusy;

        // MpvReader: el aviso switch2-fps del motor. true = era ese
        bool FpsEvent(string line)
        {
            if (!line.Contains("\"args\":[\"switch2-fps\"")) return false;
            Match m = Regex.Match(line, "\"switch2-fps\",\"bajo\",\"([0-9.]+)\",\"([0-9.]+)\",\"([0-9.]+)\"");
            if (!m.Success) return true;
            if (readerMpv != null && readerMpv != mpv) return true;   // (de un motor que ya no es el de ahora)
            double real = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double obj = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double gpu = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (InHome || homeBusy || passesBusy || relaunchSize != null || relaunchEngine || userExit)
            {
                Log.Write("Fps: el motor no llega (" + m.Groups[1].Value + " de " + m.Groups[2].Value + "), pero algo esta cambiando: nada");
                return true;
            }
            FpsLanza(real, obj, gpu, "el motor");
            return true;
        }

        void FpsLanza(double real, double obj, double gpu, string quien)
        {
            if (Interlocked.CompareExchange(ref fpsBusy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { FpsBaja(real, obj, gpu, quien); }
                catch (Exception ex) { Log.Error("Fps: bajando la configuracion", ex); }
                finally { Interlocked.Exchange(ref fpsBusy, 0); }
            });
        }

        // (1.2.01) Cada 5 s, desde CheckLsPacing: Magpie no llega a las imagenes reales del modo con mpv dandoselas todas
        int fpsMagpieWins;
        DateTime fpsMagpieLast = DateTime.MinValue;
        void FpsMagpieVigila(double captured)
        {
            int modo = cfg.Mode120 ? 120 : cfg.CapMode ? 30 : 60;
            bool juzgar = captured > 0 && !Config.EngineActive && !cfg.HomeNative && !InHome && !homeBusy && !passesBusy && relaunchSize == null &&
                          !relaunchEngine && !userExit && MagpieLive.RunningFor() > 20 && vfFps >= modo * 0.92 && (DateTime.Now - fpsMagpieLast).TotalSeconds >= 30;
            double esperado = cfg.CapMode ? Math.Min(29.97, vfFps) : Math.Min(modo, vfFps);
            bool bajo = juzgar && captured < esperado * 0.9;
            fpsMagpieWins = bajo ? fpsMagpieWins + 1 : 0;
            if (fpsMagpieWins < 2) return;
            fpsMagpieWins = 0;
            fpsMagpieLast = DateTime.Now;
            FpsLanza(captured, esperado, 0, "Magpie");
        }

        void FpsBaja(double real, double obj, double gpu, string quien)
        {
            PrepareCfg();
            int modo = cfg.Mode120 ? 120 : cfg.CapMode ? 30 : 60;
            int salida = cfg.Mode120 || cfg.FgOff ? modo : modo * cfg.FgMultNow;
            string falta = "No se llega a " + salida + " fps (" + Math.Round(real * (cfg.Mode120 || cfg.FgOff ? 1 : cfg.FgMultNow)) + ")";
            string hecho = null;
            string cap = cfg.VideoSize;
            string menos = cap == "2560x1440" ? "1920x1080" : cap == "1920x1080" ? "1280x720" : null;
            if (dlss5Shown && !cfg.Dlss5Off && cfg.Dlss5FitsNow && cfg.Dlss5PassesNow > 1)
            {
                SetPasses(cfg.Dlss5PassesNow - 1);
                hecho = "DLSS 5 con " + Pasadas(cfg.Dlss5PassesNow - 1);
            }
            else if (dlss5Shown && !cfg.Dlss5Off && cfg.Dlss5FitsNow)
            {
                PasadasOnToggle(false);
                SetDlss5State(false);
                hecho = "DLSS 5 apagado";
                // (1.2.01: antes solo cambiaba el estado y la imagen seguia igual hasta otro cambio: como el boton, se reabre)
                SaveCurProfile();
                RestartLs("DLSS 5 desactivado", "Escalado " + ScalerText(), false, true);
            }
            else if (cfg.UpscaleOn && cfg.ScalerNow != "fsr" && cfg.ScalerNow != "ninguno")
            {
                SetScalerChoice(true, "fsr");
                hecho = "escalador FSR 1";
                SaveCurProfile();
                RestartLs("Escalador · " + ScalerText(), "Un momento…");
            }
            else if (modo == 120)
            {
                SetModeN(60);
                hecho = "modo 60 fps";
            }
            else if (menos != null && !CapturaSinSentido(menos))
            {
                RequestCapture(menos);
                hecho = "captura a " + SizeName(menos);
            }
            else if (modo == 60 && !cfg.FgOff)
            {
                SetModeN(30);
                hecho = "modo 30 fps con generación (30 → " + (30 * cfg.FgMultNow) + ")";
            }
            Log.Write("Fps: " + quien + " no llega al modo " + modo + " (" + real.ToString("0.0", CultureInfo.InvariantCulture) + " de " +
                      obj.ToString("0.00", CultureInfo.InvariantCulture) + " imagenes reales/s, grafica p95 " + gpu.ToString("0.0", CultureInfo.InvariantCulture) +
                      " ms): " + (hecho != null ? "se pasa a " + hecho + " (guardado en el perfil del juego)" : "ya no queda nada que bajar, solo el aviso"));
            string texto = hecho != null ? falta + " · cambiado a " + hecho : falta + " · ya está todo al mínimo";
            OnUi(delegate { OsdWarn(texto, "7000"); });
        }
    }
}
