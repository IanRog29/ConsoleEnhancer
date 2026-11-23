// Avisos.cs - 23-11-2026 (1.2.4 ronda 7, agente TEXTOS). C# 5
//
// El usuario, tras su prueba con la PS5 (23-11 12:27-12:36): "arregla los textos que salen al aplicar cosas que salen mal, salen siempre
// equivocados". Lo que salia (su consoleenhancer.log y el codigo de entonces):
//  - "Aplicar" del menu de ajustes: "Ajustes aplicados · <StatusText>" con StatusText de la 1.2.3: con FSR 3 FG, XeSS FG y Enhanced FG
//    decia "(DLSS-G + Reflex)" (DLSS FG ya no existe); siempre "30 → 60 fps" (Smooth Motion x4, FSR 3 FG x4, Enhanced FG x4 son 30 → 120);
//    con DLSS 5 encendido "DLSS 5 no cabe en este modo"; el escalador "Catmull-Rom, sin DLSS" con FSR 1 / XeSS / sin reescalado; nada de
//    lo que no se pudo poner (Smooth Motion x4 elegido con DLSS 5 iba x2 sin decirlo) y tan largo que la pastilla lo cortaba a 4K;
//  - con relevo de motor (casi todos los cambios de generacion) el mismo aviso dos veces: en el motor de antes al pulsar (diciendo ya lo
//    nuevo con lo de antes a la vista) y en el nuevo al ponerse delante;
//  - la fila "Tipo de generacion" decia "Enhanced FG x4 · baja latencia" (el multiplicador guardado) con la fila "Multiplicador" en x2 al
//    lado ("aplicar Tipo de generación Enhanced FG x4, Multiplicador x2" en el log);
//  - el boton Vista / de captura: "Generación de fotogramas activada · XeSS FG" con Enhanced FG (FgName no la conocia); el de Menu (modo):
//    siempre "30 → 60 fps" y sin decir que generacion; L3: "Generación: Enhanced FG x4 · baja latencia" (el " · " partia el titulo) y el
//    detalle con el x2 de antes; el relevo sin titulo, "Generación: Smooth Motion · 120 fps (esports) · sin generacion" en el modo 120;
//  - A (DLSS 5): "· motor propio" en todo, y "activado" aunque no fuera (sin PrismaNr.dll / no cabe).
// Ahora todos salen de LO QUE VA DE VERDAD, sacado de la config ya puesta (lo que el motor recibe: cadena, Smooth Motion, EngineLive),
// no de lo pedido; si algo de lo pedido no se pudo, se dice por que (corto). Lo que cambia sin tocarlo (p. ej. DLSS 5 encendido baja
// Smooth Motion x4 a x2) tambien sale. Una sola vez: con relevo, al ponerse delante el motor nuevo (y "preparando" al pulsar si tarda).
// --test-avisos[=<informe>]: todas las combinaciones del menu contra lo que recibe el motor (AvisosPrueba, abajo).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        // Una copia para los avisos: lo de antes de un cambio, o lo que va a quedar (otra captura / modo) sin tocar la config de verdad, que
        // leen otros hilos. Copia superficial: solo se lee
        public Config CopiaAvisos() { return (Config)MemberwiseClone(); }

        public static string AvCapPub(string size) { return AvCap(size); }
        static string AvCap(string size)
        {
            return size == "1280x720" ? "720p" : size == "1920x1080" ? "1080p" : size == "2560x1440" ? "1440p" : size == "3840x2160" ? Tx.T("4K nativo", "native 4K") : size;
        }

        // El nombre del tipo, sin multiplicador (menu, selector, bandeja)
        public static string GenNombre(string t)
        {
            switch (t ?? "")
            {
                case "smooth": return "Smooth Motion";
                case "xess": return "XeSS FG";
                case "fsr3": return "FSR 3 FG";
                case "efg": return "Enhanced FG";
                case "no": return Tx.T("Sin generación", "No frame generation");
            }
            return t;
        }

        // Las imagenes reales del modo (sin generacion: las que se ven)
        public int GenBase { get { return Mode120 ? 120 : CapMode ? 30 : 60; } }

        // Lo pedido en este modo (lo elegido con L3 / el menu / el perfil del juego; si no, el del ini) y su multiplicador elegido
        public string GenPedida(out int mult)
        {
            string t = CapMode ? FgPick30 : FgPick60;
            if (!TipoGen(t)) t = CapMode ? FgType30 : FgType60;
            if (!TipoGen(t)) t = FgType;
            mult = t == "smooth" ? (SmoothX4 && CapMode ? 4 : 2) : t == "fsr3" ? FsrMult : t == "efg" ? EfgMult : 2;
            return TipoGen(t) ? t : "";
        }

        // Lo que va DE VERDAD: el tipo ("no" sin generacion) y su multiplicador (1 sin generacion). Lo mismo que se manda al motor y al driver
        // (EngineSmoothMode, EngineFgChain, EngineLive): Smooth Motion con sm>0 (3 = x4: XeSS FG x2 del motor debajo); XeSS FG con la cadena
        // 20 / 21; FSR 3 FG con la 1 (x2, la de AMD) o la 60 (x3 / x4, la nuestra); Enhanced FG con la 50. noVa: por que no hay generacion si
        // estaba pedida (null = apagada a proposito o va)
        public string GenVa(out int mult, out string noVa)
        {
            mult = 1; noVa = null;
            if (!FgAvailable) { noVa = Tx.T("no disponible en este PC", "not available on this PC"); return "no"; }
            if (FgOff) return "no";
            if (Mode120) { noVa = Tx.T("a 120 fps no hay", "none at 120 fps"); return "no"; }
            if (HomeNative) { noVa = Tx.T("en el menú de la consola", "in the console menu"); return "no"; }
            string t = FgTypeNow;
            bool hz100 = Pantalla.Hz() >= 100;
            if (t == "smooth")
            {
                int sm = EngineSmoothMode;
                if (sm <= 0)
                {
                    noVa = NvProfiles.EngineSmoothBroken ? Tx.T("el driver no deja poner Smooth Motion", "the driver doesn't allow Smooth Motion") :
                           !CapMode && !hz100 ? Tx.T("la pantalla va a menos de 100 Hz", "the screen is below 100 Hz") :
                           Tx.T("Smooth Motion no puede ir aquí", "Smooth Motion can't run here");
                    return "no";
                }
                mult = sm == 3 ? 4 : 2;
                return t;
            }
            if (!CapMode && !hz100) { noVa = Tx.T("la pantalla va a menos de 100 Hz", "the screen is below 100 Hz"); return "no"; }
            int chain = EngineFgChain;
            if (t == "xess")
            {
                if (!ChainXess(chain)) { noVa = Tx.T("XeSS FG no va en el motor", "XeSS FG doesn't run in the engine"); return "no"; }
                mult = 2; return t;
            }
            if (t == "fsr3") { mult = FsrMulChain(chain) ? FsrMultNow : 2; return t; }
            if (t == "efg")
            {
                if (!EfgChain(chain)) { noVa = Tx.T("Enhanced FG no puede ir ahora", "Enhanced FG can't run now"); return "no"; }
                mult = EfgMultNow; return t;
            }
            noVa = Tx.T("ninguna puede ir en este PC", "none can run on this PC");
            return "no";
        }

        // Por que un tipo pedido no va con esta captura (null = si va o no se sabe)
        public static string GenNoVaPorqueAv(string t, string capSel)
        {
            if (t == "xess" && !Paths.HayLib("libxess_fg.dll")) return Tx.T("falta libxess_fg.dll en xess\\", "libxess_fg.dll is missing in xess\\");
            if (t == "xess" && XessNo1080(capSel)) return Tx.T("no va con la captura de 1080p", "doesn't run with the 1080p capture");
            if (t == "xess" && (XessCaido || !MotorFgSabe("xess"))) return Tx.T("el motor no ha podido con él en esta sesión", "the engine couldn't run it this session");
            // (1.2.4 ronda 7, INTEGRA7: la de FGREAL -TrayApp.GenNoVaPorque, TiposGeneracion.cs-, tambien aqui)
            // (en la pastilla, corto y sin la clave del ini: la dice el menu -GenNoVaPorque- y el README)
            if (t == "fsr3" && !FsrFgOfrecer) return Tx.T("quitada: fallos y poca fluidez con juego real", "removed (artifacts, poor smoothness)");   // (EFGFIX: en ingles mas corto, que quepa en la pastilla con x4 + DLSS 5)
            if (t == "fsr3" && !Paths.HayLib("amd_fidelityfx_framegeneration_dx12.dll")) return Tx.T("falta su DLL en fsr\\", "its DLL is missing in fsr\\");
            if (t == "smooth" && Ce.NoNvidia) return Tx.T("solo en NVIDIA RTX 40 / 50", "NVIDIA RTX 40 / 50 only");
            if (t == "smooth" && Pantalla.Is119) return Tx.T("no con la tele a 119,88 Hz", "not with the TV at 119.88 Hz");
            if (t == "smooth" && (NvProfiles.SmoothBroken || NvProfiles.EngineSmoothBroken)) return Tx.T("el driver no deja ponerlo", "the driver doesn't allow it");
            if (t == "efg" && EfgCaido) return EfgCaidoTexto(true);   // (1.2.4 ronda 7, EFGFIX: cuando y que se puede reintentar)
            if (t == "efg") { double hz = Pantalla.Hz(); if (hz > 0 && hz < 100) return Tx.T("necesita la pantalla a 100 Hz o más", "needs the screen at 100 Hz or more"); }
            return null;
        }

        // Por que el multiplicador pedido no va (con el tipo que si va)
        string GenMultNoPorque(string t, int pedido, int va)
        {
            if (pedido <= va) return null;
            if (!CapMode) return Tx.T("solo en el modo 30", "only in 30 fps mode");
            string r = "";
            if (t == "smooth") r = SmoothX4Falta;
            else if (t == "fsr3") r = FsrMulNoPorQue(true);
            else if (t == "efg") r = EfgMultFalta(MotorD5Simple);   // (1.2.4 ronda 7, INTEGRA7)
            if (string.IsNullOrEmpty(r)) r = Tx.T("no puede ir ahora", "can't run now");
            // (las del menu empiezan en mayuscula y algunas por "x2 ...": aqui van detras de "x4 no: ")
            if (r.StartsWith("x2 ", StringComparison.Ordinal)) r = r.Substring(3);
            // (1.2.4 ronda 7, INTEGRA7: la de Smooth Motion x4 con DLSS 5 -SmoothX4FaltaD5, para la fila del menu, que enseña x2- detras de
            // "Smooth Motion x2 · ... · x4 no: ", sin repetir el x2 ni el "sin DLSS 5")
            if (r.StartsWith("con DLSS 5 va x2: ", StringComparison.Ordinal)) r = Tx.T("con DLSS 5 la gráfica no llega", "with DLSS 5 the GPU can't keep up");
            if (r.StartsWith("with DLSS 5 it runs x2: ", StringComparison.Ordinal)) r = "with DLSS 5 the GPU can't keep up";
            // (las de SmoothX4Falta salian sin tildes: en pantalla, con ellas)
            r = r.Replace("generacion", "generación").Replace("se ha caido", "se ha caído").Replace("esta sesion", "esta sesión").Replace("no esta o", "no está o");
            // (en minuscula solo las palabras normales del principio, no los nombres: "XeSS FG se ha caído...", "Smooth Motion no se puede...")
            if (Regex.IsMatch(r, "^(Falta|La|Las|El|Los|Apagado|Necesita|Sin|Con|Off|The|Needs|No|Only|With|Solo) ")) r = char.ToLowerInvariant(r[0]) + r.Substring(1);
            return r;
        }

        // La generacion, para los avisos: "Smooth Motion x4 · 30 → 120 fps", "Enhanced FG x2 · baja latencia · 30 → 60 fps",
        // "Sin generación · 30 fps", "120 fps (esports) · sin generación". Con lo pedido que no va: "XeSS FG no: <por que> · Smooth Motion x2 ·
        // 30 → 60 fps", "FSR 3 FG x2 · 30 → 60 fps · x4 no: <por que>". conFps = false: sin las imagenes por segundo cuando no hay generacion
        public string AvisoGen(bool conFps)
        {
            int mult; string noVa;
            string va = GenVa(out mult, out noVa);
            int b = GenBase;
            if (Mode120) return Tx.T("120 fps (esports) · sin generación", "120 fps (esports) · no frame generation");
            if (va == "no")
            {
                string s = Tx.T("Sin generación", "No frame generation") + (noVa != null && !FgOff ? ": " + noVa : "");
                return conFps ? s + " · " + b.ToString(CultureInfo.InvariantCulture) + " fps" : s;
            }
            int pm;
            string pedida = GenPedida(out pm);
            StringBuilder sb = new StringBuilder();
            if (pedida.Length > 0 && pedida != va)
            {
                string porque = GenNoVaPorqueAv(pedida, VideoSize) ?? Tx.T("no va con esto", "not available with this");
                sb.Append(GenNombre(pedida)).Append(Tx.T(" no: ", " unavailable: ")).Append(porque).Append(" · ");
            }
            sb.Append(GenNombre(va)).Append(" x").Append(mult.ToString(CultureInfo.InvariantCulture));
            if (va == "efg") sb.Append(" (").Append(EfgLow ? Tx.T("baja latencia", "low latency") : Tx.T("calidad", "quality")).Append(")");
            sb.Append(" · ").Append(b.ToString(CultureInfo.InvariantCulture)).Append(" → ").Append((b * mult).ToString(CultureInfo.InvariantCulture)).Append(" fps");
            if (pedida == va)
            {
                string mp = GenMultNoPorque(va, pm, mult);
                if (mp != null) sb.Append(" · x").Append(pm.ToString(CultureInfo.InvariantCulture)).Append(Tx.T(" no: ", " unavailable: ")).Append(mp);
            }
            return sb.ToString();
        }

        // DLSS 5: ¿va de verdad? (lo que lleva EngineDlss5Args: dlss5=1). porque: por que no, si esta pedido (null = apagado a proposito o va)
        public bool Dlss5Va(out string porque)
        {
            porque = null;
            if (Dlss5Off) return false;
            if (!Integrado.Dlss5Available) { porque = Tx.T("no instalado", "not installed"); return false; }
            if (HomeNative && !EngineDlss5Home) { porque = Tx.T("en el menú de la consola va sin él", "off in the console menu"); return false; }
            if (EngineDlss5Now) return true;
            porque = !Dlss5FitsNow ? Tx.T("no cabe en este modo", "doesn't fit in this mode") :
                     MotorDlss5Roto ? Tx.T("el motor no ha podido con él", "the engine couldn't run it") :
                     !MotorDlss5Disponible ? Tx.T("falta engine\\PrismaNr.dll", "engine\\PrismaNr.dll is missing") :
                     Tx.T("no puede ir ahora", "can't run now");
            return false;
        }

        // "1 pasada" (+ "(2 no caben)" / "(2 solo sin generación)") de lo que va
        public string Dlss5Pasadas()
        {
            int now = Math.Max(1, Math.Min(3, Dlss5PassesNow)), want = Math.Max(1, Math.Min(3, Passes30));
            string p = now.ToString(CultureInfo.InvariantCulture) + (now > 1 ? Tx.T(" pasadas", " passes") : Tx.T(" pasada", " pass"));
            if (now >= want) return p;
            string w = want.ToString(CultureInfo.InvariantCulture);
            // (16-11, coordinador: en el 60 con generacion 2-3 pasadas no caben; sin ella si: se dice, la generacion no se quita sola)
            bool sinGen = !CapMode && !FgOff && PassesFitWithoutFg(want);
            return p + (sinGen ? " (" + w + Tx.T(" solo sin generación)", " only without frame generation)") : " (" + w + Tx.T(" no caben)", " don't fit)"));
        }

        // "nivel 4 · 1 pasada" de lo que va
        public string Dlss5Detalle()
        {
            return Tx.T("nivel ", "level ") + Math.Max(1, Math.Min(4, Level30)).ToString(CultureInfo.InvariantCulture) + " · " + Dlss5Pasadas();
        }

        // "DLSS 5 nivel 4 · 1 pasada" | "sin DLSS 5" | "DLSS 5 no: no cabe en este modo"
        public string AvisoDlss5()
        {
            string porque;
            if (Dlss5Va(out porque)) return "DLSS 5 " + Dlss5Detalle();
            if (porque == null) return Tx.T("sin DLSS 5", "no DLSS 5");
            return Tx.T("DLSS 5 no: ", "DLSS 5 unavailable: ") + porque;
        }

        // El escalado que hace el motor (upscaler= de EngineLive): "DLSS M (DLSS 4.5)", "XeSS", "FSR 1", "sin reescalado". A 4K nada (""):
        // la captura ya es la pantalla
        public string AvisoEscalado()
        {
            if (WindowFor(VideoSize) == "3840x2160") return "";
            string eu = Ce.EngineUpscaler(this);
            // (el modelo por su letra, "DLSS M": la version -"(DLSS 4.5)"- no cabia en la pastilla con lo demas)
            string s = eu == "dlss" ? "DLSS " + EnginePresetNow : eu == "xess" ? "XeSS" : eu == "fsr4" ? "FSR 4" : eu == "fsr3" ? "FSR 3" :
                       eu == "catrom" ? Tx.T("sin reescalado", "no upscaling") : "FSR 1";
            if (ScalerNow == "dlss" && eu != "dlss") s = Tx.T("DLSS no (solo NVIDIA): ", "DLSS unavailable (NVIDIA only): ") + s;
            return s;
        }

        public string AvisoCaptura() { return Tx.T("Captura ", "Capture ") + AvCap(VideoSize); }
    }

    partial class TrayApp
    {
        // ---------------- lo de antes y lo de despues de un cambio ----------------
        // Los textos de cada parte, sacados en el momento (lo de Enhanced FG / FSR 3 FG es de la clase: una copia de la config no basta)
        sealed class AvisoFoto
        {
            public string Cap, Gen, D5, Esc;
            public static AvisoFoto De(Config c)
            {
                AvisoFoto f = new AvisoFoto();
                f.Cap = c.AvisoCaptura(); f.Gen = c.AvisoGen(true); f.D5 = c.AvisoDlss5(); f.Esc = c.AvisoEscalado();
                return f;
            }
        }

        // La config de ahora, ya preparada, en una copia (para los avisos)
        Config AvisoConfig() { PrepareCfg(); return cfg.CopiaAvisos(); }
        AvisoFoto AvisoAhora() { return AvisoFoto.De(AvisoConfig()); }

        // Lo que cambia entre antes y despues, en el orden del menu (captura, generacion, DLSS 5, escalado). siempre = las partes que se dicen
        // aunque no cambien (las que se han tocado: si no se pudo, sale el por que); nunca = las que ya dice el titulo. "cap,gen" etc. "" = nada
        static string AvisoCambios(AvisoFoto antes, AvisoFoto despues, string siempre, string nunca)
        {
            List<string> l = new List<string>();
            string[] si = (siempre ?? "").Split(','), no = (nunca ?? "").Split(',');
            Func<string, string, string, bool> va = delegate (string k, string a, string d)
            {
                if (string.IsNullOrEmpty(d) || Array.IndexOf(no, k) >= 0) return false;
                return Array.IndexOf(si, k) >= 0 || antes == null || a != d;
            };
            if (va("cap", antes != null ? antes.Cap : null, despues.Cap)) l.Add(despues.Cap);
            if (va("gen", antes != null ? antes.Gen : null, despues.Gen)) l.Add(despues.Gen);
            if (va("d5", antes != null ? antes.D5 : null, despues.D5)) l.Add(despues.D5);
            if (va("esc", antes != null ? antes.Esc : null, despues.Esc)) l.Add(despues.Esc);
            return string.Join(" · ", l.ToArray());
        }

        static string AvisoJunta(params string[] partes)
        {
            List<string> l = new List<string>();
            foreach (string p in partes) if (!string.IsNullOrEmpty(p)) l.Add(p);
            return string.Join(" · ", l.ToArray());
        }

        // ---------------- el aviso de cada accion (con lo de antes ya guardado y lo nuevo ya puesto) ----------------

        // "Aplicar" del menu de ajustes: capV / modo = lo que va a quedar (la captura y el modo se ponen despues, RequestCapture / SetModeN);
        // modoPedido = el del menu (120 con una captura que no lo da va a 60)
        void AjAvisoCalcula(AvisoFoto antes, HashSet<string> cambiadas, string capV, int modo, int modoPedido, out string tit, out string det)
        {
            Config c = AvisoConfig();
            c.VideoSize = capV; c.CapMode = modo == 30; c.Mode120 = modo == 120;
            AvisoFoto d = AvisoFoto.De(c);
            List<string> si = new List<string>();
            Func<string[], bool> alguna = delegate (string[] ids) { foreach (string id in ids) if (cambiadas.Contains(id)) return true; return false; };
            if (alguna(new[] { "cap" })) si.Add("cap");
            if (alguna(new[] { "modo", "fg", "tipo", "efgmul", "efglat", "fsrmul", "smx4" })) si.Add("gen");
            if (alguna(new[] { "d5", "nivel", "pasadas" })) si.Add("d5");
            if (alguna(new[] { "esc", "cap" })) si.Add("esc");
            det = AvisoCambios(antes, d, string.Join(",", si.ToArray()), null);
            if (modoPedido == 120 && modo != 120)
                det = AvisoJunta(Tx.T("120 fps no: la capturadora no lo da a ", "120 fps unavailable: the capture device doesn't do it at ") + Config.AvCapPub(capV), det);
            if (det.Length == 0) det = Tx.T("nada cambia", "nothing changes");
            tit = AjTitulo;
            AvisoSinTitulo(ref tit, ref det);
        }

        // Si es muy largo (mas de 2800 px a 4K: tres cuartos de la pantalla) o no cabe en la pastilla, sin el titulo ("Ajustes aplicados", el
        // juego): lo primero de la lista pasa a titulo
        const int AvisoLargo = 2800;
        static void AvisoSinTitulo(ref string tit, ref string det)
        {
            if (string.IsNullOrEmpty(det)) return;
            float w; try { w = AvisoAncho(PillText(tit, det), 2f); } catch { return; }
            if (w <= AvisoLargo) return;
            int c = det.IndexOf(" · ", StringComparison.Ordinal);
            tit = c > 0 ? det.Substring(0, c) : det;
            det = c > 0 ? det.Substring(c + 3) : null;
        }

        // Boton Vista / de captura, Ctrl+F, la bandeja (SetFrameGen), con fgWanted ya puesto
        void AvisoVista(AvisoFoto antes, out string tit, out string det)
        {
            Config c = AvisoConfig();
            AvisoFoto d = AvisoFoto.De(c);
            int m; string no;
            string va = c.GenVa(out m, out no);
            if (!fgWanted)
            {
                tit = Tx.T("Generación de fotogramas desactivada", "Frame generation off");
                det = AvisoJunta(Tx.T("solo las imágenes reales", "only the real frames") + " · " + c.GenBase.ToString(CultureInfo.InvariantCulture) + " fps",
                                 AvisoCambios(antes, d, null, "gen"));
                return;
            }
            tit = va == "no" ? Tx.T("Generación de fotogramas", "Frame generation") : Tx.T("Generación de fotogramas activada", "Frame generation on");
            det = AvisoCambios(antes, d, "gen", null);
        }

        // El modo (boton Menu / Options / +, Ctrl+M, la bandeja; SetModeN), con el modo ya puesto. nota: algo mas (por que no hay 120)
        void AvisoModo(AvisoFoto antes, int fps, string nota, out string tit, out string det)
        {
            Config c = AvisoConfig();
            AvisoFoto d = AvisoFoto.De(c);
            tit = fps == 120 ? Tx.T("Modo 120 fps (esports)", "120 fps mode (esports)") : fps == 30 ? Tx.T("Modo 30 fps", "30 fps mode") : Tx.T("Modo 60 fps", "60 fps mode");
            int m; string no;
            string va = c.GenVa(out m, out no);
            if (fps == 120) det = AvisoJunta(Tx.T("sin generación · baja latencia", "no frame generation · low latency"), AvisoCambios(antes, d, null, "gen"));
            else if (va == "no" && c.FgOff) det = AvisoJunta(Tx.T("sin generación", "no frame generation"), AvisoCambios(antes, d, null, "gen"));
            else det = AvisoCambios(antes, d, "gen", null);
            if (!string.IsNullOrEmpty(nota)) det = AvisoJunta(det, nota);
        }

        // A / Ctrl+D / la bandeja (ToggleDlss5), con DLSS 5 ya puesto
        void AvisoDlss5Boton(AvisoFoto antes, out string tit, out string det)
        {
            Config c = AvisoConfig();
            AvisoFoto d = AvisoFoto.De(c);
            string porque;
            bool va = c.Dlss5Va(out porque);
            string otros = AvisoCambios(antes, d, null, "d5,esc");
            if (va) { tit = Tx.T("DLSS 5 activado", "DLSS 5 on"); det = AvisoJunta(c.Dlss5Detalle(), otros); }
            else if (porque == null)
            {
                tit = Tx.T("DLSS 5 desactivado", "DLSS 5 off");
                det = AvisoJunta(d.Esc.Length > 0 ? d.Esc : Tx.T("4K nativo", "native 4K"), otros);
            }
            else { tit = "DLSS 5"; det = AvisoJunta(Tx.T("no va: ", "not running: ") + porque, otros); }
        }

        // L1 / L2 / R2 / R1 (nivel) y X / Y / B (pasadas) con DLSS 5 encendido: titulo ya hecho ("DLSS 5 · Nivel 3 de 4" / "DLSS 5 · 2 pasadas")
        // pasadas = el de las pasadas (el titulo ya las dice: detras el nivel y, si no caben, las que van)
        void AvisoNivel(AvisoFoto antes, string titulo, bool pasadas, out string tit, out string det)
        {
            Config c = AvisoConfig();
            AvisoFoto d = AvisoFoto.De(c);
            string porque;
            bool va = c.Dlss5Va(out porque);
            tit = titulo;
            string lo;
            if (!va) lo = porque == null ? Tx.T("se aplicará al activarlo", "will apply when turned on") : Tx.T("no va: ", "not running: ") + porque;
            else if (!pasadas) lo = c.Dlss5Pasadas();
            else
            {
                string p = c.Dlss5Pasadas();
                lo = Tx.T("nivel ", "level ") + Math.Max(1, Math.Min(4, c.Level30)).ToString(CultureInfo.InvariantCulture) + (p.IndexOf('(') > 0 ? " · " + Tx.T("van ", "running ") + p : "");
            }
            det = AvisoJunta(lo, AvisoCambios(antes, d, null, "d5"));
        }

        // R3 / Ctrl+R / la bandeja (ToggleScaler), con el escalador ya puesto
        void AvisoEscalador(AvisoFoto antes, out string tit, out string det)
        {
            AvisoFoto d = AvisoFoto.De(AvisoConfig());
            tit = Tx.T("Escalador", "Upscaler");
            det = AvisoJunta(d.Esc, AvisoCambios(antes, d, null, "esc"));
        }

        // L3 / Ctrl+G / la bandeja (GenApply), con el tipo ya puesto
        void AvisoTipo(AvisoFoto antes, out string tit, out string det)
        {
            tit = Tx.T("Generación", "Frame generation");
            det = AvisoCambios(antes, AvisoFoto.De(AvisoConfig()), "gen", null);
        }

        // La cruceta / Ctrl+1-4 / la bandeja (RequestCapture): lo que queda con la captura nueva
        void AvisoCapturaBoton(AvisoFoto antes, string size, out string tit, out string det)
        {
            Config c = AvisoConfig();
            c.VideoSize = size;
            if (c.Mode120 && !c.Captura120Ok(size)) { c.Mode120 = false; c.CapMode = false; }   // (RequestCapture: a 60)
            AvisoFoto d = AvisoFoto.De(c);
            tit = d.Cap;
            det = AvisoCambios(antes, d, "esc", "cap");
        }

        // (pruebas, AvisoApp) el escalador de una fila del menu
        void AjEscPoner(string v)
        {
            if (v.StartsWith("pick:")) SetScalerChoice(true, v.Substring(5));
            else SetScalerChoice(v != "alt");
            if (v.StartsWith("dlss:")) { string l = v.Substring(5); Config.DlssPick = l == "auto" ? "" : l; }
        }

        // (pruebas) el "Aplicar" del menu sin abrir nada: lo mismo que AjustesPonerTodo hasta el aviso, y la captura y el modo puestos como
        // los pondrian RequestCapture / SetModeN
        int ajPruebaModoPedido;
        void AjAvisoDe(Dictionary<string, string> e, HashSet<string> cambiadas, bool prueba, out string tit, out string det)
        {
            AvisoFoto antes = AvisoAhora();
            int modoPedido;
            string capV;
            int modo = AjModoDe(e, out capV, out modoPedido);
            ajPruebaModoPedido = modoPedido;
            lock (profileSync) applying = true;
            try { AjPonerEstado(e, modo); }
            finally { applying = false; }
            AjAvisoCalcula(antes, cambiadas, capV, modo, modoPedido, out tit, out det);
            if (prueba)
            {
                cfg.VideoSize = capV; cfg.CapMode = modo == 30; cfg.Mode120 = modo == 120;
                if (cfg.Mode120 && !cfg.Captura120Ok(capV)) { cfg.Mode120 = false; cfg.CapMode = false; }
            }
        }

        // El titulo del "Aplicar" del menu de ajustes y lo que sale al pulsar si el motor nuevo tarda
        static string AjTitulo { get { return Tx.T("Ajustes aplicados", "Settings applied"); } }
        static string PillPreparando(string title)
        {
            if (title == null) return null;
            if (title == AjTitulo) return Tx.T("Aplicando ajustes · preparando…", "Applying settings · getting ready…");
            return title + " · " + Tx.T("preparando…", "getting ready…");
        }

        // ---------------- un aviso cada vez, cuando se ve ----------------
        // (RestartLs con el motor delante y sin relevo ahora) si lo puesto pide otro motor que llegara despues (CheckEngineFfx / Smooth / Fixed,
        // a la vuelta de WatchLoop), el aviso entero va con el (ReabrirMotorSmooth lo coge) y ahora solo "preparando"; si no, ya
        readonly object avisoSync = new object();
        string avisoTit, avisoSub;
        DateTime avisoHasta = DateTime.MinValue;

        void AvisoFinal(string title, string subtitle)
        {
            if (title == null) return;
            bool despues = false;
            try { PrepareCfg(); despues = Config.EngineActive && cfg.EngineNow && relevo == null && EngineIdentityDiff(mpv) != null; } catch { }
            if (despues && PadRecent)
            {
                // (solo de algo que ha hecho el usuario -lo demas sale ya, como siempre- y poco rato: el relevo empieza en ~1 s; si no llega, que
                // no lo coja otro cambio de despues)
                AvisoGuarda(title, subtitle, 15);
                Osd(PillPreparando(title));
                return;
            }
            AvisoOlvida();
            Osd(PillText(title, subtitle));
        }

        // El aviso que espera al motor nuevo (o a la imagen tras reabrir): true = habia uno reciente (se quita)
        bool AvisoToma(out string title, out string subtitle)
        {
            lock (avisoSync)
            {
                title = avisoTit; subtitle = avisoSub;
                bool ok = title != null && DateTime.Now < avisoHasta;
                avisoTit = avisoSub = null;
                return ok;
            }
        }

        // segundos: lo que puede esperar (al relevo que empieza enseguida, 15-20; a la imagen reabierta, 90: Enhanced FG compila ~25 s la 1a vez)
        void AvisoGuarda(string title, string subtitle, int segundos) { lock (avisoSync) { avisoTit = title; avisoSub = subtitle; avisoHasta = DateTime.Now.AddSeconds(segundos); } }
        void AvisoOlvida() { lock (avisoSync) { avisoTit = avisoSub = null; } }

        // (RunMpv, "El motor ya muestra imagen": tras reabrir la imagen -otra captura que no se cambia en marcha-) el aviso que esperaba
        void AvisoTrasReabrir()
        {
            string t, s;
            if (AvisoToma(out t, out s)) Osd(PillText(t, s));
        }


        // ---------------- --test-avisos[=<informe>] ----------------
        // Todas las combinaciones del menu de ajustes (tipo, multiplicador, baja latencia, DLSS 5, captura, escalador, modo, generacion si/no)
        // desde varios puntos de partida, con las filas de verdad (AjustesModelo / AjustesDepende: lo que el menu deja elegir; lo bloqueado o
        // que no sale no se prueba) y el "Aplicar" de verdad (AjRecoge, AjPonerEstado, AjAvisoCalcula; la captura y el modo puestos como
        // RequestCapture / SetModeN) sin abrir nada (ni motor, ni grafica, ni ventanas). El aviso se compara con lo que recibiria el motor
        // (EngineLive, la cadena y el Smooth Motion del driver), no con lo pedido: el tipo y el multiplicador que van, "30 → 120 fps", DLSS 5
        // si / no y sus pasadas, el escalado; el por que de lo pedido que no va; nada de lo que ya no existe (DLSS FG, DLSS-G, Magpie, mpv,
        // PrismaFg, Catmull-Rom, "motor propio"); que quepa en la pastilla del motor a 4K. Y los demas avisos (botones Vista, Menu, A, R3, L3,
        // nivel, cruceta, Ver lo puesto) y el aviso una sola vez (AvisoFinal). Pantalla fingida a 120 Hz y a 60, DLSS 5 / PrismaNr.dll
        // instalados y sin. Hay que darle --datos=<copia>: escribe lo elegido (games\*.txt y el ini) como la app. Devuelve los fallos (0 = bien)
        public static int AvisosPrueba(string informe)
        {
            StringBuilder sb = new StringBuilder();
            int fallos = 0, n = 0, saltados = 0;
            List<string> fallosTxt = new List<string>();
            FieldInfo f5 = typeof(Integrado).GetField("dlss5State", BindingFlags.NonPublic | BindingFlags.Static);
            object f5Antes = f5 != null ? f5.GetValue(null) : null;
            bool ingles = Tx.Ingles;
            extrasVisto = DateTime.Now.AddDays(1);   // (AjustesModelo: sin recoger DLL de extras\ en la prueba)
            Stopwatch sw = Stopwatch.StartNew();
            Config baseCfg = new Config();
            try { baseCfg = Config.Load(); } catch (Exception ex) { sb.AppendLine("(sin ini: " + ex.Message + ")"); }
            try
            {
                foreach (bool en in new[] { false, true })
                    foreach (int hz in new[] { 120, 60 })
                        foreach (bool d5Dll in new[] { true, false })
                        {
                            if (en && (hz != 120 || !d5Dll)) continue;   // (en ingles, una vuelta: los textos)
                            Idioma.Poner(en ? "en" : "es");
                            Pantalla.HzPrueba = hz;
                            if (f5 != null) f5.SetValue(null, d5Dll ? 1 : 0);
                            Config.MotorDlss5Prueba1(d5Dll ? 1 : 0);
                            fallos += AvisosVuelta(sb, fallosTxt, baseCfg, hz, d5Dll, en, ref n, ref saltados);
                        }
                Idioma.Poner("es");
                Pantalla.HzPrueba = 120;
                if (f5 != null) f5.SetValue(null, 1);
                Config.MotorDlss5Prueba1(1);
                fallos += AvisosUnaVez(sb, fallosTxt, baseCfg, ref n);
                fallos += AvisosNoPuede(sb, fallosTxt, baseCfg, ref n);
                fallos += AvisosEscalera(sb, fallosTxt, ref n);   // (1.3.1, FPS)
                fallos += AvisosSesion(sb, fallosTxt, baseCfg, informe, ref n);
            }
            catch (Exception ex) { fallos++; sb.AppendLine("FALLO: " + ex); fallosTxt.Add("excepcion: " + ex.Message); }
            finally
            {
                Pantalla.HzPrueba = -1;
                if (f5 != null) f5.SetValue(null, f5Antes);
                Config.MotorDlss5Prueba1(-1);
                Config.EngineActive = false;
                Idioma.Poner(ingles ? "en" : "es");
            }
            StringBuilder cab = new StringBuilder("# TEXTOS: avisos al aplicar contra lo que va de verdad (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")\r\n");
            cab.AppendLine(n + " casos probados, " + saltados + " combinaciones que el menu no deja elegir (no se prueban), " + fallos + " fallo(s), " +
                           (sw.ElapsedMilliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s");
            int k = 0;
            foreach (string f in fallosTxt) { if (k++ >= 60) break; cab.AppendLine("FALLO " + f); }
            string txt = cab.ToString() + "\r\n" + sb.ToString();
            Console.Write(cab.ToString());
            try { if (!string.IsNullOrEmpty(informe)) File.WriteAllText(informe, txt, new UTF8Encoding(false)); } catch { }
            return fallos;
        }

        // Puntos de partida (los de la prueba del usuario del 23-11: 4K modo 30 Smooth Motion x2 con DLSS 5; 1440p XeSS FG con DLSS M; 4K FSR 3
        // FG x4 sin DLSS 5; 1440p modo 60 Enhanced FG x4 con R3 -FSR 1- y la generacion apagada): captura, modo, tipo, multiplicador, DLSS 5,
        // escalador, generacion si / no
        static readonly object[][] AvisoPartidas = {
            new object[] { "3840x2160", "30", "smooth", 2, true,  "dlss:auto", true  },
            new object[] { "2560x1440", "30", "xess",   2, true,  "dlss:M",    true  },
            new object[] { "3840x2160", "30", "fsr3",   4, false, "dlss:auto", true  },
            new object[] { "2560x1440", "60", "efg",    4, false, "alt",       false },
        };

        // Una vuelta (pantalla, DLLs, idioma): cada punto de partida x cada combinacion del menu
        static int AvisosVuelta(StringBuilder sb, List<string> fallosTxt, Config baseCfg, int hz, bool d5Dll, bool en, ref int n, ref int saltados)
        {
            int fallos = 0;
            sb.AppendLine("== pantalla " + hz + " Hz, DLSS 5 " + (d5Dll ? "instalado" : "sin instalar") + ", " + (en ? "ingles" : "espanol"));
            string[] tipos = { "smooth", "xess", "fsr3", "efg" };
            string[] caps = { "3840x2160", "2560x1440", "1920x1080" };
            string[] escs = { "dlss:auto", "dlss:K", "pick:fsr", "pick:xess", "pick:ninguno" };
            string[] modos = { "30", "60", "120" };
            foreach (object[] pa in AvisoPartidas)
            {
                foreach (string cap in caps)
                    foreach (string modo in modos)
                        foreach (bool fg in new[] { true, false })
                            foreach (string tipo in tipos)
                                foreach (int mult in tipo == "smooth" ? new[] { 2, 4 } : tipo == "xess" ? new[] { 2 } : new[] { 2, 3, 4 })
                                    foreach (bool lat in tipo == "efg" ? new[] { false, true } : new[] { false })
                                    {
                                        // (sin generacion o a 120 el tipo y el multiplicador no se eligen: una vez)
                                        if ((!fg || modo == "120") && !(tipo == (string)pa[2] && mult == 2 && !lat)) continue;
                                        if (modo == "120" && !fg) continue;
                                        foreach (bool d5 in new[] { true, false })
                                        {
                                            // el escalador: todos con 1440p, el modo 30 y la generacion; con lo demas, el de partida y FSR 1
                                            string[] es = cap == "2560x1440" && modo == "30" && fg ? escs : new[] { (string)pa[5] == "alt" ? "pick:fsr" : (string)pa[5], "pick:ninguno" };
                                            foreach (string esc in es)
                                            {
                                                if (en && (cap != "2560x1440" || esc != es[0] || !d5)) continue;   // (en ingles, menos)
                                                string fallo = AvisoCaso(sb, baseCfg, pa, tipo, mult, lat, d5, cap, modo, fg, esc);
                                                if (fallo == "") { saltados++; continue; }
                                                n++;
                                                if (fallo != null) { fallos++; fallosTxt.Add(fallo); }
                                            }
                                        }
                                    }
                // los demas avisos (botones y bandeja) desde este punto de partida
                foreach (string boton in new[] { "vista", "menu", "a", "r3", "l3", "estado", "nivel", "pasadas", "captura" })
                {
                    string fallo = AvisoBoton(sb, baseCfg, pa, boton);
                    if (fallo == "") { saltados++; continue; }
                    n++;
                    if (fallo != null) { fallos++; fallosTxt.Add(fallo); }
                }
            }
            return fallos;
        }

        // Una app de prueba con un punto de partida (sin nada abierto)
        static TrayApp AvisoApp(Config baseCfg, object[] pa)
        {
            Config c = baseCfg.CopiaAvisos();
            c.Engine = "propio";
            if (!File.Exists(c.EnginePath)) c.EnginePath = Assembly.GetExecutingAssembly().Location;   // (EngineNow mira que exista)
            c.FgAvailable = true; c.HomeNative = false; c.EngineVrr = 1; c.EngineDlss5 = true; c.Permit120 = "auto";
            c.GenList30 = new[] { "smooth", "xess", "fsr3" }; c.GenList60 = new[] { "smooth", "xess", "fsr3" };
            c.Scaler = "dlss"; c.AltScaler = "fsr";
            c.Capturas120 = new[] { "1920x1080", "2560x1440" };
            c.FgType = "smooth"; c.FgType30 = ""; c.FgType60 = "";
            Config.MotorGenCaidaOlvida();
            Config.MotorDlss5Roto = false;
            TrayApp t = new TrayApp(c, true);
            Config.EngineActive = true;
            t.lsReady = true;
            string cap = (string)pa[0], modo = (string)pa[1], tipo = (string)pa[2];
            int mult = (int)pa[3];
            c.VideoSize = cap; c.CapMode = modo == "30"; c.Mode120 = modo == "120";
            t.fgWanted = (bool)pa[6]; t.dlss5Shown = (bool)pa[4]; t.dlss5Level = 4; t.dlss5Passes = 1;
            c.FgPick30 = tipo; c.FgPick60 = tipo;
            c.SmoothX4 = tipo == "smooth" && mult == 4;
            Config.FsrMultSet(tipo == "fsr3" ? mult : 2);
            Config.EfgSet(tipo == "efg" ? mult : 2, false);
            t.AjEscPoner((string)pa[5]);
            t.PrepareCfg();
            return t;
        }

        // Una fila del menu a un valor, como con el mando (AjustesTrasCambio). 0 = puesta (o ya estaba), 1 = la fila no esta / no se puede
        // tocar (se sigue sin ella), 2 = ese valor no sale o esta bloqueado (la combinacion no se puede elegir)
        int AjPruebaPon(string id, string val)
        {
            FilaAjuste f = AjFila(id);
            if (f == null || !f.Visible || !f.Activa) return f != null && f.Visible && f.Val != val ? 2 : 1;
            int i = f.Vals.IndexOf(val);
            if (i < 0 || (f.Bloqueado(i) && i != f.Sel)) return 2;
            if (i == f.Sel) return 0;
            f.Sel = i;
            AjustesTrasCambio(f);
            return 0;
        }

        // Lo que recibiria el motor (el oraculo): tipo y multiplicador de la generacion, base, DLSS 5 si/no y el escalado
        sealed class AvisoMotor
        {
            public string Tipo = "no", Modo = "", Upscaler = "", Preset = "", Live = "";
            public int Mult = 1, Base = 30, Sm, Chain, Pasadas;
            public bool Dlss5, Baja;
            public override string ToString()
            {
                return Tipo + (Tipo != "no" ? " x" + Mult : "") + (Tipo == "efg" ? (Baja ? " latencia" : " calidad") : "") + " base " + Base + " (sm " + Sm + ", cadena " + Chain + ")" +
                       (Dlss5 ? " dlss5 p" + Pasadas : " sin-dlss5") + " " + Upscaler + (Upscaler == "dlss" ? " " + Preset : "");
            }
        }

        static AvisoMotor AvisoOraculo(Config c)
        {
            AvisoMotor m = new AvisoMotor();
            m.Sm = c.EngineSmoothMode; m.Chain = c.EngineFgChain;
            string live = EngineLive(c, m.Sm > 0, m.Chain);
            m.Live = live;
            Func<string, string> tok = delegate (string k) { Match mm = Regex.Match(live, "(?:^| )" + k + "=([^ ]*)"); return mm.Success ? mm.Groups[1].Value : null; };
            bool fg1 = tok("fg") == "1";
            m.Modo = tok("mode") ?? "";
            m.Base = m.Modo == "120" ? 120 : m.Modo == "30" ? 30 : 60;
            m.Upscaler = tok("upscaler") ?? ""; m.Preset = tok("preset") ?? "";
            m.Dlss5 = tok("dlss5") == "1";
            int p; if (int.TryParse(tok("dlss5_pasadas") ?? "", out p)) m.Pasadas = p;
            int efg = 0; int.TryParse(tok("efg") ?? "0", out efg);
            int fsm = 0; int.TryParse(tok("fsrfg_multi") ?? "0", out fsm);
            if (m.Sm == 3 && Config.ChainXess(m.Chain) && fg1) { m.Tipo = "smooth"; m.Mult = 4; }
            else if (m.Sm > 0 && m.Sm < 3) { m.Tipo = "smooth"; m.Mult = 2; }
            else if (m.Chain == 1 && fg1) { m.Tipo = "fsr3"; m.Mult = 2; }
            else if (Config.FsrMulChain(m.Chain) && fsm > 0) { m.Tipo = "fsr3"; m.Mult = fsm; }
            else if (Config.ChainXess(m.Chain) && fg1) { m.Tipo = "xess"; m.Mult = 2; }
            else if (Config.EfgChain(m.Chain) && efg > 0) { m.Tipo = "efg"; m.Mult = efg; m.Baja = tok("efg_modo") == "latencia"; }
            return m;
        }

        static readonly string[] AvisoPalabrasViejas = { "DLSS-G", "DLSS FG", "Magpie", "mpv", "PrismaFg", "Catmull", "motor propio", "own engine", "imágenes intercaladas",
                                                         "frames inserted", "Un momento", "One moment", "RTX Video", "generacion", "Generacion", "caido", " esta sesion", "(actual)",
                                                         "xeSS", "smooth Motion", "enhanced FG", "fSR", "dLSS" };

        // Lo que tiene que decir un aviso de este estado del motor (null = bien; si no, que falla)
        static string AvisoCompara(string aviso, AvisoMotor m, Config c, bool genImporta, bool d5Importa, bool escImporta, bool genPorque)
        {
            if (string.IsNullOrEmpty(aviso)) return "aviso vacio";
            foreach (string w in AvisoPalabrasViejas) if (aviso.IndexOf(w, StringComparison.Ordinal) >= 0) return "dice \"" + w + "\" (ya no existe / sin tilde)";
            if (aviso.Contains(" ·  · ") || aviso.Contains(" · · ") || aviso.EndsWith(" · ") || aviso.StartsWith(" · ") || aviso.EndsWith(" ·")) return "separadores de mas";
            string sinGen = Tx.T("in generación", "o frame generation"), nivel = Tx.T("nivel ", "level ");
            string[] nombres = { "Smooth Motion", "XeSS FG", "FSR 3 FG", "Enhanced FG" };
            string[] tiposN = { "smooth", "xess", "fsr3", "efg" };
            if (genImporta)
            {
                if (m.Tipo == "no")
                {
                    if (m.Base == 120) { if (aviso.IndexOf("120 fps", StringComparison.Ordinal) < 0) return "a 120 y no dice 120 fps"; }
                    if (!(aviso.Contains(sinGen) || aviso.Contains(Tx.T("desactivada", " off"))))
                        return "va sin generacion y no lo dice";
                    if (aviso.Contains("→")) return "dice \"" + Regex.Match(aviso, "[0-9]+ → [0-9]+ fps").Value + "\" y va sin generacion";
                    for (int i = 0; i < nombres.Length; i++)
                        if (Regex.IsMatch(aviso, Regex.Escape(nombres[i]) + " x[0-9]")) return "dice " + nombres[i] + " con multiplicador y va sin generacion";
                }
                else
                {
                    string nombre = Config.GenNombre(m.Tipo);
                    string want = nombre + " x" + m.Mult;
                    if (aviso.IndexOf(want, StringComparison.Ordinal) < 0) return "va " + want + " y no lo dice";
                    for (int i = 0; i < nombres.Length; i++)
                        if (tiposN[i] != m.Tipo && Regex.IsMatch(aviso, Regex.Escape(nombres[i]) + " x[0-9]")) return "dice " + nombres[i] + " con multiplicador y va " + want;
                    foreach (Match mx in Regex.Matches(aviso, Regex.Escape(nombre) + " x([0-9])"))
                        if (mx.Groups[1].Value != m.Mult.ToString(CultureInfo.InvariantCulture)) return "dice " + mx.Value + " y va " + want;
                    string fps = m.Base + " → " + (m.Base * m.Mult) + " fps";
                    if (aviso.IndexOf(fps, StringComparison.Ordinal) < 0) return "no dice \"" + fps + "\"";
                    if (Regex.Matches(aviso, "[0-9]+ → [0-9]+ fps").Count != 1) return "dos cuentas de fps";
                    if (m.Tipo == "efg")
                    {
                        string lat = m.Baja ? Tx.T("baja latencia", "low latency") : Tx.T("calidad", "quality");
                        if (aviso.IndexOf(lat, StringComparison.Ordinal) < 0) return "Enhanced FG sin \"" + lat + "\"";
                    }
                    if (m.Tipo != "smooth" && aviso.Contains("Smooth Motion") && !aviso.Contains("Smooth Motion" + Tx.T(" no:", " unavailable:"))) return "dice Smooth Motion y va " + want;
                }
                // lo pedido que no va: el por que
                if (genPorque && !c.FgOff && !c.Mode120)
                {
                    int pm; string pedida = c.GenPedida(out pm);
                    if (pedida.Length > 0)
                    {
                        bool tipoDistinto = m.Tipo != "no" && pedida != m.Tipo;
                        bool multMenos = m.Tipo != "no" && pedida == m.Tipo && pm > m.Mult;
                        bool nada = m.Tipo == "no";
                        string marca = Tx.T(" no: ", " unavailable: ");
                        if (tipoDistinto && aviso.IndexOf(Config.GenNombre(pedida) + marca, StringComparison.Ordinal) < 0) return "pedido " + Config.GenNombre(pedida) + ", va " + m.Tipo + " y no dice por que";
                        if (multMenos && aviso.IndexOf("x" + pm + marca, StringComparison.Ordinal) < 0) return "pedido x" + pm + ", va x" + m.Mult + " y no dice por que";
                        if (nada && aviso.IndexOf(sinGen + ": ", StringComparison.Ordinal) < 0) return "pedida generacion, va sin ella y no dice por que";
                        if (!tipoDistinto && !multMenos && !nada && (aviso.Contains("FG" + marca) || aviso.Contains("Motion" + marca) || Regex.IsMatch(aviso, "x[0-9]" + Regex.Escape(marca))))
                            return "dice que la generacion pedida no va y va";
                    }
                }
            }
            if (d5Importa)
            {
                if (m.Dlss5)
                {
                    if (aviso.IndexOf(nivel, StringComparison.OrdinalIgnoreCase) < 0) return "DLSS 5 va y no dice el nivel";
                    if (aviso.Contains(Tx.T("DLSS 5 desactivado", "DLSS 5 off")) || aviso.Contains(Tx.T("sin DLSS 5", "no DLSS 5")) || aviso.Contains(Tx.T("DLSS 5 no:", "DLSS 5 unavailable:"))) return "DLSS 5 va y dice que no";
                    string pas = m.Pasadas + (m.Pasadas > 1 ? Tx.T(" pasadas", " passes") : Tx.T(" pasada", " pass"));
                    if (aviso.IndexOf(pas, StringComparison.Ordinal) < 0) return "no dice \"" + pas + "\"";
                }
                else
                {
                    if (Regex.IsMatch(aviso, "DLSS 5 (" + nivel + "|" + Tx.T("activado", "on") + ")")) return "DLSS 5 no va y dice que si";
                    bool dPorque = aviso.Contains(Tx.T("DLSS 5 no:", "DLSS 5 unavailable:")) || (aviso.StartsWith("DLSS 5 ·", StringComparison.Ordinal) && aviso.Contains(Tx.T("no va:", "not running:")));
                    if (!(aviso.Contains(Tx.T("DLSS 5 desactivado", "DLSS 5 off")) || aviso.Contains(Tx.T("sin DLSS 5", "no DLSS 5")) || dPorque || aviso.Contains(Tx.T("se aplicará al activarlo", "will apply when turned on"))))
                        return "DLSS 5 no va y no lo dice";
                    if (!c.Dlss5Off && !dPorque) return "DLSS 5 pedido, no va y no dice por que";
                }
            }
            if (escImporta && Config.WindowFor(c.VideoSize) != "3840x2160")
            {
                string e = m.Upscaler == "dlss" ? "DLSS " + m.Preset : m.Upscaler == "xess" ? "XeSS" : m.Upscaler == "fsr1" ? "FSR 1" :
                           m.Upscaler == "catrom" ? Tx.T("sin reescalado", "no upscaling") : m.Upscaler == "fsr4" ? "FSR 4" : "FSR 3";
                if (aviso.IndexOf(e, StringComparison.Ordinal) < 0) return "el escalado va \"" + e + "\" y no lo dice";
            }
            // que quepa en la pastilla del motor a 4K (Osd.MaxW con la sombra de 18 px a cada lado, a la escala 2)
            float ancho = AvisoAncho(aviso, 2f);
            if (ancho > AvisoMaxW) return "no cabe en la pastilla a 4K (" + (int)ancho + " de " + AvisoMaxW + " px)";
            return null;
        }

        // El ancho de la pastilla del motor (Osd.Render: titulo 25 px, el resto 23 px con "   ·   "), en pixeles a la escala u
        static Font avFt, avFb; static float avU;
        static readonly object avFontSync = new object();
        // ¿Cabe en la pastilla del motor a 4K? (la mas estrecha: Osd.MaxW = 3600 px -1.2.4 ronda 7; antes 2400- con la sombra, a la escala 2 =
        // 1764 px de 1080p; a 1080p / 1440p cabe el ancho de la pantalla, 1920)
        public const int AvisoMaxW = 3600 - 36 * 2;
        public static bool AvisoCabe(string aviso) { try { return AvisoAncho(aviso, 2f) <= AvisoMaxW; } catch { return true; } }
        static float AvisoAncho(string aviso, float u) { lock (avFontSync) return AvisoAnchoL(aviso, u); }
        static float AvisoAnchoL(string aviso, float u)
        {
            string t = Idioma.T(aviso), title = t, rest = null;
            int cut = t.IndexOf(" · ", StringComparison.Ordinal);
            if (cut > 0) { title = t.Substring(0, cut); rest = t.Substring(cut + 3).Replace(" · ", "   ·   "); }
            if (avFt == null || avU != u)
            {
                avU = u;
                avFt = new Font("Segoe UI Semibold", 25 * u, FontStyle.Regular, GraphicsUnit.Pixel);
                avFb = new Font("Segoe UI", 23 * u, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            using (Bitmap b = new Bitmap(4, 4))
            using (Graphics g = Graphics.FromImage(b))
            {
                StringFormat sf = StringFormat.GenericTypographic;
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                float st = g.MeasureString(title, avFt, 100000, sf).Width, sr = rest != null ? g.MeasureString(rest, avFb, 100000, sf).Width : 0;
                return 30 * u + 11 * u + 16 * u + st + (rest != null ? 18 * u + sr : 0) + 30 * u;
            }
        }

        // Un caso del menu: las filas a esos valores, "Aplicar", y el aviso contra lo que va. null = bien, "" = el menu no deja elegirlo
        static string AvisoCaso(StringBuilder sb, Config baseCfg, object[] pa, string tipo, int mult, bool lat, bool d5, string cap, string modo, bool fg, string esc)
        {
            string caso = "partida " + pa[0] + "/" + pa[1] + "/" + pa[2] + " x" + pa[3] + (((bool)pa[4]) ? " +d5" : "") + (((bool)pa[6]) ? "" : " fg-no") + " -> " +
                          cap + " modo" + modo + (fg ? " " + tipo + " x" + mult + (lat ? " baja-lat" : "") : " fg-no") + (d5 ? " d5" : " sin-d5") + " " + esc;
            try
            {
                TrayApp t = AvisoApp(baseCfg, pa);
                t.AjustesModelo();
                // las filas en el orden del menu; tras cada una, lo que depende (AjustesTrasCambio)
                if (t.AjPruebaPon("cap", cap) == 2) return "";
                if (t.AjPruebaPon("modo", modo) == 2) return "";
                if (t.AjPruebaPon("fg", fg ? "1" : "0") == 2) return "";
                if (fg && modo != "120")
                {
                    if (t.AjPruebaPon("tipo", tipo) == 2) return "";
                    string id = tipo == "smooth" ? "smx4" : tipo == "fsr3" ? "fsrmul" : tipo == "efg" ? "efgmul" : null;
                    if (id != null && t.AjPruebaPon(id, mult.ToString(CultureInfo.InvariantCulture)) == 2) return "";
                    if (id == null && mult != 2) return "";
                    if (tipo == "efg" && t.AjPruebaPon("efglat", lat ? "1" : "0") == 2) return "";
                }
                if (t.AjPruebaPon("d5", d5 ? "1" : "0") == 2) return "";
                if (t.AjPruebaPon("nivel", "4") == 2) return "";
                if (t.AjPruebaPon("pasadas", "1") == 2) return "";
                if (t.AjPruebaPon("esc", esc) == 2) return "";
                if (t.AjCambios() == 0) return "";   // ("Aplicar" sin cambios: se cierra sin aviso)
                Dictionary<string, string> e;
                HashSet<string> cambiadas;
                List<string> que;
                t.AjRecoge(out e, out cambiadas, out que);
                string tit, det;
                t.AjAvisoDe(e, cambiadas, true, out tit, out det);
                string aviso = PillText(tit, det);
                Config fin = t.AvisoConfig();
                AvisoMotor m = AvisoOraculo(fin);
                bool genToca = cambiadas.Contains("modo") || cambiadas.Contains("fg") || cambiadas.Contains("tipo") || cambiadas.Contains("smx4") ||
                               cambiadas.Contains("fsrmul") || cambiadas.Contains("efgmul") || cambiadas.Contains("efglat");
                bool d5Toca = cambiadas.Contains("d5") || cambiadas.Contains("nivel") || cambiadas.Contains("pasadas");
                bool escToca = cambiadas.Contains("esc") || cambiadas.Contains("cap");
                string r = AvisoCompara(aviso, m, fin, genToca, d5Toca, escToca, genToca);
                if (r == null && t.ajPruebaModoPedido == 120 && m.Base != 120 && !(aviso.Contains("120 fps no") || aviso.Contains("120 fps unavailable"))) r = "120 pedido, va " + m.Base + " y no dice por que";
                if (r == null && cambiadas.Contains("cap") && aviso.IndexOf(fin.AvisoCaptura(), StringComparison.Ordinal) < 0) r = "captura cambiada y no la dice";
                if (r == null && !aviso.StartsWith(AjTitulo + " · ", StringComparison.Ordinal) && AvisoAncho(AjTitulo + " · " + aviso, 2f) <= AvisoLargo) r = "sin el titulo " + AjTitulo + " y cabia";
                sb.AppendLine((r == null ? "      " : "FALLO ") + caso + " | menu: " + string.Join(", ", que.ToArray()) + " | motor: " + m + " | " + aviso + (r != null ? "   <- " + r : ""));
                return r == null ? null : caso + ": " + r + " | " + aviso + " | motor: " + m;
            }
            catch (Exception ex) { sb.AppendLine("FALLO " + caso + ": " + ex); return caso + ": " + ex.GetType().Name + " " + ex.Message; }
        }

        // Los botones del mando / atajos / bandeja: el aviso de cada uno (las mismas funciones que la app) contra lo que queda
        static string AvisoBoton(StringBuilder sb, Config baseCfg, object[] pa, string boton)
        {
            string caso = "partida " + pa[0] + "/" + pa[1] + "/" + pa[2] + " x" + pa[3] + (((bool)pa[4]) ? " +d5" : "") + (((bool)pa[6]) ? "" : " fg-no") + " -> boton " + boton;
            try
            {
                TrayApp t = AvisoApp(baseCfg, pa);
                string tit = null, det = null;
                bool gen = true, d5 = false, esc = false, porque = true;
                AvisoFoto antes = t.AvisoAhora();
                switch (boton)
                {
                    case "vista": t.fgWanted = !t.fgWanted; t.AvisoVista(antes, out tit, out det); break;
                    case "menu":
                        {
                            int cur = t.cfg.Mode120 ? 120 : t.cfg.CapMode ? 30 : 60, next = cur == 30 ? 60 : 30;
                            t.cfg.CapMode = next == 30; t.cfg.Mode120 = false;
                            t.AvisoModo(antes, next, null, out tit, out det); break;
                        }
                    case "a": t.SetDlss5State(!t.dlss5Shown); t.AvisoDlss5Boton(antes, out tit, out det); gen = false; d5 = true; break;
                    case "r3":
                        if (Config.WindowFor(t.cfg.VideoSize) == "3840x2160") return "";   // (a 4K: "no hay escalado que cambiar")
                        if (t.scalerPick.Length > 0) t.SetScalerChoice(true); else t.SetScalerChoice(!t.dlssScalerOn);
                        t.AvisoEscalador(antes, out tit, out det); gen = false; esc = true; break;
                    case "l3":
                        {
                            List<string> opts = t.GenOptions();
                            if (!t.fgWanted || opts.Count == 0) return "";
                            string cur = t.GenCurrent();
                            string sel = opts[(opts.IndexOf(cur) + 1) % opts.Count];
                            if (t.cfg.CapMode) t.SetPicks(t.cfg.FgPick60, sel); else t.SetPicks(sel, t.cfg.FgPick30);
                            t.AvisoTipo(antes, out tit, out det); break;
                        }
                    case "estado": { string s = t.StatusText(); int c = s.IndexOf(" · ", StringComparison.Ordinal); tit = c > 0 ? s.Substring(0, c) : s; det = c > 0 ? s.Substring(c + 3) : null; d5 = esc = true; break; }
                    case "nivel":
                        if (!t.dlss5Shown) return "";
                        t.dlss5Level = 2; t.AvisoNivel(antes, "DLSS 5 · " + Tx.T("Nivel ", "Level ") + "2" + Tx.T(" de 4", " of 4"), false, out tit, out det); gen = false; d5 = true; break;
                    case "pasadas":
                        if (!t.dlss5Shown) return "";
                        t.dlss5Passes = 3; t.AvisoNivel(antes, "DLSS 5 · " + Tx.T("3 pasadas", "3 passes"), true, out tit, out det); gen = false; d5 = true; break;
                    case "captura":
                        {
                            string nueva = t.cfg.VideoSize == "3840x2160" ? "2560x1440" : "1920x1080";
                            t.AvisoCapturaBoton(antes, nueva, out tit, out det);
                            t.cfg.VideoSize = nueva;
                            if (t.cfg.Mode120 && !t.cfg.Captura120Ok(nueva)) { t.cfg.Mode120 = false; t.cfg.CapMode = false; }
                            // (la generacion y DLSS 5, solo si cambian con la captura)
                            AvisoFoto des = t.AvisoAhora();
                            gen = des.Gen != antes.Gen; d5 = des.D5 != antes.D5;
                            esc = true; porque = gen; break;
                        }
                }
                string aviso = PillText(tit, det);
                Config fin = t.AvisoConfig();
                AvisoMotor m = AvisoOraculo(fin);
                // (la "nivel" sin DLSS 5 que vaya -no instalado-: lo dice con su por que)
                string r = AvisoCompara(aviso, m, fin, gen, d5, esc, porque);
                sb.AppendLine((r == null ? "      " : "FALLO ") + caso + " | motor: " + m + " | " + aviso + (r != null ? "   <- " + r : ""));
                return r == null ? null : caso + ": " + r + " | " + aviso + " | motor: " + m;
            }
            catch (Exception ex) { sb.AppendLine("FALLO " + caso + ": " + ex); return caso + ": " + ex.GetType().Name + " " + ex.Message; }
        }

        // Lo elegido que no puede ir (la app pone otro tipo o menos multiplicador): el aviso dice que va y por que. XeSS FG caido en la sesion,
        // Enhanced FG con la pantalla a 60 Hz, FSR 3 FG x4 caido, Smooth Motion x4 sin VRR / con DLSS 5. Con "Ver lo puesto" y la generacion
        // encendida (boton Vista desde apagada) y el L3 a ese tipo
        static int AvisosNoPuede(StringBuilder sb, List<string> fallosTxt, Config baseCfg, ref int n)
        {
            int fallos = 0;
            sb.AppendLine("== lo elegido que no puede ir");
            // (1.2.4 ronda 7, EFGFIX: x4 con DLSS 5 ya se puede -"smx4_dlss5" ya no es "no puede ir"-; lo que no puede es x4 con DLSS 5 que no llego ni
            // al 25 % en esta sesion -switch2-x4d5 no-, de Smooth Motion y de Enhanced FG)
            string[] casos = { "xess_caido", "efg_60hz", "fsrmul_caido", "smx4_sin_vrr", "smx4_dlss5_nollega", "efg_caido", "smx4_xess_caido", "efgx4_dlss5_nollega" };
            foreach (string caso in casos)
                foreach (string boton in new[] { "estado", "vista" })
                {
                    n++;
                    object[] pa = caso.StartsWith("xess") ? new object[] { "2560x1440", "30", "xess", 2, true, "dlss:M", true } :
                                  caso.StartsWith("efg") ? new object[] { "3840x2160", "30", "efg", 4, caso == "efgx4_dlss5_nollega", "dlss:auto", true } :
                                  caso.StartsWith("fsrmul") ? new object[] { "3840x2160", "30", "fsr3", 4, false, "dlss:auto", true } :
                                  new object[] { "3840x2160", "30", "smooth", 4, caso == "smx4_dlss5_nollega", "dlss:auto", true };
                    Pantalla.HzPrueba = caso == "efg_60hz" ? 60 : 120;
                    TrayApp t = AvisoApp(baseCfg, pa);
                    if (caso == "xess_caido" || caso == "smx4_xess_caido") Config.MotorGenCaidaAnota("xess");
                    if (caso == "efg_caido") Config.MotorGenCaidaAnota("efg");
                    if (caso == "fsrmul_caido") Config.MotorGenCaidaAnota("fsrmul");
                    if (caso == "smx4_dlss5_nollega") Config.SmoothX4NoLlegaAnota(true, "3840x2160");   // (EFGFIX)
                    if (caso == "efgx4_dlss5_nollega") Config.EfgNoLlegaAnota(true, "3840x2160");
                    if (caso == "smx4_sin_vrr") t.cfg.EngineVrr = 0;
                    string tit, det;
                    if (boton == "vista")
                    {
                        t.fgWanted = false; AvisoFoto antes = t.AvisoAhora(); t.fgWanted = true;
                        t.AvisoVista(antes, out tit, out det);
                    }
                    else { string s = t.StatusText(); int c = s.IndexOf(" · ", StringComparison.Ordinal); tit = s.Substring(0, c); det = s.Substring(c + 3); }
                    string aviso = PillText(tit, det);
                    Config fin = t.AvisoConfig();
                    AvisoMotor m = AvisoOraculo(fin);
                    string r = AvisoCompara(aviso, m, fin, true, boton == "estado", boton == "estado", true);
                    int pm; string pedida = fin.GenPedida(out pm);
                    if (r == null && !(aviso.Contains(" no: ") || aviso.Contains(" unavailable: "))) r = "lo elegido (" + pedida + " x" + pm + ") no va y no dice por que";
                    if (r != null) { fallos++; fallosTxt.Add(caso + " " + boton + ": " + r + " | " + aviso); }
                    sb.AppendLine((r == null ? "      " : "FALLO ") + caso + " " + boton + " | motor: " + m + " | " + aviso + (r != null ? "   <- " + r : ""));
                    Config.MotorGenCaidaOlvida();
                    Config.X4NoLlegaOlvida();
                }
            Pantalla.HzPrueba = 120;
            return fallos;
        }

        // (1.3.1, FPS) Los avisos de la escalera de fps (FpsBajo.cs): lo bajado que ya cabe ("Enhanced FG x4 · DLSS 5 al 25 % y DLSS K para que
        // quepa") y el rojo cuando ya no queda nada en el motor ("No se llega a 120 fps (99) ni con DLSS 5 al 25 % y DLSS E · Smooth Motion x2 esta
        // sesión"), en los dos idiomas: sin palabras viejas, sin separadores de mas y que quepan en la pastilla a 4K
        static int AvisosEscalera(StringBuilder sb, List<string> fallosTxt, ref int n)
        {
            int fallos = 0;
            sb.AppendLine("== la escalera de fps (lo bajado por el motor; el multiplicador esta sesion)");
            string[] quienes = { "Smooth Motion x4", "Smooth Motion x2", "Enhanced FG x4", "Enhanced FG x3", "Enhanced FG x2", "XeSS FG", "FSR 3 FG x4", "30 fps", "60 fps", "120 fps" };
            int[] pcts = { -1, 25, 35, 100 };
            string[] srs = { "", "L", "K", "E" };
            foreach (bool en in new[] { false, true })
            {
                Idioma.Poner(en ? "en" : "es");
                List<string> textos = new List<string>();
                foreach (string q in quienes)
                    foreach (int p in pcts)
                        foreach (string sr in srs)
                        {
                            string t = TrayApp.EscaleraTexto(q, p, sr);
                            if (t == null) { if (p > 0 || sr.Length > 0) textos.Add("(vacio con algo bajado: " + q + " " + p + " " + sr + ")"); continue; }
                            textos.Add(t);
                        }
                foreach (int salida in new[] { 30, 60, 120 })
                    foreach (int p in pcts)
                        foreach (string sr in srs)
                            foreach (string hecho in new[] { "Smooth Motion x2", "Enhanced FG x2", null, "DLSS 5 apagado|DLSS 5 off", "escalador FSR 1|FSR 1 upscaler", "captura a 1080p|capture at 1080p" })
                            {
                                string falta = Tx.T("No se llega a " + salida + " fps (" + (salida - 21) + ")", salida + " fps not reached (" + (salida - 21) + ")") + TrayApp.EscaleraNiCon(p, sr);
                                bool sesion = hecho != null && hecho.EndsWith("x2", StringComparison.Ordinal);
                                string h = hecho, he = hecho;
                                if (hecho != null && hecho.Contains("|")) { h = hecho.Split('|')[0]; he = hecho.Split('|')[1]; }
                                textos.Add(TrayApp.FpsBajaTexto(falta, h, he, sesion));
                            }
                foreach (string t in textos)
                {
                    n++;
                    string r = null;
                    if (t.StartsWith("(vacio", StringComparison.Ordinal)) r = "aviso vacio";
                    else
                    {
                        foreach (string w in AvisoPalabrasViejas) if (t.IndexOf(w, StringComparison.Ordinal) >= 0) { r = "dice \"" + w + "\""; break; }
                        if (r == null && (t.Contains(" ·  · ") || t.Contains(" · · ") || t.EndsWith(" · ") || t.StartsWith(" · ") || t.EndsWith(" ·") || t.Contains("  "))) r = "separadores / espacios de mas";
                        if (r == null && en && (t.Contains(" al ") || t.Contains("sesión") || t.Contains("quepa"))) r = "castellano en el ingles";
                        if (r == null && !en && (t.Contains(" at ") || t.Contains("session") || t.Contains("fits"))) r = "ingles en el castellano";
                        if (r == null) { float ancho = AvisoAncho(t, 2f); if (ancho > AvisoMaxW) r = "no cabe en la pastilla a 4K (" + (int)ancho + " de " + AvisoMaxW + " px)"; }
                    }
                    if (r != null) { fallos++; fallosTxt.Add("escalera: " + r + " | " + t); }
                    sb.AppendLine((r == null ? "      " : "FALLO ") + "escalera | " + t + (r != null ? "   <- " + r : ""));
                }
            }
            Idioma.Poner("es");
            return fallos;
        }

        // La sesion del usuario del 23-11 (12:27-12:36, perfil "Consola": 4K, modo 30, DLSS 5 nivel 2, DLSS M; y la de GTA VI de las 12:05-12:16) paso
        // a paso por el menu: lo que salia (StatusTextAntes, el codigo de la 1.2.4 ronda 6) y lo que sale ahora, contra lo que va. Y las pastillas
        // nuevas pintadas a 4K en <informe>.png (sin ventanas: un mapa de bits), para verlas
        static int AvisosSesion(StringBuilder sb, List<string> fallosTxt, Config baseCfg, string informe, ref int n)
        {
            int fallos = 0;
            sb.AppendLine("== la sesion del usuario del 23-11, paso a paso (antes -> ahora)");
            List<string> pastillas = new List<string>();
            // paso: hora, lo que se toco en el menu ("fila=valor ...")
            string[][] sesiones = {
                new[] { "Consola 4K modo 30 Smooth Motion x2 DLSS 5 nivel 2", "3840x2160", "30", "smooth", "2", "1", "dlss:M", "1", "2",
                        "12:30:04|d5=0", "12:30:08|smx4=4", "12:31:20|smx4=2", "12:32:02|tipo=fsr3 fsrmul=2", "12:32:47|fsrmul=4", "12:33:15|tipo=xess",
                        "12:34:04|tipo=efg efgmul=2 efglat=1", "12:34:53|efgmul=4 efglat=0", "12:35:32|fg=0" },
                new[] { "GTA VI 4K modo 30 Enhanced FG x2, DLSS 5 no, R3 (FSR 1)", "3840x2160", "30", "efg", "2", "0", "alt", "1", "4",
                        "12:05:54|tipo=smooth smx4=4 d5=1", "12:06:32|tipo=efg efgmul=4", "12:07:35|tipo=fsr3", "12:07:51|tipo=smooth", "12:09:52|cap=2560x1440 esc=dlss:M",
                        "12:10:24|tipo=xess", "12:11:29|tipo=efg", "12:13:50|tipo=fsr3", "12:14:17|fsrmul=4", "12:15:16|cap=3840x2160", "12:15:29|cap=2560x1440 d5=0 esc=pick:ninguno",
                        "12:15:43|cap=3840x2160 d5=1" },
            };
            foreach (string[] se in sesiones)
            {
                sb.AppendLine("-- " + se[0]);
                object[] pa = { se[1], se[2], se[3], int.Parse(se[4]), se[5] == "1", se[6], se[7] == "1" };
                TrayApp t = AvisoApp(baseCfg, pa);
                t.curGame = se[0].StartsWith("GTA") ? "PS5: Grand Theft Auto VI" : "Consola";
                t.dlss5Level = int.Parse(se[8]);
                for (int i = 9; i < se.Length; i++)
                {
                    n++;
                    string[] hp = se[i].Split('|');
                    t.AjustesModelo();
                    string r = null;
                    foreach (string kv in hp[1].Split(' '))
                    {
                        string[] x = kv.Split('=');
                        int pr = t.AjPruebaPon(x[0], x[1]);
                        // (1.2.4 ronda 7, INTEGRA7: FSR 3 FG fuera del menu -FGREAL, generacion_fsr3=0 de serie-: que no se pueda elegir es lo bueno)
                        if (pr != 0 && !Config.FsrFgOfrecer && (kv == "tipo=fsr3" || kv.StartsWith("fsrmul=", StringComparison.Ordinal))) { r = ""; break; }
                        // (INTEGRA7: con DLSS 5 elegido, Enhanced FG x3 / x4 con candado -efg_multi_dlss5=0-: lo bueno es que no se pueda; el resto del paso sigue)
                        FilaAjuste fd5 = t.AjFila("d5");
                        if (pr != 0 && !Config.EfgMultiDlss5 && kv.StartsWith("efgmul=", StringComparison.Ordinal) && kv != "efgmul=2" && fd5 != null && fd5.Val == "1")
                        { sb.AppendLine("      " + hp[0] + ": " + kv + " con DLSS 5 tiene candado (Enhanced FG x2 con DLSS 5): bien"); continue; }
                        if (pr != 0) { r = "el menu no deja poner " + kv + " (" + pr + ")"; break; }
                    }
                    string aviso = "", antes = "";
                    if (r == "") { sb.AppendLine("      " + hp[0] + ": " + hp[1] + " no sale en el menu (FSR 3 FG quitada): bien"); continue; }
                    if (r == null)
                    {
                        Dictionary<string, string> e; HashSet<string> cambiadas; List<string> que;
                        t.AjRecoge(out e, out cambiadas, out que);
                        antes = "Ajustes aplicados · " + t.StatusTextAntes(e);
                        string tit, det;
                        t.AjAvisoDe(e, cambiadas, true, out tit, out det);
                        aviso = PillText(tit, det);
                        Config fin = t.AvisoConfig();
                        AvisoMotor m = AvisoOraculo(fin);
                        bool genToca = cambiadas.Contains("modo") || cambiadas.Contains("fg") || cambiadas.Contains("tipo") || cambiadas.Contains("smx4") ||
                                       cambiadas.Contains("fsrmul") || cambiadas.Contains("efgmul") || cambiadas.Contains("efglat");
                        r = AvisoCompara(aviso, m, fin, genToca, cambiadas.Contains("d5"), cambiadas.Contains("esc") || cambiadas.Contains("cap"), genToca);
                        sb.AppendLine((r == null ? "      " : "FALLO ") + hp[0] + " menu: " + string.Join(", ", que.ToArray()) + " | motor: " + m);
                        sb.AppendLine("         antes: " + antes);
                        sb.AppendLine("         ahora: " + aviso + (r != null ? "   <- " + r : ""));
                        pastillas.Add(hp[0] + "|" + aviso);
                    }
                    else sb.AppendLine("FALLO " + hp[0] + ": " + r);
                    if (r != null) { fallos++; fallosTxt.Add("sesion " + hp[0] + ": " + r + " | " + aviso); }
                }
            }
            // las pastillas nuevas, a 4K (las del motor: misma letra y medidas, PrismaUi)
            try
            {
                if (!string.IsNullOrEmpty(informe))
                {
                    float u = 2f;
                    int alto = (int)(40 * u + pastillas.Count * 92 * u);
                    using (Bitmap bmp = new Bitmap(3840, alto, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(bmp))
                    using (Font fh = new Font("Segoe UI", 16 * u, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        g.Clear(Color.FromArgb(255, 40, 52, 44));
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        float y = 20 * u;
                        foreach (string ps in pastillas)
                        {
                            string[] q = ps.Split(new[] { '|' }, 2);
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 255, 255, 255))) g.DrawString(q[0], fh, b, 20 * u, y + 20 * u);
                            PrismaUi.Pill p = PrismaUi.MeasurePill(g, q[1], u);
                            PrismaUi.DrawPill(g, p, new RectangleF((3840 - p.W) / 2, y, p.W, p.H), PrismaUi.Accent, 1f);
                            y += 92 * u;
                        }
                        // el ancho de la pastilla del motor de antes (2400) y el de ahora (3600)
                        using (Pen pv = new Pen(Color.FromArgb(160, 255, 80, 80), 2)) { g.DrawLine(pv, (3840 - 2400) / 2, 0, (3840 - 2400) / 2, alto); g.DrawLine(pv, (3840 + 2400) / 2, 0, (3840 + 2400) / 2, alto); }
                        bmp.Save(Path.ChangeExtension(informe, ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("(sin la imagen de las pastillas: " + ex.Message + ")"); }
            return fallos;
        }

        // (solo --test-avisos) lo que decia el aviso del "Aplicar" hasta la 1.2.4 ronda 6 (StatusText de entonces), con lo aplicado puesto en
        // una copia: para enseñar antes / ahora en el informe
        string StatusTextAntes(Dictionary<string, string> e)
        {
            Config c = AvisoConfig();
            string v;
            bool fgW = fgWanted, d5S = dlss5Shown;
            if ((v = AjVal(e, "fg")) != null) fgW = v == "1";
            if ((v = AjVal(e, "d5")) != null) d5S = v == "1";
            if ((v = AjVal(e, "cap")) != null) c.VideoSize = v;
            if ((v = AjVal(e, "tipo")) != null) { if (c.CapMode) c.FgPick30 = v; else c.FgPick60 = v; }
            if ((v = AjVal(e, "smx4")) != null) c.SmoothX4 = v == "4";
            if ((v = AjVal(e, "esc")) != null) { if (v.StartsWith("pick:")) { c.ScalerPick = v.Substring(5); c.AltScalerOn = false; } else { c.ScalerPick = ""; c.AltScalerOn = v == "alt"; } }
            c.FgOff = !fgW; c.Dlss5Off = !d5S;
            int fps = c.CapMode ? 30 * (fgW ? 2 : 1) : 60 * (fgW ? 2 : 1);
            return (curGame ?? "Juego") + " · " + SizeName(c.VideoSize) + " · " +
                   (fgW ? (c.CapMode ? "30" : "60") + " → " + fps + " fps (" + (c.EngineSmoothMode > 0 ? "Smooth Motion" : "DLSS-G + Reflex") + ")" : fps + " fps sin generación · Reflex") +
                   " · motor propio · " + (c.ScalerNow == "dlss" ? "DLSS " + Config.EngineModelName(c.EnginePresetNow) : "Catmull-Rom, sin DLSS") +
                   (!d5S ? " · DLSS 5 desactivado" : " · DLSS 5 no cabe en este modo");
        }

        // El aviso una sola vez: sin relevo, ya y entero; con un relevo que llegara despues (otra identidad del motor), ahora "preparando" y el
        // entero guardado para el motor nuevo (ReabrirMotorSmooth lo coge)
        static int AvisosUnaVez(StringBuilder sb, List<string> fallosTxt, Config baseCfg, ref int n)
        {
            int fallos = 0;
            sb.AppendLine("== el aviso una sola vez (AvisoFinal)");
            Process yo = Process.GetCurrentProcess();
            foreach (bool relevoDespues in new[] { false, true })
            {
                n++;
                TrayApp t = AvisoApp(baseCfg, AvisoPartidas[0]);   // (Smooth Motion x2: cadena 0)
                t.mpv = yo;
                Config.EngineFfxLaunched = 0; Config.EngineFixedLaunched = 0;
                Config.NoteEngineFfx(yo); Config.NoteEngineFixed(yo);
                Config.EngineSmoothRunning = t.cfg.EngineSmoothMode;
                if (relevoDespues) { t.cfg.FgPick30 = "efg"; t.PrepareCfg(); }   // (Enhanced FG: cadena 50 -> otro motor)
                lock (t.osdQueue) t.osdQueue.Clear();
                t.lastPadAction = DateTime.Now;   // (lo ha hecho el usuario: el menu)
                t.AvisoFinal(AjTitulo, "X");
                string enCola;
                lock (t.osdQueue) enCola = string.Join(" | ", t.osdQueue.ToArray());
                string tt, ss;
                bool guardado = t.AvisoToma(out tt, out ss);
                string r = null;
                if (relevoDespues)
                {
                    if (!guardado || ss != "X") r = "con relevo despues no se guarda el aviso para el motor nuevo";
                    else if (enCola.Contains("· X") || !enCola.Contains(Idioma.T(Tx.T("preparando…", "getting ready…")))) r = "con relevo despues sale entero ya (o sin \"preparando\"): " + enCola;
                }
                else
                {
                    if (guardado) r = "sin relevo se guarda para otro motor";
                    else if (!enCola.Contains(AjTitulo) || !enCola.Contains("X")) r = "sin relevo no sale entero: " + enCola;
                }
                if (r != null) { fallos++; fallosTxt.Add("AvisoFinal " + (relevoDespues ? "con" : "sin") + " relevo: " + r); }
                sb.AppendLine((r == null ? "      " : "FALLO ") + "AvisoFinal " + (relevoDespues ? "con relevo despues" : "sin relevo") + ": cola " + enCola + (guardado ? " (guardado: " + tt + " · " + ss + ")" : "") + (r != null ? "   <- " + r : ""));
            }
            Config.EngineSmoothRunning = -1;
            return fallos;
        }
    }
}
