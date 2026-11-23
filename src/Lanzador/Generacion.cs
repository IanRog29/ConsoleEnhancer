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
    // Tipos (1.2.4 ronda 6, QUITAFG: sin DLSS FG ni NVIDIA VFG ni x3 / x4): Sin generacion | Smooth Motion (driver; RTX 40 / 50) | XeSS FG |
    // FSR 3 FG, siempre x2 (src\Lanzador\TiposGeneracion.cs). Los de cada modo: ini generacion_lista_60 / _30 (los que tiene este PC: el plan).
    // Los ajustes de cada tipo: claves con el tipo (Config.ModeKeyGen: tabla_dlss5_30_xess...)
    partial class TrayApp
    {
        readonly object genSync = new object();
        string genSel;                              // lo marcado en el selector abierto (null = cerrado)
        DateTime genLastPress = DateTime.MinValue;
        System.Threading.Timer genTimer;
        int genSeq;
        const int GenOpenMs = 3200, GenApplyMs = 2000; // (19-11 noche: 2 s para seguir pulsando antes de aplicar)
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        // El nombre del tipo (selector, menu de ajustes, bandeja). (1.2.4 ronda 7, TEXTOS: sin multiplicador: Enhanced FG decia "Enhanced FG x4 ·
        // baja latencia" -lo guardado- en la fila del tipo con la fila "Multiplicador" en x2 al lado, y el " · " partia el titulo del aviso de
        // L3. Lo que va de verdad, con su multiplicador: Config.AvisoGen, Avisos.cs)
        // (1.2.4 ronda 7, FGREAL: Enhanced FG y FSR 3 FG, opciones experimentales avisadas: en el menu, el selector y la bandeja con
        // " (experimental)"; en los avisos de lo que va, el nombre solo -Config.GenNombre-)
        public static string GenLabel(string t, bool engine) { return Config.GenNombre(t) + (t == "efg" || t == "fsr3" ? " (experimental)" : ""); }

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
                if (r.Contains(t) || !Config.TipoGen(t)) continue;
                // (ConsoleEnhancer: la lista sale del plan de la grafica, generacion_lista_60/30; con generacion_disponible=0 nada. Smooth
                // Motion solo con NVIDIA, sin la tele a 119,88 Hz y si el driver deja; XeSS FG / FSR 3 FG con sus DLL; XeSS FG no a 1080p con
                // xess_fg_1080=0: Config.GenPuedeIr, TiposGeneracion.cs)
                if (!cfg.FgAvailable || !Config.GenPuedeIr(t, videoSize)) continue;
                r.Add(t);
            }
            // (1.2.4 ronda 6, ENHANCED) Enhanced FG, la generacion propia del motor: opcion EXTRA, al final y en cualquier grafica (EnhancedFg.cs)
            if (cfg.FgAvailable && Config.GenPuedeIr("efg", videoSize) && !r.Contains("efg")) r.Add("efg");
            return r;
        }

        // Lo que va puesto ahora en este modo
        string GenCurrent()
        {
            PrepareCfg();
            if (!fgWanted) return "no";
            // (1.2.4 ronda 6, QUITAFG: el motor y Magpie hacen el mismo tipo -Config.FgTypeFor-: Smooth Motion, XeSS FG o FSR 3 FG)
            return cfg.FgTypeNow;
        }

        string GenListText(List<string> opts, string sel, bool engine)
        {
            List<string> l = new List<string>();
            // (1.0.8: sin ▸ ◂, que la fuente del aviso del motor no tiene y salian como cuadros; « » si las tiene)
            foreach (string t in opts) l.Add(t == sel ? "«" + GenLabel(t, engine) + "»" : GenLabel(t, engine));
            return string.Join("  ", l.ToArray());
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
                if (opts.Count == 0) { Osd(Deteccion.Tx.T("Generación · no hay ninguna disponible en este PC", "Frame generation · none available on this PC")); return; }
                bool open = genSel != null && (DateTime.Now - genLastPress).TotalMilliseconds < GenOpenMs;
                // 19-11 noche (el usuario: "cuando pulso l3 sigue en smooth motion"): la 1a pulsacion ya pasa al siguiente (antes solo enseñaba la lista y, sin una 2a pulsacion en 1,4 s, dejaba lo puesto)
                if (!open) { int c0 = opts.IndexOf(cur); genSel = opts[(c0 + 1) % opts.Count]; }
                else
                {
                    int i = opts.IndexOf(genSel);
                    genSel = opts[(i + 1) % opts.Count];
                }
                genLastPress = DateTime.Now;
                bool engine = cfg.EngineNow;
                // (1.0.8: el titulo es lo de antes del primer " · "; antes salia "Generación" solo y el tipo dos veces detras)
                string head = Deteccion.Tx.T("Generación: ", "Frame generation: ") + GenLabel(genSel, engine) + (genSel == cur ? Deteccion.Tx.T(" (la de ahora)", " (current)") : "");
                string list = (opts.Count > 1 ? GenListText(opts, genSel, engine) + Deteccion.Tx.T(" · L3: cambiar", " · L3: change") : Deteccion.Tx.T("la única disponible en este PC", "the only one on this PC"));
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
                if (t == cur)
                {
                    Log.Write("Selector de generacion: " + t + " ya puesto (" + (cfg.CapMode ? "modo 30" : "modo 60") + ")");
                    Osd(Deteccion.Tx.T("Generación", "Frame generation") + " · " + AvisoConfig().AvisoGen(true) + Deteccion.Tx.T(" · ya puesta", " · already on"));
                    return;
                }
                AvisoFoto antes = AvisoAhora();
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
                          (engine != Config.EngineActive ? ": al motor propio" : ReopenNote) +
                          " · " + cfg.ProfileText);
                SaveCurProfile();
                // (1.2.4 ronda 7, TEXTOS: lo que va de verdad -tipo, multiplicador, "30 → 120 fps"; si lo elegido no puede ir, por que y que va-;
                // antes "Generación: <lo elegido>" y "30 → 60 fps · motor propio" con el x2 de antes. Avisos.cs)
                string title, sub;
                if (t == "no") AvisoVista(antes, out title, out sub);
                else AvisoTipo(antes, out title, out sub);
                RestartLs(title, sub, false, true);
            }
            finally { Interlocked.Exchange(ref fgBusy, 0); }
        }

        // La linea de detalle del aviso de la generacion. (1.2.4 ronda 7, TEXTOS: lo que va de verdad, Config.AvisoGen; t ya no cuenta: el
        // aviso es de lo que se pone, no de lo pedido)
        string GenDetail(string t, bool engine) { return AvisoConfig().AvisoGen(true); }

        // Lo elegido con L3 en cada modo ("" = lo del ini), en la config y en games\framegen_type.txt
        void SetPicks(string p60, string p30)
        {
            cfg.FgPick60 = Config.ParsePick(p60);
            cfg.FgPick30 = Config.ParsePick(p30);
            SaveText(Paths.FgPickFile, "60=" + cfg.FgPick60 + "\r\n30=" + cfg.FgPick30 + "\r\n");
        }

        void LoadPicks()
        {
            // (1.2.2-1.2.4: tambien el multiplicador de cada modo, games\framegen_mult.txt: 1.2.4 ronda 6, QUITAFG, ya no se lee: sin x3 / x4)
            string s = LoadText(Paths.FgPickFile);
            if (string.IsNullOrEmpty(s)) return;
            bool vieja = false;
            foreach (string raw in s.Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("60=")) cfg.FgPick60 = Config.ParsePick(l.Substring(3));
                else if (l.StartsWith("30=")) cfg.FgPick30 = Config.ParsePick(l.Substring(3));
                else continue;
                string v = l.Substring(3).Trim().ToLowerInvariant();
                if (v == "dlss" || v == "vfg") vieja = true;
            }
            // (1.2.4 ronda 6, QUITAFG: lo elegido con DLSS FG / NVIDIA VFG pasa al mas parecido de este PC -Config.GenEnVezDeDlss- y se guarda asi)
            if (vieja)
            {
                Log.Write("Generacion elegida: DLSS FG ya no existe, pasa a " + (Config.GenEnVezDeDlss.Length > 0 ? Config.GenEnVezDeDlss : "la de serie") + " (" + Paths.FgPickFile + ")");
                SetPicks(cfg.FgPick60, cfg.FgPick30);
            }
            if (cfg.FgPick60.Length > 0 || cfg.FgPick30.Length > 0)
                Log.Write("Generacion elegida con L3: modo 60 " + (cfg.FgPick60.Length > 0 ? cfg.FgPick60 : "la del ini") + ", modo 30 " + (cfg.FgPick30.Length > 0 ? cfg.FgPick30 : "la del ini"));
        }
    }
}
