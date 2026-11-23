// Asistente.cs - asistente del primer arranque de ConsoleEnhancer (fase 2, agente UNIVERSAL)
//
// Pasos (DISENO.md §4): idioma, lo detectado, capturadora (se abre aqui para leer sus formatos; prueba de imagen con mpv), audio
// (prueba de sonido), consola, pantalla, imagen (20-11: HDR / RTX HDR / VRR), mando (20-11: los botones de ESE mando) y el resumen con
// el plan. Al terminar escribe el ini (Ce.EscribirIni) y la huella.
// Raton y teclado (Tab / Enter / Esc). Sin consola encendida tambien vale (la prueba de imagen enseña lo que mande la capturadora).
// Prueba sin interaccion: --ui-preview=asistente;<carpeta> (una imagen de cada paso, con lo detectado en este PC; no abre la capturadora)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    sealed partial class Asistente : Form   // (20-11: pasos Imagen y Mando en AsistenteImagen.cs)
    {
        static readonly Color Fondo = Color.FromArgb(18, 20, 28), Panel_ = Color.FromArgb(30, 33, 45), Texto = Color.FromArgb(230, 234, 242),
                              Suave = Color.FromArgb(150, 158, 175), Acento = Color.FromArgb(0, 190, 240);

        readonly Hardware hw;
        readonly bool primera;
        int paso;
        const int Pasos = 9;
        bool guardado;
        bool soloVer;              // --ui-preview: no abrir nada (ni la capturadora ni mpv)
        readonly float k = Escala();
        static float Escala() { try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f); } catch { return 1f; } }
        int S(int v) { return (int)Math.Round(v * k); }
        string idioma = Tx.Idioma;
        string consola = "auto";
        CapturadoraInfo captura;
        FormatoCaptura formato;
        AudioEntrada audio;
        bool sinAudio;
        PantallaInfo pantalla;
        PlanImagen plan;
        Process prueba;

        readonly Label titulo = new Label(), sub = new Label(), estado = new Label();
        readonly Panel cuerpo = new Panel();
        readonly Button atras = new Button(), siguiente = new Button(), cancelar = new Button();

        // true = ini escrito (seguir arrancando), false = cancelado
        public static bool Ejecutar(Hardware hw, bool primera)
        {
            using (Asistente a = new Asistente(hw, primera))
            {
                Application.EnableVisualStyles();
                a.ShowDialog();
                return a.guardado;
            }
        }

        Asistente(Hardware hw, bool primera)
        {
            this.hw = hw;
            this.primera = primera;
            Text = "ConsoleEnhancer";
            try { Icon own = PrismaUi.AppIcon(32); if (own != null) Icon = own; } catch { }
            BackColor = Fondo; ForeColor = Texto;
            Font = new Font("Segoe UI", 11f);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(980), S(680));
            MinimumSize = new Size(S(800), S(560));
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };

            titulo.Font = new Font("Segoe UI Semibold", 20f); titulo.ForeColor = Texto; titulo.AutoSize = false;
            titulo.SetBounds(S(32), S(20), S(900), S(44)); titulo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            sub.ForeColor = Suave; sub.AutoSize = false; sub.SetBounds(S(34), S(66), S(900), S(48)); sub.Anchor = titulo.Anchor;
            cuerpo.SetBounds(S(32), S(118), S(916), S(470)); cuerpo.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            cuerpo.BackColor = Panel_; cuerpo.Padding = new Padding(16);
            estado.ForeColor = Suave; estado.AutoSize = false; estado.SetBounds(S(32), S(602), S(520), S(50)); estado.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Boton(cancelar, 560, delegate { Close(); });
            Boton(atras, 690, delegate { Ir(paso - 1); });
            Boton(siguiente, 820, delegate { if (paso == Pasos - 1) Guardar(); else Ir(paso + 1); });
            Controls.AddRange(new Control[] { titulo, sub, cuerpo, estado, cancelar, atras, siguiente });
            AcceptButton = siguiente;

            // de partida, lo que elegiria el plan
            pantalla = hw.Pantallas.Find(delegate(PantallaInfo p) { return p.Principal; });
            if (pantalla == null && hw.Pantallas.Count > 0) pantalla = hw.Pantallas[0];
            captura = hw.Capturadoras.Find(delegate(CapturadoraInfo c) { return c.Tipo == TipoCaptura.Capturadora; });
            FormClosing += delegate { PararPrueba(); };
            Ir(0);
        }

        void Boton(Button b, int x, EventHandler click)
        {
            b.SetBounds(S(x), S(606), S(120), S(40)); b.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Acento; b.BackColor = Panel_; b.ForeColor = Texto;
            b.Click += click;
        }

        void Textos()
        {
            cancelar.Text = Tx.T("Cancelar", "Cancel");
            atras.Text = Tx.T("Atrás", "Back");
            siguiente.Text = paso == Pasos - 1 ? Tx.T("Empezar", "Start") : Tx.T("Siguiente", "Next");
            atras.Enabled = paso > 0;
        }

        // ------------------------------------------------------------------ pasos
        void Ir(int n)
        {
            PararPrueba();
            paso = Math.Max(0, Math.Min(Pasos - 1, n));
            cuerpo.Controls.Clear();
            estado.Text = "";
            switch (paso)
            {
                case 0: PasoIdioma(); break;
                case 1: PasoDeteccion(); break;
                case 2: PasoCaptura(); break;
                case 3: PasoAudio(); break;
                case 4: PasoConsola(); break;
                case 5: PasoPantalla(); break;
                case 6: PasoImagen(); break;   // (20-11, CE-FUNCIONES: AsistenteImagen.cs)
                case 7: PasoMando(); break;    // (20-11, CE-FUNCIONES: AsistenteImagen.cs)
                default: PasoResumen(); break;
            }
            Textos();
        }

        void Cabecera(string t, string s)
        {
            titulo.Text = (paso + 1) + "/" + Pasos + "  " + t;
            sub.Text = s;
        }

        Label Etiqueta(string t, int y, int h)
        {
            Label l = new Label(); l.Text = t; l.ForeColor = Texto; l.AutoSize = false; l.SetBounds(S(16), S(y), S(884), S(h));
            l.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cuerpo.Controls.Add(l); return l;
        }

        TextBox Caja(string t, int y, int h)
        {
            TextBox b = new TextBox(); b.Multiline = true; b.ReadOnly = true; b.ScrollBars = ScrollBars.Vertical; b.WordWrap = true;
            b.BackColor = Fondo; b.ForeColor = Texto; b.BorderStyle = BorderStyle.None; b.Font = new Font("Consolas", 10f);
            b.SetBounds(S(16), S(y), S(884), S(h)); b.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            b.Text = t.Replace("\r\n", "\n").Replace("\n", "\r\n");
            cuerpo.Controls.Add(b); return b;
        }

        ComboBox Lista(int y)
        {
            ComboBox c = new ComboBox(); c.DropDownStyle = ComboBoxStyle.DropDownList; c.BackColor = Fondo; c.ForeColor = Texto; c.FlatStyle = FlatStyle.Flat;
            c.SetBounds(S(16), S(y), S(884), S(30)); c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cuerpo.Controls.Add(c); return c;
        }

        Button BotonCuerpo(string t, int x, int y, EventHandler click)
        {
            Button b = new Button(); b.Text = t; b.SetBounds(S(x), S(y), S(220), S(38));
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Acento; b.BackColor = Fondo; b.ForeColor = Texto; b.Click += click;
            cuerpo.Controls.Add(b); return b;
        }

        void PasoIdioma()
        {
            Cabecera(Tx.T("Bienvenido a ConsoleEnhancer", "Welcome to ConsoleEnhancer"),
                     primera ? Tx.T("Primer arranque: vamos a detectar tu capturadora, tu gráfica y tu pantalla y a elegir la mejor imagen para tu PC.",
                                    "First start: let's detect your capture device, GPU and display and pick the best picture for your PC.")
                             : Tx.T("Configurar de nuevo.", "Set up again."));
            Etiqueta(Tx.T("Idioma / Language", "Language / Idioma"), 20, 30);
            BotonCuerpo("Español", 16, 60, delegate { idioma = "es"; Idioma.Elegir("es"); Ir(0); });
            BotonCuerpo("English", 252, 60, delegate { idioma = "en"; Idioma.Elegir("en"); Ir(0); });
            Etiqueta(Tx.T("Elegido: ", "Selected: ") + (Tx.Ingles ? "English" : "Español"), 112, 30).ForeColor = Acento;
        }

        void PasoDeteccion()
        {
            Cabecera(Tx.T("Lo que hay en este PC", "What this PC has"), Tx.T("Detectado sin abrir nada. Los avisos se pueden arreglar ahora o más tarde.", "Detected without opening anything."));
            PlanImagen p = Plan();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            GpuInfo g = p.Gpu;
            sb.AppendLine(Tx.T("Gráfica: ", "GPU: ") + (g != null ? g.Nombre + " · " + Tx.Mb(g.Vram) + " · driver " + g.Driver : "?"));
            if (g != null && !g.Software)
                sb.AppendLine("   DLSS " + Tx.SiNo(g.F.DlssSr.Si) + " · Smooth Motion " + Tx.SiNo(g.F.SmoothMotion.Si) + " · Reflex " + Tx.SiNo(g.F.Reflex.Si) +
                              " · XeSS FG " + Tx.SiNo(g.F.XessFg.Si) + " · FSR 3 FG " + Tx.SiNo(g.F.Fsr3Fg.Si) + " · AFMF " + Tx.SiNo(g.F.Afmf.Si) + " · DLSS 5 " + (g.F.DlssNr.Si ? Tx.T("sí (no oficial)", "yes (unofficial)") : "no"));
            foreach (PantallaInfo pa in hw.Pantallas)
                sb.AppendLine(Tx.T("Pantalla: ", "Display: ") + pa.Nombre + " · " + pa.Ancho + "x" + pa.Alto + " @ " + Tx.N(pa.HzExacto, "0.###") + " Hz" + (pa.HdrPosible ? " · HDR" : "") + (pa.VrrPosible ? " · VRR" : ""));
            int nc = 0;
            foreach (CapturadoraInfo c in hw.Capturadoras) if (c.Tipo != TipoCaptura.Virtual) { nc++; sb.AppendLine(Tx.T("Capturadora: ", "Capture: ") + c.Nombre + " [" + Hardware.Tipo(c.Tipo) + "]"); }
            if (nc == 0) sb.AppendLine(Tx.T("Capturadora: ninguna. Conéctala (o instala su driver) y vuelve a abrir el asistente.", "Capture device: none. Plug it in and run the wizard again."));
            foreach (MandoInfo m in hw.Mandos) sb.AppendLine(Tx.T("Mando: ", "Controller: ") + m.Nombre + " [" + m.Origen + "]");
            if (hw.Mandos.Count == 0) sb.AppendLine(Tx.T("Mandos: ninguno conectado ahora", "Controllers: none connected now"));
            sb.AppendLine();
            foreach (string a in p.Avisos) sb.AppendLine("! " + a);
            Caja(sb.ToString(), 16, 380);
            BotonCuerpo(Tx.T("Ver todo el detalle", "Show full details"), 16, 410, delegate { MessageBox.Show(this, hw.Informe(), "ConsoleEnhancer"); });
        }

        void PasoCaptura()
        {
            Cabecera(Tx.T("Capturadora", "Capture device"), Tx.T("Elígela; se abre para leer sus formatos. «Probar imagen» la enseña unos segundos (con la consola apagada saldrá su pantalla sin señal).",
                                                                  "Pick it; it is opened to read its formats. \"Test picture\" shows it for a few seconds."));
            ComboBox lc = Lista(16);
            List<CapturadoraInfo> cs = new List<CapturadoraInfo>();
            foreach (CapturadoraInfo c in hw.Capturadoras) { cs.Add(c); lc.Items.Add(c.Nombre + "  [" + Hardware.Tipo(c.Tipo) + "]"); }
            if (cs.Count == 0) { Etiqueta(Tx.T("No hay ninguna capturadora. Puedes seguir y elegirla más tarde (menú de la bandeja > Asistente).", "No capture device. You can continue and pick it later."), 60, 60); return; }
            ComboBox lf = Lista(80);
            Label info = Etiqueta("", 124, 90);
            List<FormatoCaptura> fs = new List<FormatoCaptura>();
            EventHandler formatos = null;
            formatos = delegate
            {
                lf.Items.Clear(); fs.Clear();
                if (captura == null) return;
                if (captura.Formatos == null && soloVer) { info.Text = Tx.T("(vista previa: sin abrir la capturadora)", "(preview: capture not opened)"); return; }
                if (captura.Formatos == null)
                {
                    estado.Text = Tx.T("Leyendo los formatos de la capturadora…", "Reading the capture formats…");
                    CapturadoraInfo c = captura;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        string err = DetectorCapturadoras.LeerFormatos(c, hw.Audios, 10000);
                        try { BeginInvoke((MethodInvoker)delegate { estado.Text = err.Length > 0 ? Tx.T("No se pudo abrir: ", "Could not open: ") + err : ""; if (c == captura && paso == 2) formatos(null, null); }); } catch { }
                    });
                    return;
                }
                PlanImagen p = Plan();
                foreach (FormatoCaptura f in captura.Formatos)
                {
                    if (f.Alto < 480 || f.FpsMax < 29) continue;
                    fs.Add(f); lf.Items.Add(f.ToString() + (f.Comprimido ? Tx.T("  (comprimido)", "  (compressed)") : ""));
                }
                int sel = fs.FindIndex(delegate(FormatoCaptura f) { return formato != null ? f == formato : f.Ancho == p.CapW && f.Alto == p.CapH && f.Subtipo == p.CapFormato; });
                if (lf.Items.Count > 0) lf.SelectedIndex = sel >= 0 ? sel : 0;
                info.Text = (captura.Modelo != null ? captura.Modelo.Marca + " " + captura.Modelo.Modelo + (captura.Modelo.Notas.Length > 0 ? " · " + captura.Modelo.Notas : "") + "\r\n" : "") +
                            Tx.T("Recomendado: ", "Recommended: ") + p.CapW + "x" + p.CapH + " " + p.CapFormato + " " + p.CapFps;
            };
            lf.SelectedIndexChanged += delegate { formato = lf.SelectedIndex >= 0 && lf.SelectedIndex < fs.Count ? fs[lf.SelectedIndex] : null; };
            lc.SelectedIndexChanged += delegate { captura = cs[lc.SelectedIndex]; formato = null; audio = null; formatos(null, null); };
            lc.SelectedIndex = Math.Max(0, cs.IndexOf(captura));
            BotonCuerpo(Tx.T("Probar imagen", "Test picture"), 16, 230, delegate { ProbarImagen(); });
        }

        void PasoAudio()
        {
            Cabecera(Tx.T("Sonido de la consola", "Console sound"), Tx.T("La entrada por la que llega el sonido. «Probar sonido» la oyes unos segundos por los altavoces de Windows.",
                                                                         "The input the console sound comes from. \"Test sound\" plays it for a few seconds."));
            ComboBox la = Lista(16);
            List<AudioEntrada> op = new List<AudioEntrada>();
            if (captura != null && captura.Audio != null) { op.Add(captura.Audio); la.Items.Add(captura.Audio.Nombre + "  [" + captura.Audio.Origen + "] · " + captura.AudioMotivo); }
            foreach (AudioEntrada a in hw.Audios) if (!op.Contains(a)) { op.Add(a); la.Items.Add(a.Nombre + "  [" + a.Origen + "]"); }
            op.Add(null); la.Items.Add(Tx.T("Sin sonido de la consola", "No console sound"));
            la.SelectedIndexChanged += delegate { audio = op[la.SelectedIndex]; sinAudio = audio == null; };
            int i = sinAudio ? op.Count - 1 : audio != null ? op.IndexOf(audio) : 0;
            la.SelectedIndex = Math.Max(0, i);
            BotonCuerpo(Tx.T("Probar sonido", "Test sound"), 16, 70, delegate { ProbarSonido(); });
            Etiqueta(Tx.T("Si no se oye nada con la consola encendida, prueba otra entrada. Con el audio en el propio filtro (AVerMedia) la app prueba sola los pines «Audio» y «Audio Mixer».",
                          "If nothing plays with the console on, try another input."), 130, 80).ForeColor = Suave;
        }

        static readonly string[][] ListaConsolas =
        {
            new[] { "auto", "Automática (la deduce de la imagen)", "Automatic (detected from the picture)" },
            new[] { "switch2", "Nintendo Switch 2", "Nintendo Switch 2" }, new[] { "switch1", "Nintendo Switch", "Nintendo Switch" },
            new[] { "ps5", "PlayStation 5", "PlayStation 5" }, new[] { "ps4", "PlayStation 4", "PlayStation 4" },
            new[] { "xboxseries", "Xbox Series X|S", "Xbox Series X|S" }, new[] { "xboxone", "Xbox One", "Xbox One" },
            new[] { "otra", "Otra", "Other" },
        };

        void PasoConsola()
        {
            Cabecera(Tx.T("Consola", "Console"), Tx.T("Decide qué capturas tienen sentido, si se reconoce su menú (HOME de la Switch 2 a 4K nativo), los 120 Hz y los nombres de los botones.",
                                                      "Decides useful capture sizes, menu recognition, 120 Hz and button names."));
            ComboBox lc = Lista(16);
            foreach (string[] c in ListaConsolas) lc.Items.Add(Tx.Ingles ? c[2] : c[1]);
            lc.SelectedIndexChanged += delegate { consola = ListaConsolas[lc.SelectedIndex][0]; };
            int i = Array.FindIndex(ListaConsolas, delegate(string[] c) { return c[0] == consola; });
            lc.SelectedIndex = Math.Max(0, i);
        }

        void PasoPantalla()
        {
            Cabecera(Tx.T("Pantalla", "Display"), Tx.T("Donde se juega (la tele). La imagen sale a su resolución y Hz de ahora.", "Where you play (the TV). The picture uses its current resolution and refresh rate."));
            ComboBox lp = Lista(16);
            foreach (PantallaInfo p in hw.Pantallas) lp.Items.Add(p.Nombre + " · " + p.Ancho + "x" + p.Alto + " @ " + Tx.N(p.HzExacto, "0.###") + " Hz" + (p.Principal ? Tx.T(" · principal", " · primary") : ""));
            Label info = Etiqueta("", 60, 200);
            lp.SelectedIndexChanged += delegate
            {
                pantalla = hw.Pantallas[lp.SelectedIndex];
                PlanImagen pl = Plan();
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine(Tx.T("Máximo: ", "Max: ") + pantalla.AnchoMax + "x" + pantalla.AltoMax + " @ " + pantalla.HzEnMax + " Hz · HDR " + (pantalla.HdrActivo ? Tx.T("encendido", "on") : pantalla.HdrPosible ? Tx.T("posible", "capable") : "no") +
                              " · VRR " + Tx.SiNo(pantalla.VrrPosible) + " · " + pantalla.Conexion);
                foreach (string a in pl.Avisos) if (a.IndexOf("Hz", StringComparison.Ordinal) >= 0 || a.IndexOf("pantalla", StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf("display", StringComparison.OrdinalIgnoreCase) >= 0) sb.AppendLine("! " + a);
                // 22-11 (1.2.2, ERRORES; pendiente de la revision de la 1.2.03 "pantalla elegida en el asistente no se guarda"): la eleccion
                // solo cuenta para el plan; mpv, el motor, Magpie y la cubierta negra van a la pantalla PRINCIPAL de Windows. Que se sepa
                if (!pantalla.Principal)
                    sb.AppendLine("! " + Tx.T("La imagen sale en la pantalla principal de Windows. Para jugar en esta, hazla la principal (Configuración > Sistema > Pantalla > «Convertir en la pantalla principal») y vuelve a abrir el asistente.",
                                              "The picture goes to the Windows primary display. To play on this one, make it the primary display (Settings > System > Display > \"Make this my main display\") and open the wizard again."));
                info.Text = sb.ToString();
            };
            if (hw.Pantallas.Count > 0) lp.SelectedIndex = Math.Max(0, hw.Pantallas.IndexOf(pantalla));
        }

        void PasoResumen()
        {
            PlanImagen p = Plan();
            plan = p;
            LeerOpciones();
            Cabecera(Tx.T("Listo", "Ready"), Tx.T("Esto es lo que va a usar ConsoleEnhancer en este PC. Todo se puede cambiar luego en el ini o con este asistente.", "This is what ConsoleEnhancer will use on this PC."));
            string extra = p.Dlss5Disponible ? "\r\n" + Tx.T("DLSS 5: hay una DLL de NVIDIA modificada por la comunidad, SIN FIRMA y no oficial (nvngx_dlssnr.dll). La app la usa solo si la enciendes (botón " + BotonesCe.NombreBoton(kindMando, PadBtn.A) + "); bajo tu responsabilidad.",
                                                             "DLSS 5: a community-modified, UNSIGNED, unofficial NVIDIA DLL is present (nvngx_dlssnr.dll). Used only if you turn it on (" + BotonesCe.NombreBoton(kindMando, PadBtn.A) + " button); at your own risk.") + "\r\n"
                         : p.Gpu != null && p.Gpu.EsRtx ? "\r\n" + Tx.T("DLSS 5 (opcional, no oficial): no viene con la app. Si tienes tu propio nvngx_dlssnr.dll, ponlo con «Añadir DLSS 5» (abajo o en la bandeja).",
                                                                         "DLSS 5 (optional, unofficial): not included. If you have your own nvngx_dlssnr.dll, add it with \"Add DLSS 5\" (below or in the tray).") + "\r\n" : "";
            Caja(Componentes_() + p.Informe() + extra + ResumenImagen(p), 16, 262);   // (20-11: lo elegido en Imagen y Mando, AsistenteImagen.cs)
            // (19-11) como en Prisma: abrirse al iniciar Windows, el escritorio negro mientras esta abierta y apagar el PC al apagar la
            // consola (con aviso que se puede cancelar)
            CheckBox ci = Casilla(Tx.T("Abrir ConsoleEnhancer al iniciar Windows (pantalla en negro hasta que se ve la imagen)",
                                       "Open ConsoleEnhancer when Windows starts (black screen until the picture shows)"), 286, inicioWin);
            ci.CheckedChanged += delegate { inicioWin = ci.Checked; };
            CheckBox ce = Casilla(Tx.T("Escritorio de Windows en negro mientras está abierta (el normal vuelve al cerrarla)",
                                       "Black Windows desktop while it is open (the normal one comes back when you close it)"), 320, escritorioNegro);
            ce.CheckedChanged += delegate { escritorioNegro = ce.Checked; };
            // 1.0.14: que hace el PC al apagar la consola: nada, la espera (de serie), suspender o apagar; con lo que hace y gasta cada uno
            Label la = new Label(); la.Text = Tx.T("Al apagar la consola:", "When the console is turned off:"); la.ForeColor = Texto; la.BackColor = Panel_;
            la.AutoSize = false; la.TextAlign = ContentAlignment.MiddleLeft; la.SetBounds(S(16), S(354), S(190), S(30)); la.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            cuerpo.Controls.Add(la);
            ComboBox cm = new ComboBox(); cm.DropDownStyle = ComboBoxStyle.DropDownList; cm.FlatStyle = FlatStyle.Flat; cm.BackColor = Fondo; cm.ForeColor = Texto;
            cm.SetBounds(S(210), S(354), S(420), S(30)); cm.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            string[] modos = { "nada", "espera", "suspender", "apagar" };
            foreach (string m in modos) cm.Items.Add(CeEspera.Nombre(m));
            cm.SelectedIndex = !apagarPc ? 0 : Array.IndexOf(modos, modoApagado) > 0 ? Array.IndexOf(modos, modoApagado) : 1;
            cm.SelectedIndexChanged += delegate
            {
                apagarPc = cm.SelectedIndex > 0;
                if (apagarPc) modoApagado = modos[cm.SelectedIndex];
            };
            cuerpo.Controls.Add(cm);
            Button cq = new Button(); cq.Text = Tx.T("¿Cómo funciona y cuánto gasta?", "How it works / power use"); cq.SetBounds(S(640), S(352), S(260), S(34));
            cq.FlatStyle = FlatStyle.Flat; cq.FlatAppearance.BorderColor = Acento; cq.BackColor = Fondo; cq.ForeColor = Texto; cq.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            cq.Click += delegate { MessageBox.Show(this, CeEspera.Explicacion(), "ConsoleEnhancer", MessageBoxButtons.OK, MessageBoxIcon.Information); };
            cuerpo.Controls.Add(cq);
            // (20-11, PUBLICAR) DLSS 5 con el archivo del usuario (solo GeForce RTX; Extras.cs) y, una vez y sin insistir, el enlace de apoyo
            GpuInfo g = p.Gpu;
            int xLink = 16;
            if (g != null && g.EsRtx)
            {
                Button d5 = BotonCuerpo(Extras.HayDlss5 ? Tx.T("Quitar DLSS 5", "Remove DLSS 5") : Tx.T("Añadir DLSS 5 (tu archivo)…", "Add DLSS 5 (your file)…"), 16, 396, delegate
                {
                    if (soloVer) return;
                    if (Extras.HayDlss5) { if (!Extras.Quitar(this)) return; }
                    else
                    {
                        if (!Extras.Anadir(this)) return;
                        Ce.PrepararExtras();   // (1.2.4 ronda 6: ya no copia a magpie\; quita la copia vieja de dlssg_sm86 si la puso la app)
                    }
                    Extras.Redetectar(hw);
                    Ir(paso);
                });
                d5.Width = S(270); d5.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
                xLink = 300;
            }
            LinkLabel ko = new LinkLabel();
            ko.Text = Tx.T("Gratis y completo. ¿Te gusta? Puedes apoyarlo en Ko-fi (opcional)", "Free and complete. Like it? You can support it on Ko-fi (optional)");
            ko.LinkArea = new LinkArea(0, 0);
            int i0 = ko.Text.IndexOf("Ko-fi", StringComparison.Ordinal);
            if (i0 >= 0) ko.LinkArea = new LinkArea(i0, 5);
            ko.LinkColor = Acento; ko.ActiveLinkColor = Texto; ko.ForeColor = Suave; ko.BackColor = Panel_;
            ko.AutoSize = false; ko.SetBounds(S(xLink), S(405), S(900 - xLink), S(28)); ko.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ko.LinkClicked += delegate { if (!soloVer) Apoyo.Abrir(Apoyo.KoFi); };
            cuerpo.Controls.Add(ko);
        }


        // (20-11, PUBLICAR) mpv y Magpie (reescalado y generacion por Magpie): que esten y como volver a bajarlos
        static string Componentes_()
        {
            bool bien;
            string est = Extras.EstadoComponentes(out bien);
            if (est == null) return "";
            return Tx.T("Componentes (mpv, y XeSS de Intel y FSR de AMD para el motor; NVIDIA Streamline / DLSS / Reflex ya vienen en el zip):\r\n",
                        "Components (mpv, and Intel XeSS and AMD FSR for the engine; NVIDIA Streamline / DLSS / Reflex ship in the zip):\r\n") + est +
                   (bien ? "" : "  ! " + Tx.T("Falta algo: bandeja > Componentes: comprobar / volver a descargar\r\n", "Something is missing: tray > Components: check / download again\r\n")) +
                   Tx.T("  (Si algo falla más tarde: icono de la bandeja > Componentes: comprobar / volver a descargar)\r\n\r\n",
                        "  (If something breaks later: tray icon > Components: check / download again)\r\n\r\n");
        }

        // (19-11) las opciones del final: de partida, lo que ya hay (la tarea de inicio de esta copia, apagar_pc y escritorio_negro del ini)
        bool inicioWin, apagarPc, escritorioNegro, opcionesLeidas;
        string modoApagado = "espera";   // (1.0.14: apagar_pc_modo)
        static int AvisoS { get { int s; string v = Ce.IniLeer("apagar_pc_aviso_s"); return v != null && int.TryParse(v, out s) ? Math.Max(0, s) : 15; } }

        void LeerOpciones()
        {
            if (opcionesLeidas) return;
            opcionesLeidas = true;
            apagarPc = Ce.IniLeer("apagar_pc") == "1";
            modoApagado = CeSuspension.Normaliza(Ce.IniLeer("apagar_pc_modo"));
            escritorioNegro = Ce.IniLeer("escritorio_negro") == "1";
            if (soloVer) return;
            string exe;
            string modo = CeInicio.Estado(out exe);
            inicioWin = modo != null ? CeInicio.EsEsteExe(exe) : Ce.IniLeer("iniciar_con_windows") == "1";
        }

        CheckBox Casilla(string t, int y, bool marcada)
        {
            CheckBox c = new CheckBox(); c.Text = t; c.Checked = marcada; c.ForeColor = Texto; c.BackColor = Panel_; c.FlatStyle = FlatStyle.Flat;
            c.AutoSize = false; c.SetBounds(S(16), S(y), S(884), S(32)); c.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            cuerpo.Controls.Add(c); return c;
        }

        PlanImagen Plan()
        {
            PreferenciasPlan pref = new PreferenciasPlan();
            pref.Consola = consola == "auto" ? "" : consola;
            if (pantalla != null) pref.Pantalla = pantalla.Gdi;
            if (captura != null) pref.Capturadora = captura.DevicePath.Length > 0 ? captura.DevicePath : captura.Nombre;
            if (!soloVer) CeImagen.Preferencias(pref, pantalla);   // (20-11: G-SYNC visto / modo global, ImagenCe.cs)
            PlanImagen p = Planificador.Elegir(hw, pref);
            // (1.2.2, UNIVERSAL: Planificador.FormatoElegido: 59,94 solo si su rango lo admite -con 60,000 fijos ffmpeg no la abria- y mpv + Magpie
            // si el motor no abre ese formato)
            if (formato != null) { Planificador.FormatoElegido(p, formato); p.CapOrigen = Tx.T("elegido en el asistente", "chosen in the wizard"); }
            if (sinAudio) p.Audio = null; else if (audio != null) p.Audio = audio;
            return p;
        }

        void Guardar()
        {
            try
            {
                PlanImagen p = plan ?? Plan();
                LeerOpciones();
                Ce.EscribirIni(p, hw, consola, idioma, sinAudio ? null : p.Audio, inicioWin, apagarPc, escritorioNegro, Imagen());
                if (apagarPc) Ce.IniPoner("apagar_pc_modo", modoApagado);
                if (sinAudio) Log.Write("Asistente: sin sonido de la consola");
                GuardarImagen();   // (20-11: RTX HDR: el permiso de administrador de una vez, AsistenteImagen.cs)
                // (19-11) la tarea de inicio ya (si no, la pondria la app al arrancar)
                try
                {
                    string exe, det;
                    string modo = CeInicio.Estado(out exe);
                    bool mio = modo != null && CeInicio.EsEsteExe(exe);
                    if ((inicioWin != mio && (inicioWin || mio)) || (inicioWin && modo == "tarea"))   // (1.2.08: la tarea vieja pasa a acceso directo)
                    {
                        bool ok = CeInicio.Poner(inicioWin, out det);
                        Log.Write("Asistente: iniciar con Windows " + (inicioWin ? "si" : "no") + ": " + det);
                        if (!ok) MessageBox.Show(this, Tx.T("No se pudo cambiar el inicio con Windows: ", "Could not change the start with Windows: ") + det, "ConsoleEnhancer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    Log.Write("Asistente: al apagar la consola, " + (apagarPc ? "apagar_pc=1, apagar_pc_modo=" + modoApagado : "nada (apagar_pc=0)"));
                }
                catch (Exception ex) { Log.Error("Asistente: iniciar con Windows", ex); }
                guardado = true;
                Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "ConsoleEnhancer", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // ------------------------------------------------------------------ pruebas con mpv (8 s)
        string Mpv { get { string m = Path.Combine(Paths.AppDir, @"mpv\mpv.exe"); return File.Exists(m) ? m : null; } }

        void ProbarImagen()
        {
            if (captura == null) return;
            if (Mpv == null) { estado.Text = Tx.T("Falta mpv\\mpv.exe en la carpeta de la app", "mpv\\mpv.exe missing"); return; }
            PlanImagen p = Plan();
            string pf = p.CapFormato == "NV12" ? "nv12" : p.CapFormato == "YUY2" ? "yuyv422" : p.CapFormato == "RGB24" ? "bgr24" : "";
            string lavf = "video_size=" + p.CapW + "x" + p.CapH + ",framerate=" + p.CapFps + (pf.Length > 0 ? ",pixel_format=" + pf : p.CapFormato == "MJPG" ? ",vcodec=mjpeg" : "");
            Lanzar("\"av://dshow:video=" + captura.Nombre + "\" --demuxer-lavf-o=" + lavf + " --no-audio --profile=low-latency --untimed --geometry=60%+50%+50% --title=ConsoleEnhancer-prueba --length=8 --no-input-default-bindings",
                   Tx.T("Probando la imagen 8 s…", "Testing the picture for 8 s…"));
        }

        void ProbarSonido()
        {
            AudioEntrada a = audio ?? (captura != null ? captura.Audio : null);
            if (a == null || sinAudio) return;
            if (Mpv == null) { estado.Text = Tx.T("Falta mpv\\mpv.exe en la carpeta de la app", "mpv\\mpv.exe missing"); return; }
            string pin = a.Origen == "dshow-pin" ? "--demuxer-lavf-o=audio_pin_name=2,sample_rate=48000,channels=2 " : "";
            Lanzar("\"av://dshow:audio=" + a.Nombre + "\" " + pin + "--no-video --length=8 --no-input-default-bindings --force-window=no",
                   Tx.T("Probando el sonido 8 s…", "Testing the sound for 8 s…"));
        }

        void Lanzar(string args, string texto)
        {
            PararPrueba();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Mpv, args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                prueba = Process.Start(psi);
                EngineJob.Hold(prueba, true);   // (22-11, 1.2.2: si el asistente se cierra o muere en esos 8 s, Windows cierra la prueba)
                Process yo = prueba;
                ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(9000); try { if (yo != null && !yo.HasExited) yo.Kill(); } catch { } }); // (con fuentes en vivo --length no siempre vale)
                estado.Text = texto;
                Log.Write("Asistente: mpv " + args);
            }
            catch (Exception ex) { estado.Text = ex.Message; }
        }

        void PararPrueba()
        {
            Process p = prueba; prueba = null;
            try { if (p != null && !p.HasExited) p.Kill(); } catch { }
        }

        // ------------------------------------------------------------------ prueba sin interaccion: una imagen de cada paso
        public static void Preview(string carpeta)
        {
            Directory.CreateDirectory(carpeta);
            OpcionesDeteccion op = Ce.Opciones(false);
            Hardware hw = Hardware.Detectar(op);
            foreach (string lang in new[] { "es", "en" })
            {
                Tx.Ingles = lang == "en"; Idioma.Poner(lang);
                using (Asistente a = new Asistente(hw, true))
                {
                    a.idioma = lang; a.soloVer = true;
                    a.StartPosition = FormStartPosition.Manual; a.Location = new Point(-4000, -4000); a.ShowInTaskbar = false;
                    a.Show();
                    for (int i = 0; i < Pasos; i++)
                    {
                        a.Ir(i);
                        for (int k = 0; k < 5; k++) { Application.DoEvents(); Thread.Sleep(20); }
                        using (Bitmap b = new Bitmap(a.Width, a.Height))
                        {
                            a.DrawToBitmap(b, new Rectangle(0, 0, a.Width, a.Height));
                            b.Save(Path.Combine(carpeta, "asistente_" + lang + "_" + (i + 1) + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    File.WriteAllText(Path.Combine(carpeta, "plan_" + lang + ".txt"), a.Plan().Informe() + "\r\n" + Ce.PlanComoIni(a.Plan(), hw, "auto", lang, null, a.Imagen()), new System.Text.UTF8Encoding(false));
                }
            }
        }
    }
}
