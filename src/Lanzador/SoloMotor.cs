// SoloMotor.cs - el juego por el motor propio en todo lo que sabe hacer; Magpie solo de reserva interna (22-11-2026, 1.2.4, agente SOLOMOTOR)
//
// El usuario: "Quita lo de elegir entre motor y magpie" y "y no se podria pasar todo al motor para que funcione mejor?". Ya no se elige en
// ningun sitio (fuera la fila "DLSS 5 por" del menu de ajustes, DLSS 5 > "En el motor propio / Por Magpie" de la bandeja, la pregunta de
// "Añadir DLSS 5" y la clave motor_dlss5): la imagen la hace el motor (D3D12 + Streamline: DLSS SR / XeSS / FSR, DLSS 5 con
// engine\PrismaNr.dll de 1 a 3 pasadas -motor_dlss5_pasadas=3-, DLSS FG x2, Smooth Motion, FSR 3 FG, modos 30 / 60 / 120, Reflex) siempre
// que sepa hacer la combinacion. mpv + Magpie quedan SOLO de reserva, sin nombrarse: lo que el motor aun no hace (NVIDIA VFG, XeSS FG x3 / x4
// fuera de Intel, DLSS FG x3 / x4 fuera de RTX 50, FSR 3 FG con DLSS de escalador, DLSS FG en RTX 30 con dlssg_sm86) y DLSS 5 sin
// engine\PrismaNr.dll (o sin la nvngx_dlssnr.dll del usuario), o si el DLSS 5 del motor se rompe en la sesion (MotorDlss5Roto). Y motor=magpie / el motor que se
// cae 3 veces, como siempre. (1.2.4 MOTORFG: XeSS FG x2 y DLSS FG x3 / x4 de RTX 50 en el motor: Config.MotorFgSabe, MotorFg.cs.)
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // ===== EL REPARTO: ¿sabe el motor hacer esta generacion? (tipo: smooth, xess, fsr3; siempre x2) =====
        // El resto (FgMagpieOnly, la cadena del motor, el log) ya lo mira. Lo que no este aqui va por Magpie sin decirlo.
        // (1.2.4 ronda 6, QUITAFG: sin DLSS FG -DLSS-G-, NVIDIA VFG ni x3 / x4: lo que queda lo hace el motor; XeSS FG con DLSS 5 va por Magpie
        // como en la 1.2.3 -XefgMotor.cs- y FSR 3 FG con DLSS de escalador tambien -FgMagpieOnly-)
        public static bool MotorSabeGeneracion(string tipo)
        {
            switch (tipo ?? "")
            {
                case "smooth": return true;       // Smooth Motion del driver sobre el motor
                case "fsr3": return true;         // FSR 3 FG del motor (EngineFgFfx.cs; con DLSS de escalador no: FgMagpieOnly)
                case "xess": return MotorFgSabe("xess");   // XeSS FG del motor (EngineFgXe.cs), con sus DLL
                default: return true;              // "" / "no": sin generacion
            }
        }

        // ¿Hace el motor la imagen con lo de ahora? (EngineNow = esto + motor=propio + el exe del motor). DLSS 5 encendido y que cabe: solo
        // si el motor hace DLSS 5 (PrismaNr.dll, las pasadas hasta motor_dlss5_pasadas, la generacion que sabe; MotorDlss5Pedido); DLSS 5
        // apagado o sin caber: el motor con lo demas, como siempre
        // (1.2.4 ronda 6, QUITAFG, fase 2: sin Magpie, todo; si el motor no esta o se ha caido 3 veces, mpv sola -Engine-)
        public bool MotorSabeAhora { get { return true; } }

        // Por que va por Magpie (para el log; "" = por el motor)
        public string PorQueReserva
        {
            get
            {
                if (Engine != "propio") return "motor=" + Engine;
                if (!File.Exists(EnginePath)) return "sin " + EnginePath;
                if (FgMagpieOnly)
                {
                    string pq = MotorGenAMagpiePorQue(FgTypeNow);   // (1.2.4 ronda 5, XEFGM: XefgMotor.cs)
                    if (pq.Length == 0 && FgTypeNow == "fsr3" && Ce.EngineUpscaler(this) == "dlss") pq = "FSR 3 FG con DLSS de escalador";
                    return pq.Length > 0 ? pq : "la generacion " + FgTypeNow + " aun no la hace el motor";
                }
                if (!Dlss5Off && Dlss5FitsNow && !EngineDlss5Now)
                {
                    if (!MotorDlss5Disponible) return "DLSS 5 sin engine\\PrismaNr.dll";
                    if (MotorDlss5Roto) return "el DLSS 5 del motor no va en esta sesion";
                    string p = MotorDlss5PorQueMagpie(false);
                    return p.Length > 0 ? p : "DLSS 5 que el motor no hace";
                }
                return "";
            }
        }

        // Una linea para el log (al arrancar y en el --dry-run)
        public string SoloMotorTexto
        {
            get
            {
                string r = PorQueReserva;
                return "el juego por " + (EngineNow ? "el motor propio" : "mpv sin el motor (" + r + ")") +
                       (!Dlss5Off && Dlss5FitsNow ? "; DLSS 5 " + Dlss5PassesNow + " pasada(s) " + (EngineDlss5Now ? "en el motor (engine\\PrismaNr.dll)" : "NO (" + r + ")") : "");
            }
        }

        // --test-solo-motor[=<informe>]: el reparto con cada combinacion, sin abrir nada ni la grafica: todo por el motor (1.2.4 ronda 6: sin
        // Magpie); DLSS 5 en el motor con PrismaNr.dll, sin ella el motor sin DLSS 5; un ini viejo con motor_dlss5=0 no cambia nada; la limpieza
        // de la puesta al dia. Devuelve los fallos (0 = bien). (1.2.4 ronda 6, QUITAFG: los tipos smooth / fsr3 / xess, sin x3 / x4; dlss de
        // antes = el mas parecido)
        public static int SoloMotorPrueba(string informe)
        {
            StringBuilder sb = new StringBuilder("# SOLOMOTOR: el reparto motor / Magpie (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")\r\n");
            int fallos = 0, n = 0;
            System.Reflection.FieldInfo f5 = typeof(Integrado).GetField("dlss5State", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            object f5Antes = f5 != null ? f5.GetValue(null) : null;
            if (f5 != null) f5.SetValue(null, 1);
            string iniAntes = Paths.IniFile, iniPrueba = Path.Combine(Path.GetTempPath(), "ce_test_solo_motor_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".ini");
            try
            {
                string baseIni = File.Exists(iniAntes) ? File.ReadAllText(iniAntes) : "";
                File.WriteAllText(iniPrueba, baseIni + "\r\nmotor=propio\r\nmotor_dlss5=0\r\n", new UTF8Encoding(false));   // (un ini que eligio Magpie: ya no cuenta)
                Paths.IniFile = iniPrueba;
                Config c = Config.Load();
                c.Engine = "propio";
                if (!File.Exists(c.EnginePath)) c.EnginePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                sb.AppendLine("ini " + iniPrueba + " (+ motor_dlss5=0 al final); motor_dlss5_pasadas=" + c.EngineDlss5MaxPasses);
                sb.AppendLine("prisma dlss5 captura modo tipo pasadas -> camino dlss5_motor (tipo que va) | por que reserva");
                foreach (int prisma in new[] { 0, 1 })
                    foreach (bool off in new[] { false, true })
                        foreach (string cap in new[] { "3840x2160", "1920x1080" })
                            foreach (int modo in new[] { 30, 60, 120 })
                                foreach (string tipo in new[] { "dlss", "smooth", "fsr3", "xess" })
                                        foreach (int pas in new[] { 1, 2, 3 })
                                        {
                                            if (off && pas > 1) continue;
                                            MotorDlss5Prueba1(prisma);
                                            MotorDlss5Roto = false;
                                            c.VideoSize = cap; c.CapMode = modo == 30; c.Mode120 = modo == 120; c.FgOff = false; c.Dlss5Off = off; c.HomeNative = false;
                                            c.Passes30 = pas; c.Level30 = 3; c.ScalerPick = ""; c.AltScalerOn = false;
                                            if (modo == 30) c.FgPick30 = ParsePick(tipo); else c.FgPick60 = ParsePick(tipo);   // (dlss de antes: el mas parecido)
                                            bool en = c.EngineNow, ed = c.EngineDlss5Now;
                                            string t = c.FgTypeNow;
                                            bool genOk = c.FgOff || modo == 120 || MotorSabeGeneracion(t);
                                            if (tipo == "dlss" && !TipoGen(t)) { n++; fallos++; sb.AppendLine("FALLO dlss de antes -> \"" + t + "\" (tiene que ser smooth / xess / fsr3)"); }
                                            bool d5 = !off && c.Dlss5FitsNow;
                                            // lo esperado (1.2.4 ronda 6, QUITAFG, fase 2: sin Magpie): siempre el motor; DLSS 5 en el motor si cabe y esta
                                            // PrismaNr.dll (1-3 pasadas, con cualquier generacion, tambien XeSS FG y FSR 3 FG con DLSS de escalador)
                                            bool ok = en && genOk && ed == (d5 && prisma == 1);
                                            n++; if (!ok) fallos++;
                                            sb.AppendLine((ok ? "      " : "FALLO ") + prisma + " " + (off ? "off" : "on ") + " " + cap + " " + modo + " " + tipo + " p" + pas +
                                                          " -> " + (en ? "motor" : "SIN motor") + (ed ? " dlss5_motor" : "") + " (" + t + ") | " + c.PorQueReserva);
                                        }
                // (1.2.4 ronda 5, XEFGM; ronda 6 sin Magpie) la grafica perdida con XeSS FG en el motor: el resto de la sesion otro tipo, en el motor
                MotorDlss5Prueba1(1); MotorDlss5Roto = false; MotorGenCaidaOlvida();
                c.VideoSize = "3840x2160"; c.CapMode = true; c.Mode120 = false; c.FgOff = false; c.Dlss5Off = true; c.HomeNative = false; c.FgPick30 = "xess";
                // (1.3.1, AJUSTES: XeSS FG quitada de momento: sin generacion_xess=1 no va aunque se pida; con ella, como antes)
                bool xeOfrecer = XessFgOfrecer;
                XessFgOfrecer = false;
                string xNo = c.FgTypeNow;
                bool xnOk = xNo != "xess";
                n++; if (!xnOk) fallos++;
                sb.AppendLine((xnOk ? "      " : "FALLO ") + "XeSS FG pedido sin generacion_xess=1: va " + xNo + " (no XeSS FG)");
                XessFgOfrecer = true;
                string xAntes = c.FgTypeNow;
                MotorGenCaidaAnota("xess");
                string xDespues = c.FgTypeNow; bool xMotor = c.EngineNow;
                MotorGenCaidaOlvida();
                XessFgOfrecer = xeOfrecer;
                bool xOk = (xAntes == "xess" || !XessMotorPuede(c.VideoSize)) && xDespues != "xess" && xMotor;
                n++; if (!xOk) fallos++;
                sb.AppendLine((xOk ? "      " : "FALLO ") + "XeSS FG sin DLSS 5 (generacion_xess=1): " + xAntes + "; tras perder la grafica con el: " + xDespues + (xMotor ? " en el motor" : " SIN el motor"));
                // DLSS 5 del motor roto en la sesion: el motor sin DLSS 5 (antes, por Magpie)
                MotorDlss5Prueba1(1); MotorDlss5Roto = true;
                c.VideoSize = "3840x2160"; c.CapMode = true; c.Mode120 = false; c.Dlss5Off = false; c.Passes30 = 1; c.FgPick30 = "smooth";
                bool rOk = !c.EngineDlss5Now && c.EngineNow;
                n++; if (!rOk) fallos++;
                sb.AppendLine((rOk ? "      " : "FALLO ") + "DLSS 5 del motor roto -> " + (c.EngineNow ? "el motor sin DLSS 5" : "SIN el motor"));
                MotorDlss5Roto = false;
                // la puesta al dia quita motor_dlss5 y lo de DLSS FG / x3 / x4 (1.2.4 ronda 6, QUITAFG), y nada mas
                string viejo = "a=1\r\nmotor_dlss5=1\r\n# motor_dlss5=0 (comentario)\r\nmotor_dlss5_pasadas=1\r\nmotor_dlss5_entrada_30_4k=65,50,40\r\nmotor_dlss5=auto\r\n" +
                               "generacion_60=2\r\nmotor_generacion_30=dlss\r\ndlssg_solo_magpie=0\r\nmodo30_generacion=2\r\nfg_flujo_30=nvidia\r\ngeneracion_tipo_30=smooth\r\nxess_fg_1080=1" +
                               "\r\nmagpie_vrr=0\r\nmagpie_sm_vaciar_ms=4\r\npasadas_vigilancia=1\r\ndlss5_sin_telon_puente=1";
                string limpio = Ce.SinClavesQuitadasPrueba(viejo);
                bool okL = limpio == "a=1\r\n# motor_dlss5=0 (comentario)\r\nmotor_dlss5_entrada_30_4k=65,50,40\r\ngeneracion_tipo_30=smooth\r\nxess_fg_1080=1\r\ndlss5_sin_telon_puente=1";   // (motor_dlss5_pasadas, magpie_*, pasadas_vigilancia: de Magpie, fuera; el puente entre motores se queda)
                n++; if (!okL) fallos++;
                sb.AppendLine((okL ? "      " : "FALLO ") + "puesta al dia: \"" + limpio.Replace("\r\n", " | ") + "\"");
            }
            catch (Exception ex) { fallos++; sb.AppendLine("FALLO excepcion: " + ex); }
            finally
            {
                MotorDlss5Prueba1(-1); MotorDlss5Roto = false;
                Paths.IniFile = iniAntes;
                try { File.Delete(iniPrueba); } catch { }
                if (f5 != null) f5.SetValue(null, f5Antes);
            }
            sb.AppendLine(n + " pruebas, " + fallos + " fallos");
            string ruta = string.IsNullOrEmpty(informe) ? Path.Combine(Paths.Datos, @"games\test_solo_motor.txt") : informe;
            try { File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false)); } catch { }
            Console.Write(sb.ToString());
            return fallos;
        }
    }
}
