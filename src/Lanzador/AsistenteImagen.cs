// AsistenteImagen.cs - pasos "Imagen" (HDR / RTX HDR / Auto HDR / VRR) y "Mando" (los botones de ese mando) del asistente
// (20-11-2026, agente CE-FUNCIONES, ConsoleEnhancer). Lo de serie es lo prudente del plan (src\Deteccion\HdrVrr.cs): RTX HDR y Auto HDR
// apagados, VRR "automatico" (encendido solo si G-SYNC esta activo en el driver para esa pantalla).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    // Lo elegido en el paso "Imagen" (Ce.EscribirIni -> CeImagen.BloqueIni)
    sealed class OpcionesImagen
    {
        public bool RtxHdr;
        public string Vrr = "auto";            // auto | 1 | 0
        public string Botones = "ce";          // mando_botones: ce | prisma
    }

    sealed partial class Asistente
    {
        OpcionesImagen opImagen;
        PadKind kindMando = PadKind.XInput;
        bool kindElegido;

        // De partida: lo que ya habia en el ini (asistente otra vez) o lo prudente
        OpcionesImagen Imagen()
        {
            if (opImagen != null) return opImagen;
            OpcionesImagen o = new OpcionesImagen();
            if (!primera)
            {
                o.RtxHdr = Ce.IniLeer("rtx_hdr") == "1";
                string v = Ce.IniLeer("vrr");
                if (v == "0" || v == "1") o.Vrr = v;
                string b = Ce.IniLeer("mando_botones");
                if (b != null && b.Trim().ToLowerInvariant() == "prisma") o.Botones = "prisma";
            }
            opImagen = o;
            if (!kindElegido) { kindMando = MandoDetectado(); kindElegido = true; }
            return o;
        }

        // El tipo del primer mando que se ve (o, sin ninguno, el de la consola elegida)
        PadKind MandoDetectado()
        {
            foreach (MandoInfo m in hw.Mandos)
            {
                if (m.Virtual) continue;
                PadKind k = BotonesCe.KindDe(m.Vid, m.Pid, m.Origen == "xinput");
                return k;
            }
            if (consola.StartsWith("ps")) return consola == "ps4" ? PadKind.DualShock4 : PadKind.DualSense;
            if (consola.StartsWith("switch")) return consola == "switch2" ? PadKind.Switch2Pro : PadKind.SwitchPro;
            return PadKind.Xbox;
        }

        void PasoImagen()
        {
            OpcionesImagen o = Imagen();
            PlanImagen p = Plan();
            Cabecera(Tx.T("Imagen: HDR y VRR", "Picture: HDR and VRR"),
                     Tx.T("Opcional. Lo de serie es lo prudente: sin HDR añadido y VRR solo si ya se ha visto funcionar en esta pantalla.", "Optional. Defaults are the safe ones: no added HDR, VRR only if it has been seen working on this display."));
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Tx.T("Pantalla: ", "Display: ") + (p.Pantalla != null ? p.Pantalla.Nombre : "?") + " · HDR " +
                          (p.HdrPantalla ? (p.NitsPantalla > 0 ? Tx.N(p.NitsPantalla, "0") + " nits" : Tx.T("sí", "yes")) + (p.HdrWindows ? Tx.T(" (encendido en Windows)", " (on in Windows)") : Tx.T(" (apagado en Windows)", " (off in Windows)")) : "no") +
                          " · VRR " + (p.VrrPosible ? Tx.T("sí", "yes") : "no") + (p.VrrMotivo.Length > 0 ? " (" + p.VrrMotivo + ")" : ""));
            Etiqueta(sb.ToString(), 16, 56);
            int y = 80;
            // ---- RTX HDR (Auto HDR de Windows no: ver ImagenCe.cs)
            if (p.RtxHdrPosible)
            {
                CheckBox c = Casilla(Tx.T("RTX HDR: la imagen SDR de la consola en HDR (la app enciende el HDR de Windows mientras está abierta)",
                                          "RTX HDR: the console's SDR picture in HDR (the app turns Windows HDR on while it is open)"), y, o.RtxHdr);
                c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                c.CheckedChanged += delegate { o.RtxHdr = c.Checked; };
                Etiqueta(Tx.T("Lo hace el driver de NVIDIA (como la NVIDIA App) en el motor, y en Magpie / mpv si su ventana cubre la pantalla; se quita al salir. Brillo: el del driver (~1000 nits; con rtx_hdr_calidad=app, " + p.RtxHdrNits + " nits por el overlay de la NVIDIA App). Cuesta algo de gráfica: las escalas se ajustan solas.",
                              "Done by the NVIDIA driver (as in the NVIDIA App) for the engine, and for Magpie / mpv when their window covers the screen; removed on exit. Brightness: the driver's (~1000 nits; with rtx_hdr_calidad=app, " + p.RtxHdrNits + " nits through the NVIDIA App overlay). It costs some GPU: scales adapt."), y + 34, 66).ForeColor = Suave;
                y += 106;
            }
            else
            {
                Etiqueta(Tx.T("HDR añadido (RTX HDR): no disponible (", "Added HDR (RTX HDR): not available (") + (p.RtxHdrMotivo.Length > 0 ? p.RtxHdrMotivo : Tx.T("la pantalla no tiene HDR", "no HDR display")) + ")." +
                         Tx.T(" Auto HDR de Windows no se ofrece: solo vale para los juegos de la lista de Microsoft.", " Windows Auto HDR is not offered: it only works for games on Microsoft's list."), y, 52).ForeColor = Suave;
                y += 62;
            }
            // ---- VRR
            Etiqueta("VRR (G-SYNC / FreeSync / HDMI VRR)", y, 26);
            ComboBox lv = Lista(y + 28);
            string auto = Tx.T("Automático", "Automatic") + " (" + (p.VrrDeSerie ? Tx.T("encendido aquí", "on here") : Tx.T("apagado aquí", "off here")) + ")";
            lv.Items.Add(auto); lv.Items.Add(Tx.T("Encendido", "On")); lv.Items.Add(Tx.T("Apagado (sincronización vertical)", "Off (vsync)"));
            lv.SelectedIndex = o.Vrr == "1" ? 1 : o.Vrr == "0" ? 2 : 0;
            lv.SelectedIndexChanged += delegate { o.Vrr = lv.SelectedIndex == 1 ? "1" : lv.SelectedIndex == 2 ? "0" : "auto"; };
            lv.Enabled = p.VrrPosible;
            Etiqueta(Tx.T("Con VRR la tele sigue el ritmo de la imagen (menos saltos y algo menos de retraso con la generación). Necesita VRR encendido en la tele y en el driver.",
                          "With VRR the display follows the picture's pace (less judder, a bit less latency with frame generation). Needs VRR on in the TV and the driver."), y + 66, 44).ForeColor = Suave;
            y += 120;
            // ---- latencia por fabricante (lo que hace la app y lo que no puede)
            string lat = p.Latencia == "reflex" ? Tx.T("Latencia: NVIDIA Reflex en el motor (de serie).", "Latency: NVIDIA Reflex in the engine (default).") :
                         p.Latencia == "antilag" ? Tx.T("Latencia: Radeon Anti-Lag lo enciende AMD Software (Juegos > ConsoleEnhancerEngine.exe); la app no puede.", "Latency: enable Radeon Anti-Lag in AMD Software for ConsoleEnhancerEngine.exe.") :
                         Tx.T("Latencia: el motor con 1 imagen en cola.", "Latency: the engine keeps a 1-frame queue.");
            // (22-11, 1.2.4 SOLOMAGPIE: el juego va siempre por mpv + Magpie; el motor propio solo hace de puente mientras Magpie se abre)
            if (Config.SoloMagpieOn)
                lat = Tx.T("Latencia: la imagen va siempre por mpv + Magpie (el motor propio solo hace de puente unos segundos mientras Magpie se abre).",
                           "Latency: the picture always goes through mpv + Magpie (the built-in engine only bridges for a few seconds while Magpie opens).");
            Etiqueta(lat, y, 44).ForeColor = Suave;
        }

        void PasoMando()
        {
            OpcionesImagen o = Imagen();
            Cabecera(Tx.T("Mando", "Controller"), Tx.T("Con la imagen de la consola delante. La app lee el mando sin quitárselo a la consola: la pulsación también llega a la consola.",
                                                      "With the console picture in front. The app reads the controller without taking it from the console: presses also reach the console."));
            Etiqueta(Tx.T("Tipo de mando (para los nombres de los botones):", "Controller type (for the button names):"), 16, 26);
            ComboBox lk = Lista(44);
            PadKind[] tipos = { PadKind.Xbox, PadKind.DualSense, PadKind.DualShock4, PadKind.SwitchPro, PadKind.Switch2Pro, PadKind.JoyConL, PadKind.EightBitDo, PadKind.Generic };
            foreach (PadKind k in tipos) lk.Items.Add(NombreTipo(k));
            ComboBox lb = null;
            TextBox caja = null;
            Action pintar = delegate
            {
                StringBuilder sb = new StringBuilder();
                bool prisma = o.Botones == "prisma";
                bool antes = BotonesCe.Prisma;
                BotonesCe.Prisma = prisma;
                try
                {
                    foreach (string[] f in BotonesCe.Tabla(kindMando))
                    {
                        string b = f[0], q = f[1];
                        if (prisma && b.StartsWith(BotonesCe.NombreMenu(kindMando, true), StringComparison.Ordinal)) q = Tx.T("Modo 60 fps", "60 fps mode");
                        else if (prisma && b.StartsWith(BotonesCe.NombreVista(kindMando, true), StringComparison.Ordinal)) q = Tx.T("Modo 30 fps (la generación: el botón de captura o el panel táctil)", "30 fps mode (frame gen: capture button or touchpad)");
                        sb.AppendLine(b.PadRight(30) + q);
                    }
                }
                finally { BotonesCe.Prisma = antes; }
                sb.AppendLine();
                sb.AppendLine(Tx.T("Fuera de la imagen (modo PC, mando_pc=1): stick izquierdo = ratón, ", "Outside the picture (PC mode): left stick = mouse, ") +
                              BotonesCe.NombreBoton(kindMando, PadBtn.A) + Tx.T(" = clic, mantener ", " = click, hold ") + BotonesCe.NombreBoton(kindMando, PadBtn.Guide) + Tx.T(" 1 s = encenderlo / apagarlo.", " 1 s = toggle it."));
                if (caja != null) caja.Text = sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
            };
            lk.SelectedIndex = Math.Max(0, Array.IndexOf(tipos, kindMando == PadKind.XInput || kindMando == PadKind.GameSir ? PadKind.Xbox : kindMando == PadKind.DualSenseEdge ? PadKind.DualSense : kindMando == PadKind.JoyConR ? PadKind.JoyConL : kindMando));
            lk.SelectedIndexChanged += delegate { kindMando = tipos[lk.SelectedIndex]; pintar(); };
            Etiqueta(Tx.T("Botones:", "Buttons:"), 86, 26);
            lb = Lista(112);
            lb.Items.Add(Tx.T("ConsoleEnhancer: Menú / Options / + = modo 30 ↔ 60 · Vista / Crear / − = generación", "ConsoleEnhancer: Menu / Options / + = 30 ↔ 60 mode · View / Create / − = frame gen"));
            lb.Items.Add(Tx.T("Prisma: Vista / Share / − = modo 30 · Menú / Options / + = modo 60", "Prisma: View / Share / − = 30 mode · Menu / Options / + = 60 mode"));
            lb.SelectedIndex = o.Botones == "prisma" ? 1 : 0;
            lb.SelectedIndexChanged += delegate { o.Botones = lb.SelectedIndex == 1 ? "prisma" : "ce"; pintar(); };
            caja = Caja("", 152, 300);
            pintar();
        }

        // Con RTX HDR elegido: Allow y Driver Flags en los perfiles de NVIDIA ya (piden administrador: el aviso de UAC aqui, no al abrir la app)
        void GuardarImagen()
        {
            try
            {
                OpcionesImagen o = Imagen();
                if (o.RtxHdr && !soloVer) NvDrsCe.AsegurarAdmin(CeImagen.Exes, 0x02);
            }
            catch (Exception ex) { Log.Error("Asistente: RTX HDR", ex); }
        }

        // Para el resumen: lo elegido en "Imagen" y "Mando"
        string ResumenImagen(PlanImagen p)
        {
            OpcionesImagen o = Imagen();
            string vrr = o.Vrr == "1" ? Tx.T("encendido", "on") : o.Vrr == "0" ? Tx.T("apagado", "off") : Tx.T("automático", "automatic") + " (" + (p.VrrDeSerie ? Tx.T("encendido", "on") : Tx.T("apagado", "off")) + ")";
            string hdr = o.RtxHdr && p.RtxHdrPosible ? "RTX HDR " + p.RtxHdrNits + " nits" : Tx.T("no", "no");
            return "\r\n" + Tx.T("Elegido: HDR añadido ", "Chosen: added HDR ") + hdr + " · VRR " + vrr + " · " + Tx.T("botones ", "buttons ") +
                   (o.Botones == "prisma" ? "Prisma" : BotonesCe.NombreMenu(kindMando, true) + Tx.T(" = modo 30/60, ", " = 30/60 mode, ") + BotonesCe.NombreVista(kindMando, true) + Tx.T(" = generación", " = frame gen")) +
                   " · " + Tx.T("escalas: estimadas para esta gráfica y ajustadas jugando", "scales: estimated for this GPU and adjusted while playing") + "\r\n";
        }

        string NombreTipo(PadKind k)
        {
            switch (k)
            {
                case PadKind.Xbox: return Tx.T("Xbox / XInput (y GameSir, 8BitDo en modo X)", "Xbox / XInput (and GameSir, 8BitDo in X mode)");
                case PadKind.DualSense: return "PlayStation 5 (DualSense)";
                case PadKind.DualShock4: return "PlayStation 4 (DualShock 4)";
                case PadKind.SwitchPro: return Tx.T("Nintendo Switch (Mando Pro)", "Nintendo Switch (Pro Controller)");
                case PadKind.Switch2Pro: return Tx.T("Nintendo Switch 2 (Mando Pro)", "Nintendo Switch 2 (Pro Controller)");
                case PadKind.JoyConL: return "Joy-Con";
                case PadKind.EightBitDo: return Tx.T("8BitDo (modo Switch / D-input)", "8BitDo (Switch / D-input mode)");
                default: return Tx.T("Genérico (HID)", "Generic (HID)");
            }
        }
    }
}
