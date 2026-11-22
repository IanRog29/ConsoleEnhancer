// RutaReal.cs - 22-11-2026 (1.2.2): la ruta de verdad de un fichero, con las uniones de carpeta y los enlaces resueltos.
//
// Magpie elige el perfil de una ventana comparando su pathRule con la ruta del exe del proceso, y Windows da esa ruta YA resuelta
// (la del volumen de verdad, sin uniones ni enlaces simbolicos). Con la app en una carpeta que pasa por una union o un enlace (una
// carpeta movida con mklink /J, una biblioteca redirigida, las pruebas con bin\mpv -> terceros\mpv), el pathRule con la ruta "de
// fuera" no coincidia: Magpie usaba su perfil de serie (base de 60 en vez de 29,97, sin VRR, sin escalado automatico: la app esperaba
// 8 s y pulsaba el atajo). Visto el 22-11 por los agentes MULTI y MENU en sus pruebas. Con la ruta resuelta coincide siempre.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleEnhancer
{
    static class RutaReal
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFinalPathNameByHandleW(IntPtr file, StringBuilder path, uint len, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);

        const uint FILE_SHARE_ALL = 0x7;                  // lectura, escritura y borrado: no molesta a nadie que lo tenga abierto
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000; // (tambien vale para carpetas)
        static readonly IntPtr INVALID = new IntPtr(-1);

        // La ruta resuelta (C:\..., \\servidor\recurso\...) o la misma si no se puede (no existe, sin permiso...)
        public static string De(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            IntPtr h = INVALID;
            try
            {
                h = CreateFileW(path, 0, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
                if (h == INVALID || h == IntPtr.Zero) return path;
                StringBuilder sb = new StringBuilder(1024);
                uint n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0); // FILE_NAME_NORMALIZED | VOLUME_NAME_DOS
                if (n == 0 || n >= sb.Capacity) return path;
                string r = sb.ToString();
                if (r.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) r = @"\\" + r.Substring(8);
                else if (r.StartsWith(@"\\?\", StringComparison.Ordinal)) r = r.Substring(4);
                return File.Exists(r) || Directory.Exists(r) ? r : path;
            }
            catch { return path; }
            finally { if (h != INVALID && h != IntPtr.Zero) CloseHandle(h); }
        }
    }
}
