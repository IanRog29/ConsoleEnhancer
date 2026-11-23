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
// 21-11 (1.2.1, el usuario: "si no se llega, cambiara la escala del dlss 5 o cambiara algun otro ajuste y con ello avisara"): con DLSS 5
// por Magpie y la escala automatica (dlss5_escala_auto=1) lo vigila src\Lanzador\EscalaAuto.cs, que baja antes, en caliente, el % de
// DLSS 5, las pasadas, el modelo de DLSS SR y el tipo de generacion; esta escalera queda para lo que viene despues (DLSS 5 fuera, FSR 1,
// 120 -> 60, captura, 60 -> 30) y para el motor propio. FpsMagpieVigila sigue con DLSS 5 apagado por Magpie (XeSS / FSR 3 FG). Avisos
// con Tx.T (antes solo en espanol). A 120, las imagenes que da mpv tambien por su recuento de 20 s (FuenteFps).
// C# 5.
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using ConsoleEnhancer.Deteccion;

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

        // (1.2.4 ronda 7, EFGFIX) "switch2-x4d5" del motor (src\Motor\Engine\EngineX4D5.cs): x4 con DLSS 5 (Smooth Motion x4 / Enhanced FG x3 / x4).
        //  - "ok" <%>: cabe con DLSS 5 bajado a ese % -> aviso "x4 con DLSS 5: DLSS 5 al N % para que quepa" (una vez por %);
        //  - "no": no llega ni con DLSS 5 al 25 % -> x2 esta sesion con esta captura y DLSS 5, diciendolo (lo elegido -x4- se queda guardado)
        int x4d5Dicho = -1;
        bool X4d5Event(string line)
        {
            if (!line.Contains("\"args\":[\"switch2-x4d5\"")) return false;
            Match m = Regex.Match(line, "\"switch2-x4d5\",\"(ok|no)\",\"([0-9]+)\"");
            if (!m.Success) return true;
            if (readerMpv != null && readerMpv != mpv) return true;   // (de un motor que ya no es el de ahora)
            PrepareCfg();
            bool smx4 = cfg.FgTypeNow == "smooth" && cfg.SmoothX4Now, efg = cfg.FgTypeNow == "efg" && cfg.EfgMultNow > 2;
            if (!smx4 && !efg) return true;
            string quien = smx4 ? "Smooth Motion x4" : "Enhanced FG x" + cfg.EfgMultNow.ToString(CultureInfo.InvariantCulture);
            int pct = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (m.Groups[1].Value == "ok")
            {
                if (pct == x4d5Dicho) return true;
                x4d5Dicho = pct;
                Log.Write("x4 con DLSS 5: " + quien + " cabe con DLSS 5 al " + pct + " % (el motor lo ha bajado para que quepa)");
                if (pct < 100) OnUi(delegate { Osd(Tx.T(quien + " con DLSS 5: DLSS 5 al " + pct + " % para que quepa", quien + " with DLSS 5: DLSS 5 at " + pct + " % so it fits")); });
                return true;
            }
            if (InHome || homeBusy || passesBusy || relaunchSize != null || relaunchEngine || userExit) return true;
            if (Interlocked.CompareExchange(ref fpsBusy, 1, 0) != 0) return true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (smx4) Config.SmoothX4NoLlegaAnota(cfg.MotorD5Simple, cfg.VideoSize); else Config.EfgNoLlegaAnota(cfg.MotorD5Simple, cfg.VideoSize);
                    string va = smx4 ? "Smooth Motion x2" : "Enhanced FG x2";
                    Log.Write("x4 con DLSS 5: " + quien + " no llega a las 30 reales ni con DLSS 5 al 25 %: " + va + " esta sesion (con esta captura y DLSS 5; lo elegido se queda)");
                    RestartLs(null, null);
                    string t = Tx.T(quien + " con DLSS 5 no llega a 30 ni al 25 % · " + va + " esta sesión", quien + " with DLSS 5 can't keep 30 fps even at 25 % · " + va + " this session");
                    OnUi(delegate { OsdWarn(t, "7000"); });
                }
                catch (Exception ex) { Log.Error("x4 con DLSS 5", ex); }
                finally { Interlocked.Exchange(ref fpsBusy, 0); }
            });
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
        DateTime fpsMagpieLast = DateTime.MinValue;
        // (1.2.01-1.2.4: FpsMagpieVigila -Magpie no llegaba a las reales del modo-; 1.2.4 ronda 6, QUITAFG: sin Magpie, fuera)

        void FpsBaja(double real, double obj, double gpu, string quien)
        {
            PrepareCfg();
            int modo = cfg.Mode120 ? 120 : cfg.CapMode ? 30 : 60;
            int salida = cfg.Mode120 || cfg.FgOff ? modo : modo * cfg.FgMultNow;
            string vista = Math.Round(real * (cfg.Mode120 || cfg.FgOff ? 1 : cfg.FgMultNow)).ToString(CultureInfo.InvariantCulture);
            // (1.2.1: el aviso en el idioma elegido; antes solo en espanol)
            string falta = Tx.T("No se llega a " + salida + " fps (" + vista + ")", salida + " fps not reached (" + vista + ")");
            string hecho = null, hechoEn = null;
            string cap = cfg.VideoSize;
            string menos = cap == "2560x1440" ? "1920x1080" : cap == "1920x1080" ? "1280x720" : null;
            if (!cfg.FgOff && !cfg.Mode120 && cfg.FgTypeNow == "efg" && cfg.EfgMultNow > 2)
            {
                // (1.2.4 ronda 7, EFGFIX: Enhanced FG x3 / x4 -> x2 lo primero, como Smooth Motion x4; esta sesion con esta captura y DLSS 5 si / no)
                Config.EfgNoLlegaAnota(cfg.MotorD5Simple, cfg.VideoSize);
                hecho = "Enhanced FG x2"; hechoEn = "Enhanced FG x2";
                RestartLs(null, null);   // (sin aviso propio: habla el OsdWarn de abajo)
            }
            else if (!cfg.FgOff && !cfg.Mode120 && cfg.FgTypeNow == "smooth" && cfg.SmoothX4Now)
            {
                // (1.2.4 ronda 7, FGREAL: Smooth Motion x4 -> x2 lo primero -XeSS FG del motor + dos inventadas del driver por real-. Solo en esta
                // sesion y con esta captura y DLSS 5 si / no: lo elegido -x4- sigue guardado y la fila del menu dice por que va x2; SmoothX4.cs)
                Config.SmoothX4NoLlegaAnota(cfg.MotorD5Simple, cfg.VideoSize);
                hecho = "Smooth Motion x2"; hechoEn = "Smooth Motion x2";
                RestartLs(null, null);   // (sin aviso propio: habla el OsdWarn de abajo; TEXTOS)
            }
            else if (!cfg.FgOff && !cfg.Mode120 && cfg.FgTypeNow == "fsr3" && cfg.FsrMultNow > 2)
            {
                // (1.2.4 ronda 6, FGMULTI: FSR 3 FG x4 -> x3 -> x2 lo primero: es lo que mas grafica pide; MultiFsr.cs)
                int n = cfg.FsrMultNow - 1;
                Config.FsrMultSet(n);
                hecho = Config.FsrMulLabel(n); hechoEn = Config.FsrMulLabel(n);
                RestartLs(null, null);   // (1.2.4 ronda 7, INTEGRA7: sin aviso propio, como el de x4 -> x2: habla el OsdWarn de abajo)
            }
            else if (dlss5Shown && !cfg.Dlss5Off && cfg.Dlss5FitsNow && cfg.Dlss5PassesNow > 1)
            {
                int n = cfg.Dlss5PassesNow - 1;
                SetPasses(n);
                hecho = "DLSS 5 con " + Pasadas(n); hechoEn = "DLSS 5 with " + n + (n > 1 ? " passes" : " pass");
            }
            else if (dlss5Shown && !cfg.Dlss5Off && cfg.Dlss5FitsNow)
            {
                SetDlss5State(false);
                hecho = "DLSS 5 apagado"; hechoEn = "DLSS 5 off";
                // (1.2.01: antes solo cambiaba el estado y la imagen seguia igual hasta otro cambio: como el boton, se reabre)
                SaveCurProfile();
                RestartLs(null, null, false, true);   // (INTEGRA7: sin aviso propio: el OsdWarn)
            }
            else if (cfg.UpscaleOn && cfg.ScalerNow != "fsr" && cfg.ScalerNow != "ninguno")
            {
                SetScalerChoice(true, "fsr");
                hecho = "escalador FSR 1"; hechoEn = "FSR 1 upscaler";
                SaveCurProfile();
                RestartLs(null, null);   // (INTEGRA7: sin aviso propio: el OsdWarn)
            }
            else if (modo == 120)
            {
                SetModeN(60);
                hecho = "modo 60 fps"; hechoEn = "60 fps mode";
            }
            else if (menos != null && !CapturaSinSentido(menos))
            {
                RequestCapture(menos);
                hecho = "captura a " + SizeName(menos); hechoEn = "capture at " + SizeName(menos);
            }
            else if (modo == 60 && !cfg.FgOff)
            {
                SetModeN(30);
                hecho = "modo 30 fps con generación (30 → " + (30 * cfg.FgMultNow) + ")"; hechoEn = "30 fps mode with frame generation (30 -> " + (30 * cfg.FgMultNow) + ")";
            }
            Log.Write("Fps: " + quien + " no llega al modo " + modo + " (" + real.ToString("0.0", CultureInfo.InvariantCulture) + " de " +
                      obj.ToString("0.00", CultureInfo.InvariantCulture) + " imagenes reales/s, grafica p95 " + gpu.ToString("0.0", CultureInfo.InvariantCulture) +
                      " ms): " + (hecho != null ? "se pasa a " + hecho + " (guardado en el perfil del juego)" : "ya no queda nada que bajar, solo el aviso"));
            string texto = hecho != null ? falta + Tx.T(" · cambiado a " + hecho, ": changed to " + hechoEn) : falta + Tx.T(" · ya está todo al mínimo", ": everything is already at the minimum");
            OnUi(delegate { OsdWarn(texto, "7000"); });
        }
    }
}
