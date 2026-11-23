// MenuJuego.cs - lo del mando tambien en la bandeja y con el teclado (21-11-2026, 1.0.10)
//
// Reddit (un usuario con teclado y raton, sin mando que se conecte al PC): "todas las opciones que se cambian con atajos deberian estar
// tambien en el menu del clic derecho del icono de la bandeja" y "no encuentro como encender DLSS FG". Lo que hacen los botones del mando
// con la imagen delante (src\BotonesCe.cs, GamepadLoop) va aqui en el menu de la bandeja, con lo puesto marcado, y con atajos de teclado
// sobre la imagen de la consola (KeyHook -> TeclaCe):
//   Ctrl+F generacion si/no · Ctrl+G tipo de generacion (como L3) · Ctrl+X multiplicador x2/x3/x4 (1.2.2, Multiplicador.cs) ·
//   Ctrl+M modo 30/60/120 · Ctrl+1..4 captura 720p/1080p/1440p/4K · Ctrl+R escalador normal/alternativo · Ctrl+D DLSS 5 si/no ·
//   Ctrl+I lo que va puesto · Alt+F4 salir (ya estaba)
// Desde la bandeja, antes de cada orden se devuelve el primer plano a la imagen (las ordenes del mando solo valen con ella delante, y asi
// el aviso sale encima y se sigue jugando sin otro clic).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class TrayApp
    {
        // (una linea en el constructor del menu, arriba del todo)
        void MenuJuego(ContextMenuStrip menu)
        {
            ToolStripMenuItem fg = new ToolStripMenuItem(Tx.T("Generación de fotogramas", "Frame generation"), null, delegate
            {
                DesdeBandeja("generacion si/no", false, delegate { ToggleFrameGeneration(); });
            });
            fg.ShortcutKeyDisplayString = "Ctrl+F";

            ToolStripMenuItem tipo = new ToolStripMenuItem(Tx.T("Tipo de generación", "Frame generation type"));
            tipo.ShortcutKeyDisplayString = "Ctrl+G";
            tipo.DropDownItems.Add(new ToolStripMenuItem("…"));   // (se rellena al abrir: depende del modo de ahora)
            tipo.DropDownOpening += delegate
            {
                tipo.DropDownItems.Clear();
                List<string> opts;
                string cur;
                try { opts = GenOptions(); cur = GenCurrent(); }
                catch (Exception ex) { Log.Error("Bandeja: tipos de generacion", ex); return; }
                if (opts.Count == 0)
                {
                    ToolStripMenuItem none = new ToolStripMenuItem(Tx.T("Ninguna disponible en este PC", "None available on this PC"));
                    none.Enabled = false;
                    tipo.DropDownItems.Add(none);
                    return;
                }
                foreach (string t in opts)
                {
                    string tt = t;
                    ToolStripMenuItem it = new ToolStripMenuItem(GenLabel(t, false), null, delegate
                    {
                        DesdeBandeja("generacion " + tt, true, delegate { GenApply(tt, 0); });
                    });
                    it.Checked = t == cur;
                    tipo.DropDownItems.Add(it);
                }
            };

            // (1.2.2-1.2.4: aqui iba "Multiplicador de generación" x2 / x3 / x4; 1.2.4 ronda 6, QUITAFG: fuera, todas x2)

            ToolStripMenuItem modo = new ToolStripMenuItem(Tx.T("Modo", "Mode"));
            modo.ShortcutKeyDisplayString = "Ctrl+M";
            ToolStripMenuItem m30 = ItemModo(30, Tx.T("30 fps", "30 fps"));
            ToolStripMenuItem m60 = ItemModo(60, Tx.T("60 fps", "60 fps"));
            ToolStripMenuItem m120 = ItemModo(120, Tx.T("120 fps (esports, sin generación)", "120 fps (esports, no frame generation)"));
            modo.DropDownItems.AddRange(new ToolStripItem[] { m30, m60, m120 });

            ToolStripMenuItem cap = new ToolStripMenuItem(Tx.T("Captura", "Capture"));
            string[] tams = { "1280x720", "1920x1080", "2560x1440", "3840x2160" };
            List<ToolStripMenuItem> caps = new List<ToolStripMenuItem>();
            for (int i = 0; i < tams.Length; i++)
            {
                string s = tams[i];
                ToolStripMenuItem it = new ToolStripMenuItem(SizeName(s).Replace("4K nativo", Tx.T("4K nativo", "native 4K")), null, delegate
                {
                    DesdeBandeja("captura " + s, true, delegate { RequestCapture(s); });
                });
                it.ShortcutKeyDisplayString = "Ctrl+" + (i + 1);
                it.Tag = s;
                caps.Add(it);
                cap.DropDownItems.Add(it);
            }

            ToolStripMenuItem d5 = new ToolStripMenuItem("DLSS 5");
            ToolStripMenuItem d5on = new ToolStripMenuItem(Tx.T("Activado", "On"), null, delegate { DesdeBandeja("DLSS 5 si/no", false, delegate { ToggleDlss5(); }); });
            d5on.ShortcutKeyDisplayString = "Ctrl+D";
            d5.DropDownItems.Add(d5on);
            d5.DropDownItems.Add(new ToolStripSeparator());
            List<ToolStripMenuItem> niveles = new List<ToolStripMenuItem>(), pasadas = new List<ToolStripMenuItem>();
            for (int n = 1; n <= 4; n++)
            {
                int nn = n;
                ToolStripMenuItem it = new ToolStripMenuItem(Tx.T("Nivel ", "Level ") + n, null, delegate { DesdeBandeja("DLSS 5 nivel " + nn, false, delegate { SetLevel(nn); }); });
                niveles.Add(it);
                d5.DropDownItems.Add(it);
            }
            d5.DropDownItems.Add(new ToolStripSeparator());
            for (int n = 1; n <= 3; n++)
            {
                int nn = n;
                ToolStripMenuItem it = new ToolStripMenuItem(n == 1 ? Tx.T("1 pasada", "1 pass") : n + Tx.T(" pasadas", " passes"), null, delegate { DesdeBandeja("DLSS 5 " + nn + " pasadas", true, delegate { SetPasses(nn); }); });
                pasadas.Add(it);
                d5.DropDownItems.Add(it);
            }

            ToolStripMenuItem esc = new ToolStripMenuItem(Tx.T("Escalador alternativo", "Alternative scaler"), null, delegate { DesdeBandeja("escalador", false, delegate { ToggleScaler(); }); });
            esc.ShortcutKeyDisplayString = "Ctrl+R";
            ToolStripMenuItem tram = new ToolStripMenuItem(Tx.T("Filtro de tramado nuevo", "New dither filter"), null, delegate { DesdeBandeja("filtro de tramado", false, delegate { ToggleStrongDither(); }); });
            ToolStripMenuItem est = new ToolStripMenuItem(Tx.T("Ver lo que va puesto (en pantalla)", "Show current settings (on screen)"), null, delegate { DesdeBandeja("estado", false, delegate { Osd(StatusText()); }); });
            est.ShortcutKeyDisplayString = "Ctrl+I";

            // 1.0.11: el menu de ajustes en pantalla (el del boton central del mando)
            ToolStripMenuItem panel = new ToolStripMenuItem(Tx.T("Menú de ajustes en pantalla…", "On-screen settings menu…"), null, delegate
            {
                DesdeBandeja("menu de ajustes", false, delegate { OnUi(delegate { AjustesPedir("bandeja"); }); }); // (22-11, 1.2.2: AjustesGuia.cs)
            });
            panel.ShortcutKeyDisplayString = Tx.T("Botón central / Ctrl+O", "Guide button / Ctrl+O");
            ToolStripItem[] todo = { panel, fg, tipo, modo, cap, d5, esc, tram, est };
            int at = 0;
            foreach (ToolStripItem it in todo) menu.Items.Insert(at++, it);
            menu.Items.Insert(at, new ToolStripSeparator());

            Action marcar = delegate
            {
                bool vivo = ImagenLista();
                foreach (ToolStripItem it in todo) it.Enabled = vivo;
                try
                {
                    fg.Checked = fgWanted && cfg.FgAvailable;
                    fg.Enabled = vivo && cfg.FgAvailable && !cfg.Mode120;
                    tipo.Enabled = vivo && cfg.FgAvailable && !cfg.Mode120;

                    int cur = cfg.Mode120 ? 120 : cfg.CapMode ? 30 : 60;
                    m30.Checked = cur == 30; m60.Checked = cur == 60; m120.Checked = cur == 120;
                    m120.Visible = cur == 120 || Puede120();
                    foreach (ToolStripMenuItem it in caps) it.Checked = (string)it.Tag == cfg.VideoSize;
                    d5.Visible = Integrado.Dlss5Available;
                    d5on.Checked = dlss5Shown;
                    for (int i = 0; i < niveles.Count; i++) niveles[i].Checked = dlss5Level == i + 1;
                    for (int i = 0; i < pasadas.Count; i++) pasadas[i].Checked = dlss5Passes == i + 1;
                    esc.Checked = !dlssScalerOn;
                    esc.Visible = cfg.UpscaleOn || cfg.AltScalerOn;
                    tram.Checked = cfg.StrongDitherOn;
                    tram.Visible = cfg.StrongDither > 0;
                }
                catch (Exception ex) { Log.Error("Bandeja: marcando lo puesto", ex); }
            };
            marcar();
            menu.Opening += delegate { marcar(); };

            // 1.0.12: vigilante de los ganchos de teclado y raton (cada 2 s, hilo de la interfaz)
            ganchoTimer = new System.Windows.Forms.Timer();
            ganchoTimer.Interval = 2000;
            ganchoTimer.Tick += delegate { GanchosVigila(); };
            ganchoTimer.Start();
        }

        // ---- 1.0.12: Windows quita SIN AVISAR un gancho de teclado / raton de bajo nivel si su hilo tarda en contestar (LowLevelHooksTimeout:
        // aqui el hilo de la interfaz, ocupado un momento al arrancar el motor de reserva, al reabrir Magpie...). Desde entonces no habia
        // atajos (Ctrl+G / F / O..., Alt+F4 de la app) ni bloqueo de Joyxoff hasta reiniciar la app (21-11 12:33: Ctrl+I y Ctrl+O sin llegar).
        // Si Windows ha visto entrada (GetLastInputInfo: teclado o raton) despues de la ultima llamada a los ganchos, se vuelven a poner
        [StructLayout(LayoutKind.Sequential)] struct LastInput { public int Size; public int Time; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput li);
        volatile int ganchoUlt = Environment.TickCount;
        System.Windows.Forms.Timer ganchoTimer;
        int ganchoPuestos;
        DateTime ganchoSinRaton = DateTime.MinValue;

        // 21-11 (1.2.1): la hora de la ultima llamada de CADA gancho (KeyHook / MouseHook; ganchoUlt es la de cualquiera de los dos)
        volatile int ganchoTeclaUlt = Environment.TickCount, ganchoRatonUlt = Environment.TickCount;
        int ganchoRawPuestos;

        void GanchosVigila()
        {
            try
            {
                if (userExit || keyHook == IntPtr.Zero) return;
                // 21-11 (1.2.1, log del usuario 19:04-19:15: "Ganchos de teclado y raton: Windows ha visto entrada 2000 ms despues de su ultima
                // llamada (los habra quitado por lentos): se vuelven a poner (vez 140)" cada 2 s jugando, y el "Bloqueo de Joyxoff ... activo"
                // de cada reinstalacion): GetLastInputInfo tambien cuenta el mando XInput (Windows lo toma por el usuario delante), que ningun
                // gancho de teclado ni de raton ve. Con Raw Input (src\Mandos.cs PadHub: teclado y raton, fisicos o inyectados, lo mismo que
                // ven los ganchos) solo se vuelve a poner el gancho que se ha perdido una entrada de verdad. Sin Raw Input, lo de antes
                PadHub hub = PadHub.Shared;
                if (hub.RawInputOk)
                {
                    int t = Environment.TickCount;
                    int kb = hub.LastKbdTick, ms = hub.LastMouseTick;
                    // (la entrada reciente, 15 s; el gancho no la ha visto: su ultima llamada es de mas de 1,5 s antes)
                    bool teclaPerdida = kb != 0 && unchecked(t - kb) < 15000 && unchecked(kb - ganchoTeclaUlt) > 1500;
                    bool ratonPerdido = mouseHook != IntPtr.Zero && hub.RawMouseOk && ms != 0 && unchecked(t - ms) < 15000 && unchecked(ms - ganchoRatonUlt) > 1500;
                    if (!teclaPerdida && !ratonPerdido) return;
                    ganchoRawPuestos++;
                    if (ganchoRawPuestos <= 5 || ganchoRawPuestos % 20 == 0)
                        Log.Write("Ganchos de teclado y raton: Raw Input ha visto " + (teclaPerdida ? "teclado" : "") + (teclaPerdida && ratonPerdido ? " y " : "") +
                                  (ratonPerdido ? "raton" : "") + " que su gancho no (Windows lo habra quitado por lento): se vuelve a poner (vez " + ganchoRawPuestos + ")");
                    // (los dos van en el hilo de la interfaz: si se cayo uno por lento, seguramente el otro tambien)
                    ReinstallKeyHook();
                    if (mouseHook != IntPtr.Zero) ReinstallMouseHook();
                    ganchoUlt = Environment.TickCount;
                    return;
                }
                LastInput li = new LastInput(); li.Size = Marshal.SizeOf(typeof(LastInput));
                if (!GetLastInputInfo(ref li)) return;
                int now = Environment.TickCount;
                if (unchecked(now - li.Time) > 15000) return;               // (nada reciente)
                if (unchecked(li.Time - ganchoUlt) < 1500) return;          // los ganchos han visto lo ultimo
                // (sin el gancho del raton, mover el raton tambien cuenta como entrada sin llamada: entonces como mucho cada 30 s. 21-11, 1.2.1:
                // y siempre, sin Raw Input: el mando XInput tambien cuenta)
                if ((DateTime.Now - ganchoSinRaton).TotalSeconds < 30) return;
                ganchoSinRaton = DateTime.Now;
                ganchoPuestos++;
                if (ganchoPuestos <= 5 || ganchoPuestos % 20 == 0)
                    Log.Write("Ganchos de teclado y raton: Windows ha visto entrada " + unchecked(li.Time - ganchoUlt) + " ms despues de su ultima llamada " +
                              (mouseHook != IntPtr.Zero ? "(los habra quitado por lentos)" : "(o es el raton, sin su gancho)") + ": se vuelven a poner (vez " + ganchoPuestos + ")");
                ReinstallKeyHook();
                if (mouseHook != IntPtr.Zero) ReinstallMouseHook();
                ganchoUlt = Environment.TickCount;
            }
            catch (Exception ex) { Log.Error("Vigilando los ganchos", ex); }
        }

        ToolStripMenuItem ItemModo(int fps, string texto)
        {
            return new ToolStripMenuItem(texto, null, delegate
            {
                DesdeBandeja("modo " + fps, true, delegate
                {
                    if (fps == 120 && !Puede120()) { Osd(Aviso120NoCabe()); return; }
                    SetModeN(fps);
                });
            });
        }

        // La imagen de la consola en marcha (lo que piden las ordenes del mando)
        bool ImagenLista()
        {
            Process p = mpv;
            try { return p != null && !p.HasExited && lsReady && !userExit; } catch { return false; }
        }

        // Una orden del menu de la bandeja: en otro hilo, con la imagen delante (como con el mando). heavy = reabre cosas (captura, modo,
        // pasadas, tipo): la misma espera de 2 s que con el mando
        void DesdeBandeja(string que, bool heavy, Action orden)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (!ImagenLista()) return;
                    Process p = mpv;
                    p.Refresh();
                    IntPtr h = p.MainWindowHandle;
                    if (h != IntPtr.Zero && GetForegroundWindow() != h)
                    {
                        Thread.Sleep(60);   // (que se cierre el menu antes)
                        ForceForeground(h);
                        for (int i = 0; i < 40 && GetForegroundWindow() != h; i++) Thread.Sleep(25);
                    }
                    if (InHome || homeBusy)
                    {
                        if (!homeBusy) Osd(MenuTexto + (cfg.HomeNative ? " en 4K nativo" : "") + " · los ajustes se cambian dentro de cada juego");
                        return;
                    }
                    double since = (DateTime.Now - lastPadAction).TotalMilliseconds;
                    if (heavy && since < 2000) Thread.Sleep((int)(2000 - since));
                    Log.Write("Bandeja: " + que);
                    lastPadAction = DateTime.Now;
                    orden();
                }
                catch (Exception ex) { Log.Error("Bandeja: " + que, ex); }
            });
        }

        static readonly bool TeclasLog = Environment.GetEnvironmentVariable("CE_TECLAS_LOG") == "1";   // (pruebas: cada tecla al log)
        DateTime teclaNoLog = DateTime.MinValue;
        void TeclaNo(string tecla, string porque)
        {
            if ((DateTime.Now - teclaNoLog).TotalSeconds < 3) return;
            teclaNoLog = DateTime.Now;
            string t = tecla, p = porque;
            ThreadPool.QueueUserWorkItem(delegate { Log.Write("Teclado: " + t + " no hace nada: " + p); });
        }

        // Atajos de teclado sobre la imagen de la consola (desde KeyHook, tecla pulsada, fisica). true = es nuestra (no llega a la ventana)
        DateTime teclaCeUltima = DateTime.MinValue;
        bool TeclaCe(uint vk)
        {
            if (TeclasLog) { uint v0 = vk; bool c0 = (GetAsyncKeyState(0x11) & 0x8000) != 0, a0 = (GetAsyncKeyState(0x12) & 0x8000) != 0, ab = ajustesAbierto; ThreadPool.QueueUserWorkItem(delegate { Log.Write("TeclaCe 0x" + v0.ToString("X2") + " ctrl " + c0 + " alt " + a0 + " menu abierto " + ab); }); }
            if ((GetAsyncKeyState(0x11) & 0x8000) == 0 || (GetAsyncKeyState(0x12) & 0x8000) != 0) return false;   // Ctrl sin Alt
            if (vk == 0x4F)   // Ctrl+O: el menu de ajustes (1.0.11, src\Lanzador\MenuAjustes.cs)
            {
                if (userExit) return false;
                if (!AjPrueba && !EsVentanaDeLaApp(GetForegroundWindow())) { TeclaNo("Ctrl+O", "delante no esta la imagen: " + FgOwner()); return false; }
                if (ajustesAbierto) AjustesCerrar("Ctrl+O");
                else AjustesPedir("Ctrl+O"); // (22-11, 1.2.2: ya, en cola o diciendo por que no; AjustesGuia.cs)
                return true;
            }
            Action a = null;
            bool heavy = false;
            string que = null;
            switch (vk)
            {
                case 0x46: que = "generacion si/no"; a = delegate { ToggleFrameGeneration(); }; break;                    // F
                // (1.2.2) G: el selector del tipo de generacion, lo mismo que L3 (antes en KeyHook aparte, sin traer la imagen delante)
                case 0x47: que = "tipo de generacion"; a = delegate { GenSelectorPress(); }; break;                         // G

                case 0x4D: que = "modo"; heavy = true; a = delegate { CeBotonMenu(); }; break;                             // M
                case 0x31: case 0x32: case 0x33: case 0x34:                                                                // 1-4
                {
                    string s = new[] { "1280x720", "1920x1080", "2560x1440", "3840x2160" }[vk - 0x31];
                    que = "captura " + s; heavy = true; a = delegate { RequestCapture(s); }; break;
                }
                case 0x52: que = "escalador"; a = delegate { ToggleScaler(); }; break;                                     // R
                case 0x44: que = "DLSS 5 si/no"; a = delegate { ToggleDlss5(); }; break;                                   // D
                case 0x49: que = "estado"; a = delegate { Osd(StatusText()); }; break;                                     // I
                default: return false;
            }
            if (userExit) return false;
            if (!EsVentanaDeLaApp(GetForegroundWindow())) { TeclaNo("Ctrl+" + (char)vk, "delante no esta la imagen: " + FgOwner()); return false; }
            // (tecla mantenida = repeticiones: una orden cada 0,4 s, 2 s si reabre cosas, como con el mando)
            double since = (DateTime.Now - teclaCeUltima).TotalMilliseconds;
            if (since < 400 || (heavy && since < 2000)) return true;
            teclaCeUltima = DateTime.Now;
            DesdeBandeja("teclado Ctrl+" + (char)vk + ": " + que, heavy, a);
            return true;
        }
    }
}
