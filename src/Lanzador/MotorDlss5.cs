// MotorDlss5.cs - "DLSS 5" en el motor propio o por Magpie, elegible (22-11-2026, 1.2.3, agente MOTOR5)
//
// El usuario: "mejora sobre todo lo de la latencia". DLSS 5 por mpv + Magpie va de la captura a la tele por mpv, la composicion de
// Windows y la captura de Magpie; el motor propio lo hace sobre la captura directa (src\Motor\Engine\EngineDlss5.cs) con
// engine\PrismaNr.dll, que NO se distribuye (solo existe en el PC de quien la compila: sin ella nada de esto se ve ni cambia).
// MEDIDO (22-11, RTX 4060 Ti, tele 3840x2160@120 VRR, modo 30 + DLSS FG x2, DLSS 5 nivel 2 con 1 pasada, fuente de prueba, PresentMon):
//   4K: motor (DLSS 5 al 70 %) real 43,3 ms / inventada 26,7, el 100 % de las imagenes a 2 refrescos y 29,97 reales/s; Magpie (al
//       40-45 %) real ~70-73 ms / inventada ~59, el 65-69 % a +-2 ms y 28,1-28,5 reales/s. 1440p -> 4K (DLSS M): motor 41,9 / 25,3.
// Lo que el motor no hace sigue por Magpie (y Magpie lo hace como siempre, con su escala automatica): mas pasadas de las que hace el
// motor (motor_dlss5_pasadas, de serie 1 en la 1.2.3 y 3 desde la 1.2.4: Magpie tiene para 2-3 pasadas sus controles de halo / color / enfoque), x3 / x4 (DLSS-G del
// motor solo x2), XeSS FG / NVIDIA VFG. El cambio entre los dos va sin telon (el puente de siempre; el motor del puente suelta DLSS 5:
// las dos DLSSNR a la vez no caben en 8 GB).
// 1.2.4 (SOLOMOTOR; el usuario: "y no se podria pasar todo al motor para que funcione mejor?"): ya no se elige (fuera motor_dlss5, la fila
// "DLSS 5 por" del menu de ajustes, la bandeja y la pregunta de "Añadir DLSS 5"): con engine\PrismaNr.dll, DLSS 5 SIEMPRE en el motor si
// sabe hacer la combinacion (el reparto, en SoloMotor.cs); sin ella, por Magpie (reserva interna).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // ini motor_dlss5_pasadas (1-3): hasta cuantas pasadas de DLSS 5 hace el motor; con mas, Magpie
        public int EngineDlss5MaxPasses = 3;   // (1.2.4 SOLOMOTOR: 3 de serie, las 1-3 pasadas en el motor; antes 1)
        // El motor ha dicho que su DLSS 5 no va (switch2-dlss5 roto: sin PrismaNr.dll que cargue, PNR_Init / Create / Evaluate fallidos):
        // en esta sesion de la app DLSS 5 va por Magpie (antes el motor seguia SIN DLSS 5 sin que el lanzador lo supiera)
        public static volatile bool MotorDlss5Roto;


        // PrismaNr.dll junto al motor (mirado como mucho cada 5 s: lo piden muchas cuentas por segundo)
        static string md5DllExe; static DateTime md5DllAt; static bool md5DllOk;
        static readonly object md5DllSync = new object();
        static int md5DllPrueba = -1;   // (--test-motor-dlss5: 0 / 1 = como si no estuviera / estuviera)
        public static bool MotorDlss5Dll(string enginePath)
        {
            if (md5DllPrueba >= 0) return md5DllPrueba == 1;
            try
            {
                string exe = string.IsNullOrEmpty(enginePath) ? Paths.AppDir + @"\engine\ConsoleEnhancerEngine.exe" : enginePath;
                lock (md5DllSync)
                {
                    if (exe == md5DllExe && (DateTime.Now - md5DllAt).TotalSeconds < 5) return md5DllOk;
                    md5DllExe = exe; md5DllAt = DateTime.Now;
                    md5DllOk = File.Exists(Path.Combine(Path.GetDirectoryName(exe), "PrismaNr.dll"));
                    return md5DllOk;
                }
            }
            catch { return false; }
        }
        // (--test-solo-motor: 0 / 1 = como si no estuviera / estuviera PrismaNr.dll; -1 = de verdad)
        public static void MotorDlss5Prueba1(int v) { md5DllPrueba = v; md5DllAt = DateTime.MinValue; }
        public bool MotorDlss5Disponible { get { return Engine == "propio" && MotorDlss5Dll(EnginePath); } }

        // ¿Hace el motor DLSS 5 con lo pedido ahora? (las pasadas PEDIDAS, no las que caben: va dentro de la cuenta de Dlss5PassesNow, de la
        // escala automatica de Magpie y de DlssFgPorMagpieSinRuntime; lo de la generacion que solo hace Magpie con su guarda)
        [ThreadStatic] static bool md5Dentro;
        public bool MotorDlss5Pedido
        {
            get
            {
                if (!EngineDlss5 || MotorDlss5Roto || !MotorDlss5Disponible) return false;
                if (Math.Max(1, Math.Min(3, Passes30)) > Math.Max(1, Math.Min(3, EngineDlss5MaxPasses))) return false;
                if (md5Dentro) return true;
                md5Dentro = true;
                try { return !FgMagpieOnly && !ScalerMagpieOnly; }
                finally { md5Dentro = false; }
            }
        }

        // --test-motor-dlss5[=<informe>]: por donde va DLSS 5 (motor / Magpie) con cada combinacion, sin abrir nada ni la grafica.
        // Devuelve los fallos frente a lo esperado (0 = bien)
        public static int MotorDlss5Prueba(string informe)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder("# MOTOR5: DLSS 5 por el motor o por Magpie (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")\r\n");
            int fallos = 0, n = 0;
            System.Reflection.FieldInfo f5 = typeof(Integrado).GetField("dlss5State", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            System.Reflection.FieldInfo fg = typeof(Integrado).GetField("dlssgState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (f5 != null) f5.SetValue(null, 1);   // (como si estuviera nvngx_dlssnr.dll en magpie\)
            if (fg != null) fg.SetValue(null, 1);   // (y runtimes\dlssg_sm86)
            try
            {
                Config c = Config.Load();
                c.Engine = "propio";
                if (!File.Exists(c.EnginePath)) c.EnginePath = System.Reflection.Assembly.GetExecutingAssembly().Location;   // (EngineNow mira que exista)
                sb.AppendLine("motor_dlss5_pasadas=" + c.EngineDlss5MaxPasses + " dlss5_escala_auto=" + (c.Dlss5AutoOn ? 1 : 0) + " (ini " + Paths.IniFile + ")");
                sb.AppendLine("prisma motor_dlss5 captura tipo xN pasadas -> EngineNow EngineDlss5Now escala_auto_Magpie tipo_real xN_real | por que Magpie");
                foreach (int prisma in new[] { 0, 1 })
                    foreach (bool eng in new[] { false, true })
                        foreach (string cap in new[] { "3840x2160", "2560x1440" })
                            foreach (string tipo in new[] { "dlss", "smooth", "xess" })
                                foreach (int mult in new[] { 2, 4 })
                                    foreach (int pas in new[] { 1, 2 })
                                    {
                                        if (mult > 2 && tipo == "smooth") continue;
                                        md5DllPrueba = prisma; md5DllAt = DateTime.MinValue;
                                        MotorDlss5Roto = false;
                                        c.EngineDlss5 = eng; c.VideoSize = cap; c.CapMode = true; c.Mode120 = false; c.FgOff = false; c.Dlss5Off = false; c.HomeNative = false;
                                        c.Passes30 = pas; c.Level30 = 2; c.FgPick30 = tipo; c.MultPick30 = mult; c.ScalerPick = ""; c.AltScalerOn = false;
                                        bool en = c.EngineNow, ed = c.EngineDlss5Now, au = c.Dlss5AutoAhora;
                                        string t = c.FgTypeNow; int m = c.FgMultNow;
                                        // lo esperado: el motor con DLSS 5 solo con PrismaNr.dll, elegido, las pasadas que hace y una generacion
                                        // que sepa hacer (1.2.4: el reparto de SoloMotor.cs / MotorFg.cs: DLSS FG x2, Smooth Motion, XeSS FG x2...)
                                        bool motorDebe = prisma == 1 && eng && pas <= c.EngineDlss5MaxPasses && MotorSabeGeneracion(t, m) && c.Dlss5FitsNow;
                                        bool ok = ed == motorDebe && (!motorDebe || en) && (!motorDebe ? au == (c.Dlss5AutoOn && c.Dlss5InputPct == 0) : !au) &&
                                                  (motorDebe || !c.Dlss5FitsNow || !en);
                                        n++; if (!ok) fallos++;
                                        sb.AppendLine((ok ? "      " : "FALLO ") + prisma + " " + (eng ? 1 : 0) + " " + cap + " " + tipo + " x" + mult + " p" + pas + " -> " + (en ? "motor" : "magpie") + " " +
                                                      (ed ? "dlss5_motor" : "-") + " " + (au ? "auto" : "-") + " " + t + " x" + m + " | " + c.MotorDlss5PorQueMagpie(false));
                                    }
                // el motor dice que su DLSS 5 no va: por Magpie
                md5DllPrueba = 1; md5DllAt = DateTime.MinValue; MotorDlss5Roto = true;
                c.EngineDlss5 = true; c.VideoSize = "3840x2160"; c.Passes30 = 1; c.FgPick30 = "dlss"; c.MultPick30 = 2;
                bool rOk = !c.EngineDlss5Now && (!c.Dlss5FitsNow || !c.EngineNow);
                n++; if (!rOk) fallos++;
                sb.AppendLine((rOk ? "      " : "FALLO ") + "motor con su DLSS 5 roto -> " + (c.EngineNow ? "motor" : "magpie") + " " + (c.EngineDlss5Now ? "dlss5_motor" : "-"));
                MotorDlss5Roto = false;
            }
            catch (Exception ex) { fallos++; sb.AppendLine("FALLO excepcion: " + ex); }
            finally { md5DllPrueba = -1; MotorDlss5Roto = false; }
            sb.AppendLine(n + " pruebas, " + fallos + " fallos");
            string ruta = string.IsNullOrEmpty(informe) ? Path.Combine(Paths.Datos, @"games\test_motor_dlss5.txt") : informe;
            try { File.WriteAllText(ruta, sb.ToString(), new System.Text.UTF8Encoding(false)); } catch { }
            Console.Write(sb.ToString());
            return fallos;
        }

        // Por que va por Magpie aunque se haya elegido el motor ("" = va por el motor, o no se eligio)
        public string MotorDlss5PorQueMagpie(bool ingles)
        {
            if (!EngineDlss5 || !MotorDlss5Disponible || Dlss5Off) return "";
            if (Math.Max(1, Math.Min(3, Passes30)) > Math.Max(1, Math.Min(3, EngineDlss5MaxPasses)))
                return ingles ? Passes30 + " passes: through Magpie" : Passes30 + " pasadas: por Magpie";
            if (MultPorMagpie) return ingles ? "x" + FgMultNow + ": through Magpie" : "x" + FgMultNow + ": por Magpie";
            if (FgMagpieOnly) { string t = FgTypeNow; string n = t == "xess" ? "XeSS FG" : t == "fsr3" ? "FSR 3 FG" : t == "vfg" ? "NVIDIA VFG" : t; return ingles ? n + ": through Magpie" : n + ": por Magpie"; }
            return "";
        }
    }

    partial class TrayApp
    {

        // MpvReader: {"event":"client-message","args":["switch2-dlss5","roto","<motivo>"]} del motor (EngineDlss5.cs NrFail). true = era ese
        bool MotorDlss5Evento(string line)
        {
            if (!line.Contains("\"args\":[\"switch2-dlss5\"")) return false;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line, "\"switch2-dlss5\",\"([^\"]*)\"(?:,\"((?:[^\"\\\\]|\\\\.)*)\")?");
            string st = m.Success ? m.Groups[1].Value : "?", det = m.Success ? m.Groups[2].Value : "";
            if (st != "roto") return true;
            bool usado = cfg.EngineDlss5Now && Config.EngineActive;
            Config.MotorDlss5Roto = true;
            Log.Write("Motor: su DLSS 5 no va (" + det + ")" + (usado ? ": DLSS 5 por Magpie en esta sesion (se cambia de camino)" : ": en esta sesion DLSS 5 ira por Magpie"));
            if (usado && !cfg.Dlss5Off)
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try { System.Threading.Thread.Sleep(300); RestartLs("DLSS 5 · Magpie", Tx.T("el motor no pudo con DLSS 5", "the engine could not run DLSS 5")); }
                    catch (Exception ex) { Log.Error("DLSS 5 del motor roto: a Magpie", ex); }
                });
            return true;
        }

    }
}
