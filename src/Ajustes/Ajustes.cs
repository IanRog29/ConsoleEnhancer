// Ajustes.cs - "ConsoleEnhancer Settings.exe" (1.2.0): abre el panel de ajustes de la app (ConsoleEnhancer.exe --ajustes, junto a
// este exe): como se usa el PC, abrir con Windows, que hace el PC al apagar la consola, DLSS 5, componentes, registros y desinstalar.
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("ConsoleEnhancer settings")]
[assembly: System.Reflection.AssemblyProduct("ConsoleEnhancer")]

static class AjustesCe
{
    [STAThread]
    static int Main()
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(System.Reflection.Assembly.GetEntryAssembly().Location));
        string exe = Path.Combine(dir, "ConsoleEnhancer.exe");
        if (!File.Exists(exe))
        {
            MessageBox.Show("ConsoleEnhancer.exe no esta en esta carpeta / is not in this folder:\n" + dir, "ConsoleEnhancer",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        Process.Start(new ProcessStartInfo(exe, "--ajustes") { UseShellExecute = false, WorkingDirectory = dir });
        return 0;
    }
}
