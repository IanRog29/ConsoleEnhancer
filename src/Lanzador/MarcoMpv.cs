// MarcoMpv.cs - 22-11-2026 (1.2.1, el usuario: "intenta reducir los errores visuales del fg y de los upscalers").
// La ventana de mpv sin los estilos de titulo y de marco, para que Magpie capture la imagen ENTERA.
//
// mpv con --no-border deja su ventana con WS_CAPTION y WS_THICKFRAME (estilo 0x14CE0000; su WM_NCCALCSIZE hace que el area de cliente
// ocupe la ventana entera). Magpie toma esa ventana por una con borde de Windows 11 y le recorta el borde escalado por los ppp: con la
// tele a 300 % (288 ppp), 3 px por lado. Medido (data\logs de Magpie, partida del usuario 21-11 19:12 y prueba sin capturadora 22-11):
// ventana de 2560x1440 -> WGC "surface=2557x1437" -> origen 2554x1434 -> DLSS SR 2554x1434 -> 3840x2156 (x1,5035 en vez de x1,5) y
// "目标矩形 0,2,3840,2158": 2 px de franja negra arriba y abajo y 6 px de imagen menos; con la captura de 4K, 3834x2154 con un marco negro
// de 3 px. Quitando WS_CAPTION | WS_THICKFRAME (mismo programa, misma prueba): "surface=2560x1440", DLSS SR 2560x1440 -> 3840x2160 y la
// imagen a pantalla completa. Nada mas cambia: mpv sigue sin borde (su area de cliente ya era la ventana entera), con WS_SYSMENU y
// WS_MINIMIZEBOX (cerrar y minimizar desde la barra de tareas siguen igual). Solo mpv (el motor propio tiene su ventana y no usa Magpie).
// ini mpv_sin_marco=1 (de serie); 0 = como antes.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConsoleEnhancer
{
    static class MarcoMpv
    {
        public static volatile bool Activo = true;   // ini mpv_sin_marco

        const int GWL_STYLE = -16;
        const int WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
        const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020,
                   SWP_NOOWNERZORDER = 0x0200, SWP_ASYNCWINDOWPOS = 0x4000;

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);

        static IntPtr avisada;   // (el log una vez por ventana)

        public static bool Parse(string key, string val)
        {
            if (key != "mpv_sin_marco") return false;
            string v = val.Trim().ToLowerInvariant();
            Activo = !(v == "0" || v == "no" || v == "false" || v == "off");
            return true;
        }

        // Quita los dos estilos si los tiene (no hace nada si ya no estan). true = los ha quitado ahora
        public static bool Quitar(IntPtr h, string cuando)
        {
            try
            {
                if (!Activo || h == IntPtr.Zero || !IsWindow(h)) return false;
                int st = GetWindowLong(h, GWL_STYLE);
                if ((st & (WS_CAPTION | WS_THICKFRAME)) == 0) return false;
                SetWindowLong(h, GWL_STYLE, st & ~(WS_CAPTION | WS_THICKFRAME));
                // (asincrono: si el hilo de la ventana de mpv esta ocupado, la app no se queda esperando)
                SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED |
                                                         SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
                if (h != avisada)
                {
                    avisada = h;
                    Log.Write("Ventana de mpv sin titulo ni marco (" + cuando + "): Magpie captura la imagen entera, sin recortar el borde " +
                              "(antes 3 px por lado a 300 % y una franja negra; mpv_sin_marco=0 = como antes)");
                }
                return true;
            }
            catch (Exception ex) { Log.Error("Ventana de mpv sin marco", ex); return false; }
        }

        // Para cada mpv que se lanza: en cuanto tiene ventana se le quitan (antes de que Magpie empiece a escalar) y luego se mira cada
        // segundo mientras vive, por si mpv los vuelve a poner (no se ha visto: con el cambio de ventana HOME <-> juego siguen quitados)
        public static void Vigilar(Process p)
        {
            if (!Activo || p == null) return;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    IntPtr h = IntPtr.Zero;
                    Stopwatch sw = Stopwatch.StartNew();
                    while (h == IntPtr.Zero && sw.ElapsedMilliseconds < 15000 && !p.HasExited)
                    {
                        p.Refresh();
                        h = p.MainWindowHandle;
                        if (h == IntPtr.Zero) Thread.Sleep(10);
                    }
                    if (h == IntPtr.Zero) return;
                    Quitar(h, "al abrirse, a los " + sw.ElapsedMilliseconds + " ms");
                    while (!p.HasExited && IsWindow(h))
                    {
                        Thread.Sleep(1000);
                        Quitar(h, "otra vez");
                    }
                }
                catch (InvalidOperationException) { }   // (ese mpv ya se ha cerrado)
                catch (Exception ex) { Log.Error("Vigilando el marco de mpv", ex); }
            });
            t.IsBackground = true;
            t.Name = "MarcoMpv";
            t.Start();
        }
    }
}
