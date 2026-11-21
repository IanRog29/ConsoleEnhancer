// Desinstalar.cs - en la bandeja (21-11-2026, 1.1.1): "Cómo se usa el PC" (como una consola / como un PC normal) y "Desinstalar".
//
//  - Como una consola (lo del salon): se abre sola al iniciar Windows, escritorio negro con la app abierta y, al apagar la consola, el
//    PC en espera (apagar_pc=1, apagar_pc_modo=espera; src\Lanzador\Espera.cs).
//  - Como un PC normal (Windows de siempre): nada de eso; la app solo se abre cuando la abres tu, el escritorio de siempre y al apagar
//    la consola la app se cierra y el PC sigue como estaba. Se puede volver a la consola cuando se quiera.
//  - Desinstalar: deja Windows como estaba (sin la tarea de inicio ni la entrada de Run, el escritorio normal, el plan de energia de
//    antes si la espera lo dejo cambiado) y borra la app: solo SUS ficheros y carpetas (por si esta descomprimida dentro de Descargas o
//    del escritorio: nunca la carpeta entera a ciegas) y, si se quiere, tus ajustes y perfiles (data\ o %LOCALAPPDATA%\ConsoleEnhancer).
//    El exe no se puede borrar a si mismo: un PowerShell oculto espera a que la app se cierre y borra (log en %TEMP%).
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
            try { Ce.IniPoner("escritorio_negro", "0"); Ce.IniPoner("apagar_pc", "0"); } catch { }
            cfg.BlackDesktop = false; cfg.ShutdownPc = false;
            try { Escritorio.Cambiar(false); } catch (Exception ex) { Log.Error("Desinstalar: escritorio", ex); }
            CeEspera.PlanDeAntes("desinstalar");

            // 2) Lo que se borra al cerrarse (solo lo de la app)
            if (!PrepararBorrado(app, datos, borrar, Process.GetCurrentProcess().Id))
                MessageBox.Show(Deteccion.Tx.T("No se pudieron borrar los archivos. Cierra la app y borra la carpeta a mano:\n", "Could not delete the files. Close the app and delete the folder yourself:\n") + app,
                                PrismaUi.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            MessageBox.Show(Deteccion.Tx.T("ConsoleEnhancer se ha desinstalado. Gracias por probarlo.", "ConsoleEnhancer has been uninstalled. Thanks for trying it."),
                            PrismaUi.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            QuitRequested();
        }

        // El PowerShell oculto que espera a que se cierre el proceso pid y borra lo de la app (y los datos si borrar). Pruebas:
        // ConsoleEnhancer.exe --ce-desinstalar-prueba=<1|0> (sin dialogos ni tocar Windows: solo el borrado de ESTA carpeta)
        public static bool PrepararBorrado(string app, string datos, bool borrar, int pid)
        {
            bool datosDentro = datos.StartsWith(app.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            List<string> borrarRutas = new List<string>();
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
            StringBuilder ps = new StringBuilder();
            ps.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
            ps.AppendLine("$log = Join-Path $env:TEMP 'consoleenhancer_desinstalar.log'");
            ps.AppendLine("Add-Content $log ((Get-Date).ToString('s') + ' esperando a que se cierre ConsoleEnhancer (pid " + pid + ")')");
            ps.AppendLine("Wait-Process -Id " + pid + " -Timeout 120");
            ps.AppendLine("Start-Sleep -Seconds 2");
            ps.AppendLine("foreach ($n in @('mpv','Magpie','ConsoleEnhancerEngine')) { Get-Process -Name $n | Where-Object { $_.Path -like '" + app.Replace("'", "''") + "\\*' } | Stop-Process -Force }");
            foreach (string r in borrarRutas)
            {
                string q = r.Replace("'", "''");
                // (un enlace o union de carpeta, p. ej. bin\mpv -> terceros\mpv: solo el enlace, nunca lo de dentro)
                ps.AppendLine("$i = Get-Item -LiteralPath '" + q + "' -Force");
                ps.AppendLine("if ($i) { if ($i.Attributes -band [IO.FileAttributes]::ReparsePoint) { [IO.Directory]::Delete('" + q + "') } else { Remove-Item -LiteralPath '" + q + "' -Recurse -Force }; Add-Content $log ('borrado " + q + "') }");
            }
            // la carpeta solo si ha quedado vacia
            ps.AppendLine("if ((Test-Path -LiteralPath '" + app.Replace("'", "''") + "') -and -not (Get-ChildItem -LiteralPath '" + app.Replace("'", "''") + "' -Force)) { Remove-Item -LiteralPath '" + app.Replace("'", "''") + "' -Force; Add-Content $log 'carpeta vacia borrada' }");
            ps.AppendLine("Add-Content $log 'hecho'");
            ps.AppendLine("Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force");
            try
            {
                string f = Path.Combine(Path.GetTempPath(), "consoleenhancer_desinstalar_" + pid + ".ps1");
                File.WriteAllText(f, ps.ToString(), new UTF8Encoding(true));
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                                                            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + f + "\"");
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetTempPath();
                Process.Start(psi);
                Log.Write("Desinstalar: se cierra la app y se borran sus archivos (" + borrarRutas.Count + " rutas)");
            }
            catch (Exception ex) { Log.Error("Desinstalar: no se pudo preparar el borrado", ex); return false; }
            return true;
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
