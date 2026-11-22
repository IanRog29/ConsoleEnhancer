// SoloMotor.cs - el juego por el motor propio en todo lo que sabe hacer; Magpie solo de reserva interna (22-11-2026, 1.2.4, agente SOLOMOTOR)
//
// El usuario: "Quita lo de elegir entre motor y magpie" y "y no se podria pasar todo al motor para que funcione mejor?". Ya no se elige en
// ningun sitio (fuera la fila "DLSS 5 por" del menu de ajustes, DLSS 5 > "En el motor propio / Por Magpie" de la bandeja, la pregunta de
// "Añadir DLSS 5" y la clave motor_dlss5): la imagen la hace el motor (D3D12 + Streamline: DLSS SR / XeSS / FSR, DLSS 5 con
// engine\PrismaNr.dll de 1 a 3 pasadas -motor_dlss5_pasadas=3-, DLSS FG x2, Smooth Motion, FSR 3 FG, modos 30 / 60 / 120, Reflex) siempre
// que sepa hacer la combinacion. mpv + Magpie quedan SOLO de reserva, sin nombrarse: lo que el motor aun no hace (NVIDIA VFG, XeSS FG x3 / x4
// fuera de Intel, DLSS FG x3 / x4 fuera de RTX 50, FSR 3 FG con DLSS de escalador, DLSS FG en RTX 30 con dlssg_sm86) y DLSS 5 sin
// engine\PrismaNr.dll (no se distribuye), o si el DLSS 5 del motor se rompe en la sesion (MotorDlss5Roto). Y motor=magpie / el motor que se
// cae 3 veces, como siempre. (1.2.4 MOTORFG: XeSS FG x2 y DLSS FG x3 / x4 de RTX 50 en el motor: Config.MotorFgSabe, MotorFg.cs.)
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // ===== EL REPARTO: ¿sabe el motor hacer esta generacion? (tipo: dlss, smooth, fsr3, xess, vfg; mult 2-4) =====
        // El resto (FgMagpieOnly, la cadena del motor, el log) ya lo mira. Lo que no este aqui va por Magpie sin decirlo.
        // (1.2.4 integracion: XeSS FG y DLSS FG x3 / x4 los decide MotorFgVia, src\Lanzador\MotorFg.cs: XeSS FG x2 en el motor; x3 / x4 por
        // la via oficial -RTX 50, Intel-, con PrismaFg.dll -RTX 40- o con la generacion propia del motor; Magpie solo si nada de eso vale)
        public static bool MotorSabeGeneracion(string tipo, int mult)
        {
            switch (tipo ?? "")
            {
                case "dlss": return mult <= 2 || MotorFgVia("dlss", mult) != "";   // DLSS-G de Streamline en el motor: x2; x3 / x4 por la via oficial (RTX 50), PrismaFg o la generacion propia
                case "smooth": return true;       // Smooth Motion del driver sobre el motor
                case "fsr3": return mult <= 2;   // FSR 3 FG del motor (EngineFgFfx.cs; con DLSS de escalador no: FgMagpieOnly)
                case "xess": return MotorFgSabe("xess", mult) || MotorFgVia("xess", mult) != "";   // XeSS FG del motor (EngineFgXe.cs): x2; x3 / x4 en Intel o con la generacion propia
                case "vfg": return false;         // NVIDIA VFG: solo Magpie
                default: return true;              // "" / "no": sin generacion
            }
        }

        // ¿Hace el motor la imagen con lo de ahora? (EngineNow = esto + motor=propio + el exe del motor). DLSS 5 encendido y que cabe: solo
        // si el motor hace DLSS 5 (PrismaNr.dll, las pasadas hasta motor_dlss5_pasadas, la generacion que sabe; MotorDlss5Pedido); DLSS 5
        // apagado o sin caber: el motor con lo demas, como siempre
        public bool MotorSabeAhora { get { return !FgMagpieOnly && !ScalerMagpieOnly && (Dlss5Off || !Dlss5FitsNow || EngineDlss5Now); } }

        // Por que va por Magpie (para el log; "" = por el motor)
        public string PorQueReserva
        {
            get
            {
                if (Engine != "propio") return "motor=" + Engine;
                if (!File.Exists(EnginePath)) return "sin " + EnginePath;
                if (FgMagpieOnly) return "la generacion " + FgTypeNow + " x" + FgMultNow + " aun no la hace el motor";
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
                return "el juego por " + (EngineNow ? "el motor propio" : "mpv + Magpie (reserva: " + r + ")") +
                       (!Dlss5Off && Dlss5FitsNow ? "; DLSS 5 " + Dlss5PassesNow + " pasada(s) " + (EngineDlss5Now ? "en el motor (engine\\PrismaNr.dll)" : "por Magpie") : "");
            }
        }

        // --test-solo-motor[=<informe>]: el reparto con cada combinacion, sin abrir nada ni la grafica: con PrismaNr.dll todo lo que el motor
        // sabe va por el, sin ella DLSS 5 por Magpie, XeSS FG / x3-x4 por Magpie; un ini viejo con motor_dlss5=0 no cambia nada; la limpieza
        // de la puesta al dia. Devuelve los fallos (0 = bien)
        public static int SoloMotorPrueba(string informe)
        {
            StringBuilder sb = new StringBuilder("# SOLOMOTOR: el reparto motor / Magpie (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")\r\n");
            int fallos = 0, n = 0;
            System.Reflection.FieldInfo f5 = typeof(Integrado).GetField("dlss5State", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            System.Reflection.FieldInfo fg = typeof(Integrado).GetField("dlssgState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            object f5Antes = f5 != null ? f5.GetValue(null) : null, fgAntes = fg != null ? fg.GetValue(null) : null;
            if (f5 != null) f5.SetValue(null, 1);
            if (fg != null) fg.SetValue(null, 1);
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
                sb.AppendLine("prisma dlss5 captura modo tipo xN pasadas -> camino dlss5_motor | por que reserva");
                foreach (int prisma in new[] { 0, 1 })
                    foreach (bool off in new[] { false, true })
                        foreach (string cap in new[] { "3840x2160", "1920x1080" })
                            foreach (int modo in new[] { 30, 60, 120 })
                                foreach (string tipo in new[] { "dlss", "smooth", "fsr3", "xess" })
                                    foreach (int mult in new[] { 2, 4 })
                                        foreach (int pas in new[] { 1, 2, 3 })
                                        {
                                            if (mult > 2 && (tipo == "smooth" || tipo == "fsr3" || modo == 120)) continue;
                                            if (off && pas > 1) continue;
                                            MotorDlss5Prueba1(prisma);
                                            MotorDlss5Roto = false;
                                            c.VideoSize = cap; c.CapMode = modo == 30; c.Mode120 = modo == 120; c.FgOff = false; c.Dlss5Off = off; c.HomeNative = false;
                                            c.Passes30 = pas; c.Level30 = 3; c.ScalerPick = ""; c.AltScalerOn = false;
                                            if (modo == 30) { c.FgPick30 = tipo; c.MultPick30 = mult; } else { c.FgPick60 = tipo; c.MultPick60 = mult; }
                                            bool en = c.EngineNow, ed = c.EngineDlss5Now;
                                            string t = c.FgTypeNow; int m = c.FgMultNow;
                                            bool genOk = c.FgOff || modo == 120 || MotorSabeGeneracion(t, m);
                                            bool d5 = !off && c.Dlss5FitsNow;
                                            // lo esperado: el motor si sabe la generacion y (sin DLSS 5, o DLSS 5 con PrismaNr.dll y las pasadas que hace)
                                            bool conGen = !c.FgOff && modo != 120;   // (a 120 no hay generacion: da igual la elegida)
                                            bool motorDebe = genOk && !(conGen && t == "fsr3" && Ce.EngineUpscaler(c) == "dlss") && !(conGen && t == "dlss" && Ce.DlssgSoloMagpie) &&
                                                             (!d5 || (prisma == 1 && Math.Max(1, Math.Min(3, pas)) <= c.EngineDlss5MaxPasses));
                                            bool ok = en == motorDebe && ed == (motorDebe && d5);
                                            n++; if (!ok) fallos++;
                                            sb.AppendLine((ok ? "      " : "FALLO ") + prisma + " " + (off ? "off" : "on ") + " " + cap + " " + modo + " " + tipo + " x" + mult + " p" + pas +
                                                          " -> " + (en ? "motor" : "magpie") + (ed ? " dlss5_motor" : "") + " (" + t + " x" + m + ") | " + c.PorQueReserva);
                                        }
                // DLSS 5 del motor roto en la sesion: por Magpie
                MotorDlss5Prueba1(1); MotorDlss5Roto = true;
                c.VideoSize = "3840x2160"; c.CapMode = true; c.Mode120 = false; c.Dlss5Off = false; c.Passes30 = 1; c.FgPick30 = "dlss"; c.MultPick30 = 2;
                bool rOk = !c.EngineDlss5Now && (!c.Dlss5FitsNow || !c.EngineNow);
                n++; if (!rOk) fallos++;
                sb.AppendLine((rOk ? "      " : "FALLO ") + "DLSS 5 del motor roto -> " + (c.EngineNow ? "motor" : "magpie") + " | " + c.PorQueReserva);
                MotorDlss5Roto = false;
                // la puesta al dia quita motor_dlss5 (y nada mas)
                string viejo = "a=1\r\nmotor_dlss5=1\r\n# motor_dlss5=0 (comentario)\r\nmotor_dlss5_pasadas=1\r\nmotor_dlss5_entrada_30_4k=65,50,40\r\nmotor_dlss5=auto";
                string limpio = Ce.SinClavesQuitadasPrueba(viejo);
                bool okL = limpio == "a=1\r\n# motor_dlss5=0 (comentario)\r\nmotor_dlss5_pasadas=1\r\nmotor_dlss5_entrada_30_4k=65,50,40\r\n";
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
                if (fg != null) fg.SetValue(null, fgAntes);
            }
            sb.AppendLine(n + " pruebas, " + fallos + " fallos");
            string ruta = string.IsNullOrEmpty(informe) ? Path.Combine(Paths.Datos, @"games\test_solo_motor.txt") : informe;
            try { File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false)); } catch { }
            Console.Write(sb.ToString());
            return fallos;
        }
    }
}
