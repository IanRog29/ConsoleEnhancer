// SmoothX4.cs - 23-11-2026 (1.2.4, sexta ronda, agente SMX4): "Smooth Motion x4" en el modo 30 con la tele a 120 Hz y VRR.
//
// Smooth Motion del driver de NVIDIA solo inventa UNA imagen entre cada dos Present de la aplicacion (x2; ningun driver, hasta el 621.24 de
// noviembre de 2026, tiene mas, y el multiplicador de DLSS-G del perfil -0x104D6667- no le cambia nada: medido). Para 120/s con 30 reales el
// motor tiene que presentar 60/s DISTINTAS y que Smooth Motion doble eso. Medido el 23-11 (RTX 4060 Ti, driver 610.88, 4K, motor solo con la
// fuente de archivo, PresentMon; ver notas\SMX4.md): la unica combinacion que va pareja Y con poca latencia es XeSS FG x2 del motor (que ya
// espacia sus dos imagenes) + Smooth Motion en el perfil de ConsoleEnhancerEngine.exe SIN forzar la sincronizacion (VRR): 118,7/s, el
// 97,7 % de las imagenes a 1 refresco, las 30 reales, la 1a imagen con algo de la real nueva a 14,2 ms y la real a 39,8 ms de su llegada
// (Smooth Motion x2: 19,8 / 36,5; Magpie x4 de la 1.2.3: 72 / 81). Con la sincronizacion forzada del driver XeSS FG + Smooth Motion va MAL
// (la mitad de las imagenes no se ven); FSR 3 FG + Smooth Motion tambien va mal.
// Aqui: la clave del ini y cuando se puede. Lo hacen (una linea cada uno): Config.EngineSmoothMode (3 = este modo), NvProfiles.SetEngineSmooth
// (3 = Smooth Motion sin tocar la sincronizacion: la de la aplicacion), EngineLive (fg=1 aunque vaya Smooth Motion) y EngineFgChain (la
// cadena de XeSS FG x2). Si algo no esta (otra grafica, sin VRR, sin libxess_fg.dll, XeSS FG roto en esta sesion...), Smooth Motion x2 como
// siempre.
// ini smooth_x4=0|1 (de serie 0: se elige; 1 = en el modo 30 con Smooth Motion, x4 cuando se pueda)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public bool SmoothX4 = false;   // smooth_x4
        // (1.2.4 ronda 7, FGREAL) x4 con DLSS 5: el motor ya reserva la grafica de XeSS FG + las dos inventadas del driver (EngineMargen.cs) y la
        // escala de DLSS 5 baja para que quepa, pero aun asi no llega: medido el 23-11 (4K, modo 30, DLSS 5 nivel 4, tele 120 VRR): DLSS 5 del
        // 65 al 55 %, el motor a 15,5 ms y 25-27 reales de 30 -el Present de XeSS FG espera a la grafica 31,6 ms (sin DLSS 5, 2,1)-, en la tele
        // el 43 % de las imagenes pisadas. Con DLSS 5, x2 (y la nota del menu lo dice). smooth_x4_dlss5=1 lo deja probar
        // (1.2.4 ronda 7, EFGFIX: de serie SI. El usuario: "no se porque con dlss 5 activado no dejas activar el x4 de smooth y enhance". Con la
        // cola del motor en prioridad alta con sm=3 y DLSS 5 bajado lo que haga falta -el vigilante de EngineX4D5.cs mira las reales, no el p95 del
        // motor, que no ve XeSS FG ni el driver-: medido con el video real de GTA V a 4K, DLSS 5 nivel 4: al 45 % 27,6 -> 29,6 reales con la
        // prioridad, al 30 % 29,4 sin ella (ver notas\EFGFIX.md). Si ni al 25 % llega, x2 esta sesion y se dice -SmoothX4NoLlegaAnota-.
        // smooth_x4_dlss5=0 = como antes: x2 con DLSS 5)
        public static volatile bool SmoothX4Dlss5 = true;
        // (FGREAL) FpsBajo: x4 no llego a las reales con esta captura y DLSS 5 si / no: en esta sesion x2 con eso (se puede volver a elegir
        // con otra captura o con DLSS 5 cambiado; la nota del menu lo dice)
        static string smx4NoLlega = "";
        static string Smx4Clave(bool d5, string cap) { return (cap ?? "") + "|" + (d5 ? "d5" : "-"); }
        static string Smx4Cap(string c) { return c == "3840x2160" ? "4K" : c == "2560x1440" ? "1440p" : c == "1920x1080" ? "1080p" : c == "1280x720" ? "720p" : c ?? ""; }
        public static void SmoothX4NoLlegaAnota(bool d5, string cap) { smx4NoLlega = Smx4Clave(d5, cap); }

        public bool CeParseSmoothX4(string key, string val)
        {
            if (key == "smooth_x4_dlss5") { SmoothX4Dlss5 = val.Trim() != "0"; return true; }   // (1.2.4 ronda 7, EFGFIX: de serie 1)
            if (key != "smooth_x4") return false;
            SmoothX4 = val.Trim() == "1";
            return true;
        }

        // Lo de DLSS 5 / la captura que impide x4 ("" = nada): con d5 = DLSS 5 encendido (en el menu, lo elegido en el menu)
        string SmoothX4FaltaD5(bool d5, string cap)
        {
            if (d5 && !SmoothX4Dlss5) return Tx.T("con DLSS 5 va x2: la gráfica no llega (sin DLSS 5, x4)",
                                                  "with DLSS 5 it runs x2: the GPU can't keep up (x4 without DLSS 5)");
            if (smx4NoLlega.Length > 0 && smx4NoLlega == Smx4Clave(d5, cap))
                return d5 ? Tx.T("con DLSS 5 a " + Smx4Cap(cap) + " no llegó a 30: x2 esta sesión",
                                 "with DLSS 5 at " + Smx4Cap(cap) + " it couldn't keep 30 fps: x2 this session")
                          : Tx.T("a " + Smx4Cap(cap) + " no llegó a 30: x2 esta sesión", "at " + Smx4Cap(cap) + " it couldn't keep 30 fps: x2 this session");
            return "";
        }

        // Lo que falta para Smooth Motion x4 ahora ("" = se puede). Sin propiedades que miren EngineSmoothMode (lo usa el)
        public string SmoothX4Falta
        {
            get
            {
                if (!SmoothX4) return "smooth_x4=0";
                if (FgOff || HomeNative || Mode120 || !CapMode) return Tx.T("solo en el modo 30", "only in the 30 fps mode");
                if (!EngineSmoothFor(true)) return Tx.T("la generación del modo 30 no es Smooth Motion", "the 30 fps mode generation is not Smooth Motion");
                if (Ce.NoNvidia || NvProfiles.EngineSmoothBroken) return Tx.T("Smooth Motion no se puede poner en este PC", "Smooth Motion can't be set on this PC");
                if (Pantalla.Hz() < 119) return Tx.T("la pantalla no va a 120 Hz", "the screen is not at 120 Hz");
                if (EngineVrr <= 0) return Tx.T("sin VRR (G-SYNC) en la pantalla", "no VRR (G-SYNC) on the screen");
                // (1.2.4 ronda 6, INTEGRA6: con DLSS 5 no llega: 16,4 reales de 29,97 en el banco de SMX4 -y_xe2sm_v_d5, DLSS 5 al 90 % y sin la
                // escala automatica- y 22,5 en la app entera. Ronda 7, FGREAL: con la reserva de grafica nueva y la escala bajando, 25-27; ver
                // SmoothX4Dlss5. Con smooth_x4_dlss5=1 se deja, salvo si FpsBajo ya lo bajo con esto en esta sesion)
                { string d5 = SmoothX4FaltaD5(MotorD5Simple, VideoSize); if (d5.Length > 0) return d5; }
                // (1.2.4 ronda 6, integracion: sin Magpie ni el multiplicador viejo, lo mismo que pide el tipo XeSS FG -DLL, el motor, la captura y
                // que no se haya caido en esta sesion, XessCaido-)
                if (!GenPuedeIr("xess", VideoSize)) return XessCaido ? Tx.T("XeSS FG se ha caído en el motor en esta sesión", "XeSS FG failed in the engine this session")
                                                                   : Tx.T("XeSS FG no está o no va en el motor", "XeSS FG is missing or does not run in the engine");
                return "";
            }
        }
        public bool SmoothX4Now { get { return SmoothX4Falta.Length == 0; } }

        // (1.2.4 ronda 6, INTEGRA6) Para el menu: lo que falta para x4 que no depende del modo / tipo elegidos ("" = se puede). (1.2.4 ronda 7,
        // FGREAL: con DLSS 5 y la captura ELEGIDOS EN EL MENU, no los de ahora: el 23-11 el usuario eligio "Smooth Motion, x4, DLSS 5 Si" a la
        // vez con DLSS 5 apagado -x4 sin candado-, se guardo x4 y fue x2 sin decirlo en pantalla; luego, con DLSS 5, el x4 "aparecio bloqueado")
        public string SmoothX4FaltaMenu(bool d5, string cap)
        {
            if (Ce.NoNvidia || NvProfiles.EngineSmoothBroken) return Tx.T("Smooth Motion no se puede poner en este PC", "Smooth Motion can't be set on this PC");
            if (Pantalla.Hz() < 119) return Tx.T("La pantalla no va a 120 Hz", "The screen is not at 120 Hz");
            if (EngineVrr <= 0) return Tx.T("Sin VRR (G-SYNC) en la pantalla", "No VRR (G-SYNC) on the screen");
            { string f = SmoothX4FaltaD5(d5, cap); if (f.Length > 0) return f.Substring(0, 1).ToUpperInvariant() + f.Substring(1); }
            if (!GenPuedeIr("xess", string.IsNullOrEmpty(cap) ? VideoSize : cap)) return XessCaido ? Tx.T("XeSS FG se ha caído en el motor en esta sesión", "XeSS FG failed in the engine this session")
                                                               : Tx.T("Necesita XeSS FG en el motor (falta su DLL en xess\\)", "Needs XeSS FG in the engine (its DLL is missing in xess\\)");
            return "";
        }

        // Para el log: una vez por cambio
        static string smoothX4Dicho;
        public void SmoothX4Log()
        {
            string f = SmoothX4 ? SmoothX4Falta : "";
            if (f == smoothX4Dicho) return;
            smoothX4Dicho = f;
            if (!SmoothX4) return;
            Log.Write(f.Length == 0 ? "Smooth Motion x4: XeSS FG x2 del motor + Smooth Motion del driver encima (sin forzar la sincronizacion, VRR): 30 -> 120"
                                    : "Smooth Motion x4 pedido (smooth_x4=1) pero no ahora: " + f + " (Smooth Motion x2)");
        }

        // --test-smooth-x4[=<informe>]: cuando hay x4 y lo que llega al perfil del driver y al motor, sin abrir nada (sin tocar el perfil)
        public static int SmoothX4Prueba(string informe)
        {
            StringBuilder sb = new StringBuilder("# SMX4: Smooth Motion x4 (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "), pantalla a " +
                                                 Pantalla.Hz().ToString("0.##", CultureInfo.InvariantCulture) + " Hz\r\n");
            int fallos = 0;
            Config c = Config.Load();
            c.Engine = "propio";
            MethodInfo live = typeof(TrayApp).GetMethod("EngineLive", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Config), typeof(bool) }, null);
            bool hz120 = Pantalla.Hz() >= 119;
            // caso: x4 pedido, nada falta | sin pedirlo | sin VRR | modo 60 | otro tipo en el 30 | a 120 | generacion apagada
            string[] casos = { "x4", "sin_clave", "sin_vrr", "modo60", "xess30", "modo120", "fg_off" };
            foreach (string caso in casos)
            {
                c.SmoothX4 = caso != "sin_clave"; c.EngineVrr = caso == "sin_vrr" ? 0 : 1; c.CapMode = caso != "modo60" && caso != "modo120"; c.Mode120 = caso == "modo120";
                c.FgOff = caso == "fg_off"; c.HomeNative = false; c.Dlss5Off = true; c.VideoSize = "3840x2160";
                c.FgPick30 = caso == "xess30" ? "xess" : "smooth"; c.FgPick60 = "smooth";
                bool x4 = c.SmoothX4Now;
                int sm = c.EngineSmoothMode, chain = c.EngineFgChain;
                string args = c.MotorFgArgs(chain).Trim(), lv = live != null ? (string)live.Invoke(null, new object[] { c, sm > 0 }) : "?";
                bool fg1 = lv.StartsWith("fg=1");
                bool esperaX4 = caso == "x4" && hz120 && GenPuedeIr("xess", c.VideoSize);
                bool ok = x4 == esperaX4;
                if (esperaX4) ok = ok && sm == 3 && (chain == 20 || chain == 21) && args.Contains("fg_tipo=xess") && fg1;   // (1.2.4 ronda 6: sin fg_mult, XeSS FG siempre x2)
                else if (caso == "sin_clave" || caso == "sin_vrr") ok = ok && sm == (hz120 ? 2 : 1) && chain == 0 && !fg1;   // (Smooth Motion x2 como siempre)
                else if (caso == "modo120" || caso == "fg_off") ok = ok && sm == 0;
                else if (caso == "xess30") ok = ok && sm == 0;
                if (!ok) fallos++;
                sb.AppendLine((ok ? "bien  " : "FALLO ") + caso + ": x4=" + x4 + " (" + (c.SmoothX4Falta.Length > 0 ? c.SmoothX4Falta : "nada falta") + ") sm=" + sm + " cadena=" + chain +
                              " motor: '" + args + "' " + (lv.Length > 40 ? lv.Substring(0, 40) : lv));
            }
            // (1.2.4 ronda 7, FGREAL) el candado del menu con lo ELEGIDO en el menu: DLSS 5 si -> x4 bloqueado con el motivo real (de serie,
            // smooth_x4_dlss5=0); DLSS 5 no -> nada (si nada mas falta); y FpsBajo: x4 que no llego con esta captura y DLSS 5 -> bloqueado esta sesion
            {
                c.SmoothX4 = true; c.EngineVrr = 1; c.CapMode = true; c.Mode120 = false; c.FgOff = false; c.Dlss5Off = true; c.FgPick30 = "smooth";
                bool base4 = hz120 && GenPuedeIr("xess", "3840x2160");
                string m5 = c.SmoothX4FaltaMenu(true, "3840x2160"), m0 = c.SmoothX4FaltaMenu(false, "3840x2160");
                bool ok = (SmoothX4Dlss5 || m5.Length > 0) && (!base4 || m0.Length == 0);
                if (!ok) fallos++;
                sb.AppendLine((ok ? "bien  " : "FALLO ") + "menu: con DLSS 5 elegido '" + m5 + "' | sin DLSS 5 '" + m0 + "'");
                SmoothX4NoLlegaAnota(false, "3840x2160");
                string mn = c.SmoothX4FaltaMenu(false, "3840x2160"), m1440 = c.SmoothX4FaltaMenu(false, "2560x1440");
                ok = mn.Length > 0 && (!base4 || m1440.Length == 0) && !c.SmoothX4Now;
                if (!ok) fallos++;
                sb.AppendLine((ok ? "bien  " : "FALLO ") + "no llego a 4K sin DLSS 5: 4K '" + mn + "' | 1440p '" + m1440 + "' | x4 ahora " + c.SmoothX4Now);
                smx4NoLlega = "";
            }
            sb.AppendLine(fallos == 0 ? "Todo bien" : fallos + " fallo(s)");
            string txt = sb.ToString();
            Console.Write(txt);
            try { if (!string.IsNullOrEmpty(informe)) File.WriteAllText(informe, txt, new UTF8Encoding(false)); } catch { }
            return fallos == 0 ? 0 : 1;
        }
    }

    partial class TrayApp
    {
        // (1.2.4 ronda 6, INTEGRA6) El menu de ajustes: con Smooth Motion en el modo 30, la fila "Multiplicador" x2 / x4 (x4 = smooth_x4=1: XeSS FG x2
        // del motor debajo + Smooth Motion del driver encima, con VRR; de serie x2). x4 con candado y el motivo cuando no se puede aqui
        FilaAjuste SmoothX4Fila()
        {
            FilaAjuste mul = new FilaAjuste { Id = "smx4", Nombre = Tx.T("Multiplicador", "Multiplier") };
            string m = cfg.SmoothX4 ? "4" : "2";
            mul.Poner(new List<string> { "2", "4" }, new List<string> { "x2", "x4" }, m, m);
            mul.Visible = false;
            if (!ajRefrescando) smx4Quiere = m;   // (1.2.4 ronda 7, INTEGRA7: en un refresco del menu abierto, lo elegido sin aplicar se queda)
            return mul;
        }

        // (1.2.4 ronda 7, FGREAL) lo que el usuario tiene elegido en la fila (aunque ahora se enseñe x2 porque x4 no puede ir con lo elegido)
        string smx4Quiere = "2";
        void SmoothX4Tocada(FilaAjuste f) { if (f != null && f.Id == "smx4") smx4Quiere = f.Val; }

        // (AjustesDepende, tras el tipo) visible con Smooth Motion elegido en el modo 30. (1.2.4 ronda 7, FGREAL: el candado con DLSS 5 y la
        // captura elegidos en el menu; si x4 no puede ir, la fila enseña x2 -lo que va a ir de verdad- con el motivo, y lo elegido -x4- se
        // queda guardado: vuelve solo cuando se pueda. Antes enseñaba x4 aunque fuera x2 y, al elegir x2, x4 quedaba con candado)
        void SmoothX4Depende(string modoSel)
        {
            FilaAjuste tipo = AjFila("tipo"), fg = AjFila("fg"), mul = AjFila("smx4"), d5 = AjFila("d5"), cap = AjFila("cap");
            if (mul == null) return;
            bool on = tipo != null && tipo.Visible && tipo.Val == "smooth" && modoSel == "30";
            bool fgSi = fg == null || fg.Val == "1";
            bool d5Sel = d5 != null && d5.Visible ? d5.Val == "1" : cfg.MotorD5Simple;
            string capSel = cap != null ? cap.Val : cfg.VideoSize;
            string no = cfg.SmoothX4FaltaMenu(d5Sel && Integrado.Dlss5Available, capSel);
            string guardado = cfg.SmoothX4 ? "4" : "2";
            mul.Bloq = new List<string> { "", no };
            if (no.Length > 0)
            {
                // x4 no puede ir: se ve x2 y no cuenta como cambio (lo guardado sigue)
                mul.Sel = 0; mul.Orig = 0;
            }
            else
            {
                mul.Sel = Math.Max(0, mul.Vals.IndexOf(smx4Quiere));
                mul.Orig = mul.Vals.IndexOf(guardado);
            }
            mul.Visible = on;
            mul.Activa = on && fgSi;
            mul.Nota = !on ? "" : !fgSi ? Tx.T("Con la generación encendida", "With frame generation on") :
                       no.Length > 0 ? (smx4Quiere == "4" ? Tx.T("x4 vuelve cuando se pueda · ", "x4 comes back when possible · ") : "") + no :
                       mul.Val == "4" ? (d5Sel ? Tx.T("x4 con DLSS 5: DLSS 5 se baja lo que haga falta para que quepa", "x4 with DLSS 5: DLSS 5 is lowered as needed to fit")
                                               : Tx.T("XeSS FG x2 del motor + Smooth Motion encima", "Engine XeSS FG x2 + Smooth Motion on top")) : "";
        }

        // (AjustesPonerTodo) lo elegido, en el ini (smooth_x4); el reparto abre el motor con la cadena de XeSS FG y el perfil sin la sincronizacion
        void SmoothX4Aplicar(Dictionary<string, string> e)
        {
            string a = AjVal(e, "smx4");
            if (a == null) return;
            // (1.2.4 ronda 7, FGREAL: sin tocarla nada; con x4 sin poder ir la fila enseña x2 pero lo guardado -x4- se queda)
            FilaAjuste fila = AjFila("smx4");
            if (fila != null && !fila.Cambiada) return;
            bool x4 = a == "4";
            if (x4 == cfg.SmoothX4) return;
            cfg.SmoothX4 = x4;
            Ce.IniPoner("smooth_x4", x4 ? "1" : "0");
            Log.Write("Smooth Motion: multiplicador x" + (x4 ? "4 (smooth_x4=1: XeSS FG x2 del motor + Smooth Motion)" : "2") + (x4 && cfg.SmoothX4Falta.Length > 0 ? "; ahora no: " + cfg.SmoothX4Falta : ""));
        }
    }
}
