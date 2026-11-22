// Multiplicador.cs - x2 / x3 / x4 de DLSS FG y XeSS FG (22-11-2026, 1.2.2, agente MULTI). C# 5
//
// El usuario: "Añade modos de xefg y dlss fg de 3x y 4x (si la tv es 120hz que aparezcan bloqueados si tengo puesto 60 fps de base, que
// aparezcan para aplicarlos solo si he puesto 30). Los modos 3 y 4 que salgan si se seleccionan esos dos frame generators, si se
// seleccionan otros que no salga."
//
// - Lo elegido va por modo (como el tipo de generacion, FgPick60/30): MultPick60 / MultPick30 (0 = lo del ini: generacion_60 /
//   modo30_generacion), por juego (columna 10 de games\profiles.txt: "tipo60,tipo30,mult60,mult30") y en games\framegen_mult.txt (lo de
//   ahora, para abrir la app ya jugando). Solo cuenta con DLSS FG y XeSS FG; con Smooth Motion, FSR 3 FG, NVIDIA VFG u otras: x2.
// - Lo que de verdad va (Config.FgMultNow = MultAhora): lo pedido, bajando mientras no quepa. Un multiplicador cabe si
//   (fps reales del modo x multiplicador) <= Hz de la pantalla + 1 (29,97 x 4 = 119,88 en una tele de 120 vale; 59,94 x 3 = 180 no):
//   con la tele a 120, en el modo 60 x3 / x4 salen bloqueados ("necesita el modo 30") y en el 30 x2 / x3 / x4 (60 / 90 / 120). Con 144,
//   165 o 240 Hz igual con su cuenta. x3 / x4 sin VRR solo si la salida es divisor del refresco (90 en 120 sin VRR da tirones). El modo
//   120 nunca lleva generacion. Al pasar del 30 (x4) al 60 va el x2 (o lo que quepa) y lo dice el aviso; al volver al 30, el x4 del juego.
// - El camino: el efecto de Magpie (DLSSFG\DLSS_FrameGeneration / XeSSFG\XeSS_FrameGeneration, parametro multiplier 2-4). El DLSS-G del
//   motor propio (Streamline) solo hace x2 (la multigeneracion de Streamline es solo de RTX 50 y el motor no la pide: sin probar en este
//   PC): DLSS FG x3 / x4 va por mpv + Magpie (Config.MultPorMagpie en FgMagpieOnly) tambien sin DLSS 5. El DLSS FG de Magpie necesita
//   runtimes\dlssg_sm86 (tambien en RTX 40): sin el, x3 / x4 de DLSS FG bloqueados ("preparando un componente") y la app lo baja
//   (Componentes.AsegurarSm86, ahora tambien sin DLSS 5). XeSS FG en graficas no Intel: con x3 / x4 Magpie pone solo su modo de
//   compatibilidad (terceros\magpie\XESSFG.md).
// - Donde se elige: el menu de ajustes en pantalla (fila "Multiplicador", solo con DLSS FG o XeSS FG; lo bloqueado en gris con el
//   motivo debajo), la bandeja (Multiplicador de generacion) y Ctrl+X sobre la imagen (el siguiente que quepa). Ctrl+I (estado) lo dice.
// - Si la salida no llega (base x multiplicador, Magpie), lo primero que se baja es el multiplicador 4 -> 3 -> 2 (MultVigila, antes que la
//   escala de DLSS 5 y la escalera de FpsBajo.cs).
// - Al arrancar Magpie se mira lo que de verdad ha puesto (multiplier=Nx / effective=Nx de su log): si no es lo pedido, aviso.
// - MEDIDO el 22-11 (tele a 120 Hz con VRR, fuente sintetica, modo 30, 1440p, PresentMon sobre Magpie.exe): DLSS FG x2 59,8 / x3 89,6 /
//   x4 120,0 imagenes/s (x4 todas a 1 refresco; x3 alternando 1 y 2: el VRR no llega a la salida de Magpie); XeSS FG x2 57,9 / x3 89,7 /
//   x4 63 (la mitad de reales: ver XessX4); XeSS FG a 1080p, 59 (ya arranca: xess_fg_1080).
// ini: generacion_multiplicador=0 = como antes (solo el x2 elegible; generacion_60 / modo30_generacion tal cual para todas).
//      xess_fg_1080=0|1: XeSS FG con la ventana de 1080p (ventana_1080). Ver XessNo1080.
//      xess_fg_x4=auto|1|0: XeSS FG x4 (auto: no donde su ritmo de base 60 pide mas que la pantalla). Ver XessX4.
//      prueba_multiplicador_hz=N: solo pruebas (las reglas como si la pantalla fuera a N Hz)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public bool MultOn = true;                 // ini generacion_multiplicador (de serie 1)
        public int MultPick60, MultPick30;         // lo elegido en cada modo (0 = lo del ini)
        public int MultPickNow { get { return CapMode ? MultPick30 : MultPick60; } }
        // DLSS FG x3 / x4 pedido (menu, bandeja): Componentes.AsegurarSm86 baja dlssg_sm86 aunque no haya DLSS 5
        public static volatile bool MultPideSm86;

        public bool CeParseMult(string key, string val)
        {
            switch (key)
            {
                case "generacion_multiplicador": MultOn = val.Trim() != "0"; return true;
                case "xess_fg_1080": XessFg1080 = val.Trim() != "0"; return true;
                case "xess_fg_x4": XessX4 = val.Trim().ToLowerInvariant(); return true;
                // (solo pruebas: la pantalla como si fuera a N Hz para las reglas del multiplicador; p. ej. el menu con la tele apagada)
                case "prueba_multiplicador_hz": { int h; if (int.TryParse(val.Trim(), out h)) multHzPrueba = Math.Max(0, h); } return true;
            }
            return false;
        }

        // ---- XeSS FG con la ventana de 1080p (19-11, Magpie de antes: "no arranca, 0 imagenes"). xess_fg_1080=1: se ofrece y se usa
        // (Magpie 0.6.12, medido el 22-11: ver el informe de MULTI). Con 0, como antes: no se ofrece a 1080p
        public static volatile bool XessFg1080 = true;
        public static bool XessNo1080(string videoSize) { return !XessFg1080 && WindowFor(videoSize) == "1920x1080"; }

        public static bool MultTipo(string t) { return t == "dlss" || t == "xess"; }

        // ---- XeSS FG x4 (MEDIDO el 22-11, RTX 4060 Ti, tele a 120 Hz, Magpie 0.6.12): su ritmo XeLL toma siempre 60 de base ("XeLL input
        // pacing: baseTarget=60.000 outputTarget=240.000", igual con contentRate 30, contentMode 1 o el refresco avanzado): pide 240 a una
        // tele de 120 y se queda con la MITAD de imagenes reales (15,6-15,9/s; salida 62-63/s, peor que x2). x3 (pide 180) si va: 89,7/s.
        // xess_fg_x4: auto (de serie) = bloqueado si 60 x 4 no cabe en la pantalla; 1 = siempre; 0 = nunca
        public static volatile string XessX4 = "auto";
        static bool XessX4Malo(int m, int hz) { return m >= 4 && XessX4 != "1" && (XessX4 == "0" || (hz > 0 && 60.0 * m > hz + 1.0)); }

        // Lo pedido en un modo (2-4), sin mirar la pantalla
        public int MultPedido(bool cap30)
        {
            int p = cap30 ? MultPick30 : MultPick60;
            if (p < 2) p = cap30 ? CapFgMult : FgMult60;
            return Math.Max(2, Math.Min(4, p));
        }

        // Imagenes reales por segundo del modo (la captura va a 60000/1001)
        public static double MultBase(bool cap30) { return cap30 ? 29.97 : 59.94; }

        // Hz de la pantalla (mirados cada 2 s: FgMultNow se pregunta mucho)
        static int multHz = -1;
        static DateTime multHzAt = DateTime.MinValue;
        static int multHzPrueba;   // (solo --test-multiplicador: otra pantalla)
        public static int MultHz()
        {
            if (multHzPrueba > 0) return multHzPrueba;
            DateTime now = DateTime.UtcNow;
            if (multHz < 0 || (now - multHzAt).TotalSeconds > 2) { multHz = Pantalla.Hz(); multHzAt = now; }
            return multHz;
        }

        // ¿x m en este modo con este tipo? true = bloqueado, con el motivo (es / en, cortos: van en el menu)
        public bool MultBloqueado(bool cap30, int m, string tipo, out string es, out string en)
        {
            es = en = null;
            if (m <= 2) return false;
            if (!MultOn) { es = "desactivado (generacion_multiplicador=0)"; en = "turned off (generacion_multiplicador=0)"; return true; }
            if (!MultTipo(tipo)) { es = "solo con DLSS FG o XeSS FG"; en = "only with DLSS FG or XeSS FG"; return true; }
            int hz = MultHz();
            double sal = MultBase(cap30) * m;
            if (hz > 0 && sal > hz + 1.0)
            {
                // (tele a 120: en el modo 60, x3 = 180 y x4 = 240 no caben; en el 30 si: 90 / 120)
                if (!cap30 && MultBase(true) * m <= hz + 1.0) { es = "necesita el modo 30"; en = "needs 30 fps mode"; }
                else
                {
                    string f = Math.Round(sal).ToString(CultureInfo.InvariantCulture);
                    es = "no cabe en " + hz + " Hz (" + f + " fps)"; en = "doesn't fit " + hz + " Hz (" + f + " fps)";
                }
                return true;
            }
            // Sin VRR, x3 / x4 solo si cada imagen dura un numero entero de refrescos (90 en 120 Hz: 1 y 2 refrescos alternos = tirones)
            if (hz > 0 && !CeImagen.VrrAhora)
            {
                double k = hz / sal;
                if (Math.Abs(k - Math.Round(k)) > 0.03)
                {
                    string f = Math.Round(sal).ToString(CultureInfo.InvariantCulture);
                    es = "sin VRR da tirones (" + f + " fps en " + hz + " Hz)"; en = "stutters without VRR (" + f + " fps at " + hz + " Hz)";
                    return true;
                }
            }
            if (tipo == "xess" && XessX4Malo(m, hz))
            {
                es = "con este Magpie pide 240 fps: la mitad de imágenes reales"; en = "with this Magpie it asks for 240 fps: half the real frames";
                return true;
            }
            // DLSS FG x3 / x4 va por Magpie, que necesita runtimes\dlssg_sm86 (tambien en RTX 40)
            if (tipo == "dlss" && !Integrado.MagpieDlssgOk)
            {
                MultPideSm86 = true;
                if (Componentes.Sm86Bajando) { es = "preparando un componente…"; en = "getting a component ready…"; }
                else { es = "falta el componente dlssg_sm86"; en = "needs the dlssg_sm86 component"; }
                return true;
            }
            return false;
        }
        public bool MultBloqueado(bool cap30, int m, string tipo) { string es, en; return MultBloqueado(cap30, m, tipo, out es, out en); }
        public string MultMotivo(bool cap30, int m, string tipo) { string es, en; return MultBloqueado(cap30, m, tipo, out es, out en) ? Tx.T(es, en) : ""; }

        // Lo que va de verdad en un modo con un tipo (Config.FgMultNow): lo pedido o, si no cabe, el mayor que cabe
        public int MultAhora(bool cap30, string tipo)
        {
            if (!MultOn) return tipo == "smooth" ? 2 : Math.Max(2, Math.Min(4, cap30 ? CapFgMult : FgMult60));   // (como antes)
            if (!MultTipo(tipo)) return 2;
            int m = MultPedido(cap30);
            while (m > 2 && MultBloqueado(cap30, m, tipo)) m--;
            return m;
        }

        // --test-multiplicador[=<informe>]: las reglas de bloqueo con otras pantallas (60-240 Hz, con y sin VRR) y lo que va con cada pedido.
        // Devuelve los fallos frente a lo esperado (0 = bien). Sin abrir nada
        public static int MultPrueba(string informe)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder("# MULTI: reglas del multiplicador (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")\r\n");
            int fallos = 0;
            bool vrrAntes = CeImagen.VrrAhora;
            string x4Antes = XessX4;
            XessX4 = "1";   // (la tabla, sin el arreglo de XeSS FG x4: ese va aparte, abajo)
            // esperado: "hz vrr modo" -> los que caben de x2 x3 x4 (DLSS FG con el componente o XeSS FG)
            Dictionary<string, string> esp = new Dictionary<string, string>
            {
                { "120 1 30", "234" }, { "120 1 60", "2" }, { "120 0 30", "24" }, { "120 0 60", "2" }, { "119 1 30", "234" }, { "119 0 30", "24" },
                { "144 1 30", "234" }, { "144 1 60", "2" }, { "144 0 30", "2" }, { "165 1 60", "2" }, { "180 1 60", "23" }, { "180 0 30", "23" },
                { "240 1 60", "234" }, { "240 0 60", "24" }, { "240 0 30", "24" }, { "60 1 30", "2" }, { "60 1 60", "2" }, { "100 1 30", "23" },
            };
            try
            {
                Config c = new Config();
                foreach (int hz in new[] { 60, 100, 119, 120, 144, 165, 180, 240 })
                    foreach (bool vrr in new[] { true, false })
                        foreach (bool cap30 in new[] { true, false })
                        {
                            multHzPrueba = hz; CeImagen.VrrAhora = vrr;
                            string caben = "";
                            List<string> fila = new List<string>();
                            for (int m = 2; m <= 4; m++)
                            {
                                string es, en;
                                bool b = c.MultBloqueado(cap30, m, "xess", out es, out en);
                                if (!b) caben += m;
                                fila.Add("x" + m + (b ? " NO (" + es + ")" : " si"));
                            }
                            string k = hz + " " + (vrr ? 1 : 0) + " " + (cap30 ? 30 : 60), e;
                            bool mal = esp.TryGetValue(k, out e) && e != caben;
                            if (mal) fallos++;
                            // lo que va si se pide x4 / x3 (MultAhora)
                            c.MultPick30 = c.MultPick60 = 4; int va4 = c.MultAhora(cap30, "xess");
                            c.MultPick30 = c.MultPick60 = 3; int va3 = c.MultAhora(cap30, "xess");
                            c.MultPick30 = c.MultPick60 = 0;
                            sb.AppendLine((mal ? "FALLO " : "      ") + hz + " Hz, VRR " + (vrr ? "si" : "no") + ", modo " + (cap30 ? 30 : 60) + ": " + string.Join(" | ", fila.ToArray()) +
                                          "  -> pedido x4 va x" + va4 + ", x3 va x" + va3 + (e != null ? "  (esperado " + e + ")" : ""));
                        }
                // XeSS FG x4 con su ritmo de base 60 (xess_fg_x4=auto): no a 120 Hz, si a 240
                XessX4 = "auto";
                foreach (int hz in new[] { 120, 144, 240 })
                {
                    multHzPrueba = hz; CeImagen.VrrAhora = true;
                    bool b4 = c.MultBloqueado(true, 4, "xess"), b3 = c.MultBloqueado(true, 3, "xess"), d4 = c.MultBloqueado(true, 4, "dlss") && Integrado.MagpieDlssgOk;
                    bool esp4 = hz < 240, mal = b4 != esp4 || b3 || d4;
                    if (mal) fallos++;
                    sb.AppendLine((mal ? "FALLO " : "      ") + "XeSS FG x4 (xess_fg_x4=auto) a " + hz + " Hz, modo 30: " + (b4 ? "NO (" + c.MultMotivo(true, 4, "xess") + ")" : "si") +
                                  "; x3 " + (b3 ? "NO" : "si") + "; DLSS FG x4 " + (d4 ? "NO" : "si") + " (esperado x4 " + (esp4 ? "NO" : "si") + ", x3 si)");
                }
                XessX4 = "1";
                // con las demas generaciones, siempre x2; DLSS FG sin el componente, x3 / x4 no
                multHzPrueba = 120; CeImagen.VrrAhora = true;
                c.MultPick30 = 4;
                foreach (string t in new[] { "smooth", "fsr3", "vfg" })
                {
                    int va = c.MultAhora(true, t);
                    if (va != 2) fallos++;
                    sb.AppendLine((va != 2 ? "FALLO " : "      ") + "modo 30, " + t + " con x4 pedido: va x" + va + " (esperado x2)");
                }
                sb.AppendLine("      DLSS FG x4 en el modo 30 a 120 Hz con VRR: " + (c.MultBloqueado(true, 4, "dlss") ? "NO (" + c.MultMotivo(true, 4, "dlss") + ")" : "si") +
                              " (runtimes\\dlssg_sm86 en magpie\\: " + (Integrado.MagpieDlssgOk ? "si" : "no") + ")");
                c.MultOn = false; c.CapFgMult = 4;
                int viejo = c.MultAhora(true, "vfg");
                if (viejo != 4) fallos++;
                sb.AppendLine((viejo != 4 ? "FALLO " : "      ") + "generacion_multiplicador=0 (como antes): VFG con modo30_generacion=4 va x" + viejo + " (esperado x4)");
                // la salida de XeSS FG desde sus lineas del log de Magpie (las de la prueba xess4_1440 del 22-11: PresentMon dio 63,1/s)
                double t1 = 0, t2 = 0; long f1 = 0, s1 = 0, f2 = 0, s2 = 0;
                bool okx = TrayApp.MultXessLinea("2026-11-22 05:48:24.014|info|XeSSFGPresenter.cpp:1|Log|XeSSFG SDK output: requested=4x frames=477 submissions=120 partialBursts=1 lastFrames=4 lastFGResult=0 motionFrames=119 (not display events)", out t1, out f1, out s1) &&
                           TrayApp.MultXessLinea("2026-11-22 05:48:31.636|info|XeSSFGPresenter.cpp:1|Log|XeSSFG SDK output: requested=4x frames=957 submissions=240 partialBursts=1 lastFrames=4 lastFGResult=0 motionFrames=239 (not display events)", out t2, out f2, out s2);
                double xo = okx ? (f2 - f1) / (t2 - t1) : 0, xr = okx ? (s2 - s1) / (t2 - t1) : 0;
                okx = okx && Math.Abs(xo - 62.97) < 0.1 && Math.Abs(xr - 15.74) < 0.05;
                if (!okx) fallos++;
                sb.AppendLine((okx ? "      " : "FALLO ") + "XeSS FG desde \"XeSSFG SDK output\": salida " + xo.ToString("0.00", CultureInfo.InvariantCulture) + "/s, reales " +
                              xr.ToString("0.00", CultureInfo.InvariantCulture) + "/s (esperado 62,97 y 15,74)");
                // la columna del perfil
                GameProfile p = new GameProfile();
                p.ParsePick("dlss,xess,0,4");
                bool okp = p.Pick60 == "dlss" && p.Pick30 == "xess" && p.Mult60 == 0 && p.Mult30 == 4 && p.PickText == "dlss,xess,0,4";
                GameProfile q = new GameProfile();
                q.ParsePick("smooth,");
                okp = okp && q.Mult60 == 0 && q.Mult30 == 0 && q.PickText == "smooth,";
                if (!okp) fallos++;
                sb.AppendLine((okp ? "      " : "FALLO ") + "perfil: \"dlss,xess,0,4\" -> " + p.Pick60 + "/" + p.Pick30 + " x" + p.Mult60 + "/x" + p.Mult30 + " -> \"" + p.PickText + "\"; \"smooth,\" -> \"" + q.PickText + "\"");
            }
            catch (Exception ex) { fallos++; sb.AppendLine("EXCEPCION: " + ex); }
            finally { multHzPrueba = 0; CeImagen.VrrAhora = vrrAntes; XessX4 = x4Antes; }
            sb.AppendLine(fallos == 0 ? "OK" : fallos + " fallos");
            string f = string.IsNullOrEmpty(informe) ? Path.Combine(Paths.Datos, @"games\test_multiplicador.txt") : informe;
            try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(f))); File.WriteAllText(f, sb.ToString(), new System.Text.UTF8Encoding(false)); } catch { }
            return fallos;
        }

        // ¿Cabe DLSS 5 (1 pasada, al minimo) en la cadena de ahora con este tipo y multiplicador? (escala automatica de DLSS 5; sin ella, si).
        // MEDIDO el 22-11: 1440p, DLSS L, DLSS FG x4 sin DLSS 5 = 81 % de grafica; con DLSS 5 encendido la app lo quita ("NO CABE")
        public bool MultCabeDlss5(int m, string tipo)
        {
            if (!Dlss5AutoAhora || !Integrado.Dlss5Available) return true;
            try
            {
                CadenaEscala c = CadenaEscalaPara(1);
                c.Gen = tipo; c.Mult = m;
                return EscalaAuto.PctPara(c, 1) > 0;
            }
            catch { return true; }
        }

        // x3 / x4 puesto ahora (con la generacion encendida y fuera del 120)
        public bool MultAlto { get { return MultOn && !FgOff && !Mode120 && FgMultNow > 2; } }
        // DLSS FG x3 / x4: el motor propio solo hace x2 -> mpv + Magpie (en FgMagpieOnly)
        public bool MultPorMagpie { get { return MultOn && !FgOff && !HomeNative && !Mode120 && FgTypeNow == "dlss" && FgMultNow > 2; } }
        // " x3" para los textos (DLSS FG / XeSS FG; nada con las demas)
        public string MultEtiqueta { get { return MultOn && !FgOff && MultTipo(FgTypeNow) ? " x" + FgMultNow : ""; } }
    }

    // ---- por juego: columna 10 de profiles.txt "tipo60,tipo30[,mult60,mult30]" (una version de antes lee solo los dos primeros) ----
    partial class GameProfile
    {
        public int Mult60, Mult30;   // 0 = lo del ini
        string MultCols { get { return Mult60 > 1 || Mult30 > 1 ? "," + Mult60 + "," + Mult30 : ""; } }
        void ParseMult(string[] f)
        {
            int a = 0, b = 0;
            if (f.Length > 2) int.TryParse(f[2].Trim(), out a);
            if (f.Length > 3) int.TryParse(f[3].Trim(), out b);
            Mult60 = a >= 2 && a <= 4 ? a : 0;
            Mult30 = b >= 2 && b <= 4 ? b : 0;
        }
        string MultSummary { get { return (Mult60 > 1 ? ", multiplicador del 60: x" + Mult60 : "") + (Mult30 > 1 ? ", multiplicador del 30: x" + Mult30 : ""); } }
    }

    partial class TrayApp
    {
        static string MultFile { get { return Path.Combine(Paths.Datos, @"games\framegen_mult.txt"); } }

        // Lo elegido en cada modo (0 = lo del ini), en la config y en games\framegen_mult.txt
        void SetMults(int m60, int m30)
        {
            cfg.MultPick60 = m60 >= 2 && m60 <= 4 ? m60 : 0;
            cfg.MultPick30 = m30 >= 2 && m30 <= 4 ? m30 : 0;
            SaveText(MultFile, "60=" + cfg.MultPick60 + "\r\n30=" + cfg.MultPick30 + "\r\n");
        }

        // (al abrir la app, con LoadPicks)
        void MultCargar()
        {
            string s = LoadText(MultFile);
            if (string.IsNullOrEmpty(s)) return;
            foreach (string raw in s.Split('\n'))
            {
                string l = raw.Trim();
                int v;
                if (l.StartsWith("60=") && int.TryParse(l.Substring(3), out v)) cfg.MultPick60 = v >= 2 && v <= 4 ? v : 0;
                else if (l.StartsWith("30=") && int.TryParse(l.Substring(3), out v)) cfg.MultPick30 = v >= 2 && v <= 4 ? v : 0;
            }
            if (cfg.MultPick60 > 0 || cfg.MultPick30 > 0)
                Log.Write("Multiplicador de generacion elegido: modo 60 " + (cfg.MultPick60 > 0 ? "x" + cfg.MultPick60 : "el del ini") + ", modo 30 " + (cfg.MultPick30 > 0 ? "x" + cfg.MultPick30 : "el del ini"));
        }

        // Para los avisos: lo que no va de lo pedido en este modo, o que en el 30 hay mas (" · x4 solo en el modo 30")
        string MultNota()
        {
            if (!cfg.MultOn || cfg.FgOff || cfg.Mode120 || !fgWanted) return "";
            string t = cfg.FgTypeNow;
            if (!Config.MultTipo(t)) return "";
            int quiere = cfg.MultPedido(cfg.CapMode), va = cfg.FgMultNow;
            if (quiere > va) return " · x" + quiere + " → x" + va + " (" + cfg.MultMotivo(cfg.CapMode, quiere, t) + ")";
            if (!cfg.CapMode)
            {
                int m30 = cfg.MultAhora(true, t);
                if (m30 > va) return Tx.T(" · x" + m30 + " solo en el modo 30", " · x" + m30 + " only in 30 fps mode");
            }
            return "";
        }

        // El multiplicador m en el modo de ahora (bandeja, Ctrl+X). Reabre lo que haga falta (como el tipo de generacion)
        void MultElegir(int m, int tries)
        {
            if (InHome || homeBusy || userExit) return;
            System.Diagnostics.Process p = mpv;
            if (p == null || p.HasExited) return;
            if (passesBusy || relaunchSize != null || !lsReady || relaunchEngine || Interlocked.CompareExchange(ref fgBusy, 1, 0) != 0)
            {
                if (tries >= 40) { Log.Write("Multiplicador: x" + m + " sin poner (la imagen seguia cambiando)"); return; }
                Thread.Sleep(250);
                MultElegir(m, tries + 1);
                return;
            }
            try
            {
                PrepareCfg();
                string t = cfg.FgTypeNow;
                if (cfg.Mode120 || !fgWanted || cfg.FgOff) { Osd(Tx.T("Multiplicador · con la generación encendida (no a 120 fps)", "Multiplier · with frame generation on (not at 120 fps)")); return; }
                if (!Config.MultTipo(t)) { Osd(Tx.T("Multiplicador · solo con DLSS FG o XeSS FG (ahora " + cfg.FgName + ": x2)", "Multiplier · only with DLSS FG or XeSS FG (now " + cfg.FgName + ": x2)")); return; }
                string es, en;
                if (cfg.MultBloqueado(cfg.CapMode, m, t, out es, out en)) { Osd(cfg.FgName + " x" + m + " · " + Tx.T(es, en)); return; }
                int antes = cfg.FgMultNow;
                if (m == antes && cfg.MultPedido(cfg.CapMode) == m) { Osd(cfg.FgName + " x" + m + Tx.T(" · ya activo", " · already on")); return; }
                bool engineAntes = Config.EngineActive;
                if (cfg.CapMode) SetMults(cfg.MultPick60, m); else SetMults(m, cfg.MultPick30);
                if (m > 2 && t == "dlss") Config.MultPideSm86 = true;
                lastPadAction = DateTime.Now;
                PrepareCfg();
                bool engine = cfg.EngineNow;
                Log.Write("Multiplicador: " + cfg.FgName + " x" + m + " en el modo " + (cfg.CapMode ? "30" : "60") + " (antes x" + antes + ")" +
                          (engine != engineAntes ? (engine ? ": al motor propio (x2)" : ": a mpv + Magpie (el motor propio solo hace x2)") : ReopenNote) + " · " + cfg.ProfileText);
                SaveCurProfile();
                double b = Config.MultBase(cfg.CapMode);
                RestartLs(cfg.FgName + " x" + m, (cfg.CapMode ? "30" : "60") + " → " + Math.Round(b * m).ToString(CultureInfo.InvariantCulture) + " fps", false, true);
            }
            finally { Interlocked.Exchange(ref fgBusy, 0); }
        }

        // (pruebas sin mando, por la tuberia como switch2-pad: "mult2" / "mult3" / "mult4" = ese multiplicador ya; "multsig" = Ctrl+X)
        void MultPruebaPad(string line)
        {
            // (y el menu de ajustes sin mando: "multmenu" lo abre, "multmenu_der" / _izq / _arriba / _abajo / _ok / _atras, "multpng" lo guarda en
            // logs\menu_ajustes_<hora>.png)
            Match mm = Regex.Match(line ?? "", "\"multmenu(?:_(der|izq|arriba|abajo|ok|atras))?\"|\"multpng\"");
            if (mm.Success)
            {
                string o = mm.Value.Trim('"'), d = mm.Groups[1].Value;
                OnUi(delegate
                {
                    try
                    {
                        if (o == "multpng")
                        {
                            if (ajVentana == null) { Log.Write("Prueba: multpng sin el menu abierto"); return; }
                            string png = Path.Combine(Paths.Datos, @"logs\menu_ajustes_" + DateTime.Now.ToString("HHmmss") + ".png");
                            ajVentana.GuardarPng(png);
                            Log.Write("Prueba: menu de ajustes guardado en " + png);
                        }
                        else if (d.Length == 0) { if (!ajustesAbierto) AjustesAbrir("prueba"); }
                        else AjustesOrden(d == "der" ? AjOrden.Der : d == "izq" ? AjOrden.Izq : d == "arriba" ? AjOrden.Arriba : d == "abajo" ? AjOrden.Abajo : d == "ok" ? AjOrden.Ok : AjOrden.Atras);
                    }
                    catch (Exception ex) { Log.Error("Prueba del menu de ajustes", ex); }
                });
                return;
            }
            Match m = Regex.Match(line ?? "", "\"mult(sig|[234])\"");
            if (!m.Success) return;
            string v = m.Groups[1].Value;
            Log.Write("Prueba: multiplicador " + v + " (switch2-pad)");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { if (v == "sig") MultSiguiente(); else MultElegir(int.Parse(v, CultureInfo.InvariantCulture), 0); }
                catch (Exception ex) { Log.Error("Prueba del multiplicador", ex); }
            });
        }

        // Ctrl+X: el siguiente multiplicador que quepa (x2 -> x3 -> x4 -> x2); si no cabe ninguno mas, el motivo
        void MultSiguiente()
        {
            PrepareCfg();
            if (cfg.Mode120 || !fgWanted || cfg.FgOff) { Osd(Tx.T("Multiplicador · con la generación encendida (no a 120 fps)", "Multiplier · with frame generation on (not at 120 fps)")); return; }
            string t = cfg.FgTypeNow;
            if (!Config.MultTipo(t)) { Osd(Tx.T("Multiplicador · solo con DLSS FG o XeSS FG (ahora " + cfg.FgName + ": x2)", "Multiplier · only with DLSS FG or XeSS FG (now " + cfg.FgName + ": x2)")); return; }
            int cur = cfg.FgMultNow;
            for (int i = 1; i <= 2; i++)
            {
                int m = 2 + (cur - 2 + i) % 3;
                if (!cfg.MultBloqueado(cfg.CapMode, m, t)) { MultElegir(m, 0); return; }
            }
            if (cur > 2) { MultElegir(2, 0); return; }
            string es, en;
            cfg.MultBloqueado(cfg.CapMode, 3, t, out es, out en);
            Osd(cfg.FgName + " x2 · x3 / x4: " + Tx.T(es ?? "", en ?? ""));
        }

        // ---------------- bandeja: "Multiplicador de generación" (x2 / x3 / x4; lo que no cabe, gris con el motivo) ----------------
        ToolStripMenuItem MultBandeja()
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(Tx.T("Multiplicador de generación", "Frame generation multiplier"));
            mi.ShortcutKeyDisplayString = "Ctrl+X";
            mi.DropDownItems.Add(new ToolStripMenuItem("…"));
            mi.DropDownOpening += delegate
            {
                mi.DropDownItems.Clear();
                try
                {
                    PrepareCfg();
                    string t = cfg.FgTypeNow;
                    int cur = cfg.FgMultNow;
                    if (t == "dlss" && !Integrado.MagpieDlssgOk && !Ce.NoNvidia) { Config.MultPideSm86 = true; Componentes.AsegurarSm86(); }
                    double b = Config.MultBase(cfg.CapMode);
                    for (int m = 2; m <= 4; m++)
                    {
                        int mm = m;
                        string es, en;
                        bool bloq = cfg.MultBloqueado(cfg.CapMode, m, t, out es, out en);
                        ToolStripMenuItem it = new ToolStripMenuItem("x" + m + "  (" + Math.Round(b * m).ToString(CultureInfo.InvariantCulture) + " fps)" + (bloq ? " · " + Tx.T(es, en) : ""), null, delegate
                        {
                            DesdeBandeja("multiplicador x" + mm, true, delegate { MultElegir(mm, 0); });
                        });
                        it.Checked = m == cur;
                        it.Enabled = !bloq;
                        mi.DropDownItems.Add(it);
                    }
                }
                catch (Exception ex) { Log.Error("Bandeja: multiplicador", ex); }
            };
            return mi;
        }

        // (marcar de la bandeja) se ve con DLSS FG o XeSS FG y la generacion encendida, fuera del 120
        bool MultBandejaVisible()
        {
            try { return cfg.MultOn && cfg.FgAvailable && fgWanted && !cfg.Mode120 && Config.MultTipo(cfg.FgTypeNow); }
            catch { return false; }
        }

        // ---------------- menu de ajustes en pantalla: la fila "Multiplicador" ----------------
        int ajMultQuiere;   // lo ultimo elegido a mano en la fila (0 = nada): se recupera si vuelve a caber (p. ej. 30 -> 60 -> 30 en el menu)

        // La fila (en AjustesModelo, detras de "tipo"); null si no hay DLSS FG ni XeSS FG en este PC
        FilaAjuste AjFilaMult()
        {
            if (!ajustesAbierto) ajMultQuiere = 0;   // (al abrir el menu; con el abierto, AjustesRefrescar rehace el modelo y lo elegido sigue)
            if (!cfg.MultOn || !cfg.FgAvailable) return null;
            bool hay = false;
            foreach (string t in new[] { "dlss", "xess" })
                if (Array.IndexOf(cfg.GenList30, t) >= 0 || Array.IndexOf(cfg.GenList60, t) >= 0) hay = true;
            if (!hay) return null;
            if (!Integrado.MagpieDlssgOk && !Ce.NoNvidia && (Array.IndexOf(cfg.GenList30, "dlss") >= 0 || Array.IndexOf(cfg.GenList60, "dlss") >= 0))
            { Config.MultPideSm86 = true; Componentes.AsegurarSm86(); }
            FilaAjuste f = new FilaAjuste { Id = "mult", Nombre = Tx.T("Multiplicador", "Multiplier") };
            f.Poner(new List<string> { "2", "3", "4" }, new List<string> { "x2", "x3", "x4" }, "2", "2");
            f.Ayuda = "";
            return f;
        }

        // (AjustesDepende) la fila con el modo y el tipo elegidos en el menu: visible solo con DLSS FG / XeSS FG, lo que no cabe bloqueado
        void AjMultDepende(bool inicio, string modoSel, string tipoSel, bool fgSi)
        {
            FilaAjuste f = AjFila("mult");
            if (f == null) return;
            bool cap30 = modoSel == "30";
            f.Visible = modoSel != "120" && fgSi && Config.MultTipo(tipoSel);
            // (lo que va -o iria- en ese modo con ese tipo: lo guardado del modo, o lo que quepa)
            int ahora = cfg.MultAhora(cap30, tipoSel);
            f.Bloq = new List<string>();
            List<string> libres = new List<string>(), motivos = new List<string>();
            for (int m = 2; m <= 4; m++)
            {
                string r = cfg.MultMotivo(cap30, m, tipoSel);
                f.Bloq.Add(r);
                if (r.Length == 0) libres.Add("x" + m);
                else if (!motivos.Contains(r)) motivos.Add(r);
            }
            int want = inicio ? ahora : ajMultQuiere > 0 ? ajMultQuiere : ahora;
            int sel = want;
            while (sel > 2 && f.Bloqueado(sel - 2)) sel--;
            f.Sel = sel - 2;
            f.Orig = ahora - 2;   // (con otro modo / tipo, lo de ese modo: lo que se ve sin tocarlo no es un cambio)
            double b = Config.MultBase(cap30);
            string fps = "";
            for (int m = 2; m <= 4; m++) fps += (m > 2 ? " · " : "") + "x" + m + " = " + Math.Round(b * m).ToString(CultureInfo.InvariantCulture);
            // (lo bloqueado y por que: "x3 / x4: necesita el modo 30" o, con motivos distintos, "x3: ...; x4: ...")
            string bloq;
            if (motivos.Count <= 1) bloq = string.Join(" / ", new List<string> { "x2", "x3", "x4" }.FindAll(delegate(string x) { return !libres.Contains(x); }).ToArray()) + ": " + (motivos.Count > 0 ? motivos[0] : "");
            else { List<string> ps = new List<string>(); for (int m = 3; m <= 4; m++) if (f.Bloqueado(m - 2)) ps.Add("x" + m + ": " + f.Bloq[m - 2]); bloq = string.Join("; ", ps.ToArray()); }
            f.Ayuda = motivos.Count == 0
                ? Tx.T("Imágenes por cada real (" + fps + " fps). Más = más fluido y algo más de latencia", "Frames per real one (" + fps + " fps). More = smoother, slightly more latency")
                : bloq + "  (" + fps + " fps)";
            // (con DLSS 5 encendido en el menu, en el modo y la captura de ahora: con que multiplicador ya no cabe y se quitaria)
            FilaAjuste d5 = AjFila("d5"), capF = AjFila("cap");
            bool d5si = d5 != null ? d5.Val == "1" : dlss5Shown;
            bool mismo = modoSel == (cfg.Mode120 ? "120" : cfg.CapMode ? "30" : "60") && (capF == null || capF.Val == cfg.VideoSize);
            if (f.Visible && d5si && mismo)
            {
                List<string> sinD5 = new List<string>();
                for (int m = 3; m <= 4; m++) if (!f.Bloqueado(m - 2) && !cfg.MultCabeDlss5(m, tipoSel)) sinD5.Add("x" + m);
                if (sinD5.Count > 0) f.Ayuda += " · " + string.Join(" / ", sinD5.ToArray()) + Tx.T(": DLSS 5 no cabe (se quita)", ": DLSS 5 doesn't fit (it's removed)");
            }
        }

        // (AjustesOrden) lo elegido a mano en la fila
        void AjMultTocada(FilaAjuste f) { if (f != null && f.Id == "mult") int.TryParse(f.Val, out ajMultQuiere); }

        // (AjustesPonerTodo, con applying) el multiplicador elegido para el modo que queda
        void AjMultPoner(Dictionary<string, string> e, int modo)
        {
            string v;
            if (!e.TryGetValue("mult", out v) || modo == 120) return;
            int m;
            if (!int.TryParse(v, out m) || m < 2 || m > 4) return;
            if (modo == 30) SetMults(cfg.MultPick60, m); else SetMults(m, cfg.MultPick30);
            if (m > 2) Config.MultPideSm86 = true;
            Log.Write("Menu de ajustes: multiplicador x" + m + " en el modo " + modo);
        }

        // Altura del menu en filas (las de dos lineas cuentan dos; las escondidas, nada)
        static int AjAltas(List<FilaAjuste> filas)
        {
            int n = 0;
            foreach (FilaAjuste f in filas) if (f.Visible) n += f.Ayuda != null ? 2 : 1;
            return n;
        }

        // Tras un cambio en el menu: si cambia la altura (la fila del multiplicador aparece o se va), la ventana nueva, ya opaca
        void AjRedimensionar(int altasAntes)
        {
            int altas = AjAltas(ajFilas);
            if (altas == altasAntes || ajVentana == null) return;
            VentanaAjustes nueva = new VentanaAjustes(mpvWindow, altas);
            try { ajVentana.Dispose(); } catch { }
            ajVentana = nueva;
            AjustesPintar(255);
            ajVentana.Encima();
        }

        // Un candado pequeño a la izquierda de un valor bloqueado (con lineas: la fuente del menu puede no tener el simbolo)
        static void AjCandado(System.Drawing.Graphics g, System.Drawing.RectangleF cr, float u, System.Drawing.Color c)
        {
            float w = 10 * u, h = 8 * u, x = cr.X + 14 * u, yb = cr.Y + cr.Height / 2 - 1 * u;
            using (System.Drawing.SolidBrush b = new System.Drawing.SolidBrush(c))
            using (System.Drawing.Pen p = new System.Drawing.Pen(c, Math.Max(1f, 1.6f * u)))
            {
                g.FillRectangle(b, x, yb, w, h);
                g.DrawArc(p, x + 2 * u, yb - 6 * u, w - 4 * u, 10 * u, 180, 180);
            }
        }

        // El siguiente valor de una fila sin pasar por los bloqueados (d = +1 / -1; vuelta = carrusel)
        static int AjPaso(FilaAjuste f, int d, bool vuelta)
        {
            int n = f.Vals.Count, s = f.Sel;
            for (int i = 0; i < n; i++)
            {
                int k = s + d;
                if (vuelta) k = (k + n) % n;
                else if (k < 0 || k >= n) return f.Sel;
                s = k;
                if (!f.Bloqueado(s)) return s;
            }
            return f.Sel;
        }

        // ---------------- vigilancia: con x3 / x4, si la salida no llega, el multiplicador baja (antes que lo demas) ----------------
        int multMal;
        DateTime multBajadoAt = DateTime.MinValue;

        // XeSS FG: las imagenes que saca no estan en el "Output cadence" de Magpie (generated=0: las presenta la cadena de Intel; medido el
        // 22-11, x2 a 1440p: nuevas 721, inventadas 0, y PresentMon 57,9 imagenes/s con la mitad "Intel XeSS-FG"). Si, cada ~4 s, "XeSSFG SDK
        // output: requested=2x frames=1439 submissions=720" (acumuladas): de dos seguidas, la salida (frames/s) y las reales (submissions/s)
        // Una linea "2026-11-22 05:48:24.014|info|...|XeSSFG SDK output: requested=4x frames=477 submissions=120 ..." -> hora (s), frames, submissions
        public static bool MultXessLinea(string line, out double ts, out long frames, out long subs)
        {
            ts = 0; frames = subs = 0;
            if (line == null || line.IndexOf("XeSSFG SDK output", StringComparison.Ordinal) < 0) return false;
            Match m = Regex.Match(line, @"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+)\|.*XeSSFG SDK output: requested=\dx frames=(\d+) submissions=(\d+)");
            DateTime t;
            if (!m.Success || !DateTime.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return false;
            ts = t.Ticks / 1e7;
            frames = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            subs = long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            return true;
        }

        long multLogPos = -1;
        double multXeT = -1, multXeOut = -1, multXeReal = -1;
        long multXeFrames = -1, multXeSubs = -1;
        DateTime multXeAt = DateTime.MinValue;

        void MultLeerXess()
        {
            string path = Paths.MagpieLog;
            if (!File.Exists(path)) return;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (multLogPos < 0 || multLogPos > fs.Length) { multLogPos = Math.Max(0, fs.Length - 65536); multXeT = -1; }   // (primera vez o log rotado)
                fs.Seek(multLogPos, SeekOrigin.Begin);
                byte[] buf = new byte[Math.Min(fs.Length - multLogPos, 4 * 1024 * 1024)];
                int n = 0, r;
                while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0) n += r;
                if (n == 0) return;
                int end = Array.LastIndexOf(buf, (byte)'\n', n - 1);
                if (end < 0) return;
                multLogPos += end + 1;
                foreach (string line in System.Text.Encoding.UTF8.GetString(buf, 0, end + 1).Split('\n'))
                {
                    double ts; long f, s;
                    if (!MultXessLinea(line, out ts, out f, out s)) continue;
                    if (multXeT > 0 && f >= multXeFrames && s >= multXeSubs && ts > multXeT + 1 && ts < multXeT + 30)
                    {
                        multXeOut = (f - multXeFrames) / (ts - multXeT);
                        multXeReal = (s - multXeSubs) / (ts - multXeT);
                        multXeAt = DateTime.Now;
                    }
                    multXeT = ts; multXeFrames = f; multXeSubs = s;   // (si baja, es otra sesion de Magpie: desde aqui)
                }
            }
        }

        // CheckLsPacing cada 5 s con Magpie. true = ha bajado el multiplicador (lo demas no juzga esta vez)
        bool MultVigila(LsStats st)
        {
            try
            {
                PrepareCfg();
                bool xess = cfg.FgTypeNow == "xess";
                if (xess && cfg.MultAlto) MultLeerXess();
                if (!cfg.MultAlto || Config.EngineActive || cfg.HomeNative || InHome || homeBusy || passesBusy || relaunchSize != null || relaunchEngine ||
                    userExit || !lsReady || applying || MagpieLive.RunningFor() < 20 || (DateTime.Now - multBajadoAt).TotalSeconds < 30) { multMal = 0; return false; }
                double fuente = FuenteFps(), modo = Config.MultBase(cfg.CapMode);
                if (fuente < modo * 0.92) { multMal = 0; return false; }   // (el juego no da el modo entero: no es cosa del PC)
                int m = cfg.FgMultNow, hz = Config.MultHz();
                double salida, real;
                if (xess)
                {
                    if ((DateTime.Now - multXeAt).TotalSeconds > 6 || multXeOut <= 0) return false;   // (sin dato nuevo: se sigue la cuenta)
                    salida = multXeOut; real = multXeReal;
                    multXeAt = DateTime.MinValue;   // (cada dato una vez)
                }
                else
                {
                    if (st.CapN == 0 || st.CapTime <= 0) return false;
                    salida = (st.NewSum + st.GenSum) / st.CapTime; real = st.NewSum / st.CapTime;
                }
                double objetivo = Math.Min(modo * m, hz >= 60 ? hz - 0.2 : modo * m);
                bool mal = salida < objetivo * 0.9 || real < modo * 0.9;
                multMal = mal ? multMal + 1 : 0;
                if (multMal < 2) return false;
                multMal = 0;
                multBajadoAt = DateTime.Now;
                int nuevo = m - 1;
                while (nuevo > 2 && cfg.MultBloqueado(cfg.CapMode, nuevo, cfg.FgTypeNow)) nuevo--;
                string fs = Math.Round(salida).ToString(CultureInfo.InvariantCulture), fo = Math.Round(objetivo).ToString(CultureInfo.InvariantCulture);
                Log.Write("Multiplicador: no se llega con x" + m + " (salida " + salida.ToString("0.0", CultureInfo.InvariantCulture) + " de " + objetivo.ToString("0.0", CultureInfo.InvariantCulture) +
                          ", reales " + real.ToString("0.0", CultureInfo.InvariantCulture) + " de " + modo.ToString("0.00", CultureInfo.InvariantCulture) + "): se baja a x" + nuevo + " (guardado en el perfil del juego)");
                string tipo = cfg.FgName;
                OnUi(delegate { OsdWarn(Tx.T("No se llega a " + fo + " fps (" + fs + ") · cambiado a " + tipo + " x" + nuevo, fo + " fps not reached (" + fs + "): changed to " + tipo + " x" + nuevo), "7000"); });
                int nn = nuevo;
                ThreadPool.QueueUserWorkItem(delegate { try { MultElegir(nn, 0); } catch (Exception ex) { Log.Error("Multiplicador: bajando", ex); } });
                return true;
            }
            catch (Exception ex) { Log.Error("Multiplicador (vigilancia)", ex); return false; }
        }

        // ---------------- el tipo elegido que no puede ir (la app pone otro: que se diga, nunca en silencio) ----------------
        // Por que t no va con esta captura ahora (null = si va)
        string GenNoVaPorque(string t, string capSel)
        {
            if (t == "xess" && !File.Exists(Path.Combine(Paths.MagpieDir, "libxess_fg.dll"))) return Tx.T("falta libxess_fg.dll en magpie\\", "libxess_fg.dll is missing in magpie\\");
            if (t == "xess" && Config.XessNo1080(capSel)) return Tx.T("no va con la captura de 1080p", "doesn't run with the 1080p capture");
            if (t == "smooth" && Pantalla.Is119) return Tx.T("no con la tele a 119,88 Hz", "not with the TV at 119.88 Hz");
            if (t == "smooth" && NvProfiles.SmoothBroken) return Tx.T("el driver no deja ponerlo", "the driver doesn't allow it");
            if (t == "dlss" && cfg.DlssFgPorMagpieSinRuntime) return Tx.T("falta un componente (dlssg_sm86, preparándose)", "a component is missing (dlssg_sm86, getting ready)");
            return null;
        }

        // Lo elegido en ese modo que no va con esa captura: "XeSS FG: no va con la captura de 1080p" (null = va lo elegido)
        string GenSustituto(bool cap30, string capSel)
        {
            string want = cap30 ? cfg.FgPick30 : cfg.FgPick60;
            if (string.IsNullOrEmpty(want)) return null;
            string porque = GenNoVaPorque(want, capSel);
            return porque == null ? null : GenLabel(want, false) + ": " + porque;
        }

        // (AjustesDepende) la fila del tipo: si lo de antes no esta con la captura / modo elegidos, lo que se pone en su lugar no cuenta como
        // cambio (lo elegido se guarda y vuelve con otra captura) y debajo se dice por que. Antes se aplicaba y se guardaba en el perfil
        // (log del usuario, 22-11 04:01:27: "aplicar Captura 1080p, Tipo de generacion DLSS FG" sin tocar el tipo: adios XeSS FG)
        void AjTipoForzado(FilaAjuste tipo, string pedido, bool cap30, string capSel)
        {
            tipo.Ayuda = null;
            if (tipo.Vals.Count == 0 || !tipo.Activa) return;
            bool forzado = !string.IsNullOrEmpty(pedido) && pedido != "\u0000" && !tipo.Vals.Contains(pedido);
            if (forzado)
            {
                tipo.Orig = tipo.Sel;
                string porque = GenNoVaPorque(pedido, capSel) ?? Tx.T("no está con esto", "not available with this");
                tipo.Ayuda = GenLabel(pedido, false) + ": " + porque + " · " + Tx.T("va ", "using ") + GenLabel(tipo.Val, false) + Tx.T(" (lo elegido se guarda)", " (your choice is kept)");
                return;
            }
            string s = GenSustituto(cap30, capSel);
            if (s != null) tipo.Ayuda = s + " · " + Tx.T("va ", "using ") + GenLabel(tipo.Val, false);
        }

        // ---------------- al arrancar Magpie: lo que ha puesto de verdad (FinishLsStart, con su log desde el arranque) ----------------
        void MultComprobarArranque(string log)
        {
            try
            {
                // (el tipo: si lo elegido no puede ir, aviso; Config.FgTypeNow pone otro)
                string sus = fgWanted && !cfg.FgOff && !cfg.Mode120 && !cfg.HomeNative ? GenSustituto(cfg.CapMode, cfg.VideoSize) : null;
                if (sus != null)
                {
                    string va = GenLabel(GenCurrent(), false);
                    Log.Write("Generacion: lo elegido no puede ir (" + sus + "): va " + va);
                    string aviso = sus + " · " + Tx.T("va ", "using ") + va;
                    OnUi(delegate { OsdWarn(aviso, "6000"); });
                }
                if (cfg.FgOff || cfg.Mode120 || cfg.HomeNative || !Config.MultTipo(cfg.FgTypeNow)) return;
                int pedido = cfg.FgMultNow;
                int puesto = 0;
                Match m = Regex.Match(log ?? "", @"(?:XeSSFG initialized|DLSS FG\S* initialized):[^\r\n]*?multiplier=(\d)x");
                if (m.Success) puesto = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                // (DLSS FG: "DLSSFG capabilities: ... requested=3x effective=3x ceiling=4x" y "DLSSFG capacity: ... active=3x"; medido el 22-11 en la
                // RTX 4060 Ti con dlssg_sm86: 3x efectivo)
                Match c = Regex.Match(log ?? "", @"DLSSFG capabilities:[^\r\n]*?requested=(\d)x effective=(\d)x(?: ceiling=(\d)x)?");
                if (c.Success && cfg.FgTypeNow == "dlss")
                {
                    int ef = int.Parse(c.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (puesto == 0 || ef < puesto) puesto = ef;
                }
                Match a = Regex.Match(log ?? "", @"DLSSFG capacity:[^\r\n]*?active=(\d)x");
                if (a.Success && cfg.FgTypeNow == "dlss")
                {
                    int ac = int.Parse(a.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (puesto == 0 || ac < puesto) puesto = ac;
                }
                if (puesto == 0)
                {
                    // (XeSS FG x3 / x4 en graficas no Intel va por el modo de compatibilidad de Magpie, que solo acepta su libxess_fg.dll: si
                    // falla, Magpie lo dice como error y no inicializa; sin generacion no hay imagen -> x2, con aviso)
                    Match e = Regex.Match(log ?? "", @"\|error\|[^\r\n]*XeSS[^\r\n]*");
                    if (pedido > 2 && cfg.FgTypeNow == "xess" && e.Success && log.IndexOf("XeSSFG initialized", StringComparison.Ordinal) < 0)
                    {
                        string msg = e.Value.Length > 200 ? e.Value.Substring(0, 200) : e.Value;
                        Log.Write("Multiplicador: XeSS FG x" + pedido + " no ha arrancado en Magpie (" + msg + "): se pasa a x2");
                        int pe2 = pedido;
                        OnUi(delegate { OsdWarn(Tx.T("XeSS FG x" + pe2 + " no arranca en esta gráfica: x2", "XeSS FG x" + pe2 + " doesn't start on this GPU: x2"), "7000"); });
                        ThreadPool.QueueUserWorkItem(delegate { try { MultElegir(2, 0); } catch (Exception ex) { Log.Error("Multiplicador: a x2", ex); } });
                    }
                    return;
                }
                if (puesto == pedido) { Log.Write("Multiplicador: Magpie con " + cfg.FgName + " x" + puesto + " (lo pedido)"); return; }
                Log.Write("Multiplicador: AVISO, se pidio " + cfg.FgName + " x" + pedido + " y Magpie ha puesto x" + puesto + (c.Success ? " (" + c.Value + ")" : ""));
                string tipo = cfg.FgName;
                int pp = puesto, pe = pedido;
                OnUi(delegate { OsdWarn(Tx.T(tipo + " x" + pe + " no disponible: va x" + pp, tipo + " x" + pe + " not available: x" + pp + " is used"), "7000"); });
            }
            catch (Exception ex) { Log.Error("Multiplicador (arranque de Magpie)", ex); }
        }
    }
}
