// Desinstalar.cs - en la bandeja (21-11-2026, 1.1.1): "Cómo se usa el PC" (como una consola / como un PC normal) y "Desinstalar".
//
//  - Como una consola (lo del salon): se abre sola al iniciar Windows, escritorio negro con la app abierta y, al apagar la consola, el
//    PC en espera (apagar_pc=1, apagar_pc_modo=espera; src\Lanzador\Espera.cs).
//  - Como un PC normal (Windows de siempre): nada de eso; la app solo se abre cuando la abres tu, el escritorio de siempre y al apagar
//    la consola la app se cierra y el PC sigue como estaba. Se puede volver a la consola cuando se quiera.
//  - Desinstalar: deja Windows como estaba (sin la tarea de inicio ni la entrada de Run, el escritorio normal, el plan de energia de
//    antes si la espera lo dejo cambiado) y borra la app: solo SUS ficheros y carpetas (por si esta descomprimida dentro de Descargas o
//    del escritorio: nunca la carpeta entera a ciegas) y, si se quiere, tus ajustes y perfiles (data\ o %LOCALAPPDATA%\ConsoleEnhancer).
//    1.2.3 (DEFENDER): el borrado lo hace la propia app al final de su proceso (CeBorrado, abajo), sin PowerShell. Hasta la 1.2.2 lo
//    hacia un PowerShell oculto con -ExecutionPolicy Bypass que esperaba a que se cerrara (lo que hace un programa malicioso que se
//    borra solo; los antivirus lo miran con lupa). El exe abierto no se puede borrar pero si mover dentro del mismo disco: va a %TEMP%
//    (ConsoleEnhancer.exe.uninstalled-*.tmp; lo quita la proxima vez que se abra cualquier copia de la app) y la carpeta, si se queda
//    vacia, se borra. Log: %TEMP%\consoleenhancer_desinstalar.log
//    Lo que no se toca: los perfiles de NVIDIA (Smooth Motion / RTX HDR en el perfil de Magpie y mpv: sin la app no hacen nada) y los
//    dispositivos que se armaron para despertar el PC (Windows los trae asi de serie en los teclados).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ConsoleEnhancer
{
    partial class TrayApp
    {
        ToolStripMenuItem ceUsoConsola, ceUsoNormal;

        // (una linea en MenuCe)
        void MenuUsoYDesinstalar(ContextMenuStrip menu)
        {
            ToolStripMenuItem uso = new ToolStripMenuItem(Deteccion.Tx.T("Cómo se usa el PC", "How the PC is used"));
            ceUsoConsola = new ToolStripMenuItem(Deteccion.Tx.T("Como una consola (se abre sola, escritorio negro, modo espera)", "Like a console (opens by itself, black desktop, standby)"), null, delegate { CambiarUso(true); });
            ceUsoNormal = new ToolStripMenuItem(Deteccion.Tx.T("Como un PC normal (Windows de siempre)", "Like a normal PC (regular Windows)"), null, delegate { CambiarUso(false); });
            uso.DropDownItems.Add(ceUsoConsola);
            uso.DropDownItems.Add(ceUsoNormal);
            menu.Items.Add(uso);
            menu.Items.Add(new ToolStripMenuItem(Deteccion.Tx.T("Desinstalar ConsoleEnhancer…", "Uninstall ConsoleEnhancer…"), null, delegate { Desinstalar(); }));
            menu.Opening += delegate
            {
                bool consola = cfg.ShutdownPc && cfg.BlackDesktop;
                ceUsoConsola.Checked = consola;
                ceUsoNormal.Checked = !cfg.ShutdownPc && !cfg.BlackDesktop && cfg.StartWithWindows != 1;
            };
        }

        void CambiarUso(bool consola)
        {
            string txt = consola
                ? Deteccion.Tx.T("COMO UNA CONSOLA\n\n" +
                                 "• ConsoleEnhancer se abre solo al encender el PC, con la pantalla en negro hasta que sale la imagen.\n" +
                                 "• El escritorio de Windows en negro mientras está abierta (vuelve el normal al cerrarla a mano).\n" +
                                 "• Al apagar la consola, el PC se queda en modo espera: pantalla apagada y casi sin gasto; al encender la consola, " +
                                 "un mando o pulsar una tecla vuelve la imagen y se abre la app (lo puedes cambiar a suspender o apagar en " +
                                 "«Al apagar la consola, el PC»).\n\n¿Usar el PC como una consola?",
                                 "LIKE A CONSOLE\n\n" +
                                 "• ConsoleEnhancer opens by itself when the PC starts, with a black screen until the picture shows.\n" +
                                 "• Black Windows desktop while it is open (the normal one comes back when you close it yourself).\n" +
                                 "• When the console is turned off, the PC goes to standby: screen off and almost no power use; turning the console " +
                                 "on, a controller on or pressing a key brings the picture back and opens the app (you can switch to sleep or shut " +
                                 "down in \"When the console is turned off, the PC\").\n\nUse the PC like a console?")
                : Deteccion.Tx.T("COMO UN PC NORMAL (Windows de siempre)\n\n" +
                                 "• ConsoleEnhancer ya no se abre al iniciar Windows: la abres tú cuando quieras.\n" +
                                 "• El escritorio de Windows de siempre.\n" +
                                 "• Al apagar la consola, la app se cierra y el PC sigue encendido como estaba (sin espera, ni suspender, ni apagar).\n\n" +
                                 "Puedes volver al modo consola cuando quieras desde aquí mismo. ¿Usar el PC como un PC normal?",
                                 "LIKE A NORMAL PC (regular Windows)\n\n" +
                                 "• ConsoleEnhancer no longer opens when Windows starts: you open it whenever you want.\n" +
                                 "• The usual Windows desktop.\n" +
                                 "• When the console is turned off, the app closes and the PC stays on as it was (no standby, sleep or shut down).\n\n" +
                                 "You can switch back to console mode here at any time. Use the PC like a normal PC?");
            if (MessageBox.Show(txt, PrismaUi.AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            cfg.ShutdownPc = consola;
            Ce.IniPoner("apagar_pc", consola ? "1" : "0");
            if (consola) { cfg.ShutdownMode = "espera"; CeSuspension.Modo = "espera"; Ce.IniPoner("apagar_pc_modo", "espera"); }
            cfg.BlackDesktop = consola;
            Ce.IniPoner("escritorio_negro", consola ? "1" : "0");
            Log.Write("Como se usa el PC: " + (consola ? "como una consola" : "como un PC normal") + " (desde la bandeja)");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Escritorio.Cambiar(consola); } catch (Exception ex) { Log.Error("Escritorio", ex); }
                string det;
                bool ok = CeInicio.Poner(consola, out det);
                Log.Write("Iniciar con Windows " + (consola ? "si" : "no") + ": " + det);
                if (ok) { Ce.IniPoner("iniciar_con_windows", consola ? "1" : "0"); cfg.StartWithWindows = consola ? 1 : 0; }
                MirarInicio();
            });
        }

        void Desinstalar()
        {
            string app = Paths.AppDir;
            string datos = Paths.Datos;
            bool datosDentro = datos.StartsWith(app.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            string txt = Deteccion.Tx.T(
                "Se va a desinstalar ConsoleEnhancer:\n\n" +
                "• Deja de abrirse con Windows y vuelve el escritorio normal.\n" +
                "• Se cierra y se borran sus archivos de:\n   " + app + "\n" +
                "  (solo los suyos: si hay otras cosas en esa carpeta, se quedan).\n\n" +
                "No se toca nada más del PC ni de la consola. ¿Desinstalar?",
                "ConsoleEnhancer will be uninstalled:\n\n" +
                "• It stops opening with Windows and the normal desktop comes back.\n" +
                "• It closes and its files are deleted from:\n   " + app + "\n" +
                "  (only its own: anything else in that folder stays).\n\n" +
                "Nothing else on the PC or the console is touched. Uninstall?");
            if (MessageBox.Show(txt, PrismaUi.AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            DialogResult borrarDatos = MessageBox.Show(
                Deteccion.Tx.T("¿Borrar también tus ajustes y perfiles de juegos?\n\n" + datos + "\n\nSí = se borra todo. No = se guardan (si vuelves a instalar ConsoleEnhancer, los recupera).",
                               "Also delete your settings and game profiles?\n\n" + datos + "\n\nYes = everything is deleted. No = they are kept (if you install ConsoleEnhancer again, it gets them back)."),
                PrismaUi.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (borrarDatos == DialogResult.Cancel) return;
            bool borrar = borrarDatos == DialogResult.Yes;
            Log.Write("Desinstalar pedido desde la bandeja (" + app + (borrar ? ", con los datos " : ", sin los datos ") + datos + ")");

            // 1) Windows como estaba
            string det;
            CeInicio.Poner(false, out det);
            Log.Write("Desinstalar: inicio con Windows quitado (" + det + ")");
            // (1.2.3: iniciar_con_windows=0 tambien, en los ajustes que se guardan: una copia nueva que los herede no se pone sola en el inicio)
            try { Ce.IniPoner("escritorio_negro", "0"); Ce.IniPoner("apagar_pc", "0"); Ce.IniPoner("iniciar_con_windows", "0"); } catch { }
            cfg.BlackDesktop = false; cfg.ShutdownPc = false; cfg.StartWithWindows = 0;
            try { Escritorio.Cambiar(false); } catch (Exception ex) { Log.Error("Desinstalar: escritorio", ex); }
            CeEspera.PlanDeAntes("desinstalar");

            // 2) Lo que se borra (solo lo de la app): lo borra esta misma app al final, ya cerrada la bandeja (CeBorrado.AlSalir en Main),
            // y entonces dice como ha ido
            if (!CeBorrado.Preparar(app, datos, borrar, true))
                MessageBox.Show(Deteccion.Tx.T("No se pudieron borrar los archivos. Cierra la app y borra la carpeta a mano:\n", "Could not delete the files. Close the app and delete the folder yourself:\n") + app,
                                PrismaUi.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            QuitRequested();
        }
    }

    // ------------------------------------------------------------------ el borrado de la desinstalacion (1.2.3, DEFENDER)
    // Preparar: al pedirlo (bandeja, panel de ajustes). AlSalir: al final del proceso, con todo lo demas ya cerrado (Main tras la bandeja;
    // PanelAjustes.Run tras el panel). Prueba: ConsoleEnhancer.exe --ce-desinstalar-prueba=<1|0> (sin dialogos ni tocar Windows: solo el
    // borrado de ESTA carpeta, ya mismo). Sin PowerShell, scripts ni procesos de ayuda: lo hace este proceso con las funciones de ficheros
    // de .NET. copiaTambien (22-11, 1.2.2, ERRORES; solo desde la bandeja y el panel, nunca en la prueba): con "Sí = se borra todo", tambien
    // la copia de seguridad de %LOCALAPPDATA%\ConsoleEnhancer\backup (Integrado.CopiaSeguridad, 1.2.07). Si no, al volver a instalar la
    // app los heredaba de ahi (ajustes y perfiles "borrados")
    static class CeBorrado
    {
        static List<string> pendiente;
        static string appDir;
        // (el exe abierto, movido a %TEMP% al desinstalar: lo borra la proxima vez que se abra cualquier copia de la app, LimpiarRestos)
        const string Resto = "ConsoleEnhancer.exe.uninstalled-";
        static string LogFile { get { return Path.Combine(Path.GetTempPath(), "consoleenhancer_desinstalar.log"); } }

        public static bool Preparar(string app, string datos, bool borrar, bool copiaTambien)
        {
            bool datosDentro = datos.StartsWith(app.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            List<string> borrarRutas = new List<string>();
            if (borrar && copiaTambien) borrarRutas.Add(Integrado.CopiaDir);
            if (borrar && copiaTambien) borrarRutas.Add(ExtrasGuardados.Dir);   // (1.2.3 MOTOR5: lo que pusiste tu guardado fuera de la app)
            foreach (string n in new string[] { "ConsoleEnhancer.exe", "ConsoleEnhancer.exe.config", "ConsoleEnhancer Settings.exe", "README.txt", "portable.txt", "languages", "licenses", "engine", "resources", "downloads",
                                                 "magpie", "mpv", "scripts", "extras",
                                                 // (1.2.06) los nombres de antes (en espanol)
                                                 "Ajustes de ConsoleEnhancer.exe", "Inicio y apagado.bat", "LEEME.txt", "idiomas", "licencias", "motor", "recursos", "descargas" })
                borrarRutas.Add(Path.Combine(app, n));
            try { foreach (string f in Directory.GetFiles(app, "ConsoleEnhancer.exe.antes-*")) borrarRutas.Add(f); } catch { }
            if (borrar) borrarRutas.Add(datos);
            else if (datosDentro)
            {
                // (portable: los datos se guardan en %LOCALAPPDATA%\ConsoleEnhancer antes de borrar la carpeta: la proxima instalacion los hereda)
                string guardar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConsoleEnhancer");
                try { CopiarSinLogs(datos, guardar); Log.Write("Desinstalar: ajustes y perfiles guardados en " + guardar); borrarRutas.Add(datos); }
                catch (Exception ex) { Log.Error("Desinstalar: guardando los datos (se quedan en la carpeta)", ex); }
            }
            pendiente = borrarRutas;
            appDir = app.TrimEnd('\\');
            Log.Write("Desinstalar: al cerrarse, la app borra sus archivos (" + borrarRutas.Count + " rutas; log en " + LogFile + ")");
            return true;
        }

        // Al final del proceso (Main tras cerrar la bandeja; el panel al cerrarse): si hay un borrado pendiente, se hace y se dice como ha ido
        public static void AlSalir()
        {
            if (pendiente == null) return;
            List<string> quedan = Hacer();
            if (quedan.Count == 0)
            {
                MessageBox.Show(Deteccion.Tx.T("ConsoleEnhancer se ha desinstalado. Gracias por probarlo.", "ConsoleEnhancer has been uninstalled. Thanks for trying it."),
                                PrismaUi.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            List<string> ver = quedan.Count > 8 ? quedan.GetRange(0, 8) : quedan;
            MessageBox.Show(Deteccion.Tx.T("ConsoleEnhancer se ha desinstalado, pero no se pudo borrar esto (bórralo a mano; se abre la carpeta):\n\n",
                                           "ConsoleEnhancer has been uninstalled, but this could not be deleted (delete it yourself; the folder opens):\n\n") +
                            string.Join("\n", ver.ToArray()) + (quedan.Count > ver.Count ? "\n..." : "") + "\n\n" + LogFile,
                            PrismaUi.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            try
            {
                string abrir = Directory.Exists(appDir) ? appDir : Path.GetDirectoryName(appDir);
                if (!string.IsNullOrEmpty(abrir) && Directory.Exists(abrir)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + abrir + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        public static int Prueba(bool borrar)
        {
            if (!Preparar(Paths.AppDir, Paths.Datos, borrar, false)) return 1;
            return Hacer().Count == 0 ? 0 : 1;
        }

        // Lo que no se ha podido borrar (vacio = todo bien)
        static List<string> Hacer()
        {
            List<string> rutas = pendiente;
            pendiente = null;
            List<string> quedan = new List<string>();
            // Desde aqui el log va a %TEMP%: Log.Write volveria a crear la carpeta de la app (y data\logs)
            Log.Hook = delegate(string m)
            {
                try { File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + m + "\r\n", Encoding.UTF8); } catch { }
            };
            Log.Write("Desinstalar: borrando " + rutas.Count + " rutas de " + appDir);
            try { Environment.CurrentDirectory = Path.GetTempPath(); } catch { }   // (con la carpeta de trabajo dentro, no se puede borrar)
            try { Integrado.PararCopia(); } catch { }                             // (la copia de seguridad de cada 2 min, fuera)
            CerrarProcesos(appDir);
            string exe = Path.GetFullPath(System.Reflection.Assembly.GetEntryAssembly().Location);
            foreach (string r in rutas)
                if (!Borrar(r, exe)) quedan.Add(r);
            try
            {
                if (Directory.Exists(appDir) && Directory.GetFileSystemEntries(appDir).Length == 0) { Directory.Delete(appDir, false); Log.Write("Desinstalar: carpeta vacia borrada"); }
            }
            catch (Exception ex) { Log.Write("Desinstalar: la carpeta no se pudo borrar (" + ex.Message + ")"); }
            Log.Write("Desinstalar: hecho" + (quedan.Count > 0 ? "; quedan " + quedan.Count + ": " + string.Join(", ", quedan.ToArray()) : ""));
            return quedan;
        }

        // (1.2.1: tambien un ConsoleEnhancer.exe que quedara de esa carpeta, p. ej. la guarda de la barra de tareas: si no, el exe no se borra)
        static void CerrarProcesos(string app)
        {
            int yo = Process.GetCurrentProcess().Id;
            string dentro = app.TrimEnd('\\') + "\\";
            foreach (string n in new[] { "mpv", "Magpie", "ConsoleEnhancerEngine", "ConsoleEnhancer" })
                foreach (Process p in Process.GetProcessesByName(n))
                    using (p)
                    {
                        try
                        {
                            if (p.Id == yo || !p.MainModule.FileName.StartsWith(dentro, StringComparison.OrdinalIgnoreCase)) continue;
                            p.Kill();
                            p.WaitForExit(5000);
                            Log.Write("Desinstalar: cerrado " + n + " (pid " + p.Id + ")");
                        }
                        catch (Exception ex) { Log.Write("Desinstalar: " + n + " (pid " + p.Id + "): " + ex.Message); }
                    }
        }

        static bool Borrar(string r, string exe)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(r), exe, StringComparison.OrdinalIgnoreCase)) return MoverExe(exe);
                FileAttributes a;
                try { a = File.GetAttributes(r); }
                catch (FileNotFoundException) { return true; }
                catch (DirectoryNotFoundException) { return true; }
                // (un enlace o union de carpeta, p. ej. bin\mpv -> terceros\mpv: solo el enlace, nunca lo de dentro)
                if ((a & FileAttributes.Directory) != 0 && (a & FileAttributes.ReparsePoint) != 0) Directory.Delete(r, false);
                else if ((a & FileAttributes.Directory) != 0) BorrarCarpeta(r);
                else BorrarFichero(r);
                Log.Write("borrado " + r);
                return true;
            }
            catch (Exception ex) { Log.Write("no se pudo borrar " + r + ": " + ex.Message); return false; }
        }

        static void BorrarCarpeta(string d)
        {
            foreach (string f in Directory.GetFiles(d)) BorrarFichero(f);
            foreach (string s in Directory.GetDirectories(d))
            {
                if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) Directory.Delete(s, false);   // (enlace: solo el enlace)
                else BorrarCarpeta(s);
            }
            Reintentar(delegate
            {
                DirectoryInfo di = new DirectoryInfo(d);
                if ((di.Attributes & FileAttributes.ReadOnly) != 0) di.Attributes &= ~FileAttributes.ReadOnly;
                di.Delete(false);
            });
        }

        static void BorrarFichero(string f)
        {
            Reintentar(delegate
            {
                FileAttributes a = File.GetAttributes(f);
                if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly);
                File.Delete(f);
            });
        }

        // (un proceso recien cerrado tarda un poco en soltar sus ficheros)
        static void Reintentar(Action a)
        {
            for (int i = 0; ; i++)
            {
                try { a(); return; }
                catch (IOException) { if (i >= 5) throw; }
                catch (UnauthorizedAccessException) { if (i >= 5) throw; }
                Thread.Sleep(300);
            }
        }

        // El exe de este proceso no se puede borrar mientras esta abierto, pero si mover dentro del mismo disco: a %TEMP% (y ya no queda nada
        // en la carpeta de la app). Si %TEMP% esta en otro disco, se queda (lo dice el aviso del final)
        static bool MoverExe(string exe)
        {
            string a = Path.Combine(Path.GetTempPath(), Resto + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Process.GetCurrentProcess().Id + ".tmp");
            try
            {
                File.Move(exe, a);
                Log.Write("el exe abierto (no se puede borrar mientras se usa) va a " + a + ": se borra la proxima vez que se abra ConsoleEnhancer");
                return true;
            }
            catch (Exception ex) { Log.Write("no se pudo apartar el exe abierto " + exe + ": " + ex.Message); return false; }
        }

        // Al abrir la app: lo que dejo en %TEMP% una desinstalacion anterior (en segundo plano, CeInicio.Sincronizar)
        public static void LimpiarRestos()
        {
            try
            {
                foreach (string f in Directory.GetFiles(Path.GetTempPath(), Resto + "*.tmp"))
                    try { File.Delete(f); Log.Write("Restos de una desinstalacion anterior borrados: " + f); } catch { }
            }
            catch { }
        }

        static void CopiarSinLogs(string de, string a)
        {
            Directory.CreateDirectory(a);
            foreach (string f in Directory.GetFiles(de)) File.Copy(f, Path.Combine(a, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(de))
            {
                string n = Path.GetFileName(d);
                if (n.Equals("logs", StringComparison.OrdinalIgnoreCase) || n.Equals("cache", StringComparison.OrdinalIgnoreCase)) continue;
                CopiarSinLogs(d, Path.Combine(a, n));
            }
        }
    }
}
