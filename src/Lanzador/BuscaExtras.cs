// BuscaExtras.cs - (22-11-2026, 1.2.4) "Añadir DLSS 5" (y el arranque, si falta) busca en ESTE PC los archivos que el usuario ya tenga:
// su nvngx_dlssnr.dll. C# 5. (1.2.4 ronda 6, QUITAFG: ya no su carpeta de runtimes dlssg_sm86, que era para el DLSS FG de Magpie)
// Lo aprobo el usuario. Reglas: solo LOCAL (nada de descargas ni de enlaces), rapido y acotado (como mucho ~4 s, profundidad limitada,
// sin recorrer el disco entero, cancelable) y siempre con su permiso: se le enseña lo encontrado (ruta, tamaño, version, firmado o no
// -FirmaNvidia.cs-) y elige; se copia a extras\ y se guarda como lo demas (ExtrasGuardados.cs). Se busca por nombre de fichero, no por
// programa: Descargas, Escritorio, otras versiones de ConsoleEnhancer al lado, carpetas de Magpie, Archivos de programa (otros programas
// de video o de mods que traigan esos runtimes) y las carpetas de bibliotecas de juegos tipicas de cada disco.
// Para DLSS 5 va primero la que vale para la grafica: la original firmada de NVIDIA solo funciona en RTX 50; en RTX 20/30/40, la
// modificada (sin firma). Prueba sin el PC del usuario: --test-buscar-extras[=<informe>] (carpetas falsas en %TEMP%).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    static class BuscaExtras
    {
        public const string TipoDlss5 = "dlss5";

        public sealed class Hallazgo
        {
            public string Tipo = "", Ruta = "", Version = "", Nota = "";
            public long Bytes;
            public int Firma = FirmaNvidia.SinFirma;
            public DateTime Fecha;
            public bool Vale;          // (DLSS 5: DLL de 64 bits)
            public int Copias = 1;     // (el mismo archivo en mas sitios: se enseña una vez)
            public int Orden;          // (el orden de la carpeta en la busqueda: lo primero, lo mas probable)
        }

        // Una carpeta donde mirar y hasta que profundidad
        public sealed class Raiz { public string Ruta; public int Prof; public Raiz(string r, int p) { Ruta = r; Prof = p; } }

        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
        static string Descargas()
        {
            try
            {
                IntPtr p;
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                { string s = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p); if (!string.IsNullOrEmpty(s)) return s; }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        // Donde mirar, por orden (lo mas probable primero)
        public static List<Raiz> Raices()
        {
            List<Raiz> r = new List<Raiz>();
            r.Add(new Raiz(Descargas(), 4));
            r.Add(new Raiz(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), 4));
            try
            {
                string app = Path.GetFullPath(Paths.AppDir).TrimEnd('\\'), padre = Path.GetDirectoryName(app);
                if (!string.IsNullOrEmpty(padre) && Directory.Exists(padre))
                    foreach (string d in Directory.GetDirectories(padre, "ConsoleEnhancer*")) if (!d.Equals(app, StringComparison.OrdinalIgnoreCase)) r.Add(new Raiz(d, 4));
            }
            catch { }
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            r.Add(new Raiz(Path.Combine(la, "Magpie"), 5));
            r.Add(new Raiz(Path.Combine(la, @"ConsoleEnhancer\extras"), 3));
            foreach (string pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                if (!string.IsNullOrEmpty(pf)) r.Add(new Raiz(pf, 3));
            try
            {
                foreach (DriveInfo di in DriveInfo.GetDrives())
                {
                    if (di.DriveType != DriveType.Fixed || !di.IsReady) continue;
                    string x = di.RootDirectory.FullName;
                    foreach (string g in new[] { @"Program Files (x86)\Steam\steamapps\common", @"Program Files\Steam\steamapps\common", @"SteamLibrary\steamapps\common",
                                                 @"Steam\steamapps\common", @"Program Files\Epic Games", @"Epic Games", @"Games" })
                        r.Add(new Raiz(Path.Combine(x, g), 4));
                }
            }
            catch { }
            return r;
        }

        static readonly string[] Saltar = { "windows", "$recycle.bin", "system volume information", "winsxs", "node_modules", ".git", "__pycache__",
                                            "package cache", "windowsapps", "microsoft", "common files", "windows defender", "temp" };

        // La busqueda: por nombre de fichero, sin seguir uniones ni enlaces, hasta msMax y mientras no se cancele. excluir = la carpeta de la
        // app (lo suyo no cuenta). Devuelve lo encontrado sin repetir (el mismo archivo en varios sitios: una vez, con Copias)
        public static List<Hallazgo> Buscar(List<Raiz> raices, int msMax, Func<bool> cancelado, string excluir, out string resumen)
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<Hallazgo> todo = new List<Hallazgo>();
            HashSet<string> vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int carpetas = 0, orden = 0;
            bool corte = false;
            string exc = string.IsNullOrEmpty(excluir) ? null : Path.GetFullPath(excluir).TrimEnd('\\') + "\\";
            foreach (Raiz raiz in raices)
            {
                if (corte) break;
                if (string.IsNullOrEmpty(raiz.Ruta) || !Directory.Exists(raiz.Ruta)) continue;
                Stack<KeyValuePair<string, int>> pila = new Stack<KeyValuePair<string, int>>();
                pila.Push(new KeyValuePair<string, int>(raiz.Ruta, 0));
                while (pila.Count > 0)
                {
                    if (sw.ElapsedMilliseconds > msMax || (cancelado != null && cancelado())) { corte = true; break; }
                    KeyValuePair<string, int> kv = pila.Pop();
                    string d = kv.Key;
                    string full;
                    try { full = Path.GetFullPath(d).TrimEnd('\\'); } catch { continue; }
                    if (!vistas.Add(full)) continue;
                    if (exc != null && (full + "\\").StartsWith(exc, StringComparison.OrdinalIgnoreCase)) continue;
                    carpetas++;
                    orden++;
                    try
                    {
                        string f = Path.Combine(full, Integrado.Dlss5Dll);
                        if (File.Exists(f)) todo.Add(Dlss5(f, orden));
                    }
                    catch { }
                    if (kv.Value >= raiz.Prof) continue;
                    string[] subs;
                    try { subs = Directory.GetDirectories(full); } catch { continue; }
                    for (int i = subs.Length - 1; i >= 0; i--)
                    {
                        string n = Path.GetFileName(subs[i]).ToLowerInvariant();
                        if (Array.IndexOf(Saltar, n) >= 0) continue;
                        try
                        {
                            FileAttributes a = File.GetAttributes(subs[i]);
                            if ((a & FileAttributes.ReparsePoint) != 0) continue;   // (uniones y enlaces: ni bucles ni otro disco)
                            if ((a & FileAttributes.System) != 0 && (a & FileAttributes.Hidden) != 0) continue;
                        }
                        catch { continue; }
                        pila.Push(new KeyValuePair<string, int>(subs[i], kv.Value + 1));
                    }
                }
            }
            // sin repetir: el mismo archivo (tamaño y fecha) se queda con el primero
            List<Hallazgo> r = new List<Hallazgo>();
            foreach (Hallazgo h in todo)
            {
                Hallazgo igual = r.Find(delegate(Hallazgo x) { return x.Tipo == h.Tipo && x.Bytes == h.Bytes && x.Fecha == h.Fecha; });
                if (igual != null) igual.Copias++; else r.Add(h);
            }
            resumen = carpetas + " carpetas en " + sw.ElapsedMilliseconds + " ms" + (corte ? " (cortada: tiempo o cancelada)" : "") + ", " + r.Count + " distintos";
            return r;
        }

        static Hallazgo Dlss5(string f, int orden)
        {
            Hallazgo h = new Hallazgo { Tipo = TipoDlss5, Ruta = f, Orden = orden };
            FileInfo fi = new FileInfo(f);
            h.Bytes = fi.Length; h.Fecha = fi.LastWriteTimeUtc;
            try { h.Version = (FileVersionInfo.GetVersionInfo(f).FileVersion ?? "").Replace(',', '.').Replace(" ", ""); } catch { }
            string firmante;
            h.Firma = FirmaNvidia.Rapida(f, out firmante);   // (rapida: sin el hash de 276 MB; la de verdad, al elegirla)
            string mal = Extras.MirarDll(f);
            h.Vale = mal == null;
            if (mal != null) h.Nota = mal;
            return h;
        }

        // Para DLSS 5, primero la que vale para esta grafica (RTX 50: la original firmada; RTX 20/30/40: la modificada, sin firma); luego las
        // que valen, la version mas alta, la mas nueva y la que salio antes en la busqueda
        public static void Ordenar(List<Hallazgo> l, string familia)
        {
            bool rtx50 = (familia ?? "").ToLowerInvariant() == "rtx50";
            l.Sort(delegate(Hallazgo a, Hallazgo b)
            {
                if (a.Tipo != b.Tipo) return a.Tipo == TipoDlss5 ? -1 : 1;
                if (a.Vale != b.Vale) return a.Vale ? -1 : 1;
                if (a.Tipo == TipoDlss5)
                {
                    int pa = Afin(a, rtx50), pb = Afin(b, rtx50);
                    if (pa != pb) return pb.CompareTo(pa);
                }
                int v = Ver(b.Version).CompareTo(Ver(a.Version));
                if (v != 0) return v;
                if (a.Fecha != b.Fecha) return b.Fecha.CompareTo(a.Fecha);
                return a.Orden.CompareTo(b.Orden);
            });
        }
        static int Afin(Hallazgo h, bool rtx50) { return rtx50 ? (h.Firma == FirmaNvidia.Nvidia ? 1 : 0) : (h.Firma == FirmaNvidia.Nvidia ? 0 : 1); }
        static Version Ver(string s) { try { return new Version(s); } catch { return new Version(0, 0); } }

        public static string Familia() { try { CadenaEscala c = new CadenaEscala(); EscalaAuto.GpuPara(c); return c.Familia ?? ""; } catch { return ""; } }

        static string Tam(long b) { return b >= 1 << 20 ? (b / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : (b / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB"; }
        static string FirmaTxt(Hallazgo h)
        {
            return h.Firma == FirmaNvidia.Nvidia ? Tx.T("firmada por NVIDIA (solo RTX 50)", "signed by NVIDIA (RTX 50 only)")
                 : h.Firma == FirmaNvidia.SinFirma ? Tx.T("sin firma (modificada: RTX 20/30/40)", "unsigned (modified: RTX 20/30/40)")
                 : Tx.T("otra firma", "other signature");
        }

        // ---- con ventanas: "Buscando..." (cancelable, como mucho msMax) y la lista para elegir. null = nada elegido; otro = quiere elegir
        // el archivo a mano
        public static Hallazgo Elegir(IWin32Window w, int msMax, out bool otro, out bool cancelado)
        {
            otro = false; cancelado = false;
            List<Hallazgo> l = BuscarConVentana(w, msMax, out cancelado);
            if (cancelado) return null;
            Ordenar(l, Familia());
            List<Hallazgo> d5 = l.FindAll(delegate(Hallazgo h) { return h.Tipo == TipoDlss5; });
            if (d5.Count == 0) { otro = true; return null; }
            return Lista(w, d5, out otro);
        }

        static List<Hallazgo> BuscarConVentana(IWin32Window w, int msMax, out bool cancelado)
        {
            List<Hallazgo> r = new List<Hallazgo>();
            bool cancel = false;
            using (Form f = new Form())
            {
                f.Text = "ConsoleEnhancer"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterScreen; f.ClientSize = new Size(460, 110); f.ShowInTaskbar = false; f.TopMost = true;
                Label t = new Label { Text = Tx.T("Buscando en este PC los archivos que ya tengas (solo en tu PC, unos segundos)…",
                                                  "Looking on this PC for the files you already have (only on your PC, a few seconds)…"),
                                      Location = new Point(14, 14), Size = new Size(432, 40) };
                Button b = new Button { Text = Tx.T("Cancelar", "Cancel"), Location = new Point(350, 66), Size = new Size(96, 30) };
                b.Click += delegate { cancel = true; };
                f.Controls.Add(t); f.Controls.Add(b);
                f.CancelButton = b;
                f.Shown += delegate
                {
                    Thread th = new Thread(delegate ()
                    {
                        string res;
                        try { r = Buscar(Raices(), msMax, delegate { return cancel; }, Paths.AppDir, out res); Log.Write("Añadir DLSS 5: busqueda en el PC: " + res); }
                        catch (Exception ex) { Log.Error("Añadir DLSS 5: busqueda", ex); }
                        try { f.BeginInvoke((MethodInvoker)delegate { f.Close(); }); } catch { }
                    });
                    th.IsBackground = true; th.Name = "Buscar DLSS 5"; th.Start();
                };
                f.ShowDialog(w);
            }
            cancelado = cancel;
            return r;
        }

        static Hallazgo Lista(IWin32Window w, List<Hallazgo> d5, out bool otro)
        {
            otro = false;
            Hallazgo elegido = null;
            bool quiereOtro = false;
            using (Form f = new Form())
            {
                f.Text = Tx.T("Añadir DLSS 5: encontrado en este PC", "Add DLSS 5: found on this PC");
                f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = f.MinimizeBox = false; f.StartPosition = FormStartPosition.CenterScreen;
                f.ClientSize = new Size(860, 360); f.ShowInTaskbar = false; f.TopMost = true;
                Label t = new Label { Location = new Point(12, 10), Size = new Size(836, 36),
                                      Text = Tx.T("Estos archivos ya están en tu PC. Elige cuál copiar a la app (la primera es la que vale para tu gráfica). " +
                                                  "Nada se descarga; es tu archivo y tu responsabilidad.",
                                                  "These files are already on your PC. Pick which one to copy into the app (the first one suits your GPU). " +
                                                  "Nothing is downloaded; it is your file and your responsibility.") };
                ListView lv = new ListView { Location = new Point(12, 50), Size = new Size(836, 210), View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
                lv.Columns.Add(Tx.T("Archivo", "File"), 110); lv.Columns.Add(Tx.T("Carpeta", "Folder"), 380); lv.Columns.Add(Tx.T("Tamaño", "Size"), 70);
                lv.Columns.Add(Tx.T("Versión", "Version"), 90); lv.Columns.Add(Tx.T("Firma", "Signature"), 180);
                foreach (Hallazgo h in d5)
                {
                    ListViewItem it = new ListViewItem(new[] { Integrado.Dlss5Dll, Path.GetDirectoryName(h.Ruta) + (h.Copias > 1 ? "  (+" + (h.Copias - 1) + ")" : ""), Tam(h.Bytes), h.Version,
                                                               FirmaTxt(h) + (h.Vale ? "" : " · " + h.Nota) });
                    it.Tag = h;
                    lv.Items.Add(it);
                }
                if (lv.Items.Count > 0) lv.Items[0].Selected = true;
                Button ok = new Button { Text = Tx.T("Usar este", "Use this one"), Location = new Point(504, 312), Size = new Size(110, 32), Enabled = lv.Items.Count > 0 };
                Button man = new Button { Text = Tx.T("Elegir otro archivo…", "Pick another file…"), Location = new Point(620, 312), Size = new Size(140, 32) };
                Button no = new Button { Text = Tx.T("Cancelar", "Cancel"), Location = new Point(766, 312), Size = new Size(82, 32) };
                ok.Click += delegate { if (lv.SelectedItems.Count > 0) elegido = (Hallazgo)lv.SelectedItems[0].Tag; f.DialogResult = DialogResult.OK; };
                man.Click += delegate { quiereOtro = true; f.DialogResult = DialogResult.OK; };
                no.Click += delegate { f.DialogResult = DialogResult.Cancel; };
                lv.DoubleClick += delegate { if (lv.SelectedItems.Count > 0) { elegido = (Hallazgo)lv.SelectedItems[0].Tag; f.DialogResult = DialogResult.OK; } };
                f.Controls.AddRange(new Control[] { t, lv, ok, man, no });
                f.AcceptButton = ok; f.CancelButton = no;
                if (f.ShowDialog(w) != DialogResult.OK) return null;
            }
            otro = quiereOtro;
            return quiereOtro ? null : elegido;
        }

        // ---- al arrancar, si falta (Ce.Arranque, tras ExtrasGuardados): una vez, con permiso; nunca en pruebas, en el arranque con Windows
        // ni sin NVIDIA. La respuesta se apunta (games\dlss5_buscado.txt): se vuelve a buscar con "Añadir DLSS 5"
        public static void AlArrancar()
        {
            try
            {
                if (Extras.HayDlss5 || Ce.NoNvidia) return;
                foreach (string a in Environment.GetCommandLineArgs())
                    if (a.StartsWith("--datos=", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--simula", StringComparison.OrdinalIgnoreCase) ||
                        a.StartsWith("--prueba", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--test", StringComparison.OrdinalIgnoreCase) ||
                        a.StartsWith("--dry", StringComparison.OrdinalIgnoreCase) || a == "--inicio" || a == "--espera" || a.StartsWith("--ce-", StringComparison.OrdinalIgnoreCase) ||
                        a == "--mando-pc" || a.StartsWith("--ui-", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--componentes", StringComparison.OrdinalIgnoreCase))
                        return;
                string marca = Path.Combine(Paths.Datos, @"games\dlss5_buscado.txt");
                if (File.Exists(marca)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(marca));
                string res;
                List<Hallazgo> l = Buscar(Raices(), 3000, null, Paths.AppDir, out res);
                List<Hallazgo> d5 = l.FindAll(delegate(Hallazgo h) { return h.Tipo == TipoDlss5 && h.Vale; });
                File.WriteAllText(marca, DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " " + res + "\r\n", new UTF8Encoding(false));
                Log.Write("DLSS 5: falta; busqueda en este PC al arrancar: " + res);
                if (d5.Count == 0) return;
                Ordenar(d5, Familia());
                DialogResult q = MessageBox.Show(Tx.T("En este PC ya hay " + d5.Count + " archivo(s) " + Integrado.Dlss5Dll + " (p. ej. en " + Path.GetDirectoryName(d5[0].Ruta) + ").\r\n\r\n" +
                                                      "¿Quieres ver cuáles son y elegir uno para DLSS 5? (No se descarga nada.)",
                                                      "This PC already has " + d5.Count + " " + Integrado.Dlss5Dll + " file(s) (e.g. in " + Path.GetDirectoryName(d5[0].Ruta) + ").\r\n\r\n" +
                                                      "See them and pick one for DLSS 5? (Nothing is downloaded.)"),
                                                 "ConsoleEnhancer", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (q == DialogResult.Yes) Extras.Anadir(null);
            }
            catch (Exception ex) { Log.Error("DLSS 5: busqueda al arrancar", ex); }
        }

        // ---- --test-buscar-extras[=<informe>]: carpetas falsas en %TEMP% (sin el PC del usuario ni ventanas). 0 = bien
        public static int Prueba(string informe)
        {
            StringBuilder sb = new StringBuilder("# Busqueda de los archivos de DLSS 5 en el PC (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")\r\n");
            int fallos = 0;
            string raiz = Path.Combine(Path.GetTempPath(), "ce_test_buscar_" + Process.GetCurrentProcess().Id);
            try
            {
                if (Directory.Exists(raiz)) Directory.Delete(raiz, true);
                // una DLL de 64 bits sin firma (la del motor) y una firmada por NVIDIA (de Streamline), con el nombre de la de DLSS 5
                string sinFirma = Path.Combine(Paths.AppDir, @"engine\PrismaNr.dll"), firmada = Path.Combine(Paths.AppDir, @"engine\sl.common.dll");
                if (!File.Exists(sinFirma) || !File.Exists(firmada)) { sb.AppendLine("FALLO faltan las DLL de prueba en engine\\ (" + sinFirma + ", " + firmada + ")"); fallos++; }
                else
                {
                    Action<string, string> poner = delegate (string de, string rel) { string a = Path.Combine(raiz, rel); Directory.CreateDirectory(Path.GetDirectoryName(a)); File.Copy(de, a, true); };
                    poner(sinFirma, @"Descargas\cosas\nvngx_dlssnr.dll");                                       // prof 2: se encuentra
                    poner(firmada, @"Juegos\steamapps\common\Un juego\bin\x64\nvngx_dlssnr.dll");               // prof 5: se encuentra (raiz a 5)
                    poner(sinFirma, @"Escritorio\a\b\c\d\e\nvngx_dlssnr.dll");                                  // prof 6 > 4: NO
                    poner(sinFirma, @"Descargas\ConsoleEnhancer-v1.2.2\extras\nvngx_dlssnr.dll");               // la misma: copia (Copias 2)
                    poner(sinFirma, @"App\extras\nvngx_dlssnr.dll");                                            // la de la app: excluida
                    string smd = Path.Combine(raiz, @"Descargas\ConsoleEnhancer-v1.2.2\extras\runtimes\dlssg_sm86");
                    Directory.CreateDirectory(Path.Combine(smd, @"dlssg_sm86\runtime"));
                    File.WriteAllText(Path.Combine(smd, "version.dll"), "x");                                    // dlssg_sm86 (1.2.4 ronda 6: ya no se busca)
                    List<Raiz> rs = new List<Raiz> { new Raiz(Path.Combine(raiz, "Descargas"), 4), new Raiz(Path.Combine(raiz, "Escritorio"), 4),
                                                     new Raiz(Path.Combine(raiz, "Juegos"), 5), new Raiz(Path.Combine(raiz, "App"), 3), new Raiz(Path.Combine(raiz, "no existe"), 3) };
                    string res;
                    List<Hallazgo> l = Buscar(rs, 4000, null, Path.Combine(raiz, "App"), out res);
                    sb.AppendLine("busqueda: " + res);
                    foreach (Hallazgo h in l) sb.AppendLine("  " + h.Tipo + " " + h.Ruta + " " + h.Bytes + " v" + h.Version + " firma " + h.Firma + " vale " + h.Vale + " copias " + h.Copias);
                    List<Hallazgo> d5 = l.FindAll(delegate(Hallazgo h) { return h.Tipo == TipoDlss5; });
                    Func<bool, string, bool> mira = delegate (bool ok, string que) { sb.AppendLine((ok ? "      " : "FALLO ") + que); if (!ok) fallos++; return ok; };
                    mira(d5.Count == 2, "DLSS 5: 2 distintas (la de prof 6 y la de la app no; la copia junta) -> " + d5.Count);
                    mira(d5.Exists(delegate(Hallazgo h) { return h.Copias == 2; }), "la misma en 2 sitios: Copias 2");
                    mira(!d5.Exists(delegate(Hallazgo h) { return h.Ruta.IndexOf(@"\App\", StringComparison.OrdinalIgnoreCase) >= 0 || h.Ruta.IndexOf(@"\e\", StringComparison.OrdinalIgnoreCase) >= 0; }), "ni la de la app ni la de prof 6");
                    mira(l.TrueForAll(delegate(Hallazgo h) { return h.Tipo == TipoDlss5; }), "solo DLSS 5 (dlssg_sm86 ya no se busca)");
                    List<Hallazgo> o = new List<Hallazgo>(d5); Ordenar(o, "rtx50");
                    mira(o.Count == 2 && o[0].Firma == FirmaNvidia.Nvidia, "RTX 50: primero la firmada por NVIDIA");
                    o = new List<Hallazgo>(d5); Ordenar(o, "rtx40");
                    mira(o.Count == 2 && o[0].Firma != FirmaNvidia.Nvidia, "RTX 40: primero la sin firma (modificada)");
                    // cancelada al momento y con tiempo 0: nada
                    List<Hallazgo> c = Buscar(rs, 4000, delegate { return true; }, null, out res);
                    mira(c.Count == 0 && res.Contains("cortada"), "cancelada: nada (" + res + ")");
                    Stopwatch sw = Stopwatch.StartNew();
                    c = Buscar(Raices(), 300, null, Paths.AppDir, out res);
                    mira(sw.ElapsedMilliseconds < 2500, "las carpetas de verdad con 300 ms de tope: " + sw.ElapsedMilliseconds + " ms (" + res + ")");
                }
            }
            catch (Exception ex) { fallos++; sb.AppendLine("FALLO excepcion: " + ex); }
            finally { try { if (Directory.Exists(raiz)) Directory.Delete(raiz, true); } catch { } }
            sb.AppendLine(fallos + " fallos");
            string ruta = string.IsNullOrEmpty(informe) ? Path.Combine(Paths.Datos, @"games\test_buscar_extras.txt") : informe;
            try { Directory.CreateDirectory(Path.GetDirectoryName(ruta)); File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false)); } catch { }
            Console.Write(sb.ToString());
            return fallos;
        }
    }
}
