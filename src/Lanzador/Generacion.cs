using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace ConsoleEnhancer
{
    // 19-11 (GENERACION, el usuario: "apretando el joystick izquierdo da opcion a cambiar el tipo de frame generation y optimiza cada preset
    // con esos frame generations"). L3 jugando (la imagen de la consola delante) = selector del tipo de generacion de fotogramas:
    //   - la primera pulsacion enseña la lista del modo de ahora (30 o 60) con lo que va puesto; cada pulsacion mas, el siguiente;
    //   - 1,4 s sin pulsar y se pone el elegido (una sola reapertura, sin telon si se puede: el puente del motor o el relevo).
    // Teclado: Ctrl+G sobre la imagen de la consola (como L3). Mantener L3 ~0,8 s = el filtro de tramado nuevo / el de antes (lo que
    // hacia L3 hasta hoy). Lo elegido va por modo y por juego (columna 10 de games\profiles.txt) y en games\framegen_type.txt (lo de
    // ahora, para abrir la app ya jugando). Sin elegir nada, lo del ini (generacion_tipo_60/30, motor_generacion_60/30) como antes.
    // Tipos: Sin generacion | Smooth Motion (driver) | DLSS FG (Magpie con DLSS 5; DLSS-G del motor propio sin el) | XeSS FG | FSR 3 FG
    // (estos dos solo Magpie: con DLSS 5 apagado va mpv + Magpie sin DLSS 5). Los de cada modo: ini generacion_lista_60 / _30 (los que
    // funcionan en este PC, medidos: ver LEEME). Los ajustes de cada tipo: claves con el tipo (Config.ModeKeyGen: tabla_dlss5_30_xess...)
    partial class TrayApp
    {
        readonly object genSync = new object();
        string genSel;                              // lo marcado en el selector abierto (null = cerrado)
        DateTime genLastPress = DateTime.MinValue;
        System.Threading.Timer genTimer;
        int genSeq;
        const int GenOpenMs = 3200, GenApplyMs = 2000; // (19-11 noche: 2 s para seguir pulsando antes de aplicar)
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        public static string GenLabel(string t, bool engine)
        {
            switch (t)
            {
                case "no": return "Sin generación";
                case "smooth": return "Smooth Motion";
                case "fsr3": return "FSR 3 FG";
                case "xess": return "XeSS FG";
                case "vfg": return "NVIDIA VFG";
                default: return "DLSS FG";   // (21-11, 1.0.8: igual en el motor y en Magpie; antes "DLSS-G" en el motor)
            }
        }

        // Los tipos del modo de ahora que funcionan aqui ("no" el primero)
        List<string> GenOptions() { return GenOptionsFor(cfg.CapMode, cfg.VideoSize); }

        // (1.0.11, menu de ajustes del boton central: los tipos de OTRO modo / captura, la elegida en el menu)
        List<string> GenOptionsFor(bool cap30, string videoSize)
        {
            PrepareCfg();
            // 21-11 (1.0.8, el usuario: "al apretar el joystick izquierdo para cambiar de frame gen no hagas que se pueda desactivar"): sin
            // "Sin generacion" en la lista (se apaga con el boton de captura, como antes)
            List<string> r = new List<string>();
            foreach (string t in cap30 ? cfg.GenList30 : cfg.GenList60)
            {
                if (r.Contains(t)) continue;
                if (t == "smooth" && (NvProfiles.SmoothBroken || Pantalla.Is119)) continue; // (ahi iria DLSS FG: repetido)
                // (ConsoleEnhancer: la lista sale del plan de la grafica, generacion_lista_60/30; por si el ini es de antes, sin NVIDIA
                // nada de NVIDIA, y con generacion_disponible=0 nada)
                if (!cfg.FgAvailable || ((t == "smooth" || t == "dlss") && Ce.NoNvidia)) continue;
                if (t == "dlss" && cfg.DlssFgPorMagpieSinRuntime) continue; // (1.0.10: RTX 30 sin el runtime; 1.1.2: con DLSS 5 por Magpie, sin el runtime)
                if (t == "fsr3" && !File.Exists(Path.Combine(Paths.MagpieDir, "amd_fidelityfx_framegeneration_dx12.dll"))) continue;
                if (t == "xess" && (!File.Exists(Path.Combine(Paths.MagpieDir, "libxess_fg.dll")) || Config.XessNo1080(videoSize))) continue; // (19-11: a 1080p no arrancaba; 1.2.2: xess_fg_1080, Multiplicador.cs)
                r.Add(t);
            }
            return r;
        }

        // Lo que va puesto ahora en este modo
        string GenCurrent()
        {
            PrepareCfg();
            if (!fgWanted) return "no";
            if (cfg.EngineNow && cfg.EngineFfxWanted) return "fsr3";   // 21-11 (1.0.9): FSR 3 FG en el motor
            if (cfg.EngineNow) return cfg.EngineSmoothFor(cfg.CapMode) && !NvProfiles.EngineSmoothBroken ? "smooth" : "dlss";
            return cfg.FgTypeNow;
        }

        string GenListText(List<string> opts, string sel, bool engine)
        {
            List<string> l = new List<string>();
            // (1.0.8: sin ▸ ◂, que la fuente del aviso del motor no tiene y salian como cuadros; « » si las tiene)
            foreach (string t in opts) l.Add(t == sel ? "«" + GenLabel(t, engine) + GenMultSufijo(t) + "»" : GenLabel(t, engine) + GenMultSufijo(t));
            return string.Join("  ", l.ToArray());
        }

        // (1.2.2, MULTI) " x4" detras de DLSS FG / XeSS FG en el selector si en este modo van con x3 / x4 (con x2, como siempre)
        string GenMultSufijo(string t)
        {
            try { int m = Config.MultTipo(t) ? cfg.MultAhora(cfg.CapMode, t) : 2; return m > 2 ? " x" + m : ""; }
            catch { return ""; }
        }

        // L3 (al soltarlo) o Ctrl+G
        void GenSelectorPress()
        {
            if (InHome || homeBusy)
            {
                if (!applying) Osd(MenuTexto + (cfg.HomeNative ? " en 4K nativo" : "") + " · la generación se elige dentro de cada juego");
                return;
            }
            lastPadAction = DateTime.Now;
            lock (genSync)
            {
                List<string> opts = GenOptions();
                string cur = GenCurrent();
                if (opts.Count == 0) { Osd("Generación · no hay ninguna disponible en este PC"); return; }
                bool open = genSel != null && (DateTime.Now - genLastPress).TotalMilliseconds < GenOpenMs;
                // 19-11 noche (el usuario: "cuando pulso l3 sigue en smooth motion"): la 1a pulsacion ya pasa al siguiente (antes solo enseñaba la lista y, sin una 2a pulsacion en 1,4 s, dejaba lo puesto)
                if (!open) { int c0 = opts.IndexOf(cur); genSel = opts[(c0 + 1) % opts.Count]; }
                else
                {
                    int i = opts.IndexOf(genSel);
                    genSel = opts[(i + 1) % opts.Count];
                }
                genLastPress = DateTime.Now;
                bool engine = cfg.EngineNow && genSel != "fsr3" && genSel != "xess" && genSel != "vfg";
                // (1.0.8: el titulo es lo de antes del primer " · "; antes salia "Generación" solo y el tipo dos veces detras)
                string head = "Generación: " + GenLabel(genSel, engine) + GenMultSufijo(genSel) + (genSel == cur ? " (actual)" : "");
                string list = (opts.Count > 1 ? GenListText(opts, genSel, engine) + " · L3: cambiar" : "la única disponible en este PC");
                lock (osdQueue) osdQueue.Add(OsdCmd(head + " · " + list, GenOpenMs.ToString(CultureInfo.InvariantCulture), false));
                int seq = ++genSeq;
                if (genTimer == null) genTimer = new System.Threading.Timer(delegate { GenTimerFired(); }, null, Timeout.Infinite, Timeout.Infinite);
                genTimer.Change(GenApplyMs, Timeout.Infinite);
            }
        }

        void GenTimerFired()
        {
            string sel;
            lock (genSync)
            {
                if (genSel == null || (DateTime.Now - genLastPress).TotalMilliseconds < GenApplyMs - 50) return; // (otra pulsacion entre medias)
                sel = genSel;
                genSel = null;
            }
            try { GenApply(sel, 0); }
            catch (Exception ex) { Log.Error("Selector de generacion", ex); }
        }

        // Pone el tipo t en el modo de ahora (reabre lo que haga falta, como el boton de captura). Con algo cambiando, espera (hasta ~10 s)
        void GenApply(string t, int tries)
        {
            if (InHome || homeBusy || userExit) return;
            System.Diagnostics.Process p = mpv;
            if (p == null || p.HasExited) return;
            if (passesBusy || relaunchSize != null || !lsReady || relaunchEngine || Interlocked.CompareExchange(ref fgBusy, 1, 0) != 0)
            {
                if (tries >= 40) { Log.Write("Selector de generacion: " + t + " sin poner (la imagen seguia cambiando)"); return; }
                Thread.Sleep(250);
                GenApply(t, tries + 1);
                return;
            }
            try
            {
                string cur = GenCurrent();
                if (t == cur) { Log.Write("Selector de generacion: " + t + " ya puesto (" + (cfg.CapMode ? "modo 30" : "modo 60") + ")"); return; }
                lastPadAction = DateTime.Now; // (el telon / la reapertura llevan el aviso, como con un boton)
                if (t == "no")
                {
                    fgWanted = false;
                    SaveText(Paths.FgFile, "0");
                }
                else
                {
                    fgWanted = true;
                    SaveText(Paths.FgFile, "1");
                    if (cfg.CapMode) SetPicks(cfg.FgPick60, t); else SetPicks(t, cfg.FgPick30);
                }
                PrepareCfg();
                bool engine = cfg.EngineNow;
                Log.Write("Mando: generacion " + GenLabel(t, engine) + " en el modo " + (cfg.CapMode ? "30" : "60") + " (antes " + GenLabel(cur, Config.EngineActive) + ")" +
                          (engine != Config.EngineActive ? (engine ? ": al motor propio" : ": a mpv + Magpie (" + (cfg.Dlss5Off ? "sin DLSS 5: " : "") + "esta generacion la hace Magpie)") : ReopenNote) +
                          " · " + cfg.ProfileText);
                SaveCurProfile();
                string title = t == "no" ? "Generación de fotogramas desactivada" : "Generación: " + GenLabel(t, engine) + GenMultSufijo(t);
                string sub = t == "no" ? "Solo las imágenes reales de la consola" : GenDetail(t, engine);
                RestartLs(title, sub, false, true);
            }
            finally { Interlocked.Exchange(ref fgBusy, 0); }
        }

        // La linea de detalle del aviso: x2 y lo de DLSS 5 que cabe con ella
        string GenDetail(string t, bool engine)
        {
            if (cfg.Mode120) return "120 fps (esports) · sin generacion · baja latencia"; // 20-11 (HZ120): a 120 no hay generacion
            string fps = (cfg.CapMode ? "30" : "60") + " → " + ((cfg.CapMode ? 30 : 60) * cfg.FgMultNow) + " fps";
            if (engine) return fps + " · motor propio" + (t == "dlss" ? " con Reflex" : "");
            if (cfg.Dlss5Off) return fps + " · sin DLSS 5";
            return fps + " · DLSS 5 " + Dlss5PassText();
        }

        // Lo elegido con L3 en cada modo ("" = lo del ini), en la config y en games\framegen_type.txt
        void SetPicks(string p60, string p30)
        {
            cfg.FgPick60 = Config.ParsePick(p60);
            cfg.FgPick30 = Config.ParsePick(p30);
            SaveText(Paths.FgPickFile, "60=" + cfg.FgPick60 + "\r\n30=" + cfg.FgPick30 + "\r\n");
        }

        void LoadPicks()
        {
            MultCargar(); // (1.2.2, MULTI: el multiplicador de cada modo, games\framegen_mult.txt)
            string s = LoadText(Paths.FgPickFile);
            if (string.IsNullOrEmpty(s)) return;
            foreach (string raw in s.Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("60=")) cfg.FgPick60 = Config.ParsePick(l.Substring(3));
                else if (l.StartsWith("30=")) cfg.FgPick30 = Config.ParsePick(l.Substring(3));
            }
            if (cfg.FgPick60.Length > 0 || cfg.FgPick30.Length > 0)
                Log.Write("Generacion elegida con L3: modo 60 " + (cfg.FgPick60.Length > 0 ? cfg.FgPick60 : "la del ini") + ", modo 30 " + (cfg.FgPick30.Length > 0 ? cfg.FgPick30 : "la del ini"));
        }
    }
}
