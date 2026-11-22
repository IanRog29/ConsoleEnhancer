// Extras.cs - (20-11-2026, agente PUBLICAR) lo que ConsoleEnhancer no puede repartir lo pone el usuario con un boton, y el enlace
// voluntario para apoyar el proyecto. C# 5.
//  - "Anadir DLSS 5 (tu propio archivo)..." (bandeja y ultimo paso del asistente): el usuario elige SU nvngx_dlssnr.dll (y, si quiere,
//    su carpeta dlssg_sm86 para DLSS-G en RTX 30). Se comprueba que es una DLL de Windows de 64 bits (cabecera MZ / PE, x64, DLL) con el
//    nombre esperado, se avisa una vez de que no es oficial ni la soporta NVIDIA y se copia a <app>\extras\ (donde la busca la
//    deteccion, OpcionesDeteccion.RutasExtra, y de donde Ce.PrepararExtras la copia a magpie\). La app NO dice de donde sacarla ni
//    enlaza ninguna descarga. "Quitar DLSS 5" lo borra de extras\ (PrepararExtras quita luego la copia de magpie\).
//  - Encender DLSS 5: games\dlss5_on.txt (como el boton A jugando). DLSS 5 dentro del motor propio siempre que junto
//    al motor este PrismaNr.dll (de uso propio, no se publica; 1.2.4: sin preguntar ni clave): sin ella va por mpv + Magpie.
//  - Componentes (mpv, Magpie): comprobar y volver a bajar (--componentes-reparar, Componentes.cs).
//  - Apoyo: Ko-fi, siempre opcional (la app es gratis y completa).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    static class Apoyo
    {
        public const string KoFi = "https://ko-fi.com/ianrog29";
        public const string Repo = "https://github.com/IanRog29/ConsoleEnhancer";
        public const string Releases = "https://github.com/IanRog29/ConsoleEnhancer/releases";

        public static string Linea
        {
            get
            {
                return Tx.T("ConsoleEnhancer es gratis y completo, sin anuncios. Si te es útil y quieres apoyarlo (opcional): " + KoFi,
                            "ConsoleEnhancer is free and complete, with no ads. If you find it useful and want to support it (optional): " + KoFi);
            }
        }

        public static void Abrir(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); Log.Write("Abierto en el navegador: " + url); }
            catch (Exception ex) { Log.Error("Abriendo " + url, ex); MessageBox.Show(url, "ConsoleEnhancer"); }
        }
    }

    static class Extras
    {
        public static string Dir { get { return Path.Combine(Paths.AppDir, "extras"); } }
        public static string Dll { get { return Path.Combine(Dir, Integrado.Dlss5Dll); } }
        public static string Sm86 { get { return Path.Combine(Dir, @"runtimes\dlssg_sm86"); } }
        public static string Sm86Viejo { get { return Path.Combine(Dir, "dlssg_sm86"); } }
        public static string Sm86Puesto { get { return Directory.Exists(Sm86) ? Sm86 : Sm86Viejo; } }
        public static string PrismaNr { get { return Path.Combine(Paths.AppDir, @"engine\PrismaNr.dll"); } }
        public static bool HayDlss5 { get { return File.Exists(Dll); } }
        public static bool HaySm86 { get { return Directory.Exists(Sm86) || Directory.Exists(Sm86Viejo); } }
        public static bool MotorPuede { get { return File.Exists(PrismaNr); } }

        // null = DLL de Windows de 64 bits; si no, el motivo
        public static string MirarDll(string f)
        {
            try
            {
                using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (fs.Length < 4096) return Tx.T("el archivo es demasiado pequeño", "the file is too small");
                    if (br.ReadUInt16() != 0x5A4D) return Tx.T("no es un programa de Windows (sin cabecera MZ)", "not a Windows binary (no MZ header)");
                    fs.Position = 0x3C;
                    int pe = br.ReadInt32();
                    if (pe <= 0 || pe > fs.Length - 64) return Tx.T("cabecera PE rota", "broken PE header");
                    fs.Position = pe;
                    if (br.ReadUInt32() != 0x00004550) return Tx.T("sin cabecera PE", "no PE header");
                    ushort maquina = br.ReadUInt16();
                    fs.Position = pe + 22;
                    ushort carac = br.ReadUInt16();
                    ushort magia = br.ReadUInt16();
                    if (maquina != 0x8664 || magia != 0x20B) return Tx.T("no es de 64 bits (x64)", "not 64-bit (x64)");
                    if ((carac & 0x2000) == 0) return Tx.T("no es una DLL", "not a DLL");
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        static bool Pregunta(IWin32Window w, string t, MessageBoxIcon i)
        {
            return MessageBox.Show(w, t, "ConsoleEnhancer", MessageBoxButtons.YesNo, i) == DialogResult.Yes;
        }

        static void Mal(IWin32Window w, string t) { MessageBox.Show(w, t, "ConsoleEnhancer", MessageBoxButtons.OK, MessageBoxIcon.Warning); }

        // El asistente de "Anadir DLSS 5". true = se ha copiado (1.2.4 SOLOMOTOR: sin la pregunta del motor: con engine\PrismaNr.dll DLSS 5
        // va siempre en el motor, SoloMotor.cs)
        public static bool Anadir(IWin32Window w)
        {
            // el aviso, una vez por cada vez que se anade
            if (!Pregunta(w, Tx.T(
                    "DLSS 5 no es una función oficial.\r\n\r\n" +
                    "Usa nvngx_dlssnr.dll, una DLL de NVIDIA modificada por la comunidad, sin firma y sin licencia, y opcionalmente la carpeta " +
                    "dlssg_sm86 (también de la comunidad). NVIDIA no la ha publicado así ni la soporta, ConsoleEnhancer no la " +
                    "incluye, no la descarga ni dice de dónde sacarla, y su uso puede ir contra las condiciones de NVIDIA.\r\n\r\n" +
                    "Si la usas es bajo tu responsabilidad. La app funciona entera sin ella.\r\n\r\n¿Elegir tu archivo nvngx_dlssnr.dll?",
                    "DLSS 5 is not an official feature.\r\n\r\n" +
                    "It uses nvngx_dlssnr.dll, a community-modified NVIDIA DLL with no signature and no license, and optionally the dlssg_sm86 " +
                    "folder (also from the community). NVIDIA has not released it this way and does not support it; " +
                    "ConsoleEnhancer does not include it, download it or say where to get it, and using it may breach NVIDIA's terms.\r\n\r\n" +
                    "Using it is your own responsibility. The app works fully without it.\r\n\r\nPick your nvngx_dlssnr.dll file?"), MessageBoxIcon.Warning))
                return false;
            string origen;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = Tx.T("Tu archivo nvngx_dlssnr.dll", "Your nvngx_dlssnr.dll file");
                d.Filter = "nvngx_dlssnr.dll|nvngx_dlssnr.dll|DLL (*.dll)|*.dll";
                d.CheckFileExists = true;
                d.RestoreDirectory = true;
                if (d.ShowDialog(w) != DialogResult.OK) return false;
                origen = d.FileName;
            }
            if (!Path.GetFileName(origen).Equals(Integrado.Dlss5Dll, StringComparison.OrdinalIgnoreCase))
            {
                Mal(w, Tx.T("El archivo tiene que llamarse " + Integrado.Dlss5Dll + " (es: " + Path.GetFileName(origen) + ").",
                            "The file must be named " + Integrado.Dlss5Dll + " (it is: " + Path.GetFileName(origen) + ")."));
                return false;
            }
            string mal = MirarDll(origen);
            if (mal != null) { Mal(w, Tx.T("No vale como " + Integrado.Dlss5Dll + ": ", "Not valid as " + Integrado.Dlss5Dll + ": ") + mal); return false; }
            try
            {
                Directory.CreateDirectory(Dir);
                if (!Path.GetFullPath(origen).Equals(Path.GetFullPath(Dll), StringComparison.OrdinalIgnoreCase))
                {
                    string tmp = Dll + ".parcial";
                    File.Copy(origen, tmp, true);
                    if (File.Exists(Dll)) File.Delete(Dll);
                    File.Move(tmp, Dll);
                }
                Log.Write("DLSS 5: el usuario ha puesto su " + Integrado.Dlss5Dll + " en extras\\ (" + new FileInfo(Dll).Length + " bytes; DLL de NVIDIA modificada por la comunidad, SIN FIRMA, no oficial)");
            }
            catch (Exception ex)
            {
                Log.Error("DLSS 5: copiando a extras\\", ex);
                Mal(w, Tx.T("No se pudo copiar a ", "Could not copy to ") + Dir + ":\r\n" + ex.Message);
                return false;
            }
            // opcional: la carpeta dlssg_sm86 (1.2.02: con DLSS 5, el DLSS FG de Magpie la necesita en RTX 30 y tambien en RTX 40/50).
            // 21-11 (1.2.06, el usuario: "sigue apareciendo lo de si tengo la carpeta de dlss fg y te habia dicho que quitaras esa pregunta"):
            // ya no se pregunta. Si esta junto a la DLL elegida (o una carpeta mas arriba) se copia sola; si no, se sigue sin ella (otra
            // generacion; el menu de ajustes dice "DLSS FG · Falta dlssg_sm86" y donde ponerla)
            if (!HaySm86)
            {
                string sm = BuscarSm86(origen);
                if (sm != null) PonerSm86(w, sm);
            }
            // (22-11, 1.2.3 MOTOR5) y PrismaNr.dll ("DLSS 5" en el motor propio, de uso propio) si esta junto a la DLL elegida: a engine\
            { string pn = BuscarPrismaNr(origen); if (pn != null) PonerPrismaNr(w, pn); }
            // (1.2.3 MOTOR5) lo puesto, tambien fuera de la carpeta de la app: una version nueva lo recoge sola (ExtrasGuardados.cs)
            ExtrasGuardados.Guardar("añadido");
            // encenderlo
            bool on = Pregunta(w, Tx.T("¿Encender DLSS 5 ya? (Jugando se enciende y se apaga con el botón A del mando.)",
                                       "Turn DLSS 5 on now? (While playing, the controller's A button turns it on and off.)"), MessageBoxIcon.Question);
            PonerActivo(on);
            Integrado.Dlss5Rever();
            return true;
        }

        // dlssg_sm86 junto a la DLL elegida: en su carpeta o hasta dos mas arriba (X\dlssg_sm86 o X\runtimes\dlssg_sm86). null = no esta
        static string BuscarSm86(string dll)
        {
            try
            {
                string x = Path.GetDirectoryName(Path.GetFullPath(dll));
                for (int i = 0; i < 3 && !string.IsNullOrEmpty(x); i++, x = Path.GetDirectoryName(x))
                {
                    if (Path.GetFileName(x.TrimEnd('\\')).Equals("dlssg_sm86", StringComparison.OrdinalIgnoreCase)) return x;
                    foreach (string c in new[] { Path.Combine(x, "dlssg_sm86"), Path.Combine(x, @"runtimes\dlssg_sm86") })
                        if (Directory.Exists(c) && Directory.GetFiles(c, "*.dll", SearchOption.AllDirectories).Length > 0) return c;
                }
            }
            catch { }
            return null;
        }

        // (22-11, 1.2.3 MOTOR5) PrismaNr.dll junto a la DLL elegida, como BuscarSm86: en su carpeta o hasta dos mas arriba (X\PrismaNr.dll o
        // X\engine\PrismaNr.dll). null = no esta. (Solo la busca: PonerPrismaNr mira que valga)
        public static string BuscarPrismaNr(string dll)
        {
            try
            {
                string x = Path.GetDirectoryName(Path.GetFullPath(dll));
                for (int i = 0; i < 3 && !string.IsNullOrEmpty(x); i++, x = Path.GetDirectoryName(x))
                    foreach (string c in new[] { Path.Combine(x, "PrismaNr.dll"), Path.Combine(x, @"engine\PrismaNr.dll") })
                        if (File.Exists(c)) return c;
            }
            catch { }
            return null;
        }

        // A engine\ si es una DLL de 64 bits y no es la misma que ya hay (sin preguntar; solo avisa si no se puede: el motor abierto la tiene cargada)
        static void PonerPrismaNr(IWin32Window w, string de)
        {
            string m = MirarDll(de);
            if (m != null) { Log.Write("PrismaNr.dll junto a la DLL no vale (" + m + "): no se copia"); return; }
            try
            {
                if (Path.GetFullPath(de).Equals(Path.GetFullPath(PrismaNr), StringComparison.OrdinalIgnoreCase)) return;
                FileInfo a = new FileInfo(de), b = new FileInfo(PrismaNr);
                if (b.Exists && a.Length == b.Length && a.LastWriteTimeUtc == b.LastWriteTimeUtc) return;
                Directory.CreateDirectory(Path.GetDirectoryName(PrismaNr));
                string tmp = PrismaNr + ".parcial";
                File.Copy(de, tmp, true);
                if (File.Exists(PrismaNr)) File.Delete(PrismaNr);
                File.Move(tmp, PrismaNr);
                Log.Write("DLSS 5 en el motor propio: PrismaNr.dll de " + de + " puesta en engine\\ (" + a.Length + " bytes; de uso propio, no se distribuye)");
            }
            catch (Exception ex)
            {
                Log.Error("PrismaNr.dll: copiando a engine\\", ex);
                Mal(w, Tx.T("No se pudo poner PrismaNr.dll en engine\\ (si el motor está abierto, cierra la app y vuelve a añadir DLSS 5):\r\n",
                            "Could not put PrismaNr.dll in engine\\ (if the engine is open, close the app and add DLSS 5 again):\r\n") + ex.Message);
            }
        }

        // Copia la carpeta dlssg_sm86 encontrada a extras\runtimes\ (sin preguntar; solo avisa si algo no vale)
        static void PonerSm86(IWin32Window w, string de)
        {
            string[] dlls = Directory.GetFiles(de, "*.dll", SearchOption.AllDirectories);
            if (dlls.Length == 0) return;
            foreach (string d in dlls)
            {
                string m = MirarDll(d);
                if (m != null) { Log.Write("dlssg_sm86 junto a la DLL no vale (" + Path.GetFileName(d) + ": " + m + "): no se copia"); return; }
            }
            // (1.2.08) otra version de dlssg_for_sm86: Magpie no la valida (DLSS FG no arranca) -> no se copia; la app pone la oficial
            string otra = Componentes.Sm86Distinto(de);
            if (otra != null) { Log.Write("dlssg_sm86 junto a la DLL (" + de + ") no es la version que espera Magpie (" + otra + "): no se copia; la app usa la oficial"); return; }
            try
            {
                if (Path.GetFullPath(de).TrimEnd('\\').Equals(Path.GetFullPath(Sm86).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                QuitarSm86();
                int n = Copiar(de, Sm86);
                Log.Write("DLSS-G en RTX 30: el usuario ha puesto su dlssg_sm86 en extras\\runtimes\\ (" + n + " ficheros; de la comunidad, DLL autofirmadas, no oficial)");
            }
            catch (Exception ex) { Log.Error("dlssg_sm86: copiando a extras\\", ex); Mal(w, ex.Message); }
        }

        static int Copiar(string de, string a)
        {
            int n = 0;
            Directory.CreateDirectory(a);
            foreach (string f in Directory.GetFiles(de)) { File.Copy(f, Path.Combine(a, Path.GetFileName(f)), true); n++; }
            foreach (string d in Directory.GetDirectories(de)) n += Copiar(d, Path.Combine(a, Path.GetFileName(d)));
            return n;
        }

        // Fuera de extras\ (y la copia de magpie\ que puso la app, con su marca: si Magpie la tiene abierta, se quita al volver a abrir la app)
        static void QuitarSm86()
        {
            foreach (string d in new[] { Sm86, Sm86Viejo }) if (Directory.Exists(d)) Directory.Delete(d, true);
            string mag = Path.Combine(Paths.MagpieDir, @"runtimes\dlssg_sm86");
            try { if (File.Exists(mag + ".de_extras")) { if (Directory.Exists(mag)) Directory.Delete(mag, true); File.Delete(mag + ".de_extras"); } } catch { }
        }

        public static void PonerActivo(bool on)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Paths.Dlss5OnFile));
                File.WriteAllText(Paths.Dlss5OnFile, on ? "1" : "0");
                Log.Write("DLSS 5 " + (on ? "encendido" : "apagado") + " al anadirlo");
            }
            catch (Exception ex) { Log.Error("DLSS 5 activo", ex); }
        }

        // true = quitado
        public static bool Quitar(IWin32Window w)
        {
            if (!HayDlss5 && !HaySm86) return false;
            if (!Pregunta(w, Tx.T("¿Quitar DLSS 5? Se borran de la carpeta extras\\ de la app " + Integrado.Dlss5Dll + (HaySm86 ? " y dlssg_sm86" : "") +
                                  " (tu archivo original no se toca).", "Remove DLSS 5? " + Integrado.Dlss5Dll + (HaySm86 ? " and dlssg_sm86 are" : " is") +
                                  " deleted from the app's extras\\ folder (your original file is not touched)."), MessageBoxIcon.Question))
                return false;
            try
            {
                if (File.Exists(Dll)) File.Delete(Dll);
                QuitarSm86();
                string mag = Path.Combine(Paths.MagpieDir, Integrado.Dlss5Dll);
                try { if (File.Exists(mag + ".de_extras")) { if (File.Exists(mag)) File.Delete(mag); File.Delete(mag + ".de_extras"); } } catch { }
                Integrado.Dlss5Rever();
                Log.Write("DLSS 5 quitado por el usuario (extras\\)");
                ExtrasGuardados.QuitarDlss5();   // (1.2.3 MOTOR5: y de lo guardado fuera de la app: una version nueva ya no lo recoge)
                if (File.Exists(mag) && !File.Exists(mag + ".de_extras"))
                    Mal(w, Tx.T("Hay otra " + Integrado.Dlss5Dll + " puesta a mano en " + Paths.MagpieDir + ": bórrala tú si tampoco la quieres.",
                                "Another " + Integrado.Dlss5Dll + " was put by hand in " + Paths.MagpieDir + ": delete it yourself if you do not want it either."));
                return true;
            }
            catch (Exception ex) { Log.Error("Quitando DLSS 5", ex); Mal(w, ex.Message); return false; }
        }

        // Lo que tiene la GPU en la deteccion ya hecha (el asistente): como si se hubiera detectado con o sin la DLL
        public static void Redetectar(Hardware hw)
        {
            foreach (GpuInfo g in hw.Gpus)
            {
                if (!g.EsRtx) continue;
                if (!HayDlss5) { g.F.DlssNr.Pon(false, Tx.T("no está nvngx_dlssnr.dll (extra no oficial: no va en la app)", "nvngx_dlssnr.dll not present (unofficial extra)")); g.F.DlssNr.NoOficial = false; }
                else if (g.VramGb < 6) g.F.DlssNr.Pon(false, Tx.T("menos de 6 GB de VRAM", "under 6 GB VRAM"));
                else { g.F.DlssNr.Pon(true, Tx.T("DLL del usuario (sin firma, no oficial): ", "user DLL (unsigned, unofficial): ") + Dll); g.F.DlssNr.NoOficial = true; }
            }
        }

        // ---- componentes: lo que dice el asistente y la bandeja ----
        // Una linea por componente (mpv, Magpie): instalado / otra version / falta. null si no hay tabla
        public static string EstadoComponentes(out bool todoBien)
        {
            todoBien = true;
            try
            {
                if (!File.Exists(Componentes.Tabla)) return null;
                StringBuilder sb = new StringBuilder();
                foreach (Componente c in Componentes.Leer(Componentes.Tabla))
                {
                    if (!c.AlAbrir) continue;
                    int e = Componentes.Estado(c, Paths.AppDir);
                    if (e != 0) todoBien = false;
                    sb.Append("  •  ").Append(c.Titulo).Append(" ").Append(c.Version).Append(": ")
                      .Append(e == 0 ? Tx.T("instalado", "installed") : e == 1 ? Tx.T("de otra versión", "other version") : Tx.T("falta", "missing"))
                      .Append(" (").Append(c.Descripcion).Append(")\r\n");
                }
                return sb.ToString();
            }
            catch (Exception ex) { Log.Error("Componentes: estado", ex); return null; }
        }
    }

    partial class TrayApp
    {
        // (una linea en el constructor del menu, tras MenuAcercaDe): componentes, DLSS 5 y apoyo
        void MenuExtras(ContextMenuStrip menu)
        {
            menu.Items.Add(Tx.T("Componentes: comprobar / volver a descargar", "Components: check / download again"), null, delegate { MenuComponentes(); });
            ToolStripMenuItem d5 = new ToolStripMenuItem("", null, delegate
            {
                if (!Extras.Anadir(null)) return;
                Ce.PrepararExtras();
                bool asis = MessageBox.Show(Tx.T("DLSS 5 añadido. La app se vuelve a abrir para usarlo.\r\n\r\n¿Abrir antes el asistente para que ajuste DLSS 5 a tu gráfica? (recomendado)",
                                                 "DLSS 5 added. The app reopens to use it.\r\n\r\nOpen the setup wizard first so it tunes DLSS 5 for your GPU? (recommended)"),
                                            "ConsoleEnhancer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                if (asis) AbrirAsistente(); else CeReabrir();
            });
            ToolStripMenuItem q5 = new ToolStripMenuItem(Tx.T("Quitar DLSS 5", "Remove DLSS 5"), null, delegate
            {
                if (Extras.Quitar(null)) CeReabrir();
            });
            ToolStripMenuItem ko = new ToolStripMenuItem(Tx.T("Apoyar el proyecto (Ko-fi)", "Support the project (Ko-fi)"), null, delegate { Apoyo.Abrir(Apoyo.KoFi); });
            menu.Items.Add(d5);
            menu.Items.Add(q5);
            menu.Items.Add(ko);
            Action marcar = delegate
            {
                d5.Text = Extras.HayDlss5 ? Tx.T("Cambiar el archivo de DLSS 5…", "Change the DLSS 5 file…") : Tx.T("Añadir DLSS 5 (tu propio archivo)…", "Add DLSS 5 (your own file)…");
                q5.Visible = Extras.HayDlss5 || Extras.HaySm86;
            };
            marcar();
            menu.Opening += delegate { marcar(); };
        }

        void MenuComponentes()
        {
            bool bien;
            string est = Extras.EstadoComponentes(out bien);
            if (est == null) { MessageBox.Show(Tx.T("No está scripts\\components.json en la carpeta de la app.", "scripts\\components.json is not in the app folder."), "ConsoleEnhancer"); return; }
            string t = Tx.T("Programas libres que usa ConsoleEnhancer (se bajan de sus páginas oficiales, con su SHA-256 comprobado):\r\n\r\n",
                            "Free programs ConsoleEnhancer uses (downloaded from their official pages, SHA-256 checked):\r\n\r\n") + est + "\r\n" +
                       Tx.T("NVIDIA Streamline / DLSS / Reflex (engine\\): vienen en el zip.\r\n\r\n", "NVIDIA Streamline / DLSS / Reflex (engine\\): included in the zip.\r\n\r\n") +
                       (bien ? Tx.T("Todo está bien. ¿Volver a descargarlos igualmente (repara una instalación dañada)? La app se cierra y se vuelve a abrir.",
                                    "Everything is fine. Download them again anyway (repairs a damaged install)? The app closes and reopens.")
                             : Tx.T("Falta algo o es de otra versión. ¿Descargarlo ahora? La app se cierra y se vuelve a abrir.",
                                    "Something is missing or outdated. Download it now? The app closes and reopens."));
            if (MessageBox.Show(t, "ConsoleEnhancer", MessageBoxButtons.YesNo, bien ? MessageBoxIcon.Information : MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                string exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                ProcessStartInfo psi = new ProcessStartInfo(exe, (bien ? "--componentes-reparar " : "") + "--esperar-salida");
                psi.UseShellExecute = false;
                Process.Start(psi);
                Log.Write("Componentes pedidos desde la bandeja (" + (bien ? "volver a bajar" : "faltan") + "): la app se cierra y se abre otra vez");
                QuitRequested();
            }
            catch (Exception ex) { Log.Error("Componentes desde la bandeja", ex); }
        }
    }
}
