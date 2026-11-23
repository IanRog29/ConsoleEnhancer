// Cierres.cs - 20-11-2026 (agente CIERRES; el usuario: "Arregla que me ha cerrado alguna vez la app mia propia"). C# 5.
// La app se cerraba sola con la consola encendida: cuando la imagen dejaba de avanzar (time-pos del motor = la ultima imagen PRESENTADA)
// 1,5 s lo tomaba por la consola apagada (CheckStall). En el log del 17-11 al 20-11 todos esos cierres fueron con la consola encendida:
// el motor reconfigurandose (17-11 02:38, 02:56 x3), un cambio de captura (18-11 19:24), la grafica reiniciada (20-11 03:54, y luego la app
// salio con "Cierre normal de mpv" al no dar imagen el motor relanzado) y la grafica parada con DLSS 5 (20-11 04:14). Los apagados de
// verdad (PS5, Switch 2) llegan siempre por la pantalla "No Signal" de la capturadora (PowerOffNow, ~0,45 s) y no cambian.
// Ahora, antes de tomar un corte de imagen por la consola apagada (apagado_confirmar=1):
//  - el motor dice que la grafica esta parada (switch2-gpu, EngineCuelgue.cs del motor) o no contesta (con la consola apagada si contesta)
//  - la capturadora sigue mandando imagenes al motor (switch2-captura-ms)
//  - la consola sigue sonando despues de pararse la imagen (el medidor de audio)
// Con cualquiera de esas: no es la consola; se espera y, si sigue parada cuelgue_relanzar_ms, se cierra solo el motor / mpv y se vuelve a
// abrir (RecoverFromHang), sin cerrar la app. Sin ninguna: la consola apagada como siempre (y el aviso de reinicio de la grafica se espera
// poco si el motor decia que la grafica iba bien). apagar_pc solo con el apagado por "No Signal" y sin reinicios de la grafica ni la grafica
// parada en el ultimo minuto (apagar_pc_confirmar=1).
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public bool StallConfirm = true;        // apagado_confirmar: 0 = como antes (1,5 s sin imagen = la consola apagada)
        public int HangRelaunchMs = 8000;       // cuelgue_relanzar_ms: la imagen parada sin ser la consola, tanto: se reabre el motor / mpv (0 = esperar siempre)
        public bool ShutdownConfirm = true;     // apagar_pc_confirmar: 0 = apagar el PC con cualquier apagado por "No Signal"

        public bool ParseCierres(string key, string val)
        {
            switch (key)
            {
                case "apagado_confirmar": StallConfirm = ParseBool(val); return true;
                case "cuelgue_relanzar_ms": HangRelaunchMs = Math.Max(0, ParseInt(val)); return true;
                case "apagar_pc_confirmar": ShutdownConfirm = ParseBool(val); return true;
            }
            return false;
        }
    }

    partial class TrayApp
    {
        // Lo que dicen el motor y el audio (solo del mpv / motor de ahora)
        DateTime lastSoundAt = DateTime.MinValue;       // la ultima medida del audio de la consola con sonido
        DateTime engReplyAt = DateTime.MinValue;        // la ultima respuesta a time-pos
        Process engReplyOwner;
        volatile int engCapMs = -1;                     // switch2-captura-ms: ms desde la ultima imagen de la capturadora (-1 ninguna)
        DateTime engCapAt = DateTime.MinValue;
        Process engCapOwner;
        volatile string engGpu = "ok";                  // switch2-gpu: ok | colgada | perdida | salida
        string engGpuDetail = "";
        DateTime engGpuTroubleAt = DateTime.MinValue;
        Process engGpuOwner;
        static DateTime gpuTroubleAt = DateTime.MinValue;   // (para apagar_pc_confirmar) la grafica perdida o parada 2 s o mas
        static volatile string gpuStateNow = "ok";          // (para apagar_pc_confirmar) lo ultimo que dijo el motor
        // La imagen parada sin ser la consola (un "cuelgue")
        int hangPid = -1;
        DateTime hangSince;
        volatile int hangKilledPid = -1;
        int hangRecoveries;
        bool stallGpuOk;                                // al darla por apagada, el motor decia que la grafica iba bien
        // ese motor: RunMpv no le espera 5 s a que termine (como mucho 1 s, como con "No Signal"; matado tardaba 4,6 s en salir)
        volatile int stallOffPid = -1;
        int zeroExits;
        DateTime zeroExitSince = DateTime.MinValue;

        const string CierresQuery = "{\"command\":[\"get_property\",\"switch2-captura-ms\"],\"request_id\":79}";

        // MpvLinkLoop, con cada medida del audio
        void CierresAudio(string state) { if (state == "sound") lastSoundAt = DateTime.Now; }

        // MpvReader: cualquier respuesta a time-pos (con o sin dato)
        void CierresReply77() { engReplyAt = DateTime.Now; engReplyOwner = readerMpv; }

        // MpvReader: la respuesta a switch2-captura-ms (request_id 79)
        void CierresCaptura(string line)
        {
            Match m = Regex.Match(line, "\"data\":(-?[0-9]+)");
            if (!m.Success) return;
            int v;
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return;
            engCapMs = v;
            engCapAt = DateTime.Now;
            engCapOwner = readerMpv;
        }

        // MpvReader: el aviso switch2-gpu del motor. true = era ese
        bool CierresEvent(string line)
        {
            if (!line.Contains("\"args\":[\"switch2-gpu\"")) return false;
            Match m = Regex.Match(line, "\"switch2-gpu\",\"([^\"]*)\"(?:,\"((?:[^\"\\\\]|\\\\.)*)\")?");
            string st = m.Success ? m.Groups[1].Value : "?", det = m.Success ? m.Groups[2].Value : "";
            Process owner = readerMpv;
            if (owner != null && owner != mpv) return true;   // (de un motor que ya no es el de ahora)
            engGpuOwner = owner;
            engGpu = st;
            engGpuDetail = det;
            gpuStateNow = st;
            if (st != "ok") engGpuTroubleAt = DateTime.Now;
            if (st == "colgada" || st == "perdida") CrashFrozenGrab(owner);   // (1.2.4 arreglos, RELEVO: la imagen quieta para el telon si se cae, RelevoMotor.cs)
            if (st == "perdida") CrashCurtainEarly(owner);   // (1.2.4 ronda 7, EFGFIX: y el telon con ella ya, RelevoMotor.cs)
            // (para apagar_pc_confirmar solo lo serio: la grafica perdida, el motor cerrado por parado o una parada de 2 s o mas; las
            // cortas de un cambio de ajustes no)
            int stMs;
            if (st == "perdida" || st == "salida" || (st == "ok" && int.TryParse(det, out stMs) && stMs >= 2000)) gpuTroubleAt = DateTime.Now;
            bool ms = st == "colgada" || st == "ok" || st == "salida";
            Log.Write("Motor: grafica " + st + (det.Length > 0 ? " (" + det + (ms ? " ms" : "") + ")" : "") +
                      (st == "colgada" ? ": la imagen no avanza, pero no es la consola apagada" :
                       st == "ok" ? ": la imagen vuelve" :
                       st == "perdida" ? ": el motor se cierra para abrir otro (no es la consola apagada)" :
                       st == "salida" ? ": el motor se cierra solo, parado demasiado tiempo" : ""));
            return true;
        }

        // Por que un corte de imagen NO es la consola apagada (null = nada lo dice)
        string StallNotOff(Process p, DateTime now, double stalled)
        {
            DateTime cut = now.AddMilliseconds(-stalled);
            if (Config.EngineActive)
            {
                if (engGpuOwner == p && (engGpu != "ok" || (engGpuTroubleAt != DateTime.MinValue && engGpuTroubleAt > cut.AddSeconds(-5))))
                    return "el motor dice que la grafica esta parada (" + engGpu + (engGpuDetail.Length > 0 ? ", " + engGpuDetail : "") + ")";
                int cm = engCapMs;
                if (engCapOwner == p && cm >= 0 && cm < 700 && (now - engCapAt).TotalMilliseconds < 1000)
                    return "la capturadora sigue mandando imagenes al motor (la ultima hace " + cm + " ms)";
                if (engReplyOwner == p && engReplyAt != DateTime.MinValue && (now - engReplyAt).TotalMilliseconds >= 1000)
                    return "el motor no contesta desde hace " + (int)(now - engReplyAt).TotalMilliseconds + " ms (colgado: con la consola apagada si contesta)";
            }
            if (lastSoundAt != DateTime.MinValue && lastSoundAt > cut.AddMilliseconds(700))
                return "la consola sigue sonando (" + (int)(lastSoundAt - cut).TotalMilliseconds + " ms despues de pararse la imagen)";
            return null;
        }

        // CheckStall, con la imagen parada PowerOffStallMs: 0 = nada dice que no sea la consola apagada (como siempre); 1 = no es la
        // consola: se espera; 2 = no es la consola y lleva cuelgue_relanzar_ms: cerrado para abrirlo otra vez (RecoverFromHang)
        int StallVeto(Process p, DateTime now, double stalled)
        {
            if (!cfg.StallConfirm) return 0;
            string why = StallNotOff(p, now, stalled);
            bool eng = Config.EngineActive;
            if (why == null)
            {
                if (hangPid == p.Id) Log.Write("La imagen sigue parada (" + (int)stalled + " ms) y ya nada dice que la consola siga encendida: se toma por apagada");
                hangPid = -1;
                // (sin ningun switch2-gpu de este motor: la grafica bien)
                stallGpuOk = eng && (engGpuOwner != p || engGpu == "ok") && engReplyOwner == p && (now - engReplyAt).TotalMilliseconds < 1000;
                stallOffPid = stallGpuOk ? p.Id : -1;
                return 0;
            }
            if (hangPid != p.Id)
            {
                hangPid = p.Id;
                hangSince = now.AddMilliseconds(-stalled);
                Log.Write("No llegan imagenes a la tele desde hace " + (int)stalled + " ms, pero " + why + ": NO es la consola apagada (la grafica o " +
                          "el motor parados); se espera" + (cfg.HangRelaunchMs > 0 ? " y si sigue asi " + (cfg.HangRelaunchMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " s se vuelve a abrir" : ""));
            }
            if (cfg.HangRelaunchMs > 0 && stalled >= cfg.HangRelaunchMs)
            {
                Log.Write("La imagen sigue parada (" + (int)stalled + " ms; " + why + "): se cierra el motor (pid " + p.Id +
                          ") para volver a abrirlo; la app sigue");
                hangPid = -1;
                hangKilledPid = p.Id;
                try { p.Kill(); } catch (Exception ex) { Log.Error("Cerrando la imagen parada", ex); }
                return 2;
            }
            return 1;
        }

        // CheckStall, con la imagen otra vez avanzando
        void StallResumed(Process p, DateTime now)
        {
            if (hangPid != p.Id) return;
            hangPid = -1;
            Log.Write("La imagen vuelve tras " + (int)(now - hangSince).TotalMilliseconds + " ms parada: no era la consola apagada (la app sigue)");
        }

        // RecoverFromGpuReset: hasta cuando esperar el aviso de reinicio del driver. Con el motor diciendo que la grafica iba bien al cortarse
        // la imagen (contestando y sin cuelgue), un reinicio no se parece a esto: poco (el apagado sigue rapido); si no, los 6 s de siempre,
        // pero como poco 2 s desde ahora (el 04:14 del 20-11 el motor tardo 4,8 s en terminar y la espera ya habia pasado)
        DateTime GpuResetDeadline(DateTime cut)
        {
            bool ok = stallGpuOk;
            stallGpuOk = false;
            DateTime d = cut.AddSeconds(ok ? 1.5 : 6), min = DateTime.Now.AddSeconds(ok ? 0.3 : 2);
            return d > min ? d : min;
        }

        // RunMpv: el motor / mpv cerrado por StallVeto (la imagen parada sin ser la consola). true = volver a abrirlo; false = salir de la
        // app sin tomarlo por la consola apagada (demasiadas veces en esta sesion)
        bool RecoverFromHang(Process p, bool hung)
        {
            hangKilledPid = -1;
            hangRecoveries++;
            bool eng = Config.EngineActive;
            if (hangRecoveries > 6)
            {
                Log.Write("La imagen se ha parado " + hangRecoveries + " veces en esta sesion: se cierra la app (no es la consola apagada: el PC no se apaga)");
                return false;
            }
            Log.Write("Imagen parada sin ser la consola (motor pid " + p.Id + (hung ? ", sin terminar aun" : "") + "): se vuelve a abrir (" +
                      hangRecoveries + "a vez en esta sesion)");
            ShowCurtain("Recuperando la imagen…", "La imagen se ha parado", true);
            if (eng)
            {
                engineCrashes++;
                if (engineCrashes >= 3) Log.Write("El motor se ha parado o cerrado " + engineCrashes + " veces: se sigue con el (sin mpv no hay otro camino; ver data\\logs\\engine.log)");
            }
            try { if (!p.HasExited && !p.WaitForExit(30000)) Log.Write("El proceso parado (pid " + p.Id + ") sigue sin terminar: se abre otro igualmente"); }
            catch (Exception) { }
            try { if (File.Exists(Paths.FlagFile)) File.Delete(Paths.FlagFile); } catch { }
            return !userExit;
        }

        // RunMpv (sin imagen aun): el motor ha salido con 0 sin que nadie se lo pidiera (20-11 03:54:38: el motor relanzado tras el reinicio de
        // la grafica, y la app salio con "Cierre normal de mpv; saliendo"). true = volver a abrirlo (como mucho 3 veces en 2 min)
        bool EngineZeroExitRetry(bool eng, Process p)
        {
            if (!eng || userExit || !cfg.StallConfirm) return false;
            if (zeroExitSince == DateTime.MinValue || (DateTime.Now - zeroExitSince).TotalMinutes >= 2) { zeroExits = 0; zeroExitSince = DateTime.Now; }
            if (++zeroExits > 3)
            {
                Log.Write("El motor se ha cerrado sin imagen y sin pedirselo " + (zeroExits - 1) + " veces seguidas: se sale de la app (no es la consola apagada)");
                return false;
            }
            Log.Write("El motor (pid " + p.Id + ") se ha cerrado sin dar imagen y sin que se le pidiera (codigo 0; ver data\\logs\\engine.log): se vuelve a abrir (" + zeroExits + "a vez)");
            ShowCurtain();
            Thread.Sleep(1500);
            return !userExit;
        }

        // Main, al salir por la consola apagada (pantalla "No Signal"): apagar el PC solo si nada dice que fuera la grafica
        public static bool ApagarPcAhora(Config cfg)
        {
            bool ok = true;
            string why = "";
            if (cfg.ShutdownConfirm)
            {
                DateTime now = DateTime.Now;
                DateTime tdr = GpuReset.Since(now.AddSeconds(-60));
                if (tdr != DateTime.MinValue) { ok = false; why = "la grafica se reinicio a las " + tdr.ToString("HH:mm:ss") + " (driver de NVIDIA)"; }
                else if (gpuTroubleAt != DateTime.MinValue && (now - gpuTroubleAt).TotalSeconds < 60) { ok = false; why = "el motor vio la grafica parada o perdida a las " + gpuTroubleAt.ToString("HH:mm:ss"); }
                else if (gpuStateNow == "colgada") { ok = false; why = "el motor tenia la grafica parada al cerrarse"; }
            }
            if (!ok) Log.Write("Consola apagada, pero " + why + " en el ultimo minuto: el PC NO se apaga (apagar_pc_confirmar=1)");
            else if (cfg.ShutdownPc) Log.Write("Consola apagada confirmada (sin reinicios de la grafica ni la grafica parada en el ultimo minuto): se apaga el PC");
            else Log.Write("Consola apagada confirmada (sin reinicios de la grafica ni la grafica parada en el ultimo minuto; apagar_pc=0: el PC sigue)");
            return ok && cfg.ShutdownPc;
        }

        // Main, antes de nada con ventanas: un fallo en la interfaz o en un hilo secundario va al log y la app sigue (antes: el cuadro de
        // error de .NET o el proceso muerto sin dejar rastro). Para que un hilo secundario no mate la app hace falta ademas
        // ConsoleEnhancer.exe.config con legacyUnhandledExceptionPolicy (al lado del exe; en ConsoleEnhancer: resources\ConsoleEnhancer.exe.config,
        // que build.bat y scripts\empaquetar.ps1 ponen junto a ConsoleEnhancer.exe)
        // 21-11 (1.2.1): se llama al principio de Main (antes de la primera ventana) y otra vez donde estaba; la segunda no hace nada. En
        // el primer arranque el asistente ya habia creado ventanas en este hilo: SetUnhandledExceptionMode lanzaba InvalidOperationException
        // ("El modo de excepcion del subproceso no se puede cambiar una vez que se creen Controles") y salia un ERROR en el log. Si no se
        // puede, se sigue sin el (los manejadores de abajo valen igual)
        static bool manejadoresPuestos;
        public static void InstalarManejadoresDeFallos()
        {
            if (manejadoresPuestos) return;
            manejadoresPuestos = true;
            try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); }
            catch (InvalidOperationException) { } // (ya hay ventanas en este hilo: el modo de serie)
            catch (Exception ex) { Log.Error("SetUnhandledExceptionMode", ex); }
            Application.ThreadException += delegate (object o, ThreadExceptionEventArgs a)
            {
                try { Log.Error("Fallo no controlado en la interfaz (la app sigue)", a.Exception); } catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object o, UnhandledExceptionEventArgs a)
            {
                try
                {
                    Exception ex = a.ExceptionObject as Exception;
                    string what = "Fallo no controlado en el hilo \"" + Thread.CurrentThread.Name + "\"" + (a.IsTerminating ? " (la app se cierra)" : " (el hilo acaba; la app sigue)");
                    if (ex != null) Log.Error(what, ex); else Log.Write(what + ": " + a.ExceptionObject);
                }
                catch { }
            };
        }
    }
}
