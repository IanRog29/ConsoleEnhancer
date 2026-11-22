// MotorDlss5.cs - "DLSS 5" en el motor propio o por Magpie, elegible (22-11-2026, 1.2.3, agente MOTOR5)
//
// El usuario: "mejora sobre todo lo de la latencia". DLSS 5 por mpv + Magpie va de la captura a la tele por mpv, la composicion de
// Windows y la captura de Magpie; el motor propio lo hace sobre la captura directa (src\Motor\Engine\EngineDlss5.cs) con
// engine\PrismaNr.dll, que NO se distribuye (solo existe en el PC de quien la compila: sin ella nada de esto se ve ni cambia).
// MEDIDO (22-11, RTX 4060 Ti, tele 3840x2160@120 VRR, modo 30 + DLSS FG x2, DLSS 5 nivel 2 con 1 pasada, fuente de prueba, PresentMon):
//   4K: motor (DLSS 5 al 70 %) real 43,3 ms / inventada 26,7, el 100 % de las imagenes a 2 refrescos y 29,97 reales/s; Magpie (al
//       40-45 %) real ~70-73 ms / inventada ~59, el 65-69 % a +-2 ms y 28,1-28,5 reales/s. 1440p -> 4K (DLSS M): motor 41,9 / 25,3.
// Lo que el motor no hace sigue por Magpie (y Magpie lo hace como siempre, con su escala automatica): mas pasadas de las que hace el
// motor (motor_dlss5_pasadas, de serie 1: Magpie tiene para 2-3 pasadas sus controles de halo / color / enfoque), x3 / x4 (DLSS-G del
// motor solo x2), XeSS FG / NVIDIA VFG. El cambio entre los dos va sin telon (el puente de siempre; el motor del puente suelta DLSS 5:
// las dos DLSSNR a la vez no caben en 8 GB).
// ini motor_dlss5: 1 = motor, 0 = Magpie, auto = motor si esta engine\PrismaNr.dll (de serie). En el menu de ajustes (boton central)
// la fila "DLSS 5 por" y en la bandeja DLSS 5 > "En el motor propio" / "Por Magpie" (solo con PrismaNr.dll).
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
        public int EngineDlss5MaxPasses = 1;
        public string EngineDlss5Ini = "";   // lo que dice el ini (0 / 1 / auto)
        // El motor ha dicho que su DLSS 5 no va (switch2-dlss5 roto: sin PrismaNr.dll que cargue, PNR_Init / Create / Evaluate fallidos):
        // en esta sesion de la app DLSS 5 va por Magpie (antes el motor seguia SIN DLSS 5 sin que el lanzador lo supiera)
        public static volatile bool MotorDlss5Roto;

        // motor_dlss5=auto -> el motor si esta PrismaNr.dll
        public static bool MotorDlss5Valor(string v)
        {
            string t = (v ?? "").Trim().ToLowerInvariant();
            if (t == "auto") return MotorDlss5Dll(Paths.AppDir + @"\engine\ConsoleEnhancerEngine.exe");
            return t == "1" || t == "si" || t == "true" || t == "on" || t == "yes";
        }

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
                                        // lo esperado: el motor con DLSS 5 solo con PrismaNr.dll, elegido, 1 pasada y DLSS FG x2 / Smooth Motion
                                        bool motorDebe = prisma == 1 && eng && pas <= c.EngineDlss5MaxPasses && (t == "dlss" || t == "smooth") && m <= 2 && c.Dlss5FitsNow;
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
                // motor_dlss5=auto
                md5DllPrueba = 1; bool a1 = MotorDlss5Valor("auto"); md5DllPrueba = 0; bool a0 = MotorDlss5Valor("auto");
                bool aOk = a1 && !a0 && MotorDlss5Valor("1") && !MotorDlss5Valor("0");
                n++; if (!aOk) fallos++;
                sb.AppendLine((aOk ? "      " : "FALLO ") + "motor_dlss5=auto: con PrismaNr.dll " + (a1 ? 1 : 0) + ", sin ella " + (a0 ? 1 : 0));
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
        // Elegido en el menu de ajustes o en la bandeja: al ini y, con la imagen en marcha, al otro camino (RestartLs: sin telon si se puede)
        void SetMotorDlss5(bool on, string desde, bool reabrir)
        {
            if (cfg.EngineDlss5 == on) { if (reabrir) Osd("DLSS 5 · " + MotorDlss5Texto(on) + Tx.T(" · ya activo", " · already on")); return; }
            cfg.EngineDlss5 = on;
            cfg.EngineDlss5Ini = on ? "1" : "0";
            try { Ce.IniPoner("motor_dlss5", on ? "1" : "0"); } catch (Exception ex) { Log.Error("motor_dlss5 al ini", ex); }
            PrepareCfg();
            Log.Write("DLSS 5 " + (on ? "en el motor propio (menos retraso)" : "por Magpie (mas opciones)") + " (" + desde + "; motor_dlss5=" + (on ? 1 : 0) + ")" +
                      (on && cfg.MotorDlss5PorQueMagpie(false).Length > 0 ? "; ahora " + cfg.MotorDlss5PorQueMagpie(false) : ""));
            if (reabrir) RestartLs("DLSS 5 · " + MotorDlss5Texto(on), on ? Tx.T("menos retraso", "lower latency") : Tx.T("más opciones", "more options"));
        }

        static string MotorDlss5Texto(bool on) { return on ? Tx.T("motor propio", "built-in engine") : "Magpie"; }

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

        // ---- menu de ajustes (MenuAjustes.cs): la fila "DLSS 5 por" (solo con PrismaNr.dll y DLSS 5)
        FilaAjuste AjFilaMotorDlss5()
        {
            FilaAjuste f = new FilaAjuste { Id = "d5motor", Nombre = Tx.T("DLSS 5 por", "DLSS 5 through") };
            string v = cfg.EngineDlss5 ? "1" : "0";
            f.Poner(new List<string> { "1", "0" },
                    new List<string> { Tx.T("Motor · menos retraso", "Engine · lower latency"), Tx.T("Magpie · más opciones", "Magpie · more options") }, v, v);
            f.Visible = Integrado.Dlss5Available && cfg.MotorDlss5Disponible;
            f.Nota = cfg.MotorDlss5PorQueMagpie(Tx.Ingles);
            return f;
        }

        // La nota de la fila con lo elegido en el menu (aun sin aplicar): que va por Magpie aunque se elija el motor
        string AjNotaMotorDlss5()
        {
            FilaAjuste m = AjFila("d5motor");
            if (m == null || m.Val != "1") return "";
            FilaAjuste pas = AjFila("pasadas"), tipo = AjFila("tipo"), mult = AjFila("mult"), fg = AjFila("fg");
            int np; if (pas == null || !int.TryParse(pas.Val, NumberStyles.Integer, CultureInfo.InvariantCulture, out np)) np = dlss5Passes;
            if (np > Math.Max(1, Math.Min(3, cfg.EngineDlss5MaxPasses))) return Tx.T(np + " pasadas: por Magpie", np + " passes: through Magpie");
            bool fgSi = fg == null || fg.Val == "1";
            string t = tipo != null && tipo.Activa ? tipo.Val : "";
            if (fgSi && (t == "xess" || t == "vfg")) return Tx.T((t == "xess" ? "XeSS FG" : "NVIDIA VFG") + ": por Magpie", (t == "xess" ? "XeSS FG" : "NVIDIA VFG") + ": through Magpie");
            if (fgSi && t == "dlss" && mult != null && mult.Visible && (mult.Val == "3" || mult.Val == "4")) return Tx.T("x" + mult.Val + ": por Magpie", "x" + mult.Val + ": through Magpie");
            return "";
        }

        // (AjustesPonerTodo, con applying: sin reabrir; lo reabre su RestartLs del final)
        void AjMotorDlss5Poner(Dictionary<string, string> e)
        {
            string v;
            if (!e.TryGetValue("d5motor", out v) || v == null) return;
            bool on = v == "1";
            if (on != cfg.EngineDlss5) SetMotorDlss5(on, "menu de ajustes", false);
        }

        // ---- bandeja (MenuJuego.cs): DLSS 5 > En el motor propio / Por Magpie
        ToolStripMenuItem bMd5Motor, bMd5Magpie;
        ToolStripSeparator bMd5Sep;
        void BandejaMotorDlss5(ToolStripMenuItem d5)
        {
            bMd5Sep = new ToolStripSeparator();
            bMd5Motor = new ToolStripMenuItem(Tx.T("En el motor propio (menos retraso)", "In the built-in engine (lower latency)"), null, delegate
            {
                DesdeBandeja("DLSS 5 en el motor propio", true, delegate { SetMotorDlss5(true, "bandeja", true); });
            });
            bMd5Magpie = new ToolStripMenuItem(Tx.T("Por Magpie (más opciones)", "Through Magpie (more options)"), null, delegate
            {
                DesdeBandeja("DLSS 5 por Magpie", true, delegate { SetMotorDlss5(false, "bandeja", true); });
            });
            d5.DropDownItems.Add(bMd5Sep);
            d5.DropDownItems.Add(bMd5Motor);
            d5.DropDownItems.Add(bMd5Magpie);
        }

        void BandejaMotorDlss5Marcar()
        {
            if (bMd5Motor == null) return;
            bool hay = cfg.MotorDlss5Disponible;
            bMd5Sep.Visible = bMd5Motor.Visible = bMd5Magpie.Visible = hay;
            bMd5Motor.Checked = cfg.EngineDlss5;
            bMd5Magpie.Checked = !cfg.EngineDlss5;
            string porque = cfg.MotorDlss5PorQueMagpie(Tx.Ingles);
            bMd5Motor.ToolTipText = porque.Length > 0 ? Tx.T("Ahora ", "Now ") + porque : null;
        }
    }
}
