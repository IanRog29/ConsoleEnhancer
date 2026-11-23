// Panel.cs - "ConsoleEnhancer Settings.exe" (21-11-2026, 1.2.0): una ventana con todo lo que se cambia fuera de la imagen.
//
// El usuario: "en la carpeta de la app deja mas claro, pon un ejecutable para cambiar cosas y desinstalar y todo".
// ConsoleEnhancer.exe --ajustes (lo abre "ConsoleEnhancer Settings.exe", src\Ajustes\Ajustes.cs, junto al exe). Sustituye a
// "Inicio y apagado.bat". Con la app abierta o cerrada:
//  - Abrir ConsoleEnhancer / asistente de configuracion.
//  - Como se usa el PC (como una consola / como un PC normal), abrir con Windows, escritorio negro, que hace el PC al apagar la consola.
//  - DLSS 5 (tu propio archivo): estado, anadir / cambiar, quitar, abrir la carpeta extras\.
//  - Componentes (mpv, Magpie): comprobar / volver a bajar; abrir los registros y la carpeta de datos.
//  - Desinstalar (lo mismo que la bandeja: CeBorrado, src\Lanzador\Desinstalar.cs; 1.2.3: el borrado al cerrarse el panel, en Run).
// Lo que se guarda va al ini; si la app esta abierta, lo lee al volver a abrirse: el panel ofrece reiniciarla (--salir + abrirla).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    sealed class PanelAjustes : Form
    {
        public static int Run()
        {
            try { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); } catch (InvalidOperationException) { }
            // (1.2.02) el idioma elegido en el asistente (idioma= del ini); antes el del Windows siempre
            try { Idioma.Poner((Ce.IniLeer("idioma") ?? "").Trim().ToLowerInvariant()); } catch { }
            Log.Write("Panel de ajustes abierto (" + Paths.AppDir + ")");
            using (PanelAjustes f = new PanelAjustes()) Application.Run(f);
            CeBorrado.AlSalir(); // (1.2.3: si se pidio "Desinstalar", el borrado ahora, con el panel ya cerrado; src\Lanzador\Desinstalar.cs)
            return 0;
        }

        static readonly Color Fondo = Color.FromArgb(20, 23, 30), Tarjeta = Color.FromArgb(30, 34, 44), Texto = Color.FromArgb(236, 240, 245),
                              Detalle = Color.FromArgb(150, 160, 175), Rojo = Color.FromArgb(235, 90, 90);
        static string Exe { get { return Path.Combine(Paths.AppDir, "ConsoleEnhancer.exe"); } }

        readonly FlowLayoutPanel cuerpo;
        Label estadoApp, estadoD5;
        RadioButton rConsola, rNormal;
        CheckBox cInicio, cNegro;
        ComboBox cbApagar;
        Button bReiniciar, bQuitarD5, bAnadirD5;
        bool cargando, cambiado;
        static float k = 1f;   // pixeles por pixel de 96 ppp (la app es DPI-aware: a 4K al 300 %, 3)
        static int S(float px) { return (int)Math.Round(px * k); }
        static readonly string[] Modos = { "nada", "espera", "suspender", "apagar" };

        PanelAjustes()
        {
            Text = Tx.T("Ajustes de ConsoleEnhancer", "ConsoleEnhancer settings");
            try { Icon = PrismaUi.AppIcon(32); } catch { }
            BackColor = Fondo; ForeColor = Texto;
            Font = PrismaUi.BodyFontPt(10f);
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            using (Graphics g = CreateGraphics()) k = Math.Max(1f, g.DpiX / 96f);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(S(720), wa.Width - S(40)), Math.Min(S(820), wa.Height - S(60)));

            cuerpo = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(S(22), S(16), S(22), S(16)) };
            Controls.Add(cuerpo);

            // ---- cabecera
            Label tit = new Label { Text = "ConsoleEnhancer", Font = PrismaUi.TitleFontPt(18f), AutoSize = true, ForeColor = Texto, Margin = new Padding(S(0), S(0), S(0), S(2)) };
            cuerpo.Controls.Add(tit);
            estadoApp = Nota("");
            cuerpo.Controls.Add(estadoApp);
            FlowLayoutPanel fila = Fila();
            fila.Controls.Add(Boton(Tx.T("Abrir ConsoleEnhancer", "Open ConsoleEnhancer"), true, delegate { AbrirApp(""); }));
            fila.Controls.Add(Boton(Tx.T("Asistente de configuración…", "Setup wizard…"), false, delegate
            {
                if (!CerrarSiAbierta(Tx.T("El asistente necesita la app cerrada.", "The wizard needs the app closed."))) return;
                AbrirApp("--asistente");
            }));
            cuerpo.Controls.Add(fila);

            // ---- como se usa el PC
            Seccion(Tx.T("Cómo se usa el PC", "How the PC is used"));
            rConsola = Radio(Tx.T("Como una consola", "Like a console"));
            rNormal = Radio(Tx.T("Como un PC normal", "Like a normal PC"));
            rConsola.CheckedChanged += delegate { if (!cargando && rConsola.Checked) CambiarUso(true); };
            rNormal.CheckedChanged += delegate { if (!cargando && rNormal.Checked) CambiarUso(false); };
            cuerpo.Controls.Add(rConsola);
            cuerpo.Controls.Add(Sangria(Nota(Tx.T("Se abre sola con Windows, escritorio negro y, al apagar la consola, el PC en espera.",
                                                  "Opens with Windows, black desktop and, when the console turns off, the PC on standby."))));
            cuerpo.Controls.Add(rNormal);
            cuerpo.Controls.Add(Sangria(Nota(Tx.T("Windows de siempre: la app solo se abre cuando la abres tú.",
                                                  "Regular Windows: the app only opens when you open it."))));
            cInicio = Check(Tx.T("Abrir ConsoleEnhancer al iniciar Windows", "Open ConsoleEnhancer when Windows starts"));
            cInicio.CheckedChanged += delegate { if (!cargando) PonerInicio(cInicio.Checked); };
            cNegro = Check(Tx.T("Escritorio negro mientras la app está abierta", "Black desktop while the app is open"));
            cNegro.CheckedChanged += delegate { if (!cargando) { Ce.IniPoner("escritorio_negro", cNegro.Checked ? "1" : "0"); Cambio("escritorio_negro=" + (cNegro.Checked ? 1 : 0)); } };
            cuerpo.Controls.Add(cInicio); cuerpo.Controls.Add(cNegro);
            Label la = Nota(Tx.T("Al apagar la consola, el PC:", "When the console is turned off, the PC:"));
            la.ForeColor = Texto; la.Margin = new Padding(S(0), S(10), S(0), S(4));
            cuerpo.Controls.Add(la);
            FlowLayoutPanel fa = Fila();
            cbApagar = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(380), BackColor = Tarjeta, ForeColor = Texto, FlatStyle = FlatStyle.Flat };
            foreach (string m in Modos) cbApagar.Items.Add(CeEspera.Nombre(m));
            cbApagar.SelectedIndexChanged += delegate { if (!cargando) PonerApagar(Modos[Math.Max(0, cbApagar.SelectedIndex)]); };
            fa.Controls.Add(cbApagar);
            fa.Controls.Add(Boton(Tx.T("¿Cómo funciona y cuánto gasta?", "How does it work and how much power?"), false, delegate
            {
                MessageBox.Show(this, CeEspera.Explicacion(), PrismaUi.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));
            cuerpo.Controls.Add(fa);

            // ---- DLSS 5
            Seccion("DLSS 5");
            estadoD5 = Nota("");
            cuerpo.Controls.Add(estadoD5);
            FlowLayoutPanel fd = Fila();
            bAnadirD5 = Boton("", false, delegate
            {
                if (!Extras.Anadir(this)) return;
                Ce.PrepararExtras();
                Integrado.Dlss5Rever();
                Cambio("DLSS 5 anadido");
                Refrescar();
            });
            bQuitarD5 = Boton(Tx.T("Quitar DLSS 5", "Remove DLSS 5"), false, delegate
            {
                if (!CerrarSiAbierta(Tx.T("Para quitarlo, la app tiene que estar cerrada (Magpie lo tiene cargado).", "To remove it, the app has to be closed (Magpie has it loaded)."))) return;
                if (!Extras.Quitar(this)) return;
                Ce.PrepararExtras();
                Integrado.Dlss5Rever();
                Refrescar();
            });
            fd.Controls.Add(bAnadirD5); fd.Controls.Add(bQuitarD5);
            fd.Controls.Add(Boton(Tx.T("Abrir la carpeta extras", "Open the extras folder"), false, delegate
            {
                try { Directory.CreateDirectory(Extras.Dir); } catch { }
                AbrirCarpeta(Extras.Dir);
            }));
            cuerpo.Controls.Add(fd);

            // ---- mantenimiento
            Seccion(Tx.T("Mantenimiento", "Maintenance"));
            FlowLayoutPanel fm = Fila();
            fm.Controls.Add(Boton(Tx.T("Comprobar / volver a descargar mpv y Magpie", "Check / download mpv and Magpie again"), false, delegate
            {
                if (!CerrarSiAbierta(Tx.T("Para volver a descargarlos, la app tiene que estar cerrada.", "To download them again, the app has to be closed."))) return;
                AbrirApp("--componentes-reparar");
            }));
            fm.Controls.Add(Boton(Tx.T("Probar el aviso de apagado", "Test the shutdown notice"), false, delegate
            {
                string s = Ce.IniLeer("apagar_pc_aviso_s");
                AbrirApp("--prueba-apagado=" + (string.IsNullOrEmpty(s) || s == "0" ? "10" : s));
            }));
            fm.Controls.Add(Boton(Tx.T("Probar el modo espera", "Test standby"), false, delegate
            {
                if (MessageBox.Show(this, Tx.T("Se apaga la pantalla y el PC queda en espera. Para volver: enciende la consola o un mando, o pulsa una tecla.",
                                               "The screen turns off and the PC goes to standby. To come back: turn on the console or a controller, or press a key."),
                                    PrismaUi.AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
                if (!CerrarSiAbierta(Tx.T("La prueba necesita la app cerrada.", "The test needs the app closed."))) return;
                AbrirApp("--prueba-espera");
            }));
            fm.Controls.Add(Boton(Tx.T("Abrir los registros", "Open the logs"), false, delegate { AbrirCarpeta(Path.Combine(Paths.Datos, "logs")); }));
            fm.Controls.Add(Boton(Tx.T("Abrir mis ajustes y perfiles", "Open my settings and profiles"), false, delegate { AbrirCarpeta(Paths.Datos); }));
            cuerpo.Controls.Add(fm);

            // ---- desinstalar
            Seccion(Tx.T("Desinstalar", "Uninstall"));
            Button bd = Boton(Tx.T("Desinstalar ConsoleEnhancer…", "Uninstall ConsoleEnhancer…"), false, delegate { Desinstalar(); });
            bd.ForeColor = Rojo; bd.FlatAppearance.BorderColor = Rojo;
            cuerpo.Controls.Add(bd);

            // ---- pie: reiniciar para aplicar
            bReiniciar = Boton(Tx.T("Reiniciar ConsoleEnhancer para aplicar los cambios", "Restart ConsoleEnhancer to apply the changes"), true, delegate
            {
                if (!CerrarApp()) { MessageBox.Show(this, Tx.T("No se ha podido cerrar la app. Ciérrala desde la bandeja y vuelve a abrirla.", "Could not close the app. Close it from the tray and open it again."), PrismaUi.AppName); return; }
                AbrirApp("");
                cambiado = false;
                Refrescar();
            });
            bReiniciar.Margin = new Padding(S(0), S(22), S(0), S(0));
            cuerpo.Controls.Add(bReiniciar);

            Refrescar();
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 1500 };
            t.Tick += delegate { RefrescarEstadoApp(); };
            t.Start();
        }

        // Pruebas: --ajustes-preview=<png> lo dibuja fuera de la pantalla, sin enseñarlo, y sale
        public static int Preview(string png)
        {
            try { Application.EnableVisualStyles(); } catch (InvalidOperationException) { }
            using (PanelAjustes f = new PanelAjustes())
            {
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-32000, -32000); f.ShowInTaskbar = false;
                f.ClientSize = new Size(f.ClientSize.Width, S(1250));   // (todo, sin barra)
                f.Show(); Application.DoEvents();
                using (Bitmap bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(png); }
                f.Close();
            }
            return 0;
        }

        // ------------------------------------------------------------------ estado
        static bool AppAbierta()
        {
            Mutex m;
            if (!Mutex.TryOpenExisting("Local\\ConsoleEnhancer_SingleInstance", out m)) return false;
            m.Dispose();
            return true;
        }

        void RefrescarEstadoApp()
        {
            bool ab = AppAbierta();
            estadoApp.Text = (ab ? Tx.T("La app está abierta ahora.", "The app is open now.") : Tx.T("La app está cerrada.", "The app is closed.")) +
                             "  ·  " + Paths.AppDir;
            bReiniciar.Visible = ab && cambiado;
        }

        void Refrescar()
        {
            cargando = true;
            try
            {
                bool apagar = Ce.IniLeer("apagar_pc") == "1", negro = Ce.IniLeer("escritorio_negro") == "1";
                bool inicio = Ce.IniLeer("iniciar_con_windows") == "1";
                string modo = CeSuspension.Normaliza(Ce.IniLeer("apagar_pc_modo"));
                rConsola.Checked = apagar && negro;
                rNormal.Checked = !apagar && !negro && !inicio;
                cInicio.Checked = inicio;
                cNegro.Checked = negro;
                cbApagar.SelectedIndex = apagar ? Array.IndexOf(Modos, modo) : 0;
                bool hay = Extras.HayDlss5;
                estadoD5.Text = hay ? Tx.T("Instalado (tu archivo " + Integrado.Dlss5Dll + " en la carpeta extras). No es oficial de NVIDIA.",
                                           "Installed (your " + Integrado.Dlss5Dll + " file in the extras folder). Not official from NVIDIA.")
                                    : Tx.T("No instalado. Para instalarlo, pon tu archivo " + Integrado.Dlss5Dll + " en " + Extras.Dir + " (o usa el botón de abajo).",
                                           "Not installed. To install it, put your " + Integrado.Dlss5Dll + " file in " + Extras.Dir + " (or use the button below).");
                // (1.2.02-1.2.4: aqui se decia que DLSS FG con DLSS 5 necesitaba dlssg_sm86. 1.2.4 ronda 6, QUITAFG: sin DLSS FG, fuera)
                bAnadirD5.Text = hay ? Tx.T("Cambiar el archivo…", "Change the file…") : Tx.T("Añadir DLSS 5 (tu propio archivo)…", "Add DLSS 5 (your own file)…");
                bQuitarD5.Visible = hay || Directory.Exists(Extras.Sm86);
            }
            finally { cargando = false; }
            RefrescarEstadoApp();
        }

        void Cambio(string que)
        {
            Log.Write("Panel de ajustes: " + que);
            cambiado = true;
            RefrescarEstadoApp();
        }

        // ------------------------------------------------------------------ cambios
        void CambiarUso(bool consola)
        {
            Ce.IniPoner("apagar_pc", consola ? "1" : "0");
            if (consola) Ce.IniPoner("apagar_pc_modo", "espera");
            Ce.IniPoner("escritorio_negro", consola ? "1" : "0");
            if (!consola) { try { Escritorio.Cambiar(false); } catch (Exception ex) { Log.Error("Panel: escritorio", ex); } }
            PonerInicio(consola, false);
            Cambio("como se usa el PC: " + (consola ? "como una consola" : "como un PC normal"));
            Refrescar();
        }

        void PonerInicio(bool on, bool refrescar = true)
        {
            string det;
            bool ok = CeInicio.Poner(on, out det);
            Log.Write("Panel de ajustes: iniciar con Windows " + (on ? "si" : "no") + ": " + det);
            if (ok) Ce.IniPoner("iniciar_con_windows", on ? "1" : "0");
            else MessageBox.Show(this, Tx.T("No se pudo cambiar el inicio con Windows: ", "Could not change starting with Windows: ") + det, PrismaUi.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Cambio("iniciar con Windows " + (on ? "si" : "no"));
            if (refrescar) Refrescar();
        }

        void PonerApagar(string modo)
        {
            if (modo == "nada") Ce.IniPoner("apagar_pc", "0");
            else { Ce.IniPoner("apagar_pc", "1"); Ce.IniPoner("apagar_pc_modo", modo); }
            if (modo == "suspender") ThreadPool.QueueUserWorkItem(delegate { try { CeSuspension.PedirArmar(true); } catch { } });
            Cambio("al apagar la consola: " + modo);
            Refrescar();
        }

        // ------------------------------------------------------------------ la app
        void AbrirApp(string args)
        {
            try { Process.Start(new ProcessStartInfo(Exe, args) { UseShellExecute = false, WorkingDirectory = Paths.AppDir }); Log.Write("Panel de ajustes: abierta la app " + args); }
            catch (Exception ex) { Log.Error("Panel: abrir la app", ex); MessageBox.Show(this, ex.Message, PrismaUi.AppName); }
        }

        // La app abierta se cierra como con Salir de la bandeja (--salir) y se espera a que acabe
        static bool CerrarApp()
        {
            if (!AppAbierta()) return true;
            try
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting("Local\\ConsoleEnhancer_Salir", out ev)) using (ev) ev.Set();
            }
            catch (Exception ex) { Log.Error("Panel: --salir", ex); }
            Stopwatch sw = Stopwatch.StartNew();
            while (AppAbierta() && sw.ElapsedMilliseconds < 25000) { Application.DoEvents(); Thread.Sleep(100); }
            Thread.Sleep(500);
            return !AppAbierta();
        }

        bool CerrarSiAbierta(string porque)
        {
            if (!AppAbierta()) return true;
            if (MessageBox.Show(this, porque + "\n\n" + Tx.T("¿Cerrar ConsoleEnhancer ahora?", "Close ConsoleEnhancer now?"), PrismaUi.AppName,
                                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return false;
            UseWaitCursor = true;
            bool ok = CerrarApp();
            UseWaitCursor = false;
            if (!ok) MessageBox.Show(this, Tx.T("No se ha podido cerrar la app. Ciérrala desde la bandeja.", "Could not close the app. Close it from the tray."), PrismaUi.AppName);
            return ok;
        }

        static void AbrirCarpeta(string dir)
        {
            try { Directory.CreateDirectory(dir); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Panel: abrir " + dir, ex); }
        }

        // ------------------------------------------------------------------ desinstalar (como la bandeja)
        void Desinstalar()
        {
            string app = Paths.AppDir, datos = Paths.Datos;
            string txt = Tx.T(
                "Se va a desinstalar ConsoleEnhancer:\n\n" +
                "• Deja de abrirse con Windows y vuelve el escritorio normal.\n" +
                "• Se cierra (si está abierta) y se borran sus archivos de:\n   " + app + "\n" +
                "  (solo los suyos: si hay otras cosas en esa carpeta, se quedan).\n\n" +
                "No se toca nada más del PC ni de la consola. ¿Desinstalar?",
                "ConsoleEnhancer will be uninstalled:\n\n" +
                "• It stops opening with Windows and the normal desktop comes back.\n" +
                "• It closes (if open) and its files are deleted from:\n   " + app + "\n" +
                "  (only its own: anything else in that folder stays).\n\n" +
                "Nothing else on the PC or the console is touched. Uninstall?");
            if (MessageBox.Show(this, txt, PrismaUi.AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            DialogResult borrarDatos = MessageBox.Show(this,
                Tx.T("¿Borrar también tus ajustes y perfiles de juegos?\n\n" + datos + "\n\nSí = se borra todo. No = se guardan (si vuelves a instalar ConsoleEnhancer, los recupera).",
                     "Also delete your settings and game profiles?\n\n" + datos + "\n\nYes = everything is deleted. No = they are kept (if you install ConsoleEnhancer again, it gets them back)."),
                PrismaUi.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (borrarDatos == DialogResult.Cancel) return;
            UseWaitCursor = true;
            if (!CerrarApp())
            {
                UseWaitCursor = false;
                MessageBox.Show(this, Tx.T("No se ha podido cerrar la app. Ciérrala desde la bandeja y vuelve a intentarlo.", "Could not close the app. Close it from the tray and try again."), PrismaUi.AppName);
                return;
            }
            Log.Write("Desinstalar pedido desde el panel de ajustes (" + app + (borrarDatos == DialogResult.Yes ? ", con los datos " : ", sin los datos ") + datos + ")");
            string det;
            CeInicio.Poner(false, out det);
            Log.Write("Desinstalar: inicio con Windows quitado (" + det + ")");
            // (1.2.3: iniciar_con_windows=0 tambien, en los ajustes que se guardan: una copia nueva que los herede no se pone sola en el inicio)
            try { Ce.IniPoner("escritorio_negro", "0"); Ce.IniPoner("apagar_pc", "0"); Ce.IniPoner("iniciar_con_windows", "0"); } catch { }
            try { Escritorio.Cambiar(false); } catch (Exception ex) { Log.Error("Desinstalar: escritorio", ex); }
            CeEspera.PlanDeAntes("desinstalar");
            // (1.2.2: con "Sí", tambien la copia de seguridad. 1.2.3: el borrado lo hace este proceso al cerrarse el panel, Run, y dice como ha ido)
            bool ok = CeBorrado.Preparar(app, datos, borrarDatos == DialogResult.Yes, true);
            UseWaitCursor = false;
            if (!ok)
                MessageBox.Show(this, Tx.T("No se pudieron borrar los archivos. Borra la carpeta a mano:\n", "Could not delete the files. Delete the folder yourself:\n") + app,
                                PrismaUi.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }

        // ------------------------------------------------------------------ controles
        void Seccion(string t)
        {
            Label l = new Label { Text = t, Font = PrismaUi.TitleFontPt(12f), AutoSize = true, ForeColor = PrismaUi.Accent, Margin = new Padding(S(0), S(20), S(0), S(6)) };
            cuerpo.Controls.Add(l);
        }
        static Label Nota(string t) { return new Label { Text = t, AutoSize = true, MaximumSize = new Size(S(640), 0), ForeColor = Detalle, Margin = new Padding(S(0), S(2), S(0), S(6)) }; }
        static Label Sangria(Label l) { l.Margin = new Padding(S(24), 0, 0, S(4)); return l; }
        static FlowLayoutPanel Fila() { return new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(S(650), 0), Margin = new Padding(S(0), S(2), S(0), S(2)) }; }
        RadioButton Radio(string t) { return new RadioButton { Text = t, AutoSize = true, MaximumSize = new Size(S(640), 0), ForeColor = Texto, Margin = new Padding(S(0), S(3), S(0), S(3)) }; }
        CheckBox Check(string t) { return new CheckBox { Text = t, AutoSize = true, ForeColor = Texto, Margin = new Padding(S(0), S(3), S(0), S(3)) }; }
        static Button Boton(string t, bool principal, EventHandler click)
        {
            Button b = new Button { Text = t, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat, Padding = new Padding(S(10), S(5), S(10), S(5)), Margin = new Padding(S(0), S(4), S(10), S(4)), Cursor = Cursors.Hand };
            b.BackColor = principal ? PrismaUi.Accent : Tarjeta;
            b.ForeColor = principal ? Color.FromArgb(8, 12, 18) : Texto;
            b.FlatAppearance.BorderColor = principal ? PrismaUi.Accent : Color.FromArgb(70, 78, 92);
            b.Click += click;
            return b;
        }
    }
}
