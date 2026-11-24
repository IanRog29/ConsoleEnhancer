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
// 23-11 (1.3.1, agente FPS; el usuario: "haz que detecte mas rapidamente cuando los fps no son los fps objetivo" y "cuando no quepa el fg
// antes que intentar bajar la cantidad de multiplicacion fg baja la resolucion del dlss 5 o el rescalado dlss bajalo"): el motor
// (src\Motor\Engine\EngineEscalera.cs) lo ve en 1-2 s y baja el solo, en caliente, DLSS 5 (% y pasadas) y luego el modelo de DLSS SR; solo
// cuando ya no le queda nada avisa con "switch2-fps" "bajo" <reales> <objetivo> <p95> "agotado" <% de DLSS 5|-> <modelo|->. Aqui entonces,
// con Smooth Motion x4 / Enhanced FG x3-x4, x2 SOLO esta sesion (lo elegido se queda en el perfil) diciendo lo que ya bajo el motor; sin
// multiplicador que bajar, lo de siempre (sin la pasada menos: ya la quito el motor). "switch2-escalera": lo bajado ya cabe -> aviso corto
// (EscaleraTexto, que cubre --test-avisos). Con el menu de ajustes abierto el motor no juzga (switch2-vigila 0 / 1, MenuAjustes.cs).
// 24-11 (1.3.2, agente FPS; el usuario tras jugar con la 1.3.1: "Cuando no llega al 4x deberias dejar bajar al 3x no 2x", "no va el modo 4x
// de ninguna manera", "el enhanced fg no me llega a 120 fps ni con dlss 5 off", "Bug no llega a 120 fps 60x2 a 1440p con dlss 5 y smooth"):
//  - Enhanced FG x4 -> x3 -> x2 (antes x4 -> x2 de golpe), solo esta sesion con esa captura y DLSS 5 si / no (EfgNoLlegaAnota con el tope).
//  - El motor ya no avisa "bajo" si la grafica entera no va llena (EngineGpuUso.cs): avisa "ritmo" y aqui solo se dice, una vez por estado, que
//    no es la grafica ("Enhanced FG x4 · 108 de 120 fps: no es la gráfica (al 55 %), no se cambia nada"). Nada se baja.
//  - Lo que la app cambia sola NUNCA va al perfil del juego: DLSS 5 apagado (antes "guardado en el perfil del juego") pasa a ser de esta
//    sesion (D5NoLlegaAnota: como si no cupiera, con esta captura, modo y generacion; lo elegido sigue), FSR 1 ya lo hace el motor (esta
//    sesion), y el modo / la captura que se bajen tampoco se guardan (FpsSesion*: el perfil se escribe con lo elegido).
//  - Smooth Motion x4 ya no existe (el usuario: "Quita el smooth motion 4x que xefg es una mierda"; SmoothX4.cs).
// C# 5.
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // (1.3.2, FPS) DLSS 5 apagado por la escalera SOLO esta sesion: con este juego, captura, modo y generacion elegida (como si no cupiera:
        // Dlss5PassesNow 0). Ni el perfil ni lo elegido (dlss5Shown, dlss5_on.txt) cambian; vuelve al cambiar de juego, de captura, de modo o
        // de generacion, o al elegir DLSS 5 otra vez (D5NoLlegaOlvida)
        static string d5NoLlega = "";
        string D5NoLlegaClave() { return (EscalaAuto.Juego ?? "") + "|" + (VideoSize ?? "") + "|" + ModoFps + "|" + (FgOff ? "-" : CapMode ? FgPick30 : FgPick60); }
        public static void D5NoLlegaAnota(Config c) { d5NoLlega = c.D5NoLlegaClave(); }
        public static void D5NoLlegaOlvida(string por) { if (d5NoLlega.Length == 0) return; d5NoLlega = ""; Log.Write("Fps: DLSS 5 otra vez (" + por + "): lo apagado esta sesion por no llegar se olvida"); }
        public bool D5NoLlegaAhora { get { return d5NoLlega.Length > 0 && d5NoLlega == D5NoLlegaClave(); } }
    }

    partial class TrayApp
    {
        int fpsBusy;

        // MpvReader: el aviso switch2-fps del motor. true = era ese
        bool FpsEvent(string line)
        {
            if (!line.Contains("\"args\":[\"switch2-fps\"")) return false;
            if (line.Contains("\"switch2-fps\",\"ritmo\"")) { FpsRitmo(line); return true; }   // (1.3.2, FPS: no es la grafica)
            Match m = Regex.Match(line, "\"switch2-fps\",\"bajo\",\"([0-9.]+)\",\"([0-9.]+)\",\"([0-9.]+)\"(?:,\"agotado\",\"([0-9]+|-)\",\"([A-Z]|-)\"(?:,\"([0-9.]+)\")?)?");
            if (!m.Success) return true;
            if (readerMpv != null && readerMpv != mpv) return true;   // (de un motor que ya no es el de ahora)
            double real = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double obj = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double gpu = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (InHome || homeBusy || passesBusy || relaunchSize != null || relaunchEngine || userExit || ajustesAbierto)
            {
                Log.Write("Fps: el motor no llega (" + m.Groups[1].Value + " de " + m.Groups[2].Value + "), pero algo esta cambiando" + (ajustesAbierto ? " (el menu de ajustes abierto)" : "") + ": nada");
                return true;
            }
            // (1.3.1, FPS) "agotado": el motor ya bajo lo suyo (DLSS 5 al <pct> %, DLSS <modelo>)
            fpsAgotado = m.Groups[4].Success;
            fpsAgD5 = m.Groups[4].Success && m.Groups[4].Value != "-" ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : -1;
            fpsAgSr = m.Groups[5].Success && m.Groups[5].Value != "-" ? m.Groups[5].Value : "";
            // (las que se ven, si el motor las sabe -Enhanced FG: reales + inventadas no saltadas-; si no, reales x multiplicador)
            fpsAgVistas = m.Groups[6].Success ? double.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture) : 0;
            FpsLanza(real, obj, gpu, "el motor");
            return true;
        }

        // (1.2.4 ronda 7, EFGFIX) "switch2-x4d5" del motor (src\Motor\Engine\EngineX4D5.cs): x4 con DLSS 5 (Smooth Motion x4 / Enhanced FG x3 / x4).
        //  - "ok" <%>: cabe con DLSS 5 bajado a ese % -> aviso "x4 con DLSS 5: DLSS 5 al N % para que quepa" (una vez por %);
        //  - "no": no llega ni con DLSS 5 al 25 % -> x2 esta sesion con esta captura y DLSS 5, diciendolo (lo elegido -x4- se queda guardado)
        int x4d5Dicho = -1;
        volatile bool fpsAgotado; volatile int fpsAgD5 = -1; volatile string fpsAgSr = ""; double fpsAgVistas;

        // (1.3.1, FPS) "switch2-escalera" <pct|-> <modelo|-> <pct pedido|-> <modelo pedido|-> <pasadas>: lo que el motor bajo ya cabe -> aviso
        string escDicho = "";
        bool EscaleraEvent(string line)
        {
            if (!line.Contains("\"args\":[\"switch2-escalera\"")) return false;
            Match m = Regex.Match(line, "\"switch2-escalera\",\"([0-9]+|-)\",\"([A-Z]|-)\"");
            if (!m.Success) return true;
            if (readerMpv != null && readerMpv != mpv) return true;   // (de un motor que ya no es el de ahora)
            int pct = m.Groups[1].Value == "-" ? -1 : int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            string sr = m.Groups[2].Value == "-" ? "" : m.Groups[2].Value;
            PrepareCfg();
            string quien = EscaleraQuien(cfg);
            string clave = quien + "|" + pct + "|" + sr;
            if (clave == escDicho) return true;
            escDicho = clave;
            string t = EscaleraTexto(quien, pct, sr);
            if (t == null) return true;
            Log.Write("Escalera: " + quien + " cabe con lo que bajo el motor (" + (pct > 0 ? "DLSS 5 al " + pct + " %" : "") + (pct > 0 && sr.Length > 0 ? ", " : "") + (sr == "F" ? "FSR 1 en vez de DLSS" : sr.Length > 0 ? "DLSS " + sr : "") + "): aviso");
            OnUi(delegate { Osd(t); });
            return true;
        }

        // Lo que va (para los avisos de la escalera): "Smooth Motion x4", "Enhanced FG x3", "XeSS FG", "30 fps"...
        public static string EscaleraQuien(Config c)
        {
            int modo = c.ModoFps;
            if (c.FgOff || c.Mode120) return modo + " fps";
            string t = c.FgTypeNow;
            if (t == "smooth") return "Smooth Motion x" + (c.SmoothX4Now ? 4 : 2);
            if (t == "efg") return "Enhanced FG x" + c.EfgMultNow.ToString(CultureInfo.InvariantCulture);
            if (t == "fsr3") return Config.FsrMulLabel(c.FsrMultNow);
            if (t == "xess") return "XeSS FG";
            return modo + " fps";
        }

        // (1.3.1, FPS) "Enhanced FG x4 · DLSS 5 al 25 % y DLSS K para que quepa" (null = nada bajado). Lo cubre --test-avisos (Avisos.cs)
        public static string EscaleraTexto(string quien, int pct, string sr)
        {
            string es = "", en = "";
            if (pct > 0) { es = "DLSS 5 al " + pct + " %"; en = "DLSS 5 at " + pct + " %"; }
            // ("F" = FSR 1 en vez de DLSS esta sesion del motor)
            if (sr == "F") { es += (es.Length > 0 ? " y " : "") + "FSR 1 en vez de DLSS"; en += (en.Length > 0 ? " and " : "") + "FSR 1 instead of DLSS"; }
            else if (!string.IsNullOrEmpty(sr)) { es += (es.Length > 0 ? " y " : "") + "DLSS " + sr; en += (en.Length > 0 ? " and " : "") + "DLSS " + sr; }
            if (es.Length == 0) return null;
            return Tx.T(quien + " · " + es + " para que quepa", quien + " · " + en + " so it fits");
        }

        // (1.3.1, FPS) lo que ya bajo el motor, para el aviso del multiplicador: " ni con DLSS 5 al 25 % y DLSS E" ("" = nada)
        public static string EscaleraNiCon(int pct, string sr)
        {
            string es = "", en = "";
            if (pct > 0) { es = "DLSS 5 al " + pct + " %"; en = "DLSS 5 at " + pct + " %"; }
            string srN = sr == "F" ? "FSR 1" : "DLSS " + sr;
            if (!string.IsNullOrEmpty(sr)) { es += (es.Length > 0 ? " y " : "") + srN; en += (en.Length > 0 ? " and " : "") + srN; }
            if (es.Length == 0) return "";
            return Tx.T(" ni con " + es, " even with " + en);
        }

        bool X4d5Event(string line)
        {
            if (!line.Contains("\"args\":[\"switch2-x4d5\"")) return false;
            Match m = Regex.Match(line, "\"switch2-x4d5\",\"(ok|no)\",\"([0-9]+)\"");
            if (!m.Success) return true;
            if (readerMpv != null && readerMpv != mpv) return true;   // (de un motor que ya no es el de ahora)
            PrepareCfg();
            bool smx4 = false, efg = cfg.FgTypeNow == "efg" && cfg.EfgMultNow > 2;   // (1.3.2, FPS: Smooth Motion x4 ya no existe)
            if (!smx4 && !efg) return true;
            string quien = "Enhanced FG x" + cfg.EfgMultNow.ToString(CultureInfo.InvariantCulture);
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
                    // (1.3.2, FPS: x4 -> x3 -> x2, no x4 -> x2)
                    int n = cfg.EfgMultNow - 1;
                    Config.EfgNoLlegaAnota(cfg.MotorD5Simple, cfg.VideoSize, n);
                    string va = "Enhanced FG x" + n.ToString(CultureInfo.InvariantCulture);
                    Log.Write("x4 con DLSS 5: " + quien + " no llega a las 30 reales ni con DLSS 5 al " + pct + " %: " + va + " esta sesion (con esta captura y DLSS 5; lo elegido se queda)");
                    RestartLs(null, null);
                    string t = Tx.T(quien + " con DLSS 5 no llega a 30 ni al " + pct + " % · " + va + " esta sesión", quien + " with DLSS 5 can't keep 30 fps even at " + pct + " % · " + va + " this session");
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
            int modo = cfg.ModoFps;
            int salida = cfg.Mode120 || cfg.FgOff ? modo : modo * cfg.FgMultNow;
            string vista = Math.Round(fpsAgotado && fpsAgVistas > 0 ? fpsAgVistas : real * (cfg.Mode120 || cfg.FgOff ? 1 : cfg.FgMultNow)).ToString(CultureInfo.InvariantCulture);
            // (1.2.1: el aviso en el idioma elegido; antes solo en espanol)
            string falta = Tx.T("No se llega a " + salida + " fps (" + vista + ")", salida + " fps not reached (" + vista + ")");
            // (1.3.1, FPS) el motor ya bajo lo suyo: se dice ("ni con DLSS 5 al 25 % y DLSS E") y lo de despues es solo de esta sesion si es el
            // multiplicador; sin la pasada menos de DLSS 5 (ya la quito el motor)
            bool agotado = fpsAgotado;
            fpsAgotado = false;
            if (agotado) falta += EscaleraNiCon(fpsAgD5, fpsAgSr);
            bool sesion = false;
            string hecho = null, hechoEn = null;
            string cap = cfg.VideoSize;
            string menos = cap == "2560x1440" ? "1920x1080" : cap == "1920x1080" ? "1280x720" : null;
            if (!cfg.FgOff && !cfg.Mode120 && cfg.FgTypeNow == "efg" && cfg.EfgMultNow > 2)
            {
                // (1.2.4 ronda 7, EFGFIX: Enhanced FG x3 / x4 -> x2 lo primero; esta sesion con esta captura y DLSS 5 si / no). (1.3.2, FPS; el
                // usuario: "Cuando no llega al 4x deberias dejar bajar al 3x no 2x": de uno en uno, x4 -> x3 y, si x3 tampoco llega, x3 -> x2)
                int n = cfg.EfgMultNow - 1;
                Config.EfgNoLlegaAnota(cfg.MotorD5Simple, cfg.VideoSize, n);
                hecho = "Enhanced FG x" + n.ToString(CultureInfo.InvariantCulture); hechoEn = hecho; sesion = true;
                RestartLs(null, null);   // (sin aviso propio: habla el OsdWarn de abajo)
            }
            else if (!cfg.FgOff && !cfg.Mode120 && cfg.FgTypeNow == "fsr3" && cfg.FsrMultNow > 2)
            {
                // (1.2.4 ronda 6, FGMULTI: FSR 3 FG x4 -> x3 -> x2 lo primero: es lo que mas grafica pide; MultiFsr.cs)
                int n = cfg.FsrMultNow - 1;
                Config.FsrMultSet(n);
                hecho = Config.FsrMulLabel(n); hechoEn = Config.FsrMulLabel(n);
                RestartLs(null, null);   // (1.2.4 ronda 7, INTEGRA7: sin aviso propio, como el de x4 -> x2: habla el OsdWarn de abajo)
            }
            else if (dlss5Shown && !cfg.Dlss5Off && cfg.Dlss5FitsNow && cfg.Dlss5PassesNow > 1 && !agotado)
            {
                // (sin la escalera del motor -escalera=0-: una pasada menos; el perfil se queda con las elegidas, FpsSesion*)
                int n = cfg.Dlss5PassesNow - 1;
                FpsSesionAntes();
                try { SetPasses(n); } finally { FpsSesionDespues(); }
                hecho = "DLSS 5 con " + Pasadas(n); hechoEn = "DLSS 5 with " + n + (n > 1 ? " passes" : " pass"); sesion = true;
            }
            else if (dlss5Shown && !cfg.Dlss5Off && cfg.Dlss5FitsNow)
            {
                // (1.3.2, FPS; el coordinador, de la sesion del usuario 01:26:55: "se pasa a DLSS 5 apagado (guardado en el perfil)" con la grafica
                // sobrada) DLSS 5 fuera SOLO esta sesion, con esta captura, modo y generacion (como si no cupiera: Dlss5PassesNow 0): ni el perfil
                // ni lo elegido cambian (antes SetDlss5State + SaveCurProfile). Elegir DLSS 5 otra vez, otro juego u otra captura lo devuelve
                Config.D5NoLlegaAnota(cfg);
                hecho = "DLSS 5 apagado"; hechoEn = "DLSS 5 off"; sesion = true;
                RestartLs(null, null, false, true);   // (INTEGRA7: sin aviso propio: el OsdWarn)
            }
            else if (!agotado && cfg.UpscaleOn && cfg.ScalerNow != "fsr" && cfg.ScalerNow != "ninguno")
            {
                // (1.3.2, FPS: con la escalera del motor -agotado- FSR 1 ya lo puso el motor esta sesion -escalera_fsr1- o no tocaba; sin ella, FSR 1
                // sin guardarlo en el perfil)
                FpsSesionAntes();
                try { SetScalerChoice(true, "fsr"); } finally { FpsSesionDespues(); }
                hecho = "escalador FSR 1"; hechoEn = "FSR 1 upscaler"; sesion = true;
                RestartLs(null, null);   // (INTEGRA7: sin aviso propio: el OsdWarn)
            }
            else if (modo == 120)
            {
                FpsSesionAntes();
                try { SetModeN(60); } finally { FpsSesionDespues(); }
                hecho = "modo 60 fps"; hechoEn = "60 fps mode"; sesion = true;
            }
            else if (menos != null && !CapturaSinSentido(menos))
            {
                FpsSesionAntes();
                try { RequestCapture(menos); } finally { FpsSesionDespues(); }
                hecho = "captura a " + SizeName(menos); hechoEn = "capture at " + SizeName(menos); sesion = true;
            }
            else if (modo == 60 && !cfg.FgOff)
            {
                FpsSesionAntes();
                try { SetModeN(30); } finally { FpsSesionDespues(); }
                hecho = "modo 30 fps con generación (30 → " + (30 * cfg.FgMultNow) + ")"; hechoEn = "30 fps mode with frame generation (30 -> " + (30 * cfg.FgMultNow) + ")"; sesion = true;
            }
            Log.Write("Fps: " + quien + " no llega al modo " + modo + " (" + real.ToString("0.0", CultureInfo.InvariantCulture) + " de " +
                      obj.ToString("0.00", CultureInfo.InvariantCulture) + " imagenes reales/s, grafica p95 " + gpu.ToString("0.0", CultureInfo.InvariantCulture) +
                      " ms" + (agotado ? "; el motor ya bajo lo suyo: DLSS 5 " + (fpsAgD5 > 0 ? "al " + fpsAgD5 + " %" : "no") + ", " + (fpsAgSr == "F" ? "FSR 1 en vez de DLSS" : "DLSS SR " + (fpsAgSr.Length > 0 ? fpsAgSr : "no")) : "") + "): " +
                      (hecho != null ? "se pasa a " + hecho + (sesion ? " (solo esta sesion: lo elegido sigue en el perfil del juego)" : " (guardado en el perfil del juego)") : "ya no queda nada que bajar, solo el aviso"));   // (1.3.2, FPS: ya siempre "solo esta sesion")
            string texto = FpsBajaTexto(falta, hecho, hechoEn, sesion);
            OnUi(delegate { OsdWarn(texto, "7000"); });
        }

        // (1.3.2, FPS) "switch2-fps" "ritmo" <reales> <objetivo> <p95> <uso de la grafica %> <vistas> <efg|reflex|tarde>: no se llega pero la
        // grafica entera no va llena (EngineEscalera.cs EscNoEsGrafica): el motor no ha bajado nada y aqui tampoco; se dice una vez por estado
        string ritmoDicho = "";
        void FpsRitmo(string line)
        {
            Match m = Regex.Match(line, "\"switch2-fps\",\"ritmo\",\"([0-9.]+)\",\"([0-9.]+)\",\"([0-9.]+)\",\"([0-9]+)\",\"([0-9.]+)\",\"([a-z]+)\"");
            if (!m.Success) return;
            if (readerMpv != null && readerMpv != mpv) return;
            PrepareCfg();
            string quien = EscaleraQuien(cfg);
            int modo = cfg.ModoFps;   // (1.3.2, INTEGRA9: el modo 40 de MODO40)
            int salida = cfg.Mode120 || cfg.FgOff ? modo : modo * cfg.FgMultNow;
            double vistas = double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture), real = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (vistas <= 0) vistas = real * (cfg.Mode120 || cfg.FgOff ? 1 : cfg.FgMultNow);
            int uso = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            string causa = m.Groups[6].Value;
            Log.Write("Fps: el motor no llega (" + m.Groups[1].Value + " de " + m.Groups[2].Value + " reales/s, " + vistas.ToString("0", CultureInfo.InvariantCulture) + " de " + salida +
                      " en la tele) con la grafica entera al " + uso + " % como mucho: no es la grafica (" + (causa == "efg" ? "el ritmo de Enhanced FG" : causa == "reflex" ? "el tope de 116 con VRR y Smooth Motion" : "el ritmo de la presentacion o la fuente") +
                      "): no se cambia nada" + (InHome || homeBusy || passesBusy || relaunchSize != null || relaunchEngine || userExit || ajustesAbierto ? " (algo esta cambiando: sin aviso)" : ""));
            if (InHome || homeBusy || passesBusy || relaunchSize != null || relaunchEngine || userExit || ajustesAbierto) return;
            string clave = quien + "|" + cfg.VideoSize + "|" + (cfg.MotorD5Simple ? "d5" : "-");
            if (clave == ritmoDicho) return;
            ritmoDicho = clave;
            string t = FpsRitmoTexto(quien, (int)Math.Round(vistas), salida, uso);
            OnUi(delegate { Osd(t); });
        }

        // (1.3.2, FPS) "Enhanced FG x4 · 108 de 120 fps: no es la gráfica (al 55 %), no se cambia nada" (lo cubre --test-avisos)
        public static string FpsRitmoTexto(string quien, int vistas, int salida, int uso)
        {
            // (uso 0 = no se sabe -sin NVML-: sin el %)
            return Tx.T(quien + " · " + vistas + " de " + salida + " fps: no es la gráfica" + (uso > 0 ? " (al " + uso + " %)" : "") + ", no se cambia nada",
                        quien + " · " + vistas + " of " + salida + " fps: not the GPU" + (uso > 0 ? " (" + uso + " % busy)" : "") + ", nothing changed");
        }

        // (1.3.2, FPS) Lo que la escalera del lanzador cambia sola (modo, captura, pasadas, FSR 1 sin la escalera del motor) es de esta sesion:
        // el perfil del juego se escribe con lo elegido. SaveCurProfile (dentro de SetModeN / RequestCapture / SetPasses / SetScalerChoice y
        // despues, con cualquier cambio del mando o del menu) usa CurrentProfile, que pasa por FpsSesionPerfil: mientras el valor siga siendo el
        // que puso la app (el usuario no lo ha cambiado), va el de antes. Al cambiar de juego, el perfil del otro manda (y el de este sigue igual)
        string fpsSesJuego;
        GameProfile fpsSesAntes, fpsSesAuto;
        bool fpsSesCambiando;
        void FpsSesionAntes()
        {
            lock (profileSync)
            {
                string g = curGame;
                if (fpsSesJuego != g || fpsSesAntes == null) { fpsSesJuego = g; fpsSesAntes = null; fpsSesAntes = CurrentProfile(g); fpsSesAuto = null; }
                fpsSesCambiando = true;
            }
        }
        void FpsSesionDespues()
        {
            lock (profileSync)
            {
                fpsSesCambiando = false;
                GameProfile a = fpsSesAntes;
                fpsSesAntes = null;
                GameProfile ahora = CurrentProfile(fpsSesJuego);
                fpsSesAntes = a;
                fpsSesAuto = ahora;
            }
        }
        // (desde CurrentProfile) lo puesto por la app, cambiado por lo elegido de antes
        void FpsSesionPerfil(GameProfile p)
        {
            GameProfile a = fpsSesAntes, au = fpsSesAuto;
            if (a == null || p == null || (p.Name != null && p.Name != fpsSesJuego)) return;
            bool todo = fpsSesCambiando || au == null;
            if (todo || p.Capture == au.Capture) p.Capture = a.Capture;
            // (1.3.2, INTEGRA9: con el modo 40 de MODO40 -Cap + Mode40-: el modo entero, si no un 40 elegido despues quedaria a medias)
            if (todo || (p.Cap == au.Cap && p.Mode120 == au.Mode120 && p.Mode40 == au.Mode40)) { p.Cap = a.Cap; p.Mode120 = a.Mode120; p.Mode40 = a.Mode40; }
            if (todo || p.Passes == au.Passes) p.Passes = a.Passes;
            if (todo || (p.Scaler == au.Scaler && p.ScalerPick == au.ScalerPick)) { p.Scaler = a.Scaler; p.ScalerPick = a.ScalerPick; }
        }

        // (1.3.1, FPS) el aviso rojo de FpsBaja; el multiplicador bajado lo dice "esta sesión" (lo cubre --test-avisos)
        public static string FpsBajaTexto(string falta, string hecho, string hechoEn, bool sesion)
        {
            if (hecho == null) return falta + Tx.T(" · ya está todo al mínimo", ": everything is already at the minimum");
            if (sesion) return falta + Tx.T(" · " + hecho + " esta sesión", " · " + hechoEn + " this session");
            return falta + Tx.T(" · cambiado a " + hecho, ": changed to " + hechoEn);
        }
    }
}
