// DefenderAviso.cs - 1.2.08 (21-11-2026): si Microsoft Defender ha cerrado o puesto en cuarentena esta copia de la app, explicarlo al
// abrirla. El 21-11 Defender (Behavior:Win32/Persistence.A!ml, deteccion por comportamiento) cerro ConsoleEnhancer.exe recien
// descomprimido en Descargas al crear la tarea programada de inicio: la imagen seguia (mpv + Magpie) pero sin sonido ni mando, y el exe
// desaparecia. Desde la 1.2.08 el inicio con Windows es un acceso directo (InicioWindows.cs).
//  - Al abrir: el registro "Microsoft-Windows-Windows Defender/Operational" (lo lee cualquier usuario), sucesos 1116/1117 de los
//    ultimos 30 dias que nombran la carpeta de la app; si hay uno mas nuevo que defender_aviso= del ini, un aviso (una vez por suceso).
//  - Bandeja > "Microsoft Defender: excepción para la app...": el mismo aviso cuando se quiera.
//  - La app NO toca Defender: abre Seguridad de Windows (windowsdefender://threatsettings) con la carpeta copiada al portapapeles y
//    la excepcion la pone el usuario si quiere.
using System;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ConsoleEnhancer
{
    static class CeDefender
    {
        // El suceso de Defender mas nuevo con esta carpeta (o DateTime.MinValue); amenaza = su nombre
        public static DateTime UltimaDeteccion(out string amenaza)
        {
            amenaza = "";
            DateTime mejor = DateTime.MinValue;
            try
            {
                string dir = Paths.AppDir.TrimEnd('\\');
                string q = "*[System[(EventID=1116 or EventID=1117) and TimeCreated[timediff(@SystemTime) <= 2592000000]]]";
                EventLogQuery query = new EventLogQuery("Microsoft-Windows-Windows Defender/Operational", PathType.LogName, q);
                using (EventLogReader r = new EventLogReader(query))
                {
                    for (EventRecord e = r.ReadEvent(); e != null; e = r.ReadEvent())
                        using (e)
                        {
                            string x = e.ToXml();
                            if (x.IndexOf(dir, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            DateTime t = e.TimeCreated ?? DateTime.MinValue;
                            if (t <= mejor) continue;
                            mejor = t;
                            Match m = Regex.Match(x, "<Data Name=['\"]Threat Name['\"]>([^<]*)</Data>");
                            amenaza = m.Success ? m.Groups[1].Value : "";
                        }
                }
            }
            catch (Exception ex) { Log.Write("Microsoft Defender: no se pudo leer su registro (" + ex.Message + ")"); }
            return mejor;
        }

        // Main, antes de abrir la imagen
        public static void AlAbrir()
        {
            string amenaza;
            DateTime t = UltimaDeteccion(out amenaza);
            if (t == DateTime.MinValue) return;
            long visto;
            string v = Ce.IniLeer("defender_aviso");
            if (v != null && long.TryParse(v, out visto) && visto >= t.Ticks) return;
            Log.Write("Microsoft Defender cerro o puso en cuarentena la app el " + t.ToString("dd-MM HH:mm:ss") + " (" + amenaza + "): se avisa");
            Ce.IniPoner("defender_aviso", t.Ticks.ToString());
            Mostrar(t, amenaza);
        }

        public static void Mostrar(DateTime t, string amenaza)
        {
            string dir = Paths.AppDir.TrimEnd('\\');
            string txt = t != DateTime.MinValue
                ? string.Format(Deteccion.Tx.T(
                    "Microsoft Defender cerró ConsoleEnhancer el {0} ({1}). Por eso pudo quedarse la imagen sin sonido y sin respuesta del mando, o desaparecer ConsoleEnhancer.exe.\r\n\r\n" +
                    "Es un falso positivo de su detección por comportamiento: las versiones anteriores a la 1.2.08 creaban una tarea programada para iniciar con Windows y Defender la toma por un virus. Esta versión usa un acceso directo en la carpeta Inicio, como los programas normales.\r\n\r\n",
                    "Microsoft Defender closed ConsoleEnhancer on {0} ({1}). That is why the picture may have stayed with no sound and the controller not responding, or ConsoleEnhancer.exe disappeared.\r\n\r\n" +
                    "It is a false positive of its behaviour detection: versions before 1.2.08 created a scheduled task to start with Windows and Defender takes it for malware. This version uses a shortcut in the Startup folder, like normal programs.\r\n\r\n"),
                    t.ToString("dd-MM HH:mm"), amenaza.Length > 0 ? amenaza : "Defender")
                : "";
            txt += string.Format(Deteccion.Tx.T(
                "Si vuelve a pasar, puedes añadir una exclusión solo para la carpeta de la app:\r\n{0}\r\n\r\n" +
                "Sí = abrir Seguridad de Windows (la carpeta queda copiada): Configuración de antivirus y protección contra amenazas > Administrar la configuración > Exclusiones > Agregar una exclusión > Carpeta, y pegarla. Para recuperar el exe: Historial de protección > Restaurar.\r\n\r\n¿Abrir Seguridad de Windows?",
                "If it happens again you can add an exclusion for the app folder only:\r\n{0}\r\n\r\n" +
                "Yes = open Windows Security (the folder is copied): Virus & threat protection settings > Manage settings > Exclusions > Add an exclusion > Folder, and paste it. To get the exe back: Protection history > Restore.\r\n\r\nOpen Windows Security?"), dir);
            if (MessageBox.Show(txt, "ConsoleEnhancer - Microsoft Defender", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { Clipboard.SetText(dir); } catch { }
            try { Process.Start("windowsdefender://threatsettings"); Log.Write("Microsoft Defender: abierta Seguridad de Windows (exclusiones) con la carpeta copiada"); }
            catch (Exception ex) { Log.Error("Microsoft Defender: abrir Seguridad de Windows", ex); }
        }
    }

    partial class TrayApp
    {
        void MenuDefender(ContextMenuStrip menu)
        {
            menu.Items.Add(new ToolStripMenuItem(Deteccion.Tx.T("Microsoft Defender: excepción para la app...", "Microsoft Defender: exclusion for the app..."), null, delegate
            {
                string amenaza;
                DateTime t = CeDefender.UltimaDeteccion(out amenaza);
                CeDefender.Mostrar(t, amenaza);
            }));
        }
    }
}
