// TiposGeneracion.cs - 23-11-2026 (1.2.4 ronda 6, agente QUITAFG). C# 5
//
// El usuario: "como el dlss fg va fatal, directamente quitalo" y "Quita los xess 3x y 4x que no funcionan bien y que haya pa elegir frame
// gens y ya (da a elegir entre xefg, smooth motion (los que pueden usarlo) y fsr frame generation)".
//
// - Los tipos de generacion que quedan (siempre x2; los multiplicadores x3 / x4 ya no existen):
//     smooth = NVIDIA Smooth Motion, la del driver (perfil de NVIDIA del motor o de Magpie.exe). Solo donde se puede: RTX 40 / 50 con el driver
//              que la da (Deteccion\Gpu.cs: f.SmoothMotion -> el plan la pone en generacion_lista_60 / _30) y nunca con la tele a 119,88 Hz ni si
//              el driver no deja ponerla (NvProfiles.SmoothBroken / EngineSmoothBroken)
//     xess   = XeSS FG (libxess_fg.dll + libxell.dll de xess\, la release oficial de Intel): el motor
//     fsr3   = FSR 3 FG (amd_fidelityfx_framegeneration_dx12.dll de fsr\, la release oficial de AMD): el motor
//   Y "sin generacion" (el boton de captura, la fila Generacion del menu de ajustes, la bandeja).
// - De serie (Deteccion\Plan.cs): Smooth Motion en RTX 40 / 50 (lo que el usuario dice que va mejor); XeSS FG en Intel Arc; FSR 3 FG en lo demas.
// - Lo guardado de antes con dlss (DLSS FG) o vfg (NVIDIA VFG) -ini generacion_tipo*, games\framegen_type.txt, la columna 10 de
//   games\profiles.txt- pasa al mas parecido de este PC (GenEnVezDeDlss: el primero de la lista del selector que puede ir; en NVIDIA RTX
//   40 / 50, Smooth Motion). Los multiplicadores guardados (x3 / x4 de la columna 10, games\framegen_mult.txt) se leen y se tiran: va el
//   mismo tipo a x2. El resto de cada perfil no cambia.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // (1.2.4 ronda 6, QUITAFG) las claves de lo que ya no existe (DLSS FG, los multiplicadores x3 / x4, la generacion propia del motor,
        // dlssg_sm86): un ini de antes las trae; se aceptan sin hacer nada (ni "clave desconocida" en el log). La puesta al dia del ini
        // (Ce.ActualizarIni, ClavesQuitadas) las quita
        public static readonly string[] ClavesRetiradas = {
            "fg_dlss_flujo_4k", "fg_dlss_flujo_calidad_4k", "fg_dlss_flujo_4k_multi", "fg_dlss_vectores", "fg_dlss_flujo_calidad_60",
            "fg_flujo_30", "fg_flujo_60", "fg_flujo_calidad", "fg_flujo_calidad_60", "dlss_fg_preset", "magpie_dlssfg_ritmo_vsync", "magpie_dlssfg_lite",
            "magpie_vrr_60_fg", "magpie_sigue_fuente_60_fg", "dxgi_30_fg", "dxgi_30_vfg",
            "motor_generacion_60", "motor_generacion_30", "generacion_60", "modo30_generacion", "generacion_multiplicador", "dlssg_solo_magpie",
            "xess_fg_x4", "xess_fg_multi_cabe", "xess_fg_fijo", "xess_fg_multi_uso", "escala_coste_xess_compat", "generacion_dlss5_minimo",
            "escala_coste_dlss_fg", "escala_coste_dlss_fg_mas", "escala_credito_fijo_dlss",
            "motor_fg_propia", "motor_xefg_propia", "motor_propia_dlss5", "prueba_multiplicador_hz", "motor_dlss5_pasadas",
            // (fase 2, sin Magpie: las de Magpie que ya no lee nadie; dlss5_sin_telon_puente se queda: deja el relevo entre motores)
            "magpie_actualizar", "magpie_arranque_max_s", "magpie_captura", "magpie_origen", "magpie_repetidas", "magpie_ritmo_ntsc", "magpie_cola_cpu",
            "magpie_sm_vaciar_auto", "magpie_sm_vaciar_log", "magpie_sm_vaciar_ms", "magpie_sm_vaciar_objetivo", "magpie_sm_vaciar_partido",
            "magpie_sm_vaciar_real", "magpie_sm_vaciar_real_alto", "magpie_sm_vaciar_real_ganancia", "magpie_sm_vaciar_real_objetivo",
            "magpie_sm_vaciar_tras_ms", "magpie_sm_vaciar_umbral", "pasadas_vigilancia", "pasadas_vigilancia_imagen", "pasadas_vigilancia_imagen_pct",
            "pasadas_vigilancia_imagen_umbral", "pasadas_vigilancia_prueba", "pasadas_vigilancia_reinicios", "puente_ligero",
            "telon_esperar_magpie_max_ms", "rtx_hdr_metodo",
            // (SINMPV / INTEGRA6: las de mpv, que ya no se abre)
            "mpv_path", "test_mpv_args", "mpv_cola_ms", "mpv_cscale", "mpv_sin_marco", "captura_png" };
        // (fase 2) y todas las magpie_* (ritmo, VRR, cola, nitidez... de Magpie: ninguna la lee ya nadie)
        public static bool ClaveRetirada(string key)
        {
            string k = (key ?? "").Trim().ToLowerInvariant();
            return k.StartsWith("magpie_") || Array.IndexOf(ClavesRetiradas, k) >= 0;
        }

        public static readonly string[] TiposGen = { "smooth", "xess", "fsr3", "efg" };   // (1.2.4 ronda 6, ENHANCED: + Enhanced FG, EnhancedFg.cs)
        public static bool TipoGen(string t) { return t == "smooth" || t == "xess" || t == "fsr3" || t == "efg"; }

        // Lo que va en lugar de DLSS FG / NVIDIA VFG en lo guardado (ParsePick); "" = lo del ini (hasta que Config.Load lo calcula)
        public static volatile string GenEnVezDeDlss = "";

        // ---- XeSS FG con la ventana de 1080p (19-11, Magpie de antes: "no arranca, 0 imagenes"). xess_fg_1080=1 (de serie): se ofrece y se
        // usa (Magpie 0.6.12, medido el 22-11: 59 imagenes/s). Con 0, como antes: no se ofrece a 1080p
        public static volatile bool XessFg1080 = true;
        // (1.2.4 ronda 7, FGREAL; el usuario tras probarla con su PS5: "Fsr 3 fg ... un verdadero desastre (fallos visuales, muy poca fluidez)",
        // "fsr fg x2 y x4 un desastre en casi todo"; y el coordinador: si no se puede salvar, fuera del menu). FSR 3 FG (x2 de AMD y x3 / x4 de
        // CeFsrFg) ya no se ofrece: ni en el menu, ni en L3, ni de reserva; un perfil o un ini con fsr3 pasa al mas parecido (y se dice por que).
        // generacion_fsr3=1 lo vuelve a poner (como opcion experimental, al final)
        public static volatile bool FsrFgOfrecer = false;
        public static bool XessNo1080(string videoSize) { return !XessFg1080 && WindowFor(videoSize) == "1920x1080"; }

        public bool CeParseTipos(string key, string val)
        {
            switch (key)
            {
                case "xess_fg_1080": XessFg1080 = val.Trim() != "0"; return true;
                case "generacion_fsr3": FsrFgOfrecer = val.Trim() == "1"; return true;   // (1.2.4 ronda 7, FGREAL)
                case "efg_multi_dlss5": EfgMultiDlss5 = val.Trim() != "0"; return true;   // (1.2.4 ronda 7, EFGFIX: de serie 1)   // (1.2.4 ronda 7, INTEGRA7: Enhanced FG x3 / x4 tambien con DLSS 5, EnhancedFg.cs)
            }
            return CeParseXefgm(key, val) || CeParseMultiFsr(key, val);   // (1.2.4 ronda 6, FGMULTI: motor_fsr_multi*, MultiFsr.cs)   // (motor_xefg_dlss5: XefgMotor.cs. 1.2.4 ronda 6: proteccion* -Proteccion.cs, la de Magpie- fuera, ClavesRetiradas)
        }

        static bool XessDll() { return Paths.HayLib("libxess_fg.dll"); }
        static bool FsrFgDll() { return Paths.HayLib("amd_fidelityfx_framegeneration_dx12.dll"); }

        // ¿Puede ir este tipo aqui, ahora? (las DLL, el driver, la tele, la captura)
        public static bool GenPuedeIr(string t, string videoSize)
        {
            switch (t ?? "")
            {
                case "smooth": return !Ce.NoNvidia && !NvProfiles.SmoothBroken && !NvProfiles.EngineSmoothBroken && !Pantalla.Is119;
                case "xess": return XessDll() && MotorFgSabe("xess") && !XessNo1080(videoSize) && !XessCaido;   // (1.2.4 ronda 6: y no si la grafica se perdio con el en esta sesion ni si el motor dijo que no arranca -switch2-fgfallo-)
                case "fsr3": return FsrFgDll() && FsrFgOfrecer;   // (1.2.4 ronda 7, FGREAL: fuera salvo generacion_fsr3=1)
                case "efg": return EfgPuede();   // (1.2.4 ronda 6, ENHANCED: cualquier grafica, el motor; EnhancedFg.cs)
            }
            return false;
        }

        // El tipo que va en un modo con esta captura: lo elegido (L3 / el menu / el perfil del juego), si no el del ini del modo, si no
        // generacion_tipo; si eso no vale (dlss / vfg de antes, vacio) o no puede ir aqui, el mas parecido que si puede (GenParecida)
        public string FgTypeFor(bool cap30, string videoSize)
        {
            string t = cap30 ? FgPick30 : FgPick60;
            if (!TipoGen(t)) t = cap30 ? FgType30 : FgType60;
            if (!TipoGen(t)) t = FgType;
            if (TipoGen(t) && GenPuedeIr(t, videoSize)) return t;
            return GenParecida(cap30, videoSize, t);
        }

        // El primero de la lista del selector del modo (la del plan: en RTX 40 / 50 Smooth Motion primero) que pueda ir, sin "quitado"; si
        // ninguno, el primero de la del otro modo y luego XeSS FG / FSR 3 FG. Si nada puede ir: lo pedido si es un tipo (fallara y se dira) o
        // el primero de la lista
        public string GenParecida(bool cap30, string videoSize, string quitado)
        {
            List<string> orden = new List<string>();
            foreach (string g in cap30 ? GenList30 : GenList60) if (TipoGen(g) && !orden.Contains(g)) orden.Add(g);
            foreach (string g in cap30 ? GenList60 : GenList30) if (TipoGen(g) && !orden.Contains(g)) orden.Add(g);
            foreach (string g in new[] { "xess", "fsr3" }) if (!orden.Contains(g)) orden.Add(g);
            foreach (string g in orden) if (g != quitado && GenPuedeIr(g, videoSize)) return g;
            if (TipoGen(quitado)) return quitado;
            return orden.Count > 0 ? orden[0] : "xess";
        }

        // (al acabar Config.Load) lo que va en lugar de DLSS FG en lo guardado: el de serie del modo 30 en este PC
        internal void CeTiposTrasCargar()
        {
            try
            {
                List<string> l = new List<string>();
                foreach (string g in GenList30) if (TipoGen(g)) l.Add(g);
                foreach (string g in GenList60) if (TipoGen(g) && !l.Contains(g)) l.Add(g);
                string r = "";
                foreach (string g in l)
                {
                    bool va = g == "smooth" ? !Ce.NoNvidia : g == "xess" ? XessDll() : g == "fsr3" ? FsrFgDll() && FsrFgOfrecer : false;   // (FGREAL: FSR 3 FG solo con generacion_fsr3=1)
                    if (va) { r = g; break; }
                }
                GenEnVezDeDlss = r;
            }
            catch { GenEnVezDeDlss = ""; }
        }

        // 19-11 (GENERACION): generacion_lista_60/30 = "smooth,xess,fsr3" (sin, no, ninguna = nada: solo "Sin generacion"). (1.2.4 ronda 6,
        // QUITAFG: dlss y vfg fuera; una lista de antes con dlss -el plan de entonces dejaba fuera FSR 3 FG si habia DLSS FG o Smooth Motion-
        // gana FSR 3 FG al final, para poder elegir las tres)
        public static string[] ParseGenList(string v)
        {
            List<string> l = new List<string>();
            bool vieja = false;
            foreach (string s in v.Split(new char[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = s.Trim().ToLowerInvariant();
                if (t == "sin" || t == "no" || t == "ninguna" || t == "0") continue;
                if (t == "dlss" || t == "vfg") { vieja = true; continue; }
                t = ParseFg(t);
                if (TipoGen(t) && !l.Contains(t)) l.Add(t);
            }
            if (vieja && !l.Contains("fsr3")) l.Add("fsr3");
            return l.ToArray();
        }

        // Un tipo del ini (generacion_tipo*): smooth / xess / fsr3; dlss y vfg (de antes) = "" (lo de serie de este PC: FgTypeFor)
        static string ParseFg(string v)
        {
            v = v.Trim().ToLowerInvariant();
            if (v == "fsr3" || v == "xess" || v == "smooth") return v;
            if (v == "fsr") return "fsr3";
            if (v == "smoothmotion" || v == "smooth_motion" || v == "sm") return "smooth"; // (NVIDIA Smooth Motion, la del driver)
            if (v == "dlss" || v == "vfg" || v == "xefg") return v == "xefg" ? "xess" : "";   // (1.2.4 ronda 6: DLSS FG y NVIDIA VFG ya no existen)
            if (v == "efg" || v == "enhanced" || v == "enhancedfg" || v == "enhanced_fg") return "efg";   // (1.2.4 ronda 6, ENHANCED)
            throw new FormatException("generacion_tipo: smooth, xess, fsr3 o efg");
        }

        // 19-11 (GENERACION): un tipo guardado (perfil, games\framegen_type.txt): el tipo o "" si no vale. (1.2.4 ronda 6: dlss / vfg -> el mas
        // parecido de este PC, GenEnVezDeDlss)
        public static string ParsePick(string v)
        {
            try
            {
                if (string.IsNullOrEmpty(v) || v.Trim().Length == 0) return "";
                string l = v.Trim().ToLowerInvariant();
                if (l == "dlss" || l == "vfg") return GenEnVezDeDlss;
                return ParseFg(v);
            }
            catch { return ""; }
        }
    }

    partial class TrayApp
    {
        // ---------------- el tipo elegido que no puede ir (la app pone otro: que se diga, nunca en silencio) ----------------
        // Por que t no va con esta captura ahora (null = si va)
        string GenNoVaPorque(string t, string capSel)
        {
            if (t == "xess" && !Paths.HayLib("libxess_fg.dll")) return Tx.T("falta libxess_fg.dll en xess\\", "libxess_fg.dll is missing in xess\\");
            if (t == "xess" && Config.XessNo1080(capSel)) return Tx.T("no va con la captura de 1080p", "doesn't run with the 1080p capture");
            if (t == "xess" && (Config.XessCaido || !Config.MotorFgSabe("xess")))
                return Tx.T("el motor no ha podido con él en esta sesión", "the engine couldn't run it this session");   // (1.2.4 ronda 6: sin Magpie)
            if (t == "fsr3" && !Config.FsrFgOfrecer) return Tx.T("quitada (fallos y poca fluidez con juego real; generacion_fsr3=1 la vuelve a poner)", "removed (artifacts and poor smoothness in real games; generacion_fsr3=1 brings it back)");   // (1.2.4 ronda 7, FGREAL)
            if (t == "fsr3" && !Paths.HayLib("amd_fidelityfx_framegeneration_dx12.dll")) return Tx.T("falta su DLL en fsr\\", "its DLL is missing in fsr\\");
            if (t == "smooth" && Ce.NoNvidia) return Tx.T("solo en NVIDIA RTX 40 / 50", "NVIDIA RTX 40 / 50 only");
            if (t == "smooth" && Pantalla.Is119) return Tx.T("no con la tele a 119,88 Hz", "not with the TV at 119.88 Hz");
            if (t == "smooth" && (NvProfiles.SmoothBroken || NvProfiles.EngineSmoothBroken)) return Tx.T("el driver no deja ponerlo", "the driver doesn't allow it");
            // (1.2.4 ronda 7, EFGFIX: antes no habia motivo para Enhanced FG: tras una caida salia de la lista sin explicacion)
            if (t == "efg" && Config.EfgCaido) return Config.EfgCaidoTexto(false);
            if (t == "efg" && !Config.EfgEnMenu()) return Tx.T("necesita la pantalla a 100 Hz o más", "needs the screen at 100 Hz or more");
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
            // (1.2.4 ronda 7, EFGFIX) Enhanced FG elegido otra vez en la fila tras una caida: al aplicar se reintenta
            if (s != null && tipo.Val == "efg" && Config.EfgCaido)
            { tipo.Ayuda = GenLabel("efg", false) + ": " + Config.EfgCaidoTexto(true).Replace("; elígelo otra vez para reintentar", "").Replace("; pick it again to retry", "") + Tx.T(" · al aplicar se vuelve a intentar", " · applying retries it"); return; }
            if (s != null) tipo.Ayuda = s + " · " + Tx.T("va ", "using ") + GenLabel(tipo.Val, false);
        }

        // (al arrancar Magpie, FinishLsStart) si lo elegido no puede ir, aviso (Config.FgTypeFor pone otro). Antes en MultComprobarArranque
        void GenComprobarArranque()
        {
            try
            {
                string sus = fgWanted && !cfg.FgOff && !cfg.Mode120 && !cfg.HomeNative ? GenSustituto(cfg.CapMode, cfg.VideoSize) : null;
                if (sus == null) return;
                string va = GenLabel(GenCurrent(), false);
                Log.Write("Generacion: lo elegido no puede ir (" + sus + "): va " + va);
                string aviso = sus + " · " + Tx.T("va ", "using ") + va;
                OnUi(delegate { OsdWarn(aviso, "6000"); });
            }
            catch (Exception ex) { Log.Error("Generacion (arranque de Magpie)", ex); }
        }

        // --test-tipos-generacion[=<informe>] (1.2.4 ronda 6, QUITAFG): la puesta al dia de lo guardado de antes sin abrir nada: copias de
        // datos de usuario (carpetas con consoleenhancer.ini y games\, en <dir>; sin dir, unas de ejemplo): el ini, games\framegen_type.txt y
        // games\profiles.txt con dlss, x3 / x4 o vfg pasan al mas parecido (en NVIDIA RTX 40 / 50, Smooth Motion) sin perder lo demas; lo de
        // serie del plan en una RTX 40 es Smooth Motion; los tipos que ofrece el selector. Devuelve los fallos (0 = bien)
        public static int TiposPrueba(string informe, string dir)
        {
            StringBuilder sb = new StringBuilder("# QUITAFG: tipos de generacion (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")\r\n");
            int fallos = 0, n = 0;
            string iniAntes = Paths.IniFile, envez = Config.GenEnVezDeDlss;
            try
            {
                // 1) los valores sueltos
                Config.GenEnVezDeDlss = "smooth";
                string[][] picks = { new[] { "dlss", "smooth" }, new[] { "vfg", "smooth" }, new[] { "xess", "xess" }, new[] { "fsr3", "fsr3" }, new[] { "smooth", "smooth" },
                                     new[] { "", "" }, new[] { "fsr", "fsr3" }, new[] { "basura", "" } };
                foreach (string[] p in picks)
                {
                    string r = Config.ParsePick(p[0]);
                    bool ok = r == p[1]; n++; if (!ok) fallos++;
                    sb.AppendLine((ok ? "      " : "FALLO ") + "ParsePick(\"" + p[0] + "\") = \"" + r + "\" (esperado \"" + p[1] + "\")");
                }
                string[][] cols = { new[] { ",dlss", ",smooth" }, new[] { ",smooth,0,2", ",smooth" }, new[] { ",xess,0,4", ",xess" }, new[] { "dlss,xess,3,4", "smooth,xess" },
                                    new[] { "", "" }, new[] { "smooth,", "smooth," } };
                foreach (string[] p in cols)
                {
                    GameProfile g = new GameProfile();
                    g.ParsePick(p[0]);
                    bool ok = g.PickText == p[1]; n++; if (!ok) fallos++;
                    sb.AppendLine((ok ? "      " : "FALLO ") + "columna 10 \"" + p[0] + "\" -> \"" + g.PickText + "\" (esperado \"" + p[1] + "\")");
                }
                string[][] listas = { new[] { "dlss,smooth,xess", "smooth,xess,fsr3" }, new[] { "smooth,xess,fsr3", "smooth,xess,fsr3" }, new[] { "dlss", "fsr3" },
                                      new[] { "no", "" }, new[] { "xess,fsr3", "xess,fsr3" } };
                foreach (string[] p in listas)
                {
                    string r = string.Join(",", Config.ParseGenList(p[0]));
                    bool ok = r == p[1]; n++; if (!ok) fallos++;
                    sb.AppendLine((ok ? "      " : "FALLO ") + "generacion_lista \"" + p[0] + "\" -> \"" + r + "\" (esperado \"" + p[1] + "\")");
                }
                // 2) copias de datos de usuario (solo se leen: el ini y los perfiles se copian a una carpeta temporal)
                List<string> carpetas = new List<string>();
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    foreach (string d in Directory.GetDirectories(dir)) if (File.Exists(Path.Combine(d, "consoleenhancer.ini"))) carpetas.Add(d);
                foreach (string d in carpetas)
                {
                    string tmp = Path.Combine(Path.GetTempPath(), "ce_test_tipos_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".ini");
                    File.Copy(Path.Combine(d, "consoleenhancer.ini"), tmp, true);
                    Paths.IniFile = tmp;
                    Config c = Config.Load();
                    c.CeTiposTrasCargar();
                    sb.AppendLine("== " + Path.GetFileName(d) + ": ini generacion_tipo=" + c.FgType + " _60=" + c.FgType60 + " _30=" + c.FgType30 + "; listas 60 " + string.Join(",", c.GenList60) +
                                  " / 30 " + string.Join(",", c.GenList30) + "; en vez de DLSS FG: " + Config.GenEnVezDeDlss);
                    try { File.Delete(tmp); } catch { }
                    bool okd = Config.GenEnVezDeDlss == "smooth"; n++; if (!okd) fallos++;
                    sb.AppendLine((okd ? "      " : "FALLO ") + "en vez de DLSS FG (RTX 4060 Ti): " + Config.GenEnVezDeDlss + " (esperado smooth)");
                    foreach (bool cap in new[] { true, false })
                    {
                        c.FgPick30 = c.FgPick60 = "";
                        string t = c.FgTypeFor(cap, "3840x2160");
                        // (sin el driver ni la tele de verdad: smooth si puede ir aqui; si no, el siguiente de la lista)
                        string esp = Config.GenPuedeIr("smooth", "3840x2160") ? "smooth" : c.GenParecida(cap, "3840x2160", "smooth");
                        bool ok = t == esp; n++; if (!ok) fallos++;
                        sb.AppendLine((ok ? "      " : "FALLO ") + "modo " + (cap ? 30 : 60) + " sin elegir nada: " + t + " (esperado " + esp + ")");
                    }
                    // framegen_type.txt
                    string ft = Path.Combine(d, @"games\framegen_type.txt");
                    if (File.Exists(ft))
                    {
                        string p60 = "", p30 = "";
                        foreach (string raw in File.ReadAllLines(ft))
                        {
                            string l = raw.Trim();
                            if (l.StartsWith("60=")) p60 = Config.ParsePick(l.Substring(3));
                            else if (l.StartsWith("30=")) p30 = Config.ParsePick(l.Substring(3));
                        }
                        bool ok = Config.TipoGen(p30) || p30.Length == 0; n++; if (!ok) fallos++;
                        sb.AppendLine((ok ? "      " : "FALLO ") + "framegen_type.txt \"" + File.ReadAllText(ft).Replace("\r", "").Replace("\n", " ").Trim() + "\" -> 60=" + p60 + " 30=" + p30);
                    }
                    // profiles.txt: cada linea, la columna 10 nueva y el resto igual (lo que se guardaria)
                    string pf = Path.Combine(d, @"games\profiles.txt");
                    if (File.Exists(pf))
                        foreach (string raw in File.ReadAllLines(pf, Encoding.UTF8))
                        {
                            if (raw.Length == 0 || raw.StartsWith("#")) continue;
                            string[] f = raw.Split('|');
                            if (f.Length < 10) continue;
                            GameProfile g = new GameProfile();
                            g.ParsePick(f[9]);
                            string nueva = string.Join("|", f, 0, 9) + "|" + g.PickText;
                            bool ok = (g.Pick60.Length == 0 || Config.TipoGen(g.Pick60)) && (g.Pick30.Length == 0 || Config.TipoGen(g.Pick30)) &&
                                      g.PickText.Split(',').Length <= 2 && (f[9].IndexOf("dlss", StringComparison.Ordinal) < 0 || g.PickText.IndexOf("smooth", StringComparison.Ordinal) >= 0);
                            n++; if (!ok) fallos++;
                            sb.AppendLine((ok ? "      " : "FALLO ") + "perfil: " + raw.Substring(0, Math.Min(raw.Length, 60)).Split('|')[0] + " | col 10 \"" + f[9] + "\" -> \"" + g.PickText + "\" | " +
                                          (nueva.Substring(0, nueva.LastIndexOf('|')) == string.Join("|", f, 0, 9) ? "el resto igual" : "EL RESTO CAMBIA"));
                        }
                }
                if (carpetas.Count == 0) sb.AppendLine("(sin carpetas de datos: --test-tipos-generacion=<informe> --datos-prueba=<carpeta con copias>)");
            }
            catch (Exception ex) { fallos++; sb.AppendLine("FALLO excepcion: " + ex); }
            finally { Paths.IniFile = iniAntes; Config.GenEnVezDeDlss = envez; }
            sb.AppendLine(n + " pruebas, " + fallos + " fallos");
            string ruta = string.IsNullOrEmpty(informe) ? Path.Combine(Paths.Datos, @"games\test_tipos_generacion.txt") : informe;
            try { File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false)); } catch { }
            Console.Write(sb.ToString());
            return fallos;
        }
    }
}
