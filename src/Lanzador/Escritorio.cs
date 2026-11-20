using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace ConsoleEnhancer
{
    // 19-11-2026 (ESCRITORIO): el escritorio de Windows negro mientras la app esta abierta, y el normal del usuario al cerrarla a mano.
    // Por que: la app se abre al iniciar sesion (inicio\PrismaInicio.exe) y su telon negro llega ~3 s tarde; con el escritorio negro de
    // fondo no se ve Windows al encender. Antes (19-11 por la manana) se dejo negro a mano y para siempre; ahora lo pone y lo quita la app.
    //   Negro  = color solido 0 0 0 sin imagen, Spotlight apagado, sin iconos del escritorio y la barra de tareas en ocultacion automatica.
    //   Normal = lo de juegos\escritorio_normal.txt (editable): se guarda la primera vez de inicio\fondo_antes.txt (los valores de antes del
    //            negro a mano) y despues del escritorio que haya al abrir la app si no es el negro (si el usuario cambia el fondo con la app
    //            cerrada, se apunta el nuevo). Nunca se guarda el negro como normal.
    // Cuando (ini escritorio_negro=1; 0 = no se toca nada):
    //   - Al abrir la app: negro (en otro hilo; si ya lo esta no hace nada).
    //   - Al cerrarla a mano (Alt+F4 sobre la imagen, Salir de la bandeja, un error, la consola apagada con apagar_pc=0...): el normal,
    //     al momento y sin reiniciar el Explorador (fondo con SPI_SETDESKWALLPAPER, iconos con el comando 0x7402 de SHELLDLL_DefView,
    //     barra con ABM_SETSTATE).
    //   - Al cerrarse por la consola apagada con apagar_pc=1 (el PC se apaga) o porque Windows se apaga / cierra sesion con la app abierta
    //     (SystemEvents.SessionEnding): se queda negro, para que el siguiente arranque tambien lo sea.
    //   - Si la app se cuelga y se mata, se queda negro; el siguiente cierre a mano lo devuelve igual (siempre aplica el normal), o a mano:
    //     Switch2Magpie.exe --restaurar-escritorio (y --escritorio-negro para lo contrario).
    //   - Un exe de prueba compilado en otra carpeta no lo toca (si se mata se quedaria negro). Prueba sin consola ni ventanas:
    //     --test-escritorio=<segundos>[;apagado] (abrir, esperar y cerrar a mano o por la consola apagada; no apaga nada).
    // Estado en juegos\escritorio_estado.txt ("negro"/"normal", hora y por que). Todo en switch2.log con "Escritorio:".
    // Spotlight: se pone su ultima foto al momento y se deja encendido en el registro, pero no vuelve a cambiar de foto hasta el
    // siguiente inicio de sesion (o reinicio del Explorador): sin reiniciar el Explorador no se ha encontrado como encenderlo en vivo.
    // ConsoleEnhancer (19-11, agente CE): APAGADO de serie (escritorio_negro=0); se ofrece junto a "Iniciar con Windows" (asistente y
    // bandeja: Cambiar). Sin inicio\fondo_antes.txt: el normal se guarda la primera vez que se pone negro (el escritorio de ese momento).
    // Si se cancela el aviso de apagar el PC, vuelve el normal (Cancelado). Comandos: ConsoleEnhancer.exe --restaurar-escritorio.
    static class Escritorio
    {
        static readonly object sync = new object();
        static bool activo;
        static volatile bool cerrando;
        static DateTime windowsCerrando = DateTime.MinValue;

        static string NormalFile { get { return Path.Combine(Paths.Datos, @"juegos\escritorio_normal.txt"); } }
        static string EstadoFile { get { return Path.Combine(Paths.Datos, @"juegos\escritorio_estado.txt"); } }
        static string FondoAntesFile { get { return Path.Combine(Paths.AppDir, @"inicio\fondo_antes.txt"); } }

        const string KDesktop = @"Control Panel\Desktop";
        const string KColors = @"Control Panel\Colors";
        const string KWallpapers = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";
        const string KSpotlight = @"Software\Microsoft\Windows\CurrentVersion\DesktopSpotlight\Settings";
        const string KCreatives = @"Software\Microsoft\Windows\CurrentVersion\DesktopSpotlight\Creatives";
        const string KAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string KStuck = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3";

        // Lo que se guarda y se pone
        sealed class Ajustes
        {
            public string Fondo = "";        // imagen (Control Panel\Desktop\Wallpaper); "" = color solido
            public string Estilo = "10";     // WallpaperStyle (10 = rellenar)
            public string Mosaico = "0";     // TileWallpaper
            public string Color = "0 0 0";   // Control Panel\Colors\Background
            public int Tipo = 1;             // Explorer\Wallpapers\BackgroundType: 0 imagen, 1 color solido, 2 presentacion, 3 Spotlight
            public bool Spotlight;           // DesktopSpotlight\Settings\EnabledState
            public bool Iconos = true;       // iconos del escritorio visibles
            public bool BarraOculta;         // barra de tareas en ocultacion automatica

            public bool PareceNegro { get { return Fondo.Length == 0 && !Spotlight && !Iconos; } }

            public string Texto()
            {
                return "fondo=" + Fondo + "\r\nestilo=" + Estilo + "\r\nmosaico=" + Mosaico + "\r\ncolor=" + Color +
                       "\r\ntipo_fondo=" + Tipo + "\r\nspotlight=" + (Spotlight ? 1 : 0) + "\r\niconos=" + (Iconos ? 1 : 0) +
                       "\r\nbarra_auto_ocultar=" + (BarraOculta ? 1 : 0) + "\r\n";
            }

            public string Resumen()
            {
                return (Spotlight ? "Spotlight" : Fondo.Length > 0 ? "imagen" : "color " + Color) +
                       (Fondo.Length > 0 ? " (" + Path.GetFileName(Fondo) + ")" : "") +
                       ", iconos " + (Iconos ? "si" : "no") + ", barra " + (BarraOculta ? "oculta" : "visible");
            }
        }

        // ---------------- Enganches de la app ----------------

        // Main, al empezar (ini escritorio_negro): el negro en otro hilo, y el aviso de Windows apagandose
        public static void AlAbrir(bool escritorioNegro)
        {
            if (!escritorioNegro) return;
            // Los exe de prueba compilados en otra carpeta (usan los datos de otra instalacion) no tocan el escritorio: si se matan, se
            // quedaria negro sin la app de verdad abierta
            string exeDir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath).TrimEnd('\\');
            if (!string.Equals(exeDir, Paths.AppDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("Escritorio: exe de prueba fuera de " + Paths.AppDir + ": el escritorio no se toca");
                return;
            }
            Abrir();
        }

        // (ConsoleEnhancer) desde la bandeja o el asistente con la app abierta: negro ya, o el normal ya (y no se toca mas)
        public static void Cambiar(bool negro)
        {
            if (negro) { if (!activo || cerrando) Abrir(); return; }
            if (!activo) return;
            AlCerrar(false);
            activo = false;
        }

        // (ConsoleEnhancer) el aviso de apagar el PC cancelado: el PC sigue encendido y la app cerrada -> el escritorio normal
        public static void Cancelado()
        {
            if (!activo) return;
            ConTiempo(delegate { Restaurar("apagado del PC cancelado"); }, 10000, "el escritorio normal");
            activo = false;
        }

        static void Abrir()
        {
            activo = true;
            cerrando = false;
            try { SystemEvents.SessionEnding += AlApagarWindows; }
            catch (Exception ex) { Log.Error("Escritorio: sin aviso del apagado de Windows", ex); }
            Thread t = new Thread(delegate()
            {
                try
                {
                    // Recien iniciada la sesion puede no estar aun el Explorador: se espera hasta 30 s a la barra y el escritorio
                    for (int i = 0; i < 60 && !cerrando && !ExploradorListo(); i++) Thread.Sleep(500);
                    if (!cerrando) PonerNegro("app abierta");
                }
                catch (Exception ex) { Log.Error("Escritorio: poniendo el negro", ex); }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Name = "Escritorio";
            t.Start();
        }

        // Main, al salir de Application.Run (siempre: tambien si ha fallado). apagaPc = la consola apagada y apagar_pc=1
        public static void AlCerrar(bool apagaPc)
        {
            if (!activo) return;
            cerrando = true;
            try { SystemEvents.SessionEnding -= AlApagarWindows; } catch { }
            bool windows = (DateTime.Now - windowsCerrando).TotalSeconds < 120;
            if (apagaPc || windows)
                ConTiempo(delegate { PonerNegro(apagaPc ? "se apaga el PC (consola apagada)" : "Windows se apaga o cierra sesion"); }, 5000, "el negro al apagar");
            else
                ConTiempo(delegate { Restaurar("app cerrada a mano"); }, 10000, "el escritorio normal");
        }

        static void AlApagarWindows(object sender, SessionEndingEventArgs e)
        {
            windowsCerrando = DateTime.Now;
            Log.Write("Escritorio: Windows se " + (e.Reason == SessionEndReasons.Logoff ? "cierra la sesion" : "apaga") + " con la app abierta: se queda negro");
            ConTiempo(delegate { PonerNegro("Windows se apaga o cierra sesion"); }, 3000, "el negro al apagar Windows");
        }

        // Prueba sin consola (--test-escritorio=<segundos>[;apagado]): lo mismo que abrir la app, esperar y cerrarla a mano (o por la
        // consola apagada con apagar_pc=1, sin apagar nada). Tambien desde un exe de prueba
        public static int Prueba(string arg)
        {
            string[] p = arg.Split(';');
            int s;
            if (!int.TryParse(p[0], out s)) s = 5;
            bool apagado = p.Length > 1 && p[1] == "apagado";
            Log.Write("Escritorio: PRUEBA " + s + " s abierta y cierre " + (apagado ? "por la consola apagada (apagar_pc=1, sin apagar)" : "a mano"));
            Abrir();
            // (con los mensajes de Windows: un WM_QUERYENDSESSION mandado a mano hace de apagado de Windows)
            DateTime fin = DateTime.Now.AddSeconds(s);
            while (DateTime.Now < fin) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(20); }
            AlCerrar(apagado);
            Log.Write("Escritorio: PRUEBA terminada (estado " + LeerEstado() + ")");
            return 0;
        }

        // --restaurar-escritorio / --escritorio-negro (sin abrir la app; no mira el ini)
        public static int AMano(bool negro)
        {
            try
            {
                if (negro) PonerNegro("a mano (--escritorio-negro)");
                else Restaurar("a mano (--restaurar-escritorio)");
                return 0;
            }
            catch (Exception ex) { Log.Error("Escritorio: a mano", ex); return 1; }
        }

        // ---------------- Negro / normal ----------------

        static void PonerNegro(string porque)
        {
            lock (sync)
            {
                Ajustes ahora = Leer();
                string estado = LeerEstado();
                Ajustes normal = Cargar();
                if (estado != "negro" && !ahora.PareceNegro)
                {
                    // El escritorio de ahora es el normal del usuario: se apunta si ha cambiado (nunca el negro)
                    if (normal == null || normal.Texto() != ahora.Texto())
                    {
                        Guardar(ahora, "el escritorio de antes de abrir la app");
                        Log.Write("Escritorio: guardado el normal (" + ahora.Resumen() + ")");
                    }
                }
                else if (normal == null) AsegurarNormal();

                Ajustes negro = new Ajustes();
                negro.Fondo = ""; negro.Estilo = ahora.Estilo; negro.Mosaico = ahora.Mosaico; negro.Color = "0 0 0";
                negro.Tipo = 1; negro.Spotlight = false; negro.Iconos = false; negro.BarraOculta = true;
                string hecho = Aplicar(negro, false);
                EscribirEstado("negro", porque);
                Log.Write("Escritorio: negro (" + porque + ")" + (hecho.Length > 0 ? ": " + hecho : ": ya lo estaba"));
            }
        }

        static void Restaurar(string porque)
        {
            lock (sync)
            {
                Ajustes normal = AsegurarNormal();
                string hecho = Aplicar(normal, true);
                EscribirEstado("normal", porque);
                Log.Write("Escritorio: normal (" + porque + "; " + normal.Resumen() + ")" + (hecho.Length > 0 ? ": " + hecho : ""));
            }
        }

        // El normal guardado; si no lo hay, de inicio\fondo_antes.txt o del escritorio de ahora (si no es el negro)
        static Ajustes AsegurarNormal()
        {
            Ajustes n = Cargar();
            if (n != null) return n;
            Ajustes ahora = Leer();
            string de;
            n = DeFondoAntes(ahora);
            if (n != null) de = FondoAntesFile;
            else if (!ahora.PareceNegro && LeerEstado() != "negro") { n = ahora; de = "el escritorio de ahora"; }
            else
            {
                // Sin nada guardado y ya en negro: lo de serie de Windows (imagen de serie, iconos, barra visible)
                n = new Ajustes();
                n.Fondo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Web\Wallpaper\Windows\img0.jpg");
                if (!File.Exists(n.Fondo)) n.Fondo = "";
                n.Tipo = n.Fondo.Length > 0 ? 0 : 1;
                n.Color = "0 0 0"; n.Iconos = true; n.BarraOculta = false;
                de = "lo de serie de Windows (no habia nada guardado)";
            }
            Guardar(n, de);
            Log.Write("Escritorio: guardado el normal de " + de + " (" + n.Resumen() + ")");
            return n;
        }

        // Pone los ajustes. forzar = el fondo aunque el registro ya diga lo mismo (al restaurar). Devuelve lo que ha cambiado
        static string Aplicar(Ajustes a, bool forzar)
        {
            List<string> hecho = new List<string>();

            // Spotlight apagado antes de quitar la imagen (si no, la vuelve a poner)
            if (!a.Spotlight && RegInt(KSpotlight, "EnabledState", 0) != 0) { RegSetInt(KSpotlight, "EnabledState", 0); hecho.Add("Spotlight apagado"); }

            // Color de fondo
            int rgb = ParseRgb(a.Color);
            if (RegStr(KColors, "Background", "") != a.Color) RegSetStr(KColors, "Background", a.Color);
            if ((GetSysColor(COLOR_BACKGROUND) & 0xFFFFFF) != rgb)
            {
                SetSysColors(1, new int[] { COLOR_BACKGROUND }, new int[] { rgb });
                hecho.Add("color " + a.Color);
            }

            // Imagen (con Spotlight: la ultima foto suya, la guardada si sigue existiendo)
            string fondo = a.Fondo ?? "";
            if (a.Spotlight && (fondo.Length == 0 || !File.Exists(fondo)))
            {
                string f = FotoSpotlight();
                if (f != null) fondo = f;
            }
            if (fondo.Length > 0 && !File.Exists(fondo))
            {
                Log.Write("Escritorio: no existe la imagen de fondo " + fondo + ": color solido");
                fondo = "";
            }
            if (RegStr(KDesktop, "WallpaperStyle", "") != a.Estilo) RegSetStr(KDesktop, "WallpaperStyle", a.Estilo);
            if (RegStr(KDesktop, "TileWallpaper", "") != a.Mosaico) RegSetStr(KDesktop, "TileWallpaper", a.Mosaico);
            if (forzar || !string.Equals(RegStr(KDesktop, "Wallpaper", ""), fondo, StringComparison.OrdinalIgnoreCase))
            {
                bool ok = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, fondo, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
                hecho.Add((fondo.Length > 0 ? "imagen " + Path.GetFileName(fondo) : "sin imagen") + (ok ? "" : " (FALLO " + Marshal.GetLastWin32Error() + ")"));
            }
            int tipo = fondo.Length == 0 ? 1 : a.Tipo == 1 ? 0 : a.Tipo;
            if (RegInt(KWallpapers, "BackgroundType", -1) != tipo) RegSetInt(KWallpapers, "BackgroundType", tipo);
            if (a.Spotlight && RegInt(KSpotlight, "EnabledState", 0) != 1) { RegSetInt(KSpotlight, "EnabledState", 1); hecho.Add("Spotlight encendido"); }

            string r = PonerIconos(a.Iconos);
            if (r != null) hecho.Add(r);
            r = PonerBarra(a.BarraOculta);
            if (r != null) hecho.Add(r);
            return string.Join(", ", hecho.ToArray());
        }

        // ---------------- Iconos del escritorio ----------------

        // La vista del escritorio (SHELLDLL_DefView, en Progman o en un WorkerW) y su lista de iconos
        static IntPtr VistaEscritorio(out IntPtr lista)
        {
            lista = IntPtr.Zero;
            IntPtr vista = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr w = IntPtr.Zero;
            while (vista == IntPtr.Zero && (w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
                vista = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (vista != IntPtr.Zero) lista = FindWindowEx(vista, IntPtr.Zero, "SysListView32", null);
            return vista;
        }

        static bool ExploradorListo()
        {
            IntPtr lista;
            return FindWindow("Shell_TrayWnd", null) != IntPtr.Zero && VistaEscritorio(out lista) != IntPtr.Zero && lista != IntPtr.Zero;
        }

        static string PonerIconos(bool ver)
        {
            IntPtr lista, vista = VistaEscritorio(out lista);
            if (vista == IntPtr.Zero || lista == IntPtr.Zero)
            {
                // Sin Explorador: en el registro (lo lee al arrancar)
                int h = ver ? 0 : 1;
                if (RegInt(KAdvanced, "HideIcons", 0) == h) return null;
                RegSetInt(KAdvanced, "HideIcons", h);
                return "iconos " + (ver ? "visibles" : "ocultos") + " (registro, sin Explorador)";
            }
            if (IsWindowVisible(lista) == ver)
            {
                if (RegInt(KAdvanced, "HideIcons", 0) != (ver ? 0 : 1)) RegSetInt(KAdvanced, "HideIcons", ver ? 0 : 1);
                return null;
            }
            // El mismo comando que "Ver > Mostrar iconos del escritorio" (lo cambia y lo guarda el Explorador)
            IntPtr res;
            SendMessageTimeout(vista, WM_COMMAND, (IntPtr)0x7402, IntPtr.Zero, SMTO_ABORTIFHUNG, 3000, out res);
            for (int i = 0; i < 20 && IsWindowVisible(lista) != ver; i++) Thread.Sleep(50);
            bool ok = IsWindowVisible(lista) == ver;
            if (RegInt(KAdvanced, "HideIcons", 0) != (ver ? 0 : 1)) RegSetInt(KAdvanced, "HideIcons", ver ? 0 : 1);
            return "iconos " + (ver ? "visibles" : "ocultos") + (ok ? "" : " (el Explorador no ha respondido)");
        }

        // ---------------- Barra de tareas ----------------

        static int EstadoBarra(IntPtr barra)
        {
            APPBARDATA d = new APPBARDATA();
            d.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            d.hWnd = barra;
            return (int)SHAppBarMessage(ABM_GETSTATE, ref d);
        }

        static string PonerBarra(bool oculta)
        {
            IntPtr barra = FindWindow("Shell_TrayWnd", null);
            if (barra == IntPtr.Zero)
            {
                // Sin Explorador: el byte 8 de StuckRects3 (bit 0 = ocultacion automatica), lo lee al arrancar
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KStuck, true))
                    {
                        byte[] v = k == null ? null : k.GetValue("Settings") as byte[];
                        if (v == null || v.Length <= 8 || ((v[8] & 1) != 0) == oculta) return null;
                        v[8] = (byte)(oculta ? v[8] | 1 : v[8] & ~1);
                        k.SetValue("Settings", v, RegistryValueKind.Binary);
                    }
                    return "barra " + (oculta ? "oculta" : "visible") + " (registro, sin Explorador)";
                }
                catch (Exception ex) { return "barra: " + ex.Message; }
            }
            int st = EstadoBarra(barra);
            if (((st & ABS_AUTOHIDE) != 0) == oculta) return null;
            APPBARDATA d = new APPBARDATA();
            d.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            d.hWnd = barra;
            d.lParam = (IntPtr)((oculta ? ABS_AUTOHIDE : 0) | ABS_ALWAYSONTOP);
            SHAppBarMessage(ABM_SETSTATE, ref d);
            bool ok = ((EstadoBarra(barra) & ABS_AUTOHIDE) != 0) == oculta;
            return "barra " + (oculta ? "oculta" : "visible") + (ok ? "" : " (no ha cambiado)");
        }

        // ---------------- Leer / guardar ----------------

        static Ajustes Leer()
        {
            Ajustes a = new Ajustes();
            a.Fondo = RegStr(KDesktop, "Wallpaper", "");
            a.Estilo = RegStr(KDesktop, "WallpaperStyle", "10");
            a.Mosaico = RegStr(KDesktop, "TileWallpaper", "0");
            a.Color = RegStr(KColors, "Background", "0 0 0");
            a.Spotlight = RegInt(KSpotlight, "EnabledState", 0) == 1;
            a.Tipo = RegInt(KWallpapers, "BackgroundType", a.Spotlight ? 3 : a.Fondo.Length > 0 ? 0 : 1);
            IntPtr lista;
            if (VistaEscritorio(out lista) != IntPtr.Zero && lista != IntPtr.Zero) a.Iconos = IsWindowVisible(lista);
            else a.Iconos = RegInt(KAdvanced, "HideIcons", 0) == 0;
            IntPtr barra = FindWindow("Shell_TrayWnd", null);
            if (barra != IntPtr.Zero) a.BarraOculta = (EstadoBarra(barra) & ABS_AUTOHIDE) != 0;
            else
            {
                byte[] v = null;
                try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KStuck)) if (k != null) v = k.GetValue("Settings") as byte[]; }
                catch { }
                a.BarraOculta = v != null && v.Length > 8 && (v[8] & 1) != 0;
            }
            return a;
        }

        static Ajustes Cargar()
        {
            try
            {
                if (!File.Exists(NormalFile)) return null;
                Ajustes a = new Ajustes();
                foreach (string raw in File.ReadAllLines(NormalFile))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith("#")) continue;
                    int eq = l.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = l.Substring(0, eq).Trim().ToLowerInvariant(), v = l.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "fondo": a.Fondo = v; break;
                        case "estilo": a.Estilo = v; break;
                        case "mosaico": a.Mosaico = v; break;
                        case "color": a.Color = v; break;
                        case "tipo_fondo": int.TryParse(v, out a.Tipo); break;
                        case "spotlight": a.Spotlight = v == "1"; break;
                        case "iconos": a.Iconos = v != "0"; break;
                        case "barra_auto_ocultar": a.BarraOculta = v == "1"; break;
                    }
                }
                return a;
            }
            catch (Exception ex) { Log.Error("Escritorio: leyendo " + NormalFile, ex); return null; }
        }

        static void Guardar(Ajustes a, string de)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(NormalFile));
                File.WriteAllText(NormalFile,
                    "# El escritorio normal (el que deja la app al cerrarla a mano; mientras esta abierta, negro). Se puede editar.\r\n" +
                    "# Guardado " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " de " + de + ".\r\n" +
                    "# tipo_fondo: 0 imagen, 1 color solido, 2 presentacion, 3 Spotlight; color = R G B; barra_auto_ocultar: 1 = la barra de\r\n" +
                    "# tareas se oculta sola (asi estaba antes del 19-11), 0 = siempre visible\r\n" + a.Texto(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Error("Escritorio: guardando " + NormalFile, ex); }
        }

        // inicio\fondo_antes.txt (el negro puesto a mano el 19-11): "Antes: Wallpaper=X | Background=R G B | HideIcons=N",
        // "Barra antes: N" (byte 8 de StuckRects3) y "Spotlight antes: EnabledState=N"
        static Ajustes DeFondoAntes(Ajustes ahora)
        {
            try
            {
                if (!File.Exists(FondoAntesFile)) return null;
                string t = File.ReadAllText(FondoAntesFile);
                Match m = Regex.Match(t, @"Wallpaper=(.*?)\s*\|\s*Background=(.*?)\s*\|\s*HideIcons=(\d)");
                if (!m.Success) return null;
                Ajustes a = new Ajustes();
                a.Fondo = m.Groups[1].Value.Trim();
                a.Color = m.Groups[2].Value.Trim();
                a.Iconos = m.Groups[3].Value != "1";
                a.Estilo = ahora.Estilo;
                a.Mosaico = ahora.Mosaico;
                Match b = Regex.Match(t, @"Barra antes:\s*(\d+)");
                a.BarraOculta = b.Success ? (int.Parse(b.Groups[1].Value) & 1) != 0 : false;
                Match s = Regex.Match(t, @"Spotlight antes:\s*EnabledState=(\d+)");
                a.Spotlight = s.Success && s.Groups[1].Value == "1";
                a.Tipo = a.Spotlight ? 3 : a.Fondo.Length > 0 ? 0 : 1;
                return a;
            }
            catch (Exception ex) { Log.Error("Escritorio: leyendo " + FondoAntesFile, ex); return null; }
        }

        // La primera foto de Spotlight que siga en disco (DesktopSpotlight\Creatives)
        static string FotoSpotlight()
        {
            try
            {
                string c = RegStr(KCreatives, "Creatives", "");
                foreach (Match m in Regex.Matches(c, "\"landscapeImage\":\\{\"asset\":\"((?:[^\"\\\\]|\\\\.)*)\""))
                {
                    string f = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (File.Exists(f)) return f;
                }
            }
            catch { }
            return null;
        }

        static string LeerEstado()
        {
            try
            {
                if (!File.Exists(EstadoFile)) return null;
                string t = File.ReadAllText(EstadoFile).Trim();
                int sp = t.IndexOfAny(new char[] { ' ', '\r', '\n' });
                return (sp > 0 ? t.Substring(0, sp) : t).ToLowerInvariant();
            }
            catch { return null; }
        }

        static void EscribirEstado(string estado, string porque)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(EstadoFile));
                File.WriteAllText(EstadoFile, estado + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + porque + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------------- Utilidades ----------------

        // En otro hilo con un limite (SPI_SETDESKWALLPAPER avisa a todas las ventanas: una colgada no debe parar la salida)
        static void ConTiempo(ThreadStart f, int ms, string que)
        {
            Thread t = new Thread(delegate()
            {
                try { f(); }
                catch (Exception ex) { Log.Error("Escritorio: " + que, ex); }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            if (!t.Join(ms)) Log.Write("Escritorio: " + que + " sin terminar en " + ms + " ms");
        }

        static int ParseRgb(string s)
        {
            string[] p = (s ?? "").Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            int r = 0, g = 0, b = 0;
            if (p.Length == 3) { int.TryParse(p[0], out r); int.TryParse(p[1], out g); int.TryParse(p[2], out b); }
            return (r & 255) | ((g & 255) << 8) | ((b & 255) << 16);
        }

        static string RegStr(string key, string name, string def)
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key)) { object v = k == null ? null : k.GetValue(name); return v == null ? def : v.ToString(); } }
            catch { return def; }
        }

        static int RegInt(string key, string name, int def)
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key)) { object v = k == null ? null : k.GetValue(name); return v is int ? (int)v : def; } }
            catch { return def; }
        }

        static void RegSetStr(string key, string name, string v)
        {
            try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(key)) k.SetValue(name, v, RegistryValueKind.String); }
            catch (Exception ex) { Log.Error("Escritorio: " + key + "\\" + name, ex); }
        }

        static void RegSetInt(string key, string name, int v)
        {
            try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(key)) k.SetValue(name, v, RegistryValueKind.DWord); }
            catch (Exception ex) { Log.Error("Escritorio: " + key + "\\" + name, ex); }
        }

        const int SPI_SETDESKWALLPAPER = 0x0014, SPIF_UPDATEINIFILE = 0x01, SPIF_SENDWININICHANGE = 0x02;
        const int COLOR_BACKGROUND = 1;
        const int WM_COMMAND = 0x0111;
        const uint SMTO_ABORTIFHUNG = 0x0002;
        const uint ABM_GETSTATE = 0x04, ABM_SETSTATE = 0x0A;
        const int ABS_AUTOHIDE = 0x01, ABS_ALWAYSONTOP = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public int left, top, right, bottom;
            public IntPtr lParam;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SystemParametersInfo(int action, int param, string vparam, int winIni);
        [DllImport("user32.dll")] static extern bool SetSysColors(int n, int[] elements, int[] colors);
        [DllImport("user32.dll")] static extern int GetSysColor(int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    }
}
