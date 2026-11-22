// SoloMagpie.cs - el juego siempre por mpv + Magpie (22-11-2026, 1.2.4, agente SOLOMAGPIE)
//
// El usuario: "Quita lo de elegir entre motor y magpie, haz que solo haya magpie". Hasta la 1.2.3 habia dos elecciones "motor o Magpie":
//  - la de DLSS 5 (1.2.3, MotorDlss5.cs): motor_dlss5=auto|0|1 con engine\PrismaNr.dll, la fila "DLSS 5 por: Motor / Magpie" del menu de
//    ajustes, DLSS 5 > "En el motor propio / Por Magpie" en la bandeja y la pregunta de "Añadir DLSS 5". FUERA: DLSS 5 va siempre por
//    Magpie; el motor no lo hace aunque se le pida (src\Motor\Engine\Common.cs ignora dlss5=1) y engine\PrismaNr.dll no se carga, no se
//    pone, no se guarda ni se recoge (Extras.cs, ExtrasGuardados.cs). Las claves motor_dlss5* del ini no hacen nada y la puesta al dia
//    las quita (ConsoleEnhancerApp.cs, ClavesQuitadas).
//  - la implicita (sin elegirse en ningun menu): con DLSS 5 apagado, sin caber o en el modo 120 el juego lo hacia el motor propio. AHORA
//    tambien por mpv + Magpie (Config.EngineNow siempre false; ini solo_magpie=0, que no esta en base.ini, para comparar con la 1.2.3).
// El motor queda solo por dentro: el motor en espera hace de puente mientras Magpie se (re)abre (puente_ligero: DLSS E sin generacion)
// y nada mas; el HOME / menu de la consola a 4K nativo va por mpv (como ya iba con DLSS 5 encendido). Sin el puente cada reapertura de
// Magpie seria el telon negro de 7-15 s.
// Como el DLSS FG de este Magpie solo va con runtimes\dlssg_sm86 (Integrado.MagpieDlssgOk), sin el no se ofrece DLSS FG
// (DlssFgPorMagpieSinRuntime) y la app lo busca / baja tambien sin DLSS 5 si el selector del plan tiene DLSS FG (SoloMagpiePideSm86).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // Componentes.AsegurarSm86: con el juego siempre por Magpie, DLSS FG (si el plan la pone en el selector) necesita dlssg_sm86
        public static bool SoloMagpiePideSm86
        {
            get
            {
                if (!SoloMagpieOn) return false;
                try
                {
                    string a = Ce.IniLeer("generacion_lista_30"), b = Ce.IniLeer("generacion_lista_60");
                    if (a == null) a = "smooth,dlss,xess";   // (los de serie de Config.GenList30 / GenList60)
                    if (b == null) b = "smooth";
                    if (Ce.IniLeer("generacion_disponible") == "0") return false;
                    return (a + "," + b).ToLowerInvariant().Contains("dlss");
                }
                catch { return false; }
            }
        }

        // Una linea para el log (arranque y --dry-run): por donde va la imagen y si hay una PrismaNr.dll de antes (que ya no se usa)
        public string SoloMagpieTexto
        {
            get
            {
                string pn = Path.Combine(Path.GetDirectoryName(string.IsNullOrEmpty(EnginePath) ? Paths.AppDir + @"\engine\x.exe" : EnginePath), "PrismaNr.dll");
                bool hay = false;
                try { hay = File.Exists(pn); } catch { }
                return "el juego por " + (EngineNow ? "el motor propio (solo_magpie=0)" : "mpv + Magpie") +
                       (SoloMagpieOn ? " (1.2.4: siempre Magpie; el motor propio solo de puente)" : "") +
                       "; DLSS 5 por Magpie" + (hay ? "; " + pn + " esta pero ya no se usa" : "");
            }
        }

        // --test-solo-magpie[=<informe>]: con cada combinacion (DLSS 5 si/no, captura, modo 30/60/120, generacion, x2/x4, pasadas, HOME y
        // un ini viejo con motor_dlss5=1 y motor_dlss5_entrada_*), el juego nunca por el motor, DLSS 5 en la cadena de Magpie cuando cabe y
        // nada de dlss5 en los ajustes del motor (el del puente / en espera). Sin abrir nada ni la grafica. Devuelve los fallos (0 = bien)
        public static int SoloMagpiePrueba(string informe)
        {
            StringBuilder sb = new StringBuilder("# SOLOMAGPIE: el juego siempre por Magpie (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")\r\n");
            int fallos = 0, n = 0;
            System.Reflection.FieldInfo f5 = typeof(Integrado).GetField("dlss5State", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            System.Reflection.FieldInfo fg = typeof(Integrado).GetField("dlssgState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            object f5Antes = f5 != null ? f5.GetValue(null) : null, fgAntes = fg != null ? fg.GetValue(null) : null;
            if (f5 != null) f5.SetValue(null, 1);   // (como si estuviera nvngx_dlssnr.dll en magpie\)
            if (fg != null) fg.SetValue(null, 1);   // (y runtimes\dlssg_sm86)
            string iniAntes = Paths.IniFile, iniPrueba = Path.Combine(Path.GetTempPath(), "ce_test_solo_magpie_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".ini");
            bool soloAntes = SoloMagpieOn;
            try
            {
                // un ini como el del usuario con la 1.2.3 (DLSS 5 en el motor) al final: no debe cambiar nada
                string baseIni = File.Exists(iniAntes) ? File.ReadAllText(iniAntes) : "";
                File.WriteAllText(iniPrueba, baseIni + "\r\nmotor=propio\r\nmotor_dlss5=1\r\nmotor_dlss5_pasadas=3\r\nmotor_dlss5_entrada_30=100,100,75\r\nmotor_dlss5_entrada_30_4k=75,60,45\r\nmotor_dlss5_home=1\r\n",
                                  new UTF8Encoding(false));
                Paths.IniFile = iniPrueba;
                Config c = Config.Load();
                c.Engine = "propio";
                if (!File.Exists(c.EnginePath)) c.EnginePath = System.Reflection.Assembly.GetExecutingAssembly().Location;   // (EngineNow miraba que exista)
                sb.AppendLine("ini de prueba " + iniPrueba + " (el de " + iniAntes + " + motor_dlss5=1 ... al final); solo_magpie=" + (SoloMagpieOn ? 1 : 0) + "; " + c.SoloMagpieTexto);
                sb.AppendLine("dlss5 captura modo tipo xN pasadas home -> camino dlss5_en_Magpie args_del_motor_sin_dlss5");
                foreach (bool off in new[] { false, true })
                    foreach (string cap in new[] { "3840x2160", "2560x1440", "1920x1080" })
                        foreach (int modo in new[] { 30, 60, 120 })
                            foreach (string tipo in new[] { "dlss", "smooth", "xess", "fsr3" })
                                foreach (int mult in new[] { 2, 4 })
                                    foreach (int pas in new[] { 1, 2 })
                                        foreach (bool home in new[] { false, true })
                                        {
                                            if (mult > 2 && (tipo == "smooth" || tipo == "fsr3" || modo == 120)) continue;
                                            if (home && (pas > 1 || mult > 2 || tipo != "dlss")) continue;
                                            c.VideoSize = cap; c.CapMode = modo == 30; c.Mode120 = modo == 120; c.FgOff = false; c.Dlss5Off = off; c.HomeNative = home;
                                            c.Passes30 = pas; c.Level30 = 2; c.ScalerPick = ""; c.AltScalerOn = false;
                                            if (modo == 30) { c.FgPick30 = tipo; c.MultPick30 = mult; } else { c.FgPick60 = tipo; c.MultPick60 = mult; }
                                            bool en = c.EngineNow;
                                            bool d5 = !off && !home && c.Dlss5FitsNow;
                                            string cadena = home ? "" : JsonWriter.Serialize(MagpieConfig.Chain(c, double.NaN));
                                            bool enCadena = cadena.Contains("__dlssnrModeVersion");
                                            string args = TrayApp.EngineLivePrueba(c);
                                            bool ok = !en && (home || enCadena == d5) && !args.Contains("dlss5");
                                            n++; if (!ok) fallos++;
                                            sb.AppendLine((ok ? "      " : "FALLO ") + (off ? "off" : "on ") + " " + cap + " " + modo + " " + tipo + " x" + mult + " p" + pas + (home ? " home" : "     ") +
                                                          " -> " + (en ? "MOTOR" : "magpie") + " " + (home ? "-" : enCadena ? "dlss5" : "sin_dlss5") + (d5 != enCadena && !home ? "(esperado " + (d5 ? "dlss5" : "sin") + ")" : "") +
                                                          " " + (args.Contains("dlss5") ? "ARGS_CON_DLSS5" : "ok"));
                                        }
                // solo_magpie=0: como la 1.2.3 sin DLSS 5 (el motor con DLSS 5 apagado), y aun asi nunca DLSS 5 en el motor
                SoloMagpieOn = false;
                c.VideoSize = "2560x1440"; c.CapMode = true; c.Mode120 = false; c.Dlss5Off = true; c.HomeNative = false; c.FgPick30 = "dlss"; c.MultPick30 = 2; c.Passes30 = 1;
                bool m0 = c.EngineNow;
                c.Dlss5Off = false;
                bool m1 = c.EngineNow, d1 = c.Dlss5FitsNow;
                bool okC = m0 && (!d1 || !m1) && !TrayApp.EngineLivePrueba(c).Contains("dlss5");
                n++; if (!okC) fallos++;
                sb.AppendLine((okC ? "      " : "FALLO ") + "solo_magpie=0: DLSS 5 apagado -> " + (m0 ? "motor" : "magpie") + "; encendido -> " + (m1 ? "motor" : "magpie") + " (DLSS 5 cabe: " + (d1 ? "si" : "no") + ")");
                SoloMagpieOn = soloAntes;
                // la puesta al dia quita las motor_dlss5* (lo de despues del plan y lo conservado)
                string viejo = "a=1\r\nmotor_dlss5=1\r\n# motor_dlss5=0 (comentario)\r\nmotor_dlss5_entrada_30_4k=75,60,45\r\nmotor_dlss5_home=0\r\nmotor_reserva=1\r\nmotor_dlss5_pasadas=2";
                string limpio = Ce.SinClavesQuitadasPrueba(viejo);
                bool okL = limpio == "a=1\r\n# motor_dlss5=0 (comentario)\r\nmotor_reserva=1\r\n" || limpio == "a=1\r\n# motor_dlss5=0 (comentario)\r\nmotor_reserva=1";
                n++; if (!okL) fallos++;
                sb.AppendLine((okL ? "      " : "FALLO ") + "puesta al dia: \"" + limpio.Replace("\r\n", " | ") + "\"");
            }
            catch (Exception ex) { fallos++; sb.AppendLine("FALLO excepcion: " + ex); }
            finally
            {
                SoloMagpieOn = soloAntes;
                Paths.IniFile = iniAntes;
                try { File.Delete(iniPrueba); } catch { }
                if (f5 != null) f5.SetValue(null, f5Antes);
                if (fg != null) fg.SetValue(null, fgAntes);
            }
            sb.AppendLine(n + " pruebas, " + fallos + " fallos");
            string ruta = string.IsNullOrEmpty(informe) ? Path.Combine(Paths.Datos, @"games\test_solo_magpie.txt") : informe;
            try { File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false)); } catch { }
            Console.Write(sb.ToString());
            return fallos;
        }
    }

    partial class TrayApp
    {
        // (--test-solo-magpie: los ajustes que se mandan al motor, sin tocar los perfiles de NVIDIA como EngineArgs)
        internal static string EngineLivePrueba(Config c) { return EngineLive(c, false); }
    }
}
