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

        public bool CeParseSmoothX4(string key, string val)
        {
            if (key != "smooth_x4") return false;
            SmoothX4 = val.Trim() == "1";
            return true;
        }

        // Lo que falta para Smooth Motion x4 ahora ("" = se puede). Sin propiedades que miren EngineSmoothMode (lo usa el)
        public string SmoothX4Falta
        {
            get
            {
                if (!SmoothX4) return "smooth_x4=0";
                if (FgOff || HomeNative || Mode120 || !CapMode) return Tx.T("solo en el modo 30", "only in the 30 fps mode");
                if (!EngineSmoothFor(true)) return Tx.T("la generacion del modo 30 no es Smooth Motion", "the 30 fps mode generation is not Smooth Motion");
                if (Ce.NoNvidia || NvProfiles.EngineSmoothBroken) return Tx.T("Smooth Motion no se puede poner en este PC", "Smooth Motion can't be set on this PC");
                if (Pantalla.Hz() < 119) return Tx.T("la pantalla no va a 120 Hz", "the screen is not at 120 Hz");
                if (EngineVrr <= 0) return Tx.T("sin VRR (G-SYNC) en la pantalla", "no VRR (G-SYNC) on the screen");
                if (!MotorFgSabe("xess", 2)) return Tx.T("XeSS FG no esta o no va en el motor", "XeSS FG is missing or does not run in the engine");
                if (MotorGenAMagpie("xess", 2)) return Tx.T("XeSS FG va por la reserva", "XeSS FG goes through the fallback");
                return "";
            }
        }
        public bool SmoothX4Now { get { return SmoothX4Falta.Length == 0; } }

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
                bool esperaX4 = caso == "x4" && hz120 && MotorFgSabe("xess", 2);
                bool ok = x4 == esperaX4;
                if (esperaX4) ok = ok && sm == 3 && (chain == 20 || chain == 21) && args.Contains("fg_tipo=xess") && args.Contains("fg_mult=2") && fg1;
                else if (caso == "sin_clave" || caso == "sin_vrr") ok = ok && sm == (hz120 ? 2 : 1) && chain == 0 && !fg1;   // (Smooth Motion x2 como siempre)
                else if (caso == "modo120" || caso == "fg_off") ok = ok && sm == 0;
                else if (caso == "xess30") ok = ok && sm == 0;
                if (!ok) fallos++;
                sb.AppendLine((ok ? "bien  " : "FALLO ") + caso + ": x4=" + x4 + " (" + (c.SmoothX4Falta.Length > 0 ? c.SmoothX4Falta : "nada falta") + ") sm=" + sm + " cadena=" + chain +
                              " motor: '" + args + "' " + (lv.Length > 40 ? lv.Substring(0, 40) : lv));
            }
            sb.AppendLine(fallos == 0 ? "Todo bien" : fallos + " fallo(s)");
            string txt = sb.ToString();
            Console.Write(txt);
            try { if (!string.IsNullOrEmpty(informe)) File.WriteAllText(informe, txt, new UTF8Encoding(false)); } catch { }
            return fallos == 0 ? 0 : 1;
        }
    }
}
